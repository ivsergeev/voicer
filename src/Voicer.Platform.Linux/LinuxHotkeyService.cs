using System.Runtime.InteropServices;
using Serilog;
using Voicer.Core.Interfaces;
using Voicer.Core.Models;

namespace Voicer.Platform.Linux;

/// <summary>
/// Linux global hotkey service via X11 XGrabKey.
/// Requires X11 display server. For Wayland, consider running with XWayland.
/// </summary>
public class LinuxHotkeyService : IHotkeyService
{
    private const string LibX11 = "libX11.so.6";

    private const int KeyPress = 2;
    private const int KeyRelease = 3;
    private const int GrabModeAsync = 1;

    // X11 modifier masks
    private const uint ShiftMask = 0x01;
    private const uint LockMask = 0x02;    // CapsLock
    private const uint ControlMask = 0x04;
    private const uint Mod1Mask = 0x08;    // Alt
    private const uint Mod2Mask = 0x10;    // NumLock
    private const uint Mod4Mask = 0x40;    // Super/Win

    // MOD_* constants (Win32 canonical format)
    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int MOD_WIN = 0x0008;

    // Mask to ignore CapsLock and NumLock state when comparing
    private const uint IgnoreMask = LockMask | Mod2Mask;

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(string? display_name);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XKeysymToKeycode(IntPtr display, uint keysym);

    [DllImport(LibX11)]
    private static extern int XGrabKey(IntPtr display, int keycode, uint modifiers,
        IntPtr grab_window, int owner_events, int pointer_mode, int keyboard_mode);

    [DllImport(LibX11)]
    private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grab_window);

    [DllImport(LibX11)]
    private static extern int XNextEvent(IntPtr display, IntPtr event_return);

    [DllImport(LibX11)]
    private static extern int XFlush(IntPtr display);

    // XEvent offsets on x86_64 Linux
    private const int XKeyEventKeycodeOffset = 84;
    private const int XKeyEventStateOffset = 80;

    private class WsHotkeySlot
    {
        public int X11Keycode;
        public uint X11Mod;
        public bool IsDown;
    }

    private IntPtr _display;
    private IntPtr _rootWindow;

    // Insert hotkey
    private int _insertKeycode;
    private uint _insertX11Mod;
    private bool _isInsertDown;

    // Dynamic WS hotkeys
    private List<WsHotkeySlot> _wsSlots = new();

    private volatile bool _running;
    private Thread? _eventThread;

    public bool IsAvailable { get; private set; } = true;

    public event Action? InsertKeyPressed;
    public event Action? InsertKeyReleased;
    public event Action<int>? WsHotkeyPressed;
    public event Action<int>? WsHotkeyReleased;

    /// <summary>
    /// Converts MOD_* bitmask to X11 modifier mask.
    /// </summary>
    private static uint ModToX11Mask(int modifiers)
    {
        uint mask = 0;
        if ((modifiers & MOD_CONTROL) != 0) mask |= ControlMask;
        if ((modifiers & MOD_ALT) != 0) mask |= Mod1Mask;
        if ((modifiers & MOD_SHIFT) != 0) mask |= ShiftMask;
        if ((modifiers & MOD_WIN) != 0) mask |= Mod4Mask;
        return mask;
    }

    public void Start(int insertModifiers, int insertKeyCode, List<HotkeyAction> wsActions)
    {
        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (sessionType == "wayland")
        {
            Log.Warning("Running under Wayland. X11 hotkeys may not work. Consider: GDK_BACKEND=x11 or QT_QPA_PLATFORM=xcb");
        }

        _display = XOpenDisplay(null);
        if (_display == IntPtr.Zero)
        {
            IsAvailable = false;
            Log.Error("Cannot open X11 display. Hotkeys disabled");
            return;
        }

        _rootWindow = XDefaultRootWindow(_display);

        _insertX11Mod = ModToX11Mask(insertModifiers);
        GrabKey(insertKeyCode, _insertX11Mod, out _insertKeycode);

        _wsSlots = wsActions.Select(a =>
        {
            var x11Mod = ModToX11Mask(a.Modifiers);
            GrabKey(a.KeyCode, x11Mod, out int keycode);
            return new WsHotkeySlot { X11Keycode = keycode, X11Mod = x11Mod, IsDown = false };
        }).ToList();

        XFlush(_display);

        _running = true;
        _eventThread = new Thread(EventLoop) { IsBackground = true, Name = "X11HotkeyLoop" };
        _eventThread.Start();
    }

    private void GrabKey(int vkCode, uint x11Mod, out int keycode)
    {
        keycode = 0;
        if (!LinuxPlatformInfo.VkToX11KeySym.TryGetValue(vkCode, out uint keysym))
        {
            Log.Warning("No X11 keysym mapping for VK 0x{VkCode:X2}", vkCode);
            return;
        }

        keycode = XKeysymToKeycode(_display, keysym);
        if (keycode == 0)
        {
            Log.Warning("XKeysymToKeycode failed for keysym 0x{KeySym:X}", keysym);
            return;
        }

        // Grab with all NumLock/CapsLock combinations
        uint[] lockCombinations = [0, LockMask, Mod2Mask, LockMask | Mod2Mask];
        foreach (var lockMod in lockCombinations)
            XGrabKey(_display, keycode, x11Mod | lockMod, _rootWindow, 0, GrabModeAsync, GrabModeAsync);
    }

    private void UngrabKey(int keycode, uint x11Mod)
    {
        if (keycode == 0) return;
        uint[] lockCombinations = [0, LockMask, Mod2Mask, LockMask | Mod2Mask];
        foreach (var lockMod in lockCombinations)
            XUngrabKey(_display, keycode, x11Mod | lockMod, _rootWindow);
    }

    private void EventLoop()
    {
        IntPtr eventPtr = Marshal.AllocHGlobal(256);
        try
        {
            while (_running)
            {
                XNextEvent(_display, eventPtr);

                int type = Marshal.ReadInt32(eventPtr, 0);
                int keycode = Marshal.ReadInt32(eventPtr, XKeyEventKeycodeOffset);
                uint state = (uint)Marshal.ReadInt32(eventPtr, XKeyEventStateOffset);
                uint cleanState = state & ~IgnoreMask; // Strip CapsLock and NumLock

                // Insert hotkey
                if (keycode == _insertKeycode && cleanState == _insertX11Mod)
                {
                    if (type == KeyPress && !_isInsertDown)
                    {
                        _isInsertDown = true;
                        InsertKeyPressed?.Invoke();
                    }
                    else if (type == KeyRelease && _isInsertDown)
                    {
                        _isInsertDown = false;
                        InsertKeyReleased?.Invoke();
                    }
                    continue;
                }

                // Dynamic WS hotkeys
                var slots = _wsSlots;
                for (int i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    if (keycode == slot.X11Keycode && cleanState == slot.X11Mod)
                    {
                        if (type == KeyPress && !slot.IsDown)
                        {
                            slot.IsDown = true;
                            WsHotkeyPressed?.Invoke(i);
                        }
                        else if (type == KeyRelease && slot.IsDown)
                        {
                            slot.IsDown = false;
                            WsHotkeyReleased?.Invoke(i);
                        }
                        break;
                    }
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(eventPtr);
        }
    }

    public void Stop()
    {
        _running = false;

        if (_display != IntPtr.Zero)
        {
            UngrabKey(_insertKeycode, _insertX11Mod);
            foreach (var slot in _wsSlots)
                UngrabKey(slot.X11Keycode, slot.X11Mod);
            XFlush(_display);
            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }

        _eventThread?.Join(2000);
    }

    public void UpdateInsertHotkey(int modifiers, int keyCode)
    {
        if (_display == IntPtr.Zero) return;
        UngrabKey(_insertKeycode, _insertX11Mod);
        _insertX11Mod = ModToX11Mask(modifiers);
        GrabKey(keyCode, _insertX11Mod, out _insertKeycode);
        XFlush(_display);
        _isInsertDown = false;
    }

    public void UpdateWsHotkeys(List<HotkeyAction> wsActions)
    {
        if (_display == IntPtr.Zero) return;

        // Ungrab old
        foreach (var slot in _wsSlots)
            UngrabKey(slot.X11Keycode, slot.X11Mod);

        // Grab new
        _wsSlots = wsActions.Select(a =>
        {
            var x11Mod = ModToX11Mask(a.Modifiers);
            GrabKey(a.KeyCode, x11Mod, out int keycode);
            return new WsHotkeySlot { X11Keycode = keycode, X11Mod = x11Mod, IsDown = false };
        }).ToList();

        XFlush(_display);
    }

    public void Dispose()
    {
        Stop();
    }
}
