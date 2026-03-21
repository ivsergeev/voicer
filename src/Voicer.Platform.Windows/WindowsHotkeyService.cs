using System.Runtime.InteropServices;
using Voicer.Core.Interfaces;
using Voicer.Core.Models;

namespace Voicer.Platform.Windows;

public class WindowsHotkeyService : IHotkeyService
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    // MOD_* constants (same as Win32 RegisterHotKey)
    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int MOD_WIN = 0x0008;

    // VK codes for modifier keys
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;    // Left Alt
    private const int VK_RMENU = 0xA5;    // Right Alt
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private class WsHotkeySlot
    {
        public int VkCode;
        public int Modifiers;
        public bool IsDown;
    }

    private IntPtr _hookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _proc;

    // Insert hotkey (F6)
    private int _insertVkCode;
    private int _insertModifiers;
    private bool _isInsertKeyDown;

    // Dynamic WS hotkeys
    private List<WsHotkeySlot> _wsSlots = new();

    private int _consumeModifiers; // MOD_* bits of modifier releases to swallow

    public bool IsAvailable => true;

    public event Action? InsertKeyPressed;
    public event Action? InsertKeyReleased;
    public event Action<int>? WsHotkeyPressed;
    public event Action<int>? WsHotkeyReleased;

    public WindowsHotkeyService()
    {
        _proc = HookCallback;
    }

    public void Start(int insertModifiers, int insertKeyCode, List<HotkeyAction> wsActions)
    {
        _insertModifiers = insertModifiers;
        _insertVkCode = insertKeyCode;
        _isInsertKeyDown = false;

        _wsSlots = wsActions.Select(a => new WsHotkeySlot
        {
            VkCode = a.KeyCode,
            Modifiers = a.Modifiers,
            IsDown = false,
        }).ToList();

        using var process = System.Diagnostics.Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);

        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to set keyboard hook. Error: {Marshal.GetLastWin32Error()}");
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    public void UpdateInsertHotkey(int modifiers, int keyCode)
    {
        _insertModifiers = modifiers;
        _insertVkCode = keyCode;
        _isInsertKeyDown = false;
        _consumeModifiers = 0;
    }

    public void UpdateWsHotkeys(List<HotkeyAction> wsActions)
    {
        _wsSlots = wsActions.Select(a => new WsHotkeySlot
        {
            VkCode = a.KeyCode,
            Modifiers = a.Modifiers,
            IsDown = false,
        }).ToList();
        _consumeModifiers = 0;
    }

    private static int GetCurrentModifiers()
    {
        int mod = 0;
        if ((GetAsyncKeyState(VK_LCONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_RCONTROL) & 0x8000) != 0)
            mod |= MOD_CONTROL;
        if ((GetAsyncKeyState(VK_LMENU) & 0x8000) != 0 || (GetAsyncKeyState(VK_RMENU) & 0x8000) != 0)
            mod |= MOD_ALT;
        if ((GetAsyncKeyState(VK_LSHIFT) & 0x8000) != 0 || (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0)
            mod |= MOD_SHIFT;
        if ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0)
            mod |= MOD_WIN;
        return mod;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();

            // --- Insert hotkey ---
            if (kbd.vkCode == _insertVkCode)
            {
                int currentMod = GetCurrentModifiers();
                if (currentMod == _insertModifiers)
                {
                    if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && !_isInsertKeyDown)
                    {
                        _isInsertKeyDown = true;
                        _consumeModifiers |= _insertModifiers;
                        InsertKeyPressed?.Invoke();
                    }
                    else if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && _isInsertKeyDown)
                    {
                        _isInsertKeyDown = false;
                        InsertKeyReleased?.Invoke();
                    }

                    return (IntPtr)1; // Consume the key
                }
            }

            // Release insert hotkey if a REQUIRED modifier was released while held
            if (_isInsertKeyDown && _insertModifiers != 0 && (msg == WM_KEYUP || msg == WM_SYSKEYUP))
            {
                if (IsModifierVk(kbd.vkCode) && (_insertModifiers & VkToModBit(kbd.vkCode)) != 0)
                {
                    _isInsertKeyDown = false;
                    InsertKeyReleased?.Invoke();
                }
            }

            // --- Dynamic WS hotkeys ---
            var slots = _wsSlots; // snapshot reference
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (kbd.vkCode == slot.VkCode)
                {
                    int currentMod = GetCurrentModifiers();
                    if (currentMod == slot.Modifiers)
                    {
                        if ((msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && !slot.IsDown)
                        {
                            slot.IsDown = true;
                            _consumeModifiers |= slot.Modifiers;
                            WsHotkeyPressed?.Invoke(i);
                        }
                        else if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && slot.IsDown)
                        {
                            slot.IsDown = false;
                            WsHotkeyReleased?.Invoke(i);
                        }

                        return (IntPtr)1; // Consume the key
                    }
                }

                // Release WS hotkey if a REQUIRED modifier was released while held
                if (slot.IsDown && slot.Modifiers != 0 && (msg == WM_KEYUP || msg == WM_SYSKEYUP))
                {
                    if (IsModifierVk(kbd.vkCode) && (slot.Modifiers & VkToModBit(kbd.vkCode)) != 0)
                    {
                        slot.IsDown = false;
                        WsHotkeyReleased?.Invoke(i);
                    }
                }
            }

            // Consume modifier key releases that were part of a hotkey activation.
            if (_consumeModifiers != 0
                && (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                && IsModifierVk(kbd.vkCode))
            {
                int modBit = VkToModBit(kbd.vkCode);
                if ((_consumeModifiers & modBit) != 0)
                {
                    _consumeModifiers &= ~modBit;
                    return (IntPtr)1; // Consume modifier release
                }
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool IsModifierVk(uint vkCode)
    {
        return vkCode is VK_LCONTROL or VK_RCONTROL
            or VK_LMENU or VK_RMENU
            or VK_LSHIFT or VK_RSHIFT
            or VK_LWIN or VK_RWIN;
    }

    private static int VkToModBit(uint vkCode)
    {
        return vkCode switch
        {
            VK_LCONTROL or VK_RCONTROL => MOD_CONTROL,
            VK_LMENU or VK_RMENU => MOD_ALT,
            VK_LSHIFT or VK_RSHIFT => MOD_SHIFT,
            VK_LWIN or VK_RWIN => MOD_WIN,
            _ => 0,
        };
    }

    public void Dispose()
    {
        Stop();
    }
}
