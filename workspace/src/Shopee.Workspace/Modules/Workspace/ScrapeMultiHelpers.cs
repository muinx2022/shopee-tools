using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Shopee.Core.BigSeller;
using Shopee.Core.Scrape;

namespace Shopee.Workspace.Modules.Workspace;

/// <summary>Tập tk BigSeller đã tự đăng nhập THẤT BẠI trong 1 lượt chạy Scrape Multi (thread-safe: fan-out
/// chạy trên nhiều luồng nền). Tk trong tập này không bị đăng nhập lại (tránh khoá tk khi BigSeller đòi OTP).</summary>
internal sealed class LoginFailureTracker
{
    private readonly ConcurrentDictionary<string, bool> _failed = new(StringComparer.OrdinalIgnoreCase);

    public void Clear() => _failed.Clear();

    public void MarkFailed(string accountId) => _failed.TryAdd(accountId, false);

    public bool IsFailed(string accountId) => _failed.ContainsKey(accountId);

    /// <summary>true đúng 1 lần cho mỗi tk đã lỗi (để log 1 lần/lượt chạy, tránh spam); false nếu tk chưa lỗi hoặc đã log rồi.</summary>
    public bool TryLogOnce(string accountId)
    {
        while (_failed.TryGetValue(accountId, out var logged))
        {
            if (logged) return false;
            if (_failed.TryUpdate(accountId, true, false)) return true;
        }
        return false;
    }
}

internal static class ScrapeMultiSelection
{
    /// <summary>Quyết định trạng thái tick của 1 tk khi dựng lại danh sách: nếu còn "selection đã lưu chờ áp"
    /// (pending) thì theo pending; ngược lại giữ tick cũ (nếu có); tk mới hoàn toàn mặc định chọn.</summary>
    public static bool Resolve(string accountId, ISet<string>? pending, IReadOnlyDictionary<string, bool> previous)
    {
        if (pending is not null) return pending.Contains(accountId);
        return previous.TryGetValue(accountId, out var prev) ? prev : true;
    }
}

/// <summary>Việc phải làm tiếp với 1 dòng sync sau khi hỏi server về phiên BigSeller của tk đích.</summary>
public enum ScrapeMultiSyncAction
{
    /// <summary>Phiên sống → sync lại dòng này.</summary>
    Repost,
    /// <summary>Phiên bị từ chối (hoặc server đã báo 2001/401006) → đăng nhập lại.</summary>
    Login,
    /// <summary>Không đăng nhập, KHÔNG MarkFailed — dòng đi vào sổ lỗi với lỗi thật, các dòng sau chạy bình thường.</summary>
    Book,
    /// <summary>Đăng nhập không cứu được → tk bị đánh dấu lỗi cho hết lượt chạy (dòng vào sổ lỗi).</summary>
    MarkFailed,
}

/// <summary>Kết quả kiểm tra phiên lúc khởi động 1 tk.</summary>
internal enum SessionEnsureResult
{
    Ready,
    /// <summary>Không có/không dùng được phiên và tự đăng nhập thất bại (tk đã MarkFailed).</summary>
    Failed,
    /// <summary>Không hỏi được BigSeller (rớt mạng/timeout) — KHÔNG phải mất phiên, không đăng nhập, không MarkFailed.</summary>
    NetworkUnreachable,
}

/// <summary>Luật quyết định (thuần) của đường sync Scrape Multi khi PostCrawl lỗi: rớt mạng KHÔNG được coi là mất
/// phiên (đăng nhập lúc đó cũng hỏng theo → tk bị MarkFailed oan cả lượt).</summary>
internal static class ScrapeMultiSessionDecision
{
    /// <summary>Sau 1 lần probe phiên. <paramref name="isSessionIssue"/> = chính lỗi sync đã là 2001/401006…
    /// <paramref name="inLock"/> = probe làm TRONG khoá đăng nhập (luồng khác có thể vừa đăng nhập xong).
    /// Alive: trước khoá → Book (phiên ổn, lỗi là chuyện khác); trong khoá → Repost.
    /// Rejected → Login. Unreachable: server đã báo lỗi phiên → vẫn Login; không thì Book (không đăng nhập).</summary>
    public static ScrapeMultiSyncAction AfterProbe(bool isSessionIssue, BigSellerSessionProbe probe, bool inLock) => probe switch
    {
        BigSellerSessionProbe.Alive => inLock ? ScrapeMultiSyncAction.Repost : ScrapeMultiSyncAction.Book,
        BigSellerSessionProbe.Rejected => ScrapeMultiSyncAction.Login,
        _ => isSessionIssue ? ScrapeMultiSyncAction.Login : ScrapeMultiSyncAction.Book,
    };

    /// <summary>Sau bước đăng nhập. <paramref name="loginSuccess"/> = outcome Success; <paramref name="probe"/> = lần
    /// probe xác minh (Success) hoặc probe lại 1 lần (không Success). Theo probe, bất kể đăng nhập ra sao:
    /// Alive → Repost (phiên dùng được nên MarkFailed là sai); Unreachable → Book (không MarkFailed);
    /// Rejected → MarkFailed.</summary>
    public static ScrapeMultiSyncAction AfterLogin(bool loginSuccess, BigSellerSessionProbe probe) => probe switch
    {
        BigSellerSessionProbe.Alive => ScrapeMultiSyncAction.Repost,
        BigSellerSessionProbe.Unreachable => ScrapeMultiSyncAction.Book,
        _ => ScrapeMultiSyncAction.MarkFailed,
    };
}

/// <summary>1 URL cần cào lại JSON + các mục sổ lỗi (mỗi mục = 1 tk + 1 dòng) đang chờ JSON đó.</summary>
internal sealed record RescrapeUrlGroup(string Url, IReadOnlyList<FailedSyncItem> Items)
{
    /// <summary>Các tk có mục trong nhóm (không trùng) — ĐÚNG những tk được sync lại, không tk nào khác.</summary>
    public IReadOnlyList<string> AccountIds =>
        Items.Select(i => i.AccountId).Distinct(StringComparer.Ordinal).ToList();
}

/// <summary>Luật thuần của "Cào lại SP lỗi": mục sổ lỗi có JsonData rỗng (lúc cào không lấy được JSON nên gửi "{}"
/// → BigSeller luôn từ chối) cần lấy lại JSON từ Shopee rồi mới sync được.</summary>
internal static class ScrapeMultiRescrape
{
    /// <summary>JsonData rỗng = null/trắng/"{}" (bỏ khoảng trắng). Mục này gửi lại cũng vô ích.</summary>
    public static bool IsEmptyJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        return json.Where(c => !char.IsWhiteSpace(c)).SequenceEqual("{}");
    }

    public static IReadOnlyList<FailedSyncItem> FilterEmpty(IEnumerable<FailedSyncItem> items) =>
        items.Where(i => IsEmptyJson(i.JsonData)).ToList();

    /// <summary>Gom mục rỗng theo URL (không phân biệt hoa/thường, bỏ khoảng trắng đầu/cuối) để mỗi URL chỉ lấy
    /// JSON 1 lần; giữ thứ tự xuất hiện đầu tiên. Mục không có URL bị bỏ (không có gì để mở).</summary>
    public static IReadOnlyList<RescrapeUrlGroup> GroupByUrl(IEnumerable<FailedSyncItem> items)
    {
        var order = new List<string>();
        var map = new Dictionary<string, (string Url, List<FailedSyncItem> Items)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in FilterEmpty(items))
        {
            var url = (item.Url ?? "").Trim();
            if (url.Length == 0) continue;
            if (!map.TryGetValue(url, out var g))
            {
                g = (url, new List<FailedSyncItem>());
                map[url] = g;
                order.Add(url);
            }
            g.Items.Add(item);
        }
        return order.Select(u => new RescrapeUrlGroup(map[u].Url, map[u].Items)).ToList();
    }
}
