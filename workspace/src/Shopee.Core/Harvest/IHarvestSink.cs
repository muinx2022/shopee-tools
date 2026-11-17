namespace Shopee.Core.Harvest;

/// <summary>
/// Một sản phẩm vừa lấy được JSON <c>get_pc</c> ở chế độ Harvest — đơn vị engine giao cho
/// <see cref="IHarvestSink"/>. Engine chỉ dựng bản ghi; lưu ở đâu (outbox SQLite, đẩy Hub) là việc của sink (GĐ4).
/// </summary>
/// <param name="AccountId">Tài khoản BigSeller đích (khoá <c>product_rows.account_id</c> trên Hub).</param>
/// <param name="ShopId">Shop BigSeller đích của lượt chạy (rỗng nếu caller không truyền).</param>
/// <param name="Sheet">Sheet nguồn link.</param>
/// <param name="RowNo">Số dòng nguồn trong sheet.</param>
/// <param name="Url">Link ĐÃ làm sạch đã gửi cho extension (<see cref="HarvestLinks.CleanProductUrl"/>).</param>
/// <param name="SourceLink">Link GỐC của dòng nguồn (sheet / Hub <c>product_rows</c>), CHƯA làm sạch. GĐ4: dòng nguồn trên
/// Hub (<c>harvested_product_sources.url</c>) phải lưu GIÁ TRỊ NÀY, không phải <paramref name="Url"/> — bước up đối chiếu
/// nó với link HIỆN TẠI của dòng (dòng có thể bị import replace / sửa link sau khi cào).</param>
/// <param name="ItemId">itemId Shopee extension đã khớp với JSON.</param>
/// <param name="ShopeeShopId">shopId Shopee extension trả (null nếu không có).</param>
/// <param name="Json">JSON <c>get_pc</c> nguyên văn (extension đã cắt <c>data.account</c> + <c>delivery_address</c>).</param>
/// <param name="Source"><c>"hook"</c> | <c>"hook-reload"</c>.</param>
/// <param name="HarvestedAt">Giờ cào: <c>capturedAt</c> của extension nếu có, không thì giờ máy lúc engine nhận kết quả
/// bước (không phải giờ ghi outbox) — xem <see cref="HarvestStepResult.HarvestedAtOr"/>.</param>
/// <param name="DoneTarget">Target của tập "SP đã lấy xong" (<see cref="HarvestDoneTarget.Of"/>): sink lưu xong thì ghi khoá SP
/// (<see cref="HarvestItemKey.ForSaved"/>) vào tập này CÙNG lần lưu. null → không ghi (caller không theo dõi tiến độ).</param>
public sealed record HarvestedItem(
    string AccountId,
    string ShopId,
    string Sheet,
    int RowNo,
    string Url,
    string SourceLink,
    long ItemId,
    long? ShopeeShopId,
    string Json,
    string Source,
    DateTimeOffset HarvestedAt,
    string? DoneTarget = null);

/// <summary>Lỗi lưu dữ liệu (sink) là lỗi HẠ TẦNG của máy (đĩa / outbox), không phải lỗi tài khoản Shopee: engine dừng
/// CẢ job như ca key proxy chết (nhả tài khoản nguyên vẹn, không cooldown, không tính kẹt, không bỏ dòng). Thông điệp
/// mang tiền tố <see cref="Prefix"/> để <c>ScrapeFailurePolicy</c> nhận ra.</summary>
public static class HarvestSinkFailure
{
    public const string Prefix = "Lưu dữ liệu lỗi: ";

    public static string Message(int rowNumber, Exception error) =>
        $"{Prefix}{error.Message} (dòng {rowNumber} chưa tính xong, chạy lại sau khi sửa chỗ lưu).";

    /// <summary>Thông điệp khi sink chưa sẵn sàng lúc bắt đầu job (<see cref="IHarvestSink.EnsureReadyAsync"/> ném).</summary>
    public static string NotReadyMessage(Exception error) =>
        $"{Prefix}{error.Message} (chưa mở Brave, chưa lấy tài khoản — sửa chỗ lưu rồi chạy lại).";

    public static bool Is(string? message) =>
        message is not null && message.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Độ dài tối đa của lý do gửi lên Hub (khớp <c>ShortHubReason</c> của vòng scrape).</summary>
    public const int MaxHubReasonLength = 120;

    /// <summary>Lý do gọn để báo 'failed' lên Hub: một dòng, tối đa <see cref="MaxHubReasonLength"/> ký tự.</summary>
    public static string ShortReason(string reason)
    {
        var oneLine = string.Join(' ', reason.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return oneLine.Length <= MaxHubReasonLength ? oneLine : oneLine[..MaxHubReasonLength];
    }
}

/// <summary>
/// Nơi engine Harvest giao dữ liệu sản phẩm. Hợp đồng:
/// <list type="bullet">
/// <item><see cref="EnsureReadyAsync"/> được gọi MỘT lần đầu job, TRƯỚC khi mở Brave/lấy tài khoản: ném lỗi = dừng job
/// ngay (<see cref="HarvestSinkFailure"/>), không tốn IP/tài khoản.</item>
/// <item><see cref="SaveAsync"/> trả về bình thường = đã lưu BỀN (dòng mới được tính xong, tiến độ mới được ghi); NÉM
/// lỗi = chưa lưu, engine KHÔNG tính dòng xong và DỪNG job — dòng được làm lại ở lượt Tiếp tục.</item>
/// <item>Vì mọi lần ném đều dừng cả job, sink TỰ thử lại lỗi THOÁNG QUA (vd SQLite <c>BUSY</c>/<c>LOCKED</c> khi nhiều cửa
/// sổ ghi cùng lúc) trong một giới hạn thời gian, chỉ ném lỗi KÉO DÀI (đĩa đầy, file hỏng, không mở được DB…).</item>
/// <item>Gọi tuần tự trong một cửa sổ Brave, nhưng nhiều cửa sổ gọi song song.</item>
/// </list>
/// </summary>
public interface IHarvestSink
{
    Task EnsureReadyAsync(CancellationToken cancellationToken);

    Task SaveAsync(HarvestedItem item, CancellationToken cancellationToken);
}

/// <summary>Nơi engine Harvest ghi SP lỗi VĨNH VIỄN (SP không tồn tại / không phải trang SP / JSON bị từ chối vì nội dung) để
/// lượt sau không lên lịch lại (<see cref="HarvestDoneRebuild"/>). Lỗi TẠM (hết giờ, captcha, mạng, proxy, khoá workbook, mã
/// pdpError chưa xác nhận) KHÔNG ghi gì. Ghi hỏng không được làm hỏng dòng — engine nuốt lỗi và log (lượt sau thử lại).</summary>
public interface IHarvestFailureSink
{
    /// <param name="runId">Định danh LƯỢT chạy — mỗi lượt chỉ tính một lần thấy lỗi (cần nhiều lượt mới bỏ qua hẳn).</param>
    Task RecordPermanentFailureAsync(string target, IReadOnlyList<string> itemKeys, string reason, string runId,
        CancellationToken cancellationToken);
}
