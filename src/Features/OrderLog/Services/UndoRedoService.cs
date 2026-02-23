using System;
using System.Collections.Generic;
using System.Linq;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.Services;

/// <summary>
/// Base class for all undoable actions
/// </summary>
public abstract class UndoableAction
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public abstract string Description { get; }

    /// <summary>
    /// Executes the action
    /// </summary>
    public abstract void Execute();

    /// <summary>
    /// Undoes the action
    /// </summary>
    public abstract void Undo();

    /// <summary>
    /// Re-executes the action (default implementation calls Execute)
    /// </summary>
    public virtual void Redo() => Execute();
}

/// <summary>
/// Action for changing item status
/// </summary>
public class StatusChangeAction : UndoableAction
{
    private readonly List<OrderItem> _items;
    private readonly OrderItem.OrderStatus _newStatus;
    private readonly Dictionary<OrderItem, OrderItem.OrderStatus> _previousStatuses;

    public override string Description =>
        _items.Count == 1
            ? $"Change status to {_newStatus}"
            : $"Change status of {_items.Count} items to {_newStatus}";

    public StatusChangeAction(IEnumerable<OrderItem> items, OrderItem.OrderStatus newStatus)
    {
        _items = items.ToList();
        _newStatus = newStatus;
        _previousStatuses = _items.ToDictionary(item => item, item => item.Status);
    }

    public override void Execute()
    {
        foreach (var item in _items)
        {
            item.Status = _newStatus;
        }
    }

    public override void Undo()
    {
        foreach (var item in _items)
        {
            if (_previousStatuses.TryGetValue(item, out var previousStatus))
            {
                item.Status = previousStatus;
            }
        }
    }
}

/// <summary>
/// Action for archiving items
/// </summary>
public class ArchiveAction : UndoableAction
{
    private readonly List<OrderItem> _items;
    private readonly Dictionary<OrderItem, (OrderItem.OrderStatus Status, bool IsArchived)> _previousStates;

    public override string Description =>
        _items.Count == 1
            ? "Archive item"
            : $"Archive {_items.Count} items";

    public ArchiveAction(IEnumerable<OrderItem> items)
    {
        _items = items.ToList();
        _previousStates = _items.ToDictionary(
            item => item,
            item => (item.Status, item.IsArchived)
        );
    }

    public override void Execute()
    {
        foreach (var item in _items)
        {
            item.PreviousStatus = item.Status;
            item.IsArchived = true;
        }
    }

    public override void Undo()
    {
        foreach (var item in _items)
        {
            if (_previousStates.TryGetValue(item, out var previousState))
            {
                item.Status = previousState.Status;
                item.IsArchived = previousState.IsArchived;
            }
        }
    }
}

/// <summary>
/// Action for unarchiving items
/// </summary>
public class UnarchiveAction : UndoableAction
{
    private readonly List<OrderItem> _items;
    private readonly Dictionary<OrderItem, (OrderItem.OrderStatus Status, bool IsArchived)> _previousStates;

    public override string Description =>
        _items.Count == 1
            ? "Unarchive item"
            : $"Unarchive {_items.Count} items";

    public UnarchiveAction(IEnumerable<OrderItem> items)
    {
        _items = items.ToList();
        _previousStates = _items.ToDictionary(
            item => item,
            item => (item.Status, item.IsArchived)
        );
    }

    public override void Execute()
    {
        foreach (var item in _items)
        {
            item.IsArchived = false;
            item.Status = item.PreviousStatus ?? OrderItem.OrderStatus.InProgress;
        }
    }

    public override void Undo()
    {
        foreach (var item in _items)
        {
            if (_previousStates.TryGetValue(item, out var previousState))
            {
                item.Status = previousState.Status;
                item.IsArchived = previousState.IsArchived;
            }
        }
    }
}

/// <summary>
/// Action for editing a field value
/// </summary>
public class FieldEditAction : UndoableAction
{
    private readonly OrderItem _item;
    private readonly string _fieldName;
    private readonly object? _oldValue;
    private readonly object? _newValue;
    private readonly Action<object?> _setter;

    public override string Description => $"Edit {_fieldName}";

    public FieldEditAction(OrderItem item, string fieldName, object? oldValue, object? newValue, Action<object?> setter)
    {
        _item = item;
        _fieldName = fieldName;
        _oldValue = oldValue;
        _newValue = newValue;
        _setter = setter;
    }

    public override void Execute()
    {
        _setter(_newValue);
    }

    public override void Undo()
    {
        _setter(_oldValue);
    }
}

/// <summary>
/// Action for linking items
/// </summary>
public class LinkAction : UndoableAction
{
    private readonly List<OrderItem> _items;
    private readonly Guid _groupId;
    private readonly Dictionary<OrderItem, Guid?> _previousGroupIds;

    public override string Description =>
        _items.Count == 1
            ? "Link item"
            : $"Link {_items.Count} items";

    public LinkAction(IEnumerable<OrderItem> items, Guid groupId)
    {
        _items = items.ToList();
        _groupId = groupId;
        _previousGroupIds = _items.ToDictionary(item => item, item => item.LinkedGroupId);
    }

    public override void Execute()
    {
        foreach (var item in _items)
        {
            item.LinkedGroupId = _groupId;
        }
    }

    public override void Undo()
    {
        foreach (var item in _items)
        {
            if (_previousGroupIds.TryGetValue(item, out var previousGroupId))
            {
                item.LinkedGroupId = previousGroupId;
            }
        }
    }
}

/// <summary>
/// Action for unlinking items
/// </summary>
public class UnlinkAction : UndoableAction
{
    private readonly List<OrderItem> _items;
    private readonly Dictionary<OrderItem, Guid?> _previousGroupIds;

    public override string Description =>
        _items.Count == 1
            ? "Unlink item"
            : $"Unlink {_items.Count} items";

    public UnlinkAction(IEnumerable<OrderItem> items)
    {
        _items = items.ToList();
        _previousGroupIds = _items.ToDictionary(item => item, item => item.LinkedGroupId);
    }

    public override void Execute()
    {
        foreach (var item in _items)
        {
            item.LinkedGroupId = null;
        }
    }

    public override void Undo()
    {
        foreach (var item in _items)
        {
            if (_previousGroupIds.TryGetValue(item, out var previousGroupId))
            {
                item.LinkedGroupId = previousGroupId;
            }
        }
    }
}

/// <summary>
/// Action for deleting items
/// </summary>
public class DeleteAction : UndoableAction
{
    private readonly List<OrderItem> _items;
    private readonly ICollection<OrderItem> _collection;
    private readonly List<int> _originalIndices;

    public override string Description =>
        _items.Count == 1
            ? "Delete item"
            : $"Delete {_items.Count} items";

    public DeleteAction(IEnumerable<OrderItem> items, ICollection<OrderItem> collection)
    {
        _items = items.ToList();
        _collection = collection;

        // Store original indices for proper restoration
        _originalIndices = new List<int>();
        if (_collection is IList<OrderItem> list)
        {
            foreach (var item in _items)
            {
                _originalIndices.Add(list.IndexOf(item));
            }
        }
    }

    public override void Execute()
    {
        foreach (var item in _items)
        {
            _collection.Remove(item);
        }
    }

    public override void Undo()
    {
        if (_collection is IList<OrderItem> list)
        {
            // Restore items at their original positions
            for (int i = 0; i < _items.Count; i++)
            {
                var originalIndex = _originalIndices[i];
                if (originalIndex >= 0 && originalIndex <= list.Count)
                {
                    list.Insert(originalIndex, _items[i]);
                }
                else
                {
                    list.Add(_items[i]);
                }
            }
        }
        else
        {
            // Fallback for non-list collections
            foreach (var item in _items)
            {
                _collection.Add(item);
            }
        }
    }
}

/// <summary>
/// Action for reordering items (move up/down)
/// </summary>
public class ReorderAction : UndoableAction
{
    private readonly OrderItem _item;
    private readonly IList<OrderItem> _collection;
    private readonly int _oldIndex;
    private readonly int _newIndex;

    public override string Description => $"Move item";

    public ReorderAction(OrderItem item, IList<OrderItem> collection, int oldIndex, int newIndex)
    {
        _item = item;
        _collection = collection;
        _oldIndex = oldIndex;
        _newIndex = newIndex;
    }

    public override void Execute()
    {
        if (_oldIndex >= 0 && _oldIndex < _collection.Count && _newIndex >= 0 && _newIndex < _collection.Count)
        {
            _collection.RemoveAt(_oldIndex);
            _collection.Insert(_newIndex, _item);
        }
    }

    public override void Undo()
    {
        var currentIndex = _collection.IndexOf(_item);
        if (currentIndex >= 0 && _oldIndex >= 0 && _oldIndex <= _collection.Count)
        {
            _collection.RemoveAt(currentIndex);
            _collection.Insert(_oldIndex, _item);
        }
    }
}

/// <summary>
/// Action for changing sticky note color
/// </summary>
public class ColorChangeAction : UndoableAction
{
    private readonly List<OrderItem> _items;
    private readonly string _newColor;
    private readonly Dictionary<OrderItem, string> _previousColors;

    public override string Description =>
        _items.Count == 1
            ? "Change note color"
            : $"Change color of {_items.Count} notes";

    public ColorChangeAction(IEnumerable<OrderItem> items, string newColor)
    {
        _items = items.ToList();
        _newColor = newColor;
        _previousColors = _items.ToDictionary(item => item, item => item.ColorHex);
    }

    public override void Execute()
    {
        foreach (var item in _items)
        {
            item.ColorHex = _newColor;
        }
    }

    public override void Undo()
    {
        foreach (var item in _items)
        {
            if (_previousColors.TryGetValue(item, out var previousColor))
            {
                item.ColorHex = previousColor;
            }
        }
    }
}

/// <summary>
/// Action for pasting/duplicating items with undo support
/// </summary>
public class PasteAction : UndoableAction
{
    private readonly List<OrderItem> _pastedItems;
    private readonly ICollection<OrderItem> _collection;
    private readonly int _insertIndex;

    public override string Description =>
        _pastedItems.Count == 1
            ? "Paste item"
            : $"Paste {_pastedItems.Count} items";

    public PasteAction(List<OrderItem> pastedItems, ICollection<OrderItem> collection, int insertIndex)
    {
        _pastedItems = pastedItems;
        _collection = collection;
        _insertIndex = insertIndex;
    }

    public override void Execute()
    {
        // Insert items at specified index
        if (_collection is IList<OrderItem> list)
        {
            int index = _insertIndex;
            foreach (var item in _pastedItems)
            {
                if (index <= list.Count)
                {
                    list.Insert(index, item);
                    index++;
                }
                else
                {
                    list.Add(item);
                }
            }
        }
        else
        {
            // Fallback for non-list collections
            foreach (var item in _pastedItems)
            {
                _collection.Add(item);
            }
        }
    }

    public override void Undo()
    {
        // Remove all pasted items
        foreach (var item in _pastedItems)
        {
            _collection.Remove(item);
        }
    }
}

/// <summary>
/// Manages undo/redo stack with history
/// </summary>
public class UndoRedoStack
{
    private readonly Stack<UndoableAction> _undoStack = new();
    private readonly Stack<UndoableAction> _redoStack = new();
    private readonly int _maxHistorySize;

    public event Action? StackChanged;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;

    public IEnumerable<UndoableAction> UndoHistory => _undoStack;
    public IEnumerable<UndoableAction> RedoHistory => _redoStack;

    public UndoRedoStack(int maxHistorySize = 50)
    {
        _maxHistorySize = maxHistorySize;
    }

    /// <summary>
    /// Executes an action and adds it to the undo stack
    /// </summary>
    public void ExecuteAction(UndoableAction action)
    {
        action.Execute();
        _undoStack.Push(action);
        _redoStack.Clear(); // Clear redo stack when new action is executed

        // Trim stack if it exceeds max size
        while (_undoStack.Count > _maxHistorySize)
        {
            var items = _undoStack.ToList();
            items.RemoveAt(items.Count - 1);
            _undoStack.Clear();
            foreach (var item in Enumerable.Reverse(items))
            {
                _undoStack.Push(item);
            }
        }

        StackChanged?.Invoke();
    }

    /// <summary>
    /// Undoes the last action
    /// </summary>
    public void Undo()
    {
        if (!CanUndo) return;

        var action = _undoStack.Pop();
        action.Undo();
        _redoStack.Push(action);

        StackChanged?.Invoke();
    }

    /// <summary>
    /// Redoes the last undone action
    /// </summary>
    public void Redo()
    {
        if (!CanRedo) return;

        var action = _redoStack.Pop();
        action.Redo();
        _undoStack.Push(action);

        StackChanged?.Invoke();
    }

    /// <summary>
    /// Clears all history
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StackChanged?.Invoke();
    }
}
