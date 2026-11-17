using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ExplorerBookmarkBar.Views;

namespace ExplorerBookmarkBar.Services
{
    public class ExplorerChildBarManager
    {
        private static ExplorerChildBarManager _instance;
        public static ExplorerChildBarManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ExplorerChildBarManager();
                }
                return _instance;
            }
        }

        private class ChildBarEntry
        {
            public IntPtr ExplorerHwnd;
            public IntPtr ChildHwnd;
            public HwndSource Source;
            public BookmarkBarControl Control;
            public IntPtr ShellTabHwnd;
            public int LastWidth;
            public int LastHeight;
            public int LastClientHeight;
            public int LastTop;
            public bool LastZoomed;
            public int SettleTicks;
            public double LastDpi;
            public double LastLogicalTop;
            public bool RepaintParentAfterSettle;
            public bool IsMoveSizing;
            public int LastResizeTick;
            public DispatcherTimer ResizeEndTimer;
        }

        private const int WM_DPICHANGED_BEFOREPARENT = 0x02E2;
        private const int WM_DPICHANGED_AFTERPARENT = 0x02E3;

        // Number of 30ms ticks to keep forcing reposition/redraw after a maximize/restore
        private const int SettleTickCount = 15;

        public static Window ActiveModalDialog { get; set; }
        public static event Action<bool> ModalDialogStateChanged;

        public static void SetActiveModalDialog(Window win)
        {
            ActiveModalDialog = win;
            var handler = ModalDialogStateChanged;
            if (win != null)
            {
                if (handler != null) handler(true);
                win.Closed += (s, e) =>
                {
                    if (ActiveModalDialog == win)
                    {
                        ActiveModalDialog = null;
                        var h = ModalDialogStateChanged;
                        if (h != null) h(false);
                    }
                };
            }
            else
            {
                if (handler != null) handler(false);
            }
        }

        private readonly ConcurrentDictionary<IntPtr, ChildBarEntry> _activeBars = new ConcurrentDictionary<IntPtr, ChildBarEntry>();
        private readonly object _pinBurstLock = new object();
        private readonly Dictionary<IntPtr, long> _pinBurstDeadlines = new Dictionary<IntPtr, long>();
        private readonly HashSet<IntPtr> _activePinBurstThreads = new HashSet<IntPtr>();
        private readonly DispatcherTimer _syncTimer;
        private bool _isPaused = false;
        private int _themeCheckTick = 0;

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint ms);

        public ExplorerChildBarManager()
        {
            _syncTimer = new DispatcherTimer(DispatcherPriority.Render);
            _syncTimer.Interval = TimeSpan.FromMilliseconds(40);
            _syncTimer.Tick += (s, e) => SyncExplorerChildBars();

            BookmarkManager.Instance.SettingsChanged += () =>
            {
                foreach (var entry in _activeBars.Values)
                {
                    entry.LastHeight = -1;
                    entry.LastTop = -1;
                }
                SyncExplorerChildBars();
            };

            ThemeManager.ThemeChanged += () =>
            {
                foreach (var entry in _activeBars.Values)
                {
                    if (entry.Control != null)
                    {
                        entry.Control.ApplyTheme();
                    }
                }
            };
        }

        public bool IsPaused
        {
            get { return _isPaused; }
        }

        /// <summary>Returns true if a bookmark bar is currently attached to the given Explorer window.</summary>
        public bool HasBarFor(IntPtr hostHwnd)
        {
            return hostHwnd != IntPtr.Zero && _activeBars.ContainsKey(hostHwnd);
        }

        private bool IsActivelyResizing(ChildBarEntry entry)
        {
            if (entry == null) return false;
            if ((Win32Helper.GetAsyncKeyState(Win32Helper.VK_LBUTTON) & 0x8000) == 0)
            {
                entry.IsMoveSizing = false;
                return false;
            }
            return entry.IsMoveSizing || unchecked(Environment.TickCount - entry.LastResizeTick) < 90;
        }

        public bool IsResizing(IntPtr expHwnd)
        {
            ChildBarEntry entry;
            if (expHwnd != IntPtr.Zero && _activeBars.TryGetValue(expHwnd, out entry))
            {
                return IsActivelyResizing(entry);
            }
            return false;
        }

        private void CancelPinBurst(IntPtr expHwnd)
        {
            lock (_pinBurstLock)
            {
                _pinBurstDeadlines.Remove(expHwnd);
            }
        }

        public void OnMoveSizeStart(IntPtr expHwnd)
        {
            ChildBarEntry entry;
            if (expHwnd != IntPtr.Zero && _activeBars.TryGetValue(expHwnd, out entry))
            {
                entry.IsMoveSizing = true;
                entry.LastResizeTick = Environment.TickCount;
                CancelPinBurst(expHwnd);
            }
        }

        public void OnMoveSizeEnd(IntPtr expHwnd)
        {
            ChildBarEntry entry;
            if (expHwnd != IntPtr.Zero && _activeBars.TryGetValue(expHwnd, out entry))
            {
                entry.IsMoveSizing = false;
                if (entry.ResizeEndTimer != null)
                {
                    entry.ResizeEndTimer.Stop();
                }
                UpdateOrAttachChildBar(expHwnd);

                var settleTimer = new DispatcherTimer(DispatcherPriority.Render);
                settleTimer.Interval = TimeSpan.FromMilliseconds(120);
                settleTimer.Tick += (s, e) =>
                {
                    settleTimer.Stop();
                    if (!_isPaused && _activeBars.ContainsKey(expHwnd) && Win32Helper.IsWindow(expHwnd) && !Win32Helper.IsIconic(expHwnd))
                    {
                        UpdateOrAttachChildBar(expHwnd);
                        PinAllNow(expHwnd);
                    }
                };
                settleTimer.Start();
            }
        }

        /// <summary>
        /// Keeps the bookmark bar on top and all tab windows pushed down below it at ~1ms cadence
        /// for durationMs (coalesced per Explorer window) while Explorer creates/switches/lays out tabs.
        /// </summary>
        public void StartPinBurst(IntPtr expHwnd, int durationMs = 350)
        {
            if (_isPaused || expHwnd == IntPtr.Zero || !_activeBars.ContainsKey(expHwnd)) return;
            if (IsResizing(expHwnd)) return;

            PinAllNow(expHwnd);

            long durationTicks = (long)(durationMs * (Stopwatch.Frequency / 1000.0));
            long targetTicks = Stopwatch.GetTimestamp() + durationTicks;
            bool shouldSpawn = false;
            lock (_pinBurstLock)
            {
                long existingDeadline;
                if (!_pinBurstDeadlines.TryGetValue(expHwnd, out existingDeadline) || targetTicks > existingDeadline)
                {
                    _pinBurstDeadlines[expHwnd] = targetTicks;
                }
                if (_activePinBurstThreads.Add(expHwnd))
                {
                    shouldSpawn = true;
                }
            }

            if (!shouldSpawn) return;

            var thread = new Thread(() =>
            {
                timeBeginPeriod(1);
                try
                {
                    while (!_isPaused)
                    {
                        long deadline;
                        lock (_pinBurstLock)
                        {
                            if (!_pinBurstDeadlines.TryGetValue(expHwnd, out deadline))
                                break;
                            if (Stopwatch.GetTimestamp() >= deadline)
                                break;
                        }

                        if (!Win32Helper.IsWindow(expHwnd) || !_activeBars.ContainsKey(expHwnd) || IsResizing(expHwnd))
                            break;

                        PinAllNow(expHwnd);
                        Thread.Sleep(1);
                    }
                }
                catch { }
                finally
                {
                    lock (_pinBurstLock)
                    {
                        _pinBurstDeadlines.Remove(expHwnd);
                        _activePinBurstThreads.Remove(expHwnd);
                    }
                    timeEndPeriod(1);
                }
            });
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.Highest;
            thread.Start();
        }

        /// <summary>Returns the bookmark bar attached to the given Explorer window / file dialog, or null.</summary>
        public BookmarkBarControl GetBarFor(IntPtr hostHwnd)
        {
            ChildBarEntry entry;
            if (hostHwnd != IntPtr.Zero && _activeBars.TryGetValue(hostHwnd, out entry))
            {
                return entry.Control;
            }
            return null;
        }

        public void Start()
        {
            _isPaused = false;
            _syncTimer.Start();
            SyncExplorerChildBars();
        }

        public void Stop()
        {
            _isPaused = true;
            _syncTimer.Stop();
            CleanupAllBars();
        }

        public void ToggleVisibility()
        {
            _isPaused = !_isPaused;
            if (_isPaused)
            {
                foreach (var entry in _activeBars.Values)
                {
                    if (entry.ChildHwnd != IntPtr.Zero && Win32Helper.IsWindow(entry.ChildHwnd))
                    {
                        Win32Helper.SetWindowPos(entry.ChildHwnd, IntPtr.Zero, 0, 0, 0, 0, Win32Helper.SWP_HIDEWINDOW | Win32Helper.SWP_NOMOVE | Win32Helper.SWP_NOSIZE | Win32Helper.SWP_NOACTIVATE);
                    }
                }
            }
            else
            {
                SyncExplorerChildBars();
            }
        }

        /// <summary>
        /// Thread-safe-ish helper for a background thread: keeps the bar on top and every tab below it.
        /// Called ~every millisecond while a new tab is being opened so Explorer's own re-layout is undone
        /// before the next frame is composed.
        /// </summary>
        public void PinAllNow(IntPtr expHwnd)
        {
            try
            {
                ChildBarEntry entry;
                if (!_activeBars.TryGetValue(expHwnd, out entry)) return;
                if (entry.LastHeight <= 0 || entry.LastTop <= 0) return;
                EnsureBarOnTopNow(expHwnd);
                var tabs = Win32Helper.GetExplorerTabs(expHwnd);
                for (int i = 0; i < tabs.Count; i++)
                {
                    PinTabNow(expHwnd, tabs[i]);
                }
            }
            catch { }
        }

        /// <summary>
        /// Ensures the bookmark bar sits above all Explorer content/bridge children while leaving
        /// Explorer's title-bar scaffolding windows undisturbed at the top so Explorer doesn't fight Z-order.
        /// Returns true if the bar's Z-order had to be corrected.
        /// </summary>
        public bool EnsureBarOnTopNow(IntPtr expHwnd)
        {
            if (_isPaused) return false;
            ChildBarEntry entry;
            if (!_activeBars.TryGetValue(expHwnd, out entry)) return false;
            try
            {
                if (entry.ChildHwnd == IntPtr.Zero || !Win32Helper.IsWindow(entry.ChildHwnd))
                    return false;

                IntPtr cur = Win32Helper.GetTopWindow(expHwnd);
                if (cur == entry.ChildHwnd) return false;

                IntPtr insertAfter = Win32Helper.HWND_TOP;
                bool coveredByContent = false;
                int steps = 0;
                while (cur != IntPtr.Zero && cur != entry.ChildHwnd && steps < 16)
                {
                    string cls = Win32Helper.GetWindowClassName(cur);
                    if (cls == "InputNonClientPointerSource" || cls == "TITLE_BAR_SCAFFOLDING_WINDOW_CLASS")
                    {
                        insertAfter = cur;
                    }
                    else
                    {
                        coveredByContent = true;
                        break;
                    }
                    cur = Win32Helper.GetNextWindow(cur, Win32Helper.GW_HWNDNEXT);
                    steps++;
                }

                if (!coveredByContent && cur == entry.ChildHwnd)
                    return false;

                Win32Helper.SetWindowPos(entry.ChildHwnd, insertAfter, 0, 0, 0, 0,
                    Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_NOMOVE | Win32Helper.SWP_NOSIZE
                    | Win32Helper.SWP_NOSENDCHANGING);
                return true;
            }
            catch { }
            return false;
        }

        /// <summary>Lightweight: if a tab window was moved by Explorer, snap it back below the bar right away.</summary>
        public void PinTabNow(IntPtr expHwnd, IntPtr tabHwnd)
        {
            if (_isPaused) return;
            ChildBarEntry entry;
            if (!_activeBars.TryGetValue(expHwnd, out entry)) return;
            if (entry.LastHeight <= 0 || entry.LastTop <= 0) return;
            try
            {
                Win32Helper.RECT clientRect;
                if (!Win32Helper.GetClientRect(expHwnd, out clientRect) || clientRect.Width < 100) return;
                int contentTop = entry.LastTop + entry.LastHeight;
                int tabHeight = clientRect.Height - contentTop;
                if (tabHeight <= 50) return;

                Win32Helper.POINT o = new Win32Helper.POINT { X = 0, Y = 0 };
                Win32Helper.ClientToScreen(expHwnd, ref o);
                Win32Helper.RECT r;
                if (!Win32Helper.GetWindowRect(tabHwnd, out r)) return;
                if (Math.Abs((r.Top - o.Y) - contentTop) > 1 || Math.Abs((r.Bottom - r.Top) - tabHeight) > 2)
                {
                    Win32Helper.SetWindowPos(tabHwnd, IntPtr.Zero, 0, contentTop, clientRect.Width, tabHeight,
                        Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_NOZORDER | Win32Helper.SWP_NOSENDCHANGING | Win32Helper.SWP_ASYNCWINDOWPOS);
                }
            }
            catch { }
        }

        /// <summary>
        /// Fast-path for live corner-drag resizing of CabinetWClass: updates child bar width immediately
        /// on EVENT_OBJECT_LOCATIONCHANGE without RedrawWindow/InvalidateVisual/StartPinBurst overhead.
        /// </summary>
        public void OnHostResizedOrMoved(IntPtr expHwnd)
        {
            if (_isPaused || expHwnd == IntPtr.Zero) return;
            ChildBarEntry entry;
            if (!_activeBars.TryGetValue(expHwnd, out entry)) return;
            if (entry.LastHeight <= 0 || entry.LastTop <= 0) return;
            if (Win32Helper.IsIconic(expHwnd)) return;

            try
            {
                double dpiScale = Win32Helper.GetWindowDpiScale(expHwnd);
                if (dpiScale > 0 && Math.Abs(dpiScale - entry.LastDpi) > 0.001)
                {
                    UpdateOrAttachChildBar(expHwnd);
                    return;
                }

                Win32Helper.RECT clientRect;
                if (!Win32Helper.GetClientRect(expHwnd, out clientRect) || clientRect.Width < 100 || clientRect.Height < 100)
                    return;

                // Pure window drag-move (size unchanged): do nothing
                if (clientRect.Width == entry.LastWidth && clientRect.Height == entry.LastClientHeight)
                    return;

                entry.LastResizeTick = Environment.TickCount;
                CancelPinBurst(expHwnd);

                bool widthChanged = (clientRect.Width != entry.LastWidth);
                entry.LastWidth = clientRect.Width;
                entry.LastClientHeight = clientRect.Height;

                if (widthChanged && entry.ChildHwnd != IntPtr.Zero && Win32Helper.IsWindow(entry.ChildHwnd))
                {
                    Win32Helper.SetWindowPos(
                        entry.ChildHwnd,
                        IntPtr.Zero,
                        0,
                        entry.LastTop,
                        clientRect.Width,
                        entry.LastHeight,
                        Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_NOZORDER | Win32Helper.SWP_NOSENDCHANGING
                    );
                }

                EnsureBarOnTopNow(expHwnd);
                if (!IsActivelyResizing(entry))
                {
                    if (entry.ShellTabHwnd != IntPtr.Zero && Win32Helper.IsWindow(entry.ShellTabHwnd))
                    {
                        PinTabNow(expHwnd, entry.ShellTabHwnd);
                    }
                }
                else
                {
                    if (entry.ResizeEndTimer == null)
                    {
                        var trackedEntry = entry;
                        var trackedHwnd = expHwnd;
                        entry.ResizeEndTimer = new DispatcherTimer(DispatcherPriority.Render);
                        entry.ResizeEndTimer.Interval = TimeSpan.FromMilliseconds(65);
                        entry.ResizeEndTimer.Tick += (s, e) =>
                        {
                            trackedEntry.ResizeEndTimer.Stop();
                            if (!_isPaused && _activeBars.ContainsKey(trackedHwnd) && Win32Helper.IsWindow(trackedHwnd))
                            {
                                PinAllNow(trackedHwnd);
                            }
                        };
                    }
                    entry.ResizeEndTimer.Stop();
                    entry.ResizeEndTimer.Start();
                }
            }
            catch { }
        }

        private readonly HashSet<IntPtr> _pendingLayoutBurst = new HashSet<IntPtr>();

        /// <summary>
        /// Immediately re-asserts child bar Z-order and pushes down ShellTabWindowClass children
        /// when Explorer creates, shows, or reorders tabs instead of waiting up to 40ms for _syncTimer.
        /// </summary>
        public void OnExplorerChildLayoutEvent(IntPtr expHwnd, IntPtr childHwnd)
        {
            if (_isPaused || expHwnd == IntPtr.Zero) return;

            ChildBarEntry entry;
            if (!_activeBars.TryGetValue(expHwnd, out entry)) return;
            if (!Win32Helper.IsWindow(expHwnd) || Win32Helper.IsIconic(expHwnd)) return;

            try
            {
                EnsureBarOnTopNow(expHwnd);

                // Immediately push down the newly created/shown tab window without waiting for _syncTimer
                // and without forcing a full redraw of the bookmark bar.
                if (childHwnd != IntPtr.Zero && Win32Helper.IsWindow(childHwnd))
                {
                    PinTabNow(expHwnd, childHwnd);
                }

                StartPinBurst(expHwnd, 600);

                if (_pendingLayoutBurst.Add(expHwnd))
                {
                    var burstTimer = new DispatcherTimer(DispatcherPriority.Send);
                    burstTimer.Interval = TimeSpan.FromMilliseconds(15);
                    burstTimer.Tick += (s, e) =>
                    {
                        burstTimer.Stop();
                        _pendingLayoutBurst.Remove(expHwnd);
                        if (_isPaused || !_activeBars.ContainsKey(expHwnd)) return;
                        if (Win32Helper.IsWindow(expHwnd) && !Win32Helper.IsIconic(expHwnd))
                        {
                            UpdateOrAttachChildBar(expHwnd);
                        }
                    };
                    burstTimer.Start();
                }
            }
            catch { }
        }

        private void SyncExplorerChildBars()
        {
            if (_isPaused) return;

            try
            {
                // Theme changes are already handled via SystemEvents.UserPreferenceChanged;
                // only poll registry occasionally as a lightweight fallback (~every 3s).
                if (++_themeCheckTick >= 50)
                {
                    _themeCheckTick = 0;
                    ThemeManager.CheckAndApplyTheme();
                }

                ExplorerTabUtilityService.Instance.TrackExistingWindows();

                var windows = Win32Helper.GetAllVisibleExplorerWindows();
                HashSet<IntPtr> currentWindows = new HashSet<IntPtr>(windows);

                // Attach child bar to any new Explorer windows on any monitor
                foreach (var expHwnd in windows)
                {
                    if (ExplorerTabUtilityService.Instance.IsWindowRedirecting(expHwnd))
                        continue;

                    if (Win32Helper.IsWindow(expHwnd) && !Win32Helper.IsIconic(expHwnd))
                    {
                        UpdateOrAttachChildBar(expHwnd);
                    }
                }

                // Cleanup closed or destroyed windows
                List<IntPtr> closed = new List<IntPtr>();
                foreach (var kvp in _activeBars)
                {
                    if (!currentWindows.Contains(kvp.Key) || !Win32Helper.IsWindow(kvp.Key))
                    {
                        closed.Add(kvp.Key);
                    }
                }
                foreach (var c in closed)
                {
                    bool alive = Win32Helper.IsWindow(c);
                    App.Log("Detaching bar from HWND 0x" + c.ToString("X")
                        + " (alive=" + alive
                        + ", visible=" + (alive ? Win32Helper.IsWindowVisible(c).ToString() : "-")
                        + ", iconic=" + (alive ? Win32Helper.IsIconic(c).ToString() : "-") + ")");
                    DisposeBarEntry(_activeBars[c]);
                    ChildBarEntry removed;
                    _activeBars.TryRemove(c, out removed);
                }
            }
            catch (Exception ex)
            {
                App.Log("SyncExplorerChildBars error: " + ex.Message);
            }
        }

        private void UpdateOrAttachChildBar(IntPtr expHwnd)
        {
            try
            {
                Win32Helper.RECT clientRect;
                if (!Win32Helper.GetClientRect(expHwnd, out clientRect) || clientRect.Width < 100 || clientRect.Height < 100)
                    return;

                string parentClass = Win32Helper.GetWindowClassName(expHwnd);
                bool isFileDialog = (parentClass == "#32770");

                double dpiScale = Win32Helper.GetWindowDpiScale(expHwnd);
                ChildBarEntry existing;
                _activeBars.TryGetValue(expHwnd, out existing);

                int topBarHeight = 134; // Height of Tabs + Address Bar + Command Bar
                var contentWindows = new List<IntPtr>();

                if (!isFileDialog)
                {
                    // The header (title/tabs + address bar + command bar) is a XAML island hosted in a
                    // DesktopChildSiteBridge that is a direct child of the Explorer window and starts at
                    // the top of the client area. Use its BOTTOM edge in client coordinates (not its height,
                    // and not just any bridge) so the bar lines up in maximized, restored and any DPI.
                    Win32Helper.POINT origin = new Win32Helper.POINT { X = 0, Y = 0 };
                    Win32Helper.ClientToScreen(expHwnd, ref origin);
                    int minHeader = (int)Math.Round(96 * dpiScale);
                    int maxHeader = (int)Math.Round(165 * dpiScale);
                    int maxHeaderTop = (int)Math.Round(24 * dpiScale);
                    int maxWidthDiff = (int)Math.Round(16 * dpiScale);
                    int headerBottom = -1;

                    Win32Helper.EnumChildWindows(expHwnd, (ch, lParam) =>
                    {
                        string cls = Win32Helper.GetWindowClassName(ch);
                        if (cls == "Microsoft.UI.Content.DesktopChildSiteBridge")
                        {
                            Win32Helper.RECT r;
                            if (Win32Helper.GetParent(ch) == expHwnd
                                && Win32Helper.IsWindowVisible(ch)
                                && Win32Helper.GetWindowRect(ch, out r))
                            {
                                int relTop = r.Top - origin.Y;
                                int relBottom = r.Bottom - origin.Y;
                                int w = r.Right - r.Left;
                                if (relTop >= -maxHeaderTop && relTop <= maxHeaderTop
                                    && relBottom >= minHeader && relBottom <= maxHeader
                                    && Math.Abs(w - clientRect.Width) <= maxWidthDiff
                                    && relBottom > headerBottom)
                                {
                                    headerBottom = relBottom;
                                }
                            }
                        }
                        else if (cls == "ShellTabWindowClass" || cls == "TabWindowClass")
                        {
                            contentWindows.Add(ch);
                        }
                        return true;
                    }, IntPtr.Zero);

                    // Moved to a monitor with a different DPI (e.g. 100% <-> 125%):
                    // .NET 4.8 WS_CHILD HwndSource retains its creation-time WPF DPI transform, so recreate
                    // the HwndSource at the new monitor's DPI rather than keeping a scaled/clipped bar.
                    bool dpiChanged = (existing != null && Math.Abs(dpiScale - existing.LastDpi) > 0.001);
                    if (dpiChanged)
                    {
                        DisposeBarEntry(existing);
                        ChildBarEntry removed;
                        _activeBars.TryRemove(expHwnd, out removed);
                        existing = null;
                        if (headerBottom <= 0)
                        {
                            return;
                        }
                    }

                    // Do not attach to windows without a WinUI DesktopChildSiteBridge header (e.g. Control Panel),
                    // or before Explorer finishes creating its header.
                    if (existing == null && headerBottom <= 0)
                    {
                        return;
                    }

                    double logicalTop = (existing != null && existing.LastLogicalTop >= 96 && existing.LastLogicalTop <= 165)
                        ? existing.LastLogicalTop
                        : 136.0;
                    int expectedTopFromLogical = (int)Math.Round(logicalTop * dpiScale);

                    if (headerBottom > 0)
                    {
                        topBarHeight = headerBottom;
                        if (existing != null && dpiScale > 0)
                        {
                            existing.LastLogicalTop = headerBottom / dpiScale;
                        }
                    }
                    else if (expectedTopFromLogical > 0)
                    {
                        topBarHeight = expectedTopFromLogical;
                    }
                    else if (existing != null && existing.LastTop > 0)
                    {
                        topBarHeight = existing.LastTop;
                    }
                    else
                    {
                        return;
                    }

                    // Never cover the caption buttons (minimize / maximize / close)
                    int minSafeTop = (int)Math.Round(80 * dpiScale);
                    if (topBarHeight < minSafeTop)
                    {
                        if (existing != null && existing.LastTop >= minSafeTop) topBarHeight = existing.LastTop;
                        else return;
                    }
                }
                else
                {
                    // File Open / Save Dialog (#32770)
                    topBarHeight = 55;
                    Win32Helper.EnumChildWindows(expHwnd, (ch, lParam) =>
                    {
                        string cls = Win32Helper.GetWindowClassName(ch);
                        if (cls == "WorkerW" || cls == "ReBarWindow32")
                        {
                            Win32Helper.RECT r;
                            if (Win32Helper.GetWindowRect(ch, out r))
                            {
                                Win32Helper.POINT p = new Win32Helper.POINT { X = r.Left, Y = r.Bottom };
                                Win32Helper.ScreenToClient(expHwnd, ref p);
                                if (p.Y > 30 && p.Y < 80) topBarHeight = p.Y;
                            }
                        }
                        else if (cls == "DUIViewWndClassName")
                        {
                            contentWindows.Add(ch);
                        }
                        return true;
                    }, IntPtr.Zero);
                }

                int settingBarHeight = BookmarkManager.Instance.Settings.BarHeight;
                int physicalBarHeight = (int)Math.Round(settingBarHeight * dpiScale);
                int clientWidth = clientRect.Width;
                int clientHeight = clientRect.Height;

                ChildBarEntry entry;
                if (!_activeBars.TryGetValue(expHwnd, out entry))
                {
                    var attachSw = Stopwatch.StartNew();

                    // Create new native WS_CHILD HwndSource inside Explorer or Dialog!
                    HwndSourceParameters parameters = new HwndSourceParameters("ExplorerBookmarkBarChild", clientWidth, physicalBarHeight)
                    {
                        WindowStyle = Win32Helper.WS_CHILD | Win32Helper.WS_VISIBLE | Win32Helper.WS_CLIPSIBLINGS | Win32Helper.WS_CLIPCHILDREN,
                        ExtendedWindowStyle = Win32Helper.WS_EX_CONTROLPARENT | Win32Helper.WS_EX_TOOLWINDOW | Win32Helper.WS_EX_NOACTIVATE,
                        ParentWindow = expHwnd,
                        PositionX = 0,
                        PositionY = topBarHeight,
                        Width = clientWidth,
                        Height = physicalBarHeight
                    };

                    HwndSource source = new HwndSource(parameters);
                    BookmarkBarControl ctrl = new BookmarkBarControl(expHwnd);
                    ctrl.HorizontalAlignment = HorizontalAlignment.Stretch;
                    ctrl.VerticalAlignment = VerticalAlignment.Stretch;
                    ctrl.Width = double.NaN;
                    ctrl.Height = double.NaN;
                    source.RootVisual = ctrl;

                    entry = new ChildBarEntry
                    {
                        ExplorerHwnd = expHwnd,
                        ChildHwnd = source.Handle,
                        Source = source,
                        Control = ctrl,
                        ShellTabHwnd = contentWindows.Count > 0 ? contentWindows[0] : IntPtr.Zero,
                        LastWidth = clientWidth,
                        LastHeight = physicalBarHeight,
                        LastClientHeight = clientHeight,
                        LastTop = topBarHeight,
                        LastZoomed = Win32Helper.IsZoomed(expHwnd),
                        LastDpi = dpiScale,
                        LastLogicalTop = dpiScale > 0 ? (topBarHeight / dpiScale) : topBarHeight
                    };

                    _activeBars[expHwnd] = entry;

                    // Pin the bar to (0, LastTop). When the window is dragged to a monitor with a
                    // different DPI, WPF/Windows re-lays out the child and can move it to (0,0) for a
                    // moment, which leaves a "ghost" copy of the bar painted over Explorer's tabs.
                    var hookedEntry = entry;
                    source.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                        ChildBarHook(hookedEntry, msg, lParam, ref handled));

                    Win32Helper.SetWindowPos(
                        source.Handle,
                        Win32Helper.HWND_TOP,
                        0,
                        topBarHeight,
                        clientWidth,
                        physicalBarHeight,
                        Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_SHOWWINDOW | Win32Helper.SWP_FRAMECHANGED
                    );

                    App.Log("Attached native child bookmark bar to HWND 0x" + expHwnd.ToString("X") + " Child HWND 0x" + source.Handle.ToString("X") + " (DPI " + dpiScale.ToString("F2") + " PhysicalHeight " + physicalBarHeight + "px) in " + attachSw.ElapsedMilliseconds + "ms");
                }
                else
                {
                    entry.ShellTabHwnd = contentWindows.Count > 0 ? contentWindows[0] : IntPtr.Zero;

                    // Maximize/restore: Explorer re-lays out its header over several frames.
                    // Keep forcing position + repaint for a short while so the bar doesn't end up
                    // stale, half-painted or misplaced.
                    bool zoomed = Win32Helper.IsZoomed(expHwnd);
                    if (zoomed != entry.LastZoomed)
                    {
                        entry.LastZoomed = zoomed;
                        entry.SettleTicks = SettleTickCount;
                    }

                    bool settling = entry.SettleTicks > 0;
                    if (settling)
                    {
                        entry.SettleTicks--;
                        if (entry.SettleTicks == 0 && entry.RepaintParentAfterSettle)
                        {
                            entry.RepaintParentAfterSettle = false;
                            Win32Helper.RedrawWindow(expHwnd, IntPtr.Zero, IntPtr.Zero,
                                Win32Helper.RDW_INVALIDATE | Win32Helper.RDW_ERASE | Win32Helper.RDW_FRAME | Win32Helper.RDW_ALLCHILDREN);
                        }
                    }

                    // Update child bar position and ensure it stays at top of Z-order
                    if (settling || entry.LastWidth != clientWidth || entry.LastHeight != physicalBarHeight || entry.LastTop != topBarHeight)
                    {
                        entry.LastWidth = clientWidth;
                        entry.LastHeight = physicalBarHeight;
                        entry.LastClientHeight = clientHeight;
                        entry.LastTop = topBarHeight;

                        uint swpFlags = Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_NOZORDER;
                        if (settling) swpFlags |= Win32Helper.SWP_NOCOPYBITS;

                        Win32Helper.SetWindowPos(
                            entry.ChildHwnd,
                            IntPtr.Zero,
                            0,
                            topBarHeight,
                            clientWidth,
                            physicalBarHeight,
                            swpFlags
                        );

                        EnsureBarOnTopNow(expHwnd);

                        // Only force full redraw during maximize/restore/DPI settle; normal drag resize repaints smoothly via WPF
                        if (settling)
                        {
                            Win32Helper.RedrawWindow(entry.ChildHwnd, IntPtr.Zero, IntPtr.Zero,
                                Win32Helper.RDW_INVALIDATE | Win32Helper.RDW_ALLCHILDREN);
                            try { if (entry.Control != null) entry.Control.InvalidateVisual(); } catch { }
                        }
                    }
                    else
                    {
                        // Only bring above Explorer content windows if another content child moved above us
                        EnsureBarOnTopNow(expHwnd);
                    }
                }

                // Push down folder content windows (ShellTabWindowClass or DUIViewWndClassName)
                int contentTop = topBarHeight + physicalBarHeight;

                if (!isFileDialog && !IsActivelyResizing(entry))
                {
                    int tabHeight = clientHeight - contentTop;
                    if (tabHeight > 50 && contentWindows.Count > 0)
                    {
                        // Compare in client coordinates (window rect differs when maximized: -8px frame)
                        Win32Helper.POINT clientOrigin = new Win32Helper.POINT { X = 0, Y = 0 };
                        Win32Helper.ClientToScreen(expHwnd, ref clientOrigin);

                        foreach (var tabHwnd in contentWindows)
                        {
                            if (tabHwnd != IntPtr.Zero && Win32Helper.IsWindow(tabHwnd))
                            {
                                Win32Helper.RECT curTabRect;
                                Win32Helper.GetWindowRect(tabHwnd, out curTabRect);

                                int curRelTop = curTabRect.Top - clientOrigin.Y;
                                int curHeight = curTabRect.Bottom - curTabRect.Top;

                                if (Math.Abs(curRelTop - contentTop) > 1 || Math.Abs(curHeight - tabHeight) > 2)
                                {
                                    Win32Helper.SetWindowPos(
                                        tabHwnd,
                                        IntPtr.Zero,
                                        0,
                                        contentTop,
                                        clientWidth,
                                        tabHeight,
                                        Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_NOZORDER | Win32Helper.SWP_NOSENDCHANGING | Win32Helper.SWP_ASYNCWINDOWPOS
                                    );
                                }
                            }
                        }
                    }
                }
                else
                {
                    // For #32770 Dialog: keep the bottom untouched!
                    if (contentWindows.Count > 0)
                    {
                        foreach (var duiHwnd in contentWindows)
                        {
                            if (duiHwnd != IntPtr.Zero && Win32Helper.IsWindow(duiHwnd))
                            {
                                Win32Helper.RECT duiRect;
                                Win32Helper.GetWindowRect(duiHwnd, out duiRect);
                                Win32Helper.POINT ptTop = new Win32Helper.POINT { X = duiRect.Left, Y = duiRect.Top };
                                Win32Helper.ScreenToClient(expHwnd, ref ptTop);
                                Win32Helper.POINT ptBottom = new Win32Helper.POINT { X = duiRect.Left, Y = duiRect.Bottom };
                                Win32Helper.ScreenToClient(expHwnd, ref ptBottom);

                                int curDuiTop = ptTop.Y;
                                int curDuiBottom = ptBottom.Y;
                                int targetDuiHeight = curDuiBottom - contentTop;

                                if (targetDuiHeight > 50 && (Math.Abs(curDuiTop - contentTop) > 1 || Math.Abs(duiRect.Height - targetDuiHeight) > 2))
                                {
                                    Win32Helper.SetWindowPos(
                                        duiHwnd,
                                        IntPtr.Zero,
                                        0,
                                        contentTop,
                                        duiRect.Width > 0 ? duiRect.Width : clientWidth,
                                        targetDuiHeight,
                                        Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_NOZORDER | Win32Helper.SWP_FRAMECHANGED | Win32Helper.SWP_NOSENDCHANGING
                                    );
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log("UpdateOrAttachChildBar error: " + ex.Message);
            }
        }

        /// <summary>
        /// Window hook on the bar's child HWND. Blocks any move that doesn't come from us (WPF/Windows
        /// move the child to (0,0) during a cross-monitor DPI change) and re-syncs right after the DPI
        /// switch instead of waiting for the next timer tick.
        /// </summary>
        private IntPtr ChildBarHook(ChildBarEntry entry, int msg, IntPtr lParam, ref bool handled)
        {
            try
            {
                if (msg == 0x0014 /* WM_ERASEBKGND */)
                {
                    // Prevent Win32 GDI from erasing the newly exposed area during drag resize before WPF presents
                    handled = true;
                    return (IntPtr)1;
                }
                if (msg == WM_DPICHANGED_BEFOREPARENT || msg == WM_DPICHANGED_AFTERPARENT)
                {
                    IntPtr targetExp = entry.ExplorerHwnd;
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                    {
                        if (!_isPaused && targetExp != IntPtr.Zero && Win32Helper.IsWindow(targetExp) && !Win32Helper.IsIconic(targetExp))
                        {
                            UpdateOrAttachChildBar(targetExp);
                        }
                    }));
                }
                else if (msg == Win32Helper.WM_WINDOWPOSCHANGING && entry.LastTop > 0 && lParam != IntPtr.Zero)
                {
                    double curDpi = Win32Helper.GetWindowDpiScale(entry.ExplorerHwnd);
                    if (curDpi > 0 && Math.Abs(curDpi - entry.LastDpi) > 0.001)
                    {
                        double logicalTop = (entry.LastLogicalTop >= 96 && entry.LastLogicalTop <= 165)
                            ? entry.LastLogicalTop
                            : 136.0;
                        entry.LastTop = (int)Math.Round(logicalTop * curDpi);
                        entry.LastDpi = curDpi;
                        entry.SettleTicks = SettleTickCount;
                        entry.RepaintParentAfterSettle = true;
                    }

                    var wp = (Win32Helper.WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(Win32Helper.WINDOWPOS));
                    bool changed = false;
                    if ((wp.flags & Win32Helper.SWP_NOMOVE) == 0 && (wp.x != 0 || wp.y != entry.LastTop))
                    {
                        wp.x = 0;
                        wp.y = entry.LastTop;
                        changed = true;
                    }
                    if ((wp.flags & Win32Helper.SWP_NOSIZE) == 0 && curDpi > 0)
                    {
                        Win32Helper.RECT rc;
                        if (Win32Helper.GetClientRect(entry.ExplorerHwnd, out rc) && rc.Width >= 100)
                        {
                            int targetH = (int)Math.Round(BookmarkManager.Instance.Settings.BarHeight * curDpi);
                            if (wp.cx != rc.Width || wp.cy != targetH)
                            {
                                wp.cx = rc.Width;
                                wp.cy = targetH;
                                changed = true;
                            }
                            entry.LastWidth = rc.Width;
                            entry.LastHeight = targetH;
                        }
                    }
                    if (entry.SettleTicks > 0 && (wp.flags & Win32Helper.SWP_NOCOPYBITS) == 0)
                    {
                        wp.flags |= Win32Helper.SWP_NOCOPYBITS;
                        changed = true;
                    }
                    if (changed)
                    {
                        Marshal.StructureToPtr(wp, lParam, false);
                    }
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        private void DisposeBarEntry(ChildBarEntry entry)
        {
            try
            {
                if (entry.ResizeEndTimer != null)
                {
                    entry.ResizeEndTimer.Stop();
                    entry.ResizeEndTimer = null;
                }
            }
            catch { }

            try
            {
                if (entry.ChildHwnd != IntPtr.Zero && Win32Helper.IsWindow(entry.ChildHwnd))
                {
                    Win32Helper.SetWindowPos(
                        entry.ChildHwnd,
                        IntPtr.Zero,
                        0, 0, 0, 0,
                        Win32Helper.SWP_HIDEWINDOW | Win32Helper.SWP_NOMOVE | Win32Helper.SWP_NOSIZE | Win32Helper.SWP_NOZORDER | Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_NOCOPYBITS
                    );
                }
            }
            catch { }

            try
            {
                if (entry.Control != null)
                {
                    entry.Control.Detach();
                }
            }
            catch { }

            try
            {
                if (entry.Source != null)
                {
                    entry.Source.RootVisual = null;
                    entry.Source.Dispose();
                }
            }
            catch { }

            try
            {
                if (entry.ExplorerHwnd != IntPtr.Zero && Win32Helper.IsWindow(entry.ExplorerHwnd))
                {
                    Win32Helper.RedrawWindow(entry.ExplorerHwnd, IntPtr.Zero, IntPtr.Zero,
                        Win32Helper.RDW_INVALIDATE | Win32Helper.RDW_ERASE | Win32Helper.RDW_FRAME | Win32Helper.RDW_ALLCHILDREN);
                }
            }
            catch { }

            entry.Control = null;
            entry.Source = null;
        }

        private void CleanupAllBars()
        {
            try
            {
                foreach (var entry in _activeBars.Values)
                {
                    // Restore ShellTabWindowClass
                    if (entry.ShellTabHwnd != IntPtr.Zero && Win32Helper.IsWindow(entry.ShellTabHwnd))
                    {
                        Win32Helper.RECT clientRect;
                        if (Win32Helper.GetClientRect(entry.ExplorerHwnd, out clientRect))
                        {
                            int topBarHeight = 134;
                            Win32Helper.SetWindowPos(
                                entry.ShellTabHwnd,
                                IntPtr.Zero,
                                0,
                                topBarHeight,
                                clientRect.Width,
                                clientRect.Height - topBarHeight,
                                Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_NOZORDER | Win32Helper.SWP_FRAMECHANGED
                            );
                        }
                    }

                    DisposeBarEntry(entry);
                }
                _activeBars.Clear();
            }
            catch { }
        }
    }
}
