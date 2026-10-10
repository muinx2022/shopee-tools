// System.IO KHÔNG có trong danh sách implicit của SDK desktop (net8.0-windows).
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Shopee.Suite.Infrastructure;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.Core.Models;

namespace Shopee.Suite.Tests;

/// <summary>
/// <b>GƯƠNG danh bạ mang theo LÁT CẮT đang chạy</b> — <c>OrdersModuleHost.BuildOrdersMirror</c> hỏi hook
/// <see cref="AppServices.LatCatDangChayCua"/> cho TỪNG tài khoản, và <c>RotNguonKichGuong</c> nối hook
/// <see cref="AppServices.LatCatDoi"/> vào cờ bẩn của worker.
///
/// <para><b>Vì sao đo ở đây:</b> module Đơn hàng KHÔNG tham chiếu <c>Shopee.Core</c> nên nó không thấy DTO gương;
/// suite là nơi duy nhất nối hai bên. Trước 04/09/2026 gương chỉ chở trạng thái phiên check đơn — mà một lát cắt
/// bắt đầu bằng việc DỪNG mọi phiên để mượn hồ sơ Brave, nên hub thấy đúng chuỗi rỗng = "rảnh" và mời bấm
/// "▶ Chạy" giữa lúc gói Flash Sale đang chạy (sổ P-20260904-12).</para>
/// </summary>
public sealed class GuongLatCatQuaHookTests : IDisposable
{
    private const string LoginA = "acc-a@shopee.vn";
    private const string LoginB = "acc-b@shopee.vn";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"suite_guong_latcat_{Guid.NewGuid():N}.db");
    private readonly AppServices _services;
    private readonly long _idA;
    private readonly long _idB;

    public GuongLatCatQuaHookTests()
    {
        _services = new AppServices(_dbPath);
        _services.Accounts.Insert(new Account { Email = LoginA, Password = "p" });
        _services.Accounts.Insert(new Account { Email = LoginB, Password = "p" });
        var all = _services.Accounts.GetAll();
        _idA = all.First(a => a.Email == LoginA).Id;
        _idB = all.First(a => a.Email == LoginB).Id;
    }

    /// <summary>Máy đang chạy "Flash Sale" cho A ⇒ dòng gương của A mang đúng tên đó, dòng B vẫn <c>null</c>.
    /// Vế B là vế sống-chết: nhãn lem sang tài khoản khác thì hub hiện "✖ Dừng" ở dòng B và cú bấm đi dừng
    /// nhầm chỗ.</summary>
    [Fact]
    public void HookTraTenChoMotTaiKhoan_ChiTaiKhoanDoMangNhan()
    {
        _services.LatCatDangChayCua = id => id == _idA ? "Flash Sale" : null;

        var payload = OrdersModuleHost.BuildOrdersMirror(_services);

        Assert.Equal("Flash Sale", payload.Accounts.Single(a => a.Login == LoginA).LatCat);
        Assert.Null(payload.Accounts.Single(a => a.Login == LoginB).LatCat);
    }

    /// <summary>Hook chưa rót (màn Tài khoản chưa dựng) ⇒ MỌI dòng <c>null</c> — đúng bằng hành vi bản client
    /// trước bản vá, tức hub mới nhận client cũ không hỏng gì.</summary>
    [Fact]
    public void HookChuaRot_ThiMoiDongNull()
    {
        Assert.Null(_services.LatCatDangChayCua);

        var payload = OrdersModuleHost.BuildOrdersMirror(_services);

        Assert.Equal(2, payload.Accounts.Count);
        Assert.All(payload.Accounts, a => Assert.Null(a.LatCat));
    }

    /// <summary>Gương KHÔNG được nuốt mất trạng thái phiên khi thêm lát cắt: hai trường sống độc lập.
    /// <para>Bài trước (04/09/2026, phản biện) dựng bàn CHƯA từng tạo phiên nào nên <c>SessionState == ""</c>
    /// đúng với MỌI cách cài <c>BuildOrdersMirror</c> — kể cả một bản đã bị moi sạch dòng
    /// <c>MirrorSessionState(...)</c> (đã CHỨNG MINH bằng đột biến: đỏ không nổi). Bài này dựng một phiên B THẬT
    /// đang chạy để "" (của A, có LatCat) và một chuỗi khác-rỗng (của B, không LatCat) cùng có mặt trong MỘT lần
    /// gọi — sai một trong hai trường là đỏ ngay.</para></summary>
    [Fact]
    public void CoLatCat_TrangThaiPhienVanLaChuoiRieng()
    {
        _services.LatCatDangChayCua = id => id == _idA ? "KM của Shop" : null;
        _services.Sessions.Start(_idB); // B đang check đơn thật — SessionState của B phải khác rỗng

        var payload = OrdersModuleHost.BuildOrdersMirror(_services).Accounts;
        var a = payload.Single(x => x.Login == LoginA);
        var b = payload.Single(x => x.Login == LoginB);

        Assert.Equal("KM của Shop", a.LatCat);
        Assert.Equal("", a.SessionState);        // A: lát cắt đã dừng mọi phiên — đúng chỗ bản cũ tưởng "rảnh"
        Assert.Null(b.LatCat);
        Assert.NotEqual("", b.SessionState);     // B: phiên check đơn thật, KHÔNG bị gương nuốt mất
    }

    /// <summary>ĐƯỜNG KÍCH: hook <see cref="AppServices.LatCatDoi"/> phải chạm tới cờ bẩn của worker gương.
    /// Không có nó thì lát cắt bắt đầu lúc máy chưa chạy phiên nào sẽ KHÔNG sinh sự kiện nào cả và hub chỉ biết
    /// ở nhịp nền 60s — đúng cửa sổ người vận hành cần nút ✖ Dừng.</summary>
    [Fact]
    public void LatCatDoi_BatCoBanCuaWorkerGuong()
    {
        OrdersModuleHost.RotNguonKichGuong(_services);
        OrdersModuleHost._mirrorDirty = false;

        _services.LatCatDoi!.Invoke();

        Assert.True(OrdersModuleHost._mirrorDirty);
    }

    public void Dispose()
    {
        _services.LatCatDangChayCua = null;
        _services.LatCatDoi = null;
        _services.Log.Dispose();
        using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString()))
        {
            SqliteConnection.ClearPool(conn);
        }
        try { File.Delete(_dbPath); } catch { /* file còn bị giữ → kệ, thư mục temp */ }
        GC.SuppressFinalize(this);
    }
}
