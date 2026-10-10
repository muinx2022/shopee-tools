using XuLyDonShopee.App.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Hai hàm thuần quyết định nhịp nghỉ sau một vòng: <see cref="AccountSession.LaVongBiCat"/> (vòng vừa rồi có
/// thật sự bỏ dở việc không) và <see cref="AccountSession.NghiSauVong"/> (phút nghỉ + chuỗi vòng cắt mới, trả
/// CÙNG MỘT LÚC).
/// <para>
/// Cả hai sinh ra từ lượt phản biện 18/08/2026. <see cref="NghiNganSauVongCatTests"/> canh phần "nghỉ bao nhiêu";
/// file này canh phần "vòng nào mới được tính là cắt" và "chuỗi cộng có đúng nhịp không".
/// </para>
/// </summary>
public class VongBiCatPhanLoaiTests
{
    // ── LaVongBiCat ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Ca CHÍNH: vòng trả lỗi hạ tầng (3 shop hỏng liên tiếp, mất trang chọn shop…) → đúng là bị cắt.</summary>
    [Fact]
    public void LoiHaTang_LaVongBiCat()
        => Assert.True(AccountSession.LaVongBiCat(coLoi: true, captcha: false, loiDiaChi: false));

    /// <summary>Vòng trọn vẹn thì không có gì để cứu.</summary>
    [Fact]
    public void KhongLoi_ThiKhongPhaiVongBiCat()
        => Assert.False(AccountSession.LaVongBiCat(coLoi: false, captcha: false, loiDiaChi: false));

    /// <summary>Captcha là chuyện của Shopee, không phải hạ tầng hỏng — dồn vòng vào lúc đang bị nghi là bot chỉ
    /// làm nặng thêm.</summary>
    [Fact]
    public void Captcha_ThiKhongPhaiVongBiCat()
        => Assert.False(AccountSession.LaVongBiCat(coLoi: true, captcha: true, loiDiaChi: false));

    /// <summary>
    /// LỖI PHẢN BIỆN BẮT ĐƯỢC 18/08/2026 — tái hiện bằng nhật ký 12/08 13:52 (<c>Xong 1 vòng: 11/12 shop … bỏ qua
    /// 1 shop lỗi địa chỉ</c>). Vòng đó trả <c>Error</c> non-null nhưng ĐÃ đi hết mọi shop; tính là "bị cắt" thì
    /// nó được nghỉ ngắn 15' rồi lao vào một vòng 12 shop thao tác thật sớm hơn 75 phút — không cứu được gì.
    /// </summary>
    [Fact]
    public void ChiLoiDiaChi_ThiKHONGPhaiVongBiCat()
        => Assert.False(AccountSession.LaVongBiCat(coLoi: true, captcha: false, loiDiaChi: true));

    /// <summary>Vừa captcha vừa lỗi địa chỉ — vẫn không phải vòng cắt (hai vế loại trừ độc lập nhau).</summary>
    [Fact]
    public void CaptchaVaLoiDiaChi_ThiKhongPhaiVongBiCat()
        => Assert.False(AccountSession.LaVongBiCat(coLoi: true, captcha: true, loiDiaChi: true));

    // ── NghiSauVong ─────────────────────────────────────────────────────────────────────────────────────────
    // Vì sao gộp phút nghỉ + chuỗi mới vào MỘT hàm: ở chỗ gọi chúng từng là hai dòng phải chạy đúng thứ tự (tính
    // phút TRƯỚC khi cộng chuỗi). Phản biện chỉ ra đảo hai dòng đó thì nghỉ ngắn chết IM LẶNG mà không test nào
    // đỏ. Gộp lại thì không còn thứ tự nào để đảo — và các test dưới khoá luôn nhịp cộng.

    /// <summary>Vòng cắt ĐẦU TIÊN: nghỉ ngắn VÀ chuỗi lên 1.</summary>
    [Fact]
    public void VongCatDauTien_NghiNgan_VaChuoiLen1()
    {
        var (phutNghi, chuoi) = AccountSession.NghiSauVong(
            vongBiCat: true, soVongCatLienTiepTruocDo: 0, intervalMin: 90);

        Assert.Equal(AccountSession.PhutNghiSauVongCat, phutNghi);
        Assert.Equal(1, chuoi);
    }

    /// <summary>Vòng cắt THỨ HAI liên tiếp: interval đầy VÀ chuỗi lên 2. Đây là ca mà việc "cộng chuỗi trước khi
    /// tính phút" sẽ giết mất ưu đãi của vòng cắt đầu tiên.</summary>
    [Fact]
    public void VongCatThuHai_IntervalDay_VaChuoiLen2()
    {
        var (phutNghi, chuoi) = AccountSession.NghiSauVong(
            vongBiCat: true, soVongCatLienTiepTruocDo: 1, intervalMin: 90);

        Assert.Equal(90, phutNghi);
        Assert.Equal(2, chuoi);
    }

    /// <summary>Vòng chạy trọn: interval đầy VÀ chuỗi RESET về 0 — vòng cắt kế tiếp lại được ưu đãi.</summary>
    [Fact]
    public void VongTron_ThiChuoiReset()
    {
        var (phutNghi, chuoi) = AccountSession.NghiSauVong(
            vongBiCat: false, soVongCatLienTiepTruocDo: 5, intervalMin: 90);

        Assert.Equal(90, phutNghi);
        Assert.Equal(0, chuoi);
    }

    /// <summary>Phút nghỉ phải luôn KHỚP hàm phút riêng lẻ — hai đường không được lệch.</summary>
    [Theory]
    [InlineData(true, 0, 90)]
    [InlineData(true, 1, 90)]
    [InlineData(false, 0, 90)]
    [InlineData(true, 0, 10)]
    public void PhutNghi_KhopVoiPhutNghiTruocVongKe(bool vongBiCat, int chuoiTruocDo, int intervalMin)
    {
        var (phutNghi, _) = AccountSession.NghiSauVong(vongBiCat, chuoiTruocDo, intervalMin);

        Assert.Equal(
            AccountSession.PhutNghiTruocVongKe(vongBiCat, chuoiTruocDo, intervalMin),
            phutNghi);
    }
}
