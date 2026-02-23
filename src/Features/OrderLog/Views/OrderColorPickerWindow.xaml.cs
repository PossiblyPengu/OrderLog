using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SOUP.Features.OrderLog.Views;

public partial class OrderColorPickerWindow : Window
{
    public string SelectedColor { get; private set; }

    public OrderColorPickerWindow(string initialColor)
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            try { var path = Path.Combine(Path.GetTempPath(), "OrderColorPickerError.log"); File.AppendAllText(path, DateTime.Now.ToString("o") + " InitializeComponent failed:\n" + ex.ToString() + "\n\n"); } catch { }
            throw;
        }

        try
        {
            SelectedColor = initialColor ?? "#8b5cf6";
            HexBox.Text = SelectedColor;
            // Initialize sliders from initial color
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(SelectedColor);
                RSlider.Value = color.R;
                GSlider.Value = color.G;
                BSlider.Value = color.B;
                UpdateSlidersText();
            }
            catch { }
        }
        catch (Exception ex)
        {
            try { var path = Path.Combine(Path.GetTempPath(), "OrderColorPickerError.log"); File.AppendAllText(path, DateTime.Now.ToString("o") + " Constructor post-init failed:\n" + ex.ToString() + "\n\n"); } catch { }
            throw;
        }

        UpdatePreview(SelectedColor);
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string color)
        {
            SelectedColor = color;
            HexBox.Text = color;
            // Update sliders to reflect this color
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(color);
                RSlider.Value = c.R;
                GSlider.Value = c.G;
                BSlider.Value = c.B;
                UpdateSlidersText();
            }
            catch { }
            UpdatePreview(color);
        }
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var tb = sender as TextBox;
        var hex = tb?.Text?.Trim();
        if (string.IsNullOrEmpty(hex)) return;

        if (!hex.StartsWith("#"))
            hex = "#" + hex;

        if (hex.Length == 7 || hex.Length == 9)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                // Update sliders to match typed hex (guard null sliders during XAML load)
                if (RSlider != null) RSlider.Value = color.R;
                if (GSlider != null) GSlider.Value = color.G;
                if (BSlider != null) BSlider.Value = color.B;
                UpdateSlidersText();
                if (CustomColorPreview != null)
                    UpdatePreview(hex);
                SelectedColor = hex;
            }
            catch (FormatException)
            {
                // Invalid color format - ignore silently as user is typing
            }
            catch (Exception ex)
            {
                try { var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OrderColorPickerError.log"); System.IO.File.AppendAllText(path, DateTime.Now.ToString("o") + " HexBox_TextChanged unexpected:\n" + ex.ToString() + "\n\n"); } catch { }
            }
        }
    }

    private void Slider_ValueChanged(object? sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Build hex from RGB sliders and update preview/hex box
        var r = (byte)RSlider.Value;
        var g = (byte)GSlider.Value;
        var b = (byte)BSlider.Value;
        var hex = $"#{r:X2}{g:X2}{b:X2}";
        SelectedColor = hex;
        HexBox.Text = hex;
        UpdateSlidersText();
        UpdatePreview(hex);
    }

    private void UpdateSlidersText()
    {
        RValue.Text = ((int)RSlider.Value).ToString();
        GValue.Text = ((int)GSlider.Value).ToString();
        BValue.Text = ((int)BSlider.Value).ToString();
    }

    private void UpdatePreview(string hex)
    {
        try
        {
            CustomColorPreview.Background = new BrushConverter().ConvertFromString(hex) as Brush;
        }
        catch (FormatException)
        {
            // Invalid color format - ignore
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
