using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ExplorerBookmarkBar.Services
{
    /// <summary>
    /// Alt+1 … Alt+9 opens the Nth bookmark on the bar — only while an Explorer window or a
    /// file Open/Save dialog that has a bookmark bar is in the foreground. Everywhere else the
    /// keys pass through untouched.
    /// </summary>
    public class KeyboardShortcutService
    {
        private static KeyboardShortcutService _instance;
        public static KeyboardShortcutService Instance
        {
            get
            {
                if (_instance == null) _instance = new KeyboardShortcutService();
                return _instance;
            }
        }

        private const int VK_SHIFT = 0x10;
        private const int VK_CONTROL = 0x11;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;
        private const byte VK_MASK = 0xE8; // unassigned key, used to stop a lone Alt release from opening menus/key tips

        private Win32Helper.LowLevelKeyboardProc _proc; // keep a reference so the GC doesn't collect the callback
        private IntPtr _hook = IntPtr.Zero;
        private Dispatcher _dispatcher;

        public void Start()
        {
            if (_hook != IntPtr.Zero) return;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _proc = HookCallback;
            _hook = Win32Helper.SetWindowsHookEx(Win32Helper.WH_KEYBOARD_LL, _proc, Win32Helper.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                App.Log("Keyboard shortcut hook failed: " + Marshal.GetLastWin32Error());
            }
        }

        public void Stop()
        {
            if (_hook != IntPtr.Zero)
            {
                Win32Helper.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private static bool IsDown(int vk)
        {
            return (Win32Helper.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && wParam.ToInt32() == Win32Helper.WM_SYSKEYDOWN)
                {
                    var kb = (Win32Helper.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Win32Helper.KBDLLHOOKSTRUCT));
                    bool isDigit = kb.vkCode >= 0x31 && kb.vkCode <= 0x39; // '1'..'9'
                    bool altOnly = (kb.flags & Win32Helper.LLKHF_ALTDOWN) != 0
                                   && !IsDown(VK_CONTROL) && !IsDown(VK_SHIFT) && !IsDown(VK_LWIN) && !IsDown(VK_RWIN);

                    if (isDigit && altOnly
                        && BookmarkManager.Instance.Settings.EnableNumberShortcuts
                        && !ExplorerChildBarManager.Instance.IsPaused)
                    {
                        IntPtr fg = Win32Helper.GetForegroundWindow();
                        IntPtr root = Win32Helper.GetAncestor(fg, Win32Helper.GA_ROOT);
                        if (root == IntPtr.Zero) root = fg;

                        var bar = ExplorerChildBarManager.Instance.GetBarFor(root);
                        if (bar != null)
                        {
                            int position = (int)(kb.vkCode - 0x30);
                            _dispatcher.BeginInvoke((Action)(() =>
                            {
                                // Tap an unassigned key so releasing Alt doesn't show Explorer's key tips / menu bar
                                Win32Helper.keybd_event(VK_MASK, 0, 0, UIntPtr.Zero);
                                Win32Helper.keybd_event(VK_MASK, 0, Win32Helper.KEYEVENTF_KEYUP, UIntPtr.Zero);
                                try { bar.ActivateItemAt(position); }
                                catch (Exception ex) { App.Log("Alt+" + position + " error: " + ex.Message); }
                            }));
                            return (IntPtr)1; // swallow the key
                        }
                    }
                }
            }
            catch { }

            return Win32Helper.CallNextHookEx(_hook, nCode, wParam, lParam);
        }
    }
}
