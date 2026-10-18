using System;

namespace ShopeeBackend.Models
{
    public class ScrapedProduct
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ItemId { get; set; } = string.Empty;
        public string ShopId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public double Rating { get; set; }
        public int RatingCount { get; set; }
        public long HistoricalSold { get; set; }
        public long MonthlySold { get; set; }
        public string Location { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string CategoryPath { get; set; } = string.Empty; // Danh mục cha > Danh mục con > Danh mục cháu
        public string CategoryUrl { get; set; } = string.Empty;
        public string ProductUrl { get; set; } = string.Empty;
        public DateTime ScrapedAt { get; set; } = DateTime.Now;

        public string CategoryHierarchy
        {
            get
            {
                string parent = ShopeeBackend.Views.ProductDataView.GetParentCategoryName(CategoryUrl, CategoryPath);
                if (string.IsNullOrWhiteSpace(parent) || parent == "Chưa phân loại")
                {
                    return string.IsNullOrWhiteSpace(CategoryPath) ? "Chưa phân loại" : CategoryPath;
                }

                if (string.IsNullOrWhiteSpace(CategoryPath))
                {
                    return parent;
                }

                string trimmedPath = CategoryPath.Trim();
                if (trimmedPath.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmedPath;
                }

                return $"{parent} - {trimmedPath}";
            }
        }

        // 3-5 Days Delta Tracking & Opportunity Scoring
        public long PreviousSold { get; set; }
        public long SoldDelta { get; set; }
        public int ScanCount { get; set; } = 1;
        public DateTime? FirstScrapedAt { get; set; }
        public DateTime? LastTotalAt { get; set; }

        public double DaysTracked { get; set; }
        public double DailyVelocity { get; set; }

        // Helpers
        public bool IsRescanned => ScanCount > 1;
        public string ScanTypeDisplay => ScanCount > 1 ? $"Quét lại ({ScanCount})" : "Lần đầu";

        // UI Formatted Helpers
        public string FormattedPrice => Price > 0 ? $"{Price:N0} ₫" : "0 ₫";
        public string FormattedSold => HistoricalSold > 0
            ? (HistoricalSold >= 1000 ? $"{HistoricalSold / 1000.0:0.#}k" : HistoricalSold.ToString("N0"))
            : "0";
        public string FormattedMonthlySold => MonthlySold > 0 
            ? (MonthlySold >= 1000 ? $"{MonthlySold / 1000.0:0.#}k" : MonthlySold.ToString("N0")) 
            : "-";
        public string FormattedSoldDelta => ScanCount > 1 ? (SoldDelta > 0 ? $"+{SoldDelta:N0}" : "0") : "0";
        public string FormattedRating => Rating > 0 ? $"{Rating:0.0} ★" : "Chưa có";
        public string FormattedScrapedAt => ScrapedAt.ToString("dd/MM/yyyy HH:mm");

        public string FormattedDelta => DaysTracked >= 0.5 
            ? (SoldDelta > 0 ? $"+{SoldDelta:N0} ({DailyVelocity:0.#}/ngày)" : "0 (Đứng)")
            : "Lần quét đầu";
    }
}
