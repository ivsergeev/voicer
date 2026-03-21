using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using OpenVoicer.Models;
using OpenVoicer.Services;
using OpenVoicer.Views;
using Serilog;
using SkiaSharp;

namespace OpenVoicer;

public partial class App : Application
{
    private VoicerWsClient _wsClient = null!;
    private OpenCodeProcessManager _processManager = null!;
    private OpenCodeClient _openCodeClient = null!;
    private OpenCodeEventService _eventService = null!;
    private OpenVoicerSettings _settings = null!;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _voicerStatusItem;
    private NativeMenuItem? _openCodeStatusItem;
    private NativeMenuItem? _openCodeToggleItem;
    private bool _settingsOpen;

    public override void Initialize()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            try
            {
                _settings = Program.Services.GetRequiredService<OpenVoicerSettings>();
                _wsClient = Program.Services.GetRequiredService<VoicerWsClient>();
                _processManager = Program.Services.GetRequiredService<OpenCodeProcessManager>();
                _openCodeClient = Program.Services.GetRequiredService<OpenCodeClient>();
                _eventService = Program.Services.GetRequiredService<OpenCodeEventService>();

                // SSE event service events
                _eventService.AgentBusy += sessionId => Dispatcher.UIThread.Post(() =>
                {
                    ShowNotification("Агент работает", null, "agent", badge: "Build",
                        duration: _settings.PopupDurationSeconds);
                });
                _eventService.AgentIdle += (sessionId, lastText, agent) => Dispatcher.UIThread.Post(() =>
                {
                    var trimmed = lastText?.Trim();
                    // Pass full text — popup will show preview and expand on click
                    ShowNotification("Готово", trimmed, "done", badge: agent ?? "Build",
                        duration: _settings.PopupDurationSeconds);
                });

                // WS client events
                _wsClient.Connected += () => Dispatcher.UIThread.Post(UpdateVoicerStatus);
                _wsClient.Disconnected += () => Dispatcher.UIThread.Post(UpdateVoicerStatus);
                _wsClient.ClaimChanged += _ => Dispatcher.UIThread.Post(UpdateVoicerStatus);
                _wsClient.TranscriptionReceived += (text, context, tag) =>
                {
                    // New session tag
                    if (!string.IsNullOrEmpty(tag) &&
                        string.Equals(tag, _settings.NewSessionTag, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Information("[App] New session tag received, creating new session...");
                        _ = HandleNewSessionAsync(text, context);
                        return;
                    }

                    if (string.IsNullOrEmpty(tag) && !string.IsNullOrEmpty(text))
                    {
                        // No tag → auto-send to OpenCode
                        var prompt = !string.IsNullOrEmpty(context)
                            ? $"{context}\n\n{text}"
                            : text;

                        Log.Information("[App] Auto-sending to OpenCode: {Prompt} agent={AgentID}", prompt.Substring(0, Math.Min(prompt.Length, 80)), _settings.AgentID);
                        _ = _openCodeClient.SendPromptAsync(prompt, _settings.AgentID);
                    }
                };

                // Process manager events
                _processManager.Started += () => Dispatcher.UIThread.Post(UpdateOpenCodeStatus);
                _processManager.Stopped += () => Dispatcher.UIThread.Post(UpdateOpenCodeStatus);
                _processManager.Error += msg => Dispatcher.UIThread.Post(() =>
                {
                    UpdateOpenCodeStatus();
                    ShowNotification("Ошибка OpenCode", msg, "error");
                });

                // SSE connection status → update OpenCode status
                _eventService.Connected += () => Dispatcher.UIThread.Post(UpdateOpenCodeStatus);
                _eventService.Disconnected += () => Dispatcher.UIThread.Post(UpdateOpenCodeStatus);

                InitializeTrayIcon();

                // Start WS client
                _wsClient.Start();

                // Auto-start OpenCode if configured
                if (_settings.AutoStartOpenCode)
                {
                    _processManager.Start();
                }

                // Apply saved model to OpenCode config
                if (!string.IsNullOrEmpty(_settings.ProviderID) && !string.IsNullOrEmpty(_settings.ModelID))
                {
                    _ = _openCodeClient.SetModelAsync(_settings.ProviderID, _settings.ModelID);
                }

                // Start SSE event listener
                _eventService.Start();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[App] FATAL: OnFrameworkInitializationCompleted failed");
                // Without tray icon the app is invisible — force exit
                Environment.Exit(1);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeTrayIcon()
    {
        var menu = new NativeMenu();

        _voicerStatusItem = new NativeMenuItem("Voicer: отключён") { IsEnabled = false };
        menu.Items.Add(_voicerStatusItem);

        _openCodeStatusItem = new NativeMenuItem("OpenCode: остановлен") { IsEnabled = false };
        menu.Items.Add(_openCodeStatusItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        _openCodeToggleItem = new NativeMenuItem("Запустить OpenCode");
        _openCodeToggleItem.Click += (_, _) => ToggleOpenCode();
        menu.Items.Add(_openCodeToggleItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var openItem = new NativeMenuItem("Открыть");
        openItem.Click += (_, _) => OpenTui();
        menu.Items.Add(openItem);

        var settingsItem = new NativeMenuItem("Настройки...");
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("Выход");
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = CreateTrayIcon(),
            ToolTipText = "OpenVoicer",
            Menu = menu,
            IsVisible = true,
        };

        _trayIcon.Clicked += (_, _) => OpenTui();

        UpdateVoicerStatus();
        UpdateOpenCodeStatus();
    }

    private void OpenTui()
    {
        _ = LaunchOpenCodeTuiAsync();
    }

    private async Task LaunchOpenCodeTuiAsync()
    {
        try
        {
            var sessionId = await _openCodeClient.ResolveSessionAsync();
            var url = $"http://localhost:{_settings.OpenCodePort}";
            var attachArgs = $"attach {url}";
            if (!string.IsNullOrEmpty(sessionId))
                attachArgs += $" --session {sessionId}";

            var processManager = Program.Services.GetService(typeof(OpenCodeProcessManager)) as OpenCodeProcessManager;
            if (processManager != null)
            {
                processManager.LaunchTui(attachArgs);
            }
            else
            {
                // Fallback
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"OpenCode\" opencode {attachArgs}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                System.Diagnostics.Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[App] Failed to launch TUI");
            ShowNotification("Ошибка", $"Не удалось запустить OpenCode TUI: {ex.Message}", "error");
        }
    }

    private async Task HandleNewSessionAsync(string? text, string? context)
    {
        var sessionId = await _openCodeClient.CreateNewSessionAsync();
        if (sessionId != null)
        {
            Log.Information("[App] New session created: {SessionId}", sessionId);
            Dispatcher.UIThread.Post(() =>
                ShowNotification("Новая сессия", sessionId, "agent",
                    duration: _settings.PopupDurationSeconds));

            // If there was text with the new-session tag, send it as first prompt
            if (!string.IsNullOrEmpty(text))
            {
                var prompt = !string.IsNullOrEmpty(context)
                    ? $"{context}\n\n{text}"
                    : text;
                _ = _openCodeClient.SendPromptAsync(prompt, _settings.AgentID);
            }
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
                ShowNotification("Ошибка", "Не удалось создать новую сессию", "error"));
        }
    }

    public void ShowNotification(string title, string? description, string type,
        string? badge = null, double duration = 4,
        Action? onApprove = null, Action? onReject = null)
    {
        if (!_settings.ShowPopup) return;

        // Truncate description if PopupMaxLength is set
        if (description != null && _settings.PopupMaxLength > 0 && description.Length > _settings.PopupMaxLength)
            description = description.Substring(0, _settings.PopupMaxLength) + "...";

        Dispatcher.UIThread.Post(() =>
        {
            var popup = new NotificationPopup();
            popup.Show(title, description, type, badge, duration, onApprove, onReject);
        });
    }

    private void UpdateVoicerStatus()
    {
        if (_voicerStatusItem == null) return;

        if (_wsClient.IsConnected)
        {
            _voicerStatusItem.Header = _wsClient.IsClaimed
                ? "● Voicer: подключён (claim)"
                : "● Voicer: подключён";
        }
        else
        {
            _voicerStatusItem.Header = "○ Voicer: отключён";
        }

        UpdateTooltip();
    }

    private void UpdateOpenCodeStatus()
    {
        if (_openCodeStatusItem == null || _openCodeToggleItem == null) return;

        if (_eventService.IsConnected)
        {
            _openCodeStatusItem.Header = "● OpenCode: подключён";
            _openCodeToggleItem.Header = "Остановить OpenCode";
        }
        else if (_processManager.IsRunning)
        {
            _openCodeStatusItem.Header = "◐ OpenCode: запускается...";
            _openCodeToggleItem.Header = "Остановить OpenCode";
        }
        else
        {
            _openCodeStatusItem.Header = "○ OpenCode: отключён";
            _openCodeToggleItem.Header = "Запустить OpenCode";
        }

        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        if (_trayIcon == null) return;

        var voicer = _wsClient.IsConnected ? "Voicer ✓" : "Voicer ✗";
        var oc = _eventService.IsConnected ? "OC ✓" : "OC ✗";
        _trayIcon.ToolTipText = $"OpenVoicer — {voicer} · {oc}";
    }

    private void ToggleOpenCode()
    {
        if (_processManager.IsRunning)
            _processManager.Stop();
        else
            _processManager.Start();
    }

    private void ShowSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;

        try
        {
            var window = new SettingsWindow(_settings, _openCodeClient);

            window.SettingsChanged += newSettings =>
            {
                bool wsPortChanged = _settings.VoicerWsPort != newSettings.VoicerWsPort;
                bool ocPortChanged = _settings.OpenCodePort != newSettings.OpenCodePort;
                bool wslChanged = _settings.UseWsl != newSettings.UseWsl ||
                                  _settings.WslDistro != newSettings.WslDistro ||
                                  _settings.WslWorkDir != newSettings.WslWorkDir;
                bool modelChanged = _settings.ProviderID != newSettings.ProviderID ||
                                    _settings.ModelID != newSettings.ModelID;

                _settings.VoicerWsPort = newSettings.VoicerWsPort;
                _settings.OpenCodePort = newSettings.OpenCodePort;
                _settings.AutoStartOpenCode = newSettings.AutoStartOpenCode;
                _settings.UseWsl = newSettings.UseWsl;
                _settings.WslDistro = newSettings.WslDistro;
                _settings.WslWorkDir = newSettings.WslWorkDir;
                _settings.ProviderID = newSettings.ProviderID;
                _settings.ModelID = newSettings.ModelID;
                _settings.AgentID = newSettings.AgentID;
                _settings.NewSessionTag = newSettings.NewSessionTag;
                _settings.ShowPopup = newSettings.ShowPopup;
                _settings.PopupDurationSeconds = newSettings.PopupDurationSeconds;
                _settings.PopupMaxLength = newSettings.PopupMaxLength;
                _settings.Save();

                if (wsPortChanged)
                {
                    _wsClient.Stop();
                    _wsClient.Start();
                }

                if (ocPortChanged || wslChanged)
                {
                    // Restart OpenCode with new settings
                    Log.Information("[App] OpenCode settings changed, restarting...");
                    _eventService.Stop();
                    _processManager.Stop();
                    if (_settings.AutoStartOpenCode)
                    {
                        _processManager.Start();
                    }
                    _eventService.Start();
                    UpdateOpenCodeStatus();
                }

                if (modelChanged && !string.IsNullOrEmpty(newSettings.ProviderID) &&
                    !string.IsNullOrEmpty(newSettings.ModelID))
                {
                    _ = _openCodeClient.SetModelAsync(newSettings.ProviderID, newSettings.ModelID);
                }
            };

            window.Closed += (_, _) => _settingsOpen = false;
            window.Show();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[App] ShowSettings failed");
            _settingsOpen = false;
        }
    }

    private static WindowIcon CreateTrayIcon()
    {
        const int size = 32;
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        using var bgPaint = new SKPaint { Color = new SKColor(0x15, 0x65, 0xC0), IsAntialias = true };
        canvas.DrawCircle(size / 2f, size / 2f, size / 2f - 1, bgPaint);

        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 13,
            TextAlign = SKTextAlign.Center,
            FakeBoldText = true,
        };
        var textY = size / 2f + textPaint.TextSize / 3f;
        canvas.DrawText("OV", size / 2f, textY, textPaint);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var stream = new MemoryStream(data.ToArray());
        return new WindowIcon(stream);
    }

    private void ExitApplication()
    {
        Log.Information("[App] Shutting down...");

        _wsClient.Stop();
        _eventService.Stop();
        _processManager.Stop();

        _trayIcon?.Dispose();
        _trayIcon = null;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
