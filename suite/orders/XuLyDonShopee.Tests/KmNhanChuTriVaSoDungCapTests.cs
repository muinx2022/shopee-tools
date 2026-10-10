using System;
using System.Windows;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.App.Views;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>Form "KM của Shop": NÚT NHẬN CHỦ TRÌ (W4) và phép so ĐÚNG CẶP của đường lưu (W5)</b> — phản biện vòng 3,
/// 22/08/2026.
/// <para><b>W4:</b> vòng 2 để lại hai hạng mục đá nhau — cửa chủ trì chặn máy không chủ trì chạy lượt tự động,
/// còn <c>BanLuuSauForm</c> lại trả <c>null</c> khi người dùng không đổi gì. Máy chủ trì nghỉ hưu (cài lại
/// Windows ⇒ đổi id) là <b>KM chết vĩnh viễn trên cả fleet</b>, mà dòng nhật ký lại bảo "mở form ở đây và lưu
/// lại" — làm đúng vậy thì KHÔNG có gì xảy ra.</para>
/// <para><b>W5:</b> vòng 2 so <c>tuForm</c> với dòng ĐỌC LẠI LÚC LƯU, tức trả lời "DB có khác form không" chứ
/// không phải "người dùng có sửa gì không". Hai câu đó lệch nhau đúng bằng cửa sổ 60 giây của worker: máy B mở
/// form ra XEM → máy A đổi 25%→40% và đẩy lên → worker của B kéo về (thread Timer, modal không chặn) → B bấm
/// Đóng không chạm gì ⇒ B lưu 25%, đóng dấu mốc mới, cướp chủ trì ⇒ <b>A bị lùi về 25%</b>.</para>
/// </summary>
public class KmNhanChuTriVaSoDungCapTests
{
    private const string MayNay = "may-nay:orders";
    private const string MayKhac = "may-khac:orders";
    private static readonly DateTime LucLuu = new(2026, 8, 22, 15, 0, 0);

    private static KmConfig Dong(
        int phanTram = 25, bool tuDong = true, string ownerMachine = MayKhac,
        DateTime? cfgUpdatedAt = null, DateTime? lanChayCuoi = null)
        => new(1, tuDong, SoNgay: 7, phanTram, GioBatDau: "08:00", GioKetThuc: "09:00", lanChayCuoi)
        {
            CfgUpdatedAt = cfgUpdatedAt,
            OwnerMachine = ownerMachine,
        };

    private static KmDialogViewModel Form(KmConfig banNap, string mayNay = MayNay)
        => new(accountId: 1, login: "sub@shopee.vn", soShop: 3, banNap, mayNay);

    // ══════════════════════════ W4 — nút "Máy này nhận chủ trì" ══════════════════════════

    /// <summary>
    /// <b>Bấm nút ⇒ đóng dấu chủ trì + mốc sửa DÙ cấu hình y nguyên.</b> Đây là đường DUY NHẤT gỡ được ca "máy
    /// chủ trì đã nghỉ hưu" — không có nó thì KM chết trên cả fleet và không thao tác nào trong app cứu được.
    /// </summary>
    [Fact]
    public void BamNhanChuTri_ThiLuu_DuCauHinhKhongDoi()
    {
        var banNap = Dong(ownerMachine: MayKhac);
        var vm = Form(banNap);
        Assert.True(vm.CoTheNhanChuTri);

        vm.NhanChuTriCommand.Execute(null);

        Assert.True(vm.DaBamNhanChuTri);
        var luu = KmDialogViewModel.BanLuuSauForm(
            banNap, banNap, vm.LayCauHinh(), MayNay, LucLuu, vm.DaBamNhanChuTri);

        Assert.NotNull(luu);
        Assert.Equal(MayNay, luu!.OwnerMachine);
        Assert.Equal(LucLuu, luu.CfgUpdatedAt);
        Assert.Equal(25, luu.PhanTram);      // cấu hình KHÔNG bị đụng vào
        Assert.True(luu.TuDong);
    }

    /// <summary>Vế ĐỐI CHỨNG — KHÔNG bấm nút và không sửa gì ⇒ vẫn không lưu, không cướp chủ trì (V5 giữ
    /// nguyên). Thiếu vế này thì một bản "lúc nào cũng đóng dấu" cũng xanh, mà bản đó là bug vòng 1.</summary>
    [Fact]
    public void KhongBamNhanChuTri_VaKhongSuaGi_ThiKhongLuu()
    {
        var banNap = Dong(ownerMachine: MayKhac);
        var vm = Form(banNap);

        Assert.Null(KmDialogViewModel.BanLuuSauForm(
            banNap, banNap, vm.LayCauHinh(), MayNay, LucLuu, vm.DaBamNhanChuTri));
    }

    /// <summary>Không có hub (không biết mình là máy nào) ⇒ nút TẮT: đóng dấu một cái tên rỗng làm chủ trì là
    /// khoá chết lịch KM cho mọi máy mà chẳng ai nhận.</summary>
    [Fact]
    public void KhongCoHub_ThiNutTat()
    {
        var vm = Form(Dong(ownerMachine: MayKhac), mayNay: "");

        Assert.False(vm.CoTheNhanChuTri);
        vm.NhanChuTriCommand.Execute(null);
        Assert.False(vm.DaBamNhanChuTri);
    }

    /// <summary>Máy này ĐÃ chủ trì rồi ⇒ nút TẮT (không có gì để nhận).</summary>
    [Fact]
    public void DaLaChuTri_ThiNutTat()
        => Assert.False(Form(Dong(ownerMachine: MayNay)).CoTheNhanChuTri);

    /// <summary>Form phải NÓI RA máy nào đang chủ trì — trước bản này nó không lộ gì, nên người dùng đọc dòng
    /// nhật ký "máy chủ trì là X" xong mở form ra cũng không đối chiếu được với cái gì.</summary>
    [Fact]
    public void MoTaChuTri_NeuTenMayDangGiu_VaDoiSauKhiBam()
    {
        var vm = Form(Dong(ownerMachine: MayKhac));
        // Máy hiện bằng NHÃN người đọc được (tên + id NGẮN), không phải id thô — X6, vòng 4.
        Assert.Contains(KmLichTuDong.NhanMay(MayKhac, null), vm.MoTaChuTri);
        Assert.DoesNotContain(MayKhac, vm.MoTaChuTri);

        vm.NhanChuTriCommand.Execute(null);

        Assert.Contains(KmLichTuDong.NhanMay(MayNay, null), vm.MoTaChuTri);
        Assert.DoesNotContain(MayNay, vm.MoTaChuTri);
        Assert.False(vm.CoTheNhanChuTri);       // bấm hai lần không có nghĩa gì
    }

    /// <summary>Chưa ai chủ trì ⇒ nói rõ "chưa có", không bịa một cái tên.</summary>
    [Fact]
    public void MoTaChuTri_KhiChuaAiChuTri()
        => Assert.Contains("chưa có", Form(Dong(ownerMachine: "")).MoTaChuTri);

    // ══════════════════════════ W5 — so ĐÚNG CẶP (banNap, không phải tuoi) ══════════════════════════

    /// <summary>
    /// <b>Ca thật của W5: worker kéo bản MỚI của máy A về trong lúc form đang mở, người dùng ở máy B không chạm
    /// gì rồi bấm Đóng ⇒ KHÔNG lưu, KHÔNG đóng dấu.</b>
    /// <para>Bản vòng 2 so <c>tuForm</c> (25% — đúng cái đang hiện) với <c>tuoi</c> (40% — bản A vừa đẩy) ⇒
    /// thấy "khác" ⇒ lưu 25% + đóng dấu mốc mới + cướp chủ trì ⇒ máy A bị lùi về 25%. Đúng hình dạng lỗi vòng
    /// 1, dịch vào cửa sổ 60 giây.</para>
    /// </summary>
    [Fact]
    public void MayKhacDoiTrongLucMoForm_NguoiDungKhongSuaGi_ThiKhongLuu()
    {
        var banNap = Dong(phanTram: 25, ownerMachine: MayKhac,
            cfgUpdatedAt: new DateTime(2026, 8, 22, 10, 0, 0));
        var vm = Form(banNap);

        // Máy A đổi 25% → 40% và worker của máy này đã kéo về (mốc sửa mới hơn).
        var tuoi = Dong(phanTram: 40, ownerMachine: MayKhac,
            cfgUpdatedAt: new DateTime(2026, 8, 22, 14, 55, 0));

        Assert.Null(KmDialogViewModel.BanLuuSauForm(tuoi, banNap, vm.LayCauHinh(), MayNay, LucLuu));
    }

    /// <summary>Vế ĐỐI CHỨNG — cũng ca đó nhưng người dùng CÓ sửa (25% → 30%) ⇒ lưu và đóng dấu: họ đang ngồi
    /// trước máy, ý muốn vừa gõ ra phải thắng.</summary>
    [Fact]
    public void MayKhacDoiTrongLucMoForm_NguoiDungCoSua_ThiVanLuu()
    {
        var banNap = Dong(phanTram: 25, ownerMachine: MayKhac);
        var vm = Form(banNap);
        vm.PhanTram = 30;

        var tuoi = Dong(phanTram: 40, ownerMachine: MayKhac);
        var luu = KmDialogViewModel.BanLuuSauForm(tuoi, banNap, vm.LayCauHinh(), MayNay, LucLuu);

        Assert.NotNull(luu);
        Assert.Equal(30, luu!.PhanTram);
        Assert.Equal(MayNay, luu.OwnerMachine);
        Assert.Equal(LucLuu, luu.CfgUpdatedAt);
    }

    /// <summary>
    /// Máy THỨ BA nhận chủ trì trong lúc form đang mở, người dùng ở đây không chạm gì ⇒ vẫn KHÔNG lưu.
    /// <para>Ca này lọt qua mọi assert khác vì phần cấu hình y hệt nhau — chỉ <c>OwnerMachine</c> đổi. So sai
    /// cặp ở đây là <b>lặng lẽ giành lại chủ trì</b> chỉ vì người dùng mở form ra nhìn, tức đúng lỗi V5 đội lốt
    /// khác. (Lượt thử phá dựng ra đúng ca này: một đột biến chỉ đổi vế dựng bản so mà mọi test khác vẫn xanh.)</para>
    /// </summary>
    [Fact]
    public void MayThuBaNhanChuTriTrongLucMoForm_KhongSuaGi_ThiKhongLuu()
    {
        var banNap = Dong(phanTram: 25, ownerMachine: MayKhac);
        var vm = Form(banNap);
        var tuoi = Dong(phanTram: 25, ownerMachine: "may-ba:orders");

        Assert.Null(KmDialogViewModel.BanLuuSauForm(tuoi, banNap, vm.LayCauHinh(), MayNay, LucLuu));
    }

    // ══════════════════════════ Nút THẬT trên form THẬT ══════════════════════════

    /// <summary>
    /// <b>Dựng <c>KmDialog</c> THẬT và đo nút + dòng chữ chủ trì.</b>
    /// <para>Vì sao không dừng ở test VM: một property đúng mà XAML quên ràng buộc (hoặc tra nhầm tên tài
    /// nguyên) thì người dùng vẫn không có nút nào để bấm — mà bộ test VM vẫn xanh. Đúng bài học của
    /// <c>NutChanDoanAnTests</c>: phải đo thứ người dùng NHÌN THẤY, sau một lượt layout thật (chưa layout thì
    /// binding còn <c>Unattached</c> và mọi phép đo đều rỗng).</para>
    /// </summary>
    [StaFact]
    public void NutNhanChuTri_HienThat_TrenFormThat()
    {
        var dlg = DungDialog(Dong(ownerMachine: MayKhac), MayNay);

        Assert.True(dlg.NhanChuTriButton.IsEnabled);
        Assert.Contains(KmLichTuDong.NhanMay(MayKhac, null), dlg.ChuTriText.Text);   // nhãn, không phải id thô (X6)

        // Bấm nút THẬT (qua Command của nút, đúng đường người dùng đi) ⇒ chữ đổi và nút tự tắt.
        dlg.NhanChuTriButton.Command.Execute(null);
        LayoutMotLuot(dlg);

        Assert.Contains(KmLichTuDong.NhanMay(MayNay, null), dlg.ChuTriText.Text);
        Assert.False(dlg.NhanChuTriButton.IsEnabled);
    }

    /// <summary>Không có hub ⇒ nút TẮT trên form thật (không chỉ trong VM).</summary>
    [StaFact]
    public void KhongCoHub_ThiNutTat_TrenFormThat()
        => Assert.False(DungDialog(Dong(ownerMachine: MayKhac), mayNay: "").NhanChuTriButton.IsEnabled);

    /// <summary>
    /// <b>Ô tick "Combo Khuyến Mãi — chạy tự động" phải KHOÁ khi luồng combo chưa chạy được</b>, và dòng chữ
    /// giải thích phải HIỆN — đo trên form THẬT, sau một lượt layout thật.
    /// <para><b>Ca hỏng nó chặn</b> (phản biện 25/08/2026, NẶNG-1): ô tick trông bình thường là một CÔNG TẮC ĐÃ
    /// LÊN ĐẠN. Người dùng bật xong thì 06:0x hôm sau hub DỪNG check đơn ở MỌI máy khác, Brave + SSO, mỗi shop
    /// treo 900s vì <c>background.js</c> chưa có nhánh <c>chayCombo</c> — ~2 tiếng check đơn chết, 0 chương
    /// trình, và LẶP LẠI MỖI SÁNG. <c>KmLichTuDong.VongBat</c> chặn được máy nhưng KHÔNG nói cho ai biết vì
    /// sao; ô khoá + dòng chữ này mới là phần nói ra.</para>
    /// <para>Đo trên control THẬT chứ không trên property VM: một property đúng mà XAML quên ràng buộc thì người
    /// dùng vẫn tick được — đúng bài học <c>NutChanDoanAnTests</c>. Buộc vào chính hằng
    /// <see cref="KmLichTuDong.ComboSanSang"/> nên chặng B lật cờ là test tự đổi vế theo.</para>
    /// </summary>
    [StaFact]
    public void OTickCombo_KhoaKhiLuongChuaSanSang_TrenFormThat()
    {
        var dlg = DungDialog(Dong(ownerMachine: MayNay), MayNay);

        // LƯỚI CHỐNG RÀ RỖNG: ô tick combo nằm ở tab THỨ HAI, mà TabControl không dựng nội dung tab chưa chọn
        // vào cây trực quan. Vòng dựng form phải đi qua tab đó thật — bản chỉ layout tab đang mở khiến ba phép
        // đo bên dưới vẫn xanh trên một ô chưa bao giờ được dựng ra màn hình.
        Assert.Contains((DependencyObject)dlg.ComboBatCheck, LayoutMotLuot(dlg));

        Assert.Equal(KmLichTuDong.ComboSanSang, dlg.ComboBatCheck.IsEnabled);
        Assert.Equal(
            KmLichTuDong.ComboSanSang ? Visibility.Collapsed : Visibility.Visible,
            dlg.ComboChuaSanSangText.Visibility);
        Assert.Contains("CHƯA DÙNG ĐƯỢC", dlg.ComboChuaSanSangText.Text);
    }

    /// <summary>Dựng dialog rồi cho NỘI DUNG của nó đi qua một lượt layout thật.
    /// <para>Layout trên chính <c>Window</c> KHÔNG chạy (window chưa được hiện thì nó không phải gốc của một
    /// cây layout nào) — đo lúc đó thì mọi binding còn <c>Unattached</c> và <c>Text</c> đọc ra RỖNG, tức lại
    /// một phép đo rỗng. Đo trên <c>Content</c>: <c>DataContext</c> vẫn chảy xuống theo cây logic.</para></summary>
    private static KmDialog DungDialog(KmConfig banNap, string mayNay)
    {
        var dlg = new KmDialog(new KmDialogViewModel(1, "sub@shopee.vn", 3, banNap, mayNay));
        LayoutMotLuot(dlg);
        return dlg;
    }

    /// <summary>
    /// Cho nội dung form đi qua một lượt layout thật — <b>lặp QUA TỪNG TAB</b> rồi trả về mọi phần tử trực
    /// quan gặp được (khử trùng theo THAM CHIẾU: phần nằm ngoài tab lượt nào cũng gặp).
    /// <para><b>Vì sao phải lặp</b> (26/08/2026, form chia tab): <c>TabControl</c> của WPF KHÔNG dựng nội dung
    /// của tab CHƯA CHỌN vào cây trực quan, nên một lượt layout duy nhất chỉ dựng ra tab đầu. Ô tick combo và
    /// dòng chữ cạnh nó nằm ở tab THỨ HAI — đo chúng mà chưa hề dựng tab đó là đang đo một thứ người dùng
    /// chưa từng nhìn thấy.</para>
    /// </summary>
    private static List<DependencyObject> LayoutMotLuot(KmDialog dlg)
    {
        var than = (FrameworkElement)dlg.Content;
        var ds = new List<DependencyObject>();
        var daGap = new HashSet<DependencyObject>();
        for (var i = 0; i < dlg.KmTabs.Items.Count; i++)
        {
            dlg.KmTabs.SelectedIndex = i;
            than.Measure(new Size(900, 800));
            than.Arrange(new Rect(0, 0, 900, 800));
            than.UpdateLayout();

            foreach (var el in CayCon(than))
            {
                if (daGap.Add(el)) { ds.Add(el); }
            }
        }
        return ds;
    }

    private static IEnumerable<DependencyObject> CayCon(DependencyObject goc)
    {
        var n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(goc);
        for (var i = 0; i < n; i++)
        {
            var con = System.Windows.Media.VisualTreeHelper.GetChild(goc, i);
            yield return con;
            foreach (var chau in CayCon(con)) { yield return chau; }
        }
    }

    /// <summary>Người dùng PHẢI được báo khi máy khác đổi cấu hình giữa chừng — ở CẢ hai kết cục, với câu chữ
    /// khác nhau. Ghi đè lặng chính là thứ làm ca này không thể chẩn đoán.</summary>
    [Fact]
    public void CanhBao_KhiMayKhacDoiGiuaChung()
    {
        var banNap = Dong(phanTram: 25);
        var tuoi = Dong(phanTram: 40);

        var khongLuu = KmDialogViewModel.CauCanhBaoSauForm(banNap, tuoi, daLuu: false);
        Assert.NotNull(khongLuu);
        Assert.Contains("ĐỔI Ở MÁY KHÁC", khongLuu);
        Assert.Contains("40%", khongLuu);
        Assert.Contains("KHÔNG lưu gì", khongLuu);

        var daLuu = KmDialogViewModel.CauCanhBaoSauForm(banNap, tuoi, daLuu: true);
        Assert.NotNull(daLuu);
        Assert.Contains("ĐÈ LÊN", daLuu);
    }

    /// <summary>
    /// <b>Câu cảnh báo KHÔNG được in <c>GioKetThuc</c> — đó là TRƯỜNG CHẾT từ 26/08/2026.</b>
    /// <para>Cột DB được giữ lại có chủ đích (xoá là kéo theo migration + hợp đồng hub), nhưng nó không còn
    /// đường nào đi xuống Shopee: chương trình chạy một KHOẢNG 180 ngày, CÙNG một giờ ở hai đầu. In
    /// <c>08:00-09:00</c> ra như thể đó là khung giờ chương trình là nói sai với đúng người đang cần biết cấu
    /// hình THẬT đang chạy là gì — mà đây lại là dòng duy nhất họ đọc được về bản của máy kia.</para>
    /// </summary>
    [Fact]
    public void CanhBao_NoiKHOANG_KhongInGioKetThucDaChet()
    {
        var cau = KmDialogViewModel.CauCanhBaoSauForm(Dong(phanTram: 25), Dong(phanTram: 40), daLuu: false)!;

        Assert.Contains("08:00", cau);                       // giờ THẬT vẫn phải có
        Assert.DoesNotContain("09:00", cau);                 // …còn giờ kết thúc thì KHÔNG
        Assert.DoesNotContain("08:00-09:00", cau);
        Assert.Contains($"khoảng {KmLichTuDong.SoNgayDatTruoc} ngày, cùng giờ hai đầu", cau);
    }

    /// <summary>Vế ĐỐI CHỨNG — không ai đổi gì giữa chừng ⇒ KHÔNG cảnh báo. Cảnh báo vô cớ mỗi lần đóng form
    /// thì người dùng thôi đọc, tức mất luôn tác dụng của chính dòng đó.</summary>
    [Fact]
    public void KhongCanhBao_KhiKhongAiDoiGi()
    {
        var banNap = Dong(phanTram: 25);

        // Hai mốc CHẠY khác nhau (worker vừa đẩy mốc lên) KHÔNG phải là "đổi cấu hình".
        var tuoi = Dong(phanTram: 25, lanChayCuoi: new DateTime(2026, 8, 22, 2, 30, 0));

        Assert.Null(KmDialogViewModel.CauCanhBaoSauForm(banNap, tuoi, daLuu: false));
    }
}
