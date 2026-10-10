// System.IO KHÔNG có trong danh sách implicit của SDK desktop (net8.0-windows) — thiếu là Path/File không tồn tại.
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Shopee.Suite.Infrastructure;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;

namespace Shopee.Suite.Tests;

/// <summary>
/// <b>Vòng 6 — đóng blocker do chính vòng 5 sinh ra</b> (22/08/2026).
/// <para><b>Z1 (CHẶN PHÁT HÀNH)</b>: bản vá Y2 của vòng 5 bọc hook <c>MayNay</c> vào <c>if (coHub)</c>, mà
/// <c>coHub</c> được chụp MỘT LẦN lúc khởi tạo module. Hub thì bật được LÚC CHẠY — nút "Kết nối ngay" ở Cài
/// đặt gọi <c>CoordinationRuntime.Reconnect()</c>, và app tự quảng cáo "áp dụng ngay, không cần khởi động
/// lại". Luồng cài máy mới vào fleet đi đúng vào đó: cài app trên B → chưa cấu hình hub → cấu hình + "Kết nối
/// ngay" → ≤3s sau worker kéo hồ sơ về, B nhận <c>OwnerMachine = "may-A:orders"</c> → 02:00 <c>mayNay</c> vẫn
/// RỖNG ⇒ <c>LaMayChuTri(owner, "")</c> trả <b>true</b> ⇒ B chạy trọn lượt KM của tài khoản A đang chủ trì,
/// <b>tạo chương trình khuyến mãi THẬT</b>, rồi ghi <c>LanThuTuDong</c> (mốc dùng chung) ⇒ A mất trắng đêm.</para>
/// <para><b>Hai vòng liền cùng một gốc</b>: sửa mà không soi VÒNG ĐỜI của thứ mình đụng. Vòng 4 gói chung hai
/// nhóm có ngữ nghĩa ngược nhau; vòng 5 đóng băng một giá trị đổi được lúc chạy. Cả hai lần khuôn đúng đã nằm
/// sẵn trong cùng thư mục — <c>services.HubDaCauHinh = () =&gt; CoordinationRuntime.Client is not null;</c> ở
/// <c>OrdersModuleHost.HubRead.cs</c>, đọc TƯƠI mỗi lần gọi.</para>
/// <para><b>Vì sao 6 test vòng 5 không bắt được Z1:</b> cả hai ca chủ trì của bộ đó gọi <c>WireKmSync</c> với
/// một <c>bool</c> CỐ ĐỊNH rồi đo ngay — không ca nào ĐỔI trạng thái hub sau lượt wire, tức không ca nào chạm
/// vào chính cái trục thời gian mà lỗi nằm trên. Ở file này "hub bật lên GIỮA CHỪNG" là nhân vật chính.</para>
/// </summary>
public class KmVongSauTests : IDisposable
{
    private const string LoginA = "sub-v6@shopee.vn";

    private readonly List<string> _dbPaths = new();
    private readonly List<AppServices> _services = new();

    /// <summary>Một "máy": file DB riêng + bộ dịch vụ riêng + một tài khoản mang <see cref="LoginA"/>.</summary>
    private (AppServices Services, long AccountId) DungMay()
    {
        var path = Path.Combine(Path.GetTempPath(), $"suite_km_v6_{Guid.NewGuid():N}.db");
        _dbPaths.Add(path);
        var services = new AppServices(path);
        _services.Add(services);
        services.Accounts.Insert(new Account { Email = LoginA, Password = "p" });
        return (services, services.Accounts.GetAll().First(a => a.Email == LoginA).Id);
    }

    /// <summary>Dòng <c>km_config</c> đang BẬT lịch KM và CHƯA có máy chủ trì — đúng hình dạng dòng cũ mà lượt
    /// backfill sinh ra để đóng dấu.</summary>
    private static KmConfig LichBatChuaCoChuTri(long id) => new(
        id, TuDong: true, SoNgay: 7, PhanTram: 25, GioBatDau: "02:00", GioKetThuc: "03:00", LanChayCuoi: null);

    // ══════════════════════════ Z1(a)(c) — hook MayNay đọc TƯƠI ══════════════════════════

    /// <summary>
    /// <b>Wire khi CHƯA có hub → hub bật lên giữa chừng ⇒ <c>MayNay</c> trả ID THẬT, không cần khởi động
    /// lại.</b> Và trước lúc bật thì trả RỖNG.
    /// <para>Đây là phép đo mà vòng 5 không có: nó chỉ hỏi "hook có được rót không" ở hai cảnh TĨNH. Bản vòng 5
    /// (<c>if (coHub) services.MayNay = …</c>) làm test này ĐỎ ở dòng cuối — hook không tồn tại nên máy B
    /// vĩnh viễn "không biết mình là ai", và <c>LaMayChuTri(owner, "")</c> mở cửa cho nó chạy lượt KM của A.</para>
    /// </summary>
    [Fact]
    public void WireKhiChuaCoHub_RoiHubBatLenGiuaChung_ThiMayNayTraIdThat()
    {
        var (svc, _) = DungMay();
        var coHub = false;

        try
        {
            OrdersModuleHost.WireKmSync(svc, () => coHub);

            // Chưa cấu hình hub: "chủ trì" chưa có nghĩa ⇒ rỗng. (Hook VẪN phải tồn tại — chính chỗ vòng 5 sai.)
            Assert.NotNull(svc.MayNay);
            Assert.Equal("", svc.MayNay!());

            coHub = true;   // người dùng cấu hình hub rồi bấm "Kết nối ngay" — KHÔNG khởi động lại app

            Assert.NotEqual("", svc.MayNay!());
        }
        finally
        {
            OrdersModuleHost.DungNhipHoSoKm();
        }
    }

    /// <summary>Chiều ngược: hub TẮT giữa chừng (đổi cấu hình hub thành rỗng rồi Reconnect) ⇒ hook trả lại
    /// RỖNG ngay. Thiếu vế này thì một bản "chụp một lần ở lượt gọi ĐẦU TIÊN" cũng xanh.</summary>
    [Fact]
    public void HubTatGiuaChung_ThiMayNayTraLaiRong()
    {
        var (svc, _) = DungMay();
        var coHub = true;

        try
        {
            OrdersModuleHost.WireKmSync(svc, () => coHub);
            Assert.NotEqual("", svc.MayNay!());

            coHub = false;

            Assert.Equal("", svc.MayNay!());
        }
        finally
        {
            OrdersModuleHost.DungNhipHoSoKm();
        }
    }

    // ══════════════════════════ Z1(b) — backfill chủ trì ở NHỊP, không ở lượt wire ══════════════════════════

    /// <summary>
    /// <b>Máy cấu hình hub SAU khởi động ⇒ lượt backfill máy chủ trì VẪN chạy, và chạy ĐÚNG MỘT LẦN.</b>
    /// <para>Vòng 3–5 gọi backfill ngay trong <c>WireKmSync</c>, mà lượt wire chạy đúng một lần lúc khởi tạo
    /// module — máy nào bật hub sau đó thì lượt ấy đã trôi qua với <c>mayNay</c> rỗng và <b>vĩnh viễn không
    /// backfill</b>, tức cửa chủ trì của nó vô hiệu mãi mãi: <c>LaMayChuTri("", …)</c> trả true nên mọi máy
    /// trong fleet đều tự cho mình quyền chạy.</para>
    /// <para>Đi qua ĐÚNG <see cref="OrdersModuleHost.KmSyncTickAsync"/> chứ không gọi thẳng hàm backfill: thứ
    /// đang đo là CHỖ NỐI (nhịp có gọi nó không, và có gọi TRƯỚC lượt đẩy không), mà chỗ nối không đo thì đứt
    /// lúc nào không ai biết — đúng bài học W3.</para>
    /// </summary>
    [Fact]
    public async Task CauHinhHubSauKhoiDong_ThiNhipVanBackfillChuTri_VaChiMotLan()
    {
        var (svc, id) = DungMay();
        svc.KmConfig.Save(LichBatChuaCoChuTri(id));
        var coHub = false;

        try
        {
            OrdersModuleHost.WireKmSync(svc, () => coHub);

            // Nhịp lúc CHƯA có hub: không biết mình là ai ⇒ không đóng dấu, và latch phải còn MỞ.
            await OrdersModuleHost.KmSyncTickAsync(svc);
            Assert.Equal("", svc.KmConfig.Get(id).OwnerMachine);

            coHub = true;
            await OrdersModuleHost.KmSyncTickAsync(svc);

            var may = svc.MayNay!();
            Assert.NotEqual("", may);
            Assert.Equal(may, svc.KmConfig.Get(id).OwnerMachine);

            // …và ĐÚNG MỘT LẦN: dòng mới thêm sau đó KHÔNG được đóng dấu lây. Lượt backfill là bước vá dữ liệu
            // CŨ; chạy lại là nó cướp chủ trì của mọi dòng mà máy khác vừa nhả ra.
            // ⚠ Vế này đo luật "một lần" ở tầng BỀN (khoá trong bảng `settings`), không đo latch trong bộ nhớ —
            // bỏ hẳn latch bộ nhớ thì test này VẪN xanh (đã thử phá xác nhận), cái đỏ là
            // `ChuaCoHub_ThiBackfillKhongChay_VaKhongChotLatch`. Latch bộ nhớ chỉ để nhịp 3 giây khỏi mở SQLite.
            var idHai = ThemTaiKhoan(svc, "sub-v6-hai@shopee.vn");
            svc.KmConfig.Save(LichBatChuaCoChuTri(idHai));

            await OrdersModuleHost.KmSyncTickAsync(svc);

            Assert.Equal("", svc.KmConfig.Get(idHai).OwnerMachine);
        }
        finally
        {
            OrdersModuleHost.DungNhipHoSoKm();
        }
    }

    /// <summary>Lượt backfill KHÔNG được chốt latch khi chưa biết id máy — nếu chốt thì máy bật hub sau khởi
    /// động rơi lại đúng lỗ Z1(b), chỉ dịch từ "lượt wire" sang "nhịp đầu tiên".</summary>
    [Fact]
    public void ChuaCoHub_ThiBackfillKhongChay_VaKhongChotLatch()
    {
        var (svc, id) = DungMay();
        svc.KmConfig.Save(LichBatChuaCoChuTri(id));
        var coHub = false;

        try
        {
            OrdersModuleHost.WireKmSync(svc, () => coHub);

            Assert.False(OrdersModuleHost.BackfillChuTriMotLan(svc));
            Assert.False(OrdersModuleHost.BackfillChuTriMotLan(svc));
            Assert.Equal("", svc.KmConfig.Get(id).OwnerMachine);

            coHub = true;
            Assert.True(OrdersModuleHost.BackfillChuTriMotLan(svc));      // latch còn mở ⇒ vẫn còn cơ hội
            Assert.False(OrdersModuleHost.BackfillChuTriMotLan(svc));     // …rồi mới chốt
        }
        finally
        {
            OrdersModuleHost.DungNhipHoSoKm();
        }
    }

    // ══════════════════════════ Z2 — câu chẩn đoán "hub kẹp mốc sửa" ══════════════════════════

    /// <summary>
    /// <b>Câu "hub kẹp mốc sửa" phải nêu ĐỦ BA khả năng, và phải nhắc <c>HUB CHẠY CHẬM</c>.</b>
    /// <para>Bản vòng 4 khẳng định một chiều: "gần như chắc chắn ĐỒNG HỒ MÁY NÀY chạy nhanh". Nhưng ca CHUẨN mà
    /// Y1 sinh ra để chống là <b>hub</b> chậm giờ còn mọi client đúng giờ — khi đó dòng này bắn ở MỌI máy trong
    /// fleet và bảo từng người đi sửa đồng hồ máy mình, trong khi thủ phạm là máy chủ hub, thứ câu cũ không hề
    /// nhắc tới. Đây là lần thứ năm trong đợt một câu chữ nói mạnh hơn/khác sự thật, và lưới rà chuỗi Y3 đã đi
    /// qua đúng câu này mà vẫn cho qua vì nó chỉ kiểm "tên nút có thật".</para>
    /// </summary>
    [Fact]
    public void CauHubKepMocSua_NeuDuBaKhaNang_CoNhacHubChamGio()
    {
        var cau = OrdersModuleHost.CauHubKepMocSua(
            new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 22, 3, 0, 0, TimeSpan.Zero));

        Assert.Contains("MÁY NÀY chạy nhanh", cau);
        Assert.Contains("HUB chạy chậm", cau);
        Assert.Contains("MÚI GIỜ", cau);

        // …và thôi khẳng định một chiều: câu cũ chốt "gần như chắc chắn" đúng một nguyên nhân.
        Assert.DoesNotContain("Gần như chắc chắn", cau, StringComparison.OrdinalIgnoreCase);
    }

    private static long ThemTaiKhoan(AppServices svc, string email)
    {
        svc.Accounts.Insert(new Account { Email = email, Password = "p" });
        return svc.Accounts.GetAll().First(a => a.Email == email).Id;
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
