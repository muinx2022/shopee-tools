using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>GĐ8: trường mới của shop (<c>Source.WorkbookPath</c>, <c>TemplatePaths</c>, <c>Settings.ExportDirectory</c>) lưu / đọc lại;
/// kiểm khi phần trình duyệt bị ẩn (<c>browserChecks: false</c>) không báo gì về proxy / đăng nhập / profile / nhịp chạy nhưng vẫn
/// giữ nguyên dữ liệu các phần đó.</summary>
public sealed class SellerExportFieldsTests : IDisposable
{
    private readonly string _dir = SellerTestData.NewTempDir();
    private readonly SellerAccountStore _store;

    public SellerExportFieldsTests() =>
        _store = new SellerAccountStore(Path.Combine(_dir, "seller-accounts.json"), Path.Combine(_dir, "profiles"));

    public void Dispose() => SellerTestData.TryDelete(_dir);

    /// <summary>Dữ liệu có đủ lỗi / cảnh báo của phần ẩn: proxy tĩnh sai, key random, mật khẩu thiếu tài khoản, profile tương đối,
    /// nhịp chạy sai, trần ngày 0, 2 shop chung IP tĩnh, có shop đi proxy chung mà chưa có proxy chung.</summary>
    private static SellerAccountsData HiddenProblems() => new()
    {
        Settings = new SellerUploadSettings
        {
            Pacing = new SellerPacing { PauseBetweenShopsMinMinutes = 1 },
            SizeChartImagePath = @"D:\anh\size.jpg",
            ExportDirectory = @"D:\xuat",
        },
        Accounts =
        [
            new SellerAccount
            {
                Id = "shop-1", Label = "Một", ShopeePassword = "matkhau", ProfileDir = "tuong-doi", DailyProductCap = 0,
                ProxyOverride = new SellerProxyOverride { Kind = SellerProxyKind.Static, Static = "khong-phai-proxy" },
                Source = new SellerSource { WorkbookPath = @"D:\nguon.xlsx", Sheet = "Áo", FromRow = 2, ToRow = 50 },
                TemplatePaths = [@"D:\mau\100009.xlsx", @"D:\mau\100011.xlsx"],
            },
            new SellerAccount { Id = "shop-2", ProxyOverride = new SellerProxyOverride { Kind = SellerProxyKind.Static, Static = "1.2.3.4:80" } },
            new SellerAccount { Id = "shop-3", ProxyOverride = new SellerProxyOverride { Kind = SellerProxyKind.Static, Static = "1.2.3.4:80" } },
            new SellerAccount { Id = "shop-4", VerifyEmail = "a@b.c" },
        ],
    };

    [Fact]
    public void AnPhanTrinhDuyet_KhongLoiKhongCanhBao_LuuDuoc_GiuDuLieuAn()
    {
        var data = HiddenProblems();
        var (fullErrors, fullWarnings) = _store.Validate(data);
        Assert.NotEmpty(fullErrors);
        Assert.NotEmpty(fullWarnings);

        var (errors, warnings) = _store.Validate(data, browserChecks: false);
        Assert.Empty(errors);
        Assert.Empty(warnings);
        Assert.False(_store.Save(data).Saved);
        var r = _store.Save(data, browserChecks: false);
        Assert.True(r.Saved, string.Join("; ", r.Errors));

        var d = _store.Load();
        var a = d.Accounts[0];
        Assert.Equal(@"D:\nguon.xlsx", a.Source.WorkbookPath);
        Assert.Equal([@"D:\mau\100009.xlsx", @"D:\mau\100011.xlsx"], a.TemplatePaths);
        Assert.Equal(@"D:\xuat", d.Settings.ExportDirectory);
        Assert.Equal(("matkhau", "tuong-doi", 0, "khong-phai-proxy"), (a.ShopeePassword, a.ProfileDir, a.DailyProductCap, a.ProxyOverride!.Static));
        Assert.Equal(1, d.Settings.Pacing.PauseBetweenShopsMinMinutes);
        Assert.Equal("a@b.c", d.Accounts[3].VerifyEmail);
    }

    [Fact]
    public void AnPhanTrinhDuyet_VanKiemMaVaGiaTriMacDinh()
    {
        var data = new SellerAccountsData
        {
            Accounts =
            [
                new SellerAccount { Id = "x y" },
                new SellerAccount { Id = "shop-1", Label = "Một", Defaults = new SellerDefaults { WeightGrams = 0 } },
                new SellerAccount { Id = "SHOP-1" },
            ],
        };

        var (errors, warnings) = _store.Validate(data, browserChecks: false);

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.Contains("'x y' không hợp lệ", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("Một (shop-1): Cân nặng", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("Trùng mã", StringComparison.Ordinal));
        Assert.Empty(warnings);
    }

    [Fact]
    public void FileCuKhongCoTruongMoi_DocDuoc_LuuLaiKhongSinhTruongRong()
    {
        File.WriteAllText(_store.FilePath, """{"Version":1,"Accounts":[{"Id":"shop-1","Source":{"Sheet":"A"}}]}""");

        var d = _store.Load();

        Assert.Null(d.Accounts[0].Source.WorkbookPath);
        Assert.Null(d.Accounts[0].TemplatePaths);
        Assert.Null(d.Settings.ExportDirectory);
        Assert.True(_store.Save(d, browserChecks: false).Saved);
        var json = File.ReadAllText(_store.FilePath);
        Assert.DoesNotContain("WorkbookPath", json);
        Assert.DoesNotContain("TemplatePaths", json);
        Assert.DoesNotContain("ExportDirectory", json);
    }
}
