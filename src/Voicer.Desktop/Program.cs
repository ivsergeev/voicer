using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Voicer.Core.Interfaces;
using Voicer.Core.Services;

namespace Voicer.Desktop;

class Program
{
    public static IServiceProvider Services { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(logDir, "log.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 5,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                fileSizeLimitBytes: 2 * 1024 * 1024,
                rollOnFileSizeLimit: true)
            .CreateLogger();

        Log.Information("=== Voicer starting ===");
        Log.Information("  Version: {Version}", typeof(Program).Assembly.GetName().Version);
        Log.Information("  Runtime: {Runtime}", Environment.Version);
        Log.Information("  OS: {OS}", Environment.OSVersion);
        Log.Information("  BaseDir: {BaseDir}", AppDomain.CurrentDomain.BaseDirectory);
        Log.Information("  Args: [{Args}]", string.Join(", ", args));

        // Global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal("UNHANDLED EXCEPTION: {Exception}", e.ExceptionObject);
            Log.CloseAndFlush();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Log.Information("=== Voicer process exit ===");
            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "UNOBSERVED TASK EXCEPTION");
            e.SetObserved();
        };

        try
        {
            var services = new ServiceCollection();

            // Register platform services
#if PLATFORM_WINDOWS
            Log.Information("  Platform: Windows");
            Voicer.Platform.Windows.WindowsPlatformRegistration.AddWindowsPlatform(services);
#elif PLATFORM_MACOS
            Log.Information("  Platform: macOS");
            Voicer.Platform.macOS.MacPlatformRegistration.AddMacPlatform(services);
#elif PLATFORM_LINUX
            Log.Information("  Platform: Linux");
            Voicer.Platform.Linux.LinuxPlatformRegistration.AddLinuxPlatform(services);
#endif

            // Register desktop services (shared across platforms)
            services.AddSingleton<ITrayIconGenerator, Voicer.Desktop.Services.SkiaTrayIconGenerator>();

            // Register core services
            services.AddSingleton<SpeechRecognitionService>();
            services.AddSingleton<VoicerWebSocketServer>();
            services.AddSingleton<AppOrchestrator>();

            Services = services.BuildServiceProvider();
            Log.Information("  DI container built.");

            Log.Information("  Starting Avalonia...");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            Log.Information("=== Voicer exited normally ===");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "FATAL EXCEPTION in Main");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
