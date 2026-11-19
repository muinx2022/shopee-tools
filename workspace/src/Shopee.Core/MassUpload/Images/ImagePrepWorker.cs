using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Shopee.Core.Coordination;

namespace Shopee.Core.MassUpload.Images;

/// <summary>Tham số của <see cref="ImagePrepWorker"/> (test thu nhỏ cho nhanh).</summary>
public sealed class ImagePrepWorkerOptions
{
    /// <summary>Nhịp quét hàng đợi khi rảnh / khi cầu dao đang ngắt.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(15);
    /// <summary>Số ảnh xử lý song song mỗi lượt (giữ nhỏ: 1–2).</summary>
    public int MaxParallel { get; init; } = 2;
    /// <summary>Lùi dần <c>min(MaxBackoff, BaseBackoff × 2^(n−1))</c>: cho cầu dao chung (n = số lỗi hệ thống liên tiếp) và cho
    /// ảnh kiểm lệch (n = số lần thử của ảnh).</summary>
    public TimeSpan BaseBackoff { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(30);
    /// <summary>Ảnh kiểm lệch (MD5 / Content-Type / mã lạ) đủ chừng này lần thì <c>failed</c>. Lỗi hệ thống không tính.</summary>
    public int MaxAttempts { get; init; } = 8;
    /// <summary>Dòng <c>deleting</c> nằm quá chừng này (Release xoá hụt / bị huỷ) thì worker xoá lại trên Hub. Forget đánh dấu
    /// đến hạn ngay.</summary>
    public TimeSpan DeletingRetryAfter { get; init; } = TimeSpan.FromMinutes(10);
    /// <summary>Số dòng <c>deleting</c> tối đa dọn mỗi lượt.</summary>
    public int MaxDeletesPerPass { get; init; } = 5;
    /// <summary>URL ảnh gốc trên CDN Shopee theo id ảnh.</summary>
    public string CdnUrlFormat { get; init; } = "https://down-vn.img.susercontent.com/file/{0}";
}

/// <summary>Trạng thái mới của một ảnh sau khi worker xử lý (cho UI).</summary>
public enum ImagePrepOutcome { Verified, Retry, Failed }

/// <summary>Tiến độ một ảnh: <see cref="Error"/> = lý do khi <see cref="ImagePrepOutcome.Retry"/> / <see cref="ImagePrepOutcome.Failed"/>.</summary>
public sealed record ImagePrepProgress(string ShopKey, string SourceKey, ImagePrepOutcome Outcome, string? Error);

/// <summary>
/// Vòng nền của bước đệm MD5 ảnh (plan GĐ2 bước 4): lấy ảnh <c>pending</c> đến hạn trong <see cref="UploadStore"/> → tải ảnh
/// gốc (ảnh SP: CDN Shopee bằng IP máy, không qua proxy — đã chốt; bảng size: file cục bộ theo <see cref="SizeChartImage"/>) →
/// <see cref="ImageMd5Changer"/> → <see cref="HubClient.UploadPublicMediaAsync"/> → ghi bền <c>hosted</c> → KIỂM bằng GET URL
/// công khai KHÔNG token (200 + đúng Content-Type + MD5 khớp) → <c>verified</c>. Ảnh <c>hosted</c> (app tắt giữa lúc up và lúc
/// kiểm) được lấy lại và CHỈ KIỂM, không up lại.
/// <para><b>Lỗi của CHÍNH ảnh</b> (tăng số lần thử / <c>failed</c>):</para>
/// <list type="bullet">
/// <item>ảnh gốc 404/410, định dạng không hỗ trợ, quá cỡ, file bảng size mất / đổi nội dung, Hub 413/415 → <c>failed</c> ngay;</item>
/// <item>CDN 4xx khác 404/410/408/429 (403, 400…) → lùi dần theo ảnh; kiểm lệch (MD5, Content-Type, mã lạ) → xoá trên
/// Hub, về <c>pending</c>, lùi dần; đủ
/// <see cref="ImagePrepWorkerOptions.MaxAttempts"/> → <c>failed</c>.</item>
/// </list>
/// <para><b>Lỗi hệ thống</b> (Hub 5xx / 502 không mã / 503 không mã / 507 / 429 / 408, CDN 5xx / 429 / 408, lỗi mạng / timeout,
/// GET kiểm lỗi mạng / 5xx / 403 / 429) → ảnh GIỮ NGUYÊN trạng thái và số lần thử (chỉ xuống cuối hàng), <b>cầu dao chung</b>
/// ngắt: không lấy ảnh mới tới mốc <c>min(MaxBackoff, BaseBackoff × 2^(n−1))</c> (n = số lỗi hệ thống liên tiếp). Một ảnh xử lý
/// xong (dù đạt hay lỗi của ảnh) đóng cầu dao. Hub sập nhiều giờ không làm ảnh nào <c>failed</c>.</para>
/// <para><b>Cấu hình hỏng</b> (502 CÓ <c>ErrorCode</c>, 503 có mã, 401 / 403, 4xx khác khi PUT) → DỪNG worker
/// (<see cref="HaltReason"/> + <see cref="Halted"/>), không đổi trạng thái ảnh. Gọi <see cref="Start"/> lại sau khi sửa.</para>
/// <para>Mỗi lượt dọn trước các dòng <c>deleting</c> quá hạn (<see cref="ImagePrepWorkerOptions.DeletingRetryAfter"/>): xoá lại
/// trên Hub rồi chốt như <see cref="UploadStore.ReleaseItemAsync"/>.</para>
/// <para>Chưa kết nối Hub (client null) → chờ nhịp sau. Huỷ giữa chừng → ảnh giữ trạng thái đang có, không tăng số lần thử.
/// PUT xong là ghi <c>hosted</c> ngay (không có điểm huỷ giữa PUT trả về và lúc ghi) nên không mồ côi; huỷ khi PUT còn đang
/// bay thì chưa biết path — ảnh (nếu Hub đã lưu) nằm tới hết TTL.</para>
/// </summary>
public sealed class ImagePrepWorker : IAsyncDisposable
{
    /// <summary>User-Agent trình duyệt thông thường cho request CDN và GET kiểm.</summary>
    internal const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    /// <summary>Accept của request tải ảnh gốc từ CDN.</summary>
    internal const string ImageAccept = "image/jpeg,image/png,image/*;q=0.8";

    private readonly UploadStore _store;
    private readonly Func<HubClient?> _hub;
    private readonly HttpClient _fetch;
    private readonly ImagePrepWorkerOptions _opt;
    private readonly Action<string>? _log;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Tín hiệu đánh thức — tối đa 1.</summary>
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly object _life = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile string? _haltReason;

    /// <summary>Cầu dao chung: số lỗi hệ thống liên tiếp và mốc được lấy ảnh lại.</summary>
    private readonly object _breakerLock = new();
    private int _sysStreak;
    private DateTimeOffset _breakerUntil = DateTimeOffset.MinValue;

    /// <summary>Số lần LIÊN TIẾP (mọi ảnh) ảnh vừa up mà GET công khai trả 404 — đủ <see cref="Fresh404HaltAfter"/> là URL công
    /// khai hỏng (base URL / tunnel) → dừng worker. Một lần kiểm đạt đặt lại 0.</summary>
    private int _fresh404Streak;

    internal const int Fresh404HaltAfter = 3;

    internal const string Fresh404HaltReason = "URL công khai không truy cập được (kiểm HUB_PUBLIC_BASE_URL / tunnel)";

    /// <summary>Một ảnh vừa đổi trạng thái. Gọi từ luồng nền.</summary>
    public event Action<ImagePrepProgress>? Progress;

    /// <summary>Worker tự dừng vì cấu hình hỏng (tham số = lý do). Gọi từ luồng nền.</summary>
    public event Action<string>? Halted;

    /// <summary>Lý do dừng vì cấu hình hỏng; null = không dừng. <see cref="Start"/> xoá.</summary>
    public string? HaltReason => _haltReason;

    /// <summary>Lỗi gần nhất (vd "Chưa kết nối Hub", lỗi hệ thống đang ngắt cầu dao) — null sau một ảnh xử lý xong.</summary>
    public string? LastError { get; private set; }

    /// <summary>Mốc cầu dao chung (lỗi hệ thống) cho phép lấy ảnh lại; quá khứ = không ngắt.</summary>
    public DateTimeOffset BreakerUntil { get { lock (_breakerLock) return _breakerUntil; } }

    /// <param name="hub">Lấy HubClient HIỆN TẠI mỗi lượt (client bị tráo khi kết nối lại). null = chưa kết nối.</param>
    /// <param name="fetch">HttpClient KHÔNG kèm token, dùng tải ảnh CDN và kiểm URL công khai (xem <see cref="CreateFetchClient"/>).</param>
    /// <param name="clock">Đồng hồ cho cầu dao (test tiêm giờ giả); null = giờ hệ thống.</param>
    public ImagePrepWorker(UploadStore store, Func<HubClient?> hub, HttpClient fetch, ImagePrepWorkerOptions? options = null,
        Action<string>? log = null, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _hub = hub;
        _fetch = fetch;
        _opt = options ?? new ImagePrepWorkerOptions();
        _log = log;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>HttpClient không header xác thực, timeout 60 s, KHÔNG đi proxy hệ thống (<c>UseProxy = false</c>) — tải CDN
    /// đúng bằng IP máy như đã chốt — cho <c>fetch</c> của bản chạy thật.</summary>
    public static HttpClient CreateFetchClient() => new(CreateFetchHandler()) { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Handler của <see cref="CreateFetchClient"/>: KHÔNG proxy hệ thống (tách riêng để test kiểm được).</summary>
    internal static HttpClientHandler CreateFetchHandler() => new() { UseProxy = false };

    // ── Vòng đời ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Chạy vòng nền (đang chạy = không làm gì). Xoá <see cref="HaltReason"/> của lần dừng trước.</summary>
    public void Start()
    {
        lock (_life)
        {
            if (_loop is { IsCompleted: false }) return;
            _cts?.Dispose();
            _haltReason = null;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _loop = Task.Run(() => LoopAsync(ct));
        }
    }

    /// <summary>Dừng vòng nền, chờ lượt đang bay kết thúc (huỷ HTTP đang chờ).</summary>
    public async Task StopAsync()
    {
        Task? loop;
        lock (_life)
        {
            loop = _loop;
            _cts?.Cancel();
            _loop = null;
        }
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        lock (_life)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>Đánh thức vòng nền ngay (vd vừa đưa ảnh vào hàng chờ).</summary>
    public void Nudge()
    {
        try { _wake.Release(); } catch (SemaphoreFullException) { }
    }

    /// <summary>Task của vòng nền (cho test chờ worker tự dừng).</summary>
    internal Task? Loop { get { lock (_life) return _loop; } }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            int done;
            try { done = await RunOnceAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                // Lỗi hạ tầng (DB) — ghi log, chờ nhịp sau, không chết vòng.
                LastError = ex.Message;
                _log?.Invoke($"[ảnh] lỗi vòng xử lý: {ex.Message}");
                done = 0;
            }
            if (_haltReason is not null) return;
            if (done > 0) continue;
            try { await _wake.WaitAsync(_opt.Interval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    // ── Một lượt ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Dọn các dòng <c>deleting</c> quá hạn, rồi (nếu cầu dao không ngắt) xử lý tối đa
    /// <see cref="ImagePrepWorkerOptions.MaxParallel"/> ảnh đến hạn, song song. Trả số việc đã làm (0 = rảnh / chưa kết nối Hub
    /// / cầu dao ngắt / đang dừng vì cấu hình hỏng).</summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        if (_haltReason is not null) return 0;
        var hub = _hub();
        if (hub is null)
        {
            LastError = "Chưa kết nối Hub";
            return 0;
        }
        var work = 0;
        foreach (var d in _store.TakeStaleDeleting(_opt.DeletingRetryAfter, Math.Max(1, _opt.MaxDeletesPerPass)))
        {
            await _store.FinishDeleteAsync(d, hub, ct).ConfigureAwait(false);
            work++;
        }
        if (_clock() < BreakerUntil) return work;
        var due = _store.TakeDue(Math.Max(1, _opt.MaxParallel));
        if (due.Count == 0) return work;
        // Một ảnh gặp cấu hình hỏng thì huỷ các ảnh cùng lượt (chúng giữ trạng thái, không tăng số lần thử).
        using var pass = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await Task.WhenAll(due.Select(e => ProcessAsync(e, hub, pass))).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return work + due.Count;
    }

    /// <summary>Lỗi hệ thống (Hub / mạng / CDN 5xx-429-408) — không phải lỗi của ảnh: ngắt cầu dao chung.</summary>
    private sealed class SystemFaultException(string message) : Exception(message);

    /// <summary>Lỗi vĩnh viễn của CHÍNH ảnh (404, file bảng size mất / đổi, Hub 413/415).</summary>
    private sealed class PermanentException(string message) : Exception(message);

    /// <summary>Lỗi cấu hình (Hub / token / URL công khai) — dừng worker.</summary>
    private sealed class ConfigException(string message) : Exception(message);

    /// <summary>Lỗi gắn với CHÍNH ảnh nhưng có thể chỉ thoáng qua (CDN 403/400…): tăng số lần thử của ảnh, lùi dần, trần →
    /// <c>failed</c>; không ngắt cầu dao chung.</summary>
    private sealed class ImageRetryException(string message) : Exception(message);

    private async Task ProcessAsync(ImagePrepEntry e, HubClient hub, CancellationTokenSource pass)
    {
        var ct = pass.Token;
        try
        {
            var fresh = false;
            if (e.State != UploadStore.StateHosted)
            {
                var source = await LoadSourceAsync(e, ct).ConfigureAwait(false);
                var changed = ImageMd5Changer.Change(source, e.ShopKey, e.SourceKey);
                var up = await UploadAsync(hub, changed.Bytes, ct).ConfigureAwait(false);
                // Ghi bền NGAY sau PUT (đồng bộ, không có điểm huỷ ở giữa): app tắt từ đây thì lần sau kiểm tiếp, không up lại.
                var hosted = _store.MarkHosted(e, changed.Md5, changed.ContentType, up.Path, up.Url!, up.ExpiresAt);
                if (hosted is null)
                {
                    // Dòng không còn pending (Release xoá / lượt khác đã host) → ảnh vừa up thừa.
                    await DeleteLoggedAsync(hub, up.Path, $"{e.ShopKey}/{e.SourceKey} up thừa").ConfigureAwait(false);
                    return;
                }
                e = hosted;
                fresh = true;
            }
            await CheckHostedAsync(e, hub, fresh, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Huỷ (dừng app / ảnh khác gặp cấu hình hỏng): giữ trạng thái đang có, không tăng số lần thử.
        }
        catch (ImageMd5Exception ex) { Fail(e, ex.Message); }
        catch (PermanentException ex) { Fail(e, ex.Message); }
        catch (ImageRetryException ex) { RetryImage(e, ex.Message); }
        catch (ConfigException ex)
        {
            Halt(ex.Message);
            pass.Cancel();
        }
        catch (Exception ex) when (ex is SystemFaultException or HttpRequestException or OperationCanceledException or IOException)
        {
            // OperationCanceledException không do ct = timeout của HttpClient.
            SystemFault(e, ex is OperationCanceledException ? "quá thời gian chờ" : ex.Message);
        }
    }

    /// <summary>Lỗi của ảnh có thể thử lại: tăng số lần thử, lùi dần theo ảnh, trần → <c>failed</c>; hệ thống vẫn chạy được
    /// nên đóng cầu dao.</summary>
    private void RetryImage(ImagePrepEntry e, string reason)
    {
        CloseBreaker();
        var failed = _store.Defer(e, Backoff(e.Attempts + 1), reason, _opt.MaxAttempts);
        LastError = reason;
        if (failed) _log?.Invoke($"[ảnh] {e.ShopKey}/{e.SourceKey} lỗi: {reason}");
        Raise(e, failed ? ImagePrepOutcome.Failed : ImagePrepOutcome.Retry, reason);
    }

    private void Fail(ImagePrepEntry e, string reason)
    {
        _store.MarkFailed(e, reason);
        CloseBreaker();
        _log?.Invoke($"[ảnh] {e.ShopKey}/{e.SourceKey} lỗi: {reason}");
        Raise(e, ImagePrepOutcome.Failed, reason);
    }

    /// <summary>Lỗi hệ thống: ảnh giữ nguyên (chỉ xuống cuối hàng), ngắt cầu dao chung.</summary>
    private void SystemFault(ImagePrepEntry e, string reason)
    {
        _store.Touch(e);
        DateTimeOffset until;
        lock (_breakerLock)
        {
            _sysStreak++;
            var ms = _opt.BaseBackoff.TotalMilliseconds * Math.Pow(2, Math.Min(_sysStreak - 1, 30));
            until = _clock() + TimeSpan.FromMilliseconds(Math.Min(ms, _opt.MaxBackoff.TotalMilliseconds));
            if (until > _breakerUntil) _breakerUntil = until;
        }
        LastError = reason;
        _log?.Invoke($"[ảnh] lỗi hệ thống ({reason}) — tạm ngừng lấy ảnh tới {until:HH:mm:ss}");
        Raise(e, ImagePrepOutcome.Retry, reason);
    }

    private void CloseBreaker()
    {
        lock (_breakerLock)
        {
            _sysStreak = 0;
            _breakerUntil = DateTimeOffset.MinValue;
        }
    }

    private void Halt(string reason)
    {
        if (_haltReason is not null) return;
        _haltReason = reason;
        LastError = reason;
        _log?.Invoke($"[ảnh] DỪNG bước đệm ảnh: {reason}");
        Halted?.Invoke(reason);
    }

    private void Raise(ImagePrepEntry e, ImagePrepOutcome outcome, string? error) =>
        Progress?.Invoke(new ImagePrepProgress(e.ShopKey, e.SourceKey, outcome, error));

    private TimeSpan Backoff(int attempt)
    {
        var ms = _opt.BaseBackoff.TotalMilliseconds * Math.Pow(2, Math.Min(Math.Max(0, attempt - 1), 30));
        return TimeSpan.FromMilliseconds(Math.Min(ms, _opt.MaxBackoff.TotalMilliseconds));
    }

    /// <summary>Xoá best-effort một ảnh trên Hub (không theo huỷ của lượt), kết quả ghi log.</summary>
    private async Task DeleteLoggedAsync(HubClient hub, string? path, string why)
    {
        var ok = !string.IsNullOrEmpty(path) && await hub.DeletePublicMediaAsync(path, CancellationToken.None).ConfigureAwait(false);
        _log?.Invoke(ok ? $"[ảnh] đã xoá {path} trên Hub ({why})"
                        : $"[ảnh] xoá {path} trên Hub hụt ({why}) — Hub tự dọn khi hết hạn");
    }

    // ── Các bước ────────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<byte[]> LoadSourceAsync(ImagePrepEntry e, CancellationToken ct)
    {
        if (e.Kind == ImagePrepKind.SizeChart)
        {
            if (!SizeChartImage.TryParse(e.SourceKey, out var sha, out _))
                throw new PermanentException($"khoá ảnh bảng size sai dạng: {e.SourceKey}");
            // Đọc theo đường dẫn GỐC (giữ hoa/thường) đã ghi lúc Enqueue — không dùng phần đường dẫn chữ thường trong khoá.
            var path = e.SourcePath;
            if (string.IsNullOrWhiteSpace(path)) throw new PermanentException("Chưa có ảnh bảng size: sổ thiếu đường dẫn file — chọn lại ảnh");
            if (!File.Exists(path)) throw new PermanentException($"Chưa có ảnh bảng size: không thấy file {path}");
            if (new FileInfo(path).Length > PublicMediaLimits.MaxBytes)
                throw new PermanentException($"ảnh bảng size vượt trần {PublicMediaLimits.MaxBytes / 1024 / 1024} MB");
            var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), sha, StringComparison.OrdinalIgnoreCase))
                throw new PermanentException($"ảnh bảng size {path} đã đổi nội dung — chạy lại lượt để lấy khoá mới");
            return bytes;
        }
        var url = string.Format(System.Globalization.CultureInfo.InvariantCulture, _opt.CdnUrlFormat, Uri.EscapeDataString(e.SourceKey));
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        req.Headers.TryAddWithoutValidation("Accept", ImageAccept);
        using var r = await _fetch.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            throw new PermanentException($"ảnh gốc không còn trên CDN ({(int)r.StatusCode})");
        var code = (int)r.StatusCode;
        // 4xx khác 404/410/408/429 (vd 403, 400) = CDN từ chối CHÍNH ảnh này → lỗi của ảnh (thử lại tới trần, không ngắt cầu dao).
        if (code is >= 400 and < 500 && r.StatusCode is not (HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout))
            throw new ImageRetryException($"CDN trả {code} lặp lại");
        if (!r.IsSuccessStatusCode)
            throw new SystemFaultException($"CDN trả {code} khi tải ảnh gốc");
        return await ReadLimitedAsync(r, "ảnh gốc", ct).ConfigureAwait(false);
    }

    /// <summary>Đọc body tối đa <see cref="PublicMediaLimits.MaxBytes"/> — vượt thì lỗi vĩnh viễn, không đọc hết.</summary>
    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage r, string what, CancellationToken ct)
    {
        var max = PublicMediaLimits.MaxBytes;
        if (r.Content.Headers.ContentLength is long len && len > max)
            throw new PermanentException($"{what} {len / 1024} KB vượt trần {max / 1024 / 1024} MB");
        await using var s = await r.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        while ((n = await s.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > max) throw new PermanentException($"{what} vượt trần {max / 1024 / 1024} MB");
        }
        return ms.ToArray();
    }

    private static async Task<PublicMediaUploadResponse> UploadAsync(HubClient hub, byte[] bytes, CancellationToken ct)
    {
        var (status, body, code) = await hub.UploadPublicMediaAsync(bytes, ct).ConfigureAwait(false);
        if (body is not null) return body;
        var tag = code is null ? $"{(int)status}" : $"{(int)status} {code}";
        throw status switch
        {
            // 502 CÓ mã = client tự gán (response hỏng / URL không công khai) = cấu hình Hub hỏng; KHÔNG mã = Cloudflare tạm.
            HttpStatusCode.BadGateway when code is not null => new ConfigException($"Hub trả phản hồi hỏng ({tag})"),
            HttpStatusCode.ServiceUnavailable when code is not null => new ConfigException($"Hub thiếu cấu hình ({tag})"),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ConfigException($"Hub từ chối token ({tag})"),
            HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.UnsupportedMediaType
                => new PermanentException($"Hub từ chối ảnh ({tag})"),
            HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout => new SystemFaultException($"Hub bận ({tag})"),
            _ when (int)status >= 500 => new SystemFaultException($"Hub lỗi tạm ({tag})"),   // 5xx khác, 502/503 không mã, 507
            _ => new ConfigException($"Hub trả mã không mong đợi khi tải ảnh lên ({tag}) — Hub cũ / sai địa chỉ?"),
        };
    }

    /// <summary>Kết quả kiểm một ảnh <c>hosted</c>.</summary>
    private enum Check
    {
        /// <summary>200 + đúng Content-Type + MD5 khớp.</summary>
        Ok,
        /// <summary>Hub trả sai (mã lạ, Content-Type sai, MD5 lệch, quá cỡ) — lỗi gắn với ảnh này.</summary>
        Mismatch,
        /// <summary>404/410 — ảnh đã hết hạn / không còn trên Hub.</summary>
        Missing,
        /// <summary>Không kiểm được lúc này (mạng, timeout, 5xx, 403, 429, 408) — lỗi hệ thống.</summary>
        Unreachable,
    }

    /// <summary>Kiểm ảnh <c>hosted</c> rồi chuyển trạng thái: Ok → <c>verified</c>; Mismatch → xoá trên Hub, về <c>pending</c>
    /// (tăng số lần thử, lùi dần, trần → <c>failed</c>); Missing → về <c>pending</c> (không tăng). Ảnh VỪA up mà đã 404 thì xoá
    /// bản vừa up (best-effort), về <c>pending</c>, ngắt cầu dao; đủ <see cref="Fresh404HaltAfter"/> lần LIÊN TIẾP (mọi ảnh) →
    /// dừng worker (URL công khai hỏng). Unreachable → giữ <c>hosted</c>, lỗi hệ thống.</summary>
    private async Task CheckHostedAsync(ImagePrepEntry e, HubClient hub, bool fresh, CancellationToken ct)
    {
        var (check, problem) = await VerifyAsync(e, ct).ConfigureAwait(false);
        switch (check)
        {
            case Check.Ok:
                Interlocked.Exchange(ref _fresh404Streak, 0);
                CloseBreaker();
                // false = Release / Forget đã chuyển dòng sang deleting trong lúc kiểm — phần xoá do họ / worker dọn lo.
                if (_store.MarkVerified(e))
                {
                    LastError = null;
                    Raise(e, ImagePrepOutcome.Verified, null);
                }
                return;
            case Check.Mismatch:
            {
                CloseBreaker();
                await DeleteLoggedAsync(hub, e.HubPath, $"{e.ShopKey}/{e.SourceKey} kiểm lệch").ConfigureAwait(false);
                var failed = _store.Defer(e, Backoff(e.Attempts + 1), problem!, _opt.MaxAttempts);
                LastError = problem;
                Raise(e, failed ? ImagePrepOutcome.Failed : ImagePrepOutcome.Retry, problem);
                return;
            }
            case Check.Missing:
                if (!fresh)
                {
                    _store.ReturnToPending(e, problem!);
                    Raise(e, ImagePrepOutcome.Retry, problem);
                    return;
                }
                // Hub vừa nhận mà URL công khai đã 404: bản vừa up (nếu có thật) là rác → xoá trước khi về pending.
                await DeleteLoggedAsync(hub, e.HubPath, $"{e.ShopKey}/{e.SourceKey} vừa up mà URL công khai 404").ConfigureAwait(false);
                _store.ReturnToPending(e, problem!);
                if (Interlocked.Increment(ref _fresh404Streak) >= Fresh404HaltAfter)
                    throw new ConfigException(Fresh404HaltReason);
                SystemFault(e with { State = UploadStore.StatePending }, problem!);
                return;
            default:
                SystemFault(e, problem!);
                return;
        }
    }

    /// <summary>GET URL công khai KHÔNG token (client <c>fetch</c>), so với <c>md5</c> / <c>content_type</c> đã ghi lúc host.</summary>
    private async Task<(Check, string?)> VerifyAsync(ImagePrepEntry e, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, e.Url);
            req.Headers.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
            using var r = await _fetch.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            var code = (int)r.StatusCode;
            if (r.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                return (Check.Missing, $"kiểm URL công khai: mã {code} (ảnh đã hết hạn / không còn trên Hub)");
            if (code >= 500 || r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout)
                return (Check.Unreachable, $"kiểm URL công khai: mã {code}");
            if (r.StatusCode != HttpStatusCode.OK) return (Check.Mismatch, $"kiểm URL công khai: mã {code}");
            var type = r.Content.Headers.ContentType?.MediaType;
            if (!string.Equals(type, e.ContentType, StringComparison.OrdinalIgnoreCase))
                return (Check.Mismatch, $"kiểm URL công khai: Content-Type {type ?? "(trống)"} ≠ {e.ContentType}");
            var got = ImageMd5Changer.Md5Hex(await ReadLimitedAsync(r, "ảnh kiểm", ct).ConfigureAwait(false));
            return got == e.Md5 ? (Check.Ok, null) : (Check.Mismatch, $"kiểm URL công khai: MD5 {got} ≠ {e.Md5}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (PermanentException ex) { return (Check.Mismatch, $"kiểm URL công khai: {ex.Message}"); }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            return (Check.Unreachable, $"kiểm URL công khai lỗi: {(ex is OperationCanceledException ? "quá thời gian chờ" : ex.Message)}");
        }
    }
}
