using System.Net;
using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>GĐ4: <see cref="SellerProxyProvider"/> + <see cref="SellerKiotApi"/> — chờ mốc <c>nextRequestAt</c> rồi mới gọi
/// <c>/new</c>, huỷ khi đang chờ, lỗi Kiot rõ ràng, key giữ IP, proxy tĩnh. Không mạng thật, đồng hồ giả.</summary>
public sealed class SellerProxyProviderTests
{
    private readonly FakeClock _clock = new(SellerTestData.T0);
    private readonly FakeKiot _kiot;
    private readonly SellerProxyProvider _provider;
    private readonly List<KiotWaitTick> _ticks = [];

    public SellerProxyProviderTests()
    {
        _kiot = new FakeKiot(_clock);
        _provider = new SellerProxyProvider(_kiot.Api, new SellerProxyProviderOptions { Tick = TimeSpan.FromSeconds(15) },
            () => _clock.Now, _clock.Delay);
        _provider.Waiting += _ticks.Add;
    }

    private static SellerProxySource Shared(string key = "KEY-ABCD1234", string region = "bac") =>
        new(SellerProxyKind.Kiot, key, region, ProxyScheme.Http, RotatePerShop: true, Static: null, IsShared: true);

    [Fact]
    public async Task ChuaToiNextRequestAt_ChoDungMocRoiMoiGoiNew()
    {
        // Key đang có IP, chỉ được đổi sau 200 s — biết qua /current.
        _kiot.CurrentIp = "113.160.9.9";
        _kiot.NextChangeAt = SellerTestData.T0.AddSeconds(200);

        var r = await _provider.AcquireAsync(Shared(), CancellationToken.None);

        Assert.True(r.Ok, r.Error);
        Assert.Single(_kiot.NewCalls);
        Assert.True(_kiot.NewCalls[0] >= SellerTestData.T0.AddSeconds(200), $"gọi /new lúc {_kiot.NewCalls[0]:HH:mm:ss}, trước mốc");
        // Chờ đúng tới mốc + đệm 2 s, không hơn.
        Assert.Equal(TimeSpan.FromSeconds(202), _clock.TotalDelayed);
        Assert.NotEmpty(_ticks);
        Assert.Equal(TimeSpan.FromSeconds(202), _ticks[0].Remaining);
        Assert.Equal("1234", _ticks[0].KeyTail);
        Assert.Equal("113.160.0.1", r.Lease!.Host);
        Assert.Equal(20001, r.Lease.Port);
        Assert.Equal(ProxyLeaseSource.Kiot, r.Lease.Source);
        Assert.True(r.Lease.MustBeUnusedIp);
        Assert.Equal("113.160.0.1", r.Lease.ExpectedIp);
        Assert.Contains("region=bac", _kiot.Http.Urls.Single(u => u.Contains("/new")));
    }

    [Fact]
    public async Task ShopThuHai_DungMocCuaLanTruoc_KhongHoiLaiCurrent()
    {
        var a = await _provider.AcquireAsync(Shared(), CancellationToken.None);
        Assert.True(a.Ok);
        Assert.Equal(1, _kiot.CurrentCalls);           // lần đầu của key: hỏi mốc
        var t1 = _clock.Now;

        var b = await _provider.AcquireAsync(Shared(), CancellationToken.None);

        Assert.True(b.Ok, b.Error);
        Assert.Equal(1, _kiot.CurrentCalls);
        Assert.Equal(2, _kiot.NewCalls.Count);
        Assert.True(_kiot.NewCalls[1] >= t1 + TimeSpan.FromMinutes(5));
        Assert.NotEqual(a.Lease!.Host, b.Lease!.Host);
    }

    [Fact]
    public async Task KiotVanBatCho_DocSoGiay_ChoRoiGoiLai()
    {
        // Không biết mốc (/current không có IP) nhưng Kiot vẫn đang khoá 90 s.
        _kiot.NextChangeAt = SellerTestData.T0.AddSeconds(90);

        var r = await _provider.AcquireAsync(Shared(), CancellationToken.None);

        Assert.True(r.Ok, r.Error);
        Assert.Equal(2, _kiot.NewCalls.Count);
        Assert.Equal(SellerTestData.T0, _kiot.NewCalls[0]);
        Assert.True(_kiot.NewCalls[1] >= SellerTestData.T0.AddSeconds(90));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(-10)]
    public async Task DongHoKiotLech10Phut_ChoTheoGioMayChu(int skewMinutes)
    {
        _kiot.ServerSkew = TimeSpan.FromMinutes(skewMinutes);

        var a = await _provider.AcquireAsync(Shared(), CancellationToken.None);
        var t1 = _clock.Now;
        var b = await _provider.AcquireAsync(Shared(), CancellationToken.None);

        Assert.True(a.Ok, a.Error);
        Assert.True(b.Ok, b.Error);
        Assert.Equal(0, _kiot.Rejected);                         // không gọi /new sớm (lệch -10')
        Assert.Equal(2, _kiot.NewCalls.Count);
        // Chờ đúng 5 phút + đệm theo đồng hồ Kiot, không chờ 15 phút (lệch +10') hay 0.
        Assert.Equal(TimeSpan.FromSeconds(302), _kiot.NewCalls[1] - t1);
    }

    [Fact]
    public async Task HuyKhiDangCho_NemHuy_KhongGoiNew()
    {
        _kiot.CurrentIp = "113.160.9.9";
        _kiot.NextChangeAt = SellerTestData.T0.AddMinutes(4);
        using var cts = new CancellationTokenSource();
        _clock.OnDelay = _ => { if (_clock.Delays.Count >= 3) cts.Cancel(); };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _provider.AcquireAsync(Shared(), cts.Token));

        Assert.Empty(_kiot.NewCalls);
        Assert.Equal(3, _clock.Delays.Count);
    }

    [Fact]
    public async Task KeyHetHan_LoiRo_DanhDauKeyChet_KhongThuLai()
    {
        _kiot.Override = url => url.Contains("/new") || url.Contains("/current")
            ? FakeHttp.Text(FakeKiot.KeyExpired, HttpStatusCode.BadRequest) : null;

        var r = await _provider.AcquireAsync(Shared(), CancellationToken.None);

        Assert.False(r.Ok);
        Assert.True(r.IsKeyDead);
        Assert.Contains("hết hạn", r.Error);
        Assert.Contains("…1234", r.Error);
        Assert.DoesNotContain("KEY-ABCD1234", r.Error);   // không lộ cả key vào log
        Assert.Empty(_kiot.NewCalls);                    // /current đã báo key chết
    }

    [Fact]
    public async Task SuccessFalse_ThuLaiCoGioiHan_RoiLoiRo()
    {
        _kiot.Override = url => url.Contains("/new")
            ? FakeHttp.Text("""{"success":false,"error":"SERVER_BUSY","message":"Hệ thống bận"}""") : null;

        var r = await _provider.AcquireAsync(Shared(), CancellationToken.None);

        Assert.False(r.Ok);
        Assert.False(r.IsKeyDead);
        Assert.Contains("Hệ thống bận", r.Error);
        Assert.Equal(4, _kiot.Http.Urls.Count(u => u.Contains("/new")));   // MaxNewAttempts mặc định
    }

    [Fact]
    public async Task KiotTraJsonHong_LoiRo()
    {
        _kiot.Override = url => url.Contains("/new") ? FakeHttp.Text("<html>502</html>", HttpStatusCode.BadGateway) : null;
        var r = await _provider.AcquireAsync(Shared(), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("không đọc được", r.Error);
    }

    [Fact]
    public async Task ChoQuaLau_LoiKhongCho()
    {
        _kiot.CurrentIp = "113.160.9.9";
        _kiot.NextChangeAt = SellerTestData.T0.AddMinutes(30);
        var r = await _provider.AcquireAsync(Shared(), CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Contains("quá lâu", r.Error);
        Assert.Empty(_kiot.NewCalls);
        Assert.Empty(_clock.Delays);
    }

    [Fact]
    public async Task KeyRiengGiuIp_ChiGoiCurrent()
    {
        _kiot.CurrentIp = "113.160.7.7";
        _kiot.NextChangeAt = SellerTestData.T0.AddMinutes(3);
        var src = new SellerProxySource(SellerProxyKind.Kiot, "OWN-KEY-9999", "nam", ProxyScheme.Socks5, RotatePerShop: false, null, IsShared: false);

        var r = await _provider.AcquireAsync(src, CancellationToken.None);

        Assert.True(r.Ok, r.Error);
        Assert.Empty(_kiot.NewCalls);
        Assert.Equal("113.160.7.7", r.Lease!.Host);
        Assert.Equal(30001, r.Lease.Port);                 // cổng socks5
        Assert.Equal(ProxyScheme.Socks5, r.Lease.Scheme);
        Assert.False(r.Lease.MustBeUnusedIp);
    }

    [Fact]
    public async Task Seed_MocTuLanChayTruoc_KhongGoiNewSom()
    {
        _provider.Seed("KEY-ABCD1234", SellerTestData.T0.AddSeconds(60));
        var r = await _provider.AcquireAsync(Shared(), CancellationToken.None);
        Assert.True(r.Ok);
        Assert.Equal(0, _kiot.CurrentCalls);
        Assert.True(_kiot.NewCalls.Single() >= SellerTestData.T0.AddSeconds(60));
    }

    [Fact]
    public async Task ProxyTinh_TraNguyen_KhongGoiMang()
    {
        Assert.True(StaticProxyParser.TryParse("1.2.3.4:8080:u:p", ProxyScheme.Http, out var lease, out _));
        var src = new SellerProxySource(SellerProxyKind.Static, null, "", ProxyScheme.Http, false, lease, false);
        var r = await _provider.AcquireAsync(src, CancellationToken.None);
        Assert.Same(lease, r.Lease);
        Assert.Empty(_kiot.Http.Urls);
    }

    [Fact]
    public void Resolve_KhongCoProxyRieng_DungKeyChung_ThieuKeyThiLoi()
    {
        var acc = new SellerAccount { Id = "a" };
        var settings = new SellerUploadSettings();
        Assert.Null(SellerProxySource.Resolve(acc, settings, out var err));
        Assert.Contains("key Kiot chung", err);

        settings.SharedProxy = new KiotProxySettings { KiotKey = " K1 ", Region = "trung", Type = ProxyScheme.Http };
        var s = SellerProxySource.Resolve(acc, settings, out _)!;
        Assert.Equal("K1", s.KiotKey);
        Assert.True(s.RotatePerShop);
        Assert.True(s.IsShared);

        acc.ProxyOverride = new SellerProxyOverride { Kind = SellerProxyKind.Static, Static = "1.2.3.4" };
        Assert.Null(SellerProxySource.Resolve(acc, settings, out err));
        Assert.Contains("Proxy tĩnh sai", err);
    }
}
