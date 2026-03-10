using Voicer.Core.Interfaces;

namespace Voicer.Platform.macOS;

/// <summary>
/// macOS autostart via LaunchAgent plist in ~/Library/LaunchAgents/.
/// </summary>
public class MacAutoStartService : IAutoStartService
{
    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", "com.voicer.app.plist");

    public bool IsEnabled() => File.Exists(PlistPath);

    public void SetEnabled(bool enable)
    {
        if (enable)
        {
            var dir = Path.GetDirectoryName(PlistPath)!;
            Directory.CreateDirectory(dir);

            var exePath = Environment.ProcessPath ?? "";
            var plist = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Label</key><string>com.voicer.app</string>
                <key>ProgramArguments</key><array><string>{exePath}</string></array>
                <key>RunAtLoad</key><true/>
            </dict>
            </plist>
            """;
            File.WriteAllText(PlistPath, plist);
        }
        else
        {
            if (File.Exists(PlistPath))
                File.Delete(PlistPath);
        }
    }
}
