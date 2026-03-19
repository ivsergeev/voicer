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
    private volatile AppState _state = AppState.Idle;
    private bool _insertMode;
    private bool _selectionMode;
    private volatile bool _paused;
    private string? _selectedText;
    private volatile int _wsClientCount;

    public AppSettings Settings => _settings;

    public enum AppState { Idle, Recording, Processing }

    // Events for UI layer
    public event Action<string, string>? StateChanged;         // (status, iconType)
    public event Action<int>? ClientCountChanged;
    public event Action<string, string>? TranscriptionReady;    // (text, mode: "insert"|"ws"|"ws_sel")
    public event Action<string>? ErrorOccurred;
    public event Action<bool>? ActiveClientChanged;            // true = has active client
    public event Action<string, string?>? ClientAckReceived;   // (status, message?) from WS client
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
        _wsServer.ClientCountChanged += count =>
        {
            _wsClientCount = count;
            ClientCountChanged?.Invoke(count);

            // Update idle icon when client count crosses 0 boundary
            if (_state == AppState.Idle)
            {
                var icon = count > 0 ? "idle" : "idle_no_clients";
                var status = count > 0 ? "Idle" : "Idle (no clients)";
                StateChanged?.Invoke(status, icon);
            }
        };
        _wsServer.ActiveClientChanged += hasActive => ActiveClientChanged?.Invoke(hasActive);
        _wsServer.ClientAckReceived += (status, msg) => ClientAckReceived?.Invoke(status, msg);

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
        _audioCaptureService.NormalizeAudio = _settings.NormalizeAudio;

        // Speech recognition
        InitializeSpeechModel();

        // Hotkey (push-to-talk)
        _hotkeyService.KeyPressed += OnHotkeyPressed;
        _hotkeyService.KeyReleased += OnHotkeyReleased;
        _hotkeyService.InsertKeyPressed += OnInsertHotkeyPressed;
        _hotkeyService.InsertKeyReleased += OnInsertHotkeyReleased;
        _hotkeyService.SelectionKeyPressed += OnSelectionHotkeyPressed;
        _hotkeyService.SelectionKeyReleased += OnSelectionHotkeyReleased;

        try
        {
            _hotkeyService.Start(_settings.HotkeyModifiers, _settings.HotkeyKey,
                _settings.InsertHotkeyModifiers, _settings.InsertHotkeyKey,
                _settings.SelectionHotkeyModifiers, _settings.SelectionHotkeyKey);

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
                Console.WriteLine($"Push-to-talk hotkey (WS+Selection): {_platformInfo.GetHotkeyDisplayName(_settings.SelectionHotkeyModifiers, _settings.SelectionHotkeyKey)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Failed to register hotkey: {ex.Message}");
            ErrorOccurred?.Invoke($"Failed to register hotkey:\n{ex.Message}");
        }

        Console.WriteLine("Ready. Press hotkey to start recording.");
    }

    private string IdleIconType => _wsClientCount > 0 ? "idle" : "idle_no_clients";
    private string IdleStatus => _wsClientCount > 0 ? "Idle" : "Idle (no clients)";

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

                StateChanged?.Invoke(IdleStatus, IdleIconType);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: Failed to load speech model: {ex.Message}");
                StateChanged?.Invoke("Model error", "no_model");
                ErrorOccurred?.Invoke($"Failed to load speech model:\n{ex.Message}");
            }
        });
    }

    private async Task CaptureSelectedText()
    {
        const int pollIntervalMs = 15;
        const int pollTimeoutMs = 500;

        string? savedClipboard = null;
        try
        {
            savedClipboard = await _textInsertionService.GetClipboardText();

            // Clear clipboard so we can detect when Ctrl+C writes new content
            await _textInsertionService.SetClipboardText("");

            await _textInsertionService.SimulateCopy();

            // Poll clipboard until content appears or timeout
            string? captured = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < pollTimeoutMs)
            {
                await Task.Delay(pollIntervalMs);
                captured = await _textInsertionService.GetClipboardText();
                if (!string.IsNullOrEmpty(captured))
                    break;
            }

            if (!string.IsNullOrEmpty(captured))
            {
                _selectedText = captured;
                Console.WriteLine($"  [SELECTION] Captured in {sw.ElapsedMilliseconds}ms: {_selectedText.Substring(0, Math.Min(_selectedText.Length, 80))}...");
            }
            else if (!string.IsNullOrEmpty(savedClipboard))
            {
                _selectedText = savedClipboard;
                Console.WriteLine($"  [SELECTION] No selection, using clipboard: {_selectedText.Substring(0, Math.Min(_selectedText.Length, 80))}...");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [SELECTION] Failed to capture: {ex.Message}");
            _selectedText = null;
        }
        finally
        {
            // Restore original clipboard
            if (savedClipboard != null)
            {
                try { await _textInsertionService.SetClipboardText(savedClipboard); }
                catch { /* best effort */ }
            }
        }
    }

    private void StartRecording(bool insertMode)
    {
        if (_paused) return;
        if (_state != AppState.Idle) return;
        if (!_speechService.IsInitialized) return;

        _insertMode = insertMode;
        _state = AppState.Recording;

        try
        {
            _audioCaptureService.StartRecording();
        }
        catch (Exception ex)
        {
            _state = AppState.Idle;
            Console.WriteLine($"ERROR: Failed to start recording: {ex.Message}");
            ErrorOccurred?.Invoke($"Failed to start recording:\n{ex.Message}");
            return;
        }

        if (!insertMode) _wsServer.BroadcastStatus("recording");

        var iconType = insertMode ? "recording_insert" : _selectionMode ? "recording_ws_sel" : "recording_ws";
        var status = insertMode ? "Recording (insert)" : _selectionMode ? "Recording (ws+sel)" : "Recording (ws)";
        StateChanged?.Invoke(status, iconType);
        Console.WriteLine($"[REC] Recording started ({(insertMode ? "insert" : _selectionMode ? "ws+sel" : "ws")})...");
    }

    private void StopRecordingAndProcess()
    {
        if (_state != AppState.Recording) return;

        var insertMode = _insertMode;
        var selectionMode = _selectionMode;
        _state = AppState.Processing;
        _audioCaptureService.StopRecording();

        var iconType = insertMode ? "processing_insert" : selectionMode ? "processing_ws_sel" : "processing_ws";
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

                    var mode = insertMode ? "insert" : selectionMode ? "ws_sel" : "ws";

                    if (insertMode)
                    {
                        await PasteTextAtCursor(text);
                        TranscriptionReady?.Invoke(text, mode);
                    }
                    else
                    {
                        var fullText = _selectedText != null ? $"{_selectedText} {text}" : text;
                        _wsServer.BroadcastTranscription(fullText);
                        TranscriptionReady?.Invoke(fullText, mode);
                    }
                }
                else
                {
                    Console.WriteLine("  (empty result)");
                }

                _state = AppState.Idle;
                if (!insertMode) _wsServer.BroadcastStatus("idle");
                StateChanged?.Invoke(IdleStatus, IdleIconType);
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
        await Task.Delay(50);

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

    private void OnHotkeyPressed()
    {
        if (_paused || _state != AppState.Idle || !_speechService.IsInitialized) return;
        _selectedText = null;
        _selectionMode = false;
        StartRecording(insertMode: false);
    }

    private void OnHotkeyReleased() => StopRecordingAndProcess();

    private void OnInsertHotkeyPressed()
    {
        if (_paused || _state != AppState.Idle || !_speechService.IsInitialized) return;
        _selectedText = null;
        _selectionMode = false;
        StartRecording(insertMode: true);
    }

    private void OnInsertHotkeyReleased() => StopRecordingAndProcess();

    private async void OnSelectionHotkeyPressed()
    {
        if (_paused || _state != AppState.Idle || !_speechService.IsInitialized) return;
        _selectedText = null;
        _selectionMode = true;

        // Start recording immediately — icon changes to purple right away
        StartRecording(insertMode: false);

        // Capture selected text in parallel with recording;
        // it will be ready by the time speech recognition completes
        await CaptureSelectedText();
    }

    private void OnSelectionHotkeyReleased() => StopRecordingAndProcess();

    public void PauseForSettings()
    {
        _paused = true;

        // Force idle if recording was in progress
        if (_state == AppState.Recording)
        {
            _audioCaptureService.StopRecording();
            _state = AppState.Idle;
            _wsServer.BroadcastStatus("idle");
            StateChanged?.Invoke(IdleStatus, IdleIconType);
        }
    }

    public void ApplySettings(AppSettings newSettings)
    {
        _settings = newSettings;
        _settings.Save();

        _audioCaptureService.DeviceId = _settings.MicrophoneDeviceId;
        _audioCaptureService.NormalizeAudio = _settings.NormalizeAudio;
        _hotkeyService.UpdateHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
        _hotkeyService.UpdateInsertHotkey(_settings.InsertHotkeyModifiers, _settings.InsertHotkeyKey);
        _hotkeyService.UpdateSelectionHotkey(_settings.SelectionHotkeyModifiers, _settings.SelectionHotkeyKey);

        _paused = false;
    }

    /// <summary>
    /// Unconditionally unpauses after settings window is closed (whether saved or cancelled).
    /// </summary>
    public void ResumeAfterSettings()
    {
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
