using System;
using System.Collections.Generic;
using OrderLog.Features.Models;
using OrderLog.Features.Sync.Models;

namespace OrderLog.Features.Sync.Backends.JsonBin;

/// <summary>
/// Wire format for the single shared bin that all peers in a sync group read
/// and write. Schema is "shared inventory + per-device presence":
///   - <c>Items</c>: one entry per logical order, deduplicated by Guid. The
///     newest <c>UpdatedAt</c> wins on merge. Sticky notes are excluded.
///   - <c>Tombstones</c>: deletes that have happened anywhere.
///   - <c>Devices</c>: a tiny per-device presence dictionary so users can see
///     who has been participating, without storing per-device copies of the
///     same items (the bug that broke v1 \u2014 we were hitting the JSONBin
///     100KB free-tier limit because the same items lived in every slot).
/// </summary>
public sealed class JsonBinSharedState
{
    /// <summary>Schema version. Bumped to 2 with the dedup refactor.</summary>
    public int Version { get; set; } = 2;

    public List<OrderItem> Items { get; set; } = new();

    public List<Tombstone> Tombstones { get; set; } = new();

    /// <summary>Per-device presence metadata. Optional; tiny payload.</summary>
    public Dictionary<string, JsonBinDeviceMeta> Devices { get; set; } = new();
}

/// <summary>
/// Tiny presence record per device. Just enough to identify who pushed last.
/// </summary>
public sealed class JsonBinDeviceMeta
{
    public string DeviceName { get; set; } = string.Empty;
    public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
}
