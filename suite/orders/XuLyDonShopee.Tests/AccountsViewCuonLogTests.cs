using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.App.Views;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Ô nhật ký ở màn Tài khoản phải BÁM ĐÁY. Ba đường dẫn tới đó: có log mới (<c>LogText</c> đổi), view vào cây
/// (<c>Loaded</c>), và ô log đổi kích thước (<c>SizeChanged</c> — thu phóng Ctrl +/−, kéo giãn cửa sổ).
/// <para>
/// <b>Đo VerticalOffset chứ KHÔNG đo CaretIndex.</b> Bản test đầu (18/08/2026) khẳng định
/// <c>CaretIndex == Text.Length</c>, và lượt phản biện chứng minh bằng probe WPF rằng đó là đại lượng TÁCH RỜI
/// khỏi triệu chứng: chỉ đặt <c>CaretIndex</c> về cuối thì offset vẫn đứng im ở 474.8 trong khi đáy là 1247.6.
/// Nghĩa là xoá đúng dòng <c>ScrollToEnd()</c> — dòng DUY NHẤT thật sự vá lỗi — thì cả bộ test vẫn xanh còn
/// người dùng zoom vào là log lại trôi. Nay khẳng định thẳng thứ người dùng nhìn thấy: vị trí cuộn.
/// </para>
/// <para>
/// <b>KHÔNG gỡ binding của <c>LogBox.Text</c>.</b> Bản đầu gọi <c>BindingOperations.ClearBinding</c> cho gọn,
/// nhưng làm vậy là test chạy trên một cấu hình KHÔNG tồn tại lúc chạy thật — và chính chỗ đó che mất sự thật
/// ghi ở <see cref="NhipLogMoi_LamMatVungBoiDen_HanCheDaBiet"/>.
/// </para>
/// </summary>
public class AccountsViewCuonLogTests
{
    private const double RongDau = 900;
    private const double RongSau = 400;
    private const double CaoKhung = 300;

    /// <summary>Sai số pixel khi so "đã ở đáy chưa" — khớp <c>AccountsView.SaiSoSatDay</c>.</summary>
    private const double SaiSo = 1.0;

    /// <summary>Log đủ dài để CHẮC CHẮN tràn khung: không tràn thì mọi khẳng định về cuộn đều rỗng.</summary>
    private static string LogDai(string nhan)
        => string.Join(Environment.NewLine,
            Enumerable.Range(0, 200).Select(i => $"[{nhan}] dòng {i:D3} — nội dung đủ dài để ô log phải cuộn."));

    private static (TempDatabase Temp, AccountsViewModel Vm, AccountsView View) Dung()
    {
        var temp = new TempDatabase();
        var vm = new AccountsViewModel(new AppServices(temp.Path));
        var view = new AccountsView { DataContext = vm };
        // Bắt buộc: DataContext gắn lúc chưa vào cây nên OnDataContextChanged CỐ Ý không đăng ký; chỉ OnLoaded
        // mới gọi DangKyNgheVm. Không có bước này thì đổi LogText chẳng kích được gì.
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        return (temp, vm, view);
    }

    private static void Layout(FrameworkElement view, double rong)
    {
        view.Measure(new Size(rong, CaoKhung));
        view.Arrange(new Rect(0, 0, rong, CaoKhung));
        // UpdateLayout là mảnh thiếu thì hỏng: Arrange chỉ xếp hàng, SizeChanged bắn ở lượt chạy của trình
        // quản lý layout. Nó cũng là chỗ offset cuộn được áp.
        view.UpdateLayout();
    }

    private static double DayCua(TextBox box) => Math.Max(0, box.ExtentHeight - box.ViewportHeight);

    private static bool DangODay(TextBox box) => Math.Abs(box.VerticalOffset - DayCua(box)) <= SaiSo;

    /// <summary>Chặn test rỗng: không có gì để cuộn thì mọi khẳng định "đang ở đáy" đều đúng vô nghĩa.</summary>
    private static void PhaiCoGiDeCuon(TextBox box)
        => Assert.True(box.ExtentHeight > box.ViewportHeight + SaiSo,
            $"Log không tràn khung (extent={box.ExtentHeight}, viewport={box.ViewportHeight}) — test sẽ rỗng.");

    // ── Đường 1: có log mới ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Nhịp log mới (đường ~250ms lúc phiên đang chạy) → kéo về đáy.</summary>
    [StaFact]
    public void NhipLogMoi_ThiCuonVeDay()
    {
        var (temp, vm, view) = Dung();
        using var db = temp;

        vm.LogText = LogDai("a");
        Layout(view, RongDau);

        PhaiCoGiDeCuon(view.LogBox);
        Assert.True(DangODay(view.LogBox),
            $"offset={view.LogBox.VerticalOffset}, đáy={DayCua(view.LogBox)}");
    }

    // ── Đường 3: đổi kích thước ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// CA CHÍNH của bản vá: đang ở đáy, ô log hẹp lại (thu phóng / kéo giãn) ⇒ text cuộn lại dòng, tổng chiều
    /// cao nội dung tăng ⇒ phải BÁM ĐÁY LẠI chứ không đứng im theo pixel cũ.
    /// </summary>
    [StaFact]
    public void DoiBeRong_DangODay_ThiBamDayLai()
    {
        var (temp, vm, view) = Dung();
        using var db = temp;

        vm.LogText = LogDai("b");
        Layout(view, RongDau);
        PhaiCoGiDeCuon(view.LogBox);
        var dayTruoc = DayCua(view.LogBox);

        Layout(view, RongSau);

        // Chặn test rỗng: nếu bóp bề rộng mà nội dung không hề cuộn lại dòng thì chẳng có gì để bám lại.
        Assert.True(DayCua(view.LogBox) > dayTruoc + SaiSo,
            $"Bóp bề rộng không làm text re-wrap (đáy {dayTruoc} → {DayCua(view.LogBox)}) — test sẽ rỗng.");
        Assert.True(DangODay(view.LogBox),
            $"offset={view.LogBox.VerticalOffset}, đáy={DayCua(view.LogBox)}");
    }

    /// <summary>
    /// Mặt kia của cùng một luật, và là ca lượt phản biện 18/08/2026 bắt được: người dùng ĐÃ KÉO LÊN đọc log cũ
    /// (phiên đã dừng nên không có nhịp log nào) rồi bấm Ctrl+= cho chữ to lên → tuyệt đối KHÔNG được giật
    /// xuống đáy, vì đúng thao tác "phóng to để đọc" lại làm mất cái đang đọc.
    /// </summary>
    [StaFact]
    public void DoiBeRong_DaKeoLen_ThiKhongGiatXuongDay()
    {
        var (temp, vm, view) = Dung();
        using var db = temp;

        vm.LogText = LogDai("c");
        Layout(view, RongDau);
        PhaiCoGiDeCuon(view.LogBox);

        // Người dùng kéo lên đầu.
        view.LogBox.ScrollToVerticalOffset(0);
        view.UpdateLayout();
        Assert.True(view.LogBox.VerticalOffset <= SaiSo, "Chưa kéo lên được thì ca này vô nghĩa.");

        Layout(view, RongSau);

        Assert.True(view.LogBox.VerticalOffset <= SaiSo,
            $"Bị giật khỏi chỗ đang đọc: offset={view.LogBox.VerticalOffset}, đáy={DayCua(view.LogBox)}");
    }

    /// <summary>
    /// Đang bôi đen mà ô log đổi kích thước → KHÔNG được cướp vùng chọn. Đây là đường mà chốt
    /// <c>SelectionLength &gt; 0</c> THẬT SỰ bảo vệ (Text không bị ghi lại nên vùng chọn còn sống) — khác hẳn
    /// đường nhịp log, xem <see cref="NhipLogMoi_LamMatVungBoiDen_HanCheDaBiet"/>.
    /// </summary>
    [StaFact]
    public void DangBoiDen_DoiBeRong_ThiKhongCuopVungChon()
    {
        var (temp, vm, view) = Dung();
        using var db = temp;

        vm.LogText = LogDai("d");
        Layout(view, RongDau);
        PhaiCoGiDeCuon(view.LogBox);

        // Bôi đen ở CUỐI log để vẫn đang ở sát đáy — có vậy đường bám-đáy mới thật sự chạy tới chốt, chứ không
        // phải xanh nhờ _dangOSatDay đã tắt (khi đó test sẽ rỗng).
        var dai = view.LogBox.Text.Length;
        view.LogBox.Select(dai - 30, 30);
        view.UpdateLayout();

        Layout(view, RongSau);

        Assert.Equal(30, view.LogBox.SelectionLength);
        Assert.Equal(dai - 30, view.LogBox.SelectionStart);
    }

    // ── Hạn chế đã biết, chốt lại để không ai khẳng định ngược ───────────────────────────────────────────────

    /// <summary>
    /// <b>ĐẶC TẢ HẠN CHẾ, không phải mong muốn.</b> Nhịp log mới LÀM MẤT vùng bôi đen, và chốt
    /// <c>SelectionLength &gt; 0</c> KHÔNG cứu được: binding <c>OneWay</c> đăng ký từ lúc gắn <c>DataContext</c>
    /// (sớm hơn handler của view) nên nó ghi <c>LogBox.Text</c> trước, mà gán <c>Text</c> làm WPF tự gọi
    /// <c>Select(0,0)</c> ⇒ vùng chọn chết trước khi chốt kịp nhìn.
    /// <para>Test này tồn tại vì bản 18/08/2026 từng ghi trong comment sản phẩm rằng chốt đó cứu được Ctrl+C lúc
    /// log đang dội — sai sự thật, và không có test nào chặn. Nay có. Ngày nào ai đó thật sự vá được (neo vùng
    /// chọn theo NỘI DUNG rồi tìm lại sau khi Text bị ghi), test này sẽ đỏ — và đó là lúc SỬA nó, không phải
    /// lúc xoá nó đi.</para>
    /// </summary>
    [StaFact]
    public void NhipLogMoi_LamMatVungBoiDen_HanCheDaBiet()
    {
        var (temp, vm, view) = Dung();
        using var db = temp;

        vm.LogText = LogDai("e");
        Layout(view, RongDau);
        view.LogBox.Select(5, 20);
        Assert.Equal(20, view.LogBox.SelectionLength);

        vm.LogText = LogDai("f");
        Layout(view, RongDau);

        Assert.Equal(0, view.LogBox.SelectionLength);
    }
}
