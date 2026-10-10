using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;

namespace XuLyDonShopee.App.Views;

public partial class AccountsView : UserControl
{
    // Đang lắng nghe VM nào (để gỡ đăng ký khi DataContext đổi, tránh rò rỉ / cuộn nhầm).
    private INotifyPropertyChanged? _watchedVm;

    // View đang NẰM TRONG cây trực quan hay không — do chính Loaded/Unloaded lật. Dùng cái này chứ KHÔNG dùng
    // FrameworkElement.IsLoaded: IsLoaded chỉ đúng khi cây được gắn vào một PresentationSource thật, nên test
    // không dựng cửa sổ thì không bao giờ kiểm được nhánh có guard. Cờ tự giữ vừa tương đương trong lúc chạy
    // thật (đúng hai sự kiện đó đặt nó), vừa kiểm chứng được.
    private bool _trongCay;

    public AccountsView()
    {
        InitializeComponent();
        // DataContext gắn sau khi khởi tạo → theo dõi để đăng ký cuộn khi có VM.
        DataContextChanged += OnDataContextChanged;
        // Đổi màn con (Tài khoản ↔ Đơn hàng ↔ Thống kê) là DỰNG LẠI view chứ không phải ẩn/hiện: MainView đặt
        // màn qua ContentControl + DataTemplate, nên quay lại là một AccountsView MỚI với LogBox chưa cuộn đi
        // đâu cả. Mà LogText thì KHÔNG đổi giá trị lúc đó ⇒ OnVmPropertyChanged không bắn ⇒ log đứng ở dòng
        // ĐẦU và người dùng phải tự kéo xuống. Cuộn ngay khi Loaded để lần nào quay lại cũng thấy dòng mới nhất.
        //
        // Và chính vì view bị dựng lại mỗi lượt, vòng đời ĐĂNG KÝ phải bám Loaded/Unloaded chứ KHÔNG chỉ bám
        // DataContextChanged: VM sống suốt đời app (MainViewModel giữ một AccountsViewModel duy nhất) còn view
        // cũ bị vứt mà KHÔNG hề đổi DataContext ⇒ chỉ gỡ trong DataContextChanged thì mỗi lượt điều hướng để
        // lại một view chết bị VM giữ sống, và cứ ~250ms cả đám view chết đó lại cùng chạy CuonLogXuongCuoi.
        // Đúng lỗi OrdersView đã vá ở 2957563 nhưng bỏ sót file này (phản biện 18/08/2026).
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // Ô log đổi kích thước → bám đáy lại. Đăng ký THẲNG trong ctor, KHÔNG theo Loaded/Unloaded như đăng ký
        // vào VM: khác hẳn ca kia ở chỗ LogBox là con của chính view này nên cùng vòng đời — view chết thì cả
        // hai cùng chết, không có bên nào sống dai giữ bên kia. Đừng "sửa cho đồng bộ" với DangKyNgheVm.
        LogBox.SizeChanged += OnLogBoxSizeChanged;
        // Theo dõi người dùng có đang ở sát đáy không — xem _dangOSatDay. ScrollChanged là sự kiện NỔI BỌT từ
        // ScrollViewer bên trong template TextBox nên phải bắt bằng AddHandler.
        LogBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnLogScrollChanged));
    }

    /// <summary>
    /// Người dùng có đang ở SÁT ĐÁY nhật ký không. Chỉ dùng cho đường <see cref="OnLogBoxSizeChanged"/>.
    /// <para><b>Vì sao cần:</b> bám đáy VÔ ĐIỀU KIỆN khi đổi kích thước phá đúng thao tác người ta làm để ĐỌC
    /// log cũ — phiên đã dừng, lăn chuột lên xem một dòng lỗi, chữ Consolas 11px khó đọc nên bấm Ctrl+= phóng
    /// to, và log nhảy phắt xuống đáy làm mất chỗ đang đọc. Chốt <c>SelectionLength</c> không che được vì lăn
    /// chuột không tạo vùng chọn (phản biện 18/08/2026).</para>
    /// <para>Bản kế hoạch đầu biện hộ "đằng nào đường LogText cũng kéo xuống đáy mỗi ~250ms" — lý lẽ đó CHỈ
    /// đúng khi phiên đang chạy. Lúc phiên đã dừng, đọc lại log là ca dùng chính của panel này và không có nhịp
    /// nào cả.</para>
    /// <para>Mặc định <c>true</c>: mở panel ra là đang ở đáy.</para>
    /// </summary>
    private bool _dangOSatDay = true;

    /// <summary>Sai số pixel khi so "đã ở đáy chưa" — cuộn WPF là số thực, so bằng nhau tuyệt đối là hụt.</summary>
    private const double SaiSoSatDay = 1.0;

    /// <summary>
    /// Ghi nhớ người dùng đang ở đáy hay đã kéo lên. CHỈ tính lượt cuộn THẬT của người dùng: lượt do layout
    /// (đổi tổng chiều cao nội dung / đổi chiều cao khung nhìn) bị bỏ qua, vì chính lượt đó là cái ta đang muốn
    /// phản ứng lại — để nó ghi đè cờ thì cờ luôn phản ánh trạng thái SAU khi trôi, tức vô nghĩa.
    /// </summary>
    private void OnLogScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)
        {
            return;
        }

        _dangOSatDay = e.VerticalOffset >= e.ExtentHeight - e.ViewportHeight - SaiSoSatDay;
    }

    /// <summary>
    /// Ô log vừa đổi kích thước → cuộn bám đáy lại. Cần thiết vì style <c>logBox</c> đặt
    /// <c>TextWrapping="Wrap"</c>: đổi bề rộng ⇒ text cuộn lại dòng ⇒ tổng chiều cao nội dung đổi, mà
    /// <c>VerticalOffset</c> thì giữ nguyên theo pixel ⇒ khung nhìn trôi khỏi đáy. Hai đường dẫn tới đây là thu
    /// phóng Ctrl +/− (<c>UiZoom</c> gán <c>ScaleTransform</c> vào <c>LayoutTransform</c> ⇒ layout chạy lại ⇒ bề
    /// rộng khả dụng của LogBox đổi) và kéo giãn cửa sổ / kéo chia panel.
    /// <para>KHÔNG có vòng lặp: <c>ScrollToEnd</c> chỉ đổi offset cuộn chứ không đổi kích thước của
    /// <c>LogBox</c>, nên nó không tự bắn lại <c>SizeChanged</c>. Thanh cuộn dọc <c>Auto</c> cũng không tạo
    /// vòng: nó nằm BÊN TRONG template <c>logBox</c> nên hiện/ẩn không đổi <c>LogBox.ActualWidth</c>, và cột
    /// chứa log là <c>2*</c> (star) nên không đo theo nội dung. Đừng thêm việc gì khác vào handler này.</para>
    /// <para>Chỉ bám lại khi TRƯỚC ĐÓ đang ở sát đáy — xem <see cref="_dangOSatDay"/>. Người đang đọc log cũ mà
    /// phóng to chữ thì không được giật mất chỗ.</para>
    /// </summary>
    private void OnLogBoxSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // KHÔNG lọc theo e.WidthChanged/e.HeightChanged: WPF chỉ bắn sự kiện này khi kích thước THẬT SỰ đổi
        // (fireSizeChangedEvents còn kiểm PreviousSize != RenderSize), nên ít nhất một cờ luôn true — nhánh
        // "cả hai false" là nhánh CHẾT, và nhánh chết đội lốt guard thì tệ hơn không có (phản biện 18/08/2026).
        if (_dangOSatDay)
        {
            CuonLogXuongCuoi();
        }
    }

    /// <summary>View vào cây (mở lần đầu, hoặc quay lại từ màn con khác): bám VM + bám ngay đáy nhật ký.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _trongCay = true;
        if (DataContext is AccountsViewModel vm)
        {
            DangKyNgheVm(vm);
        }

        CuonLogXuongCuoi();
    }

    /// <summary>View rời cây (đổi sang màn con khác) → GỠ đăng ký, kẻo VM giữ view chết sống mãi.</summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _trongCay = false;
        ThoiNgheVm();
    }

    /// <summary>
    /// Khi DataContext (AccountsViewModel) gắn/đổi: đăng ký nghe <c>LogText</c> đổi để tự cuộn xuống dòng cuối.
    /// VM chỉ gán lại <c>LogText</c> mỗi nhịp gom của <c>ActivityLog</c> (~250ms) nên tự cuộn cũng chỉ chạy ngần
    /// ấy, dù log đang dội. Gỡ đăng ký cũ trước để không rò rỉ. Nuốt lỗi an toàn.
    /// </summary>
    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        ThoiNgheVm();

        // CHƯA vào cây thì KHÔNG đăng ký — Loaded sẽ lo. View bị vứt trước khi kịp Load mà đã bám vào VM thì
        // không có Unloaded nào tới gỡ hộ.
        if (_trongCay && DataContext is AccountsViewModel vm)
        {
            DangKyNgheVm(vm);
        }
    }

    /// <summary>Bám VM để nghe <c>LogText</c>. Gỡ trước khi gắn: Loaded và DataContextChanged đều gọi vào đây
    /// nên không có chốt này là đăng ký đôi, mỗi nhịp log cuộn hai lần.</summary>
    private void DangKyNgheVm(AccountsViewModel vm)
    {
        ThoiNgheVm();
        _watchedVm = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    /// <summary>Thôi bám VM hiện tại (nếu đang bám). Gọi được nhiều lần, không cần biết trạng thái trước.</summary>
    private void ThoiNgheVm()
    {
        if (_watchedVm is null)
        {
            return;
        }

        _watchedVm.PropertyChanged -= OnVmPropertyChanged;
        _watchedVm = null;
    }

    /// <summary>
    /// Bấm bất kỳ đâu trên một dòng tài khoản (kể cả vùng checkbox — nay cho click xuyên qua) → TOGGLE tick
    /// của ĐÚNG dòng đó (giữ khả năng tick nhiều để chạy nhóm). Việc chọn dòng + đổ Chi tiết/log do
    /// <c>ListBox</c> tự lo qua binding <c>SelectedItem = SelectedRow</c>, nên handler này CHỈ toggle tick.
    /// <para>Avalonia dùng <c>Tapped</c>; WPF dùng <c>MouseLeftButtonUp</c> — sự kiện chuột được mọi phần tử
    /// trên đường bubble bắn lại, nên bấm vào control con vẫn tới được Grid gốc của dòng; riêng nút "Truy cập
    /// TK" tự đánh dấu Handled nên không lỡ toggle tick.</para>
    /// </summary>
    private void OnAccountRowTapped(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AccountRowViewModel row
            && DataContext is AccountsViewModel vm)
        {
            vm.ToggleRowTick(row);
        }
    }

    /// <summary>
    /// Bấm nút "Truy cập TK" trên một dòng TK chưa xác nhận → chọn tài khoản đó + tự mở phiên trình duyệt để
    /// xác minh tay (VM lo). <c>e.Handled = true</c> để click KHÔNG lan thành thao tác bấm dòng (khỏi vô tình
    /// toggle tick). Lấy row từ DataContext của nút (thừa kế từ dòng).
    /// </summary>
    private void OnTruyCapTkClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AccountRowViewModel row
            && DataContext is AccountsViewModel vm)
        {
            vm.TruyCapTk(row);
        }

        e.Handled = true;
    }

    /// <summary>Nút "Copy": chép toàn bộ nhật ký đang hiển thị (<c>LogText</c> — chính chuỗi TextBox đang vẽ)
    /// vào clipboard để dán ra ngoài. Nuốt lỗi.</summary>
    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AccountsViewModel vm)
        {
            return;
        }
        try
        {
            var text = vm.LogText;
            Clipboard.SetText(text);
            var soDong = text.Length == 0 ? 0 : text.Split('\n').Length;
            vm.BusyStatus = $"Đã copy {soDong} dòng log vào clipboard.";
        }
        catch (System.Exception ex)
        {
            vm.BusyStatus = "Copy log lỗi: " + ex.Message;
        }
    }

    /// <summary>
    /// <c>LogText</c> vừa đổi (panel có log mới / đổi tài khoản) → cuộn xuống dòng cuối để luôn thấy hoạt động
    /// mới nhất. Marshal về UI thread cho chắc (VM chỉ gán trên UI thread nhưng vẫn phòng hờ) và ĐỌC độ dài từ
    /// chính TextBox sau khi binding đã áp. Nuốt mọi lỗi (panel có thể chưa gắn xong).
    /// </summary>
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AccountsViewModel.LogText))
        {
            return;
        }

        CuonLogXuongCuoi();
    }

    /// <summary>
    /// Bám đáy nhật ký. Gọi từ BA đường, và cả ba đều cần: <see cref="OnVmPropertyChanged"/> lo lúc có log mới
    /// hoặc đổi tài khoản, <see cref="OnLoaded"/> lo lúc view vừa được dựng lại sau khi đổi màn con,
    /// <see cref="OnLogBoxSizeChanged"/> lo lúc text cuộn lại dòng vì ô log đổi bề rộng.
    /// <para><c>UiDispatch.Post</c> chứ không gọi thẳng: tách khỏi ngăn xếp đang chạy (đường
    /// <c>OnVmPropertyChanged</c> có thể tới giữa lúc binding đang áp) để không cuộn trên trạng thái nửa vời.
    /// <b>Đừng đọc nó thành "chạy sau layout"</b> — <c>Post</c> dùng <c>BeginInvoke</c> ở
    /// <c>DispatcherPriority.Normal</c>, vốn CAO HƠN <c>Render</c>/<c>Loaded</c> nên chạy TRƯỚC lượt layout đang
    /// chờ. Việc cuộn vẫn đúng vì hai lý do khác: <c>Loaded</c> của WPF bắn sau lượt layout đã đo xong TextBox,
    /// và <c>ScrollToEnd</c> xếp lệnh cuộn để áp ở lần measure kế. Ai cần THẬT SỰ chạy sau layout thì phải đổi
    /// sang <c>DispatcherPriority.Loaded</c>/<c>ContextIdle</c>, đừng tin nhịp Post này.</para>
    /// </summary>
    private void CuonLogXuongCuoi()
    {
        UiDispatch.Post(() =>
        {
            try
            {
                // ĐANG BÔI ĐEN thì để yên: setter CaretIndex thực chất là Select(value, 0) — nó XOÁ vùng chọn,
                // mà panel này quảng cáo Ctrl+A/Ctrl+C (xem chú thích ở AccountsView.xaml).
                //
                // ⚠ PHẠM VI THẬT của chốt này, đo được chứ không phỏng đoán (phản biện 18/08/2026): nó CHỈ cứu
                // được đường Loaded và đường SizeChanged — hai đường KHÔNG ghi lại Text. Trên đường LogText đổi
                // thì vô dụng, vì binding OneWay (đăng ký từ lúc gắn DataContext, tức SỚM HƠN handler này) đã
                // ghi LogBox.Text trước, mà gán Text làm WPF tự gọi Select(0,0) ⇒ vùng chọn chết trước khi ta
                // kịp nhìn. Bản trước ghi ở đây rằng chốt này cứu được Ctrl+C lúc log đang dội — SAI, đã sửa.
                // Muốn giữ vùng chọn qua nhịp log thì phải neo theo NỘI DUNG rồi tìm lại sau khi Text bị ghi
                // (chuỗi log bị cắt bớt đầu ở mốc 200 dòng nên neo theo offset là trượt) — việc riêng.
                if (LogBox.SelectionLength > 0)
                {
                    return;
                }

                // Log là TextBox chỉ-đọc: đặt con trỏ về CUỐI + cuộn xuống dòng mới nhất.
                LogBox.CaretIndex = LogBox.Text?.Length ?? 0;
                LogBox.ScrollToEnd();
                // Ta vừa chủ động đưa về đáy ⇒ lượt đổi kích thước kế tiếp được phép bám lại.
                _dangOSatDay = true;
            }
            catch
            {
                // Bỏ qua: panel chưa dựng xong / control đã tháo. CỐ Ý không ghi nhật ký ở đây (khác quy ước
                // "nuốt lỗi phải để lại dấu vết"): hàm này chạy VÌ nhật ký vừa đổi, ghi thêm một dòng là tự gọi
                // lại chính mình — vòng lặp vô tận đắt hơn nhiều lần cái nó giúp chẩn đoán.
            }
        });
    }
}
