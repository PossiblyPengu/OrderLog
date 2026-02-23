using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.Services;

/// <summary>
/// Service for exporting OrderLog data to various formats.
/// </summary>
public interface IOrderLogExportService
{
    Task ExportToCsvAsync(IEnumerable<OrderItem> items, string filePath);
    Task ExportToJsonAsync(IEnumerable<OrderItem> items, string filePath);
    Task<(bool Success, List<OrderItem> Items, string ErrorMessage)> ImportFromCsvAsync(string filePath);
}

public class OrderLogExportService : IOrderLogExportService
{
    private readonly ILogger<OrderLogExportService>? _logger;

    public OrderLogExportService(ILogger<OrderLogExportService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Exports order items to CSV format.
    /// </summary>
    /// <param name="items">Items to export</param>
    /// <param name="filePath">Full path to save the file</param>
    public async Task ExportToCsvAsync(IEnumerable<OrderItem> items, string filePath)
    {
        try
        {
            var itemsList = items.ToList();
            var csvContent = BuildCsvContent(itemsList);

            await File.WriteAllTextAsync(filePath, csvContent);

            _logger?.LogInformation("Exported {Count} orders to CSV: {FilePath}", itemsList.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to export to CSV");
            throw;
        }
    }

    /// <summary>
    /// Exports order items to JSON format.
    /// </summary>
    /// <param name="items">Items to export</param>
    /// <param name="filePath">Full path to save the file</param>
    public async Task ExportToJsonAsync(IEnumerable<OrderItem> items, string filePath)
    {
        try
        {
            var itemsList = items.ToList();
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var jsonContent = JsonSerializer.Serialize(itemsList, options);
            await File.WriteAllTextAsync(filePath, jsonContent);

            _logger?.LogInformation("Exported {Count} orders to JSON: {FilePath}", itemsList.Count, filePath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to export to JSON");
            throw;
        }
    }

    private string BuildCsvContent(List<OrderItem> items)
    {
        var csv = new StringBuilder();

        // Header
        csv.AppendLine("Type,Vendor/Title,Status,Created,Completed,Transfer Numbers,WHS Shipment Numbers,Note Content");

        // Rows
        foreach (var item in items)
        {
            var type = item.NoteType == NoteType.StickyNote ? "Note" : "Order";
            var vendorOrTitle = item.NoteType == NoteType.StickyNote
                ? EscapeCsvField(item.NoteTitle ?? "")
                : EscapeCsvField(item.VendorName ?? "");
            var status = item.Status.ToString();
            var created = item.CreatedAt.ToString("yyyy-MM-dd HH:mm");
            var completed = item.CompletedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
            var transfers = EscapeCsvField(item.TransferNumbers ?? "");
            var shipments = EscapeCsvField(item.WhsShipmentNumbers ?? "");
            var noteContent = item.NoteType == NoteType.StickyNote
                ? EscapeCsvField(item.NoteContent ?? "")
                : "";

            csv.AppendLine($"{type},{vendorOrTitle},{status},{created},{completed},{transfers},{shipments},{noteContent}");
        }

        return csv.ToString();
    }

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }

    /// <summary>
    /// Imports order items from CSV format.
    /// </summary>
    /// <param name="filePath">Full path to the CSV file</param>
    /// <returns>Tuple with success status, imported items, and error message</returns>
    public async Task<(bool Success, List<OrderItem> Items, string ErrorMessage)> ImportFromCsvAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return (false, new List<OrderItem>(), "File not found");
            }

            var lines = await File.ReadAllLinesAsync(filePath);

            if (lines.Length < 2)
            {
                return (false, new List<OrderItem>(), "CSV file is empty or contains only the header");
            }

            // Skip header line
            var dataLines = lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
            var importedItems = new List<OrderItem>();
            var errors = new List<string>();

            for (int i = 0; i < dataLines.Count; i++)
            {
                var lineNum = i + 2; // +2 because we skipped header and arrays are 0-indexed
                var line = dataLines[i];

                try
                {
                    var item = ParseCsvLine(line);
                    if (item != null)
                    {
                        importedItems.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Line {lineNum}: {ex.Message}");
                    _logger?.LogWarning("Failed to parse CSV line {LineNum}: {Error}", lineNum, ex.Message);
                }
            }

            if (importedItems.Count == 0)
            {
                var errorMsg = errors.Count > 0
                    ? $"Failed to import any items. Errors:\n{string.Join("\n", errors.Take(5))}"
                    : "No valid items found in CSV";
                return (false, new List<OrderItem>(), errorMsg);
            }

            _logger?.LogInformation("Imported {Count} orders from CSV: {FilePath}", importedItems.Count, filePath);

            // Return the items for the ViewModel to save
            return (true, importedItems, string.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to import from CSV");
            return (false, new List<OrderItem>(), $"Import failed: {ex.Message}");
        }
    }

    private OrderItem? ParseCsvLine(string line)
    {
        var fields = ParseCsvFields(line);

        if (fields.Count < 8)
        {
            throw new FormatException($"Expected 8 fields, got {fields.Count}");
        }

        // Type,Vendor/Title,Status,Created,Completed,Transfer Numbers,WHS Shipment Numbers,Note Content
        var type = fields[0].Trim();
        var vendorOrTitle = fields[1].Trim();
        var statusStr = fields[2].Trim();
        var createdStr = fields[3].Trim();
        var completedStr = fields[4].Trim();
        var transfers = fields[5].Trim();
        var shipments = fields[6].Trim();
        var noteContent = fields[7].Trim();

        // Parse note type
        var noteType = type.Equals("Note", StringComparison.OrdinalIgnoreCase)
            ? NoteType.StickyNote
            : NoteType.Order;

        // Parse status
        if (!Enum.TryParse<OrderItem.OrderStatus>(statusStr, true, out var status))
        {
            throw new FormatException($"Invalid status: {statusStr}");
        }

        // Parse created date
        if (!DateTime.TryParse(createdStr, out var createdAt))
        {
            throw new FormatException($"Invalid created date: {createdStr}");
        }

        // Parse completed date (optional)
        DateTime? completedAt = null;
        if (!string.IsNullOrWhiteSpace(completedStr))
        {
            if (DateTime.TryParse(completedStr, out var completed))
            {
                completedAt = completed;
            }
        }

        var item = new OrderItem
        {
            Id = Guid.NewGuid(),
            NoteType = noteType,
            Status = status,
            CreatedAt = createdAt,
            CompletedAt = completedAt,
            TransferNumbers = transfers ?? string.Empty,
            WhsShipmentNumbers = shipments ?? string.Empty
        };

        if (noteType == NoteType.StickyNote)
        {
            item.NoteTitle = vendorOrTitle ?? string.Empty;
            item.NoteContent = noteContent ?? string.Empty;
        }
        else
        {
            item.VendorName = vendorOrTitle ?? string.Empty;
        }

        return item;
    }

    private List<string> ParseCsvFields(string line)
    {
        var fields = new List<string>();
        var currentField = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote
                    currentField.Append('"');
                    i++; // Skip next quote
                }
                else
                {
                    // Toggle quote mode
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                // Field separator
                fields.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }

        // Add last field
        fields.Add(currentField.ToString());

        return fields;
    }
}
