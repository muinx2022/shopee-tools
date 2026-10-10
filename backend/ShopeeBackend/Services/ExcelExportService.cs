using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiniExcelLibs;
using ShopeeBackend.Models;
using ShopeeBackend.Views;

namespace ShopeeBackend.Services
{
    public static class ExcelExportService
    {
        public static void ExportProductsToExcel(string filePath, IEnumerable<ScrapedProduct> products)
        {
            var exportRows = products.Select((p, idx) => new
            {
                STT = idx + 1,
                Lần_Quét = p.ScanTypeDisplay,
                Số_Lần_Quét = p.ScanCount,
                Khuyến_Nghị = p.RecommendationTag,
                Điểm_Tiềm_Năng = p.OpportunityScore,
                Tổng_Đã_Bán = p.HistoricalSold,
                Đã_Bán_Tháng = p.MonthlySold,
                Phát_Sinh = p.SoldDelta,
                Đã_Bán_Lần_Trước = p.PreviousSold,
                Tên_Sản_Phẩm = p.Name,
                Ngành_Hàng_Cấp_1 = ProductDataView.GetParentCategoryName(p.CategoryUrl, p.CategoryPath),
                Danh_Mục_Phân_Cấp = p.CategoryHierarchy,
                Giá_Bán_VNĐ = p.Price,
                Đánh_Giá_Sao = p.Rating > 0 ? (object)p.Rating : "",
                Lượt_Đánh_Giá = p.RatingCount,
                Nơi_Bán = p.Location,
                Link_Sản_Phẩm = p.ProductUrl,
                Link_Ảnh = p.ImageUrl,
                Đường_Dẫn_Ảnh_Máy = ImageDownloadService.HasLocalImage(p) ? ImageDownloadService.GetLocalImagePath(p) : "",
                Thời_Gian_Quét = p.ScrapedAt.ToString("yyyy-MM-dd HH:mm:ss")
            }).ToList();

            MiniExcel.SaveAs(filePath, exportRows, overwriteFile: true);
        }
    }
}
