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
    /// <summary>Zero radii — hard-cornered look.</summary>
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

    // Persistent ResourceDictionary slots — Source updated in-place; never Clear() app resources.
    private readonly ResourceDictionary _appBaseDict   = new();
    private readonly ResourceDictionary _appColourDict = new();

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
            _                  => "Neon",
        };

        return $"pack://application:,,,/SSCommandCentre;component/Themes/Marathon/Colours/{name}{(isDarkMode ? "Dark" : "Light")}.xaml";
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

            var md = app.Resources.MergedDictionaries;

            // 1. Base colour theme (dark / light)
            var themeFile = IsDarkMode
                ? "pack://application:,,,/SSCommandCentre;component/Themes/Marathon/MarathonTheme.xaml"
                : "pack://application:,,,/SSCommandCentre;component/Themes/Marathon/MarathonLightTheme.xaml";
            SwapDictSource(md, _appBaseDict, new Uri(themeFile, UriKind.Absolute));
            Serilog.Log.Debug("ApplyTheme: Set base theme {ThemeFile}", themeFile);

            // 2. Shape profile — written directly into Application.Resources so the values
            //    shadow every MergedDictionary entry (including Variables.xaml inside the base
            //    theme) and DynamicResource bindings are guaranteed to refresh immediately.
            ApplyShapeTokens(app);
            Serilog.Log.Debug("ApplyTheme: Applied shape tokens for {ShapeVariant}", ShapeVariant);

            // 3. Colour palette overlay
            var colourUri = GetColourPaletteUri(ColourTheme, IsDarkMode)!;
            SwapDictSource(md, _appColourDict, new Uri(colourUri, UriKind.Absolute));
            Serilog.Log.Debug("ApplyTheme: Set colour {ColourUri}", colourUri);

            // Raise event for any listeners
            ThemeChanged?.Invoke(this, IsDarkMode);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to apply theme");
        }
    }

    /// <summary>
    /// Writes the corner-radius tokens for the current <see cref="ShapeVariant"/> directly into
    /// <see cref="Application.Resources"/>.  Direct resource keys always take precedence over
    /// every <c>MergedDictionaries</c> entry (including the nested <c>Variables.xaml</c> inside
    /// the base theme), so every <c>{DynamicResource RadiusSm}</c> etc. binding in the visual
    /// tree refreshes immediately when these are set or updated.
    /// </summary>
    private void ApplyShapeTokens(Application app)
    {
        CornerRadius xs, sm, md, lg,
                     control, card, nav, badge, overlay,
                     window, titleBar, sidebar,
                     icon, appLogo, splash;

        switch (ShapeVariant)
        {
            case ShapeVariant.Rounded:
                xs = new(8);  sm = new(12); md = new(16); lg = new(24);
                control = new(10); card = new(16); nav = new(10); badge = new(20); overlay = new(20);
                window  = new(16); titleBar = new(16, 16, 0, 0); sidebar = new(0, 0, 0, 16);
                icon = new(12); appLogo = new(20); splash = new(32);
                break;

            case ShapeVariant.Sharp:
                xs = sm = md = lg = default;
                control = card = nav = badge = overlay = default;
                window  = titleBar = sidebar = default;
                icon = appLogo = splash = default;
                break;

            default: // Angular
                xs = new(2);  sm = new(3);  md = new(4);  lg = new(6);
                control = new(4);  card = new(4);  nav = new(3);  badge = new(3);  overlay = new(6);
                window  = new(6);  titleBar = new(6, 6, 0, 0);   sidebar = new(0, 0, 0, 6);
                icon = new(4);  appLogo = new(4);  splash = new(8);
                break;
        }

        // Asymmetric structural radii derived from the base scale
        CornerRadius cardTop, stripeXs, stripeSm, stripeMd, stripeLg, overlayTitle;
        switch (ShapeVariant)
        {
            case ShapeVariant.Rounded:
                cardTop      = new(16, 16, 0, 0);
                overlayTitle = new(20, 20, 0, 0);
                stripeXs = new(8,  0,  0, 8);
                stripeSm = new(12, 0,  0, 12);
                stripeMd = new(16, 0,  0, 16);
                stripeLg = new(24, 0,  0, 24);
                break;
            case ShapeVariant.Sharp:
                cardTop = stripeXs = stripeSm = stripeMd = stripeLg = overlayTitle = default;
                break;
            default: // Angular
                cardTop      = new(4, 4, 0, 0);
                overlayTitle = new(6, 6, 0, 0);
                stripeXs = new(2, 0, 0, 2);
                stripeSm = new(3, 0, 0, 3);
                stripeMd = new(4, 0, 0, 4);
                stripeLg = new(6, 0, 0, 6);
                break;
        }

        var r = app.Resources;
        // Semantic scale
        r["RadiusXs"] = xs;
        r["RadiusSm"] = sm;
        r["RadiusMd"] = md;
        r["RadiusLg"] = lg;
        // Compat aliases
        r["XSmallCornerRadius"] = xs;
        r["SmallCornerRadius"]  = sm;
        r["MediumCornerRadius"] = md;
        r["LargeCornerRadius"]  = lg;
        // Component-specific
        r["ControlCornerRadius"] = control;
        r["CardCornerRadius"]    = card;
        r["NavItemCornerRadius"] = nav;
        r["BadgeCornerRadius"]   = badge;
        r["OverlayCornerRadius"] = overlay;
        // Window chrome
        r["WindowCornerRadius"]   = window;
        r["TitleBarCornerRadius"] = titleBar;
        r["SidebarCornerRadius"]  = sidebar;
        // Decorative
        r["IconCornerRadius"]       = icon;
        r["AppLogoCornerRadius"]    = appLogo;
        r["SplashIconCornerRadius"] = splash;
        // Asymmetric structural
        r["CardTopCornerRadius"]      = cardTop;
        r["CardStripeXsCornerRadius"] = stripeXs;
        r["CardStripeSmCornerRadius"] = stripeSm;
        r["CardStripeMdCornerRadius"] = stripeMd;
        r["CardStripeLgCornerRadius"] = stripeLg;
        r["OverlayTitleCornerRadius"] = overlayTitle;
    }

    /// <summary>
    /// Removes <paramref name="slot"/> from <paramref name="md"/> (if present), sets its
    /// <see cref="ResourceDictionary.Source"/> to <paramref name="newUri"/>, then re-inserts
    /// it at the same index (or appends it when not yet in the collection).
    /// </summary>
    private static void SwapDictSource(
        IList<ResourceDictionary> md,
        ResourceDictionary slot,
        Uri newUri)
    {
        var idx = md.IndexOf(slot);
        if (idx >= 0)
            md.RemoveAt(idx);

        slot.Source = newUri;

        if (idx >= 0)
            md.Insert(idx, slot);
        else
            md.Add(slot);
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
                    // Do not call ApplyTheme() here — app.InitializeComponent() has not run yet
                    // and would replace Application.Resources, orphaning any added dicts.
                    // ThemeService.Initialize() (called from OnStartup) handles the real init.
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
