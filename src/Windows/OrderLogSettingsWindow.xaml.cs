using System.Windows;
using System.Windows.Input;
using OrderLog.Features.ViewModels;
using OrderLog.Helpers;

namespace OrderLog.Windows;

public partial class OrderLogSettingsWindow : AnimatedWindow
{
    protected override double CloseAnimDurationMs => 160.0;
    protected override double CloseAnimScale => 0.95;

    public OrderLogSettingsWindow(OrderLogViewModel viewModel)
    {
        InitializeComponent();
        SettingsView.DataContext = viewModel;
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
