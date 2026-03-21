using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Voicer.Core.Models;

public enum WsActionType
{
    TranscribeAndSend,
    TranscribeWithContext,
    SendTag,
}

public class HotkeyAction
{
    public int Modifiers { get; set; }
    public int KeyCode { get; set; }
    public WsActionType Action { get; set; }
    public string? Tag { get; set; }
    public string? Label { get; set; }
}

public class AppSettings
{
    public string ModelDirectory { get; set; } = "models";
    public string ModelFileName { get; set; } = "v3_e2e_ctc.int8.onnx";
    public string TokensFileName { get; set; } = "v3_e2e_ctc_vocab.txt";

    public int WebSocketPort { get; set; } = 5050;

    // Insert hotkey (F6) — unchanged
    public int InsertHotkeyModifiers { get; set; } = 0;
    public int InsertHotkeyKey { get; set; } = 0x75; // F6 by default (VK_F6)

    // Dynamic WS hotkey actions (replaces old HotkeyModifiers/HotkeyKey + SelectionHotkeyModifiers/SelectionHotkeyKey)
    public List<HotkeyAction> WsHotkeyActions { get; set; } = new()
    {
        new HotkeyAction { Modifiers = 0, KeyCode = 0x76, Action = WsActionType.TranscribeAndSend, Label = "Текст → WS" },
        new HotkeyAction { Modifiers = 0, KeyCode = 0x77, Action = WsActionType.TranscribeWithContext, Label = "Текст + контекст → WS" },
    };

    public int ModelThreads { get; set; } = 4;
    public string? MicrophoneDeviceId { get; set; }
    public bool ShowPopup { get; set; } = true;
    public double PopupDurationSeconds { get; set; } = 4;
    public int PopupMaxLength { get; set; } = 100;
    public bool NormalizeAudio { get; set; } = true;

    private static readonly string SettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
            return new AppSettings();

        var json = File.ReadAllText(SettingsPath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

        // Migration from v1: if old fields are present and WsHotkeyActions is at default,
        // check if the JSON contains old-style hotkey fields
        MigrateFromV1(json, settings);

        return settings;
    }

    private static void MigrateFromV1(string json, AppSettings settings)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // If old fields exist and WsHotkeyActions was not explicitly set in JSON
            bool hasOldFields = root.TryGetProperty("HotkeyKey", out _) ||
                                root.TryGetProperty("SelectionHotkeyKey", out _);
            bool hasNewField = root.TryGetProperty("WsHotkeyActions", out _);

            if (hasOldFields && !hasNewField)
            {
                var actions = new List<HotkeyAction>();

                if (root.TryGetProperty("HotkeyKey", out var hkKey))
                {
                    int modifiers = root.TryGetProperty("HotkeyModifiers", out var hkMod) ? hkMod.GetInt32() : 0;
                    actions.Add(new HotkeyAction
                    {
                        Modifiers = modifiers,
                        KeyCode = hkKey.GetInt32(),
                        Action = WsActionType.TranscribeAndSend,
                        Label = "Текст → WS",
                    });
                }

                if (root.TryGetProperty("SelectionHotkeyKey", out var selKey))
                {
                    int modifiers = root.TryGetProperty("SelectionHotkeyModifiers", out var selMod) ? selMod.GetInt32() : 0;
                    actions.Add(new HotkeyAction
                    {
                        Modifiers = modifiers,
                        KeyCode = selKey.GetInt32(),
                        Action = WsActionType.TranscribeWithContext,
                        Label = "Текст + контекст → WS",
                    });
                }

                if (actions.Count > 0)
                {
                    settings.WsHotkeyActions = actions;
                }
            }
        }
        catch
        {
            // Migration best-effort
        }
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
