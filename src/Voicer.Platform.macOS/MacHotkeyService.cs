using System.Runtime.InteropServices;
using Voicer.Core.Interfaces;

namespace Voicer.Platform.macOS;

/// <summary>
/// macOS global hotkey service via CGEventTap (CoreGraphics).
/// Requires Accessibility permission: System Settings → Privacy &amp; Security → Accessibility.
/// </summary>
public class MacHotkeyService : IHotkeyService
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint kCGEventKeyDown = 10;
    private const uint kCGEventKeyUp = 11;
    private const uint kCGEventFlagsChanged = 12;
    private const int kCGHIDEventTap = 0;
    private const int kCGHeadInsertEventTap = 0;
    private const int kCGEventTapOptionDefault = 0;
    private const uint kCGKeyboardEventKeycode = 9;

    // CGEventFlags for modifier keys
    private const ulong kCGEventFlagMaskControl = 0x40000;
    private const ulong kCGEventFlagMaskAlternate = 0x80000;
    private const ulong kCGEventFlagMaskShift = 0x20000;
    private const ulong kCGEventFlagMaskCommand = 0x100000;

    // Mask covering all relevant modifiers
    private const ulong RelevantModifierMask =
        kCGEventFlagMaskControl | kCGEventFlagMaskAlternate |
        kCGEventFlagMaskShift | kCGEventFlagMaskCommand;

    // MOD_* constants (Win32 canonical format)
    private const int MOD_ALT = 0x0001;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int MOD_WIN = 0x0008;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CGEventTapCallBack(IntPtr proxy, uint type, IntPtr @event, IntPtr userInfo);

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventTapCreate(
        int tap, int place, int options,
        ulong eventsOfInterest, CGEventTapCallBack callback, IntPtr userInfo);

    [DllImport(CoreGraphics)]
    private static extern void CGEventTapEnable(IntPtr tap, bool enable);

    [DllImport(CoreGraphics)]
    private static extern long CGEventGetIntegerValueField(IntPtr @event, uint field);

    [DllImport(CoreGraphics)]
    private static extern ulong CGEventGetFlags(IntPtr @event);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr port, int order);

    [DllImport(CoreFoundation)]
    private static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFRunLoopGetCurrent();

    [DllImport(CoreFoundation)]
    private static extern void CFRunLoopRun();

    [DllImport(CoreFoundation)]
    private static extern void CFRunLoopStop(IntPtr rl);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, int encoding);

    private IntPtr _eventTap;
    private IntPtr _runLoopSource;
    private IntPtr _runLoop;
    private Thread? _tapThread;
    private CGEventTapCallBack? _callbackDelegate;

    private ushort _primaryMacKeycode;
    private ulong _primaryExpectedFlags;
    private ushort _insertMacKeycode;
    private ulong _insertExpectedFlags;
    private bool _isPrimaryDown;
    private bool _isInsertDown;

    public bool IsAvailable => true;

    public event Action? KeyPressed;
    public event Action? KeyReleased;
    public event Action? InsertKeyPressed;
    public event Action? InsertKeyReleased;

    /// <summary>
    /// Converts MOD_* bitmask to CGEventFlags mask.
    /// </summary>
    private static ulong ModToCGFlags(int modifiers)
    {
        ulong flags = 0;
        if ((modifiers & MOD_CONTROL) != 0) flags |= kCGEventFlagMaskControl;
        if ((modifiers & MOD_ALT) != 0) flags |= kCGEventFlagMaskAlternate;
        if ((modifiers & MOD_SHIFT) != 0) flags |= kCGEventFlagMaskShift;
        if ((modifiers & MOD_WIN) != 0) flags |= kCGEventFlagMaskCommand;
        return flags;
    }

    public void Start(int primaryModifiers, int primaryKeyCode, int insertModifiers, int insertKeyCode)
    {
        MacPlatformInfo.VkToMacKeyCode.TryGetValue(primaryKeyCode, out _primaryMacKeycode);
        MacPlatformInfo.VkToMacKeyCode.TryGetValue(insertKeyCode, out _insertMacKeycode);
        _primaryExpectedFlags = ModToCGFlags(primaryModifiers);
        _insertExpectedFlags = ModToCGFlags(insertModifiers);

        _tapThread = new Thread(RunEventTap) { IsBackground = true, Name = "CGEventTapLoop" };
        _tapThread.Start();
    }

    private void RunEventTap()
    {
        _callbackDelegate = EventTapCallback;

        ulong eventMask = (1UL << (int)kCGEventKeyDown) | (1UL << (int)kCGEventKeyUp)
                        | (1UL << (int)kCGEventFlagsChanged);

        _eventTap = CGEventTapCreate(
            kCGHIDEventTap, kCGHeadInsertEventTap, kCGEventTapOptionDefault,
            eventMask, _callbackDelegate, IntPtr.Zero);

        if (_eventTap == IntPtr.Zero)
        {
            Console.WriteLine("[macOS] ERROR: Failed to create CGEventTap.");
            Console.WriteLine("  Grant Accessibility permission: System Settings → Privacy & Security → Accessibility.");
            return;
        }

        _runLoopSource = CFMachPortCreateRunLoopSource(IntPtr.Zero, _eventTap, 0);
        _runLoop = CFRunLoopGetCurrent();

        var commonModes = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopCommonModes", 0x08000100);
        CFRunLoopAddSource(_runLoop, _runLoopSource, commonModes);
        CGEventTapEnable(_eventTap, true);

        Console.WriteLine("[macOS] CGEventTap active. Listening for hotkeys.");
        CFRunLoopRun();
    }

    private IntPtr EventTapCallback(IntPtr proxy, uint type, IntPtr @event, IntPtr userInfo)
    {
        // Re-enable tap if system disabled it
        if (type > 0xFF)
        {
            if (_eventTap != IntPtr.Zero)
                CGEventTapEnable(_eventTap, true);
            return @event;
        }

        // Handle modifier key release while hotkey is held down
        if (type == kCGEventFlagsChanged)
        {
            if (_isPrimaryDown || _isInsertDown)
            {
                ulong flags = CGEventGetFlags(@event);
                ulong currentMod = flags & RelevantModifierMask;

                if (_isPrimaryDown && currentMod != _primaryExpectedFlags)
                {
                    _isPrimaryDown = false;
                    KeyReleased?.Invoke();
                }
                if (_isInsertDown && currentMod != _insertExpectedFlags)
                {
                    _isInsertDown = false;
                    InsertKeyReleased?.Invoke();
                }
            }
            return @event;
        }

        ushort keycode = (ushort)CGEventGetIntegerValueField(@event, kCGKeyboardEventKeycode);
        ulong eventFlags = CGEventGetFlags(@event);
        ulong currentModifiers = eventFlags & RelevantModifierMask;

        if (keycode == _primaryMacKeycode && currentModifiers == _primaryExpectedFlags)
        {
            if (type == kCGEventKeyDown && !_isPrimaryDown)
            {
                _isPrimaryDown = true;
                KeyPressed?.Invoke();
            }
            else if (type == kCGEventKeyUp && _isPrimaryDown)
            {
                _isPrimaryDown = false;
                KeyReleased?.Invoke();
            }
            return IntPtr.Zero; // Consume
        }

        if (keycode == _insertMacKeycode && currentModifiers == _insertExpectedFlags)
        {
            if (type == kCGEventKeyDown && !_isInsertDown)
            {
                _isInsertDown = true;
                InsertKeyPressed?.Invoke();
            }
            else if (type == kCGEventKeyUp && _isInsertDown)
            {
                _isInsertDown = false;
                InsertKeyReleased?.Invoke();
            }
            return IntPtr.Zero; // Consume
        }

        return @event; // Pass through
    }

    public void Stop()
    {
        if (_eventTap != IntPtr.Zero)
            CGEventTapEnable(_eventTap, false);

        if (_runLoop != IntPtr.Zero)
        {
            CFRunLoopStop(_runLoop);
            _runLoop = IntPtr.Zero;
        }

        _tapThread?.Join(2000);

        if (_runLoopSource != IntPtr.Zero) { CFRelease(_runLoopSource); _runLoopSource = IntPtr.Zero; }
        if (_eventTap != IntPtr.Zero) { CFRelease(_eventTap); _eventTap = IntPtr.Zero; }
    }

    public void UpdateHotkey(int modifiers, int keyCode)
    {
        if (MacPlatformInfo.VkToMacKeyCode.TryGetValue(keyCode, out var macKey))
            _primaryMacKeycode = macKey;
        _primaryExpectedFlags = ModToCGFlags(modifiers);
        _isPrimaryDown = false;
    }

    public void UpdateInsertHotkey(int modifiers, int keyCode)
    {
        if (MacPlatformInfo.VkToMacKeyCode.TryGetValue(keyCode, out var macKey))
            _insertMacKeycode = macKey;
        _insertExpectedFlags = ModToCGFlags(modifiers);
        _isInsertDown = false;
    }

    public void Dispose()
    {
        Stop();
    }
}
