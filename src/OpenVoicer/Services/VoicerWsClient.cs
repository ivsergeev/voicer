using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OpenVoicer.Models;

namespace OpenVoicer.Services;

public class VoicerWsClient : IDisposable
{
    private readonly OpenVoicerSettings _settings;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private int _reconnectDelay = 1000;
    private const int MaxReconnectDelay = 10000;

    public bool IsConnected { get; private set; }
    public bool IsClaimed { get; private set; }

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<bool>? ClaimChanged;
    public event Action<string>? StatusChanged;          // "recording" | "processing" | "idle"
    public event Action<string, string?, string?>? TranscriptionReceived; // (text, context, tag)
    public event Action<string>? ErrorReceived;

    public VoicerWsClient(OpenVoicerSettings settings)
    {
        _settings = settings;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _ = ConnectLoop(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();

        if (_ws is { State: WebSocketState.Open })
        {
            try
            {
                SendJsonAsync(new { type = "release" }).Wait(2000);
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", CancellationToken.None)
                    .Wait(2000);
            }
            catch { /* best effort */ }
        }

        _ws?.Dispose();
        _ws = null;
        IsConnected = false;
        IsClaimed = false;
    }

    public void SendClaim()
    {
        SendJson(new { type = "claim" });
    }

    public void SendRelease()
    {
        SendJson(new { type = "release" });
    }

    private async Task ConnectLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReceive(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WS] Connection error: {ex.Message}");
            }

            IsConnected = false;
            IsClaimed = false;
            Disconnected?.Invoke();

            if (ct.IsCancellationRequested) break;

            Console.WriteLine($"[WS] Reconnecting in {_reconnectDelay}ms...");
            try { await Task.Delay(_reconnectDelay, ct); }
            catch (OperationCanceledException) { break; }

            _reconnectDelay = Math.Min((int)(_reconnectDelay * 1.5), MaxReconnectDelay);
        }
    }

    private async Task ConnectAndReceive(CancellationToken ct)
    {
        _ws?.Dispose();
        _ws = new ClientWebSocket();

        var url = $"ws://localhost:{_settings.VoicerWsPort}";
        Console.WriteLine($"[WS] Connecting to {url}...");

        await _ws.ConnectAsync(new Uri(url), ct);

        IsConnected = true;
        _reconnectDelay = 1000;
        Console.WriteLine($"[WS] Connected to Voicer");
        Connected?.Invoke();

        // Auto-claim
        SendClaim();

        var buffer = new byte[4096];
        var msgBuffer = new StringBuilder();

        while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var result = await _ws.ReceiveAsync(buffer, ct);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                Console.WriteLine("[WS] Server closed connection");
                break;
            }

            msgBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
            {
                HandleMessage(msgBuffer.ToString());
                msgBuffer.Clear();
            }
        }
    }

    private void HandleMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp)) return;
            var type = typeProp.GetString();

            switch (type)
            {
                case "claimed":
                    if (!doc.RootElement.TryGetProperty("active", out var activeProp)) break;
                    var active = activeProp.GetBoolean();
                    IsClaimed = active;
                    Console.WriteLine($"[WS] Claim: {(active ? "active" : "inactive")}");
                    ClaimChanged?.Invoke(active);
                    break;

                case "transcription":
                    var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() : null;
                    var context = doc.RootElement.TryGetProperty("context", out var c) ? c.GetString() : null;
                    var tag = doc.RootElement.TryGetProperty("tag", out var tg) ? tg.GetString() : null;
                    Console.WriteLine($"[WS] Transcription: \"{text}\"{(tag != null ? $" [{tag}]" : "")}");
                    TranscriptionReceived?.Invoke(text ?? "", context, tag);
                    break;

                case "status":
                    var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
                    if (status != null)
                    {
                        Console.WriteLine($"[WS] Voicer status: {status}");
                        StatusChanged?.Invoke(status);
                    }
                    break;

                case "error":
                    var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
                    Console.WriteLine($"[WS] Voicer error: {msg}");
                    ErrorReceived?.Invoke(msg ?? "Unknown error");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WS] Failed to parse message: {ex.Message}");
        }
    }

    private async Task SendJsonAsync(object obj)
    {
        if (_ws is not { State: WebSocketState.Open }) return;

        try
        {
            var json = JsonSerializer.Serialize(obj);
            var bytes = Encoding.UTF8.GetBytes(json);
            using var cts = new CancellationTokenSource(2000);
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WS] Send error: {ex.Message}");
        }
    }

    private void SendJson(object obj)
    {
        _ = SendJsonAsync(obj);
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
