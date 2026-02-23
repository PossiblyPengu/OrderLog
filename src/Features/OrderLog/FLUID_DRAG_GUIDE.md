# Fluid Drag & Drop System - Testing Guide

## Overview
The OrderLog now features a fluid chess-like drag and drop system with real-time visual feedback, similar to moving pieces on a chessboard.

## Key Features

### 1. Transform-Based Dragging
- **Visual**: Card scales to 1.02x and follows your cursor
- **Performance**: Hardware-accelerated GPU transforms for smooth 60fps
- **Z-Index**: Dragged card floats above others (Z-index 100)

### 2. Real-Time Card Shifting
- **Animation**: Other cards smoothly shift to make space as you drag
- **Duration**: 300ms smooth transitions with QuadraticEase easing
- **Throttling**: Shift calculations throttled to ~60fps for performance

### 3. Two Drag Modes

#### Reorder Mode (Default)
- **Visual**: Green border (3px)
- **Action**: Moves card to new position
- **Usage**: Click and drag any card

#### Link Mode (Ctrl+Drag)
- **Visual**: Purple border (3px)
- **Action**: Links cards together into merged groups
- **Usage**: Hold Ctrl while dragging, drop on target card

### 4. Mode Switching During Drag
- Press/release Ctrl during drag to switch modes
- Border color animates smoothly between green ↔ purple (150ms)
- Mode indicator updates in real-time

### 5. Cancel Drag
- **Key**: Press Escape during drag
- **Effect**: Card animates back to original position
- **Animation**: Smooth 300ms return animation

## Testing Checklist

### Basic Functionality
- [ ] **Simple drag**: Click card, drag it up/down (widget) or around grid (full view)
- [ ] **Card follows cursor**: Dragged card should smoothly follow mouse
- [ ] **Scale effect**: Dragged card should appear slightly larger (1.02x)
- [ ] **Card shifting**: Other cards should move to make space in real-time

### Reorder Mode (Green Border)
- [ ] **Normal drag**: Drag without Ctrl shows green border
- [ ] **Drop to reorder**: Release mouse to drop card in new position
- [ ] **Status message**: "Reordered N item(s)" appears after drop
- [ ] **Persistence**: Order saved automatically after drop

### Link Mode (Purple Border)
- [ ] **Ctrl+drag**: Hold Ctrl, border turns purple
- [ ] **Drop to link**: Drop on another card to create merged group
- [ ] **Merged card**: Cards become linked group with unified header
- [ ] **Status message**: "Linked N item(s)" appears after drop

### Mode Switching
- [ ] **Toggle during drag**: Press Ctrl mid-drag, border changes green → purple
- [ ] **Release Ctrl**: Release Ctrl mid-drag, border changes purple → green
- [ ] **Smooth animation**: Color transition should be smooth (150ms)

### Cancel Operations
- [ ] **Escape key**: Press Escape during drag
- [ ] **Return animation**: Card animates back to start position
- [ ] **No changes**: Card order/links unchanged

### Edge Cases
- [ ] **Merged card drag**: Drag merged group, all members move together
- [ ] **Fast dragging**: Rapid mouse movements don't break animation
- [ ] **Window resize**: Resize during drag doesn't crash
- [ ] **Empty space**: Drop in empty area works correctly
- [ ] **First/last position**: Can drag to very top or bottom
- [ ] **Grid wrapping**: (Full view) Cards wrap correctly in 2D grid

### Performance
- [ ] **Smooth 60fps**: No stuttering or jank during drag
- [ ] **Many cards**: Test with 20+ cards, should still be smooth
- [ ] **No memory leaks**: Drag multiple cards repeatedly, memory stable

## Layout Differences

### Widget View (Vertical StackPanel)
- Cards stack vertically
- Drag up/down to reorder
- Insertion calculated by Y position
- Simpler 1D layout logic

### Full View (Horizontal WrapPanel)
- Cards arranged in grid with wrapping
- Drag in 2D space to reorder
- Insertion calculated by X,Y position
- More complex 2D layout logic

## Keyboard Modifiers

| Key | Effect |
|-----|--------|
| **Ctrl** | Switch to link mode (purple border) |
| **Escape** | Cancel drag, return to original position |

## Visual Feedback Reference

| State | Visual |
|-------|--------|
| **Normal** | Default border, original size |
| **Dragging (Reorder)** | Green border (3px), scale 1.02x, Z-index 100 |
| **Dragging (Link)** | Purple border (3px), scale 1.02x, Z-index 100 |
| **Other cards shifting** | Translate Y (vertical) or X/Y (grid), 300ms animation |

## Known Limitations

1. **Old drag handlers**: Old drag event handlers in code-behind still exist but are not wired up
2. **Section handle drag**: Section handle split-drag still uses old system (intentional)
3. **Archive drag**: Drag to archive still uses old system (intentional)

## Implementation Files

- **Core Behavior**: `OrderLogFluidDragBehavior.cs` - Main drag logic
- **Animation Engine**: `CardShiftAnimator.cs` - Card shifting calculations
- **Widget Integration**: `OrderLogWidgetView.xaml` + `.xaml.cs`
- **Full View Integration**: `OrderLogView.xaml` + `.xaml.cs`

## Troubleshooting

### Cards don't shift
- Check behavior is attached in XAML
- Verify event handlers wired up in code-behind
- Check browser console for errors

### Border stays wrong color
- Ctrl key state might be stuck
- Release all keys and try again
- Escape to cancel and restart drag

### Performance issues
- Check card count (>100 might slow down)
- Verify GPU acceleration enabled
- Close other heavy applications

### Drag doesn't start
- Ensure minimum drag distance exceeded
- Check mouse button is held down
- Verify behavior attached to correct panel

## Future Enhancements

Potential improvements for future versions:
- [ ] Multi-select drag (drag multiple selected cards)
- [ ] Drag preview with card count badge
- [ ] Custom insertion line indicator
- [ ] Configurable animation speeds
- [ ] Sound effects on drop
- [ ] Undo/redo for drag operations
