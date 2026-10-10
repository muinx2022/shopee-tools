using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Luật chỗ IP máy / trùng proxy của <see cref="ProxyLan.KiemTraChoLan"/>. Cờ
/// <c>choPhepTatCaQuaIpMay</c> bật thì bỏ qua toàn bộ check.
/// </summary>
public class ProxyLanKiemTraChoLanTests
{
    private static readonly LanDangChay Acc1IpMay = new(1, null, null);
    private static readonly LanDangChay Acc2ProxyA = new(2, "http://10.0.0.1:8000", "http://10.0.0.1:8000");
    private static readonly LanDangChay Acc3CungProxyA = new(3, "http://10.0.0.1:8000", "http://10.0.0.1:8000");
    private static readonly LanDangChay Acc4CungKey = new(4, "kiot-key-x", "http://10.0.0.2:9000");

    [Fact]
    public void CoTat_HaiAccKhongProxy_TuChoi_LyDoChuaIdDangGiu()
    {
        var kq = ProxyLan.KiemTraChoLan(
            choPhepTatCaQuaIpMay: false,
            dangChay: new[] { Acc1IpMay },
            accountId: 9,
            cauHinhGoc: null,
            proxyDaGiai: null);

        Assert.False(kq.Ok);
        Assert.Contains("1", kq.LyDo);
        Assert.Contains("IP máy", kq.LyDo);
    }

    [Fact]
    public void CoTat_MotAccIpMay_AccKiaProxyKhac_ChapNhan()
    {
        var kq = ProxyLan.KiemTraChoLan(
            choPhepTatCaQuaIpMay: false,
            dangChay: new[] { Acc1IpMay },
            accountId: 9,
            cauHinhGoc: Acc2ProxyA.CauHinhGoc,
            proxyDaGiai: Acc2ProxyA.ProxyDaGiai);

        Assert.True(kq.Ok);
        Assert.Null(kq.LyDo);
    }

    [Fact]
    public void CoTat_HaiAccCungHostPort_TuChoi()
    {
        var kq = ProxyLan.KiemTraChoLan(
            choPhepTatCaQuaIpMay: false,
            dangChay: new[] { Acc2ProxyA },
            accountId: Acc3CungProxyA.AccountId,
            cauHinhGoc: Acc3CungProxyA.CauHinhGoc,
            proxyDaGiai: Acc3CungProxyA.ProxyDaGiai);

        Assert.False(kq.Ok);
        Assert.Contains("2", kq.LyDo);
        Assert.Contains("proxy trùng", kq.LyDo);
    }

    [Fact]
    public void CoTat_HaiAccCungKeyKiot_TuChoi()
    {
        var kq = ProxyLan.KiemTraChoLan(
            choPhepTatCaQuaIpMay: false,
            dangChay: new[] { Acc4CungKey },
            accountId: 8,
            cauHinhGoc: "kiot-key-x",
            proxyDaGiai: "http://10.0.0.8:1111");

        Assert.False(kq.Ok);
        Assert.Contains("4", kq.LyDo);
        Assert.Contains("proxy trùng", kq.LyDo);
    }

    [Fact]
    public void CoBat_HaiAccKhongProxy_ChapNhan()
    {
        var kq = ProxyLan.KiemTraChoLan(
            choPhepTatCaQuaIpMay: true,
            dangChay: new[] { Acc1IpMay },
            accountId: 9,
            cauHinhGoc: null,
            proxyDaGiai: null);

        Assert.True(kq.Ok);
        Assert.Null(kq.LyDo);
    }

    [Fact]
    public void CoBat_HaiAccCungProxy_ChapNhan()
    {
        var kq = ProxyLan.KiemTraChoLan(
            choPhepTatCaQuaIpMay: true,
            dangChay: new[] { Acc2ProxyA },
            accountId: Acc3CungProxyA.AccountId,
            cauHinhGoc: Acc3CungProxyA.CauHinhGoc,
            proxyDaGiai: Acc3CungProxyA.ProxyDaGiai);

        Assert.True(kq.Ok);
        Assert.Null(kq.LyDo);
    }

    [Fact]
    public void AccTuXetChinhNoTrongDanhSach_KhongTuChan()
    {
        var kq = ProxyLan.KiemTraChoLan(
            choPhepTatCaQuaIpMay: false,
            dangChay: new[] { Acc1IpMay },
            accountId: Acc1IpMay.AccountId,
            cauHinhGoc: null,
            proxyDaGiai: null);

        Assert.True(kq.Ok);
        Assert.Null(kq.LyDo);
    }
}
