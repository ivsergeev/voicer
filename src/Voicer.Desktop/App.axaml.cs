using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Voicer.Core.Interfaces;
using Voicer.Core.Services;
using Voicer.Desktop.Views;

namespace Voicer.Desktop;

public partial class App : Application
{
    private AppOrchestrator _orchestrator = null!;
    private VoicerWebSocketServer _wsServer = null!;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _statusItem;
    private NativeMenuItem? _clientsItem;
    private ITrayIconGenerator _iconGenerator = null!;
    private WindowIcon? _currentIcon;
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
                _iconGenerator = Program.Services.GetRequiredService<ITrayIconGenerator>();
                _wsServer = Program.Services.GetRequiredService<VoicerWebSocketServer>();
                _orchestrator = Program.Services.GetRequiredService<AppOrchestrator>();

                // Subscribe to orchestrator events
                _orchestrator.StateChanged += OnStateChanged;
                _orchestrator.ClientCountChanged += OnClientCountChanged;
                _orchestrator.TranscriptionReady += OnTranscriptionReady;
                _orchestrator.ErrorOccurred += OnErrorOccurred;
                _orchestrator.ActiveClientChanged += OnActiveClientChanged;
                InitializeTrayIcon();
                _orchestrator.Initialize();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FATAL: OnFrameworkInitializationCompleted failed: {ex}");
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InitializeTrayIcon()
    {
        try
        {
            var menu = new NativeMenu();

            _statusItem = new NativeMenuItem("Статус: Ожидание") { IsEnabled = false };
            menu.Items.Add(_statusItem);
            menu.Items.Add(new NativeMenuItemSeparator());

            _clientsItem = new NativeMenuItem("Клиенты: 0") { IsEnabled = false };
            menu.Items.Add(_clientsItem);
            menu.Items.Add(new NativeMenuItemSeparator());

            var settingsItem = new NativeMenuItem("Настройки...");
            settingsItem.Click += (_, _) => ShowSettings();
            menu.Items.Add(settingsItem);

            var exitItem = new NativeMenuItem("Выход");
            exitItem.Click += (_, _) => ExitApplication();
            menu.Items.Add(exitItem);

            Console.WriteLine("Creating tray icon...");
            _trayIcon = new TrayIcon
            {
                Icon = CreateWindowIcon("idle"),
                ToolTipText = "Voicer — Ожидание",
                Menu = menu,
                IsVisible = true
            };
            Console.WriteLine("Tray icon created successfully.");

            _trayIcon.Clicked += (_, _) => ShowSettings();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL: InitializeTrayIcon failed: {ex}");
        }
    }

    private WindowIcon? CreateWindowIcon(string iconType)
    {
        try
        {
            var stream = _iconGenerator.CreateIconStream(iconType);
            return new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: CreateWindowIcon('{iconType}') failed: {ex}");
            return null;
        }
    }

    private void OnStateChanged(string status, string iconType)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_trayIcon != null)
                {
                    _trayIcon.ToolTipText = $"Voicer — {status}";
                    var icon = CreateWindowIcon(iconType);
                    if (icon != null)
                    {
                        var old = _currentIcon;
                        _currentIcon = icon;
                        _trayIcon.Icon = icon;
                        (old as IDisposable)?.Dispose();
                    }
                }

                if (_statusItem != null)
                    _statusItem.Header = $"Статус: {status}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: OnStateChanged('{status}', '{iconType}') failed: {ex}");
            }
        });
    }

    private void OnClientCountChanged(int count)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                Console.WriteLine($"[WS] Clients connected: {count}");
                if (_clientsItem != null)
                    _clientsItem.Header = $"Клиенты: {count}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: OnClientCountChanged({count}) failed: {ex}");
            }
        });
    }

    private void OnTranscriptionReady(string text, string? context, string? tag, string mode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (!_orchestrator.Settings.ShowPopup) return;

                // Check if there are active WS clients (for non-insert modes)
                bool isWsMode = mode is "ws" or "ws_sel" or "ws_tag";
                string effectiveMode = mode;
                string? infoMessage = null;
                double duration = _orchestrator.Settings.PopupDurationSeconds;

                if (isWsMode && !_wsServer.HasActiveClient)
                {
                    effectiveMode = "no_clients";
                    infoMessage = "Нет подключённых WS-клиентов";
                    duration = 6; // longer for "not delivered"
                }

                var popup = new TranscriptionPopup();
                popup.Show(text, context, tag, effectiveMode, duration,
                    _orchestrator.Settings.PopupMaxLength, infoMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: OnTranscriptionReady failed: {ex}");
            }
        });
    }

    private void OnActiveClientChanged(bool hasActive)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var msg = hasActive ? "Voice target: active client connected" : "Voice target: no active client";
                Console.WriteLine($"[WS] {msg}");

                if (_orchestrator.Settings.ShowPopup)
                {
                    var popup = new TranscriptionPopup();
                    popup.Show(msg, null, null, hasActive ? "client_connected" : "client_disconnected", _orchestrator.Settings.PopupDurationSeconds);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: OnActiveClientChanged failed: {ex}");
            }
        });
    }

    private void OnErrorOccurred(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Console.WriteLine($"ERROR: {message}");
        });
    }

    private void ShowSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;

        try
        {
            _orchestrator.PauseForSettings();

            var window = new SettingsWindow(_orchestrator.Settings,
                Program.Services.GetRequiredService<IAudioCaptureService>(),
                Program.Services.GetRequiredService<IAutoStartService>(),
                Program.Services.GetRequiredService<IPlatformInfo>());

            window.SettingsChanged += newSettings =>
            {
                _orchestrator.ApplySettings(newSettings);
            };

            window.Closed += (_, _) =>
            {
                _settingsOpen = false;
                _orchestrator.ResumeAfterSettings();
            };

            window.Show();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: ShowSettings failed: {ex}");
            _settingsOpen = false;
        }
    }

    private void ExitApplication()
    {
        try
        {
            Console.WriteLine("Shutting down...");
            _orchestrator.Shutdown();
            _trayIcon?.Dispose();
            _trayIcon = null;

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: ExitApplication failed: {ex}");
        }
    }
}
