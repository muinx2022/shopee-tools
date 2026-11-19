using System.ComponentModel;
using System.Runtime.InteropServices;
using Shopee.Workspace.Services;

namespace Shopee.Workspace.Tests;

/// <summary>
/// Luật chọn nhánh lỗi UI. Không mở cửa sổ, không đụng %AppData%.
/// </summary>
public sealed class UiErrorPolicyTests
{
    private static Exception RenderFailure() =>
        new COMException("UCEERR_RENDERTHREADFAILURE", UiErrorPolicy.RenderThreadFailureHResult);

    [Fact]
    public void BareRenderThreadFailure_IsShutdown()
        => Assert.Equal(UiErrorAction.Shutdown, UiErrorPolicy.Decide(RenderFailure(), inHandler: false));

    [Fact]
    public void HResultOnPlainException_IsShutdown()
    {
        var ex = new InvalidOperationException("SyncFlush");
        ex.HResult = UiErrorPolicy.RenderThreadFailureHResult;
        Assert.Equal(UiErrorAction.Shutdown, UiErrorPolicy.Decide(ex, inHandler: false));
    }

    [Fact]
    public void RenderThreadFailure_WrappedInInnerException_IsShutdown()
    {
        var wrapped = new InvalidOperationException("outer", new Exception("mid", RenderFailure()));
        Assert.Equal(UiErrorAction.Shutdown, UiErrorPolicy.Decide(wrapped, inHandler: false));
    }

    [Fact]
    public void RenderThreadFailure_WinsEvenInsideHandler()
        => Assert.Equal(UiErrorAction.Shutdown, UiErrorPolicy.Decide(RenderFailure(), inHandler: true));

    [Fact]
    public void InHandler_OrdinaryError_IsIgnore()
        => Assert.Equal(UiErrorAction.Ignore, UiErrorPolicy.Decide(new InvalidOperationException("popup"), inHandler: true));

    [Fact]
    public void InHandler_Win32FromDialog_IsIgnore()
        => Assert.Equal(UiErrorAction.Ignore, UiErrorPolicy.Decide(new Win32Exception("ShowDialog"), inHandler: true));

    [Theory]
    [MemberData(nameof(BenignCases))]
    public void BenignTeardown_IsIgnore(Exception ex)
        => Assert.Equal(UiErrorAction.Ignore, UiErrorPolicy.Decide(ex, inHandler: false));

    public static IEnumerable<object[]> BenignCases()
    {
        yield return new object[] { new ObjectDisposedException("cts") };
        yield return new object[] { new OperationCanceledException() };
        yield return new object[] { new Exception("CancellationTokenSource has been disposed") };
        yield return new object[] { new Exception("THE CANCELLATIONTOKENSOURCE HAS BEEN DISPOSED.") };
        yield return new object[] { new Exception("outer", new ObjectDisposedException("inner")) };
        yield return new object[] { new InvalidOperationException("wrap", new Exception("CancellationTokenSource has been disposed")) };
    }

    [Fact]
    public void OrdinaryError_IsNotify()
        => Assert.Equal(UiErrorAction.Notify, UiErrorPolicy.Decide(new InvalidOperationException("lỗi UI"), inHandler: false));

    [Fact]
    public void Win32OutsideHandler_IsNotify()
        => Assert.Equal(UiErrorAction.Notify, UiErrorPolicy.Decide(new Win32Exception("ShowDialog"), inHandler: false));

    [Fact]
    public void NearMissHResult_IsNotify()
    {
        var ex = new COMException("khác", unchecked((int)0x88980405));
        Assert.Equal(UiErrorAction.Notify, UiErrorPolicy.Decide(ex, inHandler: false));
    }
}
