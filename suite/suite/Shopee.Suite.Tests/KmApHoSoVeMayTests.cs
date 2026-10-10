// System.IO KHÔNG có trong danh sách implicit của SDK desktop (net8.0-windows) — thiếu là Path/File không tồn tại.
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Shopee.Core.Coordination;
using Shopee.Suite.Infrastructure;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;

namespace Shopee.Suite.Tests;

/// <summary>
/// <b>Bốn hàm GÁNH NẶNG NHẤT của worker đồng bộ hồ sơ KM</b> — <c>ApHoSoKmVeMay</c>, <c>ChupHoSoKm</c>,
/// <c>NenThuDayLai</c>, <c>KmSyncTickAsync</c>. Vòng 1 không có một dòng test nào cho cả bốn.
/// <para><b>Nguy nhất là <c>ApHoSoKmVeMay</c>:</b> nó TẠO dòng <c>km_config</c> mới cho tài khoản chưa từng mở
/// form — tức nó gánh trọn mục tiêu của cả đợt đồng bộ (máy B chưa ai đặt lịch phải nhận được lịch người dùng
/// đặt ở máy A). Không có assert nào khoá hành vi đó thì ai "tối ưu" nó thành <c>GetAll()</c> +
/// <c>TryGetValue</c> (đúng khuôn <c>ChupHoSoKm</c> chỉ cách 20 dòng, và trông hợp lý hơn hẳn) là mọi máy chưa
/// mở form VĨNH VIỄN không nhận hồ sơ — mà toàn bộ test của vòng 1 vẫn xanh.</para>
/// </summary>
public class KmApHoSoVeMayTests : IDisposable
{
    private const string LoginA = "sub-a@shopee.vn";
    private const string LoginB = "sub-b@shopee.vn";
    private const string MayA = "may-a:orders";

    /// <summary>Mốc "bây giờ" (giờ địa phương) — sau mọi mốc dựng cảnh, nên không ca nào chạm trần mốc tương lai
    /// trừ ca cố ý.</summary>
    private static readonly DateTime BayGio = new(2026, 8, 22, 10, 0, 0);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"suite_km_ap_{Guid.NewGuid():N}.db");
    private readonly AppServices _services;

    public KmApHoSoVeMayTests() => _services = new AppServices(_dbPath);

    private long ThemTaiKhoan(string email)
    {
        _services.Accounts.Insert(new Account { Email = email, Password = "p" });
        return _services.Accounts.GetAll().First(a => a.Email == email).Id;
    }

    private static KmProfileItem HoSo(
        string login, bool tuDong = false, int soNgay = 7, int phanTram = 5, string gioBatDau = "09:30",
        string ownerMachine = MayA, DateTimeOffset? cfgUpdatedAt = null,
        DateTimeOffset? lanChayCuoi = null, DateTimeOffset? lanThuTuDong = null)
        => new(login)
        {
            TuDong = tuDong,
            SoNgay = soNgay,
            PhanTram = phanTram,
            GioBatDau = gioBatDau,
            GioKetThuc = "10:30",
            OwnerMachine = ownerMachine,
            CfgUpdatedAt = cfgUpdatedAt ?? new DateTimeOffset(2026, 8, 21, 7, 0, 0, TimeSpan.Zero),
            LanChayCuoi = lanChayCuoi,
            LanThuTuDong = lanThuTuDong,
        };

    // ══════════════════════════ ApHoSoKmVeMay ══════════════════════════

    /// <summary>
    /// <b>Tài khoản CHƯA CÓ dòng <c>km_config</c> ⇒ PHẢI tạo dòng mới.</b> Đây là toàn bộ mục tiêu của đợt
    /// đồng bộ; đổi sang khuôn "chỉ đụng dòng đã có" là tính năng chết câm.
    /// </summary>
    [Fact]
    public void ChuaCoDong_ThiTaoDongMoi()
    {
        var id = ThemTaiKhoan(LoginA);
        Assert.False(_services.KmConfig.GetAll().ContainsKey(id), "cảnh phải bắt đầu từ 'chưa có dòng nào'");

        var doi = OrdersModuleHost.ApHoSoKmVeMay(
            _services, [HoSo(LoginA, tuDong: true, soNgay: 3, phanTram: 25, gioBatDau: "08:00")], BayGio);

        Assert.Equal(1, doi);
        Assert.True(_services.KmConfig.GetAll().ContainsKey(id), "phải có dòng THẬT trong bảng, không chỉ mặc định");
        var cf = _services.KmConfig.Get(id);
        Assert.True(cf.TuDong);
        Assert.Equal(3, cf.SoNgay);
        Assert.Equal(25, cf.PhanTram);
        Assert.Equal("08:00", cf.GioBatDau);
        Assert.Equal(MayA, cf.OwnerMachine);
    }

    /// <summary>Đã có dòng và lượt gộp KHÔNG đổi gì ⇒ KHÔNG ghi SQLite. Nhịp kéo 60 giây ghi đè một dòng y hệt
    /// là ghi đĩa vô ích, và làm mọi thứ đếm-lượt-ghi phía trên nhiễu.</summary>
    [Fact]
    public void DaCoDong_KhongDoiGi_ThiKhongGhi()
    {
        var id = ThemTaiKhoan(LoginA);
        var hub = HoSo(LoginA, tuDong: true, phanTram: 25);
        Assert.Equal(1, OrdersModuleHost.ApHoSoKmVeMay(_services, [hub], BayGio));

        Assert.Equal(0, OrdersModuleHost.ApHoSoKmVeMay(_services, [hub], BayGio));
        Assert.Equal(25, _services.KmConfig.Get(id).PhanTram);
    }

    /// <summary>Login trên hub mà máy này KHÔNG có tài khoản ⇒ bỏ qua, không tạo tài khoản mới. Danh bạ tài
    /// khoản là việc của worker gương; hồ sơ KM không được lấn sân.</summary>
    [Fact]
    public void LoginLaTrenHub_ThiBoQua_KhongDeTaiKhoanMoi()
    {
        ThemTaiKhoan(LoginA);

        var doi = OrdersModuleHost.ApHoSoKmVeMay(_services, [HoSo(LoginB, tuDong: true)], BayGio);

        Assert.Equal(0, doi);
        Assert.Single(_services.Accounts.GetAll());
        Assert.Empty(_services.KmConfig.GetAll());
    }

    /// <summary>Khớp theo login KHÔNG phân biệt hoa/thường — mỗi máy tự tạo bản ghi tài khoản nên chữ hoa/thường
    /// lệch nhau là chuyện thường, và lệch thì máy đó không bao giờ nhận hồ sơ.</summary>
    [Fact]
    public void KhopLogin_KhongPhanBietHoaThuong()
    {
        var id = ThemTaiKhoan(LoginA);

        OrdersModuleHost.ApHoSoKmVeMay(_services, [HoSo(LoginA.ToUpperInvariant(), tuDong: true)], BayGio);

        Assert.True(_services.KmConfig.Get(id).TuDong);
    }

    [Fact]
    public void DanhSachRong_ThiKhongLamGi()
        => Assert.Equal(0, OrdersModuleHost.ApHoSoKmVeMay(_services, [], BayGio));

    // ══════════════════════════ V3: nhật ký khi cấu hình ĐỔI ══════════════════════

    /// <summary>
    /// Lượt gộp làm ĐỔI cấu hình ⇒ ĐÚNG MỘT dòng nhật ký, nêu máy chủ trì + mốc sửa + tự động on/off + chu kỳ
    /// + %. Trước bản vá này cả ba tầng (client A, client B, hub) đều im lặng, nên "cấu hình tự nhiên biến
    /// mất" là thứ không thể chẩn đoán từ nhật ký.
    /// </summary>
    [Fact]
    public void DoiCauHinh_ThiGhiDungMotDongNhatKy()
    {
        ThemTaiKhoan(LoginA);

        OrdersModuleHost.ApHoSoKmVeMay(
            _services, [HoSo(LoginA, tuDong: true, soNgay: 3, phanTram: 25, gioBatDau: "08:00")], BayGio);

        var dong = _services.Log.Snapshot(LoginA).Where(e => e.Message.Contains("cấu hình vừa ĐỔI")).ToList();
        Assert.Single(dong);
        // Máy chủ trì hiện bằng NHÃN người đọc được (tên + id NGẮN), không phải id thô — X6, vòng 4. Không tra
        // được tên (test không có hub) thì chỉ còn id ngắn, và đuôi ":orders" phải biến mất.
        Assert.Contains(XuLyDonShopee.Core.Services.KmLichTuDong.NhanMay(MayA, null), dong[0].Message);
        Assert.DoesNotContain(MayA, dong[0].Message);
        Assert.Contains("21-08-2026", dong[0].Message);    // mốc sửa (đổi về giờ địa phương)
        Assert.Contains("mỗi 3 ngày", dong[0].Message);
        Assert.Contains("25%", dong[0].Message);
    }

    /// <summary>
    /// <b>Lượt gộp KHÔNG đổi cấu hình ⇒ KHÔNG log.</b> Worker kéo mỗi 60 giây; log vô điều kiện là 1440 dòng
    /// rác mỗi ngày cho mỗi tài khoản, và nhật ký spam thì người dùng thôi đọc — hỏng đúng mục đích của V3.
    /// </summary>
    [Fact]
    public void KhongDoiCauHinh_ThiKhongLog()
    {
        ThemTaiKhoan(LoginA);
        var hub = HoSo(LoginA, tuDong: true, phanTram: 25);
        OrdersModuleHost.ApHoSoKmVeMay(_services, [hub], BayGio);
        _services.Log.Clear(LoginA);

        OrdersModuleHost.ApHoSoKmVeMay(_services, [hub], BayGio);
        OrdersModuleHost.ApHoSoKmVeMay(_services, [hub], BayGio);

        Assert.Empty(_services.Log.Snapshot(LoginA));
    }

    /// <summary>Chỉ MỐC CHẠY tiến lên (cấu hình y nguyên) ⇒ có ghi DB nhưng KHÔNG log: mỗi lượt KM chạy xong là
    /// một lần mốc tiến, log hết thì lại thành spam.</summary>
    [Fact]
    public void ChiMocChayTien_ThiGhiDbNhungKhongLog()
    {
        ThemTaiKhoan(LoginA);
        var hub = HoSo(LoginA, tuDong: true, phanTram: 25);
        OrdersModuleHost.ApHoSoKmVeMay(_services, [hub], BayGio);
        _services.Log.Clear(LoginA);

        var doi = OrdersModuleHost.ApHoSoKmVeMay(
            _services,
            [hub with { LanChayCuoi = new DateTimeOffset(2026, 8, 22, 2, 30, 0, TimeSpan.Zero) }],
            BayGio);

        Assert.Equal(1, doi);
        Assert.Empty(_services.Log.Snapshot(LoginA));
    }

    // ══════════════════════════ V4: mốc rác phải NÓI RA ══════════════════════════

    /// <summary>Mốc chạy quá xa tương lai bị luật gộp bỏ ⇒ phải có dòng nhật ký nêu rõ, kèm gợi ý "kiểm đồng hồ
    /// máy". Bỏ âm thầm thì "KM tự nhiên không chạy nữa" lại là ca không có dấu vết nào.</summary>
    [Fact]
    public void MocRacTuHub_ThiBoVaGhiNhatKy()
    {
        var id = ThemTaiKhoan(LoginA);
        var xa = new DateTimeOffset(BayGio.AddYears(2), TimeSpan.Zero);

        // Sổ cảnh báo RIÊNG: sổ mặc định là static dùng chung cả assembly, mà cảnh báo bị chặn lặp theo NGÀY —
        // dùng chung thì thứ tự chạy test quyết định ca này xanh hay đỏ.
        OrdersModuleHost.ApHoSoKmVeMay(
            _services, [HoSo(LoginA, tuDong: true, lanChayCuoi: xa)], BayGio, new());

        Assert.Null(_services.KmConfig.Get(id).LanChayCuoi);
        Assert.Contains(_services.Log.Snapshot(LoginA), e => e.Message.Contains("quá xa ở TƯƠNG LAI"));
    }

    /// <summary>Vế đối chứng: mốc bình thường KHÔNG sinh dòng cảnh báo nào.</summary>
    [Fact]
    public void MocBinhThuong_ThiKhongCanhBao()
    {
        ThemTaiKhoan(LoginA);

        OrdersModuleHost.ApHoSoKmVeMay(
            _services,
            [HoSo(LoginA, tuDong: true, lanChayCuoi: new DateTimeOffset(2026, 8, 20, 2, 30, 0, TimeSpan.Zero))],
            BayGio);

        Assert.DoesNotContain(_services.Log.Snapshot(LoginA), e => e.Message.Contains("quá xa ở TƯƠNG LAI"));
    }

    // ══════════════════════════ ChupHoSoKm ══════════════════════════

    /// <summary>Chỉ chụp tài khoản ĐÃ CÓ DÒNG <c>km_config</c> — đẩy bản mặc định của mọi tài khoản lên hub là
    /// tạo hồ sơ rác cho cả fleet, và làm mờ đúng thứ cần nhìn.</summary>
    [Fact]
    public void ChupHoSoKm_ChiLayTaiKhoanDaCoDong()
    {
        var idA = ThemTaiKhoan(LoginA);
        ThemTaiKhoan(LoginB);   // chưa ai đặt lịch cho B
        _services.KmConfig.Save(new KmConfig(idA, TuDong: true, SoNgay: 3, PhanTram: 25,
            GioBatDau: "08:00", GioKetThuc: "09:00", LanChayCuoi: null));

        var items = OrdersModuleHost.ChupHoSoKm(_services);

        var item = Assert.Single(items);
        Assert.Equal(LoginA, item.Login);
        Assert.True(item.TuDong);
        Assert.Equal(25, item.PhanTram);
    }

    /// <summary>Tài khoản KHÔNG có login (bản ghi dở dang) thì bỏ — khoá trên hub là login, không có login thì
    /// không định danh xuyên máy được, và đẩy lên với login rỗng là đẻ một dòng rác dùng chung.</summary>
    [Fact]
    public void ChupHoSoKm_BoTaiKhoanKhongCoLogin()
    {
        _services.Accounts.Insert(new Account { Email = "   ", Password = "p" });
        var id = _services.Accounts.GetAll().Single().Id;
        _services.KmConfig.Save(new KmConfig(id, TuDong: true, SoNgay: 7, PhanTram: 5,
            GioBatDau: "09:30", GioKetThuc: "10:30", LanChayCuoi: null));

        Assert.Empty(OrdersModuleHost.ChupHoSoKm(_services));
    }

    [Fact]
    public void ChupHoSoKm_KhongCoDongNao_ThiRong()
    {
        ThemTaiKhoan(LoginA);
        Assert.Empty(OrdersModuleHost.ChupHoSoKm(_services));
    }

    // ══════════════════════════ NenThuDayLai ══════════════════════════

    /// <summary>Lượt đẩy gần nhất KHÔNG hỏng ⇒ thay đổi mới đi NGAY (không bắt người dùng chờ 60 giây).</summary>
    [Fact]
    public void NenThuDayLai_ChuaHongLanNao_ThiDiNgay()
        => Assert.True(OrdersModuleHost.NenThuDayLai(DateTimeOffset.MinValue, DateTimeOffset.UtcNow));

    /// <summary>Vừa hỏng ⇒ CHỜ, không thử lại ngay. Đây là thứ chặn "hub cũ chưa có route ăn một request mỗi 3
    /// giây, mãi mãi" — cờ bẩn được bật lại sau mỗi lượt đẩy hỏng nên không có mốc này là vòng lặp vô hạn.</summary>
    [Fact]
    public void NenThuDayLai_VuaHong_ThiCho()
    {
        var luc = new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
        Assert.False(OrdersModuleHost.NenThuDayLai(luc, luc.AddSeconds(3)));
        Assert.False(OrdersModuleHost.NenThuDayLai(luc, luc.AddSeconds(59)));
    }

    /// <summary>Hỏng nhưng đã đủ 60 giây ⇒ thử lại — không thì một lượt hub chập chờn khoá luôn việc đẩy tới
    /// lần ghi <c>km_config</c> kế tiếp, có thể là hôm sau.</summary>
    [Fact]
    public void NenThuDayLai_HongDaLau_ThiThuLai()
    {
        var luc = new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);
        Assert.True(OrdersModuleHost.NenThuDayLai(luc, luc.AddSeconds(60)));
        Assert.True(OrdersModuleHost.NenThuDayLai(luc, luc.AddMinutes(5)));
    }

    // ══════════════════════════ KmSyncTickAsync: nhánh DEGRADE ══════════════════════

    /// <summary>
    /// <b>Hub chưa cấu hình (<c>CoordinationRuntime.Client == null</c>) ⇒ nhịp worker KHÔNG đụng
    /// <c>km_config</c>.</b> App Đơn hàng chạy độc lập / máy chưa nối hub phải hoạt động y như trước; một nhịp
    /// vô tình ghi đè cấu hình bằng bản mặc định ở đây là mất dữ liệu người dùng mà chẳng cần tới mạng.
    /// </summary>
    [Fact]
    public async Task ChuaCauHinhHub_ThiNhipWorkerKhongDungKmConfig()
    {
        Assert.Null(CoordinationRuntime.Client);   // cảnh mặc định của test host
        var id = ThemTaiKhoan(LoginA);
        var cu = new KmConfig(id, TuDong: true, SoNgay: 3, PhanTram: 25, GioBatDau: "08:00",
            GioKetThuc: "09:00", LanChayCuoi: null);
        _services.KmConfig.Save(cu);

        await OrdersModuleHost.KmSyncTickAsync(_services);

        Assert.Equal(cu with { CfgUpdatedAt = null }, _services.KmConfig.Get(id) with { CfgUpdatedAt = null });
    }

    public void Dispose()
    {
        _services.Log.Dispose();
        using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString()))
        {
            SqliteConnection.ClearPool(conn);
        }
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { }
        GC.SuppressFinalize(this);
    }
}
