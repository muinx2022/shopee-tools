using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shopee.Core.Coordination;
using Shopee.Hub.Web.Api;
using Shopee.Hub.Web.Services;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// <b>Kho ảnh gốc (<c>/api/source-images*</c>) + link ảnh đổi MD5 công khai (<c>/api/v/…</c>)</b> qua host thật (GĐ9):
/// auth, luật id (chặn đi ra ngoài thư mục), PUT idempotent + song song, trần 507, missing, links, link trả bytes khớp
/// <c>ImageMd5Changer</c> của client, chữ ký, khoá ký bền qua khởi động lại, stats, DELETE-all. Mỗi test dựng host riêng
/// (thư mục dữ liệu riêng) vì trần và bộ đếm tính trên cả thư mục.
/// </summary>
public sealed class SourceImageHttpTests
{
    private const string Token = "test-token-source";
    private const string BaseUrl = "https://api.example.test";

    private sealed class SourceHubApp : WebApplicationFactory<Program>
    {
        private readonly int _maxMb;
        private readonly string? _baseUrl;
        private readonly bool _keepDir;
        private readonly int? _ratePerMin;
        public string DataDir { get; }

        public SourceHubApp(int maxMb = 64, string? baseUrl = BaseUrl + "/", string? dataDir = null, bool keepDir = false,
            int? ratePerMin = null)
        {
            _maxMb = maxMb; _baseUrl = baseUrl; _keepDir = keepDir; _ratePerMin = ratePerMin;
            DataDir = dataDir ?? Path.Combine(Path.GetTempPath(), "hub-src-" + Guid.NewGuid().ToString("N"));
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HUB_PUBLIC_BASE_URL")))
                throw new InvalidOperationException("Bỏ biến HUB_PUBLIC_BASE_URL trước khi chạy test.");
            Directory.CreateDirectory(DataDir);
            builder.UseSetting("Hub:DataDir", DataDir);
            builder.UseSetting("Hub:PgConn", "");
            builder.UseSetting("Hub:SourceImages:MaxTotalMb", _maxMb.ToString());
            builder.UseSetting("Hub:Media:MaxTotalMb", "64");
            builder.UseSetting("Hub:Media:MinFreeMb", "0");   // đĩa đầy giả bằng FreeBytes, không phụ thuộc máy test
            if (_baseUrl is not null) builder.UseSetting("Hub:PublicBaseUrl", _baseUrl);
            // ratePerMin ở test = SỨC CHỨA bucket; nạp lại 1 token/phút để kết quả tất định.
            if (_ratePerMin is int r)
            {
                builder.UseSetting("Hub:SourceImages:PublicBurst", r.ToString());
                builder.UseSetting("Hub:SourceImages:PublicRatePerMinute", "1");
            }
            builder.ConfigureServices(sv => sv.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(Logs));
        }

        /// <summary>Log Hub ghi ra trong test (để kiểm log 429 gộp theo IP).</summary>
        public LogCapture Logs { get; } = new();

        public HttpClient KhachCoToken()
        {
            var c = CreateClient();
            Services.GetRequiredService<HubDatabase>().SetSetting(SettingKeys.ApiToken, Token);
            c.DefaultRequestHeaders.Add("X-Api-Token", Token);
            c.DefaultRequestHeaders.Add("X-Machine-Id", "may-src");
            return c;
        }

        public SourceImageStore Store => Services.GetRequiredService<SourceImageStore>();
        public string SrcDir => Path.Combine(DataDir, "source-images");
        public string MediaDir => Path.Combine(DataDir, "media");

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && !_keepDir) { try { Directory.Delete(DataDir, true); } catch { } }
        }
    }

    private const string IdA = "vn-11134207-7r98o-lx1abcdefghi12";
    private const string IdB = "e5f3a1b2c3d4e5f60718293a4b5c6d7e";
    private const string IdC = "a5f3a1b2c3d4e5f60718293a4b5c6d7e";
    private static string Id(int n) => $"vn-11134207-test-{n:D6}";

    /// <summary>JPEG giả DÙNG ĐƯỢC: không APPn (FF D8 FF DB …) → <c>ImageMd5Changer</c> chèn COM ngay sau SOI, không đọc độ dài
    /// rác; kết thúc EOI FF D9.</summary>
    private static byte[] Jpg(int size)
    {
        var b = new byte[size];
        Random.Shared.NextBytes(b);
        b[0] = 0xFF; b[1] = 0xD8; b[2] = 0xFF; b[3] = 0xDB;
        b[^2] = 0xFF; b[^1] = 0xD9;
        return b;
    }

    /// <summary>PNG giả DÙNG ĐƯỢC: chữ ký + chunk đầu là IHDR dài 13 (đủ cho <c>ImageMd5Changer</c> chèn tEXt), rác ở giữa,
    /// kết thúc bằng chunk IEND.</summary>
    private static byte[] Png(int size)
    {
        var b = new byte[size];
        Random.Shared.NextBytes(b);
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(b, 0);
        new byte[] { 0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0xAE, 0x42, 0x60, 0x82 }.CopyTo(b, size - 12);
        return b;
    }

    private static Task<HttpResponseMessage> Put(HttpClient c, string id, byte[] data)
    {
        var content = new ByteArrayContent(data);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return c.PutAsync(HubRoutes.SourceImages + "/" + id, content);
    }

    private static async Task<SourceImagePutResponse> PutOk(HttpClient c, string id, byte[] data)
    {
        var res = await Put(c, id, data);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<SourceImagePutResponse>())!;
    }

    private static async Task<SourceImageLinksResponse> Links(HttpClient c, string shop, params string[] ids)
    {
        var res = await c.PostAsJsonAsync(HubRoutes.SourceImagesLinks, new SourceImageLinksRequest(shop, [.. ids]));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<SourceImageLinksResponse>())!;
    }

    private static string LinkPath(SourceImageLink l) => new Uri(l.Url!).AbsolutePath;

    private static async Task<string?> ErrorOf(HttpResponseMessage r)
    {
        try { return (await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("error").GetString(); }
        catch { return null; }
    }

    private static Task<HttpResponseMessage> DeleteAll(HttpClient c, object? body)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, HubRoutes.SourceImages);
        if (body is not null) req.Content = JsonContent.Create(body);
        return c.SendAsync(req);
    }

    private static List<string> AllFiles(string dir) => !Directory.Exists(dir) ? [] :
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();

    /// <summary>Chỉ file ẢNH của kho gốc (tên = id, không có '.'; bỏ <c>.meta</c> / <c>.tmp</c>).</summary>
    private static List<string> Images(string dir) => AllFiles(dir).Where(f => !Path.GetFileName(f).Contains('.')).ToList();

    // ── auth ──

    [Fact]
    public async Task RouteKhoGoc_KhongToken_401()
    {
        using var app = new SourceHubApp();
        app.KhachCoToken();   // đặt token ở Hub (có token thật mà request không gửi)
        using var anon = app.CreateClient();
        var put = new ByteArrayContent(Jpg(100));

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PutAsync(HubRoutes.SourceImages + "/" + IdA, put)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(HubRoutes.SourceImages + "/" + IdA)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync(HubRoutes.SourceImagesMissing,
            new SourceImageIdsRequest([IdA]))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync(HubRoutes.SourceImagesLinks,
            new SourceImageLinksRequest("s", [IdA]))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(HubRoutes.SourceImagesStats)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteAll(anon,
            new SourceImageDeleteAllRequest(SourceImageLimits.DeleteAllConfirm))).StatusCode);
        Assert.Empty(AllFiles(app.SrcDir));
    }

    // ── luật id ──

    [Theory]
    [InlineData("vn-11134207-7r98o-lx1abcdefghi12", true)]
    [InlineData("e5f3a1b2c3d4e5f60718293a4b5c6d7e", true)]
    [InlineData("Ab_cd-12", true)]
    [InlineData("abc1234", false)]         // < 8 ký tự
    [InlineData("-abcdefgh", false)]       // ký tự đầu không phải chữ/số
    [InlineData("../../hub.db", false)]
    [InlineData("a/../../escaped", false)]
    [InlineData("abc\\..\\..\\x", false)]
    [InlineData("abcdefgh.jpg", false)]
    [InlineData("abcdefgh ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidId_ChiChuSoGachNoiGachDuoi(string? id, bool ok) => Assert.Equal(ok, SourceImageLimits.IsValidId(id));

    [Fact]
    public void IsValidId_DaiQua128_Sai() =>
        Assert.False(SourceImageLimits.IsValidId("a" + new string('b', 128)));

    [Fact]
    public async Task Store_IdDiRaNgoaiThuMuc_BiChan_KhongGhiFileNaoNgoaiKho()
    {
        using var app = new SourceHubApp();
        app.KhachCoToken();
        foreach (var id in new[] { "a/../../escaped-file", "a/../escaped-file", "x\\..\\..\\escaped-file", "../../escaped-file" })
        {
            var r = await app.Store.SaveAsync(id, Jpg(500), CancellationToken.None);
            Assert.Equal(SourceImageStore.SaveStatus.BadId, r.Status);
            Assert.Null(await app.Store.ReadAsync(id, CancellationToken.None));
            Assert.False(app.Store.FileExists(id));
            Assert.Null(app.Store.Usable(id));
        }
        Assert.False(File.Exists(Path.Combine(app.DataDir, "escaped-file")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(app.DataDir)!, "escaped-file")));
        Assert.Empty(AllFiles(app.SrcDir));
    }

    [Fact]
    public async Task Http_IdSai_400_KhongGhi()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();

        foreach (var id in new[] { "short1", "..%2F..%2Fescaped-file", "abcdefgh.jpg", "%2E%2E%2Fabcdefgh" })
        {
            var r = await Put(c, id, Jpg(500));
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("bad-id", await ErrorOf(r));
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(HubRoutes.SourceImages + "/short1")).StatusCode);
        Assert.Empty(AllFiles(app.SrcDir));
        Assert.False(File.Exists(Path.Combine(app.DataDir, "escaped-file")));
    }

    // ── PUT ──

    [Fact]
    public async Task Put_Moi_RoiPutLaiCungId_Existed_KhongGhiDe()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        var dau = Jpg(3000);
        var sau = Png(5000);

        var r1 = await PutOk(c, IdA, dau);
        var r2 = await PutOk(c, IdA, sau);

        Assert.False(r1.Existed);
        Assert.Equal(3000, r1.Size);
        Assert.Equal("image/jpeg", r1.ContentType);
        Assert.True(r2.Existed);
        Assert.Equal(3000, r2.Size);                    // của bản ĐANG GIỮ, không phải bản vừa gửi
        Assert.Equal("image/jpeg", r2.ContentType);
        Assert.Equal(dau, File.ReadAllBytes(Path.Combine(app.SrcDir, IdA)));
        var get = await c.GetAsync(HubRoutes.SourceImages + "/" + IdA);
        Assert.Equal(dau, await get.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/jpeg", get.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Put_SongSongCungId_MotBanGhi_KhongFileDo()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        var anh = Enumerable.Range(0, 12).Select(_ => Jpg(200_000)).ToList();

        var res = await Task.WhenAll(anh.Select(a => PutOk(c, IdA, a)));

        Assert.Single(res, r => !r.Existed);
        var tren = File.ReadAllBytes(Path.Combine(app.SrcDir, IdA));
        Assert.Contains(anh, a => a.AsSpan().SequenceEqual(tren));
        Assert.Equal([Path.Combine(app.SrcDir, IdA), Path.Combine(app.SrcDir, IdA + ".meta")], AllFiles(app.SrcDir));   // không còn .tmp
        var stats = await c.GetFromJsonAsync<SourceImageStatsResponse>(HubRoutes.SourceImagesStats);
        Assert.Equal(1, stats!.SourceCount);
        Assert.Equal(200_000, stats.SourceBytes);
    }

    [Fact]
    public async Task Put_413_415_400Rong_TranTheoBanDoiMd5()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();

        var lon = await Put(c, IdA, Jpg(SourceImageLimits.MaxBytes + 1));
        var jpgVua = await Put(c, IdB, Jpg(SourceImageLimits.MaxBytes - 68));     // bản đổi MD5 = đúng trần → nhận
        var jpgSat = await Put(c, IdC, Jpg(SourceImageLimits.MaxBytes - 67));     // bản đổi MD5 vượt trần 1 byte → 413
        var pngVua = await Put(c, Id(5), Png(SourceImageLimits.MaxBytes - 84));
        var pngSat = await Put(c, Id(6), Png(SourceImageLimits.MaxBytes - 83));
        var rac = await Put(c, Id(1), Encoding.ASCII.GetBytes("<html>khong phai anh</html>"));
        var gif = await Put(c, Id(2), Encoding.ASCII.GetBytes("GIF89a-----------"));
        var rong = await Put(c, Id(3), []);
        var webp = await Put(c, Id(4), SourceImageMd5ParityTests.Fixture("tiny.webp"));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, lon.StatusCode);
        Assert.Equal("too-large", await ErrorOf(lon));
        Assert.Equal(HttpStatusCode.OK, jpgVua.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, jpgSat.StatusCode);
        Assert.Equal(HttpStatusCode.OK, pngVua.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, pngSat.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, rac.StatusCode);
        Assert.Equal("unsupported-type", await ErrorOf(rac));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, gif.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, rong.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, webp.StatusCode);   // WebP: Hub cũng như app không đổi MD5 được
        Assert.Equal("unsupported-type", await ErrorOf(webp));
        Assert.Equal([Path.Combine(app.SrcDir, IdB), Path.Combine(app.SrcDir, Id(5))], Images(app.SrcDir));

        // Bản sát trần GET qua link được, đúng trần.
        using var anon = app.CreateClient();
        var links = await Links(c, "shop-a", IdB, Id(5));
        Assert.Equal(2, links.Items.Count);
        foreach (var l in links.Items)
            Assert.Equal(PublicMediaLimits.MaxBytes, (await anon.GetByteArrayAsync(LinkPath(l))).Length);
    }

    [Fact]
    public async Task Put_QuaTran_507_IdDaCoVanExisted()
    {
        using var app = new SourceHubApp(maxMb: 1);
        var c = app.KhachCoToken();

        await PutOk(c, IdA, Jpg(600_000));
        var quaTran = await Put(c, IdB, Jpg(600_000));
        var lai = await PutOk(c, IdA, Jpg(600_000));

        Assert.Equal(HttpStatusCode.InsufficientStorage, quaTran.StatusCode);
        Assert.Equal("source-quota-exceeded", await ErrorOf(quaTran));
        Assert.True(lai.Existed);
        Assert.False(File.Exists(Path.Combine(app.SrcDir, IdB)));
        Assert.Single(Images(app.SrcDir));
    }

    [Fact]
    public async Task Put_DiaDay_507DiskLow_KhongDoDuoc_CungTuChoi()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        app.Store.FreeBytes = () => 1000;
        var day = await Put(c, IdA, Jpg(5000));
        app.Store.FreeBytes = () => throw new IOException("không đo được");
        var loi = await Put(c, IdB, Jpg(5000));

        Assert.Equal(HttpStatusCode.InsufficientStorage, day.StatusCode);
        Assert.Equal("disk-low", await ErrorOf(day));
        Assert.Equal(HttpStatusCode.InsufficientStorage, loi.StatusCode);
        Assert.Empty(AllFiles(app.SrcDir));
    }

    // ── GET/HEAD ảnh gốc (auth) ──

    [Fact]
    public async Task GetHead_AnhGoc_DungBytes_ContentLength_404KhiChuaCo()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        var anh = Png(4321);
        await PutOk(c, IdA, anh);

        var get = await c.GetAsync(HubRoutes.SourceImages + "/" + IdA);
        var head = await c.SendAsync(new HttpRequestMessage(HttpMethod.Head, HubRoutes.SourceImages + "/" + IdA));
        var khong = await c.GetAsync(HubRoutes.SourceImages + "/" + IdB);

        Assert.Equal(anh, await get.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png", get.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(4321, head.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.NotFound, khong.StatusCode);
    }

    // ── missing ──

    [Fact]
    public async Task Missing_KhuTrung_GiuThuTu_ChiIdChuaCo()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        await PutOk(c, Id(2), Jpg(100));
        await PutOk(c, Id(4), Jpg(100));

        var res = await c.PostAsJsonAsync(HubRoutes.SourceImagesMissing,
            new SourceImageIdsRequest([Id(3), Id(2), Id(1), Id(3), Id(4), Id(1)]));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal([Id(3), Id(1)], (await res.Content.ReadFromJsonAsync<SourceImageMissingResponse>())!.Missing);
    }

    [Fact]
    public async Task Missing_400_ThieuIds_QuaTran_IdSai()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();

        var thieu = await c.PostAsJsonAsync(HubRoutes.SourceImagesMissing, new SourceImageIdsRequest(null));
        var quaTran = await c.PostAsJsonAsync(HubRoutes.SourceImagesMissing,
            new SourceImageIdsRequest([.. Enumerable.Repeat(IdA, SourceImageLimits.MaxIdsPerCall + 1)]));
        var vuaTran = await c.PostAsJsonAsync(HubRoutes.SourceImagesMissing,
            new SourceImageIdsRequest([.. Enumerable.Range(0, SourceImageLimits.MaxIdsPerCall).Select(Id)]));
        var sai = await c.PostAsJsonAsync(HubRoutes.SourceImagesMissing, new SourceImageIdsRequest([IdA, "../../x"]));

        Assert.Equal(HttpStatusCode.BadRequest, thieu.StatusCode);
        Assert.Equal("ids-required", await ErrorOf(thieu));
        Assert.Equal(HttpStatusCode.BadRequest, quaTran.StatusCode);
        Assert.Equal("too-many-ids", await ErrorOf(quaTran));
        Assert.Equal(HttpStatusCode.OK, vuaTran.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, sai.StatusCode);
        Assert.Equal("bad-id", await ErrorOf(sai));
    }

    // ── links ──

    [Fact]
    public async Task Links_Url_Missing_FileHongCungLaMissing()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        await PutOk(c, IdA, SourceImageMd5ParityTests.Fixture("tiny.jpg"));
        await PutOk(c, IdB, SourceImageMd5ParityTests.Fixture("tiny.png"));
        File.WriteAllBytes(Path.Combine(app.SrcDir, IdC), Jpg(500));   // file có mà không meta (ghi dở / file cũ) = hỏng

        var r = await Links(c, "shop-a", IdA, Id(9), IdB, IdA, IdC);

        Assert.Equal([Id(9), IdC], r.Missing);
        Assert.Equal([IdA, IdB], r.Items.Select(i => i.Id));
        Assert.Matches(@"^https://api\.example\.test/api/v/[A-Za-z0-9_-]+\.jpg$", r.Items[0].Url);
        Assert.Matches(@"^https://api\.example\.test/api/v/[A-Za-z0-9_-]+\.png$", r.Items[1].Url);
    }

    [Fact]
    public async Task Links_400_ShopKey_Ids_503ChuaCoBaseUrl()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        async Task<(HttpStatusCode, string?)> Goi(SourceImageLinksRequest req)
        {
            var r = await c.PostAsJsonAsync(HubRoutes.SourceImagesLinks, req);
            return (r.StatusCode, await ErrorOf(r));
        }

        Assert.Equal((HttpStatusCode.BadRequest, "shop-key"), await Goi(new(null, [IdA])));
        Assert.Equal((HttpStatusCode.BadRequest, "shop-key"), await Goi(new("", [IdA])));
        Assert.Equal((HttpStatusCode.BadRequest, "shop-key"), await Goi(new(new string('s', 101), [IdA])));
        Assert.Equal(HttpStatusCode.OK, (await Goi(new(new string('s', 100), [IdA]))).Item1);
        Assert.Equal((HttpStatusCode.BadRequest, "ids-required"), await Goi(new("s", null)));
        Assert.Equal((HttpStatusCode.BadRequest, "too-many-ids"), await Goi(new("s", [.. Enumerable.Repeat(IdA, 201)])));
        Assert.Equal((HttpStatusCode.BadRequest, "bad-id"), await Goi(new("s", ["a/../../hub-db"])));

        using var app2 = new SourceHubApp(baseUrl: null);
        var c2 = app2.KhachCoToken();
        var r2 = await c2.PostAsJsonAsync(HubRoutes.SourceImagesLinks, new SourceImageLinksRequest("s", [IdA]));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, r2.StatusCode);
        Assert.Equal("public-base-url-missing", await ErrorOf(r2));
    }

    // ── link công khai /api/v/… ──

    [Fact]
    public async Task Link_TraBytesKhopTungByteVoiClient_ChoMoiVector()
    {
        // Mỗi ảnh vào gắn với 1 id riêng của kho (vector cùng id mà khác ảnh không cùng nằm trong một kho được).
        foreach (var g in SourceImageMd5ParityTests.Vectors().GroupBy(v => (v.Input, v.Id)))
        {
            using var dataApp = new SourceHubApp();
            var cc = dataApp.KhachCoToken();
            using var aa = dataApp.CreateClient();
            await PutOk(cc, g.Key.Id, SourceImageMd5ParityTests.Fixture(g.Key.Input));
            foreach (var v in g)
            {
                var link = (await Links(cc, v.Shop, v.Id)).Items.Single();
                var res = await aa.GetAsync(LinkPath(link));
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                Assert.Equal(SourceImageMd5ParityTests.Fixture(v.Output), await res.Content.ReadAsByteArrayAsync());
                Assert.Equal(v.ContentType, res.Content.Headers.ContentType?.MediaType);
                Assert.Equal("\"" + v.Md5 + "\"", res.Headers.ETag?.Tag);
            }
        }
    }

    [Fact]
    public async Task Link_GoiHaiLanCungBytes_KhacShopKhacMd5_CacheHeadEtag304()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        using var anon = app.CreateClient();
        await PutOk(c, IdA, SourceImageMd5ParityTests.Fixture("tiny.jpg"));
        var la = LinkPath((await Links(c, "shop-a", IdA)).Items[0]);
        var lb = LinkPath((await Links(c, "shop-b", IdA)).Items[0]);

        var g1 = await anon.GetAsync(la);
        var g2 = await anon.GetAsync(la);
        var gb = await anon.GetAsync(lb);
        var head = await anon.SendAsync(new HttpRequestMessage(HttpMethod.Head, la));
        var cond = new HttpRequestMessage(HttpMethod.Get, la);
        cond.Headers.IfNoneMatch.Add(g1.Headers.ETag!);
        var r304 = await anon.SendAsync(cond);

        var b1 = await g1.Content.ReadAsByteArrayAsync();
        Assert.Equal(b1, await g2.Content.ReadAsByteArrayAsync());
        var bb = await gb.Content.ReadAsByteArrayAsync();
        Assert.NotEqual(Convert.ToHexString(System.Security.Cryptography.MD5.HashData(b1)),
            Convert.ToHexString(System.Security.Cryptography.MD5.HashData(bb)));
        Assert.NotEqual(g1.Headers.ETag, gb.Headers.ETag);
        Assert.True(g1.Headers.CacheControl?.Public);
        Assert.Equal(TimeSpan.FromSeconds(31536000), g1.Headers.CacheControl?.MaxAge);
        Assert.Contains(g1.Headers.CacheControl!.Extensions, e => e.Name == "immutable");
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(b1.Length, head.Content.Headers.ContentLength);
        Assert.Equal("image/jpeg", head.Content.Headers.ContentType?.MediaType);
        Assert.Equal(g1.Headers.ETag, head.Headers.ETag);
        Assert.Equal(HttpStatusCode.NotModified, r304.StatusCode);
    }

    /// <summary>Giải base64url của token (để dựng token giả trong test).</summary>
    private static byte[] B64UrlDecode(string s) =>
        Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/') + new string('=', (4 - s.Length % 4) % 4));

    private static string B64UrlEncode(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task Link_SuaMotKyTuToken_404_TokenIdAKhongLayDuocIdB()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        using var anon = app.CreateClient();
        await PutOk(c, IdA, SourceImageMd5ParityTests.Fixture("tiny.jpg"));
        await PutOk(c, IdC, SourceImageMd5ParityTests.Fixture("tiny.jpg"));
        var path = LinkPath((await Links(c, "shop-a", IdA)).Items[0]);
        var token = path[(HubRoutes.PublicSourceImage.Length + 1)..^4];
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(path)).StatusCode);

        // Sửa TỪNG vị trí (đổi sang ký tự base64url khác) — không vị trí nào còn chạy.
        for (var i = 0; i < token.Length; i++)
        {
            var ch = token[i] == 'A' ? 'B' : 'A';
            var sua = token[..i] + ch + token[(i + 1)..];
            var r = await anon.GetAsync($"{HubRoutes.PublicSourceImage}/{sua}.jpg");
            Assert.True(r.StatusCode == HttpStatusCode.NotFound, $"sửa vị trí {i} vẫn {(int)r.StatusCode}");
            Assert.True(r.Headers.CacheControl?.NoStore);
        }

        // Ghép IV (chữ ký) của token A với phần mã của token C (cùng shop, cùng nội dung, id cùng độ dài) → 404; và ngược lại.
        var tokenC = LinkPath((await Links(c, "shop-a", IdC)).Items[0])[(HubRoutes.PublicSourceImage.Length + 1)..^4];
        var ba = B64UrlDecode(token);
        var bc = B64UrlDecode(tokenC);
        Assert.Equal(ba.Length, bc.Length);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync($"{HubRoutes.PublicSourceImage}/{tokenC}.jpg")).StatusCode);
        var ghep1 = ba[..17].Concat(bc[17..]).ToArray();
        var ghep2 = bc[..17].Concat(ba[17..]).ToArray();
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"{HubRoutes.PublicSourceImage}/{B64UrlEncode(ghep1)}.jpg")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"{HubRoutes.PublicSourceImage}/{B64UrlEncode(ghep2)}.jpg")).StatusCode);
        // Lật 1 bit ở từng byte phần mã (CTR dễ uốn) → 404 vì HMAC tính lại trên bản rõ không khớp IV.
        for (var i = 17; i < ba.Length; i++)
        {
            var lat = (byte[])ba.Clone();
            lat[i] ^= 0x01;
            var r = await anon.GetAsync($"{HubRoutes.PublicSourceImage}/{B64UrlEncode(lat)}.jpg");
            Assert.True(r.StatusCode == HttpStatusCode.NotFound, $"lật bit byte {i} vẫn {(int)r.StatusCode}");
        }

        // Đuôi khác loại ảnh gốc, đuôi lạ, thiếu đuôi, rác → 404.
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"{HubRoutes.PublicSourceImage}/{token}.png")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"{HubRoutes.PublicSourceImage}/{token}.webp")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"{HubRoutes.PublicSourceImage}/{token}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync($"{HubRoutes.PublicSourceImage}/rac.jpg")).StatusCode);
    }

    [Fact]
    public async Task Link_AnhGocBiXoa_404()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        using var anon = app.CreateClient();
        await PutOk(c, IdA, SourceImageMd5ParityTests.Fixture("tiny.png"));
        var path = LinkPath((await Links(c, "shop-a", IdA)).Items[0]);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(path)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await DeleteAll(c, new SourceImageDeleteAllRequest(SourceImageLimits.DeleteAllConfirm))).StatusCode);

        var sau = await anon.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, sau.StatusCode);
        Assert.True(sau.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.SendAsync(new HttpRequestMessage(HttpMethod.Head, path))).StatusCode);
    }

    [Fact]
    public async Task Link_KhongGhiFileNao()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        using var anon = app.CreateClient();
        await PutOk(c, IdA, SourceImageMd5ParityTests.Fixture("tiny.jpg"));
        await PutOk(c, IdB, SourceImageMd5ParityTests.Fixture("tiny.png"));
        var links = (await Links(c, "shop-a", IdA, IdB)).Items.Concat((await Links(c, "shop-b", IdA, IdB)).Items).ToList();
        // hub.db (+ WAL) đang mở bởi Hub → chỉ so DANH SÁCH file toàn thư mục, so BYTES trong 2 kho ảnh.
        var truoc = AllFiles(app.DataDir).Where(f => !f.EndsWith("-wal") && !f.EndsWith("-shm")).ToList();
        var giuTruoc = AllFiles(app.SrcDir).ToDictionary(f => f, File.ReadAllBytes);

        foreach (var l in links)
        {
            Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(LinkPath(l))).StatusCode);
            await anon.SendAsync(new HttpRequestMessage(HttpMethod.Head, LinkPath(l)));
        }

        var sau = AllFiles(app.DataDir).Where(f => !f.EndsWith("-wal") && !f.EndsWith("-shm")).ToList();
        Assert.Equal(truoc, sau);
        Assert.Equal(giuTruoc.Keys.Order(StringComparer.Ordinal), AllFiles(app.SrcDir));
        foreach (var f in AllFiles(app.SrcDir)) Assert.Equal(giuTruoc[f], File.ReadAllBytes(f));
        Assert.Empty(AllFiles(app.MediaDir));
    }

    [Fact]
    public async Task KhoaKy_GiuNguyenQuaKhoiDongLai()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hub-src-restart-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path;
            byte[] lan1;
            using (var app1 = new SourceHubApp(dataDir: dir, keepDir: true))
            {
                var c = app1.KhachCoToken();
                await PutOk(c, IdA, SourceImageMd5ParityTests.Fixture("tiny.jpg"));
                path = LinkPath((await Links(c, "shop-a", IdA)).Items[0]);
                using var anon = app1.CreateClient();
                lan1 = await anon.GetByteArrayAsync(path);
            }
            using (var app2 = new SourceHubApp(dataDir: dir, keepDir: true))
            {
                var c = app2.KhachCoToken();
                using var anon = app2.CreateClient();
                var res = await anon.GetAsync(path);
                Assert.Equal(HttpStatusCode.OK, res.StatusCode);
                Assert.Equal(lan1, await res.Content.ReadAsByteArrayAsync());
                Assert.Equal(path, LinkPath((await Links(c, "shop-a", IdA)).Items[0]));   // cùng khoá → cùng link
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── stats ──

    [Fact]
    public async Task Stats_DungSoTruocSauPut_Delete_KhoTamGiuNguyen()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        async Task<SourceImageStatsResponse> Stats() =>
            (await c.GetFromJsonAsync<SourceImageStatsResponse>(HubRoutes.SourceImagesStats))!;

        var s0 = await Stats();
        await PutOk(c, IdA, Jpg(1000));
        await PutOk(c, IdB, Png(2500));
        await PutOk(c, IdA, Jpg(9999));   // trùng → không đổi số
        var media = new ByteArrayContent(Jpg(700));
        var up = await c.PutAsync(HubRoutes.PublicMedia, media);
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        var s1 = await Stats();

        Assert.Equal(0, s0.SourceCount);
        Assert.Equal(0, s0.SourceBytes);
        Assert.Equal(0, s0.MediaCount);
        Assert.Equal(64L * 1024 * 1024, s0.SourceMaxBytes);
        Assert.Equal(64L * 1024 * 1024, s0.MediaMaxBytes);
        Assert.True(s0.DiskTotalBytes > 0);
        Assert.True(s0.DiskFreeBytes > 0);
        Assert.Equal(2, s1.SourceCount);
        Assert.Equal(3500, s1.SourceBytes);
        Assert.Equal(1, s1.MediaCount);
        Assert.Equal(700, s1.MediaBytes);

        Assert.Equal(HttpStatusCode.OK, (await DeleteAll(c, new SourceImageDeleteAllRequest(SourceImageLimits.DeleteAllConfirm))).StatusCode);
        var s2 = await Stats();
        Assert.Equal(0, s2.SourceCount);
        Assert.Equal(0, s2.SourceBytes);
        Assert.Equal(1, s2.MediaCount);
        Assert.Equal(700, s2.MediaBytes);
    }

    [Fact]
    public async Task Stats_DemLanDauTuDia_KhiStoreMoiDung()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hub-src-count-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "source-images"));
            File.WriteAllBytes(Path.Combine(dir, "source-images", IdA), Jpg(1234));
            File.WriteAllBytes(Path.Combine(dir, "source-images", IdA + ".abc.tmp"), Jpg(50));   // file tạm không tính
            using var app = new SourceHubApp(dataDir: dir);
            var c = app.KhachCoToken();
            var s = (await c.GetFromJsonAsync<SourceImageStatsResponse>(HubRoutes.SourceImagesStats))!;
            Assert.Equal(1, s.SourceCount);
            Assert.Equal(1234, s.SourceBytes);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── DELETE-all ──

    [Fact]
    public async Task DeleteAll_ThieuHoacSaiConfirm_400_KhongXoa()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        await PutOk(c, IdA, Jpg(1000));

        var khongBody = await DeleteAll(c, null);
        var sai = await DeleteAll(c, new SourceImageDeleteAllRequest("xoa-het-anh-goc"));
        var rong = await DeleteAll(c, new SourceImageDeleteAllRequest(null));
        var rac = new HttpRequestMessage(HttpMethod.Delete, HubRoutes.SourceImages)
        { Content = new StringContent("{khong-phai-json", Encoding.UTF8, "application/json") };
        var racRes = await c.SendAsync(rac);

        foreach (var r in new[] { khongBody, sai, rong, racRes })
        {
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Equal("confirm-required", await ErrorOf(r));
        }
        Assert.True(File.Exists(Path.Combine(app.SrcDir, IdA)));
    }

    [Fact]
    public async Task DeleteAll_XoaSach_Remaining0_KhoTamKhongMatFileNao()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        for (var i = 0; i < 5; i++) await PutOk(c, Id(i), Jpg(1000 + i));
        File.WriteAllBytes(Path.Combine(app.SrcDir, Id(0) + ".dead.tmp"), new byte[10]);   // file tạm mồ côi cũng đi
        var mediaPaths = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var up = await c.PutAsync(HubRoutes.PublicMedia, new ByteArrayContent(Jpg(500)));
            mediaPaths.Add((await up.Content.ReadFromJsonAsync<PublicMediaUploadResponse>())!.Path);
        }
        var mediaTruoc = AllFiles(app.MediaDir);
        var tongTruoc = AllFiles(app.SrcDir).Sum(f => new FileInfo(f).Length);   // ảnh + meta + tmp

        var res = await DeleteAll(c, new SourceImageDeleteAllRequest(SourceImageLimits.DeleteAllConfirm));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<SourceImageDeleteAllResponse>())!;
        Assert.Equal(5, body.Deleted);
        Assert.Equal(0, body.Remaining);
        Assert.Equal(tongTruoc, body.FreedBytes);
        Assert.True(body.FreedBytes > 1000 + 1001 + 1002 + 1003 + 1004 + 10);
        Assert.Equal(SourceImageEndpoints.DeleteAllWarning, body.Warning);
        Assert.Contains("hỏng", body.Warning);
        Assert.Empty(AllFiles(app.SrcDir));
        Assert.Equal(mediaTruoc, AllFiles(app.MediaDir));
        using var anon = app.CreateClient();
        foreach (var p in mediaPaths) Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(p)).StatusCode);
        // Kho rỗng vẫn nhận ảnh mới bình thường (bộ đếm đặt lại đúng).
        await PutOk(c, IdA, Jpg(10));
        Assert.Equal(1, (await c.GetFromJsonAsync<SourceImageStatsResponse>(HubRoutes.SourceImagesStats))!.SourceCount);
    }

    [Fact]
    public async Task DeleteAll_SongSongVoiPut_KhongFileDo_DemKhopDia()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        for (var i = 0; i < 20; i++) await PutOk(c, Id(i), Jpg(50_000));

        var puts = Enumerable.Range(100, 40).Select(i => Put(c, Id(i), Jpg(50_000))).ToList();
        var del = DeleteAll(c, new SourceImageDeleteAllRequest(SourceImageLimits.DeleteAllConfirm));
        var putRes = await Task.WhenAll(puts);
        var delRes = await del;

        Assert.All(putRes, p => Assert.Equal(HttpStatusCode.OK, p.StatusCode));
        var body = (await delRes.Content.ReadFromJsonAsync<SourceImageDeleteAllResponse>())!;
        Assert.Equal(0, body.Remaining);
        Assert.DoesNotContain(AllFiles(app.SrcDir), f => f.EndsWith(".tmp"));
        var files = Images(app.SrcDir);
        Assert.All(files, f => Assert.Equal(50_000, new FileInfo(f).Length));   // không file nào ghi dở
        var s = (await c.GetFromJsonAsync<SourceImageStatsResponse>(HubRoutes.SourceImagesStats))!;
        Assert.Equal(files.Count, s.SourceCount);
        Assert.Equal(files.Count * 50_000L, s.SourceBytes);
        var missing = await c.PostAsJsonAsync(HubRoutes.SourceImagesMissing,
            new SourceImageIdsRequest([.. files.Select(f => Path.GetFileName(f))]));
        Assert.Empty((await missing.Content.ReadFromJsonAsync<SourceImageMissingResponse>())!.Missing);   // còn lại đều có meta
    }

    // ── khoá ký: chỉ sinh khi CHƯA CÓ DÒNG, hỏng thì ném, không ghi đè ──

    private static string NewDir() => Path.Combine(Path.GetTempPath(), "hub-src-key-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("!!!khong-phai-base64!!!")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw==")]                           // base64 đúng nhưng 16 byte
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8gIQ==")]   // 34 byte
    [InlineData("")]                                                   // có dòng mà rỗng
    [InlineData("   ")]
    public void Signer_KhoaHong_Nem_KhongGhiDeSetting(string hong)
    {
        var dir = NewDir();
        try
        {
            using var db = new HubDatabase(dir);
            db.SetSetting(SettingKeys.SourceImageLinkKey, hong);

            Assert.Throws<InvalidOperationException>(() => new SourceImageLinkSigner(db));

            Assert.Equal((true, (string?)hong), db.ReadSettingRow(SettingKeys.SourceImageLinkKey));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Signer_ChuaCoDong_SinhMotLan_LanSauDungLai()
    {
        var dir = NewDir();
        try
        {
            using var db = new HubDatabase(dir);
            Assert.False(db.ReadSettingRow(SettingKeys.SourceImageLinkKey).Exists);

            var s1 = new SourceImageLinkSigner(db);
            var luu = db.ReadSettingRow(SettingKeys.SourceImageLinkKey);
            var s2 = new SourceImageLinkSigner(db);

            Assert.True(luu.Exists);
            Assert.Equal(32, Convert.FromBase64String(luu.Value!).Length);
            Assert.Equal(luu, db.ReadSettingRow(SettingKeys.SourceImageLinkKey));
            byte[] fp = [1, 2, 3, 4, 5, 6, 7, 8];
            Assert.Equal(s1.Sign("shop-a", IdA, fp), s2.Sign("shop-a", IdA, fp));
            var v = s2.Verify(s1.Sign("shop-a", IdA, fp))!.Value;
            Assert.Equal(("shop-a", IdA), (v.ShopKey, v.Id));
            Assert.Equal(fp, v.Fingerprint);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void DungHub_KhoaHong_KhongKhoiDong_KhongGhiDe()
    {
        var dir = NewDir();
        try
        {
            Directory.CreateDirectory(dir);
            using (var db = new HubDatabase(dir)) db.SetSetting(SettingKeys.SourceImageLinkKey, "");

            using (var app = new SourceHubApp(dataDir: dir, keepDir: true))
                Assert.ThrowsAny<Exception>(() => app.CreateClient());

            using var db2 = new HubDatabase(dir);
            Assert.Equal((true, (string?)""), db2.ReadSettingRow(SettingKeys.SourceImageLinkKey));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ── link công khai: query string, rate limit theo IP thật ──

    [Fact]
    public async Task Link_CoQueryString_404()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        using var anon = app.CreateClient();
        await PutOk(c, IdA, SourceImageMd5ParityTests.Fixture("tiny.jpg"));
        var path = LinkPath((await Links(c, "shop-a", IdA)).Items[0]);

        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(path)).StatusCode);
        foreach (var q in new[] { "?x=1", "?v=" + Guid.NewGuid().ToString("N"), "?a" })
        {
            var r = await anon.GetAsync(path + q);
            Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
            Assert.True(r.Headers.CacheControl?.NoStore);
            Assert.Equal(HttpStatusCode.NotFound, (await anon.SendAsync(new HttpRequestMessage(HttpMethod.Head, path + q))).StatusCode);
        }
    }

    /// <summary>Gửi GET qua TestServer với IP kết nối + header CF-Connecting-IP tuỳ chọn (HttpClient không đặt được IP).</summary>
    private static async Task<int> GetFrom(SourceHubApp app, string path, IPAddress remote, string? cfIp) =>
        (await GetCtx(app, path, remote, cfIp)).Response.StatusCode;

    private static Task<Microsoft.AspNetCore.Http.HttpContext> GetCtx(SourceHubApp app, string path, IPAddress remote, string? cfIp) =>
        app.Server.SendAsync(x =>
        {
            x.Request.Method = "GET";
            x.Request.Path = path;
            x.Connection.RemoteIpAddress = remote;
            if (cfIp is not null) x.Request.Headers["CF-Connecting-IP"] = cfIp;
        });

    [Fact]
    public async Task Link_VuotTanSuatTheoIp_429_IpKhacVanChay_CfChiTinTuLoopback()
    {
        using var app = new SourceHubApp(ratePerMin: 5);
        var c = app.KhachCoToken();
        await PutOk(c, IdA, SourceImageMd5ParityTests.Fixture("tiny.jpg"));
        var path = LinkPath((await Links(c, "shop-a", IdA)).Items[0]);

        // Qua cloudflared (loopback): đếm theo CF-Connecting-IP.
        var ipA = new List<int>();
        for (var i = 0; i < 7; i++) ipA.Add(await GetFrom(app, path, IPAddress.Loopback, "203.0.113.7"));
        var ipB = await GetFrom(app, path, IPAddress.Loopback, "198.51.100.9");

        Assert.Equal([200, 200, 200, 200, 200, 429, 429], ipA);
        Assert.Equal(200, ipB);

        // Từ ngoài (không phải loopback) tự gắn CF-Connecting-IP đổi liên tục → vẫn đếm theo IP kết nối, không né được.
        var ngoai = new List<int>();
        for (var i = 0; i < 7; i++) ngoai.Add(await GetFrom(app, path, IPAddress.Parse("192.0.2.50"), $"10.0.0.{i + 1}"));
        Assert.Equal([200, 200, 200, 200, 200, 429, 429], ngoai);

        // Bị chặn: Retry-After (giây ≥ 1), no-store; log GỘP 1 dòng / IP / phút dù bị chặn nhiều lần.
        var chan = await GetCtx(app, path, IPAddress.Loopback, "203.0.113.7");
        Assert.Equal(429, chan.Response.StatusCode);
        Assert.True(int.Parse(chan.Response.Headers["Retry-After"].ToString()) >= 1);
        Assert.Equal("no-store", chan.Response.Headers["Cache-Control"].ToString());
        var dong = app.Logs.Lines.Where(l => l.Contains("429")).ToList();
        Assert.Equal(2, dong.Count);   // 203.0.113.7 (3 lần chặn) + 192.0.2.50 (2 lần chặn) — mỗi IP đúng 1 dòng
        Assert.Single(dong, l => l.Contains("203.0.113.7"));
        Assert.Single(dong, l => l.Contains("192.0.2.50"));

        // Route có token (không gắn policy) không bị giới hạn này.
        for (var i = 0; i < 8; i++)
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(HubRoutes.SourceImagesStats)).StatusCode);
    }

    [Fact]
    public void ClientIp_ChiTinCfTuLoopback_VaChiKhiLaIpHopLe()
    {
        static string Ip(IPAddress? remote, string? cf)
        {
            var ctx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = remote;
            if (cf is not null) ctx.Request.Headers["CF-Connecting-IP"] = cf;
            return SourceImageEndpoints.ClientIp(ctx);
        }

        Assert.Equal("203.0.113.7", Ip(IPAddress.Loopback, "203.0.113.7"));
        Assert.Equal("2001:db8::1", Ip(IPAddress.IPv6Loopback, "2001:db8::1"));
        Assert.Equal("127.0.0.1", Ip(IPAddress.Loopback, "rac-khong-phai-ip"));
        Assert.Equal("127.0.0.1", Ip(IPAddress.Loopback, null));
        Assert.Equal("192.0.2.50", Ip(IPAddress.Parse("192.0.2.50"), "203.0.113.7"));
        Assert.Equal("?", Ip(null, "203.0.113.7"));
    }

    // ── vòng 3: chỉ nhận ảnh dùng được, thay ảnh hỏng, xoá 1 ảnh, dấu vân tay nội dung, token mã hoá ──

    /// <summary>Gom log Hub trong test.</summary>
    internal sealed class LogCapture : Microsoft.Extensions.Logging.ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();
        public List<string> Lines => [.. _lines];
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new L(this, categoryName);
        public void Dispose() { }

        private sealed class L(LogCapture owner, string category) : Microsoft.Extensions.Logging.ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
                TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (category.StartsWith("Shopee.Hub", StringComparison.Ordinal) || category == "SourceImages")
                    owner._lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)}");
            }
        }
    }

    private static async Task<List<string>> Missing(HttpClient c, params string[] ids)
    {
        var r = await c.PostAsJsonAsync(HubRoutes.SourceImagesMissing, new SourceImageIdsRequest([.. ids]));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<SourceImageMissingResponse>())!.Missing;
    }

    [Fact]
    public async Task Put_WebPBiTuChoi_SauDoJpegThatVanNhan()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        var jpg = SourceImageMd5ParityTests.Fixture("tiny.jpg");

        var webp = await Put(c, IdA, SourceImageMd5ParityTests.Fixture("tiny.webp"));
        var sau = await PutOk(c, IdA, jpg);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, webp.StatusCode);
        Assert.False(sau.Existed);
        Assert.False(sau.Replaced);
        Assert.Equal("image/jpeg", sau.ContentType);
        Assert.Empty(await Missing(c, IdA));
        Assert.Single((await Links(c, "shop-a", IdA)).Items);
    }

    [Fact]
    public async Task Put_AnhHong_400CorruptImage_KhongLuu()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        var jpg = SourceImageMd5ParityTests.Fixture("tiny.jpg");
        var png = SourceImageMd5ParityTests.Fixture("tiny.png");
        var cutJpg = jpg[..(jpg.Length / 2)];                              // cụt nửa → không có EOI
        var appnTran = (byte[])jpg.Clone();                                // APP0 khai độ dài vượt file, vẫn kết thúc FF D9
        appnTran[4] = 0xFF; appnTran[5] = 0xF0;
        var pngThieuIend = png[..^12];
        var pngKhongIhdr = Png(400);
        pngKhongIhdr[12] = (byte)'X';                                      // chunk đầu không phải IHDR

        var hong = new (string Id, byte[] Bytes)[] { (Id(1), cutJpg), (Id(2), appnTran), (Id(3), pngThieuIend), (Id(4), pngKhongIhdr) };
        foreach (var (id, bytes) in hong)
        {
            var r = await Put(c, id, bytes);
            Assert.True(r.StatusCode == HttpStatusCode.BadRequest, $"{id}: {(int)r.StatusCode}");
            Assert.Equal("corrupt-image", await ErrorOf(r));
        }
        Assert.Empty(AllFiles(app.SrcDir));
        Assert.Equal([Id(1), Id(2), Id(3), Id(4)], await Missing(c, Id(1), Id(2), Id(3), Id(4)));
    }

    [Fact]
    public async Task Put_BanDangGiuHong_ThayBangBanDung_Replaced()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        // (1) file không meta (ghi dở / có từ trước luật này); (2) meta lệch độ dài (file bị cắt sau khi ghi).
        _ = app.Store;   // dựng store → tạo thư mục kho
        File.WriteAllBytes(Path.Combine(app.SrcDir, IdA), Jpg(777));
        await PutOk(c, IdB, Jpg(900));
        File.WriteAllBytes(Path.Combine(app.SrcDir, IdB), Jpg(450));
        Assert.Equal([IdA, IdB], await Missing(c, IdA, IdB));
        Assert.Equal([IdA, IdB], (await Links(c, "shop-a", IdA, IdB)).Missing);

        var moiA = Png(3000);
        var moiB = Jpg(1200);
        var ra = await PutOk(c, IdA, moiA);
        var rb = await PutOk(c, IdB, moiB);
        var lai = await PutOk(c, IdA, Jpg(5000));

        Assert.True(ra.Replaced);
        Assert.False(ra.Existed);
        Assert.Equal(3000, ra.Size);
        Assert.Equal("image/png", ra.ContentType);
        Assert.True(rb.Replaced);
        Assert.True(lai.Existed);                        // bản thay đã dùng được → lần sau không ghi lại
        Assert.False(lai.Replaced);
        Assert.Empty(await Missing(c, IdA, IdB));
        Assert.Equal(moiA, await (await c.GetAsync(HubRoutes.SourceImages + "/" + IdA)).Content.ReadAsByteArrayAsync());
        Assert.Equal(moiB, File.ReadAllBytes(Path.Combine(app.SrcDir, IdB)));
        var s = (await c.GetFromJsonAsync<SourceImageStatsResponse>(HubRoutes.SourceImagesStats))!;
        Assert.Equal(2, s.SourceCount);
        Assert.Equal(3000 + 1200, s.SourceBytes);
        Assert.DoesNotContain(AllFiles(app.SrcDir), f => f.EndsWith(".tmp"));
    }

    [Fact]
    public async Task DeleteMot_401_200_404_MissingLaiBao_LinkHong()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        using var anon = app.CreateClient();
        await PutOk(c, IdA, SourceImageMd5ParityTests.Fixture("tiny.jpg"));
        await PutOk(c, IdB, Jpg(700));
        var path = LinkPath((await Links(c, "shop-a", IdA)).Items[0]);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.DeleteAsync(HubRoutes.SourceImages + "/" + IdA)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.DeleteAsync(HubRoutes.SourceImages + "/short1")).StatusCode);
        var xoa = await c.DeleteAsync(HubRoutes.SourceImages + "/" + IdA);
        var lai = await c.DeleteAsync(HubRoutes.SourceImages + "/" + IdA);

        Assert.Equal(HttpStatusCode.OK, xoa.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, lai.StatusCode);
        Assert.Equal([IdA], await Missing(c, IdA, IdB));
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(path)).StatusCode);
        Assert.Equal([Path.Combine(app.SrcDir, IdB), Path.Combine(app.SrcDir, IdB + ".meta")], AllFiles(app.SrcDir));
        var s = (await c.GetFromJsonAsync<SourceImageStatsResponse>(HubRoutes.SourceImagesStats))!;
        Assert.Equal((1L, 700L), (s.SourceCount, s.SourceBytes));
    }

    [Fact]
    public async Task Link_DauVanTayNoiDung_AnhThayThiLinkCu404()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        using var anon = app.CreateClient();
        var noiDung1 = SourceImageMd5ParityTests.Fixture("tiny.jpg");
        var noiDung2 = Jpg(1500);
        await PutOk(c, IdA, noiDung1);
        var l1 = LinkPath((await Links(c, "shop-a", IdA)).Items[0]);
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(l1)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await c.DeleteAsync(HubRoutes.SourceImages + "/" + IdA)).StatusCode);
        await PutOk(c, IdA, noiDung2);
        var l2 = LinkPath((await Links(c, "shop-a", IdA)).Items[0]);

        Assert.NotEqual(l1, l2);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync(l1)).StatusCode);   // cache/link cũ không trỏ sang bytes mới
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(l2)).StatusCode);

        // Đưa lại đúng nội dung cũ → đúng link cũ (tất định theo nội dung).
        await c.DeleteAsync(HubRoutes.SourceImages + "/" + IdA);
        await PutOk(c, IdA, noiDung1);
        Assert.Equal(l1, LinkPath((await Links(c, "shop-a", IdA)).Items[0]));
        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync(l1)).StatusCode);
    }

    [Fact]
    public async Task Token_MaHoa_KhongLoShopKeyMaAnh_TatDinh()
    {
        using var app = new SourceHubApp();
        var c = app.KhachCoToken();
        const string shop = "shop-bi-mat-Ánh-12345";
        await PutOk(c, IdA, SourceImageMd5ParityTests.Fixture("tiny.jpg"));
        await PutOk(c, IdB, SourceImageMd5ParityTests.Fixture("tiny.jpg"));

        var l1 = LinkPath((await Links(c, shop, IdA)).Items[0]);
        var l2 = LinkPath((await Links(c, shop, IdA)).Items[0]);
        var khacShop = LinkPath((await Links(c, "shop-khac", IdA)).Items[0]);
        var khacId = LinkPath((await Links(c, shop, IdB)).Items[0]);
        var token = l1[(HubRoutes.PublicSourceImage.Length + 1)..^4];
        var bytes = B64UrlDecode(token);

        Assert.Equal(l1, l2);
        Assert.NotEqual(l1, khacShop);
        Assert.NotEqual(l1, khacId);
        Assert.Equal(2, bytes[0]);   // phiên bản token v2
        foreach (var lo in new[] { IdA, "11134207", "7r98o", shop, "bi-mat", "12345" })
        {
            Assert.DoesNotContain(lo, l1, StringComparison.OrdinalIgnoreCase);
            Assert.True(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(lo)) < 0, $"lộ '{lo}' sau khi giải base64");
        }
        // Dấu vân tay nội dung (SHA-256 8 byte đầu) cũng không nằm rõ trong token.
        var fp = System.Security.Cryptography.SHA256.HashData(SourceImageMd5ParityTests.Fixture("tiny.jpg"))[..8];
        Assert.True(bytes.AsSpan().IndexOf(fp) < 0);
    }
}
