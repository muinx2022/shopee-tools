using Shopee.Core.Coordination;
using Shopee.Hub;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// <b>Bắt tay "nhường khung" xuyên máy</b> — thứ trả lời đúng câu hỏi mở đầu cả đợt: máy đặt lịch KM có dừng
/// được check đơn ở máy khác để chạy không, và xong có bật lại không.
/// <para><b>Ba bất biến phải khoá bằng test, vì hỏng cái nào cũng im lặng:</b> (1) khoá trống ⇒ KHÔNG được đẻ
/// lệnh nào — đẻ thừa là mỗi nhịp chờ lại dừng oan một máy; (2) trả khung phải bật lại ĐÚNG tập máy đã dừng;
/// (3) máy chủ trì chết giữa chừng ⇒ hub vẫn phải tự trả khung, không thì máy kia nằm im tới khi người dùng
/// tự bấm Chạy — đúng cái hỏng việc này sinh ra để sửa.</para>
/// </summary>
public sealed class KmKhungTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "hub-kmkhung-" + Guid.NewGuid().ToString("N"));

    private const string MayA = "may-a:orders";     // máy chủ trì, sắp chạy KM
    private const string MayB = "may-b:orders";     // máy đang chạy check đơn
    private const string LoginX = "sub-x@shopee.vn";
    private const string KhoaX = "orders:sub-x@shopee.vn";

    /// <summary>Cho <paramref name="may"/> giữ khoá tài khoản của <see cref="LoginX"/> — đúng đường phiên check
    /// đơn thật đi (<c>AcquireAccountLease</c> → <c>ReserveAccounts</c>).</summary>
    private static void ChoGiuKhoa(HubDatabase db, string may, string hostname)
        => db.ReserveAccounts(new AccountReserveRequest([KhoaX], may, hostname));

    private static List<OrdersCommandRow> LenhCua(HubDatabase db, string may)
        => db.OrdersCommandsOf(may);

    [Fact]
    public void KhoaTrong_ThiChoDiTiep_VaKhongDeLenhNao()
    {
        using var db = new HubDatabase(_dataDir);

        var kq = db.XinKhung(LoginX, MayA);

        Assert.True(kq.Trong);
        Assert.Null(kq.Loi);
        Assert.Empty(LenhCua(db, MayB));
        Assert.Empty(db.MayDangBiDungViKm(LoginX));
    }

    /// <summary>Chính mình đang giữ khoá (lượt trước chưa nhả, hoặc máy chủ trì cũng là máy chạy check đơn)
    /// ⇒ đi tiếp, và TUYỆT ĐỐI không tự ra lệnh dừng chính mình.</summary>
    [Fact]
    public void ChinhMinhDangGiuKhoa_ThiDiTiep_KhongTuDungChinhMinh()
    {
        using var db = new HubDatabase(_dataDir);
        ChoGiuKhoa(db, MayA, "MAY-A");

        var kq = db.XinKhung(LoginX, MayA);

        Assert.True(kq.Trong);
        Assert.Empty(LenhCua(db, MayA));
    }

    [Fact]
    public void MayKhacGiuKhoa_ThiRaLenhDung_VaGhiSo_VaNeuTenMay()
    {
        using var db = new HubDatabase(_dataDir);
        ChoGiuKhoa(db, MayB, "NUC");

        var kq = db.XinKhung(LoginX, MayA);

        Assert.False(kq.Trong);
        Assert.Equal("NUC", kq.DangDung);                     // tên máy, không phải id thô
        Assert.Equal([MayB], db.MayDangBiDungViKm(LoginX));

        var lenh = Assert.Single(LenhCua(db, MayB));
        Assert.Equal(OrdersCommandActions.Stop, lenh.Action);
        Assert.Equal(LoginX, lenh.Login);
        Assert.Empty(LenhCua(db, MayA));                      // không đụng máy đang xin
    }

    /// <summary>
    /// Client gọi lại mỗi vài giây trong lúc chờ khoá nhả ⇒ <b>KHÔNG được đẻ lệnh thứ hai</b>, và cũng không
    /// đẻ dòng sổ thứ hai. Đẻ thừa thì mỗi nhịp chờ lại là một lượt dừng oan.
    /// </summary>
    [Fact]
    public void XinLaiNhieuLan_ThiKhongDeLenhThuHai()
    {
        using var db = new HubDatabase(_dataDir);
        ChoGiuKhoa(db, MayB, "NUC");

        db.XinKhung(LoginX, MayA);
        db.XinKhung(LoginX, MayA);
        db.XinKhung(LoginX, MayA);

        Assert.Single(LenhCua(db, MayB));
        Assert.Single(db.MayDangBiDungViKm(LoginX));
    }

    /// <summary>Máy kia nhả khoá xong ⇒ lượt xin kế trả <c>true</c> mà KHÔNG tạo thêm lệnh; sổ vẫn còn nguyên
    /// để lượt <c>TraKhung</c> biết phải bật lại ai.</summary>
    [Fact]
    public void SauKhiMayKiaNhaKhoa_ThiXinDuoc_VaSoVanConDeConTraLai()
    {
        using var db = new HubDatabase(_dataDir);
        ChoGiuKhoa(db, MayB, "NUC");
        db.XinKhung(LoginX, MayA);

        db.ReleaseAccounts(new AccountReleaseRequest([KhoaX], MayB));   // phiên check đơn ở B dừng hẳn

        var kq = db.XinKhung(LoginX, MayA);

        Assert.True(kq.Trong);
        Assert.Single(LenhCua(db, MayB));                       // vẫn đúng MỘT lệnh stop, không đẻ thêm
        Assert.Equal([MayB], db.MayDangBiDungViKm(LoginX));     // sổ còn ⇒ còn đường trả khung
    }

    [Fact]
    public void TraKhung_ThiRaLenhChayLaiDungTapDaDung_RoiXoaSo()
    {
        using var db = new HubDatabase(_dataDir);
        ChoGiuKhoa(db, MayB, "NUC");
        db.XinKhung(LoginX, MayA);

        var so = db.TraKhung(LoginX);

        Assert.Equal(1, so);
        Assert.Empty(db.MayDangBiDungViKm(LoginX));
        Assert.Contains(LenhCua(db, MayB), c => c.Action == OrdersCommandActions.Run && c.Login == LoginX);
        Assert.DoesNotContain(LenhCua(db, MayA), c => c.Action == OrdersCommandActions.Run);
    }

    /// <summary>Không mượn ai ⇒ trả khung là no-op. Client gọi ở MỌI đường ra (xong/lỗi/huỷ) nên nhánh này chạy
    /// thường xuyên hơn nhánh có mượn.</summary>
    [Fact]
    public void TraKhung_KhiKhongMuonAi_ThiKhongLamGi()
    {
        using var db = new HubDatabase(_dataDir);
        Assert.Equal(0, db.TraKhung(LoginX));
        Assert.Empty(LenhCua(db, MayB));
    }

    /// <summary>
    /// <b>LƯỚI CUỐI</b> — máy chủ trì tắt/mất điện giữa lượt KM (không ai gọi <c>TraKhung</c>) ⇒ hub tự bật lại
    /// máy bị dừng khi quá hạn. Thiếu nhánh này thì máy kia nằm im tới khi người dùng tự bấm Chạy.
    /// </summary>
    [Fact]
    public void MayChuTriChetGiuaChung_ThiQuetQuaHan_TuTraKhung()
    {
        using var db = new HubDatabase(_dataDir) { HanMuonKhungMacDinh = TimeSpan.FromMinutes(90) };
        ChoGiuKhoa(db, MayB, "NUC");
        db.XinKhung(LoginX, MayA);
        // …rồi máy A tắt. Không ai gọi TraKhung.

        var bayGio = DateTimeOffset.UtcNow;
        Assert.Equal(0, db.QuetTraKhungQuaHan(bayGio.AddMinutes(80)));      // chưa tới hạn ⇒ KHÔNG đụng
        Assert.Equal([MayB], db.MayDangBiDungViKm(LoginX));

        Assert.Equal(1, db.QuetTraKhungQuaHan(bayGio.AddMinutes(95)));      // quá hạn ⇒ tự trả
        Assert.Empty(db.MayDangBiDungViKm(LoginX));
        Assert.Contains(LenhCua(db, MayB), c => c.Action == OrdersCommandActions.Run);
    }

    /// <summary>Hai tài khoản khác nhau ⇒ hai sổ độc lập. Trả khung cho tài khoản này không được bật lại máy
    /// đang bị dừng vì tài khoản kia.</summary>
    [Fact]
    public void HaiTaiKhoan_ThiSoDocLap()
    {
        using var db = new HubDatabase(_dataDir);
        const string loginY = "sub-y@shopee.vn";
        ChoGiuKhoa(db, MayB, "NUC");
        db.ReserveAccounts(new AccountReserveRequest(["orders:" + loginY], MayB, "NUC"));

        db.XinKhung(LoginX, MayA);
        db.XinKhung(loginY, MayA);

        db.TraKhung(LoginX);

        Assert.Empty(db.MayDangBiDungViKm(LoginX));
        Assert.Equal([MayB], db.MayDangBiDungViKm(loginY));   // tài khoản kia còn nguyên
    }

    [Theory]
    [InlineData("", MayA)]
    [InlineData(LoginX, "")]
    public void ThieuThamSo_ThiTuChoiEm_KhongDeLenh(string login, string may)
    {
        using var db = new HubDatabase(_dataDir);
        ChoGiuKhoa(db, MayB, "NUC");

        var kq = db.XinKhung(login, may);

        Assert.False(kq.Trong);
        Assert.NotNull(kq.Loi);
        Assert.Empty(LenhCua(db, MayB));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dataDir, true); } catch { /* thư mục tạm */ }
    }
}
