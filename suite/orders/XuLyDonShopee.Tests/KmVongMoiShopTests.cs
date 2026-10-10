using System;
using System.Collections.Generic;
using System.Linq;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// PHA 3 "KM của Shop" — <b>vòng chạy qua mọi shop</b>. Phần kiểm được bằng test là hai chỗ mà một lỗi âm thầm
/// sẽ đi thẳng vào nhật ký người dùng và không ai phát hiện:
/// <list type="number">
/// <item>đọc gói kết quả extension gửi về (<see cref="OrdersBridgeSession.DocKetQuaKmMotShop"/>) — phân biệt
/// TẠO ĐƯỢC / BỎ QUA CÓ CHỦ ĐÍCH / HỎNG;</item>
/// <item>dựng câu tổng kết (<see cref="AccountsViewModel.CauTongKetKm"/>).</item>
/// </list>
/// <para><b>Vì sao ĐÁNG test dù chỉ là chuỗi chữ:</b> đêm 18→19/08 có NĂM phép đọc-lại báo "ok"/"hỏng" sai sự
/// thật (xem plan §9). Ca nguy hiểm nhất ở đây cùng họ: shop RỖNG mà bị tính thành HỎNG, hoặc ngược lại shop
/// hỏng bị nuốt thành "bỏ qua" — cả hai đều làm người dùng tin nhầm là 12 shop đã chạy xong.</para>
/// </summary>
public class KmVongMoiShopTests
{
    private static readonly ShopListItem Shop = new("111", "Shop Một", "shop.mot");

    // ── Đọc gói kết quả ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DocKetQua_TaoDuoc_ThiDaTaoVaCoSoSanPham()
    {
        var kq = OrdersBridgeSession.DocKetQuaKmMotShop(
            """{"soSanPham":240,"daTao":true,"boQua":null,"buocHong":[]}""", Shop);

        Assert.True(kq.DaTao);
        Assert.Equal(240, kq.SoSanPham);
        Assert.Null(kq.BoQua);
        Assert.Null(kq.Loi);
    }

    [Fact]
    public void DocKetQua_ShopRong_LaBoQua_KHONG_phai_loi()
    {
        // Ca này là lý do chính của bộ test: shop không có sản phẩm là chuyện BÌNH THƯỜNG (người dùng chốt
        // "không có sản phẩm ⇒ bỏ shop đó"). Tính nhầm thành lỗi ⇒ nhật ký báo "HỎNG 5 shop" cho một lượt chạy
        // hoàn toàn đúng, và người dùng sẽ đi tìm lỗi không tồn tại.
        var kq = OrdersBridgeSession.DocKetQuaKmMotShop(
            """{"soSanPham":0,"daTao":false,"boQua":"shop KHÔNG có sản phẩm nào — bỏ qua, không tạo chương trình"}""", Shop);

        Assert.False(kq.DaTao);
        Assert.NotNull(kq.BoQua);
        Assert.Null(kq.Loi);
    }

    [Fact]
    public void DocKetQua_KhongTaoDuocVaKhongBoQua_ThiLaLoi_VaNeuDichDanhBuocHong()
    {
        var kq = OrdersBridgeSession.DocKetQuaKmMotShop(
            """{"soSanPham":12,"daTao":false,"boQua":null,"buocHong":[{"ten":"9. Cuộn + Xác nhận thanh đáy (TẠO THẬT)","loi":"cuộn 12 nhịp vẫn KHÔNG thấy nút Xác nhận thanh đáy"}]}""",
            Shop);

        Assert.False(kq.DaTao);
        Assert.Null(kq.BoQua);
        Assert.NotNull(kq.Loi);
        // Phải nêu ĐÍCH DANH bước hỏng — "không tạo được" chung chung thì lượt sau lại phải dò lại từ đầu.
        Assert.Contains("9. Cuộn", kq.Loi);
        Assert.Contains("KHÔNG thấy nút Xác nhận", kq.Loi);
    }

    [Fact]
    public void DocKetQua_KhongCoBuocHong_ThiVanBaoLoi_ChuKhongImLang()
    {
        var kq = OrdersBridgeSession.DocKetQuaKmMotShop("""{"soSanPham":3,"daTao":false,"boQua":null}""", Shop);

        Assert.False(kq.DaTao);
        Assert.NotNull(kq.Loi);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ khong phai json }")]
    public void DocKetQua_GoiHongHoacRong_ThiLaLoi_KHONG_ném(string? json)
    {
        // Extension chết giữa chừng / trả rác: phải thành LỖI CÓ TÊN của shop đó, không được ném ra ngoài làm
        // chết cả vòng 12 shop.
        var kq = OrdersBridgeSession.DocKetQuaKmMotShop(json, Shop);

        Assert.False(kq.DaTao);
        Assert.NotNull(kq.Loi);
        Assert.Equal("111", kq.ShopId);
    }

    // ── Cảnh báo: tạo được NHƯNG có thứ đáng ngờ ──────────────────────────────────────────────────────

    /// <summary>
    /// <b>Chạm trần phân trang phải NỔI LÊN thành cảnh báo, không được nuốt.</b>
    /// <para>Trần modal là 105 trang (1000 SP ÷ 10 SP/trang). Chạm trần nghĩa là shop CÒN sản phẩm chưa được
    /// chọn — chương trình vẫn tạo được nên KHÔNG bước nào đỏ, và nếu không có cảnh báo thì người dùng sẽ
    /// không bao giờ biết mình vừa tạo một chương trình thiếu hàng.</para>
    /// </summary>
    [Fact]
    public void DocKetQua_ChamTranPhanTrang_ThiCoCanhBao_MaVanLaTaoDuoc()
    {
        var kq = OrdersBridgeSession.DocKetQuaKmMotShop(
            """{"soSanPham":1000,"daTao":true,"boQua":null,"buocHong":[],"canhBao":["chạm trần 105 trang trong modal chọn sản phẩm — shop có thể còn sản phẩm CHƯA được chọn"]}""",
            Shop);

        Assert.True(kq.DaTao);
        Assert.Null(kq.Loi);
        Assert.NotNull(kq.CanhBao);
        Assert.Contains("chạm trần", kq.CanhBao);
    }

    /// <summary>
    /// <b>PHÒNG THỦ: báo TẠO ĐƯỢC mà vẫn kèm bước hỏng thì phải nói ra.</b>
    /// <para>Luồng mới dừng shop ngay khi có bước hỏng nên ca này KHÔNG được phép xảy ra — lọt tới đây nghĩa là
    /// chốt kia đã vỡ. Nuốt im lặng (bản trước chỉ đọc <c>buocHong</c> khi <c>daTao == false</c>) thì không ai
    /// biết chương trình vừa tạo có đúng giờ/đúng % hay không.</para>
    /// </summary>
    [Fact]
    public void DocKetQua_DaTaoMaVanCoBuocHong_ThiCanhBao_ChuKhongNuot()
    {
        var kq = OrdersBridgeSession.DocKetQuaKmMotShop(
            """{"soSanPham":240,"daTao":true,"boQua":null,"buocHong":[{"ten":"4. Đặt thời gian BẮT ĐẦU","loi":"lệch tháng"}]}""",
            Shop);

        Assert.True(kq.DaTao);
        Assert.Null(kq.Loi); // vẫn KHÔNG phải "hỏng": chương trình đã có thật trên Shopee
        Assert.NotNull(kq.CanhBao);
        Assert.Contains("4. Đặt thời gian BẮT ĐẦU", kq.CanhBao);
        Assert.Contains("lệch tháng", kq.CanhBao);
    }

    [Fact]
    public void DocKetQua_LuotSach_ThiKHONG_bia_ra_canh_bao()
    {
        var kq = OrdersBridgeSession.DocKetQuaKmMotShop(
            """{"soSanPham":240,"daTao":true,"boQua":null,"buocHong":[],"canhBao":[]}""", Shop);

        Assert.Null(kq.CanhBao);
    }

    [Fact]
    public void CauKetQuaShop_CoCanhBao_ThiInRa_O_MOI_ca()
    {
        var tao = OrdersBridgeSession.CauKetQuaShop(1, 12,
            new KetQuaKmMotShop("1", "Shop A", true, 1000, null, null, "chạm trần 105 trang"));
        var bo = OrdersBridgeSession.CauKetQuaShop(2, 12,
            new KetQuaKmMotShop("2", "Shop B", false, 0, "không có sản phẩm", null, "modal lạ"));

        Assert.Contains("CẢNH BÁO", tao);
        Assert.Contains("chạm trần 105 trang", tao);
        Assert.Contains("CẢNH BÁO", bo);
    }

    // ── Câu nhật ký từng shop ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CauKetQuaShop_BaCa_NoiDungKhacNhauRoRang()
    {
        var tao = OrdersBridgeSession.CauKetQuaShop(1, 12, new KetQuaKmMotShop("1", "Shop A", true, 240, null, null));
        var bo = OrdersBridgeSession.CauKetQuaShop(2, 12, new KetQuaKmMotShop("2", "Shop B", false, 0, "không có sản phẩm", null));
        var hong = OrdersBridgeSession.CauKetQuaShop(3, 12, new KetQuaKmMotShop("3", "Shop C", false, 0, null, "hết giờ chờ"));

        Assert.Contains("240 sản phẩm", tao);
        Assert.Contains("BỎ QUA", bo);
        Assert.Contains("HỎNG", hong);
        Assert.Contains("hết giờ chờ", hong);
        // Ba câu phải PHÂN BIỆT được: nuốt chung một chữ là mất khả năng đọc nhật ký để biết chuyện gì xảy ra.
        Assert.DoesNotContain("HỎNG", tao);
        Assert.DoesNotContain("HỎNG", bo);
    }

    // ── Câu tổng kết cả lượt ──────────────────────────────────────────────────────────────────────────

    private static KetQuaKm Lot(int tao, int boQua, int hong, bool captcha = false, string? loi = null)
    {
        var ds = new List<KetQuaKmMotShop>();
        for (var i = 0; i < tao; i++) { ds.Add(new KetQuaKmMotShop($"t{i}", $"Tao{i}", true, 10, null, null)); }
        for (var i = 0; i < boQua; i++) { ds.Add(new KetQuaKmMotShop($"b{i}", $"Bo{i}", false, 0, "không có sản phẩm", null)); }
        for (var i = 0; i < hong; i++) { ds.Add(new KetQuaKmMotShop($"h{i}", $"Hong{i}", false, 0, null, "lỗi gì đó")); }
        return new KetQuaKm(ds, captcha, loi);
    }

    [Fact]
    public void TongKet_DemDungBaLoai()
    {
        var kq = Lot(tao: 7, boQua: 3, hong: 2);

        Assert.Equal(7, kq.SoTao);
        Assert.Equal(3, kq.SoBoQua);
        Assert.Equal(2, kq.SoHong);

        var cau = AccountsViewModel.CauTongKetKm(kq, coPhienChayLai: true);
        Assert.Contains("tạo 7/12 shop", cau);
        Assert.Contains("bỏ qua 3", cau);
        Assert.Contains("HỎNG 2", cau);
    }

    [Fact]
    public void TongKet_NeuDichDanhShopHong()
    {
        var cau = AccountsViewModel.CauTongKetKm(Lot(tao: 1, boQua: 0, hong: 2), coPhienChayLai: true);

        // "HỎNG 2 shop" mà không nói shop nào thì người đọc vẫn phải lần lại toàn bộ nhật ký.
        Assert.Contains("Hong0", cau);
        Assert.Contains("Hong1", cau);
    }

    [Fact]
    public void TongKet_LuotSachThiKHONG_nhac_hong_hay_bo_qua()
    {
        var cau = AccountsViewModel.CauTongKetKm(Lot(tao: 12, boQua: 0, hong: 0), coPhienChayLai: true);

        Assert.Contains("tạo 12/12 shop", cau);
        Assert.DoesNotContain("HỎNG", cau);
        Assert.DoesNotContain("bỏ qua", cau);
    }

    [Fact]
    public void TongKet_Captcha_PhaiNoiDUNG_GIUA_CHUNG()
    {
        // Dừng giữa chừng mà câu tổng kết đọc như đã chạy xong là kiểu nói dối nguy hiểm nhất ở đây: người dùng
        // tin 12 shop đã có chương trình, trong khi mới 3.
        var cau = AccountsViewModel.CauTongKetKm(Lot(tao: 3, boQua: 0, hong: 0, captcha: true), coPhienChayLai: true);

        Assert.Contains("DỪNG GIỮA CHỪNG", cau);
        Assert.Contains("captcha", cau);
    }

    [Fact]
    public void TongKet_LoiGiuaChung_PhaiNoiRoLyDo()
    {
        var cau = AccountsViewModel.CauTongKetKm(
            Lot(tao: 2, boQua: 0, hong: 0, loi: "Không về được trang chọn shop sau shop X"), coPhienChayLai: true);

        Assert.Contains("Dừng giữa chừng", cau);
        Assert.Contains("Không về được trang chọn shop", cau);
    }

    [Fact]
    public void TongKet_CoPhienDaDung_ThiNoiDaChayLaiCheckDon()
    {
        // Người dùng yêu cầu "sau khi kết thúc chương trình km thì chạy lại flow check đơn". Câu tổng kết là chỗ
        // DUY NHẤT họ đọc để biết điều đó đã xảy ra.
        Assert.Contains("check đơn chạy lại", AccountsViewModel.CauTongKetKm(Lot(1, 0, 0), coPhienChayLai: true));
        Assert.Contains("check đơn chạy lại", AccountsViewModel.CauTongKetKm(Lot(0, 0, 1), coPhienChayLai: true));
    }

    /// <summary>
    /// <b>KHÔNG có phiên nào bị tạm dừng ⇒ câu tổng kết KHÔNG được hứa "check đơn chạy lại".</b>
    /// <para>Ca thật: lượt tự động 2h sáng của một tài khoản không bật check đơn. Không có phiên nào để bật lại,
    /// mà dòng log vẫn khẳng định có — sáng hôm sau người đọc đi tìm một phiên không tồn tại rồi kết luận nhầm
    /// là app hỏng. Câu chữ phải theo DỮ KIỆN.</para>
    /// </summary>
    [Fact]
    public void TongKet_KHONG_co_phien_nao_ThiKHONG_hua_chay_lai()
    {
        var cau = AccountsViewModel.CauTongKetKm(Lot(1, 0, 0), coPhienChayLai: false);

        Assert.DoesNotContain("check đơn chạy lại", cau);
        Assert.Contains("Cửa sổ đã đóng", cau);
        Assert.Contains("không có phiên check đơn nào", cau);
    }

    /// <summary>
    /// <b>Hết khung tự động 06:00 KHÔNG phải lỗi</b> — câu tổng kết phải gọi đúng tên và nói còn bao nhiêu shop.
    /// <para>Gộp vào nhánh <c>Loi</c> ("Dừng giữa chừng: …") thì người đọc sáng hôm sau đi tìm một sự cố không
    /// hề xảy ra; im lặng bỏ qua thì họ tưởng 12 shop đã chạy xong trong khi mới 4.</para>
    /// </summary>
    [Fact]
    public void TongKet_HetKhung_NoiRoConBaoNhieuShop_VaKHONG_goi_la_loi()
    {
        var ds = new List<KetQuaKmMotShop> { new("t0", "Tao0", true, 10, null, null) };
        var kq = new KetQuaKm(ds, Captcha: false, Loi: null, HetKhung: true, SoShopChuaChay: 11);

        var cau = AccountsViewModel.CauTongKetKm(kq, coPhienChayLai: true);
        Assert.Contains("hết khung tự động", cau);
        Assert.Contains("11 shop", cau);
        Assert.Contains("tạo 1/12 shop", cau); // mẫu số = tổng shop của lượt, xem KetQuaKm.TongShop
        Assert.DoesNotContain("Dừng giữa chừng", cau);

        var trangThai = AccountsViewModel.CauTrangThaiKm(kq);
        Assert.Contains("hết khung tự động", trangThai);
        Assert.Contains("11 shop", trangThai);
    }

    // ── Hạn chặng ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Hạn chặng <c>KmDump</c> phải BẰNG <c>KmChay</c>.</b>
    /// <para>Nút "Dò DOM KM" gọi thẳng cùng hàm <c>chayKmChoTabDangMo</c> của luồng thật — cùng 9 bước, cùng
    /// chặng lật tới <c>TRAN_TRANG_MODAL</c> trang trong modal. Để <c>KmDump</c> ở 240s như trước thì lượt dò
    /// trên shop nhiều sản phẩm hết hạn giữa chừng và trả về "hết thời gian chờ" thay vì bản dump — đúng lúc
    /// người ta cần nó nhất (Shopee đổi DOM). Test này là thứ giữ hai hằng không trôi lệch nhau.</para>
    /// <para><b>Sàn dưới phải ĐI THEO trần trang, không được đứng yên.</b> Nới trần trang mà để sàn ở con số
    /// cũ thì test vẫn xanh trong khi hạn chặng đã HỤT so với việc nó phải làm — đúng kiểu test trang trí. Sàn
    /// hiện tại tính cho <c>TRAN_TRANG_MODAL = 200</c> (xem <c>flow-combo.js</c>): 200 trang × ~7s ở ca xấu
    /// nhất ≈ 1400s, cộng ~150s tám bước ngoài modal ≈ 1550s ⇒ sàn 1800s. Đổi trần trang lần nữa thì sửa cả
    /// con số này.</para>
    /// </summary>
    [Fact]
    public void HanChang_KmDump_BangVoi_KmChay()
    {
        Assert.Equal(OrdersBridgeChannel.ChoChang.KmChay, OrdersBridgeChannel.ChoChang.KmDump);
        Assert.True(OrdersBridgeChannel.ChoChang.KmChay >= TimeSpan.FromSeconds(1800));
    }

    // ── Cờ captcha NẰM TRONG gói kết quả ──────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Gói kết quả tự khai <c>captcha:true</c> phải được đọc ra.</b> Đây là đường thứ hai bên cạnh gói
    /// <c>{action:"captcha"}</c>: hai gói đi hai lượt bơm WebSocket, gói riêng hoàn toàn có thể tới SAU lượt C#
    /// đã đọc xong kết quả và đi tiếp shop kế — lúc đó chỉ còn cờ trong gói cứu được cả vòng.
    /// </summary>
    [Fact]
    public void DocKetQua_GoiTuKhaiCaptcha_ThiDocRaDuocCo()
    {
        var kq = OrdersBridgeSession.DocKetQuaKmMotShop(
            """{"soSanPham":0,"daTao":false,"boQua":null,"captcha":true,"buocHong":[{"ten":"6. Modal Chọn Sản Phẩm","loi":"rơi trang verify/captcha GIỮA bước"}]}""",
            Shop);

        Assert.True(kq.Captcha);
        Assert.NotNull(kq.Loi);

        // Chặn TEST RỖNG: gói KHÔNG khai captcha thì cờ phải là false.
        var sach = OrdersBridgeSession.DocKetQuaKmMotShop(
            """{"soSanPham":240,"daTao":true,"boQua":null,"buocHong":[]}""", Shop);
        Assert.False(sach.Captcha);
    }

    /// <summary>
    /// <b>Gói "không có tab shop" phải xếp vào HỎNG, KHÔNG phải BỎ QUA.</b>
    /// <para>Bản đợt 1 để extension trả <c>boQua</c> cho ca này ⇒ C# đếm vào <c>SoBoQua</c> và câu tổng kết nói
    /// "bỏ qua N shop không có sản phẩm" — một sự cố hạ tầng (service worker chết giữa chừng) bị nguỵ trang
    /// thành kết luận nghiệp vụ. Test này khoá phía tiêu thụ: gói dạng mới (buocHong "0. Tab shop", boQua null)
    /// phải ra <c>Loi</c>.</para>
    /// </summary>
    [Fact]
    public void DocKetQua_KhongCoTabShop_LaHONG_KHONG_phai_bo_qua()
    {
        var kq = OrdersBridgeSession.DocKetQuaKmMotShop(
            """{"soSanPham":0,"daTao":false,"boQua":null,"canhBao":[],"buocHong":[{"ten":"0. Tab shop","loi":"chưa có tab shop (openShopDetail chưa chạy, hoặc service worker đã chết giữa chừng)"}]}""",
            Shop);

        Assert.Null(kq.BoQua);
        Assert.NotNull(kq.Loi);
        Assert.Contains("0. Tab shop", kq.Loi);

        var lot = new KetQuaKm(new List<KetQuaKmMotShop> { kq }, false, null);
        Assert.Equal(1, lot.SoHong);
        Assert.Equal(0, lot.SoBoQua);
        Assert.DoesNotContain("bỏ qua", AccountsViewModel.CauTongKetKm(lot, coPhienChayLai: true));
    }

    /// <summary>
    /// <b>Gói "0. Tham số" (tham số lượt KM hỏng) cũng phải là HỎNG, KHÔNG phải BỎ QUA — và nêu đích danh trường.</b>
    /// <para>Từ đợt 3, extension kiểm trọn gói tham số TRƯỚC KHI đụng trang: sai <c>phanTram</c> thì dừng ngay ở
    /// cửa thay vì lật 105 trang modal (~12 phút) rồi mới đỏ ở bước 8, và bỏ lại một form điền dở trên shop
    /// thật. Test này khoá phía TIÊU THỤ: dạng gói mới phải ra <c>Loi</c> có tên trường, để dòng nhật ký nói
    /// được phải sửa gì trong form.</para>
    /// </summary>
    [Fact]
    public void DocKetQua_ThamSoHong_LaHONG_VaNeuDichDanhTruong()
    {
        var kq = OrdersBridgeSession.DocKetQuaKmMotShop(
            """{"soSanPham":0,"daTao":false,"boQua":null,"canhBao":[],"captcha":false,"buocHong":[{"ten":"0. Tham số","loi":"tham số lượt KM KHÔNG dùng được — phanTram: % giảm không hợp lệ: '0' — phải là số nguyên 1–99, extension KHÔNG tự thay giá trị"}]}""",
            Shop);

        Assert.Null(kq.BoQua);
        Assert.NotNull(kq.Loi);
        Assert.Contains("0. Tham số", kq.Loi);
        Assert.Contains("phanTram", kq.Loi);

        var lot = new KetQuaKm(new List<KetQuaKmMotShop> { kq }, false, null);
        Assert.Equal(1, lot.SoHong);
        Assert.Equal(0, lot.SoBoQua);
    }

    /// <summary>
    /// <b>Hết khung ở shop ĐẦU: mẫu số của "tạo N/M shop" là TỔNG shop của lượt, không phải số shop đã chạy.</b>
    /// <para>Bản đợt 2 dùng <c>Shops.Count</c> ⇒ câu ra "tạo 0/0 shop … còn 12 shop chưa chạy": mẫu số tự mâu
    /// thuẫn với vế sau của chính nó, và "0/0" đọc như một lượt không có shop nào để chạy.</para>
    /// </summary>
    [Fact]
    public void TongKet_HetKhungNgayShopDau_MauSo_LaTONG_shop_cua_luot()
    {
        var kq = new KetQuaKm(Array.Empty<KetQuaKmMotShop>(), Captcha: false, Loi: null,
            HetKhung: true, SoShopChuaChay: 12);

        Assert.Equal(12, kq.TongShop);
        Assert.Contains("tạo 0/12 shop", AccountsViewModel.CauTongKetKm(kq, coPhienChayLai: true));
        Assert.Contains("tạo 0/12 shop", AccountsViewModel.CauTrangThaiKm(kq));

        // Lượt chạy TRỌN thì mẫu số không đổi so với trước (SoShopChuaChay = 0) — vế chặn "sửa quá tay".
        Assert.Contains("tạo 5/5 shop", AccountsViewModel.CauTongKetKm(Lot(5, 0, 0), coPhienChayLai: true));
    }

    [Fact]
    public void CauTrangThai_NganGon_VaUuTienCaptchaRoiToiLoi()
    {
        Assert.Contains("captcha", AccountsViewModel.CauTrangThaiKm(Lot(1, 0, 0, captcha: true)));
        Assert.Contains("mất mạng", AccountsViewModel.CauTrangThaiKm(Lot(1, 0, 0, loi: "mất mạng")));
        Assert.Contains("tạo 5/5", AccountsViewModel.CauTrangThaiKm(Lot(5, 0, 0)));
    }
}
