using Shopee.Core.BigSeller;
using static Shopee.Core.BigSeller.BigSellerSessionProbe;

namespace Shopee.Core.Tests;

/// <summary>
/// Phân loại phản hồi probe phiên BigSeller (thuần, không mạng). Rớt mạng/timeout/trả lời lạ = Unreachable —
/// KHÔNG được coi là mất phiên (bản cũ trả false cho mọi lỗi → rớt mạng kích hoạt đăng nhập → MarkFailed oan).
/// </summary>
public sealed class BigSellerSessionProbeTests
{
    [Theory]
    [InlineData(200, "{\"code\":0,\"data\":{}}", Alive)]
    [InlineData(200, "{\"code\":2001,\"msg\":\"x\"}", Rejected)]
    [InlineData(200, "{\"code\":401006}", Rejected)]
    [InlineData(401, "", Rejected)]
    [InlineData(403, "<html>forbidden</html>", Rejected)]
    [InlineData(401, "{\"code\":0}", Rejected)]
    [InlineData(500, "", Unreachable)]
    [InlineData(502, "<html>bad gateway</html>", Unreachable)]
    [InlineData(404, "not found", Unreachable)]
    [InlineData(200, "<html>captive portal</html>", Unreachable)]
    [InlineData(200, "", Unreachable)]
    [InlineData(200, "{\"data\":1}", Unreachable)]
    [InlineData(200, "{\"code\":\"0\"}", Unreachable)]
    [InlineData(500, "{\"code\":0}", Unreachable)]
    public void ClassifyProbeResponse_Single(int status, string body, BigSellerSessionProbe expected) =>
        Assert.Equal(expected, BigSellerCookieEngine.ClassifyProbeResponse(status, body));

    [Fact]
    public void ClassifyProbeResponse_NullBody_Unreachable() =>
        Assert.Equal(Unreachable, BigSellerCookieEngine.ClassifyProbeResponse(200, null));

    [Theory]
    [InlineData(Alive, Alive, Alive)]
    [InlineData(Alive, Rejected, Alive)]
    [InlineData(Rejected, Alive, Alive)]
    [InlineData(Alive, Unreachable, Alive)]
    [InlineData(Unreachable, Alive, Alive)]
    [InlineData(Rejected, Rejected, Rejected)]
    [InlineData(Rejected, Unreachable, Unreachable)]   // chập mạng ở endpoint thật + endpoint kia từ chối ≠ mất phiên
    [InlineData(Unreachable, Rejected, Unreachable)]
    [InlineData(Unreachable, Unreachable, Unreachable)]
    public void CombineProbes_AnyAlive_ElseAnyUnreachable_ElseRejected(
        BigSellerSessionProbe a, BigSellerSessionProbe b, BigSellerSessionProbe expected) =>
        Assert.Equal(expected, BigSellerCookieEngine.CombineProbes([a, b]));

    [Fact]
    public void CombineProbes_Empty_Unreachable() =>
        Assert.Equal(Unreachable, BigSellerCookieEngine.CombineProbes([]));

    [Fact]
    public async Task NoCookieFile_Rejected_WithoutNetwork()
    {
        var acc = new BigSellerAccount { CookieFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json") };
        Assert.Equal(Rejected, await BigSellerCookieEngine.ProbeSessionAsync(acc));
        Assert.False(await BigSellerCookieEngine.IsSessionAliveAsync(acc));
    }
}
