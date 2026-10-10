using Microsoft.Data.Sqlite;
using Shopee.Core.Coordination;

namespace Shopee.Hub;

/// <summary>
/// Phần HubDatabase: <b>HỒ SƠ KM của Shop dùng chung</b> — cấu hình + mốc chạy của lịch "KM của Shop", khoá
/// theo <c>login</c> của tài khoản.
/// <para><b>⚠ KHOÁ LÀ <c>login</c>, KHÔNG PHẢI <c>(machine_id, login)</c> — cố ý KHÁC hợp đồng gương danh bạ
/// (<see cref="HubDatabase.UpsertOrdersAccounts"/>).</b> Gương danh bạ trả lời "MÁY NÀY có tài khoản nào" nên
/// nó thuộc về máy, và hub được phép thay trọn danh bạ của đúng máy đó. Hồ sơ KM trả lời "TÀI KHOẢN NÀY có
/// lịch KM gì" — nó thuộc về tài khoản, mọi máy phải nhìn thấy CÙNG một bản. Ai chép hợp đồng "xoá hết rồi ghi
/// lại theo machine_id" sang đây sẽ xoá sạch hồ sơ của mọi máy khác ngay lượt đẩy đầu tiên, mà triệu chứng chỉ
/// là "KM tự nhiên tắt" chứ không phải một lỗi đỏ.</para>
/// <para><b>Gộp bằng luật DÙNG CHUNG</b> <see cref="KmHoSoGop.Gop"/> (file nguồn LINK từ
/// <c>suite\Shopee.Core\Coordination\KmHoSoGop.cs</c>) — hub và client biên dịch CÙNG một mã nguồn, không có
/// bản luật thứ hai để trôi lệch. Mốc thời gian trong bảng này LUÔN là UTC (xem
/// <see cref="KmMocThoiGian"/>).</para>
/// </summary>
public sealed partial class HubDatabase
{
    private void EnsureKmProfilesSchema()
    {
        ExecRaw(@"
-- login COLLATE NOCASE: cùng một email viết hoa/thường khác nhau giữa hai máy PHẢI về CÙNG một dòng, không thì
-- hồ sơ tách đôi và mỗi máy đọc một bản (đúng cái bug đợt này sinh ra để chống). Khai ở CỘT chứ không ở câu
-- WHERE: có thế ON CONFLICT(login) mới gộp, còn WHERE-NOCASE trên khoá phân biệt hoa/thường vẫn đẻ dòng thứ hai.
CREATE TABLE IF NOT EXISTS km_profiles(
  login TEXT PRIMARY KEY COLLATE NOCASE,
-- so_ngay DEFAULT 3: phải KHỚP `KmConfigRepository.MacDinhSoNgay` và `KmProfileItem.SoNgay` (đổi từ 7 ngày
-- 25/08/2026). Ba nơi cùng một khái niệm mà lệch nhau là chu kỳ nhảy về 7 ngày ở bất kỳ đường nào rơi vào
-- mặc định — câm lặng, không log.
  tu_dong INTEGER DEFAULT 0, so_ngay INTEGER DEFAULT 3, phan_tram INTEGER DEFAULT 5,
  gio_bat_dau TEXT DEFAULT '', gio_ket_thuc TEXT DEFAULT '',
  owner_machine TEXT DEFAULT '', cfg_updated_at TEXT DEFAULT '',
  lan_chay_cuoi TEXT DEFAULT '', lan_thu_tu_dong TEXT DEFAULT '',
  combo_bat INTEGER DEFAULT 0, combo_gio_bat_dau TEXT DEFAULT '11:00', combo_gio_ket_thuc TEXT DEFAULT '12:00',
  combo_loai INTEGER DEFAULT 3, combo_mua_sl INTEGER DEFAULT 2, combo_giam INTEGER DEFAULT 2000,
  combo_gioi_han INTEGER DEFAULT 3,
  combo_lan_chay_cuoi TEXT DEFAULT '', combo_lan_thu_tu_dong TEXT DEFAULT '',
  flash_bat INTEGER DEFAULT 0, flash_lan_chay_cuoi TEXT DEFAULT '', flash_lan_thu_tu_dong TEXT DEFAULT '',
-- flash_phan_tram_giam DEFAULT 5 / flash_so_luong_tham_gia DEFAULT 10: phải KHỚP
-- `KmProfileItem.MacDinhFlash*` và `DEFAULT` của hai cột `km_config` bên client. Một CLIENT CŨ không gửi hai
-- trường này thì hàng nhận đúng hai con số đang chạy thật, KHÔNG phải 0 — 0% giảm là một chương trình flash
-- vô nghĩa mà Shopee vẫn nhận.
  flash_phan_tram_giam INTEGER DEFAULT 5, flash_so_luong_tham_gia INTEGER DEFAULT 10,
  updated_at TEXT);");

        // COMBO KHUYẾN MÃI (25/08/2026) — 9 cột cho HUB CŨ đã có bảng km_profiles. `CREATE TABLE IF NOT EXISTS`
        // ở trên KHÔNG sửa bảng cũ, nên thiếu bước này là câu SELECT ném "no such column" ngay lượt đẩy đầu.
        AddColumnIfMissing("km_profiles", "combo_bat", "INTEGER DEFAULT 0");
        // ⚠ DEFAULT là '11:00'/'12:00', KHÔNG phải '' — dòng CŨ (có trước lượt ALTER này) nhận đúng mặc định
        // của `km_config`, nên client kéo về không bị xoá trắng giờ combo. Xem xmldoc `KmProfileItem.ComboGioBatDau`.
        AddColumnIfMissing("km_profiles", "combo_gio_bat_dau", "TEXT DEFAULT '11:00'");
        AddColumnIfMissing("km_profiles", "combo_gio_ket_thuc", "TEXT DEFAULT '12:00'");
        AddColumnIfMissing("km_profiles", "combo_loai", "INTEGER DEFAULT 3");
        AddColumnIfMissing("km_profiles", "combo_mua_sl", "INTEGER DEFAULT 2");
        AddColumnIfMissing("km_profiles", "combo_giam", "INTEGER DEFAULT 2000");
        AddColumnIfMissing("km_profiles", "combo_gioi_han", "INTEGER DEFAULT 3");
        AddColumnIfMissing("km_profiles", "combo_lan_chay_cuoi", "TEXT DEFAULT ''");
        AddColumnIfMissing("km_profiles", "combo_lan_thu_tu_dong", "TEXT DEFAULT ''");

        // FLASH SALE CỦA SHOP (28/08/2026) — 3 cột cho HUB CŨ. `flash_bat` DEFAULT 0 = TẮT: dòng cũ trên hub
        // KHÔNG được tự dưng bật một vòng người dùng chưa đặt, y luật đã dùng cho `combo_bat`.
        AddColumnIfMissing("km_profiles", "flash_bat", "INTEGER DEFAULT 0");
        AddColumnIfMissing("km_profiles", "flash_lan_chay_cuoi", "TEXT DEFAULT ''");
        AddColumnIfMissing("km_profiles", "flash_lan_thu_tu_dong", "TEXT DEFAULT ''");

        // HAI THÔNG SỐ FLASH NHẬP TỪ FORM (03/09/2026) — 2 cột cho HUB CŨ. DEFAULT 5/10, KHÔNG phải 0: dòng đã
        // có trước lượt ALTER này (và mọi lượt đẩy của một CLIENT CŨ) phải nhận đúng hai con số đang chạy thật,
        // vì client kéo bản đó về và ghi thẳng xuống `km_config`. Xem xmldoc `KmProfileItem.FlashPhanTramGiam`.
        AddColumnIfMissing("km_profiles", "flash_phan_tram_giam", "INTEGER DEFAULT 5");
        AddColumnIfMissing("km_profiles", "flash_so_luong_tham_gia", "INTEGER DEFAULT 10");
    }

    /// <summary>
    /// Nhận một lô hồ sơ KM: mỗi dòng ĐỌC bản đang giữ → <see cref="KmHoSoGop.Gop"/> → ghi lại, tất cả trong
    /// MỘT transaction. Login rỗng bị bỏ (không có khoá). Login KHÔNG có trong lô thì KHÔNG bị đụng tới — đây
    /// là điểm khác cốt lõi so với gương danh bạ, xem chú thích lớp.
    /// <para>Trả số dòng đã ghi. Lượt đẩy mang bản CŨ HƠN (máy vừa bật lên sau mấy hôm tắt) là vô hại: luật gộp
    /// giữ cấu hình mới hơn và giữ mốc chạy lớn hơn, nên hub không bao giờ lùi.</para>
    /// <para><b>⚠ DÒNG CHƯA TỒN TẠI THÌ NHẬN TRỌN BẢN VỪA NHẬN — đừng gộp với hồ sơ mặc định.</b> Bản đầu tiên
    /// dùng <c>new KmProfileItem(login)</c> làm "bản đang giữ" và nó là một hồ sơ MẶC ĐỊNH TỔNG HỢP
    /// (<c>TuDong=false, SoNgay=3, PhanTram=5</c>, giờ rỗng, <c>CfgUpdatedAt=null</c>). Client đẩy lên cũng có
    /// <c>CfgUpdatedAt=null</c> ở dòng <c>km_config</c> chưa từng sửa ⇒ <c>MoiHon(null, null) == false</c> ⇒
    /// bản thắng là MẶC ĐỊNH ⇒ <b>hub ghi mặc định và vứt trọn cấu hình thật</b>, trả HTTP 200, không log. Rồi
    /// máy khác kéo bản rỗng đó về, mở form ra xem, đóng lại — thế là <c>TuDong</c> tắt trên MỌI máy.
    /// Đây là lỗi chặn phát hành của vòng 1, khoá bằng
    /// <c>KmHoSoHubTests.DongCu_ChuaTungSua_DayLenHubLanDau_KhongDuocMatCauHinh</c>.</para>
    /// </summary>
    public int UpsertKmProfiles(KmProfilesPushRequest r) => UpsertKmProfilesTraVe(r).Count;

    /// <summary>
    /// Y hệt <see cref="UpsertKmProfiles"/> nhưng TRẢ VỀ bản ĐÃ GỘP của từng dòng vừa ghi — thứ route
    /// <c>POST /orders/km</c> gửi ngược cho client.
    /// <para><b>Vì sao lượt đẩy phải trả dữ liệu:</b> xem <see cref="KmProfilesPushResponse"/>. Tóm tắt: máy sai
    /// đồng hồ không tự phát hiện được mốc sửa rác của chính nó (với nó, 2028 không phải tương lai), nên nếu hub
    /// chỉ kẹp mốc trong bảng của mình thì máy đó vẫn đẩy lại đúng mốc ấy mỗi lần mở app. Hub là nguồn giờ NGOÀI
    /// máy đó — bản gộp đi ngược về là đường duy nhất khép vòng.</para>
    /// </summary>
    public List<KmProfileItem> UpsertKmProfilesTraVe(KmProfilesPushRequest r)
    {
        var daGop = new List<KmProfileItem>();
        if (r?.Profiles is null) return daGop;
        lock (_gate)
        {
            var bayGio = DateTimeOffset.UtcNow;
            var now = Iso(bayGio);
            using var tx = _conn.BeginTransaction();

            foreach (var p in r.Profiles)
            {
                var login = p?.Login?.Trim() ?? "";
                if (p is null || login.Length == 0) continue;

                var den = p with { Login = login };
                // `?? den` chứ KHÔNG phải `?? new KmProfileItem(login)` — xem chú thích hàm. Gộp một bản với
                // CHÍNH NÓ vẫn đi qua luật gộp (nên mốc chạy rác vẫn bị kẹp trần) mà không trộn thêm giá trị
                // mặc định nào của hub.
                var dangCo = DocHoSoLocked(tx, login) ?? den;
                var gop = KmHoSoGop.Gop(dangCo, den, bayGio);

                using var c = _conn.CreateCommand();
                c.Transaction = tx;
                c.CommandText = @"
INSERT INTO km_profiles(login,tu_dong,so_ngay,phan_tram,gio_bat_dau,gio_ket_thuc,owner_machine,cfg_updated_at,lan_chay_cuoi,lan_thu_tu_dong,
  combo_bat,combo_gio_bat_dau,combo_gio_ket_thuc,combo_loai,combo_mua_sl,combo_giam,combo_gioi_han,combo_lan_chay_cuoi,combo_lan_thu_tu_dong,
  flash_bat,flash_lan_chay_cuoi,flash_lan_thu_tu_dong,flash_phan_tram_giam,flash_so_luong_tham_gia,updated_at)
VALUES($l,$td,$sn,$pt,$gb,$gk,$om,$cu,$lc,$lt,$cbat,$cgb,$cgk,$cloai,$cmua,$cgiam,$cgh,$clc,$clt,
  $fbat,$flc,$flt,COALESCE($fptg,$fptgMd),COALESCE($fsltg,$fsltgMd),$ua)
ON CONFLICT(login) DO UPDATE SET tu_dong=$td, so_ngay=$sn, phan_tram=$pt, gio_bat_dau=$gb, gio_ket_thuc=$gk,
  owner_machine=$om, cfg_updated_at=$cu, lan_chay_cuoi=$lc, lan_thu_tu_dong=$lt,
  combo_bat=$cbat, combo_gio_bat_dau=$cgb, combo_gio_ket_thuc=$cgk, combo_loai=$cloai, combo_mua_sl=$cmua,
  combo_giam=$cgiam, combo_gioi_han=$cgh, combo_lan_chay_cuoi=$clc, combo_lan_thu_tu_dong=$clt,
  flash_bat=$fbat, flash_lan_chay_cuoi=$flc, flash_lan_thu_tu_dong=$flt,
  -- COALESCE, KHÔNG gán thẳng: `$fptg` NULL nghĩa là bên đẩy KHÔNG BIẾT trường này (một client CŨ), và bên
  -- không có ý kiến thì không được xoá con số đang chạy. Cột không đổi ⇒ mọi client mới vẫn đọc về đúng 8%
  -- người dùng vừa gõ. Ở nhánh INSERT (dòng chưa tồn tại) không có giá trị cũ để giữ nên rơi về mặc định 5/10.
  flash_phan_tram_giam=COALESCE($fptg, flash_phan_tram_giam),
  flash_so_luong_tham_gia=COALESCE($fsltg, flash_so_luong_tham_gia), updated_at=$ua;";
                c.Parameters.AddWithValue("$l", login);
                c.Parameters.AddWithValue("$td", gop.TuDong ? 1 : 0);
                c.Parameters.AddWithValue("$sn", gop.SoNgay);
                c.Parameters.AddWithValue("$pt", gop.PhanTram);
                c.Parameters.AddWithValue("$gb", gop.GioBatDau);
                c.Parameters.AddWithValue("$gk", gop.GioKetThuc);
                c.Parameters.AddWithValue("$om", gop.OwnerMachine);
                c.Parameters.AddWithValue("$cu", MocRa(gop.CfgUpdatedAt));
                c.Parameters.AddWithValue("$lc", MocRa(gop.LanChayCuoi));
                c.Parameters.AddWithValue("$lt", MocRa(gop.LanThuTuDong));
                c.Parameters.AddWithValue("$cbat", gop.ComboBat ? 1 : 0);
                c.Parameters.AddWithValue("$cgb", gop.ComboGioBatDau ?? "");
                c.Parameters.AddWithValue("$cgk", gop.ComboGioKetThuc ?? "");
                c.Parameters.AddWithValue("$cloai", gop.ComboLoai);
                c.Parameters.AddWithValue("$cmua", gop.ComboMuaSl);
                c.Parameters.AddWithValue("$cgiam", gop.ComboGiam);
                c.Parameters.AddWithValue("$cgh", gop.ComboGioiHan);
                c.Parameters.AddWithValue("$clc", MocRa(gop.ComboLanChayCuoi));
                c.Parameters.AddWithValue("$clt", MocRa(gop.ComboLanThuTuDong));
                c.Parameters.AddWithValue("$fbat", gop.FlashBat ? 1 : 0);
                c.Parameters.AddWithValue("$flc", MocRa(gop.FlashLanChayCuoi));
                c.Parameters.AddWithValue("$flt", MocRa(gop.FlashLanThuTuDong));
                // ⚠ GỬI XUỐNG NULL ĐÚNG NHƯ NÓ LÀ, và để SQL quyết (xem COALESCE trong câu lệnh trên). Bản đầu
                // 03/09/2026 thay `null` bằng mặc định NGAY TẠI ĐÂY — tức một client CŨ (không gửi trường) ghi
                // đè 5/10 lên đúng con số người dùng vừa gõ trên một máy khác, mất câm lặng và vĩnh viễn (mọi
                // máy kéo về 5% ngay lượt đồng bộ kế). Nghĩa của `null` là "KHÔNG CÓ Ý KIẾN", và nó phải được
                // tôn trọng ở CẢ luật gộp (`KmHoSoGop.Gop`) LẪN chỗ ghi này.
                // Cột vẫn KHÔNG BAO GIỜ giữ NULL: nhánh UPDATE giữ giá trị cũ, nhánh INSERT rơi về mặc định.
                c.Parameters.AddWithValue("$fptg", (object?)gop.FlashPhanTramGiam ?? DBNull.Value);
                c.Parameters.AddWithValue("$fsltg", (object?)gop.FlashSoLuongThamGia ?? DBNull.Value);
                c.Parameters.AddWithValue("$fptgMd", KmProfileItem.MacDinhFlashPhanTramGiam);
                c.Parameters.AddWithValue("$fsltgMd", KmProfileItem.MacDinhFlashSoLuongThamGia);
                c.Parameters.AddWithValue("$ua", now);
                c.ExecuteNonQuery();
                daGop.Add(gop);
            }

            tx.Commit();
            return daGop;
        }
    }

    /// <summary>Toàn bộ hồ sơ KM trên hub (client kéo về rồi tự gộp vào <c>km_config</c> của mình). Bảng rỗng →
    /// list rỗng.</summary>
    public List<KmProfileItem> AllKmProfiles()
    {
        lock (_gate)
        {
            var list = new List<KmProfileItem>();
            using var c = _conn.CreateCommand();
            c.CommandText = SelectHoSo + " ORDER BY login COLLATE NOCASE";
            using var rd = c.ExecuteReader();
            while (rd.Read()) list.Add(DocDong(rd));
            return list;
        }
    }

    /// <summary>Hồ sơ của MỘT login (null = chưa có dòng). Gọi TRONG lock + TRONG transaction của
    /// <see cref="UpsertKmProfiles"/>.</summary>
    private KmProfileItem? DocHoSoLocked(SqliteTransaction tx, string login)
    {
        using var c = _conn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = SelectHoSo + " WHERE login=$l LIMIT 1";   // cột đã COLLATE NOCASE (xem schema)
        c.Parameters.AddWithValue("$l", login);
        using var rd = c.ExecuteReader();
        return rd.Read() ? DocDong(rd) : null;
    }

    private const string SelectHoSo =
        "SELECT login,tu_dong,so_ngay,phan_tram,gio_bat_dau,gio_ket_thuc,owner_machine,cfg_updated_at," +
        "lan_chay_cuoi,lan_thu_tu_dong,combo_bat,combo_gio_bat_dau,combo_gio_ket_thuc,combo_loai," +
        "combo_mua_sl,combo_giam,combo_gioi_han,combo_lan_chay_cuoi,combo_lan_thu_tu_dong," +
        "flash_bat,flash_lan_chay_cuoi,flash_lan_thu_tu_dong," +
        "flash_phan_tram_giam,flash_so_luong_tham_gia FROM km_profiles";

    private static KmProfileItem DocDong(SqliteDataReader rd) => new(S(rd, 0))
    {
        TuDong = !rd.IsDBNull(1) && rd.GetInt32(1) != 0,
        SoNgay = rd.IsDBNull(2) ? 7 : rd.GetInt32(2),
        PhanTram = rd.IsDBNull(3) ? 5 : rd.GetInt32(3),
        GioBatDau = S(rd, 4),
        GioKetThuc = S(rd, 5),
        OwnerMachine = S(rd, 6),
        CfgUpdatedAt = MocVao(S(rd, 7)),
        LanChayCuoi = MocVao(S(rd, 8)),
        LanThuTuDong = MocVao(S(rd, 9)),
        ComboBat = !rd.IsDBNull(10) && rd.GetInt32(10) != 0,
        ComboGioBatDau = S(rd, 11),
        ComboGioKetThuc = S(rd, 12),
        ComboLoai = rd.IsDBNull(13) ? 3 : rd.GetInt32(13),
        ComboMuaSl = rd.IsDBNull(14) ? 2 : rd.GetInt32(14),
        ComboGiam = rd.IsDBNull(15) ? 2000 : rd.GetInt32(15),
        ComboGioiHan = rd.IsDBNull(16) ? 3 : rd.GetInt32(16),
        ComboLanChayCuoi = MocVao(S(rd, 17)),
        ComboLanThuTuDong = MocVao(S(rd, 18)),
        FlashBat = !rd.IsDBNull(19) && rd.GetInt32(19) != 0,
        FlashLanChayCuoi = MocVao(S(rd, 20)),
        FlashLanThuTuDong = MocVao(S(rd, 21)),
        // Ô NULL (dòng bị sửa tay) ⇒ giữ `null` trên DTO, KHÔNG rơi về 5/10: `null` là hợp đồng "hub không có ý
        // kiến" và client sẽ giữ giá trị cục bộ — đúng thứ cần, và an toàn hơn hẳn việc hub bịa ra một con số.
        FlashPhanTramGiam = rd.IsDBNull(22) ? null : rd.GetInt32(22),
        FlashSoLuongThamGia = rd.IsDBNull(23) ? null : rd.GetInt32(23),
    };

    /// <summary>Mốc → chuỗi lưu DB: <c>null</c> thành CHUỖI RỖNG (không phải NULL) để khớp <c>DEFAULT ''</c> của
    /// bảng — cột chỉ có đúng một cách biểu diễn "chưa có mốc", đọc bằng mắt cũng thấy ngay.</summary>
    private static string MocRa(DateTimeOffset? d) => d is { } x ? Iso(x.ToUniversalTime()) : "";

    /// <summary>Chuỗi trong DB → mốc UTC; rỗng/hỏng ⇒ <c>null</c> = "chưa có mốc". CỐ Ý không dùng
    /// <c>D(rd,i)</c>: hàm đó trả <see cref="DateTimeOffset.MinValue"/> cho ô rỗng, mà ở luật gộp
    /// <c>MinValue</c> là một mốc THẬT sẽ thắng <c>null</c> — đủ để một dòng rỗng khoá cứng cấu hình cũ.</summary>
    private static DateTimeOffset? MocVao(string s)
        => DateTimeOffset.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)
            ? d.ToUniversalTime()
            : null;
}
