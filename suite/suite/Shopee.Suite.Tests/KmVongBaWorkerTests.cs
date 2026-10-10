// System.IO KHÔNG có trong danh sách implicit của SDK desktop (net8.0-windows) — thiếu là Path/File không tồn tại.
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Shopee.Core.Coordination;
using Shopee.Suite.Infrastructure;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace Shopee.Suite.Tests;

/// <summary>
/// <b>Ba hạng mục của phản biện vòng 3 nằm ở tầng worker</b> (22/08/2026): cảnh NÂNG CẤP THẬT của cửa chủ trì
/// (W3), cảnh báo mốc rác phải bắn ở ca ỔN ĐỊNH (W6) + dòng log của lượt phá hoà (W8), và teardown dừng nhịp
/// hồ sơ KM (W9).
/// <para><b>Vì sao phải dựng ĐÚNG cảnh nâng cấp:</b> test vòng 2 của cửa chủ trì tự tay <c>Save</c> một dòng đã
/// có <c>OwnerMachine</c> — cảnh đó KHÔNG tồn tại trên máy người dùng ngày đầu nâng cấp, vì
/// <c>ALTER TABLE ADD COLUMN</c> để cột rỗng và đường lưu form không ghi gì khi người dùng không đổi gì. Cảnh
/// thật là: DB cũ → migration → backfill → đẩy → máy hai kéo về.</para>
/// </summary>
public class KmVongBaWorkerTests : IDisposable
{
    private const string LoginA = "sub-vb@shopee.vn";
    private const string MayMot = "may-mot:orders";
    private const string MayHai = "may-hai:orders";

    /// <summary>Trong khung KM 2–6h ⇒ lượt tự động tới hạn (dòng cũ có <c>lan_chay_cuoi</c> tháng trước).</summary>
    private static readonly DateTime TrongKhung = new(2026, 8, 22, 2, 5, 0);

    private static readonly DateTime BayGio = new(2026, 8, 22, 10, 0, 0);

    private readonly List<string> _dbPaths = new();
    private readonly List<AppServices> _services = new();

    /// <summary>Dựng bảng <c>km_config</c> theo schema bản 19/08 (CHƯA có hai cột đồng bộ) + một dòng ĐANG BẬT
    /// KM — đúng thứ nằm trên máy người dùng trước lượt nâng cấp này.</summary>
    private static void DungDbCu(string path, long accountId)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
CREATE TABLE km_config (
    account_id      INTEGER PRIMARY KEY,
    tu_dong         INTEGER NOT NULL DEFAULT 0,
    so_ngay         INTEGER NOT NULL DEFAULT 7,
    phan_tram       INTEGER NOT NULL DEFAULT 5,
    gio_bat_dau     TEXT NOT NULL DEFAULT '09:30',
    gio_ket_thuc    TEXT NOT NULL DEFAULT '10:30',
    lan_chay_cuoi   TEXT,
    lan_thu_tu_dong TEXT
);";
            cmd.ExecuteNonQuery();
        }
        using var ins = conn.CreateCommand();
        ins.CommandText = @"INSERT INTO km_config
                                (account_id, tu_dong, so_ngay, phan_tram, gio_bat_dau, gio_ket_thuc, lan_chay_cuoi)
                            VALUES ($id, 1, 3, 25, '09:30', '10:30', '2026-07-18T02:30:00.0000000');";
        ins.Parameters.AddWithValue("$id", accountId);
        ins.ExecuteNonQuery();
    }

    /// <summary>Một "máy": file DB riêng + bộ dịch vụ riêng. <paramref name="dungDbCu"/> = máy đã dùng tính
    /// năng KM từ trước (có dòng <c>km_config</c> cũ); false = máy chưa ai mở form.</summary>
    private (AppServices Services, long AccountId) DungMay(bool dungDbCu)
    {
        var path = Path.Combine(Path.GetTempPath(), $"suite_km_vb_{Guid.NewGuid():N}.db");
        _dbPaths.Add(path);

        // Dòng km_config cũ thuộc account_id 1 = tài khoản ĐẦU TIÊN được thêm ở dưới (accounts AUTOINCREMENT).
        if (dungDbCu) { DungDbCu(path, accountId: 1); }

        var services = new AppServices(path);   // ctor chạy Initialize() ⇒ migration + backfill mốc sửa
        _services.Add(services);

        // Máy CHƯA có dữ liệu KM: chèn một tài khoản đệm trước, để account_id của LoginA LỆCH so với máy kia —
        // đúng thực tế (mỗi máy tự tạo bản ghi tài khoản) và là lý do khoá xuyên máy phải là LOGIN.
        if (!dungDbCu) { services.Accounts.Insert(new Account { Email = "dem@shopee.vn", Password = "p" }); }

        services.Accounts.Insert(new Account { Email = LoginA, Password = "p" });
        var id = services.Accounts.GetAll().First(a => a.Email == LoginA).Id;
        return (services, id);
    }

    // ══════════════════════════ W3 — cảnh NÂNG CẤP THẬT ══════════════════════════

    /// <summary>
    /// <b>Máy MỘT nâng cấp từ DB cũ (đang bật KM) ⇒ backfill đóng dấu nó làm chủ trì ⇒ máy HAI kéo hồ sơ về thì
    /// ĐỨNG NGOÀI.</b>
    /// <para>Không có lượt backfill <c>owner_machine</c> thì hồ sơ đi qua hub mang chủ trì RỖNG, mà rỗng nghĩa
    /// là "ai cũng được" ⇒ cả hai máy cùng chạy lượt KM lúc 02:00 đêm đầu — đúng cuộc đua mà cửa chủ trì sinh
    /// ra để chặn. Cảnh ở đây KHÔNG tự tay <c>Save</c> một chủ trì nào; nó đi đúng đường của người dùng thật.</para>
    /// <para>Lượt "đẩy lên hub rồi máy hai kéo về" mô phỏng bằng chính <see cref="KmHoSoGop.Gop"/> — hub gọi
    /// đúng hàm này (file nguồn được LINK sang <c>Shopee.Hub.Web</c>) và với dòng CHƯA TỒN TẠI thì
    /// <c>UpsertKmProfiles</c> truyền <c>dangCo = den</c>, tức lượt đẩy đầu tiên là phép đồng nhất.</para>
    /// </summary>
    [StaFact]
    public async Task CanhNangCap_MayHaiKeoHoSoVe_ThiKhongChayLuotKm()
    {
        var (mot, _) = DungMay(dungDbCu: true);
        mot.MayNay = () => MayMot;
        Assert.Equal(1, mot.KmConfig.BackfillOwnerMachineOnce(MayMot));

        var day = OrdersModuleHost.ChupHoSoKm(mot);
        var tuHub = Assert.Single(day);
        Assert.Equal(MayMot, tuHub.OwnerMachine);

        // Máy HAI: chưa ai mở form ở đây, hub trả về đúng hồ sơ máy một vừa đẩy.
        var (hai, idHai) = DungMay(dungDbCu: false);
        hai.MayNay = () => MayHai;
        var soLanXinKhoa = 0;
        hai.AcquireAccountLease = (login, ct) =>
        {
            soLanXinKhoa++;
            return Task.FromResult(new OrdersLeaseResult(false, "MAY-GIU"));
        };
        hai.ReleaseAccountLease = login => Task.CompletedTask;

        OrdersModuleHost.ApHoSoKmVeMay(hai, [tuHub], BayGio, new());

        var cf = hai.KmConfig.Get(idHai);
        Assert.True(cf.TuDong);                       // lịch ĐÃ đồng bộ sang máy hai…
        Assert.Equal(MayMot, cf.OwnerMachine);        // …nhưng chủ trì là máy một
        Assert.False(KmLichTuDong.LaMayChuTri(cf.OwnerMachine, MayHai));

        await new AccountsViewModel(hai).NhipLichKmAsync(TrongKhung);

        Assert.Null(hai.KmConfig.Get(idHai).LanThuTuDong);   // KHÔNG đốt lượt thử của máy một
        Assert.Equal(0, soLanXinKhoa);                       // và KHÔNG vào thân lượt KM
    }

    /// <summary>Vế ĐỐI CHỨNG — chính MÁY MỘT (máy chủ trì sau backfill) vẫn chạy lượt tự động. Thiếu vế này thì
    /// một bản "sau nâng cấp không máy nào chạy" cũng xanh, mà bản đó tắt hẳn tính năng.</summary>
    [StaFact]
    public async Task CanhNangCap_ChinhMayChuTri_ThiVanChay()
    {
        var (mot, id) = DungMay(dungDbCu: true);
        mot.MayNay = () => MayMot;
        mot.KmConfig.BackfillOwnerMachineOnce(MayMot);
        var soLanXinKhoa = 0;
        mot.AcquireAccountLease = (login, ct) =>
        {
            soLanXinKhoa++;
            return Task.FromResult(new OrdersLeaseResult(false, "MAY-GIU"));
        };
        mot.ReleaseAccountLease = login => Task.CompletedTask;

        await new AccountsViewModel(mot).NhipLichKmAsync(TrongKhung);

        Assert.Equal(TrongKhung, mot.KmConfig.Get(id).LanThuTuDong);
        Assert.Equal(1, soLanXinKhoa);
    }

    /// <summary>
    /// <b>Chính WORKER phải chạy backfill chủ trì</b> — không chỉ hàm backfill đúng, mà còn phải có người GỌI
    /// nó, và gọi TRƯỚC lượt đẩy đầu tiên.
    /// <para>Tầng <c>XuLyDonShopee.Core</c> không biết máy này là máy nào (id suất đơn hàng do shell suite rót
    /// qua <c>AppServices.MayNay</c>), nên lượt backfill này KHÔNG nằm trong <c>Database.Initialize</c> được —
    /// tức nó là một chỗ nối, và chỗ nối không có test thì đứt lúc nào không ai biết. (Lượt thử phá bỏ đúng
    /// dòng gọi này: mọi test khác vẫn xanh.)</para>
    /// <para><b>Từ vòng 4 phải nói rõ CÓ HUB</b> (X4): máy không hub thì lượt backfill KHÔNG được chạy — xem
    /// <c>KmVongBonTests.KhongCoHub_ThiWireKmSyncKhongBackfillChuTri_VaKhongDongLatch</c>.</para>
    /// <para><b>Từ vòng 6 chỗ nối đó là NHỊP, không còn là lượt wire</b> (Z1b): lượt wire chạy đúng một lần lúc
    /// khởi tạo module, nên máy cấu hình hub SAU khởi động vĩnh viễn không backfill. Test này đổi vế đo theo —
    /// vẫn hỏi đúng một câu ("worker có backfill dòng cũ không"), chỉ gõ cửa đúng chỗ hàm nay đứng; ca "hub bật
    /// lên giữa chừng" nằm ở <c>KmVongSauTests</c>.</para>
    /// </summary>
    [Fact]
    public async Task WorkerChayBackfillChuTriChoDongCu()
    {
        var (svc, id) = DungMay(dungDbCu: true);
        Assert.Equal("", svc.KmConfig.Get(id).OwnerMachine);

        try
        {
            OrdersModuleHost.WireKmSync(svc, coHub: () => true);
            await OrdersModuleHost.KmSyncTickAsync(svc);

            var may = svc.MayNay!();
            Assert.NotEqual("", may);
            Assert.Equal(may, svc.KmConfig.Get(id).OwnerMachine);
        }
        finally
        {
            OrdersModuleHost.DungNhipHoSoKm();
        }
    }

    // ══════════════════════════ W6 — cảnh báo phải bắn ở ca ỔN ĐỊNH ══════════════════════════

    /// <summary>
    /// <b>Máy ĐÃ CÓ dòng khớp sẵn + hồ sơ hub mang mốc rác ⇒ vẫn phải có ĐÚNG MỘT dòng cảnh báo mỗi ngày.</b>
    /// <para>Đây là ca cảnh báo sinh ra để bắn, và là ca bản vòng 2 KHÔNG bao giờ bắn: mốc rác bị luật gộp bỏ
    /// nên kết quả gộp bằng đúng bản đang có ⇒ <c>moi == cu</c> ⇒ <c>continue</c> ⇒ im lặng, lặp lại mỗi 60
    /// giây mãi mãi. Test vòng 2 xanh chỉ vì nó dựng cảnh tài khoản CHƯA CÓ DÒNG.</para>
    /// </summary>
    [Fact]
    public void MocRac_MayDaCoDongKhopSan_ThiVanCanhBao_VaChiMotDongMoiNgay()
    {
        var (svc, id) = DungMay(dungDbCu: false);
        var cu = new KmConfig(id, TuDong: true, SoNgay: 3, PhanTram: 25, GioBatDau: "08:00",
            GioKetThuc: "09:00", LanChayCuoi: null)
        {
            CfgUpdatedAt = new DateTime(2026, 8, 21, 9, 0, 0),
            OwnerMachine = MayMot,
        };
        svc.KmConfig.Save(cu);

        // Hồ sơ hub: Y HỆT dòng đang có, chỉ thêm một mốc chạy rác (máy khác sai đồng hồ).
        var tuHub = new KmProfileItem(LoginA)
        {
            TuDong = true,
            SoNgay = 3,
            PhanTram = 25,
            GioBatDau = "08:00",
            GioKetThuc = "09:00",
            OwnerMachine = MayMot,
            CfgUpdatedAt = KmMocThoiGian.LenDay(cu.CfgUpdatedAt),
            LanChayCuoi = new DateTimeOffset(BayGio.AddYears(2), TimeSpan.Zero),
        };

        var so = new Dictionary<string, DateTime>();
        var doi = OrdersModuleHost.ApHoSoKmVeMay(svc, [tuHub], BayGio, so);
        OrdersModuleHost.ApHoSoKmVeMay(svc, [tuHub], BayGio.AddMinutes(1), so);
        OrdersModuleHost.ApHoSoKmVeMay(svc, [tuHub], BayGio.AddMinutes(2), so);

        Assert.Equal(0, doi);   // đúng cảnh ỔN ĐỊNH: không dòng nào bị ghi lại
        Assert.Single(svc.Log.Snapshot(LoginA).Where(e => e.Message.Contains("quá xa ở TƯƠNG LAI")));

        // NGÀY SAU thì được nói lại — sự cố còn nguyên, người đọc hôm sau cũng cần biết.
        OrdersModuleHost.ApHoSoKmVeMay(svc, [tuHub], BayGio.AddDays(1), so);
        Assert.Equal(2, svc.Log.Snapshot(LoginA).Count(e => e.Message.Contains("quá xa ở TƯƠNG LAI")));
    }

    /// <summary>Câu cảnh báo phải nêu CẢ HAI khả năng (W11): máy khác nhanh giờ, hoặc CHÍNH MÁY NÀY chậm giờ —
    /// vế thứ hai mới là vế đổi một kiểu chết (KM không chạy) lấy một kiểu chạy trùng.</summary>
    [Fact]
    public void CauMocRac_NeuCaHaiKhaNangSaiDongHo()
    {
        var cau = OrdersModuleHost.CauMocRac("lần chạy cuối", new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Contains("CHÍNH MÁY NÀY", cau);
        Assert.Contains("ĐÃ BỎ", cau);
    }

    // ══════════════════════════ W8 — lượt PHÁ HOÀ phải để lại dấu vết ══════════════════════════

    /// <summary>
    /// Lượt gộp phải PHÁ HOÀ (cùng mốc sửa, khác cấu hình) ⇒ ghi ĐÚNG MỘT dòng nhật ký mỗi ngày. Lượt này quyết
    /// bằng một luật người dùng không hề gõ ra (ưu tiên lịch đang bật, rồi id máy nhỏ hơn) nên im lặng ở đây là
    /// để lại một thay đổi cấu hình không ai giải thích nổi.
    /// </summary>
    [Fact]
    public void PhaHoa_ThiGhiDungMotDongNhatKyMoiNgay()
    {
        var (svc, id) = DungMay(dungDbCu: false);
        var mocChung = new DateTime(2026, 8, 21, 9, 0, 0);
        svc.KmConfig.Save(new KmConfig(id, TuDong: false, SoNgay: 7, PhanTram: 5, GioBatDau: "09:30",
            GioKetThuc: "10:30", LanChayCuoi: null) { CfgUpdatedAt = mocChung, OwnerMachine = "" });

        var tuHub = new KmProfileItem(LoginA)
        {
            TuDong = true,
            SoNgay = 3,
            PhanTram = 25,
            GioBatDau = "08:00",
            GioKetThuc = "09:00",
            OwnerMachine = MayMot,
            CfgUpdatedAt = KmMocThoiGian.LenDay(mocChung),
        };

        var so = new Dictionary<string, DateTime>();
        OrdersModuleHost.ApHoSoKmVeMay(svc, [tuHub], BayGio, so);
        OrdersModuleHost.ApHoSoKmVeMay(svc, [tuHub], BayGio.AddMinutes(1), so);

        Assert.Single(svc.Log.Snapshot(LoginA).Where(e => e.Message.Contains("CÙNG mốc sửa")));
        Assert.Equal(25, svc.KmConfig.Get(id).PhanTram);   // bản BẬT lịch thắng
    }

    /// <summary>Vế ĐỐI CHỨNG — mốc sửa KHÁC nhau thì KHÔNG có dòng phá hoà nào (luật "sửa sau thắng" đã đủ giải
    /// thích, thêm dòng nữa là nhiễu).</summary>
    [Fact]
    public void MocSuaKhacNhau_ThiKhongCoDongPhaHoa()
    {
        var (svc, id) = DungMay(dungDbCu: false);
        svc.KmConfig.Save(new KmConfig(id, TuDong: false, SoNgay: 7, PhanTram: 5, GioBatDau: "09:30",
            GioKetThuc: "10:30", LanChayCuoi: null) { CfgUpdatedAt = new DateTime(2026, 8, 20, 9, 0, 0) });

        var tuHub = new KmProfileItem(LoginA)
        {
            TuDong = true,
            SoNgay = 3,
            PhanTram = 25,
            GioBatDau = "08:00",
            GioKetThuc = "09:00",
            OwnerMachine = MayMot,
            CfgUpdatedAt = KmMocThoiGian.LenDay(new DateTime(2026, 8, 21, 9, 0, 0)),
        };

        OrdersModuleHost.ApHoSoKmVeMay(svc, [tuHub], BayGio, new());

        Assert.DoesNotContain(svc.Log.Snapshot(LoginA), e => e.Message.Contains("CÙNG mốc sửa"));
    }

    // ══════════════════════════ W9 — teardown dừng nhịp hồ sơ KM ══════════════════════════

    /// <summary>
    /// <b><c>_kmTimer</c> phải được dispose lúc thoát app.</b> Teardown vòng 2 dispose ba nhịp kia mà quên nhịp
    /// này: <c>StopAllAsync</c> chờ tới 30 giây rồi <c>svc.Log.Dispose()</c> chạy sau — trong khoảng đó nhịp 3
    /// giây vẫn bắn, nên dòng nhật ký của lượt gộp rơi vào một <c>ActivityLog</c> đã đóng và
    /// <c>KmConfig.Save</c> vẫn ghi SQLite đúng lúc app đang đóng / đang bị Velopack thay file.
    /// </summary>
    [Fact]
    public void Teardown_DungNhipHoSoKm()
    {
        var (svc, _) = DungMay(dungDbCu: false);
        try
        {
            OrdersModuleHost.WireKmSync(svc);
            Assert.True(OrdersModuleHost.NhipHoSoKmDangChay);

            OrdersModuleHost.DungNhipHoSoKm();

            Assert.False(OrdersModuleHost.NhipHoSoKmDangChay);
        }
        finally
        {
            OrdersModuleHost.DungNhipHoSoKm();   // không để một nhịp 3 giây sống sang test khác
        }
    }

    public void Dispose()
    {
        foreach (var s in _services) { s.Log.Dispose(); }
        foreach (var path in _dbPaths)
        {
            using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
            {
                SqliteConnection.ClearPool(conn);
            }
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
        GC.SuppressFinalize(this);
    }
}
