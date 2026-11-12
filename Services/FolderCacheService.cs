using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ExplorerBookmarkBar.Services
{
    public class CachedFolderItem
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool HasChildren { get; set; }
        public bool IsDrive { get; set; }
    }

    public class FolderCacheEntry
    {
        public DateTime CachedTimeUtc { get; set; }
        public int TotalCount { get; set; }
        public List<CachedFolderItem> SubFolders { get; set; }

        public FolderCacheEntry()
        {
            SubFolders = new List<CachedFolderItem>();
        }
    }

    /// <summary>
    /// Everything-style in-memory folder cache & live synchronizer.
    /// - Stores directory trees in RAM for instant (0ms) menu rendering.
    /// - Persists cache to folder_cache.json so app restarts are instantly warm.
    /// - Watches filesystem changes (Created, Deleted, Renamed) using FileSystemWatcher
    ///   so changes are immediately reflected in cache without rescanning.
    /// - Prefetches child folders in background when menus open so hovering is instant.
    /// - Never blocks the UI thread.
    /// </summary>
    public class FolderCacheService
    {
        private static FolderCacheService _instance;
        private static readonly object _lock = new object();

        public static FolderCacheService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new FolderCacheService();
                        }
                    }
                }
                return _instance;
            }
        }

        private const string THIS_PC_CLSID = "20D04FE0-3AEA-1069-A2D8-08002B30309D";
        private const int MaxSubfolders = 300;

        private readonly string _cacheFilePath;
        private readonly JavaScriptSerializer _serializer;
        private readonly ConcurrentDictionary<string, FolderCacheEntry> _cache;
        private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers;
        private readonly ConcurrentDictionary<string, byte> _inFlightScans;
        private readonly ConcurrentDictionary<string, int> _eventVersions;
        private readonly HashSet<string> _networkRoots;
        private readonly System.Threading.Timer _saveDebounceTimer;
        private readonly object _saveLock = new object();
        private bool _isDirty;

        public event Action<string> CacheUpdated;

        private FolderCacheService()
        {
            _serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            _cache = new ConcurrentDictionary<string, FolderCacheEntry>(StringComparer.OrdinalIgnoreCase);
            _watchers = new ConcurrentDictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
            _inFlightScans = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            _eventVersions = new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _networkRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            RefreshNetworkRoots();

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configDir = Path.Combine(appData, "ExplorerBookmarkBar");
            if (!Directory.Exists(configDir))
            {
                try { Directory.CreateDirectory(configDir); } catch { }
            }
            _cacheFilePath = Path.Combine(configDir, "folder_cache.json");

            // Debounce saver: saves cache to disk 3 seconds after the last change
            _saveDebounceTimer = new System.Threading.Timer(_ => SaveCacheToDisk(), null, Timeout.Infinite, Timeout.Infinite);

            LoadCacheFromDisk();
        }

        private void RefreshNetworkRoots()
        {
            try
            {
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType == DriveType.Network && d.Name.Length >= 2)
                    {
                        found.Add(d.Name.Substring(0, 2));
                    }
                }
                lock (_networkRoots)
                {
                    _networkRoots.Clear();
                    foreach (var r in found) _networkRoots.Add(r);
                }
            }
            catch { }
        }

        public bool IsSlowOrNetworkPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith("\\\\", StringComparison.Ordinal)) return true;
            if (path.Length >= 2 && path[1] == ':')
            {
                string root = path.Substring(0, 2);
                lock (_networkRoots)
                {
                    return _networkRoots.Contains(root);
                }
            }
            return false;
        }

        #region Disk Persistence

        private void LoadCacheFromDisk()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    string json = File.ReadAllText(_cacheFilePath);
                    var dict = _serializer.Deserialize<Dictionary<string, FolderCacheEntry>>(json);
                    if (dict != null)
                    {
                        foreach (var kvp in dict)
                        {
                            _cache[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log("Failed to load folder cache from disk: " + ex.Message);
            }
        }

        private void ScheduleSave()
        {
            _isDirty = true;
            try
            {
                _saveDebounceTimer.Change(3000, Timeout.Infinite);
            }
            catch { }
        }

        private void SaveCacheToDisk()
        {
            lock (_saveLock)
            {
                if (!_isDirty) return;
                _isDirty = false;

                try
                {
                    var snapshot = new Dictionary<string, FolderCacheEntry>(_cache, StringComparer.OrdinalIgnoreCase);
                    string json = _serializer.Serialize(snapshot);
                    string tmp = _cacheFilePath + ".tmp";
                    File.WriteAllText(tmp, json);
                    if (File.Exists(_cacheFilePath))
                    {
                        File.Replace(tmp, _cacheFilePath, null);
                    }
                    else
                    {
                        File.Move(tmp, _cacheFilePath);
                    }
                }
                catch (Exception ex)
                {
                    App.Log("Failed to persist folder cache to disk: " + ex.Message);
                }
            }
        }

        public void FlushToDisk()
        {
            try
            {
                _saveDebounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
                SaveCacheToDisk();
            }
            catch { }
        }

        #endregion

        #region Cache Retrieval & Query

        public static bool IsThisPc(string path)
        {
            return !string.IsNullOrEmpty(path) && path.IndexOf(THIS_PC_CLSID, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsFileSystemPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith("::", StringComparison.Ordinal) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return false;
            return (path.Length >= 2 && path[1] == ':') || path.StartsWith("\\\\", StringComparison.Ordinal);
        }

        /// <summary>
        /// Instantly returns cached subfolders if available in RAM.
        /// Returns null if not yet cached.
        /// </summary>
        public FolderCacheEntry TryGetFromCache(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            FolderCacheEntry entry;
            if (_cache.TryGetValue(path, out entry))
            {
                return entry;
            }
            return null;
        }

        /// <summary>
        /// Triggers a background refresh only if the cache is missing or stale.
        /// Never touches filesystem or blocks the caller's thread.
        /// </summary>
        public void RefreshInBackgroundIfStale(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            FolderCacheEntry cached;
            if (_cache.TryGetValue(path, out cached))
            {
                double maxAgeMinutes = IsThisPc(path) ? 2.0 : 5.0;
                bool stale = (DateTime.UtcNow - cached.CachedTimeUtc).TotalMinutes > maxAgeMinutes;
                bool needsWatcher = IsFileSystemPath(path) && !IsSlowOrNetworkPath(path) && !_watchers.ContainsKey(path);
                if (stale || needsWatcher)
                {
                    Task.Run(() =>
                    {
                        if (needsWatcher) EnsureWatcher(path);
                        if (stale) ScanAndCache(path);
                    });
                }
            }
            else
            {
                Task.Run(() => ScanAndCache(path));
            }
        }

        /// <summary>
        /// Gets subfolders: returns immediately from cache if available,
        /// and optionally starts background sync if stale or missing.
        /// </summary>
        public async Task<FolderCacheEntry> GetSubfoldersAsync(string path, bool forceRefresh = false)
        {
            if (string.IsNullOrEmpty(path))
            {
                return new FolderCacheEntry();
            }

            FolderCacheEntry cached;
            if (!forceRefresh && _cache.TryGetValue(path, out cached))
            {
                EnsureWatcher(path);

                double maxAgeMinutes = IsThisPc(path) ? 2.0 : 5.0;
                if ((DateTime.UtcNow - cached.CachedTimeUtc).TotalMinutes > maxAgeMinutes)
                {
                    var _ = Task.Run(() => ScanAndCache(path));
                }

                return cached;
            }

            // Not in cache, scan asynchronously
            return await Task.Run(() => ScanAndCache(path));
        }

        /// <summary>
        /// Silently prefetches the immediate children of an opened menu in the background
        /// so hovering over any item opens its submenu in 0ms from cache.
        /// Skips slow/network paths (both UNC and mapped network drives like Z:\).
        /// </summary>
        public void PrefetchChildrenAsync(IEnumerable<CachedFolderItem> items)
        {
            if (items == null) return;
            var paths = items
                .Where(f => f != null && !string.IsNullOrEmpty(f.FullPath) && !IsSlowOrNetworkPath(f.FullPath))
                .Select(f => f.FullPath)
                .Take(25) // Cap prefetch per menu open so huge folders don't flood I/O
                .ToList();

            if (paths.Count == 0) return;

            Task.Run(() =>
            {
                Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = 4 }, p =>
                {
                    try
                    {
                        FolderCacheEntry existing;
                        if (_cache.TryGetValue(p, out existing) && (DateTime.UtcNow - existing.CachedTimeUtc).TotalMinutes <= 5.0)
                        {
                            return;
                        }
                        ScanAndCache(p, attachWatcher: false);
                    }
                    catch { }
                });
            });
        }

        #endregion

        #region Scanning & FileSystemWatcher

        private FolderCacheEntry ScanAndCache(string path, bool attachWatcher = true)
        {
            // Deduplicate concurrent scans of the exact same path
            if (!_inFlightScans.TryAdd(path, 0))
            {
                FolderCacheEntry existing;
                if (_cache.TryGetValue(path, out existing)) return existing;
            }

            try
            {
                var entry = new FolderCacheEntry { CachedTimeUtc = DateTime.UtcNow };
                bool scanSucceeded = false;

                try
                {
                    if (IsThisPc(path))
                    {
                        var drives = DriveInfo.GetDrives();
                        var results = new CachedFolderItem[drives.Length];

                        // Probe all drives in parallel so a single slow/waking network or optical drive
                        // does not stall the rest of the drives.
                        Parallel.For(0, drives.Length, i =>
                        {
                            var drive = drives[i];
                            string letter = drive.Name.TrimEnd('\\');
                            string label = "";
                            try
                            {
                                if (drive.IsReady) label = drive.VolumeLabel;
                            }
                            catch { }

                            if (string.IsNullOrEmpty(label))
                            {
                                label = drive.DriveType == DriveType.Removable ? "USB Drive"
                                      : drive.DriveType == DriveType.Network ? "Network Drive"
                                      : drive.DriveType == DriveType.CDRom ? "DVD Drive" : "Local Disk";
                            }
                            results[i] = new CachedFolderItem
                            {
                                Name = label + " (" + letter + ")",
                                FullPath = drive.Name,
                                HasChildren = true,
                                IsDrive = true
                            };
                        });

                        foreach (var item in results)
                        {
                            if (item != null) entry.SubFolders.Add(item);
                        }
                        entry.TotalCount = entry.SubFolders.Count;
                        scanSucceeded = true;
                    }
                    else if (IsFileSystemPath(path) && Directory.Exists(path))
                    {
                        var di = new DirectoryInfo(path);
                        var dirs = new List<DirectoryInfo>();
                        int total = 0;

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
                        if (dirs.Count > MaxSubfolders)
                        {
                            dirs.RemoveRange(MaxSubfolders, dirs.Count - MaxSubfolders);
                        }

                        foreach (var d in dirs)
                        {
                            entry.SubFolders.Add(new CachedFolderItem
                            {
                                Name = d.Name,
                                FullPath = d.FullName,
                                HasChildren = true
                            });
                        }
                        entry.TotalCount = total;
                        scanSucceeded = true;

                        // Set up live watcher for this directory if requested
                        if (attachWatcher) EnsureWatcher(path);
                    }
                }
                catch (Exception ex)
                {
                    App.Log("ScanAndCache failed for " + path + ": " + ex.Message);
                }

                // Preserve existing persistent cache if a scan failed (e.g. sleeping network share)
                FolderCacheEntry prevEntry;
                if (!scanSucceeded && _cache.TryGetValue(path, out prevEntry) && prevEntry != null && prevEntry.SubFolders.Count > 0)
                {
                    return prevEntry;
                }

                _cache[path] = entry;
                ScheduleSave();

                try
                {
                    var handler = CacheUpdated;
                    if (handler != null) handler(path);
                }
                catch { }

                return entry;
            }
            finally
            {
                byte ignored;
                _inFlightScans.TryRemove(path, out ignored);
            }
        }

        /// <summary>
        /// Everything-style live monitoring:
        /// Attaches a FileSystemWatcher so any created/deleted/renamed subfolders
        /// update the in-memory cache directly without requiring a full re-scan.
        /// </summary>
        public void EnsureWatcher(string path)
        {
            if (!IsFileSystemPath(path) || IsSlowOrNetworkPath(path)) return;
            if (_watchers.Count >= 64 && !_watchers.ContainsKey(path)) return;

            try
            {
                if (!Directory.Exists(path)) return;

                _watchers.GetOrAdd(path, dirPath =>
                {
                    try
                    {
                        var watcher = new FileSystemWatcher(dirPath)
                        {
                            NotifyFilter = NotifyFilters.DirectoryName,
                            IncludeSubdirectories = false,
                            EnableRaisingEvents = true
                        };

                        watcher.Created += (s, e) => OnFileSystemEvent(dirPath);
                        watcher.Deleted += (s, e) => OnFileSystemEvent(dirPath);
                        watcher.Renamed += (s, e) => OnFileSystemEvent(dirPath);
                        return watcher;
                    }
                    catch
                    {
                        return null;
                    }
                });
            }
            catch { }
        }

        private void OnFileSystemEvent(string parentPath)
        {
            int ver = _eventVersions.AddOrUpdate(parentPath, 1, (_, old) => old + 1);
            // Debounce rescan so rapid folder bursts only trigger one scan
            Task.Delay(300).ContinueWith(_ =>
            {
                int current;
                if (_eventVersions.TryGetValue(parentPath, out current) && current == ver)
                {
                    ScanAndCache(parentPath);
                }
            });
        }

        /// <summary>
        /// Warms up cache for all bookmarked paths (and drives) in the background on startup.
        /// Local paths and drives run first so UI is immediately ready; slow network shares
        /// run separately so they never block local caching.
        /// </summary>
        public void WarmUpAsync(IEnumerable<string> paths)
        {
            var pathList = paths != null ? paths.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() : new List<string>();

            Task.Run(() =>
            {
                var sw = Stopwatch.StartNew();

                // 1. Identify which drives are network/slow vs local fixed
                var fixedDrives = new List<string>();
                var networkRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        if (d.DriveType == DriveType.Network)
                        {
                            networkRoots.Add(d.Name.Substring(0, 2)); // e.g. "Z:"
                        }
                        else if (d.DriveType == DriveType.Fixed && d.IsReady)
                        {
                            fixedDrives.Add(d.Name);
                        }
                    }
                }
                catch { }

                Func<string, bool> isSlowPath = p =>
                {
                    if (p.StartsWith("\\\\", StringComparison.Ordinal)) return true;
                    if (p.Length >= 2 && p[1] == ':' && networkRoots.Contains(p.Substring(0, 2))) return true;
                    return false;
                };

                var fastPaths = pathList.Where(p => !isSlowPath(p)).ToList();
                var slowPaths = pathList.Where(p => isSlowPath(p)).ToList();

                // 2. Scan This PC + fast local bookmarked paths + fixed drive roots in parallel
                foreach (var fd in fixedDrives)
                {
                    if (!fastPaths.Contains(fd, StringComparer.OrdinalIgnoreCase))
                        fastPaths.Add(fd);
                }

                Parallel.ForEach(fastPaths, new ParallelOptions { MaxDegreeOfParallelism = 4 }, p =>
                {
                    try { ScanAndCache(p); } catch { }
                });

                App.Log("FolderCache warm-up (local) finished: " + fastPaths.Count + " paths in " + sw.ElapsedMilliseconds + "ms (total cached: " + _cache.Count + ")");

                // 3. Scan network paths on a lower-priority parallel loop so they don't stall local warmup
                if (slowPaths.Count > 0)
                {
                    Parallel.ForEach(slowPaths, new ParallelOptions { MaxDegreeOfParallelism = 2 }, p =>
                    {
                        try { ScanAndCache(p); } catch { }
                    });
                    App.Log("FolderCache warm-up (network) finished: " + slowPaths.Count + " paths in " + sw.ElapsedMilliseconds + "ms");
                }
            });
        }

        #endregion
    }
}
