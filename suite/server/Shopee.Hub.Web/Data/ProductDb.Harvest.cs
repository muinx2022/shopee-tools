using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Shopee.Core.Coordination;

namespace Shopee.Hub;

/// <summary>
/// Phần ProductDb: kho dữ liệu SP Shopee do app Harvest cào (bảng <c>harvested_products</c> +
/// <c>harvested_product_sources</c>, migration 4; khoá dòng nguồn đổi sang (account_id, sheet, item_id) ở migration 5). SQL
/// viết TAY như các phần khác.
/// </summary>
public sealed partial class ProductDb
{
    /// <summary>Kiểm 1 SP trước khi ghi. null = hợp lệ; khác null = lý do từ chối (ổn định, client có thể log/đếm).
    /// Tách hàm thuần để test được không cần Postgres. <paramref name="skew"/> = giờ Hub − ClientNow của lô (xem
    /// <see cref="UpsertHarvestedAsync"/>).</summary>
    internal static string? ValidateHarvested(HarvestedProductItem? it, DateTimeOffset now, TimeSpan skew)
    {
        if (it is null) return "item-null";
        if (it.ItemId <= 0) return "item-id";
        if (it.ShopId <= 0) return "shop-id";
        if (string.IsNullOrWhiteSpace(it.RawHash)) return "raw-hash";
        // Mốc cào BẮT BUỘC: thiếu thì không biết bản nào mới hơn; quy về đồng hồ Hub mà tràn số thì cũng vậy.
        if (it.HarvestedAt is not { } raw || raw == default || QuyMoc(raw, skew, now) is null) return "harvested-at";
        if (it.AccountId is { Length: > HarvestedProductLimits.MaxKeyLength }) return "account-too-long";
        if (it.Sheet is { Length: > HarvestedProductLimits.MaxKeyLength }) return "sheet-too-long";
        if (it.Url is { Length: > HarvestedProductLimits.MaxUrlLength }) return "url-too-long";
        if (it.SourceUrl is { Length: > HarvestedProductLimits.MaxUrlLength }) return "source-url-too-long";
        if (it.RowNo is < 1) return "row-no";
        // Bộ ba dòng nguồn: đủ cả ba hoặc không có gì. Thiếu 1–2 = client ghép sai → lặng lẽ bỏ nối dòng là mất dấu.
        var soTruongNguon = (string.IsNullOrWhiteSpace(it.AccountId) ? 0 : 1) + (string.IsNullOrWhiteSpace(it.Sheet) ? 0 : 1)
                            + (it.RowNo is null ? 0 : 1);
        if (soTruongNguon is 1 or 2) return "source-partial";
        if (string.IsNullOrWhiteSpace(it.RawJson)) return "raw-json";
        try
        {
            using var doc = JsonDocument.Parse(it.RawJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return "raw-json";
            // PDP HỢP LỆ mới được ghi: Shopee trả lỗi (vd {"error":90309999,"data":null}) mà lọt vào thì ghi đè mất bản tốt.
            // error hợp lệ = vắng, null, hoặc SỐ 0 (vài endpoint v4 trả 0 khi OK — extension GĐ2 cũng coi 0 là thành
            // công). Mọi giá trị khác (số khác 0, chuỗi "0", false, object…) là lỗi.
            if (root.TryGetProperty("error", out var err) && err.ValueKind != JsonValueKind.Null
                && !(err.ValueKind == JsonValueKind.Number && err.TryGetInt64(out var maLoi) && maLoi == 0))
                return "raw-error";
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("item_id", out var rawId) || IdTrongRaw(rawId) is not long idTrongRaw)
                return "raw-no-item";
            // item_id / shop_id phải đúng SP này — client ghép nhầm JSON của SP khác là lưu sai âm thầm. shop_id vắng
            // hoặc null thì bỏ qua (không phải PDP nào cũng có); có mà không đọc được thì coi là lệch.
            if (idTrongRaw != it.ItemId) return "raw-item-mismatch";
            if (item.TryGetProperty("shop_id", out var rawShop) && rawShop.ValueKind != JsonValueKind.Null
                && IdTrongRaw(rawShop) != it.ShopId)
                return "raw-shop-mismatch";
        }
        catch (JsonException) { return "raw-json"; }
        return null;
    }

    /// <summary>Quy mốc cào về đồng hồ Hub: <c>min(raw + skew, now)</c>. KẸP chứ không từ chối mốc tương lai — đồng hồ
    /// máy bị Windows Time kéo lùi giữa lúc cào và lúc gửi sẽ làm mốc nhỉnh hơn giờ Hub một chút, từ chối là mất mục
    /// vô cớ; kẹp về "bây giờ" thì bản đó cũng không chặn được bản cào thật về sau. Mốc cũ (bao lâu cũng được) cứ
    /// nhận — luật "bản cũ không ghi đè bản mới" đã đủ bảo vệ. null = tràn số (vd DateTimeOffset.MaxValue).</summary>
    internal static DateTimeOffset? QuyMoc(DateTimeOffset raw, TimeSpan skew, DateTimeOffset now)
    {
        try
        {
            var at = raw + skew;
            return at > now ? now : at;
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>Id trong raw: số nguyên, hoặc chuỗi CHỈ gồm chữ số (NumberStyles.None — không nhận khoảng trắng, dấu
    /// +/-). Khác đi → null.</summary>
    private static long? IdTrongRaw(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.TryGetInt64(out var n) ? n : null,
        JsonValueKind.String => long.TryParse(e.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null,
        _ => null,
    };

    /// <summary>Lỗi Postgres là lỗi TẤT ĐỊNH CỦA MỤC (gửi lại y nguyên lần nào cũng hỏng → Ok=false cho mục đó):
    /// chỉ lớp 22 (dữ liệu, vd \u0000 jsonb không nhận), 23 (ràng buộc), 54 (vượt giới hạn, vd index row quá lớn).
    /// MỌI lớp khác là chuyện của Hub chứ không phải của mục → ném ra ngoài, CẢ lô không ghi: lớp 53/57/08/40 (hết tài
    /// nguyên, operator, kết nối, deadlock) và lỗi kết nối Npgsql → 503 <c>pg-unavailable</c>
    /// (<c>ProductApiEndpoints.WithPgHarvest</c>); lớp còn lại (25006 read-only, 55P03 khoá, XX000, 42 lệch schema…) → 500.
    /// Client giữ lô, lùi dần; 500 lặp ở riêng một SP gửi lẻ (có SP khác vào giữa các lần) mới bị đánh lỗi. Đoán nhầm theo
    /// hướng này chỉ tốn lượt gửi lại; đoán nhầm hướng kia là client xoá mất dữ liệu.</summary>
    internal static bool IsItemFaultPg(string sqlState) =>
        sqlState.Length >= 2 && sqlState[..2] is "22" or "23" or "54";

    // ── Upsert lô SP đã cào ──────────────────────────────────────────────────────
    // ĐỒNG HỒ: mọi harvested_at so sánh + ghi đều là bản ĐÃ QUY về đồng hồ Hub = min(HarvestedAt + (giờ Hub − ClientNow),
    // giờ Hub) (xem QuyMoc). Máy chạy nhanh/chậm vài giờ vẫn xếp đúng trước-sau với máy khác; độ trễ mạng (vài giây)
    // lẫn vào skew, không đáng kể.
    // MỖI SP 1 transaction riêng (SP + dòng nguồn cùng commit): SP hỏng không kéo theo SP khác. Chưa có → insert;
    // có rồi + hash KHÁC + bản gửi KHÔNG cũ hơn (harvested_at >=) → ghi đè mọi cột, harvest_count+1, updated_at=now()
    // (Applied=true). Hash TRÙNG → chỉ đẩy harvested_at lên GREATEST(cũ, mới) — không đụng raw/cột khác/đếm/updated_at,
    // để bản CŨ hơn hash khác tới sau không lật được bản vừa cào lại. Bản CŨ hơn hash khác → không đổi gì. Hai ca này
    // Ok=true (client xoá khỏi hàng đợi) nhưng Applied=false. Dòng nguồn upsert
    // theo (account_id, sheet, item_id) — migration 5 — kể cả khi hash trùng: SP đã thuộc shop đích thì luôn giữ liên kết,
    // SP khác cùng số dòng là liên kết RIÊNG (không đè); row_no / url cập nhật về lần thấy mới nhất, chỉ khi harvested_at gửi
    // lên không cũ hơn bản đang giữ. Kết quả trả THEO CHỈ SỐ MỤC (cùng itemId có thể ở nhiều mục;
    // khi đó harvest_count có thể đếm dư — chỉ là số thống kê, chấp nhận).
    // PostgresException của MỤC (IsItemFaultPg: 22/23/54) → Ok=false cho đúng mục đó, rollback, làm tiếp mục sau —
    // nếu ném 500 thì client gửi lại lần nào cũng chết ở đúng mục đó, hàng đợi tắc vĩnh viễn. Lớp khác và lỗi không
    // phải PostgresException (mất kết nối, timeout) ném ra ngoài → 500 → client gửi lại cả lô (an toàn vì idempotent).
    public async Task<HarvestedProductBatchResponse> UpsertHarvestedAsync(
        IReadOnlyList<HarvestedProductItem?> items, string? machineId, DateTimeOffset clientNow, CancellationToken ct)
    {
        const string sqlProduct = @"
INSERT INTO harvested_products (item_id, shop_id, url, name, cat_id, category_ids, brand_id, price_min, price_max,
  price_before_discount, tier_count, model_count, image_urls, video_urls, size_chart_url, raw, raw_hash, machine_id,
  harvested_at)
VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19)
ON CONFLICT (item_id) DO UPDATE SET
  shop_id = EXCLUDED.shop_id, url = EXCLUDED.url, name = EXCLUDED.name, cat_id = EXCLUDED.cat_id,
  category_ids = EXCLUDED.category_ids, brand_id = EXCLUDED.brand_id, price_min = EXCLUDED.price_min,
  price_max = EXCLUDED.price_max, price_before_discount = EXCLUDED.price_before_discount,
  tier_count = EXCLUDED.tier_count, model_count = EXCLUDED.model_count, image_urls = EXCLUDED.image_urls,
  video_urls = EXCLUDED.video_urls, size_chart_url = EXCLUDED.size_chart_url, raw = EXCLUDED.raw,
  raw_hash = EXCLUDED.raw_hash, machine_id = EXCLUDED.machine_id, harvested_at = EXCLUDED.harvested_at,
  harvest_count = harvested_products.harvest_count + 1, updated_at = now()
WHERE harvested_products.raw_hash IS DISTINCT FROM EXCLUDED.raw_hash
  AND EXCLUDED.harvested_at >= harvested_products.harvested_at;";
        const string sqlSource = @"
INSERT INTO harvested_product_sources (account_id, sheet, row_no, item_id, url, harvested_at)
VALUES ($1, $2, $3, $4, $5, $6)
ON CONFLICT (account_id, sheet, item_id) DO UPDATE SET
  row_no = EXCLUDED.row_no, url = EXCLUDED.url, harvested_at = EXCLUDED.harvested_at
WHERE EXCLUDED.harvested_at >= harvested_product_sources.harvested_at;";
        // Upsert bị WHERE chặn mà hash TRÙNG: cùng dữ liệu, chỉ là lần cào mới hơn → nhích mốc lên (GREATEST).
        const string sqlBumpAt = @"
UPDATE harvested_products SET harvested_at = $3
WHERE item_id = $1 AND raw_hash = $2 AND harvested_at < $3;";

        var results = new List<HarvestedProductResult>(items.Count);
        var now = DateTimeOffset.UtcNow;
        var skew = now - clientNow;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var reason = ValidateHarvested(it, now, skew);
            if (reason is not null) { results.Add(new HarvestedProductResult(i, it?.ItemId ?? 0, false, false, reason)); continue; }
            var at = QuyMoc(it!.HarvestedAt!.Value, skew, now)!.Value;   // ValidateHarvested đã loại ca tràn số

            await using var tx = await conn.BeginTransactionAsync(ct);
            var applied = false;
            try
            {
                await using (var cmd = new NpgsqlCommand(sqlProduct, conn, tx))
                {
                    cmd.Parameters.AddWithValue(it.ItemId);
                    cmd.Parameters.AddWithValue(it.ShopId);
                    cmd.Parameters.Add(Param(it.Url));
                    cmd.Parameters.Add(Param(it.Name));
                    cmd.Parameters.Add(Param(it.CatId));
                    cmd.Parameters.Add(JsonbOf(it.CategoryIds));
                    cmd.Parameters.Add(Param(it.BrandId));
                    cmd.Parameters.Add(Param(it.PriceMin));
                    cmd.Parameters.Add(Param(it.PriceMax));
                    cmd.Parameters.Add(Param(it.PriceBeforeDiscount));
                    cmd.Parameters.Add(Param(it.TierCount));
                    cmd.Parameters.Add(Param(it.ModelCount));
                    cmd.Parameters.Add(JsonbOf(it.ImageUrls));
                    cmd.Parameters.Add(JsonbOf(it.VideoUrls));
                    cmd.Parameters.Add(Param(it.SizeChartUrl));
                    cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = it.RawJson! });
                    cmd.Parameters.AddWithValue(it.RawHash!);
                    cmd.Parameters.Add(Param(machineId));
                    cmd.Parameters.Add(ParamTs(at));
                    applied = await cmd.ExecuteNonQueryAsync(ct) > 0;
                }
                if (!applied)
                {
                    await using var cmd = new NpgsqlCommand(sqlBumpAt, conn, tx);
                    cmd.Parameters.AddWithValue(it.ItemId);
                    cmd.Parameters.AddWithValue(it.RawHash!);
                    cmd.Parameters.Add(ParamTs(at));
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                // ValidateHarvested đã chặn bộ ba thiếu 1–2 trường → tới đây là đủ cả ba hoặc không có gì.
                if (it.RowNo is int rowNo)
                {
                    await using var cmd = new NpgsqlCommand(sqlSource, conn, tx);
                    cmd.Parameters.AddWithValue(it.AccountId!);
                    cmd.Parameters.AddWithValue(it.Sheet!);
                    cmd.Parameters.AddWithValue(rowNo);
                    cmd.Parameters.AddWithValue(it.ItemId);
                    // Dòng nguồn giữ link GỐC của dòng (SourceUrl); client cũ chưa gửi thì lấy link đã làm sạch.
                    cmd.Parameters.Add(Param(string.IsNullOrWhiteSpace(it.SourceUrl) ? it.Url : it.SourceUrl));
                    cmd.Parameters.Add(ParamTs(at));
                    await cmd.ExecuteNonQueryAsync(ct);
                }
                await tx.CommitAsync(ct);
                results.Add(new HarvestedProductResult(i, it.ItemId, true, applied, null));
            }
            catch (PostgresException ex) when (IsItemFaultPg(ex.SqlState))
            {
                await tx.RollbackAsync(ct);
                results.Add(new HarvestedProductResult(i, it.ItemId, false, false, $"db {ex.SqlState}: {ex.MessageText}"));
            }
        }
        return new HarvestedProductBatchResponse(results);
    }

    // ── Đếm: số liên kết nguồn khớp lọc + số SP khác nhau ───────────────────────────────
    // account/sheet rỗng = không lọc chiều đó. Từ migration 5 mỗi liên kết là một (account, sheet, SP) — số đầu đếm theo SP
    // của từng shop đích (không còn theo vị trí dòng). Không lọc gì cả → Products đếm THẲNG harvested_products (gồm SP cào
    // không kèm dòng nguồn); có lọc → đếm item_id khác nhau trong các liên kết khớp.
    public async Task<HarvestedProductStats> GetHarvestedStatsAsync(string account, string sheet, CancellationToken ct)
    {
        const string sql = @"
SELECT (SELECT count(*) FROM harvested_product_sources
        WHERE ($1 = '' OR account_id = $1) AND ($2 = '' OR sheet = $2)),
       CASE WHEN $1 = '' AND $2 = '' THEN (SELECT count(*) FROM harvested_products)
            ELSE (SELECT count(DISTINCT item_id) FROM harvested_product_sources
                  WHERE ($1 = '' OR account_id = $1) AND ($2 = '' OR sheet = $2)) END;";
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(account);
        cmd.Parameters.AddWithValue(sheet);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        await rd.ReadAsync(ct);
        return new HarvestedProductStats(rd.GetInt64(0), rd.GetInt64(1));
    }

    // Kiểu khai TƯỜNG MINH: tham số null không mang kiểu thì Postgres phải tự đoán theo ngữ cảnh.
    private static NpgsqlParameter Param(long? v) =>
        new() { NpgsqlDbType = NpgsqlDbType.Bigint, Value = v.HasValue ? v.Value : DBNull.Value };
    private static NpgsqlParameter Param(int? v) =>
        new() { NpgsqlDbType = NpgsqlDbType.Integer, Value = v.HasValue ? v.Value : DBNull.Value };
    private static NpgsqlParameter Param(string? v) =>
        new() { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)v ?? DBNull.Value };
    // Npgsql chỉ nhận DateTimeOffset offset 0 cho timestamptz → quy về UTC trước.
    private static NpgsqlParameter ParamTs(DateTimeOffset v) =>
        new() { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = v.ToUniversalTime() };
    private static NpgsqlParameter JsonbOf<T>(T[]? v) =>
        new() { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = v is null ? DBNull.Value : JsonSerializer.Serialize(v) };
}
