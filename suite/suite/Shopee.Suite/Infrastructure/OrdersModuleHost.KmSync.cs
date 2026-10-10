using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Shopee.Core.Coordination;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Services;

namespace Shopee.Suite.Infrastructure;

// Partial của OrdersModuleHost: ĐỒNG BỘ HỒ SƠ KM của Shop giữa các máy qua hub — đẩy khi bẩn (khuôn
// .Mirror.cs) + kéo định kỳ (khuôn .GsheetConfig.cs).
//
// VÌ SAO CẦN: cấu hình "KM của Shop" nằm trong SQLite CỤC BỘ từng máy, nên bật "chạy tự động" ở máy A thì máy B
// đọc `TuDong = false` và cứ đặt vòng check đơn đè lên khung 2–6h của A — hai luồng tranh cùng một tài khoản
// Shopee. Và mốc `LanChayCuoi` cũng của riêng từng máy, nên đêm sau máy B chạy lại đúng lượt KM máy A vừa chạy.
public static partial class OrdersModuleHost
{
    /// <summary>Nhịp kiểm của worker hồ sơ KM — cũng là khoảng cách TỐI THIỂU giữa 2 lượt đẩy (vài lượt ghi
    /// <c>km_config</c> liên tiếp gộp thành một lượt đẩy). Khuôn <see cref="MirrorTick"/>.</summary>
    private static readonly TimeSpan KmSyncTick = TimeSpan.FromSeconds(3);

    /// <summary>Nhịp KÉO hồ sơ KM về. 60s là đủ tươi cho một lịch chạy theo NGÀY, và khớp nhịp kéo cấu hình
    /// GSheet nên hai worker không đẻ ra hai thói quen khác nhau cho cùng một việc.</summary>
    private static readonly TimeSpan KmPullEvery = TimeSpan.FromSeconds(60);

    /// <summary>Timer worker hồ sơ KM — PHẢI giữ tham chiếu static, không thì Timer bị GC gom là hết nhịp.</summary>
    private static Timer? _kmTimer;

    /// <summary>Có thay đổi CẦN đẩy (form KM lưu / chốt lượt KM / đánh dấu lượt thử tự động). true lúc khởi tạo
    /// để lượt đầu đẩy ngay. volatile: đặt từ thread UI, đọc từ thread timer.</summary>
    private static volatile bool _kmDirty = true;

    /// <summary>Chốt chống chồng lấn 2 lượt đồng bộ hồ sơ KM (0/1).</summary>
    private static int _kmSyncing;

    /// <summary>Mốc lượt KÉO gần nhất (kể cả lượt HỎNG) — chặn hub offline làm worker gọi mạng mỗi 3s.</summary>
    private static DateTimeOffset _kmLastPull = DateTimeOffset.MinValue;

    /// <summary>Mốc lượt ĐẨY HỎNG gần nhất (<see cref="DateTimeOffset.MinValue"/> = lượt gần nhất OK).
    /// <para><b>Vì sao cần riêng một mốc:</b> đẩy hỏng thì phải bật lại cờ bẩn (không thì thay đổi nằm im tới
    /// lượt ghi <c>km_config</c> kế tiếp — có thể là hôm sau). Nhưng cờ bẩn khiến nhịp 3s đẩy lại NGAY, tức
    /// một hub cũ chưa có route sẽ ăn một request mỗi 3 giây, mãi mãi. Mốc này giãn lượt thử lại về
    /// <see cref="KmPullEvery"/>, còn lượt đẩy sau một thay đổi THẬT (mốc rỗng) vẫn đi ngay.</para></summary>
    private static DateTimeOffset _kmPushLoiLuc = DateTimeOffset.MinValue;

    /// <summary>
    /// Bật worker đồng bộ hồ sơ KM + rót các hook module Đơn hàng cần (tra tên máy, tiếng chuông "hồ sơ đã
    /// đổi", và id máy này). Hub chưa cấu hình → worker quay không tải: mỗi nhịp chỉ kiểm
    /// <see cref="CoordinationRuntime.Client"/>.
    /// </summary>
    internal static void WireKmSync(AppServices services)
        => WireKmSync(services, () => CoordinationRuntime.Client is not null);

    /// <summary>
    /// Thân của <see cref="WireKmSync(AppServices)"/>, nhận cổng "máy này CÓ hub hay không" qua THAM SỐ để test
    /// dựng được cả hai nhánh (<see cref="CoordinationRuntime.Client"/> là static không gán được từ ngoài).
    /// </summary>
    /// <param name="coHub"><b>HÀM, không phải giá trị</b> — "có hub" là trạng thái ĐỔI ĐƯỢC LÚC CHẠY (nút
    /// "Kết nối ngay" ở Cài đặt gọi <c>CoordinationRuntime.Reconnect()</c>, và app tự quảng cáo "áp dụng ngay,
    /// không cần khởi động lại"). Chụp một lần lúc khởi tạo là lỗi CHẶN PHÁT HÀNH — xem thân hàm.</param>
    internal static void WireKmSync(AppServices services, Func<bool> coHub)
    {
        services.TenMay = TraTenMay;
        services.KmHoSoDaDoi = () => _kmDirty = true;

        // ⚠ HOOK `MayNay` RÓT VÔ ĐIỀU KIỆN, NHƯNG ĐỌC TƯƠI — đừng bọc nó lại vào `if (coHub())`.
        //
        // Nó phải trả RỖNG khi chưa có hub ("chủ trì" chỉ có nghĩa khi có NHIỀU MÁY — xem ca hỏng thứ hai), mà
        // "chưa có hub" là trạng thái ĐỔI ĐƯỢC LÚC CHẠY: nút "Kết nối ngay" ở Cài đặt gọi
        // `CoordinationRuntime.Reconnect()`, và app tự quảng cáo "áp dụng ngay, không cần khởi động lại".
        // Khuôn đúng nằm cách hai trăm dòng, cùng thư mục — `services.HubDaCauHinh` ở .HubRead.cs — và hợp
        // đồng của chính hook này (xmldoc `AppServices.MayNay`) viết: "Phải đọc TƯƠI mỗi lần gọi".
        //
        // Ca hỏng nếu ĐÓNG BĂNG lúc khởi tạo (phản biện vòng 5, Z1 — CHẶN PHÁT HÀNH): cài app trên máy B khi
        // chưa cấu hình hub ⇒ hook chụp "không hub" ⇒ người dùng cấu hình hub rồi bấm "Kết nối ngay" ⇒ ≤3s sau
        // worker kéo hồ sơ về, B nhận TuDong = true và OwnerMachine = "may-A:orders" ⇒ 02:00 `mayNay` vẫn RỖNG
        // ⇒ `LaMayChuTri(owner, "")` trả true ⇒ B chạy trọn lượt KM của tài khoản A đang chủ trì, TẠO CHƯƠNG
        // TRÌNH KHUYẾN MÃI THẬT, rồi ghi `LanThuTuDong` (mốc DÙNG CHUNG) ⇒ A mất trắng đêm.
        //
        // Ca hỏng nếu rót THẲNG `LeaseMachineId`, không qua cổng (phản biện vòng 3 X4 cho vế backfill, vòng 4
        // Y2 cho vế `MayNay`): `LeaseMachineId` KHÔNG BAO GIỜ rỗng (machine.json là file cục bộ, thiếu thì tự
        // sinh) nên nhánh "mayNay rỗng ⇒ không đóng dấu" ở tầng dưới KHÔNG đỡ được máy chạy độc lập. Người dùng
        // MỘT máy, không hub, mở form đổi 5% → 25% rồi bấm "Đóng" ⇒ `BanLuuSauForm` đóng dấu owner_machine =
        // id máy đó; hôm sau machine.json bị tạo lại (dọn AppData — đã có tiền lệ trên máy này; hoặc hồ sơ
        // Windows mới, hoặc cài lại máy) ⇒ id mới ⇒ LaMayChuTri false ⇒ KM tự động NGỪNG HẲN trên máy duy nhất.
        services.MayNay = () => coHub() ? LeaseMachineId : "";

        // Lượt BACKFILL máy chủ trì KHÔNG còn đứng ở đây (vòng 3–5 để nó ở đây): lượt wire chạy đúng một lần
        // lúc khởi tạo module, nên máy cấu hình hub SAU khởi động sẽ đi qua nó với `mayNay` rỗng và VĨNH VIỄN
        // không backfill. Nay nó chạy ở NHỊP ĐẦU TIÊN đọc được id máy — xem `BackfillChuTriMotLan`.

        _kmDangThoat = false;   // rót lại worker sau một lượt dừng (test, hoặc khởi động lại module)
        _kmTimer = new Timer(_ => _ = KmSyncTickAsync(services), null, TimeSpan.Zero, KmSyncTick);
    }

    /// <summary>
    /// Các bộ dịch vụ ĐÃ đi qua lượt backfill máy chủ trì — chốt trong bộ nhớ để nhịp 3 giây khỏi mở SQLite hỏi
    /// lại mãi mãi. Khoá BỀN ("chạy đúng một lần trên máy này") nằm ở bảng <c>settings</c>, do
    /// <see cref="KmConfigRepository.BackfillOwnerMachineOnce"/> giữ.
    /// <para><b>Chốt theo BỘ DỊCH VỤ, không phải một cờ <c>bool</c> toàn cục</b>: production chỉ có một
    /// <see cref="AppServices"/> nên hai cách y hệt nhau, nhưng bộ test chạy nhiều lớp SONG SONG và mỗi lớp
    /// dựng bộ dịch vụ + database riêng — một cờ chung là lớp này đóng latch của lớp kia, và phép đo "backfill
    /// có chạy không" thành ra phụ thuộc thứ tự chạy. <see cref="ConditionalWeakTable{TKey,TValue}"/> để bảng
    /// này không giữ sống bộ dịch vụ nào (nó cầm SQLite + ActivityLog).</para>
    /// </summary>
    private static readonly ConditionalWeakTable<AppServices, object> _kmDaBackfill = new();

    /// <summary>
    /// <b>BACKFILL máy chủ trì cho dòng cũ đang bật KM — chạy ở NHỊP ĐẦU TIÊN đọc được id máy này, và phải
    /// đứng TRƯỚC lượt đẩy đầu tiên.</b> Thiếu bước này thì cửa chủ trì vô hiệu trên mọi máy đang có dữ liệu
    /// (ca hỏng đầy đủ: xmldoc <see cref="KmConfigRepository.BackfillOwnerMachineOnce"/>).
    /// <para><b>Vì sao ở NHỊP chứ không ở <see cref="WireKmSync(AppServices, Func{bool})"/></b> (phản biện vòng
    /// 5, Z1): lượt wire chạy đúng một lần lúc khởi tạo module, còn hub thì cấu hình được GIỮA CHỪNG — máy nào
    /// bật hub sau khởi động sẽ vĩnh viễn không có lượt backfill.</para>
    /// <para><b>Cổng vào là hook <c>MayNay</c> chứ không phải <c>Client</c> trực tiếp:</b> con dấu backfill và
    /// con dấu lượt lưu form phải cùng MỘT cách gọi tên máy, không thì mọi phép so "chủ trì có phải tôi không"
    /// đều ra sai. Hook rỗng = chưa có hub ⇒ chưa biết mình là ai ⇒ KHÔNG đóng dấu và KHÔNG chốt latch (nhịp
    /// sau, hoặc lượt bật hub sau, vẫn còn cơ hội).</para>
    /// <para>Latch chốt TRƯỚC lượt gọi: DB hỏng thì đừng ném lại mỗi 3 giây. Khoá BỀN trong <c>settings</c> vẫn
    /// chưa đóng nên lần khởi động sau còn thử lại được.</para>
    /// </summary>
    /// <returns><c>true</c> nếu lượt backfill THẬT SỰ chạy ở lần gọi này.</returns>
    internal static bool BackfillChuTriMotLan(AppServices services)
    {
        if (_kmDaBackfill.TryGetValue(services, out _))
        {
            return false;
        }
        var may = services.MayNay?.Invoke() ?? "";
        if (may.Length == 0)
        {
            return false;   // chưa có hub ⇒ chưa biết mình là ai ⇒ để nhịp sau, latch vẫn MỞ
        }

        _kmDaBackfill.AddOrUpdate(services, new object());
        try { services.KmConfig.BackfillOwnerMachineOnce(may); }
        catch (Exception ex) { Trace.WriteLine("[OrdersModuleHost] Backfill máy chủ trì KM lỗi: " + ex.Message); }
        return true;
    }

    /// <summary>Nhịp worker hồ sơ KM đang chạy hay chưa — cho lượt teardown khoá được bằng test
    /// (<see cref="DungNhipHoSoKm"/>).</summary>
    internal static bool NhipHoSoKmDangChay => _kmTimer is not null;

    /// <summary>
    /// App ĐANG THOÁT ⇒ nhịp nào CHƯA VÀO <see cref="KmSyncTickAsync"/> thì quay đầu ngay ở cửa đầu hàm.
    /// volatile: đặt từ thread UI lúc thoát, đọc từ thread timer.
    /// <para><b>Cờ này KHÔNG che ca nào — nói rõ để lần sau không ai tin nhầm</b> (phản biện vòng 4, Y4): nó
    /// được đọc ĐÚNG MỘT LẦN, ở dòng đầu <see cref="KmSyncTickAsync"/>. Một nhịp đã đi qua cửa đó và đang
    /// <c>await</c> mạng (tới 8 giây) sẽ chạy TIẾP tới hết, tức vẫn có thể <c>Log.Append</c> sau
    /// <c>Log.Dispose()</c> và <c>KmConfig.Save</c> lúc Velopack đang thay file — đúng ca mà
    /// <see cref="DungNhipHoSoKm"/> muốn chặn nhưng KHÔNG chặn được.</para>
    /// <para>Cái nó thật sự đóng là cửa sổ giữa <c>Timer.Dispose()</c> và lúc app tắt hẳn: callback của
    /// <see cref="Timer"/> chạy trên thread pool và có thể đã được XẾP HÀNG trước lượt dispose —
    /// <c>Dispose()</c> không huỷ được lượt đã xếp, còn cờ này thì chặn nó ngay khi nó bắt đầu chạy. Muốn che
    /// nốt ca "đang bay" thì phải kiểm cờ SAU mỗi <c>await</c> trong <see cref="KmSyncTickAsync"/> — chưa làm,
    /// xem mục NỢ KỸ THUẬT của plan.</para>
    /// </summary>
    private static volatile bool _kmDangThoat;

    /// <summary>Kết cục của một nhịp worker — trả ra để lượt "đang thoát" khoá được bằng test (bên trong nhịp
    /// chỉ có tác dụng phụ, mà tác dụng phụ của một nhịp KHÔNG làm gì thì không quan sát được).</summary>
    internal enum NhipKm
    {
        /// <summary>Đang thoát app ⇒ không đụng gì (<see cref="_kmDangThoat"/>).</summary>
        DangThoat,
        /// <summary>Lượt trước chưa xong ⇒ bỏ nhịp này.</summary>
        DangBanLuotTruoc,
        /// <summary>Hub chưa cấu hình ⇒ degrade như một máy.</summary>
        ChuaCoHub,
        /// <summary>Có gọi hub (đẩy và/hoặc kéo) trong nhịp này.</summary>
        DaLam,
    }

    /// <summary>
    /// Dừng nhịp worker hồ sơ KM. Gọi từ <c>StopAsync</c> cùng chỗ với <c>_gsheetTimer</c>/<c>_mirrorTimer</c>.
    /// <para><b>Bỏ sót là hỏng thật</b> (phản biện vòng 3): <c>StopAllAsync</c> chờ tới 30 giây và
    /// <c>svc.Log.Dispose()</c> chạy sau nó — trong khoảng đó nhịp 3 giây vẫn bắn, nên dòng nhật ký của lượt
    /// gộp rơi vào một <c>ActivityLog</c> đã đóng, và <c>KmConfig.Save</c> vẫn ghi SQLite đúng lúc app đang
    /// đóng hoặc đang bị Velopack thay file.</para>
    /// <para><b>Và <c>Timer.Dispose()</c> KHÔNG chờ callback đang bay</b> (đính chính vòng 4 — câu xmldoc cũ nói
    /// mạnh hơn sự thật): callback là fire-and-forget, bên trong có <c>await</c> mạng tới 8 giây, nên một nhịp
    /// đã khởi động vẫn có thể <c>Log.Append</c> SAU <c>Log.Dispose()</c>. Vì vậy hàm này còn bật cờ
    /// <see cref="_kmDangThoat"/>.</para>
    /// <para><b>Nhưng cờ đó chỉ chặn nhịp CHƯA VÀO hàm</b> (đính chính vòng 5, Y4 — câu cũ ở đây lại nói mạnh
    /// hơn sự thật lần nữa): nó được đọc đúng một lần ở dòng đầu <see cref="KmSyncTickAsync"/>, nên nhịp đang
    /// <c>await</c> mạng KHÔNG dừng. Phạm vi thật + phần còn hở: xem xmldoc <see cref="_kmDangThoat"/>.</para>
    /// </summary>
    internal static void DungNhipHoSoKm()
    {
        _kmDangThoat = true;
        _kmTimer?.Dispose();
        _kmTimer = null;
    }

    /// <summary>Một nhịp worker: có thay đổi → ĐẨY rồi KÉO NGAY (khỏi chờ hết nhịp mới thấy bản đã gộp);
    /// không thì chỉ kéo khi đã đủ <see cref="KmPullEvery"/>.</summary>
    internal static async Task<NhipKm> KmSyncTickAsync(AppServices services)
    {
        // CỬA ĐẦU TIÊN là cờ thoát: callback timer có thể đã được XẾP HÀNG trên thread pool trước lượt
        // `Timer.Dispose()` (dispose không huỷ được lượt đã xếp), mà mọi thứ nhịp này chạm tới (ActivityLog,
        // SQLite) đều sắp bị đóng. ⚠ Đây là chỗ DUY NHẤT đọc cờ — nhịp đã qua đây rồi thì chạy tới hết, kể cả
        // khi app thoát giữa chừng. Xem xmldoc `_kmDangThoat` cho phần còn hở.
        if (_kmDangThoat)
        {
            return NhipKm.DangThoat;
        }
        if (Interlocked.Exchange(ref _kmSyncing, 1) == 1)
        {
            return NhipKm.DangBanLuotTruoc; // lượt trước chưa xong → bỏ nhịp này
        }

        try
        {
            // ⚠ BACKFILL ĐỨNG TRƯỚC LƯỢT ĐẨY — thứ tự này là lý do gốc của W3: đẩy trước rồi mới đóng dấu thì
            // lô đầu tiên lên hub mang owner_machine RỖNG, và hub gộp xong là bản rỗng đó lan sang mọi máy.
            // Nó cũng đứng trước cổng `Client` bên dưới, cố ý: cổng THẬT của nó là hook `MayNay` (rỗng ⇒ chưa
            // có hub ⇒ không làm gì), nên đặt ở đây thì luật "nhịp đầu tiên đọc được id máy" đo được bằng test
            // mà không phải dựng một HubClient thật.
            BackfillChuTriMotLan(services);

            // Cổng kiểm là Client (y worker gương): chạy được ở CẢ chế độ Full/Workspace lẫn chế độ Shopee.
            if (CoordinationRuntime.Client is not { } client)
            {
                return NhipKm.ChuaCoHub; // hub chưa cấu hình → KHÔNG đụng km_config: degrade như một máy
            }

            await LamMoiTenMayAsync(client).ConfigureAwait(false);

            var banDay = _kmDirty && NenThuDayLai(_kmPushLoiLuc, DateTimeOffset.UtcNow);
            if (banDay)
            {
                _kmDirty = false;   // xoá cờ TRƯỚC khi chụp: thay đổi xảy ra trong lúc đẩy sẽ bật lại → nhịp sau đẩy tiếp
                await DayHoSoKmAsync(services, client).ConfigureAwait(false);
            }

            if (banDay || (DateTimeOffset.UtcNow - _kmLastPull) >= KmPullEvery)
            {
                await KeoHoSoKmAsync(services, client).ConfigureAwait(false);
            }
            return NhipKm.DaLam;
        }
        catch (Exception ex)
        {
            // Không để lỗi hub làm chết thread timer (một lần ném là hết nhịp cho tới khi khởi động lại app).
            Trace.WriteLine("[OrdersModuleHost] Nhịp đồng bộ hồ sơ KM lỗi: " + ex.Message);
            return NhipKm.DaLam;
        }
        finally
        {
            Interlocked.Exchange(ref _kmSyncing, 0);
        }
    }

    /// <summary>Đẩy hồ sơ KM của máy này lên hub (fire-and-forget theo nghĩa "hỏng thì lượt sau đẩy lại" —
    /// payload là ảnh chụp toàn bộ nên bỏ một lượt không mất dữ liệu). Không có dòng nào ⇒ không gọi mạng.</summary>
    private static async Task DayHoSoKmAsync(AppServices services, HubClient client)
    {
        try
        {
            var items = ChupHoSoKm(services);
            if (items.Count == 0)
            {
                return; // máy này chưa ai đặt lịch KM → không có gì để góp
            }
            var daGop = await client.PushKmProfilesAsync(new KmProfilesPushRequest(LeaseMachineId, items))
                .ConfigureAwait(false);
            _kmPushLoiLuc = DateTimeOffset.MinValue;   // đẩy được → thôi giãn nhịp

            // KHÉP VÒNG: ghi bản hub trả về xuống km_config của CHÍNH máy này. `null`/rỗng = hub cũ ⇒ bỏ qua.
            ApBanHubTraVe(services, items, daGop, DateTime.Now);
        }
        catch (Exception ex)
        {
            // Gồm cả hub CŨ 404 (EnsureSuccessStatusCode ném) + timeout tunnel → lượt sau đẩy lại.
            Trace.WriteLine("[OrdersModuleHost] Đẩy hồ sơ KM lên hub lỗi: " + ex.Message);
            _kmDirty = true;              // đẩy hỏng thì vẫn còn BẨN, không thì thay đổi chờ tới lượt ghi km_config kế tiếp
            _kmPushLoiLuc = DateTimeOffset.UtcNow;   // …nhưng thử lại sau 60s, không phải mỗi nhịp 3s
        }
    }

    /// <summary>(THUẦN) Có được thử lượt đẩy ở nhịp này không: lượt đẩy gần nhất KHÔNG hỏng
    /// (<paramref name="loiLuc"/> = <see cref="DateTimeOffset.MinValue"/>) ⇒ đi ngay; hỏng rồi ⇒ chờ đủ
    /// <see cref="KmPullEvery"/> mới thử lại.
    /// <para>Nhận mốc hỏng qua THAM SỐ chứ không đọc thẳng trường static: hàm này quyết định "một hub cũ chưa
    /// có route có ăn một request mỗi 3 giây, mãi mãi, hay không" — đúng loại luật phải khoá được bằng test mà
    /// không phải dựng cả worker.</para></summary>
    internal static bool NenThuDayLai(DateTimeOffset loiLuc, DateTimeOffset bayGio)
        => loiLuc == DateTimeOffset.MinValue || (bayGio - loiLuc) >= KmPullEvery;

    /// <summary>
    /// Kéo hồ sơ KM trên hub về, gộp vào <c>km_config</c> cục bộ và GHI LẠI dòng nào thực sự đổi.
    /// <para><b>Hub trả <c>null</c> = "KHÔNG BIẾT"</b> (offline / hub cũ chưa có route) ⇒ GIỮ NGUYÊN bản cục bộ,
    /// hành vi y một máy chạy độc lập. Đây là luật đã có ở <c>AcquireAccountLease</c> — mất hub thì cũng không
    /// phối hợp được với ai, chặn hay xoá dữ liệu chỉ làm app vô dụng khi mất mạng.</para>
    /// </summary>
    private static async Task KeoHoSoKmAsync(AppServices services, HubClient client)
    {
        try
        {
            var hoSo = await client.GetKmProfilesAsync().ConfigureAwait(false);
            if (hoSo is null)
            {
                return; // "không biết" → giữ nguyên bản cục bộ
            }
            ApHoSoKmVeMay(services, hoSo, DateTime.Now);
        }
        catch (Exception ex)
        {
            Trace.WriteLine("[OrdersModuleHost] Kéo hồ sơ KM từ hub lỗi: " + ex.Message);
        }
        finally
        {
            // Đặt mốc kể cả khi HỎNG: hub offline thì worker chỉ thử lại mỗi 60s, không gọi mạng mỗi 3s.
            _kmLastPull = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Chụp hồ sơ KM của máy này thành payload đẩy. CHỈ lấy tài khoản ĐÃ CÓ DÒNG trong <c>km_config</c>
    /// (<see cref="KmConfigRepository.GetAll"/>) và có login — tài khoản chưa ai đặt lịch thì không có gì để
    /// góp, đẩy bản mặc định lên chỉ tạo hồ sơ rác cho cả fleet.
    /// </summary>
    internal static List<KmProfileItem> ChupHoSoKm(AppServices services)
    {
        var cauHinh = services.KmConfig.GetAll();
        var items = new List<KmProfileItem>();
        foreach (var acc in services.Accounts.GetAll())
        {
            var login = acc.Email?.Trim() ?? "";
            if (login.Length == 0 || !cauHinh.TryGetValue(acc.Id, out var cf))
            {
                continue;   // không có login thì không định danh xuyên máy được (khoá trên hub là login)
            }
            items.Add(ThanhHoSo(login, cf));
        }
        return items;
    }

    /// <summary>
    /// Áp danh sách hồ sơ hub về <c>km_config</c>: khớp theo <b>login</b> (không phân biệt hoa/thường — mỗi máy
    /// tự tạo bản ghi tài khoản nên <c>account_id</c> LỆCH giữa các máy), gộp bằng đúng luật dùng chung
    /// <see cref="KmHoSoGop.Gop"/>, và CHỈ ghi SQLite khi kết quả thực sự khác bản đang có (nhịp 60s ghi đè một
    /// dòng y hệt là ghi đĩa vô ích, và làm nhật ký DB nhiễu).
    /// <para>Login trên hub mà máy này không có tài khoản ⇒ BỎ QUA, không tạo tài khoản mới: danh bạ tài khoản
    /// là việc của <c>WireOrdersDirectory</c>, hồ sơ KM không được lấn sân.</para>
    /// <para><b>Tài khoản CHƯA CÓ dòng <c>km_config</c> thì hàm này TẠO dòng mới</b> — đó là toàn bộ mục tiêu
    /// của đợt đồng bộ (máy B chưa ai mở form phải nhận được lịch người dùng đặt ở máy A). Cố ý dùng
    /// <c>KmConfig.Get</c> (trả bản MẶC ĐỊNH khi chưa có dòng) chứ KHÔNG phải <c>GetAll()</c> +
    /// <c>TryGetValue</c> như <see cref="ChupHoSoKm"/> ngay bên trên: đổi sang khuôn kia thì mọi máy chưa mở
    /// form VĨNH VIỄN không nhận hồ sơ, và không một test nào của vòng 1 đỏ. Nay có
    /// <c>KmApHoSoVeMayTests.ChuaCoDong_ThiTaoDongMoi</c> khoá.</para>
    /// <para><b>GHI NHẬT KÝ khi lượt gộp làm ĐỔI CẤU HÌNH</b> (V3): trước đó cả ba tầng (client A, client B,
    /// hub) đều im lặng, nên cấu hình bị nuốt là thứ không thể chẩn đoán từ nhật ký. Chỉ log khi phần cấu hình
    /// THỰC SỰ khác (<see cref="KmConfig.CungCauHinh"/>) — log mỗi nhịp là spam 60 giây một dòng, và nhật ký
    /// spam thì người dùng thôi đọc, tức mất luôn tác dụng của chính dòng log.</para>
    /// </summary>
    /// <param name="bayGio">Mốc "bây giờ" (giờ địa phương) — dùng để kẹp trần mốc chạy rác, xem
    /// <see cref="KmHoSoGop.BienMocTuongLai"/>.</param>
    /// <param name="daBaoTrongNgay">Sổ "cảnh báo nào đã nói hôm nay" (xem <see cref="NenBaoMotLanMoiNgay"/>).
    /// Production để <c>null</c> ⇒ dùng sổ chung của worker; test truyền sổ RIÊNG để hai ca không giẫm lên
    /// nhau.</param>
    internal static int ApHoSoKmVeMay(
        AppServices services, IReadOnlyList<KmProfileItem> hoSo, DateTime bayGio,
        Dictionary<string, DateTime>? daBaoTrongNgay = null)
    {
        if (hoSo.Count == 0)
        {
            return 0;
        }
        var soCanhBao = daBaoTrongNgay ?? _daBaoTrongNgay;

        var theoLogin = new Dictionary<string, KmProfileItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in hoSo)
        {
            var login = p?.Login?.Trim() ?? "";
            if (p is not null && login.Length > 0) { theoLogin[login] = p; }
        }

        var doi = 0;
        foreach (var acc in services.Accounts.GetAll())
        {
            var login = acc.Email?.Trim() ?? "";
            if (login.Length == 0 || !theoLogin.TryGetValue(login, out var tuHub))
            {
                continue;
            }

            var cu = services.KmConfig.Get(acc.Id);
            var moi = GopVeMay(cu, login, tuHub, bayGio);

            // ⚠ HAI CẢNH BÁO NÀY PHẢI ĐỨNG **TRƯỚC** CỬA THOÁT `moi == cu` — đừng đảo lại (phản biện vòng 3).
            // Ca chúng sinh ra để bắt là ca ỔN ĐỊNH: máy đã có dòng khớp sẵn, mốc rác từ hub bị luật gộp bỏ nên
            // kết quả gộp bằng đúng bản đang có ⇒ `continue` ⇒ KHÔNG một dòng log nào, lặp lại mỗi 60 giây mãi
            // mãi. Đặt sau cửa thoát thì cảnh báo chỉ bắn ở ca tài khoản CHƯA CÓ DÒNG — tức không bao giờ bắn
            // trên máy đang chạy thật.
            NoiMocRacNeuCo(services, login, tuHub, bayGio, soCanhBao);
            NoiPhaHoaNeuCo(services, login, cu, tuHub, bayGio, soCanhBao);

            if (moi == cu || ChiKhacMocSuaDaKep(cu, moi, tuHub, bayGio))
            {
                continue;
            }

            services.KmConfig.Save(moi);
            doi++;

            if (!KmConfig.CungCauHinh(cu, moi))
            {
                services.Log.Append(login, CauDoiCauHinh(moi, TraTenMay(moi.OwnerMachine)));
            }
        }
        return doi;
    }

    /// <summary>
    /// (THUẦN) Bản gộp chỉ khác bản đang có <b>ở đúng cái mốc sửa vừa bị KẸP</b> ⇒ ĐỪNG ghi đĩa.
    /// <para><b>Ca thật</b> (phản biện vòng 3, X5): máy chạy CHẬM giờ hơn 6 tiếng thấy mốc ĐÚNG của hub luôn là
    /// "quá xa tương lai" ⇒ <c>KepMocSua</c> kéo nó về "bây giờ" — mà "bây giờ" mỗi nhịp một khác ⇒
    /// <c>moi != cu</c> ⇒ <c>Save</c> mỗi 60 giây, vĩnh viễn; và vì <c>CungCauHinh</c> cố ý bỏ qua
    /// <c>CfgUpdatedAt</c> nên KHÔNG một dòng log nào. Ghi đĩa im lặng mỗi phút là thứ chỉ lộ ra khi ai đó đi
    /// soi SQLite.</para>
    /// <para>Cố ý KHÔNG chặn theo kiểu "bỏ qua mọi khác biệt ở riêng mốc sửa": mốc sửa hợp lệ mà đổi thì phải
    /// ghi (nó là trọng tài của luật "sửa sau thắng", lưu sai là lượt đẩy kế mang mốc sai). Chỉ bỏ đúng lượt mà
    /// mốc ấy là giá trị do chính máy này bịa ra vì đồng hồ lệch.</para>
    /// </summary>
    internal static bool ChiKhacMocSuaDaKep(KmConfig cu, KmConfig moi, KmProfileItem tuHub, DateTime bayGio)
    {
        if (moi == cu)
        {
            return false;
        }
        var moc = KmMocThoiGian.LenDay(bayGio) ?? DateTimeOffset.UtcNow;
        return KmHoSoGop.MocQuaXaTuongLai(tuHub.CfgUpdatedAt, moc)
               && (moi with { CfgUpdatedAt = cu.CfgUpdatedAt }) == cu;
    }

    /// <summary>
    /// <b>KHÉP VÒNG PHẢN HỒI của lượt ĐẨY: ghi bản hub vừa trả về xuống <c>km_config</c> của CHÍNH máy này.</b>
    /// <para><b>CHỈ nhóm CẤU HÌNH + mốc SỬA được ghi đè; hai mốc CHẠY đi qua <c>Max</c></b> — hai nhóm có ngữ
    /// nghĩa ngược nhau, gói chung là lỗi CHẶN PHÁT HÀNH. Xem <see cref="ApBanKhepVong"/> cho ca hỏng đầy đủ.</para>
    /// <para><b>Vì sao nhóm cấu hình phải GHI ĐÈ chứ không gộp lại</b> (phản biện vòng 3, X4 vòng 4): lượt gộp ở phía client
    /// dùng đồng hồ của CHÍNH máy này, nên một máy sai đồng hồ (đóng dấu mốc sửa <c>2028</c>) không bao giờ
    /// thấy mốc của mình là "quá xa tương lai" ⇒ gộp ra đúng bản cũ ⇒ không sửa gì ⇒ và vì cờ bẩn bật lúc khởi
    /// tạo, <b>mỗi lần mở app nó lại đẩy <c>2028</c> lên, hub lại kẹp về <c>now</c>, bản của nó lại thắng và lùi
    /// mất sửa đổi của máy khác</b>. Lặp vô hạn, còn nhật ký ở máy kia chỉ nói "cấu hình vừa ĐỔI" — nói dối, vì
    /// không ai sửa gì. Hub là nguồn giờ NGOÀI máy đó nên bản nó trả về là thứ duy nhất phá được vòng lặp.</para>
    /// <para><b>Chỉ ghi đè khi dòng cục bộ CHƯA ĐỔI kể từ lượt chụp payload.</b> Bản hub trả về được gộp từ ảnh
    /// chụp CŨ; nếu người dùng vừa lưu form (hoặc <c>ChotLuotKm</c> vừa ghi mốc) trong lúc request đang bay thì
    /// ghi đè là xoá đúng thay đổi vừa xảy ra. Bỏ qua login đó — cờ bẩn đã bật lại nên nhịp sau đẩy tiếp.</para>
    /// <para><paramref name="daGop"/> <c>null</c> hoặc rỗng = hub CŨ / body không đọc được ⇒ KHÔNG đổi gì,
    /// KHÔNG ném (xem <c>HubClient.PushKmProfilesAsync</c>).</para>
    /// </summary>
    /// <param name="daDay">Đúng lô vừa đẩy lên (<see cref="ChupHoSoKm"/>) — vế so của phép kiểm "dòng cục bộ có
    /// đổi trong lúc request đang bay không".</param>
    /// <param name="daGop">Bản đã gộp hub trả về trong phản hồi <c>POST</c>.</param>
    /// <returns>Số dòng thực sự ghi lại.</returns>
    internal static int ApBanHubTraVe(
        AppServices services, IReadOnlyList<KmProfileItem> daDay, IReadOnlyList<KmProfileItem>? daGop,
        DateTime bayGio, Dictionary<string, DateTime>? daBaoTrongNgay = null)
    {
        if (daGop is null || daGop.Count == 0)
        {
            return 0;
        }
        var soCanhBao = daBaoTrongNgay ?? _daBaoTrongNgay;
        var theoLoginDay = TheoLogin(daDay);
        var theoLoginGop = TheoLogin(daGop);

        var doi = 0;
        foreach (var acc in services.Accounts.GetAll())
        {
            var login = acc.Email?.Trim() ?? "";
            if (login.Length == 0
                || !theoLoginGop.TryGetValue(login, out var tuHub)
                || !theoLoginDay.TryGetValue(login, out var daGui))
            {
                continue;
            }

            var cu = services.KmConfig.Get(acc.Id);
            if (ThanhHoSo(login, cu) != daGui)
            {
                continue;   // ai đó vừa ghi km_config trong lúc request đang bay → bản hub trả về đã cũ
            }

            NoiHubKepMocSuaNeuCo(services, login, daGui, tuHub, bayGio, soCanhBao);

            // ⚠ ApBanKhepVong, KHÔNG phải ApVaoDong: bản này gộp bằng đồng hồ của HUB, nên hai mốc CHẠY trong
            // nó có thể đã bị `LocMoc` bỏ về `null` chỉ vì hub sai giờ — gán thẳng là xoá trắng mốc hợp lệ của
            // máy này và mở đường chạy KM LẶP trong cùng khung. Xem xmldoc của ApBanKhepVong.
            var moi = ApBanKhepVong(cu, tuHub);
            if (moi == cu)
            {
                continue;
            }

            services.KmConfig.Save(moi);
            doi++;

            if (!KmConfig.CungCauHinh(cu, moi))
            {
                services.Log.Append(login, CauDoiCauHinh(moi, TraTenMay(moi.OwnerMachine)));
            }
        }
        return doi;
    }

    /// <summary>Danh sách hồ sơ → map theo login (bỏ dòng null/rỗng login), so khớp KHÔNG phân biệt hoa/thường:
    /// mỗi máy tự tạo bản ghi tài khoản nên cùng một email có thể viết hoa/thường khác nhau.</summary>
    private static Dictionary<string, KmProfileItem> TheoLogin(IReadOnlyList<KmProfileItem> ds)
    {
        var map = new Dictionary<string, KmProfileItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in ds)
        {
            var login = p?.Login?.Trim() ?? "";
            if (p is not null && login.Length > 0) { map[login] = p; }
        }
        return map;
    }

    /// <summary>(THUẦN) NHÓM CẤU HÌNH + mốc SỬA của hồ sơ → dòng <c>km_config</c> (CỬA ĐỔI MÚI GIỜ chiều NHẬN).
    /// HAI MỐC CHẠY giữ nguyên bản cũ — nơi gọi tự quyết định luật cho chúng, xem
    /// <see cref="ApVaoDong"/> / <see cref="ApBanKhepVong"/>. Giữ nguyên mọi trường khác của dòng
    /// (<c>AccountId</c>…).</summary>
    private static KmConfig ApNhomCauHinh(KmConfig cu, KmProfileItem hoSo) => cu with
    {
        TuDong = hoSo.TuDong,
        SoNgay = hoSo.SoNgay,
        PhanTram = hoSo.PhanTram,
        GioBatDau = hoSo.GioBatDau ?? "",
        GioKetThuc = hoSo.GioKetThuc ?? "",
        OwnerMachine = hoSo.OwnerMachine ?? "",
        CfgUpdatedAt = KmMocThoiGian.VeMay(hoSo.CfgUpdatedAt),
            // 7 trường CẤU HÌNH combo đi CÙNG GÓI với cấu hình KM — cùng một `CfgUpdatedAt` quyết cả hai nhóm.
        ComboBat = hoSo.ComboBat,
        ComboGioBatDau = hoSo.ComboGioBatDau ?? "",
        ComboGioKetThuc = hoSo.ComboGioKetThuc ?? "",
        ComboLoai = hoSo.ComboLoai,
        ComboMuaSl = hoSo.ComboMuaSl,
        ComboGiam = hoSo.ComboGiam,
        ComboGioiHan = hoSo.ComboGioiHan,
            // Công tắc vòng FLASH cũng thuộc NHÓM CẤU HÌNH — cùng `CfgUpdatedAt` quyết.
        FlashBat = hoSo.FlashBat,
            // ⚠ HAI THÔNG SỐ FLASH: `?? cu.X`, KHÔNG gán thẳng (03/09/2026). Hai trường này NULLABLE trên dây,
            // và `null` nghĩa là "bên gửi KHÔNG BIẾT trường này" — HUB CŨ chưa deploy bản có cột thì JSON không
            // có field. Gán thẳng `hoSo.X` (hoặc `hoSo.X ?? 5`) là lượt kéo kế tiếp ghi 5% đè lên đúng con số
            // người dùng vừa đặt, 60 giây sau, không một dòng log. GIỮ bản cục bộ mới là hành vi đúng: bên không
            // biết trường thì không có ý kiến gì về nó. Xem xmldoc `KmProfileItem.FlashPhanTramGiam`.
        // …và KẸP BIÊN (lượt 3, 04/09/2026): `km_profiles` không có CHECK, một dòng hub bị sửa tay thành 0/150 mà
        // ghi thẳng vào `km_config` là MỌI gói flash sau đó chết ở bước "0. Tham số" tới khi người dùng mở form.
        // Cặp không qua được cửa form ⇒ giữ nguyên bản cục bộ (đã qua form) — kiểm theo CẶP vì cửa form kiểm theo cặp.
        FlashPhanTramGiam = ThamSoFlashTuHubHopLe(hoSo, cu) ? hoSo.FlashPhanTramGiam ?? cu.FlashPhanTramGiam : cu.FlashPhanTramGiam,
        FlashSoLuongThamGia = ThamSoFlashTuHubHopLe(hoSo, cu) ? hoSo.FlashSoLuongThamGia ?? cu.FlashSoLuongThamGia : cu.FlashSoLuongThamGia,
};

    /// <summary>
    /// (THUẦN) Cặp %GIẢM / SL tham gia mà hub gửi về có QUA được đúng cửa kiểm của form
    /// (<c>KmDialogViewModel.KiemTraFormFlash</c>) không. Giá trị hub KHÔNG gửi (null) thì thế bằng bản cục bộ trước
    /// khi kiểm — vì đó là giá trị sẽ được ghi.
    /// </summary>
    internal static bool ThamSoFlashTuHubHopLe(KmProfileItem hoSo, KmConfig cu)
        => XuLyDonShopee.App.ViewModels.KmDialogViewModel.KiemTraFormFlash(
               hoSo.FlashPhanTramGiam ?? cu.FlashPhanTramGiam,
               hoSo.FlashSoLuongThamGia ?? cu.FlashSoLuongThamGia) is null;

    /// <summary>
    /// (THUẦN) Hồ sơ ĐÃ GỘP → dòng <c>km_config</c>: lấy TRỌN bản hồ sơ, kể cả hai mốc chạy.
    /// <para><b>Chỉ dùng cho hồ sơ mà nơi gọi ĐÃ chạy <see cref="KmHoSoGop.Gop"/> với đồng hồ CỦA MÁY NÀY</b> —
    /// tức đường KÉO (<see cref="GopVeMay"/>), nơi hai mốc chạy vừa đi qua <c>Max(cục bộ, hub)</c> nên gán thẳng
    /// là đúng. Bản hub trả về ở đường ĐẨY thì KHÔNG: nó gộp bằng đồng hồ của HUB, xem
    /// <see cref="ApBanKhepVong"/>.</para>
    /// </summary>
    internal static KmConfig ApVaoDong(KmConfig cu, KmProfileItem hoSo) => ApNhomCauHinh(cu, hoSo) with
    {
        LanChayCuoi = KmMocThoiGian.VeMay(hoSo.LanChayCuoi),
        LanThuTuDong = KmMocThoiGian.VeMay(hoSo.LanThuTuDong),
        ComboLanChayCuoi = KmMocThoiGian.VeMay(hoSo.ComboLanChayCuoi),
        ComboLanThuTuDong = KmMocThoiGian.VeMay(hoSo.ComboLanThuTuDong),
        FlashLanChayCuoi = KmMocThoiGian.VeMay(hoSo.FlashLanChayCuoi),
        FlashLanThuTuDong = KmMocThoiGian.VeMay(hoSo.FlashLanThuTuDong),
    };

    /// <summary>
    /// (THUẦN) Bản hub trả về ở lượt ĐẨY → dòng <c>km_config</c>: <b>ghi đè nhóm cấu hình + mốc SỬA, nhưng hai
    /// mốc CHẠY chỉ được TIẾN</b> (<see cref="KmHoSoGop.Max"/> với bản cục bộ).
    /// <para><b>Vì sao hai nhóm phải tách</b> (phản biện vòng 4, Y1 — lỗi CHẶN PHÁT HÀNH): mốc SỬA bị hub
    /// <c>KepMocSua</c> <b>THAY</b> giá trị, nên máy nguồn không tự biết và phải nhận lại — đó là toàn bộ lý do
    /// khép vòng tồn tại. Mốc CHẠY thì ngược hẳn: <c>LocMoc</c> <b>BỎ</b> chúng về <c>null</c>, và bỏ ở CẢ HAI
    /// phía nên chúng tự rụng, không cần ai khép vòng hộ.</para>
    /// <para><b>Ca hỏng nếu gán thẳng</b> — không cần máy client nào sai giờ, chỉ cần <b>HUB</b> chậm &gt; 6h
    /// (VM khôi phục snapshot, NTP chết): máy A chạy xong lượt KM lúc 02:10 ⇒ <c>ChotLuotKm</c> ghi
    /// <c>LanChayCuoi</c> ⇒ ≤3s sau worker đẩy ⇒ hub thấy mốc ấy "quá xa tương lai" theo đồng hồ CỦA NÓ ⇒ trả
    /// về <c>null</c> ⇒ ghi đè ⇒ <b>hai mốc chạy hợp lệ của A bị xoá trắng</b> ⇒ nhịp 60s kế
    /// <c>KmLichTuDong.DenGioChay</c> trả <c>true</c> ⇒ <b>chạy lại trọn vòng shop và tạo chương trình khuyến
    /// mãi THẬT lần thứ hai</b>, lặp tới 06:00, mỗi đêm, trên mọi máy. Hoàn toàn im lặng:
    /// <c>KmConfig.CungCauHinh</c> bỏ qua mốc nên không có dòng <c>CauDoiCauHinh</c>, và
    /// <c>NoiMocRacNeuCo</c> không nằm trên đường đẩy.</para>
    /// <para><b>Phải là <c>Max</c>, không phải "giữ nguyên bản cũ":</b> mốc chạy là bánh cóc dùng chung — một
    /// máy KHÁC vừa chạy lượt KM thì mốc mới hơn của nó về qua đây, và máy này phải TIẾN theo, không thì đêm
    /// nay nó chạy lại đúng lượt máy kia vừa chạy.</para>
    /// </summary>
    internal static KmConfig ApBanKhepVong(KmConfig cu, KmProfileItem hoSo) => ApNhomCauHinh(cu, hoSo) with
    {
        LanChayCuoi = KmMocThoiGian.VeMay(
            KmHoSoGop.Max(KmMocThoiGian.LenDay(cu.LanChayCuoi), hoSo.LanChayCuoi)),
        LanThuTuDong = KmMocThoiGian.VeMay(
            KmHoSoGop.Max(KmMocThoiGian.LenDay(cu.LanThuTuDong), hoSo.LanThuTuDong)),
        // Hai mốc COMBO đi ĐÚNG luật MAX như hai mốc trên — cùng lý do: lượt đẩy có thể bị hub kẹp trần theo
        // đồng hồ CỦA HUB, mà mốc combo vừa ghi xong là thứ chặn vòng combo chạy lại lần thứ hai trong khung.
        ComboLanChayCuoi = KmMocThoiGian.VeMay(
            KmHoSoGop.Max(KmMocThoiGian.LenDay(cu.ComboLanChayCuoi), hoSo.ComboLanChayCuoi)),
        ComboLanThuTuDong = KmMocThoiGian.VeMay(
            KmHoSoGop.Max(KmMocThoiGian.LenDay(cu.ComboLanThuTuDong), hoSo.ComboLanThuTuDong)),
        // Hai mốc FLASH đi ĐÚNG luật MAX như bốn mốc trên. Riêng `FlashLanThuTuDong` còn đắt hơn: từ 01/09/2026
        // khung flash là CẢ NGÀY, nên nó là thứ duy nhất giữ GIÃN CÁCH 30 phút giữa hai gói — mất nó là nhịp 60
        // giây mở gói mới mỗi phút.
        FlashLanChayCuoi = KmMocThoiGian.VeMay(
            KmHoSoGop.Max(KmMocThoiGian.LenDay(cu.FlashLanChayCuoi), hoSo.FlashLanChayCuoi)),
        FlashLanThuTuDong = KmMocThoiGian.VeMay(
            KmHoSoGop.Max(KmMocThoiGian.LenDay(cu.FlashLanThuTuDong), hoSo.FlashLanThuTuDong)),
    };

    /// <summary>(THUẦN) Hub có KẸP mốc sửa của lượt đẩy này không: cấu hình trả về vẫn là cấu hình mình gửi
    /// (tức bản mình THẮNG), nhưng mốc sửa trả về LÙI so với mốc mình gửi — chỉ có một cách ra được như thế, là
    /// <c>KmHoSoGop.KepMocSua</c> đã kéo mốc về giờ của hub.</summary>
    internal static bool HubDaKepMocSua(KmProfileItem daGui, KmProfileItem tuHub)
        => daGui.CfgUpdatedAt is { } gui && tuHub.CfgUpdatedAt is { } ve && ve < gui
           && KmHoSoGop.CungCauHinh(daGui, tuHub);

    /// <summary>Ghi nhật ký nếu hub vừa kẹp mốc sửa của máy này — tối đa MỘT dòng mỗi tài khoản mỗi ngày.</summary>
    private static void NoiHubKepMocSuaNeuCo(
        AppServices services, string login, KmProfileItem daGui, KmProfileItem tuHub, DateTime bayGio,
        Dictionary<string, DateTime> daBao)
    {
        if (HubDaKepMocSua(daGui, tuHub)
            && NenBaoMotLanMoiNgay(daBao, login + "|hub-kep-moc-sua", bayGio))
        {
            services.Log.Append(login, CauHubKepMocSua(daGui.CfgUpdatedAt, tuHub.CfgUpdatedAt));
        }
    }

    /// <summary>
    /// (THUẦN) Dòng nhật ký khi HUB kẹp mốc sửa của máy này — dấu hiệu LỆCH ĐỒNG HỒ giữa máy này và hub.
    /// <para>Đây là cảnh máy sai đồng hồ KHÔNG thể tự chẩn đoán: với nó, mốc 2028 không phải tương lai. Chỉ có
    /// hub nhìn ra, và chỉ dòng này nói lại được.</para>
    /// <para><b>Phải nêu ĐỦ BA khả năng</b> (phản biện vòng 5, Z2 — bản cũ khẳng định một chiều "gần như chắc
    /// chắn ĐỒNG HỒ MÁY NÀY chạy nhanh"). Phép so chạy trên hiệu giữa hai đồng hồ, nên đúng ca mà Y1 sinh ra để
    /// chống — <b>HUB</b> chậm giờ, mọi client đúng giờ — cho ra cùng một triệu chứng, và câu cũ bắn ở MỌI máy
    /// trong fleet bảo người dùng đi sửa đồng hồ máy client trong khi thủ phạm là hub. Hai câu ngay bên cạnh
    /// (<see cref="CauMocRac"/>, <see cref="CauMocSuaRac"/>) đã nêu cả hai chiều từ W11; câu này thì chưa, và
    /// lưới rà chuỗi Y3 không bắt được vì nó chỉ kiểm "tên nút có thật", không kiểm "chẩn đoán có đúng".</para>
    /// </summary>
    internal static string CauHubKepMocSua(DateTimeOffset? daGui, DateTimeOffset? tuHub)
        => "Hồ sơ KM đồng bộ lên hub: mốc sửa cấu hình máy này gửi ("
           + (KmMocThoiGian.VeMay(daGui) is DateTime g ? g.ToString("dd-MM-yyyy HH:mm") : "(rỗng)")
           + ") nằm quá xa ở TƯƠNG LAI so với giờ của hub — đã bị KẸP về "
           + (KmMocThoiGian.VeMay(tuHub) is DateTime v ? v.ToString("dd-MM-yyyy HH:mm") : "(rỗng)")
           + " và ghi lại xuống máy này (cấu hình giữ nguyên). Nguyên nhân là LỆCH ĐỒNG HỒ giữa máy này và máy "
           + "chủ hub, và có BA khả năng: hoặc ĐỒNG HỒ MÁY NÀY chạy nhanh, hoặc ĐỒNG HỒ HUB chạy chậm (máy chủ "
           + "vừa khôi phục snapshot / NTP chết — khi đó dòng này bắn ở MỌI máy trong nhóm, đó là dấu hiệu nhận "
           + "ra), hoặc một trong hai bên đặt SAI MÚI GIỜ. Kiểm ngày giờ của cả máy này lẫn máy chủ hub — để "
           + "nguyên thì mọi thay đổi cấu hình ở máy khác đều bị bản của máy sai giờ đè.";

    /// <summary>Sổ chung "cảnh báo nào đã nói hôm nay" của worker — xem <see cref="NenBaoMotLanMoiNgay"/>.</summary>
    private static readonly Dictionary<string, DateTime> _daBaoTrongNgay = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// (THUẦN trên <paramref name="daBao"/>) Cảnh báo mang <paramref name="khoa"/> đã nói trong NGÀY của
    /// <paramref name="bayGio"/> chưa; chưa thì ghi sổ và trả <c>true</c>.
    /// <para>Cần vì hai cảnh báo dưới đây nằm ở nhánh KHÔNG đổi gì — không chặn thì chúng lặp mỗi 60 giây,
    /// vĩnh viễn. Cùng luật với <c>AccountsViewModel.BaoKhongPhaiChuTri</c>: nhật ký spam thì người dùng thôi
    /// đọc, tức mất luôn tác dụng của chính dòng log.</para>
    /// </summary>
    internal static bool NenBaoMotLanMoiNgay(Dictionary<string, DateTime> daBao, string khoa, DateTime bayGio)
    {
        if (daBao.TryGetValue(khoa, out var ngay) && ngay == bayGio.Date)
        {
            return false;
        }
        daBao[khoa] = bayGio.Date;
        return true;
    }

    /// <summary>
    /// (THUẦN) Dòng nhật ký khi lượt gộp làm ĐỔI cấu hình KM của một tài khoản — phải đủ để đối chiếu giữa hai
    /// máy: ai chủ trì · sửa lúc nào · tự động on/off · chu kỳ · % · khung giờ · <b>và vòng COMBO</b>.
    /// <para><b>Vì sao vế combo bắt buộc có</b> (phản biện 25/08/2026, VỪA-4): cửa sổ rollout CLIENT CŨ. Máy B
    /// chưa cập nhật Velopack (JSON không có trường <c>combo*</c>) chỉ sửa <c>%</c> lúc 10:05 ⇒ mốc sửa của nó
    /// mới hơn ⇒ luật "máy nào sửa sau thắng" lấy TRỌN gói của B ⇒ <c>ComboBat</c> về <c>false</c>,
    /// <c>ComboGiam</c> về mặc định ⇒ máy A kéo bản đó về 60 giây sau và <b>vòng combo tắt câm</b>. Bản cũ của
    /// dòng này không có một chữ nào về combo, nên lượt reset đó hoàn toàn không để lại dấu.</para>
    /// <para><b>Cố ý KHÔNG tách nhóm gộp riêng cho <c>combo*</c></b> — đó là đổi hợp đồng đồng bộ, ngoài phạm vi
    /// chặng A (xem khối chú thích COMBO trên <c>KmProfileItem</c>: hai luật gộp, không đẻ luật thứ ba). Ở đây
    /// chỉ làm cho lượt reset THÔI IM LẶNG.</para>
    /// </summary>
    /// <param name="tenChuTri">Tên máy chủ trì tra được (<see cref="TraTenMay"/>); null/rỗng ⇒ chỉ hiện id ngắn.</param>
    internal static string CauDoiCauHinh(KmConfig moi, string? tenChuTri = null)
        => "Hồ sơ KM đồng bộ từ hub: cấu hình vừa ĐỔI — "
           + (moi.TuDong ? $"tự động BẬT (mỗi {moi.SoNgay} ngày)" : "tự động TẮT")
           + $" · giảm {moi.PhanTram}% · {moi.GioBatDau}-{moi.GioKetThuc}"
           // Tên máy + id NGẮN, không phải GUID thô: xem KmLichTuDong.NhanMay.
           + $" · máy chủ trì: {(KmLichTuDong.NhanMay(moi.OwnerMachine, tenChuTri) is { Length: > 0 } m ? m : "(chưa có)")}"
           // Vế COMBO: nêu cả cờ BẬT/TẮT lẫn mức ưu đãi, vì lượt reset của client cũ kéo CẢ HAI về mặc định.
           + (moi.ComboBat
               ? $" · combo BẬT ({moi.ComboGioBatDau}-{moi.ComboGioKetThuc}, mua {moi.ComboMuaSl} giảm "
                 + $"{moi.ComboGiam}, loại {moi.ComboLoai}, giới hạn {moi.ComboGioiHan})"
               : $" · combo TẮT (mua {moi.ComboMuaSl} giảm {moi.ComboGiam})")
           // Vế FLASH: nêu cả cờ BẬT/TẮT lẫn HAI THÔNG SỐ — cùng lý do với vế combo ngay trên. Cửa sổ rollout
           // client cũ có thể TẮT câm vòng flash (vòng đó tạo chương trình THẬT trên mọi shop mỗi ngày), và hai
           // con số kia nay là thứ người dùng GÕ rồi đi thẳng xuống Shopee — cả hai đổi thì phải để lại dấu.
           + (moi.FlashBat ? " · flash sale BẬT (cả ngày, gói trong khe nghỉ check đơn)" : " · flash sale TẮT")
           + $" (giảm {moi.FlashPhanTramGiam}%, SL tham gia {moi.FlashSoLuongThamGia})"
           + $" · sửa lúc: {(moi.CfgUpdatedAt is DateTime c ? c.ToString("dd-MM-yyyy HH:mm") : "(không rõ)")}";

    /// <summary>Ghi nhật ký nếu hồ sơ kéo về mang mốc QUÁ XA TƯƠNG LAI — tối đa MỘT dòng mỗi loại mốc mỗi tài
    /// khoản mỗi ngày. Ba loại: hai mốc CHẠY (bị BỎ) và mốc SỬA cấu hình (bị KẸP về bây giờ).</summary>
    private static void NoiMocRacNeuCo(
        AppServices services, string login, KmProfileItem tuHub, DateTime bayGio,
        Dictionary<string, DateTime> daBao)
    {
        var moc = KmMocThoiGian.LenDay(bayGio) ?? DateTimeOffset.UtcNow;
        if (KmHoSoGop.MocQuaXaTuongLai(tuHub.LanChayCuoi, moc)
            && NenBaoMotLanMoiNgay(daBao, login + "|moc-chay-cuoi", bayGio))
        {
            services.Log.Append(login, CauMocRac("lần chạy cuối", tuHub.LanChayCuoi));
        }
        if (KmHoSoGop.MocQuaXaTuongLai(tuHub.LanThuTuDong, moc)
            && NenBaoMotLanMoiNgay(daBao, login + "|moc-thu-tu-dong", bayGio))
        {
            services.Log.Append(login, CauMocRac("lần thử tự động", tuHub.LanThuTuDong));
        }
        // MỐC SỬA — vế "log" mà vòng 3 làm thiếu (W7 viết "kẹp về bayGio + log", chỉ có vế kẹp). Không có dòng
        // này thì đúng ca nguy hiểm nhất của nhóm — một máy sai đồng hồ đè cấu hình cả fleet — diễn ra hoàn toàn
        // im lặng ở phía mốc.
        if (KmHoSoGop.MocQuaXaTuongLai(tuHub.CfgUpdatedAt, moc)
            && NenBaoMotLanMoiNgay(daBao, login + "|moc-sua", bayGio))
        {
            services.Log.Append(login, CauMocSuaRac(tuHub.CfgUpdatedAt));
        }
    }

    /// <summary>
    /// (THUẦN) Dòng nhật ký khi MỐC SỬA cấu hình từ hub nằm quá xa tương lai và bị KẸP.
    /// <para>Cố ý nói khác câu của mốc CHẠY: mốc chạy bị <b>BỎ</b> (mất một sự kiện), mốc sửa bị <b>KẸP</b>
    /// (cấu hình giữ nguyên, chỉ con dấu thời gian đổi) — hai hậu quả khác nhau thì không được dùng chung một
    /// câu, người đọc sẽ chữa nhầm bệnh.</para>
    /// </summary>
    internal static string CauMocSuaRac(DateTimeOffset? moc)
        => "Hồ sơ KM đồng bộ từ hub: mốc SỬA CẤU HÌNH = "
           + (KmMocThoiGian.VeMay(moc) is DateTime d ? d.ToString("dd-MM-yyyy HH:mm") : "(rỗng)")
           + $" nằm quá xa ở TƯƠNG LAI (biên {KmHoSoGop.BienMocTuongLai.TotalHours:0}h) — đã KẸP về bây giờ, "
           + "cấu hình vẫn giữ nguyên. Nguyên nhân là SAI ĐỒNG HỒ HỆ THỐNG: hoặc một máy khác trong nhóm chạy "
           + "nhanh giờ, hoặc CHÍNH MÁY NÀY chạy chậm giờ. Kiểm lại ngày giờ của tất cả các máy — để nguyên thì "
           + "bản của máy sai giờ luôn thắng luật \"máy nào sửa sau thắng\".";

    /// <summary>
    /// (THUẦN) Dòng nhật ký khi một mốc chạy ở quá xa tương lai bị bỏ.
    /// <para><b>Nói CẢ HAI khả năng</b> (W11): phép so "quá xa tương lai" dùng đồng hồ của CHÍNH máy đang đọc,
    /// nên một máy bị chậm hơn thực tế &gt; 6 giờ sẽ thấy mốc ĐÚNG của hub là mốc tương lai và bỏ nó — hậu quả
    /// ngược hẳn: <c>LanChayCuoi</c> về rỗng ⇒ máy đó coi như "chưa chạy lần nào" ⇒ chạy KM lại. Câu chữ chỉ
    /// đổ cho "một máy khác sai giờ" sẽ dắt người đọc đi sai hướng đúng lúc họ cần đúng hướng nhất.</para>
    /// </summary>
    internal static string CauMocRac(string tenMoc, DateTimeOffset? moc)
        => $"Hồ sơ KM đồng bộ từ hub: mốc \"{tenMoc}\" = "
           + (KmMocThoiGian.VeMay(moc) is DateTime d ? d.ToString("dd-MM-yyyy HH:mm") : "(rỗng)")
           + $" nằm quá xa ở TƯƠNG LAI (biên {KmHoSoGop.BienMocTuongLai.TotalHours:0}h) — ĐÃ BỎ, tức lượt KM "
           + "kế có thể chạy sớm hơn chu kỳ. Nguyên nhân là SAI ĐỒNG HỒ HỆ THỐNG: hoặc một máy khác trong nhóm "
           + "chạy nhanh giờ, hoặc CHÍNH MÁY NÀY chạy chậm giờ. Kiểm lại ngày giờ của tất cả các máy.";

    /// <summary>Ghi nhật ký nếu lượt gộp này phải PHÁ HOÀ (cùng mốc sửa, khác cấu hình) — tối đa MỘT dòng mỗi
    /// tài khoản mỗi ngày.</summary>
    private static void NoiPhaHoaNeuCo(
        AppServices services, string login, KmConfig cu, KmProfileItem tuHub, DateTime bayGio,
        Dictionary<string, DateTime> daBao)
    {
        var moc = KmMocThoiGian.LenDay(bayGio) ?? DateTimeOffset.UtcNow;
        if (KmHoSoGop.CanPhaHoa(ThanhHoSo(login, cu), tuHub, moc)
            && NenBaoMotLanMoiNgay(daBao, login + "|pha-hoa", bayGio))
        {
            services.Log.Append(login, CauPhaHoa());
        }
    }

    /// <summary>
    /// (THUẦN) Dòng nhật ký của lượt phá hoà. Lượt này quyết bằng một luật người dùng KHÔNG hề gõ ra (ưu tiên
    /// lịch đang bật, rồi id máy nhỏ hơn) nên im lặng ở đây là để lại một thay đổi cấu hình không ai giải thích
    /// nổi — xem <c>KmHoSoGop.ThangKhiHoaMoc</c>.
    /// <para><b>Câu hướng dẫn phải trỏ vào thao tác CÓ THẬT</b> (phản biện vòng 3, X2 — lần thứ hai cùng một
    /// hình dạng lỗi trong đợt này). Bản vòng 3 bảo "mở form … và LƯU lại một lần": form KM có đúng ba nút
    /// <i>Chạy chương trình</i> · <i>Đóng</i> · <i>Máy này nhận chủ trì</i>, <b>không có nút Lưu</b>, và đóng
    /// form mà không sửa gì thì <c>KmDialogViewModel.BanLuuSauForm</c> trả <c>null</c> — làm đúng lời khuyên
    /// thì KHÔNG có gì xảy ra, và đêm sau lại đúng dòng này.</para>
    /// </summary>
    internal static string CauPhaHoa()
        => "Hồ sơ KM đồng bộ từ hub: máy này và hub có CÙNG mốc sửa cấu hình mà cấu hình lại khác nhau "
           + "(thường gặp ngay sau lượt nâng cấp, khi mọi máy được đóng dấu cùng một mốc). Đã chọn bản thắng "
           + "theo thứ tự cố định: lịch đang BẬT thắng lịch đang tắt, rồi tới máy chủ trì có id nhỏ hơn. Muốn "
           + "chốt bản của máy nào thì sang máy đó mở form 'KM của Shop', SỬA LẠI thông số cho đúng ý rồi bấm "
           + "\"Đóng\" — form chỉ ghi khi có thay đổi thật, mở ra xem rồi đóng thì không đóng dấu gì. Chỉ muốn "
           + "giành quyền chủ trì mà không đổi thông số thì bấm nút \"Máy này nhận chủ trì\" rồi \"Đóng\".";

    /// <summary>
    /// (THUẦN) Gộp hồ sơ hub vào một bản <c>km_config</c> cục bộ — CỬA ĐỔI MÚI GIỜ chiều KÉO: mốc trên dây là
    /// UTC, mốc trả về là giờ địa phương (mọi luật của <c>KmLichTuDong</c> chạy trên giờ địa phương).
    /// <para><paramref name="tuHub"/> <c>null</c> = không hỏi được hub ⇒ trả NGUYÊN bản cục bộ (degrade như một
    /// máy). Hàm này là chỗ khoá nhánh đó bằng test.</para>
    /// </summary>
    /// <param name="bayGio">Mốc "bây giờ" (giờ địa phương) — đổi sang UTC ở ĐÚNG cửa này rồi đưa vào luật gộp
    /// để kẹp trần mốc chạy rác (<see cref="KmHoSoGop.BienMocTuongLai"/>).</param>
    internal static KmConfig GopVeMay(KmConfig cu, string login, KmProfileItem? tuHub, DateTime bayGio)
    {
        if (tuHub is null)
        {
            return cu;
        }

        var gop = KmHoSoGop.Gop(ThanhHoSo(login, cu), tuHub, KmMocThoiGian.LenDay(bayGio) ?? DateTimeOffset.UtcNow);
        return ApVaoDong(cu, gop);
    }

    // ══════════════════════════ TÊN MÁY (X6) ══════════════════════════

    /// <summary>Nhịp làm mới bảng tên máy. 5 phút: tên máy gần như không đổi, mà đây chỉ là dữ liệu HIỂN THỊ —
    /// hỏng thì cùng lắm hiện id ngắn.</summary>
    private static readonly TimeSpan TenMayLamMoiMoi = TimeSpan.FromMinutes(5);

    private static readonly object _tenMayLock = new();

    /// <summary>id suất → tên máy, chụp từ <c>/fleet</c>. Rỗng cho tới lượt làm mới đầu tiên.</summary>
    private static Dictionary<string, string> _tenMay = new(StringComparer.OrdinalIgnoreCase);

    private static DateTimeOffset _tenMayLuc = DateTimeOffset.MinValue;

    /// <summary>
    /// Tra TÊN MÁY từ id suất đơn hàng (hook <c>AppServices.TenMay</c>). null = chưa tra được ⇒ nơi gọi hiện id
    /// ngắn. Máy NÀY luôn tra được mà không cần hub — <c>MachineIdentity</c> là dữ liệu cục bộ.
    /// </summary>
    internal static string? TraTenMay(string? machineId)
    {
        var id = machineId?.Trim() ?? "";
        if (id.Length == 0) { return null; }
        if (string.Equals(id, LeaseMachineId, StringComparison.OrdinalIgnoreCase))
        {
            return MachineIdentity.Shared.DisplayName;
        }
        lock (_tenMayLock)
        {
            return _tenMay.TryGetValue(id, out var ten) && ten.Length > 0 ? ten : null;
        }
    }

    /// <summary>Làm mới bảng tên máy từ <c>/fleet</c>, tối đa <see cref="TenMayLamMoiMoi"/> một lượt. Hỏng ⇒ giữ
    /// bảng cũ và im lặng: đây là dữ liệu hiển thị, không được làm hỏng nhịp đồng bộ vì nó.</summary>
    private static async Task LamMoiTenMayAsync(HubClient client)
    {
        if ((DateTimeOffset.UtcNow - _tenMayLuc) < TenMayLamMoiMoi)
        {
            return;
        }
        _tenMayLuc = DateTimeOffset.UtcNow;   // đặt TRƯỚC lượt gọi: hỏng cũng không thử lại mỗi nhịp 3 giây
        try
        {
            var fleet = await client.FleetAsync().ConfigureAwait(false);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in fleet.Machines)
            {
                var id = m?.MachineId?.Trim() ?? "";
                var ten = m?.Hostname?.Trim() ?? "";
                if (id.Length > 0 && ten.Length > 0) { map[id] = ten; }
            }
            lock (_tenMayLock) { _tenMay = map; }
        }
        catch (Exception ex)
        {
            Trace.WriteLine("[OrdersModuleHost] Làm mới tên máy cho hồ sơ KM lỗi: " + ex.Message);
        }
    }

    /// <summary>(THUẦN) <c>km_config</c> cục bộ → hồ sơ đi dây — CỬA ĐỔI MÚI GIỜ chiều ĐẨY: mốc trong DB là giờ
    /// địa phương (<c>Kind = Unspecified</c>), mốc trên dây là UTC.</summary>
    internal static KmProfileItem ThanhHoSo(string login, KmConfig cf) => new(login)
    {
        TuDong = cf.TuDong,
        SoNgay = cf.SoNgay,
        PhanTram = cf.PhanTram,
        GioBatDau = cf.GioBatDau,
        GioKetThuc = cf.GioKetThuc,
        OwnerMachine = cf.OwnerMachine,
        CfgUpdatedAt = KmMocThoiGian.LenDay(cf.CfgUpdatedAt),
        LanChayCuoi = KmMocThoiGian.LenDay(cf.LanChayCuoi),
        LanThuTuDong = KmMocThoiGian.LenDay(cf.LanThuTuDong),
        ComboBat = cf.ComboBat,
        ComboGioBatDau = cf.ComboGioBatDau,
        ComboGioKetThuc = cf.ComboGioKetThuc,
        ComboLoai = cf.ComboLoai,
        ComboMuaSl = cf.ComboMuaSl,
        ComboGiam = cf.ComboGiam,
        ComboGioiHan = cf.ComboGioiHan,
        ComboLanChayCuoi = KmMocThoiGian.LenDay(cf.ComboLanChayCuoi),
        ComboLanThuTuDong = KmMocThoiGian.LenDay(cf.ComboLanThuTuDong),
        FlashBat = cf.FlashBat,
        FlashLanChayCuoi = KmMocThoiGian.LenDay(cf.FlashLanChayCuoi),
        FlashLanThuTuDong = KmMocThoiGian.LenDay(cf.FlashLanThuTuDong),
        // Chiều ĐẨY thì luôn có số thật: `km_config` khai NOT NULL DEFAULT nên hai cột này không bao giờ null ở
        // client. `null` trên dây chỉ sinh ra từ một bản CŨ hơn ở phía đối diện, không từ đây.
        FlashPhanTramGiam = cf.FlashPhanTramGiam,
        FlashSoLuongThamGia = cf.FlashSoLuongThamGia,
    };
}
