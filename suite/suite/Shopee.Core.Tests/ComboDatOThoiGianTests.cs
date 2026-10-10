namespace Shopee.Core.Tests;

/// <summary>
/// Lưới cho <c>datOThoiGian</c> của <b>flow-combo.js</b> — guard "chọn NGÀY hỏng ⇒ KHÔNG bấm Xác nhận"
/// chuyển từ bản km (vá 27/08/2026) sang combo ngày 30/08/2026 (sổ P-20260829-2).
/// <para><b>Vì sao đáng có lưới riêng:</b> bấm Xác nhận trên một picker chưa chọn được gì là CHỐT GIÁ TRỊ CŨ
/// và xoá sạch bằng chứng về cú trượt — chỉ còn "ĐỌC LẠI KHÔNG KHỚP" không nói được vì sao. Bản km có test
/// giữ thứ tự này (<c>EdsONgayHopLeTests.DatOThoiGian_ChonNgayHong_ThiKhongDiToiXacNhan</c>); combo trước
/// 30/08 đi TUYẾN TÍNH và không lưới nào canh. Cùng khuôn "soi nguyên văn nguồn" với test bên km.</para>
/// </summary>
public class ComboDatOThoiGianTests
{
    private const string FileCombo = "flow-combo.js";

    /// <summary>
    /// Thứ tự trong thân hàm phải là: click ngày → guard "KHÔNG bấm Xác nhận" → mới tới
    /// <c>pageXacNhanPopup</c>; và guard phải kiểm đúng khuôn <c>if (!ngay || !ngay.ok)</c>.
    /// </summary>
    [Fact]
    public void DatOThoiGianCombo_ChonNgayHong_ThiKhongDiToiXacNhan()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileCombo, "datOThoiGian");

        Assert.Contains("if (!ngay || !ngay.ok)", nguon);
        Assert.Contains("KHÔNG bấm Xác nhận", nguon);

        var viNgay = nguon.IndexOf("pageClickNgayTheoSo", StringComparison.Ordinal);
        var viGuard = nguon.IndexOf("KHÔNG bấm Xác nhận", StringComparison.Ordinal);
        var viXacNhan = nguon.IndexOf("pageXacNhanPopup", StringComparison.Ordinal);
        Assert.True(viNgay >= 0 && viGuard >= 0 && viXacNhan >= 0, "thiếu một trong ba mốc của thứ tự guard");
        Assert.True(viNgay < viGuard, "guard phải đứng SAU cú click ngày");
        Assert.True(viGuard < viXacNhan, "guard phải đứng TRƯỚC cú Xác nhận — hỏng ở đâu dừng ở đó");
    }

    /// <summary>
    /// Nhánh guard phải mang <c>loi</c> có tên (bước gọi b4/b5 nối <c>dat.loi</c> lên nhật ký) — không có nó
    /// thì cú trượt lại thành một bước đỏ câm đúng như trước khi vá.
    /// </summary>
    [Fact]
    public void DatOThoiGianCombo_GuardMangLoiCoTen()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileCombo, "datOThoiGian");
        Assert.Contains("loi: manh.join(", nguon);

        // Và chỗ gọi phải NỐI `dat.loi` lên bước — soi cả hai bước b4/b5 trong hàm chạy chính.
        var chay = FlashSaleDoDomTests.NguonHam(FileCombo, "chayComboChoTabDangMo");
        var soChoNoi = System.Text.RegularExpressions.Regex.Matches(chay, @"loi: dat\.loi \|\|").Count;
        Assert.Equal(2, soChoNoi);
    }
}
