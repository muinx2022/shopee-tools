using System;
using System.Windows;
using XuLyDonShopee.App.ViewModels;

namespace XuLyDonShopee.App.Views;

/// <summary>
/// Form "KM của Shop" — nhập thông số chương trình khuyến mãi. Toàn bộ luật nằm ở
/// <see cref="KmDialogViewModel"/>; code-behind chỉ lo hai nút (khuôn <see cref="ChanDoanDonDialog"/>).
/// <para><b>⚠ Nút "Chạy chương trình" CHẠY THẬT:</b> đóng dialog xong, nơi gọi mở trình duyệt và TẠO chương
/// trình khuyến mãi trên MỌI shop của tài khoản. Dữ liệu sai thì dialog Ở LẠI cho người dùng sửa.</para>
/// </summary>
public partial class KmDialog : Window
{
    /// <summary>True khi người dùng bấm "Chạy chương trình" và dữ liệu HỢP LỆ — nơi gọi dựa vào đây để ghi
    /// nhật ký. Bấm "Đóng" (hoặc bấm Chạy mà dữ liệu sai) ⇒ false.</summary>
    public bool DaBamChay { get; private set; }

    // Constructor không tham số cho designer/XAML previewer.
    public KmDialog()
    {
        InitializeComponent();
    }

    public KmDialog(KmDialogViewModel vm) : this()
    {
        DataContext = vm;
    }

    /// <summary>
    /// "Chạy chương trình": nhờ VM kiểm tra. SAI ⇒ VM đã gán <c>Loi</c>, dialog Ở LẠI (không đóng, không ném).
    /// ĐÚNG ⇒ đánh dấu rồi đóng để nơi gọi lưu cấu hình + ghi nhật ký.
    /// </summary>
    private void OnChay(object sender, RoutedEventArgs e)
    {
        if (DataContext is not KmDialogViewModel vm)
        {
            return;
        }

        if (vm.ThuChay() is null)
        {
            return; // dữ liệu sai — ở lại form, lỗi đã hiện trong khung cảnh báo
        }

        DaBamChay = true;
        Dong();
    }

    /// <summary>
    /// "Chạy Combo" (tab Combo Khuyến Mãi): cùng hợp đồng với <see cref="OnChay"/>, chỉ đổi hàm kiểm của VM.
    /// SAI ⇒ ở lại form; ĐÚNG ⇒ đóng, và <see cref="KmDialogViewModel.VongYeuCauChay"/> nói cho nơi gọi biết
    /// phải chạy vòng COMBO chứ không phải vòng KM.
    /// </summary>
    private void OnChayCombo(object sender, RoutedEventArgs e)
    {
        if (DataContext is not KmDialogViewModel vm)
        {
            return;
        }

        if (vm.ThuChayCombo() is null)
        {
            return; // dữ liệu sai — ở lại form, lỗi đã hiện trong khung cảnh báo
        }

        DaBamChay = true;
        Dong();
    }

    /// <summary>"Đóng": nơi gọi vẫn lưu cấu hình — NHƯNG chỉ khi nó hợp lệ
    /// (<see cref="KmDialogViewModel.CauHinhHopLe"/>). Không chạy gì.</summary>
    private void OnDong(object sender, RoutedEventArgs e)
    {
        DaBamChay = false;
        Dong();
    }

    /// <summary><c>DialogResult</c> chỉ gán được khi mở bằng <c>ShowDialog()</c> — mở bằng <c>Show()</c> thì
    /// đóng thẳng (cùng lối phòng hờ như <see cref="ConfirmDialog"/>).</summary>
    private void Dong()
    {
        try { DialogResult = DaBamChay; }
        catch (InvalidOperationException) { Close(); }
    }
}
