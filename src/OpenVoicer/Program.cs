using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using OpenVoicer.Models;
using OpenVoicer.Services;
using Serilog;

namespace OpenVoicer;

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

        Log.Information("=== OpenVoicer starting ===");
        Log.Information("  Version: {Version}", typeof(Program).Assembly.GetName().Version);
        Log.Information("  Runtime: {Runtime}", Environment.Version);
        Log.Information("  OS: {OS}", Environment.OSVersion);
        Log.Information("  BaseDir: {BaseDir}", AppDomain.CurrentDomain.BaseDirectory);
        Log.Information("  Args: [{Args}]", string.Join(", ", args));

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal("UNHANDLED EXCEPTION: {Exception}", e.ExceptionObject);
            Log.CloseAndFlush();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Log.Information("=== OpenVoicer process exit ===");
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

            services.AddSingleton<OpenVoicerSettings>(_ => OpenVoicerSettings.Load());
            services.AddSingleton<VoicerWsClient>();
            services.AddSingleton<OpenCodeProcessManager>();
            services.AddSingleton<OpenCodeClient>();
            services.AddSingleton<OpenCodeEventService>();

            Services = services.BuildServiceProvider();
            Log.Information("  DI container built.");

            Log.Information("  Starting Avalonia...");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            Log.Information("=== OpenVoicer exited normally ===");
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
