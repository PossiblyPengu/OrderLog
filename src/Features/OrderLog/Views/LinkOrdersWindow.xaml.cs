using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SOUP.Features.OrderLog.Models;
using SOUP.Features.OrderLog.ViewModels;

namespace SOUP.Features.OrderLog.Views;

public partial class LinkOrdersWindow : Window
{
    private readonly OrderItem _source;
    private readonly OrderLogViewModel _vm;
    private readonly List<OrderItem> _allAvailableOrders;
    private CollectionViewSource _viewSource;

    public List<OrderItem> SelectedOrders => OrdersList.SelectedItems.Cast<OrderItem>().ToList();

    public LinkOrdersWindow(OrderItem source, OrderLogViewModel vm)
    {
        InitializeComponent();
        _source = source;
        _vm = vm;

        // Populate list with non-archived orders excluding the source
        _allAvailableOrders = _vm.Items.Where(i => i.Id != _source.Id && i.NoteType == NoteType.Order).ToList();

        // Set up collection view for filtering
        _viewSource = new() { Source = _allAvailableOrders };
        _viewSource.Filter += ApplyFilters;
        OrdersList.ItemsSource = _viewSource.View;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click to maximize/restore (optional)
            return;
        }
        DragMove();
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
    private void ApplyFilters(object sender, FilterEventArgs e)
    {
        if (e.Item is not OrderItem order)
        {
            e.Accepted = false;
            return;
        }

        // Filter by linked status
        if (ShowLinkedCheckBox.IsChecked == false && order.LinkedGroupId != null)
        {
            e.Accepted = false;
            return;
        }

        // Filter by search text
        var searchText = SearchBox.Text?.Trim();
        if (!string.IsNullOrEmpty(searchText))
        {
            var matches =
                (order.VendorName?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (order.TransferNumbers?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (order.WhsShipmentNumbers?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (order.DisplayTitle?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false);

            if (!matches)
            {
                e.Accepted = false;
                return;
            }
        }

        e.Accepted = true;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _viewSource.View.Refresh();
    }

    private void FilterChanged(object sender, RoutedEventArgs e)
    {
        _viewSource?.View.Refresh();
    }

    private void OrdersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Update Link button state
        LinkBtn.IsEnabled = OrdersList.SelectedItems.Count > 0;

        // Update preview panel
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var selectedOrders = SelectedOrders;

        if (selectedOrders.Count == 0)
        {
            PreviewPlaceholder.Visibility = Visibility.Visible;
            PreviewList.Visibility = Visibility.Collapsed;
            return;
        }

        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        PreviewList.Visibility = Visibility.Visible;

        // Build preview list: source order + selected orders
        var previewItems = new List<OrderItem> { _source };
        previewItems.AddRange(selectedOrders);

        PreviewList.ItemsSource = previewItems;
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void LinkBtn_Click(object sender, RoutedEventArgs e)
    {
        var selectedOrders = SelectedOrders;

        if (selectedOrders.Count == 0)
        {
            MessageBox.Show(this, "Please select one or more orders to link with.", "Link Orders", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Determine group id to use: prefer existing group id from source, any selected item, or create new
        Guid groupId = _source.LinkedGroupId
            ?? selectedOrders.FirstOrDefault(o => o.LinkedGroupId != null)?.LinkedGroupId
            ?? Guid.NewGuid();

        // Collect all items that need to be linked (source + selected + any existing group members)
        var itemsToLink = new HashSet<Guid> { _source.Id };
        foreach (var selected in selectedOrders)
        {
            itemsToLink.Add(selected.Id);
        }

        // Also include any items already in related groups
        var relatedGroupIds = new HashSet<Guid?> { _source.LinkedGroupId };
        foreach (var selected in selectedOrders)
        {
            if (selected.LinkedGroupId != null)
                relatedGroupIds.Add(selected.LinkedGroupId);
        }

        // Apply the unified group id to all related items
        foreach (var item in _vm.Items)
        {
            if (itemsToLink.Contains(item.Id) ||
                (item.LinkedGroupId != null && relatedGroupIds.Contains(item.LinkedGroupId)))
            {
                item.LinkedGroupId = groupId;
            }
        }

        // Also update archived collection if needed
        foreach (var item in _vm.ArchivedItems)
        {
            if (itemsToLink.Contains(item.Id) ||
                (item.LinkedGroupId != null && relatedGroupIds.Contains(item.LinkedGroupId)))
            {
                item.LinkedGroupId = groupId;
            }
        }

        // Show success message
        var count = selectedOrders.Count + 1; // +1 for source
        MessageBox.Show(this,
            $"Successfully linked {count} orders. They will now be displayed as a merged card with unified status.",
            "Link Orders",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        DialogResult = true;
        Close();
    }
}
