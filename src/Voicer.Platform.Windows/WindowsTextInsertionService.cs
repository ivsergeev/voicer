using System.Runtime.InteropServices;
using Voicer.Core.Interfaces;

namespace Voicer.Platform.Windows;

public class WindowsTextInsertionService : ITextInsertionService
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    // Clipboard
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12; // Alt
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_V = 0x56;
    private const uint WM_PASTE = 0x0302;
    private const uint MAPVK_VK_TO_VSC = 0;

    public Task<string?> GetClipboardText()
    {
        string? result = null;
        if (!OpenClipboard(IntPtr.Zero))
            return Task.FromResult(result);

        try
        {
            if (IsClipboardFormatAvailable(CF_UNICODETEXT))
            {
                IntPtr hData = GetClipboardData(CF_UNICODETEXT);
                if (hData != IntPtr.Zero)
                {
                    IntPtr pData = GlobalLock(hData);
                    if (pData != IntPtr.Zero)
                    {
                        try { result = Marshal.PtrToStringUni(pData); }
                        finally { GlobalUnlock(hData); }
                    }
                }
            }
        }
        finally
        {
            CloseClipboard();
        }

        return Task.FromResult(result);
    }

    public Task SetClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            Console.WriteLine("  [CLIPBOARD] OpenClipboard failed");
            return Task.CompletedTask;
        }

        try
        {
            EmptyClipboard();

            // Allocate global memory for the string (null-terminated UTF-16)
            int byteCount = (text.Length + 1) * 2;
            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
            if (hGlobal == IntPtr.Zero)
            {
                Console.WriteLine("  [CLIPBOARD] GlobalAlloc failed");
                return Task.CompletedTask;
            }

            IntPtr pGlobal = GlobalLock(hGlobal);
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pGlobal, text.Length);
                // Write null terminator
                Marshal.WriteInt16(pGlobal + text.Length * 2, 0);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            SetClipboardData(CF_UNICODETEXT, hGlobal);
            // Note: after SetClipboardData, the system owns hGlobal — don't free it
        }
        finally
        {
            CloseClipboard();
        }

        return Task.CompletedTask;
    }

    public async Task SimulatePaste()
    {
        IntPtr targetWindow = GetForegroundWindow();

        await Task.Run(() =>
        {
            Thread.Sleep(50);

            // Unconditionally release ALL modifier keys before pasting.
            // The hotkey hook may have consumed their key-up events, leaving
            // the system in a stale "held" state (e.g. Alt+letter hotkeys).
            // Never re-press — by the time paste runs after speech recognition
            // the user has long since released everything.
            ushort[] allModifiers = [VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN];

            var events = new List<INPUT>();

            foreach (var mod in allModifiers)
                events.Add(KeyUp(mod));

            events.Add(KeyDown(VK_CONTROL));
            events.Add(KeyDown(VK_V));
            events.Add(KeyUp(VK_V));
            events.Add(KeyUp(VK_CONTROL));

            var arr = events.ToArray();
            uint sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
            Console.WriteLine($"  [INSERT] SendInput sent {sent}/{arr.Length} events (all modifiers cleared before Ctrl+V)");

            // If SendInput was blocked (UIPI), try WM_PASTE
            if (sent == 0)
            {
                Console.WriteLine("  [INSERT] SendInput blocked, trying WM_PASTE fallback...");
                try
                {
                    IntPtr target = targetWindow;
                    uint foregroundThread = GetWindowThreadProcessId(targetWindow, out _);
                    uint currentThread = GetCurrentThreadId();

                    if (AttachThreadInput(currentThread, foregroundThread, true))
                    {
                        IntPtr focused = GetFocus();
                        if (focused != IntPtr.Zero)
                            target = focused;
                        AttachThreadInput(currentThread, foregroundThread, false);
                    }

                    SendMessage(target, WM_PASTE, IntPtr.Zero, IntPtr.Zero);
                    Console.WriteLine($"  [INSERT] WM_PASTE sent to 0x{target:X}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [INSERT] WM_PASTE also failed: {ex.Message}");
                }
            }
        });
    }

    private const ushort VK_C = 0x43;

    public async Task SimulateCopy()
    {
        await Task.Run(() =>
        {
            Thread.Sleep(50);

            ushort[] allModifiers = [VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN];
            var events = new List<INPUT>();

            foreach (var mod in allModifiers)
                events.Add(KeyUp(mod));

            events.Add(KeyDown(VK_CONTROL));
            events.Add(KeyDown(VK_C));
            events.Add(KeyUp(VK_C));
            events.Add(KeyUp(VK_CONTROL));

            var arr = events.ToArray();
            uint sent = SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>());
            Console.WriteLine($"  [COPY] SendInput sent {sent}/{arr.Length} events (Ctrl+C)");
        });
    }

    private static INPUT KeyUp(ushort vk) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT
        {
            wVk = vk,
            wScan = (ushort)MapVirtualKeyW(vk, MAPVK_VK_TO_VSC),
            dwFlags = KEYEVENTF_KEYUP,
        }},
    };

    private static INPUT KeyDown(ushort vk) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT
        {
            wVk = vk,
            wScan = (ushort)MapVirtualKeyW(vk, MAPVK_VK_TO_VSC),
        }},
    };
}
