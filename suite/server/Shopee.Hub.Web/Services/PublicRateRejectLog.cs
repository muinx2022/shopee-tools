using System.Collections.Concurrent;

namespace Shopee.Hub.Web.Services;

/// <summary>
/// Ghi log khi link ảnh công khai <c>/api/v/…</c> bị giới hạn tần suất (429) — để biết NGAY nếu chặn nhầm Shopee — nhưng
/// GỘP: tối đa 1 dòng / IP / phút (dòng ghi kèm số lần bị chặn từ lần ghi trước), không để một IP dồn dập làm tràn log.
/// </summary>
public sealed class PublicRateRejectLog
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(1);
    private const int PruneAbove = 10_000;
    private readonly ConcurrentDictionary<string, Entry> _byIp = new();
    private readonly ILogger<PublicRateRejectLog> _log;

    private sealed class Entry
    {
        public DateTimeOffset LastLogged = DateTimeOffset.MinValue;
        public long Suppressed;
    }

    /// <summary>Đồng hồ — TEST thay để giả thời gian trôi.</summary>
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public PublicRateRejectLog(ILogger<PublicRateRejectLog> log) => _log = log;

    /// <summary>Ghi nhận 1 lần chặn của <paramref name="ip"/>; ghi log nếu IP này chưa có dòng nào trong 1 phút qua.</summary>
    public void Rejected(string ip, string path)
    {
        var now = Clock();
        if (_byIp.Count > PruneAbove)
            foreach (var kv in _byIp)
                if (now - kv.Value.LastLogged > Every) _byIp.TryRemove(kv);
        var e = _byIp.GetOrAdd(ip, _ => new Entry());
        long gop;
        lock (e)
        {
            if (now - e.LastLogged < Every) { e.Suppressed++; return; }
            gop = e.Suppressed;
            e.Suppressed = 0;
            e.LastLogged = now;
        }
        _log.LogWarning("source-images: link ảnh công khai bị giới hạn tần suất (429) từ IP {Ip}, path {Path}; {Gop} lần chặn "
            + "khác của IP này đã gộp từ dòng trước. Nếu là Shopee thì nâng Hub:SourceImages:PublicBurst / PublicRatePerMinute.",
            ip, path, gop);
    }
}
