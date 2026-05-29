using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OrderLog.Features.Models;

namespace OrderLog.Features.Services;

/// <summary>
/// Event args raised after a successful save, carrying the items that were
/// persisted. Used by P2P sync to compute the diff against its last-broadcast
/// snapshot.
/// </summary>
public sealed class OrderItemsSavedEventArgs : EventArgs
{
    public IReadOnlyList<OrderItem> Items { get; }
    /// <summary>Item Ids that were present in the DB before but not in this save (i.e. removed).</summary>
    public IReadOnlyList<Guid> DeletedIds { get; }

    public OrderItemsSavedEventArgs(IReadOnlyList<OrderItem> items, IReadOnlyList<Guid> deletedIds)
    {
        Items = items;
        DeletedIds = deletedIds;
    }
}

public interface IOrderLogService : IDisposable
{
    Task<List<OrderItem>> LoadAsync();
    Task SaveAsync(List<OrderItem> items);

    /// <summary>
    /// Raised after every successful <see cref="SaveAsync"/> with the items
    /// that were persisted and the ids that were removed.
    /// </summary>
    event EventHandler<OrderItemsSavedEventArgs>? ItemsSaved;
}
