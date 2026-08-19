using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        public BookmarkBarControl(IntPtr explorerHwnd)
        {
            InitializeComponent();
            _explorerHwnd = explorerHwnd;
            _manager = BookmarkManager.Instance;
            _isFileDialog = (Win32Helper.GetWindowClassName(explorerHwnd) == "#32770");

            BookmarksItemsControl.ItemsSource = _displayItems;

            _manager.BookmarksChanged += RefreshDisplayItems;
            _manager.SettingsChanged += ApplySettings;
            ThemeManager.ThemeChanged += () => Dispatcher.Invoke((Action)RefreshDisplayItems);

            if (_isFileDialog)
            {
                BtnAddCurrent.Visibility = Visibility.Collapsed;
                BtnSettings.Visibility = Visibility.Collapsed;
                BtnClose.Visibility = Visibility.Collapsed;
            }

            ExplorerChildBarManager.ModalDialogStateChanged += isDialogOpen =>
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
            };

            RefreshDisplayItems();
            ApplySettings();
            ApplyTheme();
            Loaded += (s, e) => UpdateOverflowVisibility();
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
            var s = _manager.Settings;

            MainBorder.Margin = new Thickness(0);
            MainBorder.CornerRadius = new CornerRadius(0);
            MainBorder.BorderThickness = new Thickness(0, 0, 0, 1);

            RefreshDisplayItems();
        }

        private void RefreshDisplayItems()
        {
            var s = _manager.Settings;
            var margin = new Thickness(s.ItemSpacing, 1, s.ItemSpacing, 1);
            var padding = new Thickness(s.ItemPadding, 3, s.ItemPadding, 3);

            _displayItems.Clear();
            foreach (var b in _manager.Bookmarks)
            {
                BookmarkDisplayItem item = new BookmarkDisplayItem();
                item.Source = b;
                item.IconGeometry = GetGeometryForType(b.IconType);
                item.IconBrush = GetBrushForType(b.IconType, b.CustomColor);
                item.ItemMargin = margin;
                item.ItemPadding = padding;
                _displayItems.Add(item);
            }

            Dispatcher.BeginInvoke(new Action(UpdateOverflowVisibility), System.Windows.Threading.DispatcherPriority.Background);
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

        private void BookmarkButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                if (CheckModalBlocking())
                {
                    e.Handled = true;
                    return;
                }

                var btn = sender as Button;
                var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
                if (item != null && !string.IsNullOrEmpty(item.Path))
                {
                    e.Handled = true;
                    if (_isFileDialog)
                    {
                        ExplorerComHelper.NavigateActiveExplorer(_explorerHwnd, item.Path);
                    }
                    else
                    {
                        ExplorerComHelper.OpenInNewTab(_explorerHwnd, item.Path);
                    }
                }
            }
        }

        private void BookmarkButton_Click(object sender, RoutedEventArgs e)
        {
            if (CheckModalBlocking()) return;

            var btn = sender as Button;
            var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
            if (item == null) return;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                ExplorerComHelper.OpenInNewWindow(item.Path);
            }
            else
            {
                ExplorerComHelper.NavigateActiveExplorer(_explorerHwnd, item.Path);
            }
        }

        private void BookmarkButton_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (_isFileDialog || CheckModalBlocking())
            {
                e.Handled = true;
            }
        }

        private void BookmarkButton_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (_isFileDialog || CheckModalBlocking())
            {
                e.Handled = true;
                return;
            }

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
            if (CheckModalBlocking()) return;

            var menuItem = sender as MenuItem;
            var contextMenu = menuItem != null ? menuItem.Parent as ContextMenu : null;
            var btn = contextMenu != null ? contextMenu.PlacementTarget as Button : null;
            var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
            if (item != null)
            {
                ExplorerComHelper.NavigateActiveExplorer(_explorerHwnd, item.Path);
            }
        }

        private void MenuOpenNewTab_Click(object sender, RoutedEventArgs e)
        {
            if (CheckModalBlocking()) return;

            var menuItem = sender as MenuItem;
            var contextMenu = menuItem != null ? menuItem.Parent as ContextMenu : null;
            var btn = contextMenu != null ? contextMenu.PlacementTarget as Button : null;
            var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
            if (item != null)
            {
                ExplorerComHelper.OpenInNewTab(_explorerHwnd, item.Path);
            }
        }

        private void MenuOpenNewWindow_Click(object sender, RoutedEventArgs e)
        {
            if (CheckModalBlocking()) return;

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
            if (CheckModalBlocking()) return;

            var menuItem = sender as MenuItem;
            var contextMenu = menuItem != null ? menuItem.Parent as ContextMenu : null;
            var btn = contextMenu != null ? contextMenu.PlacementTarget as Button : null;
            var item = btn != null ? btn.Tag as BookmarkDisplayItem : null;
            if (item != null)
            {
                var dlg = new BookmarkEditDialog(item.Source, isNew: false, callerHwnd: _explorerHwnd);
                dlg.ShowDialog();
            }
        }

        private void MenuCopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (CheckModalBlocking()) return;

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
            if (CheckModalBlocking()) return;

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
            if (CheckModalBlocking()) return;

            string currentPath = ExplorerComHelper.GetActiveExplorerPath(_explorerHwnd);
            if (string.IsNullOrEmpty(currentPath))
            {
                currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            string name = "";
            string iconType = "Folder";

            if (currentPath.Contains("20D04FE0-3AEA-1069-A2D8-08002B30309D"))
            {
                name = "This PC";
                currentPath = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
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

        private void UserControl_DragOver(object sender, DragEventArgs e)
        {
            if (CheckModalBlocking())
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

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

        private void UserControl_Drop(object sender, DragEventArgs e)
        {
            if (CheckModalBlocking())
            {
                e.Handled = true;
                return;
            }

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

            var contextMenu = new ContextMenu();
            contextMenu.PlacementTarget = BtnOverflow;
            contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;

            foreach (var b in _manager.Bookmarks)
            {
                var itemSource = b;
                var menuItem = new MenuItem();
                menuItem.ToolTip = itemSource.Path;

                var sp = new StackPanel();
                sp.Orientation = Orientation.Horizontal;

                var path = new System.Windows.Shapes.Path();
                path.Width = 14;
                path.Height = 14;
                path.Margin = new Thickness(0, 0, 8, 0);
                path.VerticalAlignment = VerticalAlignment.Center;
                path.Stretch = Stretch.Uniform;
                path.Data = GetGeometryForType(itemSource.IconType);
                path.Fill = GetBrushForType(itemSource.IconType, itemSource.CustomColor);
                sp.Children.Add(path);

                var txt = new TextBlock();
                txt.Text = itemSource.Name;
                txt.VerticalAlignment = VerticalAlignment.Center;
                txt.FontSize = 12;
                txt.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
                sp.Children.Add(txt);

                menuItem.Header = sp;

                menuItem.Click += (s, ev) =>
                {
                    if (CheckModalBlocking()) return;
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    {
                        ExplorerComHelper.OpenInNewWindow(itemSource.Path);
                    }
                    else
                    {
                        ExplorerComHelper.NavigateActiveExplorer(_explorerHwnd, itemSource.Path);
                    }
                };

                menuItem.PreviewMouseDown += (s, ev) =>
                {
                    if (ev.ChangedButton == MouseButton.Middle)
                    {
                        if (CheckModalBlocking()) { ev.Handled = true; return; }
                        ev.Handled = true;
                        contextMenu.IsOpen = false;
                        if (_isFileDialog)
                        {
                            ExplorerComHelper.NavigateActiveExplorer(_explorerHwnd, itemSource.Path);
                        }
                        else
                        {
                            ExplorerComHelper.OpenInNewTab(_explorerHwnd, itemSource.Path);
                        }
                    }
                };

                contextMenu.Items.Add(menuItem);
            }

            if (contextMenu.Items.Count > 0)
            {
                contextMenu.IsOpen = true;
            }
        }
    }
}
