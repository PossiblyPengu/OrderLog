using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using OrderLog.Features.ViewModels;
using OrderLog.Services;
using OrderLog.Helpers;

namespace OrderLog.Windows;

public partial class OrderLogWidgetWindow : AnimatedWindow
{
    private readonly OrderLogViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;

    protected override double CloseAnimDurationMs => 180.0;
    protected override double CloseAnimScale => 0.97;

    // AppBar state
    private bool _isAppBarRegistered;
    private AppBarEdge _currentEdge = AppBarEdge.None;
    private AppBarEdge _edgeBeforeMinimize = AppBarEdge.None;
    private int _appBarCallbackId;
    private HwndSource? _hwndSource;

    // Current width for the docked appbar/window; user-adjustable
    private double _dockedWidth = 380;

    // Drag-to-resize state
    private bool _isResizing = false;
    private double _resizeStartScreenX;
    private double _resizeStartWidth;

    #region Windows API Imports

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int RegisterWindowMessage(string msg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private const uint ABM_NEW = 0x00;
    private const uint ABM_REMOVE = 0x01;
    private const uint ABM_QUERYPOS = 0x02;
    private const uint ABM_SETPOS = 0x03;

    private const uint ABN_POSCHANGED = 0x01;
    private const uint ABN_FULLSCREENAPP = 0x02;

    private const uint ABE_LEFT = 0;
    private const uint ABE_RIGHT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public int lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    private enum AppBarEdge
    {
        None,
        Left,
        Right
    }

    #endregion

    public OrderLogWidgetWindow(OrderLogViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;

        DataContext = _viewModel;
        WidgetView.DataContext = _viewModel;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;

        ApplyTheme();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle | WS_EX_TOOLWINDOW));

        _appBarCallbackId = RegisterWindowMessage("OrderLogAppBarCallback");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == _appBarCallbackId)
        {
            switch (wParam.ToInt32())
            {
                case (int)ABN_POSCHANGED:
                    if (_isAppBarRegistered && _currentEdge != AppBarEdge.None)
                    {
                        PositionAppBar();
                    }
                    handled = true;
                    break;

                case (int)ABN_FULLSCREENAPP:
                    if (lParam.ToInt32() != 0)
                    {
                        Topmost = false;
                    }
                    else
                    {
                        Topmost = true;
                    }
                    handled = true;
                    break;
            }
        }

        return IntPtr.Zero;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyWidgetWidth(_viewModel.WidgetWidth);
        DockToEdge(AppBarEdge.Right);
        InitializeWidgetAsync();
    }

    private async void InitializeWidgetAsync()
    {
        Log.Information("OrderLogWidgetWindow: InitializeWidgetAsync starting");
        try
        {
            var initTask = _viewModel.InitializeAsync();
            if (await Task.WhenAny(initTask, Task.Delay(5000)) != initTask)
            {
                Log.Warning("OrderLogWidgetWindow: ViewModel.InitializeAsync is taking >5s");
            }
            await initTask;
            Log.Information("OrderLogWidgetWindow: ViewModel.InitializeAsync completed");

            await Dispatcher.InvokeAsync(() =>
            {
                WidgetView.InvalidateVisual();
                WidgetView.UpdateLayout();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
            Log.Information("OrderLogWidgetWindow: UI layout updated after init");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize OrderLog widget");
        }
        finally
        {
            try { _viewModel.IsLoading = false; } catch { }
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;

        try
        {
            var themeService = _serviceProvider.GetService<ThemeService>();
            if (themeService != null)
            {
                themeService.ThemeChanged -= OnThemeChanged;
            }
        }
        catch { }

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        if (_isAppBarRegistered)
        {
            UnregisterAppBar();
        }

        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Normal && _edgeBeforeMinimize != AppBarEdge.None)
        {
            RegisterAppBar();
            _currentEdge = _edgeBeforeMinimize;
            PositionAppBar();
            _edgeBeforeMinimize = AppBarEdge.None;

            Log.Debug("AppBar re-registered after restore at edge: {Edge}", _currentEdge);
        }
    }

    #region AppBar Registration

    private void RegisterAppBar()
    {
        if (_isAppBarRegistered) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        var data = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            uCallbackMessage = (uint)_appBarCallbackId
        };

        var result = SHAppBarMessage(ABM_NEW, ref data);
        _isAppBarRegistered = result != 0;

        Log.Debug("AppBar registered: {IsRegistered}", _isAppBarRegistered);
    }

    private void UnregisterAppBar()
    {
        if (!_isAppBarRegistered) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        var data = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd
        };

        SHAppBarMessage(ABM_REMOVE, ref data);
        _isAppBarRegistered = false;
        _currentEdge = AppBarEdge.None;

        Log.Debug("AppBar unregistered");
    }

    private void PositionAppBar()
    {
        if (!_isAppBarRegistered || _currentEdge == AppBarEdge.None) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
        var screenBounds = screen.Bounds;

        var data = new APPBARDATA
        {
            cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            uEdge = _currentEdge switch
            {
                AppBarEdge.Left => ABE_LEFT,
                AppBarEdge.Right => ABE_RIGHT,
                _ => ABE_RIGHT
            }
        };

        int appBarWidth = (int)Math.Round(_dockedWidth * GetDpiScale());

        switch (_currentEdge)
        {
            case AppBarEdge.Left:
                data.rc.left = screenBounds.Left;
                data.rc.top = screenBounds.Top;
                data.rc.right = screenBounds.Left + appBarWidth;
                data.rc.bottom = screenBounds.Bottom;
                break;

            case AppBarEdge.Right:
                data.rc.left = screenBounds.Right - appBarWidth;
                data.rc.top = screenBounds.Top;
                data.rc.right = screenBounds.Right;
                data.rc.bottom = screenBounds.Bottom;
                break;
        }

        SHAppBarMessage(ABM_QUERYPOS, ref data);

        switch (_currentEdge)
        {
            case AppBarEdge.Left:
                data.rc.right = data.rc.left + appBarWidth;
                break;
            case AppBarEdge.Right:
                data.rc.left = data.rc.right - appBarWidth;
                break;
        }

        SHAppBarMessage(ABM_SETPOS, ref data);

        var dpiScale = GetDpiScale();
        Left = data.rc.left / dpiScale;
        Top = data.rc.top / dpiScale;
        Width = (data.rc.right - data.rc.left) / dpiScale;
        Height = (data.rc.bottom - data.rc.top) / dpiScale;

        MoveWindow(hwnd, data.rc.left, data.rc.top,
            data.rc.right - data.rc.left,
            data.rc.bottom - data.rc.top, true);
    }

    private double GetDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        return source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
    }

    #endregion

    #region Dock Commands

    private void DockLeft_Click(object sender, RoutedEventArgs e)
    {
        DockToEdge(AppBarEdge.Left);
    }

    private void DockRight_Click(object sender, RoutedEventArgs e)
    {
        DockToEdge(AppBarEdge.Right);
    }

    private void DockToEdge(AppBarEdge edge)
    {
        if (!_isAppBarRegistered)
        {
            RegisterAppBar();
        }

        _currentEdge = edge;
        PositionAppBar();
        WidgetView.ApplyDockSide(edge == AppBarEdge.Left);

        // Flip resize grip to inner edge
        if (ResizeGrip != null)
            ResizeGrip.HorizontalAlignment = edge == AppBarEdge.Left
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left;

        Log.Debug("Docked to {Edge}", edge);
    }

    #endregion

    #region Theme Support

    private void ApplyTheme()
    {
        try
        {
            var themeService = _serviceProvider.GetService<ThemeService>();
            if (themeService != null)
            {
                themeService.ThemeChanged += OnThemeChanged;
                // Apply initial window-local overrides; app resources are already set by ThemeService.
                ApplyThemeResources(themeService.IsDarkMode);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to subscribe to theme changes");
        }
    }

    

    private void ApplyThemeResources(bool isDarkMode)
    {
        // Theme resources are managed by ThemeService at app level and inherit automatically.
        // Only apply window-local overrides here.
        if (_viewModel.CardFontSize > 0)
            Resources["CardFontSize"] = _viewModel.CardFontSize;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrderLogViewModel.WidgetWidth))
        {
            Dispatcher.Invoke(() => ApplyWidgetWidth(_viewModel.WidgetWidth));
        }
    }

    private void ApplyWidgetWidth(double requestedWidth)
    {
        double clamped = Math.Clamp(requestedWidth,
            OrderLogViewModel.MinWidgetWidth,
            OrderLogViewModel.MaxWidgetWidth);

        _dockedWidth = clamped;

        if (!_isResizing)
        {
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(200);

            // Animate width
            var widthAnim = new DoubleAnimation(clamped, dur) { EasingFunction = easing };
            widthAnim.Completed += (_, _) =>
            {
                BeginAnimation(WidthProperty, null);
                BeginAnimation(LeftProperty, null);
                Width = clamped;
                if (_isAppBarRegistered) PositionAppBar();
            };
            BeginAnimation(WidthProperty, widthAnim);

            // Pin docked edge: for right-dock, slide Left so the right edge stays fixed
            if (_currentEdge == AppBarEdge.Right)
            {
                double newLeft = Left + Width - clamped;
                var leftAnim = new DoubleAnimation(newLeft, dur) { EasingFunction = easing };
                BeginAnimation(LeftProperty, leftAnim);
            }
        }
        else
        {
            // Live drag: pin docked edge by adjusting Left for right-dock
            if (_currentEdge == AppBarEdge.Right)
            {
                double rightEdge = Left + Width;
                Left = rightEdge - clamped;
            }
            Width = clamped;
            if (_isAppBarRegistered) PositionAppBar();
        }
    }

    private void OnThemeChanged(object? sender, bool isDarkMode)
    {
        Dispatcher.Invoke(() =>
        {
            try
            {
                Features.Converters.StatusToColorConverter.InvalidateCache();
                ApplyThemeResources(isDarkMode);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to apply theme change");
            }
        });
    }

    #endregion

    #region Drag Resize

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isResizing = true;
        _resizeStartScreenX = PointToScreen(e.GetPosition(this)).X;
        _resizeStartWidth = _dockedWidth;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isResizing) return;

        double currentScreenX = PointToScreen(e.GetPosition(this)).X;
        double delta = _currentEdge == AppBarEdge.Left
            ? currentScreenX - _resizeStartScreenX   // left-dock: drag right = wider
            : _resizeStartScreenX - currentScreenX;  // right-dock: drag left = wider

        double newWidth = Math.Clamp(
            _resizeStartWidth + delta,
            OrderLogViewModel.MinWidgetWidth,
            OrderLogViewModel.MaxWidgetWidth);

        // Pin the docked edge: for right-dock, adjust Left so right edge stays fixed
        if (_currentEdge == AppBarEdge.Right)
        {
            double rightEdge = Left + Width;
            Left = rightEdge - newWidth;
        }

        _dockedWidth = newWidth;
        Width = newWidth;
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isResizing) return;
        _isResizing = false;
        ((UIElement)sender).ReleaseMouseCapture();

        // Commit final width to viewmodel (saves settings) and reposition AppBar
        _viewModel.WidgetWidth = _dockedWidth;
        if (_isAppBarRegistered) PositionAppBar();
        e.Handled = true;
    }

    #endregion

    #region Window Event Handlers

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // No dragging - always docked
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        _edgeBeforeMinimize = _currentEdge;

        if (_isAppBarRegistered)
        {
            UnregisterAppBar();
        }
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    public void ShowWidget()
    {
        Show();
        Activate();

        if (_isAppBarRegistered && _currentEdge != AppBarEdge.None)
        {
            PositionAppBar();
        }
    }

    #endregion
}
