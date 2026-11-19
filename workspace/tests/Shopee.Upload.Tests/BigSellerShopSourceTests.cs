using Shopee.Core.BigSeller;
using Shopee.Upload.Import;

namespace Shopee.Upload.Tests;

/// <summary>Đọc shop BigSeller (GĐ5c vòng 2): nhãn rỗng, file <c>bigseller.json</c> hỏng. Chỉ dùng file tạm, không đụng file thật.</summary>
public sealed class BigSellerShopSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "upload-bs-source-tests-" + Guid.NewGuid().ToString("N"));

    public BigSellerShopSourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void NhanRong_HienTKBigSellerSoThuTu_Kem6KyTuId_KhongDungEmail()
    {
        List<BigSellerAccount> accounts =
        [
            new() { Id = "abcdef0123456789", Label = "", Email = "an@secret-mail.vn", Shops = [new() { Name = "S1" }] },
            new() { Id = "id2", Label = "  ", Email = "hai@secret-mail.vn", Shops = [new() { Name = "S2" }] },
            new() { Id = "zzz999", Label = "Có nhãn", Email = "ba@secret-mail.vn", Shops = [new() { Name = "S3" }, new() { Name = "S4" }] },
        ];

        var shops = BigSellerShopSource.Flatten(accounts);

        Assert.Equal(["TK BigSeller 1 (abcdef)", "TK BigSeller 2 (id2)", "Có nhãn", "Có nhãn"], shops.Select(s => s.AccountLabel));
        Assert.DoesNotContain(shops, s => s.AccountLabel.Contains("secret-mail"));
        var plan = BigSellerShopImport.Plan(shops, []);
        Assert.Equal("TK BigSeller 1 (abcdef)", plan.ToAdd[0].AccountDisplay);
    }

    private string Write(string text)
    {
        var p = Path.Combine(_dir, "bigseller.json");
        File.WriteAllText(p, text);
        return p;
    }

    [Fact]
    public void FileKhongCo_HoacRong_HoacDanhSachRong_KhongLoi()
    {
        BigSellerShopSource.EnsureLoaded(Path.Combine(_dir, "khong-co.json"), 0);
        BigSellerShopSource.EnsureLoaded(Write("   "), 0);
        BigSellerShopSource.EnsureLoaded(Write("[]"), 0);
        BigSellerShopSource.EnsureLoaded(Write("{ hỏng"), 2);   // Store đã đọc được tài khoản → không kiểm lại
    }

    [Fact]
    public void FileHong_BaoKhongDocDuoc_KhongTrichNoiDung()
    {
        var p = Write("[{\"Id\":\"a\",\"Password\":\"BiMat#Json1\",\"Shops\":[ hỏng");

        var ex = Assert.Throws<BigSellerReadException>(() => BigSellerShopSource.EnsureLoaded(p, 0));

        Assert.Contains("Không đọc được bigseller.json", ex.Message);
        Assert.DoesNotContain("BiMat#Json1", ex.Message);
    }

    [Fact]
    public void FileCoTaiKhoan_MaStoreRong_BaoLoi()
    {
        var p = Write("[{\"Id\":\"a\",\"Shops\":[]}]");
        var ex = Assert.Throws<BigSellerReadException>(() => BigSellerShopSource.EnsureLoaded(p, 0));
        Assert.Contains("1 tài khoản", ex.Message);
    }

    [Fact]
    public void StoreRongMaFileCoTaiKhoan_NapLaiMotLanRoiMoiQuyet()
    {
        var p = Write("[{\"Id\":\"a\",\"Shops\":[{\"Name\":\"S1\"}]}]");
        List<BigSellerAccount> loaded = [];
        var reloads = 0;

        var shops = BigSellerShopSource.Read(p, () => loaded, () =>
        {
            reloads++;
            loaded = [new() { Id = "a", Shops = [new() { Name = "S1" }] }];   // lần nạp lại đọc được (Workspace đã ghi xong)
        });

        Assert.Equal(1, reloads);
        Assert.Equal("S1", Assert.Single(shops).ShopName);
    }

    [Fact]
    public void NapLaiVanRong_BaoLoi_ChiNapLaiMotLan()
    {
        var p = Write("{ hỏng");
        var reloads = 0;
        Assert.Throws<BigSellerReadException>(() => BigSellerShopSource.Read(p, () => [], () => reloads++));
        Assert.Equal(1, reloads);
    }

    [Fact]
    public void StoreCoTaiKhoan_KhongNapLai()
    {
        var reloads = 0;
        var shops = BigSellerShopSource.Read(Path.Combine(_dir, "khong-co.json"),
            () => [new() { Id = "a", Shops = [new() { Name = "S1" }] }], () => reloads++);
        Assert.Equal(0, reloads);
        Assert.Single(shops);
    }

    [Fact]
    public void FileDangBiKhoa_BaoLoi()
    {
        var p = Write("[{\"Id\":\"a\"}]");
        using var lockIt = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Throws<BigSellerReadException>(() => BigSellerShopSource.EnsureLoaded(p, 0));
    }
}
