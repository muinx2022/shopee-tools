using System;
using System.Windows;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.App.Views;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>X6 (phản biện vòng 3, 22/08/2026): form KM và nhật ký phải hiện TÊN MÁY, không phải GUID thô.</b>
/// <para>Id suất đơn hàng là <c>&lt;guid 32 ký tự&gt;:orders</c>. Người dùng đọc <i>"Máy chủ trì:
/// 3f8a91c2…:orders"</i> thì với nhóm 3 máy vẫn phải mở form ở từng máy để đoán ra ai đang giữ — đúng cái W4
/// sinh ra để chấm dứt. Repo đã có quy ước ngược lại ở <c>AccountSession.CauBiTuChoiKhoa</c> (nói TÊN máy) và
/// <c>MachineIdentity.Label</c> (tên + id ngắn).</para>
/// </summary>
public class KmNhanMayTests
{
    private const string MayNay = "3f8a91c2d4e5f6a7b8c9d0e1f2a3b4c5:orders";
    private const string MayKhac = "aa11bb22cc33dd44ee55ff66aa77bb88:orders";

    private static KmConfig Dong(string ownerMachine)
        => new(1, TuDong: true, SoNgay: 7, PhanTram: 25, GioBatDau: "08:00", GioKetThuc: "09:00", LanChayCuoi: null)
        {
            OwnerMachine = ownerMachine,
        };

    // ══════════════════════════ Hàm thuần ══════════════════════════

    /// <summary>Tra được tên ⇒ "tên (id ngắn)" đúng khuôn <c>MachineIdentity.Label</c>. Vẫn giữ id ngắn: hai máy
    /// trùng tên hiển thị là chuyện có thật, và id ngắn là thứ duy nhất đối chiếu được với bảng máy trên Hub.</summary>
    [Fact]
    public void CoTen_ThiHienTenKemIdNgan()
    {
        var nhan = KmLichTuDong.NhanMay(MayNay, "Máy kho 2");

        Assert.Equal("Máy kho 2 (3f8a91)", nhan);
        Assert.DoesNotContain(":orders", nhan);
        Assert.DoesNotContain("c2d4e5", nhan);   // KHÔNG lọt phần đuôi của GUID
    }

    /// <summary>Không tra được tên (hub chưa cấu hình / máy chưa từng heartbeat) ⇒ rơi về id NGẮN, tuyệt đối
    /// không phải id thô.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void KhongTraDuocTen_ThiChiIdNgan(string? ten)
    {
        var nhan = KmLichTuDong.NhanMay(MayNay, ten);

        Assert.Equal("3f8a91", nhan);
        Assert.DoesNotContain(":orders", nhan);
    }

    /// <summary>Id rỗng = "chưa ai chủ trì" ⇒ trả chuỗi rỗng để nơi gọi tự nói câu của nó ("chưa có"), không
    /// dựng ra một cái nhãn giả.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IdRong_ThiNhanRong(string? id) => Assert.Equal("", KmLichTuDong.NhanMay(id, "Máy kho 2"));

    /// <summary>Id ngắn hơn 6 ký tự (dữ liệu lạ / test) không được làm hàm ném.</summary>
    [Fact]
    public void IdNgan_ThiGiuNguyen() => Assert.Equal("abc", KmLichTuDong.NhanMay("abc", null));

    // ══════════════════════════ Form ══════════════════════════

    /// <summary>Form hiện TÊN máy chủ trì khi tra được — và không để lọt GUID thô ra màn hình.</summary>
    [Fact]
    public void MoTaChuTri_HienTenMayKhiTraDuoc()
    {
        var vm = new KmDialogViewModel(1, "sub@shopee.vn", 3, Dong(MayKhac), MayNay,
            id => id == MayKhac ? "Máy kho 2" : "Máy này");

        Assert.Contains("Máy kho 2 (aa11bb)", vm.MoTaChuTri);
        Assert.DoesNotContain(MayKhac, vm.MoTaChuTri);
    }

    /// <summary>Không có hook tra tên (app chạy độc lập) ⇒ vẫn không được in id thô; hiện id ngắn.</summary>
    [Fact]
    public void MoTaChuTri_KhongCoHookTraTen_ThiIdNgan()
    {
        var vm = new KmDialogViewModel(1, "sub@shopee.vn", 3, Dong(MayKhac), MayNay);

        Assert.Contains("aa11bb", vm.MoTaChuTri);
        Assert.DoesNotContain(MayKhac, vm.MoTaChuTri);
    }

    /// <summary>Bấm "Máy này nhận chủ trì" ⇒ dòng chữ đổi sang TÊN của máy này, cũng không phải id thô.</summary>
    [Fact]
    public void MoTaChuTri_SauKhiNhanChuTri_HienTenMayNay()
    {
        var vm = new KmDialogViewModel(1, "sub@shopee.vn", 3, Dong(MayKhac), MayNay,
            id => id == MayNay ? "PC-KHO-1" : null);

        vm.NhanChuTriCommand.Execute(null);

        Assert.Contains("PC-KHO-1 (3f8a91)", vm.MoTaChuTri);
        Assert.DoesNotContain(MayNay, vm.MoTaChuTri);
    }

    /// <summary>Và trên FORM THẬT (sau một lượt layout thật) — property đúng mà XAML tra nhầm ràng buộc thì
    /// người dùng vẫn đọc phải GUID.</summary>
    [StaFact]
    public void MoTaChuTri_TrenFormThat_KhongLoGuidTho()
    {
        var dlg = new KmDialog(new KmDialogViewModel(
            1, "sub@shopee.vn", 3, Dong(MayKhac), MayNay, _ => "Máy kho 2"));
        var phanTu = LayoutQuaTungTab(dlg);

        Assert.Contains("Máy kho 2 (aa11bb)", dlg.ChuTriText.Text);
        Assert.DoesNotContain(MayKhac, dlg.ChuTriText.Text);

        // LƯỚI CHỐNG RÀ RỖNG: dòng chủ trì ở VÙNG CHUNG (ngoài tab) nên nó xanh kể cả khi vòng dựng form bỏ
        // sót tab — chốt luôn ở đây rằng vòng dựng đi qua CẢ ô của tab đầu lẫn ô của tab combo.
        Assert.Contains((DependencyObject)dlg.TenBox, phanTu);
        Assert.Contains((DependencyObject)dlg.ComboGiamBox, phanTu);
    }

    /// <summary>
    /// Cho nội dung form đi qua một lượt layout thật — <b>lặp QUA TỪNG TAB</b> rồi trả về mọi phần tử trực
    /// quan gặp được (khử trùng theo THAM CHIẾU: phần nằm ngoài tab lượt nào cũng gặp).
    /// <para><b>Vì sao phải lặp</b> (26/08/2026, form chia tab): <c>TabControl</c> của WPF KHÔNG dựng nội dung
    /// của tab CHƯA CHỌN vào cây trực quan, nên một lượt layout duy nhất chỉ dựng ra tab đầu — mọi vòng đi bộ
    /// trên cây sau đó rà THIẾU chứ không ĐỎ.</para>
    /// </summary>
    private static List<DependencyObject> LayoutQuaTungTab(KmDialog dlg)
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
}
