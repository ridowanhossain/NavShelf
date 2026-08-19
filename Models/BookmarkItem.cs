using System;
using System.Collections.Generic;

namespace ExplorerBookmarkBar.Models
{
    public class BookmarkItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string IconType { get; set; } // "Folder", "Drive", "Star", "Code", "Music", "Video", "Image", "Desktop", "Download", "Document", "Custom"
        public string CustomColor { get; set; } // hex color e.g. "#0078D4" or empty
        public bool IsFolder { get; set; }
        public List<BookmarkItem> Children { get; set; }

        public BookmarkItem()
        {
            Id = Guid.NewGuid().ToString();
            Children = new List<BookmarkItem>();
            IconType = "Folder";
            CustomColor = "";
        }

        public BookmarkItem(string name, string path, string iconType = "Folder", bool isFolder = false)
        {
            Id = Guid.NewGuid().ToString();
            Name = name;
            Path = path;
            IconType = iconType;
            IsFolder = isFolder;
            CustomColor = "";
            Children = new List<BookmarkItem>();
        }
    }
}
