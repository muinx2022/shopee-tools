using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// MỘT SHOP HỎNG KHÔNG ĐƯỢC GIẾT CẢ VÒNG — bảng quyết định
/// <see cref="OrdersBridgeSession.QuyetDinhSauShopHong"/>.
/// <para>
/// Bối cảnh (10/08/2026): cầu nối rớt giữa chặng "Chuẩn bị hàng" ở shop 6/12, ngoại lệ xuyên thẳng ra khối catch
/// cuối <c>RunAllShopsAsync</c> ⇒ 6 shop còn lại không hề được mở. Vòng duyệt 12 shop nghỉ 3–5' mỗi shop nên mất
/// 6 shop là mất gần một giờ và phải chờ vòng sau.
/// </para>
/// <para>Bảng này giữ đúng HAI đường vẫn phải dừng: chạm trần hỏng liên tiếp, và không quay lại được trang chọn
/// shop khi còn shop phía sau (shop kế chắc chắn không mở nổi).</para>
/// <para><b>Sửa 18/08/2026:</b> chạm trần KHÔNG còn mặc nhiên là dừng vòng. Điều tra nhật ký 8 ngày
/// (11–17/08/2026) tìm ra 6 chuỗi "chờ 30s chưa thấy tab shop mở", mỗi chuỗi ĐÚNG 3 shop liên tiếp và giết trọn
/// một vòng; không chuỗi nào tự khỏi (0/6) nhưng vòng chạy trên trình duyệt vừa mở lại đi trọn 12/12 shop
/// (<b>6/6</b>), giá 5 giây. Nay chạm trần mà còn đường cứu thì MỞ LẠI trình duyệt.</para>
/// </summary>
public class ShopHongKhongGietCaVongTests
{
    /// <summary>Đường cứu ĐỦ điều kiện: còn shop phía sau, còn lượt mở lại, không dính captcha. Mọi ca không nói
    /// gì khác về ba biến này đều dùng bộ mặc định "còn cứu được" để chỉ đổi ĐÚNG một biến mỗi ca.</summary>
    private static SauShopHong Quyet(
        int hongLienTiep, bool conShopPhiaSau, bool veDuocPicker, bool conLuotMoLai = true, bool captcha = false)
        => OrdersBridgeSession.QuyetDinhSauShopHong(
            hongLienTiep, conShopPhiaSau, veDuocPicker, conLuotMoLai, captcha);

    [Fact]
    public void HongMotShop_ConShopPhiaSau_VeDuocPicker_ThiChayTiep()
    {
        // Ca chính của bản vá: shop 6 hỏng, shop 7..12 vẫn phải được chạy.
        Assert.Equal(SauShopHong.ChayTiepShopKe, Quyet(1, conShopPhiaSau: true, veDuocPicker: true));
    }

    [Fact]
    public void HongDuoiTran_ThiVanChayTiep()
    {
        // Ngay SÁT trần vẫn phải chạy tiếp — trần là mốc dừng, không phải mốc cảnh báo.
        Assert.Equal(SauShopHong.ChayTiepShopKe,
            Quyet(OrdersBridgeSession.TranShopHongLienTiep - 1, conShopPhiaSau: true, veDuocPicker: true));
    }

    [Fact]
    public void KhongVeDuocPicker_ConShopPhiaSau_ThiDungVong()
    {
        // Luật CŨ giữ nguyên: không về được trang chọn shop thì shop kế chắc chắn chết.
        Assert.Equal(SauShopHong.DungVongKhongVePicker, Quyet(1, conShopPhiaSau: true, veDuocPicker: false));
    }

    [Fact]
    public void KhongVeDuocPicker_NhungLaSHOPCUOI_ThiKhongDungVong()
    {
        // Luật CŨ giữ nguyên: picker hỏng sau shop cuối không hại ai — vòng đằng nào cũng kết thúc.
        Assert.Equal(SauShopHong.ChayTiepShopKe, Quyet(1, conShopPhiaSau: false, veDuocPicker: false));
    }

    // ===== Chạm trần: mở lại trình duyệt thay vì bỏ trắng nửa vòng (18/08/2026) =====

    /// <summary>Ca CHÍNH của bản vá: 3 shop liên tiếp hỏng, còn shop phía sau, còn lượt mở lại, không captcha
    /// ⇒ mở lại trình duyệt chứ KHÔNG dừng vòng. Đây đúng hình 6 chuỗi trong nhật ký 11–17/08/2026.</summary>
    [Fact]
    public void ChamTran_ConDuongCuu_ThiMoLaiTrinhDuyet()
    {
        Assert.Equal(SauShopHong.MoLaiTrinhDuyetHongLienTiep,
            Quyet(OrdersBridgeSession.TranShopHongLienTiep, conShopPhiaSau: true, veDuocPicker: false));
    }

    /// <summary>Vượt trần vẫn đi đường cứu — trần so bằng <c>&gt;=</c>, không phải <c>==</c>.</summary>
    [Fact]
    public void VuotTran_ConDuongCuu_ThiVanMoLaiTrinhDuyet()
    {
        Assert.Equal(SauShopHong.MoLaiTrinhDuyetHongLienTiep,
            Quyet(OrdersBridgeSession.TranShopHongLienTiep + 5, conShopPhiaSau: true, veDuocPicker: true));
    }

    /// <summary>HẾT lượt mở lại ⇒ dừng vòng. Không có chốt này thì trình duyệt hỏng HẲN kéo vòng vào chuỗi
    /// mở–đóng vô tận.</summary>
    [Fact]
    public void ChamTran_HetLuotMoLai_ThiDungVong()
    {
        Assert.Equal(SauShopHong.DungVongHongLienTiep,
            Quyet(OrdersBridgeSession.TranShopHongLienTiep, conShopPhiaSau: true, veDuocPicker: false,
                conLuotMoLai: false));
    }

    /// <summary>Đang dính CAPTCHA ⇒ dừng vòng. Đẩy thêm một lượt mở trình duyệt vào đúng lúc Shopee đang nghi là
    /// bot chính là tự khai — cùng lý lẽ với <see cref="OrdersBridgeSession.QuyetDinhKhiMatPicker"/>.</summary>
    [Fact]
    public void ChamTran_DangCaptcha_ThiDungVong()
    {
        Assert.Equal(SauShopHong.DungVongHongLienTiep,
            Quyet(OrdersBridgeSession.TranShopHongLienTiep, conShopPhiaSau: true, veDuocPicker: false,
                captcha: true));
    }

    /// <summary>Không còn việc phía sau ⇒ mở lại trình duyệt chẳng cứu được ai, dừng vòng.</summary>
    [Fact]
    public void ChamTran_KhongConShopPhiaSau_ThiDungVong()
    {
        Assert.Equal(SauShopHong.DungVongHongLienTiep,
            Quyet(OrdersBridgeSession.TranShopHongLienTiep, conShopPhiaSau: false, veDuocPicker: true));
    }

    /// <summary>
    /// <b>HAI HÀM PHẢI CÙNG KẾT LUẬN.</b> Chỗ gọi dùng <c>MoLaiTrinhDuyetHongLienTiep</c> để gán
    /// <c>matPicker</c> rồi rơi vào khối mở lại đã có — mà khối đó hỏi LẠI
    /// <see cref="OrdersBridgeSession.QuyetDinhKhiMatPicker"/>. Lệch một ca là vòng vừa quyết mở lại đã rơi thẳng
    /// vào <see cref="KhiMatPicker.DungVong"/>: log nói "MỞ LẠI" mà thực tế bỏ trắng nửa vòng y như cũ.
    /// </summary>
    [Theory]
    [InlineData(0, false)]                                              // ca chính: còn lượt, không captcha
    [InlineData(OrdersBridgeSession.TranMoLaiTrinhDuyet - 1, false)]    // lượt CUỐI vẫn phải mở
    [InlineData(OrdersBridgeSession.TranMoLaiTrinhDuyet, false)]        // hết lượt ⇒ cả hai cùng dừng
    [InlineData(0, true)]                                               // captcha ⇒ cả hai cùng dừng
    public void ChamTran_KetLuanKhopVoiQuyetDinhKhiMatPicker(int soLanDaMoLai, bool captcha)
    {
        var conLuotMoLai = soLanDaMoLai < OrdersBridgeSession.TranMoLaiTrinhDuyet;

        var sauShopHong = Quyet(
            OrdersBridgeSession.TranShopHongLienTiep, conShopPhiaSau: true, veDuocPicker: false,
            conLuotMoLai: conLuotMoLai, captcha: captcha);
        var khiMatPicker = OrdersBridgeSession.QuyetDinhKhiMatPicker(
            conShopChuaChay: true, soLanDaMoLai: soLanDaMoLai, captcha: captcha);

        Assert.Equal(
            sauShopHong == SauShopHong.MoLaiTrinhDuyetHongLienTiep,
            khiMatPicker == KhiMatPicker.MoLaiTrinhDuyet);
    }
}
