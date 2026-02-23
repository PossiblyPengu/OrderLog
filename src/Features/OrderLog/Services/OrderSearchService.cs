using System;
using System.Collections.Generic;
using System.Linq;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.Services;

/// <summary>
/// Represents a search match within an order item
/// </summary>
public class SearchMatch
{
    public string FieldName { get; set; } = string.Empty;
    public string MatchText { get; set; } = string.Empty;
    public int StartIndex { get; set; }
    public int Length { get; set; }
}

/// <summary>
/// Service for searching and filtering order items
/// </summary>
public class OrderSearchService
{
    /// <summary>
    /// Searches items by query string. Searches vendor name, transfer numbers, shipment numbers, and note content.
    /// </summary>
    /// <param name="items">Items to search</param>
    /// <param name="query">Search query (case-insensitive)</param>
    /// <returns>Filtered items matching the query</returns>
    public IEnumerable<OrderItem> Search(IEnumerable<OrderItem> items, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return items;

        var trimmedQuery = query.Trim();
        var comparison = StringComparison.OrdinalIgnoreCase;

        return items.Where(item =>
            (!string.IsNullOrEmpty(item.VendorName) && item.VendorName.Contains(trimmedQuery, comparison)) ||
            (!string.IsNullOrEmpty(item.TransferNumbers) && item.TransferNumbers.Contains(trimmedQuery, comparison)) ||
            (!string.IsNullOrEmpty(item.WhsShipmentNumbers) && item.WhsShipmentNumbers.Contains(trimmedQuery, comparison)) ||
            (!string.IsNullOrEmpty(item.NoteContent) && item.NoteContent.Contains(trimmedQuery, comparison)) ||
            (!string.IsNullOrEmpty(item.NoteTitle) && item.NoteTitle.Contains(trimmedQuery, comparison))
        );
    }

    /// <summary>
    /// Filters items by status (multi-select)
    /// </summary>
    /// <param name="items">Items to filter</param>
    /// <param name="statuses">Statuses to include (empty = all)</param>
    /// <returns>Filtered items</returns>
    public IEnumerable<OrderItem> FilterByStatus(IEnumerable<OrderItem> items, OrderItem.OrderStatus[]? statuses)
    {
        if (statuses == null || statuses.Length == 0)
            return items;

        return items.Where(item => statuses.Contains(item.Status));
    }

    /// <summary>
    /// Filters items by date range
    /// </summary>
    /// <param name="items">Items to filter</param>
    /// <param name="start">Start date (inclusive, null = no start limit)</param>
    /// <param name="end">End date (inclusive, null = no end limit)</param>
    /// <returns>Filtered items</returns>
    public IEnumerable<OrderItem> FilterByDateRange(IEnumerable<OrderItem> items, DateTime? start, DateTime? end)
    {
        var result = items;

        if (start.HasValue)
        {
            result = result.Where(item => item.CreatedAt.Date >= start.Value.Date);
        }

        if (end.HasValue)
        {
            result = result.Where(item => item.CreatedAt.Date <= end.Value.Date);
        }

        return result;
    }

    /// <summary>
    /// Filters items by color hex values
    /// </summary>
    /// <param name="items">Items to filter</param>
    /// <param name="colorHexes">Color hex values to include (empty = all)</param>
    /// <returns>Filtered items</returns>
    public IEnumerable<OrderItem> FilterByColor(IEnumerable<OrderItem> items, string[]? colorHexes)
    {
        if (colorHexes == null || colorHexes.Length == 0)
            return items;

        // Normalize colors for comparison (remove # if present, convert to uppercase)
        var normalizedColors = colorHexes
            .Select(c => c?.Replace("#", "").ToUpperInvariant())
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet();

        if (normalizedColors.Count == 0)
            return items;

        return items.Where(item =>
        {
            var itemColor = item.ColorHex?.Replace("#", "").ToUpperInvariant();
            return !string.IsNullOrEmpty(itemColor) && normalizedColors.Contains(itemColor);
        });
    }

    /// <summary>
    /// Filters items by note type (Order vs StickyNote)
    /// </summary>
    /// <param name="items">Items to filter</param>
    /// <param name="noteType">Type to filter by (null = all)</param>
    /// <returns>Filtered items</returns>
    public IEnumerable<OrderItem> FilterByNoteType(IEnumerable<OrderItem> items, NoteType? noteType)
    {
        if (!noteType.HasValue)
            return items;

        return items.Where(item => item.NoteType == noteType.Value);
    }

    /// <summary>
    /// Filters sticky notes by category (General, Todo, Reminder, Log, Idea)
    /// </summary>
    /// <param name="items">Items to filter</param>
    /// <param name="noteCategory">Category to filter by (null = all)</param>
    /// <returns>Filtered items</returns>
    public IEnumerable<OrderItem> FilterByNoteCategory(IEnumerable<OrderItem> items, NoteCategory? noteCategory)
    {
        if (!noteCategory.HasValue)
            return items;

        // Only filter sticky notes by category
        return items.Where(item =>
            item.NoteType == NoteType.StickyNote &&
            item.NoteCategory == noteCategory.Value);
    }

    /// <summary>
    /// Applies all filters in sequence
    /// </summary>
    /// <param name="items">Items to filter</param>
    /// <param name="query">Search query</param>
    /// <param name="statuses">Status filter</param>
    /// <param name="startDate">Start date filter</param>
    /// <param name="endDate">End date filter</param>
    /// <param name="colorHexes">Color filter</param>
    /// <param name="noteType">Note type filter</param>
    /// <param name="noteCategory">Note category filter (only applies to sticky notes)</param>
    /// <param name="expandLinkedGroups">Whether to expand linked order groups</param>
    /// <returns>Filtered items</returns>
    public IEnumerable<OrderItem> ApplyAllFilters(
        IEnumerable<OrderItem> items,
        string? query = null,
        OrderItem.OrderStatus[]? statuses = null,
        DateTime? startDate = null,
        DateTime? endDate = null,
        string[]? colorHexes = null,
        NoteType? noteType = null,
        NoteCategory? noteCategory = null,
        bool expandLinkedGroups = true)
    {
        var result = items;

        if (!string.IsNullOrWhiteSpace(query))
        {
            result = Search(result, query);
        }

        if (statuses != null && statuses.Length > 0)
        {
            result = FilterByStatus(result, statuses);
        }

        if (startDate.HasValue || endDate.HasValue)
        {
            result = FilterByDateRange(result, startDate, endDate);
        }

        if (colorHexes != null && colorHexes.Length > 0)
        {
            result = FilterByColor(result, colorHexes);
        }

        if (noteType.HasValue)
        {
            result = FilterByNoteType(result, noteType);
        }

        if (noteCategory.HasValue)
        {
            result = FilterByNoteCategory(result, noteCategory);
        }

        // When searching, expand results to include entire linked groups
        if (expandLinkedGroups && !string.IsNullOrWhiteSpace(query))
        {
            result = ExpandLinkedGroups(result, items);
        }

        return result;
    }

    /// <summary>
    /// Expands the filtered results to include all items in any linked group
    /// where at least one item matched the filter.
    /// </summary>
    /// <param name="filteredItems">Items that matched the filter</param>
    /// <param name="allItems">All items to search for linked group members</param>
    /// <returns>Expanded results including entire linked groups</returns>
    public IEnumerable<OrderItem> ExpandLinkedGroups(IEnumerable<OrderItem> filteredItems, IEnumerable<OrderItem> allItems)
    {
        var filteredList = filteredItems.ToList();
        
        // Get all linked group IDs from the filtered items
        var matchedGroupIds = filteredList
            .Where(item => item.LinkedGroupId.HasValue)
            .Select(item => item.LinkedGroupId!.Value)
            .Distinct()
            .ToHashSet();

        if (matchedGroupIds.Count == 0)
        {
            // No linked groups in results, return as-is
            return filteredList;
        }

        // Get all items that belong to any of the matched linked groups
        var linkedGroupMembers = allItems
            .Where(item => item.LinkedGroupId.HasValue && matchedGroupIds.Contains(item.LinkedGroupId.Value))
            .ToList();

        // Combine: original filtered items + linked group members (avoiding duplicates)
        var resultIds = filteredList.Select(i => i.Id).ToHashSet();
        var expandedResults = new List<OrderItem>(filteredList);

        foreach (var member in linkedGroupMembers)
        {
            if (!resultIds.Contains(member.Id))
            {
                expandedResults.Add(member);
                resultIds.Add(member.Id);
            }
        }

        return expandedResults;
    }

    /// <summary>
    /// Gets all search matches within an item for highlighting purposes
    /// </summary>
    /// <param name="item">Item to analyze</param>
    /// <param name="query">Search query</param>
    /// <returns>List of match locations</returns>
    public List<SearchMatch> GetMatches(OrderItem item, string query)
    {
        var matches = new List<SearchMatch>();

        if (string.IsNullOrWhiteSpace(query))
            return matches;

        var trimmedQuery = query.Trim();
        var comparison = StringComparison.OrdinalIgnoreCase;

        // Search vendor name
        if (!string.IsNullOrEmpty(item.VendorName))
        {
            var index = item.VendorName.IndexOf(trimmedQuery, comparison);
            if (index >= 0)
            {
                matches.Add(new SearchMatch
                {
                    FieldName = nameof(item.VendorName),
                    MatchText = item.VendorName.Substring(index, trimmedQuery.Length),
                    StartIndex = index,
                    Length = trimmedQuery.Length
                });
            }
        }

        // Search transfer numbers
        if (!string.IsNullOrEmpty(item.TransferNumbers))
        {
            var index = item.TransferNumbers.IndexOf(trimmedQuery, comparison);
            if (index >= 0)
            {
                matches.Add(new SearchMatch
                {
                    FieldName = nameof(item.TransferNumbers),
                    MatchText = item.TransferNumbers.Substring(index, trimmedQuery.Length),
                    StartIndex = index,
                    Length = trimmedQuery.Length
                });
            }
        }

        // Search shipment numbers
        if (!string.IsNullOrEmpty(item.WhsShipmentNumbers))
        {
            var index = item.WhsShipmentNumbers.IndexOf(trimmedQuery, comparison);
            if (index >= 0)
            {
                matches.Add(new SearchMatch
                {
                    FieldName = nameof(item.WhsShipmentNumbers),
                    MatchText = item.WhsShipmentNumbers.Substring(index, trimmedQuery.Length),
                    StartIndex = index,
                    Length = trimmedQuery.Length
                });
            }
        }

        // Search note content
        if (!string.IsNullOrEmpty(item.NoteContent))
        {
            var index = item.NoteContent.IndexOf(trimmedQuery, comparison);
            if (index >= 0)
            {
                matches.Add(new SearchMatch
                {
                    FieldName = nameof(item.NoteContent),
                    MatchText = item.NoteContent.Substring(index, Math.Min(trimmedQuery.Length, item.NoteContent.Length - index)),
                    StartIndex = index,
                    Length = trimmedQuery.Length
                });
            }
        }

        // Search note title
        if (!string.IsNullOrEmpty(item.NoteTitle))
        {
            var index = item.NoteTitle.IndexOf(trimmedQuery, comparison);
            if (index >= 0)
            {
                matches.Add(new SearchMatch
                {
                    FieldName = nameof(item.NoteTitle),
                    MatchText = item.NoteTitle.Substring(index, trimmedQuery.Length),
                    StartIndex = index,
                    Length = trimmedQuery.Length
                });
            }
        }

        return matches;
    }

    /// <summary>
    /// Checks if any filters are currently active
    /// </summary>
    public bool HasActiveFilters(
        string? query,
        OrderItem.OrderStatus[]? statuses,
        DateTime? startDate,
        DateTime? endDate,
        string[]? colorHexes,
        NoteType? noteType,
        NoteCategory? noteCategory = null)
    {
        return !string.IsNullOrWhiteSpace(query) ||
               (statuses != null && statuses.Length > 0) ||
               startDate.HasValue ||
               endDate.HasValue ||
               (colorHexes != null && colorHexes.Length > 0) ||
               noteType.HasValue ||
               noteCategory.HasValue;
    }
}
