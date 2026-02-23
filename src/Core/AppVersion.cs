namespace SOUP.Core;

/// <summary>
/// Application version information and changelog.
/// </summary>
/// <remarks>
/// <para>
/// This static class contains the application's version information,
/// release date, and changelog history. Update this class when
/// releasing new versions.
/// </para>
/// <para>
/// Version Format: MAJOR.MINOR.PATCH
/// - MAJOR: Breaking changes or major new features
/// - MINOR: New features, backwards compatible
/// - PATCH: Bug fixes and minor improvements
/// </para>
/// </remarks>
public static class AppVersion
{
    /// <summary>
    /// The current application version string (e.g., "4.6.1").
    /// </summary>
    public const string Version = "5.9.0";

    /// <summary>
    /// The current version display string with 'v' prefix (e.g., "v4.6.1").
    /// </summary>
    public const string DisplayVersion = "v" + Version;

    /// <summary>
    /// The release channel (Stable, Beta, Alpha, Dev).
    /// </summary>
    public const string Channel = "Stable";

    /// <summary>
    /// The build date in ISO format.
    /// </summary>
    public const string BuildDate = "2026-02-10";

    /// <summary>
    /// Full version string with channel (e.g., "v4.3.0 - Stable").
    /// </summary>
    public const string FullVersion = DisplayVersion + " - " + Channel;

    /// <summary>
    /// Gets the changelog entries for all versions.
    /// </summary>
    public static IReadOnlyList<ChangelogEntry> Changelog { get; } = new List<ChangelogEntry>
    {
        new("5.1.0", "2026-02-04", "Release Update", new[]
        {
            "feat: Enhance ExpireWiseView with scrolling and property change handling- Added PropertyChanged event handling to scroll MainContentScrollViewer to top when SelectedStore changes.- Removed unused selection change handling for ItemsGrid.- Implemented horizontal scrolling for TimelineScroll with mouse wheel.feat: Introduce development bypass for admin checks in ExternalDataSettingsView- Added DEV_BYPASS_ADMIN_CHECK constant to skip admin credential verification during development.feat: Update UnifiedSettingsWindow layout with Data Backup section- Added a new section for data backup options including export, import, and open data folder buttons.- Adjusted layout to accommodate new features.fix: Lazy-load OrderLog settings view in UnifiedSettingsWindow- Modified logic to create OrderLogSettingsView only if OrderLogSettings is available.feat: Improve theme handling in OrderLogWidgetWindow- Added event subscription for widget theme setting changes.- Implemented logic to apply independent theme settings based on ViewModel.feat: Add PowerShell scripts for module execution- Created scripts for running AllocationBuddy, EssentialsBuddy, ExpireWise, Fun Stuff, OrderLog, and SwiftLabel modules with build options.feat: Implement database backup service- Added DatabaseBackupService for exporting and importing SQLite database backups.- Supports creating ZIP archives and includes metadata for backups.feat: Introduce converters for ExpireWise- Added NullToBoolConverter, DateEqualsConverter, and IntToVisibilityConverter for UI binding scenarios.feat: Create StoreTabModel and StoreDropdownItem models- Implemented models for managing store tabs and dropdown items in ExpireWise."
        }),
        new("4.36.0", "2026-01-29", "Release Update", new[]
        {
            "feat: Add modernization progress documentation for Order Log Widget- Created MODERNIZATION_PROGRESS.md to outline the phases and changes made during the UI modernization of the Order Log Widget.- Documented completed phases including styling updates for cards, fields, dropdowns, buttons, and form controls.- Highlighted key improvements, statistics, and optional future enhancements.style: Introduce modern header styles for buttons and search bar- Added ModernHeaderStyles.xaml with new styles for pill buttons, icon buttons, and a floating search bar.- Implemented animations for hover effects and scaling.test: Set up infrastructure tests with xUnit- Created GlobalUsings.cs to include xUnit globally for tests.- Established Infrastructure.Tests.csproj with necessary package references for testing.- Implemented SqliteRepositoryTests.cs to validate repository functionality with SQLite.- Developed FileImportExportServiceTests.cs to ensure correct import/export behavior for files."
        }),
        new("4.35.0", "2026-01-28", "Release Update", new[]
        {
            "Enhance EssentialsBuddy with snapshot functionality and UI improvements- Added snapshot saving and comparison features in EssentialsBuddyViewModel.- Updated status filter labels for clarity in EssentialsBuddyView.- Improved UI layout with buttons for saving snapshots and comparing runs.- Implemented default sorting for the EssentialsBuddy data grid.- Enhanced column width persistence for better user experience."
        }),
        new("4.34.2", "2026-01-28", "Release Update", new[]
        {
            "Update sorting logic for archived items in OrderLogViewModel"
        }),
        new("4.34.1", "2026-01-28", "Release Update", new[]
        {
            "Enhance time calculation to consider only work hours for order items"
        }),
        new("4.32.3", "2026-01-27", "Release Update", new[]
        {
            "1"
        }),
        new("4.30.2", "2026-01-22", "Release Update", new[]
        {
            "feat: Add notification system and import/export functionality for expiration items- Implemented a notification system to check and notify users of expiring items.- Added ExpireWiseImportExportService for importing from CSV and exporting to CSV/Excel.- Created ExpireWiseItemService for managing expiration items (add, update, delete).- Developed ExpireWiseMonthNavigationService for month navigation and item grouping.- Introduced ExpireWiseSearchService for searching and filtering expiration items.- Added unit tests for import/export and month navigation functionalities.- Updated ExpireWiseView.xaml to include a notification button and inline notification list."
        }),
        new("4.30.1", "2026-01-22", "Release Update", new[]
        {
            "feat: add update progress panel and download functionality in AboutPage"
        }),
        new("4.22.0", "2026-01-13", "Release Update", new[]
        {
            "feat: Update project to .NET 10 and enhance globalization support- Updated runtime configuration to enable invariant globalization and predefined cultures.- Added XML documentation for GeneratedInternalTypeHelper methods in publish-framework and publish-portable.- Modified README and publish scripts to reflect .NET 10 requirements.- Refactored various classes to use target-typed new expressions for cleaner code.- Improved error handling and performance in HexToBrushConverter and StatusToColorConverter.- Introduced a new OrderLogColors class for better color management in OrderLog views.- Enhanced the CI workflow for building and testing the project on GitHub Actions."
        }),
        new("4.21.0", "2026-01-13", "Release Update", new[]
        {
            "Update target framework to .NET 10.0 across all projects and scripts"
        }),
        new("4.20.1", "2026-01-12", "Release Update", new[]
        {
            "Enhance widget management and UI improvements- Added handling for widget closure to ensure the main application shuts down if no windows are visible.- Updated OrderLogWidgetView.xaml to use a new elevated card style and adjusted margins for better layout.- Implemented a retry mechanism for fetching album art in SpotifyService, improving user experience when album art is not immediately available.- Introduced new theme resources in BasicTheme.xaml for better styling consistency, including elevated card styles and ghost button styles."
        }),
        new("4.20.0", "2026-01-12", "Release Update", new[]
        {
            "Refactor application startup logic to improve widget handling and splash screen management"
        }),
        new("4.19.1", "2026-01-12", "Release Update", new[]
        {
            "Enhance splash screen handling to only display for main app, ensuring proper status updates and closing behavior."
        }),
        new("4.19.0", "2026-01-12", "Release Update", new[]
        {
            "Add VendorNameGroupComparerConverter and update OrderLogWidgetView for visibility binding"
        }),
        new("4.18.4", "2026-01-12", "Release Update", new[]
        {
            "Improve album art handling and add follow-up polling after media key actions"
        }),
        new("4.18.3", "2026-01-12", "Release Update", new[]
        {
            "feat: Implement themed splash screen with animations and status updates during application startup- Added a new SplashWindow to display during application startup.- Integrated fade-in and fade-out animations for the splash screen.- Included logo scaling effect and progress animation.- Updated App.xaml.cs to show splash screen and update status during initialization.- Enhanced AppVersion to reflect the new version 4.18.2 and added changelog entries.- Modified OrderLogWidgetWindow to clear taskbar overlay on closing.- Added error handling for build and publish processes in release.ps1."
        }),
        new("4.18.2", "2026-01-12", "Release Update", new[]
        {
            "feat: Implement themed splash screen with animations and status updates during application startup- Added a new SplashWindow to display during application startup.- Integrated fade-in and fade-out animations for the splash screen.- Included logo scaling effect and progress animation.- Updated App.xaml.cs to show splash screen and update status during initialization.- Enhanced AppVersion to reflect the new version 4.18.1 and added changelog entries.- Modified OrderLogWidgetWindow to clear taskbar overlay on closing."
        }),
        new("4.18.1", "2026-01-12", "Release Update", new[]
        {
            "feat: Add splash screen with animations and status updates during application startup- Implemented a themed splash screen that displays during startup, showing status updates for various initialization steps.- Updated application version to 4.18.0.- Enhanced the OrderLogWidgetWindow to clear the taskbar overlay on closing.- Added fade-in and fade-out animations for the splash screen, along with a logo scaling effect.- Updated changelog to reflect the new splash screen feature."
        }),
        new("4.18.0", "2026-01-12", "Release Update", new[]
        {
            "Add splash screen with animations and status updates during application startup"
        }),
        new("4.17.14", "2026-01-12", "Release Update", new[]
        {
            "Enhance theme management and error handling in release scripts- Added error handling for build and publish processes in release.ps1.- Updated ThemeService to load ModernStyles before applying the basic theme.- Extended BasicTheme.xaml with module icons and additional color definitions.- Modified OrderLogWidgetWindow to support basic theme selection in ApplyThemeResources method."
        }),
        new("4.17.11", "2026-01-12", "Release Update", new[]
        {
            "Add AppLifecycleService for centralized application lifecycle management"
        }),
        new("4.17.9", "2026-01-12", "Release Update", new[]
        {
            "Increase timeout for application closure and improve process termination handling in updater script"
        }),
        new("4.17.8", "2026-01-12", "Release Update", new[]
        {
            "Enhance command-line argument handling for widget launch and update UI styles"
        }),
        new("4.17.7", "2026-01-12", "Release Update", new[]
        {
            "Add widget process handling to manage SOUP application lifecycle"
        }),
        new("4.17.6", "2026-01-12", "Release Update", new[]
        {
            "Enhance process termination logic during updates by killing all SOUP processes and logging errors"
        }),
        new("4.17.5", "2026-01-12", "Release Update", new[]
        {
            "Dispose tray icon service when hiding widget and before application shutdown"
        }),
        new("4.17.4", "2026-01-12", "Release Update", new[]
        {
            "Add Basic Windows theme and update theme application logic"
        }),
        new("4.17.3", "2026-01-12", "Release Update", new[]
        {
            "Refactor application shutdown process in AboutWindow and OrderLogWidgetWindow"
        }),
        new("4.17.2", "2026-01-12", "Release Update", new[]
        {
            "Implement update handling by bypassing closing confirmations during application updates"
        }),
        new("4.17.1", "2026-01-12", "Release Update", new[]
        {
            "Replace xcopy with robocopy for more reliable file copying during updates"
        }),
        new("4.17.0", "2026-01-12", "Release Update", new[]
        {
            "Update prerequisites to .NET 9 and add basic theme option in settings"
        }),
        new("4.16.0", "2026-01-10", "Release Update", new[]
        {
            "Add command to copy item redistribution data to clipboard"
        }),
        new("4.15.1", "2026-01-09", "Release Update", new[]
        {
            "Update button tooltip text for clarity in AllocationBuddyRPGView"
        }),
        new("4.15.0", "2026-01-09", "Release Update", new[]
        {
            "Update .NET SDK version in scripts and enhance AllocationBuddyRPGView with item view mode"
        }),
        new("4.14.2", "2026-01-09", "Release Update", new[]
        {
            "Enhance status message handling and update UI colors in OrderLog"
        }),
        new("4.14.1", "2026-01-09", "Release Update", new[]
        {
            "Enhance status message handling and update UI colors in OrderLog"
        }),
        new("4.14.0", "2026-01-09", "Release Update", new[]
        {
            "Update .NET SDK version and enhance Order Log widget functionality"
        }),
        new("4.13.0", "2026-01-09", "Release Update", new[]
        {
            "Refactor database integration from LiteDB to SQLite- Updated AllocationBuddyParser to reflect SQLite usage in comments.- Replaced LiteDB package reference with Microsoft.Data.Sqlite in project file.- Modified DictionarySyncService to batch upsert operations for items and stores.- Changed DictionaryManagementViewModel to log and manage data using SQLite.- Updated ExpirationItemDialogViewModel to retrieve stores from SQLite.- Implemented SqliteDbContext for managing SQLite connections and database initialization.- Created SqliteUnitOfWork for handling unit of work pattern with SQLite.- Developed SqliteRepository for CRUD operations with JSON serialization for entities.- Added migration tool to convert existing LiteDB databases to SQLite format."
        }),
        new("4.12.0", "2026-01-09", "Release Update", new[]
        {
            "Refactor export functionality to improve user experience and error handling",
            "refactored widget update pipeline"
        }),
        new("4.11.0", "2026-01-09", "Release Update", new[]
        {
            "Refactor widget handling and improve UI consistency with new message dialog"
        }),
        new("4.10.1", "2026-01-09", "Release Update", new[]
        {
            "Update to .NET 9.0 and enhance widget management- Changed target framework from net8.0 to net9.0 in project files.- Updated Microsoft.Extensions packages to version 9.0.0.- Introduced WidgetProcessService for managing the widget as a separate process, improving isolation and stability.- Updated release script to automatically add changelog entries.- Enhanced About window to display current version and changelog status.- Refactored various services to use the new WidgetProcessService."
        }),
        new("4.10.0", "2026-01-09", "Release Update", new[]
        {
            "Update to .NET 9.0 and enhance widget management"
        }),
        new("4.9.0", "2026-01-09", "Cleanup Release", new[]
        {
            "🎨 Added missing AccentBrush to theme files for consistency",
            "🧹 Removed FriendshipDungeonMG project",
            "🧹 Removed WPF DungeonCrawler easter egg",
            "🔧 Fixed About window changelog display",
            "📝 Release script now auto-updates changelog"
        }),
        new("4.6.13", "2026-01-08", "Release Update", new[]
        {
            "workflow fixes"
        }),
        new("4.6.12", "2026-01-08", "Release Update", new[]
        {
            "Adjusted defualt month in ExpireWise AddItem Dialog"
        }),
        new("4.6.11", "2026-01-08", "Release Update", new[]
        {
            "add item update"
        }),
        new("4.6.7", "2026-01-07", "Release Update", new[]
        {
            "Add PowerShell script for local HTTP server to serve SOUP updates"
        }),
        new("4.6.5", "2026-01-07", "Release Update", new[]
        {
            "feat: Update version to 4.6.5 and enhance release script with changelog prompts"
        }),
        new("4.6.4", "2026-01-07", "Release Infrastructure", new[]
        {
            "🚀 Added GitHub Actions release workflow",
            "🔄 Added automatic update checking via GitHub Releases",
            "🔒 Added security scanning for sensitive data",
            "📦 Enhanced release script with changelog prompts",
            "🖼️ Fixed tray icon not showing in published builds"
        }),



        new("4.2.0", "2025-12-05", "UI Polish Update", new[]
        {
            "🎨 Enhanced gradient backgrounds in both light and dark themes",
            "🌈 Sidebar now uses smooth gradient transitions",
            "🖼️ Module splash screens have more vibrant color gradients",
            "🪟 Title bar now matches sidebar color for seamless look",
            "📍 Moved About button into Settings window",
            "📜 Removed scroll from launcher sidebar navigation",
            "🔤 S.A.P title now follows theme colors",
            "📦 Installer now offers Full vs Portable installation options",
            "💾 Full install: Framework-dependent (~15 MB, requires .NET 8)",
            "📁 Portable install: Self-contained (~75 MB, runs anywhere)"
        }),

        new("4.1.0", "2025-12-05", "Quality of Life Update", new[]
        {
            "✨ Added About dialog with version and module information (F1)",
            "✨ Added window position and size persistence",
            "✨ Added keyboard shortcuts panel in launcher sidebar",
            "⌨️ Ctrl+T to toggle theme",
            "⌨️ Escape to return to launcher from any module",
            "⌨️ Alt+H alternative home shortcut",
            "📝 Added comprehensive XML documentation to all code",
            "🗂️ Reorganized project structure to modern standards",
            "🧹 Cleaned up temporary and backup files",
            "📋 Added README and improved .gitignore"
        }),

        new("4.0.0", "2025-12-01", "Fourth Major Release", new[]
        {
            "🚀 Fourth rendition of S.A.P (S.A.M. Add-on Pack)",
            "📦 ExpireWise - Expiration date tracking and management",
            "📊 AllocationBuddy - Store allocation management with RPG mode",
            "📋 EssentialsBuddy - Essential items inventory tracking",
            "🏷️ SwiftLabel - Quick label generation",
            "🌙 Dark and Light theme support with persistence",
            "💾 SQLite database for data persistence",
            "📤 Excel import/export support",
            "🗃️ Archive system for session management",
            "🔧 Modular installer with component selection"
        })
    };

    /// <summary>
    /// Gets the latest changelog entry.
    /// Returns the entry matching the current Version, or the first entry if no match.
    /// </summary>
    public static ChangelogEntry LatestChanges =>
        Changelog.FirstOrDefault(c => c.Version == Version) ?? Changelog[0];
}

/// <summary>
/// Represents a single version's changelog entry.
/// </summary>
/// <param name="Version">The version number (e.g., "1.1.0").</param>
/// <param name="Date">The release date in ISO format.</param>
/// <param name="Title">The release title/name.</param>
/// <param name="Changes">List of changes in this version.</param>
public record ChangelogEntry(
    string Version,
    string Date,
    string Title,
    IReadOnlyList<string> Changes
);
