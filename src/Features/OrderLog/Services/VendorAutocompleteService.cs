using System;
using System.Collections.Generic;
using System.Linq;
using OrderLog.Data;
using OrderLog.Data.Entities;

namespace OrderLog.Features.Services;

/// <summary>
/// Provides vendor autocomplete functionality by searching the dictionary database.
/// Caches all vendors in memory for fast filtering.
/// </summary>
public sealed class VendorAutocompleteService
{
    private List<VendorEntity> _cachedVendors = new();
    private bool _isLoaded;
    private readonly object _lock = new();

    public static VendorAutocompleteService Instance { get; } = new();

    private VendorAutocompleteService() { }

    public void EnsureLoaded()
    {
        if (_isLoaded) return;

        lock (_lock)
        {
            if (_isLoaded) return;

            try
            {
                _cachedVendors = DictionaryDbContext.Instance.GetAllVendors();
                _isLoaded = true;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to load vendors for autocomplete");
                _cachedVendors = new();
            }
        }
    }

    public void RefreshCache()
    {
        lock (_lock)
        {
            _isLoaded = false;
        }
        EnsureLoaded();
    }

    /// <summary>
    /// Gets all vendor display names for dropdown population
    /// </summary>
    public IReadOnlyList<string> GetAllVendorNames()
    {
        EnsureLoaded();
        var snapshot = GetVendorSnapshot();
        return snapshot
            .OrderByDescending(v => v.UseCount)
            .ThenBy(v => v.DisplayName)
            .Select(v => v.DisplayName)
            .ToList();
    }

    /// <summary>
    /// Searches vendors by partial name match (for autocomplete filtering)
    /// </summary>
    /// <param name="searchTerm">Partial text to search</param>
    /// <param name="limit">Maximum results to return</param>
    /// <returns>List of matching vendor display names, ordered by usage frequency</returns>
    public IReadOnlyList<string> Search(string searchTerm, int limit = 15)
    {
        EnsureLoaded();
        var snapshot = GetVendorSnapshot();

        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return snapshot
                .OrderByDescending(v => v.UseCount)
                .ThenBy(v => v.DisplayName)
                .Take(limit)
                .Select(v => v.DisplayName)
                .ToList();
        }

        var term = searchTerm.Trim();
        
        // Score-based matching: exact start > word start > contains
        var matches = snapshot
            .Select(v => new
            {
                Vendor = v,
                Score = CalculateMatchScore(v.DisplayName, term)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Vendor.UseCount)
            .ThenBy(x => x.Vendor.DisplayName)
            .Take(limit)
            .Select(x => x.Vendor.DisplayName)
            .ToList();

        return matches;
    }

    private List<VendorEntity> GetVendorSnapshot()
    {
        lock (_lock)
        {
            return new List<VendorEntity>(_cachedVendors);
        }
    }

    private static int CalculateMatchScore(string displayName, string term)
    {
        var name = displayName.ToUpperInvariant();
        var search = term.ToUpperInvariant();

        // Exact match
        if (name == search) return 100;

        // Starts with search term
        if (name.StartsWith(search, StringComparison.OrdinalIgnoreCase)) return 80;

        // Word starts with search term (e.g., "DOC" matches "DOC JOHNSON")
        var words = name.Split(' ', '-', '_');
        foreach (var word in words)
        {
            if (word.StartsWith(search, StringComparison.OrdinalIgnoreCase)) return 60;
        }

        // Contains search term anywhere
        if (name.Contains(search, StringComparison.OrdinalIgnoreCase)) return 40;

        return 0;
    }

    /// <summary>
    /// Increments use count for a vendor (call when vendor is selected/used)
    /// </summary>
    public void RecordVendorUsage(string vendorName)
    {
        if (string.IsNullOrWhiteSpace(vendorName)) return;

        try
        {
            DictionaryDbContext.Instance.IncrementVendorUseCount(vendorName);

            lock (_lock)
            {
                var vendor = _cachedVendors.FirstOrDefault(v =>
                    v.DisplayName.Equals(vendorName, StringComparison.OrdinalIgnoreCase) ||
                    v.Name.Equals(vendorName.ToUpperInvariant(), StringComparison.Ordinal));

                if (vendor != null)
                {
                    vendor.UseCount++;
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Failed to record vendor usage for {Vendor}", vendorName);
        }
    }
}
