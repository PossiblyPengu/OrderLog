using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Documents;
using System.Windows.Controls.Primitives;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using OrderLog.Features.Constants;
using OrderLog.Features.Models;
using OrderLog.Features.ViewModels;
using OrderLog.Features.Helpers;
using OrderLog.Services;

namespace OrderLog.Features.Views;

/// <summary>
/// Full-featured widget view for Order Log - designed for AppBar docking
/// </summary>
public partial class OrderLogWidgetView : UserControl
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private static void ApplySettingsDarkTitleBar(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            int dark = ThemeService.Instance.IsDarkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        catch { }
    }

    private bool _nowPlayingExpanded = false;
    private bool _notesExpanded = false;
    private bool _showingArchivedTab = false;
    private double _activeTabScrollPosition = 0;
    private double _archivedTabScrollPosition = 0;
    private SpotifyService? _spotifyService;
    private bool _progressRenderHooked;
    private System.Windows.Threading.DispatcherTimer? _equalizerTimer;
    private System.Windows.Threading.DispatcherTimer? _marqueeTimer;
    private Storyboard? _marqueeStoryboard;
    private bool _isMarqueeRunning = false;
    private string? _lastMarqueeTrack;
    private Random _random = new();
    private KeyboardShortcutManager? _keyboardShortcutManager;
    private bool _lastHasMedia = false; // For auto-hide fade tracking
    private string? _lastAlbumArtTrackKey; // For crossfade detection

    public OrderLogWidgetView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        InitializeEqualizerTimer();
        InitializeMarqueeTimer();
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // ESC cancels link mode first
            if (DataContext is OrderLogViewModel vm && vm.IsLinkMode)
            {
                vm.CancelLinkMode();
                e.Handled = true;
                return;
            }

            // ESC closes the inline Add Order card if it is open
            if (AddOrderCard?.Visibility == Visibility.Visible)
            {
                CancelAddOrder_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }

    private void AddOrderForm_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmAddOrder_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelAddOrder_Click(sender, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void ActiveTab_Click(object sender, RoutedEventArgs e)
    {
        // Save archived tab scroll position before switching
        if (_showingArchivedTab && MainScrollViewer != null)
        {
            _archivedTabScrollPosition = MainScrollViewer.VerticalOffset;
        }

        _showingArchivedTab = false;
        UpdateTabState();

        // Force refresh of active display items
        if (DataContext is OrderLogViewModel vm)
        {
            vm.RefreshDisplayItems();
        }

        // Restore active tab scroll position
        if (MainScrollViewer != null)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                MainScrollViewer.ScrollToVerticalOffset(_activeTabScrollPosition);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void ArchivedTab_Click(object sender, RoutedEventArgs e)
    {
        // Save active tab scroll position before switching
        if (!_showingArchivedTab && MainScrollViewer != null)
        {
            _activeTabScrollPosition = MainScrollViewer.VerticalOffset;
        }

        _showingArchivedTab = true;
        UpdateTabState();

        // Force refresh of archived display items
        if (DataContext is OrderLogViewModel vm)
        {
            _ = vm.RefreshArchivedDisplayItemsAsync();
        }

        // Restore archived tab scroll position
        if (MainScrollViewer != null)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                MainScrollViewer.ScrollToVerticalOffset(_archivedTabScrollPosition);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void UpdateTabState()
    {
        // Update tab button styles - using modern segmented control style
        if (_showingArchivedTab)
        {
            // Apply inactive style to Active tab
            ActiveTabButton.Style = FindResource("WidgetTabButtonStyle") as Style;
            // Apply active style to Archived tab
            ArchivedTabButton.Style = FindResource("WidgetTabButtonActiveStyle") as Style;

            // Animate tab transition — going right (Active → Archived)
            // Mirror direction when docked left so the slide always feels "inward"
            AnimateTabTransition(ActiveItemsPanel, ArchivedItemsPanel, goingRight: !_isDockedLeft);
            AddButtonsPanel.Visibility = Visibility.Collapsed;
            SideHandlePanel.Visibility = Visibility.Collapsed;
            QuickJumpContent.Visibility = Visibility.Collapsed;
            if (AddOrderCard != null) AddOrderCard.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Apply active style to Active tab
            ActiveTabButton.Style = FindResource("WidgetTabButtonActiveStyle") as Style;
            // Apply inactive style to Archived tab
            ArchivedTabButton.Style = FindResource("WidgetTabButtonStyle") as Style;

            // Animate tab transition — going left (Archived → Active)
            // Mirror direction when docked left so the slide always feels "inward"
            AnimateTabTransition(ArchivedItemsPanel, ActiveItemsPanel, goingRight: _isDockedLeft);
            AddButtonsPanel.Visibility = Visibility.Visible;
            SideHandlePanel.Visibility = Visibility.Visible;
        }
    }

    private void AnimateTabTransition(FrameworkElement outgoing, FrameworkElement incoming, bool goingRight)
    {
        const double slideOffset = 36.0;
        double exitX  = goingRight ? -slideOffset :  slideOffset;
        double enterX = goingRight ?  slideOffset : -slideOffset;

        var outTransform = outgoing.RenderTransform as TranslateTransform;
        var inTransform  = incoming.RenderTransform as TranslateTransform;

        // Reset incoming transform to the off-screen start position before revealing it
        if (inTransform != null) inTransform.X = enterX;

        // Make incoming panel visible but transparent so its layout is measured
        incoming.Visibility = Visibility.Visible;
        incoming.Opacity = 0;

        // --- Outgoing: slide out + fade out (130 ms, EaseIn) ---
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(130))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (s, _) =>
        {
            outgoing.Visibility = Visibility.Collapsed;
            outgoing.BeginAnimation(OpacityProperty, null);
            if (outTransform != null)
            {
                outTransform.BeginAnimation(TranslateTransform.XProperty, null);
                outTransform.X = 0;
            }
        };

        if (outTransform != null)
        {
            var slideOut = new DoubleAnimation(0, exitX, TimeSpan.FromMilliseconds(130))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            outTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
        }
        outgoing.BeginAnimation(OpacityProperty, fadeOut);

        // --- Incoming: slide in + fade in (210 ms, EaseOut) ---
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(210))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeIn.Completed += (s, _) =>
        {
            incoming.BeginAnimation(OpacityProperty, null);
            incoming.Opacity = 1;
            if (inTransform != null)
            {
                inTransform.BeginAnimation(TranslateTransform.XProperty, null);
                inTransform.X = 0;
            }
        };

        if (inTransform != null)
        {
            var slideIn = new DoubleAnimation(enterX, 0, TimeSpan.FromMilliseconds(210))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            inTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
        }
        incoming.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void NotesToggle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not OrderLogViewModel vm) return;
        if (vm.NotesOnlyMode) return; // Drawer is permanently open in notes-only mode
        bool opening = !vm.NotesExpanded;
        // Set guard BEFORE updating vm.NotesExpanded so that when PropertyChanged fires
        // UpdateNotesHeaderState sees _notesExpanded already in sync and returns early,
        // preventing an immediate Visibility = Collapsed that would kill the animation.
        _notesExpanded = opening;
        UpdateNotesToggleButtonState(opening);
        vm.NotesExpanded = opening;
        if (opening) OpenNotesDrawer(); else AnimateDrawerClose();
    }

    private void NotesDrawerClose_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrderLogViewModel vm && vm.NotesOnlyMode) return;
        CloseNotesDrawer(true);
    }

    private void NotesScrim_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is OrderLogViewModel vm && vm.NotesOnlyMode) return;
        CloseNotesDrawer(true);
    }

    private void OpenNotesDrawer()
    {
        if (NotesDrawerOverlay == null) return;
        double slideOffset = _isDockedLeft ? -(NotesDrawer?.ActualWidth > 0 ? NotesDrawer.ActualWidth : 600) : (NotesDrawer?.ActualWidth > 0 ? NotesDrawer.ActualWidth : 600);

        NotesDrawerOverlay.Visibility = Visibility.Visible;
        NotesDrawerTransform.X = slideOffset;
        if (SideHandlePanel != null) SideHandlePanel.Visibility = Visibility.Collapsed;

        var slideIn = new DoubleAnimation(slideOffset, 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeScrim = new DoubleAnimation(0, 0.7, TimeSpan.FromMilliseconds(200));
        NotesDrawerTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
        NotesScrim.BeginAnimation(OpacityProperty, fadeScrim);
    }

    private void CloseNotesDrawer(bool syncViewModel = false)
    {
        if (NotesDrawerOverlay == null) return;
        if (DataContext is OrderLogViewModel vm2 && vm2.NotesOnlyMode) return;

        // Set guard BEFORE triggering PropertyChanged so UpdateNotesHeaderState returns early
        // and does not immediately set Visibility = Collapsed, which would swallow the animation.
        _notesExpanded = false;
        UpdateNotesToggleButtonState(false);

        if (syncViewModel && DataContext is OrderLogViewModel vm)
        {
            vm.NotesExpanded = false; // PropertyChanged fires here, but guard prevents double-collapse
        }

        AnimateDrawerClose();
    }

    private void AnimateDrawerClose()
    {
        if (NotesDrawerOverlay == null || NotesDrawerOverlay.Visibility != Visibility.Visible) return;

        double slideOffset = _isDockedLeft
            ? -(NotesDrawer?.ActualWidth > 0 ? NotesDrawer.ActualWidth : 600)
            : (NotesDrawer?.ActualWidth > 0 ? NotesDrawer.ActualWidth : 600);

        var slideOut = new DoubleAnimation(0, slideOffset, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        slideOut.Completed += (s, _) =>
        {
            NotesDrawerOverlay.Visibility = Visibility.Collapsed;
            if (SideHandlePanel != null) SideHandlePanel.Visibility = Visibility.Visible;
        };
        var fadeScrim = new DoubleAnimation(0.7, 0, TimeSpan.FromMilliseconds(160));
        NotesDrawerTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
        NotesScrim.BeginAnimation(OpacityProperty, fadeScrim);
    }

    private void UpdateNotesToggleButtonState(bool isOpen)
    {
        // Hide the whole side handle when the drawer is open (drawer covers the widget anyway)
        if (SideHandlePanel != null)
            SideHandlePanel.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyNotesOnlyMode(bool isNotesOnly)
    {
        if (isNotesOnly)
        {
            // Force drawer open permanently: no animation, no scrim, hide close button and side handle
            _notesExpanded = true;
            if (NotesDrawerOverlay != null)
            {
                // Stop any running animations first
                NotesDrawerTransform.BeginAnimation(TranslateTransform.XProperty, null);
                NotesScrim.BeginAnimation(OpacityProperty, null);

                NotesDrawerOverlay.Visibility = Visibility.Visible;
                NotesDrawerTransform.X = 0;
                NotesScrim.Opacity = 0;
                NotesScrim.IsHitTestVisible = false;
            }
            if (SideHandlePanel != null)
                SideHandlePanel.Visibility = Visibility.Collapsed;
            if (NotesDrawerCloseButton != null)
                NotesDrawerCloseButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Restore normal drawer behavior
            if (NotesScrim != null)
                NotesScrim.IsHitTestVisible = true;
            if (NotesDrawerCloseButton != null)
                NotesDrawerCloseButton.Visibility = Visibility.Visible;

            // Sync drawer state with current NotesExpanded
            if (DataContext is OrderLogViewModel vm)
            {
                _notesExpanded = !vm.NotesExpanded; // Force UpdateNotesHeaderState to run
                UpdateNotesHeaderState(vm.NotesExpanded);
            }
        }
    }

    private void InitializeEqualizerTimer()
    {
        _equalizerTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _equalizerTimer.Tick += (s, e) => AnimateEqualizerBars();
    }

    private void InitializeMarqueeTimer()
    {
        // Timer to periodically check if marquee should be running
        _marqueeTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _marqueeTimer.Tick += MarqueeTimer_Tick;
    }

    private void MarqueeTimer_Tick(object? sender, EventArgs e)
    {
        // Only update marquee when collapsed and visible
        if (_nowPlayingExpanded || MarqueeContainer.Visibility != Visibility.Visible)
        {
            StopMarquee();
            return;
        }

        // Measure content width vs container width
        MarqueeContent.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double contentWidth = MarqueeContent.DesiredSize.Width;
        double containerWidth = MarqueeContainer.ActualWidth;

        // Wait for layout to give the container a valid width
        if (containerWidth <= 0) return;

        // Restart marquee when track changes
        var currentTrack = CollapsedTrackTitle.Text;
        if (_isMarqueeRunning && currentTrack != _lastMarqueeTrack)
        {
            StopMarquee();
        }
        _lastMarqueeTrack = currentTrack;

        if (contentWidth > containerWidth && !_isMarqueeRunning)
        {
            StartMarquee(contentWidth, containerWidth);
        }
        else if (contentWidth <= containerWidth && _isMarqueeRunning)
        {
            StopMarquee();
        }
    }

    private void StartMarquee(double contentWidth, double containerWidth)
    {
        if (_isMarqueeRunning) return;
        _isMarqueeRunning = true;

        // Calculate animation duration based on content width (pixels per second)
        double pixelsPerSecond = 40; // Adjust speed here
        double scrollDistance = contentWidth;
        double duration = scrollDistance / pixelsPerSecond;

        // Create the scroll animation
        _marqueeStoryboard = new Storyboard();

        var animation = new DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = RepeatBehavior.Forever
        };

        // Start at 0 (left edge)
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));

        // Pause briefly at start
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2))));

        // Scroll left (negative X) to show all content
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollDistance + containerWidth,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2 + duration))));

        // Pause briefly at end
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollDistance + containerWidth,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4 + duration))));

        // Quick reset to start
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0,
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.5 + duration))));

        Storyboard.SetTarget(animation, MarqueeTransform);
        Storyboard.SetTargetProperty(animation, new PropertyPath(TranslateTransform.XProperty));

        _marqueeStoryboard.Children.Add(animation);
        _marqueeStoryboard.Begin();
    }

    private void StopMarquee()
    {
        if (!_isMarqueeRunning) return;
        _isMarqueeRunning = false;

        _marqueeStoryboard?.Stop();
        _marqueeStoryboard = null;

        // Reset position
        if (MarqueeTransform != null)
        {
            MarqueeTransform.X = 0;
        }
    }

    private void NowPlayingContent_MouseEnter(object sender, MouseEventArgs e)
    {
        // Brighten the control bar on hover
        if (ControlBar == null) return;
        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        ControlBar.BeginAnimation(OpacityProperty, fadeIn);
    }

    private void NowPlayingContent_MouseLeave(object sender, MouseEventArgs e)
    {
        // Dim the control bar when not hovering
        if (ControlBar == null) return;
        var fadeOut = new DoubleAnimation(0.9, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        ControlBar.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void ProgressTrack_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Seek requires Web API which has been removed
    }

    private void AnimateEqualizerBars()
    {
        if (EqBar1 == null) return;

        // Animate each bar to a random height
        AnimateBar(EqBar1, 0.3 + _random.NextDouble() * 0.7);
        AnimateBar(EqBar2, 0.3 + _random.NextDouble() * 0.7);
        AnimateBar(EqBar3, 0.3 + _random.NextDouble() * 0.7);
    }

    private void AnimateBar(System.Windows.Shapes.Rectangle bar, double targetScale)
    {
        if (bar.RenderTransform is ScaleTransform scale)
        {
            var animation = new DoubleAnimation(targetScale, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Page load fade-in
        if (this.Content is UIElement root)
        {
            root.Opacity = 0;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            root.BeginAnimation(OpacityProperty, fadeIn);
        }

        // Initialize Spotify service asynchronously
        InitializeSpotifyAndWireUpAsync();

        // Initialize theme icon and subscribe to changes
        UpdateThemeIcon(ThemeService.Instance.IsDarkMode);
        ThemeService.Instance.ThemeChanged += OnThemeChanged;

        // Subscribe to ViewModel property changes for ShowNowPlaying
        if (DataContext is OrderLogViewModel viewModel)
        {
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            viewModel.ItemAdded += ViewModel_ItemAdded;

            // Initialize keyboard shortcuts
            _keyboardShortcutManager = new KeyboardShortcutManager(viewModel);
            _keyboardShortcutManager.RegisterShortcuts(this);

            // Wire up keyboard shortcut events
            _keyboardShortcutManager.SearchFocusRequested += FocusSearchBox;
            _keyboardShortcutManager.ScrollToTopRequested += ScrollToTop;
            _keyboardShortcutManager.ScrollToBottomRequested += ScrollToBottom;
            _keyboardShortcutManager.JumpToDialogRequested += ShowJumpDialog;
            _keyboardShortcutManager.HelpDialogRequested += ShowKeyboardHelp;

            // Subscribe to navigation changes
            viewModel.PropertyChanged += ViewModel_NavigationPropertyChanged;

            // Initialize notes header state (will be updated via PropertyChanged when settings load)
            UpdateNotesHeaderState(viewModel.NotesExpanded);

            // Apply notes-only mode if already enabled from saved settings
            if (viewModel.NotesOnlyMode)
                ApplyNotesOnlyMode(true);
        }
    }

    private void UpdateNotesHeaderState(bool isExpanded)
    {
        // Guard: if already in sync (e.g. CloseNotesDrawer already updated _notesExpanded),
        // don't interfere with an in-progress animation.
        if (_notesExpanded == isExpanded) return;
        _notesExpanded = isExpanded;
        UpdateNotesToggleButtonState(isExpanded);
        if (NotesDrawerOverlay != null)
        {
            if (isExpanded)
            {
                // Instant open on initial state restore (no entrance animation needed)
                NotesDrawerOverlay.Visibility = Visibility.Visible;
                NotesDrawerTransform.X = 0;
                NotesScrim.Opacity = 0.7;
            }
            else if (NotesDrawerOverlay.Visibility == Visibility.Visible)
            {
                // Drawer is open — animate it closed (covers ViewModel-side changes e.g. keyboard shortcuts)
                AnimateDrawerClose();
            }
            else
            {
                // Already collapsed (initial state) — no animation needed
                NotesDrawerOverlay.Visibility = Visibility.Collapsed;
            }
        }
    }

    private bool _isDockedLeft = false;

    public void ApplyDockSide(bool isDockedLeft)
    {
        _isDockedLeft = isDockedLeft;

        if (SideHandlePanel != null)
        {
            SideHandlePanel.HorizontalAlignment = isDockedLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;

            var cols = SideHandlePanel.ColumnDefinitions;
            if (cols.Count == 2)
            {
                // Left dock: col0 = handle strip, col1 = expanding content
                // Right dock: col0 = expanding content, col1 = handle strip
                cols[0].Width = GridLength.Auto;
                cols[1].Width = GridLength.Auto;
            }

            Grid.SetColumn(SideHandleStrip, isDockedLeft ? 0 : 1);
            Grid.SetColumn(QuickJumpContent, isDockedLeft ? 1 : 0);

            SideHandleStrip.CornerRadius = isDockedLeft
                ? new CornerRadius(0, 4, 4, 0)
                : new CornerRadius(4, 0, 0, 4);

            QuickJumpContent.CornerRadius = isDockedLeft
                ? new CornerRadius(0, 3, 3, 0)
                : new CornerRadius(3, 0, 0, 3);
            QuickJumpContent.BorderThickness = isDockedLeft
                ? new Thickness(0, 1, 1, 1)
                : new Thickness(1, 1, 0, 1);

            if (SideHandleSlideTransform != null)
            {
                SideHandleSlideTransform.X = isDockedLeft ? -20 : 20;
            }

            // Flip edge hints and hover zone to match dock side
            if (SideHandleEdgeHints != null)
            {
                SideHandleEdgeHints.HorizontalAlignment = isDockedLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                SideHandleEdgeHints.Margin = isDockedLeft ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
            }
            if (SideHandleHoverZone != null)
            {
                SideHandleHoverZone.HorizontalAlignment = isDockedLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            }

            // Flip QuickJumpContent slide-in origin to match dock side
            QuickJumpContent.RenderTransformOrigin = isDockedLeft ? new Point(0, 0.5) : new Point(1, 0.5);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrderLogViewModel.StatusMessage))
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (StatusMessageText == null) return;
                StatusMessageText.BeginAnimation(OpacityProperty, null);
                var flash = new DoubleAnimation(0.3, 1.0, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                StatusMessageText.BeginAnimation(OpacityProperty, flash);
            });
        }
        else if (e.PropertyName == nameof(OrderLogViewModel.ShowNowPlaying))
        {
            Dispatcher.Invoke(() => UpdateNowPlayingUI());
        }
        else if (e.PropertyName == nameof(OrderLogViewModel.NotesExpanded))
        {
            if (sender is OrderLogViewModel vm)
            {
                // In notes-only mode, the drawer is managed by ApplyNotesOnlyMode — skip normal handling
                if (!vm.NotesOnlyMode)
                    UpdateNotesHeaderState(vm.NotesExpanded);
            }
        }
        else if (e.PropertyName == nameof(OrderLogViewModel.NotesOnlyMode))
        {
            if (sender is OrderLogViewModel vm)
            {
                ApplyNotesOnlyMode(vm.NotesOnlyMode);
            }
        }
    }

    private void ViewModel_NavigationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrderLogViewModel.CurrentNavigationItem))
        {
            if (DataContext is OrderLogViewModel vm && vm.CurrentNavigationItem != null)
            {
                ScrollToItem(vm.CurrentNavigationItem);
            }
        }
    }

    private async void ViewModel_ItemAdded(OrderItem item)
    {
        // Sticky notes live in the drawer's own ScrollViewer – routing through
        // MainScrollViewer would throw/hang because the note element is not in that visual tree.
        if (item.NoteType == NoteType.StickyNote)
        {
            await FocusNewNoteInDrawerAsync();
            return;
        }

        await ScrollToAndFocusNewItemAsync(item);
    }

    private async Task FocusNewNoteInDrawerAsync()
    {
        // Give the ItemsControl time to render the new note
        await Task.Delay(80);

        await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                // NotesSection is the StackPanel inside the drawer's ScrollViewer.
                // Walk its visual tree to find the first editable text input.
                var rtb = FindVisualChild<RichTextBox>(NotesSection);
                if (rtb != null)
                {
                    rtb.Focus();
                    return;
                }

                var tb = FindVisualChild<TextBox>(NotesSection);
                tb?.Focus();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to focus new note in drawer");
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async void InitializeSpotifyAndWireUpAsync()
    {
        try
        {
            // Initialize Spotify service
            await InitializeSpotifyAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize Spotify service");
        }

        // Ensure the main ScrollViewer receives mouse wheel even when children mark events handled
        SetupMouseWheelHandling();
    }

    private void SetupMouseWheelHandling()
    {
        try
        {
            if (MainScrollViewer != null)
            {
                MouseWheelEventHandler handler = (s, ev) =>
                {
                    try
                    {
                        // If Now Playing is expanded, collapse it when the user starts scrolling
                        if (_nowPlayingExpanded)
                        {
                            SetNowPlayingExpanded(false);
                        }

                        double newOffset = MainScrollViewer.VerticalOffset - (ev.Delta / 3.0);
                        newOffset = Math.Max(0, Math.Min(newOffset, MainScrollViewer.ExtentHeight - MainScrollViewer.ViewportHeight));
                        MainScrollViewer.ScrollToVerticalOffset(newOffset);
                        ev.Handled = true;
                    }
                    catch (Exception ex) { Log.Debug(ex, "Scroll handling fallback"); }
                };

                // Attach both preview and bubbling with handledEventsToo
                MainScrollViewer.AddHandler(UIElement.PreviewMouseWheelEvent, handler, true);
                MainScrollViewer.AddHandler(UIElement.MouseWheelEvent, handler, true);
                // Also collapse NowPlaying when the scrollviewer content is changed (e.g., scrollbar drag)
                MainScrollViewer.ScrollChanged += (s, ev) =>
                {
                    try
                    {
                        if (ev.VerticalChange != 0 && _nowPlayingExpanded)
                        {
                            SetNowPlayingExpanded(false);
                        }
                    }
                    catch { }
                };

                // Collapse NowPlaying when the user clicks anything behind/outside it
                MainScrollViewer.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
                    new MouseButtonEventHandler((s, ev) =>
                    {
                        try
                        {
                            if (!_nowPlayingExpanded) return;
                            // Only collapse if the click is outside the NowPlayingSection
                            if (NowPlayingSection != null && NowPlayingSection.IsMouseOver) return;
                            SetNowPlayingExpanded(false);
                        }
                        catch { }
                    }), true);
            }
        }
        catch (Exception ex) { Log.Debug(ex, "Optional scroll enhancement setup failed"); }
    }

    private void SetNowPlayingExpanded(bool expanded)
    {
        // Ensure this runs on UI thread
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetNowPlayingExpanded(expanded));
            return;
        }

        _nowPlayingExpanded = expanded;
        NowPlayingToggleIcon.Text = _nowPlayingExpanded ? "▼" : "▲";

        double targetHeight = Math.Min(Math.Max(this.ActualWidth * 0.88, 200), 300);

        if (_nowPlayingExpanded)
        {
            NowPlayingContent.Visibility = Visibility.Visible;
            NowPlayingContent.BeginAnimation(HeightProperty, null);
            var expandAnimation = new DoubleAnimation(0, targetHeight, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            expandAnimation.Completed += (_, _) =>
            {
                NowPlayingContent.BeginAnimation(HeightProperty, null);
                NowPlayingContent.Height = double.NaN;
            };
            NowPlayingContent.BeginAnimation(HeightProperty, expandAnimation);
        }
        else
        {
            var currentHeight = NowPlayingContent.ActualHeight;
            var collapseAnimation = new DoubleAnimation(currentHeight, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            collapseAnimation.Completed += (s, _) =>
            {
                NowPlayingContent.Visibility = Visibility.Collapsed;
                NowPlayingContent.BeginAnimation(HeightProperty, null);
            };
            NowPlayingContent.BeginAnimation(HeightProperty, collapseAnimation);
        }
        UpdateNowPlayingUI();
    }

    private void Card_MouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is not OrderLogViewModel vm || !vm.IsLinkMode) return;
        if (sender is not Border border) return;

        // Get the item from this card
        var item = border.DataContext switch
        {
            OrderItem oi => oi,
            ViewModels.OrderItemGroup group => group.First,
            _ => null
        };

        // Don't highlight the source card (it already has a blue border)
        if (item != null && vm.LinkModeSource != null && item.Id != vm.LinkModeSource.Id)
        {
            // Green border for hover target
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(34, 197, 94)); // Tailwind green-500
            border.BorderThickness = new Thickness(2);
        }
    }

    private void Card_MouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is not OrderLogViewModel vm) return;
        if (sender is not Border border) return;

        // Get the item from this card
        var item = border.DataContext switch
        {
            OrderItem oi => oi,
            ViewModels.OrderItemGroup group => group.First,
            _ => null
        };

        // Don't clear if this is the source card (keep blue border)
        if (item != null && vm.LinkModeSource != null && item.Id == vm.LinkModeSource.Id)
        {
            return;
        }

        // Clear hover highlight
        border.ClearValue(Border.BorderBrushProperty);
        border.ClearValue(Border.BorderThicknessProperty);
    }

    private async void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not OrderLogViewModel vm || !vm.IsLinkMode) return;

        // Get the order item from the clicked card
        OrderItem? targetItem = null;
        if (sender is FrameworkElement fe)
        {
            targetItem = fe.DataContext switch
            {
                OrderItem item => item,
                ViewModels.OrderItemGroup group => group.First,
                _ => null
            };
        }

        if (targetItem != null)
        {
            e.Handled = true;
            await vm.CompleteLinkModeAsync(targetItem);
        }
    }

    private void Card_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is OrderLogViewModel vm && vm.IsLinkMode && e.Key == Key.Escape)
        {
            vm.CancelLinkMode();
            e.Handled = true;
        }
    }

    private void ArchivedSortToggle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not OrderLogViewModel vm) return;
        vm.CycleArchivedSortMode();
    }

    /// <summary>
    /// Resolves an OrderItem from a context menu sender, handling WPF's ContextMenu DataContext inheritance issues.
    /// </summary>
    private static OrderItem? GetOrderItemFromContextMenu(object sender)
    {
        if (sender is not MenuItem menuItem) return null;

        // Try direct DataContext first
        if (menuItem.DataContext is OrderItem order)
            return order;

        // For nested menu items, walk up to the ContextMenu and get PlacementTarget's DataContext
        DependencyObject? current = menuItem;
        while (current != null)
        {
            if (current is ContextMenu cm && cm.PlacementTarget is FrameworkElement pt)
                return pt.DataContext as OrderItem;
            current = LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
                return typedChild;

            var childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null)
                return childOfChild;
        }
        return null;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Clean up event subscriptions
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;

        _equalizerTimer?.Stop();
        _marqueeTimer?.Stop();
        StopMarquee();

        if (_spotifyService != null)
        {
            _spotifyService.PropertyChanged -= SpotifyService_PropertyChanged;
        }

        // Unhook progress render callback
        if (_progressRenderHooked)
        {
            System.Windows.Media.CompositionTarget.Rendering -= OnProgressRenderFrame;
            _progressRenderHooked = false;
        }

        // Unsubscribe from ViewModel property changes
        if (DataContext is OrderLogViewModel viewModel)
        {
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            viewModel.PropertyChanged -= ViewModel_NavigationPropertyChanged;
            viewModel.ItemAdded -= ViewModel_ItemAdded;
        }

        // Cleanup keyboard shortcuts
        if (_keyboardShortcutManager != null)
        {
            _keyboardShortcutManager.SearchFocusRequested -= FocusSearchBox;
            _keyboardShortcutManager.ScrollToTopRequested -= ScrollToTop;
            _keyboardShortcutManager.ScrollToBottomRequested -= ScrollToBottom;
            _keyboardShortcutManager.JumpToDialogRequested -= ShowJumpDialog;
            _keyboardShortcutManager.HelpDialogRequested -= ShowKeyboardHelp;
            _keyboardShortcutManager.UnregisterShortcuts();
            _keyboardShortcutManager = null;
        }

        // Unsubscribe from theme changes
        ThemeService.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeService.Instance.ToggleTheme();
    }

    private void ExportImportMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not OrderLogViewModel vm) return;

            var settingsView = new OrderLogSettingsView { DataContext = vm };
            var settingsWindow = new Window
            {
                Title = "OrderLog Settings",
                Content = settingsView,
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 450,
                MinHeight = 500,
                MaxHeight = 700,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize,
            };

            // Apply theme resources to the settings window
            foreach (var dict in Resources.MergedDictionaries)
            {
                settingsWindow.Resources.MergedDictionaries.Add(dict);
            }
            settingsWindow.Background = (System.Windows.Media.Brush)FindResource("BackgroundBrush");
            ApplySettingsDarkTitleBar(settingsWindow);

            settingsWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open settings window");
        }
    }

    private void ShowFilters_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not OrderLogViewModel viewModel)
                return;

            var dialog = new OrderLogFilterDialog(
                viewModel.StatusFilters,
                viewModel.FilterStartDate,
                viewModel.FilterEndDate,
                viewModel.NoteTypeFilter,
                viewModel.NoteCategoryFilter)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                // Apply filters from dialog to ViewModel
                viewModel.StatusFilters = dialog.SelectedStatuses;
                viewModel.FilterStartDate = dialog.StartDate;
                viewModel.FilterEndDate = dialog.EndDate;
                viewModel.NoteTypeFilter = dialog.SelectedNoteType;
                viewModel.NoteCategoryFilter = dialog.SelectedNoteCategory;

                // Update status message
                var filterCount = 0;
                if (dialog.SelectedStatuses?.Length > 0) filterCount++;
                if (dialog.StartDate.HasValue || dialog.EndDate.HasValue) filterCount++;
                if (dialog.SelectedNoteType.HasValue) filterCount++;
                if (dialog.SelectedNoteCategory.HasValue) filterCount++;

                if (filterCount > 0)
                {
                    viewModel.StatusMessage = $"{filterCount} filter{(filterCount > 1 ? "s" : "")} applied";
                }
                else
                {
                    viewModel.StatusMessage = "Filters cleared";
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to show filter dialog");
        }
    }

    private void OnThemeChanged(object? sender, bool isDarkMode)
    {
        Dispatcher.Invoke(() => UpdateThemeIcon(isDarkMode));
    }


    private void UpdateThemeIcon(bool isDarkMode)
    {
        // Half-circle contrast icon works for both themes — no update needed
    }

    private async Task InitializeSpotifyAsync()
    {
        try
        {
            _spotifyService = SpotifyService.Instance;
            await _spotifyService.InitializeAsync();
            _spotifyService.PropertyChanged += SpotifyService_PropertyChanged;
            HistoryList.ItemsSource = _spotifyService.RecentTracks;
            UpdateNowPlayingUI();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize Spotify service in widget");
        }
    }

    private void SpotifyService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SpotifyService.TrackPosition) or nameof(SpotifyService.TrackDuration))
        {
            Dispatcher.BeginInvoke(UpdateProgressBar);
            return;
        }
        Dispatcher.BeginInvoke(UpdateNowPlayingUI);
    }

    private void UpdateNowPlayingUI()
    {
        if (_spotifyService == null)
        {
            // When spotify service isn't initialized we don't show the header by default
            try
            {
                NowPlayingHeaderText.Visibility = Visibility.Collapsed;
                MarqueeContainer.Visibility = Visibility.Collapsed;
            }
            catch { }
            return;
        }

        // Auto-hide with smooth fade when nothing is playing
        var viewModel = DataContext as OrderLogViewModel;
        var showNowPlaying = viewModel?.ShowNowPlaying ?? true;
        bool shouldBeVisible = showNowPlaying && _spotifyService.HasMedia;

        if (shouldBeVisible && !_lastHasMedia)
        {
            // Fade in
            NowPlayingSection.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            NowPlayingSection.BeginAnimation(OpacityProperty, fadeIn);
        }
        else if (!shouldBeVisible && _lastHasMedia)
        {
            // Fade out then collapse
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, _) =>
            {
                // Guard: if media returned while fade-out was running, don't collapse
                var vm2 = DataContext as OrderLogViewModel;
                bool stillHidden = !(vm2?.ShowNowPlaying == true && _spotifyService?.HasMedia == true);
                if (stillHidden)
                {
                    NowPlayingSection.Visibility = Visibility.Collapsed;
                    NowPlayingSection.BeginAnimation(OpacityProperty, null);
                }
            };
            NowPlayingSection.BeginAnimation(OpacityProperty, fadeOut);
        }
        else if (!shouldBeVisible)
        {
            // Abort any in-progress fade-out so its Completed callback never fires
            NowPlayingSection.BeginAnimation(OpacityProperty, null);
            NowPlayingSection.Visibility = Visibility.Collapsed;
        }
        _lastHasMedia = shouldBeVisible;

        if (!_spotifyService.HasMedia) return;

        TrackTitleText.Text = _spotifyService.TrackTitle;
        ArtistNameText.Text = _spotifyService.ArtistName;
        PlayPauseButton.Content = _spotifyService.IsPlaying ? "⏸" : "▶";
        try { MiniPlayPauseButton.Content = _spotifyService.IsPlaying ? "⏸" : "▶"; } catch { }

        // Hide API-only UI elements (Web API removed)
        try
        {
            LikeButton.Visibility = Visibility.Collapsed;
            VolumeDownButton.Visibility = Visibility.Collapsed;
            VolumeUpButton.Visibility = Visibility.Collapsed;
            HistoryButton.Visibility = Visibility.Collapsed;
            ShuffleIndicatorWrapper.Visibility = Visibility.Collapsed;
            RepeatIndicatorWrapper.Visibility = Visibility.Collapsed;
            AlbumDeviceText.Visibility = Visibility.Collapsed;
        }
        catch { }

        // Album art crossfade
        var currentArtKey = $"{_spotifyService.TrackTitle}|{_spotifyService.ArtistName}";
        if (_spotifyService.AlbumArt != null && currentArtKey != _lastAlbumArtTrackKey)
        {
            // Crossfade: move current to old, set new, animate
            AlbumArtImageOld.Source = AlbumArtImage.Source;
            AlbumArtImageOld.Opacity = 1;
            AlbumArtImage.Source = _spotifyService.AlbumArt;
            AlbumArtImage.Opacity = 0;

            var fadeInNew = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var fadeOutOld = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(500))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            AlbumArtImage.BeginAnimation(OpacityProperty, fadeInNew);
            AlbumArtImageOld.BeginAnimation(OpacityProperty, fadeOutOld);

            _lastAlbumArtTrackKey = currentArtKey;
        }
        else if (_spotifyService.AlbumArt != null && AlbumArtImage.Source != _spotifyService.AlbumArt)
        {
            AlbumArtImage.Source = _spotifyService.AlbumArt;
            AlbumArtImage.Opacity = 1;
        }
        else if (_spotifyService.AlbumArt == null)
        {
            AlbumArtImage.Source = null;
            AlbumArtImage.Opacity = 1;
        }

        AlbumArtBlurredBg.Source = _spotifyService.AlbumArt;

        // Sync mini album art thumbnail
        try { MiniAlbumArt.Source = _spotifyService.AlbumArt; } catch { }

        // Show/hide album art placeholder
        AlbumArtPlaceholder.Visibility = _spotifyService.AlbumArt == null
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Dominant color tint
        try
        {
            var dominantColor = _spotifyService.DominantColor;
            if (dominantColor != System.Windows.Media.Colors.Transparent)
            {
                DominantColorBg.Background = new SolidColorBrush(dominantColor);
                MiniBarAccentStripe.Background = new SolidColorBrush(dominantColor);
            }
        }
        catch { }

        // Progress bar
        UpdateProgressBar();

        // Shuffle/Repeat indicators
        UpdateShuffleRepeatIndicators();

        // Like button state
        LikeButton.Content = _spotifyService.IsCurrentTrackLiked ? "♥" : "♡";
        LikeButton.Foreground = _spotifyService.IsCurrentTrackLiked
            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 80, 80))
            : (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");

        // Control equalizer animation
        if (_spotifyService.IsPlaying)
        {
            EqualizerPanel.Visibility = Visibility.Visible;
            MusicIcon.Visibility = Visibility.Collapsed;
            _equalizerTimer?.Start();
        }
        else
        {
            _equalizerTimer?.Stop();
            EqualizerPanel.Visibility = Visibility.Collapsed;
            MusicIcon.Visibility = Visibility.Visible;
        }

        // Update header and collapsed view based on expand state
        var track = _spotifyService.TrackTitle ?? "";
        var artist = _spotifyService.ArtistName ?? "";

        if (_nowPlayingExpanded)
        {
            // Expanded: show "Now Playing" label, hide marquee
            NowPlayingHeaderText.Visibility = Visibility.Visible;
            NowPlayingHeaderText.Text = "Now Playing";
            MarqueeContainer.Visibility = Visibility.Collapsed;
            StopMarquee();
            _marqueeTimer?.Stop();
            // Ensure the expanded content is visible/animated in case an update collapsed it
            try
            {
                if (NowPlayingContent.Visibility != Visibility.Visible)
                {
                    NowPlayingContent.Visibility = Visibility.Visible;
                    NowPlayingContent.BeginAnimation(HeightProperty, null);
                    double targetHeight = Math.Min(Math.Max(this.ActualWidth * 0.75, 220), 300);
                    var expandAnimation = new DoubleAnimation(0, targetHeight, TimeSpan.FromMilliseconds(200))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                    };
                    NowPlayingContent.BeginAnimation(HeightProperty, expandAnimation);
                }
            }
            catch { }
        }
        else
        {
            // Collapsed: hide the static "Now Playing" label; show marquee if a track exists
            NowPlayingHeaderText.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrEmpty(track))
            {
                MarqueeContainer.Visibility = Visibility.Visible;
                CollapsedTrackTitle.Text = track;
                CollapsedArtistName.Text = artist;
                _marqueeTimer?.Start();
            }
            else
            {
                // No track playing, hide marquee as well
                MarqueeContainer.Visibility = Visibility.Collapsed;
                _marqueeTimer?.Stop();
            }
        }
    }

    private void UpdateProgressBar()
    {
        if (_spotifyService == null) return;
        try
        {
            TrackDurationText.Text = FormatTimeSpan(_spotifyService.TrackDuration);

            // Hook/unhook the per-frame render callback based on playback state
            if (_spotifyService.IsPlaying && _spotifyService.TrackDuration.TotalSeconds > 0)
            {
                if (!_progressRenderHooked)
                {
                    System.Windows.Media.CompositionTarget.Rendering += OnProgressRenderFrame;
                    _progressRenderHooked = true;
                }
            }
            else
            {
                if (_progressRenderHooked)
                {
                    System.Windows.Media.CompositionTarget.Rendering -= OnProgressRenderFrame;
                    _progressRenderHooked = false;
                }
                // Set final position when paused
                UpdateProgressBarWidth(_spotifyService.TrackPosition, _spotifyService.TrackDuration);
            }
        }
        catch { }
    }

    private void OnProgressRenderFrame(object? sender, EventArgs e)
    {
        if (_spotifyService == null || !_spotifyService.IsPlaying) return;
        try
        {
            var pos = _spotifyService.InterpolatedPosition;
            var dur = _spotifyService.TrackDuration;
            UpdateProgressBarWidth(pos, dur);
        }
        catch { }
    }

    private void UpdateProgressBarWidth(TimeSpan pos, TimeSpan dur)
    {
        TrackPositionText.Text = FormatTimeSpan(pos);

        if (dur.TotalSeconds <= 0)
        {
            ProgressBarFill.Width = 0;
            return;
        }

        double ratio = Math.Clamp(pos.TotalSeconds / dur.TotalSeconds, 0, 1);

        var parent = ProgressBarFill.Parent as Grid;
        if (parent != null && parent.ActualWidth > 0)
            ProgressBarFill.Width = parent.ActualWidth * ratio;

        try
        {
            var miniParent = MiniProgressBarFill?.Parent as Grid;
            if (MiniProgressBarFill != null && miniParent != null && miniParent.ActualWidth > 0)
                MiniProgressBarFill.Width = miniParent.ActualWidth * ratio;
        }
        catch { }
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    private void UpdateShuffleRepeatIndicators()
    {
        if (_spotifyService == null) return;
        try
        {
            // Shuffle
            ShuffleIndicator.Opacity = _spotifyService.IsShuffleEnabled ? 1.0 : 0.3;
            ShuffleIndicator.Foreground = _spotifyService.IsShuffleEnabled
                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                : (System.Windows.Media.Brush)FindResource("TextTertiaryBrush");
            ShuffleIndicator.ToolTip = _spotifyService.IsShuffleEnabled ? "Shuffle On" : "Shuffle Off";

            // Repeat
            RepeatIndicator.Opacity = _spotifyService.RepeatMode > 0 ? 1.0 : 0.3;
            RepeatIndicator.Foreground = _spotifyService.RepeatMode > 0
                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                : (System.Windows.Media.Brush)FindResource("TextTertiaryBrush");
            RepeatIndicator.Text = _spotifyService.RepeatMode == 1 ? "🔂" : "🔁";
            RepeatIndicator.ToolTip = _spotifyService.RepeatMode switch
            {
                1 => "Repeat Track",
                2 => "Repeat All",
                _ => "Repeat Off"
            };
        }
        catch { }
    }

    private void NowPlayingHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            // Double-click opens Spotify
            OpenSpotify();
            return;
        }

        _nowPlayingExpanded = !_nowPlayingExpanded;
        NowPlayingToggleIcon.Text = _nowPlayingExpanded ? "▼" : "▲";

        // Calculate target height based on widget width (for square-ish album art)
        double targetHeight = Math.Min(Math.Max(this.ActualWidth * 0.88, 200), 300);

        // Animated expand/collapse
        if (_nowPlayingExpanded)
        {
            NowPlayingContent.Visibility = Visibility.Visible;
            NowPlayingContent.BeginAnimation(HeightProperty, null); // Clear previous animation
            var expandAnimation = new DoubleAnimation(0, targetHeight, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            expandAnimation.Completed += (_, _) =>
            {
                NowPlayingContent.BeginAnimation(HeightProperty, null);
                NowPlayingContent.Height = double.NaN;
            };
            NowPlayingContent.BeginAnimation(HeightProperty, expandAnimation);
        }
        else
        {
            var currentHeight = NowPlayingContent.ActualHeight;
            var collapseAnimation = new DoubleAnimation(currentHeight, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            collapseAnimation.Completed += (s, _) =>
            {
                NowPlayingContent.Visibility = Visibility.Collapsed;
                NowPlayingContent.BeginAnimation(HeightProperty, null); // Clear animation to allow auto-sizing
            };
            NowPlayingContent.BeginAnimation(HeightProperty, collapseAnimation);
        }
        UpdateNowPlayingUI();
    }

    private System.Windows.Point _dragStartPoint;

    private void Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            _dragStartPoint = e.GetPosition(null);
        }
    }

    private void Item_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (sender is FrameworkElement fe && fe.DataContext is OrderItem order)
        {
            if (DataContext is OrderLogViewModel vm && vm.SelectedItems.Count > 1 && vm.SelectedItems.Contains(order))
            {
                var ids = vm.SelectedItems.Select(i => i.Id).ToArray();
                var data = new DataObject();
                data.SetData("OrderItemIds", ids);
                DragDrop.DoDragDrop(fe, data, DragDropEffects.Move);
            }
            else
            {
                var data = new DataObject("OrderItemId", order.Id);
                DragDrop.DoDragDrop(fe, data, DragDropEffects.Move);
            }
        }
    }

    private void Item_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("OrderItemId"))
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        if (sender is Border b)
        {
            if (b.Tag == null) b.Tag = b.BorderBrush;
            b.BorderBrush = Application.Current?.Resources["SuccessBrush"] as Brush ?? System.Windows.Media.Brushes.LightGreen;
        }
        e.Handled = true;
    }

    private void Item_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border b && b.Tag is System.Windows.Media.Brush orig)
        {
            b.BorderBrush = orig;
            b.Tag = null;
        }
    }

    private async void Item_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.Data.GetDataPresent("OrderItemId") && !e.Data.GetDataPresent("OrderItemIds")) return;

            var droppedIds = new System.Collections.Generic.List<Guid>();
            if (e.Data.GetDataPresent("OrderItemIds") && e.Data.GetData("OrderItemIds") is Guid[] arr)
            {
                droppedIds.AddRange(arr);
            }
            else if (e.Data.GetDataPresent("OrderItemId"))
            {
                droppedIds.Add((Guid)e.Data.GetData("OrderItemId"));
            }

            if (DataContext is OrderLogViewModel vm)
            {
                var droppedItems = vm.Items.Concat(vm.ArchivedItems).Where(i => droppedIds.Contains(i.Id)).ToList();

                // Check if this is a split-drag (dragging from section handle to unlink)
                bool isSplitDrag = e.Data.GetDataPresent("SplitFromGroup") && e.Data.GetData("SplitFromGroup") is bool split && split;

                // If split-drag, unlink the dragged item
                if (isSplitDrag && droppedItems.Count == 1)
                {
                    droppedItems[0].LinkedGroupId = null;
                }

                OrderItem? target = null;
                if (sender is FrameworkElement fe && fe.DataContext is OrderItem ti) target = ti;
                if (droppedItems.Count > 0)
                {
                    // Only link when Ctrl is held; otherwise move the items.
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    {
                        // If the drop target is a practically-empty placeholder, attempt to find a nearby non-empty replacement.
                        if (target == null || target.IsPracticallyEmpty)
                        {
                            try
                            {
                                // Try to find nearest non-empty target in the active items panel based on drop position
                                var panel = ActiveItemsPanel as Panel;
                                if (panel != null)
                                {
                                    var mousePos = e.GetPosition(panel);
                                    OrderItem? replacement = null;
                                    double best = double.MaxValue;

                                    foreach (var panelChild in panel.Children.OfType<FrameworkElement>())
                                    {
                                        if (panelChild.Visibility != Visibility.Visible) continue;
                                        var border = FindVisualChild<Border>(panelChild);
                                        if (border == null) continue;
                                        OrderItem? oi = border.DataContext as OrderItem;
                                        if (oi == null)
                                        {
                                            if (border.DataContext is ViewModels.OrderItemGroup grp && grp.Members?.Count > 0) oi = grp.First;
                                            if (oi == null) continue;
                                        }

                                        if (oi.IsPracticallyEmpty) continue;

                                        var bounds = new Rect(border.TransformToAncestor(panel).Transform(new Point(0, 0)), new Size(border.ActualWidth, border.ActualHeight));
                                        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                                        var dist = (center - mousePos).Length;
                                        if (dist < best)
                                        {
                                            best = dist;
                                            replacement = oi;
                                        }
                                    }

                                    if (replacement != null)
                                    {
                                        target = replacement;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "Error finding nearest non-empty target");
                            }

                            if (target == null || target.IsPracticallyEmpty)
                            {
                                vm.StatusMessage = "Cannot link to an empty placeholder";
                                return;
                            }
                        }

                        Log.Debug("Widget.Item_Drop: dropped={DroppedIds} target={TargetId}:{TargetVendor}",
                            string.Join(',', droppedItems.Select(i => i.Id)),
                            target?.Id,
                            target?.VendorName ?? "<no-vendor>");

                        await vm.LinkItemsAsync(droppedItems, target);
                        vm.StatusMessage = "Linked items";
                    }
                    else
                    {
                        await vm.MoveOrdersAsync(droppedItems, target);
                        if (isSplitDrag)
                        {
                            vm.StatusMessage = "Split and moved order";
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Widget drop failed");
        }
    }

    private void OpenSpotify()
    {
        try
        {
            var processes = System.Diagnostics.Process.GetProcessesByName("Spotify");
            if (processes.Length > 0)
            {
                // Spotify is running, bring to foreground
                var hWnd = processes[0].MainWindowHandle;
                if (hWnd != IntPtr.Zero)
                {
                    OrderLog.Helpers.NativeMethods.SetForegroundWindow(hWnd);
                    OrderLog.Helpers.NativeMethods.ShowWindow(hWnd, OrderLog.Helpers.NativeMethods.ShowWindowCommands.SW_RESTORE);
                }
            }
            else
            {
                // Launch Spotify
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "spotify:",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open Spotify");
        }
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_spotifyService != null)
        {
            await _spotifyService.PlayPauseAsync();
        }
    }

    private async void NextTrack_Click(object sender, RoutedEventArgs e)
    {
        if (_spotifyService != null)
        {
            await _spotifyService.NextTrackAsync();
        }
    }

    private async void PrevTrack_Click(object sender, RoutedEventArgs e)
    {
        if (_spotifyService != null)
        {
            await _spotifyService.PreviousTrackAsync();
        }
    }

    private async void VolumeUp_Click(object sender, RoutedEventArgs e)
    {
        if (_spotifyService != null)
        {
            await _spotifyService.VolumeUpAsync();
        }
    }

    private async void VolumeDown_Click(object sender, RoutedEventArgs e)
    {
        if (_spotifyService != null)
        {
            await _spotifyService.VolumeDownAsync();
        }
    }

    private void LikeTrack_Click(object sender, RoutedEventArgs e)
    {
        _spotifyService?.ToggleLikeCurrentTrack();
        UpdateNowPlayingUI();
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryPopup != null)
        {
            HistoryPopup.IsOpen = !HistoryPopup.IsOpen;
        }
    }


    private void AddBlankOrder_Click(object sender, RoutedEventArgs e)
    {
        Log.Debug("AddBlankOrder_Click fired");

        // Show inline add order card with animation
        if (AddOrderCard != null)
        {
            // Clear form fields
            if (InlineVendorNameBox != null) InlineVendorNameBox.Text = string.Empty;
            if (InlineTransferNumbersBox != null) InlineTransferNumbersBox.Text = string.Empty;
            if (InlineWhsShipmentNumbersBox != null) InlineWhsShipmentNumbersBox.Text = string.Empty;
            if (InlineStatusComboBox != null) InlineStatusComboBox.SelectedValue = Models.OrderItem.OrderStatus.NotReady;

            // Animate card expansion
            AddOrderCard.Visibility = Visibility.Visible;
            AddOrderCard.Opacity = 0;
            AddOrderCard.RenderTransform = new ScaleTransform(0.95, 0.95);
            AddOrderCard.RenderTransformOrigin = new Point(0.5, 0);

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var scaleX = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var scaleY = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            AddOrderCard.BeginAnimation(OpacityProperty, fadeIn);
            if (AddOrderCard.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
            }

            // Focus vendor name field after animation starts
            _ = Dispatcher.BeginInvoke(new Action(() => InlineVendorNameBox?.Focus()),
                System.Windows.Threading.DispatcherPriority.Input);

            // Scroll to top to show the card
            MainScrollViewer?.ScrollToTop();
        }
    }

    private void CancelAddOrder_Click(object sender, RoutedEventArgs e)
    {
        // Hide the inline add order card with animation
        if (AddOrderCard != null)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var scaleX = new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var scaleY = new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOut.Completed += (s, _) =>
            {
                AddOrderCard.Visibility = Visibility.Collapsed;
                AddOrderCard.BeginAnimation(OpacityProperty, null);
            };

            AddOrderCard.BeginAnimation(OpacityProperty, fadeOut);
            if (AddOrderCard.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
            }
        }
    }

    private async void ConfirmAddOrder_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not OrderLogViewModel vm) return;

        var vendorName = InlineVendorNameBox?.Text?.Trim();

        if (string.IsNullOrEmpty(vendorName))
        {
            MessageBox.Show("Please enter a vendor name.", "Vendor Name Required",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            InlineVendorNameBox?.Focus();
            return;
        }

        var status = InlineStatusComboBox?.SelectedValue is Models.OrderItem.OrderStatus selectedStatus
            ? selectedStatus
            : Models.OrderItem.OrderStatus.NotReady;

        var order = Models.OrderItem.CreateBlankOrder(vendorName, isPlaceholder: false);
        order.TransferNumbers = InlineTransferNumbersBox?.Text?.Trim() ?? string.Empty;
        order.WhsShipmentNumbers = InlineWhsShipmentNumbersBox?.Text?.Trim() ?? string.Empty;
        order.Status = status;

        await vm.AddOrderAsync(order);

        // Hide the card with animation and scroll to top to show the new order
        if (AddOrderCard != null)
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var scaleX = new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var scaleY = new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOut.Completed += (s, _) =>
            {
                AddOrderCard.Visibility = Visibility.Collapsed;
                AddOrderCard.BeginAnimation(OpacityProperty, null);
            };

            AddOrderCard.BeginAnimation(OpacityProperty, fadeOut);
            if (AddOrderCard.RenderTransform is ScaleTransform scale)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
            }
        }

        MainScrollViewer?.ScrollToTop();
    }

    private void AddBlankNote_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OrderLogViewModel vm)
        {
            _ = AddBlankNoteAsync(vm);
        }
    }

    private async Task AddBlankNoteAsync(OrderLogViewModel vm)
    {
        var note = OrderItem.CreateBlankNote();
        await vm.AddOrderAsync(note);
        // Autofocus is handled by ItemAdded event
    }

    private async Task ScrollToAndFocusNewItemAsync(OrderItem item)
    {
        // Wait for UI to update
        await Task.Delay(50);

        // Scroll to top where new items appear
        MainScrollViewer.ScrollToTop();

        // Wait for scroll and render
        await Task.Delay(100);

        await Dispatcher.InvokeAsync(() =>
        {
            try
            {
                // Find the ListBoxItem for the new item (should be at index 0)
                var container = ActiveItemsListBox.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
                if (container != null)
                {
                    // For orders, find the VendorName TextBox; for notes, find the first RichTextBox or TextBox
                    if (item.NoteType == NoteType.Order)
                    {
                        // Find the TextBox bound to VendorName
                        var vendorNameTextBox = FindTextBoxByBinding(container, "VendorName");
                        if (vendorNameTextBox != null)
                        {
                            vendorNameTextBox.Focus();
                            vendorNameTextBox.SelectAll();
                            return;
                        }
                    }
                    else
                    {
                        // For sticky notes, try to find the RichTextBox first
                        var richTextBox = FindVisualChild<RichTextBox>(container);
                        if (richTextBox != null)
                        {
                            richTextBox.Focus();
                            return;
                        }
                    }

                    // Fallback: find first TextBox
                    var textBox = FindVisualChild<TextBox>(container);
                    if (textBox != null)
                    {
                        textBox.Focus();
                        textBox.SelectAll();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to focus new item");
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private TextBox? FindTextBoxByBinding(DependencyObject parent, string propertyName)
    {
        var textBoxes = FindVisualChildren<TextBox>(parent);
        foreach (var textBox in textBoxes)
        {
            var binding = textBox.GetBindingExpression(TextBox.TextProperty);
            if (binding?.ParentBinding?.Path?.Path == propertyName)
            {
                return textBox;
            }
        }
        return null;
    }

    private IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) yield break;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void ColorBar_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.DataContext is not OrderItem order) return;
        if (DataContext is not OrderLogViewModel vm) return;

        // Only allow color picking for sticky notes - orders use status colors
        if (order.NoteType != NoteType.StickyNote) return;

        var picker = new OrderColorPickerWindow(order.ColorHex)
        {
            Owner = Window.GetWindow(this)
        };

        if (picker.ShowDialog() == true)
        {
            order.ColorHex = picker.SelectedColor;
            _ = vm.SaveAsync();
        }
    }

    private void CopyVendorName_Click(object sender, RoutedEventArgs e)
    {
        CopyFieldToClipboard(sender, "Vendor name");
    }

    private void CopyTransferNumbers_Click(object sender, RoutedEventArgs e)
    {
        CopyFieldToClipboard(sender, "Transfer numbers");
    }

    private void CopyWhsNumbers_Click(object sender, RoutedEventArgs e)
    {
        CopyFieldToClipboard(sender, "WHS numbers");
    }

    private async void CopyFieldToClipboard(object sender, string fieldName)
    {
        if (sender is Button btn && btn.Tag is string value && !string.IsNullOrWhiteSpace(value))
        {
            System.Windows.Shapes.Path? icon = null;
            Border? border = null;
            System.Windows.Media.Geometry? originalData = null;
            
            try
            {
                System.Windows.Clipboard.SetText(value);

                // Show visual feedback - find the Path icon and Border in the button
                if (btn.Template.FindName("Icon", btn) is System.Windows.Shapes.Path foundIcon &&
                    btn.Template.FindName("Bd", btn) is Border foundBorder)
                {
                    icon = foundIcon;
                    border = foundBorder;
                    
                    // Store original icon data (fill and background are dynamic resources, so we reset via resource lookup)
                    originalData = icon.Data;

                    // Show checkmark icon and success color
                    icon.Data = System.Windows.Media.Geometry.Parse("M9,16.17L4.83,12l-1.42,1.41L9,19 21,7l-1.41-1.41z");
                    icon.Fill = System.Windows.Media.Brushes.White;
                    border.Background = (System.Windows.Media.Brush)FindResource("SuccessBrush");
                }

                // Wait briefly then restore to default (not hover) state
                await Task.Delay(800);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to copy {FieldName} to clipboard", fieldName);
            }
            finally
            {
                // Always restore the button state, even if an error occurred
                try
                {
                    if (icon != null && border != null && originalData != null)
                    {
                        icon.Data = originalData;
                        icon.Fill = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
                        border.Background = (System.Windows.Media.Brush)FindResource("SurfaceBrush");
                    }
                }
                catch
                {
                    // If restoration fails, try to force a template reload
                    btn.InvalidateVisual();
                    btn.InvalidateProperty(Button.TemplateProperty);
                }
            }
        }
    }

    private async void QuickArchive_Click(object sender, RoutedEventArgs e)
    {
        var debugPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sscc_debug.txt");
        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:HH:mm:ss.fff} [QuickArchive_Click] Handler called\n");

        // Get the OrderItem from the sender's DataContext
        OrderItem? order = null;
        if (sender is FrameworkElement fe)
        {
            order = fe.DataContext as OrderItem;
        }

        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:HH:mm:ss.fff} [QuickArchive_Click] order={(order?.Id.ToString() ?? "null")}, IsArchived={(order?.IsArchived.ToString() ?? "n/a")}\n");

        if (order == null)
        {
            Log.Warning("QuickArchive_Click: order is null, sender type={Type}", sender?.GetType().Name);
            return;
        }
        if (DataContext is not OrderLogViewModel vm) return;

        try
        {
            if (order.IsStickyNote)
            {
                await vm.DeleteCommand.ExecuteAsync(order);
                return;
            }

            // Toggle archive state based on current state
            if (order.IsArchived)
            {
                Log.Information("QuickArchive_Click: Unarchiving {Id} '{Vendor}'", order.Id, order.VendorName);
                await vm.UnarchiveOrderAsync(order);
            }
            else
            {
                await vm.ArchiveOrderAsync(order);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "QuickArchive_Click failed for {Id}", order.Id);
        }
    }

    private void ChangeColor_Click(object sender, RoutedEventArgs e)
    {
        // Locate the OrderItem robustly: ContextMenu menu items don't always have DataContext set.
        OrderItem? order = null;
        
        // Handle Button clicks (from toolbar)
        if (sender is Button btn)
        {
            // Prefer CommandParameter when supplied
            if (btn.CommandParameter is OrderItem cp)
                order = cp;
            else
                order = btn.DataContext as OrderItem;
        }
        // Handle MenuItem clicks (from context menu)
        else if (sender is MenuItem menuItem)
        {
            // Prefer CommandParameter when supplied (more reliable inside ContextMenu)
            if (menuItem.CommandParameter is OrderItem cp)
                order = cp;
            else
                order = menuItem.DataContext as OrderItem;
            if (order == null)
            {
                if (menuItem.Parent is ContextMenu cm && cm.PlacementTarget is FrameworkElement pt)
                    order = pt.DataContext as OrderItem;
            }
        }

        if (order == null) return;
        if (DataContext is not OrderLogViewModel vm) return;

        // Only allow color picking for sticky notes - orders use status colors
        if (order.NoteType != NoteType.StickyNote) return;

        var picker = new OrderColorPickerWindow(order.ColorHex)
        {
            Owner = Window.GetWindow(this)
        };

        if (picker.ShowDialog() == true)
        {
            order.ColorHex = picker.SelectedColor;
            _ = vm.SaveAsync();
        }
    }

    private void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox || comboBox.DataContext is not OrderItem order) return;
        if (DataContext is not OrderLogViewModel vm) return;
        if (comboBox.SelectedItem is not ComboBoxItem selectedItem) return;
        // Skip if this is initialization (no previous selection) - only act on actual user changes
        if (e.RemovedItems.Count == 0) return;

        if (selectedItem.Tag is OrderItem.OrderStatus newStatus)
        {
            // Get the previous status from RemovedItems (before TwoWay binding changed it)
            OrderItem.OrderStatus? previousStatus = null;
            if (e.RemovedItems[0] is ComboBoxItem oldItem && oldItem.Tag is OrderItem.OrderStatus oldStatus)
            {
                previousStatus = oldStatus;
            }

            // SetStatusAsync handles all statuses including Done (archives linked groups together)
            _ = vm.SetStatusAsync(order, newStatus, previousStatus);
        }
    }

    private void StatusIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not OrderItem order) return;
        if (button.Tag is not OrderItem.OrderStatus targetStatus) return;
        if (DataContext is not OrderLogViewModel vm) return;

        var previousStatus = order.Status;
        if (previousStatus == targetStatus) return;

        _ = vm.SetStatusAsync(order, targetStatus, previousStatus);
    }

    private void UnifiedStatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox) return;
        if (comboBox.DataContext is not ViewModels.OrderItemGroup group) return;
        if (DataContext is not OrderLogViewModel vm) return;
        if (comboBox.SelectedItem is not ComboBoxItem selectedItem) return;
        if (selectedItem.Tag is not OrderItem.OrderStatus newStatus) return;
        // Skip if this is initialization (no previous selection) - only act on actual user changes
        if (e.RemovedItems.Count == 0) return;

        // Get the previous status from RemovedItems (before TwoWay binding changed it)
        OrderItem.OrderStatus? previousStatus = null;
        if (e.RemovedItems[0] is ComboBoxItem oldItem && oldItem.Tag is OrderItem.OrderStatus oldStatus)
        {
            previousStatus = oldStatus;
        }

        // SetStatusAsync handles linked groups automatically - just call it once
        // It will apply the status to ALL members of the group
        var representative = group.First;
        if (representative != null)
        {
            _ = vm.SetStatusAsync(representative, newStatus, previousStatus);
        }
    }

    private void SetStatus_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: OrderItem.OrderStatus status } menuItem &&
            DataContext is OrderLogViewModel vm)
        {
            var order = GetOrderItemFromContextMenu(menuItem);
            if (order != null)
            {
                // "Done" means archive immediately
                if (status == OrderItem.OrderStatus.Done)
                {
                    order.PreviousStatus = order.Status;
                    _ = vm.ArchiveOrderAsync(order);
                }
                else
                {
                    _ = vm.SetStatusAsync(order, status);
                }
            }
        }
    }

    private void ResetInProgressTimer_Click(object sender, RoutedEventArgs e)
    {
        var order = GetOrderItemFromContextMenu(sender);
        if (order != null && DataContext is OrderLogViewModel vm)
        {
            order.ResetInProgressTimer();
            _ = vm.SaveAsync();
        }
    }

    private void ResetOnDeckTimer_Click(object sender, RoutedEventArgs e)
    {
        var order = GetOrderItemFromContextMenu(sender);
        if (order != null && DataContext is OrderLogViewModel vm)
        {
            order.ResetOnDeckTimer();
            _ = vm.SaveAsync();
        }
    }

    private void ResetAllTimers_Click(object sender, RoutedEventArgs e)
    {
        var order = GetOrderItemFromContextMenu(sender);
        if (order != null && DataContext is OrderLogViewModel vm)
        {
            order.ResetAllTimers();
            _ = vm.SaveAsync();
        }
    }

    private void LinkButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement fe) return;
            
            var order = fe.DataContext as OrderItem;
            if (order == null)
            {
                Log.Warning("LinkButton_Click: Could not get OrderItem from button DataContext");
                return;
            }

            if (DataContext is OrderLogViewModel vm)
            {
                vm.StartLinkMode(order);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start link mode from button");
        }
    }

    private void LinkWith_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var order = GetOrderItemFromContextMenu(sender);
            if (order == null)
            {
                Log.Warning("LinkWith_Click: Could not get OrderItem from context menu");
                return;
            }

            if (DataContext is OrderLogViewModel vm)
            {
                // Enter link mode - user clicks another card to complete
                vm.StartLinkMode(order);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to start link mode");
        }
    }

    private async void UnlinkSingleItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not OrderLogViewModel vm) return;
            if (sender is not FrameworkElement fe) return;

            // Get the OrderItem from button's DataContext (it's in the member template)
            if (fe.DataContext is not OrderItem item)
            {
                Log.Warning("UnlinkSingleItem_Click: Could not get OrderItem from DataContext");
                return;
            }

            if (item.LinkedGroupId == null)
            {
                vm.StatusMessage = "Item is not linked";
                return;
            }

            // Just clear this one item's LinkedGroupId
            item.LinkedGroupId = null;

            await vm.SaveAsync();
            vm.RefreshDisplayItems();
            vm.StatusMessage = $"Removed {item.VendorName ?? "item"} from group";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to unlink single item");
        }
    }

    private async void Unlink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not OrderLogViewModel vm) return;

            Guid? groupId = null;

            // Handle MenuItem (context menu) with OrderItem DataContext
            if (sender is MenuItem menuItem && menuItem.DataContext is OrderItem order)
            {
                groupId = order.LinkedGroupId;
            }
            // Handle Button (merged card footer) with OrderItemGroup DataContext
            else if (sender is Button button && button.DataContext is ViewModels.OrderItemGroup group)
            {
                groupId = group.LinkedGroupId;
            }
            // Handle FrameworkElement with OrderItemGroup DataContext (generic fallback)
            else if (sender is FrameworkElement fe && fe.DataContext is ViewModels.OrderItemGroup grp)
            {
                groupId = grp.LinkedGroupId;
            }

            if (groupId == null)
            {
                vm.StatusMessage = "Order was not linked";
                return;
            }

            // Clear linked id for all items in same group
            foreach (var item in vm.Items)
            {
                if (item.LinkedGroupId == groupId) item.LinkedGroupId = null;
            }
            foreach (var item in vm.ArchivedItems)
            {
                if (item.LinkedGroupId == groupId) item.LinkedGroupId = null;
            }

            await vm.SaveAsync();
            vm.RefreshDisplayItems();
            vm.StatusMessage = "Unlinked group";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to unlink orders in widget view");
        }
    }

    private async void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var order = GetOrderItemFromContextMenu(sender);
            if (order != null && DataContext is OrderLogViewModel vm)
            {
                await vm.MoveUpCommand.ExecuteAsync(order);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed MoveUp in widget");
        }
    }

    private async void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var order = GetOrderItemFromContextMenu(sender);
            if (order != null && DataContext is OrderLogViewModel vm)
            {
                await vm.MoveDownCommand.ExecuteAsync(order);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed MoveDown in widget");
        }
    }

    private async void ArchiveNote_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var order = GetOrderItemFromContextMenu(sender);
            if (order != null && DataContext is OrderLogViewModel vm)
            {
                if (order.IsStickyNote)
                {
                    await vm.DeleteCommand.ExecuteAsync(order);
                    return;
                }
                // Store previous status before archiving so it can be restored
                order.PreviousStatus = order.Status;
                await vm.ArchiveOrderCommand.ExecuteAsync(order);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to archive order");
        }
    }

    private async void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var order = GetOrderItemFromContextMenu(sender);
            if (order != null && DataContext is OrderLogViewModel vm)
            {
                await vm.DeleteCommand.ExecuteAsync(order);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete order");
        }
    }

    private async void DeleteArchivedNote_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var order = GetOrderItemFromContextMenu(sender);
            if (order != null && DataContext is OrderLogViewModel vm)
            {
                await vm.DeleteCommand.ExecuteAsync(order);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete archived order");
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var order = GetOrderItemFromContextMenu(sender);
            if (order != null && DataContext is OrderLogViewModel vm)
            {
                vm.SelectedItem = order;
                vm.CopyCommand?.Execute(null);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to copy order");
        }
    }

    private async void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var order = GetOrderItemFromContextMenu(sender);
            if (DataContext is OrderLogViewModel vm)
            {
                // Set insertion context if an order was right-clicked
                if (order != null)
                {
                    vm.SelectedItem = order;
                }
                await vm.PasteCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to paste order");
        }
    }

    private async void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var order = GetOrderItemFromContextMenu(sender);
            if (order != null && DataContext is OrderLogViewModel vm)
            {
                vm.SelectedItem = order;
                await vm.DuplicateCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to duplicate order");
        }
    }

    private async void UnarchiveNote_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var debugPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sscc_debug.txt");
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:HH:mm:ss.fff} [UnarchiveNote_Click] Handler called\n");

            var order = GetOrderItemFromContextMenu(sender);
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:HH:mm:ss.fff} [UnarchiveNote_Click] Got order: {(order?.Id.ToString() ?? "null")}\n");

            if (order != null && DataContext is OrderLogViewModel vm)
            {
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:HH:mm:ss.fff} [UnarchiveNote_Click] Calling UnarchiveOrderCommand\n");
                await vm.UnarchiveOrderCommand.ExecuteAsync(order);
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:HH:mm:ss.fff} [UnarchiveNote_Click] Command completed\n");
            }
            else
            {
                System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:HH:mm:ss.fff} [UnarchiveNote_Click] Early exit - order={order != null}, DataContext is OrderLogViewModel={DataContext is OrderLogViewModel}\n");
            }
        }
        catch (Exception ex)
        {
            var debugPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sscc_debug.txt");
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:HH:mm:ss.fff} [UnarchiveNote_Click] EXCEPTION: {ex.Message}\n");
            Log.Warning(ex, "Failed to unarchive order");
        }
    }

    private void SetNoteCategory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var order = GetOrderItemFromContextMenu(sender);
            if (order == null) return;

            var tag = (sender as MenuItem)?.Tag?.ToString();
            if (Enum.TryParse<NoteCategory>(tag, out var category))
            {
                order.NoteCategory = category;
                if (DataContext is OrderLogViewModel vm)
                    _ = vm.SaveAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to set note category");
        }
    }

    private async void RestoreGroup_Click(object sender, RoutedEventArgs e)
    {
        var debugPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sscc_debug.txt");
        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:HH:mm:ss.fff} [RestoreGroup_Click] Handler called\n");

        try
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.DataContext is not ViewModels.OrderItemGroup group) return;
            if (DataContext is not OrderLogViewModel vm) return;

            var representative = group.First;
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:HH:mm:ss.fff} [RestoreGroup_Click] Calling UnarchiveOrderAsync for {representative?.Id}\n");
            if (representative != null)
                await vm.UnarchiveOrderAsync(representative);
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:HH:mm:ss.fff} [RestoreGroup_Click] EXCEPTION: {ex.Message}\n");
            Log.Warning(ex, "Failed to restore group");
        }
    }

    private async void UnarchiveGroup_Click(object sender, RoutedEventArgs e)
    {
        var debugPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sscc_debug.txt");
        System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:HH:mm:ss.fff} [UnarchiveGroup_Click] Handler called\n");

        try
        {
            if (sender is not MenuItem menuItem) return;
            var contextMenu = menuItem.Parent as ContextMenu;
            if (contextMenu?.PlacementTarget is not FrameworkElement target) return;
            if (target.DataContext is not ViewModels.OrderItemGroup group) return;
            if (DataContext is not OrderLogViewModel vm) return;

            var representative = group.First;
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:HH:mm:ss.fff} [UnarchiveGroup_Click] Calling UnarchiveOrderAsync for {representative?.Id}\n");
            if (representative != null)
                await vm.UnarchiveOrderAsync(representative);
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(debugPath, $"{DateTime.Now:HH:mm:ss.fff} [UnarchiveGroup_Click] EXCEPTION: {ex.Message}\n");
            Log.Warning(ex, "Failed to unarchive group");
        }
    }

    private async void DeleteArchivedGroup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem menuItem) return;
            var contextMenu = menuItem.Parent as ContextMenu;
            if (contextMenu?.PlacementTarget is not FrameworkElement target) return;
            if (target.DataContext is not ViewModels.OrderItemGroup group) return;
            if (DataContext is not OrderLogViewModel vm) return;

            foreach (var member in group.Members.ToList())
            {
                await vm.DeleteCommand.ExecuteAsync(member);
            }
            vm.StatusMessage = "Deleted group";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete archived group");
        }
    }

    // Inline editing for order card fields
    private void EditableField_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is UIElement el)
        {
            el.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void EditableField_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.Foreground = Application.Current?.Resources["TextPrimaryBrush"] as Brush ?? Brushes.White;
            tb.Background = Application.Current?.Resources["SurfaceHoverBrush"] as Brush ?? Brushes.Transparent;
            tb.SelectAll();
        }
    }

    private async void EditableField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.Background = Brushes.Transparent;

            // Use disabled color if empty, secondary otherwise
            if (string.IsNullOrEmpty(tb.Text))
            {
                tb.Foreground = Application.Current?.Resources["TextDisabledBrush"] as Brush ?? Brushes.Gray;
            }
            else
            {
                tb.Foreground = Application.Current?.Resources["TextSecondaryBrush"] as Brush ?? Brushes.Gray;
            }

            // Save changes
            if (DataContext is OrderLogViewModel vm)
            {
                await vm.SaveAsync();
            }
        }
    }

    #region Text Formatting Tools

    private void FormatBold_Click(object sender, RoutedEventArgs e)
        => Helpers.TextFormattingHelper.FormatBold(sender, this);

    private void FormatItalic_Click(object sender, RoutedEventArgs e)
        => Helpers.TextFormattingHelper.FormatItalic(sender, this);

    private void FormatUnderline_Click(object sender, RoutedEventArgs e)
        => Helpers.TextFormattingHelper.FormatUnderline(sender, this);

    private void InsertBullet_Click(object sender, RoutedEventArgs e)
        => Helpers.TextFormattingHelper.InsertBullet(sender, this);

    private void InsertCheckbox_Click(object sender, RoutedEventArgs e)
        => Helpers.TextFormattingHelper.InsertCheckbox(sender, this);

    private void InsertTimestamp_Click(object sender, RoutedEventArgs e)
        => Helpers.TextFormattingHelper.InsertTimestamp(sender, this);

    private void InsertDivider_Click(object sender, RoutedEventArgs e)
        => Helpers.TextFormattingHelper.InsertDivider(sender, this);

    private void NoteContent_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Ensure the RichTextBox gets focus when clicked - prevents drag behavior from blocking
        if (sender is RichTextBox rtb)
        {
            // Check if click is on a checkbox - toggle it
            var position = e.GetPosition(rtb);
            if (Helpers.TextFormattingHelper.IsPositionOverCheckbox(rtb, position))
            {
                // Move caret to click position first
                var textPos = rtb.GetPositionFromPoint(position, true);
                if (textPos != null)
                    rtb.CaretPosition = textPos;
                
                if (Helpers.TextFormattingHelper.ToggleCheckboxAtCurrentLine(rtb))
                {
                    // Save the change
                    Helpers.TextFormattingHelper.UpdateNoteContent(sender, this);
                    e.Handled = true;
                    return;
                }
            }
            
            rtb.Focus();
            e.Handled = false; // Let the event continue for text selection
        }
    }

    private void NoteContent_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is RichTextBox rtb)
            Helpers.TextFormattingHelper.HandleListAutoContinuation(rtb, e);

        if (e.Key == Key.Escape && !e.Handled && sender is UIElement el)
        {
            el.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
            e.Handled = true;
        }
    }

    private void NoteContent_Loaded(object sender, RoutedEventArgs e)
    {
        // Load saved content and attach selection change handler for UI feedback
        if (sender is RichTextBox rtb)
        {
            Helpers.TextFormattingHelper.LoadNoteContent(rtb);
            rtb.SelectionChanged -= NoteRichTextBox_SelectionChanged;
            rtb.SelectionChanged += NoteRichTextBox_SelectionChanged;
            // Initialize button states
            UpdateFormattingToolbarState(rtb);
        }
    }

    private void NoteContent_LostFocus(object sender, RoutedEventArgs e)
    {
        Helpers.TextFormattingHelper.UpdateNoteContent(sender, this);
    }

    private void NoteRichTextBox_SelectionChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is RichTextBox rtb)
        {
            UpdateFormattingToolbarState(rtb);
        }
    }

    private void UpdateFormattingToolbarState(RichTextBox rtb)
    {
        try
        {
            // Determine formatting at selection
            var fw = rtb.Selection.GetPropertyValue(TextElement.FontWeightProperty);
            bool isBold = fw != DependencyProperty.UnsetValue && fw.Equals(FontWeights.Bold);

            var fs = rtb.Selection.GetPropertyValue(TextElement.FontStyleProperty);
            bool isItalic = fs != DependencyProperty.UnsetValue && fs.Equals(FontStyles.Italic);

            var td = rtb.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
            bool isUnderline = td != DependencyProperty.UnsetValue && td is TextDecorationCollection tdc && tdc.Contains(TextDecorations.Underline[0]);

            // Check if current paragraph is in a List (bullet)
            bool isInList = false;
            var para = rtb.CaretPosition.Paragraph;
            var p = para as DependencyObject;
            while (p != null)
            {
                if (p is System.Windows.Documents.List) { isInList = true; break; }
                p = VisualTreeHelper.GetParent(p);
            }

            // Find toolbar toggles in the same DataTemplate visual tree
            var container = FindAncestor<FrameworkElement>(rtb);
            if (container != null)
            {
                var bold = FindDescendantByName<ToggleButton>(container, "BoldToggle");
                var italic = FindDescendantByName<ToggleButton>(container, "ItalicToggle");
                var underline = FindDescendantByName<ToggleButton>(container, "UnderlineToggle");
                var bullet = FindDescendantByName<ToggleButton>(container, "BulletToggle");

                if (bold != null) bold.IsChecked = isBold;
                if (italic != null) italic.IsChecked = isItalic;
                if (underline != null) underline.IsChecked = isUnderline;
                if (bullet != null) bullet.IsChecked = isInList;
            }
        }
        catch { }
    }

    private static T? FindDescendantByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T fe && fe.Name == name) return fe;
            var found = FindDescendantByName<T>(child, name);
            if (found != null) return found;
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(start);
        while (parent != null)
        {
            if (parent is T t) return t;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    #endregion

    #region Merged Card Drag and Drop

    private System.Windows.Point _mergedCardDragStartPoint;

    private void MergedCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            _mergedCardDragStartPoint = e.GetPosition(null);
        }
    }

    private void MergedCard_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not ViewModels.OrderItemGroup group) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _mergedCardDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _mergedCardDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        // Drag all member IDs
        var ids = group.Members.Select(m => m.Id).ToArray();
        var data = new DataObject();
        data.SetData("OrderItemIds", ids);
        data.SetData("IsMergedCard", true); // Flag to indicate it's a merged card drag

        DragDrop.DoDragDrop(fe, data, DragDropEffects.Move);
    }

    private void MergedCard_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("OrderItemId") || e.Data.GetDataPresent("OrderItemIds"))
        {
            e.Effects = DragDropEffects.Move;

            // Visual feedback
            if (sender is Border b)
            {
                if (b.Tag == null) b.Tag = b.BorderBrush;
                b.BorderBrush = Application.Current?.Resources["AccentBrush"] as Brush ?? System.Windows.Media.Brushes.LightBlue;
                b.BorderThickness = new Thickness(3);
            }
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void MergedCard_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border b && b.Tag is System.Windows.Media.Brush orig)
        {
            b.BorderBrush = orig;
            b.BorderThickness = new Thickness(1);
            b.Tag = null;
        }
    }

    private async void MergedCard_Drop(object sender, DragEventArgs e)
    {
        try
        {
            // Reset visual feedback
            if (sender is Border b && b.Tag is System.Windows.Media.Brush orig)
            {
                b.BorderBrush = orig;
                b.BorderThickness = new Thickness(1);
                b.Tag = null;
            }

            if (!e.Data.GetDataPresent("OrderItemId") && !e.Data.GetDataPresent("OrderItemIds")) return;

            var droppedIds = new System.Collections.Generic.List<Guid>();
            if (e.Data.GetDataPresent("OrderItemIds") && e.Data.GetData("OrderItemIds") is Guid[] arr)
            {
                droppedIds.AddRange(arr);
            }
            else if (e.Data.GetDataPresent("OrderItemId"))
            {
                droppedIds.Add((Guid)e.Data.GetData("OrderItemId"));
            }

            if (DataContext is not OrderLogViewModel vm) return;
            if (sender is not FrameworkElement fe || fe.DataContext is not ViewModels.OrderItemGroup targetGroup) return;

            var droppedItems = vm.Items.Concat(vm.ArchivedItems).Where(i => droppedIds.Contains(i.Id)).ToList();
            var target = targetGroup.First; // Drop before the first item of target group

            // Check if this is a split-drag (dragging from section handle to unlink)
            bool isSplitDrag = e.Data.GetDataPresent("SplitFromGroup") && e.Data.GetData("SplitFromGroup") is bool split && split;

            // If split-drag, unlink the dragged item
            if (isSplitDrag && droppedItems.Count == 1)
            {
                droppedItems[0].LinkedGroupId = null;
            }

            if (droppedItems.Count > 0)
            {
                // If Ctrl is held, link with target group
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
                {
                    await vm.LinkItemsAsync(droppedItems, target);
                    vm.StatusMessage = "Linked items";
                }
                else
                {
                    await vm.MoveOrdersAsync(droppedItems, target);
                    if (isSplitDrag)
                    {
                        vm.StatusMessage = "Split and moved order";
                    }
                    else
                    {
                        vm.StatusMessage = $"Moved {droppedItems.Count} item(s)";
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Merged card drop failed");
        }
    }

    #endregion

    #region Section Drag Handles (Split-Drag)

    private System.Windows.Point _sectionDragStartPoint;

    private void SectionDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            _sectionDragStartPoint = e.GetPosition(null);
            e.Handled = true; // Prevent merged card drag from starting
        }
    }

    private void SectionDragHandle_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not Border border) return;

        // Find the OrderItem from the Border's DataContext
        var current = border.DataContext;
        if (current is not OrderItem orderItem) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _sectionDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _sectionDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        // Drag this single order (will auto-unlink when dropped elsewhere)
        var data = new DataObject();
        data.SetData("OrderItemId", orderItem.Id);
        data.SetData("SplitFromGroup", true); // Flag to indicate split-drag

        DragDrop.DoDragDrop(border, data, DragDropEffects.Move);
        e.Handled = true;
    }

    #endregion

    #region Container Drop Zone (iOS-like behavior)

    private void Container_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("OrderItemId") || e.Data.GetDataPresent("OrderItemIds"))
        {
            e.Effects = DragDropEffects.Move;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private async void Container_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.Data.GetDataPresent("OrderItemId") && !e.Data.GetDataPresent("OrderItemIds")) return;

            var droppedIds = new System.Collections.Generic.List<Guid>();
            if (e.Data.GetDataPresent("OrderItemIds") && e.Data.GetData("OrderItemIds") is Guid[] arr)
            {
                droppedIds.AddRange(arr);
            }
            else if (e.Data.GetDataPresent("OrderItemId"))
            {
                droppedIds.Add((Guid)e.Data.GetData("OrderItemId"));
            }

            if (DataContext is not OrderLogViewModel vm) return;

            var droppedItems = vm.Items.Concat(vm.ArchivedItems).Where(i => droppedIds.Contains(i.Id)).ToList();

            // Check if this is a split-drag (dragging from section handle to unlink)
            bool isSplitDrag = e.Data.GetDataPresent("SplitFromGroup") && e.Data.GetData("SplitFromGroup") is bool split && split;

            // If split-drag, unlink the dragged item
            if (isSplitDrag && droppedItems.Count == 1)
            {
                droppedItems[0].LinkedGroupId = null;
                await vm.SaveAsync();
                vm.StatusMessage = "Unlinked order";
            }
            else
            {
                // Just moved to empty space - keep current position
                vm.StatusMessage = $"Moved {droppedItems.Count} item(s)";
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Container drop failed");
        }
    }

    #endregion

    #region Multi-Select Helpers

    /// <summary>
    /// Handles when a multi-select checkbox is checked - adds item to selection
    /// </summary>
    private void MultiSelectCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            if (checkBox.Tag is OrderItem item)
            {
                if (DataContext is OrderLogViewModel vm && !vm.SelectedItems.Contains(item))
                {
                    vm.SelectedItems.Add(item);
                }
            }
            else if (checkBox.Tag is OrderItemGroup grp)
            {
                if (DataContext is OrderLogViewModel vm)
                {
                    foreach (var m in grp.Members)
                    {
                        if (!vm.SelectedItems.Contains(m)) vm.SelectedItems.Add(m);
                    }
                }
            }
            else if (checkBox.Tag is OrderItem item2)
            {
                if (DataContext is OrderLogViewModel vm && !vm.SelectedItems.Contains(item2)) vm.SelectedItems.Add(item2);
            }
        }
    }

    /// <summary>
    /// Handles when a multi-select checkbox is unchecked - removes item from selection
    /// </summary>
    private void MultiSelectCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            if (checkBox.Tag is OrderItem item)
            {
                if (DataContext is OrderLogViewModel vm && vm.SelectedItems.Contains(item))
                {
                    vm.SelectedItems.Remove(item);
                }
            }
            else if (checkBox.Tag is ViewModels.OrderItemGroup grp)
            {
                if (DataContext is OrderLogViewModel vm)
                {
                    foreach (var m in grp.Members.ToList())
                    {
                        vm.SelectedItems.Remove(m);
                    }
                }
            }
            else if (checkBox.Tag is Models.OrderItem item2)
            {
                if (DataContext is OrderLogViewModel vm) vm.SelectedItems.Remove(item2);
            }
        }
    }

    #endregion

    #region Keyboard Shortcut Helpers

    /// <summary>
    /// Focuses the search box and selects all text for quick editing
    /// </summary>
    private void FocusSearchBox()
    {
        try
        {
            SearchBox?.Focus();
            SearchBox?.SelectAll();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to focus search box");
        }
    }

    /// <summary>
    /// Click handler for the search border to focus the search box
    /// </summary>
    private void SearchBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        FocusSearchBox();
        e.Handled = true;
    }

    /// <summary>
    /// Scrolls the main content to the top
    /// </summary>
    private void ScrollToTop()
    {
        try
        {
            MainScrollViewer?.ScrollToTop();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to scroll to top");
        }
    }

    /// <summary>
    /// Scrolls the main content to the bottom
    /// </summary>
    private void ScrollToBottom()
    {
        try
        {
            MainScrollViewer?.ScrollToEnd();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to scroll to bottom");
        }
    }

    /// <summary>
    /// Scrolls to a specific item in the list
    /// </summary>
    private void ScrollToItem(OrderItem item)
    {
        try
        {
            // Find the ListBoxItem container for this item
            var container = ActiveItemsListBox?.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
            if (container != null)
            {
                container.BringIntoView();
                return;
            }

            // If not found in active items, check if we need to switch tabs
            if (DataContext is OrderLogViewModel vm)
            {
                // If item is archived, might need to switch to archived tab
                if (item.IsArchived)
                {
                    Log.Debug("Item is archived, would need to switch to archived tab");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to scroll to item");
        }
    }

    /// <summary>
    /// Shows the jump-to-item dialog for quick navigation
    /// </summary>
    private void ShowJumpDialog()
    {
        // Future implementation: Show quick jump dialog to navigate to specific order
        // For now, users can use Ctrl+F to search and then Arrow Up/Down to navigate
        if (DataContext is OrderLogViewModel vm)
        {
            vm.StatusMessage = "Use Ctrl+F to search, then Arrow Up/Down to navigate";
        }
        Log.Debug("Jump dialog requested (not yet fully implemented - use search + arrows)");
    }

    /// <summary>
    /// Shows the keyboard shortcuts help dialog (future implementation)
    /// </summary>
    private void ShowKeyboardHelp()
    {
        // Future implementation: Show keyboard shortcuts help dialog
        Log.Debug("Keyboard help requested (not yet implemented)");
    }

    /// <summary>
    /// Scrolls to the top of the specified expander within the MainScrollViewer
    /// </summary>
    private void ScrollToExpander(Expander expander)
    {
        if (expander == null || MainScrollViewer == null)
            return;

        expander.IsExpanded = true;

        // Use dispatcher to ensure layout is updated before calculating position
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            try
            {
                // Get the position of the expander relative to the scroll content
                var transform = expander.TransformToAncestor(MainScrollViewer);
                var point = transform.Transform(new Point(0, 0));
                
                // Calculate target scroll position (current offset + element position - small padding)
                double targetOffset = MainScrollViewer.VerticalOffset + point.Y - 8;
                MainScrollViewer.ScrollToVerticalOffset(Math.Max(0, targetOffset));
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to calculate scroll position, falling back to BringIntoView");
                expander.BringIntoView();
            }
        });
    }

    /// <summary>
    /// Scrolls to the In Progress status section
    /// </summary>
    private void JumpToInProgress_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ScrollToExpander(InProgressExpander);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to jump to In Progress section");
        }
    }

    /// <summary>
    /// Scrolls to the On Deck status section
    /// </summary>
    private void JumpToOnDeck_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ScrollToExpander(OnDeckExpander);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to jump to On Deck section");
        }
    }

    /// <summary>
    /// Scrolls to the Not Ready status section
    /// </summary>
    private void JumpToNotReady_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ScrollToExpander(NotReadyExpander);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to jump to Not Ready section");
        }
    }

    private void QuickJumpHandle_MouseEnter(object sender, MouseEventArgs e)
    {
        QuickJumpContent.Opacity = 0;
        QuickJumpContent.Visibility = Visibility.Visible;

        QuickJumpContent.BeginAnimation(OpacityProperty, null);
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        QuickJumpContent.BeginAnimation(OpacityProperty, fadeIn);

        if (QuickJumpSlideTransform != null)
        {
            var slideIn = new DoubleAnimation(0.75, 1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            QuickJumpSlideTransform.BeginAnimation(ScaleTransform.ScaleXProperty, slideIn);
        }
    }

    private void SideHandleHoverZone_MouseLeave(object sender, MouseEventArgs e)
    {
        if (SideHandlePanel == null || SideHandleSlideTransform == null) return;
        if (SideHandlePanel.IsMouseOver) return;
        SideHandlePanel_MouseLeave(sender, e);
    }

    private void SideHandleHoverZone_MouseEnter(object sender, MouseEventArgs e)
    {
        if (SideHandlePanel == null || SideHandleSlideTransform == null) return;

        SideHandlePanel.IsHitTestVisible = true;
        SideHandlePanel.BeginAnimation(OpacityProperty, null);
        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        SideHandlePanel.BeginAnimation(OpacityProperty, fadeIn);

        var slide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        SideHandleSlideTransform.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private void SideHandlePanel_MouseLeave(object sender, MouseEventArgs e)
    {
        if (SideHandlePanel == null || SideHandleSlideTransform == null) return;

        QuickJumpContent.BeginAnimation(OpacityProperty, null);
        if (QuickJumpSlideTransform != null)
        {
            QuickJumpSlideTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            QuickJumpSlideTransform.ScaleX = 0.75;
        }
        QuickJumpContent.Visibility = Visibility.Collapsed;

        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fadeOut.Completed += (_, _) => { if (!SideHandlePanel.IsMouseOver) SideHandlePanel.IsHitTestVisible = false; };
        SideHandlePanel.BeginAnimation(OpacityProperty, fadeOut);

        var offset = _isDockedLeft ? -20 : 20;
        var slide = new DoubleAnimation(offset, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        SideHandleSlideTransform.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    #endregion
}
