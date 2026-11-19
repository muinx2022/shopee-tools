using System.Net;
using System.Net.Http;
using System.Text;
using Shopee.Core.Coordination;

namespace Shopee.Core.Tests.MassUpload.Images;

/// <summary>Thế giới GIẢ cho bước đệm MD5 ảnh, không mạng thật:
/// <list type="bullet">
/// <item><see cref="Hub"/> — handler của <see cref="HubClient"/> (<c>http://hub.test</c>): <c>PUT /api/media</c> lưu bytes, trả
/// URL công khai <c>https://media.example.com/api/media/&lt;tên&gt;</c>; <c>DELETE /api/media/&lt;tên&gt;</c> xoá.</item>
/// <item><see cref="Fetch"/> — handler của HttpClient KHÔNG token: CDN <c>down-vn.img.susercontent.com/file/&lt;id&gt;</c> và GET
/// URL công khai.</item>
/// </list>
/// Mỗi chỗ có móc để test bẻ phản hồi.</summary>
internal sealed class FakeMediaWorld
{
    public const string PublicBase = "https://media.example.com";

    /// <summary>Ảnh gốc trên CDN theo id; không có = 404.</summary>
    public Dictionary<string, byte[]> Cdn { get; } = [];
    /// <summary>Ảnh đang nằm trên Hub theo tên.</summary>
    public Dictionary<string, byte[]> Stored { get; } = [];
    public List<string> Deleted { get; } = [];
    public List<HttpRequestMessage> HubRequests { get; } = [];
    public List<HttpRequestMessage> FetchRequests { get; } = [];
    public int Uploads;

    /// <summary>Đồng hồ cho hạn ảnh (<c>expiresAt</c> = giờ này + 7 ngày); null = mốc cố định 2026-11-24.</summary>
    public Func<DateTimeOffset>? Clock { get; set; }

    /// <summary>Mã trả cho ảnh CDN theo id (null = xử lý thường).</summary>
    public Func<string, HttpStatusCode?>? CdnStatus { get; set; }
    /// <summary>Ném lỗi khi PUT (mô phỏng mất mạng / timeout HttpClient) — null = xử lý thường.</summary>
    public Func<Exception?>? UploadThrow { get; set; }
    /// <summary>Ném lỗi khi GET URL công khai — null = xử lý thường.</summary>
    public Func<Exception?>? PublicThrow { get; set; }
    /// <summary>Bẻ phản hồi PUT (trả null = xử lý thường).</summary>
    public Func<int, HttpResponseMessage?>? UploadOverride { get; set; }
    /// <summary>Chạy (await) ngay khi Hub nhận PUT, trước khi lưu ảnh — test chen thao tác vào giữa lúc worker đang up.</summary>
    public Func<Task>? OnUpload { get; set; }
    /// <summary>Bẻ bytes trả về khi GET URL công khai (lần GET thứ n bắt đầu từ 1) — null = trả đúng bytes đã lưu.</summary>
    public Func<int, byte[], byte[]>? PublicTamper { get; set; }
    /// <summary>Bẻ phản hồi DELETE (trả null = xử lý thường).</summary>
    public Func<string, HttpResponseMessage?>? DeleteOverride { get; set; }
    /// <summary>Chặn DELETE tới khi bị huỷ (test huỷ giữa lúc xoá).</summary>
    public bool DeleteHang { get; set; }
    public TaskCompletionSource DeleteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Chặn GET CDN tới khi bị huỷ (test huỷ giữa chừng).</summary>
    public bool CdnHang { get; set; }
    public TaskCompletionSource CdnEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    /// <summary>Bẻ phản hồi GET URL công khai theo path (trả null = xử lý thường).</summary>
    public Func<string, HttpResponseMessage?>? PublicOverride { get; set; }
    /// <summary>Chặn GET URL công khai tới khi bị huỷ (mô phỏng app tắt giữa lúc up xong và lúc kiểm).</summary>
    public bool PublicHang { get; set; }
    public TaskCompletionSource PublicEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _publicGets;
    private int _seq;

    public Handler Hub => new(this, true);
    public Handler Fetch => new(this, false);

    public HubClient HubClient() =>
        new(new HubClientConfig { Enabled = true, BaseUrl = "http://hub.test", ApiToken = "tok" }, "may-1", Hub);

    public HttpClient FetchClient() => new(Fetch);

    public static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    internal sealed class Handler(FakeMediaWorld w, bool hub) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            lock (w) (hub ? w.HubRequests : w.FetchRequests).Add(req);
            var path = req.RequestUri!.AbsolutePath;
            if (hub)
            {
                if (req.Method == HttpMethod.Put && path == HubRoutes.PublicMedia)
                {
                    if (w.UploadThrow?.Invoke() is { } ex) throw ex;
                    var n = Interlocked.Increment(ref w.Uploads);
                    var bytes = await req.Content!.ReadAsByteArrayAsync(ct);
                    if (w.OnUpload is { } hook) await hook();
                    if (w.UploadOverride?.Invoke(n) is { } o) return o;
                    var ext = bytes[0] == 0x89 ? "png" : "jpg";
                    var name = $"x{Interlocked.Increment(ref w._seq):D4}.{ext}";
                    lock (w) w.Stored[name] = bytes;
                    return Json(HttpStatusCode.OK,
                        $"{{\"path\":\"{HubRoutes.PublicMedia}/{name}\",\"url\":\"{PublicBase}{HubRoutes.PublicMedia}/{name}\","
                        + $"\"expiresAt\":\"{(w.Clock is { } c ? c().AddDays(7).ToString("o") : "2026-11-24T00:00:00Z")}\"}}");
                }
                if (req.Method == HttpMethod.Delete && path.StartsWith(HubRoutes.PublicMedia + "/", StringComparison.Ordinal))
                {
                    var name = path[(HubRoutes.PublicMedia.Length + 1)..];
                    w.DeleteEntered.TrySetResult();
                    if (w.DeleteHang) await Task.Delay(Timeout.Infinite, ct);
                    lock (w) w.Deleted.Add(name);
                    if (w.DeleteOverride?.Invoke(name) is { } o) return o;
                    lock (w) w.Stored.Remove(name);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (req.RequestUri.Host == "down-vn.img.susercontent.com")
            {
                w.CdnEntered.TrySetResult();
                if (w.CdnHang) await Task.Delay(Timeout.Infinite, ct);
                var id = Uri.UnescapeDataString(path["/file/".Length..]);
                if (w.CdnStatus?.Invoke(id) is { } st) return new HttpResponseMessage(st);
                lock (w)
                    return w.Cdn.TryGetValue(id, out var b)
                        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) }
                        : new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (req.RequestUri.GetLeftPart(UriPartial.Authority) == PublicBase)
            {
                w.PublicEntered.TrySetResult();
                if (w.PublicHang) await Task.Delay(Timeout.Infinite, ct);
                if (w.PublicThrow?.Invoke() is { } pex) throw pex;
                if (w.PublicOverride?.Invoke(path) is { } o) return o;
                var name = path[(HubRoutes.PublicMedia.Length + 1)..];
                byte[]? stored;
                lock (w) w.Stored.TryGetValue(name, out stored);
                if (stored is null) return new HttpResponseMessage(HttpStatusCode.NotFound);
                var n = Interlocked.Increment(ref w._publicGets);
                var body = w.PublicTamper?.Invoke(n, stored) ?? stored;
                var content = new ByteArrayContent(body);
                content.Headers.ContentType = new(name.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}

/// <summary>Đồng hồ giả, tiến tay.</summary>
internal sealed class ManualClock(DateTimeOffset start)
{
    public DateTimeOffset Now { get; set; } = start;
    public void Advance(TimeSpan d) => Now += d;
}
