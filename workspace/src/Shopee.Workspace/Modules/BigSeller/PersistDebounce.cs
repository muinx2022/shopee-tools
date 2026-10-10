using System.Threading;
using Shopee.Core.BigSeller;

namespace Shopee.Workspace.Modules.BigSeller;

/// <summary>
/// Gộp nhiều lần lưu liên tiếp (gõ từng phím trong ô cấu hình BigSeller) thành 1 lần ghi đĩa ~500ms sau khi
/// ngừng sửa.
/// </summary>
internal static class PersistDebounce
{
    private static readonly object _lock = new();
    private static Timer? _timer;

    public static void Schedule()
    {
        lock (_lock)
        {
            _timer ??= new Timer(_ => { try { BigSellerStore.Shared.Save(); } catch { } });
            _timer.Change(500, Timeout.Infinite);
        }
    }
}
