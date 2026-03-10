using System.IO;
using Voicer.Core.Interfaces;
using Voicer.Core.Models;
using Voicer.Core.Services;

namespace Voicer.Desktop;

public class AppOrchestrator : IDisposable
{
    private readonly IHotkeyService _hotkeyService;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly SpeechRecognitionService _speechService;
    private readonly VoicerWebSocketServer _wsServer;
    private readonly ITextInsertionService _textInsertionService;
    private readonly IPlatformInfo _platformInfo;

    private AppSettings _settings = null!;
    private AppState _state = AppState.Idle;
    private bool _insertMode;
    private bool _paused;

    public AppSettings Settings => _settings;

    public enum AppState { Idle, Recording, Processing }

    // Events for UI layer
    public event Action<string, string>? StateChanged;         // (status, iconType)
    public event Action<int>? ClientCountChanged;
    public event Action<string, bool>? TranscriptionReady;     // (text, insertMode)
    public event Action<string>? ErrorOccurred;
    // Clipboard is now handled directly via ITextInsertionService

    public AppOrchestrator(
        IHotkeyService hotkeyService,
        IAudioCaptureService audioCaptureService,
        SpeechRecognitionService speechService,
        VoicerWebSocketServer wsServer,
        ITextInsertionService textInsertionService,
        IPlatformInfo platformInfo)
    {
        _hotkeyService = hotkeyService;
        _audioCaptureService = audioCaptureService;
        _speechService = speechService;
        _wsServer = wsServer;
        _textInsertionService = textInsertionService;
        _platformInfo = platformInfo;
    }

    public void Initialize()
    {
        _settings = AppSettings.Load();

        // WebSocket server
        _wsServer.ClientCountChanged += count => ClientCountChanged?.Invoke(count);

        try
        {
            _wsServer.Start(_settings.WebSocketPort);
            Console.WriteLine($"WebSocket server started on ws://0.0.0.0:{_settings.WebSocketPort}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Failed to start WebSocket server: {ex.Message}");
            ErrorOccurred?.Invoke($"Failed to start WebSocket server on port {_settings.WebSocketPort}:\n{ex.Message}");
        }

        // Audio capture
        var mics = _audioCaptureService.GetMicrophoneDevices();
        Console.WriteLine($"Microphones found: {mics.Count}");
        foreach (var (id, name) in mics)
        {
            bool selected = id == _settings.MicrophoneDeviceId;
            Console.WriteLine($"  {name}{(selected ? " (selected)" : "")}");
        }

        if (mics.Count == 0)
            Console.WriteLine("WARNING: No microphone devices found!");
        else if (string.IsNullOrEmpty(_settings.MicrophoneDeviceId))
            Console.WriteLine("  Using default capture device.");

        _audioCaptureService.DeviceId = _settings.MicrophoneDeviceId;

        // Speech recognition
        InitializeSpeechModel();

        // Hotkey (push-to-talk)
        _hotkeyService.KeyPressed += OnHotkeyPressed;
        _hotkeyService.KeyReleased += OnHotkeyReleased;
        _hotkeyService.InsertKeyPressed += OnInsertHotkeyPressed;
        _hotkeyService.InsertKeyReleased += OnInsertHotkeyReleased;

        try
        {
            _hotkeyService.Start(_settings.HotkeyModifiers, _settings.HotkeyKey,
                _settings.InsertHotkeyModifiers, _settings.InsertHotkeyKey);

            if (!_hotkeyService.IsAvailable)
            {
                Console.WriteLine("ERROR: Hotkey service is not available.");
                ErrorOccurred?.Invoke(
                    "Global hotkeys are not available.\n" +
                    "This usually happens on Linux with Wayland (X11 is required).\n" +
                    "Try launching with: GDK_BACKEND=x11 ./Voicer");
            }
            else
            {
                Console.WriteLine($"Push-to-talk hotkey (WS): {_platformInfo.GetHotkeyDisplayName(_settings.HotkeyModifiers, _settings.HotkeyKey)}");
                Console.WriteLine($"Push-to-talk hotkey (Insert): {_platformInfo.GetHotkeyDisplayName(_settings.InsertHotkeyModifiers, _settings.InsertHotkeyKey)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Failed to register hotkey: {ex.Message}");
            ErrorOccurred?.Invoke($"Failed to register hotkey:\n{ex.Message}");
        }

        Console.WriteLine("Ready. Press hotkey to start recording.");
    }

    private void InitializeSpeechModel()
    {
        var modelPath = _settings.GetModelPath();
        var tokensPath = _settings.GetTokensPath();

        if (!File.Exists(modelPath) || !File.Exists(tokensPath))
        {
            StateChanged?.Invoke("No model", "no_model");
            Console.WriteLine("WARNING: Model files not found.");
            Console.WriteLine($"  Expected: {modelPath}");
            Console.WriteLine($"  Expected: {tokensPath}");
            Console.WriteLine("  Run: powershell ./scripts/download-model.ps1");
            return;
        }

        Console.WriteLine("Loading speech model...");

        Task.Run(() =>
        {
            try
            {
                StateChanged?.Invoke("Loading model...", "processing_ws");
                _speechService.Initialize(modelPath, tokensPath, _settings.ModelThreads);
                Console.WriteLine("Speech model loaded (e2e with punctuation).");

                StateChanged?.Invoke("Idle", "idle");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Failed to load speech model: {ex.Message}");
                StateChanged?.Invoke("Model error", "no_model");
                ErrorOccurred?.Invoke($"Failed to load speech model:\n{ex.Message}");
            }
        });
    }

    private void StartRecording(bool insertMode)
    {
        if (_paused) return;
        if (_state != AppState.Idle) return;
        if (!_speechService.IsInitialized) return;

        _insertMode = insertMode;
        _state = AppState.Recording;
        _audioCaptureService.StartRecording();
        if (!insertMode) _wsServer.BroadcastStatus("recording");

        var iconType = insertMode ? "recording_insert" : "recording_ws";
        var status = insertMode ? "Recording (insert)" : "Recording (ws)";
        StateChanged?.Invoke(status, iconType);
        Console.WriteLine($"[REC] Recording started ({(insertMode ? "insert" : "ws")})...");
    }

    private void StopRecordingAndProcess()
    {
        if (_state != AppState.Recording) return;

        var insertMode = _insertMode;
        _state = AppState.Processing;
        _audioCaptureService.StopRecording();

        var iconType = insertMode ? "processing_insert" : "processing_ws";
        StateChanged?.Invoke("Processing", iconType);
        if (!insertMode) _wsServer.BroadcastStatus("processing");
        Console.WriteLine("[REC] Recording stopped. Processing...");

        var samples = _audioCaptureService.GetRecordedSamples();
        var durationSec = samples.Length / 16000.0;
        float maxAbs = 0, sumSq = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float abs = Math.Abs(samples[i]);
            if (abs > maxAbs) maxAbs = abs;
            sumSq += samples[i] * samples[i];
        }
        float rms = samples.Length > 0 ? (float)Math.Sqrt(sumSq / samples.Length) : 0;
        Console.WriteLine($"  Audio: {durationSec:F1}s, {samples.Length} samples, rms={rms:F4}, peak={maxAbs:F4}");

        Task.Run(async () =>
        {
            try
            {
                var text = _speechService.Recognize(samples);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine($"  >>> {text}");

                    if (insertMode)
                    {
                        await PasteTextAtCursor(text);
                    }
                    else
                    {
                        _wsServer.BroadcastTranscription(text);
                    }

                    TranscriptionReady?.Invoke(text, insertMode);
                }
                else
                {
                    Console.WriteLine("  (empty result)");
                }

                _state = AppState.Idle;
                if (!insertMode) _wsServer.BroadcastStatus("idle");
                StateChanged?.Invoke("Idle", "idle");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ERROR: {ex.Message}");
                if (!insertMode) _wsServer.BroadcastError(ex.Message);
                _state = AppState.Idle;
                if (!insertMode) _wsServer.BroadcastStatus("idle");
                StateChanged?.Invoke("Error", "no_model");
            }
        });
    }

    private async Task PasteTextAtCursor(string text)
    {
        // Save current clipboard
        string? savedClipboard = null;
        try { savedClipboard = await _textInsertionService.GetClipboardText(); }
        catch (Exception ex) { Console.WriteLine($"[Paste] Warning: failed to save clipboard: {ex.Message}"); }

        // Set recognized text to clipboard
        await _textInsertionService.SetClipboardText(text);

        // Simulate Ctrl+V / Cmd+V
        await _textInsertionService.SimulatePaste();

        // Wait for target app to consume the paste
        await Task.Delay(500);

        // Restore previous clipboard content with retry
        if (savedClipboard != null)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await _textInsertionService.SetClipboardText(savedClipboard);
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Paste] Warning: clipboard restore attempt {attempt}/3 failed: {ex.Message}");
                    if (attempt < 3) await Task.Delay(100);
                }
            }
        }
    }

    private void OnHotkeyPressed() => StartRecording(insertMode: false);
    private void OnHotkeyReleased() => StopRecordingAndProcess();
    private void OnInsertHotkeyPressed() => StartRecording(insertMode: true);
    private void OnInsertHotkeyReleased() => StopRecordingAndProcess();

    public void PauseForSettings()
    {
        _paused = true;

        // Force idle if recording was in progress
        if (_state == AppState.Recording)
        {
            _audioCaptureService.StopRecording();
            _state = AppState.Idle;
            _wsServer.BroadcastStatus("idle");
            StateChanged?.Invoke("Idle", "idle");
        }
    }

    public void ApplySettings(AppSettings newSettings)
    {
        _settings = newSettings;
        _settings.Save();

        _audioCaptureService.DeviceId = _settings.MicrophoneDeviceId;
        _hotkeyService.UpdateHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
        _hotkeyService.UpdateInsertHotkey(_settings.InsertHotkeyModifiers, _settings.InsertHotkeyKey);

        _paused = false;
    }

    public void Shutdown()
    {
        _hotkeyService.Dispose();
        _audioCaptureService.Dispose();
        _speechService.Dispose();
        _wsServer.Dispose();
    }

    public void Dispose()
    {
        Shutdown();
    }
}
