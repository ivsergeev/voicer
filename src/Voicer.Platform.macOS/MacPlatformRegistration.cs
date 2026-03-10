using Microsoft.Extensions.DependencyInjection;
using Voicer.Core.Interfaces;

namespace Voicer.Platform.macOS;

public static class MacPlatformRegistration
{
    public static IServiceCollection AddMacPlatform(IServiceCollection services)
    {
        services.AddSingleton<IHotkeyService, MacHotkeyService>();
        services.AddSingleton<IAudioCaptureService, MacAudioCaptureService>();
        services.AddSingleton<ITextInsertionService, MacTextInsertionService>();
        services.AddSingleton<IAutoStartService, MacAutoStartService>();
        services.AddSingleton<IPlatformInfo, MacPlatformInfo>();
        return services;
    }
}
