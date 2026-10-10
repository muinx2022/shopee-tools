using System.Globalization;
using System.Linq;
using XuLyDonShopee.Core.Data;

namespace XuLyDonShopee.Core.Services;

/// <summary>
/// <b>Ba loại chương trình khuyến mãi của "KM của Shop"</b> — mỗi loại một KHUNG GIỜ riêng trong đêm
/// (<see cref="KmLichTuDong.Khung"/>) và một CẶP MỐC CHẠY riêng trong <c>km_config</c>.
/// <para>Giá trị số CỐ ĐỊNH: enum này không đi xuống DB hay lên hub (mỗi loại có cột riêng), nhưng đổi thứ tự
/// vẫn làm mọi mảng/ma trận test đọc sai — không có lý do gì để đổi.</para>
/// </summary>
public enum LoaiChuongTrinh
{
    /// <summary>Chương Trình Của Shop — luồng đầu tiên, khung 02–04h. Xem <c>flow-km.js</c>.</summary>
    ChuongTrinhShop = 0,

    /// <summary>Combo Khuyến Mãi — khung 06–08h (25/08/2026).</summary>
    Combo = 1,

    /// <summary>Flash Sale Của Shop — <b>khung CẢ NGÀY <c>[0,23)</c>, chạy thành nhiều GÓI NHỎ</b> (người dùng
    /// chốt 01/09/2026). Luồng đã chạy được (<c>flow-flashsale.js/chayFlashSale</c>); công tắc là
    /// <c>km_config.flash_bat</c>, mặc định TẮT.
    /// <para>Khác hai vòng kia ở BỐN chỗ: chu kỳ CỨNG 1 ngày (<see cref="KmLichTuDong.SoNgayChuKy"/>), hạn bắt
    /// đầu shop mới là <b>22:15</b> chứ không phải giờ đóng khung, không có thông số nào để cấu hình, và
    /// <b>không đòi khung riêng</b> — mỗi lượt là một GÓI chiếm KHE NGHỈ giữa hai vòng check đơn
    /// (<see cref="KmLichTuDong.DenGioGoiFlash"/>), nên nó KHÔNG nằm trong <see cref="KmLichTuDong.KhungCuaBienAn"/>.</para></summary>
    FlashSale = 2,
}

/// <summary>
/// <b>Lịch chạy tự động của "KM của Shop"</b> — TOÀN BỘ là hàm thuần, không đụng đồng hồ hệ thống, không đụng
/// trình duyệt: mọi hàm nhận <c>bayGio</c> từ ngoài.
/// <para><b>BA KHUNG, KHÔNG PHẢI MỘT</b> (25/08/2026): mỗi <see cref="LoaiChuongTrinh"/> chạy trong khung giờ
/// riêng của nó — xem bảng ở <see cref="Khung"/>. Mọi luật nhận thêm tham số <c>loai</c>, và hai mốc chạy được
/// TÁCH THEO LOẠI trong <c>km_config</c> (xem <see cref="MocThuCua"/> cho ca hỏng nếu gộp).</para>
/// <para><b>Vì sao bắt buộc thuần:</b> luật ở đây là "chạy trong khung xx–yyh, n ngày một lần". Không có cách
/// nào kiểm bằng tay ngoài việc thức tới 2h sáng và chờ; mà một lỗi lệch khung giờ thì hoặc là chương trình
/// không bao giờ chạy, hoặc là nó chạy đè lên vòng check đơn. Nhận <c>bayGio</c> làm tham số là cách duy nhất
/// để test được cả hai ca đó trong một phần nghìn giây.</para>
/// </summary>
public static class KmLichTuDong
{
    /// <summary>
    /// <b>Giờ MỞ khung của "Chương Trình Của Shop"</b> (1h sáng). Bằng <c>Khung(LoaiChuongTrinh.ChuongTrinhShop).Mo</c>.
    /// </summary>
    public const int GioMoKhung = 1;

    /// <summary>
    /// <b>Giờ ĐÓNG khung của "Chương Trình Của Shop"</b> — mốc này KHÔNG thuộc khung (4h sáng).
    /// </summary>
    public const int GioDongKhung = 4;

    /// <summary>Giờ MỞ / ĐÓNG khung "Combo Khuyến Mãi" — 01–04h (chạy chung khung đêm với KM của Shop).</summary>
    public const int GioMoKhungCombo = 1;
    public const int GioDongKhungCombo = 4;

    /// <summary>
    /// Giờ MỞ / ĐÓNG khung <b>"Flash Sale Của Shop"</b> — <b>CẢ NGÀY, <c>[0,23)</c></b> (người dùng chốt
    /// 01/09/2026, plan <c>2026-09-01-flash-ca-ngay-xen-khe-nghi.md</c> §1).
    ///
    /// <para><b>ĐỔI TỪ 21–24h</b>. Khung tối cũ dựng trên một giả định mà người dùng đã BÁC BỎ bằng bằng chứng:
    /// <i>"chạy ban ngày thì Shopee chưa chắc đã mở khung của ngày mai"</i> — sai, lượt 19:03 ngày 31/08 tạo
    /// được bình thường và ngày 30/08 còn thấy cả draft cho <c>D+2</c>. Vì lượt này đặt chương trình cho NGÀY
    /// HÔM SAU nên <b>mọi giờ trong ngày đều là giờ hợp lệ</b>, và một cửa sổ 3 tiếng thì không đủ chỗ cho 12
    /// shop × ~40 phút (sổ <c>P-20260901-1</c>).</para>
    ///
    /// <para><b>Giờ ĐÓNG là 23, KHÔNG phải 24</b>: shop cuối được bắt đầu tới
    /// <see cref="PhutHanChotBatDauShopMoiFlash"/> = 22:15, cộng trần ~45 phút một shop
    /// (<see cref="PhutChotShopMoiTruocCuoiKhe"/>) ⇒ lượt xong quanh 23:00. Để 24 thì
    /// <see cref="ConDuocBatDauShopMoi"/> và <see cref="TrongKhung"/> nói hai chuyện khác nhau suốt 1h45 cuối
    /// ngày mà chẳng ai dùng khoảng đó. <b>Giữ kiến trúc int-GIỜ</b> — đừng rework <see cref="Khung"/> sang phút
    /// chỉ để biểu diễn 22:15; hạn shop mới vốn đã là một hằng PHÚT riêng.</para>
    ///
    /// <para><b>⚠ Khung này KHÔNG đòi check đơn nhường giờ</b> — xem <see cref="KhungCuaBienAn"/>. Cả ngày là
    /// khung, nên coi nó như biên an là dời vòng check đơn đi vô hạn.</para>
    /// </summary>
    public const int GioMoKhungFlashSale = 0;
    public const int GioDongKhungFlashSale = 23;

    /// <summary>
    /// <b>Phút trong ngày mà lượt Flash Sale THÔI BẮT ĐẦU SHOP MỚI</b> — <c>22:15</c> (người dùng chốt
    /// 01/09/2026; trước đó là 23:45).
    /// <para><b>Vì sao HẠ xuống</b>: Shopee đóng đăng ký cho ngày <c>D+1</c> quanh <b>~22:50</b> — đo thật đêm
    /// 31/08/2026, lượt 23:20 mở ra thì lịch đã HẾT ĐỎ. Bắt đầu một shop sau 22:15 là chạy vào khoảng Shopee sắp
    /// đóng cửa: 8 khung × ~4,5 phút ≈ 36 phút, tức nửa sau của shop rơi vào vùng chết. Đệm 35 phút.</para>
    /// <para>Chỉ chặn shop MỚI, KHÔNG cắt ngang shop đang chạy dở — xem <see cref="ConDuocBatDauShopMoi"/>.</para>
    /// <para><b>Cần CẢ HAI chốt, đừng bỏ chốt nào:</b> chốt này chặn shop mới, còn chốt "ngày đích tính MỘT LẦN
    /// lúc bắt đầu lượt" (<see cref="NgayDichFlash"/>) mới là thứ cứu shop đang chạy dở khi nó vắt qua 00:00.
    /// Với hạn 22:15 + trần 45 phút một shop thì không gói nào vắt qua nửa đêm được nữa.</para>
    /// </summary>
    public const int PhutHanChotBatDauShopMoiFlash = 22 * 60 + 15;

    /// <summary>
    /// <b>GIÃN CÁCH tối thiểu giữa hai lần MỞ GÓI flash</b>, tính bằng PHÚT — <c>30</c>.
    /// <para>Thay vai trò của chốt "một lượt thử mỗi khung" cũ (<see cref="DaThuTrongKhungNay"/>), thứ đã chết
    /// theo khung 21–24h: khung nay là cả ngày nên "một lượt mỗi khung" sẽ thành một lượt mỗi NGÀY.</para>
    /// <para><b>Cái nó chặn</b>: gói hỏng NHANH (SSO đỏ, hub từ chối, shop đầu ném) không ghi mốc chạy cuối, nên
    /// không có hằng này thì nhịp đồng hồ 60 giây mở gói mới MỖI PHÚT — đúng cái profile phóng Brave + SSO liên
    /// tục làm Shopee bật <c>verify/traffic</c>. Đo theo <c>cf.FlashLanThuTuDong</c> (mốc THỬ, ghi trước khi
    /// chạy), không phải mốc chạy cuối.</para>
    /// </summary>
    public const int PhutGianCachGoiFlash = 30;

    /// <summary>
    /// <b>Không bắt đầu shop MỚI khi còn ít hơn ngần này PHÚT tới cuối khe nghỉ</b> — <c>45</c>.
    /// <para>Đo thật 31/08/2026 với form mới: một shop đủ 8 khung mất 32–36 phút (~4,5 phút/khung), cộng mở shop
    /// và nghỉ ⇒ lấy trần 45. Khớp với hạn chặng <c>OrdersBridgeChannel.ChoChang.FlashChay</c> = 2700s.</para>
    /// <para>Đây là vế làm cho gói ĐÀN HỒI: hết một shop thì nhìn đồng hồ, khe còn ≥45 phút thì làm shop kế. Với
    /// khe nghỉ 120 phút tự ra ~2 shop mỗi gói mà không cần đếm shop.</para>
    /// </summary>
    public const int PhutChotShopMoiTruocCuoiKhe = 45;

    /// <summary>
    /// <b>KHE NGHỈ tối thiểu để đáng mở một gói flash</b>, tính bằng PHÚT — <c>75</c> (nâng từ 60 ngày
    /// 01/09/2026, phản biện vòng 1).
    /// <para><b>Vì sao 60 là quá sát:</b> khe 60 phút chỉ chừa <b>15 phút RUNWAY</b> — hạn shop mới của gói là
    /// <c>cuối khe − <see cref="PhutChotShopMoiTruocCuoiKhe"/></c> = cuối khe − 45', nên gói phải phóng Brave,
    /// qua SSO và về tới trang chọn shop trong 15 phút thì mới kịp bắt đầu lấy một shop. SSO chậm (mã xác minh
    /// email, Shopee đáp lâu) là gói tốn trọn một lượt phóng Brave cho 0 shop. Sàn 75 nâng runway lên 30 phút,
    /// đúng ngưỡng đã đo được của một lượt mở + SSO chậm.</para>
    /// <para>Con số này cũng chính là thứ tự loại kỳ nghỉ NGẮN 15 phút sau một vòng check đơn bị cắt
    /// (<c>AccountSession.NghiSauVong</c>): kỳ nghỉ đó sinh ra để hạ tầng dịu lại, không phải chỗ chen việc.</para>
    /// <para><b>⚠ Ràng buộc hai chiều với chu kỳ check đơn</b>: hạ
    /// <c>AppGeneralSettings.DefaultOrderIntervalMinutes</c> xuống dưới mốc này là vòng flash chết câm — có test
    /// riêng khoá (<c>AppGeneralSettingsTests</c>).</para>
    /// </summary>
    public const int PhutKheToiThieuChoGoi = 75;

    /// <summary>
    /// <b>TRẦN thời gian một lượt HOÃN chạy lại check đơn được NẰM CHỜ gói flash / lát cắt cầu nối đang chạy</b>
    /// — <c>2 ×</c> hạn chặng một shop flash (<c>OrdersBridgeChannel.ChoChang.FlashChay</c> = 2700s) = 90 phút.
    ///
    /// <para><b>Vì sao 2× chứ không 1×</b> (03/09/2026, sổ P-20260903-1): lượt hoãn tỉnh dậy có thể rơi vào GIỮA
    /// một shop, nên nó phải chờ hết shop đó (≤ 1× hạn chặng) rồi còn phần dọn dẹp của gói — đóng cửa sổ Brave,
    /// nhả cổng 47821, nhả khoá xuyên máy. 1× là hết trần đúng lúc gói sắp xong.</para>
    ///
    /// <para><b>Quá trần thì KHÔNG mở check đơn</b> — chủ ý, không phải bỏ sót: thà thiếu check đơn một khe còn
    /// hơn đè lên gói đang chạy (đúng ca đã đo 03/09: mở đè ⇒ <c>HttpListenerException</c> cổng 47821 ⇒ lượt dọn
    /// hồ sơ giết Brave của gói ⇒ shop đang dở HỎNG). Dòng nhật ký ở đó phải nói rõ để người dùng biết bấm ▶.</para>
    ///
    /// <para><b>Ở đây, không ở tầng App</b>: <c>OrdersBridgeChannel.ChoChang</c> là <c>internal</c> của
    /// <c>XuLyDonShopee.Core</c> nên <c>AccountsViewModel</c> không đọc tới được; mà viết lại "90 phút" bằng tay
    /// ở tầng kia là hai hằng cho một khái niệm, trôi lệch ngay lần nới hạn chặng kế tiếp. Mọi hằng thời gian
    /// khác của vòng flash cũng đã nằm ở lớp này.</para>
    /// </summary>
    public static readonly TimeSpan TranChoNhuongFlash = OrdersBridgeChannel.ChoChang.FlashChay * 2;

    /// <summary>
    /// <b>TRẦN số lần MỞ GÓI flash trong một ngày, TÍNH THEO TỪNG TÀI KHOẢN</b> — <c>10</c>.
    /// <para><b>Mỗi TÀI KHOẢN mỗi ngày</b>, không phải mỗi máy (sửa 01/09/2026, phản biện vòng 1): sổ chung cho
    /// cả máy thì tài khoản A tiêu hết trần rồi tài khoản B không còn gói nào — bỏ đói câm lặng, và nạn nhân là
    /// tài khoản đứng sau trong danh sách nên nó bị đói MỌI ngày chứ không phải ngẫu nhiên.</para>
    /// <para>12 shop cần ~6 gói (khe 120 phút ra ~2 shop/gói); trần 10 chừa đệm cho gói RỖNG — gói hỏng ở SSO,
    /// gói mở ra rồi đụng ngay hạn shop mới — mà vẫn có điểm dừng CỨNG.</para>
    /// <para>Kế thừa vai trò chống "phóng Brave vô hạn" của chốt một-lượt-mỗi-khung cũ. <b>Đếm IN-MEMORY</b>
    /// (<c>AccountsViewModel</c>), app khởi động lại thì về 0 — chấp nhận có chủ đích: xem §0 của plan, tuyệt
    /// đối không đẻ thêm cột tiến độ nào xuống DB/hub.</para>
    /// </summary>
    public const int TranGoiThuMoiNgayFlash = 10;

    /// <summary>
    /// <b>Số giờ BIÊN AN trước mỗi khung</b> — vòng check đơn không được ĐẶT mốc trong <c>[mở − 1h, đóng)</c>.
    /// <para><b>Vì sao 60 phút</b> (plan cũ §5, người dùng chốt lại 19/08/2026): một vòng check đơn chạy
    /// 30–45 phút. Chỉ chặn đúng khung thì một vòng khởi động lúc <c>01:5x</c> vẫn còn đang chạy khi chương
    /// trình tới giờ — và hai luồng tranh CÙNG một hồ sơ Brave, cùng cổng cầu nối 47821, cùng khoá xuyên máy.</para>
    /// </summary>
    public const int SoGioBienAn = 1;

    /// <summary>Giờ mở BIÊN AN của khung "Chương Trình Của Shop" (1h sáng) — giữ tên cũ. Xem <see cref="SoGioBienAn"/>.</summary>
    public const int GioBatDauBienAn = GioMoKhung - SoGioBienAn;

    /// <summary>
    /// <b>Các loại chương trình mà vòng check đơn PHẢI NHƯỜNG KHUNG cho</b> — để nơi cần quét thì quét một mảng
    /// chứ không chép tay mấy nhánh <c>if</c>.
    /// <para><b>⚠ CỐ Ý KHÔNG CÓ <see cref="LoaiChuongTrinh.FlashSale"/></b> (01/09/2026). Từ khi khung flash là
    /// CẢ NGÀY <c>[0,23)</c>, để nó ở đây nghĩa là mọi mốc vòng check đơn đều rơi vào "biên an flash" ⇒
    /// <see cref="HoanVongCheckDon"/> dời mốc tới 23:00, rồi 23:00 lại rơi vào biên an của khung KM hôm sau…
    /// tức <b>check đơn tự dời vòng vô hạn</b>. Và nó KHÔNG cần: vòng flash nay chờ KHE NGHỈ có sẵn của check
    /// đơn (<see cref="DenGioGoiFlash"/>), không đòi ai nhường gì.</para>
    /// </summary>
    internal static readonly LoaiChuongTrinh[] LoaiDoiNhuongKhung =
    [
        LoaiChuongTrinh.ChuongTrinhShop, LoaiChuongTrinh.Combo,
    ];

    /// <summary>
    /// <b>PURE — BẢNG KHUNG GIỜ: mỗi loại chương trình một khung riêng</b> (người dùng chốt 25/08/2026).
    /// <list type="table">
    /// <item><term>ChuongTrinhShop</term><description><c>02–04h</c>, biên an <c>[01, 04)</c></description></item>
    /// <item><term>Combo</term><description><c>06–08h</c>, biên an <c>[05, 08)</c></description></item>
    /// <item><term>FlashSale</term><description><c>00–23h</c> — <b>KHÔNG có biên an</b>, xem
    /// <see cref="LoaiDoiNhuongKhung"/></description></item>
    /// </list>
    /// <para>Khe <c>[04, 05)</c> và <c>[08, 09)</c> là chỗ DUY NHẤT còn lại để check đơn BẮT ĐẦU một vòng giữa
    /// hai khung ĐÊM — mỗi khe đúng một tiếng, vì biên an ăn 60 phút trước khung kế. Nới khung mà không tính lại
    /// chỗ này là bóp chết khe đó.</para>
    /// <para><b>Khung Flash Sale nay TRÙM cả hai khung kia</b> (00–23h) và đó KHÔNG phải xung đột: hai khung đêm
    /// vẫn đòi check đơn nhường giờ như cũ, còn flash thì chỉ chen vào KHE NGHỈ mà check đơn tự để lại — mà
    /// trong khung KM/Combo thì check đơn có chạy đâu để có khe. Cửa chặn thật nằm ở
    /// <c>AccountsViewModel.NhipLichFlashAsync</c> (mọi phiên phải đang nghỉ) và ở cờ <c>_bridgeRunning</c>.</para>
    /// </summary>
    public static (int Mo, int Dong) Khung(LoaiChuongTrinh loai) => loai switch
    {
        LoaiChuongTrinh.ChuongTrinhShop => (GioMoKhung, GioDongKhung),
        LoaiChuongTrinh.Combo => (GioMoKhungCombo, GioDongKhungCombo),
        LoaiChuongTrinh.FlashSale => (GioMoKhungFlashSale, GioDongKhungFlashSale),
        _ => throw new ArgumentOutOfRangeException(nameof(loai), loai, "Loại chương trình không biết."),
    };

    /// <summary>PURE — tên loại chương trình đúng như Shopee gọi, để câu nhật ký nói được người dùng đang chờ
    /// khung nào.</summary>
    public static string TenLoai(LoaiChuongTrinh loai) => loai switch
    {
        LoaiChuongTrinh.ChuongTrinhShop => "Chương Trình Của Shop",
        LoaiChuongTrinh.Combo => "Combo Khuyến Mãi",
        LoaiChuongTrinh.FlashSale => "Flash Sale",
        _ => throw new ArgumentOutOfRangeException(nameof(loai), loai, "Loại chương trình không biết."),
    };

    /// <summary>
    /// <b>ĐỘ DÀI của KHOẢNG chương trình khuyến mãi, tính bằng NGÀY</b> — người dùng đính chính 26/08/2026:
    /// chương trình không phải một lát cắt một tiếng ở ngày +180, mà là một KHOẢNG chạy từ
    /// <see cref="NgayBatDau"/> (hôm nay) tới <see cref="NgayKetThuc"/> (hôm nay + hằng này), <b>cùng một giờ ở
    /// hai đầu</b>. <b>Dùng CHUNG cho cả ba loại</b> — trần 180 ngày là trần của Shopee, không phải của riêng
    /// luồng nào.
    /// <para><b>⚠ 180 ĐÚNG BẰNG TRẦN CỦA SHOPEE</b> ("không quá 180 ngày", <c>plans/2026-08-18-km-cua-shop.md</c>
    /// §3) — tức mốc KẾT THÚC nằm SÁT BIÊN, KHÔNG có lấy một giờ dự phòng. Mốc BẮT ĐẦU thì luôn an toàn (hôm
    /// nay), nên hình dạng hỏng nếu Shopee tính trần theo timestamp là <b>bước 5 ĐỎ, bước 4 xanh</b> — khác bản
    /// 25/08 khi cả hai ô cùng nằm ở ngày +180.</para>
    /// <para><b>Lúc đó lượt hỏng trông như thế nào</b> — hai hình dạng, tuỳ Shopee chặn ở mức ngày hay mức giờ:
    /// ô ngày mang <c>disabled</c> ⇒ <c>pageClickNgayTheoSo</c> báo <c>"không thấy ô ngày 'N'"</c>; hoặc ô ngày
    /// qua được mà <c>li</c> giờ bị chặn ⇒ <c>pageClickLiThoiGian</c> báo <c>"li '09' đang disabled"</c>. Cả hai
    /// đều làm phép ĐỌC LẠI lệch ⇒ <b>bước 5</b> (ô KẾT THÚC) ĐỎ ⇒ shop bị bỏ qua. Không có chương trình sai
    /// nào được tạo — cờ <c>daTao</c> chỉ bật ở bước cuối.</para>
    /// <para><b>⚠ HẬU QUẢ KHÔNG DỪNG Ở "MẤT MỘT ĐÊM"</b> (phản biện 25/08/2026). Lượt tạo được 0 shop thì
    /// <c>ChotLuotKm</c> CỐ Ý không ghi mốc chạy cuối (đúng: đừng đẩy lịch đi xx ngày cho một đêm chẳng tạo
    /// được gì). Nhưng nếu nguyên nhân là trần 180 — một nguyên nhân KHÔNG BAO GIỜ TỰ HẾT — thì đêm nào
    /// <see cref="DenGioChay"/> cũng tới hạn lại: mỗi đêm một vòng SSO + 12 shop, tạo 0 chương trình, mãi mãi.
    /// Đúng profile lưu lượng làm Shopee bật <c>verify/traffic</c>.</para>
    /// <para><b>Cách gỡ: hạ hằng số này xuống mức CÓ BIÊN (170–179), không phải 180.</b> Nó nằm một chỗ chính là
    /// để đổi bằng một dòng. Muốn biết trước khi đốt một đêm: bật <c>SHOPEE_DEV_TOOLS=1</c> rồi bấm nút DÒ trên
    /// MỘT shop — <c>thamSoLuotDo</c> trong <c>flow-km.js</c> nay đi đúng độ trễ này.</para>
    /// </summary>
    public const int SoNgayDatTruoc = 180;

    /// <summary>
    /// <b>PURE — ngày MỞ ĐẦU khoảng chương trình</b> cho một lượt chạy tại <paramref name="bayGio"/>: chính
    /// <c>bayGio.Date</c> (HÔM NAY).
    /// <para><b>⚠ Cặp đôi với <see cref="NgayKetThuc"/>, đừng dùng một mình.</b> Hàm <c>NgayChuongTrinh</c> cũ
    /// (một ngày duy nhất) đã XOÁ HẲN ngày 26/08/2026 — nó mang nghĩa "chương trình diễn ra ngày nào", mà từ nay
    /// chương trình diễn ra trong một KHOẢNG, nên cái tên ấy chỉ còn là lời mời dùng nhầm.</para>
    /// <para><b>Hệ quả phải biết:</b> mốc bắt đầu nay là <i>hôm nay lúc GioBatDau</i> ⇒ nó ĐI QUA TRONG NGÀY. Bấm
    /// tay lúc 13:28 với giờ 11:00 là mốc đã ở quá khứ ⇒ picker Shopee vô hiệu hoá ⇒ bước 4 đỏ. Cửa chặn nằm ở
    /// <c>KmDialogViewModel.KiemTraMocTuongLai</c> và nó từ chối NGAY TRÊN FORM, kèm cách gỡ. Hai lượt tự động
    /// thì vẫn an toàn tự nhiên: KM chạy 02–04h với giờ 09:30, combo chạy 06–08h với giờ 11:00.</para>
    /// <para>Nhận <paramref name="bayGio"/> từ ngoài (không gọi <c>DateTime.Now</c>) theo đúng luật của cả lớp
    /// này — xem phần tóm tắt ở đầu lớp.</para>
    /// </summary>
    public static DateTime NgayBatDau(DateTime bayGio) => bayGio.Date;

    /// <summary>
    /// <b>PURE — ngày ĐÓNG khoảng chương trình</b>: <c>bayGio.Date + <see cref="SoNgayDatTruoc"/></c>.
    /// <para>Là nguồn sự thật DUY NHẤT của cả hai đường (form "KM của Shop" và lượt tự động) và của cả hai vòng
    /// (Chương Trình Của Shop, Combo Khuyến Mãi). Mỗi nơi tự cộng lấy 180 là mấy hằng số sẽ trôi khỏi nhau — mà
    /// lệch nhau ở đây nghĩa là chương trình khuyến mãi THẬT sống sai độ dài, và chỉ lộ ra khi ai đó mở Shopee
    /// lên nhìn.</para>
    /// <para><c>AddDays</c> của .NET tự lo tháng đủ/thiếu và năm nhuận, nên không có bảng ngày nào phải bảo trì
    /// ở đây; test khoá vài ca vắt qua năm và qua 29/02.</para>
    /// </summary>
    public static DateTime NgayKetThuc(DateTime bayGio) => bayGio.Date.AddDays(SoNgayDatTruoc);

    /// <summary>
    /// <b>PURE — đọc chuỗi giờ <c>"HH:mm"</c> thành <see cref="TimeSpan"/></b>; <c>null</c> = sai định dạng.
    /// <para><b>Nguồn sự thật DUY NHẤT của phép đọc giờ</b> — <c>KmDialogViewModel.DocGio</c> gọi thẳng vào đây.
    /// Hai bản chép tay là hai luật "giờ hợp lệ" khác nhau giữa cửa kiểm trên form và cửa kiểm giữa vòng chạy,
    /// mà triệu chứng của lệch ấy là một cấu hình qua được form rồi chết ở giữa vòng.</para>
    /// <para><c>InvariantCulture</c> + khuôn CỨNG <c>hh\:mm</c>: chuỗi này nằm trong <c>km_config</c> và đi
    /// xuống extension nguyên văn, không được phụ thuộc máy nào đang đặt lịch gì.</para>
    /// </summary>
    public static TimeSpan? DocGio(string? s)
        => TimeSpan.TryParseExact(s ?? string.Empty, @"hh\:mm", CultureInfo.InvariantCulture, out var t)
            ? t
            : null;

    /// <summary>
    /// <b>Thời lượng ƯỚC TÍNH một shop chiếm của vòng, tính bằng PHÚT.</b> Gồm kỳ nghỉ giữa hai shop
    /// (<c>OrdersBridgeSession.NghiGiuaShopToiThieu</c>–<c>NghiGiuaShopToiDa</c> = 3–5 phút, lấy cận trên) cộng
    /// phần thao tác thật trong shop (mở Chi tiết → 9 bước DOM → đóng tab).
    /// <para><b>Đây là ước lượng để tính BIÊN AN, không phải hạn chặng.</b> Nó chỉ cần đủ lớn để mốc bắt đầu
    /// không trôi qua giữa vòng; ước thừa thì người dùng bị đẩy giờ chạy ra muộn hơn vài chục phút, ước thiếu
    /// thì nửa sau danh sách shop đỏ hết. Đắt lệch nhau rất xa ⇒ lấy cận trên.</para>
    /// </summary>
    public const int ThoiLuongMoiShopUocTinh = 8;

    /// <summary>SÀN của <see cref="BienAnMocBatDau"/>, tính bằng PHÚT — kể cả vòng 1 shop cũng phải cách mốc bắt
    /// đầu ngần này (SSO + trang chọn shop + một shop chậm đã ăn hết chỗ đó).</summary>
    public const int BienAnMocBatDauToiThieu = 15;

    /// <summary>
    /// <b>PURE — BIÊN AN của MỐC BẮT ĐẦU</b>: mốc <i>ngày bắt đầu + giờ</i> phải cách "bây giờ" ÍT NHẤT ngần
    /// này thì cả vòng mới kịp chạy xong trước khi nó trôi qua.
    /// <para><b>Vì sao cần</b> (phản biện 26/08/2026, lỗi NẶNG): từ khi mốc bắt đầu là <i>HÔM NAY lúc
    /// GioBatDau</i>, nó là một HẠN CHÓT ĐỒNG HỒ — mà cửa kiểm chỉ chạy MỘT LẦN trước vòng. Vòng 12 shop mất
    /// ~1 tiếng, nên một mốc "còn 5 phút nữa" qua được cửa rồi trôi qua ở shop thứ hai, và <b>toàn bộ shop còn
    /// lại đỏ</b> với câu <c>"li '14' đang disabled"</c> — không dòng nào nói nguyên nhân là đồng hồ.</para>
    /// <para><b>Tính theo SỐ SHOP</b> vì đó chính là thứ quyết định vòng dài bao lâu:
    /// <c>soShop × <see cref="ThoiLuongMoiShopUocTinh"/></c>, kẹp sàn
    /// <see cref="BienAnMocBatDauToiThieu"/> phút.</para>
    /// <para>Đây là cửa chặn TRƯỚC vòng. Cửa thứ hai — kiểm lại mốc TRƯỚC MỖI SHOP — nằm trong
    /// <c>OrdersBridgeSession.ChayVongTaoChuongTrinhAsync</c>: ước lượng nào cũng có thể sai, và khi nó sai thì
    /// vòng phải DỪNG với câu nói đúng nguyên nhân chứ không phải để 8 shop tự đỏ.</para>
    /// </summary>
    /// <param name="soShop">Số shop của tài khoản trong lượt này (≤ 0 ⇒ chỉ còn sàn).</param>
    public static TimeSpan BienAnMocBatDau(int soShop)
        => TimeSpan.FromMinutes(Math.Max(BienAnMocBatDauToiThieu, Math.Max(0, soShop) * ThoiLuongMoiShopUocTinh));

    /// <summary>
    /// <b>PURE — SỐ SHOP LƯỢT NÀY THỰC SỰ SẼ CHẠY</b>, sau khi áp trần của giai đoạn thử.
    /// <para>Vòng combo bị <see cref="TranShopLuotCombo"/> cắt sau N shop, nên mọi phép tính "vòng này mất bao
    /// lâu" phải hỏi hàm NÀY, đừng dùng thẳng tổng số shop của tài khoản.</para>
    /// <para><b>Ca thật 26/08/2026 — 16:16.</b> Người dùng bấm Chạy Combo trên tài khoản 12 shop trong khi trần
    /// thử là 2. Cửa kiểm tính biên an theo 12 shop ⇒ đòi <b>96 phút</b> và khuyên "từ 18:00 trở đi — đủ cho
    /// một vòng 12 shop", trong khi lượt ấy chỉ chạy 2 shop và cần ~16 phút. Cửa kiểm nói một con số THẬT
    /// nhưng của một vòng KHÔNG BAO GIỜ xảy ra ⇒ chặn oan gần một tiếng rưỡi.</para>
    /// <para>Vòng Chương Trình Của Shop không có trần ⇒ trả nguyên <paramref name="soShop"/>. Vòng Flash Sale
    /// có trần riêng <see cref="TranShopLuotFlash"/>.</para>
    /// </summary>
    public static int SoShopSeChay(int soShop, LoaiChuongTrinh loai)
    {
        var tong = Math.Max(0, soShop);
        var tran = TranShopCua(loai);
        return tran > 0 ? Math.Min(tong, tran) : tong;
    }

    /// <summary>
    /// <b>PURE — TRẦN SỐ SHOP mỗi lượt của ĐÚNG loại đó</b> (<c>0</c> = không giới hạn). Cửa DUY NHẤT tra trần,
    /// để <see cref="SoShopSeChay"/> và vòng chạy không đọc hai hằng khác nhau.
    /// </summary>
    public static int TranShopCua(LoaiChuongTrinh loai) => loai switch
    {
        LoaiChuongTrinh.Combo => TranShopLuotCombo,
        LoaiChuongTrinh.FlashSale => TranShopLuotFlash,
        _ => 0,
    };

    /// <summary>
    /// Kiểm tra xem một lượt KM hoặc Combo có nên ghi mốc chạy cuối (LanChayCuoi / ComboLanChayCuoi) hay không.
    /// <para>Ghi mốc khi:
    /// <list type="bullet">
    /// <item>Có ít nhất một shop tạo được chương trình (<see cref="KetQuaKm.SoTao"/> &gt; 0).</item>
    /// <item>HOẶC cả lượt đã quét trọn vẹn mọi shop (<c>Shops.Count &gt; 0</c>, <c>SoShopChuaChay == 0</c>,
    /// không lỗi cả lượt, không captcha, không shop nào hỏng) và TẤT CẢ các shop đều có kết luận hợp lệ
    /// (tạo được hoặc bỏ qua vì không còn sản phẩm để thêm). Việc quét tìm sản phẩm mới định kỳ 3 ngày/lần đã xong;
    /// không ghi mốc sẽ khiến lịch tự động lặp lại mỗi đêm vô ích.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static bool NenGhiMoc(KetQuaKm kq)
    {
        if (kq.SoTao > 0) return true;

        return kq.Shops.Count > 0
            && kq.SoShopChuaChay == 0
            && kq.SoHong == 0
            && !kq.Captcha
            && kq.Loi == null
            && kq.TranThuDaCham == 0
            && kq.MocBatDauDaTroiQua == null
            && !kq.NhuongChoCheckDon
            && kq.Shops.All(s => s.DaTao || s.BoQua is not null);
    }

    /// <summary>
    /// <b>LUỒNG COMBO đã CHẠY ĐƯỢC chưa — CÔNG TẮC AN TOÀN của cả vòng combo.</b> <c>true</c> từ 26/08/2026
    /// (chặng B xong: <c>extensions/shopee-orders/flow-combo.js</c> + nhánh <c>chayCombo</c> trong
    /// <c>background.js</c>).
    /// <para><b>Vì sao hằng này tồn tại</b> (phản biện 25/08/2026, NẶNG-1): lúc chặng A xong thì phía extension
    /// CHƯA có người nhận — gói <c>{action:"chayCombo"}</c> rơi vào <c>background.js</c> mà không nhánh nào nhận
    /// (hồi đó cũng chưa có <c>default</c>), nên mỗi shop treo tới hết hạn chặng <c>ChoChang.KmChay</c> = 900s
    /// rồi HỎNG, nghỉ 3–5 phút, sang shop kế. Giá của MỘT đêm bật nhầm: 06:0x hub DỪNG check đơn ở MỌI máy khác
    /// → Brave + SSO → ~6–7 shop bị mở thật → tới 08:00 hết khung: <b>~2 tiếng check đơn chết, 0 chương trình
    /// được tạo</b>. Và nó KHÔNG dừng ở một đêm: <c>ChotLuotCombo</c> chỉ ghi <c>ComboLanChayCuoi</c> khi
    /// <c>SoTao &gt; 0</c> — luôn 0 ⇒ <see cref="DenGioChay"/> mãi đi nhánh "chưa chạy lần nào ⇒ true" ⇒ tới hạn
    /// LẠI mỗi sáng, vĩnh viễn.</para>
    /// <para><b>⚠ GIỮ NGUYÊN HẰNG, ĐỪNG XOÁ.</b> Nó là chỗ DUY NHẤT tắt cả vòng combo khi luồng DOM vỡ (Shopee
    /// đổi markup, phải tắt gấp trong lúc chờ bản vá) — gác cả hai cửa qua <see cref="VongBat"/>: lịch tự động
    /// (<see cref="DenGioChay"/>) và nhường khung check đơn (<see cref="NenNhuongKhung"/>); form thì khoá ô tick
    /// bằng chính hằng này. Tắt lại = sửa đúng một dòng.</para>
    /// <para><b>⚠ CÁCH SỬA SAI, ĐỪNG LÀM:</b> ghi <c>ComboLanChayCuoi</c> cho lượt 0 shop để "chặn lặp" — mâu
    /// thuẫn luật đã chốt của <c>ChotLuotKm</c> (đừng đẩy lịch đi <c>SoNgay</c> ngày cho một lượt chẳng tạo được
    /// gì).</para>
    /// </summary>
    public const bool ComboSanSang = true;

    /// <summary>
    /// <b>TRẦN SỐ SHOP mỗi lượt COMBO — CÔNG TẮC GIAI ĐOẠN THỬ</b> (người dùng chốt 26/08/2026, nguyên văn:
    /// <i>"khi test thì chỉ chạy hết 1 - 2 shop thì dừng, không chạy đủ 12, khi nào xong test thì sẽ mở ra để
    /// chạy full list shop"</i>).
    /// <para><b><c>0</c> = KHÔNG giới hạn</b> (chạy hết danh sách shop). Thử xong thì đặt về <c>0</c> — đúng một
    /// dòng, y như <see cref="ComboSanSang"/>; hai hằng này cùng một họ "công tắc giai đoạn thử".</para>
    /// <para><b>Vì sao cần:</b> <c>flow-combo.js</c> chưa từng chạy trên Shopee THẬT, mà
    /// <see cref="ComboSanSang"/> đã bật — nên nếu luồng vỡ thì lượt tự động 06h sáng đốt 2 shop chứ không phải
    /// 12. Chặt hơn thì rẻ hơn trong lúc chưa ai chạy thật.</para>
    /// <para><b>Áp cho CẢ HAI đường</b> của vòng combo (nút bấm tay và lịch tự động 06–08h) vì cả hai đi chung
    /// <c>OrdersBridgeSession.ChayComboMoiShopAsync</c>. <b>KHÔNG áp cho vòng KM</b> — vòng đó đang chạy thật và
    /// ổn định.</para>
    /// <para><b>Dừng sớm vì trần thì PHẢI NÓI RA</b>: <c>KetQuaKm.TranThuDaCham</c> mang số trần lên tới câu tổng
    /// kết, tách hẳn khỏi hai lý do dừng sớm đã có (hết khung · captcha). Một lượt dừng ở shop thứ 2 mà nhật ký
    /// không nói gì thì y hệt "10 shop hỏng câm".</para>
    /// </summary>
    public const int TranShopLuotCombo = 0;

    /// <summary>
    /// <b>TRẦN SỐ SHOP mỗi lượt FLASH SALE — CÔNG TẮC GIAI ĐOẠN THỬ</b>. <b>GỠ về <c>0</c> = không giới hạn</b>
    /// (01/09/2026); từng để <c>1</c> cho lượt chạy thật đầu tiên 28/08/2026.
    /// <para><b>Vì sao gỡ được:</b> từ 01/09 một lượt flash không còn là "cả danh sách shop trong một đêm" mà là
    /// một GÓI chiếm đúng khe nghỉ giữa hai vòng check đơn — <b>giới hạn của gói là THỜI GIAN, không phải số
    /// shop</b> (<see cref="PhutChotShopMoiTruocCuoiKhe"/> + <see cref="PhutHanChotBatDauShopMoiFlash"/>). Đếm
    /// shop ở đây nữa là hai cái phanh cho cùng một bánh xe, và cái phanh này thì mù về đồng hồ.</para>
    /// <para>Mật độ thao tác của Flash vẫn cao hơn combo hai bậc (<b>8 lần bấm "Tạo" mỗi shop</b> so với 1), nên
    /// chỗ chặn "phóng Brave quá nhiều" nay là <see cref="TranGoiThuMoiNgayFlash"/> và
    /// <see cref="PhutGianCachGoiFlash"/> — chặn ở tầng SỐ GÓI, đúng chỗ chi phí thật nằm.</para>
    /// <para><b>⚠ ĐỪNG viết test đọc thẳng giá trị hằng này rồi tự khai "gỡ trần thì sửa test"</b> — đó đúng bài
    /// học của <see cref="TranShopLuotCombo"/>: test phải sống được ở CẢ HAI trạng thái của công tắc. Cách đúng:
    /// TIÊM trần vào hàm (tham số <c>tranShop</c> của <c>ChayVongTaoChuongTrinhAsync</c>), đừng đọc hằng.</para>
    /// <para>Áp cho CẢ HAI đường của vòng flash (nút bấm tay và lịch tự động) vì cả hai đi chung
    /// <c>OrdersBridgeSession.ChayFlashSaleMoiShopAsync</c>.</para>
    /// </summary>
    public const int TranShopLuotFlash = 0;

    /// <summary>
    /// <b>LUỒNG FLASH SALE đã CHẠY ĐƯỢC chưa — CÔNG TẮC AN TOÀN của cả vòng.</b> <c>true</c> từ 28/08/2026
    /// (<c>flow-flashsale.js/chayFlashSale</c> + nhánh <c>chayFlashSale</c> trong <c>background.js</c> đã lên).
    /// <para>Khuôn và lý do y hệt <see cref="ComboSanSang"/>: người dùng tick được ô flash trên form từ trước
    /// khi luồng phía extension có thật, nên cờ trong DB KHÔNG đủ để quyết định "đêm nay có chạy". Đây là chỗ
    /// DUY NHẤT tắt cả vòng flash khi DOM Shopee vỡ — sửa đúng một dòng.</para>
    /// </summary>
    public const bool FlashSanSang = true;

    /// <summary>
    /// <b>PURE — KHE NGHỈ này có đủ dài để chen một GÓI flash vào không.</b>
    /// <para>Đo bằng <see cref="PhutKheToiThieuChoGoi"/>. Đây cũng chính là thứ tự loại kỳ nghỉ NGẮN 15 phút sau
    /// một vòng check đơn bị cắt: kỳ nghỉ ấy sinh ra để hạ tầng dịu lại, không phải chỗ chen việc.</para>
    /// </summary>
    /// <param name="bayGio">Mốc hiện tại.</param>
    /// <param name="cuoiKhe">Mốc phiên check đơn sẽ bắt đầu vòng kế (<c>IAccountSession.ThoiDiemVongKe</c>).</param>
    public static bool KheDuChoGoiFlash(DateTime bayGio, DateTime cuoiKhe)
        => (cuoiKhe - bayGio).TotalMinutes >= PhutKheToiThieuChoGoi;

    /// <summary>
    /// <b>PURE — HẠN THÔI BẮT ĐẦU SHOP MỚI của một GÓI flash</b>: <c>min(cuối khe −
    /// <see cref="PhutChotShopMoiTruocCuoiKhe"/>, hạn NGÀY 22:15)</c>.
    ///
    /// <para><b>Hai vế, cần CẢ HAI, và chúng chặn hai chuyện khác nhau.</b> Vế KHE giữ gói nằm gọn trong khoảng
    /// nghỉ mà nó mượn — tràn ra là gói còn đang chạy khi phiên check đơn tỉnh dậy, hai luồng tranh cùng một hồ
    /// sơ Brave và cùng cổng 47821. Vế NGÀY là hạn của SHOPEE (đóng đăng ký <c>D+1</c> quanh ~22:50), nó không
    /// biết gì về khe nghỉ và cũng không được khe nghỉ nới ra.</para>
    ///
    /// <para><b>Đây chính là chỗ làm cho gói ĐÀN HỒI</b>: hạn này rót xuống <c>hanChot</c> của vòng, rồi
    /// <see cref="ConDuocBatDauShopMoi"/> kiểm TRƯỚC MỖI SHOP tự quyết gói làm được mấy shop. Khe 120 phút tự ra
    /// ~2 shop mà không hằng số nào phải nói ra con số 2.</para>
    /// </summary>
    public static DateTime HanChotGoiFlash(DateTime bayGio, DateTime cuoiKhe)
    {
        var hanKhe = cuoiKhe.AddMinutes(-PhutChotShopMoiTruocCuoiKhe);
        var hanNgay = HanChotLuotTuDong(bayGio, LoaiChuongTrinh.FlashSale);
        return hanKhe < hanNgay ? hanKhe : hanNgay;
    }

    /// <summary>
    /// <b>PURE — NGÀY ĐÍCH của một lượt Flash Sale: ngày BẮT ĐẦU LƯỢT + 1.</b>
    ///
    /// <para><b>⚠ GỌI ĐÚNG MỘT LẦN, lúc bắt đầu lượt, rồi RÓT vào gói tham số dùng cho MỌI shop.</b> Tuyệt đối
    /// không gọi lại trong vòng lặp shop: chữ "ngày mai" đổi nghĩa GIỮA LƯỢT khi lượt vắt qua nửa đêm. Shop 1–8
    /// chạy lúc 23:5x nhắm <c>D+1</c>; shop 9–12 chạy lúc 00:0x thì "ngày mai" đã là <c>D+2</c> ⇒ một lượt tạo
    /// flash sale cho HAI ngày khác nhau, còn ngày <c>D+1</c> thì bốn shop cuối không có gì. <b>Không lỗi, không
    /// log.</b> (Từ 01/09/2026 hạn 22:15 + trần 45 phút một shop làm ca này không dựng nổi nữa — nhưng chốt vẫn
    /// giữ: nó rẻ, và nó là thứ chặn ca "một gói chạy lâu bất thường".)</para>
    ///
    /// <para>Đây đúng bài học đã ghi ở <c>ChayVongTaoChuongTrinhAsync</c>: <i>"MỘT lượt đọc đồng hồ cho cả hai
    /// cửa thời gian — hai lượt đọc là hai câu trả lời khác nhau cho cùng một câu hỏi"</i>.</para>
    ///
    /// <para><b>Đặt cho NGÀY MAI chứ không phải hôm nay</b> (người dùng chốt 27/08: <i>"shopee cho set trước
    /// mà"</i>): đủ 8 khung thay vì ~7, và không khung nào ở trạng thái "đã bắt đầu dở".</para>
    /// </summary>
    public static DateTime NgayDichFlash(DateTime bayGio) => bayGio.Date.AddDays(1);

    /// <summary>
    /// <b>PURE — có được phép MỞ MỘT GÓI FLASH ngay bây giờ không</b> (01/09/2026). Năm điều kiện, thiếu một là
    /// không mở. Đây là cửa THUẦN của <c>AccountsViewModel.NhipLichFlashAsync</c>; ba cửa còn lại của nhịp —
    /// tiến độ ngày, "mọi phiên đang nghỉ", độ dài khe — cần trạng thái RUNTIME nên nằm ở tầng App.
    ///
    /// <list type="number">
    /// <item><see cref="VongBat"/> — công tắc của tài khoản + <see cref="FlashSanSang"/>.</item>
    /// <item><see cref="TrongKhung"/> — giờ ∈ <c>[0,23)</c>.</item>
    /// <item><see cref="ConDuocBatDauShopMoi"/> với hạn <b>22:15</b> — mở gói lúc 22:20 là chắc chắn 0 shop, mà
    /// vẫn tốn trọn một lượt phóng Brave + SSO (ca thật đã ghi ở bản 21–24h cũ: app khởi động 23:50).</item>
    /// <item><b>GIÃN CÁCH GÓI</b> — <c>cf.FlashLanThuTuDong</c> cách <paramref name="bayGio"/> chưa đủ
    /// <see cref="PhutGianCachGoiFlash"/> phút thì thôi. <b>Thay hẳn <see cref="DaThuTrongKhungNay"/></b> cho
    /// vòng này: chốt một-lượt-mỗi-khung chết theo khung 21–24h, vì "một lượt mỗi khung" nay là một lượt mỗi
    /// NGÀY — mà cả thiết kế mới dựng trên việc chạy NHIỀU gói một ngày.</item>
    /// <item><b>HÔM NAY ĐÃ XONG TRỌN</b> — <c>cf.FlashLanChayCuoi</c> rơi đúng ngày
    /// <paramref name="bayGio"/>. ⚠ <b>Ý NGHĨA CỦA MỐC ĐÃ ĐỔI</b>: từ "lượt gần nhất có tạo được gì đó" thành
    /// <b>"ngày đích của hôm đó đã phủ ĐỦ MỌI SHOP"</b> — xem <c>AccountsViewModel.ChotLuotFlash</c>. Nhờ vậy
    /// một lượt bấm tay lẻ 1 shop không còn đốt lượt tự động của cả ngày, và không cần ngoại lệ
    /// "mốc-ngoài-khung" nào nữa.</item>
    /// </list>
    /// </summary>
    public static bool DenGioGoiFlash(KmConfig cf, DateTime bayGio)
    {
        if (!VongBat(cf, LoaiChuongTrinh.FlashSale)) { return false; }
        if (!TrongKhung(bayGio, LoaiChuongTrinh.FlashSale)) { return false; }
        // CẤM mở gói trong khung 01:00–04:00 — thời gian dành riêng cho KM & Combo của Shop (không check đơn, không flash sale)
        if (bayGio.Hour >= GioMoKhung && bayGio.Hour < GioDongKhung) { return false; }
        if (!ConDuocBatDauShopMoi(bayGio, HanChotLuotTuDong(bayGio, LoaiChuongTrinh.FlashSale))) { return false; }
        if (MocThuCua(cf, LoaiChuongTrinh.FlashSale) is DateTime thu
            && (bayGio - thu).TotalMinutes < PhutGianCachGoiFlash)
        {
            return false;
        }
        if (MocChayCuoiCua(cf, LoaiChuongTrinh.FlashSale) is DateTime cuoi && cuoi.Date == bayGio.Date)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// <b>PURE — vòng tự động của loại này có ĐANG BẬT không.</b> Mỗi loại một CỜ RIÊNG.
    /// <para><b>CHOKE POINT DUY NHẤT của vòng combo:</b> <c>cf.ComboBat</c> phải đi kèm
    /// <see cref="ComboSanSang"/>. Người dùng tick được ô combo trên form từ trước khi luồng phía extension có
    /// thật, nên cờ trong DB KHÔNG đủ để quyết định "đêm nay có chạy" — xem <see cref="ComboSanSang"/> cho giá
    /// phải trả của một đêm bật nhầm.</para>
    /// <para><b>FlashSale: <c>cf.FlashBat &amp;&amp; <see cref="FlashSanSang"/></b> (28/08/2026) — cùng khuôn
    /// combo, mặc định TẮT trong DB nên không tài khoản nào tự dưng có một vòng tạo tới 8 chương trình THẬT mỗi
    /// shop mỗi đêm. Người dùng bật bằng ô tick trên tab "Flash Sale" của form KM.</para>
    /// </summary>
    public static bool VongBat(KmConfig cf, LoaiChuongTrinh loai) => loai switch
    {
        LoaiChuongTrinh.ChuongTrinhShop => cf.TuDong,
        LoaiChuongTrinh.Combo => cf.ComboBat && ComboSanSang,
        LoaiChuongTrinh.FlashSale => cf.FlashBat && FlashSanSang,
        _ => false,
    };

    /// <summary>
    /// <b>PURE — mốc TẠO ĐƯỢC gần nhất của ĐÚNG vòng đó.</b> Xem <see cref="MocThuCua"/> cho lý do phải tách.
    /// </summary>
    public static DateTime? MocChayCuoiCua(KmConfig cf, LoaiChuongTrinh loai) => loai switch
    {
        LoaiChuongTrinh.ChuongTrinhShop => cf.LanChayCuoi,
        LoaiChuongTrinh.Combo => cf.ComboLanChayCuoi,
        LoaiChuongTrinh.FlashSale => cf.FlashLanChayCuoi,
        _ => null,
    };

    /// <summary>
    /// <b>PURE — mốc BẮT ĐẦU THỬ gần nhất của ĐÚNG vòng đó.</b>
    /// <para><b>⚠ HAI VÒNG PHẢI CÓ HAI MỐC RIÊNG — đây là chỗ dễ hỏng nhất của cả đợt (plan §A2).</b> Dùng
    /// chung một <c>LanChayCuoi</c> thì: lượt KM chạy 02:05 ngày D ghi mốc <c>D 02:xx</c>; tới 06:05 vòng combo
    /// hỏi <see cref="DenGioChay"/> → <c>bayGio &gt;= cuoi.Date + SoNgay + 6h</c> = <c>D+3 06:00</c> → <b>false</b>
    /// ⇒ <b>combo KHÔNG BAO GIỜ CHẠY</b>, và không một dòng nhật ký nào nói vì sao.</para>
    /// </summary>
    public static DateTime? MocThuCua(KmConfig cf, LoaiChuongTrinh loai) => loai switch
    {
        LoaiChuongTrinh.ChuongTrinhShop => cf.LanThuTuDong,
        LoaiChuongTrinh.Combo => cf.ComboLanThuTuDong,
        // ⚠ TRẢ <c>null</c> Ở ĐÂY LÀ CA HỎNG NẶNG NHẤT CỦA VÒNG FLASH, đừng "gọn" lại thành `_ => null`:
        // vế GIÃN CÁCH GÓI của `DenGioGoiFlash` đọc đúng mốc này, nên null ⇒ nhịp đồng hồ 60 giây mở gói mới
        // MỖI PHÚT suốt cả ngày khi gói đầu hỏng nhanh ⇒ hàng trăm lần phóng Brave, mỗi lần một lượt SSO.
        // (Trước 01/09/2026 chỗ đọc là `DaThuTrongKhungNay`; đổi người đọc, không đổi hậu quả.)
        LoaiChuongTrinh.FlashSale => cf.FlashLanThuTuDong,
        _ => null,
    };

    /// <summary>Đang trong khung CHẠY của loại chương trình đó (mốc ĐÓNG không thuộc khung).</summary>
    public static bool TrongKhung(DateTime bayGio, LoaiChuongTrinh loai)
    {
        var (mo, dong) = Khung(loai);
        return bayGio.Hour >= mo && bayGio.Hour < dong;
    }

    /// <summary>
    /// <b>PURE — mốc này rơi vào BIÊN AN của khung nào</b> (<c>[mở − <see cref="SoGioBienAn"/>, đóng)</c>), hoặc
    /// <c>null</c> nếu không khung nào.
    /// <para>Hai biên an — <c>[1,4)</c> và <c>[5,8)</c> — RỜI NHAU nên mỗi mốc thuộc tối đa một khung; mọi giờ
    /// còn lại là chỗ vòng check đơn được phép bắt đầu.</para>
    /// <para><b>⚠ CHỈ QUÉT <see cref="LoaiDoiNhuongKhung"/>, KHÔNG quét Flash Sale</b> (01/09/2026) — lý do đầy
    /// đủ ở xmldoc của mảng đó: khung flash nay là cả ngày, đưa nó vào đây là check đơn tự dời vòng vô hạn.
    /// Vòng flash không đòi nhường khung; nó CHỜ khe nghỉ mà check đơn vốn đã có.</para>
    /// </summary>
    public static LoaiChuongTrinh? KhungCuaBienAn(DateTime moc)
    {
        foreach (var loai in LoaiDoiNhuongKhung)
        {
            var (mo, dong) = Khung(loai);
            if (moc.Hour >= mo - SoGioBienAn && moc.Hour < dong) { return loai; }
        }
        return null;
    }

    /// <summary>Mốc rơi vào BIÊN AN của BẤT KỲ khung nào đòi nhường (KM · Combo — không có flash).</summary>
    public static bool TrongBienAn(DateTime moc) => KhungCuaBienAn(moc) is not null;

    /// <summary>
    /// <b>PURE — CHU KỲ (số ngày) giữa hai lượt của ĐÚNG vòng đó.</b>
    /// <para>Hai vòng KM/Combo dùng <c>cf.SoNgay</c> người dùng đặt trên form (mặc định 3).
    /// <b>Vòng FLASH SALE thì CỨNG <see cref="SoNgayChuKyFlash"/> = 1 — MỖI NGÀY MỘT LƯỢT</b>, không đọc
    /// <c>cf.SoNgay</c>.</para>
    /// <para><b>Vì sao cứng chứ không cho người dùng chỉnh</b> (plan §4): việc của một đêm Flash Sale là đặt
    /// chương trình cho ĐÚNG NGÀY MAI. Bỏ một đêm là mất trắng ngày đó — không có lượt nào "bù" được, vì lượt
    /// đêm sau chỉ nhắm ngày của chính nó. Chu kỳ 3 ngày ở đây nghĩa là 2/3 số ngày trong tháng KHÔNG có flash
    /// sale, và không một dòng log nào nói ra điều đó.</para>
    /// <para>Kẹp sàn 1: <c>cf.SoNgay</c> đến từ DB nên một dòng bị sửa tay thành 0 sẽ làm mốc kế trùng mốc cũ
    /// ⇒ tới hạn ngay lập tức, mỗi nhịp.</para>
    /// </summary>
    public static int SoNgayChuKy(KmConfig cf, LoaiChuongTrinh loai)
        => loai == LoaiChuongTrinh.FlashSale ? SoNgayChuKyFlash : Math.Max(1, cf.SoNgay);

    /// <summary>Chu kỳ CỨNG của vòng Flash Sale: <b>1 = mỗi ngày một lượt</b> (người dùng chốt 28/08/2026).</summary>
    public const int SoNgayChuKyFlash = 1;

    /// <summary>
    /// Mốc SỚM NHẤT được phép chạy lượt kế của <paramref name="loai"/>: <c>mốc chạy cuối + SoNgay</c>, kéo về
    /// đúng giờ mở khung CỦA LOẠI ĐÓ.
    /// <para><c>SoNgay</c> dùng chung cho KM và Combo (người dùng: "3 ngày chạy 1 lần, 3 vòng riêng biệt");
    /// <b>vòng FLASH đi chu kỳ CỨNG 1 ngày</b> — xem <see cref="SoNgayChuKy"/>. Mỗi vòng một CẶP MỐC riêng.</para>
    /// <para>Chưa chạy lần nào ⇒ khung GẦN NHẤT sắp tới. Cố ý KHÔNG chạy ngay khi vừa bật: người dùng bật "tự
    /// động" lúc 10h sáng mà vòng 12 shop khởi động tức thì là bất ngờ khó chịu.</para>
    /// </summary>
    public static DateTime MocChayKe(KmConfig cf, DateTime bayGio, LoaiChuongTrinh loai)
    {
        if (MocChayCuoiCua(cf, loai) is not DateTime cuoi)
        {
            return KhungGanNhat(bayGio, loai);
        }
        var moc = cuoi.Date.AddDays(SoNgayChuKy(cf, loai)).AddHours(Khung(loai).Mo);
        // Mốc đã trôi qua (app tắt mấy hôm) ⇒ khung gần nhất sắp tới, không dồn nhiều lượt bù.
        return moc < bayGio ? KhungGanNhat(bayGio, loai) : moc;
    }

    /// <summary>Đầu khung gần nhất của <paramref name="loai"/> KHÔNG nhỏ hơn <paramref name="bayGio"/>; đang
    /// trong khung thì là chính nó.</summary>
    private static DateTime KhungGanNhat(DateTime bayGio, LoaiChuongTrinh loai)
    {
        if (TrongKhung(bayGio, loai)) { return bayGio; }
        var homNay = bayGio.Date.AddHours(Khung(loai).Mo);
        return bayGio < homNay ? homNay : bayGio.Date.AddDays(1).AddHours(Khung(loai).Mo);
    }

    /// <summary>
    /// <b>Có phải chạy NGAY BÂY GIỜ vòng <paramref name="loai"/> không.</b> Bốn điều kiện, thiếu một là không
    /// chạy: vòng đó đang bật (<see cref="VongBat"/>) · đang trong khung của LOẠI ĐÓ · <b>khung này chưa thử
    /// lượt nào</b> · đã tới mốc <c>mốc chạy cuối + SoNgay</c>.
    /// <para><b>⚠ CHỈ CÒN KM và COMBO đi qua đây</b> (01/09/2026). Vòng FLASH có cửa riêng
    /// <see cref="DenGioGoiFlash"/> vì luật của nó đã khác hẳn: nhiều GÓI mỗi ngày (chứ không phải một lượt mỗi
    /// khung), giãn cách tính bằng PHÚT (chứ không phải "đã thử trong khung này"), và mốc chạy cuối mang nghĩa
    /// "ngày đã phủ đủ mọi shop". Hai nhánh <c>if (loai == FlashSale)</c> từng nằm ở đây — cửa 23:45 và ngoại lệ
    /// mốc-ngoài-khung của commit <c>6582e64</c> — đã GỠ: cả hai sinh ra từ khung 21–24h và hết lý do tồn tại.</para>
    /// <para><b>MỘT LƯỢT THỬ MỖI KHUNG</b> (<see cref="MocThuCua"/>, thêm 19/08/2026): trước đó chỉ có mốc
    /// "chạy cuối" — mà mốc ấy chỉ ghi khi TẠO ĐƯỢC. Lượt hỏng (captcha · máy khác đang giữ khoá · lỗi ở shop
    /// đầu) không để lại dấu, nên nhịp đồng hồ 60 giây bắn lại lượt mới MỖI PHÚT cho tới hết khung: hàng chục
    /// lần SSO một đêm, đúng thứ làm Shopee bật <c>verify/traffic/error</c>. Nay thử một lần là thôi tới khung
    /// đêm sau.</para>
    /// <para><b>HAI VÒNG, HAI CẶP MỐC — xem <see cref="MocThuCua"/>.</b> Gộp lại là combo chết câm.</para>
    /// <para><b>Ngữ nghĩa "ngày lịch" của mốc chạy cuối — CỐ Ý, đừng đổi:</b> mốc kế lấy <c>cuoi.Date</c>
    /// (bỏ phần giờ) rồi cộng <c>SoNgay</c> và kéo về giờ mở khung. Nghĩa là chạy tay lúc 23:50 ngày D với
    /// <c>SoNgay = 1</c> thì lượt tự động rơi vào <b>giờ mở khung ngày D+1</b>. Đó là hành vi ĐÚNG với cách
    /// người dùng nói ("sau xx NGÀY", đếm theo ngày lịch chứ không phải theo 24 giờ tròn); giữ phần giờ thì
    /// "mỗi ngày" sẽ trôi dần và cuối cùng rơi ra ngoài khung.</para>
    /// </summary>
    public static bool DenGioChay(KmConfig cf, DateTime bayGio, LoaiChuongTrinh loai)
    {
        if (!VongBat(cf, loai)) { return false; }
        if (!TrongKhung(bayGio, loai)) { return false; }
        if (DaThuTrongKhungNay(cf, bayGio, loai)) { return false; }
        if (MocChayCuoiCua(cf, loai) is not DateTime cuoi) { return true; } // đang bật, tới khung đầu tiên ⇒ chạy
        return bayGio >= cuoi.Date.AddDays(SoNgayChuKy(cf, loai)).AddHours(Khung(loai).Mo);
    }

    /// <summary>
    /// Vòng <paramref name="loai"/> ĐÃ thử trong ĐÚNG khung đang diễn ra hay chưa.
    /// <para>Mọi khung đều nằm trọn trong một ngày lịch nên "cùng khung" = cùng <c>Date</c> và mốc thử cũng nằm
    /// trong khung ĐÓ. Mốc thử của đêm TRƯỚC (ngày khác) không chặn đêm nay.</para>
    /// </summary>
    public static bool DaThuTrongKhungNay(KmConfig cf, DateTime bayGio, LoaiChuongTrinh loai)
        => MocThuCua(cf, loai) is DateTime thu && thu.Date == bayGio.Date && TrongKhung(thu, loai);

    /// <summary>
    /// <b>HẠN CHÓT của một lượt TỰ ĐỘNG</b>: giờ ĐÓNG khung của <paramref name="loai"/>, tính trên chính ngày
    /// lượt đó khởi động (Shop 04:00 · Combo 08:00).
    /// <para>Lượt tự động khởi động được tới tận phút cuối khung (<see cref="DenGioChay"/>), mà vòng 12 shop thì
    /// không có mốc dừng nào của riêng nó: với hạn chặng 900s + nghỉ 3–5 phút mỗi shop, ca xấu chạy thêm vài
    /// tiếng và check đơn chết suốt buổi. Hạn chót này là thứ cắt vòng lại.</para>
    /// <para><b>⚠ FLASH SALE LÀ NGOẠI LỆ: <c>22:15</c>, KHÔNG phải giờ đóng khung (23:00).</b> Shopee đóng đăng
    /// ký cho ngày <c>D+1</c> quanh ~22:50, nên nửa sau của một shop bắt đầu lúc 22:3x rơi vào vùng chết. Xem
    /// <see cref="PhutHanChotBatDauShopMoiFlash"/>.</para>
    /// </summary>
    public static DateTime HanChotLuotTuDong(DateTime bayGio, LoaiChuongTrinh loai)
        => loai == LoaiChuongTrinh.FlashSale
            ? bayGio.Date.AddMinutes(PhutHanChotBatDauShopMoiFlash)
            : bayGio.Date.AddHours(Khung(loai).Dong);

    /// <summary>
    /// <b>Còn được BẮT ĐẦU một shop mới không.</b> <paramref name="hanChot"/> <c>null</c> = đường bấm tay (người
    /// dùng đang ngồi trước máy, họ tự quyết khi nào dừng) ⇒ luôn được.
    /// <para>Chỉ chặn shop MỚI, KHÔNG cắt ngang shop đang chạy dở: cắt giữa chừng có thể bỏ lại một form đã điền
    /// nửa vời trên Shopee, còn tệ hơn chạy quá giờ vài phút.</para>
    /// </summary>
    public static bool ConDuocBatDauShopMoi(DateTime bayGio, DateTime? hanChot)
        => hanChot is not DateTime han || bayGio < han;

    /// <summary>
    /// <b>Đẩy mốc vòng check đơn ra KHỎI khung chương trình — kèm BIÊN AN 60 phút.</b> Người dùng chốt: "tới
    /// gần giờ chạy thì không tự đặt vòng kế nữa, phải đặt sau khi khung đóng".
    /// <para>Mốc rơi vào biên an của khung nào (<see cref="KhungCuaBienAn"/>) thì kéo tới giờ ĐÓNG của <b>ĐÚNG
    /// khung đó</b> — 04:00 · 08:00 · 12:00, <b>KHÔNG phải luôn luôn 06:00 như bản một-khung cũ</b>. Ngoài ba
    /// biên an thì trả nguyên. <paramref name="nhuongKhung"/> <c>false</c> ⇒ KHÔNG đụng vào.</para>
    /// <para><b>Hàm này chỉ biết DỜI MỐC — quyết định "có nhường hay không" nằm ở
    /// <see cref="NenNhuongKhung"/>.</b> Đừng truyền thẳng <c>cf.TuDong</c> vào đây: "có bật tự động" khác
    /// "đêm nay có chạy", và lẫn hai thứ đó là đổi hàng tiếng check đơn MỖI ĐÊM lấy một lượt mỗi
    /// <c>SoNgay</c> đêm.</para>
    /// <para><b>Vì sao biên an 60 phút</b> (khôi phục 19/08/2026 theo plan cũ §5): vòng check đơn chạy 30–45
    /// phút, nên một vòng ĐẶT lúc 01:30 vẫn đang chạy khi khung KM tới giờ. Khung CHẠY
    /// (<see cref="TrongKhung"/>) vẫn nguyên; biên an chỉ áp cho việc HOÃN vòng check đơn.</para>
    /// </summary>
    public static DateTime HoanVongCheckDon(DateTime mocDuKien, bool nhuongKhung)
    {
        if (!nhuongKhung) { return mocDuKien; }
        if (KhungCuaBienAn(mocDuKien) is not LoaiChuongTrinh loai) { return mocDuKien; }
        return mocDuKien.Date.AddHours(Khung(loai).Dong);
    }

    /// <summary>
    /// <b>PURE — máy này có phải NHƯỜNG khung cho một luồng chương trình ở vòng check đơn sắp đặt không.</b>
    /// Ba điều kiện, thiếu một là KHÔNG nhường (giữ nguyên mốc vòng kế).
    /// <list type="number">
    /// <item><b>Mốc dự kiến đâm vào biên an của MỘT khung</b> (<see cref="KhungCuaBienAn"/>) và <b>vòng của
    /// đúng khung đó đang bật</b> (<see cref="VongBat"/>) — chặn khung cho một vòng chẳng bao giờ chạy là tự
    /// cắt check đơn không vì cái gì. Mốc rơi vào khe nghỉ (<c>[4,5)</c>, <c>[8,9)</c>) ⇒ không khung nào ⇒
    /// không nhường.</item>
    /// <item><b>Máy này là máy CHỦ TRÌ</b> (<see cref="LaMayChuTri"/>) — <b>CHẶN TẠM CỦA PHA A, PHA B PHẢI GỠ,
    /// xem hạng mục <c>B1</c> trong <c>plans/2026-08-22-dong-bo-ho-so-km-xuyen-may.md</c>.</b> Từ khi hồ sơ KM
    /// đồng bộ được, cờ bật lan sang MỌI máy giữ tài khoản đó, nên máy KHÔNG chủ trì cũng bắt đầu dời vòng —
    /// <b>mà vẫn ôm khoá <c>orders:&lt;login&gt;</c></b>, vì nhả khoá là việc của pha B và pha B chưa làm. Kết
    /// quả là máy chủ trì vẫn không xin được khoá còn máy kia mất thêm mấy tiếng check đơn mỗi đêm — tệ hơn hẳn
    /// lúc chưa đồng bộ.</item>
    /// <item><b>Đêm nay THỰC SỰ tới hạn</b> theo <see cref="MocChayKe"/> — không có vế này thì với
    /// <c>SoNgay = 3</c> ta đổi <b>khung × 3 đêm</b> check đơn lấy đúng MỘT lượt, vì
    /// <see cref="HoanVongCheckDon"/> chỉ nhận một <c>bool</c> chứ không hỏi hôm nay có chạy hay không.</item>
    /// </list>
    /// <para><b>"Đêm nay" là khung mà <paramref name="mocDuKien"/> đâm vào</b> — mỗi biên an nằm gọn trong một
    /// ngày lịch nên khung đó là <c>[mocDuKien.Date + mở, mocDuKien.Date + đóng)</c>. Mốc chạy kế rơi trước lúc
    /// khung đóng ⇒ đêm nay có lượt ⇒ nhường.</para>
    /// </summary>
    /// <param name="cf">Cấu hình KM (đã gộp từ hub) của tài khoản đang chạy vòng.</param>
    /// <param name="mayNay">Id suất đơn hàng của máy này; rỗng/null = không có hub ⇒ xem <see cref="LaMayChuTri"/>.</param>
    /// <param name="mocDuKien">Mốc vòng check đơn kế dự kiến.</param>
    /// <param name="bayGio">Mốc hiện tại (giờ địa phương) — gốc tính <see cref="MocChayKe"/>.</param>
    public static bool NenNhuongKhung(KmConfig cf, string? mayNay, DateTime mocDuKien, DateTime bayGio)
    {
        foreach (var loai in LoaiDoiNhuongKhung)
        {
            var (mo, dong) = Khung(loai);
            if (mocDuKien.Hour >= mo - SoGioBienAn && mocDuKien.Hour < dong
                && VongBat(cf, loai)
                && LaMayChuTri(cf.OwnerMachine, mayNay)
                && MocChayKe(cf, bayGio, loai) < mocDuKien.Date.AddHours(dong))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// <b>PURE — máy này có phải máy CHỦ TRÌ của lịch KM tài khoản đó không.</b> Dùng ở hai chỗ: cửa chạy lượt
    /// tự động (<c>AccountsViewModel.NhipLichKmAsync</c>) và cửa nhường khung (<see cref="NenNhuongKhung"/>).
    /// <para><b>Vì sao phải có cửa này ngay khi hồ sơ KM đồng bộ được</b> (phản biện 22/08/2026): trước đồng
    /// bộ, <c>TuDong</c> chỉ bật ở đúng máy người dùng mở form nên "ai chạy" tự phân xử. Đồng bộ xong thì
    /// <c>TuDong = true</c> lan sang MỌI máy giữ tài khoản đó ⇒ 02:00 cả fleet cùng tới hạn, cùng dừng phiên
    /// check đơn cục bộ, cùng đốt <c>LanThuTuDong</c>, và máy thắng khoá là NGẪU NHIÊN. Nặng nhất: máy kéo danh
    /// bạ về từ hub có bản ghi RỖNG MẬT KHẨU — nó giành được khoá lúc 2h sáng rồi SSO hỏng, còn máy chủ trì
    /// thật thì đã mất lượt của đêm đó.</para>
    /// <para><b>RỖNG mang HAI nghĩa khác nhau — đừng gộp lại (phản biện vòng 6, 22/08/2026).</b> Ba bản trước
    /// coi cả hai là một và cùng trả <c>true</c>; đó là gốc của một lỗi tạo khuyến mãi THẬT trùng nhau:</para>
    /// <list type="bullet">
    /// <item><paramref name="ownerMachine"/> RỖNG = <b>"KHÔNG AI chủ trì"</b> (dữ liệu cũ chưa ai nhận, hoặc máy
    /// chạy độc lập không hub nên chẳng ai đóng dấu) ⇒ <c>true</c>, giữ nguyên hành vi trước khi có cửa chủ trì.
    /// Bỏ nhánh này là KM chết trên máy một mình.</item>
    /// <item><paramref name="mayNay"/> RỖNG trong khi ĐÃ CÓ chủ trì = <b>"máy này KHÔNG BIẾT MÌNH LÀ AI"</b>
    /// (chưa cấu hình hub, vừa bấm NGẮT ở Cài đặt, hoặc đang trong cửa sổ <c>Reconnect()</c>) ⇒ <c>false</c>.
    /// Không biết mình là ai thì KHÔNG được tự nhận mình là <c>ownerMachine</c>. Trả <c>true</c> ở đây nghĩa là
    /// máy B lúc 2h sáng chạy trọn lượt KM của tài khoản mà máy A đang chủ trì — hai máy cùng tạo chương trình
    /// khuyến mãi thật trên cùng tập shop, không một dòng nhật ký nào.</item>
    /// </list>
    /// <para><b>Hướng sai lệch là CỐ Ý:</b> chặt quá thì KM không chạy — ồn ào, có dòng "MÁY CHỦ TRÌ là X" mỗi
    /// đêm để lần ra. Lỏng quá thì KM chạy thừa — câm, và để lại chương trình khuyến mãi thật phải đi gỡ tay.</para>
    /// <para>Đường thoát khi chủ trì trỏ vào máy đã nghỉ hưu: nối lại hub rồi bấm <b>"Máy này nhận chủ trì"</b>
    /// trên form KM — nút đó vốn đã đòi <c>mayNay</c> khác rỗng, nên hai bên nay cùng một luật.</para>
    /// </summary>
    public static bool LaMayChuTri(string? ownerMachine, string? mayNay)
    {
        var chuTri = ownerMachine?.Trim() ?? "";
        var toi = mayNay?.Trim() ?? "";
        if (chuTri.Length == 0) { return true; }   // không ai chủ trì ⇒ ai cũng được chạy (hành vi cũ)
        if (toi.Length == 0) { return false; }     // đã có chủ trì mà mình không biết mình là ai ⇒ ĐỨNG NGOÀI
        return string.Equals(chuTri, toi, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <b>PURE — NHÃN người đọc được của một id máy</b>: <c>"Máy kho 2 (3f8a91)"</c>, hoặc chỉ <c>"3f8a91"</c>
    /// khi chưa tra được tên.
    /// <para><b>Vì sao không in id thô</b> (phản biện vòng 3, 22/08/2026): id suất đơn hàng là
    /// <c>&lt;guid 32 ký tự&gt;:orders</c>. Form KM và nhật ký in nguyên chuỗi đó thì người dùng có một fleet 3
    /// máy vẫn phải mở form ở từng máy để đoán ra ai đang chủ trì — trong khi repo đã có quy ước ngược lại ở
    /// <c>AccountSession.CauBiTuChoiKhoa</c> (nói TÊN máy) và <c>MachineIdentity.Label</c> (tên + id ngắn).</para>
    /// <para>Vẫn GIỮ id ngắn cạnh tên: hai máy trùng tên hiển thị là chuyện có thật (hai PC cùng đặt "PC-KHO"),
    /// và id ngắn là thứ duy nhất đối chiếu được với dòng trong bảng máy trên Hub.</para>
    /// </summary>
    /// <param name="machineId">Id suất đơn hàng (<c>&lt;id-máy&gt;:orders</c>). Rỗng ⇒ trả chuỗi rỗng.</param>
    /// <param name="tenMay">Tên máy tra được (hostname / tên người dùng đặt); rỗng/null ⇒ chỉ hiện id ngắn.</param>
    public static string NhanMay(string? machineId, string? tenMay)
    {
        var id = machineId?.Trim() ?? "";
        if (id.Length == 0) { return ""; }

        // Bỏ hậu tố suất (":orders") rồi cắt 6 ký tự đầu — đúng khuôn MachineIdentity.Label.
        var loi = id.Split(':')[0];
        var ngan = loi.Length <= 6 ? loi : loi[..6];
        var ten = tenMay?.Trim() ?? "";
        return ten.Length == 0 ? ngan : $"{ten} ({ngan})";
    }

    /// <summary>Câu nhật ký khi vòng check đơn bị hoãn — nói RÕ nhường khung NÀO, kẻo người đọc tưởng phiên treo.</summary>
    public static string CauHoanVong(DateTime mocCu, DateTime mocMoi, LoaiChuongTrinh? loaiCu = null)
    {
        var loai = loaiCu ?? KhungCuaBienAn(mocCu) ?? LoaiChuongTrinh.ChuongTrinhShop;
        var (mo, dong) = Khung(loai);
        return $"Vòng kế rơi vào khung {TenLoai(loai)} ({mo}–{dong}h, biên an từ {mo - SoGioBienAn}h) — dời từ "
               + $"{mocCu:HH:mm} sang {mocMoi:HH:mm} để nhường luồng KM của Shop.";
    }
}
