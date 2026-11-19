using Shopee.Workspace.Modules.Workspace;

namespace Shopee.Workspace.Tests;

/// <summary>
/// <see cref="WorkspaceShopViewModel.ChooseProgressText"/>: cột TIẾN ĐỘ ghi Import và Update dạng đã / tổng.
/// </summary>
public sealed class WorkspaceShopProgressLineTests
{
    [Fact]
    public void ProgressLine_LiveThang()
    {
        const string live = "Đang update dòng 12 sheet Ảnh";
        Assert.Equal(live, WorkspaceShopViewModel.ChooseProgressText(live, listingLimit: true, 10, 20, 5, 30));
        Assert.Equal(live, WorkspaceShopViewModel.ChooseProgressText(live, listingLimit: false, 0, 0, 0, 0));
    }

    [Fact]
    public void ProgressLine_CoSo_GhiHaiDong_KeCaChuaXong()
    {
        var text = WorkspaceShopViewModel.ChooseProgressText(null, listingLimit: false, 3693, 3703, 3180, 3703);
        Assert.Equal("Import: 3693 / 3703\nUpdate: 3180 / 3703", text);
    }

    [Fact]
    public void ProgressLine_ChamGioiHan_VanGiuSo_RoiThemCauGioiHan()
    {
        var text = WorkspaceShopViewModel.ChooseProgressText(null, listingLimit: true, 0, 3703, 2667, 3703);
        Assert.Equal(
            "Import: 0 / 3703\nUpdate: 2667 / 3703\n" + WorkspaceShopViewModel.ListingLimitProgressText,
            text);
    }

    [Fact]
    public void ProgressLine_ChuaCoSo_KhongCoGioiHan_Rong()
    {
        Assert.Equal("", WorkspaceShopViewModel.ChooseProgressText(null, listingLimit: false, 0, 0, 0, 0));
        Assert.Equal("", WorkspaceShopViewModel.ChooseProgressText("", listingLimit: false, 0, 0, 0, 0));
    }

    [Fact]
    public void ProgressLine_ChuaCoSo_CoGioiHan_ChiCauGioiHan()
    {
        Assert.Equal(
            WorkspaceShopViewModel.ListingLimitProgressText,
            WorkspaceShopViewModel.ChooseProgressText(null, listingLimit: true, 0, 0, 0, 0));
    }
}
