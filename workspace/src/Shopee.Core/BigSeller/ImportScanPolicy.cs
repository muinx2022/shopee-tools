namespace Shopee.Core.BigSeller;

/// <summary>
/// Quyết định thuần của lượt Import to Store (BigSeller) để test được, không dính Playwright:
/// có quét thêm vòng không, trang crawl đã "xác nhận" (DOM khớp response pageList.json mới) chưa, và khi nào bỏ phần còn lại của tab.
/// </summary>
public static class ImportScanPolicy
{
    /// <summary>Số vòng quét tối đa (mỗi vòng = quét cả 2 tab từ trang 1).</summary>
    public const int MaxPasses = 3;

    /// <summary>Số trang LIÊN TIẾP vừa chưa xác nhận vừa không có SP khớp thì bỏ phần còn lại của tab (phanh).</summary>
    public const int MaxUnconfirmedNoMatchPages = 3;

    /// <summary>Có sang vòng quét mới không. Chỉ khi: chưa đủ, còn dưới <paramref name="maxPasses"/>, VÀ vòng vừa rồi
    /// có lô đã gửi (danh sách đã đổi, SP có thể dồn trang) HOẶC có tab/trang CHƯA XÁC NHẬN (có thể sót).
    /// Vòng không gửi gì và không có gì chưa xác nhận → quét lại cũng không ra thêm gì → dừng.</summary>
    public static bool ShouldRescan(int pass, int maxPasses, bool allImported, bool sentThisPass, bool hasUnconfirmed) =>
        !allImported && pass < maxPasses && (sentThisPass || hasUnconfirmed);

    /// <summary>Trang crawl đã được XÁC NHẬN chưa: có response pageList.json MỚI hơn mốc
    /// (<paramref name="newerResponse"/>) đã đọc được (<paramref name="responseKeys"/> ≠ null) VÀ ảnh trên DOM thuộc đúng
    /// response đó. Response rỗng thì DOM phải không còn dòng nào. Chưa đủ điều kiện → chưa được kết luận
    /// "0 khớp / đã quét sạch" (DOM có thể còn dòng trang cũ hoặc đang tải). Xác nhận KHÔNG chặn việc tick SP khớp:
    /// tick theo map ảnh→id vốn đã đúng SP.</summary>
    public static bool IsPageConfirmed(
        bool newerResponse, IReadOnlyCollection<string>? responseKeys, int domRowCount, IReadOnlyCollection<string> domImageKeys)
    {
        if (!newerResponse || responseKeys is null) return false;
        if (responseKeys.Count == 0) return domRowCount == 0;
        if (domImageKeys.Count == 0) return false;
        foreach (var key in domImageKeys)
            if (!responseKeys.Contains(key)) return false;
        return true;
    }

    /// <summary>Trang THẬT SỰ không có dữ liệu mới: không có response mới hơn mốc, HOẶC response không đọc được (null),
    /// HOẶC không khoá ảnh DOM nào thuộc response. Có giao nhau với response (chỉ lạ vài ảnh) thì KHÔNG phải — đó là
    /// trang có dữ liệu nhưng chưa xác nhận tròn vẹn.</summary>
    public static bool HasNoNewPageData(
        bool newerResponse, IReadOnlyCollection<string>? responseKeys, IReadOnlyCollection<string> domImageKeys)
    {
        if (!newerResponse || responseKeys is null) return true;
        foreach (var key in domImageKeys)
            if (responseKeys.Contains(key)) return false;
        return true;
    }

    /// <summary>Số trang liên tiếp "chưa xác nhận, không có SP khớp VÀ không có dữ liệu trang mới" sau trang vừa quét:
    /// trang đã xác nhận, có SP khớp, hoặc có dữ liệu trang mới (<paramref name="noNewPageData"/> = false, vd chỉ lạ vài ảnh)
    /// thì về 0; ngược lại +1.</summary>
    public static int NextUnconfirmedNoMatchStreak(int streak, bool pageConfirmed, bool hadMatch, bool noNewPageData) =>
        pageConfirmed || hadMatch || !noNewPageData ? 0 : streak + 1;
    /// <summary>Phanh: đủ <see cref="MaxUnconfirmedNoMatchPages"/> trang liên tiếp chưa xác nhận mà không có SP khớp →
    /// bỏ phần còn lại của tab (vòng sau thử lại), khỏi mất ~17s/trang cho cả tab.</summary>
    public static bool ShouldAbandonTab(int unconfirmedNoMatchStreak) =>
        unconfirmedNoMatchStreak >= MaxUnconfirmedNoMatchPages;

    /// <summary>Mô tả vì sao trang chưa xác nhận (để log): có response mới không, số khoá response (null = đọc
    /// không được), số dòng, số khoá ảnh DOM, tối đa <paramref name="maxForeign"/> khoá DOM không thuộc response.</summary>
    public static string DescribeUnconfirmed(
        bool newerResponse, IReadOnlyCollection<string>? responseKeys, int domRowCount,
        IReadOnlyCollection<string> domImageKeys, int maxForeign = 5)
    {
        var foreign = responseKeys is null
            ? domImageKeys.Take(maxForeign).ToList()
            : domImageKeys.Where(k => !responseKeys.Contains(k)).Take(maxForeign).ToList();
        return $"newer={newerResponse}, khoá response={(responseKeys is null ? "null" : responseKeys.Count.ToString())}, " +
               $"dòng DOM={domRowCount}, khoá ảnh DOM={domImageKeys.Count}, khoá lạ (≤{maxForeign})=[{string.Join(", ", foreign)}]";
    }
}
