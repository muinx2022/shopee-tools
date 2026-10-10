using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>NHÁNH HOÃN chạy lại check đơn tới CUỐI KHE NGHỈ</b> — nhánh sinh ra sau mỗi GÓI Flash Sale.
///
/// <para><b>Ý NGHĨA CỦA MỐC ĐÃ ĐỔI 01/09/2026, mệnh đề thì KHÔNG.</b> Cũ: mốc <c>22:00</c> cố định, chỉ đặt ở
/// nhánh "ngày mai không đỏ" (hằng <c>GioGoiLaiCheckDonSauFlash</c>, nay đã xoá). Mới: mốc là <b>cuối khe nghỉ
/// mà gói vừa mượn</b>, đặt cho MỌI gói — gói xong sớm phải trả lại đúng phần khe còn thừa, có thế nhịp vòng
/// check đơn mới giữ nguyên như khi không có flash. Ba mệnh đề dưới đây đo cùng một thứ như trước: mốc bị xoá
/// sau khi dùng · chờ xong thì thật sự chạy lại · người dùng bấm Dừng thì tuyệt đối không mở lại.</para>
///
/// <para>Vì <c>ChotLuotFlash</c> KHÔNG còn đặt mốc này (nay là việc của <c>NhipLichFlashAsync</c>, vốn đi qua
/// khoá xuyên máy + phóng Brave nên không test nào chạm tới), các bài dưới đây rót mốc qua chỗ nối
/// <c>DatMocChayLaiSauFlashChoTest</c>.</para>
///
/// <para><b>Vì sao file này tồn tại</b> (phản biện 29/08/2026): chỗ nối <c>ChoTruocKhiChayLaiCheckDon</c> được
/// dựng ra để test lái, nhưng <b>không một test nào dùng nó</b> — tức toàn bộ nhánh hoãn không có lưới nào.
/// Ba mệnh đề dưới đây là ba thứ có thể hỏng, và cái thứ ba là nặng nhất.</para>
///
/// <list type="number">
/// <item><b>Mốc bị XOÁ sau khi dùng</b> — để sót thì mọi lượt mượn hồ sơ SAU đó cũng bị hoãn theo;</item>
/// <item><b>Chờ xong thì THẬT SỰ chạy lại</b> — không thì check đơn chết im tới khi người dùng tự bấm;</item>
/// <item><b>⚠ NGƯỜI DÙNG BẤM DỪNG GIỮA CHỪNG ⇒ TUYỆT ĐỐI KHÔNG mở lại.</b> Lượt hoãn ngủ tới 55 phút; không
/// huỷ được thì app tự mở Brave sau khi đã được bảo dừng — phần mềm phủ quyết thao tác của người dùng, im
/// lặng. Đây là hành vi MỚI do chính bản vá 28/08 sinh ra: trước đó <c>TraHoSo</c> chạy lại NGAY, trong cùng
/// tick với cú Dừng, nên cờ <c>_nguoiDungDungTay</c> đủ để chặn.</item>
/// </list>
///
/// <para><b>⚠ KHÔNG bài test nào ở đây được chạm <c>Sessions.Start</c> thật</b> — nó phóng trình duyệt. Mệnh đề
/// cần khoá là <i>"có gọi hay KHÔNG gọi"</i>, nên đo qua chỗ nối <c>ChayLaiPhienCheckDon</c>.</para>
/// </summary>
public class FlashHoanChayLaiTests
{
    /// <summary>Một bàn thử: VM + một tài khoản + hai chỗ nối đã rót (chờ, và chạy lại).</summary>
    private sealed class Ban : IDisposable
    {
        public TempDatabase Temp { get; init; } = null!;
        public AppServices Services { get; init; } = null!;
        public AccountsViewModel Vm { get; init; } = null!;
        public long Id { get; init; }

        /// <summary>Các khoảng chờ đã rót vào chỗ nối.</summary>
        public List<TimeSpan> DaCho { get; } = new();

        /// <summary>Token của từng lượt chờ — để đo cú Dừng có CẮT được lượt chờ hay không.</summary>
        public List<CancellationToken> Token { get; } = new();

        /// <summary>Các tài khoản ĐÃ được chạy lại check đơn (thay cho <c>Sessions.Start</c> thật).</summary>
        public List<long> DaChayLai { get; } = new();

        /// <summary>Cổng để bài test tự quyết khi nào lượt chờ kết thúc "bình thường".</summary>
        public TaskCompletionSource<bool> ChoXong { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() => Temp.Dispose();
    }

    private static Ban Dung(string email)
    {
        var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = email, Password = "p" });
        var id = services.Accounts.GetAll().First(a => a.Email == email).Id;
        var ban = new Ban { Temp = temp, Services = services, Vm = new AccountsViewModel(services), Id = id };

        ban.Vm.ChayLaiPhienCheckDon = x => ban.DaChayLai.Add(x);
        // ĐÔNG LẠNH đồng hồ của nhánh hoãn (30/08/2026): mọi test dùng mốc lịch sử 29/08 21:0x, mà bản đọc
        // `DateTime.Now` thật làm cả lớp thành BOM HẸN GIỜ — xanh tới 22:00 đêm 29/08 rồi tự đỏ vĩnh viễn
        // (mốc hoãn 22:00 của 29/08 đã trôi qua ⇒ nhánh hoãn không bao giờ vào). 21:10 nằm TRONG khoảng
        // [21:0x, 22:00) của đúng ngày các mốc test dùng.
        ban.Vm.DongHoHoanChayLai = () => new DateTime(2026, 8, 29, 21, 10, 0);
        ban.Vm.ChoTruocKhiChayLaiCheckDon = (t, ct) =>
        {
            ban.DaCho.Add(t);
            ban.Token.Add(ct);
            // Nghe token: production là `Task.Delay(t, ct)`, nên bản test cũng phải HUỶ ĐƯỢC — không thì lưới
            // "người dùng bấm Dừng" chỉ đo được cái cờ chứ không đo được lượt chờ có thoát hay không.
            return Task.WhenAny(ban.ChoXong.Task, Task.Delay(Timeout.Infinite, ct));
        };
        return ban;
    }

    /// <summary>Đặt mốc hoãn (= CUỐI KHE NGHỈ mà gói vừa mượn) rồi TRẢ hồ sơ — đúng chuỗi của một gói Flash
    /// Sale xong sớm.</summary>
    private static void VaoNhanhHoan(Ban b, DateTime cuoiKhe)
    {
        b.Vm.DatMocChayLaiSauFlashChoTest(cuoiKhe);
        b.Vm.TraHoSoChoTest(b.Id);
    }

    /// <summary>Chờ lượt hoãn kết thúc (bình thường hoặc bị huỷ) — không bài test nào được ngủ theo đồng hồ.</summary>
    private static async Task ChoLuotHoanXongAsync(Ban b)
    {
        if (b.Vm.LuotHoanGanNhat is Task t) { await t; }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 1. MỐC bị XOÁ sau khi dùng
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>TraHoSo</c> ĐỌC RỒI XOÁ mốc hoãn ngay — mốc là của ĐÚNG lượt vừa xong.
    /// <para>Để sót thì lượt mượn hồ sơ SAU đó (nút "Mở list shop", một lượt KM) cũng bị hoãn tới 22:00, không
    /// vì cái gì — và lần đó thì chẳng có lượt Flash nào để giải thích.</para>
    /// </summary>
    [StaFact]
    public async Task MocHoan_BiXoaNgaySauKhiDung()
    {
        using var b = Dung("hoan-moc@mail.com");

        VaoNhanhHoan(b, new DateTime(2026, 8, 29, 22, 0, 0));
        Assert.Null(b.Vm.MocChayLaiCheckDonSauFlash);

        // Lượt TRẢ HỒ SƠ KẾ TIẾP (không có Flash nào) ⇒ chạy lại NGAY, không hoãn.
        b.Vm.TraHoSoChoTest(b.Id);
        Assert.Single(b.DaCho);   // vẫn đúng MỘT lượt chờ, không đẻ thêm

        b.ChoXong.TrySetResult(true);
        await ChoLuotHoanXongAsync(b);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2. CHỜ XONG THÌ THẬT SỰ CHẠY LẠI
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Hết biên an ⇒ chạy lại ĐÚNG tài khoản đó; và <b>trước khi hết chờ thì TUYỆT ĐỐI chưa được chạy</b>.
    /// <para>Vế thứ hai mới là vế khoá được cái tên "hoãn": một bản gọi <c>Start</c> ngay rồi mới chờ cũng qua
    /// được vế thứ nhất.</para>
    /// </summary>
    [StaFact]
    public async Task ChoXong_ThiChayLaiDungTaiKhoan_ChuaXongThiChua()
    {
        using var b = Dung("hoan-chay@mail.com");

        VaoNhanhHoan(b, new DateTime(2026, 8, 29, 22, 0, 0));

        Assert.Single(b.DaCho);
        Assert.True(b.DaCho[0] > TimeSpan.Zero, "khoảng chờ không dương — lượt hoãn thành chạy-ngay");
        Assert.Empty(b.DaChayLai);

        b.ChoXong.TrySetResult(true);
        await ChoLuotHoanXongAsync(b);

        Assert.Equal(new[] { b.Id }, b.DaChayLai);
    }

    /// <summary>
    /// Khoảng chờ rót vào chỗ nối phải là khoảng tới ĐÚNG MỐC ĐÃ ĐẶT — không phải một hằng "5 phút" nào.
    /// <para>Đồng hồ của nhánh hoãn đã ĐÔNG LẠNH ở 21:10 (xem <c>Dung</c>) nên phép đo là SỐ CHÍNH XÁC, không
    /// còn kẹp giữa hai lượt đọc <c>DateTime.Now</c> — chính bản kẹp đó là bom hẹn giờ đã nổ ngày 30/08.</para>
    /// <para><b>Hai mốc, không một</b>: khe ngắn (30') và khe dài (110') phải cho ra hai khoảng chờ KHÁC NHAU.
    /// Một bản vá lỡ tay chốt cứng lại một hằng nào đó vẫn qua được vế đơn.</para>
    /// </summary>
    [StaTheory]
    [InlineData(21, 40, 30)]    // cuối khe 21:40 ⇒ chờ 30'
    [InlineData(23, 0, 110)]    // cuối khe 23:00 ⇒ chờ 110'
    public async Task KhoangCho_LaKhoangToiCuoiKhe_KhongPhaiMotHangBatKy(int gio, int phut, int phutCho)
    {
        using var b = Dung("hoan-khoang@mail.com");

        VaoNhanhHoan(b, new DateTime(2026, 8, 29, gio, phut, 0));

        Assert.Single(b.DaCho);
        Assert.Equal(TimeSpan.FromMinutes(phutCho), b.DaCho[0]);   // tới từng phút như dòng log hứa

        b.ChoXong.TrySetResult(true);
        await ChoLuotHoanXongAsync(b);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 3. ⚠ NGƯỜI DÙNG BẤM DỪNG GIỮA CHỪNG ⇒ KHÔNG MỞ LẠI
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Ca nặng nhất của cả nhánh.</b> 21:06 đặt mốc 22:00 → 21:10 người dùng bấm Dừng → 22:00 task tỉnh dậy.
    /// Nó phải THÔI, không được chạy lại.
    ///
    /// <para>Đo CẢ HAI vế: token phải bị huỷ (lượt chờ thoát NGAY, không ngồi tới 22:00), và tài khoản tuyệt
    /// đối không được chạy lại. Chỉ đo vế thứ hai thì một bản "vẫn ngủ đủ 55 phút rồi mới bỏ" cũng qua — mà
    /// bản đó giữ app sống một cách vô nghĩa và làm cú Dừng không có hiệu lực tức thì.</para>
    /// </summary>
    [StaFact]
    public async Task NguoiDungBamDungGiuaChung_ThiKHONG_MoLai()
    {
        using var b = Dung("hoan-dung@mail.com");

        VaoNhanhHoan(b, new DateTime(2026, 8, 29, 22, 0, 0));
        Assert.Single(b.Token);
        Assert.False(b.Token[0].IsCancellationRequested);

        b.Vm.HuyLuotHoanChayLaiCheckDon("người dùng bấm Dừng tất cả");

        Assert.True(b.Token[0].IsCancellationRequested,
            "token của lượt hoãn KHÔNG bị huỷ — người dùng bấm Dừng mà app vẫn đếm ngược tới 22:00");
        await ChoLuotHoanXongAsync(b);
        Assert.Empty(b.DaChayLai);
    }

    /// <summary>
    /// <c>Dispose</c> (thoát app) cũng phải huỷ: task tỉnh dậy trong một VM đã chết rồi mở Brave là mở trình
    /// duyệt sau khi người dùng đã đóng app.
    /// </summary>
    [StaFact]
    public async Task ThoatApp_ThiHuyLuotHoan()
    {
        using var b = Dung("hoan-thoat@mail.com");

        VaoNhanhHoan(b, new DateTime(2026, 8, 29, 22, 0, 0));
        b.Vm.Dispose();

        Assert.True(b.Token[0].IsCancellationRequested);
        await ChoLuotHoanXongAsync(b);
        Assert.Empty(b.DaChayLai);
    }

    /// <summary>
    /// <b>Token đã huỷ mà lượt chờ vẫn về bình thường (chỗ nối không nghe token) ⇒ vẫn KHÔNG được chạy lại.</b>
    ///
    /// <para>Đây là cửa THỨ HAI mà phản biện đòi: kiểm lại ngay TRƯỚC lượt chạy. Cửa token một mình không đủ —
    /// giữa lúc huỷ và lúc task tỉnh dậy vẫn còn một khe, và một lượt mượn hồ sơ khác chen vào có thể thay CTS
    /// mới trong khi task cũ vẫn cầm token cũ.</para>
    /// </summary>
    [StaFact]
    public async Task ChoNoiKhongNgheToken_ThiCuaThuHaiVanChan()
    {
        using var b = Dung("hoan-cua2@mail.com");
        // Chỗ nối CỐ Ý bỏ qua token — mô phỏng đúng khe hở nói trên.
        b.Vm.ChoTruocKhiChayLaiCheckDon = (t, ct) => { b.DaCho.Add(t); b.Token.Add(ct); return b.ChoXong.Task; };

        VaoNhanhHoan(b, new DateTime(2026, 8, 29, 22, 0, 0));
        b.Vm.HuyLuotHoanChayLaiCheckDon("người dùng bấm Dừng tất cả");

        b.ChoXong.TrySetResult(true);
        await ChoLuotHoanXongAsync(b);

        Assert.Empty(b.DaChayLai);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 3b. ⚠ TỚI MỐC MÀ GÓI FLASH CÒN CHẠY ⇒ NHƯỜNG, KHÔNG ĐÈ (03/09/2026, sổ P-20260903-1)
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Ca hỏng đã đo thật, ba lần trong một tối 03/09/2026.</b> Lượt hoãn tới mốc trong lúc một gói Flash
    /// Sale còn đang chạy: nó mở phiên check đơn đè lên ⇒ cổng cầu nối 47821 đã có chủ ⇒
    /// <c>HttpListenerException</c> ⇒ lượt dọn hồ sơ đóng luôn 10 cửa sổ Brave của gói ⇒ shop đang dở HỎNG, và
    /// vòng check đơn cũng hỏng theo ("Vòng cầu nối chưa trọn").
    ///
    /// <para><b>Ba mệnh đề, cần cả ba:</b> (1) CHƯA mở check đơn khi gói còn chạy; (2) đã BẬT cờ nhường để gói
    /// thôi bắt đầu shop mới — không có nó thì lượt hoãn ngồi chờ tới hết trần 90' cho một gói 12 shop rồi bỏ
    /// luôn check đơn; (3) gói xong ⇒ nhịp chờ kế tiếp mở check đơn ĐÚNG tài khoản đó. Thiếu (3) là "không đè"
    /// biến thành "không bao giờ chạy lại".</para>
    /// </summary>
    [StaFact]
    public async Task DangChayFlash_ThiChoShopXong_RoiMoiChayLai()
    {
        using var b = Dung("hoan-nhuong@mail.com");

        var dangChayGoi = true;
        b.Vm.DangChayFlashHoacCauNoi = () => dangChayGoi;

        // HAI CỔNG, không phải một danh sách: `daVaoNhip` báo cho bài test biết vòng nhường đã đứng ở nhịp chờ
        // (và cú hoàn thành TCS là RÀO BỘ NHỚ, nên mọi thứ hook vừa ghi đều thấy được ở đây); `choTiep` là chỗ
        // bài test mở khoá cho vòng quay thêm một lượt.
        var daVaoNhip = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var choTiep = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        b.Vm.ChoNhipNhuongFlash = _ => { daVaoNhip.TrySetResult(true); return choTiep.Task; };

        VaoNhanhHoan(b, new DateTime(2026, 8, 29, 22, 0, 0));
        b.ChoXong.TrySetResult(true);                     // hết khoảng chờ tới mốc

        // Vòng nhường đã vào: cờ BẬT, check đơn CHƯA mở, và nó đang đứng ở nhịp chờ thứ nhất.
        await CoHanAsync(daVaoNhip.Task);
        Assert.True(b.Vm.NhuongChoCheckDon, "chưa bật cờ nhường ⇒ gói vẫn lấy shop mới trong lúc check đơn chờ");
        Assert.Empty(b.DaChayLai);
        Assert.Contains(b.Services.Log.Snapshot("hoan-nhuong@mail.com"),
            e => e.Message.Contains("chờ shop hiện tại xong"));

        // Gói xong ⇒ nhịp kế thấy cờ tắt ⇒ MỞ check đơn, đúng tài khoản.
        dangChayGoi = false;
        choTiep.TrySetResult(true);
        await ChoLuotHoanXongAsync(b);

        Assert.Equal(new[] { b.Id }, b.DaChayLai);
    }

    /// <summary>
    /// <b>Người dùng bấm Dừng GIỮA LÚC ĐANG NHƯỜNG ⇒ tuyệt đối không mở lại.</b>
    /// <para>Vòng nhường có thể kéo tới 90 phút — dài hơn cả lượt chờ tới mốc — nên nó là một cửa sổ MỚI để app
    /// phủ quyết cú Dừng của người dùng, đúng hình dạng lỗi mà cả file này sinh ra để chặn (xem mệnh đề số 3 ở
    /// xmldoc lớp). Đo cả token: cú Dừng phải cắt được nhịp chờ NGAY, không ngồi hết nhịp 10 giây.</para>
    /// </summary>
    [StaFact]
    public async Task HuyTrongLucChoNhuong_ThiKhongMo()
    {
        using var b = Dung("hoan-nhuong-huy@mail.com");
        b.Vm.DangChayFlashHoacCauNoi = () => true;       // gói KHÔNG bao giờ xong

        var daVaoNhip = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokenNhip = CancellationToken.None;
        b.Vm.ChoNhipNhuongFlash = ct =>
        {
            tokenNhip = ct;
            daVaoNhip.TrySetResult(true);
            return Task.Delay(Timeout.Infinite, ct);
        };

        VaoNhanhHoan(b, new DateTime(2026, 8, 29, 22, 0, 0));
        b.ChoXong.TrySetResult(true);
        await CoHanAsync(daVaoNhip.Task);

        b.Vm.HuyLuotHoanChayLaiCheckDon("người dùng bấm Dừng tất cả");

        Assert.True(tokenNhip.IsCancellationRequested,
            "token của nhịp nhường KHÔNG bị huỷ — người dùng bấm Dừng mà app vẫn đứng chờ gói flash");
        await ChoLuotHoanXongAsync(b);
        Assert.Empty(b.DaChayLai);

        // ⚠ CỜ PHẢI ĐƯỢC HẠ trên đường huỷ (sửa vòng 2, 03/09/2026). Cờ "cố ý không hạ" chỉ đúng ở lối ra bình
        // thường — ở đó lượt/gói KẾ TIẾP reset nó. Huỷ thì KHÔNG CÒN AI CHỜ nữa: để nó treo là gói đang chạy
        // dừng sau shop kế cho một lượt check đơn đã chết, và không dòng nào nói ra ("flash chỉ làm 1 shop").
        Assert.False(b.Vm.NhuongChoCheckDon,
            "huỷ giữa lúc nhường mà cờ vẫn treo ⇒ gói đang chạy dừng sau shop kế cho một lượt đã chết");
        Assert.Null(b.Vm.MocHoanChayLaiDangCho);
    }

    /// <summary>
    /// <b>⚠ ĐUA GIỮA NHỊP 60 GIÂY VÀ VÒNG NHƯỜNG 10 GIÂY — mốc hoãn KHÔNG được xoá trước khi nhường xong.</b>
    ///
    /// <para><b>Ca hỏng mà bài này chặn</b> (phản biện vòng 2, 03/09/2026): bản đầu xoá
    /// <c>MocHoanChayLaiDangCho</c> NGAY khi lượt hoãn tới mốc, TRƯỚC vòng chờ. Chuỗi: L1 tới mốc trong lúc gói
    /// G chạy ⇒ bật cờ, XOÁ mốc, nằm chờ ⇒ G nhường xong, hạ <c>_dangChayFlashTuDong</c> ⇒ nhịp 60 giây bắn
    /// TRƯỚC nhịp chờ 10 giây: không phiên nào chạy, mốc đã <c>null</c> ⇒ khe giả định lại là trọn 120' ⇒ MỞ
    /// GÓI G2 <b>và RESET cờ nhường</b> ⇒ L1 tỉnh dậy thấy vẫn "đang chạy" nhưng cờ đã tắt ⇒ G2 không bao giờ
    /// nhường ⇒ L1 ngồi hết trần 90' rồi <b>BỎ LUÔN check đơn</b>.</para>
    ///
    /// <para><b>Hai chốt, đo cả hai:</b> (a) mốc CÒN NGUYÊN suốt lúc nhường — nhịp vì thế vẫn thấy nó và từ
    /// chối mở gói (vế nhịp đo ở <c>FlashGoiKheNghiTests.NhipTrongLucNhuong_*</c>); (b) cờ được ĐẶT LẠI MỖI
    /// NHỊP, nên kể cả khi một lượt khác (flash bấm tay, một vòng KM) reset nó giữa chừng thì lượt sau vẫn
    /// nhường. Bỏ (b) là ca trên chỉ đổi tên thủ phạm.</para>
    /// </summary>
    [StaFact]
    public async Task DuaVoiLuotResetCo_ThiMocConNguyen_VaCoDuocDatLaiMoiNhip()
    {
        using var b = Dung("hoan-nhuong-dua@mail.com");

        var dangChay = true;
        b.Vm.DangChayFlashHoacCauNoi = () => dangChay;

        // Mỗi nhịp là một lượt "chờ tay": bài test mở khoá từng nhịp một để soi trạng thái GIỮA hai nhịp — đúng
        // khoảnh khắc mà ca đua xảy ra.
        var vaoNhip = new[] { CongMoi(), CongMoi() };
        var choTiep = new[] { CongMoi(), CongMoi() };
        var soNhip = 0;
        b.Vm.ChoNhipNhuongFlash = _ =>
        {
            var k = Interlocked.Increment(ref soNhip) - 1;
            if (k >= vaoNhip.Length) { return Task.CompletedTask; }
            vaoNhip[k].TrySetResult(true);
            return choTiep[k].Task;
        };

        var cuoiKhe = new DateTime(2026, 8, 29, 22, 0, 0);
        VaoNhanhHoan(b, cuoiKhe);
        b.ChoXong.TrySetResult(true);

        // ── (a) ĐANG NHƯỜNG: mốc CÒN, cờ BẬT, check đơn CHƯA mở.
        await CoHanAsync(vaoNhip[0].Task);
        Assert.Equal(cuoiKhe, b.Vm.MocHoanChayLaiDangCho);
        Assert.True(b.Vm.NhuongChoCheckDon);
        Assert.Empty(b.DaChayLai);

        // ── (b) MỘT LƯỢT KHÁC MỞ GIỮA CHỪNG và reset cờ (đúng cái nhịp/đường tay/KM/Combo làm trước mỗi lượt).
        b.Vm.HaCoNhuongChoTest();
        Assert.False(b.Vm.NhuongChoCheckDon);         // tiền đề: cờ ĐÃ bị hạ thật
        choTiep[0].TrySetResult(true);                // vòng nhường quay thêm một nhịp

        await CoHanAsync(vaoNhip[1].Task);
        Assert.True(b.Vm.NhuongChoCheckDon,
            "cờ nhường KHÔNG được đặt lại ⇒ lượt mở giữa chừng chạy trọn danh sách, còn check đơn chờ hết trần");
        Assert.Equal(cuoiKhe, b.Vm.MocHoanChayLaiDangCho);   // mốc vẫn phải chặn nhịp

        // ── (c) Lượt kia xong ⇒ check đơn mở, và mốc mới được xoá.
        dangChay = false;
        choTiep[1].TrySetResult(true);
        await ChoLuotHoanXongAsync(b);

        Assert.Equal(new[] { b.Id }, b.DaChayLai);
        Assert.Null(b.Vm.MocHoanChayLaiDangCho);
    }

    /// <summary>
    /// <b>Câu nhường phải gọi ĐÚNG TÊN cái đang giữ cầu nối.</b>
    ///
    /// <para><b>Vì sao</b> (phản biện vòng 2, 03/09/2026): cửa <c>DangChayFlashHoacCauNoi</c> gồm cả
    /// <c>_bridgeRunning</c>, tức "Mở list shop", một lượt KM/Combo bấm tay, hay vòng KM tự động 2h sáng — nhưng
    /// bản đầu nói cứng <i>"Gói Flash Sale đang chạy"</i> cho mọi ca. Đây là dòng DUY NHẤT giải thích vì sao
    /// check đơn muộn 40 phút; nó khai một gói flash không tồn tại thì người đọc thôi tin cả nhật ký.</para>
    ///
    /// <para>Vế ĐỐI CHỨNG nằm ngay trong bài: câu KM <b>không được chứa "Flash"</b>. Thiếu nó thì một bản vá
    /// ghép thêm tên việc vào SAU câu cũ vẫn xanh.</para>
    /// </summary>
    [StaFact]
    public async Task CauNhuong_NoiDungTenLatCatDangChay()
    {
        using var b = Dung("hoan-nhuong-ten@mail.com");
        b.Vm.DangChayFlashHoacCauNoi = () => true;      // lượt KM KHÔNG bao giờ xong
        b.Vm.DatTenLatCatChoTest("KM của Shop");

        var daVaoNhip = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        b.Vm.ChoNhipNhuongFlash = ct =>
        {
            daVaoNhip.TrySetResult(true);
            return Task.Delay(Timeout.Infinite, ct);
        };

        VaoNhanhHoan(b, new DateTime(2026, 8, 29, 22, 0, 0));
        b.ChoXong.TrySetResult(true);
        await CoHanAsync(daVaoNhip.Task);

        var cau = b.Services.Log.Snapshot("hoan-nhuong-ten@mail.com")
            .Single(e => e.Message.Contains("chờ shop hiện tại xong")).Message;
        Assert.Contains("Lượt 'KM của Shop' đang giữ cầu nối", cau);
        Assert.DoesNotContain("Flash", cau);

        b.Vm.HuyLuotHoanChayLaiCheckDon("dọn bài test");
        await ChoLuotHoanXongAsync(b);
    }

    /// <summary>
    /// <b>Quá trần chờ (<see cref="KmLichTuDong.TranChoNhuongFlash"/> = 2 × hạn một shop flash) ⇒ KHÔNG mở
    /// check đơn, và nói RÕ vì sao.</b>
    ///
    /// <para><b>Đây là quyết định có chủ ý, không phải bỏ sót:</b> thà thiếu check đơn một khe còn hơn đè lên
    /// một gói đang giữ hồ sơ Brave + cổng 47821. Nhưng người dùng phải BIẾT — nên dòng nhật ký ở đó là phần
    /// bắt buộc của mệnh đề, và bài này đo cả nó lẫn cả việc trần đúng bằng 2× hạn chặng.</para>
    ///
    /// <para>Đồng hồ của nhánh hoãn đã đông lạnh ở 21:10 (xem <c>Dung</c>) nên bài này tự nhấc nó qua trần thay
    /// vì chờ thật 90 phút — cùng lý do <c>DongHoHoanChayLai</c> tồn tại.</para>
    /// </summary>
    [StaFact]
    public async Task QuaTranChoNhuong_ThiKhongMo_VaLogKhongDe()
    {
        using var b = Dung("hoan-nhuong-tran@mail.com");
        b.Vm.DangChayFlashHoacCauNoi = () => true;       // gói KHÔNG bao giờ xong

        // Trần là 2× hạn chặng một shop flash — khoá con số lại ở đây, không viết cứng "90 phút".
        Assert.Equal(OrdersBridgeChannel.ChoChang.FlashChay * 2, KmLichTuDong.TranChoNhuongFlash);

        var batDau = new DateTime(2026, 8, 29, 21, 10, 0);
        var bayGio = batDau;
        b.Vm.DongHoHoanChayLai = () => bayGio;
        // Nhịp chờ thứ nhất trả về ngay nhưng nhấc đồng hồ QUA trần ⇒ vòng lặp phải bỏ ở lượt kiểm kế tiếp.
        b.Vm.ChoNhipNhuongFlash = _ =>
        {
            bayGio = batDau + KmLichTuDong.TranChoNhuongFlash + TimeSpan.FromMinutes(1);
            return Task.CompletedTask;
        };

        VaoNhanhHoan(b, new DateTime(2026, 8, 29, 22, 0, 0));
        b.ChoXong.TrySetResult(true);
        await ChoLuotHoanXongAsync(b);

        Assert.Empty(b.DaChayLai);
        var log = b.Services.Log.Snapshot("hoan-nhuong-tran@mail.com");
        Assert.Contains(log, e => e.Message.Contains("QUÁ HẠN CHỜ gói Flash Sale")
                                  && e.Message.Contains("KHÔNG mở check đơn")
                                  && e.Message.Contains("Bấm ▶ Chạy"));
    }

    /// <summary>
    /// Vế ĐỐI CHỨNG: <b>KHÔNG có gói nào chạy ⇒ mở check đơn NGAY, không ghi dòng nhường nào.</b>
    /// <para>Không có vế này thì một bản vá "luôn nhường" cũng qua ba bài trên — và mọi lượt hoãn bình thường
    /// sẽ ngồi chờ hết trần 90 phút rồi bỏ luôn check đơn. Đây cũng là đường đi THƯỜNG NGÀY: gói xong sớm rồi
    /// mới tới mốc hoãn.</para>
    /// </summary>
    [StaFact]
    public async Task KhongCoGoiNaoChay_ThiMoNgay_KhongGhiDongNhuong()
    {
        using var b = Dung("hoan-nhuong-sach@mail.com");
        b.Vm.DangChayFlashHoacCauNoi = () => false;
        b.Vm.ChoNhipNhuongFlash = _ => throw new InvalidOperationException(
            "KHÔNG được vào vòng nhường khi chẳng có gói nào đang chạy");

        VaoNhanhHoan(b, new DateTime(2026, 8, 29, 22, 0, 0));
        b.ChoXong.TrySetResult(true);
        await ChoLuotHoanXongAsync(b);

        Assert.Equal(new[] { b.Id }, b.DaChayLai);
        Assert.False(b.Vm.NhuongChoCheckDon);
        Assert.DoesNotContain(b.Services.Log.Snapshot("hoan-nhuong-sach@mail.com"),
            e => e.Message.Contains("chờ shop hiện tại xong"));
    }

    /// <summary>Một cổng mở-tay cho bài test — TCS chạy tiếp BẤT ĐỒNG BỘ để cú <c>TrySetResult</c> không kéo
    /// luôn phần còn lại của vòng nhường vào thread của bài test.</summary>
    private static TaskCompletionSource<bool> CongMoi()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Chờ một task, có HẠN — mệnh đề sai thì bài ĐỎ với một câu nói rõ, không TREO cả bộ test
    /// (sổ P-20260901-2/-3).</summary>
    private static async Task CoHanAsync(Task t)
    {
        var xong = await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(ReferenceEquals(xong, t), "hết 5 giây mà vòng nhường chưa vào tới nhịp chờ");
        await t;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 4. BA ĐƯỜNG DỪNG đều phải gọi lượt huỷ
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Cả ba đường "người dùng thôi" đều phải HUỶ lượt hoãn</b>: <c>StopAllAsync</c> · <c>StopSelected</c>
    /// (khi tập tick giao với tập đang hoãn) · <c>Dispose</c>.
    ///
    /// <para>Đo trên NGUỒN vì hai đường đầu chạy qua UI/`Sessions` thật (phóng trình duyệt). Thiếu MỘT đường là
    /// còn một cú bấm mà app phủ quyết được — và đó chính là hình dạng của lỗi này.</para>
    ///
    /// <para><b>⚠ <c>StopSelected</c> phải hỏi <c>_dsTkDangHoanChayLai</c>, KHÔNG phải <c>_dsTkDaDung</c></b>:
    /// tập sau đã bị <c>TraHoSo</c> dọn sạch ngay lúc đặt lượt hoãn, nên vế cũ không còn nhìn thấy tài khoản
    /// đang hoãn nữa.</para>
    /// </summary>
    [Fact]
    public void BaDuongDung_DeuGoiLuotHuy()
    {
        var phien = NguonFile("orders", "XuLyDonShopee.App", "ViewModels", "AccountsViewModel.Phien.cs");
        var vm = NguonFile("orders", "XuLyDonShopee.App", "ViewModels", "AccountsViewModel.cs");

        var stopAll = CatVung(phien, "private async Task StopAllAsync()", "/// <summary>Câu báo đã đóng cửa sổ");
        Assert.Contains("HuyLuotHoanChayLaiCheckDon(", stopAll);

        var stopSel = CatVung(phien, "private void StopSelected()", "/// <summary>\"Dừng tất cả\"");
        Assert.Contains("_dsTkDangHoanChayLai.Contains", stopSel);
        Assert.Contains("HuyLuotHoanChayLaiCheckDon(", stopSel);

        Assert.Contains("HuyLuotHoanChayLaiCheckDon(\"thoát app\")", vm);
    }

    // ── Lượt 3 (04/09/2026): vòng đời lượt hoãn kéo tới lối ra THẬT, không phải tới mốc ─────────────────

    /// <summary>
    /// <b>"Dừng đã chọn" GIỮA LÚC NHƯỜNG phải huỷ được lượt hoãn</b> (sửa lượt 3, 04/09/2026 — phản biện dựng được,
    /// NẶNG). Bản trước rút tài khoản khỏi tập hoãn ngay khi tới mốc rồi mới vào vòng nhường (tới 90'): suốt
    /// quãng đó <c>StopSelected</c> không thấy tài khoản, không huỷ CTS, và lượt tỉnh dậy tự mở Brave sau khi
    /// người dùng đã bảo dừng — đúng ca phản biện 29/08/2026 chấm NẶNG, mở lại bằng một đường khác. Bài
    /// <c>BaDuongDung_DeuGoiLuotHuy</c> chỉ neo NGUỒN nên vẫn xanh khi hành vi này hỏng — bài này đo HÀNH VI.
    /// </summary>
    [StaFact]
    public async Task DungDaChonTrongLucNhuong_ThiHuyDuoc_KhongMoLai()
    {
        using var b = Dung("hoan-nhuong-dung-da-chon@mail.com");
        b.Vm.DangChayFlashHoacCauNoi = () => true;       // gói không bao giờ tự xong… cho tới khi test hạ cờ ở dưới

        // Nhịp NGẮN, không phải Infinite: nếu cửa huỷ hỏng, lượt phải đi tiếp tới ChayLai để bài này ĐỎ chứ không treo.
        var daVaoNhip = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        b.Vm.ChoNhipNhuongFlash = ct => { daVaoNhip.TrySetResult(true); return Task.Delay(20, ct); };

        VaoNhanhHoan(b, new DateTime(2026, 8, 29, 22, 0, 0));
        b.ChoXong.TrySetResult(true);
        await CoHanAsync(daVaoNhip.Task);

        // Đang nhường: tài khoản PHẢI còn trong tập hoãn — không thì cửa dưới của StopSelected mù.
        Assert.True(b.Vm.DangHoanChayLai(b.Id),
            "đang nhường mà tài khoản đã rời tập hoãn — 'Dừng đã chọn' sẽ không huỷ được lượt");

        var dong = b.Vm.Accounts.First(r => r.Id == b.Id);
        dong.IsSelected = true;
        b.Vm.StopSelectedCommand.Execute(null);

        // Gói "xong" NGAY SAU cú Dừng: cửa huỷ đúng thì lượt đã chết vì token; cửa huỷ hỏng thì lượt thấy đường
        // sạch và MỞ check đơn ⇒ Assert.Empty dưới đây bắt được, thay vì bài treo vô hạn.
        b.Vm.DangChayFlashHoacCauNoi = () => false;

        await ChoLuotHoanXongAsync(b);
        Assert.Empty(b.DaChayLai);
        Assert.False(b.Vm.NhuongChoCheckDon, "huỷ rồi mà cờ nhường vẫn treo");
        Assert.Null(b.Vm.MocHoanChayLaiDangCho);
        Assert.False(b.Vm.DangHoanChayLai(b.Id));
    }

    /// <summary>
    /// <b>Lượt hoãn THỨ HAI cho cùng tài khoản (đường bình thường: gói mở trong khe hoãn vừa xong) KHÔNG được xoá
    /// mốc của lượt đang nhường, và chỉ MỘT lần mở</b> (sửa lượt 3, 04/09/2026). Bản trước: tới mốc hai lượt cùng
    /// tỉnh, bên thua <c>Remove</c> trả false rồi rơi vào nhánh "ĐÃ BỊ HUỶ" và xoá mốc — mở lại đúng cửa sổ đua
    /// mà F1 vừa đóng. Với danh tính lượt: lượt cũ thấy mình đã bị thay thì rút lui im lặng; lượt mới là chủ duy nhất.
    /// </summary>
    [StaFact]
    public async Task LuotHoanThuHai_CungTaiKhoan_KhongXoaMocCuaLuotDangNhuong_VaChiMoMotLan()
    {
        using var b = Dung("hoan-hai-luot@mail.com");
        b.Vm.DangChayFlashHoacCauNoi = () => true;

        var daVaoNhip = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        b.Vm.ChoNhipNhuongFlash = ct => { daVaoNhip.TrySetResult(true); return Task.Delay(20, ct); };

        var moc = new DateTime(2026, 8, 29, 22, 0, 0);
        VaoNhanhHoan(b, moc);   // L1
        VaoNhanhHoan(b, moc);   // L2 — gói mở trong khe hoãn vừa xong, lên lịch lại cùng mốc
        Assert.Equal(2, b.DaCho.Count);

        b.ChoXong.TrySetResult(true);   // cả hai cùng tỉnh
        await CoHanAsync(daVaoNhip.Task);

        // Chủ lượt (L2) đang nhường: mốc PHẢI còn nguyên dù L1 vừa tỉnh và rút lui.
        Assert.Equal(moc, b.Vm.MocHoanChayLaiDangCho);
        Assert.True(b.Vm.NhuongChoCheckDon);
        Assert.True(b.Vm.DangHoanChayLai(b.Id));

        b.Vm.DangChayFlashHoacCauNoi = () => false;   // gói nhường xong
        await ChoLuotHoanXongAsync(b);

        Assert.Equal(new[] { b.Id }, b.DaChayLai);    // đúng MỘT lần mở, không đúp
        Assert.Null(b.Vm.MocHoanChayLaiDangCho);
        Assert.False(b.Vm.DangHoanChayLai(b.Id));
    }

    // ── Đồ nghề ───────────────────────────────────────────────────────────────────────────────────────

    private static string NguonFile(params string[] duong)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "ShopeeSuite.sln"))) { continue; }
            var f = Path.Combine(new[] { dir.FullName }.Concat(duong).ToArray());
            if (File.Exists(f)) { return File.ReadAllText(f); }
        }

        throw new FileNotFoundException("Không tìm thấy " + string.Join("/", duong));
    }

    private static string CatVung(string nguon, string tu, string den)
    {
        var i = nguon.IndexOf(tu, StringComparison.Ordinal);
        Assert.True(i >= 0, $"không còn đoạn bắt đầu bằng '{tu}'");
        var j = nguon.IndexOf(den, i, StringComparison.Ordinal);
        Assert.True(j > i, $"không còn mốc kết thúc '{den}' sau '{tu}'");
        return nguon[i..j];
    }
}
