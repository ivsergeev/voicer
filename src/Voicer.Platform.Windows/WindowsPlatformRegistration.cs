using Microsoft.Extensions.DependencyInjection;
using Voicer.Core.Interfaces;

namespace Voicer.Platform.Windows;

public static class WindowsPlatformRegistration
{
    public static IServiceCollection AddWindowsPlatform(IServiceCollection services)
    {
        services.AddSingleton<IHotkeyService, WindowsHotkeyService>();
        services.AddSingleton<IAudioCaptureService, WindowsAudioCaptureService>();
        services.AddSingleton<ITextInsertionService, WindowsTextInsertionService>();
        services.AddSingleton<IAutoStartService, WindowsAutoStartService>();
        services.AddSingleton<IPlatformInfo, WindowsPlatformInfo>();
        return services;
    }
}
