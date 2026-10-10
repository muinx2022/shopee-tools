using System;
using Microsoft.Data.Sqlite;
using XuLyDonShopee.Core.Data;

namespace XuLyDonShopee.Tests;

/// <summary>
/// Hai cột thêm 22/08/2026 cho <c>km_config</c> phục vụ đồng bộ hồ sơ KM xuyên máy: <c>cfg_updated_at</c> (mốc
/// sửa cấu hình — trọng tài của luật "máy nào sửa sau thắng") và <c>owner_machine</c> (máy chủ trì).
/// <para><b>Vì sao lại một bộ test migration nữa:</b> đúng cái bẫy của đợt <c>lan_thu_tu_dong</c> (19/08) —
/// bảng <c>km_config</c> ĐÃ nằm trên máy người dùng, <c>CREATE TABLE IF NOT EXISTS</c> không sửa bảng cũ, và
/// thiếu <c>ALTER TABLE</c> thì <c>KmConfigRepository.Get</c> ném "no such column" ngay khi mở màn Tài khoản —
/// hỏng CẢ MÀN HÌNH chứ không riêng tính năng KM.</para>
/// </summary>
public class KmCotDongBoTests
{
    /// <summary>Dựng bảng <c>km_config</c> theo schema của bản 19/08 (đã có <c>lan_thu_tu_dong</c>, CHƯA có hai
    /// cột đồng bộ) + một dòng dữ liệu thật.</summary>
    private static void DungSchema1908(string path, long accountId, int soNgay = 3)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var conn = new SqliteConnection(cs);
        conn.Open();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
CREATE TABLE km_config (
    account_id      INTEGER PRIMARY KEY,
    tu_dong         INTEGER NOT NULL DEFAULT 0,
    so_ngay         INTEGER NOT NULL DEFAULT 7,
    phan_tram       INTEGER NOT NULL DEFAULT 5,
    gio_bat_dau     TEXT NOT NULL DEFAULT '09:30',
    gio_ket_thuc    TEXT NOT NULL DEFAULT '10:30',
    lan_chay_cuoi   TEXT,
    lan_thu_tu_dong TEXT
);";
            cmd.ExecuteNonQuery();
        }

        using var ins = conn.CreateCommand();
        ins.CommandText = @"INSERT INTO km_config
                                (account_id, tu_dong, so_ngay, phan_tram, gio_bat_dau, gio_ket_thuc, lan_chay_cuoi)
                            VALUES ($id, 1, $sn, 12, '08:00', '09:00', '2026-08-18T02:30:00.0000000');";
        ins.Parameters.AddWithValue("$id", accountId);
        ins.Parameters.AddWithValue("$sn", soNgay);
        ins.ExecuteNonQuery();
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
    public void DbCu_ThieuHaiCotDongBo_ThiMigrationThemVao_GetKhongNem()
    {
        using var temp = new TempDatabase();
        DungSchema1908(temp.Path, accountId: 77);
        Assert.False(CoCot(temp.Path, "km_config", "cfg_updated_at"));
        Assert.False(CoCot(temp.Path, "km_config", "owner_machine"));

        var db = new Database(temp.Path); // Initialize() chạy migration

        Assert.True(CoCot(temp.Path, "km_config", "cfg_updated_at"));
        Assert.True(CoCot(temp.Path, "km_config", "owner_machine"));

        // Dữ liệu cũ CÒN NGUYÊN và Get không ném. `owner_machine` đọc ra rỗng = "chưa có chủ trì";
        // `cfg_updated_at` KHÔNG còn null mà mang mốc backfill quá khứ (V1 vế 2) — xem
        // `DbCu_SauMigration_DongCuDuocDongDauMocQuaKhu`.
        var cf = new KmConfigRepository(db).Get(77);
        Assert.True(cf.TuDong);
        Assert.Equal(3, cf.SoNgay);
        Assert.Equal(12, cf.PhanTram);
        Assert.Equal(new DateTime(2026, 8, 18, 2, 30, 0), cf.LanChayCuoi);
        Assert.NotNull(cf.CfgUpdatedAt);
        Assert.Equal("", cf.OwnerMachine);
    }

    [Fact]
    public void DbCu_SauMigration_GhiDocHaiCotMoi_BenQuaRestart()
    {
        using var temp = new TempDatabase();
        DungSchema1908(temp.Path, accountId: 77);
        var repo = new KmConfigRepository(new Database(temp.Path));

        var moc = new DateTime(2026, 8, 22, 9, 12, 45);
        repo.Save(repo.Get(77) with { CfgUpdatedAt = moc, OwnerMachine = "may-b:orders" });

        // Mở LẠI database (mô phỏng khởi động lại app).
        var doc = new KmConfigRepository(new Database(temp.Path)).Get(77);
        Assert.Equal(moc, doc.CfgUpdatedAt);
        Assert.Equal("may-b:orders", doc.OwnerMachine);
        Assert.Equal(new DateTime(2026, 8, 18, 2, 30, 0), doc.LanChayCuoi);   // không đè mốc kia
    }

    /// <summary>
    /// <see cref="KmConfigRepository.GetAll"/> chỉ trả tài khoản ĐÃ CÓ DÒNG — worker đồng bộ dựa vào đó để
    /// không đẩy hồ sơ mặc định của mọi tài khoản lên hub. Dùng <see cref="KmConfigRepository.Get"/> cho từng
    /// tài khoản thì không phân biệt được "đã đặt lịch" với "chưa ai đụng tới" (hàm đó trả bản mặc định).
    /// </summary>
    [Fact]
    public void GetAll_ChiTraDongDaCo_VaDungGiaTri()
    {
        using var temp = new TempDatabase();
        var repo = new KmConfigRepository(new Database(temp.Path));

        var moc = new DateTime(2026, 8, 22, 1, 2, 3);
        repo.Save(new KmConfig(7, TuDong: true, SoNgay: 3, PhanTram: 25, GioBatDau: "08:00",
            GioKetThuc: "09:00", LanChayCuoi: null) { CfgUpdatedAt = moc, OwnerMachine = "may-a:orders" });

        var tatCa = repo.GetAll();

        Assert.Single(tatCa);
        Assert.False(tatCa.ContainsKey(99));          // tài khoản chưa đặt lịch KHÔNG có mặt
        var cf = tatCa[7];
        Assert.True(cf.TuDong);
        Assert.Equal(3, cf.SoNgay);
        Assert.Equal(25, cf.PhanTram);
        Assert.Equal("08:00", cf.GioBatDau);
        Assert.Equal(moc, cf.CfgUpdatedAt);
        Assert.Equal("may-a:orders", cf.OwnerMachine);
    }

    /// <summary>Dòng cũ <c>so_ngay = 0</c> phải đọc ra 1 qua CẢ HAI cửa đọc — thêm <c>GetAll</c> mà quên chuẩn
    /// hoá là worker đẩy số 0 lên hub, rồi mọi máy kéo về một cấu hình mà lượt kiểm của
    /// <c>NhipLichKmAsync</c> từ chối.</summary>
    [Fact]
    public void GetAll_SoNgay0_CungChuanHoaVe1_NhuGet()
    {
        using var temp = new TempDatabase();
        DungSchema1908(temp.Path, accountId: 77, soNgay: 0);
        var repo = new KmConfigRepository(new Database(temp.Path));

        Assert.Equal(1, repo.Get(77).SoNgay);
        Assert.Equal(1, repo.GetAll()[77].SoNgay);

        // Vế đối chứng: số hợp lệ không bị đụng vào.
        using var temp2 = new TempDatabase();
        DungSchema1908(temp2.Path, accountId: 77, soNgay: 5);
        Assert.Equal(5, new KmConfigRepository(new Database(temp2.Path)).GetAll()[77].SoNgay);
    }

    // ── BACKFILL cfg_updated_at cho dòng CŨ (V1 vế 2, 22/08/2026) ──────────────

    /// <summary>
    /// <b>Dòng <c>km_config</c> có sẵn phải được đóng dấu một mốc QUÁ KHỨ, không được để NULL.</b>
    /// <para><c>ALTER TABLE ADD COLUMN</c> chỉ thêm cột. Để mốc NULL thì dòng mang cấu hình THẬT ngang hàng
    /// với hồ sơ RỖNG của một máy chưa ai mở form (<c>null</c> thua mọi giá trị, và <c>null</c> so <c>null</c>
    /// thì bên đang giữ thắng) ⇒ máy chưa mở form không bao giờ nhận được cấu hình thật, mà không lỗi, không
    /// log. Mốc phải ở QUÁ KHỨ để mọi lượt người dùng sửa từ nay về sau vẫn thắng nó.</para>
    /// </summary>
    [Fact]
    public void DbCu_SauMigration_DongCuDuocDongDauMocQuaKhu()
    {
        using var temp = new TempDatabase();
        DungSchema1908(temp.Path, accountId: 77);

        var cf = new KmConfigRepository(new Database(temp.Path)).Get(77);

        Assert.NotNull(cf.CfgUpdatedAt);
        Assert.Equal(DateTime.Parse(Database.MocBackfillKmCfg, null,
            System.Globalization.DateTimeStyles.RoundtripKind), cf.CfgUpdatedAt);
        Assert.True(cf.CfgUpdatedAt < DateTime.Now, "mốc backfill phải ở QUÁ KHỨ");
        // Cấu hình thật CÒN NGUYÊN — backfill chỉ đóng dấu, không đụng dữ liệu.
        Assert.True(cf.TuDong);
        Assert.Equal(12, cf.PhanTram);
    }

    /// <summary>Lượt sửa SAU backfill vẫn thắng mốc backfill — nếu không thì đóng dấu quá khứ hoá ra khoá cứng
    /// cấu hình cũ, đổi một lỗi lấy một lỗi khác.</summary>
    [Fact]
    public void MocBackfill_ThuaMoiLuotSuaSauDo()
    {
        using var temp = new TempDatabase();
        DungSchema1908(temp.Path, accountId: 77);
        var repo = new KmConfigRepository(new Database(temp.Path));

        var mocSua = new DateTime(2026, 8, 22, 9, 0, 0);
        repo.Save(repo.Get(77) with { PhanTram = 30, CfgUpdatedAt = mocSua });

        var doc = new KmConfigRepository(new Database(temp.Path)).Get(77);
        Assert.Equal(mocSua, doc.CfgUpdatedAt);
        Assert.True(doc.CfgUpdatedAt > DateTime.Parse(Database.MocBackfillKmCfg, null,
            System.Globalization.DateTimeStyles.RoundtripKind));
        Assert.Equal(30, doc.PhanTram);
    }

    /// <summary>
    /// <b>Backfill chạy ĐÚNG MỘT LẦN</b> — dòng ghi SAU lượt nâng cấp với mốc <c>null</c> (ví dụ
    /// <c>ChotLuotKm</c> trên tài khoản chưa ai mở form) KHÔNG được đóng dấu quá khứ ké ở lần khởi động sau.
    /// Đóng dấu ké là gán cho nó một "lượt sửa" chưa từng xảy ra, và bản rỗng đó sẽ đi tranh với cấu hình thật
    /// của máy khác.
    /// </summary>
    [Fact]
    public void Backfill_ChayDungMotLan_KhongDongDauChoDongGhiSau()
    {
        using var temp = new TempDatabase();
        DungSchema1908(temp.Path, accountId: 77);
        var repo = new KmConfigRepository(new Database(temp.Path));   // lượt migration + backfill đầu tiên

        // Dòng MỚI, mốc sửa null (chưa ai mở form cho tài khoản này).
        repo.Save(new KmConfig(88, TuDong: false, SoNgay: 7, PhanTram: 5, GioBatDau: "09:30",
            GioKetThuc: "10:30", LanChayCuoi: new DateTime(2026, 8, 22, 2, 30, 0)));

        // Mở lại app: migration chạy lại, nhưng backfill đã có cờ "đã chạy".
        var doc = new KmConfigRepository(new Database(temp.Path)).Get(88);

        Assert.Null(doc.CfgUpdatedAt);
    }

    [Fact]
    public void DbMoi_MacDinh_HaiCotMoiLaChuaBiet()
    {
        using var temp = new TempDatabase();
        var cf = new KmConfigRepository(new Database(temp.Path)).Get(12345);

        Assert.Null(cf.CfgUpdatedAt);
        Assert.Equal("", cf.OwnerMachine);
    }

    // ── BACKFILL owner_machine cho dòng CŨ (W3, phản biện vòng 3 — 22/08/2026) ──────────────

    private const string MayNay = "may-nay:orders";

    /// <summary>
    /// <b>Dòng cũ ĐANG BẬT KM phải được đóng dấu MÁY NÀY làm chủ trì; dòng đang TẮT thì để rỗng.</b>
    /// <para>Không có bước này thì cửa chủ trì (chạy lượt KM tự động + nhường khung check đơn) <b>VÔ HIỆU trên
    /// mọi máy đang có dữ liệu</b>: lượt backfill của vòng 2 chỉ đóng dấu <c>cfg_updated_at</c>, nên
    /// <c>owner_machine</c> còn rỗng ⇒ <c>LaMayChuTri("", …)</c> trả <c>true</c> ⇒ MỌI máy đều được chạy, đúng
    /// cuộc đua mà cửa ấy sinh ra để chặn — ngay đêm đầu sau nâng cấp.</para>
    /// <para>Dòng TẮT để rỗng là cố ý: không ai chủ trì một lịch đang tắt, và đóng dấu bừa vào đó là biến một
    /// máy tình cờ nâng cấp trước thành chủ trì của một lịch nó chưa từng đặt.</para>
    /// </summary>
    [Fact]
    public void DbCu_DangBatKm_ThiBackfillDongDauChuTri_DongTatThiVanRong()
    {
        using var temp = new TempDatabase();
        DungSchema1908(temp.Path, accountId: 77);           // dòng này tu_dong = 1
        var repo = new KmConfigRepository(new Database(temp.Path));
        repo.Save(repo.Get(77) with { AccountId = 88, TuDong = false });   // dòng thứ hai, lịch TẮT

        Assert.Equal("", repo.Get(77).OwnerMachine);        // trước backfill: chưa ai chủ trì
        var soDong = repo.BackfillOwnerMachineOnce(MayNay);

        Assert.Equal(1, soDong);
        Assert.Equal(MayNay, repo.Get(77).OwnerMachine);
        Assert.Equal("", repo.Get(88).OwnerMachine);
    }

    /// <summary>Backfill KHÔNG cướp chủ trì của máy khác — dòng đã có chủ trì thì để nguyên.</summary>
    [Fact]
    public void Backfill_KhongDeLenChuTriDaCo()
    {
        using var temp = new TempDatabase();
        DungSchema1908(temp.Path, accountId: 77);
        var repo = new KmConfigRepository(new Database(temp.Path));
        repo.Save(repo.Get(77) with { OwnerMachine = "may-khac:orders" });

        Assert.Equal(0, repo.BackfillOwnerMachineOnce(MayNay));
        Assert.Equal("may-khac:orders", repo.Get(77).OwnerMachine);
    }

    /// <summary>CHẠY ĐÚNG MỘT LẦN — dòng bật KM ghi SAU lượt nâng cấp không bị đóng dấu ké. Chạy mỗi lần khởi
    /// động thì mọi máy trong nhóm lần lượt cướp chủ trì của nhau mỗi lần mở app.</summary>
    [Fact]
    public void BackfillChuTri_ChayDungMotLan_KhongDongDauChoDongGhiSau()
    {
        using var temp = new TempDatabase();
        DungSchema1908(temp.Path, accountId: 77);
        var repo = new KmConfigRepository(new Database(temp.Path));
        repo.BackfillOwnerMachineOnce(MayNay);

        // Dòng MỚI, bật KM, chưa có chủ trì (ví dụ hồ sơ vừa kéo từ hub về cho tài khoản chưa ai mở form).
        repo.Save(new KmConfig(88, TuDong: true, SoNgay: 7, PhanTram: 5, GioBatDau: "09:30",
            GioKetThuc: "10:30", LanChayCuoi: null));

        Assert.Equal(0, new KmConfigRepository(new Database(temp.Path)).BackfillOwnerMachineOnce(MayNay));
        Assert.Equal("", repo.Get(88).OwnerMachine);
    }

    /// <summary>Không có hub (<c>mayNay</c> rỗng) ⇒ KHÔNG làm gì và KHÔNG đánh dấu "đã chạy" — lần sau có hub
    /// thì vẫn còn cơ hội đóng dấu. Đánh dấu ké ở đây là mất vĩnh viễn lượt backfill của máy đó.</summary>
    [Fact]
    public void Backfill_KhongCoHub_ThiKhongLamGi_VaVanChayDuocSau()
    {
        using var temp = new TempDatabase();
        DungSchema1908(temp.Path, accountId: 77);
        var repo = new KmConfigRepository(new Database(temp.Path));

        Assert.Equal(0, repo.BackfillOwnerMachineOnce(""));
        Assert.Equal("", repo.Get(77).OwnerMachine);

        Assert.Equal(1, repo.BackfillOwnerMachineOnce(MayNay));
        Assert.Equal(MayNay, repo.Get(77).OwnerMachine);
    }
}
