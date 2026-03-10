using Microsoft.Extensions.DependencyInjection;
using Voicer.Core.Interfaces;

namespace Voicer.Platform.Linux;

public static class LinuxPlatformRegistration
{
    public static IServiceCollection AddLinuxPlatform(IServiceCollection services)
    {
        services.AddSingleton<IHotkeyService, LinuxHotkeyService>();
        services.AddSingleton<IAudioCaptureService, LinuxAudioCaptureService>();
        services.AddSingleton<ITextInsertionService, LinuxTextInsertionService>();
        services.AddSingleton<IAutoStartService, LinuxAutoStartService>();
        services.AddSingleton<IPlatformInfo, LinuxPlatformInfo>();
        return services;
    }
}
