using System.Text.Json;
using Shopee.Core.BigSeller;
using Shopee.Core.Infrastructure;

namespace Shopee.Workspace.Infrastructure;

/// <summary>
/// Cấu hình CHẠY (từ dòng / đến dòng / số cửa sổ / khung / nghỉ giữa link…) RIÊNG của app Harvest, theo tài khoản BigSeller,
/// lưu ở <c>shared\harvest-run-config.json</c>. App Scrape dùng <see cref="BigSellerAccount.RunConfig"/> (trong
/// <c>bigseller.json</c>); Harvest ghi vào đó thì vd "Từ dòng 500" ở Harvest làm Scrape bỏ sót dòng 2–499. Lần đầu một tk
/// chưa có cấu hình Harvest → khởi tạo bằng BẢN SAO cấu hình hiện tại của tk (không ghi ngược).
/// </summary>
public sealed class HarvestRunConfigStore
{
    private static readonly Lazy<HarvestRunConfigStore> _shared =
        new(() => new HarvestRunConfigStore(Path.Combine(SuitePaths.ModuleDir("shared"), "harvest-run-config.json")));

    public static HarvestRunConfigStore Shared => _shared.Value;

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly object _lock = new();
    private readonly Dictionary<string, BigSellerRunConfig> _items;

    internal string FilePath { get; }

    internal HarvestRunConfigStore(string filePath)
    {
        FilePath = filePath;
        _items = JsonAtomicFile.TryLoad<Dictionary<string, BigSellerRunConfig>>(FilePath, JsonOpts)
                 ?? new Dictionary<string, BigSellerRunConfig>(StringComparer.Ordinal);
    }

    /// <summary>Cấu hình Harvest của tk (cùng một object mỗi lần gọi). Chưa có → tạo mới: DẢI DÒNG mặc định (từ dòng 2, tới
    /// hết sheet — KHÔNG ăn theo dải dòng đang đặt cho app Scrape), chỉ chép thông số máy (số cửa sổ, khung, nghỉ giữa link) từ
    /// <see cref="BigSellerAccount.RunConfig"/>; LƯU NGAY (lần mở sau không chép lại). Không đụng object của tk.</summary>
    public BigSellerRunConfig For(BigSellerAccount account)
    {
        lock (_lock)
        {
            if (_items.TryGetValue(account.Id, out var cfg)) return cfg;
            cfg = Seed(account.RunConfig);
            _items[account.Id] = cfg;
            JsonAtomicFile.Save(FilePath, _items, JsonOpts);
            return cfg;
        }
    }

    internal static BigSellerRunConfig Seed(BigSellerRunConfig? scrape)
    {
        var cfg = new BigSellerRunConfig();   // StartRow = 2, EndRow = 0 (toàn bộ)
        if (scrape is null) return cfg;
        cfg.Processes = scrape.Processes;
        cfg.FrameSize = scrape.FrameSize;
        cfg.RestMinSeconds = scrape.RestMinSeconds;
        cfg.RestMaxSeconds = scrape.RestMaxSeconds;
        return cfg;
    }

    public void Save()
    {
        lock (_lock) JsonAtomicFile.Save(FilePath, _items, JsonOpts);
    }

}

/// <summary>Nguồn cấu hình chạy theo VAI TRÒ: Harvest → <see cref="HarvestRunConfigStore"/>; vai trò khác → đúng như cũ
/// (<see cref="BigSellerAccount.RunConfig"/>, lưu <c>bigseller.json</c>).</summary>
public static class RunConfigSource
{
    public static BigSellerRunConfig For(WorkspaceRole role, BigSellerAccount account, HarvestRunConfigStore? harvest = null) =>
        role == WorkspaceRole.Harvest
            ? (harvest ?? HarvestRunConfigStore.Shared).For(account)
            : account.RunConfig ??= new BigSellerRunConfig();

    public static void Save(WorkspaceRole role, HarvestRunConfigStore? harvest = null, Action? saveBigSeller = null)
    {
        if (role == WorkspaceRole.Harvest) (harvest ?? HarvestRunConfigStore.Shared).Save();
        else (saveBigSeller ?? (() => BigSellerStore.Shared.Save()))();
    }
}
