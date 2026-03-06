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
            var svc = OrderLog.Services.ThemeService.Instance;
            var themeFile = svc.IsDarkMode
                ? "pack://application:,,,/OrderLog;component/Themes/Marathon/MarathonTheme.xaml"
                : "pack://application:,,,/OrderLog;component/Themes/Marathon/MarathonLightTheme.xaml";
            var shapeFile = svc.ShapeVariant switch
            {
                OrderLog.Services.ShapeVariant.Rounded => "pack://application:,,,/OrderLog;component/Themes/Marathon/Shapes/RoundedShape.xaml",
                OrderLog.Services.ShapeVariant.Sharp   => "pack://application:,,,/OrderLog;component/Themes/Marathon/Shapes/SharpShape.xaml",
                _                                      => "pack://application:,,,/OrderLog;component/Themes/Marathon/Shapes/AngularShape.xaml",
            };
            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(themeFile) });
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(shapeFile) });
            var colourUri = OrderLog.Services.ThemeService.GetColourPaletteUri(svc.ColourTheme, svc.IsDarkMode);
            if (colourUri != null)
                Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(colourUri) });
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
