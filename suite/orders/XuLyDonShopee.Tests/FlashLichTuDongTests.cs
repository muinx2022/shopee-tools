using System;
using System.Linq;
using System.Threading.Tasks;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>LỊCH FLASH SALE TỰ ĐỘNG — khung CẢ NGÀY <c>[0,23)</c>, hạn shop mới 22:15</b> (người dùng chốt
/// 01/09/2026; trước đó là khung tối 21:00–23:45, chốt 28/08/2026).
///
/// <para>Bốn mệnh đề, theo đúng thứ tự nguy hiểm:</para>
/// <list type="number">
/// <item><b>Công tắc <c>flash_bat</c> mặc định TẮT</b> — không tài khoản nào tự dưng có một vòng tạo tới 8
/// chương trình THẬT mỗi shop mỗi ngày;</item>
/// <item><b>Trong khung + công tắc BẬT ⇒ nhịp mở gói; TẮT ⇒ không chạy gì</b>;</item>
/// <item><b>Gói đầu HỎNG ⇒ nhịp phút sau KHÔNG mở lại</b> — mốc <c>FlashLanThuTuDong</c> ghi TRƯỚC khi chạy.
/// Đây là phép quan trọng nhất của cả file: khung nay dài CẢ NGÀY và nhịp đồng hồ là 60 giây, nên thiếu chốt
/// này là hàng trăm lần phóng Brave + SSO một ngày;</item>
/// <item><b><c>MocChayKe</c> cho FlashSale trả mốc đầu khung (00:00)</b>, không phải <c>null</c>.</item>
/// </list>
/// </summary>
public class FlashLichTuDongTests
{
    /// <summary>Cấu hình gốc: mọi vòng TẮT, chu kỳ 3 ngày (mặc định của form).</summary>
    private static KmConfig Cf(long id = 1, bool flashBat = false, DateTime? flashChayCuoi = null,
                               DateTime? flashLanThu = null, string owner = "")
        => new(id, TuDong: false, SoNgay: 3, PhanTram: 5, GioBatDau: "09:30", GioKetThuc: "10:30",
               LanChayCuoi: null)
        {
            FlashBat = flashBat,
            FlashLanChayCuoi = flashChayCuoi,
            FlashLanThuTuDong = flashLanThu,
            OwnerMachine = owner,
        };

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 1. CÔNG TẮC — mặc định TẮT, và `VongBat` phải THẬT SỰ hỏi nó
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b><c>flash_bat</c> mặc định TẮT ở MỌI cửa</b>: bản mặc định trong bộ nhớ, dòng vừa ghi xuống SQLite, và
    /// dòng CŨ vừa được migration thêm cột.
    /// <para>Đây là luật đã trả giá ở <c>ComboSanSang</c>: một vòng bật sẵn mà người dùng chưa đặt sẽ mở Brave
    /// + SSO + tạo chương trình THẬT trên mọi shop, mỗi đêm, cho tới khi ai đó phát hiện.</para>
    /// </summary>
    [Fact]
    public void FlashBat_MacDinhTAT_OMoiCua()
    {
        // (a) bản mặc định khi tài khoản chưa có dòng nào
        Assert.False(KmConfigRepository.MacDinh(7).FlashBat);

        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = "f@mail.com", Password = "p" });
        var id = services.Accounts.GetAll().First().Id;

        // (b) đọc từ DB khi chưa ai lưu gì
        Assert.False(services.KmConfig.Get(id).FlashBat);

        // (c) ⚠ CA THẬT SỰ NGUY HIỂM: DÒNG CŨ vừa được migration thêm cột.
        // Người dùng đã cấu hình combo từ tháng trước ⇒ km_config CÓ dòng. Bản nâng cấp chạy
        // `ALTER TABLE ADD COLUMN flash_bat … DEFAULT x`, và SQLite BACKFILL đúng giá trị `x` vào mọi dòng
        // đang có. `x = 1` nghĩa là **cả fleet bật vòng Flash Sale ngay đêm nâng cấp** mà không ai bấm gì —
        // tạo tới 8 chương trình THẬT mỗi shop. Hai cửa ở trên KHÔNG bắt được ca này: `MacDinh` là bản trong
        // bộ nhớ, còn `Save` luôn ghi giá trị tường minh, nên DEFAULT của cột chưa bao giờ được đọc tới.
        // Ở đây INSERT bằng đúng bộ cột của bản CŨ (không có 3 cột flash) rồi đọc lại.
        using (var conn = services.Database.OpenConnection())
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO km_config
                                    (account_id, tu_dong, so_ngay, phan_tram, gio_bat_dau, gio_ket_thuc)
                                VALUES ($id, 1, 3, 5, '09:30', '10:30');";
            cmd.Parameters.AddWithValue("$id", id + 100);
            cmd.ExecuteNonQuery();
        }
        Assert.False(services.KmConfig.Get(id + 100).FlashBat,
            "dòng km_config CŨ nhận flash_bat = 1 sau migration — cả fleet bật vòng Flash Sale mà không ai bấm gì");

        // (d) LƯU rồi ĐỌC LẠI — vòng tròn phải giữ đúng giá trị, cả hai chiều.
        services.KmConfig.Save(Cf(id, flashBat: true));
        Assert.True(services.KmConfig.Get(id).FlashBat);
        services.KmConfig.Save(Cf(id, flashBat: false));
        Assert.False(services.KmConfig.Get(id).FlashBat);
    }

    /// <summary>
    /// <b><c>VongBat(FlashSale)</c> phải HỎI công tắc</b> — trả cứng <c>true</c>/<c>false</c> đều phải làm test
    /// này ĐỎ (đó là phép thử phá của mục 1).
    /// <para>Vế <c>FlashSanSang</c> viết theo hằng chứ KHÔNG ghim <c>true</c>: hằng đó là công tắc tắt-gấp khi
    /// DOM Shopee vỡ, và một test ghim giá trị sẽ đỏ OAN đúng lúc cần tắt gấp rồi bị ai đó xoá đi.</para>
    /// </summary>
    [Fact]
    public void VongBatFlash_TheoDungCongTac()
    {
        Assert.False(KmLichTuDong.VongBat(Cf(flashBat: false), LoaiChuongTrinh.FlashSale));
        Assert.Equal(KmLichTuDong.FlashSanSang,
            KmLichTuDong.VongBat(Cf(flashBat: true), LoaiChuongTrinh.FlashSale));

        // ĐỘC LẬP với hai vòng kia: bật flash KHÔNG kéo theo cái nào.
        var chiFlash = Cf(flashBat: true);
        Assert.False(KmLichTuDong.VongBat(chiFlash, LoaiChuongTrinh.ChuongTrinhShop));
        Assert.False(KmLichTuDong.VongBat(chiFlash, LoaiChuongTrinh.Combo));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2. CỬA MỞ GÓI — CẢ NGÀY [0,23), HẠN SHOP MỚI 22:15
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ma trận giờ × công tắc trên <see cref="KmLichTuDong.DenGioGoiFlash"/>.
    /// <para><b>ĐỔI VẾ 01/09/2026</b>: khung cũ 21–24h dựng trên giả định "ban ngày Shopee chưa mở khung của
    /// ngày mai" — người dùng bác bỏ bằng bằng chứng (lượt 19:03 ngày 31/08 tạo được, 30/08 còn thấy draft cho
    /// <c>D+2</c>). Vì lượt đặt cho NGÀY HÔM SAU nên mọi giờ trong ngày đều hợp lệ; cửa sổ 3 tiếng thì không
    /// đủ chỗ cho 12 shop × ~40 phút.</para>
    /// </summary>
    [Theory]
    [InlineData(0, 30, true, true)]     // 00:30 — đầu ngày, BẬT ⇒ mở gói
    [InlineData(11, 0, true, true)]     // 11:00 — giữa trưa, nay là giờ HỢP LỆ
    [InlineData(22, 14, true, true)]    // 22:14 — sát hạn shop mới, vẫn kịp
    [InlineData(22, 20, false, false)]  // (công tắc tắt ⇒ luôn KHÔNG, ở bất kỳ giờ nào)
    [InlineData(22, 20, true, false)]   // 22:20 — quá hạn 22:15 ⇒ THÔI
    [InlineData(23, 30, true, false)]   // 23:30 — ngoài khung [0,23)
    [InlineData(11, 0, false, false)]   // giữa trưa, TẮT ⇒ KHÔNG
    public void DenGioGoiFlash_MaTranGioVaCongTac(int gio, int phut, bool bat, bool mongDoi)
    {
        var bayGio = new DateTime(2026, 8, 29).AddHours(gio).AddMinutes(phut);
        Assert.Equal(mongDoi, KmLichTuDong.DenGioGoiFlash(Cf(flashBat: bat), bayGio));
    }

    /// <summary>
    /// <b>GIÃN CÁCH GÓI 30 phút</b> — thay chốt "một lượt thử mỗi khung", thứ đã chết theo khung 21–24h (khung
    /// nay là cả ngày, nên "một lượt mỗi khung" sẽ thành một lượt mỗi NGÀY).
    /// <para>Đo cả hai vế quanh mốc: 29 phút ⇒ CHƯA, 31 phút ⇒ ĐƯỢC. Một vế thì một bản "luôn cho" hoặc "luôn
    /// chặn" vẫn xanh.</para>
    /// </summary>
    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(31, true)]
    [InlineData(0, false)]
    public void GianCachGoi_ChanGoiMoiTrong30Phut(int phutTruoc, bool mongDoi)
    {
        var bayGio = new DateTime(2026, 8, 29, 14, 0, 0);
        var cf = Cf(flashBat: true) with { FlashLanThuTuDong = bayGio.AddMinutes(-phutTruoc) };
        Assert.Equal(mongDoi, KmLichTuDong.DenGioGoiFlash(cf, bayGio));
    }

    /// <summary>
    /// <b>Mốc NGÀY của HÔM NAY ⇒ thôi mở gói; mốc của HÔM QUA ⇒ vẫn mở.</b>
    /// <para>Ý nghĩa của <c>FlashLanChayCuoi</c> nay là "ngày đích của hôm đó đã phủ ĐỦ MỌI SHOP" — xem
    /// <c>AccountsViewModel.ChotLuotFlash</c>. Vế "hôm qua" là vế chống khoá vĩnh viễn.</para>
    /// </summary>
    [Fact]
    public void MocNgayHomNay_ThiThoiMoGoi_MocHomQuaThiVanMo()
    {
        var bayGio = new DateTime(2026, 8, 29, 14, 0, 0);

        var xongHomNay = Cf(flashBat: true, flashChayCuoi: new DateTime(2026, 8, 29, 9, 30, 0));
        Assert.False(KmLichTuDong.DenGioGoiFlash(xongHomNay, bayGio));

        var xongHomQua = Cf(flashBat: true, flashChayCuoi: new DateTime(2026, 8, 28, 21, 30, 0));
        Assert.True(KmLichTuDong.DenGioGoiFlash(xongHomQua, bayGio));
    }

    /// <summary>
    /// <b>Nhịp lịch THẬT: công tắc BẬT ⇒ ghi mốc thử + đi tới lượt chạy; TẮT ⇒ không đụng gì.</b>
    ///
    /// <para>Đo trên <c>NhipLichFlashAsync</c> chứ không chỉ trên hàm thuần: một hàm thuần đúng mà nhịp quên
    /// gọi thì lịch không bao giờ nổ, và không test hàm-thuần nào đỏ. Bằng chứng "nhịp đã vào tới thân" là
    /// <c>FlashLanThuTuDong</c> vừa được ghi — lượt chạy thật dừng ngay sau đó ở cửa khoá xuyên máy (không có
    /// hub trong test) nên không có trình duyệt nào được mở.</para>
    /// </summary>
    [StaFact]
    public async Task NhipLichFlash_BatThiBanLuot_TatThiKhongChayGi()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = "flash-nhip@mail.com", Password = "p" });
        var id = services.Accounts.GetAll().First().Id;
        var bayGio = new DateTime(2026, 8, 29, 21, 5, 0);

        // ── (a) TẮT ⇒ KHÔNG đụng gì: không mốc thử, không dòng nhật ký nào của vòng flash.
        services.KmConfig.Save(Cf(id, flashBat: false));
        await new AccountsViewModel(services).NhipLichFlashAsync(bayGio);
        Assert.Null(services.KmConfig.Get(id).FlashLanThuTuDong);

        // ── (b) BẬT ⇒ nhịp vào thân: ghi mốc thử TRƯỚC khi chạy.
        services.KmConfig.Save(Cf(id, flashBat: true));
        await new AccountsViewModel(services).NhipLichFlashAsync(bayGio);
        Assert.Equal(bayGio, services.KmConfig.Get(id).FlashLanThuTuDong);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 3. ⚠ PHÉP QUAN TRỌNG NHẤT — LƯỢT ĐẦU HỎNG THÌ NHỊP PHÚT SAU KHÔNG BẮN LẠI
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Gói đầu HỎNG NHANH thì nhịp 60 giây kế tiếp KHÔNG được mở gói mới — phải chờ đủ 30 phút giãn cách.</b>
    ///
    /// <para><b>Đây là ca hỏng NẶNG NHẤT trong ba vòng nếu để lọt.</b> Khung Flash nay dài CẢ NGÀY và nhịp đồng
    /// hồ là 60 giây; mọi lối ra hỏng (captcha · máy khác giữ khoá · lỗi shop đầu) đều cố ý KHÔNG ghi mốc ngày.
    /// Thiếu <c>FlashLanThuTuDong</c> thì vế giãn cách của <c>DenGioGoiFlash</c> không có gì để đo ⇒ mở gói mới
    /// MỖI PHÚT ⇒ hàng trăm lần phóng Brave + SSO một ngày, đúng thứ làm Shopee bật <c>verify/traffic</c>.</para>
    ///
    /// <para><b>ĐỔI VẾ 01/09/2026</b>: cũ là "cấm tới hết khung đêm nay", mới là "cấm trong 30 phút" — vì cả
    /// thiết kế mới dựng trên việc chạy NHIỀU gói một ngày. Vế cuối (sau 30 phút thì MỞ LẠI) vì thế cũng là một
    /// mệnh đề thật, không phải phần thừa: cấm quá tay là 10 shop cuối không bao giờ chạy.</para>
    /// </summary>
    [StaFact]
    public async Task GoiDauHong_ThiNhipPhutSau_KHONG_MoLai_NhungSau30PhutThiCo()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = "flash-hong@mail.com", Password = "p" });
        var id = services.Accounts.GetAll().First().Id;

        var nhip1 = new DateTime(2026, 8, 29, 10, 5, 0);
        services.KmConfig.Save(Cf(id, flashBat: true));

        // Trước nhịp đầu: TỚI HẠN.
        Assert.True(KmLichTuDong.DenGioGoiFlash(services.KmConfig.Get(id), nhip1));

        await new AccountsViewModel(services).NhipLichFlashAsync(nhip1);

        var sauNhip1 = services.KmConfig.Get(id);
        // Gói HỎNG: mốc THỬ có, mốc NGÀY không.
        Assert.Equal(nhip1, sauNhip1.FlashLanThuTuDong);
        Assert.Null(sauNhip1.FlashLanChayCuoi);

        // ── Trong 30 phút giãn cách: MỌI nhịp đều bị chặn.
        foreach (var phut in new[] { 1, 5, 15, 29 })
        {
            var nhipSau = nhip1.AddMinutes(phut);
            Assert.True(KmLichTuDong.TrongKhung(nhipSau, LoaiChuongTrinh.FlashSale));
            Assert.False(KmLichTuDong.DenGioGoiFlash(sauNhip1, nhipSau),
                $"nhịp {nhipSau:HH:mm} vẫn mở gói — vòng flash sẽ phóng Brave lại chỉ sau {phut} phút");
        }

        // ── HẾT giãn cách ⇒ MỞ LẠI. Không có vế này thì phần còn lại của danh sách shop chết theo gói đầu.
        Assert.True(KmLichTuDong.DenGioGoiFlash(sauNhip1, nhip1.AddMinutes(31)));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 4. MocChayKe — ĐẦU NGÀY (00:00), và CHU KỲ CỨNG MỖI NGÀY
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>MocChayKe</c> cho FlashSale trả mốc <b>đầu khung = 00:00</b>, không phải <c>null</c> và không phải giờ
    /// của vòng khác.
    /// <para><b>ĐỔI TỪ 21:00 ngày 01/09/2026</b> theo khung mới <c>[0,23)</c>. Đang TRONG khung (mà cả ngày đều
    /// là khung) thì <c>KhungGanNhat</c> trả CHÍNH thời điểm hiện tại — nên hai vế dưới đây cho hai câu trả lời
    /// khác nhau, và đó là đúng.</para>
    /// </summary>
    [Fact]
    public void MocChayKeFlash_LaDauNgay_KhongPhaiNull()
    {
        Assert.Equal(0, KmLichTuDong.GioMoKhungFlashSale);
        var cf = Cf(flashBat: true, flashChayCuoi: new DateTime(2026, 8, 29, 21, 30, 0));

        // Chạy hôm qua ⇒ lượt kế là 00:00 HÔM NAY (chu kỳ CỨNG 1 ngày, KHÔNG đọc cf.SoNgay = 3) — mốc ấy đã
        // trôi qua so với 10:00 nên rơi về "khung gần nhất", mà 10:00 vẫn nằm trong khung ⇒ chính nó.
        var bayGio = new DateTime(2026, 8, 30, 10, 0, 0);
        Assert.Equal(bayGio, KmLichTuDong.MocChayKe(cf, bayGio, LoaiChuongTrinh.FlashSale));

        // Mốc chưa trôi qua (đứng trước nửa đêm hôm trước) ⇒ đúng 00:00 của ngày kế.
        var demTruoc = new DateTime(2026, 8, 29, 22, 0, 0);
        Assert.Equal(new DateTime(2026, 8, 30), KmLichTuDong.MocChayKe(cf, demTruoc, LoaiChuongTrinh.FlashSale));

        // Chưa chạy lần nào ⇒ khung GẦN NHẤT sắp tới; đang trong khung ⇒ chính bây giờ.
        Assert.Equal(bayGio, KmLichTuDong.MocChayKe(Cf(flashBat: true), bayGio, LoaiChuongTrinh.FlashSale));
    }

    /// <summary>
    /// <b>Chu kỳ vòng FLASH là CỨNG 1 ngày, KHÔNG đọc <c>cf.SoNgay</c></b> — trong khi hai vòng kia thì có.
    ///
    /// <para>Bỏ một đêm Flash Sale là <b>mất trắng ngày đó</b>: lượt đêm sau chỉ nhắm ngày của chính nó, không
    /// có lượt nào "bù" được. Với <c>SoNgay = 3</c> thì 2/3 số ngày trong tháng sẽ không có flash sale, và
    /// không một dòng log nào nói ra điều đó.</para>
    /// </summary>
    [Fact]
    public void ChuKyFlash_CungMotNgay_KhongDocSoNgay()
    {
        var cf = Cf(flashBat: true, flashChayCuoi: new DateTime(2026, 8, 29, 21, 30, 0));

        Assert.Equal(KmLichTuDong.SoNgayChuKyFlash,
            KmLichTuDong.SoNgayChuKy(cf, LoaiChuongTrinh.FlashSale));
        Assert.Equal(1, KmLichTuDong.SoNgayChuKyFlash);

        // Vế ĐỐI CHỨNG — hai vòng kia VẪN đi theo `cf.SoNgay` (3).
        Assert.Equal(cf.SoNgay, KmLichTuDong.SoNgayChuKy(cf, LoaiChuongTrinh.ChuongTrinhShop));
        Assert.Equal(cf.SoNgay, KmLichTuDong.SoNgayChuKy(cf, LoaiChuongTrinh.Combo));

        // Hệ quả THẬT: NGÀY SAU đã tới hạn lại (chứ không phải sau 3 ngày). Hỏi qua cửa của chính vòng flash —
        // từ 01/09/2026 vòng này KHÔNG đi qua `DenGioChay` nữa.
        Assert.True(KmLichTuDong.DenGioGoiFlash(cf, new DateTime(2026, 8, 30, 21, 5, 0)));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 5. CÁI GIÁ LÊN CHECK ĐƠN — con số phải NÓI RA ĐƯỢC
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Bật vòng flash ⇒ check đơn KHÔNG mất một giờ nào — ĐÚNG 0 giờ bị dời, cả 24 giờ trong ngày.</b>
    ///
    /// <para><b>ĐẢO VẾ 01/09/2026 (trước đó test này khoá con số 4 TIẾNG MỖI ĐÊM).</b> Vòng flash nay KHÔNG đòi
    /// nhường khung nữa: nó CHỜ khe nghỉ mà check đơn vốn đã để lại, và trả nốt phần khe còn thừa qua
    /// <c>MocChayLaiCheckDonSauFlash</c>. Cái giá lên check đơn vì thế bằng 0 về mặt LỊCH.</para>
    ///
    /// <para><b>⚠ ĐÂY LÀ QUẢ MÌN TO NHẤT CỦA ĐỢT, và test này là thứ gỡ nó.</b> Khung flash mới là CẢ NGÀY
    /// <c>[0,23)</c>; nếu <c>FlashSale</c> còn nằm trong phép quét của <see cref="KmLichTuDong.KhungCuaBienAn"/>
    /// thì MỌI mốc vòng check đơn đều rơi vào "biên an flash" ⇒ <c>HoanVongCheckDon</c> dời mốc tới 23:00, rồi
    /// 23:00 lại đâm vào biên an khung KM hôm sau… tức <b>check đơn tự dời vòng vô hạn và không bao giờ chạy
    /// nữa</b>. Con số phải là 0, và phải đo đủ 24 giờ.</para>
    ///
    /// <para>Vế ĐỐI CHỨNG ở dưới giữ cho test không thành cái luôn-đúng: hai vòng KM/Combo VẪN đòi nhường
    /// khung, đúng 6 giờ (biên an <c>[1,4)</c> + <c>[5,8)</c>).</para>
    /// </summary>
    [Fact]
    public void BatFlash_ThiCheckDon_KHONG_MatGioNao()
    {
        var cf = Cf(flashBat: true, flashChayCuoi: new DateTime(2026, 8, 28, 21, 30, 0));
        const string may = "may-1:orders";
        var cfChiFlash = cf with { OwnerMachine = may, TuDong = false, ComboBat = false };
        var bayGio = new DateTime(2026, 8, 29, 19, 0, 0);

        var soGioBiDoi = 0;
        for (var gio = 0; gio < 24; gio++)
        {
            var mocDuKien = new DateTime(2026, 8, 29).AddHours(gio).AddMinutes(30);
            var nhuong = KmLichTuDong.NenNhuongKhung(cfChiFlash, may, mocDuKien, bayGio);
            var sau = KmLichTuDong.HoanVongCheckDon(mocDuKien, nhuong);
            if (sau != mocDuKien) { soGioBiDoi++; }
        }

        Assert.Equal(0, soGioBiDoi);

        // Và flash KHÔNG còn là "khung của biên an" ở bất kỳ giờ nào trong ngày.
        for (var gio = 0; gio < 24; gio++)
        {
            var moc = new DateTime(2026, 8, 29).AddHours(gio).AddMinutes(30);
            Assert.NotEqual(LoaiChuongTrinh.FlashSale, KmLichTuDong.KhungCuaBienAn(moc));
        }

        // ── VẾ ĐỐI CHỨNG: hai vòng kia VẪN đòi nhường — 4 giờ ([0,4) cho khung 1-4h có biên an).
        // "Bây giờ" phải đứng TRƯỚC hai khung đêm ấy, kẻo `MocChayKe` trả sang ngày hôm sau và vế thứ ba của
        // `NenNhuongKhung` ("đêm nay THỰC SỰ tới hạn") tắt hết — lúc đó test đo nhầm sang chuyện khác.
        var truocKhungDem = new DateTime(2026, 8, 29, 0, 30, 0);
        var cfKmCombo = cf with { OwnerMachine = may, TuDong = true, ComboBat = true, FlashBat = false, SoNgay = 1 };
        var soGioKmCombo = 0;
        for (var gio = 0; gio < 24; gio++)
        {
            var mocDuKien = new DateTime(2026, 8, 29).AddHours(gio).AddMinutes(30);
            if (KmLichTuDong.NenNhuongKhung(cfKmCombo, may, mocDuKien, truocKhungDem)) { soGioKmCombo++; }
        }
        Assert.Equal(4, soGioKmCombo);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 6. KHOẢNG "CÒN TRONG KHUNG MÀ CHẮC CHẮN 0 SHOP" — [22:15, 23:00)
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Sau 22:15 thì nhịp KHÔNG được mở gói nữa, dù <c>TrongKhung</c> vẫn <c>true</c>.</b>
    ///
    /// <para>Hai vòng kia có hạn chót TRÙNG giờ đóng khung nên khoảng này rỗng. Flash thì hạn là <b>22:15</b>
    /// còn khung đóng lúc <b>23:00</b> ⇒ có đúng 45 phút mà một cửa thiếu vế hạn chót vẫn cho chạy.</para>
    ///
    /// <para><b>Chi phí THẬT, kết quả chắc chắn BẰNG 0</b> (phản biện 29/08/2026, giữ nguyên lý do khi hạ mốc
    /// xuống 22:15): app khởi động lúc 22:20 ⇒ nhịp mở gói ⇒ <c>xinKhungXuyenMay: true</c> ⇒ <b>hub DỪNG check
    /// đơn ở MỌI máy</b> ⇒ mở Brave + SSO (~2–3 phút) ⇒ tới vòng shop thì <c>ConDuocBatDauShopMoi</c> false ngay
    /// ở <c>i = 0</c> ⇒ <b>0 shop</b>. Phải chặn ở TẦNG NHỊP, trước khi đụng tới hub.</para>
    /// </summary>
    [Theory]
    [InlineData(22, 14, true)]    // sát hạn — VẪN được
    [InlineData(22, 15, false)]   // đúng mốc hạn ⇒ THÔI
    [InlineData(22, 30, false)]   // ca thật: app khởi động muộn
    [InlineData(22, 59, false)]
    public void SauHanChot_ThiKhongMoGoiNua_DuVanTrongKhung(int gio, int phut, bool mongDoi)
    {
        var bayGio = new DateTime(2026, 8, 29, gio, phut, 0);
        var cf = Cf(flashBat: true);

        // Vẫn TRONG khung — tức cửa duy nhất phân biệt được hai ca là hạn chót.
        Assert.True(KmLichTuDong.TrongKhung(bayGio, LoaiChuongTrinh.FlashSale));
        Assert.Equal(mongDoi, KmLichTuDong.DenGioGoiFlash(cf, bayGio));
    }

    /// <summary>
    /// Vế ĐỐI CHỨNG: cửa hạn chót KHÔNG được lan sang hai vòng kia — hạn của chúng TRÙNG giờ đóng khung, nên
    /// thêm cửa cho chúng là cắt oan mất phút cuối khung.
    /// </summary>
    [Fact]
    public void CuaHanChot_ChiApChoVongFlash()
    {
        var cf = new KmConfig(1, TuDong: true, SoNgay: 3, PhanTram: 5, GioBatDau: "09:30",
                              GioKetThuc: "10:30", LanChayCuoi: null) { ComboBat = true };

        // 03:59 — phút cuối khung KM và Combo, hạn chót cũng là 04:00 ⇒ vẫn phải chạy.
        Assert.True(KmLichTuDong.DenGioChay(cf, new DateTime(2026, 8, 29, 3, 59, 0),
            LoaiChuongTrinh.ChuongTrinhShop));
        Assert.True(KmLichTuDong.DenGioChay(cf, new DateTime(2026, 8, 29, 3, 59, 0), LoaiChuongTrinh.Combo));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 7. CỘT "Flash Sale" TRÊN LƯỚI RA MỘT CON SỐ
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Tiêu chí plan §5: cột "Flash Sale" ở lưới tab Shops hiện được SỐ — hết cảnh luôn <c>—</c>.</b>
    ///
    /// <para>Mọi lưới hiện có của cột này chỉ đo ca <c>null</c> ⇒ <c>"—"</c> (hồi luồng chưa làm), tức chúng
    /// vẫn xanh kể cả khi cột không bao giờ nhận nổi một con số. Test này đi TRỌN đường: ghi số qua
    /// <c>ResultsRepository</c> (đúng đường mà <c>onShopChuongTrinh</c> của vòng chạy dùng) → đọc lại → dựng
    /// dòng lưới → đọc chuỗi hiện ra.</para>
    ///
    /// <para>Con số của Flash là TỔNG sản phẩm đã nạp qua các khung của đêm đó (mỗi khung ~10 sp) nên nó lớn
    /// hơn hẳn hai cột kia — đó là bình thường, và test dùng đúng một con số như vậy (80 = 8 khung × 10).</para>
    /// </summary>
    [Fact]
    public void CotFlashSale_RaCONSO_KhongConLuonLaGachNgang()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        const string homNay = "2026-08-30";
        const string login = "shop.mot";

        // Đúng đường mà vòng chạy dùng (`onShopChuongTrinh` → `CongSoSanPhamChuongTrinh`).
        services.Results.CongSoSanPhamChuongTrinh(7, login, homNay, LoaiChuongTrinh.FlashSale, 80);

        var map = services.Results.GetChuongTrinhByDay(7, homNay);
        Assert.Equal(80, map[(login, LoaiChuongTrinh.FlashSale)]);

        // …và dòng lưới hiện ra ĐÚNG con số đó.
        var dong = new ShopPrepareRow("Shop Một", login, 0,
            kmSoSanPham: null, comboSoSanPham: null,
            flashSoSanPham: map[(login, LoaiChuongTrinh.FlashSale)]);
        Assert.Equal("80", dong.FlashHienThi);

        // VẾ ĐỐI CHỨNG — `—` vẫn giữ đúng nghĩa cũ: hôm đó KHÔNG chạy loại này. Thiếu vế này thì một bản
        // "luôn in số" cũng qua được.
        Assert.Equal("—", new ShopPrepareRow("Shop Một", login, 0).FlashHienThi);
        // Và `0` KHÁC `—`: đã chạy mà không tạo được sp nào.
        Assert.Equal("0", new ShopPrepareRow("Shop Một", login, 0, flashSoSanPham: 0).FlashHienThi);
    }
}
