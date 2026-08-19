using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ExplorerBookmarkBar.Services
{
    public class ExplorerHookEngine
    {
        private static ExplorerHookEngine _instance;
        public static ExplorerHookEngine Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ExplorerHookEngine();
                }
                return _instance;
            }
        }

        private readonly DispatcherTimer _scanTimer;
        private readonly Dictionary<IntPtr, IntPtr> _explorerTabWindows = new Dictionary<IntPtr, IntPtr>();

        public event Action<IntPtr, Win32Helper.RECT, int> ExplorerLayoutUpdated;
        public event Action<IntPtr> ExplorerClosed;

        public ExplorerHookEngine()
        {
            _scanTimer = new DispatcherTimer(DispatcherPriority.Render);
            _scanTimer.Interval = TimeSpan.FromMilliseconds(30);
            _scanTimer.Tick += (s, e) => MaintainExplorerLayouts();
        }

        public void Start()
        {
            _scanTimer.Start();
            MaintainExplorerLayouts();
        }

        public void Stop()
        {
            _scanTimer.Stop();
            RestoreAllLayouts();
        }

        private void MaintainExplorerLayouts()
        {
            try
            {
                var windows = Win32Helper.GetAllVisibleExplorerWindows();
                HashSet<IntPtr> currentHwnds = new HashSet<IntPtr>(windows);

                foreach (var expHwnd in windows)
                {
                    if (Win32Helper.IsWindow(expHwnd) && !Win32Helper.IsIconic(expHwnd))
                    {
                        ApplyPushDownToExplorer(expHwnd);
                    }
                }

                // Clean up closed windows
                List<IntPtr> dead = new List<IntPtr>();
                foreach (var kvp in _explorerTabWindows)
                {
                    if (!currentHwnds.Contains(kvp.Key) || !Win32Helper.IsWindow(kvp.Key))
                    {
                        dead.Add(kvp.Key);
                    }
                }
                foreach (var d in dead)
                {
                    _explorerTabWindows.Remove(d);
                    if (ExplorerClosed != null)
                    {
                        ExplorerClosed(d);
                    }
                }
            }
            catch (Exception ex)
            {
                App.Log("MaintainExplorerLayouts error: " + ex.Message);
            }
        }

        private void ApplyPushDownToExplorer(IntPtr expHwnd)
        {
            try
            {
                Win32Helper.RECT expScreenRect;
                if (!Win32Helper.GetWindowBounds(expHwnd, out expScreenRect) || expScreenRect.Width < 150 || expScreenRect.Height < 150)
                    return;

                Win32Helper.RECT clientRect;
                if (!Win32Helper.GetClientRect(expHwnd, out clientRect) || clientRect.Width < 100 || clientRect.Height < 100)
                    return;

                int topBarHeight = 134; // Standard Windows 11 Tabs + Address + Command Bar height
                IntPtr shellTabHwnd = IntPtr.Zero;

                // Find ShellTabWindowClass and DesktopChildSiteBridge
                Win32Helper.EnumChildWindows(expHwnd, (ch, lParam) =>
                {
                    string cls = Win32Helper.GetWindowClassName(ch);
                    if (cls == "Microsoft.UI.Content.DesktopChildSiteBridge")
                    {
                        Win32Helper.RECT r;
                        if (Win32Helper.GetWindowRect(ch, out r))
                        {
                            int h = r.Bottom - r.Top;
                            if (h > 60 && h < 200)
                            {
                                topBarHeight = h;
                            }
                        }
                    }
                    else if (cls == "ShellTabWindowClass")
                    {
                        shellTabHwnd = ch;
                    }
                    return true;
                }, IntPtr.Zero);

                if (shellTabHwnd != IntPtr.Zero && Win32Helper.IsWindow(shellTabHwnd))
                {
                    _explorerTabWindows[expHwnd] = shellTabHwnd;

                    int barHeight = BookmarkManager.Instance.Settings.BarHeight;
                    int targetTop = topBarHeight + barHeight;
                    int targetHeight = clientRect.Height - targetTop;

                    if (targetHeight > 50)
                    {
                        Win32Helper.RECT currentChildRect;
                        Win32Helper.GetWindowRect(shellTabHwnd, out currentChildRect);
                        int currentRelTop = currentChildRect.Top - expScreenRect.Top;
                        int currentHeight = currentChildRect.Bottom - currentChildRect.Top;

                        // Only call SetWindowPos if position or size actually differs
                        if (Math.Abs(currentRelTop - targetTop) > 1 || Math.Abs(currentHeight - targetHeight) > 2)
                        {
                            Win32Helper.SetWindowPos(
                                shellTabHwnd,
                                IntPtr.Zero,
                                0,
                                targetTop,
                                clientRect.Width,
                                targetHeight,
                                Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_NOZORDER | Win32Helper.SWP_FRAMECHANGED | Win32Helper.SWP_NOSENDCHANGING
                            );
                        }
                    }
                }

                if (ExplorerLayoutUpdated != null)
                {
                    ExplorerLayoutUpdated(expHwnd, expScreenRect, topBarHeight);
                }
            }
            catch (Exception ex)
            {
                App.Log("ApplyPushDown error: " + ex.Message);
            }
        }

        public void RestoreAllLayouts()
        {
            try
            {
                foreach (var kvp in _explorerTabWindows)
                {
                    IntPtr expHwnd = kvp.Key;
                    IntPtr tabHwnd = kvp.Value;
                    if (Win32Helper.IsWindow(expHwnd) && Win32Helper.IsWindow(tabHwnd))
                    {
                        Win32Helper.RECT clientRect;
                        if (Win32Helper.GetClientRect(expHwnd, out clientRect))
                        {
                            int topBarHeight = 134;
                            Win32Helper.SetWindowPos(
                                tabHwnd,
                                IntPtr.Zero,
                                0,
                                topBarHeight,
                                clientRect.Width,
                                clientRect.Height - topBarHeight,
                                Win32Helper.SWP_NOACTIVATE | Win32Helper.SWP_NOZORDER | Win32Helper.SWP_FRAMECHANGED
                            );
                        }
                    }
                }
                _explorerTabWindows.Clear();
            }
            catch { }
        }
    }
}
