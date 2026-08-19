using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Web.Script.Serialization;
using ExplorerBookmarkBar.Models;

namespace ExplorerBookmarkBar.Services
{
    public class BookmarkManager
    {
        private static BookmarkManager _instance;
        public static BookmarkManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new BookmarkManager();
                }
                return _instance;
            }
        }

        private readonly string _configDir;
        private readonly string _bookmarksFile;
        private readonly string _settingsFile;
        private readonly JavaScriptSerializer _serializer;

        public ObservableCollection<BookmarkItem> Bookmarks { get; private set; }
        public AppSettings Settings { get; private set; }

        public event Action BookmarksChanged;
        public event Action SettingsChanged;

        public BookmarkManager()
        {
            _serializer = new JavaScriptSerializer();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _configDir = Path.Combine(appData, "ExplorerBookmarkBar");
            _bookmarksFile = Path.Combine(_configDir, "bookmarks.json");
            _settingsFile = Path.Combine(_configDir, "settings.json");

            Bookmarks = new ObservableCollection<BookmarkItem>();
            Settings = new AppSettings();

            if (!Directory.Exists(_configDir))
            {
                Directory.CreateDirectory(_configDir);
            }

            LoadSettings();
            LoadBookmarks();
        }

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFile))
                {
                    string json = File.ReadAllText(_settingsFile);
                    AppSettings s = _serializer.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        if (s.TopOffset < 110)
                        {
                            s.TopOffset = 135;
                        }
                        Settings = s;
                    }
                }
            }
            catch { }
        }

        public void SaveSettings()
        {
            try
            {
                string json = _serializer.Serialize(Settings);
                File.WriteAllText(_settingsFile, json);
                if (SettingsChanged != null)
                {
                    SettingsChanged();
                }
            }
            catch { }
        }

        public void LoadBookmarks()
        {
            Bookmarks.Clear();
            try
            {
                if (File.Exists(_bookmarksFile))
                {
                    string json = File.ReadAllText(_bookmarksFile);
                    BookmarkItem[] items = _serializer.Deserialize<BookmarkItem[]>(json);
                    if (items != null && items.Length > 0)
                    {
                        foreach (var item in items)
                        {
                            Bookmarks.Add(item);
                        }
                        if (BookmarksChanged != null)
                        {
                            BookmarksChanged();
                        }
                        return;
                    }
                }
            }
            catch { }

            // Initialize defaults if empty or first run
            AddDefaultBookmarks();
        }

        public void AddDefaultBookmarks()
        {
            Bookmarks.Clear();
            Bookmarks.Add(new BookmarkItem("This PC", "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "Drive"));
            SaveBookmarks();
        }

        public void SaveBookmarks()
        {
            try
            {
                string json = _serializer.Serialize(Bookmarks);
                File.WriteAllText(_bookmarksFile, json);
                if (BookmarksChanged != null)
                {
                    BookmarksChanged();
                }
            }
            catch { }
        }

        public void AddBookmark(string name, string path, string iconType = "Folder", string color = "")
        {
            if (string.IsNullOrEmpty(name))
            {
                name = Path.GetFileName(path.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(name)) name = path;
            }

            if (string.IsNullOrEmpty(iconType))
            {
                iconType = DetectIconType(path);
            }

            var item = new BookmarkItem(name, path, iconType);
            item.CustomColor = color;

            Bookmarks.Add(item);
            SaveBookmarks();
        }

        public void RemoveBookmark(BookmarkItem item)
        {
            if (item != null && Bookmarks.Contains(item))
            {
                Bookmarks.Remove(item);
                SaveBookmarks();
            }
        }

        public void MoveBookmark(int oldIndex, int newIndex)
        {
            if (oldIndex >= 0 && oldIndex < Bookmarks.Count && newIndex >= 0 && newIndex < Bookmarks.Count)
            {
                var item = Bookmarks[oldIndex];
                Bookmarks.RemoveAt(oldIndex);
                Bookmarks.Insert(newIndex, item);
                SaveBookmarks();
            }
        }

        public string DetectIconType(string path)
        {
            if (string.IsNullOrEmpty(path)) return "Folder";

            if (path.Contains("::{20D04FE0") || (path.Length <= 3 && path.Contains(":")))
                return "Drive";

            if (path.IndexOf("download", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Download";

            if (path.IndexOf("document", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Document";

            if (path.IndexOf("desktop", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Desktop";

            if (path.IndexOf("picture", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("photo", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Image";

            if (path.IndexOf("music", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Music";

            if (path.IndexOf("video", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("movie", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Video";

            if (path.IndexOf("code", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("project", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("src", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Code";

            return "Folder";
        }
    }
}
