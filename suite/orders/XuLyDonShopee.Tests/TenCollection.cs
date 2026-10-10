namespace XuLyDonShopee.Tests;

/// <summary>
/// Tên các xunit <b>collection</b> dùng để ép một nhóm lớp test chạy NỐI ĐUÔI thay vì song song.
/// <para><b>Vì sao cần:</b> xunit chạy các lớp test song song theo mặc định. Lớp nào SỬA trạng thái static của
/// mã sản phẩm (hạn chờ, nhịp hỏi, cờ toàn cục) mà chạy cùng lúc với lớp khác cũng sửa đúng biến đó thì đỏ
/// NGẪU NHIÊN — hỏng kiểu tốn nhất để lần ra, vì chạy lại một mình thì luôn xanh.</para>
/// <para>Đặt tên ở MỘT chỗ để hai lớp không gõ lệch nhau một ký tự rồi tưởng đã nối đuôi (xunit coi hai chuỗi
/// khác nhau là hai collection khác nhau, và không báo gì cả).</para>
/// </summary>
internal static class TenCollection
{
    /// <summary>Các lớp rút <c>AccountsViewModel.HanChoKhungXuyenMay</c> / <c>NhipHoiKhungXuyenMay</c> xuống
    /// để khỏi ngồi chờ 3 phút thật.</summary>
    public const string KhungXuyenMay = "KhungXuyenMay";

    /// <summary>
    /// Các lớp ĐẶT biến môi trường <c>SHOPEE_DEV_TOOLS</c> (<see cref="AccountsViewModel.BienMoiTruongDev"/>)
    /// rồi dựng <c>AccountsViewModel</c> để đo <c>Visibility</c> THẬT của nút chẩn đoán.
    /// <para><b>Vì sao phải nối đuôi:</b> biến môi trường là trạng thái của CẢ TIẾN TRÌNH, và
    /// <c>HienNutDev</c> đọc nó ĐÚNG MỘT LẦN lúc dựng VM. Hai lớp cùng đặt/khôi phục biến đó song song thì
    /// lớp A dựng VM đúng lúc lớp B vừa ghi đè giá trị ⇒ đỏ NGẪU NHIÊN.</para>
    /// <para><b>Ca thật 27/08/2026:</b> chỉ có <c>NutChanDoanAnTests</c> đụng biến này nên không ai thấy vấn
    /// đề; thêm <c>FlashSaleDoDomTests</c> (nút "Dò DOM Flash") là có lớp THỨ HAI, và cả hai
    /// <c>Visibility_ThatCua*</c> cùng đỏ trong lượt chạy đầy đủ — trong khi chạy riêng từng lớp thì luôn
    /// xanh. Đúng kiểu hỏng mà xmldoc của lớp này cảnh báo.</para>
    /// </summary>
    public const string BienMoiTruongDev = "BienMoiTruongDev";
}
