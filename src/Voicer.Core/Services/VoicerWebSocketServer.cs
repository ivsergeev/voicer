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

    public event Action<int>? ClientCountChanged;
    /// <summary>
    /// Fired when the active (claimed) client changes. Parameter is true if there is an active client.
    /// </summary>
    public event Action<bool>? ActiveClientChanged;
    /// <summary>
    /// Fired when a client sends an ack message. Parameters: (status, message?).
    /// </summary>
    public event Action<string, string?>? ClientAckReceived;

    public void Start(int port)
    {
        _server = new WebSocketServer($"ws://0.0.0.0:{port}");
        _server.Start(socket =>
        {
            socket.OnOpen = () =>
            {
                bool becameActive = false;
                lock (_lock)
                {
                    _clients.Add(socket);

                    // First client auto-claims
                    if (_activeClient == null)
                    {
                        _activeClient = socket;
                        becameActive = true;
                        SendTo(socket, JsonSerializer.Serialize(new { type = "claimed", active = true }));
                    }
                    else
                    {
                        SendTo(socket, JsonSerializer.Serialize(new { type = "claimed", active = false }));
                    }
                }
                ClientCountChanged?.Invoke(ClientCount);
                if (becameActive) ActiveClientChanged?.Invoke(true);
            };

            socket.OnClose = () =>
            {
                bool claimChanged = false;
                lock (_lock)
                {
                    _clients.Remove(socket);
                    if (_activeClient == socket)
                    {
                        PromoteNextClient();
                        claimChanged = true;
                    }
                }
                ClientCountChanged?.Invoke(ClientCount);
                if (claimChanged) ActiveClientChanged?.Invoke(HasClients);
            };

            socket.OnError = ex =>
            {
                bool claimChanged = false;
                lock (_lock)
                {
                    _clients.Remove(socket);
                    if (_activeClient == socket)
                    {
                        PromoteNextClient();
                        claimChanged = true;
                    }
                }
                ClientCountChanged?.Invoke(ClientCount);
                if (claimChanged) ActiveClientChanged?.Invoke(HasClients);
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
                        bool changed = false;
                        lock (_lock)
                        {
                            var prevActive = _activeClient;
                            _activeClient = socket;

                            // Notify the previous active client it lost the claim
                            if (prevActive != null && prevActive != socket)
                            {
                                SendTo(prevActive, JsonSerializer.Serialize(new { type = "claimed", active = false }));
                                changed = true;
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
                                SendTo(socket, JsonSerializer.Serialize(new { type = "claimed", active = false }));
                                PromoteNextClient(exclude: socket);
                                changed = true;
                            }
                        }
                        if (changed) ActiveClientChanged?.Invoke(HasClients);
                    }
                    break;

                case "ack":
                    {
                        var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
                        var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
                        if (status != null)
                        {
                            ClientAckReceived?.Invoke(status, msg);
                        }
                    }
                    break;

                // Ignore ping and unknown messages
            }
        }
        catch
        {
            // Malformed JSON — ignore
        }
    }

    /// <summary>
    /// Passes the active role to the next available client.
    /// Must be called under _lock.
    /// </summary>
    private void PromoteNextClient(IWebSocketConnection? exclude = null)
    {
        _activeClient = null;

        var next = _clients.FirstOrDefault(c => c != exclude);
        if (next != null)
        {
            _activeClient = next;
            SendTo(next, JsonSerializer.Serialize(new { type = "claimed", active = true }));
        }
    }

    public void BroadcastTranscription(string text)
    {
        var message = JsonSerializer.Serialize(new
        {
            type = "transcription",
            text,
            timestamp = DateTime.UtcNow.ToString("o")
        });

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
