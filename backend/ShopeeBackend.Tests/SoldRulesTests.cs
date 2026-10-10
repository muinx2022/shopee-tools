using ShopeeBackend.Services;
using Xunit;

namespace ShopeeBackend.Tests;

public class SoldRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0);

    [Fact]
    public void Ca1_NhanThang_GhiVaoThang()
    {
        var sold = SoldRules.AssignLabeled(hasMonthLabel: true, value: 910);
        Assert.Equal(0, sold.Historical);
        Assert.Equal(910, sold.Monthly);
    }

    [Fact]
    public void Ca2_KhongNhan_GhiVaoTong()
    {
        var sold = SoldRules.AssignLabeled(hasMonthLabel: false, value: 800000);
        Assert.Equal(800000, sold.Historical);
        Assert.Equal(0, sold.Monthly);
    }

    [Fact]
    public void Ca3_JsonHaiSoKhacNhau_GiuCaHai()
    {
        var sold = SoldRules.AssignUnlabeled(historical: 15400, monthly: 1200);
        Assert.Equal(15400, sold.Historical);
        Assert.Equal(1200, sold.Monthly);
    }

    [Fact]
    public void Ca4_JsonHaiSoBangNhau_ChiGhiTong()
    {
        var sold = SoldRules.AssignUnlabeled(historical: 800000, monthly: 800000);
        Assert.Equal(800000, sold.Historical);
        Assert.Equal(0, sold.Monthly);
    }

    [Fact]
    public void Ca5_DuNgay_TruTrenTong()
    {
        var result = SoldRules.ApplyTotalReading(
            oldHistorical: 1000,
            previousSold: 1000,
            currentDelta: 0,
            lastTotalAt: Now.AddDays(-8),
            newHistorical: 1100,
            now: Now,
            minDays: 7);

        Assert.Equal(100, result.SoldDelta);
        Assert.Equal(1100, result.PreviousSold);
        Assert.Equal(Now, result.LastTotalAt);
        Assert.True(result.BaselineMoved);
    }

    [Fact]
    public void Ca6_ChuaDuNgay_GiuMocCu()
    {
        var baseline = Now.AddDays(-6);
        var result = SoldRules.ApplyTotalReading(
            oldHistorical: 1000,
            previousSold: 1000,
            currentDelta: 0,
            lastTotalAt: baseline,
            newHistorical: 1100,
            now: Now,
            minDays: 7);

        Assert.Equal(0, result.SoldDelta);
        Assert.Equal(1000, result.PreviousSold);
        Assert.Equal(baseline, result.LastTotalAt);
        Assert.False(result.BaselineMoved);
    }

    [Fact]
    public void Ca7_QuetThang_KhongDoiPhatSinh()
    {
        Assert.Equal(40, SoldRules.KeepDeltaOnMonthScan(40));
        Assert.Equal(0, SoldRules.KeepDeltaOnMonthScan(0));

        var monthOnly = SoldRules.AssignLabeled(hasMonthLabel: true, value: 910);
        Assert.Equal(0, monthOnly.Historical);
    }

    [Fact]
    public void Ca8_TongMoiNhoHonMoc_PhatSinhBangKhong()
    {
        var result = SoldRules.ApplyTotalReading(
            oldHistorical: 1000,
            previousSold: 1000,
            currentDelta: 15,
            lastTotalAt: Now.AddDays(-8),
            newHistorical: 900,
            now: Now,
            minDays: 7);

        Assert.Equal(0, result.SoldDelta);
        Assert.Equal(1000, result.PreviousSold);
        Assert.Equal(Now.AddDays(-8), result.LastTotalAt);
        Assert.False(result.BaselineMoved);
    }

    [Fact]
    public void Ca9_ChuaCoTongCu_PhatSinhBangKhong()
    {
        var result = SoldRules.ApplyTotalReading(
            oldHistorical: 0,
            previousSold: 0,
            currentDelta: 0,
            lastTotalAt: null,
            newHistorical: 800000,
            now: Now,
            minDays: 7);

        Assert.Equal(0, result.SoldDelta);
        Assert.Equal(800000, result.PreviousSold);
        Assert.Equal(Now, result.LastTotalAt);
        Assert.True(result.BaselineMoved);
    }
}
