using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.Converters;

/// <summary>
/// Converts NoteType to Visibility, with optional inversion.
/// Eliminates repetitive DataTriggers for showing/hiding elements based on NoteType.
/// </summary>
public class NoteTypeToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// The NoteType value that should result in Visible.
    /// Default is StickyNote.
    /// </summary>
    public NoteType VisibleWhen { get; set; } = NoteType.StickyNote;

    /// <summary>
    /// If true, inverts the logic (shows when NOT matching VisibleWhen).
    /// </summary>
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not NoteType noteType)
            return Visibility.Collapsed;

        // Check if parameter overrides VisibleWhen
        if (parameter is string paramStr && Enum.TryParse<NoteType>(paramStr, out var paramNoteType))
        {
            var matches = noteType == paramNoteType;
            return Invert ? (matches ? Visibility.Collapsed : Visibility.Visible)
                         : (matches ? Visibility.Visible : Visibility.Collapsed);
        }

        // Use VisibleWhen property
        var isMatch = noteType == VisibleWhen;
        return Invert ? (isMatch ? Visibility.Collapsed : Visibility.Visible)
                     : (isMatch ? Visibility.Visible : Visibility.Collapsed);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Shows element only for Order items (not StickyNotes).
/// </summary>
public class ShowForOrdersConverter : NoteTypeToVisibilityConverter
{
    public ShowForOrdersConverter()
    {
        VisibleWhen = NoteType.Order;
    }
}

/// <summary>
/// Shows element only for StickyNote items.
/// </summary>
public class ShowForNotesConverter : NoteTypeToVisibilityConverter
{
    public ShowForNotesConverter()
    {
        VisibleWhen = NoteType.StickyNote;
    }
}
