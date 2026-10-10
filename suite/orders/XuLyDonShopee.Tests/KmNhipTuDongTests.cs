using System;
using System.Linq;
using System.Threading.Tasks;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>Nhịp LỊCH KM TỰ ĐỘNG (<c>AccountsViewModel.NhipLichKmAsync</c>) — chốt "không đốt lượt cho một cấu hình
/// chắc chắn hỏng".</b>
/// <para>Ca thật mà lượt phản biện vòng 2 (19/08/2026) tìm ra: <c>GioBatDau = "01:00"</c> hoàn toàn hợp lệ với
/// form, nhưng lượt tự động 02:05 gửi xuống một mốc ĐÃ QUÁ KHỨ ⇒ picker của Shopee chặn ⇒ 12 shop đỏ. Và vì
/// <c>LanThuTuDong</c> đã được ghi, đêm nào cũng lặp lại y hệt — vĩnh viễn, không một dòng log nào nói vì sao.
/// Hỏng kiểu này không ai phát hiện nổi bằng mắt: nhật ký chỉ toàn "shop X: HỎNG".</para>
/// <para><b>26/08/2026 — chương trình nay chạy một KHOẢNG</b>: bắt đầu HÔM NAY lúc <c>GioBatDau</c>, kết thúc
/// hôm nay + <see cref="KmLichTuDong.SoNgayDatTruoc"/> ngày, CÙNG giờ hai đầu. Hệ quả: đúng ca <c>01:00</c> ở
/// trên <b>SỐNG LẠI</b> — bản 25/08 đẩy cả chương trình đi 180 ngày nên nó tự tan một thời gian, nay mốc bắt đầu
/// về lại hôm nay. Ba việc của lớp này từ nay: (a) cửa kiểm cấu hình chặn được cấu hình rác, (b) ca <c>01:00</c>
/// PHẢI bị chặn ở cửa đó và KHÔNG phóng trình duyệt, (c) hai mốc gửi xuống đúng bằng hôm nay và hôm nay + 180.</para>
/// <para><b>Vì sao test được mà không phóng trình duyệt thật:</b> mọi lượt chạy đều đi qua
/// <c>MuonHoSoAsync</c> → <c>AcquireAccountLease</c>. Cho hook đó trả "máy khác đang giữ" là lượt dừng ngay ở
/// cửa, trước khi có bất kỳ tiến trình Brave nào — mà dòng nhật ký "bắt đầu lượt KM" thì đã kịp ghi, nên vẫn
/// phân biệt được "có phóng" với "không phóng".</para>
/// </summary>
public class KmNhipTuDongTests
{
    /// <summary>Dựng VM + một tài khoản, và CHẶN mọi lượt chạy thật ở cửa khoá xuyên máy.</summary>
    private static (TempDatabase Temp, AppServices Services, AccountsViewModel Vm, long AccountId, string Email)
        DungVm(string email)
    {
        var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = email, Password = "p" });
        var id = services.Accounts.GetAll().First(a => a.Email == email).Id;

        // "Máy khác đang giữ" ⇒ MuonHoSoAsync trả false ⇒ KHÔNG có tiến trình Brave nào được phóng trong test.
        services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(false, "MAY-KHAC"));
        services.ReleaseAccountLease = login => Task.CompletedTask;

        return (temp, services, new AccountsViewModel(services), id, email);
    }

    private static KmConfig CauHinh(long id, string gioBatDau, string gioKetThuc = "10:30", int phanTram = 5)
        => new(id, TuDong: true, SoNgay: 1, PhanTram: phanTram, GioBatDau: gioBatDau, GioKetThuc: gioKetThuc,
               LanChayCuoi: null, LanThuTuDong: null);

    /// <summary>
    /// <b>Cấu hình HỎNG ⇒ KHÔNG phóng, có log nói lý do, <c>LanThuTuDong</c> VẪN được ghi.</b>
    /// <para>Ghi mốc thử là phần quan trọng ngang phần không phóng: không ghi thì nhịp đồng hồ 60 giây lặp lại
    /// chính dòng log này MỖI PHÚT cho tới 6h sáng — 235 dòng rác một đêm.</para>
    /// <para><b>Ca dựng cảnh đã đổi BA LẦN, ghi lại để đừng đổi ngược:</b> đầu tiên là
    /// <c>GioBatDau = "01:00"</c> (mốc quá khứ so với nhịp 02:05) — bản 25/08/2026 đẩy chương trình đi 180 ngày
    /// làm cấu hình ấy HỢP LỆ; rồi tới "giờ kết thúc ≤ giờ bắt đầu" — bản 26/08/2026 BỎ hẳn luật đó (hai đầu
    /// khoảng cùng một giờ); rồi tới ĐỊNH DẠNG GIỜ — nhưng
    /// <see cref="CauHinhGioSaiDinhDangThiCungBiChan"/> đã đứng sẵn ở đúng nhánh ấy, nên hai test đo CÙNG một
    /// dòng code trong khi số test vẫn là hai (phản biện 26/08/2026, NHẸ-7). Nay dựng bằng vế <b>% GIẢM</b> —
    /// một nhánh khác hẳn trong <see cref="KmDialogViewModel.KiemTraForm"/>.</para>
    /// </summary>
    [StaFact]
    public async Task CauHinhHong_ThiKHONG_phong_NhungVAN_ghi_LanThuTuDong()
    {
        var (temp, services, vm, id, email) = DungVm("km-nhip-cauhinhhong@mail.com");
        using var db = temp;
        services.KmConfig.Save(CauHinh(id, gioBatDau: "09:30", phanTram: 0));

        var bayGio = new DateTime(2026, 8, 19, 2, 5, 0);
        await vm.NhipLichKmAsync(bayGio);

        var log = services.Log.Snapshot(email);
        Assert.Contains(log, e => e.Message.Contains("CẤU HÌNH KHÔNG DÙNG ĐƯỢC"));
        Assert.Contains(log, e => e.Message.Contains("% giảm phải là số nguyên từ 1 đến 99"));
        Assert.DoesNotContain(log, e => e.Message.Contains("bắt đầu lượt KM"));

        Assert.Equal(bayGio, services.KmConfig.Get(id).LanThuTuDong);
    }

    /// <summary>
    /// <b>KHOẢNG chương trình của lượt tự động = ngày chạy → ngày chạy + 180</b> (người dùng đính chính
    /// 26/08/2026), CÙNG một giờ ở hai đầu — và nhật ký phải NÓI RA cả hai mốc.
    /// <para><b>Vì sao đo qua nhật ký chứ không qua tham số gửi xuống extension:</b> lượt chạy trong test bị
    /// chặn ở cửa khoá xuyên máy (xem <see cref="DungVm"/>) nên không có gói lệnh nào để soi. Dòng log là thứ
    /// DUY NHẤT chỗ gọi để lại — và nó cũng đúng là thứ người dùng đọc lúc 8h sáng để biết đêm qua app đã đặt
    /// chương trình cho ngày nào.</para>
    /// <para><b>Đây là test khoá CHỖ GỌI, không phải khoá hàm thuần.</b> Test cho
    /// <see cref="KmLichTuDong.NgayKetThuc"/> lẻ vẫn xanh kể cả khi <c>NhipLichKmAsync</c> quên gọi nó —
    /// đúng lỗ phủ đã bị chỉ mặt ở đợt trước.</para>
    /// </summary>
    [StaFact]
    public async Task KhoangChuongTrinh_CuaLuotTuDong_LaHomNay_ToiHomNayCong180()
    {
        var (temp, services, vm, id, email) = DungVm("km-nhip-180@mail.com");
        using var db = temp;
        services.KmConfig.Save(CauHinh(id, gioBatDau: "09:30"));

        var bayGio = new DateTime(2026, 8, 19, 2, 5, 0);
        await vm.NhipLichKmAsync(bayGio);

        // Khoảng: 19-08-2026 → 15-02-2027 (đúng 180 ngày), CÙNG giờ 09:30 ở hai đầu.
        Assert.Equal("19-08-2026", KmLichTuDong.NgayBatDau(bayGio).ToString("dd-MM-yyyy"));
        Assert.Equal("15-02-2027", KmLichTuDong.NgayKetThuc(bayGio).ToString("dd-MM-yyyy"));

        var log = services.Log.Snapshot(email);
        Assert.Contains(log, e => e.Message.Contains("Chương trình chạy từ 19-08-2026 09:30 tới 15-02-2027 09:30"));
        Assert.Contains(log, e => e.Message.Contains("khoảng 180 ngày, cùng giờ hai đầu"));

        // Và TUYỆT ĐỐI không còn hình dạng CŨ: một lát cắt một tiếng ở ngày +180.
        Assert.DoesNotContain(log, e => e.Message.Contains("15-02-2027 09:30-10:30"));

        // Tên tự sinh phải bám ĐÚNG ngày BẮT ĐẦU — tên nói một ngày mà picker đặt một ngày khác là không đối
        // chiếu được về sau (tên là thứ duy nhất tìm lại được chương trình trên Shopee).
        Assert.Equal(
            "KM ngày 19-08-2026 từ 09:30",
            KmDialogViewModel.SinhTenMacDinh(KmLichTuDong.NgayBatDau(bayGio), "09:30"));
    }

    /// <summary>
    /// <b>Ca <c>GioBatDau = "01:00"</c> ở nhịp 02:05 LẠI bị chặn — và đó là hành vi ĐÚNG.</b>
    /// <para>Ca thật của phản biện vòng 2 (19/08/2026): mốc <c>01:00</c> cùng ngày đã trôi qua lúc lượt tự động
    /// chạy ⇒ picker chặn ⇒ 12 shop đỏ mỗi đêm. Bản 25/08 đẩy cả chương trình đi 180 ngày nên ca ấy tự tan;
    /// bản 26/08 đưa mốc BẮT ĐẦU về HÔM NAY nên nó SỐNG LẠI, lần này bị bắt ngay trên cửa cấu hình và KHÔNG
    /// phóng trình duyệt.</para>
    /// <para>Test này là chỗ ghi lại điều đó — nó đỏ nếu ai bỏ cửa <c>KiemTraMocTuongLai</c> ra khỏi nhịp tự
    /// động, mà bỏ nó nghĩa là quay về đúng 12 shop đỏ mỗi đêm.</para>
    /// </summary>
    [StaFact]
    public async Task GioBatDauSomHonNhip_LAI_LaMocQuaKhu_VaBiChanTruocKhiPhongTrinhDuyet()
    {
        var (temp, services, vm, id, email) = DungVm("km-nhip-quakhu@mail.com");
        using var db = temp;
        services.KmConfig.Save(CauHinh(id, gioBatDau: "01:00"));

        var bayGio = new DateTime(2026, 8, 19, 2, 5, 0);
        await vm.NhipLichKmAsync(bayGio);

        var log = services.Log.Snapshot(email);
        Assert.Contains(log, e => e.Message.Contains("CẤU HÌNH KHÔNG DÙNG ĐƯỢC"));
        Assert.Contains(log, e => e.Message.Contains("ĐÃ TRÔI QUA"));
        Assert.DoesNotContain(log, e => e.Message.Contains("bắt đầu lượt KM"));

        // VẪN ghi mốc thử — không ghi thì nhịp 60 giây spam lại chính dòng đó tới hết khung.
        Assert.Equal(bayGio, services.KmConfig.Get(id).LanThuTuDong);
    }

    /// <summary>
    /// <b>Vế ĐỐI CHỨNG — cấu hình HỢP LỆ ở đúng nhịp đó thì lượt VẪN phải phóng.</b>
    /// Thiếu vế này thì một bản "chặn mọi lượt tự động" cũng xanh, mà đó là cách làm chết tính năng.
    /// </summary>
    [StaFact]
    public async Task CauHinhHopLe_ThiVAN_bat_dau_luot()
    {
        var (temp, services, vm, id, email) = DungVm("km-nhip-hople@mail.com");
        using var db = temp;
        services.KmConfig.Save(CauHinh(id, gioBatDau: "09:30"));

        var bayGio = new DateTime(2026, 8, 19, 2, 5, 0);
        await vm.NhipLichKmAsync(bayGio);

        var log = services.Log.Snapshot(email);
        Assert.Contains(log, e => e.Message.Contains("bắt đầu lượt KM"));
        Assert.DoesNotContain(log, e => e.Message.Contains("CẤU HÌNH KHÔNG DÙNG ĐƯỢC"));

        // Dòng đó phải nêu HẠN CHÓT — người đọc log cần biết vòng sẽ tự cắt ở đâu. Từ 25/08/2026 khung
        // Chương Trình Của Shop thu còn 02–04h nên hạn chót là 04:00, không phải 06:00.
        Assert.Contains(log, e => e.Message.Contains("không bắt đầu shop mới sau 04:00"));
        Assert.Equal(bayGio, services.KmConfig.Get(id).LanThuTuDong);
    }

    /// <summary>
    /// <b>CA B của phản biện 26/08/2026 — <c>GioBatDau = "02:05"</c>: hợp lệ với MỌI cửa kiểm cũ, và im lặng
    /// giết lượt tự động VĨNH VIỄN.</b>
    /// <para>Khung KM chạy 02–04h. Mốc bắt đầu <c>02:05</c> còn ở tương lai khi nhịp nổ lúc <c>02:01</c>, nên
    /// cửa "mốc phải ở tương lai" cho qua. Nhưng tới lúc mở được shop đầu (SSO + trang chọn shop) thì 02:05 đã
    /// trôi qua ⇒ <b>mọi shop đỏ</b> ⇒ <c>SoTao = 0</c> ⇒ <c>ChotLuotKm</c> CỐ Ý không ghi
    /// <c>LanChayCuoi</c> ⇒ <c>DenGioChay</c> lại trả true đêm sau ⇒ <b>đêm nào cũng lặp y hệt, mãi mãi</b> —
    /// đúng profile lưu lượng làm Shopee bật <c>verify/traffic</c>.</para>
    /// <para>Cửa chặn là BIÊN AN theo số shop: 12 shop ⇒ 96 phút, mà 02:05 chỉ cách 02:01 đúng 4 phút.</para>
    /// </summary>
    [StaFact]
    public async Task CaB_GioBatDauSatNgayGioChay_ThiBiChanTaiCuaCauHinh_KemCachGo()
    {
        var (temp, services, vm, id, email) = DungVm("km-nhip-cab@mail.com");
        using var db = temp;
        services.KmConfig.Save(CauHinh(id, gioBatDau: "02:05"));

        // 12 shop — đúng số shop thật của một tài khoản, và là thứ quyết định biên an.
        services.Results.UpsertShops(
            id, Enumerable.Range(1, 12).Select(i => new ShopListItem($"{i}00", $"Shop {i}", $"shop.{i}")));

        var bayGio = new DateTime(2026, 8, 19, 2, 1, 0);
        await vm.NhipLichKmAsync(bayGio);

        var log = services.Log.Snapshot(email);
        Assert.Contains(log, e => e.Message.Contains("CẤU HÌNH KHÔNG DÙNG ĐƯỢC"));
        Assert.Contains(log, e => e.Message.Contains("QUÁ SÁT"));
        Assert.Contains(log, e => e.Message.Contains("một vòng 12 shop cần khoảng 96 phút"));
        // CÁCH GỠ phải có mặt, và phải là một giờ CỤ THỂ (02:01 + 96' = 03:37 ⇒ làm tròn lên bước 5' ⇒ 03:40).
        Assert.Contains(log, e => e.Message.Contains("từ 03:40 trở đi"));
        Assert.DoesNotContain(log, e => e.Message.Contains("bắt đầu lượt KM"));

        Assert.Equal(bayGio, services.KmConfig.Get(id).LanThuTuDong);
    }

    /// <summary>
    /// Cấu hình rác kiểu khác (giờ SAI ĐỊNH DẠNG) cũng phải bị chặn ở cùng cửa — luật là luật THUẦN dùng lại
    /// của form (<see cref="KmDialogViewModel.KiemTraForm"/>), không phải một bộ luật thứ hai.
    /// <para><b>Cảnh dựng ĐỔI 26/08/2026</b>: vế "giờ kết thúc ≤ giờ bắt đầu" đã bỏ cùng ô nhập, nên ca hỏng
    /// mượn sang vế định dạng — vẫn còn sống và vẫn đúng ý định đo của test này.</para>
    /// </summary>
    [StaFact]
    public async Task CauHinhGioSaiDinhDangThiCungBiChan()
    {
        var (temp, services, vm, id, email) = DungVm("km-nhip-gio@mail.com");
        using var db = temp;
        services.KmConfig.Save(CauHinh(id, gioBatDau: "9h30"));

        await vm.NhipLichKmAsync(new DateTime(2026, 8, 19, 2, 5, 0));

        var log = services.Log.Snapshot(email);
        Assert.Contains(log, e => e.Message.Contains("CẤU HÌNH KHÔNG DÙNG ĐƯỢC"));
        Assert.DoesNotContain(log, e => e.Message.Contains("bắt đầu lượt KM"));
    }
}
