using Shopee.Core.Harvest;
using static Shopee.Core.Tests.HarvestTestData;

namespace Shopee.Core.Tests;

/// <summary>GĐ5 — chữ overlay của vòng Harvest: dòng kết quả (tên rút gọn · biến thể · ảnh · video đã lưu link) và bảng
/// tổng kết cuối khối (có/không dòng Hub).</summary>
public sealed class HarvestOverlayTextTests
{
    [Fact]
    public void RowSaved_SpCoVideo_2Tang()
    {
        var s = HarvestOverlayText.TrySummarize(Fixture("pdp-2tang-8model.json"));

        var line = HarvestOverlayText.RowSaved(12, s);

        Assert.StartsWith("✓ Đã xong dòng 12: ", line);
        Assert.Contains(" · 8 biến thể · 5 ảnh · 🎬 Có video (đã lưu link)", line);
        Assert.DoesNotContain("\n", line);
    }

    [Fact]
    public void RowSaved_KhongVideo_1Tang()
    {
        var line = HarvestOverlayText.RowSaved(3, HarvestOverlayText.TrySummarize(Fixture("pdp-1tang-70model-4capdm.json")));
        Assert.EndsWith(" · 70 biến thể · 9 ảnh · Không có video", line);
    }

    [Fact]
    public void RowSaved_SpKhongBienThe()
    {
        var line = HarvestOverlayText.RowSaved(4, HarvestOverlayText.TrySummarize(Fixture("pdp-khong-bien-the.json")));
        Assert.Contains(" · không biến thể · ", line);
    }

    [Fact]
    public void RowSaved_KhongCoTomTat_ChiPhanDau()
    {
        Assert.Equal("✓ Đã xong dòng 7", HarvestOverlayText.RowSaved(7, null));
        Assert.Null(HarvestOverlayText.TrySummarize("{ hỏng"));
        Assert.Null(HarvestOverlayText.TrySummarize(null));
    }

    [Fact]
    public void ShortName_CatDung60KyTu_GopDong_KhongCatDoiEmoji()
    {
        var longName = new string('a', 80);
        var cut = HarvestOverlayText.ShortName(longName);
        Assert.Equal(60, cut.Length);
        Assert.EndsWith("…", cut);

        Assert.Equal("Áo thun nữ", HarvestOverlayText.ShortName("  Áo\nthun   nữ "));

        var emoji = new string('b', 58) + "😀😀";       // 58 + 4 code unit: ký tự thứ 59-60 là cặp surrogate
        var e = HarvestOverlayText.ShortName(emoji);
        Assert.False(char.IsHighSurrogate(e[^2]), "không được để lẻ nửa emoji trước dấu …");
        Assert.True(e.Length <= 60);
    }

    [Fact]
    public void BlockDone_CoVaKhongCoDongHub()
    {
        var hub = HarvestOverlayText.HubLine(120, 3, 1);
        Assert.Equal("Hub: đã nhận 120 · app chờ đẩy 3 · lỗi 1", hub);

        Assert.Equal("✓ Xong dòng 2–61 (shop A)\nĐã lấy: 58 · Bỏ qua/lỗi: 2\nHub: đã nhận 120 · app chờ đẩy 3 · lỗi 1",
            HarvestOverlayText.BlockDone(2, 61, "shop A", 58, 2, hub));
        Assert.Equal("✓ Xong dòng 2–61 (shop A)\nĐã lấy: 58 · Bỏ qua/lỗi: 2",
            HarvestOverlayText.BlockDone(2, 61, "shop A", 58, 2, null));
        Assert.Equal("✓ Xong dòng 2–61\nĐã lấy: 0 · Bỏ qua/lỗi: 0",
            HarvestOverlayText.BlockDone(2, 61, " ", 0, 0, ""));
    }
}
