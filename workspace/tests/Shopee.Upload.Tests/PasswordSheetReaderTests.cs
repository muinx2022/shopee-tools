using ClosedXML.Excel;
using Shopee.Upload.Import;

namespace Shopee.Upload.Tests;

/// <summary>Đọc file mật khẩu (GĐ5c mục 2): tìm cột theo tên, khớp CHÍNH XÁC, đọc chữ hiển thị, giữ nguyên mật khẩu.</summary>
public sealed class PasswordSheetReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "upload-pwsheet-tests-" + Guid.NewGuid().ToString("N"));

    public PasswordSheetReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void ImportText_Key_HaChu_BoDau_GopKhoangTrang()
    {
        Assert.Equal("pass mail", ImportText.Key("  Pass \n Mail "));
        Assert.Equal("tai khoan", ImportText.Key("Tài Khoản"));
        Assert.Equal("dong ho dep", ImportText.Key("ĐỒNG HỒ  Đẹp"));
        Assert.Equal("", ImportText.Key("   "));
        Assert.False(ImportText.SameKey("", "  "));
    }

    [Fact]
    public void TimHangTieuDe_CotXenKe_KhopChinhXacMailVaPassMail()
    {
        var sheet = PasswordSheetReader.Read(ImportFixtures.WriteSample(_dir));

        Assert.Equal("Trang tính1", sheet.SheetName);
        Assert.Equal(3, sheet.HeaderRow);
        Assert.Equal(3, sheet.Rows.Count);   // dòng trống bị bỏ
        var a = sheet.Rows[0];
        Assert.Equal(4, a.RowNumber);
        Assert.Equal("shop-a.login", a.Acc);                 // ACC trim
        Assert.Equal(ImportFixtures.PassA, a.Pass);
        Assert.Equal("a@hotmail.com", a.Mail);               // email trim, lấy đúng cột "Mail" chứ không phải "Pass  Mail"
        Assert.Equal(ImportFixtures.MailPassA, a.MailPass);  // "Pass  Mail" không bị nhầm thành cột Pass
        Assert.Equal("Shop Bé Xinh", sheet.Rows[1].Acc);
    }

    [Fact]
    public void MatKhau_GiuNguyenTungKyTu_SoKhongThanhDangMu_KhongMatSo0()
    {
        var path = Path.Combine(_dir, "so.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("S");
            ImportFixtures.Row(ws, 1, "ACC", "Pass", "mail", "pass Mail");
            ws.Cell(2, 1).Value = "acc-1";
            ws.Cell(2, 2).Value = 123456789012d;                 // số dài, General → không được thành 1.23457E+11
            ws.Cell(2, 3).Value = "m1@x.vn";
            ws.Cell(2, 4).Value = 120000d;                       // không được thành 1.2E+5
            ws.Cell(3, 1).Value = "acc-2";
            ws.Cell(3, 2).Value = 1234d;
            ws.Cell(3, 2).Style.NumberFormat.Format = "000000";  // hiển thị 001234 → giữ số 0 đầu
            ws.Cell(3, 3).Value = "m2@x.vn";
            ws.Cell(3, 4).Value = "  a b ";                     // dấu cách đầu/cuối là một phần mật khẩu
            ws.Cell(4, 1).Value = "acc-3";
            ws.Cell(4, 2).Value = "007";                         // ô chữ
            ws.Cell(4, 4).Value = 12.5d;
            ws.Cell(5, 1).Value = " acc-4 ";
            ws.Cell(5, 2).Value = "  Mk có cách	 ";        // mật khẩu Shopee có dấu cách / tab đầu cuối
            wb.SaveAs(path);
        }

        var rows = PasswordSheetReader.Read(path).Rows;

        Assert.Equal("123456789012", rows[0].Pass);
        Assert.Equal("120000", rows[0].MailPass);
        Assert.Equal("001234", rows[1].Pass);
        Assert.Equal("  a b ", rows[1].MailPass);
        Assert.Equal("007", rows[2].Pass);
        Assert.Equal("12.5", rows[2].MailPass);
        Assert.Equal("", rows[2].Mail);
        Assert.Equal("acc-4", rows[3].Acc);
        Assert.Equal("  Mk có cách	 ", rows[3].Pass);
    }

    [Theory]
    [InlineData("vi-VN")]
    [InlineData("en-US")]
    public void OSoCoDinhDangRieng_DocSoNguyenInvariant_GiuSo0DemDau(string culture)
    {
        var path = Path.Combine(_dir, $"fmt-{culture}.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("S");
            ImportFixtures.Row(ws, 1, "ACC", "Pass", "mail", "pass Mail");
            ws.Cell(2, 1).Value = "acc-1";
            ws.Cell(2, 2).Value = 1234567d;
            ws.Cell(2, 2).Style.NumberFormat.Format = "#,##0";      // vi-VN hiện 1.234.567, en-US 1,234,567
            ws.Cell(2, 4).Value = 9876543d;
            ws.Cell(2, 4).Style.NumberFormat.NumberFormatId = 4;    // dựng sẵn "#,##0.00"
            ws.Cell(3, 1).Value = "acc-2";
            ws.Cell(3, 2).Value = 42d;
            ws.Cell(3, 2).Style.NumberFormat.Format = "000000";     // đệm 0 → 000042
            ws.Cell(3, 4).Value = 5000d;
            ws.Cell(3, 4).Style.NumberFormat.Format = "0.00";       // không được thành 5000,00 / 5000.00
            wb.SaveAs(path);
        }

        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            var rows = PasswordSheetReader.Read(path).Rows;
            Assert.Equal("1234567", rows[0].Pass);
            Assert.Equal("9876543", rows[0].MailPass);
            Assert.Equal("000042", rows[1].Pass);
            Assert.Equal("5000", rows[1].MailPass);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = old;
        }
    }

    [Fact]
    public void ThieuCot_BaoRoTenCot_KhongCoGiaTriO()
    {
        var path = Path.Combine(_dir, "thieu.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("Trang tính1");
            ImportFixtures.Row(ws, 1, "ACC", "Pass", "mail");
            ImportFixtures.Row(ws, 2, "acc-1", "BiMat#Thieu1", "x@y.vn");
            wb.SaveAs(path);
        }

        var ex = Assert.Throws<PasswordFileException>(() => PasswordSheetReader.Read(path));

        Assert.Contains("thiếu cột pass Mail", ex.Message);
        Assert.DoesNotContain("BiMat#Thieu1", ex.Message);
    }

    [Fact]
    public void ChiCoCotPassMail_KhongDuocCoiLaCotMail()
    {
        // Chỉ có "pass Mail", không có "mail": khớp kiểu contains sẽ lấy "pass Mail" làm cột mail và coi như đủ cột.
        var path = Path.Combine(_dir, "khongmail.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("S");
            ImportFixtures.Row(ws, 1, "ACC", "Pass", "pass Mail", "SĐT");
            ImportFixtures.Row(ws, 2, "acc-1", "p1", "mp1", "0900");
            wb.SaveAs(path);
        }

        var ex = Assert.Throws<PasswordFileException>(() => PasswordSheetReader.Read(path));
        Assert.Contains("thiếu cột mail", ex.Message);
    }

    [Fact]
    public void CotLap_BaoLoi_KhongDoan()
    {
        var path = Path.Combine(_dir, "lap.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("S");
            ImportFixtures.Row(ws, 1, "ACC", "Pass", "mail", "pass Mail", "PASS");
            ImportFixtures.Row(ws, 2, "acc-1", "p1", "m@x.vn", "mp1", "p2");
            wb.SaveAs(path);
        }

        var ex = Assert.Throws<PasswordFileException>(() => PasswordSheetReader.Read(path));
        Assert.Contains("cột lặp Pass", ex.Message);
    }

    [Fact]
    public void HangTieuDeQuaSau_KhongTimThay()
    {
        var path = Path.Combine(_dir, "sau.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("S");
            ImportFixtures.Row(ws, PasswordSheetReader.HeaderScanRows + 1, "ACC", "Pass", "mail", "pass Mail");
            wb.SaveAs(path);
        }

        Assert.Throws<PasswordFileException>(() => PasswordSheetReader.Read(path));
    }

    [Fact]
    public void OLoi_VaOChiCoKhoangTrang_BoCaDong_BaoSoDong_MailKhoangTrangLaTrong()
    {
        var path = Path.Combine(_dir, "loi.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("S");
            ImportFixtures.Row(ws, 1, "ACC", "Pass", "mail", "pass Mail");
            ImportFixtures.Row(ws, 2, "ok-1", "Pass#Ok1", "a@x.vn", "Mail#Ok1");
            ImportFixtures.Row(ws, 3, "na", "x", "b@x.vn", "y");
            ws.Cell(3, 2).Value = XLError.NoValueAvailable;              // #N/A ở Pass
            ImportFixtures.Row(ws, 4, "div", "x", "c@x.vn", "y");
            ws.Cell(4, 4).FormulaA1 = "1/0";                             // #DIV/0! ở pass Mail
            ImportFixtures.Row(ws, 5, "name", "x", "d@x.vn", "y");
            ws.Cell(5, 3).Value = XLError.NameNotRecognized;             // #NAME? ở mail
            ImportFixtures.Row(ws, 6, "space-pass", "x", "e@x.vn", "y");
            ws.Cell(6, 2).Value = "   ";                                 // Pass chỉ khoảng trắng
            ImportFixtures.Row(ws, 7, "x", "Pass#7", "f@x.vn", "Mail#7");
            ws.Cell(7, 1).Value = " ";                                   // ACC chỉ khoảng trắng
            ImportFixtures.Row(ws, 8, "space-mailpass", "Pass#8", "g@x.vn", "x");
            ws.Cell(8, 4).Value = "	 ";                                // pass Mail chỉ khoảng trắng
            ImportFixtures.Row(ws, 9, "space-mail", "Pass#9", "x", "Mail#9");
            ws.Cell(9, 3).Value = "  ";                                  // mail chỉ khoảng trắng → trống, giữ dòng
            ImportFixtures.Row(ws, 10, "vong", "x", "h@x.vn", "y");
            ws.Cell(10, 2).FormulaA1 = "B10";                            // công thức vòng tròn
            ImportFixtures.Row(ws, 11, "ngay", "Pass#11", "i@x.vn", "x");
            ws.Cell(11, 4).Value = new DateTime(2026, 1, 2);             // ô ngày: không ném
            wb.SaveAs(path);
        }

        var sheet = PasswordSheetReader.Read(path);

        Assert.Equal([3, 4, 5, 6, 7, 8, 10], sheet.BadRows);
        Assert.Equal(["ok-1", "space-mail", "ngay"], sheet.Rows.Select(r => r.Acc));
        Assert.Equal("", sheet.Rows[1].Mail);
        Assert.NotEqual("", sheet.Rows[2].MailPass);

        var text = PasswordImport.Summary("f.xlsx", sheet, PasswordImport.Plan(sheet.Rows, []), null);
        Assert.Contains("Bỏ 7 dòng có ô lỗi hoặc ô chỉ có khoảng trắng", text);
        Assert.Contains("dòng 3, 4, 5, 6, 7, 8, 10", text);
    }

    [Fact]
    public void SheetAnDungTruoc_UuTienSheetHien_ChiDungSheetAnKhiKhongCoSheetHien()
    {
        var path = Path.Combine(_dir, "an.xlsx");
        using (var wb = new XLWorkbook())
        {
            var hidden = wb.AddWorksheet("Cu (an)");
            ImportFixtures.Row(hidden, 1, "ACC", "Pass", "mail", "pass Mail");
            ImportFixtures.Row(hidden, 2, "acc-cu", "Pass#Cu", "cu@x.vn", "m");
            hidden.Visibility = XLWorksheetVisibility.Hidden;
            var shown = wb.AddWorksheet("Moi");
            ImportFixtures.Row(shown, 1, "ACC", "Pass", "mail", "pass Mail");
            ImportFixtures.Row(shown, 2, "acc-moi", "Pass#Moi", "moi@x.vn", "m");
            wb.SaveAs(path);
        }
        var sheet = PasswordSheetReader.Read(path);
        Assert.Equal("Moi", sheet.SheetName);
        Assert.False(sheet.Hidden);

        var path2 = Path.Combine(_dir, "an2.xlsx");
        using (var wb = new XLWorkbook())
        {
            var shown = wb.AddWorksheet("Khac");
            ImportFixtures.Row(shown, 1, "Ghi chú");
            var hidden = wb.AddWorksheet("Chi an");
            ImportFixtures.Row(hidden, 1, "ACC", "Pass", "mail", "pass Mail");
            ImportFixtures.Row(hidden, 2, "acc-an", "Pass#An", "an@x.vn", "m");
            hidden.Visibility = XLWorksheetVisibility.Hidden;
            wb.SaveAs(path2);
        }
        var s2 = PasswordSheetReader.Read(path2);
        Assert.Equal("Chi an", s2.SheetName);
        Assert.True(s2.Hidden);
        Assert.Contains("đang đọc sheet ẩn \"Chi an\"", PasswordImport.Summary("f.xlsx", s2, PasswordImport.Plan(s2.Rows, []), null));
    }

    [Fact]
    public void FileKhongPhaiXlsx_BaoLoi()
    {
        var path = Path.Combine(_dir, "rac.xlsx");
        File.WriteAllText(path, "khong phai excel");
        Assert.Throws<PasswordFileException>(() => PasswordSheetReader.Read(path));
    }

    [Fact]
    public void PasswordRow_ToString_KhongCoMatKhau()
    {
        var r = new PasswordRow(5, "acc-x", "Mk#Secret01", "m@x.vn", "Mail#Secret02");
        Assert.DoesNotContain("Mk#Secret01", r.ToString());
        Assert.DoesNotContain("Mail#Secret02", r.ToString());
        Assert.Contains("acc-x", r.ToString());
    }
}
