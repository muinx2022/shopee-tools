using Shopee.Core.Coordination;
using Shopee.Hub;
using Shopee.Hub.Web.Components;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// Tầng HIỂN THỊ của lát cắt (KM/Combo/Flash/Mở-list-shop) trên trang Giao việc — plan
/// <c>plans/2026-09-04-guong-mang-lat-cat-dang-chay.md</c> mục 3 bước 6, tiêu chí N5. bUnit chưa có hạ tầng
/// (P-20260904-13) nên đây là tầng DUY NHẤT chạm được luật hiển thị: mọi quyết định (nút chính nào, ba nút ▶
/// vòng có mờ không, nhãn/màu ô trạng thái) đều nằm ở hàm THUẦN trong <see cref="DispatchViewLogic"/>, Razor
/// chỉ gọi lại — xem <c>DispatchOrdersTab.razor</c>.
/// </summary>
public sealed class DispatchLatCatViewTests
{
    // ── OrdersDangBan: cửa quyết nút chính "✖ Dừng" / "▶ Chạy" ─────────────────────────

    /// <summary>Trước bản vá 04/09/2026, nút chính chỉ nhìn <c>SessionState</c> — máy đang chạy Flash Sale thì
    /// phiên check đơn đã bị chính lát cắt dừng, chuỗi rỗng, nút hiện "▶ Chạy" giữa lúc gói đang chạy. Vế LatCat
    /// là vế SỐNG CÒN của cả đợt vá.</summary>
    [Theory]
    [InlineData("", null, false)]              // rảnh thật — hành vi cũ
    [InlineData(OrdersSessionStates.Running, null, true)]   // đang check đơn — hành vi cũ
    [InlineData("", "Flash Sale", true)]        // CA HỒI QUY: lát cắt dừng phiên, SessionState rỗng
    [InlineData(OrdersSessionStates.Stopping, "KM của Shop", true)]   // cả hai cùng bận (khoảnh khắc chờ dừng)
    [InlineData("", "", false)]                 // LatCat rỗng (không phải null) vẫn tính là không có
    public void OrdersDangBan_XetCaSessionLanLatCat(string sessionState, string? latCat, bool expected)
        => Assert.Equal(expected, DispatchViewLogic.OrdersDangBan(sessionState, latCat));

    // ── LatCatIcon: ký hiệu theo TÊN việc client gửi lên ────────────────────────────────

    [Theory]
    [InlineData("Flash Sale", "⚡")]
    [InlineData("Flash Sale Buổi Tối", "⚡")]          // chứa "Flash" vẫn nhận icon Flash
    [InlineData("Combo Khuyến Mãi", "📦")]
    [InlineData("KM của Shop", "🏷")]
    [InlineData("Mở list shop", "▶")]                  // tên lạ (không khớp 3 nhóm trên) → rơi về ▶, không mất chữ
    [InlineData(null, "")]
    [InlineData("", "")]
    public void LatCatIcon_TraKyHieuDungNhom(string? latCat, string expected)
        => Assert.Equal(expected, DispatchViewLogic.LatCatIcon(latCat));

    // ── OrdersTrangThaiLabel / Css: lát cắt THẮNG trạng thái phiên ──────────────────────

    [Fact]
    public void OrdersTrangThai_KhongLatCat_GiuNguyenHanhViCu()
    {
        // (c) không LatCat ⇒ hành vi cũ nguyên — chính là OrdersStateLabel/Css cũ, không đổi kết quả.
        Assert.Equal(DispatchViewLogic.OrdersStateLabel(OrdersSessionStates.Running),
            DispatchViewLogic.OrdersTrangThaiLabel(OrdersSessionStates.Running, null));
        Assert.Equal(DispatchViewLogic.OrdersStateCss(OrdersSessionStates.Running),
            DispatchViewLogic.OrdersTrangThaiCss(OrdersSessionStates.Running, null));
        Assert.Equal(DispatchViewLogic.OrdersStateLabel("unknown"),
            DispatchViewLogic.OrdersTrangThaiLabel("unknown", ""));
    }

    /// <summary>(a) CA CHÍNH: state rỗng + LatCat "Flash Sale" ⇒ nhãn đúng tên lát cắt kèm icon, màu như đang
    /// chạy ("run") — không phải màu "idle" mà <c>OrdersStateCss("")</c> sẽ trả.</summary>
    [Fact]
    public void OrdersTrangThai_CoLatCat_NhanTenLatCatMauRun()
    {
        Assert.Equal("⚡ Flash Sale", DispatchViewLogic.OrdersTrangThaiLabel("", "Flash Sale"));
        Assert.Equal("run", DispatchViewLogic.OrdersTrangThaiCss("", "Flash Sale"));
    }

    // ── WorkLabel: cột "Việc" của bảng KPI ──────────────────────────────────────────────

    [Fact]
    public void WorkLabel_DongDonHangCoLatCat_NoiTenLatCat()
    {
        var w = new DispatchWorkItem("k", "", DispatchViewLogic.OrdersOperation, "—", "acc@mail.com", "PC-01",
            DateTimeOffset.Now, "—", false, LatCat: "Flash Sale");

        Assert.Equal("⚡ Flash Sale", DispatchViewLogic.WorkLabel(w));
    }

    [Fact]
    public void WorkLabel_KhongLatCat_DungNhanChungTheoOp()
    {
        var w = new DispatchWorkItem("k", "", DispatchViewLogic.OrdersOperation, "—", "acc@mail.com", "PC-01",
            DateTimeOffset.Now, "—", false);

        Assert.Equal(FleetViewProjection.OperationLabel(DispatchViewLogic.OrdersOperation), DispatchViewLogic.WorkLabel(w));
    }

    // ── LatCatCuaMay: khoá ba nút ▶ vòng của MỌI dòng cùng máy ─────────────────────────

    private static OrdersMirrorAccount Acc(string login, string? latCat = null) =>
        new(login, "", false, null, DateTimeOffset.Now, []) { LatCat = latCat };

    /// <summary>Máy rảnh (không acc nào có LatCat) ⇒ null — ba nút ▶ vòng KHÔNG bị khoá bởi luật này.</summary>
    [Fact]
    public void LatCatCuaMay_KhongAiChayLatCat_ThiNull()
        => Assert.Null(DispatchViewLogic.LatCatCuaMay([Acc("a@mail.com"), Acc("b@mail.com")]));

    [Fact]
    public void LatCatCuaMay_DanhSachRong_ThiNull()
        => Assert.Null(DispatchViewLogic.LatCatCuaMay([]));

    /// <summary>(b) CA CHÍNH: một dòng của máy đang giữ lát cắt ⇒ trả đúng (tên, login) của dòng đó — đây là
    /// điều kiện Razor dùng để khoá CẢ BA nút ▶ vòng của MỌI dòng cùng máy, không riêng dòng đang chạy.</summary>
    [Fact]
    public void LatCatCuaMay_MotDongChayLatCat_TraDungTenVaLogin()
    {
        var got = DispatchViewLogic.LatCatCuaMay([Acc("a@mail.com"), Acc("b@mail.com", "Flash Sale")]);

        Assert.NotNull(got);
        Assert.Equal("Flash Sale", got!.Value.LatCat);
        Assert.Equal("b@mail.com", got.Value.Login);
    }

    /// <summary>"Máy khác không bị": <c>LatCatCuaMay</c> chỉ quét danh sách của MỘT máy (đúng cách Razor gọi nó
    /// — <c>_oAccounts</c> đã lọc theo máy đang chọn). Danh sách của máy KHÁC không chứa acc đang giữ lát cắt
    /// thì trả null dù hệ thống đang có một lát cắt chạy ở nơi khác.</summary>
    [Fact]
    public void LatCatCuaMay_DanhSachCuaMayKhac_KhongThayLatCatMayKia()
    {
        var mayA = new List<OrdersMirrorAccount> { Acc("a@mail.com", "Flash Sale") };
        var mayB = new List<OrdersMirrorAccount> { Acc("c@mail.com"), Acc("d@mail.com") };

        Assert.NotNull(DispatchViewLogic.LatCatCuaMay(mayA));
        Assert.Null(DispatchViewLogic.LatCatCuaMay(mayB));
    }

    // ── CanhBaoChayDeNhuong: nút "▶ Chạy" của tài khoản RẢNH khi MÁY đang bận cho acc khác (phản biện 04/09) ──

    /// <summary>Máy đang chạy lát cắt cho tài khoản KHÁC ⇒ tài khoản đang xét (rảnh) vẫn nhận cảnh báo trước khi
    /// bấm "▶ Chạy" — nút không bị khoá (chặn hẳn là quyết định khác), nhưng người vận hành phải biết trước hậu
    /// quả: bấm vào là ép lát cắt kia nhường chỗ.</summary>
    [Fact]
    public void CanhBaoChayDeNhuong_MayDangGiuChoAccKhac_ThiCoCanhBao()
    {
        var canhBao = DispatchViewLogic.CanhBaoChayDeNhuong("b@mail.com", ("Flash Sale", "a@mail.com"));

        Assert.NotNull(canhBao);
        Assert.Contains("Flash Sale", canhBao);
        Assert.Contains("a@mail.com", canhBao);
    }

    /// <summary>Máy rảnh ⇒ không cảnh báo.</summary>
    [Fact]
    public void CanhBaoChayDeNhuong_MayRanh_ThiNull()
        => Assert.Null(DispatchViewLogic.CanhBaoChayDeNhuong("a@mail.com", null));

    /// <summary>Lát cắt đang chạy ĐÚNG LÀ của chính tài khoản đang xét ⇒ không cảnh báo (nút đã là "✖ Dừng" ở
    /// nhánh khác rồi, hàm này không được gọi tới trong ca đó — nhưng vẫn phải đúng nếu lỡ gọi).</summary>
    [Fact]
    public void CanhBaoChayDeNhuong_LatCatLaCuaChinhAcc_ThiNull()
        => Assert.Null(DispatchViewLogic.CanhBaoChayDeNhuong("a@mail.com", ("Flash Sale", "a@mail.com")));
}
