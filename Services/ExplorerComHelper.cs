using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ExplorerBookmarkBar.Services
{
    public static class ExplorerComHelper
    {
        [ComImport]
        [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            [PreserveSig]
            int QueryService([In] ref Guid guidService, [In] ref Guid riid, [Out] out IntPtr ppvObject);
        }

        [ComImport]
        [Guid("000214E2-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            [PreserveSig]
            int GetWindow(out IntPtr phwnd);
            [PreserveSig]
            int ContextSensitiveHelp(bool fEnterMode);
            [PreserveSig]
            int InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
            [PreserveSig]
            int SetMenuSB(IntPtr hmenuShared, IntPtr holemenu, IntPtr hwndActiveObject);
            [PreserveSig]
            int RemoveMenusSB(IntPtr hmenuShared);
            [PreserveSig]
            int SetStatusTextSB(IntPtr pszStatusText);
            [PreserveSig]
            int EnableModelessSB(bool fEnable);
            [PreserveSig]
            int TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
            [PreserveSig]
            int BrowseObject(IntPtr pidl, uint wFlags);
            [PreserveSig]
            int GetViewStateStream(uint grfMode, out IntPtr ppStrm);
            [PreserveSig]
            int GetControlWindow(uint id, out IntPtr phwnd);
            [PreserveSig]
            int SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr pret);
            [PreserveSig]
            int QueryActiveShellView(out IntPtr ppshv);
        }

        [ComImport]
        [Guid("000214E3-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellView
        {
            [PreserveSig]
            int GetWindow(out IntPtr phwnd);
        }

        [ComImport]
        [Guid("cde725b0-3009-11d0-9940-00c04fa309f4")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFolderView
        {
            [PreserveSig]
            int GetCurrentViewMode(out uint pViewMode);
            [PreserveSig]
            int SetCurrentViewMode(uint ViewMode);
            [PreserveSig]
            int GetFolder([In] ref Guid riid, [Out] out IntPtr ppv);
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            [PreserveSig]
            int BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, out IntPtr ppv);
            [PreserveSig]
            int GetParent(out IShellItem ppsi);
            [PreserveSig]
            int GetDisplayName(uint sigdnName, out IntPtr ppszName);
            [PreserveSig]
            int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            [PreserveSig]
            int Compare(IShellItem psi, uint hint, out int piOrder);
        }

        private static readonly Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid SID_SShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
        private static readonly Guid IID_IShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
        private static readonly Guid IID_IShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");

        private const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;
        private const uint SBSP_SAMEBROWSER = 0x0001;
        private const uint SBSP_ABSOLUTE = 0x0000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHParseDisplayName([MarshalAs(UnmanagedType.LPWStr)] string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        public static IntPtr GetActiveShellTabHwnd(IntPtr explorerHwnd)
        {
            if (explorerHwnd == IntPtr.Zero || !Win32Helper.IsWindow(explorerHwnd))
                return IntPtr.Zero;

            IntPtr activeTab = IntPtr.Zero;
            Win32Helper.EnumChildWindows(explorerHwnd, (ch, lParam) =>
            {
                string cls = Win32Helper.GetWindowClassName(ch);
                if (cls == "ShellTabWindowClass" || cls == "TabWindowClass")
                {
                    int style = Win32Helper.GetWindowLong(ch, Win32Helper.GWL_STYLE);
                    if ((style & Win32Helper.WS_VISIBLE) != 0)
                    {
                        activeTab = ch;
                        return false; // Found active visible tab window
                    }
                }
                return true;
            }, IntPtr.Zero);

            return activeTab;
        }

        private static IntPtr GetShellTabAncestor(IntPtr hwnd, IntPtr topLevelHwnd)
        {
            IntPtr current = hwnd;
            while (current != IntPtr.Zero && current != topLevelHwnd)
            {
                string cls = Win32Helper.GetWindowClassName(current);
                if (cls == "ShellTabWindowClass" || cls == "TabWindowClass")
                {
                    return current;
                }
                current = Win32Helper.GetParent(current);
            }
            return IntPtr.Zero;
        }

        private class BrowserTabMatch
        {
            public dynamic Win;
            public IShellBrowser Browser;
            public IntPtr TabHwnd;
            public string ShellItemPath;
        }

        private static BrowserTabMatch GetTabInfoFromBrowser(dynamic win, IntPtr targetHwnd)
        {
            if (win == null) return null;

            BrowserTabMatch match = new BrowserTabMatch { Win = win, TabHwnd = IntPtr.Zero, ShellItemPath = string.Empty };

            try
            {
                IServiceProvider sp = win as IServiceProvider;
                if (sp != null)
                {
                    IntPtr pBrowser = IntPtr.Zero;
                    Guid sId = SID_STopLevelBrowser;
                    Guid rId = IID_IShellBrowser;
                    int hr = sp.QueryService(ref sId, ref rId, out pBrowser);
                    if (hr != 0 || pBrowser == IntPtr.Zero)
                    {
                        sId = SID_SShellBrowser;
                        hr = sp.QueryService(ref sId, ref rId, out pBrowser);
                    }

                    if (pBrowser != IntPtr.Zero)
                    {
                        IShellBrowser sb = Marshal.GetObjectForIUnknown(pBrowser) as IShellBrowser;
                        match.Browser = sb;

                        if (sb != null)
                        {
                            IntPtr pView = IntPtr.Zero;
                            hr = sb.QueryActiveShellView(out pView);
                            if (hr == 0 && pView != IntPtr.Zero)
                            {
                                try
                                {
                                    IShellView sv = Marshal.GetObjectForIUnknown(pView) as IShellView;
                                    if (sv != null)
                                    {
                                        IntPtr viewHwnd;
                                        if (sv.GetWindow(out viewHwnd) == 0 && viewHwnd != IntPtr.Zero)
                                        {
                                            match.TabHwnd = GetShellTabAncestor(viewHwnd, targetHwnd);
                                        }
                                    }

                                    // Extract path from IFolderView -> IShellItem
                                    IFolderView fv = Marshal.GetObjectForIUnknown(pView) as IFolderView;
                                    if (fv != null)
                                    {
                                        IntPtr pItem = IntPtr.Zero;
                                        Guid itemGuid = IID_IShellItem;
                                        if (fv.GetFolder(ref itemGuid, out pItem) == 0 && pItem != IntPtr.Zero)
                                        {
                                            try
                                            {
                                                IShellItem item = Marshal.GetObjectForIUnknown(pItem) as IShellItem;
                                                if (item != null)
                                                {
                                                    IntPtr ppsz = IntPtr.Zero;
                                                    if (item.GetDisplayName(SIGDN_DESKTOPABSOLUTEPARSING, out ppsz) == 0 && ppsz != IntPtr.Zero)
                                                    {
                                                        try
                                                        {
                                                            match.ShellItemPath = Marshal.PtrToStringUni(ppsz);
                                                        }
                                                        finally
                                                        {
                                                            CoTaskMemFree(ppsz);
                                                        }
                                                    }
                                                }
                                            }
                                            finally
                                            {
                                                Marshal.Release(pItem);
                                            }
                                        }
                                    }
                                }
                                finally
                                {
                                    Marshal.Release(pView);
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return match;
        }

        private static BrowserTabMatch FindActiveBrowserTab(IntPtr targetHwnd)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic windows = shell.Windows();
                if (windows == null || windows.Count == 0) return null;

                IntPtr activeTabHwnd = IntPtr.Zero;
                if (targetHwnd != IntPtr.Zero)
                {
                    activeTabHwnd = GetActiveShellTabHwnd(targetHwnd);
                }
                else
                {
                    IntPtr fg = Win32Helper.GetForegroundWindow();
                    if (fg != IntPtr.Zero)
                    {
                        IntPtr root;
                        if (Win32Helper.IsExplorerWindow(fg, out root))
                        {
                            targetHwnd = root;
                            activeTabHwnd = GetActiveShellTabHwnd(targetHwnd);
                        }
                    }
                }

                BrowserTabMatch targetHwndFallback = null;
                BrowserTabMatch anyVisibleFallback = null;
                BrowserTabMatch firstFallback = null;

                for (int i = 0; i < windows.Count; i++)
                {
                    try
                    {
                        dynamic win = windows.Item(i);
                        if (win == null) continue;

                        BrowserTabMatch info = GetTabInfoFromBrowser(win, targetHwnd);
                        if (info == null) continue;

                        if (firstFallback == null) firstFallback = info;

                        long winHwnd = 0;
                        try { winHwnd = (long)win.HWND; } catch { }

                        // 1. Exact match with the currently active ShellTabWindowClass
                        if (activeTabHwnd != IntPtr.Zero && info.TabHwnd != IntPtr.Zero && info.TabHwnd == activeTabHwnd)
                        {
                            return info;
                        }

                        // 2. Check if this COM window belongs to the target Explorer window
                        bool matchesTarget = (targetHwnd == IntPtr.Zero)
                            || (winHwnd != 0 && winHwnd == targetHwnd.ToInt64())
                            || (info.TabHwnd != IntPtr.Zero && Win32Helper.GetRootWindow(info.TabHwnd) == targetHwnd);

                        if (matchesTarget)
                        {
                            if (info.TabHwnd != IntPtr.Zero)
                            {
                                int style = Win32Helper.GetWindowLong(info.TabHwnd, Win32Helper.GWL_STYLE);
                                if ((style & Win32Helper.WS_VISIBLE) != 0)
                                {
                                    return info;
                                }
                            }

                            if (targetHwndFallback == null)
                            {
                                targetHwndFallback = info;
                            }
                        }

                        // 3. Save any visible tab as general fallback
                        if (info.TabHwnd != IntPtr.Zero && anyVisibleFallback == null)
                        {
                            int style = Win32Helper.GetWindowLong(info.TabHwnd, Win32Helper.GWL_STYLE);
                            if ((style & Win32Helper.WS_VISIBLE) != 0)
                            {
                                anyVisibleFallback = info;
                            }
                        }
                    }
                    catch { }
                }

                if (targetHwndFallback != null) return targetHwndFallback;
                if (anyVisibleFallback != null) return anyVisibleFallback;
                return firstFallback;
            }
            catch { return null; }
        }

        public static dynamic GetActiveBrowserObject(IntPtr targetHwnd)
        {
            var match = FindActiveBrowserTab(targetHwnd);
            return match != null ? match.Win : null;
        }

        public static string GetFileDialogCurrentPath(IntPtr dialogHwnd)
        {
            try
            {
                string detected = string.Empty;
                Win32Helper.EnumChildWindows(dialogHwnd, (ch, lParam) =>
                {
                    string cls = Win32Helper.GetWindowClassName(ch);
                    if (cls == "ToolbarWindow32" || cls == "Address Band Root" || cls == "Edit")
                    {
                        string txt = Win32Helper.GetWindowText(ch);
                        if (!string.IsNullOrEmpty(txt))
                        {
                            if (txt.StartsWith("Address: ", StringComparison.OrdinalIgnoreCase))
                            {
                                detected = txt.Substring(9).Trim();
                                return false;
                            }
                            else if (Directory.Exists(txt))
                            {
                                detected = txt.Trim();
                                return false;
                            }
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                return detected;
            }
            catch { return string.Empty; }
        }

        public static string GetActiveExplorerPath(IntPtr targetHwnd)
        {
            try
            {
                if (targetHwnd != IntPtr.Zero && Win32Helper.IsWindow(targetHwnd))
                {
                    string winClass = Win32Helper.GetWindowClassName(targetHwnd);
                    if (winClass == "#32770")
                    {
                        string dlgPath = GetFileDialogCurrentPath(targetHwnd);
                        if (!string.IsNullOrEmpty(dlgPath)) return dlgPath;
                    }
                }

                var match = FindActiveBrowserTab(targetHwnd);
                if (match != null)
                {
                    if (!string.IsNullOrEmpty(match.ShellItemPath))
                    {
                        return match.ShellItemPath;
                    }

                    dynamic win = match.Win;
                    if (win != null)
                    {
                        try
                        {
                            if (win.Document != null && win.Document.Folder != null && win.Document.Folder.Self != null)
                            {
                                string path = win.Document.Folder.Self.Path;
                                if (!string.IsNullOrEmpty(path))
                                {
                                    return path;
                                }
                            }
                        }
                        catch { }

                        try
                        {
                            string url = win.LocationURL;
                            if (!string.IsNullOrEmpty(url))
                            {
                                if (url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                                {
                                    Uri uri = new Uri(url);
                                    return uri.LocalPath;
                                }
                                return url;
                            }
                        }
                        catch { }

                        try
                        {
                            string locName = win.LocationName;
                            if (!string.IsNullOrEmpty(locName))
                            {
                                return locName;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return string.Empty;
        }

        public static IntPtr FindEditControl(IntPtr explorerHwnd)
        {
            IntPtr editHwnd = IntPtr.Zero;
            Win32Helper.EnumChildWindows(explorerHwnd, (ch, lParam) =>
            {
                string cls = Win32Helper.GetWindowClassName(ch);
                if (cls == "Edit" && Win32Helper.IsWindowVisible(ch))
                {
                    editHwnd = ch;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return editHwnd;
        }

        public static bool NavigateViaAddressBar(IntPtr explorerHwnd, string path)
        {
            if (explorerHwnd == IntPtr.Zero || !Win32Helper.IsWindow(explorerHwnd))
                return false;

            try
            {
                // Ensure Explorer window is in foreground
                Win32Helper.SetForegroundWindow(explorerHwnd);
                System.Threading.Thread.Sleep(20);

                // 1. Check if an Edit control is already visible
                IntPtr edit = FindEditControl(explorerHwnd);

                // 2. If not visible, send WM_COMMAND 41504 (internal accelerator for focus address bar)
                if (edit == IntPtr.Zero)
                {
                    Win32Helper.SendMessage(explorerHwnd, Win32Helper.WM_COMMAND, (IntPtr)41504, IntPtr.Zero);
                    System.Threading.Thread.Sleep(30);
                    edit = FindEditControl(explorerHwnd);
                }

                // 3. If still not visible, send Ctrl+L shortcut
                if (edit == IntPtr.Zero)
                {
                    Win32Helper.keybd_event(Win32Helper.VK_CONTROL, 0, 0, UIntPtr.Zero);
                    Win32Helper.keybd_event(Win32Helper.VK_L, 0, 0, UIntPtr.Zero);
                    Win32Helper.keybd_event(Win32Helper.VK_L, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Win32Helper.keybd_event(Win32Helper.VK_CONTROL, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                    System.Threading.Thread.Sleep(40);
                    edit = FindEditControl(explorerHwnd);
                }

                // 4. If Win32 Edit control exists, set text and press Enter
                if (edit != IntPtr.Zero)
                {
                    Win32Helper.SendMessage(edit, Win32Helper.WM_SETTEXT, IntPtr.Zero, path);
                    Win32Helper.PostMessage(edit, Win32Helper.WM_KEYDOWN, (IntPtr)Win32Helper.VK_RETURN, IntPtr.Zero);
                    Win32Helper.PostMessage(edit, Win32Helper.WM_KEYUP, (IntPtr)Win32Helper.VK_RETURN, IntPtr.Zero);
                    return true;
                }

                // 5. For modern Windows 11 XAML Island Address Bar, paste path via Ctrl+V and press Enter
                string prevClipboard = null;
                try
                {
                    if (System.Windows.Clipboard.ContainsText())
                    {
                        prevClipboard = System.Windows.Clipboard.GetText();
                    }
                    System.Windows.Clipboard.SetText(path);
                }
                catch { }

                Win32Helper.keybd_event(Win32Helper.VK_CONTROL, 0, 0, UIntPtr.Zero);
                Win32Helper.keybd_event(Win32Helper.VK_V, 0, 0, UIntPtr.Zero);
                Win32Helper.keybd_event(Win32Helper.VK_V, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                Win32Helper.keybd_event(Win32Helper.VK_CONTROL, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                System.Threading.Thread.Sleep(20);

                Win32Helper.keybd_event((byte)Win32Helper.VK_RETURN, 0, 0, UIntPtr.Zero);
                Win32Helper.keybd_event((byte)Win32Helper.VK_RETURN, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);

                // Restore previous clipboard content
                if (prevClipboard != null)
                {
                    var timer = new System.Windows.Threading.DispatcherTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(500);
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        try { System.Windows.Clipboard.SetText(prevClipboard); } catch { }
                    };
                    timer.Start();
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool NavigateFileDialog(IntPtr dialogHwnd, string path)
        {
            if (string.IsNullOrEmpty(path) || dialogHwnd == IntPtr.Zero) return false;

            // Handle special paths
            if (path.Equals("This PC", StringComparison.OrdinalIgnoreCase) || path.Equals("My Computer", StringComparison.OrdinalIgnoreCase))
            {
                path = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
            }

            try
            {
                // Find the File Name Edit control inside the #32770 dialog
                IntPtr editHwnd = IntPtr.Zero;

                Win32Helper.EnumChildWindows(dialogHwnd, (ch, lParam) =>
                {
                    string cls = Win32Helper.GetWindowClassName(ch);
                    if (cls == "Edit")
                    {
                        if (Win32Helper.IsWindowVisible(ch))
                        {
                            editHwnd = ch;
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                if (editHwnd != IntPtr.Zero)
                {
                    // Focus dialog and enter path into edit box + Enter
                    Win32Helper.SetForegroundWindow(dialogHwnd);
                    Win32Helper.SendMessage(editHwnd, Win32Helper.WM_SETTEXT, IntPtr.Zero, path);
                    Win32Helper.PostMessage(editHwnd, Win32Helper.WM_KEYDOWN, (IntPtr)Win32Helper.VK_RETURN, IntPtr.Zero);
                    Win32Helper.PostMessage(editHwnd, Win32Helper.WM_KEYUP, (IntPtr)Win32Helper.VK_RETURN, IntPtr.Zero);
                    return true;
                }
            }
            catch { }

            return false;
        }

        public static bool NavigateActiveExplorer(IntPtr targetHwnd, string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // If target is a File Open/Save Dialog (#32770)
            if (targetHwnd != IntPtr.Zero && Win32Helper.IsWindow(targetHwnd))
            {
                string cls = Win32Helper.GetWindowClassName(targetHwnd);
                if (cls == "#32770")
                {
                    return NavigateFileDialog(targetHwnd, path);
                }
            }

            // Handle special paths
            if (path.Equals("This PC", StringComparison.OrdinalIgnoreCase) || path.Equals("My Computer", StringComparison.OrdinalIgnoreCase))
            {
                path = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
            }

            // 1. Primary Strategy: Navigate via Address Bar (guaranteed to affect ONLY the currently active tab)
            if (targetHwnd != IntPtr.Zero && Win32Helper.IsWindow(targetHwnd))
            {
                if (NavigateViaAddressBar(targetHwnd, path))
                {
                    return true;
                }
            }
            else
            {
                IntPtr fg = Win32Helper.GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    IntPtr root;
                    if (Win32Helper.IsExplorerWindow(fg, out root))
                    {
                        if (NavigateViaAddressBar(root, path))
                        {
                            return true;
                        }
                    }
                }
            }

            // 2. Secondary Strategy: COM IShellBrowser BrowseObject / Navigate2
            try
            {
                var match = FindActiveBrowserTab(targetHwnd);
                if (match != null)
                {
                    if (match.Browser != null)
                    {
                        try
                        {
                            IntPtr pidl = IntPtr.Zero;
                            uint sfgao;
                            if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out sfgao) == 0 && pidl != IntPtr.Zero)
                            {
                                try
                                {
                                    match.Browser.BrowseObject(pidl, SBSP_SAMEBROWSER | SBSP_ABSOLUTE);
                                    return true;
                                }
                                finally
                                {
                                    CoTaskMemFree(pidl);
                                }
                            }
                        }
                        catch { }
                    }

                    if (match.Win != null)
                    {
                        match.Win.Navigate2(path);
                        return true;
                    }
                }
            }
            catch { }

            // 3. Fallback: Open using process start
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + path + "\"",
                    UseShellExecute = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void OpenInNewTab(IntPtr targetHwnd, string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            // Handle special paths
            if (path.Equals("This PC", StringComparison.OrdinalIgnoreCase) || path.Equals("My Computer", StringComparison.OrdinalIgnoreCase))
            {
                path = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
            }

            if (targetHwnd == IntPtr.Zero)
            {
                targetHwnd = Win32Helper.GetForegroundWindow();
            }

            try
            {
                if (targetHwnd != IntPtr.Zero && Win32Helper.IsWindow(targetHwnd))
                {
                    // Track existing COM windows count before sending Ctrl+T
                    int prevCount = 0;
                    Type shellType = Type.GetTypeFromProgID("Shell.Application");
                    if (shellType != null)
                    {
                        try
                        {
                            dynamic shell = Activator.CreateInstance(shellType);
                            dynamic windows = shell.Windows();
                            prevCount = windows.Count;
                        }
                        catch { }
                    }

                    Win32Helper.SetForegroundWindow(targetHwnd);

                    // Send Ctrl+T to create a new tab in Windows 11 File Explorer
                    const byte VK_CONTROL = 0x11;
                    const byte VK_T = 0x54;
                    const uint KEYEVENTF_KEYUP = 0x0002;

                    Win32Helper.keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                    Win32Helper.keybd_event(VK_T, 0, 0, UIntPtr.Zero);
                    Win32Helper.keybd_event(VK_T, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Win32Helper.keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                    // Wait for new tab COM instance to be created and navigate ONLY the newly added tab
                    var timer = new System.Windows.Threading.DispatcherTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(200);
                    int attempts = 0;

                    timer.Tick += (s, e) =>
                    {
                        attempts++;
                        try
                        {
                            if (shellType != null)
                            {
                                dynamic shell = Activator.CreateInstance(shellType);
                                dynamic windows = shell.Windows();

                                if (windows.Count > prevCount || attempts >= 3)
                                {
                                    timer.Stop();

                                    // Try navigating the active tab first (new tab gets focused)
                                    dynamic activeWin = GetActiveBrowserObject(targetHwnd);
                                    if (activeWin != null)
                                    {
                                        activeWin.Navigate2(path);
                                        return;
                                    }

                                    // Find the newest COM entry for this Explorer HWND
                                    for (int i = windows.Count - 1; i >= 0; i--)
                                    {
                                        try
                                        {
                                            dynamic win = windows.Item(i);
                                            long hwnd = 0;
                                            try { hwnd = (long)win.HWND; } catch { }

                                            if (hwnd == targetHwnd.ToInt64() || targetHwnd == IntPtr.Zero)
                                            {
                                                win.Navigate2(path);
                                                return;
                                            }
                                        }
                                        catch { }
                                    }

                                    if (windows.Count > 0)
                                    {
                                        dynamic win = windows.Item(windows.Count - 1);
                                        win.Navigate2(path);
                                    }
                                }
                            }
                        }
                        catch
                        {
                            timer.Stop();
                        }
                    };
                    timer.Start();
                    return;
                }
            }
            catch { }

            // Fallback: Open in new window
            OpenInNewWindow(path);
        }

        public static void OpenInNewWindow(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + path + "\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}


