using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>GĐ4: đọc proxy tĩnh <c>host:port[:user:pass]</c> — đủ dạng hợp lệ và từ chối dạng sai.</summary>
public sealed class StaticProxyParserTests
{
    [Theory]
    [InlineData("1.2.3.4:8080", ProxyScheme.Http, ProxyScheme.Http, "1.2.3.4", 8080, null, null)]
    [InlineData(" 1.2.3.4:8080 ", ProxyScheme.Socks5, ProxyScheme.Socks5, "1.2.3.4", 8080, null, null)]
    [InlineData("proxy.example.vn:3128:user1:pa:ss", ProxyScheme.Http, ProxyScheme.Http, "proxy.example.vn", 3128, "user1", "pa:ss")]
    [InlineData("http://1.2.3.4:8080:u:p", ProxyScheme.Socks5, ProxyScheme.Http, "1.2.3.4", 8080, "u", "p")]
    [InlineData("socks5://1.2.3.4:1080", ProxyScheme.Http, ProxyScheme.Socks5, "1.2.3.4", 1080, null, null)]
    [InlineData("HTTP://u%40x:p%3Aw@1.2.3.4:8080/", ProxyScheme.Http, ProxyScheme.Http, "1.2.3.4", 8080, "u@x", "p:w")]
    [InlineData("[2001:db8::1]:8080", ProxyScheme.Http, ProxyScheme.Http, "2001:db8::1", 8080, null, null)]
    [InlineData("http://[2001:db8::1]:8080:u:p", ProxyScheme.Http, ProxyScheme.Http, "2001:db8::1", 8080, "u", "p")]
    public void HopLe(string raw, ProxyScheme def, ProxyScheme scheme, string host, int port, string? user, string? pass)
    {
        Assert.True(StaticProxyParser.TryParse(raw, def, out var l, out var err), err);
        Assert.Equal(scheme, l!.Scheme);
        Assert.Equal(host, l.Host);
        Assert.Equal(port, l.Port);
        Assert.Equal(user, l.User);
        Assert.Equal(pass, l.Pass);
        Assert.Equal(ProxyLeaseSource.Static, l.Source);
        Assert.False(l.MustBeUnusedIp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3.4:")]
    [InlineData("1.2.3.4:0")]
    [InlineData("1.2.3.4:70000")]
    [InlineData("1.2.3.4:80a")]
    [InlineData("1.2.3.4:8080:user")]
    [InlineData("1.2.3.4:8080::pass")]
    [InlineData("1.2.3.4:8080:user:")]
    [InlineData("https://1.2.3.4:8080")]
    [InlineData("ftp://1.2.3.4:21")]
    [InlineData(":8080")]
    [InlineData("bad host:8080")]
    [InlineData("[2001:db8::1:8080")]
    [InlineData("[not-ip]:8080")]
    [InlineData("socks5://1.2.3.4:1080:u:p")]
    [InlineData("u:p@1.2.3.4:8080:u2:p2")]
    public void TuChoi(string raw)
    {
        Assert.False(StaticProxyParser.TryParse(raw, ProxyScheme.Http, out var l, out var err));
        Assert.Null(l);
        Assert.False(string.IsNullOrWhiteSpace(err));
    }

    /// <summary>Nhập sai thứ tự / sai dạng: câu lỗi KHÔNG được chứa mẩu nào user nhập (mẩu đó có thể chính là mật khẩu).</summary>
    [Theory]
    [InlineData("myuser:Secret99:1.2.3.4:8080", "Secret99")]
    [InlineData("1.2.3.4:8080@user:Secret99", "Secret99")]
    [InlineData("Sec!ret99:8080:u:p", "Sec!ret99")]
    [InlineData("[Secret99]:8080", "Secret99")]
    [InlineData("Secret99://1.2.3.4:8080", "Secret99")]
    public void LoiKhongChuaChuoiUserNhap(string raw, string secret)
    {
        Assert.False(StaticProxyParser.TryParse(raw, ProxyScheme.Http, out _, out var err));
        Assert.DoesNotContain(secret, err);
        Assert.DoesNotContain("8080", err);
        Assert.DoesNotContain("1.2.3.4", err);
    }

    [Fact]
    public void MatKhauCoA_DangHostPortUserPass()
    {
        Assert.True(StaticProxyParser.TryParse("1.2.3.4:8080:alice:p@ss@w", ProxyScheme.Http, out var l, out var err), err);
        Assert.Equal("1.2.3.4", l!.Host);
        Assert.Equal(8080, l.Port);
        Assert.Equal("alice", l.User);
        Assert.Equal("p@ss@w", l.Pass);
    }

    [Fact]
    public void MatKhauCoA_DangUrl()
    {
        Assert.True(StaticProxyParser.TryParse("http://alice:p@ss@1.2.3.4:8080", ProxyScheme.Http, out var l, out var err), err);
        Assert.Equal("1.2.3.4", l!.Host);
        Assert.Equal("alice", l.User);
        Assert.Equal("p@ss", l.Pass);
    }

    [Fact]
    public void Socks5CoMatKhau_LoiNoiRoLyDo()
    {
        Assert.False(StaticProxyParser.TryParse("1.2.3.4:1080:u:p", ProxyScheme.Socks5, out _, out var err));
        Assert.Contains("SOCKS5", err);
    }

    [Fact]
    public void Lease_ServerArgKhongCoMatKhau_ToStringKhongLoMatKhau()
    {
        Assert.True(StaticProxyParser.TryParse("[2001:db8::1]:8080:alice:s3cret", ProxyScheme.Http, out var l, out _));
        Assert.Equal("http://[2001:db8::1]:8080", l!.ServerArg);
        Assert.DoesNotContain("s3cret", l.ToString());
        Assert.DoesNotContain("alice", l.ToString());
        Assert.True(l.HasCredentials);
    }

    /// <summary>Gõ ngược thứ tự mà cú pháp vẫn "hợp lệ": user là IPv4, host không phải IP và không có dấu chấm → từ chối, lỗi không
    /// chứa input.</summary>
    [Theory]
    [InlineData("myuser:12345:1.2.3.4:8080")]
    [InlineData("http://admin:3128:10.0.0.1:pw")]
    public void GoNguocThuTu_TuChoi_LoiKhongChuaInput(string raw)
    {
        Assert.False(StaticProxyParser.TryParse(raw, ProxyScheme.Http, out var l, out var err));
        Assert.Null(l);
        Assert.Contains("ngược thứ tự", err);
        foreach (var part in raw.Replace("http://", "").Split(':')) Assert.DoesNotContain(part, err);
    }

    [Theory]
    [InlineData("1.2.3.4:8080:me@mail.com:12345", "1.2.3.4", 8080, "me@mail.com", "12345")]
    [InlineData("proxy.vn:8080:10.0.0.1:pw", "proxy.vn", 8080, "10.0.0.1", "pw")]   // host có dấu chấm → không coi là gõ ngược
    [InlineData("localhost:8080:alice:pw", "localhost", 8080, "alice", "pw")]
    public void KhongPhaiGoNguoc_DocDung(string raw, string host, int port, string user, string pass)
    {
        Assert.True(StaticProxyParser.TryParse(raw, ProxyScheme.Http, out var l, out var err), err);
        Assert.Equal(host, l!.Host);
        Assert.Equal(port, l.Port);
        Assert.Equal(user, l.User);
        Assert.Equal(pass, l.Pass);
    }
}
