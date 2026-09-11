using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using AppLauncher.Models;
using AppLauncher.Services;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ScaleTransform = System.Windows.Media.ScaleTransform;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace AppLauncher.Views;

public partial class MainWindow : Window
{
    private readonly IncrementalScanner _scanner;
    private readonly LauncherConfig _config;

    private List<AppItem> _allApps = new();
    private readonly ObservableCollection<AppItem> _filteredPinnedApps = new();
    private readonly ObservableCollection<AppItem> _filteredRegularApps = new();
    private readonly ObservableCollection<AppItem> _searchResults = new();

    private bool _isAnimating;
    private bool _isShown;
    private bool _isClosingToTray;
    private DateTime _lastShowTime = DateTime.MinValue;
    private HwndSource? _hwndSource;
    private bool _nativeAcrylicEnabled;
    private int _autoHideSuspensionCount;

    public bool IsLauncherShown => _isShown;

    public MainWindow(LauncherConfig config)
    {
        InitializeComponent();

        _config = config ?? throw new ArgumentNullException(nameof(config));
        _scanner = new IncrementalScanner();

        _nativeAcrylicEnabled = NativeMethods.IsWindows11AcrylicSupported();
        ConfigureWindowSurfaceForPlatform();

        PinnedAppsItemsControl.ItemsSource = _filteredPinnedApps;
        RegularAppsItemsControl.ItemsSource = _filteredRegularApps;
        SearchResultsItemsControl.ItemsSource = _searchResults;

        try
        {
            var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(icoPath))
            {
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(icoPath, UriKind.Absolute));
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("MainWindow.LoadIconFile", ex);
        }

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        SourceInitialized += MainWindow_SourceInitialized;

        LocalizationService.LanguageChanged += OnLanguageChanged;

        ApplyAppearanceSettings(_config);
    }

    public void ApplyAppearanceSettings(LauncherConfig config)
    {
        Dispatcher.Invoke(() =>
        {
            int opacityPercent = _nativeAcrylicEnabled
                ? Math.Clamp(config.BackgroundOpacityPercent, 45, 85)
                : Math.Clamp(config.BackgroundOpacityPercent, 92, 100);
            byte alpha = (byte)((opacityPercent * 255) / 100);
            byte gray = (byte)Math.Clamp(config.BackgroundGrayTone, 18, 80);
            byte blueTint = (byte)Math.Clamp(gray + 8, 0, 255);

            var bgColor = System.Windows.Media.Color.FromArgb(alpha, gray, gray, blueTint);
            RootContainer.Background = new System.Windows.Media.SolidColorBrush(bgColor);
        });
    }

    private void ConfigureWindowSurfaceForPlatform()
    {
        if (_nativeAcrylicEnabled)
        {
            AllowsTransparency = false;
            RootContainer.CornerRadius = new CornerRadius(0);
            RootContainer.BorderThickness = new Thickness(0);
            RootContainer.BorderBrush = System.Windows.Media.Brushes.Transparent;
            return;
        }

        // Keep the existing WPF rounded surface on Windows 10 / unsupported builds.
        AllowsTransparency = true;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, null);
        RootContainer.CornerRadius = new CornerRadius(26);
        RootContainer.BorderThickness = new Thickness(1);
        RootContainer.BorderBrush = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
    }

    private void OnLanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateLocalizedTexts();
        });
    }

    public void UpdateLocalizedTexts()
    {
        if (SearchPlaceholder != null) SearchPlaceholder.Text = LocalizationService.Get("Search_Placeholder");
        if (MenuAddApp != null) MenuAddApp.Header = LocalizationService.Get("Blank_AddApp");
        if (MenuAddFolder != null) MenuAddFolder.Header = LocalizationService.Get("Blank_AddFolder");
        if (MenuRefreshList != null) MenuRefreshList.Header = LocalizationService.Get("Blank_Refresh");
        if (EmptyStateTitle != null) EmptyStateTitle.Text = LocalizationService.Get("Empty_NoApp");
        if (EmptyStateSubtitle != null) EmptyStateSubtitle.Text = LocalizationService.Get("Empty_Hint");
    }

    private IntPtr _appIconHandle = IntPtr.Zero;
    private IntPtr _appIconSmallHandle = IntPtr.Zero;

    private static System.Drawing.Bitmap CreateVectorIconBitmap(int size)
    {
        var bmp = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
        g.Clear(System.Drawing.Color.FromArgb(0, 0, 0, 0));

        float pad = Math.Max(1f, size * 0.05f);
        float sqSize = size - (pad * 2f);
        float cornerRad = sqSize * 0.23f;
        float d = cornerRad * 2f;

        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(pad, pad, d, d, 180, 90);
        path.AddArc(pad + sqSize - d, pad, d, d, 270, 90);
        path.AddArc(pad + sqSize - d, pad + sqSize - d, d, d, 0, 90);
        path.AddArc(pad, pad + sqSize - d, d, d, 90, 90);
        path.CloseFigure();

        using var bgBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 18, 18, 20));
        g.FillPath(bgBrush, path);

        float gridMargin = sqSize * 0.14f;
        float gridArea = sqSize - (gridMargin * 2f);
        float gap = Math.Max(1f, gridArea * 0.08f);
        float tileDim = (gridArea - (gap * 2f)) / 3f;
        float tileRad = Math.Max(1f, tileDim * 0.25f);
        float td = tileRad * 2f;

        using var whiteBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 255, 255, 255));

        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                float tx = pad + gridMargin + c * (tileDim + gap);
                float ty = pad + gridMargin + r * (tileDim + gap);

                using var tilePath = new System.Drawing.Drawing2D.GraphicsPath();
                tilePath.AddArc(tx, ty, td, td, 180, 90);
                tilePath.AddArc(tx + tileDim - td, ty, td, td, 270, 90);
                tilePath.AddArc(tx + tileDim - td, ty + tileDim - td, td, td, 0, 90);
                tilePath.AddArc(tx, ty + tileDim - td, td, td, 90, 90);
                tilePath.CloseFigure();

                g.FillPath(whiteBrush, tilePath);
            }
        }

        return bmp;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        
        // Enable WS_MINIMIZEBOX and WS_SYSMENU so Windows Taskbar manages minimize/restore natively
        NativeMethods.EnableTaskbarMinimize(handle);

        try
        {
            using var bmpBig = CreateVectorIconBitmap(48);
            using var bmpSmall = CreateVectorIconBitmap(16);

            _appIconHandle = NativeMethods.Create32BitAlphaHicon(bmpBig);
            _appIconSmallHandle = NativeMethods.Create32BitAlphaHicon(bmpSmall);

            if (_appIconHandle != IntPtr.Zero)
            {
                // 1. Send WM_SETICON directly to window
                NativeMethods.SendMessage(handle, 0x0080, (IntPtr)1, _appIconHandle); // WM_SETICON ICON_BIG
                NativeMethods.SendMessage(handle, 0x0080, (IntPtr)0, _appIconSmallHandle != IntPtr.Zero ? _appIconSmallHandle : _appIconHandle); // WM_SETICON ICON_SMALL

                // 2. Set Class Long Icon for Windows Taskbar shell
                NativeMethods.SetClassLongPtr(handle, NativeMethods.GCLP_HICON, _appIconHandle);
                NativeMethods.SetClassLongPtr(handle, NativeMethods.GCLP_HICONSM, _appIconSmallHandle != IntPtr.Zero ? _appIconSmallHandle : _appIconHandle);

                // 3. Set WPF Window.Icon using HIcon source
                var wpfIcon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    _appIconHandle,
                    Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                wpfIcon.Freeze();
                Icon = wpfIcon;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("MainWindow.InitializeIcon", ex);
        }

        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);

        if (_nativeAcrylicEnabled)
        {
            _nativeAcrylicEnabled = NativeMethods.TryEnableWindows11Acrylic(handle);
            if (!_nativeAcrylicEnabled)
            {
                // DWM can be unavailable despite the OS version check (for example, composition disabled).
                // Keep the non-layered HWND but fall back to an opaque readable surface.
                RootContainer.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(0xF2, 0x20, 0x26, 0x30));
                ApplyAppearanceSettings(_config);
            }
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETICON = 0x007F;
        const int WM_CLOSE = 0x0010;
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_MINIMIZE = 0xF020;
        const int SC_RESTORE = 0xF120;
        const int SC_CLOSE = 0xF060;

        if (msg == WM_GETICON)
        {
            int iconType = (int)wParam;
            if (iconType == 0 && _appIconSmallHandle != IntPtr.Zero)
            {
                handled = true;
                return _appIconSmallHandle;
            }
            if (_appIconHandle != IntPtr.Zero)
            {
                handled = true;
                return _appIconHandle;
            }
        }

        if (App.WM_SHOW_LAUNCHER != 0 && msg == App.WM_SHOW_LAUNCHER)
        {
            if (!_isShown || Visibility != Visibility.Visible)
            {
                ShowLauncher(forceReanimate: true);
            }
            else
            {
                BringToFront();
            }
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_CLOSE)
        {
            // Right-click Taskbar "Close Window" -> Hide from Taskbar completely into Tray
            HideToTray();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_SYSCOMMAND)
        {
            int cmd = (int)wParam & 0xFFF0;
            if (cmd == SC_CLOSE)
            {
                // Right-click Taskbar "Close Window"
                HideToTray();
                handled = true;
                return IntPtr.Zero;
            }
            else if (cmd == SC_MINIMIZE)
            {
                // Left click taskbar when active -> Hide
                HideLauncher();
                handled = true;
                return IntPtr.Zero;
            }
            else if (cmd == SC_RESTORE)
            {
                // Left click taskbar when minimized -> Show
                ShowLauncher();
                handled = true;
                return IntPtr.Zero;
            }
        }

        return IntPtr.Zero;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 0. Set up initial localized UI strings
        UpdateLocalizedTexts();

        // 1. Instant 0ms Cold Startup: immediately show cached apps from memory
        _allApps = _scanner.GetCachedAppsFast(_config);
        ApplyFilter(string.Empty);

        FocusSearchBox();

        // 2. Background async incremental refresh
        _ = LoadAppsAsync(false);
        _ = Task.Delay(500).ContinueWith(_ => NativeMethods.TrimWorkingSet());
    }

    public bool IsExplicitExiting { get; set; }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (IsExplicitExiting)
        {
            if (_appIconHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_appIconHandle);
                _appIconHandle = IntPtr.Zero;
            }
            if (_appIconSmallHandle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_appIconSmallHandle);
                _appIconSmallHandle = IntPtr.Zero;
            }
            return;
        }
        e.Cancel = true;
        HideToTray();
    }

    public async Task LoadAppsAsync(bool forceFullScan = false)
    {
        try
        {
            var apps = await _scanner.ScanAsync(_config, forceFullScan);
            await Dispatcher.InvokeAsync(() =>
            {
                _allApps = apps;
                ApplyFilter(SearchBox.Text);
            });

            // Trim memory after scan finishes to keep footprint ultra-low (~15-20MB)
            _ = Task.Delay(300).ContinueWith(_ => NativeMethods.TrimWorkingSet());
        }
        catch (Exception ex)
        {
            DiagnosticLog.Write("MainWindow.LoadApps", ex);
        }
    }

    private void ApplyFilter(string query)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        var matches = string.IsNullOrEmpty(trimmed)
            ? _allApps
            : _allApps.Where(a =>
                a.DisplayName.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                a.SearchableFileName.Contains(trimmed, StringComparison.OrdinalIgnoreCase)).ToList();

        _filteredPinnedApps.Clear();
        _filteredRegularApps.Clear();
        _searchResults.Clear();

        if (string.IsNullOrEmpty(trimmed))
        {
            foreach (var app in matches)
            {
                if (app.IsPinned)
                {
                    _filteredPinnedApps.Add(app);
                }
                else
                {
                    _filteredRegularApps.Add(app);
                }
            }

            BrowseSectionsPanel.Visibility = Visibility.Visible;
            SearchResultsItemsControl.Visibility = Visibility.Collapsed;
            PinnedDivider.Visibility = _filteredPinnedApps.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        else
        {
            foreach (var app in matches)
            {
                _searchResults.Add(app);
            }

            BrowseSectionsPanel.Visibility = Visibility.Collapsed;
            SearchResultsItemsControl.Visibility = Visibility.Visible;
        }

        EmptyStatePanel.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    #region Window Toggle / Show / Hide / HideToTray Animations

    public void ToggleLauncher()
    {
        if (_isShown)
        {
            HideLauncher();
        }
        else
        {
            ShowLauncher();
        }
    }

    public void BringToFront()
    {
        // A hidden launcher can still have a visible native Acrylic surface while
        // its content layer is transparent. Always restore the full visual state
        // through the normal show path instead of exposing the HWND directly.
        if (!_isShown || Visibility != Visibility.Visible || _isAnimating || ContentLayer.Opacity <= 0.01)
        {
            ShowLauncher(forceReanimate: true);
            return;
        }

        WindowState = WindowState.Normal;
        Visibility = Visibility.Visible;
        Show();
        Topmost = true;
        Activate();
        Focus();
        FocusSearchBox();
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.ForceForegroundWindow(hwnd);
            }
        }
        catch { }
    }

    public void FocusSearchBox()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (SearchBox != null)
            {
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
                SearchBox.CaretIndex = SearchBox.Text?.Length ?? 0;
            }
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    public void ShowLauncher(bool forceReanimate = false)
    {
        if (_isShown && !forceReanimate && Visibility == Visibility.Visible)
        {
            BringToFront();
            return;
        }

        _isAnimating = false;
        _isClosingToTray = false;
        _isShown = true;
        _lastShowTime = DateTime.UtcNow;

        WindowState = WindowState.Normal;
        Visibility = Visibility.Visible;
        Show();
        Topmost = true;

        CenterWindowOnActiveScreen();

        SearchBox.Text = string.Empty;
        if (SearchPlaceholder != null)
        {
            SearchPlaceholder.Visibility = Visibility.Visible;
        }
        FocusSearchBox();

        ContentScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ContentScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ContentLayer.BeginAnimation(OpacityProperty, null);

        ContentLayer.Opacity = 1.0;
        ContentScaleTransform.ScaleX = 1.0;
        ContentScaleTransform.ScaleY = 1.0;

        // Subtle content-only pop-in (Scale: 0.97 -> 1.0 in 120ms, Opacity: 0.0 -> 1.0 in 100ms)
        var scaleAnimX = new DoubleAnimation
        {
            From = 0.97,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var scaleAnimY = new DoubleAnimation
        {
            From = 0.97,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var opacityAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(100),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        opacityAnim.Completed += (s, e) =>
        {
            ContentLayer.Opacity = 1.0;
            ContentScaleTransform.ScaleX = 1.0;
            ContentScaleTransform.ScaleY = 1.0;
            _isAnimating = false;
            Activate();
            Focus();
            FocusSearchBox();
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                NativeMethods.ForceForegroundWindow(hwnd);
            }
            catch { }
        };

        ContentScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimX);
        ContentScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimY);
        ContentLayer.BeginAnimation(OpacityProperty, opacityAnim);

        Activate();
        Focus();
        FocusSearchBox();
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.ForceForegroundWindow(hwnd);
            }
        }
        catch { }
    }

    public void HideLauncher()
    {
        if (!_isShown || _isAnimating) return;
        _isShown = false;
        _isAnimating = true;

        // Subtle content-only pop-out (Scale: 1.0 -> 0.97 in 110ms, Opacity: 1.0 -> 0.0 in 95ms)
        var scaleAnimX = new DoubleAnimation
        {
            From = 1.0,
            To = 0.97,
            Duration = TimeSpan.FromMilliseconds(110),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        var scaleAnimY = new DoubleAnimation
        {
            From = 1.0,
            To = 0.97,
            Duration = TimeSpan.FromMilliseconds(110),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        var opacityAnim = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(95),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        opacityAnim.Completed += (s, e) =>
        {
            _isAnimating = false;
            Visibility = Visibility.Hidden;
        };

        ContentScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimX);
        ContentScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimY);
        ContentLayer.BeginAnimation(OpacityProperty, opacityAnim);
    }

    public void HideToTray()
    {
        if (_isClosingToTray) return;
        _isClosingToTray = true;
        _isShown = false;
        _isAnimating = true;

        var scaleAnimX = new DoubleAnimation
        {
            From = 1.0,
            To = 0.97,
            Duration = TimeSpan.FromMilliseconds(110),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        var scaleAnimY = new DoubleAnimation
        {
            From = 1.0,
            To = 0.97,
            Duration = TimeSpan.FromMilliseconds(110),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        var opacityAnim = new DoubleAnimation
        {
            From = ContentLayer.Opacity > 0 ? ContentLayer.Opacity : 1.0,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(95),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        opacityAnim.Completed += (s, e) =>
        {
            _isAnimating = false;
            _isClosingToTray = false;
            Visibility = Visibility.Hidden;
        };

        ContentScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimX);
        ContentScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimY);
        ContentLayer.BeginAnimation(OpacityProperty, opacityAnim);
    }

    private void CenterWindowOnActiveScreen()
    {
        try
        {
            var primaryWidth = SystemParameters.PrimaryScreenWidth;
            var primaryHeight = SystemParameters.PrimaryScreenHeight;

            Left = (primaryWidth - Width) / 2.0;
            Top = (primaryHeight - Height) / 2.0;
        }
        catch
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    public void BeginExternalInteraction()
    {
        _autoHideSuspensionCount++;
    }

    public void EndExternalInteraction()
    {
        if (_autoHideSuspensionCount > 0)
        {
            _autoHideSuspensionCount--;
        }
    }

    #endregion

    #region Launching & App Operations

    private void LaunchApp(AppItem? app, bool runAsAdmin = false)
    {
        if (app == null || !File.Exists(app.ExePath))
            return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = app.ExePath,
                WorkingDirectory = string.IsNullOrWhiteSpace(app.WorkingDirectory) ? Path.GetDirectoryName(app.ExePath) : app.WorkingDirectory,
                UseShellExecute = true
            };

            if (runAsAdmin)
            {
                psi.Verb = "runas";
            }

            Process.Start(psi);

            app.LaunchCount++;
            app.LastLaunchedUtc = DateTime.UtcNow;
            Task.Run(() =>
            {
                _scanner.SaveCache();
            });

            HideLauncher();
        }
        catch (Win32Exception w32ex) when (w32ex.NativeErrorCode == 1223)
        {
            // Silently ignore UAC cancel
        }
        catch
        {
            // Ignore
        }
    }

    private void AppTile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is AppItem app)
        {
            LaunchApp(app);
            e.Handled = true;
        }
    }

    private void AppTile_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.ContextMenu is WpfContextMenu cm && elem.DataContext is AppItem app)
        {
            if (cm.Items.Count >= 8)
            {
                if (cm.Items[0] is WpfMenuItem m0) m0.Header = LocalizationService.Get("Menu_Launch");
                if (cm.Items[1] is WpfMenuItem m1) m1.Header = LocalizationService.Get("Menu_LaunchAdmin");
                if (cm.Items[3] is WpfMenuItem m3) m3.Header = LocalizationService.Get("Menu_OpenFolder");
                if (cm.Items[4] is WpfMenuItem m4) m4.Header = app.IsPinned ? LocalizationService.Get("Menu_Unpin") : LocalizationService.Get("Menu_Pin");
                if (cm.Items[5] is WpfMenuItem m5) m5.Header = LocalizationService.Get("Menu_Rename");
                if (cm.Items[7] is WpfMenuItem m7) m7.Header = LocalizationService.Get("Menu_Hide");
            }
        }
    }

    private void MenuLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfMenuItem menuItem && menuItem.DataContext is AppItem app)
        {
            LaunchApp(app, false);
        }
    }

    private void MenuLaunchAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfMenuItem menuItem && menuItem.DataContext is AppItem app)
        {
            LaunchApp(app, true);
        }
    }

    private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfMenuItem menuItem && menuItem.DataContext is AppItem app)
        {
            try
            {
                if (File.Exists(app.ExePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{app.ExePath}\"");
                }
            }
            catch { }
        }
    }

    private void MenuTogglePin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfMenuItem menuItem && menuItem.DataContext is AppItem app)
        {
            _scanner.PinApp(app.ExePath, _config, !app.IsPinned);
            _ = LoadAppsAsync(false);
        }
    }

    private void MenuRename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfMenuItem menuItem && menuItem.DataContext is AppItem app)
        {
            var prompt = new RenameDialog(app.DisplayName) { Owner = this };
            if (prompt.ShowDialog() == true)
            {
                _scanner.RenameApp(app.ExePath, prompt.ResultName, _config);
                _ = LoadAppsAsync(false);
            }
        }
    }

    private void MenuHide_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfMenuItem menuItem && menuItem.DataContext is AppItem app)
        {
            _scanner.HideApp(app.ExePath, _config);
            _allApps.Remove(app);
            ApplyFilter(SearchBox.Text);
        }
    }

    #endregion

    #region Blank Canvas Context Menu (Add Apps / Folders / Refresh)

    private void MenuAddApp_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Get("Dialog_AddAppTitle"),
            Filter = LocalizationService.Get("Dialog_AddAppFilter"),
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                var ext = Path.GetExtension(file);
                if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var target = NativeMethods.ResolveShortcutTarget(file);
                    if (!string.IsNullOrEmpty(target) && File.Exists(target))
                    {
                        _scanner.AddCustomApp(target, _config);
                    }
                }
                else if (string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    _scanner.AddCustomApp(file, _config);
                }
            }

            _ = LoadAppsAsync(false);
        }
    }

    private void MenuAddFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = LocalizationService.Get("Dialog_AddFolderTitle"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            _scanner.AddCustomScanDirectory(dialog.SelectedPath, _config);
            _ = LoadAppsAsync(false);
        }
    }

    private async void MenuRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadAppsAsync(true);
    }

    #endregion

    #region Keyboard Controls & Caret Focus Handling

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchPlaceholder != null)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }
        ApplyFilter(SearchBox.Text);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var firstApp = string.IsNullOrWhiteSpace(SearchBox.Text)
                ? _filteredPinnedApps.FirstOrDefault() ?? _filteredRegularApps.FirstOrDefault()
                : _searchResults.FirstOrDefault();
            if (firstApp != null)
            {
                var runAsAdmin = (Keyboard.Modifiers & ModifierKeys.Control) != 0 || (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                LaunchApp(firstApp, runAsAdmin);
                e.Handled = true;
            }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                SearchBox.Text = string.Empty;
            }
            else
            {
                HideLauncher();
            }
            e.Handled = true;
            return;
        }

        if (!SearchBox.IsFocused && ((e.Key >= Key.A && e.Key <= Key.Z) || (e.Key >= Key.D0 && e.Key <= Key.D9) || e.Key == Key.Back))
        {
            SearchBox.Focus();
        }
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Maintain search focus ready for input
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_autoHideSuspensionCount > 0 || _isClosingToTray || !_isShown || _isAnimating)
        {
            return;
        }

        // Buffer during initial show animation
        if ((DateTime.UtcNow - _lastShowTime).TotalMilliseconds < 120)
        {
            return;
        }

        var fgHwnd = NativeMethods.GetForegroundWindow();
        if (fgHwnd != IntPtr.Zero)
        {
            var ourHwnd = new WindowInteropHelper(this).Handle;
            if (fgHwnd == ourHwnd)
            {
                return;
            }

        }

        HideLauncher();
    }

    #endregion
}
