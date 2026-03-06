using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OrderLog.Services;

/// <summary>
/// Named colour palette applied on top of the base dark/light theme.
/// </summary>
public enum ColourTheme
{
    /// <summary>Neon green — the default Marathon accent.</summary>
    Neon = 0,
    /// <summary>Electric blue — WinUI-inspired accent.</summary>
    Blue = 1,
    /// <summary>Solar amber — warm scanner yellow.</summary>
    Amber = 2,
    /// <summary>Corrosive red — critical-alert accent.</summary>
    Red = 3,
    /// <summary>Aeon purple — violet plasma accent.</summary>
    Purple = 4,
    /// <summary>Cryo cyan — ice scanner accent.</summary>
    Cyan = 5,
}

/// <summary>
/// Controls which corner-radius profile is applied on top of the colour theme.
/// </summary>
public enum ShapeVariant
{
    /// <summary>Angular, small radii — the default Marathon aesthetic.</summary>
    Angular = 0,
    /// <summary>Generous radii — Fluent / WinUI-inspired softness.</summary>
    Rounded = 1,
    /// <summary>Zero radii — hard-cornered brutalist look.</summary>
    Sharp = 2,
}

/// <summary>
/// Service for managing application theme (color mode + shape variant) in WPF.
/// </summary>
/// <remarks>
/// <para>
/// This service provides centralized theme management for the application,
/// supporting dynamic switching between light and dark colour modes and
/// three shape profiles (Angular / Rounded / Sharp) at runtime.
/// </para>
/// <para>
/// Theme preference is persisted to disk in the user's AppData folder and
/// automatically restored on application startup.
/// </para>
/// </remarks>
public partial class ThemeService : ObservableObject
{
    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
    /// <summary>
    /// Gets the singleton instance of the theme service.
    /// </summary>
    private static readonly Lazy<ThemeService> _instance = new(() => new ThemeService(), isThreadSafe: true);

    /// <summary>
    /// Gets the singleton instance of the theme service.
    /// </summary>
    public static ThemeService Instance => _instance.Value;

    private readonly string _settingsPath;
    private const string SettingsFileName = "theme-settings.json";

    /// <summary>
    /// Gets or sets whether dark mode is currently active.
    /// </summary>
    [ObservableProperty]
    private bool _isDarkMode = true;

    /// <summary>
    /// Gets or sets the active shape profile.
    /// </summary>
    [ObservableProperty]
    private ShapeVariant _shapeVariant = ShapeVariant.Angular;

    /// <summary>
    /// Gets or sets the active colour palette.
    /// </summary>
    [ObservableProperty]
    private ColourTheme _colourTheme = ColourTheme.Neon;

    /// <summary>
    /// Occurs when the theme changes between light and dark mode.
    /// </summary>
    public event EventHandler<bool>? ThemeChanged;

    private ThemeService()
    {
        _settingsPath = Path.Combine(Core.AppPaths.AppData, SettingsFileName);

        LoadTheme();
    }

    /// <summary>
    /// Toggles between light and dark themes.
    /// </summary>
    public void ToggleTheme()
    {
        IsDarkMode = !IsDarkMode;
        ApplyTheme();
        SaveTheme();
    }

    /// <summary>
    /// Sets the theme explicitly to light or dark.
    /// </summary>
    /// <param name="isDark"><c>true</c> for dark theme; <c>false</c> for light theme.</param>
    public void SetTheme(bool isDark)
    {
        if (IsDarkMode != isDark)
        {
            IsDarkMode = isDark;
            ApplyTheme();
            SaveTheme();
        }
    }

    /// <summary>
    /// Changes the active shape profile and immediately applies it.
    /// </summary>
    public void SetShapeVariant(ShapeVariant variant)
    {
        if (ShapeVariant != variant)
        {
            ShapeVariant = variant;
            ApplyTheme();
            SaveTheme();
        }
    }

    /// <summary>
    /// Changes the active colour palette and immediately applies it.
    /// </summary>
    public void SetColourTheme(ColourTheme colour)
    {
        if (ColourTheme != colour)
        {
            ColourTheme = colour;
            ApplyTheme();
            SaveTheme();
        }
    }

    /// <summary>
    /// Returns the pack URI for the colour palette overlay, or <c>null</c> when the default Neon palette is active.
    /// </summary>
    public static string? GetColourPaletteUri(ColourTheme colour, bool isDarkMode)
    {
        var name = colour switch
        {
            ColourTheme.Blue   => "Blue",
            ColourTheme.Amber  => "Amber",
            ColourTheme.Red    => "Red",
            ColourTheme.Purple => "Purple",
            ColourTheme.Cyan   => "Cyan",
            _                  => null,
        };

        if (name == null) return null;
        return $"pack://application:,,,/Themes/Marathon/Colours/{name}{(isDarkMode ? "Dark" : "Light")}.xaml";
    }

    /// <summary>
    /// Initializes and applies the theme on application startup.
    /// </summary>
    public void Initialize()
    {
        ApplyTheme();
    }

    /// <summary>
    /// Applies the current theme by swapping ResourceDictionaries in the application.
    /// </summary>
    private void ApplyTheme()
    {
        var app = Application.Current;
        if (app == null)
        {
            Serilog.Log.Debug("ApplyTheme: Application.Current is null");
            return;
        }

        try
        {
            Serilog.Log.Debug("ApplyTheme: Starting. IsDarkMode={IsDarkMode} ShapeVariant={ShapeVariant}", IsDarkMode, ShapeVariant);

            // Clear all merged dictionaries first
            app.Resources.MergedDictionaries.Clear();
            Serilog.Log.Debug("ApplyTheme: Cleared merged dictionaries");

            // 1. Load Marathon 2026 design system (dark or light colour variant)
            var themeFile = IsDarkMode
                ? "pack://application:,,,/Themes/Marathon/MarathonTheme.xaml"
                : "pack://application:,,,/Themes/Marathon/MarathonLightTheme.xaml";

            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(themeFile, UriKind.Absolute)
            });
            Serilog.Log.Debug("ApplyTheme: Added colour theme {ThemeFile}", themeFile);

            // 2. Overlay shape profile (overrides all CornerRadius tokens)
            var shapeFile = ShapeVariant switch
            {
                ShapeVariant.Rounded => "pack://application:,,,/Themes/Marathon/Shapes/RoundedShape.xaml",
                ShapeVariant.Sharp   => "pack://application:,,,/Themes/Marathon/Shapes/SharpShape.xaml",
                _                    => "pack://application:,,,/Themes/Marathon/Shapes/AngularShape.xaml",
            };

            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(shapeFile, UriKind.Absolute)
            });
            Serilog.Log.Debug("ApplyTheme: Added shape profile {ShapeFile}", shapeFile);

            // 3. Overlay colour palette (overrides accent tokens; omitted for default Neon)
            var colourUri = GetColourPaletteUri(ColourTheme, IsDarkMode);
            if (colourUri != null)
            {
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(colourUri, UriKind.Absolute)
                });
                Serilog.Log.Debug("ApplyTheme: Added colour palette {ColourUri}", colourUri);
            }

            // Raise event for any listeners
            ThemeChanged?.Invoke(this, IsDarkMode);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to apply theme");
        }
    }

    /// <summary>
    /// Loads the theme preference from disk.
    /// </summary>
    private void LoadTheme()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<ThemeSettings>(json);

                if (settings != null)
                {
                    IsDarkMode = settings.IsDarkMode;
                    ShapeVariant = settings.ShapeVariant;
                    ColourTheme = settings.ColourTheme;
                    ApplyTheme();
                }
            }
        }
        catch (Exception ex)
        {
            // If loading fails, use default dark theme and log the error
            Serilog.Log.Warning(ex, "Failed to load theme settings, using default dark theme");
            IsDarkMode = true;
            ShapeVariant = ShapeVariant.Angular;
        }
    }

    /// <summary>
    /// Saves the theme preference to disk.
    /// </summary>
    private void SaveTheme()
    {
        try
        {
            var settings = new ThemeSettings
            {
                IsDarkMode = IsDarkMode,
                ShapeVariant = ShapeVariant,
                ColourTheme = ColourTheme,
            };

            var json = JsonSerializer.Serialize(settings, s_jsonOptions);

            File.WriteAllText(_settingsPath, json);
        }
        catch (Exception ex)
        {
            // Log the error but don't crash the application
            Serilog.Log.Warning(ex, "Failed to save theme settings");
        }
    }

    /// <summary>
    /// Internal class for serializing theme settings to JSON.
    /// </summary>
    private class ThemeSettings
    {
        /// <summary>Gets or sets whether dark mode is enabled.</summary>
        public bool IsDarkMode { get; set; }

        /// <summary>Gets or sets the active shape profile.</summary>
        public ShapeVariant ShapeVariant { get; set; } = ShapeVariant.Angular;

        /// <summary>Gets or sets the active colour palette.</summary>
        public ColourTheme ColourTheme { get; set; } = ColourTheme.Neon;
    }
}
