using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>GĐ5d vòng 2 (NẶNG): host có ký tự vô hình / chữ số toàn khổ. .NET (<c>IdnHost</c>) "làm sạch" nên kiểm IP qua proxy vẫn đạt,
/// còn Brave gặp <c>--proxy-server</c> sai thì bỏ proxy, đi thẳng IP máy. Chặn nhiều lớp: parser, Kiot, kiểm IP, dựng tham số Brave.</summary>
public sealed class ProxyHostHardeningTests
{
    /// <summary>5 ca đã chạy thật: (host có ký tự lạ, vị trí ký tự lạ có ở HAI ĐẦU chuỗi nhập không).</summary>
    public static TheoryData<string, bool> BadHosts => new()
    {
        { "\uFEFF1.2.3.4", true },        // BOM đầu chuỗi
        { "\u200E127.0.0.1", true },      // LRM đầu chuỗi
        { "127.0.0.1\u00AD", false },     // soft hyphen cuối host (giữa chuỗi nhập)
        { "１２７.０.０.１", false },        // chữ số toàn khổ
        { "1.2.3.4\u200B", false },       // ZWSP cuối host
    };

    [Theory]
    [MemberData(nameof(BadHosts))]
    public void Parser_DangTab_VaDangHaiCham_KhongBaoGioRaHostLa(string host, bool atEdge)
    {
        foreach (var raw in new[] { $"{host}\t8080", $"{host}:8080", $"{host} 8080\tu1\tpw1", $"{host}:8080:u1:pw1" })
        {
            var ok = StaticProxyParser.TryParse(raw, ProxyScheme.Http, out var l, out var err);
            if (atEdge)
            {
                // Ký tự vô hình ở đầu chuỗi nhập: bỏ đi (hay dính khi chép) — host còn lại phải là ASCII sạch.
                Assert.True(ok, err);
                Assert.True(ProxyHostRules.IsSafeForBrowser(l!), l!.ServerArg);
                Assert.All(l.ServerArg, c => Assert.InRange(c, '!', '~'));
            }
            else
            {
                Assert.False(ok);
                Assert.Null(l);
                Assert.DoesNotContain(host, err);
                Assert.DoesNotContain("8080", err);
            }
        }
    }

    [Theory]
    [InlineData("1.2.3.4\uFEFF:8080")]          // BOM ở GIỮA → lỗi, không tự bỏ
    [InlineData("1.2.\u200B3.4\t8080")]
    [InlineData("1.2.3.4:８０８０")]               // cổng toàn khổ
    [InlineData("-bad.vn:8080")]
    [InlineData("bad-.vn:8080")]
    [InlineData("a..b:8080")]
    [InlineData("a_b.vn:8080")]
    [InlineData("1.2.3.4:8080:u\u0001:pw")]      // user có ký tự điều khiển
    [InlineData("1.2.3.4:8080:u1:p\u200Bw")]     // pass có ký tự vô hình
    [InlineData("http://u1:p%00w@1.2.3.4:8080")] // %00 sau khi giải mã
    public void Parser_KyTuLaOGiua_TuChoi(string raw)
    {
        Assert.False(StaticProxyParser.TryParse(raw, ProxyScheme.Http, out var l, out _));
        Assert.Null(l);
    }

    [Fact]
    public void Parser_TrimKyTuVoHinhHaiDau_CaCuoiChuoi()
    {
        Assert.True(StaticProxyParser.TryParse("\uFEFF\u200E 1.2.3.4\t8080 \u200B\r\n", ProxyScheme.Http, out var l, out var err), err);
        Assert.Equal("http://1.2.3.4:8080", l!.ServerArg);
    }

    [Theory]
    [MemberData(nameof(BadHosts))]
    public void BuildThamSoBrave_HostLa_Nem(string host, bool _)
    {
        var lease = new ProxyLease(ProxyScheme.Http, host, 8080);
        var ex = Assert.Throws<ArgumentException>(() => SellerBrowserArgs.Build(@"C:\p\profile", lease, null));
        Assert.DoesNotContain(host, ex.Message);
    }

    [Fact]
    public void BuildThamSoBrave_HostHopLe_VanChay()
    {
        Assert.Contains(SellerBrowserArgs.Build(@"C:\p\profile", new ProxyLease(ProxyScheme.Http, "proxy.example.vn", 3128), null),
            a => a == "--proxy-server=http://proxy.example.vn:3128");
        Assert.Contains(SellerBrowserArgs.Build(@"C:\p\profile", new ProxyLease(ProxyScheme.Http, "2001:DB8:0::1", 8080), null),
            a => a == "--proxy-server=http://[2001:DB8:0::1]:8080");
    }

    [Theory]
    [MemberData(nameof(BadHosts))]
    public async Task KiemIp_HostLa_TuChoiTruocMoiRequest(string host, bool _)
    {
        var world = new FakeIpWorld();
        var v = new ExitIpVerifier(world.Create);

        var r = await v.VerifyAsync(new ProxyLease(ProxyScheme.Http, host, 8080), null, CancellationToken.None);

        Assert.Equal(ExitIpFailure.BadProxyHost, r.Failure);
        Assert.Empty(world.Handlers);   // không gọi cả dịch vụ IP máy
    }

    [Theory]
    [InlineData("1.2.3.4", true)]
    [InlineData("Proxy.Example.VN", true)]
    [InlineData("2001:DB8:0::1", true)]   // .NET viết lại IPv6 — cùng địa chỉ
    [InlineData("127.1", false)]          // .NET viết lại thành 127.0.0.1 — khác chuỗi đưa Brave
    public void IdnHost_PhaiGiongHostDuaBrave(string host, bool same) =>
        Assert.Equal(same, ProxyHostRules.UriHostMatches(new ProxyLease(ProxyScheme.Http, host, 8080)));

    [Fact]
    public async Task Kiot_TraHostLa_TuChoi()
    {
        var clock = new FakeClock(SellerTestData.T0);
        var kiot = new FakeKiot(clock) { Override = _ => FakeHttp.Text(FakeKiot.Ok("１２７.０.０.１", null)) };
        var provider = new SellerProxyProvider(kiot.Api, new SellerProxyProviderOptions(), () => clock.Now, clock.Delay);
        var source = new SellerProxySource(SellerProxyKind.Kiot, "KEY-ABCD1234", "bac", ProxyScheme.Http, RotatePerShop: true, Static: null,
            IsShared: true);

        var r = await provider.AcquireAsync(source, CancellationToken.None);

        Assert.False(r.Ok);
        Assert.Contains("không hợp lệ", r.Error);
    }

    // ─────────────── IPv6 ───────────────

    [Fact]
    public void IPv6_HostPortUserPass_UserCoA_KhongBiDocThanhUrl()
    {
        Assert.True(StaticProxyParser.TryParse("[2001:db8::1]:8080:me@mail.com:99", ProxyScheme.Http, out var l, out var err), err);
        Assert.Equal("2001:db8::1", l!.Host);
        Assert.Equal(8080, l.Port);
        Assert.Equal("me@mail.com", l.User);
        Assert.Equal("99", l.Pass);
    }
}
