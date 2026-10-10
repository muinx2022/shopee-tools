using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.App.Views;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// PHA 2 "KM của Shop" — form + lưu cấu hình theo tài khoản. Pha này CHƯA có luồng chạy (không mở trình duyệt),
/// nên mọi thứ dưới đây test được không cần Brave.
/// </summary>
public class KmFormTests
{
    // ══════════ 1. Sinh tên mặc định ══════════

    /// <summary>
    /// Tên mặc định bám ĐÚNG khuôn mới: <c>KM ngày dd-MM-yyyy từ HH:mm</c> — mang ngày BẮT ĐẦU và MỘT giờ
    /// (26/08/2026). Vế <c>- HH:mm</c> cũ đã biến mất cùng với ô "Kết thúc".
    /// </summary>
    [Fact]
    public void SinhTenMacDinh_DungKhuon()
    {
        var ten = KmDialogViewModel.SinhTenMacDinh(new DateTime(2026, 8, 18), "09:30");
        Assert.Equal("KM ngày 18-08-2026 từ 09:30", ten);
        Assert.DoesNotContain(" - ", ten);
    }

    /// <summary>Đổi ngày hoặc đổi giờ ⇒ tên đổi theo (đây là lý do nó phải là hàm thuần, không phải hằng chuỗi).</summary>
    [Fact]
    public void SinhTenMacDinh_DoiNgayHoacGio_ThiTenDoi()
    {
        var goc = KmDialogViewModel.SinhTenMacDinh(new DateTime(2026, 8, 18), "09:30");

        Assert.NotEqual(goc, KmDialogViewModel.SinhTenMacDinh(new DateTime(2026, 8, 19), "09:30"));
        Assert.NotEqual(goc, KmDialogViewModel.SinhTenMacDinh(new DateTime(2026, 8, 18), "08:00"));
    }

    /// <summary>Trần 150 ký tự — Shopee chặn cứng ở đó, gửi dài hơn là mất phần đuôi âm thầm.</summary>
    [Fact]
    public void CatTen_TranDung150()
    {
        Assert.Equal(150, KmDialogViewModel.CatTen(new string('a', 300)).Length);
        Assert.Equal("ngắn", KmDialogViewModel.CatTen("ngắn"));
        Assert.Equal(string.Empty, KmDialogViewModel.CatTen(null));
    }

    // ══════════ 2. Tên tự sinh lại — trừ khi người dùng đã sửa tay ══════════

    private static KmDialogViewModel VmForm(long accountId = 1, int soShop = 12)
        => new(accountId, "shop@mail.com", soShop, KmConfigRepository.MacDinh(accountId));

    /// <summary>
    /// <b>Form mở ra mang sẵn ngày HÔM NAY</b> (đính chính 26/08/2026 — ô "Ngày" nay là ngày MỞ ĐẦU khoảng,
    /// không còn là "ngày duy nhất ở +180"), và tên tự sinh mang đúng ngày đó.
    /// <para>Đây là một trong hai đường quyết khoảng chương trình; đường kia là lượt tự động
    /// (<c>KmNhipTuDongTests</c>). Cả hai phải đi qua cùng <see cref="KmLichTuDong.NgayBatDau"/> /
    /// <see cref="KmLichTuDong.NgayKetThuc"/>: lệch nhau nghĩa là bấm tay và chạy tự động tạo hai chương trình
    /// khác khoảng trên cùng một shop.</para>
    /// </summary>
    [StaFact]
    public void VmForm_NgayMacDinh_LaHomNay_VaGoiMangDuHaiMoc()
    {
        var vm = VmForm();

        Assert.Equal(KmLichTuDong.NgayBatDau(DateTime.Now), vm.Ngay);
        Assert.Equal(DateTime.Today, vm.Ngay);
        Assert.Equal($"KM ngày {vm.Ngay:dd-MM-yyyy} từ 09:30", vm.TenChuongTrinh);

        // Gói của lượt bấm tay: hai mốc cách nhau ĐÚNG 180 ngày, một giờ dùng chung.
        var thamSo = vm.DungThamSoKm();
        Assert.Equal(DateTime.Today, thamSo.NgayBatDau);
        Assert.Equal(DateTime.Today.AddDays(180), thamSo.NgayKetThuc);
        Assert.Equal(vm.GioBatDau, thamSo.Gio);
    }

    /// <summary>Chưa sửa tay ⇒ đổi ngày/giờ thì tên SINH LẠI.</summary>
    [StaFact]
    public void DoiNgayGio_ChuaSuaTay_ThiTenSinhLai()
    {
        var vm = VmForm();
        vm.Ngay = new DateTime(2026, 8, 18);
        Assert.Equal("KM ngày 18-08-2026 từ 09:30", vm.TenChuongTrinh);

        vm.Ngay = new DateTime(2026, 12, 25);
        Assert.Equal("KM ngày 25-12-2026 từ 09:30", vm.TenChuongTrinh);

        vm.GioBatDau = "08:00";
        Assert.Equal("KM ngày 25-12-2026 từ 08:00", vm.TenChuongTrinh);
    }

    /// <summary>
    /// Người dùng TỰ SỬA tên ⇒ app THÔI ghi đè, kể cả khi sau đó họ đổi ngày/giờ.
    /// <para>Sửa tay là ý định rõ ràng; ghi đè lên nó là app cãi lại người dùng và làm mất công họ vừa gõ.</para>
    /// </summary>
    [StaFact]
    public void DaSuaTenTay_ThiDoiNgayGio_KhongGhiDe()
    {
        var vm = VmForm();
        vm.TenChuongTrinh = "Tên tôi tự đặt";

        vm.Ngay = new DateTime(2026, 12, 25);
        vm.GioBatDau = "08:00";
        vm.GioKetThuc = "23:00";

        Assert.Equal("Tên tôi tự đặt", vm.TenChuongTrinh);
    }

    // ══════════ 3. Kiểm tra đầu vào ══════════

    /// <summary>
    /// Ma trận đủ ca. Hợp lệ ⇒ null; sai ⇒ CÂU LỖI (không ném, không đóng form).
    /// <para><b>Vế "giờ kết thúc phải SAU giờ bắt đầu" đã BỎ (26/08/2026)</b> — hai đầu khoảng dùng CHUNG một
    /// giờ nên luật ấy vô nghĩa, và ô nhập cũng biến mất khỏi form.</para>
    /// </summary>
    [Fact]
    public void KiemTraForm_MaTran()
    {
        // Hợp lệ.
        Assert.Null(KmDialogViewModel.KiemTraForm("KM tết", "09:30", 5, false, 0));
        Assert.Null(KmDialogViewModel.KiemTraForm("KM tết", "09:30", 5, true, 7));

        // Tên rỗng / chỉ khoảng trắng.
        Assert.Contains("không được để trống", KmDialogViewModel.KiemTraForm("", "09:30", 5, false, 0));
        Assert.Contains("không được để trống", KmDialogViewModel.KiemTraForm("   ", "09:30", 5, false, 0));

        // Tên quá 150.
        Assert.Contains("150", KmDialogViewModel.KiemTraForm(new string('a', 151), "09:30", 5, false, 0));

        // Giờ sai định dạng.
        Assert.Contains("HH:mm", KmDialogViewModel.KiemTraForm("KM", "9h30", 5, false, 0));
        Assert.Contains("HH:mm", KmDialogViewModel.KiemTraForm("KM", "muộn", 5, false, 0));
        Assert.Contains("HH:mm", KmDialogViewModel.KiemTraForm("KM", null, 5, false, 0));

        // % ngoài 1–99.
        Assert.Contains("1 đến 99", KmDialogViewModel.KiemTraForm("KM", "09:30", 0, false, 0));
        Assert.Contains("1 đến 99", KmDialogViewModel.KiemTraForm("KM", "09:30", 100, false, 0));

        // Số ngày < 1 CHỈ là lỗi khi đã bật tự động.
        Assert.Contains("≥ 1", KmDialogViewModel.KiemTraForm("KM", "09:30", 5, true, 0));
        Assert.Null(KmDialogViewModel.KiemTraForm("KM", "09:30", 5, false, 0));
    }

    /// <summary>
    /// <b>Mốc <c>Ngày + Giờ bắt đầu</c> phải ở TƯƠNG LAI <i>và cách bây giờ ít nhất một BIÊN AN</i> — chỉ chặn
    /// đường bấm CHẠY.</b>
    /// <para>Picker của Shopee vô hiệu hoá ngày quá khứ và phần giờ đã trôi qua, nên mốc quá khứ chắc chắn làm
    /// bước 4 của extension đỏ — sau khi đã mở Brave, qua SSO và mở shop. Chặn trên form thì người dùng biết lý
    /// do trong một giây.</para>
    /// <para><b>Vế BIÊN AN thêm 26/08/2026</b> (phản biện, lỗi NẶNG): "lớn hơn bây giờ một phút" KHÔNG đủ — mốc
    /// bắt đầu là hạn chót đồng hồ mà cửa này chỉ chạy MỘT LẦN, còn vòng thì mất hàng chục phút. Ở đây đo với
    /// <c>soShop = 1</c> ⇒ biên an rơi về SÀN 15 phút.</para>
    /// </summary>
    [Fact]
    public void KiemTraMocTuongLai_ChanMocDaTroiQua_VaCaMocQuaSAT()
    {
        var bayGio = new DateTime(2026, 8, 19, 14, 0, 0);

        // Quá khứ: cùng ngày nhưng giờ đã trôi qua · ngày hôm qua · ĐÚNG mốc hiện tại (không còn là tương lai).
        Assert.NotNull(KmDialogViewModel.KiemTraMocTuongLai(new DateTime(2026, 8, 19), "09:30", bayGio, 1));
        Assert.NotNull(KmDialogViewModel.KiemTraMocTuongLai(new DateTime(2026, 8, 18), "23:59", bayGio, 1));
        Assert.NotNull(KmDialogViewModel.KiemTraMocTuongLai(new DateTime(2026, 8, 19), "14:00", bayGio, 1));

        // TƯƠNG LAI NHƯNG QUÁ SÁT — vế MỚI, và là vế đã giết 8 shop cuối của lượt thật 26/08: mốc còn 1 phút
        // nữa thì qua cửa cũ, rồi trôi qua ngay ở shop thứ hai.
        Assert.NotNull(KmDialogViewModel.KiemTraMocTuongLai(new DateTime(2026, 8, 19), "14:01", bayGio, 1));
        Assert.NotNull(KmDialogViewModel.KiemTraMocTuongLai(new DateTime(2026, 8, 19), "14:14", bayGio, 1));

        // ĐỦ biên an (đúng sàn 15 phút) ⇒ qua. Biên là ">=", không phải ">".
        Assert.Null(KmDialogViewModel.KiemTraMocTuongLai(new DateTime(2026, 8, 19), "14:15", bayGio, 1));
        Assert.Null(KmDialogViewModel.KiemTraMocTuongLai(new DateTime(2026, 8, 20), "00:30", bayGio, 1));

        // Lượt tự động 2h sáng đặt chương trình 09:30 CÙNG NGÀY — phải hợp lệ, đây là ca chạy hằng đêm.
        Assert.Null(KmDialogViewModel.KiemTraMocTuongLai(
            new DateTime(2026, 8, 19), "09:30", new DateTime(2026, 8, 19, 2, 5, 0), 12));

        // Giờ sai định dạng: KHÔNG nói lỗi hai lần (KiemTraForm đã có câu riêng).
        Assert.Null(KmDialogViewModel.KiemTraMocTuongLai(new DateTime(2026, 8, 19), "9h30", bayGio, 1));
    }

    /// <summary>
    /// <b>BIÊN AN phải tính theo SỐ SHOP LƯỢT NÀY THỰC SỰ CHẠY, không phải tổng số shop của tài khoản.</b>
    /// <para><b>Ca thật 16:16 ngày 26/08/2026.</b> Bấm "Chạy Combo" trên tài khoản 12 shop trong khi
    /// <see cref="KmLichTuDong.TranShopLuotCombo"/> = 2. Cửa kiểm tính biên theo 12 shop ⇒ đòi 96 phút và
    /// khuyên "từ 18:00 trở đi — đủ cho một vòng 12 shop", trong khi lượt ấy chỉ chạy 2 shop và cần ~16 phút.
    /// Con số THẬT, nhưng của một vòng KHÔNG BAO GIỜ xảy ra ⇒ chặn oan gần một tiếng rưỡi.</para>
    /// <para>Vòng KM và Flash Sale không có trần ⇒ trả nguyên tổng số shop. Vế đối chứng đó là thứ chặn một
    /// bản "áp trần cho mọi vòng" lọt qua.</para>
    /// </summary>
    [Fact]
    public void SoShopSeChay_TonTrongTranCheDoThu_CuaTUNGVONG()
    {
        var tran = KmLichTuDong.TranShopLuotCombo;
        var tranFlash = KmLichTuDong.TranShopLuotFlash;

        // Vòng combo: bị trần cắt. ⚠ Viết theo `tran > 0 ? … : 12` chứ KHÔNG ghim con số 2 — hằng này là công
        // tắc giai đoạn thử, người dùng sẽ đặt về 0 để chạy full list, và một test ghim số sẽ đỏ OAN đúng lúc
        // đó rồi bị ai đó xoá đi. Test phải sống qua cả hai trạng thái của công tắc.
        Assert.Equal(tran > 0 ? Math.Min(12, tran) : 12, KmLichTuDong.SoShopSeChay(12, LoaiChuongTrinh.Combo));
        // Ít shop hơn trần ⇒ giữ nguyên, đừng phồng lên bằng trần.
        Assert.Equal(1, KmLichTuDong.SoShopSeChay(1, LoaiChuongTrinh.Combo));

        // Vòng FLASH SALE: trần RIÊNG, không dùng chung với combo. ⚠ Cùng luật viết như trên — theo
        // `tranFlash > 0 ? … : 12`, KHÔNG ghim con số 1: hằng này cũng là công tắc giai đoạn thử.
        Assert.Equal(tranFlash > 0 ? Math.Min(12, tranFlash) : 12,
            KmLichTuDong.SoShopSeChay(12, LoaiChuongTrinh.FlashSale));
        Assert.Equal(tranFlash, KmLichTuDong.TranShopCua(LoaiChuongTrinh.FlashSale));
        Assert.Equal(tran, KmLichTuDong.TranShopCua(LoaiChuongTrinh.Combo));

        // Vế ĐỐI CHỨNG — vòng KM KHÔNG có trần, và đó là vế phải giữ được kể cả khi hai công tắc kia đổi.
        Assert.Equal(0, KmLichTuDong.TranShopCua(LoaiChuongTrinh.ChuongTrinhShop));
        Assert.Equal(12, KmLichTuDong.SoShopSeChay(12, LoaiChuongTrinh.ChuongTrinhShop));

        // Và hệ quả THẬT của cả chuyện này: khi trần đang BẬT, biên an của lượt combo 12 shop phải là biên của
        // `tran` shop — chứ không phải 96 phút của vòng 12 shop không bao giờ chạy.
        if (tran > 0)
        {
            Assert.Equal(
                KmLichTuDong.BienAnMocBatDau(tran),
                KmLichTuDong.BienAnMocBatDau(KmLichTuDong.SoShopSeChay(12, LoaiChuongTrinh.Combo)));
            Assert.NotEqual(
                KmLichTuDong.BienAnMocBatDau(12),
                KmLichTuDong.BienAnMocBatDau(KmLichTuDong.SoShopSeChay(12, LoaiChuongTrinh.Combo)));
        }
    }

    /// <summary>
    /// <b>MA TRẬN BIÊN AN — biên tính theo SỐ SHOP, kẹp sàn 15 phút.</b>
    /// <para>Đây là con số quyết định một vòng có kịp chạy xong trước mốc bắt đầu hay không. Đo thẳng trên hàm
    /// thuần <see cref="KmLichTuDong.BienAnMocBatDau"/> <b>và</b> trên cửa kiểm thật, vì một bản tính đúng mà
    /// cửa kiểm quên gọi thì cũng vẫn 8 shop đỏ.</para>
    /// </summary>
    [Theory]
    [InlineData(0, 15)]   // không đọc được shop nào ⇒ chỉ còn sàn
    [InlineData(1, 15)]   // 1 × 8 = 8 phút, dưới sàn ⇒ sàn thắng
    [InlineData(2, 16)]   // 2 × 8, vừa vượt sàn
    [InlineData(12, 96)]  // vòng 12 shop thật — ~1 tiếng 36 phút
    public void BienAnMocBatDau_TinhTheoSoShop_VaKepSan15Phut(int soShop, int phutMongDoi)
    {
        Assert.Equal(phutMongDoi, (int)KmLichTuDong.BienAnMocBatDau(soShop).TotalMinutes);

        // ...và cửa kiểm phải dùng ĐÚNG con số đó: mốc đặt ĐÚNG biên thì qua, sớm hơn một phút thì chặn.
        var bayGio = new DateTime(2026, 8, 19, 0, 0, 0);
        var ngay = new DateTime(2026, 8, 19);
        var dungBien = bayGio.AddMinutes(phutMongDoi);
        var somMotPhut = bayGio.AddMinutes(phutMongDoi - 1);

        Assert.Null(KmDialogViewModel.KiemTraMocTuongLai(ngay, dungBien.ToString("HH:mm"), bayGio, soShop));
        Assert.NotNull(KmDialogViewModel.KiemTraMocTuongLai(ngay, somMotPhut.ToString("HH:mm"), bayGio, soShop));
    }

    /// <summary>
    /// <b>GIỜ GỢI Ý không bao giờ được gần hơn BIÊN AN</b> — đây chính là chỗ bản trước tự tay đẩy người dùng
    /// vào ca hỏng: bấm 13:28, app khuyên "ví dụ 14:00" (32 phút), người dùng làm đúng lời khuyên, rồi 8 shop
    /// cuối đỏ.
    /// <para>Ca <c>13:59:30</c> là ca biên của phép làm tròn: <c>bayGio + 15'</c> = <c>14:14:30</c>, làm tròn
    /// LÊN bước 5 phút ⇒ <c>14:15</c>. Một bản làm tròn XUỐNG (hoặc cắt về <c>HH:00</c> như bản đầu) ra
    /// <c>14:00</c> — chỉ cách hiện tại 30 giây.</para>
    /// <para><b>Bước 5 phút, KHÔNG phải nửa giờ</b> (26/08/2026): làm tròn thô đẩy lượt thử 2 shop từ
    /// <c>16:33</c> lên <c>17:00</c> — bắt chờ thêm 27 phút không vì lý do gì.</para>
    /// </summary>
    [Theory]
    [InlineData("2026-08-26 13:28:00", 12, "15:05")]   // 13:28 + 96' = 15:04 → làm tròn lên 15:05
    [InlineData("2026-08-26 13:59:30", 1, "14:15")]    // 13:59:30 + 15' = 14:14:30 → 14:15
    [InlineData("2026-08-26 13:00:00", 1, "13:15")]    // 13:00 + 15' = 13:15 → đã đúng bước, giữ nguyên
    [InlineData("2026-08-26 02:01:00", 12, "03:40")]   // nhịp tự động 12 shop: 02:01 + 96' = 03:37 → 03:40
    [InlineData("2026-08-26 16:17:00", 2, "16:35")]    // LƯỢT THỬ 2 shop: 16:17 + 16' = 16:33 → 16:35
    public void GioGoiY_LuonCachBayGio_ItNhatBienAn(string bayGioText, int soShop, string gioGoiYMongDoi)
    {
        var bayGio = DateTime.ParseExact(
            bayGioText, "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

        // Mốc 00:01 chắc chắn đã quá khứ ⇒ luôn có câu lỗi để soi.
        var loi = KmDialogViewModel.KiemTraMocTuongLai(bayGio.Date, "00:01", bayGio, soShop);
        Assert.NotNull(loi);
        Assert.Contains(gioGoiYMongDoi, loi);

        // Và mệnh đề THẬT SỰ quan trọng: giờ được gợi ý phải TỰ NÓ qua được cửa kiểm.
        Assert.Null(KmDialogViewModel.KiemTraMocTuongLai(bayGio.Date, gioGoiYMongDoi, bayGio, soShop));
    }

    /// <summary>
    /// <b>Gợi ý vắt qua nửa đêm ⇒ KHÔNG gợi một giờ trong ngày</b>: ô Giờ chỉ đặt được giờ TRONG ngày bắt đầu,
    /// nên "00:30" cho một mốc bắt đầu hôm nay là gợi ý đúng một mốc quá khứ khác.
    /// </summary>
    [Fact]
    public void GioGoiY_VatQuaNuaDem_ThiNoiRoLaHomNayHetCho()
    {
        var bayGio = new DateTime(2026, 8, 26, 23, 50, 0);
        var loi = KmDialogViewModel.KiemTraMocTuongLai(bayGio.Date, "11:00", bayGio, 12) ?? "";

        Assert.Contains("KHÔNG còn đủ chỗ", loi);
        Assert.DoesNotContain("từ 00:", loi);
    }

    /// <summary>
    /// <b>Bấm CHẠY với mốc quá khứ ⇒ từ chối, và CÂU LỖI PHẢI DẠY ĐƯỢC CÁCH GỠ.</b>
    /// <para>Đây là ca thường gặp NHẤT từ 26/08/2026 (plan §4): mốc bắt đầu nay là HÔM NAY lúc Giờ, nên bấm tay
    /// lúc 13:28 với giờ 11:00 là chắc chắn bị chặn. Câu chỉ nói "đã ở QUÁ KHỨ" để người dùng đứng trước một
    /// form không có ô nào tên "quá khứ" — nên câu phải nêu ĐÚNG mốc, nêu bây giờ là mấy giờ, và chỉ đích danh
    /// ô phải sửa.</para>
    /// </summary>
    [StaFact]
    public void VmForm_MocQuaKhu_ThiThuChayTuChoi_VaCauLoiChiRoCachGo()
    {
        var vm = VmForm();
        vm.Ngay = new DateTime(2026, 8, 26);
        vm.GioBatDau = "11:00";

        Assert.Null(vm.ThuChay(new DateTime(2026, 8, 26, 13, 28, 0)));
        Assert.Null(vm.TongKet);

        var loi = vm.Loi ?? "";
        Assert.Contains("26-08-2026 11:00", loi);        // mốc bắt đầu, nói ra bằng số
        Assert.Contains("ĐÃ TRÔI QUA", loi);
        Assert.Contains("26-08-2026 13:28", loi);        // và bây giờ là mấy giờ
        Assert.Contains("ô Giờ", loi);                   // chỉ đích danh ô phải sửa
        Assert.Contains("MUỘN HƠN", loi);
        // Gợi ý một giờ CỤ THỂ — và giờ ấy phải TÍNH TỪ BIÊN AN của vòng 12 shop (96 phút), không phải
        // "+1 tiếng rồi cắt về HH:00" như bản đầu. 13:28 + 96' = 15:04 ⇒ làm tròn lên bước 5 phút ⇒ 15:05.
        // ⚠ Bản đầu gợi "14:00" — đúng cái mốc mà làm theo là 8 shop cuối đỏ.
        Assert.Contains("15:05", loi);
        Assert.DoesNotContain("14:00", loi);
        Assert.Contains("22-02-2027", loi);              // và nói khoảng chạy tới đâu (26-08-2026 + 180)

        // Và lời khuyên phải TỰ NÓ dùng được: đặt đúng giờ được gợi ý thì lượt chạy được.
        vm.GioBatDau = "15:05";
        Assert.NotNull(vm.ThuChay(new DateTime(2026, 8, 26, 13, 28, 0)));
    }

    /// <summary>
    /// <b>Đường "Đóng" chỉ được lưu khi cấu hình HỢP LỆ</b> — nhưng KHÔNG bị chặn bởi mốc quá khứ.
    /// <para>Hai vế, hai lý do: cấu hình rác (giờ gõ dở) mà ghi đè bản cũ thì lượt tự động 2h sáng chạy bằng
    /// chính mớ rác ấy; còn chặn theo mốc quá khứ thì người mở form ra sửa % giảm mà lỡ để lại một ngày đã trôi
    /// qua (họ tự chọn trên <c>DatePicker</c>, hoặc mở lại một form đang mở dở từ hôm trước) sẽ không lưu nổi
    /// một thay đổi hoàn toàn hợp lệ.</para>
    /// </summary>
    [StaFact]
    public void VmForm_CauHinhHopLe_ChiTheoDinhDang_KhongTheoMocQuaKhu()
    {
        var vm = VmForm();
        vm.Ngay = new DateTime(2020, 1, 1); // mốc quá khứ chắc chắn
        Assert.True(vm.CauHinhHopLe, "Mốc quá khứ KHÔNG được chặn đường lưu khi bấm Đóng.");

        vm.GioBatDau = "9h3";
        Assert.False(vm.CauHinhHopLe);

        vm.GioBatDau = "09:30";
        vm.PhanTram = 0;
        Assert.False(vm.CauHinhHopLe);

        vm.PhanTram = 15;
        Assert.True(vm.CauHinhHopLe);
    }

    /// <summary>Cấu hình lấy ra từ form phải MANG THEO mốc lượt tự động đã thử — bấm Đóng không được xoá nó.</summary>
    [StaFact]
    public void VmForm_LayCauHinh_GiuNguyen_LanThuTuDong()
    {
        var moc = new DateTime(2026, 8, 19, 2, 5, 0);
        var vm = new KmDialogViewModel(
            1, "shop@mail.com", 12,
            KmConfigRepository.MacDinh(1) with { LanThuTuDong = moc });

        // Mất mốc này là lịch đêm nay thử thêm một lượt nữa — đúng thứ mốc sinh ra để chặn.
        Assert.Equal(moc, vm.LayCauHinh().LanThuTuDong);
    }

    /// <summary>Câu mở đầu phải nói THẲNG là ĐANG BẮT ĐẦU CHẠY, kèm đủ thông tin lượt chạy.
    /// <para>Trước 19/08 câu này nói "chưa chạy (pha 3 chưa có luồng)" — đúng ở pha 2, nhưng từ khi form nối vào
    /// luồng thật thì nó thành LỜI NÓI DỐI: người dùng bấm Chạy, vòng 12 shop bắt đầu, mà nhật ký lại bảo chưa
    /// chạy gì.</para></summary>
    [Fact]
    public void CauBatDauChay_NoiRoLaDANGCHAY_VaDuThongTin()
    {
        var cau = KmDialogViewModel.CauBatDauChay(
            new ThamSoKm("KM ngày 18-08-2026 từ 09:30",
                         new DateTime(2026, 8, 18), new DateTime(2027, 2, 14), "09:30", 5),
            12, "shop@mail.com", tuDong: true, soNgay: 7,
            bayGio: new DateTime(2026, 8, 18, 10, 0, 0));

        Assert.Contains("BẮT ĐẦU CHẠY", cau);
        Assert.DoesNotContain("chưa chạy", cau);
        Assert.Contains("KM ngày 18-08-2026", cau);
        // CẢ HAI mốc của khoảng, CÙNG giờ — thứ người dùng đối chiếu khi mở Shopee lên nhìn.
        Assert.Contains("18-08-2026 09:30 → 14-02-2027 09:30", cau);
        Assert.Contains("giảm 5%", cau);
        Assert.Contains("12 shop", cau);
        Assert.Contains("shop@mail.com", cau);

        // "Lần kế dự kiến" = NGÀY CHẠY + 7, kéo về đầu khung KM — KHÔNG phải ngày kết thúc + 7.
        Assert.Contains("25-08-2026 01:00", cau);
        Assert.DoesNotContain("21-02-2027", cau);
    }

    /// <summary>
    /// <b>"Lần kế dự kiến" tính từ MỐC CHẠY, không phải từ ngày chương trình</b> — và phải khớp
    /// <see cref="KmLichTuDong.MocChayKe"/>, thứ thật sự quyết định lượt tự động nổ lúc nào.
    /// <para>Mốc KẾT THÚC của chương trình đi trước 180 ngày, nên hai gốc tính này lệch nhau đúng 180 ngày.
    /// Lấy nhầm gốc là câu tổng kết hứa một mốc chẳng liên quan gì tới lịch thật — kiểu nói dối chỉ lộ ra khi
    /// người dùng ngồi chờ tới ngày đó mà không thấy gì chạy.</para>
    /// </summary>
    [Fact]
    public void LanChayKe_TinhTuMocChay_VaKhopMocChayKeCuaLich()
    {
        var chay = new DateTime(2026, 8, 18, 3, 20, 0);

        Assert.Equal(new DateTime(2026, 8, 25, 1, 0, 0), KmDialogViewModel.LanChayKe(chay, 7));
        Assert.Equal(new DateTime(2026, 8, 19, 1, 0, 0), KmDialogViewModel.LanChayKe(chay, 1));

        // soNgay rác (0 / âm) vẫn phải ra một mốc TIẾN, y như `MocChayKe` kẹp `Math.Max(1, …)`.
        Assert.Equal(new DateTime(2026, 8, 19, 1, 0, 0), KmDialogViewModel.LanChayKe(chay, 0));

        // Cùng một câu trả lời với lịch thật: chạy xong thì `LanChayCuoi = chay`.
        var cf = new KmConfig(1, TuDong: true, SoNgay: 7, PhanTram: 5,
            GioBatDau: "09:30", GioKetThuc: "10:30", LanChayCuoi: chay);
        Assert.Equal(KmLichTuDong.MocChayKe(cf, chay, LoaiChuongTrinh.ChuongTrinhShop), KmDialogViewModel.LanChayKe(chay, 7));
    }

    /// <summary>Tắt tự động ⇒ không bịa ra "lần kế dự kiến".</summary>
    [Fact]
    public void CauBatDauChay_TatTuDong_ThiNoiTat()
    {
        var cau = KmDialogViewModel.CauBatDauChay(
            new ThamSoKm("KM", new DateTime(2026, 8, 18), new DateTime(2027, 2, 14), "09:30", 5),
            3, "a@b.c", tuDong: false, soNgay: 7,
            bayGio: new DateTime(2026, 8, 18, 10, 0, 0));

        Assert.Contains("tự động: tắt", cau);
        Assert.DoesNotContain("lần kế", cau);
    }

    // ══════════ 4. Repository ══════════

    /// <summary>Chưa có bản ghi ⇒ trả mặc định (09:30–10:30, 5%, 7 ngày, tự động tắt).</summary>
    [Fact]
    public void KmConfig_ChuaCoBanGhi_TraMacDinh()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);

        var cf = services.KmConfig.Get(99);

        Assert.False(cf.TuDong);
        Assert.Equal(3, cf.SoNgay);   // mặc định đổi 7 → 3 ngày (25/08/2026)
        Assert.Equal(5, cf.PhanTram);
        Assert.Equal("09:30", cf.GioBatDau);
        Assert.Equal("10:30", cf.GioKetThuc);
        Assert.Null(cf.LanChayCuoi);
    }

    /// <summary>Lưu rồi nạp lại ra ĐÚNG giá trị, gồm cả mốc chạy cuối.</summary>
    [Fact]
    public void KmConfig_LuuRoiNapLai_DungGiaTri()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        var moc = new DateTime(2026, 8, 18, 9, 30, 0, DateTimeKind.Local);

        services.KmConfig.Save(new KmConfig(7, TuDong: true, SoNgay: 3, PhanTram: 25,
            GioBatDau: "08:00", GioKetThuc: "22:15", LanChayCuoi: moc));

        var cf = services.KmConfig.Get(7);
        Assert.True(cf.TuDong);
        Assert.Equal(3, cf.SoNgay);
        Assert.Equal(25, cf.PhanTram);
        Assert.Equal("08:00", cf.GioBatDau);
        Assert.Equal("22:15", cf.GioKetThuc);
        Assert.Equal(moc, cf.LanChayCuoi);
    }

    /// <summary>Hai tài khoản KHÔNG đè cấu hình của nhau (khoá là <c>account_id</c>).</summary>
    [Fact]
    public void KmConfig_HaiTaiKhoan_KhongDeNhau()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);

        services.KmConfig.Save(new KmConfig(1, true, 3, 10, "08:00", "09:00", null));
        services.KmConfig.Save(new KmConfig(2, false, 9, 40, "20:00", "21:00", null));

        Assert.Equal(10, services.KmConfig.Get(1).PhanTram);
        Assert.Equal(40, services.KmConfig.Get(2).PhanTram);
        Assert.Equal("08:00", services.KmConfig.Get(1).GioBatDau);
        Assert.Equal("20:00", services.KmConfig.Get(2).GioBatDau);
    }

    /// <summary>Lưu lần hai cho CÙNG tài khoản là CẬP NHẬT, không đẻ dòng thứ hai.</summary>
    [Fact]
    public void KmConfig_LuuLanHai_ThiCapNhat()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);

        services.KmConfig.Save(new KmConfig(5, false, 7, 5, "09:30", "10:30", null));
        services.KmConfig.Save(new KmConfig(5, true, 2, 30, "07:00", "08:00", null));

        var cf = services.KmConfig.Get(5);
        Assert.True(cf.TuDong);
        Assert.Equal(30, cf.PhanTram);
    }

    // ══════════ 5. ViewModel form ══════════

    /// <summary>Mở form khi tài khoản chưa có bản ghi ⇒ nạp đúng mặc định lên các ô.</summary>
    [StaFact]
    public void VmForm_ChuaCoCauHinh_NapDungMacDinh()
    {
        var vm = VmForm();

        Assert.Equal("09:30", vm.GioBatDau);
        Assert.Equal("10:30", vm.GioKetThuc);
        Assert.Equal(5, vm.PhanTram);
        Assert.False(vm.TuDong);
        Assert.Equal(3, vm.SoNgay);   // mặc định đổi 7 → 3 ngày (25/08/2026)
        Assert.False(vm.ChoNhapSoNgay);   // ô "sau xx ngày" mờ khi chưa bật tự động
        Assert.Null(vm.Loi);
    }

    /// <summary>Tick "Chạy tự động" ⇒ ô số ngày mới bật.</summary>
    [StaFact]
    public void VmForm_TickTuDong_ThiMoOSoNgay()
    {
        var vm = VmForm();
        Assert.False(vm.ChoNhapSoNgay);

        vm.TuDong = true;
        Assert.True(vm.ChoNhapSoNgay);
    }

    /// <summary>
    /// <b>Ô chu kỳ "sau xx ngày" ở VÙNG CHUNG mở khi BẤT KỲ vòng nào đang bật</b> — kể cả khi chỉ bật riêng
    /// vòng combo (26/08/2026, form chia tab).
    /// <para>Ô đó nằm NGOÀI tab và là chu kỳ dùng chung cho cả ba vòng, nên nếu nó vẫn theo
    /// <c>ChoNhapSoNgay</c> (chỉ nhìn <c>TuDong</c> của Chương Trình Của Shop) thì người bật riêng vòng combo
    /// không còn đường nào chỉnh nhịp của nó — mà ô mờ không nói vì sao nó mờ thì y như không tồn tại.</para>
    /// </summary>
    [StaFact]
    public void VmForm_OChuKyChung_MoKhiBatKyVongNaoBat()
    {
        var vm = VmForm();
        Assert.False(vm.ChoNhapChuKyChung);   // chưa vòng nào bật ⇒ mờ

        vm.ComboBat = true;                   // CHỈ vòng combo — vế mà `ChoNhapSoNgay` bỏ sót
        Assert.True(vm.ChoNhapChuKyChung);
        Assert.False(vm.ChoNhapSoNgay);

        vm.ComboBat = false;
        vm.TuDong = true;                     // CHỈ vòng Chương Trình Của Shop
        Assert.True(vm.ChoNhapChuKyChung);

        vm.TuDong = false;
        Assert.False(vm.ChoNhapChuKyChung);   // tắt hết ⇒ mờ lại
    }

    /// <summary>
    /// Bấm "Chạy chương trình" khi dữ liệu SAI ⇒ trả null, gán <c>Loi</c>, KHÔNG dựng câu tổng kết.
    /// <para>Nơi gọi dựa vào <c>null</c> để KHÔNG ghi nhật ký — không có chốt này thì nhật ký có một dòng
    /// "đã chạy" cho một lượt chưa hề hợp lệ.</para>
    /// </summary>
    [StaFact]
    public void VmForm_DuLieuSai_ThuChayTraNull_VaKhongCoTongKet()
    {
        var vm = VmForm();
        // Cảnh dựng ĐỔI 26/08/2026: vế "giờ kết thúc ≤ giờ bắt đầu" đã BỎ cùng với ô nhập, nên ca hỏng mượn
        // sang vế ĐỊNH DẠNG GIỜ — vẫn còn sống, và vẫn đúng ý định đo của test này (dữ liệu sai ⇒ null).
        vm.GioBatDau = "9h30";

        // `bayGio` truyền tay để test không phụ thuộc đồng hồ.
        Assert.Null(vm.ThuChay(DateTime.Today));
        Assert.NotNull(vm.Loi);
        Assert.Null(vm.TongKet);
    }

    /// <summary>Dữ liệu đúng ⇒ có câu tổng kết, và lỗi cũ được xoá.</summary>
    [StaFact]
    public void VmForm_DuLieuDung_ThiCoTongKet_VaXoaLoiCu()
    {
        var vm = VmForm();
        vm.GioBatDau = "9h30";   // sai định dạng — cảnh dựng thay cho vế "kết thúc ≤ bắt đầu" đã bỏ
        vm.ThuChay(DateTime.Today);
        Assert.NotNull(vm.Loi);

        vm.GioBatDau = "09:30";
        var tk = vm.ThuChay(DateTime.Today);

        Assert.NotNull(tk);
        Assert.Null(vm.Loi);
        Assert.Contains("BẮT ĐẦU CHẠY", tk!);
    }

    /// <summary>Cấu hình lấy ra từ form khớp những gì đang hiện trên ô.</summary>
    [StaFact]
    public void VmForm_LayCauHinh_KhopVoiO()
    {
        var vm = VmForm(accountId: 42);
        vm.TuDong = true;
        vm.SoNgay = 3;
        vm.PhanTram = 15;
        vm.GioBatDau = "07:00";
        vm.GioKetThuc = "08:30";

        var cf = vm.LayCauHinh();

        Assert.Equal(42, cf.AccountId);
        Assert.True(cf.TuDong);
        Assert.Equal(3, cf.SoNgay);
        Assert.Equal(15, cf.PhanTram);
        Assert.Equal("07:00", cf.GioBatDau);
        Assert.Equal("08:30", cf.GioKetThuc);
    }

    // ══════════ 6. Dialog ══════════

    /// <summary>Các ô trên dialog có <c>x:Name</c>, bind đúng property, và ô tên chặn ở 150 ký tự.</summary>
    [StaFact]
    public void KmDialog_CacOBindDung_VaMaxLength150()
    {
        var dlg = new KmDialog(VmForm());

        Assert.Equal(150, dlg.TenBox.MaxLength);
        Assert.Equal(nameof(KmDialogViewModel.TenChuongTrinh), DuongBind(dlg.TenBox, TextBox.TextProperty));
        Assert.Equal(nameof(KmDialogViewModel.Ngay), DuongBind(dlg.NgayPicker, DatePicker.SelectedDateProperty));
        Assert.Equal(nameof(KmDialogViewModel.GioBatDau), DuongBind(dlg.GioBatDauBox, TextBox.TextProperty));
        Assert.Equal(nameof(KmDialogViewModel.PhanTram), DuongBind(dlg.PhanTramBox, TextBox.TextProperty));
        Assert.Equal(nameof(KmDialogViewModel.TuDong), DuongBind(dlg.TuDongCheck, CheckBox.IsCheckedProperty));
        Assert.Equal(nameof(KmDialogViewModel.SoNgay), DuongBind(dlg.SoNgayBox, TextBox.TextProperty));

        // Ô chu kỳ nằm ở VÙNG CHUNG (ngoài tab) từ 26/08/2026 ⇒ cửa mở/khoá của nó phải là
        // `ChoNhapChuKyChung` (bất kỳ vòng nào bật), KHÔNG còn là `ChoNhapSoNgay` (chỉ nhìn vòng đầu). Property
        // đúng mà XAML quên đổi ràng buộc thì người bật riêng vòng combo vẫn gõ không được, và bộ test VM vẫn xanh.
        Assert.Equal(nameof(KmDialogViewModel.ChoNhapChuKyChung),
            DuongBind(dlg.ChuKyRow, UIElement.IsEnabledProperty));

        // Khung lỗi hiện theo Loi — lỗi phải nhìn thấy ngay trong form, không phải MessageBox.
        Assert.Equal(nameof(KmDialogViewModel.Loi), DuongBind(dlg.LoiBox, UIElement.VisibilityProperty));
    }

    private static string? DuongBind(DependencyObject el, DependencyProperty dp)
        => BindingOperations.GetBinding(el, dp)?.Path?.Path;

    // ══════════ 7. HAI Ô THÔNG SỐ FLASH (03/09/2026) — người dùng chốt "không fix trong code" ══════════

    /// <summary>
    /// <b>Ma trận biên của <see cref="KmDialogViewModel.KiemTraFormFlash"/>.</b> Hai con số này đi THẲNG xuống
    /// form Shopee, và extension kiểm lại đúng hai biên ấy (fail-closed) — lệch biên giữa hai bên là một số qua
    /// được form rồi chết ở bước "0. Tham số", hoặc ngược lại.
    /// <para>Câu lỗi % còn phải NÓI RA mức tối thiểu Shopee đang đòi: bản trước là hằng 5 chính vì lý do đó, nên
    /// người gõ 1–4 cần biết trước thay vì nhận một lỗi từ Shopee ở bước cuối.</para>
    /// </summary>
    [Theory]
    [InlineData(0, 10, false)]      // % = 0 ⇒ chương trình vô nghĩa
    [InlineData(1, 10, true)]       // biên dưới
    [InlineData(5, 10, true)]       // mặc định đang chạy thật
    [InlineData(99, 10, true)]      // biên trên
    [InlineData(100, 10, false)]    // vượt dải ô của Shopee
    [InlineData(-1, 10, false)]
    [InlineData(5, 0, false)]       // SL = 0 ⇒ không bán được gì
    [InlineData(5, 1, true)]
    [InlineData(5, 9999, true)]
    [InlineData(5, 10000, false)]
    public void KiemTraFormFlash_MaTranBien(int phanTram, int soLuong, bool hopLe)
    {
        var loi = KmDialogViewModel.KiemTraFormFlash(phanTram, soLuong);
        Assert.Equal(hopLe, loi is null);
        if (!hopLe)
        {
            Assert.StartsWith(KmDialogViewModel.DauCauLoiFlash, loi);
        }
    }

    /// <summary>
    /// Câu lỗi % phải DẠY được người đọc mức tối thiểu Shopee đòi — không chỉ nói "sai".
    /// <para><b>Biên là [1, 99], KHÔNG phải [5, 99]</b>: 5 là mức Shopee đòi ở thời điểm này, không phải một
    /// luật của app. Chặn cứng ở 5 là lần Shopee hạ mức thì người dùng không đặt nổi con số hợp lệ mà phải chờ
    /// một bản phát hành — đúng cái đợt này đi gỡ. Nên 2 vẫn HỢP LỆ, chỉ có câu lỗi mới nhắc tới 5.</para>
    /// </summary>
    [Fact]
    public void KiemTraFormFlash_CauLoiPhanTram_NoiMucToiThieuCuaShopee()
    {
        Assert.Null(KmDialogViewModel.KiemTraFormFlash(2, 10));   // dưới mức Shopee đòi nhưng KHÔNG bị app chặn
        Assert.Contains("tối thiểu 5%", KmDialogViewModel.KiemTraFormFlash(0, 10));
    }

    /// <summary>
    /// <b>Hai ô flash: NẠP từ cấu hình, MANG vào bản lưu, và ĐỔI thì PHẢI LƯU.</b>
    ///
    /// <para>Vế cuối là vế đắt nhất (cùng ca hỏng mà công tắc <c>FlashBat</c> đã phải vá): người dùng gõ 5% → 8%
    /// rồi bấm Đóng, mà <c>BanLuuSauForm</c> kết luận "không sửa gì" ⇒ trả <c>null</c> ⇒ KHÔNG lưu ⇒ cú gõ biến
    /// mất, không một dấu vết, và đêm sau gói flash vẫn gửi 5%.</para>
    /// </summary>
    [StaFact]
    public void VmForm_HaiOFlash_NapDung_VaDoiThiPhaiLuu()
    {
        var banNap = KmConfigRepository.MacDinh(1) with { FlashPhanTramGiam = 6, FlashSoLuongThamGia = 12 };
        var vm = new KmDialogViewModel(1, "shop@mail.com", 12, banNap);

        // NẠP: form hiện đúng số đã lưu, không phải mặc định 5/10.
        Assert.Equal(6, vm.FlashPhanTramGiam);
        Assert.Equal(12, vm.FlashSoLuongThamGia);
        Assert.Equal(6, vm.LayCauHinh().FlashPhanTramGiam);
        Assert.Equal(12, vm.LayCauHinh().FlashSoLuongThamGia);

        // KHÔNG đổi gì ⇒ không lưu (hành vi cũ phải giữ nguyên).
        var luc = new DateTime(2026, 9, 3, 23, 0, 0);
        Assert.Null(KmDialogViewModel.BanLuuSauForm(banNap, banNap, vm.LayCauHinh(), "may-1", luc));

        // ĐỔI % ⇒ CÓ lưu, và bản lưu mang đúng số mới.
        vm.FlashPhanTramGiam = 8;
        var luu = KmDialogViewModel.BanLuuSauForm(banNap, banNap, vm.LayCauHinh(), "may-1", luc);
        Assert.NotNull(luu);
        Assert.Equal(8, luu!.FlashPhanTramGiam);
        Assert.Equal(12, luu.FlashSoLuongThamGia);

        // ĐỔI SL cũng vậy (đo riêng: một bản vá chỉ nối một trong hai trường vẫn qua vế trên).
        vm.FlashPhanTramGiam = 6;
        vm.FlashSoLuongThamGia = 25;
        var luu2 = KmDialogViewModel.BanLuuSauForm(banNap, banNap, vm.LayCauHinh(), "may-1", luc);
        Assert.NotNull(luu2);
        Assert.Equal(25, luu2!.FlashSoLuongThamGia);
    }

    /// <summary>
    /// <b>Hai ô flash RÁC ⇒ KHÔNG lưu gì cả, và câu lỗi hiện NGAY trong form.</b>
    ///
    /// <para><b>Cửa <c>CauHinhHopLe</c> chặn VÔ ĐIỀU KIỆN, không gác sau <c>FlashBat</c></b> — khác cụm combo, và
    /// cố ý: gác sau công tắc thì người dùng gõ 150% trong lúc vòng flash đang TẮT sẽ được LƯU, rồi tháng sau họ
    /// bật vòng lên và gói đầu gửi 150% xuống Shopee. Fail-open ở đúng chỗ tệ nhất.</para>
    ///
    /// <para>Vế <c>Loi</c> là phần bù bắt buộc: đường "Đóng" không có chỗ nào hiện lỗi được (form đã đóng), nên
    /// nếu không nói ra lúc GÕ thì người dùng chỉ thấy "cấu hình tự nhiên không lưu".</para>
    /// </summary>
    [StaFact]
    public void VmForm_OFlashRac_ThiKhongLuu_VaHienLoiNgay()
    {
        var vm = VmForm();
        Assert.True(vm.CauHinhHopLe);
        Assert.Null(vm.Loi);

        // Vòng flash đang TẮT mà vẫn phải chặn.
        Assert.False(vm.FlashBat);
        vm.FlashPhanTramGiam = 150;
        Assert.False(vm.CauHinhHopLe);
        Assert.NotNull(vm.Loi);
        Assert.StartsWith(KmDialogViewModel.DauCauLoiFlash, vm.Loi);

        // Sửa lại hợp lệ ⇒ câu lỗi CỦA CHÍNH hai ô này được dọn, cửa lưu mở lại.
        vm.FlashPhanTramGiam = 8;
        Assert.True(vm.CauHinhHopLe);
        Assert.Null(vm.Loi);

        // …nhưng KHÔNG được nuốt câu lỗi của tab khác: đặt một lỗi của vòng KM rồi gõ ô flash hợp lệ.
        vm.PhanTram = 0;
        vm.Loi = KmDialogViewModel.KiemTraForm(vm.TenChuongTrinh, vm.GioBatDau, vm.PhanTram, vm.TuDong, vm.SoNgay);
        Assert.NotNull(vm.Loi);
        vm.FlashSoLuongThamGia = 20;
        Assert.NotNull(vm.Loi);
    }

    /// <summary>Hai ô flash trên dialog có <c>x:Name</c> và bind đúng property — thiếu bind là người dùng gõ vào
    /// hư không, mà bộ test VM ở trên vẫn xanh.</summary>
    [StaFact]
    public void KmDialog_HaiOFlash_BindDung()
    {
        var dlg = new KmDialog(VmForm());

        Assert.Equal(nameof(KmDialogViewModel.FlashPhanTramGiam),
            DuongBind(dlg.FlashPhanTramGiamBox, TextBox.TextProperty));
        Assert.Equal(nameof(KmDialogViewModel.FlashSoLuongThamGia),
            DuongBind(dlg.FlashSoLuongThamGiaBox, TextBox.TextProperty));

        // `UpdateSourceTrigger=PropertyChanged` là phần bắt buộc: mặc định của TextBox là LostFocus, và lúc đó
        // câu lỗi chỉ hiện sau khi người dùng rời ô — hoặc không bao giờ, nếu họ bấm Đóng ngay.
        Assert.Equal(UpdateSourceTrigger.PropertyChanged,
            BindingOperations.GetBinding(dlg.FlashPhanTramGiamBox, TextBox.TextProperty)!.UpdateSourceTrigger);
        Assert.Equal(UpdateSourceTrigger.PropertyChanged,
            BindingOperations.GetBinding(dlg.FlashSoLuongThamGiaBox, TextBox.TextProperty)!.UpdateSourceTrigger);
    }
}
