using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OrderLog.Features.Models;

namespace OrderLog.Features.Services;

public class BulkOperationResult
{
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool IsSuccess => FailureCount == 0;
}

public class OrderBulkOperationsService
{
    public BulkOperationResult SetStatusBulk(IEnumerable<OrderItem> items, OrderItem.OrderStatus newStatus)
    {
        var result = new BulkOperationResult();

        foreach (var item in items)
        {
            try
            {
                item.Status = newStatus;
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.FailureCount++;
                result.Errors.Add($"Failed to set status for {item.VendorName ?? "item"}: {ex.Message}");
            }
        }

        return result;
    }

    public BulkOperationResult ArchiveBulk(IEnumerable<OrderItem> items)
    {
        var result = new BulkOperationResult();

        foreach (var item in items)
        {
            try
            {
                item.PreviousStatus = item.Status;
                item.IsArchived = true;
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.FailureCount++;
                result.Errors.Add($"Failed to archive {item.VendorName ?? "item"}: {ex.Message}");
            }
        }

        return result;
    }

    public BulkOperationResult UnarchiveBulk(IEnumerable<OrderItem> items)
    {
        var result = new BulkOperationResult();

        foreach (var item in items)
        {
            try
            {
                item.IsArchived = false;
                // Restore previous status or default to InProgress
                item.Status = item.PreviousStatus ?? OrderItem.OrderStatus.InProgress;
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.FailureCount++;
                result.Errors.Add($"Failed to unarchive {item.VendorName ?? "item"}: {ex.Message}");
            }
        }

        return result;
    }

    public BulkOperationResult DeleteBulk(IEnumerable<OrderItem> items, ICollection<OrderItem> itemsCollection)
    {
        var result = new BulkOperationResult();
        var itemsList = items.ToList(); // Materialize to avoid collection modification issues

        foreach (var item in itemsList)
        {
            try
            {
                itemsCollection.Remove(item);
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.FailureCount++;
                result.Errors.Add($"Failed to delete {item.VendorName ?? "item"}: {ex.Message}");
            }
        }

        return result;
    }

    public BulkOperationResult SetColorBulk(IEnumerable<OrderItem> items, string colorHex)
    {
        var result = new BulkOperationResult();

        foreach (var item in items)
        {
            try
            {
                // Only allow color changes on sticky notes
                if (item.NoteType != NoteType.StickyNote)
                {
                    result.FailureCount++;
                    result.Errors.Add($"Cannot set color for order items, only sticky notes");
                    continue;
                }

                item.ColorHex = colorHex;
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.FailureCount++;
                result.Errors.Add($"Failed to set color for {item.VendorName ?? "item"}: {ex.Message}");
            }
        }

        return result;
    }

    public BulkOperationResult LinkItemsBulk(IEnumerable<OrderItem> items, Guid? groupId = null)
    {
        var result = new BulkOperationResult();
        var linkGroupId = groupId ?? Guid.NewGuid();

        foreach (var item in items)
        {
            try
            {
                item.LinkedGroupId = linkGroupId;
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.FailureCount++;
                result.Errors.Add($"Failed to link {item.VendorName ?? "item"}: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Unlinks multiple items (clears their LinkedGroupId)
    /// </summary>
    /// <param name="items">Items to unlink</param>
    /// <returns>Result of the operation</returns>
    public BulkOperationResult UnlinkItemsBulk(IEnumerable<OrderItem> items)
    {
        var result = new BulkOperationResult();

        foreach (var item in items)
        {
            try
            {
                item.LinkedGroupId = null;
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                result.FailureCount++;
                result.Errors.Add($"Failed to unlink {item.VendorName ?? "item"}: {ex.Message}");
            }
        }

        return result;
    }
}
