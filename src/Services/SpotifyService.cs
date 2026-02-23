using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace SOUP.Services;

/// <summary>
/// Stub SpotifyService for standalone OrderLog (no WinRT dependency).
/// The full implementation requires net10.0-windows10.0.19041.0 for Windows.Media.Control.
/// </summary>
public class SpotifyService : INotifyPropertyChanged, IDisposable
{
    private static readonly Lazy<SpotifyService> _instance = new(() => new SpotifyService());
    public static SpotifyService Instance => _instance.Value;

    public bool HasMedia => false;
    public bool IsPlaying => false;
    public string TrackTitle => "";
    public string ArtistName => "";
    public BitmapImage? AlbumArt => null;

#pragma warning disable CS0067
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

    public Task InitializeAsync() => Task.CompletedTask;
    public Task PlayPauseAsync() => Task.CompletedTask;
    public Task NextTrackAsync() => Task.CompletedTask;
    public Task PreviousTrackAsync() => Task.CompletedTask;

    public void Dispose() { }
}
