using System;

namespace OrderLog.Features.Sync.Models;

/// <summary>
/// Records that a given <see cref="OrderId"/> was deleted at <see cref="DeletedAt"/>.
/// Used to propagate deletes through P2P sync (without tombstones, a peer that
/// hadn't yet learned about an item would resurrect it on next exchange).
/// </summary>
public sealed class Tombstone
{
    public Guid OrderId { get; set; }
    public DateTime DeletedAt { get; set; } = DateTime.UtcNow;
}
