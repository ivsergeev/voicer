using Voicer.Core.Interfaces;

namespace Voicer.Platform.Linux;

/// <summary>
/// Linux autostart via XDG .desktop file in ~/.config/autostart/.
/// </summary>
public class LinuxAutoStartService : IAutoStartService
{
    private static string DesktopFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "autostart", "voicer.desktop");

    public bool IsEnabled() => File.Exists(DesktopFilePath);

    public void SetEnabled(bool enable)
    {
        if (enable)
        {
            var dir = Path.GetDirectoryName(DesktopFilePath)!;
            Directory.CreateDirectory(dir);

            var exePath = Environment.ProcessPath ?? "";
            var content = $"""
            [Desktop Entry]
            Type=Application
            Name=Voicer
            Exec={exePath}
            X-GNOME-Autostart-enabled=true
            """;
            File.WriteAllText(DesktopFilePath, content);
        }
        else
        {
            if (File.Exists(DesktopFilePath))
                File.Delete(DesktopFilePath);
        }
    }
}
