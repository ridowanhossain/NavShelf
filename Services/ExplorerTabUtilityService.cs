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
        private IntPtr _locHook;
        private IntPtr _moveSizeHook;
        private Win32Helper.WinEventDelegate _winEventDelegate;
        private IntPtr _mouseHook;
        private Win32Helper.LowLevelMouseProc _mouseProc;
        private Dispatcher _dispatcher;
        private DispatcherTimer _desktopResolveTimer;
        private IntPtr _lastDesktopClickListViewHwnd;
        private Win32Helper.POINT _lastDesktopClickPt;
        private uint _lastDesktopClickTimeMs;
        private bool _hasDesktopClickCandidate;
        private IntPtr _pendingDesktopListViewHwnd;
        private int _pendingDesktopItemIndex = -1;
        private string _pendingDesktopFolderPath;
        private bool _pendingDesktopResolved;
        private int _resolveGeneration;
        private bool _swallowingSecondClick;
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
            _dispatcher = Dispatcher.CurrentDispatcher;

            SeedKnownWindows();

            _winEventDelegate = OnWinEvent;
            _winEventHook = Win32Helper.SetWinEventHook(
                Win32Helper.EVENT_OBJECT_CREATE,
                Win32Helper.EVENT_OBJECT_REORDER,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                Win32Helper.WINEVENT_OUTOFCONTEXT | Win32Helper.WINEVENT_SKIPOWNPROCESS);

            _locHook = Win32Helper.SetWinEventHook(
                0x800B, // EVENT_OBJECT_LOCATIONCHANGE
                0x800B,
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                Win32Helper.WINEVENT_OUTOFCONTEXT | Win32Helper.WINEVENT_SKIPOWNPROCESS);

            _moveSizeHook = Win32Helper.SetWinEventHook(
                0x000A, // EVENT_SYSTEM_MOVESIZESTART
                0x000B, // EVENT_SYSTEM_MOVESIZEEND
                IntPtr.Zero,
                _winEventDelegate,
                0,
                0,
                Win32Helper.WINEVENT_OUTOFCONTEXT | Win32Helper.WINEVENT_SKIPOWNPROCESS);

            _mouseProc = MouseHookProc;
            using (var curProcess = System.Diagnostics.Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                IntPtr hMod = Win32Helper.GetModuleHandle(curModule != null ? curModule.ModuleName : null);
                _mouseHook = Win32Helper.SetWindowsHookEx(Win32Helper.WH_MOUSE_LL, _mouseProc, hMod, 0);
            }

            DispatchUiAction(delegate { ExplorerComHelper.WarmUpDesktopViewCache(); });

            App.Log("ExplorerTabUtilityService started.");
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;

            if (_desktopResolveTimer != null)
            {
                _desktopResolveTimer.Stop();
                _desktopResolveTimer = null;
            }

            if (_mouseHook != IntPtr.Zero)
            {
                Win32Helper.UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            _mouseProc = null;
            ResetDesktopClickTracking();

            if (_winEventHook != IntPtr.Zero)
            {
                Win32Helper.UnhookWinEvent(_winEventHook);
                _winEventHook = IntPtr.Zero;
            }
            _winEventDelegate = null;
            if (_locHook != IntPtr.Zero)
            {
                Win32Helper.UnhookWinEvent(_locHook);
                _locHook = IntPtr.Zero;
            }
            if (_moveSizeHook != IntPtr.Zero)
            {
                Win32Helper.UnhookWinEvent(_moveSizeHook);
                _moveSizeHook = IntPtr.Zero;
            }

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

        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0 || !_isRunning || lParam == IntPtr.Zero)
            {
                return Win32Helper.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            }

            int msg = wParam.ToInt32();
            if (msg == Win32Helper.WM_MBUTTONDOWN || msg == Win32Helper.WM_MBUTTONUP)
            {
                try
                {
                    var mbInfo = (Win32Helper.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                        lParam,
                        typeof(Win32Helper.MSLLHOOKSTRUCT));
                    IntPtr mbHit = Win32Helper.WindowFromPoint(mbInfo.pt);
                    if (mbHit != IntPtr.Zero)
                    {
                        IntPtr mbRoot = Win32Helper.GetRootWindow(mbHit);
                        if (mbRoot != IntPtr.Zero && ExplorerChildBarManager.Instance.HasBarFor(mbRoot))
                        {
                            ExplorerChildBarManager.Instance.StartPinBurst(mbRoot, 850);
                        }
                    }
                }
                catch { }
                return Win32Helper.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            }

            if (msg != Win32Helper.WM_LBUTTONDOWN && msg != Win32Helper.WM_LBUTTONUP)
            {
                return Win32Helper.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            }

            try
            {
                if (msg == Win32Helper.WM_LBUTTONUP)
                {
                    if (_swallowingSecondClick)
                    {
                        _swallowingSecondClick = false;
                        return (IntPtr)1;
                    }

                    if (_hasDesktopClickCandidate && (!_pendingDesktopResolved || _pendingDesktopItemIndex < 0))
                    {
                        IntPtr lvUpHwnd = _lastDesktopClickListViewHwnd;
                        Win32Helper.POINT ptUp = _lastDesktopClickPt;
                        int genUp = _resolveGeneration;
                        DispatchUiAction(delegate { ResolveDesktopClick(lvUpHwnd, ptUp, genUp); });
                    }
                    return Win32Helper.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }

                // WM_LBUTTONDOWN
                _swallowingSecondClick = false;

                if (!BookmarkManager.Instance.Settings.OpenFoldersInNewTab || Win32Helper.IsAnyModifierDown())
                {
                    ResetDesktopClickTracking();
                    return Win32Helper.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }

                var info = (Win32Helper.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(
                    lParam,
                    typeof(Win32Helper.MSLLHOOKSTRUCT));
                if ((info.flags & Win32Helper.LLMHF_INJECTED) != 0)
                {
                    return Win32Helper.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }

                IntPtr hitHwnd = Win32Helper.WindowFromPoint(info.pt);
                IntPtr lvHwnd;
                if (!Win32Helper.IsDesktopListView(hitHwnd, out lvHwnd))
                {
                    ResetDesktopClickTracking();
                    return Win32Helper.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }

                uint clickTime = info.time;
                uint dblTime = Win32Helper.GetDoubleClickTime();
                int cxHalf = Math.Max(6, Win32Helper.GetSystemMetrics(Win32Helper.SM_CXDOUBLECLK));
                int cyHalf = Math.Max(6, Win32Helper.GetSystemMetrics(Win32Helper.SM_CYDOUBLECLK));

                bool isSecondClickInTimeAndSpace = _hasDesktopClickCandidate
                    && _lastDesktopClickListViewHwnd == lvHwnd
                    && unchecked(clickTime - _lastDesktopClickTimeMs) <= dblTime
                    && Math.Abs(info.pt.X - _lastDesktopClickPt.X) <= cxHalf
                    && Math.Abs(info.pt.Y - _lastDesktopClickPt.Y) <= cyHalf;

                if (isSecondClickInTimeAndSpace)
                {
                    if (Win32Helper.GetDesktopSelectedCount(lvHwnd) == 1)
                    {
                        int currentIdx = Win32Helper.GetDesktopPrimaryItemIndex(lvHwnd);
                        if (currentIdx >= 0)
                        {
                            if (!_pendingDesktopResolved
                                || _pendingDesktopListViewHwnd != lvHwnd
                                || _pendingDesktopItemIndex != currentIdx)
                            {
                                _pendingDesktopFolderPath = ExplorerComHelper.TryGetDesktopFolderPathAtClick(lvHwnd, currentIdx, info.pt);
                                _pendingDesktopListViewHwnd = lvHwnd;
                                _pendingDesktopItemIndex = currentIdx;
                                _pendingDesktopResolved = true;
                            }

                            if (!string.IsNullOrEmpty(_pendingDesktopFolderPath))
                            {
                                IntPtr hostHwnd = FindTargetExplorerWindow(IntPtr.Zero);
                                if (hostHwnd != IntPtr.Zero)
                                {
                                    string targetPath = _pendingDesktopFolderPath;
                                    ResetDesktopClickTracking();
                                    _swallowingSecondClick = true;

                                    DispatchUiAction(delegate { OpenDesktopFolderInExistingWindow(hostHwnd, targetPath); });
                                    return (IntPtr)1;
                                }
                            }
                        }
                    }

                    ResetDesktopClickTracking();
                    return Win32Helper.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }

                _lastDesktopClickListViewHwnd = lvHwnd;
                _lastDesktopClickPt = info.pt;
                _lastDesktopClickTimeMs = clickTime;
                _hasDesktopClickCandidate = true;
                _pendingDesktopListViewHwnd = lvHwnd;
                _pendingDesktopItemIndex = -1;
                _pendingDesktopFolderPath = null;
                _pendingDesktopResolved = false;
                int gen = ++_resolveGeneration;
                Win32Helper.POINT capturedPt = info.pt;

                DispatchUiAction(delegate { ScheduleDesktopClickResolve(lvHwnd, capturedPt, gen); });
            }
            catch (Exception ex)
            {
                App.Log("MouseHookProc error: " + ex.Message);
            }

            return Win32Helper.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private void DispatchUiAction(Action action)
        {
            if (action == null) return;
            Dispatcher dispatcher = _dispatcher;
            if (dispatcher == null && System.Windows.Application.Current != null)
            {
                dispatcher = System.Windows.Application.Current.Dispatcher;
            }
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
            }
        }

        private void ResetDesktopClickTracking()
        {
            _hasDesktopClickCandidate = false;
            _lastDesktopClickListViewHwnd = IntPtr.Zero;
            _lastDesktopClickPt = new Win32Helper.POINT();
            _lastDesktopClickTimeMs = 0;
            _pendingDesktopListViewHwnd = IntPtr.Zero;
            _pendingDesktopItemIndex = -1;
            _pendingDesktopFolderPath = null;
            _pendingDesktopResolved = false;
            _resolveGeneration++;
            if (_desktopResolveTimer != null)
            {
                _desktopResolveTimer.Stop();
            }
        }

        private void ScheduleDesktopClickResolve(IntPtr lvHwnd, Win32Helper.POINT clickPt, int gen)
        {
            if (!_isRunning || gen != _resolveGeneration || !_hasDesktopClickCandidate) return;

            if (_desktopResolveTimer != null)
            {
                _desktopResolveTimer.Stop();
            }

            var timer = new DispatcherTimer(DispatcherPriority.Normal);
            timer.Interval = TimeSpan.FromMilliseconds(5);
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                ResolveDesktopClick(lvHwnd, clickPt, gen);
            };
            _desktopResolveTimer = timer;
            timer.Start();
        }

        private void ResolveDesktopClick(IntPtr lvHwnd, Win32Helper.POINT clickPt, int gen)
        {
            if (!_isRunning || gen != _resolveGeneration || !_hasDesktopClickCandidate) return;
            if (!Win32Helper.IsWindow(lvHwnd)) return;

            int selCount = Win32Helper.GetDesktopSelectedCount(lvHwnd);
            if (selCount != 1)
            {
                _pendingDesktopItemIndex = -1;
                _pendingDesktopFolderPath = null;
                _pendingDesktopResolved = false;
                return;
            }

            int itemIndex = Win32Helper.GetDesktopPrimaryItemIndex(lvHwnd);
            if (itemIndex < 0)
            {
                _pendingDesktopItemIndex = -1;
                _pendingDesktopFolderPath = null;
                _pendingDesktopResolved = false;
                return;
            }

            string folderPath = ExplorerComHelper.TryGetDesktopFolderPathAtClick(lvHwnd, itemIndex, clickPt);
            if (gen != _resolveGeneration || !_hasDesktopClickCandidate) return;

            _pendingDesktopListViewHwnd = lvHwnd;
            _pendingDesktopItemIndex = itemIndex;
            _pendingDesktopFolderPath = folderPath;
            _pendingDesktopResolved = true;
        }

        private void OpenDesktopFolderInExistingWindow(IntPtr preferredHostHwnd, string targetPath)
        {
            if (!_isRunning || string.IsNullOrEmpty(targetPath)) return;

            IntPtr hostHwnd = IsValidTabHost(preferredHostHwnd, IntPtr.Zero)
                ? preferredHostHwnd
                : FindTargetExplorerWindow(IntPtr.Zero);
            if (hostHwnd == IntPtr.Zero) return;

            _lastActiveExplorerHwnd = hostHwnd;
            App.Log("Direct desktop double-click opening '" + targetPath + "' in tab of " + hostHwnd);
            ExplorerComHelper.OpenInNewTab(hostHwnd, targetPath, null);
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (!_isRunning) return;
            if (eventType == 0x8001 || eventType == 0x8003) return; // Ignore DESTROY / HIDE

            if (eventType == 0x000A) // EVENT_SYSTEM_MOVESIZESTART
            {
                if (hwnd != IntPtr.Zero && ExplorerChildBarManager.Instance.HasBarFor(hwnd))
                {
                    ExplorerChildBarManager.Instance.OnMoveSizeStart(hwnd);
                }
                return;
            }
            if (eventType == 0x000B) // EVENT_SYSTEM_MOVESIZEEND
            {
                if (hwnd != IntPtr.Zero && ExplorerChildBarManager.Instance.HasBarFor(hwnd))
                {
                    ExplorerChildBarManager.Instance.OnMoveSizeEnd(hwnd);
                }
                return;
            }

            if (eventType == Win32Helper.EVENT_OBJECT_REORDER)
            {
                if (idObject == 0 && hwnd != IntPtr.Zero && ExplorerChildBarManager.Instance.HasBarFor(hwnd))
                {
                    if (ExplorerChildBarManager.Instance.IsResizing(hwnd))
                    {
                        ExplorerChildBarManager.Instance.EnsureBarOnTopNow(hwnd);
                        return;
                    }
                    if (ExplorerChildBarManager.Instance.EnsureBarOnTopNow(hwnd))
                    {
                        ExplorerChildBarManager.Instance.StartPinBurst(hwnd, 500);
                    }
                    else
                    {
                        ExplorerChildBarManager.Instance.PinAllNow(hwnd);
                    }
                }
                return;
            }

            if (eventType == 0x800B)
            {
                if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;

                // Fast-path: live CabinetWClass resize updates bar width immediately without polling bursts
                if (ExplorerChildBarManager.Instance.HasBarFor(hwnd))
                {
                    ExplorerChildBarManager.Instance.OnHostResizedOrMoved(hwnd);
                    return;
                }

                // Explorer re-laid out a tab or header: keep bar above and tab below without spawning a 1ms burst
                string lcls = Win32Helper.GetWindowClassName(hwnd);
                if (lcls == "Microsoft.UI.Content.DesktopChildSiteBridge")
                {
                    IntPtr bcab = Win32Helper.GetParent(hwnd);
                    if (bcab != IntPtr.Zero && ExplorerChildBarManager.Instance.HasBarFor(bcab))
                    {
                        ExplorerChildBarManager.Instance.EnsureBarOnTopNow(bcab);
                    }
                    return;
                }
                if (lcls != "ShellTabWindowClass" && lcls != "TabWindowClass") return;
                IntPtr cab = Win32Helper.GetParent(hwnd);
                if (cab != IntPtr.Zero && ExplorerChildBarManager.Instance.HasBarFor(cab))
                {
                    ExplorerChildBarManager.Instance.EnsureBarOnTopNow(cab);
                    if (!ExplorerChildBarManager.Instance.IsResizing(cab))
                    {
                        ExplorerChildBarManager.Instance.PinTabNow(cab, hwnd);
                    }
                }
                return;
            }
            if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) return;

            string cls = Win32Helper.GetWindowClassName(hwnd);
            if (cls == "ShellTabWindowClass" || cls == "TabWindowClass")
            {
                IntPtr cabinetHwnd = Win32Helper.GetRootWindow(hwnd);
                if (cabinetHwnd != IntPtr.Zero && ExplorerChildBarManager.Instance.HasBarFor(cabinetHwnd))
                {
                    ExplorerChildBarManager.Instance.OnExplorerChildLayoutEvent(cabinetHwnd, hwnd);
                }
                return;
            }

            if (cls != "CabinetWClass") return;

            RedirectState existingState;
            if (eventType == Win32Helper.EVENT_OBJECT_SHOW && _redirectingWindows.TryGetValue(hwnd, out existingState))
            {
                // ShowWindow can reset layered alpha when surfacing the window; re-enforce cloak immediately.
                CloakWindow(hwnd, existingState);
                return;
            }

            TrackExistingWindows();
            HandleNewCabinetWindow(hwnd, eventType == Win32Helper.EVENT_OBJECT_CREATE);
        }

        private void HandleNewCabinetWindow(IntPtr newHwnd, bool allowBeforeVisible = false)
        {
            if (!_isRunning || newHwnd == IntPtr.Zero || !Win32Helper.IsWindow(newHwnd)) return;
            if (_knownWindows.Contains(newHwnd) || _redirectingWindows.ContainsKey(newHwnd)) return;

            if (!allowBeforeVisible && !Win32Helper.IsWindowVisible(newHwnd))
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
            const int maxTicks = 180; // ~1800ms at 10ms interval
            var timer = new DispatcherTimer(DispatcherPriority.Normal);
            timer.Interval = TimeSpan.FromMilliseconds(10);
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

                // Keep window cloaked while polling so ShowWindow cannot flash before navigation/redirect completes.
                CloakWindow(newHwnd, state);

                var snapshot = ExplorerComHelper.TryGetExplorerWindowSnapshot(newHwnd);
                if (snapshot == null && ticks >= 8)
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
            return path.IndexOf("26EE0668", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("5399E694", StringComparison.OrdinalIgnoreCase) >= 0;
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
                int disableTransitions = 1;
                Win32Helper.DwmSetWindowAttribute(hwnd, Win32Helper.DWMWA_TRANSITIONS_FORCEDISABLED, ref disableTransitions, sizeof(int));

                int exStyle = Win32Helper.GetWindowLong(hwnd, Win32Helper.GWL_EXSTYLE);
                if ((exStyle & Win32Helper.WS_EX_LAYERED) == 0)
                {
                    Win32Helper.SetWindowLong(hwnd, Win32Helper.GWL_EXSTYLE, exStyle | Win32Helper.WS_EX_LAYERED);
                    if (state != null) state.AddedLayeredStyle = true;
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

                int enableTransitions = 0;
                Win32Helper.DwmSetWindowAttribute(hwnd, Win32Helper.DWMWA_TRANSITIONS_FORCEDISABLED, ref enableTransitions, sizeof(int));
            }
            catch { }
        }
    }
}
