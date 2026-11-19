using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Shopee.Core.Coordination;
using static Shopee.Hub.Web.Tests.HarvestedProductsHttpTests;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// <b>Đọc SP đã harvest theo dòng nguồn (<c>GET /api/harvested-products/rows</c>) và theo itemId
/// (<c>POST /api/harvested-products/by-items</c>)</b> — host thật + Postgres thật, dùng
/// lại fixture của <see cref="HarvestedProductsHttpTests"/> (database tạm riêng cho lớp này). Dữ liệu nạp qua đúng route
/// batch để khoá cùng cách account/sheet đang được lưu. Mỗi test dùng account riêng (Guid) để không giẫm nhau.
/// </summary>
public sealed class HarvestedRowsHttpTests : IClassFixture<HarvestHubApp>
{
    private readonly HarvestHubApp _app;

    public HarvestedRowsHttpTests(HarvestHubApp app) => _app = app;

    private static long IdMoi() => Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);
    private static string AcctMoi() => "acct-" + Guid.NewGuid().ToString("N")[..10];

    private static HarvestedProductItem Sp(long itemId, string acct, string sheet, int rowNo, string title = "Ao thun") =>
        new(itemId, 777, $"https://shopee.vn/product/777/{itemId}", title, 100352, [100017], 0, 99000, 129000, 150000, 1, 1,
            ["https://down-vn.img.susercontent.com/file/a"], null, null,
            "{\"error\":null,\"data\":{\"item\":{\"item_id\":" + itemId + ",\"shop_id\":777,\"title\":\"" + title + "\"}}}",
            "h-" + itemId, acct, sheet, rowNo, DateTimeOffset.UtcNow, $"https://shopee.vn/goc-{itemId}?row={rowNo}");

    private static async Task Nap(HttpClient c, IEnumerable<HarvestedProductItem> items)
    {
        foreach (var lo in items.Chunk(HarvestedProductLimits.MaxBatch))
        {
            var res = await c.PostAsJsonAsync(HubRoutes.HarvestedProductsBatch,
                new HarvestedProductBatchRequest("may-test", DateTimeOffset.UtcNow, [.. lo]));
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = (await res.Content.ReadFromJsonAsync<HarvestedProductBatchResponse>())!;
            Assert.All(body.Results, r => Assert.True(r.Ok, r.Reason));
        }
    }

    private static string Url(string acct, string sheet, int? from = null, int? to = null, HarvestedRowsCursor? after = null, int? limit = null)
    {
        var q = $"{HubRoutes.HarvestedProductsRows}?account={Uri.EscapeDataString(acct)}&sheet={Uri.EscapeDataString(sheet)}";
        if (from is not null) q += $"&from={from}";
        if (to is not null) q += $"&to={to}";
        if (after is not null) q += $"&afterRow={after.AfterRow}&afterItemId={after.AfterItemId}";
        if (limit is not null) q += $"&limit={limit}";
        return q;
    }

    private static async Task<HarvestedRowsPage> Trang(HttpClient c, string url)
    {
        var res = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<HarvestedRowsPage>())!;
    }

    /// <summary>Đọc hết các trang theo con trỏ; trả cả số trang để khoá cỡ trang.</summary>
    private static async Task<(List<HarvestedSourceRow> Rows, List<int> CoTrang)> DocHet(HttpClient c, string acct, string sheet,
        int? from = null, int? to = null, int? limit = null)
    {
        var all = new List<HarvestedSourceRow>();
        var co = new List<int>();
        HarvestedRowsCursor? after = null;
        for (var i = 0; i < 100; i++)
        {
            var p = await Trang(c, Url(acct, sheet, from, to, after, limit));
            all.AddRange(p.Rows);
            co.Add(p.Rows.Count);
            if (p.Next is null) return (all, co);
            after = p.Next;
        }
        throw new InvalidOperationException("con trỏ không tiến");
    }

    [PgFact]
    public async Task PhanTrang_50MoiTrang_DuVaDungThuTu_TrangCuoiNextNull()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = AcctMoi();
        var items = Enumerable.Range(1, 120).Select(r => Sp(IdMoi(), acct, "S1", r)).ToList();
        await Nap(c, items);

        var (rows, co) = await DocHet(c, acct, "S1");

        Assert.Equal([50, 50, 20], co);
        Assert.Equal(Enumerable.Range(1, 120), rows.Select(r => r.RowNo));
        Assert.Equal(items.Select(i => i.ItemId), rows.Select(r => r.ItemId));
    }

    [PgFact]
    public async Task TrangVuaDuLimit_HetDuLieu_NextNull_KhongBatGoiThem()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = AcctMoi();
        await Nap(c, Enumerable.Range(1, 50).Select(r => Sp(IdMoi(), acct, "S1", r)));

        var p = await Trang(c, Url(acct, "S1"));

        Assert.Equal(50, p.Rows.Count);
        Assert.Null(p.Next);
    }

    [PgFact]
    public async Task LimitQuaTran_BiKepVe50()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = AcctMoi();
        await Nap(c, Enumerable.Range(1, 60).Select(r => Sp(IdMoi(), acct, "S1", r)));

        var p = await Trang(c, Url(acct, "S1", limit: 500));

        Assert.Equal(HarvestedProductLimits.MaxRowsPage, p.Rows.Count);
        Assert.NotNull(p.Next);
    }

    [PgFact]
    public async Task LocDaiDong_ChiTraDongTrongKhoang()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = AcctMoi();
        await Nap(c, Enumerable.Range(1, 10).Select(r => Sp(IdMoi(), acct, "S1", r)));

        var (rows, _) = await DocHet(c, acct, "S1", from: 3, to: 6);
        Assert.Equal([3, 4, 5, 6], rows.Select(r => r.RowNo));

        var (tu8, _) = await DocHet(c, acct, "S1", from: 8);
        Assert.Equal([8, 9, 10], tu8.Select(r => r.RowNo));
    }

    [PgFact]
    public async Task KhongLoAccountSheetKhac_KeCaKhacHoaThuong()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = AcctMoi();
        var cuaToi = Sp(IdMoi(), acct, "S1", 1);
        await Nap(c, [
            cuaToi,
            Sp(IdMoi(), acct, "S2", 1),                     // sheet khác
            Sp(IdMoi(), acct + "-x", "S1", 1),              // account khác
            Sp(IdMoi(), acct.ToUpperInvariant(), "S1", 1),  // khác hoa thường: batch lưu nguyên chuỗi → là account khác
            Sp(IdMoi(), acct, "s1", 1),
        ]);

        var (rows, _) = await DocHet(c, acct, "S1");

        Assert.Equal([cuaToi.ItemId], rows.Select(r => r.ItemId));
    }

    [PgFact]
    public async Task DongKhongCoSp_KhongTra()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = AcctMoi();
        var co = Sp(IdMoi(), acct, "S1", 1);
        await Nap(c, [co]);
        // Liên kết mồ côi (SP không có trong harvested_products) — không có FK nên chèn thẳng được.
        await using (var conn = await _app.MoDbAsync())
        {
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO harvested_product_sources (account_id, sheet, row_no, item_id, url) VALUES ($1, 'S1', 2, $2, 'x');", conn);
            cmd.Parameters.AddWithValue(acct);
            cmd.Parameters.AddWithValue(IdMoi());
            await cmd.ExecuteNonQueryAsync();
        }

        var (rows, _) = await DocHet(c, acct, "S1");

        Assert.Equal([1], rows.Select(r => r.RowNo));
    }

    [PgFact]
    public async Task MotDongNhieuSp_ConTroTachGiuaDong_KhongMatKhongLap()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = AcctMoi();
        // Migration 5: khoá (account, sheet, item) → 2 SP cùng row_no 5 là 2 liên kết.
        var a = Sp(IdMoi(), acct, "S1", 5);
        var b = Sp(IdMoi(), acct, "S1", 5);
        var d = Sp(IdMoi(), acct, "S1", 6);
        await Nap(c, [a, b, d]);

        var (rows, co) = await DocHet(c, acct, "S1", limit: 1);

        Assert.Equal([1, 1, 1], co);
        Assert.Equal(new[] { a.ItemId, b.ItemId }.Order().Append(d.ItemId), rows.Select(r => r.ItemId));
    }

    [PgFact]
    public async Task Raw_LaJsonLong_UrlLaLinkGocCuaDong_DuCot()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = AcctMoi();
        var sp = Sp(IdMoi(), acct, "S1", 7, title: "Vay hoa");
        await Nap(c, [sp]);

        var res = await c.GetAsync(Url(acct, "S1"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var row = doc.RootElement.GetProperty("rows")[0];

        Assert.Equal(7, row.GetProperty("rowNo").GetInt32());
        Assert.Equal(sp.SourceUrl, row.GetProperty("url").GetString());
        Assert.Equal(sp.ItemId, row.GetProperty("itemId").GetInt64());
        Assert.Equal(777, row.GetProperty("shopId").GetInt64());
        var at = row.GetProperty("harvestedAt").GetDateTimeOffset();
        Assert.True(Math.Abs((at - DateTimeOffset.UtcNow).TotalMinutes) < 5);
        var linked = row.GetProperty("linkedAt").GetDateTimeOffset();
        Assert.True(Math.Abs((linked - DateTimeOffset.UtcNow).TotalMinutes) < 5);
        var raw = row.GetProperty("raw");
        Assert.Equal(JsonValueKind.Object, raw.ValueKind);   // lồng, không phải chuỗi escape
        Assert.Equal(sp.ItemId, raw.GetProperty("data").GetProperty("item").GetProperty("item_id").GetInt64());
        Assert.Equal("Vay hoa", raw.GetProperty("data").GetProperty("item").GetProperty("title").GetString());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("next").ValueKind);
    }

    [PgFact]
    public async Task ThieuAccountHoacSheet_HoacDaiNguoc_400()
    {
        var c = await _app.KhachCoTokenAsync();

        var r1 = await c.GetAsync($"{HubRoutes.HarvestedProductsRows}?sheet=S1");
        var r2 = await c.GetAsync($"{HubRoutes.HarvestedProductsRows}?account=a");
        var r3 = await c.GetAsync(Url("a", "S1", from: 9, to: 3));

        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);
        Assert.Contains("account-sheet-required", await r1.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, r3.StatusCode);
        Assert.Contains("bad-range", await r3.Content.ReadAsStringAsync());
    }

    [PgFact]
    public async Task KhongToken_401()
    {
        await _app.KhachCoTokenAsync();   // seed token + chờ Pg
        using var anon = _app.CreateClient();

        var res = await anon.GetAsync(Url("a", "S1"));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [PgFact]
    public async Task LinkedAt_LaMocLienKet_KhacHarvestedAtCuaRaw()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = AcctMoi();
        var id = IdMoi();
        // Lần 1: liên kết acct/S1 lúc 2 giờ trước. Lần 2: CÙNG SP (cùng hash) cào lại ở shop khác → raw nhích mốc lên
        // hiện tại, còn liên kết acct/S1 giữ mốc cũ.
        await Nap(c, [Sp(id, acct, "S1", 1) with { HarvestedAt = DateTimeOffset.UtcNow.AddHours(-2) }]);
        await Nap(c, [Sp(id, acct + "-khac", "S1", 1)]);

        var row = (await DocHet(c, acct, "S1")).Rows.Single();

        Assert.True(Math.Abs((row.LinkedAt - DateTimeOffset.UtcNow.AddHours(-2)).TotalMinutes) < 5, $"linkedAt={row.LinkedAt:o}");
        Assert.True(Math.Abs((row.HarvestedAt - DateTimeOffset.UtcNow).TotalMinutes) < 5, $"harvestedAt={row.HarvestedAt:o}");
    }

    // ── by-items ──

    private static async Task<HttpResponseMessage> PostIds(HttpClient c, object body) =>
        await c.PostAsJsonAsync(HubRoutes.HarvestedProductsByItems, body);

    [PgFact]
    public async Task ByItems_TraDung_BoIdChuaCo_KhuTrung_GiuThuTuGui()
    {
        var c = await _app.KhachCoTokenAsync();
        var acct = AcctMoi();
        // 3 id x < y < z, gửi theo thứ tự y, z, x — không trùng thứ tự tăng lẫn giảm, nên chỉ đúng khi Hub giữ thứ tự gửi.
        var ids = Enumerable.Range(0, 3).Select(_ => IdMoi()).Order().ToArray();
        var x = Sp(ids[0], acct, "S1", 3, title: "Ba");
        var a = Sp(ids[2], acct, "S1", 1, title: "Mot");
        var b = Sp(ids[1], acct, "S1", 2, title: "Hai");
        await Nap(c, [a, b, x]);
        var chuaCo = IdMoi();

        var res = await PostIds(c, new HarvestedByItemsRequest([b.ItemId, chuaCo, a.ItemId, x.ItemId, b.ItemId]));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("items");
        Assert.Equal(3, items.GetArrayLength());
        Assert.Equal(b.ItemId, items[0].GetProperty("itemId").GetInt64());
        Assert.Equal(a.ItemId, items[1].GetProperty("itemId").GetInt64());
        Assert.Equal(x.ItemId, items[2].GetProperty("itemId").GetInt64());
        Assert.Equal(777, items[1].GetProperty("shopId").GetInt64());
        Assert.True(Math.Abs((items[1].GetProperty("harvestedAt").GetDateTimeOffset() - DateTimeOffset.UtcNow).TotalMinutes) < 5);
        var raw = items[1].GetProperty("raw");
        Assert.Equal(JsonValueKind.Object, raw.ValueKind);   // lồng, không phải chuỗi escape
        Assert.Equal("Mot", raw.GetProperty("data").GetProperty("item").GetProperty("title").GetString());
        Assert.Equal(a.ItemId, raw.GetProperty("data").GetProperty("item").GetProperty("item_id").GetInt64());
    }

    [PgFact]
    public async Task ByItems_SpKhongCoDongNguon_VanTra()
    {
        var c = await _app.KhachCoTokenAsync();
        var sp = Sp(IdMoi(), "x", "S1", 1) with { AccountId = null, Sheet = null, RowNo = null };
        await Nap(c, [sp]);

        var body = (await (await PostIds(c, new HarvestedByItemsRequest([sp.ItemId]))).Content
            .ReadFromJsonAsync<HarvestedByItemsResponse>())!;

        Assert.Equal([sp.ItemId], body.Items.Select(i => i.ItemId));
    }

    [PgFact]
    public async Task ByItems_Qua50_400_Dung50_200_TrungCungDem()
    {
        var c = await _app.KhachCoTokenAsync();
        var ids50 = Enumerable.Range(0, 50).Select(_ => IdMoi()).ToList();

        var ok = await PostIds(c, new HarvestedByItemsRequest(ids50));
        var qua = await PostIds(c, new HarvestedByItemsRequest([.. ids50, IdMoi()]));
        var quaVoiTrung = await PostIds(c, new HarvestedByItemsRequest([.. ids50, ids50[0]]));

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Empty((await ok.Content.ReadFromJsonAsync<HarvestedByItemsResponse>())!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, qua.StatusCode);
        Assert.Contains("too-many-ids", await qua.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, quaVoiTrung.StatusCode);
    }

    [PgFact]
    public async Task ByItems_ThieuItemIds_400_MangRong_200Rong()
    {
        var c = await _app.KhachCoTokenAsync();

        var thieu = await PostIds(c, new { });
        var rong = await PostIds(c, new HarvestedByItemsRequest([]));

        Assert.Equal(HttpStatusCode.BadRequest, thieu.StatusCode);
        Assert.Contains("item-ids-required", await thieu.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, rong.StatusCode);
        Assert.Empty((await rong.Content.ReadFromJsonAsync<HarvestedByItemsResponse>())!.Items);
    }

    [PgFact]
    public async Task ByItems_KhongToken_401()
    {
        await _app.KhachCoTokenAsync();
        using var anon = _app.CreateClient();

        var res = await PostIds(anon, new HarvestedByItemsRequest([1]));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
