using System.Net;
using System.Net.Http;
using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>GĐ4: <see cref="ExitIpVerifier"/> — IP ra phải lấy được, khác IP máy, khác mọi IP đã dùng cho shop khác (Kiot đổi IP); dịch vụ IP
/// dự phòng; handler thật đi thẳng KHÔNG proxy hệ thống, qua proxy có xác thực.</summary>
public sealed class ExitIpVerifierTests
{
    private readonly FakeIpWorld _world = new();
    private readonly ExitIpVerifier _v;

    public ExitIpVerifierTests() => _v = new ExitIpVerifier(_world.Create);

    private static ProxyLease Kiot(string ip) =>
        new(ProxyScheme.Http, ip, 20001, ExpectedIp: ip) { Source = ProxyLeaseSource.Kiot, MustBeUnusedIp = true };

    [Fact]
    public async Task IpRaKhacIpMay_Dat()
    {
        var r = await _v.VerifyAsync(Kiot("113.160.0.1"), null, CancellationToken.None);
        Assert.True(r.Ok, r.Reason);
        Assert.Equal("113.160.0.1", r.ExitIp);
        Assert.Equal("27.72.100.100", r.MachineIp);
        // IP máy hỏi KHÔNG qua proxy, IP ra hỏi QUA đúng proxy.
        Assert.Null(_world.Handlers[0]);
        Assert.Equal("http://113.160.0.1:20001", _world.Handlers[1]!.ServerArg);
    }

    [Fact]
    public async Task IpRaTrungIpMay_TuChoi()
    {
        _world.ExitIpFor = _ => "27.72.100.100";
        var r = await _v.VerifyAsync(Kiot("113.160.0.1"), null, CancellationToken.None);
        Assert.False(r.Ok);
        Assert.Equal(ExitIpFailure.SameAsMachine, r.Failure);
        Assert.Contains("TRÙNG IP máy", r.Reason);
    }

    [Fact]
    public async Task IpRaTrungMotIpDaDung_TuChoi_ProxyTinhThiKhongXet()
    {
        var used = new Dictionary<string, string> { ["113.160.0.9"] = "shop-9", [" 113.160.0.1\n"] = "shop-1" };
        var r = await _v.VerifyAsync(Kiot("113.160.0.1"), used, CancellationToken.None);
        Assert.Equal(ExitIpFailure.AlreadyUsed, r.Failure);
        Assert.Contains("shop-1", r.Reason);

        // Proxy tĩnh (1 shop 1 IP) không có luật "khác shop trước".
        var stat = new ProxyLease(ProxyScheme.Http, "113.160.0.1", 8080);
        Assert.True((await _v.VerifyAsync(stat, used, CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task KhongLayDuocIpMay_TuChoi_KhongHoiQuaProxy()
    {
        _world.MachineIp = null;
        var r = await _v.VerifyAsync(Kiot("113.160.0.1"), null, CancellationToken.None);
        Assert.Equal(ExitIpFailure.MachineIpUnavailable, r.Failure);
        Assert.DoesNotContain(_world.Handlers, h => h is not null);
    }

    [Fact]
    public async Task DichVuChinhLoi_DungDuPhong()
    {
        _world.Override = (host, lease) => host == "api.ipify.org" ? FakeHttp.Text("oops", HttpStatusCode.ServiceUnavailable) : null;
        var r = await _v.VerifyAsync(Kiot("113.160.0.1"), null, CancellationToken.None);
        Assert.True(r.Ok, r.Reason);
        Assert.Contains("checkip.amazonaws.com|http://113.160.0.1:20001", _world.Hits);
    }

    [Fact]
    public async Task DichVuTraRacKhongPhaiIp_CoiNhuLoi_SangDuPhong()
    {
        _world.Override = (host, lease) => host == "api.ipify.org" && lease is not null ? FakeHttp.Text("12345") : null;
        var r = await _v.VerifyAsync(Kiot("113.160.0.1"), null, CancellationToken.None);
        Assert.True(r.Ok, r.Reason);
        Assert.Equal("113.160.0.1", r.ExitIp);
    }

    [Fact]
    public async Task HetDichVu_LoiIpRa()
    {
        _world.Override = (host, lease) => lease is null ? null : throw new HttpRequestException("proxy refused");
        var r = await _v.VerifyAsync(Kiot("113.160.0.1"), null, CancellationToken.None);
        Assert.Equal(ExitIpFailure.ExitIpUnavailable, r.Failure);
        Assert.Contains("proxy refused", r.Reason);
        Assert.Contains("api.ipify.org", r.Reason);
        Assert.Contains("checkip.amazonaws.com", r.Reason);
    }

    [Fact]
    public async Task IpMay_HoiLaiMoiLanKiem()
    {
        Assert.True((await _v.VerifyAsync(Kiot("113.160.0.1"), null, CancellationToken.None)).Ok);
        // IP nhà mạng đổi giữa 2 shop: lần kiểm sau phải thấy IP máy mới.
        _world.MachineIp = "113.160.0.2";
        var r = await _v.VerifyAsync(Kiot("113.160.0.2"), null, CancellationToken.None);
        Assert.Equal(ExitIpFailure.SameAsMachine, r.Failure);
        Assert.Equal(2, _world.Handlers.Count(h => h is null));
    }

    [Fact]
    public void HandlerThat_DiThangKhongProxyHeThong_QuaProxyCoXacThuc()
    {
        using var direct = (SocketsHttpHandler)ExitIpVerifier.CreateHandler(null);
        Assert.False(direct.UseProxy);

        var lease = new ProxyLease(ProxyScheme.Http, "1.2.3.4", 8080, "alice", "s3cret");
        using var via = (SocketsHttpHandler)ExitIpVerifier.CreateHandler(lease);
        Assert.True(via.UseProxy);
        var proxy = Assert.IsType<WebProxy>(via.Proxy);
        Assert.Equal(new Uri("http://1.2.3.4:8080"), proxy.Address);
        Assert.False(proxy.BypassProxyOnLocal);
        var cred = proxy.Credentials!.GetCredential(proxy.Address!, "Basic")!;
        Assert.Equal("alice", cred.UserName);
        Assert.Equal("s3cret", cred.Password);

        using var noAuth = (SocketsHttpHandler)ExitIpVerifier.CreateHandler(new ProxyLease(ProxyScheme.Socks5, "1.2.3.4", 1080));
        var p2 = Assert.IsType<WebProxy>(noAuth.Proxy);
        Assert.Equal(new Uri("socks5://1.2.3.4:1080"), p2.Address);
        Assert.Null(p2.Credentials);
    }
}
