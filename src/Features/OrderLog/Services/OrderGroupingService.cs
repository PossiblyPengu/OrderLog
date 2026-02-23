using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SOUP.Features.OrderLog.Models;
using SOUP.Features.OrderLog.ViewModels;

namespace SOUP.Features.OrderLog.Services;

/// <summary>
/// Helper service to build grouped display collections and status groups
/// from flat OrderItem collections. Extracted from OrderLogViewModel to
/// simplify view-model responsibilities and make grouping logic testable.
/// </summary>
public class OrderGroupingService
{
    public enum OrderLogSortMode
    {
        Status = 0,
        CreatedAt = 1,
        VendorName = 2
    }

    public ObservableCollection<OrderItemGroup> BuildDisplayCollection(IEnumerable<OrderItem> sourceItems, bool sortByStatus, bool sortStatusDescending, OrderLogSortMode sortMode)
    {
        var display = new ObservableCollection<OrderItemGroup>();

        var srcList = sourceItems.Where(i => i.IsRenderable).ToList();

        var groups = new List<OrderItemGroup>();
        var seenGroupIds = new HashSet<Guid?>();

        foreach (var item in srcList)
        {
            // Treat Guid.Empty as no group (historical/invalid data may have empty GUIDs)
            var gid = item.LinkedGroupId;
            if (gid == null || gid == Guid.Empty)
            {
                groups.Add(new OrderItemGroup(new[] { item }));
            }
            else
            {
                if (seenGroupIds.Contains(gid)) continue;
                seenGroupIds.Add(gid);
                var members = srcList.Where(i => i.LinkedGroupId == gid).ToList();
                if (members.Count > 0)
                    groups.Add(new OrderItemGroup(members));
            }
        }

        var noteGroups = groups.Where(g => g.Members.All(m => m.IsStickyNote)).ToList();
        var orderGroups = groups.Except(noteGroups).ToList();

        if (sortByStatus)
        {
            static int StatusPriority(OrderItem.OrderStatus s) => s switch
            {
                OrderItem.OrderStatus.NotReady => 0,
                OrderItem.OrderStatus.OnDeck => 1,
                OrderItem.OrderStatus.InProgress => 2,
                OrderItem.OrderStatus.Done => 3,
                _ => 4
            };

            Func<OrderItemGroup, int> keySelector = g => g.Members.Min(m => StatusPriority(m.Status));
            orderGroups = sortStatusDescending
                ? orderGroups.OrderByDescending(keySelector).ToList()
                : orderGroups.OrderBy(keySelector).ToList();
        }

        // If not sorting by status, apply the selected sort mode
        if (!sortByStatus)
        {
            switch (sortMode)
            {
                case OrderLogSortMode.CreatedAt:
                    orderGroups = sortStatusDescending
                        ? orderGroups.OrderByDescending(g => g.Members.Min(m => m.CreatedAt)).ToList()
                        : orderGroups.OrderBy(g => g.Members.Min(m => m.CreatedAt)).ToList();
                    break;
                case OrderLogSortMode.VendorName:
                    orderGroups = sortStatusDescending
                        ? orderGroups.OrderByDescending(g => g.Members.Min(m => m.VendorName)).ToList()
                        : orderGroups.OrderBy(g => g.Members.Min(m => m.VendorName)).ToList();
                    break;
                default:
                    break;
            }
        }

        foreach (var g in orderGroups)
        {
            var ordered = g.Members.OrderBy(m => m.CreatedAt).ToList();
            g.Members.Clear();
            foreach (var m in ordered) g.Members.Add(m);
            display.Add(g);
        }

        foreach (var g in noteGroups)
        {
            display.Add(g);
        }

        return display;
    }

    public void PopulateStatusGroups(IEnumerable<OrderItem> sourceItems,
        ObservableCollection<OrderItemGroup> notReadyItems,
        ObservableCollection<OrderItemGroup> onDeckItems,
        ObservableCollection<OrderItemGroup> inProgressItems)
    {
        notReadyItems.Clear();
        onDeckItems.Clear();
        inProgressItems.Clear();

        // Sort source items by CreatedAt (newest first) to get date-sorted groups
        var srcList = sourceItems
            .Where(i => i.IsRenderable && !i.IsStickyNote)
            .OrderByDescending(i => i.CreatedAt)
            .ToList();
        var seenGroupIds = new HashSet<Guid?>();

        // Create groups, preserving date order
        var allGroups = new List<(OrderItemGroup Group, DateTime SortDate)>();

        foreach (var item in srcList)
        {
            OrderItemGroup group;
            var gid = item.LinkedGroupId;
            if (gid == null || gid == Guid.Empty)
            {
                group = new OrderItemGroup(new[] { item });
            }
            else
            {
                if (seenGroupIds.Contains(gid)) continue;
                seenGroupIds.Add(gid);
                var members = srcList.Where(i => i.LinkedGroupId == gid).ToList();
                if (members.Count == 0) continue;
                group = new OrderItemGroup(members);
            }

            // Use the newest item's date for group sorting
            var sortDate = group.Members.Max(m => m.CreatedAt);
            allGroups.Add((group, sortDate));
        }

        // Sort groups by date (newest first) and add to appropriate collection
        foreach (var (group, _) in allGroups.OrderByDescending(g => g.SortDate))
        {
            var status = group.First?.Status ?? OrderItem.OrderStatus.NotReady;
            switch (status)
            {
                case OrderItem.OrderStatus.NotReady:
                    notReadyItems.Add(group);
                    break;
                case OrderItem.OrderStatus.OnDeck:
                    onDeckItems.Add(group);
                    break;
                case OrderItem.OrderStatus.InProgress:
                    inProgressItems.Add(group);
                    break;
            }
        }
    }
}
