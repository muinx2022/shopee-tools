using Microsoft.Data.Sqlite;

namespace XuLyDonShopee.Core.Data;

/// <summary>
/// Quản lý kết nối và khởi tạo cơ sở dữ liệu SQLite cục bộ.
/// </summary>
public class Database
{
    /// <summary>Đường dẫn file .db đang dùng.</summary>
    public string Path { get; }

    private readonly string _connectionString;

    /// <summary>
    /// Khởi tạo database. Nếu <paramref name="dbPath"/> null thì dùng đường dẫn mặc định
    /// (%APPDATA%\XuLyDonShopee\app.db trên Windows, ~/.config/XuLyDonShopee/app.db trên Linux).
    /// Tự tạo thư mục và các bảng nếu chưa có.
    /// </summary>
    public Database(string? dbPath = null)
    {
        Path = dbPath ?? DefaultPath();

        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path
        }.ToString();

        Initialize();
    }

    /// <summary>Đường dẫn file DB mặc định trong thư mục dữ liệu ứng dụng của người dùng.</summary>
    public static string DefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return System.IO.Path.Combine(appData, "XuLyDonShopee", "app.db");
    }

    /// <summary>
    /// Thư mục MẶC ĐỊNH lưu phiếu/hóa đơn khi người dùng chưa chọn ở Cài đặt:
    /// <c>{thư mục chứa app.db}\Phieu-giao-hang</c> (nằm cạnh dữ liệu app). Là nguồn mặc định của
    /// <see cref="SettingsRepository.GetInvoiceFolder"/>.
    /// </summary>
    public string DefaultInvoiceDir()
        => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path) ?? ".", "Phieu-giao-hang");

    /// <summary>Mở một kết nối mới (đã Open). Caller chịu trách nhiệm Dispose.</summary>
    public SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void Initialize()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS accounts (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    Email      TEXT NOT NULL,
    Password   TEXT NOT NULL,
    Phone      TEXT,
    Cookie     TEXT,
    Note       TEXT,
    ProxyKey   TEXT,
    PickupAddress TEXT,
    VerifyEmail TEXT,
    VerifyEmailPassword TEXT,
    Status     TEXT NOT NULL,
    CreatedAt  TEXT NOT NULL,
    UpdatedAt  TEXT NOT NULL,
    verify_failed_at TEXT,
    last_checked_shop_login TEXT
);

CREATE TABLE IF NOT EXISTS proxies (
    Id         INTEGER PRIMARY KEY AUTOINCREMENT,
    Host       TEXT NOT NULL,
    Port       INTEGER NOT NULL,
    Username   TEXT,
    Password   TEXT,
    Type       TEXT NOT NULL,
    Status     TEXT NOT NULL,
    CreatedAt  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS settings (
    key   TEXT PRIMARY KEY,
    value TEXT
);

CREATE TABLE IF NOT EXISTS orders (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    account_id         INTEGER NOT NULL,
    shop_id            TEXT,
    order_sn           TEXT NOT NULL,
    shopee_order_id    TEXT,
    buyer_username     TEXT,
    items_json         TEXT,
    item_count         INTEGER,
    item_summary       TEXT,
    sku                TEXT,
    gsheet_synced_at   TEXT,
    gsheet_file_url    TEXT,
    gsheet_da_huy      INTEGER,
    gsheet_da_co_van_don INTEGER,
    gsheet_da_co_uoc_tinh INTEGER,
    gsheet_da_co_don_tra_hang INTEGER,
    gsheet_tab         TEXT,
    gsheet_push_gen    INTEGER NOT NULL DEFAULT 0,
    return_request_code TEXT,
    hub_synced_at      TEXT,
    hub_push_gen       INTEGER NOT NULL DEFAULT 0,
    hub_push_gen_sent  INTEGER,
    hub_slip_synced_at TEXT,
    sold_counted_at    TEXT,
    total_price        INTEGER,
    total_price_text   TEXT,
    final_amount       INTEGER,
    final_amount_text  TEXT,
    payment_method     TEXT,
    status             TEXT,
    status_description TEXT,
    cancel_reason      TEXT,
    channel            TEXT,
    carrier            TEXT,
    tracking_number    TEXT,
    synced_at          TEXT,
    created_at         TEXT,
    updated_at         TEXT,
    UNIQUE(account_id, order_sn)
);

CREATE TABLE IF NOT EXISTS account_shops (
    account_id INTEGER NOT NULL,
    shop_login TEXT NOT NULL,
    shop_name  TEXT,
    sort_order INTEGER,                -- vị trí shop theo ĐÚNG thứ tự trang /portal/shop (NULL = dữ liệu cũ)
    return_count_last INTEGER,         -- MỐC CŨ, đếm trên tab Tất cả — chỉ giữ để chẩn đoán, KHÔNG còn đọc/ghi
    return_count_last_tra_hang INTEGER,-- mốc ĐANG DÙNG: số yêu cầu ở tab Đơn Trả hàng Hoàn tiền lần check gần
                                       -- nhất (NULL = chưa từng check → lượt sau quét trang đầu một lượt)
    tra_hang_con_sot INTEGER NOT NULL DEFAULT 0, -- 1 = lượt check trả hàng gần nhất BIẾT là còn sót (chạm trần
                                       -- dòng/trang, lật trượt, 0 dòng) ⇒ lượt sau VẪN lật trang dù trang đầu
                                       -- không có mã mới. Xem ShopFlowRunner.CheckDonTraHangAsync.
    tra_hang_sot_ly_do TEXT NOT NULL DEFAULT '', -- VÌ SAO còn sót ('' = không sót): 'tran' (chạm trần, danh
                                       -- sách vơi bớt là tự hết) | 'sap_xep' (không đổi được sắp xếp — cần
                                       -- người xem selector/UI) | 'lat_truot' (tạm thời) | 'doc_hong' (ô tổng
                                       -- có số mà 0 dòng — selector dòng có thể đã đổi). Chỉ để chẩn đoán;
                                       -- quyết định chế độ rút tồn đọng vẫn theo tra_hang_con_sot.
    updated_at TEXT NOT NULL,
    UNIQUE(account_id, shop_login)
);

-- MÃ YÊU CẦU TRẢ HÀNG, SỐNG ĐỘC LẬP với vòng đời đơn. Shopee cho trả hàng trong 15 ngày, mà app dọn đơn KẾT
-- THÚC ngay khi đã ghi sheet + đếm + đẩy hub (OrderPersistPipeline.NenXoaDonKetThuc) — nên lúc yêu cầu trả hàng xuất
-- hiện thì đơn thường đã bị xoá khỏi `orders` và mã quét được bị vứt đi (đó là lý do số mã lấy được vẫn là 0).
-- Bảng này là nguồn sự thật MỚI cho mã trả hàng; `orders.return_request_code` từ nay chỉ để HIỂN THỊ.
--
-- ⚠ TUYỆT ĐỐI KHÔNG gắn khoá ngoại tới `orders` và KHÔNG xoá theo đơn — đó là toàn bộ mục đích của bảng. Chống
-- phình bằng DỌN THEO TUỔI (ReturnCodesRepository.DonDep, mặc định 90 ngày).
--   · gsheet_synced_at NULL = mã CHƯA đẩy lên Google Sheet (mã ĐỔI thì reset về NULL để đẩy lại).
--   · shop_login thuần để chẩn đoán (mã này của shop nào).
CREATE TABLE IF NOT EXISTS return_codes (
    account_id INTEGER NOT NULL,
    order_sn   TEXT NOT NULL,
    code       TEXT NOT NULL,
    shop_login TEXT,
    created_at TEXT NOT NULL,
    gsheet_synced_at TEXT,
    PRIMARY KEY (account_id, order_sn)
);

CREATE TABLE IF NOT EXISTS prepare_daily (
    account_id INTEGER NOT NULL,
    shop_login TEXT NOT NULL,
    day        TEXT NOT NULL,          -- yyyy-MM-dd (giờ địa phương)
    count      INTEGER NOT NULL DEFAULT 0,
    UNIQUE(account_id, shop_login, day)
);

-- SO SAN PHAM da nap vao chuong trinh khuyen mai theo (tai khoan, shop, ngay, loai) — nguon cua ba cot
-- KM | Combo | Flash Sale o luoi tab Shops. `loai` = LoaiChuongTrinh (0=ChuongTrinhShop, 1=Combo, 2=FlashSale).
-- CONG DON y het prepare_daily: chay combo hai luot trong mot ngay la tao HAI chuong trinh, tong sp cua ngay
-- phai la tong ca hai — ghi de se nuot mat luot truoc.
-- ⚠ KHONG CO DONG = hom do KHONG chay loai nay (o luoi hien dau gach ngang); dong voi so 0 = DA chay shop nay nhung khong
-- tao duoc sp nao (bo qua vi modal rong / shop hong). Hai ca do KHAC NHAU voi nguoi doc, nen vong chay ghi dong
-- cho MOI shop da chay qua — xem OrdersBridgeSession.ChayVongTaoChuongTrinhAsync.
CREATE TABLE IF NOT EXISTS km_program_daily (
    account_id  INTEGER NOT NULL,
    shop_login  TEXT NOT NULL,
    day         TEXT NOT NULL,          -- yyyy-MM-dd (gio dia phuong), cung khuon prepare_daily
    loai        INTEGER NOT NULL,       -- LoaiChuongTrinh: 0=KM Shop, 1=Combo, 2=Flash Sale
    so_san_pham INTEGER NOT NULL DEFAULT 0,
    UNIQUE(account_id, shop_login, day, loai)
);

-- Banner canh bao loi dia chi lay hang tren tab Ket qua: ben toi khi user bam X
-- (khong tu an theo vong / ngay / restart). Khoa (account_id, shop_login); dismissed_at NULL = dang hien.
CREATE TABLE IF NOT EXISTS pickup_address_alerts (
    account_id INTEGER NOT NULL,
    shop_login TEXT NOT NULL,
    province TEXT DEFAULT '',
    created_at TEXT NOT NULL,
    dismissed_at TEXT,
    PRIMARY KEY(account_id, shop_login)
);

-- Cau hinh KM cua Shop theo TUNG TAI KHOAN. Co y KHONG dung bang settings: settings la cau hinh TOAN CUC
-- cua app, con moi tai khoan co lich KM rieng (% giam, khung gio, chu ky tu dong).
-- gio_bat_dau / gio_ket_thuc luu dang HH:mm; lan_chay_cuoi la ISO-8601 (NULL = chua chay lan nao).
-- lan_thu_tu_dong: moc luot TU DONG bat dau THU gan nhat (ISO-8601, NULL = chua lan nao) — ghi ngay khi luot
-- khoi dong ke ca luot hong, de moi tai khoan chi thu MOT lan moi khung 2-6h.
-- cfg_updated_at: moc SUA CAU HINH gan nhat (ISO-8601, GIO DIA PHUONG nhu moi moc khac cua bang nay) — trong
-- tai cua luat gop xuyen may (may nao sua sau thi thang — xem KmHoSoGop). NULL = may nay chua tung mo form.
-- owner_machine: MAY CHU TRI = may da mo form KM va luu cau hinh gan nhat. Rong = chua may nao nhan chu tri.
-- combo_*: COMBO KHUYEN MAI (25/08/2026) — chuong trinh thu HAI cua KM cua Shop, khung 06-08h. 7 cot dau la
-- CAU HINH (nguoi dung sua tren form), 2 cot moc cuoi la SU KIEN DA XAY RA.
-- ⚠ combo_lan_chay_cuoi / combo_lan_thu_tu_dong PHAI TACH khoi lan_chay_cuoi / lan_thu_tu_dong: dung chung thi
-- luot KM luc 02:05 tu tay khoa vong combo luc 06:05 cung ngay (xem KmLichTuDong.MocThuCua) — cam lang, vinh vien.
-- so_ngay DEFAULT 3 (doi tu 7 ngay 25/08/2026): 3 ngay chay 1 lan, 3 vong rieng biet. Chi ap cho dong MOI.
CREATE TABLE IF NOT EXISTS km_config (
    account_id      INTEGER PRIMARY KEY,
    tu_dong         INTEGER NOT NULL DEFAULT 0,
    so_ngay         INTEGER NOT NULL DEFAULT 3,
    phan_tram       INTEGER NOT NULL DEFAULT 5,
    gio_bat_dau     TEXT NOT NULL DEFAULT '09:30',
    gio_ket_thuc    TEXT NOT NULL DEFAULT '10:30',
    lan_chay_cuoi   TEXT,
    lan_thu_tu_dong TEXT,
    cfg_updated_at  TEXT,
    owner_machine   TEXT NOT NULL DEFAULT '',
    combo_bat            INTEGER NOT NULL DEFAULT 0,
    combo_gio_bat_dau    TEXT NOT NULL DEFAULT '11:00',
    combo_gio_ket_thuc   TEXT NOT NULL DEFAULT '12:00',
    combo_loai           INTEGER NOT NULL DEFAULT 3,
    combo_mua_sl         INTEGER NOT NULL DEFAULT 2,
    combo_giam           INTEGER NOT NULL DEFAULT 2000,
    combo_gioi_han       INTEGER NOT NULL DEFAULT 3,
    combo_lan_chay_cuoi     TEXT,
    combo_lan_thu_tu_dong   TEXT,
    flash_bat               INTEGER NOT NULL DEFAULT 0,
    flash_lan_chay_cuoi     TEXT,
    flash_lan_thu_tu_dong   TEXT,
    flash_phan_tram_giam    INTEGER NOT NULL DEFAULT 5,
    flash_so_luong_tham_gia INTEGER NOT NULL DEFAULT 10
);";
        cmd.ExecuteNonQuery();

        // Migration cho DB CŨ đã tồn tại: CREATE TABLE IF NOT EXISTS ở trên KHÔNG sửa bảng cũ, nên
        // thêm cột mới bằng ALTER TABLE ADD COLUMN (không phá dữ liệu người dùng đang có).
        EnsureColumn(conn, "accounts", "ProxyKey", "TEXT");
        EnsureColumn(conn, "accounts", "PickupAddress", "TEXT");

        // Mốc lượt KM TỰ ĐỘNG bắt đầu thử gần nhất — thêm cho DB CŨ đã có bảng km_config (bản 19/08 sáng).
        // Thiếu cột này thì `KmConfigRepository.Get` ném ngay khi mở màn Tài khoản.
        EnsureColumn(conn, "km_config", "lan_thu_tu_dong", "TEXT");

        // ĐỒNG BỘ HỒ SƠ KM XUYÊN MÁY (22/08/2026) — thêm cho DB CŨ đã có bảng km_config. Thiếu 2 cột này thì
        // `KmConfigRepository.Get` ném "no such column" NGAY khi mở màn Tài khoản, y hệt ca lan_thu_tu_dong ở
        // trên. cfg_updated_at = mốc sửa cấu hình (trọng tài của luật "máy nào sửa sau thắng");
        // owner_machine = máy chủ trì (máy mở form KM gần nhất). Dòng cũ nhận NULL/'' = "chưa biết" — đúng ngữ
        // nghĩa cần: bản chưa từng sửa THUA mọi bản có mốc, và chưa có chủ trì thì chạy y hành vi hiện tại.
        EnsureColumn(conn, "km_config", "cfg_updated_at", "TEXT");
        EnsureColumn(conn, "km_config", "owner_machine", "TEXT");

        // COMBO KHUYẾN MÃI (25/08/2026) — 9 cột cho DB CŨ đã có bảng km_config. Thiếu chúng thì
        // `KmConfigRepository.Get` ném "no such column" NGAY khi mở màn Tài khoản, y hệt hai ca ở trên.
        // Khai NOT NULL DEFAULT để dòng cũ nhận thẳng giá trị mặc định của bảng §A2 (11:00–12:00, giảm 2.000đ
        // khi mua 2, giới hạn 3, VÒNG COMBO TẮT) — không có dòng nào tự dưng bật một vòng người dùng chưa đặt.
        // Hai cột mốc để nullable: NULL = "chưa chạy lần nào", đúng ngữ nghĩa của cặp mốc bên vòng kia.
        EnsureColumn(conn, "km_config", "combo_bat", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "km_config", "combo_gio_bat_dau", "TEXT NOT NULL DEFAULT '11:00'");
        EnsureColumn(conn, "km_config", "combo_gio_ket_thuc", "TEXT NOT NULL DEFAULT '12:00'");
        EnsureColumn(conn, "km_config", "combo_loai", "INTEGER NOT NULL DEFAULT 3");
        EnsureColumn(conn, "km_config", "combo_mua_sl", "INTEGER NOT NULL DEFAULT 2");
        EnsureColumn(conn, "km_config", "combo_giam", "INTEGER NOT NULL DEFAULT 2000");
        EnsureColumn(conn, "km_config", "combo_gioi_han", "INTEGER NOT NULL DEFAULT 3");
        EnsureColumn(conn, "km_config", "combo_lan_chay_cuoi", "TEXT");
        EnsureColumn(conn, "km_config", "combo_lan_thu_tu_dong", "TEXT");

        // FLASH SALE CUA SHOP (28/08/2026) — vong thu BA; tu 01/09/2026 chay thanh nhieu GOI NHO ca ngay.
        // Ba cot, dung khuon combo:
        //  · flash_bat = CONG TAC BAT/TAT vong flash, MAC DINH TAT — khong dong nao tu dung bat mot vong
        //    nguoi dung chua dat (y luat da dung cho combo_bat).
        //  · flash_lan_chay_cuoi / flash_lan_thu_tu_dong PHAI TACH khoi cap moc cua hai vong kia. Thieu
        //    flash_lan_thu_tu_dong thi cho GIAN CACH GOI khong co gi de do ⇒ nhip 60 giay mo goi moi MOI PHUT
        //    khi goi dau hong nhanh (xem KmLichTuDong.DenGioGoiFlash).
        //  ⚠ VAN CHI BA COT: tien do trong ngay (shop nao da xong) giu IN-MEMORY o AccountsViewModel, KHONG
        //    xuong DB — mot con tro nhu vay se TROI LECH giua cac may trong fleet.
        EnsureColumn(conn, "km_config", "flash_bat", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "km_config", "flash_lan_chay_cuoi", "TEXT");
        EnsureColumn(conn, "km_config", "flash_lan_thu_tu_dong", "TEXT");

        // HAI THONG SO FLASH NHAP TU FORM (03/09/2026) — nguoi dung chot "5% va 10 sp cho phep nhap tu client,
        // khong fix trong code". Truoc do chung la hai hang PHAN_TRAM_GIAM_FLASH / SO_LUONG_KM_FLASH trong
        // extensions/shopee-orders/flow-flashsale.js (da XOA).
        //  · NOT NULL DEFAULT 5 / 10 — dong CU nhan dung hai con so dang chay that, khong doi hanh vi cua may
        //    nao dang chay; DEFAULT phai khop KmConfig.MacDinhFlash* va DEFAULT cot ben km_profiles cua hub.
        //  · Thieu hai cot nay thi KmConfigRepository.Get nem "no such column" NGAY khi mo man Tai khoan — y he
        //    ba ca migration km_config phia tren.
        EnsureColumn(conn, "km_config", "flash_phan_tram_giam", "INTEGER NOT NULL DEFAULT 5");
        EnsureColumn(conn, "km_config", "flash_so_luong_tham_gia", "INTEGER NOT NULL DEFAULT 10");

        // Backfill MỘT LẦN mốc sửa cho các dòng km_config ĐÃ CÓ lúc migration chạy — xem xmldoc.
        BackfillCfgUpdatedAtOnce(conn);

        // Hộp thư Hotmail/Outlook nhận mail xác minh Shopee + mật khẩu hộp thư — thêm cho DB CŨ (chưa có 2 cột).
        EnsureColumn(conn, "accounts", "VerifyEmail", "TEXT");
        EnsureColumn(conn, "accounts", "VerifyEmailPassword", "TEXT");

        // Cờ "TK chưa xác nhận": thời điểm (UTC ISO-8601) lần cuối autorun phát hiện phiên còn kẹt ở trang
        // verify/login/captcha khi kết thúc lượt (NULL = bình thường). Bền qua restart để danh sách TK lỗi còn
        // đó hôm sau; tự gỡ khi phiên đăng nhập được. Thêm cho DB CŨ.
        EnsureColumn(conn, "accounts", "verify_failed_at", "TEXT");

        // Mốc shop đã kiểm tra hoàn tất gần nhất trong vòng check đơn (NULL = chưa chạy / vừa xong cả vòng).
        // Khi chạy lại phiên, tiếp tục từ shop kế tiếp thay vì chạy lại từ đầu. Thêm cho DB CŨ.
        EnsureColumn(conn, "accounts", "last_checked_shop_login", "TEXT");

        // "Số tiền cuối cùng" lấy từ trang chi tiết đơn (cột "Ước tính" ở màn Đơn hàng) — thêm cho DB CŨ đã
        // có bảng orders (kiểm cột tồn tại trước khi ALTER, không phá dữ liệu sẵn có).
        EnsureColumn(conn, "orders", "final_amount", "INTEGER");
        EnsureColumn(conn, "orders", "final_amount_text", "TEXT");

        // SKU sản phẩm (5 ký tự alphanumeric cuối cùng của tên sản phẩm) — thêm cho DB CŨ.
        EnsureColumn(conn, "orders", "sku", "TEXT");

        // Cờ chống đẩy TRÙNG lên Google Sheet: gsheet_synced_at = thời điểm đã ghi đơn lên sheet;
        // gsheet_file_url = link file phiếu đã upload (cột C); gsheet_da_huy = trạng thái hủy ĐÃ ĐẨY lần
        // trước (0/1; NULL = chưa đẩy) để đổi màu khi trạng thái hủy thay đổi; gsheet_da_co_van_don = lần đẩy
        // gần nhất có gửi mã vận đơn chưa (0/1; NULL = chưa đẩy) để tự điền cột B khi vận đơn xuất hiện sau;
        // gsheet_da_co_uoc_tinh = lần đẩy sheet gần nhất ĐÃ gửi kèm số ước tính (final_amount) chưa (0/1;
        // NULL = chưa từng đẩy) để tự đẩy lại điền ĐÚNG số tiền khi ước tính xuất hiện sau. Thêm cho DB CŨ.
        EnsureColumn(conn, "orders", "gsheet_synced_at", "TEXT");
        EnsureColumn(conn, "orders", "gsheet_file_url", "TEXT");
        EnsureColumn(conn, "orders", "gsheet_da_huy", "INTEGER");
        EnsureColumn(conn, "orders", "gsheet_da_co_van_don", "INTEGER");
        EnsureColumn(conn, "orders", "gsheet_da_co_uoc_tinh", "INTEGER");

        // Tab (sheet) đã ghi LẦN ĐẦU của đơn: từ bản "tab theo tháng" trở đi, tab đích tự tính "Tháng MM-yyyy"
        // (hoặc override ở Cài đặt). Nhớ tab lần đầu để đơn cũ CẬP NHẬT lại (bổ sung vận đơn/phiếu/đổi màu hủy)
        // vẫn về ĐÚNG tab đã ghi — không nhân đôi dòng khi sang tháng mới. Thêm cho DB CŨ.
        EnsureColumn(conn, "orders", "gsheet_tab", "TEXT");

        // Backfill MỘT LẦN cho đơn ĐÃ ghi sheet TRƯỚC bản này (gsheet_synced_at NOT NULL) nhưng chưa có gsheet_tab:
        // chúng nằm ở tab tên trong setting cũ (mặc định LEGACY "tháng 4"). Nhớ lại kẻo lần đẩy cập nhật sau nhân
        // đôi dòng ở tab tháng mới. NULLIF(TRIM(...),'') coi setting chuỗi-trắng như chưa đặt → mặc định "tháng 4".
        // Idempotent tự nhiên: chỉ chạm dòng gsheet_tab CÒN NULL (chạy lại lần 2 không đổi gì).
        BackfillGsheetTab(conn);

        // Cờ chống đẩy TRÙNG lên HUB đơn hàng (module Đơn hàng đẩy đơn lên hub sau mỗi lượt Sync):
        // hub_synced_at = thời điểm đơn ĐÃ được hub nhận OK (NULL = chưa đẩy, còn trong hàng đợi ngầm → lượt
        // sync sau tự đẩy bù khi hub sống lại). Thêm cho DB CŨ.
        EnsureColumn(conn, "orders", "hub_synced_at", "TEXT");

        // Cờ chống đẩy TRÙNG FILE PHIẾU lên HUB (module Đơn hàng đẩy phiếu PDF sau khi đơn đã lên hub):
        // hub_slip_synced_at = thời điểm phiếu của đơn ĐÃ được hub lưu OK (NULL = chưa đẩy phiếu → lượt sync sau
        // tự đẩy khi có file phiếu local hợp lệ). Thêm cho DB CŨ.
        EnsureColumn(conn, "orders", "hub_slip_synced_at", "TEXT");

        // Cờ chống ĐẾM TRÙNG "Đã bán" theo SKU trên kho hub: sold_counted_at = thời điểm đơn ĐÃ được tính +1
        // (hoặc grandfather = đơn đã-giao-sẵn được đánh dấu KHÔNG +1). NULL = chưa xử lý đếm. Đơn CHUYỂN từ
        // chưa-giao → đã-giao mới +1; set cờ SAU khi hub +1 OK (kẻo mất đếm nếu hub lỗi). Thêm cho DB CŨ.
        EnsureColumn(conn, "orders", "sold_counted_at", "TEXT");

        // Mã shop (mô hình 1 tài khoản subaccount = nhiều shop): gắn shop_id vào mỗi đơn để lọc theo shop khi đẩy
        // GSheet (cột Tên Shop = tên đăng nhập shop) — KHÔNG đẩy nhầm đơn shop này với tên shop kia. Đơn CŨ shop_id
        // NULL (vẫn đẩy như trước theo account khi lọc shopId=null). KHÔNG backfill. Thêm cho DB CŨ.
        EnsureColumn(conn, "orders", "shop_id", "TEXT");

        // Tên đăng nhập shop (vd "alina99.store") để HIỂN THỊ/LỌC màn Đơn hàng — khác shop_id (SỐ). Sync gắn từ
        // `_currentShopLogin` (callback cầu nối set trước khi lưu); đơn cũ NULL (fallback "(shop ?)" khi hiển thị).
        // KHÔNG backfill. Thêm cho DB CŨ.
        EnsureColumn(conn, "orders", "shop_login", "TEXT");

        // Thời điểm ĐÃ "chuẩn bị hàng" xong đơn (ISO-8601 UTC; NULL = chưa/không biết — đơn arrange TRƯỚC bản này).
        // Ghi LẦN ĐẦU thôi (COALESCE ở MarkPrepared) rồi đẩy lên hub để hub đếm số đơn chuẩn bị hàng chung cho cả
        // fleet (đếm từ bảng đơn, không cộng bộ đếm rời của từng máy). Thêm cho DB CŨ.
        EnsureColumn(conn, "orders", "prepared_at", "TEXT");

        // Vị trí shop trong danh sách của subaccount: ghi theo ĐÚNG thứ tự trang /portal/shop trả về (0, 1, 2…)
        // để tab "Shops" hiện shop cùng thứ tự người dùng thấy trên Shopee. NULL = dữ liệu CŨ chưa biết thứ tự
        // (chưa đọc lại shop-list lần nào) → xếp cuối theo tên. Thêm cho DB CŨ.
        EnsureColumn(conn, "account_shops", "sort_order", "INTEGER");

        // MỐC "số yêu cầu trả hàng" của lần check gần nhất, nhớ THEO TỪNG SHOP (bước check đơn trả hàng ở CUỐI
        // flow mỗi shop). NULL = shop chưa từng check. Thêm cho DB CŨ.
        //
        // HAI CỘT, KHÔNG PHẢI TRÙNG LẶP:
        //  · return_count_last — mốc CŨ, đếm trên tab "Tất cả" (bản trước không chọn tab nên lấy nhầm số gộp cả
        //    Đơn Hủy / Đơn Giao hàng không thành công). GIỮ LẠI để chẩn đoán, KHÔNG đọc/ghi nữa.
        //  · return_count_last_tra_hang — mốc ĐANG DÙNG, đếm trên tab "Đơn Trả hàng Hoàn tiền". Cột mới nên bắt
        //    đầu toàn NULL ⇒ mọi shop rơi vào nhánh LanDau và TỰ quét lại một lượt rồi vào nếp — không cần script
        //    reset. So mốc tab "Tất cả" với số của tab mới là so hai đại lượng khác nhau (vd 141 → 12 sẽ ra nhánh
        //    Giam, không check gì mà mốc lại chốt sai), nên bắt buộc phải sang cột riêng.
        EnsureColumn(conn, "account_shops", "return_count_last", "INTEGER");
        EnsureColumn(conn, "account_shops", "return_count_last_tra_hang", "INTEGER");

        // CỜ "CÒN SÓT" của bước check trả hàng, bền THEO SHOP. Vì sao cần: một lượt chạm trần (200 dòng / 10
        // trang) bỏ lại phần đuôi nằm SAU dải-đã-biết; lượt kế chỉ lật trang khi TRANG ĐẦU còn mã mới, mà phần
        // đuôi thì không nằm ở trang đầu ⇒ không lượt nào với tới nữa, tới lúc nó trôi lên thì đã quá cửa sổ 20
        // ngày và bị LocTheoCuaSo bỏ ⇒ mất vĩnh viễn. Cờ bật ⇒ lượt sau chạy "chế độ rút tồn đọng": vẫn lật trang
        // dù trang đầu toàn mã cũ, cho tới khi đọc tới đáy thì tự tắt. Shop cũ nhận 0 = hành vi y hệt trước đây.
        EnsureColumn(conn, "account_shops", "tra_hang_con_sot", "INTEGER NOT NULL DEFAULT 0");

        // LÝ DO còn sót (thêm cùng ngày, đợt trả nợ): 0/1 ở trên trả lời "có sót không", cột này trả lời "vì
        // sao" — 'tran' tự hết khi danh sách vơi, còn 'sap_xep'/'doc_hong' là selector/UI hỏng, cần NGƯỜI xem.
        // Không tách thì shop hỏng sắp xếp giữ cờ 1 vĩnh viễn mà nhật ký không nói được tại sao.
        EnsureColumn(conn, "account_shops", "tra_hang_sot_ly_do", "TEXT NOT NULL DEFAULT ''");

        // MÃ YÊU CẦU TRẢ HÀNG khớp với đơn (đọc ở trang "Trả hàng/Hoàn tiền/Hủy" cuối flow shop) — đẩy lên cột
        // "Đơn trả hàng" của Google Sheet + hub + màn Đơn hàng. NULL = đơn chưa có yêu cầu trả hàng nào.
        // gsheet_da_co_don_tra_hang = lần đẩy sheet gần nhất ĐÃ gửi kèm mã này chưa (0/1; NULL = chưa) — mẫu y
        // hệt gsheet_da_co_van_don, để mã xuất hiện SAU vẫn được đẩy lại điền vào ô. Thêm cho DB CŨ.
        EnsureColumn(conn, "orders", "return_request_code", "TEXT");
        EnsureColumn(conn, "orders", "gsheet_da_co_don_tra_hang", "INTEGER");

        // CẶP CỘT "THẾ HỆ" chống ĐUA giữa lượt đẩy hub đang bay và các chỗ reset cờ `hub_synced_at`:
        //  · hub_push_gen      = thế hệ DỮ LIỆU của đơn — MỌI chỗ reset `hub_synced_at = NULL` (UpsertMany khi
        //    trạng thái/vận đơn/ước tính đổi, MarkPrepared, SetReturnRequestCodes) +1 cột này.
        //  · hub_push_gen_sent = thế hệ đã CHỤP cho lượt đẩy đang bay (GetForHubPush ghi = hub_push_gen; NULL =
        //    chưa chụp lần nào).
        // MarkHubSynced chỉ niêm phong đơn khi hai số CÒN BẰNG NHAU: đơn bị đổi giữa lúc lô đang bay (tunnel có thể
        // hàng chục giây) có gen mới hơn → không bị đóng cờ oan → lượt đẩy sau mang dữ liệu MỚI lên hub. Trước đây
        // `COALESCE(hub_synced_at, $at)` đóng cờ vô điều kiện nên dữ liệu mới KHÔNG BAO GIỜ lên hub (đơn hủy kẹt
        // "Chờ lấy hàng", mã trả bị nuốt) — cùng lớp lỗi "cờ đã đẩy kẹt" đã sửa ở v1.6.3.
        // Đơn CŨ nhận mặc định 0/NULL: gen_sent NULL nghĩa là "chưa chụp" nên lượt mark đầu tiên sau khi nâng cấp
        // không đóng cờ; GetForHubPush ngay trước đó đã chụp nên thực tế không có đơn nào kẹt.
        EnsureColumn(conn, "orders", "hub_push_gen", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "orders", "hub_push_gen_sent", "INTEGER");

        // CÙNG BỆNH, PHÍA GOOGLE SHEET: `gsheet_push_gen` = thế hệ dữ liệu của đơn cho đường ghi sheet. Chốt thế
        // hệ ở đây bảo vệ CẢ NHÓM cờ gsheet (`gsheet_synced_at` + 4 cờ `gsheet_da_*`), nên MỌI đường ghi mở BẤT KỲ
        // cờ nào trong nhóm đều phải +1 cột này: nút "Đẩy lại" (`DatLaiCoDayLai`) và `SetReturnRequestCodes` (mã
        // trả hàng đổi → mở `gsheet_da_co_don_tra_hang`). Còn
        // `MarkGsheetSynced` chỉ đóng cờ khi thế hệ CÒN BẰNG số đã đọc ra lúc dựng lô. Không có lớp bảo vệ này
        // thì lượt đẩy sheet đang bay (chu kỳ 2 phút, mỗi lượt vài giây) đóng lại đúng bộ cờ mà cú bấm "Đẩy lại"
        // vừa mở ⇒ thao tác của người dùng bị NUỐT IM LẶNG trong khi màn hình đã báo "đã xếp vào hàng chờ".
        // KHÁC phía hub một chi tiết: hub cần CẶP cột vì `MarkHubSynced` chỉ nhận danh sách order_sn, còn ở đây
        // thế hệ đọc được đi theo từng đơn trong `GsheetPendingOrder.GsheetPushGen` tới tận lúc đóng cờ ⇒ một cột
        // là đủ, không cần cột "_sent" (và không phải GHI trong lúc ĐỌC).
        EnsureColumn(conn, "orders", "gsheet_push_gen", "INTEGER NOT NULL DEFAULT 0");

        // Banner lỗi địa chỉ đồng bộ đa máy KHÔNG so mốc thời gian (đồng hồ mỗi máy một khác), mà theo:
        //  · hub_rev = số hiệu bản ghi của Hub mà máy này ĐÃ NHẬN. Hub lớn hơn → nghe Hub.
        //  · cho_day = có thay đổi tại chỗ (phát hiện lỗi / bấm X) CHƯA đẩy được lên Hub → Hub KHÔNG được đè,
        //    và mỗi lượt sync phải thử đẩy lại. Đây là chỗ vá lỗ "Hub chết lúc phát hiện lỗi thì mất cảnh báo".
        // Dòng CŨ nhận 0/0: hub_rev=0 nên lượt ghi kế trên Hub (rev≥1) chắc chắn lớn hơn → vẫn nhận được.
        EnsureColumn(conn, "pickup_address_alerts", "hub_rev", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "pickup_address_alerts", "cho_day", "INTEGER NOT NULL DEFAULT 0");

        // Backfill MỘT LẦN: đơn ĐÃ lên hub TRƯỚC bản này có thể thiếu "Số tiền cuối cùng" trên hub — bản cũ chỉ
        // RESET hub_synced_at khi mã vận đơn vừa xuất hiện, KHÔNG reset khi final_amount vừa lấy được, nên đơn
        // lên hub ở lượt sync đầu (chưa mở trang chi tiết) hiển thị "—" VĨNH VIỄN dù local đã có số. Đẩy lại các
        // đơn ĐÃ có final_amount để hub cập nhật (UpsertOrders bên hub idempotent → chỉ ghi đè cùng dữ liệu).
        BackfillHubFinalAmountOnce(conn);
    }

    /// <summary>Khoá "đã chạy" của <see cref="BackfillCfgUpdatedAtOnce"/> trong bảng <c>settings</c>.</summary>
    private const string BackfillKmCfgUpdatedAtKey = "backfill_km_cfg_updated_at_v1";

    /// <summary>
    /// MỐC QUÁ KHỨ CỐ ĐỊNH đóng dấu cho dòng <c>km_config</c> cũ chưa từng có <c>cfg_updated_at</c>.
    /// <para><b>Cố định, KHÔNG phải <c>DateTime.Now</c>.</b> Dùng <c>Now</c> thì hai máy migrate ở hai thời
    /// điểm khác nhau lại đẻ ra đúng cuộc đua mà lượt backfill này sinh ra để chấm dứt — máy nào mở app sau
    /// tự nhiên "sửa cấu hình sau" và nuốt bản của máy kia.</para>
    /// <para><b>Giờ ĐỊA PHƯƠNG, <c>Kind = Unspecified</c></b> — y quy ước của mọi mốc trong bảng này (đổi sang
    /// UTC là việc của đúng hai cửa đẩy/kéo trong <c>KmMocThoiGian</c>, không mở cửa thứ ba ở đây).</para>
    /// </summary>
    internal const string MocBackfillKmCfg = "2026-01-01T00:00:00.0000000";

    /// <summary>
    /// Đóng dấu <see cref="MocBackfillKmCfg"/> cho các dòng <c>km_config</c> ĐANG CÓ mà <c>cfg_updated_at</c>
    /// còn NULL — <c>ALTER TABLE ADD COLUMN</c> ở trên chỉ thêm cột, KHÔNG backfill.
    /// <para><b>Vì sao thiếu bước này là hỏng:</b> <c>cfg_updated_at</c> là trọng tài của luật gộp xuyên máy
    /// (<c>KmHoSoGop</c>), và <c>null</c> nghĩa là "thua mọi giá trị". Dòng cũ mang cấu hình THẬT mà mốc
    /// <c>null</c> thì nó ngang hàng với hồ sơ RỖNG của một máy chưa ai mở form: máy chưa mở form kéo hồ sơ
    /// hub về, thấy <c>null</c> so <c>null</c> ⇒ giữ bản mặc định của mình ⇒ <b>không bao giờ nhận được cấu
    /// hình thật</b>, mà chẳng có lỗi nào. Đóng dấu một mốc quá khứ thì cấu hình thật THẮNG hồ sơ rỗng, nhưng
    /// vẫn THUA mọi lượt người dùng sửa từ nay về sau.</para>
    /// <para><b>CHẠY ĐÚNG MỘT LẦN</b> — chốt bằng khoá trong bảng <c>settings</c>, đúng khuôn
    /// <see cref="BackfillHubFinalAmountOnce"/>. Chạy mỗi lần khởi động thì một dòng ghi SAU này với mốc
    /// <c>null</c> (ví dụ <c>ChotLuotKm</c> trên tài khoản chưa ai mở form) cũng bị đóng dấu quá khứ ké — sai
    /// phạm vi: backfill chỉ dành cho dòng đã tồn tại lúc nâng cấp.</para>
    /// </summary>
    private static void BackfillCfgUpdatedAtOnce(SqliteConnection conn)
    {
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT 1 FROM settings WHERE key = $k;";
            check.Parameters.AddWithValue("$k", BackfillKmCfgUpdatedAtKey);
            if (check.ExecuteScalar() is not null)
            {
                return;
            }
        }

        using (var upd = conn.CreateCommand())
        {
            upd.CommandText = "UPDATE km_config SET cfg_updated_at = $moc WHERE cfg_updated_at IS NULL;";
            upd.Parameters.AddWithValue("$moc", MocBackfillKmCfg);
            upd.ExecuteNonQuery();
        }

        using var mark = conn.CreateCommand();
        mark.CommandText = "INSERT INTO settings(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v;";
        mark.Parameters.AddWithValue("$k", BackfillKmCfgUpdatedAtKey);
        mark.Parameters.AddWithValue("$v", DateTime.UtcNow.ToString("O"));
        mark.ExecuteNonQuery();
    }

    /// <summary>Khoá "đã chạy" của <see cref="BackfillHubFinalAmountOnce"/> trong bảng <c>settings</c>.</summary>
    private const string BackfillHubFinalAmountKey = "backfill_hub_final_amount_v1";

    /// <summary>
    /// Xoá cờ <c>hub_synced_at</c> của các đơn đã có <c>final_amount</c> để lượt đẩy hub kế đẩy LẠI (bổ sung
    /// "Số tiền cuối cùng" cho đơn đã nằm trên hub mà thiếu số). CHẠY ĐÚNG MỘT LẦN — chốt bằng khoá trong bảng
    /// <c>settings</c>; nếu chạy mỗi lần khởi động thì đơn sẽ bị đẩy lại vô hạn.
    /// </summary>
    private static void BackfillHubFinalAmountOnce(SqliteConnection conn)
    {
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT 1 FROM settings WHERE key = $k;";
            check.Parameters.AddWithValue("$k", BackfillHubFinalAmountKey);
            if (check.ExecuteScalar() is not null)
            {
                return;
            }
        }

        using (var upd = conn.CreateCommand())
        {
            // +1 hub_push_gen như MỌI chỗ reset cờ khác (xem cặp cột "thế hệ" ở Initialize): lượt đẩy nào đang bay
            // lúc app khởi động lại cũng không được đóng cờ đè lên đợt backfill này.
            upd.CommandText = @"UPDATE orders SET hub_synced_at = NULL, hub_push_gen = hub_push_gen + 1
WHERE final_amount IS NOT NULL AND hub_synced_at IS NOT NULL;";
            upd.ExecuteNonQuery();
        }

        using var mark = conn.CreateCommand();
        mark.CommandText = "INSERT INTO settings(key, value) VALUES($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v;";
        mark.Parameters.AddWithValue("$k", BackfillHubFinalAmountKey);
        mark.Parameters.AddWithValue("$v", DateTime.UtcNow.ToString("O"));
        mark.ExecuteNonQuery();
    }

    /// <summary>
    /// Đảm bảo bảng <paramref name="table"/> có cột <paramref name="column"/>. Nếu chưa có (DB cũ) thì
    /// <c>ALTER TABLE ... ADD COLUMN</c> — chỉ THÊM cột, KHÔNG đụng dữ liệu sẵn có. Idempotent: chạy
    /// nhiều lần không lỗi (đã có cột thì bỏ qua). Tên bảng/cột/kiểu là hằng nội bộ nên nội suy an toàn.
    /// </summary>
    private static void EnsureColumn(SqliteConnection conn, string table, string column, string columnType)
    {
        // Đọc danh sách cột hiện có; cột "name" nằm ở chỉ số 1 của PRAGMA table_info.
        var exists = false;
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = $"PRAGMA table_info({table});";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
        {
            return;
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnType};";
        alter.ExecuteNonQuery();
    }

    /// <summary>
    /// Backfill cột <c>orders.gsheet_tab</c> cho đơn ĐÃ ghi sheet trước bản "tab theo tháng": tab đích của
    /// chúng là tên tab CŨ trong setting <c>gsheet_tab_name</c> (chuỗi-trắng/thiếu → mặc định LEGACY
    /// <see cref="SettingsRepository.DefaultGsheetTabName"/> "tháng 4"). Chỉ chạm đơn có
    /// <c>gsheet_synced_at NOT NULL</c> và <c>gsheet_tab IS NULL</c> ⇒ idempotent (lần 2 không đổi; đơn chưa
    /// đẩy giữ NULL). <c>NULLIF(TRIM(...),'')</c> để setting toàn khoảng trắng không thành tab sai.
    /// </summary>
    private static void BackfillGsheetTab(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"UPDATE orders SET gsheet_tab = COALESCE(
    NULLIF(TRIM((SELECT value FROM settings WHERE key = 'gsheet_tab_name')), ''), 'tháng 4')
WHERE gsheet_synced_at IS NOT NULL AND gsheet_tab IS NULL;";
        cmd.ExecuteNonQuery();
    }
}
