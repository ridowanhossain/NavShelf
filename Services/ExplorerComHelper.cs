using System;
using System.Diagnostics;
using System.IO;

namespace ExplorerBookmarkBar.Services
{
    public static class ExplorerComHelper
    {
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

                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return string.Empty;

                dynamic shell = Activator.CreateInstance(shellType);
                dynamic windows = shell.Windows();

                for (int i = 0; i < windows.Count; i++)
                {
                    try
                    {
                        dynamic win = windows.Item(i);
                        long hwnd = 0;
                        try { hwnd = (long)win.HWND; } catch { }

                        // If matching HWND or targetHwnd is 0, check document path
                        if (targetHwnd == IntPtr.Zero || hwnd == targetHwnd.ToInt64() || targetHwnd.ToInt64() == 0)
                        {
                            if (win.Document != null && win.Document.Folder != null && win.Document.Folder.Self != null)
                            {
                                string path = win.Document.Folder.Self.Path;
                                if (!string.IsNullOrEmpty(path))
                                {
                                    return path;
                                }
                            }

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

                            string locName = win.LocationName;
                            if (!string.IsNullOrEmpty(locName))
                            {
                                return locName;
                            }
                        }
                    }
                    catch { }
                }

                // If not found by exact HWND, return path of first Explorer window
                for (int i = 0; i < windows.Count; i++)
                {
                    try
                    {
                        dynamic win = windows.Item(i);
                        if (win.Document != null && win.Document.Folder != null && win.Document.Folder.Self != null)
                        {
                            string path = win.Document.Folder.Self.Path;
                            if (!string.IsNullOrEmpty(path)) return path;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return string.Empty;
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

            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType);
                    dynamic windows = shell.Windows();

                    // Search backwards from newest/active tab
                    for (int i = windows.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            dynamic win = windows.Item(i);
                            long hwnd = 0;
                            try { hwnd = (long)win.HWND; } catch { }

                            if (targetHwnd == IntPtr.Zero || hwnd == targetHwnd.ToInt64())
                            {
                                win.Navigate2(path);
                                return true;
                            }
                        }
                        catch { }
                    }

                    // Fallback to first available window
                    if (windows.Count > 0)
                    {
                        dynamic win = windows.Item(0);
                        win.Navigate2(path);
                        return true;
                    }
                }
            }
            catch { }

            // Fallback: Open using process start
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
