using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Logging;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.Services;

/// <summary>
/// Service for copying and pasting OrderLog items via clipboard.
/// Serializes items to JSON format with metadata wrapper for version compatibility.
/// </summary>
public class OrderLogClipboardService
{
    private readonly ILogger<OrderLogClipboardService>? _logger;

    public OrderLogClipboardService(ILogger<OrderLogClipboardService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Copy items to clipboard as JSON with metadata wrapper
    /// </summary>
    /// <param name="items">Items to copy</param>
    public void CopyToClipboard(IEnumerable<OrderItem> items)
    {
        try
        {
            var itemsList = items.ToList();

            var wrapper = new ClipboardData
            {
                Type = "OrderLogClipboard",
                Version = 1,
                Items = itemsList
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(wrapper, options);
            Clipboard.SetText(json);

            _logger?.LogInformation("Copied {Count} items to clipboard", itemsList.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to copy items to clipboard");
            throw;
        }
    }

    /// <summary>
    /// Try to paste items from clipboard
    /// </summary>
    /// <param name="items">Output list of pasted items (transformed for pasting)</param>
    /// <returns>True if successful, false if clipboard doesn't contain valid OrderLog data</returns>
    public bool TryPasteFromClipboard(out List<OrderItem> items)
    {
        items = new List<OrderItem>();

        try
        {
            if (!Clipboard.ContainsText())
            {
                _logger?.LogDebug("Clipboard does not contain text");
                return false;
            }

            var json = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger?.LogDebug("Clipboard text is empty");
                return false;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var wrapper = JsonSerializer.Deserialize<ClipboardData>(json, options);

            if (wrapper?.Type != "OrderLogClipboard")
            {
                _logger?.LogDebug("Clipboard does not contain OrderLog data (type: {Type})", wrapper?.Type ?? "null");
                return false;
            }

            if (wrapper.Version != 1)
            {
                _logger?.LogWarning("Unsupported clipboard version: {Version}", wrapper.Version);
                return false;
            }

            if (wrapper.Items == null || wrapper.Items.Count == 0)
            {
                _logger?.LogDebug("Clipboard contains no items");
                return false;
            }

            items = TransformForPasting(wrapper.Items);
            _logger?.LogInformation("Pasted {Count} items from clipboard", items.Count);
            return true;
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "Failed to parse clipboard JSON");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to paste from clipboard");
            return false;
        }
    }

    /// <summary>
    /// Clone items without using clipboard (for Duplicate command)
    /// </summary>
    /// <param name="items">Items to clone</param>
    /// <returns>Cloned items with new GUIDs and reset state</returns>
    public List<OrderItem> CloneItems(IEnumerable<OrderItem> items)
    {
        return TransformForPasting(items.ToList());
    }

    /// <summary>
    /// Transform items for pasting: generate new GUIDs, reset state, handle linked groups
    /// </summary>
    /// <param name="originals">Original items to transform</param>
    /// <returns>Transformed items ready for insertion</returns>
    private List<OrderItem> TransformForPasting(List<OrderItem> originals)
    {
        // Check if any original items had linked groups
        var originalGroupIds = originals
            .Where(i => i.LinkedGroupId != null)
            .Select(i => i.LinkedGroupId)
            .Distinct()
            .ToList();

        var hasLinkedItems = originalGroupIds.Count > 0;

        // If pasting linked items, create a NEW group ID for all pasted items
        // This keeps pasted items linked to each other but separate from originals
        var newGroupId = hasLinkedItems ? Guid.NewGuid() : (Guid?)null;

        return originals.Select(orig => new OrderItem
        {
            // NEW values
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,

            // RESET values
            Status = OrderItem.OrderStatus.NotReady,
            IsArchived = false,
            StartedAt = null,
            CompletedAt = null,
            AccumulatedTimeTicks = 0,
            PreviousStatus = null,

            // PRESERVED values
            NoteType = orig.NoteType,
            VendorName = orig.VendorName,
            NoteTitle = orig.NoteTitle,
            TransferNumbers = orig.TransferNumbers,
            WhsShipmentNumbers = orig.WhsShipmentNumbers,
            NoteContent = orig.NoteContent,
            ColorHex = orig.ColorHex,
            NoteCategory = orig.NoteCategory,

            // LINKED GROUP handling: If original was linked, assign NEW group ID
            LinkedGroupId = orig.LinkedGroupId != null ? newGroupId : null,

            // Order will be set by ViewModel based on insertion position
            Order = 0
        }).ToList();
    }

    /// <summary>
    /// Clipboard data wrapper for version compatibility
    /// </summary>
    private class ClipboardData
    {
        public string Type { get; set; } = "";
        public int Version { get; set; }
        public List<OrderItem> Items { get; set; } = new();
    }
}
