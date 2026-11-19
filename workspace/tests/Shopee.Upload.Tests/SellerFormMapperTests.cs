using System.Text.Json;
using System.Text.Json.Serialization;
using Shopee.Core.MassUpload.Seller;
using Shopee.Upload.Forms;

namespace Shopee.Upload.Tests;

public sealed class SellerFormMapperTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "upload-form-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private SellerAccountStore NewStore() =>
        new(Path.Combine(_dir, "seller-accounts.json"), Path.Combine(_dir, "profiles"));

    /// <summary>Dữ liệu có đủ 3 kiểu proxy + mọi giá trị khác mặc định, để round-trip bắt được ô nào bị rơi.</summary>
    private static SellerAccountsData Sample() => new()
    {
        Version = 1,
        Settings = new SellerUploadSettings
        {
            SharedProxy = new KiotProxySettings { KiotKey = "SHARED-KEY-0001", Region = "bac", Type = ProxyScheme.Socks5 },
            SizeChartImagePath = @"C:\anh\bang-size.jpg",
            Pacing = new SellerPacing
            {
                PauseBetweenFilesMinMinutes = 4, PauseBetweenFilesMaxMinutes = 9,
                PauseBetweenShopsMinMinutes = 11, PauseBetweenShopsMaxMinutes = 21,
                ActiveFromHour = 7, ActiveToHour = 23,
            },
        },
        Accounts =
        [
            new SellerAccount
            {
                Id = "shop-a", Label = "Shop A", Enabled = true,
                Defaults = new SellerDefaults { StockPerVariation = 55, WeightGrams = 321, LengthCm = 11, WidthCm = 12, HeightCm = 13, ProductsPerFile = 25 },
                Source = new SellerSource { BigSellerAccount = "bs1", Sheet = "Sheet2", FromRow = 3, ToRow = 99 },
                DailyProductCap = 77,
            },
            new SellerAccount
            {
                Id = "shop-b", Label = "Shop B", Enabled = false, ProfileDir = @"D:\profiles\b",
                ProxyOverride = new SellerProxyOverride
                {
                    Kind = SellerProxyKind.Kiot, KiotKey = "OWN-KEY-0002", Region = "nam", KiotKeepIp = true, Type = ProxyScheme.Socks5,
                },
            },
            new SellerAccount
            {
                Id = "shop-c", Label = "Shop C",
                ProxyOverride = new SellerProxyOverride
                {
                    Kind = SellerProxyKind.Static, Static = "1.2.3.4:8080:user:pa:ss", Type = ProxyScheme.Http,
                },
            },
        ],
    };

    private static string ToJson(SellerAccountsData d) => JsonSerializer.Serialize(d, Json);

    private static (SettingsForm Settings, List<AccountForm> Accounts) Forms(SellerAccountsData d) =>
        (SellerFormMapper.ToForm(d.Settings), d.Accounts.Select(SellerFormMapper.ToForm).ToList());

    [Fact]
    public void RoundTrip_DuLieu_Form_DuLieu_GiuNguyenMoiTruong()
    {
        var original = Sample();
        var (settings, accounts) = Forms(original);

        var (data, errors) = SellerFormMapper.ToData(settings, accounts, original);

        Assert.Empty(errors);
        Assert.Equal(ToJson(original), ToJson(data));
    }

    [Fact]
    public void RoundTrip_GiuSizeChartVaVersion_KhongCoTrenMan()
    {
        var original = Sample();
        original.Version = 7;
        var (settings, accounts) = Forms(original);

        var (data, _) = SellerFormMapper.ToData(settings, accounts, original);

        Assert.Equal(7, data.Version);
        Assert.Equal(@"C:\anh\bang-size.jpg", data.Settings.SizeChartImagePath);
    }

    [Fact]
    public void LuuQuaStore_RoiDocLai_FormThayDuDuLieu()
    {
        var store = NewStore();
        var original = Sample();
        var (settings, accounts) = Forms(original);
        var (data, errors) = SellerFormMapper.ToData(settings, accounts, original);
        Assert.Empty(errors);

        var saved = store.Save(data);
        Assert.True(saved.Saved, string.Join("; ", saved.Errors));

        var reloaded = NewStore().Load();
        var (s2, a2) = Forms(reloaded);
        var (again, errors2) = SellerFormMapper.ToData(s2, a2, reloaded);
        Assert.Empty(errors2);
        Assert.Equal(ToJson(original), ToJson(again));
        Assert.Equal("SHARED-KEY-0001", s2.KiotKey);
        Assert.Equal(ProxyChoice.KiotOwn, a2[1].Proxy);
        Assert.True(a2[1].OwnKeepIp);
        Assert.Equal(ProxyChoice.Static, a2[2].Proxy);
        Assert.Equal("1.2.3.4:8080:user:pa:ss", a2[2].StaticProxy);
    }

    [Fact]
    public void OSoSai_BaoLoiTheoTenO_KhongDoan()
    {
        var f = SellerFormMapper.NewAccountForm("seller-1");
        f.Label = "Shop X";
        f.WeightGrams = "abc";
        f.StockPerVariation = "";
        f.DailyProductCap = "1.5";
        var errors = new List<string>();

        SellerFormMapper.ToAccount(f, errors);

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.Contains("Shop X (seller-1)") && e.Contains("cân nặng") && e.Contains("'abc'"));
        Assert.Contains(errors, e => e.Contains("tồn kho") && e.Contains("chưa nhập"));
        Assert.Contains(errors, e => e.Contains("trần SP mỗi ngày") && e.Contains("'1.5'"));
    }

    [Fact]
    public void DongNguonDeTrong_La0_SoAmVanDocDeCoreBaoLuat()
    {
        var f = SellerFormMapper.NewAccountForm("seller-1");
        f.FromRow = " ";
        f.ToRow = "";
        f.StockPerVariation = "-5";
        var errors = new List<string>();

        var a = SellerFormMapper.ToAccount(f, errors);

        Assert.Empty(errors);
        Assert.Equal(0, a.Source.FromRow);
        Assert.Equal(0, a.Source.ToRow);
        Assert.Equal(-5, a.Defaults.StockPerVariation);
        Assert.NotEmpty(a.Defaults.Validate());   // Core chặn tồn kho âm
    }

    [Fact]
    public void NhipChaySai_BaoLoi()
    {
        var s = SellerFormMapper.ToForm(new SellerUploadSettings());
        s.PauseShopsMin = "năm";
        var errors = new List<string>();

        SellerFormMapper.ToSettings(s, null, errors);

        Assert.Single(errors);
        Assert.Contains("nghỉ giữa 2 shop (từ)", errors[0]);
    }

    [Fact]
    public void DoiKieuProxy_ChiGhiPhanCuaKieuDangChon()
    {
        var f = SellerFormMapper.NewAccountForm("seller-1");
        f.OwnKiotKey = "OWN-KEY-9999";
        f.StaticProxy = "5.6.7.8:3128";
        var errors = new List<string>();

        f.Proxy = ProxyChoice.Static;
        var st = SellerFormMapper.ToAccount(f, errors).ProxyOverride!;
        Assert.Equal(SellerProxyKind.Static, st.Kind);
        Assert.Equal("5.6.7.8:3128", st.Static);
        Assert.Null(st.KiotKey);

        f.Proxy = ProxyChoice.KiotOwn;
        var kv = SellerFormMapper.ToAccount(f, errors).ProxyOverride!;
        Assert.Equal(SellerProxyKind.Kiot, kv.Kind);
        Assert.Equal("OWN-KEY-9999", kv.KiotKey);
        Assert.Null(kv.Static);

        f.Proxy = ProxyChoice.Shared;
        Assert.Null(SellerFormMapper.ToAccount(f, errors).ProxyOverride);
        Assert.Empty(errors);
        // Form vẫn giữ ô của kiểu kia (đổi ý quay lại không phải gõ lại).
        Assert.Equal("5.6.7.8:3128", f.StaticProxy);
    }

    [Fact]
    public void KeyRiengTrong_StoreValidateBaoLoi_KhongLuu()
    {
        var store = NewStore();
        var f = SellerFormMapper.NewAccountForm("seller-1");
        f.Proxy = ProxyChoice.KiotOwn;
        var (data, errors) = SellerFormMapper.ToData(new SettingsForm(), [f], null);
        // SettingsForm rỗng → ô nhịp chạy trống là lỗi đọc ô; Store vẫn phải chặn key riêng trống.
        Assert.NotEmpty(errors);

        var r = store.Save(data);

        Assert.False(r.Saved);
        Assert.Contains(r.Errors, e => e.Contains("thiếu key"));
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void NewId_BoQuaMaDaDung_KhongPhanBietHoaThuong_VaMaBiChiem()
    {
        Assert.Equal("seller-1", SellerFormMapper.NewId([]));
        Assert.Equal("seller-3", SellerFormMapper.NewId(["SELLER-1", "seller-2"]));
        // seller-1 đã có thư mục profile trên đĩa (tài khoản cũ vừa xoá) → không dùng lại.
        Assert.Equal("seller-2", SellerFormMapper.NewId([], id => id == "seller-1"));
        Assert.True(SellerAccountStore.IsValidId(SellerFormMapper.NewId(["seller-1"])));
    }

    [Fact]
    public void Secrets_GomKeyChungKeyRiengVaMatKhauProxyTinh()
    {
        var (settings, accounts) = Forms(Sample());

        var secrets = SellerFormMapper.Secrets(settings, accounts).ToList();

        Assert.Contains("SHARED-KEY-0001", secrets);
        Assert.Contains("OWN-KEY-0002", secrets);
        Assert.Contains("pa:ss", secrets);
    }

    [Fact]
    public void MoTaProxy_KhongInMatKhauVaKeyNguyen()
    {
        var (_, accounts) = Forms(Sample());
        var kiot = accounts[1];
        var stat = accounts[2];

        Assert.Equal("Kiot riêng …0002", kiot.ProxySummary);
        Assert.Equal("Tĩnh 1.2.3.4:8080", stat.ProxySummary);
        Assert.Equal("Đọc được: http://1.2.3.4:8080 (có user/pass)", stat.StaticProxyPreview);
        Assert.DoesNotContain("pa:ss", stat.StaticProxyPreview);
    }

    [Fact]
    public void Edited_ChiBaoKhiSuaONhap_KhongBaoKhiDoiThuocTinhSuyRa()
    {
        var f = SellerFormMapper.NewAccountForm("seller-1");
        var n = 0;
        f.Edited += () => n++;

        f.ResolvedProfileDir = @"C:\x";
        Assert.Equal(0, n);

        f.Label = "Shop";
        Assert.Equal(1, n);

        f.IsStaticProxy = true;   // radio → đổi Proxy (1 lần ô nhập), các bool suy ra không tính
        Assert.Equal(2, n);
        Assert.Equal(ProxyChoice.Static, f.Proxy);
    }

    // ─────────────── vòng 3 ───────────────

    [Theory]
    [InlineData("myuser:Secret99:1.2.3.4:8080")]
    [InlineData("1.2.3.4:8080@user:Secret99")]
    public void Secrets_ProxyTinhDocKhongDuoc_TraMoiDoanDai(string raw)
    {
        var f = SellerFormMapper.NewAccountForm("seller-1");
        f.Proxy = ProxyChoice.Static;
        f.StaticProxy = raw;
        Assert.False(StaticProxyParser.TryParse(raw, ProxyScheme.Http, out _, out _));

        var secrets = SellerFormMapper.Secrets(new SettingsForm(), [f]).ToList();

        Assert.Contains("Secret99", secrets);
        Assert.DoesNotContain(secrets, s => s is { Length: > 0 and < 4 });
        Assert.DoesNotContain("Secret99", SecretText.Redact($"lỗi {raw}", secrets));
    }

    [Fact]
    public void ToData_GhiDeLenBanGoc_GiuTruongLaCuaTaiKhoanVaOverride()
    {
        var original = Sample();
        original.Accounts[1].ExtensionData = new() { ["Mode"] = JsonSerializer.SerializeToElement("Subaccount") };
        original.Accounts[1].ProxyOverride!.ExtensionData = new() { ["Sticky"] = JsonSerializer.SerializeToElement(true) };
        original.RetiredIds = ["old-1"];
        var (settings, accounts) = Forms(original);
        accounts[1].Label = "Shop B mới";

        var (data, errors) = SellerFormMapper.ToData(settings, accounts, original, ["seller-9"]);

        Assert.Empty(errors);
        Assert.Equal("Subaccount", data.Accounts[1].ExtensionData!["Mode"].GetString());
        Assert.True(data.Accounts[1].ProxyOverride!.ExtensionData!["Sticky"].GetBoolean());
        Assert.Equal("Shop B mới", data.Accounts[1].Label);
        Assert.Equal(["old-1", "seller-9"], data.RetiredIds);
        // Bản đã đọc không bị sửa (lưu lỗi thì còn nguyên để so).
        Assert.Equal("Shop B", original.Accounts[1].Label);
        Assert.Equal(["old-1"], original.RetiredIds);
    }

    [Fact]
    public void NewId_BoQuaMaDaXoa()
    {
        Assert.Equal("seller-3", SellerFormMapper.NewId(["seller-2"], null, ["SELLER-1"]));
    }

    // ─────────────── vòng 4 ───────────────

    [Theory]
    [InlineData("myuser:12345:1.2.3.4:8080", "12345")]              // gõ ngược: Core từ chối → che mọi đoạn
    [InlineData("1.2.3.4:8080:me@mail.com:12345", "12345")]         // @ trong user: đọc đúng, pass = 12345
    [InlineData("http://1.2.3.4:8080:alice:9999", "9999")]
    [InlineData("1.2.3.4:8080:Secret99:alice", "Secret99")]           // đổi chỗ user/pass: đọc được nhưng mật khẩu thật nằm ở ô user
    public void Secrets_ProxyTinh_LuonCheMoiDoan_TruHostVaCong(string raw, string secret)
    {
        var f = SellerFormMapper.NewAccountForm("seller-1");
        f.Proxy = ProxyChoice.Static;
        f.StaticProxy = raw;

        var secrets = SellerFormMapper.Secrets(new SettingsForm(), [f]).ToList();
        var redacted = SecretText.Redact($"lỗi: {raw} | IP ra 1.2.3.4 qua http://1.2.3.4:8080", secrets);

        Assert.Contains(secret, secrets);
        Assert.DoesNotContain(secret, redacted.Replace("…" + secret[^4..], ""));
        Assert.DoesNotContain("http", secrets);   // scheme không bị coi là bí mật (sẽ băm nát mọi URL trong log)
        if (StaticProxyParser.TryParse(raw, ProxyScheme.Http, out _, out _))
        {
            Assert.DoesNotContain("1.2.3.4", secrets);
            Assert.DoesNotContain("8080", secrets);
            Assert.Contains("IP ra 1.2.3.4 qua http://1.2.3.4:8080", redacted);
        }
    }

    [Fact]
    public void RoundTrip_TruongLa_MoiTang()
    {
        var store = NewStore();
        Directory.CreateDirectory(_dir);
        File.WriteAllText(store.FilePath, """
            {"Version":1,"X0":0,
             "Settings":{"X1":1,"SharedProxy":{"KiotKey":"SHARED-KEY-0001","Region":"bac","X2":2},"Pacing":{"X3":3}},
             "Accounts":[{"Id":"shop-a","X4":4,"Defaults":{"X5":5},"Source":{"X6":6},
                          "ProxyOverride":{"Kind":"Kiot","KiotKey":"OWN-KEY-0002","Region":"nam","X7":7}}]}
            """);
        var original = store.Load();
        var (s, a) = Forms(original);
        a[0].Label = "Đổi";
        s.PauseFilesMin = "4";

        var (data, errors) = SellerFormMapper.ToData(s, a, original);
        Assert.Empty(errors);
        Assert.True(store.Save(data).Saved);

        var json = File.ReadAllText(store.FilePath);
        for (var i = 0; i <= 7; i++) Assert.Contains($"\"X{i}\": {i}", json);
        Assert.Equal("Đổi", NewStore().Load().Accounts[0].Label);
    }
}
