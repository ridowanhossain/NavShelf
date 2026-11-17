using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;

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
        [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
        [InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IShellWindows
        {
            [PreserveSig]
            int get_Count(out int Count);
            [PreserveSig]
            int Item([In, MarshalAs(UnmanagedType.Struct)] object index, [Out, MarshalAs(UnmanagedType.IDispatch)] out object Folder);
            [PreserveSig]
            int _NewEnum([Out, MarshalAs(UnmanagedType.IUnknown)] out object ppunk);
            [PreserveSig]
            int Register([In, MarshalAs(UnmanagedType.IDispatch)] object pid, int HWND, int swClass, out int plCookie);
            [PreserveSig]
            int RegisterPending(int lThreadId, [In] ref object pvarloc, [In] ref object pvarlocRoot, int swClass, out int plCookie);
            [PreserveSig]
            int Revoke(int lCookie);
            [PreserveSig]
            int OnNavigate(int lCookie, [In] ref object pvarLoc);
            [PreserveSig]
            int OnActivated(int lCookie, short fActive);
            [PreserveSig]
            int FindWindowSW([In] ref object pvarLoc, [In] ref object pvarLocRoot, int swClass, out int phwnd, int swfwOptions, [Out, MarshalAs(UnmanagedType.IDispatch)] out object ppdispOut);
        }

        private static readonly Guid CLSID_ShellWindows = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        private static readonly Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid SID_SShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
        private static readonly Guid IID_IShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");

        private const int SWC_DESKTOP = 0x8;
        private const int SWFO_NEEDDISPATCH = 0x1;
        private const uint SBSP_SAMEBROWSER = 0x0001;
        private const uint SBSP_ABSOLUTE = 0x0000;

        private static dynamic s_cachedDesktopDoc;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHParseDisplayName([MarshalAs(UnmanagedType.LPWStr)] string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        /// <summary>
        /// Pre-initializes the cached Desktop IShellFolderViewDual COM proxy and DLR call sites
        /// so the first Desktop double-click resolves rapidly.
        /// </summary>
        public static void WarmUpDesktopViewCache()
        {
            try
            {
                TryGetDesktopFolderPathAtClick(new IntPtr(1), 0, new Win32Helper.POINT());
            }
            catch { }
        }

        private static dynamic GetDesktopFolderViewDocument(bool forceRefresh)
        {
            if (!forceRefresh && s_cachedDesktopDoc != null)
            {
                return s_cachedDesktopDoc;
            }

            object shellWindowsObj = null;
            try
            {
                Type shellWindowsType = Type.GetTypeFromCLSID(CLSID_ShellWindows);
                if (shellWindowsType == null) return null;

                shellWindowsObj = Activator.CreateInstance(shellWindowsType);
                IShellWindows shellWindows = shellWindowsObj as IShellWindows;
                if (shellWindows == null) return null;

                object emptyLoc = null;
                object emptyRoot = null;
                int deskHwnd = 0;
                object topDisp = null;
                int hr = shellWindows.FindWindowSW(ref emptyLoc, ref emptyRoot, SWC_DESKTOP, out deskHwnd, SWFO_NEEDDISPATCH, out topDisp);
                if (hr < 0 || topDisp == null) return null;

                dynamic doc = ((dynamic)topDisp).Document;
                s_cachedDesktopDoc = doc;
                return doc;
            }
            catch
            {
                s_cachedDesktopDoc = null;
                return null;
            }
            finally
            {
                if (shellWindowsObj != null && Marshal.IsComObject(shellWindowsObj))
                {
                    try { Marshal.ReleaseComObject(shellWindowsObj); } catch { }
                }
            }
        }

        /// <summary>
        /// Resolves the target folder path (or virtual shell CLSID like This PC / Recycle Bin)
        /// for the clicked desktop item using automation-marshaled IShellFolderViewDual, or null
        /// if the item is not a redirectable folder.
        /// </summary>
        public static string TryGetDesktopFolderPathAtClick(IntPtr listViewHwnd, int itemIndex, Win32Helper.POINT screenPt)
        {
            if (listViewHwnd == IntPtr.Zero || itemIndex < 0) return null;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    dynamic doc = GetDesktopFolderViewDocument(attempt > 0);
                    if (doc == null) return null;

                    dynamic item = doc.FocusedItem;
                    if (item == null)
                    {
                        dynamic sel = doc.SelectedItems();
                        if (sel != null && (int)sel.Count == 1)
                        {
                            item = sel.Item(0);
                        }
                    }

                    return ExtractDesktopFolderItemPath(item);
                }
                catch
                {
                    s_cachedDesktopDoc = null;
                }
            }

            return null;
        }

        private static string ExtractDesktopFolderItemPath(dynamic item)
        {
            if (item == null) return null;

            bool isLink = false;
            try { isLink = (bool)item.IsLink; } catch { }

            if (isLink)
            {
                try
                {
                    dynamic link = item.GetLink;
                    if (link != null)
                    {
                        string args = null;
                        try { args = (string)link.Arguments; } catch { }
                        if (!string.IsNullOrWhiteSpace(args)) return null;

                        string linkTargetPath = null;
                        try { linkTargetPath = (string)link.Path; } catch { }
                        if (!string.IsNullOrWhiteSpace(linkTargetPath)
                            && Directory.Exists(linkTargetPath)
                            && !IsControlPanelClsidPath(linkTargetPath))
                        {
                            return linkTargetPath;
                        }

                        dynamic targetItem = null;
                        try { targetItem = link.Target; } catch { }
                        if (targetItem != null)
                        {
                            bool targetIsFolder = false;
                            string targetPath = null;
                            try { targetIsFolder = (bool)targetItem.IsFolder; } catch { }
                            try { targetPath = (string)targetItem.Path; } catch { }
                            if (targetIsFolder
                                && !string.IsNullOrWhiteSpace(targetPath)
                                && !IsControlPanelClsidPath(targetPath)
                                && (targetPath.StartsWith("::", StringComparison.Ordinal)
                                    || targetPath.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase)
                                    || Directory.Exists(targetPath)))
                            {
                                return targetPath;
                            }
                        }
                    }
                }
                catch { }
                return null;
            }

            bool isFolder = false;
            try { isFolder = (bool)item.IsFolder; } catch { }
            if (!isFolder) return null;

            string path = null;
            try { path = (string)item.Path; } catch { }
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (IsControlPanelClsidPath(path)) return null;

            if (path.StartsWith("::", StringComparison.Ordinal)
                || path.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase)
                || Directory.Exists(path))
            {
                return path;
            }

            return null;
        }

        private static bool IsControlPanelClsidPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return path.IndexOf("26EE0668", StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("5399E694", StringComparison.OrdinalIgnoreCase) >= 0;
        }

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

            return ExtractPathFromWin(match.Win);
        }

        private static string ExtractPathFromWin(dynamic win)
        {
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

                long targetId = explorerHwnd.ToInt64();
                for (int i = windows.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        dynamic win = windows.Item(i);
                        if (win == null) continue;

                        long winHwnd = 0;
                        try { winHwnd = (long)win.HWND; } catch { }
                        if (winHwnd != 0 && winHwnd != targetId) continue;

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

            return NavigateBrowserTabMatch(match, normalized, selectedNames);
        }

        private static bool NavigateBrowserTabMatch(BrowserTabMatch match, string normalizedPath, string[] selectedNames)
        {
            if (match == null || string.IsNullOrEmpty(normalizedPath)) return false;

            bool navigated = false;
            if (match.Browser != null)
            {
                navigated = NavigateWithShellBrowser(match.Browser, normalizedPath);
            }

            if (!navigated && match.Win != null)
            {
                try
                {
                    match.Win.Navigate2(normalizedPath);
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

        private static readonly HashSet<IntPtr> _claimedNewTabHwnds = new HashSet<IntPtr>();
        private static readonly HashSet<IntPtr> _claimedWinUnknowns = new HashSet<IntPtr>();

        private static IntPtr GetComUnknownPtr(object comObj)
        {
            if (comObj == null) return IntPtr.Zero;
            try
            {
                IntPtr unk = Marshal.GetIUnknownForObject(comObj);
                if (unk != IntPtr.Zero)
                {
                    Marshal.Release(unk);
                }
                return unk;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private static HashSet<IntPtr> SnapshotExistingBrowserUnknowns(IntPtr targetHwnd, out List<object> pinnedWins)
        {
            var result = new HashSet<IntPtr>();
            pinnedWins = new List<object>();
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return result;
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic windows = shell.Windows();
                if (windows == null) return result;

                long targetId = targetHwnd.ToInt64();
                int count = windows.Count;
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic win = windows.Item(i);
                        if (win == null) continue;
                        long winHwnd = 0;
                        try { winHwnd = (long)win.HWND; } catch { }
                        if (winHwnd != targetId) continue;

                        IntPtr unk = GetComUnknownPtr(win);
                        if (unk != IntPtr.Zero)
                        {
                            result.Add(unk);
                            pinnedWins.Add((object)win);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return result;
        }

        private static dynamic FindNewCreatedBrowserWindow(
            IntPtr targetHwnd,
            IntPtr capturedNewTabHwnd,
            HashSet<IntPtr> existingWinUnknowns,
            bool allowUnmatchedSingleWin,
            out IntPtr foundWinUnk)
        {
            foundWinUnk = IntPtr.Zero;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return null;
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic windows = shell.Windows();
                if (windows == null || windows.Count == 0) return null;

                long targetId = targetHwnd.ToInt64();
                dynamic fallbackWin = null;
                IntPtr fallbackUnk = IntPtr.Zero;
                int fallbackCount = 0;

                for (int i = windows.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        dynamic win = windows.Item(i);
                        if (win == null) continue;
                        long winHwnd = 0;
                        try { winHwnd = (long)win.HWND; } catch { }
                        if (winHwnd != targetId) continue;

                        IntPtr unk = GetComUnknownPtr(win);
                        if (unk == IntPtr.Zero) continue;
                        if (existingWinUnknowns.Contains(unk) || _claimedWinUnknowns.Contains(unk)) continue;

                        if (capturedNewTabHwnd != IntPtr.Zero)
                        {
                            BrowserTabMatch info = GetTabInfoFromBrowser(win, targetHwnd);
                            if (info != null && info.TabHwnd == capturedNewTabHwnd)
                            {
                                foundWinUnk = unk;
                                return win;
                            }
                        }

                        if (fallbackWin == null)
                        {
                            fallbackWin = win;
                            fallbackUnk = unk;
                        }
                        fallbackCount++;
                    }
                    catch { }
                }

                // Wait for TabHwnd match normally so the new tab's IShellView is initialized before navigating.
                // Only use single unmatched new COM window after initial settle if TabHwnd still hasn't resolved.
                if (allowUnmatchedSingleWin && fallbackCount == 1 && fallbackWin != null)
                {
                    foundWinUnk = fallbackUnk;
                    return fallbackWin;
                }
            }
            catch { }
            return null;
        }

        private static bool NavigateNewTabComWindow(dynamic newWin, string normalizedPath, string[] selectedNames)
        {
            if (newWin == null || string.IsNullOrEmpty(normalizedPath)) return false;

            bool navigated = false;
            try
            {
                // Navigate the specific IWebBrowser2 tab object directly so SID_STopLevelBrowser
                // can never route navigation to the previous active tab during tab transition.
                newWin.Navigate2(normalizedPath);
                navigated = true;
            }
            catch { }

            if (!navigated)
            {
                try
                {
                    IServiceProvider sp = newWin as IServiceProvider;
                    if (sp != null)
                    {
                        IntPtr pBrowser = IntPtr.Zero;
                        Guid sId = SID_SShellBrowser;
                        Guid rId = IID_IShellBrowser;
                        if (sp.QueryService(ref sId, ref rId, out pBrowser) == 0 && pBrowser != IntPtr.Zero)
                        {
                            IShellBrowser sb = Marshal.GetObjectForIUnknown(pBrowser) as IShellBrowser;
                            if (sb != null)
                            {
                                navigated = NavigateWithShellBrowser(sb, normalizedPath);
                            }
                        }
                    }
                }
                catch { }
            }

            if (navigated && selectedNames != null && selectedNames.Length > 0)
            {
                // May run on a background thread: select items there instead of using a DispatcherTimer.
                Task.Run(() =>
                {
                    try
                    {
                        System.Threading.Thread.Sleep(220);
                        TrySelectItems(newWin, selectedNames);
                    }
                    catch { }
                });
            }

            return navigated;
        }

        private static List<AutomationElement> GetHeaderTabItems(IntPtr explorerHwnd)
        {
            var result = new List<AutomationElement>();
            if (explorerHwnd == IntPtr.Zero || !Win32Helper.IsWindow(explorerHwnd)) return result;

            try
            {
                var tabCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tab);
                var tabItemCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);

                // Primary: walk immediate UIA children of CabinetWClass and skip ShellTabWindowClass
                // (WinUI 3 XAML Islands TabView is exposed under CabinetWClass's UIA tree, not always the raw bridge HWND).
                AutomationElement winEl = AutomationElement.FromHandle(explorerHwnd);
                if (winEl != null)
                {
                    var walker = TreeWalker.ControlViewWalker;
                    AutomationElement child = walker.GetFirstChild(winEl);
                    while (child != null)
                    {
                        string childCls = string.Empty;
                        try { childCls = child.Current.ClassName ?? string.Empty; } catch { }

                        if (childCls != "ShellTabWindowClass" &&
                            childCls != "TabWindowClass" &&
                            !childCls.StartsWith("ExplorerBookmarkBar", StringComparison.OrdinalIgnoreCase))
                        {
                            ControlType ct = null;
                            try { ct = child.Current.ControlType; } catch { }
                            AutomationElement tabStrip = (ct == ControlType.Tab)
                                ? child
                                : child.FindFirst(TreeScope.Descendants, tabCond);

                            if (tabStrip != null)
                            {
                                AutomationElementCollection items = tabStrip.FindAll(TreeScope.Children, tabItemCond);
                                if (items == null || items.Count == 0)
                                {
                                    items = tabStrip.FindAll(TreeScope.Descendants, tabItemCond);
                                }
                                if (items != null && items.Count > 0)
                                {
                                    for (int t = 0; t < items.Count; t++) result.Add(items[t]);
                                    return result;
                                }
                            }
                            else
                            {
                                AutomationElementCollection directItems = child.FindAll(TreeScope.Descendants, tabItemCond);
                                if (directItems != null && directItems.Count > 0)
                                {
                                    for (int t = 0; t < directItems.Count; t++) result.Add(directItems[t]);
                                    return result;
                                }
                            }
                        }

                        try { child = walker.GetNextSibling(child); }
                        catch { break; }
                    }
                }
            }
            catch { }

            return result;
        }

        private static AutomationElement GetSelectedTabElement(IntPtr explorerHwnd, out int selectedIndex)
        {
            selectedIndex = -1;
            try
            {
                var items = GetHeaderTabItems(explorerHwnd);
                for (int i = 0; i < items.Count; i++)
                {
                    object patObj;
                    if (items[i].TryGetCurrentPattern(SelectionItemPattern.Pattern, out patObj))
                    {
                        var selPat = patObj as SelectionItemPattern;
                        if (selPat != null && selPat.Current.IsSelected)
                        {
                            selectedIndex = i;
                            return items[i];
                        }
                    }
                }
                App.Log("GetSelectedTabElement: found " + items.Count + " TabItems, none reported IsSelected.");
            }
            catch (Exception ex)
            {
                App.Log("GetSelectedTabElement error: " + ex.Message);
            }
            return null;
        }

        private static bool TrySelectAutomationTab(AutomationElement tabEl)
        {
            if (tabEl == null) return false;
            try
            {
                object patObj;
                if (tabEl.TryGetCurrentPattern(SelectionItemPattern.Pattern, out patObj))
                {
                    var selPat = patObj as SelectionItemPattern;
                    if (selPat != null)
                    {
                        if (!selPat.Current.IsSelected)
                        {
                            selPat.Select();
                        }
                        // Return immediately once Select() succeeded; checking Current.IsSelected synchronously
                        // before WinUI dispatches selection previously caused redundant Invoke/Ctrl+1 fallbacks.
                        return true;
                    }
                }

                if (tabEl.TryGetCurrentPattern(InvokePattern.Pattern, out patObj))
                {
                    var invPat = patObj as InvokePattern;
                    if (invPat != null)
                    {
                        invPat.Invoke();
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool IsAutomationTabSelected(AutomationElement tabEl)
        {
            if (tabEl == null) return false;
            try
            {
                object patObj;
                if (tabEl.TryGetCurrentPattern(SelectionItemPattern.Pattern, out patObj))
                {
                    var selPat = patObj as SelectionItemPattern;
                    if (selPat != null)
                    {
                        return selPat.Current.IsSelected;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void EnsureTabSelected(IntPtr explorerHwnd, AutomationElement prevTabElement, int prevTabIndex, bool allowShortcutFallback)
        {
            if (TrySelectAutomationTab(prevTabElement)) return;
            if (prevTabIndex < 0) return;

            try
            {
                var items = GetHeaderTabItems(explorerHwnd);
                if (prevTabIndex >= 0 && prevTabIndex < items.Count)
                {
                    if (TrySelectAutomationTab(items[prevTabIndex])) return;
                }
            }
            catch { }

            // Last-resort shortcut fallback (Ctrl+1..8) only on first restore if UIA failed completely
            if (allowShortcutFallback && prevTabIndex >= 0 && prevTabIndex <= 7 && Win32Helper.GetForegroundWindow() == explorerHwnd)
            {
                try
                {
                    App.Log("EnsureTabSelected: falling back to Ctrl+" + (prevTabIndex + 1) + " shortcut.");
                    byte vkDigit = (byte)(0x31 + prevTabIndex); // '1'..'8'
                    Win32Helper.keybd_event(Win32Helper.VK_CONTROL, 0, 0, UIntPtr.Zero);
                    Win32Helper.keybd_event(vkDigit, 0, 0, UIntPtr.Zero);
                    Win32Helper.keybd_event(vkDigit, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                    Win32Helper.keybd_event(Win32Helper.VK_CONTROL, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                }
                catch { }
            }
        }

        public static void OpenInNewTab(IntPtr targetHwnd, string path, string[] selectedNames = null, bool activateNewTab = true)
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

            if (activateNewTab)
            {
                OpenInNewTabCore(targetHwnd, path, selectedNames, true, null, -1);
                return;
            }

            // NEVER call UIAutomation synchronously on the UI thread hosting a WS_CHILD of CabinetWClass;
            // capture the currently selected TabItem on a background MTA thread, then dispatch back.
            IntPtr capturedTarget = targetHwnd;
            string capturedPath = path;
            string[] capturedSelected = selectedNames;
            Task.Run(() =>
            {
                int prevIdx = -1;
                AutomationElement prevEl = GetSelectedTabElement(capturedTarget, out prevIdx);
                System.Windows.Application app = System.Windows.Application.Current;
                if (app != null && app.Dispatcher != null)
                {
                    app.Dispatcher.BeginInvoke(new Action(() =>
                        OpenInNewTabCore(capturedTarget, capturedPath, capturedSelected, false, prevEl, prevIdx)));
                }
            });
        }

        private static readonly object _claimLock = new object();

        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

        /// <summary>Sleeps ~ms milliseconds but re-pins the bookmark bar / tab layout roughly every millisecond.</summary>
        private static void PinnedSleep(IntPtr explorerHwnd, int ms)
        {
            var sw = Stopwatch.StartNew();
            do
            {
                ExplorerChildBarManager.Instance.PinAllNow(explorerHwnd);
                System.Threading.Thread.Sleep(1);
            }
            while (sw.ElapsedMilliseconds < ms);
        }

        private static void OpenInNewTabCore(
            IntPtr targetHwnd,
            string path,
            string[] selectedNames,
            bool activateNewTab,
            AutomationElement prevTabElement,
            int prevTabIndex)
        {
            try
            {
                if (!Win32Helper.IsWindow(targetHwnd))
                {
                    OpenInNewWindow(path);
                    return;
                }

                // Ensure WPF button releases keyboard focus (UI thread only)
                try { System.Windows.Input.Keyboard.ClearFocus(); } catch { }

                // IMPORTANT: everything below (Shell.Windows COM enumeration, polling) runs on a background
                // thread. Doing it on the UI thread blocks the bookmark bar's own message loop, so the bar
                // could not repaint after Explorer covered it -> visible flash while a tab was opening.
                Task.Run(() => OpenInNewTabWorker(targetHwnd, path, selectedNames, activateNewTab, prevTabElement, prevTabIndex));
            }
            catch
            {
                OpenInNewWindow(path);
            }
        }

        private static void OpenInNewTabWorker(
            IntPtr targetHwnd,
            string path,
            string[] selectedNames,
            bool activateNewTab,
            AutomationElement prevTabElement,
            int prevTabIndex)
        {
            IntPtr capturedNewTabHwnd = IntPtr.Zero;
            IntPtr claimedWinUnk = IntPtr.Zero;
            List<object> pinnedExistingWins = null;
            timeBeginPeriod(1);
            try
            {
                var existingTabs = Win32Helper.GetExplorerTabs(targetHwnd);
                if (existingTabs.Count == 0)
                {
                    OpenInNewWindow(path);
                    return;
                }

                if (Win32Helper.GetForegroundWindow() != targetHwnd)
                {
                    Win32Helper.RestoreWindowToForeground(targetHwnd);
                }

                IntPtr activeTabBefore = GetActiveShellTabHwnd(targetHwnd);
                if (activeTabBefore == IntPtr.Zero) activeTabBefore = existingTabs[0];

                var existingSet = new HashSet<IntPtr>(existingTabs);
                HashSet<IntPtr> existingWinUnknowns = SnapshotExistingBrowserUnknowns(targetHwnd, out pinnedExistingWins);

                // Trigger Explorer's native Ctrl+T command (0xA21B) on the active ShellTabWindowClass
                Win32Helper.PostMessage(activeTabBefore, Win32Helper.WM_COMMAND, (IntPtr)0xA21B, IntPtr.Zero);

                dynamic createdWin = null;
                const int maxAttempts = 150; // ~1.5s at 10ms interval
                bool fallbackCtrlTSent = false;
                bool earlyRestoreDone = false;

                for (int attempts = 1; attempts <= maxAttempts; attempts++)
                {
                    PinnedSleep(targetHwnd, 10);

                    if (!Win32Helper.IsWindow(targetHwnd)) return;

                    if (capturedNewTabHwnd == IntPtr.Zero)
                    {
                        var currentTabs = Win32Helper.GetExplorerTabs(targetHwnd);
                        for (int i = 0; i < currentTabs.Count; i++)
                        {
                            IntPtr tab = currentTabs[i];
                            if (existingSet.Contains(tab)) continue;
                            lock (_claimLock)
                            {
                                if (_claimedNewTabHwnds.Contains(tab)) continue;
                                _claimedNewTabHwnds.Add(tab);
                            }
                            capturedNewTabHwnd = tab;
                            break;
                        }

                        // If 0xA21B did not spawn a tab within ~400ms, retry once with Ctrl+T
                        if (capturedNewTabHwnd == IntPtr.Zero && !fallbackCtrlTSent && attempts >= 40)
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

                    if (capturedNewTabHwnd == IntPtr.Zero) continue;

                    if (!activateNewTab && !earlyRestoreDone)
                    {
                        // Switch back to the previous tab the instant the new tab exists (before navigation).
                        earlyRestoreDone = true;
                        if (prevTabIndex >= 0 && prevTabIndex <= 7 && Win32Helper.GetForegroundWindow() == targetHwnd)
                        {
                            byte vkDigit = (byte)(0x31 + prevTabIndex);
                            Win32Helper.keybd_event(Win32Helper.VK_CONTROL, 0, 0, UIntPtr.Zero);
                            Win32Helper.keybd_event(vkDigit, 0, 0, UIntPtr.Zero);
                            Win32Helper.keybd_event(vkDigit, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                            Win32Helper.keybd_event(Win32Helper.VK_CONTROL, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                        }
                        else if (!IsAutomationTabSelected(prevTabElement))
                        {
                            EnsureTabSelected(targetHwnd, prevTabElement, prevTabIndex, false);
                        }
                    }

                    IntPtr foundUnk;
                    dynamic foundWin = FindNewCreatedBrowserWindow(
                        targetHwnd, capturedNewTabHwnd, existingWinUnknowns, attempts >= 15, out foundUnk);
                    if (foundWin == null || foundUnk == IntPtr.Zero) continue;

                    createdWin = foundWin;
                    claimedWinUnk = foundUnk;
                    lock (_claimLock) { _claimedWinUnknowns.Add(foundUnk); }

                    if (!NavigateNewTabComWindow(createdWin, path, selectedNames)) continue;

                    if (activateNewTab && Win32Helper.GetForegroundWindow() != targetHwnd)
                    {
                        Win32Helper.RestoreWindowToForeground(targetHwnd);
                    }

                    // Re-verify (60ms x 4) so Explorer's default Home navigation neither overwrites the target
                    // path nor leaves the new tab active on background open.
                    for (int v = 0; v < 4; v++)
                    {
                        PinnedSleep(targetHwnd, 60);
                        if (!Win32Helper.IsWindow(targetHwnd) || !Win32Helper.IsWindow(capturedNewTabHwnd)) return;

                        string currentPath = ExtractPathFromWin(createdWin);
                        if (!PathsRoughlyEqual(currentPath, path))
                        {
                            NavigateNewTabComWindow(createdWin, path, selectedNames);
                        }

                        if (!activateNewTab && !IsAutomationTabSelected(prevTabElement))
                        {
                            EnsureTabSelected(targetHwnd, prevTabElement, prevTabIndex, false);
                        }
                    }
                    return;
                }

                // Timed out
                if (capturedNewTabHwnd == IntPtr.Zero)
                {
                    // Never touch the current tab if the new tab failed to spawn
                    OpenInNewWindow(path);
                }
            }
            catch
            {
                if (capturedNewTabHwnd == IntPtr.Zero)
                {
                    OpenInNewWindow(path);
                }
            }
            finally
            {
                lock (_claimLock)
                {
                    if (capturedNewTabHwnd != IntPtr.Zero) _claimedNewTabHwnds.Remove(capturedNewTabHwnd);
                    if (claimedWinUnk != IntPtr.Zero) _claimedWinUnknowns.Remove(claimedWinUnk);
                }
                if (pinnedExistingWins != null) pinnedExistingWins.Clear();
                timeEndPeriod(1);
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


