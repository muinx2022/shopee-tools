using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>LỆNH NGƯỜI DÙNG BẤM TRÊN HUB → <c>AccountsViewModel.XuLyLenhTuHub</c>, và LUẬT ƯU TIÊN FLASH</b>
/// (người dùng chốt 03/09/2026: <i>"ưu tiên flash sale, nếu flash sale đang chạy, không chạy các flow khác"</i>).
///
/// <para><b>Vì sao lớp này tồn tại:</b> trước 03/09/2026 luồng poll heartbeat gọi THẲNG <c>Sessions.Start</c>,
/// tức đi VÒNG qua mọi cửa từ chối của app — từ hub mở được phiên check đơn ĐÈ LÊN một gói Flash Sale đang
/// chạy (cùng hồ sơ Brave, cùng cổng cầu nối 47821; đo thật ngày 03/09/2026: lượt dọn hồ sơ đóng 10 cửa sổ
/// Brave của gói, shop đang dở HỎNG). Luật mới nằm trọn trong VM, nên nó phải được đo ở đây.</para>
///
/// <para><b>Bốn mệnh đề, hỏng cái nào cũng im lặng:</b>
/// <list type="number">
/// <item>gói flash đang chạy ⇒ <c>run-km</c>/<c>run-combo</c>/<c>run-flash</c> đều 'failed' và KHÔNG mở gì;</item>
/// <item>lát cắt KHÁC đang chạy ⇒ cũng từ chối cả ba (kể cả <c>run-flash</c>): cắt ngang giữa shop để lại
/// chương trình rác trên Shopee — "ưu tiên" nghĩa là không ai chạy ĐÈ lên flash, không phải flash được giết
/// flow khác;</item>
/// <item><c>run</c> (check đơn) thì NGƯỢC LẠI: KHÔNG 'failed', KHÔNG đè — xếp một lượt chạy lại rồi ack 'done'
/// kèm ghi chú, và chỉ mở phiên khi cái đang giữ cầu nối xong;</item>
/// <item><c>stop-all</c> dừng MỌI hoạt động của ĐÚNG MỘT tài khoản (người dùng chốt 04/09/2026) — huỷ cả lượt
/// hoãn đang treo của nó, KHÔNG đụng tài khoản khác trên cùng máy.</item>
/// </list></para>
///
/// <para><b>⚠ KHÔNG bài nào ở đây được để một lượt chạy THẬT khởi động</b> — nó phóng Brave. Mọi ca đo đều là
/// ca TỪ CHỐI hoặc ca đi qua chỗ nối (<c>ChayLaiPhienCheckDon</c>, phiên giả không mở trình duyệt).</para>
/// </summary>
public class LenhTuHubUuTienFlashTests
{
    /// <summary>10:00 ngày 01/09/2026 — trong ngày, xa hạn flash 22:15.</summary>
    private static readonly DateTime BayGio = new(2026, 9, 1, 10, 0, 0);

    // ══════════════════ 1. Gói flash đang chạy ⇒ ba lệnh vòng đều lùi ══════════════════

    [Theory]
    [InlineData(HanhDongLenhTuHub.ChayKm)]
    [InlineData(HanhDongLenhTuHub.ChayCombo)]
    [InlineData(HanhDongLenhTuHub.ChayFlash)]
    public void GoiFlashDangChay_ThiMoiLenhVongDeuTuChoi_VaKhongMoGi(string action)
    {
        using var ban = Ban.Dung();
        ban.DangChayFlash();

        var (status, loi) = ban.Vm.XuLyLenhTuHub(action, ban.Id, Ban.Email);

        Assert.Equal(KetQuaLenhTuHub.Failed, status);
        Assert.Equal(AccountsViewModel.CauTuChoiViUuTienFlash, loi);
        Assert.Contains("ưu tiên flash", loi);
        // KHÔNG mở gì: không phiên nào được dựng, không lượt hoãn nào bị xếp.
        Assert.Null(ban.Services.Sessions.Get(ban.Id));
        Assert.False(ban.Vm.DangHoanChayLai(ban.Id));
        Assert.Contains("TỪ CHỐI", ban.NhatKy());
    }

    /// <summary>Lát cắt KHÁC (vòng KM đang chạy) ⇒ cả ba lệnh vòng lùi bằng câu "đang mở list shop / chạy thử",
    /// KỂ CẢ <c>run-flash</c>: flash không được cắt ngang một vòng đang dở giữa shop.</summary>
    [Theory]
    [InlineData(HanhDongLenhTuHub.ChayKm)]
    [InlineData(HanhDongLenhTuHub.ChayCombo)]
    [InlineData(HanhDongLenhTuHub.ChayFlash)]
    public void LatCatKhacDangChay_ThiMoiLenhVongDeuTuChoi_KeCaFlash(string action)
    {
        using var ban = Ban.Dung();
        ban.DangChayLatCatKhac();

        var (status, loi) = ban.Vm.XuLyLenhTuHub(action, ban.Id, Ban.Email);

        Assert.Equal(KetQuaLenhTuHub.Failed, status);
        Assert.Equal(AccountsViewModel.CauTuChoiViLatCatDangChay, loi);
        Assert.Null(ban.Services.Sessions.Get(ban.Id));
    }

    // ══════════════════ 2. `run` khi đang có gói/lát cắt giữ cầu nối ══════════════════

    /// <summary>Đúng luật ƯU TIÊN FLASH: gói flash đang chạy ⇒ lệnh ▶ Chạy từ hub bị TỪ CHỐI
    /// (failed + câu từ chối ưu tiên flash). Không chen check đơn vào giữa gói, không nhường, không hoãn.</summary>
    [Fact]
    public void Chay_KhiGoiFlashDangChay_BiTuChoiViUuTienFlash()
    {
        using var ban = Ban.Dung();
        ban.DangChayFlash();

        var (status, loi) = ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.Chay, ban.Id, Ban.Email);

        Assert.Equal(KetQuaLenhTuHub.Failed, status);
        Assert.Equal(AccountsViewModel.CauTuChoiViUuTienFlash, loi);
        Assert.False(ban.Vm.NhuongChoCheckDon);
        Assert.False(ban.Vm.DangHoanChayLai(ban.Id));
        Assert.Empty(ban.DaChayLai);                       // chưa Sessions.Start / chưa chạy lại
        Assert.Null(ban.Services.Sessions.Get(ban.Id));
        Assert.Contains("từ chối", ban.NhatKy());
    }

    /// <summary>Cái đang giữ cầu nối là một vòng KM chứ không phải gói flash ⇒ ghi chú ack phải nói ĐÚNG TÊN
    /// nó. Nhật ký/ack khai một gói flash không tồn tại thì người đọc thôi tin nó.</summary>
    [Fact]
    public void Chay_KhiLatCatKhacDangGiu_GhiChuNoiDungTenLatCat()
    {
        using var ban = Ban.Dung();
        ban.DangChayLatCatKhac();

        var (status, loi) = ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.Chay, ban.Id, Ban.Email);

        Assert.Equal(KetQuaLenhTuHub.Done, status);
        Assert.Contains("KM của Shop", loi);
        Assert.DoesNotContain("Flash Sale", loi);
        Assert.True(ban.Vm.DangHoanChayLai(ban.Id));
    }

    /// <summary>Lát cắt khác xong (cửa "đang chạy" hạ) ⇒ lượt vừa xếp THẬT SỰ mở lại check đơn, và đúng tài khoản đó.</summary>
    [Fact]
    public async Task Chay_KhiLatCatKhacXong_ThiChayLaiDungId()
    {
        using var ban = Ban.Dung();
        ban.DangChayLatCatKhac();

        ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.Chay, ban.Id, Ban.Email);
        Assert.Empty(ban.DaChayLai);

        ban.HetLatCat();          // lát cắt xong
        ban.ChoNhuongXong();      // nhịp chờ kế tiếp trả về
        await ban.Vm.LuotHoanGanNhat!;

        Assert.Equal(new[] { ban.Id }, ban.DaChayLai);
        Assert.False(ban.Vm.DangHoanChayLai(ban.Id));      // chủ lượt đã rút ở lối ra thật
        Assert.Null(ban.Vm.MocHoanChayLaiDangCho);
    }

    /// <summary>
    /// Lệnh <c>run</c> giữa lát cắt khác KHÔNG bị <c>TraHoSo</c> nuốt. Mốc SỚM HƠN thắng:
    /// mốc chờ vẫn là BÂY GIỜ (không nhảy lên cuối khe), và khi lát cắt xong check đơn lên NGAY.
    /// </summary>
    [Fact]
    public async Task Chay_GiuaLatCatKhac_KhongBiTraHoSoDoiSangMocCuoiKhe()
    {
        using var ban = Ban.Dung();
        // Lượt hoãn (dù mốc quá khứ ở đường test) không được ngủ thật — trả về ngay để đo được luật.
        ban.Vm.ChoTruocKhiChayLaiCheckDon = (_, __) => Task.CompletedTask;
        ban.DangChayLatCatKhac();

        // (1) Bấm ▶ Chạy giữa lát cắt khác ⇒ lượt hub mốc-BÂY-GIỜ, đang nhường.
        ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.Chay, ban.Id, Ban.Email);
        Assert.Equal(BayGio, ban.Vm.MocHoanChayLaiDangCho);   // mốc hub = bây giờ
        Assert.Empty(ban.DaChayLai);

        // (2) Lát cắt xong + trả hồ sơ THẬT qua TraHoSo với mốc CUỐI KHE (muộn hơn).
        ban.Vm.DatMocChayLaiSauFlashChoTest(BayGio.AddMinutes(40));
        ban.HetLatCat();                    // không còn giữ cầu nối
        ban.Vm.TraHoSoChoTest(ban.Id);

        // ĐÂY LÀ CHỖ PHÂN BIỆT: mốc chờ VẪN là BÂY GIỜ (giữ lượt hub), KHÔNG nhảy lên cuối khe (BayGio+40).
        Assert.Equal(BayGio, ban.Vm.MocHoanChayLaiDangCho);

        // (3) Lượt hub tỉnh (nhường xong) ⇒ mở check đơn NGAY, không trễ tới cuối khe.
        ban.ChoNhuongXong();
        await ban.Vm.LuotHoanGanNhat!;
        Assert.Equal(new[] { ban.Id }, ban.DaChayLai);
        Assert.False(ban.Vm.DangHoanChayLai(ban.Id));
        Assert.Null(ban.Vm.MocHoanChayLaiDangCho);
    }

    /// <summary>Đường sạch ⇒ mở phiên NGAY như cũ (đo qua phiên GIẢ — không Brave).</summary>
    [Fact]
    public void Chay_KhiDuongSach_MoPhienNgay()
    {
        using var ban = Ban.Dung();

        var (status, loi) = ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.Chay, ban.Id, Ban.Email);

        Assert.Equal(KetQuaLenhTuHub.Done, status);
        Assert.Null(loi);
        Assert.NotNull(ban.Services.Sessions.Get(ban.Id));
        Assert.False(ban.Vm.DangHoanChayLai(ban.Id));      // đường sạch thì KHÔNG xếp lượt hoãn nào
    }

    /// <summary>Phiên đã chạy ⇒ 'done' + "đã chạy sẵn", KHÔNG xếp lượt hoãn (lệnh lặp do mạng không được đẻ
    /// thêm việc).</summary>
    [Fact]
    public void Chay_KhiPhienDangChay_TraDaChaySan()
    {
        using var ban = Ban.Dung();
        ban.Services.Sessions.Start(ban.Id);

        var (status, loi) = ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.Chay, ban.Id, Ban.Email);

        Assert.Equal(KetQuaLenhTuHub.Done, status);
        Assert.Equal("đã chạy sẵn", loi);
        Assert.False(ban.Vm.DangHoanChayLai(ban.Id));
    }

    // ══════════════════ 3. stop-all ══════════════════

    /// <summary>"✖ Dừng" từ hub phải HUỶ lượt hoãn đang treo của tài khoản đó — không huỷ thì app tự mở Brave sau
    /// khi người dùng đã bảo dừng (đúng ca NẶNG mà nút ribbon "Dừng tất cả" đã phải sửa 29/08/2026).</summary>
    [Fact]
    public void DungTatCa_HuyLuotHoanDangTreo()
    {
        using var ban = Ban.Dung();
        // Dựng đúng trạng thái "vừa có gói flash xong, đang hoãn chạy lại tới cuối khe nghỉ".
        ban.Vm.DatMocChayLaiSauFlashChoTest(BayGio.AddMinutes(40));
        ban.Vm.TraHoSoChoTest(ban.Id);
        Assert.True(ban.Vm.DangHoanChayLai(ban.Id));

        var (status, _) = ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.DungTatCa, ban.Id, Ban.Email);

        Assert.Equal(KetQuaLenhTuHub.Done, status);
        Assert.False(ban.Vm.DangHoanChayLai(ban.Id));
        Assert.Null(ban.Vm.MocHoanChayLaiDangCho);
        // (Không assert `DaChayLai` rỗng: lượt hoãn ngủ 40' thật nên trong ~1ms của bài này nó KHÔNG THỂ có phần
        //  tử dù đường huỷ có bị gỡ — mệnh đề rỗng, phản biện 04/09/2026.)
        Assert.Contains("ĐÃ HUỶ lượt hoãn chạy lại check đơn", ban.NhatKy());
        Assert.Contains("Lịch tự động KHÔNG tắt", ban.NhatKy());
    }

    /// <summary>
    /// <b>PER-TÀI-KHOẢN (NẶNG-1, người dùng chốt 04/09/2026).</b> "✖ Dừng" trên dòng tài khoản A KHÔNG được đụng
    /// tới tài khoản B trên cùng máy: phiên check đơn của B vẫn chạy, lượt hoãn của B vẫn treo. Bản trước gọi
    /// <c>StopAllAsync</c> quét sạch mọi phiên + huỷ mọi lượt hoãn của cả máy.
    /// </summary>
    [Fact]
    public void DungMotTaiKhoan_KhongDungPhienVaLuotHoanCuaTaiKhoanKhac()
    {
        using var ban = Ban.Dung();
        var idB = ban.ThemTaiKhoan("b@mail.com");

        // B đang chạy check đơn.
        ban.Services.Sessions.Start(idB);
        // Mỗi tài khoản một lượt hoãn đang treo (mốc cuối khe).
        ban.Vm.DatMocChayLaiSauFlashChoTest(BayGio.AddMinutes(40));
        ban.Vm.TraHoSoChoTest(ban.Id);
        ban.Vm.DatMocChayLaiSauFlashChoTest(BayGio.AddMinutes(40));
        ban.Vm.TraHoSoChoTest(idB);
        Assert.True(ban.Vm.DangHoanChayLai(ban.Id));
        Assert.True(ban.Vm.DangHoanChayLai(idB));

        // Dừng ĐÚNG A.
        var (status, _) = ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.DungTatCa, ban.Id, Ban.Email);

        Assert.Equal(KetQuaLenhTuHub.Done, status);
        // A: lượt hoãn bị huỷ.
        Assert.False(ban.Vm.DangHoanChayLai(ban.Id));
        // B: KHÔNG bị đụng — phiên còn chạy, lượt hoãn còn treo, mốc chờ của B vẫn còn.
        Assert.True(ban.Services.Sessions.IsRunning(idB));
        Assert.True(ban.Vm.DangHoanChayLai(idB));
        Assert.NotNull(ban.Vm.MocHoanChayLaiDangCho);
    }

    /// <summary>
    /// <b>Chỉ cắt lát cắt cầu nối khi nó thuộc ĐÚNG tài khoản bị Dừng.</b> Máy chạy lát cắt của tài khoản KHÁC
    /// thì cú "✖ Dừng" của tài khoản này KHÔNG được <c>TryKillPoc</c> (cắt ngang là hỏng shop đang dở của tài
    /// khoản khác). Đo trên hàm thuần vì <c>_bridgeRunning</c> thật + <c>TryKillPoc</c> gắn với lát cắt phóng Brave.
    /// </summary>
    [Theory]
    // bridgeRunning, tài khoản giữ lát cắt, tài khoản bị Dừng, có được cắt không
    [InlineData(true, 7L, 7L, true)]    // đúng lát cắt của mình ⇒ cắt
    [InlineData(true, 9L, 7L, false)]   // lát cắt của tài khoản khác ⇒ KHÔNG cắt
    [InlineData(false, 7L, 7L, false)]  // không có lát cắt nào ⇒ không có gì để cắt
    [InlineData(true, null, 7L, false)] // cờ bật nhưng chưa gán chủ ⇒ không cắt
    public void LatCatDangChayThuocTaiKhoan_ChiCatKhiDungChu(
        bool bridge, long? chuLatCat, long idDung, bool moDoi)
        => Assert.Equal(moDoi,
            AccountsViewModel.LatCatDangChayThuocTaiKhoan(bridge, chuLatCat, idDung));

    // ══════════════════ 4. Cửa cấu hình ══════════════════

    /// <summary>Cấu hình flash rác ⇒ 'failed' mang ĐÚNG câu lỗi của <c>KiemTraFormFlash</c>, và KHÔNG phóng
    /// trình duyệt: chạy để 12 shop cùng đỏ vì một con số sai là lãng phí một đêm.</summary>
    [Fact]
    public void ChayFlash_CauHinhRac_TraCauLoiCuaFormFlash()
    {
        using var ban = Ban.Dung();
        var cu = ban.Services.KmConfig.Get(ban.Id);
        ban.Services.KmConfig.Save(cu with { FlashPhanTramGiam = 0 });

        var (status, loi) = ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.ChayFlash, ban.Id, Ban.Email);

        Assert.Equal(KetQuaLenhTuHub.Failed, status);
        Assert.Equal(KmDialogViewModel.KiemTraFormFlash(0, cu.FlashSoLuongThamGia), loi);
        Assert.Contains("1 đến 99", loi);
        Assert.Contains("cấu hình của tài khoản này không dùng được", ban.NhatKy());
    }

    /// <summary>Cấu hình KM rác ⇒ 'failed' mang câu lỗi của <c>KiemTraForm</c>. Cùng bộ luật thuần với lịch tự
    /// động và với form — ba bộ song song thì bộ ít chạy nhất sẽ mục dần.</summary>
    [Fact]
    public void ChayKm_CauHinhRac_TraCauLoiCuaForm()
    {
        using var ban = Ban.Dung();
        var cu = ban.Services.KmConfig.Get(ban.Id);
        ban.Services.KmConfig.Save(cu with { PhanTram = 0 });

        var (status, loi) = ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.ChayKm, ban.Id, Ban.Email);

        Assert.Equal(KetQuaLenhTuHub.Failed, status);
        Assert.Contains("1 đến 99", loi);
    }

    /// <summary>Hub mới + client cũ là ca có thật sau mỗi lượt deploy lệch nhau: lệnh lạ phải 'failed' KÈM TÊN
    /// LỆNH, không im lặng nuốt.</summary>
    [Fact]
    public void LenhLa_TraFailedKemTenLenh()
    {
        using var ban = Ban.Dung();

        var (status, loi) = ban.Vm.XuLyLenhTuHub("run-teleport", ban.Id, Ban.Email);

        Assert.Equal(KetQuaLenhTuHub.Failed, status);
        Assert.Contains("run-teleport", loi);
    }

    // ══════════════════ 5. Nhãn — hai bên phải gọi cùng một tên ══════════════════

    [Theory]
    [InlineData(HanhDongLenhTuHub.Chay, "▶ Chạy")]
    // "✖ Dừng" (không phải "Dừng tất cả"): stop-all nay dừng ĐÚNG một tài khoản (người dùng chốt 04/09/2026).
    [InlineData(HanhDongLenhTuHub.DungTatCa, "✖ Dừng")]
    [InlineData(HanhDongLenhTuHub.ChayKm, "▶ KM")]
    [InlineData(HanhDongLenhTuHub.ChayCombo, "▶ Combo")]
    [InlineData(HanhDongLenhTuHub.ChayFlash, "▶ Flash")]
    public void NhanLenhTuHub_KhopNhanHienTrenHub(string action, string nhan)
        => Assert.Equal(nhan, AccountsViewModel.NhanLenhTuHub(action));

    // ══════════════════ 7. run-km / run-combo / run-flash: HẠN CHÓT (NẶNG-2) ══════════════════

    /// <summary>
    /// <b>HẠN CHÓT của lệnh chạy vòng từ hub.</b> KM/Combo phải là <c>null</c> (đúng đường BẤM TAY — người dùng
    /// tự quyết khi nào dừng); chỉ Flash mới có hạn 22:15. Bản trước mượn hạn TỰ ĐỘNG (giờ đóng khung 04:00/08:00)
    /// cho cả ba ⇒ bấm ▶ KM lúc 10 giờ sáng là hạn ở quá khứ ⇒ 0 shop chạy được, im lặng (NẶNG, 04/09/2026).
    /// </summary>
    [Fact]
    public void HanChotLenhVong_KmVaCombo_LaNull_ChiFlashCoHan()
    {
        Assert.Null(AccountsViewModel.HanChotLenhVongTuHub(LoaiChuongTrinh.ChuongTrinhShop, BayGio));
        Assert.Null(AccountsViewModel.HanChotLenhVongTuHub(LoaiChuongTrinh.Combo, BayGio));

        var hanFlash = AccountsViewModel.HanChotLenhVongTuHub(LoaiChuongTrinh.FlashSale, BayGio);
        Assert.NotNull(hanFlash);
        // 22:15 CÙNG NGÀY (Shopee đóng đăng ký D+1 quanh ~22:50) — khớp ChayFlashSaleTayAsync.
        Assert.Equal(BayGio.Date.AddHours(22).AddMinutes(15), hanFlash!.Value);
    }

    // ══════════════════ 6. Thứ tự nhịp tự động: FLASH → KM → COMBO ══════════════════

    /// <summary>
    /// <b>NEO NGUỒN.</b> Ba nhịp gọi nối đuôi trong cùng một tick và mỗi nhịp lùi khi một vòng khác đang chạy.
    /// Thứ tự ưu tiên là KM → COMBO → FLASH (người dùng chốt 08/09/2026: KM và Combo chạy 3 ngày 1 lần nên
    /// không ưu tiên Flash Sale trước KM/Combo).
    /// </summary>
    [Fact]
    public void NhipTuDong_GoiKmTruocComboTruocFlash()
    {
        var nguon = CatVung(
            NguonFile("orders", "XuLyDonShopee.App", "ViewModels", "AccountsViewModel.KetQua.cs"),
            "internal async Task ChayNhipLichKmAsync", "catch (System.OperationCanceledException)");

        var iFlash = nguon.IndexOf("await NhipLichFlashAsync(", StringComparison.Ordinal);
        var iKm = nguon.IndexOf("await NhipLichKmAsync(", StringComparison.Ordinal);
        var iCombo = nguon.IndexOf("await NhipLichComboAsync(", StringComparison.Ordinal);

        Assert.True(iFlash >= 0 && iKm >= 0 && iCombo >= 0, "thiếu một trong ba lượt gọi nhịp");
        Assert.True(iKm < iCombo, "nhịp KM phải gọi TRƯỚC nhịp COMBO");
        Assert.True(iCombo < iFlash, "nhịp COMBO phải gọi TRƯỚC nhịp FLASH (KM & Combo ưu tiên trước flash — 08/09/2026)");
    }

    /// <summary>
    /// <b>Combo đọc ĐỒNG HỒ TƯƠI sau khi nhịp KM chạy.</b> Nhịp KM có thể <c>await</c> một lượt chạy mất 40-50 phút;
    /// nhịp Combo sau đó phải đọc lại đồng hồ tươi qua <c>DongHoNhipLich</c>.
    /// </summary>
    [Fact]
    public async Task NhipTuDong_ComboDocDongHoTuoiSauKm()
    {
        using var ban = Ban.Dung();
        var mocTuoi = BayGio.AddHours(3);
        ban.Vm.DongHoNhipLich = () => mocTuoi;
        var daNhan = new Dictionary<string, DateTime>();
        ban.Vm.MocNhipDaNhanChoTest = (nhip, moc) => daNhan[nhip] = moc;

        // Cấu hình mặc định (vòng tắt) ⇒ không nhịp con nào phóng trình duyệt; ta chỉ đo mốc hai nhịp NHẬN.
        await ban.Vm.ChayNhipLichKmAsync(BayGio);

        // KM chạy đầu nhận BayGio; Combo chạy sau nhận mốc tươi đọc từ DongHoNhipLich.
        Assert.Equal(BayGio, daNhan["KM"]);
        Assert.Equal(mocTuoi, daNhan["Combo"]);
    }

    // ══════════════════ 5. per-tài-khoản qua lượt mượn hồ sơ (NẶNG-A / VỪA-C / VỪA-D, 04/09/2026) ══════════════════

    /// <summary>
    /// <b>NẶNG-A — "✖ Dừng" tài khoản A GIỮA lát cắt của A: B (bị tạm dừng cùng lượt mượn hồ sơ) vẫn phải được
    /// chạy lại khi trả hồ sơ.</b> Bản trước bật một cờ "người dùng dừng tay" cho CẢ MÁY nên <c>TraHoSo</c> bỏ
    /// qua cả B — check đơn của B chết câm dù tooltip vừa hứa "Máy vẫn chạy tài khoản khác".
    /// </summary>
    [Fact]
    public void DungA_GiuaLatCatCuaA_ThiBVanDuocChayLaiKhiTraHoSo()
    {
        using var ban = Ban.Dung();
        var idB = ban.ThemTaiKhoan("b@mail.com");
        ban.Vm.DatTenLatCatChoTest(AccountsViewModel.TenViecFlash, ban.Id, giuCauNoi: true);

        var (status, _) = ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.DungTatCa, ban.Id, Ban.Email);
        Assert.Equal(KetQuaLenhTuHub.Done, status);

        // Lát cắt về `finally` ⇒ trả hồ sơ cho đúng tập đã tạm dừng {A, B}.
        ban.Vm.DatTenLatCatChoTest(null);
        ban.Vm.TraHoSoChoTest(ban.Id, idB);

        Assert.True(ban.Services.Sessions.IsRunning(idB), "B phải được chạy lại — cú Dừng là của riêng A");
        Assert.False(ban.Services.Sessions.IsRunning(ban.Id), "A vừa bị bảo dừng thì không được tự mở lại");
    }

    /// <summary>
    /// <b>NẶNG-A, chiều ngược — "✖ Dừng" tài khoản B trong lúc lát cắt của A đang chạy: B KHÔNG được tự mở lại
    /// khi A trả hồ sơ.</b> Bản trước chỉ bật cờ theo CHỦ lát cắt (A) nên cú Dừng B là no-op, và <c>TraHoSo</c>
    /// mở lại B sau khi người dùng đã bảo dừng — đúng lớp lỗi nút "Dừng tất cả" đã phải sửa 29/08/2026.
    /// </summary>
    [Fact]
    public void DungB_GiuaLatCatCuaA_ThiBKhongTuMoLaiKhiTraHoSo()
    {
        using var ban = Ban.Dung();
        var idB = ban.ThemTaiKhoan("b@mail.com");
        ban.Vm.DatTenLatCatChoTest(AccountsViewModel.TenViecFlash, ban.Id, giuCauNoi: true);

        var (status, _) = ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.DungTatCa, idB, "b@mail.com");
        Assert.Equal(KetQuaLenhTuHub.Done, status);

        ban.Vm.DatTenLatCatChoTest(null);
        ban.Vm.TraHoSoChoTest(ban.Id, idB);

        Assert.True(ban.Services.Sessions.IsRunning(ban.Id), "A không bị ai bảo dừng thì phải chạy lại");
        Assert.False(ban.Services.Sessions.IsRunning(idB), "B đã bị bảo dừng — tự mở lại là phủ quyết người dùng");
        Assert.Contains("check đơn KHÔNG mở lại: bạn đã bấm Dừng tài khoản này", ban.NhatKyCua("b@mail.com"));
    }

    /// <summary>Người dùng đổi ý: Dừng B rồi ▶ Chạy B (từ hub) trong cùng lượt mượn ⇒ B PHẢI được chạy lại.</summary>
    [Fact]
    public void DungBRoiChayB_TrongCungLuotMuon_ThiBDuocChayLai()
    {
        using var ban = Ban.Dung();
        var idB = ban.ThemTaiKhoan("b@mail.com");
        ban.Vm.DatTenLatCatChoTest(AccountsViewModel.TenViecFlash, ban.Id, giuCauNoi: true);

        ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.DungTatCa, idB, "b@mail.com");
        ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.Chay, idB, "b@mail.com");

        ban.Vm.DatTenLatCatChoTest(null);
        ban.Vm.TraHoSoChoTest(ban.Id, idB);

        Assert.True(ban.Services.Sessions.IsRunning(idB) || ban.Vm.DangHoanChayLai(idB),
            "▶ Chạy sau cú Dừng phải thắng: B chạy lại (ngay hoặc qua lượt hoãn)");
    }

    /// <summary>
    /// <b>VỪA-C — bước "đóng cửa sổ Mở list shop" của "✖ Dừng" A KHÔNG được huỷ token của lát cắt tài khoản
    /// KHÁC đang chạy.</b> Ca: cửa sổ "Mở list shop" của A còn để lại (<c>_pocAccountId = A</c>), lát cắt KM của B
    /// vừa bắt đầu và đang chờ khung xuyên máy (tới 3'). Bản trước <c>TryKillPoc()</c> huỷ <c>_bridgeCts</c> — là
    /// token của B — nên lượt KM của B chết với dòng "Đã hủy KM của Shop".
    /// </summary>
    [Fact]
    public void DungA_CuaSoXemCuaA_NhungLatCatCuaB_ThiKhongHuyTokenCuaB()
    {
        using var ban = Ban.Dung();
        var idB = ban.ThemTaiKhoan("b@mail.com");
        using var ctsB = new CancellationTokenSource();
        ban.Vm.DatTenLatCatChoTest("KM của Shop", idB, giuCauNoi: true);
        ban.Vm.DatCuaSoXemChoTest(ban.Id, ctsB);

        ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.DungTatCa, ban.Id, Ban.Email);

        Assert.False(ctsB.IsCancellationRequested, "token của lát cắt B bị huỷ bởi cú Dừng A");
        Assert.Contains("đang chạy lát cắt của tài khoản khác", ban.NhatKy());
        ban.Vm.DatCuaSoXemChoTest(null, null);
    }

    /// <summary>Đối chứng VỪA-C: không ai chạy lát cắt ⇒ cửa sổ xem của A bị đóng như cũ (token bị huỷ).</summary>
    [Fact]
    public void DungA_CuaSoXemCuaA_KhongAiChayLatCat_ThiDongCuaSo()
    {
        using var ban = Ban.Dung();
        using var cts = new CancellationTokenSource();
        ban.Vm.DatCuaSoXemChoTest(ban.Id, cts);

        ban.Vm.XuLyLenhTuHub(HanhDongLenhTuHub.DungTatCa, ban.Id, Ban.Email);

        Assert.True(cts.IsCancellationRequested, "không có lát cắt của ai khác thì cửa sổ xem của A phải bị đóng");
        ban.Vm.DatCuaSoXemChoTest(null, null);
    }

    /// <summary>
    /// <b>VỪA-D — hết hạn chờ thì THÂN LỆNH không được chạy nữa.</b> Bản trước trả <c>failed</c> nhưng cái đã xếp
    /// hàng lên UI thread vẫn chạy khi UI rảnh: hub ghi "lỗi, bấm lại" trong khi máy đã mở phiên / phóng gói —
    /// ack nói dối, và cú bấm lại nhận câu thứ hai mâu thuẫn.
    /// </summary>
    [Fact]
    public void LenhHub_HetHanCho_ThiThanLenhKhongChayNua()
    {
        using var ban = Ban.Dung();
        var hang = new List<Action>();
        ban.Vm.UiCoQuyen = () => false;             // lệnh đến từ luồng heartbeat, không phải UI thread
        ban.Vm.UiXepHang = a => hang.Add(a);        // UI "bận": xếp hàng nhưng chưa chạy
        ban.Vm.HanChoLenhTuHub = TimeSpan.FromMilliseconds(50);

        var (status, err) = ban.Vm.ThiHanhLenhTuHub(HanhDongLenhTuHub.Chay, ban.Id, Ban.Email);
        Assert.Equal(KetQuaLenhTuHub.Failed, status);
        Assert.Contains("giao diện đang bận", err);

        // UI rảnh lại, chạy phần đã xếp ⇒ thân lệnh phải BỎ QUA, không mở phiên.
        foreach (var a in hang) { a(); }
        Assert.False(ban.Services.Sessions.IsRunning(ban.Id), "đã ack failed mà thân lệnh vẫn chạy ⇒ hub nói dối");
    }

    /// <summary>Đối chứng VỪA-D: UI rảnh trong hạn ⇒ thân lệnh chạy, ack <c>done</c> như cũ.</summary>
    [Fact]
    public void LenhHub_UiRanhTrongHan_ThiChayVaAckDone()
    {
        using var ban = Ban.Dung();
        ban.Vm.UiCoQuyen = () => false;
        ban.Vm.UiXepHang = a => a();                // UI rảnh: chạy ngay khi được xếp
        ban.Vm.HanChoLenhTuHub = TimeSpan.FromSeconds(5);

        var (status, _) = ban.Vm.ThiHanhLenhTuHub(HanhDongLenhTuHub.Chay, ban.Id, Ban.Email);

        Assert.Equal(KetQuaLenhTuHub.Done, status);
        Assert.True(ban.Services.Sessions.IsRunning(ban.Id));
    }

    // ── Đồ nghề ───────────────────────────────────────────────────────────────────────────────────────

    private static string NguonFile(params string[] duong)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "ShopeeSuite.sln"))) { continue; }
            var f = Path.Combine(new[] { dir.FullName }.Concat(duong).ToArray());
            if (File.Exists(f)) { return File.ReadAllText(f); }
        }

        throw new FileNotFoundException("Không tìm thấy " + string.Join("/", duong));
    }

    private static string CatVung(string nguon, string tu, string den)
    {
        var i = nguon.IndexOf(tu, StringComparison.Ordinal);
        Assert.True(i >= 0, $"không còn đoạn bắt đầu bằng '{tu}'");
        var j = nguon.IndexOf(den, i, StringComparison.Ordinal);
        Assert.True(j > i, $"không còn mốc kết thúc '{den}' sau '{tu}'");
        return nguon[i..j];
    }

    /// <summary>Một bàn thử: DB tạm + AppServices dùng PHIÊN GIẢ (không Brave) + VM + một tài khoản có cấu
    /// hình KM/Combo/Flash HỢP LỆ (để ca từ chối không lẫn với ca cấu hình rác).</summary>
    private sealed class Ban : IDisposable
    {
        public const string Email = "hub@mail.com";

        private readonly Dictionary<long, PhienGia> _phien = new();

        /// <summary>Cổng để bài test tự quyết khi nào một nhịp "chờ nhường" kết thúc.</summary>
        private readonly TaskCompletionSource<bool> _nhipXong = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private bool _dangChay;

        public TempDatabase Temp { get; private init; } = null!;
        public AppServices Services { get; private set; } = null!;
        public AccountsViewModel Vm { get; private set; } = null!;
        public long Id { get; private set; }

        /// <summary>Các tài khoản ĐÃ được chạy lại check đơn (thay cho <c>Sessions.Start</c> của lượt hoãn).</summary>
        public List<long> DaChayLai { get; } = new();

        public static Ban Dung()
        {
            var temp = new TempDatabase();
            var ban = new Ban { Temp = temp };
            // Factory phải vào từ CTOR (AccountsViewModel đăng ký Sessions.Changed ngay trong ctor của nó).
            var services = new AppServices(temp.Path, id => ban.LayPhien(id));
            ban.Services = services;
            ban.Vm = new AccountsViewModel(services);

            services.Accounts.Insert(new Account { Email = Email, Password = "p" });
            ban.Id = services.Accounts.GetAll().First(a => a.Email == Email).Id;
            // CỐ Ý giữ nguyên cấu hình mặc định: không bài nào ở đây đi tới nhánh CHẠY THẬT (nhánh đó phóng
            // Brave), nên không bài nào phụ thuộc cửa `KiemTraMocTuongLai` — thứ vốn so với đồng hồ THẬT và sẽ
            // biến cả lớp thành bom hẹn giờ nếu ai đó dựa vào nó.

            ban.Vm.ChayLaiPhienCheckDon = x => ban.DaChayLai.Add(x);
            ban.Vm.DongHoHoanChayLai = () => BayGio;
            ban.Vm.DangChayFlashHoacCauNoi = () => ban._dangChay;
            // Nhịp chờ nhường do bài test lái; vẫn NGHE token để cú Dừng cắt được (y production `Task.Delay`).
            ban.Vm.ChoNhipNhuongFlash = ct => Task.WhenAny(ban._nhipXong.Task, Task.Delay(Timeout.Infinite, ct));
            return ban;
        }

        /// <summary>Thêm một tài khoản NỮA (cùng máy) — để đo lệnh per-tài-khoản không đụng tài khoản khác.</summary>
        public long ThemTaiKhoan(string email)
        {
            Services.Accounts.Insert(new Account { Email = email, Password = "p" });
            return Services.Accounts.GetAll().First(a => a.Email == email).Id;
        }

        /// <summary>Dựng trạng thái "một GÓI FLASH đang giữ cầu nối".</summary>
        public void DangChayFlash()
        {
            _dangChay = true;
            Vm.DatTenLatCatChoTest(AccountsViewModel.TenViecFlash);
        }

        /// <summary>Dựng trạng thái "một lát cắt KHÁC (vòng KM) đang giữ cầu nối".</summary>
        public void DangChayLatCatKhac()
        {
            _dangChay = true;
            Vm.DatTenLatCatChoTest("KM của Shop");
        }

        /// <summary>Lượt đang giữ cầu nối đã xong.</summary>
        public void HetLatCat()
        {
            _dangChay = false;
            Vm.DatTenLatCatChoTest(null);
        }

        /// <summary>Cho nhịp chờ hiện tại trả về (vòng nhường kiểm lại cửa "còn ai đang chạy không").</summary>
        public void ChoNhuongXong() => _nhipXong.TrySetResult(true);

        /// <summary>Toàn bộ nhật ký của tài khoản thử — nơi duy nhất người vận hành đọc được lý do.</summary>
        public string NhatKy()
            => string.Join("\n", Services.Log.Snapshot(Email).Select(e => e.Display));

        /// <summary>Nhật ký của một tài khoản khác trên cùng bàn thử.</summary>
        public string NhatKyCua(string email)
            => string.Join("\n", Services.Log.Snapshot(email).Select(e => e.Display));

        private PhienGia LayPhien(long id)
        {
            if (!_phien.TryGetValue(id, out var p)) { _phien[id] = p = new PhienGia(id); }
            return p;
        }

        public void Dispose()
        {
            try { Vm.HuyLuotHoanChayLaiCheckDon("dọn bàn thử"); } catch { /* bỏ qua */ }
            try { Vm.Dispose(); } catch { /* bỏ qua */ }
            Temp.Dispose();
        }
    }

    /// <summary>Phiên giả: KHÔNG mở trình duyệt.</summary>
    private sealed class PhienGia : IAccountSession
    {
        public PhienGia(long id) => AccountId = id;

        public long AccountId { get; }
        public SessionState State { get; private set; } = SessionState.Stopped;
        public string? StatusText => null;
        public int? ToShipCount => null;
        public bool ReadyForActions => true;
        public bool IsShopLoopRunning => false;
        public DateTime? ThoiDiemVongKe { get; set; }
        public string? LastError => null;
        public Process? BraveProcess => null;

        public event Action? Changed;

        public Task StartAsync()
        {
            State = SessionState.Running;
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public void MarkQueued()
        {
            if (State is SessionState.Opening or SessionState.Running or SessionState.Stopping) { return; }
            State = SessionState.Queued;
            Changed?.Invoke();
        }

        public Task StopAsync()
        {
            State = SessionState.Stopped;
            ThoiDiemVongKe = null;
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public Task<bool> RedownloadSlipAsync(string orderSn) => Task.FromResult(false);
    }
}
