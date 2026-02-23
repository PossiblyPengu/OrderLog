using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace SOUP.Infrastructure.Services;

/// <summary>
/// Event args for settings changed notifications.
/// </summary>
public class SettingsChangedEventArgs : EventArgs
{
    public string AppName { get; }
    public SettingsChangedEventArgs(string appName) => AppName = appName;
}

/// <summary>
/// Service for loading and saving application settings
/// </summary>
public sealed class SettingsService
{
    private readonly string _settingsDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Event raised when settings are saved for any app.
    /// </summary>
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    public SettingsService()
    {
        _settingsDirectory = Path.Combine(Core.AppPaths.LocalAppData, "Settings");

        // Ensure the settings directory exists
        Directory.CreateDirectory(_settingsDirectory);

        _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Load settings from file. Returns default settings if file doesn't exist.
    /// </summary>
    public async Task<T> LoadSettingsAsync<T>(string appName) where T : new()
    {
        var filePath = GetSettingsFilePath(appName);

        if (!File.Exists(filePath))
        {
            return new T();
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions) ?? new T();
        }
        catch (Exception ex)
        {
            // Log the exception instead of swallowing it
            Serilog.Log.Warning(ex, "Error loading settings for {AppName}", appName);
            return new T();
        }
    }

    /// <summary>
    /// Save settings to file and notify subscribers.
    /// </summary>
    public async Task SaveSettingsAsync<T>(string appName, T settings)
    {
        var filePath = GetSettingsFilePath(appName);

        try
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);
            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);

            // Notify subscribers that settings have changed
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(appName));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save settings for {appName}", ex);
        }
    }

    /// <summary>
    /// Get the full file path for settings file
    /// </summary>
    private string GetSettingsFilePath(string appName)
    {
        // Sanitize the app name to prevent path injection
        var sanitized = string.Concat(appName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "default";
        }
        return Path.Combine(_settingsDirectory, $"{sanitized}.settings.json");
    }

    /// <summary>
    /// Delete settings file for an app (reset to defaults)
    /// </summary>
    public void ResetSettings(string appName)
    {
        var filePath = GetSettingsFilePath(appName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
