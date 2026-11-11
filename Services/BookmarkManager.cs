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

        /// <summary>
        /// Crash-safe write: write to a temp file first, then atomically swap it in,
        /// keeping the previous good version as "&lt;file&gt;.bak". A power loss or crash
        /// mid-write can no longer leave a truncated/corrupt config behind.
        /// </summary>
        private static void SafeWriteAllText(string path, string content)
        {
            string tmp = path + ".tmp";
            string bak = path + ".bak";
            File.WriteAllText(tmp, content);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, bak, true);
                    return;
                }
                catch
                {
                    // File.Replace can fail on some file systems; fall back to copy-backup + overwrite
                    try { File.Copy(path, bak, true); } catch { }
                    File.Copy(tmp, path, true);
                    try { File.Delete(tmp); } catch { }
                    return;
                }
            }
            File.Move(tmp, path);
        }

        /// <summary>
        /// Reads and deserializes a config file, falling back to its .bak copy if the main file is
        /// missing or corrupt. Returns null if neither is usable. Sets restoredFromBackup when the .bak was used.
        /// </summary>
        private T ReadWithBackup<T>(string path, Func<T, bool> isValid, out bool restoredFromBackup) where T : class
        {
            restoredFromBackup = false;
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                try
                {
                    if (!File.Exists(candidate)) continue;
                    string json = File.ReadAllText(candidate);
                    if (string.IsNullOrWhiteSpace(json)) continue;
                    T value = _serializer.Deserialize<T>(json);
                    if (value != null && isValid(value))
                    {
                        restoredFromBackup = candidate != path;
                        return value;
                    }
                }
                catch (Exception ex)
                {
                    // Serializer messages can embed the whole file content; keep the log short
                    string msg = ex.Message ?? string.Empty;
                    int cut = msg.IndexOfAny(new[] { '\r', '\n' });
                    if (cut >= 0) msg = msg.Substring(0, cut);
                    if (msg.Length > 120) msg = msg.Substring(0, 120) + "...";
                    App.Log("Failed to read " + Path.GetFileName(candidate) + ": " + msg);
                }
            }
            return null;
        }

        /// <summary>
        /// Copies the good .bak over the damaged main file. Done directly (not via SafeWriteAllText)
        /// so the damaged file is never rotated into .bak and the good backup is kept.
        /// </summary>
        private static void RestoreFromBackup(string path)
        {
            try { File.Copy(path + ".bak", path, true); }
            catch (Exception ex) { App.Log("Restore from backup failed: " + ex.Message); }
        }

        /// <summary>Keeps an unreadable config file aside instead of silently overwriting it.</summary>
        private static void PreserveCorruptFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                string aside = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(path, aside, true);
                App.Log("Unreadable config preserved as " + Path.GetFileName(aside));
            }
            catch { }
        }

        public void LoadSettings()
        {
            bool fromBackup;
            AppSettings s = ReadWithBackup<AppSettings>(_settingsFile, x => true, out fromBackup);
            if (s != null)
            {
                if (s.TopOffset < 110)
                {
                    s.TopOffset = 135;
                }
                Settings = s;
                if (fromBackup)
                {
                    PreserveCorruptFile(_settingsFile);
                    App.Log("settings.json was unreadable; restored from backup.");
                    RestoreFromBackup(_settingsFile);
                }
            }
            else
            {
                PreserveCorruptFile(_settingsFile);
            }
        }

        public void SaveSettings()
        {
            try
            {
                string json = _serializer.Serialize(Settings);
                SafeWriteAllText(_settingsFile, json);
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

            bool fromBackup;
            BookmarkItem[] items = ReadWithBackup<BookmarkItem[]>(_bookmarksFile, x => true, out fromBackup);

            if (items != null && items.Length == 0)
            {
                // File read fine but the user removed everything: same as first run (no backup restore)
                AddDefaultBookmarks();
                return;
            }

            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item != null) Bookmarks.Add(item);
                }

                if (fromBackup)
                {
                    // Main file was damaged: keep it for inspection, then rewrite it from the backup
                    PreserveCorruptFile(_bookmarksFile);
                    App.Log("bookmarks.json was unreadable; restored " + Bookmarks.Count + " bookmarks from backup.");
                    RestoreFromBackup(_bookmarksFile);
                }

                if (BookmarksChanged != null)
                {
                    BookmarksChanged();
                }
                return;
            }

            // Neither the file nor its backup is usable (or this is the first run).
            // Never destroy an existing file we couldn't read: keep a copy before writing defaults.
            PreserveCorruptFile(_bookmarksFile);
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
                SafeWriteAllText(_bookmarksFile, json);
                if (BookmarksChanged != null)
                {
                    BookmarksChanged();
                }
            }
            catch { }
        }

        public void AddBookmark(string name, string path, string iconType = "Folder", string color = "")
        {
            InsertBookmark(Bookmarks.Count, name, path, iconType, color);
        }

        public void InsertBookmark(int index, string name, string path, string iconType = "Folder", string color = "")
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

            if (index >= 0 && index <= Bookmarks.Count)
            {
                Bookmarks.Insert(index, item);
            }
            else
            {
                Bookmarks.Add(item);
            }
            SaveBookmarks();
        }

        public void RemoveBookmark(BookmarkItem item)
        {
            if (item == null) return;
            if (DetachItem(item))
            {
                SaveBookmarks();
            }
        }

        // ───────────────────────── Bookmark folders (groups) ─────────────────────────

        public IEnumerable<BookmarkItem> GetGroups()
        {
            foreach (var b in Bookmarks)
            {
                if (b != null && b.IsFolder) yield return b;
            }
        }

        /// <summary>Returns the group that directly contains the item, or null if it is top-level / not found.</summary>
        public BookmarkItem FindParentGroup(BookmarkItem item)
        {
            if (item == null) return null;
            foreach (var g in Bookmarks)
            {
                if (g != null && g.IsFolder && g.Children != null && g.Children.Contains(item)) return g;
            }
            return null;
        }

        /// <summary>Removes the item from wherever it lives (bar or a group) without saving.</summary>
        private bool DetachItem(BookmarkItem item)
        {
            if (Bookmarks.Remove(item)) return true;
            var parent = FindParentGroup(item);
            return parent != null && parent.Children.Remove(item);
        }

        public BookmarkItem AddGroup(string name, int index = -1)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "New Folder";
            var group = new BookmarkItem(name.Trim(), "", "Group", true);
            if (index >= 0 && index <= Bookmarks.Count) Bookmarks.Insert(index, group);
            else Bookmarks.Add(group);
            SaveBookmarks();
            return group;
        }

        public void MoveIntoGroup(BookmarkItem item, BookmarkItem group)
        {
            if (item == null || group == null || !group.IsFolder || item == group) return;
            if (item.IsFolder) return; // no nested groups
            if (group.Children == null) group.Children = new List<BookmarkItem>();
            if (group.Children.Contains(item)) return;
            DetachItem(item);
            group.Children.Add(item);
            SaveBookmarks();
        }

        public void MoveOutOfGroup(BookmarkItem item, int index = -1)
        {
            var parent = FindParentGroup(item);
            if (parent == null) return;
            parent.Children.Remove(item);
            if (index < 0 || index > Bookmarks.Count)
            {
                int gi = Bookmarks.IndexOf(parent);
                index = gi >= 0 ? gi + 1 : Bookmarks.Count;
            }
            Bookmarks.Insert(index, item);
            SaveBookmarks();
        }

        /// <summary>Deletes a group. Its bookmarks are kept and put back on the bar where the group was.</summary>
        public void DeleteGroup(BookmarkItem group, bool keepChildren)
        {
            int idx = Bookmarks.IndexOf(group);
            if (idx < 0) return;
            Bookmarks.RemoveAt(idx);
            if (keepChildren && group.Children != null)
            {
                foreach (var c in group.Children)
                {
                    Bookmarks.Insert(idx++, c);
                }
            }
            SaveBookmarks();
        }

        public void AddBookmarkToGroup(BookmarkItem group, string name, string path)
        {
            if (group == null || !group.IsFolder) return;
            if (string.IsNullOrEmpty(name))
            {
                name = Path.GetFileName(path.TrimEnd('\\', '/'));
                if (string.IsNullOrEmpty(name)) name = path;
            }
            if (group.Children == null) group.Children = new List<BookmarkItem>();
            group.Children.Add(new BookmarkItem(name, path, DetectIconType(path)));
            SaveBookmarks();
        }

        // ───────────────────────── Export / Import ─────────────────────────

        public void ExportTo(string file)
        {
            File.WriteAllText(file, _serializer.Serialize(Bookmarks));
        }

        /// <summary>Imports bookmarks from a file. Returns the number of bookmarks added. Throws on invalid file.</summary>
        public int ImportFrom(string file, bool replace)
        {
            string json = File.ReadAllText(file);
            BookmarkItem[] items = _serializer.Deserialize<BookmarkItem[]>(json);
            if (items == null) throw new InvalidDataException("The file does not contain any bookmarks.");

            if (replace) Bookmarks.Clear();

            // Skip exact duplicates (same path at top level) when merging
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var b in Bookmarks)
            {
                if (b != null && !b.IsFolder && !string.IsNullOrEmpty(b.Path)) existing.Add(b.Path);
            }

            int added = 0;
            foreach (var item in items)
            {
                if (item == null) continue;
                if (string.IsNullOrEmpty(item.Id)) item.Id = Guid.NewGuid().ToString();
                if (item.Children == null) item.Children = new List<BookmarkItem>();

                if (item.IsFolder)
                {
                    // Merge into an existing folder with the same name instead of duplicating it
                    BookmarkItem target = null;
                    foreach (var g in GetGroups())
                    {
                        if (string.Equals(g.Name, item.Name, StringComparison.OrdinalIgnoreCase)) { target = g; break; }
                    }
                    if (target == null)
                    {
                        Bookmarks.Add(item);
                        added += item.Children.Count;
                        continue;
                    }
                    if (target.Children == null) target.Children = new List<BookmarkItem>();
                    foreach (var child in item.Children)
                    {
                        if (child == null) continue;
                        bool dup = false;
                        foreach (var c in target.Children)
                        {
                            if (c != null && string.Equals(c.Path, child.Path, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                        }
                        if (dup) continue;
                        target.Children.Add(child);
                        added++;
                    }
                    continue;
                }

                if (!string.IsNullOrEmpty(item.Path) && existing.Contains(item.Path)) continue;
                Bookmarks.Add(item);
                existing.Add(item.Path ?? string.Empty);
                added++;
            }

            if (Bookmarks.Count == 0)
            {
                AddDefaultBookmarks();
            }
            else
            {
                SaveBookmarks();
            }
            return added;
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
