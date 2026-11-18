using Shopee.Core.Coordination;
using Shopee.Core.Infrastructure;

namespace Shopee.Core.Tests;

/// <summary>
/// GĐ7.1 — app Harvest: init Hub CHỈ-CLIENT (<see cref="CoordinationRuntime.ClientOnly"/>) với kho cấu hình Hub RIÊNG
/// (<see cref="CoordinationRuntime.ClientStore"/>, <c>harvest\hub-client.json</c>). Không dựng <see cref="HttpCoordinationHub"/>
/// (không heartbeat / poll /fleet / lease / kéo cấu hình BigSeller), không suất đơn hàng, cả lúc khởi động lẫn lúc nối lại; lưu cấu
/// hình Harvest không tạo / sửa <c>hub-client.json</c> chung của app Scrape / Update. Test đổi static → reset ở finally.
/// (Core.Tests có <c>data-dir.txt</c> → SuitePaths là thư mục test, không đụng %AppData% thật.)
/// </summary>
[Collection("CoordinationRuntime")]
public sealed class CoordinationClientOnlyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "coord-client-only-" + Guid.NewGuid().ToString("N"));

    public CoordinationClientOnlyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        CoordinationRuntime.ClientOnly = false;
        CoordinationRuntime.ClientStore = null;
        CoordinationRuntime.Reconnect();   // dọn client / timer của test (kho chung của thư mục test không có cấu hình → NoOp)
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void ClientOnly_KhoRieng_InitVaReconnect_ChiDungClient_KhongHubKhongSuatDonHang()
    {
        var shared = SuitePaths.RootFile("hub-client.json");
        var sharedBefore = File.Exists(shared) ? File.GetLastWriteTimeUtc(shared) : (DateTime?)null;

        var store = new HubClientConfigStore(Path.Combine(_dir, "harvest", "hub-client.json"));
        Directory.CreateDirectory(Path.Combine(_dir, "harvest"));
        Assert.True(store.Save(new HubClientConfig { Enabled = true, BaseUrl = "http://127.0.0.1:1", ApiToken = "tok-test" }));
        Assert.True(File.Exists(store.FilePath));

        CoordinationRuntime.ClientStore = store;
        CoordinationRuntime.ClientOnly = true;

        CoordinationRuntime.InitForCurrentMode();
        AssertClientOnly();
        var first = CoordinationRuntime.Client;
        Assert.Equal("http://127.0.0.1:1", CoordinationRuntime.HarvestHubAccess()!.BaseUrl);   // đọc kho RIÊNG

        Assert.True(CoordinationRuntime.Reconnect());
        AssertClientOnly();
        Assert.NotSame(first, CoordinationRuntime.Client);                              // nối lại = client mới, bản cũ đã dispose

        // Kho chung (app Scrape / Update) không bị tạo / sửa.
        Assert.Equal(sharedBefore, File.Exists(shared) ? File.GetLastWriteTimeUtc(shared) : (DateTime?)null);
        Assert.False(HubClientConfigStore.Shared.Current.IsConfigured && HubClientConfigStore.Shared.Current.ApiToken == "tok-test");
    }

    private static void AssertClientOnly()
    {
        Assert.NotNull(CoordinationRuntime.Client);
        Assert.Null(CoordinationRuntime.Hub);
        Assert.Same(NoOpCoordinationHub.Instance, Coordination.Coordination.Hub);
        Assert.Null(CoordinationRuntime.OrdersSlot);
        Assert.Null(CoordinationRuntime.Relogin);
    }

    [Fact]
    public void MacDinh_KhoChung_KhongClientOnly()
    {
        Assert.False(CoordinationRuntime.ClientOnly);
        Assert.Same(HubClientConfigStore.Shared, CoordinationRuntime.ClientStore);
        Assert.Equal(SuitePaths.RootFile("hub-client.json"), HubClientConfigStore.Shared.FilePath);
    }

    [Fact]
    public void LuuHong_TraFalse()
    {
        // Đường dẫn file đang là THƯ MỤC → không ghi đè được → Save trả false (không ném) để màn Kết nối Hub báo lỗi.
        var path = Path.Combine(_dir, "hub-client.json");
        Directory.CreateDirectory(path);
        var store = new HubClientConfigStore(path);
        Assert.False(store.Save(new HubClientConfig { Enabled = true, BaseUrl = "https://x.test", ApiToken = "t" }));
    }
}
