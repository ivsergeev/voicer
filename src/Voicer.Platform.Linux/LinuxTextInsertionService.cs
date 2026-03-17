using System.Diagnostics;
using Voicer.Core.Interfaces;

namespace Voicer.Platform.Linux;

/// <summary>
/// Linux text insertion via xdotool (X11) or wtype (Wayland).
/// Clipboard via xclip (X11) or wl-copy/wl-paste (Wayland).
/// </summary>
public class LinuxTextInsertionService : ITextInsertionService
{
    private static bool IsWayland =>
        Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland";

    public async Task<string?> GetClipboardText()
    {
        return await Task.Run(() =>
        {
            try
            {
                var tool = IsWayland ? "wl-paste" : "xclip";
                var args = IsWayland ? "--no-newline" : "-selection clipboard -o";

                var psi = new ProcessStartInfo(tool, args)
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
                var tool = IsWayland ? "wl-copy" : "xclip";
                var args = IsWayland ? "" : "-selection clipboard";

                var psi = new ProcessStartInfo(tool, args)
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
                Console.WriteLine($"[Linux] Clipboard set failed: {ex.Message}");
            }
        });
    }

    public async Task SimulateCopy()
    {
        await Task.Run(() =>
        {
            Thread.Sleep(50);

            try
            {
                if (IsWayland)
                {
                    // wtype doesn't have a direct copy, use wl-copy with selection
                    Process.Start(new ProcessStartInfo("wtype", "-M ctrl -P c -p c -m ctrl")
                    {
                        UseShellExecute = false, CreateNoWindow = true
                    })?.WaitForExit(2000);
                }
                else
                {
                    Process.Start(new ProcessStartInfo("xdotool", "key --clearmodifiers ctrl+c")
                    {
                        UseShellExecute = false, CreateNoWindow = true
                    })?.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Linux] SimulateCopy failed: {ex.Message}");
            }
        });
    }

    public async Task SimulatePaste()
    {
        await Task.Run(() =>
        {
            Thread.Sleep(50);

            try
            {
                if (IsWayland)
                {
                    Process.Start(new ProcessStartInfo("wtype", "-M ctrl -P v -p v -m ctrl")
                    {
                        UseShellExecute = false, CreateNoWindow = true
                    })?.WaitForExit(2000);
                }
                else
                {
                    Process.Start(new ProcessStartInfo("xdotool", "key --clearmodifiers ctrl+v")
                    {
                        UseShellExecute = false, CreateNoWindow = true
                    })?.WaitForExit(2000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Linux] Text insertion failed: {ex.Message}");
                Console.WriteLine("  Ensure xdotool (X11) or wtype (Wayland) is installed.");
            }
        });
    }
}
