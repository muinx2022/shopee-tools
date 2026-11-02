using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

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
