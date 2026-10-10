using System;
using Microsoft.Data.Sqlite;
using XuLyDonShopee.Core.Data;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Lưu trữ cấu hình "KM của Shop" (<c>km_config</c>) — trọng tâm là cột <c>lan_thu_tu_dong</c> thêm 19/08/2026.
/// <para><b>Vì sao migration đáng một bộ test riêng:</b> bảng <c>km_config</c> ĐÃ TỒN TẠI trên máy người dùng
/// (bản 19/08 sáng đã tạo nó), mà <c>CREATE TABLE IF NOT EXISTS</c> KHÔNG sửa bảng cũ. Thiếu lượt
/// <c>ALTER TABLE</c> thì <c>KmConfigRepository.Get</c> ném "no such column" NGAY khi mở màn Tài khoản — hỏng
/// cả màn hình, không riêng tính năng KM.</para>
/// </summary>
public class KmCauHinhLuuTruTests
{
    /// <summary>Dựng bảng <c>km_config</c> theo schema CŨ (chưa có <c>lan_thu_tu_dong</c>) + một dòng dữ liệu.</summary>
    private static void DungSchemaCu(string path, long accountId)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
CREATE TABLE km_config (
    account_id    INTEGER PRIMARY KEY,
    tu_dong       INTEGER NOT NULL DEFAULT 0,
    so_ngay       INTEGER NOT NULL DEFAULT 7,
    phan_tram     INTEGER NOT NULL DEFAULT 5,
    gio_bat_dau   TEXT NOT NULL DEFAULT '09:30',
    gio_ket_thuc  TEXT NOT NULL DEFAULT '10:30',
    lan_chay_cuoi TEXT
);";
            cmd.ExecuteNonQuery();
        }

        using (var ins = conn.CreateCommand())
        {
            ins.CommandText = @"INSERT INTO km_config
                                    (account_id, tu_dong, so_ngay, phan_tram, gio_bat_dau, gio_ket_thuc, lan_chay_cuoi)
                                VALUES ($id, 1, 3, 12, '08:00', '09:00', '2026-08-18T02:30:00.0000000');";
            ins.Parameters.AddWithValue("$id", accountId);
            ins.ExecuteNonQuery();
        }
    }

    private static bool CoCot(string path, string bang, string cot)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({bang});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), cot, StringComparison.OrdinalIgnoreCase)) { return true; }
        }
        return false;
    }

    [Fact]
    public void DbCu_ThieuCot_LanThuTuDong_ThiMigrationThemVao_KhongMatDuLieu()
    {
        using var temp = new TempDatabase();
        DungSchemaCu(temp.Path, accountId: 77);
        Assert.False(CoCot(temp.Path, "km_config", "lan_thu_tu_dong"));

        var db = new Database(temp.Path); // Initialize() chạy migration
        Assert.True(CoCot(temp.Path, "km_config", "lan_thu_tu_dong"));

        // Dữ liệu cũ CÒN NGUYÊN, cột mới = null.
        var cf = new KmConfigRepository(db).Get(77);
        Assert.True(cf.TuDong);
        Assert.Equal(3, cf.SoNgay);
        Assert.Equal(12, cf.PhanTram);
        Assert.Equal("08:00", cf.GioBatDau);
        Assert.Equal(new DateTime(2026, 8, 18, 2, 30, 0), cf.LanChayCuoi);
        Assert.Null(cf.LanThuTuDong);
    }

    [Fact]
    public void DbCu_SauMigration_GhiDoc_LanThuTuDong_BinhThuong()
    {
        using var temp = new TempDatabase();
        DungSchemaCu(temp.Path, accountId: 77);
        var repo = new KmConfigRepository(new Database(temp.Path));

        var moc = new DateTime(2026, 8, 19, 2, 5, 30);
        repo.Save(repo.Get(77) with { LanThuTuDong = moc });

        // Mở LẠI database (mô phỏng khởi động lại app): mốc phải bền qua restart, đó là cả điểm của nó —
        // cờ trong bộ nhớ chết theo tiến trình, mốc trong DB thì không.
        var doc = new KmConfigRepository(new Database(temp.Path)).Get(77);
        Assert.Equal(moc, doc.LanThuTuDong);
        Assert.Equal(new DateTime(2026, 8, 18, 2, 30, 0), doc.LanChayCuoi); // không đè mốc kia
    }

    /// <summary>
    /// <b>Dòng cũ <c>so_ngay = 0</c> phải ĐỌC RA 1</b> — các bản trước ghi được số đó vào DB.
    /// <para>Vì sao đáng một test: <c>KmLichTuDong</c> vốn đã <c>Math.Max(1, SoNgay)</c> nên lịch vẫn chạy đúng,
    /// nhưng lượt KIỂM CẤU HÌNH thêm ở đợt 2 (<c>NhipLichKmAsync</c>) lại đòi <c>SoNgay >= 1</c> ⇒ tài khoản đó
    /// mỗi đêm ăn một dòng "CẤU HÌNH KHÔNG DÙNG ĐƯỢC" cho một cấu hình mà phần còn lại của hệ thống coi là hợp
    /// lệ. Chuẩn hoá ngay ở cửa ĐỌC thì mọi tầng thấy cùng một con số.</para>
    /// </summary>
    [Fact]
    public void DbCu_SoNgay0_ThiDocRa1_KhongLamHongLuotKiemCauHinh()
    {
        using var temp = new TempDatabase();
        DungSchemaCu(temp.Path, accountId: 77);
        DatSoNgayThoTrongDb(temp.Path, accountId: 77, soNgay: 0);

        Assert.Equal(1, new KmConfigRepository(new Database(temp.Path)).Get(77).SoNgay);

        // Vế đối chứng: số hợp lệ KHÔNG bị đụng vào — thiếu vế này thì một bản "luôn trả 1" cũng xanh, mà bản
        // đó biến mọi chu kỳ 7 ngày thành chạy mỗi đêm.
        DatSoNgayThoTrongDb(temp.Path, accountId: 77, soNgay: 5);
        Assert.Equal(5, new KmConfigRepository(new Database(temp.Path)).Get(77).SoNgay);
    }

    /// <summary>Ghi thẳng <c>so_ngay</c> bằng SQL — <c>KmConfigRepository.Save</c> đi qua record đã chuẩn hoá nên
    /// không dựng lại được dòng dữ liệu cũ mà test này cần.</summary>
    private static void DatSoNgayThoTrongDb(string path, long accountId, int soNgay)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE km_config SET so_ngay = $sn WHERE account_id = $id;";
        cmd.Parameters.AddWithValue("$sn", soNgay);
        cmd.Parameters.AddWithValue("$id", accountId);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void DbMoi_MacDinh_KhongCoBanGhi_ThiTraMacDinh_KhongNem()
    {
        using var temp = new TempDatabase();
        var cf = new KmConfigRepository(new Database(temp.Path)).Get(12345);

        Assert.False(cf.TuDong);
        Assert.Equal(3, cf.SoNgay);   // mặc định đổi 7 → 3 ngày (25/08/2026)
        Assert.Equal(5, cf.PhanTram);
        Assert.Null(cf.LanChayCuoi);
        Assert.Null(cf.LanThuTuDong);
        // Hai thông số FLASH (03/09/2026) — mặc định 5% / SL 10, khớp hai hằng `KmConfig.MacDinhFlash*` và
        // `DEFAULT` của hai cột.
        Assert.Equal(5, cf.FlashPhanTramGiam);
        Assert.Equal(10, cf.FlashSoLuongThamGia);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // HAI THÔNG SỐ FLASH NHẬP TỪ FORM (03/09/2026) — người dùng chốt "không fix trong code"
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Vòng tròn ghi/đọc hai thông số flash.</b> Trước 03/09/2026 chúng là hai hằng trong
    /// <c>flow-flashsale.js</c>; nay là hai cột, và đây là bài chứng minh con số người dùng gõ SỐNG QUA một
    /// lượt ghi rồi đọc lại từ đĩa.
    /// <para>Số dùng CỐ Ý khác mặc định (8/25, không phải 5/10): một bản vá quên hẳn hai tham số SQL vẫn đọc ra
    /// đúng mặc định và sẽ xanh nếu bài test dùng 5/10 — đúng hình dạng lỗ phủ.</para>
    /// </summary>
    [Fact]
    public void ThamSoFlash_GhiRoiDocLai_GiuNguyenSoNguoiDungGo()
    {
        using var temp = new TempDatabase();
        var repo = new KmConfigRepository(new Database(temp.Path));
        repo.Save(repo.Get(77) with { FlashPhanTramGiam = 8, FlashSoLuongThamGia = 25 });

        // ĐỌC LẠI qua một Database MỚI ⇒ thật sự xuống đĩa, không phải cache trong bộ nhớ.
        var doc = new KmConfigRepository(new Database(temp.Path)).Get(77);
        Assert.Equal(8, doc.FlashPhanTramGiam);
        Assert.Equal(25, doc.FlashSoLuongThamGia);

        // …và cả qua `GetAll` (đường của worker đồng bộ hub — nó dùng `cot0: 1`, một chỉ số KHÁC).
        Assert.Equal(8, new KmConfigRepository(new Database(temp.Path)).GetAll()[77].FlashPhanTramGiam);
        Assert.Equal(25, new KmConfigRepository(new Database(temp.Path)).GetAll()[77].FlashSoLuongThamGia);
    }

    /// <summary>
    /// <b>DB CŨ (thiếu hai cột) ⇒ migration thêm vào, dòng cũ nhận 5/10, dữ liệu cũ còn nguyên.</b>
    /// <para>Thiếu lượt <c>ALTER TABLE</c> thì <c>KmConfigRepository.Get</c> ném "no such column" NGAY khi mở
    /// màn Tài khoản — hỏng cả màn hình, y ba ca migration <c>km_config</c> trước đó. Và <c>DEFAULT</c> phải là
    /// 5/10 chứ không phải 0: 0% giảm là một chương trình flash vô nghĩa mà Shopee vẫn nhận.</para>
    /// </summary>
    [Fact]
    public void DbCu_ThieuCotThamSoFlash_ThiMigrationThemVao_DongCuNhanMacDinh()
    {
        using var temp = new TempDatabase();
        DungSchemaCu(temp.Path, accountId: 77);
        Assert.False(CoCot(temp.Path, "km_config", "flash_phan_tram_giam"));
        Assert.False(CoCot(temp.Path, "km_config", "flash_so_luong_tham_gia"));

        var db = new Database(temp.Path);   // Initialize() chạy migration
        Assert.True(CoCot(temp.Path, "km_config", "flash_phan_tram_giam"));
        Assert.True(CoCot(temp.Path, "km_config", "flash_so_luong_tham_gia"));

        var cf = new KmConfigRepository(db).Get(77);
        Assert.Equal(5, cf.FlashPhanTramGiam);
        Assert.Equal(10, cf.FlashSoLuongThamGia);
        // Dữ liệu cũ của dòng KHÔNG bị lượt migration đụng tới.
        Assert.True(cf.TuDong);
        Assert.Equal(12, cf.PhanTram);
        Assert.Equal("08:00", cf.GioBatDau);
    }

    /// <summary>
    /// <b><c>CungCauHinh</c> phải THẤY hai thông số flash đổi.</b>
    /// <para>Thiếu chúng ở đó thì người dùng gõ 5% → 8% rồi bấm Đóng là <c>BanLuuSauForm</c> kết luận "không sửa
    /// gì" ⇒ KHÔNG lưu ⇒ cú gõ biến mất không một dấu vết. Cùng ca hỏng mà công tắc <c>FlashBat</c> đã phải vá.</para>
    /// </summary>
    [Fact]
    public void CungCauHinh_ThayHaiThamSoFlashDoi()
    {
        var a = KmConfigRepository.MacDinh(1);
        Assert.True(KmConfig.CungCauHinh(a, a));
        Assert.False(KmConfig.CungCauHinh(a, a with { FlashPhanTramGiam = 8 }));
        Assert.False(KmConfig.CungCauHinh(a, a with { FlashSoLuongThamGia = 25 }));
    }
}
