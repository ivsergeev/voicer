using System.IO;
using Serilog;
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
    private readonly object _stateLock = new();
    private AppState _state = AppState.Idle;
    private bool _insertMode;
    private volatile bool _paused;
    private string? _selectedText;
    private volatile int _wsClientCount;
    private volatile bool _hasActiveClaim;

    // Current WS hotkey action being executed (set on press, read on release/process)
    private HotkeyAction? _currentWsAction;

    public AppSettings Settings => _settings;

    public enum AppState { Idle, Recording, Processing }

    // Events for UI layer
    public event Action<string, string>? StateChanged;         // (status, iconType)
    public event Action<int>? ClientCountChanged;
    /// <summary>
    /// Fired when transcription is ready. Parameters: (text, context, tag, mode).
    /// mode: "insert" | "ws" | "ws_sel" | "ws_tag"
    /// </summary>
    public event Action<string, string?, string?, string>? TranscriptionReady;
    public event Action<string>? ErrorOccurred;
    public event Action<bool>? ActiveClientChanged;            // true = has active client

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
            if (count == 0) _hasActiveClaim = false;
            ClientCountChanged?.Invoke(count);

            if (_state == AppState.Idle)
            {
                StateChanged?.Invoke(IdleStatus, IdleIconType);
            }
        };
        _wsServer.ActiveClientChanged += hasActive =>
        {
            _hasActiveClaim = hasActive;
            ActiveClientChanged?.Invoke(hasActive);

            // Update idle icon when claim state changes
            if (_state == AppState.Idle)
            {
                StateChanged?.Invoke(IdleStatus, IdleIconType);
            }
        };


        try
        {
            _wsServer.Start(_settings.WebSocketPort);
            Log.Information("WebSocket server started on ws://0.0.0.0:{Port}", _settings.WebSocketPort);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start WebSocket server");
            ErrorOccurred?.Invoke($"Failed to start WebSocket server on port {_settings.WebSocketPort}:\n{ex.Message}");
        }

        // Audio capture
        var mics = _audioCaptureService.GetMicrophoneDevices();
        Log.Information("Microphones found: {Count}", mics.Count);
        foreach (var (id, name) in mics)
        {
            bool selected = id == _settings.MicrophoneDeviceId;
            Log.Debug("Microphone: {Name}{Selected}", name, selected ? " (selected)" : "");
        }

        if (mics.Count == 0)
            Log.Warning("No microphone devices found");
        else if (string.IsNullOrEmpty(_settings.MicrophoneDeviceId))
            Log.Information("Using default capture device");

        _audioCaptureService.DeviceId = _settings.MicrophoneDeviceId;
        _audioCaptureService.NormalizeAudio = _settings.NormalizeAudio;

        // Speech recognition
        InitializeSpeechModel();

        // Hotkeys
        _hotkeyService.InsertKeyPressed += OnInsertHotkeyPressed;
        _hotkeyService.InsertKeyReleased += OnInsertHotkeyReleased;
        _hotkeyService.WsHotkeyPressed += OnWsHotkeyPressed;
        _hotkeyService.WsHotkeyReleased += OnWsHotkeyReleased;

        try
        {
            _hotkeyService.Start(
                _settings.InsertHotkeyModifiers, _settings.InsertHotkeyKey,
                _settings.WsHotkeyActions);

            if (!_hotkeyService.IsAvailable)
            {
                Log.Error("Hotkey service is not available");
                ErrorOccurred?.Invoke(
                    "Global hotkeys are not available.\n" +
                    "This usually happens on Linux with Wayland (X11 is required).\n" +
                    "Try launching with: GDK_BACKEND=x11 ./Voicer");
            }
            else
            {
                Log.Debug("Push-to-talk hotkey (Insert): {Hotkey}", _platformInfo.GetHotkeyDisplayName(_settings.InsertHotkeyModifiers, _settings.InsertHotkeyKey));
                foreach (var action in _settings.WsHotkeyActions)
                {
                    Log.Debug("WS hotkey: {Hotkey} -> {Action}{Tag}", _platformInfo.GetHotkeyDisplayName(action.Modifiers, action.KeyCode), action.Action, action.Tag != null ? $" [{action.Tag}]" : "");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to register hotkey");
            ErrorOccurred?.Invoke($"Failed to register hotkey:\n{ex.Message}");
        }

        Log.Information("Ready. Press hotkey to start recording");
    }

    private string IdleIconType => _wsClientCount > 0
        ? (_hasActiveClaim ? "idle_claimed" : "idle")
        : "idle_no_clients";
    private string IdleStatus => _wsClientCount > 0
        ? (_hasActiveClaim ? "Idle (claimed)" : "Idle")
        : "Idle (no clients)";

    private void InitializeSpeechModel()
    {
        var modelPath = _settings.GetModelPath();
        var tokensPath = _settings.GetTokensPath();

        if (!File.Exists(modelPath) || !File.Exists(tokensPath))
        {
            StateChanged?.Invoke("No model", "no_model");
            Log.Warning("Model files not found. Expected: {ModelPath}, {TokensPath}. Run: powershell ./scripts/download-model.ps1", modelPath, tokensPath);
            return;
        }

        Log.Information("Loading speech model");

        Task.Run(() =>
        {
            try
            {
                StateChanged?.Invoke("Loading model...", "processing_ws");
                _speechService.Initialize(modelPath, tokensPath, _settings.ModelThreads);
                Log.Information("Speech model loaded (e2e with punctuation)");

                StateChanged?.Invoke(IdleStatus, IdleIconType);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load speech model");
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
                Log.Debug("Selection captured in {ElapsedMs}ms: {Preview}", sw.ElapsedMilliseconds, _selectedText.Substring(0, Math.Min(_selectedText.Length, 80)));
            }
            else if (!string.IsNullOrEmpty(savedClipboard))
            {
                _selectedText = savedClipboard;
                Log.Debug("No selection, using clipboard: {Preview}", _selectedText.Substring(0, Math.Min(_selectedText.Length, 80)));
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to capture selection");
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
        if (!_speechService.IsInitialized) return;

        lock (_stateLock)
        {
            if (_state != AppState.Idle) return;
            _insertMode = insertMode;
            _state = AppState.Recording;
        }

        try
        {
            _audioCaptureService.StartRecording();
        }
        catch (Exception ex)
        {
            lock (_stateLock) _state = AppState.Idle;
            Log.Error(ex, "Failed to start recording");
            ErrorOccurred?.Invoke($"Failed to start recording:\n{ex.Message}");
            return;
        }

        if (!insertMode) _wsServer.BroadcastStatus("recording");

        bool isSelectionMode = _currentWsAction?.Action == WsActionType.TranscribeWithContext;

        string iconType, status;
        if (insertMode)
        {
            iconType = "recording_insert";
            status = "Recording (insert)";
        }
        else if (_hasActiveClaim)
        {
            iconType = isSelectionMode ? "recording_ws_sel" : "recording_ws";
            status = isSelectionMode ? "Recording (ws+sel)" : "Recording (ws)";
        }
        else
        {
            iconType = isSelectionMode ? "recording_ws_sel_noclaim" : "recording_ws_noclaim";
            status = isSelectionMode ? "Recording (ws+sel, no claim)" : "Recording (ws, no claim)";
        }
        StateChanged?.Invoke(status, iconType);
        Log.Debug("Recording started ({Mode})", insertMode ? "insert" : isSelectionMode ? "ws+sel" : "ws");
    }

    private void StopRecordingAndProcess()
    {
        bool insertMode;
        HotkeyAction? currentAction;

        lock (_stateLock)
        {
            if (_state != AppState.Recording) return;
            insertMode = _insertMode;
            currentAction = _currentWsAction;
            _state = AppState.Processing;
        }

        bool isSelectionMode = currentAction?.Action == WsActionType.TranscribeWithContext;
        _audioCaptureService.StopRecording();

        string procIconType;
        if (insertMode)
            procIconType = "processing_insert";
        else if (_hasActiveClaim)
            procIconType = isSelectionMode ? "processing_ws_sel" : "processing_ws";
        else
            procIconType = isSelectionMode ? "processing_ws_sel_noclaim" : "processing_ws_noclaim";
        StateChanged?.Invoke("Processing", procIconType);
        if (!insertMode) _wsServer.BroadcastStatus("processing");
        Log.Debug("Recording stopped, processing");

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
        Log.Debug("Audio: {Duration:F1}s, {SampleCount} samples, rms={Rms:F4}, peak={Peak:F4}", durationSec, samples.Length, rms, maxAbs);

        Task.Run(async () =>
        {
            try
            {
                var text = _speechService.Recognize(samples);

                if (insertMode)
                {
                    // Insert mode: require non-empty text
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        Log.Information("Transcription: {Text}", text);
                        await PasteTextAtCursor(text);
                        TranscriptionReady?.Invoke(text, null, null, "insert");
                    }
                    else
                    {
                        Log.Debug("Transcription result is empty (insert mode)");
                    }
                }
                else
                {
                    // WS mode: send if text is non-empty, or if there's context/tag
                    string? context = _selectedText;
                    string? tag = currentAction?.Tag;
                    bool hasContent = !string.IsNullOrWhiteSpace(text) ||
                                     !string.IsNullOrEmpty(context) ||
                                     !string.IsNullOrEmpty(tag);

                    if (hasContent)
                    {
                        text ??= "";
                        Log.Information("Transcription: {Text}{Context}{Tag}",
                            string.IsNullOrWhiteSpace(text) ? "(empty)" : text,
                            context != null ? $" +ctx[{context.Length}]" : "",
                            tag != null ? $" +tag[{tag}]" : "");

                        string mode = isSelectionMode ? "ws_sel" : "ws";
                        _wsServer.BroadcastTranscription(text, context, tag);
                        TranscriptionReady?.Invoke(text, context, tag, mode);
                    }
                    else
                    {
                        Log.Debug("Transcription result is empty (ws mode, no context/tag)");
                    }
                }

                lock (_stateLock) _state = AppState.Idle;
                if (!insertMode) _wsServer.BroadcastStatus("idle");
                StateChanged?.Invoke(IdleStatus, IdleIconType);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Recognition failed");
                if (!insertMode) _wsServer.BroadcastError(ex.Message);
                lock (_stateLock) _state = AppState.Idle;
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
        catch (Exception ex) { Log.Warning(ex, "Failed to save clipboard before paste"); }

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
                    Log.Warning(ex, "Clipboard restore attempt {Attempt}/3 failed", attempt);
                    if (attempt < 3) await Task.Delay(100);
                }
            }
        }
    }

    // --- Insert hotkey (F6) ---

    private void OnInsertHotkeyPressed()
    {
        if (_paused || _state != AppState.Idle || !_speechService.IsInitialized) return;
        _selectedText = null;
        _currentWsAction = null;
        StartRecording(insertMode: true);
    }

    private void OnInsertHotkeyReleased() => StopRecordingAndProcess();

    // --- Dynamic WS hotkeys ---

    private void OnWsHotkeyPressed(int index)
    {
        if (_paused || _state != AppState.Idle || !_speechService.IsInitialized) return;
        if (index < 0 || index >= _settings.WsHotkeyActions.Count) return;

        var action = _settings.WsHotkeyActions[index];
        _currentWsAction = action;
        _selectedText = null;

        if (action.Action == WsActionType.SendTag)
        {
            // SendTag: no recording, immediately send tag
            var tag = action.Tag ?? "";
            Log.Debug("WS SendTag: [{Tag}]", tag);
            _wsServer.BroadcastTranscription("", tag: tag);
            TranscriptionReady?.Invoke("", null, tag, "ws_tag");
            _currentWsAction = null; // clear to avoid stale state
            return;
        }

        // TranscribeAndSend or TranscribeWithContext: start recording
        StartRecording(insertMode: false);

        if (action.Action == WsActionType.TranscribeWithContext)
        {
            // Capture selected text in parallel with recording
            _ = CaptureSelectedText();
        }
    }

    private void OnWsHotkeyReleased(int index)
    {
        // For SendTag actions, recording was never started — nothing to do
        if (index >= 0 && index < _settings.WsHotkeyActions.Count
            && _settings.WsHotkeyActions[index].Action == WsActionType.SendTag)
            return;

        StopRecordingAndProcess();
    }

    public void PauseForSettings()
    {
        _paused = true;

        // Force idle if recording was in progress
        bool wasRecording;
        lock (_stateLock)
        {
            wasRecording = _state == AppState.Recording;
            if (wasRecording) _state = AppState.Idle;
        }
        if (wasRecording)
        {
            _audioCaptureService.StopRecording();
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
        _hotkeyService.UpdateInsertHotkey(_settings.InsertHotkeyModifiers, _settings.InsertHotkeyKey);
        _hotkeyService.UpdateWsHotkeys(_settings.WsHotkeyActions);

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
