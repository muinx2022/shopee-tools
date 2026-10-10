using CommunityToolkit.Mvvm.ComponentModel;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.App.ViewModels;

/// <summary>
/// Một dòng lưới tab "Shops": tên Shop (hiển thị) + số đơn đã Chuẩn bị hàng của ngày đang lọc + ba cột số sản
/// phẩm của chương trình khuyến mãi (KM · Combo · Flash Sale, cũng theo ngày đang lọc). Tick xong nằm
/// <b>trong từng ô action</b> (không còn một dấu V đầu hàng cho cả dòng).
/// <para>
/// Là LỚP quan sát được (không còn <c>record</c> bất biến) vì các cờ tiến độ đổi TẠI CHỖ trong lúc chạy — dựng
/// lại dòng mỗi lần đổi cờ sẽ làm lưới nháy. Kéo theo: hết value-equality, so sánh dòng là so THAM CHIẾU.
/// </para>
/// </summary>
public sealed partial class ShopPrepareRow : ObservableObject
{
    /// <param name="shopName">Tên hiển thị ở cột "Shop" (<c>account_shops.shop_name</c>, thiếu thì chính login).</param>
    /// <param name="shopLogin">KHÓA shop (<c>account_shops.shop_login</c> = khóa <c>prepare_daily</c>) — dùng để khớp
    /// nhãn shop mà phiên báo về; KHÁC <paramref name="shopName"/> khi shop có tên hiển thị riêng.</param>
    /// <param name="preparedCount">Số đơn đã Chuẩn bị hàng trong ngày đang lọc.</param>
    /// <param name="kmSoSanPham">Số sản phẩm nạp vào chương trình "KM của Shop" trong ngày đang lọc —
    /// <c>null</c> = hôm đó KHÔNG chạy loại này (xem <see cref="KmSoSanPham"/>).</param>
    /// <param name="comboSoSanPham">Như trên, cho "Combo Khuyến Mãi".</param>
    /// <param name="flashSoSanPham">Như trên, cho "Flash Sale Của Shop".</param>
    public ShopPrepareRow(string shopName, string shopLogin, int preparedCount,
        int? kmSoSanPham = null, int? comboSoSanPham = null, int? flashSoSanPham = null)
    {
        ShopName = shopName;
        ShopLogin = shopLogin;
        PreparedCount = preparedCount;
        KmSoSanPham = kmSoSanPham;
        ComboSoSanPham = comboSoSanPham;
        FlashSoSanPham = flashSoSanPham;
    }

    /// <summary>Tên shop hiển thị ở cột "Shop" (không đổi trong đời dòng).</summary>
    public string ShopName { get; }

    /// <summary>Khóa shop dùng khớp với nhãn phiên báo về (không đổi trong đời dòng).</summary>
    public string ShopLogin { get; }

    /// <summary>Số đơn đã Chuẩn bị hàng của ngày đang lọc.</summary>
    [ObservableProperty]
    private int _preparedCount;

    /// <summary>Shop này đã được kiểm tra XONG trong lượt chạy check đơn hiện tại (kể cả lỗi/bỏ qua) —
    /// nguồn tick ô <b>Chuẩn bị hàng</b>. Xóa sạch khi lượt check đơn mới bắt đầu (phiên đọc lại danh sách shop
    /// với <c>luotChayMoi</c>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTick))]
    private bool _daKiemTra;

    /// <summary>Đang check chính shop này (chữ "đang kiểm tra…" thay cho số + tick ô Chuẩn bị hàng).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTick))]
    private bool _isChecking;

    /// <summary>Shop đang có banner lỗi địa chỉ chưa đóng (X) — hiện dấu X đỏ ở cột hẹp trước tên shop.
    /// Bền tới khi user bấm Đóng trên banner (không theo vòng chạy). Không tranh chỗ với tick ô Chuẩn bị hàng.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoiDiaChi))]
    private bool _coLoiDiaChi;

    /// <summary>Hiện dấu X đỏ lỗi địa chỉ ở cột đầu — độc lập với đang check / tick ô Chuẩn bị hàng
    /// (hai chỗ khác nhau, không còn chồng icon).</summary>
    public bool ShowLoiDiaChi => CoLoiDiaChi;

    /// <summary>Tick ô <b>Chuẩn bị hàng</b>: đã check xong shop này và không còn đang check.
    /// Lỗi địa chỉ hiện X ở cột đầu, không ẩn tick này.</summary>
    public bool ShowTick => DaKiemTra && !IsChecking;

    // ══════════ Ba cột chương trình khuyến mãi: KM | Combo | Flash Sale (27/08/2026) ══════════

    /// <summary>Chuỗi hiện trong ô khi shop KHÔNG chạy loại chương trình đó trong ngày đang lọc. Một hằng cho cả
    /// ba cột — ba chuỗi rời là ba cột trông khác nhau sau một lần sửa quên chỗ.</summary>
    private const string KhongChay = "—";

    /// <summary>
    /// Số sản phẩm nạp vào chương trình <b>"KM của Shop"</b> của ngày đang lọc.
    /// <para><b><c>int?</c> chứ KHÔNG phải <c>int</c>:</b> chương trình chạy vài ngày một lần nên phần lớn ngày
    /// ô này rỗng. <c>null</c> = hôm đó KHÔNG chạy loại này (hiện <c>—</c>); <c>0</c> = ĐÃ chạy shop này nhưng
    /// không tạo được sản phẩm nào (bỏ qua vì modal rỗng / shop hỏng). Hiện <c>0</c> cho một ngày không hề chạy
    /// là phần mềm nói sai.</para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KmHienThi))]
    [NotifyPropertyChangedFor(nameof(ShowTickKm))]
    private int? _kmSoSanPham;

    /// <summary>Số sản phẩm nạp vào <b>"Combo Khuyến Mãi"</b> của ngày đang lọc — cùng quy ước <c>null</c>/<c>0</c>
    /// với <see cref="KmSoSanPham"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ComboHienThi))]
    [NotifyPropertyChangedFor(nameof(ShowTickCombo))]
    private int? _comboSoSanPham;

    /// <summary>Số sản phẩm nạp vào <b>"Flash Sale Của Shop"</b> của ngày đang lọc — cùng quy ước
    /// <c>null</c>/<c>0</c> với hai cột kia.
    /// <para><b>Từ 28/08/2026 ô này ra SỐ THẬT</b>: luồng đã chạy (<c>flow-flashsale.js/chayFlashSale</c>) và
    /// vòng ghi số qua <c>OrdersBridgeSession.ChayVongTaoChuongTrinhAsync</c> → <c>onShopChuongTrinh</c> →
    /// <c>km_program_daily</c>, y hệt hai vòng kia. Con số là TỔNG sản phẩm đã nạp qua các khung của đêm đó
    /// (mỗi khung ~10 sp), nên nó lớn hơn hẳn hai cột kia — đó là bình thường.</para></summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FlashHienThi))]
    [NotifyPropertyChangedFor(nameof(ShowTickFlash))]
    private int? _flashSoSanPham;

    /// <summary>
    /// Loại chương trình mà phiên ĐANG chạy trên chính shop này — <c>null</c> = dòng này không chạy chương trình
    /// nào.
    /// <para><b>MỘT trường chứ không ba cờ bool rời:</b> ba cờ biểu diễn được trạng thái "đang chạy KM và Combo
    /// cùng lúc trên một shop" — thứ không tồn tại; mà một khi biểu diễn được thì sẽ có đường code tạo ra nó.</para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KmDangChay))]
    [NotifyPropertyChangedFor(nameof(ComboDangChay))]
    [NotifyPropertyChangedFor(nameof(FlashDangChay))]
    [NotifyPropertyChangedFor(nameof(ShowTickKm))]
    [NotifyPropertyChangedFor(nameof(ShowTickCombo))]
    [NotifyPropertyChangedFor(nameof(ShowTickFlash))]
    private LoaiChuongTrinh? _loaiDangChay;

    /// <summary>Ô cột KM đang hiện chữ "đang chạy…" thay cho số.</summary>
    public bool KmDangChay => LoaiDangChay == LoaiChuongTrinh.ChuongTrinhShop;

    /// <summary>Ô cột Combo đang hiện chữ "đang chạy…" thay cho số.</summary>
    public bool ComboDangChay => LoaiDangChay == LoaiChuongTrinh.Combo;

    /// <summary>Ô cột Flash Sale đang hiện chữ "đang chạy…" thay cho số.</summary>
    public bool FlashDangChay => LoaiDangChay == LoaiChuongTrinh.FlashSale;

    /// <summary>Chuỗi hiện ở ô cột KM: số, hoặc <c>—</c> khi hôm đó không chạy loại này.</summary>
    public string KmHienThi => KmSoSanPham?.ToString() ?? KhongChay;

    /// <summary>Chuỗi hiện ở ô cột Combo — xem <see cref="KmHienThi"/>.</summary>
    public string ComboHienThi => ComboSoSanPham?.ToString() ?? KhongChay;

    /// <summary>Chuỗi hiện ở ô cột Flash Sale — xem <see cref="KmHienThi"/>.</summary>
    public string FlashHienThi => FlashSoSanPham?.ToString() ?? KhongChay;

    /// <summary>Tick ô KM: ngày lọc đã có dòng DB (kể cả 0 sp) và ô không đang chạy.</summary>
    public bool ShowTickKm => KmSoSanPham.HasValue && !KmDangChay;

    /// <summary>Tick ô Combo — cùng quy ước <see cref="ShowTickKm"/>.</summary>
    public bool ShowTickCombo => ComboSoSanPham.HasValue && !ComboDangChay;

    /// <summary>Tick ô Flash Sale — cùng quy ước <see cref="ShowTickKm"/>.</summary>
    public bool ShowTickFlash => FlashSoSanPham.HasValue && !FlashDangChay;
}
