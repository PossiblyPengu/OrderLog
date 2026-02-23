using System.Windows;
using System.Windows.Input;
using SOUP.Features.OrderLog.ViewModels;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.Helpers;

/// <summary>
/// Manages keyboard shortcuts for the Order Log widget
/// </summary>
public class KeyboardShortcutManager
{
    private readonly OrderLogViewModel _viewModel;
    private UIElement? _targetElement;

    public KeyboardShortcutManager(OrderLogViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    /// <summary>
    /// Registers keyboard shortcuts on the specified UI element
    /// </summary>
    public void RegisterShortcuts(UIElement element)
    {
        _targetElement = element;
        element.PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// Unregisters keyboard shortcuts
    /// </summary>
    public void UnregisterShortcuts()
    {
        if (_targetElement != null)
        {
            _targetElement.PreviewKeyDown -= OnPreviewKeyDown;
            _targetElement = null;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Don't intercept if user is typing in a TextBox
        if (e.OriginalSource is System.Windows.Controls.TextBox textBox)
        {
            // Allow Ctrl+A, Ctrl+Z, Ctrl+F even in TextBox for specific cases
            bool isCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            if (isCtrl && e.Key == Key.F)
            {
                // Ctrl+F should always focus the search box
                e.Handled = true;
                HandleCtrlF();
                return;
            }

            // Allow other shortcuts only if TextBox is empty or read-only
            if (!string.IsNullOrEmpty(textBox.Text) && !textBox.IsReadOnly)
            {
                return;
            }
        }

        bool handled = HandleKeyboardShortcut(e.Key, Keyboard.Modifiers);
        if (handled)
        {
            e.Handled = true;
        }
    }

    private bool HandleKeyboardShortcut(Key key, ModifierKeys modifiers)
    {
        // Ctrl key combinations
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            switch (key)
            {
                case Key.N:
                    // Ctrl+N: New order (focus vendor name field or trigger add)
                    HandleCtrlN();
                    return true;

                case Key.M:
                    // Ctrl+M: New sticky note
                    HandleCtrlM();
                    return true;

                case Key.F:
                    // Ctrl+F: Focus search
                    HandleCtrlF();
                    return true;

                case Key.Z:
                    // Ctrl+Z: Undo
                    HandleCtrlZ();
                    return true;

                case Key.A:
                    // Ctrl+A: Archive selected (only if not in TextBox)
                    if (!(Keyboard.FocusedElement is System.Windows.Controls.TextBox))
                    {
                        HandleCtrlA();
                        return true;
                    }
                    break;

                case Key.C:
                    // Ctrl+C: Copy selected items (only if not in TextBox)
                    if (!(Keyboard.FocusedElement is System.Windows.Controls.TextBox))
                    {
                        HandleCtrlC();
                        return true;
                    }
                    break;

                case Key.V:
                    // Ctrl+V: Paste items (only if not in TextBox)
                    if (!(Keyboard.FocusedElement is System.Windows.Controls.TextBox))
                    {
                        HandleCtrlV();
                        return true;
                    }
                    break;

                case Key.D:
                    // Ctrl+D: Duplicate selected items (only if not in TextBox)
                    if (!(Keyboard.FocusedElement is System.Windows.Controls.TextBox))
                    {
                        HandleCtrlD();
                        return true;
                    }
                    break;

                case Key.Delete:
                    // Ctrl+Delete: Delete selected
                    HandleCtrlDelete();
                    return true;

                case Key.E:
                    // Ctrl+Shift+E: Export to CSV
                    if (modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        HandleCtrlShiftE();
                        return true;
                    }
                    break;

                case Key.J:
                    // Ctrl+Shift+J: Export to JSON
                    if (modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        HandleCtrlShiftJ();
                        return true;
                    }
                    break;

                case Key.I:
                    // Ctrl+Shift+I: Import from CSV
                    if (modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        HandleCtrlShiftI();
                        return true;
                    }
                    break;

                case Key.Home:
                    // Ctrl+Home: Jump to top
                    HandleCtrlHome();
                    return true;

                case Key.End:
                    // Ctrl+End: Jump to bottom
                    HandleCtrlEnd();
                    return true;

                case Key.G:
                    // Ctrl+G: Jump to... (future implementation)
                    HandleCtrlG();
                    return true;

                // Quick status shortcuts
                case Key.D0:
                case Key.NumPad0:
                    // Ctrl+0: Set status to NotReady
                    HandleStatusShortcut(OrderItem.OrderStatus.NotReady);
                    return true;

                case Key.D1:
                case Key.NumPad1:
                    // Ctrl+Shift+1: Apply template #1
                    // Ctrl+1: Set status to NotReady
                    if (modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        HandleApplyTemplate(0);
                        return true;
                    }
                    HandleStatusShortcut(OrderItem.OrderStatus.NotReady);
                    return true;

                case Key.D2:
                case Key.NumPad2:
                    // Ctrl+Shift+2: Apply template #2
                    // Ctrl+2: Set status to OnDeck
                    if (modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        HandleApplyTemplate(1);
                        return true;
                    }
                    HandleStatusShortcut(OrderItem.OrderStatus.OnDeck);
                    return true;

                case Key.D3:
                case Key.NumPad3:
                    // Ctrl+Shift+3: Apply template #3
                    // Ctrl+3: Set status to InProgress
                    if (modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        HandleApplyTemplate(2);
                        return true;
                    }
                    HandleStatusShortcut(OrderItem.OrderStatus.InProgress);
                    return true;

                case Key.T:
                    // Ctrl+Shift+T: Manage templates
                    if (modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        HandleManageTemplates();
                        return true;
                    }
                    break;
            }
        }
        // Arrow key navigation (without Ctrl)
        else if (modifiers == ModifierKeys.None)
        {
            switch (key)
            {
                case Key.Escape:
                    // Escape: Clear search
                    if (_viewModel.IsSearchActive)
                    {
                        _viewModel.ClearSearchCommand?.Execute(null);
                        return true;
                    }
                    break;

                case Key.Up:
                    // Arrow Up: Navigate to previous item
                    HandleArrowUp();
                    return true;

                case Key.Down:
                    // Arrow Down: Navigate to next item
                    HandleArrowDown();
                    return true;
            }
        }

        // F1: Show keyboard shortcuts help (future)
        if (key == Key.F1)
        {
            HandleF1();
            return true;
        }

        return false;
    }

    // Command handlers
    private void HandleCtrlN()
    {
        // Trigger add new order - will need UI hookup
        // For now, we can raise a custom event or use a command
        // Implementation depends on how AddBlankOrder_Click is wired
    }

    private void HandleCtrlM()
    {
        // Trigger add new sticky note
        // Similar to HandleCtrlN
    }

    private void HandleCtrlF()
    {
        // Focus the search box
        // This will need to be implemented in the View's code-behind
        // Raise an event that the View can subscribe to
        SearchFocusRequested?.Invoke();
    }

    private void HandleCtrlZ()
    {
        // Execute undo command
        _viewModel.UndoCommand?.Execute(null);
    }

    private void HandleCtrlA()
    {
        // Archive selected item
        if (_viewModel.SelectedItem != null)
        {
            _viewModel.ArchiveOrderCommand?.Execute(_viewModel.SelectedItem);
        }
    }

    private void HandleCtrlDelete()
    {
        // Delete selected item
        if (_viewModel.SelectedItem != null)
        {
            _viewModel.DeleteCommand?.Execute(_viewModel.SelectedItem);
        }
    }

    private void HandleCtrlC()
    {
        // Copy selected item(s)
        _viewModel.CopyCommand?.Execute(null);
    }

    private void HandleCtrlV()
    {
        // Paste item(s)
        _viewModel.PasteCommand?.Execute(null);
    }

    private void HandleCtrlD()
    {
        // Duplicate selected item(s)
        _viewModel.DuplicateCommand?.Execute(null);
    }

    private void HandleCtrlShiftE()
    {
        // Trigger CSV export
        _viewModel.ExportToCsvCommand?.Execute(null);
    }

    private void HandleCtrlShiftJ()
    {
        // Trigger JSON export
        _viewModel.ExportToJsonCommand?.Execute(null);
    }

    private void HandleCtrlShiftI()
    {
        // Trigger CSV import
        _viewModel.ImportFromCsvCommand?.Execute(null);
    }

    private void HandleStatusShortcut(OrderItem.OrderStatus status)
    {
        // Set selected item status
        if (_viewModel.SelectedItem != null && !_viewModel.SelectedItem.IsArchived)
        {
            _viewModel.SelectedItem.Status = status;
        }
    }

    private void HandleCtrlHome()
    {
        // Jump to top - update navigation state and raise event for View to handle scrolling
        _viewModel.NavigateToTopCommand?.Execute(null);
        ScrollToTopRequested?.Invoke();
    }

    private void HandleCtrlEnd()
    {
        // Jump to bottom - update navigation state and raise event for View to handle scrolling
        _viewModel.NavigateToBottomCommand?.Execute(null);
        ScrollToBottomRequested?.Invoke();
    }

    private void HandleCtrlG()
    {
        // Show "Jump to..." dialog (future implementation)
        JumpToDialogRequested?.Invoke();
    }

    private void HandleArrowUp()
    {
        // Navigate to previous item
        _viewModel.NavigatePreviousCommand?.Execute(null);
    }

    private void HandleArrowDown()
    {
        // Navigate to next item
        _viewModel.NavigateNextCommand?.Execute(null);
    }

    private void HandleF1()
    {
        // Show keyboard shortcuts help dialog (future implementation)
        HelpDialogRequested?.Invoke();
    }

    private void HandleApplyTemplate(int templateIndex)
    {
        // Templates feature removed: no-op
    }

    private void HandleManageTemplates()
    {
        // Templates feature removed: no-op
    }

    // Events for View to subscribe to
    public event Action? SearchFocusRequested;
    public event Action? ScrollToTopRequested;
    public event Action? ScrollToBottomRequested;
    public event Action? JumpToDialogRequested;
    public event Action? HelpDialogRequested;
}
