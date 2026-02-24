using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace SOUP.Features.OrderLog.Services;

/// <summary>
/// Persists group expand/collapse states for the orders UI.
/// Uses debouncing to avoid excessive file writes.
/// </summary>
public sealed class GroupStateStore : IDisposable
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger<GroupStateStore>? _logger;
    private Dictionary<string, bool> _states = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _saveTimer;
    private readonly object _lock = new();
    private bool _disposed;

    public GroupStateStore(ILogger<GroupStateStore>? logger = null)
    {
        _logger = logger;

        Directory.CreateDirectory(Core.AppPaths.OrderLogDir);
        _path = Path.Combine(Core.AppPaths.OrderLogDir, "groups.json");

        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            _states = JsonSerializer.Deserialize<Dictionary<string, bool>>(json)
                      ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load group states from {Path}", _path);
            _states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        try
        {
            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(_states, s_jsonOptions);
            }
            File.WriteAllText(_path, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save group states to {Path}", _path);
        }
    }

    public bool Get(string? name, bool defaultValue = true)
    {
        if (string.IsNullOrEmpty(name)) return defaultValue;
        lock (_lock)
        {
            return _states.TryGetValue(name, out var value) ? value : defaultValue;
        }
    }

    public void Set(string? name, bool value)
    {
        if (string.IsNullOrEmpty(name)) return;

        lock (_lock)
        {
            _states[name] = value;

            // Debounce: save after 500ms of no changes
            // Reuse timer instead of creating new one each time
            if (_saveTimer == null)
            {
                _saveTimer = new Timer(_ => Save(), null, 500, Timeout.Infinite);
            }
            else
            {
                _saveTimer.Change(500, Timeout.Infinite);
            }
        }
    }

    public void ResetAll()
    {
        lock (_lock)
        {
            _states.Clear();
            _saveTimer?.Dispose();
            _saveTimer = null;

            try
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to delete group states file");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_lock)
        {
            // Flush any pending saves
            _saveTimer?.Dispose();
            Save();
            _disposed = true;
        }
    }
}
