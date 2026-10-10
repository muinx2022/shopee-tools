using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Xunit;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

public class TiepTucShopKeTiepTests
{
    private static ShopListItem Shop(string id, string name, string login)
        => new(id, name, login);

    [Fact]
    public void TimViTriShop_TimTheoShopId_ThanhCong()
    {
        var shops = new List<ShopListItem>
        {
            Shop("101", "Shop Một", "shop.mot"),
            Shop("102", "Shop Hai", "shop.hai"),
            Shop("103", "Shop Ba", "shop.ba"),
        };

        var idx = OrdersBridgeSession.TimViTriShop(shops, "102");
        Assert.Equal(1, idx);
    }

    [Fact]
    public void TimViTriShop_TimTheoLoginName_KhongPhanBietHoaThuongKhoangTrang()
    {
        var shops = new List<ShopListItem>
        {
            Shop("101", "Shop Một", "shop.mot"),
            Shop("102", "Shop Hai", "shop.hai"),
            Shop("103", "Shop Ba", "shop.ba"),
        };

        var idx = OrdersBridgeSession.TimViTriShop(shops, "  SHOP.HAI  ");
        Assert.Equal(1, idx);
    }

    [Fact]
    public void TimViTriShop_TimTheoShopName_KhiKhongCoLoginName()
    {
        var shops = new List<ShopListItem>
        {
            Shop("101", "Shop A", ""),
            Shop("102", "Shop B", ""),
        };

        var idx = OrdersBridgeSession.TimViTriShop(shops, "Shop B");
        Assert.Equal(1, idx);
    }

    [Fact]
    public void TimViTriShop_ShopCuoi_TraVeIndexCuoi()
    {
        var shops = new List<ShopListItem>
        {
            Shop("101", "Shop 1", "shop1"),
            Shop("102", "Shop 2", "shop2"),
            Shop("103", "Shop 3", "shop3"),
        };

        var idx = OrdersBridgeSession.TimViTriShop(shops, "shop3");
        Assert.Equal(2, idx);
    }

    [Fact]
    public void TimViTriShop_KhongTimThay_HoacNull_TraVeAmMot()
    {
        var shops = new List<ShopListItem>
        {
            Shop("101", "Shop 1", "shop1"),
            Shop("102", "Shop 2", "shop2"),
        };

        Assert.Equal(-1, OrdersBridgeSession.TimViTriShop(shops, "shop_la"));
        Assert.Equal(-1, OrdersBridgeSession.TimViTriShop(shops, null));
        Assert.Equal(-1, OrdersBridgeSession.TimViTriShop(shops, "   "));
        Assert.Equal(-1, OrdersBridgeSession.TimViTriShop(Array.Empty<ShopListItem>(), "shop1"));
    }

    [Fact]
    public void LogicStartIdx_XongShop4_TiepTucShop5()
    {
        // 10 shop: index 0..9 (Shop 1..10)
        var shops = new List<ShopListItem>();
        for (int i = 1; i <= 10; i++)
        {
            shops.Add(Shop($"{i}00", $"Shop {i}", $"shop{i}"));
        }

        // Shop 4 đã xong -> mốc là shop4
        var lastShop = "shop4";
        var idx = OrdersBridgeSession.TimViTriShop(shops, lastShop);
        Assert.Equal(3, idx); // Shop 4 nằm ở index 3

        var startIdx = 0;
        if (idx >= 0 && idx < shops.Count - 1)
        {
            startIdx = idx + 1;
        }

        Assert.Equal(4, startIdx); // Shop 5 (index 4)
        Assert.Equal("shop5", shops[startIdx].LoginName);
    }

    [Fact]
    public void LogicStartIdx_DangDoShop4_MocLaShop3_ChayLaiTuShop4()
    {
        // Shop 3 xong -> mốc là shop3. Shop 4 đang chạy dở thì app đóng.
        var shops = new List<ShopListItem>();
        for (int i = 1; i <= 10; i++)
        {
            shops.Add(Shop($"{i}00", $"Shop {i}", $"shop{i}"));
        }

        var lastShop = "shop3"; // Shop 4 dở dang chưa hoàn tất nên mốc vẫn là shop3
        var idx = OrdersBridgeSession.TimViTriShop(shops, lastShop);
        Assert.Equal(2, idx); // Shop 3 nằm ở index 2

        var startIdx = 0;
        if (idx >= 0 && idx < shops.Count - 1)
        {
            startIdx = idx + 1;
        }

        Assert.Equal(3, startIdx); // Bắt đầu từ Shop 4 (index 3)
        Assert.Equal("shop4", shops[startIdx].LoginName);
    }

    [Fact]
    public void LogicStartIdx_XongShopCuoiCung_VongSauChayLaiTuShop1()
    {
        var shops = new List<ShopListItem>
        {
            Shop("101", "Shop 1", "shop1"),
            Shop("102", "Shop 2", "shop2"),
            Shop("103", "Shop 3", "shop3"),
        };

        var lastShop = "shop3"; // Đã xong shop cuối
        var idx = OrdersBridgeSession.TimViTriShop(shops, lastShop);
        Assert.Equal(2, idx);

        int startIdx;
        if (idx >= 0 && idx < shops.Count - 1)
        {
            startIdx = idx + 1;
        }
        else
        {
            startIdx = 0; // Xong shop cuối -> vòng sau bắt đầu lại từ shop 1
        }

        Assert.Equal(0, startIdx);
        Assert.Equal("shop1", shops[startIdx].LoginName);
    }

    [Fact]
    public void AccountRepository_GetVaSetLastCheckedShop_LuuVaDocDung()
    {
        using var temp = new TempDatabase();
        var repo = new AccountRepository(temp.Open());

        var id = repo.Insert(new Account
        {
            Email = "subacc@test.com",
            Password = "pass",
            Status = AccountStatus.HoatDong
        });

        // Ban đầu chưa có mốc
        Assert.Null(repo.GetLastCheckedShop(id));

        // Lưu shop4 đã xong
        repo.SetLastCheckedShop(id, "shop4.login");
        Assert.Equal("shop4.login", repo.GetLastCheckedShop(id));

        // Lưu shop5 đã xong
        repo.SetLastCheckedShop(id, "shop5.login");
        Assert.Equal("shop5.login", repo.GetLastCheckedShop(id));

        // Hoàn tất vòng -> xóa mốc (null)
        repo.SetLastCheckedShop(id, null);
        Assert.Null(repo.GetLastCheckedShop(id));
    }

    [Fact]
    public void Migration_DBCuChuaCoCot_TuDongThemCot_KhongLoi()
    {
        using var temp = new TempDatabase();
        var dbPath = temp.Path;

        // Dựng bảng accounts kiểu cũ KHÔNG có cột last_checked_shop_login
        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        using (var conn = new SqliteConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE accounts (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    Email      TEXT NOT NULL,
    Password   TEXT NOT NULL,
    Phone      TEXT,
    Cookie     TEXT,
    Note       TEXT,
    Status     TEXT NOT NULL,
    CreatedAt  TEXT NOT NULL,
    UpdatedAt  TEXT NOT NULL
);
INSERT INTO accounts (Email, Password, Status, CreatedAt, UpdatedAt)
VALUES ('old@test.com', 'pass', 'HoatDong', '2026-01-01T00:00:00', '2026-01-01T00:00:00');";
            cmd.ExecuteNonQuery();
        }

        // Mở lại qua Database -> migration chạy EnsureColumn
        var db = new Database(dbPath);
        var repo = new AccountRepository(db);

        // Đọc được tài khoản cũ
        var acc = repo.GetById(1);
        Assert.NotNull(acc);
        Assert.Equal("old@test.com", acc!.Email);

        // Đọc/ghi last_checked_shop_login hoạt động bình thường trên DB vừa migrate
        Assert.Null(repo.GetLastCheckedShop(1));
        repo.SetLastCheckedShop(1, "shop.resumed");
        Assert.Equal("shop.resumed", repo.GetLastCheckedShop(1));
    }
}
