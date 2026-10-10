using Shopee.Core.Coordination;
using Shopee.Hub;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// Bảng <c>km_profiles</c> trên hub — hồ sơ KM DÙNG CHUNG của tài khoản, khoá <c>login</c>.
/// <para><b>Bẫy phải khoá bằng test:</b> hợp đồng ở đây NGƯỢC với gương danh bạ ngay bên cạnh
/// (<c>UpsertOrdersAccounts</c> xoá trọn danh bạ của một <c>machine_id</c> rồi ghi lại). Ai chép nhầm hợp đồng
/// đó sang đây thì lượt đẩy của máy A xoá sạch hồ sơ của máy B, mà triệu chứng chỉ là "KM tự nhiên tắt".</para>
/// </summary>
public sealed class KmHoSoHubTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "hub-km-test-" + Guid.NewGuid().ToString("N"));

    private const string May1 = "may-1:orders";
    private const string May2 = "may-2:orders";
    private const string LoginA = "sub-a@shopee.vn";
    private const string LoginB = "sub-b@shopee.vn";

    private static readonly DateTimeOffset Cu = new(2026, 8, 20, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Moi = new(2026, 8, 21, 3, 0, 0, TimeSpan.Zero);

    private static KmProfilesPushRequest Day(string machineId, params KmProfileItem[] items)
        => new(machineId, [.. items]);

    /// <param name="lanThuTuDong"><b>Phải truyền được, và phải có test dùng tới</b> (phản biện vòng 5, Z3):
    /// bản trước không có tham số này nên MỌI hồ sơ đẩy lên trong cả bộ đều mang <c>null</c>, và phép đo duy
    /// nhất chạm tới mốc ấy là một <c>Assert.Null</c>. Nghiệm thu vòng 5 đột biến vế <c>LanThuTuDong</c> trong
    /// <c>KmHoSoGop.Gop</c> (mất mốc ⇒ vẫn ra <c>null</c>) và <b>không một test nào trong cả repo đỏ</b> —
    /// 2552 test. Mốc này là thứ pha C dựa vào để chốt "đêm nay ai đã dùng lượt".</param>
    private static KmProfileItem HoSo(
        string login, DateTimeOffset? cfgUpdatedAt, bool tuDong = false, int phanTram = 5,
        string ownerMachine = "", DateTimeOffset? lanChayCuoi = null, DateTimeOffset? lanThuTuDong = null)
        => new(login)
        {
            TuDong = tuDong,
            SoNgay = 7,
            PhanTram = phanTram,
            GioBatDau = "09:30",
            GioKetThuc = "10:30",
            OwnerMachine = ownerMachine,
            CfgUpdatedAt = cfgUpdatedAt,
            LanChayCuoi = lanChayCuoi,
            LanThuTuDong = lanThuTuDong,
        };

    private static KmProfileItem LayRa(HubDatabase db, string login)
        => db.AllKmProfiles().Single(p => string.Equals(p.Login, login, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <b>9 cột COMBO đi trọn vòng qua bảng <c>km_profiles</c></b> — đẩy lên rồi đọc lại không mất trường nào.
    /// <para>Test luật gộp thuần (<c>KmHoSoGopTests</c>) KHÔNG bắt được ca này: quên một cột trong câu INSERT
    /// hay trong <c>SelectHoSo</c> thì luật vẫn đúng, chỉ có giá trị rơi mất khi đi qua SQLite — và triệu chứng
    /// là "thông số combo tự quay về mặc định sau vài phút" (worker kéo bản rỗng của hub về).</para>
    /// </summary>
    [Fact]
    public void Combo_9Cot_DiTronVongQuaBangHub()
    {
        using var db = new HubDatabase(_dataDir);
        var mocChay = new DateTimeOffset(2026, 8, 21, 6, 30, 0, TimeSpan.Zero);
        var mocThu = new DateTimeOffset(2026, 8, 21, 6, 5, 0, TimeSpan.Zero);

        db.UpsertKmProfiles(Day(May1, HoSo(LoginA, Moi, ownerMachine: May1) with
        {
            ComboBat = true,
            ComboGioBatDau = "07:15",
            ComboGioKetThuc = "08:45",
            ComboLoai = 2,
            ComboMuaSl = 4,
            ComboGiam = 15,
            ComboGioiHan = 9,
            ComboLanChayCuoi = mocChay,
            ComboLanThuTuDong = mocThu,
        }));

        var p = LayRa(db, LoginA);
        Assert.True(p.ComboBat);
        Assert.Equal("07:15", p.ComboGioBatDau);
        Assert.Equal("08:45", p.ComboGioKetThuc);
        Assert.Equal(2, p.ComboLoai);
        Assert.Equal(4, p.ComboMuaSl);
        Assert.Equal(15, p.ComboGiam);
        Assert.Equal(9, p.ComboGioiHan);
        Assert.Equal(mocChay, p.ComboLanChayCuoi);
        Assert.Equal(mocThu, p.ComboLanThuTuDong);
    }

    /// <summary>Mốc combo của máy CŨ HƠN không kéo lùi mốc đã có trên hub (luật MAX, qua đúng đường HTTP-ish
    /// của <c>UpsertKmProfiles</c> chứ không phải gọi hàm gộp lẻ).</summary>
    [Fact]
    public void Combo_MocCuKhongKeoLuiMocMoi_TrenHub()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertKmProfiles(Day(May1, HoSo(LoginA, Cu) with { ComboLanChayCuoi = Moi, ComboLanThuTuDong = Moi }));
        db.UpsertKmProfiles(Day(May2, HoSo(LoginA, Cu) with { ComboLanChayCuoi = Cu, ComboLanThuTuDong = Cu }));

        var p = LayRa(db, LoginA);
        Assert.Equal(Moi, p.ComboLanChayCuoi);
        Assert.Equal(Moi, p.ComboLanThuTuDong);
    }

    /// <summary>
    /// <b>Chu kỳ mặc định 3 NGÀY phải sống sót qua trọn vòng đồng bộ hub</b> — cả khi bản đẩy lên KHÔNG nói gì
    /// về <c>so_ngay</c> (client cũ, hoặc JSON thiếu field).
    /// <para><b>Vì sao khoá</b> (phản biện 25/08/2026, NHẸ-5): ba nơi cùng khai một khái niệm —
    /// <c>KmConfigRepository.MacDinhSoNgay</c>, <c>KmProfileItem.SoNgay</c> và <c>DEFAULT</c> của cột
    /// <c>km_profiles.so_ngay</c>. Hai nơi sau từng còn đứng ở <b>7</b> sau lượt đổi 25/08/2026. Hành vi lúc đó
    /// vẫn đúng (client luôn gửi <c>so_ngay</c> tường minh), nhưng chỉ cần MỘT đường rơi vào mặc định là chu kỳ
    /// nhảy về 7 ngày — câm lặng, không log, và người dùng chỉ thấy "sao lâu thế mới chạy".</para>
    /// </summary>
    [Fact]
    public void SoNgayMacDinh_La3_QuaTronVongHub()
    {
        // DTO: bản chưa ai đặt gì (hub cũ / client cũ không gửi field) phải là 3.
        Assert.Equal(3, new KmProfileItem(LoginA).SoNgay);

        using var db = new HubDatabase(_dataDir);
        db.UpsertKmProfiles(Day(May1, new KmProfileItem(LoginA) { CfgUpdatedAt = Moi, OwnerMachine = May1 }));

        // …và đi qua cột `so_ngay` của SQLite rồi đọc lại vẫn là 3, không phải DEFAULT cũ 7.
        Assert.Equal(3, LayRa(db, LoginA).SoNgay);
    }

    [Fact]
    public void DayRoiDoc_TraDungBanDaGop()
    {
        using var db = new HubDatabase(_dataDir);

        Assert.Equal(1, db.UpsertKmProfiles(Day(May1,
            HoSo(LoginA, Moi, tuDong: true, phanTram: 25, ownerMachine: May1, lanChayCuoi: Cu))));

        var p = LayRa(db, LoginA);
        Assert.True(p.TuDong);
        Assert.Equal(25, p.PhanTram);
        Assert.Equal(May1, p.OwnerMachine);
        Assert.Equal(Moi, p.CfgUpdatedAt);
        Assert.Equal(Cu, p.LanChayCuoi);
        Assert.Null(p.LanThuTuDong);   // chưa ai thử ⇒ null, KHÔNG phải MinValue (xem MocVao)
    }

    /// <summary>Login LẠ ⇒ thêm dòng mới, KHÔNG đụng dòng đang có. Đây là vế "hub không thay trọn bảng".</summary>
    [Fact]
    public void LoginLa_TaoDongMoi_KhongDungDongKhac()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertKmProfiles(Day(May1, HoSo(LoginA, Moi, tuDong: true, phanTram: 25)));

        db.UpsertKmProfiles(Day(May2, HoSo(LoginB, Moi, phanTram: 40)));

        Assert.Equal(2, db.AllKmProfiles().Count);
        Assert.Equal(25, LayRa(db, LoginA).PhanTram);   // hồ sơ của máy khác CÒN NGUYÊN
        Assert.Equal(40, LayRa(db, LoginB).PhanTram);
    }

    /// <summary>Ca sống-chết của cả đợt: máy vừa bật lên sau mấy hôm tắt đẩy bản CŨ của nó lên. Hub phải giữ
    /// cấu hình mới hơn VÀ mốc chạy mới hơn — lùi mốc là đêm sau chạy KM trùng.</summary>
    [Fact]
    public void DayBanCuHon_HubGiuCauHinhMoi_VaMocChayKhongLui()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertKmProfiles(Day(May1,
            HoSo(LoginA, Moi, tuDong: true, phanTram: 25, ownerMachine: May1, lanChayCuoi: Moi)));

        db.UpsertKmProfiles(Day(May2,
            HoSo(LoginA, Cu, tuDong: false, phanTram: 5, ownerMachine: May2, lanChayCuoi: Cu)));

        var p = LayRa(db, LoginA);
        Assert.True(p.TuDong);
        Assert.Equal(25, p.PhanTram);
        Assert.Equal(May1, p.OwnerMachine);
        Assert.Equal(Moi, p.LanChayCuoi);
    }

    /// <summary>Vế đối chứng: bản MỚI HƠN thì phải vào được — thiếu vế này thì một bản "không bao giờ ghi đè"
    /// cũng xanh, mà bản đó biến hồ sơ hub thành bất biến sau lượt đẩy đầu tiên.</summary>
    [Fact]
    public void DayBanMoiHon_HubNhanCauHinhMoi()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertKmProfiles(Day(May1, HoSo(LoginA, Cu, phanTram: 5, ownerMachine: May1)));

        db.UpsertKmProfiles(Day(May2, HoSo(LoginA, Moi, tuDong: true, phanTram: 25, ownerMachine: May2)));

        var p = LayRa(db, LoginA);
        Assert.True(p.TuDong);
        Assert.Equal(25, p.PhanTram);
        Assert.Equal(May2, p.OwnerMachine);
    }

    /// <summary>Chiều còn lại của "hai nhóm, hai luật": máy khác có cấu hình MỚI HƠN (thắng nhóm cấu hình) mà
    /// mốc chạy CŨ HƠN ⇒ mốc TUYỆT ĐỐI không lùi. Không tách riêng ca này thì một bản "mốc chạy đi theo bản
    /// thắng cấu hình" vẫn xanh — mà bản đó chính là đường dẫn tới chạy KM trùng.</summary>
    [Fact]
    public void CauHinhMoiHonNhungMocChayCuHon_ThiMocVanKhongLui()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertKmProfiles(Day(May1, HoSo(LoginA, Cu, phanTram: 25, lanChayCuoi: Moi)));

        db.UpsertKmProfiles(Day(May2, HoSo(LoginA, Moi, phanTram: 5, lanChayCuoi: Cu)));

        var p = LayRa(db, LoginA);
        Assert.Equal(5, p.PhanTram);            // cấu hình: bản mới hơn thắng
        Assert.Equal(Moi, p.LanChayCuoi);       // mốc chạy: KHÔNG lùi
    }

    /// <summary>Mốc chạy của MÁY KHÁC vẫn góp lên được dù cấu hình của nó cũ hơn — hai nhóm trường, hai luật.</summary>
    [Fact]
    public void MayKhacGopMocChayMoi_DuCauHinhCuHon()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertKmProfiles(Day(May1, HoSo(LoginA, Moi, phanTram: 25, lanChayCuoi: Cu)));

        db.UpsertKmProfiles(Day(May2, HoSo(LoginA, Cu, phanTram: 5, lanChayCuoi: Moi)));

        var p = LayRa(db, LoginA);
        Assert.Equal(25, p.PhanTram);
        Assert.Equal(Moi, p.LanChayCuoi);
    }

    /// <summary>Cùng email viết hoa/thường khác nhau ở hai máy PHẢI về CÙNG một dòng. Tách đôi thì mỗi máy đọc
    /// một hồ sơ và cả cơ chế đồng bộ vô nghĩa — mà không có dấu hiệu gì ngoài "vẫn chạy trùng".</summary>
    [Fact]
    public void LoginKhacHoaThuong_VanLaMotDong()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertKmProfiles(Day(May1, HoSo(LoginA, Cu, phanTram: 5)));

        db.UpsertKmProfiles(Day(May2, HoSo(LoginA.ToUpperInvariant(), Moi, phanTram: 25)));

        Assert.Single(db.AllKmProfiles());
        Assert.Equal(25, LayRa(db, LoginA).PhanTram);
    }

    /// <summary>
    /// <b>[LỖI CHẶN PHÁT HÀNH của vòng 1 — đọc trước khi đụng <c>UpsertKmProfiles</c>]</b> Dòng
    /// <c>km_config</c> có từ đợt 18/08 mang cấu hình THẬT nhưng <c>cfg_updated_at</c> còn NULL
    /// (<c>ALTER TABLE ADD COLUMN</c> không backfill). Lượt đẩy ĐẦU TIÊN của nó lên hub KHÔNG được làm mất
    /// cấu hình đó.
    /// <para>Bản vòng 1 lấy <c>DocHoSoLocked(...) ?? new KmProfileItem(login)</c> làm "bản đang giữ" — một hồ
    /// sơ MẶC ĐỊNH TỔNG HỢP cũng có <c>CfgUpdatedAt = null</c> ⇒ <c>MoiHon(null, null) == false</c> ⇒ bản
    /// thắng là mặc định ⇒ hub ghi <c>TuDong=false, SoNgay=7, PhanTram=5</c>, giờ rỗng, và trả HTTP 200 mà
    /// không log một dòng nào. Từ đó là chuỗi bảy bước: máy B kéo bản rỗng về → người dùng mở form trên B để
    /// XEM rồi bấm Đóng → B đóng dấu cho bản rỗng → máy A kéo về trong ≤60 giây → <b>KM tắt trên mọi máy</b>.
    /// </para>
    /// </summary>
    [Fact]
    public void DongCu_ChuaTungSua_DayLenHubLanDau_KhongDuocMatCauHinh()
    {
        using var db = new HubDatabase(_dataDir);
        var cu = HoSo(LoginA, cfgUpdatedAt: null, tuDong: true, phanTram: 25, ownerMachine: May1) with
        {
            SoNgay = 3,
            GioBatDau = "08:00",
            GioKetThuc = "09:00",
        };

        Assert.Equal(1, db.UpsertKmProfiles(Day(May1, cu)));

        var p = LayRa(db, LoginA);
        Assert.True(p.TuDong, "TuDong bị nuốt — hub đã gộp với hồ sơ mặc định thay vì nhận trọn bản đầu tiên");
        Assert.Equal(3, p.SoNgay);
        Assert.Equal(25, p.PhanTram);
        Assert.Equal("08:00", p.GioBatDau);
        Assert.Equal("09:00", p.GioKetThuc);
        Assert.Equal(May1, p.OwnerMachine);
    }

    /// <summary>Ca ĐẦY ĐỦ bảy bước của lỗi trên, chạy trọn trên hub: dòng cũ (mốc null) đẩy lên → máy B kéo về
    /// → B đóng dấu bản nó vừa kéo (mô phỏng "mở form ra xem rồi bấm Đóng" của bản chưa có V5) → A kéo về.
    /// <c>TuDong</c> của A phải VẪN bật. Test này khoá cả vế 1 của V1 lẫn luật "mốc mới hơn mang trọn gói".</summary>
    [Fact]
    public void CaBayBuoc_DongCu_QuaMayB_RoiVeMayA_ThiTuDongVanBat()
    {
        using var db = new HubDatabase(_dataDir);
        var cu = HoSo(LoginA, cfgUpdatedAt: null, tuDong: true, phanTram: 25, ownerMachine: May1) with
        {
            SoNgay = 3,
            GioBatDau = "08:00",
            GioKetThuc = "09:00",
        };
        db.UpsertKmProfiles(Day(May1, cu));

        // Máy B kéo về ĐÚNG bản trên hub rồi đóng dấu nó (mốc mới hơn, chủ trì đổi sang B) — bản B mang theo
        // là bản NÓ VỪA KÉO, không phải một bản mặc định.
        var bKeoVe = LayRa(db, LoginA);
        db.UpsertKmProfiles(Day(May2, bKeoVe with { OwnerMachine = May2, CfgUpdatedAt = Moi }));

        var p = LayRa(db, LoginA);
        Assert.True(p.TuDong, "TuDong tắt sau vòng qua máy B — cấu hình đã bị nuốt ở đâu đó");
        Assert.Equal(3, p.SoNgay);
        Assert.Equal(25, p.PhanTram);
        Assert.Equal("08:00", p.GioBatDau);
        Assert.Equal(May2, p.OwnerMachine);   // chủ trì ĐỔI được: đây là ý muốn của người dùng
    }

    // ── Trần mốc TƯƠNG LAI (V4): máy sai đồng hồ không được khoá chết KM cả fleet ──

    /// <summary>Mốc chạy xa hơn <see cref="KmHoSoGop.BienMocTuongLai"/> ở tương lai (máy sai năm) bị BỎ — không
    /// bỏ thì <c>Max</c> giữ nó vĩnh viễn, mọi máy kéo về, và <c>DenGioChay</c> trả false tới tận năm đó.</summary>
    [Fact]
    public void MocChayQuaXaTuongLai_ThiBiBo()
    {
        using var db = new HubDatabase(_dataDir);
        var xa = DateTimeOffset.UtcNow.AddYears(2);

        db.UpsertKmProfiles(Day(May1, HoSo(LoginA, Moi, phanTram: 25, lanChayCuoi: xa)));

        var p = LayRa(db, LoginA);
        Assert.Null(p.LanChayCuoi);
        Assert.Equal(25, p.PhanTram);   // phần cấu hình KHÔNG bị đụng — chỉ mốc rác rụng
    }

    /// <summary>Vế đối chứng: mốc tương lai NẰM TRONG biên (lệch đồng hồ thật giữa hai máy) vẫn nhận. Thiếu vế
    /// này thì một bản "bỏ mọi mốc tương lai" cũng xanh, mà bản đó vứt luôn mốc của máy lệch giờ vài phút.</summary>
    [Fact]
    public void MocChayTuongLaiTrongBien_ThiVanNhan()
    {
        using var db = new HubDatabase(_dataDir);
        var gan = DateTimeOffset.UtcNow.AddHours(1);

        db.UpsertKmProfiles(Day(May1, HoSo(LoginA, Moi, lanChayCuoi: gan)));

        Assert.NotNull(LayRa(db, LoginA).LanChayCuoi);
    }

    // ── Z3 (phản biện vòng 5): mốc LanThuTuDong đi qua hub — vế chưa có MỘT phép đo nào ─────────

    /// <summary>
    /// <b><c>LanThuTuDong</c> KHÔNG được lùi khi một máy đẩy mốc CŨ HƠN lên.</b>
    /// <para>Mốc này là "tài khoản đã dùng lượt thử của khung đêm nay" — <c>KmLichTuDong.DenGioChay</c> đọc nó
    /// để quyết có bắn lượt KM tự động hay không. Lùi được là máy vừa bật lên sau mấy hôm tắt xoá dấu vết lượt
    /// thử của đêm nay khỏi hub, mọi máy kéo về, và <b>đêm nay chạy lượt KM lần thứ hai</b> — tạo chương trình
    /// khuyến mãi THẬT. Đây là vế mà đột biến của nghiệm thu vòng 5 đi qua mà không ai đỏ.</para>
    /// </summary>
    [Fact]
    public void DayMocThuTuDongCuHon_ThiHubGiuMocMoi()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertKmProfiles(Day(May1, HoSo(LoginA, Moi, phanTram: 25, lanThuTuDong: Moi)));

        db.UpsertKmProfiles(Day(May2, HoSo(LoginA, Cu, phanTram: 5, lanThuTuDong: Cu)));

        Assert.Equal(Moi, LayRa(db, LoginA).LanThuTuDong);
    }

    /// <summary>
    /// <b>Vế ĐỐI CHỨNG: <c>LanThuTuDong</c> MỚI HƠN thì phải TIẾN được.</b> Thiếu vế này thì một bản "luôn giữ
    /// mốc đang có" cũng xanh — mà bản đó nghĩa là máy A đốt lượt thử lúc 02:00 rồi hub không bao giờ biết, nên
    /// máy B 02:01 vẫn thấy "chưa ai thử" và chạy chồng lên.
    /// <para>Đẩy kèm mốc cấu hình CŨ HƠN, cố ý: mốc chạy phải tiến theo luật RIÊNG của nó, không đi theo bản
    /// thắng nhóm cấu hình (hai nhóm trường, hai luật).</para>
    /// </summary>
    [Fact]
    public void DayMocThuTuDongMoiHon_ThiHubTien_DuCauHinhCuHon()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertKmProfiles(Day(May1, HoSo(LoginA, Moi, phanTram: 25, lanThuTuDong: Cu)));

        db.UpsertKmProfiles(Day(May2, HoSo(LoginA, Cu, phanTram: 5, lanThuTuDong: Moi)));

        var p = LayRa(db, LoginA);
        Assert.Equal(Moi, p.LanThuTuDong);
        Assert.Equal(25, p.PhanTram);       // cấu hình: bản mốc sửa mới hơn vẫn thắng, không bị mốc chạy kéo theo
    }

    /// <summary>Hub CHƯA có mốc mà máy đẩy lên có ⇒ nhận (<c>null</c> thua mọi giá trị) — và mốc <c>LanChayCuoi</c>
    /// đứng cạnh KHÔNG bị lẫn sang: hai mốc là hai bánh cóc riêng.</summary>
    [Fact]
    public void HubChuaCoMocThuTuDong_ThiNhanMocMayDayLen_VaKhongLanSangMocChayCuoi()
    {
        using var db = new HubDatabase(_dataDir);
        db.UpsertKmProfiles(Day(May1, HoSo(LoginA, Cu, phanTram: 25)));

        db.UpsertKmProfiles(Day(May2, HoSo(LoginA, Moi, phanTram: 5, lanChayCuoi: Cu, lanThuTuDong: Moi)));

        var p = LayRa(db, LoginA);
        Assert.Equal(Moi, p.LanThuTuDong);
        Assert.Equal(Cu, p.LanChayCuoi);
    }

    /// <summary>Mốc <c>LanThuTuDong</c> quá xa tương lai (máy sai năm) bị BỎ — y luật của <c>LanChayCuoi</c>.
    /// Không lọc thì <c>Max</c> giữ nó vĩnh viễn và <c>DenGioChay</c> trả false tới tận năm đó trên cả fleet.</summary>
    [Fact]
    public void MocThuTuDongQuaXaTuongLai_ThiBiBo()
    {
        using var db = new HubDatabase(_dataDir);

        db.UpsertKmProfiles(Day(May1,
            HoSo(LoginA, Moi, phanTram: 25, lanThuTuDong: DateTimeOffset.UtcNow.AddYears(2))));

        var p = LayRa(db, LoginA);
        Assert.Null(p.LanThuTuDong);
        Assert.Equal(25, p.PhanTram);   // chỉ mốc rác rụng, cấu hình còn nguyên
    }

    [Fact]
    public void LoginRong_ThiBoQua_KhongDeDongRac()
    {
        using var db = new HubDatabase(_dataDir);
        Assert.Equal(0, db.UpsertKmProfiles(Day(May1, HoSo("   ", Moi))));
        Assert.Empty(db.AllKmProfiles());
    }

    [Fact]
    public void BangRong_ThiDocRaListRong_KhongNem()
    {
        using var db = new HubDatabase(_dataDir);
        Assert.Empty(db.AllKmProfiles());
    }

    // ── W7 + W8 (phản biện vòng 3, 22/08/2026) đi qua ĐÚNG lượt upsert của hub ──────────────

    /// <summary>
    /// <b>Mốc SỬA quá xa tương lai bị KẸP về "bây giờ" ngay trên hub, và cấu hình của bản đó KHÔNG bị vứt.</b>
    /// <para>Không kẹp thì một máy sai đồng hồ ghi mốc 2028 là bản của nó thắng vĩnh viễn, VÀ mốc ấy tự lan:
    /// mọi máy kéo về rồi ghi lại chính nó xuống <c>km_config</c>, nên xoá dòng trên hub cũng vô ích. Kẹp về
    /// "bây giờ" chứ không bỏ về <c>null</c> — <c>null</c> ở nhóm cấu hình nghĩa là "thua mọi giá trị", tức
    /// vứt trọn cấu hình.</para>
    /// </summary>
    [Fact]
    public void MocSuaQuaXaTuongLai_ThiHubKepVeBayGio()
    {
        using var db = new HubDatabase(_dataDir);
        var xa = DateTimeOffset.UtcNow.AddYears(2);

        db.UpsertKmProfiles(Day(May1, HoSo(LoginA, xa, tuDong: true, phanTram: 25, ownerMachine: May1)));

        var p = LayRa(db, LoginA);
        Assert.NotNull(p.CfgUpdatedAt);
        Assert.True(p.CfgUpdatedAt < DateTimeOffset.UtcNow.AddHours(1), "mốc sửa rác phải bị kẹp về gần 'bây giờ'");
        Assert.Equal(25, p.PhanTram);       // cấu hình còn nguyên
        Assert.True(p.TuDong);
    }

    /// <summary>
    /// <b>Ca W8 đi qua hub: hai máy nâng cấp cùng lúc nên mang CÙNG mốc backfill mà cấu hình khác nhau.</b>
    /// <para>Không phá hoà thì bản thắng là bản ĐẨY TRƯỚC (<c>MoiHon</c> dùng <c>&gt;</c> nên bằng nhau =
    /// thua): máy B khởi động trước ⇒ hub giữ bản mặc định của B, máy A đẩy lên không đổi được gì, A kéo về
    /// cũng không đổi được gì ⇒ hub / A / B ba bản khác nhau, ổn định vĩnh viễn. Ở đây phải ra bản của A (lịch
    /// đang BẬT thắng lịch tắt) bất kể ai đẩy trước.</para>
    /// </summary>
    [Fact]
    public void CungMocBackfill_KhacCauHinh_ThiBanBatKmThang_DuAiDayTruoc()
    {
        var mocBackfill = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        using (var db = new HubDatabase(_dataDir))
        {
            db.UpsertKmProfiles(Day(May2, HoSo(LoginA, mocBackfill, tuDong: false, phanTram: 5)));
            db.UpsertKmProfiles(Day(May1, HoSo(LoginA, mocBackfill, tuDong: true, phanTram: 25, ownerMachine: May1)));

            var p = LayRa(db, LoginA);
            Assert.True(p.TuDong);
            Assert.Equal(25, p.PhanTram);
            Assert.Equal(May1, p.OwnerMachine);
        }

        // Đảo THỨ TỰ đẩy: kết quả phải Y HỆT, không thì "ai khởi động trước" vẫn còn quyết định cấu hình.
        var dir2 = _dataDir + "-daonguoc";
        try
        {
            using var db2 = new HubDatabase(dir2);
            db2.UpsertKmProfiles(Day(May1, HoSo(LoginA, mocBackfill, tuDong: true, phanTram: 25, ownerMachine: May1)));
            db2.UpsertKmProfiles(Day(May2, HoSo(LoginA, mocBackfill, tuDong: false, phanTram: 5)));

            var p = LayRa(db2, LoginA);
            Assert.True(p.TuDong);
            Assert.Equal(25, p.PhanTram);
            Assert.Equal(May1, p.OwnerMachine);
        }
        finally
        {
            try { if (Directory.Exists(dir2)) Directory.Delete(dir2, recursive: true); } catch { }
        }
    }

    // ── HAI CỘT THÔNG SỐ FLASH (03/09/2026) ─────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Hai cột thông số flash đi trọn vòng qua bảng <c>km_profiles</c></b> — đẩy lên rồi đọc lại không mất
    /// trường nào.
    /// <para>Cùng lý do với bài <c>Combo_9Cot_DiTronVongQuaBangHub</c>: test luật gộp thuần KHÔNG bắt được ca
    /// quên một cột trong câu INSERT hay trong <c>SelectHoSo</c> — luật vẫn đúng, chỉ có giá trị rơi mất khi đi
    /// qua SQLite, và triệu chứng là "% giảm flash tự quay về mặc định sau vài phút".</para>
    /// <para>Số dùng CỐ Ý khác mặc định 5/10 — dùng đúng mặc định thì một bản vá quên hẳn hai tham số SQL vẫn
    /// đọc ra 5/10 (nhờ <c>DEFAULT</c> của cột) và xanh.</para>
    /// </summary>
    [Fact]
    public void Flash_HaiCotThamSo_DiTronVongQuaBangHub()
    {
        using var db = new HubDatabase(_dataDir);

        db.UpsertKmProfiles(Day(May1, HoSo(LoginA, Moi, ownerMachine: May1) with
        {
            FlashPhanTramGiam = 8,
            FlashSoLuongThamGia = 25,
        }));

        var p = LayRa(db, LoginA);
        Assert.Equal(8, p.FlashPhanTramGiam);
        Assert.Equal(25, p.FlashSoLuongThamGia);
    }

    /// <summary>
    /// <b>CLIENT CŨ không gửi hai trường ⇒ hàng trên hub nhận đúng MẶC ĐỊNH 5/10, không phải 0 và không phải
    /// NULL.</b>
    ///
    /// <para><b>0% giảm là một chương trình flash vô nghĩa mà Shopee vẫn nhận</b> — nên nếu hub ghi 0 vào cột
    /// thì mọi máy MỚI kéo bản đó về sẽ đặt flash 0% cho cả fleet, im lặng. Cột chỉ được có đúng MỘT cách biểu
    /// diễn "chưa ai đặt", và đó là hai con số đang chạy thật.</para>
    ///
    /// <para>Lượt đẩy thứ hai (một client MỚI) khẳng định thêm rằng cột thật sự ghi được, tức vế trên không xanh
    /// nhờ một bản vá "bỏ hẳn hai cột".</para>
    /// </summary>
    [Fact]
    public void Flash_ClientCuKhongGuiThamSo_ThiCotNhanMacDinh()
    {
        using var db = new HubDatabase(_dataDir);

        // Client CŨ: hồ sơ không mang hai trường ⇒ null trên DTO.
        var cu = HoSo(LoginA, Moi, ownerMachine: May1);
        Assert.Null(cu.FlashPhanTramGiam);
        Assert.Null(cu.FlashSoLuongThamGia);

        db.UpsertKmProfiles(Day(May1, cu));

        var p = LayRa(db, LoginA);
        Assert.Equal(KmProfileItem.MacDinhFlashPhanTramGiam, p.FlashPhanTramGiam);
        Assert.Equal(KmProfileItem.MacDinhFlashSoLuongThamGia, p.FlashSoLuongThamGia);

        // …và một client MỚI vẫn ghi đè được bằng số thật (mốc sửa mới hơn ⇒ gói của nó thắng).
        db.UpsertKmProfiles(Day(May2, HoSo(LoginA, Moi.AddHours(1), ownerMachine: May2) with
        {
            FlashPhanTramGiam = 12,
            FlashSoLuongThamGia = 30,
        }));
        var p2 = LayRa(db, LoginA);
        Assert.Equal(12, p2.FlashPhanTramGiam);
        Assert.Equal(30, p2.FlashSoLuongThamGia);
    }

    /// <summary>
    /// <b>⚠ CLIENT CŨ ĐẨY LÊN KHÔNG XOÁ ĐƯỢC CON SỐ ĐANG CHẠY.</b>
    ///
    /// <para><b>Ca hỏng mà bài này chặn</b> (phản biện vòng 2, 03/09/2026): máy MỚI đặt 8% lúc 10:00; máy CŨ
    /// (chưa có hai trường) sửa một thứ khác lúc 10:05 rồi đẩy lên. Bản cũ thắng mốc sửa, và bản đầu thay
    /// <c>null</c> bằng mặc định NGAY TẠI CHỖ GHI ⇒ cột thành 5 ⇒ mọi máy kéo về 5%, 60 giây sau, không một dòng
    /// log. Hai lớp phải cùng tôn trọng nghĩa của <c>null</c>: luật gộp (<c>nen.X ?? khac.X</c>) và câu upsert
    /// (<c>COALESCE($fptg, flash_phan_tram_giam)</c>).</para>
    ///
    /// <para>Mặc định 5/10 vì thế chỉ được dùng ở ĐÚNG một ca: dòng CHƯA TỒN TẠI (đo ở
    /// <see cref="Flash_ClientCuKhongGuiThamSo_ThiCotNhanMacDinh"/>).</para>
    /// </summary>
    [Fact]
    public void Flash_ClientCuDayLen_ThiKhongDeMatSoDangCo()
    {
        using var db = new HubDatabase(_dataDir);

        // Máy MỚI đặt 8%/25 lúc `Moi`.
        db.UpsertKmProfiles(Day(May2, HoSo(LoginA, Moi, ownerMachine: May2) with
        {
            FlashPhanTramGiam = 8,
            FlashSoLuongThamGia = 25,
        }));
        Assert.Equal(8, LayRa(db, LoginA).FlashPhanTramGiam);

        // Máy CŨ sửa sau đó (mốc MỚI HƠN ⇒ gói của nó thắng) nhưng KHÔNG biết hai trường này.
        var clientCu = HoSo(LoginA, Moi.AddHours(1), ownerMachine: May1);
        Assert.Null(clientCu.FlashPhanTramGiam);
        db.UpsertKmProfiles(Day(May1, clientCu));

        var p = LayRa(db, LoginA);
        Assert.Equal(8, p.FlashPhanTramGiam);
        Assert.Equal(25, p.FlashSoLuongThamGia);

        // …trong khi phần CẤU HÌNH mà client cũ THẬT SỰ có ý kiến thì vẫn thắng bình thường — không phải bài này
        // vô hiệu hoá luôn lượt đẩy của máy cũ.
        Assert.Equal(May1, p.OwnerMachine);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, recursive: true); } catch { }
    }
}
