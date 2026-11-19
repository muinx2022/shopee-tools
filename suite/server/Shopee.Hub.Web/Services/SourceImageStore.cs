using System.Globalization;
using System.Security.Cryptography;
using Shopee.Core.Coordination;

namespace Shopee.Hub.Web.Services;

/// <summary>
/// Kho ẢNH GỐC (GĐ9): ảnh SP Shopee nguyên bytes, khoá theo mã ảnh Shopee, app Harvest đẩy lên lúc cào. KHÔNG công khai
/// (route cần token Client), KHÔNG hạn, KHÔNG tự xoá. Lưu kiểu <see cref="PublicMediaStore"/>: blob trên đĩa trong
/// <c>DataDir/source-images/</c> (thư mục RIÊNG, không vào manifest <c>files</c>, không vào backup đêm), tên file = đúng id
/// (luật <see cref="SourceImageLimits.IsValidId"/>: không có '.', '/', '\' nên không đi ra ngoài thư mục được).
/// <para><b>Chỉ nhận ảnh DÙNG ĐƯỢC</b> (<see cref="Validate"/>): JPEG/PNG đúng cấu trúc mà <see cref="ImageMd5Changer"/>
/// đổi MD5 được, bản đổi không vượt trần. Mỗi ảnh nhận có file phụ <c>{id}.meta</c> =
/// <c>"{sha256 8 byte hex} {độ dài} {jpg|png}"</c> ghi SAU ảnh: dấu vân tay nội dung để ký vào link (ảnh khác bytes → link
/// khác → cache Cloudflare cũ không trả nhầm), khỏi băm lại mỗi request. Ảnh "dùng được" ⇔ có meta đọc được VÀ độ dài khớp
/// file — file không meta / lệch (ghi dở lúc mất điện, file cũ trước khi có luật này) coi như CHƯA CÓ: <c>missing</c> báo
/// lại, PUT mới THAY.</para>
/// <para>Số ảnh + tổng byte (chỉ file ảnh, không tính meta / tmp) giữ trong bộ nhớ (đếm đĩa MỘT lần lúc dùng đầu tiên, cập
/// nhật trong khoá) để kiểm trần và trả stats không phải liệt kê vài chục nghìn file mỗi lần.</para>
/// </summary>
public sealed class SourceImageStore
{
    private const string MetaSuffix = ".meta";
    private readonly string _dir;
    private readonly long _maxTotalBytes;
    private readonly long _minFreeBytes;
    // Mọi thao tác ghi / xoá (PUT, DELETE 1 ảnh, DELETE-all) đi qua khoá này: hai PUT cùng id không ghi đôi, hai PUT khác id
    // không cùng lọt trần, DELETE-all không thấy file dở. Đọc (missing / links / GET) không khoá — ảnh + meta đổi tên nguyên tử.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _count = -1;   // -1 = chưa đếm
    private long _bytes;

    /// <summary>Dung lượng còn trống (byte) của ổ chứa <see cref="Dir"/> — TEST thay để giả đĩa đầy.</summary>
    internal Func<long> FreeBytes { get; set; }

    /// <summary>Tổng dung lượng (byte) của ổ chứa <see cref="Dir"/>.</summary>
    internal Func<long> TotalBytes { get; set; }

    public string Dir => _dir;
    public long MaxTotalBytes => _maxTotalBytes;

    public SourceImageStore(HubOptions opts)
    {
        _dir = Path.Combine(opts.DataDir, "source-images");
        _maxTotalBytes = Math.Max(0, opts.SourceImagesMaxTotalBytes);
        _minFreeBytes = Math.Max(0, opts.MediaMinFreeBytes);
        Directory.CreateDirectory(_dir);
        // Như PublicMediaStore: Windows cần tên ổ gốc; Linux đo đúng mount chứa thư mục.
        var drive = OperatingSystem.IsWindows() ? Path.GetPathRoot(Path.GetFullPath(_dir))! : _dir;
        FreeBytes = () => new DriveInfo(drive).AvailableFreeSpace;
        TotalBytes = () => new DriveInfo(drive).TotalSize;
    }

    /// <summary>Kiểu ảnh theo MAGIC BYTES (như <see cref="PublicMediaStore.SniffExt"/>): <c>jpg</c> | <c>png</c> | null.</summary>
    internal static string? SniffExt(ReadOnlySpan<byte> data) => PublicMediaStore.SniffExt(data);

    internal static string ContentTypeOf(string? ext) =>
        ext is null ? "application/octet-stream" : PublicMediaStore.ContentTypeOf(ext);

    /// <summary>Kiểm ảnh có DÙNG ĐƯỢC không (cùng một phép kiểm cho PUT mới và cho bản đang giữ): magic JPEG/PNG (khác →
    /// UnsupportedType, gồm WebP); JPEG phải kết thúc <c>FF D9</c>, PNG phải kết thúc bằng chunk IEND (khác → Corrupt);
    /// thử <see cref="ImageMd5Changer.Change"/> thật — ném vì bản đổi vượt <see cref="PublicMediaLimits.MaxBytes"/> →
    /// TooLarge, ném vì cấu trúc (APPn vượt file, thiếu IHDR…) → Corrupt. Ok → null.</summary>
    internal static SaveStatus? Validate(byte[] data)
    {
        if (data.Length == 0) return SaveStatus.Empty;
        var ext = SniffExt(data);
        if (ext is null) return SaveStatus.UnsupportedType;
        if (ext == "jpg" && !(data.Length >= 4 && data[^2] == 0xFF && data[^1] == 0xD9)) return SaveStatus.Corrupt;
        if (ext == "png" && !(data.Length >= 8 + 25 + 12 && data.AsSpan(data.Length - 12, 4).SequenceEqual(new byte[4])
                              && data.AsSpan(data.Length - 8, 4).SequenceEqual("IEND"u8))) return SaveStatus.Corrupt;
        try { ImageMd5Changer.Change(data, "probe", "probe"); }
        catch (ImageMd5Exception)
        {
            // Cùng điều kiện trần mà ImageMd5Changer kiểm (ảnh gốc hoặc bản ra vượt trần) — phần còn lại là lỗi cấu trúc.
            var growth = ext == "png" ? 4 + 4 + 7 + 1 + 64 + 4 : 2 + 2 + 64;
            return data.Length + growth > PublicMediaLimits.MaxBytes ? SaveStatus.TooLarge : SaveStatus.Corrupt;
        }
        return null;
    }

    /// <summary>Đường dẫn vật lý của id HỢP LỆ; id sai luật → null (chặn mọi đường đi ra ngoài thư mục).</summary>
    private string? PathOf(string? id) => SourceImageLimits.IsValidId(id) ? Path.Combine(_dir, id!) : null;

    /// <summary>Thông tin ảnh DÙNG ĐƯỢC: dấu vân tay (8 byte đầu SHA-256), độ dài, đuôi. File không có / không meta / meta
    /// hỏng / độ dài lệch / id sai → null.</summary>
    public sealed record ImageInfo(byte[] Fingerprint, long Size, string Ext);

    public ImageInfo? Usable(string? id)
    {
        if (PathOf(id) is not { } p) return null;
        try
        {
            var parts = File.ReadAllText(p + MetaSuffix).Split(' ');
            if (parts.Length != 3 || parts[0].Length != 16 || parts[2] is not ("jpg" or "png")
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var len)) return null;
            var fp = Convert.FromHexString(parts[0]);
            return new FileInfo(p).Length == len ? new ImageInfo(fp, len, parts[2]) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { return null; }
    }

    /// <summary>Có FILE ảnh (dùng được hay không) — cho GET ảnh gốc của client.</summary>
    public bool FileExists(string? id) => PathOf(id) is { } p && File.Exists(p);

    /// <summary>Đọc nguyên bytes 1 ảnh. Không có / id sai → null (kể cả khi vừa bị xoá giữa chừng).</summary>
    public async Task<byte[]?> ReadAsync(string? id, CancellationToken ct)
    {
        if (PathOf(id) is not { } p) return null;
        try { return await File.ReadAllBytesAsync(p, ct); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public enum SaveStatus { Ok, Existed, Replaced, BadId, Empty, TooLarge, UnsupportedType, Corrupt, QuotaExceeded, DiskLow }

    /// <summary><see cref="Size"/> / <see cref="Ext"/> = của bản ĐANG GIỮ khi Ok / Existed / Replaced; <see cref="UsedBytes"/>
    /// khi QuotaExceeded; <see cref="FreeBytes"/> khi DiskLow (-1 = không đo được).</summary>
    public sealed record SaveResult(SaveStatus Status, long Size = 0, string? Ext = null, long UsedBytes = 0, long FreeBytes = 0);

    /// <summary>Lưu 1 ảnh gốc. Thứ tự: id → cỡ → <see cref="Validate"/> → (trong khoá) bản đang giữ DÙNG ĐƯỢC → Existed, KHÔNG
    /// ghi lại (bytes gửi lên bị bỏ qua, kể cả khi khác); có file mà không dùng được → THAY (Replaced) → trần tổng (tính
    /// phần chênh khi thay) → đĩa trống → ghi. Ghi: xoá meta cũ → ảnh vào tmp + fsync → đổi tên (đè khi thay) → meta tmp +
    /// fsync → đổi tên. Mất điện giữa chừng thì còn ảnh không meta = "không dùng được" → Harvest đẩy lại, không bao giờ có
    /// meta trỏ sai bytes. Không đo được đĩa trống → từ chối (DiskLow, -1), như kho ảnh tạm.</summary>
    public async Task<SaveResult> SaveAsync(string? id, byte[] data, CancellationToken ct)
    {
        if (PathOf(id) is not { } path) return new SaveResult(SaveStatus.BadId);
        if (data.Length == 0) return new SaveResult(SaveStatus.Empty);
        if (data.Length > SourceImageLimits.MaxBytes) return new SaveResult(SaveStatus.TooLarge);
        if (Validate(data) is { } bad) return new SaveResult(bad);
        var ext = SniffExt(data)!;

        await _gate.WaitAsync(ct);
        try
        {
            if (Usable(id) is { } had) return new SaveResult(SaveStatus.Existed, had.Size, had.Ext);
            EnsureCountedLocked();
            long oldLen = -1;
            try { oldLen = new FileInfo(path).Length; } catch (FileNotFoundException) { }
            var delta = data.Length - Math.Max(0, oldLen);
            if (_bytes + delta > _maxTotalBytes) return new SaveResult(SaveStatus.QuotaExceeded, UsedBytes: _bytes);
            long free;
            try { free = FreeBytes(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { free = -1; }
            if (free - data.Length < _minFreeBytes)   // free = -1 (không đo được) luôn rơi vào nhánh này
                return new SaveResult(SaveStatus.DiskLow, UsedBytes: _bytes, FreeBytes: free);

            var fp = SHA256.HashData(data).AsSpan(0, 8).ToArray();
            File.Delete(path + MetaSuffix);   // không có cũng không ném
            await WriteDurableAsync(path, data, ct);
            await WriteDurableAsync(path + MetaSuffix, System.Text.Encoding.ASCII.GetBytes(
                $"{Convert.ToHexString(fp).ToLowerInvariant()} {data.Length.ToString(CultureInfo.InvariantCulture)} {ext}"), ct);
            if (oldLen < 0) { _count++; _bytes += delta; }
            else { _count = -1; EnsureCountedLocked(); }   // thay bản hỏng (hiếm): đếm lại đĩa — file hỏng có thể đã đổi
                                                           // cỡ ngoài tầm bộ đếm (ghi dở, bị cắt)
            return new SaveResult(oldLen < 0 ? SaveStatus.Ok : SaveStatus.Replaced, data.Length, ext);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Ghi tmp + fsync rồi đổi tên đè lên <paramref name="path"/> (đổi tên trong cùng thư mục là nguyên tử).</summary>
    private static async Task WriteDurableAsync(string path, byte[] data, CancellationToken ct)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                             FileOptions.Asynchronous))
            {
                await fs.WriteAsync(data, ct);
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>Xoá 1 ảnh (meta trước, ảnh sau) trong khoá ghi. true = đã xoá file ảnh; không có / id sai → false.</summary>
    public async Task<bool> DeleteAsync(string? id, CancellationToken ct)
    {
        if (PathOf(id) is not { } path) return false;
        await _gate.WaitAsync(ct);
        try
        {
            EnsureCountedLocked();
            File.Delete(path + MetaSuffix);
            long len;
            try { len = new FileInfo(path).Length; } catch (FileNotFoundException) { return false; }
            File.Delete(path);
            _count--;
            _bytes -= len;
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Số ảnh + tổng byte hiện có (bộ đếm trong bộ nhớ, đếm đĩa lần đầu).</summary>
    public async Task<(long Count, long Bytes)> GetStatsAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { EnsureCountedLocked(); return (_count, _bytes); }
        finally { _gate.Release(); }
    }

    /// <summary>Xoá SẠCH kho gốc trong khoá ghi (PUT song song đợi xong hoặc chạy sau, không có file dở). Xoá luôn meta + file
    /// tạm mồ côi. Sau khi xoá ĐẾM LẠI đĩa (không trừ) → <c>Remaining</c>; bộ đếm trong bộ nhớ đặt theo số đếm lại.</summary>
    public async Task<(long Deleted, long Remaining, long FreedBytes)> DeleteAllAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            long deleted = 0, freed = 0;
            foreach (var f in new DirectoryInfo(_dir).EnumerateFiles())
            {
                try
                {
                    var len = f.Length;
                    var isImage = SourceImageLimits.IsValidId(f.Name);
                    f.Delete();
                    freed += len;
                    if (isImage) deleted++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _count = -1;
            EnsureCountedLocked();
            return (deleted, _count, freed);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Đếm đĩa nếu bộ đếm chưa có: chỉ tính file tên đúng luật id (bỏ meta, file tạm).</summary>
    private void EnsureCountedLocked()
    {
        if (_count >= 0) return;
        long n = 0, total = 0;
        foreach (var f in new DirectoryInfo(_dir).EnumerateFiles())
        {
            if (!SourceImageLimits.IsValidId(f.Name)) continue;
            try { total += f.Length; n++; } catch (IOException) { }
        }
        _count = n;
        _bytes = total;
    }
}
