namespace Shopee.Core.Harvest;

/// <summary>
/// Chữ overlay của vòng Harvest (thuần, test được): dòng kết quả sau mỗi SP lưu xong và bảng tổng kết cuối khối. Harvest
/// KHÔNG tải video — chỉ lưu link video trong JSON; overlay nói rõ điều đó. "Đã xong" = đã lưu vào kho trên máy (outbox);
/// Hub nhận sau (worker đẩy nền) — số Hub nằm ở dòng tổng kết.
/// </summary>
public static class HarvestOverlayText
{
    public const int MaxNameLength = 60;

    /// <summary>Tóm tắt SP từ JSON đã bắt; JSON không đọc được → null (overlay bỏ phần chi tiết, không ném).</summary>
    public static ShopeePdpSummary? TrySummarize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return ShopeePdpParser.Parse(json); }
        catch { return null; }
    }

    /// <summary>Tên SP rút gọn một dòng, tối đa <paramref name="max"/> ký tự (kể cả dấu …).</summary>
    public static string ShortName(string? name, int max = MaxNameLength)
    {
        var oneLine = string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (oneLine.Length <= max) return oneLine;
        var cut = Math.Max(0, max - 1);
        if (cut > 0 && char.IsHighSurrogate(oneLine[cut - 1])) cut--;   // không cắt đôi emoji
        return oneLine[..cut].TrimEnd() + "…";
    }

    /// <summary><c>✓ Đã xong dòng N: tên · 3 biến thể · 9 ảnh · 🎬 Có video (đã lưu link)</c>. Không có tóm tắt → chỉ phần đầu.</summary>
    public static string RowSaved(int rowNumber, ShopeePdpSummary? s)
    {
        var head = $"✓ Đã xong dòng {rowNumber}";
        if (s is null) return head;
        var parts = new List<string>();
        var name = ShortName(s.Name);
        if (name.Length > 0) parts.Add(name);
        parts.Add(s.TierCount == 0 ? "không biến thể" : $"{s.ModelCount} biến thể");
        parts.Add($"{s.ImageUrls.Length} ảnh");
        parts.Add(s.VideoUrls.Length > 0 ? "🎬 Có video (đã lưu link)" : "Không có video");
        return $"{head}: {string.Join(" · ", parts)}";
    }

    /// <summary><c>Hub: đã nhận P · app chờ đẩy Q · lỗi R</c> — app cung cấp qua <c>HarvestRunOptions.HubStatusText</c>. P gồm cả SP
    /// extension tự đẩy (GĐ7); Q / R là hàng đợi phía app.</summary>
    public static string HubLine(long received, int pending, int failed) =>
        $"Hub: đã nhận {received} · app chờ đẩy {pending} · lỗi {failed}";

    /// <summary>Bảng tổng kết cuối khối (để nguyên trên trang, không ẩn). <paramref name="hubLine"/> null/rỗng → bỏ dòng Hub.</summary>
    public static string BlockDone(int fromRow, int toRow, string? account, int saved, int skippedOrFailed, string? hubLine)
    {
        var who = string.IsNullOrWhiteSpace(account) ? "" : $" ({account.Trim()})";
        var text = $"✓ Xong dòng {fromRow}–{toRow}{who}\nĐã lấy: {saved} · Bỏ qua/lỗi: {skippedOrFailed}";
        return string.IsNullOrWhiteSpace(hubLine) ? text : $"{text}\n{hubLine.Trim()}";
    }
}
