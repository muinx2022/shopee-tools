using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>Vòng "KM của Shop" chạy trên CẦU NỐI THẬT</b> (<see cref="BridgeTestRig"/> đóng vai extension) — khoá
/// CHUỖI LỆNH và quyết định đi-tiếp/dừng cho bốn hình dạng shop đã biết.
/// <para><b>Vì sao bắt buộc phải là rig chứ không phải test hàm thuần</b> (tiêu chí §6 plan cũ bị chấm KHÔNG
/// ĐẠT 19/08/2026): mọi lệnh trong vòng này TÁC ĐỘNG THẬT lên shop của người dùng — một lệnh thừa là một
/// chương trình khuyến mãi thật mọc thêm, một lệnh thiếu là shop bị bỏ im lặng. Test đọc gói JSON
/// (<c>KmVongMoiShopTests</c>) không thấy được cái nào trong hai chuyện đó.</para>
/// <para>Chỉ chạy được khúc SAU khi đã về trang chọn shop: bước đăng nhập/SSO phóng trình duyệt thật. Hai móc
/// "về picker" và "nghỉ 3–5 phút" được rót từ ngoài (xem <see cref="OrdersBridgeSession.ChayKmSauPickerAsync"/>)
/// — chờ thật 3 phút trong test là vô nghĩa.</para>
/// </summary>
public class KmVongRigTests
{
    /// <summary>
    /// Gói của một lượt THẬT: khoảng <b>25-09-2026 → 24-03-2027</b> (đúng 180 ngày), CÙNG giờ 09:30 ở hai đầu.
    /// Hai mốc cố ý KHÁC THÁNG và KHÁC NĂM — đó là hình dạng thật từ 26/08/2026, và là thứ bắt được bản vá gửi
    /// nhầm một mốc cho cả hai ô.
    /// </summary>
    private static readonly ThamSoKm ThamSo =
        new("KM ngày 25-09-2026 từ 09:30",
            new DateTime(2026, 9, 25), new DateTime(2027, 3, 24), "09:30", 7);

    private const string MotShop = """[{"rowKey":"111","name":"Shop Một","login":"shop.mot"}]""";

    private const string HaiShop =
        """[{"rowKey":"111","name":"Shop Một","login":"shop.mot"},""" +
        """{"rowKey":"222","name":"Shop Hai","login":"shop.hai"}]""";

    private const string BaShop =
        """[{"rowKey":"111","name":"Shop Một","login":"shop.mot"},""" +
        """{"rowKey":"222","name":"Shop Hai","login":"shop.hai"},""" +
        """{"rowKey":"333","name":"Shop Ba","login":"shop.ba"}]""";

    /// <summary>Gói extension trả về khi TẠO ĐƯỢC.</summary>
    private static string GoiTao(int soSp) =>
        $$"""{"soSanPham":{{soSp}},"daTao":true,"boQua":null,"canhBao":[],"buocHong":[]}""";

    /// <summary>Gói khi shop RỖNG (bước 6 đo trong modal đang mở).</summary>
    private const string GoiRong =
        """{"soSanPham":0,"daTao":false,"boQua":"shop KHÔNG có sản phẩm nào — bỏ qua, không tạo chương trình","canhBao":[],"buocHong":[]}""";

    /// <summary>Gói khi extension KHÔNG có tab shop — hỏng HẠ TẦNG, `boQua` phải là null (xem hạng mục 18).</summary>
    private const string GoiKhongCoTab =
        """{"soSanPham":0,"daTao":false,"boQua":null,"canhBao":[],"captcha":false,"buocHong":[{"ten":"0. Tab shop","loi":"chưa có tab shop (openShopDetail chưa chạy, hoặc service worker đã chết giữa chừng)"}]}""";

    /// <summary>Gói tự khai rơi captcha GIỮA một bước (bước 6 dài ~12 phút là ca hay dính nhất).</summary>
    private const string GoiCaptchaTrongGoi =
        """{"soSanPham":0,"daTao":false,"boQua":null,"canhBao":[],"captcha":true,"buocHong":[{"ten":"6. Modal Chọn Sản Phẩm","loi":"rơi trang verify/captcha GIỮA bước: https://shopee.vn/verify/traffic"}]}""";

    /// <summary>Gói khi HỎNG giữa chừng (bước 4 đỏ ⇒ luồng dừng trước bước 9, không tạo gì).</summary>
    private const string GoiHong =
        """{"soSanPham":0,"daTao":false,"boQua":null,"canhBao":[],"buocHong":[{"ten":"4. Đặt thời gian BẮT ĐẦU","loi":"ĐỌC LẠI KHÔNG KHỚP: kỳ vọng bằng '09:30 25-09-2026', đọc được '09:30 25-08-2026'"}]}""";

    // ══════════ Ca (a) — shop CÓ sản phẩm: tạo được, chuỗi lệnh đúng bằng ══════════

    /// <summary>
    /// Một shop có sản phẩm: chuỗi lệnh ĐÚNG BẰNG <c>[readShopList, openShopDetail, chayKm]</c> — không thiếu
    /// (quên mở shop thì extension thao tác trên trang chọn shop), không thừa (mọi lệnh thừa đều đụng shop thật).
    /// Và tham số <c>chayKm</c> phải mang đủ NGÀY/THÁNG/NĂM: thiếu tháng thì extension click nhầm ngày 25 của
    /// tháng đang hiện trên picker.
    /// </summary>
    [Fact]
    public async Task CaA_ShopCoSanPham_TaoDuoc_ChuoiLenhDungBang()
    {
        await using var rig = await BridgeTestRig.StartAsync();
        var lenhDaGui = new List<string>();

        var chay = ChayVongAsync(rig);

        lenhDaGui.Add(await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = MotShop });

        lenhDaGui.Add(await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "shopOpened" });

        using (var l = await rig.NhanLenhAsync())
        {
            var root = l.RootElement;
            lenhDaGui.Add(root.GetProperty("action").GetString() ?? "");

            // Token lượt = shopId (channel dùng để loại gói lạc lượt).
            Assert.Equal("111", root.GetProperty("token").GetString());

            var ts = root.GetProperty("thamSo");

            // ⚠ KHOÁ CẢ HAI MỐC, TỪNG TRƯỜNG MỘT. Đây là chỗ DUY NHẤT trong bộ test thấy được thứ THẬT SỰ đi
            // xuống Shopee — mọi thước khác (dòng log, câu tổng kết) đều là chuỗi dựng lại. Bản vá gửi nhầm mốc
            // bắt đầu cho cả hai ô sẽ đỏ ở khối `kt` dưới đây, và chỉ ở đây.
            var bd = ts.GetProperty("bd");
            Assert.Equal(25, bd.GetProperty("ngayTrongThang").GetInt32());
            Assert.Equal(9, bd.GetProperty("thang").GetInt32());
            Assert.Equal(2026, bd.GetProperty("nam").GetInt32());
            Assert.Equal("25-09-2026", bd.GetProperty("ngayDayDu").GetString());

            var kt = ts.GetProperty("kt");
            Assert.Equal(24, kt.GetProperty("ngayTrongThang").GetInt32());
            Assert.Equal(3, kt.GetProperty("thang").GetInt32());
            Assert.Equal(2027, kt.GetProperty("nam").GetInt32());
            Assert.Equal("24-03-2027", kt.GetProperty("ngayDayDu").GetString());

            // MỘT giờ cho cả hai đầu — trường `gioKetThuc` cũ phải BIẾN MẤT khỏi gói, không chỉ là "không dùng".
            Assert.Equal("09:30", ts.GetProperty("gio").GetString());
            Assert.False(ts.TryGetProperty("gioKetThuc", out _), "gói còn sót trường 'gioKetThuc' đã bỏ");
            Assert.False(ts.TryGetProperty("ngayTrongThang", out _), "gói còn sót trường ngày PHẲNG của bản cũ");

            Assert.Equal(7, ts.GetProperty("phanTram").GetInt32());
        }
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiTao(240) });

        var kq = await chay;

        Assert.Equal(new[] { "readShopList", "openShopDetail", "chayKm" }, lenhDaGui);
        Assert.Single(kq.Shops);
        Assert.True(kq.Shops[0].DaTao);
        Assert.Equal(240, kq.Shops[0].SoSanPham);
        Assert.Equal(1, kq.SoTao);
        Assert.False(kq.Captcha);
        Assert.Null(kq.Loi);
        Assert.Null(await ThuNhanThemLenhAsync(rig));
    }

    // ══════════ Ca (b) — shop RỖNG: bỏ qua, KHÔNG tạo ══════════

    /// <summary>Shop rỗng ⇒ <c>BoQua</c>, KHÔNG tính là lỗi, và tuyệt đối không có lệnh nào bay thêm.</summary>
    [Fact]
    public async Task CaB_ShopRong_ThiBoQua_KhongTao_KhongLenhThua()
    {
        await using var rig = await BridgeTestRig.StartAsync();
        var lenhDaGui = new List<string>();

        var chay = ChayVongAsync(rig);

        lenhDaGui.Add(await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = MotShop });
        lenhDaGui.Add(await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "shopOpened" });
        lenhDaGui.Add(await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiRong });

        var kq = await chay;

        Assert.Equal(new[] { "readShopList", "openShopDetail", "chayKm" }, lenhDaGui);
        Assert.Equal(0, kq.SoTao);
        Assert.Equal(1, kq.SoBoQua);
        Assert.Equal(0, kq.SoHong);
        Assert.False(kq.Shops[0].DaTao);
        Assert.Null(kq.Shops[0].Loi);
        Assert.Null(await ThuNhanThemLenhAsync(rig));
    }

    // ══════════ Ca (c) — shop HỎNG giữa chừng: ghi HỎNG rồi ĐI TIẾP shop kế ══════════

    /// <summary>
    /// Shop #1 hỏng ở bước 4 ⇒ ghi HỎNG (nêu đích danh bước) rồi vòng vẫn chạy shop #2 — 12 shop mà chết vì
    /// shop thứ nhất thì 11 shop sau mất chương trình.
    /// </summary>
    [Fact]
    public async Task CaC_ShopHongGiuaChung_GhiHONG_VaVongDiTiepShopKe()
    {
        await using var rig = await BridgeTestRig.StartAsync();
        var lenhDaGui = new List<string>();

        var chay = ChayVongAsync(rig);

        lenhDaGui.Add(await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = HaiShop });

        lenhDaGui.Add(await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "shopOpened" });
        lenhDaGui.Add(await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiHong });

        lenhDaGui.Add(await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "shopOpened" });
        lenhDaGui.Add(await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "222", data = GoiTao(12) });

        var kq = await chay;

        Assert.Equal(
            new[] { "readShopList", "openShopDetail", "chayKm", "openShopDetail", "chayKm" },
            lenhDaGui);
        Assert.Equal(2, kq.Shops.Count);
        Assert.Equal(1, kq.SoHong);
        Assert.Equal(1, kq.SoTao);
        Assert.Contains("4. Đặt thời gian BẮT ĐẦU", kq.Shops[0].Loi);
        Assert.True(kq.Shops[1].DaTao);
        Assert.Null(kq.Loi); // vòng chạy HẾT, không phải dừng giữa chừng
    }

    // ══════════ Ca (d) — CAPTCHA: dừng cả vòng ══════════

    /// <summary>
    /// <b>Captcha giữa chặng <c>chayKm</c> ⇒ DỪNG CẢ VÒNG.</b> Bản trước chỉ kiểm captcha sau
    /// <c>openShopDetail</c>, nên rơi <c>/verify</c> ở bước 4 của shop #1 là vòng vẫn đi tiếp shop #2 trên một
    /// phiên Shopee đang nghi ngờ — tự khai bot.
    /// <para>Shop #2 phải KHÔNG có lệnh nào bay tới: đây là mệnh đề trung tâm của ca này.</para>
    /// </summary>
    [Fact]
    public async Task CaD_Captcha_GiuaChangChayKm_ThiDUNG_CA_VONG()
    {
        await using var rig = await BridgeTestRig.StartAsync();
        var lenhDaGui = new List<string>();

        var chay = ChayVongAsync(rig);

        lenhDaGui.Add(await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = HaiShop });
        lenhDaGui.Add(await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "shopOpened" });
        lenhDaGui.Add(await NhanTenLenhAsync(rig));

        // Extension rơi trang verify giữa luồng: báo captcha rồi thôi (đúng như flow-km.js làm).
        await rig.GuiAsync(new { action = "captcha", message = "https://shopee.vn/verify/traffic/error" });

        var kq = await chay;

        Assert.True(kq.Captcha);
        Assert.Contains("captcha", kq.Loi ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "readShopList", "openShopDetail", "chayKm" }, lenhDaGui);
        Assert.Null(await ThuNhanThemLenhAsync(rig)); // KHÔNG đụng tới shop #2
    }

    // ══════════ Gói kết quả LẠC LƯỢT ══════════

    /// <summary>
    /// <b>Gói <c>kmKetQua</c> mang token của shop KHÁC phải bị BỎ.</b>
    /// <para>Ca thật: shop #1 vừa hết hạn chờ (900s), vòng đi tiếp shop #2, rồi extension mới gửi xong kết quả
    /// của shop #1. Không lọc thì nhật ký ghi "Shop Hai: đã tạo chương trình cho 240 sản phẩm" cho một shop
    /// chưa làm gì — và người dùng không có cách nào biết.</para>
    /// </summary>
    [Fact]
    public async Task GoiKetQuaLacLuot_ThiBiBO_KhongTinhChoShopDangChay()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var chay = ChayVongAsync(rig);

        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = MotShop });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);

        // Gói LẠC (token của shop khác) tới trước — phải bị bỏ...
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "999", data = GoiTao(240) });
        // ...rồi gói ĐÚNG của lượt đang chờ mới là kết quả.
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiRong });

        var kq = await chay;

        Assert.Single(kq.Shops);
        Assert.False(kq.Shops[0].DaTao);
        Assert.Equal(0, kq.Shops[0].SoSanPham);
        Assert.Equal(1, kq.SoBoQua);
    }

    // ══════════ HẠN CHÓT 06:00 của lượt TỰ ĐỘNG ══════════

    /// <summary>
    /// <b>Hết khung giữa vòng ⇒ shop sau KHÔNG chạy, và kết quả nói rõ còn mấy shop.</b>
    /// <para>Lượt tự động khởi động được tới 05:59 mà vòng 12 shop không có mốc dừng nào của riêng nó: với hạn
    /// chặng 900s + nghỉ 3–5 phút mỗi shop, ca xấu chạy tới gần 10h sáng và check đơn chết cả buổi sáng.</para>
    /// <para>Đồng hồ giả nhảy qua hạn chót ngay trong kỳ nghỉ giữa hai shop — đúng chỗ vòng thật tiêu thời gian.
    /// Mệnh đề trung tâm: shop #2 KHÔNG có lệnh nào bay tới.</para>
    /// </summary>
    [Fact]
    public async Task HetKhungGiuaVong_ThiKHONG_bat_dau_shop_moi_VaNoiRoConMayShop()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var hanChot = new DateTime(2026, 8, 19, 6, 0, 0);
        var bayGio = new DateTime(2026, 8, 19, 5, 50, 0);
        var chay = ChayVongAsync(rig, hanChot, () => bayGio,
            nghiGiuaShop: _ => { bayGio = new DateTime(2026, 8, 19, 6, 1, 0); return Task.CompletedTask; });

        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = HaiShop });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiTao(240) });

        var kq = await chay;

        Assert.True(kq.HetKhung);
        Assert.Equal(1, kq.SoShopChuaChay);
        Assert.Single(kq.Shops);
        Assert.Equal(1, kq.SoTao);
        Assert.Null(kq.Loi); // hết khung KHÔNG phải lỗi
        Assert.Null(await ThuNhanThemLenhAsync(rig)); // shop #2 không nhận lệnh nào
    }

    /// <summary>
    /// Vế ĐỐI CHỨNG: cùng kịch bản mà <c>hanChot = null</c> (đường bấm tay) thì vòng chạy HẾT cả hai shop.
    /// Thiếu vế này thì một bản "dừng luôn sau shop đầu" cũng xanh.
    /// </summary>
    [Fact]
    public async Task KhongCoHanChot_ThiChayHetMoiShop()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var chay = ChayVongAsync(rig, hanChot: null, dongHo: () => new DateTime(2026, 8, 19, 9, 0, 0));

        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = HaiShop });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiTao(240) });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "222", data = GoiTao(12) });

        var kq = await chay;

        Assert.False(kq.HetKhung);
        Assert.Equal(0, kq.SoShopChuaChay);
        Assert.Equal(2, kq.SoTao);
    }

    // ══════════ NHƯỜNG CHỖ CHO CHECK ĐƠN (03/09/2026, sổ P-20260903-1 — phản biện vòng 2) ══════════

    /// <summary>
    /// <b>Vòng KM cũng NHƯỜNG CHỖ cho một lượt chạy lại check đơn đang chờ.</b>
    ///
    /// <para><b>Vì sao vòng KM cần cửa này</b> (phản biện vòng 2, 03/09/2026): lượt hoãn chạy lại check đơn
    /// nhường cho <i>bất kỳ</i> lát cắt cầu nối nào (cửa <c>_bridgeRunning</c>), không riêng gói flash — nên một
    /// vòng KM tự động 2h sáng nhận cờ bật mà KHÔNG đọc thì lượt hoãn ngồi hết trần 90 phút rồi <b>bỏ luôn
    /// check đơn</b>, trong khi vòng KM vẫn thong thả đi tiếp 11 shop. Bản đầu chỉ rót cờ cho vòng flash.</para>
    ///
    /// <para>Ba mệnh đề, y bài flash cùng tên: shop đang chạy KHÔNG bị cắt · shop kế KHÔNG được mở ·
    /// <c>SoShopChuaChay = n − 1</c> (cùng hình dạng kết quả với nhánh hết hạn).</para>
    /// </summary>
    [Fact]
    public async Task PhaiNhuongSauShop_ThiVongKmDungSauShopDangChay()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var nhuong = false;
        var chay = ChayVongAsync(rig, phaiNhuongSauShop: () => nhuong);

        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });

        // Shop 1 chạy TRỌN — cờ chỉ bật khi shop này đã xong (đúng như đời thật: lượt hoãn tỉnh dậy giữa vòng).
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        nhuong = true;
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiTao(240) });

        var kq = await chay;

        Assert.True(kq.NhuongChoCheckDon, "kết quả vòng KM không mang cờ 'nhường chỗ cho check đơn'");
        Assert.False(kq.HetKhung);
        Assert.Null(kq.Loi);
        Assert.Single(kq.Shops);            // shop 1 không bị cắt ngang
        Assert.Equal(2, kq.SoShopChuaChay);
        Assert.Equal(3, kq.TongShop);
        Assert.Null(await ThuNhanThemLenhAsync(rig));   // shop 2 KHÔNG được mở
        Assert.True(rig.CoLog("[KM] Nhường chỗ cho check đơn"));
    }

    /// <summary>Vế ĐỐI CHỨNG: cờ TẮT (và <c>null</c>) ⇒ vòng KM chạy hết danh sách. Thiếu nó thì một bản
    /// "luôn nhường" cũng xanh — mà bản đó dừng MỌI vòng KM sau shop đầu, câm lặng.</summary>
    [Fact]
    public async Task PhaiNhuongSauShopTat_ThiVongKmChayHet()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var chay = ChayVongAsync(rig, phaiNhuongSauShop: () => false);

        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = HaiShop });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiTao(240) });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "222", data = GoiTao(12) });

        var kq = await chay;

        Assert.False(kq.NhuongChoCheckDon);
        Assert.Equal(2, kq.SoTao);
        Assert.Equal(0, kq.SoShopChuaChay);
    }

    // ══════════ MỐC BẮT ĐẦU trôi qua GIỮA VÒNG ══════════

    /// <summary>
    /// <b>Mốc bắt đầu của chính chương trình trôi qua giữa vòng ⇒ DỪNG, và câu nói ĐÚNG nguyên nhân.</b>
    /// <para><b>Lỗ phủ mà ca này lấp</b> (phản biện 26/08/2026, lỗi NẶNG): mọi test cũ của cửa kiểm mốc đo hàm
    /// thuần TẠI ĐÚNG MỘT THỜI ĐIỂM, còn hai rig chạy vòng thật thì <c>dongHo</c> ĐỨNG YÊN. Nên cái vế thật sự
    /// nguy hiểm — mốc là một hạn chót ĐỒNG HỒ mà chỉ được kiểm MỘT LẦN, trước vòng — không có thước nào cả.
    /// Vòng 12 shop mất ~1 tiếng; mốc trôi qua ở shop thứ 5 là <b>8 shop cuối đỏ hết</b> với câu
    /// <c>"li '09' đang disabled"</c>, trông y hệt Shopee đổi DOM.</para>
    /// <para>Đồng hồ giả nhảy qua mốc trong kỳ nghỉ SAU shop #2 — đúng chỗ vòng thật tiêu thời gian. Ba mệnh
    /// đề: shop #3 không nhận lệnh nào · <c>SoShopChuaChay</c> = 1 · dòng log loại trừ tường minh cái tên mà
    /// người đọc sẽ tự gán ("lỗi selector").</para>
    /// </summary>
    [Fact]
    public async Task MocBatDauTroiQuaGiuaVong_ThiDUNG_VaNoiRoKhongPhaiLoiSelector()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        // Mốc bắt đầu của `ThamSo` là 25-09-2026 09:30.
        var bayGio = new DateTime(2026, 9, 25, 9, 0, 0);
        var soLanNghi = 0;
        var chay = ChayVongAsync(rig, hanChot: null, () => bayGio,
            nghiGiuaShop: _ =>
            {
                // Nghỉ lần 1 (sau shop #1): vẫn còn trước mốc. Lần 2 (sau shop #2): đã qua mốc.
                soLanNghi++;
                bayGio = soLanNghi >= 2
                    ? new DateTime(2026, 9, 25, 9, 31, 0)
                    : new DateTime(2026, 9, 25, 9, 20, 0);
                return Task.CompletedTask;
            });

        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiTao(240) });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "222", data = GoiTao(12) });

        var kq = await chay;

        Assert.Equal(new DateTime(2026, 9, 25, 9, 30, 0), kq.MocBatDauDaTroiQua);
        Assert.Equal(1, kq.SoShopChuaChay);
        Assert.Equal(3, kq.TongShop);
        Assert.Equal(2, kq.SoTao);

        // Phân biệt RÕ với ba lý do dừng sớm đã có — nhầm tên là người đọc đi tìm sai chỗ.
        Assert.Null(kq.Loi);
        Assert.False(kq.HetKhung);
        Assert.False(kq.Captcha);
        Assert.Equal(0, kq.TranThuDaCham);

        Assert.True(rig.CoLog("MỐC BẮT ĐẦU của chương trình (25-09-2026 09:30) ĐÃ TRÔI QUA GIỮA VÒNG"));
        Assert.True(rig.CoLog("còn 1 shop chưa chạy"));
        Assert.True(rig.CoLog("KHÔNG PHẢI LỖI SELECTOR"));

        Assert.Null(await ThuNhanThemLenhAsync(rig)); // shop #3 không nhận lệnh nào
    }

    /// <summary>
    /// Vế ĐỐI CHỨNG của ca trên: đồng hồ tiến y hệt nhưng KHÔNG vượt mốc ⇒ chạy hết cả ba shop. Thiếu vế này
    /// thì một bản "cứ có <c>mocBatDau</c> là dừng" cũng xanh — mà bản đó giết luôn tính năng.
    /// </summary>
    [Fact]
    public async Task DongHoTienNhungCHUA_quaMoc_ThiChayHetMoiShop()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var bayGio = new DateTime(2026, 9, 25, 9, 0, 0);
        var chay = ChayVongAsync(rig, hanChot: null, () => bayGio,
            nghiGiuaShop: _ => { bayGio = bayGio.AddMinutes(10); return Task.CompletedTask; });

        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = BaShop });
        foreach (var token in new[] { "111", "222", "333" })
        {
            await NhanTenLenhAsync(rig);
            await rig.GuiAsync(new { action = "shopOpened" });
            await NhanTenLenhAsync(rig);
            await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token, data = GoiTao(5) });
        }

        var kq = await chay;

        Assert.Null(kq.MocBatDauDaTroiQua);
        Assert.Equal(0, kq.SoShopChuaChay);
        Assert.Equal(3, kq.SoTao);
    }

    // ══════════ Gói "không có tab shop" — HỎNG, không phải BỎ QUA ══════════

    /// <summary>
    /// <b>Extension không có tab shop (service worker chết giữa chừng) ⇒ shop vào <c>SoHong</c>.</b>
    /// <para>Bản đợt 1 trả <c>boQua</c> cho ca này, nên câu tổng kết nói "bỏ qua N shop không có sản phẩm" cho
    /// một sự cố hạ tầng — người dùng đọc log tưởng shop rỗng thật rồi thôi không điều tra. Và vòng vẫn phải đi
    /// tiếp shop kế: hỏng hạ tầng ở một shop không giết cả lượt.</para>
    /// </summary>
    [Fact]
    public async Task GoiKhongCoTabShop_ThiVaoSoHong_KhongVaoSoBoQua()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var chay = ChayVongAsync(rig);

        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = HaiShop });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiKhongCoTab });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "222", data = GoiTao(12) });

        var kq = await chay;

        Assert.Equal(1, kq.SoHong);
        Assert.Equal(0, kq.SoBoQua);
        Assert.Contains("0. Tab shop", kq.Shops[0].Loi);
        Assert.Equal(1, kq.SoTao); // vòng vẫn đi tiếp shop kế
    }

    // ══════════ Captcha khai TRONG gói kết quả ══════════

    /// <summary>
    /// <b>Gói kết quả tự khai <c>captcha:true</c> cũng phải DỪNG CẢ VÒNG</b>, không cần gói
    /// <c>{action:"captcha"}</c> đi kèm.
    /// <para>Vì sao cần đường thứ hai: hai gói đi hai lượt bơm WebSocket. Gói <c>captcha</c> riêng hoàn toàn có
    /// thể hạ cánh SAU lượt C# đã đọc xong kết quả và đi tiếp shop kế — ca captcha giữa bước 6 (chặng dài ~12
    /// phút) là ca dễ vướng nhất. Test này dựng đúng trạng thái đó: rig CHỈ gửi gói kết quả.</para>
    /// </summary>
    [Fact]
    public async Task GoiKetQuaTuKhaiCaptcha_ThiDUNG_CA_VONG()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        var chay = ChayVongAsync(rig);

        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = HaiShop });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "shopOpened" });
        await NhanTenLenhAsync(rig);
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiCaptchaTrongGoi });

        var kq = await chay;

        Assert.True(kq.Captcha);
        Assert.Contains("captcha", kq.Loi ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Single(kq.Shops);
        Assert.Null(await ThuNhanThemLenhAsync(rig)); // KHÔNG đụng tới shop #2
    }

    // ══════════ Hạ tầng chung ══════════

    /// <summary>
    /// Chạy vòng KM với hai móc GIẢ: "về picker" luôn thành công, "nghỉ giữa shop" không tốn thời gian.
    /// <para>Cố ý KHÔNG giả lập chặng <c>closeShopTab</c>: nó thuộc <c>ShopFlowRunner</c> và đã có bộ test
    /// riêng; ở đây ta khoá phần vòng KM tự thêm vào.</para>
    /// </summary>
    /// <summary>
    /// Mốc "bây giờ" mặc định của mọi ca trong file — <b>TRƯỚC</b> mốc bắt đầu của <see cref="ThamSo"/>
    /// (25-09-2026 09:30) nên cửa "mốc bắt đầu đã trôi qua" không nổ.
    /// <para><b>Cố định, KHÔNG dùng <c>DateTime.Now</c></b> (26/08/2026): từ khi vòng kiểm lại mốc bắt đầu
    /// trước mỗi shop, một đồng hồ thật biến cả file này thành bom hẹn giờ — mọi ca sẽ đỏ hàng loạt kể từ
    /// 25-09-2026 09:30, và triệu chứng sẽ trông như một bản vá làm hỏng vòng KM.</para>
    /// </summary>
    private static readonly DateTime TruocMocBatDau = new(2026, 9, 25, 8, 0, 0);

    private static Task<KetQuaKm> ChayVongAsync(
        BridgeTestRig rig, DateTime? hanChot = null, Func<DateTime>? dongHo = null,
        Func<CancellationToken, Task>? nghiGiuaShop = null, Func<bool>? phaiNhuongSauShop = null)
        => OrdersBridgeSession.ChayKmSauPickerAsync(
            rig.Channel, null, rig.Log, ThamSo,
            vePicker: _ => Task.FromResult(KetQuaVePicker.VeDuoc),
            nghiGiuaShop: nghiGiuaShop ?? (_ => Task.CompletedTask),
            hanChot: hanChot,
            dongHo: dongHo ?? (() => TruocMocBatDau),
            ct: CancellationToken.None,
            phaiNhuongSauShop: phaiNhuongSauShop);

    private static async Task<string> NhanTenLenhAsync(BridgeTestRig rig)
    {
        using var l = await rig.NhanLenhAsync();
        return l.RootElement.GetProperty("action").GetString() ?? "";
    }

    /// <summary>Chờ xem còn lệnh nào nữa không; hết giờ ⇒ null. Một lượt duy nhất — lượt hết hạn ABORT socket
    /// của rig (ghi chú cùng khuôn ở <c>MoListShopTests</c>).</summary>
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
}
