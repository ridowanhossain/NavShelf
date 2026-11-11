using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace ExplorerBookmarkBar.Models
{
    public class BookmarkDisplayItem : INotifyPropertyChanged
    {
        public BookmarkItem Source { get; set; }

        public string Name
        {
            get { return Source != null ? Source.Name : string.Empty; }
        }

        public string Path
        {
            get { return Source != null ? Source.Path : string.Empty; }
        }

        public bool IsGroup
        {
            get { return Source != null && Source.IsFolder; }
        }

        /// <summary>1-based position on the bar (for the Alt+N hint), 0 = none.</summary>
        public int Position { get; set; }

        /// <summary>▾ shown for groups and for bookmarks whose subfolders can be browsed.</summary>
        private Visibility _chevronVisibility = Visibility.Collapsed;
        public Visibility ChevronVisibility
        {
            get { return _chevronVisibility; }
            set { if (_chevronVisibility != value) { _chevronVisibility = value; OnPropertyChanged("ChevronVisibility"); } }
        }

        private bool _isMissing;
        public bool IsMissing
        {
            get { return _isMissing; }
            set
            {
                if (_isMissing != value)
                {
                    _isMissing = value;
                    OnPropertyChanged("IsMissing");
                    OnPropertyChanged("ItemOpacity");
                    OnPropertyChanged("ToolTipText");
                }
            }
        }

        public double ItemOpacity
        {
            get { return _isMissing ? 0.45 : 1.0; }
        }

        public string ToolTipText
        {
            get
            {
                if (Source == null) return string.Empty;
                string hint = (Position >= 1 && Position <= 9) ? "   (Alt+" + Position + ")" : string.Empty;
                if (Source.IsFolder)
                {
                    int n = Source.Children != null ? Source.Children.Count : 0;
                    return Source.Name + " — " + n + " bookmark" + (n == 1 ? "" : "s") + hint;
                }
                if (_isMissing) return "Folder not found: " + Source.Path;
                return Source.Path + hint;
            }
        }

        private bool _isDropTarget;
        /// <summary>Highlighted while files are dragged over the middle of this bookmark (copy/move into it).</summary>
        public bool IsDropTarget
        {
            get { return _isDropTarget; }
            set { if (_isDropTarget != value) { _isDropTarget = value; OnPropertyChanged("IsDropTarget"); } }
        }

        public Geometry IconGeometry { get; set; }
        public Brush IconBrush { get; set; }
        public Thickness ItemMargin { get; set; }
        public Thickness ItemPadding { get; set; }

        private Visibility _dropIndicatorLeftVisibility = Visibility.Collapsed;
        public Visibility DropIndicatorLeftVisibility
        {
            get { return _dropIndicatorLeftVisibility; }
            set
            {
                if (_dropIndicatorLeftVisibility != value)
                {
                    _dropIndicatorLeftVisibility = value;
                    OnPropertyChanged("DropIndicatorLeftVisibility");
                }
            }
        }

        private Visibility _dropIndicatorRightVisibility = Visibility.Collapsed;
        public Visibility DropIndicatorRightVisibility
        {
            get { return _dropIndicatorRightVisibility; }
            set
            {
                if (_dropIndicatorRightVisibility != value)
                {
                    _dropIndicatorRightVisibility = value;
                    OnPropertyChanged("DropIndicatorRightVisibility");
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
