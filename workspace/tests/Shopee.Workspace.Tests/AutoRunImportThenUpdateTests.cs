using Shopee.Workspace.Modules.Workspace;

namespace Shopee.Workspace.Tests;

/// <summary>Auto Run: sau Import của shop LUÔN chạy Update (không còn "continue" bỏ shop khi Import 0 SP / bị chặn); hàm này chỉ chọn câu log.</summary>
public sealed class AutoRunImportThenUpdateTests
{
    [Theory]
    [InlineData(0, 0, false, true)]      // Import 0 SP, chưa xong (hoặc bị chặn): ca từng bị bỏ qua cả Update
    [InlineData(0, 0, true, false)]      // shop đã Import xong
    [InlineData(5, 5, false, false)]     // import được SP
    [InlineData(0, 5, false, false)]     // không thêm SP mới nhưng đã có SP từ trước
    [InlineData(-3, 0, false, false)]    // Clear đầu lượt bỏ SP khỏi Import Done (imported âm) -> không phải ca 0 SP
    public void IsZeroImportRun_OnlyForNothingImportedAndNotDone(int imported, int after, bool importDone, bool expected) =>
        Assert.Equal(expected, AutoRunViewModel.IsZeroImportRun(imported, after, importDone));
}
