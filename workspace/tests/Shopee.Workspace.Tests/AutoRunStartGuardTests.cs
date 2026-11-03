using Shopee.Workspace.Modules.Workspace;

namespace Shopee.Workspace.Tests;

/// <summary>Luật chặn bấm nhầm "Bắt đầu Auto Run" ngay sau khi lượt trước kết thúc (nút Bắt đầu nhảy vào chỗ nút Dừng).</summary>
public sealed class AutoRunStartGuardTests
{
    private static readonly DateTime Ended = new(2026, 11, 3, 8, 20, 33, DateTimeKind.Utc);

    [Fact]
    public void NeverRan_StartAllowed() => Assert.False(AutoRunViewModel.ShouldIgnoreStart(null, Ended));

    [Theory]
    [InlineData(0, true)]
    [InlineData(1000, true)]     // log thật: lượt 2 bắt đầu sau 1s
    [InlineData(2999, true)]
    [InlineData(3000, false)]    // đúng 3s trở đi mới cho Bắt đầu
    [InlineData(60000, false)]
    public void StartWithinThreeSecondsOfLastEnd_IsIgnored(int msAfterEnd, bool expectedIgnored) =>
        Assert.Equal(expectedIgnored, AutoRunViewModel.ShouldIgnoreStart(Ended, Ended.AddMilliseconds(msAfterEnd)));
}
