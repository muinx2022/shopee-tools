using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary><see cref="SellerBrowserSession.CloseAsync"/>: đóng xong (true) thì gọi lại không đóng lần nữa; lượt trước chưa đóng hẳn
/// (false / lỗi) thì gọi lại phải đóng thêm một lượt.</summary>
public sealed class SellerBrowserSessionTests
{
    private static SellerBrowserSession Session(Func<Task<bool>> close) =>
        new(null, @"C:\p", new ProxyLease(ProxyScheme.Http, "1.2.3.4", 8080), "9.9.9.9", close);

    [Fact]
    public async Task DongXong_GoiLaiKhongDongThem()
    {
        var calls = 0;
        var s = Session(() => { calls++; return Task.FromResult(true); });

        Assert.True(await s.CloseAsync());
        Assert.True(await s.CloseAsync());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ChuaDongHan_GoiLaiThiDongThem()
    {
        var results = new Queue<bool>([false, true]);
        var calls = 0;
        var s = Session(() => { calls++; return Task.FromResult(results.Dequeue()); });

        Assert.False(await s.CloseAsync());
        Assert.True(await s.CloseAsync());
        Assert.Equal(2, calls);
        Assert.True(await s.CloseAsync());
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task LuotTruocLoi_GoiLaiThiDongThem()
    {
        var calls = 0;
        var s = Session(() => ++calls == 1 ? Task.FromException<bool>(new IOException("x")) : Task.FromResult(true));

        await Assert.ThrowsAsync<IOException>(() => s.CloseAsync());
        Assert.True(await s.CloseAsync());
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DangDong_GoiDongThoi_ChungMotLuot()
    {
        var tcs = new TaskCompletionSource<bool>();
        var calls = 0;
        var s = Session(() => { calls++; return tcs.Task; });

        var a = s.CloseAsync();
        var b = s.CloseAsync();
        tcs.SetResult(true);

        Assert.True(await a);
        Assert.True(await b);
        Assert.Equal(1, calls);
    }
}
