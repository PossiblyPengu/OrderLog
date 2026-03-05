using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OrderLog.Core;
using OrderLog.Helpers;

namespace OrderLog.Features.Services;

/// <summary>
/// Service for managing automatic color assignment to vendors
/// </summary>
public class VendorColorService
{
    private static readonly JsonSerializerOptions s_readOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ILogger<VendorColorService>? _logger;
    private readonly string _mappingsFilePath;
    private Dictionary<string, string> _vendorColorMap = new();
    private readonly object _lock = new();

    // Predefined color palette (10 distinct, accessible colors)
    private static readonly string[] ColorPalette = new[]
    {
        "#B56576", // Dusty Rose
        "#E63946", // Red
        "#F77F00", // Orange
        "#FCBF49", // Gold
        "#06A77D", // Teal
        "#277DA1", // Blue
        "#5A189A", // Purple
        "#D62828", // Crimson
        "#2A9D8F", // Turquoise
        "#E76F51"  // Coral
    };

    public VendorColorService(ILogger<VendorColorService>? logger = null)
    {
        _logger = logger;
        _mappingsFilePath = Path.Combine(AppPaths.OrderLogDir, "vendor-colors.json");
    }

    /// <summary>
    /// Load vendor color mappings from file
    /// </summary>
    public async Task LoadMappingsAsync()
    {
        try
        {
            if (!File.Exists(_mappingsFilePath))
            {
                _logger?.LogInformation("Vendor color mappings file not found, starting with empty map");
                lock (_lock)
                {
                    _vendorColorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                return;
            }

            var readTask = File.ReadAllTextAsync(_mappingsFilePath);
            var timeoutTask = Task.Delay(5000);
            if (await Task.WhenAny(readTask, timeoutTask) != readTask)
            {
                _logger?.LogWarning("VendorColorService: Reading mappings file is taking >5s, skipping and starting with empty map");
                lock (_lock)
                {
                    _vendorColorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                return;
            }
            var json = await readTask;
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger?.LogWarning("Vendor color mappings file is empty");
                lock (_lock)
                {
                    _vendorColorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                return;
            }

            var collection = JsonSerializer.Deserialize<VendorColorMappingCollection>(json, s_readOptions);
            if (collection == null || collection.Version != 1)
            {
                _logger?.LogWarning("Unsupported vendor color mappings version: {Version}", collection?.Version ?? 0);
                _vendorColorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            lock (_lock)
            {
                _vendorColorMap = collection.Mappings != null
                    ? new Dictionary<string, string>(collection.Mappings, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            _logger?.LogInformation("Loaded {Count} vendor color mappings", _vendorColorMap.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load vendor color mappings");
            lock (_lock)
            {
                _vendorColorMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Save vendor color mappings to file
    /// </summary>
    public async Task SaveMappingsAsync()
    {
        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(_mappingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Dictionary<string, string> snapshot;
            lock (_lock)
            {
                snapshot = new Dictionary<string, string>(_vendorColorMap, StringComparer.OrdinalIgnoreCase);
            }

            var collection = new VendorColorMappingCollection
            {
                Version = 1,
                Mappings = snapshot
            };

            var json = JsonSerializer.Serialize(collection, s_writeOptions);

            // Atomic write: write to temp file, then move
            var tempFile = _mappingsFilePath + ".tmp";
            await File.WriteAllTextAsync(tempFile, json);
            File.Move(tempFile, _mappingsFilePath, overwrite: true);

            _logger?.LogInformation("Saved {Count} vendor color mappings", _vendorColorMap.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save vendor color mappings");
            throw;
        }
    }

    /// <summary>
    /// Get color for a vendor (auto-assigns if not found)
    /// </summary>
    public string GetColorForVendor(string vendorName)
    {
        if (string.IsNullOrWhiteSpace(vendorName))
        {
            return ColorPalette[0]; // Default color
        }

        var normalizedVendor = vendorName.Trim();

        lock (_lock)
        {
            // Return existing mapping if available
            if (_vendorColorMap.TryGetValue(normalizedVendor, out var existingColor))
            {
                return existingColor;
            }

            // Auto-assign color based on hash
            var assignedColor = AssignColorByHash(normalizedVendor);
            _vendorColorMap[normalizedVendor] = assignedColor;

            // Save asynchronously (fire and forget)
            SaveMappingsAsync().SafeFireAndForget("SaveVendorColorMappings");

            _logger?.LogDebug("Auto-assigned color {Color} to vendor '{Vendor}'", assignedColor, normalizedVendor);
            return assignedColor;
        }
    }

    /// <summary>
    /// Set custom color for a vendor
    /// </summary>
    public async Task SetVendorColorAsync(string vendorName, string colorHex)
    {
        if (string.IsNullOrWhiteSpace(vendorName))
        {
            throw new ArgumentException("Vendor name cannot be empty", nameof(vendorName));
        }

        if (string.IsNullOrWhiteSpace(colorHex) || !colorHex.StartsWith("#"))
        {
            throw new ArgumentException("Invalid color hex value", nameof(colorHex));
        }

        var normalizedVendor = vendorName.Trim();

        lock (_lock)
        {
            _vendorColorMap[normalizedVendor] = colorHex;
        }

        await SaveMappingsAsync();
        _logger?.LogInformation("Set custom color {Color} for vendor '{Vendor}'", colorHex, normalizedVendor);
    }

    /// <summary>
    /// Remove vendor color mapping
    /// </summary>
    public async Task RemoveVendorColorAsync(string vendorName)
    {
        if (string.IsNullOrWhiteSpace(vendorName))
        {
            return;
        }

        var normalizedVendor = vendorName.Trim();

        lock (_lock)
        {
            _vendorColorMap.Remove(normalizedVendor);
        }

        await SaveMappingsAsync();
        _logger?.LogInformation("Removed color mapping for vendor '{Vendor}'", normalizedVendor);
    }

    /// <summary>
    /// Get all vendor-color mappings
    /// </summary>
    public Dictionary<string, string> GetAllMappings()
    {
        lock (_lock)
        {
            return new Dictionary<string, string>(_vendorColorMap, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Clear all vendor color mappings
    /// </summary>
    public async Task ClearAllMappingsAsync()
    {
        lock (_lock)
        {
            _vendorColorMap.Clear();
        }

        await SaveMappingsAsync();
        _logger?.LogInformation("Cleared all vendor color mappings");
    }

    /// <summary>
    /// Assign color based on vendor name hash
    /// </summary>
    private string AssignColorByHash(string vendorName)
    {
        // Use a simple hash to get consistent color assignment
        var hash = GetStableHashCode(vendorName.ToLowerInvariant());
        var colorIndex = Math.Abs(hash % ColorPalette.Length);
        return ColorPalette[colorIndex];
    }

    /// <summary>
    /// Get stable hash code (consistent across .NET versions)
    /// </summary>
    private int GetStableHashCode(string str)
    {
        unchecked
        {
            int hash1 = 5381;
            int hash2 = hash1;

            for (int i = 0; i < str.Length && str[i] != '\0'; i += 2)
            {
                hash1 = ((hash1 << 5) + hash1) ^ str[i];
                if (i == str.Length - 1 || str[i + 1] == '\0')
                    break;
                hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
            }

            return hash1 + (hash2 * 1566083941);
        }
    }

    /// <summary>
    /// Get the predefined color palette
    /// </summary>
    public static string[] GetColorPalette() => ColorPalette;
}

/// <summary>
/// Container for vendor color mapping persistence
/// </summary>
public class VendorColorMappingCollection
{
    public int Version { get; set; } = 1;
    public Dictionary<string, string> Mappings { get; set; } = new();
}
