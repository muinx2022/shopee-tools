using System.Collections.Generic;
using XuLyDonShopee.App.ViewModels;

namespace XuLyDonShopee.Tests;

public class ShopPrepareRowTests
{
    [Fact]
    public void ShopHienThi_KhiChuaCoSoSp_HienDauGach()
    {
        var row = new ShopPrepareRow("Shop A", "shop.a", 0);
        Assert.Equal("Shop A (Tổng số sp: — / —)", row.ShopHienThi);
    }

    [Fact]
    public void ShopHienThi_KhiCoSoSpVaGioiHan_HienDungDinhDang()
    {
        var row = new ShopPrepareRow("Shop A", "shop.a", 0, tongSoSp: 150, gioiHanSp: 1000);
        Assert.Equal("Shop A (Tổng số sp: 150 / 1000)", row.ShopHienThi);
    }

    [Fact]
    public void ShopHienThi_KhiChiCoMotGiaTri_HienHopLe()
    {
        var row = new ShopPrepareRow("Shop B", "shop.b", 0, tongSoSp: 80);
        Assert.Equal("Shop B (Tổng số sp: 80 / —)", row.ShopHienThi);

        var row2 = new ShopPrepareRow("Shop C", "shop.c", 0, gioiHanSp: 500);
        Assert.Equal("Shop C (Tổng số sp: — / 500)", row2.ShopHienThi);
    }

    [Fact]
    public void DoiSoSp_BanNotificationChoShopHienThi()
    {
        var row = new ShopPrepareRow("Shop A", "shop.a", 0);
        var changedProps = new List<string>();
        row.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is not null)
            {
                changedProps.Add(e.PropertyName);
            }
        };

        row.TongSoSp = 200;
        Assert.Contains(nameof(ShopPrepareRow.TongSoSp), changedProps);
        Assert.Contains(nameof(ShopPrepareRow.ShopHienThi), changedProps);
        Assert.Equal("Shop A (Tổng số sp: 200 / —)", row.ShopHienThi);

        row.GioiHanSp = 3000;
        Assert.Contains(nameof(ShopPrepareRow.GioiHanSp), changedProps);
        Assert.Equal("Shop A (Tổng số sp: 200 / 3000)", row.ShopHienThi);
    }
}
