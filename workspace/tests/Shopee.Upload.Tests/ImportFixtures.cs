using ClosedXML.Excel;
using Shopee.Upload.Import;

namespace Shopee.Upload.Tests;

/// <summary>File xlsx GIẢ cho test GĐ5c — dựng bằng ClosedXML vào thư mục tạm. Không dùng file thật của user.</summary>
internal static class ImportFixtures
{
    /// <summary>Mật khẩu giả trong file mẫu — test kiểm không chuỗi nào lọt ra log / tóm tắt.</summary>
    public const string PassA = "MkShopA#2026";
    public const string MailPassA = "MailA!secret9";
    public const string PassB = "MkShopB#7788";
    public const string MailPassB = "MailB!secret7";
    public const string PassUnmatched = "KhongKhop#555";

    /// <summary>File giống file user: 2 dòng tiêu đề phụ, hàng tiêu đề ở dòng 3, cột lạ (SĐT, ngân hàng, họ tên) xen giữa, cột
    /// "pass Mail" đứng TRƯỚC cột "mail" (khớp kiểu contains là lấy nhầm), tên cột lẫn hoa thường.</summary>
    public static string WriteSample(string dir, string name = "mat-khau.xlsx")
    {
        var path = Path.Combine(dir, name);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Trang tính1");
        ws.Cell(1, 1).Value = "DANH SÁCH SHOP";
        ws.Cell(2, 1).Value = "cập nhật 18/11";
        string[] head = ["STT", "acc", "Họ tên", "PASS", "SĐT", "Pass  Mail", "Số TK ngân hàng", "Mail"];
        for (var i = 0; i < head.Length; i++) ws.Cell(3, i + 1).Value = head[i];
        Row(ws, 4, "1", " shop-a.login ", "Nguyễn Văn A", PassA, "0901234567", MailPassA, "0011223344", " a@hotmail.com ");
        Row(ws, 5, "2", "Shop Bé Xinh", "Trần B", PassB, "0907654321", MailPassB, "9988776655", "b@hotmail.com");
        Row(ws, 6, "", "", "", "", "", "", "", "");   // dòng trống
        Row(ws, 7, "3", "khong-co-tren-app", "Lê C", PassUnmatched, "0900000000", "x", "1", "c@hotmail.com");
        wb.SaveAs(path);
        return path;
    }

    public static void Row(IXLWorksheet ws, int r, params string[] values)
    {
        for (var i = 0; i < values.Length; i++)
            if (values[i].Length > 0) ws.Cell(r, i + 1).Value = values[i];
    }

    public static PasswordRow PRow(int n, string acc, string pass = "", string mail = "", string mailPass = "") =>
        new(n, acc, pass, mail, mailPass);
}
