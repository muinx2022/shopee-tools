using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>CỬA MÁY CHỦ TRÌ của lượt KM tự động + đường LƯU FORM</b> — hai hạng mục sinh ra từ lượt phản biện
/// 22/08/2026, khi hồ sơ KM bắt đầu đồng bộ xuyên máy.
/// <para><b>Vì sao cửa chủ trì là bắt buộc, không phải "để pha sau":</b> trước đồng bộ, <c>TuDong</c> chỉ bật ở
/// đúng máy người dùng mở form nên "ai chạy lượt tự động" tự phân xử. Đồng bộ xong thì cờ ấy lan sang MỌI máy
/// giữ tài khoản đó ⇒ 02:00 cả fleet cùng tới hạn, cùng dừng phiên check đơn cục bộ, cùng đốt lượt thử của
/// đêm, và máy thắng khoá là ngẫu nhiên. Tức là <b>chỉ đồng bộ mà không có cửa này thì tình hình TỆ HƠN</b>
/// lúc chưa đồng bộ.</para>
/// <para><b>Vì sao đường lưu form đáng test:</b> nó là bước 5–6 của ca mất cấu hình đã dựng lại được — mở form
/// ra XEM rồi bấm Đóng mà cũng đóng dấu "vừa sửa" + cướp chủ trì, thế là một hồ sơ rỗng thắng cấu hình thật
/// trên toàn fleet.</para>
/// </summary>
public class KmChuTriVaLuuFormTests
{
    private const string MayNay = "may-nay:orders";
    private const string MayKhac = "may-khac:orders";

    /// <summary>Trong khung KM 2–6h, đã tới hạn (chưa chạy lần nào) ⇒ <c>DenGioChay</c> true.</summary>
    private static readonly DateTime TrongKhung = new(2026, 8, 19, 2, 5, 0);

    /// <summary>Số lần THÂN lượt KM đi tới cửa xin khoá — bằng 0 nghĩa là lượt đó chưa hề vào thân (W12: assert
    /// TRỰC TIẾP thay vì suy gián tiếp từ mốc <c>LanThuTuDong</c>).</summary>
    private int _soLanXinKhoa;

    private (TempDatabase Temp, AppServices Services, AccountsViewModel Vm, long AccountId)
        DungVm(string email, string? mayNay)
    {
        var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = email, Password = "p" });
        var id = services.Accounts.GetAll().First(a => a.Email == email).Id;

        // "Máy khác đang giữ khoá" ⇒ lượt KM nào lọt qua cửa chủ trì cũng dừng ngay ở cửa khoá, KHÔNG phóng
        // Brave nào (khuôn KmNhipTuDongTests). Hook này cũng là DẤU CHÂN của thân lượt KM: nó nằm trong
        // `ChayLatCatAsync`, sau khi lượt đã quyết chạy — đếm nó là đo trực tiếp "có vào thân hay không".
        services.AcquireAccountLease = (login, ct) =>
        {
            _soLanXinKhoa++;
            return Task.FromResult(new OrdersLeaseResult(false, "MAY-GIU"));
        };
        services.ReleaseAccountLease = login => Task.CompletedTask;
        services.MayNay = mayNay is null ? null : () => mayNay;

        return (temp, services, new AccountsViewModel(services), id);
    }

    private static KmConfig LichBat(long id, string ownerMachine) => new(
        id, TuDong: true, SoNgay: 1, PhanTram: 5, GioBatDau: "09:30", GioKetThuc: "10:30", LanChayCuoi: null)
    {
        OwnerMachine = ownerMachine,
    };

    // ══════════════════════════ Luật thuần: LaMayChuTri ══════════════════════════

    [Theory]
    [InlineData(MayNay, MayNay, true)]                    // đúng máy chủ trì
    [InlineData("MAY-NAY:ORDERS", MayNay, true)]          // so không phân biệt hoa/thường
    [InlineData("  " + MayNay + "  ", MayNay, true)]      // dữ liệu có khoảng trắng thừa
    [InlineData(MayKhac, MayNay, false)]                  // máy khác chủ trì ⇒ đứng ngoài
    [InlineData("", MayNay, true)]                        // KHÔNG AI chủ trì ⇒ hành vi như trước khi có cửa này
    [InlineData(MayKhac, "", false)]                      // ĐÃ CÓ chủ trì mà mình không biết mình là ai ⇒ ĐỨNG NGOÀI
    [InlineData("", "", true)]                            // không ai chủ trì ⇒ vẫn chạy (máy một mình, không hub)
    public void LaMayChuTri_MaTran(string ownerMachine, string mayNay, bool mong)
        => Assert.Equal(mong, KmLichTuDong.LaMayChuTri(ownerMachine, mayNay));

    /// <summary>
    /// Tham số <c>null</c> đi cùng đường với chuỗi rỗng — và <b>hai chiều ra hai kết quả KHÁC nhau</b>, đúng luật
    /// mới: chủ trì null = "không ai chủ trì" ⇒ chạy; máy này null trong khi ĐÃ CÓ chủ trì = "không biết mình là
    /// ai" ⇒ đứng ngoài.
    /// <para><b>Khẳng định thứ hai vừa bị LẬT (22/08/2026)</b> — bản cũ assert <c>True</c>, và chính nó khoá con
    /// bug: máy vừa bấm NGẮT hub ở Cài đặt (không cần khởi động lại) tự nhận mình là chủ trì và chạy lượt KM
    /// THẬT của máy khác. Test khoá đúng cái hành vi sai thì bản vá đúng sẽ làm nó đỏ — đó là lý do đổi, không
    /// phải nới assert.</para>
    /// </summary>
    [Fact]
    public void LaMayChuTri_ThamSoNull_DiCungDuongVoiChuoiRong()
    {
        Assert.True(KmLichTuDong.LaMayChuTri(null, MayNay));
        Assert.False(KmLichTuDong.LaMayChuTri(MayKhac, null));
    }

    // ══════════════════════════ Nhịp lịch: cửa chủ trì ══════════════════════════

    /// <summary>
    /// <b>Máy khác chủ trì ⇒ KHÔNG chạy và KHÔNG ghi <c>LanThuTuDong</c>.</b> Vế "không ghi" quan trọng ngang
    /// vế "không chạy": mốc đó là "tài khoản này đã dùng lượt thử của khung đêm nay" và nó là mốc CHUNG xuyên
    /// máy — ghi vào là đốt lượt của đúng máy chủ trì, nó mất trắng đêm đó.
    /// </summary>
    [StaFact]
    public async Task MayKhacChuTri_ThiKhongChay_VaKhongGhiMocThu()
    {
        var (temp, services, vm, id) = DungVm("km-chutri-khac@mail.com", MayNay);
        using var db = temp;
        services.KmConfig.Save(LichBat(id, MayKhac));

        await vm.NhipLichKmAsync(TrongKhung);

        Assert.Null(services.KmConfig.Get(id).LanThuTuDong);

        // (W12) Assert TRỰC TIẾP là lượt KM KHÔNG vào thân — vế "mốc thử còn null" ở trên chỉ suy ra gián tiếp
        // từ thứ tự các câu lệnh trong impl, nên một bản "vẫn chạy nhưng quên ghi mốc" cũng làm nó xanh.
        Assert.Equal(0, _soLanXinKhoa);
        Assert.DoesNotContain(services.Log.Snapshot("km-chutri-khac@mail.com"),
            e => e.Message.Contains("bắt đầu lượt KM"));
    }

    /// <summary>Vế ĐỐI CHỨNG của W12 — đúng máy chủ trì thì lượt VÀO thân thật (chạm tới cửa xin khoá). Không
    /// có vế này thì "đếm bằng 0" chỉ chứng minh cái đếm không bao giờ tăng.</summary>
    [StaFact]
    public async Task DungMayChuTri_ThiVaoThanLuotKm()
    {
        var (temp, services, vm, id) = DungVm("km-chutri-than@mail.com", MayNay);
        using var db = temp;
        services.KmConfig.Save(LichBat(id, MayNay));

        await vm.NhipLichKmAsync(TrongKhung);

        Assert.Equal(1, _soLanXinKhoa);
        Assert.Contains(services.Log.Snapshot("km-chutri-than@mail.com"),
            e => e.Message.Contains("bắt đầu lượt KM"));
    }

    /// <summary>Vế ĐỐI CHỨNG — đúng máy chủ trì thì lượt vẫn chạy (và ghi mốc thử như cũ). Thiếu vế này thì một
    /// bản "không bao giờ chạy" cũng xanh, mà bản đó tắt hẳn tính năng.</summary>
    [StaFact]
    public async Task DungMayChuTri_ThiVanChay_VaGhiMocThu()
    {
        var (temp, services, vm, id) = DungVm("km-chutri-dung@mail.com", MayNay);
        using var db = temp;
        services.KmConfig.Save(LichBat(id, MayNay));

        await vm.NhipLichKmAsync(TrongKhung);

        Assert.Equal(TrongKhung, services.KmConfig.Get(id).LanThuTuDong);
    }

    /// <summary>ĐỘ LÙI 1 — dữ liệu cũ chưa có chủ trì (<c>OwnerMachine</c> rỗng) ⇒ chạy y hành vi hiện tại.</summary>
    [StaFact]
    public async Task ChuTriRong_ThiChayNhuCu()
    {
        var (temp, services, vm, id) = DungVm("km-chutri-rong@mail.com", MayNay);
        using var db = temp;
        services.KmConfig.Save(LichBat(id, ownerMachine: ""));

        await vm.NhipLichKmAsync(TrongKhung);

        Assert.Equal(TrongKhung, services.KmConfig.Get(id).LanThuTuDong);
    }

    /// <summary>
    /// Hồ sơ ghi MỘT MÁY KHÁC chủ trì, còn máy này không biết mình là ai (hook <c>MayNay</c> null: chưa cấu hình
    /// hub, vừa bấm NGẮT ở Cài đặt, hoặc đang trong cửa sổ <c>Reconnect()</c>) ⇒ <b>KHÔNG chạy</b>, và
    /// <b>KHÔNG đốt <c>LanThuTuDong</c></b> — lượt của đêm nay là của máy chủ trì thật.
    /// <para><b>Khẳng định này vừa bị LẬT (22/08/2026)</b>, và bản cũ chính là con bug: nó assert rằng máy không
    /// hub vẫn chạy "y hành vi hiện tại, KỂ CẢ khi hồ sơ ghi một máy chủ trì khác". Hậu quả thật: bấm NGẮT hub ở
    /// Cài đặt (một cú, không cần khởi động lại) là 2h sáng máy này chạy trọn lượt KM của tài khoản máy A đang
    /// chủ trì — hai máy cùng TẠO CHƯƠNG TRÌNH KHUYẾN MÃI THẬT trên cùng tập shop, rồi mốc thử chung bị đốt nên
    /// máy A mất luôn lượt của đêm đó.</para>
    /// <para>Ca "máy một mình, không hub" KHÔNG bị ảnh hưởng: ở đó chẳng ai đóng dấu chủ trì nên
    /// <c>owner_machine</c> rỗng, và <c>LaMayChuTri("", …)</c> vẫn trả <c>true</c> — xem test ngay trên.</para>
    /// </summary>
    [StaFact]
    public async Task KhongBietMinhLaAi_MaHoSoGhiMayKhac_ThiKhongChay()
    {
        var (temp, services, vm, id) = DungVm("km-chutri-nohub@mail.com", mayNay: null);
        using var db = temp;
        services.KmConfig.Save(LichBat(id, MayKhac));

        await vm.NhipLichKmAsync(TrongKhung);

        Assert.Null(services.KmConfig.Get(id).LanThuTuDong);
    }

    /// <summary>
    /// Dòng nhật ký "máy khác chủ trì" ghi ĐÚNG MỘT LẦN mỗi tài khoản mỗi đêm.
    /// <para>Cửa chủ trì cố ý không ghi <c>LanThuTuDong</c>, nên <c>DenGioChay</c> còn trả true suốt khung
    /// 2–6h ⇒ nhịp 60 giây đi qua ~240 lần. Không chặn thì đúng một dòng này lặp 240 lần mỗi đêm, và nhật ký
    /// spam thì người dùng thôi đọc — mất luôn tác dụng của chính dòng log.</para>
    /// </summary>
    [StaFact]
    public async Task MayKhacChuTri_ChiGhiMotDongLogMoiDem()
    {
        var (temp, services, vm, id) = DungVm("km-chutri-log@mail.com", MayNay);
        using var db = temp;
        services.KmConfig.Save(LichBat(id, MayKhac));

        await vm.NhipLichKmAsync(TrongKhung);
        await vm.NhipLichKmAsync(TrongKhung.AddMinutes(1));
        await vm.NhipLichKmAsync(TrongKhung.AddMinutes(2));

        var dong = services.Log.Snapshot("km-chutri-log@mail.com")
            .Where(e => e.Message.Contains("MÁY CHỦ TRÌ")).ToList();
        Assert.Single(dong);
        // Máy chủ trì hiện bằng NHÃN (tên + id NGẮN), không phải id thô — X6, vòng 4.
        Assert.Contains(KmLichTuDong.NhanMay(MayKhac, null), dong[0].Message);
        Assert.DoesNotContain(MayKhac, dong[0].Message);

        // (W4) Câu log phải nêu ĐÚNG thao tác có thật. Vòng 2 bảo "mở form và lưu lại" — làm đúng vậy thì
        // `BanLuuSauForm` trả null và KHÔNG có gì xảy ra; nay chỉ đúng nút "Máy này nhận chủ trì".
        Assert.Contains("Máy này nhận chủ trì", dong[0].Message);
        Assert.DoesNotContain("và lưu lại", dong[0].Message);

        // Đêm SAU thì được nói lại — cùng một tài khoản, khung khác.
        await vm.NhipLichKmAsync(TrongKhung.AddDays(1));
        Assert.Equal(2, services.Log.Snapshot("km-chutri-log@mail.com")
            .Count(e => e.Message.Contains("MÁY CHỦ TRÌ")));
    }

    // ══════════════════════════ Đường LƯU FORM (V5 + V6) ══════════════════════════

    private static KmConfig Dong(
        long id = 1, bool tuDong = false, int soNgay = 7, int phanTram = 5, string gioBatDau = "09:30",
        DateTime? lanChayCuoi = null, DateTime? lanThuTuDong = null,
        DateTime? cfgUpdatedAt = null, string ownerMachine = "")
        => new(id, tuDong, soNgay, phanTram, gioBatDau, "10:30", lanChayCuoi, lanThuTuDong)
        {
            CfgUpdatedAt = cfgUpdatedAt,
            OwnerMachine = ownerMachine,
        };

    private static readonly DateTime LucLuu = new(2026, 8, 22, 15, 0, 0);

    /// <summary>
    /// <b>(V5) Mở form ra XEM rồi bấm Đóng ⇒ KHÔNG lưu gì, KHÔNG đóng dấu, KHÔNG cướp chủ trì.</b>
    /// Đây là bước 5–6 của ca mất cấu hình: máy B mở một hồ sơ rỗng ra nhìn rồi đóng, thế là bản rỗng có mốc
    /// mới nhất và nó thắng cấu hình thật của máy A trên toàn fleet.
    /// </summary>
    [Fact]
    public void LuuForm_KhongDoiGi_ThiKhongLuu_KhongDoiChuTri()
    {
        var tuoi = Dong(tuDong: true, phanTram: 25, gioBatDau: "08:00",
            cfgUpdatedAt: new DateTime(2026, 8, 20, 9, 0, 0), ownerMachine: MayKhac);
        var tuForm = Dong(tuDong: true, phanTram: 25, gioBatDau: "08:00");   // form trả đúng giá trị đã nạp

        // banNap = tuoi: cảnh "không ai đổi gì trong lúc form mở" (ca máy khác đổi giữa chừng nằm ở
        // `LuuForm_MayKhacDoiTrongLucMoForm_KhongDoiGi_ThiKhongLuu`).
        Assert.Null(KmDialogViewModel.BanLuuSauForm(tuoi, banNap: tuoi, tuForm, MayNay, LucLuu));
    }

    /// <summary>Vế ĐỐI CHỨNG — đổi dù MỘT trường thì phải lưu, đóng dấu mốc sửa và nhận chủ trì. Thiếu vế này
    /// thì một bản "không bao giờ lưu" cũng xanh, mà bản đó làm form KM thành đồ trang trí.</summary>
    [Theory]
    [InlineData("tudong")]
    [InlineData("songay")]
    [InlineData("phantram")]
    [InlineData("giobatdau")]
    [InlineData("gioketthuc")]
    public void LuuForm_DoiMotTruong_ThiLuu_VaDongDau(string truong)
    {
        var tuoi = Dong(tuDong: true, soNgay: 7, phanTram: 25, gioBatDau: "08:00", ownerMachine: MayKhac);
        var tuForm = truong switch
        {
            "tudong" => Dong(tuDong: false, soNgay: 7, phanTram: 25, gioBatDau: "08:00"),
            "songay" => Dong(tuDong: true, soNgay: 3, phanTram: 25, gioBatDau: "08:00"),
            "phantram" => Dong(tuDong: true, soNgay: 7, phanTram: 30, gioBatDau: "08:00"),
            "giobatdau" => Dong(tuDong: true, soNgay: 7, phanTram: 25, gioBatDau: "07:00"),
            _ => Dong(tuDong: true, soNgay: 7, phanTram: 25, gioBatDau: "08:00") with { GioKetThuc = "11:30" },
        };

        var luu = KmDialogViewModel.BanLuuSauForm(tuoi, banNap: tuoi, tuForm, MayNay, LucLuu);

        Assert.NotNull(luu);
        Assert.Equal(LucLuu, luu!.CfgUpdatedAt);
        Assert.Equal(MayNay, luu.OwnerMachine);
    }

    /// <summary>
    /// <b>(V6) Đường lưu form KHÔNG được làm LÙI hai mốc chạy.</b> Form là modal mở hàng phút; worker đồng bộ
    /// (nhịp 60s) hoặc một lượt <c>ChotLuotKm</c> có thể đã đẩy mốc lên trong lúc đó. Bản trả về phải mang mốc
    /// của dòng ĐỌC LẠI lúc lưu, không phải ảnh chụp lúc mở form.
    /// </summary>
    [Fact]
    public void LuuForm_KhongLamLuiHaiMocChay()
    {
        var mocTuoi = new DateTime(2026, 8, 22, 2, 40, 0);
        var mocThuTuoi = new DateTime(2026, 8, 22, 2, 5, 0);
        var tuoi = Dong(phanTram: 25, lanChayCuoi: mocTuoi, lanThuTuDong: mocThuTuoi);

        // Ảnh chụp lúc MỞ form: mốc CŨ hơn (worker đã đẩy mốc mới lên trong lúc form đang mở).
        var tuForm = Dong(phanTram: 30,
            lanChayCuoi: new DateTime(2026, 8, 15, 2, 30, 0),
            lanThuTuDong: new DateTime(2026, 8, 15, 2, 0, 0));

        // banNap mang đúng cấu hình người dùng NHÌN THẤY lúc mở (25%) — chỉ hai mốc chạy là cũ.
        var banNap = Dong(phanTram: 25,
            lanChayCuoi: new DateTime(2026, 8, 15, 2, 30, 0),
            lanThuTuDong: new DateTime(2026, 8, 15, 2, 0, 0));

        var luu = KmDialogViewModel.BanLuuSauForm(tuoi, banNap, tuForm, MayNay, LucLuu);

        Assert.NotNull(luu);
        Assert.Equal(30, luu!.PhanTram);            // phần người dùng sửa VẪN vào
        Assert.Equal(mocTuoi, luu.LanChayCuoi);     // mốc chạy lấy của dòng TƯƠI
        Assert.Equal(mocThuTuoi, luu.LanThuTuDong);
    }

    /// <summary>Không có hub (<c>mayNay</c> rỗng) ⇒ chủ trì để RỖNG, không ghi bừa một cái tên. Rỗng nghĩa là
    /// "chưa ai chủ trì" và mọi bên đọc phải rơi về hành vi hiện tại.</summary>
    [Fact]
    public void LuuForm_KhongCoHub_ThiChuTriDeRong()
    {
        var luu = KmDialogViewModel.BanLuuSauForm(
            Dong(phanTram: 25), banNap: Dong(phanTram: 25), Dong(phanTram: 30), "", LucLuu);

        Assert.NotNull(luu);
        Assert.Equal("", luu!.OwnerMachine);
        Assert.Equal(LucLuu, luu.CfgUpdatedAt);
    }

    // ══════════════════════════ Luật dùng chung: CungCauHinh ══════════════════════

    /// <summary>Hai mốc CHẠY khác nhau KHÔNG tính là "đổi cấu hình" — tính vào thì mỗi lượt KM chạy xong là một
    /// lần đóng dấu "người dùng vừa sửa", và máy chạy KM tự nhiên cướp chủ trì.</summary>
    [Fact]
    public void CungCauHinh_BoQuaMocChay_VaBoQuaMocSua()
    {
        var a = Dong(phanTram: 25, lanChayCuoi: new DateTime(2026, 8, 1), cfgUpdatedAt: new DateTime(2026, 8, 1));
        var b = Dong(phanTram: 25, lanChayCuoi: new DateTime(2026, 8, 20), lanThuTuDong: new DateTime(2026, 8, 20),
            cfgUpdatedAt: new DateTime(2026, 8, 20));

        Assert.True(KmConfig.CungCauHinh(a, b));
    }

    /// <summary>Đổi MÁY CHỦ TRÌ thì tính là đổi cấu hình — đó là thông tin đáng ghi vào nhật ký khi hồ sơ gộp
    /// về làm đổi ai là người chạy lượt tự động.</summary>
    [Fact]
    public void CungCauHinh_DoiChuTri_ThiKhacNhau()
        => Assert.False(KmConfig.CungCauHinh(Dong(ownerMachine: MayNay), Dong(ownerMachine: MayKhac)));

    [Fact]
    public void CungCauHinh_DoiPhanTram_ThiKhacNhau()
        => Assert.False(KmConfig.CungCauHinh(Dong(phanTram: 5), Dong(phanTram: 25)));
}
