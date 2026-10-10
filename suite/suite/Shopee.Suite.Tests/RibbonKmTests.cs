using System;
using System.Linq;
using System.Windows.Input;
using Shopee.Suite.ViewModels;

namespace Shopee.Suite.Tests;

/// <summary>
/// Nhóm ribbon "Khuyến mãi" của tab Shopee (pha 2 "KM của Shop").
/// <para><b>Vì sao test factory chứ không dựng <c>ShellViewModel</c></b>: ctor của shell khởi động TRỌN BỘ
/// module + database THẬT của người dùng. Test không được đụng vào đó, nên phần dựng nhóm ribbon được tách ra
/// thành <c>TaoNhomKhuyenMai</c> — chỉ cần một <c>ICommand</c> giả là kiểm được.</para>
/// </summary>
public class RibbonKmTests
{
    /// <summary>Command giả — chỉ để nhận diện, không chạy gì.</summary>
    private sealed class LenhGia : ICommand
    {
        // ICommand đòi khai sự kiện này; lệnh giả không bao giờ phát nên add/remove để trống (khỏi CS0067).
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }

    /// <summary>
    /// Nhóm phải tên "Khuyến mãi", có ĐÚNG một item "KM của Shop", và item đó nối vào ĐÚNG command được truyền
    /// vào (ở shell là <c>acc.MoFormKmCommand</c>).
    /// </summary>
    [Fact]
    public void TaoNhomKhuyenMai_DungTen_DungItem_DungCommand()
    {
        var lenh = new LenhGia();

        var nhom = ShellViewModel.TaoNhomKhuyenMai(lenh);

        Assert.Equal("Khuyến mãi", nhom.Title);
        var item = Assert.IsType<RibbonActionItem>(Assert.Single(nhom.Items));
        Assert.Equal("KM của Shop", item.Title);
        Assert.Same(lenh, item.Command);
    }

    /// <summary>
    /// Icon phải tra ra HÌNH THẬT từ bảng ánh xạ (<c>Themes/Icons.xaml</c>).
    /// <para><c>RibbonActionItem</c> nuốt khoá sai và trả <c>Icon = null</c> (cố ý: thiếu icon không đáng làm
    /// sập dải ribbon). Nghĩa là bịa một khoá không có thật thì nút hiện ra TRỐNG TRƠN mà không lỗi gì —
    /// đúng loại hỏng chỉ phát hiện bằng mắt. Test này nạp đúng từ điển icon thật rồi kiểm.</para>
    /// </summary>
    [StaFact]
    public void TaoNhomKhuyenMai_IconTraRaHinhThat()
    {
        NapTuDienIcon();

        var item = (RibbonActionItem)ShellViewModel.TaoNhomKhuyenMai(new LenhGia()).Items.Single();

        Assert.NotNull(item.Icon);
    }

    /// <summary>Nạp Themes/Icons.xaml vào Application hiện tại — `LookupIcon` tra qua
    /// <c>Application.Current.TryFindResource</c>, không có Application thì mọi icon đều null.</summary>
    private static void NapTuDienIcon()
    {
        var app = System.Windows.Application.Current ?? new System.Windows.Application();
        var uri = new Uri("pack://application:,,,/ShopeeSuite;component/Themes/Icons.xaml", UriKind.Absolute);
        if (!app.Resources.MergedDictionaries.Any(d => d.Source == uri))
        {
            app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary { Source = uri });
        }
    }
}
