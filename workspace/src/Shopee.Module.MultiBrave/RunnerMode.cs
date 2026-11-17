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
/// <param name="ShopId">Nhãn tuỳ chọn của lượt chạy — đi vào <see cref="HarvestedItem.ShopId"/> (không gửi Hub).</param>
/// <param name="PdpErrorBreakerThreshold">N dòng liên tiếp có pdpError thì coi như bị chặn (mặc định 3).</param>
/// <param name="BlockErrorCodes">Mã lỗi get_pc coi là chặn bot; null = để mặc định của extension ([90309999]).</param>
/// <param name="HubStatusText">Tuỳ chọn: một dòng tình trạng đẩy Hub (vd <see cref="HarvestOverlayText.HubLine"/>) cho bảng
/// tổng kết cuối khối. null / ném / quá hạn → bỏ dòng Hub, không ảnh hưởng kết thúc khối.</param>
/// <param name="RecordDone">true → ghi khoá SP lưu được vào tập "SP đã lấy xong" TOÀN CỤC (đi vào
/// <see cref="HarvestedItem.RecordDone"/>) và SP lỗi vĩnh viễn vào <paramref name="FailureSink"/>; caller dựng lại tiến độ từ hai
/// tập này ở lượt sau. false = không ghi.</param>
/// <param name="FailureSink">Nơi ghi SP lỗi VĨNH VIỄN (lượt sau không lên lịch). null = không ghi.</param>
/// <param name="RunId">Định danh lượt chạy (job) — lỗi vĩnh viễn chỉ được xác nhận khi thấy ở nhiều lượt khác nhau. null =
/// mỗi lần dựng bộ xử lý dòng là một lượt.</param>
/// <param name="SampleLink">Nguồn dự phòng của link mẫu kiểm chứng phiên (vd link SP lưu được gần nhất trong outbox) khi khối
/// chưa lưu được dòng nào. null / trả null = không có mẫu.</param>
public sealed record HarvestRunOptions(
    IHarvestSink Sink,
    string ShopId,
    int PdpErrorBreakerThreshold = HarvestStepPolicy.DefaultPdpErrorBreakerThreshold,
    IReadOnlyList<long>? BlockErrorCodes = null,
    Func<CancellationToken, Task<string?>>? HubStatusText = null,
    bool RecordDone = false,
    IHarvestFailureSink? FailureSink = null,
    string? RunId = null,
    Func<string?>? SampleLink = null);
