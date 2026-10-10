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
/// PHA 1 "KM của Shop" — lát cắt <b>DÒ DOM</b>. Phần kiểm được bằng test là <b>phía C#</b>: chuỗi lệnh gửi xuống
/// extension, và đường ghi file dump + tóm tắt nhật ký.
/// <para><b>Không test được phần DOM</b>: <c>BridgeTestRig</c> là một WebSocket giả đóng vai extension, nó không
/// có trang, không có Vue, không có <c>eds-date-picker</c>. Chính vì thế pha 1 mới TỒN TẠI — câu hỏi "DOM thuần
/// có điều khiển nổi form của Shopee không" chỉ trả lời được bằng cách chạy thật rồi đọc dump.</para>
/// </summary>
public class KmDoDomTests
{
    /// <summary>Bảng shop y khuôn extension gửi về (dùng lại khuôn của <c>MoListShopTests</c>).</summary>
    private const string BangShopJson =
        """[{"rowKey":"111","name":"Shop Một","login":"shop.mot"}]""";

    /// <summary>Bản dump tối giản nhưng ĐÚNG KHUÔN <c>flow-km.js</c> gửi về (có cả kyVong/docLai).</summary>
    private const string DumpJson = """
    {
      "luc": "2026-08-18T19:00:00.000Z",
      "buoc": [
        { "ten": "1. Vào Khuyến Mãi của Shop", "ok": true,  "loi": null,
          "kyVong": "discount", "docLai": "https://banhang.shopee.vn/portal/marketing/list/discount" },
        { "ten": "4. THÍ NGHIỆM B — picker BẮT ĐẦU", "ok": false,
          "loi": "ĐỌC LẠI KHÔNG KHỚP: kỳ vọng chứa '09:30', đọc được '20:30 18-08-2026'",
          "kyVong": "09:30", "docLai": "20:30 18-08-2026" }
      ]
    }
    """;

    // ══════════ 1. Chuỗi lệnh phía C# ══════════

    /// <summary>
    /// Chuỗi lệnh của lát cắt dò DOM phải ĐÚNG BẰNG <c>[readShopList, openShopDetail, dumpKm]</c>.
    /// <para>Khoá cả hai đầu: không THIẾU (quên mở shop thì extension dò trên trang chọn shop, dump vô nghĩa) và
    /// không THỪA (pha 1 tuyệt đối không được đụng thêm lệnh nào lên shop thật).</para>
    /// <para>Chỉ chạy được khúc SAU khi đã về picker — bước đăng nhập/SSO phóng trình duyệt thật nên nằm ngoài
    /// tầm rig; đây cũng đúng khúc mà lát cắt này thêm mới.</para>
    /// </summary>
    [Fact]
    public async Task ChuoiLenhDoDom_DungBang_ReadShopList_OpenShopDetail_DumpKm()
    {
        await using var rig = await BridgeTestRig.StartAsync();
        var lenhDaGui = new List<string>();

        // Lái CHÍNH hàm production, không chép lại chuỗi lệnh: bản chép chỉ khoá được chính nó.
        var chay = OrdersBridgeSession.DoDomKmSauPickerAsync(rig.Channel, null, rig.Log, CancellationToken.None);

        using (var l = await rig.NhanLenhAsync()) { lenhDaGui.Add(l.RootElement.GetProperty("action").GetString() ?? ""); }
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BangShopJson });

        using (var l = await rig.NhanLenhAsync()) { lenhDaGui.Add(l.RootElement.GetProperty("action").GetString() ?? ""); }
        await rig.GuiAsync(new { action = "shopOpened" });

        using (var l = await rig.NhanLenhAsync()) { lenhDaGui.Add(l.RootElement.GetProperty("action").GetString() ?? ""); }
        await rig.GuiAsync(new { action = "pageData", kind = "kmDump", data = DumpJson });

        var kq = await chay;

        Assert.Equal(new[] { "readShopList", "openShopDetail", "dumpKm" }, lenhDaGui);
        Assert.Equal("111", kq.ShopId);
        Assert.Null(kq.Error);
        Assert.False(kq.Captcha);
        Assert.NotNull(kq.DumpJson);
        Assert.Contains("THÍ NGHIỆM B", kq.DumpJson!);

        // Không còn lệnh nào bay tiếp — pha 1 KHÔNG được đụng gì thêm lên shop thật.
        Assert.Null(await ThuNhanThemLenhAsync(rig));
    }

    /// <summary>Chờ xem còn lệnh nào nữa không; hết giờ ⇒ null. Một lượt duy nhất — lượt hết hạn ABORT socket
    /// của rig (xem ghi chú cùng khuôn ở <c>MoListShopTests</c>).</summary>
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

    // ══════════ 2. Ghi file dump + tóm tắt nhật ký ══════════

    private static (TempDatabase Temp, AppServices Services, AccountsViewModel Vm) DungVm()
    {
        var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        return (temp, services, new AccountsViewModel(services));
    }

    /// <summary>Tên file dump bám mốc thời gian tới GIÂY — hai lượt dò cách nhau vài phút không được đè nhau.</summary>
    [Fact]
    public void TenFileDumpKm_TheoMocThoiGian()
    {
        var t = new DateTime(2026, 8, 18, 19, 4, 5, DateTimeKind.Local);
        Assert.Equal("km-dom-20260818-190405.json", AccountsViewModel.TenFileDumpKm(t));

        // Khác giây ⇒ khác tên (không đè bản dump trước).
        Assert.NotEqual(AccountsViewModel.TenFileDumpKm(t), AccountsViewModel.TenFileDumpKm(t.AddSeconds(1)));
    }

    /// <summary>
    /// Dump được ghi ra FILE trong thư mục <c>logs</c> cạnh database, NGUYÊN VẸN nội dung.
    /// <para>Phải là file chứ không phải panel nhật ký: panel giữ tối đa 200 dòng mỗi nguồn, đổ bản dump vài
    /// chục KB vào đó là mất sạch phần đầu — đúng thứ cần đọc nhất.</para>
    /// </summary>
    [StaFact]
    public void GhiFileDumpKm_RaThuMucLogs_DungNoiDung()
    {
        var (temp, services, vm) = DungVm();
        using var db = temp;

        var luc = new DateTime(2026, 8, 18, 19, 4, 5, DateTimeKind.Local);
        var duong = vm.GhiFileDumpKm(DumpJson, luc);

        Assert.NotNull(duong);
        Assert.True(File.Exists(duong!), $"Không thấy file dump ở {duong}");
        Assert.Equal(
            Path.Combine(Path.GetDirectoryName(services.Database.Path)!, "logs", "km-dom-20260818-190405.json"),
            duong);
        Assert.Equal(DumpJson, File.ReadAllText(duong!));
    }

    /// <summary>
    /// Tóm tắt MỘT DÒNG MỖI BƯỚC, nói rõ <c>ok</c> hay <c>KHÔNG ăn</c> và kèm lý do.
    /// <para>Đây là thứ người đọc nhật ký nhìn thấy — kết quả ÂM TÍNH ("KHÔNG ăn") phải hiện ra chứ không được
    /// chìm vào file, vì chính nó là câu trả lời của pha 1.</para>
    /// </summary>
    [Fact]
    public void TomTatDumpKm_MoiBuocMotDong_NoiRoKhongAn()
    {
        var ds = AccountsViewModel.TomTatDumpKm(DumpJson);

        Assert.Equal(2, ds.Count);
        Assert.Contains("ok", ds[0]);
        Assert.Contains("1. Vào Khuyến Mãi của Shop", ds[0]);
        Assert.Contains("KHÔNG ăn", ds[1]);
        Assert.Contains("ĐỌC LẠI KHÔNG KHỚP", ds[1]);
    }
    /// <summary>
    /// <b>Bằng chứng ĐỌC LẠI phải hiện trên nhật ký, không chìm trong file.</b>
    /// <para>Lượt chạy thật 20:24 ngày 18/08/2026 báo <c>ok</c> cho cả hai thí nghiệm picker trong khi ô thời
    /// gian vẫn nguyên <c>20:30 18-08-2026</c> — cờ <c>ok</c> lúc đó chỉ nghĩa là "click không ném", tức một
    /// PROOF ĐO RỖNG, và nó suýt làm báo cáo sai lên người dùng.</para>
    /// <para>Nay mỗi bước có khai kỳ vọng thì dòng tóm tắt phải in ra <b>kỳ vọng gì</b> và <b>đọc lại được gì</b>
    /// — người đọc tự chấm được, không phải tin một chữ "ok".</para>
    /// </summary>
    [Fact]
    public void TomTatDumpKm_InRaBangChungDocLai_KhongChiTinChuOk()
    {
        var ds = AccountsViewModel.TomTatDumpKm(DumpJson);

        // Bước hỏng: phải thấy CẢ kỳ vọng LẪN giá trị đọc lại được, ngay trên dòng nhật ký.
        Assert.Contains("kỳ vọng '09:30'", ds[1]);
        Assert.Contains("đọc lại '20:30 18-08-2026'", ds[1]);

        // Bước chạy được cũng in bằng chứng — để dòng "ok" cũng kiểm được, không phải tin suông.
        Assert.Contains("kỳ vọng 'discount'", ds[0]);
        Assert.Contains("/portal/marketing/list/discount", ds[0]);
    }

    /// <summary>Bước CHỈ ĐỌC (không khai kỳ vọng) thì không bịa ra cụm bằng chứng rỗng.</summary>
    [Fact]
    public void TomTatDumpKm_BuocChiDoc_KhongInCumBangChung()
    {
        const string json = """
        { "buoc": [ { "ten": "7. Dump selector", "ok": true, "loi": null } ] }
        """;

        var ds = AccountsViewModel.TomTatDumpKm(json);

        Assert.Single(ds);
        Assert.Contains("7. Dump selector", ds[0]);
        Assert.DoesNotContain("kỳ vọng", ds[0]);
    }


    /// <summary>JSON rỗng/hỏng ⇒ danh sách rỗng, KHÔNG ném: đường chẩn đoán hỏng thì báo, đừng đổ cả lượt chạy.</summary>
    [Fact]
    public void TomTatDumpKm_JsonHongHoacRong_TraRong_KhongNem()
    {
        Assert.Empty(AccountsViewModel.TomTatDumpKm(null));
        Assert.Empty(AccountsViewModel.TomTatDumpKm(""));
        Assert.Empty(AccountsViewModel.TomTatDumpKm("{ khong-phai-json"));
        Assert.Empty(AccountsViewModel.TomTatDumpKm("""{"khong-co-buoc": 1}"""));
    }

    // ══════════ 3. Nút TẠM có trên XAML và bind đúng ══════════

    /// <summary>
    /// Nút tạm <c>DoDomKmButton</c> phải tồn tại, bind <c>DoDomKmCommand</c> + <c>CanRun</c>, và nằm trong tab
    /// "Shops" cạnh hai nút kia.
    /// <para>Test này cũng là cái NHẮC gỡ nút ở pha 3: gỡ nút mà quên gỡ test thì test đỏ ngay, không có chuyện
    /// nút chẩn đoán ở lại vĩnh viễn trong bản phát hành.</para>
    /// </summary>
    [StaFact]
    public void NutDoDomKm_ConTrenXaml_BindDung()
    {
        var (temp, _, vm) = DungVm();
        using var db = temp;

        var view = new XuLyDonShopee.App.Views.AccountsView { DataContext = vm };
        var nut = view.DoDomKmButton;
        Assert.NotNull(nut);

        Assert.Equal(nameof(AccountsViewModel.DoDomKmCommand),
            System.Windows.Data.BindingOperations
                .GetBinding(nut, System.Windows.Controls.Primitives.ButtonBase.CommandProperty)?.Path?.Path);
        Assert.Equal(nameof(AccountsViewModel.CanRun),
            System.Windows.Data.BindingOperations
                .GetBinding(nut, System.Windows.UIElement.IsEnabledProperty)?.Path?.Path);

        // Cùng cha với "Mở list shop" ⇒ đúng hàng đầu tab Shops.
        Assert.Same(
            System.Windows.LogicalTreeHelper.GetParent(view.MoListShopButton),
            System.Windows.LogicalTreeHelper.GetParent(nut));
    }
}
