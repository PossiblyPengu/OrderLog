using System;

namespace OrderLog.Features.Sync.Models;

/// <summary>
/// Persisted JSONBin sync configuration. Stored via the existing
/// <c>SettingsService</c> under app name "OrderLogSync".
/// </summary>
public sealed class SyncSettings
{
    /// <summary>Master enable switch for the sync subsystem.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Stable per-device identifier (generated on first run).</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Friendly name shown to other peers. Defaults to the machine name.</summary>
    public string DeviceName { get; set; } = Environment.MachineName;

    /// <summary>
    /// JSONBin master key, DPAPI-encrypted then base64-encoded. Empty when
    /// cloud sync has not been configured.
    /// </summary>
    public string JsonBinMasterKeyProtected { get; set; } = string.Empty;

    /// <summary>
    /// Id of the single shared bin that all peers in this sync group read
    /// and write. Also acts as the pairing code copied between PCs.
    /// </summary>
    public string JsonBinSharedBinId { get; set; } = string.Empty;

    /// <summary>How often to poll the shared bin for peer changes.</summary>
    public int JsonBinPollIntervalSeconds { get; set; } = 10;
}
