using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Hai hàm thuần đỡ đường MỞ LẠI TRÌNH DUYỆT giữa vòng: <see cref="OrdersBridgeSession.ConLuotMoLai"/> (còn được
/// mở lại nữa không) và <see cref="OrdersBridgeSession.GhepShopThuLaiConLai"/> (phần hàng đợi thử lại còn lại sau
/// khi mở lại).
/// <para>
/// Cả hai sinh ra từ lượt phản biện 18/08/2026 của bản vá "chạm trần 3 shop hỏng thì mở lại thay vì dừng vòng".
/// </para>
/// </summary>
public class MoLaiTrinhDuyetGiuaVongTests
{
    private static ShopListItem Shop(string id) => new(id, "Shop " + id, "shop" + id + ".store");

    // ── ConLuotMoLai ────────────────────────────────────────────────────────────────────────────────────────
    // Ngưỡng mở lại từng nằm ở HAI chỗ (QuyetDinhKhiMatPicker tự so ">= trần", chỗ gọi chạm-trần tự so "< trần").
    // Hai chỗ tự so là hai chỗ có thể lệch nhau: phản biện chỉ ra rằng đổi một bên thành "<=" (cho 3 lần mở lại
    // thay vì 2) KHÔNG làm đỏ test nào. Gom về một hàm rồi khoá bằng test.

    [Theory]
    [InlineData(0, true)]   // chưa mở lại lần nào
    [InlineData(1, true)]   // đã mở lại 1 lần — còn 1 lượt
    [InlineData(2, false)]  // chạm trần TranMoLaiTrinhDuyet = 2
    [InlineData(3, false)]  // vượt trần (không nên xảy ra, nhưng không được nới)
    public void ConLuotMoLai_TheoTran(int soLanDaMoLai, bool mongDoi)
        => Assert.Equal(mongDoi, OrdersBridgeSession.ConLuotMoLai(soLanDaMoLai));

    /// <summary>
    /// Ngưỡng phải là MỘT: <see cref="OrdersBridgeSession.QuyetDinhKhiMatPicker"/> hết lượt đúng lúc
    /// <see cref="OrdersBridgeSession.ConLuotMoLai"/> nói hết. Lệch nhau là vòng vừa quyết mở lại đã bị chặn ngay
    /// sau đó — bản vá tưởng có tác dụng mà không.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ConLuotMoLai_KhopVoiQuyetDinhKhiMatPicker(int soLanDaMoLai)
    {
        var conLuot = OrdersBridgeSession.ConLuotMoLai(soLanDaMoLai);
        var quyet = OrdersBridgeSession.QuyetDinhKhiMatPicker(
            conShopChuaChay: true, soLanDaMoLai, captcha: false);
        Assert.Equal(conLuot, quyet == KhiMatPicker.MoLaiTrinhDuyet);
    }

    // ── GhepShopThuLaiConLai ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// CA CHÍNH — cái mà phản biện 18/08/2026 bắt được. Trong LƯỢT THỬ LẠI, mọi shop đều đã nằm trong
    /// <c>daChayShopId</c> (lượt chính đã đụng hết), nên <see cref="OrdersBridgeSession.GhepShopConLai"/> trả
    /// RỖNG và <c>BienMat</c> cũng rỗng ⇒ phần hàng đợi còn lại bị bỏ IM LẶNG. Test giữ CẢ HAI khẳng định cạnh
    /// nhau để lần sau không ai gộp hai đường lại làm một.
    /// </summary>
    [Fact]
    public void LuotThuLai_GiuNguyenPhanConLai_DuMoiShopDaChay()
    {
        var picker = new[] { Shop("1"), Shop("2"), Shop("3"), Shop("4") };
        var conLaiCuaHangDoi = new[] { Shop("2"), Shop("3") };
        var daChay = new HashSet<string>(new[] { "1", "2", "3", "4" }, StringComparer.Ordinal);

        var thuLai = OrdersBridgeSession.GhepShopThuLaiConLai(picker, conLaiCuaHangDoi);
        Assert.Equal(new[] { "2", "3" }, thuLai.ConLai.Select(s => s.ShopId));
        Assert.Empty(thuLai.BienMat);

        // Chính đường CŨ mới là thứ làm mất việc — giữ lại để nói rõ vì sao phải tách hàm.
        var duongCu = OrdersBridgeSession.GhepShopConLai(picker, conLaiCuaHangDoi, daChay);
        Assert.Empty(duongCu.ConLai);
        Assert.Empty(duongCu.BienMat);
    }

    /// <summary>Shop biến mất khỏi picker sau khi mở lại → KHÔNG chạy, và phải lọt ra BienMat để caller ghi nhật
    /// ký. Nuốt im lặng là đúng cái tội đang đi sửa.</summary>
    [Fact]
    public void ShopBienMatKhoiPicker_ThiVaoBienMat_KhongVaoConLai()
    {
        var picker = new[] { Shop("1"), Shop("3") };
        var conLaiCuaHangDoi = new[] { Shop("2"), Shop("3") };

        var ghep = OrdersBridgeSession.GhepShopThuLaiConLai(picker, conLaiCuaHangDoi);

        Assert.Equal(new[] { "3" }, ghep.ConLai.Select(s => s.ShopId));
        Assert.Equal(new[] { "shop2.store" }, ghep.BienMat);
    }

    /// <summary>Giữ ĐÚNG thứ tự của hàng đợi, không lấy theo thứ tự picker (picker đọc lại có thể xếp khác).</summary>
    [Fact]
    public void GiuThuTuCuaHangDoi_KhongTheoThuTuPicker()
    {
        var picker = new[] { Shop("3"), Shop("1"), Shop("2") };
        var conLaiCuaHangDoi = new[] { Shop("2"), Shop("1") };

        var ghep = OrdersBridgeSession.GhepShopThuLaiConLai(picker, conLaiCuaHangDoi);

        Assert.Equal(new[] { "2", "1" }, ghep.ConLai.Select(s => s.ShopId));
    }

    /// <summary>Hàng đợi đã hết → rỗng cả hai vế, không nổ.</summary>
    [Fact]
    public void HangDoiRong_ThiRong()
    {
        var ghep = OrdersBridgeSession.GhepShopThuLaiConLai(
            new[] { Shop("1") }, Array.Empty<ShopListItem>());

        Assert.Empty(ghep.ConLai);
        Assert.Empty(ghep.BienMat);
    }

    /// <summary>Picker đọc lại rỗng (trang hỏng) → không shop nào chạy, TẤT CẢ vào BienMat để được ghi nhật ký.</summary>
    [Fact]
    public void PickerRong_ThiTatCaVaoBienMat()
    {
        var ghep = OrdersBridgeSession.GhepShopThuLaiConLai(
            Array.Empty<ShopListItem>(), new[] { Shop("2"), Shop("3") });

        Assert.Empty(ghep.ConLai);
        Assert.Equal(2, ghep.BienMat.Count);
    }
}
