namespace Shopee.Workspace.Infrastructure;

/// <summary>Nhãn của luồng "scrape" theo vai trò process: app Harvest gọi việc này là "Lấy dữ liệu" (không bấm Scrape
/// của BigSeller). Vai trò khác giữ NGUYÊN chữ cũ.</summary>
public static class RoleText
{
    /// <summary>"Scrape" | "Lấy dữ liệu" (đầu câu / tên việc).</summary>
    public static string ScrapeOp => WorkspaceAppConfig.IsHarvest ? "Lấy dữ liệu" : "Scrape";

    /// <summary>"scrape" | "lấy dữ liệu" (giữa câu).</summary>
    public static string ScrapeOpLower => WorkspaceAppConfig.IsHarvest ? "lấy dữ liệu" : "scrape";
}
