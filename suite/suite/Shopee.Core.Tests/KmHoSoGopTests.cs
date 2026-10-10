using Shopee.Core.Coordination;

namespace Shopee.Core.Tests;

/// <summary>
/// LUẬT GỘP hồ sơ KM + quy ước múi giờ (<see cref="KmHoSoGop"/>, <see cref="KmMocThoiGian"/>) — hàm thuần dùng
/// CHUNG cho hub và client.
/// <para><b>Vì sao bộ test này quan trọng hơn vẻ ngoài của nó:</b> cả hai chế độ hỏng của luật gộp đều IM LẶNG.
/// Gộp sai nhóm cấu hình ⇒ cấu hình người dùng vừa sửa bị bản cũ trên hub nuốt (họ chỉ thấy "form tự quay về số
/// cũ"). Gộp sai nhóm mốc chạy ⇒ một máy có mốc cũ đẩy lùi <c>LanChayCuoi</c> và đêm sau máy khác chạy lại đúng
/// lượt KM vừa chạy — không lỗi, không log, chỉ có shop nhận hai chương trình khuyến mãi.</para>
/// </summary>
public class KmHoSoGopTests
{
    private const string Login = "sub@shopee.vn";

    private static readonly DateTimeOffset Cu = new(2026, 8, 20, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Moi = new(2026, 8, 21, 3, 0, 0, TimeSpan.Zero);

    /// <summary>Mốc "bây giờ" mặc định của bộ test: SAU cả <see cref="Cu"/> lẫn <see cref="Moi"/>, nên không ca
    /// nào ở đây chạm trần mốc tương lai (<see cref="KmHoSoGop.BienMocTuongLai"/>) — trần có bộ test riêng.</summary>
    private static readonly DateTimeOffset BayGio = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    private static KmProfileItem HoSo(
        DateTimeOffset? cfgUpdatedAt, bool tuDong = false, int soNgay = 7, int phanTram = 5,
        string gioBatDau = "09:30", string ownerMachine = "",
        DateTimeOffset? lanChayCuoi = null, DateTimeOffset? lanThuTuDong = null)
        => new(Login)
        {
            TuDong = tuDong,
            SoNgay = soNgay,
            PhanTram = phanTram,
            GioBatDau = gioBatDau,
            GioKetThuc = "10:30",
            OwnerMachine = ownerMachine,
            CfgUpdatedAt = cfgUpdatedAt,
            LanChayCuoi = lanChayCuoi,
            LanThuTuDong = lanThuTuDong,
        };

    /// <summary>Hồ sơ có phần COMBO — 7 trường cấu hình + 2 mốc chạy, thêm 25/08/2026.</summary>
    private static KmProfileItem HoSoCombo(
        DateTimeOffset? cfgUpdatedAt, bool comboBat = false, string comboGioBatDau = "11:00",
        int comboGiam = 2000, int comboMuaSl = 2, int comboGioiHan = 3, int comboLoai = 3,
        DateTimeOffset? comboLanChayCuoi = null, DateTimeOffset? comboLanThuTuDong = null)
        => HoSo(cfgUpdatedAt) with
        {
            ComboBat = comboBat,
            ComboGioBatDau = comboGioBatDau,
            ComboGioKetThuc = "12:00",
            ComboLoai = comboLoai,
            ComboMuaSl = comboMuaSl,
            ComboGiam = comboGiam,
            ComboGioiHan = comboGioiHan,
            ComboLanChayCuoi = comboLanChayCuoi,
            ComboLanThuTuDong = comboLanThuTuDong,
        };

    // ── COMBO KHUYẾN MÃI: 7 trường cấu hình theo `cfg_updated_at`, 2 mốc theo MAX ────────────────────

    /// <summary>7 trường cấu hình combo đi theo ĐÚNG BẢN THẮNG của <c>CfgUpdatedAt</c> — không đẻ luật thứ hai.</summary>
    [Fact]
    public void Combo_CauHinh_TheoBanCoMocSuaMoiHon()
    {
        var dangCo = HoSoCombo(Cu, comboBat: false, comboGioBatDau: "11:00", comboGiam: 2000);
        var den = HoSoCombo(Moi, comboBat: true, comboGioBatDau: "07:30", comboGiam: 5000,
                            comboMuaSl: 4, comboGioiHan: 9, comboLoai: 2);

        var gop = KmHoSoGop.Gop(dangCo, den, BayGio);

        Assert.True(gop.ComboBat);
        Assert.Equal("07:30", gop.ComboGioBatDau);
        Assert.Equal(5000, gop.ComboGiam);
        Assert.Equal(4, gop.ComboMuaSl);
        Assert.Equal(9, gop.ComboGioiHan);
        Assert.Equal(2, gop.ComboLoai);

        // Vế đối chứng: bản CŨ HƠN không được ghi đè.
        var nguoc = KmHoSoGop.Gop(den, dangCo, BayGio);
        Assert.True(nguoc.ComboBat);
        Assert.Equal("07:30", nguoc.ComboGioBatDau);
    }

    /// <summary>Hai mốc COMBO gộp theo MAX — mốc mới thắng mốc cũ, và mốc CŨ KHÔNG kéo lùi mốc mới.</summary>
    [Fact]
    public void Combo_HaiMocChay_GopTheoMAX_ChiTien_KhongLui()
    {
        var dangCo = HoSoCombo(Cu, comboLanChayCuoi: Moi, comboLanThuTuDong: Moi);
        var den = HoSoCombo(Cu, comboLanChayCuoi: Cu, comboLanThuTuDong: Cu);

        Assert.Equal(Moi, KmHoSoGop.Gop(dangCo, den, BayGio).ComboLanChayCuoi);
        Assert.Equal(Moi, KmHoSoGop.Gop(den, dangCo, BayGio).ComboLanChayCuoi);
        Assert.Equal(Moi, KmHoSoGop.Gop(dangCo, den, BayGio).ComboLanThuTuDong);
        Assert.Equal(Moi, KmHoSoGop.Gop(den, dangCo, BayGio).ComboLanThuTuDong);

        // null THUA mọi giá trị (đúng luật của hai mốc bên vòng kia).
        Assert.Equal(Cu, KmHoSoGop.Gop(HoSoCombo(Cu), HoSoCombo(Cu, comboLanChayCuoi: Cu), BayGio).ComboLanChayCuoi);
    }

    /// <summary>Mốc combo RÁC (quá xa tương lai — máy sai đồng hồ) bị BỎ ở CẢ HAI phía, y luật của hai mốc kia.
    /// Không lọc thì <c>Max</c> giữ nó vĩnh viễn và vòng combo chết câm trên cả fleet.</summary>
    [Fact]
    public void Combo_MocRacQuaXaTuongLai_ThiBO()
    {
        var rac = BayGio.AddYears(2);
        var gop = KmHoSoGop.Gop(
            HoSoCombo(Cu, comboLanChayCuoi: rac, comboLanThuTuDong: rac),
            HoSoCombo(Cu, comboLanChayCuoi: Cu, comboLanThuTuDong: Cu),
            BayGio);

        Assert.Equal(Cu, gop.ComboLanChayCuoi);
        Assert.Equal(Cu, gop.ComboLanThuTuDong);
    }

    /// <summary>
    /// <b>Hai bản chỉ khác THÔNG SỐ COMBO thì KHÔNG được coi là "y hệt"</b> — thiếu vế này trong
    /// <see cref="KmHoSoGop.CungCauHinh"/> thì lượt phá hoà (cùng mốc sửa) giữ bản đang có, và thay đổi combo
    /// của máy kia biến mất không một dòng log.
    /// </summary>
    [Fact]
    public void Combo_CungCauHinh_PhaiTinhCa7TruongCombo()
    {
        var a = HoSoCombo(Cu, comboBat: false);
        Assert.False(KmHoSoGop.CungCauHinh(a, a with { ComboBat = true }));
        Assert.False(KmHoSoGop.CungCauHinh(a, a with { ComboGioBatDau = "07:00" }));
        Assert.False(KmHoSoGop.CungCauHinh(a, a with { ComboGioKetThuc = "09:00" }));
        Assert.False(KmHoSoGop.CungCauHinh(a, a with { ComboLoai = 2 }));
        Assert.False(KmHoSoGop.CungCauHinh(a, a with { ComboMuaSl = 5 }));
        Assert.False(KmHoSoGop.CungCauHinh(a, a with { ComboGiam = 1 }));
        Assert.False(KmHoSoGop.CungCauHinh(a, a with { ComboGioiHan = 7 }));
        Assert.True(KmHoSoGop.CungCauHinh(a, a with { ComboLanChayCuoi = Moi }));   // MỐC không thuộc cấu hình
        Assert.True(KmHoSoGop.CungCauHinh(a, a));
    }

    // ── Nhóm CẤU HÌNH: mốc sửa mới hơn thắng ────────────────────────────────────

    [Fact]
    public void CauHinh_BanMoiHonThang_MangTronGoiCauHinh()
    {
        var dangCo = HoSo(Cu, tuDong: false, soNgay: 7, phanTram: 5, gioBatDau: "09:30", ownerMachine: "may-a:orders");
        var den = HoSo(Moi, tuDong: true, soNgay: 3, phanTram: 25, gioBatDau: "08:00", ownerMachine: "may-b:orders");

        var gop = KmHoSoGop.Gop(dangCo, den, BayGio);

        Assert.True(gop.TuDong);
        Assert.Equal(3, gop.SoNgay);
        Assert.Equal(25, gop.PhanTram);
        Assert.Equal("08:00", gop.GioBatDau);
        Assert.Equal("may-b:orders", gop.OwnerMachine);
        Assert.Equal(Moi, gop.CfgUpdatedAt);
    }

    /// <summary>Vế đối chứng bắt buộc: bản CŨ HƠN tuyệt đối không được ghi đè — đây chính là ca "máy vừa bật lên
    /// sau mấy hôm tắt đẩy bản của nó lên hub".</summary>
    [Fact]
    public void CauHinh_BanCuHon_KhongGhiDe()
    {
        var dangCo = HoSo(Moi, tuDong: true, soNgay: 3, phanTram: 25, gioBatDau: "08:00", ownerMachine: "may-b:orders");
        var den = HoSo(Cu, tuDong: false, soNgay: 7, phanTram: 5, gioBatDau: "09:30", ownerMachine: "may-a:orders");

        var gop = KmHoSoGop.Gop(dangCo, den, BayGio);

        Assert.True(gop.TuDong);
        Assert.Equal(3, gop.SoNgay);
        Assert.Equal(25, gop.PhanTram);
        Assert.Equal("08:00", gop.GioBatDau);
        Assert.Equal("may-b:orders", gop.OwnerMachine);
        Assert.Equal(Moi, gop.CfgUpdatedAt);
    }

    /// <summary>
    /// Mốc BẰNG NHAU mà cấu hình KHÁC ⇒ phá hoà theo thứ tự cố định, và gộp lần hai ra ĐÚNG một kết quả.
    /// <para><b>ĐỔI LUẬT ở vòng 3 (22/08/2026) — bản trước khẳng định "bằng nhau ⇒ giữ bản đang có" và test này
    /// khoá đúng câu đó.</b> Phản biện chỉ ra hệ quả: lượt backfill đóng dấu CÙNG một mốc cho mọi máy, nên
    /// "giữ bản đang có" nghĩa là bản ĐẨY TRƯỚC thắng ⇒ hub / máy A / máy B ổn định ở BA bản khác nhau, vĩnh
    /// viễn. Nay hoà thì phá theo thứ tự: lịch đang BẬT thắng, rồi <c>OwnerMachine</c> nhỏ hơn thắng — ở đây
    /// hai bên cùng tắt nên <c>may-a</c> thắng, ở CẢ HAI chiều gộp.</para>
    /// <para>Idempotent vẫn là ràng buộc bắt buộc: không thì worker 60 giây một nhịp làm hồ sơ dao động qua
    /// lại giữa hai máy.</para>
    /// </summary>
    [Fact]
    public void CauHinh_MocBangNhau_PhaHoaCoThuTu_VaGopHaiLanRaMotKetQua()
    {
        var dangCo = HoSo(Moi, phanTram: 25, ownerMachine: "may-b:orders");
        var den = HoSo(Moi, phanTram: 5, ownerMachine: "may-a:orders");

        var lan1 = KmHoSoGop.Gop(dangCo, den, BayGio);
        Assert.Equal(5, lan1.PhanTram);
        Assert.Equal("may-a:orders", lan1.OwnerMachine);

        // Đảo chiều ⇒ CÙNG kết quả: đó mới là thứ làm cả fleet hội tụ.
        var daoChieu = KmHoSoGop.Gop(den, dangCo, BayGio);
        Assert.Equal(5, daoChieu.PhanTram);
        Assert.Equal("may-a:orders", daoChieu.OwnerMachine);

        var lan2 = KmHoSoGop.Gop(lan1, den, BayGio);
        Assert.Equal(lan1, lan2);
    }

    [Fact]
    public void CauHinh_ChuaTungSua_ThuaBanCoMoc_CaHaiChieu()
    {
        // Máy chưa từng mở form (null) đẩy lên hub: KHÔNG được xoá cấu hình người khác vừa đặt.
        var giuLai = KmHoSoGop.Gop(HoSo(Moi, phanTram: 25), HoSo(null, phanTram: 5), BayGio);
        Assert.Equal(25, giuLai.PhanTram);

        // Chiều ngược: máy chưa từng mở form kéo hồ sơ hub về ⇒ nhận cấu hình của hub (đây là mảnh vá cho
        // "bật tự động ở máy A mà máy B không biết").
        var nhanVe = KmHoSoGop.Gop(HoSo(null, phanTram: 5), HoSo(Moi, tuDong: true, phanTram: 25), BayGio);
        Assert.True(nhanVe.TuDong);
        Assert.Equal(25, nhanVe.PhanTram);
    }

    [Fact]
    public void CauHinh_CaHaiChuaTungSua_GiuBanDangCo()
    {
        var gop = KmHoSoGop.Gop(HoSo(null, phanTram: 25), HoSo(null, phanTram: 5), BayGio);
        Assert.Equal(25, gop.PhanTram);
        Assert.Null(gop.CfgUpdatedAt);
    }

    // ── Nhóm MỐC CHẠY: MAX, chỉ tiến ────────────────────────────────────────────

    /// <summary>Bản đến CÓ cấu hình mới hơn (nên nó thắng ở nhóm cấu hình) nhưng mốc chạy CŨ HƠN ⇒ mốc vẫn
    /// KHÔNG được lùi. Cố ý cho bản đến thắng nhóm cấu hình: để mốc chạy đi theo bản thắng cấu hình thì test
    /// vẫn xanh dù luật MAX đã bị gỡ — đúng cái bẫy lượt thử phá 22/08 lôi ra.</summary>
    [Fact]
    public void MocChay_ChiTien_MocCuHonKhongDayLui()
    {
        var dangCo = HoSo(Cu, lanChayCuoi: Moi, lanThuTuDong: Moi);
        var den = HoSo(Moi, lanChayCuoi: Cu, lanThuTuDong: Cu);

        var gop = KmHoSoGop.Gop(dangCo, den, BayGio);

        Assert.Equal(Moi, gop.LanChayCuoi);
        Assert.Equal(Moi, gop.LanThuTuDong);
    }

    [Fact]
    public void MocChay_MocMoiHonThiTien()
    {
        var gop = KmHoSoGop.Gop(HoSo(Cu, lanChayCuoi: Cu), HoSo(Cu, lanChayCuoi: Moi), BayGio);
        Assert.Equal(Moi, gop.LanChayCuoi);
    }

    [Theory]
    [InlineData(false, true)]   // dangCo null, den có
    [InlineData(true, false)]   // dangCo có, den null
    public void MocChay_NullThuaMoiGiaTri(bool dangCoCoMoc, bool denCoMoc)
    {
        var gop = KmHoSoGop.Gop(
            HoSo(Cu, lanChayCuoi: dangCoCoMoc ? Moi : null),
            HoSo(Cu, lanChayCuoi: denCoMoc ? Moi : null),
            BayGio);

        Assert.Equal(Moi, gop.LanChayCuoi);
    }

    [Fact]
    public void MocChay_CaHaiNull_ThiNull()
        => Assert.Null(KmHoSoGop.Gop(HoSo(Cu), HoSo(Cu), BayGio).LanChayCuoi);

    /// <summary>
    /// <b>HAI NHÓM, HAI LUẬT — ca chốt.</b> Bản đến có cấu hình CŨ HƠN (thua ở nhóm cấu hình) nhưng mang mốc
    /// chạy MỚI HƠN (thắng ở nhóm mốc). Gộp hai luật làm một thì một trong hai vế sẽ sai, và đây là ca thật:
    /// máy chủ trì chạy KM xong (mốc mới) trong khi người dùng vừa sửa cấu hình ở máy khác.
    /// </summary>
    [Fact]
    public void HaiNhomDocLap_CauHinhCuHonVanGopDuocMocChayMoiHon()
    {
        var dangCo = HoSo(Moi, phanTram: 25, lanChayCuoi: Cu);
        var den = HoSo(Cu, phanTram: 5, lanChayCuoi: Moi);

        var gop = KmHoSoGop.Gop(dangCo, den, BayGio);

        Assert.Equal(25, gop.PhanTram);       // cấu hình: bản đang có (mốc sửa mới hơn) thắng
        Assert.Equal(Moi, gop.LanChayCuoi);   // mốc chạy: MAX, lấy của bản đến
    }

    // ── TRẦN MỐC TƯƠNG LAI (V4): bánh cóc MAX phải có nắp ───────────────────────

    /// <summary>
    /// Mốc chạy xa hơn <see cref="KmHoSoGop.BienMocTuongLai"/> ở tương lai là RÁC (máy sai đồng hồ, chỉnh nhầm
    /// năm) ⇒ BỎ. Không bỏ thì <c>Max</c> giữ nó vĩnh viễn, mọi máy kéo về, <c>DenGioChay</c> trả false tới
    /// tận năm đó và KM chết trên toàn fleet — im lặng, và không gỡ được từ trong app.
    /// </summary>
    [Fact]
    public void MocChay_QuaXaTuongLai_ThiBiBo()
    {
        var xa = BayGio.AddYears(2);

        var gop = KmHoSoGop.Gop(HoSo(Cu, lanChayCuoi: Cu), HoSo(Cu, lanChayCuoi: xa), BayGio);

        Assert.Equal(Cu, gop.LanChayCuoi);   // mốc thật CÒN NGUYÊN, chỉ mốc rác rụng
    }

    /// <summary>Cả <c>LanChayCuoi</c> lẫn <c>LanThuTuDong</c> đều phải có trần — <c>LanThuTuDong</c> rác cũng
    /// khoá cứng <c>DaThuTrongKhungNay</c> theo cùng một kiểu.</summary>
    [Fact]
    public void MocThuTuDong_QuaXaTuongLai_ThiBiBo()
    {
        var xa = BayGio.AddYears(2);

        var gop = KmHoSoGop.Gop(HoSo(Cu), HoSo(Cu, lanThuTuDong: xa), BayGio);

        Assert.Null(gop.LanThuTuDong);
    }

    /// <summary>Vế ĐỐI CHỨNG — mốc tương lai NẰM TRONG biên (hai máy lệch đồng hồ thật vài phút, hoặc một máy
    /// đặt sai timezone vài giờ) vẫn phải nhận. Thiếu vế này thì một bản "bỏ mọi mốc &gt; now" cũng xanh, mà
    /// bản đó vứt mốc thật của máy lệch giờ và mở lại đường chạy KM trùng.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void MocChay_TuongLaiTrongBien_ThiVanNhan(int soGio)
    {
        var gan = BayGio.AddHours(soGio);

        var gop = KmHoSoGop.Gop(HoSo(Cu, lanChayCuoi: Cu), HoSo(Cu, lanChayCuoi: gan), BayGio);

        Assert.Equal(gan, gop.LanChayCuoi);
    }

    /// <summary>
    /// <b>ĐƯỜNG TỰ GỠ.</b> Mốc rác nằm ở phía <c>dangCo</c> (đã ghi vào bảng hub bởi một bản cũ chưa có trần)
    /// cũng phải rụng — đó là lý do luật lọc CẢ HAI phía chứ không riêng bản vừa nhận. Chỉ lọc bản đến thì mốc
    /// rác đã lỡ vào bảng sẽ nằm đó vĩnh viễn và đường gỡ duy nhất là sửa tay database trên VPS.
    /// </summary>
    [Fact]
    public void MocRacOPhiaDangCo_CungBiBo()
    {
        var xa = BayGio.AddYears(2);

        var gop = KmHoSoGop.Gop(HoSo(Cu, lanChayCuoi: xa), HoSo(Cu, lanChayCuoi: Cu), BayGio);

        Assert.Equal(Cu, gop.LanChayCuoi);
    }

    /// <summary>Trần CHỈ áp cho mốc chạy, KHÔNG đụng phần cấu hình: hồ sơ mang mốc rác vẫn giữ nguyên
    /// <c>TuDong</c>/<c>PhanTram</c>… — bỏ nhầm cả cấu hình là biến một lỗi đồng hồ thành mất dữ liệu.</summary>
    [Fact]
    public void MocRac_KhongDungToiPhanCauHinh()
    {
        var gop = KmHoSoGop.Gop(
            HoSo(Cu, phanTram: 5),
            HoSo(Moi, tuDong: true, phanTram: 25, lanChayCuoi: BayGio.AddYears(2)),
            BayGio);

        Assert.True(gop.TuDong);
        Assert.Equal(25, gop.PhanTram);
        Assert.Null(gop.LanChayCuoi);
    }

    [Fact]
    public void Gop_GiuLogin_KhongDeRong()
    {
        var gop = KmHoSoGop.Gop(new KmProfileItem(""), new KmProfileItem(Login), BayGio);
        Assert.Equal(Login, gop.Login);
    }

    // ── MÚI GIỜ: đổi ở đúng hai cửa, đi vòng tròn phải ra đúng mốc ban đầu ───────

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]   // đúng thứ km_config đang lưu
    [InlineData(DateTimeKind.Local)]         // dữ liệu cũ ghi bằng DateTime.Now
    public void DoiMuiGio_DiVongTron_RaDungMocBanDau(DateTimeKind kind)
    {
        var goc = DateTime.SpecifyKind(new DateTime(2026, 8, 22, 2, 5, 30), kind);

        var veMay = KmMocThoiGian.VeMay(KmMocThoiGian.LenDay(goc));

        Assert.Equal(goc, veMay);
        Assert.Equal(DateTimeKind.Unspecified, veMay!.Value.Kind);   // đúng quy ước của km_config
    }

    /// <summary>Mốc nửa đêm trong khung KM 2–6h — ca mà lệch múi giờ làm hỏng ÂM THẦM: lệch 7 tiếng thì mốc kế
    /// rơi ra ngoài khung và KM không bao giờ chạy nữa, không một dòng log nào nói vì sao.</summary>
    [Fact]
    public void DoiMuiGio_MocTrongKhungKm_LenDayLaUtcThat_VeMayVanDungGio()
    {
        var goc = new DateTime(2026, 8, 22, 2, 5, 0, DateTimeKind.Unspecified);

        var day = KmMocThoiGian.LenDay(goc);

        Assert.NotNull(day);
        Assert.Equal(TimeSpan.Zero, day!.Value.Offset);                       // trên dây LUÔN là UTC
        Assert.Equal(goc.ToUniversalTime(), day.Value.UtcDateTime);           // đúng mốc, không phải "gán nhãn UTC"
        Assert.Equal(goc, KmMocThoiGian.VeMay(day));
    }

    // ── TRẦN của MỐC SỬA (W7) + PHÁ HOÀ khi mốc bằng nhau (W8) — phản biện vòng 3, 22/08/2026 ──

    /// <summary>
    /// <b>Mốc SỬA quá xa tương lai bị KẸP VỀ "bây giờ", KHÔNG bỏ về <c>null</c>.</b>
    /// <para>Bỏ về <c>null</c> nghĩa là "thua mọi giá trị", tức <b>vứt trọn cấu hình</b> của bản đó — ngữ nghĩa
    /// khác hẳn mốc CHẠY (ở đó <c>null</c> chỉ là "chưa chạy lần nào"). Không kẹp thì mốc 2028 thắng vĩnh viễn
    /// VÀ tự lan: mọi máy kéo về rồi ghi chính mốc ấy xuống <c>km_config</c>, nên xoá dòng trên hub cũng vô
    /// ích.</para>
    /// </summary>
    [Fact]
    public void MocSuaQuaXaTuongLai_ThiKepVeBayGio_VaGiuNguyenCauHinh()
    {
        var xa = BayGio.AddYears(2);
        var dangCo = HoSo(Cu, phanTram: 5);
        var den = HoSo(xa, tuDong: true, phanTram: 25, gioBatDau: "08:00", ownerMachine: "may-loi:orders");

        var gop = KmHoSoGop.Gop(dangCo, den, BayGio);

        Assert.Equal(BayGio, gop.CfgUpdatedAt);      // KẸP, không phải null
        Assert.Equal(25, gop.PhanTram);              // cấu hình của bản đó KHÔNG bị vứt
        Assert.True(gop.TuDong);
    }

    /// <summary>Mốc rác đã nằm ở phía ĐANG GIỮ (trên hub) cũng bị kẹp — đó là đường TỰ GỠ, không phải sửa tay
    /// database.</summary>
    [Fact]
    public void MocSuaRacOPhiaDangCo_CungBiKep()
    {
        var gop = KmHoSoGop.Gop(HoSo(BayGio.AddYears(2), phanTram: 25), HoSo(Cu, phanTram: 5), BayGio);

        Assert.Equal(BayGio, gop.CfgUpdatedAt);
        Assert.Equal(25, gop.PhanTram);
    }

    /// <summary>Vế ĐỐI CHỨNG — mốc sửa TRONG biên (lệch đồng hồ thật) vẫn nhận nguyên. Kẹp mọi mốc tương lai là
    /// nuốt nhầm lượt sửa thật của một máy nhanh giờ vài phút.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void MocSuaTuongLaiTrongBien_ThiVanNhanNguyen(int gioLech)
    {
        var hoiSom = BayGio.AddHours(gioLech);

        var gop = KmHoSoGop.Gop(HoSo(Cu, phanTram: 5), HoSo(hoiSom, phanTram: 25), BayGio);

        Assert.Equal(hoiSom, gop.CfgUpdatedAt);
        Assert.Equal(25, gop.PhanTram);
    }

    /// <summary>
    /// <b>Cùng mốc sửa mà khác cấu hình ⇒ bản có <c>TuDong = true</c> thắng.</b> Ca thật: sau lượt nâng cấp,
    /// mọi máy được đóng dấu CÙNG một mốc backfill, mà <see cref="KmHoSoGop.MoiHon"/> dùng <c>&gt;</c> nên
    /// "bằng nhau = thua" ⇒ bản thắng là bản ĐẨY TRƯỚC. Không phá hoà thì hub / máy A / máy B giữ ba bản khác
    /// nhau, ổn định vĩnh viễn.
    /// </summary>
    [Fact]
    public void HoaMoc_BanBatTuDongThang_DuODauVao()
    {
        var batKm = HoSo(Cu, tuDong: true, phanTram: 25, gioBatDau: "08:00", ownerMachine: "may-a:orders");
        var macDinh = HoSo(Cu, tuDong: false, phanTram: 5);

        // Thắng ở CẢ HAI chiều: hub gộp (dangCo = bản trên hub) và client gộp (dangCo = bản cục bộ).
        Assert.Equal(25, KmHoSoGop.Gop(macDinh, batKm, BayGio).PhanTram);
        Assert.Equal(25, KmHoSoGop.Gop(batKm, macDinh, BayGio).PhanTram);
        Assert.True(KmHoSoGop.Gop(macDinh, batKm, BayGio).TuDong);
        Assert.True(KmHoSoGop.Gop(batKm, macDinh, BayGio).TuDong);
    }

    /// <summary>
    /// <b>Hoà tiếp mà MỘT BÊN KHÔNG CÓ CHỦ TRÌ ⇒ bên CÓ chủ trì thắng.</b> Chuỗi rỗng KHÔNG phải một id máy nên
    /// phải bị loại khỏi vế so id.
    /// <para><b>Ca hỏng nếu thiếu</b> (phản biện vòng 6, 22/08/2026): <c>string.Compare</c> xếp <c>""</c> trước
    /// MỌI chuỗi, nên bản rỗng thắng ở <b>cả hai chiều</b> — <c>Compare("", "may-a") &lt; 0</c> giữ bản rỗng, mà
    /// <c>Compare("may-a", "") &gt; 0</c> cũng lấy bản rỗng. Chỉ cần MỘT máy từng đẩy hồ sơ với chủ trì rỗng
    /// (ví dụ lượt backfill hỏng thoáng qua) là quyền chủ trì bị xoá trên TOÀN FLEET ở lượt gộp kế, rồi
    /// <c>LaMayChuTri("", …)</c> mở cửa cho mọi máy cùng chạy KM đêm sau — không một dòng log.</para>
    /// </summary>
    [Fact]
    public void HoaMoc_MotBenKhongCoChuTri_ThiBenCoChuTriThang()
    {
        var coChuTri = HoSo(Cu, tuDong: true, phanTram: 25, ownerMachine: "may-a:orders");
        var rong = HoSo(Cu, tuDong: true, phanTram: 40, ownerMachine: "");

        // Cả hai chiều ra cùng một kết quả, và kết quả đó là bản CÓ chủ trì.
        Assert.Equal("may-a:orders", KmHoSoGop.Gop(coChuTri, rong, BayGio).OwnerMachine);
        Assert.Equal("may-a:orders", KmHoSoGop.Gop(rong, coChuTri, BayGio).OwnerMachine);
        Assert.Equal(25, KmHoSoGop.Gop(coChuTri, rong, BayGio).PhanTram);
        Assert.Equal(25, KmHoSoGop.Gop(rong, coChuTri, BayGio).PhanTram);
    }

    /// <summary>Hoà tiếp (cùng mốc, cùng <c>TuDong</c>, CẢ HAI đều có chủ trì) ⇒ <c>OwnerMachine</c> nhỏ hơn
    /// thắng — một thứ tự TOÀN PHẦN, không mang ý nghĩa nghiệp vụ, chỉ để mọi máy ra CÙNG một kết quả.</summary>
    [Fact]
    public void HoaMoc_VaCungTuDong_ThiIdMayNhoHonThang()
    {
        var a = HoSo(Cu, tuDong: true, phanTram: 25, ownerMachine: "may-a:orders");
        var b = HoSo(Cu, tuDong: true, phanTram: 40, ownerMachine: "may-b:orders");

        Assert.Equal(25, KmHoSoGop.Gop(a, b, BayGio).PhanTram);
        Assert.Equal(25, KmHoSoGop.Gop(b, a, BayGio).PhanTram);
    }

    /// <summary>HỘI TỤ — chuỗi đẩy/kéo thật của ca W8 phải đưa hub, máy A và máy B về CÙNG một bản. Đây mới là
    /// thứ người dùng cảm nhận được; ba assert lẻ ở trên chỉ là điều kiện cần.</summary>
    [Fact]
    public void HoaMoc_ChuoiDayKeo_ThiCaBaBenHoiTu()
    {
        var a = HoSo(Cu, tuDong: true, phanTram: 25, gioBatDau: "08:00", ownerMachine: "may-a:orders");
        var b = HoSo(Cu, tuDong: false, phanTram: 5);

        var hub = b;                                   // máy B khởi động trước ⇒ đẩy trước (dòng chưa có: dangCo = den)
        hub = KmHoSoGop.Gop(hub, a, BayGio);           // máy A đẩy lên
        var aSauKhiKeo = KmHoSoGop.Gop(a, hub, BayGio);
        var bSauKhiKeo = KmHoSoGop.Gop(b, hub, BayGio);

        Assert.True(hub.TuDong);
        Assert.Equal(25, hub.PhanTram);
        Assert.Equal(hub.PhanTram, aSauKhiKeo.PhanTram);
        Assert.Equal(hub.PhanTram, bSauKhiKeo.PhanTram);
        Assert.Equal(hub.OwnerMachine, bSauKhiKeo.OwnerMachine);
        Assert.True(bSauKhiKeo.TuDong);
    }

    /// <summary>
    /// Cùng mốc, cùng cấu hình, KHÁC MÁY CHỦ TRÌ ⇒ vẫn phải phá hoà về một chủ trì DUY NHẤT.
    /// <para>Ca này lọt lưới nếu phép so cấu hình bỏ qua <c>OwnerMachine</c>: mỗi bên giữ chủ trì của mình, cả
    /// fleet bất đồng về "ai chạy lượt tự động" vĩnh viễn — hoặc hai máy cùng chạy, hoặc không máy nào chạy.
    /// (Lượt thử phá dựng ra đúng ca này.)</para>
    /// </summary>
    [Fact]
    public void HoaMoc_CungCauHinh_KhacChuTri_ThiVanHoiTuVeMotChuTri()
    {
        var a = HoSo(Cu, tuDong: true, phanTram: 25, ownerMachine: "may-a:orders");
        var b = HoSo(Cu, tuDong: true, phanTram: 25, ownerMachine: "may-b:orders");

        Assert.True(KmHoSoGop.CanPhaHoa(a, b, BayGio));
        Assert.Equal("may-a:orders", KmHoSoGop.Gop(a, b, BayGio).OwnerMachine);
        Assert.Equal("may-a:orders", KmHoSoGop.Gop(b, a, BayGio).OwnerMachine);
    }

    /// <summary>Cùng mốc mà cấu hình Y HỆT ⇒ giữ bản đang có, <see cref="KmHoSoGop.Gop"/> vẫn IDEMPOTENT (nhịp
    /// 60 giây gộp đi gộp lại không được làm dữ liệu dao động).</summary>
    [Fact]
    public void HoaMoc_CungCauHinh_ThiKhongDoiGi()
    {
        var x = HoSo(Cu, tuDong: true, phanTram: 25, ownerMachine: "may-a:orders");

        var gop = KmHoSoGop.Gop(x, x, BayGio);

        Assert.Equal(x.PhanTram, gop.PhanTram);
        Assert.Equal(x.OwnerMachine, gop.OwnerMachine);
        Assert.Equal(x.CfgUpdatedAt, gop.CfgUpdatedAt);
        Assert.False(KmHoSoGop.CanPhaHoa(x, x, BayGio));
    }

    /// <summary><see cref="KmHoSoGop.CanPhaHoa"/> là cờ để nơi gọi GHI LOG — phải bật đúng ca (cùng mốc, khác
    /// cấu hình) và tắt ở ca mốc khác nhau (lúc đó luật "sửa sau thắng" đã đủ giải thích).</summary>
    [Fact]
    public void CanPhaHoa_ChiBatKhiCungMocMaKhacCauHinh()
    {
        Assert.True(KmHoSoGop.CanPhaHoa(HoSo(Cu, phanTram: 5), HoSo(Cu, phanTram: 25), BayGio));
        Assert.False(KmHoSoGop.CanPhaHoa(HoSo(Cu, phanTram: 5), HoSo(Moi, phanTram: 25), BayGio));
        Assert.False(KmHoSoGop.CanPhaHoa(HoSo(Cu, phanTram: 5), HoSo(Cu, phanTram: 5), BayGio));
    }

    [Fact]
    public void DoiMuiGio_Null_ThiNull()
    {
        Assert.Null(KmMocThoiGian.LenDay(null));
        Assert.Null(KmMocThoiGian.VeMay(null));
    }

    /// <summary>Cùng một mốc đồng hồ ghi bằng <c>Unspecified</c> hay <c>Local</c> phải LÊN DÂY như nhau — hai
    /// cách ghi đó cùng tồn tại trong <c>km_config</c> (dòng cũ ghi bằng <c>DateTime.Now</c>, dòng mới ghi
    /// chuỗi không offset), gửi lên hub thành hai mốc khác nhau là gộp sai ngay.</summary>
    [Fact]
    public void DoiMuiGio_UnspecifiedVaLocal_LenDayRaCungMotMoc()
    {
        var clock = new DateTime(2026, 8, 22, 2, 5, 0);

        Assert.Equal(
            KmMocThoiGian.LenDay(DateTime.SpecifyKind(clock, DateTimeKind.Unspecified)),
            KmMocThoiGian.LenDay(DateTime.SpecifyKind(clock, DateTimeKind.Local)));
    }

    // ── HAI THÔNG SỐ FLASH (03/09/2026): nhóm CẤU HÌNH, và NULL nghĩa là "không biết" ────────────────

    /// <summary>
    /// Hai thông số flash đi theo ĐÚNG BẢN THẮNG của <c>CfgUpdatedAt</c> — cùng gói với cấu hình kia, không đẻ
    /// luật gộp thứ ba.
    /// </summary>
    [Fact]
    public void Flash_ThamSo_TheoBanCoMocSuaMoiHon()
    {
        var dangCo = HoSo(Cu) with { FlashPhanTramGiam = 5, FlashSoLuongThamGia = 10 };
        var den = HoSo(Moi) with { FlashPhanTramGiam = 8, FlashSoLuongThamGia = 25 };

        var gop = KmHoSoGop.Gop(dangCo, den, BayGio);
        Assert.Equal(8, gop.FlashPhanTramGiam);
        Assert.Equal(25, gop.FlashSoLuongThamGia);

        // Vế đối chứng: bản CŨ HƠN không được ghi đè.
        var nguoc = KmHoSoGop.Gop(den, dangCo, BayGio);
        Assert.Equal(8, nguoc.FlashPhanTramGiam);
        Assert.Equal(25, nguoc.FlashSoLuongThamGia);
    }

    /// <summary>
    /// <b>⚠ BẢN THẮNG KHÔNG BIẾT TRƯỜNG ⇒ LẤY CỦA BÊN KIA, KHÔNG xoá trắng.</b> Đây là NGOẠI LỆ duy nhất của
    /// luật "lấy trọn gói của bản nền", và nó có lý do đo được.
    ///
    /// <para><b>Ca hỏng mà bài này chặn</b> (phản biện vòng 2, 03/09/2026): một CLIENT CŨ (chưa có hai trường)
    /// sửa cấu hình lúc 10:05 rồi đẩy lên; hàng trên hub đang giữ 8% do máy mới đặt lúc 10:00. Bản cũ thắng mốc
    /// sửa ⇒ nếu lấy thẳng <c>nen.X</c> thì hai trường thành <c>null</c> ⇒ hub ghi DEFAULT 5 ⇒ <b>mọi máy kéo về
    /// 5%</b>, đúng 60 giây sau, không một dòng log. <c>null</c> là "KHÔNG CÓ Ý KIẾN", và bên không có ý kiến
    /// thì không được phủ quyết bên có.</para>
    ///
    /// <para>Vế thứ hai là vế giữ ngữ nghĩa: CẢ HAI cùng <c>null</c> ⇒ vẫn <c>null</c> (không ai biết trường
    /// này) — chỗ ánh xạ về <c>km_config</c> sẽ đọc <c>?? cu.X</c> và GIỮ giá trị cục bộ. Thay bằng 5/10 ngay
    /// tại đây là biến "không biết" thành một ý muốn người dùng chưa từng gõ.</para>
    /// </summary>
    [Fact]
    public void Flash_BanThangKhongBietTruong_ThiLayCuaBenKia_ChuKhongXoaTrang()
    {
        var dangCo8 = HoSo(Cu) with { FlashPhanTramGiam = 8, FlashSoLuongThamGia = 25 };
        var moiHonNhungKhongBiet = HoSo(Moi);   // client CŨ: hai trường vắng ⇒ null

        // Bản MỚI HƠN không biết trường ⇒ giá trị của bản cũ SỐNG SÓT.
        var gop = KmHoSoGop.Gop(dangCo8, moiHonNhungKhongBiet, BayGio);
        Assert.Equal(8, gop.FlashPhanTramGiam);
        Assert.Equal(25, gop.FlashSoLuongThamGia);

        // Đối xứng: đảo hai phía cũng ra đúng thế (bản nền lúc này là bản đang có, cũng không biết).
        var nguoc = KmHoSoGop.Gop(moiHonNhungKhongBiet, dangCo8, BayGio);
        Assert.Equal(8, nguoc.FlashPhanTramGiam);
        Assert.Equal(25, nguoc.FlashSoLuongThamGia);

        // CẢ HAI cùng không biết ⇒ vẫn null (giữ nguyên nghĩa "không có ý kiến" cho chặng ánh xạ).
        var caHaiKhongBiet = KmHoSoGop.Gop(HoSo(Cu), HoSo(Moi), BayGio);
        Assert.Null(caHaiKhongBiet.FlashPhanTramGiam);
        Assert.Null(caHaiKhongBiet.FlashSoLuongThamGia);
    }

    /// <summary>
    /// <b>Hai bản CÙNG BIẾT mà khác thông số flash thì KHÔNG "y hệt"</b> — thiếu vế này trong
    /// <see cref="KmHoSoGop.CungCauHinh"/> thì lượt phá hoà (cùng mốc sửa) giữ bản đang có và cú đổi 8% của máy
    /// kia biến mất, không một dòng log.
    ///
    /// <para><b>Nhưng <c>null</c> KHÔNG phải một khác biệt</b> — nó là "bên đó chưa biết trường này" (hub cũ /
    /// client cũ). So thẳng <c>a == b</c> thì trong cửa sổ rollout, mỗi lượt kéo về ghi một dòng "cùng mốc sửa
    /// mà cấu hình khác nhau, đã chọn bản thắng theo thứ tự cố định" — cảnh báo SAI cho đúng tình huống mà cả
    /// thiết kế nullable sinh ra để đi qua im lặng. Xem <c>CungThamSoNullable</c>.</para>
    /// </summary>
    [Fact]
    public void Flash_CungCauHinh_TinhHaiThamSo_NhungNullLaKhongCoYKien()
    {
        var a = HoSo(Cu) with { FlashPhanTramGiam = 5, FlashSoLuongThamGia = 10 };

        // CÙNG BIẾT mà khác số ⇒ KHÁC.
        Assert.False(KmHoSoGop.CungCauHinh(a, a with { FlashPhanTramGiam = 8 }));
        Assert.False(KmHoSoGop.CungCauHinh(a, a with { FlashSoLuongThamGia = 25 }));
        Assert.True(KmHoSoGop.CungCauHinh(a, a));

        // MỘT BÊN không biết ⇒ KHÔNG phải khác biệt, ở CẢ HAI chiều.
        var khongBiet = a with { FlashPhanTramGiam = null, FlashSoLuongThamGia = null };
        Assert.True(KmHoSoGop.CungCauHinh(a, khongBiet));
        Assert.True(KmHoSoGop.CungCauHinh(khongBiet, a));

        // …và vì thế lượt PHÁ HOÀ không bật cho ca "client mới ↔ hub cũ" (đây mới là hậu quả đo được).
        Assert.False(KmHoSoGop.CanPhaHoa(a, khongBiet, BayGio));
        Assert.True(KmHoSoGop.CanPhaHoa(a, a with { FlashPhanTramGiam = 8 }, BayGio));

        // CẢ HAI cùng không biết ⇒ y hệt.
        Assert.True(KmHoSoGop.CungCauHinh(HoSo(Cu), HoSo(Cu)));
    }

    /// <summary>Hai hằng mặc định của hợp đồng hub phải khớp con số đang chạy thật (5% / SL 10) — cùng ràng buộc
    /// ba-nơi-một-khái-niệm đã ghi cho <c>SoNgay = 3</c>. Lệch là một đường nào đó rơi vào mặc định và đổi thầm
    /// cấu hình flash của cả fleet.</summary>
    [Fact]
    public void Flash_HangMacDinh_KhopSoDangChayThat()
    {
        Assert.Equal(5, KmProfileItem.MacDinhFlashPhanTramGiam);
        Assert.Equal(10, KmProfileItem.MacDinhFlashSoLuongThamGia);
    }
}
