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
