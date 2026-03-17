using System.Diagnostics;
using System.Runtime.InteropServices;
using Voicer.Core.Interfaces;

namespace Voicer.Platform.macOS;

/// <summary>
/// macOS text insertion via Cmd+V simulation using CGEventPost (CoreGraphics).
/// Clipboard via pbcopy / pbpaste.
/// </summary>
public class MacTextInsertionService : ITextInsertionService
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private const int kCGHIDEventTap = 0;
    private const ushort kVK_ANSI_V = 0x09;
    private const ulong kCGEventFlagMaskCommand = 0x100000UL;

    [DllImport(CoreGraphics)]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

    [DllImport(CoreGraphics)]
    private static extern void CGEventSetFlags(IntPtr @event, ulong flags);

    [DllImport(CoreGraphics)]
    private static extern void CGEventPost(int tap, IntPtr @event);

    [DllImport(CoreGraphics)]
    private static extern ulong CGEventGetFlags(IntPtr @event);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    public async Task<string?> GetClipboardText()
    {
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("pbpaste")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return null;
                var text = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(2000);
                return text;
            }
            catch { return null; }
        });
    }

    public async Task SetClipboardText(string text)
    {
        await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("pbcopy")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return;
                proc.StandardInput.Write(text);
                proc.StandardInput.Close();
                proc.WaitForExit(2000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[macOS] Clipboard set failed: {ex.Message}");
            }
        });
    }

    private const ushort kVK_ANSI_C = 0x08;

    public async Task SimulateCopy()
    {
        await Task.Run(() =>
        {
            Thread.Sleep(50);

            IntPtr keyDown = CGEventCreateKeyboardEvent(IntPtr.Zero, kVK_ANSI_C, true);
            CGEventSetFlags(keyDown, kCGEventFlagMaskCommand);
            CGEventPost(kCGHIDEventTap, keyDown);
            CFRelease(keyDown);

            Thread.Sleep(10);

            IntPtr keyUp = CGEventCreateKeyboardEvent(IntPtr.Zero, kVK_ANSI_C, false);
            CGEventSetFlags(keyUp, kCGEventFlagMaskCommand);
            CGEventPost(kCGHIDEventTap, keyUp);
            CFRelease(keyUp);

            Console.WriteLine("  [COPY] macOS Cmd+C simulated via CGEventPost");
        });
    }

    public async Task SimulatePaste()
    {
        await Task.Run(() =>
        {
            Thread.Sleep(50);

            // Create Cmd+V key down event
            IntPtr keyDown = CGEventCreateKeyboardEvent(IntPtr.Zero, kVK_ANSI_V, true);
            CGEventSetFlags(keyDown, kCGEventFlagMaskCommand);
            CGEventPost(kCGHIDEventTap, keyDown);
            CFRelease(keyDown);

            Thread.Sleep(10);

            // Create Cmd+V key up event
            IntPtr keyUp = CGEventCreateKeyboardEvent(IntPtr.Zero, kVK_ANSI_V, false);
            CGEventSetFlags(keyUp, kCGEventFlagMaskCommand);
            CGEventPost(kCGHIDEventTap, keyUp);
            CFRelease(keyUp);

            Console.WriteLine("  [INSERT] macOS Cmd+V simulated via CGEventPost");
        });
    }
}
