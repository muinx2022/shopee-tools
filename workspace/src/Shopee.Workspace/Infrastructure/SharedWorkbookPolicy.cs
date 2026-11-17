namespace Shopee.Workspace.Infrastructure;

/// <summary>
/// Ai được thay đổi workbook chung (chọn file nguồn / gộp / ngừng dùng). App Harvest CHỈ ĐỌC: gộp lại đổi thứ tự dòng của
/// file gộp dùng chung, mà tiến độ <c>shared:</c> của app Scrape không biết gì về Harvest → Harvest gộp là làm tiến độ Scrape
/// bị cũ âm thầm. Workbook chung do app Scrape (hoặc Full / Update) quản lý; tiến độ Harvest dựng lại từ tập SP đã lấy
/// xong mỗi lần chạy nên không phụ thuộc thứ tự dòng (<see cref="Shopee.Core.Harvest.HarvestDoneRebuild"/>).
/// </summary>
public static class SharedWorkbookPolicy
{
    public const string ReadOnlyNote = "Workbook chung được quản lý ở app Scrape — app này chỉ đọc.";

    public static bool CanEdit(WorkspaceRole role) => role != WorkspaceRole.Harvest;

    public static bool CanEditCurrent => CanEdit(WorkspaceAppConfig.Role);
}
