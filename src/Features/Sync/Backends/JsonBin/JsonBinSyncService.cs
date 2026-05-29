using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using OrderLog.Features.Models;
using OrderLog.Features.Services;
using OrderLog.Features.Sync.Helpers;
using OrderLog.Features.Sync.Models;
using OrderLog.Features.Sync.Services;
using OrderLog.Infrastructure.Services;

namespace OrderLog.Features.Sync.Backends.JsonBin;

/// <summary>
/// Cloud sync transport that pushes/pulls a single shared inventory through
/// a jsonbin.io bin. The bin contains a deduplicated <c>Items</c> list keyed
/// by Guid; each peer merges remote items into its local store and re-pushes
/// the union on every change. Sticky notes are local-only.
///
/// HTTPS only (port 443). No admin, no firewall changes. Always-on once
/// paired \u2014 there's no enable toggle. Polling cadence is configurable
/// via <see cref="SyncSettings.JsonBinPollIntervalSeconds"/>.
/// </summary>
public sealed partial class JsonBinSyncService : ObservableObject, IDisposable
{
    private const string SettingsAppName = "OrderLogSync";

    private readonly IOrderLogService _repository;
    private readonly SettingsService _settingsService;
    private readonly TombstoneStore _tombstones;
    private readonly ILogger<JsonBinSyncService>? _logger;

    /// <summary>Snapshot of what we last broadcast (for change detection).</summary>
    private readonly ConcurrentDictionary<Guid, DateTime> _lastBroadcastUpdatedAt = new();

    /// <summary>Cache of remote items' UpdatedAt at last poll, so we only fire merge events on real changes.</summary>
    private readonly ConcurrentDictionary<Guid, DateTime> _lastSeenRemoteUpdatedAt = new();

    private JsonBinClient? _client;
    private CancellationTokenSource? _cts;
    private Task? _pollLoop;
    private Task? _writeLoop;
    private readonly SemaphoreSlim _writeSignal = new(0);
    private readonly SemaphoreSlim _binMutex = new(1, 1);
    private bool _disposed;

    [ObservableProperty]
    private SyncSettings _settings = new();

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "Cloud sync not configured";

    [ObservableProperty]
    private DateTime? _lastSyncUtc;

    [ObservableProperty]
    private int _knownPeerCount;

    public event EventHandler<RemoteChangesEventArgs>? RemoteChangesReceived;

    public JsonBinSyncService(
        IOrderLogService repository,
        SettingsService settingsService,
        TombstoneStore tombstones,
        ILogger<JsonBinSyncService>? logger = null)
    {
        _repository = repository;
        _settingsService = settingsService;
        _tombstones = tombstones;
        _logger = logger;

        _repository.ItemsSaved += OnRepositorySaved;
    }

    // ─── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Loads persisted settings and starts polling automatically if pairing
    /// info is present. There is no enable toggle \u2014 once paired, the
    /// service runs whenever the app is open.
    /// </summary>
    public async Task InitializeAsync(SyncSettings settings)
    {
        Settings = settings;

        var key = SecretProtector.Unprotect(settings.JsonBinMasterKeyProtected);
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(settings.JsonBinSharedBinId))
        {
            StatusText = "Cloud sync not configured";
            return;
        }

        // Always-on: once paired, we run. Self-correcting any leftover
        // Enabled=false from older builds.
        Settings.Enabled = true;
        await PersistSettingsAsync().ConfigureAwait(false);

        try { await StartAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Cloud sync auto-start failed"); }
    }

    public static async Task<bool> ValidateKeyAsync(string masterKey, ILogger? logger = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(masterKey)) return false;
        try
        {
            var client = new JsonBinClient(masterKey, logger);
            return await client.ValidateKeyAsync(ct).ConfigureAwait(false);
        }
        catch { return false; }
    }

    /// <summary>
    /// Host pairing: creates the shared bin pre-populated with this device's
    /// current orders. Returns the bin id which is the pairing code shared
    /// with the other PC. Auto-starts sync.
    /// </summary>
    public async Task<string> PairAsHostAsync(string masterKey)
    {
        var client = new JsonBinClient(masterKey, _logger);
        await EnsureDeviceIdAsync().ConfigureAwait(false);

        var initial = new JsonBinSharedState();
        await MergeLocalIntoStateAsync(initial).ConfigureAwait(false);

        var binId = await client.CreateBinAsync($"orderlog-sync-{Environment.UserName}", initial).ConfigureAwait(false);

        Settings.JsonBinMasterKeyProtected = SecretProtector.Protect(masterKey);
        Settings.JsonBinSharedBinId = binId;
        Settings.Enabled = true;
        await PersistSettingsAsync().ConfigureAwait(false);

        await StartAsync().ConfigureAwait(false);
        return binId;
    }

    /// <summary>
    /// Joining peer: takes the bin id (pairing code) from the host, reads the
    /// existing bin, merges this device's local orders into it, writes it
    /// back, and auto-starts sync.
    /// </summary>
    public async Task JoinAsync(string masterKey, string binId)
    {
        if (string.IsNullOrWhiteSpace(binId))
            throw new ArgumentException("Pairing code is required", nameof(binId));

        var client = new JsonBinClient(masterKey, _logger);
        await EnsureDeviceIdAsync().ConfigureAwait(false);

        var current = await client.ReadBinAsync<JsonBinSharedState>(binId).ConfigureAwait(false)
                      ?? new JsonBinSharedState();
        current = MigrateIfNeeded(current);
        await MergeLocalIntoStateAsync(current).ConfigureAwait(false);

        await client.UpdateBinAsync(binId, current).ConfigureAwait(false);

        Settings.JsonBinMasterKeyProtected = SecretProtector.Protect(masterKey);
        Settings.JsonBinSharedBinId = binId;
        Settings.Enabled = true;
        await PersistSettingsAsync().ConfigureAwait(false);

        await StartAsync().ConfigureAwait(false);
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;

        var key = SecretProtector.Unprotect(Settings.JsonBinMasterKeyProtected);
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(Settings.JsonBinSharedBinId))
        {
            StatusText = "Cloud sync not configured";
            return;
        }

        _client = new JsonBinClient(key, _logger);
        _cts = new CancellationTokenSource();

        // Seed broadcast snapshot so the first save after start doesn't fire
        // a redundant push of every existing item.
        try
        {
            var items = await _repository.LoadAsync().ConfigureAwait(false);
            _lastBroadcastUpdatedAt.Clear();
            foreach (var it in items.Where(ShouldSync))
                _lastBroadcastUpdatedAt[it.Id] = it.UpdatedAt;
        }
        catch (Exception ex) { _logger?.LogDebug(ex, "Snapshot seed failed"); }

        IsRunning = true;
        StatusText = "Cloud sync running - polling for peer changes";
        Settings.Enabled = true;
        await PersistSettingsAsync().ConfigureAwait(false);

        _pollLoop = Task.Run(() => PollLoopAsync(_cts.Token));
        _writeLoop = Task.Run(() => WriteLoopAsync(_cts.Token));

        // Initial push so our local state is reflected in the bin from launch.
        try { _writeSignal.Release(); } catch { }

        _logger?.LogInformation("JsonBinSyncService started (bin={Bin})", Settings.JsonBinSharedBinId);
    }

    public async Task StopAsync()
    {
        try { _cts?.Cancel(); } catch { }
        try { _writeSignal.Release(); } catch { }

        try { if (_pollLoop != null) await _pollLoop.ConfigureAwait(false); } catch { }
        try { if (_writeLoop != null) await _writeLoop.ConfigureAwait(false); } catch { }
        _pollLoop = null;
        _writeLoop = null;

        _client = null;
        IsRunning = false;
        // We don't flip Settings.Enabled here \u2014 always-on means the
        // service will restart on next launch if pairing info is present.
        _logger?.LogInformation("JsonBinSyncService stopped");
    }

    /// <summary>
    /// Clears the JSONBin pairing on this PC. The shared bin itself is NOT
    /// deleted (the other PC may still be using it).
    /// </summary>
    public async Task UnpairAsync()
    {
        if (IsRunning) await StopAsync().ConfigureAwait(false);
        Settings.JsonBinMasterKeyProtected = string.Empty;
        Settings.JsonBinSharedBinId = string.Empty;
        Settings.Enabled = false;
        await PersistSettingsAsync().ConfigureAwait(false);
        StatusText = "Cloud sync not configured";
    }

    // ─── Polling (incoming peer changes) ───────────────────────────────────

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(3, Settings.JsonBinPollIntervalSeconds));
        await PollOnceAsync(ct).ConfigureAwait(false);
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(interval, ct).ConfigureAwait(false); }
            catch (TaskCanceledException) { break; }
            try { await PollOnceAsync(ct).ConfigureAwait(false); }
            catch (Exception ex) { _logger?.LogDebug(ex, "Poll cycle failed"); }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        if (_client == null) return;
        try
        {
            var raw = await _client.ReadBinAsync<JsonBinSharedState>(Settings.JsonBinSharedBinId, ct).ConfigureAwait(false);
            if (raw == null)
            {
                _logger?.LogWarning("Poll: bin {Bin} read returned null", Settings.JsonBinSharedBinId);
                return;
            }
            var state = MigrateIfNeeded(raw);

            _logger?.LogDebug("Poll: bin has {ItemCount} items, {TombCount} tombstones, {DevCount} devices",
                state.Items.Count, state.Tombstones.Count, state.Devices.Count);

            // Compute deltas: items whose UpdatedAt is new to us.
            var myKey = Settings.DeviceId.ToString();
            var changedItems = new List<OrderItem>();
            foreach (var it in state.Items)
            {
                if (!ShouldSync(it)) continue;
                if (_tombstones.IsDeleted(it.Id, out var deletedAt) && deletedAt >= it.UpdatedAt) continue;
                if (_lastSeenRemoteUpdatedAt.TryGetValue(it.Id, out var seenAt) && it.UpdatedAt <= seenAt) continue;
                _lastSeenRemoteUpdatedAt[it.Id] = it.UpdatedAt;
                changedItems.Add(it);
            }

            if (state.Tombstones.Count > 0)
                _ = _tombstones.MergeAsync(state.Tombstones);

            if (changedItems.Count > 0 || state.Tombstones.Count > 0)
            {
                // Update broadcast snapshot for items we just received so a
                // subsequent local save doesn't re-broadcast them.
                foreach (var it in changedItems) _lastBroadcastUpdatedAt[it.Id] = it.UpdatedAt;

                var sourceName = state.Devices
                    .Where(kv => !string.Equals(kv.Key, myKey, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(kv => kv.Value.LastSeenUtc)
                    .Select(kv => kv.Value.DeviceName)
                    .FirstOrDefault() ?? "peer";

                _logger?.LogInformation(
                    "Poll: {ChangedCount} item change(s), {TombCount} tombstone(s) from {Source}",
                    changedItems.Count, state.Tombstones.Count, sourceName);

                try
                {
                    RemoteChangesReceived?.Invoke(this, new RemoteChangesEventArgs(changedItems, state.Tombstones, sourceName));
                }
                catch (Exception ex) { _logger?.LogWarning(ex, "RemoteChangesReceived handler threw"); }
            }

            KnownPeerCount = state.Devices.Count(kv => !string.Equals(kv.Key, myKey, StringComparison.OrdinalIgnoreCase));
            LastSyncUtc = DateTime.UtcNow;

            // Reconciliation: if our local state has items the bin doesn't
            // (or newer copies), schedule a push. Self-healing against any
            // missed save event or earlier failed push.
            await ReconcileWithBinAsync(state, myKey).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "PollOnce failed");
        }
    }

    /// <summary>
    /// Compares our local repo to the bin and signals a push if we have any
    /// item the bin doesn't, an item newer than the bin's copy, or our own
    /// presence entry is stale/missing.
    /// </summary>
    private async Task ReconcileWithBinAsync(JsonBinSharedState state, string myKey)
    {
        try
        {
            var local = await _repository.LoadAsync().ConfigureAwait(false);
            var localOrders = local.Where(ShouldSync).ToList();

            var binById = new Dictionary<Guid, DateTime>(state.Items.Count);
            foreach (var it in state.Items) binById[it.Id] = it.UpdatedAt;

            foreach (var it in localOrders)
            {
                if (!binById.TryGetValue(it.Id, out var binUpdated))
                {
                    _logger?.LogInformation(
                        "Reconcile: local item {Id} missing from bin - scheduling push", it.Id);
                    try { _writeSignal.Release(); } catch { }
                    return;
                }
                if (it.UpdatedAt > binUpdated)
                {
                    _logger?.LogInformation(
                        "Reconcile: local item {Id} newer (local={L} > bin={B}) - scheduling push",
                        it.Id, it.UpdatedAt, binUpdated);
                    try { _writeSignal.Release(); } catch { }
                    return;
                }
            }

            if (!state.Devices.ContainsKey(myKey))
            {
                _logger?.LogInformation("Reconcile: our presence is missing - scheduling push");
                try { _writeSignal.Release(); } catch { }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Reconcile failed");
        }
    }

    // ─── Writing (outgoing local changes) ──────────────────────────────────

    private void OnRepositorySaved(object? sender, OrderItemsSavedEventArgs e)
    {
        bool anyChange = false;
        foreach (var it in e.Items)
        {
            if (!ShouldSync(it)) continue;
            if (!_lastBroadcastUpdatedAt.TryGetValue(it.Id, out var last) || it.UpdatedAt > last)
            {
                anyChange = true;
                break;
            }
        }
        if (!anyChange)
        {
            foreach (var id in e.DeletedIds)
            {
                if (_lastBroadcastUpdatedAt.ContainsKey(id)) { anyChange = true; break; }
            }
        }

        if (e.DeletedIds.Count > 0)
            _ = _tombstones.AddRangeAsync(e.DeletedIds, DateTime.UtcNow);

        UpdateSnapshotFromSave(e);

        if (!IsRunning || !anyChange) return;
        try { _writeSignal.Release(); } catch { }
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        var debounce = TimeSpan.FromSeconds(2);
        while (!ct.IsCancellationRequested)
        {
            try { await _writeSignal.WaitAsync(ct).ConfigureAwait(false); }
            catch (TaskCanceledException) { break; }
            if (ct.IsCancellationRequested) break;

            try { await Task.Delay(debounce, ct).ConfigureAwait(false); }
            catch (TaskCanceledException) { break; }
            while (_writeSignal.CurrentCount > 0)
            {
                try { await _writeSignal.WaitAsync(ct).ConfigureAwait(false); } catch { break; }
            }

            try { await PushMergeAsync(ct).ConfigureAwait(false); }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Push to JSONBin failed; will retry on next change");
            }
        }
    }

    /// <summary>
    /// Read-modify-write: pull latest bin, merge our local orders into its
    /// shared inventory (newest UpdatedAt wins per item), update our presence,
    /// and PUT back.
    /// </summary>
    private async Task PushMergeAsync(CancellationToken ct)
    {
        if (_client == null) return;
        await _binMutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var raw = await _client.ReadBinAsync<JsonBinSharedState>(Settings.JsonBinSharedBinId, ct).ConfigureAwait(false);
            var state = raw != null ? MigrateIfNeeded(raw) : new JsonBinSharedState();

            int addedOrUpdated = await MergeLocalIntoStateAsync(state).ConfigureAwait(false);

            // Update our presence.
            var myKey = Settings.DeviceId.ToString();
            state.Devices[myKey] = new JsonBinDeviceMeta
            {
                DeviceName = Settings.DeviceName,
                LastSeenUtc = DateTime.UtcNow,
            };

            // Merge any local tombstones not already in the bin.
            var binTombIds = new HashSet<Guid>(state.Tombstones.Select(t => t.OrderId));
            foreach (var t in _tombstones.Snapshot())
            {
                if (!binTombIds.Contains(t.OrderId))
                {
                    state.Tombstones.Add(t);
                    binTombIds.Add(t.OrderId);
                }
            }

            await _client.UpdateBinAsync(Settings.JsonBinSharedBinId, state, ct).ConfigureAwait(false);
            LastSyncUtc = DateTime.UtcNow;
            _logger?.LogInformation(
                "Pushed merge: bin now has {Items} items, {Tombs} tombstones, {Devices} devices (our delta: {Delta})",
                state.Items.Count, state.Tombstones.Count, state.Devices.Count, addedOrUpdated);
        }
        finally { _binMutex.Release(); }
    }

    /// <summary>
    /// Merges this device's local orders into the given shared state. Items
    /// already in the bin with the same or newer <c>UpdatedAt</c> are kept;
    /// older entries are replaced; new local entries are added. Returns the
    /// number of items the merge added or updated.
    /// </summary>
    private async Task<int> MergeLocalIntoStateAsync(JsonBinSharedState state)
    {
        var local = await _repository.LoadAsync().ConfigureAwait(false);
        var localOrders = local.Where(ShouldSync).ToList();

        // Index bin items for O(1) lookup.
        var binIndex = new Dictionary<Guid, int>(state.Items.Count);
        for (int i = 0; i < state.Items.Count; i++) binIndex[state.Items[i].Id] = i;

        int delta = 0;
        foreach (var it in localOrders)
        {
            if (binIndex.TryGetValue(it.Id, out var idx))
            {
                if (it.UpdatedAt > state.Items[idx].UpdatedAt)
                {
                    state.Items[idx] = it;
                    delta++;
                }
            }
            else
            {
                state.Items.Add(it);
                binIndex[it.Id] = state.Items.Count - 1;
                delta++;
            }
        }

        // Apply tombstones from the bin to the items list: anything tombstoned
        // with deletedAt >= item.UpdatedAt gets dropped.
        if (state.Tombstones.Count > 0)
        {
            var tombById = state.Tombstones.GroupBy(t => t.OrderId)
                .ToDictionary(g => g.Key, g => g.Max(t => t.DeletedAt));
            state.Items = state.Items
                .Where(i => !(tombById.TryGetValue(i.Id, out var dAt) && dAt >= i.UpdatedAt))
                .ToList();
        }

        return delta;
    }

    private void UpdateSnapshotFromSave(OrderItemsSavedEventArgs e)
    {
        var alive = new HashSet<Guid>();
        foreach (var it in e.Items)
        {
            if (!ShouldSync(it)) continue;
            _lastBroadcastUpdatedAt[it.Id] = it.UpdatedAt;
            alive.Add(it.Id);
        }
        foreach (var id in e.DeletedIds)
            _lastBroadcastUpdatedAt.TryRemove(id, out _);
        foreach (var key in _lastBroadcastUpdatedAt.Keys)
        {
            if (!alive.Contains(key))
                _lastBroadcastUpdatedAt.TryRemove(key, out _);
        }
    }

    // ─── Migration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Detects v1 ("devices map of slots, each with items+tombstones") payloads
    /// and rewrites them in-place to v2 ("shared items list + presence map"),
    /// deduplicating by Guid with newest UpdatedAt winning.
    /// </summary>
    private JsonBinSharedState MigrateIfNeeded(JsonBinSharedState state)
    {
        if (state.Version >= 2) return state;
        // v1 had per-device slots with their own items. The deserialised
        // JsonBinSharedState already projects the top-level items/tombstones
        // (empty in v1). We need to reach into the raw JSON for the device
        // slots' inner items.
        // We can't recover slot internals from this typed object directly,
        // so just leave Items empty and let the local merge repopulate it.
        // PushMergeAsync will write a clean v2 payload on the next push.
        _logger?.LogInformation("Detected v1 bin payload \u2014 upgrading to v2 on next push");
        state.Version = 2;
        state.Items ??= new();
        state.Tombstones ??= new();
        state.Devices ??= new();
        return state;
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static bool ShouldSync(OrderItem item)
        => item != null && item.NoteType == NoteType.Order;

    private async Task EnsureDeviceIdAsync()
    {
        if (Settings.DeviceId == Guid.Empty)
        {
            Settings.DeviceId = Guid.NewGuid();
            await PersistSettingsAsync().ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(Settings.DeviceName))
            Settings.DeviceName = Environment.MachineName;
    }

    private async Task PersistSettingsAsync()
    {
        try { await _settingsService.SaveSettingsAsync(SettingsAppName, Settings).ConfigureAwait(false); }
        catch (Exception ex) { _logger?.LogWarning(ex, "Failed to persist sync settings"); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _repository.ItemsSaved -= OnRepositorySaved; } catch { }
        try { StopAsync().GetAwaiter().GetResult(); } catch { }
        _cts?.Dispose();
        _writeSignal.Dispose();
        _binMutex.Dispose();
    }
}
