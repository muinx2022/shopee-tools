using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Net.Http.Headers;
using Shopee.Core.Coordination;
using Shopee.Hub.Web.Services;

namespace Shopee.Hub.Web.Api;

/// <summary>
/// Kho ẢNH GỐC (<see cref="SourceImageStore"/>, GĐ9) + link ảnh ĐỔI MD5 công khai. Mọi route <c>/api/source-images*</c>
/// cần token Client (như <c>/api/harvested-products/*</c>). Riêng <c>GET/HEAD /api/v/{token}.{ext}</c> KHÔNG auth: Shopee
/// kéo ảnh không kèm header — Hub giải + kiểm token MÃ HOÁ (<see cref="SourceImageLinkSigner"/>), đổi MD5 ảnh gốc theo
/// (shop, id) NGAY lúc gọi bằng <see cref="ImageMd5Changer"/> (tất định, cùng thuật toán app Upload) rồi trả bytes. KHÔNG
/// ghi file nào, không hạn: link sống chừng nào ảnh gốc còn đúng bytes đó.
/// </summary>
public static class SourceImageEndpoints
{
    /// <summary>Tên policy rate limiter của link công khai (đăng ký ở Program.cs: token bucket theo
    /// <see cref="ClientIp"/>, mức <c>HubOptions.SourceImagePublicBurst</c> / <c>SourceImagePublicRatePerMinute</c>).</summary>
    public const string PublicRatePolicy = "source-image-public";

    /// <summary>IP để đếm tần suất: <c>CF-Connecting-IP</c> CHỈ được tin khi kết nối đến từ loopback (cloudflared chạy cùng
    /// máy) và là IP hợp lệ; còn lại dùng <c>RemoteIpAddress</c> — đã qua UseForwardedHeaders, vốn chỉ tin X-Forwarded-For từ
    /// proxy loopback. Request từ ngoài tự gắn CF-Connecting-IP để đổi IP liên tục KHÔNG né được giới hạn.</summary>
    public static string ClientIp(HttpContext ctx)
    {
        var remote = ctx.Connection.RemoteIpAddress;
        if (remote is not null && IPAddress.IsLoopback(remote)
            && IPAddress.TryParse(ctx.Request.Headers["CF-Connecting-IP"].ToString().Trim(), out var cf))
            return cf.ToString();
        return remote?.ToString() ?? "?";
    }

    /// <summary>Đăng ký policy <see cref="PublicRatePolicy"/>: token bucket theo <see cref="ClientIp"/> (sức chứa
    /// <see cref="HubOptions.SourceImagePublicBurst"/>, nạp <see cref="HubOptions.SourceImagePublicRatePerMinute"/>/phút), không
    /// xếp hàng. Bị chặn → 429 + <c>Retry-After</c> (giây, theo limiter hoặc chu kỳ nạp) + <c>Cache-Control: no-store</c>
    /// (Cloudflare không giữ 429) + log qua <see cref="PublicRateRejectLog"/> (gộp 1 dòng / IP / phút).</summary>
    public static void ConfigurePublicRateLimit(Microsoft.AspNetCore.RateLimiting.RateLimiterOptions o, HubOptions opts)
    {
        var burst = Math.Max(1, opts.SourceImagePublicBurst);
        var perMin = Math.Max(1, opts.SourceImagePublicRatePerMinute);
        var (period, tokens) = perMin >= 60
            ? (TimeSpan.FromSeconds(1), (perMin + 59) / 60)
            : (TimeSpan.FromSeconds(60.0 / perMin), 1);
        o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        o.AddPolicy(PublicRatePolicy, ctx => System.Threading.RateLimiting.RateLimitPartition.GetTokenBucketLimiter(
            ClientIp(ctx), _ => new System.Threading.RateLimiting.TokenBucketRateLimiterOptions
            {
                TokenLimit = burst,
                TokensPerPeriod = tokens,
                ReplenishmentPeriod = period,
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
        o.OnRejected = (rc, _) =>
        {
            var http = rc.HttpContext;
            var retry = rc.Lease.TryGetMetadata(System.Threading.RateLimiting.MetadataName.RetryAfter, out var ra) && ra > TimeSpan.Zero
                ? ra : period;
            http.Response.Headers[HeaderNames.RetryAfter] = Math.Max(1, (long)Math.Ceiling(retry.TotalSeconds))
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            http.Response.Headers[HeaderNames.CacheControl] = "no-store";
            http.RequestServices.GetService<PublicRateRejectLog>()?.Rejected(ClientIp(http), http.Request.Path.Value ?? "");
            return ValueTask.CompletedTask;
        };
    }

    /// <summary>Câu cảnh báo cố định trong response + log của DELETE-all.</summary>
    internal const string DeleteAllWarning = "Link ảnh của các file đã xuất sẽ hỏng (ảnh gốc không còn để đổi MD5).";

    public static void MapSourceImageApi(this WebApplication app)
    {
        var api = app.MapGroup("").RequireAuthorization("Client");

        // ── POST missing: id nào kho gốc CHƯA có ảnh DÙNG ĐƯỢC (không file / file hỏng / ghi dở). id sai luật → 400 cả lời
        //    gọi (không đoán phần đúng). ──
        api.MapPost(HubRoutes.SourceImagesMissing, (SourceImageIdsRequest? r, SourceImageStore store) =>
        {
            if (CheckIds(r?.Ids) is { } bad) return bad;
            return Results.Json(new SourceImageMissingResponse(r!.Ids!.Distinct(StringComparer.Ordinal)
                .Where(id => store.Usable(id) is null).ToList()));
        });

        // ── PUT 1 ảnh gốc, idempotent theo id (bản đang giữ dùng được → existed; hỏng → thay, replaced). id sai → 400 TRƯỚC
        //    khi đọc body; đọc tối đa MaxBytes + 1. Ảnh không dùng được: WebP / lạ → 415, hỏng cấu trúc → 400 corrupt-image. ──
        api.MapPut(HubRoutes.SourceImages + "/{id}", async (string id, HttpRequest req, SourceImageStore store,
            CancellationToken ct) =>
        {
            if (!SourceImageLimits.IsValidId(id)) return BadId(id);
            if (req.ContentLength is > SourceImageLimits.MaxBytes) return TooLarge();
            using var ms = new MemoryStream();
            var buf = new byte[64 * 1024];
            int read;
            while ((read = await req.Body.ReadAsync(buf, ct)) > 0)
            {
                ms.Write(buf, 0, read);
                if (ms.Length > SourceImageLimits.MaxBytes) return TooLarge();
            }
            var r = await store.SaveAsync(id, ms.ToArray(), ct);
            return r.Status switch
            {
                SourceImageStore.SaveStatus.Ok or SourceImageStore.SaveStatus.Existed or SourceImageStore.SaveStatus.Replaced =>
                    Results.Json(new SourceImagePutResponse(id, r.Status == SourceImageStore.SaveStatus.Existed,
                        r.Status == SourceImageStore.SaveStatus.Replaced, r.Size, SourceImageStore.ContentTypeOf(r.Ext))),
                SourceImageStore.SaveStatus.Corrupt => Results.BadRequest(new { error = "corrupt-image" }),
                SourceImageStore.SaveStatus.BadId => BadId(id),
                SourceImageStore.SaveStatus.Empty => Results.BadRequest(new { error = "empty" }),
                SourceImageStore.SaveStatus.TooLarge => TooLarge(),
                SourceImageStore.SaveStatus.UnsupportedType =>
                    Results.Json(new { error = "unsupported-type" }, statusCode: StatusCodes.Status415UnsupportedMediaType),
                SourceImageStore.SaveStatus.DiskLow => Results.Json(new { error = "disk-low", freeBytes = r.FreeBytes },
                    statusCode: StatusCodes.Status507InsufficientStorage),
                _ => Results.Json(new { error = "source-quota-exceeded", usedBytes = r.UsedBytes, maxBytes = store.MaxTotalBytes },
                    statusCode: StatusCodes.Status507InsufficientStorage),
            };
        });

        // ── GET/HEAD 1 ảnh gốc NGUYÊN BYTES (auth Client — để Harvest/Upload kiểm, không phải cho Shopee). Có file mà không
        //    dùng được vẫn trả (Content-Type application/octet-stream) để còn soi. ──
        api.MapMethods(HubRoutes.SourceImages + "/{id}", [HttpMethods.Get, HttpMethods.Head],
            (string id, HttpContext ctx, SourceImageStore store) =>
        {
            if (!SourceImageLimits.IsValidId(id)) return BadId(id);
            var path = Path.Combine(store.Dir, id);
            long len;
            try { len = new FileInfo(path).Length; }
            catch (FileNotFoundException) { return Results.NotFound(); }
            var type = SourceImageStore.ContentTypeOf(store.Usable(id)?.Ext);
            if (HttpMethods.IsHead(ctx.Request.Method))
            {
                ctx.Response.ContentType = type;
                ctx.Response.ContentLength = len;
                return Results.Empty;
            }
            return Results.File(path, type);
        });

        // ── DELETE 1 ảnh gốc (meta + ảnh, trong khoá ghi). 200 = đã xoá; 404 = không có. Link đã phát của ảnh đó hỏng. ──
        api.MapDelete(HubRoutes.SourceImages + "/{id}", async (string id, SourceImageStore store, CancellationToken ct) =>
        {
            if (!SourceImageLimits.IsValidId(id)) return BadId(id);
            return await store.DeleteAsync(id, ct) ? Results.Ok(new { deleted = true }) : Results.NotFound();
        });

        // ── POST links: link công khai (token mã hoá shop + id + dấu vân tay nội dung) cho từng id có ảnh DÙNG ĐƯỢC; id chưa
        //    có / hỏng → missing. Không ghi gì. ──
        api.MapPost(HubRoutes.SourceImagesLinks, (SourceImageLinksRequest? r, SourceImageStore store, PublicMediaStore media,
            SourceImageLinkSigner signer) =>
        {
            if (!media.HasPublicBaseUrl)
                return Results.Json(new { error = "public-base-url-missing" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            var shop = r?.ShopKey;
            if (string.IsNullOrEmpty(shop) || shop.Length > SourceImageLimits.MaxShopKeyLength)
                return Results.BadRequest(new { error = "shop-key", max = SourceImageLimits.MaxShopKeyLength });
            if (CheckIds(r!.Ids) is { } bad) return bad;
            var items = new List<SourceImageLink>();
            var missing = new List<string>();
            foreach (var id in r.Ids!.Distinct(StringComparer.Ordinal))
            {
                if (store.Usable(id) is not { } p) { missing.Add(id); continue; }
                items.Add(new SourceImageLink(id,
                    $"{media.PublicBaseUrl}{HubRoutes.PublicSourceImage}/{signer.Sign(shop, id, p.Fingerprint)}.{p.Ext}"));
            }
            return Results.Json(new SourceImageLinksResponse(items, missing));
        });

        // ── GET stats: ổ đĩa + kho gốc + kho ảnh tạm. Kho gốc đếm theo bộ đếm trong bộ nhớ, kho tạm nhớ 30 giây. ──
        api.MapGet(HubRoutes.SourceImagesStats, async (SourceImageStore store, PublicMediaStore media, CancellationToken ct) =>
        {
            var (srcCount, srcBytes) = await store.GetStatsAsync(ct);
            var (mediaCount, mediaBytes) = await media.GetStatsAsync(ct);
            long total, free;
            try { total = store.TotalBytes(); free = store.FreeBytes(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { total = -1; free = -1; }
            return Results.Json(new SourceImageStatsResponse(total, free, srcCount, srcBytes, store.MaxTotalBytes,
                mediaCount, mediaBytes, media.MaxTotalBytes));
        });

        // ── DELETE-all: xoá SẠCH kho gốc. Bắt buộc body {confirm:"XOA-HET-ANH-GOC"} — thiếu/sai/không phải JSON → 400, không
        //    xoá gì. KHÔNG đụng kho ảnh tạm công khai (file đã xuất dùng ảnh bảng size ở đó vẫn chạy tới khi hết hạn). ──
        api.MapDelete(HubRoutes.SourceImages, async (HttpContext ctx, SourceImageStore store, ILoggerFactory lf,
            CancellationToken ct) =>
        {
            SourceImageDeleteAllRequest? body;
            try { body = await ctx.Request.ReadFromJsonAsync<SourceImageDeleteAllRequest>(ct); }
            catch (JsonException) { body = null; }
            catch (InvalidOperationException) { body = null; }   // thiếu / sai Content-Type
            if (!string.Equals(body?.Confirm, SourceImageLimits.DeleteAllConfirm, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "confirm-required", confirm = SourceImageLimits.DeleteAllConfirm });
            var (deleted, remaining, freed) = await store.DeleteAllAsync(ct);
            var machine = ctx.Request.Headers["X-Machine-Id"].ToString();
            lf.CreateLogger("SourceImages").LogWarning(
                "source-images: XOÁ SẠCH kho ảnh gốc theo yêu cầu máy '{Machine}' lúc {At:o}: xoá {Deleted} ảnh, giải phóng "
                + "{Freed} byte, còn {Remaining}. {Warning}",
                machine.Length > 0 ? machine : "(không rõ)", DateTimeOffset.UtcNow, deleted, freed, remaining, DeleteAllWarning);
            return Results.Json(new SourceImageDeleteAllResponse(deleted, remaining, freed, DeleteAllWarning));
        });

        // ── GET/HEAD link ảnh đổi MD5 CÔNG KHAI. Mọi lỗi (đuôi lạ, token sai/sửa, ảnh gốc không có / không dùng được / đã bị
        //    THAY — dấu vân tay trong token khác meta / đổi kiểu / không đổi MD5 được) → 404 như nhau, no-store. Bytes tất định theo (shop, id, ảnh gốc) nên cache dài + immutable; ETag =
        //    MD5 của bytes trả (If-None-Match khớp → 304). Link có query string → 404: Cloudflare mặc định coi query là
        //    khoá cache khác, chặn hẳn để không ai thêm "?x=…" mà né cache dội thẳng vào Hub. Rate limit theo IP
        //    (PublicRatePolicy) → 429. ──
        app.MapMethods(HubRoutes.PublicSourceImage + "/{file}", [HttpMethods.Get, HttpMethods.Head],
            async (string file, HttpContext ctx, SourceImageStore store, SourceImageLinkSigner signer, CancellationToken ct) =>
        {
            ctx.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
            if (ctx.Request.QueryString.HasValue) return NotFoundNoStore(ctx);
            var dot = file.LastIndexOf('.');
            var ext = dot > 0 ? file[(dot + 1)..] : "";
            if (ext is not ("jpg" or "png") || signer.Verify(file[..dot]) is not { } link
                || store.Usable(link.Id) is not { } info || info.Ext != ext
                || !CryptographicOperations.FixedTimeEquals(info.Fingerprint, link.Fingerprint)
                || await store.ReadAsync(link.Id, ct) is not { } src || src.Length != info.Size)
                return NotFoundNoStore(ctx);
            ImageMd5Result changed;
            try { changed = ImageMd5Changer.Change(src, link.ShopKey, link.Id); }
            catch (ImageMd5Exception) { return NotFoundNoStore(ctx); }

            var etag = "\"" + changed.Md5 + "\"";
            ctx.Response.Headers[HeaderNames.CacheControl] = "public, max-age=31536000, immutable";
            ctx.Response.Headers[HeaderNames.ETag] = etag;
            if (ctx.Request.Headers.IfNoneMatch.Any(v => v is not null && (v == etag || v == "*")))
                return Results.StatusCode(StatusCodes.Status304NotModified);
            if (HttpMethods.IsHead(ctx.Request.Method))
            {
                ctx.Response.ContentType = changed.ContentType;
                ctx.Response.ContentLength = changed.Bytes.Length;
                return Results.Empty;
            }
            return Results.Bytes(changed.Bytes, changed.ContentType);
        }).AllowAnonymous().RequireRateLimiting(PublicRatePolicy);
    }

    /// <summary>Kiểm danh sách id: thiếu → 400 <c>ids-required</c>; quá trần (đếm cả trùng) → 400 <c>too-many-ids</c>; có id
    /// sai luật → 400 <c>bad-id</c> kèm id đầu tiên sai. Hợp lệ → null.</summary>
    private static IResult? CheckIds(List<string>? ids)
    {
        if (ids is null) return Results.BadRequest(new { error = "ids-required" });
        if (ids.Count > SourceImageLimits.MaxIdsPerCall)
            return Results.BadRequest(new { error = "too-many-ids", max = SourceImageLimits.MaxIdsPerCall });
        var bad = ids.FirstOrDefault(id => !SourceImageLimits.IsValidId(id));
        return bad is null && ids.All(id => id is not null) ? null : BadId(bad);
    }

    private static IResult BadId(string? id) => Results.BadRequest(new { error = "bad-id", id });

    private static IResult TooLarge() =>
        Results.Json(new { error = "too-large", max = SourceImageLimits.MaxBytes }, statusCode: StatusCodes.Status413PayloadTooLarge);

    private static IResult NotFoundNoStore(HttpContext ctx)
    {
        ctx.Response.Headers[HeaderNames.CacheControl] = "no-store";
        ctx.Response.Headers.Remove(HeaderNames.ETag);
        return Results.NotFound();
    }
}
