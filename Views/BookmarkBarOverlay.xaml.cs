using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ExplorerBookmarkBar.Models;
using ExplorerBookmarkBar.Services;

namespace ExplorerBookmarkBar.Views
{
    public class BookmarkDisplayItem
    {
        public BookmarkItem Source { get; set; }
        public string Name
        {
            get { return Source != null ? Source.Name : string.Empty; }
        }
        public string Path
        {
            get { return Source != null ? Source.Path : string.Empty; }
        }
        public Geometry IconGeometry { get; set; }
        public Brush IconBrush { get; set; }
        public Thickness ItemMargin { get; set; }
        public Thickness ItemPadding { get; set; }
    }

    public partial class BookmarkBarOverlay : Window
    {
        private const int GWLP_HWNDPARENT = -8;

        private readonly BookmarkManager _manager;
        private readonly DispatcherTimer _trackerTimer;
        private IntPtr _currentExplorerHwnd = IntPtr.Zero;
        private IntPtr _windowHwnd = IntPtr.Zero;
        private bool _isInteracting = false;
        private bool _isManuallyHidden = false;

        private readonly ObservableCollection<BookmarkDisplayItem> _displayItems = new ObservableCollection<BookmarkDisplayItem>();

        public BookmarkBarOverlay()
        {
            InitializeComponent();
            _manager = BookmarkManager.Instance;

            BookmarksItemsControl.ItemsSource = _displayItems;

            _manager.BookmarksChanged += RefreshDisplayItems;
            _manager.SettingsChanged += ApplySettings;

            RefreshDisplayItems();

            _trackerTimer = new DispatcherTimer(DispatcherPriority.Render);
            _trackerTimer.Interval = TimeSpan.FromMilliseconds(25);
            _trackerTimer.Tick += TrackerTimer_Tick;

            MouseEnter += (s, e) => { _isInteracting = true; };
            MouseLeave += (s, e) => { _isInteracting = false; };

            ExplorerHookEngine.Instance.ExplorerLayoutUpdated += OnExplorerLayoutUpdated;
            ExplorerHookEngine.Instance.ExplorerClosed += OnExplorerClosed;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowHwnd = new WindowInteropHelper(this).Handle;

            // Make it WS_EX_TOOLWINDOW (hide from Alt+Tab) and WS_EX_NOACTIVATE (don't steal focus)
            int exStyle = Win32Helper.GetWindowLong(_windowHwnd, Win32Helper.GWL_EXSTYLE);
            exStyle &= ~Win32Helper.WS_EX_TOPMOST; // NEVER topmost - stays strictly glued to Explorer Z-order!
            exStyle |= Win32Helper.WS_EX_TOOLWINDOW | Win32Helper.WS_EX_NOACTIVATE;
            Win32Helper.SetWindowLong(_windowHwnd, Win32Helper.GWL_EXSTYLE, exStyle);

            ApplySettings();
            _trackerTimer.Start();
            ExplorerHookEngine.Instance.Start();
        }

        public void ToggleVisibility()
        {
            _isManuallyHidden = !_isManuallyHidden;
            if (_isManuallyHidden)
            {
                Visibility = Visibility.Collapsed;
            }
        }

        private void ApplySettings()
        {
            var s = _manager.Settings;
            Height = s.BarHeight;
            if (s.CornerRadius > 0)
            {
                MainBorder.CornerRadius = new CornerRadius(0, 0, s.CornerRadius, s.CornerRadius);
                MainBorder.BorderThickness = new Thickness(1, 0, 1, 1);
            }
            else
            {
                MainBorder.CornerRadius = new CornerRadius(0);
                MainBorder.BorderThickness = new Thickness(0, 0, 0, 1);
            }
        }

        private void RefreshDisplayItems()
        {
            _displayItems.Clear();
            foreach (var b in _manager.Bookmarks)
            {
                BookmarkDisplayItem item = new BookmarkDisplayItem();
                item.Source = b;
                item.IconGeometry = GetGeometryForType(b.IconType);
                item.IconBrush = GetBrushForType(b.IconType, b.CustomColor);
                _displayItems.Add(item);
            }
        }

        private Geometry GetGeometryForType(string type)
        {
            string pathData;
            switch (type)
            {
                case "Drive":
                    pathData = "M6,2H18A2,2 0 0,1 20,4V20A2,2 0 0,1 18,22H6A2,2 0 0,1 4,20V4A2,2 0 0,1 6,2M12,4A1,1 0 0,0 11,5A1,1 0 0,0 12,6A1,1 0 0,0 13,5A1,1 0 0,0 12,4M12,18A1.5,1.5 0 0,0 10.5,19.5A1.5,1.5 0 0,0 12,21A1.5,1.5 0 0,0 13.5,19.5A1.5,1.5 0 0,0 12,18Z";
                    break;
                case "Download":
                    pathData = "M5,20H19V18H5M19,9H15V3H9V9H5L12,16L19,9Z";
                    break;
                case "Document":
                    pathData = "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z";
                    break;
                case "Desktop":
                    pathData = "M21,16H3V4H21M21,2H3C1.89,2 1,2.89 1,4V16A2,2 0 0,0 3,18H10V20H8V22H16V20H14V18H21A2,2 0 0,0 23,16V4C23,2.89 22.1,2 21,2Z";
                    break;
                case "Star":
                    pathData = "M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z";
                    break;
                case "Code":
                    pathData = "M14.6,16.6L19.2,12L14.6,7.4L16,6L22,12L16,18L14.6,16.6M9.4,16.6L4.8,12L9.4,7.4L8,6L2,12L8,18L9.4,16.6Z";
                    break;
                case "Image":
                    pathData = "M8.5,13.5L11,16.5L14.5,12L19,18H5M21,19V5C21,3.89 20.1,3 19,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19Z";
                    break;
                case "Music":
                    pathData = "M12,3V13.55A4,4 0 1,0 14,17V7H18V3H12Z";
                    break;
                case "Video":
                    pathData = "M17,10.5V7A1,1 0 0,0 16,6H4A1,1 0 0,0 3,7V17A1,1 0 0,0 4,18H16A1,1 0 0,0 17,17V13.5L21,17.5V6.5L17,10.5Z";
                    break;
                default: // Folder
                    pathData = "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z";
                    break;
            }
            try
            {
                return Geometry.Parse(pathData);
            }
            catch
            {
                return Geometry.Parse("M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z");
            }
        }

        private Brush GetBrushForType(string type, string customColor)
        {
            if (!string.IsNullOrEmpty(customColor))
            {
                try
                {
                    return (Brush)new BrushConverter().ConvertFromString(customColor);
                }
                catch { }
            }

            switch (type)
            {
                case "Drive":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CC2FF"));
                case "Download":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6CCB5F"));
                case "Document":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#29B6F6"));
                case "Desktop":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AB47BC"));
                case "Star":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD54F"));
                case "Code":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
                case "Image":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF7043"));
                case "Music":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EC407A"));
                case "Video":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252"));
                default: // Folder
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCA28"));
            }
        }

        private void OnExplorerLayoutUpdated(IntPtr hwnd, Win32Helper.RECT rect, int topBarHeight)
        {
            if (_isManuallyHidden) return;
            if (hwnd == _currentExplorerHwnd || _currentExplorerHwnd == IntPtr.Zero)
            {
                _currentExplorerHwnd = hwnd;
                PositionBar(hwnd, rect, topBarHeight);
            }
        }

        private void OnExplorerClosed(IntPtr hwnd)
        {
            if (_currentExplorerHwnd == hwnd)
            {
                _currentExplorerHwnd = IntPtr.Zero;
                Visibility = Visibility.Collapsed;
            }
        }

        private void TrackerTimer_Tick(object sender, EventArgs e)
        {
            if (_isManuallyHidden) return;

            IntPtr fgHwnd = Win32Helper.GetForegroundWindow();
            IntPtr rootHwnd;

            bool isExplorerFg = Win32Helper.IsExplorerWindow(fgHwnd, out rootHwnd);
            IntPtr activeExplorer = IntPtr.Zero;

            if (isExplorerFg)
            {
                activeExplorer = rootHwnd;
            }
            else if (_isInteracting && _currentExplorerHwnd != IntPtr.Zero)
            {
                activeExplorer = _currentExplorerHwnd;
            }
            else
            {
                // If another app (e.g. Chrome/VSCode) is foreground, check if user enabled AutoHide
                if (_manager.Settings.AutoHideWhenInactive)
                {
                    if (Visibility != Visibility.Collapsed) Visibility = Visibility.Collapsed;
                    return;
                }

                // Check if last explorer or any explorer is visible
                if (_currentExplorerHwnd != IntPtr.Zero && Win32Helper.IsWindowVisible(_currentExplorerHwnd) && !Win32Helper.IsIconic(_currentExplorerHwnd))
                {
                    activeExplorer = _currentExplorerHwnd;
                }
                else
                {
                    IntPtr visibleExp = Win32Helper.FindVisibleExplorerWindow();
                    if (visibleExp != IntPtr.Zero)
                    {
                        activeExplorer = visibleExp;
                    }
                }
            }

            if (activeExplorer != IntPtr.Zero)
            {
                _currentExplorerHwnd = activeExplorer;

                if (Win32Helper.IsIconic(activeExplorer))
                {
                    if (Visibility != Visibility.Collapsed)
                        Visibility = Visibility.Collapsed;
                    return;
                }

                Win32Helper.RECT rect;
                if (Win32Helper.GetWindowBounds(activeExplorer, out rect) && rect.Width > 150 && rect.Height > 150)
                {
                    var layout = Win32Helper.DetectExplorerLayout(activeExplorer);
                    PositionBar(activeExplorer, rect, layout.TopBarHeight);
                }
                else
                {
                    if (Visibility != Visibility.Collapsed)
                        Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                if (Visibility != Visibility.Collapsed)
                {
                    Visibility = Visibility.Collapsed;
                }
            }
        }

        private void PositionBar(IntPtr explorerHwnd, Win32Helper.RECT rect, int topBarHeight)
        {
            if (explorerHwnd == IntPtr.Zero || !Win32Helper.IsWindow(explorerHwnd)) return;

            // Link window ownership so it strictly follows Explorer Z-order and animations
            try
            {
                IntPtr currentOwner = Win32Helper.GetAncestor(_windowHwnd, Win32Helper.GA_ROOTOWNER);
                if (currentOwner != explorerHwnd)
                {
                    Win32Helper.SetWindowLong(_windowHwnd, GWLP_HWNDPARENT, explorerHwnd.ToInt32());
                }
            }
            catch { }

            var s = _manager.Settings;

            int effectiveTopOffset = s.UseAutoLayout ? topBarHeight : s.TopOffset;
            if (effectiveTopOffset < 110) effectiveTopOffset = 134;

            int targetLeft = rect.Left + s.LeftMargin;
            int targetTop = rect.Top + effectiveTopOffset;
            int targetWidth = rect.Width - s.LeftMargin - s.RightMargin;
            int targetHeight = s.BarHeight;

            if (targetWidth < 100) targetWidth = 100;

            // Get system DPI scaling factor
            double dpiX = 1.0, dpiY = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }
            if (dpiX <= 0) dpiX = 1.0;
            if (dpiY <= 0) dpiY = 1.0;

            // Update WPF layout bounds
            Left = targetLeft / dpiX;
            Top = targetTop / dpiY;
            Width = targetWidth / dpiX;
            Height = targetHeight / dpiY;

            // Position right above explorer with zero-lag HWND_TOP (NOT HWND_TOPMOST!)
            Win32Helper.SetWindowPos(
                _windowHwnd,
                Win32Helper.HWND_TOP,
                targetLeft,
                targetTop,
                targetWidth,
                targetHeight,
                Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_SHOWWINDOW | Win32Helper.SWP_NOOWNERZORDER
            );

            if (Visibility != Visibility.Visible)
            {
                Visibility = Visibility.Visible;
            }
        }

        private void BookmarkButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
            if (item == null) return;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                ExplorerComHelper.OpenInNewWindow(item.Path);
            }
            else
            {
                ExplorerComHelper.NavigateActiveExplorer(_currentExplorerHwnd, item.Path);
            }
        }

        private void BookmarkButton_RightClick(object sender, MouseButtonEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
                e.Handled = true;
            }
        }

        private void MenuOpenCurrent_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem != null ? menuItem.Parent as ContextMenu : null;
            var btn = contextMenu != null ? contextMenu.PlacementTarget as Button : null;
            var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
            if (item != null)
            {
                ExplorerComHelper.NavigateActiveExplorer(_currentExplorerHwnd, item.Path);
            }
        }

        private void MenuOpenNewWindow_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem != null ? menuItem.Parent as ContextMenu : null;
            var btn = contextMenu != null ? contextMenu.PlacementTarget as Button : null;
            var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
            if (item != null)
            {
                ExplorerComHelper.OpenInNewWindow(item.Path);
            }
        }

        private void MenuEdit_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem != null ? menuItem.Parent as ContextMenu : null;
            var btn = contextMenu != null ? contextMenu.PlacementTarget as Button : null;
            var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
            if (item != null)
            {
                var dlg = new BookmarkEditDialog(item.Source);
                dlg.ShowDialog();
            }
        }

        private void MenuCopyPath_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem != null ? menuItem.Parent as ContextMenu : null;
            var btn = contextMenu != null ? contextMenu.PlacementTarget as Button : null;
            var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
            if (item != null && !string.IsNullOrEmpty(item.Path))
            {
                try
                {
                    Clipboard.SetText(item.Path);
                }
                catch { }
            }
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem != null ? menuItem.Parent as ContextMenu : null;
            var btn = contextMenu != null ? contextMenu.PlacementTarget as Button : null;
            var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
            if (item != null)
            {
                _manager.RemoveBookmark(item.Source);
            }
        }

        private void BtnAddCurrent_Click(object sender, RoutedEventArgs e)
        {
            string currentPath = ExplorerComHelper.GetActiveExplorerPath(_currentExplorerHwnd);
            if (string.IsNullOrEmpty(currentPath))
            {
                currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            string name = Path.GetFileName(currentPath.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(name))
            {
                if (currentPath.Contains("::{20D04FE0")) name = "This PC";
                else name = currentPath;
            }

            var newItem = new BookmarkItem(name, currentPath, _manager.DetectIconType(currentPath));
            var dlg = new BookmarkEditDialog(newItem, isNew: true);
            dlg.ShowDialog();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow settingsWin = new SettingsWindow();
            settingsWin.ShowDialog();
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scv = sender as ScrollViewer;
            if (scv != null)
            {
                if (e.Delta < 0)
                    scv.LineRight();
                else
                    scv.LineLeft();
                e.Handled = true;
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    foreach (var path in files)
                    {
                        string name = Path.GetFileName(path.TrimEnd('\\', '/'));
                        if (string.IsNullOrEmpty(name)) name = path;
                        _manager.AddBookmark(name, path);
                    }
                }
            }
        }
    }
}
