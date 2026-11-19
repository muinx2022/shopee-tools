using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shopee.Core.Coordination;
using Shopee.Hub.Web.Services;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// <b>Kho ảnh công khai có hạn (<c>/api/media</c>)</b> qua host thật: upload cần token, GET thì không; magic bytes chứ
/// không Content-Type; hạn 404 + job dọn; trần dung lượng. Mỗi test dựng host riêng (thư mục dữ liệu riêng) vì trần
/// dung lượng tính trên cả thư mục.
/// </summary>
public sealed class PublicMediaHttpTests
{
    private const string Token = "test-token-media";

    private sealed class MediaHubApp : WebApplicationFactory<Program>
    {
        public const string BaseUrl = "https://api.example.test";
        private readonly int _maxMb;
        private readonly int _minFreeMb;
        private readonly string? _baseUrl;
        public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "hub-media-" + Guid.NewGuid().ToString("N"));

        /// <summary><paramref name="minFreeMb"/> mặc định 0: test không được phụ thuộc đĩa thật của máy chạy test (ca
        /// đĩa đầy giả bằng <see cref="PublicMediaStore.FreeBytes"/>).</summary>
        public MediaHubApp(int maxMb = 64, string? baseUrl = BaseUrl + "/", int minFreeMb = 0)
        { _maxMb = maxMb; _baseUrl = baseUrl; _minFreeMb = minFreeMb; }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HUB_PUBLIC_BASE_URL")))
                throw new InvalidOperationException("Bỏ biến HUB_PUBLIC_BASE_URL trước khi chạy test.");
            Directory.CreateDirectory(DataDir);
            builder.UseSetting("Hub:DataDir", DataDir);
            builder.UseSetting("Hub:PgConn", "");
            builder.UseSetting("Hub:Media:MaxTotalMb", _maxMb.ToString());
            builder.UseSetting("Hub:Media:MinFreeMb", _minFreeMb.ToString());
            if (_baseUrl is not null) builder.UseSetting("Hub:PublicBaseUrl", _baseUrl);
        }

        public HttpClient KhachCoToken()
        {
            var c = CreateClient();
            Services.GetRequiredService<HubDatabase>().SetSetting(SettingKeys.ApiToken, Token);
            c.DefaultRequestHeaders.Add("X-Api-Token", Token);
            c.DefaultRequestHeaders.Add("X-Machine-Id", "may-media");
            return c;
        }

        public PublicMediaStore Store => Services.GetRequiredService<PublicMediaStore>();

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { try { Directory.Delete(DataDir, true); } catch { } }
        }
    }

    private static byte[] Jpg(int size)
    {
        var b = new byte[size];
        Random.Shared.NextBytes(b);
        b[0] = 0xFF; b[1] = 0xD8; b[2] = 0xFF; b[3] = 0xE0;
        return b;
    }

    private static byte[] Png(int size)
    {
        var b = new byte[size];
        Random.Shared.NextBytes(b);
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        return b;
    }

    private static async Task<HttpResponseMessage> Put(HttpClient c, byte[] data, string contentType = "application/octet-stream")
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await c.PutAsync(HubRoutes.PublicMedia, content);
    }

    private static async Task<PublicMediaUploadResponse> PutOk(HttpClient c, byte[] data)
    {
        var res = await Put(c, data);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<PublicMediaUploadResponse>())!;
    }

    [Fact]
    public async Task UploadJpg_GetKhongToken_DungBytesVaContentType()
    {
        using var app = new MediaHubApp();
        var c = app.KhachCoToken();
        var anh = Jpg(50_000);

        var up = await PutOk(c, anh);

        Assert.Matches(@"^/api/media/[0-9]+-[0-9a-f]{32}\.jpg$", up.Path);
        Assert.Equal(MediaHubApp.BaseUrl + up.Path, up.Url);   // base URL cấu hình có '/' cuối → không nhân đôi
        Assert.InRange(up.ExpiresAt, DateTimeOffset.UtcNow.AddDays(7).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(7).AddMinutes(1));
        using var anon = app.CreateClient();
        var get = await anon.GetAsync(up.Path);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/jpeg", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(anh, await get.Content.ReadAsByteArrayAsync());
        Assert.True(get.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromHours(1), get.Headers.CacheControl?.MaxAge);   // còn 7 ngày → kẹp trần 1 giờ
    }

    [Fact]
    public async Task CacheControl_200MaxAgeKhongQuaHanConLai_404NoStore()
    {
        using var app = new MediaHubApp();
        var c = app.KhachCoToken();
        var up = await PutOk(c, Jpg(1000));
        using var anon = app.CreateClient();
        app.Store.Clock = () => up.ExpiresAt.AddSeconds(-90);   // còn 90 giây

        var gan = await anon.GetAsync(up.Path);
        var head = await anon.SendAsync(new HttpRequestMessage(HttpMethod.Head, up.Path));
        var sai = await anon.GetAsync(up.Path.Replace(".jpg", ".png"));
        app.Store.Clock = () => up.ExpiresAt;
        var het = await anon.GetAsync(up.Path);

        Assert.Equal(HttpStatusCode.OK, gan.StatusCode);
        Assert.InRange(gan.Headers.CacheControl!.MaxAge!.Value.TotalSeconds, 0, 90);
        Assert.InRange(head.Headers.CacheControl!.MaxAge!.Value.TotalSeconds, 0, 90);
        Assert.Equal(HttpStatusCode.NotFound, sai.StatusCode);
        Assert.True(sai.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.NotFound, het.StatusCode);
        Assert.True(het.Headers.CacheControl?.NoStore);
        Assert.Null(het.Headers.CacheControl?.MaxAge);
    }

    [Theory]
    [InlineData("https://api.schedra.net", "https://api.schedra.net")]
    [InlineData("  https://api.schedra.net/  ", "https://api.schedra.net")]
    [InlineData("https://API.schedra.net/hub/", "https://api.schedra.net/hub")]
    [InlineData("http://api.schedra.net", null)]
    [InlineData("api.schedra.net", null)]
    [InlineData("https://u:p@api.schedra.net", null)]
    [InlineData("https://api.schedra.net?x", null)]
    [InlineData("", null)]
    public void NormalizePublicBaseUrl_ChiNhanHttpsTuyetDoi(string raw, string? ketQua) =>
        Assert.Equal(ketQua, PublicMediaStore.NormalizePublicBaseUrl(raw));

    [Fact]
    public async Task UploadPng_DuoiPng_ContentTypePng()
    {
        using var app = new MediaHubApp();
        var c = app.KhachCoToken();

        var up = await PutOk(c, Png(10_000));

        Assert.EndsWith(".png", up.Path);
        using var anon = app.CreateClient();
        var get = await anon.GetAsync(up.Path);
        Assert.Equal("image/png", get.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UploadKhongToken_401()
    {
        using var app = new MediaHubApp();
        app.KhachCoToken();   // seed token
        using var anon = app.CreateClient();

        var res = await Put(anon, Jpg(1000));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Empty(Directory.EnumerateFiles(app.Store.Dir));
    }

    [Fact]
    public async Task TokenSai_SuaHan_TenLa_404_KhongLietKeDuoc()
    {
        using var app = new MediaHubApp();
        var c = app.KhachCoToken();
        var up = await PutOk(c, Jpg(1000));
        var ten = up.Path[(HubRoutes.PublicMedia.Length + 1)..];
        var dash = ten.IndexOf('-');
        var tok = ten[(dash + 1)..ten.IndexOf('.')];
        using var anon = app.CreateClient();

        var sai = new[]
        {
            $"{HubRoutes.PublicMedia}/{ten[..dash]}-{new string('0', 32)}.jpg",                   // token sai
            $"{HubRoutes.PublicMedia}/{long.Parse(ten[..dash]) + 86400}-{tok}.jpg",                // sửa hạn
            $"{HubRoutes.PublicMedia}/{ten[..dash]}-{tok}.png",                                    // sai đuôi
            $"{HubRoutes.PublicMedia}/{ten[..dash]}-{tok.ToUpperInvariant()}.jpg",                 // chữ hoa
            $"{HubRoutes.PublicMedia}/..%2Fhub.db",
        };
        foreach (var u in sai)
            Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(u)).StatusCode);

        Assert.NotEqual(HttpStatusCode.OK, (await anon.GetAsync(HubRoutes.PublicMedia)).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await anon.GetAsync(HubRoutes.PublicMedia + "/")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await c.GetAsync(HubRoutes.PublicMedia)).StatusCode);
        // Không lọt vào manifest kho file (client có token cũng không liệt kê được).
        Assert.DoesNotContain(tok, await c.GetStringAsync(HubRoutes.Manifest));
    }

    [Fact]
    public async Task HetHan_404_VaBiJobDon()
    {
        using var app = new MediaHubApp();
        var c = app.KhachCoToken();
        var up = await PutOk(c, Jpg(1000));
        using var anon = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(up.Path)).StatusCode);

        app.Store.Clock = () => up.ExpiresAt;   // đúng mốc hết hạn

        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(up.Path)).StatusCode);
        Assert.Single(Directory.EnumerateFiles(app.Store.Dir));   // GET không tự xoá; job dọn mới xoá
        Assert.Equal(1, await app.Store.SweepExpiredAsync(CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(app.Store.Dir));
    }

    [Fact]
    public async Task JobDon_KhongXoaAnhConHan()
    {
        using var app = new MediaHubApp();
        var c = app.KhachCoToken();
        var up = await PutOk(c, Jpg(1000));
        app.Store.Clock = () => up.ExpiresAt.AddSeconds(-5);

        Assert.Equal(0, await app.Store.SweepExpiredAsync(CancellationToken.None));
        using var anon = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(up.Path)).StatusCode);
    }

    [Fact]
    public async Task QuaCo_413_DungTran2MB_Ok()
    {
        using var app = new MediaHubApp();
        var c = app.KhachCoToken();

        var qua = await Put(c, Jpg(PublicMediaLimits.MaxBytes + 1));
        var vua = await Put(c, Jpg(PublicMediaLimits.MaxBytes));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, qua.StatusCode);
        Assert.Contains("too-large", await qua.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, vua.StatusCode);
        Assert.Single(Directory.EnumerateFiles(app.Store.Dir));
    }

    [Fact]
    public async Task SaiDinhDang_415_KeCaKhaiContentTypeAnh_Rong400()
    {
        using var app = new MediaHubApp();
        var c = app.KhachCoToken();
        var gif = "GIF89a"u8.ToArray().Concat(new byte[100]).ToArray();
        var text = "<html>hi</html>"u8.ToArray();

        var r1 = await Put(c, gif, "image/jpeg");
        var r2 = await Put(c, text, "image/png");
        var r3 = await Put(c, []);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, r1.StatusCode);
        Assert.Contains("unsupported-type", await r1.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, r2.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, r3.StatusCode);
        Assert.Empty(Directory.EnumerateFiles(app.Store.Dir));
    }

    [Fact]
    public async Task VuotTran_507_KhongXoaAnhConHan_HetHanThiNhanLai()
    {
        using var app = new MediaHubApp(maxMb: 1);
        var c = app.KhachCoToken();
        var dau = await PutOk(c, Jpg(700_000));

        var vuot = await Put(c, Jpg(700_000));

        Assert.Equal(HttpStatusCode.InsufficientStorage, vuot.StatusCode);
        Assert.Contains("media-quota-exceeded", await vuot.Content.ReadAsStringAsync());
        using var anon = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(dau.Path)).StatusCode);   // ảnh cũ còn nguyên

        app.Store.Clock = () => dau.ExpiresAt.AddSeconds(1);   // ảnh đầu hết hạn → chỗ được giải phóng
        Assert.Equal(HttpStatusCode.OK, (await Put(c, Jpg(700_000))).StatusCode);
    }

    [Fact]
    public async Task HaiAnhGiongNhau_HaiTokenKhacNhau()
    {
        using var app = new MediaHubApp();
        var c = app.KhachCoToken();
        var anh = Jpg(1000);

        var a = await PutOk(c, anh);
        var b = await PutOk(c, anh);

        Assert.NotEqual(a.Path, b.Path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://api.schedra.net")]
    [InlineData("api.schedra.net")]
    [InlineData("https://api.schedra.net/?x=1")]
    [InlineData("https://api.schedra.net/#a")]
    [InlineData("rác :// lung tung")]
    public async Task BaseUrlCongKhaiThieuHoacSaiDang_503_KhongGhiFile(string? baseUrl)
    {
        using var app = new MediaHubApp(baseUrl: baseUrl);
        var c = app.KhachCoToken();

        var res = await Put(c, Jpg(1000));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Contains("public-base-url-missing", await res.Content.ReadAsStringAsync());
        Assert.Empty(Directory.EnumerateFiles(app.Store.Dir));
    }

    [Fact]
    public async Task DiaSapDay_507DiskLow_KhongGhiFile_KhongDoDuoc_CungTuChoi()
    {
        using var app = new MediaHubApp(minFreeMb: 100);
        var c = app.KhachCoToken();
        var anh = Jpg(10_000);

        app.Store.FreeBytes = () => 100L * 1024 * 1024 + anh.Length - 1;   // ghi xong còn thiếu 1 byte so với ngưỡng
        var thieu = await Put(c, anh);
        app.Store.FreeBytes = () => throw new IOException("statvfs");
        var khongDo = await Put(c, anh);
        app.Store.FreeBytes = () => 100L * 1024 * 1024 + anh.Length;      // vừa đủ ngưỡng
        var du = await Put(c, anh);

        Assert.Equal(HttpStatusCode.InsufficientStorage, thieu.StatusCode);
        Assert.Contains("disk-low", await thieu.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.InsufficientStorage, khongDo.StatusCode);
        Assert.Contains("disk-low", await khongDo.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, du.StatusCode);
        Assert.Single(Directory.EnumerateFiles(app.Store.Dir));
    }

    [Fact]
    public async Task Head_KhongToken_HeaderNhuGet_KhongBody_TenSai404()
    {
        using var app = new MediaHubApp();
        var c = app.KhachCoToken();
        var anh = Jpg(12_345);
        var up = await PutOk(c, anh);
        using var anon = app.CreateClient();

        var head = await anon.SendAsync(new HttpRequestMessage(HttpMethod.Head, up.Path));
        var sai = await anon.SendAsync(new HttpRequestMessage(HttpMethod.Head, up.Path.Replace(".jpg", ".png")));

        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal("image/jpeg", head.Content.Headers.ContentType?.MediaType);
        Assert.Equal(anh.Length, head.Content.Headers.ContentLength);
        Assert.Contains("max-age=", head.Headers.CacheControl?.ToString());
        Assert.Empty(await head.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, sai.StatusCode);
    }

    [Fact]
    public async Task Delete_CanToken_XoaXongGet404_XoaLai404()
    {
        using var app = new MediaHubApp();
        var c = app.KhachCoToken();
        var up = await PutOk(c, Jpg(1000));
        using var anon = app.CreateClient();

        var khongToken = await anon.DeleteAsync(up.Path);
        Assert.Equal(HttpStatusCode.Unauthorized, khongToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(up.Path)).StatusCode);   // vẫn còn

        Assert.Equal(HttpStatusCode.OK, (await c.DeleteAsync(up.Path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(up.Path)).StatusCode);
        Assert.Empty(Directory.EnumerateFiles(app.Store.Dir));
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync(up.Path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync(HubRoutes.PublicMedia + "/..%2Fhub.db")).StatusCode);
    }
}
