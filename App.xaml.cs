using System;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using AppLauncher.Models;
using AppLauncher.Services;
using AppLauncher.Views;
using Application = System.Windows.Application;

namespace AppLauncher;

public partial class App : Application
{
    private const string MutexName = @"Global\AppLauncher_SingleInstance_Mutex_9988";
    private const string PipeName = "AppLauncher_Wakeup_Pipe_9988";

    public static uint WM_SHOW_LAUNCHER;

    private static Mutex? _mutex;
    private static bool _hasMutex;
    private NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private CancellationTokenSource? _pipeCts;
    private LauncherConfig? _config;
    private bool _pinTaskbarRequestInProgress;

    protected override void OnStartup(StartupEventArgs e)
    {
        DiagnosticLog.Initialize();

        // Global Exception Handlers
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogError("AppDomain Unhandled Exception", args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogError("Dispatcher Unhandled Exception", args.Exception);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            LogError("TaskScheduler Unobserved Exception", args.Exception);
            args.SetObserved();
        };

        if (e.Args.Contains("--test"))
        {
            _ = ProgramTest.RunTest().ContinueWith(_ => Dispatcher.Invoke(Shutdown));
            return;
        }

        try
        {
            NativeMethods.SetCurrentProcessExplicitAppUserModelID(TaskbarPinService.AppUserModelId);
        }
        catch { }

        WM_SHOW_LAUNCHER = NativeMethods.RegisterWindowMessage("AppLauncher_Wakeup_Msg_9988");

        try
        {
            _mutex = new Mutex(true, MutexName, out _hasMutex);
        }
        catch
        {
            _hasMutex = false;
        }

        if (!_hasMutex)
        {
            // Another instance is already running -> Signal it to wake up & show window, then exit immediately!
            SendWakeupSignal();
            Environment.Exit(0);
            return;
        }

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            var config = LauncherConfig.Load();
            _config = config;
            LocalizationService.Initialize(config.Language);
            LocalizationService.LanguageChanged += OnLanguageChanged;

            _mainWindow = new MainWindow(config);
            MainWindow = _mainWindow;
            SetupTrayIcon();
            StartPipeServer();

            // Show window on first launch
            _mainWindow.Show();
            _mainWindow.ShowLauncher();
        }
        catch (Exception ex)
        {
            LogError("Startup Init Exception", ex);
        }
    }

    private void OnLanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateTrayContextMenu();
        });
    }

    private static void SendWakeupSignal()
    {
        try
        {
            NativeMethods.AllowSetForegroundWindow(-1);

            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(500);
            client.WriteByte(1);
            client.Flush();
        }
        catch
        {
            // Ignore
        }
    }

    private void StartPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    var b = server.ReadByte();
                    if (b != -1)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (_mainWindow != null)
                            {
                                if (!_mainWindow.IsLauncherShown || _mainWindow.Visibility != Visibility.Visible)
                                {
                                    _mainWindow.ShowLauncher(forceReanimate: true);
                                }
                                else
                                {
                                    _mainWindow.BringToFront();
                                }
                            }
                        });
                    }
                }
                catch
                {
                    if (token.IsCancellationRequested) break;
                    await Task.Delay(200, token);
                }
            }
        }, token);
    }

    private void SetupTrayIcon()
    {
        try
        {
            Icon? customIcon = null;
            try
            {
                var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(icoPath))
                {
                    using var fs = new FileStream(icoPath, FileMode.Open, FileAccess.Read);
                    customIcon = new Icon(fs);
                }
            }
            catch { }

            _notifyIcon = new NotifyIcon
            {
                Icon = customIcon ?? SystemIcons.Application,
                Text = "AppLauncher",
                Visible = true
            };

            UpdateTrayContextMenu();

            // Tray click logic:
            // When closed/hidden -> play center pop-out animation
            // When already open -> simply bring to topmost foreground (no replaying animation)
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (_mainWindow != null)
                    {
                        if (!_mainWindow.IsLauncherShown || _mainWindow.Visibility != Visibility.Visible)
                        {
                            _mainWindow.ShowLauncher(forceReanimate: true);
                        }
                        else
                        {
                            _mainWindow.BringToFront();
                        }
                    }
                }
            };
        }
        catch (Exception ex)
        {
            LogError("Tray Icon Init Exception", ex);
        }
    }

    private void UpdateTrayContextMenu()
    {
        if (_notifyIcon == null) return;

        try
        {
            _notifyIcon.ContextMenuStrip?.Dispose();
        }
        catch { }

        if (_config == null) return;
        var config = _config;
        var contextMenu = new ContextMenuStrip();

        // Language Submenu
        var langSubMenu = new ToolStripMenuItem(LocalizationService.Get("Tray_Language"));
        
        var zhItem = new ToolStripMenuItem("中文 (Chinese)")
        {
            Checked = LocalizationService.CurrentLanguage == LocalizationService.LanguageChinese
        };
        zhItem.Click += (s, e) =>
        {
            LocalizationService.SetLanguage(LocalizationService.LanguageChinese, config);
        };

        var enItem = new ToolStripMenuItem("English")
        {
            Checked = LocalizationService.CurrentLanguage == LocalizationService.LanguageEnglish
        };
        enItem.Click += (s, e) =>
        {
            LocalizationService.SetLanguage(LocalizationService.LanguageEnglish, config);
        };

        langSubMenu.DropDownItems.Add(zhItem);
        langSubMenu.DropDownItems.Add(enItem);

        var pinTaskbarItem = new ToolStripMenuItem(LocalizationService.Get("Tray_PinTaskbar"));
        pinTaskbarItem.Click += async (s, e) => await PinToTaskbarAsync(pinTaskbarItem);

        var exitItem = new ToolStripMenuItem(LocalizationService.Get("Tray_Exit"));
        exitItem.Click += (s, e) =>
        {
            try
            {
                if (_mainWindow != null)
                {
                    _mainWindow.IsExplicitExiting = true;
                    _mainWindow.Close();
                }
                _pipeCts?.Cancel();
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
                if (_hasMutex)
                {
                    try { _mutex?.ReleaseMutex(); } catch { }
                    _hasMutex = false;
                }
            }
            catch { }
            finally
            {
                Environment.Exit(0);
            }
        };

        contextMenu.Items.Add(langSubMenu);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(pinTaskbarItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.Text = "AppLauncher";
    }

    private async Task PinToTaskbarAsync(ToolStripMenuItem menuItem)
    {
        if (_pinTaskbarRequestInProgress) return;

        _pinTaskbarRequestInProgress = true;
        menuItem.Enabled = false;

        var window = _mainWindow;
        window?.BeginExternalInteraction();

        try
        {
            // TaskbarManager requires an explicit foreground interaction.
            window?.BringToFront();
            await Dispatcher.InvokeAsync(
                () => { },
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            var result = await TaskbarPinService.RequestPinCurrentApplicationAsync();
            var (messageKey, image) = result.Status switch
            {
                TaskbarPinStatus.Pinned => ("Tray_PinTaskbarSuccess", MessageBoxImage.Information),
                TaskbarPinStatus.AlreadyPinned => ("Tray_PinTaskbarAlreadyPinned", MessageBoxImage.Information),
                TaskbarPinStatus.UserDeclined => ("Tray_PinTaskbarDeclined", MessageBoxImage.Information),
                TaskbarPinStatus.NotAllowed => ("Tray_PinTaskbarNotAllowed", MessageBoxImage.Warning),
                TaskbarPinStatus.NotSupported => ("Tray_PinTaskbarNotSupported", MessageBoxImage.Warning),
                _ => ("Tray_PinTaskbarFailure", MessageBoxImage.Error)
            };

            if (window != null)
            {
                System.Windows.MessageBox.Show(
                    window,
                    LocalizationService.Get(messageKey),
                    "AppLauncher",
                    MessageBoxButton.OK,
                    image);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    LocalizationService.Get(messageKey),
                    "AppLauncher",
                    MessageBoxButton.OK,
                    image);
            }
        }
        catch (Exception ex)
        {
            LogError("Taskbar Pin UI Exception", ex);
            if (window != null)
            {
                System.Windows.MessageBox.Show(
                    window,
                    LocalizationService.Get("Tray_PinTaskbarFailure"),
                    "AppLauncher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    LocalizationService.Get("Tray_PinTaskbarFailure"),
                    "AppLauncher",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        finally
        {
            window?.EndExternalInteraction();
            menuItem.Enabled = true;
            _pinTaskbarRequestInProgress = false;
        }
    }

    private static void LogError(string context, Exception? ex)
    {
        DiagnosticLog.WriteCrash(context, ex);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts?.Cancel();
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        if (_hasMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
            _hasMutex = false;
        }

        base.OnExit(e);
    }
}
