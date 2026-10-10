using System;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>LUẬT NHƯỜNG KHUNG của vòng check đơn</b> (<see cref="KmLichTuDong.NenNhuongKhung"/>) — hai hạng mục CHẶN
/// PHÁT HÀNH của phản biện vòng 3, 22/08/2026.
/// <para><b>W1 — pha A đứng một mình là BƯỚC LÙI.</b> Từ khi hồ sơ KM đồng bộ được, <c>TuDong = true</c> lan
/// sang MỌI máy giữ tài khoản đó, nên máy KHÔNG chủ trì cũng bắt đầu dời mọi vòng rơi vào <c>[01:00, 06:00)</c>
/// ra sau 06:00 — <b>mà vẫn ôm khoá <c>orders:&lt;login&gt;</c></b> (nhả khoá là việc của pha B, chưa làm). Kết
/// cục: máy chủ trì VẪN không xin được khoá nên KM vẫn không chạy, còn máy kia mất thêm 5 tiếng check đơn mỗi
/// đêm. Trước pha A thì nó chạy suốt đêm. Đây là hồi quy thuần.</para>
/// <para><b>W2 — nhường khung nổ MỖI ĐÊM, kể cả chu kỳ 7 ngày.</b> <c>HoanVongCheckDon</c> chỉ nhận một
/// <c>bool</c> "có bật tự động", không hỏi <c>MocChayKe</c>. Với <c>SoNgay = 7</c> đó là đổi <b>5 giờ × 7
/// đêm</b> check đơn lấy đúng MỘT lượt KM.</para>
/// </summary>
public class KmNhuongKhungTests
{
    private const string MayNay = "may-nay:orders";
    private const string MayKhac = "may-khac:orders";

    /// <summary>Mốc vòng kế rơi vào BIÊN AN <c>[01:00, 06:00)</c> — tức lượt nhường khung có tác dụng thật.</summary>
    private static readonly DateTime MocTrongBienAn = new(2026, 8, 22, 1, 30, 0);

    /// <summary>"Bây giờ" ngay trước mốc vòng kế (00:50 cùng đêm).</summary>
    private static readonly DateTime BayGio = new(2026, 8, 22, 0, 50, 0);

    private static KmConfig Lich(
        bool tuDong = true, int soNgay = 7, DateTime? lanChayCuoi = null, string ownerMachine = MayNay)
        => new(1, tuDong, soNgay, PhanTram: 25, GioBatDau: "09:30", GioKetThuc: "10:30", lanChayCuoi)
        {
            OwnerMachine = ownerMachine,
        };

    // ══════════════════════════ W1 — cửa CHỦ TRÌ (chặn tạm của pha A) ══════════════════════════

    /// <summary>
    /// <b>Máy KHÔNG chủ trì thì KHÔNG nhường khung</b> — và mốc vòng kế phải giữ NGUYÊN, không bị kéo tới 06:00.
    /// Vế thứ hai mới là vế người dùng cảm nhận được: nhường mà không nhả khoá là mất trắng 5 tiếng check đơn.
    /// </summary>
    [Fact]
    public void MayKhongChuTri_ThiKhongNhuong_VaMocGiuNguyen()
    {
        var cf = Lich(lanChayCuoi: null, ownerMachine: MayKhac);   // null = tới hạn ngay khung gần nhất

        var nhuong = KmLichTuDong.NenNhuongKhung(cf, MayNay, MocTrongBienAn, BayGio);

        Assert.False(nhuong);
        Assert.Equal(MocTrongBienAn, KmLichTuDong.HoanVongCheckDon(MocTrongBienAn, nhuong));
    }

    /// <summary>Vế ĐỐI CHỨNG — máy CHỦ TRÌ vẫn nhường y như trước pha A. Thiếu vế này thì một bản "không bao
    /// giờ nhường" cũng xanh, mà bản đó để check đơn giẫm thẳng vào lượt KM.
    /// <para><b>Mốc mới là 04:00</b>, không phải 06:00: từ 25/08/2026 khung Chương Trình Của Shop thu còn
    /// 02–04h, và <c>HoanVongCheckDon</c> dời tới giờ ĐÓNG của ĐÚNG khung mốc đó đâm vào.</para></summary>
    [Fact]
    public void MayChuTri_ThiVanNhuong_NhuTruocPhaA()
    {
        var cf = Lich(lanChayCuoi: null, ownerMachine: MayNay);

        var nhuong = KmLichTuDong.NenNhuongKhung(cf, MayNay, MocTrongBienAn, BayGio);

        Assert.True(nhuong);
        Assert.Equal(new DateTime(2026, 8, 22, 4, 0, 0),
            KmLichTuDong.HoanVongCheckDon(MocTrongBienAn, nhuong));
    }

    /// <summary>ĐỘ LÙI — chủ trì RỖNG = KHÔNG AI chủ trì (dữ liệu cũ chưa backfill, hoặc máy một mình không hub
    /// nên chẳng ai đóng dấu) ⇒ nhường y hành vi trước pha A. Mất mạng không được đổi hành vi.</summary>
    [Theory]
    [InlineData("", MayNay)]
    [InlineData("", "")]
    public void ChuTriRong_ThiVanNhuong(string ownerMachine, string mayNay)
        => Assert.True(KmLichTuDong.NenNhuongKhung(
            Lich(lanChayCuoi: null, ownerMachine: ownerMachine), mayNay, MocTrongBienAn, BayGio));

    /// <summary>
    /// ĐÃ CÓ chủ trì mà máy này KHÔNG BIẾT MÌNH LÀ AI (chưa cấu hình hub, vừa bấm NGẮT, hoặc đang trong cửa sổ
    /// <c>Reconnect()</c>) ⇒ <b>KHÔNG nhường</b>.
    /// <para><b>Khẳng định này vừa bị LẬT (22/08/2026)</b> — bản cũ assert <c>True</c> với lý do "mất mạng không
    /// được đổi hành vi". Nhưng nhường khung ở pha A <b>không nhả khoá</b> (đó là việc của B1), nên một máy
    /// không phải chủ trì mà nhường thì vừa mất 5 tiếng check đơn mỗi đêm vừa KHÔNG cho ai chạy KM được — đúng
    /// hồi quy W1. Không biết mình là ai ⇒ cứ chạy check đơn như trước pha A, đó mới là "giữ nguyên hành vi".</para>
    /// </summary>
    [Theory]
    [InlineData(MayKhac, "")]
    [InlineData(MayKhac, null)]
    public void DaCoChuTri_MaKhongBietMinhLaAi_ThiKhongNhuong(string ownerMachine, string? mayNay)
        => Assert.False(KmLichTuDong.NenNhuongKhung(
            Lich(lanChayCuoi: null, ownerMachine: ownerMachine), mayNay, MocTrongBienAn, BayGio));

    /// <summary>Tắt lịch KM ⇒ không nhường, y luật cũ (chặn khung cho tài khoản chẳng bao giờ chạy KM là tự cắt
    /// 5 tiếng check đơn mỗi đêm không vì cái gì).</summary>
    [Fact]
    public void TatKmTuDong_ThiKhongNhuong()
        => Assert.False(KmLichTuDong.NenNhuongKhung(
            Lich(tuDong: false, lanChayCuoi: null), MayNay, MocTrongBienAn, BayGio));

    // ══════════════════════════ W2 — chỉ nhường ĐÊM THỰC SỰ TỚI HẠN ══════════════════════════

    /// <summary>
    /// <b>Chu kỳ 7 ngày, vừa chạy hôm qua ⇒ KHÔNG nhường đêm nay.</b> Đây là ca thường trực: 6 trong 7 đêm
    /// không có lượt KM nào, mà bản trước vẫn cắt 5 tiếng check đơn của cả 6 đêm đó.
    /// </summary>
    [Fact]
    public void ChuKy7Ngay_ChuaToiHan_ThiKhongNhuong()
    {
        var cf = Lich(soNgay: 7, lanChayCuoi: new DateTime(2026, 8, 21, 2, 30, 0));

        var nhuong = KmLichTuDong.NenNhuongKhung(cf, MayNay, MocTrongBienAn, BayGio);

        Assert.False(nhuong);
        Assert.Equal(MocTrongBienAn, KmLichTuDong.HoanVongCheckDon(MocTrongBienAn, nhuong));
    }

    /// <summary>Đúng đêm tới hạn (lần cuối cách đây 7 ngày ⇒ mốc kế là 02:00 đêm nay) ⇒ nhường.</summary>
    [Fact]
    public void ChuKy7Ngay_DungDemToiHan_ThiNhuong()
    {
        var cf = Lich(soNgay: 7, lanChayCuoi: new DateTime(2026, 8, 15, 2, 30, 0));

        Assert.Equal(new DateTime(2026, 8, 22, 1, 0, 0), KmLichTuDong.MocChayKe(cf, BayGio, LoaiChuongTrinh.ChuongTrinhShop));
        Assert.True(KmLichTuDong.NenNhuongKhung(cf, MayNay, MocTrongBienAn, BayGio));
    }

    /// <summary>SÁT HẠN — mốc kế rơi vào đêm MAI (23/08) thì đêm NAY không nhường. Đây là ca biên dễ sai nhất:
    /// lệch một ngày là lại cắt oan một đêm check đơn, hoặc tệ hơn là không nhường đúng đêm cần.</summary>
    [Fact]
    public void ToiHanDemMai_ThiDemNayKhongNhuong()
    {
        var cf = Lich(soNgay: 7, lanChayCuoi: new DateTime(2026, 8, 16, 2, 30, 0));

        Assert.Equal(new DateTime(2026, 8, 23, 1, 0, 0), KmLichTuDong.MocChayKe(cf, BayGio, LoaiChuongTrinh.ChuongTrinhShop));
        Assert.False(KmLichTuDong.NenNhuongKhung(cf, MayNay, MocTrongBienAn, BayGio));

        // …và đúng đêm mai thì nhường: vòng kế rơi vào 01:30 ngày 23/08.
        Assert.True(KmLichTuDong.NenNhuongKhung(
            cf, MayNay, new DateTime(2026, 8, 23, 1, 30, 0), new DateTime(2026, 8, 23, 0, 50, 0)));
    }

    /// <summary>Chưa chạy lần nào (<c>LanChayCuoi = null</c>) ⇒ khung gần nhất là đêm nay ⇒ nhường. Vế này giữ
    /// đúng ngữ nghĩa của <see cref="KmLichTuDong.MocChayKe"/>, đừng lẫn với "chưa tới hạn".</summary>
    [Fact]
    public void ChuaChayLanNao_ThiNhuongNgayDemGanNhat()
        => Assert.True(KmLichTuDong.NenNhuongKhung(Lich(lanChayCuoi: null), MayNay, MocTrongBienAn, BayGio));

    /// <summary>Mốc vòng kế NGOÀI biên an (13:00 trưa) thì dù có nhường hay không cũng không đụng gì — luật cũ,
    /// giữ nguyên. Ở đây chỉ khoá lại rằng W2 không vô tình dời một mốc ban ngày.</summary>
    [Fact]
    public void MocNgoaiBienAn_ThiKhongDoiDuToiHan()
    {
        var trua = new DateTime(2026, 8, 22, 13, 0, 0);
        var cf = Lich(lanChayCuoi: null);

        Assert.Equal(trua, KmLichTuDong.HoanVongCheckDon(
            trua, KmLichTuDong.NenNhuongKhung(cf, MayNay, trua, new DateTime(2026, 8, 22, 12, 30, 0))));
    }
}
