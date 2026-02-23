using System;
using System.IO;

namespace SOUP.Core;

/// <summary>
/// Centralized application paths for consistent directory access across all modules.
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// Base application data directory: %APPDATA%\SOUP
    /// </summary>
    public static string AppData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SOUP");

    /// <summary>
    /// Local application data directory: %LOCALAPPDATA%\SOUP
    /// </summary>
    public static string LocalAppData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SOUP");

    /// <summary>
    /// User's desktop directory
    /// </summary>
    public static string Desktop { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

    /// <summary>
    /// Main database directory: %APPDATA%\SOUP\Data
    /// </summary>
    public static string DataDir { get; } = Path.Combine(AppData, "Data");

    /// <summary>
    /// Shared dictionary database directory: %APPDATA%\SOUP\Shared
    /// </summary>
    public static string SharedDir { get; } = Path.Combine(AppData, "Shared");

    /// <summary>
    /// Logs directory: %APPDATA%\SOUP\Logs
    /// </summary>
    public static string LogsDir { get; } = Path.Combine(AppData, "Logs");

    /// <summary>
    /// OrderLog module directory: %APPDATA%\SOUP\OrderLog
    /// </summary>
    public static string OrderLogDir { get; } = Path.Combine(AppData, "OrderLog");

    /// <summary>
    /// AllocationBuddy module directory: %APPDATA%\SOUP\AllocationBuddy
    /// </summary>
    public static string AllocationBuddyDir { get; } = Path.Combine(AppData, "AllocationBuddy");

    /// <summary>
    /// ExpireWise module directory: %APPDATA%\SOUP\ExpireWise
    /// </summary>
    public static string ExpireWiseDir { get; } = Path.Combine(AppData, "ExpireWise");

    /// <summary>
    /// EssentialsBuddy module directory: %APPDATA%\SOUP\EssentialsBuddy
    /// </summary>
    public static string EssentialsBuddyDir { get; } = Path.Combine(AppData, "EssentialsBuddy");

    /// <summary>
    /// Main database path: %APPDATA%\SOUP\Data\SOUP.db
    /// </summary>
    public static string MainDbPath { get; } = Path.Combine(DataDir, "SOUP.db");

    /// <summary>
    /// Shared dictionary database path: %APPDATA%\SOUP\Shared\dictionaries.db
    /// </summary>
    public static string DictionaryDbPath { get; } = Path.Combine(SharedDir, "dictionaries.db");

    /// <summary>
    /// OrderLog database path: %APPDATA%\SOUP\OrderLog\orders.db
    /// </summary>
    public static string OrderLogDbPath { get; } = Path.Combine(OrderLogDir, "orders.db");

    /// <summary>
    /// Ensures all required directories exist.
    /// </summary>
    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(AppData);
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(SharedDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(OrderLogDir);
        Directory.CreateDirectory(AllocationBuddyDir);
        Directory.CreateDirectory(ExpireWiseDir);
        Directory.CreateDirectory(EssentialsBuddyDir);
    }
}
