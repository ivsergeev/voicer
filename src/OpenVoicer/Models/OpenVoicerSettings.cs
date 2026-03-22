using System.IO;
using System.Text.Json;
using Serilog;

namespace OpenVoicer.Models;

public class OpenVoicerSettings
{
    public int VoicerWsPort { get; set; } = 5050;
    public int OpenCodePort { get; set; } = 4096;
    public bool AutoStartOpenCode { get; set; } = true;
    public bool UseWsl { get; set; } = false;
    public string WslDistro { get; set; } = "";
    public string WslWorkDir { get; set; } = "~";
    public string WorkDir { get; set; } = "";
    public string? ProviderID { get; set; }
    public string? ModelID { get; set; }
    public string AgentID { get; set; } = "build";
    public string NewSessionTag { get; set; } = "new-session";
    public string ContextTag { get; set; } = "context";
    public bool ShowPopup { get; set; } = true;
    public double PopupDurationSeconds { get; set; } = 4;
    public int PopupMaxLength { get; set; } = 100;

    private static readonly string SettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    public static OpenVoicerSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new OpenVoicerSettings();

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<OpenVoicerSettings>(json) ?? new OpenVoicerSettings();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Settings] Failed to load settings, using defaults");
            return new OpenVoicerSettings();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Settings] Failed to save settings to {Path}", SettingsPath);
        }
    }
}
