using System.Net;
using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>GĐ4: <see cref="SellerRunGate"/> — KHÔNG có đường nào mở Brave khi chưa kiểm IP đạt; mở với đúng proxy vừa kiểm;
/// mọi nhánh lỗi trả lý do.</summary>
public sealed class SellerRunGateTests
{
    private const string Profile = @"C:\p\shop";
    private readonly FakeClock _clock = new(SellerTestData.T0);
    private readonly FakeKiot _kiot;
    private readonly FakeIpWorld _ips = new();
    private readonly FakeLauncher _launcher = new();
    private readonly SellerRunGate _gate;
    private readonly SellerUploadSettings _settings = new()
    {
        SharedProxy = new KiotProxySettings { KiotKey = "SHARED-KEY-0001", Region = "bac" },
    };

    public SellerRunGateTests()
    {
        _kiot = new FakeKiot(_clock);
        var provider = new SellerProxyProvider(_kiot.Api, new SellerProxyProviderOptions { Tick = TimeSpan.FromMinutes(1) },
            () => _clock.Now, _clock.Delay);
        var verifier = new ExitIpVerifier(_ips.Create);
        _gate = new SellerRunGate(provider, verifier, _launcher, clock: () => _clock.Now, delay: _clock.Delay);
    }

    private static SellerAccount Acc(string id = "shop-1") => new() { Id = id };

    private Task<SellerGateResult> Open(SellerAccount? a = null) =>
        _gate.OpenAsync(a ?? Acc(), _settings, Profile, [@"C:\ext\seller-upload"], CancellationToken.None);

    [Fact]
    public async Task IpDat_MoBraveDungProxyVuaKiem()
    {
        var r = await Open();

        Assert.True(r.Ok, r.Reason);
        var req = Assert.Single(_launcher.Requests);
        Assert.Equal("113.160.0.1", req.Proxy.Host);
        Assert.Equal("113.160.0.1", req.ExitIp);
        Assert.Equal(Profile, req.ProfileDir);
        Assert.Equal([@"C:\ext\seller-upload"], req.ExtensionDirs);
        // Proxy đưa cho Brave đúng là proxy đã kiểm IP.
        Assert.Equal(req.Proxy.ServerArg, _ips.Handlers.Last()!.ServerArg);
        Assert.Equal("113.160.0.1", _gate.PreviousShopIp);
        Assert.Equal("shop-1", Assert.Single(_gate.UsedIps).AccountId);
    }

    [Fact]
    public async Task IpRaTrungIpMay_KhongMoBrave()
    {
        _ips.ExitIpFor = _ => _ips.MachineIp;
        var r = await Open();
        Assert.False(r.Ok);
        Assert.Equal(SellerGateFailure.ExitIpRejected, r.Failure);
        Assert.Contains("TRÙNG IP máy", r.Reason);
        Assert.Empty(_launcher.Requests);
        Assert.Equal(2, _kiot.NewCalls.Count);      // thử lại có giới hạn bằng IP mới
        Assert.Empty(_gate.UsedIps);
    }

    [Fact]
    public async Task KhongBietIpMay_KhongMoBrave()
    {
        _ips.MachineIp = null;
        var r = await Open();
        Assert.Equal(SellerGateFailure.ExitIpRejected, r.Failure);
        Assert.Empty(_launcher.Requests);
    }

    [Fact]
    public async Task ProxyChet_KhongMoBrave()
    {
        _ips.ExitIpFor = _ => null;
        var r = await Open();
        Assert.Equal(SellerGateFailure.ExitIpRejected, r.Failure);
        Assert.Empty(_launcher.Requests);
    }

    [Fact]
    public async Task ShopKe_KiotChuaDoiIp_ThuLaiSauMoc_RoiMoVoiIpMoi()
    {
        Assert.True((await Open(Acc("shop-1"))).Ok);
        var t1 = _clock.Now;
        // Lần /new kế Kiot vẫn trả IP cũ (chưa đổi thật), lần sau mới ra IP mới.
        _kiot.Ips[1] = "113.160.0.1";

        var r = await Open(Acc("shop-2"));

        Assert.True(r.Ok, r.Reason);
        Assert.Equal(2, _launcher.Requests.Count);
        Assert.Equal("113.160.0.3", _launcher.Requests[1].Proxy.Host);
        Assert.Equal(3, _kiot.NewCalls.Count);
        // Không lần /new nào gọi trước mốc 5 phút của IP trước.
        Assert.True(_kiot.NewCalls[1] >= t1 + TimeSpan.FromMinutes(5));
        Assert.True(_kiot.NewCalls[2] >= _kiot.NewCalls[1] + TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ShopKe_KiotMaiTraIpCu_BoQuaShop_KhongMoBrave()
    {
        Assert.True((await Open(Acc("shop-1"))).Ok);
        _kiot.Ips[1] = _kiot.Ips[2] = "113.160.0.1";

        var r = await Open(Acc("shop-2"));

        Assert.Equal(SellerGateFailure.ExitIpRejected, r.Failure);
        Assert.Contains("đã dùng cho shop shop-1", r.Reason);
        Assert.Single(_launcher.Requests);
    }

    [Fact]
    public async Task ABA_ShopThuBaNhanLaiIpCuaShopDau_TuChoi()
    {
        Assert.True((await Open(Acc("shop-1"))).Ok);          // .0.1
        Assert.True((await Open(Acc("shop-2"))).Ok);          // .0.2
        _kiot.Ips[2] = _kiot.Ips[3] = "113.160.0.1";          // Kiot quay về IP của shop-1

        var r = await Open(Acc("shop-3"));

        Assert.Equal(SellerGateFailure.ExitIpRejected, r.Failure);
        Assert.Contains("đã dùng cho shop shop-1", r.Reason);
        Assert.Equal(2, _launcher.Requests.Count);
    }

    [Fact]
    public async Task CungTaiKhoanChayLai_IpCuCuaChinhNo_KhongTinhTrung()
    {
        Assert.True((await Open(Acc("shop-1"))).Ok);
        _kiot.Ips[1] = "113.160.0.1";
        Assert.True((await Open(Acc("shop-1"))).Ok);
    }

    [Fact]
    public async Task ChuaLayDuocIpRa_ThuLaiCungProxy_RoiMoi()
    {
        // Proxy mới đổi IP: 2 lần kiểm đầu chưa thông (đếm theo số lần tạo handler qua proxy = số lần kiểm).
        _ips.ExitIpFor = l => _ips.Handlers.Count(h => h is not null) <= 2 ? null : l.Host;

        var r = await Open();

        Assert.True(r.Ok, r.Reason);
        Assert.Single(_kiot.NewCalls);                         // không tốn thêm lần đổi IP
        Assert.Equal(2, _clock.Delays.Count(d => d == TimeSpan.FromSeconds(5)));
        Assert.Equal("113.160.0.1", _launcher.Requests.Single().Proxy.Host);
    }

    [Fact]
    public async Task ProxyKhongBaoGioThong_ThuCungProxy3Lan_RoiMoiDoiIp()
    {
        _ips.ExitIpFor = _ => null;
        var r = await Open();
        Assert.Equal(SellerGateFailure.ExitIpRejected, r.Failure);
        Assert.Equal(2, _kiot.NewCalls.Count);                 // 2 vòng
        Assert.Equal(8, _ips.Handlers.Count(h => h is not null)); // mỗi vòng 1 + 3 lần thử lại cùng proxy
        Assert.Empty(_launcher.Requests);
    }

    [Fact]
    public async Task KeyChungChet_LoiCaLuot_KhongMoBrave()
    {
        _kiot.Override = _ => FakeHttp.Text(FakeKiot.KeyExpired, HttpStatusCode.BadRequest);
        var r = await Open();
        Assert.Equal(SellerGateFailure.KiotKeyDead, r.Failure);
        Assert.True(r.IsFleetWide);
        Assert.Empty(_launcher.Requests);
    }

    [Fact]
    public async Task KeyRiengChet_ChiBoShopNay()
    {
        _kiot.Override = _ => FakeHttp.Text(FakeKiot.KeyExpired, HttpStatusCode.BadRequest);
        var a = Acc();
        a.ProxyOverride = new SellerProxyOverride { Kind = SellerProxyKind.Kiot, KiotKey = "OWN-KEY-0002" };
        var r = await Open(a);
        Assert.Equal(SellerGateFailure.KiotKeyDead, r.Failure);
        Assert.False(r.IsFleetWide);
        Assert.Empty(_launcher.Requests);
    }

    [Fact]
    public async Task ThieuCauHinhProxy_KhongGoiMang_KhongMoBrave()
    {
        _settings.SharedProxy.KiotKey = null;
        var r = await Open();
        Assert.Equal(SellerGateFailure.ProxyConfig, r.Failure);
        Assert.Empty(_kiot.Http.Urls);
        Assert.Empty(_ips.Handlers);
        Assert.Empty(_launcher.Requests);
    }

    [Fact]
    public async Task ProxyTinh_IpDat_MoBrave_CoUserPass()
    {
        var a = Acc();
        a.ProxyOverride = new SellerProxyOverride { Kind = SellerProxyKind.Static, Static = "45.1.2.3:8080:u:p" };
        var r = await Open(a);
        Assert.True(r.Ok, r.Reason);
        var req = Assert.Single(_launcher.Requests);
        Assert.Equal("u", req.Proxy.User);
        Assert.Empty(_kiot.Http.Urls);
    }

    [Fact]
    public async Task MoBraveLoi_TraLyDo()
    {
        _launcher.Throw = new InvalidOperationException("Còn Brave đang chạy trên profile");
        var r = await Open();
        Assert.Equal(SellerGateFailure.LaunchFailed, r.Failure);
        Assert.Contains("Còn Brave", r.Reason);
        Assert.Equal("113.160.0.1", r.ExitIp);
        // Mở lỗi thì IP không tính là đã dùng / IP shop trước.
        Assert.Empty(_gate.UsedIps);
        Assert.Null(_gate.PreviousShopIp);
    }

    [Fact]
    public async Task MoBraveLoi_ShopKeKhongBiSoVoiIpCuaShopLoi()
    {
        Assert.True((await Open(Acc("shop-1"))).Ok);
        _launcher.Throw = new InvalidOperationException("hỏng");
        Assert.Equal(SellerGateFailure.LaunchFailed, (await Open(Acc("shop-2"))).Failure);   // IP .0.2

        Assert.Equal("113.160.0.1", _gate.PreviousShopIp);
        Assert.Equal(["shop-1"], _gate.UsedIps.Select(u => u.AccountId));
    }

    [Fact]
    public async Task Huy_NemHuy_KhongMoBrave()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _gate.OpenAsync(Acc(), _settings, Profile, [], cts.Token));
        Assert.Empty(_launcher.Requests);
    }
}
