using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace OrderLog.Services;

/// <summary>
/// Handles Spotify OAuth 2.0 PKCE authentication flow.
/// Persists tokens to a JSON file in %APPDATA%\SOUP\spotify-tokens.json.
/// </summary>
public sealed class SpotifyAuthService : IDisposable
{
    private static readonly Lazy<SpotifyAuthService> _instance = new(() => new SpotifyAuthService());
    public static SpotifyAuthService Instance => _instance.Value;

    private const string TokenFilePath = "spotify-tokens.json";
    private const string AuthorizeUrl = "https://accounts.spotify.com/authorize";
    private const string TokenUrl = "https://accounts.spotify.com/api/token";
    private const string CallbackPath = "/callback/";
    private const int CallbackPort = 5789;
    private static readonly string RedirectUri = $"http://localhost:{CallbackPort}{CallbackPath}";

    // Required scopes for full player control
    private static readonly string[] Scopes =
    [
        "user-read-playback-state",
        "user-modify-playback-state",
        "user-read-currently-playing",
        "user-read-recently-played",
        "user-library-read",
        "user-library-modify",
    ];

    // TODO: Replace with your Spotify Developer App Client ID
    private const string EmbeddedClientId = "YOUR_SPOTIFY_CLIENT_ID_HERE";

    private readonly HttpClient _httpClient = new();
    private string _clientId = EmbeddedClientId;
    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _tokenExpiry = DateTime.MinValue;
    private HttpListener? _callbackListener;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public string? AccessToken => _accessToken;
    public bool IsAuthenticated => !string.IsNullOrEmpty(_refreshToken);
    public string? ClientId => _clientId;

    public event EventHandler? AuthenticationChanged;

    private SpotifyAuthService() { }

    public void Dispose()
    {
        _httpClient.Dispose();
        _callbackListener?.Close();
        _refreshLock.Dispose();
    }

    /// <summary>
    /// Initialize by loading persisted tokens.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadTokensAsync();
        if (IsAuthenticated)
        {
            Log.Information("SpotifyAuth: Found persisted refresh token, attempting token refresh");
            await RefreshAccessTokenAsync();
        }
    }

    /// <summary>
    /// Override the embedded Client ID (optional).
    /// </summary>
    public void SetClientId(string? clientId)
    {
        if (!string.IsNullOrWhiteSpace(clientId))
            _clientId = clientId;
    }

    /// <summary>
    /// Start the full PKCE auth flow: opens browser, waits for callback, exchanges code for tokens.
    /// </summary>
    public async Task<bool> AuthenticateAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_clientId))
        {
            Log.Warning("SpotifyAuth: Cannot authenticate — no Client ID configured");
            return false;
        }

        try
        {
            // Generate PKCE code verifier & challenge
            var codeVerifier = GenerateCodeVerifier();
            var codeChallenge = GenerateCodeChallenge(codeVerifier);

            // Build authorize URL
            var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            var scopeString = string.Join(" ", Scopes);
            var authUrl = $"{AuthorizeUrl}?client_id={Uri.EscapeDataString(_clientId)}" +
                          $"&response_type=code" +
                          $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                          $"&scope={Uri.EscapeDataString(scopeString)}" +
                          $"&state={Uri.EscapeDataString(state)}" +
                          $"&code_challenge_method=S256" +
                          $"&code_challenge={Uri.EscapeDataString(codeChallenge)}";

            // Start local HTTP listener for callback
            var authCode = await ListenForCallbackAsync(authUrl, state, ct);
            if (string.IsNullOrEmpty(authCode))
            {
                Log.Warning("SpotifyAuth: No auth code received from callback");
                return false;
            }

            // Exchange code for tokens
            var success = await ExchangeCodeForTokensAsync(authCode, codeVerifier);
            if (success)
            {
                await SaveTokensAsync();
                AuthenticationChanged?.Invoke(this, EventArgs.Empty);
                Log.Information("SpotifyAuth: Authentication successful");
            }
            return success;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SpotifyAuth: Authentication failed");
            return false;
        }
    }

    /// <summary>
    /// Get a valid access token, refreshing if expired.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync()
    {
        if (string.IsNullOrEmpty(_refreshToken))
            return null;

        if (DateTime.UtcNow < _tokenExpiry && !string.IsNullOrEmpty(_accessToken))
            return _accessToken;

        await RefreshAccessTokenAsync();
        return _accessToken;
    }

    /// <summary>
    /// Disconnect: clear all tokens.
    /// </summary>
    public async Task DisconnectAsync()
    {
        _accessToken = null;
        _refreshToken = null;
        _tokenExpiry = DateTime.MinValue;
        await SaveTokensAsync();
        AuthenticationChanged?.Invoke(this, EventArgs.Empty);
        Log.Information("SpotifyAuth: Disconnected");
    }

    private async Task<string?> ListenForCallbackAsync(string authUrl, string expectedState, CancellationToken ct)
    {
        _callbackListener?.Close();
        _callbackListener = new HttpListener();
        _callbackListener.Prefixes.Add($"http://localhost:{CallbackPort}/");

        try
        {
            _callbackListener.Start();
        }
        catch (HttpListenerException ex)
        {
            Log.Error(ex, "SpotifyAuth: Failed to start callback listener on port {Port}", CallbackPort);
            return null;
        }

        // Open browser for auth
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SpotifyAuth: Failed to open browser");
            _callbackListener.Stop();
            return null;
        }

        try
        {
            // Wait for the callback (timeout after 2 minutes)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(2));

            while (!cts.Token.IsCancellationRequested)
            {
                var contextTask = _callbackListener.GetContextAsync();
                var completedTask = await Task.WhenAny(contextTask, Task.Delay(-1, cts.Token)).ConfigureAwait(false);

                if (completedTask != contextTask)
                    return null;

                var context = await contextTask;
                var request = context.Request;
                var response = context.Response;

                if (request.Url?.AbsolutePath == CallbackPath)
                {
                    var query = request.QueryString;
                    var code = query["code"];
                    var state = query["state"];
                    var error = query["error"];

                    string responseHtml;
                    if (!string.IsNullOrEmpty(error))
                    {
                        responseHtml = "<html><body style='font-family:sans-serif;text-align:center;padding:50px;background:#1a1a2e;color:#e0e0e0'>" +
                                       $"<h2 style='color:#ff4444'>Authorization Failed</h2><p>{error}</p><p>You can close this tab.</p></body></html>";
                        var errorBytes = Encoding.UTF8.GetBytes(responseHtml);
                        response.ContentType = "text/html";
                        response.ContentLength64 = errorBytes.Length;
                        await response.OutputStream.WriteAsync(errorBytes, ct);
                        response.Close();
                        return null;
                    }

                    if (state != expectedState)
                    {
                        Log.Warning("SpotifyAuth: State mismatch in callback");
                        response.StatusCode = 400;
                        response.Close();
                        continue;
                    }

                    responseHtml = "<html><body style='font-family:sans-serif;text-align:center;padding:50px;background:#1a1a2e;color:#e0e0e0'>" +
                                   "<h2 style='color:#00ff88'>Connected to Spotify!</h2>" +
                                   "<p>You can close this tab and return to OrderLog.</p></body></html>";
                    var bytes = Encoding.UTF8.GetBytes(responseHtml);
                    response.ContentType = "text/html";
                    response.ContentLength64 = bytes.Length;
                    await response.OutputStream.WriteAsync(bytes, ct);
                    response.Close();

                    return code;
                }
                else
                {
                    response.StatusCode = 404;
                    response.Close();
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log.Information("SpotifyAuth: Callback listener cancelled/timed out");
        }
        finally
        {
            try { _callbackListener.Stop(); } catch { }
        }

        return null;
    }

    private async Task<bool> ExchangeCodeForTokensAsync(string code, string codeVerifier)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = _clientId!,
            ["code_verifier"] = codeVerifier,
        });

        var response = await _httpClient.PostAsync(TokenUrl, content);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Log.Error("SpotifyAuth: Token exchange failed: {Status} {Error}", response.StatusCode, error);
            return false;
        }

        var json = await response.Content.ReadAsStringAsync();
        return ParseTokenResponse(json);
    }

    private async Task RefreshAccessTokenAsync()
    {
        if (string.IsNullOrEmpty(_refreshToken) || string.IsNullOrEmpty(_clientId))
            return;

        await _refreshLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (DateTime.UtcNow < _tokenExpiry && !string.IsNullOrEmpty(_accessToken))
                return;

            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _refreshToken,
                ["client_id"] = _clientId,
            });

            var response = await _httpClient.PostAsync(TokenUrl, content);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Log.Warning("SpotifyAuth: Token refresh failed: {Status} {Error}", response.StatusCode, error);

                // If refresh token is invalid, clear auth
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    _accessToken = null;
                    _refreshToken = null;
                    _tokenExpiry = DateTime.MinValue;
                    await SaveTokensAsync();
                    AuthenticationChanged?.Invoke(this, EventArgs.Empty);
                }
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            ParseTokenResponse(json);
            await SaveTokensAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SpotifyAuth: Token refresh error");
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool ParseTokenResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            _accessToken = root.GetProperty("access_token").GetString();
            var expiresIn = root.GetProperty("expires_in").GetInt32();
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60); // 60s buffer

            // Refresh token may or may not be included in refresh responses
            if (root.TryGetProperty("refresh_token", out var rt))
            {
                _refreshToken = rt.GetString();
            }

            Log.Debug("SpotifyAuth: Token obtained, expires in {ExpiresIn}s", expiresIn);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SpotifyAuth: Failed to parse token response");
            return false;
        }
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    #region Token Persistence

    private string GetTokenFilePath() =>
        Path.Combine(Core.AppPaths.AppData, TokenFilePath);

    private async Task LoadTokensAsync()
    {
        var path = GetTokenFilePath();
        if (!File.Exists(path)) return;

        try
        {
            var json = await File.ReadAllTextAsync(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("client_id", out var cid))
                _clientId ??= cid.GetString();
            if (root.TryGetProperty("refresh_token", out var rt))
                _refreshToken = rt.GetString();
            if (root.TryGetProperty("access_token", out var at))
                _accessToken = at.GetString();
            if (root.TryGetProperty("token_expiry", out var te))
            {
                if (DateTime.TryParse(te.GetString(), out var expiry))
                    _tokenExpiry = expiry;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SpotifyAuth: Failed to load tokens");
        }
    }

    private async Task SaveTokensAsync()
    {
        var path = GetTokenFilePath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var data = new Dictionary<string, string?>();
            if (!string.IsNullOrEmpty(_clientId))
                data["client_id"] = _clientId;
            if (!string.IsNullOrEmpty(_refreshToken))
                data["refresh_token"] = _refreshToken;
            if (!string.IsNullOrEmpty(_accessToken))
                data["access_token"] = _accessToken;
            data["token_expiry"] = _tokenExpiry.ToString("O");

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SpotifyAuth: Failed to save tokens");
        }
    }

    #endregion
}
