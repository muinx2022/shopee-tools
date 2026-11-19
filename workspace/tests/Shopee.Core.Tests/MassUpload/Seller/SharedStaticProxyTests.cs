using System.Text.Json;
using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>GĐ5d: proxy tĩnh ở mục "Proxy chung" — thứ tự Kiot &gt; tĩnh, dạng cách bằng khoảng trắng / tab, cảnh báo nhiều shop chung
/// một IP tĩnh.</summary>
public sealed class SharedStaticProxyTests : IDisposable
{
    private readonly string _dir = SellerTestData.NewTempDir();
    private readonly SellerAccountStore _store;

    public SharedStaticProxyTests() =>
        _store = new SellerAccountStore(Path.Combine(_dir, "seller-accounts.json"), Path.Combine(_dir, "profiles"));

    public void Dispose() => SellerTestData.TryDelete(_dir);

    private static SellerUploadSettings Settings(string? key, string? stat) => new()
    {
        SharedProxy = new KiotProxySettings { KiotKey = key, Region = "bac", Static = stat },
    };

    // ─────────────── Resolve: Kiot > tĩnh ───────────────

    [Fact]
    public void Resolve_CoKeyKiot_DungKiot_DuCoProxyTinh()
    {
        var s = SellerProxySource.Resolve(new SellerAccount { Id = "a" }, Settings("KEY-0001", "193.233.210.148\t8080"), out var err)!;
        Assert.Null(err);
        Assert.Equal(SellerProxyKind.Kiot, s.Kind);
        Assert.Equal("KEY-0001", s.KiotKey);
        Assert.True(s.RotatePerShop);
        Assert.Null(s.Static);
    }

    [Fact]
    public void Resolve_KhongKey_CoProxyTinh_DungProxyTinhChung()
    {
        var s = SellerProxySource.Resolve(new SellerAccount { Id = "a" }, Settings(" ", "193.233.210.148\t8080"), out var err)!;
        Assert.Null(err);
        Assert.Equal(SellerProxyKind.Static, s.Kind);
        Assert.True(s.IsShared);
        Assert.False(s.RotatePerShop);
        Assert.Equal("193.233.210.148", s.Static!.Host);
        Assert.Equal(8080, s.Static.Port);
        Assert.False(s.Static.MustBeUnusedIp);
    }

    [Fact]
    public void Resolve_KhongCaHai_Loi_ProxyTinhSai_LoiKhongChuaInput()
    {
        Assert.Null(SellerProxySource.Resolve(new SellerAccount { Id = "a" }, Settings(null, null), out var err));
        Assert.Contains("chưa có proxy chung", err);

        Assert.Null(SellerProxySource.Resolve(new SellerAccount { Id = "a" }, Settings(null, "myuser:Secret99:1.2.3.4:8080"), out err));
        Assert.Contains("Proxy tĩnh chung sai", err);
        Assert.DoesNotContain("Secret99", err);
    }

    [Fact]
    public void Resolve_ProxyRiengVanThangProxyChung()
    {
        var acc = new SellerAccount { Id = "a", ProxyOverride = new() { Kind = SellerProxyKind.Static, Static = "5.6.7.8:3128" } };
        var s = SellerProxySource.Resolve(acc, Settings(null, "1.2.3.4 8080"), out _)!;
        Assert.Equal("5.6.7.8", s.Static!.Host);
        Assert.False(s.IsShared);
    }

    // ─────────────── Parser: khoảng trắng / tab ───────────────

    [Theory]
    [InlineData("193.233.210.148\t8080", "193.233.210.148", 8080, null, null)]
    [InlineData("193.233.210.148 8080", "193.233.210.148", 8080, null, null)]
    [InlineData("\r\n193.233.210.148\t8080\r\n", "193.233.210.148", 8080, null, null)]   // dán từ Excel
    [InlineData("193.233.210.148\t8080\tuser1\tp:a@ss", "193.233.210.148", 8080, "user1", "p:a@ss")]
    [InlineData("proxy.vn   3128  user1  pw", "proxy.vn", 3128, "user1", "pw")]
    [InlineData("[2001:db8::1]\t8080", "2001:db8::1", 8080, null, null)]
    public void Parser_NhanDangCachKhoangTrang(string raw, string host, int port, string? user, string? pass)
    {
        Assert.True(StaticProxyParser.TryParse(raw, ProxyScheme.Http, out var l, out var err), err);
        Assert.Equal(host, l!.Host);
        Assert.Equal(port, l.Port);
        Assert.Equal(user, l.User);
        Assert.Equal(pass, l.Pass);
    }

    [Theory]
    [InlineData("1.2.3.4 8080 user")]          // 3 đoạn
    [InlineData("1.2.3.4 80a")]
    [InlineData("1.2.3.4:8080 user pass")]     // host có ':'
    [InlineData("http://1.2.3.4 8080")]
    [InlineData("1.2.3.4 8080 us:er pw")]      // user có ':'
    [InlineData("bad host:8080")]
    [InlineData("1.2.3.4 8080 a b c")]
    [InlineData("u@1.2.3.4 8080")]
    public void Parser_KhoangTrangSaiCho_TuChoi(string raw)
    {
        Assert.False(StaticProxyParser.TryParse(raw, ProxyScheme.Http, out var l, out var err));
        Assert.Null(l);
        Assert.False(string.IsNullOrWhiteSpace(err));
    }

    [Theory]
    [InlineData("myuser\tSecret99\t1.2.3.4\t8080", "Secret99")]   // gõ ngược thứ tự
    [InlineData("Secret99 8080 u", "Secret99")]
    [InlineData("1.2.3.4 8080 Secret99", "Secret99")]
    public void Parser_KhoangTrang_LoiKhongChuaInput(string raw, string secret)
    {
        Assert.False(StaticProxyParser.TryParse(raw, ProxyScheme.Http, out _, out var err));
        Assert.DoesNotContain(secret, err);
        Assert.DoesNotContain("1.2.3.4", err);
        Assert.DoesNotContain("8080", err);
    }

    // ─────────────── Validate: cảnh báo trùng IP tĩnh ───────────────

    private static SellerAccountsData Data(string? key, string? stat, params SellerAccount[] accounts) =>
        new() { Settings = Settings(key, stat), Accounts = [.. accounts] };

    [Fact]
    public void Validate_HaiShopDangBatDiProxyTinhChung_CanhBao_KhongChan()
    {
        var d = Data(null, "193.233.210.148\t8080\tu1\tpw1",
            new SellerAccount { Id = "a", Label = "Shop A" }, new SellerAccount { Id = "b", Label = "Shop B" },
            new SellerAccount { Id = "c", Label = "Shop C", Enabled = false });

        var (errors, warnings) = _store.Validate(d);

        Assert.Empty(errors);
        var w = Assert.Single(warnings, x => x.Contains("dùng chung 1 IP tĩnh"));
        Assert.StartsWith("2 shop dùng chung 1 IP tĩnh", w);
        Assert.Contains("Shopee thấy các shop này cùng một IP", w);
        Assert.Contains("Shop A", w);
        Assert.DoesNotContain("Shop C", w);          // tài khoản đang tắt không tính
        Assert.DoesNotContain("pw1", w);
        Assert.DoesNotContain(warnings, x => x.Contains("chưa nhập key Kiot chung"));
        Assert.True(_store.Save(d).Saved);
    }

    [Fact]
    public void Validate_ProxyTinhChungTrungProxyTinhRieng_DemCaHai()
    {
        var d = Data(null, "193.233.210.148 8080",
            new SellerAccount { Id = "a", Label = "Shop A" },
            new SellerAccount { Id = "b", Label = "Shop B", ProxyOverride = new() { Kind = SellerProxyKind.Static, Static = "http://193.233.210.148:8080" } });

        var (_, warnings) = _store.Validate(d);

        Assert.Contains(warnings, x => x.StartsWith("2 shop dùng chung 1 IP tĩnh") && x.Contains("193.233.210.148:8080"));
    }

    [Fact]
    public void Validate_CoKeyKiot_ProxyTinhChungBiBoQua_KhongCanhBaoTrungIp()
    {
        var d = Data("KEY-0001", "193.233.210.148 8080",
            new SellerAccount { Id = "a", Label = "Shop A" }, new SellerAccount { Id = "b", Label = "Shop B" });
        var (_, warnings) = _store.Validate(d);
        Assert.DoesNotContain(warnings, x => x.Contains("dùng chung 1 IP tĩnh"));
    }

    [Fact]
    public void Validate_ProxyTinhChungSai_Loi_KhongLoInput()
    {
        var d = Data(null, "myuser:Secret99:1.2.3.4:8080", new SellerAccount { Id = "a" });
        var (errors, _) = _store.Validate(d);
        var e = Assert.Single(errors);
        Assert.Contains("Proxy chung: proxy tĩnh sai", e);
        Assert.DoesNotContain("Secret99", e);
    }

    [Fact]
    public void Socks5CoUserPass_CoKeyKiot_ChiCanhBao_KhongKey_Loi()
    {
        SellerAccountsData Make(string? key)
        {
            var d = Data(key, "1.2.3.4:8080:u1:pw1", new SellerAccount { Id = "a" });
            d.Settings.SharedProxy.Type = ProxyScheme.Socks5;
            return d;
        }

        var (errors, warnings) = _store.Validate(Make("KEY-0001"));
        Assert.Empty(errors);
        Assert.Contains(warnings, w => w.Contains("đang bị bỏ qua vì có key Kiot") && w.Contains("http://"));
        Assert.True(_store.Save(Make("KEY-0001")).Saved);

        (errors, _) = _store.Validate(Make(null));
        Assert.Contains(errors, e => e.Contains("SOCKS5") && e.Contains("http://"));
    }

    [Fact]
    public void LoiDinhDangKhac_CoKeyKiot_VanLaLoi()
    {
        var d = Data("KEY-0001", "1.2.3.4​:8080", new SellerAccount { Id = "a" });
        var (errors, _) = _store.Validate(d);
        Assert.Contains(errors, e => e.Contains("Proxy chung: proxy tĩnh sai"));
    }

    [Fact]
    public void TrungIpTinh_IPv6HaiCachViet_VanGop()
    {
        var d = Data(null, "[2001:DB8:0::1]	8080",
            new SellerAccount { Id = "a", Label = "Shop A" },
            new SellerAccount { Id = "b", Label = "Shop B", ProxyOverride = new() { Kind = SellerProxyKind.Static, Static = "[2001:db8::1]:8080" } });

        var (_, warnings) = _store.Validate(d);

        Assert.Contains(warnings, w => w.StartsWith("2 shop dùng chung 1 IP tĩnh ([2001:db8::1]:8080)"));
    }

    [Fact]
    public void LuuRoiDocLai_GiuProxyTinhChung()
    {
        var d = Data(null, "193.233.210.148\t8080", new SellerAccount { Id = "a" });
        Assert.True(_store.Save(d).Saved);
        Assert.Equal("193.233.210.148\t8080", _store.Load().Settings.SharedProxy.Static);

        // File cũ không có trường Static → null, Resolve vẫn báo thiếu proxy chung.
        var json = JsonSerializer.Serialize(new { Settings = new { SharedProxy = new { KiotKey = (string?)null } }, Accounts = new[] { new { Id = "a" } } });
        File.WriteAllText(_store.FilePath, json);
        Assert.Null(_store.Load().Settings.SharedProxy.Static);
    }
}
