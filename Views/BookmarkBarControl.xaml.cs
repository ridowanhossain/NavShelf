using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ExplorerBookmarkBar.Models;
using ExplorerBookmarkBar.Services;

namespace ExplorerBookmarkBar.Views
{
    public partial class BookmarkBarControl : UserControl
    {
        private readonly BookmarkManager _manager;
        private IntPtr _explorerHwnd;
        private readonly ObservableCollection<BookmarkDisplayItem> _displayItems = new ObservableCollection<BookmarkDisplayItem>();
        private readonly bool _isFileDialog;
        private Point? _dragStartPoint = null;
        private bool _isDragging = false;
        private const string DRAG_FORMAT = "NavShelfBookmarkItem";
        private const string THIS_PC_CLSID = "20D04FE0-3AEA-1069-A2D8-08002B30309D";

        private enum OpenMode { Current, NewTab, NewWindow }
        private enum DropZone { Left, Center, Right }

        private readonly Action _themeChangedHandler;
        private readonly Action<bool> _modalStateHandler;
        private readonly DispatcherTimer _missingCheckTimer;
        private bool _missingCheckRunning;
        private bool _detached;

        public BookmarkBarControl(IntPtr explorerHwnd)
        {
            InitializeComponent();
            _explorerHwnd = explorerHwnd;
            _manager = BookmarkManager.Instance;
            _isFileDialog = (Win32Helper.GetWindowClassName(explorerHwnd) == "#32770");

            BookmarksItemsControl.ItemsSource = _displayItems;

            if (_isFileDialog)
            {
                BtnAddCurrent.Visibility = Visibility.Collapsed;
                BtnSettings.Visibility = Visibility.Collapsed;
                BtnClose.Visibility = Visibility.Collapsed;
            }

            // Subscribe to app-wide events. These are long-lived (singleton/static), so every
            // subscription MUST be removed in Detach() or closed bars stay alive forever.
            _themeChangedHandler = () => Dispatcher.Invoke((Action)RefreshDisplayItems);
            _modalStateHandler = OnModalDialogStateChanged;
            _manager.BookmarksChanged += RefreshDisplayItems;
            _manager.SettingsChanged += ApplySettings;
            ThemeManager.ThemeChanged += _themeChangedHandler;
            ExplorerChildBarManager.ModalDialogStateChanged += _modalStateHandler;

            // Periodically re-check whether bookmarked folders exist (USB / network drives come and go)
            _missingCheckTimer = new DispatcherTimer(DispatcherPriority.Background);
            _missingCheckTimer.Interval = TimeSpan.FromSeconds(5);
            _missingCheckTimer.Tick += (s, e) => StartMissingCheck();
            _missingCheckTimer.Start();

            RefreshDisplayItems();
            ApplySettings();
            ApplyTheme();
            Loaded += (s, e) => UpdateOverflowVisibility();
        }

        private void OnModalDialogStateChanged(bool isDialogOpen)
        {
            Dispatcher.Invoke(() =>
            {
                BtnAddCurrent.IsEnabled = !isDialogOpen;
                BtnSettings.IsEnabled = !isDialogOpen;
                BtnOverflow.IsEnabled = !isDialogOpen;
                BtnClose.IsEnabled = !isDialogOpen;
                BtnAddCurrent.Opacity = isDialogOpen ? 0.35 : 1.0;
                BtnSettings.Opacity = isDialogOpen ? 0.35 : 1.0;
                BtnOverflow.Opacity = isDialogOpen ? 0.35 : 1.0;
                BtnClose.Opacity = isDialogOpen ? 0.35 : 1.0;
            });
        }

        /// <summary>
        /// Removes all app-wide event subscriptions. Call when the host Explorer window or
        /// file dialog goes away so this control can be garbage-collected.
        /// </summary>
        public void Detach()
        {
            if (_detached) return;
            _detached = true;
            _missingCheckTimer.Stop();
            _manager.BookmarksChanged -= RefreshDisplayItems;
            _manager.SettingsChanged -= ApplySettings;
            ThemeManager.ThemeChanged -= _themeChangedHandler;
            ExplorerChildBarManager.ModalDialogStateChanged -= _modalStateHandler;
        }

        public void ApplyTheme()
        {
            Dispatcher.Invoke((Action)(() =>
            {
                try
                {
                    var app = Application.Current;
                    if (app != null && app.Resources != null)
                    {
                        if (app.Resources.Contains("BarBackground"))
                        {
                            MainBorder.Background = (Brush)app.Resources["BarBackground"];
                        }
                        if (app.Resources.Contains("BarBorder"))
                        {
                            MainBorder.BorderBrush = (Brush)app.Resources["BarBorder"];
                        }
                    }
                    RefreshDisplayItems();
                    UpdateLayout();
                }
                catch { }
            }));
        }

        public void SetExplorerHwnd(IntPtr hwnd)
        {
            _explorerHwnd = hwnd;
        }

        private void ApplySettings()
        {
            MainBorder.Margin = new Thickness(0);
            MainBorder.CornerRadius = new CornerRadius(0);
            MainBorder.BorderThickness = new Thickness(0, 0, 0, 1);

            RefreshDisplayItems();
        }

        // ═════════════════════════════ Display items ═════════════════════════════

        private void RefreshDisplayItems()
        {
            if (_detached) return;

            var s = _manager.Settings;
            var margin = new Thickness(s.ItemSpacing, 1, s.ItemSpacing, 1);
            var padding = new Thickness(s.ItemPadding, 3, s.ItemPadding, 3);

            // Keep the last known "missing" state so items don't flicker while re-checking
            var previousMissing = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var previousIsDir = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var old in _displayItems)
            {
                if (!old.IsGroup && !string.IsNullOrEmpty(old.Path))
                {
                    previousMissing[old.Path] = old.IsMissing;
                    previousIsDir[old.Path] = old.ChevronVisibility == Visibility.Visible;
                }
            }

            _displayItems.Clear();
            int position = 0;
            foreach (var b in _manager.Bookmarks)
            {
                if (b == null) continue;
                position++;

                BookmarkDisplayItem item = new BookmarkDisplayItem();
                item.Source = b;
                item.Position = position;
                item.IconGeometry = GetGeometryForType(b.IsFolder ? "Group" : b.IconType);
                item.IconBrush = GetBrushForType(b.IsFolder ? "Group" : b.IconType, b.CustomColor);
                item.ItemMargin = margin;
                item.ItemPadding = padding;

                if (!b.IsFolder && !string.IsNullOrEmpty(b.Path))
                {
                    bool wasMissing, wasDir;
                    if (previousMissing.TryGetValue(b.Path, out wasMissing)) item.IsMissing = wasMissing;
                    if (IsThisPc(b.Path))
                    {
                        item.ChevronVisibility = Visibility.Visible;
                    }
                    else if (previousIsDir.TryGetValue(b.Path, out wasDir) && wasDir)
                    {
                        item.ChevronVisibility = Visibility.Visible;
                    }
                }
                _displayItems.Add(item);
            }

            Dispatcher.BeginInvoke(new Action(UpdateOverflowVisibility), DispatcherPriority.Background);
            StartMissingCheck();
        }

        private static bool IsThisPc(string path)
        {
            return !string.IsNullOrEmpty(path) && path.IndexOf(THIS_PC_CLSID, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>A normal drive or UNC path (not a shell namespace like ::{GUID}).</summary>
        private static bool IsFileSystemPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith("::", StringComparison.Ordinal) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return false;
            return (path.Length >= 2 && path[1] == ':') || path.StartsWith("\\\\", StringComparison.Ordinal);
        }

        /// <summary>
        /// Checks in the background whether each bookmarked folder still exists, then dims missing
        /// ones and enables the subfolder ▾ for real folders. Never blocks the UI (slow network drives).
        /// </summary>
        private void StartMissingCheck()
        {
            if (_detached || _missingCheckRunning || !IsVisible && IsLoaded) return;

            var targets = new List<BookmarkDisplayItem>();
            foreach (var d in _displayItems)
            {
                if (!d.IsGroup && IsFileSystemPath(d.Path)) targets.Add(d);
            }
            if (targets.Count == 0) return;

            var paths = targets.Select(t => t.Path).ToList();
            _missingCheckRunning = true;

            Task.Factory.StartNew(() =>
            {
                var results = new List<int>(); // 0 = missing, 1 = file, 2 = directory
                foreach (var p in paths)
                {
                    int state = 0;
                    try
                    {
                        if (Directory.Exists(p)) state = 2;
                        else if (File.Exists(p)) state = 1;
                    }
                    catch { }
                    results.Add(state);
                }
                return results;
            }).ContinueWith(task =>
            {
                _missingCheckRunning = false;
                if (_detached || task.IsFaulted) return;
                var results = task.Result;
                for (int i = 0; i < targets.Count && i < results.Count; i++)
                {
                    targets[i].IsMissing = results[i] == 0;
                    targets[i].ChevronVisibility = results[i] == 2 ? Visibility.Visible : Visibility.Collapsed;
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private Geometry GetGeometryForType(string type)
        {
            string pathData;
            switch (type)
            {
                case "Group":
                    pathData = "M22,4H14L12,2H6A2,2 0 0,0 4,4V16A2,2 0 0,0 6,18H22A2,2 0 0,0 24,16V6A2,2 0 0,0 22,4M2,6H0V11H0V20A2,2 0 0,0 2,22H20V20H2V6Z";
                    break;
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
                case "Group":
                    return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA726"));
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

        private bool CheckModalBlocking()
        {
            var modal = ExplorerChildBarManager.ActiveModalDialog;
            if (modal != null && modal.IsLoaded)
            {
                if (modal.WindowState == WindowState.Minimized)
                {
                    modal.WindowState = WindowState.Normal;
                }
                modal.Activate();
                modal.Focus();
                return true;
            }
            return false;
        }

        // ═════════════════════════════ Opening folders ═════════════════════════════

        private void OpenPath(string path, OpenMode mode)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (mode == OpenMode.NewWindow)
            {
                ExplorerComHelper.OpenInNewWindow(path);
            }
            else if (mode == OpenMode.NewTab && !_isFileDialog)
            {
                ExplorerComHelper.OpenInNewTab(_explorerHwnd, path);
            }
            else
            {
                ExplorerComHelper.NavigateActiveExplorer(_explorerHwnd, path);
            }
        }

        private static OpenMode ModeFromModifiers()
        {
            return Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? OpenMode.NewWindow : OpenMode.Current;
        }

        /// <summary>Alt+1..9: open the Nth bookmark (or drop down the Nth folder).</summary>
        public void ActivateItemAt(int position)
        {
            if (CheckModalBlocking()) return;
            int idx = position - 1;
            if (idx < 0 || idx >= _displayItems.Count) return;

            var item = _displayItems[idx];
            if (item.IsGroup)
            {
                var btn = FindChipButton(idx);
                if (btn != null)
                {
                    btn.BringIntoView();
                    ShowGroupMenu(btn, item.Source);
                }
            }
            else
            {
                OpenPath(item.Path, OpenMode.Current);
            }
        }

        private Button FindChipButton(int index)
        {
            var container = BookmarksItemsControl.ItemContainerGenerator.ContainerFromIndex(index) as ContentPresenter;
            if (container == null || container.ContentTemplate == null) return null;
            container.ApplyTemplate();
            return container.ContentTemplate.FindName("ChipButton", container) as Button;
        }

        // ═════════════════════════════ Chip mouse handling ═════════════════════════════

        private void BookmarkButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                e.Handled = true;
                if (CheckModalBlocking()) return;

                var btn = sender as Button;
                var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
                if (item != null && !item.IsGroup && !string.IsNullOrEmpty(item.Path))
                {
                    OpenPath(item.Path, OpenMode.NewTab);
                }
            }
            else if (e.ChangedButton == MouseButton.Left)
            {
                if (!CheckModalBlocking())
                {
                    _dragStartPoint = e.GetPosition(this);
                    _isDragging = false;
                }
            }
        }

        private void BookmarkButton_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _dragStartPoint.HasValue && !_isDragging)
            {
                Point currentPoint = e.GetPosition(this);
                Vector diff = _dragStartPoint.Value - currentPoint;

                double minDragDistance = Math.Max(12.0, SystemParameters.MinimumHorizontalDragDistance * 2.5);
                if (Math.Abs(diff.X) > minDragDistance || Math.Abs(diff.Y) > minDragDistance)
                {
                    var btn = sender as Button;
                    var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
                    if (item != null && item.Source != null)
                    {
                        _isDragging = true;
                        DataObject data = new DataObject();
                        data.SetData(DRAG_FORMAT, item.Source);

                        if (!item.IsGroup && IsFileSystemPath(item.Path) && !item.IsMissing)
                        {
                            var fileList = new System.Collections.Specialized.StringCollection();
                            fileList.Add(item.Path);
                            data.SetFileDropList(fileList);
                        }

                        try
                        {
                            DragDrop.DoDragDrop(btn, data, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
                        }
                        finally
                        {
                            _isDragging = false;
                            _dragStartPoint = null;
                            ClearDropIndicators();
                        }
                    }
                }
            }
        }

        private void BookmarkButton_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _dragStartPoint = null;
                _isDragging = false;
            }
            else if (e.ChangedButton == MouseButton.Middle)
            {
                e.Handled = true;
            }
        }

        private void BookmarkButton_Click(object sender, RoutedEventArgs e)
        {
            if (CheckModalBlocking()) return;

            var btn = sender as Button;
            var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
            if (item == null) return;

            if (item.IsGroup)
            {
                ShowGroupMenu(btn, item.Source);
                return;
            }

            OpenPath(item.Path, ModeFromModifiers());
        }

        private void SubfolderChevron_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (CheckModalBlocking()) return;

            var el = sender as FrameworkElement;
            var item = el != null ? el.Tag as BookmarkDisplayItem : null;
            if (item == null || item.IsGroup) return;

            var menu = CreateMenu(el);
            PopulateSubfolders(menu.Items, item.Path, menu);
            menu.IsOpen = true;
        }

        private void BookmarkButton_RightClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_isFileDialog || CheckModalBlocking()) return;

            var btn = sender as Button;
            var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
            if (item == null) return;

            ContextMenu menu = item.IsGroup
                ? BuildGroupContextMenu(btn, item.Source)
                : BuildBookmarkContextMenu(btn, item.Source, null);
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        }

        private void BarBackground_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (e.Handled || _isFileDialog || CheckModalBlocking()) return;
            e.Handled = true;

            var menu = CreateMenu(BookmarksScrollViewer);
            menu.Placement = PlacementMode.MousePoint;
            menu.Items.Add(MakeActionItem("Add Current Folder...", () => BtnAddCurrent_Click(this, new RoutedEventArgs())));
            menu.Items.Add(MakeActionItem("New Bookmark Folder...", () => PromptNewGroup()));
            menu.Items.Add(NewSeparator());
            menu.Items.Add(MakeActionItem("Settings...", () => SettingsWindow.ShowOrActivate(_explorerHwnd)));
            menu.IsOpen = true;
        }

        // ═════════════════════════════ Menu building helpers ═════════════════════════════

        private ContextMenu CreateMenu(UIElement target)
        {
            var menu = new ContextMenu();
            menu.PlacementTarget = target;
            menu.Placement = PlacementMode.Bottom;
            try
            {
                menu.Style = (Style)FindResource(typeof(ContextMenu));
                menu.Resources[typeof(MenuItem)] = FindResource(typeof(MenuItem));
            }
            catch { }
            return menu;
        }

        private Separator NewSeparator()
        {
            var sep = new Separator();
            sep.SetResourceReference(Control.BackgroundProperty, "MenuSeparator");
            return sep;
        }

        private FrameworkElement MakeHeader(Geometry icon, Brush iconBrush, string text, bool dim)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            if (icon != null)
            {
                var path = new System.Windows.Shapes.Path
                {
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Stretch = Stretch.Uniform,
                    Data = icon,
                    Fill = iconBrush
                };
                sp.Children.Add(path);
            }
            var txt = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                MaxWidth = 360,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            txt.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            sp.Children.Add(txt);
            if (dim) sp.Opacity = 0.45;
            return sp;
        }

        private MenuItem MakeActionItem(string text, Action action)
        {
            var mi = new MenuItem { Header = text };
            mi.Click += (s, e) =>
            {
                if (e.OriginalSource != mi) return;
                if (CheckModalBlocking()) return;
                action();
            };
            return mi;
        }

        private MenuItem MakeInfoItem(string text)
        {
            return new MenuItem { Header = text, IsEnabled = false };
        }

        /// <summary>Finds the MenuItem that directly owns the element that was clicked.</summary>
        private static MenuItem NearestMenuItem(DependencyObject d)
        {
            while (d != null)
            {
                var mi = d as MenuItem;
                if (mi != null) return mi;
                DependencyObject parent = null;
                if (d is Visual || d is System.Windows.Media.Media3D.Visual3D) parent = VisualTreeHelper.GetParent(d);
                if (parent == null) parent = LogicalTreeHelper.GetParent(d);
                d = parent;
            }
            return null;
        }

        /// <summary>
        /// Makes a menu entry open a folder: left click = current tab (Ctrl = new window),
        /// middle click = new tab. Works for entries that also have a subfolder submenu.
        /// </summary>
        private void AttachOpenHandlers(MenuItem mi, string path, ContextMenu root)
        {
            mi.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (NearestMenuItem(e.OriginalSource as DependencyObject) != mi) return;
                e.Handled = true;
                root.IsOpen = false;
                if (CheckModalBlocking()) return;
                OpenPath(path, ModeFromModifiers());
            };
            mi.PreviewMouseDown += (s, e) =>
            {
                if (e.ChangedButton != MouseButton.Middle) return;
                if (NearestMenuItem(e.OriginalSource as DependencyObject) != mi) return;
                e.Handled = true;
                root.IsOpen = false;
                if (CheckModalBlocking()) return;
                OpenPath(path, OpenMode.NewTab);
            };
            mi.PreviewMouseUp += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle) e.Handled = true;
            };
            // Keyboard (Enter) and accessibility tools raise Click instead of mouse events.
            // Mouse clicks never get here: the handler above marks the mouse-up as handled.
            mi.Click += (s, e) =>
            {
                if (e.OriginalSource != mi) return;
                root.IsOpen = false;
                if (CheckModalBlocking()) return;
                OpenPath(path, OpenMode.Current);
            };
        }

        // ═════════════════════════════ Subfolder browsing ═════════════════════════════

        private class SubFolder
        {
            public string Name;
            public string FullPath;
            public bool HasChildren;
            public bool IsDrive;
        }

        private const int MaxSubfolders = 300;

        /// <summary>
        /// Lists subfolders (or drives, for This PC) without hanging the UI on slow/offline drives.
        /// Returns null if the folder did not respond in time.
        /// </summary>
        private static List<SubFolder> ListSubfolders(string path, out int totalCount)
        {
            totalCount = 0;
            var task = Task.Factory.StartNew(() =>
            {
                var list = new List<SubFolder>();
                int total = 0;
                if (IsThisPc(path))
                {
                    foreach (var drive in DriveInfo.GetDrives())
                    {
                        string letter = drive.Name.TrimEnd('\\');
                        string label = "";
                        try { if (drive.IsReady) label = drive.VolumeLabel; } catch { }
                        if (string.IsNullOrEmpty(label))
                        {
                            label = drive.DriveType == DriveType.Removable ? "USB Drive"
                                  : drive.DriveType == DriveType.Network ? "Network Drive"
                                  : drive.DriveType == DriveType.CDRom ? "DVD Drive" : "Local Disk";
                        }
                        list.Add(new SubFolder { Name = label + " (" + letter + ")", FullPath = drive.Name, HasChildren = true, IsDrive = true });
                    }
                    return Tuple.Create(list, list.Count);
                }

                var di = new DirectoryInfo(path);
                var dirs = new List<DirectoryInfo>();
                foreach (var d in di.EnumerateDirectories())
                {
                    try
                    {
                        if ((d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    }
                    catch { continue; }
                    total++;
                    dirs.Add(d);
                }
                dirs.Sort((a, b) => Win32Helper.StrCmpLogicalW(a.Name, b.Name));
                if (dirs.Count > MaxSubfolders) dirs.RemoveRange(MaxSubfolders, dirs.Count - MaxSubfolders);

                // Peek one level deeper (time-boxed) so leaf folders don't show a submenu arrow
                var sw = Stopwatch.StartNew();
                foreach (var d in dirs)
                {
                    bool hasChildren = true;
                    if (sw.ElapsedMilliseconds < 700)
                    {
                        try { hasChildren = d.EnumerateDirectories().Any(x => (x.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0); }
                        catch { hasChildren = false; }
                    }
                    list.Add(new SubFolder { Name = d.Name, FullPath = d.FullName, HasChildren = hasChildren });
                }
                return Tuple.Create(list, total);
            });

            try
            {
                if (!task.Wait(2000)) return null;
                totalCount = task.Result.Item2;
                return task.Result.Item1;
            }
            catch (AggregateException ex)
            {
                App.Log("ListSubfolders failed for " + path + ": " + (ex.InnerException != null ? ex.InnerException.Message : ex.Message));
                return new List<SubFolder>();
            }
        }

        /// <summary>Fills a menu with the subfolders of a path. Deeper levels load when hovered.</summary>
        private void PopulateSubfolders(ItemCollection items, string path, ContextMenu root)
        {
            int total;
            var folders = ListSubfolders(path, out total);
            if (folders == null)
            {
                items.Add(MakeInfoItem("(Folder is not responding)"));
                return;
            }
            if (folders.Count == 0)
            {
                items.Add(MakeInfoItem("(No subfolders)"));
                return;
            }

            var folderGeo = GetGeometryForType("Folder");
            var folderBrush = GetBrushForType("Folder", null);
            var driveGeo = GetGeometryForType("Drive");
            var driveBrush = GetBrushForType("Drive", null);

            foreach (var f in folders)
            {
                var folder = f;
                var mi = new MenuItem();
                mi.Header = MakeHeader(folder.IsDrive ? driveGeo : folderGeo, folder.IsDrive ? driveBrush : folderBrush, folder.Name, false);
                mi.ToolTip = folder.FullPath;
                AttachOpenHandlers(mi, folder.FullPath, root);

                if (folder.HasChildren)
                {
                    mi.Items.Add(MakeInfoItem("Loading..."));
                    bool loaded = false;
                    mi.SubmenuOpened += (s, e) =>
                    {
                        if (e.OriginalSource != mi || loaded) return;
                        loaded = true;
                        mi.Items.Clear();
                        PopulateSubfolders(mi.Items, folder.FullPath, root);
                    };
                }
                items.Add(mi);
            }

            if (total > folders.Count)
            {
                items.Add(MakeInfoItem("… " + (total - folders.Count) + " more (open the folder to see all)"));
            }
        }

        // ═════════════════════════════ Bookmark folders (groups) ═════════════════════════════

        private void ShowGroupMenu(FrameworkElement target, BookmarkItem group)
        {
            var root = CreateMenu(target);

            if (group.Children == null || group.Children.Count == 0)
            {
                root.Items.Add(MakeInfoItem("(Empty — drag bookmarks onto this folder)"));
            }
            else
            {
                foreach (var c in group.Children)
                {
                    var child = c;
                    bool isDir = IsFileSystemPath(child.Path) || IsThisPc(child.Path);
                    bool missing = IsFileSystemPath(child.Path) && !QuickExists(child.Path);

                    var mi = new MenuItem();
                    mi.Header = MakeHeader(GetGeometryForType(child.IconType), GetBrushForType(child.IconType, child.CustomColor), child.Name, missing);
                    mi.ToolTip = missing ? "Folder not found: " + child.Path : child.Path;
                    AttachOpenHandlers(mi, child.Path, root);

                    // Right-click an entry: manage it (opens a separate menu)
                    mi.PreviewMouseRightButtonUp += (s, e) =>
                    {
                        if (NearestMenuItem(e.OriginalSource as DependencyObject) != mi) return;
                        e.Handled = true;
                        root.IsOpen = false;
                        if (_isFileDialog || CheckModalBlocking()) return;
                        var cm = BuildBookmarkContextMenu(target, child, group);
                        cm.Placement = PlacementMode.MousePoint;
                        cm.IsOpen = true;
                    };

                    if (isDir && !missing)
                    {
                        mi.Items.Add(MakeInfoItem("Loading..."));
                        bool loaded = false;
                        mi.SubmenuOpened += (s, e) =>
                        {
                            if (e.OriginalSource != mi || loaded) return;
                            loaded = true;
                            mi.Items.Clear();
                            PopulateSubfolders(mi.Items, child.Path, root);
                        };
                    }
                    root.Items.Add(mi);
                }
            }

            if (!_isFileDialog)
            {
                root.Items.Add(NewSeparator());
                root.Items.Add(MakeActionItem("Add Current Folder Here", () => AddCurrentFolderToGroup(group)));
            }

            root.IsOpen = true;
        }

        private static bool QuickExists(string path)
        {
            try
            {
                // Avoid hanging on offline network shares
                if (path.StartsWith("\\\\", StringComparison.Ordinal)) return true;
                return Directory.Exists(path) || File.Exists(path);
            }
            catch { return true; }
        }

        private ContextMenu BuildBookmarkContextMenu(UIElement target, BookmarkItem bookmark, BookmarkItem parentGroup)
        {
            var menu = CreateMenu(target);
            string path = bookmark.Path;

            menu.Items.Add(MakeActionItem("Open in Current Tab", () => OpenPath(path, OpenMode.Current)));
            menu.Items.Add(MakeActionItem("Open in New Tab", () => OpenPath(path, OpenMode.NewTab)));
            menu.Items.Add(MakeActionItem("Open in New Window", () => OpenPath(path, OpenMode.NewWindow)));
            menu.Items.Add(NewSeparator());
            menu.Items.Add(MakeActionItem("Edit Bookmark...", () =>
            {
                var dlg = new BookmarkEditDialog(bookmark, isNew: false, callerHwnd: _explorerHwnd);
                dlg.ShowDialog();
            }));
            menu.Items.Add(MakeActionItem("Copy Path", () =>
            {
                try { Clipboard.SetText(path ?? string.Empty); } catch { }
            }));

            // Move to Folder ▸
            var moveTo = new MenuItem { Header = "Move to Folder" };
            foreach (var g in _manager.GetGroups())
            {
                var grp = g;
                if (grp == parentGroup) continue;
                var gi = new MenuItem { Header = MakeHeader(GetGeometryForType("Group"), GetBrushForType("Group", null), grp.Name, false) };
                gi.Click += (s, e) =>
                {
                    if (e.OriginalSource != gi || CheckModalBlocking()) return;
                    _manager.MoveIntoGroup(bookmark, grp);
                };
                moveTo.Items.Add(gi);
            }
            if (moveTo.Items.Count > 0) moveTo.Items.Add(NewSeparator());
            var newFolderItem = new MenuItem { Header = "New Folder..." };
            newFolderItem.Click += (s, e) =>
            {
                if (e.OriginalSource != newFolderItem || CheckModalBlocking()) return;
                var g = PromptNewGroup();
                if (g != null) _manager.MoveIntoGroup(bookmark, g);
            };
            moveTo.Items.Add(newFolderItem);
            menu.Items.Add(moveTo);

            if (parentGroup != null)
            {
                menu.Items.Add(MakeActionItem("Move Out to Bar", () => _manager.MoveOutOfGroup(bookmark)));
            }

            menu.Items.Add(NewSeparator());
            menu.Items.Add(MakeActionItem("Delete Bookmark", () => _manager.RemoveBookmark(bookmark)));
            return menu;
        }

        private ContextMenu BuildGroupContextMenu(FrameworkElement target, BookmarkItem group)
        {
            var menu = CreateMenu(target);
            menu.Items.Add(MakeActionItem("Show Bookmarks", () => ShowGroupMenu(target, group)));
            menu.Items.Add(MakeActionItem("Add Current Folder Here", () => AddCurrentFolderToGroup(group)));
            menu.Items.Add(NewSeparator());
            menu.Items.Add(MakeActionItem("Rename Folder...", () =>
            {
                var dlg = new BookmarkEditDialog(group, isNew: false, callerHwnd: _explorerHwnd);
                dlg.ShowDialog();
            }));
            menu.Items.Add(MakeActionItem("New Folder...", () => PromptNewGroup()));
            menu.Items.Add(NewSeparator());
            menu.Items.Add(MakeActionItem("Delete Folder", () =>
            {
                int n = group.Children != null ? group.Children.Count : 0;
                string msg = n > 0
                    ? "Delete the folder \"" + group.Name + "\"?\n\nIts " + n + " bookmark" + (n == 1 ? "" : "s") + " will be moved back to the bar."
                    : "Delete the empty folder \"" + group.Name + "\"?";
                if (MessageBox.Show(msg, "Delete Folder", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _manager.DeleteGroup(group, true);
                }
            }));
            return menu;
        }

        /// <summary>Asks for a name and creates a new bookmark folder. Returns it, or null if cancelled.</summary>
        private BookmarkItem PromptNewGroup()
        {
            var group = new BookmarkItem("New Folder", "", "Group", true);
            var dlg = new BookmarkEditDialog(group, isNew: true, callerHwnd: _explorerHwnd);
            dlg.ShowDialog();
            return _manager.Bookmarks.Contains(group) ? group : null;
        }

        private void AddCurrentFolderToGroup(BookmarkItem group)
        {
            string path = ExplorerComHelper.GetActiveExplorerPath(_explorerHwnd);
            if (string.IsNullOrEmpty(path)) return;
            string name = IsThisPc(path) ? "This PC" : null;
            if (IsThisPc(path)) path = "::{" + THIS_PC_CLSID + "}";
            _manager.AddBookmarkToGroup(group, name, path);
        }

        // ═════════════════════════════ Toolbar buttons ═════════════════════════════

        private void BtnAddCurrent_Click(object sender, RoutedEventArgs e)
        {
            if (CheckModalBlocking()) return;

            string currentPath = ExplorerComHelper.GetActiveExplorerPath(_explorerHwnd);
            if (string.IsNullOrEmpty(currentPath))
            {
                currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            string name = "";
            string iconType = "Folder";

            if (currentPath.Contains(THIS_PC_CLSID))
            {
                name = "This PC";
                currentPath = "::{" + THIS_PC_CLSID + "}";
                iconType = "Drive";
            }
            else if (currentPath.Contains("679F8FB0-7208-4EB2-9CD0-CF05408BD86D") || currentPath.Contains("59031A47-3F72-44A7-89C5-5595FE6B30EE"))
            {
                name = "Home";
                iconType = "Folder";
            }
            else if (currentPath.Contains("645FF040-5081-101B-9F08-00AA002F954E"))
            {
                name = "Recycle Bin";
                iconType = "Folder";
            }
            else if (currentPath.Contains("F02C1A0D-BE21-4350-88B0-7367FC96EF3C"))
            {
                name = "Network";
                iconType = "Folder";
            }
            else
            {
                string clean = currentPath.TrimEnd('\\', '/');
                name = Path.GetFileName(clean);
                if (string.IsNullOrEmpty(name))
                {
                    name = clean;
                }

                if (name.StartsWith("{") && name.EndsWith("}"))
                {
                    name = "This PC";
                }

                iconType = _manager.DetectIconType(currentPath);
            }

            var newItem = new BookmarkItem(name, currentPath, iconType);
            var dlg = new BookmarkEditDialog(newItem, isNew: true, callerHwnd: _explorerHwnd);
            dlg.ShowDialog();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            if (CheckModalBlocking()) return;

            try
            {
                App.Log("Settings button clicked on Explorer HWND 0x" + _explorerHwnd.ToString("X"));
                var anim = new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(450))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                };
                SettingsIconRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
            }
            catch { }

            SettingsWindow.ShowOrActivate(_explorerHwnd);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (CheckModalBlocking()) return;
            App.ExitApp();
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

        // ═════════════════════════════ Drag & drop ═════════════════════════════
        //
        // Dropping onto a bookmark chip:
        //   • left/right quarter  → insert a bookmark there / reorder (blue line indicator)
        //   • middle              → bookmark folder: move the bookmark into it / add dropped folders to it
        //                         → normal folder:   copy or move the dropped files INTO that folder
        //                            (same drive = move, other drive = copy; hold Ctrl = copy, Shift = move)

        private static DropZone GetDropZone(FrameworkElement target, DragEventArgs e)
        {
            double x = e.GetPosition(target).X;
            double w = Math.Max(1.0, target.ActualWidth);
            if (x < w * 0.25) return DropZone.Left;
            if (x > w * 0.75) return DropZone.Right;
            return DropZone.Center;
        }

        private static string[] GetDroppedFiles(DragEventArgs e)
        {
            try { return e.Data.GetData(DataFormats.FileDrop) as string[]; }
            catch { return null; }
        }

        /// <summary>Can the dropped files be copied/moved into this bookmark's folder?</summary>
        private static bool CanTransferInto(BookmarkDisplayItem target, string[] files)
        {
            if (target == null || target.IsGroup || target.IsMissing || files == null || files.Length == 0) return false;
            if (!IsFileSystemPath(target.Path) || target.ChevronVisibility != Visibility.Visible) return false; // verified directory

            string dest = target.Path.TrimEnd('\\') + "\\";
            foreach (var f in files)
            {
                if (string.IsNullOrEmpty(f)) return false;
                string src = f.TrimEnd('\\');
                // Not into itself / its own subfolder, and not where it already is
                if (dest.StartsWith(src + "\\", StringComparison.OrdinalIgnoreCase)) return false;
                string parent = Path.GetDirectoryName(src);
                if (parent != null && string.Equals(parent.TrimEnd('\\') + "\\", dest, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private static DragDropEffects TransferEffect(DragEventArgs e, string[] files, string destDir)
        {
            bool ctrl = (e.KeyStates & DragDropKeyStates.ControlKey) != 0;
            bool shift = (e.KeyStates & DragDropKeyStates.ShiftKey) != 0;
            DragDropEffects wanted;
            if (ctrl) wanted = DragDropEffects.Copy;
            else if (shift) wanted = DragDropEffects.Move;
            else
            {
                string destRoot = Path.GetPathRoot(destDir) ?? "";
                string srcRoot = Path.GetPathRoot(files[0]) ?? "";
                wanted = string.Equals(destRoot, srcRoot, StringComparison.OrdinalIgnoreCase) ? DragDropEffects.Move : DragDropEffects.Copy;
            }
            if ((e.AllowedEffects & wanted) == 0)
            {
                wanted = (e.AllowedEffects & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;
            }
            return wanted;
        }

        private void BookmarkButton_DragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (CheckModalBlocking())
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            var targetBtn = sender as Button;
            var target = targetBtn != null ? targetBtn.Tag as BookmarkDisplayItem : null;
            if (target == null) return;

            DropZone zone = GetDropZone(targetBtn, e);

            if (e.Data.GetDataPresent(DRAG_FORMAT))
            {
                var dragged = e.Data.GetData(DRAG_FORMAT) as BookmarkItem;
                if (target.IsGroup && zone == DropZone.Center && dragged != null && !dragged.IsFolder && dragged != target.Source)
                {
                    ShowDropTarget(target);
                    e.Effects = DragDropEffects.Move;
                }
                else
                {
                    UpdateSingleDropIndicator(target, zone != DropZone.Left && (zone == DropZone.Right || e.GetPosition(targetBtn).X > targetBtn.ActualWidth / 2.0));
                    e.Effects = DragDropEffects.Move;
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = GetDroppedFiles(e);
                if (zone == DropZone.Center && target.IsGroup)
                {
                    ShowDropTarget(target);
                    e.Effects = DragDropEffects.Link;
                }
                else if (zone == DropZone.Center && CanTransferInto(target, files))
                {
                    ShowDropTarget(target);
                    e.Effects = TransferEffect(e, files, target.Path);
                }
                else
                {
                    UpdateSingleDropIndicator(target, e.GetPosition(targetBtn).X > targetBtn.ActualWidth / 2.0);
                    e.Effects = DragDropEffects.Link;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void BookmarkButton_DragLeave(object sender, DragEventArgs e)
        {
            var targetBtn = sender as Button;
            var target = targetBtn != null ? targetBtn.Tag as BookmarkDisplayItem : null;
            if (target != null)
            {
                target.DropIndicatorLeftVisibility = Visibility.Collapsed;
                target.DropIndicatorRightVisibility = Visibility.Collapsed;
                target.IsDropTarget = false;
            }
        }

        private void BookmarkButton_Drop(object sender, DragEventArgs e)
        {
            ClearDropIndicators();
            e.Handled = true;

            if (CheckModalBlocking()) return;

            var targetBtn = sender as Button;
            var target = targetBtn != null ? targetBtn.Tag as BookmarkDisplayItem : null;
            if (target == null || target.Source == null) return;

            DropZone zone = GetDropZone(targetBtn, e);
            bool isRightHalf = e.GetPosition(targetBtn).X > (targetBtn.ActualWidth / 2.0);

            if (e.Data.GetDataPresent(DRAG_FORMAT))
            {
                var draggedSource = e.Data.GetData(DRAG_FORMAT) as BookmarkItem;
                if (draggedSource == null || draggedSource == target.Source) return;

                if (target.IsGroup && zone == DropZone.Center && !draggedSource.IsFolder)
                {
                    _manager.MoveIntoGroup(draggedSource, target.Source);
                    return;
                }

                int oldIndex = _manager.Bookmarks.IndexOf(draggedSource);
                int targetIndex = _manager.Bookmarks.IndexOf(target.Source);

                if (oldIndex >= 0 && targetIndex >= 0)
                {
                    int newIndex;
                    if (oldIndex < targetIndex)
                    {
                        newIndex = isRightHalf ? targetIndex : Math.Max(0, targetIndex - 1);
                    }
                    else
                    {
                        newIndex = isRightHalf ? Math.Min(_manager.Bookmarks.Count - 1, targetIndex + 1) : targetIndex;
                    }

                    if (newIndex != oldIndex)
                    {
                        _manager.MoveBookmark(oldIndex, newIndex);
                    }
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = GetDroppedFiles(e);
                if (files == null || files.Length == 0) return;

                if (zone == DropZone.Center && target.IsGroup)
                {
                    foreach (var path in files)
                    {
                        _manager.AddBookmarkToGroup(target.Source, null, path);
                    }
                    return;
                }

                if (zone == DropZone.Center && CanTransferInto(target, files))
                {
                    var effect = TransferEffect(e, files, target.Path);
                    if (effect != DragDropEffects.None)
                    {
                        e.Effects = effect;
                        Win32Helper.ShellTransfer(_explorerHwnd, files, target.Path, effect == DragDropEffects.Move);
                    }
                    return;
                }

                int targetIndex = _manager.Bookmarks.IndexOf(target.Source);
                int insertIndex = isRightHalf ? targetIndex + 1 : targetIndex;
                insertIndex = Math.Max(0, Math.Min(_manager.Bookmarks.Count, insertIndex));

                foreach (var path in files)
                {
                    string name = Path.GetFileName(path.TrimEnd('\\', '/'));
                    if (string.IsNullOrEmpty(name)) name = path;
                    _manager.InsertBookmark(insertIndex, name, path);
                    insertIndex++;
                }
            }
        }

        private void UserControl_DragOver(object sender, DragEventArgs e)
        {
            if (CheckModalBlocking())
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(DRAG_FORMAT))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Link;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void UserControl_DragLeave(object sender, DragEventArgs e)
        {
            ClearDropIndicators();
        }

        private void UserControl_Drop(object sender, DragEventArgs e)
        {
            ClearDropIndicators();

            if (CheckModalBlocking())
            {
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(DRAG_FORMAT))
            {
                var draggedSource = e.Data.GetData(DRAG_FORMAT) as BookmarkItem;
                if (draggedSource != null)
                {
                    int oldIndex = _manager.Bookmarks.IndexOf(draggedSource);
                    int lastIndex = _manager.Bookmarks.Count - 1;
                    if (oldIndex >= 0 && oldIndex != lastIndex && lastIndex >= 0)
                    {
                        _manager.MoveBookmark(oldIndex, lastIndex);
                    }
                }
                e.Handled = true;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = GetDroppedFiles(e);
                if (files != null && files.Length > 0)
                {
                    foreach (var path in files)
                    {
                        string name = Path.GetFileName(path.TrimEnd('\\', '/'));
                        if (string.IsNullOrEmpty(name)) name = path;
                        _manager.AddBookmark(name, path);
                    }
                }
                e.Handled = true;
            }
        }

        private void ShowDropTarget(BookmarkDisplayItem target)
        {
            foreach (var item in _displayItems)
            {
                item.DropIndicatorLeftVisibility = Visibility.Collapsed;
                item.DropIndicatorRightVisibility = Visibility.Collapsed;
                item.IsDropTarget = item == target;
            }
        }

        private void UpdateSingleDropIndicator(BookmarkDisplayItem targetItem, bool isRightHalf)
        {
            foreach (var item in _displayItems)
            {
                item.IsDropTarget = false;
                if (item == targetItem)
                {
                    item.DropIndicatorLeftVisibility = !isRightHalf ? Visibility.Visible : Visibility.Collapsed;
                    item.DropIndicatorRightVisibility = isRightHalf ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    item.DropIndicatorLeftVisibility = Visibility.Collapsed;
                    item.DropIndicatorRightVisibility = Visibility.Collapsed;
                }
            }
        }

        private void ClearDropIndicators()
        {
            foreach (var item in _displayItems)
            {
                item.DropIndicatorLeftVisibility = Visibility.Collapsed;
                item.DropIndicatorRightVisibility = Visibility.Collapsed;
                item.IsDropTarget = false;
            }
        }

        // ═════════════════════════════ Overflow (») menu ═════════════════════════════

        private void BookmarksScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateOverflowVisibility();
        }

        private void BookmarksScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateOverflowVisibility();
        }

        private void BookmarksItemsControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateOverflowVisibility();
        }

        private void UpdateOverflowVisibility()
        {
            if (BookmarksScrollViewer == null || BtnOverflow == null) return;

            bool isOverflowing = BookmarksScrollViewer.ExtentWidth > (BookmarksScrollViewer.ViewportWidth + 2);
            BtnOverflow.Visibility = isOverflowing ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnOverflow_Click(object sender, RoutedEventArgs e)
        {
            if (CheckModalBlocking()) return;

            var root = CreateMenu(BtnOverflow);

            foreach (var b in _manager.Bookmarks)
            {
                if (b == null) continue;
                var itemSource = b;

                if (itemSource.IsFolder)
                {
                    // Bookmark folder: its bookmarks as a submenu
                    var gm = new MenuItem();
                    gm.Header = MakeHeader(GetGeometryForType("Group"), GetBrushForType("Group", itemSource.CustomColor), itemSource.Name, false);
                    if (itemSource.Children == null || itemSource.Children.Count == 0)
                    {
                        gm.Items.Add(MakeInfoItem("(Empty)"));
                    }
                    else
                    {
                        foreach (var c in itemSource.Children)
                        {
                            var child = c;
                            var cm = new MenuItem();
                            cm.Header = MakeHeader(GetGeometryForType(child.IconType), GetBrushForType(child.IconType, child.CustomColor), child.Name, false);
                            cm.ToolTip = child.Path;
                            AttachOpenHandlers(cm, child.Path, root);
                            gm.Items.Add(cm);
                        }
                    }
                    root.Items.Add(gm);
                    continue;
                }

                var menuItem = new MenuItem();
                menuItem.ToolTip = itemSource.Path;
                menuItem.Header = MakeHeader(GetGeometryForType(itemSource.IconType), GetBrushForType(itemSource.IconType, itemSource.CustomColor), itemSource.Name, false);
                AttachOpenHandlers(menuItem, itemSource.Path, root);
                root.Items.Add(menuItem);
            }

            if (root.Items.Count > 0)
            {
                root.IsOpen = true;
            }
        }
    }
}
