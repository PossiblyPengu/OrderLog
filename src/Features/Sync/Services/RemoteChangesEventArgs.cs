using System;
using System.Collections.Generic;
using OrderLog.Features.Models;
using OrderLog.Features.Sync.Models;

namespace OrderLog.Features.Sync.Services;

/// <summary>
/// Event payload raised by a sync backend when it has merged a batch of
/// remote changes that the view-model needs to apply locally.
/// </summary>
public sealed class RemoteChangesEventArgs : EventArgs
{
    public IReadOnlyList<OrderItem> Items { get; }
    public IReadOnlyList<Tombstone> Tombstones { get; }
    public string SourceDeviceName { get; }

    public RemoteChangesEventArgs(IReadOnlyList<OrderItem> items, IReadOnlyList<Tombstone> tombstones, string sourceDevice)
    {
        Items = items;
        Tombstones = tombstones;
        SourceDeviceName = sourceDevice;
    }
}
