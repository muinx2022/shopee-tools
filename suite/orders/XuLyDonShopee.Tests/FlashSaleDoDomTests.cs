using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Lượt <b>DÒ DOM "Flash Sale Của Shop"</b> — phần kiểm được bằng test ở phía <b>C#</b>: chuỗi lệnh gửi xuống
/// extension, đường ghi file dump, và nút chẩn đoán trên XAML.
/// <para>Phần JS (luật ô ngày ĐỎ, luật chọn ngày, ràng buộc "KHÔNG TẠO GÌ") nằm ở
/// <c>Shopee.Core.Tests/FlashSaleDoDomTests</c> — ở đó có Jint chạy thật thân hàm production.</para>
/// </summary>
[Collection(TenCollection.BienMoiTruongDev)]
public class FlashSaleDoDomTests
{
    /// <summary>Bảng shop y khuôn extension gửi về (dùng lại khuôn của <c>KmDoDomTests</c>).</summary>
    private const string BangShopJson =
        """[{"rowKey":"111","name":"Shop Một","login":"shop.mot"}]""";

    /// <summary>Bản dump tối giản nhưng ĐÚNG KHUÔN <c>flow-flashsale.js</c> gửi về.</summary>
    private const string DumpJson = """
    {
      "luc": "2026-08-27T21:00:00.000Z",
      "luong": "flashsale",
      "anSo": { "lichThangNay": { "soNgayDo": 5, "ngayDo": [27, 28, 29, 30, 31] } },
      "buoc": [
        { "ten": "1. Vào Flash Sale Của Shop", "ok": true, "loi": null,
          "kyVong": "/shop-flash-sale", "docLai": "https://banhang.shopee.vn/portal/marketing/shop-flash-sale/list" },
        { "ten": "10. HỦY modal sản phẩm (KHÔNG tạo gì)", "ok": false,
          "loi": "không thấy nút Hủy hay nút đóng trong modal", "kyVong": "dong", "docLai": "con-mo" }
      ]
    }
    """;

    // ══════════ 1. Chuỗi lệnh phía C# ══════════

    /// <summary>
    /// Chuỗi lệnh của lát cắt dò Flash Sale phải ĐÚNG BẰNG <c>[readShopList, openShopDetail, dumpFlashSale]</c>.
    /// <para>Khoá cả hai đầu: không THIẾU (quên mở shop thì extension dò trên trang chọn shop, dump vô nghĩa) và
    /// không THỪA — <b>lệnh thứ tư là chỗ một thao tác tác động lên shop thật có thể lặng lẽ mọc ra</b>, đúng
    /// cách nút "Dò DOM KM" đã biến thành nút tạo chương trình thật.</para>
    /// </summary>
    [Fact]
    public async Task ChuoiLenhDoDomFlash_DungBang_ReadShopList_OpenShopDetail_DumpFlashSale()
    {
        await using var rig = await BridgeTestRig.StartAsync();
        var lenhDaGui = new List<string>();

        // Lái CHÍNH hàm production, không chép lại chuỗi lệnh: bản chép chỉ khoá được chính nó.
        var chay = OrdersBridgeSession.DoDomFlashSaleSauPickerAsync(rig.Channel, null, rig.Log, CancellationToken.None);

        using (var l = await rig.NhanLenhAsync()) { lenhDaGui.Add(l.RootElement.GetProperty("action").GetString() ?? ""); }
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BangShopJson });

        using (var l = await rig.NhanLenhAsync()) { lenhDaGui.Add(l.RootElement.GetProperty("action").GetString() ?? ""); }
        await rig.GuiAsync(new { action = "shopOpened" });

        using (var l = await rig.NhanLenhAsync()) { lenhDaGui.Add(l.RootElement.GetProperty("action").GetString() ?? ""); }
        await rig.GuiAsync(new { action = "pageData", kind = "flashDump", data = DumpJson });

        var kq = await chay;

        Assert.Equal(new[] { "readShopList", "openShopDetail", "dumpFlashSale" }, lenhDaGui);
        Assert.Equal("111", kq.ShopId);
        Assert.Null(kq.Error);
        Assert.False(kq.Captcha);
        Assert.NotNull(kq.DumpJson);
        Assert.Contains("flashsale", kq.DumpJson!);

        // Không còn lệnh nào bay tiếp — lượt dò KHÔNG được đụng gì thêm lên shop thật.
        Assert.Null(await ThuNhanThemLenhAsync(rig));
    }

    /// <summary>
    /// Gói <c>kmDump</c> (của lượt dò KM) KHÔNG được tính là kết quả của lượt dò Flash Sale.
    /// <para>Đây là lý do hai chặng tách riêng: gói của lượt trước tới muộn — chuyện thường gặp khi lượt trước
    /// vừa hết hạn chờ — mà lọt vào đây thì app ghi một bản dump của luồng KHÁC vào file <c>flash-dom-*.json</c>,
    /// rồi người đọc dump đi kết luận về một DOM chưa từng được dò.</para>
    /// </summary>
    [Fact]
    public async Task GoiKmDumpToiNhamLuot_KhongDuocTinhChoLuotFlash()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var chay = OrdersBridgeSession.DoDomFlashSaleSauPickerAsync(rig.Channel, null, rig.Log, CancellationToken.None);

        using (await rig.NhanLenhAsync()) { }
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BangShopJson });
        using (await rig.NhanLenhAsync()) { }
        await rig.GuiAsync(new { action = "shopOpened" });
        using (await rig.NhanLenhAsync()) { }

        // Gói LẠC LƯỢT: đúng khuôn, sai kind.
        await rig.GuiAsync(new { action = "pageData", kind = "kmDump", data = """{"buoc":[]}""" });
        Assert.False(chay.IsCompleted, "gói kmDump lạc lượt đã bị tính là kết quả của lượt dò Flash Sale");

        await rig.GuiAsync(new { action = "pageData", kind = "flashDump", data = DumpJson });
        var kq = await chay;
        Assert.Contains("flashsale", kq.DumpJson!);
    }

    /// <summary>
    /// <b>Lệnh <c>dumpFlashSale</c> phải mang theo SHOP ĐANG ĐO</b> (vị trí đếm từ 0, mã, tên).
    ///
    /// <para>Vì sao đáng có lưới: mọi kết luận của lượt dò đều phụ thuộc shop nào — "shop này toàn 0 doanh số"
    /// và "thứ tự sản phẩm đổi/không đổi" là câu về MỘT shop cụ thể. Không có trường này thì đọc lại một bản
    /// dump cũ là phải đoán, và đoán sai thì cả kết luận sai theo.</para>
    /// </summary>
    [Fact]
    public async Task LenhDumpFlashSale_MangTheoShopDangDo()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        // viTriShop = 1 ⇒ shop THỨ HAI trong bảng (chỉ số đếm từ 0).
        var chay = OrdersBridgeSession.DoDomFlashSaleSauPickerAsync(
            rig.Channel, null, rig.Log, CancellationToken.None, 1);

        using (await rig.NhanLenhAsync()) { }
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BangHaiShopJson });
        using (await rig.NhanLenhAsync()) { }
        await rig.GuiAsync(new { action = "shopOpened" });

        using (var l = await rig.NhanLenhAsync())
        {
            var g = l.RootElement;
            Assert.Equal("dumpFlashSale", g.GetProperty("action").GetString());
            var shop = g.GetProperty("shop");
            Assert.Equal(1, shop.GetProperty("viTri").GetInt32());
            Assert.Equal("222", shop.GetProperty("shopId").GetString());
            Assert.Equal("Shop Hai", shop.GetProperty("ten").GetString());
        }

        await rig.GuiAsync(new { action = "pageData", kind = "flashDump", data = DumpJson });
        var kq = await chay;
        Assert.Equal("222", kq.ShopId);
    }

    /// <summary>Bảng HAI shop — để chấm được phép chọn theo vị trí.</summary>
    private const string BangHaiShopJson =
        """[{"rowKey":"111","name":"Shop Một","login":"shop.mot"},""" +
        """{"rowKey":"222","name":"Shop Hai","login":"shop.hai"}]""";

    /// <summary>Chờ xem còn lệnh nào nữa không; hết giờ ⇒ null. Một lượt duy nhất — lượt hết hạn ABORT socket
    /// của rig (cùng khuôn <c>KmDoDomTests</c>).</summary>
    private static async Task<string?> ThuNhanThemLenhAsync(BridgeTestRig rig, int cho = 600)
    {
        try
        {
            using var l = await rig.NhanLenhAsync(TimeSpan.FromMilliseconds(cho));
            return l.RootElement.TryGetProperty("action", out var a) ? a.GetString() : l.RootElement.ToString();
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    // ══════════ 2. Ghi file dump ══════════

    private static (TempDatabase Temp, AppServices Services, AccountsViewModel Vm) DungVm()
    {
        var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        return (temp, services, new AccountsViewModel(services));
    }

    /// <summary>
    /// Tên file dump bám mốc thời gian tới GIÂY và có tiền tố RIÊNG <c>flash-dom-</c>.
    /// <para>Tiền tố riêng không phải chuyện thẩm mỹ: đọc dump là việc SO hai bản với nhau để thấy Shopee vừa
    /// đổi cái gì — so nhầm bản của luồng KM thì kết luận sai ngay từ dòng đầu.</para>
    /// </summary>
    [Fact]
    public void TenFileDumpFlash_TheoMocThoiGian_VaKhacTienToVoiKm()
    {
        var t = new DateTime(2026, 8, 27, 21, 4, 5, DateTimeKind.Local);

        Assert.Equal("flash-dom-20260827-210405.json", AccountsViewModel.TenFileDumpFlash(t));
        Assert.NotEqual(AccountsViewModel.TenFileDumpFlash(t), AccountsViewModel.TenFileDumpFlash(t.AddSeconds(1)));
        Assert.NotEqual(AccountsViewModel.TenFileDumpFlash(t), AccountsViewModel.TenFileDumpKm(t));
    }

    /// <summary>Dump được ghi ra FILE trong thư mục <c>logs</c> cạnh database, NGUYÊN VẸN nội dung.</summary>
    [StaFact]
    public void GhiFileDumpFlash_RaThuMucLogs_DungNoiDung()
    {
        var (temp, services, vm) = DungVm();
        using var db = temp;

        var luc = new DateTime(2026, 8, 27, 21, 4, 5, DateTimeKind.Local);
        var duong = vm.GhiFileDumpFlash(DumpJson, luc);

        Assert.NotNull(duong);
        Assert.True(File.Exists(duong!), $"Không thấy file dump ở {duong}");
        Assert.Equal(
            Path.Combine(Path.GetDirectoryName(services.Database.Path)!, "logs", "flash-dom-20260827-210405.json"),
            duong);
        Assert.Equal(DumpJson, File.ReadAllText(duong!));
    }

    /// <summary>Bộ tóm tắt dùng CHUNG với lượt dò KM — gói dump hai luồng cùng khuôn <c>buoc[]</c>.</summary>
    [Fact]
    public void TomTatDumpFlash_DungChungBoTomTatCuaKm()
    {
        var ds = AccountsViewModel.TomTatDumpKm(DumpJson);

        Assert.Equal(2, ds.Count);
        Assert.Contains("1. Vào Flash Sale Của Shop", ds[0]);
        Assert.Contains("KHÔNG ăn", ds[1]);
        Assert.Contains("10. HỦY modal sản phẩm", ds[1]);
        Assert.Contains("kỳ vọng 'dong'", ds[1]);
        Assert.Contains("đọc lại 'con-mo'", ds[1]);
    }

    // ══════════ 3. Nút chẩn đoán trên XAML ══════════

    private static string DocXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ShopeeSuite.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        var duong = Path.Combine(dir!.FullName, "orders", "XuLyDonShopee.App", "Views", "AccountsView.xaml");
        Assert.True(File.Exists(duong), "không thấy AccountsView.xaml ở " + duong);
        return File.ReadAllText(duong);
    }

    /// <summary>
    /// Nút <c>DoDomFlashButton</c> phải tồn tại, bind <c>DoDomFlashCommand</c> + <c>CanRun</c>, và nằm CÙNG
    /// hàng với nút dò KM.
    /// </summary>
    [StaFact]
    public void NutDoDomFlash_ConTrenXaml_BindDung()
    {
        var (temp, _, vm) = DungVm();
        using var db = temp;

        var view = new XuLyDonShopee.App.Views.AccountsView { DataContext = vm };
        var nut = view.DoDomFlashButton;
        Assert.NotNull(nut);

        Assert.Equal(nameof(AccountsViewModel.DoDomFlashCommand),
            System.Windows.Data.BindingOperations
                .GetBinding(nut, System.Windows.Controls.Primitives.ButtonBase.CommandProperty)?.Path?.Path);
        Assert.Equal(nameof(AccountsViewModel.CanRun),
            System.Windows.Data.BindingOperations
                .GetBinding(nut, System.Windows.UIElement.IsEnabledProperty)?.Path?.Path);

        // Cùng cha với nút dò KM ⇒ đúng hàng đầu tab Shops, cạnh nhau.
        Assert.Same(
            System.Windows.LogicalTreeHelper.GetParent(view.DoDomKmButton),
            System.Windows.LogicalTreeHelper.GetParent(nut));
    }

    /// <summary>
    /// <b>ĐO <c>Visibility</c> THẬT trên control THẬT, cả hai chiều biến môi trường.</b> Cùng lý do đã ghi ở
    /// <c>NutChanDoanAnTests</c>: test soi chuỗi XAML từng XANH trong khi nút HIỆN với mọi người, vì binding
    /// fail im lặng thì <c>Visibility</c> giữ mặc định <c>Visible</c>.
    /// </summary>
    [StaFact]
    public void Visibility_ThatCuaNutFlash_TheoDungBienMoiTruong()
    {
        Assert.Equal(System.Windows.Visibility.Collapsed, DungRoiDoVisibility(null));
        Assert.Equal(System.Windows.Visibility.Collapsed, DungRoiDoVisibility("0"));
        Assert.Equal(System.Windows.Visibility.Visible, DungRoiDoVisibility("1"));
    }

    /// <summary><c>Measure</c>+<c>Arrange</c>+<c>UpdateLayout</c> là bắt buộc — nút nằm trong một
    /// <c>TabItem</c>, chưa qua lượt layout thì binding còn <c>Unattached</c> và phép đo là RỖNG.</summary>
    private static System.Windows.Visibility DungRoiDoVisibility(string? giaTriBien)
    {
        var cu = Environment.GetEnvironmentVariable(AccountsViewModel.BienMoiTruongDev);
        try
        {
            Environment.SetEnvironmentVariable(AccountsViewModel.BienMoiTruongDev, giaTriBien);
            using var temp = new TempDatabase();
            var vm = new AccountsViewModel(new AppServices(temp.Path));
            var view = new XuLyDonShopee.App.Views.AccountsView { DataContext = vm };
            view.Measure(new System.Windows.Size(1600, 1000));
            view.Arrange(new System.Windows.Rect(0, 0, 1600, 1000));
            view.UpdateLayout();
            return view.DoDomFlashButton.Visibility;
        }
        finally
        {
            Environment.SetEnvironmentVariable(AccountsViewModel.BienMoiTruongDev, cu);
        }
    }

    /// <summary>
    /// Tooltip phải nói rõ lượt này <b>KHÔNG TẠO</b> gì — và không được sao chép câu cảnh báo "CÓ TẠO" của nút
    /// KM sang.
    /// <para>Hai nút nằm cạnh nhau, khác nhau đúng ở chỗ này. Tooltip sai ở đây tệ theo cả hai chiều: ghi "CÓ
    /// TẠO" thì người ta không dám bấm một công cụ chẩn đoán an toàn; ghi "không tạo" cho nút KM thì người ta
    /// bấm nhầm và để lại chương trình rác trên shop thật (đã xảy ra 27/08/2026).</para>
    /// </summary>
    [Fact]
    public void Xaml_ToolTipNutFlash_PhaiNoiRo_KHONG_TAO()
    {
        var xaml = DocXaml();
        var i = xaml.IndexOf("DoDomFlashButton", StringComparison.Ordinal);
        Assert.True(i >= 0, "nút chẩn đoán Flash đã bị XOÁ khỏi XAML");
        var ketThuc = xaml.IndexOf("</Button>", i, StringComparison.Ordinal);
        Assert.True(ketThuc > i);
        var the = xaml.Substring(i, ketThuc - i);

        Assert.Contains("Visibility=", the);
        Assert.Contains("HienNutDev", the);
        Assert.Contains("BoolToVis", the);
        Assert.Matches(new Regex("KHÔNG TẠO", RegexOptions.IgnoreCase), the);
        Assert.DoesNotContain("CÓ TẠO", the);
    }
}
