using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using OpenVoicer.Models;
using OpenVoicer.Services;

namespace OpenVoicer;

class Program
{
    public static IServiceProvider Services { get; private set; } = null!;

    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine("=== OpenVoicer starting ===");
        Console.WriteLine($"  Version: {typeof(Program).Assembly.GetName().Version}");
        Console.WriteLine($"  Runtime: {Environment.Version}");
        Console.WriteLine($"  OS: {Environment.OSVersion}");
        Console.WriteLine($"  BaseDir: {AppDomain.CurrentDomain.BaseDirectory}");
        Console.WriteLine($"  Args: [{string.Join(", ", args)}]");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Console.WriteLine($"FATAL UNHANDLED EXCEPTION: {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.WriteLine($"UNOBSERVED TASK EXCEPTION: {e.Exception}");
            e.SetObserved();
        };

        try
        {
            var services = new ServiceCollection();

            services.AddSingleton<OpenVoicerSettings>(_ => OpenVoicerSettings.Load());
            services.AddSingleton<VoicerWsClient>();
            services.AddSingleton<OpenCodeProcessManager>();
            services.AddSingleton<OpenCodeClient>();
            services.AddSingleton<OpenCodeEventService>();

            Services = services.BuildServiceProvider();
            Console.WriteLine("  DI container built.");

            Console.WriteLine("  Starting Avalonia...");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            Console.WriteLine("=== OpenVoicer exited normally ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FATAL EXCEPTION in Main: {ex}");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
