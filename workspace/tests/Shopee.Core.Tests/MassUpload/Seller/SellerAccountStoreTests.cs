using Shopee.Core.MassUpload;
using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>GĐ4: <see cref="SellerAccountStore"/> — lưu/đọc lại, ghi nguyên tử, lỗi chặn lưu, cảnh báo trùng proxy (không chặn),
/// giá trị mặc định → <see cref="MassUploadDefaults"/>.</summary>
public sealed class SellerAccountStoreTests : IDisposable
{
    private readonly string _dir = SellerTestData.NewTempDir();
    private readonly SellerAccountStore _store;

    public SellerAccountStoreTests() =>
        _store = new SellerAccountStore(Path.Combine(_dir, "seller-accounts.json"), Path.Combine(_dir, "profiles"));

    public void Dispose() => SellerTestData.TryDelete(_dir);

    private static SellerAccountsData Sample() => new()
    {
        Settings = new SellerUploadSettings
        {
            SharedProxy = new KiotProxySettings { KiotKey = "SHARED-0001", Region = "bac", Type = ProxyScheme.Http },
            SizeChartImagePath = @"D:\images\1.jpeg",
            Pacing = new SellerPacing { PauseBetweenShopsMinMinutes = 12, ActiveToHour = 21 },
        },
        Accounts =
        [
            new SellerAccount
            {
                Id = "shop-01", Label = "Shop Một",
                Defaults = new SellerDefaults { StockPerVariation = 50, WeightGrams = 300, LengthCm = 20, WidthCm = 15, HeightCm = 5, ProductsPerFile = 25 },
                Source = new SellerSource { BigSellerAccount = "bs1", Sheet = "Áo", FromRow = 2, ToRow = 200 },
                DailyProductCap = 80,
            },
            new SellerAccount
            {
                Id = "shop_02", Label = "Shop Hai", Enabled = false,
                ProxyOverride = new SellerProxyOverride { Kind = SellerProxyKind.Static, Static = "45.1.2.3:8080:u:p" },
            },
            new SellerAccount
            {
                Id = "shop-03",
                ProxyOverride = new SellerProxyOverride { Kind = SellerProxyKind.Kiot, KiotKey = "OWN-0003", Region = "nam", KiotKeepIp = true, Type = ProxyScheme.Socks5 },
            },
        ],
    };

    [Fact]
    public void LuuRoiDocLai_GiuNguyen()
    {
        var res = _store.Save(Sample());
        Assert.True(res.Saved, string.Join("; ", res.Errors));
        Assert.Empty(res.Warnings);

        var d = new SellerAccountStore(_store.FilePath, Path.Combine(_dir, "profiles")).Load();
        Assert.Equal("SHARED-0001", d.Settings.SharedProxy.KiotKey);
        Assert.Equal(12, d.Settings.Pacing.PauseBetweenShopsMinMinutes);
        Assert.Equal(21, d.Settings.Pacing.ActiveToHour);
        Assert.Equal(@"D:\images\1.jpeg", d.Settings.SizeChartImagePath);
        Assert.Equal(3, d.Accounts.Count);
        var a = d.Accounts[0];
        Assert.Equal("Shop Một", a.Label);
        Assert.Null(a.ProxyOverride);
        Assert.Equal(300, a.Defaults.WeightGrams);
        Assert.Equal("Áo", a.Source.Sheet);
        Assert.Equal(200, a.Source.ToRow);
        Assert.Equal(80, a.DailyProductCap);
        Assert.False(d.Accounts[1].Enabled);
        Assert.Equal("45.1.2.3:8080:u:p", d.Accounts[1].ProxyOverride!.Static);
        Assert.Equal(SellerProxyKind.Kiot, d.Accounts[2].ProxyOverride!.Kind);
        Assert.True(d.Accounts[2].ProxyOverride!.KiotKeepIp);
        Assert.Equal(ProxyScheme.Socks5, d.Accounts[2].ProxyOverride!.Type);

        // Enum ghi bằng chữ, không có mật khẩu Shopee nào trong mô hình.
        var json = File.ReadAllText(_store.FilePath);
        Assert.Contains("\"Static\"", json);
        Assert.Contains("\"Socks5\"", json);
        Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GhiNguyenTu_KhongDeFileTam_GhiDeBanCu()
    {
        Assert.True(_store.Save(Sample()).Saved);
        var d = _store.Load();
        d.Accounts[0].Label = "Đổi tên";
        Assert.True(_store.Save(d).Saved);

        Assert.Equal("Đổi tên", _store.Load().Accounts[0].Label);
        Assert.Equal([Path.GetFileName(_store.FilePath)], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void CoLoi_KhongGhi_FileCuGiuNguyen()
    {
        Assert.True(_store.Save(Sample()).Saved);
        var before = File.ReadAllBytes(_store.FilePath);

        var bad = Sample();
        bad.Accounts.Add(new SellerAccount { Id = "shop-01" });                 // trùng mã
        bad.Accounts.Add(new SellerAccount { Id = "bad id/.." });                // mã sai
        bad.Accounts.Add(new SellerAccount { Id = "x", ProxyOverride = new() { Kind = SellerProxyKind.Static, Static = "1.2.3.4" } });
        bad.Accounts.Add(new SellerAccount { Id = "y", Defaults = new SellerDefaults { WeightGrams = 0, ProductsPerFile = 500 } });
        bad.Settings.Pacing.PauseBetweenShopsMinMinutes = 3;

        var res = _store.Save(bad);

        Assert.False(res.Saved);
        Assert.Contains(res.Errors, e => e.Contains("Trùng mã"));
        Assert.Contains(res.Errors, e => e.Contains("không hợp lệ"));
        Assert.Contains(res.Errors, e => e.Contains("proxy tĩnh sai"));
        Assert.Contains(res.Errors, e => e.Contains("Cân nặng"));
        Assert.Contains(res.Errors, e => e.Contains("Số SP mỗi file"));
        Assert.Contains(res.Errors, e => e.Contains("≥ 5 phút"));
        Assert.Equal(before, File.ReadAllBytes(_store.FilePath));
    }

    [Fact]
    public void TrungProxy_CanhBao_VanLuu()
    {
        var d = Sample();
        d.Accounts.Add(new SellerAccount { Id = "shop-04", ProxyOverride = new() { Kind = SellerProxyKind.Kiot, KiotKey = "OWN-0003", Region = "nam" } });
        d.Accounts.Add(new SellerAccount { Id = "shop-05", ProxyOverride = new() { Kind = SellerProxyKind.Static, Static = "http://x:y@45.1.2.3:8080" } });
        d.Accounts.Add(new SellerAccount { Id = "shop-06", ProxyOverride = new() { Kind = SellerProxyKind.Kiot, KiotKey = "SHARED-0001", Region = "bac" } });

        var res = _store.Save(d);

        Assert.True(res.Saved, string.Join("; ", res.Errors));
        Assert.Contains(res.Warnings, w => w.Contains("shop-03") && w.Contains("shop-04") && w.Contains("…0003"));
        Assert.Contains(res.Warnings, w => w.Contains("Shop Hai") && w.Contains("shop-05") && w.Contains("45.1.2.3:8080"));
        Assert.Contains(res.Warnings, w => w.Contains("shop-06") && w.Contains("trùng key proxy chung"));
        Assert.DoesNotContain(res.Warnings, w => w.Contains("OWN-0003") || w.Contains("SHARED-0001"));   // không lộ cả key
    }

    [Fact]
    public void CanhBao_VungRandom_ThieuKeyChung()
    {
        var d = new SellerAccountsData { Accounts = [new SellerAccount { Id = "a" }] };
        var (errors, warnings) = _store.Validate(d);
        Assert.Empty(errors);
        Assert.Contains(warnings, w => w.Contains("chưa nhập key Kiot chung"));

        d.Settings.SharedProxy.KiotKey = "K-1111";
        (_, warnings) = _store.Validate(d);
        Assert.Contains(warnings, w => w.Contains("random"));
    }

    [Fact]
    public void TrungThuMucProfile_Loi()
    {
        var d = Sample();
        d.Accounts[1].ProfileDir = Path.Combine(_dir, "profiles", "shop-01");
        Assert.Contains(_store.Validate(d).Errors, e => e.Contains("chung một thư mục profile"));
    }

    [Fact]
    public void ProfileMacDinh_DuoiGocProfiles()
    {
        Assert.Equal(Path.Combine(_dir, "profiles", "shop-01"), _store.ResolveProfileDir(new SellerAccount { Id = "shop-01" }));
        Assert.Equal(@"E:\p\x", _store.ResolveProfileDir(new SellerAccount { Id = "x", ProfileDir = @"E:\p\x" }));
        Assert.EndsWith(Path.Combine("upload", "seller-accounts.json"), SellerAccountStore.DefaultPath());
    }

    [Fact]
    public void FileHong_DoiTen_TraRong()
    {
        File.WriteAllText(_store.FilePath, "{ hỏng");
        var d = _store.Load();
        Assert.Empty(d.Accounts);
        Assert.False(File.Exists(_store.FilePath));
        var bak = Assert.Single(Directory.GetFiles(_dir, "seller-accounts.json.corrupt-*"));
        Assert.Equal(bak, _store.LastCorruptBackupPath);
    }

    [Fact]
    public void FileTot_KhongCoDuongDanCorrupt()
    {
        Assert.True(_store.Save(Sample()).Saved);
        _store.Load();
        Assert.Null(_store.LastCorruptBackupPath);
    }

    [Fact]
    public void TaiKhoanNull_BoQua_KhongNem()
    {
        File.WriteAllText(_store.FilePath, "{\"Accounts\":[null,{\"Id\":\"shop-01\"},null],\"RetiredIds\":[null,\"old-1\"]}");

        var d = _store.Load();

        Assert.Equal("shop-01", Assert.Single(d.Accounts).Id);
        Assert.Equal(["old-1"], d.RetiredIds);
        Assert.True(File.Exists(_store.FilePath));   // không coi là file hỏng
        Assert.Null(_store.LastCorruptBackupPath);
    }

    [Fact]
    public void ThuocTinhLa_VaRetiredIds_GiuNguyenSauKhiLuu()
    {
        File.WriteAllText(_store.FilePath,
            "{\"Version\":1,\"Future\":{\"a\":1},\"RetiredIds\":[\"old-1\"],\"Accounts\":[{\"Id\":\"shop-01\",\"Mode\":\"Subaccount\"," +
            "\"ProxyOverride\":{\"Kind\":\"Kiot\",\"KiotKey\":\"K-0001\",\"Region\":\"bac\",\"Sticky\":true}}]}");

        var d = _store.Load();
        d.Accounts[0].Label = "Đổi";
        Assert.True(_store.Save(d).Saved);

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_store.FilePath));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("Future").GetProperty("a").GetInt32());
        Assert.Equal("old-1", root.GetProperty("RetiredIds")[0].GetString());
        var acc = root.GetProperty("Accounts")[0];
        Assert.Equal("Subaccount", acc.GetProperty("Mode").GetString());
        Assert.Equal("Đổi", acc.GetProperty("Label").GetString());
        Assert.True(acc.GetProperty("ProxyOverride").GetProperty("Sticky").GetBoolean());
    }

    [Fact]
    public void ProfileTuongDoi_Loi()
    {
        var d = Sample();
        d.Accounts[0].ProfileDir = @"profiles\shop-01";
        Assert.Contains(_store.Validate(d).Errors, e => e.Contains("Shop Một") && e.Contains("đường dẫn tuyệt đối"));
        d.Accounts[0].ProfileDir = @"\khong-o-dia";
        Assert.Contains(_store.Validate(d).Errors, e => e.Contains("đường dẫn tuyệt đối"));
        d.Accounts[0].ProfileDir = Path.Combine(_dir, "p1");
        Assert.Empty(_store.Validate(d).Errors);
    }

    [Theory]
    [InlineData("BraveSoftware", "Brave-Browser")]
    [InlineData("Google", "Chrome")]
    [InlineData("Microsoft", "Edge")]
    public void ProfileTrongDuLieuTrinhDuyetCaNhan_Loi(string vendor, string product)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var d = Sample();
        d.Accounts[0].ProfileDir = Path.Combine(local, vendor, product, "User Data");
        Assert.Contains(_store.Validate(d).Errors, e => e.Contains("dữ liệu trình duyệt cá nhân"));
        d.Accounts[0].ProfileDir = Path.Combine(local, vendor, product, "USER DATA", "Profile 1");
        Assert.Contains(_store.Validate(d).Errors, e => e.Contains("dữ liệu trình duyệt cá nhân"));
        d.Accounts[0].ProfileDir = Path.Combine(local, vendor, product, "User Data-khac");
        Assert.DoesNotContain(_store.Validate(d).Errors, e => e.Contains("dữ liệu trình duyệt cá nhân"));
    }

    [Fact]
    public void JsonLaNull_CoiNhuHong_DoiTen()
    {
        File.WriteAllText(_store.FilePath, "null");
        Assert.Empty(_store.Load().Accounts);
        Assert.Single(Directory.GetFiles(_dir, "seller-accounts.json.corrupt-*"));
    }

    [Fact]
    public void FileDangBiKhoa_Nem_KhongDoiTen_SaveSauKhongDeMat()
    {
        Assert.True(_store.Save(Sample()).Saved);
        var before = File.ReadAllBytes(_store.FilePath);

        using (new FileStream(_store.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.ThrowsAny<IOException>(() => _store.Load());

        Assert.Empty(Directory.GetFiles(_dir, "*.corrupt-*"));
        Assert.Equal(before, File.ReadAllBytes(_store.FilePath));
        Assert.Equal(3, _store.Load().Accounts.Count);
    }

    [Fact]
    public void FileHong_DoiTenKhongDuoc_Nem_KhongTraRong()
    {
        File.WriteAllText(_store.FilePath, "{ hỏng");
        // Mở cho phép người khác ĐỌC nhưng không cho xoá/đổi tên → đọc được, File.Move hỏng.
        using (new FileStream(_store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var ex = Assert.ThrowsAny<IOException>(() => _store.Load());
            Assert.Contains("không đổi tên được", ex.Message);
        }
        Assert.True(File.Exists(_store.FilePath));
        Assert.Empty(Directory.GetFiles(_dir, "*.corrupt-*"));
    }

    [Fact]
    public void ChuaCoFile_TraRong()
    {
        var d = _store.Load();
        Assert.Empty(d.Accounts);
        Assert.NotNull(d.Settings.SharedProxy);
    }

    [Fact]
    public void Defaults_MacDinhHopLe_DoiSangMassUploadDefaults()
    {
        Assert.Empty(new SellerDefaults().Validate());
        var m = new SellerDefaults { StockPerVariation = 7, WeightGrams = 250, LengthCm = 30, WidthCm = 20, HeightCm = 10, ProductsPerFile = 40 }
            .ToMassUploadDefaults("sizechart:abc:d:\\1.jpeg");
        Assert.Equal(new MassUploadDefaults(7, 250, 30, 20, 10, "sizechart:abc:d:\\1.jpeg", 40), m);

        Assert.Throws<InvalidOperationException>(() => new SellerDefaults { StockPerVariation = 1_000_000 }.ToMassUploadDefaults("k"));
        Assert.Throws<InvalidOperationException>(() => new SellerDefaults().ToMassUploadDefaults(" "));
    }

    [Fact]
    public void ProfileLongNhau_HaiChieu_LaTrung()
    {
        var d = Sample();
        d.Accounts[1].ProfileDir = Path.Combine(_dir, "x", "p");
        d.Accounts[2].ProfileDir = Path.Combine(_dir, "x", "p", "Default");
        Assert.Contains(_store.Validate(d).Errors, e => e.Contains("lồng nhau") && e.Contains("Shop Hai") && e.Contains("shop-03"));

        (d.Accounts[1].ProfileDir, d.Accounts[2].ProfileDir) = (d.Accounts[2].ProfileDir, d.Accounts[1].ProfileDir);
        Assert.Contains(_store.Validate(d).Errors, e => e.Contains("lồng nhau"));

        d.Accounts[2].ProfileDir = Path.Combine(_dir, "x", "p2");   // anh em, không lồng
        Assert.DoesNotContain(_store.Validate(d).Errors, e => e.Contains("profile"));
    }

    [Fact]
    public void ProfileTroVaoGocProfilesCuaApp_Loi()
    {
        var d = Sample();
        d.Accounts[1].ProfileDir = Path.Combine(_dir, "profiles");   // cha của profile mặc định của shop-01
        Assert.Contains(_store.Validate(d).Errors, e => e.Contains("Shop Hai") && e.Contains("gốc profile mặc định"));

        d.Accounts[1].ProfileDir = _dir;                              // ông của nó
        Assert.Contains(_store.Validate(d).Errors, e => e.Contains("gốc profile mặc định"));
    }

    [Fact]
    public void TienToDuongDanDai_VanBatTrung()
    {
        var d = Sample();
        d.Accounts[1].ProfileDir = @"\\?\" + Path.Combine(_dir, "profiles", "shop-01");
        Assert.Contains(_store.Validate(d).Errors, e => e.Contains("chung một thư mục profile"));

        Assert.Equal(@"\\srv\share\p", SellerAccountStore.NormalizePath(@"\\?\UNC\srv\share\p\"));
        Assert.Equal(@"D:\p", SellerAccountStore.NormalizePath(@"\\?\D:\p"));
    }

    [Theory]
    [InlineData("CocCoc", "Browser")]
    [InlineData("BraveSoftware", "Brave-Browser-Beta")]
    [InlineData("BraveSoftware", "Brave-Browser-Nightly")]
    [InlineData("Google", "Chrome SxS")]
    public void ProfileTrongTrinhDuyetKhac_Loi(string vendor, string product)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var d = Sample();
        d.Accounts[0].ProfileDir = @"\\?\" + Path.Combine(local, vendor, product, "User Data", "Default");
        Assert.Contains(_store.Validate(d).Errors, e => e.Contains("dữ liệu trình duyệt cá nhân"));
    }

    [Fact]
    public void ThuocTinhLa_MoiTang_GiuNguyen()
    {
        File.WriteAllText(_store.FilePath,
            "{\"Settings\":{\"S\":1,\"SharedProxy\":{\"K\":2},\"Pacing\":{\"P\":3}}," +
            "\"Accounts\":[{\"Id\":\"a\",\"Defaults\":{\"D\":4},\"Source\":{\"R\":5}}]}");
        Assert.True(_store.Save(_store.Load()).Saved);
        var json = File.ReadAllText(_store.FilePath);
        foreach (var (k, v) in new[] { ("S", 1), ("K", 2), ("P", 3), ("D", 4), ("R", 5) }) Assert.Contains($"\"{k}\": {v}", json);
    }
}
