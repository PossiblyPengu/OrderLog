using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.Views;

public partial class OrderLogFilterDialog : Window
{
    public OrderItem.OrderStatus[]? SelectedStatuses { get; private set; }
    public DateTime? StartDate { get; private set; }
    public DateTime? EndDate { get; private set; }
    public NoteType? SelectedNoteType { get; private set; }
    public NoteCategory? SelectedNoteCategory { get; private set; }

    public OrderLogFilterDialog(
        OrderItem.OrderStatus[]? currentStatuses = null,
        DateTime? currentStartDate = null,
        DateTime? currentEndDate = null,
        NoteType? currentNoteType = null,
        NoteCategory? currentNoteCategory = null)
    {
        InitializeComponent();

        // Set initial values from current filters
        if (currentStatuses != null)
        {
            StatusNotReadyCheckBox.IsChecked = currentStatuses.Contains(OrderItem.OrderStatus.NotReady);
            StatusOnDeckCheckBox.IsChecked = currentStatuses.Contains(OrderItem.OrderStatus.OnDeck);
            StatusInProgressCheckBox.IsChecked = currentStatuses.Contains(OrderItem.OrderStatus.InProgress);
            StatusDoneCheckBox.IsChecked = currentStatuses.Contains(OrderItem.OrderStatus.Done);
        }

        StartDatePicker.SelectedDate = currentStartDate;
        EndDatePicker.SelectedDate = currentEndDate;

        if (currentNoteType.HasValue)
        {
            TypeAllRadio.IsChecked = false;
            if (currentNoteType == NoteType.Order)
                TypeOrdersOnlyRadio.IsChecked = true;
            else
                TypeNotesOnlyRadio.IsChecked = true;
        }

        if (currentNoteCategory.HasValue)
        {
            CategoryAllRadio.IsChecked = false;
            switch (currentNoteCategory.Value)
            {
                case NoteCategory.General:
                    CategoryGeneralRadio.IsChecked = true;
                    break;
                case NoteCategory.Todo:
                    CategoryTodoRadio.IsChecked = true;
                    break;
                case NoteCategory.Reminder:
                    CategoryReminderRadio.IsChecked = true;
                    break;
                case NoteCategory.Log:
                    CategoryLogRadio.IsChecked = true;
                    break;
                case NoteCategory.Idea:
                    CategoryIdeaRadio.IsChecked = true;
                    break;
            }
        }

        // Wire up event handlers for note type changes
        TypeAllRadio.Checked += NoteTypeRadio_Changed;
        TypeOrdersOnlyRadio.Checked += NoteTypeRadio_Changed;
        TypeNotesOnlyRadio.Checked += NoteTypeRadio_Changed;

        // Update category section visibility based on initial state
        UpdateCategorySectionVisibility();
    }

    private void NoteTypeRadio_Changed(object sender, RoutedEventArgs e)
    {
        UpdateCategorySectionVisibility();
    }

    private void UpdateCategorySectionVisibility()
    {
        // Only show category section when "Sticky Notes Only" is selected
        NoteCategorySection.Visibility = TypeNotesOnlyRadio.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        // Collect selected statuses
        var statuses = new List<OrderItem.OrderStatus>();
        if (StatusNotReadyCheckBox.IsChecked == true)
            statuses.Add(OrderItem.OrderStatus.NotReady);
        if (StatusOnDeckCheckBox.IsChecked == true)
            statuses.Add(OrderItem.OrderStatus.OnDeck);
        if (StatusInProgressCheckBox.IsChecked == true)
            statuses.Add(OrderItem.OrderStatus.InProgress);
        if (StatusDoneCheckBox.IsChecked == true)
            statuses.Add(OrderItem.OrderStatus.Done);

        SelectedStatuses = statuses.Count > 0 ? statuses.ToArray() : null;

        // Date range
        StartDate = StartDatePicker.SelectedDate;
        EndDate = EndDatePicker.SelectedDate;

        // Note type
        if (TypeOrdersOnlyRadio.IsChecked == true)
            SelectedNoteType = NoteType.Order;
        else if (TypeNotesOnlyRadio.IsChecked == true)
            SelectedNoteType = NoteType.StickyNote;
        else
            SelectedNoteType = null;

        // Note category (only applicable for sticky notes)
        if (TypeNotesOnlyRadio.IsChecked == true)
        {
            if (CategoryGeneralRadio.IsChecked == true)
                SelectedNoteCategory = NoteCategory.General;
            else if (CategoryTodoRadio.IsChecked == true)
                SelectedNoteCategory = NoteCategory.Todo;
            else if (CategoryReminderRadio.IsChecked == true)
                SelectedNoteCategory = NoteCategory.Reminder;
            else if (CategoryLogRadio.IsChecked == true)
                SelectedNoteCategory = NoteCategory.Log;
            else if (CategoryIdeaRadio.IsChecked == true)
                SelectedNoteCategory = NoteCategory.Idea;
            else
                SelectedNoteCategory = null;
        }
        else
        {
            SelectedNoteCategory = null;
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        // Uncheck all status checkboxes
        StatusNotReadyCheckBox.IsChecked = false;
        StatusOnDeckCheckBox.IsChecked = false;
        StatusInProgressCheckBox.IsChecked = false;
        StatusDoneCheckBox.IsChecked = false;

        // Clear date pickers
        StartDatePicker.SelectedDate = null;
        EndDatePicker.SelectedDate = null;

        // Reset to "All" for note type
        TypeAllRadio.IsChecked = true;

        // Reset to "All" for note category
        CategoryAllRadio.IsChecked = true;
    }
}
