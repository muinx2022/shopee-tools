using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>GÓI FLASH CHIẾM KHE NGHỈ GIỮA HAI VÒNG CHECK ĐƠN</b> (người dùng chốt 01/09/2026, plan
/// <c>2026-09-01-flash-ca-ngay-xen-khe-nghi.md</c> §4).
///
/// <para><b>Thay đổi lớn nhất của đợt, và cũng là thứ dễ hỏng câm nhất:</b> trước đây lượt flash CẮT NGANG vòng
/// check đơn đang chạy (nhờ hub dừng mọi máy). Nay nó CHỜ: chỉ mở gói khi MỌI phiên đang hoạt động đều đang
/// nghỉ, và chỉ khi khe nghỉ còn đủ dài. Hỏng ở đây thì hoặc là flash không bao giờ chạy, hoặc là nó giẫm lên
/// một vòng check đơn đang mở dở một shop — cả hai đều không có dòng log nào tự nói ra.</para>
///
/// <para><b>⚠ KHÔNG bài test nào ở đây được để lượt chạy thật phóng trình duyệt.</b> Không có hub trong test nên
/// lượt dừng ngay ở cửa khoá xuyên máy; bằng chứng "nhịp đã vào tới thân" là <c>FlashLanThuTuDong</c> vừa được
/// ghi — đúng khuôn <c>FlashLichTuDongTests</c>.</para>
/// </summary>
public class FlashGoiKheNghiTests
{
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 1. HÀM THUẦN — KHE ĐỦ DÀI, VÀ HẠN CỦA GÓI
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Khe <b>&lt; 75 phút</b> ⇒ KHÔNG mở gói. Đây là thứ tự loại kỳ nghỉ NGẮN 15 phút sau một vòng check đơn bị
    /// cắt: kỳ nghỉ ấy sinh ra để hạ tầng dịu lại, không phải chỗ chen việc.
    /// <para><b>Sàn nâng 60 → 75 ngày 01/09/2026</b> (phản biện vòng 1): hạn shop mới của gói là
    /// <c>cuối khe − 45'</c>, nên khe 60 phút chỉ chừa 15 phút RUNWAY cho phóng Brave + SSO — một lượt SSO chậm
    /// là gói tốn trọn một lần mở trình duyệt cho 0 shop. Sàn 75 nâng runway lên 30 phút.</para>
    /// </summary>
    [Theory]
    [InlineData(15, false)]    // nghỉ ngắn sau vòng bị cắt
    [InlineData(60, false)]    // sàn CŨ — phải KHÔNG còn đủ
    [InlineData(74, false)]
    [InlineData(75, true)]     // đúng mốc
    [InlineData(120, true)]    // chu kỳ check đơn mặc định
    public void KheDuChoGoi_ChiKhiTuBayMuoiLamPhutTroLen(int phutKhe, bool mongDoi)
    {
        var bayGio = new DateTime(2026, 9, 1, 10, 0, 0);
        Assert.Equal(mongDoi, KmLichTuDong.KheDuChoGoiFlash(bayGio, bayGio.AddMinutes(phutKhe)));
    }

    /// <summary>
    /// <b>Hạn của gói là MIN của hai vế</b> — và bài này đo CẢ HAI vế thắng, mỗi vế một ca.
    /// <para>Chỉ đo một vế thì một bản vá bỏ quên vế kia vẫn xanh: bỏ vế KHE ⇒ gói tràn qua lúc check đơn tỉnh
    /// dậy, hai luồng tranh một hồ sơ Brave; bỏ vế NGÀY ⇒ gói bắt đầu shop lúc 22:4x, chạy nửa sau vào lúc
    /// Shopee đã đóng đăng ký cho ngày hôm sau.</para>
    /// </summary>
    [Fact]
    public void HanChotGoi_LaMinCuaHaiVe()
    {
        // (a) VẾ KHE thắng — 10:00, khe tới 12:00 ⇒ 12:00 − 45' = 11:15 (sớm hơn hạn ngày 22:15).
        Assert.Equal(new DateTime(2026, 9, 1, 11, 15, 0),
            KmLichTuDong.HanChotGoiFlash(new DateTime(2026, 9, 1, 10, 0, 0), new DateTime(2026, 9, 1, 12, 0, 0)));

        // (b) VẾ NGÀY thắng — 21:30, khe kéo tới 23:30 ⇒ 22:45 theo khe, nhưng hạn ngày 22:15 chặn trước.
        Assert.Equal(new DateTime(2026, 9, 1, 22, 15, 0),
            KmLichTuDong.HanChotGoiFlash(new DateTime(2026, 9, 1, 21, 30, 0), new DateTime(2026, 9, 1, 23, 30, 0)));

        // (c) Khe rất dài (phiên vừa bị hoãn nhường khung KM) — vẫn không quá hạn ngày.
        Assert.Equal(new DateTime(2026, 9, 1, 22, 15, 0),
            KmLichTuDong.HanChotGoiFlash(new DateTime(2026, 9, 1, 20, 0, 0), new DateTime(2026, 9, 2, 4, 0, 0)));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2. NHỊP — CHỈ MỞ GÓI KHI MỌI PHIÊN ĐANG NGHỈ
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Có MỘT phiên đang GIỮA VÒNG (<c>ThoiDiemVongKe == null</c>) ⇒ nhịp về ÊM, không mở gói.</b>
    ///
    /// <para>Đây là mệnh đề thay cho toàn bộ cơ chế "cắt ngang vòng check đơn" của bản cũ. Cắt ngang là vòng
    /// check đơn bắt đầu lại từ shop 1 ở lượt sau ⇒ shop 1–2 bị mở hai lần trong ít phút, đúng mẫu đã làm Shopee
    /// bật <c>verify/traffic</c> (ca thật: 2 lần mở cùng shop trong 17 phút, đêm 18/08/2026).</para>
    ///
    /// <para>Bằng chứng đo được: <c>FlashLanThuTuDong</c> KHÔNG được ghi — nhịp chưa vào tới thân.</para>
    /// </summary>
    [StaFact]
    public async Task CoPhienDangGiuaVong_ThiKhongMoGoi()
    {
        using var ban = Ban.Dung();
        var phien = ban.ChayPhienGia(ban.Id);
        phien.ThoiDiemVongKe = null;   // ĐANG giữa một vòng check đơn

        await ban.Vm.NhipLichFlashAsync(BayGio);

        Assert.Null(ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);
    }

    /// <summary>
    /// Vế ĐỐI CHỨNG bắt buộc: <b>mọi phiên đang NGHỈ và khe đủ dài ⇒ gói MỞ.</b>
    /// <para>Không có vế này thì một bản vá "không bao giờ mở gói" cũng qua được bài trên — và flash sẽ chết câm
    /// đúng như cái nó định chữa.</para>
    /// </summary>
    /// <remarks>Bài này đo LUÔN mệnh đề <b>"gói trả lại phần khe còn thừa"</b>: nhịp đặt
    /// <c>MocChayLaiCheckDonSauFlash = cuối khe</c> trước khi chạy, nên khi lượt kết thúc (ở đây là bị cửa khoá
    /// xuyên máy chặn) <c>TraHoSo</c> phải đi nhánh HOÃN tới đúng mốc đó — chứ không chạy lại check đơn ngay.
    /// Thiếu vế này thì mỗi gói flash làm trôi nhịp vòng check đơn một lần, câm lặng.</remarks>
    [StaFact]
    public async Task MoiPhienDangNghi_VaKheDuDai_ThiMoGoi_VaTraLaiPhanKheConThua()
    {
        using var ban = Ban.Dung();
        var cuoiKhe = BayGio.AddMinutes(120);            // khe 120' — dư chỗ cho một gói
        var phien = ban.ChayPhienGia(ban.Id);
        phien.ThoiDiemVongKe = cuoiKhe;

        // Đông lạnh đồng hồ của nhánh hoãn + chặn lượt chờ thật (production là Task.Delay tới cuối khe).
        var xongGoiLuc = BayGio.AddMinutes(50);
        ban.Vm.DongHoHoanChayLai = () => xongGoiLuc;
        var daCho = new List<TimeSpan>();
        ban.Vm.ChoTruocKhiChayLaiCheckDon = (t, _) => { daCho.Add(t); return Task.CompletedTask; };

        await ban.Vm.NhipLichFlashAsync(BayGio);

        Assert.Equal(BayGio, ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);

        // Check đơn KHÔNG chạy lại ngay: chờ đúng phần khe còn thừa (120' − 50' = 70').
        Assert.Single(daCho);
        Assert.Equal(TimeSpan.FromMinutes(70), daCho[0]);
    }

    /// <summary>
    /// <b>Khe NGẮN (15 phút, kỳ nghỉ sau một vòng bị cắt) ⇒ KHÔNG mở gói</b> — dù phiên đang nghỉ thật.
    /// <para>Kỳ nghỉ ngắn đó sinh ra để hạ tầng dịu lại sau một vòng hỏng; chen một lượt SSO + mở shop vào đúng
    /// lúc ấy là đổ thêm dầu.</para>
    /// </summary>
    [StaFact]
    public async Task KheNgan_ThiKhongMoGoi()
    {
        using var ban = Ban.Dung();
        var phien = ban.ChayPhienGia(ban.Id);
        phien.ThoiDiemVongKe = BayGio.AddMinutes(15);

        await ban.Vm.NhipLichFlashAsync(BayGio);

        Assert.Null(ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);
    }

    /// <summary>
    /// <b>Khe là mốc SỚM NHẤT trong các phiên đang chạy</b> — một phiên nghỉ ngắn thì cả nhịp phải theo nó.
    /// <para>Lấy mốc muộn nhất (hoặc mốc của phiên đầu danh sách) là gói còn đang chạy khi phiên nghỉ-ngắn tỉnh
    /// dậy: hai luồng tranh cùng một hồ sơ Brave và cùng cổng cầu nối 47821.</para>
    /// <para><b>Bài này dựng HAI phiên cùng <c>Running</c> bằng cách gọi thẳng <c>StartAsync</c></b>, tức đi
    /// vòng qua luật một-slot của <see cref="AccountSessionManager"/>. Cố ý: phép <c>Min</c> là lưới PHÒNG THỦ
    /// cho đúng trạng thái ấy, và không dựng được nó thì lưới không có gì để đo.</para>
    /// </summary>
    [StaFact]
    public async Task KheLayMocSomNhatCuaMoiPhien()
    {
        using var ban = Ban.Dung();
        var id2 = ban.ThemTaiKhoan("flash-2@mail.com");

        var p1 = ban.ChayPhienGia(ban.Id);
        p1.ThoiDiemVongKe = BayGio.AddMinutes(180);   // phiên này nghỉ rất dài
        var p2 = ban.ChayPhienGia(id2);
        p2.ThoiDiemVongKe = BayGio.AddMinutes(20);    // nhưng phiên này dậy sau 20 phút

        Assert.Equal(2, ban.Services.Sessions.Active.Count);

        await ban.Vm.NhipLichFlashAsync(BayGio);

        // Khe THẬT chỉ có 20 phút ⇒ dưới 60 ⇒ KHÔNG gói nào được mở, cho tài khoản NÀO.
        Assert.Null(ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);
        Assert.Null(ban.Services.KmConfig.Get(id2).FlashLanThuTuDong);
    }

    /// <summary>
    /// <b>Phiên đang XẾP HÀNG (<c>Queued</c>) KHÔNG được tính vào phép đo khe.</b>
    ///
    /// <para><b>Nếu tính, vòng flash chết câm ở mọi máy có từ 2 tài khoản trở lên.</b> Chỉ MỘT phiên được chiếm
    /// slot cầu nối; tick 3 tài khoản là 1 phiên chạy + 2 phiên nằm hàng đợi cho tới khi phiên kia dừng hẳn —
    /// mà vòng check đơn thì không bao giờ tự dừng. Phiên xếp hàng có <c>ThoiDiemVongKe == null</c> (nó chưa
    /// chạy vòng nào), nên cửa "mọi phiên phải đang nghỉ" sẽ đóng vĩnh viễn.</para>
    ///
    /// <para>Nó cũng không thuộc câu hỏi này: phiên xếp hàng chưa có vòng nào để mà bị cắt ngang.</para>
    /// </summary>
    [StaFact]
    public async Task PhienDangXepHang_KhongTinhVaoPhepDoKhe()
    {
        using var ban = Ban.Dung();
        var id2 = ban.ThemTaiKhoan("flash-2@mail.com");

        var p1 = ban.ChayPhienGia(ban.Id);
        p1.ThoiDiemVongKe = BayGio.AddMinutes(120);   // phiên ĐANG CHẠY: nghỉ, khe rộng

        var p2 = ban.XepHangPhienGia(id2);            // phiên thứ hai chỉ nằm hàng đợi
        Assert.Equal(SessionState.Queued, p2.State);
        Assert.Null(p2.ThoiDiemVongKe);

        await ban.Vm.NhipLichFlashAsync(BayGio);

        Assert.Equal(BayGio, ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);
    }

    /// <summary>
    /// KHÔNG phiên nào đang chạy ⇒ không có khe THẬT để đo; lấy chính chu kỳ check đơn làm khe giả định.
    /// <para>Chu kỳ mặc định là 120 phút nên gói vẫn mở. Vế này giữ cho vòng flash sống được trên máy mà người
    /// dùng chưa bấm Chạy — nếu không, tài khoản nào tắt check đơn là flash chết theo, câm.</para>
    /// </summary>
    [StaFact]
    public async Task KhongPhienNaoChay_ThiLayChuKyCheckDonLamKhe()
    {
        using var ban = Ban.Dung();
        Assert.Empty(ban.Services.Sessions.Active);

        await ban.Vm.NhipLichFlashAsync(BayGio);

        Assert.Equal(BayGio, ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);
    }

    /// <summary>
    /// Vế ĐỐI CHỨNG của bài trên: <b>chu kỳ check đơn ngắn (30 phút) ⇒ khe giả định cũng ngắn ⇒ KHÔNG mở gói.</b>
    /// <para>Không có vế này thì một bản vá bỏ hẳn phép đo khe ở nhánh "không phiên nào chạy" vẫn xanh — và với
    /// chu kỳ 30 phút thì gói sẽ bị vòng check đơn giẫm lên ngay giữa shop đầu.</para>
    /// </summary>
    [StaFact]
    public async Task KhongPhienNaoChay_MaChuKyNgan_ThiKhongMoGoi()
    {
        using var ban = Ban.Dung();
        ban.Services.Settings.SetOrderIntervalMinutes(30);

        await ban.Vm.NhipLichFlashAsync(BayGio);

        Assert.Null(ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 3. TRẦN SỐ GÓI MỖI NGÀY
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Trần <see cref="KmLichTuDong.TranGoiThuMoiNgayFlash"/> gói mỗi ngày — gói kế tiếp KHÔNG được mở.</b>
    ///
    /// <para>Kế thừa vai trò chống "phóng Brave vô hạn" của chốt một-lượt-mỗi-khung cũ. Giãn cách 30 phút một
    /// mình KHÔNG đủ: một ngày có 23 giờ hợp lệ, tức tới 46 gói nếu gói nào cũng hỏng nhanh.</para>
    ///
    /// <para><b>Hai vế, đo tách nhau vì lý do TỐC ĐỘ:</b> một gói THẬT tốn ~17 giây trong test, nên dựng trạng
    /// thái "đã chạm trần" bằng ngần ấy lượt thật là nhiều phút cho một bài. Vế (a) mở một gói thật để chứng
    /// minh bộ đếm CÓ TĂNG; vế (b) đặt thẳng bộ đếm rồi đo cửa trần. Thiếu vế (a) thì một bản vá quên hẳn lượt
    /// <c>++</c> vẫn xanh — và trần sẽ không bao giờ chạm tới.</para>
    /// </summary>
    [StaFact]
    public async Task TranGoiMoiNgay_GoiVuotTranKhongMo()
    {
        using var ban = Ban.Dung();
        var tran = KmLichTuDong.TranGoiThuMoiNgayFlash;

        // (a) Bộ đếm PHẢI tăng sau một gói thật.
        Assert.Equal(0, ban.Vm.SoGoiFlashDaMoTrongNgay(ban.Id));
        await ban.Vm.NhipLichFlashAsync(BayGio);
        Assert.Equal(1, ban.Vm.SoGoiFlashDaMoTrongNgay(ban.Id));

        // (b) Đã chạm trần ⇒ THÔI, dù mọi cửa khác đều mở.
        var quaTran = BayGio.AddMinutes(40);
        ban.Vm.DatSoGoiFlashDaMoChoTest(ban.Id, quaTran, tran);
        Assert.True(KmLichTuDong.DenGioGoiFlash(ban.Services.KmConfig.Get(ban.Id), quaTran));

        await ban.Vm.NhipLichFlashAsync(quaTran);

        // Mốc thử vẫn là của gói (a) — không gói nào mở thêm.
        Assert.Equal(BayGio, ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);
        Assert.Equal(tran, ban.Vm.SoGoiFlashDaMoTrongNgay(ban.Id));
    }

    /// <summary>
    /// <b>Trần gói là của TỪNG TÀI KHOẢN, không phải một sổ chung cho cả máy</b> (sửa 01/09/2026, phản biện
    /// vòng 1).
    ///
    /// <para><b>Ca hỏng mà bài này chặn:</b> sổ chung thì tài khoản ĐẦU danh sách tiêu hết trần rồi tài khoản
    /// sau không còn gói nào — mọi ngày, vì nhịp duyệt theo thứ tự cố định — và không một dòng log nào nói ra.
    /// Nạn nhân luôn là cùng một tài khoản, nên nó mất trắng vòng flash vĩnh viễn.</para>
    /// </summary>
    [StaFact]
    public async Task TranGoiDemRiengTungTaiKhoan_KhongBoDoiTaiKhoanSau()
    {
        using var ban = Ban.Dung();
        var id2 = ban.ThemTaiKhoan("flash-2@mail.com");

        // CHẶN Ở CỬA KHOÁ XUYÊN MÁY, đừng để lượt phóng Brave THẬT cho tài khoản thứ hai: hồ sơ Brave của một
        // tài khoản CHƯA TỪNG chạy phải dựng mới, và lượt ấy treo vô hạn trong môi trường test (không extension
        // nào nối vào cổng 47821). Mốc thử vẫn được ghi TRƯỚC khi chạy, nên bằng chứng "nhịp đã vào tới thân
        // cho tài khoản 2" không mất gì.
        ban.Services.AcquireAccountLease = (_, _) => Task.FromResult(new OrdersLeaseResult(false, "may-kia"));

        // Tài khoản 1 đã tiêu hết trần của NÓ.
        ban.Vm.DatSoGoiFlashDaMoChoTest(ban.Id, BayGio, KmLichTuDong.TranGoiThuMoiNgayFlash);

        await ban.Vm.NhipLichFlashAsync(BayGio);

        // Tài khoản 2 vẫn phải mở được gói của mình.
        Assert.Null(ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);
        Assert.Equal(BayGio, ban.Services.KmConfig.Get(id2).FlashLanThuTuDong);
        Assert.Equal(1, ban.Vm.SoGoiFlashDaMoTrongNgay(id2));
    }

    /// <summary>
    /// <b>SANG NGÀY LỊCH thì đếm lại từ 0</b> — app chạy liên tục qua nửa đêm là chuyện thường, và không có lượt
    /// reset thì trần "N gói mỗi ngày" biến thành "N gói mỗi lần mở app".
    /// </summary>
    [StaFact]
    public async Task SangNgayMoi_ThiDemLaiSoGoi()
    {
        using var ban = Ban.Dung();

        // Hôm qua đã chạm trần.
        var homQua = new DateTime(2026, 9, 1, 20, 0, 0);
        ban.Vm.DatSoGoiFlashDaMoChoTest(ban.Id, homQua, KmLichTuDong.TranGoiThuMoiNgayFlash);

        // NGÀY HÔM SAU: cùng một VM, cùng một tài khoản ⇒ vẫn phải mở được gói.
        var ngayMai = new DateTime(2026, 9, 2, 6, 0, 0);
        await ban.Vm.NhipLichFlashAsync(ngayMai);

        Assert.Equal(ngayMai, ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);
        Assert.Equal(1, ban.Vm.SoGoiFlashDaMoTrongNgay(ban.Id));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 4. "NGÀY KHÔNG CÒN ĐỎ" **KHÔNG** ĐƯỢC ĐÓNG CỬA NGÀY
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Một gói ra kết quả TOÀN KHÔNG-ĐỎ vẫn KHÔNG được chặn gói sau.</b> Đây là vế NGƯỢC của bản đầu đợt
    /// 01/09/2026, và nó đổi vì phản biện vòng 1.
    ///
    /// <para><b>Cơ chế đã GỠ:</b> bản đầu đặt cờ <c>_flashNgayDongCua</c> khi mọi shop của gói đều khai "ngày
    /// đích không còn đỏ", rồi nhịp thôi mở gói tới hết ngày. Cờ ấy dựng trên bằng chứng của MỘT GÓI ~2 shop và
    /// có ít nhất ba đường sai-dương — gói gồm đúng những shop đã kín khung từ gói trước (app vừa khởi động lại
    /// nên mất tiến độ); một shop kín khung cộng một lượt <c>vePicker</c> hỏng cắt gói ở đó; Shopee mở khung
    /// ngày hôm sau muộn. Mỗi lần sai-dương là MẤT TRỌN một ngày flash, trong khi cái nó tiết kiệm chỉ là vài
    /// gói thừa cho một ngày Shopee đã đóng thật — thứ mà giãn cách 30 phút và trần gói/ngày vốn đã lo.</para>
    ///
    /// <para>Bài đo cả hai nửa: (a) mốc ngày KHÔNG bị ghi (lượt chưa trọn) nên cửa hàm thuần còn mở; (b) sau
    /// giãn cách, nhịp VẪN mở gói mới.</para>
    /// </summary>
    [StaFact]
    public async Task GoiToanKhongDo_ThiNhipVanMoGoiSau()
    {
        using var ban = Ban.Dung();

        // Một gói đã kết luận "cả lượt không còn đỏ".
        ban.Vm.ChotLuotFlash(ban.Id, "flash@mail.com", new KetQuaKm(
            [new KetQuaKmMotShop("111", "Shop Một", false, 0, "ngày đích không đỏ", null,
                NgayMaiKhongDo: true, ShopLogin: "shop.mot")],
            false, null, SoShopChuaChay: 2, NgayMaiKhongDo: true), BayGio);

        // (a) Mốc ngày KHÔNG được ghi (lượt chưa trọn) ⇒ cửa hàm thuần còn mở.
        var cf = ban.Services.KmConfig.Get(ban.Id) with { FlashBat = true };
        Assert.Null(cf.FlashLanChayCuoi);
        Assert.True(KmLichTuDong.DenGioGoiFlash(cf, BayGio.AddMinutes(40)));

        // (b) Và nhịp THẬT SỰ mở gói mới sau giãn cách — không cửa in-memory nào chặn nữa.
        var sau = BayGio.AddMinutes(40);
        await ban.Vm.NhipLichFlashAsync(sau);

        Assert.Equal(sau, ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 4b. TỪ CHỐI MỞ GÓI THÌ PHẢI NÓI RA — MỘT DÒNG, CÓ TIẾT CHẾ
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Khe quá ngắn ⇒ ghi MỘT dòng nhật ký có SỐ ĐO, và dòng ấy bị TIẾT CHẾ.</b>
    ///
    /// <para><b>Vì sao bắt buộc</b> (phản biện vòng 1, 01/09/2026): ba cửa mới của nhịp đều về ÊM — không lỗi,
    /// không mốc, không dòng nào. Một máy mà khe nghỉ luôn dưới sàn sẽ KHÔNG BAO GIỜ chạy flash, và triệu chứng
    /// duy nhất người dùng thấy là "bật rồi mà chẳng thấy gì". Dòng này phải mang số đo để họ biết nới cái gì.</para>
    ///
    /// <para>Vế TIẾT CHẾ đo cùng bài: nhịp chạy mỗi 60 giây, nên nhịp thứ hai cách 1 phút KHÔNG được ghi thêm
    /// dòng nào — không có nó là 1440 dòng rác mỗi ngày, và đúng dòng cần đọc thì chìm mất.</para>
    /// </summary>
    [StaFact]
    public async Task KheNgan_ThiGhiMotDongCoSoDo_VaTietChe()
    {
        using var ban = Ban.Dung();
        var phien = ban.ChayPhienGia(ban.Id);
        phien.ThoiDiemVongKe = BayGio.AddMinutes(20);

        await ban.Vm.NhipLichFlashAsync(BayGio);

        var dong = ban.Services.Log.Snapshot("flash@mail.com")
            .Where(e => e.Message.Contains("CHƯA mở được")).ToList();
        Assert.Single(dong);
        Assert.Contains("20 phút", dong[0].Message);            // số đo THẬT của khe
        Assert.Contains(KmLichTuDong.PhutKheToiThieuChoGoi.ToString(), dong[0].Message);

        // Nhịp kế (1 phút sau) KHÔNG được ghi thêm dòng nào.
        await ban.Vm.NhipLichFlashAsync(BayGio.AddMinutes(1));
        Assert.Single(ban.Services.Log.Snapshot("flash@mail.com")
            .Where(e => e.Message.Contains("CHƯA mở được")));
    }

    /// <summary>
    /// Cửa <b>"có phiên đang GIỮA VÒNG"</b> cũng phải nói ra, và phải nêu ĐÍCH DANH phiên nào — đó là thông tin
    /// duy nhất giúp người đọc biết vì sao khe không bao giờ mở ra.
    /// </summary>
    [StaFact]
    public async Task PhienDangGiuaVong_ThiGhiMotDongNeuDichDanh()
    {
        using var ban = Ban.Dung();
        var phien = ban.ChayPhienGia(ban.Id);
        phien.ThoiDiemVongKe = null;

        await ban.Vm.NhipLichFlashAsync(BayGio);

        var dong = ban.Services.Log.Snapshot("flash@mail.com")
            .Where(e => e.Message.Contains("CHƯA mở được")).ToList();
        Assert.Single(dong);
        Assert.Contains("GIỮA một vòng", dong[0].Message);
        Assert.Contains($"#{ban.Id}", dong[0].Message);
    }

    /// <summary>
    /// <b>Còn CỬA SỔ Brave của lượt "Mở list shop" ⇒ KHÔNG mở gói</b> (thêm 01/09/2026, phản biện vòng 1).
    ///
    /// <para>Cửa sổ ấy nghĩa là hồ sơ Brave đang được MƯỢN và chưa trả. Mở gói lúc này là
    /// <c>DongCuaSoCuLai</c> giết mất cửa sổ ngay dưới tay người đang thao tác. Cờ <c>_bridgeRunning</c> ở đầu
    /// nhịp KHÔNG bắt được ca này: lượt mở list shop đã kết thúc từ lâu, chỉ có cửa sổ là còn sống.</para>
    /// </summary>
    [StaFact]
    public async Task ConCuaSoXemDangMo_ThiKhongMoGoi()
    {
        using var cuaSo = new CuaSoGia();
        using var ban = Ban.Dung();

        // Một lát cắt ĐỂ LẠI cửa sổ — đúng hình dạng của lượt "Mở list shop". `CuaSoXemDangMo` đọc
        // `Process.HasExited` nên không giả bằng gì khác được (khuôn của `MoListShopTests`).
        await ban.Vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", (phien, _, _) =>
        {
            phien.Process = cuaSo.TienTrinh;
            return Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong"));
        }, accountIdChiDinh: ban.Id);
        Assert.True(ban.Vm.CuaSoXemDangMo, "Tiền đề của test hỏng: phải còn một cửa sổ để lại.");

        await ban.Vm.NhipLichFlashAsync(BayGio);

        Assert.Null(ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);
        Assert.Contains(ban.Services.Log.Snapshot("flash@mail.com"),
            e => e.Message.Contains("Mở list shop") && e.Message.Contains("CHƯA mở được"));
    }

    /// <summary>Một tiến trình rẻ tiền THẬT đóng vai "cửa sổ Brave để lại" — cùng khuôn với
    /// <c>MoListShopTests.CuaSoGia</c>: <c>CuaSoXemDangMo</c> đọc <c>Process.HasExited</c> nên không giả bằng gì
    /// khác được. Tự kill trong <c>Dispose</c>: test không được để rác tiến trình trên máy người dùng.</summary>
    private sealed class CuaSoGia : IDisposable
    {
        public Process TienTrinh { get; }

        public CuaSoGia()
            => TienTrinh = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c ping -n 60 127.0.0.1",  // `timeout` thoát ngay khi không có console — `ping` thì không
                CreateNoWindow = true,
                UseShellExecute = false,
            })!;

        public void Dispose()
        {
            try { if (!TienTrinh.HasExited) { TienTrinh.Kill(entireProcessTree: true); } }
            catch { /* đã chết rồi thì thôi */ }
            try { TienTrinh.Dispose(); } catch { /* bỏ qua */ }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 4c. GÓI KHÔNG MƯỢN ĐƯỢC HỒ SƠ ⇒ KHÔNG ĐƯỢC ĐỂ MỐC HOÃN TREO LẠI
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Gói bị TỪ CHỐI KHOÁ (máy khác đang giữ) ⇒ mốc hoãn phải bị XOÁ, check đơn chạy lại NGAY.</b>
    ///
    /// <para><b>Ca hỏng mà bài này chặn</b> (phản biện vòng 1, 01/09/2026): nhịp đặt
    /// <c>MocChayLaiCheckDonSauFlash = cuối khe</c> TRƯỚC khi gọi lượt chạy, tức trước cả lượt xin khoá. Lượt
    /// hỏng ở đó là hàm về sớm mà mốc vẫn nằm đấy ⇒ <c>TraHoSo</c> bắt vòng check đơn NẰM CHỜ tới cuối khe cho
    /// một gói flash CHƯA HỀ CHẠY. Mất trọn một khe nghỉ, và không dòng log nào nói đó là oan.</para>
    /// </summary>
    [StaFact]
    public async Task GoiKhongMuonDuocHoSo_ThiKhongDeMocHoanTreoLai()
    {
        using var ban = Ban.Dung();
        var phien = ban.ChayPhienGia(ban.Id);
        phien.ThoiDiemVongKe = BayGio.AddMinutes(120);

        // Máy KHÁC đang giữ khoá tài khoản ⇒ `MuonHoSoAsync` trả false, lượt về sớm.
        ban.Services.AcquireAccountLease = (_, _) => Task.FromResult(new OrdersLeaseResult(false, "may-kia"));

        var daCho = new List<TimeSpan>();
        ban.Vm.DongHoHoanChayLai = () => BayGio.AddMinutes(5);
        ban.Vm.ChoTruocKhiChayLaiCheckDon = (t, _) => { daCho.Add(t); return Task.CompletedTask; };

        await ban.Vm.NhipLichFlashAsync(BayGio);

        // Gói ĐÃ được mở (mốc thử ghi trước khi chạy) nhưng không mượn được hồ sơ…
        Assert.Equal(BayGio, ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);
        // …nên KHÔNG lượt hoãn nào được dựng: check đơn chạy lại ngay.
        Assert.Empty(daCho);
        Assert.Null(ban.Vm.MocChayLaiCheckDonSauFlash);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 5. MỐC VÒNG KẾ — ĐẶT ĐÚNG CHỖ TRONG VÒNG NỀN CỦA PHIÊN
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b><c>ThoiDiemVongKe</c> phải được ĐẶT ngay trước lượt nghỉ và XOÁ ở mọi lối ra.</b>
    ///
    /// <para><b>Vì sao đo trên NGUỒN:</b> mệnh đề nằm trong <c>AccountSession.RunBridgeContinuousAsync</c>, mà
    /// hàm đó xin khoá xuyên máy rồi PHÓNG BRAVE THẬT — không bài test nào được chạm vào. Ba thứ có thể hỏng thì
    /// đều là thứ tự dòng lệnh, nên grep nguồn là phép đo trung thực nhất còn lại.</para>
    ///
    /// <list type="number">
    /// <item><b>Đặt SAU <c>HoanVongCheckDon</c></b> — mốc trước khi dời không phải mốc thật: gói flash sẽ tưởng
    /// khe chỉ còn 20 phút trong khi thật ra còn 4 tiếng, hoặc ngược lại.</item>
    /// <item><b>Đặt TRƯỚC <c>Task.Delay</c></b> — đặt sau thì cả kỳ nghỉ trôi qua mà không ai biết phiên đang
    /// nghỉ, và gói không bao giờ mở.</item>
    /// <item><b>XOÁ khi hết nghỉ / bị huỷ</b> (<c>finally</c>) <b>và khi phiên tháo dỡ</b>
    /// (<c>ResetBridgeState</c>) — để sót là nhịp thấy một phiên đã chết vẫn "đang nghỉ tới 21:30".</item>
    /// </list>
    /// </summary>
    [Fact]
    public void ThoiDiemVongKe_DatSauHoanVong_TruocDelay_VaXoaOMoiLoiRa()
    {
        var nguon = NguonFile("orders", "XuLyDonShopee.App", "Services", "AccountSession.cs");

        var iHoan = nguon.IndexOf("KmLichTuDong.HoanVongCheckDon(", StringComparison.Ordinal);
        var iDat = nguon.IndexOf("DatThoiDiemVongKe(nextRunAt)", StringComparison.Ordinal);
        var iDelay = nguon.IndexOf("await Task.Delay(TimeSpan.FromMinutes(phutNghi), ct)", StringComparison.Ordinal);

        Assert.True(iHoan > 0, "không còn lượt hoãn nhường khung — mốc vòng kế sẽ nói sai khi vòng bị dời");
        Assert.True(iDat > iHoan, "mốc vòng kế đặt TRƯỚC HoanVongCheckDon ⇒ đó là mốc chưa dời, không phải mốc thật");
        Assert.True(iDelay > iDat, "mốc vòng kế đặt SAU lượt nghỉ ⇒ cả kỳ nghỉ không ai biết phiên đang nghỉ");

        // Xoá ở lối ra của lượt nghỉ, và ở lượt tháo dỡ phiên.
        Assert.Contains("finally { DatThoiDiemVongKe(null); }", nguon);
        var reset = CatVung(nguon, "private void ResetBridgeState()", "private void SetError(");
        Assert.Contains("DatThoiDiemVongKe(null)", reset);
    }

    /// <summary>
    /// <b>Bộ lọc <c>boQuaShop</c> phải đi HẾT chuỗi: nhịp → <c>ChayFlashSaleAsync</c> → vòng chạy.</b>
    ///
    /// <para><b>Vì sao đo trên NGUỒN, và vì sao bắt buộc phải có</b> (lỗ phủ tìm ra bằng thử phá 01/09/2026):
    /// mọi test nhịp đều bị chặn ở cửa khoá xuyên máy nên gói tham số KHÔNG bao giờ tới được vòng chạy trong
    /// test — tức <b>bỏ hẳn đối số <c>boQuaShop</c> ở chỗ gọi vẫn xanh 100%</b>. Mà đó chính là hình dạng của
    /// lỗi tốn kém nhất: gói thứ hai mở lại đúng những shop vừa xong, mỗi shop một khe nghỉ, không dòng log nào
    /// nói ra. Test lọc ở tầng vòng (<c>FlashSaleLuongThatTests.BoQuaShop_*</c>) chỉ chứng minh vòng lọc ĐÚNG
    /// khi được đưa danh sách, không chứng minh có ai đưa.</para>
    /// </summary>
    [Fact]
    public void BoQuaShop_DiHetChuoiTuNhipXuongVongChay()
    {
        var phien = NguonFile("orders", "XuLyDonShopee.App", "ViewModels", "AccountsViewModel.Phien.cs");

        // (1) NHỊP truyền tập shop đã xong xuống lượt chạy.
        var nhip = CatVung(phien, "internal async Task NhipLichFlashAsync(", "/// <summary>PURE — mốc SỚM HƠN");
        Assert.Contains("TapShopXongFlash(acc.Id,", nhip);
        Assert.Contains("ChayFlashSaleAsync(acc.Id, acc.Email, thamSo, hanChot, boQuaShop,", nhip);

        // (2) `ChayFlashSaleAsync` rót tiếp xuống vòng chạy của cầu nối.
        var chay = CatVung(phien, "internal Task ChayFlashSaleAsync(", "internal KetLuanLatCat ChotLuotFlash(");
        Assert.Contains("login, thamSo, hanChot, boQuaShop, ct, phaiNhuongSauShop)", chay);

        // (3) Và tầng Core rót nốt vào vòng lặp shop.
        var core = NguonFile("orders", "XuLyDonShopee.Core", "Services", "OrdersBridgeSession.cs");
        var moiShop = CatVung(core, "public async Task<KetQuaKm> ChayFlashSaleMoiShopAsync(", "internal static Task<KetQuaKm> ChayFlashSaleSauPickerAsync(");
        Assert.Contains("boQuaShop: boQuaShop", moiShop);
        Assert.Contains("phaiNhuongSauShop: phaiNhuongSauShop", moiShop);

        // (4) Và `ChayFlashSaleSauPickerAsync` rót nốt cờ nhường vào vòng dùng chung — chặng cuối của chuỗi,
        //     cùng lý do đo-trên-nguồn với `boQuaShop`: bỏ đối số ở đây thì mọi test tầng nhịp vẫn xanh.
        var sauPicker = CatVung(core, "internal static Task<KetQuaKm> ChayFlashSaleSauPickerAsync(",
            "private static async Task<KetQuaKm> ChayVongTaoChuongTrinhAsync(");
        Assert.Contains("phaiNhuongSauShop: phaiNhuongSauShop", sauPicker);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 6. KHE GIẢ ĐỊNH BỊ KẸP Ở MỐC HOÃN CHẠY LẠI CHECK ĐƠN (03/09/2026, sổ P-20260903-1)
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Có một lượt hoãn chạy lại check đơn đang chờ ⇒ khe GIẢ ĐỊNH bị kẹp ở mốc đó.</b>
    ///
    /// <para><b>Ca hỏng mà bài này chặn — đo thật 03/09/2026, hai lần trong một tối:</b> gói 1 xong lúc 18:09 ⇒
    /// hoãn chạy lại check đơn tới 18:52 ⇒ 18:10:09 nhịp thấy "không phiên nào chạy" nên lấy TRỌN chu kỳ 120'
    /// làm khe giả định ⇒ MỞ GÓI 2 ⇒ 18:50 xong ⇒ 18:50:09 MỞ GÓI 3 <b>trong khi mốc hoãn chỉ còn 2'50"</b> ⇒
    /// 18:52:59 lượt hoãn mở phiên check đơn đè lên ⇒ <c>HttpListenerException</c> cổng 47821 ⇒ lượt dọn hồ sơ
    /// đóng 10 cửa sổ Brave của gói ⇒ shop Delica HỎNG. Lặp y hệt 21:25 và 22:01 (Sully), và vòng check đơn
    /// 22:01 cũng hỏng theo.</para>
    ///
    /// <para><b>Hai vế, và vế thứ hai là vế bắt buộc:</b> mốc gần (+40') ⇒ KHÔNG mở; mốc XA (+100') ⇒ VẪN mở
    /// bình thường và hạn gói tính theo chính mốc đó. Chỉ đo vế đầu thì một bản vá "có lượt hoãn thì thôi mở
    /// gói mãi mãi" cũng xanh — mà bản đó giết vòng flash sau gói đầu tiên của mỗi ngày.</para>
    /// </summary>
    [StaFact]
    public async Task CoLuotHoanDangCho_ThiKheGiaDinhChanOMocHoan()
    {
        // ── (a) MỐC GẦN (+40') ⇒ khe giả định chỉ còn 40' ⇒ dưới sàn 75' ⇒ KHÔNG mở gói.
        using (var ban = Ban.Dung())
        {
            Assert.Empty(ban.Services.Sessions.Active);
            Assert.Equal(120, ban.Services.Settings.GetOrderIntervalMinutes());   // tiền đề: chu kỳ 120'

            ban.Vm.DatMocHoanDangChoChoTest(ban.Id, BayGio.AddMinutes(40));

            await ban.Vm.NhipLichFlashAsync(BayGio);

            Assert.Null(ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);

            // Câu từ chối phải NÊU ĐÍCH DANH mốc hoãn — cách gỡ của ca này là CHỜ, không phải nới chu kỳ.
            var dong = ban.Services.Log.Snapshot("flash@mail.com")
                .Where(e => e.Message.Contains("CHƯA mở được")).ToList();
            Assert.Single(dong);
            // Neo CẢ CỤM "đã hẹn 10:40", không phải "10:40" rời: chuỗi giờ ấy còn xuất hiện ở vế "(tới HH:mm)"
            // của cùng câu, nên một bản vá quên in mốc hoãn vẫn qua được phép so lỏng.
            Assert.Contains("đã hẹn 10:40", dong[0].Message);
            Assert.DoesNotContain("nới 'Khoảng nghỉ", dong[0].Message);
        }

        // ── (b) MỐC XA (+100') ⇒ khe 100' ≥ 75' ⇒ MỞ, và mốc trả lại khe = ĐÚNG mốc hoãn (không đẩy muộn hơn).
        using (var ban = Ban.Dung())
        {
            var mocHoan = BayGio.AddMinutes(100);
            ban.Vm.DatMocHoanDangChoChoTest(ban.Id, mocHoan);

            // Chặn ở cửa khoá xuyên máy: mốc thử vẫn ghi TRƯỚC khi chạy nên bằng chứng "đã vào thân" còn nguyên,
            // mà không lượt Brave THẬT nào bị phóng.
            ban.Services.AcquireAccountLease = (_, _) => Task.FromResult(new OrdersLeaseResult(false, "may-kia"));

            await ban.Vm.NhipLichFlashAsync(BayGio);

            Assert.Equal(BayGio, ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);

            // Dòng MỞ GÓI đọc từ chính khe + hạn của gói: khe = mốc hoãn 11:40, hạn = 11:40 − 45' = 10:55.
            var moGoi = ban.Services.Log.Snapshot("flash@mail.com")
                .First(e => e.Message.Contains("MỞ GÓI"));
            Assert.Contains($"Khe nghỉ check đơn tới {mocHoan:HH:mm}", moGoi.Message);
            Assert.Contains($"không bắt đầu shop mới sau {KmLichTuDong.HanChotGoiFlash(BayGio, mocHoan):HH:mm}",
                moGoi.Message);

            // …và MÔ TẢ CHƯƠNG TRÌNH đọc từ CHÍNH gói sắp gửi đi phải nêu HAI THÔNG SỐ (03/09/2026). Đây là
            // dòng DUY NHẤT nói ra Shopee sắp nhận mấy phần trăm — bài học lỗ phủ 25/08/2026: dòng log ghép
            // từ biến rời thì nó chỉ bảo vệ chính nó, không bảo vệ thứ đi xuống Shopee.
            var cf = ban.Services.KmConfig.Get(ban.Id);
            Assert.Contains($"giảm {cf.FlashPhanTramGiam}% · SL tham gia {cf.FlashSoLuongThamGia}/SP",
                moGoi.Message);
        }
    }

    /// <summary>
    /// <b>CẢ HAI đường chạy flash phải RESET cờ nhường ngay trước lượt, và cùng truyền nó xuống vòng shop.</b>
    ///
    /// <para><b>Vì sao đo trên NGUỒN</b> (cùng lý do với <see cref="BoQuaShop_DiHetChuoiTuNhipXuongVongChay"/>):
    /// mọi test tầng nhịp đều bị chặn ở cửa khoá xuyên máy nên cờ KHÔNG bao giờ tới được vòng chạy trong test —
    /// bỏ lượt reset vẫn xanh 100%.</para>
    ///
    /// <para><b>Quên reset là ca hỏng câm và tốn:</b> cờ là của MÁY chứ không của gói, nên một lượt hoãn đã
    /// nhường xong vẫn để cờ bật; gói SAU dừng ngay sau shop đầu với câu "nhường chỗ cho check đơn" trong khi
    /// chẳng còn ai chờ. Triệu chứng người dùng thấy là "flash chỉ làm được 1 shop mỗi gói".</para>
    /// </summary>
    [Fact]
    public void CoNhuong_DuocRESET_TruocMoiLuot_OCaHaiDuongChay()
    {
        var phien = NguonFile("orders", "XuLyDonShopee.App", "ViewModels", "AccountsViewModel.Phien.cs");

        // (1) Đường NHỊP: reset rồi mới gọi, và truyền cờ xuống.
        var nhip = CatVung(phien, "internal async Task NhipLichFlashAsync(", "/// <summary>PURE — mốc SỚM HƠN");
        var iReset = nhip.IndexOf("_nhuongChoCheckDon = false;", StringComparison.Ordinal);
        var iGoi = nhip.IndexOf("ChayFlashSaleAsync(acc.Id", StringComparison.Ordinal);
        Assert.True(iReset > 0, "nhịp KHÔNG reset cờ nhường ⇒ gói sau dừng ngay sau shop đầu");
        Assert.True(iGoi > iReset, "lượt reset nằm SAU chỗ gọi ⇒ gói này chạy với cờ của lượt hoãn trước");
        Assert.Contains("() => _nhuongChoCheckDon", nhip);

        // (2) Đường BẤM TAY: cùng luật, không có ngoại lệ nào cho "người dùng ngồi trước máy".
        var tay = CatVung(phien, "private Task ChayFlashSaleTayAsync()", "/// <summary>\r\n    /// KHUNG DÙNG CHUNG");
        var jReset = tay.IndexOf("_nhuongChoCheckDon = false;", StringComparison.Ordinal);
        var jGoi = tay.IndexOf("return ChayFlashSaleAsync(", StringComparison.Ordinal);
        Assert.True(jReset > 0, "đường bấm tay KHÔNG reset cờ nhường");
        Assert.True(jGoi > jReset, "lượt reset của đường tay nằm SAU chỗ gọi");
        Assert.Contains("phaiNhuongSauShop: () => _nhuongChoCheckDon", tay);
    }

    /// <summary>
    /// <b>HAI VÒNG KM/COMBO cũng reset VÀ rót cờ nhường xuống vòng shop.</b>
    ///
    /// <para><b>Lỗ phủ mà bài này lấp</b> (phản biện vòng 2, 03/09/2026): bản đầu chỉ rót cờ cho vòng flash,
    /// trong khi cửa <c>DangChayFlashHoacCauNoi</c> gồm cả <c>_bridgeRunning</c> — tức lượt hoãn NHƯỜNG cho một
    /// vòng KM tự động 2h sáng, nhưng vòng KM ấy không hề đọc cờ. Kết cục: lượt hoãn ngồi hết trần 90 phút rồi
    /// BỎ LUÔN check đơn, còn vòng KM vẫn đi tiếp 11 shop. Không dòng nào nói ra.</para>
    ///
    /// <para>Đo trên NGUỒN, cùng lý do với bài ngay trên: mọi test tầng VM đều bị chặn ở cửa khoá xuyên máy nên
    /// đối số này không bao giờ tới được vòng chạy trong test. (Vế HÀNH VI của cờ ở tầng vòng lặp đã có
    /// <c>KmVongRigTests.PhaiNhuongSauShop_ThiVongKmDungSauShopDangChay</c>.)</para>
    /// </summary>
    [Fact]
    public void CoNhuong_DiXuongCaVongKmVaVongCombo()
    {
        var phien = NguonFile("orders", "XuLyDonShopee.App", "ViewModels", "AccountsViewModel.Phien.cs");

        foreach (var (ten, tu, den, goi) in new[]
        {
            ("KM của Shop", "private Task ChayKmAsync(long accountId",
                "internal KetLuanLatCat ChotLuotKm(", "ChayKmMoiShopAsync("),
            ("Combo Khuyến Mãi", "internal Task ChayComboAsync(long accountId",
                "internal KetLuanLatCat ChotLuotCombo(", "ChayComboMoiShopAsync("),
        })
        {
            var vung = CatVung(phien, tu, den);
            var iReset = vung.IndexOf("_nhuongChoCheckDon = false;", StringComparison.Ordinal);
            var iGoi = vung.IndexOf(goi, StringComparison.Ordinal);
            Assert.True(iReset > 0, $"vòng {ten} KHÔNG reset cờ nhường ⇒ lượt sau dừng ngay sau shop đầu");
            Assert.True(iGoi > iReset, $"lượt reset của vòng {ten} nằm SAU chỗ gọi");
            Assert.Contains("() => _nhuongChoCheckDon", vung);
        }

        // Và tầng Core rót nốt xuống vòng dùng chung — bỏ đối số ở đây thì cả hai mệnh đề trên vẫn xanh.
        var core = NguonFile("orders", "XuLyDonShopee.Core", "Services", "OrdersBridgeSession.cs");
        Assert.Contains("phaiNhuongSauShop: phaiNhuongSauShop",
            CatVung(core, "public async Task<KetQuaKm> ChayKmMoiShopAsync(",
                "/// <b>\"Combo Khuyến Mãi\" — LUỒNG THẬT.</b>"));
        Assert.Contains("phaiNhuongSauShop: phaiNhuongSauShop",
            CatVung(core, "public async Task<KetQuaKm> ChayComboMoiShopAsync(",
                "internal static Task<KetQuaKm> ChayKmSauPickerAsync("));
        Assert.Contains("phaiNhuongSauShop: phaiNhuongSauShop",
            CatVung(core, "internal static Task<KetQuaKm> ChayKmSauPickerAsync(",
                "internal static Task<KetQuaKm> ChayComboSauPickerAsync("));
        Assert.Contains("phaiNhuongSauShop: phaiNhuongSauShop",
            CatVung(core, "internal static Task<KetQuaKm> ChayComboSauPickerAsync(",
                "/// <b>\"Flash Sale Của Shop\" — LUỒNG THẬT.</b>"));
    }

    /// <summary>
    /// Vế ĐỐI CHỨNG của bài trên ở chiều còn lại: <b>KHÔNG có lượt hoãn nào ⇒ khe giả định là TRỌN chu kỳ</b>,
    /// y hành vi cũ. Hai bài <c>KhongPhienNaoChay_*</c> đã khoá vế "mở được"; bài này khoá thêm rằng phép kẹp
    /// KHÔNG tự dựng ra một mốc từ hư không (một bản vá dùng <c>DateTime.MinValue</c> làm "chưa có mốc" sẽ kẹp
    /// khe về quá khứ và giết vòng flash trên mọi máy).
    /// </summary>
    [StaFact]
    public async Task KhongCoLuotHoan_ThiKheGiaDinhVanLaTronChuKy()
    {
        using var ban = Ban.Dung();
        Assert.Null(ban.Vm.MocHoanChayLaiDangCho);

        await ban.Vm.NhipLichFlashAsync(BayGio);

        var moGoi = ban.Services.Log.Snapshot("flash@mail.com").First(e => e.Message.Contains("MỞ GÓI"));
        Assert.Contains($"Khe nghỉ check đơn tới {BayGio.AddMinutes(120):HH:mm}", moGoi.Message);
    }

    /// <summary>
    /// <b><see cref="AccountsViewModel.MocHoanChayLaiDangCho"/> được ĐẶT khi phóng lượt hoãn và XOÁ ở CẢ BA lối
    /// ra.</b> Để sót ở một lối là mọi gói flash sau đó bị kẹp khe vào một mốc của lượt hoãn ĐÃ CHẾT — vĩnh
    /// viễn, vì không còn ai xoá nó nữa, và triệu chứng chỉ là "flash thôi chạy" cùng một dòng từ chối mỗi giờ.
    /// </summary>
    [StaFact]
    public async Task MocHoanDangCho_DatKhiPhong_VaXoaOMoiLoiRa()
    {
        // ── Lối ra 1: lượt hoãn CHẠY tới cùng.
        using (var b = Ban.Dung())
        {
            var cuoiKhe = BayGio.AddMinutes(60);
            b.Vm.DongHoHoanChayLai = () => BayGio;
            b.Vm.ChayLaiPhienCheckDon = _ => { };
            var choXong = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            b.Vm.ChoTruocKhiChayLaiCheckDon = (_, _) => choXong.Task;

            b.Vm.DatMocChayLaiSauFlashChoTest(cuoiKhe);
            b.Vm.TraHoSoChoTest(b.Id);
            Assert.Equal(cuoiKhe, b.Vm.MocHoanChayLaiDangCho);   // ĐANG chờ ⇒ nhịp phải thấy

            choXong.TrySetResult(true);
            if (b.Vm.LuotHoanGanNhat is Task t) { await t; }
            Assert.Null(b.Vm.MocHoanChayLaiDangCho);
        }

        // ── Lối ra 2: người dùng HUỶ lượt hoãn.
        using (var b = Ban.Dung())
        {
            b.Vm.DongHoHoanChayLai = () => BayGio;
            b.Vm.ChayLaiPhienCheckDon = _ => { };
            b.Vm.ChoTruocKhiChayLaiCheckDon = (_, ct) => Task.Delay(Timeout.Infinite, ct);

            b.Vm.DatMocChayLaiSauFlashChoTest(BayGio.AddMinutes(60));
            b.Vm.TraHoSoChoTest(b.Id);
            Assert.NotNull(b.Vm.MocHoanChayLaiDangCho);

            b.Vm.HuyLuotHoanChayLaiCheckDon("người dùng bấm Dừng tất cả");
            Assert.Null(b.Vm.MocHoanChayLaiDangCho);
            if (b.Vm.LuotHoanGanNhat is Task t) { await t; }
            Assert.Null(b.Vm.MocHoanChayLaiDangCho);   // nhánh "ĐÃ BỊ HUỶ" cũng không dựng lại
        }
    }

    /// <summary>
    /// <b>⚠ NHỊP BẮN ĐÚNG LÚC LƯỢT HOÃN ĐANG NHƯỜNG ⇒ VẪN PHẢI TỪ CHỐI.</b> Đây là nửa "nhịp" của ca đua mà
    /// <c>FlashHoanChayLaiTests.DuaVoiLuotResetCo_*</c> đo ở nửa kia.
    ///
    /// <para><b>Ca hỏng</b> (phản biện vòng 2, 03/09/2026): bản đầu xoá mốc NGAY khi lượt hoãn tới, trước vòng
    /// chờ. Gói vừa nhường xong ⇒ không phiên nào chạy + mốc đã <c>null</c> ⇒ nhịp 60 giây thấy khe giả định là
    /// trọn 120' và MỞ GÓI MỚI — <b>ngay trên đầu một lượt check đơn đang đứng chờ</b>, đồng thời RESET cờ
    /// nhường nên gói mới cũng không nhường. Lượt hoãn ngồi hết trần 90' rồi bỏ luôn check đơn.</para>
    ///
    /// <para>Trạng thái dựng ở đây là trạng thái THẬT của khoảnh khắc đó: không phiên nào chạy (gói vừa dừng
    /// hết), lượt hoãn đang trong vòng nhường.</para>
    /// </summary>
    [StaFact]
    public async Task NhipTrongLucNhuong_ThiVanTuChoiViMocHoan()
    {
        using var ban = Ban.Dung();
        Assert.Empty(ban.Services.Sessions.Active);      // tiền đề: gói vừa dừng hết phiên

        var moc = BayGio.AddMinutes(40);
        ban.Vm.DongHoHoanChayLai = () => BayGio;
        ban.Vm.ChayLaiPhienCheckDon = _ => { };
        ban.Vm.ChoTruocKhiChayLaiCheckDon = (_, _) => Task.CompletedTask;
        ban.Vm.DangChayFlashHoacCauNoi = () => true;     // gói CÒN đang chạy khi lượt hoãn tới mốc

        var daVaoNhuong = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ban.Vm.ChoNhipNhuongFlash = ct =>
        {
            daVaoNhuong.TrySetResult(true);
            return Task.Delay(Timeout.Infinite, ct);
        };

        ban.Vm.DatMocChayLaiSauFlashChoTest(moc);
        ban.Vm.TraHoSoChoTest(ban.Id);

        var xong = await Task.WhenAny(daVaoNhuong.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(ReferenceEquals(xong, daVaoNhuong.Task), "vòng nhường không vào tới nhịp chờ trong 5 giây");

        // ĐANG nhường ⇒ mốc PHẢI còn ⇒ nhịp thấy và từ chối.
        Assert.Equal(moc, ban.Vm.MocHoanChayLaiDangCho);

        await ban.Vm.NhipLichFlashAsync(BayGio);

        Assert.Null(ban.Services.KmConfig.Get(ban.Id).FlashLanThuTuDong);   // KHÔNG vào tới thân ⇒ KHÔNG mở gói
        var dong = ban.Services.Log.Snapshot("flash@mail.com")
            .Where(e => e.Message.Contains("CHƯA mở được")).ToList();
        Assert.Single(dong);
        Assert.Contains($"đã hẹn {moc:HH:mm}", dong[0].Message);

        ban.Vm.HuyLuotHoanChayLaiCheckDon("dọn bài test");
        if (ban.Vm.LuotHoanGanNhat is Task t) { await t; }
    }

    /// <summary>
    /// <b>MỖI TÀI KHOẢN MỘT MỐC, và khe giả định bị kẹp ở mốc SỚM NHẤT.</b>
    ///
    /// <para><b>Ca hỏng</b> (phản biện vòng 2, 03/09/2026): bản đầu giữ đúng MỘT <c>DateTime?</c> dùng chung cho
    /// mọi tài khoản. Hai tài khoản cùng hoãn thì cái sau GHI ĐÈ mốc cái trước, và lượt xoá của MỘT trong hai
    /// xoá luôn mốc của bên kia ⇒ nhịp lại mở gói đè lên một lượt hoãn còn sống. Cầu nối là tài nguyên CHUNG của
    /// cả máy (một hồ sơ Brave, một cổng 47821) nên mốc của bất kỳ tài khoản nào cũng phải chặn gói của mọi tài
    /// khoản — phép đọc vì thế là <c>Min</c>.</para>
    /// </summary>
    [StaFact]
    public void HaiTaiKhoanHoan_ThiLayMocSomNhat_VaXoaRiengTungCai()
    {
        using var ban = Ban.Dung();
        var idB = ban.ThemTaiKhoan("flash-b@mail.com");

        var gan = BayGio.AddMinutes(40);
        var xa = BayGio.AddMinutes(100);
        ban.Vm.DatMocHoanDangChoChoTest(ban.Id, gan);
        ban.Vm.DatMocHoanDangChoChoTest(idB, xa);

        Assert.Equal(gan, ban.Vm.MocHoanChayLaiDangCho);   // MIN, không phải "cái đặt sau"

        // Xoá cái GẦN ⇒ còn cái XA, KHÔNG phải null. Bản dùng chung một ô sẽ ra null ở đây.
        ban.Vm.DatMocHoanDangChoChoTest(ban.Id, null);
        Assert.Equal(xa, ban.Vm.MocHoanChayLaiDangCho);

        ban.Vm.DatMocHoanDangChoChoTest(idB, null);
        Assert.Null(ban.Vm.MocHoanChayLaiDangCho);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 7. CÂU TỔNG KẾT NÓI ĐÚNG LÝ DO DỪNG (03/09/2026 — sổ P-20260903-7 + nhánh nhường của P-20260903-1)
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Câu tổng kết in HẠN CỦA GÓI, không phải giờ đóng khung của loại.</b>
    ///
    /// <para><b>Lỗi thật (sổ P-20260903-7):</b> gói flash dừng lúc 18:07 vì hạn của GÓI (= min(cuối khe − 45',
    /// 22:15)), nhưng nhật ký in <i>"DỪNG vì hết khung tự động (23:00)"</i> — lấy
    /// <see cref="KmLichTuDong.GioDongKhungFlashSale"/>. Người đọc đi tìm một mốc 23:00 không liên quan, mà đây
    /// là dòng DUY NHẤT nói ra vì sao gói chỉ làm được một shop.</para>
    ///
    /// <para><b>Vế RƠI VỀ cũng phải đo:</b> lượt KHÔNG mang hạn (<c>HanChotDaCham == null</c> — hai vòng
    /// KM/Combo dừng vì đúng cái khung của chúng) vẫn in giờ đóng khung, vì ở đó con số ấy MỚI là con số đúng.
    /// Hai bài <c>ComboKhuyenMaiTests</c> neo "(8:00)"/"(4:00)" phải xanh nguyên.</para>
    /// </summary>
    [Fact]
    public void CauTongKet_HetKhung_InHanCuaGoi_KhongPhaiGioDongKhung()
    {
        var hanGoi = new DateTime(2026, 9, 3, 18, 7, 0);
        var kq = new KetQuaKm([], false, null, HetKhung: true, SoShopChuaChay: 3, HanChotDaCham: hanGoi);

        var cau = AccountsViewModel.CauTongKetKm(kq, false, LoaiChuongTrinh.FlashSale);
        Assert.Contains("hết khung tự động (18:07)", cau);
        Assert.DoesNotContain("(23:00)", cau);
        Assert.Contains("còn 3 shop", cau);

        // Ô TRẠNG THÁI cũng phải mang mốc thật — người dùng nhìn nó trước cả nhật ký.
        Assert.Contains("(18:07)", AccountsViewModel.CauTrangThaiKm(kq, LoaiChuongTrinh.FlashSale));

        // VẾ RƠI VỀ: lượt không mang hạn ⇒ giờ đóng khung của loại (flash 23:00).
        var khongHan = new KetQuaKm([], false, null, HetKhung: true, SoShopChuaChay: 3);
        Assert.Contains($"hết khung tự động ({KmLichTuDong.GioDongKhungFlashSale}:00)",
            AccountsViewModel.CauTongKetKm(khongHan, false, LoaiChuongTrinh.FlashSale));
    }

    /// <summary>
    /// <b>Cờ NHƯỜNG CHỖ ⇒ câu tổng kết nói đúng tên lý do; và nó xếp SAU nhánh <c>Loi</c>.</b>
    ///
    /// <para><b>Vế thứ hai là vế bắt buộc</b> (sổ P-20260901-6, đúng lỗi đã xảy ra một lần với
    /// <c>NgayMaiKhongDo</c>): một gói vừa nhường chỗ VỪA hỏng thật — cầu nối đứt, <c>vePicker</c> không về —
    /// mà nhánh mới đứng TRƯỚC <c>Loi</c> thì câu êm ái "không phải lỗi" sẽ NUỐT MẤT câu lỗi duy nhất của lượt.
    /// Cùng luật cho ô trạng thái.</para>
    /// </summary>
    [Fact]
    public void CauTongKet_NhuongChoCheckDon_NoiDungLyDo_VaSauNhanhLoi()
    {
        var kq = new KetQuaKm([], false, null, SoShopChuaChay: 2, NhuongChoCheckDon: true);
        var cau = AccountsViewModel.CauTongKetKm(kq, false, LoaiChuongTrinh.FlashSale);
        Assert.Contains("NHƯỜNG CHỖ CHO CHECK ĐƠN", cau);
        Assert.Contains("còn 2 shop", cau);
        Assert.DoesNotContain("hết khung tự động", cau);
        Assert.Contains("nhường chỗ cho check đơn",
            AccountsViewModel.CauTrangThaiKm(kq, LoaiChuongTrinh.FlashSale));

        // VỪA NHƯỜNG VỪA HỎNG ⇒ nói cái HỎNG, không nói câu êm ái.
        var caHai = kq with { Loi = "cầu nối đứt giữa chặng" };
        var cauCaHai = AccountsViewModel.CauTongKetKm(caHai, false, LoaiChuongTrinh.FlashSale);
        Assert.Contains("cầu nối đứt giữa chặng", cauCaHai);
        Assert.DoesNotContain("NHƯỜNG CHỖ CHO CHECK ĐƠN", cauCaHai);
        Assert.Contains("cầu nối đứt giữa chặng",
            AccountsViewModel.CauTrangThaiKm(caHai, LoaiChuongTrinh.FlashSale));
    }

    // ── Đồ nghề ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>10:00 ngày 01/09/2026 — giữa khung cả ngày, cách xa hạn 22:15.</summary>
    private static readonly DateTime BayGio = new(2026, 9, 1, 10, 0, 0);

    /// <summary>Một bàn thử: DB tạm + AppServices dùng PHIÊN GIẢ (không Brave) + VM + một tài khoản bật flash.</summary>
    private sealed class Ban : IDisposable
    {
        private readonly Dictionary<long, PhienGia> _phien = new();
        public TempDatabase Temp { get; private init; } = null!;
        public AppServices Services { get; private set; } = null!;
        public AccountsViewModel Vm { get; private set; } = null!;
        public long Id { get; private set; }

        public static Ban Dung()
        {
            var temp = new TempDatabase();
            var ban = new Ban { Temp = temp };
            // Factory phải vào từ CTOR của AppServices: AccountsViewModel đăng ký Sessions.Changed ngay trong
            // ctor của nó (xem MoListShopTests), gán lại Sessions sau đó là cắt đứt người nghe.
            var services = new AppServices(temp.Path, id => ban.LayPhien(id));
            ban.Services = services;
            ban.Vm = new AccountsViewModel(services);
            ban.Id = ban.ThemTaiKhoan("flash@mail.com");
            return ban;
        }

        public long ThemTaiKhoan(string email)
        {
            Services.Accounts.Insert(new Account { Email = email, Password = "p" });
            var id = Services.Accounts.GetAll().First(a => a.Email == email).Id;
            var cu = Services.KmConfig.Get(id);
            Services.KmConfig.Save(cu with { FlashBat = true });
            return id;
        }

        public long IdCuaEmail(string email) => Services.Accounts.GetAll().First(a => a.Email == email).Id;

        /// <summary>Đưa một phiên GIẢ vào trạng thái ĐANG CHẠY. Gọi thẳng <c>StartAsync</c> sau lượt đăng ký ở
        /// manager: phiên thứ hai trở đi vốn bị manager cho xếp hàng (một slot cầu nối), mà bài đo phép
        /// <c>Min</c> thì cần hai phiên cùng chạy.</summary>
        public PhienGia ChayPhienGia(long id)
        {
            Services.Sessions.Start(id);
            var p = LayPhien(id);
            _ = p.StartAsync();
            return p;
        }

        /// <summary>Đưa một phiên GIẢ vào hàng đợi ĐÚNG đường thật: manager tự gọi <c>MarkQueued</c> khi slot
        /// cầu nối đã có chủ.</summary>
        public PhienGia XepHangPhienGia(long id)
        {
            Services.Sessions.Start(id);
            return LayPhien(id);
        }

        private PhienGia LayPhien(long id)
        {
            if (!_phien.TryGetValue(id, out var p)) { _phien[id] = p = new PhienGia(id); }
            return p;
        }

        public void Dispose() => Temp.Dispose();
    }

    /// <summary>Phiên giả: KHÔNG mở trình duyệt, và cho bài test đặt thẳng <c>ThoiDiemVongKe</c>.</summary>
    private sealed class PhienGia : IAccountSession
    {
        public PhienGia(long id) => AccountId = id;

        public long AccountId { get; }
        public SessionState State { get; private set; } = SessionState.Stopped;
        public string? StatusText => null;
        public int? ToShipCount => null;
        public bool ReadyForActions => true;
        public bool IsShopLoopRunning => false;

        /// <summary>Mốc vòng kế — <c>null</c> = ĐANG giữa một vòng check đơn (nhịp flash phải từ chối mở gói).</summary>
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

        /// <summary>Y luật phiên thật: chỉ hạ về hàng đợi khi phiên chưa hoạt động.</summary>
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
}
