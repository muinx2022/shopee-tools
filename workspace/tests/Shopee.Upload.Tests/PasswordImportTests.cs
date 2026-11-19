using Shopee.Upload.Forms;
using Shopee.Upload.Import;
using static Shopee.Upload.Tests.ImportFixtures;

namespace Shopee.Upload.Tests;

/// <summary>Khớp dòng file mật khẩu với tài khoản trên form + điền (GĐ5c mục 2) — hàm thuần.</summary>
public class PasswordImportTests
{
    private static AccountForm Form(string id, string label, string login = "", string pass = "", string mail = "", string mailPass = "")
    {
        var f = SellerFormMapper.NewAccountForm(id);
        f.Label = label;
        f.ShopeeLogin = login;
        f.ShopeePassword = pass;
        f.VerifyEmail = mail;
        f.VerifyEmailPassword = mailPass;
        return f;
    }

    [Fact]
    public void KhopTheoTaiKhoanShopeeTruoc_RoiTheoNhan_BoDauHoaThuong()
    {
        var byLogin = Form("seller-1", "Tên khác hẳn", login: "SHOP-A.Login");
        var byLabel = Form("seller-2", "  shop be xinh ");
        var none = Form("seller-3", "Không có trong file", login: "zzz");
        var rows = new[] { PRow(4, "shop-a.login", "p1", "a@x.vn", "mp1"), PRow(5, "Shop Bé Xinh", "p2", "b@x.vn", "mp2"), PRow(7, "le-c", "p3") };

        var plan = PasswordImport.Plan(rows, [byLogin, byLabel, none]);

        Assert.Equal(2, plan.Matches.Count);
        Assert.Contains(plan.Matches, m => m.Form == byLogin && m.Row.RowNumber == 4 && m.ByLogin);
        Assert.Contains(plan.Matches, m => m.Form == byLabel && m.Row.RowNumber == 5 && !m.ByLogin);
        Assert.Equal(["Không có trong file"], plan.NotFound);
        Assert.Equal(1, plan.UnmatchedRows);
        Assert.Equal(3, plan.TotalRows);
    }

    [Fact]
    public void KhopQuaNhan_MaFormDaCoTaiKhoanShopeeKhac_BoQua_KhongDienOChoNao()
    {
        // Form giữ tài khoản cũ 0912345678 (không có trong file); nhãn trùng ACC fflqam22bl → KHÔNG được ghép mật khẩu / email của
        // fflqam22bl vào tài khoản 0912345678, dù chọn chỉ điền ô trống hay ghi đè.
        foreach (var choice in new[] { OverwriteChoice.FillEmptyOnly, OverwriteChoice.OverwriteAll })
        {
            var f = Form("seller-1", "fflqam22bl", login: "0912345678");
            var rows = new[] { PRow(5, "fflqam22bl", "Pass#Khac01", "khac@x.vn", "Mail#Khac01") };
            var plan = PasswordImport.Plan(rows, [f]);
            var r = PasswordImport.Apply(plan, choice);

            Assert.Empty(plan.Matches);
            Assert.Equal(["fflqam22bl"], plan.LoginMismatch);
            Assert.Equal(("0912345678", "", "", ""), (f.ShopeeLogin, f.ShopeePassword, f.VerifyEmail, f.VerifyEmailPassword));
            Assert.Equal(0, plan.UnmatchedRows);   // dòng đã được "nhận" (và báo) — không đếm thêm là dòng lạ
            var text = PasswordImport.Summary("f.xlsx", new PasswordSheet("S", 1, rows), plan, r);
            Assert.Contains("Bỏ qua — tài khoản Shopee trên form khác file", text);
            Assert.Contains("(fflqam22bl)", text);
            Assert.DoesNotContain("Pass#Khac01", text);
        }
    }

    [Fact]
    public void KhopQuaNhan_FormChuaCoTaiKhoanShopee_VanDien()
    {
        var f = Form("seller-1", "Shop Bé Xinh");
        var plan = PasswordImport.Plan([PRow(5, "Shop Bé Xinh", "p2")], [f]);
        var m = Assert.Single(plan.Matches);
        Assert.False(m.ByLogin);
        Assert.Empty(plan.LoginMismatch);
    }

    [Fact]
    public void LoginUuTienHonNhan_KhiCaHaiDeuCoDongRieng()
    {
        // A: login = ACC X (dòng 2), nhãn = ACC Y (dòng 3). Phải nhận dòng X; dòng Y không khớp ai.
        var a = Form("seller-1", "acc-y", login: "acc-x");
        var plan = PasswordImport.Plan([PRow(2, "acc-x", "PassX#01"), PRow(3, "acc-y", "PassY#01")], [a]);

        var m = Assert.Single(plan.Matches);
        Assert.Equal(2, m.Row.RowNumber);
        Assert.True(m.ByLogin);
        PasswordImport.Apply(plan, OverwriteChoice.FillEmptyOnly);
        Assert.Equal("PassX#01", a.ShopeePassword);
        Assert.Equal(1, plan.UnmatchedRows);
    }

    [Fact]
    public void DienVaoForm_KhongDungTaiKhoanKhongKhop_KhongXoaKhiFileTrong()
    {
        var a = Form("seller-1", "Shop A");
        var other = Form("seller-2", "Shop khác", login: "giu-nguyen", pass: "GiuNguyen#1", mail: "g@x.vn", mailPass: "GiuMail#1");
        var b = Form("seller-3", "Shop B", pass: "CoSan#123");
        var rows = new[] { PRow(2, "Shop A", "  p a ", "a@x.vn", "mpA"), PRow(3, "Shop B", "", "b@x.vn", "") };

        var plan = PasswordImport.Plan(rows, [a, other, b]);
        var r = PasswordImport.Apply(plan, OverwriteChoice.FillEmptyOnly);

        Assert.Equal("Shop A", a.ShopeeLogin);
        Assert.Equal("  p a ", a.ShopeePassword);   // không trim mật khẩu
        Assert.Equal("a@x.vn", a.VerifyEmail);
        Assert.Equal("mpA", a.VerifyEmailPassword);
        Assert.Equal("CoSan#123", b.ShopeePassword); // file trống → không xoá
        Assert.Equal("", b.VerifyEmailPassword);
        Assert.Equal("b@x.vn", b.VerifyEmail);
        Assert.Equal(("giu-nguyen", "GiuNguyen#1", "g@x.vn", "GiuMail#1"),
            (other.ShopeeLogin, other.ShopeePassword, other.VerifyEmail, other.VerifyEmailPassword));
        Assert.Equal(["Shop A", "Shop B"], r.Filled);
    }

    [Fact]
    public void ChiDienOTrong_KhongGhiDe_GhiDeTatCa_ThiGhiDe()
    {
        AccountForm Make() => Form("seller-1", "Shop A", pass: "Cu#Pass01", mail: "", mailPass: "Cu#Mail01");
        var rows = new[] { PRow(2, "Shop A", "Moi#Pass02", "moi@x.vn", "Moi#Mail02") };

        var keep = Make();
        var plan = PasswordImport.Plan(rows, [keep]);
        Assert.Equal(1, plan.ConflictAccounts);
        Assert.Equal(2, plan.ConflictFields);   // 2 mật khẩu khác; login + email đang trống
        var r = PasswordImport.Apply(plan, OverwriteChoice.FillEmptyOnly);
        Assert.Equal("Cu#Pass01", keep.ShopeePassword);
        Assert.Equal("Cu#Mail01", keep.VerifyEmailPassword);
        Assert.Equal("moi@x.vn", keep.VerifyEmail);
        Assert.Equal("Shop A", keep.ShopeeLogin);
        Assert.Equal(2, r.FieldsKept);
        Assert.Equal(2, r.FieldsWritten);

        var over = Make();
        PasswordImport.Apply(PasswordImport.Plan(rows, [over]), OverwriteChoice.OverwriteAll);
        Assert.Equal("Moi#Pass02", over.ShopeePassword);
        Assert.Equal("Moi#Mail02", over.VerifyEmailPassword);

        var cancel = Make();
        var rc = PasswordImport.Apply(PasswordImport.Plan(rows, [cancel]), OverwriteChoice.Cancel);
        Assert.Equal("Cu#Pass01", cancel.ShopeePassword);
        Assert.Equal("", cancel.VerifyEmail);
        Assert.Equal(0, rc.FieldsWritten);
    }

    [Fact]
    public void FileTrong_GhiDeTatCa_VanKhongXoaOTrenForm_CaBonTruong()
    {
        // Dòng có đủ 4 ô trống (kể cả ACC — dựng tay vì Reader bỏ dòng ACC trống) + chọn GHI ĐÈ TẤT CẢ: không ô nào bị xoá.
        var f = Form("seller-1", "Shop A", login: "login-cu", pass: "Pass#Cu01", mail: "cu@x.vn", mailPass: "Mail#Cu01");
        var plan = new PasswordImportPlan();
        plan.Matches.Add(new PasswordMatch(f, PRow(2, "", "", "", ""), ByLogin: false));

        Assert.Empty(PasswordImport.Changes(plan.Matches[0]));
        var r = PasswordImport.Apply(plan, OverwriteChoice.OverwriteAll);

        Assert.Equal(("login-cu", "Pass#Cu01", "cu@x.vn", "Mail#Cu01"), (f.ShopeeLogin, f.ShopeePassword, f.VerifyEmail, f.VerifyEmailPassword));
        Assert.Equal(0, r.FieldsWritten);
        Assert.Equal(["Shop A"], r.Unchanged);
    }

    [Fact]
    public void FileTrongTungO_GhiDeTatCa_ChiGhiOCoGiaTri()
    {
        // Mỗi lần chỉ một ô trong file có giá trị: 3 ô còn lại (trống trong file) phải giữ nguyên dù Ghi đè tất cả.
        var rows = new (PasswordRow Row, string Field)[]
        {
            (PRow(2, "login-moi"), "login"),
            (PRow(2, "", "Pass#Moi02"), "pass"),
            (PRow(2, "", "", "moi@x.vn"), "mail"),
            (PRow(2, "", "", "", "Mail#Moi02"), "mailpass"),
        };
        foreach (var (row, field) in rows)
        {
            var f = Form("seller-1", "Shop A", login: "login-cu", pass: "Pass#Cu01", mail: "cu@x.vn", mailPass: "Mail#Cu01");
            var plan = new PasswordImportPlan();
            plan.Matches.Add(new PasswordMatch(f, row, ByLogin: false));
            PasswordImport.Apply(plan, OverwriteChoice.OverwriteAll);

            Assert.Equal(field == "login" ? "login-moi" : "login-cu", f.ShopeeLogin);
            Assert.Equal(field == "pass" ? "Pass#Moi02" : "Pass#Cu01", f.ShopeePassword);
            Assert.Equal(field == "mail" ? "moi@x.vn" : "cu@x.vn", f.VerifyEmail);
            Assert.Equal(field == "mailpass" ? "Mail#Moi02" : "Mail#Cu01", f.VerifyEmailPassword);
        }
    }

    [Fact]
    public void TomTat_TaiKhoanBiGiuO_LaNhomRieng_KhongPhaiKhongCoGiDeDoi()
    {
        var keep = Form("seller-1", "Shop Giu", login: "Shop Giu", pass: "Pass#Cu01");      // chỉ có ô khác → giữ
        var same = Form("seller-2", "Shop Giong", login: "Shop Giong", pass: "Pass#Same1"); // đã giống hệt
        var rows = new[] { PRow(2, "Shop Giu", "Pass#Moi02"), PRow(3, "Shop Giong", "Pass#Same1") };
        var plan = PasswordImport.Plan(rows, [keep, same]);
        var r = PasswordImport.Apply(plan, OverwriteChoice.FillEmptyOnly);

        Assert.Equal(["Shop Giu"], r.Kept);
        Assert.Equal(["Shop Giong"], r.Unchanged);
        Assert.Empty(r.Filled);
        var text = PasswordImport.Summary("f.xlsx", new PasswordSheet("S", 1, rows), plan, r);
        Assert.Contains("Giữ nguyên (không ghi đè): 1 tài khoản (Shop Giu)", text);
        Assert.Contains("Khớp nhưng không có gì để đổi: 1 tài khoản (Shop Giong)", text);
        Assert.DoesNotContain("Pass#Moi02", text);
    }

    [Fact]
    public void AccLapTrongFile_KhongDien_BaoRo()
    {
        var a = Form("seller-1", "Shop A");
        var rows = new[] { PRow(2, "Shop A", "p1"), PRow(9, "shop a", "p2"), PRow(10, "Shop C", "p3") };

        var plan = PasswordImport.Plan(rows, [a]);
        PasswordImport.Apply(plan, OverwriteChoice.OverwriteAll);

        Assert.Empty(plan.Matches);
        Assert.Equal("", a.ShopeePassword);
        var d = Assert.Single(plan.DuplicateAccs);
        Assert.Equal([2, 9], d.Rows);
        Assert.Contains(plan.Ambiguous, x => x.Contains("Shop A") && x.Contains("2, 9"));
    }

    [Fact]
    public void MotDongKhopHaiTaiKhoan_KhongChonBua()
    {
        var a = Form("seller-1", "Shop A");
        var a2 = Form("seller-2", "Khác", login: "shop a");
        var plan = PasswordImport.Plan([PRow(2, "Shop A", "p1")], [a, a2]);
        PasswordImport.Apply(plan, OverwriteChoice.OverwriteAll);

        Assert.Empty(plan.Matches);
        Assert.Equal("", a.ShopeePassword);
        Assert.Equal("", a2.ShopeePassword);
        Assert.Single(plan.Ambiguous);
    }

    [Fact]
    public void TomTat_KhongCoMatKhau_KeCaODuocGiuNguyen()
    {
        var a = Form("seller-1", "Shop A", pass: "Cu#Pass01");
        var b = Form("seller-2", "Không thấy");
        var rows = new[] { PRow(2, "Shop A", PassA, "a@x.vn", MailPassA), PRow(3, "x", PassUnmatched, "", MailPassB) };
        var plan = PasswordImport.Plan(rows, [a, b]);
        var result = PasswordImport.Apply(plan, OverwriteChoice.FillEmptyOnly);

        var text = PasswordImport.Summary("f.xlsx", new PasswordSheet("S", 1, rows), plan, result)
                   + string.Join("", plan.Matches) + string.Join("", plan.Matches.SelectMany(PasswordImport.Changes));

        foreach (var secret in new[] { PassA, MailPassA, PassUnmatched, MailPassB, "Cu#Pass01" })
            Assert.DoesNotContain(secret, text);
        Assert.Contains("Đã điền 1 tài khoản", text);
        Assert.Contains("Không thấy", text);
        Assert.Contains("không khớp tài khoản nào: 1", text);
    }
}
