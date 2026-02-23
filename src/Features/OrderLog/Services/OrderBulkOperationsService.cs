using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.Services;

/// <summary>
/// Result of a bulk operation
/// </summary>
public class BulkOperationResult
{
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool IsSuccess => FailureCount == 0;
}

/// <summary>
/// Service for performing bulk operations on order items
/// </summary>
public class OrderBulkOperationsService
{
    /// <summary>
    /// Sets the status for multiple items at once
    /// </summary>
    /// <param name="items">Items to update</param>
    /// <param name="newStatus">New status to apply</param>
    /// <returns>Result of the operation</returns>
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

    /// <summary>
    /// Archives multiple items at once
    /// </summary>
    /// <param name="items">Items to archive</param>
    /// <returns>Result of the operation</returns>
    public BulkOperationResult ArchiveBulk(IEnumerable<OrderItem> items)
    {
        var result = new BulkOperationResult();

        foreach (var item in items)
        {
            try
            {
                // Store previous status before archiving
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

    /// <summary>
    /// Unarchives multiple items at once
    /// </summary>
    /// <param name="items">Items to unarchive</param>
    /// <returns>Result of the operation</returns>
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

    /// <summary>
    /// Deletes multiple items (marks them for removal)
    /// </summary>
    /// <param name="items">Items to delete</param>
    /// <param name="itemsCollection">The collection to remove items from</param>
    /// <returns>Result of the operation</returns>
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

    /// <summary>
    /// Sets the color for multiple sticky notes at once
    /// </summary>
    /// <param name="items">Items to update (must be sticky notes)</param>
    /// <param name="colorHex">Hex color to apply (e.g., "#FF5733")</param>
    /// <returns>Result of the operation</returns>
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

    /// <summary>
    /// Links multiple items together with a shared group ID
    /// </summary>
    /// <param name="items">Items to link</param>
    /// <param name="groupId">Optional group ID (generates new one if null)</param>
    /// <returns>Result of the operation</returns>
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
