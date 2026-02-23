using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Xaml.Behaviors;
using SOUP.Features.OrderLog.Services;

namespace SOUP.Features.OrderLog.Behaviors;

/// <summary>
/// Attached behavior that adds vendor autocomplete functionality to a TextBox.
/// Shows a popup with matching vendor suggestions as the user types.
/// </summary>
public class VendorAutocompleteBehavior : Behavior<TextBox>
{
    private Popup? _popup;
    private ListBox? _listBox;
    private bool _isUpdatingText;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.Register(nameof(IsEnabled), typeof(bool), typeof(VendorAutocompleteBehavior),
            new PropertyMetadata(true));

    public static readonly DependencyProperty MinimumSearchLengthProperty =
        DependencyProperty.Register(nameof(MinimumSearchLength), typeof(int), typeof(VendorAutocompleteBehavior),
            new PropertyMetadata(1));

    public static readonly DependencyProperty MaxSuggestionsProperty =
        DependencyProperty.Register(nameof(MaxSuggestions), typeof(int), typeof(VendorAutocompleteBehavior),
            new PropertyMetadata(10));

    /// <summary>
    /// Whether autocomplete is enabled
    /// </summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// Minimum characters before showing suggestions
    /// </summary>
    public int MinimumSearchLength
    {
        get => (int)GetValue(MinimumSearchLengthProperty);
        set => SetValue(MinimumSearchLengthProperty, value);
    }

    /// <summary>
    /// Maximum number of suggestions to show
    /// </summary>
    public int MaxSuggestions
    {
        get => (int)GetValue(MaxSuggestionsProperty);
        set => SetValue(MaxSuggestionsProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();

        AssociatedObject.TextChanged += OnTextChanged;
        AssociatedObject.PreviewKeyDown += OnPreviewKeyDown;
        AssociatedObject.LostFocus += OnLostFocus;
        AssociatedObject.GotFocus += OnGotFocus;

        CreatePopup();
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();

        AssociatedObject.TextChanged -= OnTextChanged;
        AssociatedObject.PreviewKeyDown -= OnPreviewKeyDown;
        AssociatedObject.LostFocus -= OnLostFocus;
        AssociatedObject.GotFocus -= OnGotFocus;

        if (_popup != null)
        {
            _popup.IsOpen = false;
            _popup = null;
        }
        _listBox = null;
    }

    private void CreatePopup()
    {
        _listBox = new ListBox
        {
            MaxHeight = 200,
            MinWidth = 200,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            Background = Application.Current.TryFindResource("SurfaceBrush") as Brush ?? Brushes.White,
            BorderBrush = Application.Current.TryFindResource("BorderBrush") as Brush ?? Brushes.Gray,
            Foreground = Application.Current.TryFindResource("TextPrimaryBrush") as Brush ?? Brushes.Black,
        };

        _listBox.PreviewMouseLeftButtonDown += OnListBoxItemClicked;
        _listBox.PreviewKeyDown += OnListBoxKeyDown;

        _popup = new Popup
        {
            PlacementTarget = AssociatedObject,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = new Border
            {
                Background = Application.Current.TryFindResource("SurfaceBrush") as Brush ?? Brushes.White,
                BorderBrush = Application.Current.TryFindResource("BorderBrush") as Brush ?? Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8,
                    Opacity = 0.3,
                    ShadowDepth = 2
                },
                Child = _listBox
            }
        };
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        // Show suggestions when focused if there's text
        if (!string.IsNullOrWhiteSpace(AssociatedObject.Text) && AssociatedObject.Text.Length >= MinimumSearchLength)
        {
            UpdateSuggestions(AssociatedObject.Text);
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingText || !IsEnabled) return;

        var text = AssociatedObject.Text;

        if (string.IsNullOrWhiteSpace(text) || text.Length < MinimumSearchLength)
        {
            HidePopup();
            return;
        }

        UpdateSuggestions(text);
    }

    private void UpdateSuggestions(string searchText)
    {
        if (_listBox == null || _popup == null) return;

        var suggestions = VendorAutocompleteService.Instance.Search(searchText, MaxSuggestions);

        if (suggestions.Count == 0)
        {
            HidePopup();
            return;
        }

        _listBox.ItemsSource = suggestions;
        _listBox.SelectedIndex = -1;

        // Update width to match textbox
        if (_popup.Child is Border border)
        {
            border.MinWidth = Math.Max(200, AssociatedObject.ActualWidth);
        }

        _popup.IsOpen = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_popup == null || _listBox == null || !_popup.IsOpen) return;

        switch (e.Key)
        {
            case Key.Down:
                if (_listBox.SelectedIndex < _listBox.Items.Count - 1)
                {
                    _listBox.SelectedIndex++;
                    _listBox.ScrollIntoView(_listBox.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (_listBox.SelectedIndex > 0)
                {
                    _listBox.SelectedIndex--;
                    _listBox.ScrollIntoView(_listBox.SelectedItem);
                }
                e.Handled = true;
                break;

            case Key.Enter:
            case Key.Tab:
                if (_listBox.SelectedIndex >= 0)
                {
                    SelectItem(_listBox.SelectedItem as string);
                    e.Handled = e.Key == Key.Enter;
                }
                else
                {
                    HidePopup();
                }
                break;

            case Key.Escape:
                HidePopup();
                e.Handled = true;
                break;
        }
    }

    private void OnListBoxItemClicked(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element)
        {
            var item = element.DataContext as string;
            if (!string.IsNullOrEmpty(item))
            {
                SelectItem(item);
                e.Handled = true;
            }
        }
    }

    private void OnListBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _listBox?.SelectedItem is string item)
        {
            SelectItem(item);
            e.Handled = true;
        }
    }

    private void SelectItem(string? item)
    {
        if (string.IsNullOrEmpty(item)) return;

        _isUpdatingText = true;
        try
        {
            AssociatedObject.Text = item;
            AssociatedObject.CaretIndex = item.Length;
            
            // Record usage to boost this vendor in future suggestions
            VendorAutocompleteService.Instance.RecordVendorUsage(item);
        }
        finally
        {
            _isUpdatingText = false;
        }

        HidePopup();

        // Move focus to trigger LostFocus binding update
        var request = new TraversalRequest(FocusNavigationDirection.Next);
        AssociatedObject.MoveFocus(request);
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        // Small delay to allow click on popup item to register
        _ = AssociatedObject.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!AssociatedObject.IsKeyboardFocusWithin && _listBox?.IsKeyboardFocusWithin != true)
            {
                HidePopup();
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void HidePopup()
    {
        if (_popup != null)
        {
            _popup.IsOpen = false;
        }
    }
}
