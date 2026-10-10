using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShopeeBackend.Models
{
    public class CategoryInfo : INotifyPropertyChanged
    {
        private string _url = string.Empty;
        private string _name = string.Empty;
        private int _currentSubIndex = 1;
        private int _currentPage = 1;
        private int _totalSubCategories = 0;
        private string _status = "Chờ quét"; // "Chờ quét", "Đang quét", "Hoàn thành"
        private DateTime? _lastScrapedAt;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string Url
        {
            get => _url;
            set { if (_url != value) { _url = value; OnPropertyChanged(); } }
        }

        public string Name
        {
            get => _name;
            set { if (_name != value) { _name = value; OnPropertyChanged(); } }
        }

        public int CurrentSubIndex
        {
            get => _currentSubIndex;
            set
            {
                if (_currentSubIndex != value)
                {
                    _currentSubIndex = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressDisplay));
                }
            }
        }

        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (_currentPage != value)
                {
                    _currentPage = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressDisplay));
                }
            }
        }

        public int TotalSubCategories
        {
            get => _totalSubCategories;
            set
            {
                if (_totalSubCategories != value)
                {
                    _totalSubCategories = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressDisplay));
                }
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressDisplay));
                }
            }
        }

        public DateTime? LastScrapedAt
        {
            get => _lastScrapedAt;
            set { if (_lastScrapedAt != value) { _lastScrapedAt = value; OnPropertyChanged(); } }
        }

        public string ProgressDisplay
        {
            get
            {
                if (Status == "Hoàn thành")
                    return "✔ Hoàn thành";

                string subText = TotalSubCategories > 0
                    ? $"Mục {CurrentSubIndex}/{TotalSubCategories} (Trang {CurrentPage})"
                    : $"Mục {CurrentSubIndex} (Trang {CurrentPage})";

                if (Status == "Đang quét")
                    return $"⟳ Đang quét: {subText}";

                if (CurrentSubIndex > 1 || CurrentPage > 1)
                    return $"⏸ Tạm dừng: {subText}";

                return "Chưa quét";
            }
        }
    }
}
