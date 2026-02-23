namespace SOUP.Data.Entities;

/// <summary>
/// Entity for store entries (store code to name/rank mapping)
/// </summary>
public class StoreEntity
{
    /// <summary>
    /// Primary key - store code
    /// </summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string Rank { get; set; } = "";
}
