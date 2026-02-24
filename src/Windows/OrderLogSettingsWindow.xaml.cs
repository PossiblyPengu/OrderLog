using System;
using System.Windows;
using System.Windows.Input;
using SOUP.Features.OrderLog.ViewModels;
using SOUP.Services;

namespace SOUP.Windows;

public partial class OrderLogSettingsWindow : Window
{
    public OrderLogSettingsWindow(OrderLogViewModel viewModel)
    {
        InitializeComponent();
        SettingsView.DataContext = viewModel;
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        try
        {
            var isDarkMode = ThemeService.Instance.IsDarkMode;
            var themePath = isDarkMode
                ? "pack://application:,,,/OrderLog;component/Themes/DarkTheme.xaml"
                : "pack://application:,,,/OrderLog;component/Themes/LightTheme.xaml";

            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/OrderLog;component/Themes/ModernStyles.xaml")
            });
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(themePath) });
            Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/OrderLog;component/Features/OrderLog/Themes/OrderLogWidgetTheme.xaml")
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
