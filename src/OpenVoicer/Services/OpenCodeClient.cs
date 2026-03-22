using System.Net.Http;
using System.Text;
using System.Text.Json;
using OpenVoicer.Models;
using Serilog;

namespace OpenVoicer.Services;

/// <summary>
/// HTTP client for OpenCode API (session management, prompt sending).
/// </summary>
public class OpenCodeClient : IDisposable
{
    private readonly OpenVoicerSettings _settings;
    private readonly HttpClient _http;
    private string? _activeSessionId;

    public string? ActiveSessionId => _activeSessionId;

    public OpenCodeClient(OpenVoicerSettings settings)
    {
        _settings = settings;
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{_settings.OpenCodePort}"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        Log.Debug("[OC API] HttpClient created, base: {BaseAddress}", _http.BaseAddress);
    }

    /// <summary>
    /// Returns active session ID, creating a new one on first call.
    /// </summary>
    public async Task<string?> ResolveSessionAsync()
    {
        if (_activeSessionId != null) return _activeSessionId;

        // Always create a fresh session at startup
        return await CreateSessionAsync();
    }

    /// <summary>
    /// Creates a new OpenCode session and sets it as active.
    /// </summary>
    public async Task<string?> CreateNewSessionAsync()
    {
        _activeSessionId = null;
        return await CreateSessionAsync();
    }

    /// <summary>
    /// Creates a new OpenCode session via POST /session.
    /// </summary>
    private async Task<string?> CreateSessionAsync()
    {
        try
        {
            var content = new StringContent("{}", Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync("/session", content);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Error("[OC API] POST /session failed: {StatusCode}", resp.StatusCode);
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("id", out var idProp))
            {
                _activeSessionId = idProp.GetString();
                Log.Information("[OC API] Created session: {SessionId}", _activeSessionId);
                return _activeSessionId;
            }

            Log.Warning("[OC API] Created session but no id in response");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OC API] CreateSession error");
            return null;
        }
    }

    /// <summary>
    /// Sends a prompt to the active OpenCode session.
    /// </summary>
    public async Task<bool> SendPromptAsync(string text, string? agent = null)
    {
        var sessionId = await ResolveSessionAsync();
        if (sessionId == null)
        {
            Log.Warning("[OC API] No session available, cannot send prompt");
            return false;
        }

        try
        {
            var bodyDict = new Dictionary<string, object>
            {
                ["parts"] = new[] { new { type = "text", text } }
            };
            if (!string.IsNullOrEmpty(agent))
                bodyDict["agent"] = agent;

            var content = new StringContent(
                JsonSerializer.Serialize(bodyDict),
                Encoding.UTF8,
                "application/json");

            var resp = await _http.PostAsync($"/session/{sessionId}/prompt_async", content);

            if (resp.IsSuccessStatusCode)
            {
                Log.Information("[OC API] Prompt sent to session {SessionId}", sessionId);
                return true;
            }

            var error = await resp.Content.ReadAsStringAsync();
            Log.Error("[OC API] Prompt failed ({StatusCode}): {Error}", resp.StatusCode, error);
            // Session may have been deleted — reset so next call re-resolves
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                _activeSessionId = null;
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OC API] SendPrompt error");
            return false;
        }
    }

    /// <summary>
    /// Fetches available providers and their models from GET /provider.
    /// Returns list of (providerID, providerName, modelID, modelName).
    /// </summary>
    public async Task<List<(string ProviderId, string ProviderName, string ModelId)>> GetAvailableModelsAsync()
    {
        var result = new List<(string, string, string)>();
        try
        {
            var resp = await _http.GetAsync("/provider");
            if (!resp.IsSuccessStatusCode) return result;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("all", out var allProp))
            {
                foreach (var provider in allProp.EnumerateArray())
                {
                    var pid = provider.GetProperty("id").GetString() ?? "";
                    var pname = provider.GetProperty("name").GetString() ?? pid;

                    if (provider.TryGetProperty("models", out var models))
                    {
                        foreach (var model in models.EnumerateObject())
                        {
                            result.Add((pid, pname, model.Name));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OC API] GetAvailableModels error");
        }
        return result;
    }

    /// <summary>
    /// Fetches just provider IDs and names (lightweight).
    /// </summary>
    public async Task<List<(string Id, string Name, List<string> Models)>> GetProvidersAsync()
    {
        var result = new List<(string, string, List<string>)>();
        try
        {
            Log.Debug("[OC API] GET /provider ...");
            var resp = await _http.GetAsync("/provider");
            Log.Debug("[OC API] GET /provider → {StatusCode}", resp.StatusCode);
            if (!resp.IsSuccessStatusCode) return result;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("all", out var allProp))
            {
                foreach (var provider in allProp.EnumerateArray())
                {
                    var pid = provider.GetProperty("id").GetString() ?? "";
                    var pname = provider.GetProperty("name").GetString() ?? pid;
                    var models = new List<string>();

                    if (provider.TryGetProperty("models", out var modelsProp))
                    {
                        foreach (var model in modelsProp.EnumerateObject())
                            models.Add(model.Name);
                    }

                    result.Add((pid, pname, models));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OC API] GetProviders error");
        }
        return result;
    }

    /// <summary>
    /// Gets the current model from OpenCode config.
    /// </summary>
    public async Task<(string? ProviderId, string? ModelId)> GetCurrentModelAsync()
    {
        try
        {
            var resp = await _http.GetAsync("/config");
            if (!resp.IsSuccessStatusCode) return (null, null);

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            // Check agent.build for current model
            if (doc.RootElement.TryGetProperty("agent", out var agent) &&
                agent.TryGetProperty("build", out var build))
            {
                var pid = build.TryGetProperty("providerID", out var p) ? p.GetString() : null;
                var mid = build.TryGetProperty("modelID", out var m) ? m.GetString() : null;
                return (pid, mid);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OC API] GetCurrentModel error");
        }
        return (null, null);
    }

    /// <summary>
    /// Sets the model for OpenCode via PATCH /config.
    /// </summary>
    public async Task<bool> SetModelAsync(string providerID, string modelID)
    {
        try
        {
            var body = new
            {
                agent = new
                {
                    build = new { providerID, modelID }
                }
            };

            var content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            var request = new HttpRequestMessage(new HttpMethod("PATCH"), "/config") { Content = content };
            var resp = await _http.SendAsync(request);

            if (resp.IsSuccessStatusCode)
            {
                Log.Information("[OC API] Model set to {ProviderID}/{ModelID}", providerID, modelID);
                // Reset session so next prompt uses new model
                _activeSessionId = null;
                return true;
            }

            Log.Error("[OC API] SetModel failed: {StatusCode}", resp.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OC API] SetModel error");
            return false;
        }
    }

    /// <summary>
    /// Fetches available agents from GET /agent.
    /// Returns list of (name, description, mode) for non-hidden agents.
    /// </summary>
    public async Task<List<(string Name, string Description, string Mode)>> GetAgentsAsync()
    {
        var result = new List<(string, string, string)>();
        try
        {
            Log.Debug("[OC API] GET /agent ...");
            var resp = await _http.GetAsync("/agent");
            Log.Debug("[OC API] GET /agent → {StatusCode}", resp.StatusCode);
            if (!resp.IsSuccessStatusCode) return result;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var agent in doc.RootElement.EnumerateArray())
                {
                    // Skip hidden agents
                    if (agent.TryGetProperty("hidden", out var hidden) && hidden.GetBoolean())
                        continue;

                    var name = agent.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var desc = agent.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                    var mode = agent.TryGetProperty("mode", out var m) ? m.GetString() ?? "" : "";

                    if (!string.IsNullOrEmpty(name))
                        result.Add((name, desc, mode));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OC API] GetAgents error");
        }
        return result;
    }

    /// <summary>
    /// Aborts the currently running prompt in the active session.
    /// </summary>
    public async Task<bool> AbortAsync()
    {
        if (_activeSessionId == null)
        {
            Log.Warning("[OC API] No active session to abort");
            return false;
        }

        try
        {
            var resp = await _http.PostAsync($"/session/{_activeSessionId}/abort", null);
            if (resp.IsSuccessStatusCode)
            {
                Log.Information("[OC API] Session {SessionId} aborted", _activeSessionId);
                return true;
            }

            Log.Error("[OC API] Abort failed ({StatusCode})", resp.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OC API] Abort error");
            return false;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
