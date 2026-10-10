using XuLyDonShopee.App.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Test hàm thuần <see cref="AccountSession.PhutNghiTruocVongKe"/> — vòng BỊ CẮT giữa chừng thì nghỉ NGẮN rồi
/// chạy lại, thay vì bắt phần shop bị bỏ chờ trọn interval đầy.
/// <para>
/// Bối cảnh (nhật ký 11–17/08/2026): 6 lần một chuỗi 3 shop hỏng liên tiếp cắt trọn vòng, mỗi lần bỏ trắng 6–9
/// shop. Vòng NGAY SAU đó chạy trọn 12/12 shop, 6/6 lần — tức phần shop bị bỏ chỉ cần một vòng nữa, không cần
/// một chu kỳ 90'. Bắt chúng chờ đủ interval là trả giá hai lần cho cùng một sự cố.
/// </para>
/// <para>
/// <b>Chốt quan trọng — chỉ vòng cắt ĐẦU TIÊN được ưu đãi.</b> Vòng cắt thứ hai liên tiếp nghĩa là hỏng dai; dồn
/// vòng ngắn vào Shopee lúc đó vừa vô ích vừa giống hành vi bot. Không có chốt này thì một sự cố hạ tầng kéo dài
/// biến thành chuỗi vòng 15' liên tục.
/// </para>
/// </summary>
public class NghiNganSauVongCatTests
{
    /// <summary>Ca CHÍNH của bản vá: vòng cắt đầu tiên → nghỉ ngắn thay vì trọn interval.</summary>
    [Fact]
    public void VongCatDauTien_ThiNghiNgan()
        => Assert.Equal(AccountSession.PhutNghiSauVongCat,
            AccountSession.PhutNghiTruocVongKe(vongBiCat: true, soVongCatLienTiepTruocDo: 0, intervalMin: 90));

    /// <summary>Vòng cắt THỨ HAI liên tiếp → quay về interval đầy. Chốt chống dập Shopee khi hỏng dai.</summary>
    [Fact]
    public void VongCatThuHaiLienTiep_ThiVeIntervalDay()
        => Assert.Equal(90,
            AccountSession.PhutNghiTruocVongKe(vongBiCat: true, soVongCatLienTiepTruocDo: 1, intervalMin: 90));

    /// <summary>Chuỗi cắt dài vẫn giữ interval đầy — so bằng <c>== 0</c> chứ không phải "chưa quá N".</summary>
    [Fact]
    public void ChuoiVongCatDai_ThiVanIntervalDay()
        => Assert.Equal(90,
            AccountSession.PhutNghiTruocVongKe(vongBiCat: true, soVongCatLienTiepTruocDo: 7, intervalMin: 90));

    /// <summary>Vòng chạy TRỌN → nghỉ như cấu hình, không đụng gì tới hành vi cũ.</summary>
    [Fact]
    public void VongKhongBiCat_ThiNghiNhuCu()
        => Assert.Equal(90,
            AccountSession.PhutNghiTruocVongKe(vongBiCat: false, soVongCatLienTiepTruocDo: 0, intervalMin: 90));

    /// <summary>Vòng chạy trọn SAU một chuỗi cắt cũng nghỉ đủ interval — biến <c>vongBiCat</c> mới là cửa chính,
    /// bộ đếm chỉ là điều kiện phụ.</summary>
    [Fact]
    public void VongKhongBiCat_DuTruocDoDaCat_ThiVanNghiDuInterval()
        => Assert.Equal(90,
            AccountSession.PhutNghiTruocVongKe(vongBiCat: false, soVongCatLienTiepTruocDo: 3, intervalMin: 90));

    /// <summary>
    /// Người dùng đặt interval NGẮN HƠN mốc nghỉ ngắn (10' &lt; 15') ⇒ vẫn nghỉ 10'. "Nghỉ ngắn" không bao giờ
    /// được phép biến thành nghỉ DÀI hơn cấu hình — đó là lý do có <c>Math.Min</c>.
    /// </summary>
    [Fact]
    public void IntervalNganHonMocNghiNgan_ThiGiuIntervalCuaNguoiDung()
        => Assert.Equal(10,
            AccountSession.PhutNghiTruocVongKe(vongBiCat: true, soVongCatLienTiepTruocDo: 0, intervalMin: 10));

    /// <summary>Interval đúng bằng mốc → hai đường trùng nhau, không có bậc nhảy lạ ở biên.</summary>
    [Fact]
    public void IntervalBangMocNghiNgan_ThiKhongDoi()
        => Assert.Equal(AccountSession.PhutNghiSauVongCat,
            AccountSession.PhutNghiTruocVongKe(
                vongBiCat: true, soVongCatLienTiepTruocDo: 0,
                intervalMin: AccountSession.PhutNghiSauVongCat));
}
