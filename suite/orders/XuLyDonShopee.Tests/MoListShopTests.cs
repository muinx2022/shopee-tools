using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.App.Views;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Nút <b>"Mở list shop"</b> (tab con "Shops", màn Tài khoản): đi ĐÚNG luồng chạy tài khoản nhưng DỪNG ở trang
/// chọn shop và ĐỂ NGUYÊN cửa sổ Brave cho người dùng.
/// <para>Bốn mệnh đề được khoá ở đây, theo đúng thứ tự dễ hỏng:</para>
/// <list type="number">
/// <item><b>"Đến list shop thì DỪNG"</b> — chạy trên cầu nối THẬT (<see cref="BridgeTestRig"/>): đọc xong danh
/// sách shop là im, KHÔNG có <c>openShopDetail</c> nào bay tiếp. Đây là mệnh đề trung tâm của cả việc này nên
/// nó phải là TEST chứ không phải một lượt đọc mã.</item>
/// <item>Chốt cửa + <see cref="AccountsViewModel.TinhCanStop"/> + <see cref="AccountsViewModel.NenDongCuaSoXem"/>
/// — ba hàm thuần quyết định "có mở không / nút Dừng có sáng không / có đóng cửa sổ để lại không".</item>
/// <item>Khoá chạy XUYÊN MÁY: máy khác đang giữ tài khoản ⇒ không mở gì (cửa sổ này chiếm tài khoản trên Shopee
/// y như một vòng chạy).</item>
/// <item>Nút vẫn còn trên XAML, bind đúng command + đúng cờ bật/tắt, và nằm trong tab "Shops".</item>
/// </list>
/// </summary>
public class MoListShopTests
{
    // ══════════ 1. Hành vi THẬT của đường đọc danh sách shop (cầu nối thật, không trình duyệt) ══════════

    /// <summary>Bảng shop y khuôn extension gửi về (<c>pageScanShopList</c> quét <c>tr[data-row-key]</c>).</summary>
    private const string BangShopJson =
        """[{"rowKey":"111","name":"Shop Một","login":"shop.mot"},""" +
        """{"rowKey":"222","name":"Shop Hai","login":"shop.hai"},""" +
        """{"rowKey":"333","name":"Shop Ba","login":"shop.ba"}]""";

    /// <summary>
    /// Đường đọc danh sách shop dùng chung: gửi ĐÚNG <c>readShopList</c>, parse đúng bảng nhận về, và bắn callback
    /// tab "Shops" ĐÚNG MỘT lần với đúng danh sách đó.
    /// </summary>
    [Fact]
    public async Task DocDanhSachShop_GuiDungLenh_ParseDung_BanCallbackDungMotLan()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var lanBan = 0;
        IReadOnlyList<ShopListItem>? nhanDuoc = null;
        var chay = OrdersBridgeSession.DocDanhSachShopAsync(
            rig.Channel,
            shops => { lanBan++; nhanDuoc = shops; },
            CancellationToken.None);

        using (var lenh = await rig.NhanLenhAsync())
        {
            Assert.Equal("readShopList", lenh.RootElement.GetProperty("action").GetString());
        }
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BangShopJson });

        var shops = await chay;

        Assert.Equal(3, shops.Count);
        Assert.Equal(new[] { "111", "222", "333" }, shops.Select(s => s.ShopId));
        Assert.Equal("shop.mot", shops[0].LoginName);

        Assert.Equal(1, lanBan);
        Assert.Same(shops, nhanDuoc); // callback nhận CHÍNH danh sách trả về, không phải một bản khác
    }

    /// <summary>
    /// <b>MỆNH ĐỀ TRUNG TÂM: "đến list shop thì DỪNG".</b> Chuỗi lệnh mà cả pha sau-picker gửi xuống extension
    /// phải ĐÚNG BẰNG <c>[readShopList, detachDebugger]</c> — không thiếu, không thừa.
    /// <para><b>Đo trên <see cref="OrdersBridgeSession.DocXongThiDungAsync"/>, KHÔNG phải trên
    /// <c>DocDanhSachShopAsync</c>.</b> Bản test đầu (18/08/2026) đo hàm đọc-danh-sách 4 dòng, và lượt phản biện
    /// chỉ ra nó đo hụt phạm vi: hàm ấy vốn KHÔNG THỂ gửi thêm lệnh nào, nên khẳng định "không có
    /// <c>openShopDetail</c>" của nó đúng bất kể ai nhét gì vào <c>MoDenDanhSachShopAsync</c> — tức đúng cái
    /// regression việc này sinh ra để chặn thì nó không thấy.</para>
    /// <para>Vẫn không gọi được thẳng <c>MoDenDanhSachShopAsync</c>: bước đăng nhập của nó phóng trình duyệt
    /// thật. <c>DocXongThiDungAsync</c> là TOÀN BỘ phần còn lại sau khi đã về picker — đúng khúc mà bản cũ đi
    /// tiếp vào vòng lặp shop.</para>
    /// </summary>
    [Fact]
    public async Task DocXongThiDung_ChuoiLenhDungBang_ReadShopList_RoiDetachDebugger()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var chay = OrdersBridgeSession.DocXongThiDungAsync(
            rig.Channel, null, rig.Log, CancellationToken.None);

        var lenhDaGui = new List<string>();
        using (var lenh = await rig.NhanLenhAsync())
        {
            lenhDaGui.Add(lenh.RootElement.GetProperty("action").GetString() ?? "");
        }
        // Chỉ trả bảng shop SAU khi đã ghi nhận lệnh đầu — đúng nhịp của extension thật.
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BangShopJson });

        var ket = await chay;
        Assert.Equal(3, ket.Shops.Count);
        Assert.Null(ket.Error);
        Assert.False(ket.Captcha);
        // "Dừng ở picker" nghĩa là KHÔNG đụng shop nào: không có shop đầu nào được mở, không đọc Chờ Lấy Hàng.
        Assert.Null(ket.FirstShopId);
        Assert.Null(ket.ToShipCount);

        // Vét nốt mọi lệnh còn lại rồi mới so cả chuỗi.
        while (await ThuNhanThemLenhAsync(rig, 400) is { } them)
        {
            lenhDaGui.Add(them);
        }

        Assert.Equal(new[] { "readShopList", "detachDebugger" }, lenhDaGui);
    }

    /// <summary>
    /// Chờ <paramref name="cho"/> ms xem còn lệnh nào bay đi nữa không; hết giờ = không còn ⇒ null.
    /// <para><b>Lượt trả null là lượt CUỐI dùng được.</b> Hết hạn chờ là <c>BridgeTestRig</c> huỷ token của
    /// <c>ReceiveAsync</c>, mà lượt huỷ đó ABORT luôn <c>ClientWebSocket</c> — mọi lượt nhận sau chỉ còn ném
    /// <c>WebSocketException("Aborted")</c>. Gọi trong vòng lặp thì được (mỗi lệnh có thật trả về ngay, chỉ lượt
    /// hết giờ cuối cùng mới giết socket), nhưng đừng gọi lại sau khi đã nhận null.</para>
    /// <para>Nhịp giữ-sống của rig là 20s (mặc định production) nên không có gói <c>ping</c> nào chen vào.</para>
    /// </summary>
    private static async Task<string?> ThuNhanThemLenhAsync(BridgeTestRig rig, int cho = 700)
    {
        try
        {
            using var lenh = await rig.NhanLenhAsync(TimeSpan.FromMilliseconds(cho));
            return lenh.RootElement.TryGetProperty("action", out var a)
                ? a.GetString()
                : lenh.RootElement.ToString();
        }
        catch (OperationCanceledException)
        {
            return null; // im lặng — đúng cái ta muốn
        }
    }

    // ══════════ 2. Ba hàm quyết định THUẦN ══════════

    /// <summary>
    /// Cờ "còn thứ cần đóng" sáng theo BA nguồn độc lập. Ca quan trọng nhất là ca thứ ba: hai vế cũ đều false mà vẫn phải
    /// sáng — đó đúng là trạng thái sau khi "Mở list shop" chạy xong và để nguyên cửa sổ.
    /// </summary>
    [Fact]
    public void TinhCanStop_MaTran()
    {
        Assert.False(AccountsViewModel.TinhCanStop(false, false, false));
        Assert.True(AccountsViewModel.TinhCanStop(true, false, false));
        Assert.True(AccountsViewModel.TinhCanStop(false, true, false));

        // CA CHÍNH: chỉ còn cửa sổ "Mở list shop" để lại. Bỏ vế này là nút Dừng tắt trong khi cửa sổ vẫn giữ khoá
        // hồ sơ và extension của nó vẫn tranh cổng cầu nối 47821.
        Assert.True(AccountsViewModel.TinhCanStop(false, false, true));
    }

    /// <summary>
    /// <b>Nút "Đóng cửa sổ" phải BẤM ĐƯỢC ngay trong lúc lát cắt đang chạy</b> — lúc đó bấm = HUỶ.
    /// <para>Đây là ca mà bản trước sai và không test nào thấy: <c>CuaSoXemDangMo</c> đọc <c>_pocProcess</c>, mà
    /// biến đó mãi tới khi lát cắt XONG mới được gán ⇒ suốt lúc chạy nút tắt. Trong khi hai câu duy nhất bảo
    /// người dùng "bấm 'Đóng cửa sổ'" lại CHỈ phát ra đúng lúc đó. Nặng nhất ở nhánh cookie hết hạn: lát cắt
    /// đứng chờ nhập mã vài phút mà không có đường huỷ nào.</para>
    /// </summary>
    [StaFact]
    public void DangChayLatCat_ThiNutDongCuaSo_VanBamDuoc()
    {
        var (temp, services, vm) = DungVm();
        using var db = temp;
        ChonTaiKhoan(services, vm, "dangchay@mail.com");

        Assert.False(vm.CoTheDongCuaSo); // nền: chưa có gì để đóng

        GiaVoDangChayLatCat(vm);

        Assert.True(vm.CoTheDongCuaSo,
            "Lát cắt đang chạy mà nút 'Đóng cửa sổ' tắt — không còn đường huỷ nào cho người dùng.");
    }

    /// <summary>Chỉ đóng cửa sổ để lại khi nó thuộc ĐÚNG tài khoản bị tác động.</summary>
    [Fact]
    public void NenDongCuaSoXem_MaTran()
    {
        Assert.False(AccountsViewModel.NenDongCuaSoXem(null, new long[] { 1, 2, 3 }));
        Assert.True(AccountsViewModel.NenDongCuaSoXem(2, new long[] { 1, 2, 3 }));

        // "Dừng đã chọn" của tài khoản khác KHÔNG được cướp cửa sổ người dùng đang xem.
        Assert.False(AccountsViewModel.NenDongCuaSoXem(9, new long[] { 1, 2, 3 }));
    }

    // ══════════ 3. ViewModel thật (SQLite tạm, không trình duyệt) ══════════

    private static (TempDatabase Temp, AppServices Services, AccountsViewModel Vm) DungVm(
        Func<long, IAccountSession>? phienGia = null)
    {
        var temp = new TempDatabase();
        // Factory phải vào từ CTOR: AccountsViewModel đăng ký Sessions.Changed ngay trong ctor của nó, nên gán
        // lại Sessions sau đó là cắt đứt người nghe — test sẽ chạy trong môi trường KHÔNG có sự kiện phiên.
        var services = new AppServices(temp.Path, phienGia);
        return (temp, services, new AccountsViewModel(services));
    }

    /// <summary>Chọn tài khoản đầu tiên trong lưới (đổ form + đặt <c>_editingId</c>).</summary>
    private static string ChonTaiKhoan(AppServices services, AccountsViewModel vm, string email)
    {
        services.Accounts.Insert(new Account { Email = email, Password = "p" });
        vm.Reload();
        vm.SelectedRow = vm.Accounts.First(a => a.Email == email);
        return email;
    }

    /// <summary>
    /// KHOÁ CHẠY XUYÊN MÁY: máy khác đang giữ tài khoản ⇒ TỪ CHỐI ngay, nói rõ TÊN MÁY, và KHÔNG mở gì.
    /// <para>Vì sao bắt buộc: cửa sổ "Mở list shop" đăng nhập chính tài khoản Shopee đó, mở lúc máy kia đang chạy
    /// vòng của nó là đá phiên/ăn captcha. Nút Dừng phải vẫn TẮT — không có cửa sổ nào để đóng.</para>
    /// </summary>
    [StaFact]
    public async Task MoListShop_MayKhacDangGiuKhoa_TuChoi_KhongMoGi()
    {
        var (temp, services, vm) = DungVm();
        using var db = temp;

        var email = ChonTaiKhoan(services, vm, "khoa@mail.com");
        var soLanXin = 0;
        services.AcquireAccountLease = (login, ct) =>
        {
            soLanXin++;
            Assert.Equal(email, login); // hook nhận login THÔ, module không tự chuẩn hoá
            return Task.FromResult(new OrdersLeaseResult(false, "MAY-KHO-1"));
        };

        await vm.MoListShopCommand.ExecuteAsync(null);

        Assert.Equal(1, soLanXin);
        Assert.Contains("MAY-KHO-1", vm.BusyStatus ?? "");
        Assert.Contains(services.Log.Snapshot(email), e => e.Message.Contains("MAY-KHO-1"));

        // Không có cửa sổ nào được mở ⇒ nút Dừng vẫn tắt (đây là VM sạch, chưa phiên production nào).
        Assert.False(vm.CanStop);
    }

    /// <summary>
    /// Chưa chọn tài khoản nào ⇒ lệnh no-op tuyệt đối: không ném, không xin khoá, không ghi nhật ký.
    /// (Nút bind <c>CanRun</c> nên bình thường đang mờ — nhưng lệnh vẫn phải tự chốt cửa, vì
    /// <c>ICommand.Execute</c> gọi được từ chỗ khác.)
    /// </summary>
    [StaFact]
    public async Task MoListShop_ChuaChonTaiKhoan_KhongLamGi()
    {
        var (temp, services, vm) = DungVm();
        using var db = temp;

        // Hook TỪ CHỐI, cố ý: nếu ai đó gỡ mất chốt "chưa chọn tài khoản" thì lệnh sẽ đi tiếp và rơi vào đây —
        // cờ daXinKhoa bật, test đỏ, mà KHÔNG có trình duyệt thật nào bị phóng. Hook trả Ok=true thì lượt thử phá
        // của chính test này sẽ mở Brave thật.
        var daXinKhoa = false;
        services.AcquireAccountLease = (login, ct) =>
        {
            daXinKhoa = true;
            return Task.FromResult(new OrdersLeaseResult(false, "MAY-KHAC"));
        };

        await vm.MoListShopCommand.ExecuteAsync(null); // không được ném

        Assert.False(daXinKhoa);
        Assert.False(vm.CanRun);
        Assert.False(vm.CanStop);
        Assert.Null(vm.BusyStatus);
    }

    // ══════════ 3b. Lát cắt đang chạy ⇒ MỌI đường mở phiên phải lùi ══════════

    /// <summary>
    /// Bật cờ <c>_bridgeRunning</c> — trạng thái "lát cắt cầu nối đang chạy" mà không có đường công khai nào
    /// đặt được (đặt thật thì phải phóng trình duyệt). Đọc/ghi field private trong test đã có tiền lệ ở
    /// <c>AccountsViewVongDoiTests.SoNguoiNghe</c>.
    /// </summary>
    /// <summary>Rót hook khoá xuyên máy TỪ CHỐI — phiên có lỡ được mở thì cũng dừng trước khi đụng trình duyệt
    /// (xem ghi chú "lưới an toàn" ở <see cref="LatCatDangChay_BaDuongMoPhien_DeuTuChoi_KhongMoPhienNao"/>).</summary>
    private static void LuoiChanMoTrinhDuyet(AppServices services)
        => services.AcquireAccountLease =
            (login, ct) => Task.FromResult(new OrdersLeaseResult(false, "LUOI-AN-TOAN-TEST"));

    private static void GiaVoDangChayLatCat(AccountsViewModel vm)
    {
        var f = typeof(AccountsViewModel).GetField("_bridgeRunning",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(f); // đổi tên field mà quên test này ⇒ đỏ ngay, không âm thầm bỏ qua
        f!.SetValue(vm, true);
    }

    /// <summary>
    /// <b>Một khoá KHÔNG được có hai chủ.</b> Lát cắt đang chạy thì cả ba đường mở phiên production phải từ chối:
    /// <c>Run</c> (một tài khoản), <c>RunSelected</c> (hàng loạt), <c>TruyCapTk</c> (nút trên dòng chưa xác nhận).
    /// <para>Vì sao nặng (phản biện 18/08/2026): <c>ReleaseAccountLease</c> phía shell là DELETE thẳng theo
    /// (tài khoản, máy), KHÔNG đếm chủ — nên lượt nhả của cửa sổ xem sẽ XOÁ lease của phiên production đang chạy
    /// cùng tài khoản, và máy kia lập tức chen vào. Chưa kể hai Chromium cùng một hồ sơ.</para>
    /// <para>Gộp ba đường một <c>[Fact]</c>: cùng một luật, và mỗi đường cần một VM sạch riêng.</para>
    /// <para><b>LƯỚI AN TOÀN, đừng gỡ:</b> mỗi VM được rót <see cref="LuoiChanMoTrinhDuyet"/>. Lượt THỬ PHÁ của
    /// chính test này là gỡ chốt ra — lúc đó <c>Sessions.Start</c> chạy thật, và nếu không có lưới thì luồng nền
    /// đi tới <c>OrdersBridgeLauncher.Launch</c>, chỗ gọi
    /// <c>FreeProfile(alsoMatchBridgeExtension: true)</c> — quét GIẾT MỌI trình duyệt cầu nối trên máy, kể cả
    /// của app người dùng đang chạy thật. Khoá lease được kiểm TRƯỚC mọi việc đụng trình duyệt
    /// (<c>AccountSession.RunBridgeContinuousAsync</c> → <c>XinKhoaChayAsync</c>) nên lưới chặn kịp, mà
    /// <c>Sessions.Active</c> thì đã kịp non-empty ⇒ test vẫn ĐỎ đúng chỗ.</para>
    /// </summary>
    [StaFact]
    public void LatCatDangChay_BaDuongMoPhien_DeuTuChoi_KhongMoPhienNao()
    {
        // ── Đường 1: "Chạy" (tài khoản đang xem) ──
        {
            var (temp, services, vm) = DungVm();
            using var db = temp;
            var email = ChonTaiKhoan(services, vm, "run@mail.com");
            LuoiChanMoTrinhDuyet(services);
            GiaVoDangChayLatCat(vm);

            vm.RunCommand.Execute(null);

            Assert.Empty(services.Sessions.Active);
            Assert.Contains(services.Log.Snapshot(email),
                e => e.Message.Contains(AccountsViewModel.CauTuChoiViLatCatDangChay));
            Assert.Equal(AccountsViewModel.CauTuChoiViLatCatDangChay, vm.BusyStatus);
        }

        // ── Đường 2: "Chạy đã chọn" (hàng loạt theo tick) ──
        {
            var (temp, services, vm) = DungVm();
            using var db = temp;
            ChonTaiKhoan(services, vm, "batch@mail.com");
            vm.Accounts.First().IsSelected = true;
            LuoiChanMoTrinhDuyet(services);
            GiaVoDangChayLatCat(vm);

            vm.RunSelectedCommand.Execute(null);

            Assert.Empty(services.Sessions.Active);
            Assert.Contains(services.Log.Snapshot("Hàng loạt"),
                e => e.Message.Contains(AccountsViewModel.CauTuChoiViLatCatDangChay));
        }

        // ── Đường 3: "Truy cập TK" — đường trước 18/08/2026 KHÔNG có chốt nào cả ──
        {
            var (temp, services, vm) = DungVm();
            using var db = temp;
            var email = ChonTaiKhoan(services, vm, "truycap@mail.com");
            LuoiChanMoTrinhDuyet(services);
            GiaVoDangChayLatCat(vm);

            vm.TruyCapTk(vm.Accounts.First());

            Assert.Empty(services.Sessions.Active);
            Assert.Contains(services.Log.Snapshot(email),
                e => e.Message.Contains(AccountsViewModel.CauTuChoiViLatCatDangChay));
        }
    }

    // ══════════ 3c. Khoá xuyên máy phải được NHẢ trên MỌI lối ra của khung ══════════

    /// <summary>
    /// Đếm số lần hook nhả khoá được gọi khi chạy khung <c>ChayLatCatAsync</c> với một <paramref name="than"/>
    /// giả. <b>Không phóng trình duyệt</b>: ctor <c>OrdersBridgeSession</c> chỉ dựng channel + flow, việc mở
    /// Brave nằm trong <c>StartBridgeAndLaunch</c> mà chỉ thân lệnh thật mới gọi tới.
    /// </summary>
    private static async Task<int> DemLuotNhaKhoaAsync(
        Func<OrdersBridgeSession, OrdersLoginParams, CancellationToken,
            Task<AccountsViewModel.KetLuanLatCat>> than)
    {
        var (temp, services, vm) = DungVm();
        using var db = temp;
        ChonTaiKhoan(services, vm, "khoa-vong-doi@mail.com");

        var soLanNha = 0;
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login => { soLanNha++; return Task.CompletedTask; };

        await vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", than);
        return soLanNha;
    }

    /// <summary>
    /// <b>Lưới hồi quy cho hạng mục nặng nhất: khoá phải được nhả trên MỌI lối ra.</b>
    /// <para>Lease KHÔNG tự hết hạn (heartbeat 60s giữ sống tới khi tắt app) nên rò một lượt là tài khoản đó bị
    /// máy khác bỏ qua vĩnh viễn, âm thầm. Ca THỨ NHẤT là chính lỗ hổng của bản trước: <c>than</c> TRẢ VỀ bình
    /// thường mang theo <c>Error</c>/<c>Captcha</c> — không ném, nên không khối <c>catch</c> nào chạy, và bản
    /// đặt <c>NhaKhoaXem()</c> trong <c>catch</c> rò khoá 100% ở đây.</para>
    /// </summary>
    [StaFact]
    public async Task NhaKhoaXuyenMay_DungMotLan_TrenCaBaLoiRa()
    {
        // Ca 1 — than TRẢ VỀ bình thường (gồm cả nhánh Error/Captcha, vốn trả bằng GIÁ TRỊ chứ không ném).
        Assert.Equal(1, await DemLuotNhaKhoaAsync((_, _, _) =>
            Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong"))));

        // Ca 2 — than NÉM.
        Assert.Equal(1, await DemLuotNhaKhoaAsync((_, _, _) =>
            Task.FromException<AccountsViewModel.KetLuanLatCat>(new InvalidOperationException("hỏng"))));

        // Ca 3 — than bị HUỶ (người dùng bấm "Đóng cửa sổ" giữa chừng).
        Assert.Equal(1, await DemLuotNhaKhoaAsync((_, _, ct) =>
            Task.FromCanceled<AccountsViewModel.KetLuanLatCat>(new CancellationToken(canceled: true))));
    }

    /// <summary>
    /// <b>Khung KHÔNG được huỷ chính lượt nó vừa mở.</b> Khi thân lệnh bắt đầu chạy, token phải còn SỐNG.
    /// <para>Lỗi thật (app 17:04 ngày 18/08/2026): sau khi đảo thứ tự thành "xin khoá trước, giết cửa sổ cũ
    /// sau", lời gọi dọn dẹp ở giữa lại là <c>TryKillPoc</c> — mà dòng đầu của nó là <c>_bridgeCts?.Cancel()</c>.
    /// Ở thứ tự cũ nó huỷ token của lượt TRƯỚC (vô hại); ở thứ tự mới nó huỷ đúng token vừa tạo. Nhật ký ghi
    /// "Đã hủy mở list shop." cùng giây với "Chờ extension nối cầu (ready)", Brave mở ra rồi đứng chết ở trang
    /// subaccount và thành cửa sổ mồ côi. <b>1839 test xanh không cái nào thấy</b> — vì không cái nào nhìn vào
    /// token mà thân lệnh THẬT SỰ nhận được.</para>
    /// </summary>
    [StaFact]
    public async Task KhungKhongTuHuy_ThanLenhNhanDuocTokenCONSONG()
    {
        var (temp, services, vm) = DungVm();
        using var db = temp;
        ChonTaiKhoan(services, vm, "token-song@mail.com");
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login => Task.CompletedTask;

        bool? daHuyNgayTuDau = null;
        await vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", (_, _, ct) =>
        {
            daHuyNgayTuDau = ct.IsCancellationRequested;
            return Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong"));
        });

        Assert.NotNull(daHuyNgayTuDau); // chặn test rỗng: thân lệnh phải THỰC SỰ được gọi
        Assert.False(daHuyNgayTuDau,
            "Khung đã huỷ token của chính lượt nó vừa mở — thân lệnh chưa chạy dòng nào đã bị Dừng.");
    }

    /// <summary>
    /// <b>Cờ <c>CoTheDongCuaSo</c> phải được BÁO ĐỔI</b>, không chỉ tính đúng.
    /// <para>Lỗi thật (đo bằng UI Automation trên app đang chạy, 18/08/2026): XAML bind
    /// <c>IsEnabled="{Binding CoTheDongCuaSo}"</c> nhưng không chỗ nào gọi
    /// <c>OnPropertyChanged(nameof(CoTheDongCuaSo))</c> ⇒ binding không bao giờ đọc lại, nút đứng nguyên
    /// <c>enabled=False</c> ở CẢ BA thời điểm (trước khi bấm, đang chạy, và sau khi xong lúc còn cửa sổ). Test
    /// XAML cũ chỉ khoá ĐƯỜNG BIND nên vẫn xanh — đây là test khoá NHỊP BÁO ĐỔI.</para>
    /// </summary>
    [StaFact]
    public async Task CoTheDongCuaSo_PhaiDuocBaoDoi_TrongMotLuotChayKhung()
    {
        var (temp, services, vm) = DungVm();
        using var db = temp;
        ChonTaiKhoan(services, vm, "bao-doi@mail.com");
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login => Task.CompletedTask;

        var daBan = new List<string>();
        vm.PropertyChanged += (_, e) => daBan.Add(e.PropertyName ?? "");

        // ĐO Ở THỜI ĐIỂM SỚM NHẤT có thể quan sát được TỪ NGOÀI: bên trong hook xin khoá.
        //
        // Vì sao không đo trong `than`, và cũng không đo sau khi khung xong — cả hai đều ĐO HỤT: `DongCuaSoCuLai`
        // (giữa khung) và hai khối `finally` đều bắn cờ, nên xoá đúng dòng `BaoCoDoiCuaSoXem()` ở ĐẦU khung —
        // dòng đã gây lỗi thật 17:04 — thì hai chỗ đo kia vẫn thấy cờ và test vẫn xanh.
        // Hook xin khoá chạy SAU `_bridgeRunning = true` nhưng TRƯỚC `DongCuaSoCuLai`, nên nó là khe duy nhất
        // phân biệt được. Khe này cũng chính là ca người dùng thật gặp: lượt gọi hub có thể mất tới 8 giây, mà
        // suốt 8 giây đó nút "Đóng cửa sổ" phải đã bấm được rồi.
        List<string>? banLucXinKhoa = null;
        bool? batLucXinKhoa = null;
        services.AcquireAccountLease = (login, ct) =>
        {
            banLucXinKhoa = new List<string>(daBan);
            batLucXinKhoa = vm.CoTheDongCuaSo;
            return Task.FromResult(new OrdersLeaseResult(true, null));
        };

        var batLucChay = false;
        await vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", (_, _, _) =>
        {
            batLucChay = vm.CoTheDongCuaSo;
            return Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong"));
        });

        Assert.NotNull(banLucXinKhoa); // chặn test rỗng: hook phải THỰC SỰ được gọi
        Assert.Contains(nameof(AccountsViewModel.CoTheDongCuaSo), banLucXinKhoa!);
        Assert.True(batLucXinKhoa,
            "Khung đã bật _bridgeRunning mà chưa báo CoTheDongCuaSo — suốt lượt xin khoá (tới 8s) nút 'Đóng "
            + "cửa sổ' vẫn tắt, người dùng không có đường huỷ.");
        Assert.True(batLucChay, "Đang chạy thân lệnh mà CoTheDongCuaSo vẫn false.");

        // Hai cờ anh em cũng phải được báo — cùng một hàm bắn cả ba, mất cái nào là nút kẹt cái đó.
        Assert.Contains(nameof(AccountsViewModel.CuaSoXemDangMo), daBan);
        Assert.Contains(nameof(AccountsViewModel.CanStop), daBan);
    }

    /// <summary>
    /// <b>Bấm "Đóng cửa sổ" GIỮA CHỪNG: khoá nhả ĐÚNG MỘT lần, và lượt MƯỢN KẾ phải CHỜ lượt nhả đó xong.</b>
    /// <para>Từ 18/08/2026 khoá được giữ tới lúc TRẢ HỒ SƠ (không nhả ở <c>finally</c> nữa), mà <c>TraHoSo</c>
    /// chạy đồng bộ trên UI thread nên lượt nhả là bất đồng bộ không await được tại chỗ. Bất biến thay thế:
    /// <c>MuonHoSoAsync</c> phải <c>await</c> chuỗi nhả trước khi xin khoá mới — nếu không, DELETE cũ hạ cánh
    /// sau lượt cấp mới và xoá đúng dòng vừa cấp (cùng <c>account_id</c> + <c>machine_id</c>), mà
    /// <c>HeartbeatLeases</c> là UPDATE thuần nên không hồi phục.</para>
    /// <para><b>Hook nhả khoá phải có độ trễ THẬT:</b> đường "bắn đi rồi quên" vẫn gọi hook đúng 1 lần nên ĐẾM
    /// không phân biệt được. Thứ phân biệt là lượt mượn kế có CHỜ hay không.</para>
    /// </summary>
    [StaFact]
    public async Task DongCuaSoGiuaChung_NhaKhoaMotLan_VaLuotMuonKeChoNhaXong()
    {
        var (temp, services, vm) = DungVm();
        using var db = temp;
        ChonTaiKhoan(services, vm, "dong-giua-chung@mail.com");

        var soLanNha = 0;
        var nhaXong = false;
        bool? nhaXongLucXinLai = null;
        services.AcquireAccountLease = (login, ct) =>
        {
            if (soLanNha > 0) { nhaXongLucXinLai = nhaXong; } // lượt xin THỨ HAI
            return Task.FromResult(new OrdersLeaseResult(true, null));
        };
        services.ReleaseAccountLease = async login =>
        {
            soLanNha++;
            await Task.Delay(200);   // mô phỏng lượt gọi hub qua mạng
            nhaXong = true;
        };

        await vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", (_, _, ct) =>
        {
            // Người dùng bấm "Đóng cửa sổ" giữa chừng — nút này bật được chính nhờ CoTheDongCuaSo.
            vm.DongCuaSoXemCommand.Execute(null);
            ct.ThrowIfCancellationRequested(); // TryKillPoc đã cancel token ⇒ thân lệnh thoát như thật
            return Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("không tới đây"));
        });

        Assert.Equal(1, soLanNha);

        // Lượt mượn KẾ: phải thấy lượt nhả trước đã xong hẳn.
        await vm.ChayLatCatAsync("Việc thử 2", "bắt đầu", "đang chạy", (_, _, _) =>
            Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong")));

        Assert.NotNull(nhaXongLucXinLai); // chặn test rỗng: phải THỰC SỰ có lượt xin thứ hai
        Assert.True(nhaXongLucXinLai,
            "Lượt mượn kế xin khoá trong lúc lượt nhả trước còn đang bay — DELETE cũ sẽ xoá đúng dòng vừa cấp.");
    }

    // ══════════ 3d. Tạm dừng check đơn để mượn hồ sơ, rồi chạy lại khi đóng cửa sổ ══════════

    /// <summary>
    /// Phiên GIẢ: chỉ đếm Start/Stop, không đụng Brave. Đủ để canh đường "dừng → mở → đóng → chạy lại".
    /// </summary>
    private sealed class PhienGia : IAccountSession
    {
        public long AccountId { get; }
        private SessionState _state = SessionState.Stopped;

        /// <summary>Phiên mất bao lâu mới dừng HẲN sau khi Stop — mô phỏng khối `finally` của phiên thật
        /// (nhả khoá xuyên máy → đóng Brave → Dispose kênh) chạy chậm. Zero = dừng tức thì.
        /// <para>Theo ĐỒNG HỒ chứ không theo số lần đọc: `State` bị đọc rải rác ở nhiều chỗ (manager, Active,
        /// khẳng định của test) nên đếm lượt đọc là một cái bẫy — bộ đếm cạn trước khi tới chỗ cần đo.</para></summary>
        public TimeSpan TreDung { get; set; } = TimeSpan.Zero;

        private DateTime _dungXongLuc = DateTime.MinValue;

        public SessionState State
        {
            get
            {
                if (_state == SessionState.Stopping && DateTime.UtcNow >= _dungXongLuc) { _state = SessionState.Stopped; }
                return _state;
            }
        }
        public string? StatusText => null;
        public int? ToShipCount => null;
        public bool ReadyForActions => false;
        public bool IsShopLoopRunning => false;

        /// <summary>Mốc vòng kế — test nhịp gói flash rót giá trị vào đây; mặc định <c>null</c> = "đang giữa
        /// một vòng check đơn", trạng thái mà nhịp flash phải TỪ CHỐI mở gói.</summary>
        public DateTime? ThoiDiemVongKe { get; set; }

        public string? LastError => null;
        public System.Diagnostics.Process? BraveProcess => null;

        public int StartCalls { get; private set; }
        public int StopCalls { get; private set; }

        public event Action? Changed;

        public PhienGia(long id) => AccountId = id;

        public Task StartAsync()
        {
            StartCalls++;
            _state = SessionState.Running;
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCalls++;
            _state = SessionState.Stopping;
            _dungXongLuc = DateTime.UtcNow + TreDung;
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        public void MarkQueued()
        {
            // Giống AccountSession: đang hoạt động thì KHÔNG hạ về hàng đợi.
            if (State is SessionState.Opening or SessionState.Running or SessionState.Stopping) { return; }
            _state = SessionState.Queued;
            Changed?.Invoke();
        }
        public Task<bool> ProcessOrdersAsync() => Task.FromResult(false);
        public Task<bool> CheckOrdersAsync() => Task.FromResult(false);
        public Task<bool> SyncOrdersAsync() => Task.FromResult(false);
        public Task<bool> RedownloadSlipAsync(string orderSn) => Task.FromResult(false);
    }

    /// <summary>
    /// Thay <c>Sessions</c> bằng manager dựng trên <see cref="PhienGia"/> — không có seam này thì mọi test đụng
    /// Start/Stop phiên đều phóng Brave thật.
    /// </summary>
    private static (Dictionary<long, PhienGia> Kho, Func<long, IAccountSession> Factory) KhoPhienGia()
    {
        var kho = new Dictionary<long, PhienGia>();
        return (kho, id =>
        {
            if (!kho.TryGetValue(id, out var p)) { kho[id] = p = new PhienGia(id); }
            return p;
        });
    }

    /// <summary>
    /// <b>Vòng đời mượn hồ sơ:</b> đang check đơn → bấm mở ⇒ phiên bị DỪNG đúng 1 lần; đóng cửa sổ ⇒ phiên
    /// được CHẠY LẠI đúng 1 lần, đúng tài khoản đó.
    /// <para>Đây là yêu cầu người dùng chốt 18/08/2026 ("không muốn phải login lại, khi nào chạy nút đó thì sẽ
    /// dừng việc check đơn lại") — thay hẳn luật cũ "có phiên chạy thì từ chối".</para>
    /// </summary>
    [StaFact]
    public async Task DangCheckDon_BamMo_ThiDungPhien_DongCuaSo_ThiChayLai()
    {
        var (kho, factory) = KhoPhienGia();
        var (temp, services, vm) = DungVm(factory);
        using var db = temp;
        var email = ChonTaiKhoan(services, vm, "muon-ho-so@mail.com");
        var id = services.Accounts.GetAll().Single(a => a.Email == email).Id;

        services.Sessions.Start(id); // phiên check đơn đang chạy
        var phien = kho[id];
        Assert.Equal(1, phien.StartCalls);

        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login => Task.CompletedTask;

        await vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", (_, _, _) =>
            Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong")));

        Assert.Equal(1, phien.StopCalls); // đã tạm dừng check đơn để mượn hồ sơ
        Assert.Contains(services.Log.Snapshot(email), e => e.Message.Contains("Tạm dừng check đơn"));

        // Không có cửa sổ thật nào được để lại (than giả), nên khung tự trả hồ sơ ngay ở finally.
        Assert.Equal(2, phien.StartCalls);
        Assert.Contains(services.Log.Snapshot(email), e => e.Message.Contains("chạy lại check đơn"));
    }

    /// <summary>
    /// Người dùng chủ động bấm "Dừng tất cả" trong lúc mượn hồ sơ ⇒ <b>KHÔNG</b> chạy lại check đơn.
    /// </summary>
    [StaFact]
    public async Task NguoiDungBamDungTatCa_ThiKhongChayLaiCheckDon()
    {
        var (kho, factory) = KhoPhienGia();
        var (temp, services, vm) = DungVm(factory);
        using var db = temp;
        var email = ChonTaiKhoan(services, vm, "dung-han@mail.com");
        var id = services.Accounts.GetAll().Single(a => a.Email == email).Id;

        services.Sessions.Start(id);
        var phien = kho[id];
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login => Task.CompletedTask;

        await vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", async (_, _, _) =>
        {
            await vm.StopAllCommand.ExecuteAsync(null); // người dùng dừng HẲN giữa chừng
            return AccountsViewModel.KetLuanLatCat.Chung("xong");
        });

        Assert.Equal(1, phien.StopCalls);
        Assert.Equal(1, phien.StartCalls); // vẫn là lượt Start ban đầu — KHÔNG có lượt chạy lại nào
        Assert.DoesNotContain(services.Log.Snapshot(email), e => e.Message.Contains("chạy lại check đơn"));
    }

    /// <summary>
    /// <b>THỨ TỰ BẮT BUỘC: dừng check đơn HẲN rồi mới xin khoá xuyên máy.</b>
    /// <para>Phiên check đơn giữ ĐÚNG khoá <c>orders:&lt;login&gt;</c> và chỉ nhả trong khối <c>finally</c> của
    /// nó. Xin khoá sớm thì hub thấy CÙNG <c>machine_id</c> nên cấp cho ta, rồi lượt nhả của phiên kia tới sau
    /// XOÁ ĐÚNG DÒNG VỪA CẤP — mà <c>HeartbeatLeases</c> là UPDATE thuần nên không hồi phục: ta chạy trong khi
    /// trên hub tài khoản đang TRỐNG, máy 2 nhảy vào. Đúng thứ người dùng yêu cầu phải chặn.</para>
    /// <para>Stub dừng CHẬM (<c>TreDung</c>) nên nếu khung không chờ, lượt xin khoá sẽ rơi vào lúc phiên
    /// còn <c>Stopping</c> — chính là điều test này bắt.</para>
    /// </summary>
    [StaFact]
    public async Task PhaiDungCheckDonHAN_RoiMoiXinKhoaXuyenMay()
    {
        var (kho, factory) = KhoPhienGia();
        var (temp, services, vm) = DungVm(factory);
        using var db = temp;
        var email = ChonTaiKhoan(services, vm, "thu-tu@mail.com");
        var id = services.Accounts.GetAll().Single(a => a.Email == email).Id;

        services.Sessions.Start(id);
        kho[id].TreDung = TimeSpan.FromSeconds(1); // finally của phiên thật chạy chậm

        bool? conChayLucXinKhoa = null;
        services.AcquireAccountLease = (login, ct) =>
        {
            conChayLucXinKhoa = services.Sessions.IsRunning(id);
            return Task.FromResult(new OrdersLeaseResult(true, null));
        };
        services.ReleaseAccountLease = login => Task.CompletedTask;

        await vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", (_, _, _) =>
            Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong")));

        Assert.NotNull(conChayLucXinKhoa); // chặn test rỗng: phải THỰC SỰ có lượt xin khoá
        Assert.False(conChayLucXinKhoa,
            "Khung xin khoá xuyên máy trong lúc phiên check đơn CHƯA dừng hẳn — lượt nhả của nó sẽ xoá đúng "
            + "lease vừa cấp cho ta, và máy 2 giành được tài khoản.");
    }

    /// <summary>
    /// Phiên check đơn KHÔNG chịu dừng trong hạn chờ ⇒ <b>không xin khoá, không mở gì</b>, báo lỗi rõ.
    /// </summary>
    [StaFact]
    public async Task CheckDonKhongChiuDung_ThiKhongXinKhoa_VaKhongMoGi()
    {
        using var hanNgan = new HanChoNgan(800); // khỏi chờ thật 30s
        var (kho, factory) = KhoPhienGia();
        var (temp, services, vm) = DungVm(factory);
        using var db = temp;
        var email = ChonTaiKhoan(services, vm, "khong-chiu-dung@mail.com");
        var id = services.Accounts.GetAll().Single(a => a.Email == email).Id;

        services.Sessions.Start(id);
        kho[id].TreDung = TimeSpan.FromHours(1); // không bao giờ dừng hẳn trong khuôn khổ test

        var daXinKhoa = false;
        services.AcquireAccountLease = (login, ct) =>
        {
            daXinKhoa = true;
            return Task.FromResult(new OrdersLeaseResult(true, null));
        };
        services.ReleaseAccountLease = login => Task.CompletedTask;

        var thanDaChay = false;
        await vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", (_, _, _) =>
        {
            thanDaChay = true;
            return Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong"));
        });

        Assert.False(daXinKhoa, "Chưa dừng được check đơn mà đã xin khoá xuyên máy.");
        Assert.False(thanDaChay, "Chưa dừng được check đơn mà đã chạy thân lệnh (mở trình duyệt).");
        Assert.Contains(services.Log.Snapshot(email), e => e.Message.Contains("chưa dừng hẳn"));
    }

    /// <summary>Rút hạn chờ xuống ngắn cho test đường hết-hạn, tự trả lại giá trị thật khi test xong.</summary>
    private sealed class HanChoNgan : IDisposable
    {
        private readonly TimeSpan _cu = AccountsViewModel.HanChoDungHan;
        public HanChoNgan(int ms) => AccountsViewModel.HanChoDungHan = TimeSpan.FromMilliseconds(ms);
        public void Dispose() => AccountsViewModel.HanChoDungHan = _cu;
    }

    /// <summary>Hạn chờ THẬT phải là 30s — lượt rút hạn của test không được lặng lẽ đổi giá trị production.</summary>
    [Fact]
    public void HanChoDungHan_MacDinh_La30Giay()
        => Assert.Equal(TimeSpan.FromSeconds(30), AccountsViewModel.HanChoDungHan);

    /// <summary>
    /// <b>Tài khoản KHÁC đang chạy cũng phải bị tạm dừng, và được chạy lại khi trả hồ sơ.</b>
    /// <para>Cổng 47821 và <c>FreeProfile(alsoMatchBridgeExtension: true)</c> đều TOÀN MÁY. Chỉ dừng tài khoản
    /// đang xem thì mở list shop cho B trong lúc A chạy sẽ trượt cổng, extension báo lỗi,
    /// <c>QuyetDinhSauThuBanSach</c> ra <c>DangNhapLai</c> và Playwright bắt ĐĂNG NHẬP LẠI — đúng thứ người dùng
    /// mở lời bằng câu "tôi không muốn phải login lại".</para>
    /// </summary>
    [StaFact]
    public async Task TaiKhoanKHAC_DangChay_CungBiTamDung_VaChayLai()
    {
        var (kho, factory) = KhoPhienGia();
        var (temp, services, vm) = DungVm(factory);
        using var db = temp;
        var emailA = ChonTaiKhoan(services, vm, "tk-a@mail.com");
        var emailB = ChonTaiKhoan(services, vm, "tk-b@mail.com");
        var idA = services.Accounts.GetAll().Single(a => a.Email == emailA).Id;
        var idB = services.Accounts.GetAll().Single(a => a.Email == emailB).Id;

        services.Sessions.Start(idA);                          // A đang chạy
        vm.SelectedRow = vm.Accounts.First(r => r.Id == idB);  // người dùng đang xem B
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login => Task.CompletedTask;

        await vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", (_, _, _) =>
            Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong")));

        Assert.Equal(1, kho[idA].StopCalls);   // A bị dừng dù người dùng đang xem B
        Assert.Equal(2, kho[idA].StartCalls);  // và được chạy lại khi trả hồ sơ
    }

    /// <summary>
    /// <b>Hàng đợi:</b> dừng A xong, manager bắn B (đang <c>Queued</c>) lên NGAY trong cùng lock. Vòng chờ phải
    /// bám <c>Active</c> RỖNG chứ không phải "A đã dừng chưa" — nếu không, hai bên cùng phóng trình duyệt và
    /// bên sau <c>FreeProfile</c> giết bên trước.
    /// </summary>
    [StaFact]
    public async Task HangDoi_PhaiChoToiKhiActiveRong()
    {
        var (kho, factory) = KhoPhienGia();
        var (temp, services, vm) = DungVm(factory);
        using var db = temp;
        var emailA = ChonTaiKhoan(services, vm, "hd-a@mail.com");
        var emailB = ChonTaiKhoan(services, vm, "hd-b@mail.com");
        var idA = services.Accounts.GetAll().Single(a => a.Email == emailA).Id;
        var idB = services.Accounts.GetAll().Single(a => a.Email == emailB).Id;

        services.Sessions.Start(idA);
        services.Sessions.Start(idB);
        Assert.Equal(2, services.Sessions.Active.Count); // chặn test rỗng
        // Dừng CHẬM — nếu không, Active về 0 tức thì và test không phân biệt được "có chờ" với "không chờ".
        kho[idA].TreDung = TimeSpan.FromSeconds(1);
        kho[idB].TreDung = TimeSpan.FromSeconds(1);

        int? soActiveLucXinKhoa = null;
        services.AcquireAccountLease = (login, ct) =>
        {
            soActiveLucXinKhoa = services.Sessions.Active.Count;
            return Task.FromResult(new OrdersLeaseResult(true, null));
        };
        services.ReleaseAccountLease = login => Task.CompletedTask;

        await vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", (_, _, _) =>
            Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong")));

        Assert.Equal(0, soActiveLucXinKhoa); // đã chờ tới khi KHÔNG còn phiên nào

        // CẢ HAI đều bị dừng (B đang Queued cũng tính) và CẢ HAI đều được đưa trở lại sau khi trả hồ sơ.
        Assert.Equal(1, kho[idA].StopCalls);
        Assert.Equal(1, kho[idB].StopCalls);
        Assert.Equal(2, services.Sessions.Active.Count);
    }

    /// <summary>
    /// <c>StopSelected</c> chỉ được coi là "người dùng dừng tay" khi tập tick GIAO với tập đang mượn.
    /// <para>Bật vô điều kiện (bản trước) nghĩa là: tick 0 dòng, hoặc tick một tài khoản chẳng liên quan, cũng
    /// làm lượt trả hồ sơ IM LẶNG bỏ bước chạy lại — check đơn chết mà không ai báo.</para>
    /// </summary>
    [StaFact]
    public async Task StopSelected_TickKhongLienQuan_ThiVanChayLaiCheckDon()
    {
        var (kho, factory) = KhoPhienGia();
        var (temp, services, vm) = DungVm(factory);
        using var db = temp;
        var emailA = ChonTaiKhoan(services, vm, "ss-a@mail.com");
        var emailB = ChonTaiKhoan(services, vm, "ss-b@mail.com");
        var idA = services.Accounts.GetAll().Single(a => a.Email == emailA).Id;

        services.Sessions.Start(idA);
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login => Task.CompletedTask;

        await vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", (_, _, _) =>
        {
            // Tick B (KHÔNG thuộc tập đang mượn) rồi bấm "Dừng đã chọn".
            vm.Accounts.First(r => r.Email == emailB).IsSelected = true;
            vm.StopSelectedCommand.Execute(null);
            return Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong"));
        });

        Assert.Equal(2, kho[idA].StartCalls); // A vẫn được chạy lại
    }

    /// <summary>Tick ĐÚNG tài khoản đang mượn thì tôn trọng ý người dùng: KHÔNG chạy lại.</summary>
    [StaFact]
    public async Task StopSelected_TickDungTaiKhoanDangMuon_ThiKhongChayLai()
    {
        var (kho, factory) = KhoPhienGia();
        var (temp, services, vm) = DungVm(factory);
        using var db = temp;
        var emailA = ChonTaiKhoan(services, vm, "ss2-a@mail.com");
        var idA = services.Accounts.GetAll().Single(a => a.Email == emailA).Id;

        services.Sessions.Start(idA);
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login => Task.CompletedTask;

        await vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", (_, _, _) =>
        {
            vm.Accounts.First(r => r.Id == idA).IsSelected = true;
            vm.StopSelectedCommand.Execute(null);
            return Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong"));
        });

        Assert.Equal(1, kho[idA].StartCalls); // vẫn là lượt Start ban đầu
    }

    /// <summary>Mượn lượt HAI (tài khoản khác) thì lượt một phải được trả ĐỦ: khoá cũ nhả, phiên đã dừng chạy lại.</summary>
    [StaFact]
    public async Task MuonLuotHai_ThiLuotMotDuocTraDu()
    {
        var (kho, factory) = KhoPhienGia();
        var (temp, services, vm) = DungVm(factory);
        using var db = temp;
        var emailA = ChonTaiKhoan(services, vm, "m2-a@mail.com");
        var emailB = ChonTaiKhoan(services, vm, "m2-b@mail.com");
        var idA = services.Accounts.GetAll().Single(a => a.Email == emailA).Id;

        services.Sessions.Start(idA);
        var soLanNha = 0;
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login => { soLanNha++; return Task.CompletedTask; };

        await vm.ChayLatCatAsync("Lượt 1", "bắt đầu", "đang chạy", (_, _, _) =>
            Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong")));

        vm.SelectedRow = vm.Accounts.First(r => r.Email == emailB);
        await vm.ChayLatCatAsync("Lượt 2", "bắt đầu", "đang chạy", (_, _, _) =>
            Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong")));

        Assert.Equal(2, soLanNha);             // khoá của CẢ HAI lượt đều được nhả
        Assert.True(kho[idA].StartCalls >= 2); // A không bị bỏ quên
    }

    /// <summary>
    /// <b>Khoá xuyên máy được GIỮ tới lúc trả hồ sơ</b>, không nhả ngay khi khung xong.
    /// <para>Nhả sớm (bản trước) là: cửa sổ còn mở hàng phút mà khoá đã trống từ giây thứ 5, máy 2 giành mất
    /// tài khoản, rồi người dùng đóng cửa sổ thì <c>Start</c> bị hub từ chối — phiên chết hẳn ngay dưới câu vừa
    /// hứa "đóng cửa sổ xong sẽ chạy lại".</para>
    /// </summary>
    [StaFact]
    public async Task KhoaXuyenMay_GiuToiLucTraHoSo()
    {
        var (temp, services, vm) = DungVm();
        using var db = temp;
        ChonTaiKhoan(services, vm, "giu-khoa@mail.com");

        var soLanNha = 0;
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login => { soLanNha++; return Task.CompletedTask; };

        // Thân lệnh để lại "cửa sổ" bằng cách... không có tiến trình thật, nên khung sẽ tự trả ở finally.
        // Ta chỉ cần chắc: TRONG lúc thân lệnh chạy, khoá VẪN đang giữ.
        int? nhaLucDangChay = null;
        await vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", (_, _, _) =>
        {
            nhaLucDangChay = soLanNha;
            return Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong"));
        });

        Assert.Equal(0, nhaLucDangChay); // đang chạy mà đã nhả là nhả quá sớm
        Assert.Equal(1, soLanNha);       // trả hồ sơ xong mới nhả, đúng một lần
    }

    /// <summary>
    /// Bấm "Đóng cửa sổ" GIỮA LÚC đang chờ phiên dừng hẳn ⇒ thoát NGAY, không đứng hết hạn chờ.
    /// <para>Không nghe token thì người dùng bấm huỷ ở giây thứ 3 vẫn phải ngồi đủ 30 giây rồi mới nhận một câu
    /// lỗi SAI nguyên nhân ("phiên check đơn chưa dừng hẳn") — trong khi thật ra chính họ vừa huỷ.</para>
    /// </summary>
    [StaFact]
    public async Task HuyGiuaLucChoDung_ThiThoatNgay_KhongDoiHetHan()
    {
        using var hanNgan = new HanChoNgan(10_000); // đủ dài để "thoát sớm" là bằng chứng thật
        var (kho, factory) = KhoPhienGia();
        var (temp, services, vm) = DungVm(factory);
        using var db = temp;
        var emailA = ChonTaiKhoan(services, vm, "huy-cho@mail.com");
        var idA = services.Accounts.GetAll().Single(a => a.Email == emailA).Id;

        services.Sessions.Start(idA);
        kho[idA].TreDung = TimeSpan.FromHours(1); // không bao giờ dừng hẳn trong khuôn khổ test
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login => Task.CompletedTask;

        var thanDaChay = false;
        var chay = vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", (_, _, _) =>
        {
            thanDaChay = true;
            return Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("không tới đây"));
        });

        await Task.Delay(300);
        vm.DongCuaSoXemCommand.Execute(null); // người dùng huỷ giữa lúc còn đang chờ

        var dongHo = System.Diagnostics.Stopwatch.StartNew();
        await chay;
        dongHo.Stop();

        Assert.True(dongHo.Elapsed < TimeSpan.FromSeconds(5),
            $"Huỷ rồi mà khung còn đứng chờ thêm {dongHo.Elapsed.TotalSeconds:0.0}s — vòng chờ không nghe token huỷ.");
        Assert.False(thanDaChay, "Đã huỷ mà thân lệnh vẫn chạy (vẫn đi mở trình duyệt).");
    }

    // ══════════ 3e. Cửa sổ CÒN SỐNG sau khi khung xong — hai luật chỉ lộ ra ở trạng thái này ══════════

    /// <summary>
    /// Một tiến trình rẻ tiền THẬT đóng vai "cửa sổ Brave để lại": <c>CuaSoXemDangMo</c> đọc
    /// <c>Process.HasExited</c> nên không giả bằng gì khác được.
    /// <para>Tự kill trong <c>Dispose</c> — test tuyệt đối không được để rác tiến trình trên máy người dùng.</para>
    /// </summary>
    private sealed class CuaSoGia : IDisposable
    {
        public System.Diagnostics.Process TienTrinh { get; }

        public CuaSoGia()
        {
            TienTrinh = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c ping -n 60 127.0.0.1",  // `timeout` thoát ngay khi không có console — `ping` thì không
                CreateNoWindow = true,
                UseShellExecute = false,
            })!;
        }

        public void Dispose()
        {
            try { if (!TienTrinh.HasExited) { TienTrinh.Kill(entireProcessTree: true); } }
            catch { /* đã chết rồi thì thôi */ }
            try { TienTrinh.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// <b>Khoá xuyên máy được GIỮ suốt thời gian cửa sổ còn mở, chỉ nhả khi TRẢ HỒ SƠ.</b>
    /// <para>Đây là ca mà mọi test trước không dựng nổi: cần một <c>Process</c> còn sống sau khi khung xong, nếu
    /// không thì <c>finally</c> tự gọi <c>TraHoSo</c> ngay và chính sách "nhả sớm" với "nhả khi trả" cho ra kết
    /// quả giống hệt nhau.</para>
    /// <para>Nhả sớm là: máy 2 tới chu kỳ giành mất tài khoản, rồi người dùng đóng cửa sổ thì <c>Start</c> bị hub
    /// từ chối — phiên chết hẳn ngay dưới câu vừa hứa "đóng cửa sổ xong sẽ chạy lại".</para>
    /// </summary>
    [StaFact]
    public async Task CuaSoConSong_ThiKhoaVanDuocGIU_ChiNhaKhiDongCuaSo()
    {
        using var cuaSo = new CuaSoGia();
        var (temp, services, vm) = DungVm();
        using var db = temp;
        ChonTaiKhoan(services, vm, "giu-khoa-that@mail.com");

        var soLanNha = 0;
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login => { soLanNha++; return Task.CompletedTask; };

        await vm.ChayLatCatAsync("Việc thử", "bắt đầu", "đang chạy", (phien, _, _) =>
        {
            phien.Process = cuaSo.TienTrinh; // lát cắt "để lại cửa sổ"
            return Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong"));
        });

        Assert.True(vm.CuaSoXemDangMo, "Cửa sổ giả phải còn sống — test sẽ rỗng nếu không.");
        Assert.Equal(0, soLanNha); // CÒN cửa sổ ⇒ CHƯA được nhả khoá

        vm.DongCuaSoXemCommand.Execute(null);

        Assert.False(vm.CuaSoXemDangMo);
        Assert.Equal(1, soLanNha); // đóng cửa sổ = trả hồ sơ ⇒ mới nhả
    }

    /// <summary>
    /// <b>Mượn lượt HAI phải nhả khoá của lượt MỘT TRƯỚC khi xin khoá mới</b> — và đúng theo THỨ TỰ đó.
    /// <para><c>_loginKhoaXem</c> chỉ giữ được MỘT login: xin khoá cho B trong lúc còn giữ khoá của A là lease
    /// của A rò lại vĩnh viễn (heartbeat giữ sống tới khi tắt app), máy 2 bỏ qua A mãi mãi mà không ai báo.</para>
    /// <para>Ca này cũng chỉ dựng được khi lượt một ĐỂ LẠI cửa sổ — nếu không, <c>finally</c> đã trả hồ sơ xong
    /// và nhánh nhả-khoá-cũ không bao giờ chạy.</para>
    /// </summary>
    [StaFact]
    public async Task MuonLuotHai_NhaKhoaCuTRUOC_RoiMoiXinKhoaMoi()
    {
        using var cuaSo = new CuaSoGia();
        var (temp, services, vm) = DungVm();
        using var db = temp;
        var emailA = ChonTaiKhoan(services, vm, "luot1-a@mail.com");
        var emailB = ChonTaiKhoan(services, vm, "luot2-b@mail.com");

        var nhatKy = new List<string>();
        services.AcquireAccountLease = (login, ct) => { nhatKy.Add("xin:" + login); return Task.FromResult(new OrdersLeaseResult(true, null)); };
        services.ReleaseAccountLease = login => { nhatKy.Add("nha:" + login); return Task.CompletedTask; };

        // Lượt 1 cho A — ĐỂ LẠI cửa sổ, nên khoá của A vẫn đang giữ.
        vm.SelectedRow = vm.Accounts.First(r => r.Email == emailA);
        await vm.ChayLatCatAsync("Lượt 1", "bắt đầu", "đang chạy", (phien, _, _) =>
        {
            phien.Process = cuaSo.TienTrinh;
            return Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong"));
        });
        Assert.Equal(new[] { "xin:" + emailA }, nhatKy); // chặn test rỗng: chưa nhả gì cả

        // Lượt 2 cho B.
        vm.SelectedRow = vm.Accounts.First(r => r.Email == emailB);
        await vm.ChayLatCatAsync("Lượt 2", "bắt đầu", "đang chạy", (_, _, _) =>
            Task.FromResult(AccountsViewModel.KetLuanLatCat.Chung("xong")));

        var iNhaA = nhatKy.IndexOf("nha:" + emailA);
        var iXinB = nhatKy.IndexOf("xin:" + emailB);
        Assert.True(iNhaA >= 0, $"Khoá của lượt MỘT không bao giờ được nhả — nhật ký: [{string.Join(", ", nhatKy)}]");
        Assert.True(iXinB >= 0, "Lượt hai chưa xin khoá — test rỗng.");
        Assert.True(iNhaA < iXinB,
            $"Xin khoá mới TRƯỚC khi nhả khoá cũ — nhật ký: [{string.Join(", ", nhatKy)}]");
    }

    /// <summary>
    /// <b>Phiên rơi LỖI: khoá xuyên máy phải được nhả TRƯỚC khi <c>SetError</c> đặt <c>State = Error</c>.</b>
    /// <para><c>SetError</c> đặt <c>State = Error</c> VÀ <c>_bridge = null</c> ngay lập tức, tức từ giây đó nhìn
    /// từ ngoài phiên đã "dừng hẳn" (<c>IsRunning == false &amp;&amp; BraveProcess == null</c>) trong khi khoá vẫn
    /// đang giữ. Nút "Mở list shop" chờ đúng hai tín hiệu đó rồi đi xin khoá: hub thấy CÙNG machine_id nên CẤP,
    /// xong lượt DELETE của phiên này (tới 8s) hạ cánh và xoá đúng dòng vừa cấp — <c>HeartbeatLeases</c> là
    /// UPDATE thuần nên không hồi phục.</para>
    /// <para><b>Cách ép lỗi mà KHÔNG cần trình duyệt:</b> đặt sẵn một FILE đúng chỗ thư mục hồ sơ, để
    /// <c>Directory.CreateDirectory</c> ném <c>IOException</c>. Điểm ném này nằm SAU <c>XinKhoaChayAsync</c>
    /// (khoá đã giữ) và TRƯỚC mọi thứ đụng Brave — đúng khe cần đo, và tất định.</para>
    /// </summary>
    [StaFact]
    public async Task PhienRoiLoi_NhaKhoaTRUOC_KhiDatStateError()
    {
        using var temp = new TempDatabase();
        var services = new AppServices(temp.Path);   // phiên THẬT, không stub
        services.Accounts.Insert(new Account { Email = "loi-som@mail.com", Password = "p" });
        var id = services.Accounts.GetAll().Single().Id;

        // Chặn Directory.CreateDirectory bằng một FILE cùng tên thư mục hồ sơ.
        var baseDir = System.IO.Path.GetDirectoryName(services.Database.Path)!;
        var duongHoSo = System.IO.Path.Combine(baseDir, "profiles", $"{id}-brave");
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(baseDir, "profiles"));
        System.IO.File.WriteAllText(duongHoSo, "chan-tao-thu-muc");

        SessionState? trangThaiLucNha = null;
        var daNha = false;
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
        services.ReleaseAccountLease = login =>
        {
            trangThaiLucNha = services.Sessions.Get(id)?.State;
            daNha = true;
            return Task.CompletedTask;
        };

        services.Sessions.Start(id);

        var han = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < han && !daNha)
        {
            await Task.Delay(50);
        }

        Assert.True(daNha, "Phiên lỗi mà KHÔNG nhả khoá — tài khoản kẹt tới khi tắt app.");
        Assert.NotNull(trangThaiLucNha);
        Assert.NotEqual(SessionState.Error, trangThaiLucNha);
    }

    // ══════════ 4. Nút còn trên XAML, bind đúng, nằm đúng tab ══════════

    /// <summary>
    /// Nút phải TỒN TẠI trong <c>AccountsView</c>, bind <c>MoListShopCommand</c> + <c>CanRun</c>, và nằm trong
    /// tab "Shops" (không phải tab "Chi tiết"). Không có test này thì một lượt dọn XAML gỡ mất nút mà cả bộ test
    /// vẫn xanh — lệnh VM đâu có biết nó còn lối vào hay không.
    /// </summary>
    [StaFact]
    public void NutMoListShop_ConTrenXaml_BindDung_NamTrongTabShops()
    {
        var (temp, _, vm) = DungVm();
        using var db = temp;

        var view = new AccountsView { DataContext = vm };
        var nut = view.MoListShopButton;
        Assert.NotNull(nut);

        Assert.Equal(nameof(AccountsViewModel.MoListShopCommand),
            LayDuongBind(nut, System.Windows.Controls.Primitives.ButtonBase.CommandProperty));
        Assert.Equal(nameof(AccountsViewModel.CanRun),
            LayDuongBind(nut, UIElement.IsEnabledProperty));

        Assert.Equal("Shops", NhanTabChua(nut));
    }

    /// <summary>
    /// Nút <b>"Đóng cửa sổ"</b> phải tồn tại cạnh "Mở list shop", bind <c>DongCuaSoXemCommand</c>, và hiện/ẩn
    /// theo <c>CuaSoXemDangMo</c>.
    /// <para>Nút này là lối đóng DUY NHẤT của cửa sổ để lại: <c>grep CanStop</c> trên toàn bộ <c>.xaml</c> ra 0
    /// chỗ bind, tức nút "■ Dừng" mà mọi câu chữ trước đây nhắc tới KHÔNG hề tồn tại (phản biện 18/08/2026).
    /// Gỡ nút này đi là người dùng chỉ còn "Dừng tất cả" (giết luôn mọi phiên khác) hoặc tắt app.</para>
    /// </summary>
    [StaFact]
    public void NutDongCuaSo_ConTrenXaml_BindDung_NamCanhNutMoListShop()
    {
        var (temp, _, vm) = DungVm();
        using var db = temp;

        var view = new AccountsView { DataContext = vm };
        var nut = view.DongCuaSoXemButton;
        Assert.NotNull(nut);

        Assert.Equal(nameof(AccountsViewModel.DongCuaSoXemCommand),
            LayDuongBind(nut, System.Windows.Controls.Primitives.ButtonBase.CommandProperty));

        // LUÔN HIỆN, chỉ mờ đi. Bind Visibility (bản đầu) là bẫy bấm nhầm: nút hiện ra đúng dưới con trỏ vừa
        // bấm "Mở list shop" và đẩy nút kia sang trái ~110px.
        Assert.Equal(nameof(AccountsViewModel.CoTheDongCuaSo),
            LayDuongBind(nut, UIElement.IsEnabledProperty));
        Assert.Null(LayDuongBind(nut, UIElement.VisibilityProperty));
        Assert.Equal(Visibility.Visible, nut.Visibility);

        // Cùng cha với "Mở list shop" ⇒ thật sự nằm cạnh nhau, không lạc sang khối khác của màn hình.
        Assert.Same(LogicalTreeHelper.GetParent(view.MoListShopButton), LogicalTreeHelper.GetParent(nut));
        Assert.Equal("Shops", NhanTabChua(nut));
    }

    /// <summary>Đường Binding đang gắn vào một thuộc tính (null = không phải Binding / không có).</summary>
    private static string? LayDuongBind(DependencyObject el, DependencyProperty dp)
        => BindingOperations.GetBinding(el, dp)?.Path?.Path;

    /// <summary>Chữ trên header của <see cref="TabItem"/> chứa phần tử này (leo cây logic; null = không ở tab nào).
    /// Header của tab Shops là một <c>StackPanel</c> gồm nhãn + huy hiệu số shop, nên phải lấy TextBlock đầu.</summary>
    private static string? NhanTabChua(DependencyObject el)
    {
        for (var cha = LogicalTreeHelper.GetParent(el); cha is not null; cha = LogicalTreeHelper.GetParent(cha))
        {
            if (cha is not TabItem tab) { continue; }
            return tab.Header switch
            {
                string s => s,
                DependencyObject header => LogicalTreeHelper.GetChildren(header)
                    .OfType<TextBlock>().FirstOrDefault()?.Text,
                _ => null,
            };
        }
        return null;
    }
}
