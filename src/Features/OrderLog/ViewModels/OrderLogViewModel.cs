using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OrderLog.Features.Constants;
using OrderLog.Features.Models;
using OrderLog.Helpers;
using OrderLog.Infrastructure.Services;
using OrderLog.Services;
using OrderLog.Features.Services;
using OrderLog.Features.Sync.Models;
using Microsoft.Win32;

namespace OrderLog.Features.ViewModels;

public partial class OrderLogViewModel : ObservableObject, IDisposable
{
    // Constants for configurable timeouts
    private const int TimerIntervalSeconds = 1;
    private const int DefaultUndoTimeoutSeconds = 5;
    private const int StatusClearSeconds = 3;
    private const double DefaultCardFontSize = 13.0;
    private const double DefaultWidgetWidth = 380.0;
    public const double MinWidgetWidth = 320.0;
    public const double MaxWidgetWidth = 520.0;

    private readonly IOrderLogService _orderLogService;
    private readonly GroupStateStore _groupStateStore;
    private readonly SettingsService _settingsService;
    private readonly DialogService _dialogService;
    private readonly ILogger<OrderLogViewModel>? _logger;
    private readonly OrderSearchService _searchService;
    private readonly OrderLogClipboardService _clipboardService;
    private readonly UndoRedoStack _undoRedoStack;
    private readonly DispatcherTimer _timer;
    private bool _disposed;
    private DispatcherTimer? _undoTimer;
    private DispatcherTimer? _undoCountdownTimer;
    private DispatcherTimer? _statusClearTimer;
    private System.Threading.CancellationTokenSource? _saveDebounceCts;
    private System.Threading.CancellationTokenSource? _settingsSaveCts;

    // Lock for thread-safe access to HashSets
    private readonly object _collectionLock = new();

    // HashSets for O(1) membership checks instead of O(n) Contains on ObservableCollection
    private readonly HashSet<Guid> _itemIds = new();
    private readonly HashSet<Guid> _archivedItemIds = new();

    public ObservableCollection<OrderItem> Items { get; } = new();
    public ObservableCollection<OrderItem> ArchivedItems { get; } = new();
    public ObservableCollection<OrderItem> SelectedItems { get; } = new();

    [ObservableProperty]
    private int _selectedItemsCount;
    public ObservableCollection<OrderItem> StickyNotes { get; } = new();
    public ObservableCollection<OrderItemGroup> DisplayItems { get; } = new();
    public ObservableCollection<OrderItemGroup> DisplayArchivedItems { get; } = new();
    private Task? _archivedRefreshTask;
    

    // Grouping helper service (extracted to simplify VM)
    private readonly OrderGroupingService _groupingService;

    public OrderGroupingService.OrderLogSortMode SortModeEnum { get; private set; } = OrderGroupingService.OrderLogSortMode.Status;
    
    public OrderGroupingService.OrderLogSortMode ArchivedSortModeEnum { get; private set; } = OrderGroupingService.OrderLogSortMode.CreatedAt;

    [ObservableProperty]
    private int _displayItemsCount;

    [ObservableProperty]
    private int _displayArchivedItemsCount;

    [ObservableProperty]
    private int _displayMembersCount;

    /// <summary>Helper to get all items (active + archived) without repeated Concat calls</summary>
    private IEnumerable<OrderItem> AllItems => Items.Concat(ArchivedItems);

    [ObservableProperty]
    private bool _showArchived = false;

    // UI settings persisted for widget
    [ObservableProperty]
    private double _cardFontSize = DefaultCardFontSize;

    [ObservableProperty]
    private double _widgetWidth = DefaultWidgetWidth;

    [ObservableProperty]
    private bool _showNowPlaying = true;

    [ObservableProperty]
    private int _undoTimeoutSeconds = DefaultUndoTimeoutSeconds;

    [ObservableProperty]
    private string _defaultOrderColor = OrderLogColors.DefaultOrder;

    [ObservableProperty]
    private string _defaultNoteColor = OrderLogColors.DefaultNote;

    [ObservableProperty]
    private bool _sortStatusDescending = false;

    [ObservableProperty]
    private bool _notReadyGroupExpanded = true;

    [ObservableProperty]
    private bool _onDeckGroupExpanded = true;

    [ObservableProperty]
    private bool _inProgressGroupExpanded = true;

    [ObservableProperty]
    private bool _notesExpanded = true;

    // Status-grouped collections used by the view
    public ObservableCollection<OrderItemGroup> NotReadyItems { get; } = new();
    public ObservableCollection<OrderItemGroup> OnDeckItems { get; } = new();
    public ObservableCollection<OrderItemGroup> InProgressItems { get; } = new();

    [ObservableProperty]
    private int _notReadyCount;

    [ObservableProperty]
    private int _onDeckCount;

    [ObservableProperty]
    private int _inProgressCount;

    /// <summary>
    /// Gets or sets whether the widget is in notes-only mode (hides order functionality).
    /// </summary>
    [ObservableProperty]
    private bool _notesOnlyMode = false;

    /// <summary>
    /// Basic system option: run app at Windows startup.
    /// </summary>
    [ObservableProperty]
    private bool _runAtStartup = false;

    // Search & Filter Properties
    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearchActive = false;

    [ObservableProperty]
    private OrderItem.OrderStatus[]? _statusFilters = null;

    [ObservableProperty]
    private DateTime? _filterStartDate = null;

    [ObservableProperty]
    private DateTime? _filterEndDate = null;

    [ObservableProperty]
    private string[]? _colorFilters = null;

    [ObservableProperty]
    private NoteType? _noteTypeFilter = null;

    [ObservableProperty]
    private NoteCategory? _noteCategoryFilter = null;

    // Multi-select mode for bulk operations
    [ObservableProperty]
    private bool _isMultiSelectMode = false;


    // ─── Theme proxy properties ─────────────────────────────────────────────
    // Delegate to ThemeService so the settings view can bind directly to the VM.

    /// <summary>Gets or sets whether dark mode is active (live proxy to ThemeService).</summary>
    public bool IsDarkMode
    {
        get => OrderLog.Services.ThemeService.Instance.IsDarkMode;
        set
        {
            if (OrderLog.Services.ThemeService.Instance.IsDarkMode != value)
            {
                OrderLog.Services.ThemeService.Instance.SetTheme(value);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets or sets the active shape profile (live proxy to ThemeService).</summary>
    public OrderLog.Services.ShapeVariant ShapeVariant
    {
        get => OrderLog.Services.ThemeService.Instance.ShapeVariant;
        set
        {
            if (OrderLog.Services.ThemeService.Instance.ShapeVariant != value)
            {
                OrderLog.Services.ThemeService.Instance.SetShapeVariant(value);
                OnPropertyChanged();
            }
        }
    }

    /// <summary>Gets or sets the active colour palette (live proxy to ThemeService).</summary>
    public OrderLog.Services.ColourTheme ColourTheme
    {
        get => OrderLog.Services.ThemeService.Instance.ColourTheme;
        set
        {
            if (OrderLog.Services.ThemeService.Instance.ColourTheme != value)
            {
                OrderLog.Services.ThemeService.Instance.SetColourTheme(value);
                OnPropertyChanged();
            }
        }
    }

    // Navigation properties for enhanced navigation
    [ObservableProperty]
    private OrderItem? _currentNavigationItem = null;

    [ObservableProperty]
    private int _currentItemIndex = -1;

    [ObservableProperty]
    private double _savedScrollPosition = 0;

    partial void OnSearchQueryChanged(string value)
    {
        IsSearchActive = !string.IsNullOrWhiteSpace(value);
        RefreshDisplayItems();
    }

    partial void OnStatusFiltersChanged(OrderItem.OrderStatus[]? value)
    {
        RefreshDisplayItems();
    }

    partial void OnFilterStartDateChanged(DateTime? value)
    {
        RefreshDisplayItems();
    }

    partial void OnFilterEndDateChanged(DateTime? value)
    {
        RefreshDisplayItems();
    }

    partial void OnRunAtStartupChanged(bool value)
    {
        SaveWidgetSettings();
        SetRunAtStartup(value);
    }

    private void SetRunAtStartup(bool enable)
    {
        try
        {
            const string keyName = "SSCommandCentre";
            const string legacyKeyName = "OrderLog";
            var exe = System.Reflection.Assembly.GetEntryAssembly()?.Location ?? string.Empty;
            if (string.IsNullOrEmpty(exe)) return;

            using var rk = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true)
                       ?? Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");

            // Migrate: remove legacy key if present
            rk.DeleteValue(legacyKeyName, false);

            if (enable)
            {
                rk.SetValue(keyName, $"\"{exe}\"");
            }
            else
            {
                rk.DeleteValue(keyName, false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to set run-at-startup registry key");
        }
    }

    partial void OnColorFiltersChanged(string[]? value)
    {
        RefreshDisplayItems();
    }

    partial void OnNoteTypeFilterChanged(NoteType? value)
    {
        RefreshDisplayItems();
    }

    partial void OnNoteCategoryFilterChanged(NoteCategory? value)
    {
        RefreshDisplayItems();
    }


    [ObservableProperty]
    private OrderItem? _selectedItem;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    // Link mode state
    [ObservableProperty]
    private bool _isLinkMode;

    [ObservableProperty]
    private OrderItem? _linkModeSource;

    partial void OnIsLinkModeChanged(bool value)
    {
        if (value && LinkModeSource != null)
        {
            StatusMessage = $"🔗 Click another card to link with {LinkModeSource.VendorName ?? "this item"}...";
        }
        else
        {
            UpdateDefaultStatus();
        }
    }

    /// <summary>Enter link mode with the specified item as the source.</summary>
    public void StartLinkMode(OrderItem source)
    {
        LinkModeSource = source;
        IsLinkMode = true;
    }

    /// <summary>Cancel link mode without linking.</summary>
    public void CancelLinkMode()
    {
        LinkModeSource = null;
        IsLinkMode = false;
    }

    /// <summary>Complete link mode by linking the source to the target.</summary>
    public async Task CompleteLinkModeAsync(OrderItem target)
    {
        if (!IsLinkMode || LinkModeSource == null || target == LinkModeSource)
        {
            CancelLinkMode();
            return;
        }

        var source = LinkModeSource;
        CancelLinkMode();

        await LinkItemsAsync(new List<OrderItem> { source }, target);
        StatusMessage = $"Linked {source.VendorName ?? "item"} with {target.VendorName ?? "item"}";
    }

    partial void OnStatusMessageChanged(string value)
    {
        // Auto-clear status message after timeout (unless it's empty or the default count message)
        if (!string.IsNullOrEmpty(value) && !IsDefaultStatusMessage(value))
        {
            _statusClearTimer ??= new() { Interval = TimeSpan.FromSeconds(StatusClearSeconds) };
            _statusClearTimer.Stop();
            _statusClearTimer.Tick -= OnStatusClearTimerTick;
            _statusClearTimer.Tick += OnStatusClearTimerTick;
            _statusClearTimer.Start();
        }
    }

    private bool IsDefaultStatusMessage(string message)
    {
        // Default status shows counts like "5 active . 3 archived"
        // Archived/unarchived notifications should not be treated as default
        return message.Contains(" active") && 
               message.Contains(" archived") && 
               !message.StartsWith("Archived") && 
               !message.StartsWith("Unarchived");
    }

    private void UpdateDefaultStatus()
    {
        StatusMessage = $"{Items.Count} active . {ArchivedItems.Count} archived";
    }

    private void OnStatusClearTimerTick(object? sender, EventArgs e)
    {
        _statusClearTimer?.Stop();
        UpdateDefaultStatus();
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _undoAvailable;

    [ObservableProperty]
    private string _undoMessage = string.Empty;

    [ObservableProperty]
    private int _undoSecondsRemaining;

    [ObservableProperty]
    private bool _redoAvailable;

    [ObservableProperty]
    private string _redoMessage = string.Empty;

    [ObservableProperty]
    private int _undoStackCount;

    [ObservableProperty]
    private int _redoStackCount;

    /// <summary>
    /// Gets the undo history for display in UI
    /// </summary>
    public IEnumerable<UndoableAction> UndoHistory => _undoRedoStack.UndoHistory;

    /// <summary>
    /// Gets the redo history for display in UI
    /// </summary>
    public IEnumerable<UndoableAction> RedoHistory => _undoRedoStack.RedoHistory;

    [ObservableProperty]
    private string _newNoteVendorName = string.Empty;

    [ObservableProperty]
    private string _newNoteTransferNumbers = string.Empty;

    [ObservableProperty]
    private string _newNoteWhsShipmentNumbers = string.Empty;

    [ObservableProperty]
    private string _stickyNoteContent = string.Empty;

    private string _newNoteColorHex = OrderLogColors.DefaultOrder;
    private string _stickyNoteColorHex = OrderLogColors.DefaultNote; // Yellow default for sticky notes

    public event Action<OrderItem>? ItemAdded;

    public OrderLogViewModel(
        IOrderLogService orderLogService,
        GroupStateStore groupStateStore,
        SettingsService settingsService,
        DialogService dialogService,
        ILogger<OrderLogViewModel>? logger = null)
    {
        _orderLogService = orderLogService;
        _groupStateStore = groupStateStore;
        _settingsService = settingsService;
        _dialogService = dialogService;
        _logger = logger;
        _searchService = new OrderSearchService();
        _clipboardService = new OrderLogClipboardService(null);
        _undoRedoStack = new UndoRedoStack(maxHistorySize: 50);

        _timer = new() { Interval = TimeSpan.FromSeconds(TimerIntervalSeconds) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        // Wire up undo/redo stack changes to update UI
        _undoRedoStack.StackChanged += OnUndoRedoStackChanged;

        // grouping helper service (extracted to simplify VM)
        _groupingService = new OrderGroupingService();

        // Ensure counts update when display collections change
        DisplayItems.CollectionChanged += (s, e) => UpdateDisplayCounts();
        DisplayArchivedItems.CollectionChanged += (s, e) => UpdateDisplayCounts();

        // Ensure selection count updates when selection changes
        SelectedItems.CollectionChanged += (s, e) => SelectedItemsCount = SelectedItems.Count;

        UpdateDisplayCounts();

        _logger?.LogInformation("OrderLogViewModel initialized");
    }

    // Settings save handlers for properties modified from the settings view
    partial void OnCardFontSizeChanged(double value)
    {
        // Update the DynamicResource so the widget reflects the change immediately
        if (Application.Current != null)
        {
            Application.Current.Resources["CardFontSize"] = value;

            foreach (Window window in Application.Current.Windows)
            {
                try
                {
                    window.Resources["CardFontSize"] = value;
                }
                catch
                {
                    // Window might not define the resource locally; ignore
                }
            }
        }
        SaveWidgetSettings();
    }
    partial void OnWidgetWidthChanged(double value) => SaveWidgetSettings();
    partial void OnShowNowPlayingChanged(bool value) => SaveWidgetSettings();
    partial void OnShowArchivedChanged(bool value) => SaveWidgetSettings();
    partial void OnDefaultOrderColorChanged(string value) => SaveWidgetSettings();
    partial void OnDefaultNoteColorChanged(string value) => SaveWidgetSettings();
    partial void OnNotesOnlyModeChanged(bool value)
    {
        if (value)
        {
            NotesExpanded = true;
        }
        SaveWidgetSettings();
    }

    partial void OnNotesExpandedChanged(bool value)
    {
        SaveWidgetSettings();
    }

    partial void OnNotReadyGroupExpandedChanged(bool value)
    {
        SaveWidgetSettings();
    }

    partial void OnOnDeckGroupExpandedChanged(bool value)
    {
        SaveWidgetSettings();
    }

    partial void OnInProgressGroupExpandedChanged(bool value)
    {
        SaveWidgetSettings();
    }

    partial void OnUndoTimeoutSecondsChanged(int value)
    {
        // If undo timer exists, update its interval to reflect new setting
        if (_undoTimer != null)
        {
            try
            {
                _undoTimer.Interval = TimeSpan.FromSeconds(value);
            }
            catch { }
        }

        SaveWidgetSettings();
    }

    private void SaveWidgetSettings()
    {
        _settingsSaveCts?.Cancel();
        _settingsSaveCts?.Dispose();
        _settingsSaveCts = new System.Threading.CancellationTokenSource();
        var token = _settingsSaveCts.Token;
        var settings = new OrderLogWidgetSettings
        {
            CardFontSize = CardFontSize,
            WidgetWidth = WidgetWidth,
            ShowNowPlaying = ShowNowPlaying,
            ShowArchived = ShowArchived,
            UndoTimeoutSeconds = UndoTimeoutSeconds,
            DefaultOrderColor = DefaultOrderColor,
            DefaultNoteColor = DefaultNoteColor,
            NotesOnlyMode = NotesOnlyMode,
            SortStatusDescending = SortStatusDescending,
            NotReadyGroupExpanded = NotReadyGroupExpanded,
            OnDeckGroupExpanded = OnDeckGroupExpanded,
            InProgressGroupExpanded = InProgressGroupExpanded,
            NotesExpanded = NotesExpanded,
            RunAtStartup = RunAtStartup
        };
        SaveWidgetSettingsDebouncedAsync(settings, token).SafeFireAndForget("SaveWidgetSettings");
    }

    private async Task SaveWidgetSettingsDebouncedAsync(OrderLogWidgetSettings settings, System.Threading.CancellationToken token)
    {
        try
        {
            await Task.Delay(300, token);
            await _settingsService.SaveSettingsAsync("OrderLogWidget", settings);
        }
        catch (OperationCanceledException)
        {
            // Expected during debounce when newer settings changes arrive.
        }
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        foreach (var item in Items)
        {
            item.RefreshTimeInProgress();
            item.RefreshTimeOnDeck();
        }
    }

    private void OnUndoRedoStackChanged()
    {
        UndoAvailable = _undoRedoStack.CanUndo;
        RedoAvailable = _undoRedoStack.CanRedo;
        UndoStackCount = _undoRedoStack.UndoCount;
        RedoStackCount = _undoRedoStack.RedoCount;

        // Notify property changes for history collections
        OnPropertyChanged(nameof(UndoHistory));
        OnPropertyChanged(nameof(RedoHistory));

        // Update messages
        if (_undoRedoStack.CanUndo)
        {
            var lastAction = _undoRedoStack.UndoHistory.FirstOrDefault();
            UndoMessage = lastAction != null ? $"Undo: {lastAction.Description}" : "Undo available";
        }
        else
        {
            UndoMessage = string.Empty;
        }

        if (_undoRedoStack.CanRedo)
        {
            var lastAction = _undoRedoStack.RedoHistory.FirstOrDefault();
            RedoMessage = lastAction != null ? $"Redo: {lastAction.Description}" : "Redo available";
        }
        else
        {
            RedoMessage = string.Empty;
        }
    }

    public async Task InitializeAsync()
    {
        _logger?.LogInformation("OrderLogViewModel.InitializeAsync: start");
        try
        {
            IsLoading = true;
            StatusMessage = "Loading...";
        }
        catch { }
        // load persisted widget settings
        try
        {
            StatusMessage = "Loading settings...";
            var swSettings = System.Diagnostics.Stopwatch.StartNew();
            _logger?.LogInformation("Loading widget settings...");
            var settingsTask = _settingsService.LoadSettingsAsync<OrderLogWidgetSettings>("OrderLogWidget");
            if (await Task.WhenAny(settingsTask, Task.Delay(5000)) != settingsTask)
            {
                _logger?.LogWarning("Loading widget settings is taking >5s");
            }
            var s = await settingsTask;
            swSettings.Stop();
            _logger?.LogInformation("Loaded widget settings in {Ms}ms", swSettings.ElapsedMilliseconds);
            CardFontSize = s.CardFontSize <= 0 ? DefaultCardFontSize : s.CardFontSize;
            WidgetWidth = s.WidgetWidth <= 0
                ? DefaultWidgetWidth
                : Math.Clamp(s.WidgetWidth, MinWidgetWidth, MaxWidgetWidth);
            // Note: ShowNowPlaying defaults to true in the model, so we can use it directly
            ShowNowPlaying = s.ShowNowPlaying;
            ShowArchived = s.ShowArchived;
            UndoTimeoutSeconds = s.UndoTimeoutSeconds <= 0 ? DefaultUndoTimeoutSeconds : s.UndoTimeoutSeconds;
            DefaultOrderColor = string.IsNullOrEmpty(s.DefaultOrderColor) ? OrderLogColors.DefaultOrder : s.DefaultOrderColor;
            DefaultNoteColor = string.IsNullOrEmpty(s.DefaultNoteColor) ? OrderLogColors.DefaultNote : s.DefaultNoteColor;
            NotesOnlyMode = s.NotesOnlyMode;
            // Sorting preferences
            SortStatusDescending = s.SortStatusDescending;
            // Status group expand/collapse state
            NotReadyGroupExpanded = s.NotReadyGroupExpanded;
            OnDeckGroupExpanded = s.OnDeckGroupExpanded;
            InProgressGroupExpanded = s.InProgressGroupExpanded;
            NotesExpanded = s.NotesExpanded;
            // System settings
            RunAtStartup = s.RunAtStartup;
            SetRunAtStartup(RunAtStartup);
            // Apply font size to resources
            if (Application.Current != null)
            {
                Application.Current.Resources["CardFontSize"] = CardFontSize;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load widget settings, using defaults");
        }

        // Templates removed: skip loading templates
        try
        {
            StatusMessage = "Loading items...";
            var swLoad = System.Diagnostics.Stopwatch.StartNew();
            _logger?.LogInformation("Loading items from data store...");
            var loadTask = LoadAsync();
            var timeoutTask = Task.Delay(10000);
            if (await Task.WhenAny(loadTask, timeoutTask) != loadTask)
            {
                _logger?.LogWarning("Loading items is taking >10s; continuing startup and letting load finish in background");
                // Attach continuation to log when background load completes
                _ = loadTask.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        _logger?.LogWarning(t.Exception, "Background LoadAsync failed");
                    }
                    else
                    {
                        _logger?.LogInformation("Background LoadAsync completed");
                    }
                }, TaskScheduler.Default);
            }
            else
            {
                await loadTask;
                swLoad.Stop();
                _logger?.LogInformation("Loaded items in {Ms}ms", swLoad.ElapsedMilliseconds);
            }

            StatusMessage = "Finalizing...";
            _logger?.LogInformation("OrderLogViewModel.InitializeAsync: completed");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Error during InitializeAsync post-settings work");
            StatusMessage = "Initialization error";
        }
        try
        {
            IsLoading = false;
            UpdateDefaultStatus();
        }
        catch { }
    }

    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            _logger?.LogInformation("OrderLogViewModel.LoadAsync: calling _orderLogService.LoadAsync");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var loadTask = _orderLogService.LoadAsync();
            if (await Task.WhenAny(loadTask, Task.Delay(5000)) != loadTask)
            {
                _logger?.LogWarning("_orderLogService.LoadAsync is taking >5s");
            }
            var all = await loadTask;
            sw.Stop();
            _logger?.LogInformation("OrderLogViewModel.LoadAsync: _orderLogService.LoadAsync completed in {Ms}ms", sw.ElapsedMilliseconds);

            Items.Clear();
            ArchivedItems.Clear();
            _itemIds.Clear();
            _archivedItemIds.Clear();

            int autoRepaired = 0;
            foreach (var it in all.OrderBy(i => i.Order))
            {
                // Auto-repair: any item logically completed (Status==Done) but
                // not flagged archived is a victim of an older bug where the
                // IsArchived flag wasn't set. Treat it as archived and re-
                // persist the corrected flag on the next save.
                if (!it.IsArchived && it.Status == OrderItem.OrderStatus.Done)
                {
                    it.SuppressUpdatedAtBump = true;
                    try { it.IsArchived = true; }
                    finally { it.SuppressUpdatedAtBump = false; }
                    autoRepaired++;
                }

                if (it.IsArchived)
                {
                    ArchivedItems.Add(it);
                    _archivedItemIds.Add(it.Id);
                }
                else
                {
                    Items.Add(it);
                    _itemIds.Add(it.Id);
                }
            }

            _logger?.LogInformation(
                "OrderLogViewModel.LoadAsync: Items={ItemsCount}, Archived={ArchivedCount}, AutoRepaired={AutoRepaired}",
                Items.Count, ArchivedItems.Count, autoRepaired);

            RefreshDisplayItems();
            RefreshArchivedDisplayItems();
            UpdateDefaultStatus();

            // Persist any auto-repaired flags so they stick across launches.
            if (autoRepaired > 0)
            {
                try { await SaveAsync(); }
                catch (Exception ex) { _logger?.LogWarning(ex, "Failed to persist auto-repair after load"); }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load order log items");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task DebouncedSaveAsync(int debounceMs = 300)
    {
        try
        {
            _saveDebounceCts?.Cancel();
            _saveDebounceCts?.Dispose();
            _saveDebounceCts = new System.Threading.CancellationTokenSource();
            var token = _saveDebounceCts.Token;
            await Task.Delay(debounceMs, token);
            if (token.IsCancellationRequested) return;
            await SaveAsync();
        }
        catch (TaskCanceledException) { } // Expected when debounce is re-triggered
    }

    public async Task SaveAsync()
    {
        try
        {
            var all = Items.Concat(ArchivedItems).ToList();
            await _orderLogService.SaveAsync(all);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save order log items");
        }
    }

    /// <summary>
    /// Merges remote sync changes into the local collections. Items with a
    /// newer <see cref="OrderItem.UpdatedAt"/> than the local copy replace it;
    /// tombstones remove items irrespective of local edits. Persists once after
    /// merging. Safe to call from any thread.
    /// </summary>
    public async Task ApplyRemoteChangesAsync(
        IReadOnlyList<OrderItem> remoteItems,
        IReadOnlyList<Tombstone> tombstones,
        string sourceDeviceName)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(() => ApplyRemoteChangesAsync(remoteItems, tombstones, sourceDeviceName));
            return;
        }

        bool anyChange = false;

        // Index existing items for fast lookup.
        var byId = new Dictionary<Guid, OrderItem>();
        foreach (var it in Items) byId[it.Id] = it;
        foreach (var it in ArchivedItems) byId[it.Id] = it;

        // Apply tombstones (deletes).
        foreach (var tomb in tombstones)
        {
            if (byId.TryGetValue(tomb.OrderId, out var existing))
            {
                if (existing.UpdatedAt > tomb.DeletedAt)
                {
                    // Local edit is newer than the delete; keep the item.
                    continue;
                }
                RemoveFromItems(existing);
                RemoveFromArchived(existing);
                byId.Remove(tomb.OrderId);
                anyChange = true;
            }
        }

        // Apply item upserts.
        foreach (var remote in remoteItems)
        {
            if (remote == null || remote.Id == Guid.Empty) continue;

            // Respect tombstones: skip resurrecting an item we know is deleted.
            // (Caller has already merged tombstones into the store.)
            if (byId.TryGetValue(remote.Id, out var local))
            {
                if (remote.UpdatedAt <= local.UpdatedAt)
                    continue; // local is at-least-as-fresh; ignore

                // Replace the local item in-place so existing UI bindings see
                // the new content via the CollectionChanged Replace event.
                ReplaceItem(local, remote);
                anyChange = true;
            }
            else
            {
                // New item from peer.
                remote.SuppressUpdatedAtBump = true;
                try
                {
                    if (remote.IsArchived)
                        AddToArchived(remote);
                    else
                        AddToItems(remote, insertAtTop: false);
                }
                finally { remote.SuppressUpdatedAtBump = false; }
                byId[remote.Id] = remote;
                anyChange = true;
            }
        }

        if (!anyChange) return;

        RefreshDisplayItems();
        RefreshArchivedDisplayItems();
        try { await SaveAsync(); } catch (Exception ex) { _logger?.LogWarning(ex, "Save after remote merge failed"); }
        StatusMessage = $"Synced from {sourceDeviceName} - {Items.Count} active . {ArchivedItems.Count} archived";
        _logger?.LogInformation(
            "ApplyRemoteChangesAsync done: source={Source}, remoteCount={Remote}, tombstones={Tombs}, Items={Items}, Archived={Archived}",
            sourceDeviceName, remoteItems.Count, tombstones.Count, Items.Count, ArchivedItems.Count);
    }

    /// <summary>Replace an existing item in its collection with a remote copy.</summary>
    private void ReplaceItem(OrderItem local, OrderItem remote)
    {
        remote.SuppressUpdatedAtBump = true;
        try
        {
            // Active collection
            var idx = Items.IndexOf(local);
            if (idx >= 0)
            {
                if (remote.IsArchived)
                {
                    // Item moved from active to archived on the peer.
                    RemoveFromItems(local);
                    AddToArchived(remote);
                }
                else
                {
                    Items[idx] = remote;
                    // _itemIds already has local.Id which equals remote.Id; nothing to do.
                }
                return;
            }

            var aidx = ArchivedItems.IndexOf(local);
            if (aidx >= 0)
            {
                if (!remote.IsArchived)
                {
                    RemoveFromArchived(local);
                    AddToItems(remote, insertAtTop: false);
                }
                else
                {
                    ArchivedItems[aidx] = remote;
                }
            }
        }
        finally { remote.SuppressUpdatedAtBump = false; }
    }

    /// <summary>
    /// Repairs data inconsistencies caused by the archiving bug where
    /// <c>IsArchived</c> wasn't being set. Moves to the archive any item in
    /// the active list that is logically completed:
    ///   - <c>Status == Done</c> (the canonical "this is finished" signal), OR
    ///   - <c>PreviousStatus != null</c> (signals it was already archived once
    ///     but ended up back in the active list via a buggy code path).
    /// </summary>
    [RelayCommand]
    public async Task RepairArchivedItemsAsync()
    {
        var itemsToRepair = Items.Where(i =>
                i.IsArchived == false
                && (i.Status == OrderItem.OrderStatus.Done || i.PreviousStatus != null))
            .ToList();

        var totalActive = Items.Count;
        var totalArchived = ArchivedItems.Count;
        _logger?.LogInformation(
            "Repair diagnostic: Active={Active}, Archived={Archived}, ToRepair={ToRepair}",
            totalActive, totalArchived, itemsToRepair.Count);

        if (itemsToRepair.Count == 0)
        {
            StatusMessage = $"No items to repair (Active: {totalActive}, Archived: {totalArchived})";
            return;
        }

        foreach (var item in itemsToRepair)
        {
            // Make sure the item carries IsArchived=true once moved so it doesn't
            // get re-loaded into the active list next launch.
            item.SuppressUpdatedAtBump = true;
            try
            {
                RemoveFromItems(item);
                AddToArchived(item);
            }
            finally { item.SuppressUpdatedAtBump = false; }
        }

        RefreshDisplayItems();
        RefreshArchivedDisplayItems();
        await SaveAsync();
        StatusMessage = $"Repaired {itemsToRepair.Count} items - moved to archive (Active: {Items.Count}, Archived: {ArchivedItems.Count})";
        _logger?.LogInformation("Repaired {Count} items with IsArchived inconsistency", itemsToRepair.Count);
    }

    [RelayCommand]
    public async Task ArchiveOrderAsync(OrderItem? item)
    {
        if (item == null) return;
        if (!_itemIds.Contains(item.Id)) return;

        // Get all linked items to archive together
        List<OrderItem> itemsToArchive;
        if (item.LinkedGroupId != null)
        {
            var gid = item.LinkedGroupId.Value;
            // Include any member from all items (active or already archived) to ensure group consistency
            itemsToArchive = AllItems.Where(i => i.LinkedGroupId == gid).ToList();
        }
        else
        {
            itemsToArchive = new List<OrderItem> { item };
        }

        // Record archive action for undo and set PreviousStatus/IsArchived
        var archiveAction = new ArchiveAction(itemsToArchive);
        _undoRedoStack.ExecuteAction(archiveAction);

        // Ensure items are marked Done so timestamps (CompletedAt) are set
        foreach (var it in itemsToArchive)
        {
            it.Status = OrderItem.OrderStatus.Done;
        }

        foreach (var it in itemsToArchive)
        {
            RemoveFromItems(it);
            AddToArchived(it);
        }

        _logger?.LogInformation("Archived group {GroupId} moved {Count} items to archive", item.LinkedGroupId, itemsToArchive.Count);

        // Sync timestamps across linked group so archived preview shows consistent times
        if (itemsToArchive.Count > 1)
        {
            var referenceItem = itemsToArchive[0];
            foreach (var it in itemsToArchive.Skip(1))
            {
                it.SyncTimestampsFrom(referenceItem);
            }

            foreach (var it in itemsToArchive)
            {
                it.RefreshTimeInProgress();
                it.RefreshTimeOnDeck();
            }
        }

        RefreshDisplayItems();
        RefreshArchivedDisplayItems();
        await SaveAsync();
        var archiveMsg = itemsToArchive.Count == 1 ? "Archived item" : $"Archived {itemsToArchive.Count} linked items";
        StartUndoTimer(archiveMsg);
    }

    [RelayCommand]
    public async Task UnarchiveOrderAsync(OrderItem? item)
    {
        if (item == null) { return; }
        if (!_archivedItemIds.Contains(item.Id))
        {
            _logger?.LogWarning("UnarchiveOrderAsync: Item {Id} not found in _archivedItemIds", item.Id);
            return;
        }

        // Get all linked items to unarchive together
        List<OrderItem> itemsToUnarchive;
        if (item.LinkedGroupId != null)
        {
            var gid = item.LinkedGroupId.Value;
            itemsToUnarchive = ArchivedItems.Where(i => i.LinkedGroupId == gid).ToList();
        }
        else
        {
            itemsToUnarchive = new List<OrderItem> { item };
        }

        _logger?.LogInformation("UnarchiveOrderAsync: Unarchiving {Count} items", itemsToUnarchive.Count);

        foreach (var it in itemsToUnarchive)
        {
            var oldStatus = it.Status;
            var oldPreviousStatus = it.PreviousStatus;
            var wasInItemIds = _itemIds.Contains(it.Id);
            var wasInArchivedIds = _archivedItemIds.Contains(it.Id);

            RemoveFromArchived(it);
            // Restore previous status so the item appears in the active view
            // (Done status is excluded from NotReady/OnDeck/InProgress groups)
            it.Status = it.PreviousStatus ?? OrderItem.OrderStatus.NotReady;
            it.PreviousStatus = null; // Clear so repair doesn't recapture
            AddToItems(it, insertAtTop: true);

            var nowInItemIds = _itemIds.Contains(it.Id);
            _logger?.LogInformation("UnarchiveOrderAsync: Item {Id} - OldStatus={OldStatus}, PrevStatus={PrevStatus}, NewStatus={NewStatus}, WasInItemIds={WasInItemIds}, NowInItemIds={NowInItemIds}",
                it.Id, oldStatus, oldPreviousStatus, it.Status, wasInItemIds, nowInItemIds);
        }

        _logger?.LogInformation("UnarchiveOrderAsync: Items collection now has {Count} items, _itemIds has {IdCount} ids",
            Items.Count, _itemIds.Count);

        RefreshDisplayItems();
        RefreshArchivedDisplayItems();
        await SaveAsync();
        StatusMessage = itemsToUnarchive.Count == 1
            ? "Unarchived item"
            : $"Unarchived {itemsToUnarchive.Count} linked items";
    }

    [RelayCommand]
    public void CycleArchivedSortMode()
    {
        ArchivedSortModeEnum = ArchivedSortModeEnum switch
        {
            OrderGroupingService.OrderLogSortMode.CreatedAt => OrderGroupingService.OrderLogSortMode.VendorName,
            OrderGroupingService.OrderLogSortMode.VendorName => OrderGroupingService.OrderLogSortMode.CreatedAt,
            _ => OrderGroupingService.OrderLogSortMode.CreatedAt
        };
        OnPropertyChanged(nameof(ArchivedSortModeEnum));
        RefreshArchivedDisplayItems();
    }
    
    [RelayCommand]
    public async Task MoveUpAsync(OrderItem? item)
    {
        if (item == null) return;

        // Try move within active items
        var idx = Items.IndexOf(item);
        if (idx > 0)
        {
            var action = new ReorderAction(item, Items as IList<OrderItem> ?? Items.ToList(), idx, idx - 1);
            _undoRedoStack.ExecuteAction(action);
            await DebouncedSaveAsync();
            StatusMessage = "Moved up";
            return;
        }

        // Try archived list
        var aidx = ArchivedItems.IndexOf(item);
        if (aidx > 0)
        {
            var action = new ReorderAction(item, ArchivedItems as IList<OrderItem> ?? ArchivedItems.ToList(), aidx, aidx - 1);
            _undoRedoStack.ExecuteAction(action);
            await DebouncedSaveAsync();
            StatusMessage = "Moved up (archived)";
        }
    }

    [RelayCommand]
    public async Task MoveDownAsync(OrderItem? item)
    {
        if (item == null) return;

        // active items
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1)
        {
            var action = new ReorderAction(item, Items as IList<OrderItem> ?? Items.ToList(), idx, idx + 1);
            _undoRedoStack.ExecuteAction(action);
            await DebouncedSaveAsync();
            StatusMessage = "Moved down";
            return;
        }

        // archived items
        var aidx = ArchivedItems.IndexOf(item);
        if (aidx >= 0 && aidx < ArchivedItems.Count - 1)
        {
            var action = new ReorderAction(item, ArchivedItems as IList<OrderItem> ?? ArchivedItems.ToList(), aidx, aidx + 1);
            _undoRedoStack.ExecuteAction(action);
            await DebouncedSaveAsync();
            StatusMessage = "Moved down (archived)";
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(OrderItem? item)
    {
        if (item == null) return;

        RemoveFromItems(item);
        RemoveFromArchived(item);
        RefreshDisplayItems();
        RefreshArchivedDisplayItems();
        await SaveAsync();
        StatusMessage = "Deleted item";
    }

    private void StartUndoTimer(string message)
    {
        StatusMessage = message + " - tap Undo to revert";

        // Reuse existing timer instead of creating new one each time
        // Ensure the main timer fires after the configured timeout
        if (_undoTimer == null)
        {
            _undoTimer = new() { Interval = TimeSpan.FromSeconds(UndoTimeoutSeconds) };
            _undoTimer.Tick += OnUndoTimerTick;
        }
        else
        {
            _undoTimer.Stop();
            _undoTimer.Interval = TimeSpan.FromSeconds(UndoTimeoutSeconds);
        }
        _undoTimer.Start();

        // Initialize and start a 1s countdown timer for the UI
        UndoSecondsRemaining = UndoTimeoutSeconds;
        if (_undoCountdownTimer == null)
        {
            _undoCountdownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
            _undoCountdownTimer.Tick += (s, e) =>
            {
                try
                {
                    if (UndoSecondsRemaining > 0) UndoSecondsRemaining--;
                    if (UndoSecondsRemaining <= 0)
                    {
                        CompleteUndoTimeout();
                    }
                }
                catch { }
            };
        }
        else
        {
            _undoCountdownTimer.Stop();
        }
        _undoCountdownTimer.Start();
    }

    private void OnUndoTimerTick(object? sender, EventArgs e)
    {
        // Timer expired - hide the UI undo bar while leaving history intact
        CompleteUndoTimeout();
    }

    private void CompleteUndoTimeout()
    {
        _undoTimer?.Stop();
        _undoCountdownTimer?.Stop();
        UndoSecondsRemaining = 0;
        UndoAvailable = false;
        UndoMessage = string.Empty;
        UpdateDefaultStatus();
    }

    /// <summary>
    /// Undo the last action
    /// </summary>
    [RelayCommand]
    private async Task UndoAsync()
    {
        if (!_undoRedoStack.CanUndo) return;

        _undoRedoStack.Undo();

        // Sync UI with model changes
        RefreshDisplayItems();
        RefreshArchivedDisplayItems();
        await SaveAsync();

        StatusMessage = "Undo applied";
    }

    /// <summary>
    /// Redo the last undone action
    /// </summary>
    [RelayCommand]
    private async Task RedoAsync()
    {
        if (!_undoRedoStack.CanRedo) return;

        _undoRedoStack.Redo();

        // Sync UI with model changes
        RefreshDisplayItems();
        RefreshArchivedDisplayItems();
        await SaveAsync();

        StatusMessage = "Redo applied";
    }

    public async Task SetStatusAsync(OrderItem? item, OrderItem.OrderStatus status, OrderItem.OrderStatus? previousStatus = null)
    {
        if (item == null) return;

        // Determine affected items (linked group vs single)
        List<OrderItem> itemsToChange;
        if (item.LinkedGroupId != null)
        {
            var gid = item.LinkedGroupId.Value;
            itemsToChange = AllItems.Where(i => i.LinkedGroupId == gid).ToList();
        }
        else
        {
            itemsToChange = new List<OrderItem> { item };
        }

        var willBeArchived = status == OrderItem.OrderStatus.Done;

        if (willBeArchived)
        {
            // Use ArchiveAction for undo support and to set PreviousStatus/IsArchived
            var action = new ArchiveAction(itemsToChange);
            _undoRedoStack.ExecuteAction(action);

            // Ensure items transition to Done so timestamps (CompletedAt) are set
            foreach (var it in itemsToChange)
            {
                it.Status = OrderItem.OrderStatus.Done;
            }

            foreach (var it in itemsToChange)
            {
                RemoveFromItems(it);
                AddToArchived(it);
            }

            // Sync timestamps for linked groups being archived
            if (itemsToChange.Count > 1)
            {
                var referenceItem = itemsToChange[0];
                foreach (var it in itemsToChange.Skip(1))
                {
                    it.SyncTimestampsFrom(referenceItem);
                }

                // Force a refresh to ensure UI updates
                foreach (var it in itemsToChange)
                {
                    it.RefreshTimeInProgress();
                    it.RefreshTimeOnDeck();
                }
            }
        }
        else
        {
            var action = new StatusChangeAction(itemsToChange, status);
            _undoRedoStack.ExecuteAction(action);
            foreach (var it in itemsToChange)
            {
                it.Status = status;
            }

            // Sync timestamps for linked groups
            if (itemsToChange.Count > 1)
            {
                var referenceItem = itemsToChange[0];
                foreach (var it in itemsToChange.Skip(1))
                {
                    it.SyncTimestampsFrom(referenceItem);
                }

                // Force a refresh to ensure UI updates
                foreach (var it in itemsToChange)
                {
                    it.RefreshTimeInProgress();
                    it.RefreshTimeOnDeck();
                }
            }
        }

        RefreshDisplayItems();
        RefreshArchivedDisplayItems();
        await SaveAsync();

        StartUndoTimer(willBeArchived ? "Archived item" : "Changed status");
    }

    public async Task AddOrderAsync(OrderItem order)
    {
        AddToItems(order, insertAtTop: true);
        RefreshDisplayItems();
        await SaveAsync();
        StatusMessage = "Order added";
        ItemAdded?.Invoke(order);
    }

    public async Task<bool> AddOrderInlineAsync()
    {
        if (string.IsNullOrWhiteSpace(NewNoteVendorName))
        {
            StatusMessage = "Vendor name is required";
            return false;
        }

        // Apply auto-coloring if enabled and vendor name is set
        var colorToUse = _newNoteColorHex;

        var order = new OrderItem
        {
            VendorName = NewNoteVendorName.Trim(),
            TransferNumbers = NewNoteTransferNumbers?.Trim() ?? string.Empty,
            WhsShipmentNumbers = NewNoteWhsShipmentNumbers?.Trim() ?? string.Empty,
            ColorHex = colorToUse,
            CreatedAt = DateTime.UtcNow
        };

        AddToItems(order, insertAtTop: true);
        RefreshDisplayItems();
        await SaveAsync();

        // Clear the form
        NewNoteVendorName = string.Empty;
        NewNoteTransferNumbers = string.Empty;
        NewNoteWhsShipmentNumbers = string.Empty;
        _newNoteColorHex = DefaultOrderColor;

        StatusMessage = "Order added";
        ItemAdded?.Invoke(order);
        return true;
    }

    /// <summary>
    /// Add a sticky note (simple note without order tracking)
    /// </summary>
    public async Task<bool> AddStickyNoteAsync()
    {
        if (string.IsNullOrWhiteSpace(StickyNoteContent))
        {
            StatusMessage = "Note content is required";
            return false;
        }

        var note = new OrderItem
        {
            NoteType = NoteType.StickyNote,
            NoteTitle = string.Empty,
            NoteContent = StickyNoteContent.Trim(),
            ColorHex = _stickyNoteColorHex,
            CreatedAt = DateTime.UtcNow,
            Status = OrderItem.OrderStatus.OnDeck // Sticky notes start as "On Deck" (yellow)
        };

        AddToItems(note, insertAtTop: true);
        RefreshDisplayItems();
        await SaveAsync();

        // Clear the form
        StickyNoteContent = string.Empty;
        _stickyNoteColorHex = DefaultNoteColor;

        StatusMessage = "Sticky note added";
        ItemAdded?.Invoke(note);
        return true;
    }

    /// <summary>
    /// Quick add a sticky note with specified content
    /// </summary>
    public async Task AddQuickStickyNoteAsync(string content, string? colorHex = null)
    {
        var note = new OrderItem
        {
            NoteType = NoteType.StickyNote,
            NoteTitle = string.Empty,
            NoteContent = content.Trim(),
            ColorHex = colorHex ?? DefaultNoteColor,
            CreatedAt = DateTime.UtcNow,
            Status = OrderItem.OrderStatus.OnDeck
        };

        AddToItems(note, insertAtTop: true);
        RefreshDisplayItems();
        await SaveAsync();
        StatusMessage = "Quick note added";
        ItemAdded?.Invoke(note);
    }

    public void SetStickyNoteColor(string colorHex)
    {
        _stickyNoteColorHex = colorHex;
    }

    public string GetStickyNoteColor() => _stickyNoteColorHex;

    public void SetNewNoteColor(string colorHex)
    {
        _newNoteColorHex = colorHex;
    }

    public string GetNewNoteColor() => _newNoteColorHex;

    [RelayCommand]
    private void ToggleArchived()
    {
        ShowArchived = !ShowArchived;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
        IsSearchActive = false;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchQuery = string.Empty;
        StatusFilters = null;
        FilterStartDate = null;
        FilterEndDate = null;
        ColorFilters = null;
        NoteTypeFilter = null;
        NoteCategoryFilter = null;
        IsSearchActive = false;
        StatusMessage = "Filters cleared";
    }

    // Bulk Operations Commands

    [RelayCommand]
    private void ToggleMultiSelectMode()
    {
        IsMultiSelectMode = !IsMultiSelectMode;
        if (!IsMultiSelectMode)
        {
            SelectedItems.Clear();
        }
        StatusMessage = IsMultiSelectMode ? "Multi-select mode enabled" : "Multi-select mode disabled";
    }

    [RelayCommand]
    private void ClearSelection()
    {
        SelectedItems.Clear();
        StatusMessage = "Selection cleared";
    }

    [RelayCommand]
    private async Task BulkArchiveAsync()
    {
        if (SelectedItems.Count == 0)
        {
            StatusMessage = "No items selected";
            return;
        }

        var itemsToArchive = SelectedItems.ToList();
        var action = new ArchiveAction(itemsToArchive);
        _undoRedoStack.ExecuteAction(action);

        // Ensure Done status so CompletedAt is stamped (mirrors ArchiveOrderAsync behaviour)
        foreach (var item in itemsToArchive)
        {
            item.Status = OrderItem.OrderStatus.Done;
        }

        // Move items from Items to ArchivedItems
        foreach (var item in itemsToArchive)
        {
            RemoveFromItems(item);
            AddToArchived(item);
        }

        SelectedItems.Clear();
        RefreshDisplayItems();
        await SaveAsync();
        StartUndoTimer($"Archived {itemsToArchive.Count} item(s)");
    }

    [RelayCommand]
    private async Task BulkUnarchiveAsync()
    {
        if (SelectedItems.Count == 0)
        {
            StatusMessage = "No items selected";
            return;
        }

        var itemsToUnarchive = SelectedItems.ToList();
        var action = new UnarchiveAction(itemsToUnarchive);
        _undoRedoStack.ExecuteAction(action);

        // Move items from ArchivedItems to Items
        foreach (var item in itemsToUnarchive)
        {
            RemoveFromArchived(item);
            AddToItems(item, insertAtTop: true);
        }

        SelectedItems.Clear();
        RefreshDisplayItems();
        await SaveAsync();
        StartUndoTimer($"Unarchived {itemsToUnarchive.Count} item(s)");
    }

    [RelayCommand]
    private async Task BulkDeleteAsync()
    {
        if (SelectedItems.Count == 0)
        {
            StatusMessage = "No items selected";
            return;
        }

        var count = SelectedItems.Count;
        var confirmResult = MessageBox.Show(
            $"Are you sure you want to delete {count} selected item(s)?",
            "Delete Items",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmResult != MessageBoxResult.Yes)
        {
            StatusMessage = "Delete cancelled";
            return;
        }

        var itemsToDelete = SelectedItems.ToList();

        // Split across active vs archived collections so each DeleteAction targets the right list
        var activeToDelete = itemsToDelete.Where(i => _itemIds.Contains(i.Id)).ToList();
        var archivedToDelete = itemsToDelete.Where(i => _archivedItemIds.Contains(i.Id)).ToList();

        if (activeToDelete.Count > 0)
        {
            var action = new DeleteAction(activeToDelete, Items);
            _undoRedoStack.ExecuteAction(action);
        }
        if (archivedToDelete.Count > 0)
        {
            var action = new DeleteAction(archivedToDelete, ArchivedItems);
            _undoRedoStack.ExecuteAction(action);
        }

        SelectedItems.Clear();
        RefreshDisplayItems();
        RefreshArchivedDisplayItems();
        await SaveAsync();

        StartUndoTimer($"Deleted {itemsToDelete.Count} item(s)");
    }

    [RelayCommand]
    private async Task BulkSetStatusAsync(OrderItem.OrderStatus newStatus)
    {
        if (SelectedItems.Count == 0)
        {
            StatusMessage = "No items selected";
            return;
        }

        var itemsToUpdate = SelectedItems.Where(i => !i.IsArchived).ToList();
        if (itemsToUpdate.Count == 0)
        {
            StatusMessage = "No active items selected";
            return;
        }

        // If setting to Done, archive the items
        if (newStatus == OrderItem.OrderStatus.Done)
        {
            var action = new ArchiveAction(itemsToUpdate);
            _undoRedoStack.ExecuteAction(action);

            // Ensure Done status so CompletedAt is stamped
            foreach (var item in itemsToUpdate)
            {
                item.Status = OrderItem.OrderStatus.Done;
            }

            foreach (var item in itemsToUpdate)
            {
                RemoveFromItems(item);
                AddToArchived(item);
            }
        }
        else
        {
            var action = new StatusChangeAction(itemsToUpdate, newStatus);
            _undoRedoStack.ExecuteAction(action);
        }

        SelectedItems.Clear();
        RefreshDisplayItems();
        RefreshArchivedDisplayItems();
        await SaveAsync();

        StartUndoTimer($"Updated {itemsToUpdate.Count} item(s) to {newStatus}");
    }

    [RelayCommand]
    private async Task BulkSetColorAsync(string colorHex)
    {
        if (SelectedItems.Count == 0)
        {
            StatusMessage = "No items selected";
            return;
        }

        var stickyNotes = SelectedItems.Where(i => i.NoteType == NoteType.StickyNote).ToList();
        if (stickyNotes.Count == 0)
        {
            StatusMessage = "No sticky notes selected (color only applies to sticky notes)";
            return;
        }

        var action = new ColorChangeAction(stickyNotes, colorHex);
        _undoRedoStack.ExecuteAction(action);

        RefreshDisplayItems();
        await SaveAsync();
        StartUndoTimer($"Updated color for {stickyNotes.Count} sticky note(s)");
    }

    [RelayCommand]
    private async Task BulkLinkAsync()
    {
        if (SelectedItems.Count < 2)
        {
            StatusMessage = "Select at least 2 items to link";
            return;
        }

        var itemsToLink = SelectedItems.ToList();
        var groupId = Guid.NewGuid();
        var action = new LinkAction(itemsToLink, groupId);
        _undoRedoStack.ExecuteAction(action);

        RefreshDisplayItems();
        await SaveAsync();
        StartUndoTimer($"Linked {itemsToLink.Count} item(s)");
    }

    [RelayCommand]
    private async Task BulkUnlinkAsync()
    {
        if (SelectedItems.Count == 0)
        {
            StatusMessage = "No items selected";
            return;
        }

        var itemsToUnlink = SelectedItems.Where(i => i.LinkedGroupId != null).ToList();
        if (itemsToUnlink.Count == 0)
        {
            StatusMessage = "No linked items selected";
            return;
        }

        var action = new UnlinkAction(itemsToUnlink);
        _undoRedoStack.ExecuteAction(action);

        RefreshDisplayItems();
        await SaveAsync();
        StartUndoTimer($"Unlinked {itemsToUnlink.Count} item(s)");
    }

    // Navigation Commands

    [RelayCommand]
    private void NavigateToItem(OrderItem? item)
    {
        if (item == null) return;
        CurrentNavigationItem = item;

        // Find the index in DisplayItems
        for (int i = 0; i < DisplayItems.Count; i++)
        {
            if (DisplayItems[i].Members.Contains(item))
            {
                CurrentItemIndex = i;
                break;
            }
        }
    }

    [RelayCommand]
    private void NavigateNext()
    {
        if (DisplayItems.Count == 0) return;

        if (CurrentItemIndex < DisplayItems.Count - 1)
        {
            CurrentItemIndex++;
            var nextGroup = DisplayItems[CurrentItemIndex];
            CurrentNavigationItem = nextGroup.First;
        }
        else
        {
            // Wrap around to the beginning
            CurrentItemIndex = 0;
            CurrentNavigationItem = DisplayItems[0].First;
        }
    }

    [RelayCommand]
    private void NavigatePrevious()
    {
        if (DisplayItems.Count == 0) return;

        if (CurrentItemIndex > 0)
        {
            CurrentItemIndex--;
            var prevGroup = DisplayItems[CurrentItemIndex];
            CurrentNavigationItem = prevGroup.First;
        }
        else
        {
            // Wrap around to the end
            CurrentItemIndex = DisplayItems.Count - 1;
            CurrentNavigationItem = DisplayItems[CurrentItemIndex].First;
        }
    }

    [RelayCommand]
    private void NavigateToTop()
    {
        if (DisplayItems.Count == 0) return;

        CurrentItemIndex = 0;
        CurrentNavigationItem = DisplayItems[0].First;
    }

    [RelayCommand]
    private void NavigateToBottom()
    {
        if (DisplayItems.Count == 0) return;

        CurrentItemIndex = DisplayItems.Count - 1;
        CurrentNavigationItem = DisplayItems[CurrentItemIndex].First;
    }

    /// <summary>
    /// Saves the current scroll position for persistence
    /// </summary>
    public void SaveScrollPosition(double position)
    {
        SavedScrollPosition = position;
    }

    /// <summary>
    /// Restores the saved scroll position
    /// </summary>
    public double GetSavedScrollPosition()
    {
        return SavedScrollPosition;
    }

    [RelayCommand]
    private async Task ExportToCsv()
    {
        try
        {
            var itemsToExport = ShowArchived ? ArchivedItems : Items;
            if (itemsToExport.Count == 0)
            {
                StatusMessage = "No items to export";
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var defaultFileName = $"OrderLog_Export_{timestamp}.csv";

            var filePath = await _dialogService.ShowSaveFileDialogAsync(
                "Export to CSV",
                defaultFileName,
                "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*");

            if (string.IsNullOrEmpty(filePath))
            {
                StatusMessage = "Export cancelled";
                return;
            }

            IsLoading = true;
            StatusMessage = "Exporting to CSV...";

            var exportService = new Services.OrderLogExportService();
            await exportService.ExportToCsvAsync(itemsToExport, filePath);

            var fileName = System.IO.Path.GetFileName(filePath);
            StatusMessage = $"Exported {itemsToExport.Count} item(s)";
            _dialogService.ShowExportSuccessDialog(fileName, filePath, itemsToExport.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export error: {ex.Message}";
            _logger?.LogError(ex, "Failed to export to CSV");
            _dialogService.ShowExportErrorDialog(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ExportToJson()
    {
        try
        {
            var itemsToExport = ShowArchived ? ArchivedItems : Items;
            if (itemsToExport.Count == 0)
            {
                StatusMessage = "No items to export";
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var defaultFileName = $"OrderLog_Export_{timestamp}.json";

            var filePath = await _dialogService.ShowSaveFileDialogAsync(
                "Export to JSON",
                defaultFileName,
                "JSON Files (*.json)|*.json|All Files (*.*)|*.*");

            if (string.IsNullOrEmpty(filePath))
            {
                StatusMessage = "Export cancelled";
                return;
            }

            IsLoading = true;
            StatusMessage = "Exporting to JSON...";

            var exportService = new Services.OrderLogExportService();
            await exportService.ExportToJsonAsync(itemsToExport, filePath);

            var fileName = System.IO.Path.GetFileName(filePath);
            StatusMessage = $"Exported {itemsToExport.Count} item(s) to JSON";
            _dialogService.ShowExportSuccessDialog(fileName, filePath, itemsToExport.Count);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export error: {ex.Message}";
            _logger?.LogError(ex, "Failed to export to JSON");
            _dialogService.ShowExportErrorDialog(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ImportFromCsv()
    {
        try
        {
            var filePaths = await _dialogService.ShowOpenFileDialogAsync(
                "Import from CSV",
                "CSV Files",
                "csv");

            if (filePaths == null || filePaths.Length == 0)
            {
                StatusMessage = "Import cancelled";
                return;
            }

            var filePath = filePaths[0]; // Take first file

            IsLoading = true;
            StatusMessage = "Importing from CSV...";

            var exportService = new Services.OrderLogExportService();
            var (success, items, errorMessage) = await exportService.ImportFromCsvAsync(filePath);

            if (!success)
            {
                StatusMessage = $"Import failed: {errorMessage}";
                _dialogService.ShowImportErrorDialog(errorMessage);
                return;
            }

            // Save all imported items
            var importedCount = 0;
            foreach (var item in items)
            {
                Items.Add(item);
                _itemIds.Add(item.Id);
                importedCount++;
            }

            await SaveAsync();

            var fileName = System.IO.Path.GetFileName(filePath);
            StatusMessage = $"Imported {importedCount} item(s) from CSV";
            _dialogService.ShowImportSuccessDialog(fileName, importedCount);

            RefreshDisplayItems();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import error: {ex.Message}";
            _logger?.LogError(ex, "Failed to import from CSV");
            _dialogService.ShowImportErrorDialog(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Copy selected item(s) to clipboard
    /// </summary>
    [RelayCommand]
    private void Copy()
    {
        var itemsToCopy = SelectedItems.Count > 0
            ? SelectedItems.ToList()
            : (SelectedItem != null ? new List<OrderItem> { SelectedItem } : new List<OrderItem>());

        if (itemsToCopy.Count == 0)
        {
            StatusMessage = "No items selected to copy";
                return;
        }

        _clipboardService.CopyToClipboard(itemsToCopy);
        StatusMessage = $"Copied {itemsToCopy.Count} item(s) to clipboard";
        _logger?.LogInformation("Copied {Count} items to clipboard", itemsToCopy.Count);
    }

    /// <summary>
    /// Paste item(s) from clipboard
    /// </summary>
    [RelayCommand]
    private async Task PasteAsync()
    {
        if (!_clipboardService.TryPasteFromClipboard(out var pastedItems))
        {
            StatusMessage = "Clipboard does not contain valid order data";
                return;
        }

        if (pastedItems.Count == 0)
        {
            StatusMessage = "No items to paste";
                return;
        }

        // Determine insertion index: after selected item or at top
        int insertIndex = 0;
        if (SelectedItem != null && _itemIds.Contains(SelectedItem.Id))
        {
            insertIndex = Items.IndexOf(SelectedItem) + 1;
        }

        // Execute with undo support
        var action = new PasteAction(pastedItems, Items, insertIndex);
        _undoRedoStack.ExecuteAction(action);

        RefreshDisplayItems();
        await SaveAsync();

        StartUndoTimer($"Pasted {pastedItems.Count} item(s)");
        _logger?.LogInformation("Pasted {Count} items", pastedItems.Count);
    }

    /// <summary>
    /// Duplicate selected item(s) (copy + paste in one action)
    /// </summary>
    [RelayCommand]
    private async Task DuplicateAsync()
    {
        var itemsToDuplicate = SelectedItems.Count > 0
            ? SelectedItems.ToList()
            : (SelectedItem != null ? new List<OrderItem> { SelectedItem } : new List<OrderItem>());

        if (itemsToDuplicate.Count == 0)
        {
            StatusMessage = "No items selected to duplicate";
                return;
        }

        var duplicatedItems = _clipboardService.CloneItems(itemsToDuplicate);

        // Auto color disabled: keep duplicated color

        // Determine insertion index: after selected item or at top
        int insertIndex = 0;
        if (SelectedItem != null && _itemIds.Contains(SelectedItem.Id))
        {
            insertIndex = Items.IndexOf(SelectedItem) + 1;
        }

        // Execute with undo support
        var action = new PasteAction(duplicatedItems, Items, insertIndex);
        _undoRedoStack.ExecuteAction(action);

        RefreshDisplayItems();
        await SaveAsync();

        StartUndoTimer($"Duplicated {duplicatedItems.Count} item(s)");
        _logger?.LogInformation("Duplicated {Count} items", duplicatedItems.Count);
    }

    

    public async Task MoveOrderAsync(OrderItem dropped, OrderItem? target)
    {
        if (dropped == target) return;

        if (target == null)
        {
            if (_itemIds.Contains(dropped.Id))
            {
                Items.Remove(dropped);
                Items.Add(dropped);
            }
        }
        else
        {
            int oldIndex = Items.IndexOf(dropped);
            int newIndex = Items.IndexOf(target);
            if (oldIndex < 0 || newIndex < 0) return;

            Items.RemoveAt(oldIndex);
            if (oldIndex < newIndex) newIndex--;
            Items.Insert(newIndex, dropped);
        }

        await SaveAsync();
    }

    /// <summary>
    /// Swap two orders' positions in the collection. Used for iOS-style slide-past reordering.
    /// </summary>
    public void SwapOrders(OrderItem item1, OrderItem item2)
    {
        if (item1 == null || item2 == null || item1.Id == item2.Id) return;

        // Find which collection contains the items
        var collection = _itemIds.Contains(item1.Id) ? Items : ArchivedItems;

        var idx1 = collection.IndexOf(item1);
        var idx2 = collection.IndexOf(item2);

        if (idx1 < 0 || idx2 < 0) return;

        // Swap by moving
        collection.Move(idx1, idx2);

        // Don't save yet - we'll save when drag finishes to avoid excessive I/O
        RefreshDisplayItems();
    }

    /// <summary>
    /// Move an item to a specific index in its collection. Used after drag-based reordering.
    /// </summary>
    public void MoveItemToIndex(OrderItem item, int newIndex)
    {
        if (item == null) return;

        // Determine which collection the item belongs to
        ObservableCollection<OrderItem> collection;
        if (item.NoteType == NoteType.StickyNote)
        {
            collection = StickyNotes;
        }
        else if (_itemIds.Contains(item.Id))
        {
            collection = Items;
        }
        else
        {
            collection = ArchivedItems;
        }

        var currentIndex = collection.IndexOf(item);

        if (currentIndex < 0 || currentIndex == newIndex) return;
        if (newIndex < 0 || newIndex >= collection.Count) return;

        collection.Move(currentIndex, newIndex);
        
        // Only refresh display items for orders, not sticky notes
        if (item.NoteType != NoteType.StickyNote)
        {
            RefreshDisplayItems();
        }
    }

    /// <summary>
    /// Move one or more orders as a block. Preserves relative order of moved items.
    /// If any item belongs to a linked group and the dragged set is a single item,
    /// the entire linked group will be moved together.
    /// </summary>
    public async Task MoveOrdersAsync(System.Collections.Generic.List<OrderItem> droppedItems, OrderItem? target)
    {
        if (droppedItems == null || droppedItems.Count == 0) return;

        // Check if we're operating on sticky notes
        bool isNotesOperation = droppedItems.Any(d => d.NoteType == NoteType.StickyNote);
        if (isNotesOperation)
        {
            // Handle sticky notes reordering
            MoveItemsInCollection(droppedItems, target, StickyNotes);
            await SaveAsync();
            return;
        }

        // If single item and it has a linked group, expand to full group
        if (droppedItems.Count == 1 && droppedItems[0].LinkedGroupId != null)
        {
            var gid = droppedItems[0].LinkedGroupId;
            var groupMembers = AllItems.Where(i => i.LinkedGroupId == gid).ToList();
            if (groupMembers.Count > 1)
            {
                droppedItems = groupMembers;
            }
        }

        // Determine target collection based on where items/target are located (O(1) checks)
        bool operateOnItems = droppedItems.Any(d => _itemIds.Contains(d.Id));
        if (target != null)
        {
            operateOnItems = _itemIds.Contains(target.Id);
        }

        // Perform the move operation on the appropriate collection
        var collection = operateOnItems ? Items : ArchivedItems;
        MoveItemsInCollection(droppedItems, target, collection);

        await SaveAsync();
        RefreshDisplayItems();
    }

    /// <summary>
    /// Helper method to move items within a collection while preserving their relative order.
    /// </summary>
    private void MoveItemsInCollection(List<OrderItem> droppedItems, OrderItem? target, ObservableCollection<OrderItem> collection)
    {
        // Remove items from collection, preserving the order they appear in the collection
        var toInsert = droppedItems
            .Where(d => collection.Contains(d))
            .OrderBy(d => collection.IndexOf(d))
            .ToList();

        foreach (var item in toInsert)
        {
            collection.Remove(item);
        }

        // Calculate insertion index and insert items
        int insertIndex = target == null ? collection.Count : Math.Max(0, collection.IndexOf(target));

        foreach (var item in toInsert)
        {
            if (insertIndex > collection.Count)
            {
                insertIndex = collection.Count;
            }
            collection.Insert(insertIndex++, item);
        }
    }

    /// <summary>
    /// Link the provided items together with the target (if provided) into a single LinkedGroupId.
    /// If any item already belongs to a group, groups are unified.
    /// </summary>
    public async Task LinkItemsAsync(System.Collections.Generic.List<OrderItem> itemsToLink, OrderItem? target)
    {
        // Simple, deterministic linking algorithm
        if (itemsToLink == null || itemsToLink.Count == 0) return;

        // Ensure target exists and is renderable
        if (target == null) return;
        if (!target.IsRenderable) return;

        // Only link active (non-archived) items - resolve by ID to get the actual instances from Items
        var activeItemsById = Items.ToDictionary(i => i.Id);

        // Resolve target from Items collection
        if (!activeItemsById.TryGetValue(target.Id, out var actualTarget))
        {
            // Target is not in active items - don't link to archived items
            return;
        }
        target = actualTarget;

        // Resolve candidates from Items collection only
        var candidates = new List<OrderItem>();
        foreach (var it in itemsToLink)
        {
            if (it == null) continue;
            if (!activeItemsById.TryGetValue(it.Id, out var knownItem)) continue;
            if (!knownItem.IsRenderable) continue;
            candidates.Add(knownItem);
        }
        if (candidates.Count == 0) return;

        // Enforce same NoteType as target
        candidates = candidates.Where(i => i.NoteType == target.NoteType).ToList();
        if (candidates.Count == 0) return;

        // Determine group id: prefer target's group, otherwise any candidate's group, otherwise new
        Guid groupId;
        if (target.LinkedGroupId != null) groupId = target.LinkedGroupId.Value;
        else
        {
            var existing = candidates.Select(c => c.LinkedGroupId).FirstOrDefault(g => g != null);
            groupId = existing ?? Guid.NewGuid();
        }

        // Assign group id to target
        target.LinkedGroupId = groupId;

        // Assign group id to candidates
        foreach (var c in candidates)
            c.LinkedGroupId = groupId;

        // Also pull in any other active items that already belonged to these groups (to unify groups)
        var groupsToUnify = new HashSet<Guid>(candidates.Select(c => c.LinkedGroupId ?? Guid.Empty).Where(g => g != Guid.Empty));
        if (target.LinkedGroupId != null) groupsToUnify.Add(target.LinkedGroupId.Value);

        foreach (var it in Items)
        {
            if (it.LinkedGroupId != null && groupsToUnify.Contains(it.LinkedGroupId.Value))
            {
                if (it.IsRenderable)
                    it.LinkedGroupId = groupId;
            }
        }

        // Sync timestamps across the newly linked group
        var allGroupItems = Items.Where(i => i.LinkedGroupId == groupId).ToList();
        if (allGroupItems.Count > 1)
        {
            // Use the target as the reference for timestamps
            foreach (var it in allGroupItems)
            {
                if (it.Id != target.Id)
                {
                    it.SyncTimestampsFrom(target);
                }
            }

            // Force a refresh to ensure UI updates
            foreach (var it in allGroupItems)
            {
                it.RefreshTimeInProgress();
                it.RefreshTimeOnDeck();
            }
        }

        await SaveAsync();
        RefreshDisplayItems();
    }

    /// <summary>
    /// Refresh the display items from the Items collection.
    /// Call this after modifying Items externally (e.g., linking orders).
    /// </summary>
    public void RefreshDisplayItems()
    {
        RefreshDisplayCollection(Items, DisplayItems);
        RefreshStatusGroups();
        RefreshStickyNotes();
        UpdateLinkedItemCounts();
        OnPropertyChanged(nameof(DisplayItems)); // Ensure UI updates
    }

    /// <summary>
    /// Refresh the sticky notes collection (separate from orders).
    /// </summary>
    private void RefreshStickyNotes()
    {
        StickyNotes.Clear();

        // Start with all sticky notes
        IEnumerable<OrderItem> notes = Items.Where(i => i.IsStickyNote);

        // Apply search and filters if active
        if (_searchService.HasActiveFilters(SearchQuery, StatusFilters, FilterStartDate, FilterEndDate, ColorFilters, NoteTypeFilter, NoteCategoryFilter))
        {
            notes = _searchService.ApplyAllFilters(
                notes,
                SearchQuery,
                StatusFilters,
                FilterStartDate,
                FilterEndDate,
                ColorFilters,
                NoteTypeFilter,
                NoteCategoryFilter);
        }

        // Order by created date and add to collection
        foreach (var note in notes.OrderBy(i => i.CreatedAt))
        {
            StickyNotes.Add(note);
        }
    }

    /// <summary>
    /// Update the LinkedItemCount property for all items based on their LinkedGroupId.
    /// </summary>
    private void UpdateLinkedItemCounts()
    {
        // Group all items by their LinkedGroupId
        var linkedGroups = AllItems
            .Where(i => i.LinkedGroupId != null)
            .GroupBy(i => i.LinkedGroupId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        // Update each item's count (count - 1 to exclude itself)
        foreach (var item in AllItems)
        {
            if (item.LinkedGroupId != null && linkedGroups.TryGetValue(item.LinkedGroupId.Value, out var count))
            {
                item.LinkedItemCount = count - 1; // Exclude the item itself
            }
            else
            {
                item.LinkedItemCount = 0;
            }
        }
    }

    /// <summary>
    /// Refresh the status-grouped collections for collapsible status view.
    /// </summary>
    private void RefreshStatusGroups()
    {
        // Apply search and filters to items before grouping by status
        IEnumerable<OrderItem> filtered = Items;

        if (_searchService.HasActiveFilters(SearchQuery, StatusFilters, FilterStartDate, FilterEndDate, ColorFilters, NoteTypeFilter, NoteCategoryFilter))
        {
            filtered = _searchService.ApplyAllFilters(
                Items,
                SearchQuery,
                StatusFilters,
                FilterStartDate,
                FilterEndDate,
                ColorFilters,
                NoteTypeFilter,
                NoteCategoryFilter);
        }

        var beforeNotReady = NotReadyItems.Count;
        var beforeOnDeck = OnDeckItems.Count;
        var beforeInProgress = InProgressItems.Count;

        // Convert to ObservableCollection for grouping service
        var filteredCollection = new ObservableCollection<OrderItem>(filtered);

        // Delegate status-group population to the grouping service
        _groupingService.PopulateStatusGroups(filteredCollection, NotReadyItems, OnDeckItems, InProgressItems);

        // Update count properties (ObservableCollection.Count doesn't raise PropertyChanged)
        NotReadyCount = NotReadyItems.Count;
        OnDeckCount = OnDeckItems.Count;
        InProgressCount = InProgressItems.Count;

        _logger?.LogInformation("RefreshStatusGroups: Items={Items}, Filtered={Filtered}, NotReady={NotReady}({NotReadyBefore}), OnDeck={OnDeck}({OnDeckBefore}), InProgress={InProgress}({InProgressBefore})",
            Items.Count, filteredCollection.Count, NotReadyItems.Count, beforeNotReady, OnDeckItems.Count, beforeOnDeck, InProgressItems.Count, beforeInProgress);
    }

    /// <summary>
    /// Refresh the archived display items from the ArchivedItems collection.
    /// </summary>
    /// <summary>
    /// Schedule a non-blocking refresh of the archived display items.
    /// If a refresh is already running, this call returns immediately.
    /// </summary>
    public void RefreshArchivedDisplayItems()
    {
        if (_archivedRefreshTask != null && !_archivedRefreshTask.IsCompleted) return;
        _archivedRefreshTask = RefreshArchivedDisplayItemsAsync();
    }

    /// <summary>
    /// Asynchronously rebuilds the archived display groups off the UI thread
    /// then applies the resulting groups back on the UI thread to avoid freezes.
    /// </summary>
    public async Task RefreshArchivedDisplayItemsAsync()
    {
        // Snapshot source and apply filters on calling thread (fast)
        IEnumerable<OrderItem> filtered = ArchivedItems;

        if (_searchService.HasActiveFilters(SearchQuery, StatusFilters, FilterStartDate, FilterEndDate, ColorFilters, NoteTypeFilter, NoteCategoryFilter))
        {
            filtered = _searchService.ApplyAllFilters(
                ArchivedItems,
                SearchQuery,
                StatusFilters,
                FilterStartDate,
                FilterEndDate,
                ColorFilters,
                NoteTypeFilter,
                NoteCategoryFilter);
        }

        var snapshot = filtered.ToList();
        // Diagnostic: log linked-group distribution in archived items to help debug grouping issues
        try
        {
            var linkedGroups = snapshot
                .Where(i => i.LinkedGroupId != null && i.LinkedGroupId != Guid.Empty)
                .GroupBy(i => i.LinkedGroupId)
                .Select(g => new { GroupId = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToList();

            var totalLinkedItems = linkedGroups.Sum(g => g.Count);
            _logger?.LogDebug("Archived snapshot: {Total} items, {LinkedItems} in {Groups} linked groups", snapshot.Count, totalLinkedItems, linkedGroups.Count);
            if (linkedGroups.Count > 0)
            {
                var sample = string.Join(", ", linkedGroups.Take(6).Select(g => $"{g.GroupId}:{g.Count}"));
                _logger?.LogDebug("Archived linked groups sample: {Sample}", sample);
            }
        }
        catch { }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var sortMode = ArchivedSortModeEnum;

        // Build groups on background thread
        var built = await Task.Run(() =>
        {
            var filteredCollection = new ObservableCollection<OrderItem>(snapshot);
            // Use the archived sort mode setting
            return _groupingService.BuildDisplayCollection(filteredCollection, false, true, sortMode);
        }).ConfigureAwait(false);
        sw.Stop();

        // Apply results back on UI thread
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                DisplayArchivedItems.Clear();
                foreach (var g in built)
                    DisplayArchivedItems.Add(g);
                UpdateDisplayCounts();
                OnPropertyChanged(nameof(DisplayArchivedItems)); // Ensure UI updates
            });
            _logger?.LogInformation("RefreshArchivedDisplayItemsAsync built {Groups} groups from {Items} items in {Ms}ms", built.Count, snapshot.Count, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to apply archived display items");
        }
    }

    /// <summary>
    /// Shared helper to build grouped display items from a source collection.
    /// Groups linked items together and optionally sorts groups by status.
    /// Sticky notes are excluded from sorting and appended in original order.
    /// Applies search/filter before grouping.
    /// </summary>
    private void RefreshDisplayCollection(
        ObservableCollection<OrderItem> source,
        ObservableCollection<OrderItemGroup> display)
    {
        // Apply search and filters first
        IEnumerable<OrderItem> filtered = source;

        if (_searchService.HasActiveFilters(SearchQuery, StatusFilters, FilterStartDate, FilterEndDate, ColorFilters, NoteTypeFilter, NoteCategoryFilter))
        {
            filtered = _searchService.ApplyAllFilters(
                source,
                SearchQuery,
                StatusFilters,
                FilterStartDate,
                FilterEndDate,
                ColorFilters,
                NoteTypeFilter,
                NoteCategoryFilter);
        }

        // Convert to ObservableCollection for grouping service
        var filteredCollection = new ObservableCollection<OrderItem>(filtered);

        // Use grouping service to build ordered display collection and apply it
        var built = _groupingService.BuildDisplayCollection(filteredCollection, true, SortStatusDescending, SortModeEnum);
        display.Clear();
        foreach (var g in built)
        {
            display.Add(g);
        }

        // Log grouping details for diagnostics
        try
        {
            var details = string.Join(',', built.Select(g => $"{(g.LinkedGroupId?.ToString() ?? "(null)")}:{g.Count}"));
            _logger?.LogInformation("OrderLog grouping built {GroupCount} groups: {Details}", built.Count, details);
        }
        catch { }

        UpdateDisplayCounts();
    }

    private void UpdateDisplayCounts()
    {
        // DisplayItems/DisplayArchivedItems are groups; DisplayMembersCount counts total members
        DisplayItemsCount = DisplayItems.Sum(g => g.Members.Count);
        DisplayArchivedItemsCount = DisplayArchivedItems.Sum(g => g.Members.Count);
        DisplayMembersCount = Items.Count + ArchivedItems.Count;
    }

    public bool GetGroupState(string? name, bool defaultValue = true)
        => _groupStateStore.Get(name, defaultValue);

    public void SetGroupState(string? name, bool value)
        => _groupStateStore.Set(name, value);

    #region Collection Helper Methods (O(1) membership with HashSet tracking)

    /// <summary>
    /// Adds an item to the Items collection with O(1) membership tracking.
    /// </summary>
    private void AddToItems(OrderItem item, bool insertAtTop = false)
    {
        lock (_collectionLock)
        {
            if (_itemIds.Contains(item.Id))
            {
                return;
            }

            if (insertAtTop)
                Items.Insert(0, item);
            else
                Items.Add(item);
            _itemIds.Add(item.Id);
        }
    }

    /// <summary>
    /// Removes an item from the Items collection with O(1) membership tracking.
    /// </summary>
    private void RemoveFromItems(OrderItem item)
    {
        lock (_collectionLock)
        {
            if (!_itemIds.Contains(item.Id)) return;

            Items.Remove(item);
            _itemIds.Remove(item.Id);
        }
    }

    /// <summary>
    /// Adds an item to the ArchivedItems collection with O(1) membership tracking.
    /// </summary>
    private void AddToArchived(OrderItem item)
    {
        lock (_collectionLock)
        {
            if (_archivedItemIds.Contains(item.Id)) return;

            item.IsArchived = true;
            ArchivedItems.Add(item);
            _archivedItemIds.Add(item.Id);
        }
    }

    /// <summary>
    /// Removes an item from the ArchivedItems collection with O(1) membership tracking.
    /// </summary>
    private void RemoveFromArchived(OrderItem item)
    {
        lock (_collectionLock)
        {
            if (!_archivedItemIds.Contains(item.Id))
            {
                return;
            }

            item.IsArchived = false;
            ArchivedItems.Remove(item);
            _archivedItemIds.Remove(item.Id);
        }
    }

    #endregion

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;

            if (_undoTimer != null)
            {
                _undoTimer.Stop();
                _undoTimer.Tick -= OnUndoTimerTick;
            }

            _undoCountdownTimer?.Stop();

            if (_statusClearTimer != null)
            {
                _statusClearTimer.Tick -= OnStatusClearTimerTick;
                _statusClearTimer.Stop();
            }

            _saveDebounceCts?.Cancel();
            _saveDebounceCts?.Dispose();
            _settingsSaveCts?.Cancel();
            _settingsSaveCts?.Dispose();

            _undoRedoStack.StackChanged -= OnUndoRedoStackChanged;

            _itemIds.Clear();
            _archivedItemIds.Clear();

            _logger?.LogInformation("OrderLogViewModel disposed");
        }

        _disposed = true;
    }

    [RelayCommand]
    public async Task ClearAllArchivedAsync()
    {
        if (ArchivedItems.Count == 0)
        {
            StatusMessage = "No archived items to clear";
            return;
        }

        var confirm = MessageBox.Show("Are you sure you want to permanently delete all archived items?", "Clear Archived", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            StatusMessage = "Clear archived cancelled";
            return;
        }

        var toDelete = ArchivedItems.ToList();
        foreach (var it in toDelete)
        {
            RemoveFromArchived(it);
        }

        RefreshArchivedDisplayItems();
        await SaveAsync();
        StatusMessage = "Cleared archived items";
    }
}
