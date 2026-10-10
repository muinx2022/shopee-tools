using Microsoft.Data.Sqlite;

namespace XuLyDonShopee.Core.Data;

/// <summary>
/// Cấu hình "KM của Shop" của MỘT tài khoản. Giờ lưu dạng <c>HH:mm</c> (chuỗi) chứ không phải
/// <see cref="TimeSpan"/>: đây là "giờ trong ngày" người dùng gõ trên form, không phải một khoảng thời gian —
/// và chuỗi thì đọc thẳng được khi soi database bằng tay.
/// </summary>
/// <param name="AccountId">Tài khoản sở hữu cấu hình.</param>
/// <param name="TuDong">Bật chạy tự động theo chu kỳ.</param>
/// <param name="SoNgay">Chu kỳ tự động: lần kế = lần chạy trước + ngần này ngày.</param>
/// <param name="PhanTram">% giảm áp chung cho mọi sản phẩm.</param>
/// <param name="GioBatDau">Giờ bắt đầu khuyến mãi, <c>HH:mm</c>.</param>
/// <param name="GioKetThuc">Giờ kết thúc khuyến mãi, <c>HH:mm</c>.</param>
/// <param name="LanChayCuoi">Mốc lần TẠO ĐƯỢC gần nhất (null = chưa lần nào) — gốc tính chu kỳ "sau xx ngày".</param>
/// <param name="LanThuTuDong">
/// Mốc lần lịch tự động BẮT ĐẦU THỬ gần nhất (null = chưa lần nào). Khác <paramref name="LanChayCuoi"/> ở chỗ
/// nó ghi NGAY KHI lượt tự động khởi động, bất kể lượt đó tạo được hay hỏng.
/// <para><b>Vì sao phải có riêng:</b> <paramref name="LanChayCuoi"/> chỉ ghi khi tạo được ⇒ một lượt hỏng
/// (captcha, máy khác đang giữ khoá, lỗi ở shop đầu) không để lại dấu vết nào, và nhịp đồng hồ 60 giây sẽ thử
/// lại LIÊN TỤC tới 6h sáng: hàng chục lượt SSO một đêm, đúng thứ làm Shopee bật verify. Mốc này cho phép
/// <c>KmLichTuDong.DenGioChay</c> chốt "mỗi tài khoản tối đa MỘT lượt thử mỗi khung 2–6h".</para>
/// <para>Lượt BẤM TAY KHÔNG ghi mốc này — người dùng bấm lại là chuyện của họ.</para>
/// </param>
public sealed record KmConfig(
    long AccountId,
    bool TuDong,
    int SoNgay,
    int PhanTram,
    string GioBatDau,
    string GioKetThuc,
    DateTime? LanChayCuoi,
    DateTime? LanThuTuDong = null)
{
    // HAI TRƯỜNG ĐỒNG BỘ XUYÊN MÁY (22/08/2026) — để trong THÂN record (property init) chứ KHÔNG thêm tham số
    // vị trí: hàng chục chỗ đang dựng bằng 7–8 tham số (kể cả test) sẽ không phải sửa, và `with { … }` vẫn
    // dùng được như thường.

    /// <summary>
    /// Mốc SỬA CẤU HÌNH gần nhất — <b>giờ địa phương</b>, y quy ước của mọi mốc trong bảng này. <c>null</c> =
    /// máy này chưa từng mở form KM cho tài khoản đó.
    /// <para><b>Đây là trọng tài của luật gộp xuyên máy</b> "máy nào sửa sau thắng" (<c>KmHoSoGop</c>): bản có
    /// mốc mới hơn mang trọn cấu hình của nó sang, bản <c>null</c> thua mọi bản có mốc. Nhờ vậy một máy chưa
    /// bao giờ mở form vẫn đẩy hồ sơ lên hub bình thường mà KHÔNG xoá cấu hình người khác vừa đặt.</para>
    /// </summary>
    public DateTime? CfgUpdatedAt { get; init; }

    /// <summary>
    /// MÁY CHỦ TRÌ — máy đã mở form KM và lưu cấu hình gần nhất (id suất đơn hàng). Rỗng = chưa máy nào nhận
    /// chủ trì (dữ liệu cũ, hoặc app chạy độc lập không có hub).
    /// <para><b>ĐÃ CÓ NGƯỜI ĐỌC</b> (22/08/2026): <c>KmLichTuDong.LaMayChuTri</c> chặn hai cửa —
    /// <c>AccountsViewModel.NhipLichKmAsync</c> (chỉ máy chủ trì chạy lượt KM tự động) và
    /// <c>KmLichTuDong.NenNhuongKhung</c> (chỉ máy chủ trì nhường khung check đơn 2–6h). Rỗng ⇒ giữ nguyên
    /// hành vi cũ ở cả hai cửa.</para>
    /// <para>Dòng của DB cũ được đóng dấu một lần qua <see cref="KmConfigRepository.BackfillOwnerMachineOnce"/>
    /// — không có bước đó thì cả hai cửa trên vô hiệu, vì rỗng nghĩa là "ai cũng được".</para>
    /// </summary>
    public string OwnerMachine { get; init; } = "";

    // ══════════════ COMBO KHUYẾN MÃI (25/08/2026) — chương trình thứ HAI của "KM của Shop" ══════════════
    // Cũng để trong THÂN record (property init), KHÔNG thêm tham số vị trí: hàng chục chỗ đang dựng bằng 7–8
    // tham số (kể cả test) sẽ không phải sửa. Cùng luật đã dùng cho CfgUpdatedAt/OwnerMachine ở trên.

    /// <summary>Bật vòng COMBO tự động (khung 06–08h) — <b>ĐỘC LẬP với <see cref="TuDong"/> của Chương Trình
    /// Của Shop</b>: người dùng chốt "ba vòng riêng biệt", bật cái này không kéo theo cái kia.</summary>
    public bool ComboBat { get; init; }

    /// <summary>Giờ bắt đầu chương trình combo, <c>HH:mm</c> (mặc định <c>11:00</c>).</summary>
    public string ComboGioBatDau { get; init; } = MacDinhComboGioBatDau;

    /// <summary>Giờ kết thúc chương trình combo, <c>HH:mm</c> (mặc định <c>12:00</c>).</summary>
    public string ComboGioKetThuc { get; init; } = MacDinhComboGioKetThuc;

    /// <summary>Loại combo trên Shopee: <c>2</c> = giảm theo %, <c>3</c> = giảm theo SỐ TIỀN (mặc định),
    /// <c>1</c> = giá đặc biệt. Đúng <c>value</c> của ô radio trên form Shopee.</summary>
    public int ComboLoai { get; init; } = MacDinhComboLoai;

    /// <summary>Mức ưu đãi — mua bao nhiêu sản phẩm (ô <c>tiers.0.minAmount</c>). Mặc định 2; mua 1 sp không
    /// thành combo nên luật kiểm đầu vào chặn dưới 2.</summary>
    public int ComboMuaSl { get; init; } = MacDinhComboMuaSl;

    /// <summary>Mức giảm: ĐỒNG nếu <see cref="ComboLoai"/> = 3, PHẦN TRĂM nếu = 2. Mặc định 2000 (đồng).</summary>
    public int ComboGiam { get; init; } = MacDinhComboGiam;

    /// <summary>Giới hạn đặt hàng (ô <c>usageLimit</c>). Mặc định 3.</summary>
    public int ComboGioiHan { get; init; } = MacDinhComboGioiHan;

    /// <summary>
    /// Mốc lần TẠO ĐƯỢC gần nhất của vòng COMBO (null = chưa lần nào).
    /// <para><b>⚠ PHẢI TÁCH KHỎI <see cref="LanChayCuoi"/> — xem <c>KmLichTuDong.MocThuCua</c>.</b> Dùng chung
    /// một mốc thì lượt KM lúc 02:05 tự tay khoá vòng combo lúc 06:05 cùng ngày, câm lặng và vĩnh viễn.</para>
    /// </summary>
    public DateTime? ComboLanChayCuoi { get; init; }

    /// <summary>Mốc vòng COMBO bắt đầu THỬ gần nhất (null = chưa lần nào) — chốt "mỗi khung 06–08h tối đa MỘT
    /// lượt thử", y vai trò của <see cref="LanThuTuDong"/> ở vòng kia.</summary>
    public DateTime? ComboLanThuTuDong { get; init; }

    // ═══════ FLASH SALE CỦA SHOP (28/08/2026) — chương trình thứ BA; khung CẢ NGÀY [0,23) từ 01/09/2026 ═══════
    // Cùng khuôn combo: property init trong THÂN record, KHÔNG thêm tham số vị trí.
    //
    // ⚠ CHỈ BA TRƯỜNG, và đó là CÓ CHỦ ĐÍCH. Flash Sale không có tên/khoảng ngày/mức giảm để cấu hình: toàn bộ
    // thông số của một đêm là NGÀY ĐÍCH, mà ngày đích suy được từ chính ngày chạy (hôm nay + 1). Việc của một
    // đêm là HÀM THUẦN CỦA NGÀY (plan §4) nên KHÔNG có — và đừng thêm — con trỏ tiến độ nào ở đây: không cột
    // "đã tới ngày nào", không cột "khung nào đã dùng", không danh sách shop đã xong. Một con trỏ như vậy sẽ
    // TRÔI LỆCH ngay khi một máy trong fleet bỏ lỡ một đêm, và không ai biết máy nào đúng.

    /// <summary>Bật vòng FLASH SALE tự động (nhiều GÓI nhỏ cả ngày) — <b>ĐỘC LẬP với <see cref="TuDong"/> và
    /// <see cref="ComboBat"/></b>: ba vòng riêng biệt, bật cái này không kéo theo cái kia. Mặc định TẮT.</summary>
    public bool FlashBat { get; init; }

    /// <summary>
    /// Mốc lần TẠO ĐƯỢC gần nhất của vòng FLASH (null = chưa lần nào).
    /// <para><b>⚠ PHẢI TÁCH khỏi <see cref="LanChayCuoi"/> và <see cref="ComboLanChayCuoi"/></b> — xem
    /// <c>KmLichTuDong.MocThuCua</c>: gộp mốc là một vòng tự tay khoá vòng kia, câm lặng và vĩnh viễn.</para>
    /// </summary>
    public DateTime? FlashLanChayCuoi { get; init; }

    /// <summary>
    /// Mốc vòng FLASH bắt đầu THỬ gần nhất (null = chưa lần nào) — gốc đo của chốt <b>GIÃN CÁCH 30 phút giữa
    /// hai GÓI</b> (<c>KmLichTuDong.PhutGianCachGoiFlash</c>; trước 01/09/2026 là chốt "mỗi khung một lượt thử").
    /// <para><b>Thiếu trường này thì vòng flash là ca hỏng NẶNG NHẤT trong ba vòng</b>: cửa chủ trì và mọi lối
    /// ra hỏng cố ý KHÔNG ghi mốc ngày, nên <c>DenGioGoiFlash</c> không có gì để đo giãn cách ⇒ mở gói mới MỖI
    /// PHÚT, mà khung nay là CẢ NGÀY ⇒ hàng trăm lần phóng Brave một ngày.</para>
    /// </summary>
    public DateTime? FlashLanThuTuDong { get; init; }

    /// <summary>
    /// <b>%GIẢM áp HÀNG LOẠT cho mọi sản phẩm của mọi khung flash</b> — ô thứ NHẤT của khối "Chỉnh sửa hàng
    /// loạt" trên form flash Shopee. Mặc định <see cref="MacDinhFlashPhanTramGiam"/> (5 — mức tối thiểu Shopee
    /// cho phép).
    /// <para><b>Trước 03/09/2026 đây là hằng <c>PHAN_TRAM_GIAM_FLASH = 5</c> trong
    /// <c>extensions/shopee-orders/flow-flashsale.js</c></b>; người dùng chốt "cho phép nhập từ client, lần sau
    /// là 10% thì không phải sửa code". Hai hằng ấy đã XOÁ khỏi extension — con số nay đi trọn đường
    /// form → <c>km_config</c> → <c>ThamSoFlashSale.Goi()</c> → <c>docThamSoFlash</c>.</para>
    /// <para><b>Thuộc nhóm CẤU HÌNH</b> (không phải mốc chạy) ⇒ vào <see cref="CungCauHinh"/> và đi theo GÓI của
    /// luật gộp "máy nào sửa sau thắng". Thiếu ở <see cref="CungCauHinh"/> thì người dùng đổi 5 → 8 rồi bấm Đóng
    /// là "không sửa gì" ⇒ KHÔNG lưu ⇒ cú gõ biến mất không dấu vết.</para>
    /// </summary>
    public int FlashPhanTramGiam { get; init; } = MacDinhFlashPhanTramGiam;

    /// <summary>
    /// <b>"SL sản phẩm khuyến mãi" áp HÀNG LOẠT</b> — ô thứ HAI của cùng khối "Chỉnh sửa hàng loạt", tức số
    /// lượng MỖI sản phẩm được bán trong khung. Mặc định <see cref="MacDinhFlashSoLuongThamGia"/> (10).
    /// <para><b>⚠ KHÔNG PHẢI số sản phẩm mỗi khung</b> (người dùng đính chính 03/09/2026 khi được hỏi lại): số
    /// sản phẩm vẫn là 10 SP bán chạy nhất của MỘT trang, và đó là luật của luồng, không phải cấu hình.</para>
    /// </summary>
    public int FlashSoLuongThamGia { get; init; } = MacDinhFlashSoLuongThamGia;

    /// <summary>
    /// PURE — hai bản có CÙNG phần CẤU HÌNH không (bỏ qua mốc chạy <c>LanChayCuoi</c>/<c>LanThuTuDong</c> và
    /// bỏ qua chính <see cref="CfgUpdatedAt"/>).
    /// <para><b>Một luật, hai nơi dùng — cố ý:</b> (1) đường LƯU FORM chỉ đóng dấu
    /// <see cref="CfgUpdatedAt"/>/<see cref="OwnerMachine"/> khi người dùng THỰC SỰ đổi gì đó (mở form ra xem
    /// rồi bấm Đóng không được cướp chủ trì); (2) worker đồng bộ chỉ ghi nhật ký khi lượt gộp làm ĐỔI cấu
    /// hình. Hai chỗ mà hai định nghĩa "đổi" thì sẽ có ca log một đằng đóng dấu một nẻo.</para>
    /// <para><b>Có tính <see cref="OwnerMachine"/></b>: đổi máy chủ trì là thay đổi đáng ghi vào nhật ký, và ở
    /// đường lưu form thì hai bên so sánh đều lấy chủ trì từ dòng đang có nên trường này không tự gây lệch.
    /// <b>KHÔNG tính <see cref="CfgUpdatedAt"/></b>: nó chỉ là con dấu thời gian của chính lượt sửa — tính vào
    /// thì mọi so sánh đều "khác" và cả hai công dụng ở trên thành vô nghĩa.</para>
    /// </summary>
    public static bool CungCauHinh(KmConfig a, KmConfig b)
        => a.TuDong == b.TuDong
           && a.SoNgay == b.SoNgay
           && a.PhanTram == b.PhanTram
           && a.GioBatDau == b.GioBatDau
           && a.GioKetThuc == b.GioKetThuc
           && string.Equals(a.OwnerMachine ?? "", b.OwnerMachine ?? "", StringComparison.Ordinal)
           // 7 trường CẤU HÌNH combo — cùng nhóm "ý muốn người dùng", nên cùng luật. Hai MỐC chạy combo cố ý
           // KHÔNG tính vào đây, y như hai mốc của vòng kia.
           && a.ComboBat == b.ComboBat
           && string.Equals(a.ComboGioBatDau ?? "", b.ComboGioBatDau ?? "", StringComparison.Ordinal)
           && string.Equals(a.ComboGioKetThuc ?? "", b.ComboGioKetThuc ?? "", StringComparison.Ordinal)
           && a.ComboLoai == b.ComboLoai
           && a.ComboMuaSl == b.ComboMuaSl
           && a.ComboGiam == b.ComboGiam
           && a.ComboGioiHan == b.ComboGioiHan
           // Công tắc vòng FLASH — thuộc nhóm "ý muốn người dùng", nên cùng luật. Hai MỐC chạy flash cố ý
           // KHÔNG tính vào đây, y như hai cặp mốc của hai vòng kia.
           && a.FlashBat == b.FlashBat
           // Hai THÔNG SỐ flash nhập từ form (03/09/2026) — cùng nhóm "ý muốn người dùng" nên cùng luật. Thiếu
           // chúng ở đây là cú đổi 5% → 8% bị coi là "không sửa gì" ⇒ không lưu, không dấu vết.
           && a.FlashPhanTramGiam == b.FlashPhanTramGiam
           && a.FlashSoLuongThamGia == b.FlashSoLuongThamGia;

    // Mặc định của 7 trường cấu hình combo — MỘT chỗ duy nhất, để `MacDinh`, khởi tạo property và câu
    // `CREATE TABLE`/`ALTER TABLE` không trôi khỏi nhau.
    internal const string MacDinhComboGioBatDau = "11:00";
    internal const string MacDinhComboGioKetThuc = "12:00";
    internal const int MacDinhComboLoai = 3;      // 3 = giảm theo SỐ TIỀN
    internal const int MacDinhComboMuaSl = 2;
    internal const int MacDinhComboGiam = 2000;   // đồng
    internal const int MacDinhComboGioiHan = 3;

    // Mặc định của 2 thông số FLASH — cùng luật một-chỗ-duy-nhất với cụm combo trên. PHẢI khớp `DEFAULT` của hai
    // cột `km_config` (xem `Database.Initialize`), khởi tạo property ở trên, và `KmProfileItem.MacDinhFlash*`
    // của hợp đồng hub (+ `DEFAULT` cột `km_profiles`). 5% là mức tối thiểu Shopee cho phép; 10 là con số quy
    // trình tay người dùng mô tả 17:4x 31/08/2026.
    internal const int MacDinhFlashPhanTramGiam = 5;
    internal const int MacDinhFlashSoLuongThamGia = 10;
}

/// <summary>
/// Đọc/ghi bảng <c>km_config</c> — cấu hình "KM của Shop" theo TỪNG tài khoản (khoá <c>account_id</c>).
/// <para><b>Cố ý KHÔNG dùng <c>SettingsRepository</c></b>: bảng <c>settings</c> là cấu hình TOÀN CỤC của app
/// (thư mục phiếu, nhịp check đơn…), còn mỗi tài khoản có lịch KM riêng. Nhét vào đó là mọi tài khoản dùng
/// chung một % giảm và một chu kỳ.</para>
/// </summary>
public class KmConfigRepository
{
    private readonly Database _db;

    public KmConfigRepository(Database db) => _db = db;

    /// <summary>
    /// Mặc định khi tài khoản CHƯA có bản ghi — khớp mặc định trên form (09:30–10:30, 5%), mặc định combo
    /// (11:00–12:00, giảm 2.000đ khi mua 2, giới hạn 3) qua các hằng <c>KmConfig.MacDinhCombo*</c>, và mặc định
    /// flash (giảm 5%, SL tham gia 10) qua <c>KmConfig.MacDinhFlash*</c> — hai trường sau nhận giá trị từ chính
    /// khởi tạo property của record, nên không phải liệt kê lại ở đây và cũng không trôi lệch được.
    /// <para><b>Chu kỳ mặc định 3 NGÀY</b> (đổi từ 7 ngày, người dùng chốt 25/08/2026: "3 ngày chạy 1 lần, 3
    /// vòng riêng biệt"). Chỉ ảnh hưởng tài khoản CHƯA có dòng — dòng cũ giữ nguyên số người dùng đã đặt.</para>
    /// </summary>
    public static KmConfig MacDinh(long accountId)
        => new(accountId, TuDong: false, SoNgay: MacDinhSoNgay, PhanTram: 5,
               GioBatDau: "09:30", GioKetThuc: "10:30", LanChayCuoi: null);

    /// <summary>Chu kỳ tự động mặc định (ngày) — dùng chung cho cả ba vòng, khớp <c>DEFAULT</c> của cột
    /// <c>so_ngay</c> trong <c>Database.Initialize</c>.</summary>
    public const int MacDinhSoNgay = 3;

    /// <summary>Đọc cấu hình của một tài khoản; chưa có bản ghi ⇒ trả <see cref="MacDinh"/> (không null).</summary>
    public KmConfig Get(long accountId)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT tu_dong, so_ngay, phan_tram, gio_bat_dau, gio_ket_thuc, lan_chay_cuoi,
                                   lan_thu_tu_dong, cfg_updated_at, owner_machine,
                                   combo_bat, combo_gio_bat_dau, combo_gio_ket_thuc, combo_loai,
                                   combo_mua_sl, combo_giam, combo_gioi_han,
                                   combo_lan_chay_cuoi, combo_lan_thu_tu_dong,
                                   flash_bat, flash_lan_chay_cuoi, flash_lan_thu_tu_dong,
                                   flash_phan_tram_giam, flash_so_luong_tham_gia
                            FROM km_config WHERE account_id = $id;";
        cmd.Parameters.AddWithValue("$id", accountId);

        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            return MacDinh(accountId);
        }

        return DocDong(accountId, r);
    }

    /// <summary>
    /// MỌI dòng cấu hình đang có trong bảng, khoá theo <c>account_id</c> — cho worker đồng bộ hồ sơ KM lên hub.
    /// <para>CỐ Ý khác <see cref="Get"/>: hàm kia trả <see cref="MacDinh"/> khi chưa có dòng nào, nên gọi nó
    /// cho từng tài khoản thì không phân biệt được "người dùng đã đặt lịch" với "chưa ai đụng tới". Đẩy cả mớ
    /// mặc định lên hub là tạo hồ sơ rỗng cho mọi tài khoản của mọi máy — rác, và làm mờ đúng thứ cần nhìn.</para>
    /// </summary>
    public Dictionary<long, KmConfig> GetAll()
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT account_id, tu_dong, so_ngay, phan_tram, gio_bat_dau, gio_ket_thuc,
                                   lan_chay_cuoi, lan_thu_tu_dong, cfg_updated_at, owner_machine,
                                   combo_bat, combo_gio_bat_dau, combo_gio_ket_thuc, combo_loai,
                                   combo_mua_sl, combo_giam, combo_gioi_han,
                                   combo_lan_chay_cuoi, combo_lan_thu_tu_dong,
                                   flash_bat, flash_lan_chay_cuoi, flash_lan_thu_tu_dong,
                                   flash_phan_tram_giam, flash_so_luong_tham_gia
                            FROM km_config;";

        var map = new Dictionary<long, KmConfig>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetInt64(0);
            map[id] = DocDong(id, r, cot0: 1);
        }
        return map;
    }

    /// <summary>Dựng <see cref="KmConfig"/> từ dòng đang đọc. <paramref name="cot0"/> = chỉ số cột
    /// <c>tu_dong</c> (0 khi câu SELECT không lấy <c>account_id</c>, 1 khi có) — một chỗ đọc duy nhất để
    /// <see cref="Get"/> và <see cref="GetAll"/> không lệch nhau khi thêm cột.</summary>
    private static KmConfig DocDong(long accountId, SqliteDataReader r, int cot0 = 0) => new(
        accountId,
        TuDong: r.GetInt32(cot0) != 0,
        SoNgay: ChuanHoaSoNgay(r.GetInt32(cot0 + 1)),
        PhanTram: r.GetInt32(cot0 + 2),
        GioBatDau: r.GetString(cot0 + 3),
        GioKetThuc: r.GetString(cot0 + 4),
        LanChayCuoi: r.IsDBNull(cot0 + 5) ? null : DocMoc(r.GetString(cot0 + 5)),
        LanThuTuDong: r.IsDBNull(cot0 + 6) ? null : DocMoc(r.GetString(cot0 + 6)))
    {
        CfgUpdatedAt = r.IsDBNull(cot0 + 7) ? null : DocMoc(r.GetString(cot0 + 7)),
        // Dòng của DB CŨ (vừa ALTER TABLE thêm cột) mang NULL → rỗng = "chưa có chủ trì".
        OwnerMachine = r.IsDBNull(cot0 + 8) ? "" : r.GetString(cot0 + 8),
        // 9 cột COMBO. Migration thêm chúng dạng NOT NULL DEFAULT nên dòng cũ đã có sẵn giá trị mặc định; vẫn
        // giữ nhánh IsDBNull vì một DB bị sửa tay không được làm vỡ màn Tài khoản.
        ComboBat = !r.IsDBNull(cot0 + 9) && r.GetInt32(cot0 + 9) != 0,
        ComboGioBatDau = r.IsDBNull(cot0 + 10) ? KmConfig.MacDinhComboGioBatDau : r.GetString(cot0 + 10),
        ComboGioKetThuc = r.IsDBNull(cot0 + 11) ? KmConfig.MacDinhComboGioKetThuc : r.GetString(cot0 + 11),
        ComboLoai = r.IsDBNull(cot0 + 12) ? KmConfig.MacDinhComboLoai : r.GetInt32(cot0 + 12),
        ComboMuaSl = r.IsDBNull(cot0 + 13) ? KmConfig.MacDinhComboMuaSl : r.GetInt32(cot0 + 13),
        ComboGiam = r.IsDBNull(cot0 + 14) ? KmConfig.MacDinhComboGiam : r.GetInt32(cot0 + 14),
        ComboGioiHan = r.IsDBNull(cot0 + 15) ? KmConfig.MacDinhComboGioiHan : r.GetInt32(cot0 + 15),
        ComboLanChayCuoi = r.IsDBNull(cot0 + 16) ? null : DocMoc(r.GetString(cot0 + 16)),
        ComboLanThuTuDong = r.IsDBNull(cot0 + 17) ? null : DocMoc(r.GetString(cot0 + 17)),
        // 3 cột FLASH SALE. `flash_bat` khai NOT NULL DEFAULT 0 nên dòng cũ đã có sẵn 0 = TẮT; vẫn giữ nhánh
        // IsDBNull vì một DB bị sửa tay không được làm vỡ màn Tài khoản.
        FlashBat = !r.IsDBNull(cot0 + 18) && r.GetInt32(cot0 + 18) != 0,
        FlashLanChayCuoi = r.IsDBNull(cot0 + 19) ? null : DocMoc(r.GetString(cot0 + 19)),
        FlashLanThuTuDong = r.IsDBNull(cot0 + 20) ? null : DocMoc(r.GetString(cot0 + 20)),
        // 2 cột THÔNG SỐ flash (03/09/2026). Migration khai NOT NULL DEFAULT nên dòng cũ đã có sẵn 5/10; nhánh
        // IsDBNull vẫn giữ — một DB bị sửa tay không được làm vỡ màn Tài khoản, và rơi về mặc định ở đây là
        // đúng ngữ nghĩa ("chưa ai đặt" = mức tối thiểu Shopee).
        FlashPhanTramGiam = r.IsDBNull(cot0 + 21)
            ? KmConfig.MacDinhFlashPhanTramGiam : r.GetInt32(cot0 + 21),
        FlashSoLuongThamGia = r.IsDBNull(cot0 + 22)
            ? KmConfig.MacDinhFlashSoLuongThamGia : r.GetInt32(cot0 + 22),
    };

    /// <summary>UPSERT cấu hình theo <c>account_id</c> — tài khoản khác nhau không đè nhau (khoá chính).</summary>
    public void Save(KmConfig cf)
    {
        using var conn = _db.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO km_config
                                (account_id, tu_dong, so_ngay, phan_tram, gio_bat_dau, gio_ket_thuc,
                                 lan_chay_cuoi, lan_thu_tu_dong, cfg_updated_at, owner_machine,
                                 combo_bat, combo_gio_bat_dau, combo_gio_ket_thuc, combo_loai,
                                 combo_mua_sl, combo_giam, combo_gioi_han,
                                 combo_lan_chay_cuoi, combo_lan_thu_tu_dong,
                                 flash_bat, flash_lan_chay_cuoi, flash_lan_thu_tu_dong,
                                 flash_phan_tram_giam, flash_so_luong_tham_gia)
                            VALUES ($id, $td, $sn, $pt, $gb, $gk, $lc, $ltd, $cua, $om,
                                    $cbat, $cgb, $cgk, $cloai, $cmua, $cgiam, $cgh, $clc, $cltd,
                                    $fbat, $flc, $fltd, $fptg, $fsltg)
                            ON CONFLICT(account_id) DO UPDATE SET
                                tu_dong = $td, so_ngay = $sn, phan_tram = $pt,
                                gio_bat_dau = $gb, gio_ket_thuc = $gk, lan_chay_cuoi = $lc,
                                lan_thu_tu_dong = $ltd, cfg_updated_at = $cua, owner_machine = $om,
                                combo_bat = $cbat, combo_gio_bat_dau = $cgb, combo_gio_ket_thuc = $cgk,
                                combo_loai = $cloai, combo_mua_sl = $cmua, combo_giam = $cgiam,
                                combo_gioi_han = $cgh, combo_lan_chay_cuoi = $clc,
                                combo_lan_thu_tu_dong = $cltd,
                                flash_bat = $fbat, flash_lan_chay_cuoi = $flc,
                                flash_lan_thu_tu_dong = $fltd,
                                flash_phan_tram_giam = $fptg,
                                flash_so_luong_tham_gia = $fsltg;";
        cmd.Parameters.AddWithValue("$id", cf.AccountId);
        cmd.Parameters.AddWithValue("$td", cf.TuDong ? 1 : 0);
        cmd.Parameters.AddWithValue("$sn", cf.SoNgay);
        cmd.Parameters.AddWithValue("$pt", cf.PhanTram);
        cmd.Parameters.AddWithValue("$gb", cf.GioBatDau);
        cmd.Parameters.AddWithValue("$gk", cf.GioKetThuc);
        cmd.Parameters.AddWithValue("$lc", (object?)cf.LanChayCuoi?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ltd", (object?)cf.LanThuTuDong?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cua", (object?)cf.CfgUpdatedAt?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$om", cf.OwnerMachine ?? "");
        cmd.Parameters.AddWithValue("$cbat", cf.ComboBat ? 1 : 0);
        cmd.Parameters.AddWithValue("$cgb", cf.ComboGioBatDau ?? KmConfig.MacDinhComboGioBatDau);
        cmd.Parameters.AddWithValue("$cgk", cf.ComboGioKetThuc ?? KmConfig.MacDinhComboGioKetThuc);
        cmd.Parameters.AddWithValue("$cloai", cf.ComboLoai);
        cmd.Parameters.AddWithValue("$cmua", cf.ComboMuaSl);
        cmd.Parameters.AddWithValue("$cgiam", cf.ComboGiam);
        cmd.Parameters.AddWithValue("$cgh", cf.ComboGioiHan);
        cmd.Parameters.AddWithValue("$clc", (object?)cf.ComboLanChayCuoi?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cltd", (object?)cf.ComboLanThuTuDong?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fbat", cf.FlashBat ? 1 : 0);
        cmd.Parameters.AddWithValue("$flc", (object?)cf.FlashLanChayCuoi?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fltd", (object?)cf.FlashLanThuTuDong?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fptg", cf.FlashPhanTramGiam);
        cmd.Parameters.AddWithValue("$fsltg", cf.FlashSoLuongThamGia);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Khoá "đã chạy" của <see cref="BackfillOwnerMachineOnce"/> trong bảng <c>settings</c>.</summary>
    internal const string KhoaBackfillOwnerMachine = "backfill_km_owner_machine_v1";

    /// <summary>
    /// <b>Đóng dấu <paramref name="mayNay"/> làm MÁY CHỦ TRÌ cho các dòng <c>km_config</c> ĐANG BẬT KM
    /// (<c>tu_dong = 1</c>) mà chưa có chủ trì.</b> CHẠY ĐÚNG MỘT LẦN, chốt bằng khoá trong bảng
    /// <c>settings</c> (khuôn <c>Database.BackfillCfgUpdatedAtOnce</c>).
    /// <para><b>Thiếu bước này thì cửa chủ trì VÔ HIỆU trên mọi máy đang có dữ liệu</b> (phản biện vòng 3,
    /// 22/08/2026): lượt backfill của vòng 2 chỉ đóng dấu <c>cfg_updated_at</c>, nên dòng cũ có
    /// <c>owner_machine</c> rỗng ⇒ <c>KmLichTuDong.LaMayChuTri("", …)</c> trả <c>true</c> ⇒ <b>mọi máy đều được
    /// chạy</b> ⇒ đúng cuộc đua mà cửa chủ trì sinh ra để chặn, ngay đêm đầu sau nâng cấp. Chỗ ghi
    /// <c>OwnerMachine</c> duy nhất là đường lưu form, mà đường đó không ghi gì khi người dùng không đổi gì.</para>
    /// <para><b>Chỉ dòng <c>tu_dong = 1</c></b> — máy đang BẬT lịch KM chính là máy người dùng đã mở form. Dòng
    /// tắt để rỗng: không ai chủ trì một lịch đang tắt, và đóng dấu bừa vào đó là biến một máy tình cờ nâng cấp
    /// trước thành chủ trì của một lịch nó chưa từng đặt.</para>
    /// <para><b>Vì sao KHÔNG nằm trong <c>Database.Initialize</c></b> như lượt backfill mốc sửa: tầng
    /// <c>XuLyDonShopee.Core</c> không biết máy này là máy nào (id suất đơn hàng do shell suite rót qua
    /// <c>AppServices.MayNay</c> — chính lý do hook đó tồn tại). Nơi gọi là
    /// <c>OrdersModuleHost.WireKmSync</c>, ngay sau khi rót hook, TRƯỚC lượt đẩy đầu tiên.</para>
    /// <para><paramref name="mayNay"/> rỗng (app chạy độc lập / chưa có hub) ⇒ KHÔNG làm gì và KHÔNG đánh dấu
    /// đã chạy: lần sau có hub thì vẫn còn cơ hội đóng dấu.</para>
    /// </summary>
    /// <returns>Số dòng vừa được đóng dấu (0 khi đã chạy rồi, hoặc không có dòng nào cần).</returns>
    public int BackfillOwnerMachineOnce(string? mayNay)
    {
        var may = mayNay?.Trim() ?? "";
        if (may.Length == 0)
        {
            return 0;
        }

        using var conn = _db.OpenConnection();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT 1 FROM settings WHERE key = $k;";
            check.Parameters.AddWithValue("$k", KhoaBackfillOwnerMachine);
            if (check.ExecuteScalar() is not null)
            {
                return 0;
            }
        }

        int soDong;
        using (var upd = conn.CreateCommand())
        {
            upd.CommandText = @"UPDATE km_config SET owner_machine = $may
                                WHERE tu_dong = 1 AND (owner_machine IS NULL OR owner_machine = '');";
            upd.Parameters.AddWithValue("$may", may);
            soDong = upd.ExecuteNonQuery();
        }

        using var mark = conn.CreateCommand();
        mark.CommandText = "INSERT INTO settings(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v;";
        mark.Parameters.AddWithValue("$k", KhoaBackfillOwnerMachine);
        mark.Parameters.AddWithValue("$v", DateTime.UtcNow.ToString("O"));
        mark.ExecuteNonQuery();
        return soDong;
    }

    /// <summary>
    /// PURE — chu kỳ ĐỌC RA luôn ≥ 1 ngày, khớp <c>Math.Max(1, …)</c> mà <c>KmLichTuDong.DenGioChay</c> đã dùng.
    /// <para><b>Vì sao chuẩn hoá ngay lúc ĐỌC</b> (phản biện vòng 3, 19/08/2026): các bản trước ghi được
    /// <c>so_ngay = 0</c> vào DB. Dòng đó nay đụng lượt kiểm cấu hình của <c>NhipLichKmAsync</c> (hạng mục 21)
    /// và bị chặn — mỗi đêm một dòng "CẤU HÌNH KHÔNG DÙNG ĐƯỢC" cho một tài khoản mà lịch thì vẫn chạy đúng theo
    /// <c>Math.Max</c> kia. Sửa ở cửa đọc thì mọi tầng phía trên (form, lịch, kiểm cấu hình) thấy CÙNG một con
    /// số, thay vì mỗi tầng tự vá theo cách riêng.</para>
    /// </summary>
    internal static int ChuanHoaSoNgay(int soNgay) => soNgay < 1 ? 1 : soNgay;

    /// <summary>Đọc mốc ISO-8601; hỏng ⇒ null (dữ liệu cũ/sửa tay không được làm vỡ màn hình).</summary>
    private static DateTime? DocMoc(string s)
        => DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
}
