using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using ExplorerBookmarkBar.Models;
using ExplorerBookmarkBar.Services;

namespace ExplorerBookmarkBar.Views
{
    public partial class SettingsWindow : Window
    {
        private static SettingsWindow _currentInstance;

        public static void ShowOrActivate(IntPtr callerHwnd = default(IntPtr))
        {
            if (callerHwnd == IntPtr.Zero)
            {
                callerHwnd = Win32Helper.GetForegroundWindow();
            }

            if (_currentInstance != null && _currentInstance.IsLoaded)
            {
                if (_currentInstance.WindowState == WindowState.Minimized)
                {
                    _currentInstance.WindowState = WindowState.Normal;
                }
                CenterOnHwnd(_currentInstance, callerHwnd);
                _currentInstance.Activate();
                _currentInstance.Focus();
                return;
            }

            _currentInstance = new SettingsWindow();
            _currentInstance.Closed += (s, e) => { _currentInstance = null; };
            CenterOnHwnd(_currentInstance, callerHwnd);
            ExplorerChildBarManager.SetActiveModalDialog(_currentInstance);
            _currentInstance.Show();
            _currentInstance.Activate();
        }

        private static void CenterOnHwnd(Window win, IntPtr hwnd)
        {
            try
            {
                System.Windows.Forms.Screen screen = (hwnd != IntPtr.Zero && Win32Helper.IsWindow(hwnd))
                    ? System.Windows.Forms.Screen.FromHandle(hwnd)
                    : System.Windows.Forms.Screen.PrimaryScreen;

                if (screen != null)
                {
                    var area = screen.WorkingArea;

                    // Get screen DPI scale for accurate multi-monitor DIP positioning
                    var source = PresentationSource.FromVisual(win);
                    double dpiX = (source != null && source.CompositionTarget != null) ? source.CompositionTarget.TransformToDevice.M11 : 1.0;
                    double dpiY = (source != null && source.CompositionTarget != null) ? source.CompositionTarget.TransformToDevice.M22 : 1.0;

                    if (dpiX <= 0) dpiX = 1.0;
                    if (dpiY <= 0) dpiY = 1.0;

                    double dipLeft = area.Left / dpiX;
                    double dipTop = area.Top / dpiY;
                    double dipWidth = area.Width / dpiX;
                    double dipHeight = area.Height / dpiY;

                    win.WindowStartupLocation = WindowStartupLocation.Manual;
                    win.Left = dipLeft + Math.Max(0, (dipWidth - win.Width) / 2);
                    win.Top = dipTop + Math.Max(0, (dipHeight - win.Height) / 2);

                    App.Log(string.Format("Positioned SettingsWindow on screen {0} (DPI {1:F2}) at ({2}, {3})", screen.DeviceName, dpiX, win.Left, win.Top));
                }
            }
            catch (Exception ex)
            {
                App.Log("CenterOnHwnd error: " + ex.Message);
            }
        }

        private readonly BookmarkManager _manager;
        private readonly AppSettings _backupSettings;

        public SettingsWindow()
        {
            InitializeComponent();
            _manager = BookmarkManager.Instance;
            Icon = Win32Helper.GetExplorerIconImageSource();

            // Deep clone backup settings in case of cancel
            _backupSettings = new AppSettings
            {
                TopOffset = _manager.Settings.TopOffset,
                BarHeight = _manager.Settings.BarHeight,
                LeftMargin = _manager.Settings.LeftMargin,
                RightMargin = _manager.Settings.RightMargin,
                ItemSpacing = _manager.Settings.ItemSpacing,
                ItemPadding = _manager.Settings.ItemPadding,
                AutoHideWhenInactive = _manager.Settings.AutoHideWhenInactive,
                StartWithWindows = _manager.Settings.StartWithWindows,
                UseAutoLayout = _manager.Settings.UseAutoLayout,
                EnableNumberShortcuts = _manager.Settings.EnableNumberShortcuts
            };

            ChkNumberShortcuts.IsChecked = _manager.Settings.EnableNumberShortcuts;

            // Set UI values
            SliderBarHeight.Value = _manager.Settings.BarHeight;
            SliderSpacing.Value = _manager.Settings.ItemSpacing;
            SliderPadding.Value = _manager.Settings.ItemPadding;

            TxtBarHeightVal.Text = _manager.Settings.BarHeight.ToString() + "px";
            TxtSpacingVal.Text = _manager.Settings.ItemSpacing.ToString() + "px";
            TxtPaddingVal.Text = _manager.Settings.ItemPadding.ToString() + "px";

            ChkAutoStart.IsChecked = Win32Helper.IsAutoStartEnabled();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            BtnCancel_Click(sender, e);
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtBarHeightVal == null || TxtSpacingVal == null || TxtPaddingVal == null) return;

            int barHeight = (int)SliderBarHeight.Value;
            int spacing = (int)SliderSpacing.Value;
            int padding = (int)SliderPadding.Value;

            TxtBarHeightVal.Text = barHeight.ToString() + "px";
            TxtSpacingVal.Text = spacing.ToString() + "px";
            TxtPaddingVal.Text = padding.ToString() + "px";
        }

        private void ApplyCurrentSettings()
        {
            _manager.Settings.BarHeight = (int)SliderBarHeight.Value;
            _manager.Settings.ItemSpacing = (int)SliderSpacing.Value;
            _manager.Settings.ItemPadding = (int)SliderPadding.Value;
            _manager.Settings.LeftMargin = 0;
            _manager.Settings.RightMargin = 0;
            _manager.Settings.StartWithWindows = ChkAutoStart.IsChecked == true;
            _manager.Settings.EnableNumberShortcuts = ChkNumberShortcuts.IsChecked == true;

            Win32Helper.SetAutoStart(_manager.Settings.StartWithWindows);
            _manager.SaveSettings();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            ApplyCurrentSettings();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            ApplyCurrentSettings();
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            // Restore backup
            _manager.Settings.BarHeight = _backupSettings.BarHeight;
            _manager.Settings.ItemSpacing = _backupSettings.ItemSpacing;
            _manager.Settings.ItemPadding = _backupSettings.ItemPadding;
            _manager.Settings.LeftMargin = 0;
            _manager.Settings.RightMargin = 0;
            _manager.Settings.StartWithWindows = _backupSettings.StartWithWindows;
            _manager.Settings.EnableNumberShortcuts = _backupSettings.EnableNumberShortcuts;

            Win32Helper.SetAutoStart(_backupSettings.StartWithWindows);
            _manager.SaveSettings();

            Close();
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export Bookmarks",
                Filter = "NavShelf bookmarks (*.json)|*.json|All files (*.*)|*.*",
                FileName = "NavShelf-bookmarks-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog(this) != true) return;

            try
            {
                _manager.ExportTo(dlg.FileName);
                MessageBox.Show(this, "Bookmarks exported to:\n" + dlg.FileName, "Export Bookmarks", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not export bookmarks:\n" + ex.Message, "Export Bookmarks", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Bookmarks",
                Filter = "NavShelf bookmarks (*.json)|*.json|All files (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog(this) != true) return;

            var choice = MessageBox.Show(this,
                "How should the imported bookmarks be added?\n\n" +
                "Yes  —  Merge: keep your current bookmarks and add the new ones\n" +
                "No   —  Replace: remove your current bookmarks first\n" +
                "Cancel  —  Don't import",
                "Import Bookmarks", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) return;

            try
            {
                int added = _manager.ImportFrom(dlg.FileName, replace: choice == MessageBoxResult.No);
                MessageBox.Show(this, added + " bookmark" + (added == 1 ? "" : "s") + " imported.", "Import Bookmarks", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "This file could not be imported. Nothing was changed.\n\n" + ex.Message, "Import Bookmarks", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show("Are you sure you want to reset bookmarks to defaults? Only 'This PC' will remain.", "Reset Bookmarks", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                _manager.AddDefaultBookmarks();
                MessageBox.Show("Bookmarks have been reset to default (This PC).", "Reset Bookmarks", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
