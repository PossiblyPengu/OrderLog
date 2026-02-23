using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;
using SOUP.Features.OrderLog.Models;
using SOUP.Features.OrderLog.Services;

namespace SOUP.Features.OrderLog.Views;

public partial class OrderTemplateManagerDialog : Window
{
    private readonly OrderTemplateService _templateService;
    public ObservableCollection<OrderTemplate> Templates { get; }
    private TemplateSortBy _currentSortBy = TemplateSortBy.Name;

    public OrderTemplateManagerDialog(OrderTemplateService templateService)
    {
        InitializeComponent();
        _templateService = templateService;
        Templates = new ObservableCollection<OrderTemplate>();
        TemplateListBox.ItemsSource = Templates;

        LoadTemplates();
    }

    private async void LoadTemplates()
    {
        try
        {
            var templates = await _templateService.LoadTemplatesAsync();
            RefreshTemplateList(templates);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load templates");
            MessageBox.Show(
                $"Failed to load templates: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void RefreshTemplateList(System.Collections.Generic.List<OrderTemplate> templates)
    {
        // Sort templates based on current sort mode
        var sorted = _templateService.GetTemplatesSorted(_currentSortBy);

        Templates.Clear();
        foreach (var template in sorted)
        {
            Templates.Add(template);
        }

        // Clear selection and preview
        TemplateListBox.SelectedItem = null;
        UpdatePreview(null);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OrderTemplateEditorDialog();
            if (dialog.ShowDialog() == true)
            {
                var template = dialog.Template;
                await _templateService.AddTemplateAsync(template);
                LoadTemplates();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create template");
            MessageBox.Show(
                $"Failed to create template: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateListBox.SelectedItem is not OrderTemplate selected)
            return;

        try
        {
            var dialog = new OrderTemplateEditorDialog(selected);
            if (dialog.ShowDialog() == true)
            {
                var template = dialog.Template;
                await _templateService.UpdateTemplateAsync(template);
                LoadTemplates();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update template");
            MessageBox.Show(
                $"Failed to update template: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateListBox.SelectedItem is not OrderTemplate selected)
            return;

        try
        {
            // Create a copy with new ID and modified name
            var duplicate = new OrderTemplate
            {
                Id = Guid.NewGuid(),
                Name = selected.Name + " (Copy)",
                VendorName = selected.VendorName,
                TransferNumbers = selected.TransferNumbers,
                WhsShipmentNumbers = selected.WhsShipmentNumbers,
                ColorHex = selected.ColorHex,
                DefaultStatus = selected.DefaultStatus,
                CreatedAt = DateTime.UtcNow,
                UseCount = 0
            };

            await _templateService.AddTemplateAsync(duplicate);
            LoadTemplates();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to duplicate template");
            MessageBox.Show(
                $"Failed to duplicate template: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateListBox.SelectedItem is not OrderTemplate selected)
            return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete template '{selected.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _templateService.DeleteTemplateAsync(selected.Id);
            LoadTemplates();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete template");
            MessageBox.Show(
                $"Failed to delete template: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void SortComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SortComboBox.SelectedIndex < 0)
            return;

        _currentSortBy = SortComboBox.SelectedIndex switch
        {
            0 => TemplateSortBy.Name,
            1 => TemplateSortBy.UseCount,
            2 => TemplateSortBy.DateCreated,
            _ => TemplateSortBy.Name
        };

        // Reload with new sort
        LoadTemplates();
    }

    private void TemplateListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selected = TemplateListBox.SelectedItem as OrderTemplate;
        UpdatePreview(selected);
    }

    private void UpdatePreview(OrderTemplate? template)
    {
        if (template == null)
        {
            EmptyStateText.Visibility = Visibility.Visible;
            PreviewContent.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyStateText.Visibility = Visibility.Collapsed;
        PreviewContent.Visibility = Visibility.Visible;

        // Update preview fields
        PreviewName.Text = template.Name;
        PreviewVendor.Text = string.IsNullOrWhiteSpace(template.VendorName) ? "(Not set)" : template.VendorName;
        PreviewTransferNumbers.Text = string.IsNullOrWhiteSpace(template.TransferNumbers) ? "(Not set)" : template.TransferNumbers;
        PreviewWhsNumbers.Text = string.IsNullOrWhiteSpace(template.WhsShipmentNumbers) ? "(Not set)" : template.WhsShipmentNumbers;

        PreviewStatus.Text = template.DefaultStatus switch
        {
            OrderItem.OrderStatus.NotReady => "🔴 Not Ready",
            OrderItem.OrderStatus.OnDeck => "🟡 On Deck",
            OrderItem.OrderStatus.InProgress => "🟢 In Progress",
            _ => "Unknown"
        };

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(template.ColorHex);
            PreviewColor.Background = new SolidColorBrush(color);
        }
        catch
        {
            PreviewColor.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B56576"));
        }

        PreviewUseCount.Text = template.UseCount.ToString();
        PreviewCreatedAt.Text = template.CreatedAt.ToString("yyyy-MM-dd HH:mm");
    }
}

/// <summary>
/// Converter to check if a value is not null (for enabling buttons)
/// </summary>
public class NullToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
