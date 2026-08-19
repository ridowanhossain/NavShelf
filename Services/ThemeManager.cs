using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ExplorerBookmarkBar.Services
{
    public static class ThemeManager
    {
        public static event Action ThemeChanged;

        public static bool IsDarkTheme { get; private set; }

        public static void Initialize()
        {
            ApplyCurrentTheme();

            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
                {
                    var app = Application.Current;
                    if (app != null && app.Dispatcher != null)
                    {
                        app.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            CheckAndApplyTheme();
                        }));
                    }
                }
            };
        }

        public static bool DetectSystemDarkTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AppsUseLightTheme");
                        if (val != null && val is int)
                        {
                            return ((int)val) == 0;
                        }
                    }
                }
            }
            catch { }
            return true;
        }

        public static void CheckAndApplyTheme()
        {
            bool detected = DetectSystemDarkTheme();
            if (detected != IsDarkTheme)
            {
                ApplyCurrentTheme();
            }
        }

        public static void ApplyCurrentTheme()
        {
            bool isDark = DetectSystemDarkTheme();
            IsDarkTheme = isDark;

            var app = Application.Current;
            if (app == null) return;
            var res = app.Resources;
            if (res == null) return;

            // Colors for Dark vs Light Mode matching Windows 11 Fluent Design
            string barBg = isDark ? "#191919" : "#F3F3F3";
            string barBorder = isDark ? "#2B2B2B" : "#E5E5E5";
            string itemHover = isDark ? "#282828" : "#E5E5E5";
            string itemHoverBorder = isDark ? "#383838" : "#D0D0D0";
            string itemPressed = isDark ? "#333333" : "#DADADA";
            string textPrimary = isDark ? "#EDEDED" : "#1F1F1F";
            string textSecondary = isDark ? "#A0A0A0" : "#5C5C5C";
            string accentBlue = isDark ? "#60CDFF" : "#0067C0";
            string accentHover = isDark ? "#4CC2FF" : "#1873C4";
            string accentGold = isDark ? "#FFD54F" : "#D97706";

            string dialogBg = isDark ? "#202020" : "#FFFFFF";
            string titleBarBg = isDark ? "#1E1E1E" : "#F3F3F3";
            string dialogBorder = isDark ? "#383838" : "#D5D5D5";
            string sectionBg = isDark ? "#2B2B2B" : "#F7F7F7";
            string inputBg = isDark ? "#2D2D2D" : "#FFFFFF";
            string inputBorder = isDark ? "#444444" : "#CCCCCC";

            string menuBg = isDark ? "#202020" : "#FFFFFF";
            string menuBorder = isDark ? "#2B2B2B" : "#E5E5E5";
            string menuHover = isDark ? "#2D2D2D" : "#EDEDED";
            string menuSeparator = isDark ? "#383838" : "#E5E5E5";

            SetBrush(res, "BarBackground", barBg);
            SetBrush(res, "BarBorder", barBorder);
            SetBrush(res, "ItemHoverBackground", itemHover);
            SetBrush(res, "ItemHoverBorder", itemHoverBorder);
            SetBrush(res, "ItemPressedBackground", itemPressed);
            SetBrush(res, "TextPrimary", textPrimary);
            SetBrush(res, "TextSecondary", textSecondary);
            SetBrush(res, "AccentBlue", accentBlue);
            SetBrush(res, "AccentColor", accentBlue);
            SetBrush(res, "AccentHover", accentHover);
            SetBrush(res, "AccentGold", accentGold);

            SetBrush(res, "DialogBg", dialogBg);
            SetBrush(res, "TitleBarBg", titleBarBg);
            SetBrush(res, "DialogBorder", dialogBorder);
            SetBrush(res, "SectionBg", sectionBg);
            SetBrush(res, "InputBg", inputBg);
            SetBrush(res, "InputBorder", inputBorder);

            SetBrush(res, "MenuBg", menuBg);
            SetBrush(res, "MenuBorder", menuBorder);
            SetBrush(res, "MenuHover", menuHover);
            SetBrush(res, "MenuSeparator", menuSeparator);

            App.Log(string.Format("ThemeManager: Applied {0} theme.", isDark ? "Dark" : "Light"));

            if (ThemeChanged != null)
            {
                ThemeChanged();
            }
        }

        private static void SetBrush(ResourceDictionary res, string key, string hex)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                brush.Freeze();

                if (res.Contains(key))
                {
                    res[key] = brush;
                }
                else
                {
                    res.Add(key, brush);
                }
            }
            catch { }
        }
    }
}
