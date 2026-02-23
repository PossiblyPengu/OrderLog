namespace SOUP.Data.Entities;

/// <summary>
/// Entity for vendor entries used in OrderLog cards.
/// Stores vendor names for autocomplete and standardization.
/// </summary>
public class VendorEntity
{
    /// <summary>
    /// Primary key - vendor name (normalized to uppercase for matching)
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Display name (original casing as entered by user)
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Optional vendor code/abbreviation
    /// </summary>
    public string Code { get; set; } = "";

    /// <summary>
    /// Number of times this vendor has been used (for sorting by popularity)
    /// </summary>
    public int UseCount { get; set; } = 0;

    /// <summary>
    /// Optional color hex code associated with this vendor (for auto-coloring)
    /// </summary>
    public string ColorHex { get; set; } = "";
}
