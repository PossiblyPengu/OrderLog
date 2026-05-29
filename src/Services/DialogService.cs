using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using OrderLog.Windows;

namespace OrderLog.Services;

/// <summary>
/// Service for displaying dialogs, message boxes, and file pickers in WPF.
/// </summary>
public class DialogService
{
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
        MessageDialog.Show($"Export failed:\n\n{errorMessage}", "Export Error", DialogType.Warning);
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
        MessageDialog.Show($"Import failed:\n\n{errorMessage}", "Import Error", DialogType.Warning);
    }

}
