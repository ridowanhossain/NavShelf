using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace ExplorerBookmarkBar.Services
{
    public static class Win32Helper
    {
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int GWLP_WNDPROC = -4;

        public const int WS_CHILD = 0x40000000;
        public const int WS_VISIBLE = 0x10000000;
        public const int WS_CLIPSIBLINGS = 0x04000000;
        public const int WS_CLIPCHILDREN = 0x02000000;
        public const int WS_POPUP = unchecked((int)0x80000000);

        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_CONTROLPARENT = 0x00010000;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public static readonly IntPtr HWND_TOP = new IntPtr(0);
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOREDRAW = 0x0008;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_HIDEWINDOW = 0x0080;
        public const uint SWP_NOCOPYBITS = 0x0100;
        public const uint SWP_NOOWNERZORDER = 0x0200;
        public const uint SWP_NOSENDCHANGING = 0x0400;
        public const uint SWP_ASYNCWINDOWPOS = 0x4000;

        public const uint GA_PARENT = 1;
        public const uint GA_ROOT = 2;
        public const uint GA_ROOTOWNER = 3;
        public const uint GW_HWNDNEXT = 2;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int WM_SIZE = 0x0005;
        public const int WM_MOVE = 0x0003;
        public const int WM_WINDOWPOSCHANGED = 0x0047;
        public const int WM_WINDOWPOSCHANGING = 0x0046;
        public const int WM_DESTROY = 0x0002;
        public const int WM_NCDESTROY = 0x0082;
        public const int WM_ACTIVATE = 0x0006;
        public const int WM_SETFOCUS = 0x0007;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width
            {
                get { return Right - Left; }
            }

            public int Height
            {
                get { return Bottom - Top; }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public uint flags;
        }

        public delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);

        public static double GetWindowDpiScale(IntPtr hWnd)
        {
            try
            {
                uint dpi = GetDpiForWindow(hWnd);
                if (dpi > 0) return dpi / 96.0;
            }
            catch { }
            return 1.0;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

        [DllImport("comctl32.dll", SetLastError = true)]
        public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetTopWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetNextWindow(IntPtr hWnd, uint wCmd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public static string GetWindowClassName(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return string.Empty;
            StringBuilder sb = new StringBuilder(256);
            GetClassName(hWnd, sb, 256);
            return sb.ToString();
        }

        public static string GetWindowText(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return string.Empty;
            StringBuilder sb = new StringBuilder(512);
            GetWindowText(hWnd, sb, 512);
            return sb.ToString();
        }

        public static IntPtr GetRootWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return IntPtr.Zero;
            IntPtr root = GetAncestor(hWnd, GA_ROOT);
            if (root != IntPtr.Zero) return root;
            IntPtr rootOwner = GetAncestor(hWnd, GA_ROOTOWNER);
            if (rootOwner != IntPtr.Zero) return rootOwner;
            return hWnd;
        }

        public static bool IsExplorerWindow(IntPtr hWnd, out IntPtr rootHwnd)
        {
            rootHwnd = IntPtr.Zero;
            if (hWnd == IntPtr.Zero) return false;

            rootHwnd = GetRootWindow(hWnd);

            string rootClass = GetWindowClassName(rootHwnd);
            if (rootClass == "CabinetWClass" || rootClass == "ExploreWClass")
            {
                return true;
            }

            string directClass = GetWindowClassName(hWnd);
            if (directClass == "CabinetWClass" || directClass == "ExploreWClass")
            {
                rootHwnd = hWnd;
                return true;
            }

            uint pid;
            GetWindowThreadProcessId(rootHwnd, out pid);
            try
            {
                Process proc = Process.GetProcessById((int)pid);
                if (proc != null && proc.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase))
                {
                    if (rootClass != "Shell_TrayWnd" && rootClass != "Progman" && rootClass != "WorkerW" && !rootClass.Contains("Tray"))
                    {
                        if (rootClass == "CabinetWClass") return true;
                    }
                }
            }
            catch { }

            return false;
        }

        public static IntPtr FindVisibleExplorerWindow()
        {
            IntPtr hwnd = IntPtr.Zero;
            while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, "CabinetWClass", null)) != IntPtr.Zero)
            {
                if (IsWindowVisible(hwnd) && !IsIconic(hwnd))
                {
                    RECT rect;
                    if (GetWindowBounds(hwnd, out rect) && rect.Width > 150 && rect.Height > 150)
                    {
                        return hwnd;
                    }
                }
            }
            return IntPtr.Zero;
        }

        public const uint WM_SETTEXT = 0x000C;
        public const uint WM_KEYDOWN = 0x0100;
        public const uint WM_KEYUP = 0x0101;
        public const uint WM_COMMAND = 0x0111;
        public const int VK_RETURN = 0x0D;
        public const byte VK_CONTROL = 0x11;
        public const byte VK_L = 0x4C;
        public const byte VK_MENU = 0x12;
        public const byte VK_D = 0x44;
        public const byte VK_V = 0x56;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        public static extern int GetDlgCtrlID(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

        // ───────────── Shell file copy/move (Windows progress + conflict dialogs, Ctrl+Z undo) ─────────────

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        private const uint FO_MOVE = 0x0001;
        private const uint FO_COPY = 0x0002;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOCONFIRMMKDIR = 0x0200;

        /// <summary>
        /// Copies or moves files/folders into destDir using the Windows shell, on a background thread so the
        /// bookmark bar stays responsive. Windows shows its own progress, "replace or skip" and error dialogs.
        /// </summary>
        public static void ShellTransfer(IntPtr owner, string[] sources, string destDir, bool move)
        {
            if (sources == null || sources.Length == 0 || string.IsNullOrEmpty(destDir)) return;

            string from = string.Join("\0", sources) + "\0\0";
            string to = destDir + "\0\0";

            var t = new System.Threading.Thread(() =>
            {
                try
                {
                    var op = new SHFILEOPSTRUCT
                    {
                        hwnd = owner,
                        wFunc = move ? FO_MOVE : FO_COPY,
                        pFrom = from,
                        pTo = to,
                        fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR)
                    };
                    int rc = SHFileOperation(ref op);
                    if (rc != 0 && !op.fAnyOperationsAborted)
                    {
                        App.Log("ShellTransfer failed with code 0x" + rc.ToString("X"));
                    }
                }
                catch (Exception ex)
                {
                    App.Log("ShellTransfer error: " + ex.Message);
                }
            });
            t.SetApartmentState(System.Threading.ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int StrCmpLogicalW(string psz1, string psz2);

        // ───────────── Low-level keyboard hook (Alt+1..9 bookmark shortcuts) ─────────────

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public const int WH_KEYBOARD_LL = 13;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const uint LLKHF_ALTDOWN = 0x20;
        public const uint LLKHF_INJECTED = 0x10;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        public const uint RDW_INVALIDATE = 0x0001;
        public const uint RDW_ERASE = 0x0004;
        public const uint RDW_ALLCHILDREN = 0x0080;
        public const uint RDW_UPDATENOW = 0x0100;
        public const uint RDW_FRAME = 0x0400;

        [DllImport("user32.dll")]
        public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        public const uint WM_GETTEXT = 0x000D;
        public const uint WM_GETTEXTLENGTH = 0x000E;
        public const int IDOK = 1;
        public const int BN_CLICKED = 0;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);

        /// <summary>
        /// Reads a control's text via WM_GETTEXT (works for Edit controls in other processes,
        /// unlike GetWindowText).
        /// </summary>
        public static string GetControlText(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return string.Empty;
            try
            {
                int len = (int)SendMessage(hWnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
                if (len <= 0) return string.Empty;
                StringBuilder sb = new StringBuilder(len + 1);
                SendMessage(hWnd, WM_GETTEXT, (IntPtr)(len + 1), sb);
                return sb.ToString();
            }
            catch { return string.Empty; }
        }

        public static bool IsFileDialog(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return false;

            bool hasFolderView = false;
            bool hasControls = false;

            EnumChildWindows(hwnd, (ch, lParam) =>
            {
                string cls = GetWindowClassName(ch);
                if (cls == "DUIViewWndClassName" || cls == "SHELLDLL_DefView")
                {
                    hasFolderView = true;
                }
                else if (cls == "WorkerW" || cls == "ReBarWindow32" || cls == "Address Band Root" || cls == "ComboBoxEx32" || cls == "Edit")
                {
                    hasControls = true;
                }
                return !(hasFolderView && hasControls);
            }, IntPtr.Zero);

            return hasFolderView && hasControls;
        }

        public static List<IntPtr> GetAllVisibleExplorerWindows()
        {
            List<IntPtr> list = new List<IntPtr>();
            EnumWindows((hwnd, lParam) =>
            {
                if (IsWindowVisible(hwnd) && !IsIconic(hwnd))
                {
                    string cls = GetWindowClassName(hwnd);
                    if (cls == "CabinetWClass" || cls == "ExploreWClass")
                    {
                        list.Add(hwnd);
                    }
                    else if (cls == "#32770")
                    {
                        RECT r;
                        if (GetWindowRect(hwnd, out r) && r.Width >= 250 && r.Height >= 200 && IsFileDialog(hwnd))
                        {
                            list.Add(hwnd);
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static bool GetWindowBounds(IntPtr hWnd, out RECT bounds)
        {
            bounds = new RECT();
            if (hWnd == IntPtr.Zero) return false;

            int res = DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out bounds, Marshal.SizeOf(typeof(RECT)));
            if (res == 0 && bounds.Width > 0 && bounds.Height > 0)
            {
                return true;
            }

            return GetWindowRect(hWnd, out bounds);
        }

        public struct ExplorerLayoutInfo
        {
            public int TopBarHeight;
            public IntPtr ContentContainerHwnd;
            public RECT ClientRect;
        }

        /// <summary>
        /// Analyzes Windows 11 / Windows 10 Explorer window to find where the top bar (Tabs + Address + Command Bar) ends
        /// and where the content view (ShellTabWindowClass / DUIView / SHELLDLL_DefView) starts.
        /// </summary>
        public static ExplorerLayoutInfo DetectExplorerLayout(IntPtr explorerHwnd)
        {
            ExplorerLayoutInfo layout = new ExplorerLayoutInfo();
            layout.TopBarHeight = 135; // Default standard height of Windows 11 Tabs + Address + Command Bar
            layout.ContentContainerHwnd = IntPtr.Zero;

            RECT clientRect;
            GetClientRect(explorerHwnd, out clientRect);
            layout.ClientRect = clientRect;

            if (explorerHwnd == IntPtr.Zero) return layout;

            RECT expScreenRect;
            GetWindowRect(explorerHwnd, out expScreenRect);

            // Enumerate direct child windows
            IntPtr contentHwnd = IntPtr.Zero;
            int highestTop = int.MaxValue;

            EnumChildWindows(explorerHwnd, (ch, lParam) =>
            {
                string cls = GetWindowClassName(ch);
                if (cls == "ShellTabWindowClass" || cls == "DUIViewWndClassName" || cls == "SHELLDLL_DefView")
                {
                    RECT childRect;
                    if (GetWindowRect(ch, out childRect))
                    {
                        int relTop = childRect.Top - expScreenRect.Top;
                        // The content view typically starts below the top bar (e.g. 100-150px)
                        if (relTop > 60 && relTop < 250 && relTop < highestTop)
                        {
                            highestTop = relTop;
                            contentHwnd = ch;
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (contentHwnd != IntPtr.Zero && highestTop < int.MaxValue)
            {
                layout.TopBarHeight = highestTop;
                layout.ContentContainerHwnd = contentHwnd;
            }

            return layout;
        }

        public static void SetAutoStart(bool enable)
        {
            string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            string appName = "ExplorerBookmarkBar";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKey, true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        string exePath = Process.GetCurrentProcess().MainModule.FileName;
                        key.SetValue(appName, "\"" + exePath + "\"");
                    }
                    else
                    {
                        key.DeleteValue(appName, false);
                    }
                }
            }
            catch { }
        }

        public static bool IsAutoStartEnabled()
        {
            string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            string appName = "ExplorerBookmarkBar";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(runKey, false))
                {
                    return key.GetValue(appName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static System.Windows.Media.ImageSource GetExplorerIconImageSource()
        {
            try
            {
                string expPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                using (System.Drawing.Icon ico = System.Drawing.Icon.ExtractAssociatedIcon(expPath))
                {
                    if (ico != null)
                    {
                        return System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            ico.Handle,
                            System.Windows.Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
