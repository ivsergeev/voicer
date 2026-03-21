using System.Text.Json;
using Fleck;

namespace Voicer.Core.Services;

public class VoicerWebSocketServer : IDisposable
{
    private WebSocketServer? _server;
    private readonly List<IWebSocketConnection> _clients = new();
    private IWebSocketConnection? _activeClient;
    private readonly object _lock = new();

    public int ClientCount
    {
        get { lock (_lock) return _clients.Count; }
    }

    public bool HasClients
    {
        get { lock (_lock) return _clients.Count > 0; }
    }

    public bool HasActiveClient
    {
        get { lock (_lock) return _activeClient != null; }
    }

    public event Action<int>? ClientCountChanged;
    /// <summary>
    /// Fired when the active (claimed) client changes. Parameter is true if there is an active client.
    /// </summary>
    public event Action<bool>? ActiveClientChanged;


    public void Start(int port)
    {
        _server = new WebSocketServer($"ws://0.0.0.0:{port}");
        _server.Start(socket =>
        {
            socket.OnOpen = () =>
            {
                lock (_lock)
                {
                    _clients.Add(socket);
                    SendTo(socket, JsonSerializer.Serialize(new { type = "claimed", active = false }));
                }
                ClientCountChanged?.Invoke(ClientCount);
            };

            socket.OnClose = () =>
            {
                bool hadClaim = false;
                lock (_lock)
                {
                    _clients.Remove(socket);
                    if (_activeClient == socket)
                    {
                        _activeClient = null;
                        hadClaim = true;
                    }
                }
                ClientCountChanged?.Invoke(ClientCount);
                if (hadClaim) ActiveClientChanged?.Invoke(false);
            };

            socket.OnError = ex =>
            {
                bool hadClaim = false;
                lock (_lock)
                {
                    _clients.Remove(socket);
                    if (_activeClient == socket)
                    {
                        _activeClient = null;
                        hadClaim = true;
                    }
                }
                ClientCountChanged?.Invoke(ClientCount);
                if (hadClaim) ActiveClientChanged?.Invoke(false);
            };

            socket.OnMessage = message => HandleClientMessage(socket, message);
        });
    }

    private void HandleClientMessage(IWebSocketConnection socket, string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var type = doc.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case "claim":
                    {
                        bool changed;
                        lock (_lock)
                        {
                            var prevActive = _activeClient;
                            changed = prevActive != socket;
                            _activeClient = socket;

                            // Notify the previous active client it lost the claim
                            if (prevActive != null && prevActive != socket)
                            {
                                SendTo(prevActive, JsonSerializer.Serialize(new { type = "claimed", active = false }));
                            }

                            // Confirm to the new active client
                            SendTo(socket, JsonSerializer.Serialize(new { type = "claimed", active = true }));
                        }
                        if (changed) ActiveClientChanged?.Invoke(true);
                    }
                    break;

                case "release":
                    {
                        bool changed = false;
                        lock (_lock)
                        {
                            if (_activeClient == socket)
                            {
                                _activeClient = null;
                                SendTo(socket, JsonSerializer.Serialize(new { type = "claimed", active = false }));
                                changed = true;
                            }
                        }
                        if (changed) ActiveClientChanged?.Invoke(false);
                    }
                    break;

                // Ignore ack, ping and unknown messages
            }
        }
        catch
        {
            // Malformed JSON — ignore
        }
    }

    /// <summary>
    /// Sends a transcription message to the active client (protocol v2).
    /// Fields context and tag are included only when non-null.
    /// </summary>
    public void BroadcastTranscription(string text, string? context = null, string? tag = null)
    {
        var obj = new Dictionary<string, object>
        {
            ["type"] = "transcription",
            ["text"] = text,
            ["timestamp"] = DateTime.UtcNow.ToString("o"),
        };
        if (context != null) obj["context"] = context;
        if (tag != null) obj["tag"] = tag;

        var message = JsonSerializer.Serialize(obj);

        // Send transcription ONLY to the active client
        lock (_lock)
        {
            if (_activeClient != null)
            {
                SendTo(_activeClient, message);
            }
        }
    }

    public void BroadcastStatus(string status)
    {
        var message = JsonSerializer.Serialize(new
        {
            type = "status",
            status
        });

        Broadcast(message);
    }

    public void BroadcastError(string errorMessage)
    {
        var message = JsonSerializer.Serialize(new
        {
            type = "error",
            message = errorMessage
        });

        Broadcast(message);
    }

    private static void SendTo(IWebSocketConnection client, string message)
    {
        try
        {
            client.Send(message);
        }
        catch { /* client disconnected */ }
    }

    private void Broadcast(string message)
    {
        List<IWebSocketConnection> snapshot;
        lock (_lock)
        {
            snapshot = new List<IWebSocketConnection>(_clients);
        }

        foreach (var client in snapshot)
        {
            try
            {
                client.Send(message);
            }
            catch
            {
                lock (_lock) _clients.Remove(client);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var client in _clients)
            {
                try { client.Close(); } catch { }
            }
            _clients.Clear();
            _activeClient = null;
        }

        _server?.Dispose();
    }
}
