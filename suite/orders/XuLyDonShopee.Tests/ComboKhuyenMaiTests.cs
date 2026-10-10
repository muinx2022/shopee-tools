using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.App.ViewModels;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.Tests;

/// <summary>
/// <b>COMBO KHUYẾN MÃI — chương trình thứ HAI của "KM của Shop"</b> (chặng A, plan
/// <c>plans/2026-08-25-combo-khuyen-mai.md</c>). Khung 06–08h, mốc chạy TÁCH RIÊNG khỏi vòng KM.
/// <para><b>Ca hỏng đắt nhất mà file này sinh ra để khoá</b> (plan §A2): nếu hai vòng dùng CHUNG
/// <c>LanChayCuoi</c> thì lượt KM chạy 02:05 ngày D ghi mốc <c>D 02:xx</c>, và tới 06:05 vòng combo hỏi
/// <c>DenGioChay</c> → <c>bayGio &gt;= cuoi.Date + SoNgay + 6h</c> = <c>D+3 06:00</c> → <b>false</b> ⇒
/// <b>combo KHÔNG BAO GIỜ CHẠY</b>, im lặng và vĩnh viễn. Không test nào cũ bắt được: cả hai vòng vẫn "xanh"
/// theo nghĩa không ném, không log lỗi.</para>
/// </summary>
// Xem chú thích ở `KmKhungXuyenMayTests`: lớp này cũng rút hai biến static hạn/nhịp chờ khung xuống, nên phải
// chạy nối đuôi với lớp đó.
[Collection(TenCollection.KhungXuyenMay)]
public class ComboKhuyenMaiTests
{
    private const long AccId = 1;

    private static KmConfig Cf(
        bool comboBat = true, int soNgay = 3,
        DateTime? kmChayCuoi = null, DateTime? kmThu = null,
        DateTime? comboChayCuoi = null, DateTime? comboThu = null,
        bool kmTuDong = true)
        => new(AccId, kmTuDong, soNgay, 5, "09:30", "10:30", kmChayCuoi, kmThu)
        {
            ComboBat = comboBat,
            ComboLanChayCuoi = comboChayCuoi,
            ComboLanThuTuDong = comboThu,
        };

    // ══════════════════════ §A2 — HAI VÒNG, HAI CẶP MỐC ══════════════════════

    /// <summary>
    /// <b>Ca hỏng §A2, khoá bằng con số:</b> vòng KM chạy 02:05 ngày D và ghi mốc xong — <b>vòng combo lúc
    /// 06:05 CÙNG NGÀY vẫn phải thấy mình CHƯA chạy lần nào</b>.
    /// <para>Gộp hai mốc lại làm một (dù <c>MocChayCuoiCua</c> hay <c>MocThuCua</c>) là test này ĐỎ ngay.</para>
    /// <para><b>⚠ Vì sao đo <c>MocChayKe</c>/<c>DaThuTrongKhungNay</c> chứ không đo thẳng <c>DenGioChay</c></b>
    /// (đổi 25/08/2026, cùng lượt thêm <see cref="KmLichTuDong.ComboSanSang"/>): công tắc an toàn ấy đang chặn
    /// <c>VongBat(Combo)</c>, nên <c>DenGioChay</c> hiện trả <c>false</c> BẤT KỂ hai mốc có bị gộp hay không —
    /// đo nó lúc này là một phép đo RỖNG. Hai hàm dưới đây nằm TRƯỚC công tắc nên vẫn khoá được đúng ca hỏng;
    /// vế <c>DenGioChay</c> vẫn giữ, nhưng buộc vào chính hằng đó để nó tự sống lại khi chặng B lật cờ.</para>
    /// </summary>
    [Fact]
    public void KmVuaChayLuc0130_ThiComboLuc0205_CungNgay_VAN_ToiHan()
    {
        var d = new DateTime(2026, 8, 25);
        var luc0205 = d.AddHours(2).AddMinutes(5);
        var sauLuotKm = Cf(
            kmChayCuoi: d.AddHours(1).AddMinutes(30),    // lượt KM vừa TẠO ĐƯỢC
            kmThu: d.AddHours(1).AddMinutes(5));        // và đã đốt lượt thử của khung 01–04h

        // Mốc THỬ riêng: khung Combo chưa có lượt thử nào, dù vòng KM đã dùng hết lượt thử của nó.
        Assert.False(KmLichTuDong.DaThuTrongKhungNay(sauLuotKm, luc0205, LoaiChuongTrinh.Combo));
        Assert.True(KmLichTuDong.DaThuTrongKhungNay(
            sauLuotKm, d.AddHours(1).AddMinutes(30), LoaiChuongTrinh.ChuongTrinhShop));

        // Mốc CHẠY riêng: vòng combo chưa chạy lần nào ⇒ mốc kế là NGAY BÂY GIỜ (đang trong khung).
        Assert.Equal(luc0205, KmLichTuDong.MocChayKe(sauLuotKm, luc0205, LoaiChuongTrinh.Combo));

        // Cửa cuối cùng đi theo ĐÚNG công tắc sẵn-sàng — lật hằng đó là vế này tự khẳng định "tới hạn".
        Assert.Equal(
            KmLichTuDong.ComboSanSang,
            KmLichTuDong.DenGioChay(sauLuotKm, luc0205, LoaiChuongTrinh.Combo));

        // Đối chứng, chặn TEST RỖNG: chính vòng KM thì đã dùng hết lượt của ngày hôm đó.
        Assert.False(KmLichTuDong.DenGioChay(sauLuotKm, d.AddHours(1).AddMinutes(30), LoaiChuongTrinh.ChuongTrinhShop));
    }

    /// <summary>Chiều ngược lại: vòng combo vừa chạy KHÔNG được khoá vòng KM của đêm sau.</summary>
    [Fact]
    public void ComboVuaChay_ThiKhongKhoaVongKm()
    {
        var d = new DateTime(2026, 8, 25);
        var sauLuotCombo = Cf(comboChayCuoi: d.AddHours(2).AddMinutes(30), comboThu: d.AddHours(1).AddMinutes(5));

        // Vòng KM chưa chạy lần nào ⇒ khung 01–04h ngày hôm sau vẫn tới hạn.
        Assert.True(KmLichTuDong.DenGioChay(sauLuotCombo, d.AddDays(1).AddHours(1), LoaiChuongTrinh.ChuongTrinhShop));
        // Còn chính vòng combo thì phải đợi đủ SoNgay: mốc kế là D+3 01:00, không phải D+1.
        Assert.Equal(
            d.AddDays(3).AddHours(1),
            KmLichTuDong.MocChayKe(sauLuotCombo, d.AddDays(1).AddHours(1), LoaiChuongTrinh.Combo));
        Assert.False(KmLichTuDong.DenGioChay(sauLuotCombo, d.AddDays(1).AddHours(1), LoaiChuongTrinh.Combo));
    }

    // ══════════════════════ NẶNG-1 — CÔNG TẮC `ComboSanSang` ══════════════════════

    /// <summary>
    /// <b>Vế ĐỐI CHỨNG khoá chính cái công tắc:</b> <c>VongBat(cf có ComboBat = true, Combo)</c> phải ĐÚNG BẰNG
    /// <see cref="KmLichTuDong.ComboSanSang"/> — không hơn, không kém.
    /// <para>Test này ĐỎ ngay khi ai đó lật hằng mà quên phần còn lại, và cũng đỏ nếu ai đó gỡ vế
    /// <c>&amp;&amp; ComboSanSang</c> ra khỏi <see cref="KmLichTuDong.VongBat"/> — tức nó khoá đúng CHOKE POINT
    /// chứ không khoá một hằng nằm rời.</para>
    /// </summary>
    [Fact]
    public void VongBatCombo_BangDUNG_HangComboSanSang()
    {
        Assert.Equal(KmLichTuDong.ComboSanSang, KmLichTuDong.VongBat(Cf(comboBat: true), LoaiChuongTrinh.Combo));

        // Cờ trong DB TẮT thì tắt hẳn, bất kể hằng — hai vế AND, không phải OR.
        Assert.False(KmLichTuDong.VongBat(Cf(comboBat: false), LoaiChuongTrinh.Combo));

        // Và công tắc này KHÔNG được đụng tới vòng KM: nó có cờ riêng.
        Assert.True(KmLichTuDong.VongBat(Cf(comboBat: false, kmTuDong: true), LoaiChuongTrinh.ChuongTrinhShop));
    }

    /// <summary>
    /// <b>Vòng combo KHÔNG được tick thì im TUYỆT ĐỐI</b>, dù đồng hồ điểm 06:05, máy này là máy chủ trì và cấu
    /// hình sạch: không dòng nhật ký nào, <b>không xin khung, không xin khoá tài khoản, không phóng trình
    /// duyệt</b>, và <b>KHÔNG ghi mốc thử</b>.
    /// <para><b>Vì sao "không ghi mốc thử" là một mệnh đề riêng:</b> ghi mốc ở đây trông vô hại nhưng nó ĐỐT
    /// lượt thử của khung — tới ngày người dùng bật ô tick, đúng khung đó vòng combo lại đứng ngoài một lần nữa
    /// mà không ai hiểu vì sao.</para>
    /// <para><b>Trước 26/08/2026 test này đo cửa <see cref="KmLichTuDong.ComboSanSang"/></b> (hằng lúc đó
    /// <c>false</c>, luồng extension chưa tồn tại). Chặng B xong, hằng lật <c>true</c> ⇒ đo cửa ấy ở đây thành
    /// phép đo RỖNG, nên đổi sang đo cửa CÒN SỐNG là cờ <c>ComboBat</c> trong DB. Cửa hằng vẫn được khoá ở
    /// <see cref="VongBatCombo_BangDUNG_HangComboSanSang"/>, và cái giá của một đêm bật nhầm vẫn nguyên đó:
    /// hub DỪNG check đơn ở MỌI máy khác (deadline 90 phút) → Brave + SSO → tới 08:00 hết khung.</para>
    /// </summary>
    [StaFact]
    public async Task NhipCombo_ChuaTickCombo_ThiKhongChayGi_VaKhongDotLuotThu()
    {
        var (temp, services, vm, id) = DungVm("combo-chua-tick@mail.com");
        using var db = temp;
        var daXinKhoa = 0;
        var daXinKhung = 0;
        services.AcquireAccountLease = (l, ct) => { daXinKhoa++; return Task.FromResult(new OrdersLeaseResult(true, null)); };
        services.ReleaseAccountLease = l => Task.CompletedTask;
        services.XinKhungKm = (l, ct) => { daXinKhung++; return Task.FromResult(new KmKhungResult(true, null)); };
        services.TraKhungKm = l => Task.CompletedTask;
        services.KmConfig.Save(CauHinhCombo(id) with { ComboBat = false });   // cấu hình SẠCH, chỉ thiếu ô tick

        await vm.NhipLichComboAsync(new DateTime(2026, 8, 25, 6, 5, 0));

        Assert.Empty(services.Log.Snapshot("combo-chua-tick@mail.com"));
        Assert.Equal(0, daXinKhung);
        Assert.Equal(0, daXinKhoa);
        Assert.Null(services.KmConfig.Get(id).ComboLanThuTuDong);
        Assert.Null(services.KmConfig.Get(id).ComboLanChayCuoi);
    }

    /// <summary>
    /// <b>Vòng combo TẮT thì KHÔNG được cắt check đơn.</b> Vòng check đơn định đặt mốc kế lúc 05:30 — tức rơi
    /// vào biên an <c>[5, 8)</c> của khung combo — vẫn phải GIỮ NGUYÊN mốc đó khi combo không bật.
    /// <para>Đây là nửa thứ hai của ca hỏng NẶNG-1: chặn khung cho một vòng chẳng bao giờ chạy là tự cắt 2–3
    /// tiếng check đơn mỗi chu kỳ để đổi lấy KHÔNG GÌ CẢ. <c>VongBat</c> là chỗ duy nhất chặn được cả hai cửa,
    /// và test này là vế chứng minh cửa thứ hai cũng đóng.</para>
    /// <para><b>Đổi 26/08/2026 (lật <see cref="KmLichTuDong.ComboSanSang"/>):</b> trước đây vế "không nhường"
    /// đo bằng chính hằng đó với <c>ComboBat = true</c>. Hằng nay <c>true</c> nên vế ấy đổi nguồn sang cờ DB
    /// (<c>ComboBat = false</c>), và thêm vế NGƯỢC LẠI — combo BẬT thì 05:30 PHẢI nhường tới 08:00, thứ trước
    /// chặng B không đo được.</para>
    /// </summary>
    [Fact]
    public void NenNhuongKhung_TheoDungVongDangBat()
    {
        var d = new DateTime(2026, 8, 25);
        var mocKmCombo = d.AddHours(1).AddMinutes(30);   // biên an [0,4) của khung 01–04h

        // Cả hai TẮT ⇒ KHÔNG nhường, mốc check đơn giữ nguyên.
        var cfTat = Cf(comboBat: false, kmTuDong: false) with { OwnerMachine = "may-A:orders" };
        Assert.False(KmLichTuDong.NenNhuongKhung(cfTat, "may-A:orders", mocKmCombo, d.AddHours(0).AddMinutes(30)));
        Assert.Equal(mocKmCombo, KmLichTuDong.HoanVongCheckDon(
            mocKmCombo, KmLichTuDong.NenNhuongKhung(cfTat, "may-A:orders", mocKmCombo, d.AddHours(0).AddMinutes(30))));

        // Chỉ Combo BẬT ⇒ nhường, và dời tới giờ ĐÓNG của khung (04:00).
        var cfComboBat = Cf(comboBat: true, kmTuDong: false) with { OwnerMachine = "may-A:orders" };
        Assert.True(KmLichTuDong.NenNhuongKhung(cfComboBat, "may-A:orders", mocKmCombo, d.AddHours(0).AddMinutes(30)));
        Assert.Equal(d.AddHours(4), KmLichTuDong.HoanVongCheckDon(mocKmCombo, nhuongKhung: true));

        // Chỉ KM BẬT ⇒ nhường tới 04:00.
        var cfKmBat = Cf(comboBat: false, kmTuDong: true) with { OwnerMachine = "may-A:orders" };
        Assert.True(KmLichTuDong.NenNhuongKhung(cfKmBat, "may-A:orders", mocKmCombo, d.AddHours(0).AddMinutes(30)));
        Assert.Equal(d.AddHours(4), KmLichTuDong.HoanVongCheckDon(mocKmCombo, nhuongKhung: true));

        // Ngoài khung đêm (05:30) ⇒ KHÔNG nhường dù cả hai BẬT.
        var mocNgoai = d.AddHours(5).AddMinutes(30);
        var cfCaHaiBat = Cf(comboBat: true, kmTuDong: true) with { OwnerMachine = "may-A:orders" };
        Assert.False(KmLichTuDong.NenNhuongKhung(cfCaHaiBat, "may-A:orders", mocNgoai, d.AddHours(4)));
        Assert.Equal(mocNgoai, KmLichTuDong.HoanVongCheckDon(mocNgoai, nhuongKhung: true));
    }

    /// <summary>Hai mốc THỬ cũng phải riêng: lượt combo hỏng lúc 01:05 không được ăn lượt thử của vòng KM.</summary>
    [Fact]
    public void MocThuCuaHaiVong_LaHaiMocRieng()
    {
        var d = new DateTime(2026, 8, 25);
        var cf = Cf(comboThu: d.AddHours(1).AddMinutes(5));

        Assert.True(KmLichTuDong.DaThuTrongKhungNay(cf, d.AddHours(2), LoaiChuongTrinh.Combo));
        Assert.False(KmLichTuDong.DaThuTrongKhungNay(cf, d.AddHours(2), LoaiChuongTrinh.ChuongTrinhShop));
    }

    /// <summary>
    /// Vòng combo TẮT ⇒ không bao giờ chạy, dù đúng khung và quá hạn lâu. Và <b>công tắc của ba vòng ĐỘC LẬP
    /// nhau</b>: bật combo KHÔNG kéo theo flash.
    ///
    /// <para><b>⚠ Mốc kiểm của vế Flash phải nằm TRONG khung VÀ trước hạn 22:15</b> (sửa 28/08/2026, giữ nguyên
    /// tinh thần khi khung đổi thành cả ngày 01/09/2026). Bản đầu kiểm ở một mốc NGOÀI khung, nên test xanh vì
    /// "ngoài khung" chứ KHÔNG phải vì công tắc tắt: nó sẽ vẫn xanh kể cả khi <c>VongBat</c> bị sửa thành trả
    /// cứng <c>true</c>. Đúng kiểu "xanh vì lý do sai".</para>
    /// </summary>
    [Fact]
    public void ComboTat_VaFlashTat_ThiKhongChay()
    {
        var d = new DateTime(2026, 8, 25);
        Assert.False(KmLichTuDong.DenGioChay(Cf(comboBat: false), d.AddHours(2), LoaiChuongTrinh.Combo));

        // 22:00 — vẫn trong khung [0,23) và chưa qua hạn 22:15 ⇒ cửa duy nhất còn lại là công tắc `flash_bat`.
        var trongKhungFlash = d.AddHours(22);
        Assert.True(KmLichTuDong.TrongKhung(trongKhungFlash, LoaiChuongTrinh.FlashSale));
        Assert.False(KmLichTuDong.DenGioGoiFlash(Cf(comboBat: true), trongKhungFlash));
        Assert.False(KmLichTuDong.VongBat(Cf(comboBat: true), LoaiChuongTrinh.FlashSale));
    }

    /// <summary><c>SoNgay</c> DÙNG CHUNG cho cả ba vòng (người dùng: "3 ngày chạy 1 lần, 3 vòng riêng biệt").</summary>
    [Fact]
    public void SoNgay_DungCHUNG_choCaBaVong()
    {
        var d = new DateTime(2026, 8, 25);
        var cf = Cf(soNgay: 3, kmChayCuoi: d.AddHours(1), comboChayCuoi: d.AddHours(2));

        Assert.Equal(d.AddDays(3).AddHours(1), KmLichTuDong.MocChayKe(cf, d.AddHours(12), LoaiChuongTrinh.ChuongTrinhShop));
        Assert.Equal(d.AddDays(3).AddHours(1), KmLichTuDong.MocChayKe(cf, d.AddHours(12), LoaiChuongTrinh.Combo));
    }

    // ══════════════════════ §A2 — cột DB + migration ══════════════════════

    /// <summary>Dựng bảng <c>km_config</c> theo schema bản 22/08 (CHƯA có cột combo nào) + một dòng thật.</summary>
    private static void DungSchema2208(string path, long accountId)
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
    lan_thu_tu_dong TEXT,
    cfg_updated_at  TEXT,
    owner_machine   TEXT NOT NULL DEFAULT ''
);";
            cmd.ExecuteNonQuery();
        }
        using var ins = conn.CreateCommand();
        ins.CommandText = @"INSERT INTO km_config
                                (account_id, tu_dong, so_ngay, phan_tram, gio_bat_dau, gio_ket_thuc, lan_chay_cuoi)
                            VALUES ($id, 1, 7, 12, '08:00', '09:00', '2026-08-18T02:30:00.0000000');";
        ins.Parameters.AddWithValue("$id", accountId);
        ins.ExecuteNonQuery();
    }

    /// <summary>
    /// <b>DB CŨ (chưa có 9 cột combo) mở lên KHÔNG được ném</b>, và dòng cũ phải đọc ra ĐÚNG mặc định của bảng
    /// §A2 — quan trọng nhất là <c>ComboBat = false</c>: nâng cấp app không được tự bật một vòng người dùng
    /// chưa từng đặt.
    /// <para>Cùng cái bẫy đã cắn hai lần (<c>lan_thu_tu_dong</c> 19/08, <c>cfg_updated_at</c> 22/08):
    /// <c>CREATE TABLE IF NOT EXISTS</c> KHÔNG sửa bảng cũ, thiếu <c>ALTER TABLE</c> là <c>Get</c> ném
    /// "no such column" và hỏng CẢ MÀN Tài khoản.</para>
    /// </summary>
    [Fact]
    public void DbCu_ThieuCotCombo_ThiMigrationThemVao_DocRaMacDinh()
    {
        using var temp = new TempDatabase();
        DungSchema2208(temp.Path, AccId);

        var db = new Database(temp.Path);   // Initialize() chạy migration
        var repo = new KmConfigRepository(db);
        var cf = repo.Get(AccId);

        // Dữ liệu cũ CÒN NGUYÊN.
        Assert.True(cf.TuDong);
        Assert.Equal(12, cf.PhanTram);
        Assert.Equal("08:00", cf.GioBatDau);
        Assert.Equal(7, cf.SoNgay);   // dòng CŨ giữ giá trị người dùng đã đặt, không bị kéo về mặc định mới

        // 9 cột combo đọc ra ĐÚNG bảng §A2.
        Assert.False(cf.ComboBat);
        Assert.Equal("11:00", cf.ComboGioBatDau);
        Assert.Equal("12:00", cf.ComboGioKetThuc);
        Assert.Equal(3, cf.ComboLoai);
        Assert.Equal(2, cf.ComboMuaSl);
        Assert.Equal(2000, cf.ComboGiam);
        Assert.Equal(3, cf.ComboGioiHan);
        Assert.Null(cf.ComboLanChayCuoi);
        Assert.Null(cf.ComboLanThuTuDong);
    }

    /// <summary>Ghi rồi đọc lại 9 trường combo — vòng tròn qua SQLite, không mất trường nào.</summary>
    [Fact]
    public void LuuRoiDocLai_GiuNguyen9TruongCombo()
    {
        using var temp = new TempDatabase();
        var repo = new KmConfigRepository(new Database(temp.Path));
        var moc = new DateTime(2026, 8, 25, 6, 7, 0);

        repo.Save(new KmConfig(AccId, true, 3, 5, "09:30", "10:30", null, null)
        {
            ComboBat = true,
            ComboGioBatDau = "07:15",
            ComboGioKetThuc = "08:45",
            ComboLoai = 2,
            ComboMuaSl = 4,
            ComboGiam = 15,
            ComboGioiHan = 9,
            ComboLanChayCuoi = moc,
            ComboLanThuTuDong = moc.AddMinutes(-2),
        });

        var cf = repo.Get(AccId);
        Assert.True(cf.ComboBat);
        Assert.Equal("07:15", cf.ComboGioBatDau);
        Assert.Equal("08:45", cf.ComboGioKetThuc);
        Assert.Equal(2, cf.ComboLoai);
        Assert.Equal(4, cf.ComboMuaSl);
        Assert.Equal(15, cf.ComboGiam);
        Assert.Equal(9, cf.ComboGioiHan);
        Assert.Equal(moc, cf.ComboLanChayCuoi);
        Assert.Equal(moc.AddMinutes(-2), cf.ComboLanThuTuDong);

        // Và mốc của vòng KM KHÔNG bị lượt ghi combo đụng vào.
        Assert.Null(cf.LanChayCuoi);
        Assert.Null(cf.LanThuTuDong);
    }

    /// <summary>Tài khoản MỚI (chưa có dòng) nhận chu kỳ mặc định <b>3 ngày</b> — đổi từ 7 ngày 25/08/2026.</summary>
    [Fact]
    public void MacDinh_ChuKy_La3Ngay()
    {
        Assert.Equal(3, KmConfigRepository.MacDinhSoNgay);
        Assert.Equal(3, KmConfigRepository.MacDinh(AccId).SoNgay);

        using var temp = new TempDatabase();
        Assert.Equal(3, new KmConfigRepository(new Database(temp.Path)).Get(AccId).SoNgay);
    }

    // ══════════════════════ §A3 — luật thuần của form ══════════════════════

    /// <summary>
    /// <b>Tên combo mang NGÀY + GIỜ bắt đầu, 22 ký tự, vừa trần 25</b> (người dùng chốt 26/08/2026).
    /// <para>Bản trước chỉ mang ngày ⇒ hai lượt chạy CÙNG NGÀY đẻ hai combo TRÙNG TÊN trên cùng một shop; chạy
    /// lại để kiểm chứng một bản vá là phải đi gỡ tay từng shop (lượt thật 26/08 tạo combo trên 11 shop).</para>
    /// <para>Vế ĐỘ DÀI là vế phải giữ: 22 &lt; 25 nên KHÔNG bị cắt cụt. Cắt cụt ở đây là mất đúng phần giờ —
    /// tức quay lại chính ca trùng tên vừa sửa, mà không một dấu hiệu nào báo.</para>
    /// </summary>
    [Fact]
    public void SinhTenComboMacDinh_MangNgayVaGio_Va22KyTu()
    {
        var ten = KmDialogViewModel.SinhTenComboMacDinh(new DateTime(2027, 2, 15), "11:00");
        Assert.Equal("Combo 15-02-2027 11:00", ten);
        Assert.Equal(22, ten.Length);
        Assert.True(ten.Length <= KmDialogViewModel.TranDoDaiTenCombo);
        Assert.Equal(25, KmDialogViewModel.TranDoDaiTenCombo);

        // Hai lượt CÙNG NGÀY, khác giờ ⇒ khác tên. Đây là cả lý do tồn tại của vế giờ.
        Assert.NotEqual(ten, KmDialogViewModel.SinhTenComboMacDinh(new DateTime(2027, 2, 15), "23:30"));

        // Khuôn của vòng KM vẫn KHÔNG nhét vừa ô 25 ký tự — kể cả sau khi rút vế giờ kết thúc (26/08/2026):
        // "KM ngày 15-02-2027 từ 11:00" = 27 ký tự. Đây là lý do combo phải có khuôn riêng.
        Assert.True(KmDialogViewModel.SinhTenMacDinh(new DateTime(2027, 2, 15), "11:00").Length
                    > KmDialogViewModel.TranDoDaiTenCombo);
    }

    [Fact]
    public void CatTenCombo_CatDungTran25()
    {
        Assert.Equal(25, KmDialogViewModel.CatTenCombo(new string('x', 40)).Length);
        Assert.Equal("abc", KmDialogViewModel.CatTenCombo("abc"));
        Assert.Equal("", KmDialogViewModel.CatTenCombo(null));
    }

    /// <summary>Ma trận <c>KiemTraFormCombo</c>: từng vế sai → ĐÚNG câu lỗi của vế đó; hợp lệ → null.</summary>
    [Theory]
    // hợp lệ (mặc định của bảng §A2)
    [InlineData("Combo 15-02-2027", "11:00", 3, 2, 2000, 3, null)]
    [InlineData("Combo 15-02-2027", "11:00", 2, 2, 15, 1, null)]
    // tên
    [InlineData("", "11:00", 3, 2, 2000, 3, "Tên combo không được để trống.")]
    [InlineData("   ", "11:00", 3, 2, 2000, 3, "Tên combo không được để trống.")]
    [InlineData("KM ngày 15-02-2027 từ 11:00 - 12:00", "11:00", 3, 2, 2000, 3, "tối đa 25 ký tự")]
    // giờ — vế "kết thúc phải SAU bắt đầu" đã BỎ (26/08/2026): hai đầu khoảng dùng chung MỘT giờ.
    [InlineData("Combo", "9:00", 3, 2, 2000, 3, "Giờ của combo không hợp lệ")]
    [InlineData("Combo", "12:0", 3, 2, 2000, 3, "Giờ của combo không hợp lệ")]
    [InlineData("Combo", "24:00", 3, 2, 2000, 3, "Giờ của combo không hợp lệ")]
    [InlineData("Combo", null, 3, 2, 2000, 3, "Giờ của combo không hợp lệ")]
    // loại
    [InlineData("Combo", "11:00", 0, 2, 2000, 3, "Loại combo phải là 1")]
    [InlineData("Combo", "11:00", 4, 2, 2000, 3, "Loại combo phải là 1")]
    // mua N sp
    [InlineData("Combo", "11:00", 3, 1, 2000, 3, "tối thiểu 2 sản phẩm")]
    [InlineData("Combo", "11:00", 3, 0, 2000, 3, "tối thiểu 2 sản phẩm")]
    // mức giảm
    [InlineData("Combo", "11:00", 3, 2, 0, 3, "Mức giảm của combo phải ≥ 1.")]
    [InlineData("Combo", "11:00", 2, 2, 100, 3, "từ 1 đến 99")]
    [InlineData("Combo", "11:00", 3, 2, 100000, 3, null)]   // giảm SỐ TIỀN thì không có trần 99
    // giới hạn
    [InlineData("Combo", "11:00", 3, 2, 2000, 0, "Giới hạn đặt hàng của combo phải ≥ 1.")]
    public void KiemTraFormCombo_MaTran(
        string ten, string? gio, int loai, int muaSl, int giam, int gioiHan, string? chua)
    {
        var loi = KmDialogViewModel.KiemTraFormCombo(ten, gio, loai, muaSl, giam, gioiHan);
        if (chua is null) { Assert.Null(loi); }
        else { Assert.Contains(chua, loi); }
    }

    /// <summary>Form: 7 trường combo đi qua <c>LayCauHinh</c> → <c>BanLuuSauForm</c> mà không rơi trường nào;
    /// và <b>chỉ đổi thông số combo cũng phải được coi là "người dùng có sửa"</b> — thiếu vế này thì thay đổi
    /// combo bị nuốt lặng lẽ ở đường bấm Đóng.</summary>
    [Fact]
    public void Form_DoiRIENG_ThongSoCombo_ThiVAN_Luu()
    {
        var banNap = KmConfigRepository.MacDinh(AccId);
        var vm = new KmDialogViewModel(AccId, "a@b.c", 3, banNap);

        Assert.False(vm.ComboBat);
        vm.ComboBat = true;
        vm.ComboGiam = 5000;

        var tuForm = vm.LayCauHinh();
        Assert.True(tuForm.ComboBat);
        Assert.Equal(5000, tuForm.ComboGiam);

        var luu = KmDialogViewModel.BanLuuSauForm(banNap, banNap, tuForm, "may-A:orders", new DateTime(2026, 8, 25, 9, 0, 0));
        Assert.NotNull(luu);
        Assert.True(luu!.ComboBat);
        Assert.Equal(5000, luu.ComboGiam);
        Assert.Equal("may-A:orders", luu.OwnerMachine);
    }

    /// <summary>Mở form ra XEM rồi đóng (không chạm gì) vẫn KHÔNG lưu — vế combo không được phá luật (V5).</summary>
    [Fact]
    public void Form_ChiMoRaXem_ThiKhongLuu_KeCaKhiCoComBo()
    {
        var banNap = KmConfigRepository.MacDinh(AccId) with { ComboBat = true, ComboGiam = 7000 };
        var vm = new KmDialogViewModel(AccId, "a@b.c", 3, banNap);

        Assert.Null(KmDialogViewModel.BanLuuSauForm(
            banNap, banNap, vm.LayCauHinh(), "may-A:orders", new DateTime(2026, 8, 25, 9, 0, 0)));
    }

    // ══════════════════════ §A4 — gói tham số gửi xuống extension ══════════════════════

    /// <summary><see cref="ThamSoCombo.Goi"/>: HAI mốc ngày theo khuôn <c>KhuonNgayGoi</c> (y hệt
    /// <c>ThamSoKm</c>) + MỘT <c>gio</c> + 4 trường combo, và mọi giá trị đọc TỪ CHÍNH gói (đổi một trường của
    /// record là gói đổi theo).</summary>
    [Fact]
    public void ThamSoCombo_Goi_DungKhuonHaiMocNgay_MotGio_VaDu4TruongCombo()
    {
        var ts = new ThamSoCombo("Combo 19-08-2026",
            new DateTime(2026, 8, 19), new DateTime(2027, 2, 15), "11:00", 3, 2, 2000, 3);
        var json = System.Text.Json.JsonSerializer.Serialize(ts.Goi());

        Assert.Contains("\"ten\":\"Combo 19-08-2026\"", json);
        // Hai mốc, mỗi mốc đủ bốn trường. Kiểm cả CỤM (không phải từng mảnh rời) để một bản vá gửi cùng một
        // mốc cho hai ô là ĐỎ chứ không phải xanh vì "ngayTrongThang":19 vẫn có mặt ở đâu đó.
        Assert.Contains("\"bd\":{\"ngayTrongThang\":19,\"thang\":8,\"nam\":2026,\"ngayDayDu\":\"19-08-2026\"}", json);
        Assert.Contains("\"kt\":{\"ngayTrongThang\":15,\"thang\":2,\"nam\":2027,\"ngayDayDu\":\"15-02-2027\"}", json);
        Assert.Contains("\"gio\":\"11:00\"", json);
        Assert.DoesNotContain("gioKetThuc", json);
        Assert.Contains("\"loai\":3", json);
        Assert.Contains("\"muaSl\":2", json);
        Assert.Contains("\"giam\":2000", json);
        Assert.Contains("\"gioiHan\":3", json);
    }

    /// <summary><see cref="ThamSoCombo.MoTa"/> đọc từ CHÍNH gói — đây là thước đo của dòng nhật ký.</summary>
    [Fact]
    public void ThamSoCombo_MoTa_NoiDuNgay_Gio_UuDai_GioiHan()
    {
        var ts = new ThamSoCombo("Combo 19-08-2026",
            new DateTime(2026, 8, 19), new DateTime(2027, 2, 15), "11:00", 3, 2, 2000, 3);
        var moTa = ts.MoTa();
        Assert.Contains("Combo 19-08-2026", moTa);
        // CẢ HAI mốc của khoảng, CÙNG giờ — mô tả nói ra KHOẢNG, không phải một lát cắt.
        Assert.Contains("19-08-2026 11:00 → 15-02-2027 11:00", moTa);
        // Số tiền viết theo mốc TIẾNG VIỆT: dấu CHẤM phân nhóm (NHẸ-8). "2,000đ" của InvariantCulture làm
        // người đọc quen mốc Việt hiểu thành "2 phẩy 000".
        Assert.Contains("mua 2 sp giảm 2.000đ", moTa);
        Assert.DoesNotContain("2,000", moTa);
        Assert.Contains("giới hạn đặt hàng 3", moTa);

        Assert.Contains("mua 3 sp giảm 15%", (ts with { Loai = 2, MuaSl = 3, Giam = 15 }).MoTa());
        // Mốc triệu cũng phải đúng nhóm 3 chữ số: 1.500.000đ.
        Assert.Contains("giảm 1.500.000đ", (ts with { Giam = 1_500_000 }).MoTa());
    }

    // ══════════════════════ VỪA-3 — câu tổng kết nói ĐÚNG khung và ĐÚNG tên việc ══════════════════════

    /// <summary>
    /// <b>Lượt COMBO chạm hạn chót phải in giờ đóng khung của CHÍNH NÓ (4:00, cùng khung 1-4h với KM) và đúng tên việc.</b>
    /// <para>Vế đối chứng ngay dưới giữ vòng KM nguyên vẹn: 4:00 và "KM của Shop".</para>
    /// </summary>
    [Fact]
    public void CauTongKetCombo_HetKhung_NoiDungGio4_VaDungTenViec()
    {
        var kq = new KetQuaKm(
            [new KetQuaKmMotShop("s1", "Shop 1", DaTao: true, SoSanPham: 5, null, null)],
            Captcha: false, Loi: null, HetKhung: true, SoShopChuaChay: 4);

        var cauCombo = AccountsViewModel.CauTongKetCombo(kq, coPhienChayLai: true);
        Assert.StartsWith("Combo Khuyến Mãi xong:", cauCombo);
        Assert.Contains("hết khung tự động (4:00)", cauCombo);

        var trangThaiCombo = AccountsViewModel.CauTrangThaiKm(kq, LoaiChuongTrinh.Combo);
        Assert.StartsWith("Combo Khuyến Mãi:", trangThaiCombo);
        Assert.DoesNotContain("KM của Shop", trangThaiCombo);

        // ĐỐI CHỨNG — vòng KM giữ nguyên câu chữ cũ.
        var cauKm = AccountsViewModel.CauTongKetKm(kq, coPhienChayLai: true);
        Assert.StartsWith("KM của Shop xong:", cauKm);
        Assert.Contains("hết khung tự động (4:00)", cauKm);
        Assert.StartsWith("KM của Shop:", AccountsViewModel.CauTrangThaiKm(kq));
    }

    // ══════════════════════ §A5 — vòng combo đi ĐÚNG khung xuyên máy ══════════════════════

    private static (TempDatabase Temp, AppServices Services, AccountsViewModel Vm, long Id) DungVm(string email)
    {
        var temp = new TempDatabase();
        var services = new AppServices(temp.Path);
        services.Accounts.Insert(new Account { Email = email, Password = "p" });
        var id = services.Accounts.GetAll().First(a => a.Email == email).Id;
        return (temp, services, new AccountsViewModel(services), id);
    }

    private static KmConfig CauHinhCombo(long id)
        => new(id, TuDong: false, SoNgay: 3, PhanTram: 5, GioBatDau: "09:30", GioKetThuc: "10:30",
               LanChayCuoi: null, LanThuTuDong: null)
        { ComboBat = true };

    /// <summary>
    /// <b>Test khoá CHỖ GỌI của §A5:</b> lượt combo phải đi qua <c>ChayLatCatAsync</c> với
    /// <c>xinKhungXuyenMay: true</c>. Cho hook <c>XinKhungKm</c> trả "máy khác đang giữ" mãi ⇒ lượt phải DỪNG
    /// NGAY Ở CỬA: không xin khoá tài khoản, không phóng trình duyệt, có dòng nhật ký nêu tên máy kia, và
    /// <b>vẫn TRẢ khung</b>.
    /// <para><b>Vì sao phải khoá chỗ gọi chứ không chỉ khoá hàm:</b> bỏ cờ <c>xinKhungXuyenMay</c> ở
    /// <c>ChayComboAsync</c> thì mọi test hàm thuần vẫn xanh, mà production thành ra chạy combo trên một tài
    /// khoản máy khác đang check đơn — đúng họ lỗ phủ "hàm đúng, chỗ nối không ai đo" đã cắn ba lần.</para>
    /// <para><b>Gọi thẳng <c>ChayComboAsync</c>, KHÔNG qua <c>NhipLichComboAsync</c></b> (đổi 25/08/2026):
    /// <see cref="KmLichTuDong.ComboSanSang"/> chặn nhịp ở cửa đầu tiên, nên đi đường nhịp là test không bao giờ
    /// chạm tới thứ nó định đo. Đường bấm-tay/chặng-B vẫn đi đúng hàm này.</para>
    /// <para><b>Và dòng nhật ký ở đây nói ĐÚNG TÊN VIỆC</b> (VỪA-3/NHẸ-7): "BỎ lượt <b>Combo Khuyến Mãi</b>",
    /// không phải "BỎ lượt KM đêm nay" — lượt combo chạy 6h SÁNG.</para>
    /// </summary>
    [StaFact]
    public async Task VongCombo_DiQuaKhungXuyenMay_MayKhacGiu_ThiDungOCua_KhongPhongTrinhDuyet()
    {
        var hanGoc = AccountsViewModel.MacDinhHanChoKhung;
        var nhipGoc = AccountsViewModel.MacDinhNhipHoiKhung;
        AccountsViewModel.HanChoKhungXuyenMay = TimeSpan.FromMilliseconds(600);
        AccountsViewModel.NhipHoiKhungXuyenMay = TimeSpan.FromMilliseconds(50);
        try
        {
            var (temp, services, vm, id) = DungVm("combo-khung@mail.com");
            using var db = temp;
            var daXinKhoa = 0;
            var soLanTra = 0;
            services.AcquireAccountLease = (l, ct) => { daXinKhoa++; return Task.FromResult(new OrdersLeaseResult(true, null)); };
            services.ReleaseAccountLease = l => Task.CompletedTask;
            services.XinKhungKm = (l, ct) => Task.FromResult(new KmKhungResult(false, "NUC"));
            services.TraKhungKm = l => { soLanTra++; return Task.CompletedTask; };
            services.KmConfig.Save(CauHinhCombo(id));

            var bayGio = new DateTime(2026, 8, 25, 6, 5, 0);
            await vm.ChayComboAsync(
                id, "combo-khung@mail.com",
                AccountsViewModel.DungThamSoCombo(services.KmConfig.Get(id), bayGio),
                KmLichTuDong.HanChotLuotTuDong(bayGio, LoaiChuongTrinh.Combo));

            var nhat = string.Join("\n", services.Log.Snapshot("combo-khung@mail.com").Select(d => d.Message));
            Assert.Contains("NUC", nhat);                              // đã đi qua cửa khung xuyên máy
            Assert.Contains("BỎ lượt Combo Khuyến Mãi", nhat);         // …và nói ĐÚNG tên việc
            Assert.DoesNotContain("đêm nay", nhat);                    // lượt combo chạy 6h SÁNG
            Assert.Equal(0, daXinKhoa);                                // KHÔNG mở phiên, KHÔNG phóng trình duyệt
            Assert.Equal(1, soLanTra);                                 // vẫn TRẢ khung, dù bỏ lượt
        }
        finally
        {
            AccountsViewModel.HanChoKhungXuyenMay = hanGoc;
            AccountsViewModel.NhipHoiKhungXuyenMay = nhipGoc;
        }
    }

    /// <summary>
    /// <b>Gói <c>ThamSoCombo</c> của lượt tự động dựng TỪ MỘT NGUỒN</b> — <c>AccountsViewModel.DungThamSoCombo</c>
    /// là chỗ duy nhất tính ngày, và cả TÊN lẫn trường <c>Ngay</c> đều lấy từ đúng biến đó.
    /// <para>Đây là bài học lỗ phủ 25/08/2026: giữ hai lượt đọc độc lập thì đổi trường <c>Ngay</c> mà tên vẫn
    /// mang ngày đúng ⇒ 37/37 test combo VẪN XANH. Test này đo cả hai trong một phép so, cộng vế đối chứng
    /// "KHÔNG phải ngày hôm nay".</para>
    /// <para><b>Vế NHẬT KÝ đã khôi phục 26/08/2026</b> (chặng B lật <see cref="KmLichTuDong.ComboSanSang"/> nên
    /// nhịp lại ghi được dòng đó): xem <see cref="NhipCombo_ToiHan_DongLogDocTuChinhGoiThamSo"/>.</para>
    /// </summary>
    [Fact]
    public void DungThamSoCombo_MotNguon_KhoangLaHomNay_ToiHomNayCongSoNgayDatTruoc()
    {
        var bayGio = new DateTime(2026, 8, 19, 6, 5, 0);
        var cf = CauHinhCombo(AccId);

        var thamSo = AccountsViewModel.DungThamSoCombo(cf, bayGio);

        var ngayBd = bayGio.Date;                                        // 19-08-2026
        var ngayKt = bayGio.Date.AddDays(KmLichTuDong.SoNgayDatTruoc);   // 15-02-2027
        Assert.Equal(ngayBd, thamSo.NgayBatDau);
        Assert.Equal(ngayKt, thamSo.NgayKetThuc);
        // TÊN mang ngày BẮT ĐẦU **và GIỜ** (26/08/2026): hai lượt cùng ngày khác giờ ⇒ khác tên, không đẻ combo
        // trùng tên trên cùng một shop.
        Assert.Equal("Combo " + ngayBd.ToString("dd-MM-yyyy") + " " + thamSo.Gio, thamSo.Ten);

        // 6 trường thông số đi thẳng từ cấu hình, không trường nào rơi. `ComboGioKetThuc` KHÔNG còn đi xuống
        // Shopee (cột DB giữ lại, xem `KmDialogViewModel.GioKetThuc`).
        Assert.Equal(cf.ComboGioBatDau, thamSo.Gio);
        Assert.Equal(cf.ComboLoai, thamSo.Loai);
        Assert.Equal(cf.ComboMuaSl, thamSo.MuaSl);
        Assert.Equal(cf.ComboGiam, thamSo.Giam);
        Assert.Equal(cf.ComboGioiHan, thamSo.GioiHan);

        // ⚠ ĐO HAI MỐC MỘT CÁCH KHÔNG THỂ NHẦM: ghép nguyên cụm khoảng đúng khuôn `MoTa()`. Kiểm "ngày đúng có
        // xuất hiện đâu đó" là KHÔNG ĐỦ — TÊN chương trình cũng mang ngày bắt đầu.
        var moTa = thamSo.MoTa();
        Assert.Contains("19-08-2026 11:00 → 15-02-2027 11:00", moTa);
        // Vế đối chứng: một bản vá gửi CÙNG một mốc cho hai đầu sẽ dựng ra cụm dưới đây.
        Assert.DoesNotContain("19-08-2026 11:00 → 19-08-2026 11:00", moTa);
        Assert.DoesNotContain("15-02-2027 11:00 → 15-02-2027 11:00", moTa);
    }

    /// <summary>
    /// <b>Dòng nhật ký "tới hạn" của nhịp combo ĐỌC TỪ CHÍNH GÓI <see cref="ThamSoCombo"/> sắp gửi xuống</b> —
    /// không ghép lại từ <c>cf</c>.
    /// <para><b>Vì sao đây là thước quan trọng</b> (bài học lỗ phủ 25/08/2026,
    /// <c>plans/2026-08-25-km-dat-truoc-180-ngay.md</c>): giữ hai lượt đọc độc lập thì dòng log thành một chuỗi
    /// SONG SONG tự nhất quán — đổi ngày/giờ thật sự gửi xuống Shopee mà log vẫn in đúng, và không test nào đỏ.
    /// Đo trên dòng log của nhịp là đo đúng chỗ người trực đọc lúc 6h sáng.</para>
    /// <para>Cho khung xuyên máy trả "máy khác đang giữ" để lượt dừng ngay sau khi ghi log, KHÔNG phóng trình
    /// duyệt. Test này KHÔNG chạy được trước 26/08/2026: <see cref="KmLichTuDong.ComboSanSang"/> khi đó chặn
    /// nhịp ở cửa đầu tiên.</para>
    /// </summary>
    [StaFact]
    public async Task NhipCombo_ToiHan_DongLogDocTuChinhGoiThamSo()
    {
        var hanGoc = AccountsViewModel.MacDinhHanChoKhung;
        var nhipGoc = AccountsViewModel.MacDinhNhipHoiKhung;
        AccountsViewModel.HanChoKhungXuyenMay = TimeSpan.FromMilliseconds(600);
        AccountsViewModel.NhipHoiKhungXuyenMay = TimeSpan.FromMilliseconds(50);
        try
        {
            var (temp, services, vm, id) = DungVm("combo-log@mail.com");
            using var db = temp;
            var daXinKhoa = 0;
            services.AcquireAccountLease = (l, ct) => { daXinKhoa++; return Task.FromResult(new OrdersLeaseResult(true, null)); };
            services.ReleaseAccountLease = l => Task.CompletedTask;
            services.XinKhungKm = (l, ct) => Task.FromResult(new KmKhungResult(false, "NUC"));
            services.TraKhungKm = l => Task.CompletedTask;
            services.KmConfig.Save(CauHinhCombo(id));

            var bayGio = new DateTime(2026, 8, 19, 1, 5, 0);
            await vm.NhipLichComboAsync(bayGio);

            var nhat = string.Join("\n", services.Log.Snapshot("combo-log@mail.com").Select(d => d.Message));
            var thamSo = AccountsViewModel.DungThamSoCombo(services.KmConfig.Get(id), bayGio);
            // MoTa() là thứ dòng log phải in ra NGUYÊN VẸN — nó gói cả ngày, khung giờ, mức ưu đãi và giới hạn.
            Assert.Contains(thamSo.MoTa(), nhat);
            // Vế ĐỐI CHỨNG chặn "ngày nào cũng khớp": mốc KẾT THÚC không được đứng ở chỗ mốc bắt đầu.
            Assert.DoesNotContain("15-02-2027 11:00 → 15-02-2027 11:00", nhat);
            Assert.Contains("khung 1-4h", nhat);              // khung của COMBO (1-4h)
            Assert.Contains("sau 04:00", nhat);               // hạn chót lượt tự động của khung combo
            Assert.Equal(bayGio, services.KmConfig.Get(id).ComboLanThuTuDong);
            Assert.Equal(0, daXinKhoa);                       // dừng ở cửa khung xuyên máy, không mở phiên
        }
        finally
        {
            AccountsViewModel.HanChoKhungXuyenMay = hanGoc;
            AccountsViewModel.NhipHoiKhungXuyenMay = nhipGoc;
        }
    }

    /// <summary>Ngoài khung 01–04h thì nhịp combo KHÔNG làm gì — kể cả lúc 00:59 và 04:00.</summary>
    [StaFact]
    public async Task NhipCombo_NgoaiKhung_ThiKhongLamGi()
    {
        var (temp, services, vm, id) = DungVm("combo-ngoaikhung@mail.com");
        using var db = temp;
        services.AcquireAccountLease = (l, ct) => Task.FromResult(new OrdersLeaseResult(false, "MAY-KHAC"));
        services.ReleaseAccountLease = l => Task.CompletedTask;
        services.KmConfig.Save(CauHinhCombo(id));

        await vm.NhipLichComboAsync(new DateTime(2026, 8, 19, 0, 59, 0));
        await vm.NhipLichComboAsync(new DateTime(2026, 8, 19, 4, 0, 0));
        await vm.NhipLichComboAsync(new DateTime(2026, 8, 19, 6, 5, 0));

        Assert.Empty(services.Log.Snapshot("combo-ngoaikhung@mail.com"));
        Assert.Null(services.KmConfig.Get(id).ComboLanThuTuDong);
    }

    /// <summary>
    /// Cấu hình combo hỏng ⇒ luật kiểm mà NHỊP dùng phải bắt được, nhịp NÓI RA lý do và KHÔNG phóng gì — nhưng
    /// <b>vẫn ghi mốc THỬ</b>, kẻo nhịp 60 giây lặp lại chính dòng đó mỗi phút tới 4h.
    /// <para><b>Đổi 26/08/2026 — khôi phục hai vế cũ.</b> Ở chặng A, <see cref="KmLichTuDong.ComboSanSang"/>
    /// (<c>false</c>) chặn nhịp ở CỬA ĐẦU TIÊN nên nhánh cấu-hình-hỏng không với tới được, và test phải bỏ hai
    /// vế "log nói lý do" + "mốc thử vẫn ghi". Chặng B lật hằng ⇒ nhịp đi tới đúng nhánh đó ⇒ khôi phục.</para>
    /// </summary>
    [StaFact]
    public async Task NhipCombo_CauHinhHong_ThiNoiRaLyDo_GhiMocThu_VaKhongPhong()
    {
        var (temp, services, vm, id) = DungVm("combo-cauhinhhong@mail.com");
        using var db = temp;
        var daXinKhoa = 0;
        var daXinKhung = 0;
        services.AcquireAccountLease = (l, ct) => { daXinKhoa++; return Task.FromResult(new OrdersLeaseResult(false, "MAY-KHAC")); };
        services.ReleaseAccountLease = l => Task.CompletedTask;
        services.XinKhungKm = (l, ct) => { daXinKhung++; return Task.FromResult(new KmKhungResult(true, null)); };
        services.TraKhungKm = l => Task.CompletedTask;
        // Cảnh dựng ĐỔI 26/08/2026: vế "giờ kết thúc ≤ giờ bắt đầu" đã bỏ cùng ô nhập, nên ca hỏng mượn sang
        // vế ĐỊNH DẠNG GIỜ — vẫn còn sống, và vẫn đúng ý định đo của test này.
        var cfHong = CauHinhCombo(id) with { ComboGioBatDau = "11h00" };
        services.KmConfig.Save(cfHong);

        var bayGio = new DateTime(2026, 8, 19, 1, 5, 0);
        var thamSo = AccountsViewModel.DungThamSoCombo(cfHong, bayGio);
        var loi = KmDialogViewModel.KiemTraFormCombo(
            thamSo.Ten, thamSo.Gio,
            thamSo.Loai, thamSo.MuaSl, thamSo.Giam, thamSo.GioiHan);
        Assert.Contains("Giờ của combo không hợp lệ", loi);

        await vm.NhipLichComboAsync(bayGio);

        var nhat = string.Join("\n", services.Log.Snapshot("combo-cauhinhhong@mail.com").Select(d => d.Message));
        Assert.Contains("CẤU HÌNH KHÔNG DÙNG ĐƯỢC", nhat);
        Assert.Contains("Giờ của combo không hợp lệ", nhat);
        Assert.Equal(bayGio, services.KmConfig.Get(id).ComboLanThuTuDong);   // đốt lượt thử, không lặp mỗi phút
        Assert.Null(services.KmConfig.Get(id).ComboLanChayCuoi);            // nhưng KHÔNG đẩy lịch đi SoNgay ngày
        Assert.Equal(0, daXinKhoa);                                         // và KHÔNG phóng trình duyệt
        Assert.Equal(0, daXinKhung);
    }

    /// <summary><c>ChotLuotCombo</c> ghi <c>ComboLanChayCuoi</c>, <b>KHÔNG</b> ghi <c>LanChayCuoi</c> — chỗ này
    /// mà nhầm là vòng combo tự tay khoá vòng KM.</summary>
    [StaFact]
    public void ChotLuotCombo_GhiDungMocCuaVongCombo()
    {
        var (temp, services, vm, id) = DungVm("combo-chot@mail.com");
        using var db = temp;
        services.KmConfig.Save(CauHinhCombo(id));

        var luc = new DateTime(2026, 8, 25, 7, 30, 0);
        var kq = new KetQuaKm(
            [new KetQuaKmMotShop("s1", "Shop 1", DaTao: true, SoSanPham: 5, null, null)], false, null);

        var ketLuan = vm.ChotLuotCombo(id, kq, luc);

        var cf = services.KmConfig.Get(id);
        Assert.Equal(luc, cf.ComboLanChayCuoi);
        Assert.Null(cf.LanChayCuoi);
        Assert.StartsWith("Combo Khuyến Mãi xong: tạo 1/1 shop", ketLuan.Nhat);
    }

    /// <summary>Lượt tạo được 0 shop và có shop hỏng ⇒ KHÔNG ghi mốc (để đêm sau thử lại).</summary>
    [StaFact]
    public void ChotLuotCombo_KhongTaoDuocShopNao_ThiKhongGhiMoc()
    {
        var (temp, services, vm, id) = DungVm("combo-chot0@mail.com");
        using var db = temp;
        services.KmConfig.Save(CauHinhCombo(id));

        vm.ChotLuotCombo(id, new KetQuaKm(
            [new KetQuaKmMotShop("s1", "Shop 1", DaTao: false, SoSanPham: 0, null, "hỏng")], false, null),
            new DateTime(2026, 8, 25, 7, 30, 0));

        Assert.Null(services.KmConfig.Get(id).ComboLanChayCuoi);
    }

    /// <summary>
    /// Mọi shop đều BỎ QUA vì không còn sản phẩm để thêm (quét trọn vẹn, không lỗi, không captcha)
    /// ⇒ VẪN GHI mốc ComboLanChayCuoi để giữ chu kỳ 3 ngày/lần, không lặp lại mỗi đêm.
    /// </summary>
    [StaFact]
    public void ChotLuotCombo_MoiShopBoQua_KhongConSanPham_ThiGHI_Moc()
    {
        var (temp, services, vm, id) = DungVm("combo-chot-boqua@mail.com");
        using var db = temp;
        services.KmConfig.Save(CauHinhCombo(id));

        var luc = new DateTime(2026, 9, 17, 7, 30, 0);
        var shops = new List<KetQuaKmMotShop>
        {
            new("s1", "Shop 1", DaTao: false, SoSanPham: 0, BoQua: "modal KHÔNG còn sản phẩm", Loi: null),
            new("s2", "Shop 2", DaTao: false, SoSanPham: 0, BoQua: "modal KHÔNG còn sản phẩm", Loi: null),
        };
        var kq = new KetQuaKm(shops, Captcha: false, Loi: null, HetKhung: false, SoShopChuaChay: 0);

        vm.ChotLuotCombo(id, kq, luc);

        Assert.Equal(luc, services.KmConfig.Get(id).ComboLanChayCuoi);
    }
}
