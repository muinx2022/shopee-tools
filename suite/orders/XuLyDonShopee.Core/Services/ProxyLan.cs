namespace XuLyDonShopee.Core.Services;

/// <summary>Một làn đang chiếm chỗ: tài khoản, cấu hình proxy gốc (key Kiot / host:port), địa chỉ đã giải
/// (<c>null</c> = đang đi IP máy).</summary>
public readonly record struct LanDangChay(long AccountId, string? CauHinhGoc, string? ProxyDaGiai);

/// <summary>
/// Luật chỗ proxy / IP máy khi nhiều tài khoản chạy cùng lúc. Đợt này chỉ có hàm thuần
/// <see cref="KiemTraChoLan"/>; phân tích cấu hình và giải Kiot nằm đợt 1 đa-làn.
/// </summary>
public static class ProxyLan
{
    /// <summary>
    /// Kiểm tra tài khoản <paramref name="accountId"/> có được mở làn với
    /// <paramref name="proxyDaGiai"/> hay không, khi các làn <paramref name="dangChay"/> đã chiếm chỗ.
    /// </summary>
    /// <param name="choPhepTatCaQuaIpMay">true = bỏ qua toàn bộ check (IP máy + trùng proxy).</param>
    /// <param name="dangChay">Các làn đang chạy; chính <paramref name="accountId"/> (nếu có) bị bỏ qua.</param>
    /// <param name="cauHinhGoc">Cấu hình gốc trên tài khoản (key Kiot hoặc host:port); rỗng = không so key.</param>
    /// <param name="proxyDaGiai"><c>null</c> = đi IP máy; khác null = địa chỉ proxy đã giải.</param>
    public static (bool Ok, string? LyDo) KiemTraChoLan(
        bool choPhepTatCaQuaIpMay,
        IReadOnlyList<LanDangChay> dangChay,
        long accountId,
        string? cauHinhGoc,
        string? proxyDaGiai)
    {
        if (choPhepTatCaQuaIpMay)
        {
            return (true, null);
        }

        foreach (var lan in dangChay)
        {
            if (lan.AccountId == accountId)
            {
                continue;
            }

            if (proxyDaGiai is null && lan.ProxyDaGiai is null)
            {
                return (false, $"IP máy đang do tài khoản {lan.AccountId} dùng");
            }

            if (proxyDaGiai is null)
            {
                continue;
            }

            if (lan.ProxyDaGiai == proxyDaGiai)
            {
                return (false, $"proxy trùng với tài khoản {lan.AccountId}");
            }

            var keyMinh = cauHinhGoc?.Trim() ?? string.Empty;
            var keyLan = lan.CauHinhGoc?.Trim() ?? string.Empty;
            if (keyMinh.Length > 0
                && keyLan.Length > 0
                && string.Equals(keyMinh, keyLan, StringComparison.Ordinal))
            {
                return (false, $"proxy trùng với tài khoản {lan.AccountId}");
            }
        }

        return (true, null);
    }
}
