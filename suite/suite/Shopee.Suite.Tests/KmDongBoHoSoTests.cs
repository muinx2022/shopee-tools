// System.IO KHÔNG có trong danh sách implicit của SDK desktop (net8.0-windows) — thiếu là Path/File không tồn tại.
using System.IO;
using Microsoft.Data.Sqlite;
using Shopee.Core.Coordination;
using Shopee.Suite.Infrastructure;
using XuLyDonShopee.Core.Data;

namespace Shopee.Suite.Tests;

/// <summary>
/// HAI CỬA ĐỔI MÚI GIỜ của đồng bộ hồ sơ KM: <c>OrdersModuleHost.ThanhHoSo</c> (đẩy: giờ địa phương → UTC) và
/// <c>OrdersModuleHost.GopVeMay</c> (kéo: UTC → giờ địa phương, gộp bằng luật dùng chung).
/// <para><b>Vì sao đúng hai cửa này đáng bộ test riêng:</b> <c>km_config</c> lưu <see cref="DateTime"/> với
/// <c>Kind = Unspecified</c>, tức một con số KHÔNG mang múi giờ. Sai ở đây thì <c>LanChayCuoi</c> lệch 7 tiếng,
/// chu kỳ "mỗi 7 ngày" thành 6 hoặc 8, hoặc mốc kế rơi ra ngoài khung 2–6h và KM không bao giờ chạy nữa —
/// tất cả đều IM LẶNG, không lỗi, không log.</para>
/// </summary>
public class KmDongBoHoSoTests
{
    private const string Login = "sub@shopee.vn";

    /// <summary>Mốc "bây giờ" (giờ địa phương) mặc định của bộ test — SAU mọi mốc dựng cảnh ở đây, nên không
    /// ca nào chạm trần mốc tương lai (<see cref="KmHoSoGop.BienMocTuongLai"/>); trần có bộ test riêng.</summary>
    private static readonly DateTime BayGio = new(2026, 8, 22, 10, 0, 0);

    private static KmConfig CauHinh(
        bool tuDong = false, int soNgay = 7, int phanTram = 5, string gioBatDau = "09:30",
        DateTime? lanChayCuoi = null, DateTime? lanThuTuDong = null,
        DateTime? cfgUpdatedAt = null, string ownerMachine = "")
        => new(1, tuDong, soNgay, phanTram, gioBatDau, "10:30", lanChayCuoi, lanThuTuDong)
        {
            CfgUpdatedAt = cfgUpdatedAt,
            OwnerMachine = ownerMachine,
        };

    // ── Cửa ĐẨY + cửa KÉO: đi vòng tròn ─────────────────────────────────────────

    /// <summary>
    /// <b>Chu kỳ mặc định 3 ngày khai ở HAI tầng phải TRÙNG nhau</b> — <c>KmConfigRepository.MacDinhSoNgay</c>
    /// (client) và <c>KmProfileItem.SoNgay</c> (DTO đồng bộ). Tầng thứ ba (cột <c>km_profiles.so_ngay</c> của
    /// hub) khoá bằng <c>KmHoSoHubTests.SoNgayMacDinh_La3_QuaTronVongHub</c>.
    /// <para>Vế thứ hai là ca thật: hub CŨ chưa deploy bản có field ⇒ JSON về thiếu <c>SoNgay</c> ⇒ DTO rơi về
    /// giá trị khởi tạo, và giá trị ấy đi thẳng xuống <c>km_config</c>. Lệch 7 vs 3 ở đây là chu kỳ của người
    /// dùng bị đổi âm thầm.</para>
    /// </summary>
    [Fact]
    public void ChuKyMacDinh_HaiTang_DeuLa3()
    {
        Assert.Equal(3, KmConfigRepository.MacDinhSoNgay);
        Assert.Equal(KmConfigRepository.MacDinhSoNgay, new KmProfileItem(Login).SoNgay);

        // Hub gửi về bản KHÔNG có field SoNgay + mốc sửa mới hơn ⇒ cấu hình của hub thắng, và số kéo xuống
        // km_config phải là 3.
        var ve = OrdersModuleHost.GopVeMay(
            CauHinh(soNgay: 5, cfgUpdatedAt: new DateTime(2026, 8, 20, 9, 0, 0)),
            Login,
            new KmProfileItem(Login)
            {
                PhanTram = 25,
                GioBatDau = "08:00",
                GioKetThuc = "09:00",
                CfgUpdatedAt = new DateTimeOffset(2026, 8, 22, 2, 0, 0, TimeSpan.Zero),
            },
            BayGio);

        Assert.Equal(25, ve.PhanTram);   // bản của hub ĐÃ vào (không thì vế dưới vô nghĩa)
        Assert.Equal(3, ve.SoNgay);
    }

    // ── VỪA-4: lượt reset cấu hình combo phải NÓI RA ────────────────────────────

    /// <summary>
    /// <b>Cửa sổ rollout CLIENT CŨ: bản đến làm TẮT vòng combo ⇒ dòng nhật ký PHẢI nhắc tới combo.</b>
    /// <para>Ca thật (phản biện 25/08/2026): máy B chưa cập nhật Velopack nên JSON của nó không có trường
    /// <c>combo*</c>; nó chỉ sửa <c>%</c> lúc 10:05 ⇒ mốc sửa mới hơn ⇒ luật "máy nào sửa sau thắng" lấy TRỌN
    /// gói của B ⇒ <c>ComboBat</c> về <c>false</c> và <c>ComboGiam</c> về mặc định ⇒ máy A kéo bản đó về 60 giây
    /// sau và <b>vòng combo tắt câm</b>. Bản cũ của <c>CauDoiCauHinh</c> không có một chữ nào về combo, nên lượt
    /// reset đó không để lại dấu vết nào để lần ra.</para>
    /// <para>Đo trên KẾT QUẢ GỘP THẬT (<c>GopVeMay</c>) chứ không tự bịa một <c>KmConfig</c>: có thế mới đồng
    /// thời khẳng định lượt gộp ĐÚNG LÀ có làm tắt combo — tức test không nói về một ca tưởng tượng.</para>
    /// </summary>
    [Fact]
    public void ClientCu_LamTatCombo_ThiCauDoiCauHinh_PhaiNhacToiCombo()
    {
        var cuaMayNay = CauHinh(cfgUpdatedAt: new DateTime(2026, 8, 22, 9, 0, 0)) with
        {
            ComboBat = true,
            ComboGiam = 5000,
        };

        // Bản của máy B: mốc sửa MỚI HƠN, và không mang một trường combo nào (client cũ).
        var tuClientCu = new KmProfileItem(Login)
        {
            PhanTram = 25,
            GioBatDau = "09:30",
            GioKetThuc = "10:30",
            CfgUpdatedAt = new DateTimeOffset(2026, 8, 22, 9, 5, 0, TimeSpan.Zero),
        };

        var ve = OrdersModuleHost.GopVeMay(cuaMayNay, Login, tuClientCu, BayGio);
        Assert.False(ve.ComboBat);                                   // lượt reset CÓ THẬT
        Assert.NotEqual(cuaMayNay.ComboGiam, ve.ComboGiam);

        var cau = OrdersModuleHost.CauDoiCauHinh(ve, null);
        Assert.Contains("combo TẮT", cau);
        Assert.Contains(ve.ComboGiam.ToString(), cau);               // mức ưu đãi vừa bị kéo về cũng phải hiện

        // Vế ĐỐI CHỨNG: combo còn BẬT thì câu nói BẬT — thiếu vế này thì một bản in "combo TẮT" cứng cũng xanh.
        var cauBat = OrdersModuleHost.CauDoiCauHinh(cuaMayNay, null);
        Assert.Contains("combo BẬT", cauBat);
        Assert.Contains("5000", cauBat);
    }

    // ── HAI THÔNG SỐ FLASH (03/09/2026): `null` trên dây ⇒ GIỮ bản cục bộ ───────────────────────────

    /// <summary>
    /// <b>Bản đến KHÔNG BIẾT hai trường flash (hub cũ / client cũ) ⇒ GIỮ số cục bộ, KHÔNG reset về 5/10.</b>
    ///
    /// <para><b>Đây là lỗi mất cấu hình mà cả thiết kế nullable sinh ra để chặn</b>, và nó khác hẳn ca combo ở
    /// bài trên: ở đó cách chữa là "mặc định của DTO phải khớp mặc định của <c>km_config</c>", còn hai con số
    /// này là thứ người dùng GÕ và có thể là bất kỳ số nào — không có mặc định nào chữa được. Nếu
    /// <c>ApNhomCauHinh</c> gán thẳng <c>hoSo.X</c> thì máy vừa đặt 8% sẽ nhận lại 5% (hoặc 0) ở lượt kéo kế
    /// tiếp, đúng 60 giây sau, không một dòng log.</para>
    ///
    /// <para><b>Chú ý bản đến ở đây có mốc sửa MỚI HƠN</b> — tức nó THẮNG lượt gộp và mang trọn cấu hình của nó
    /// sang (vế `PhanTram` khẳng định điều đó). Đúng ca khó: bản thắng vẫn không được xoá thứ nó không biết.</para>
    /// </summary>
    [Fact]
    public void ClientCu_KhongGuiThamSoFlash_ThiGiuNguyenSoCucBo()
    {
        var cuaMayNay = CauHinh(cfgUpdatedAt: new DateTime(2026, 8, 22, 9, 0, 0)) with
        {
            FlashPhanTramGiam = 8,
            FlashSoLuongThamGia = 25,
        };

        // Bản đến: mốc sửa MỚI HƠN (thắng), nhưng KHÔNG mang hai trường flash ⇒ null.
        var tuClientCu = new KmProfileItem(Login)
        {
            PhanTram = 25,
            GioBatDau = "09:30",
            GioKetThuc = "10:30",
            CfgUpdatedAt = new DateTimeOffset(2026, 8, 22, 9, 5, 0, TimeSpan.Zero),
        };

        var ve = OrdersModuleHost.GopVeMay(cuaMayNay, Login, tuClientCu, BayGio);

        Assert.Equal(25, ve.PhanTram);            // bản đến ĐÃ thắng (không thì vế dưới vô nghĩa)
        Assert.Equal(8, ve.FlashPhanTramGiam);    // …mà vẫn KHÔNG xoá thứ nó không biết
        Assert.Equal(25, ve.FlashSoLuongThamGia);
    }

    /// <summary>Hub trả cặp số KHÔNG QUA cửa form (vd dòng bị sửa tay thành 0) ⇒ giữ bản cục bộ, không ghi rác vào
    /// <c>km_config</c> — nếu không thì mọi gói flash sau đó chết ở bước "0. Tham số" tới khi người dùng mở form.
    /// Kiểm theo CẶP (như cửa form): một vế rác thì cả cặp giữ cục bộ.</summary>
    [Fact]
    public void HubTraThamSoFlashRac_ThiGiuNguyenSoCucBo()
    {
        var cuaMayNay = CauHinh(cfgUpdatedAt: new DateTime(2026, 8, 22, 9, 0, 0)) with
        {
            FlashPhanTramGiam = 8,
            FlashSoLuongThamGia = 25,
        };
        var tuHub = new KmProfileItem(Login)
        {
            PhanTram = 25,
            GioBatDau = "09:30",
            GioKetThuc = "10:30",
            CfgUpdatedAt = new DateTimeOffset(2026, 8, 22, 9, 5, 0, TimeSpan.Zero),
            FlashPhanTramGiam = 0,        // rác: dưới sàn 1%
            FlashSoLuongThamGia = 30,
        };

        var ve = OrdersModuleHost.GopVeMay(cuaMayNay, Login, tuHub, BayGio);

        Assert.Equal(25, ve.PhanTram);              // bản đến vẫn thắng phần cấu hình hợp lệ
        Assert.Equal(8, ve.FlashPhanTramGiam);      // …nhưng cặp flash rác thì giữ cục bộ
        Assert.Equal(25, ve.FlashSoLuongThamGia);
    }

    /// <summary>
    /// Vế ĐỐI CHỨNG: bản đến CÓ BIẾT hai trường ⇒ số của nó VÀO THẬT.
    /// <para>Không có vế này thì một bản vá "luôn giữ bản cục bộ" cũng xanh — và lúc đó đổi % flash ở máy A sẽ
    /// không bao giờ tới máy B, tức mất luôn tính năng đồng bộ mà đợt này đi trọn 4 lớp để có.</para>
    /// </summary>
    [Fact]
    public void ClientMoi_GuiThamSoFlash_ThiSoCuaNoVaoThat()
    {
        var cuaMayNay = CauHinh(cfgUpdatedAt: new DateTime(2026, 8, 22, 9, 0, 0)) with
        {
            FlashPhanTramGiam = 5,
            FlashSoLuongThamGia = 10,
        };

        var tuMayKhac = OrdersModuleHost.ThanhHoSo(Login, CauHinh(
            cfgUpdatedAt: new DateTime(2026, 8, 22, 9, 5, 0)) with
        {
            FlashPhanTramGiam = 12,
            FlashSoLuongThamGia = 30,
        });

        var ve = OrdersModuleHost.GopVeMay(cuaMayNay, Login, tuMayKhac, BayGio);
        Assert.Equal(12, ve.FlashPhanTramGiam);
        Assert.Equal(30, ve.FlashSoLuongThamGia);

        // Chiều ĐẨY luôn mang số thật (cột client khai NOT NULL DEFAULT ⇒ không bao giờ null ở đây).
        Assert.Equal(12, tuMayKhac.FlashPhanTramGiam);
        Assert.Equal(30, tuMayKhac.FlashSoLuongThamGia);

        // Và dòng nhật ký "cấu hình vừa ĐỔI" phải nói ra hai con số — một lượt gộp đổi chúng mà im lặng thì
        // không ai lần lại nổi vì sao đêm sau Shopee nhận 12% thay vì 5%.
        var cau = OrdersModuleHost.CauDoiCauHinh(ve, null);
        Assert.Contains("giảm 12%", cau);
        Assert.Contains("SL tham gia 30", cau);
    }

    /// <summary>Local → hồ sơ (UTC) → local phải ra ĐÚNG ba mốc ban đầu. Hồ sơ hub ở đây mang cấu hình CŨ HƠN
    /// nên không đụng gì tới bản local — test đo riêng đường mốc thời gian.</summary>
    [Fact]
    public void DiVongTron_BaMocGiuNguyen()
    {
        var cf = CauHinh(
            lanChayCuoi: new DateTime(2026, 8, 15, 2, 30, 0),
            lanThuTuDong: new DateTime(2026, 8, 22, 2, 5, 0),
            cfgUpdatedAt: new DateTime(2026, 8, 21, 14, 3, 9));

        var hoSo = OrdersModuleHost.ThanhHoSo(Login, cf);
        var ve = OrdersModuleHost.GopVeMay(cf, Login, hoSo, BayGio);

        Assert.Equal(cf.LanChayCuoi, ve.LanChayCuoi);
        Assert.Equal(cf.LanThuTuDong, ve.LanThuTuDong);
        Assert.Equal(cf.CfgUpdatedAt, ve.CfgUpdatedAt);
    }

    /// <summary>Cửa ĐẨY phải ra UTC THẬT (đổi giá trị theo offset máy), không phải "gán nhãn UTC" cho con số
    /// địa phương — bản gán nhãn cũng làm test vòng tròn ở trên xanh, nên phải đo riêng.</summary>
    [Fact]
    public void CuaDay_RaUtcThat_KhongPhaiGanNhan()
    {
        var moc = new DateTime(2026, 8, 22, 2, 5, 0);
        var hoSo = OrdersModuleHost.ThanhHoSo(Login, CauHinh(lanChayCuoi: moc));

        Assert.NotNull(hoSo.LanChayCuoi);
        Assert.Equal(TimeSpan.Zero, hoSo.LanChayCuoi!.Value.Offset);
        Assert.Equal(moc.ToUniversalTime(), hoSo.LanChayCuoi.Value.UtcDateTime);
    }

    [Fact]
    public void CuaDay_MocNull_ThiVanNull_KhongThanhMinValue()
    {
        var hoSo = OrdersModuleHost.ThanhHoSo(Login, CauHinh());

        Assert.Null(hoSo.LanChayCuoi);
        Assert.Null(hoSo.LanThuTuDong);
        Assert.Null(hoSo.CfgUpdatedAt);
    }

    // ── Nhánh DEGRADE: mất hub thì dùng nguyên km_config cục bộ ─────────────────

    /// <summary>
    /// <b>Mất hub ⇒ hành vi y một máy.</b> <c>GetKmProfilesAsync</c> trả <c>null</c> khi offline / hub cũ chưa
    /// có route; hàm gộp phải trả về CHÍNH bản cục bộ, không đụng một trường nào. Đây là luật đã có ở
    /// <c>AcquireAccountLease</c> — mất mạng không được làm chết tính năng.
    /// </summary>
    [Fact]
    public void MatHub_ThiGiuNguyenBanCucBo()
    {
        var cf = CauHinh(tuDong: true, soNgay: 3, phanTram: 25, gioBatDau: "08:00",
            lanChayCuoi: new DateTime(2026, 8, 15, 2, 30, 0),
            cfgUpdatedAt: new DateTime(2026, 8, 21, 14, 0, 0), ownerMachine: "may-a:orders");

        var ve = OrdersModuleHost.GopVeMay(cf, Login, tuHub: null, BayGio);

        Assert.Same(cf, ve);
    }

    // ── Gộp thật: hub thắng / local thắng ───────────────────────────────────────

    /// <summary>Ca hỏng (a) của plan: bật "chạy tự động" ở máy A, máy B chưa từng mở form (<c>CfgUpdatedAt</c>
    /// null) ⇒ B phải NHẬN cấu hình của A.</summary>
    [Fact]
    public void HubCoMocSuaMoiHon_ThiMayNayNhanCauHinhCuaHub()
    {
        var cucBo = CauHinh();   // chưa từng mở form
        var tuHub = new KmProfileItem(Login)
        {
            TuDong = true,
            SoNgay = 3,
            PhanTram = 25,
            GioBatDau = "08:00",
            GioKetThuc = "09:00",
            OwnerMachine = "may-a:orders",
            CfgUpdatedAt = new DateTimeOffset(2026, 8, 21, 7, 0, 0, TimeSpan.Zero),
        };

        var ve = OrdersModuleHost.GopVeMay(cucBo, Login, tuHub, BayGio);

        Assert.True(ve.TuDong);
        Assert.Equal(3, ve.SoNgay);
        Assert.Equal(25, ve.PhanTram);
        Assert.Equal("08:00", ve.GioBatDau);
        Assert.Equal("may-a:orders", ve.OwnerMachine);
        Assert.Equal(1, ve.AccountId);   // KHÓA cục bộ giữ nguyên: account_id là của máy này, không đến từ hub
    }

    /// <summary>Vế đối chứng: bản cục bộ vừa sửa (mốc mới hơn) KHÔNG được hồ sơ hub cũ nuốt — nếu không, người
    /// dùng bấm Lưu xong thấy cấu hình tự quay về giá trị cũ trong vòng một phút.</summary>
    [Fact]
    public void CucBoSuaSauHon_ThiHubKhongDeDuoc()
    {
        var cucBo = CauHinh(tuDong: true, phanTram: 25, gioBatDau: "08:00",
            cfgUpdatedAt: new DateTime(2026, 8, 22, 9, 0, 0), ownerMachine: "may-b:orders");
        var tuHub = new KmProfileItem(Login)
        {
            TuDong = false,
            PhanTram = 5,
            GioBatDau = "09:30",
            GioKetThuc = "10:30",
            OwnerMachine = "may-a:orders",
            CfgUpdatedAt = new DateTimeOffset(2026, 8, 20, 7, 0, 0, TimeSpan.Zero),
        };

        var ve = OrdersModuleHost.GopVeMay(cucBo, Login, tuHub, BayGio);

        Assert.True(ve.TuDong);
        Assert.Equal(25, ve.PhanTram);
        Assert.Equal("08:00", ve.GioBatDau);
        Assert.Equal("may-b:orders", ve.OwnerMachine);
    }

    /// <summary>Mốc chạy trên hub MỚI HƠN thì máy này phải nhận, kể cả khi cấu hình của nó thắng — đây là thứ
    /// chặn "đêm sau máy B chạy lại đúng lượt KM máy A vừa chạy".</summary>
    [Fact]
    public void MocChayTrenHubMoiHon_ThiMayNayTien_DuCauHinhCuaMinhThang()
    {
        var cucBo = CauHinh(phanTram: 25,
            lanChayCuoi: new DateTime(2026, 8, 15, 2, 30, 0),
            cfgUpdatedAt: new DateTime(2026, 8, 22, 9, 0, 0));
        var mocHub = new DateTime(2026, 8, 22, 2, 40, 0);
        var tuHub = new KmProfileItem(Login)
        {
            PhanTram = 5,
            CfgUpdatedAt = new DateTimeOffset(2026, 8, 20, 7, 0, 0, TimeSpan.Zero),
            LanChayCuoi = KmMocThoiGian.LenDay(mocHub),
        };

        var ve = OrdersModuleHost.GopVeMay(cucBo, Login, tuHub, BayGio);

        Assert.Equal(25, ve.PhanTram);          // cấu hình: bản cục bộ thắng
        Assert.Equal(mocHub, ve.LanChayCuoi);   // mốc chạy: MAX, lấy của hub, ĐÚNG giờ địa phương
    }

    /// <summary>Mốc chạy trên hub CŨ HƠN thì tuyệt đối không kéo lùi mốc của máy này — kể cả khi hồ sơ hub
    /// THẮNG ở nhóm cấu hình (mốc sửa mới hơn). Cho hub thắng cấu hình là cố ý: nếu mốc chạy đi theo bản thắng
    /// cấu hình thì test phải ĐỎ, chứ không được xanh nhờ tình cờ.</summary>
    [Fact]
    public void MocChayTrenHubCuHon_ThiKhongKeoLui()
    {
        var cucBo = CauHinh(lanChayCuoi: new DateTime(2026, 8, 22, 2, 40, 0));
        var tuHub = new KmProfileItem(Login)
        {
            CfgUpdatedAt = new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero),
            LanChayCuoi = KmMocThoiGian.LenDay(new DateTime(2026, 8, 15, 2, 30, 0)),
        };

        Assert.Equal(new DateTime(2026, 8, 22, 2, 40, 0),
            OrdersModuleHost.GopVeMay(cucBo, Login, tuHub, BayGio).LanChayCuoi);
    }

    // ── Vòng thật qua SQLite: so_ngay = 0 của DB cũ không được sống sót ─────────

    /// <summary>
    /// Hồ sơ hub mang <c>SoNgay = 0</c> (một máy bản cũ đẩy lên từ dòng <c>so_ngay = 0</c> có sẵn trong DB) đi
    /// qua trọn vòng gộp → ghi → đọc thì phải ra ≥ 1. Không giữ được thì tài khoản đó mỗi đêm ăn một dòng
    /// "CẤU HÌNH KHÔNG DÙNG ĐƯỢC" của <c>NhipLichKmAsync</c> — và lần này là trên MỌI máy, không riêng máy có
    /// dòng hỏng.
    /// </summary>
    [Fact]
    public void SoNgay0TuHub_QuaVongDongBo_VanDocRaItNhat1()
    {
        var path = Path.Combine(Path.GetTempPath(), $"suite_km_test_{Guid.NewGuid():N}.db");
        try
        {
            var repo = new KmConfigRepository(new Database(path));
            repo.Save(CauHinh(cfgUpdatedAt: new DateTime(2026, 8, 20, 9, 0, 0)));

            var tuHub = new KmProfileItem(Login)
            {
                SoNgay = 0,
                PhanTram = 25,
                GioBatDau = "08:00",
                GioKetThuc = "09:00",
                CfgUpdatedAt = new DateTimeOffset(2026, 8, 22, 2, 0, 0, TimeSpan.Zero),
            };

            repo.Save(OrdersModuleHost.GopVeMay(repo.Get(1), Login, tuHub, BayGio));

            var doc = new KmConfigRepository(new Database(path)).Get(1);
            Assert.Equal(25, doc.PhanTram);   // cấu hình của hub ĐÃ vào (không thì test dưới là vô nghĩa)
            Assert.True(doc.SoNgay >= 1, $"so_ngay đọc ra phải ≥ 1, thực tế {doc.SoNgay}");
        }
        finally
        {
            using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                SqliteConnection.ClearPool(conn);
            }
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
