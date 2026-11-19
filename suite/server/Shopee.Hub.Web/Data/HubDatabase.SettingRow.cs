namespace Shopee.Hub;

/// <summary>Phần HubDatabase: đọc / chèn 1 dòng <c>settings</c> phân biệt được "CHƯA CÓ DÒNG" với "có dòng mà rỗng / NULL"
/// (<see cref="GetSetting"/> trả null cho cả "không có dòng" lẫn value NULL). Dùng cho giá trị KHÔNG ĐƯỢC tự sinh lại khi
/// đã từng có — khoá ký link ảnh (GĐ9).</summary>
public sealed partial class HubDatabase
{
    /// <summary>(có dòng không, value — null nếu cột NULL).</summary>
    public (bool Exists, string? Value) ReadSettingRow(string key)
    {
        lock (_gate)
        {
            using var c = _conn.CreateCommand();
            c.CommandText = "SELECT value FROM settings WHERE key=$k";
            c.Parameters.AddWithValue("$k", key);
            using var r = c.ExecuteReader();
            if (!r.Read()) return (false, null);
            return (true, r.IsDBNull(0) ? null : r.GetString(0));
        }
    }

    /// <summary>Chèn CHỈ KHI chưa có dòng cho key (không bao giờ ghi đè). true = đã chèn.</summary>
    public bool TryInsertSetting(string key, string value)
    {
        lock (_gate)
        {
            using var c = _conn.CreateCommand();
            c.CommandText = "INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO NOTHING;";
            c.Parameters.AddWithValue("$k", key);
            c.Parameters.AddWithValue("$v", value);
            return c.ExecuteNonQuery() > 0;
        }
    }
}
