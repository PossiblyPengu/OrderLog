namespace SOUP.Features.OrderLog.Models;

public class OrderLogWidgetSettings
{
    public double CardFontSize { get; set; } = 13.0;
    public bool ShowNowPlaying { get; set; } = true;
    public bool ShowArchived { get; set; } = true;
    public int UndoTimeoutSeconds { get; set; } = 5;
    public string DefaultOrderColor { get; set; } = "#B56576";
    public string DefaultNoteColor { get; set; } = "#FFD700";
    public bool NotesOnlyMode { get; set; } = false;
    public bool SortByStatus { get; set; } = true; // Always true - status headers always visible
    public bool SortStatusDescending { get; set; } = false;
    public bool AutoColorByVendor { get; set; } = true; // Auto-assign colors based on vendor name

    // Status group expand/collapse state
    public bool NotReadyGroupExpanded { get; set; } = true;
    public bool OnDeckGroupExpanded { get; set; } = true;
    public bool InProgressGroupExpanded { get; set; } = true;

    // Notes section expand/collapse state
    public bool NotesExpanded { get; set; } = true;

    // Independent theme control for widget
    public bool UseIndependentTheme { get; set; } = false;
    public bool WidgetIsDarkMode { get; set; } = true;
}
