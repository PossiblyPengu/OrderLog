using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using OrderLog.Features.Sync.Backends.JsonBin;
using OrderLog.Features.Sync.Models;
using OrderLog.Infrastructure.Services;

namespace OrderLog.Features.Sync.Views;

/// <summary>
/// Settings panel for the JSONBin cloud sync backend. Resolves the
/// <see cref="JsonBinSyncService"/> singleton from DI and exposes the
/// configuration / pairing UI.
/// </summary>
public partial class SyncSettingsView : UserControl
{
    private const string SettingsAppName = "OrderLogSync";

    private JsonBinSyncService? _cloud;
    private SettingsService? _settingsService;
    private DispatcherTimer? _refreshTimer;

    public SyncSettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ─── Lifecycle ─────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _cloud = App.GetService<JsonBinSyncService>();
            _settingsService = App.GetService<SettingsService>();
        }
        catch
        {
            IsEnabled = false;
            StatusLine.Text = "Sync service is unavailable.";
            return;
        }

        DeviceNameBox.Text = _cloud.Settings.DeviceName;
        RefreshStatusLines();
        RefreshPairUi();
        _cloud.PropertyChanged += OnCloudPropertyChanged;

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (s, _) => UpdateLastSyncLine();
        _refreshTimer.Start();
        UpdateLastSyncLine();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_cloud != null) _cloud.PropertyChanged -= OnCloudPropertyChanged;
        _refreshTimer?.Stop();
        _refreshTimer = null;
    }

    // ─── Status / refresh helpers ──────────────────────────────────────────

    private void OnCloudPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            RefreshStatusLines();
            RefreshPairUi();
        });
    }

    private void RefreshStatusLines()
    {
        if (_cloud == null) return;
        StatusLine.Text = _cloud.StatusText;

        PeerCountLine.Text = _cloud.IsRunning
            ? $"Polling every {_cloud.Settings.JsonBinPollIntervalSeconds}s \u2014 {_cloud.KnownPeerCount} peer(s) seen"
            : "Push/pull state via jsonbin.io \u2014 no admin or firewall changes required";
    }

    private void RefreshPairUi()
    {
        if (_cloud == null) return;
        var s = _cloud.Settings;
        var isPaired = !string.IsNullOrEmpty(s.JsonBinSharedBinId);

        PairedSummary.Visibility = isPaired ? Visibility.Visible : Visibility.Collapsed;
        JoinRow.Visibility = isPaired ? Visibility.Collapsed : Visibility.Visible;
        HostBtn.Visibility = isPaired ? Visibility.Collapsed : Visibility.Visible;
        JoinBtn.Visibility = isPaired ? Visibility.Collapsed : Visibility.Visible;
        UnpairBtn.Visibility = isPaired ? Visibility.Visible : Visibility.Collapsed;

        PairingCodeReadout.Text = s.JsonBinSharedBinId;
    }

    private void UpdateLastSyncLine()
    {
        if (_cloud == null) { LastSyncLine.Text = string.Empty; return; }
        if (_cloud.LastSyncUtc is { } lastUtc)
        {
            var elapsed = DateTime.UtcNow - lastUtc;
            string text;
            if (elapsed.TotalSeconds < 60) text = $"Last sync: {(int)elapsed.TotalSeconds}s ago";
            else if (elapsed.TotalMinutes < 60) text = $"Last sync: {(int)elapsed.TotalMinutes}m ago";
            else text = $"Last sync: {lastUtc.ToLocalTime():g}";
            LastSyncLine.Text = text;
        }
        else
        {
            LastSyncLine.Text = string.Empty;
        }
    }

    // ─── Device-name handlers ──────────────────────────────────────────────

    private async void DeviceNameBox_LostFocus(object sender, RoutedEventArgs e)
        => await CommitDeviceNameAsync();

    private async void DeviceNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await CommitDeviceNameAsync();
            Keyboard.ClearFocus();
        }
    }

    private async Task CommitDeviceNameAsync()
    {
        if (_cloud == null || _settingsService == null) return;
        var name = DeviceNameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name) || name == _cloud.Settings.DeviceName) return;
        _cloud.Settings.DeviceName = name;
        try { await _settingsService.SaveSettingsAsync(SettingsAppName, _cloud.Settings); } catch { }
    }

    // ─── JSONBin pairing handlers ──────────────────────────────────────────

    private async void StartNewSync_Click(object sender, RoutedEventArgs e)
    {
        if (_cloud == null) return;
        var key = MasterKeyBox.Password?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            KeyStatusLine.Text = "Enter your JSONBin master key first.";
            return;
        }

        SetBusy(true, "Validating key...");
        try
        {
            if (!await JsonBinSyncService.ValidateKeyAsync(key))
            {
                KeyStatusLine.Text = "That key didn't work \u2014 double-check it on jsonbin.io.";
                return;
            }

            KeyStatusLine.Text = "Creating collection and bin...";
            var code = await _cloud.PairAsHostAsync(key);
            await _cloud.StartAsync();
            KeyStatusLine.Text = "Paired \u2014 share the pairing code with your other PC.";
            MasterKeyBox.Password = string.Empty;
            RefreshPairUi();
            RefreshStatusLines();
            try { Clipboard.SetText(code); KeyStatusLine.Text += " (copied to clipboard)"; } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Pairing failed: {ex.Message}", "Sync", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false, null); }
    }

    private async void JoinSync_Click(object sender, RoutedEventArgs e)
    {
        if (_cloud == null) return;
        var key = MasterKeyBox.Password?.Trim();
        var code = JoinCodeBox.Text?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            KeyStatusLine.Text = "Enter your JSONBin master key first.";
            return;
        }
        if (string.IsNullOrEmpty(code))
        {
            KeyStatusLine.Text = "Paste the pairing code from the other PC.";
            return;
        }

        SetBusy(true, "Validating key...");
        try
        {
            if (!await JsonBinSyncService.ValidateKeyAsync(key))
            {
                KeyStatusLine.Text = "That key didn't work \u2014 double-check it on jsonbin.io.";
                return;
            }

            KeyStatusLine.Text = "Joining sync group...";
            await _cloud.JoinAsync(key, code);
            await _cloud.StartAsync();
            KeyStatusLine.Text = "Joined \u2014 changes will sync within a few seconds.";
            MasterKeyBox.Password = string.Empty;
            JoinCodeBox.Text = string.Empty;
            RefreshPairUi();
            RefreshStatusLines();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Join failed: {ex.Message}", "Sync", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { SetBusy(false, null); }
    }

    private async void Unpair_Click(object sender, RoutedEventArgs e)
    {
        if (_cloud == null) return;
        if (MessageBox.Show("Unpair this PC from JSONBin sync? You'll need to re-enter the master key and pairing code to resume.",
                "Unpair", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _cloud.UnpairAsync();
            RefreshPairUi();
            RefreshStatusLines();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unpair failed: {ex.Message}", "Sync", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyPairingCode_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(PairingCodeReadout.Text ?? string.Empty); }
        catch { }
    }

    private void SetBusy(bool busy, string? statusText)
    {
        HostBtn.IsEnabled = !busy;
        JoinBtn.IsEnabled = !busy;
        UnpairBtn.IsEnabled = !busy;
        if (statusText != null) KeyStatusLine.Text = statusText;
    }
}
