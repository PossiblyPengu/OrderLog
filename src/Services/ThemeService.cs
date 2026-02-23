using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SOUP.Services;

/// <summary>
/// Service for managing application theme (light/dark mode) in WPF.
/// </summary>
/// <remarks>
/// <para>
/// This service provides centralized theme management for the application,
/// supporting dynamic switching between light and dark themes at runtime.
/// </para>
/// <para>
/// Theme preference is persisted to disk in the user's AppData folder and
/// automatically restored on application startup.
/// </para>
/// </remarks>
public partial class ThemeService : ObservableObject
{
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
    /// Occurs when the theme changes between light and dark mode.
    /// </summary>
    public event EventHandler<bool>? ThemeChanged;

    public ThemeService()
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
            Serilog.Log.Debug("ApplyTheme: Starting. IsDarkMode={IsDarkMode}", IsDarkMode);

            // Clear all merged dictionaries first
            app.Resources.MergedDictionaries.Clear();
            Serilog.Log.Debug("ApplyTheme: Cleared merged dictionaries");

            // Load ModernStyles.xaml first (base styles), then color theme (colors override)
            var modernStyles = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Themes/ModernStyles.xaml", UriKind.Absolute)
            };
            app.Resources.MergedDictionaries.Add(modernStyles);
            Serilog.Log.Debug("ApplyTheme: Added ModernStyles.xaml");

            var colorTheme = new ResourceDictionary
            {
                Source = new Uri(IsDarkMode
                    ? "pack://application:,,,/Themes/DarkTheme.xaml"
                    : "pack://application:,,,/Themes/LightTheme.xaml", UriKind.Absolute)
            };
            app.Resources.MergedDictionaries.Add(colorTheme);
            Serilog.Log.Debug("ApplyTheme: Added {ThemeName}.xaml", IsDarkMode ? "DarkTheme" : "LightTheme");

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
                    ApplyTheme();
                }
            }
        }
        catch (Exception ex)
        {
            // If loading fails, use default dark theme and log the error
            Serilog.Log.Warning(ex, "Failed to load theme settings, using default dark theme");
            IsDarkMode = true;
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
                IsDarkMode = IsDarkMode
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

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
        /// <summary>
        /// Gets or sets whether dark mode is enabled.
        /// </summary>
        public bool IsDarkMode { get; set; }
    }
}
