using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SOUP.Features.OrderLog.Services;
using SOUP.Features.OrderLog.ViewModels;

namespace SOUP.Features.OrderLog.Views;

/// <summary>
/// Interaction logic for UndoHistoryPanel.xaml
/// </summary>
public partial class UndoHistoryPanel : UserControl
{
    private OrderLogViewModel? _viewModel;

    public UndoHistoryPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrderLogViewModel vm)
        {
            _viewModel = vm;
            UpdateHistory();

            // Subscribe to stack changes if we have access to the stack
            // Note: This would require exposing the stack or an event from the ViewModel
        }
    }

    private void UpdateHistory()
    {
        if (_viewModel == null) return;

        // Update bindings
        // Note: The actual binding happens through the DataContext
        // This method is here for future manual updates if needed
    }

    public static readonly DependencyProperty UndoHistoryProperty =
        DependencyProperty.Register(
            nameof(UndoHistory),
            typeof(IEnumerable<UndoableAction>),
            typeof(UndoHistoryPanel),
            new PropertyMetadata(null));

    public IEnumerable<UndoableAction>? UndoHistory
    {
        get => (IEnumerable<UndoableAction>?)GetValue(UndoHistoryProperty);
        set => SetValue(UndoHistoryProperty, value);
    }

    public static readonly DependencyProperty RedoHistoryProperty =
        DependencyProperty.Register(
            nameof(RedoHistory),
            typeof(IEnumerable<UndoableAction>),
            typeof(UndoHistoryPanel),
            new PropertyMetadata(null));

    public IEnumerable<UndoableAction>? RedoHistory
    {
        get => (IEnumerable<UndoableAction>?)GetValue(RedoHistoryProperty);
        set => SetValue(RedoHistoryProperty, value);
    }

    public static readonly DependencyProperty UndoCountProperty =
        DependencyProperty.Register(
            nameof(UndoCount),
            typeof(int),
            typeof(UndoHistoryPanel),
            new PropertyMetadata(0));

    public int UndoCount
    {
        get => (int)GetValue(UndoCountProperty);
        set => SetValue(UndoCountProperty, value);
    }

    public static readonly DependencyProperty RedoCountProperty =
        DependencyProperty.Register(
            nameof(RedoCount),
            typeof(int),
            typeof(UndoHistoryPanel),
            new PropertyMetadata(0));

    public int RedoCount
    {
        get => (int)GetValue(RedoCountProperty);
        set => SetValue(RedoCountProperty, value);
    }

    public static readonly DependencyProperty HasHistoryProperty =
        DependencyProperty.Register(
            nameof(HasHistory),
            typeof(bool),
            typeof(UndoHistoryPanel),
            new PropertyMetadata(false));

    public bool HasHistory
    {
        get => (bool)GetValue(HasHistoryProperty);
        set => SetValue(HasHistoryProperty, value);
    }

    public static readonly DependencyProperty HasNoHistoryProperty =
        DependencyProperty.Register(
            nameof(HasNoHistory),
            typeof(bool),
            typeof(UndoHistoryPanel),
            new PropertyMetadata(true));

    public bool HasNoHistory
    {
        get => (bool)GetValue(HasNoHistoryProperty);
        set => SetValue(HasNoHistoryProperty, value);
    }
}
