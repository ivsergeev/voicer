using System.IO;
using System.Text.Json;

namespace Voicer.Core.Models;

public class AppSettings
{
    public string ModelDirectory { get; set; } = "models";
    public string ModelFileName { get; set; } = "v3_e2e_ctc.int8.onnx";
    public string TokensFileName { get; set; } = "v3_e2e_ctc_vocab.txt";

    public int WebSocketPort { get; set; } = 5050;
    public int HotkeyModifiers { get; set; } = 0; // MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4, MOD_WIN=8
    public int HotkeyKey { get; set; } = 0x76; // F7 by default (VK_F7)
    public int InsertHotkeyModifiers { get; set; } = 0;
    public int InsertHotkeyKey { get; set; } = 0x75; // F6 by default (VK_F6)
    public int SelectionHotkeyModifiers { get; set; } = 0;
    public int SelectionHotkeyKey { get; set; } = 0x77; // F8 by default (VK_F8)
    public int ModelThreads { get; set; } = 4;
    public string? MicrophoneDeviceId { get; set; }
    public bool ShowPopup { get; set; } = true;
    public bool NormalizeAudio { get; set; } = true;

    private static readonly string SettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        var json = File.ReadAllText(SettingsPath);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    public string GetModelPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDir, ModelDirectory, ModelFileName);
    }

    public string GetTokensPath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDir, ModelDirectory, TokensFileName);
    }

}
