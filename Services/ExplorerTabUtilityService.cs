using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace ExplorerBookmarkBar.Services
{
    /// <summary>
    /// Redirects newly opened File Explorer windows into new tabs inside an already open Explorer window
    /// (modeled after w4po/ExplorerTabUtility).
    /// </summary>
    public class ExplorerTabUtilityService
    {
        private static ExplorerTabUtilityService _instance;
        public static ExplorerTabUtilityService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ExplorerTabUtilityService();
                }
                return _instance;
            }
        }

        private class RedirectState
        {
            public bool AddedLayeredStyle;
            public DispatcherTimer PollTimer;
        }

        private readonly HashSet<IntPtr> _knownWindows = new HashSet<IntPtr>();
        private readonly HashSet<IntPtr> _closingRedirectedWindows = new HashSet<IntPtr>();
        private readonly Dictionary<IntPtr, int> _windowTabCounts = new Dictionary<IntPtr, int>();
        private readonly Dictionary<IntPtr, RedirectState> _redirectingWindows = new Dictionary<IntPtr, RedirectState>();

        private IntPtr _winEventHook;
        private Win32Helper.WinEventDelegate _winEventDelegate;
        private IntPtr _lastActiveExplorerHwnd;
        private int _lastTabDetachTick;
        private bool _isRunning;

        private ExplorerTabUtilityService()
        {
        }

        public bool IsWindowRedirecting(IntPtr hwnd)
        {
            return hwnd != IntPtr.Zero && _redirectingWindows.ContainsKey(hwnd);
        }

        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;

            SeedKnownWindows();

            _winEventDelegate = OnWinEvent;
            _winEventHook = Win32Helper.SetWinEventHook(
                Win32Helper.EVENT_OBJECT_CREATE,
                Win32Helper.EVENT_OBJECT_SHOW,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                Win32Helper.WINEVENT_OUTOFCONTEXT | Win32Helper.WINEVENT_SKIPOWNPROCESS);

            App.Log("ExplorerTabUtilityService started.");
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;

            if (_winEventHook != IntPtr.Zero)
            {
                Win32Helper.UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }
            _winEventDelegate = null;

            foreach (var kvp in new List<KeyValuePair<IntPtr, RedirectState>>(_redirectingWindows))
            {
                if (kvp.Value.PollTimer != null)
                {
                    kvp.Value.PollTimer.Stop();
                }
                UncloakWindow(kvp.Key, kvp.Value);
            }
            _redirectingWindows.Clear();
            _knownWindows.Clear();
            _closingRedirectedWindows.Clear();
            _windowTabCounts.Clear();
        }

        private void SeedKnownWindows()
        {
            _knownWindows.Clear();
            _windowTabCounts.Clear();

            var cabinets = Win32Helper.GetAllCabinetWindows();
            foreach (IntPtr hwnd in cabinets)
            {
                _knownWindows.Add(hwnd);
                _windowTabCounts[hwnd] = Win32Helper.GetExplorerTabs(hwnd).Count;
                if (_lastActiveExplorerHwnd == IntPtr.Zero)
                {
                    _lastActiveExplorerHwnd = hwnd;
                }
            }
        }

        public void TrackExistingWindows()
        {
            if (!_isRunning) return;

            IntPtr fg = Win32Helper.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                IntPtr fgRoot;
                if (Win32Helper.IsExplorerWindow(fg, out fgRoot)
                    && fgRoot != IntPtr.Zero
                    && Win32Helper.GetWindowClassName(fgRoot) == "CabinetWClass"
                    && _knownWindows.Contains(fgRoot)
                    && !_redirectingWindows.ContainsKey(fgRoot)
                    && !_closingRedirectedWindows.Contains(fgRoot))
                {
                    _lastActiveExplorerHwnd = fgRoot;
                }
            }

            var current = Win32Helper.GetAllCabinetWindows();
            var currentSet = new HashSet<IntPtr>(current);

            // Detect tab count decreases (tab close or detach) on known windows
            foreach (IntPtr hwnd in current)
            {
                if (_redirectingWindows.ContainsKey(hwnd) || _closingRedirectedWindows.Contains(hwnd)) continue;

                int tabCount = Win32Helper.GetExplorerTabs(hwnd).Count;
                int prevCount;
                if (_windowTabCounts.TryGetValue(hwnd, out prevCount))
                {
                    if (tabCount < prevCount)
                    {
                        _lastTabDetachTick = Environment.TickCount;
                    }
                    _windowTabCounts[hwnd] = tabCount;
                }
                else if (_knownWindows.Contains(hwnd))
                {
                    _windowTabCounts[hwnd] = tabCount;
                }
                else
                {
                    HandleNewCabinetWindow(hwnd);
                }
            }

            // Clean up destroyed handles
            var deadKnown = new List<IntPtr>();
            foreach (IntPtr h in _knownWindows)
            {
                if (!currentSet.Contains(h) && !Win32Helper.IsWindow(h))
                {
                    deadKnown.Add(h);
                }
            }
            foreach (IntPtr h in deadKnown)
            {
                _knownWindows.Remove(h);
                _closingRedirectedWindows.Remove(h);
                _windowTabCounts.Remove(h);
                if (_lastActiveExplorerHwnd == h)
                {
                    _lastActiveExplorerHwnd = IntPtr.Zero;
                }
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (!_isRunning) return;
            if (eventType != Win32Helper.EVENT_OBJECT_CREATE && eventType != Win32Helper.EVENT_OBJECT_SHOW) return;
            if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;
            if (Win32Helper.GetWindowClassName(hwnd) != "CabinetWClass") return;

            TrackExistingWindows();
            HandleNewCabinetWindow(hwnd);
        }

        private void HandleNewCabinetWindow(IntPtr newHwnd)
        {
            if (!_isRunning || newHwnd == IntPtr.Zero || !Win32Helper.IsWindow(newHwnd)) return;
            if (_knownWindows.Contains(newHwnd) || _redirectingWindows.ContainsKey(newHwnd)) return;

            // Only act once the window actually exists as a visible CabinetWClass (or on EVENT_OBJECT_CREATE wait for SHOW if still hidden)
            if (!Win32Helper.IsWindowVisible(newHwnd))
            {
                return;
            }

            if (!BookmarkManager.Instance.Settings.OpenFoldersInNewTab
                || Win32Helper.IsCtrlShiftDown()
                || IsRecentTabDetach())
            {
                MarkAsKnown(newHwnd);
                return;
            }

            IntPtr targetMainHwnd = FindTargetExplorerWindow(newHwnd);
            if (targetMainHwnd == IntPtr.Zero)
            {
                MarkAsKnown(newHwnd);
                return;
            }

            // Skip redirection if the new window already hosts multiple tabs
            if (Win32Helper.GetExplorerTabs(newHwnd).Count > 1)
            {
                MarkAsKnown(newHwnd);
                return;
            }

            var state = new RedirectState();
            CloakWindow(newHwnd, state);
            _redirectingWindows[newHwnd] = state;

            int ticks = 0;
            const int maxTicks = 80; // ~2000ms at 25ms interval
            var timer = new DispatcherTimer(DispatcherPriority.Normal);
            timer.Interval = TimeSpan.FromMilliseconds(25);
            timer.Tick += (s, e) =>
            {
                ticks++;
                if (!_isRunning || !Win32Helper.IsWindow(newHwnd))
                {
                    timer.Stop();
                    _redirectingWindows.Remove(newHwnd);
                    return;
                }

                // Abort redirection if the user detached a tab or the window gained multiple tabs
                if (IsRecentTabDetach() || Win32Helper.GetExplorerTabs(newHwnd).Count > 1)
                {
                    timer.Stop();
                    CompleteWithoutRedirect(newHwnd, state);
                    return;
                }

                var snapshot = ExplorerComHelper.TryGetExplorerWindowSnapshot(newHwnd);
                if (snapshot == null && ticks >= 24)
                {
                    snapshot = TryGetFallbackTabTitleSnapshot(newHwnd);
                }
                if (snapshot != null && !string.IsNullOrEmpty(snapshot.Path))
                {
                    if (IsControlPanelPath(snapshot.Path))
                    {
                        timer.Stop();
                        CompleteWithoutRedirect(newHwnd, state);
                        return;
                    }

                    timer.Stop();
                    _redirectingWindows.Remove(newHwnd);
                    _knownWindows.Add(newHwnd);

                    IntPtr hostHwnd = Win32Helper.IsWindow(targetMainHwnd)
                        ? targetMainHwnd
                        : FindTargetExplorerWindow(newHwnd);

                    if (hostHwnd == IntPtr.Zero)
                    {
                        UncloakWindow(newHwnd, state);
                        _windowTabCounts[newHwnd] = Win32Helper.GetExplorerTabs(newHwnd).Count;
                        return;
                    }

                    App.Log("Redirecting new Explorer window " + newHwnd + " (" + snapshot.Path + ") into tab of " + hostHwnd);
                    _closingRedirectedWindows.Add(newHwnd);

                    try
                    {
                        if (snapshot.ComWindow != null)
                        {
                            snapshot.ComWindow.Quit();
                        }
                    }
                    catch { }

                    if (Win32Helper.IsWindow(newHwnd))
                    {
                        Win32Helper.PostMessage(newHwnd, Win32Helper.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }

                    ExplorerComHelper.OpenInNewTab(hostHwnd, snapshot.Path, snapshot.SelectedNames);
                    return;
                }

                if (ticks >= maxTicks)
                {
                    timer.Stop();
                    App.Log("ExplorerTabUtilityService: Timed out waiting for snapshot on " + newHwnd + "; revealing window.");
                    CompleteWithoutRedirect(newHwnd, state);
                }
            };

            state.PollTimer = timer;
            timer.Start();
        }

        private static ExplorerComHelper.ExplorerWindowSnapshot TryGetFallbackTabTitleSnapshot(IntPtr explorerHwnd)
        {
            var tabs = Win32Helper.GetExplorerTabs(explorerHwnd);
            if (tabs.Count == 0) return null;

            IntPtr tabHwnd = tabs[0];
            string title = (Win32Helper.GetWindowText(tabHwnd) ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(title)) return null;

            string resolvedPath = null;
            if (title.Equals("This PC", StringComparison.OrdinalIgnoreCase)
                || title.Equals("My Computer", StringComparison.OrdinalIgnoreCase)
                || title.Equals("Home", StringComparison.OrdinalIgnoreCase)
                || title.Equals("Quick Access", StringComparison.OrdinalIgnoreCase)
                || title.Equals("Recycle Bin", StringComparison.OrdinalIgnoreCase)
                || title.Equals("Network", StringComparison.OrdinalIgnoreCase))
            {
                resolvedPath = title;
            }
            else if (title.Length >= 5
                && title[title.Length - 1] == ')'
                && title[title.Length - 2] == ':'
                && char.IsLetter(title[title.Length - 3])
                && title[title.Length - 4] == '('
                && title[title.Length - 5] == ' ')
            {
                resolvedPath = char.ToUpperInvariant(title[title.Length - 3]) + ":\\";
            }
            else if (System.IO.Directory.Exists(title))
            {
                resolvedPath = title;
            }

            if (string.IsNullOrEmpty(resolvedPath)) return null;

            return new ExplorerComHelper.ExplorerWindowSnapshot
            {
                ExplorerHwnd = explorerHwnd,
                TabHwnd = tabHwnd,
                Path = ExplorerComHelper.NormalizeSpecialPath(resolvedPath)
            };
        }

        private bool IsRecentTabDetach()
        {
            if (_lastTabDetachTick == 0) return false;
            int elapsed = unchecked(Environment.TickCount - _lastTabDetachTick);
            return elapsed >= 0 && elapsed < 1500;
        }

        private static bool IsControlPanelPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.IndexOf("26EE0668-A00A-44D7-9371-BEB064C98683", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("5399E694-AE9C-458F-A337-66197F6B01AA", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void MarkAsKnown(IntPtr hwnd)
        {
            _knownWindows.Add(hwnd);
            _windowTabCounts[hwnd] = Win32Helper.GetExplorerTabs(hwnd).Count;
            if (Win32Helper.GetForegroundWindow() == hwnd || _lastActiveExplorerHwnd == IntPtr.Zero)
            {
                _lastActiveExplorerHwnd = hwnd;
            }
        }

        private void CompleteWithoutRedirect(IntPtr hwnd, RedirectState state)
        {
            _redirectingWindows.Remove(hwnd);
            UncloakWindow(hwnd, state);
            MarkAsKnown(hwnd);
        }

        private IntPtr FindTargetExplorerWindow(IntPtr excludeHwnd)
        {
            if (IsValidTabHost(_lastActiveExplorerHwnd, excludeHwnd))
            {
                return _lastActiveExplorerHwnd;
            }

            var cabinets = Win32Helper.GetAllCabinetWindows();
            foreach (IntPtr hwnd in cabinets)
            {
                if (IsValidTabHost(hwnd, excludeHwnd))
                {
                    return hwnd;
                }
            }

            return IntPtr.Zero;
        }

        private bool IsValidTabHost(IntPtr hwnd, IntPtr excludeHwnd)
        {
            if (hwnd == IntPtr.Zero || hwnd == excludeHwnd) return false;
            if (!Win32Helper.IsWindow(hwnd) || !Win32Helper.IsWindowVisible(hwnd)) return false;
            if (_redirectingWindows.ContainsKey(hwnd)) return false;
            return Win32Helper.GetExplorerTabs(hwnd).Count > 0;
        }

        private static void CloakWindow(IntPtr hwnd, RedirectState state)
        {
            try
            {
                int exStyle = Win32Helper.GetWindowLong(hwnd, Win32Helper.GWL_EXSTYLE);
                if ((exStyle & Win32Helper.WS_EX_LAYERED) == 0)
                {
                    Win32Helper.SetWindowLong(hwnd, Win32Helper.GWL_EXSTYLE, exStyle | Win32Helper.WS_EX_LAYERED);
                    state.AddedLayeredStyle = true;
                }
                Win32Helper.SetLayeredWindowAttributes(hwnd, 0, 0, Win32Helper.LWA_ALPHA);
            }
            catch { }
        }

        private static void UncloakWindow(IntPtr hwnd, RedirectState state)
        {
            try
            {
                if (!Win32Helper.IsWindow(hwnd)) return;
                Win32Helper.SetLayeredWindowAttributes(hwnd, 0, 255, Win32Helper.LWA_ALPHA);
                if (state != null && state.AddedLayeredStyle)
                {
                    int exStyle = Win32Helper.GetWindowLong(hwnd, Win32Helper.GWL_EXSTYLE);
                    Win32Helper.SetWindowLong(hwnd, Win32Helper.GWL_EXSTYLE, exStyle & ~Win32Helper.WS_EX_LAYERED);
                }
            }
            catch { }
        }
    }
}
