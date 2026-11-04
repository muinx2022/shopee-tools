using Shopee.Workspace.Modules.Workspace;

namespace Shopee.Workspace.Tests;

/// <summary>
/// <see cref="WorkspaceShopViewModel.ChooseProgressText"/>: chữ cột TIẾN ĐỘ khi Update chạm listing limit.
/// </summary>
public sealed class WorkspaceShopProgressLineTests
{
    private const string Cu = "Đã cập nhật 2667 dòng";
    private const string GioiHan = "Đã đạt giới hạn SP của shop";

    [Fact]
    public void ProgressLine_LiveThang()
    {
        const string live = "Đang update dòng 12 sheet Ảnh";
        Assert.Equal(live, WorkspaceShopViewModel.ChooseProgressText(live, listingLimit: true, updateDone: true, Cu));
        Assert.Equal(live, WorkspaceShopViewModel.ChooseProgressText(live, listingLimit: false, updateDone: false, ""));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProgressLine_CoCo_DungCauGioiHan_KeCaChuaXongVaKeCaDaCoSoDong(bool updateDone)
    {
        Assert.Equal(GioiHan, WorkspaceShopViewModel.ChooseProgressText(null, listingLimit: true, updateDone, Cu));
        Assert.Equal(GioiHan, WorkspaceShopViewModel.ChooseProgressText("", listingLimit: true, updateDone, "Đã cập nhật 0 dòng"));
        Assert.Equal(WorkspaceShopViewModel.ListingLimitProgressText, GioiHan);
    }

    [Fact]
    public void ProgressLine_KhongCo_DaXong_TraChuCu()
    {
        Assert.Equal(Cu, WorkspaceShopViewModel.ChooseProgressText(null, listingLimit: false, updateDone: true, Cu));
        Assert.Equal("Đã cập nhật 0 dòng", WorkspaceShopViewModel.ChooseProgressText(null, listingLimit: false, updateDone: true, "Đã cập nhật 0 dòng"));
    }

    [Fact]
    public void ProgressLine_KhongCo_ChuaXong_Rong()
    {
        Assert.Equal("", WorkspaceShopViewModel.ChooseProgressText(null, listingLimit: false, updateDone: false, Cu));
        Assert.Equal("", WorkspaceShopViewModel.ChooseProgressText("", listingLimit: false, updateDone: false, ""));
    }
}
