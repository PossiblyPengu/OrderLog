using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Serilog;
using OrderLog.Features.ViewModels;
using OrderLog.Services;

namespace OrderLog.Features.Views;

public partial class OrderLogSettingsView : UserControl
{
    public OrderLogSettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is OrderLogViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;

        if (e.NewValue is OrderLogViewModel newVm)
        {
            newVm.PropertyChanged += OnViewModelPropertyChanged;
            UpdateShapeButtonHighlights(newVm.ShapeVariant);
            UpdateColourSwatchHighlights(newVm.ColourTheme);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrderLogViewModel.ShapeVariant) && sender is OrderLogViewModel vm)
            Dispatcher.Invoke(() => UpdateShapeButtonHighlights(vm.ShapeVariant));
        else if (e.PropertyName == nameof(OrderLogViewModel.ColourTheme) && sender is OrderLogViewModel vm2)
            Dispatcher.Invoke(() => UpdateColourSwatchHighlights(vm2.ColourTheme));
    }

    private void UpdateShapeButtonHighlights(ShapeVariant active)
    {
        var accentBrush   = TryFindResource("AccentBrush")  as Brush ?? Brushes.DodgerBlue;
        var borderBrush   = TryFindResource("BorderBrush")  as Brush ?? Brushes.Gray;

        ShapeAngularBtn.BorderBrush = active == ShapeVariant.Angular ? accentBrush : borderBrush;
        ShapeRoundedBtn.BorderBrush = active == ShapeVariant.Rounded ? accentBrush : borderBrush;
        ShapeSharpBtn.BorderBrush   = active == ShapeVariant.Sharp   ? accentBrush : borderBrush;
    }

    private void UpdateColourSwatchHighlights(ColourTheme active)
    {
        var accentBrush = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
        var borderBrush = TryFindResource("BorderBrush") as Brush ?? Brushes.Gray;

        ColourNeonBtn.BorderBrush   = active == ColourTheme.Neon   ? accentBrush : borderBrush;
        ColourBlueBtn.BorderBrush   = active == ColourTheme.Blue   ? accentBrush : borderBrush;
        ColourAmberBtn.BorderBrush  = active == ColourTheme.Amber  ? accentBrush : borderBrush;
        ColourRedBtn.BorderBrush    = active == ColourTheme.Red    ? accentBrush : borderBrush;
        ColourPurpleBtn.BorderBrush = active == ColourTheme.Purple ? accentBrush : borderBrush;
        ColourCyanBtn.BorderBrush   = active == ColourTheme.Cyan   ? accentBrush : borderBrush;
    }

    private void ShapeAngular_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrderLogViewModel vm) vm.ShapeVariant = ShapeVariant.Angular;
    }

    private void ShapeRounded_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrderLogViewModel vm) vm.ShapeVariant = ShapeVariant.Rounded;
    }

    private void ShapeSharp_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrderLogViewModel vm) vm.ShapeVariant = ShapeVariant.Sharp;
    }

    private void ColourNeon_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrderLogViewModel vm) vm.ColourTheme = ColourTheme.Neon;
    }

    private void ColourBlue_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrderLogViewModel vm) vm.ColourTheme = ColourTheme.Blue;
    }

    private void ColourAmber_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrderLogViewModel vm) vm.ColourTheme = ColourTheme.Amber;
    }

    private void ColourRed_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrderLogViewModel vm) vm.ColourTheme = ColourTheme.Red;
    }

    private void ColourPurple_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrderLogViewModel vm) vm.ColourTheme = ColourTheme.Purple;
    }

    private void ColourCyan_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrderLogViewModel vm) vm.ColourTheme = ColourTheme.Cyan;
    }

    private void ClearArchived_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not OrderLogViewModel vm) return;

        if (vm.ArchivedItems.Count == 0)
        {
            vm.StatusMessage = "No archived items to clear";
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to permanently delete all {vm.ArchivedItems.Count} archived items?\n\nThis action cannot be undone.",
            "Clear All Archived",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            ClearArchivedAsync(vm);
        }
    }

    private async void ClearArchivedAsync(OrderLogViewModel vm)
    {
        try
        {
            var count = vm.ArchivedItems.Count;
            await vm.ClearAllArchivedAsync();
            vm.StatusMessage = $"Cleared {count} archived items";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to clear archived items");
        }
    }

    private void PickOrderColor_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not OrderLogViewModel vm) return;

        var picker = new OrderColorPickerWindow(vm.DefaultOrderColor)
        {
            Owner = Window.GetWindow(this)
        };

        if (picker.ShowDialog() == true)
        {
            vm.DefaultOrderColor = picker.SelectedColor;
        }
    }

    private void PickNoteColor_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is not OrderLogViewModel vm) return;

        var picker = new OrderColorPickerWindow(vm.DefaultNoteColor)
        {
            Owner = Window.GetWindow(this)
        };

        if (picker.ShowDialog() == true)
        {
            vm.DefaultNoteColor = picker.SelectedColor;
        }
    }
}
