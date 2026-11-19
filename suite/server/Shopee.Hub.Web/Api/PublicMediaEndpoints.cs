using Microsoft.Net.Http.Headers;
using Shopee.Core.Coordination;
using Shopee.Hub.Web.Services;

namespace Shopee.Hub.Web.Api;

/// <summary>
/// Kho ảnh CÔNG KHAI có hạn (<see cref="PublicMediaStore"/>). Upload / xoá cần token Client; GET/HEAD thì KHÔNG (Shopee
/// kéo ảnh không kèm header). Đặt dưới <c>/api/</c> như mọi route mới: đã chắc chắn qua Cloudflare Tunnel (client đang
/// dùng <c>/api/orders</c>, <c>/api/harvested-products/*</c>) và không đụng trang Blazor. Không có route liệt kê.
/// </summary>
public static class PublicMediaEndpoints
{
    public static void MapPublicMediaApi(this WebApplication app)
    {
        // PUT body = bytes ảnh. Chưa cấu hình base URL công khai → 503 TRƯỚC khi đọc body (không ghi gì). Đọc tối đa
        // MaxBytes + 1 để biết "quá cỡ" mà không nạp cả body lớn vào RAM.
        app.MapPut(HubRoutes.PublicMedia, async (HttpRequest req, PublicMediaStore store, CancellationToken ct) =>
        {
            if (!store.HasPublicBaseUrl) return PublicBaseUrlMissing();
            if (req.ContentLength is > PublicMediaLimits.MaxBytes)
                return TooLarge();
            using var ms = new MemoryStream();
            var buf = new byte[64 * 1024];
            int read;
            while ((read = await req.Body.ReadAsync(buf, ct)) > 0)
            {
                ms.Write(buf, 0, read);
                if (ms.Length > PublicMediaLimits.MaxBytes) return TooLarge();
            }
            var r = await store.SaveAsync(ms.ToArray(), ct);
            return r.Status switch
            {
                PublicMediaStore.UploadStatus.Ok => Results.Json(r.Response),
                PublicMediaStore.UploadStatus.Empty => Results.BadRequest(new { error = "empty" }),
                PublicMediaStore.UploadStatus.TooLarge => TooLarge(),
                PublicMediaStore.UploadStatus.UnsupportedType =>
                    Results.Json(new { error = "unsupported-type" }, statusCode: StatusCodes.Status415UnsupportedMediaType),
                PublicMediaStore.UploadStatus.PublicBaseUrlMissing => PublicBaseUrlMissing(),
                PublicMediaStore.UploadStatus.DiskLow => Results.Json(new { error = "disk-low", freeBytes = r.FreeBytes },
                    statusCode: StatusCodes.Status507InsufficientStorage),
                _ => Results.Json(new { error = "media-quota-exceeded", usedBytes = r.UsedBytes },
                    statusCode: StatusCodes.Status507InsufficientStorage),
            };
        }).RequireAuthorization("Client");

        // GET + HEAD công khai (HEAD: cùng header, không body — công cụ/CDN hay dò HEAD trước). Tên sai luật / hết hạn /
        // không có → 404 như nhau (không lộ ảnh nào từng tồn tại). Cache-Control giới hạn theo hạn còn lại (tối đa 1 giờ)
        // để Cloudflare không giữ ảnh quá hạn; 404 thì no-store.
        app.MapMethods(HubRoutes.PublicMedia + "/{name}", [HttpMethods.Get, HttpMethods.Head],
            (string name, HttpContext ctx, PublicMediaStore store) =>
        {
            ctx.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
            if (store.Find(name) is not { } f)
            {
                ctx.Response.Headers[HeaderNames.CacheControl] = "no-store";
                return Results.NotFound();
            }
            var left = (long)Math.Clamp((f.Expires - store.Clock()).TotalSeconds, 0, 3600);
            ctx.Response.Headers[HeaderNames.CacheControl] = $"public, max-age={left}";
            if (HttpMethods.IsHead(ctx.Request.Method))
            {
                ctx.Response.ContentType = f.ContentType;
                try { ctx.Response.ContentLength = new FileInfo(f.Path).Length; }
                catch (IOException) { return Results.NotFound(); }   // job dọn vừa xoá giữa chừng
                return Results.Empty;
            }
            return Results.File(f.Path, f.ContentType);
        }).AllowAnonymous();

        // DELETE (auth Client): bước đăng xoá ảnh khi Shopee đã kéo xong, khỏi chờ hết hạn. 200 = đã xoá; 404 = không có
        // (tên sai luật, đã xoá, hoặc job dọn đã dọn) — client coi 404 cũng là "sạch".
        // CẢNH BÁO: MỘT URL có thể được NHIỀU SP / nhiều file đăng dùng chung (ảnh bìa trùng ảnh phân loại, ảnh bảng size
        // dùng chung mọi SP) — phía gọi phải ĐẾM THAM CHIẾU và chỉ xoá khi không còn SP nào chờ Shopee kéo ảnh đó.
        app.MapDelete(HubRoutes.PublicMedia + "/{name}", async (string name, PublicMediaStore store, CancellationToken ct) =>
            await store.DeleteAsync(name, ct) ? Results.Ok() : Results.NotFound()).RequireAuthorization("Client");
    }

    private static IResult TooLarge() =>
        Results.Json(new { error = "too-large", max = PublicMediaLimits.MaxBytes }, statusCode: StatusCodes.Status413PayloadTooLarge);

    private static IResult PublicBaseUrlMissing() =>
        Results.Json(new { error = "public-base-url-missing" }, statusCode: StatusCodes.Status503ServiceUnavailable);
}
