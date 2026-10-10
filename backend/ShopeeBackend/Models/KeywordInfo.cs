using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShopeeBackend.Models
{
    public class KeywordInfo : INotifyPropertyChanged
    {
        private string _keyword = string.Empty;
        private int _currentPage = 1;
        private int _totalPages = 0;
        private int _productCount = 0;
        private string _status = "Chờ quét"; // "Chờ quét", "Đang quét", "Hoàn thành", "Tạm dừng", "Lỗi"
        private DateTime? _lastScrapedAt;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string Keyword
        {
            get => _keyword;
            set { if (_keyword != value) { _keyword = value; OnPropertyChanged(); } }
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

        public int TotalPages
        {
            get => _totalPages;
            set
            {
                if (_totalPages != value)
                {
                    _totalPages = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressDisplay));
                }
            }
        }

        public int ProductCount
        {
            get => _productCount;
            set
            {
                if (_productCount != value)
                {
                    _productCount = value;
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
                    return ProductCount > 0 ? $"✔ Hoàn thành ({ProductCount:N0} SP)" : "✔ Hoàn thành";

                if (Status == "Đang quét")
                    return $"⟳ Đang quét: Trang {CurrentPage} ({ProductCount:N0} SP)";

                if (CurrentPage > 1)
                    return $"⏸ Tạm dừng: Trang {CurrentPage} ({ProductCount:N0} SP)";

                return "Chưa quét";
            }
        }
    }
}
