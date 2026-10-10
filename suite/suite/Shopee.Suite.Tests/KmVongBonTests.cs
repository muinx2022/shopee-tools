// System.IO KHÔNG có trong danh sách implicit của SDK desktop (net8.0-windows) — thiếu là Path/File không tồn tại.
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using Shopee.Core.Coordination;
using Shopee.Suite.Infrastructure;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.App.Views;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;

namespace Shopee.Suite.Tests;

/// <summary>
/// <b>Vòng 4 — vòng phản hồi không khép kín, và ba chỗ phần mềm NÓI SAI với người dùng</b> (22/08/2026).
/// <list type="bullet">
/// <item><b>X1</b> — <c>POST /orders/km</c> trả BẢN ĐÃ GỘP và client ghi đè <c>km_config</c> của chính nó: máy
/// sai đồng hồ không tự sửa được mốc rác của mình (với nó, 2028 không phải tương lai) nên nếu không khép vòng
/// thì mỗi lần mở app nó lại đẩy 2028 lên và lùi mất sửa đổi của máy khác — vô hạn.</item>
/// <item><b>X3</b> — vế "log" của W7 bị làm thiếu: mốc SỬA bị kẹp mà không một dòng nhật ký nào.</item>
/// <item><b>X4</b> — backfill chủ trì chạy kể cả khi máy KHÔNG có hub, latch đóng luôn ⇒ máy một mình tự khoá
/// chết KM của chính nó khi <c>machine.json</c> bị tạo lại.</item>
/// <item><b>X5</b> — máy chậm giờ &gt; 6h ghi đĩa im lặng mỗi 60 giây, vĩnh viễn.</item>
/// <item><b>X7</b> — chỗ nối <c>StopAsync</c> → dừng nhịp hồ sơ KM không có lưới.</item>
/// </list>
/// </summary>
public class KmVongBonTests : IDisposable
{
    private const string LoginA = "sub-v4@shopee.vn";
    private const string MayMot = "may-mot:orders";
    private const string MayHai = "may-hai:orders";

    /// <summary>Giờ THẬT (của hub và của các máy chạy đúng đồng hồ).</summary>
    private static readonly DateTimeOffset GioHub = new(2026, 8, 22, 3, 0, 0, TimeSpan.Zero);

    /// <summary>Đồng hồ của máy hỏng: lệch hẳn hai năm về TƯƠNG LAI (pin CMOS chết / chỉnh nhầm năm).</summary>
    private static readonly DateTime GioMaySai = new(2028, 3, 1, 10, 0, 0);

    private readonly List<string> _dbPaths = new();
    private readonly List<AppServices> _services = new();

    /// <summary>Một "máy": file DB riêng + bộ dịch vụ riêng + một tài khoản mang <see cref="LoginA"/>.</summary>
    private (AppServices Services, long AccountId) DungMay()
    {
        var path = Path.Combine(Path.GetTempPath(), $"suite_km_v4_{Guid.NewGuid():N}.db");
        _dbPaths.Add(path);
        var services = new AppServices(path);
        _services.Add(services);
        services.Accounts.Insert(new Account { Email = LoginA, Password = "p" });
        return (services, services.Accounts.GetAll().First(a => a.Email == LoginA).Id);
    }

    private static KmConfig Dong(long id, int phanTram, DateTime? cfgUpdatedAt, string chuTri = MayMot)
        => new(id, TuDong: true, SoNgay: 7, phanTram, GioBatDau: "08:00", GioKetThuc: "09:00", LanChayCuoi: null)
        {
            CfgUpdatedAt = cfgUpdatedAt,
            OwnerMachine = chuTri,
        };

    /// <summary>Mô phỏng đúng việc hub làm với một lô đẩy: gộp từng dòng bằng luật DÙNG CHUNG, dòng CHƯA TỒN
    /// TẠI thì <c>dangCo = den</c> (xem <c>HubDatabase.UpsertKmProfiles</c>), và dùng GIỜ CỦA HUB.
    /// <para>Không gọi thẳng <c>HubDatabase</c> được: <c>Shopee.Suite.Tests</c> không tham chiếu project hub, và
    /// hub LINK sẵn file nguồn của <c>XuLyDonShopee.Core</c> nên ref thẳng sẽ trùng kiểu (CS0433). Vế hub thật
    /// đo riêng ở <c>Shopee.Hub.Web.Tests.KmRouteHttpTests</c> — qua HTTP, đúng route.</para></summary>
    private static List<KmProfileItem> HubGop(
        IReadOnlyList<KmProfileItem> day, DateTimeOffset gioHub, KmProfileItem? dangCoTrenHub = null)
        => day.Select(p => KmHoSoGop.Gop(dangCoTrenHub ?? p, p, gioHub)).ToList();

    // ══════════════════════════ X1 — KHÉP VÒNG PHẢN HỒI ══════════════════════════

    /// <summary>
    /// <b>Máy sai đồng hồ đẩy mốc sửa rác ⇒ sau MỘT lượt đẩy, <c>km_config</c> của CHÍNH máy đó mang mốc hub
    /// trả về; và lượt đẩy KẾ không còn lùi được cấu hình máy khác.</b>
    /// <para>Trước bản này: hub kẹp mốc trong bảng của mình, nhưng máy nguồn không hề biết — nó kéo hồ sơ về,
    /// lượt gộp chạy trên ĐỒNG HỒ CỦA NÓ nên 2028 không phải "tương lai" ⇒ không sửa gì ⇒ mở app lần sau lại
    /// đẩy 2028 lên. Vòng lặp vô hạn, và nhật ký ở máy kia chỉ nói "cấu hình vừa ĐỔI" — tức nói dối.</para>
    /// </summary>
    [Fact]
    public void MaySaiDongHo_DayLenRoiNhanLai_ThiMocSuaVeGioHub_VaThoiDeCauHinhMayKhac()
    {
        var (svc, id) = DungMay();
        svc.KmConfig.Save(Dong(id, phanTram: 25, cfgUpdatedAt: GioMaySai));

        var day = OrdersModuleHost.ChupHoSoKm(svc);
        var traVe = HubGop(day, GioHub);
        Assert.True(traVe[0].CfgUpdatedAt < day[0].CfgUpdatedAt, "hub phải KẸP mốc sửa rác");

        var doi = OrdersModuleHost.ApBanHubTraVe(svc, day, traVe, GioMaySai, new());

        Assert.Equal(1, doi);
        var sau = svc.KmConfig.Get(id);
        Assert.Equal(KmMocThoiGian.VeMay(traVe[0].CfgUpdatedAt), sau.CfgUpdatedAt);   // đã nhận mốc của hub
        Assert.Equal(25, sau.PhanTram);                                              // cấu hình KHÔNG bị đụng
        Assert.True(sau.TuDong);

        // Lượt đẩy KẾ mang mốc đã được chữa ⇒ một máy khác sửa SAU đó vẫn thắng bình thường.
        var dayLan2 = OrdersModuleHost.ChupHoSoKm(svc);
        var mayKhacSuaSau = new KmProfileItem(LoginA)
        {
            TuDong = true,
            SoNgay = 7,
            PhanTram = 40,
            GioBatDau = "08:00",
            GioKetThuc = "09:00",
            OwnerMachine = MayHai,
            CfgUpdatedAt = GioHub.AddMinutes(5),
        };
        var gopLan2 = KmHoSoGop.Gop(mayKhacSuaSau, dayLan2[0], GioHub.AddMinutes(10));

        Assert.Equal(40, gopLan2.PhanTram);
        Assert.Equal(MayHai, gopLan2.OwnerMachine);
    }

    /// <summary>Và lượt khép vòng đó phải NÓI RA — máy sai đồng hồ là ca người dùng không thể tự chẩn đoán, vì
    /// trên chính máy đó mọi mốc đều trông bình thường. Đúng MỘT dòng mỗi ngày.</summary>
    [Fact]
    public void MaySaiDongHo_ThiGhiDungMotDongNhatKyMoiNgay()
    {
        var (svc, id) = DungMay();
        svc.KmConfig.Save(Dong(id, phanTram: 25, cfgUpdatedAt: GioMaySai));
        var day = OrdersModuleHost.ChupHoSoKm(svc);
        var traVe = HubGop(day, GioHub);

        var so = new Dictionary<string, DateTime>();
        OrdersModuleHost.ApBanHubTraVe(svc, day, traVe, GioMaySai, so);

        // Người dùng ở máy hỏng lưu form lần nữa ⇒ lại đóng dấu mốc 2028 ⇒ hub lại kẹp. Trong CÙNG NGÀY thì
        // không được nói lại: nhật ký spam thì người dùng thôi đọc, tức mất luôn tác dụng của chính dòng đó.
        svc.KmConfig.Save(Dong(id, phanTram: 25, cfgUpdatedAt: GioMaySai));
        var day2 = OrdersModuleHost.ChupHoSoKm(svc);
        OrdersModuleHost.ApBanHubTraVe(svc, day2, HubGop(day2, GioHub), GioMaySai.AddMinutes(1), so);

        var dong = svc.Log.Snapshot(LoginA).Where(e => e.Message.Contains("ĐỒNG HỒ MÁY NÀY chạy nhanh")).ToList();
        Assert.Single(dong);
        Assert.Contains("KẸP về", dong[0].Message);

        // NGÀY SAU thì được nói lại — đồng hồ vẫn sai, người đọc hôm sau cũng cần biết.
        svc.KmConfig.Save(Dong(id, phanTram: 25, cfgUpdatedAt: GioMaySai.AddDays(1)));
        var day3 = OrdersModuleHost.ChupHoSoKm(svc);
        OrdersModuleHost.ApBanHubTraVe(svc, day3, HubGop(day3, GioHub), GioMaySai.AddDays(1), so);

        Assert.Equal(2, svc.Log.Snapshot(LoginA).Count(e => e.Message.Contains("ĐỒNG HỒ MÁY NÀY chạy nhanh")));
    }

    /// <summary>Vế ĐỐI CHỨNG — đồng hồ đúng thì lượt đẩy nhận lại đúng bản mình gửi ⇒ KHÔNG ghi đĩa, KHÔNG log.
    /// Thiếu vế này thì một bản "lúc nào cũng ghi đè + luôn cảnh báo" cũng xanh, mà bản đó spam mỗi 3 giây.</summary>
    [Fact]
    public void DongHoDung_ThiLuotDayNhanLai_KhongDoiGiVaKhongLog()
    {
        var (svc, id) = DungMay();
        svc.KmConfig.Save(Dong(id, phanTram: 25, cfgUpdatedAt: new DateTime(2026, 8, 22, 9, 0, 0)));
        var day = OrdersModuleHost.ChupHoSoKm(svc);

        var doi = OrdersModuleHost.ApBanHubTraVe(svc, day, HubGop(day, GioHub), new DateTime(2026, 8, 22, 10, 0, 0), new());

        Assert.Equal(0, doi);
        Assert.Empty(svc.Log.Snapshot(LoginA));
    }

    /// <summary><b>Nhánh DEGRADE bắt buộc: hub CŨ trả body không có bản gộp ⇒ client KHÔNG ném, KHÔNG đổi gì.</b>
    /// <c>HubClient.PushKmProfilesAsync</c> trả <c>null</c> ở ca đó (body lạ) hoặc danh sách rỗng (hub cũ trả
    /// <c>{"profiles": n}</c> — trường mới giữ giá trị khởi tạo).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HubCu_KhongTraBanGop_ThiKhongDoiGiVaKhongNem(bool traNull)
    {
        var (svc, id) = DungMay();
        var cu = Dong(id, phanTram: 25, cfgUpdatedAt: GioMaySai);
        svc.KmConfig.Save(cu);
        var day = OrdersModuleHost.ChupHoSoKm(svc);

        var doi = OrdersModuleHost.ApBanHubTraVe(
            svc, day, traNull ? null : new List<KmProfileItem>(), GioMaySai, new());

        Assert.Equal(0, doi);
        Assert.Equal(GioMaySai, svc.KmConfig.Get(id).CfgUpdatedAt);   // giữ NGUYÊN bản cục bộ
        Assert.Empty(svc.Log.Snapshot(LoginA));
    }

    /// <summary>
    /// Người dùng lưu form (hoặc <c>ChotLuotKm</c> ghi mốc) TRONG LÚC request đẩy đang bay ⇒ bản hub trả về đã
    /// cũ ⇒ KHÔNG được ghi đè. Ghi đè ở đây là xoá đúng thay đổi người dùng vừa làm, im lặng.
    /// </summary>
    [Fact]
    public void DongCucBoDoiTrongLucRequestDangBay_ThiKhongGhiDe()
    {
        var (svc, id) = DungMay();
        svc.KmConfig.Save(Dong(id, phanTram: 25, cfgUpdatedAt: GioMaySai));
        var day = OrdersModuleHost.ChupHoSoKm(svc);
        var traVe = HubGop(day, GioHub);

        // …request đang bay thì người dùng đổi 25% → 40% ở chính máy này.
        var vuaSua = Dong(id, phanTram: 40, cfgUpdatedAt: GioMaySai.AddMinutes(1));
        svc.KmConfig.Save(vuaSua);

        var doi = OrdersModuleHost.ApBanHubTraVe(svc, day, traVe, GioMaySai.AddMinutes(2), new());

        Assert.Equal(0, doi);
        Assert.Equal(40, svc.KmConfig.Get(id).PhanTram);
    }

    // ══════════════════════════ X3 + X5 — mốc SỬA bị kẹp: nói ra, và đừng ghi đĩa mỗi nhịp ══════════════════════════

    /// <summary>
    /// <b>Máy chạy CHẬM giờ hơn 6 tiếng: mốc sửa của hub luôn là "tương lai" với nó ⇒ bị kẹp về "bây giờ" mới
    /// mỗi nhịp.</b> Lượt đầu phải ghi (cấu hình thật sự đổi), hai lượt sau KHÔNG được ghi thêm — bản vòng 3 ghi
    /// SQLite mỗi 60 giây, vĩnh viễn, và im lặng vì <c>CungCauHinh</c> cố ý bỏ qua mốc sửa (X5). Kèm ĐÚNG MỘT
    /// dòng nhật ký mỗi ngày về mốc sửa bị kẹp — vế "log" mà W7 làm thiếu (X3).
    /// </summary>
    [Fact]
    public void MayChamGio_HaiLuotKeoLienTiep_KhongSinhLuotGhiDiaThuHai_VaChiMotDongLog()
    {
        var (svc, id) = DungMay();
        var bayGioMay = new DateTime(2026, 8, 22, 0, 0, 0);   // đồng hồ máy này chậm hơn thực tế mấy ngày
        svc.KmConfig.Save(Dong(id, phanTram: 5, cfgUpdatedAt: new DateTime(2026, 8, 20, 9, 0, 0)));

        var tuHub = new KmProfileItem(LoginA)
        {
            TuDong = true,
            SoNgay = 7,
            PhanTram = 25,
            GioBatDau = "08:00",
            GioKetThuc = "09:00",
            OwnerMachine = MayMot,
            // Mốc ĐÚNG của hub, nhưng với đồng hồ chậm của máy này thì nó luôn là "quá xa tương lai".
            CfgUpdatedAt = new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero),
        };

        var so = new Dictionary<string, DateTime>();
        var lan1 = OrdersModuleHost.ApHoSoKmVeMay(svc, [tuHub], bayGioMay, so);
        var lan2 = OrdersModuleHost.ApHoSoKmVeMay(svc, [tuHub], bayGioMay.AddMinutes(1), so);
        var lan3 = OrdersModuleHost.ApHoSoKmVeMay(svc, [tuHub], bayGioMay.AddMinutes(2), so);

        Assert.Equal(1, lan1);   // cấu hình thật sự đổi (5% → 25%) ⇒ PHẢI ghi
        Assert.Equal(0, lan2);   // chỉ còn khác nhau ở cái mốc vừa bị kẹp ⇒ đừng ghi đĩa
        Assert.Equal(0, lan3);
        Assert.Equal(25, svc.KmConfig.Get(id).PhanTram);

        Assert.Single(svc.Log.Snapshot(LoginA).Where(e => e.Message.Contains("mốc SỬA CẤU HÌNH")));

        // NGÀY SAU thì được nói lại — đồng hồ vẫn sai, người đọc hôm sau cũng cần biết.
        OrdersModuleHost.ApHoSoKmVeMay(svc, [tuHub], bayGioMay.AddDays(1), so);
        Assert.Equal(2, svc.Log.Snapshot(LoginA).Count(e => e.Message.Contains("mốc SỬA CẤU HÌNH")));
    }

    /// <summary>
    /// <b>Vế ĐỐI CHỨNG của X5, và là cái chặn bản vá đi quá tay:</b> mốc sửa MỚI HƠN mà HỢP LỆ, dù chỉ mình nó
    /// khác, vẫn phải được ghi.
    /// <para>Nếu cửa "đừng ghi đĩa" chặn theo kiểu "hễ chỉ khác mỗi mốc sửa thì bỏ qua" thì dòng cục bộ giữ mãi
    /// mốc CŨ, và lượt đẩy kế mang mốc cũ đó đi so với cả fleet — tức làm hỏng đúng trọng tài của luật "máy nào
    /// sửa sau thắng". Chỉ được bỏ qua đúng lượt mà mốc ấy là giá trị máy này tự bịa vì đồng hồ lệch.
    /// (Lượt thử phá dựng đúng bản vá quá tay đó: mọi test khác vẫn xanh.)</para>
    /// </summary>
    [Fact]
    public void MocSuaMoiHonMaHopLe_DuChiMinhNoKhac_ThiVanGhi()
    {
        var (svc, id) = DungMay();
        var bayGio = new DateTime(2026, 8, 22, 12, 0, 0);
        var mocCu = new DateTime(2026, 8, 20, 9, 0, 0);
        var mocMoi = new DateTime(2026, 8, 21, 9, 0, 0);
        svc.KmConfig.Save(Dong(id, phanTram: 25, cfgUpdatedAt: mocCu));

        var tuHub = new KmProfileItem(LoginA)
        {
            TuDong = true,
            SoNgay = 7,
            PhanTram = 25,          // cấu hình Y HỆT — chỉ mỗi mốc sửa tiến lên
            GioBatDau = "08:00",
            GioKetThuc = "09:00",
            OwnerMachine = MayMot,
            CfgUpdatedAt = KmMocThoiGian.LenDay(mocMoi),
        };

        Assert.Equal(1, OrdersModuleHost.ApHoSoKmVeMay(svc, [tuHub], bayGio, new()));

        Assert.Equal(mocMoi, svc.KmConfig.Get(id).CfgUpdatedAt);
        Assert.Empty(svc.Log.Snapshot(LoginA));   // cấu hình không đổi ⇒ vẫn KHÔNG log
    }

    /// <summary>Vế ĐỐI CHỨNG — mốc sửa NẰM TRONG biên ⇒ không log, và lượt gộp vẫn ghi bình thường.</summary>
    [Fact]
    public void MocSuaTrongBien_ThiKhongLogVaVanGhi()
    {
        var (svc, id) = DungMay();
        var bayGio = new DateTime(2026, 8, 22, 12, 0, 0);
        svc.KmConfig.Save(Dong(id, phanTram: 5, cfgUpdatedAt: new DateTime(2026, 8, 20, 9, 0, 0)));

        var tuHub = new KmProfileItem(LoginA)
        {
            TuDong = true,
            SoNgay = 7,
            PhanTram = 25,
            GioBatDau = "08:00",
            GioKetThuc = "09:00",
            OwnerMachine = MayMot,
            CfgUpdatedAt = KmMocThoiGian.LenDay(bayGio.AddHours(-1)),
        };

        Assert.Equal(1, OrdersModuleHost.ApHoSoKmVeMay(svc, [tuHub], bayGio, new()));

        Assert.Equal(25, svc.KmConfig.Get(id).PhanTram);
        Assert.DoesNotContain(svc.Log.Snapshot(LoginA), e => e.Message.Contains("mốc SỬA CẤU HÌNH"));
    }

    // ══════════════════════════ X4 — backfill chủ trì CHỈ khi có hub ══════════════════════════

    /// <summary>
    /// <b>Máy KHÔNG có hub ⇒ lượt rót worker KHÔNG được backfill chủ trì, và KHÔNG được đóng latch.</b>
    /// <para>Ca thật (phản biện vòng 3): người dùng MỘT máy, không hub. <c>LeaseMachineId</c> không bao giờ rỗng
    /// (<c>machine.json</c> là file cục bộ) nên nhánh "mayNay rỗng ⇒ không làm gì" bên trong hàm backfill KHÔNG
    /// đỡ được ca này. Nâng cấp ⇒ đóng dấu chủ trì + latch đóng; hôm sau <c>machine.json</c> bị tạo lại (dọn
    /// AppData / hồ sơ Windows mới / cài lại máy) ⇒ id mới ⇒ <c>LaMayChuTri</c> false ⇒ <b>KM tự động ngừng hẳn
    /// trên máy duy nhất</b>. Trước W3 thì chủ trì rỗng nghĩa là "ai cũng chạy" nên ca này không tồn tại.</para>
    /// <para>Gọi bản MỘT THAM SỐ — đúng hàm production gọi — nên nó khoá luôn cả cái cổng
    /// <c>CoordinationRuntime.Client is not null</c> (trong test, hub chưa bao giờ được cấu hình).</para>
    /// </summary>
    [Fact]
    public void KhongCoHub_ThiWireKmSyncKhongBackfillChuTri_VaKhongDongLatch()
    {
        var (svc, id) = DungMay();
        svc.KmConfig.Save(new KmConfig(id, TuDong: true, SoNgay: 7, PhanTram: 25, GioBatDau: "08:00",
            GioKetThuc: "09:00", LanChayCuoi: null));
        Assert.Equal("", svc.KmConfig.Get(id).OwnerMachine);

        try
        {
            OrdersModuleHost.WireKmSync(svc);

            Assert.Equal("", svc.KmConfig.Get(id).OwnerMachine);   // KHÔNG đóng dấu

            // …và latch còn MỞ: hôm sau người dùng cấu hình hub thì lượt backfill vẫn còn cơ hội chạy.
            Assert.Equal(1, svc.KmConfig.BackfillOwnerMachineOnce(MayMot));
            Assert.Equal(MayMot, svc.KmConfig.Get(id).OwnerMachine);
        }
        finally
        {
            OrdersModuleHost.DungNhipHoSoKm();
        }
    }

    // ══════════════════════════ X7 — chỗ nối teardown ══════════════════════════

    /// <summary>
    /// <b>Đường dọn dẹp lúc thoát app PHẢI dừng nhịp hồ sơ KM.</b> Vòng 3 khoá được hàm dừng nhịp, nhưng bỏ
    /// hẳn DÒNG GỌI trong <c>StopAsync</c> thì không một test nào đỏ (nghiệm thu xác nhận bằng grep) — vì hàm
    /// đó đọc <c>Services</c> static và gọi <c>StopAllAsync</c>. Nay thân dọn dẹp nhận tham số nên gọi thẳng
    /// được.
    /// </summary>
    [Fact]
    public async Task DonDepKhiThoat_ThiDungNhipHoSoKm()
    {
        var (svc, _) = DungMay();
        try
        {
            OrdersModuleHost.WireKmSync(svc);
            Assert.True(OrdersModuleHost.NhipHoSoKmDangChay);

            await OrdersModuleHost.DonDepKhiThoatAsync(svc, null);

            Assert.False(OrdersModuleHost.NhipHoSoKmDangChay);
        }
        finally
        {
            OrdersModuleHost.DungNhipHoSoKm();
        }
    }

    /// <summary>
    /// <b>Nhịp đang BAY lúc thoát app cũng phải tự dừng.</b> <c>Timer.Dispose()</c> KHÔNG chờ callback đang
    /// chạy, mà callback là fire-and-forget có <c>await</c> mạng tới 8 giây — nên một nhịp vẫn có thể
    /// <c>Log.Append</c> / <c>KmConfig.Save</c> SAU <c>Log.Dispose()</c> và trong lúc Velopack đang thay file.
    /// </summary>
    [Fact]
    public async Task DangThoat_ThiNhipTuDungOCuaDauTien()
    {
        var (svc, _) = DungMay();
        try
        {
            OrdersModuleHost.WireKmSync(svc);
            // Chưa thoát: nhịp đi qua cửa cờ, dừng ở cổng hub (test không có hub) — KHÁC hẳn "đang thoát".
            Assert.Equal(OrdersModuleHost.NhipKm.ChuaCoHub, await OrdersModuleHost.KmSyncTickAsync(svc));

            OrdersModuleHost.DungNhipHoSoKm();

            Assert.Equal(OrdersModuleHost.NhipKm.DangThoat, await OrdersModuleHost.KmSyncTickAsync(svc));
        }
        finally
        {
            OrdersModuleHost.DungNhipHoSoKm();
        }
    }

    // ══════════════════════════ X2 — mọi câu hướng dẫn trỏ vào NÚT CÓ THẬT ══════════════════════════

    /// <summary>
    /// <b>Form KM có ĐÚNG bốn nút.</b> Chốt danh sách này lại là để lần sau ai thêm/bớt nút thì phải quay lại
    /// đúng chỗ các câu hướng dẫn — nguồn gốc của lỗi X2 là một câu bảo người dùng bấm "LƯU", nút không hề tồn
    /// tại; và đó đã là lần thứ HAI cùng một hình dạng lỗi trong đợt này (W4 sửa đúng câu đó ở một chỗ khác rồi
    /// cùng vòng lại đẻ ra bản sao).
    /// <para><b>BA → BỐN ngày 26/08/2026</b>: tab Combo có thêm nút "Chạy Combo" (đường chạy TAY của vòng combo).
    /// <b>Vẫn chốt DANH SÁCH ĐÚNG BẰNG, cố ý không nới thành "≥ 3"</b> — chính phép so đúng-bằng mới bắt được
    /// một nút lạ mọc thêm, mà nút trên form này là nút MỞ TRÌNH DUYỆT và tạo chương trình THẬT.</para>
    /// </summary>
    [StaFact]
    public void FormKm_CoDungBonNut()
    {
        Assert.Equal(
            new[] { "Chạy chương trình", "Chạy Combo", "Máy này nhận chủ trì", "Đóng" }
                .OrderBy(x => x, StringComparer.Ordinal),
            NhanCacNutTrenForm().OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>
    /// <b>LƯỚI CHỐNG RÀ RỖNG của vòng đi bộ trên form</b> — vòng rà phải chạm được nội dung của CẢ BA tab.
    /// <para>Từ 26/08/2026 form KM chia tab, mà <c>TabControl</c> không dựng nội dung tab chưa chọn vào cây
    /// trực quan: một vòng rà chỉ đo tab đang mở sẽ <b>im lặng bỏ sót hai phần ba form</b> và mọi test khác
    /// vẫn xanh. Ba mốc dưới đây mỗi mốc nằm ở một tab khác nhau, nên bản "chỉ gom tab đang mở" ĐỎ ngay.</para>
    /// </summary>
    [StaFact]
    public void FormKm_VongRaDiQuaCaBaTab()
    {
        var chu = DungFormRoiGomMoiTab().OfType<TextBlock>()
            .Select(t => t.Text?.Trim() ?? "").Where(t => t.Length > 0).ToList();

        Assert.Contains(chu, c => c.Contains("Tự sinh theo ngày/giờ"));  // tab Chương Trình Của Shop
        Assert.Contains(chu, c => c.Contains("Giới hạn đặt hàng"));      // tab Combo Khuyến Mãi
        // Tab Flash Sale — từ 28/08/2026 tab này KHÔNG còn là chỗ giữ chỗ ("khung 10-12h") mà đã có công tắc
        // thật. Neo vào CÁI GIÁ mà người dùng phải biết trước khi bật. Từ 01/09/2026 cái giá ĐỔI: hết cửa sổ
        // 21-24h cắt vòng ("20:00 tới 24:00" cũ), thay bằng nhiều gói nhỏ cả ngày — mỗi gói chiếm một khe nghỉ.
        Assert.Contains(chu, c => c.Contains("chiếm trọn một khe nghỉ")); // tab Flash Sale
        Assert.True(chu.Count >= 34, $"chỉ gom được {chu.Count} dòng chữ — vòng rà đang bỏ sót tab nào đó");
    }

    /// <summary>
    /// <b>Form nói ra KHOẢNG 180 ngày, và KHÔNG còn ô "Kết thúc" nào</b> (người dùng đính chính 26/08/2026).
    /// <para><b>Vì sao đo trên form THẬT chứ không đọc XAML:</b> ô "Kết thúc" biến mất là một quyết định về
    /// GIAO DIỆN — một ô nhập không đi đâu chính là loại điều khiển gây hiểu nhầm mà đợt này đã phải sửa hai
    /// lần. Test hàm thuần không thấy được nó, và <c>x:Name</c> bị xoá thì trình biên dịch chỉ bắt được những
    /// test đang gọi tên ô đó.</para>
    /// <para><b>Dòng mô tả phải có trên CẢ HAI tab</b> — người dùng mở tab nào cũng phải đọc được là chương
    /// trình sống 180 ngày chứ không phải một tiếng. Vòng rà đi qua từng tab, xem
    /// <see cref="DungFormRoiGomMoiTab"/>.</para>
    /// </summary>
    [StaFact]
    public void FormKm_NoiRaKhoang180Ngay_VaKhongConOKetThuc()
    {
        var phanTu = DungFormRoiGomMoiTab();
        var chu = phanTu.OfType<TextBlock>().Select(t => t.Text?.Trim() ?? "").Where(t => t.Length > 0).ToList();

        // Không còn NHÃN "Kết thúc" nào trên toàn form (cả hai tab).
        Assert.DoesNotContain(chu, c => c == "Kết thúc");
        // …và cũng không còn câu chữ của luật đã bỏ.
        Assert.DoesNotContain(chu, c => c.Contains("kết thúc phải sau bắt đầu"));

        // Ô giờ còn lại đổi nhãn thành "Giờ" — có ĐÚNG hai ô (một mỗi tab có ô giờ).
        Assert.Equal(2, chu.Count(c => c == "Giờ"));

        // Dòng mô tả khoảng: HAI dòng, mỗi tab một dòng, và mỗi dòng phải nêu cả độ dài lẫn luật "cùng giờ".
        var moTaKhoang = chu.Where(c => c.Contains("180 ngày") && c.Contains("cùng giờ này ở hai đầu")).ToList();
        Assert.Equal(2, moTaKhoang.Count);
        Assert.Contains(moTaKhoang, c => c.Contains("HÔM NAY"));   // tab Combo: luôn bắt đầu hôm nay

        // Và KHÔNG ô nhập nào còn buộc vào hai trường giờ kết thúc — chúng đã biến mất khỏi cây trực quan chứ
        // không phải chỉ bị ẩn đi (ẩn thì `Text` vẫn hai chiều và người dùng vẫn sửa được bằng phím Tab).
        var duongBind = phanTu.OfType<TextBox>()
            .Select(t => System.Windows.Data.BindingOperations.GetBinding(t, TextBox.TextProperty)?.Path?.Path ?? "")
            .Where(p => p.Length > 0).ToList();
        Assert.DoesNotContain(nameof(KmDialogViewModel.GioKetThuc), duongBind);
        Assert.DoesNotContain(nameof(KmDialogViewModel.ComboGioKetThuc), duongBind);

        // Lưới chống rà rỗng: hai ô GIỜ CÒN LẠI vẫn phải có mặt (một mỗi tab), kẻo "không tìm thấy gì" thành
        // ra một test luôn xanh.
        Assert.Contains(nameof(KmDialogViewModel.GioBatDau), duongBind);
        Assert.Contains(nameof(KmDialogViewModel.ComboGioBatDau), duongBind);
    }

    /// <summary>
    /// <b>Mọi CÂU HƯỚNG DẪN của pha A chỉ được nhắc tới nút/thao tác CÓ THẬT.</b> Mỗi cụm nằm trong dấu ngoặc
    /// kép của các dòng nhật ký người dùng đọc phải là TÊN MỘT NÚT trên form, hoặc nằm trong danh sách ngoại lệ
    /// khai tường minh ngay dưới đây (dữ liệu, không phải thao tác).
    /// <para><b>(vòng 5, Y3) Vòng rà gồm CẢ <c>ToolTip</c> của MỌI nút trên form thật.</b> Bản vòng 4 chỉ rà
    /// các dòng nhật ký, nên hai tooltip "Chạy chương trình" và "Máy này nhận chủ trì" nằm ngoài lưới — mà
    /// tooltip là chỗ câu chữ nói sai dễ sống lâu nhất, người viết code hiếm khi đọc lại chúng.</para>
    /// </summary>
    [StaFact]
    public void MoiCauHuongDan_ChiNhacToiNutCoThat()
    {
        var nutTrenForm = CacNutTrenForm();
        var nut = nutTrenForm.Select(x => x.Nhan).ToHashSet(StringComparer.Ordinal);

        // Ngoại lệ KHAI TƯỜNG MINH: các cụm trong ngoặc kép KHÔNG phải tên nút.
        var ngoaiLe = new HashSet<string>(StringComparer.Ordinal)
        {
            "lần chạy cuối", "lần thử tự động",   // tên MỐC trong câu cảnh báo sai đồng hồ
            "máy nào sửa sau thắng",              // tên LUẬT gộp, trích trong câu cảnh báo
            TenChuongTrinhMau,                    // tên chương trình KM trong dòng "bắt đầu chạy" — DỮ LIỆU
            // Nhãn máy chủ trì trong dòng "không phải chủ trì" — DỮ LIỆU, không phải thao tác.
            XuLyDonShopee.Core.Services.KmLichTuDong.NhanMay("may-khac:orders", null),
        };

        var cacCau = CacCauNguoiDungDoc().Select(x => x.Cau)
            .Concat(nutTrenForm.Select(x => x.Nut.ToolTip?.ToString() ?? ""))
            .ToList();

        // Lưới chống "test xanh vì không rà gì cả": danh sách phải THẬT SỰ có mặt các dòng khó lấy nhất — hai
        // dòng của nhịp lịch tự động chỉ sinh ra khi chạy đúng đường của nó, còn tooltip thì chỉ đọc được sau
        // một lượt layout thật.
        // (26/08/2026) Ngưỡng đếm nâng 15 → 32 cho khớp thực tế: 15 là con số của thời danh sách còn ngắn, để
        // nguyên thì mất non nửa số câu vẫn không ai biết.
        Assert.Contains(cacCau, c => c.Contains("MÁY CHỦ TRÌ là"));
        Assert.Contains(cacCau, c => c.Contains("CẤU HÌNH KHÔNG DÙNG ĐƯỢC"));
        Assert.Contains(cacCau, c => c.Contains("CÙNG mốc sửa"));
        Assert.Contains(cacCau, c => c.Contains("CHẠY THẬT"));            // tooltip "Chạy chương trình"
        Assert.Contains(cacCau, c => c.Contains("Đóng dấu MÁY NÀY"));     // tooltip "Máy này nhận chủ trì"
        Assert.True(cacCau.Count >= 32, $"chỉ rà được {cacCau.Count} câu — thiếu chỗ nào đó");
        Assert.True(cacCau.Sum(c => CumTrongNgoacKep(c).Count()) >= 6, "không có cụm trong ngoặc kép nào để rà");

        foreach (var cau in cacCau)
        {
            foreach (var cum in CumTrongNgoacKep(cau))
            {
                Assert.True(nut.Contains(cum) || ngoaiLe.Contains(cum),
                    $"Câu hướng dẫn nhắc tới \"{cum}\" mà form KM không có nút nào tên vậy.\nCâu: {cau}");
            }

            // Và cấm hẳn cái hình dạng đã lặp HAI LẦN trong đợt này: khuyên "lưu lại" trong khi form không có
            // nút Lưu nào — cụm đó thường viết KHÔNG có ngoặc kép nên vòng rà ở trên không thấy.
            foreach (var cam in new[] { "lưu lại", "nút lưu", "bấm lưu", "và lưu" })
            {
                Assert.DoesNotContain(cam, cau, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// <b>Tooltip nút "Đóng" phải nói đúng hành vi thật</b> — câu thứ ba cùng hình dạng lỗi X2. Bản trước viết
    /// "Cấu hình được lưu nếu HỢP LỆ", nhưng từ V5/W5 thì mở form ra XEM rồi đóng KHÔNG ghi gì cả (và cũng không
    /// đóng dấu máy chủ trì). Người dùng đọc câu cũ rồi tưởng mình vừa "chốt" cấu hình — đúng bước 5 của ca mất
    /// cấu hình vòng 1.
    /// </summary>
    [StaFact]
    public void TooltipNutDong_NoiDungHanhViThat()
    {
        var tip = TooltipNut("Đóng");

        Assert.Contains("không sửa gì", tip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KHÔNG ghi", tip, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Và cụ thể: câu của lượt PHÁ HOÀ thôi bảo người dùng "LƯU lại" (nút không tồn tại), thay bằng
    /// đúng hai đường có thật.</summary>
    [Fact]
    public void CauPhaHoa_ThoiKhuyenMotThaoTacKhongTonTai()
    {
        var cau = OrdersModuleHost.CauPhaHoa();

        Assert.DoesNotContain("LƯU lại", cau);
        Assert.Contains("Máy này nhận chủ trì", cau);
        Assert.Contains("Đóng", cau);
    }

    /// <summary>
    /// <b>(vòng 5, Y3) Lưới rà chuỗi phải bắt được cả cái CHƯA TỒN TẠI.</b> Danh sách khai tay ở
    /// <see cref="CacCauNguoiDungDoc"/> chỉ chặn HỒI QUY: pha B đã hẹn thêm một câu <c>CauHoanVong</c> mới, mà
    /// câu chưa được liệt kê thì mọi test rà chuỗi vẫn xanh — đúng bài học của chính X2 ("câu thứ ba sinh ra ở
    /// vòng sau"), chỉ là ở tầng lưới thay vì tầng câu chữ.
    /// <para>Nên lưới thật nằm ở PHẢN CHIẾU: mọi hàm <c>static</c> tên <c>Cau*</c> trả <c>string</c> của ba lớp
    /// sinh câu cho người dùng đều phải có mặt trong danh sách. Quên liệt kê ⇒ ĐỎ ngay, kèm tên hàm.</para>
    /// </summary>
    [StaFact]
    public void MoiHamSinhCau_DeuPhaiNamTrongVongRaChuoi()
    {
        var daRa = CacCauNguoiDungDoc().Select(x => x.Ham).ToHashSet(StringComparer.Ordinal);
        var tatCa = CacHamSinhCau();

        // Lưới chống phép đo RỖNG: phản chiếu mà không thấy hàm nào thì mọi khẳng định dưới đây đúng vô nghĩa.
        // Sàn nâng từ 7 lên 11 khi Z4 kéo thêm AccountSession + AccountsViewModel vào lưới: sàn phải bám sát
        // số thật, không thì nó thôi chặn được ca "phản chiếu hụt mất một lớp".
        Assert.True(tatCa.Count >= 11, $"phản chiếu chỉ thấy {tatCa.Count} hàm Cau* — sai kiểu hoặc sai BindingFlags");

        var thieu = tatCa.Where(t => !daRa.Contains(t)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.True(thieu.Count == 0,
            "Có hàm sinh câu cho người dùng KHÔNG nằm trong vòng rà chuỗi: " + string.Join(", ", thieu)
            + ". Thêm nó vào CacCauNguoiDungDoc() rồi chạy lại — lưới này tồn tại để chặn ĐÚNG cái vừa được "
            + "thêm vào, không phải để chặn cái đã có.");
    }

    /// <summary>Tên mọi hàm <c>static</c> <c>Cau*</c> trả <c>string</c> của các lớp sinh câu cho người dùng ở
    /// pha A. <c>DeclaredOnly</c> để không nhặt phải thành viên thừa kế từ <c>object</c>.
    /// <para><b>(vòng 6, Z4) Thêm <c>AccountSession</c> và <c>AccountsViewModel</c></b> — đúng vùng pha B sắp
    /// mổ. <c>AccountSession.CauBiTuChoiKhoa</c> là chính câu C7 nói đang sai với người bấm tay và là nơi B1
    /// sẽ thêm dòng "đã nhả khoá"; hai câu tổng kết lượt KM thì người dùng đọc mỗi đêm. Không kéo chúng vào
    /// lưới TRƯỚC pha B thì câu B1 viết ra rơi đúng vào lỗ mà Y3 vừa vá.</para>
    /// <para><b>Chỉ bắt HÀM, không bắt hằng</b>: <c>AccountsViewModel</c> còn hai chuỗi
    /// <c>internal const string Cau*</c> (<c>CauTuChoiViLatCatDangChay</c>, <c>CauDaDongCuaSoXem</c>) mà
    /// <c>GetMethods</c> không thấy — khai thẳng ở đây, xem mục "chỗ chưa có lưới" trong báo cáo vòng 6.</para></summary>
    private static List<string> CacHamSinhCau()
        => new[]
            {
                typeof(OrdersModuleHost), typeof(KmDialogViewModel),
                typeof(XuLyDonShopee.Core.Services.KmLichTuDong),
                typeof(XuLyDonShopee.App.Services.AccountSession), typeof(AccountsViewModel),
            }
            .SelectMany(t => t.GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(m => m.Name.StartsWith("Cau", StringComparison.Ordinal) && m.ReturnType == typeof(string))
            .Select(m => m.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

    /// <summary>Tên chương trình KM dùng trong câu "bắt đầu chạy" — DỮ LIỆU người dùng gõ, không phải nút.</summary>
    private const string TenChuongTrinhMau = "KM 22-08 02:00-03:00";

    /// <summary>Gói thông số combo mẫu cho câu tổng kết của nút "Chạy Combo" — một KHOẢNG 180 ngày.</summary>
    private static readonly XuLyDonShopee.Core.Services.ThamSoCombo ThamSoComboMau =
        new("Combo 19-08-2026", new DateTime(2026, 8, 19), new DateTime(2027, 2, 15), "11:00",
            Loai: 3, MuaSl: 2, Giam: 2000, GioiHan: 3);

    /// <summary>Gói thông số KM mẫu cho câu tổng kết của nút "Chạy chương trình".</summary>
    private static readonly XuLyDonShopee.Core.Services.ThamSoKm ThamSoKmMau =
        new(TenChuongTrinhMau, new DateTime(2026, 8, 22), new DateTime(2027, 2, 18), "02:00", 25);

    /// <summary>Mọi dòng nhật ký + câu trên form của pha A mà NGƯỜI DÙNG đọc được, kèm TÊN HÀM sinh ra chúng.
    /// <para>Tên hàm là vế so của <see cref="MoiHamSinhCau_DeuPhaiNamTrongVongRaChuoi"/> — dùng
    /// <c>nameof</c> chứ không gõ chuỗi, để đổi tên hàm cũng kéo theo danh sách này.</para></summary>
    private IEnumerable<(string Ham, string Cau)> CacCauNguoiDungDoc()
    {
        var (svc, id) = DungMay();
        var cu = Dong(id, phanTram: 25, cfgUpdatedAt: new DateTime(2026, 8, 20, 9, 0, 0), chuTri: "may-khac:orders");
        svc.KmConfig.Save(cu);

        yield return (nameof(OrdersModuleHost.CauPhaHoa), OrdersModuleHost.CauPhaHoa());
        yield return (nameof(OrdersModuleHost.CauMocRac),
            OrdersModuleHost.CauMocRac("lần chạy cuối", GioHub.AddYears(2)));
        yield return (nameof(OrdersModuleHost.CauMocSuaRac), OrdersModuleHost.CauMocSuaRac(GioHub.AddYears(2)));
        yield return (nameof(OrdersModuleHost.CauHubKepMocSua),
            OrdersModuleHost.CauHubKepMocSua(KmMocThoiGian.LenDay(GioMaySai), GioHub));
        yield return (nameof(OrdersModuleHost.CauDoiCauHinh), OrdersModuleHost.CauDoiCauHinh(cu, null));
        yield return (nameof(KmDialogViewModel.CauCanhBaoSauForm),
            KmDialogViewModel.CauCanhBaoSauForm(cu, Dong(id, 40, cu.CfgUpdatedAt), daLuu: false)!);
        yield return (nameof(KmDialogViewModel.CauCanhBaoSauForm),
            KmDialogViewModel.CauCanhBaoSauForm(cu, Dong(id, 40, cu.CfgUpdatedAt), daLuu: true)!);

        // Dòng tổng kết ghi NGAY KHI bấm "Chạy chương trình" — từ đây trình duyệt mở và chương trình khuyến mãi
        // được tạo THẬT, nên nó cũng là câu người dùng đọc.
        yield return (nameof(KmDialogViewModel.CauBatDauChay), KmDialogViewModel.CauBatDauChay(
            ThamSoKmMau, soShop: 3, login: LoginA, tuDong: true, soNgay: 7,
            bayGio: new DateTime(2026, 8, 22, 3, 0, 0)));

        // Dòng tổng kết của nút "Chạy Combo" (26/08/2026) — đường chạy TAY của vòng combo. Cùng lý do với
        // `CauBatDauChay`: từ đây trình duyệt mở và combo được tạo THẬT trên mọi shop.
        yield return (nameof(KmDialogViewModel.CauBatDauChayCombo), KmDialogViewModel.CauBatDauChayCombo(
            ThamSoComboMau, soShop: 3, login: LoginA, comboBat: true, soNgay: 3,
            bayGio: new DateTime(2026, 8, 22, 3, 0, 0)));
        yield return (nameof(KmDialogViewModel.CauBatDauChayCombo), KmDialogViewModel.CauBatDauChayCombo(
            ThamSoComboMau, soShop: 3, login: LoginA, comboBat: false, soNgay: 3,
            bayGio: new DateTime(2026, 8, 22, 3, 0, 0)));

        // Dòng nhật ký của vòng CHECK ĐƠN khi nó nhường khung cho KM — pha B sẽ sửa đúng câu này.
        yield return (nameof(XuLyDonShopee.Core.Services.KmLichTuDong.CauHoanVong),
            XuLyDonShopee.Core.Services.KmLichTuDong.CauHoanVong(
                new DateTime(2026, 8, 22, 3, 0, 0), new DateTime(2026, 8, 22, 6, 0, 0)));

        yield return ("(MoTaChuTri)", new KmDialogViewModel(id, LoginA, 3, cu, MayMot).MoTaChuTri);

        // (vòng 6, Z4) Vùng pha B sắp mổ: câu "tài khoản đang chạy ở máy khác" (B1 sẽ thêm vế "đã nhả khoá")
        // và hai câu tổng kết lượt KM người dùng đọc mỗi đêm.
        yield return (nameof(XuLyDonShopee.App.Services.AccountSession.CauBiTuChoiKhoa),
            XuLyDonShopee.App.Services.AccountSession.CauBiTuChoiKhoa("PC-KHO-2"));
        yield return (nameof(XuLyDonShopee.App.Services.AccountSession.CauBiTuChoiKhoa),
            XuLyDonShopee.App.Services.AccountSession.CauBiTuChoiKhoa(null));

        // Cả ba kết cục của câu tổng kết (xong · hết khung · captcha) — mỗi kết cục là một nhánh chữ khác nhau.
        foreach (var kq in CacKetQuaKmMau())
        {
            yield return (nameof(AccountsViewModel.CauTongKetKm), AccountsViewModel.CauTongKetKm(kq, true));
            yield return (nameof(AccountsViewModel.CauTongKetKm), AccountsViewModel.CauTongKetKm(kq, false));
            // Vòng COMBO (25/08/2026) dùng CHUNG thân câu, chỉ đổi tên việc ở đầu — nhưng vẫn phải qua lưới rà
            // chuỗi: đây là dòng người dùng đọc mỗi sáng sau đêm chạy combo.
            yield return (nameof(AccountsViewModel.CauTongKetCombo), AccountsViewModel.CauTongKetCombo(kq, true));
            yield return (nameof(AccountsViewModel.CauTongKetCombo), AccountsViewModel.CauTongKetCombo(kq, false));
            yield return (nameof(AccountsViewModel.CauTrangThaiKm), AccountsViewModel.CauTrangThaiKm(kq));
        }

        // Hai dòng của nhịp lịch tự động: "không phải chủ trì" và "cấu hình không dùng được" — lấy qua ĐÚNG
        // đường sinh ra chúng (chúng là private), tức test cũng khoá luôn việc chúng còn được ghi ra hay không.
        foreach (var dong in DongNhipLichKm(svc, id))
        {
            yield return ("(nhịp lịch KM)", dong);
        }
    }

    /// <summary>Bốn kết cục của một lượt KM/Combo mà hai câu tổng kết phải nói khác nhau: chạy xong (có shop
    /// hỏng) · hết khung · rơi captcha · <b>bị trần CHẾ ĐỘ THỬ cắt</b> (26/08/2026). Một kết cục thì lưới chỉ
    /// rà được một nhánh chữ.</summary>
    private static IEnumerable<XuLyDonShopee.Core.Services.KetQuaKm> CacKetQuaKmMau()
    {
        var shops = new[]
        {
            new XuLyDonShopee.Core.Services.KetQuaKmMotShop("1", "Shop Một", true, 12, null, null),
            new XuLyDonShopee.Core.Services.KetQuaKmMotShop("2", "Shop Hai", false, 0, null, "timeout"),
        };
        yield return new XuLyDonShopee.Core.Services.KetQuaKm(shops, false, null);
        yield return new XuLyDonShopee.Core.Services.KetQuaKm(shops, false, null, HetKhung: true, SoShopChuaChay: 4);
        yield return new XuLyDonShopee.Core.Services.KetQuaKm(shops, true, null);
        yield return new XuLyDonShopee.Core.Services.KetQuaKm(
            shops, false, null, SoShopChuaChay: 10, TranThuDaCham: 2);
    }

    /// <summary>Chạy nhịp lịch KM ở hai cảnh sinh ra hai dòng nhật ký hướng dẫn, trả về đúng các dòng đó.</summary>
    private static IEnumerable<string> DongNhipLichKm(AppServices svc, long id)
    {
        var trongKhung = new DateTime(2026, 8, 22, 2, 5, 0);
        svc.MayNay = () => MayMot;   // chủ trì đang là "may-khac:orders" ⇒ máy này đứng ngoài
        new AccountsViewModel(svc).NhipLichKmAsync(trongKhung).GetAwaiter().GetResult();

        // …rồi cảnh cấu hình hỏng: máy này chủ trì nhưng GIỜ SAI ĐỊNH DẠNG.
        //
        // Cảnh dựng đã đổi HAI LẦN, ghi lại để đừng đổi ngược:
        //  · trước 25/08/2026 dùng `GioBatDau = "01:00"` (mốc quá khứ so với lượt 02:05) — bản đẩy chương trình
        //    đi 180 ngày làm cấu hình ấy thành HỢP LỆ, cảnh im lặng không sinh dòng nào để rà;
        //  · rồi dùng `GioKetThuc < GioBatDau` — bản 26/08/2026 BỎ hẳn luật đó (hai đầu khoảng cùng một giờ),
        //    nên cảnh lại im lặng.
        // Vế ĐỊNH DẠNG GIỜ thì không phụ thuộc vào hai quyết định trên.
        svc.KmConfig.Save(svc.KmConfig.Get(id) with { OwnerMachine = MayMot, GioBatDau = "2h00" });
        new AccountsViewModel(svc).NhipLichKmAsync(trongKhung).GetAwaiter().GetResult();

        return svc.Log.Snapshot(LoginA).Select(e => e.Message).Where(m => m.Contains("Lịch KM tự động")).ToList();
    }

    /// <summary>Nhãn của MỌI nút trên <c>KmDialog</c> THẬT (sau một lượt layout thật).</summary>
    private static List<string> NhanCacNutTrenForm() => CacNutTrenForm().Select(x => x.Nhan).ToList();

    /// <summary>Tooltip của nút mang nhãn <paramref name="nhan"/> trên form THẬT.</summary>
    private static string TooltipNut(string nhan)
        => CacNutTrenForm().First(x => x.Nhan == nhan).Nut.ToolTip?.ToString() ?? "";

    /// <summary>Dựng <c>KmDialog</c> THẬT, cho đi qua một lượt layout thật rồi trả về (nút, nhãn) của mọi nút.
    /// <para>Layout trên chính <c>Window</c> KHÔNG chạy (window chưa hiện thì nó không phải gốc của cây layout
    /// nào) — đo lúc đó thì binding còn <c>Unattached</c> và mọi <c>Text</c> đọc ra RỖNG, tức lại một phép đo
    /// rỗng. Đo trên <c>Content</c>.</para></summary>
    private static List<(Button Nut, string Nhan)> CacNutTrenForm()
    {
        var ds = new List<(Button, string)>();
        foreach (var b in DungFormRoiGomMoiTab().OfType<Button>())
        {
            var chu = CayCon(b).OfType<TextBlock>().Select(t => t.Text?.Trim() ?? "").FirstOrDefault(t => t.Length > 0);
            if (chu is { Length: > 0 }) { ds.Add((b, chu)); }
        }
        return ds;
    }

    /// <summary>
    /// Dựng <c>KmDialog</c> THẬT rồi trả về MỌI phần tử trực quan của nó — <b>gom QUA TỪNG TAB</b>.
    /// <para><b>Vì sao phải lặp qua tab</b> (26/08/2026, form chia tab): <c>TabControl</c> của WPF KHÔNG dựng
    /// nội dung của tab CHƯA CHỌN vào cây trực quan. Bản cũ đo đúng một lượt layout, nên sau khi chia tab nó
    /// sẽ rà <b>THIẾU chứ không ĐỎ</b> — hai phần ba form biến mất khỏi mọi vòng rà mà mọi test vẫn xanh, đúng
    /// loại phép đo RỖNG repo này đã dính nhiều lần. Mỗi tab được chọn → layout lại → gom, rồi hợp cả ba
    /// (khử trùng theo THAM CHIẾU: phần nằm ngoài tab lượt nào cũng gặp).</para>
    /// <para>Lưới canh chính vòng lặp này là <see cref="FormKm_VongRaDiQuaCaBaTab"/>.</para>
    /// </summary>
    private static List<DependencyObject> DungFormRoiGomMoiTab()
    {
        var cf = new KmConfig(1, TuDong: true, SoNgay: 7, PhanTram: 25, GioBatDau: "08:00",
            GioKetThuc: "09:00", LanChayCuoi: null) { OwnerMachine = MayHai };
        var dlg = new KmDialog(new KmDialogViewModel(1, LoginA, 3, cf, MayMot));
        var than = (FrameworkElement)dlg.Content;

        var ds = new List<DependencyObject>();
        var daGap = new HashSet<DependencyObject>();
        for (var i = 0; i < dlg.KmTabs.Items.Count; i++)
        {
            dlg.KmTabs.SelectedIndex = i;
            than.Measure(new Size(900, 800));
            than.Arrange(new Rect(0, 0, 900, 800));
            than.UpdateLayout();

            foreach (var el in CayCon(than))
            {
                if (daGap.Add(el)) { ds.Add(el); }
            }
        }
        return ds;
    }

    private static IEnumerable<DependencyObject> CayCon(DependencyObject goc)
    {
        var n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(goc);
        for (var i = 0; i < n; i++)
        {
            var con = System.Windows.Media.VisualTreeHelper.GetChild(goc, i);
            yield return con;
            foreach (var chau in CayCon(con)) { yield return chau; }
        }
    }

    /// <summary>Các cụm nằm giữa hai dấu ngoặc kép trong một câu.</summary>
    private static IEnumerable<string> CumTrongNgoacKep(string cau)
    {
        var phan = cau.Split('"');
        for (var i = 1; i < phan.Length; i += 2)
        {
            var cum = phan[i].Trim();
            if (cum.Length > 0) { yield return cum; }
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
