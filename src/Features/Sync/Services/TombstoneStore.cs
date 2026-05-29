using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OrderLog.Features.Sync.Models;

namespace OrderLog.Features.Sync.Services;

/// <summary>
/// Persists tombstones (deleted item Ids + timestamps) so deletes propagate
/// reliably across P2P sync. Tombstones older than <see cref="RetentionDays"/>
/// are pruned on save.
/// </summary>
public sealed class TombstoneStore
{
    private const int RetentionDays = 60;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly string _filePath;
    private readonly ILogger<TombstoneStore>? _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ConcurrentDictionary<Guid, DateTime> _byId = new();

    public TombstoneStore(ILogger<TombstoneStore>? logger = null)
    {
        _logger = logger;
        Directory.CreateDirectory(Core.AppPaths.OrderLogDir);
        _filePath = Path.Combine(Core.AppPaths.OrderLogDir, "tombstones.json");
        _ = LoadAsync();
    }

    public IReadOnlyCollection<Tombstone> Snapshot()
        => _byId.Select(kv => new Tombstone { OrderId = kv.Key, DeletedAt = kv.Value }).ToArray();

    public bool IsDeleted(Guid id, out DateTime deletedAt)
        => _byId.TryGetValue(id, out deletedAt);

    public async Task AddRangeAsync(IEnumerable<Guid> ids, DateTime? deletedAtUtc = null)
    {
        var ts = deletedAtUtc ?? DateTime.UtcNow;
        var added = false;
        foreach (var id in ids)
        {
            if (_byId.TryGetValue(id, out var existing))
            {
                if (ts > existing)
                {
                    _byId[id] = ts;
                    added = true;
                }
            }
            else
            {
                _byId[id] = ts;
                added = true;
            }
        }
        if (added) await SaveAsync();
    }

    public async Task MergeAsync(IEnumerable<Tombstone> incoming)
    {
        var changed = false;
        foreach (var t in incoming)
        {
            if (_byId.TryGetValue(t.OrderId, out var existing))
            {
                if (t.DeletedAt > existing)
                {
                    _byId[t.OrderId] = t.DeletedAt;
                    changed = true;
                }
            }
            else
            {
                _byId[t.OrderId] = t.DeletedAt;
                changed = true;
            }
        }
        if (changed) await SaveAsync();
    }

    private async Task LoadAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = await File.ReadAllTextAsync(_filePath);
            var items = JsonSerializer.Deserialize<List<Tombstone>>(json, JsonOptions);
            if (items != null)
            {
                var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
                foreach (var t in items)
                {
                    if (t.DeletedAt < cutoff) continue;
                    _byId[t.OrderId] = t.DeletedAt;
                }
            }
            _logger?.LogInformation("TombstoneStore loaded {Count} tombstones", _byId.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load tombstones, starting empty");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task SaveAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            // Prune old tombstones during save.
            var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
            foreach (var kv in _byId.ToArray())
            {
                if (kv.Value < cutoff)
                    _byId.TryRemove(kv.Key, out _);
            }

            var list = _byId.Select(kv => new Tombstone { OrderId = kv.Key, DeletedAt = kv.Value }).ToList();
            var json = JsonSerializer.Serialize(list, JsonOptions);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save tombstones");
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
