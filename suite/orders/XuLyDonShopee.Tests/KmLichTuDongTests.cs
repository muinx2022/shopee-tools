using System;
using System.Collections.Generic;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// PHA 4 "KM của Shop" — <b>lịch chạy tự động 2–6h sáng, mỗi xx ngày</b>.
/// <para><b>Vì sao bộ test này là thứ DUY NHẤT kiểm được luật:</b> muốn thử bằng tay thì phải thức tới 2h sáng
/// và chờ đúng ngày thứ xx. Lệch khung giờ một tiếng thì hoặc chương trình KM không bao giờ chạy, hoặc nó nổ
/// vào giữa ban ngày và cướp hồ sơ Brave của vòng check đơn đang chạy — cả hai đều là lỗi IM LẶNG, chỉ lộ ra
/// sau nhiều ngày.</para>
/// </summary>
public class KmLichTuDongTests
{
    private static KmConfig Cf(bool tuDong, int soNgay, DateTime? lanCuoi, DateTime? lanThu = null)
        => new(1, tuDong, soNgay, 5, "09:30", "10:30", lanCuoi, lanThu);

    // ── KHOẢNG của chương trình: hôm nay → hôm nay + 180 (26/08/2026) ───────────────────────────────

    /// <summary>
    /// <b>Hai mốc của khoảng: bắt đầu = HÔM NAY, kết thúc = hôm nay + 180 — và cả hai BỎ phần giờ.</b>
    /// <para>Bỏ giờ là phần dễ quên nhất: <c>ngayDayDu</c> gửi xuống extension là chuỗi <c>dd-MM-yyyy</c>, còn
    /// giờ đi riêng bằng trường <c>gio</c>. Giữ lại phần giờ của <c>bayGio</c> ở đây thì mọi phép so mốc phía
    /// trên (<c>KiemTraMocTuongLai</c>) lệch đi vài tiếng mà không ai thấy.</para>
    /// <para><b>Ca của plan §5</b>: <c>26-08-2026 13:28</c> ⇒ bắt đầu <c>26-08-2026</c>, kết thúc
    /// <c>22-02-2027</c>.</para>
    /// </summary>
    [Fact]
    public void HaiMocKhoang_LaHomNay_VaHomNayCong180_VaBoPhanGio()
    {
        Assert.Equal(180, KmLichTuDong.SoNgayDatTruoc);

        // Ca thật của plan: bấm tay lúc 13:28 ngày 26/08/2026.
        var bamTay = new DateTime(2026, 8, 26, 13, 28, 0);
        Assert.Equal(new DateTime(2026, 8, 26), KmLichTuDong.NgayBatDau(bamTay));
        Assert.Equal(new DateTime(2027, 2, 22), KmLichTuDong.NgayKetThuc(bamTay));

        // Lượt tự động điển hình: 02:05 đêm 19/08/2026.
        var tuDong = new DateTime(2026, 8, 19, 2, 5, 0);
        Assert.Equal(new DateTime(2026, 8, 19), KmLichTuDong.NgayBatDau(tuDong));
        Assert.Equal(new DateTime(2027, 2, 15), KmLichTuDong.NgayKetThuc(tuDong));

        // Phần giờ KHÔNG được sót lại ở CẢ HAI mốc.
        var cuoiNgay = new DateTime(2026, 8, 19, 23, 59, 59);
        Assert.Equal(TimeSpan.Zero, KmLichTuDong.NgayBatDau(cuoiNgay).TimeOfDay);
        Assert.Equal(TimeSpan.Zero, KmLichTuDong.NgayKetThuc(cuoiNgay).TimeOfDay);
    }

    /// <summary>
    /// <b>Khoảng luôn dài ĐÚNG 180 ngày lịch</b>, kể cả khi vắt qua năm mới và qua tháng 2 của một năm NHUẬN.
    /// <para>Cộng NGÀY chứ không cộng tháng: 01/01/2027 + 180 = 30/06/2027 (2027 không nhuận), còn
    /// 01/10/2027 + 180 = 29/03/2028 — đi qua đúng ngày 29/02/2028 của năm nhuận. Một bản vá "cộng 6 tháng cho
    /// gọn" sẽ đỏ ở đây.</para>
    /// </summary>
    [Theory]
    [InlineData(2027, 1, 1, 2027, 6, 30)]     // vắt qua nửa năm, năm thường
    [InlineData(2027, 10, 1, 2028, 3, 29)]    // vắt qua năm mới VÀ qua 29/02 của năm nhuận 2028
    [InlineData(2028, 1, 1, 2028, 6, 29)]     // trọn trong năm nhuận ⇒ lệch một ngày so với 2027
    public void KhoangLuonDaiDung180NgayLich(int nBd, int tBd, int dBd, int nKt, int tKt, int dKt)
    {
        var bd = new DateTime(nBd, tBd, dBd, 3, 0, 0);
        Assert.Equal(new DateTime(nKt, tKt, dKt), KmLichTuDong.NgayKetThuc(bd));
        Assert.Equal(180, (KmLichTuDong.NgayKetThuc(bd) - KmLichTuDong.NgayBatDau(bd)).TotalDays);
    }

    /// <summary>
    /// <b>⚠ MỐC BẮT ĐẦU NAY TRÔI QUA TRONG NGÀY — đây là hệ quả nguy hiểm nhất của bản 26/08/2026.</b>
    /// <para>Bản 25/08 đẩy cả chương trình đi 180 ngày nên mọi giờ đều ở tương lai; nay mốc bắt đầu là HÔM NAY
    /// lúc <c>GioBatDau</c>, nên một lượt bấm tay lúc 13:28 với giờ 11:00 gửi xuống Shopee một mốc QUÁ KHỨ ⇒
    /// picker vô hiệu hoá ⇒ bước 4 đỏ. Test này khoá đúng sự thật đó để không ai "sửa" nó bằng cách bỏ cửa chặn
    /// ở <c>KmDialogViewModel.KiemTraMocTuongLai</c>.</para>
    /// <para>Vế còn lại: hai lượt TỰ ĐỘNG vẫn an toàn — KM chạy 02–04h với 09:30, combo 06–08h với 11:00.</para>
    /// </summary>
    [Fact]
    public void MocBatDau_TROI_QUA_TrongNgay_KhacHanBanDayDi180Ngay()
    {
        var bayGio = new DateTime(2026, 8, 26, 13, 28, 0);
        var batDauLuc11 = KmLichTuDong.NgayBatDau(bayGio).Add(new TimeSpan(11, 0, 0));
        Assert.True(batDauLuc11 < bayGio, "mốc 11:00 hôm nay phải nằm ở QUÁ KHỨ lúc 13:28");

        // Hai lượt tự động: mốc bắt đầu vẫn ở tương lai.
        var nhipKm = new DateTime(2026, 8, 26, 2, 5, 0);
        Assert.True(KmLichTuDong.NgayBatDau(nhipKm).Add(new TimeSpan(9, 30, 0)) > nhipKm);
        var nhipCombo = new DateTime(2026, 8, 26, 6, 5, 0);
        Assert.True(KmLichTuDong.NgayBatDau(nhipCombo).Add(new TimeSpan(11, 0, 0)) > nhipCombo);
    }

    // ── Khung Chương Trình Của Shop: 01–04h (người dùng chốt 08/09/2026) ──────────────────────────

    [Theory]
    [InlineData(0, 59, false)]  // 00:59 — chưa tới khung
    [InlineData(1, 0, true)]    // 01:00 — mở khung, thuộc khung
    [InlineData(2, 0, true)]    // 02:00 — thuộc khung
    [InlineData(3, 30, true)]
    [InlineData(3, 59, true)]   // 03:59 — vẫn trong khung
    [InlineData(4, 0, false)]   // 04:00 — ĐÓNG khung, KHÔNG thuộc
    [InlineData(5, 59, false)]
    [InlineData(6, 0, false)]
    [InlineData(12, 0, false)]
    [InlineData(23, 30, false)]
    public void TrongKhung_DungHaiMocBien(int gio, int phut, bool mongDoi)
    {
        Assert.Equal(mongDoi, KmLichTuDong.TrongKhung(new DateTime(2026, 8, 19, gio, phut, 0), LoaiChuongTrinh.ChuongTrinhShop));
    }

    /// <summary>
    /// <b>MA TRẬN ĐỦ 24 GIỜ × 3 LOẠI</b> — bảng khung là thứ quyết định lượt nào nổ lúc nào.
    /// <para>Khung ĐÊM 01:00–04:00 dùng chung cho cả KM và Combo (người dùng chốt 08/09/2026: chạy 3 ngày 1 lần,
    /// cùng khung đêm, không check đơn, không flash sale). Ban ngày từ 04:00 trở đi hoàn toàn tự do cho check đơn.</para>
    /// </summary>
    [Fact]
    public void TrongKhung_MaTranDu24Gio_HaiKhungDemRoiNhau()
    {
        var mong = new Dictionary<LoaiChuongTrinh, (int Mo, int Dong)>
        {
            [LoaiChuongTrinh.ChuongTrinhShop] = (1, 4),
            [LoaiChuongTrinh.Combo] = (1, 4),
            [LoaiChuongTrinh.FlashSale] = (0, 23),
        };

        for (var gio = 0; gio < 24; gio++)
        {
            var moc = new DateTime(2026, 8, 19, gio, 30, 0);
            foreach (var (loai, khung) in mong)
            {
                var trong = gio >= khung.Mo && gio < khung.Dong;
                Assert.Equal(trong, KmLichTuDong.TrongKhung(moc, loai));
                Assert.Equal(khung, KmLichTuDong.Khung(loai));
            }
        }
    }

    /// <summary>Các mốc người dùng chốt tận tay — viết thẳng ra để đọc plan là đối chiếu được.</summary>
    [Fact]
    public void TrongKhung_BonMocNguoiDungChot()
    {
        Assert.True(KmLichTuDong.TrongKhung(new DateTime(2026, 8, 19, 2, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.False(KmLichTuDong.TrongKhung(new DateTime(2026, 8, 19, 5, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.True(KmLichTuDong.TrongKhung(new DateTime(2026, 8, 19, 2, 0, 0), LoaiChuongTrinh.Combo));
        Assert.False(KmLichTuDong.TrongKhung(new DateTime(2026, 8, 19, 7, 0, 0), LoaiChuongTrinh.Combo));
        // Flash Sale nay là khung CẢ NGÀY [0,23) (chốt 01/09/2026): lượt đặt chương trình cho NGÀY HÔM SAU nên
        // mọi giờ trong ngày đều hợp lệ — giả định cũ "ban ngày Shopee chưa mở khung" đã bị bằng chứng bác bỏ.
        Assert.True(KmLichTuDong.TrongKhung(new DateTime(2026, 8, 19, 0, 0, 0), LoaiChuongTrinh.FlashSale));
        Assert.True(KmLichTuDong.TrongKhung(new DateTime(2026, 8, 19, 11, 0, 0), LoaiChuongTrinh.FlashSale));
        Assert.True(KmLichTuDong.TrongKhung(new DateTime(2026, 8, 19, 22, 59, 0), LoaiChuongTrinh.FlashSale));
        // 23:00 là mốc ĐÓNG — KHÔNG thuộc khung (shop cuối bắt đầu ≤22:15 + trần 45' ⇒ xong quanh 23:00).
        Assert.False(KmLichTuDong.TrongKhung(new DateTime(2026, 8, 19, 23, 0, 0), LoaiChuongTrinh.FlashSale));
    }

    // ── Đến giờ chạy chưa ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DenGioChay_TatTuDong_ThiKHONG_chay_du_dung_khung()
    {
        var bayGio = new DateTime(2026, 8, 19, 3, 0, 0);
        Assert.False(KmLichTuDong.DenGioChay(Cf(tuDong: false, 3, null), bayGio, LoaiChuongTrinh.ChuongTrinhShop));
    }

    [Fact]
    public void DenGioChay_NgoaiKhung_ThiKHONG_chay_du_qua_han_lau()
    {
        // Quá hạn 100 ngày vẫn KHÔNG được nổ lúc 10h sáng: vòng 12 shop sẽ cướp hồ sơ Brave của check đơn.
        var quaHanLau = Cf(true, 3, new DateTime(2026, 5, 1));
        Assert.False(KmLichTuDong.DenGioChay(quaHanLau, new DateTime(2026, 8, 19, 10, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.False(KmLichTuDong.DenGioChay(quaHanLau, new DateTime(2026, 8, 19, 0, 59, 0), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.False(KmLichTuDong.DenGioChay(quaHanLau, new DateTime(2026, 8, 19, 4, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.False(KmLichTuDong.DenGioChay(quaHanLau, new DateTime(2026, 8, 19, 5, 59, 0), LoaiChuongTrinh.ChuongTrinhShop));
        // ...nhưng trong khung thì chạy.
        Assert.True(KmLichTuDong.DenGioChay(quaHanLau, new DateTime(2026, 8, 19, 1, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
    }

    [Fact]
    public void DenGioChay_ChuaChayLanNao_ThiChayNgayKhungDauTien()
    {
        Assert.True(KmLichTuDong.DenGioChay(Cf(true, 3, null), new DateTime(2026, 8, 19, 2, 5, 0), LoaiChuongTrinh.ChuongTrinhShop));
    }

    [Fact]
    public void DenGioChay_ChuaDuSoNgay_ThiKHONG_chay()
    {
        // Chạy lần cuối 18/08, chu kỳ 3 ngày ⇒ sớm nhất 21/08 02:00. Rạng sáng 20/08 CHƯA tới.
        var cf = Cf(true, 3, new DateTime(2026, 8, 18, 2, 30, 0));
        Assert.False(KmLichTuDong.DenGioChay(cf, new DateTime(2026, 8, 20, 3, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.True(KmLichTuDong.DenGioChay(cf, new DateTime(2026, 8, 21, 2, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
    }

    [Fact]
    public void DenGioChay_SoNgay0Hoac_Am_ThiVanCoit_la_1_ngay()
    {
        // Cấu hình rác (0 ngày) KHÔNG được biến thành "chạy lại mỗi nhịp 60 giây" — đó là 12 shop mỗi phút.
        var cf = Cf(true, 0, new DateTime(2026, 8, 19, 2, 30, 0));
        Assert.False(KmLichTuDong.DenGioChay(cf, new DateTime(2026, 8, 19, 3, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.True(KmLichTuDong.DenGioChay(cf, new DateTime(2026, 8, 20, 2, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
    }

    // ── MỘT lượt thử mỗi khung 2–6h (mốc LanThuTuDong) ────────────────────────────────────────────────

    /// <summary>
    /// <b>Lượt tự động ĐÃ THỬ trong khung này thì KHÔNG được thử lại — kể cả khi nó HỎNG.</b>
    /// <para>Đây là ca mà bản trước sai và không test nào thấy: <c>LanChayCuoi</c> chỉ ghi khi TẠO ĐƯỢC, nên
    /// một lượt hỏng (captcha / khoá xuyên máy / lỗi shop đầu) không để lại dấu vết, và nhịp đồng hồ 60 giây
    /// bắn lượt mới MỖI PHÚT cho tới 6h sáng — hàng chục lượt SSO một đêm, đúng thứ làm Shopee bật verify.</para>
    /// </summary>
    [Fact]
    public void DenGioChay_DaThuTrongKhungNay_ThiKHONG_thu_lai_du_luot_do_HONG()
    {
        // 02:05 đã thử một lượt (hỏng ⇒ LanChayCuoi vẫn null). 02:06, 03:00, 05:59 đều KHÔNG được thử nữa.
        var daThu = Cf(true, 3, lanCuoi: null, lanThu: new DateTime(2026, 8, 19, 2, 5, 0));
        Assert.False(KmLichTuDong.DenGioChay(daThu, new DateTime(2026, 8, 19, 2, 6, 0), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.False(KmLichTuDong.DenGioChay(daThu, new DateTime(2026, 8, 19, 3, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.False(KmLichTuDong.DenGioChay(daThu, new DateTime(2026, 8, 19, 3, 59, 0), LoaiChuongTrinh.ChuongTrinhShop));

        // Chặn TEST RỖNG: cùng cấu hình mà CHƯA thử lượt nào thì đúng những mốc đó phải chạy.
        var chuaThu = Cf(true, 3, lanCuoi: null);
        Assert.True(KmLichTuDong.DenGioChay(chuaThu, new DateTime(2026, 8, 19, 2, 6, 0), LoaiChuongTrinh.ChuongTrinhShop));
    }

    [Fact]
    public void DenGioChay_MocThuCuaDEM_TRUOC_ThiKHONG_chan_dem_nay()
    {
        // Thử hỏng đêm 19/08 ⇒ đêm 20/08 vẫn phải chạy. Chặn cả đời thì tính năng chết im.
        var cf = Cf(true, 1, lanCuoi: null, lanThu: new DateTime(2026, 8, 19, 2, 5, 0));
        Assert.True(KmLichTuDong.DenGioChay(cf, new DateTime(2026, 8, 20, 2, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
    }

    [Fact]
    public void DaThuTrongKhungNay_ChiTinhMocNAM_TRONG_khung()
    {
        // Mốc 23:00 hôm qua (ngoài khung) không phải "lượt thử của khung này" — dù cùng ngày lịch với 23:xx.
        var cf = Cf(true, 1, lanCuoi: null, lanThu: new DateTime(2026, 8, 19, 23, 0, 0));
        Assert.False(KmLichTuDong.DaThuTrongKhungNay(cf, new DateTime(2026, 8, 19, 3, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.False(KmLichTuDong.DaThuTrongKhungNay(Cf(true, 1, null), new DateTime(2026, 8, 19, 3, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.True(KmLichTuDong.DaThuTrongKhungNay(
            Cf(true, 1, null, new DateTime(2026, 8, 19, 2, 30, 0)), new DateTime(2026, 8, 19, 3, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
    }

    /// <summary>
    /// <b>NGỮ NGHĨA "NGÀY LỊCH" của <c>LanChayCuoi</c> — cố ý, và test này là chỗ khoá nó lại.</b>
    /// <para>Chạy tay lúc 23:50 ngày D với <c>SoNgay = 1</c> ⇒ lượt tự động rơi vào 02:00 ngày D+1, tức chỉ
    /// 10 phút sau. Trông như lỗi nhưng ĐÚNG với cách người dùng nói ("sau xx NGÀY"); giữ phần giờ thì mốc sẽ
    /// trôi dần mỗi chu kỳ và cuối cùng rơi ra ngoài khung 2–6h. Ai định "sửa" chỗ này sẽ làm test đỏ và phải
    /// đọc lý do trước.</para>
    /// </summary>
    [Fact]
    public void DenGioChay_ChayTay2350_SoNgay1_ThiLuotTuDongRoiVao_0200_NGAY_KE()
    {
        var cf = Cf(true, 1, new DateTime(2026, 8, 19, 23, 50, 0));
        Assert.True(KmLichTuDong.DenGioChay(cf, new DateTime(2026, 8, 20, 2, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
    }

    // ── Mốc kế (để hiển thị) ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MocChayKe_TinhTuLanCuoi_VaKeoVeDauKhung()
    {
        var moc = KmLichTuDong.MocChayKe(Cf(true, 3, new DateTime(2026, 8, 18, 14, 45, 0)),
                                         new DateTime(2026, 8, 18, 15, 0, 0), LoaiChuongTrinh.ChuongTrinhShop);
        Assert.Equal(new DateTime(2026, 8, 21, 1, 0, 0), moc);
    }

    [Fact]
    public void MocChayKe_ChuaChayLanNao_ThiLaKhung1hGanNhat()
    {
        // 15h hôm nay ⇒ 01:00 NGÀY MAI.
        Assert.Equal(new DateTime(2026, 8, 20, 1, 0, 0),
            KmLichTuDong.MocChayKe(Cf(true, 3, null), new DateTime(2026, 8, 19, 15, 0, 0), LoaiChuongTrinh.ChuongTrinhShop));
        // 00:30 hôm nay ⇒ 01:00 CHÍNH HÔM NAY (chưa qua khung).
        Assert.Equal(new DateTime(2026, 8, 19, 1, 0, 0),
            KmLichTuDong.MocChayKe(Cf(true, 3, null), new DateTime(2026, 8, 19, 0, 30, 0), LoaiChuongTrinh.ChuongTrinhShop));
    }

    [Fact]
    public void MocChayKe_MocDaTroiQua_ThiKHONG_don_nhieu_luot_bu()
    {
        // App tắt 2 tuần: chỉ chạy MỘT lượt ở khung gần nhất, không bù 4 lượt đã lỡ.
        var moc = KmLichTuDong.MocChayKe(Cf(true, 3, new DateTime(2026, 8, 1)),
                                         new DateTime(2026, 8, 19, 15, 0, 0), LoaiChuongTrinh.ChuongTrinhShop);
        Assert.Equal(new DateTime(2026, 8, 20, 1, 0, 0), moc);
    }

    // ── Nhường khung cho KM: vòng check đơn ───────────────────────────────────────────────────────────

    [Fact]
    public void HoanVongCheckDon_MocRoiVaoKhung_ThiDoiToi_GioDongCuaDUNGKhungDo()
    {
        // 03:20 đâm vào khung Chương Trình Của Shop ⇒ dời tới 04:00 (giờ ĐÓNG của khung ĐÓ).
        Assert.Equal(new DateTime(2026, 8, 19, 4, 0, 0),
            KmLichTuDong.HoanVongCheckDon(new DateTime(2026, 8, 19, 3, 20, 0), nhuongKhung: true));
    }

    /// <summary>
    /// <b>Khung đêm 01:00–04:00 (biên an [00:00, 04:00))</b>: các mốc rơi vào biên an đều dời tới 04:00.
    /// Ngoài biên an (04:30, 08:30, 13:00) GIỮ NGUYÊN.
    /// </summary>
    [Theory]
    [InlineData(0, 30, 4)]
    [InlineData(1, 30, 4)]
    [InlineData(3, 59, 4)]
    public void HoanVongCheckDon_MoiKhungDoiToiGioDongCuaChinhNo(int gio, int phut, int gioMongDoi)
    {
        var moc = new DateTime(2026, 8, 19, gio, phut, 0);
        Assert.Equal(new DateTime(2026, 8, 19).AddHours(gioMongDoi),
            KmLichTuDong.HoanVongCheckDon(moc, nhuongKhung: true));
    }

    /// <summary>
    /// <b>Mốc ngoài khung đêm KHÔNG bị dời.</b>
    /// </summary>
    [Theory]
    [InlineData(20, 30)]
    [InlineData(21, 0)]
    [InlineData(23, 59)]
    [InlineData(12, 0)]
    [InlineData(4, 30)]
    public void HoanVongCheckDon_GioNgoaiHaiKhungDem_GiuNguyen(int gio, int phut)
    {
        var moc = new DateTime(2026, 8, 19, gio, phut, 0);
        Assert.Null(KmLichTuDong.KhungCuaBienAn(moc));
        Assert.Equal(moc, KmLichTuDong.HoanVongCheckDon(moc, nhuongKhung: true));
    }

    [Theory]
    [InlineData(4, 30)]
    [InlineData(8, 30)]
    [InlineData(13, 0)]
    [InlineData(20, 30)]
    public void HoanVongCheckDon_KheNGHI_ThiGiuNguyen(int gio, int phut)
    {
        var moc = new DateTime(2026, 8, 19, gio, phut, 0);
        Assert.Equal(moc, KmLichTuDong.HoanVongCheckDon(moc, nhuongKhung: true));
    }

    /// <summary>
    /// <b>BIÊN AN 60 PHÚT: mốc trong <c>[00:00, 01:00)</c> cũng phải bị dời sang 04:00.</b>
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 30)]
    [InlineData(0, 59)]
    public void HoanVongCheckDon_MocTrongBIEN_AN_60phut_ThiCungDoiToi_GioDongKhung(int gio, int phut)
    {
        var moc = new DateTime(2026, 8, 19, gio, phut, 0);
        Assert.Equal(new DateTime(2026, 8, 19, 4, 0, 0), KmLichTuDong.HoanVongCheckDon(moc, nhuongKhung: true));
    }

    /// <summary>Biên TRÊN của biên an khung Shop: 03:59 vẫn nằm trong khung ⇒ dời tới 04:00 (mốc đóng khung).</summary>
    [Fact]
    public void HoanVongCheckDon_Moc0359_ThiDoiToi_0400()
    {
        var moc = new DateTime(2026, 8, 19, 3, 59, 0);
        Assert.Equal(new DateTime(2026, 8, 19, 4, 0, 0), KmLichTuDong.HoanVongCheckDon(moc, nhuongKhung: true));
    }

    [Fact]
    public void HoanVongCheckDon_Moc2359_VanGIU_NGUYEN()
    {
        // 23:59 đêm trước nằm ngoài biên an [00:00, 04:00) ⇒ giữ nguyên.
        var moc = new DateTime(2026, 8, 19, 23, 59, 0);
        Assert.Equal(moc, KmLichTuDong.HoanVongCheckDon(moc, nhuongKhung: true));
    }

    [Fact]
    public void HoanVongCheckDon_NgoaiBienAn_ThiGIU_NGUYEN()
    {
        foreach (var gio in new[] { 4, 8, 13, 19, 23 })
        {
            var moc = new DateTime(2026, 8, 19, gio, 30, 0);
            Assert.Equal(moc, KmLichTuDong.HoanVongCheckDon(moc, nhuongKhung: true));
        }
    }

    [Fact]
    public void TrongKhung_KHONG_bi_keo_theo_bien_an()
    {
        // Khung CHẠY KM vẫn là [01:00, 04:00): biên an [00:00, 01:00) chỉ áp cho việc HOÃN vòng check đơn.
        Assert.False(KmLichTuDong.TrongKhung(new DateTime(2026, 8, 19, 0, 30, 0), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.True(KmLichTuDong.TrongBienAn(new DateTime(2026, 8, 19, 0, 30, 0)));
        Assert.Equal(LoaiChuongTrinh.ChuongTrinhShop, KmLichTuDong.KhungCuaBienAn(new DateTime(2026, 8, 19, 0, 30, 0)));
    }

    [Fact]
    public void HoanVongCheckDon_TatKmTuDong_ThiKHONG_chan_gi_ca()
    {
        // Tài khoản không chạy KM mà vẫn bị cấm khung 1–4h là tự cắt check đơn không vì cái gì.
        var moc = new DateTime(2026, 8, 19, 3, 20, 0);
        Assert.Equal(moc, KmLichTuDong.HoanVongCheckDon(moc, nhuongKhung: false));
    }

    // ── HẠN CHÓT 04:00 của lượt tự động ───────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Hạn chót của lượt tự động là 04:00 CỦA CHÍNH NGÀY lượt đó khởi động (cả Shop và Combo).</b>
    /// </summary>
    [Fact]
    public void HanChotLuotTuDong_LaGioDongKhungCuaTUNGLOAI_CungNgay()
    {
        // Chương Trình Của Shop: 04:00. Combo: 04:00. Flash Sale: 22:15.
        Assert.Equal(new DateTime(2026, 8, 19, 4, 0, 0),
            KmLichTuDong.HanChotLuotTuDong(new DateTime(2026, 8, 19, 1, 5, 0), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.Equal(new DateTime(2026, 8, 19, 4, 0, 0),
            KmLichTuDong.HanChotLuotTuDong(new DateTime(2026, 8, 19, 3, 59, 0), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.Equal(new DateTime(2026, 8, 19, 4, 0, 0),
            KmLichTuDong.HanChotLuotTuDong(new DateTime(2026, 8, 19, 1, 5, 0), LoaiChuongTrinh.Combo));
        // ⚠ FLASH SALE LÀ NGOẠI LỆ — 22:15, KHÔNG phải giờ đóng khung (23:00).
        Assert.Equal(new DateTime(2026, 8, 19, 22, 15, 0),
            KmLichTuDong.HanChotLuotTuDong(new DateTime(2026, 8, 19, 10, 5, 0), LoaiChuongTrinh.FlashSale));
        Assert.Equal(new DateTime(2026, 8, 19, 22, 15, 0),
            KmLichTuDong.HanChotLuotTuDong(new DateTime(2026, 8, 19, 22, 14, 0), LoaiChuongTrinh.FlashSale));
    }

    /// <summary>
    /// <b>Ma trận "còn được bắt đầu shop mới không".</b> Mốc 04:00 CHÍNH NÓ đã là hết khung (đóng khung).
    /// </summary>
    [Theory]
    [InlineData(1, 5, true)]    // đầu khung
    [InlineData(3, 59, true)]   // sát hạn — VẪN được bắt đầu shop mới
    [InlineData(4, 0, false)]   // ĐÚNG hạn — thôi
    [InlineData(4, 1, false)]
    public void ConDuocBatDauShopMoi_MaTran(int gio, int phut, bool mongDoi)
    {
        var hanChot = new DateTime(2026, 8, 19, 4, 0, 0);
        Assert.Equal(mongDoi, KmLichTuDong.ConDuocBatDauShopMoi(new DateTime(2026, 8, 19, gio, phut, 0), hanChot));
    }

    [Fact]
    public void ConDuocBatDauShopMoi_DuongBamTay_LuonTRUE()
    {
        Assert.True(KmLichTuDong.ConDuocBatDauShopMoi(new DateTime(2026, 8, 19, 23, 59, 0), null));
    }

    [Fact]
    public void CauHoanVong_NoiRoGioCu_GioMoi_VaLyDo()
    {
        var cau = KmLichTuDong.CauHoanVong(new DateTime(2026, 8, 19, 3, 20, 0), new DateTime(2026, 8, 19, 4, 0, 0));
        Assert.Contains("03:20", cau);
        Assert.Contains("04:00", cau);
        Assert.Contains("KM", cau);
        Assert.Contains("Chương Trình Của Shop", cau);

        // Mốc của khung COMBO phải nói TÊN KHUNG ĐÓ — nói nhầm tên là người đọc đi tìm sai luồng.
        var cauCombo = KmLichTuDong.CauHoanVong(new DateTime(2026, 8, 19, 2, 30, 0), new DateTime(2026, 8, 19, 4, 0, 0), LoaiChuongTrinh.Combo);
        Assert.Contains("Combo Khuyến Mãi", cauCombo);
        Assert.Contains("1–4h", cauCombo);
    }
}
