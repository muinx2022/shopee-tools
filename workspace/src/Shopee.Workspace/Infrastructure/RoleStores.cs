using Shopee.Core.Coordination;
using Shopee.Core.Scrape;

namespace Shopee.Workspace.Infrastructure;

/// <summary>
/// Kho tiến độ / cấu hình đích của luồng "scrape" THEO VAI TRÒ process. App Harvest dùng lại nguyên màn Workspace
/// chế độ scrape-only nhưng phải ghi vào file RIÊNG (<c>harvest-progress.json</c> / <c>harvest-targets.json</c>) — dòng
/// đã lấy JSON không được tính là dòng đã cào BigSeller và ngược lại. Mọi vai trò khác trả ĐÚNG kho
/// <c>Shared</c> như trước (Scrape / Update / Full không đổi hành vi).
/// </summary>
public static class RoleStores
{
    public static ScrapeProgressStore ScrapeProgress => For(WorkspaceAppConfig.Role).Progress;

    public static ScrapeTargetConfigStore ScrapeTargets => For(WorkspaceAppConfig.Role).Targets;

    /// <summary>Op điều phối của luồng scrape theo vai trò (khoá <see cref="CoordKey"/>).</summary>
    public static CoordOp ScrapeOp => For(WorkspaceAppConfig.Role).Op;

    /// <summary>Luật thuần (test được): Harvest → kho Harvest + <see cref="CoordOp.Harvest"/>; còn lại → kho Shared +
    /// <see cref="CoordOp.Scrape"/>.</summary>
    internal static (ScrapeProgressStore Progress, ScrapeTargetConfigStore Targets, CoordOp Op) For(WorkspaceRole role) =>
        role == WorkspaceRole.Harvest
            ? (ScrapeProgressStore.Harvest, ScrapeTargetConfigStore.Harvest, CoordOp.Harvest)
            : (ScrapeProgressStore.Shared, ScrapeTargetConfigStore.Shared, CoordOp.Scrape);
}
