using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OrderLog.Features.Sync.Backends.JsonBin;

/// <summary>
/// Thin wrapper around jsonbin.io v3 REST endpoints. Stateless aside from
/// the master key. Endpoints used (all available on the free tier):
///   POST   /v3/b              Create a bin
///   GET    /v3/b/{id}/latest  Read current bin content
///   PUT    /v3/b/{id}         Update bin content
/// We deliberately avoid the Collections API \u2014 it's limited to 1
/// collection on the free tier and would 403 once that quota is reached.
/// </summary>
public sealed class JsonBinClient
{
    private const string BaseUrl = "https://api.jsonbin.io/v3";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: true) },
    };

    private readonly HttpClient _http;
    private readonly string _masterKey;
    private readonly ILogger? _logger;

    public JsonBinClient(string masterKey, ILogger? logger = null)
    {
        _masterKey = masterKey ?? throw new ArgumentNullException(nameof(masterKey));
        _logger = logger;
        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        })
        {
            Timeout = TimeSpan.FromSeconds(20),
            BaseAddress = new Uri(BaseUrl + "/"),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OrderLog/1.0");
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Add("X-Master-Key", _masterKey);
        req.Headers.Add("X-Bin-Meta", "false");
        return req;
    }

    /// <summary>
    /// Validates the master key by attempting a harmless read against a
    /// non-existent bin id. A valid key returns 404 (bin not found);
    /// an invalid key returns 401.
    /// </summary>
    public async Task<bool> ValidateKeyAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = NewRequest(HttpMethod.Get, "b/000000000000000000000000/latest");
            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            // 401/403 \u21d2 bad key; anything else (404, 400, 200) \u21d2 key accepted
            return resp.StatusCode != HttpStatusCode.Unauthorized
                && resp.StatusCode != HttpStatusCode.Forbidden;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "JSONBin key validation failed");
            return false;
        }
    }

    /// <summary>Creates a new private bin and returns its id.</summary>
    public async Task<string> CreateBinAsync<T>(string binName, T initialContent, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Post, "b");
        req.Headers.Add("X-Bin-Name", binName);
        req.Headers.Add("X-Bin-Private", "true");
        var json = JsonSerializer.Serialize(initialContent, JsonOptions);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            _logger?.LogWarning("JSONBin POST /b failed: {Status} {Body}", resp.StatusCode, body);
            resp.EnsureSuccessStatusCode();
        }

        var id = ExtractIdFromCreate(body);
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("JSONBin: bin create returned no id");
        return id;
    }

    /// <summary>Reads a bin's latest contents and deserialises as T.</summary>
    public async Task<T?> ReadBinAsync<T>(string binId, CancellationToken ct = default) where T : class
    {
        using var req = NewRequest(HttpMethod.Get, $"b/{Uri.EscapeDataString(binId)}/latest");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        if (!resp.IsSuccessStatusCode)
        {
            _logger?.LogWarning("JSONBin GET /b/{Bin} failed: {Status} {Body}", binId, resp.StatusCode, body);
            resp.EnsureSuccessStatusCode();
        }

        // JSONBin may or may not honour X-Bin-Meta=false depending on the
        // endpoint / plan tier. Be defensive: if the response is the wrapped
        // form ({ "record": {...}, "metadata": {...} }), unwrap it first. We
        // CANNOT just try-deserialise-as-T because that silently succeeds with
        // an empty object when T has different fields than the wrapper.
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("record", out var rec)
                && doc.RootElement.TryGetProperty("metadata", out _))
            {
                return JsonSerializer.Deserialize<T>(rec.GetRawText(), JsonOptions);
            }
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "JSONBin GET /b/{Bin} deserialise failed. Body head: {Head}",
                binId, body.Length > 200 ? body.Substring(0, 200) + "..." : body);
            return null;
        }
    }

    /// <summary>Replaces a bin's contents with the given payload.</summary>
    public async Task UpdateBinAsync<T>(string binId, T payload, CancellationToken ct = default)
    {
        using var req = NewRequest(HttpMethod.Put, $"b/{Uri.EscapeDataString(binId)}");
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger?.LogWarning("JSONBin PUT {Bin} failed: {Status} {Body}", binId, resp.StatusCode, body);
            resp.EnsureSuccessStatusCode();
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static string? ExtractIdFromCreate(string body)
    {
        // With X-Bin-Meta=false the create response is typically the raw record,
        // but the id may still appear under "metadata.id" depending on plan.
        // Be defensive and try several shapes.
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("metadata", out var md)
                    && md.TryGetProperty("id", out var mdId)
                    && mdId.ValueKind == JsonValueKind.String)
                    return mdId.GetString();
                if (doc.RootElement.TryGetProperty("id", out var topId)
                    && topId.ValueKind == JsonValueKind.String)
                    return topId.GetString();
            }
        }
        catch { }
        return null;
    }
}
