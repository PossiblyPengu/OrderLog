namespace SOUP.Features.OrderLog.Constants;

/// <summary>
/// Centralized color constants for OrderLog feature.
/// Eliminates magic string color codes throughout the codebase.
/// </summary>
public static class OrderLogColors
{
    // Default colors for new items
    public const string DefaultOrder = "#B56576";
    public const string DefaultNote = "#FFD700";

    // Status colors (matches OrderItem.UpdateStatusColor)
    public const string StatusNotReady = "#FF4444";       // Red
    public const string StatusOnDeck = "#FFD700";         // Yellow/Gold
    public const string StatusInProgress = "#4CAF50";     // Green

    // Additional predefined note colors
    public const string NoteYellow = "#FFD700";
    public const string NotePink = "#FFB6C1";
    public const string NoteBlue = "#87CEEB";
    public const string NoteGreen = "#90EE90";
    public const string NotePurple = "#DDA0DD";
    public const string NoteOrange = "#FFB347";

    // App / palette colors used in the color picker and UI
    public const string PaletteIndigo = "#667EEA";
    public const string PaletteViolet = "#8B5CF6";
    public const string PaletteEmerald = "#10B981";
    public const string PaletteAmber = "#F59E0B";
    public const string PaletteDanger = "#EF4444";
    public const string PaletteBlue = "#3B82F6";

    // Extended palette (picked from OrderColorPickerWindow)
    public const string ExtMoss = "#6AA84F";
    public const string ExtGold = "#FFD966";
    public const string ExtSky = "#6FA8DC";
    public const string ExtRose = "#C27BA0";
    public const string ExtCoral = "#FF7F50";
    public const string ExtLime = "#9ACD32";
    public const string ExtTurquoise = "#40E0D0";
    public const string ExtPink = "#EC4899";
    public const string ExtTeal = "#14B8A6";
}
