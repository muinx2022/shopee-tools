using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shopee.Core.Coordination;
using Shopee.Hub;
using Shopee.Hub.Web.Services;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// <b>Kho dữ liệu SP Harvest (<c>/api/harvested-products/*</c>) đi qua HOST THẬT + Postgres THẬT.</b>
/// <para>Khác các bộ test khác (chỉ cần SQLite của <c>HubDatabase</c>): <c>ProductDb</c> là Postgres, upsert dựa vào
/// <c>ON CONFLICT … WHERE</c> và kiểu <c>jsonb</c> — giả lập thì không khoá được đúng những thứ đó. Mỗi lớp test tạo
/// một database TẠM riêng (<c>hub_harvest_test_*</c>) trên Postgres cục bộ rồi xoá khi xong.</para>
/// <para>Postgres lấy từ <c>HUB_TEST_PG_CONN</c> (chuỗi kết nối quyền tạo database), mặc định
/// <c>Host=127.0.0.1;Username=postgres;Database=postgres</c>. Không kết nối được → các test ở đây bị SKIP (hiện rõ
/// trong kết quả), không đỏ oan trên máy không cài Postgres.</para>
/// </summary>
public sealed class HarvestedProductsHttpTests : IClassFixture<HarvestedProductsHttpTests.HarvestHubApp>
{
    private const string Token = "test-token-harvest";
    private const string May1 = "may-1:harvest";

    /// <summary>Fact chỉ chạy khi có Postgres cục bộ; không có thì Skip kèm lý do.</summary>
    public sealed class PgFactAttribute : FactAttribute
    {
        public PgFactAttribute()
        {
            if (!PgAdmin.Reachable.Value) Skip = "Không kết nối được Postgres test (" + PgAdmin.ConnString + ") — đặt HUB_TEST_PG_CONN.";
        }
    }

    internal static class PgAdmin
    {
        public static readonly string ConnString =
            Environment.GetEnvironmentVariable("HUB_TEST_PG_CONN") is { Length: > 0 } s
                ? s : "Host=127.0.0.1;Username=postgres;Database=postgres";

        public static readonly Lazy<bool> Reachable = new(() =>
        {
            try
            {
                var b = new NpgsqlConnectionStringBuilder(ConnString) { Timeout = 3, Pooling = false };
                using var c = new NpgsqlConnection(b.ConnectionString);
                c.Open();
                return true;
            }
            catch { return false; }
        });

        public static void Exec(string sql)
        {
            var b = new NpgsqlConnectionStringBuilder(ConnString) { Pooling = false };
            using var c = new NpgsqlConnection(b.ConnectionString);
            c.Open();
            using var cmd = new NpgsqlCommand(sql, c);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Host hub dựng một lần cho cả lớp: thư mục dữ liệu TẠM + database Postgres TẠM + token API seed sẵn.</summary>
    public sealed class HarvestHubApp : WebApplicationFactory<Program>
    {
        public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "hub-harvest-" + Guid.NewGuid().ToString("N"));
        public string DbName { get; } = "hub_harvest_test_" + Guid.NewGuid().ToString("N");
        public string TestConn => new NpgsqlConnectionStringBuilder(PgAdmin.ConnString) { Database = DbName }.ConnectionString;
        private bool _dbCreated;

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            // Program.cs ưu tiên env HUB_PG_CONN hơn cấu hình → env mà có giá trị thì test sẽ ghi vào DB THẬT. Chặn.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HUB_PG_CONN")))
                throw new InvalidOperationException("HUB_PG_CONN đang đặt trong môi trường — test sẽ ghi vào DB đó. Bỏ biến này trước khi chạy test.");
            Directory.CreateDirectory(DataDir);
            PgAdmin.Exec($"CREATE DATABASE \"{DbName}\";");
            _dbCreated = true;
            builder.UseSetting("Hub:DataDir", DataDir);
            builder.UseSetting("Hub:PgConn", TestConn);
        }

        /// <summary>HttpClient mang sẵn token + id máy; chờ <see cref="ProductDb.IsReady"/> (migrate chạy ở nền).</summary>
        public async Task<HttpClient> KhachCoTokenAsync(string machineId = May1)
        {
            var c = CreateClient();
            Services.GetRequiredService<HubDatabase>().SetSetting(SettingKeys.ApiToken, Token);
            var pdb = Services.GetRequiredService<ProductDb>();
            for (var i = 0; i < 300 && !pdb.IsReady; i++) await Task.Delay(100);
            Assert.True(pdb.IsReady, "ProductDb không migrate xong trong 30 s");
            c.DefaultRequestHeaders.Add("X-Api-Token", Token);
            c.DefaultRequestHeaders.Add("X-Machine-Id", machineId);
            return c;
        }

        public async Task<NpgsqlConnection> MoDbAsync()
        {
            var conn = new NpgsqlConnection(TestConn);
            await conn.OpenAsync();
            return conn;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing) return;
            try { Directory.Delete(DataDir, true); } catch { }
            if (_dbCreated)
            {
                NpgsqlConnection.ClearAllPools();
                try { PgAdmin.Exec($"DROP DATABASE IF EXISTS \"{DbName}\" WITH (FORCE);"); } catch { }
            }
        }
    }

    private readonly HarvestHubApp _app;

    public HarvestedProductsHttpTests(HarvestHubApp app) => _app = app;

    // itemId ngẫu nhiên lớn: các test trong lớp dùng chung 1 database, không được giẫm lên nhau.
    private static long IdMoi() => Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);

    private static HarvestedProductItem Sp(long itemId, string hash, string title = "Ao thun", string? acct = null,
        string? sheet = null, int? rowNo = null, string? rawJson = null, long shopId = 123456, DateTimeOffset? harvestedAt = null) =>
        new(itemId, shopId, $"https://shopee.vn/product/{shopId}/{itemId}", title, 100352, [100017, 100099, 100352], 0,
            99000, 129000, 150000, 2, 16,
            ["https://down-vn.img.susercontent.com/file/a", "https://down-vn.img.susercontent.com/file/b"],
            ["https://cvf.shopee.vn/file/v.mp4"], null,
            rawJson ?? "{\"error\":null,\"data\":{\"item\":{\"item_id\":" + itemId + ",\"shop_id\":" + shopId
                       + ",\"title\":\"" + title + "\"}}}", hash,
            acct, sheet, rowNo, harvestedAt ?? DateTimeOffset.UtcNow);

    private static Task<HarvestedProductBatchResponse> PostOk(HttpClient c, params HarvestedProductItem[] items) =>
        PostMay(c, May1, items);


    private static Task<HarvestedProductBatchResponse> PostMay(HttpClient c, string? machineId, params HarvestedProductItem[] items) =>
        PostLo(c, machineId, DateTimeOffset.UtcNow, items);

    /// <summary>POST lô + khoá hợp đồng chung của mọi phản hồi: đủ số mục, đúng thứ tự chỉ số, itemId khớp mục gửi,
    /// Ok ⇔ không có Reason, Applied ⇒ Ok. <paramref name="clientNow"/> = giờ máy client giả lập.</summary>
    private static async Task<HarvestedProductBatchResponse> PostLo(HttpClient c, string? machineId, DateTimeOffset clientNow,
        params HarvestedProductItem[] items)
    {
        var res = await c.PostAsJsonAsync(HubRoutes.HarvestedProductsBatch, new HarvestedProductBatchRequest(machineId, clientNow, [.. items]));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<HarvestedProductBatchResponse>())!;
        Assert.Equal(items.Length, body.Results.Count);
        for (var i = 0; i < items.Length; i++)
        {
            Assert.Equal(i, body.Results[i].Index);
            Assert.Equal(items[i].ItemId, body.Results[i].ItemId);
            Assert.Equal(body.Results[i].Ok, body.Results[i].Reason is null);
            Assert.True(!body.Results[i].Applied || body.Results[i].Ok, $"mục {i}: Applied mà không Ok");
        }
        return body;
    }

    private static string CjkNgauNhien(int n)
    {
        var rnd = new Random(42);
        return new string(Enumerable.Range(0, n).Select(_ => (char)rnd.Next(0x4E00, 0x9FFF)).ToArray());
    }

    private static List<long> IdsOk(HarvestedProductBatchResponse r) => r.Results.Where(x => x.Ok).Select(x => x.ItemId).ToList();

    private sealed record DongSp(int HarvestCount, string Name, string RawTitle, string Hash, int SoAnh,
        DateTime FirstSeen, DateTime Updated, string? MachineId, long PriceMin);

    private async Task<DongSp?> DocSp(long itemId)
    {
        await using var conn = await _app.MoDbAsync();
        await using var cmd = new NpgsqlCommand(@"
SELECT harvest_count, name, raw->'data'->'item'->>'title', raw_hash, jsonb_array_length(image_urls),
       first_seen_at, updated_at, machine_id, price_min
FROM harvested_products WHERE item_id = $1;", conn);
        cmd.Parameters.AddWithValue(itemId);
        await using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;
        return new DongSp(rd.GetInt32(0), rd.GetString(1), rd.GetString(2), rd.GetString(3), rd.GetInt32(4),
            rd.GetDateTime(5), rd.GetDateTime(6), rd.IsDBNull(7) ? null : rd.GetString(7), rd.GetInt64(8));
    }

    [PgFact]
    public async Task SpMoi_DuocInsert_DuCot()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();

        var r = await PostOk(c, Sp(id, "h1"));

        Assert.Equal([id], IdsOk(r));
        Assert.True(r.Results[0].Applied);
        var d = await DocSp(id);
        Assert.NotNull(d);
        Assert.Equal(1, d!.HarvestCount);
        Assert.Equal("Ao thun", d.Name);
        Assert.Equal("Ao thun", d.RawTitle);   // raw lưu là jsonb thật, truy vấn được vào trong
        Assert.Equal(2, d.SoAnh);
        Assert.Equal(99000, d.PriceMin);
        Assert.Equal(May1, d.MachineId);
    }

    /// <summary>Gửi lại CÙNG hash (client retry sau khi mất phản hồi) → vẫn ok=true, KHÔNG ghi lại, KHÔNG tăng đếm.</summary>
    [PgFact]
    public async Task GuiLaiCungHash_KhongTangHarvestCount_KhongDoiGi()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();
        await PostOk(c, Sp(id, "h1"));
        var truoc = await DocSp(id);

        var r = await PostOk(c, Sp(id, "h1", title: "Ten khac nhung cung hash"));

        Assert.Equal([id], IdsOk(r));
        Assert.False(r.Results[0].Applied);   // ok nhưng không ghi gì
        var sau = await DocSp(id);
        Assert.Equal(1, sau!.HarvestCount);
        Assert.Equal("Ao thun", sau.Name);
        Assert.Equal(truoc!.Updated, sau.Updated);
    }

    [PgFact]
    public async Task HashKhac_TangHarvestCount_VaCapNhatCot()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();
        await PostOk(c, Sp(id, "h1"));
        var truoc = await DocSp(id);
        await Task.Delay(20);

        var r = await PostOk(c, Sp(id, "h2", title: "Ao thun moi"));

        Assert.Equal([id], IdsOk(r));
        var sau = await DocSp(id);
        Assert.Equal(2, sau!.HarvestCount);
        Assert.Equal("Ao thun moi", sau.Name);
        Assert.Equal("Ao thun moi", sau.RawTitle);
        Assert.Equal("h2", sau.Hash);
        Assert.True(sau.Updated > truoc!.Updated, "updated_at phải nhảy khi dữ liệu đổi");
        Assert.Equal(truoc.FirstSeen, sau.FirstSeen);
    }

    [PgFact]
    public async Task Lo51Sp_Bi400CaLo_KhongGhiGi()
    {
        var c = await _app.KhachCoTokenAsync();
        var items = Enumerable.Range(0, HarvestedProductLimits.MaxBatch + 1).Select(_ => Sp(IdMoi(), "h")).ToList();

        var res = await c.PostAsJsonAsync(HubRoutes.HarvestedProductsBatch, new HarvestedProductBatchRequest(May1, DateTimeOffset.UtcNow, items));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Null(await DocSp(items[0].ItemId));
    }

    [PgFact]
    public async Task Lo50Sp_DungTran_VaoHet()
    {
        var c = await _app.KhachCoTokenAsync();
        var items = Enumerable.Range(0, HarvestedProductLimits.MaxBatch).Select(_ => Sp(IdMoi(), "h")).ToArray();

        var r = await PostOk(c, items);

        Assert.All(r.Results, x => Assert.True(x.Ok));
    }

    /// <summary>SP hỏng (đủ kiểu, kể cả \u0000 — JSON hợp lệ nhưng jsonb của Postgres từ chối) → ok=false đúng mục đó
    /// kèm reason; mục lành trong cùng lô vẫn vào.</summary>
    [PgFact]
    public async Task SpHong_BiRejectedTungCai_SpKhacVanVao()
    {
        var c = await _app.KhachCoTokenAsync();
        long tot1 = IdMoi(), tot2 = IdMoi(), jsonHong = IdMoi(), mang = IdMoi(), hashRong = IdMoi(), nul = IdMoi(),
            shop0 = IdMoi(), lech = IdMoi(), row0 = IdMoi(), urlDai = IdMoi(), khongItem = IdMoi(), dataNull = IdMoi(),
            loiShopee = IdMoi();

        var r = await PostOk(c,
            Sp(tot1, "h"),
            Sp(jsonHong, "h", rawJson: "{\"data\": {\"item\":"),
            Sp(mang, "h", rawJson: "[1,2,3]"),
            Sp(hashRong, " "),
            Sp(nul, "h", rawJson: "{\"error\":null,\"data\":{\"item\":{\"item_id\":" + nul + ",\"title\":\"a\\u0000b\"}}}"),
            Sp(0, "h"),
            Sp(shop0, "h", shopId: 0),
            Sp(lech, "h", rawJson: "{\"error\":null,\"data\":{\"item\":{\"item_id\":" + (lech + 1) + "}}}"),
            Sp(row0, "h", acct: "acc", sheet: "S", rowNo: 0),
            Sp(urlDai, "h") with { Url = "https://shopee.vn/" + new string('a', HarvestedProductLimits.MaxUrlLength) },
            Sp(IdMoi(), "h", acct: new string('a', HarvestedProductLimits.MaxKeyLength + 1), sheet: "S", rowNo: 2),
            Sp(IdMoi(), "h", acct: "acc", sheet: new string('s', HarvestedProductLimits.MaxKeyLength + 1), rowNo: 2),
            Sp(khongItem, "h", rawJson: "{\"error\":null,\"data\":{\"item\":{\"title\":\"x\"}}}"),
            Sp(dataNull, "h", rawJson: "{\"error\":null,\"data\":null}"),
            Sp(loiShopee, "h", rawJson: "{\"error\":90309999,\"data\":null}"),
            Sp(IdMoi(), "h", acct: "acc", sheet: "S"),                       // thiếu rowNo
            Sp(IdMoi(), "h", rowNo: 3),                                       // chỉ có rowNo
            Sp(IdMoi(), "h") with { HarvestedAt = null },
            Sp(tot2, "h"));

        Assert.Equal([tot1, tot2], IdsOk(r));
        string? Ly(int i) => r.Results[i].Reason;
        Assert.Equal("account-too-long", Ly(10));
        Assert.Equal("sheet-too-long", Ly(11));
        Assert.Equal("raw-no-item", Ly(12));
        Assert.Equal("raw-no-item", Ly(13));
        Assert.Equal("raw-error", Ly(14));
        Assert.Equal("source-partial", Ly(15));
        Assert.Equal("source-partial", Ly(16));
        Assert.Equal("harvested-at", Ly(17));
        Assert.Equal("raw-json", Ly(1));
        Assert.Equal("raw-json", Ly(2));
        Assert.Equal("raw-hash", Ly(3));
        Assert.StartsWith("db 22", Ly(4));     // 22P05: jsonb không nhận \u0000
        Assert.Equal("item-id", Ly(5));
        Assert.Equal("shop-id", Ly(6));
        Assert.Equal("raw-item-mismatch", Ly(7));
        Assert.Equal("row-no", Ly(8));
        Assert.Equal("url-too-long", Ly(9));
        Assert.NotNull(await DocSp(tot1));
        Assert.NotNull(await DocSp(tot2));
        Assert.Null(await DocSp(nul));
        Assert.Null(await DocSp(lech));
        Assert.Null(await DocSp(row0));
    }

    /// <summary>Cùng itemId ở 2 mục (khác dòng nguồn), mục 2 hỏng → kết quả THEO CHỈ SỐ: mục 0 ok, mục 1 hỏng. Nếu
    /// trả theo itemId thì X nằm cả "đã nhận" lẫn "từ chối" → client xoá theo itemId mất mục 1 không ai biết.</summary>
    [PgFact]
    public async Task CungItemId_HaiMuc_MotMucHong_KetQuaTheoChiSo()
    {
        var c = await _app.KhachCoTokenAsync();
        var x = IdMoi();
        var acct = "acc-" + Guid.NewGuid().ToString("N");

        var r = await PostOk(c,
            Sp(x, "h", acct: acct, sheet: "S1", rowNo: 2),
            Sp(x, "h", acct: acct, sheet: "S1", rowNo: 0));

        Assert.True(r.Results[0].Ok);
        Assert.False(r.Results[1].Ok);
        Assert.Equal("row-no", r.Results[1].Reason);
    }

    /// <summary>Ca phản biện tái hiện: sheet 4000 ký tự CJK (~12 KB UTF-8) vượt kiểm độ dài → mục giữa Ok=false, lô vẫn
    /// 200, 2 mục lành vào DB. Trước bản vá: 54000 "index row size exceeds" ném 500 giữa lô → client gửi lại lần
    /// nào cũng 500 ở đúng mục đó, hàng đợi tắc vĩnh viễn.</summary>
    [PgFact]
    public async Task SheetCjk4000KyTu_GiuaHaiMucLanh_Tra200_MucGiuaHong()
    {
        var c = await _app.KhachCoTokenAsync();
        long a = IdMoi(), giua = IdMoi(), b = IdMoi();
        var acct = "acc-" + Guid.NewGuid().ToString("N");

        var r = await PostOk(c,
            Sp(a, "h", acct: acct, sheet: "S1", rowNo: 2),
            // Ký tự NGẪU NHIÊN (seed cố định): chuỗi lặp 1 ký tự bị nén nên index vẫn nhận, không tái hiện được 54000.
            Sp(giua, "h", acct: acct, sheet: CjkNgauNhien(4000), rowNo: 3),
            Sp(b, "h", acct: acct, sheet: "S1", rowNo: 4));

        Assert.True(r.Results[0].Ok);
        Assert.False(r.Results[1].Ok);
        Assert.True(r.Results[2].Ok);
        Assert.NotNull(await DocSp(a));
        Assert.Null(await DocSp(giua));
        Assert.NotNull(await DocSp(b));
    }

    /// <summary>Phân loại SQLSTATE: CHỈ lớp 22/23/54 là lỗi của mục (Ok=false); mọi lớp khác là chuyện của Hub → 500
    /// cả lô cho client giữ lô gửi lại. Đường 500 thật qua HTTP có test riêng (đổi tên bảng); 54000 thật qua HTTP
    /// không dựng được nữa vì kiểm độ dài key chặn trước — nên khoá phân loại ở mức hàm.</summary>
    [Theory]
    [InlineData("22P05", true)]
    [InlineData("23505", true)]
    [InlineData("54000", true)]
    [InlineData("25006", false)]
    [InlineData("55P03", false)]
    [InlineData("XX000", false)]
    [InlineData("42P01", false)]
    [InlineData("08006", false)]
    [InlineData("40P01", false)]
    [InlineData("53100", false)]
    [InlineData("57014", false)]
    [InlineData("58030", false)]
    public void IsItemFaultPg_ChiLop22_23_54(string sqlState, bool loiCuaMuc) =>
        Assert.Equal(loiCuaMuc, ProductDb.IsItemFaultPg(sqlState));

    /// <summary>Body không có MachineId → machine_id lấy header X-Machine-Id; body có thì body thắng.</summary>
    [PgFact]
    public async Task MachineId_BodyRong_LayHeader_BodyCoThiBodyThang()
    {
        var c = await _app.KhachCoTokenAsync("may-header");
        long tuHeader = IdMoi(), tuBody = IdMoi();

        await PostMay(c, null, Sp(tuHeader, "h"));
        await PostMay(c, "may-body", Sp(tuBody, "h"));

        Assert.Equal("may-header", (await DocSp(tuHeader))!.MachineId);
        Assert.Equal("may-body", (await DocSp(tuBody))!.MachineId);
    }

    /// <summary>Liên kết nguồn upsert theo (account, sheet, SP) — migration 5: cùng SP ở dòng khác → cập nhật row_no (dòng
    /// thấy gần nhất); SP khác cùng số dòng → liên kết riêng; không có trường nguồn nào → chỉ lưu SP; có 1–2 trường → mục bị
    /// từ chối source-partial; stats đếm đúng theo lọc.</summary>
    [PgFact]
    public async Task DongNguon_UpsertTheoAccountSheetSP_StatsDemDung()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = "acc-" + Guid.NewGuid().ToString("N");
        long a = IdMoi(), b = IdMoi(), le = IdMoi();

        await PostOk(c, Sp(a, "h", acct: acct, sheet: "Shop1", rowNo: 5), Sp(a, "h", acct: acct, sheet: "Shop1", rowNo: 6));
        await PostOk(c, Sp(b, "h", acct: acct, sheet: "Shop1", rowNo: 5));      // SP b ở dòng 5: liên kết riêng
        await PostOk(c, Sp(a, "h", acct: acct, sheet: "Shop2", rowNo: 5));      // cùng row_no, khác sheet = dòng khác
        await PostOk(c, Sp(le, "h"));                                            // không có trường nguồn → chỉ lưu SP
        var thieu = await PostOk(c, Sp(IdMoi(), "h", acct: acct, sheet: null, rowNo: 7));   // thiếu sheet → từ chối
        Assert.Equal("source-partial", thieu.Results[0].Reason);

        await using (var conn = await _app.MoDbAsync())
        await using (var cmd = new NpgsqlCommand(
            "SELECT sheet, row_no, item_id FROM harvested_product_sources WHERE account_id = $1 ORDER BY sheet, row_no;", conn))
        {
            cmd.Parameters.AddWithValue(acct);
            await using var rd = await cmd.ExecuteReaderAsync();
            var dong = new List<(string, int, long)>();
            while (await rd.ReadAsync()) dong.Add((rd.GetString(0), rd.GetInt32(1), rd.GetInt64(2)));
            Assert.Equal([("Shop1", 5, b), ("Shop1", 6, a), ("Shop2", 5, a)], dong);
        }
        Assert.NotNull(await DocSp(le));   // SP vẫn được lưu dù không nối dòng

        var theoAcc = await c.GetFromJsonAsync<HarvestedProductStats>($"{HubRoutes.HarvestedProductsStats}?account={acct}");
        Assert.Equal(new HarvestedProductStats(3, 2), theoAcc);
        var theoSheet = await c.GetFromJsonAsync<HarvestedProductStats>($"{HubRoutes.HarvestedProductsStats}?account={acct}&sheet=Shop1");
        Assert.Equal(new HarvestedProductStats(2, 2), theoSheet);
        var tatCa = await c.GetFromJsonAsync<HarvestedProductStats>(HubRoutes.HarvestedProductsStats);
        Assert.True(tatCa!.Rows >= 3 && tatCa.Products >= 3, $"không lọc phải đếm toàn bộ (kể cả SP không nối dòng): {tatCa}");
    }

    /// <summary>Migration 5 trên DB đang ở v4 có dữ liệu: cùng (account, sheet, SP) ở nhiều dòng → gộp còn MỘT bản (harvested_at
    /// mới nhất, row_no của bản đó); SP khác cùng số dòng giữ nguyên; sau migration khoá mới chặn trùng SP, cho phép trùng dòng.</summary>
    [PgFact]
    public async Task Migration5_GopTrungTheoSP_DoiKhoa()
    {
        var db = "hub_mig5_test_" + Guid.NewGuid().ToString("N");
        PgAdmin.Exec($"CREATE DATABASE \"{db}\";");
        var conn = new NpgsqlConnectionStringBuilder(PgAdmin.ConnString) { Database = db, Pooling = false }.ConnectionString;
        try
        {
            await using (var pdb = new ProductDb(conn)) await pdb.InitAsync(CancellationToken.None);
            async Task Exec(string sql)
            {
                await using var c = new NpgsqlConnection(conn);
                await c.OpenAsync();
                await using var cmd = new NpgsqlCommand(sql, c);
                await cmd.ExecuteNonQueryAsync();
            }
            // Đưa về đúng hình v4: khoá theo dòng, chưa có bản ghi version 5.
            await Exec(@"ALTER TABLE harvested_product_sources DROP CONSTRAINT ux_hps_acct_sheet_item;
DROP INDEX ix_hps_acct_sheet_row;
ALTER TABLE harvested_product_sources ADD CONSTRAINT harvested_product_sources_account_id_sheet_row_no_key UNIQUE (account_id, sheet, row_no);
DELETE FROM schema_version WHERE version = 5;
INSERT INTO harvested_product_sources (account_id, sheet, row_no, item_id, url, harvested_at) VALUES
 ('a', 'S1', 2, 100, 'u-cu',  '2026-01-01T00:00:00Z'),
 ('a', 'S1', 7, 100, 'u-moi', '2026-01-02T00:00:00Z'),
 ('a', 'S1', 3, 200, 'u2',    '2026-01-01T00:00:00Z'),
 ('a', 'S2', 2, 100, 'u3',    '2026-01-01T00:00:00Z');");

            await using (var pdb = new ProductDb(conn)) await pdb.InitAsync(CancellationToken.None);

            var dong = new List<(string, int, long, string)>();
            await using (var c = new NpgsqlConnection(conn))
            {
                await c.OpenAsync();
                await using var cmd = new NpgsqlCommand(
                    "SELECT sheet, row_no, item_id, url FROM harvested_product_sources ORDER BY sheet, item_id;", c);
                await using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync()) dong.Add((rd.GetString(0), rd.GetInt32(1), rd.GetInt64(2), rd.GetString(3)));
            }
            Assert.Equal([("S1", 7, 100L, "u-moi"), ("S1", 3, 200L, "u2"), ("S2", 2, 100L, "u3")], dong);
            await Exec("INSERT INTO harvested_product_sources (account_id, sheet, row_no, item_id) VALUES ('a', 'S1', 7, 300);");   // trùng dòng: được
            await Assert.ThrowsAsync<PostgresException>(() =>
                Exec("INSERT INTO harvested_product_sources (account_id, sheet, row_no, item_id) VALUES ('a', 'S1', 9, 100);"));   // trùng SP: chặn
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            try { PgAdmin.Exec($"DROP DATABASE IF EXISTS \"{db}\" WITH (FORCE);"); } catch { }
        }
    }

    /// <summary>Ca phản biện vòng 12: workbook dời dòng — SP A lưu ở dòng 5, sau đó SP B cũng ở dòng 5 → cả HAI liên kết "SP thuộc
    /// shop đích" còn nguyên (khoá cũ theo row_no để B đè mất A).</summary>
    [PgFact]
    public async Task HaiSPCungSoDong_GiuCaHaiLienKet()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = "acc-" + Guid.NewGuid().ToString("N");
        long a = IdMoi(), b = IdMoi();
        var t = DateTimeOffset.UtcNow.AddMinutes(-10);

        await PostOk(c, Sp(a, "h", acct: acct, sheet: "S1", rowNo: 5, harvestedAt: t));
        await PostOk(c, Sp(b, "h", acct: acct, sheet: "S1", rowNo: 5, harvestedAt: t.AddMinutes(1)));

        Assert.Equal(5, (await DocNguon(acct, "S1", a)).RowNo);
        Assert.Equal(5, (await DocNguon(acct, "S1", b)).RowNo);
        Assert.Equal(new HarvestedProductStats(2, 2),
            await c.GetFromJsonAsync<HarvestedProductStats>($"{HubRoutes.HarvestedProductsStats}?account={acct}&sheet=S1"));
    }

    /// <summary>Bản cào CŨ (harvested_at nhỏ hơn) tới SAU bản mới — vd. outbox máy A dồn lâu mới đẩy — KHÔNG được
    /// ghi đè SP lẫn dòng nguồn; vẫn ok=true để client xoá khỏi hàng đợi.</summary>
    [PgFact]
    public async Task BanCuToiSauBanMoi_KhongGhiDe_VanOk()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = "acc-" + Guid.NewGuid().ToString("N");
        long moi = IdMoi(), cu = IdMoi();
        var t = DateTimeOffset.UtcNow.AddMinutes(-5);   // lệch hẳn khỏi now() của Hub → bắt được nếu Hub ghi now()

        await PostOk(c, Sp(moi, "h-moi", title: "Ban moi", acct: acct, sheet: "S1", rowNo: 9, harvestedAt: t.AddHours(-2)));
        // Nhánh UPDATE của liên kết nguồn: bản mới hơn (t) của CÙNG SP → row_no = dòng mới, harvested_at = t của client.
        await PostOk(c, Sp(moi, "h-moi", title: "Ban moi", acct: acct, sheet: "S1", rowNo: 2, harvestedAt: t));
        var r = await PostOk(c,
            Sp(moi, "h-cu", title: "Ban cu", harvestedAt: t.AddHours(-1)),
            Sp(moi, "h-moi", acct: acct, sheet: "S1", rowNo: 7, harvestedAt: t.AddHours(-1)));   // liên kết cũ hơn → giữ dòng 2

        Assert.True(r.Results[0].Ok);
        Assert.True(r.Results[1].Ok);
        var d = await DocSp(moi);
        Assert.Equal("Ban moi", d!.Name);
        Assert.Equal("h-moi", d.Hash);
        Assert.Equal(1, d.HarvestCount);
        var (rowNo, luu) = await DocNguon(acct, "S1", moi);
        Assert.Equal(2, rowNo);
        // harvested_at của liên kết nguồn = giá trị CLIENT gửi (đã quy đồng hồ — client test cùng đồng hồ Hub nên chỉ lệch
        // vài ms độ trễ), KHÔNG phải now() của Hub (lệch 5 phút).
        Assert.True((luu - t).Duration() < TimeSpan.FromSeconds(10), $"harvested_at dòng nguồn {luu:O} lệch t {t:O}");
    }

    /// <summary>Ca phản biện tái hiện: Shopee trả lỗi (<c>{"error":90309999,"data":null}</c>) mà client vẫn đẩy lên
    /// với hash khác → trước bản vá ghi ĐÈ bản tốt. Giờ ok=false raw-error, bản tốt giữ nguyên.</summary>
    [PgFact]
    public async Task RawLoiShopee_KhongGhiDeBanTot()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();
        await PostOk(c, Sp(id, "h-tot"));

        var r = await PostOk(c, Sp(id, "h-loi", rawJson: "{\"error\":90309999,\"data\":null}"));

        Assert.Equal("raw-error", r.Results[0].Reason);
        var d = await DocSp(id);
        Assert.Equal("h-tot", d!.Hash);
        Assert.Equal("Ao thun", d.RawTitle);
    }

    /// <summary>error hợp lệ = vắng / null / SỐ 0 (vài endpoint v4 trả 0 khi OK — extension GĐ2 coi là thành công,
    /// Hub từ chối thì SP kẹt mãi ở "lỗi đẩy"). Chuỗi "0", false, số khác 0, object → raw-error.</summary>
    [PgFact]
    public async Task RawError_ChiNhanVangNullHoacSo0()
    {
        var c = await _app.KhachCoTokenAsync();
        var ids = Enumerable.Range(0, 7).Select(_ => IdMoi()).ToArray();
        string Raw(long id, string? err) => "{" + (err is null ? "" : "\"error\":" + err + ",")
            + "\"data\":{\"item\":{\"item_id\":" + id + "}}}";

        var r = await PostOk(c,
            Sp(ids[0], "h", rawJson: Raw(ids[0], "0")),
            Sp(ids[1], "h", rawJson: Raw(ids[1], null)),          // vắng error
            Sp(ids[2], "h", rawJson: Raw(ids[2], "\"0\"")),
            Sp(ids[3], "h", rawJson: Raw(ids[3], "90309999")),
            Sp(ids[4], "h", rawJson: Raw(ids[4], "false")),
            Sp(ids[5], "h", rawJson: Raw(ids[5], "{}")),
            Sp(ids[6], "h", rawJson: Raw(ids[6], "0.5")));

        Assert.True(r.Results[0].Applied, "error:0 phải là body hợp lệ");
        Assert.True(r.Results[1].Applied, "vắng error phải là body hợp lệ");
        for (var i = 2; i < 7; i++) Assert.Equal("raw-error", r.Results[i].Reason);
    }

    /// <summary>Lỗi Postgres KHÔNG phải của mục (ở đây 42P01 — bảng nguồn biến mất, như Hub lệch schema) → 500 CẢ lô
    /// để client giữ lô gửi lại; transaction của mục đang ghi rollback (SP không lọt vào nửa vời). Khôi phục bảng
    /// trong finally. Đổi tên bảng thay vì read-only DB: tác dụng ngay trên connection đang nằm trong pool.</summary>
    [PgFact]
    public async Task LoiPgKhongPhaiCuaMuc_Tra500CaLo_RollbackMucDangGhi()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = "acc-" + Guid.NewGuid().ToString("N");
        var id = IdMoi();
        await using var conn = await _app.MoDbAsync();
        await using (var cmd = new NpgsqlCommand("ALTER TABLE harvested_product_sources RENAME TO hps_tam;", conn))
            await cmd.ExecuteNonQueryAsync();
        HttpResponseMessage res;
        try
        {
            res = await c.PostAsJsonAsync(HubRoutes.HarvestedProductsBatch, new HarvestedProductBatchRequest(May1, DateTimeOffset.UtcNow,
                [Sp(id, "h", acct: acct, sheet: "S1", rowNo: 2)]));
        }
        finally
        {
            await using var cmd = new NpgsqlCommand("ALTER TABLE hps_tam RENAME TO harvested_product_sources;", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.Null(await DocSp(id));   // SP đã insert trong tx rồi mới gãy ở dòng nguồn → phải rollback
    }

    private async Task<DateTimeOffset> DocMoc(long itemId)
    {
        await using var conn = await _app.MoDbAsync();
        await using var cmd = new NpgsqlCommand("SELECT harvested_at FROM harvested_products WHERE item_id = $1;", conn);
        cmd.Parameters.AddWithValue(itemId);
        return new DateTimeOffset((DateTime)(await cmd.ExecuteScalarAsync())!);
    }

    private static void GanBang(DateTimeOffset mong, DateTimeOffset thuc, string ghiChu) =>
        Assert.True((thuc - mong).Duration() < TimeSpan.FromSeconds(10), $"{ghiChu}: mong {mong:O}, thực {thuc:O}");

    /// <summary>Hash TRÙNG mà mốc mới hơn → chỉ nhích harvested_at (Applied=false, không tăng đếm, updated_at giữ). Nhờ
    /// vậy bản hash KHÁC nhưng cũ hơn mốc mới tới sau KHÔNG lật được: (h1,01h) → (h1,09h) → (h0,05h) ⇒ vẫn h1.</summary>
    [PgFact]
    public async Task HashTrungMocMoiHon_ChiNhichMoc_BanCuHashKhacKhongLatDuoc()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();
        var goc = DateTimeOffset.UtcNow.AddHours(-10);

        var r1 = await PostOk(c, Sp(id, "h1", harvestedAt: goc.AddHours(1)));
        var truoc = await DocSp(id);
        var r2 = await PostOk(c, Sp(id, "h1", title: "khong duoc ghi", harvestedAt: goc.AddHours(9)));
        var giua = await DocSp(id);
        var mocGiua = await DocMoc(id);
        var r3 = await PostOk(c, Sp(id, "h0", title: "Ban cu hash khac", harvestedAt: goc.AddHours(5)));

        Assert.True(r1.Results[0].Applied);
        Assert.True(r2.Results[0].Ok);
        Assert.False(r2.Results[0].Applied);
        Assert.Equal(1, giua!.HarvestCount);
        Assert.Equal(truoc!.Updated, giua.Updated);
        Assert.Equal("Ao thun", giua.Name);
        GanBang(goc.AddHours(9), mocGiua, "hash trùng mốc mới hơn phải nhích harvested_at");
        Assert.True(r3.Results[0].Ok);
        Assert.False(r3.Results[0].Applied);
        var sau = await DocSp(id);
        Assert.Equal("h1", sau!.Hash);
        Assert.Equal("Ao thun", sau.Name);
    }

    /// <summary>HarvestedAt mang offset +07:00 (DateTimeOffset.Now của máy VN) → ghi đúng thời điểm. Npgsql chỉ nhận
    /// offset 0 cho timestamptz, thiếu .ToUniversalTime() là ném (không phải PostgresException → 500).</summary>
    [PgFact]
    public async Task HarvestedAtOffsetCong7_GhiDungThoiDiem()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();
        var at = DateTimeOffset.UtcNow.AddMinutes(-3).ToOffset(TimeSpan.FromHours(7));

        var r = await PostLo(c, May1, DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)), Sp(id, "h", harvestedAt: at));

        Assert.True(r.Results[0].Applied);
        GanBang(at, await DocMoc(id), "offset +07");
    }

    private async Task<(int RowNo, DateTimeOffset At)> DocNguon(string acct, string sheet, long itemId)
    {
        await using var conn = await _app.MoDbAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT row_no, harvested_at FROM harvested_product_sources WHERE account_id = $1 AND sheet = $2 AND item_id = $3;", conn);
        cmd.Parameters.AddWithValue(acct);
        cmd.Parameters.AddWithValue(sheet);
        cmd.Parameters.AddWithValue(itemId);
        await using var rd = await cmd.ExecuteReaderAsync();
        Assert.True(await rd.ReadAsync(), "không thấy liên kết nguồn");
        var kq = (rd.GetInt32(0), new DateTimeOffset(rd.GetDateTime(1)));
        Assert.False(await rd.ReadAsync(), "mỗi (account, sheet, SP) chỉ một liên kết");
        return kq;
    }

    /// <summary>Máy chạy NHANH 20 giờ: ClientNow và HarvestedAt cùng +20h → Hub quy về "bây giờ" cho CẢ SP lẫn dòng
    /// nguồn; bản cào đúng giờ của máy khác tới sau (hash khác, cùng dòng nguồn trỏ SP khác) vẫn ghi đè được cả hai —
    /// không bị bản +20h chặn mãi.</summary>
    [PgFact]
    public async Task MayChayNhanh20h_QuyVeGioHub_KhongChanBanDungGioToiSau()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = "acc-" + Guid.NewGuid().ToString("N");
        long id = IdMoi(), id2 = IdMoi();
        var nhanh = DateTimeOffset.UtcNow.AddHours(20);

        var r1 = await PostLo(c, May1, nhanh, Sp(id, "h-nhanh", acct: acct, sheet: "S1", rowNo: 2, harvestedAt: nhanh));
        GanBang(DateTimeOffset.UtcNow, await DocMoc(id), "mốc SP máy nhanh phải quy về giờ Hub");
        GanBang(DateTimeOffset.UtcNow, (await DocNguon(acct, "S1", id)).At, "mốc dòng nguồn máy nhanh phải quy về giờ Hub");
        await Task.Delay(50);
        var r2 = await PostLo(c, May1, DateTimeOffset.UtcNow,
            Sp(id, "h-dung", acct: acct, sheet: "S1", rowNo: 3, harvestedAt: DateTimeOffset.UtcNow),
            Sp(id2, "h", acct: acct, sheet: "S1", rowNo: 2, harvestedAt: DateTimeOffset.UtcNow));

        Assert.True(r1.Results[0].Applied);
        Assert.True(r2.Results[0].Applied);
        Assert.Equal("h-dung", (await DocSp(id))!.Hash);
        Assert.Equal(3, (await DocNguon(acct, "S1", id)).RowNo);   // không bị liên kết +20h chặn
        Assert.Equal(2, (await DocNguon(acct, "S1", id2)).RowNo);
    }

    /// <summary>Nhích mốc (hash trùng) cũng dùng mốc ĐÃ QUY: máy chậm 3 ngày gửi lại cùng hash, cào cách đây 1h theo
    /// đồng hồ thật → mốc SP nhích lên ≈ giờ Hub − 1h (bản thô −3 ngày thì cũ hơn mốc đang giữ, sẽ không nhích).</summary>
    [PgFact]
    public async Task NhichMoc_DungMocDaQuyDongHo()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();
        var that = DateTimeOffset.UtcNow;
        await PostOk(c, Sp(id, "h1", harvestedAt: that.AddHours(-10)));

        var cham = that.AddDays(-3);
        var r = await PostLo(c, May1, cham, Sp(id, "h1", harvestedAt: cham.AddHours(-1)));

        Assert.True(r.Results[0].Ok);
        Assert.False(r.Results[0].Applied);
        GanBang(that.AddHours(-1), await DocMoc(id), "nhích mốc phải dùng mốc đã quy đồng hồ");
    }

    /// <summary>Máy chạy CHẬM 3 ngày: ClientNow và HarvestedAt cùng −3 ngày → ghi bình thường, mốc lưu ≈ giờ Hub.</summary>
    [PgFact]
    public async Task MayChayCham3Ngay_GhiBinhThuong_MocQuaVeGioHub()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();
        var cham = DateTimeOffset.UtcNow.AddDays(-3);

        var r = await PostLo(c, May1, cham, Sp(id, "h", harvestedAt: cham));

        Assert.True(r.Results[0].Applied);
        GanBang(DateTimeOffset.UtcNow, await DocMoc(id), "mốc máy chậm phải quy về giờ Hub");
    }

    /// <summary>Sau khi quy đồng hồ: mốc TƯƠNG LAI được nhận nhưng KẸP về giờ Hub (đồng hồ máy bị kéo lùi giữa lúc cào
    /// và lúc gửi không được làm mất mục); mốc cũ bao lâu cũng nhận.</summary>
    [PgFact]
    public async Task MocSauQuyDongHo_TuongLaiBiKepVeGioHub_CuVanNhan()
    {
        var c = await _app.KhachCoTokenAsync();
        long tuongLai = IdMoi(), ratCu = IdMoi();
        var bay = DateTimeOffset.UtcNow;

        var r = await PostLo(c, May1, bay,
            Sp(tuongLai, "h", harvestedAt: bay.AddMinutes(10)),
            Sp(ratCu, "h", harvestedAt: bay.AddDays(-61)));

        Assert.True(r.Results[0].Applied);
        Assert.True(r.Results[1].Applied);
        var moc = await DocMoc(tuongLai);
        Assert.True(moc <= DateTimeOffset.UtcNow, $"mốc tương lai phải bị kẹp về giờ Hub: {moc:O}");
        GanBang(DateTimeOffset.UtcNow, moc, "mốc tương lai kẹp về giờ Hub");
        GanBang(bay.AddDays(-61), await DocMoc(ratCu), "mốc cũ 61 ngày giữ nguyên");
    }

    /// <summary>HarvestedAt + skew TRÀN số (MaxValue với máy chậm, MinValue với máy nhanh) → harvested-at cho RIÊNG mục
    /// đó, lô vẫn 200 (không được ném ArgumentOutOfRange thành 500 cả lô).</summary>
    [PgFact]
    public async Task HarvestedAtTranSo_ChiMucDoHong_LoVan200()
    {
        var c = await _app.KhachCoTokenAsync();
        long tot = IdMoi();

        var rCham = await PostLo(c, May1, DateTimeOffset.UtcNow.AddDays(-1),   // skew ≈ +1 ngày
            Sp(IdMoi(), "h", harvestedAt: DateTimeOffset.MaxValue),
            Sp(tot, "h", harvestedAt: DateTimeOffset.UtcNow.AddDays(-1)));
        var rNhanh = await PostLo(c, May1, DateTimeOffset.UtcNow.AddDays(1),   // skew ≈ −1 ngày
            Sp(IdMoi(), "h", harvestedAt: DateTimeOffset.MinValue.AddHours(1)));

        Assert.Equal("harvested-at", rCham.Results[0].Reason);
        Assert.True(rCham.Results[1].Applied);
        Assert.Equal("harvested-at", rNhanh.Results[0].Reason);
    }

    [PgFact]
    public async Task ThieuClientNow_Bi400CaLo()
    {
        var c = await _app.KhachCoTokenAsync();
        var id = IdMoi();

        var res = await c.PostAsJsonAsync(HubRoutes.HarvestedProductsBatch, new HarvestedProductBatchRequest(May1, null, [Sp(id, "h")]));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("client-now", await res.Content.ReadAsStringAsync());
        Assert.Null(await DocSp(id));
    }

    /// <summary>item_id dạng CHUỖI SỐ được nhận; chuỗi có khoảng trắng / dấu + bị loại (NumberStyles.None);
    /// data.item.shop_id lệch ShopId → raw-shop-mismatch; shop_id vắng thì bỏ qua.</summary>
    [PgFact]
    public async Task RawId_ChuoiSo_VaShopId()
    {
        var c = await _app.KhachCoTokenAsync();
        long chuoi = IdMoi(), trang = IdMoi(), cong = IdMoi(), lechShop = IdMoi(), vangShop = IdMoi();
        string Raw(string itemId, string? shop) =>
            "{\"error\":null,\"data\":{\"item\":{\"item_id\":" + itemId + (shop is null ? "" : ",\"shop_id\":" + shop) + "}}}";

        var r = await PostOk(c,
            Sp(chuoi, "h", rawJson: Raw("\"" + chuoi + "\"", "\"123456\"")),
            Sp(trang, "h", rawJson: Raw("\" " + trang + "\"", null)),
            Sp(cong, "h", rawJson: Raw("\"+" + cong + "\"", null)),
            Sp(lechShop, "h", rawJson: Raw(lechShop.ToString(), "999")),
            Sp(vangShop, "h", rawJson: Raw(vangShop.ToString(), null)));

        Assert.True(r.Results[0].Applied);
        Assert.Equal("raw-no-item", r.Results[1].Reason);
        Assert.Equal("raw-no-item", r.Results[2].Reason);
        Assert.Equal("raw-shop-mismatch", r.Results[3].Reason);
        Assert.True(r.Results[4].Applied);
    }

    /// <summary>SourceUrl (link GỐC của dòng) vào <c>harvested_product_sources.url</c>; <c>harvested_products.url</c> vẫn
    /// là link đã làm sạch. Client cũ không gửi SourceUrl → dòng nguồn lấy Url. SourceUrl quá dài → từ chối mục.</summary>
    [PgFact]
    public async Task SourceUrl_VaoDongNguon_UrlSachVaoSp_VangThiLayUrl()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = "acc-" + Guid.NewGuid().ToString("N");
        long a = IdMoi(), b = IdMoi();
        const string goc = "https://shopee.vn/Ao-thun-i.123456.1%7DD?sp_atk=x";

        var r = await PostOk(c,
            Sp(a, "h", acct: acct, sheet: "S", rowNo: 1) with { SourceUrl = goc },
            Sp(b, "h", acct: acct, sheet: "S", rowNo: 2),
            Sp(IdMoi(), "h", acct: acct, sheet: "S", rowNo: 3)
                with { SourceUrl = "https://shopee.vn/" + new string('a', HarvestedProductLimits.MaxUrlLength) });

        Assert.True(r.Results[0].Ok);
        Assert.True(r.Results[1].Ok);
        Assert.Equal("source-url-too-long", r.Results[2].Reason);
        await using var conn = await _app.MoDbAsync();
        async Task<string> Doc(string sql, object p)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue(p);
            return (string)(await cmd.ExecuteScalarAsync())!;
        }
        Assert.Equal(goc, await Doc("SELECT url FROM harvested_product_sources WHERE account_id = $1 AND row_no = 1;", acct));
        Assert.Equal($"https://shopee.vn/product/123456/{a}", await Doc("SELECT url FROM harvested_products WHERE item_id = $1;", a));
        Assert.Equal($"https://shopee.vn/product/123456/{b}",
            await Doc("SELECT url FROM harvested_product_sources WHERE account_id = $1 AND row_no = 2;", acct));
    }

    /// <summary>Lỗi PHÍA HUB (mất kết nối Postgres; PostgresException lớp 53/57/08/40) → 503 pg-unavailable, để client
    /// không coi là "mục hỏng" (500 lặp ở một SP → đánh lỗi SP). Lớp khác và NpgsqlOperationInProgressException vẫn ném
    /// (→ 500). Gọi thẳng lớp bọc với ProductDb THẬT đã sẵn sàng: không dựng được cảnh socket đứt / đầy kết nối đúng lúc qua
    /// HTTP thật (Npgsql mở kết nối mới cho mỗi request, Postgres test luôn sống).</summary>
    [PgFact]
    public async Task LoiPhiaHub_Thi503PgUnavailable_LoiKhacVanNem()
    {
        await _app.KhachCoTokenAsync();

        var r = await Api.ProductApiEndpoints.WithPgHarvest(_app.Services,
            _ => throw new NpgsqlException("Exception while reading from stream", new IOException("socket đứt")));

        var sc = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(r);
        Assert.Equal(503, sc.StatusCode);
        var val = Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IValueHttpResult>(r).Value;
        Assert.Contains("pg-unavailable", System.Text.Json.JsonSerializer.Serialize(val));

        // Hết chờ / cạn pool (NpgsqlException bọc TimeoutException) cũng là Hub hỏng → 503.
        var t = await Api.ProductApiEndpoints.WithPgHarvest(_app.Services,
            _ => throw new NpgsqlException("The connection pool has been exhausted", new TimeoutException()));
        Assert.Equal(503, Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(t).StatusCode);

        // Postgres còn sống nhưng hết tài nguyên / bị operator ngắt / deadlock → lỗi phía Hub → 503.
        foreach (var state in new[] { "53300", "53100", "53200", "57P01", "57014", "08006", "40P01", "40001" })
        {
            var pr = await Api.ProductApiEndpoints.WithPgHarvest(_app.Services,
                _ => throw new PostgresException("x", "FATAL", "FATAL", state));
            Assert.True(503 == Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(pr).StatusCode, state);
        }
        // Lớp khác (lệch schema 42, lỗi nội bộ XX) = lỗi code/triển khai Hub → vẫn ném (500).
        foreach (var state in new[] { "42P01", "XX000", "25006" })
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(() => Api.ProductApiEndpoints.WithPgHarvest(_app.Services,
                _ => throw new PostgresException("x", "ERROR", "ERROR", state)));
            Assert.Equal(state, ex.SqlState);
        }
        // Dùng một kết nối cho hai lệnh cùng lúc = lỗi CODE Hub → ném tiếp (500), không giả làm "Pg mất kết nối".
        await Assert.ThrowsAsync<NpgsqlOperationInProgressException>(() => Api.ProductApiEndpoints.WithPgHarvest(_app.Services,
            _ => throw new NpgsqlOperationInProgressException(new NpgsqlCommand("SELECT 1"))));
        var ok = await Api.ProductApiEndpoints.WithPgHarvest(_app.Services, _ => Task.FromResult(Microsoft.AspNetCore.Http.Results.Ok()));
        Assert.Equal(200, Assert.IsAssignableFrom<Microsoft.AspNetCore.Http.IStatusCodeHttpResult>(ok).StatusCode);
    }

    [PgFact]
    public async Task KhongCoToken_Thi401()
    {
        await _app.KhachCoTokenAsync();
        var c = _app.CreateClient();

        var res = await c.PostAsJsonAsync(HubRoutes.HarvestedProductsBatch, new HarvestedProductBatchRequest(May1, DateTimeOffset.UtcNow, []));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
