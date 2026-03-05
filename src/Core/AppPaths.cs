using System;
using System.IO;

namespace OrderLog.Core;

/// <summary>
/// Centralized application paths for consistent directory access.
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
        Directory.CreateDirectory(SharedDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(OrderLogDir);
    }
}
