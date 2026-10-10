using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.App.Views;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>Nút "Chạy Combo" — đường chạy TAY của vòng Combo Khuyến Mãi</b> (người dùng chốt 26/08/2026).
/// <para><b>Vì sao cần đường tay:</b> trước bản này vòng combo CHỈ có lịch tự động 06–08h sáng, nên không có
/// cách nào thử <c>flow-combo.js</c> vừa viết ngoài việc ngồi chờ tới 6h sáng hôm sau — và một lượt thử hỏng
/// thì phải chờ tiếp <c>SoNgay</c> ngày.</para>
/// <para><b>Ba mệnh đề file này khoá:</b> (1) luật kiểm của <c>ThuChayCombo</c> và cờ
/// <see cref="KmDialogViewModel.VongYeuCauChay"/> ở MỌI lối ra; (2) CHỖ RẼ trong <c>MoFormKm</c> — bấm Chạy
/// Combo phải đi vào <c>ChayComboAsync</c> chứ không phải <c>ChayKmAsync</c>; (3) nút có THẬT trên tab Combo và
/// mở/khoá theo đúng <see cref="KmLichTuDong.ComboSanSang"/>.</para>
/// <para><b>Mệnh đề (2) là mệnh đề đắt nhất</b>: chỗ rẽ đó quyết định trình duyệt sắp tạo chương trình LOẠI NÀO
/// trên mọi shop thật. Rẽ nhầm nhánh là một lượt tạo Chương Trình Của Shop THẬT trong khi người dùng bấm nút
/// combo — đúng họ lỗ phủ "hàm đúng, chỗ nối không ai đo" đã cắn ba lần trong đợt này.</para>
/// </summary>
[Collection(TenCollection.KhungXuyenMay)]
public class ComboNutChayTayTests
{
    private const string Login = "sub@shopee.vn";
    private const long AccId = 1;

    /// <summary>Mốc "bây giờ" của mọi lượt bấm tay trong file — <b>08:00</b>, ngoài mọi khung tự động (đường
    /// bấm tay KHÔNG phụ thuộc khung, và ràng buộc đó cũng là thứ đang được đo).</summary>
    // ⚠ ĐỦ SỚM so với 11:00 (giờ mặc định của combo) — không chỉ "trước", mà trước ÍT NHẤT BIÊN AN của vòng
    // 3 shop (`KmLichTuDong.BienAnMocBatDau(3)` = 24 phút). Từ 26/08/2026 mốc BẮT ĐẦU là HÔM NAY lúc giờ đó,
    // nên một `BayGio` buổi chiều — hay chỉ đơn giản là quá sát 11:00 — làm MỌI lượt bấm tay bị
    // `KiemTraMocTuongLai` từ chối, biến file này thành một bộ test chỉ đo đúng nhánh từ chối.
    private static readonly DateTime BayGio = new(2026, 8, 26, 8, 0, 0);

    private static KmConfig CauHinh(bool comboBat = true) =>
        new(AccId, TuDong: false, SoNgay: 3, PhanTram: 25, GioBatDau: "09:30", GioKetThuc: "10:30",
            LanChayCuoi: null, LanThuTuDong: null)
        { ComboBat = comboBat };

    private static KmDialogViewModel Form(bool comboBat = true)
        => new(AccId, Login, soShop: 3, CauHinh(comboBat), "may-nay:orders");

    // ══════════════════════ (1) Ma trận ThuChayCombo ══════════════════════

    /// <summary>
    /// <b>Từng vế SAI của thông số combo ⇒ đúng câu lỗi của vế đó, KHÔNG có câu tổng kết, và
    /// <see cref="KmDialogViewModel.VongYeuCauChay"/> phải về <c>null</c>.</b>
    /// <para>Vế cuối là vế dễ quên nhất và đắt nhất: để sót vòng của lượt bấm TRƯỚC thì nơi gọi vẫn thấy một
    /// vòng hợp lệ để chạy, dù lượt bấm này đã bị chặn vì dữ liệu sai.</para>
    /// </summary>
    [Theory]
    // giờ: SAI ĐỊNH DẠNG. Vế "kết thúc phải SAU bắt đầu" đã BỎ (26/08/2026) cùng với ô nhập — hai đầu khoảng
    // dùng CHUNG một giờ nên luật ấy vô nghĩa. Hai ca cũ đổi sang hai hình dạng sai định dạng khác còn sống.
    [InlineData("9:00", 3, 2, 2000, 3, "Giờ của combo không hợp lệ")]
    [InlineData("11:0", 3, 2, 2000, 3, "Giờ của combo không hợp lệ")]
    [InlineData("24:00", 3, 2, 2000, 3, "Giờ của combo không hợp lệ")]
    [InlineData("11h00", 3, 2, 2000, 3, "Giờ của combo không hợp lệ")]
    // loại combo ngoài {1,2,3}
    [InlineData("11:00", 0, 2, 2000, 3, "Loại combo phải là 1")]
    [InlineData("11:00", 5, 2, 2000, 3, "Loại combo phải là 1")]
    // mua < 2 sản phẩm
    [InlineData("11:00", 3, 1, 2000, 3, "Combo phải mua tối thiểu 2 sản phẩm")]
    [InlineData("11:00", 3, 0, 2000, 3, "Combo phải mua tối thiểu 2 sản phẩm")]
    // mức giảm
    [InlineData("11:00", 3, 2, 0, 3, "Mức giảm của combo phải ≥ 1")]
    [InlineData("11:00", 2, 2, 0, 3, "Mức giảm của combo phải ≥ 1")]
    [InlineData("11:00", 2, 2, 100, 3, "Combo giảm theo % thì mức giảm phải là số nguyên từ 1 đến 99")]
    // giới hạn đặt hàng
    [InlineData("11:00", 3, 2, 2000, 0, "Giới hạn đặt hàng của combo phải ≥ 1")]
    public void ThuChayCombo_VeSai_ThiDungCauLoi_KhongTongKet_KhongVongYeuCau(
        string gio, int loai, int muaSl, int giam, int gioiHan, string manhLoi)
    {
        var vm = Form();
        vm.ComboGioBatDau = gio;
        vm.ComboLoai = loai;
        vm.ComboMuaSl = muaSl;
        vm.ComboGiam = giam;
        vm.ComboGioiHan = gioiHan;

        Assert.Null(vm.ThuChayCombo(BayGio));
        Assert.Contains(manhLoi, vm.Loi);
        Assert.Null(vm.TongKet);
        Assert.Null(vm.VongYeuCauChay);
    }

    /// <summary>
    /// <b>Thông số hợp lệ ⇒ có câu tổng kết, hết lỗi, và vòng yêu cầu là COMBO.</b>
    /// <para>Câu tổng kết phải đủ để đối chiếu về sau: tên · ngày · khung giờ · mức ưu đãi · giới hạn · phạm vi
    /// shop. Kiểm từng vế chứ không chỉ kiểm "khác null" — một câu rỗng cũng khác null.</para>
    /// <para><b>Hai mốc lấy qua <see cref="KmLichTuDong.NgayBatDau"/> / <see cref="KmLichTuDong.NgayKetThuc"/></b>
    /// (hôm nay → hôm nay + 180), KHÔNG phải ô ngày của tab Chương Trình Của Shop: vế đối chứng bên dưới đổi
    /// <c>vm.Ngay</c> đi chỗ khác rồi khẳng định câu tổng kết KHÔNG hề nhắc tới nó.</para>
    /// </summary>
    [Fact]
    public void ThuChayCombo_HopLe_ThiCoTongKet_VaVongYeuCauLaCombo()
    {
        var vm = Form();
        vm.Ngay = new DateTime(2026, 12, 31);   // ô ngày của TAB KIA — vòng combo không được đọc nó

        var cau = vm.ThuChayCombo(BayGio);

        Assert.NotNull(cau);
        Assert.Null(vm.Loi);
        Assert.Equal(cau, vm.TongKet);
        Assert.Equal(LoaiChuongTrinh.Combo, vm.VongYeuCauChay);

        var ngayBd = KmLichTuDong.NgayBatDau(BayGio);
        var ngayKt = KmLichTuDong.NgayKetThuc(BayGio);
        Assert.StartsWith("Combo Khuyến Mãi — BẮT ĐẦU CHẠY:", cau);
        Assert.Contains("Combo " + ngayBd.ToString("dd-MM-yyyy"), cau);   // tên mang ngày BẮT ĐẦU
        // CẢ HAI mốc, CÙNG giờ — bản vá gửi một mốc cho hai ô sẽ đỏ ở đây.
        Assert.Contains(ngayBd.ToString("dd-MM-yyyy") + " 11:00 → " + ngayKt.ToString("dd-MM-yyyy") + " 11:00", cau);
        Assert.Contains("mua 2 sp giảm 2.000đ", cau);
        Assert.Contains("giới hạn đặt hàng 3", cau);
        Assert.Contains("3 shop của " + Login, cau);

        // Vế ĐỐI CHỨNG: ô ngày của tab kia KHÔNG rò vào câu của vòng combo.
        Assert.DoesNotContain("31-12-2026", cau);
    }

    /// <summary>
    /// <b>Câu tổng kết nói ra TRẦN CHẾ ĐỘ THỬ</b> (<see cref="KmLichTuDong.TranShopLuotCombo"/>) — người bấm nút
    /// phải biết TRƯỚC rằng lượt sắp chạy dừng sau 2 shop, chứ không phải đọc ra sau khi nó đã dừng.
    /// <para>Buộc thẳng vào hằng nên đặt hằng về 0 (bỏ trần) là test tự đổi vế theo, không phải sửa tay.</para>
    /// </summary>
    [Fact]
    public void ThuChayCombo_TongKet_NoiRaTranCheDoThu()
    {
        var cau = Form().ThuChayCombo(BayGio)!;
        // Chép hằng vào biến để trình biên dịch thôi gấp hằng và cắt hẳn một nhánh (CS0162): cả hai nhánh đều
        // là mệnh đề thật, chỉ có một nhánh đúng tại một thời điểm.
        var tran = KmLichTuDong.TranShopLuotCombo;

        if (tran > 0)
        {
            Assert.Contains("CHẾ ĐỘ THỬ", cau);
            Assert.Contains($"tối đa {tran} shop", cau);
        }
        else
        {
            Assert.DoesNotContain("CHẾ ĐỘ THỬ", cau);
        }
    }

    // ══════════════════════ (2) Bấm nút NÀY thắng nút bấm TRƯỚC ══════════════════════

    /// <summary>
    /// <b>Bấm Chạy chương trình rồi bấm Chạy Combo ⇒ vòng yêu cầu là COMBO</b> — vòng của lượt bấm SAU, không
    /// sót vòng trước.
    /// </summary>
    [Fact]
    public void BamKmRoiBamCombo_ThiVongYeuCauLaCombo()
    {
        var vm = Form();

        Assert.NotNull(vm.ThuChay(BayGio));
        Assert.Equal(LoaiChuongTrinh.ChuongTrinhShop, vm.VongYeuCauChay);

        Assert.NotNull(vm.ThuChayCombo(BayGio));
        Assert.Equal(LoaiChuongTrinh.Combo, vm.VongYeuCauChay);
    }

    /// <summary>Chiều ngược lại — bấm Chạy Combo rồi bấm Chạy chương trình ⇒ vòng yêu cầu là CHƯƠNG TRÌNH CỦA
    /// SHOP. Thiếu chiều này thì một bản "luôn luôn Combo" cũng xanh.</summary>
    [Fact]
    public void BamComboRoiBamKm_ThiVongYeuCauLaChuongTrinhShop()
    {
        var vm = Form();

        Assert.NotNull(vm.ThuChayCombo(BayGio));
        Assert.Equal(LoaiChuongTrinh.Combo, vm.VongYeuCauChay);

        Assert.NotNull(vm.ThuChay(BayGio));
        Assert.Equal(LoaiChuongTrinh.ChuongTrinhShop, vm.VongYeuCauChay);
    }

    /// <summary>
    /// <b>Lượt bấm SAU có dữ liệu SAI ⇒ vòng của lượt TRƯỚC phải bị xoá.</b> Đây là ca hỏng thật của một bản chỉ
    /// gán <c>VongYeuCauChay</c> ở nhánh thành công: người dùng bấm Chạy Combo hợp lệ, đổi ý, sửa giờ KM cho sai
    /// rồi bấm Chạy chương trình — form ở lại (đúng), nhưng nếu họ bấm Đóng thì nơi gọi vẫn còn thấy một vòng
    /// combo để chạy.
    /// </summary>
    [Fact]
    public void BamComboHopLe_RoiBamKmSai_ThiVongYeuCauVeNull()
    {
        var vm = Form();
        Assert.NotNull(vm.ThuChayCombo(BayGio));

        vm.GioBatDau = "9h30";   // sai định dạng ⇒ lượt bấm KM này SAI (vế "kết thúc ≤ bắt đầu" đã bỏ)
        Assert.Null(vm.ThuChay(BayGio));

        Assert.Null(vm.VongYeuCauChay);
        Assert.Null(vm.TongKet);
    }

    /// <summary>Chiều ngược lại: bấm KM hợp lệ rồi bấm Combo với thông số sai ⇒ cũng phải về <c>null</c>.</summary>
    [Fact]
    public void BamKmHopLe_RoiBamComboSai_ThiVongYeuCauVeNull()
    {
        var vm = Form();
        Assert.NotNull(vm.ThuChay(BayGio));

        vm.ComboMuaSl = 1;
        Assert.Null(vm.ThuChayCombo(BayGio));

        Assert.Null(vm.VongYeuCauChay);
        Assert.Null(vm.TongKet);
    }

    // ══════════════════════ (3) CHỖ GỌI: MoFormKm rẽ đúng vòng ══════════════════════

    private static (TempDatabase Temp, AppServices Services, AccountsViewModel Vm) DungVm(string email)
    {
        var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = email, Password = "p" });
        var vm = new AccountsViewModel(services);
        vm.SelectedRow = vm.Accounts.First(a => a.Email == email);
        var id = services.Accounts.GetAll().First(a => a.Email == email).Id;
        services.KmConfig.Save(CauHinh() with { AccountId = id });
        return (temp, services, vm);
    }

    /// <summary>
    /// Chạy <c>MoFormKm</c> với một form GIẢ đã bấm sẵn một trong hai nút, rồi trả về nhật ký.
    /// <para>Cho hook khung xuyên máy trả "máy khác đang giữ" nên lượt DỪNG NGAY Ở CỬA: không xin khoá tài
    /// khoản, không phóng trình duyệt — mà dòng nhật ký của cửa đó lại NÊU ĐÍCH DANH TÊN VIỆC, tức nó là thước
    /// đo rẻ và không thể nhầm của câu hỏi "vừa rẽ vào vòng nào".</para>
    /// </summary>
    private static async Task<(string Nhat, int SoLanXinKhoa)> ChayMoFormKmAsync(string email, bool bamCombo)
    {
        var hanGoc = AccountsViewModel.MacDinhHanChoKhung;
        var nhipGoc = AccountsViewModel.MacDinhNhipHoiKhung;
        AccountsViewModel.HanChoKhungXuyenMay = TimeSpan.FromMilliseconds(600);
        AccountsViewModel.NhipHoiKhungXuyenMay = TimeSpan.FromMilliseconds(50);
        try
        {
            var (temp, services, vm) = DungVm(email);
            using var db = temp;
            var soLanXinKhoa = 0;
            services.AcquireAccountLease = (l, ct) => { soLanXinKhoa++; return Task.FromResult(new OrdersLeaseResult(true, null)); };
            services.ReleaseAccountLease = l => Task.CompletedTask;
            services.XinKhungKm = (l, ct) => Task.FromResult(new KmKhungResult(false, "NUC"));
            services.TraKhungKm = l => Task.CompletedTask;

            // Form GIẢ: đứng đúng chỗ modal WPF, bấm đúng một nút rồi trả "có chạy".
            vm.MoHopThoaiKm = form => (bamCombo ? form.ThuChayCombo(BayGio) : form.ThuChay(BayGio)) is not null;

            await vm.MoFormKmCommand.ExecuteAsync(null);

            return (string.Join("\n", services.Log.Snapshot(email).Select(d => d.Message)), soLanXinKhoa);
        }
        finally
        {
            AccountsViewModel.HanChoKhungXuyenMay = hanGoc;
            AccountsViewModel.NhipHoiKhungXuyenMay = nhipGoc;
        }
    }

    /// <summary>
    /// Y hệt <see cref="ChayMoFormKmAsync"/> (bấm "Chạy Combo"), nhưng GIỮ LẠI cả VM lẫn chính object form mà
    /// <c>MoFormKm</c> vừa dựng — để so THAM CHIẾU giữa gói đã kiểm và gói đã gửi.
    /// </summary>
    private static async Task<(AccountsViewModel Vm, KmDialogViewModel? Form)> ChayMoFormKmBatFormAsync(
        string email, DateTime luc)
    {
        var hanGoc = AccountsViewModel.MacDinhHanChoKhung;
        var nhipGoc = AccountsViewModel.MacDinhNhipHoiKhung;
        AccountsViewModel.HanChoKhungXuyenMay = TimeSpan.FromMilliseconds(600);
        AccountsViewModel.NhipHoiKhungXuyenMay = TimeSpan.FromMilliseconds(50);
        try
        {
            var (temp, services, vm) = DungVm(email);
            using var db = temp;
            services.AcquireAccountLease = (l, ct) => Task.FromResult(new OrdersLeaseResult(true, null));
            services.ReleaseAccountLease = l => Task.CompletedTask;
            services.XinKhungKm = (l, ct) => Task.FromResult(new KmKhungResult(false, "NUC"));
            services.TraKhungKm = l => Task.CompletedTask;

            KmDialogViewModel? batDuoc = null;
            vm.MoHopThoaiKm = form =>
            {
                batDuoc = form;
                return form.ThuChayCombo(luc) is not null;
            };

            await vm.MoFormKmCommand.ExecuteAsync(null);
            return (vm, batDuoc);
        }
        finally
        {
            AccountsViewModel.HanChoKhungXuyenMay = hanGoc;
            AccountsViewModel.NhipHoiKhungXuyenMay = nhipGoc;
        }
    }

    /// <summary>
    /// <b>Bấm "Chạy Combo" ⇒ <c>MoFormKm</c> đi vào ĐƯỜNG COMBO, KHÔNG đi đường KM.</b>
    /// <para>Đây là mệnh đề trung tâm của cả file. Một bản luôn gọi <c>ChayKmAsync</c> vẫn làm mọi test hàm
    /// thuần ở trên xanh, mà production thì mở trình duyệt tạo <b>Chương Trình Của Shop THẬT</b> trên mọi shop
    /// trong khi người dùng bấm nút combo.</para>
    /// </summary>
    [Fact]
    public async Task MoFormKm_BamChayCombo_ThiDiDuongCombo_KhongDiDuongKm()
    {
        var (nhat, soLanXinKhoa) = await ChayMoFormKmAsync("combo-tay@mail.com", bamCombo: true);

        Assert.Contains("Combo Khuyến Mãi — BẮT ĐẦU CHẠY:", nhat);      // câu tổng kết của vòng combo
        Assert.Contains("BỎ lượt Combo Khuyến Mãi", nhat);              // …và cửa khung nêu ĐÚNG tên việc
        Assert.DoesNotContain("BỎ lượt KM của Shop", nhat);
        Assert.DoesNotContain("KM của Shop — BẮT ĐẦU CHẠY", nhat);
        Assert.Equal(0, soLanXinKhoa);                                  // dừng ở cửa, KHÔNG phóng trình duyệt
    }

    /// <summary>
    /// <b>Vế ĐỐI CHỨNG — bấm "Chạy chương trình" vẫn đi ĐƯỜNG KM, không đổi một dòng nào.</b> Thiếu vế này thì
    /// một bản "lúc nào cũng chạy combo" cũng xanh ở test trên, và hậu quả đúng bằng chiều ngược lại.
    /// </summary>
    [Fact]
    public async Task MoFormKm_BamChayChuongTrinh_ThiVanDiDuongKm()
    {
        var (nhat, soLanXinKhoa) = await ChayMoFormKmAsync("km-tay@mail.com", bamCombo: false);

        Assert.Contains("KM của Shop — BẮT ĐẦU CHẠY:", nhat);
        Assert.Contains("BỎ lượt KM của Shop", nhat);
        Assert.DoesNotContain("BỎ lượt Combo Khuyến Mãi", nhat);
        Assert.DoesNotContain("Combo Khuyến Mãi — BẮT ĐẦU CHẠY", nhat);
        Assert.Equal(0, soLanXinKhoa);
    }

    /// <summary>
    /// <b>Gói được GỬI phải là CHÍNH gói đã KIỂM — không phải một lượt đọc đồng hồ thứ hai.</b>
    /// <para><b>Ca hỏng thật</b> (phản biện 26/08/2026, VỪA-3): <c>MoFormKm</c> từng gọi
    /// <c>DungThamSoCombo(vm.LayCauHinh(), DateTime.Now)</c> lần thứ hai, trong khi <c>ThuChayCombo</c> đã dựng
    /// gói bằng <c>luc</c> lần một và dùng nó cho phép kiểm + câu tổng kết. Bấm lúc <c>23:59:5x</c> ⇒ nhật ký
    /// nói <c>D → D+180</c>, Shopee nhận <c>D+1 → D+181</c>.</para>
    /// <para><b>Cách đo — SO THAM CHIẾU, không so giá trị:</b> giữ lại chính object form mà <c>MoFormKm</c> vừa
    /// dùng, rồi khẳng định gói nó gửi đi (<c>AccountsViewModel.GoiComboBamTayVuaGui</c>) là CÙNG MỘT object
    /// với gói nó vừa kiểm (<c>KmDialogViewModel.ThamSoComboDaKiem</c>). So giá trị thì một đột biến "dựng lại
    /// bằng <c>DateTime.Now</c>" vẫn xanh mọi hôm mà hai lượt đọc tình cờ cùng ngày — tức gần như mọi hôm.</para>
    /// <para>Rót thêm một mốc bấm ở NGÀY KHÁC để phép so giá trị đi kèm cũng có nghĩa.</para>
    /// </summary>
    [Fact]
    public async Task MoFormKm_GuiDUNG_goiDaKiem_KhongDocDongHoLanHai()
    {
        var moc = new DateTime(2026, 8, 19, 8, 0, 0);
        var (vm, form) = await ChayMoFormKmBatFormAsync("combo-goi-da-kiem@mail.com", moc);

        Assert.NotNull(form);
        Assert.NotNull(form!.ThamSoComboDaKiem);
        Assert.NotNull(vm.GoiComboBamTayVuaGui);

        // MỆNH ĐỀ TRUNG TÂM: CÙNG MỘT object, không phải "hai object bằng nhau".
        Assert.Same(form.ThamSoComboDaKiem, vm.GoiComboBamTayVuaGui);

        // …và gói ấy mang ngày của MỐC BẤM, không phải ngày thật của máy.
        Assert.Equal(new DateTime(2026, 8, 19), vm.GoiComboBamTayVuaGui!.NgayBatDau);
        Assert.Equal(new DateTime(2027, 2, 15), vm.GoiComboBamTayVuaGui.NgayKetThuc);
    }

    /// <summary>
    /// Vế hàm thuần của mệnh đề trên: <see cref="KmDialogViewModel.ThamSoComboDaKiem"/> mang ĐÚNG gói mà
    /// <c>ThuChayCombo</c> vừa kiểm, và bị XOÁ ở mọi lối ra không phải "combo hợp lệ" — y khuôn
    /// <see cref="KmDialogViewModel.VongYeuCauChay"/>.
    /// </summary>
    [Fact]
    public void ThamSoComboDaKiem_MangDungGoiVuaKiem_VaBiXoaOMoiLoiRaKhac()
    {
        var vm = Form();
        Assert.Null(vm.ThamSoComboDaKiem);   // chưa bấm gì

        Assert.NotNull(vm.ThuChayCombo(BayGio));
        var goi = vm.ThamSoComboDaKiem;
        Assert.NotNull(goi);
        Assert.Equal(KmLichTuDong.NgayBatDau(BayGio), goi!.NgayBatDau);
        Assert.Equal(KmLichTuDong.NgayKetThuc(BayGio), goi.NgayKetThuc);
        // …và ĐÚNG gói đó đã đi vào câu tổng kết (khuôn của `CauBatDauChayCombo`, không phải `MoTa`).
        Assert.Contains(
            $"{goi.Ten} · {goi.NgayBatDau:dd-MM-yyyy} {goi.Gio} → {goi.NgayKetThuc:dd-MM-yyyy} {goi.Gio}",
            vm.TongKet);

        // Bấm KM (hợp lệ) ⇒ gói combo của lượt trước phải biến mất cùng `VongYeuCauChay`.
        Assert.NotNull(vm.ThuChay(BayGio));
        Assert.Null(vm.ThamSoComboDaKiem);

        // Lượt combo SAI ⇒ cũng phải null, không để sót gói của lượt hợp lệ trước đó.
        Assert.NotNull(vm.ThuChayCombo(BayGio));
        Assert.NotNull(vm.ThamSoComboDaKiem);
        vm.ComboMuaSl = 1;
        Assert.Null(vm.ThuChayCombo(BayGio));
        Assert.Null(vm.ThamSoComboDaKiem);
    }

    /// <summary>Đóng form mà không bấm nút chạy nào ⇒ KHÔNG chạy vòng nào cả (không có câu BẮT ĐẦU CHẠY, không
    /// chạm cửa khung xuyên máy). Vế thứ ba của chỗ rẽ.</summary>
    [Fact]
    public async Task MoFormKm_ChiDongForm_ThiKhongChayVongNao()
    {
        var hanGoc = AccountsViewModel.MacDinhHanChoKhung;
        AccountsViewModel.HanChoKhungXuyenMay = TimeSpan.FromMilliseconds(600);
        try
        {
            var (temp, services, vm) = DungVm("dong-form@mail.com");
            using var db = temp;
            var soLanXinKhung = 0;
            services.XinKhungKm = (l, ct) => { soLanXinKhung++; return Task.FromResult(new KmKhungResult(true, null)); };
            vm.MoHopThoaiKm = _ => false;   // bấm "Đóng"

            await vm.MoFormKmCommand.ExecuteAsync(null);

            var nhat = string.Join("\n", services.Log.Snapshot("dong-form@mail.com").Select(d => d.Message));
            Assert.DoesNotContain("BẮT ĐẦU CHẠY", nhat);
            Assert.Equal(0, soLanXinKhung);
        }
        finally
        {
            AccountsViewModel.HanChoKhungXuyenMay = hanGoc;
        }
    }

    // ══════════════════════ (4) Nút có THẬT trên tab Combo ══════════════════════

    /// <summary>
    /// <b>Nút "Chạy Combo" có thật trên tab Combo của form THẬT</b>, và <c>IsEnabled</c> bám ĐÚNG
    /// <see cref="KmLichTuDong.ComboSanSang"/>.
    /// <para>Đo trên control thật sau một lượt layout thật, và khẳng định nó nằm TRONG cây con của tab Combo —
    /// một property VM đúng mà XAML quên ràng buộc thì người dùng vẫn bấm được, đúng bài học
    /// <c>NutChanDoanAnTests</c>.</para>
    /// </summary>
    [StaFact]
    public void NutChayCombo_CoThatTrenTabCombo_VaBamComboSanSang()
    {
        var dlg = DungDialog(comboBat: true);

        // LƯỚI CHỐNG RÀ RỖNG: tab Combo là tab THỨ HAI, TabControl không dựng tab chưa chọn vào cây trực quan.
        Assert.Contains((DependencyObject)dlg.ChayComboButton, LayoutMotLuot(dlg));
        Assert.Contains((DependencyObject)dlg.ChayComboButton, CayCon(dlg.ComboBox));

        Assert.Equal(KmLichTuDong.ComboSanSang, dlg.ChayComboButton.IsEnabled);
        Assert.Equal("Chạy Combo", NhanNut(dlg.ChayComboButton));
    }

    /// <summary>
    /// <b>Nút ĐỘC LẬP với ô tick "chạy tự động"</b> — không tick vẫn bấm chạy được, y hệt quan hệ giữa nút "Chạy
    /// chương trình" và ô tick <c>TuDong</c> của vòng kia.
    /// <para>Vế này bắt đúng ca một bản đặt nút vào trong khối <c>IsEnabled="{Binding ChoNhapCombo}"</c>: mọi
    /// phép đo khác vẫn xanh, còn người dùng thì không có đường nào thử vòng combo mà không bật lịch tự động
    /// 6h sáng lên trước.</para>
    /// </summary>
    [StaFact]
    public void NutChayCombo_KhongTickChayTuDong_VanBamDuoc()
    {
        var dlg = DungDialog(comboBat: false);
        LayoutMotLuot(dlg);

        Assert.False(dlg.ComboBatCheck.IsChecked ?? true);
        Assert.Equal(KmLichTuDong.ComboSanSang, dlg.ChayComboButton.IsEnabled);

        // …trong khi các Ô NHẬP thông số thì vẫn mờ theo ô tick (hành vi cũ, không được đổi).
        Assert.False(dlg.ComboGiamBox.IsEnabled);
    }

    /// <summary>
    /// Tooltip nói ra bốn điều người bấm phải biết: CHẠY THẬT · phạm vi mọi shop · độc lập với ô tick · và
    /// TRẦN chế độ thử.
    /// <para><b>Vế trần buộc vào chính hằng</b>: con số trong tooltip là chuỗi cứng trong XAML, không đọc được
    /// <see cref="KmLichTuDong.TranShopLuotCombo"/> — nên nếu không có phép so này thì đổi hằng xong là tooltip
    /// nói dối, câm lặng. Đây đúng họ lỗi X2 (câu chữ trỏ vào thứ không còn đúng).</para>
    /// </summary>
    [StaFact]
    public void TooltipNutChayCombo_NoiRaChayThat_MoiShop_DocLapVoiOTick_VaTranCheDoThu()
    {
        var dlg = DungDialog(comboBat: true);
        LayoutMotLuot(dlg);
        var tip = dlg.ChayComboButton.ToolTip?.ToString() ?? "";
        var tran = KmLichTuDong.TranShopLuotCombo;   // chép ra biến để trình biên dịch thôi gấp hằng

        Assert.Contains("CHẠY THẬT", tip);
        Assert.Contains("MỌI shop", tip);
        Assert.Contains("ĐỘC LẬP", tip);
        Assert.Contains("chạy tự động", tip);

        if (tran > 0)
        {
            Assert.Contains("CHẾ ĐỘ THỬ", tip);
            Assert.Contains($"dừng sau {tran} shop", tip);
        }
        else
        {
            Assert.DoesNotContain("CHẾ ĐỘ THỬ", tip);
        }
    }

    private static KmDialog DungDialog(bool comboBat)
        => new(new KmDialogViewModel(AccId, Login, 3, CauHinh(comboBat), "may-nay:orders"));

    private static string NhanNut(Button nut)
        => CayCon(nut).OfType<TextBlock>().Select(t => t.Text?.Trim() ?? "")
            .FirstOrDefault(t => t.Length > 0) ?? "";

    /// <summary>Cho nội dung form đi qua một lượt layout thật, LẶP QUA TỪNG TAB (TabControl không dựng nội dung
    /// tab chưa chọn) rồi trả về mọi phần tử trực quan gặp được.</summary>
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
}
