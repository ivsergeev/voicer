using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Voicer.Core.Interfaces;
using Voicer.Desktop.Views;

namespace Voicer.Desktop;

public partial class App : Application
{
    private AppOrchestrator _orchestrator = null!;
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
                _orchestrator = Program.Services.GetRequiredService<AppOrchestrator>();

                // Subscribe to orchestrator events
                _orchestrator.StateChanged += OnStateChanged;
                _orchestrator.ClientCountChanged += OnClientCountChanged;
                _orchestrator.TranscriptionReady += OnTranscriptionReady;
                _orchestrator.ErrorOccurred += OnErrorOccurred;
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

            _statusItem = new NativeMenuItem("Status: Idle") { IsEnabled = false };
            menu.Items.Add(_statusItem);
            menu.Items.Add(new NativeMenuItemSeparator());

            _clientsItem = new NativeMenuItem("Clients: 0") { IsEnabled = false };
            menu.Items.Add(_clientsItem);
            menu.Items.Add(new NativeMenuItemSeparator());

            var settingsItem = new NativeMenuItem("Settings...");
            settingsItem.Click += (_, _) => ShowSettings();
            menu.Items.Add(settingsItem);

            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += (_, _) => ExitApplication();
            menu.Items.Add(exitItem);

            Console.WriteLine("Creating tray icon...");
            _trayIcon = new TrayIcon
            {
                Icon = CreateWindowIcon("idle"),
                ToolTipText = "Voicer - Idle",
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
                    _trayIcon.ToolTipText = $"Voicer - {status}";
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
                    _statusItem.Header = $"Status: {status}";
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
                    _clientsItem.Header = $"Clients: {count}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: OnClientCountChanged({count}) failed: {ex}");
            }
        });
    }

    private void OnTranscriptionReady(string text, string mode)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_orchestrator.Settings.ShowPopup)
                {
                    var popup = new TranscriptionPopup();
                    popup.Show(text, mode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: OnTranscriptionReady failed: {ex}");
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
