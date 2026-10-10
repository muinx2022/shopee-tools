using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>Vòng "Combo Khuyến Mãi" chạy trên CẦU NỐI THẬT</b> (<see cref="BridgeTestRig"/> đóng vai extension) —
/// bản đối xứng của <see cref="KmVongRigTests"/> cho vòng combo.
/// <para><b>Lỗ phủ mà file này lấp</b> (phản biện 25/08/2026, VỪA-2): <c>grep</c> cả repo cho
/// <c>ChayComboSauPickerAsync</c> · <c>ChayComboMoiShopAsync</c> · chuỗi <c>"chayCombo"</c> ra <b>0 lần trong
/// test</b>. Phiên chính ĐÃ ĐO: đổi <c>action: "chayCombo"</c> thành <c>"chayKm"</c> mà <b>2131/2131 test vẫn
/// XANH</b>. Đột biến đó biến lượt combo 6h sáng thành lượt tạo <b>Chương Trình Của Shop THẬT</b> trên MỌI
/// shop — và nó chỉ lộ ra khi chặng B lên, tức khi nó đã tạo khuyến mãi thật rồi.</para>
/// <para>Vì thế test ở đây khẳng định <b>ĐÚNG BẰNG</b> chứ không phải "có chứa": chuỗi lệnh, token, và
/// <b>từng trường một</b> của gói <see cref="ThamSoCombo.Goi"/>. Gửi <c>new { }</c> hay gửi gói
/// của vòng kia đều phải ĐỎ.</para>
/// <para>Chỉ chạy được khúc SAU trang chọn shop (bước SSO phóng trình duyệt thật); hai móc "về picker" và
/// "nghỉ 3–5 phút" rót từ ngoài, y khuôn <see cref="KmVongRigTests"/>.</para>
/// </summary>
public class ComboVongRigTests
{
    /// <summary>Gói combo mặc định của bảng §A2, chạy một KHOẢNG 180 ngày như lượt tự động thật:
    /// <b>19-08-2026 → 15-02-2027</b>, CÙNG giờ 11:00 hai đầu. Hai mốc khác tháng khác năm.</summary>
    private static readonly ThamSoCombo ThamSo =
        new("Combo 19-08-2026", new DateTime(2026, 8, 19), new DateTime(2027, 2, 15), "11:00",
            Loai: 3, MuaSl: 2, Giam: 2000, GioiHan: 3);

    private const string MotShop = """[{"rowKey":"111","name":"Shop Một","login":"shop.mot"}]""";

    private const string HaiShop =
        """[{"rowKey":"111","name":"Shop Một","login":"shop.mot"},""" +
        """{"rowKey":"222","name":"Shop Hai","login":"shop.hai"}]""";

    private static string GoiTao(int soSp) =>
        $$"""{"soSanPham":{{soSp}},"daTao":true,"boQua":null,"canhBao":[],"buocHong":[]}""";

    /// <summary>Gói HỎNG của luồng combo: bước điền mức giảm đỏ vì cú click radio "số tiền" không ăn — đúng ca
    /// mà plan §5 dặn <c>flow-combo.js</c> phải chặn (gõ 2000 vào ô <c>discountPercentage</c> là khai giảm
    /// 2000%).</summary>
    private const string GoiHong =
        """{"soSanPham":0,"daTao":false,"boQua":null,"canhBao":[],"buocHong":[{"ten":"5. Mức ưu đãi","loi":"chưa thấy input[name='tiers.0.discountValue'] — cú click radio value=3 không ăn"}]}""";

    // ══════════ Ca (a) — shop tạo được: chuỗi lệnh + TỪNG TRƯỜNG của gói ══════════

    /// <summary>
    /// <b>Mệnh đề trung tâm của cả file:</b> một shop tạo được ⇒ chuỗi lệnh ĐÚNG BẰNG
    /// <c>[readShopList, openShopDetail, <b>chayCombo</b>]</c>, <c>token</c> = <c>shopId</c>, và gói
    /// <c>thamSo</c> mang ĐỦ mọi trường của <see cref="ThamSoCombo.Goi"/> với ĐÚNG giá trị.
    /// <para>Đo từng trường chứ không đo cả cụm: thiếu <c>thang</c>/<c>nam</c> là extension click trúng ngày 15
    /// của tháng đang hiện trên picker; sai <c>loai</c> là điền số tiền vào ô phần trăm.</para>
    /// </summary>
    [Fact]
    public async Task CaA_ShopTaoDuoc_ChuoiLenhDungBang_VaGoiThamSoDuTungTruong()
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
            Assert.Equal("Combo 19-08-2026", ts.GetProperty("ten").GetString());

            // HAI MỐC, từng trường một — bản vá gửi nhầm một mốc cho cả hai ô chỉ đỏ ở đây.
            var bd = ts.GetProperty("bd");
            Assert.Equal(19, bd.GetProperty("ngayTrongThang").GetInt32());
            Assert.Equal(8, bd.GetProperty("thang").GetInt32());
            Assert.Equal(2026, bd.GetProperty("nam").GetInt32());
            Assert.Equal("19-08-2026", bd.GetProperty("ngayDayDu").GetString());

            var kt = ts.GetProperty("kt");
            Assert.Equal(15, kt.GetProperty("ngayTrongThang").GetInt32());
            Assert.Equal(2, kt.GetProperty("thang").GetInt32());
            Assert.Equal(2027, kt.GetProperty("nam").GetInt32());
            Assert.Equal("15-02-2027", kt.GetProperty("ngayDayDu").GetString());

            Assert.Equal("11:00", ts.GetProperty("gio").GetString());
            Assert.False(ts.TryGetProperty("gioKetThuc", out _), "gói còn sót trường 'gioKetThuc' đã bỏ");
            Assert.Equal(3, ts.GetProperty("loai").GetInt32());
            Assert.Equal(2, ts.GetProperty("muaSl").GetInt32());
            Assert.Equal(2000, ts.GetProperty("giam").GetInt32());
            Assert.Equal(3, ts.GetProperty("gioiHan").GetInt32());

            // …và KHÔNG mang trường của vòng kia: gửi nhầm gói `ThamSoKm` thì `phanTram` có mặt.
            Assert.False(ts.TryGetProperty("phanTram", out _));
        }
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "111", data = GoiTao(240) });

        var kq = await chay;

        Assert.Equal(new[] { "readShopList", "openShopDetail", "chayCombo" }, lenhDaGui);
        Assert.Single(kq.Shops);
        Assert.True(kq.Shops[0].DaTao);
        Assert.Equal(240, kq.Shops[0].SoSanPham);
        Assert.Equal(1, kq.SoTao);
        Assert.False(kq.Captcha);
        Assert.Null(kq.Loi);
        Assert.Null(await ThuNhanThemLenhAsync(rig));
    }

    // ══════════ Ca (b) — shop HỎNG: ghi HỎNG rồi đi tiếp, vẫn ĐÚNG lệnh `chayCombo` ══════════

    /// <summary>
    /// Shop #1 hỏng ở bước mức ưu đãi ⇒ ghi HỎNG (nêu đích danh bước) rồi vòng vẫn chạy shop #2 — và cả hai
    /// lượt đều phải là <c>chayCombo</c>, kể cả lượt sau lượt hỏng.
    /// <para>Vế "kể cả lượt sau" không thừa: chuỗi lệnh dài ra là chỗ một bản chép nhầm dễ lọt nhất.</para>
    /// </summary>
    [Fact]
    public async Task CaB_ShopHong_GhiHONG_DiTiepShopKe_VaVanLaLenhChayCombo()
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

        using (var l = await rig.NhanLenhAsync())
        {
            var root = l.RootElement;
            lenhDaGui.Add(root.GetProperty("action").GetString() ?? "");
            Assert.Equal("222", root.GetProperty("token").GetString());
            // Gói tham số dựng MỘT LẦN, dùng lại nguyên vẹn cho mọi shop.
            Assert.Equal(2000, root.GetProperty("thamSo").GetProperty("giam").GetInt32());
        }
        await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = "222", data = GoiTao(12) });

        var kq = await chay;

        Assert.Equal(
            new[] { "readShopList", "openShopDetail", "chayCombo", "openShopDetail", "chayCombo" },
            lenhDaGui);
        Assert.Equal(2, kq.Shops.Count);
        Assert.Equal(1, kq.SoHong);
        Assert.Equal(1, kq.SoTao);
        Assert.Contains("5. Mức ưu đãi", kq.Shops[0].Loi);
        Assert.True(kq.Shops[1].DaTao);
        Assert.Null(kq.Loi); // vòng chạy HẾT, không phải dừng giữa chừng
    }

    // ══════════ Ca (c) — TRẦN SỐ SHOP của CHẾ ĐỘ THỬ (26/08/2026) ══════════

    /// <summary>
    /// <b>Đường PRODUCTION của vòng combo dừng đúng sau <see cref="KmLichTuDong.TranShopLuotCombo"/> shop</b>,
    /// và nói ra lý do.
    /// <para><b>Đo đúng đường thật:</b> gọi <c>ChayComboSauPickerAsync</c> mà KHÔNG truyền <c>tranShop</c> — y
    /// hệt <c>ChayComboMoiShopAsync</c> trong production. Truyền số vào tay là chỉ đo tham số, không đo được
    /// chỗ nối giữa hằng và vòng chạy.</para>
    /// <para>Người dùng chốt 26/08/2026: <i>"khi test thì chỉ chạy hết 1 - 2 shop thì dừng, không chạy đủ 12"</i>.
    /// Trần áp cho CẢ đường bấm tay lẫn lịch tự động 06–08h vì cả hai đi chung hàm này.</para>
    /// </summary>
    [Fact]
    public async Task CaC_ChayDungSoShopCuaTran_RoiDUNG_VaNoiRaLyDo()
    {
        // TRẦN RÓT TAY, KHÔNG đọc hằng. `TranShopLuotCombo` là công tắc giai đoạn thử: người dùng đặt nó về 0
        // để chạy full list, và một test đọc thẳng hằng sẽ ĐỎ OAN đúng lúc đó rồi bị xoá đi — mất luôn lưới của
        // CƠ CHẾ trần. Vế "đường production dùng đúng hằng" đo riêng ở `CaC_DuongProduction_DungDungHang`.
        const int tran = 2;
        await using var rig = await BridgeTestRig.StartAsync();
        var chay = ChayVongAsync(rig, tranShop: tran);

        Assert.Equal("readShopList", await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = DsShop(SoShopMau) });
        await NuoiShopAsync(rig, tran, "chayCombo");

        var kq = await chay;

        // Dừng ĐÚNG chỗ: không một lệnh nào nữa được gửi xuống cho shop thứ `tran + 1`.
        Assert.Null(await ThuNhanThemLenhAsync(rig));
        Assert.Equal(tran, kq.Shops.Count);
        Assert.Equal(tran, kq.SoTao);
        Assert.Equal(SoShopMau - tran, kq.SoShopChuaChay);
        Assert.Equal(SoShopMau, kq.TongShop);
        Assert.Equal(tran, kq.TranThuDaCham);

        // …và KHÔNG đội lốt hai lý do dừng sớm đã có.
        Assert.False(kq.HetKhung);
        Assert.False(kq.Captcha);
        Assert.Null(kq.Loi);

        // DỪNG SỚM PHẢI NÓI RA — cả trong nhật ký lượt chạy lẫn trong câu tổng kết người dùng đọc.
        Assert.True(rig.CoLog("CHẾ ĐỘ THỬ"), "nhật ký lượt chạy KHÔNG nói gì về việc dừng sớm");
        Assert.True(rig.CoLog($"trần {tran} shop"), "nhật ký lượt chạy không nêu con số trần");

        var tongKet = AccountsViewModel.CauTongKetCombo(kq, coPhienChayLai: true);
        Assert.Contains($"tạo {tran}/{SoShopMau} shop", tongKet);
        Assert.Contains("CHẾ ĐỘ THỬ", tongKet);
        Assert.Contains($"trần {tran} shop mỗi lượt", tongKet);
        Assert.Contains($"còn {SoShopMau - tran} shop chưa chạy", tongKet);
        // …và KHÔNG đội lốt hai lý do dừng sớm kia. So trên ĐÚNG mệnh đề của chúng, không so trên mỗi cụm
        // "hết khung": chính câu chế độ thử có vế "cũng không phải do hết khung", và vế đó phải được giữ.
        Assert.DoesNotContain("DỪNG vì hết khung", tongKet);
        Assert.DoesNotContain("DỪNG GIỮA CHỪNG vì rơi captcha", tongKet);
    }

    /// <summary>
    /// <b>KHÔNG NGHỈ khi không còn shop nào SẼ chạy</b> — kỳ nghỉ 3–5 phút chỉ có nghĩa giữa hai shop THẬT.
    /// <para><b>Ca thật 16:51→16:56 ngày 26/08/2026.</b> Trần 2 shop; shop 2 xong lúc 16:51:52 nhưng vòng vẫn
    /// nằm nghỉ tới 16:56:28 mới in dòng "DỪNG theo trần". Bốn phút rưỡi đó không chạy gì — mà khoá xuyên máy
    /// chưa nhả, nên check đơn ở MỌI máy vẫn đang bị dừng. Kỳ nghỉ thừa ở đây đắt hơn hẳn một kỳ nghỉ thường.</para>
    /// <para>Nguyên nhân: trần được kiểm ở ĐẦU vòng lặp, còn kỳ nghỉ chạy ở CUỐI thân vòng — nên nó luôn kịp
    /// nghỉ một lần nữa trước khi cửa trần nổ.</para>
    /// </summary>
    [Fact]
    public async Task CaC_ChamTran_ThiKHONG_nghi_them_mot_ky_nua()
    {
        const int tran = 2;
        var soLanNghi = 0;

        await using var rig = await BridgeTestRig.StartAsync();
        var chay = ChayVongAsync(rig, nghiGiuaShop: _ => { soLanNghi++; return Task.CompletedTask; }, tranShop: tran);

        Assert.Equal("readShopList", await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = DsShop(SoShopMau) });
        await NuoiShopAsync(rig, tran, "chayCombo");

        var kq = await chay;

        Assert.Equal(tran, kq.Shops.Count);
        // Nghỉ ĐÚNG (tran - 1) lần: chỉ giữa hai shop thật chạy. Bản trước nghỉ `tran` lần — lần cuối là thừa.
        Assert.Equal(tran - 1, soLanNghi);
    }

    /// <summary>
    /// <b>Đường PRODUCTION dùng đúng hằng <see cref="KmLichTuDong.TranShopLuotCombo"/></b> — vế này là thứ
    /// chặn một bản "quên truyền trần xuống vòng lặp", mà hai test kia (rót tay) sẽ không thấy.
    /// <para>Sống được ở CẢ HAI trạng thái công tắc: trần &gt; 0 thì dừng đúng số đó; trần = 0 thì chạy hết.</para>
    /// </summary>
    [Fact]
    public async Task CaC_DuongProduction_DungDungHang()
    {
        var tran = KmLichTuDong.TranShopLuotCombo;
        var soSeChay = tran > 0 ? Math.Min(tran, SoShopMau) : SoShopMau;

        await using var rig = await BridgeTestRig.StartAsync();
        var chay = ChayVongAsync(rig);   // KHÔNG truyền tranShop ⇒ đi đúng đường production

        Assert.Equal("readShopList", await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = DsShop(SoShopMau) });
        await NuoiShopAsync(rig, soSeChay, "chayCombo");

        var kq = await chay;

        Assert.Equal(soSeChay, kq.Shops.Count);
        Assert.Equal(tran > 0 ? tran : 0, kq.TranThuDaCham);
    }

    /// <summary>
    /// <b>Vế ĐỐI CHỨNG — trần <c>0</c> (đã xong giai đoạn thử) ⇒ chạy ĐỦ cả danh sách shop.</b>
    /// <para>Thiếu vế này thì một bản "luôn dừng sau 2 shop" cũng xanh ở test trên, và lúc bỏ trần thật thì vòng
    /// combo vĩnh viễn chỉ chạy 2 shop đầu — im lặng, vì câu tổng kết vẫn nói đúng con số nó làm.</para>
    /// </summary>
    [Fact]
    public async Task CaC_TranBang0_ThiChayDuCaDanhSachShop()
    {
        await using var rig = await BridgeTestRig.StartAsync();
        var chay = ChayVongAsync(rig, tranShop: 0);

        Assert.Equal("readShopList", await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = DsShop(SoShopMau) });
        await NuoiShopAsync(rig, SoShopMau, "chayCombo");

        var kq = await chay;

        Assert.Equal(SoShopMau, kq.Shops.Count);
        Assert.Equal(SoShopMau, kq.SoTao);
        Assert.Equal(0, kq.SoShopChuaChay);
        Assert.Equal(0, kq.TranThuDaCham);
        Assert.DoesNotContain("CHẾ ĐỘ THỬ", AccountsViewModel.CauTongKetCombo(kq, coPhienChayLai: true));
    }

    // ══════════ MỐC BẮT ĐẦU trôi qua GIỮA VÒNG (26/08/2026) ══════════

    /// <summary>
    /// <b>Bản đối xứng của <c>KmVongRigTests.MocBatDauTroiQuaGiuaVong_…</c> cho vòng COMBO</b> — mốc bắt đầu
    /// trôi qua giữa vòng ⇒ DỪNG, câu nói đúng nguyên nhân, <c>SoShopChuaChay</c> đúng.
    /// <para><b>Phải có bản riêng cho vòng này</b> dù hai vòng đi chung một hàm: chỗ nối là
    /// <c>ThamSoCombo.MocBatDau()</c>, một hàm KHÁC với bản của <c>ThamSoKm</c>. Một bản vá quên nối ở đây thì
    /// đúng vòng combo mất cửa chặn — mà nó là vòng đang ở giai đoạn thử, tức vòng ít ai soi nhật ký nhất.</para>
    /// <para>Trần chế độ thử = 2 shop, nên ca này rót <c>tranShop: 0</c> để đo được cửa mốc chứ không bị trần
    /// cắt trước.</para>
    /// </summary>
    [Fact]
    public async Task MocBatDauTroiQuaGiuaVong_ThiDUNG_VaNoiRoKhongPhaiLoiSelector()
    {
        await using var rig = await BridgeTestRig.StartAsync();

        // Mốc bắt đầu của `ThamSo` là 19-08-2026 11:00.
        var bayGio = new DateTime(2026, 8, 19, 10, 30, 0);
        var soLanNghi = 0;
        var chay = ChayVongAsync(rig, hanChot: null, () => bayGio,
            nghiGiuaShop: _ =>
            {
                soLanNghi++;
                bayGio = soLanNghi >= 2
                    ? new DateTime(2026, 8, 19, 11, 1, 0)
                    : new DateTime(2026, 8, 19, 10, 45, 0);
                return Task.CompletedTask;
            },
            tranShop: 0);

        Assert.Equal("readShopList", await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = DsShop(SoShopMau) });
        await NuoiShopAsync(rig, 2, "chayCombo");

        var kq = await chay;

        Assert.Equal(new DateTime(2026, 8, 19, 11, 0, 0), kq.MocBatDauDaTroiQua);
        Assert.Equal(SoShopMau - 2, kq.SoShopChuaChay);
        Assert.Equal(SoShopMau, kq.TongShop);
        Assert.Equal(2, kq.SoTao);

        Assert.Null(kq.Loi);
        Assert.False(kq.HetKhung);
        Assert.False(kq.Captcha);
        Assert.Equal(0, kq.TranThuDaCham);

        Assert.True(rig.CoLog("MỐC BẮT ĐẦU của chương trình (19-08-2026 11:00) ĐÃ TRÔI QUA GIỮA VÒNG"));
        Assert.True(rig.CoLog("KHÔNG PHẢI LỖI SELECTOR"));
        Assert.Null(await ThuNhanThemLenhAsync(rig));

        // Câu người dùng đọc cũng phải nói đúng tên, và LOẠI TRỪ ba lý do dừng sớm kia.
        var tongKet = AccountsViewModel.CauTongKetCombo(kq, coPhienChayLai: true);
        Assert.Contains("MỐC BẮT ĐẦU của chương trình (11:00) đã TRÔI QUA GIỮA VÒNG", tongKet);
        Assert.Contains($"còn {SoShopMau - 2} shop chưa chạy", tongKet);
        Assert.Contains("KHÔNG phải lỗi selector", tongKet);
        Assert.DoesNotContain("DỪNG vì hết khung", tongKet);
        Assert.DoesNotContain("CHẾ ĐỘ THỬ", tongKet);
    }

    /// <summary>
    /// <b>Trần chế độ thử KHÔNG chạm tới vòng KM.</b> Vòng KM đang chạy THẬT và ổn định — người dùng chốt
    /// 26/08/2026 là chỉ hãm vòng combo.
    /// <para>Đo trên chính <c>ChayKmSauPickerAsync</c> với cùng <see cref="SoShopMau"/> shop: đủ cả 5 shop nhận
    /// lệnh <c>chayKm</c>, <c>TranThuDaCham</c> = 0. Một bản đặt trần vào vòng lặp DÙNG CHUNG (thay vì truyền
    /// riêng cho combo) sẽ hãm cả vòng KM và làm test này đỏ.</para>
    /// </summary>
    [Fact]
    public async Task VongKM_KhongBiTranCheDoThu_ChayDuCaDanhSachShop()
    {
        await using var rig = await BridgeTestRig.StartAsync();
        var chay = OrdersBridgeSession.ChayKmSauPickerAsync(
            rig.Channel, null, rig.Log,
            new ThamSoKm("KM 19-08-2026", new DateTime(2026, 8, 19), new DateTime(2027, 2, 15), "11:00",
                         PhanTram: 25),
            vePicker: _ => Task.FromResult(KetQuaVePicker.VeDuoc),
            nghiGiuaShop: _ => Task.CompletedTask,
            hanChot: null,
            dongHo: () => TruocMocBatDau,
            ct: CancellationToken.None);

        Assert.Equal("readShopList", await NhanTenLenhAsync(rig));
        await rig.GuiAsync(new { action = "pageData", kind = "shopList", data = DsShop(SoShopMau) });
        await NuoiShopAsync(rig, SoShopMau, "chayKm");

        var kq = await chay;

        Assert.Equal(SoShopMau, kq.Shops.Count);
        Assert.Equal(SoShopMau, kq.SoTao);
        Assert.Equal(0, kq.TranThuDaCham);
        Assert.Equal(0, kq.SoShopChuaChay);
    }

    // ══════════ Hạ tầng chung (y khuôn KmVongRigTests) ══════════

    /// <summary>Số shop của các ca đo TRẦN — phải LỚN HƠN trần để phân biệt được "dừng vì trần" với "hết
    /// shop".</summary>
    private const int SoShopMau = 5;

    /// <summary>
    /// Mốc "bây giờ" mặc định của mọi ca trong file — <b>TRƯỚC</b> mốc bắt đầu của <see cref="ThamSo"/>
    /// (19-08-2026 11:00) nên cửa "mốc bắt đầu đã trôi qua" không nổ.
    /// <para><b>Cố định, KHÔNG dùng <c>DateTime.Now</c></b> (26/08/2026, y khuôn
    /// <see cref="KmVongRigTests"/>): mốc của <see cref="ThamSo"/> nằm ở QUÁ KHỨ so với đồng hồ thật, nên một
    /// <c>DateTime.Now</c> ở đây làm mọi ca của file dừng ngay ở shop đầu — và triệu chứng trông như bản vá
    /// làm hỏng vòng combo.</para>
    /// </summary>
    private static readonly DateTime TruocMocBatDau = new(2026, 8, 19, 9, 0, 0);

    /// <summary>Danh sách <paramref name="n"/> shop dạng JSON y khuôn extension trả về.</summary>
    private static string DsShop(int n)
        => "[" + string.Join(",", Enumerable.Range(1, n)
            .Select(i => $$"""{"rowKey":"{{i}}00","name":"Shop {{i}}","login":"shop.{{i}}"}""")) + "]";

    /// <summary>Đóng vai extension cho <paramref name="soLuot"/> shop liên tiếp: nhận
    /// <c>openShopDetail</c> → báo mở xong → nhận <paramref name="lenhMongDoi"/> → trả gói tạo được.</summary>
    private static async Task NuoiShopAsync(BridgeTestRig rig, int soLuot, string lenhMongDoi)
    {
        for (var i = 1; i <= soLuot; i++)
        {
            Assert.Equal("openShopDetail", await NhanTenLenhAsync(rig));
            await rig.GuiAsync(new { action = "shopOpened" });
            Assert.Equal(lenhMongDoi, await NhanTenLenhAsync(rig));
            await rig.GuiAsync(new { action = "pageData", kind = "kmKetQua", token = $"{i}00", data = GoiTao(10) });
        }
    }

    /// <param name="tranShop"><c>null</c> = <b>KHÔNG truyền tham số</b>, tức chạy đúng đường production (giá trị
    /// mặc định của tham số là <see cref="KmLichTuDong.TranShopLuotCombo"/>). Truyền số = rót tay, chỉ dùng cho
    /// vế đối chứng "đã bỏ trần".</param>
    private static Task<KetQuaKm> ChayVongAsync(
        BridgeTestRig rig, DateTime? hanChot = null, Func<DateTime>? dongHo = null,
        Func<CancellationToken, Task>? nghiGiuaShop = null, int? tranShop = null)
        => tranShop is int tran
            ? OrdersBridgeSession.ChayComboSauPickerAsync(
                rig.Channel, null, rig.Log, ThamSo,
                vePicker: _ => Task.FromResult(KetQuaVePicker.VeDuoc),
                nghiGiuaShop: nghiGiuaShop ?? (_ => Task.CompletedTask),
                hanChot: hanChot,
                dongHo: dongHo ?? (() => TruocMocBatDau),
                ct: CancellationToken.None,
                tranShop: tran)
            : OrdersBridgeSession.ChayComboSauPickerAsync(
                rig.Channel, null, rig.Log, ThamSo,
                vePicker: _ => Task.FromResult(KetQuaVePicker.VeDuoc),
                nghiGiuaShop: nghiGiuaShop ?? (_ => Task.CompletedTask),
                hanChot: hanChot,
                dongHo: dongHo ?? (() => TruocMocBatDau),
                ct: CancellationToken.None);

    private static async Task<string> NhanTenLenhAsync(BridgeTestRig rig)
    {
        using var l = await rig.NhanLenhAsync();
        return l.RootElement.GetProperty("action").GetString() ?? "";
    }

    /// <summary>Chờ xem còn lệnh nào nữa không; hết giờ ⇒ null (xem ghi chú cùng khuôn ở
    /// <see cref="KmVongRigTests"/>).</summary>
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
