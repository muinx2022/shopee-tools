using Npgsql;
using Shopee.Core.Coordination;

namespace Shopee.Hub;

/// <summary>
/// Phần ProductDb: ĐỌC SP đã harvest — theo danh sách itemId (bước đăng SP lên Shopee dùng đường này) và theo dòng nguồn
/// (chỉ tra cứu: row_no là chỉ số dòng NÉN, có thể cũ). Chỉ đọc, không đụng đường ghi của <see cref="UpsertHarvestedAsync"/>.
/// </summary>
public sealed partial class ProductDb
{
    // ── Đọc SP theo danh sách itemId ────────────────────────────────────────────────
    // Bước đăng SP tự đọc nguồn hiện tại (workbook / product_rows) rồi tách item_id, nên không phụ thuộc row_no trên Hub.
    // Trả theo thứ tự id gửi lên; id không có dữ liệu thì vắng mặt. Caller đã khử trùng + kẹp số lượng.
    public async Task<List<HarvestedItemData>> GetHarvestedByItemsAsync(IReadOnlyList<long> itemIds, CancellationToken ct)
    {
        if (itemIds.Count == 0) return [];
        const string sql = @"
SELECT item_id, shop_id, harvested_at, raw::text FROM harvested_products WHERE item_id = ANY($1);";
        var found = new Dictionary<long, HarvestedItemData>(itemIds.Count);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(itemIds.ToArray());
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            using var doc = JsonDocument.Parse(rd.GetString(3));
            var id = rd.GetInt64(0);
            found[id] = new HarvestedItemData(id, rd.GetInt64(1), Utc(rd.GetDateTime(2)), doc.RootElement.Clone());
        }
        return itemIds.Where(found.ContainsKey).Select(id => found[id]).ToList();
    }

    private static DateTimeOffset Utc(DateTime v) => new(DateTime.SpecifyKind(v, DateTimeKind.Utc));

    // ── Đọc 1 trang dòng nguồn → SP (raw) ───────────────────────────────────────────
    // row_no / from / to là CHỈ SỐ DÒNG NÉN theo Harvest lúc thấy SP, CÓ THỂ CŨ sau chèn/xoá dòng (Harvest không gửi lại
    // SP đã xong) — route này chỉ để tra cứu, KHÔNG dùng để đăng SP.
    // account/sheet so khớp CHÍNH XÁC (phân biệt hoa thường) — đúng như batch upsert lưu (ghi nguyên chuỗi client gửi,
    // không chuẩn hoá) và như GetHarvestedStatsAsync lọc. JOIN trong: liên kết mà SP chưa có trong harvested_products thì
    // không trả. Con trỏ (row_no, item_id) chứ không chỉ row_no: từ migration 5 một row_no có thể có nhiều SP. Đọc dư 1
    // dòng để biết còn trang sau không — trang vừa đủ limit mà hết dữ liệu thì Next = null, client khỏi gọi thêm.
    public async Task<HarvestedRowsPage> GetHarvestedRowsAsync(string account, string sheet, int fromRow, int toRow,
        int afterRow, long afterItemId, int limit, CancellationToken ct)
    {
        const string sql = @"
SELECT s.row_no, s.url, p.item_id, p.shop_id, p.harvested_at, s.harvested_at AS linked_at, p.raw::text
FROM harvested_product_sources s
JOIN harvested_products p ON p.item_id = s.item_id
WHERE s.account_id = $1 AND s.sheet = $2 AND s.row_no BETWEEN $3 AND $4
  AND (s.row_no, s.item_id) > ($5, $6)
ORDER BY s.row_no, s.item_id
LIMIT $7;";
        var rows = new List<HarvestedSourceRow>(limit + 1);
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(account);
        cmd.Parameters.AddWithValue(sheet);
        cmd.Parameters.AddWithValue(fromRow);
        cmd.Parameters.AddWithValue(toRow);
        cmd.Parameters.AddWithValue(afterRow);
        cmd.Parameters.AddWithValue(afterItemId);
        cmd.Parameters.AddWithValue(limit + 1);
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            // raw là jsonb hợp lệ (ValidateHarvested đã kiểm lúc ghi) → Parse không ném; Clone để sống qua Dispose.
            using var doc = JsonDocument.Parse(rd.GetString(6));
            rows.Add(new HarvestedSourceRow(
                rd.GetInt32(0), rd.IsDBNull(1) ? null : rd.GetString(1), rd.GetInt64(2), rd.GetInt64(3),
                Utc(rd.GetDateTime(4)), Utc(rd.GetDateTime(5)), doc.RootElement.Clone()));
        }
        HarvestedRowsCursor? next = null;
        if (rows.Count > limit)
        {
            rows.RemoveAt(rows.Count - 1);
            var last = rows[^1];
            next = new HarvestedRowsCursor(last.RowNo, last.ItemId);
        }
        return new HarvestedRowsPage(rows, next);
    }
}
