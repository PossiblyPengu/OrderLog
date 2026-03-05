using System;
using System.Windows;
using System.Windows.Input;
using OrderLog.Features.ViewModels;
using OrderLog.Helpers;
using OrderLog.Services;

namespace OrderLog.Windows;

public partial class OrderLogSettingsWindow : AnimatedWindow
{
    protected override double CloseAnimDurationMs => 160.0;
    protected override double CloseAnimScale => 0.95;

    public OrderLogSettingsWindow(OrderLogViewModel viewModel)
    {
        InitializeComponent();
        SettingsView.DataContext = viewModel;
        ApplyTheme();
        ThemeService.Instance.ThemeChanged += OnThemeChanged;
        Unloaded += (s, e) => ThemeService.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, bool isDarkMode)
    {
        Dispatcher.Invoke(ApplyTheme);
    }

    private void ApplyTheme()
    {
        try
        {
            var isDarkMode = OrderLog.Services.ThemeService.Instance.IsDarkMode;
            var themeFile = isDarkMode
                ? "pack://application:,,,/OrderLog;component/Themes/Marathon/MarathonTheme.xaml"
                : "pack://application:,,,/OrderLog;component/Themes/Marathon/MarathonLightTheme.xaml";
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(themeFile)
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to apply theme to settings window");
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            return;
        DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

}
