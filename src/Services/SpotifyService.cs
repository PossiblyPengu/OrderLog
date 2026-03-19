using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Serilog;
using OrderLog.Helpers;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace OrderLog.Services;

/// <summary>
/// Represents a recently played track entry.
/// </summary>
public record RecentTrack(string Title, string Artist, BitmapImage? AlbumArt, DateTime PlayedAt);

/// <summary>
/// Spotify playback service using Windows Media Session API (SMTC) for auto-detection
/// and metadata, with Spotify Web API as an optional enhancement for richer control.
/// </summary>
public class SpotifyService : INotifyPropertyChanged, IDisposable
{
    private static readonly Lazy<SpotifyService> _instance = new(() => new SpotifyService());
    public static SpotifyService Instance => _instance.Value;

    // Windows API for sending key events
    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_VOLUME_UP = 0xAF;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_MUTE = 0xAD;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    // Pre-compiled regex patterns for extracting/removing featured artists from titles
#pragma warning disable MA0023 // Capture groups are intentional — used by ExtractFeaturedArtists
    private static readonly System.Text.RegularExpressions.Regex[] FeatExtractPatterns =
    [
        new(@"\(feat\.?\s+(.+?)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\(ft\.?\s+(.+?)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\(with\s+(.+?)\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\[feat\.?\s+(.+?)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\[ft\.?\s+(.+?)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\[with\s+(.+?)\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\s+-\s+feat\.?\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\s+-\s+ft\.?\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
    ];
#pragma warning restore MA0023

    private static readonly System.Text.RegularExpressions.Regex[] FeatRemovePatterns =
    [
        new(@"\s*\(feat\.?\s+.+?\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\s*\(ft\.?\s+.+?\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\s*\(with\s+.+?\)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\s*\[feat\.?\s+.+?\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\s*\[ft\.?\s+.+?\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\s*\[with\s+.+?\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\s*-\s+feat\.?\s+.+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
        new(@"\s*-\s+ft\.?\s+.+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled),
    ];

    // ── Private state ──────────────────────────────────────────────────────
    private string _trackTitle = "Not Playing";
    private string _artistName = "";
    private string _albumName = "";
    private string _deviceName = "";
    private bool _isPlaying;
    private bool _hasMedia;
    private BitmapImage? _albumArt;
    private string? _lastTrackKey;
    private readonly System.Timers.Timer _pollTimer;
    private readonly System.Timers.Timer _positionTimer;
    private DateTime _lastUserAction = DateTime.MinValue;
    private const int UserActionCooldownMs = 3000;
    private CancellationTokenSource? _artRetryCts;
    private DateTime _lastValidMediaTimestamp = DateTime.MinValue;
    private static readonly TimeSpan MediaHoldDuration = TimeSpan.FromSeconds(6);

    private TimeSpan _trackPosition;
    private TimeSpan _trackDuration;
    private bool _isShuffleEnabled;
    private int _repeatMode; // 0=Off, 1=Track, 2=List
    private Color _dominantColor = Colors.Transparent;
    private bool _isCurrentTrackLiked;
    private int _volumePercent = 50;

    private const int MaxRecentTracks = 20;
    private readonly ObservableCollection<RecentTrack> _recentTracks = new();
    private readonly HashSet<string> _likedTrackKeys = new();

    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;

    // ── Events ─────────────────────────────────────────────────────────────
    public event EventHandler<string>? TrackChanged;

    // ── Public properties ──────────────────────────────────────────────────

    public string TrackTitle
    {
        get => _trackTitle;
        private set { if (_trackTitle != value) { _trackTitle = value; OnPropertyChanged(); } }
    }

    public string ArtistName
    {
        get => _artistName;
        private set { if (_artistName != value) { _artistName = value; OnPropertyChanged(); } }
    }

    public string AlbumName
    {
        get => _albumName;
        private set { if (_albumName != value) { _albumName = value; OnPropertyChanged(); } }
    }

    public string DeviceName
    {
        get => _deviceName;
        private set { if (_deviceName != value) { _deviceName = value; OnPropertyChanged(); } }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set { if (_isPlaying != value) { _isPlaying = value; OnPropertyChanged(); } }
    }

    public bool HasMedia
    {
        get => _hasMedia;
        private set { if (_hasMedia != value) { _hasMedia = value; OnPropertyChanged(); } }
    }

    public BitmapImage? AlbumArt
    {
        get => _albumArt;
        private set { _albumArt = value; OnPropertyChanged(); }
    }

    public TimeSpan TrackPosition
    {
        get => _trackPosition;
        private set { if (_trackPosition != value) { _trackPosition = value; OnPropertyChanged(); } }
    }

    public TimeSpan TrackDuration
    {
        get => _trackDuration;
        private set { if (_trackDuration != value) { _trackDuration = value; OnPropertyChanged(); } }
    }

    public bool IsShuffleEnabled
    {
        get => _isShuffleEnabled;
        private set { if (_isShuffleEnabled != value) { _isShuffleEnabled = value; OnPropertyChanged(); } }
    }

    /// <summary>0=Off, 1=Track, 2=List</summary>
    public int RepeatMode
    {
        get => _repeatMode;
        private set { if (_repeatMode != value) { _repeatMode = value; OnPropertyChanged(); } }
    }

    public Color DominantColor
    {
        get => _dominantColor;
        private set { if (_dominantColor != value) { _dominantColor = value; OnPropertyChanged(); } }
    }

    public int VolumePercent
    {
        get => _volumePercent;
        private set { if (_volumePercent != value) { _volumePercent = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<RecentTrack> RecentTracks => _recentTracks;

    public bool IsCurrentTrackLiked
    {
        get => _isCurrentTrackLiked;
        private set { if (_isCurrentTrackLiked != value) { _isCurrentTrackLiked = value; OnPropertyChanged(); } }
    }

    // ── Constructor / Dispose ──────────────────────────────────────────────
    private SpotifyService()
    {
        _pollTimer = new System.Timers.Timer(2000);
        _pollTimer.Elapsed += (s, e) => PollMediaSessionAsync().SafeFireAndForget("PollMediaSession");

        _positionTimer = new System.Timers.Timer(500);
        _positionTimer.Elapsed += (s, e) => UpdatePositionAsync().SafeFireAndForget("UpdatePosition");
    }

    public void Dispose()
    {
        _pollTimer?.Stop();
        _pollTimer?.Dispose();
        _positionTimer?.Stop();
        _positionTimer?.Dispose();

        if (_sessionManager != null)
        {
            _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
            _sessionManager.SessionsChanged -= OnSessionsChanged;
        }

        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        GC.SuppressFinalize(this);
        Log.Information("SpotifyService disposed");
    }

    // ── Initialization ─────────────────────────────────────────────────────
    public async Task InitializeAsync()
    {
        // Initialize SMTC (auto-detects any media player)
        try
        {
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
            _sessionManager.SessionsChanged += OnSessionsChanged;
            await UpdateCurrentSessionAsync();

            if (!_pollTimer.Enabled) _pollTimer.Start();
            if (!_positionTimer.Enabled) _positionTimer.Start();

            Log.Information("SpotifyService initialized with Windows Media Session API");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize Windows Media Session API, falling back to window polling");
            if (!_pollTimer.Enabled) _pollTimer.Start();
        }

    }

    // ── SMTC Session Management ────────────────────────────────────────────
    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        UpdateCurrentSessionAsync().SafeFireAndForget("OnCurrentSessionChanged");
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        UpdateCurrentSessionAsync().SafeFireAndForget("OnSessionsChanged");
    }

    private async Task UpdateCurrentSessionAsync()
    {
        try
        {
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            }

            var sessions = _sessionManager?.GetSessions();
            _currentSession = null;

            if (sessions != null)
            {
                foreach (var session in sessions)
                {
                    if (session.SourceAppUserModelId?.Contains("Spotify", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _currentSession = session;
                        break;
                    }
                }
                _currentSession ??= _sessionManager?.GetCurrentSession();
            }

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                await UpdateMediaInfoAsync();
            }
            else
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (ShouldHoldLastMedia())
                        return;

                    ClearMediaState();
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update current session");
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        UpdateMediaInfoAsync().SafeFireAndForget("OnMediaPropertiesChanged");
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        UpdateMediaInfoAsync().SafeFireAndForget("OnPlaybackInfoChanged");
    }

    // ── Polling ────────────────────────────────────────────────────────────
    private async Task PollMediaSessionAsync()
    {
        if ((DateTime.Now - _lastUserAction).TotalMilliseconds < UserActionCooldownMs)
            return;

        await UpdateMediaInfoAsync();
    }

    /// <summary>
    /// Syncs position from SMTC every 500ms and stores a timestamp so the UI
    /// can interpolate smoothly between syncs.
    /// </summary>
    private Task UpdatePositionAsync()
    {
        if (!_isPlaying) return Task.CompletedTask;
        if (_currentSession == null) return Task.CompletedTask;

        try
        {
            var timeline = _currentSession.GetTimelineProperties();
            if (timeline != null)
            {
                var position = timeline.Position;
                var duration = timeline.EndTime - timeline.StartTime;

                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    _lastSyncPosition = position;
                    _lastSyncTime = DateTime.UtcNow;
                    TrackPosition = position;
                    if (duration > TimeSpan.Zero)
                        TrackDuration = duration;
                });
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to update track position");
        }
        return Task.CompletedTask;
    }

    // ── Interpolated position for smooth UI ──────────────────────────────
    private TimeSpan _lastSyncPosition;
    private DateTime _lastSyncTime = DateTime.MinValue;

    /// <summary>
    /// Returns the estimated current position by interpolating from the last
    /// SMTC sync point. Call this from the UI render loop for fluid progress.
    /// </summary>
    public TimeSpan InterpolatedPosition
    {
        get
        {
            if (!_isPlaying || _lastSyncTime == DateTime.MinValue)
                return _trackPosition;

            var elapsed = DateTime.UtcNow - _lastSyncTime;
            var estimated = _lastSyncPosition + elapsed;

            if (_trackDuration > TimeSpan.Zero && estimated > _trackDuration)
                return _trackDuration;

            return estimated;
        }
    }

    // ── SMTC Media Info Update ─────────────────────────────────────────────
    private async Task UpdateMediaInfoAsync()
    {
        if (_currentSession == null)
        {
            await UpdateCurrentSessionAsync();
            return;
        }

        try
        {
            var mediaProperties = await _currentSession.TryGetMediaPropertiesAsync();
            var playbackInfo = _currentSession.GetPlaybackInfo();

            string? title = mediaProperties?.Title;
            string? artist = mediaProperties?.Artist;
            string? albumArtist = mediaProperties?.AlbumArtist;

            Log.Debug("SMTC Raw Data - Title: '{Title}', Artist: '{Artist}', AlbumArtist: '{AlbumArtist}'",
                title, artist, albumArtist);

            string? featuredArtists = ExtractFeaturedArtists(title);

            string completeArtist = artist ?? "";
            if (!string.IsNullOrEmpty(featuredArtists) &&
                !string.IsNullOrEmpty(artist) &&
                !artist.Contains(featuredArtists, StringComparison.OrdinalIgnoreCase))
            {
                completeArtist = $"{artist}, {featuredArtists}";
            }

            string cleanTitle = title ?? "";
            if (!string.IsNullOrEmpty(featuredArtists))
                cleanTitle = RemoveFeaturedFromTitle(title);

            bool isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            bool shuffleActive = playbackInfo?.IsShuffleActive ?? false;
            int repeatMode = playbackInfo?.AutoRepeatMode switch
            {
                global::Windows.Media.MediaPlaybackAutoRepeatMode.Track => 1,
                global::Windows.Media.MediaPlaybackAutoRepeatMode.List => 2,
                _ => 0
            };

            TimeSpan position = TimeSpan.Zero;
            TimeSpan duration = TimeSpan.Zero;
            try
            {
                var timeline = _currentSession.GetTimelineProperties();
                if (timeline != null)
                {
                    position = timeline.Position;
                    duration = timeline.EndTime - timeline.StartTime;
                }
            }
            catch { /* Timeline not always available */ }

            string currentTrackKey = $"{title ?? ""}|{artist ?? ""}";
            bool trackChanged = _lastTrackKey != currentTrackKey;
            _lastTrackKey = currentTrackKey;

            BitmapImage? albumArt = null;
            if (mediaProperties?.Thumbnail != null)
            {
                try
                {
                    using var stream = await mediaProperties.Thumbnail.OpenReadAsync();
                    albumArt = await ConvertToBitmapImageAsync(stream);
                    if (albumArt == null)
                        Log.Debug("Album art thumbnail present but conversion returned null for '{Title}'", title);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to load album art thumbnail for '{Title}'", title);
                }
            }
            else
            {
                Log.Debug("No thumbnail available from SMTC for '{Title}'", title);
            }

            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (!string.IsNullOrEmpty(title))
                {
                    _lastValidMediaTimestamp = DateTime.UtcNow;
                    HasMedia = true;
                    TrackTitle = cleanTitle;
                    ArtistName = completeArtist;
                    IsPlaying = isPlaying;
                    IsShuffleEnabled = shuffleActive;
                    RepeatMode = repeatMode;
                    TrackPosition = position;
                    if (duration > TimeSpan.Zero)
                        TrackDuration = duration;

                    IsCurrentTrackLiked = _likedTrackKeys.Contains(currentTrackKey);

                    if (albumArt != null)
                    {
                        CancelArtRetry();
                        AlbumArt = albumArt;
                        ExtractDominantColor(albumArt);
                    }
                    else if (trackChanged)
                    {
                        AlbumArt = null;
                        CancelArtRetry();
                        var newCts = new CancellationTokenSource();
                        _artRetryCts = newCts;
                        RetryFetchAlbumArtAsync(currentTrackKey, newCts.Token).SafeFireAndForget("RetryFetchAlbumArt");
                    }
                    else if (AlbumArt == null && _artRetryCts == null)
                    {
                        var newCts = new CancellationTokenSource();
                        _artRetryCts = newCts;
                        RetryFetchAlbumArtAsync(currentTrackKey, newCts.Token).SafeFireAndForget("RetryFetchAlbumArt:Poll");
                    }

                    if (trackChanged && !string.IsNullOrEmpty(cleanTitle))
                    {
                        AddToRecentTracks(cleanTitle, completeArtist, AlbumArt ?? albumArt);
                        TrackChanged?.Invoke(this, cleanTitle);
                    }
                }
                else
                {
                    if (ShouldHoldLastMedia())
                        return;

                    ClearMediaState();
                }
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update media info");
        }
    }

    private bool ShouldHoldLastMedia()
    {
        if (_lastValidMediaTimestamp == DateTime.MinValue)
            return false;

        return (DateTime.UtcNow - _lastValidMediaTimestamp) < MediaHoldDuration;
    }

    private void ClearMediaState()
    {
        HasMedia = false;
        TrackTitle = "No media playing";
        ArtistName = "";
        IsPlaying = false;
        AlbumArt = null;
        TrackPosition = TimeSpan.Zero;
        TrackDuration = TimeSpan.Zero;
        _lastValidMediaTimestamp = DateTime.MinValue;
    }

    // ── Playback Control ───────────────────────────────────────────────────
    public Task PlayPauseAsync()
    {
        _lastUserAction = DateTime.Now;
        SendMediaKey(VK_MEDIA_PLAY_PAUSE);
        IsPlaying = !IsPlaying;
        return Task.CompletedTask;
    }

    public Task NextTrackAsync()
    {
        _lastUserAction = DateTime.Now;
        SendMediaKey(VK_MEDIA_NEXT_TRACK);
        PostUserActionPollsAsync().SafeFireAndForget("NextTrackPolls");
        return Task.CompletedTask;
    }

    public Task PreviousTrackAsync()
    {
        _lastUserAction = DateTime.Now;
        SendMediaKey(VK_MEDIA_PREV_TRACK);
        PostUserActionPollsAsync().SafeFireAndForget("PreviousTrackPolls");
        return Task.CompletedTask;
    }

    public Task VolumeUpAsync()
    {
        SendMediaKey(VK_VOLUME_UP);
        SendMediaKey(VK_VOLUME_UP);
        return Task.CompletedTask;
    }

    public Task VolumeDownAsync()
    {
        SendMediaKey(VK_VOLUME_DOWN);
        SendMediaKey(VK_VOLUME_DOWN);
        return Task.CompletedTask;
    }

    public Task VolumeMuteAsync()
    {
        SendMediaKey(VK_VOLUME_MUTE);
        return Task.CompletedTask;
    }

    public void ToggleLikeCurrentTrack()
    {
        if (!HasMedia) return;

        if (string.IsNullOrEmpty(_lastTrackKey)) return;
        if (_likedTrackKeys.Contains(_lastTrackKey))
        {
            _likedTrackKeys.Remove(_lastTrackKey);
            IsCurrentTrackLiked = false;
        }
        else
        {
            _likedTrackKeys.Add(_lastTrackKey);
            IsCurrentTrackLiked = true;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────
    private async Task PostUserActionPollsAsync()
    {
        try
        {
            await Task.Delay(700);
            await UpdateMediaInfoAsync();

            await Task.Delay(1200);
            await UpdateMediaInfoAsync();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Post-user-action media poll failed");
        }
    }

    private void AddToRecentTracks(string title, string artist, BitmapImage? art)
    {
        if (_recentTracks.Count > 0 && _recentTracks[0].Title == title && _recentTracks[0].Artist == artist)
            return;

        _recentTracks.Insert(0, new RecentTrack(title, artist, art, DateTime.Now));

        while (_recentTracks.Count > MaxRecentTracks)
            _recentTracks.RemoveAt(_recentTracks.Count - 1);
    }

    private void ExtractDominantColor(BitmapImage image)
    {
        try
        {
            var formatted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int width = formatted.PixelWidth;
            int height = formatted.PixelHeight;

            int sampleStep = Math.Max(1, Math.Min(width, height) / 20);
            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            formatted.CopyPixels(pixels, stride, 0);

            double bestScore = 0;
            Color bestColor = Colors.Gray;

            for (int y = 0; y < height; y += sampleStep)
            {
                for (int x = 0; x < width; x += sampleStep)
                {
                    int idx = y * stride + x * 4;
                    byte b = pixels[idx];
                    byte g = pixels[idx + 1];
                    byte r = pixels[idx + 2];

                    int brightness = (r + g + b) / 3;
                    if (brightness < 30 || brightness > 230) continue;

                    int max = Math.Max(r, Math.Max(g, b));
                    int min = Math.Min(r, Math.Min(g, b));
                    double saturation = max == 0 ? 0 : (double)(max - min) / max;
                    double score = saturation * (0.5 + brightness / 510.0);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestColor = Color.FromRgb(r, g, b);
                    }
                }
            }

            DominantColor = bestScore > 0.1 ? bestColor : Color.FromRgb(100, 100, 100);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to extract dominant color from album art");
            DominantColor = Color.FromRgb(100, 100, 100);
        }
    }

    private async Task<BitmapImage?> ConvertToBitmapImageAsync(IRandomAccessStreamWithContentType stream)
    {
        try
        {
            byte[] imageBytes;
            using (var memoryStream = new MemoryStream())
            {
                var inputStream = stream.AsStreamForRead();
                await inputStream.CopyToAsync(memoryStream);
                imageBytes = memoryStream.ToArray();
            }

            if (imageBytes.Length < 100)
            {
                Log.Debug("Album art stream too small ({Bytes} bytes), likely empty or corrupt", imageBytes.Length);
                return null;
            }

            BitmapImage? bitmap = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = new MemoryStream(imageBytes);
                bitmap.EndInit();
                bitmap.Freeze();
            });

            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to convert stream to BitmapImage");
            return null;
        }
    }

    private async Task RetryFetchAlbumArtAsync(string expectedTrackKey, CancellationToken token)
    {
        try
        {
            var delays = new[] { 300, 600, 1000, 1500, 2500, 4000, 6000, 10000 };

            foreach (var d in delays)
            {
                if (token.IsCancellationRequested) return;
                await Task.Delay(d, token);
                if (token.IsCancellationRequested) return;

                try
                {
                    if (_currentSession == null) continue;
                    var mediaProps = await _currentSession.TryGetMediaPropertiesAsync();
                    var title = mediaProps?.Title ?? "";
                    var artist = mediaProps?.Artist ?? "";
                    var key = $"{title}|{artist}";

                    if (!string.Equals(key, expectedTrackKey, StringComparison.Ordinal))
                        return;

                    if (mediaProps?.Thumbnail != null)
                    {
                        using var stream = await mediaProps.Thumbnail.OpenReadAsync();
                        var img = await ConvertToBitmapImageAsync(stream);
                        if (img != null)
                        {
                            Log.Debug("Album art retry succeeded after {Delay}ms delay", d);
                            CancelArtRetry();
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                AlbumArt = img;
                                ExtractDominantColor(img);
                            });
                            return;
                        }
                        Log.Debug("Album art retry: thumbnail present but conversion failed (delay={Delay}ms)", d);
                    }
                    else
                    {
                        Log.Debug("Album art retry: no thumbnail available yet (delay={Delay}ms)", d);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Log.Debug(ex, "RetryFetchAlbumArtAsync attempt failed"); }
            }
        }
        finally
        {
            var current = Interlocked.CompareExchange(ref _artRetryCts, null, null);
            if (current != null && !token.IsCancellationRequested)
            {
                Log.Debug("Album art retries exhausted for track key '{TrackKey}' — will retry on next poll", expectedTrackKey);
                Interlocked.CompareExchange(ref _artRetryCts, null, current);
            }
        }
    }

    private void CancelArtRetry()
    {
        var old = Interlocked.Exchange(ref _artRetryCts, null);
        if (old != null)
        {
            try { old.Cancel(); } catch { }
            try { old.Dispose(); } catch { }
        }
    }

    private void SendMediaKey(byte keyCode)
    {
        try
        {
            keybd_event(keyCode, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            keybd_event(keyCode, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to send media key");
        }
    }

    private static string? ExtractFeaturedArtists(string? title)
    {
        if (string.IsNullOrEmpty(title))
            return null;

        foreach (var regex in FeatExtractPatterns)
        {
            var match = regex.Match(title);
            if (match.Success && match.Groups.Count > 1)
                return match.Groups[1].Value.Trim();
        }

        return null;
    }

    private static string RemoveFeaturedFromTitle(string? title)
    {
        if (string.IsNullOrEmpty(title))
            return "";

        string result = title;
        foreach (var regex in FeatRemovePatterns)
            result = regex.Replace(result, "");

        return result.Trim();
    }

    // ── INotifyPropertyChanged ─────────────────────────────────────────────
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
