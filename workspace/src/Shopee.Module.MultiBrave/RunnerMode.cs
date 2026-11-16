using Shopee.Core.Harvest;

namespace Shopee.Modules.MultiBrave;

/// <summary>Chế độ chạy của engine MultiBrave. Một PROCESS chỉ chạy một chế độ (exe Scrape / exe Harvest) — đặt bằng
/// <see cref="MultiBraveRuntime.SetMode"/> lúc khởi động, trước khi dựng <see cref="ScrapeRunner"/>.</summary>
public enum RunnerMode
{
    /// <summary>Bấm Scrape của BigSeller (extension <c>shopee-scrape</c>) — mặc định, hành vi cũ.</summary>
    Scrape,
    /// <summary>Lấy JSON <c>get_pc</c> của Shopee (extension <c>shopee-harvest</c>), giao <see cref="IHarvestSink"/>;
    /// không BigSeller (không cookie/token, không Product Copy, không video).</summary>
    Harvest,
}

/// <summary>Tham số riêng của lượt chạy Harvest, caller (GĐ5) truyền qua <see cref="ScrapeRunner.Harvest"/>.</summary>
/// <param name="Sink">Nơi lưu dữ liệu (outbox GĐ4). Bắt buộc.</param>
/// <param name="ShopId">Shop BigSeller đích của lượt chạy — đi vào <see cref="HarvestedItem.ShopId"/>.</param>
/// <param name="PdpErrorBreakerThreshold">N dòng liên tiếp có pdpError thì coi như bị chặn (mặc định 3).</param>
/// <param name="BlockErrorCodes">Mã lỗi get_pc coi là chặn bot; null = để mặc định của extension ([90309999]).</param>
public sealed record HarvestRunOptions(
    IHarvestSink Sink,
    string ShopId,
    int PdpErrorBreakerThreshold = HarvestStepPolicy.DefaultPdpErrorBreakerThreshold,
    IReadOnlyList<long>? BlockErrorCodes = null);
