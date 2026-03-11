using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace OrderLog.Services;

/// <summary>
/// Spotify Web API playback state returned by GET /v1/me/player.
/// </summary>
public class SpotifyPlaybackState
{
    public bool IsPlaying { get; set; }
    public int ProgressMs { get; set; }
    public int DurationMs { get; set; }
    public string? TrackId { get; set; }
    public string? TrackName { get; set; }
    public string? ArtistName { get; set; }
    public string? AlbumName { get; set; }
    public string? AlbumArtUrl { get; set; }
    public int VolumePercent { get; set; }
    public bool ShuffleState { get; set; }
    public string RepeatState { get; set; } = "off"; // off, track, context
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
}

/// <summary>
/// A recently played track from the Spotify API.
/// </summary>
public class SpotifyRecentTrack
{
    public string? TrackId { get; set; }
    public string? TrackName { get; set; }
    public string? ArtistName { get; set; }
    public string? AlbumArtUrl { get; set; }
    public DateTime PlayedAt { get; set; }
}

/// <summary>
/// HTTP client wrapper for Spotify Web API endpoints.
/// </summary>
public sealed class SpotifyWebApiClient : IDisposable
{
    private static readonly Lazy<SpotifyWebApiClient> _instance = new(() => new SpotifyWebApiClient());
    public static SpotifyWebApiClient Instance => _instance.Value;

    private const string BaseUrl = "https://api.spotify.com/v1";
    private readonly HttpClient _httpClient = new();

    private SpotifyWebApiClient()
    {
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task<bool> EnsureAuthHeaderAsync()
    {
        var token = await SpotifyAuthService.Instance.GetAccessTokenAsync();
        if (string.IsNullOrEmpty(token))
            return false;

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return true;
    }

    /// <summary>
    /// Get current playback state.
    /// </summary>
    public async Task<SpotifyPlaybackState?> GetPlaybackStateAsync()
    {
        if (!await EnsureAuthHeaderAsync()) return null;

        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/me/player");

            // 204 = no active device
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return null;

            if (!response.IsSuccessStatusCode)
            {
                Log.Debug("SpotifyAPI: GetPlaybackState failed: {Status}", response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var state = new SpotifyPlaybackState
            {
                IsPlaying = root.GetProperty("is_playing").GetBoolean(),
                ProgressMs = root.TryGetProperty("progress_ms", out var prog) ? prog.GetInt32() : 0,
                ShuffleState = root.TryGetProperty("shuffle_state", out var shuf) && shuf.GetBoolean(),
                RepeatState = root.TryGetProperty("repeat_state", out var rep) ? rep.GetString() ?? "off" : "off",
            };

            // Device
            if (root.TryGetProperty("device", out var device))
            {
                state.VolumePercent = device.TryGetProperty("volume_percent", out var vol) ? vol.GetInt32() : 50;
                state.DeviceId = device.TryGetProperty("id", out var did) ? did.GetString() : null;
                state.DeviceName = device.TryGetProperty("name", out var dn) ? dn.GetString() : null;
            }

            // Track item
            if (root.TryGetProperty("item", out var item))
            {
                state.TrackId = item.TryGetProperty("id", out var tid) ? tid.GetString() : null;
                state.TrackName = item.TryGetProperty("name", out var tn) ? tn.GetString() : null;
                state.DurationMs = item.TryGetProperty("duration_ms", out var dur) ? dur.GetInt32() : 0;

                // Artists
                if (item.TryGetProperty("artists", out var artists) && artists.GetArrayLength() > 0)
                {
                    var names = new List<string>();
                    foreach (var artist in artists.EnumerateArray())
                    {
                        if (artist.TryGetProperty("name", out var an))
                            names.Add(an.GetString() ?? "");
                    }
                    state.ArtistName = string.Join(", ", names);
                }

                // Album art
                if (item.TryGetProperty("album", out var album))
                {
                    state.AlbumName = album.TryGetProperty("name", out var aln) ? aln.GetString() : null;
                    if (album.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
                    {
                        // Prefer medium-sized image (300px)
                        string? bestUrl = null;
                        int bestSize = 0;
                        foreach (var img in images.EnumerateArray())
                        {
                            var url = img.TryGetProperty("url", out var u) ? u.GetString() : null;
                            var width = img.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                            if (url != null && (bestUrl == null || Math.Abs(width - 300) < Math.Abs(bestSize - 300)))
                            {
                                bestUrl = url;
                                bestSize = width;
                            }
                        }
                        state.AlbumArtUrl = bestUrl;
                    }
                }
            }

            return state;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SpotifyAPI: GetPlaybackState error");
            return null;
        }
    }

    /// <summary>
    /// Resume playback.
    /// </summary>
    public async Task<bool> PlayAsync()
    {
        if (!await EnsureAuthHeaderAsync()) return false;
        try
        {
            var response = await _httpClient.PutAsync($"{BaseUrl}/me/player/play", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { Log.Debug(ex, "SpotifyAPI: Play failed"); return false; }
    }

    /// <summary>
    /// Pause playback.
    /// </summary>
    public async Task<bool> PauseAsync()
    {
        if (!await EnsureAuthHeaderAsync()) return false;
        try
        {
            var response = await _httpClient.PutAsync($"{BaseUrl}/me/player/pause", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { Log.Debug(ex, "SpotifyAPI: Pause failed"); return false; }
    }

    /// <summary>
    /// Skip to next track.
    /// </summary>
    public async Task<bool> NextAsync()
    {
        if (!await EnsureAuthHeaderAsync()) return false;
        try
        {
            var response = await _httpClient.PostAsync($"{BaseUrl}/me/player/next", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { Log.Debug(ex, "SpotifyAPI: Next failed"); return false; }
    }

    /// <summary>
    /// Skip to previous track.
    /// </summary>
    public async Task<bool> PreviousAsync()
    {
        if (!await EnsureAuthHeaderAsync()) return false;
        try
        {
            var response = await _httpClient.PostAsync($"{BaseUrl}/me/player/previous", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { Log.Debug(ex, "SpotifyAPI: Previous failed"); return false; }
    }

    /// <summary>
    /// Seek to position in currently playing track.
    /// </summary>
    public async Task<bool> SeekAsync(int positionMs)
    {
        if (!await EnsureAuthHeaderAsync()) return false;
        try
        {
            var response = await _httpClient.PutAsync($"{BaseUrl}/me/player/seek?position_ms={positionMs}", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { Log.Debug(ex, "SpotifyAPI: Seek failed"); return false; }
    }

    /// <summary>
    /// Set volume (0-100).
    /// </summary>
    public async Task<bool> SetVolumeAsync(int volumePercent)
    {
        if (!await EnsureAuthHeaderAsync()) return false;
        volumePercent = Math.Clamp(volumePercent, 0, 100);
        try
        {
            var response = await _httpClient.PutAsync($"{BaseUrl}/me/player/volume?volume_percent={volumePercent}", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { Log.Debug(ex, "SpotifyAPI: SetVolume failed"); return false; }
    }

    /// <summary>
    /// Toggle shuffle state.
    /// </summary>
    public async Task<bool> SetShuffleAsync(bool state)
    {
        if (!await EnsureAuthHeaderAsync()) return false;
        try
        {
            var response = await _httpClient.PutAsync($"{BaseUrl}/me/player/shuffle?state={state.ToString().ToLowerInvariant()}", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { Log.Debug(ex, "SpotifyAPI: SetShuffle failed"); return false; }
    }

    /// <summary>
    /// Set repeat mode: "off", "track", or "context".
    /// </summary>
    public async Task<bool> SetRepeatAsync(string state)
    {
        if (!await EnsureAuthHeaderAsync()) return false;
        try
        {
            var response = await _httpClient.PutAsync($"{BaseUrl}/me/player/repeat?state={state}", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { Log.Debug(ex, "SpotifyAPI: SetRepeat failed"); return false; }
    }

    /// <summary>
    /// Save a track to user's library ("like" it).
    /// </summary>
    public async Task<bool> SaveTrackAsync(string trackId)
    {
        if (!await EnsureAuthHeaderAsync()) return false;
        try
        {
            var response = await _httpClient.PutAsync($"{BaseUrl}/me/tracks?ids={trackId}", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { Log.Debug(ex, "SpotifyAPI: SaveTrack failed"); return false; }
    }

    /// <summary>
    /// Remove a track from user's library ("unlike" it).
    /// </summary>
    public async Task<bool> RemoveTrackAsync(string trackId)
    {
        if (!await EnsureAuthHeaderAsync()) return false;
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/me/tracks?ids={trackId}");
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { Log.Debug(ex, "SpotifyAPI: RemoveTrack failed"); return false; }
    }

    /// <summary>
    /// Check if a track is saved in user's library.
    /// </summary>
    public async Task<bool> IsTrackSavedAsync(string trackId)
    {
        if (!await EnsureAuthHeaderAsync()) return false;
        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/me/tracks/contains?ids={trackId}");
            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement;
            return arr.GetArrayLength() > 0 && arr[0].GetBoolean();
        }
        catch (Exception ex) { Log.Debug(ex, "SpotifyAPI: IsTrackSaved failed"); return false; }
    }

    /// <summary>
    /// Get recently played tracks (up to 20).
    /// </summary>
    public async Task<List<SpotifyRecentTrack>> GetRecentlyPlayedAsync(int limit = 20)
    {
        var tracks = new List<SpotifyRecentTrack>();
        if (!await EnsureAuthHeaderAsync()) return tracks;

        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/me/player/recently-played?limit={limit}");
            if (!response.IsSuccessStatusCode) return tracks;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var track = new SpotifyRecentTrack();

                    if (item.TryGetProperty("played_at", out var pa) && DateTime.TryParse(pa.GetString(), out var playedAt))
                        track.PlayedAt = playedAt;

                    if (item.TryGetProperty("track", out var t))
                    {
                        track.TrackId = t.TryGetProperty("id", out var tid) ? tid.GetString() : null;
                        track.TrackName = t.TryGetProperty("name", out var tn) ? tn.GetString() : null;

                        if (t.TryGetProperty("artists", out var artists) && artists.GetArrayLength() > 0)
                        {
                            var names = new List<string>();
                            foreach (var a in artists.EnumerateArray())
                            {
                                if (a.TryGetProperty("name", out var an))
                                    names.Add(an.GetString() ?? "");
                            }
                            track.ArtistName = string.Join(", ", names);
                        }

                        if (t.TryGetProperty("album", out var album) &&
                            album.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
                        {
                            track.AlbumArtUrl = images[0].TryGetProperty("url", out var u) ? u.GetString() : null;
                        }
                    }

                    tracks.Add(track);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SpotifyAPI: GetRecentlyPlayed failed");
        }

        return tracks;
    }

    /// <summary>
    /// Get the current playback queue.
    /// </summary>
    public async Task<List<SpotifyRecentTrack>> GetQueueAsync()
    {
        var tracks = new List<SpotifyRecentTrack>();
        if (!await EnsureAuthHeaderAsync()) return tracks;

        try
        {
            var response = await _httpClient.GetAsync($"{BaseUrl}/me/player/queue");
            if (!response.IsSuccessStatusCode) return tracks;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("queue", out var queue))
            {
                foreach (var t in queue.EnumerateArray())
                {
                    var track = new SpotifyRecentTrack
                    {
                        TrackId = t.TryGetProperty("id", out var tid) ? tid.GetString() : null,
                        TrackName = t.TryGetProperty("name", out var tn) ? tn.GetString() : null,
                    };

                    if (t.TryGetProperty("artists", out var artists) && artists.GetArrayLength() > 0)
                    {
                        var names = new List<string>();
                        foreach (var a in artists.EnumerateArray())
                        {
                            if (a.TryGetProperty("name", out var an))
                                names.Add(an.GetString() ?? "");
                        }
                        track.ArtistName = string.Join(", ", names);
                    }

                    if (t.TryGetProperty("album", out var album) &&
                        album.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
                    {
                        track.AlbumArtUrl = images[0].TryGetProperty("url", out var u) ? u.GetString() : null;
                    }

                    tracks.Add(track);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SpotifyAPI: GetQueue failed");
        }

        return tracks;
    }

    /// <summary>
    /// Download an image from a URL and return as BitmapImage.
    /// </summary>
    public async Task<System.Windows.Media.Imaging.BitmapImage?> DownloadImageAsync(string url)
    {
        try
        {
            var bytes = await _httpClient.GetByteArrayAsync(url);
            if (bytes.Length < 100) return null;

            System.Windows.Media.Imaging.BitmapImage? bitmap = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new System.IO.MemoryStream(bytes);
                bitmap.EndInit();
                bitmap.Freeze();
            });
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "SpotifyAPI: Image download failed");
            return null;
        }
    }
}
