using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Shopee.Core.Coordination;

namespace Shopee.Hub.Web.Services;

/// <summary>
/// Kho ảnh CÔNG KHAI có hạn: Shopee (Đăng Hàng Loạt) kéo ảnh qua URL KHÔNG kèm token, nên kho <c>/files</c> (cần token)
/// không dùng được. Lưu kiểu kho video: blob trên đĩa trong <c>DataDir/media/</c> (thư mục RIÊNG, không vào manifest
/// <c>files</c> nên không ai liệt kê được, cũng không vào bản backup đêm). KHÔNG có bảng DB: tên file mang sẵn mọi thứ —
/// <c>&lt;hạn unix giây&gt;-&lt;32 hex ngẫu nhiên 128 bit&gt;.&lt;jpg|png&gt;</c>. Hạn nằm trong tên nên đổi cấu hình hạn
/// không kéo dài/rút ngắn ảnh cũ, và sửa số hạn trên URL chỉ ra tên không tồn tại → 404.
/// </summary>
public sealed class PublicMediaStore
{
    private static readonly Regex NameRule = new(
        @"^(?<exp>[0-9]{1,12})-(?<tok>[0-9a-f]{32})\.(?<ext>jpg|png)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly string _dir;
    private readonly TimeSpan _retention;
    private readonly long _maxTotalBytes;
    private readonly long _minFreeBytes;
    private readonly string _publicBaseUrl;
    // Kiểm trần + ghi phải nguyên tử: hai upload song song cùng lọt trần là vượt trần.
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Stats cho màn "Dung lượng Hub" (GĐ9): đếm cả thư mục mỗi lần gọi là chậm khi kho có vài chục nghìn file → nhớ
    // 30 giây; mọi lần ghi/xoá qua store này đều huỷ bản nhớ (job dọn chỉ huỷ khi có xoá). Chỉ đọc/ghi trong _gate.
    private (long Count, long Bytes, long At)? _stats;
    private static readonly long StatsTtlMs = 30_000;

    /// <summary>Đồng hồ — TEST thay để giả thời gian trôi.</summary>
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>Dung lượng còn trống (byte) của ổ chứa <see cref="Dir"/> — TEST thay để giả đĩa đầy.</summary>
    internal Func<long> FreeBytes { get; set; }

    public string Dir => _dir;

    /// <summary>Hub đã cấu hình base URL công khai chưa. Chưa thì KHÔNG nhận upload: ảnh lên mà không có URL tuyệt đối
    /// thì bước đăng phải tự ghép URL từ base URL của client (có thể là địa chỉ LAN/loopback Shopee không kéo được).</summary>
    public bool HasPublicBaseUrl => _publicBaseUrl.Length > 0;

    /// <summary>Base URL công khai đã chuẩn hoá (không '/' cuối); rỗng = chưa cấu hình. Link ảnh đổi MD5 (GĐ9) dùng chung.</summary>
    public string PublicBaseUrl => _publicBaseUrl;

    public PublicMediaStore(HubOptions opts, ILogger<PublicMediaStore>? log = null)
    {
        _dir = Path.Combine(opts.DataDir, "media");
        _retention = TimeSpan.FromDays(Math.Max(1, opts.MediaRetentionDays));
        _maxTotalBytes = Math.Max(0, opts.MediaMaxTotalBytes);
        _minFreeBytes = Math.Max(0, opts.MediaMinFreeBytes);
        _publicBaseUrl = NormalizePublicBaseUrl(opts.PublicBaseUrl) ?? "";
        // Program.cs ép dựng store ngay lúc khởi động → cảnh báo này ra ĐÚNG 1 lần mỗi lần chạy hub.
        if (string.IsNullOrWhiteSpace(opts.PublicBaseUrl))
            log?.LogWarning("media: chưa cấu hình Hub:PublicBaseUrl / HUB_PUBLIC_BASE_URL → upload ảnh công khai trả 503");
        else if (_publicBaseUrl.Length == 0)
            log?.LogWarning("media: Hub:PublicBaseUrl '{Url}' sai dạng (cần https://host[/path], không query/fragment) → coi như "
                            + "chưa cấu hình, upload ảnh công khai trả 503", opts.PublicBaseUrl);
        Directory.CreateDirectory(_dir);
        // Windows: DriveInfo cần tên ổ gốc. Linux: statvfs trên CHÍNH thư mục → đúng mount chứa media/ (không phải "/").
        var drive = OperatingSystem.IsWindows() ? Path.GetPathRoot(Path.GetFullPath(_dir))! : _dir;
        FreeBytes = () => new DriveInfo(drive).AvailableFreeSpace;
    }

    /// <summary>Chuẩn hoá base URL công khai: URI tuyệt đối scheme https, có host, không user-info / query / fragment; cắt
    /// "/" cuối. Sai dạng (<c>http://…</c>, thiếu scheme, rác) hoặc rỗng → null = coi như chưa cấu hình. Không kiểm host
    /// có phải IP riêng: cấu hình do admin đặt; client tự kiểm URL nhận về (<c>HubClient.IsPublicHttpsUrl</c>).</summary>
    internal static string? NormalizePublicBaseUrl(string? raw)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0 || !Uri.TryCreate(s, UriKind.Absolute, out var u)) return null;
        if (u.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(u.Host) || u.UserInfo.Length > 0
            || u.Query.Length > 0 || u.Fragment.Length > 0 || s.Contains('?') || s.Contains('#')) return null;
        return u.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }

    /// <summary>Kiểu ảnh theo MAGIC BYTES (không tin Content-Type client gửi): JPEG <c>FF D8 FF</c>, PNG
    /// <c>89 50 4E 47 0D 0A 1A 0A</c>. Khác → null.</summary>
    internal static string? SniffExt(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return "jpg";
        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "png";
        return null;
    }

    internal static string ContentTypeOf(string ext) => ext == "png" ? "image/png" : "image/jpeg";

    /// <summary>Tách tên hợp lệ → (hạn, đuôi). Tên sai luật (kể cả có <c>/</c>, <c>..</c>, chữ hoa) → null.</summary>
    internal static (DateTimeOffset Expires, string Ext)? ParseName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var m = NameRule.Match(name);
        if (!m.Success) return null;
        if (!long.TryParse(m.Groups["exp"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var exp)) return null;
        try { return (DateTimeOffset.FromUnixTimeSeconds(exp), m.Groups["ext"].Value); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    public enum UploadStatus { Ok, Empty, TooLarge, UnsupportedType, QuotaExceeded, DiskLow, PublicBaseUrlMissing }

    /// <summary><see cref="UsedBytes"/> có nghĩa khi QuotaExceeded; <see cref="FreeBytes"/> khi DiskLow (-1 = không đo
    /// được).</summary>
    public sealed record UploadResult(UploadStatus Status, PublicMediaUploadResponse? Response, long UsedBytes = 0, long FreeBytes = 0);

    /// <summary>Lưu 1 ảnh. Thứ tự: base URL công khai → cỡ → magic bytes → (trong khoá) dọn ảnh ĐÃ hết hạn → trần tổng →
    /// đĩa trống. Ảnh còn hạn không bao giờ bị xoá để nhường chỗ. Không đo được đĩa trống → từ chối (DiskLow, -1): thà
    /// dừng lượt đăng còn hơn ghi tới đầy đĩa làm chết cả hub.db.</summary>
    public async Task<UploadResult> SaveAsync(byte[] data, CancellationToken ct)
    {
        if (!HasPublicBaseUrl) return new UploadResult(UploadStatus.PublicBaseUrlMissing, null);
        if (data.Length == 0) return new UploadResult(UploadStatus.Empty, null);
        if (data.Length > PublicMediaLimits.MaxBytes) return new UploadResult(UploadStatus.TooLarge, null);
        var ext = SniffExt(data);
        if (ext is null) return new UploadResult(UploadStatus.UnsupportedType, null);

        await _gate.WaitAsync(ct);
        try
        {
            var now = Clock();
            SweepExpiredLocked(now);
            var used = UsedBytesLocked();
            if (used + data.Length > _maxTotalBytes) return new UploadResult(UploadStatus.QuotaExceeded, null, used);
            long free;
            try { free = FreeBytes(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { free = -1; }
            if (free - data.Length < _minFreeBytes)   // free = -1 (không đo được) luôn rơi vào nhánh này
                return new UploadResult(UploadStatus.DiskLow, null, used, free);

            // Làm tròn LÊN tới giây: hạn trong tên không bao giờ sớm hơn mốc báo client.
            var expires = DateTimeOffset.FromUnixTimeSeconds((now + _retention).ToUnixTimeSeconds() + 1);
            var name = $"{expires.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}-"
                       + $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}.{ext}";
            var path = Path.Combine(_dir, name);
            var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(tmp, data, ct);
            File.Move(tmp, path, overwrite: false);
            _stats = null;
            var rel = HubRoutes.PublicMedia + "/" + name;
            return new UploadResult(UploadStatus.Ok, new PublicMediaUploadResponse(rel, _publicBaseUrl + rel, expires));
        }
        finally { _gate.Release(); }
    }

    /// <summary>Đường dẫn vật lý + Content-Type của ảnh còn hạn; tên sai / hết hạn / không có → null.</summary>
    public (string Path, string ContentType, DateTimeOffset Expires)? Find(string? name)
    {
        if (ParseName(name) is not { } p) return null;
        if (p.Expires <= Clock()) return null;
        var path = Path.Combine(_dir, name!);
        return File.Exists(path) ? (path, ContentTypeOf(p.Ext), p.Expires) : null;
    }

    /// <summary>Xoá 1 ảnh theo tên (kể cả đã hết hạn mà job dọn chưa tới). true = đã xoá; tên sai luật / không có →
    /// false.</summary>
    public async Task<bool> DeleteAsync(string? name, CancellationToken ct)
    {
        if (ParseName(name) is null) return false;
        await _gate.WaitAsync(ct);
        try
        {
            var path = Path.Combine(_dir, name!);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            _stats = null;
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Job dọn: xoá ảnh đã hết hạn (theo hạn trong tên) + file tạm mồ côi &gt; 1 giờ. Trả số ảnh đã xoá.</summary>
    public async Task<int> SweepExpiredAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { return SweepExpiredLocked(Clock()); }
        finally { _gate.Release(); }
    }

    private int SweepExpiredLocked(DateTimeOffset now)
    {
        var n = 0;
        foreach (var path in Directory.EnumerateFiles(_dir))
        {
            var file = Path.GetFileName(path);
            try
            {
                if (ParseName(file) is { } p)
                {
                    if (p.Expires <= now) { File.Delete(path); n++; }
                }
                else if (file.EndsWith(".tmp", StringComparison.Ordinal)
                         && File.GetLastWriteTimeUtc(path) < now.UtcDateTime - TimeSpan.FromHours(1))
                {
                    File.Delete(path);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        if (n > 0) _stats = null;
        return n;
    }

    /// <summary>Số ảnh (file tên đúng luật, kể cả đã hết hạn mà job dọn chưa tới) + tổng byte cả thư mục (như trần tính).
    /// Nhớ <see cref="StatsTtlMs"/>; ghi/xoá qua store thì đếm lại ngay lần sau.</summary>
    public async Task<(long Count, long Bytes)> GetStatsAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = Environment.TickCount64;
            if (_stats is { } c && now - c.At < StatsTtlMs) return (c.Count, c.Bytes);
            long n = 0, total = 0;
            foreach (var f in new DirectoryInfo(_dir).EnumerateFiles())
            {
                try { total += f.Length; } catch (IOException) { continue; }
                if (ParseName(f.Name) is not null) n++;
            }
            _stats = (n, total, now);
            return (n, total);
        }
        finally { _gate.Release(); }
    }

    public long MaxTotalBytes => _maxTotalBytes;

    /// <summary>Tổng byte đang chiếm (mọi file trong thư mục, kể cả tmp đang ghi — tính dư còn hơn vượt trần).</summary>
    private long UsedBytesLocked()
    {
        long total = 0;
        foreach (var f in new DirectoryInfo(_dir).EnumerateFiles())
        {
            try { total += f.Length; } catch (IOException) { }
        }
        return total;
    }
}
