using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
            public int LastTop;
            public bool LastZoomed;
            public int SettleTicks;
        }

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

        private readonly Dictionary<IntPtr, ChildBarEntry> _activeBars = new Dictionary<IntPtr, ChildBarEntry>();
        private readonly DispatcherTimer _syncTimer;
        private bool _isPaused = false;

        public ExplorerChildBarManager()
        {
            _syncTimer = new DispatcherTimer(DispatcherPriority.Render);
            _syncTimer.Interval = TimeSpan.FromMilliseconds(30);
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

        private void SyncExplorerChildBars()
        {
            if (_isPaused) return;

            try
            {
                ThemeManager.CheckAndApplyTheme();

                var windows = Win32Helper.GetAllVisibleExplorerWindows();
                HashSet<IntPtr> currentWindows = new HashSet<IntPtr>(windows);

                // Attach child bar to any new Explorer windows on any monitor
                foreach (var expHwnd in windows)
                {
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
                    DisposeBarEntry(_activeBars[c]);
                    _activeBars.Remove(c);
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
                    int minHeader = (int)Math.Round(60 * dpiScale);
                    int maxHeader = (int)Math.Round(260 * dpiScale);
                    int maxHeaderTop = (int)Math.Round(24 * dpiScale);
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
                                    && w >= clientRect.Width / 2
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

                    if (headerBottom > 0)
                    {
                        topBarHeight = headerBottom;
                    }
                    else if (existing != null && existing.LastTop > 0)
                    {
                        // Header not measurable right now (e.g. mid maximize/restore animation):
                        // keep the last good position instead of jumping to a guess.
                        topBarHeight = existing.LastTop;
                    }
                    else
                    {
                        // Not attached yet and header not ready: wait for the next tick.
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
                        LastTop = topBarHeight,
                        LastZoomed = Win32Helper.IsZoomed(expHwnd)
                    };

                    _activeBars[expHwnd] = entry;

                    Win32Helper.SetWindowPos(
                        source.Handle,
                        Win32Helper.HWND_TOP,
                        0,
                        topBarHeight,
                        clientWidth,
                        physicalBarHeight,
                        Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_SHOWWINDOW | Win32Helper.SWP_FRAMECHANGED
                    );

                    App.Log("Attached native child bookmark bar to HWND 0x" + expHwnd.ToString("X") + " Child HWND 0x" + source.Handle.ToString("X") + " (DPI " + dpiScale.ToString("F2") + " PhysicalHeight " + physicalBarHeight + "px)");
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
                    if (settling) entry.SettleTicks--;

                    // Update child bar position and ensure it stays at top of Z-order
                    if (settling || entry.LastWidth != clientWidth || entry.LastHeight != physicalBarHeight || entry.LastTop != topBarHeight)
                    {
                        entry.LastWidth = clientWidth;
                        entry.LastHeight = physicalBarHeight;
                        entry.LastTop = topBarHeight;

                        Win32Helper.SetWindowPos(
                            entry.ChildHwnd,
                            Win32Helper.HWND_TOP,
                            0,
                            topBarHeight,
                            clientWidth,
                            physicalBarHeight,
                            Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_SHOWWINDOW | Win32Helper.SWP_FRAMECHANGED
                        );

                        // Repaint fully so the bar doesn't appear broken/half-drawn after resizing
                        Win32Helper.RedrawWindow(entry.ChildHwnd, IntPtr.Zero, IntPtr.Zero,
                            Win32Helper.RDW_INVALIDATE | Win32Helper.RDW_ERASE | Win32Helper.RDW_ALLCHILDREN | Win32Helper.RDW_FRAME | Win32Helper.RDW_UPDATENOW);
                        try { if (entry.Control != null) entry.Control.InvalidateVisual(); } catch { }
                    }
                    else
                    {
                        // Ensure Z-order remains on top of any newly activated content
                        Win32Helper.SetWindowPos(
                            entry.ChildHwnd,
                            Win32Helper.HWND_TOP,
                            0,
                            0,
                            0,
                            0,
                            Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_NOMOVE | Win32Helper.SWP_NOSIZE | Win32Helper.SWP_SHOWWINDOW
                        );
                    }
                }

                // Push down folder content windows (ShellTabWindowClass or DUIViewWndClassName)
                int contentTop = topBarHeight + physicalBarHeight;

                if (!isFileDialog)
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
                                        Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_NOZORDER | Win32Helper.SWP_FRAMECHANGED | Win32Helper.SWP_NOSENDCHANGING
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

        private void DisposeBarEntry(ChildBarEntry entry)
        {
            try
            {
                if (entry.Source != null)
                {
                    entry.Source.Dispose();
                }
            }
            catch { }
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
