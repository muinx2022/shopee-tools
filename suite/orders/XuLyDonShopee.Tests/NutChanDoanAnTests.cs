using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>Nút chẩn đoán "Dò DOM KM" phải ẨN mặc định, và code của nó phải CÒN NGUYÊN.</b>
/// <para>Hai vế, hai lý do khác nhau:</para>
/// <list type="number">
/// <item><b>Ẩn:</b> nút này chạy TRỌN luồng, tức bấm nhầm là tạo một chương trình khuyến mãi THẬT trên shop
/// thật của người dùng.</item>
/// <item><b>Còn nguyên:</b> người dùng chốt 19/08/2026 — giữ lại phòng khi Shopee đổi DOM. Lượt dò ghi nhật ký
/// từng bước kèm giá trị đọc lại nên chỉ thẳng bước nào vỡ; dò lại từ đầu mất một đêm, bật cờ mất một giây.
/// Test này là cái chặn người sau "dọn dẹp" nó đi.</item>
/// </list>
/// </summary>
[Collection(TenCollection.BienMoiTruongDev)]
public class NutChanDoanAnTests
{
    private static string DocXaml()
    {
        // Đi ngược từ thư mục chạy test lên tới gốc repo — không hard-code đường dẫn tuyệt đối.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ShopeeSuite.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        var duong = Path.Combine(dir!.FullName, "orders", "XuLyDonShopee.App", "Views", "AccountsView.xaml");
        Assert.True(File.Exists(duong), "không thấy AccountsView.xaml ở " + duong);
        return File.ReadAllText(duong);
    }

    [Fact]
    public void MacDinh_KHONG_bat_bien_moi_truong_thi_nut_AN()
    {
        Assert.False(AccountsViewModel.DocCoDev(null));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData("bat", false)]
    public void CoBienMoiTruong_ThiTheoDungGiaTri(string giaTri, bool mongDoi)
    {
        Assert.Equal(mongDoi, AccountsViewModel.DocCoDev(giaTri));
    }

    /// <summary>
    /// <b>ĐO <c>Visibility</c> THẬT của nút trên control THẬT, cả hai chiều biến môi trường.</b>
    /// <para><b>Vì sao cần dù đã có 4 test cũ:</b> bốn test kia đọc thẳng property và soi chuỗi trong file
    /// .xaml — cả hai đều XANH trong khi nút HIỆN với mọi người. Lý do: <c>HienNutDev</c> từng là property
    /// <c>static</c>, mà Binding path chỉ phân giải trên ĐỐI TƯỢNG DataContext ⇒ binding fail im lặng ⇒
    /// <c>Visibility</c> giữ mặc định <c>Visible</c>. Test trang trí đúng nghĩa: nó khẳng định "có ràng buộc"
    /// và "cờ tính đúng", nhưng KHÔNG khẳng định cái duy nhất người dùng thấy — nút có hiện không.</para>
    /// <para>Biến môi trường được đặt TRƯỚC khi dựng VM: cờ đọc một lần trong lượt khởi tạo VM.</para>
    /// </summary>
    [StaFact]
    public void Visibility_ThatCuaNut_TheoDungBienMoiTruong()
    {
        Assert.Equal(Visibility.Collapsed, DungRoiDoVisibility(null));
        Assert.Equal(Visibility.Collapsed, DungRoiDoVisibility("0"));
        Assert.Equal(Visibility.Visible, DungRoiDoVisibility("1"));
        Assert.Equal(Visibility.Visible, DungRoiDoVisibility("true"));
    }

    /// <summary>
    /// Đặt biến môi trường → dựng VM + <c>AccountsView</c> THẬT → đọc <c>Visibility</c> của nút chẩn đoán.
    /// <para><b><c>Measure</c> + <c>Arrange</c> + <c>UpdateLayout</c> là phần bắt buộc, đừng bỏ.</b> Nút nằm
    /// trong một <c>TabItem</c>; chưa qua lượt layout thì binding còn ở trạng thái <c>Unattached</c> và
    /// <c>Visibility</c> vẫn là mặc định <c>Visible</c> — đo lúc đó thì test XANH cả khi binding hỏng, tức lại
    /// một phép đo rỗng nữa (đã kiểm chứng: cùng đoạn mã, bỏ layout ⇒ đọc ra <c>Visible</c> ở cả hai chiều).</para>
    /// </summary>
    private static Visibility DungRoiDoVisibility(string? giaTriBien)
    {
        var cu = Environment.GetEnvironmentVariable(AccountsViewModel.BienMoiTruongDev);
        try
        {
            Environment.SetEnvironmentVariable(AccountsViewModel.BienMoiTruongDev, giaTriBien);
            using var temp = new TempDatabase();
            var vm = new AccountsViewModel(new AppServices(temp.Path));
            var view = new XuLyDonShopee.App.Views.AccountsView { DataContext = vm };
            view.Measure(new Size(1600, 1000));
            view.Arrange(new Rect(0, 0, 1600, 1000));
            view.UpdateLayout();
            return view.DoDomKmButton.Visibility;
        }
        finally
        {
            Environment.SetEnvironmentVariable(AccountsViewModel.BienMoiTruongDev, cu);
        }
    }

    [Fact]
    public void Xaml_PhaiRangBuoc_Visibility_vao_HienNutDev()
    {
        var xaml = DocXaml();
        var i = xaml.IndexOf("DoDomKmButton", StringComparison.Ordinal);
        Assert.True(i >= 0, "nút chẩn đoán đã bị XOÁ khỏi XAML — người dùng chốt GIỮ LẠI, xem xmldoc của HienNutDev");

        // Lấy đúng thẻ Button đó rồi soi: ràng buộc Visibility phải nằm TRONG thẻ, không phải ở đâu đó trong file.
        var ketThuc = xaml.IndexOf("</Button>", i, StringComparison.Ordinal);
        Assert.True(ketThuc > i);
        var the = xaml.Substring(i, ketThuc - i);

        Assert.Contains("Visibility=", the);
        Assert.Contains("HienNutDev", the);
        Assert.Contains("BoolToVis", the);
    }

    [Fact]
    public void Xaml_ToolTip_PhaiCanhBao_la_CO_TAO_THAT()
    {
        // Trước 19/08 tooltip ghi "KHÔNG tạo chương trình khuyến mãi nào" — đúng ở pha 1, nhưng từ khi có bước 9
        // thì thành lời nói dối nguy hiểm: người bấm tin là an toàn, thực tế tạo chương trình thật trên shop thật.
        var xaml = DocXaml();
        var i = xaml.IndexOf("DoDomKmButton", StringComparison.Ordinal);
        var ketThuc = xaml.IndexOf("</Button>", i, StringComparison.Ordinal);
        var the = xaml.Substring(i, ketThuc - i);

        Assert.DoesNotContain("KHÔNG tạo chương trình khuyến mãi nào", the);
        Assert.Matches(new Regex("CÓ TẠO", RegexOptions.IgnoreCase), the);
    }

    [Fact]
    public void Lenh_DoDomKm_van_con_ton_tai()
    {
        // Ẩn nút KHÔNG được kéo theo việc xoá lệnh: bật cờ lên mà lệnh không còn thì cờ vô nghĩa.
        var kieu = typeof(AccountsViewModel);
        Assert.NotNull(kieu.GetProperty("DoDomKmCommand"));
    }
}
