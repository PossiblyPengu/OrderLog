using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using SOUP.Windows;

namespace SOUP.Services;

/// <summary>
/// Service for displaying dialogs, message boxes, and file pickers in WPF.
/// </summary>
/// <remarks>
/// <para>
/// This service provides a consistent API for all dialog interactions in the application,
/// including message boxes, confirmation dialogs, and file open/save dialogs.
/// </para>
/// <para>
/// All methods are async-compatible for consistency with MVVM patterns, though WPF
/// dialogs are inherently synchronous.
/// </para>
/// </remarks>
public class DialogService
{
    // Windows DWM API for dark title bar
    [DllImport("dwmapi.dll", PreserveSig = true, SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>
    /// Apply dark mode to the window title bar
    /// </summary>
    private static void ApplyDarkTitleBar(Window window)
    {
        if (!ThemeService.Instance.IsDarkMode) return;

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        int useImmersiveDarkMode = 1;
        try
        {
            var hr = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));
            if (hr != 0)
            {
                Serilog.Log.Debug("DwmSetWindowAttribute failed (hr={Hr}) when applying dark title bar", hr);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Exception calling DwmSetWindowAttribute");
        }
    }

    /// <summary>
    /// Shows an informational message dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The message content.</param>
    /// <returns>A completed task.</returns>
    public Task ShowMessageAsync(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Shows an error message dialog with an error icon.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The error message content.</param>
    /// <returns>A completed task.</returns>
    public Task ShowErrorAsync(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Shows a Yes/No confirmation dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="message">The confirmation message.</param>
    /// <returns><c>true</c> if the user clicked Yes; otherwise, <c>false</c>.</returns>
    public Task<bool> ShowConfirmationAsync(string title, string message)
    {
        var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
        return Task.FromResult(result == MessageBoxResult.Yes);
    }

    /// <summary>
    /// Shows an open file dialog that allows selecting multiple files.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="filterName">The display name for the file type filter.</param>
    /// <param name="extensions">File extensions to filter (e.g., "xlsx", "csv").</param>
    /// <returns>Array of selected file paths, or <c>null</c> if cancelled.</returns>
    public Task<string[]?> ShowOpenFileDialogAsync(string title, string filterName, params string[] extensions)
    {
        var filterParts = new System.Collections.Generic.List<string>();
        if (extensions != null && extensions.Length > 0)
        {
            var extList = string.Join(";", extensions.Select(e => $"*.{e.TrimStart('*', '.')}"));
            filterParts.Add($"{filterName} ({extList})|{extList}");
        }
        filterParts.Add("All Files (*.*)|*.*");

        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = string.Join("|", filterParts),
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = true
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileNames : null);
    }

    /// <summary>
    /// Shows a save file dialog.
    /// </summary>
    /// <param name="title">The dialog title.</param>
    /// <param name="defaultFileName">The default file name.</param>
    /// <param name="filter">The file type filter string (e.g., "CSV Files|*.csv").</param>
    /// <returns>The selected file path, or <c>null</c> if cancelled.</returns>
    public Task<string?> ShowSaveFileDialogAsync(string title, string defaultFileName, string filter = "All Files|*.*")
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = filter,
            CheckPathExists = true
        };

        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    /// <summary>
    /// Shows a custom window as a modal dialog.
    /// </summary>
    /// <param name="dialog">The window to display.</param>
    /// <returns>The dialog result.</returns>
    public Task<bool?> ShowDialogAsync(Window dialog)
    {
        // Only set owner if MainWindow is visible (don't block other windows like widget)
        if (Application.Current?.MainWindow is { IsVisible: true } mainWindow)
        {
            dialog.Owner = mainWindow;
        }
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        return Task.FromResult(dialog.ShowDialog());
    }

    /// <summary>
    /// Shows a UserControl embedded in a dialog window and returns a result.
    /// </summary>
    /// <typeparam name="T">The type of result expected from the dialog.</typeparam>
    /// <param name="content">The UserControl to display as dialog content.</param>
    /// <returns>The result from the dialog, or default if closed without result.</returns>
    /// <remarks>
    /// The content UserControl should set its Tag property to an <see cref="Action{T}"/>
    /// and invoke it with the result when the dialog should close.
    /// </remarks>
    public Task<T> ShowContentDialogAsync<T>(System.Windows.Controls.UserControl content)
    {
        var tcs = new TaskCompletionSource<T>();

        var dialog = new Window
        {
            Content = content,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            ResizeMode = ResizeMode.NoResize,
            MinWidth = 450,
            MinHeight = 350,
            MaxWidth = 1000,
            MaxHeight = 900,
            ShowInTaskbar = false,
            Background = System.Windows.Media.Brushes.Transparent
        };

        // Set owner only when MainWindow is visible (don't block other windows like widget)
        if (Application.Current?.MainWindow is { IsVisible: true } mainWindow)
        {
            dialog.Owner = mainWindow;
        }

        // Store the result handler
        content.Tag = new Action<T>(result =>
        {
            tcs.TrySetResult(result);
            dialog.Close();
        });

        dialog.ShowDialog();

        // If not already set, return default
        if (!tcs.Task.IsCompleted)
        {
            tcs.TrySetResult(default(T)!);
        }

        return tcs.Task;
    }

    /// <summary>
    /// Shows a themed export success dialog with option to open the file location.
    /// </summary>
    /// <param name="fileName">The name of the exported file.</param>
    /// <param name="filePath">The full path to the exported file.</param>
    /// <param name="itemCount">The number of items exported.</param>
    /// <returns>True if user chose to open the file location.</returns>
    public bool ShowExportSuccessDialog(string fileName, string filePath, int itemCount)
    {
        var message = $"Successfully exported {itemCount} item(s) to:\n\n{fileName}\n\nWould you like to open the folder?";
        var openFolder = MessageDialog.Show(message, "Export Complete", DialogType.Information, DialogButtons.YesNo);

        if (openFolder)
        {
            try
            {
                // Open folder and select the file
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            catch
            {
                // Fallback: just open the folder
                var folder = System.IO.Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(folder))
                {
                    Process.Start("explorer.exe", folder);
                }
            }
        }

        return openFolder;
    }

    /// <summary>
    /// Shows a themed export error dialog.
    /// </summary>
    /// <param name="errorMessage">The error message to display.</param>
    public void ShowExportErrorDialog(string errorMessage)
    {
        MessageDialog.ShowWarning(null!, $"Export failed:\n\n{errorMessage}", "Export Error");
    }

    /// <summary>
    /// Shows a themed import success dialog.
    /// </summary>
    /// <param name="fileName">The name of the imported file.</param>
    /// <param name="itemCount">The number of items imported.</param>
    public void ShowImportSuccessDialog(string fileName, int itemCount)
    {
        var message = $"Successfully imported {itemCount} item(s) from:\n\n{fileName}";
        MessageDialog.Show(message, "Import Complete", DialogType.Information, DialogButtons.OK);
    }

    /// <summary>
    /// Shows a themed import error dialog.
    /// </summary>
    /// <param name="errorMessage">The error message to display.</param>
    public void ShowImportErrorDialog(string errorMessage)
    {
        MessageDialog.ShowWarning(null!, $"Import failed:\n\n{errorMessage}", "Import Error");
    }

    /// <summary>
    /// Shows a generic error dialog with custom message.
    /// </summary>
    /// <param name="message">The error message to display.</param>
    /// <param name="title">The dialog title.</param>
    public void ShowError(string message, string title = "Error")
    {
        MessageDialog.ShowError(null!, message, title);
    }

    /// <summary>
    /// Shows a generic warning dialog with custom message.
    /// </summary>
    /// <param name="message">The warning message to display.</param>
    /// <param name="title">The dialog title.</param>
    public void ShowWarning(string message, string title = "Warning")
    {
        MessageDialog.ShowWarning(null!, message, title);
    }

    /// <summary>
    /// Shows a generic info dialog with custom message.
    /// </summary>
    /// <param name="message">The info message to display.</param>
    /// <param name="title">The dialog title.</param>
    public void ShowInfo(string message, string title = "Information")
    {
        MessageDialog.ShowInfo(null!, message, title);
    }
}
