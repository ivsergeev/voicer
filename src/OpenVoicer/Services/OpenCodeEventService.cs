using System.Net.Http;
using System.Text.Json;
using OpenVoicer.Models;
using Serilog;

namespace OpenVoicer.Services;

/// <summary>
/// SSE client that subscribes to OpenCode /event endpoint
/// and raises events for agent status changes, completions, errors.
/// </summary>
public class OpenCodeEventService : IDisposable
{
    private readonly OpenVoicerSettings _settings;
    private readonly HttpClient _http;
    private CancellationTokenSource? _cts;
    private int _reconnectDelay = 1000;
    private const int MaxReconnectDelay = 10000;

    // Cache last assistant text for "done" popup
    private string? _lastAssistantText;
    private string? _lastAgent;
    private bool _isBusy;

    public bool IsConnected { get; private set; }

    // Events
    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<string>? AgentBusy;           // sessionId
    public event Action<string, string?, string?>? AgentIdle;  // sessionId, lastText, agent
    public event Action<string>? ErrorOccurred;       // message

    public OpenCodeEventService(OpenVoicerSettings settings)
    {
        _settings = settings;
        _http = new HttpClient
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = ConnectLoop(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        IsConnected = false;
    }

    private async Task ConnectLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ReadSseStream(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SSE] Connection error");
            }

            if (IsConnected)
            {
                IsConnected = false;
                Disconnected?.Invoke();
            }

            if (ct.IsCancellationRequested) break;

            Log.Debug("[SSE] Reconnecting in {ReconnectDelay}ms...", _reconnectDelay);
            try { await Task.Delay(_reconnectDelay, ct); }
            catch (OperationCanceledException) { break; }

            _reconnectDelay = Math.Min((int)(_reconnectDelay * 1.5), MaxReconnectDelay);
        }
    }

    private async Task ReadSseStream(CancellationToken ct)
    {
        var url = $"http://localhost:{_settings.OpenCodePort}/event";
        Log.Debug("[SSE] Connecting to {Url}...", url);

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        IsConnected = true;
        _reconnectDelay = 1000;
        _isBusy = false;
        _lastAssistantText = null;
        _lastAgent = null;
        Log.Information("[SSE] Connected to OpenCode events");
        Connected?.Invoke();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            // Timeout if no data received for 60 seconds (heartbeat should arrive more often)
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(TimeSpan.FromSeconds(60));

            string? line;
            try
            {
                line = await reader.ReadLineAsync(readCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Log.Warning("[SSE] Read timeout, reconnecting...");
                break;
            }
            if (line == null) break; // Stream closed

            if (line.StartsWith("data: "))
            {
                var json = line.Substring(6);
                try
                {
                    HandleEventJson(json);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[SSE] Parse error");
                }
            }
        }
    }

    private void HandleEventJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProp)) return;
        var type = typeProp.GetString();

        var props = root.TryGetProperty("properties", out var p) ? p : default;

        switch (type)
        {
            case "server.connected":
                Log.Debug("[SSE] Server connected event received");
                break;

            case "session.status":
                HandleSessionStatus(props);
                break;

            case "session.idle":
                HandleSessionIdle(props);
                break;

            case "message.part.updated":
                HandleMessagePartUpdated(props);
                break;

            case "message.part.delta":
                HandleMessagePartDelta(props);
                break;

            case "message.updated":
                HandleMessageUpdated(props);
                break;
        }
    }

    private void HandleSessionStatus(JsonElement props)
    {
        if (!props.TryGetProperty("sessionID", out var sidProp)) return;
        var sessionId = sidProp.GetString() ?? "";

        if (props.TryGetProperty("status", out var status) &&
            status.ValueKind == JsonValueKind.Object &&
            status.TryGetProperty("type", out var statusType))
        {
            var st = statusType.GetString();
            if (st == "busy" && !_isBusy)
            {
                _isBusy = true;
                _lastAssistantText = null;
                Log.Debug("[SSE] Agent busy: {SessionId}", sessionId);
                AgentBusy?.Invoke(sessionId);
            }
            else if (st == "idle")
            {
                FireIdleIfBusy(sessionId);
            }
        }
    }

    private void HandleSessionIdle(JsonElement props)
    {
        if (!props.TryGetProperty("sessionID", out var sidProp)) return;
        var sessionId = sidProp.GetString() ?? "";
        FireIdleIfBusy(sessionId);
    }

    private void FireIdleIfBusy(string sessionId)
    {
        if (!_isBusy) return;
        _isBusy = false;
        var text = _lastAssistantText;
        Log.Debug("[SSE] Agent idle: {SessionId}, textLen={Len}", sessionId, text?.Length ?? 0);
        AgentIdle?.Invoke(sessionId, text, _lastAgent);
        _lastAssistantText = null;
    }

    private void HandleMessagePartUpdated(JsonElement props)
    {
        if (!props.TryGetProperty("part", out var part)) return;
        if (part.ValueKind != JsonValueKind.Object) return;

        var partType = part.TryGetProperty("type", out var pt) ? pt.GetString() : null;

        if (partType == "text" && part.TryGetProperty("text", out var textProp))
        {
            var text = textProp.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                _lastAssistantText = text;
            }
        }
    }

    private void HandleMessagePartDelta(JsonElement props)
    {
        if (!props.TryGetProperty("delta", out var delta)) return;

        // delta can be a string (plain text delta) or an object with type/content
        if (delta.ValueKind == JsonValueKind.String)
        {
            var content = delta.GetString();
            if (!string.IsNullOrEmpty(content))
            {
                _lastAssistantText = (_lastAssistantText ?? "") + content;
            }
            return;
        }

        if (delta.ValueKind != JsonValueKind.Object) return;

        var deltaType = delta.TryGetProperty("type", out var dt) ? dt.GetString() : null;

        if (deltaType == "text" && delta.TryGetProperty("content", out var contentProp))
        {
            var content = contentProp.GetString();
            if (!string.IsNullOrEmpty(content))
            {
                _lastAssistantText = (_lastAssistantText ?? "") + content;
            }
        }
    }

    private void HandleMessageUpdated(JsonElement props)
    {
        if (!props.TryGetProperty("info", out var info)) return;

        // Track agent name from assistant messages
        if (info.TryGetProperty("role", out var role) && role.GetString() == "assistant")
        {
            if (info.TryGetProperty("agent", out var agent))
                _lastAgent = agent.GetString();
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
        _http.Dispose();
    }
}
