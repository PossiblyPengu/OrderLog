using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.Views;

public partial class OrderTemplateEditorDialog : Window
{
    public new OrderTemplate Template { get; private set; }
    private string _currentColorHex = "#B56576";

    /// <summary>
    /// Create a new template from scratch
    /// </summary>
    public OrderTemplateEditorDialog()
    {
        InitializeComponent();
        TitleText.Text = "Create Template";
        DefaultStatusComboBox.SelectedIndex = 0; // NotReady

        Template = new OrderTemplate
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            ColorHex = _currentColorHex,
            DefaultStatus = OrderItem.OrderStatus.NotReady,
            UseCount = 0
        };

        TemplateNameTextBox.Focus();
    }

    /// <summary>
    /// Edit an existing template
    /// </summary>
    public OrderTemplateEditorDialog(OrderTemplate template)
    {
        InitializeComponent();
        TitleText.Text = "Edit Template";
        Template = template;

        // Populate fields from template
        TemplateNameTextBox.Text = template.Name;
        VendorNameTextBox.Text = template.VendorName ?? string.Empty;
        TransferNumbersTextBox.Text = template.TransferNumbers ?? string.Empty;
        WhsShipmentNumbersTextBox.Text = template.WhsShipmentNumbers ?? string.Empty;

        // Set status
        DefaultStatusComboBox.SelectedIndex = template.DefaultStatus switch
        {
            OrderItem.OrderStatus.NotReady => 0,
            OrderItem.OrderStatus.OnDeck => 1,
            OrderItem.OrderStatus.InProgress => 2,
            _ => 0
        };

        // Set color
        _currentColorHex = template.ColorHex;
        UpdateColorPreview();

        TemplateNameTextBox.Focus();
        TemplateNameTextBox.SelectAll();
    }

    /// <summary>
    /// Create a template from an existing order
    /// </summary>
    public OrderTemplateEditorDialog(OrderItem order)
    {
        InitializeComponent();
        TitleText.Text = "Save as Template";

        Template = new OrderTemplate
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            UseCount = 0
        };

        // Populate fields from order
        TemplateNameTextBox.Text = order.VendorName ?? "New Template";
        VendorNameTextBox.Text = order.VendorName ?? string.Empty;
        TransferNumbersTextBox.Text = order.TransferNumbers ?? string.Empty;
        WhsShipmentNumbersTextBox.Text = order.WhsShipmentNumbers ?? string.Empty;

        // Set status
        DefaultStatusComboBox.SelectedIndex = order.Status switch
        {
            OrderItem.OrderStatus.NotReady => 0,
            OrderItem.OrderStatus.OnDeck => 1,
            OrderItem.OrderStatus.InProgress => 2,
            _ => 0
        };

        // Set color
        _currentColorHex = order.ColorHex ?? "#B56576";
        UpdateColorPreview();

        TemplateNameTextBox.Focus();
        TemplateNameTextBox.SelectAll();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Validate template name
        var templateName = TemplateNameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(templateName))
        {
            MessageBox.Show(
                "Template name is required.",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            TemplateNameTextBox.Focus();
            return;
        }

        // Update template properties
        Template.Name = templateName;
        Template.VendorName = string.IsNullOrWhiteSpace(VendorNameTextBox.Text) ? null : VendorNameTextBox.Text.Trim();
        Template.TransferNumbers = string.IsNullOrWhiteSpace(TransferNumbersTextBox.Text) ? null : TransferNumbersTextBox.Text.Trim();
        Template.WhsShipmentNumbers = string.IsNullOrWhiteSpace(WhsShipmentNumbersTextBox.Text) ? null : WhsShipmentNumbersTextBox.Text.Trim();
        Template.ColorHex = _currentColorHex;

        // Get selected status
        if (DefaultStatusComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            Template.DefaultStatus = selectedItem.Tag?.ToString() switch
            {
                "NotReady" => OrderItem.OrderStatus.NotReady,
                "OnDeck" => OrderItem.OrderStatus.OnDeck,
                "InProgress" => OrderItem.OrderStatus.InProgress,
                _ => OrderItem.OrderStatus.NotReady
            };
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ColorPreview_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Open the color picker dialog
        var colorPicker = new OrderColorPickerWindow(_currentColorHex)
        {
            Owner = this
        };

        if (colorPicker.ShowDialog() == true)
        {
            _currentColorHex = colorPicker.SelectedColor;
            UpdateColorPreview();
        }
    }

    private void ColorPreset_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string colorHex)
        {
            _currentColorHex = colorHex;
            UpdateColorPreview();
        }
    }

    private void UpdateColorPreview()
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(_currentColorHex);
            ColorPreviewBorder.Background = new SolidColorBrush(color);
        }
        catch
        {
            // Fallback to default if color parsing fails
            ColorPreviewBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B56576"));
        }
    }
}
