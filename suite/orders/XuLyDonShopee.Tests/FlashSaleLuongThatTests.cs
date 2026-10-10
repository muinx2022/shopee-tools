using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>Vòng "Flash Sale Của Shop" chạy trên CẦU NỐI THẬT</b> (<see cref="BridgeTestRig"/> đóng vai extension) +
/// các LUẬT LỊCH của khung <b>cả ngày <c>[0,23)</c>, hạn shop mới 22:15</b> (đổi 01/09/2026 từ 21:00–23:45).
///
/// <para>Cùng khuôn và cùng lý do với <c>ComboVongRigTests</c>: chuỗi lệnh gửi xuống extension là thứ quyết
/// định trình duyệt sắp làm gì trên shop THẬT, và một đột biến ở đó (đổi <c>action</c>, gửi nhầm gói của vòng
/// kia) không làm đỏ một test hàm-thuần nào.</para>
///
/// <para><b>Bốn mệnh đề nặng nhất của file này</b>, theo đúng thứ tự nguy hiểm:</para>
/// <list type="number">
/// <item><b>NGÀY ĐÍCH tính MỘT LẦN</b> — đồng hồ giả nhảy qua nửa đêm giữa hai shop mà mọi shop vẫn nhắm CÙNG
/// một ngày;</item>
/// <item><b>không bắt đầu shop mới sau 22:15</b>;</item>
/// <item><b>một shop "không còn đỏ" KHÔNG được giết cả lượt</b> — kết luận mức ngày cần MỌI shop đồng thuận;</item>
/// <item><b>KHÔNG lưu trạng thái nào xuống DB</b> — grep chứng minh.</item>
/// </list>
/// </summary>
public class FlashSaleLuongThatTests
{
    /// <summary>Gói mặc định: ngày đích 30-08-2026 (lượt chạy đêm 29/08) — không có trần khung.
    /// <para><b>%giảm 6 và SL 12 — CỐ Ý KHÁC mặc định 5/10</b> (03/09/2026): hai con số này đến từ cấu hình người
    /// dùng, nên nếu bài test dùng đúng mặc định thì một bản vá bỏ quên chúng ở <c>Goi()</c> (hoặc dựng lại từ
    /// một hằng nào đó) vẫn xanh — đúng hình dạng của lỗi vừa vá.</para></summary>
    private static readonly ThamSoFlashSale ThamSo =
        new(new DateTime(2026, 8, 30), PhanTramGiam: 6, SoLuongThamGia: 12);

    /// <summary>Mốc "bây giờ" mặc định của vòng: 21:30 ngày 29/08 — trong khung, trước hạn 22:15.</summary>
    private static readonly DateTime TrongKhung = new(2026, 8, 29, 21, 30, 0);

    private const string MotShop = """[{"rowKey":"111","name":"Shop Một","login":"shop.mot"}]""";

    private const string BaShop =
        """[{"rowKey":"111","name":"Shop Một","login":"shop.mot"},""" +
        """{"rowKey":"222","name":"Shop Hai","login":"shop.hai"},""" +
        """{"rowKey":"333","name":"Shop Ba","login":"shop.ba"}]""";

    private static string GoiTao(int soSp) =>
        $$"""{"soSanPham":{{soSp}},"daTao":true,"boQua":null,"canhBao":[],"buocHong":[],"ngayMaiKhongDo":false}""";

    /// <summary>Gói "ngày đích KHÔNG phải ngày đỏ" — Shopee chưa mở khung Flash Sale cho ngày mai.</summary>
    private const string GoiKhongDo =
        """{"soSanPham":0,"daTao":false,"boQua":"ngày đích 30-08-2026 KHÔNG phải ngày đỏ","canhBao":[],"buocHong":[],"ngayMaiKhongDo":true}""";

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 1. CHUỖI LỆNH + GÓI THAM SỐ
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Chuỗi lệnh ĐÚNG BẰNG <c>[readShopList, openShopDetail, <b>chayFlashSale</b>]</c>, <c>token</c> =
    /// <c>shopId</c>, và gói <c>thamSo</c> mang ĐỦ ba trường của <see cref="ThamSoFlashSale.Goi"/>.
    /// <para>Đo TỪNG TRƯỜNG: thiếu <c>thang</c>/<c>nam</c> là extension click trúng ngày 30 của tháng đang hiện
    /// trên picker — một tháng có thể KHÔNG phải tháng đích ở đêm cuối tháng.</para>
    /// </summary>
    [Fact]
    public async Task ChuoiLenh_DungBang_VaGoiThamSoDuTungTruong()
    {
        await using var rig = await BridgeTestRig.StartAsync();
        var lenh = new List<string>();

        var chay = ChayVongAsync(rig);

        lenh.Add(await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = MotShop });

        lenh.Add(await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "shopOpened" });

        using (var l = await rig.NhanLenhAsync())
        {
            var root = l.RootElement;
            lenh.Add(root.GetProperty("action").GetString() ?? "");
            Assert.Equal("111", root.GetProperty("token").GetString());

            var ts = root.GetProperty("thamSo");
            Assert.Equal(30, ts.GetProperty("ngay").GetInt32());
            Assert.Equal(8, ts.GetProperty("thang").GetInt32());
            Assert.Equal(2026, ts.GetProperty("nam").GetInt32());

            // HAI THÔNG SỐ BULK NHẬP TỪ FORM (03/09/2026) — thay hai hằng đã xoá khỏi extension. Đo đúng 6/12
            // của `ThamSo`, KHÔNG phải 5/10: thiếu hai trường này (hoặc gửi một hằng) là extension dừng ở bước
            // giả "0. Tham số" — hoặc tệ hơn, một bản extension cũ áp 5% cho một shop người dùng đã đặt 6%.
            Assert.Equal(6, ts.GetProperty("phanTramGiam").GetInt32());
            Assert.Equal(12, ts.GetProperty("soLuongThamGia").GetInt32());

            // TRẦN KHUNG ĐÃ GỠ (03/09/2026, người dùng chốt "bỏ trần 8 khung, lấy hết khung Shopee mở"): gói
            // KHÔNG được mang lại trường này, kẻo extension đọc nó thành trần và dựng lại đúng cái vừa bỏ.
            Assert.False(ts.TryGetProperty("soKhungToiDa", out _));

            // …và KHÔNG mang trường của hai vòng kia: gửi nhầm gói là extension đọc tham số rác.
            Assert.False(ts.TryGetProperty("phanTram", out _));
            Assert.False(ts.TryGetProperty("bd", out _));
        }
        await rig.GuiAsync(new { action = "pageData", kind = "flashKetQua", token = "111", data = GoiTao(80) });

        var kq = await chay;

        Assert.Equal(new[] { "readShopList", "openShopDetail", "chayFlashSale" }, lenh);
        Assert.Single(kq.Shops);
        Assert.True(kq.Shops[0].DaTao);
        Assert.Equal(80, kq.Shops[0].SoSanPham);
        Assert.False(kq.NgayMaiKhongDo);
        Assert.Null(await ThuNhanThemLenhAsync(rig));
    }

    /// <summary>
    /// <b>Gói <c>kmKetQua</c> (của vòng KM/Combo) KHÔNG được tính là kết quả của lượt FLASH.</b>
    /// <para>Đây là lý do <c>ArmFlashChay</c> tách chặng riêng. Ba luồng cùng ghi vào một ô lưới
    /// <c>km_program_daily</c>, nên một gói lạc ở đây là một con số THẬT của luồng khác nằm nhầm ô — không lỗi,
    /// không log, và người đọc lưới tin rằng Flash Sale đã chạy.</para>
    /// </summary>
    [Fact]
    public async Task GoiKmKetQuaToiNhamLuot_KhongDuocTinhChoLuotFlash()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var chay = ChayVongAsync(rig);
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = MotShop });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);

        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiTao(240) });
        Assert.False(chay.IsCompleted, "gói kmKetQua lạc luồng đã bị tính là kết quả của lượt Flash Sale");

        await rig.GuiAsync(new { action = "pageData", kind = "flashKetQua", token = "111", data = GoiTao(80) });
        var kq = await chay;
        Assert.Equal(80, kq.Shops[0].SoSanPham);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2. NGÀY ĐÍCH TÍNH MỘT LẦN — ca giết bản "đọc lại DateTime.Now mỗi shop"
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Đồng hồ giả NHẢY QUA NỬA ĐÊM giữa shop 1 và shop 2 ⇒ cả ba shop vẫn nhắm CÙNG một ngày.</b>
    ///
    /// <para>Đây là ca hỏng đắt nhất của cả luồng và nó KHÔNG để lại một dòng log nào: shop chạy trước nửa đêm
    /// nhắm <c>D+1</c>, shop chạy sau nhắm <c>D+2</c> ⇒ một lượt tạo flash sale cho HAI ngày khác nhau, còn
    /// ngày <c>D+1</c> thì mấy shop cuối không có gì.</para>
    ///
    /// <para><b>Vì sao đo ở đây chứ không ở <c>KmLichTuDong.NgayDichFlash</c>:</b> hàm thuần đó luôn đúng — thứ
    /// có thể sai là VÒNG LẶP đọc lại đồng hồ. Test phải soi cái gói THẬT SỰ đi xuống extension ở từng shop.</para>
    /// </summary>
    [Fact]
    public async Task NgayDich_TinhMotLan_DongHoNhayQuaNuaDem_MoiShopVanCungMotNgay()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        // Đồng hồ: shop 1 lúc 23:40 ngày 29/08 → shop 2 và 3 lúc 00:05 ngày 30/08. Lượt này truyền
        // `hanChot: null` nên KHÔNG có gì tự dừng vòng ở nửa đêm — cửa duy nhất còn lại là ngày đích đã đóng
        // băng, đúng thứ bài test đo. (Đường production nay có hạn 22:15 nên ca vắt-qua-nửa-đêm không dựng nổi
        // trên đó nữa; giữ ở đây vì hàm thuần phải đúng bất kể ai gọi nó thế nào.)
        var moc = new[]
        {
            new DateTime(2026, 8, 29, 23, 40, 0),
            new DateTime(2026, 8, 30, 0, 5, 0),
            new DateTime(2026, 8, 30, 0, 20, 0),
        };
        var lan = 0;
        var chay = ChayVongAsync(rig, hanChot: null, dongHo: () => moc[Math.Min(lan, moc.Length - 1)]);

        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });

        var ngayGui = new List<string>();
        foreach (var shopId in new[] { "111", "222", "333" })
        {
            await NhanTenLenhAsync(rig);                       // openShopDetail
            await rig.GuiAsync(new { action = "shopOpened" });
            using (var l = await rig.NhanLenhAsync())
            {
                var ts = l.RootElement.GetProperty("thamSo");
                ngayGui.Add($"{ts.GetProperty("ngay").GetInt32()}/{ts.GetProperty("thang").GetInt32()}"
                            + $"/{ts.GetProperty("nam").GetInt32()}");
            }
            await rig.GuiAsync(new { action = "pageData", kind = "flashKetQua", token = shopId, data = GoiTao(80) });
            lan++;
        }

        var kq = await chay;

        Assert.Equal(new[] { "30/8/2026", "30/8/2026", "30/8/2026" }, ngayGui);
        Assert.Equal(3, kq.SoTao);
    }

    /// <summary>Hàm thuần: ngày đích = ngày bắt đầu lượt + 1, kể cả khi rơi sang THÁNG SAU (đêm cuối tháng).</summary>
    [Theory]
    [InlineData(2026, 8, 29, 21, 2026, 8, 30)]
    [InlineData(2026, 8, 31, 23, 2026, 9, 1)]     // đêm cuối tháng ⇒ ngày mai thuộc THÁNG SAU
    [InlineData(2026, 12, 31, 21, 2027, 1, 1)]    // đêm cuối NĂM
    public void NgayDichFlash_LaNgayBatDauLuotCongMot(
        int nam, int thang, int ngay, int gio, int namMong, int thangMong, int ngayMong)
    {
        Assert.Equal(new DateTime(namMong, thangMong, ngayMong),
            KmLichTuDong.NgayDichFlash(new DateTime(nam, thang, ngay, gio, 30, 0)));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 3. KHÔNG BẮT ĐẦU SHOP MỚI SAU 22:15
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Hạn chót của lượt Flash Sale là <b>22:15</b>, KHÔNG phải giờ đóng khung (23:00).
    /// <para>Hạ từ 23:45 ngày 01/09/2026: đo thật đêm 31/08 cho thấy Shopee đóng đăng ký cho <c>D+1</c> quanh
    /// ~22:50 (lượt 23:20 mở ra thì lịch đã hết đỏ), nên nửa sau của một shop bắt đầu lúc 22:3x rơi vào vùng
    /// chết.</para></summary>
    [Fact]
    public void HanChotFlash_La2215_KhongPhaiGioDongKhung()
    {
        var han = KmLichTuDong.HanChotLuotTuDong(new DateTime(2026, 8, 29, 21, 0, 0), LoaiChuongTrinh.FlashSale);
        Assert.Equal(new DateTime(2026, 8, 29, 22, 15, 0), han);
        Assert.NotEqual(new DateTime(2026, 8, 29).AddHours(KmLichTuDong.Khung(LoaiChuongTrinh.FlashSale).Dong), han);

        Assert.True(KmLichTuDong.ConDuocBatDauShopMoi(new DateTime(2026, 8, 29, 22, 14, 59), han));
        Assert.False(KmLichTuDong.ConDuocBatDauShopMoi(new DateTime(2026, 8, 29, 22, 15, 0), han));
    }

    /// <summary>
    /// <b>Vòng KHÔNG mở shop mới sau hạn</b> — shop 1 chạy xong lúc 22:10, đồng hồ nhích sang 22:16 thì shop
    /// 2 và 3 KHÔNG được mở, và câu tổng kết phải nói còn mấy shop chưa chạy.
    /// <para>Đo trên VÒNG THẬT chứ không chỉ trên <c>ConDuocBatDauShopMoi</c>: hàm thuần đúng mà vòng quên gọi
    /// thì vẫn chạy vào vùng Shopee đã đóng đăng ký.</para>
    /// </summary>
    [Fact]
    public async Task Vong_KhongBatDauShopMoiSau2215()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var han = new DateTime(2026, 8, 29, 22, 15, 0);
        var xongShopMot = false;
        var chay = ChayVongAsync(rig, hanChot: han,
            dongHo: () => xongShopMot ? new DateTime(2026, 8, 29, 22, 16, 0) : new DateTime(2026, 8, 29, 22, 10, 0));

        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });

        await NhanTenLenhAsync(rig);                       // openShopDetail shop 1
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);                       // chayFlashSale shop 1
        await rig.GuiAsync(new { action = "pageData", kind = "flashKetQua", token = "111", data = GoiTao(80) });
        xongShopMot = true;

        var kq = await chay;

        Assert.Equal(1, kq.SoTao);
        Assert.True(kq.HetKhung);
        Assert.Equal(2, kq.SoShopChuaChay);
        Assert.Equal(3, kq.TongShop);
        // Và KHÔNG có lệnh nào bay tiếp: shop 2 tuyệt đối không được mở.
        Assert.Null(await ThuNhanThemLenhAsync(rig));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 4. "NGÀY KHÔNG CÒN ĐỎ" — KẾT LUẬN MỨC NGÀY, CẦN MỌI SHOP ĐỒNG THUẬN
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>MỘT shop khai "không còn đỏ" KHÔNG được giết cả lượt</b> — vòng phải đi tiếp và làm nốt hai shop kia,
    /// và cờ mức ngày <c>KetQuaKm.NgayMaiKhongDo</c> phải là <c>false</c>.
    ///
    /// <para><b>Đây là vế NGƯỢC của bản trước, và đổi vì một lỗi ĐO ĐƯỢC</b> (sổ P-20260831-3). Lý do cũ —
    /// "lịch khung là của SHOPEE nên một shop khai là đủ" — bị chính số đo bác bỏ: 16:31 lịch còn đỏ, K1 dựng
    /// nốt khung cuối của shop đó, 16:33 lịch HẾT đỏ. Tức cờ tắt cả khi chỉ RIÊNG shop này kín khung ⇒ một shop
    /// đã làm xong giết oan mọi shop còn lại, không lỗi, không cảnh báo.</para>
    ///
    /// <para>Shop khai cờ vẫn được tính là BỎ QUA (nó không còn gì để làm), nên <c>SoBoQua</c> = 1.</para>
    /// </summary>
    [Fact]
    public async Task MotShopKhongDo_KhongGietCaLuot_VaCoMucNgayVanTat()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var chay = ChayVongAsync(rig);
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });

        // Shop 1 KHÔNG đỏ; shop 2 và 3 tạo được bình thường.
        foreach (var (shopId, goi) in new[] { ("111", GoiKhongDo), ("222", GoiTao(80)), ("333", GoiTao(60)) })
        {
            await NhanTenLenhAsync(rig);
            await rig.GuiAsync(new { action = "shopOpened" });
            await NhanTenLenhAsync(rig);
            await rig.GuiAsync(new { action = "pageData", kind = "flashKetQua", token = shopId, data = goi });
        }

        var kq = await chay;

        Assert.False(kq.NgayMaiKhongDo);        // ⚠ mệnh đề chính: KHÔNG kết luận mức ngày từ một shop
        Assert.Equal(2, kq.SoTao);              // hai shop kia VẪN chạy
        Assert.Equal(1, kq.SoBoQua);
        Assert.Equal(0, kq.SoShopChuaChay);
        Assert.Equal(3, kq.TongShop);
        Assert.Null(kq.Loi);
        Assert.True(rig.CoLog("ĐÂY KHÔNG PHẢI LỖI"));
    }

    /// <summary>
    /// <b>CẢ LƯỢT đều "không còn đỏ" và không shop nào tạo được gì ⇒ cờ mức ngày BẬT.</b>
    /// <para>Cặp với bài trên: một shop khai không đủ, cả lượt khai thì mới bật.</para>
    /// <para><b>⚠ Cờ này CHỈ ĐỂ BÁO CÁO</b> (chốt 01/09/2026, phản biện vòng 1) — nó đi vào câu tổng kết và một
    /// dòng nhật ký, KHÔNG đóng cửa ngày ở tầng nhịp. Bằng chứng của một GÓI ~2 shop không đủ để kết luận cho
    /// 12 shop; vế ấy đo ở <c>FlashGoiKheNghiTests.GoiToanKhongDo_ThiNhipVanMoGoiSau</c>.</para>
    /// </summary>
    [Fact]
    public async Task CaLuotKhongDo_ThiCoMucNgayBat()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var chay = ChayVongAsync(rig);
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });

        foreach (var shopId in new[] { "111", "222", "333" })
        {
            await NhanTenLenhAsync(rig);
            await rig.GuiAsync(new { action = "shopOpened" });
            await NhanTenLenhAsync(rig);
            await rig.GuiAsync(new { action = "pageData", kind = "flashKetQua", token = shopId, data = GoiKhongDo });
        }

        var kq = await chay;

        Assert.True(kq.NgayMaiKhongDo);
        Assert.Equal(0, kq.SoTao);
        Assert.Equal(3, kq.SoBoQua);
        Assert.Equal(0, kq.SoShopChuaChay);
        Assert.Null(kq.Loi);
        Assert.False(kq.Captcha);
    }

    /// <summary>
    /// <b>MỐC NGÀY (<c>FlashLanChayCuoi</c>) CHỈ ĐƯỢC GHI KHI NGÀY ĐÃ PHỦ ĐỦ MỌI SHOP.</b>
    ///
    /// <para><b>Chốt quan trọng nhất của đợt 01/09/2026.</b> Luật cũ ghi mốc khi <c>SoTao &gt; 0</c>; từ khi một
    /// ngày chia thành nhiều GÓI nhỏ, luật ấy cho gói ĐẦU TIÊN ghi mốc rồi <c>DenGioGoiFlash</c> khoá sạch các
    /// gói còn lại ⇒ 10 shop cuối không bao giờ chạy, và không một dòng log nào nói ra.</para>
    ///
    /// <para>Ba vế đo ở đây: <b>(a)</b> gói tạo được nhưng CÒN shop chưa chạy ⇒ KHÔNG ghi; <b>(b)</b> gói phủ
    /// nốt phần còn lại ⇒ GHI; <b>(c)</b> tiến độ ngày tích luỹ đúng khóa shop qua hai gói.</para>
    /// </summary>
    [StaFact]
    public void MocNgay_ChiGhiKhiPhuDuMoiShop()
    {
        var temp = new TempDatabase();
        using var db = temp;
        var services = new XuLyDonShopee.App.Services.AppServices(temp.Path);
        services.Accounts.Insert(new XuLyDonShopee.Core.Models.Account { Email = "flash@mail.com", Password = "p" });
        var vm = new XuLyDonShopee.App.ViewModels.AccountsViewModel(services);
        var idTk = services.Accounts.GetAll().First().Id;

        var luc = new DateTime(2026, 9, 1, 10, 0, 0);
        var ngayDich = KmLichTuDong.NgayDichFlash(luc);

        // Picker của gói 1 đọc được ĐỦ 3 shop — đường thật do `ChayLatCatAsync` ghi khi callback đọc xong
        // danh sách, TRƯỚC bộ lọc `boQuaShop`. Đây là MẪU SỐ của luật "ngày đã trọn".
        vm.GhiSoShopThayFlash(idTk, ngayDich, 3);

        // (a) GÓI 1: tạo được 2 shop, còn 1 shop chưa chạy (hết hạn gói) ⇒ ngày CHƯA trọn.
        vm.ChotLuotFlash(idTk, "flash@mail.com", new KetQuaKm(
            [
                new KetQuaKmMotShop("111", "Shop Một", DaTao: true, SoSanPham: 80, null, null, ShopLogin: "shop.mot"),
                new KetQuaKmMotShop("222", "Shop Hai", DaTao: true, SoSanPham: 60, null, null, ShopLogin: "shop.hai"),
            ],
            false, null, HetKhung: true, SoShopChuaChay: 1), luc);

        Assert.Null(services.KmConfig.Get(idTk).FlashLanChayCuoi);
        Assert.Equal(new[] { "shop.hai", "shop.mot" }, vm.ShopFlashDaXong(idTk, ngayDich).OrderBy(x => x).ToArray());

        // (b) GÓI 2: shop cuối (đường thật: hai shop kia đã bị `boQuaShop` lọc khỏi danh sách) ⇒ TRỌN ⇒ ghi mốc.
        var luc2 = new DateTime(2026, 9, 1, 12, 30, 0);
        vm.ChotLuotFlash(idTk, "flash@mail.com", new KetQuaKm(
            [new KetQuaKmMotShop("333", "Shop Ba", DaTao: true, SoSanPham: 50, null, null, ShopLogin: "shop.ba")],
            false, null), luc2);

        var cf = services.KmConfig.Get(idTk);
        Assert.Equal(luc2, cf.FlashLanChayCuoi);
        // ⚠ ĐÚNG MỘT mốc được ghi. Gộp mốc là một vòng tự tay khoá vòng kia — câm lặng và vĩnh viễn
        // (xem `KmLichTuDong.MocThuCua`).
        Assert.Null(cf.LanChayCuoi);
        Assert.Null(cf.ComboLanChayCuoi);
        Assert.Equal(3, vm.ShopFlashDaXong(idTk, ngayDich).Count);
    }

    /// <summary>
    /// <b>Lượt BẤM TAY lẻ vài shop KHÔNG "đốt" lượt tự động của cả ngày.</b>
    ///
    /// <para>Thay cho chốt của commit <c>6582e64</c> (ngoại lệ "mốc chạy cuối nằm ngoài khung 21–24h" trong
    /// <c>DenGioChay</c>), thứ đã gỡ cùng khung tối. Ca thật đêm 31/08: lượt tay 19:04 tạo được ⇒ ghi mốc ⇒ 21h
    /// lượt auto lặng lẽ nghỉ, không một dòng log nói vì sao.</para>
    /// <para>Nay chốt nằm ở CHÍNH luật ghi mốc, nên nó đúng cho mọi giờ trong ngày chứ không chỉ ngoài một
    /// khung: lượt tay chạy 1/3 shop ⇒ chưa trọn ⇒ không ghi ⇒ <c>DenGioGoiFlash</c> vẫn cho mở gói.</para>
    /// </summary>
    [StaFact]
    public void LuotTayMotShop_KhongDotLuotTuDong()
    {
        var temp = new TempDatabase();
        using var db = temp;
        var services = new XuLyDonShopee.App.Services.AppServices(temp.Path);
        services.Accounts.Insert(new XuLyDonShopee.Core.Models.Account { Email = "flash@mail.com", Password = "p" });
        var vm = new XuLyDonShopee.App.ViewModels.AccountsViewModel(services);
        var idTk = services.Accounts.GetAll().First().Id;

        var tay = new DateTime(2026, 9, 1, 19, 4, 0);
        vm.ChotLuotFlash(idTk, "flash@mail.com", new KetQuaKm(
            [new KetQuaKmMotShop("111", "Shop Một", DaTao: true, SoSanPham: 80, null, null, ShopLogin: "shop.mot")],
            false, null, SoShopChuaChay: 11), tay);

        var cf = services.KmConfig.Get(idTk) with { FlashBat = true };
        Assert.Null(cf.FlashLanChayCuoi);
        // Và cửa nhịp vẫn MỞ cho gói tự động sau đó.
        Assert.True(KmLichTuDong.DenGioGoiFlash(cf, new DateTime(2026, 9, 1, 20, 0, 0)));
    }

    /// <summary>
    /// <b>Shop khai "ngày đích không còn đỏ" mà chưa tạo được gì thì KHÔNG XONG — gói sau phải thử lại nó.</b>
    ///
    /// <para><b>Ca hỏng mà bài này chặn</b> (phản biện vòng 1, 01/09/2026): bản đầu gộp shop ấy vào nhánh
    /// <c>BoQua</c> nên nó vào thẳng tập XONG và bị <c>boQuaShop</c> gạt khỏi MỌI gói còn lại của ngày. Lịch mất
    /// đỏ vì Shopee render chậm, hoặc vì hôm đó Shopee mở khung muộn, là shop mất trắng cả ngày — câm lặng, và
    /// tệ hơn: ngày vẫn được ghi mốc "đã trọn" nên cả những gói của ngày cũng thôi mở.</para>
    ///
    /// <para>Hai vế: <b>(a)</b> shop không-đỏ KHÔNG vào tập xong (⇒ gói sau vẫn chạy nó); <b>(b)</b> ngày KHÔNG
    /// được ghi mốc "trọn" khi vẫn còn một shop như vậy — dù lượt sạch lỗi và đã chạy hết danh sách.</para>
    /// <para>Vế ĐỐI CHỨNG cùng bài: shop <c>BoQua</c> vì lý do THẬT (không có sản phẩm) thì VẪN xong như cũ —
    /// không có nó thì một bản vá "không bao giờ tính shop nào là xong" cũng qua được.</para>
    /// </summary>
    [StaFact]
    public void ShopKhongDoMaChuaTao_ThiCHUAXong_VaNgayChuaTron()
    {
        var temp = new TempDatabase();
        using var db = temp;
        var services = new XuLyDonShopee.App.Services.AppServices(temp.Path);
        services.Accounts.Insert(new XuLyDonShopee.Core.Models.Account { Email = "flash@mail.com", Password = "p" });
        var vm = new XuLyDonShopee.App.ViewModels.AccountsViewModel(services);
        var idTk = services.Accounts.GetAll().First().Id;

        var luc = new DateTime(2026, 9, 1, 10, 0, 0);
        var ngayDich = KmLichTuDong.NgayDichFlash(luc);
        vm.GhiSoShopThayFlash(idTk, ngayDich, 2);   // picker đọc được đủ 2 shop

        // Gói CHẠY HẾT danh sách, không lỗi: shop 1 tạo được, shop 2 khai "ngày đích không còn đỏ".
        vm.ChotLuotFlash(idTk, "flash@mail.com", new KetQuaKm(
            [
                new KetQuaKmMotShop("111", "Shop Một", DaTao: true, SoSanPham: 80, null, null, ShopLogin: "shop.mot"),
                new KetQuaKmMotShop("222", "Shop Hai", false, 0, "ngày đích không đỏ", null,
                    NgayMaiKhongDo: true, ShopLogin: "shop.hai"),
            ],
            false, null), luc);

        // (a) CHỈ shop 1 vào tập xong ⇒ gói sau KHÔNG lọc mất shop 2.
        Assert.Equal(new[] { "shop.mot" }, vm.ShopFlashDaXong(idTk, ngayDich).ToArray());
        // (b) Và ngày CHƯA trọn — dù lượt sạch lỗi, không captcha, không còn shop chưa chạy.
        Assert.Null(services.KmConfig.Get(idTk).FlashLanChayCuoi);

        // ── VẾ ĐỐI CHỨNG: `BoQua` vì lý do THẬT vẫn là XONG, và ngày trọn theo.
        vm.ChotLuotFlash(idTk, "flash@mail.com", new KetQuaKm(
            [new KetQuaKmMotShop("222", "Shop Hai", false, 0, "không có sản phẩm nào", null,
                ShopLogin: "shop.hai")],
            false, null), luc);

        Assert.Equal(2, vm.ShopFlashDaXong(idTk, ngayDich).Count);
        Assert.Equal(luc, services.KmConfig.Get(idTk).FlashLanChayCuoi);
    }

    /// <summary>
    /// <b>Mốc ngày KHÔNG được ghi khi tập xong chưa phủ hết SỐ SHOP TỪNG THẤY của ngày.</b>
    ///
    /// <para><b>Ca hỏng mà bài này chặn</b> (phản biện vòng 1, 01/09/2026): mỗi gói chạy trên danh sách ĐÃ BỊ
    /// <c>boQuaShop</c> lọc, nên "mọi shop của lượt đều xong + không còn shop chưa chạy" là mệnh đề về CÁI LƯỢT,
    /// không phải về cả ngày. Một lượt picker render thiếu — 5/12 shop, đã xảy ra thật — làm cả ba vế ấy đúng
    /// hết, rồi mốc ngày được ghi và <c>DenGioGoiFlash</c> khoá sạch phần còn lại của ngày. Bảy shop biến mất,
    /// không lỗi, không log.</para>
    ///
    /// <para>Hai vế: <b>(a)</b> thiếu so với số từng thấy ⇒ KHÔNG ghi; <b>(b)</b> gói sau phủ nốt ⇒ GHI. Vế (b)
    /// bắt buộc: thiếu nó thì một bản vá "không bao giờ ghi mốc" cũng xanh, và ngày nào cũng chạy lại từ đầu.</para>
    /// </summary>
    [StaFact]
    public void MocNgay_KhongGhiKhiPickerRenderThieuShop()
    {
        var temp = new TempDatabase();
        using var db = temp;
        var services = new XuLyDonShopee.App.Services.AppServices(temp.Path);
        services.Accounts.Insert(new XuLyDonShopee.Core.Models.Account { Email = "flash@mail.com", Password = "p" });
        var vm = new XuLyDonShopee.App.ViewModels.AccountsViewModel(services);
        var idTk = services.Accounts.GetAll().First().Id;

        var luc = new DateTime(2026, 9, 1, 10, 0, 0);
        var ngayDich = KmLichTuDong.NgayDichFlash(luc);

        // Gói 1 đọc được ĐỦ 3 shop nhưng chỉ kịp làm 2 (hết hạn gói).
        vm.GhiSoShopThayFlash(idTk, ngayDich, 3);
        vm.ChotLuotFlash(idTk, "flash@mail.com", new KetQuaKm(
            [
                new KetQuaKmMotShop("111", "Shop Một", DaTao: true, SoSanPham: 80, null, null, ShopLogin: "shop.mot"),
                new KetQuaKmMotShop("222", "Shop Hai", DaTao: true, SoSanPham: 60, null, null, ShopLogin: "shop.hai"),
            ],
            false, null, HetKhung: true, SoShopChuaChay: 1), luc);
        Assert.Null(services.KmConfig.Get(idTk).FlashLanChayCuoi);

        // (a) GÓI 2: picker render THIẾU — chỉ thấy 1 shop, và nó vốn đã xong nên lượt về rỗng, sạch lỗi.
        // Ba vế "của lượt" đều đúng hết, nhưng ngày mới có 2/3 shop ⇒ TUYỆT ĐỐI KHÔNG được ghi mốc.
        vm.GhiSoShopThayFlash(idTk, ngayDich, 1);
        vm.ChotLuotFlash(idTk, "flash@mail.com", new KetQuaKm([], false, null), luc.AddHours(2));

        Assert.Null(services.KmConfig.Get(idTk).FlashLanChayCuoi);
        // Và số TỪNG THẤY vẫn là 3 — lấy MAX, không lấy lượt gần nhất.
        Assert.Equal(2, vm.ShopFlashDaXong(idTk, ngayDich).Count);

        // (b) GÓI 3: picker lành lại, shop cuối chạy xong ⇒ đủ 3/3 ⇒ GHI mốc.
        var luc3 = luc.AddHours(4);
        vm.GhiSoShopThayFlash(idTk, ngayDich, 3);
        vm.ChotLuotFlash(idTk, "flash@mail.com", new KetQuaKm(
            [new KetQuaKmMotShop("333", "Shop Ba", DaTao: true, SoSanPham: 50, null, null, ShopLogin: "shop.ba")],
            false, null), luc3);

        Assert.Equal(luc3, services.KmConfig.Get(idTk).FlashLanChayCuoi);
    }

    /// <summary>
    /// <b>Câu tổng kết phải nêu <c>Loi</c> TRƯỚC cờ "ngày không còn đỏ"</b> — cả ở dòng nhật ký lẫn ô trạng thái.
    ///
    /// <para><b>Ca hỏng mà bài này chặn</b> (phản biện vòng 1, 01/09/2026): hai thứ đó có thể cùng đúng trong
    /// một gói — shop đầu khai không-đỏ rồi lượt <c>vePicker</c> hỏng, cắt gói ngay tại đó. Bản trước xếp cờ
    /// TRƯỚC <c>Loi</c> nên câu duy nhất người dùng đọc được là một câu êm ái "KHÔNG phải lỗi selector", còn câu
    /// lỗi thật thì bị nuốt mất. Cờ là quan sát; <c>Loi</c> là sự cố.</para>
    /// </summary>
    [Fact]
    public void CauTongKet_NeuLoiTruocCoNgayKhongDo()
    {
        var kq = new KetQuaKm(
            [new KetQuaKmMotShop("111", "Shop Một", false, 0, "ngày đích không đỏ", null,
                NgayMaiKhongDo: true, ShopLogin: "shop.mot")],
            false, "Không về được trang chọn shop sau shop Shop Một — dừng lượt.",
            SoShopChuaChay: 2, NgayMaiKhongDo: true);

        var nhat = XuLyDonShopee.App.ViewModels.AccountsViewModel.CauTongKetKm(
            kq, false, LoaiChuongTrinh.FlashSale);
        Assert.Contains("Không về được trang chọn shop", nhat);
        Assert.DoesNotContain("KHÔNG phải lỗi selector", nhat);

        var trangThai = XuLyDonShopee.App.ViewModels.AccountsViewModel.CauTrangThaiKm(
            kq, LoaiChuongTrinh.FlashSale);
        Assert.Contains("Không về được trang chọn shop", trangThai);

        // ── VẾ ĐỐI CHỨNG: KHÔNG có `Loi` ⇒ câu nói về cờ, và nói HAI CHIỀU (kín khung ⋁ Shopee chưa/đã đóng).
        var sachLoi = kq with { Loi = null };
        var nhat2 = XuLyDonShopee.App.ViewModels.AccountsViewModel.CauTongKetKm(
            sachLoi, false, LoaiChuongTrinh.FlashSale);
        Assert.Contains("không còn ĐỎ", nhat2);
        Assert.Contains("đã kín khung", nhat2);
        Assert.Contains("Shopee chưa mở / đã đóng", nhat2);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 5. TRẦN CHẾ ĐỘ THỬ — TIÊM vào hàm, KHÔNG đọc hằng
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Trần <c>1</c> ⇒ chạy đúng một shop rồi dừng, và câu tổng kết mang SỐ TRẦN (không lẫn với "hết khung").
    /// <para><b>⚠ TIÊM trần qua tham số, KHÔNG đọc <see cref="KmLichTuDong.TranShopLuotFlash"/></b> — bài học
    /// của công tắc combo: test đọc hằng sẽ đỏ OAN đúng lúc người dùng gỡ trần, rồi bị xoá đi. Test này sống
    /// được ở CẢ HAI trạng thái của công tắc.</para>
    /// </summary>
    [Fact]
    public async Task TranCheDoThu_MotShop_ThiDungSauShopDau()
    {
        await using var rig = await BridgeTestRig.StartAsync();
        var daNghi = 0;

        var chay = ChayVongAsync(rig, tranShop: 1, nghiGiuaShop: _ => { daNghi++; return Task.CompletedTask; });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "flashKetQua", token = "111", data = GoiTao(80) });

        var kq = await chay;

        Assert.Equal(1, kq.SoTao);
        Assert.Equal(1, kq.TranThuDaCham);
        Assert.Equal(2, kq.SoShopChuaChay);
        Assert.False(kq.HetKhung);
        // KHÔNG nằm nghỉ 3–5 phút rồi mới dừng (ca thật 16:51→16:56 của vòng combo).
        Assert.Equal(0, daNghi);
        Assert.Null(await ThuNhanThemLenhAsync(rig));
    }

    /// <summary>Vế ĐỐI CHỨNG: trần <c>0</c> ⇒ chạy hết danh sách. Không có vế này thì một bản vá "luôn dừng sau
    /// shop đầu" vẫn xanh.</summary>
    [Fact]
    public async Task TranBangKhong_ThiChayHetDanhSach()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var chay = ChayVongAsync(rig, tranShop: 0);
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });
        foreach (var shopId in new[] { "111", "222", "333" })
        {
            await NhanTenLenhAsync(rig);
            await rig.GuiAsync(new { action = "shopOpened" });
            await NhanTenLenhAsync(rig);
            await rig.GuiAsync(new { action = "pageData", kind = "flashKetQua", token = shopId, data = GoiTao(80) });
        }

        var kq = await chay;
        Assert.Equal(3, kq.SoTao);
        Assert.Equal(0, kq.TranThuDaCham);
    }

    /// <summary>
    /// <b>Gói bị HẠN cắt ⇒ KHÔNG nằm nghỉ 3–5 phút rồi mới dừng.</b>
    ///
    /// <para><b>Ca hỏng mà bài này chặn</b> (phản biện vòng 1, 01/09/2026): vòng nghỉ giữa hai shop TRƯỚC khi
    /// quay lại đầu vòng lặp, mà cửa <c>ConDuocBatDauShopMoi</c> thì nằm Ở ĐẦU vòng lặp — nên một gói đã hết giờ
    /// vẫn nằm nghỉ đủ 3–5 phút RỒI MỚI in dòng "Hết khung". Ngần ấy phút nằm TRONG khe nghỉ mà gói đang mượn,
    /// và check đơn ở MỌI máy vẫn đang bị dừng vì khoá xuyên máy chưa nhả. Đúng hình dạng ca thật 16:51→16:56
    /// ngày 26/08/2026, nhưng của vế HẠN thay vì vế TRẦN (trần nay đã gỡ về 0, thành nhánh chết).</para>
    ///
    /// <para>Đồng hồ giả nhảy sau lượt đọc ĐẦU TIÊN: shop 1 vẫn được bắt đầu (10:00 &lt; hạn 10:30), rồi mọi lượt
    /// đọc sau đó là 11:00 — quá hạn. Vế nghỉ phải thấy điều đó NGAY, không đợi tới vòng lặp kế.</para>
    /// </summary>
    [Fact]
    public async Task GoiBiHanCat_ThiKhongNamNghiTruocKhiDung()
    {
        await using var rig = await BridgeTestRig.StartAsync();
        var daNghi = 0;
        var lanDoc = 0;
        var t0 = new DateTime(2026, 8, 29, 10, 0, 0);

        var chay = ChayVongAsync(
            rig,
            hanChot: t0.AddMinutes(30),
            dongHo: () => ++lanDoc == 1 ? t0 : t0.AddMinutes(60),
            nghiGiuaShop: _ => { daNghi++; return Task.CompletedTask; });

        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "flashKetQua", token = "111", data = GoiTao(80) });

        var kq = await chay;

        Assert.Equal(1, kq.SoTao);
        Assert.True(kq.HetKhung);
        Assert.Equal(2, kq.SoShopChuaChay);
        Assert.Equal(0, daNghi);            // ⚠ mệnh đề chính
        Assert.Null(await ThuNhanThemLenhAsync(rig));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 6. KHÔNG LƯU TRẠNG THÁI NÀO XUỐNG DB — grep chứng minh
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>KHÔNG có CON TRỎ TIẾN ĐỘ nào xuống DB.</b> Việc của một đêm Flash Sale là HÀM THUẦN CỦA NGÀY
    /// (plan §4): không cột "đã tới ngày nào", không "khung nào đã dùng", không danh sách shop đã xong.
    ///
    /// <para><b>Vì sao điều này quyết định chứ không phải tiện nghi:</b> hồ sơ KM đồng bộ xuyên máy và fleet có
    /// nhiều máy. Một con trỏ tiến độ sẽ TRÔI LỆCH ngay khi một máy bỏ lỡ một đêm (tắt máy, mất mạng, thua
    /// khoá) — máy A tưởng đã làm tới ngày 12, máy B tưởng mới tới ngày 9, và không ai biết ai đúng.</para>
    ///
    /// <para><b>⚠ HAI MỐC LỊCH thì KHÁC HẲN và BẮT BUỘC PHẢI CÓ</b> (<c>flash_lan_chay_cuoi</c> ·
    /// <c>flash_lan_thu_tu_dong</c>): chúng gộp theo MAX nên fleet tự hội tụ thay vì trôi lệch, và
    /// <c>flash_lan_thu_tu_dong</c> là thứ DUY NHẤT chặn nhịp 60 giây phóng lại Brave suốt khung 3 tiếng
    /// (180 lần một đêm). Test này khoá đúng RANH GIỚI đó: có ba cột, và KHÔNG có gì hơn.</para>
    /// </summary>
    [Fact]
    public void KhongLuuConTroTienDoNaoXuongDb()
    {
        var nguon = NguonFile("orders", "XuLyDonShopee.App", "ViewModels", "AccountsViewModel.Phien.cs");

        // Vùng NGUỒN của `ChotLuotFlash` (nằm giữa hai mốc này).
        var vung = CatVung(nguon,
            "internal KetLuanLatCat ChotLuotFlash(",
            "internal DateTime? MocChayLaiCheckDonSauFlash");
        // ĐÚNG MỘT trường được ghi, và là mốc CỦA VÒNG NÀY. Khớp trên chính CÂU LỆNH `with { … }` chứ không
        // trên tên trường trần: xmldoc ở đây có nhắc tên hai mốc kia để nói "TUYỆT ĐỐI KHÔNG PHẢI chúng", và
        // một phép grep bắt cả chú thích là một phép grep tự đỏ vì lời cảnh báo của chính nó.
        Assert.Contains("with { FlashLanChayCuoi = luc }", vung);
        Assert.DoesNotContain("with { ComboLanChayCuoi", vung);
        Assert.DoesNotContain("with { LanChayCuoi", vung);
        // Không đụng lưới shop ⇒ không có danh sách "shop đã xong" nào.
        Assert.DoesNotContain("Results.", vung);

        // Hồ sơ KM: ĐÚNG BA cột flash (một công tắc + hai mốc lịch), KHÔNG con trỏ tiến độ nào.
        var db = NguonFile("orders", "XuLyDonShopee.Core", "Data", "Database.cs");
        foreach (var cot in new[] { "flash_bat", "flash_lan_chay_cuoi", "flash_lan_thu_tu_dong" })
        {
            Assert.Contains(cot, db);
        }
        foreach (var conTro in new[]
                 {
                     "flash_ngay_", "flash_khung_", "flash_con_tro", "flash_shop_", "flash_tien_do",
                 })
        {
            Assert.DoesNotContain(conTro, db);
        }

        var kmConfig = NguonFile("orders", "XuLyDonShopee.Core", "Data", "KmConfigRepository.cs");
        Assert.Contains("public bool FlashBat", kmConfig);
        Assert.Contains("public DateTime? FlashLanChayCuoi", kmConfig);
        Assert.Contains("public DateTime? FlashLanThuTuDong", kmConfig);
        Assert.DoesNotContain("FlashNgayDaLam", kmConfig);
        Assert.DoesNotContain("FlashKhungDaDung", kmConfig);
        Assert.DoesNotContain("FlashShopDaXong", kmConfig);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 7. HẠN CHẶNG RIÊNG — không mượn ngân sách của vòng KM
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Chặng <c>FlashChay</c> phải là HẰNG RIÊNG (không mượn <c>KmChay</c>)</b>, và phép cộng đứng sau nó
    /// phải còn trong xmldoc.
    /// <para>Mượn ngân sách của nhau là một trong hai luồng luôn sai: rộng quá thì một shop hỏng THẬT giữ chỗ
    /// lâu trong khe nghỉ mà gói đang mượn; hẹp quá thì shop bị ghi HỎNG oan sau khi đã tạo thật vài chương
    /// trình, và lượt sau tạo TRÙNG — ĐÃ XẢY RA THẬT 19:25 31/08/2026 (trần 900s cũ chết ở khung 5/8 sau khi
    /// 4 khung đã TẠO + KÍCH HOẠT). Nay <b>2700s</b>: đo cả đêm 31/08 cho ra 4–4,5 phút/khung, tức một shop đủ
    /// 8 khung mất 32–36 phút và trần 1800s chỉ còn suýt soát. Khác hẳn <c>KmChay</c> (1800s) — hai hằng độc
    /// lập, chỉnh bên nào không kéo bên kia.</para>
    /// </summary>
    [Fact]
    public void HanChang_FlashChay_RiengVaDuChoTamKhung()
    {
        var nguon = NguonFile("orders", "XuLyDonShopee.Core", "Services", "OrdersBridgeChannel.cs");
        Assert.Contains("FlashChay = TimeSpan.FromSeconds(2700)", nguon);
        Assert.Contains("KmChay = TimeSpan.FromSeconds(1800)", nguon);

        // Từ 03/09/2026 KHÔNG còn trần khung: hạn này là thứ duy nhất giới hạn số khung một shop làm được trong
        // một gói, nên xmldoc phải nói ra phép chia 2700s ÷ (giây/khung) và nhắc chốt kỹ thuật còn lại bên JS.
        Assert.DoesNotContain("SO_KHUNG_TOI_DA", nguon);
        Assert.Contains("TRAN_VONG_KHUNG_KY_THUAT", nguon);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 7. LỌC SHOP ĐÃ XONG (gói thứ hai trở đi) + HAI SỐ RIÊNG CỦA GÓI KẾT QUẢ
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b><c>boQuaShop</c> lọc đúng shop đã xong, và KHÔNG mở chúng lần nữa.</b>
    ///
    /// <para>Từ 01/09/2026 một ngày flash chia thành nhiều GÓI, mỗi gói chiếm một khe nghỉ — nên gói thứ hai
    /// phải biết bỏ qua phần đã làm. Lọc SAI thì gói sau mở lại đúng shop vừa xong: tốn cả khe nghỉ cho một
    /// shop chỉ để nghe nó nói "hết khung", và mỗi lần mở shop là một lần đụng vào Shopee.</para>
    ///
    /// <para><b>Khóa lọc là <c>login</c>, KHÔNG phải tên hiển thị</b> — shop trong bộ dữ liệu này có
    /// <c>name</c> ≠ <c>login</c>, nên một bản vá lọc theo <c>ShopName</c> sẽ không khớp gì và test đỏ.</para>
    /// </summary>
    [Fact]
    public async Task BoQuaShop_LocDungShopDaXong_VaGhiMotDongLog()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var boQua = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shop.mot", "shop.ba" };
        var chay = ChayVongAsync(rig, boQuaShop: boQua);
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });

        // CHỈ shop 2 được mở.
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "flashKetQua", token = "222", data = GoiTao(80) });

        var kq = await chay;

        Assert.Equal(1, kq.SoTao);
        Assert.Equal(1, kq.TongShop);                       // lượt này CHỈ có 1 shop
        Assert.Equal("shop.hai", kq.Shops[0].ShopLogin);    // và là đúng shop chưa xong
        Assert.Null(await ThuNhanThemLenhAsync(rig));
        Assert.True(rig.CoLog("Bỏ qua 2 shop đã xong hôm nay"));
    }

    /// <summary>
    /// Vế ĐỐI CHỨNG: <c>boQuaShop</c> <c>null</c> ⇒ chạy CẢ danh sách, không lọc gì. Đây cũng là đường của hai
    /// vòng KM/Combo — chúng không có tham số này nên luôn đi nhánh <c>null</c>.
    /// </summary>
    [Fact]
    public async Task BoQuaShopNull_ThiChayCaDanhSach()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var chay = ChayVongAsync(rig, boQuaShop: null);
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });
        foreach (var shopId in new[] { "111", "222", "333" })
        {
            await NhanTenLenhAsync(rig);
            await rig.GuiAsync(new { action = "shopOpened" });
            await NhanTenLenhAsync(rig);
            await rig.GuiAsync(new { action = "pageData", kind = "flashKetQua", token = shopId, data = GoiTao(80) });
        }

        var kq = await chay;
        Assert.Equal(3, kq.SoTao);
        Assert.False(rig.CoLog("Bỏ qua"));
    }

    /// <summary>
    /// <b>Bộ lọc gạt SẠCH danh sách ⇒ lượt về ÊM (không lỗi), và KHÔNG mở shop nào.</b>
    /// <para>Ca này xảy ra thật khi ngày đã phủ đủ mọi shop mà mốc ngày chưa kịp ghi. Trả về một <c>Loi</c> ở
    /// đây là câu tổng kết báo hỏng cho một lượt chẳng có gì để làm; và quan trọng hơn, <c>ChotLuotFlash</c>
    /// đọc <c>Loi is null</c> để kết luận ngày đã TRỌN.</para>
    /// </summary>
    [Fact]
    public async Task BoQuaShop_GatSachDanhSach_ThiVeEm()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var boQua = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shop.mot", "shop.hai", "shop.ba" };
        var chay = ChayVongAsync(rig, boQuaShop: boQua);
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });

        var kq = await chay;

        Assert.Null(kq.Loi);
        Assert.Empty(kq.Shops);
        Assert.Equal(0, kq.SoShopChuaChay);
        Assert.False(kq.NgayMaiKhongDo);        // lượt rỗng KHÔNG được kết luận "ngày đóng cửa"
        Assert.Null(await ThuNhanThemLenhAsync(rig));
    }

    /// <summary>
    /// <b>Đọc <c>soKhungDaTao</c> và <c>soTaiChe</c> từ gói kết quả; trường VẮNG ⇒ <c>0</c>.</b>
    ///
    /// <para><c>soTaiChe</c> từng bị RƠI ở gói extension dù luồng vẫn đếm (sổ P-20260831-5) — mà <c>daTao</c>
    /// tính CẢ nó, nên nhật ký đọc ra "tạo 0 khung" cho một shop đã dựng lại chương trình thật. Nối lại
    /// 01/09/2026, và vế "vắng ⇒ 0" là bắt buộc: hai luồng KM/Combo không gửi mấy trường này, và bản extension
    /// cũ trên máy người dùng cũng chưa gửi <c>soTaiChe</c>.</para>
    /// </summary>
    [Fact]
    public void DocSoKhungVaSoTaiChe_VangThiBangKhong()
    {
        var shop = new ShopListItem("111", "Shop Một", "shop.mot");

        var duCa = OrdersBridgeSession.DocKetQuaKmMotShop(
            """{"soSanPham":80,"daTao":true,"boQua":null,"soKhungDaTao":5,"soTaiChe":3}""", shop);
        Assert.Equal(5, duCa.SoKhungDaTao);
        Assert.Equal(3, duCa.SoTaiChe);
        Assert.Equal("shop.mot", duCa.ShopLogin);

        // Bản extension CŨ: có `soKhungDaTao`, chưa có `soTaiChe`.
        var thieuTaiChe = OrdersBridgeSession.DocKetQuaKmMotShop(
            """{"soSanPham":80,"daTao":true,"boQua":null,"soKhungDaTao":8}""", shop);
        Assert.Equal(8, thieuTaiChe.SoKhungDaTao);
        Assert.Equal(0, thieuTaiChe.SoTaiChe);

        // Gói của vòng KM/Combo: không có trường nào trong hai trường ấy.
        var goiKm = OrdersBridgeSession.DocKetQuaKmMotShop(
            """{"soSanPham":40,"daTao":true,"boQua":null}""", shop);
        Assert.Equal(0, goiKm.SoKhungDaTao);
        Assert.Equal(0, goiKm.SoTaiChe);

        // Sai KIỂU cũng phải ra 0, không được ném: gói đến từ extension, không phải từ code mình.
        var saiKieu = OrdersBridgeSession.DocKetQuaKmMotShop(
            """{"soSanPham":40,"daTao":true,"boQua":null,"soKhungDaTao":"tám"}""", shop);
        Assert.Equal(0, saiKieu.SoKhungDaTao);
    }

    /// <summary>
    /// <b>Extension PHẢI gửi cả <c>soKhungDaTao</c> lẫn <c>soTaiChe</c></b> trong gói <c>flashKetQua</c>.
    /// <para>Phía C# đọc "vắng ⇒ 0" nên thiếu trường KHÔNG làm gì đỏ — đúng hình dạng của lỗi đã có: phần mềm
    /// báo "tạo 0 khung" cho một shop đã tái chế thật. Grep nguồn là lưới duy nhất còn lại.</para>
    /// </summary>
    [Fact]
    public void GoiExtension_MangCaSoKhungDaTaoVaSoTaiChe()
    {
        var js = NguonFile("extensions", "shopee-orders", "flow-flashsale.js");
        var vung = CatVung(js, "export async function doChayFlashSale(", "\n}");
        Assert.Contains("soKhungDaTao: kq.soKhungDaTao || 0", vung);
        Assert.Contains("soTaiChe: kq.soTaiChe || 0", vung);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 8. NHƯỜNG CHỖ CHO CHECK ĐƠN (03/09/2026, sổ P-20260903-1)
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Cờ nhường bật GIỮA VÒNG ⇒ vòng DỪNG sau shop đang chạy, phần còn lại tính là CHƯA CHẠY.</b>
    ///
    /// <para><b>Ba mệnh đề trong một bài, và cần cả ba:</b> (1) shop ĐANG chạy KHÔNG bị cắt — nó vẫn nhận đủ
    /// chuỗi lệnh và vào <c>Shops</c>; (2) shop kế KHÔNG được mở (không có lệnh nào nữa); (3) hình dạng kết quả
    /// đúng bằng nhánh hết hạn — <c>SoShopChuaChay == n − 1</c> nên <c>ChotLuotFlash</c> KHÔNG ghi mốc ngày và
    /// gói sau làm tiếp. Thiếu (1) là bỏ lại một chương trình 0-sản-phẩm CHIẾM KHUNG trên Shopee; thiếu (3) là
    /// ngày bị đóng dấu "đã trọn" cho một danh sách còn 2 shop chưa ai đụng.</para>
    ///
    /// <para><b>Cờ đọc qua <see cref="Func{TResult}"/>, không phải <c>bool</c></b> — bài này bật cờ SAU khi vòng
    /// đã bắt đầu, đúng như đời thật (lượt hoãn tỉnh dậy giữa gói). Một bản vá nhận <c>bool</c> chụp giá trị lúc
    /// vào hàm sẽ không bao giờ thấy cú bật, và test này đỏ.</para>
    /// </summary>
    [Fact]
    public async Task PhaiNhuongSauShop_ThiDungSauShopDangChay_ConLaiChuaChay()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var nhuong = false;
        var chay = ChayVongAsync(rig, phaiNhuongSauShop: () => nhuong);

        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });

        // Shop 1 chạy TRỌN — cờ chỉ bật khi shop này đã xong.
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        nhuong = true;
        await rig.GuiAsync(new { action = "pageData", kind = "flashKetQua", token = "111", data = GoiTao(80) });

        var kq = await chay;

        Assert.True(kq.NhuongChoCheckDon, "kết quả không mang cờ 'nhường chỗ cho check đơn'");
        Assert.False(kq.HetKhung, "nhường chỗ KHÔNG phải hết khung — hai lý do dừng, hai câu tổng kết");
        Assert.Null(kq.Loi);
        Assert.Single(kq.Shops);                 // shop 1 chạy trọn, không bị cắt
        Assert.Equal("shop.mot", kq.Shops[0].ShopLogin);
        Assert.Equal(2, kq.SoShopChuaChay);      // n − 1 = 2 shop cho gói sau
        Assert.Equal(3, kq.TongShop);            // mẫu số vẫn là cả danh sách
        Assert.False(kq.NgayMaiKhongDo);         // shop 1 TẠO ĐƯỢC ⇒ không kết luận "ngày không đỏ"
        Assert.Null(await ThuNhanThemLenhAsync(rig));   // shop 2 KHÔNG được mở
        Assert.True(rig.CoLog("Nhường chỗ cho check đơn"));
        Assert.True(rig.CoLog("còn 2 shop"));
    }

    /// <summary>
    /// Vế ĐỐI CHỨNG: <b>cờ nhường LUÔN TẮT ⇒ chạy hết danh sách</b>, và <c>null</c> (hai vòng KM/Combo) cũng vậy.
    /// <para>Không có vế này thì một bản vá "luôn nhường" cũng qua được bài trên — và vòng flash sẽ dừng sau
    /// shop đầu của MỌI gói, câm lặng, đúng cái nó định tránh.</para>
    /// </summary>
    [Fact]
    public async Task PhaiNhuongSauShopTatOrNull_ThiChayCaDanhSach()
    {
        foreach (var co in new Func<bool>?[] { null, () => false })
        {
            await using var rig = await BridgeTestRig.StartAsync();
            var chay = ChayVongAsync(rig, phaiNhuongSauShop: co);
            await NhanTenLenhAsync(rig);
            await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });
            foreach (var shopId in new[] { "111", "222", "333" })
            {
                await NhanTenLenhAsync(rig);
                await rig.GuiAsync(new { action = "shopOpened" });
                await NhanTenLenhAsync(rig);
                await rig.GuiAsync(new { action = "pageData", kind = "flashKetQua", token = shopId, data = GoiTao(80) });
            }

            var kq = await chay;
            Assert.Equal(3, kq.SoTao);
            Assert.False(kq.NhuongChoCheckDon);
            Assert.Equal(0, kq.SoShopChuaChay);
            Assert.False(rig.CoLog("Nhường chỗ"));
        }
    }

    // ── Đồ nghề ───────────────────────────────────────────────────────────────────────────────────────

    private static Task<KetQuaKm> ChayVongAsync(
        BridgeTestRig rig, DateTime? hanChot = null, Func<DateTime>? dongHo = null,
        Func<CancellationToken, Task>? nghiGiuaShop = null, int? tranShop = null,
        IReadOnlySet<string>? boQuaShop = null, Func<bool>? phaiNhuongSauShop = null)
        => OrdersBridgeSession.ChayFlashSaleSauPickerAsync(
            rig.Channel, null, rig.Log, ThamSo,
            vePicker: _ => Task.FromResult(KetQuaVePicker.VeDuoc),
            nghiGiuaShop: nghiGiuaShop ?? (_ => Task.CompletedTask),
            hanChot: hanChot,
            dongHo: dongHo ?? (() => TrongKhung),
            ct: CancellationToken.None,
            // Mặc định của tham số là hằng công tắc — mọi test trong file TIÊM số riêng để đo luật thật, không
            // phụ thuộc công tắc đang bật hay tắt.
            tranShop: tranShop ?? 0,
            boQuaShop: boQuaShop,
            phaiNhuongSauShop: phaiNhuongSauShop);

    private static async Task<string> NhanTenLenhAsync(BridgeTestRig rig)
    {
        using var l = await rig.NhanLenhAsync();
        return l.RootElement.GetProperty("action").GetString() ?? "";
    }

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

    /// <summary>Đi từ thư mục chạy test ngược lên tìm gốc repo rồi đọc một file nguồn.</summary>
    private static string NguonFile(params string[] duong)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (!File.Exists(Path.Combine(dir.FullName, "ShopeeSuite.sln")))
            {
                continue;
            }
            var f = Path.Combine(new[] { dir.FullName }.Concat(duong).ToArray());
            if (File.Exists(f)) { return File.ReadAllText(f); }
        }

        throw new FileNotFoundException("Không tìm thấy " + string.Join("/", duong) + " từ " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Cắt đoạn nguồn từ <paramref name="tu"/> tới <paramref name="den"/>.
    /// <para>Cố ý KHÔNG cắt theo dấu ngoặc cân bằng: <c>ChayFlashSaleAsync</c> là hàm <b>thân biểu thức</b>
    /// (<c>=&gt;</c>), không có <c>{</c> của riêng nó — cắt theo ngoặc sẽ ăn nhầm thân của hàm KẾ TIẾP và biến
    /// phép grep thành một cái đạt/hỏng ngẫu nhiên.</para>
    /// </summary>
    private static string CatVung(string nguon, string tu, string den)
    {
        var i = nguon.IndexOf(tu, StringComparison.Ordinal);
        Assert.True(i >= 0, $"không còn đoạn bắt đầu bằng '{tu}'");
        var j = nguon.IndexOf(den, i, StringComparison.Ordinal);
        Assert.True(j > i, $"không còn mốc kết thúc '{den}' sau '{tu}'");
        return nguon[i..j];
    }
}
