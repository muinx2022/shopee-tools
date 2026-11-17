using Shopee.Core.Scrape;

namespace Shopee.Core.Harvest;

/// <summary>Kết quả hỏi Hub "SP nào đã có dữ liệu còn hạn". <see cref="Known"/> null = KHÔNG hỏi được (chưa nối Hub, mất mạng,
/// 5xx…) — caller chỉ dựa vào tập đã xong cục bộ, KHÔNG được coi là "Hub chưa có gì"; <see cref="Error"/> = lý do để log.</summary>
public sealed record HarvestHubLookup(IReadOnlySet<long>? Known, string? Error)
{
    public static readonly HarvestHubLookup Skipped = new(new HashSet<long>(), null);
}

/// <summary>Hàm hỏi Hub (vd <c>HubClient.GetHarvestedKnownAsync</c>): itemId → mốc lấy gần nhất của các id CÒN HẠN. Ném = không hỏi
/// được.</summary>
public delegate Task<IReadOnlyDictionary<long, DateTimeOffset>> HarvestKnownQuery(
    IReadOnlyCollection<long> itemIds, int maxAgeDays, CancellationToken cancellationToken);

/// <summary>
/// Lên lịch một sheet của app Harvest: dòng nào đã có (tập đã xong cục bộ còn hạn, hoặc Hub báo đã có còn hạn), dòng nào bỏ qua
/// (lỗi vĩnh viễn đã xác nhận), dòng nào còn phải lấy. Tập đã xong / lỗi là TOÀN CỤC theo khoá SP: SP đã lưu ở sheet / file khác
/// cũng tính là đã có.
/// </summary>
public static class HarvestPlanning
{
    /// <summary>Số ngày mặc định của "lấy lại nếu dữ liệu cũ hơn N ngày".</summary>
    public const int DefaultMaxAgeDays = 30;

    /// <summary>Kẹp N ngày về [1 .. <see cref="Coordination.HarvestedKnownLimits.MaxAgeDays"/>] (0 / âm → mặc định).</summary>
    public static int ClampMaxAgeDays(int days) =>
        days <= 0 ? DefaultMaxAgeDays : Math.Min(days, Coordination.HarvestedKnownLimits.MaxAgeDays);

    /// <summary>Mốc cũ nhất còn tính là "đã có" của tập đã xong cục bộ.</summary>
    public static DateTimeOffset DoneSince(DateTimeOffset now, int maxAgeDays) => now.AddDays(-ClampMaxAgeDays(maxAgeDays));

    /// <summary>Khoá SP của các dòng (khử trùng) — để Reset / "Cào lại dòng đã bỏ" gỡ đúng khoá của sheet.</summary>
    public static IReadOnlyList<string> KeysOf(IEnumerable<ScrapeLinkItem> items) =>
        items.Select(i => HarvestItemKey.FromLink(i.Link)).OfType<string>().Distinct(StringComparer.Ordinal).ToList();

    /// <summary>itemId của các dòng có khoá <c>id:</c> (khử trùng). Dòng có trong <paramref name="done"/> không cần hỏi.</summary>
    public static IReadOnlyList<long> ItemIdsToAsk(IEnumerable<ScrapeLinkItem> items, IReadOnlySet<string>? done = null) =>
        items.Select(i => HarvestItemKey.FromLink(i.Link))
            .Where(k => k is not null && (done is null || !done.Contains(k)))
            .Select(HarvestSource.ItemIdOfKey).OfType<long>().Distinct().ToList();

    /// <summary>Hỏi Hub các itemId đã có dữ liệu còn hạn. Không có hàm hỏi / lỗi gì (trừ huỷ của caller) → <see cref="HarvestHubLookup.Known"/>
    /// null kèm lý do, KHÔNG ném — Hub sập không được chặn lượt chạy.</summary>
    public static async Task<HarvestHubLookup> AskHubAsync(HarvestKnownQuery? query, IReadOnlyCollection<long> itemIds,
        int maxAgeDays, CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0) return HarvestHubLookup.Skipped;
        if (query is null) return new HarvestHubLookup(null, "chưa nối Hub");
        try
        {
            var known = await query(itemIds, ClampMaxAgeDays(maxAgeDays), cancellationToken).ConfigureAwait(false);
            return new HarvestHubLookup(known.Keys.ToHashSet(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new HarvestHubLookup(null, ex.Message); }
    }

    /// <summary>Dựng tiến độ của một sheet (dải đọc từ <see cref="HarvestWorkbookReader"/>) từ tập đã xong + lỗi + Hub.</summary>
    public static HarvestDoneRebuildResult Build(HarvestWorkbookRange range, IReadOnlySet<string> done, IReadOnlySet<string>? failed,
        IReadOnlySet<long>? hubKnown) =>
        HarvestDoneRebuild.Build(range.DenseStart, range.DenseEnd, range.Links?.Items ?? [], done, failed, hubKnown);
}
