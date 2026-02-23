using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SOUP.Features.OrderLog.Models;

public enum NoteType
{
    Order = 0,
    StickyNote = 1
}

public enum NoteCategory
{
    General = 0,
    Todo = 1,
    Reminder = 2,
    Log = 3,
    Idea = 4
}

public partial class OrderItem : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    private NoteType _noteType = NoteType.Order;

    /// <summary>
    /// Category for sticky notes (General, Todo, Reminder, Log, Idea).
    /// Only applies when NoteType is StickyNote.
    /// </summary>
    [ObservableProperty]
    private NoteCategory _noteCategory = NoteCategory.General;

    [ObservableProperty]
    private string _vendorName = string.Empty;

    /// <summary>
    /// Title for sticky notes (e.g., "Quick Note", "TODO", etc.)
    /// </summary>
    [ObservableProperty]
    private string _noteTitle = string.Empty;

    [ObservableProperty]
    private string _transferNumbers = string.Empty;

    [ObservableProperty]
    private string _whsShipmentNumbers = string.Empty;

    /// <summary>
    /// Content for sticky notes
    /// </summary>
    [ObservableProperty]
    private string _noteContent = string.Empty;

    public enum OrderStatus
    {
        NotReady = 0,
        OnDeck = 1,
        InProgress = 2,
        Done = 3
    }

    [ObservableProperty]
    private OrderStatus _status = OrderStatus.NotReady;

    /// <summary>
    /// Stores the status before archiving, so it can be restored when unarchiving.
    /// </summary>
    [ObservableProperty]
    private OrderStatus? _previousStatus;

    [ObservableProperty]
    private string _colorHex = Constants.OrderLogColors.DefaultOrder;

    /// <summary>
    /// Stores the color when the order was OnDeck, so the OnDeck timer can maintain its color.
    /// </summary>
    [ObservableProperty]
    private string _onDeckColorHex = Constants.OrderLogColors.StatusOnDeck;

    /// <summary>
    /// Stores the color when the order was InProgress, so the InProgress timer can maintain its color.
    /// </summary>
    [ObservableProperty]
    private string _inProgressColorHex = Constants.OrderLogColors.StatusInProgress;

    [ObservableProperty]
    private Guid? _linkedGroupId;

    /// <summary>
    /// Number of other items linked to this item (excluding itself).
    /// Updated by the ViewModel when linked groups change.
    /// </summary>
    [ObservableProperty]
    private int _linkedItemCount = 0;

    [ObservableProperty]
    private bool _isArchived = false;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? OnDeckAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Formatted display of the creation timestamp.
    /// Shows time only if created today, otherwise shows date and time.
    /// </summary>
    [JsonIgnore]
    public string CreatedAtDisplay
    {
        get
        {
            if (CreatedAt.Date == DateTime.Today)
                return CreatedAt.ToString("h:mm tt").ToLower();
            else if (CreatedAt.Year == DateTime.Now.Year)
                return CreatedAt.ToString("MMM d, h:mm tt").ToLower();
            else
                return CreatedAt.ToString("MMM d yyyy, h:mm tt").ToLower();
        }
    }

    /// <summary>
    /// Accumulated time from previous "In Progress" sessions (laps).
    /// Stored as ticks for JSON serialization.
    /// </summary>
    public long AccumulatedTimeTicks { get; set; } = 0;

    [JsonIgnore]
    public TimeSpan AccumulatedTime
    {
        get => TimeSpan.FromTicks(AccumulatedTimeTicks);
        set => AccumulatedTimeTicks = value.Ticks;
    }

    /// <summary>
    /// Total time spent in OnDeck status.
    /// Stored as ticks for JSON serialization.
    /// </summary>
    public long OnDeckDurationTicks { get; set; } = 0;

    [JsonIgnore]
    public TimeSpan OnDeckDuration
    {
        get => TimeSpan.FromTicks(OnDeckDurationTicks);
        set => OnDeckDurationTicks = value.Ticks;
    }

    /// <summary>
    /// Order for manual sorting/reordering (lower = earlier in list)
    /// </summary>
    public int Order { get; set; }

    [JsonIgnore]
    public TimeSpan TimeInProgress
    {
        get
        {
            // Current lap time only — count only overlap with work hours
            if (StartedAt == null) return TimeSpan.Zero;
            if (Status == OrderStatus.InProgress)
            {
                return CalculateWorkTimeBetween(StartedAt.Value, DateTime.Now);
            }
            return TimeSpan.Zero;
        }
    }

    [JsonIgnore]
    public TimeSpan TimeOnDeck
    {
        get
        {
            // If currently OnDeck, calculate live duration
            if (Status == OrderStatus.OnDeck && OnDeckAt != null)
            {
                return DateTime.Now - OnDeckAt.Value;
            }
            // Otherwise, return the stored duration from when it was OnDeck
            return OnDeckDuration;
        }
    }

    [JsonIgnore]
    public string TimeOnDeckDisplay
    {
        get
        {
            var time = TimeOnDeck;
            if (time == TimeSpan.Zero) return string.Empty;
            return FormatTimeSpan(time);
        }
    }

    /// <summary>
    /// Total time including all previous laps plus current session.
    /// </summary>
    [JsonIgnore]
    public TimeSpan TotalTimeInProgress => AccumulatedTime + TimeInProgress;

    /// <summary>
    /// Whether there are previous laps to show.
    /// </summary>
    [JsonIgnore]
    public bool HasPreviousLaps => AccumulatedTimeTicks > 0;

    [JsonIgnore]
    public string TimeInProgressDisplay
    {
        get
        {
            var current = TimeInProgress;
            var total = TotalTimeInProgress;

            // If there are previous laps, show "current (total)"
            if (HasPreviousLaps && Status == OrderStatus.InProgress)
            {
                return $"{FormatTimeSpan(current)} ({FormatTimeSpan(total)})";
            }

            // For Done status with laps, just show total
            if (HasPreviousLaps && Status == OrderStatus.Done)
            {
                return FormatTimeSpan(total);
            }

            // No laps, just show current/total
            return FormatTimeSpan(total);
        }
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalDays >= 7)
        {
            int weeks = (int)(ts.TotalDays / 7);
            int days = (int)(ts.TotalDays % 7);
            return days > 0 ? $"{weeks}w {days}d" : $"{weeks}w";
        }
        else if (ts.TotalDays >= 1)
        {
            int days = (int)ts.TotalDays;
            int hours = ts.Hours;
            return hours > 0 ? $"{days}d {hours}h" : $"{days}d";
        }
        else if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m";
        }
        else
        {
            return $"{ts.Minutes}m {ts.Seconds:D2}s";
        }
    }

    // Cache for optimized refresh - only notify when display actually changes
    private string _lastTimeDisplay = string.Empty;

    /// <summary>
    /// Called by a UI timer to raise PropertyChanged for computed properties.
    /// Only raises notifications if the displayed value actually changed.
    /// </summary>
    public void RefreshTimeInProgress()
    {
        var currentDisplay = TimeInProgressDisplay;
        if (currentDisplay != _lastTimeDisplay)
        {
            _lastTimeDisplay = currentDisplay;
            OnPropertyChanged(nameof(TimeInProgress));
            OnPropertyChanged(nameof(TimeInProgressDisplay));
        }
    }

    public void RefreshTimeOnDeck()
    {
        OnPropertyChanged(nameof(TimeOnDeck));
        OnPropertyChanged(nameof(TimeOnDeckDisplay));
    }

    /// <summary>
    /// Resets the "In Progress" timer, clearing accumulated time and current session.
    /// </summary>
    public void ResetInProgressTimer()
    {
        AccumulatedTimeTicks = 0;
        if (Status == OrderStatus.InProgress)
        {
            StartedAt = DateTime.Now; // Restart current session
        }
        else
        {
            StartedAt = null;
        }
        RefreshTimeInProgress();
    }

    /// <summary>
    /// Resets the "On Deck" timer, clearing stored duration and restarting if currently OnDeck.
    /// </summary>
    public void ResetOnDeckTimer()
    {
        OnDeckDurationTicks = 0;
        if (Status == OrderStatus.OnDeck)
        {
            OnDeckAt = DateTime.Now; // Restart current session
        }
        else
        {
            OnDeckAt = null;
        }
        RefreshTimeOnDeck();
    }

    /// <summary>
    /// Resets all timers (both In Progress and On Deck).
    /// </summary>
    public void ResetAllTimers()
    {
        ResetInProgressTimer();
        ResetOnDeckTimer();
    }

    partial void OnStatusChanged(OrderStatus value)
    {
        UpdateStatusColor(value);
        UpdateTimestamps(value);
        RefreshTimeInProgress();
        RefreshTimeOnDeck();
    }

    private void UpdateStatusColor(OrderStatus status)
    {
        // Only update color for non-Done statuses (keep custom colors when marking done)
        ColorHex = status switch
        {
            OrderStatus.NotReady => Constants.OrderLogColors.StatusNotReady,
            OrderStatus.OnDeck => Constants.OrderLogColors.StatusOnDeck,
            OrderStatus.InProgress => Constants.OrderLogColors.StatusInProgress,
            OrderStatus.Done => ColorHex,         // Keep existing color
            _ => ColorHex
        };

        // Capture OnDeck color when entering OnDeck status
        if (status == OrderStatus.OnDeck)
        {
            OnDeckColorHex = Constants.OrderLogColors.StatusOnDeck;
        }

        // Capture InProgress color when entering InProgress status
        if (status == OrderStatus.InProgress)
        {
            InProgressColorHex = Constants.OrderLogColors.StatusInProgress;
        }
    }

    private void UpdateTimestamps(OrderStatus status)
    {
        // Initialize _previousStatusForLap if this is the first status change after loading
        // Use StartedAt presence to detect if we were InProgress (StartedAt is only set when InProgress)
        _previousStatusForLap ??= StartedAt != null ? OrderStatus.InProgress : Status;

        // If leaving InProgress, accumulate the current session time (only work hours)
        if (_previousStatusForLap == OrderStatus.InProgress && status != OrderStatus.InProgress && StartedAt != null)
        {
            AccumulatedTime += CalculateWorkTimeBetween(StartedAt.Value, DateTime.Now);
        }

        // If leaving OnDeck, store the OnDeck duration
        if (_previousStatusForLap == OrderStatus.OnDeck && status != OrderStatus.OnDeck && OnDeckAt != null)
        {
            OnDeckDuration = DateTime.Now - OnDeckAt.Value;
        }

        switch (status)
        {
            case OrderStatus.OnDeck:
                // Set OnDeckAt when moving to OnDeck status
                if (OnDeckAt == null)
                {
                    OnDeckAt = DateTime.Now;
                    OnDeckDuration = TimeSpan.Zero; // Reset duration when starting new OnDeck session
                }
                StartedAt = null;
                CompletedAt = null;
                break;

            case OrderStatus.InProgress:
                // Only set StartedAt if not already set (preserve on deserialization/reload)
                if (StartedAt == null)
                {
                    StartedAt = DateTime.Now;
                }
                OnDeckAt = null; // Clear OnDeck time when starting work
                CompletedAt = null;
                break;

            case OrderStatus.Done:
                // Time was already accumulated above when leaving InProgress
                StartedAt = null; // Clear so TimeInProgress returns Zero
                OnDeckAt = null; // Clear OnDeck time
                CompletedAt ??= DateTime.Now; // Only set if not already set
                break;

            case OrderStatus.NotReady:
                StartedAt = null;
                OnDeckAt = null;
                CompletedAt = null;
                // Keep AccumulatedTime so we can resume later
                break;
        }

        _previousStatusForLap = status;
    }

    /// <summary>
    /// Synchronizes timestamps with another item (typically used for linked groups).
    /// Copies OnDeckAt, StartedAt, OnDeckDuration, AccumulatedTime, and timer colors from the source item.
    /// </summary>
    public void SyncTimestampsFrom(OrderItem source)
    {
        if (source == null) return;

        // Sync timestamps
        OnDeckAt = source.OnDeckAt;
        StartedAt = source.StartedAt;
        CompletedAt = source.CompletedAt;

        // Sync durations
        OnDeckDuration = source.OnDeckDuration;
        AccumulatedTime = source.AccumulatedTime;

        // Sync timer colors
        OnDeckColorHex = source.OnDeckColorHex;
        InProgressColorHex = source.InProgressColorHex;

        // Sync previous status for lap tracking
        _previousStatusForLap = source._previousStatusForLap;

        // Refresh displays
        RefreshTimeInProgress();
        RefreshTimeOnDeck();
    }

    // Track previous status for lap accumulation (initialized to current status)
    [JsonIgnore]
    private OrderStatus? _previousStatusForLap;

    /// <summary>
    /// Resets the accumulated lap time to zero.
    /// </summary>
    public void ResetAccumulatedTime()
    {
        AccumulatedTimeTicks = 0;
        OnPropertyChanged(nameof(AccumulatedTime));
        OnPropertyChanged(nameof(TotalTimeInProgress));
        OnPropertyChanged(nameof(HasPreviousLaps));
        RefreshTimeInProgress();
    }

    private static TimeSpan CalculateWorkTimeBetween(DateTime start, DateTime end)
    {
        if (end <= start) return TimeSpan.Zero;

        // Workday boundaries
        var workStart = new TimeSpan(8, 0, 0);   // 08:00
        var workEnd = new TimeSpan(16, 30, 0);   // 16:30

        TimeSpan total = TimeSpan.Zero;

        var day = start.Date;
        var lastDay = end.Date;

        while (day <= lastDay)
        {
            var dayWorkStart = day + workStart;
            var dayWorkEnd = day + workEnd;

            var segStart = start > dayWorkStart ? start : dayWorkStart;
            var segEnd = end < dayWorkEnd ? end : dayWorkEnd;

            if (segEnd > segStart)
            {
                total += segEnd - segStart;
            }

            day = day.AddDays(1);
        }

        return total;
    }

    /// <summary>
    /// Helper property to check if this is a sticky note
    /// </summary>
    [JsonIgnore]
    public bool IsStickyNote => NoteType == NoteType.StickyNote;

    /// <summary>
    /// Whether this item should be rendered as an order card in the UI.
    /// Items with any content are renderable. Only truly empty items are hidden.
    /// </summary>
    [JsonIgnore]
    public bool IsRenderable => !IsPracticallyEmpty || IsPlaceholder;

    /// <summary>
    /// Transient flag used to indicate this order is a UI placeholder (should render, but not be treated as a filled order).
    /// This is not persisted.
    /// </summary>
    [JsonIgnore]
    public bool IsPlaceholder { get; set; } = false;

    /// <summary>
    /// Display title - uses VendorName for orders, first line of NoteContent for sticky notes
    /// </summary>
    [JsonIgnore]
    public string DisplayTitle => NoteType == NoteType.StickyNote
        ? (string.IsNullOrWhiteSpace(NoteContent) ? "Quick Note" : NoteContent.Split('\n')[0].Trim())
        : VendorName;

    /// <summary>
    /// Checks if this item is practically empty (no meaningful content).
    /// Used to filter out blank placeholders during persistence and linking operations.
    /// </summary>
    [JsonIgnore]
    public bool IsPracticallyEmpty =>
        string.IsNullOrWhiteSpace(VendorName)
        && string.IsNullOrWhiteSpace(TransferNumbers)
        && string.IsNullOrWhiteSpace(WhsShipmentNumbers)
        && string.IsNullOrWhiteSpace(NoteContent);

    /// <summary>
    /// Factory helper to create a new blank Order (non-sticky note) with sensible defaults.
    /// </summary>
    public static OrderItem CreateBlankOrder(string vendorName = "",
                                            string transferNumbers = "",
                                            string whsShipmentNumbers = "",
                                            string? colorHex = null,
                                            bool isPlaceholder = false)
    {
        return new OrderItem
        {
            NoteType = NoteType.Order,
            VendorName = vendorName ?? string.Empty,
            TransferNumbers = transferNumbers ?? string.Empty,
            WhsShipmentNumbers = whsShipmentNumbers ?? string.Empty,
            ColorHex = colorHex ?? Constants.OrderLogColors.DefaultOrder,
            Status = OrderStatus.NotReady,
            IsPlaceholder = isPlaceholder,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Factory helper to create a new blank Sticky Note with sensible defaults.
    /// </summary>
    public static OrderItem CreateBlankNote(string content = "", string? colorHex = null)
    {
        return new OrderItem
        {
            NoteType = NoteType.StickyNote,
            NoteTitle = string.Empty,
            NoteContent = content ?? string.Empty,
            ColorHex = colorHex ?? Constants.OrderLogColors.DefaultNote,
            Status = OrderStatus.OnDeck,
            CreatedAt = DateTime.UtcNow
        };
    }
}
