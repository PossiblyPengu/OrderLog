using System.Windows;
using System.Windows.Media;
using SOUP.Services;

namespace SOUP.Windows;

/// <summary>
/// Dialog type determines the icon and color scheme
/// </summary>
public enum DialogType
{
    Information,
    Warning,
    Error,
    Question
}

/// <summary>
/// Button configuration for the dialog
/// </summary>
public enum DialogButtons
{
    OK,
    OKCancel,
    YesNo
}

/// <summary>
/// Themed message dialog that matches the application's dark/light theme.
/// Use this instead of MessageBox for a consistent look.
/// </summary>
public partial class MessageDialog : Window
{
    private MessageDialog()
    {
        InitializeComponent();
        ApplyTheme();
        ThemeService.Instance.ThemeChanged += OnThemeChanged;
        Unloaded += (s, e) => ThemeService.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, bool isDarkMode)
    {
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        var themePath = ThemeService.Instance.IsDarkMode
            ? "pack://application:,,,/SOUP;component/Themes/DarkTheme.xaml"
            : "pack://application:,,,/SOUP;component/Themes/LightTheme.xaml";

        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(themePath) });
    }

    private void Configure(string message, string title, DialogType type, DialogButtons buttons)
    {
        TitleText.Text = title;
        MessageText.Text = message;
        Title = title;

        // Configure icon and color based on type
        var (icon, brush) = type switch
        {
            DialogType.Information => ("ℹ", FindResource("AccentBrush") as Brush),
            DialogType.Warning => ("⚠", FindResource("WarningBrush") as Brush),
            DialogType.Error => ("✕", FindResource("DangerBrush") as Brush),
            DialogType.Question => ("?", FindResource("AccentBrush") as Brush),
            _ => ("ℹ", FindResource("AccentBrush") as Brush)
        };

        IconText.Text = icon;
        IconBorder.Background = brush ?? Brushes.DodgerBlue;

        // Configure buttons
        switch (buttons)
        {
            case DialogButtons.OK:
                PrimaryButton.Content = "OK";
                SecondaryButton.Visibility = Visibility.Collapsed;
                break;

            case DialogButtons.OKCancel:
                PrimaryButton.Content = "OK";
                SecondaryButton.Content = "Cancel";
                SecondaryButton.Visibility = Visibility.Visible;
                break;

            case DialogButtons.YesNo:
                PrimaryButton.Content = "Yes";
                SecondaryButton.Content = "No";
                SecondaryButton.Visibility = Visibility.Visible;
                break;
        }

        // Use accent color for primary button, except for warnings/errors which use their color
        if (type == DialogType.Warning)
        {
            PrimaryButton.Background = FindResource("WarningBrush") as Brush ?? Brushes.Orange;
        }
        else if (type == DialogType.Error)
        {
            PrimaryButton.Background = FindResource("DangerBrush") as Brush ?? Brushes.Red;
        }
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Shows a themed message dialog.
    /// </summary>
    /// <param name="owner">The owner window</param>
    /// <param name="message">The message to display</param>
    /// <param name="title">The dialog title</param>
    /// <param name="type">The type of dialog (affects icon and colors)</param>
    /// <param name="buttons">The button configuration</param>
    /// <returns>True if primary button (OK/Yes) was clicked, false otherwise</returns>
    public static bool Show(Window owner, string message, string title, DialogType type = DialogType.Information, DialogButtons buttons = DialogButtons.OK)
    {
        var dialog = new MessageDialog
        {
            Owner = owner
        };
        dialog.Configure(message, title, type, buttons);
        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// Shows a themed message dialog without an owner (centers on screen).
    /// </summary>
    public static bool Show(string message, string title, DialogType type = DialogType.Information, DialogButtons buttons = DialogButtons.OK)
    {
        var dialog = new MessageDialog
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        dialog.Configure(message, title, type, buttons);
        return dialog.ShowDialog() == true;
    }

    /// <summary>
    /// Shows an information dialog with OK button.
    /// </summary>
    public static void ShowInfo(Window owner, string message, string title = "Information")
    {
        Show(owner, message, title, DialogType.Information, DialogButtons.OK);
    }

    /// <summary>
    /// Shows a warning dialog with OK button.
    /// </summary>
    public static void ShowWarning(Window owner, string message, string title = "Warning")
    {
        Show(owner, message, title, DialogType.Warning, DialogButtons.OK);
    }

    /// <summary>
    /// Shows an error dialog with OK button.
    /// </summary>
    public static void ShowError(Window owner, string message, string title = "Error")
    {
        Show(owner, message, title, DialogType.Error, DialogButtons.OK);
    }

    /// <summary>
    /// Shows a question dialog with Yes/No buttons.
    /// </summary>
    /// <returns>True if Yes was clicked</returns>
    public static bool AskQuestion(Window owner, string message, string title = "Confirm")
    {
        return Show(owner, message, title, DialogType.Question, DialogButtons.YesNo);
    }
}
