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

        public class ExplorerWindowSnapshot
        {
            public IntPtr ExplorerHwnd;
            public IntPtr TabHwnd;
            public string Path;
            public string[] SelectedNames;
            public dynamic ComWindow;
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

                                    if (match.TabHwnd == IntPtr.Zero)
                                    {
                                        IntPtr browserHwnd;
                                        if (sb.GetWindow(out browserHwnd) == 0 && browserHwnd != IntPtr.Zero)
                                        {
                                            string bCls = Win32Helper.GetWindowClassName(browserHwnd);
                                            if (bCls == "ShellTabWindowClass" || bCls == "TabWindowClass")
                                            {
                                                match.TabHwnd = browserHwnd;
                                            }
                                            else
                                            {
                                                match.TabHwnd = GetShellTabAncestor(browserHwnd, targetHwnd);
                                            }
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

        private static string ExtractPathFromMatch(BrowserTabMatch match)
        {
            if (match == null) return string.Empty;

            if (!string.IsNullOrEmpty(match.ShellItemPath))
            {
                return match.ShellItemPath;
            }

            dynamic win = match.Win;
            if (win == null) return string.Empty;

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

            return string.Empty;
        }

        private static string[] ExtractSelectedItemNames(dynamic win)
        {
            if (win == null) return null;
            try
            {
                dynamic doc = win.Document;
                if (doc == null) return null;
                dynamic selected = doc.SelectedItems();
                if (selected == null) return null;
                int count = (int)selected.Count;
                if (count <= 0) return null;

                string[] names = new string[count];
                for (int i = 0; i < count; i++)
                {
                    names[i] = (string)selected.Item(i).Name;
                }
                return names;
            }
            catch { return null; }
        }

        private static void TrySelectItems(dynamic win, string[] names)
        {
            if (win == null || names == null || names.Length == 0) return;
            try
            {
                dynamic doc = win.Document;
                if (doc == null || doc.Folder == null) return;
                for (int i = 0; i < names.Length; i++)
                {
                    string name = names[i];
                    if (string.IsNullOrEmpty(name)) continue;
                    object item = doc.Folder.ParseName(name);
                    if (item != null)
                    {
                        // 1 = SVSI_SELECT
                        doc.SelectItem(ref item, 1);
                    }
                }
            }
            catch { }
        }

        public static ExplorerWindowSnapshot TryGetExplorerWindowSnapshot(IntPtr explorerHwnd)
        {
            if (explorerHwnd == IntPtr.Zero || !Win32Helper.IsWindow(explorerHwnd))
                return null;

            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic windows = shell.Windows();
                if (windows == null || windows.Count == 0) return null;

                long targetId = explorerHwnd.ToInt64();
                for (int i = windows.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        dynamic win = windows.Item(i);
                        if (win == null) continue;

                        long winHwnd = 0;
                        try { winHwnd = (long)win.HWND; } catch { }

                        BrowserTabMatch info = GetTabInfoFromBrowser(win, explorerHwnd);
                        bool matchesWindow = (winHwnd == targetId)
                            || (info != null && info.TabHwnd != IntPtr.Zero && Win32Helper.GetRootWindow(info.TabHwnd) == explorerHwnd);

                        if (!matchesWindow || info == null) continue;

                        string path = ExtractPathFromMatch(info);
                        if (!string.IsNullOrEmpty(path))
                        {
                            return new ExplorerWindowSnapshot
                            {
                                ExplorerHwnd = explorerHwnd,
                                TabHwnd = info.TabHwnd,
                                Path = path,
                                SelectedNames = ExtractSelectedItemNames(win),
                                ComWindow = win
                            };
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        private static BrowserTabMatch FindBrowserTabByHwnd(IntPtr explorerHwnd, IntPtr tabHwnd)
        {
            if (explorerHwnd == IntPtr.Zero || tabHwnd == IntPtr.Zero) return null;

            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic windows = shell.Windows();
                if (windows == null || windows.Count == 0) return null;

                for (int i = windows.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        dynamic win = windows.Item(i);
                        if (win == null) continue;

                        BrowserTabMatch info = GetTabInfoFromBrowser(win, explorerHwnd);
                        if (info != null && info.TabHwnd == tabHwnd)
                        {
                            return info;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        public static bool NavigateTabByHwnd(IntPtr explorerHwnd, IntPtr tabHwnd, string path, string[] selectedNames = null)
        {
            if (string.IsNullOrEmpty(path)) return false;

            string normalized = NormalizeSpecialPath(path);
            BrowserTabMatch match = (tabHwnd != IntPtr.Zero)
                ? FindBrowserTabByHwnd(explorerHwnd, tabHwnd)
                : FindActiveBrowserTab(explorerHwnd);

            if (match == null) return false;

            bool navigated = false;
            if (match.Browser != null)
            {
                navigated = NavigateWithShellBrowser(match.Browser, normalized);
            }

            if (!navigated && match.Win != null)
            {
                try
                {
                    match.Win.Navigate2(normalized);
                    navigated = true;
                }
                catch { }
            }

            if (navigated && selectedNames != null && selectedNames.Length > 0 && match.Win != null)
            {
                var selectTimer = new System.Windows.Threading.DispatcherTimer();
                selectTimer.Interval = TimeSpan.FromMilliseconds(220);
                selectTimer.Tick += (s, e) =>
                {
                    selectTimer.Stop();
                    TrySelectItems(match.Win, selectedNames);
                };
                selectTimer.Start();
            }

            return navigated;
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
                    return ExtractPathFromMatch(match);
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
                IntPtr editHwnd = FindFileNameEdit(dialogHwnd);
                if (editHwnd == IntPtr.Zero) return false;

                // Remember what was in the File name box (e.g. a suggested save name) so it can be restored.
                // A leftover rooted path (from an earlier failed attempt) is not worth restoring.
                string originalText = Win32Helper.GetControlText(editHwnd);
                if (LooksLikePath(originalText)) originalText = string.Empty;

                Win32Helper.SetForegroundWindow(dialogHwnd);
                Win32Helper.SendMessage(editHwnd, Win32Helper.WM_SETTEXT, IntPtr.Zero, path);

                // Trigger the dialog's own OK command directly instead of simulating Enter.
                // A posted Enter keystroke is unreliable: it can be swallowed by the autocomplete
                // dropdown or ignored when keyboard focus sits on the bookmark bar, which leaves
                // the path sitting in the File name box without navigating.
                SendDialogOk(dialogHwnd);

                // Verify: when the dialog navigates into a folder it replaces the File name text.
                // If the path is still there, retry once, then restore the original text.
                int attempt = 0;
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(350);
                timer.Tick += (s, e) =>
                {
                    attempt++;
                    try
                    {
                        if (!Win32Helper.IsWindow(editHwnd)) { timer.Stop(); return; }

                        string current = Win32Helper.GetControlText(editHwnd);
                        bool stillPath = string.Equals(current, path, StringComparison.OrdinalIgnoreCase);

                        if (!stillPath)
                        {
                            timer.Stop();
                            // Navigated. If the dialog emptied the box, put back the suggested file name.
                            if (string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(originalText))
                                Win32Helper.SendMessage(editHwnd, Win32Helper.WM_SETTEXT, IntPtr.Zero, originalText);
                            return;
                        }

                        if (attempt == 1)
                        {
                            // Retry with a real Enter key as a second strategy
                            Win32Helper.SetForegroundWindow(dialogHwnd);
                            SendDialogOk(dialogHwnd);
                            Win32Helper.PostMessage(editHwnd, Win32Helper.WM_KEYDOWN, (IntPtr)Win32Helper.VK_RETURN, IntPtr.Zero);
                            Win32Helper.PostMessage(editHwnd, Win32Helper.WM_KEYUP, (IntPtr)Win32Helper.VK_RETURN, IntPtr.Zero);
                        }
                        else
                        {
                            // Give up cleanly: don't leave the raw path in the File name box
                            timer.Stop();
                            Win32Helper.SendMessage(editHwnd, Win32Helper.WM_SETTEXT, IntPtr.Zero, originalText ?? string.Empty);
                        }
                    }
                    catch { timer.Stop(); }
                };
                timer.Start();

                return true;
            }
            catch { }

            return false;
        }

        private static bool LooksLikePath(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            return text.StartsWith("::", StringComparison.Ordinal)
                || text.StartsWith("\\\\", StringComparison.Ordinal)
                || (text.Length >= 2 && text[1] == ':');
        }

        private static void SendDialogOk(IntPtr dialogHwnd)
        {
            IntPtr okBtn = Win32Helper.GetDlgItem(dialogHwnd, Win32Helper.IDOK);
            IntPtr wParam = (IntPtr)((Win32Helper.BN_CLICKED << 16) | (Win32Helper.IDOK & 0xFFFF));
            Win32Helper.PostMessage(dialogHwnd, Win32Helper.WM_COMMAND, wParam, okBtn);
        }

        /// <summary>
        /// Finds the "File name" Edit box of a common Open/Save dialog. It lives inside the
        /// combo box with control ID 1148 (cmb13), or is the Edit with ID 1152 (edt1) on older layouts.
        /// Falls back to the first visible Edit that is not part of the address bar.
        /// </summary>
        private static IntPtr FindFileNameEdit(IntPtr dialogHwnd)
        {
            const int CMB13 = 1148;
            const int EDT1 = 1152;
            IntPtr exact = IntPtr.Zero;
            IntPtr fallback = IntPtr.Zero;

            Win32Helper.EnumChildWindows(dialogHwnd, (ch, lParam) =>
            {
                if (Win32Helper.GetWindowClassName(ch) != "Edit" || !Win32Helper.IsWindowVisible(ch))
                    return true;

                int id = Win32Helper.GetDlgCtrlID(ch);
                IntPtr p1 = Win32Helper.GetParent(ch);
                IntPtr p2 = p1 != IntPtr.Zero ? Win32Helper.GetParent(p1) : IntPtr.Zero;

                if (id == EDT1 || id == CMB13
                    || (p1 != IntPtr.Zero && Win32Helper.GetDlgCtrlID(p1) == CMB13)
                    || (p2 != IntPtr.Zero && Win32Helper.GetDlgCtrlID(p2) == CMB13))
                {
                    exact = ch;
                    return false;
                }

                if (fallback == IntPtr.Zero)
                {
                    // Skip the address bar's edit (lives under "Address Band Root" / breadcrumb)
                    bool inAddressBar = false;
                    IntPtr p = p1;
                    for (int i = 0; i < 6 && p != IntPtr.Zero && p != dialogHwnd; i++)
                    {
                        string pc = Win32Helper.GetWindowClassName(p);
                        if (pc == "Address Band Root" || pc == "Breadcrumb Parent") { inAddressBar = true; break; }
                        p = Win32Helper.GetParent(p);
                    }
                    if (!inAddressBar) fallback = ch;
                }
                return true;
            }, IntPtr.Zero);

            return exact != IntPtr.Zero ? exact : fallback;
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

        private static string NormalizePathForComparison(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string p = path.Trim().TrimEnd('\\', '/');
            if (p.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring(6); // keep "::{GUID}"
            }
            return p;
        }

        private static bool PathsRoughlyEqual(string a, string b)
        {
            string na = NormalizePathForComparison(a);
            string nb = NormalizePathForComparison(b);
            if (string.Equals(na, nb, StringComparison.OrdinalIgnoreCase)) return true;

            // Home / Quick Access has two CLSIDs across Windows 11 builds
            bool aHome = na.IndexOf("F874310E-B6B7-47DC-BC84-B9E6B38F5903", StringComparison.OrdinalIgnoreCase) >= 0
                || na.IndexOf("679F85CB-0220-4080-B29B-55403D05AAB6", StringComparison.OrdinalIgnoreCase) >= 0;
            bool bHome = nb.IndexOf("F874310E-B6B7-47DC-BC84-B9E6B38F5903", StringComparison.OrdinalIgnoreCase) >= 0
                || nb.IndexOf("679F85CB-0220-4080-B29B-55403D05AAB6", StringComparison.OrdinalIgnoreCase) >= 0;
            return aHome && bHome;
        }

        public static void OpenInNewTab(IntPtr targetHwnd, string path, string[] selectedNames = null)
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

            var existingTabs = Win32Helper.GetExplorerTabs(targetHwnd);
            if (existingTabs.Count == 0)
            {
                // Classic / non-tabbed Explorer (e.g. Windows 10) -> open in new window
                OpenInNewWindow(path);
                return;
            }

            try
            {
                // Ensure WPF button releases keyboard focus
                try { System.Windows.Input.Keyboard.ClearFocus(); } catch { }

                Win32Helper.RestoreWindowToForeground(targetHwnd);

                // Trigger Explorer's native Ctrl+T command (0xA21B) directly on ShellTabWindowClass
                Win32Helper.PostMessage(existingTabs[0], Win32Helper.WM_COMMAND, (IntPtr)0xA21B, IntPtr.Zero);

                var existingSet = new System.Collections.Generic.HashSet<IntPtr>(existingTabs);
                IntPtr capturedNewTabHwnd = IntPtr.Zero;
                int tabAppearAttempt = -1;
                int attempts = 0;
                const int maxAttempts = 72; // ~1.8s at 25ms interval
                bool fallbackCtrlTSent = false;

                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(25);
                timer.Tick += (s, e) =>
                {
                    attempts++;
                    try
                    {
                        if (!Win32Helper.IsWindow(targetHwnd))
                        {
                            timer.Stop();
                            return;
                        }

                        if (capturedNewTabHwnd == IntPtr.Zero)
                        {
                            var currentTabs = Win32Helper.GetExplorerTabs(targetHwnd);
                            for (int i = 0; i < currentTabs.Count; i++)
                            {
                                if (!existingSet.Contains(currentTabs[i]))
                                {
                                    capturedNewTabHwnd = currentTabs[i];
                                    tabAppearAttempt = attempts;
                                    break;
                                }
                            }

                            // If 0xA21B did not spawn a tab within ~400ms, retry once with Ctrl+T
                            if (capturedNewTabHwnd == IntPtr.Zero && !fallbackCtrlTSent && attempts >= 16)
                            {
                                fallbackCtrlTSent = true;
                                App.Log("OpenInNewTab: 0xA21B did not create a tab within 400ms; sending Ctrl+T fallback.");
                                Win32Helper.RestoreWindowToForeground(targetHwnd);
                                Win32Helper.keybd_event(Win32Helper.VK_CONTROL, 0, 0, UIntPtr.Zero);
                                Win32Helper.keybd_event(0x54 /* VK_T */, 0, 0, UIntPtr.Zero);
                                Win32Helper.keybd_event(0x54 /* VK_T */, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                                Win32Helper.keybd_event(Win32Helper.VK_CONTROL, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                            }
                        }

                        if (capturedNewTabHwnd != IntPtr.Zero)
                        {
                            var tabMatch = FindBrowserTabByHwnd(targetHwnd, capturedNewTabHwnd);
                            string readyPath = tabMatch != null ? ExtractPathFromMatch(tabMatch) : string.Empty;
                            bool waitedEnough = (attempts - tabAppearAttempt) >= 8; // ~200ms settle for default tab nav

                            if (tabMatch != null && (!string.IsNullOrEmpty(readyPath) || waitedEnough))
                            {
                                if (NavigateTabByHwnd(targetHwnd, capturedNewTabHwnd, path, selectedNames))
                                {
                                    timer.Stop();
                                    Win32Helper.RestoreWindowToForeground(targetHwnd);

                                    // Re-verify ~180ms later so Explorer's default new-tab page does not overwrite target path
                                    var verifyTimer = new System.Windows.Threading.DispatcherTimer();
                                    verifyTimer.Interval = TimeSpan.FromMilliseconds(180);
                                    verifyTimer.Tick += (vs, ve) =>
                                    {
                                        verifyTimer.Stop();
                                        try
                                        {
                                            if (!Win32Helper.IsWindow(targetHwnd) || !Win32Helper.IsWindow(capturedNewTabHwnd)) return;
                                            var checkMatch = FindBrowserTabByHwnd(targetHwnd, capturedNewTabHwnd);
                                            string currentPath = checkMatch != null ? ExtractPathFromMatch(checkMatch) : string.Empty;
                                            if (!PathsRoughlyEqual(currentPath, path))
                                            {
                                                NavigateTabByHwnd(targetHwnd, capturedNewTabHwnd, path, selectedNames);
                                            }
                                            Win32Helper.RestoreWindowToForeground(targetHwnd);
                                        }
                                        catch { }
                                    };
                                    verifyTimer.Start();
                                    return;
                                }
                            }
                        }

                        if (attempts >= maxAttempts)
                        {
                            timer.Stop();
                            if (capturedNewTabHwnd != IntPtr.Zero)
                            {
                                NavigateTabByHwnd(targetHwnd, capturedNewTabHwnd, path, selectedNames);
                            }
                            else
                            {
                                NavigateActiveExplorer(targetHwnd, path);
                            }
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


