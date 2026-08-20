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

            // In Windows 11 every tab in an Explorer window gets its own
            // ShellTabWindowClass child window. The active tab is the topmost
            // one in Z-order. EnumChildWindows walks the child tree in Z-order
            // (top to bottom), so the first ShellTabWindowClass we encounter
            // (ignoring our own bookmark-bar child) is the active tab.
            // Visibility alone is not enough because all tabs are WS_VISIBLE.
            IntPtr rootHwnd = Win32Helper.GetRootWindow(explorerHwnd);
            if (rootHwnd == IntPtr.Zero)
                return IntPtr.Zero;

            IntPtr activeTab = IntPtr.Zero;
            Win32Helper.EnumChildWindows(rootHwnd, (ch, lParam) =>
            {
                string cls = Win32Helper.GetWindowClassName(ch);

                // Skip our own bookmark-bar HwndSource child windows,
                // which are held on top of the Z-order
                if (cls.StartsWith("ExplorerBookmarkBar", StringComparison.OrdinalIgnoreCase))
                    return true;

                if (cls == "ShellTabWindowClass" || cls == "TabWindowClass")
                {
                    int style = Win32Helper.GetWindowLong(ch, Win32Helper.GWL_STYLE);
                    if ((style & Win32Helper.WS_VISIBLE) != 0)
                    {
                        activeTab = ch;
                        return false; // topmost visible tab found = active tab
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

                // 0. When we know the exact active tab HWND, only use that
                //    COM window. Every COM entry in Shell.Application.Windows()
                //    maps to a different tab; using a fallback entry (the first
                //    visible one) is what causes the folder to change in tab 1.
                if (activeTabHwnd != IntPtr.Zero)
                {
                    for (int i = 0; i < windows.Count; i++)
                    {
                        try
                        {
                            dynamic win = windows.Item(i);
                            if (win == null) continue;

                            BrowserTabMatch info = GetTabInfoFromBrowser(win, targetHwnd);
                            if (info != null && info.TabHwnd != IntPtr.Zero && info.TabHwnd == activeTabHwnd)
                            {
                                return info;
                            }
                        }
                        catch { }
                    }

                    // No exact match. Returning an arbitrary "visible" entry here is
                    // exactly what causes the folder to change in tab 1 when the user
                    // clicks a bookmark while tab 2/3/4 is active. Fall through to
                    // NavigateViaAddressBar instead, which targets the truly active tab.
                    return null;
                }

                // Legacy path (only used when we could not determine the active tab
                // HWND, e.g. Windows 10 Explorer without tabbed ShellTabWindowClass).
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

        private static bool SetClipboardTextWithRetry(string text, int retries = 5, int delayMs = 15)
        {
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    System.Windows.Clipboard.SetText(text);
                    return true;
                }
                catch
                {
                    if (i < retries - 1)
                        System.Threading.Thread.Sleep(delayMs);
                }
            }
            return false;
        }

        public static string NormalizeSpecialPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            if (path.Equals("This PC", StringComparison.OrdinalIgnoreCase) || path.Equals("My Computer", StringComparison.OrdinalIgnoreCase))
            {
                return "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
            }
            if (path.Equals("Recycle Bin", StringComparison.OrdinalIgnoreCase))
            {
                return "::{645FF040-5081-101B-9F08-00AA002F954E}";
            }
            if (path.Equals("Network", StringComparison.OrdinalIgnoreCase))
            {
                return "::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";
            }
            if (path.Equals("Control Panel", StringComparison.OrdinalIgnoreCase))
            {
                return "::{5399E694-AE9C-458F-A337-66197F6B01AA}";
            }
            if (path.Equals("Quick Access", StringComparison.OrdinalIgnoreCase) || path.Equals("Home", StringComparison.OrdinalIgnoreCase))
            {
                return "::{679F85CB-0220-4080-B29B-55403D05AAB6}";
            }

            return path;
        }

        private static bool NavigateWithShellBrowser(IShellBrowser browser, string path)
        {
            if (browser == null || string.IsNullOrEmpty(path)) return false;

            IntPtr pidl = IntPtr.Zero;
            uint sfgao;
            if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out sfgao) == 0 && pidl != IntPtr.Zero)
            {
                try
                {
                    int hr = browser.BrowseObject(pidl, SBSP_SAMEBROWSER | SBSP_ABSOLUTE);
                    return hr == 0;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    CoTaskMemFree(pidl);
                }
            }
            return false;
        }

        public static bool NavigateViaAddressBar(IntPtr explorerHwnd, string path)
        {
            if (explorerHwnd == IntPtr.Zero || !Win32Helper.IsWindow(explorerHwnd))
                return false;

            try
            {
                // Ensure WPF button releases keyboard focus
                try
                {
                    System.Windows.Input.Keyboard.ClearFocus();
                }
                catch { }

                // Ensure Explorer window is in foreground
                Win32Helper.SetForegroundWindow(explorerHwnd);
                System.Threading.Thread.Sleep(30);

                // 1. Check if an Edit control is already visible
                IntPtr edit = FindEditControl(explorerHwnd);

                // 2. If not visible, send Alt+D shortcut to focus and select the address bar
                if (edit == IntPtr.Zero)
                {
                    Win32Helper.keybd_event(Win32Helper.VK_MENU, 0, 0, UIntPtr.Zero);
                    Win32Helper.keybd_event(Win32Helper.VK_D, 0, 0, UIntPtr.Zero);
                    Win32Helper.keybd_event(Win32Helper.VK_D, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Win32Helper.keybd_event(Win32Helper.VK_MENU, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                    System.Threading.Thread.Sleep(80);
                    edit = FindEditControl(explorerHwnd);
                }

                // 3. If Win32 Edit control exists, set text and press Enter
                if (edit != IntPtr.Zero)
                {
                    Win32Helper.SendMessage(edit, Win32Helper.WM_SETTEXT, IntPtr.Zero, path);
                    Win32Helper.PostMessage(edit, Win32Helper.WM_KEYDOWN, (IntPtr)Win32Helper.VK_RETURN, IntPtr.Zero);
                    Win32Helper.PostMessage(edit, Win32Helper.WM_KEYUP, (IntPtr)Win32Helper.VK_RETURN, IntPtr.Zero);
                    return true;
                }

                // 4. For modern Windows 11 XAML Island Address Bar, paste path via Ctrl+V and press Enter
                string prevClipboard = null;
                try
                {
                    if (System.Windows.Clipboard.ContainsText())
                    {
                        prevClipboard = System.Windows.Clipboard.GetText();
                    }
                    SetClipboardTextWithRetry(path);
                }
                catch { }

                Win32Helper.keybd_event(Win32Helper.VK_CONTROL, 0, 0, UIntPtr.Zero);
                Win32Helper.keybd_event(Win32Helper.VK_V, 0, 0, UIntPtr.Zero);
                Win32Helper.keybd_event(Win32Helper.VK_V, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                Win32Helper.keybd_event(Win32Helper.VK_CONTROL, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                System.Threading.Thread.Sleep(50);

                Win32Helper.keybd_event((byte)Win32Helper.VK_RETURN, 0, 0, UIntPtr.Zero);
                Win32Helper.keybd_event((byte)Win32Helper.VK_RETURN, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);

                // Restore previous clipboard content
                if (prevClipboard != null)
                {
                    var timer = new System.Windows.Threading.DispatcherTimer();
                    timer.Interval = TimeSpan.FromMilliseconds(600);
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        try { SetClipboardTextWithRetry(prevClipboard); } catch { }
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

            path = NormalizeSpecialPath(path);

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

            path = NormalizeSpecialPath(path);

            // 1. Primary Strategy: COM IShellBrowser BrowseObject on Active Tab
            // (Instant, native Shell API, 100% reliable on 1st click, no focus issues, no clipboard touch, no key simulation)
            try
            {
                var match = FindActiveBrowserTab(targetHwnd);
                if (match != null && match.Browser != null)
                {
                    if (NavigateWithShellBrowser(match.Browser, path))
                    {
                        return true;
                    }
                }
            }
            catch { }

            // 2. Secondary Strategy: COM win.Navigate2 (Clean in-process COM call)
            try
            {
                var match = FindActiveBrowserTab(targetHwnd);
                if (match != null && match.Win != null)
                {
                    match.Win.Navigate2(path);
                    return true;
                }
            }
            catch { }

            // 3. Tertiary Strategy: Navigate via Address Bar (only when COM is unavailable)
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

            // 4. Fallback: Open using process start
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

            path = NormalizeSpecialPath(path);

            if (targetHwnd == IntPtr.Zero || !Win32Helper.IsWindow(targetHwnd))
            {
                targetHwnd = Win32Helper.GetForegroundWindow();
                IntPtr root;
                if (!Win32Helper.IsExplorerWindow(targetHwnd, out root))
                {
                    targetHwnd = Win32Helper.FindVisibleExplorerWindow();
                }
                else
                {
                    targetHwnd = root;
                }
            }

            if (targetHwnd == IntPtr.Zero || !Win32Helper.IsWindow(targetHwnd))
            {
                OpenInNewWindow(path);
                return;
            }

            // Check if this Explorer window supports tabs (ShellTabWindowClass)
            IntPtr initialActiveTab = GetActiveShellTabHwnd(targetHwnd);
            if (initialActiveTab == IntPtr.Zero)
            {
                // Classic / non-tabbed Explorer (e.g. Windows 10) -> open in new window
                OpenInNewWindow(path);
                return;
            }

            try
            {
                // Ensure WPF button releases keyboard focus
                try { System.Windows.Input.Keyboard.ClearFocus(); } catch { }

                Win32Helper.SetForegroundWindow(targetHwnd);
                System.Threading.Thread.Sleep(30);

                // Send Ctrl+T to create a new tab in Windows 11 File Explorer
                const byte VK_CONTROL = 0x11;
                const byte VK_T = 0x54;
                const uint KEYEVENTF_KEYUP = 0x0002;

                Win32Helper.keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                Win32Helper.keybd_event(VK_T, 0, 0, UIntPtr.Zero);
                Win32Helper.keybd_event(VK_T, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                Win32Helper.keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

                // In Windows 11, tab creation is asynchronous (takes 50-250ms).
                // We poll every 35ms to detect when the new tab is active and its COM object is ready,
                // then navigate via IShellBrowser.BrowseObject (instant, silent, zero address bar typing).
                int attempts = 0;
                const int maxAttempts = 35; // ~1.2s timeout
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(35);
                timer.Tick += (s, e) =>
                {
                    attempts++;
                    try
                    {
                        IntPtr currentActiveTab = GetActiveShellTabHwnd(targetHwnd);
                        bool tabChanged = (currentActiveTab != IntPtr.Zero && currentActiveTab != initialActiveTab);

                        // If new tab is active OR we have waited > 300ms (attempts >= 9)
                        if (tabChanged || attempts >= 9)
                        {
                            var match = FindActiveBrowserTab(targetHwnd);
                            if (match != null)
                            {
                                if (match.Browser != null && NavigateWithShellBrowser(match.Browser, path))
                                {
                                    timer.Stop();
                                    return;
                                }

                                if (match.Win != null)
                                {
                                    try
                                    {
                                        match.Win.Navigate2(path);
                                        timer.Stop();
                                        return;
                                    }
                                    catch { }
                                }
                            }
                        }

                        if (attempts >= maxAttempts)
                        {
                            timer.Stop();
                            // Fallback
                            NavigateActiveExplorer(targetHwnd, path);
                        }
                    }
                    catch
                    {
                        if (attempts >= maxAttempts)
                        {
                            timer.Stop();
                        }
                    }
                };
                timer.Start();
            }
            catch
            {
                OpenInNewWindow(path);
            }
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


