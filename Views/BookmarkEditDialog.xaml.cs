using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ExplorerBookmarkBar.Models;
using ExplorerBookmarkBar.Services;

namespace ExplorerBookmarkBar.Views
{
    public partial class BookmarkEditDialog : Window
    {
        private readonly BookmarkItem _targetItem;
        private readonly bool _isNew;
        private string _selectedIconType = "Folder";

        private readonly string[] _iconTypes = new string[]
        {
            "Folder", "Drive", "Download", "Document", "Desktop", "Star", "Code", "Image", "Music", "Video"
        };

        public BookmarkEditDialog(BookmarkItem item, bool isNew = false, IntPtr callerHwnd = default(IntPtr))
        {
            InitializeComponent();
            _targetItem = item != null ? item : new BookmarkItem();
            _isNew = isNew;

            Icon = Win32Helper.GetExplorerIconImageSource();
            CenterOnScreen(callerHwnd);
            ExplorerChildBarManager.SetActiveModalDialog(this);

            TxtDialogTitle.Text = _isNew ? "Add New Bookmark" : "Edit Bookmark";
            TxtName.Text = _targetItem.Name;
            TxtPath.Text = _targetItem.Path;
            _selectedIconType = string.IsNullOrEmpty(_targetItem.IconType) ? "Folder" : _targetItem.IconType;

            ThemeManager.ThemeChanged += () => Dispatcher.Invoke((Action)BuildIconPicker);
            BuildIconPicker();
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

        private void CenterOnScreen(IntPtr hwnd)
        {
            try
            {
                var screen = (hwnd != IntPtr.Zero && Win32Helper.IsWindow(hwnd))
                    ? System.Windows.Forms.Screen.FromHandle(hwnd)
                    : System.Windows.Forms.Screen.PrimaryScreen;

                if (screen != null)
                {
                    var area = screen.WorkingArea;
                    double dpiX = 1.0, dpiY = 1.0;
                    var source = PresentationSource.FromVisual(this);
                    if (source != null && source.CompositionTarget != null)
                    {
                        dpiX = source.CompositionTarget.TransformToDevice.M11;
                        dpiY = source.CompositionTarget.TransformToDevice.M22;
                    }
                    if (dpiX <= 0) dpiX = 1.0;
                    if (dpiY <= 0) dpiY = 1.0;

                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = (area.Left / dpiX) + Math.Max(0, (area.Width / dpiX - Width) / 2);
                    Top = (area.Top / dpiY) + Math.Max(0, (area.Height / dpiY - Height) / 2);
                }
            }
            catch { }
        }

        private void BuildIconPicker()
        {
            IconSelectionPanel.Children.Clear();
            bool isDark = ThemeManager.IsDarkTheme;

            var selectedBg = Application.Current != null && Application.Current.Resources.Contains("AccentColor")
                ? (Brush)Application.Current.Resources["AccentColor"]
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString(isDark ? "#0078D4" : "#0067C0"));

            var unselectedBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isDark ? "#2B2B2B" : "#F3F3F3"));
            var hoverBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isDark ? "#383838" : "#E5E5E5"));
            var unselectedBorder = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isDark ? "#444444" : "#D0D0D0"));
            var selectedBorder = selectedBg;
            var unselectedIconBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isDark ? "#CCCCCC" : "#4A4A4A"));
            var selectedIconBrush = Brushes.White;

            foreach (var icon in _iconTypes)
            {
                bool isSelected = (icon == _selectedIconType);

                var border = new Border();
                border.Width = 36;
                border.Height = 32;
                border.CornerRadius = new CornerRadius(4);
                border.Margin = new Thickness(0, 0, 6, 6);
                border.Background = isSelected ? selectedBg : unselectedBg;
                border.BorderBrush = isSelected ? selectedBorder : unselectedBorder;
                border.BorderThickness = new Thickness(1);
                border.Cursor = Cursors.Hand;
                border.Tag = icon;
                border.SnapsToDevicePixels = true;

                var path = new System.Windows.Shapes.Path();
                path.Width = 16;
                path.Height = 16;
                path.Stretch = Stretch.Uniform;
                path.Fill = isSelected ? selectedIconBrush : unselectedIconBrush;
                path.Data = GetGeometry(icon);

                border.Child = path;

                border.MouseEnter += (s, e) =>
                {
                    var b = (Border)s;
                    if ((string)b.Tag != _selectedIconType)
                    {
                        b.Background = hoverBg;
                    }
                };
                border.MouseLeave += (s, e) =>
                {
                    var b = (Border)s;
                    if ((string)b.Tag != _selectedIconType)
                    {
                        b.Background = unselectedBg;
                    }
                };

                border.MouseDown += (s, e) =>
                {
                    _selectedIconType = (string)((Border)s).Tag;
                    BuildIconPicker();
                };

                IconSelectionPanel.Children.Add(border);
            }
        }

        private Geometry GetGeometry(string type)
        {
            switch (type)
            {
                case "Drive":
                    return Geometry.Parse("M6,2H18A2,2 0 0,1 20,4V20A2,2 0 0,1 18,22H6A2,2 0 0,1 4,20V4A2,2 0 0,1 6,2M12,4A1,1 0 0,0 11,5A1,1 0 0,0 12,6A1,1 0 0,0 13,5A1,1 0 0,0 12,4M12,18A1.5,1.5 0 0,0 10.5,19.5A1.5,1.5 0 0,0 12,21A1.5,1.5 0 0,0 13.5,19.5A1.5,1.5 0 0,0 12,18Z");
                case "Download":
                    return Geometry.Parse("M5,20H19V18H5M19,9H15V3H9V9H5L12,16L19,9Z");
                case "Document":
                    return Geometry.Parse("M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z");
                case "Desktop":
                    return Geometry.Parse("M21,16H3V4H21M21,2H3C1.89,2 1,2.89 1,4V16A2,2 0 0,0 3,18H10V20H8V22H16V20H14V18H21A2,2 0 0,0 23,16V4C23,2.89 22.1,2 21,2Z");
                case "Star":
                    return Geometry.Parse("M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z");
                case "Code":
                    return Geometry.Parse("M14.6,16.6L19.2,12L14.6,7.4L16,6L22,12L16,18L14.6,16.6M9.4,16.6L4.8,12L9.4,7.4L8,6L2,12L8,18L9.4,16.6Z");
                case "Image":
                    return Geometry.Parse("M8.5,13.5L11,16.5L14.5,12L19,18H5M21,19V5C21,3.89 20.1,3 19,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19Z");
                case "Music":
                    return Geometry.Parse("M12,3V13.55A4,4 0 1,0 14,17V7H18V3H12Z");
                case "Video":
                    return Geometry.Parse("M17,10.5V7A1,1 0 0,0 16,6H4A1,1 0 0,0 3,7V17A1,1 0 0,0 4,18H16A1,1 0 0,0 17,17V13.5L21,17.5V6.5L17,10.5Z");
                default:
                    return Geometry.Parse("M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z");
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select a folder to bookmark";
                if (!string.IsNullOrEmpty(TxtPath.Text) && System.IO.Directory.Exists(TxtPath.Text))
                {
                    dialog.SelectedPath = TxtPath.Text;
                }

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    TxtPath.Text = dialog.SelectedPath;
                    if (string.IsNullOrWhiteSpace(TxtName.Text))
                    {
                        TxtName.Text = System.IO.Path.GetFileName(dialog.SelectedPath);
                    }
                }
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtName.Text.Trim();
            string path = TxtPath.Text.Trim();

            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show("Please enter a path or browse to select a folder.", "Missing Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(name))
            {
                name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(name)) name = path;
            }

            _targetItem.Name = name;
            _targetItem.Path = path;
            _targetItem.IconType = _selectedIconType;

            if (_isNew)
            {
                BookmarkManager.Instance.Bookmarks.Add(_targetItem);
            }

            BookmarkManager.Instance.SaveBookmarks();
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
