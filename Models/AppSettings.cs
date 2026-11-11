using System;

namespace ExplorerBookmarkBar.Models
{
    public class AppSettings
    {
        public int TopOffset { get; set; }
        public int LeftMargin { get; set; }
        public int RightMargin { get; set; }
        public int BarHeight { get; set; }
        public int CornerRadius { get; set; }
        public bool AutoHideWhenInactive { get; set; }
        public bool StartWithWindows { get; set; }
        public bool ShowIconsOnly { get; set; }
        public int ItemSpacing { get; set; }
        public int ItemPadding { get; set; }
        public string Theme { get; set; } // "Dark", "Light", "Fluent"
        public bool UseAutoLayout { get; set; }
        public bool EnableNumberShortcuts { get; set; } // Alt+1..9 opens bookmark N in Explorer / file dialogs

        public AppSettings()
        {
            TopOffset = 135;
            LeftMargin = 0;
            RightMargin = 0;
            BarHeight = 32;
            CornerRadius = 0;
            AutoHideWhenInactive = false;
            StartWithWindows = false;
            ShowIconsOnly = false;
            ItemSpacing = 2;
            ItemPadding = 8;
            Theme = "Dark";
            UseAutoLayout = true;
            EnableNumberShortcuts = true;
        }
    }
}
