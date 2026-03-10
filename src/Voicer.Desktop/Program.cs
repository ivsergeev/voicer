using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Voicer.Core.Interfaces;
using Voicer.Core.Services;

namespace Voicer.Desktop;

class Program
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static FileLogger Logger { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        // Initialize file logger first — before anything else
        Logger = new FileLogger();
        Logger.RedirectConsole();

        Console.WriteLine("=== Voicer starting ===");
        Console.WriteLine($"  Version: {typeof(Program).Assembly.GetName().Version}");
        Console.WriteLine($"  Runtime: {Environment.Version}");
        Console.WriteLine($"  OS: {Environment.OSVersion}");
        Console.WriteLine($"  BaseDir: {AppDomain.CurrentDomain.BaseDirectory}");
        Console.WriteLine($"  Args: [{string.Join(", ", args)}]");

        // Global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.WriteLine($"FATAL UNHANDLED EXCEPTION: {e.ExceptionObject}");
            Logger.Dispose();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Console.WriteLine("=== ProcessExit event fired ===");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.WriteLine($"UNOBSERVED TASK EXCEPTION: {e.Exception}");
            e.SetObserved();
        };

        try
        {
            var services = new ServiceCollection();

            // Register platform services
#if PLATFORM_WINDOWS
            Console.WriteLine("  Platform: Windows");
            Voicer.Platform.Windows.WindowsPlatformRegistration.AddWindowsPlatform(services);
#elif PLATFORM_MACOS
            Console.WriteLine("  Platform: macOS");
            Voicer.Platform.macOS.MacPlatformRegistration.AddMacPlatform(services);
#elif PLATFORM_LINUX
            Console.WriteLine("  Platform: Linux");
            Voicer.Platform.Linux.LinuxPlatformRegistration.AddLinuxPlatform(services);
#endif

            // Register desktop services (shared across platforms)
            services.AddSingleton<ITrayIconGenerator, Voicer.Desktop.Services.SkiaTrayIconGenerator>();

            // Register core services
            services.AddSingleton<SpeechRecognitionService>();
            services.AddSingleton<VoicerWebSocketServer>();
            services.AddSingleton<AppOrchestrator>();

            Services = services.BuildServiceProvider();
            Console.WriteLine("  DI container built.");

            Console.WriteLine("  Starting Avalonia...");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            Console.WriteLine("=== Voicer exited normally ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL EXCEPTION in Main: {ex}");
            throw;
        }
        finally
        {
            Logger.Dispose();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
