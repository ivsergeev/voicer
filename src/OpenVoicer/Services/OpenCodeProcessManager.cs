using System.Diagnostics;
using OpenVoicer.Models;
using Serilog;

namespace OpenVoicer.Services;

public class OpenCodeProcessManager : IDisposable
{
    private readonly OpenVoicerSettings _settings;
    private Process? _process;

    public bool IsRunning
    {
        get
        {
            try
            {
                return _process is { HasExited: false };
            }
            catch
            {
                _process?.Dispose();
                _process = null;
                return false;
            }
        }
    }

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
            Log.Debug("[OC] Already running");
            return;
        }

        // Check if port is already in use (opencode may already be running externally)
        if (IsPortInUse(_settings.OpenCodePort))
        {
            Log.Information("[OC] Port {Port} already in use — assuming OpenCode is running externally", _settings.OpenCodePort);
            Started?.Invoke();
            return;
        }

        try
        {
            if (_settings.UseWsl)
                StartViaWsl();
            else
                StartDirect();

            Log.Information("[OC] Started (PID {Pid})", _process!.Id);
            Started?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OC] Failed to start");
            Error?.Invoke($"Failed to start OpenCode: {ex.Message}");
        }
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            client.Connect("127.0.0.1", port);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void StartDirect()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "opencode",
            Arguments = $"serve --port {_settings.OpenCodePort}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(_settings.WorkDir) && Directory.Exists(_settings.WorkDir))
            psi.WorkingDirectory = _settings.WorkDir;

        Log.Information("[OC] Starting: {FileName} {Arguments} (cwd: {Cwd})", psi.FileName, psi.Arguments, psi.WorkingDirectory ?? "(default)");
        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        AttachEvents();
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    private void StartViaWsl()
    {
        var distroArg = GetDistroArg();

        var workDir = string.IsNullOrWhiteSpace(_settings.WslWorkDir) ? "~" : _settings.WslWorkDir;
        var port = _settings.OpenCodePort;

        // Force interactive bash so .bashrc is fully loaded
        // (non-interactive shells skip .bashrc due to "case $- in *i*)" check)
        var psi = new ProcessStartInfo
        {
            FileName = "wsl",
            Arguments = (distroArg + "bash -i").Trim(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        Log.Information("[OC] Starting WSL: wsl {Arguments}", psi.Arguments);
        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        AttachEvents();
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        // serve uses CWD as project directory — cd first, then exec.
        // exec replaces shell with opencode — when opencode exits, WSL process exits too.
        // Use \n explicitly — Windows \r\n breaks paths in WSL.
        _process.StandardInput.NewLine = "\n";
        // .bashrc is loaded automatically by bash -i
        _process.StandardInput.WriteLine($"cd {workDir} && exec opencode serve --port {port}");
        _process.StandardInput.Close();

        Log.Information("[OC] Sent to WSL: cd {WorkDir} && exec opencode serve --port {Port}",
            workDir, port);
    }

    private void AttachEvents()
    {
        _process!.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null) Log.Debug("[OC stdout] {Data}", e.Data);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) Log.Warning("[OC stderr] {Data}", e.Data);
        };
        _process.Exited += (_, _) =>
        {
            var exitCode = -1;
            try { exitCode = _process?.ExitCode ?? -1; } catch { }
            Log.Information("[OC] Process exited with code {ExitCode}", exitCode);
            Stopped?.Invoke();
        };
    }

    public void Stop()
    {
        var proc = _process;
        if (proc == null) return;
        _process = null;

        try
        {
            bool hasExited;
            try { hasExited = proc.HasExited; }
            catch { hasExited = true; } // already disposed or inaccessible

            if (!hasExited)
            {
                try { Log.Information("[OC] Stopping (PID {Pid})...", proc.Id); }
                catch { Log.Information("[OC] Stopping..."); }

                if (_settings.UseWsl)
                    KillOpenCodeInWsl();

                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(5000);
            }
            Log.Information("[OC] Stopped");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OC] Error stopping");
        }
        finally
        {
            proc.Dispose();
        }
    }

    private void KillOpenCodeInWsl()
    {
        try
        {
            var distroArg = GetDistroArg();
            var psi = new ProcessStartInfo
            {
                FileName = "wsl",
                Arguments = distroArg.TrimEnd(),
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };

            using var killProc = Process.Start(psi);
            if (killProc != null)
            {
                killProc.StandardInput.NewLine = "\n";
                killProc.StandardInput.WriteLine($"pkill -f 'opencode serve --port {_settings.OpenCodePort}'");
                killProc.StandardInput.WriteLine("exit");
                killProc.StandardInput.Close();
                killProc.WaitForExit(3000);
            }
            Log.Debug("[OC] Sent pkill to opencode in WSL (port {Port})", _settings.OpenCodePort);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[OC] Failed to pkill opencode in WSL");
        }
    }

    /// <summary>
    /// Launches opencode TUI in a visible terminal window.
    /// For WSL: opens PowerShell → wsl → opencode attach.
    /// For direct: opens cmd → opencode attach.
    /// </summary>
    public void LaunchTui(string attachArgs)
    {
        if (_settings.UseWsl)
            LaunchTuiWsl(attachArgs);
        else
            LaunchTuiDirect(attachArgs);
    }

    private void LaunchTuiWsl(string attachArgs)
    {
        var distroArg = GetDistroArg();
        var workDir = string.IsNullOrWhiteSpace(_settings.WslWorkDir) ? "~" : _settings.WslWorkDir;

        // Step 1: Write TUI script inside WSL via stdin (avoids all escaping issues)
        const string tuiScript = "/tmp/openvoicer-tui.sh";
        WriteTuiScript(tuiScript, workDir, attachArgs);

        // Step 2: Launch terminal with wsl executing the script
        var wslRunArgs = $"{distroArg}sh {tuiScript}";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wt",
                Arguments = $"new-tab --title \"OpenCode\" wsl {wslRunArgs}",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Log.Information("[App] Launching TUI via wt: wsl {Args}", wslRunArgs);
            Process.Start(psi);
        }
        catch
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"OpenCode TUI\" wsl {wslRunArgs}",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Log.Information("[App] Launching TUI via cmd: wsl {Args}", wslRunArgs);
            Process.Start(psi);
        }
    }

    /// <summary>
    /// Writes a shell script inside WSL via stdin — no escaping issues.
    /// </summary>
    private void WriteTuiScript(string scriptPath, string workDir, string attachArgs)
    {
        var distroArg = GetDistroArg();
        var psi = new ProcessStartInfo
        {
            FileName = "wsl",
            Arguments = distroArg.TrimEnd(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        // Use \n explicitly — Windows \r\n breaks shell scripts in WSL
        proc.StandardInput.NewLine = "\n";
        // Write script via heredoc — clean, no escaping
        proc.StandardInput.WriteLine($"cat > {scriptPath} << 'OPENVOICER_EOF'");
        proc.StandardInput.WriteLine("#!/bin/sh");
        proc.StandardInput.WriteLine($"exec opencode {attachArgs} --dir {workDir}");
        proc.StandardInput.WriteLine("OPENVOICER_EOF");
        proc.StandardInput.WriteLine("exit");
        proc.StandardInput.Close();

        proc.WaitForExit(5000);
        Log.Debug("[OC] Wrote WSL TUI script: {Path}", scriptPath);
    }

    private void LaunchTuiDirect(string attachArgs)
    {
        var dirArg = !string.IsNullOrWhiteSpace(_settings.WorkDir) && Directory.Exists(_settings.WorkDir)
            ? $" --dir \"{_settings.WorkDir}\""
            : "";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"OpenCode TUI\" opencode {attachArgs}{dirArg}",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        Log.Information("[App] Launching TUI: {Arguments}", psi.Arguments);
        Process.Start(psi);
    }

    private string GetDistroArg()
    {
        return !string.IsNullOrWhiteSpace(_settings.WslDistro)
            ? $"-d {_settings.WslDistro} "
            : "";
    }

    public void Dispose()
    {
        Stop();
    }
}
