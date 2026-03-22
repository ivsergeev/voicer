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
    private readonly Queue<NotificationPopup> _processingPopups = new();
    private readonly List<(string Text, NotificationPopup? Popup)> _contextMessages = new();
    private NotificationPopup? _responsePopup;
    private bool _cancelRequested;

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
                    // New agent work started — clear stale cancel flag
                    _cancelRequested = false;

                    // Skip if processing popups are already showing
                    if (_processingPopups.Count > 0) return;

                    ShowNotification("Агент работает", null, "agent", badge: "Build",
                        duration: _settings.PopupDurationSeconds);
                });
                _eventService.AgentIdle += (sessionId, lastText, agent) => Dispatcher.UIThread.Post(() =>
                {
                    // Dismiss previous response popup
                    _responsePopup?.Dismiss();
                    _responsePopup = null;

                    // After cancel: dismiss ALL processing popups, show one "Отменено"
                    if (_cancelRequested)
                    {
                        _cancelRequested = false;
                        DismissAllProcessingPopups();
                        ShowNotification("Отменено", null, "agent", badge: agent ?? "Build",
                            duration: _settings.PopupDurationSeconds);
                        return;
                    }

                    var trimmed = lastText?.Trim();

                    if (_processingPopups.Count > 0)
                    {
                        // Keep the last popup → complete with response; dismiss the rest
                        var last = DismissAllProcessingPopupsExceptLast();
                        if (last != null)
                        {
                            // Subscribe and assign BEFORE Complete() to avoid
                            // race where popup closes before handler is attached
                            _responsePopup = last;
                            last.Closed += (_, _) =>
                            {
                                if (_responsePopup == last) _responsePopup = null;
                            };
                            last.Complete("Готово", trimmed, badge: agent ?? "Build",
                                duration: _settings.PopupDurationSeconds);
                            return;
                        }
                    }

                    // Fallback: no processing popup — show standalone
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

                    // Context tag — accumulate, don't send yet
                    if (!string.IsNullOrEmpty(tag) &&
                        string.Equals(tag, _settings.ContextTag, StringComparison.OrdinalIgnoreCase))
                    {
                        // Build context from text and/or selected text
                        var parts = new List<string>();
                        if (!string.IsNullOrEmpty(context)) parts.Add(context);
                        if (!string.IsNullOrEmpty(text)) parts.Add(text);
                        if (parts.Count == 0) return; // nothing to add

                        var ctxText = string.Join("\n\n", parts);
                        Log.Information("[App] Context message added: {Text}", ctxText.Substring(0, Math.Min(ctxText.Length, 80)));
                        Dispatcher.UIThread.Post(() => AddContextMessage(ctxText));
                        return;
                    }

                    // Any other message (no tag or unknown tag) → send to OpenCode
                    {
                        var parts = new List<string>();
                        if (!string.IsNullOrEmpty(context)) parts.Add(context);
                        if (!string.IsNullOrEmpty(text)) parts.Add(text);

                        // Prepend accumulated context messages
                        if (_contextMessages.Count > 0)
                        {
                            var ctxParts = _contextMessages.Select(c => c.Text).ToList();
                            ctxParts.AddRange(parts);
                            parts = ctxParts;

                            // Capture popup refs, clear list synchronously, dismiss on UI thread
                            var popups = _contextMessages
                                .Where(c => c.Popup != null)
                                .Select(c => c.Popup!)
                                .ToList();
                            _contextMessages.Clear();
                            Dispatcher.UIThread.Post(() =>
                            {
                                foreach (var p in popups) p.Dismiss();
                            });
                        }

                        if (parts.Count == 0) return; // nothing to send

                        var prompt = string.Join("\n\n", parts);

                        Log.Information("[App] Auto-sending to OpenCode: {Prompt} agent={AgentID}", prompt.Substring(0, Math.Min(prompt.Length, 80)), _settings.AgentID);
                        _ = _openCodeClient.SendPromptAsync(prompt, _settings.AgentID);

                        // Show persistent processing popup immediately
                        Dispatcher.UIThread.Post(() => ShowProcessingPopup(
                            !string.IsNullOrEmpty(text) ? text : "(контекст)"));
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
            Icon = CreateTrayIcon(TrayConnectionState.Disconnected),
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
        // Show immediate feedback — session creation can be slow
        NotificationPopup? creatingPopup = null;
        if (_settings.ShowPopup)
        {
            Dispatcher.UIThread.Post(() =>
            {
                creatingPopup = new NotificationPopup();
                creatingPopup.Show("Создание сессии...", null, "processing", badge: "Session");
            });
        }

        var sessionId = await _openCodeClient.CreateNewSessionAsync();

        if (sessionId != null)
        {
            Log.Information("[App] New session created: {SessionId}", sessionId);
            Dispatcher.UIThread.Post(() =>
            {
                creatingPopup?.Dismiss();
                ShowNotification("Новая сессия", sessionId, "agent",
                    duration: _settings.PopupDurationSeconds);
            });

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
            {
                creatingPopup?.Dismiss();
                ShowNotification("Ошибка", "Не удалось создать новую сессию", "error");
            });
        }
    }

    private void AddContextMessage(string text)
    {
        if (!_settings.ShowPopup)
        {
            _contextMessages.Add((text, Popup: null));
            return;
        }

        var displayText = text;
        if (_settings.PopupMaxLength > 0 && displayText.Length > _settings.PopupMaxLength)
            displayText = displayText.Substring(0, _settings.PopupMaxLength) + "...";

        var popup = new NotificationPopup();
        var entry = (Text: text, Popup: (NotificationPopup?)popup);

        // Add to list and show BEFORE subscribing to close events
        // to avoid race where popup closes before entry is in the list
        _contextMessages.Add(entry);
        popup.Show("Контекст", $"\u201C{displayText}\u201D", "context",
            badge: _settings.ContextTag);

        popup.RemoveRequested += () =>
        {
            _contextMessages.RemoveAll(c => c.Popup == popup);
            Log.Information("[App] Context message removed, {Count} remaining", _contextMessages.Count);
        };
        popup.Closed += (_, _) =>
        {
            _contextMessages.RemoveAll(c => c.Popup == popup);
        };
        Log.Information("[App] Context messages: {Count}", _contextMessages.Count);
    }

    private void ClearContextMessages()
    {
        foreach (var (_, popup) in _contextMessages)
            popup?.Dismiss();
        _contextMessages.Clear();
    }

    private void DismissAllProcessingPopups()
    {
        while (_processingPopups.Count > 0)
            _processingPopups.Dequeue().Dismiss();
    }

    private NotificationPopup? DismissAllProcessingPopupsExceptLast()
    {
        while (_processingPopups.Count > 1)
            _processingPopups.Dequeue().Dismiss();
        return _processingPopups.Count > 0 ? _processingPopups.Dequeue() : null;
    }

    private void ShowProcessingPopup(string promptText)
    {
        if (!_settings.ShowPopup) return;

        var displayText = promptText;
        if (_settings.PopupMaxLength > 0 && displayText.Length > _settings.PopupMaxLength)
            displayText = displayText.Substring(0, _settings.PopupMaxLength) + "...";

        var popup = new NotificationPopup();
        popup.CancelRequested += () =>
        {
            Log.Information("[App] Cancel requested from popup");
            _cancelRequested = true;
            _ = _openCodeClient.AbortAsync();
        };

        _processingPopups.Enqueue(popup);
        popup.Show("Обработка...", $"\u201C{displayText}\u201D", "processing",
            badge: _settings.AgentID ?? "Build");
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
        RefreshTrayIcon();
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
        RefreshTrayIcon();
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
                bool workDirChanged = _settings.WorkDir != newSettings.WorkDir;
                bool wslChanged = _settings.UseWsl != newSettings.UseWsl ||
                                  _settings.WslDistro != newSettings.WslDistro;
                bool modelChanged = _settings.ProviderID != newSettings.ProviderID ||
                                    _settings.ModelID != newSettings.ModelID;

                _settings.VoicerWsPort = newSettings.VoicerWsPort;
                _settings.OpenCodePort = newSettings.OpenCodePort;
                _settings.AutoStartOpenCode = newSettings.AutoStartOpenCode;
                _settings.WorkDir = newSettings.WorkDir;
                _settings.UseWsl = newSettings.UseWsl;
                _settings.WslDistro = newSettings.WslDistro;
                _settings.ProviderID = newSettings.ProviderID;
                _settings.ModelID = newSettings.ModelID;
                _settings.AgentID = newSettings.AgentID;
                _settings.NewSessionTag = newSettings.NewSessionTag;
                _settings.ContextTag = newSettings.ContextTag;
                _settings.ShowPopup = newSettings.ShowPopup;
                _settings.PopupDurationSeconds = newSettings.PopupDurationSeconds;
                _settings.PopupMaxLength = newSettings.PopupMaxLength;

                if (wsPortChanged)
                {
                    _wsClient.Stop();
                    _wsClient.Start();
                }

                if (ocPortChanged || wslChanged || workDirChanged)
                {
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

                // Save after applying all changes
                _settings.Save();
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

    private enum TrayConnectionState { BothConnected, Partial, Disconnected }

    private WindowIcon CreateTrayIcon(TrayConnectionState state = TrayConnectionState.Disconnected)
    {
        const int size = 32;
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var bgColor = state == TrayConnectionState.Disconnected
            ? new SKColor(0x2D, 0x2D, 0x32)
            : new SKColor(0x1E, 0x3A, 0x5F);
        var textColor = state == TrayConnectionState.Disconnected
            ? new SKColor(0x6B, 0x72, 0x80)
            : new SKColor(0xE0, 0xE7, 0xEF);

        using var bgPaint = new SKPaint { Color = bgColor, IsAntialias = true };
        canvas.DrawCircle(size / 2f, size / 2f, size / 2f - 1, bgPaint);

        using var textPaint = new SKPaint
        {
            Color = textColor,
            IsAntialias = true,
            TextSize = 13,
            TextAlign = SKTextAlign.Center,
            FakeBoldText = true,
        };
        var textY = size / 2f + textPaint.TextSize / 3f;
        canvas.DrawText("OV", size / 2f, textY, textPaint);

        // Connection badge
        if (state is TrayConnectionState.BothConnected or TrayConnectionState.Partial)
        {
            var (badgeBg, badgeFg) = state == TrayConnectionState.BothConnected
                ? (new SKColor(0x1A, 0x3E, 0x30), new SKColor(0x4A, 0xDE, 0x80))
                : (new SKColor(0x4E, 0x34, 0x12), new SKColor(0xFB, 0xBF, 0x24));

            using var badgeBgPaint = new SKPaint { Color = badgeBg, IsAntialias = true };
            using var badgeBorderPaint = new SKPaint { Color = bgColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
            using var badgeDotPaint = new SKPaint { Color = badgeFg, IsAntialias = true };

            canvas.DrawCircle(26, 6, 5, badgeBgPaint);
            canvas.DrawCircle(26, 6, 5, badgeBorderPaint);
            canvas.DrawCircle(26, 6, 2.5f, badgeDotPaint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var stream = new MemoryStream(data.ToArray());
        return new WindowIcon(stream);
    }

    private TrayConnectionState GetConnectionState()
    {
        bool voicer = _wsClient.IsConnected;
        bool openCode = _eventService.IsConnected;
        if (voicer && openCode) return TrayConnectionState.BothConnected;
        if (voicer || openCode) return TrayConnectionState.Partial;
        return TrayConnectionState.Disconnected;
    }

    private void RefreshTrayIcon()
    {
        if (_trayIcon == null) return;
        _trayIcon.Icon = CreateTrayIcon(GetConnectionState());
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
