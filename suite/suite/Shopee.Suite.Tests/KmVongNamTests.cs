// System.IO KHÔNG có trong danh sách implicit của SDK desktop (net8.0-windows) — thiếu là Path/File không tồn tại.
using System.IO;
using System.Linq;
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
/// <b>Vòng 5 — đóng hai blocker do chính vòng 4 sinh ra</b> (22/08/2026).
/// <list type="bullet">
/// <item><b>Y1</b> (CHẶN PHÁT HÀNH) — lượt khép vòng của <c>ApBanHubTraVe</c> gán thẳng CẢ HAI MỐC CHẠY từ bản
/// hub trả về. Mà bản đó gộp bằng đồng hồ CỦA HUB: hub chậm &gt; 6h (VM khôi phục snapshot / NTP chết) là
/// <c>LocMoc</c> bỏ mốc chạy hợp lệ về <c>null</c> ⇒ mốc của máy vừa chạy KM bị xoá trắng ⇒
/// <c>DenGioChay</c> trả <c>true</c> ⇒ <b>chạy lại trọn vòng shop, tạo chương trình khuyến mãi THẬT lần thứ
/// hai</b>, mỗi 60 giây tới 06:00, im lặng tuyệt đối.</item>
/// <item><b>Y2</b> — X4 (vòng 4) mới đóng MỘT trong hai cửa chủ trì: lượt backfill đã nằm trong
/// <c>if (coHub)</c>, nhưng hook <c>MayNay</c> thì vẫn rót vô điều kiện, nên máy MỘT MÌNH không hub vẫn tự
/// đóng dấu <c>owner_machine</c> lúc lưu form — rồi <c>machine.json</c> bị tạo lại là KM tự động chết hẳn.</item>
/// </list>
/// <para><b>Vì sao 39 test vòng 4 không bắt được Y1:</b> helper dựng cảnh của bộ đó LUÔN đặt
/// <c>LanChayCuoi: null</c> và không bao giờ đặt <c>LanThuTuDong</c> ⇒ không một assert nào chạm hai mốc chạy
/// trên đường đẩy-nhận-lại. Ở file này hai mốc đó là NHÂN VẬT CHÍNH, và helper <see cref="Dong"/> bắt buộc
/// truyền chúng tường minh.</para>
/// </summary>
public class KmVongNamTests : IDisposable
{
    private const string LoginA = "sub-v5@shopee.vn";
    private const string MayMot = "may-mot:orders";
    private const string MayHai = "may-hai:orders";

    /// <summary>Đồng hồ ĐÚNG của máy A (giờ địa phương) — đang ở trong khung KM 2–6h.</summary>
    private static readonly DateTime BayGioMayA = new(2026, 8, 22, 2, 10, 0);

    /// <summary>Lượt KM đêm nay của máy A vừa chốt xong lúc 02:05, và mốc thử của khung này là 02:00.</summary>
    private static readonly DateTime ChayCuoi = new(2026, 8, 22, 2, 5, 0);
    private static readonly DateTime ThuTuDong = new(2026, 8, 22, 2, 0, 0);

    /// <summary>
    /// Đồng hồ HUB, chậm 3 NGÀY so với thực tế. Cố ý chọn cỡ NGÀY chứ không phải "6h + một chút": phép so
    /// "quá xa tương lai" chạy trên mốc tuyệt đối, mà mốc của máy là giờ ĐỊA PHƯƠNG — lấy khoảng cách sát biên
    /// thì kết quả test phụ thuộc múi giờ của máy chạy CI. 3 ngày thì lệch múi giờ nào cũng không lật được.
    /// </summary>
    private static readonly DateTimeOffset GioHubCham = new(2026, 8, 19, 2, 0, 0, TimeSpan.Zero);

    /// <summary>Đồng hồ hub ĐÚNG — vế đối chứng.</summary>
    private static readonly DateTimeOffset GioHubDung = new(2026, 8, 22, 3, 0, 0, TimeSpan.Zero);

    private readonly List<string> _dbPaths = new();
    private readonly List<AppServices> _services = new();

    /// <summary>Một "máy": file DB riêng + bộ dịch vụ riêng + một tài khoản mang <see cref="LoginA"/>.</summary>
    private (AppServices Services, long AccountId) DungMay()
    {
        var path = Path.Combine(Path.GetTempPath(), $"suite_km_v5_{Guid.NewGuid():N}.db");
        _dbPaths.Add(path);
        var services = new AppServices(path);
        _services.Add(services);
        services.Accounts.Insert(new Account { Email = LoginA, Password = "p" });
        return (services, services.Accounts.GetAll().First(a => a.Email == LoginA).Id);
    }

    /// <summary>
    /// Dòng <c>km_config</c> của cảnh thử. <b>HAI MỐC CHẠY là tham số BẮT BUỘC, không có giá trị mặc định</b> —
    /// đúng chỗ helper vòng 4 để lọt Y1: nó khoá cứng <c>LanChayCuoi: null</c> cho "tiện" nên mọi ca dựng qua
    /// nó đều mù với hai trường này.
    /// </summary>
    private static KmConfig Dong(
        long id, DateTime? lanChayCuoi, DateTime? lanThuTuDong,
        int phanTram = 25, DateTime? cfgUpdatedAt = null, string chuTri = MayMot)
        => new(id, TuDong: true, SoNgay: 7, phanTram, GioBatDau: "02:00", GioKetThuc: "03:00",
            lanChayCuoi, lanThuTuDong)
        {
            CfgUpdatedAt = cfgUpdatedAt ?? new DateTime(2026, 8, 20, 9, 0, 0),
            OwnerMachine = chuTri,
        };

    /// <summary>Mô phỏng đúng việc hub làm với một lô đẩy: gộp từng dòng bằng luật DÙNG CHUNG
    /// (<see cref="KmHoSoGop.Gop"/> — hub LINK chính file nguồn này), dòng CHƯA TỒN TẠI thì <c>dangCo = den</c>
    /// theo <c>HubDatabase.UpsertKmProfiles</c>, và dùng ĐỒNG HỒ CỦA HUB.</summary>
    private static List<KmProfileItem> HubGop(
        IReadOnlyList<KmProfileItem> day, DateTimeOffset gioHub, KmProfileItem? dangCoTrenHub = null)
        => day.Select(p => KmHoSoGop.Gop(dangCoTrenHub ?? p, p, gioHub)).ToList();

    // ══════════════════════════ Y1 — mốc CHẠY chỉ TIẾN, không bao giờ bị xoá ══════════════════════════

    /// <summary>
    /// <b>HUB chậm giờ ⇒ nó trả về mốc chạy <c>null</c> ⇒ mốc cục bộ KHÔNG ĐƯỢC ĐỔI, và
    /// <c>DenGioChay</c> vẫn phải trả <c>false</c>.</b>
    /// <para>Đây là ca CHẶN PHÁT HÀNH của vòng 4, và cái giá của nó không phải một dòng log sai: máy A vừa chạy
    /// xong lượt KM 02:05, ≤3s sau worker đẩy, hub bỏ mốc, client ghi đè ⇒ nhịp 60 giây kế lại thấy "chưa chạy
    /// lần nào" ⇒ <b>mở trình duyệt tạo chương trình khuyến mãi THẬT lần thứ hai trên mọi shop</b>. Không cần
    /// máy client nào sai giờ — chỉ mình hub.</para>
    /// <para>Lượt này VẪN ghi đĩa (mốc sửa bị hub kẹp nên nhóm cấu hình thật sự đổi), tức phép đo không phải
    /// "không làm gì cả" mà đúng là "có ghi, nhưng hai mốc chạy sống sót".</para>
    /// </summary>
    [Fact]
    public void HubChamGio_TraVeMocChayRong_ThiMocCucBoKhongDoi_VaKhongChayLap()
    {
        var (svc, id) = DungMay();
        svc.KmConfig.Save(Dong(id, ChayCuoi, ThuTuDong));

        var day = OrdersModuleHost.ChupHoSoKm(svc);
        var traVe = HubGop(day, GioHubCham);

        // Tiền đề của cảnh: hub ĐÃ bỏ trắng hai mốc chạy hợp lệ. Không có phép kiểm này thì test vẫn xanh kể
        // cả khi hub trả về nguyên mốc — tức đo rỗng.
        Assert.NotNull(day[0].LanChayCuoi);
        Assert.NotNull(day[0].LanThuTuDong);
        Assert.Null(traVe[0].LanChayCuoi);
        Assert.Null(traVe[0].LanThuTuDong);

        var doi = OrdersModuleHost.ApBanHubTraVe(svc, day, traVe, BayGioMayA, new());

        var sau = svc.KmConfig.Get(id);
        Assert.Equal(1, doi);                       // có ghi thật (mốc sửa bị kẹp) — không phải "không làm gì"
        Assert.Equal(ChayCuoi, sau.LanChayCuoi);    // …mà hai mốc chạy vẫn nguyên
        Assert.Equal(ThuTuDong, sau.LanThuTuDong);

        // Và hệ quả cuối cùng, thứ người dùng thật sự trả giá: KHÔNG chạy lại lượt KM trong cùng khung.
        Assert.False(KmLichTuDong.DenGioChay(sau, BayGioMayA, LoaiChuongTrinh.ChuongTrinhShop));
    }

    /// <summary>
    /// <b>Vế NGƯỢC, và là cái chặn bản vá đi quá tay: hub trả mốc chạy MỚI HƠN ⇒ mốc cục bộ phải TIẾN LÊN.</b>
    /// <para>Luật là <c>Max</c>, không phải "giữ nguyên bản cũ". Máy HAI vừa chạy lượt KM và đẩy mốc lên trước;
    /// bản hub trả về cho máy MỘT mang mốc ấy. Vá thành "mốc chạy thì đừng đụng" là máy một không bao giờ biết
    /// máy hai đã chạy ⇒ đêm nay nó chạy lại đúng lượt đó — vẫn là chạy trùng, chỉ đổi đường tới.</para>
    /// </summary>
    [Fact]
    public void HubTraMocChayMoiHon_ThiMocCucBoTienLen()
    {
        var (svc, id) = DungMay();
        var chayCuoiCu = new DateTime(2026, 8, 15, 2, 5, 0);
        svc.KmConfig.Save(Dong(id, chayCuoiCu, lanThuTuDong: null));

        var day = OrdersModuleHost.ChupHoSoKm(svc);

        // Trên hub đã có dòng của MÁY HAI: cùng cấu hình, cùng mốc sửa, nhưng mốc chạy mới hơn hẳn.
        var trenHub = day[0] with
        {
            LanChayCuoi = KmMocThoiGian.LenDay(ChayCuoi),
            LanThuTuDong = KmMocThoiGian.LenDay(ThuTuDong),
        };
        var traVe = HubGop(day, GioHubDung, trenHub);
        Assert.Equal(KmMocThoiGian.LenDay(ChayCuoi), traVe[0].LanChayCuoi);   // tiền đề: hub GIỮ mốc mới

        var doi = OrdersModuleHost.ApBanHubTraVe(svc, day, traVe, BayGioMayA, new());

        var sau = svc.KmConfig.Get(id);
        Assert.Equal(1, doi);
        Assert.Equal(ChayCuoi, sau.LanChayCuoi);      // TIẾN lên mốc của máy hai…
        Assert.Equal(ThuTuDong, sau.LanThuTuDong);
        Assert.False(KmLichTuDong.DenGioChay(sau, BayGioMayA, LoaiChuongTrinh.ChuongTrinhShop));   // …nên đêm nay thôi chạy trùng
    }

    /// <summary>
    /// <b>Nhóm CẤU HÌNH + mốc SỬA vẫn được GHI ĐÈ như vòng 4 — bản vá Y1 không được làm hỏng X1.</b>
    /// <para>Khép vòng sinh ra để chữa đúng ca này: máy khác sửa sau thắng trên hub, và máy này phải nhận
    /// TRỌN gói cấu hình đó về (kể cả chủ trì), chứ không phải gộp lại bằng đồng hồ của chính nó.</para>
    /// </summary>
    [Fact]
    public void NhomCauHinhVaMocSua_VanBiGhiDeTronGoi()
    {
        var (svc, id) = DungMay();
        svc.KmConfig.Save(Dong(id, ChayCuoi, ThuTuDong, phanTram: 25, chuTri: MayMot));

        var day = OrdersModuleHost.ChupHoSoKm(svc);

        // Máy HAI sửa SAU ⇒ bản của nó thắng trên hub, mang trọn cấu hình + chủ trì của nó. MỌI trường của nhóm
        // cấu hình đều đổi giá trị ở đây, kể cả `TuDong` và `SoNgay`: trường nào giữ nguyên giá trị cũ thì phép
        // đo của trường đó là phép đo RỖNG — đúng hình dạng lỗi đã để lọt Y1.
        var mocMoi = new DateTime(2026, 8, 21, 9, 0, 0);
        var mayHaiSuaSau = day[0] with
        {
            TuDong = false,
            SoNgay = 3,
            PhanTram = 40,
            GioBatDau = "03:00",
            GioKetThuc = "04:00",
            OwnerMachine = MayHai,
            CfgUpdatedAt = KmMocThoiGian.LenDay(mocMoi),
        };
        var traVe = HubGop(day, GioHubDung, mayHaiSuaSau);

        var doi = OrdersModuleHost.ApBanHubTraVe(svc, day, traVe, BayGioMayA, new());

        var sau = svc.KmConfig.Get(id);
        Assert.Equal(1, doi);
        Assert.False(sau.TuDong);
        Assert.Equal(3, sau.SoNgay);
        Assert.Equal(40, sau.PhanTram);
        Assert.Equal("03:00", sau.GioBatDau);
        Assert.Equal("04:00", sau.GioKetThuc);
        Assert.Equal(MayHai, sau.OwnerMachine);
        Assert.Equal(mocMoi, sau.CfgUpdatedAt);
        Assert.Equal(ChayCuoi, sau.LanChayCuoi);      // …và hai mốc chạy vẫn đi đường riêng của chúng
        Assert.Equal(ThuTuDong, sau.LanThuTuDong);

        // Cấu hình ĐỔI ⇒ vẫn phải nói ra, y vòng 4.
        Assert.Contains(svc.Log.Snapshot(LoginA), e => e.Message.Contains("cấu hình vừa ĐỔI"));
    }

    /// <summary>
    /// (THUẦN) Luật của riêng lượt khép vòng, đo trực tiếp không qua DB: hub trả <c>null</c> ⇒ giữ mốc cũ; hub
    /// trả mốc mới hơn ⇒ tiến; hub trả mốc CŨ HƠN ⇒ vẫn giữ mốc cũ (mốc chạy là bánh cóc, không bao giờ lùi).
    /// </summary>
    [Fact]
    public void ApBanKhepVong_MocChayChiTien_KhongBaoGioLui()
    {
        var cu = Dong(1, ChayCuoi, ThuTuDong);
        var hoSo = OrdersModuleHost.ThanhHoSo(LoginA, cu);

        var hubBoMoc = hoSo with { LanChayCuoi = null, LanThuTuDong = null };
        Assert.Equal(ChayCuoi, OrdersModuleHost.ApBanKhepVong(cu, hubBoMoc).LanChayCuoi);
        Assert.Equal(ThuTuDong, OrdersModuleHost.ApBanKhepVong(cu, hubBoMoc).LanThuTuDong);

        var hubLuiMoc = hoSo with { LanChayCuoi = KmMocThoiGian.LenDay(ChayCuoi.AddDays(-3)) };
        Assert.Equal(ChayCuoi, OrdersModuleHost.ApBanKhepVong(cu, hubLuiMoc).LanChayCuoi);

        var hubTienMoc = hoSo with { LanChayCuoi = KmMocThoiGian.LenDay(ChayCuoi.AddDays(3)) };
        Assert.Equal(ChayCuoi.AddDays(3), OrdersModuleHost.ApBanKhepVong(cu, hubTienMoc).LanChayCuoi);

        // Máy chưa có mốc nào mà hub có ⇒ nhận: `null` thua mọi giá trị.
        var chuaChay = Dong(1, lanChayCuoi: null, lanThuTuDong: null);
        Assert.Equal(ChayCuoi, OrdersModuleHost.ApBanKhepVong(chuaChay, hoSo).LanChayCuoi);
    }

    // ══════════════════════════ Y2 — cửa chủ trì thứ hai: hook MayNay ══════════════════════════

    /// <summary>
    /// <b>Không có hub ⇒ hook <c>MayNay</c> trả RỖNG, nên lượt lưu form KHÔNG đóng dấu chủ trì.</b>
    /// <para><b>(vòng 6, Z1c) Assert đổi từ <c>Assert.Null(svc.MayNay)</c> sang giá trị hook TRẢ VỀ.</b> Bản
    /// vòng 5 khoá HÌNH DẠNG CÀI ĐẶT ("hook không được rót") chứ không khoá HÀNH VI ("máy không hub thì không
    /// biết mình là ai"), và chính hình dạng ấy là con bug: hook không rót nghĩa là nó cũng không bao giờ được
    /// rót SAU đó, nên máy cấu hình hub giữa chừng vĩnh viễn trả rỗng ⇒ <c>LaMayChuTri(owner, "")</c> trả true
    /// ⇒ nó chạy lượt KM của máy khác. Nay hook rót vô điều kiện nhưng ĐỌC TƯƠI — hành vi ở cảnh này y hệt,
    /// nên đây là đổi VẾ ĐO chứ không phải nới assert; xem
    /// <see cref="WireKhiChuaCoHub_RoiHubBatLenGiuaChung_ThiMayNayTraIdThat"/> cho vế mà bản cũ để lọt.</para>
    /// <para>Ca thật: người dùng MỘT máy, không hub, mở form đổi 5% → 25% rồi bấm "Đóng". Vòng 4 đã chặn lượt
    /// backfill ở cửa này, nhưng <c>MayNay</c> vẫn rót vô điều kiện nên <c>BanLuuSauForm</c> vẫn đóng dấu
    /// <c>owner_machine</c>. Hôm sau <c>machine.json</c> bị tạo lại (dọn AppData / hồ sơ Windows mới / cài lại
    /// máy) ⇒ id mới ⇒ <c>LaMayChuTri</c> false ⇒ <b>KM tự động chết hẳn trên máy duy nhất</b>, và vòng check
    /// đơn cũng thôi nhường khung.</para>
    /// <para>Đoạn dựng cảnh lặp lại ĐÚNG ba dòng của <c>AccountsViewModel.MoFormKm</c> (đọc <c>MayNay</c> →
    /// <c>BanLuuSauForm</c> → <c>Save</c>): đường thật đi qua <c>DialogService.ShowKm</c>, một modal WPF không
    /// dựng được headless. Chỗ nối form ⇄ hook vẫn là chỗ chưa có lưới — xem NỢ KỸ THUẬT.</para>
    /// </summary>
    [Fact]
    public void KhongCoHub_ThiLuuFormKhongDongDauChuTri()
    {
        var (svc, id) = DungMay();
        var banNap = Dong(id, lanChayCuoi: null, lanThuTuDong: null, phanTram: 5, chuTri: "");
        svc.KmConfig.Save(banNap);

        try
        {
            OrdersModuleHost.WireKmSync(svc, coHub: () => false);

            Assert.Equal("", svc.MayNay?.Invoke() ?? "");   // hook TRẢ RỖNG…
            Assert.Equal("", LuuFormNhuNguoiDung(svc, id, banNap).OwnerMachine);   // …nên chủ trì vẫn RỖNG
        }
        finally
        {
            OrdersModuleHost.DungNhipHoSoKm();
        }
    }

    /// <summary>
    /// <b>Vế ĐỐI CHỨNG — CÓ hub thì vẫn đóng dấu chủ trì y vòng 4.</b> Thiếu vế này thì một bản "không bao giờ
    /// rót <c>MayNay</c>" cũng xanh, mà bản đó vô hiệu hoá trọn cửa chủ trì trên đúng cảnh nó sinh ra để phục
    /// vụ (nhiều máy dùng chung một tài khoản).
    /// </summary>
    [Fact]
    public void CoHub_ThiLuuFormVanDongDauChuTri()
    {
        var (svc, id) = DungMay();
        var banNap = Dong(id, lanChayCuoi: null, lanThuTuDong: null, phanTram: 5, chuTri: "");
        svc.KmConfig.Save(banNap);

        try
        {
            OrdersModuleHost.WireKmSync(svc, coHub: () => true);

            var may = svc.MayNay!();
            Assert.NotEqual("", may);
            Assert.Equal(may, LuuFormNhuNguoiDung(svc, id, banNap).OwnerMachine);
        }
        finally
        {
            OrdersModuleHost.DungNhipHoSoKm();
        }
    }

    /// <summary>Đúng đường <c>AccountsViewModel.MoFormKm</c> đi khi người dùng sửa % rồi bấm "Đóng": đọc hook
    /// <c>MayNay</c> → dựng bản lưu → ghi. Trả về dòng đọc lại sau khi ghi.</summary>
    private static KmConfig LuuFormNhuNguoiDung(AppServices svc, long id, KmConfig banNap)
    {
        var mayNay = svc.MayNay?.Invoke() ?? "";
        var tuForm = banNap with { PhanTram = 25 };   // người dùng đổi 5% → 25%
        var luuBan = KmDialogViewModel.BanLuuSauForm(
            svc.KmConfig.Get(id), banNap, tuForm, mayNay, new DateTime(2026, 8, 22, 11, 0, 0));

        Assert.NotNull(luuBan);   // có sửa thật ⇒ phải có bản để lưu, không thì phép đo dưới là đo rỗng
        svc.KmConfig.Save(luuBan!);
        return svc.KmConfig.Get(id);
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
