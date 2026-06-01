using System;

namespace OrderLog.Features.Sync.Models;

/// <summary>
/// Persisted cloud sync configuration. Stored via the existing
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
    /// Hosted sync endpoint URL, DPAPI-encrypted then base64-encoded. Empty
    /// when cloud sync has not been configured.
    /// Property name is retained for backward compatibility.
    /// </summary>
    public string JsonBinMasterKeyProtected { get; set; } = string.Empty;

    /// <summary>
    /// Pairing code identifying the single shared state file that all peers
    /// in this sync group read and write. Property name is retained for
    /// backward compatibility.
    /// </summary>
    public string JsonBinSharedBinId { get; set; } = string.Empty;

    /// <summary>How often to poll the shared bin for peer changes.</summary>
    public int JsonBinPollIntervalSeconds { get; set; } = 10;
}
