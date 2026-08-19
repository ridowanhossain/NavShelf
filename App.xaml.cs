using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using ExplorerBookmarkBar.Models;
using ExplorerBookmarkBar.Services;
using ExplorerBookmarkBar.Views;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace ExplorerBookmarkBar
{
    public partial class App : Application
    {
        private const int HOTKEY_ID = 9001;
        private const int WM_HOTKEY = 0x0312;
        private const string MUTEX_NAME = "ExplorerBookmarkBar_SingleInstance_Mutex";

        private Mutex _mutex;
        private NotifyIcon _trayIcon;
        private HwndSource _hotkeyHwndSource;
        private static string _logFile;

        public static void Log(string msg)
        {
            try
            {
                if (string.IsNullOrEmpty(_logFile))
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    string dir = Path.Combine(appData, "ExplorerBookmarkBar");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    _logFile = Path.Combine(dir, "app.log");
                }
                File.AppendAllText(_logFile, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + msg + Environment.NewLine);
            }
            catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Log("FATAL UNHANDLED EXCEPTION: " + (args.ExceptionObject != null ? args.ExceptionObject.ToString() : "Unknown"));
            };

            DispatcherUnhandledException += (s, args) =>
            {
                Log("DISPATCHER EXCEPTION: " + (args.Exception != null ? args.Exception.ToString() : "Unknown"));
                args.Handled = true;
            };

            Log("Application starting...");

            ThemeManager.Initialize();

            base.OnStartup(e);

            bool createdNew;
            _mutex = new Mutex(true, MUTEX_NAME, out createdNew);
            if (!createdNew)
            {
                Log("Mutex already held by another instance. Exiting.");
                MessageBox.Show("Explorer Bookmark Bar is already running in the background. Check your system tray icons!", "Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            try
            {
                Log("Initializing BookmarkManager...");
                var manager = BookmarkManager.Instance;

                Log("Starting Native Child Bar Manager...");
                ExplorerChildBarManager.Instance.Start();

                Log("Setting up System Tray...");
                SetupSystemTray();

                Log("Setting up Global Hotkey...");
                SetupGlobalHotkey();

                Log("Application started successfully and running in background.");
            }
            catch (Exception ex)
            {
                Log("STARTUP ERROR: " + ex.ToString());
                MessageBox.Show("Error starting Explorer Bookmark Bar: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void SetupSystemTray()
        {
            _trayIcon = new NotifyIcon();
            _trayIcon.Text = "NavShelf - Explorer Bookmark Bar";
            _trayIcon.Visible = true;

            try
            {
                _trayIcon.Icon = SystemIcons.Application;
            }
            catch { }

            var contextMenu = new ContextMenuStrip();

            var itemToggle = new ToolStripMenuItem("📌 Toggle Bookmark Bar (Ctrl+Shift+B)", null, (s, e) =>
            {
                ExplorerChildBarManager.Instance.ToggleVisibility();
            });
            contextMenu.Items.Add(itemToggle);

            var itemAdd = new ToolStripMenuItem("➕ Add Current Folder", null, (s, e) =>
            {
                string path = ExplorerComHelper.GetActiveExplorerPath(IntPtr.Zero);
                if (string.IsNullOrEmpty(path))
                {
                    path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }
                string name = Path.GetFileName(path.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(name)) name = path;
                var dlg = new BookmarkEditDialog(new BookmarkItem(name, path, BookmarkManager.Instance.DetectIconType(path)), isNew: true);
                dlg.ShowDialog();
            });
            contextMenu.Items.Add(itemAdd);

            contextMenu.Items.Add(new ToolStripSeparator());

            var itemSettings = new ToolStripMenuItem("⚙️ Settings", null, (s, e) =>
            {
                SettingsWindow.ShowOrActivate();
            });
            contextMenu.Items.Add(itemSettings);

            contextMenu.Items.Add(new ToolStripSeparator());

            var itemExit = new ToolStripMenuItem("❌ Exit", null, (s, e) =>
            {
                ExitApplication();
            });
            contextMenu.Items.Add(itemExit);

            _trayIcon.ContextMenuStrip = contextMenu;
            _trayIcon.DoubleClick += (s, e) =>
            {
                SettingsWindow.ShowOrActivate();
            };
        }

        private void SetupGlobalHotkey()
        {
            try
            {
                HwndSourceParameters p = new HwndSourceParameters("ExplorerBookmarkBarHotkeySink")
                {
                    WindowStyle = Win32Helper.WS_POPUP,
                    ExtendedWindowStyle = Win32Helper.WS_EX_TOOLWINDOW | Win32Helper.WS_EX_NOACTIVATE,
                    ParentWindow = new IntPtr(-3), // HWND_MESSAGE - pure background message sink, never shown on taskbar
                    Width = 0,
                    Height = 0,
                    PositionX = -10000,
                    PositionY = -10000
                };
                _hotkeyHwndSource = new HwndSource(p);
                if (_hotkeyHwndSource != null)
                {
                    _hotkeyHwndSource.AddHook(HwndHook);
                    Win32Helper.RegisterHotKey(_hotkeyHwndSource.Handle, HOTKEY_ID, Win32Helper.MOD_CONTROL | Win32Helper.MOD_SHIFT | Win32Helper.MOD_NOREPEAT, 0x42);
                }
            }
            catch (Exception ex)
            {
                Log("Hotkey registration error: " + ex.Message);
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ExplorerChildBarManager.Instance.ToggleVisibility();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public static void ExitApp()
        {
            try
            {
                var app = Current as App;
                if (app != null)
                {
                    app.ExitApplication();
                }
                else
                {
                    Environment.Exit(0);
                }
            }
            catch
            {
                Environment.Exit(0);
            }
        }

        private void ExitApplication()
        {
            Log("ExitApplication called.");
            try
            {
                ExplorerChildBarManager.Instance.Stop();

                if (_hotkeyHwndSource != null)
                {
                    Win32Helper.UnregisterHotKey(_hotkeyHwndSource.Handle, HOTKEY_ID);
                    _hotkeyHwndSource.Dispose();
                }
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
                if (_mutex != null)
                {
                    _mutex.ReleaseMutex();
                    _mutex.Dispose();
                }
            }
            catch { }

            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log("OnExit event triggered with exit code: " + e.ApplicationExitCode);
            base.OnExit(e);
        }
    }
}
