using System.Diagnostics;
using OpenVoicer.Models;

namespace OpenVoicer.Services;

public class OpenCodeProcessManager : IDisposable
{
    private readonly OpenVoicerSettings _settings;
    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };

    public event Action? Started;
    public event Action? Stopped;
    public event Action<string>? Error;

    public OpenCodeProcessManager(OpenVoicerSettings settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        if (IsRunning)
        {
            Console.WriteLine("[OC] Already running");
            return;
        }

        try
        {
            var psi = CreateStartInfo();

            Console.WriteLine($"[OC] Starting: {psi.FileName} {psi.Arguments}");

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) Console.WriteLine($"[OC stdout] {e.Data}");
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) Console.WriteLine($"[OC stderr] {e.Data}");
            };
            _process.Exited += (_, _) =>
            {
                Console.WriteLine($"[OC] Process exited with code {_process?.ExitCode}");
                Stopped?.Invoke();
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            Console.WriteLine($"[OC] Started (PID {_process.Id})");
            Started?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OC] Failed to start: {ex.Message}");
            Error?.Invoke($"Failed to start OpenCode: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!IsRunning) return;

        try
        {
            Console.WriteLine($"[OC] Stopping (PID {_process!.Id})...");

            // Try graceful shutdown first
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);

            Console.WriteLine("[OC] Stopped");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OC] Error stopping: {ex.Message}");
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    private ProcessStartInfo CreateStartInfo()
    {
        if (_settings.UseWsl)
        {
            var distroArg = !string.IsNullOrWhiteSpace(_settings.WslDistro)
                ? $"-d {_settings.WslDistro} "
                : "";
            return new ProcessStartInfo
            {
                FileName = "wsl",
                Arguments = $"{distroArg}-e opencode serve --port {_settings.OpenCodePort}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
        }

        return new ProcessStartInfo
        {
            FileName = "opencode",
            Arguments = $"serve --port {_settings.OpenCodePort}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
    }

    /// <summary>
    /// Builds command arguments for launching opencode TUI (attach) — used by App.axaml.cs.
    /// </summary>
    public (string FileName, string Arguments) GetTuiLaunchArgs(string attachArgs)
    {
        if (_settings.UseWsl)
        {
            var distroArg = !string.IsNullOrWhiteSpace(_settings.WslDistro)
                ? $"-d {_settings.WslDistro} "
                : "";
            return ("wsl", $"{distroArg}-e opencode {attachArgs}");
        }

        return ("opencode", attachArgs);
    }

    public void Dispose()
    {
        Stop();
    }
}
