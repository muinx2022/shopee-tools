using System.Globalization;
using System.Text.Json;
using XuLyDonShopee.Core.Models;

namespace XuLyDonShopee.Core.Services;

/// <summary>
/// Kết quả một "lát cắt kiểm chứng" của cầu nối extension↔C#: đọc danh sách shop → mở "Chi tiết"
/// shop đầu (trusted click, kỳ vọng KHÔNG captcha) → đọc số "Chờ Lấy Hàng".
/// </summary>
/// <param name="Shops">Danh sách shop parse từ bảng <c>/portal/shop</c> (rỗng nếu chưa/không đọc được).</param>
/// <param name="FirstShopId">Mã shop đầu đã thử mở "Chi tiết" (null nếu không có shop nào).</param>
/// <param name="ToShipCount">Số "Chờ Lấy Hàng" đọc được ở shop đầu (null nếu không đọc được).</param>
/// <param name="Captcha">True nếu extension báo rơi vào trang verify/captcha.</param>
/// <param name="Error">Thông báo lỗi (null nếu chạy trọn lát cắt không lỗi).</param>
/// <param name="OrdersCount">GĐ3: số đơn đọc được từ tab "Tất cả" (0 nếu không đọc).</param>
/// <param name="SlipsSaved">GĐ3: số phiếu giao PDF đã lưu (Phần B; 0 nếu không xử đơn).</param>
public sealed record OrdersBridgeSliceResult(
    IReadOnlyList<ShopListItem> Shops,
    string? FirstShopId,
    int? ToShipCount,
    bool Captcha,
    string? Error,
    int OrdersCount = 0,
    int SlipsSaved = 0);

    /// <summary>
    /// Kết quả lát cắt <b>DÒ DOM</b> của pha 1 "KM của Shop" — xem <see cref="OrdersBridgeSession.DoDomKmAsync"/>.
    /// </summary>
    /// <param name="ShopId">Shop đã mở chi tiết để dò (null nếu chưa mở được shop nào).</param>
    /// <param name="DumpJson">JSON thô extension trả về (mảng bước). null = không nhận được.</param>
    /// <param name="Captcha">Rơi vào trang verify/captcha.</param>
    /// <param name="Error">Thông báo lỗi (null nếu chạy trọn).</param>
    public sealed record KetQuaDoDomKm(string? ShopId, string? DumpJson, bool Captcha, string? Error);

    /// <summary>
    /// Kết quả lát cắt <b>DÒ DOM Flash Sale Của Shop</b> — xem
    /// <see cref="OrdersBridgeSession.DoDomFlashSaleAsync"/>.
    /// <para>Cùng bốn trường với <see cref="KetQuaDoDomKm"/> nhưng là kiểu RIÊNG, cố ý: hai lượt dò trả về hai
    /// thứ khác nhau (lượt KM có thể ĐÃ TẠO chương trình, lượt Flash Sale thì tuyệt đối không) và dùng chung
    /// một kiểu là mời gọi dùng chung luôn cả đường xử lý.</para>
    /// </summary>
    /// <param name="ShopId">Shop đã mở chi tiết để dò (null nếu chưa mở được shop nào).</param>
    /// <param name="DumpJson">JSON thô extension trả về. null = không nhận được.</param>
    /// <param name="Captcha">Rơi vào trang verify/captcha.</param>
    /// <param name="Error">Thông báo lỗi (null nếu chạy trọn).</param>
    public sealed record KetQuaDoDomFlash(string? ShopId, string? DumpJson, bool Captcha, string? Error);


/// <summary>Tham số đăng nhập cho <see cref="OrdersBridgeSession.RunLoginThenSliceAsync"/> (GĐ2).</summary>
/// <param name="User">Tên đăng nhập subaccount (= <c>acc.Email</c> ở luồng production).</param>
/// <param name="Pass">Mật khẩu subaccount (= <c>acc.Password</c>).</param>
/// <param name="VerifyEmail">Hotmail/Outlook để đọc mã xác thực (có thể rỗng → không mở hộp thư).</param>
/// <param name="VerifyEmailPassword">Mật khẩu hộp thư.</param>
public sealed record OrdersLoginParams(
    string User, string Pass, string? VerifyEmail, string? VerifyEmailPassword);

/// <summary>GĐ4: kết quả một VÒNG qua MỌI shop (<see cref="OrdersBridgeSession.RunAllShopsAsync"/>).</summary>
/// <param name="ShopCount">Số shop đọc được từ picker.</param>
/// <param name="ShopsDone">Số shop đã xử trọn trong vòng này.</param>
/// <param name="TotalOrders">Tổng số đơn đọc được (cộng qua các shop).</param>
/// <param name="TotalSlips">Tổng số phiếu PDF đã lưu.</param>
/// <param name="Captcha">True nếu dừng vòng vì captcha/verify.</param>
/// <param name="Error">Thông báo lỗi (null nếu vòng chạy trọn không lỗi).</param>
/// <param name="PickupAddressFailed">True nếu có ≥1 shop bị BỎ QUA vì KHÔNG đặt được địa chỉ lấy hàng
/// (chưa in phiếu cho shop đó; các shop khác trong vòng vẫn chạy) — caller cảnh báo ra kênh ngoài.
/// KHÁC <paramref name="Captcha"/>: nguyên nhân khác, xử khác.</param>
/// <param name="PickupFailedShop">Nhãn shop (hoặc nhiều shop nối bằng ", ") không đặt được địa chỉ
/// (null nếu không dính lỗi này).</param>
/// <param name="PickupOkShops">Nhãn shop (hoặc nhiều shop nối bằng ", ") ĐẶT ĐƯỢC địa chỉ lấy hàng trong vòng
/// này — caller dùng để TỰ GỠ banner lỗi địa chỉ cũ của đúng những shop đó. null = chưa shop nào chạy được bước
/// đặt địa chỉ (shop 0 đơn chờ lấy hàng KHÔNG tính: không chạy bước đó thì chưa chứng minh được gì).
/// ĐỘC LẬP với <paramref name="PickupFailedShop"/>: một vòng có thể vừa có shop lành vừa có shop hỏng.</param>
public sealed record OrdersBridgeRunResult(
    int ShopCount, int ShopsDone, int TotalOrders, int TotalSlips, bool Captcha, string? Error,
    bool PickupAddressFailed = false, string? PickupFailedShop = null, string? PickupOkShops = null);

/// <summary>Kết quả MỘT lượt mở trình duyệt sạch + SSO về trang chọn shop
/// (<see cref="OrdersBridgeSession.SsoVePickerAsync"/>).</summary>
internal enum KetQuaSso
{
    /// <summary>Đã về trang chọn shop (/portal/shop) — chạy tiếp được.</summary>
    Ok,

    /// <summary>Rơi vào trang verify/captcha — KHÔNG được đăng nhập lại, phải nghỉ vòng.</summary>
    Captcha,

    /// <summary>Extension TRẢ LỜI rõ ràng là không vào được, vì lý do KHÁC captcha: /account ra form đăng nhập
    /// (cookie hết hạn), SSO trượt, không thấy "Kênh Người bán", picker không render, hoặc gửi lệnh bị từ chối vì
    /// socket đã rớt. Đây là nhóm ĐÁNG đăng nhập lại — ca chính là cookie hết hạn.</summary>
    Loi,

    /// <summary>KHÔNG có phản hồi trong hạn chặng (extension chưa nối cầu / trang treo). Khác <see cref="Loi"/> ở
    /// chỗ ta KHÔNG biết đang ở đâu, nên KHÔNG đăng nhập lại: cookie hết hạn thì extension báo NGAY (nó kiểm
    /// <c>pageIsLoginForm</c> trước khi làm gì), nên treo gần như không bao giờ có nghĩa "cần đăng nhập lại".
    /// <para><b>Ca thật phải chặn:</b> tổng hạn phía extension của <c>gotoSellerCentre</c> (10s dò entry + 90s chờ
    /// tab banhang + 15s load + 30s ensure picker ≈ 145s) DÀI HƠN hạn chặng <c>AtSeller</c> (120s) — nên khi Shopee
    /// bày trang verify, C# có thể hết giờ TRƯỚC lúc extension kịp gửi <c>captcha</c>. Gộp ca này vào
    /// <see cref="Loi"/> là đi mở trình duyệt đăng nhập ngay giữa lúc bị nghi ngờ; bản trước khi đảo thứ tự để
    /// <c>TimeoutException</c> xuyên lên caller (nghỉ vòng) — giữ đúng hành vi đó.</para>
    /// <para><b>BẤT BIẾN mà luật này đứng lên</b> (phá là kẹt tài khoản vĩnh viễn): <c>ready</c> tới C# KHÔNG phụ
    /// thuộc trang đang mở — <c>background.js</c> gọi <c>bridge.connect()</c> ở TOP-LEVEL service worker, chạy ngay
    /// khi bản chép extension được nạp lúc phóng trình duyệt, độc lập với content script. Nhờ vậy "hết giờ Ready"
    /// luôn nghĩa là hạ tầng hỏng, KHÔNG BAO GIỜ nghĩa là "trang không phải Shopee ⇒ có thể đã logout". Nếu ai bỏ
    /// lời gọi connect top-level đó (để <c>ready</c> chỉ đến từ content script), phải xét lại
    /// <see cref="OrdersBridgeSession.QuyetDinhSauThuBanSach"/>: lúc ấy Treo có thể mang nghĩa cần đăng nhập lại
    /// thật, và nghỉ-vòng-mãi sẽ không bao giờ tự thoát.</para></summary>
    Treo,
}

/// <summary>Kết quả một lượt SSO kèm lý do (chỉ có khi <see cref="KetQuaSso.Loi"/>) để log + trả lên caller.</summary>
internal sealed record KetQuaSsoChiTiet(KetQuaSso Ket, string? LyDo);

/// <summary>Việc phải làm sau một lượt thử bản sạch — xem <see cref="OrdersBridgeSession.QuyetDinhSauThuBanSach"/>.</summary>
internal enum HanhDongSauThuSach
{
    /// <summary>Đã ở trang chọn shop → chạy tiếp vòng shop.</summary>
    ChayTiep,

    /// <summary>Captcha → kết thúc êm, caller kiểm <c>CaptchaSeen</c> rồi nghỉ vòng.</summary>
    DungVongCaptcha,

    /// <summary>Đóng bản sạch, đăng nhập lại bằng Playwright rồi mở bản sạch lần nữa.</summary>
    DangNhapLai,

    /// <summary>Đã đăng nhập lại mà vẫn không vào được → trả lỗi, chờ vòng sau.</summary>
    BaoLoi,
}

/// <summary>Việc phải làm sau khi MỘT shop hỏng giữa chừng (ngoại lệ ném ra từ thân shop) — xem
/// <see cref="OrdersBridgeSession.QuyetDinhSauShopHong"/>.</summary>
internal enum SauShopHong
{
    /// <summary>Bỏ shop hỏng, chạy tiếp shop kế — mặc định, vì hỏng thường chỉ của MỘT shop.</summary>
    ChayTiepShopKe,

    /// <summary>Hỏng LIÊN TIẾP chạm trần mà vẫn còn đường cứu (còn việc phía sau, còn lượt mở lại, không dính
    /// captcha) ⇒ ĐÓNG trình duyệt sạch, mở lại, SSO về picker rồi chạy tiếp phần CHƯA chạy của CHÍNH vòng này.
    /// <para>Vì sao mở lại chứ không dừng: điều tra nhật ký 8 ngày (11–17/08/2026) tìm ra 6 chuỗi lỗi
    /// "chờ 30s chưa thấy tab shop mở", mỗi chuỗi ĐÚNG 3 shop liên tiếp và giết trọn một vòng. Không chuỗi nào tự
    /// khỏi trong cùng vòng (0/6), nhưng vòng NGAY SAU — tức vòng chạy trên trình duyệt vừa mở lại — đi trọn 12/12
    /// shop, <b>6/6 lần</b>. Giá mở lại đo được là 5 giây (17:49:58 mở → 17:50:03 đã về trang chọn shop, cookie hồ
    /// sơ còn hạn nên bỏ qua đăng nhập Playwright); giá của việc dừng vòng là 90 phút chờ + 6–9 shop trắng.</para></summary>
    MoLaiTrinhDuyetHongLienTiep,

    /// <summary>Hỏng LIÊN TIẾP chạm trần và KHÔNG còn đường cứu (hết lượt mở lại / đang dính captcha / không còn
    /// việc phía sau) → dừng vòng, chờ vòng sau.</summary>
    DungVongHongLienTiep,

    /// <summary>Không đưa được về trang chọn shop mà CÒN shop phía sau ⇒ shop kế chắc chắn chết → dừng vòng.</summary>
    DungVongKhongVePicker,
}

/// <summary>Việc phải làm khi MẤT trang chọn shop giữa vòng — xem
/// <see cref="OrdersBridgeSession.QuyetDinhKhiMatPicker"/>.</summary>
internal enum KhiMatPicker
{
    /// <summary>Không còn shop nào chưa chạy ⇒ vòng đằng nào cũng xong, picker hỏng không hại ai.</summary>
    KetThucBinhThuong,

    /// <summary>Đóng trình duyệt sạch → mở lại → SSO về picker → chạy tiếp phần CHƯA chạy của CHÍNH vòng này.</summary>
    MoLaiTrinhDuyet,

    /// <summary>Hết đường (chạm trần mở lại, hoặc đang dính captcha) → dừng vòng, chờ vòng sau.</summary>
    DungVong,
}

/// <summary>Phần CHƯA chạy của vòng sau khi mở lại trình duyệt và đọc lại picker — xem
/// <see cref="OrdersBridgeSession.GhepShopConLai"/>.</summary>
/// <param name="ConLai">Shop chưa chạy, theo THỨ TỰ của danh sách vừa đọc lại.</param>
/// <param name="BienMat">Nhãn những shop chưa chạy mà danh sách mới KHÔNG còn (chỉ để ghi nhật ký).</param>
internal sealed record ShopConLaiSauMoLai(IReadOnlyList<ShopListItem> ConLai, IReadOnlyList<string> BienMat);


/// <summary>
/// <b>KHUÔN NGÀY dùng chung của mọi gói tham số gửi xuống extension.</b> Một mốc ngày luôn đi thành BỐN trường:
/// ba số rời để extension CHUYỂN THÁNG rồi click ô ngày, cộng một chuỗi <c>dd-MM-yyyy</c> để ĐỌC LẠI so bằng.
/// <para><b>Một khuôn, hai vòng, hai mốc</b> (26/08/2026): từ khi chương trình chạy một KHOẢNG thì mỗi gói mang
/// HAI mốc ngày, và cả <c>flow-km.js</c> lẫn <c>flow-combo.js</c> đọc chúng bằng cùng một hàm kiểm. Bốn bản chép
/// tay của cùng bốn trường là bốn cơ hội để một bản trôi lệch — mà triệu chứng là chương trình mọc sai ngày trên
/// shop thật, không phải một test đỏ.</para>
/// </summary>
internal static class KhuonNgayGoi
{
    /// <summary>PURE — bốn trường của MỘT mốc ngày.</summary>
    /// <param name="ngay">Mốc cần gói (phần giờ bị bỏ qua — giờ đi riêng ở trường <c>gio</c>).</param>
    public static object Goi(DateTime ngay) => new
    {
        ngayTrongThang = ngay.Day,
        // Tháng + năm để extension CHUYỂN THÁNG trên picker trước khi click ô ngày: gửi mỗi `ngayTrongThang`
        // thì đặt 25-09 sẽ click trúng 25-08 của tháng đang hiện.
        thang = ngay.Month,
        nam = ngay.Year,
        // InvariantCulture: chuỗi này được extension so BẰNG với text đọc lại trên picker. Máy đặt lịch khác
        // (vd Thái/Ả Rập) thì ToString mặc định ra chữ số/lịch khác ⇒ bước 4/5 đỏ trên đúng những máy đó, mà
        // không ai lần ra được vì sao.
        ngayDayDu = ngay.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// Tham số một lượt chạy <b>"Chương Trình Của Shop"</b> — do form trên App dựng, Core chỉ dùng, không tự đoán.
/// <para><b>MỘT KHOẢNG, KHÔNG PHẢI MỘT NGÀY</b> (người dùng đính chính 26/08/2026): chương trình chạy từ
/// <paramref name="NgayBatDau"/> tới <paramref name="NgayKetThuc"/>, <b>cùng một giờ</b>
/// (<paramref name="Gio"/>) ở hai đầu. Trường <c>Ngay</c> + <c>GioKetThuc</c> của bản cũ đã XOÁ HẲN: giữ lại là
/// mời người sau dùng nhầm một cái tên chỉ còn đúng một nửa.</para>
/// <para><b>DỰNG MỘT LẦN RỒI MỌI THỨ ĐỌC TỪ CHÍNH NÓ — kể cả dòng nhật ký</b> (bài học lỗ phủ 25/08/2026, xem
/// <c>plans/2026-08-25-km-dat-truoc-180-ngay.md</c>): giữ biến rời rồi log một đằng gửi một nẻo thì dòng log
/// trở thành một chuỗi song song TỰ NHẤT QUÁN, và đổi thứ đi xuống Shopee không làm đỏ test nào.
/// <see cref="MoTa"/> và <see cref="Goi"/> đều đọc từ đúng object này.</para>
/// </summary>
/// <param name="Ten">Tên chương trình (tối đa 150 ký tự, đã cắt sẵn ở tầng form).</param>
/// <param name="NgayBatDau">Ngày MỞ ĐẦU khoảng (<c>KmLichTuDong.NgayBatDau</c>).</param>
/// <param name="NgayKetThuc">Ngày ĐÓNG khoảng (<c>KmLichTuDong.NgayKetThuc</c>).</param>
/// <param name="Gio">"HH:mm" — DÙNG CHO CẢ HAI ĐẦU của khoảng.</param>
/// <param name="PhanTram">% giảm.</param>
public sealed record ThamSoKm(string Ten, DateTime NgayBatDau, DateTime NgayKetThuc, string Gio, int PhanTram)
{
    /// <summary>PURE — dòng MÔ TẢ chương trình cho nhật ký, đọc từ CHÍNH gói này. Mọi nơi cần nói "lượt KM sắp
    /// tạo cái gì" phải gọi hàm này, đừng ghép chuỗi từ biến rời.</summary>
    public string MoTa()
        => $"\"{Ten}\" · {NgayBatDau.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)} {Gio}"
           + $" → {NgayKetThuc.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)} {Gio}"
           + $" · giảm {PhanTram}%";

    /// <summary>
    /// GÓI gửi xuống extension: HAI mốc ngày (<c>bd</c>/<c>kt</c>, khuôn <see cref="KhuonNgayGoi"/>) + MỘT
    /// <c>gio</c>.
    /// <para>Người nhận phía extension: <c>flow-km.js</c> (<c>docThamSoKm</c>) — nó KIỂM lại đủ mọi trường và
    /// HỎNG NGAY ở "bước 0" nếu thiếu/sai, thay vì lấy giá trị mặc định. Đổi tên trường ở đây là phải đổi cả bên
    /// đó.</para>
    /// </summary>
    public object Goi() => new
    {
        ten = Ten,
        bd = KhuonNgayGoi.Goi(NgayBatDau),
        kt = KhuonNgayGoi.Goi(NgayKetThuc),
        gio = Gio,
        phanTram = PhanTram,
    };

    /// <summary>PURE — MỐC BẮT ĐẦU thật của chương trình (<c>NgayBatDau</c> + <c>Gio</c>); <c>null</c> khi
    /// <see cref="Gio"/> sai định dạng. Xem <see cref="ThamSoCombo.MocBatDau"/> cho lý do tồn tại.</summary>
    public DateTime? MocBatDau()
        => KmLichTuDong.DocGio(Gio) is TimeSpan t ? NgayBatDau.Date.Add(t) : null;
}

/// <summary>
/// Tham số một lượt chạy <b>"Combo Khuyến Mãi"</b> — chương trình thứ HAI của "KM của Shop" (25/08/2026).
/// <para><b>DỰNG MỘT LẦN RỒI MỌI THỨ ĐỌC TỪ CHÍNH NÓ — kể cả dòng nhật ký</b> (bài học lỗ phủ 25/08/2026, xem
/// <c>plans/2026-08-25-km-dat-truoc-180-ngay.md</c>): giữ biến rời rồi log một đằng gửi một nẻo thì dòng log
/// trở thành một chuỗi song song TỰ NHẤT QUÁN, và đổi thứ đi xuống Shopee không làm đỏ test nào.
/// <see cref="MoTa"/> và <see cref="Goi"/> đều đọc từ đúng object này.</para>
/// </summary>
/// <param name="Ten">Tên chương trình — <b>tối đa 25 ký tự</b>, đã cắt sẵn ở tầng form
/// (<c>KmDialogViewModel.CatTenCombo</c>). Ô trên Shopee mang <c>maxlength="25"</c>.</param>
/// <param name="NgayBatDau">Ngày MỞ ĐẦU khoảng (<c>KmLichTuDong.NgayBatDau</c>).</param>
/// <param name="NgayKetThuc">Ngày ĐÓNG khoảng (<c>KmLichTuDong.NgayKetThuc</c>).</param>
/// <param name="Gio">"HH:mm" — DÙNG CHO CẢ HAI ĐẦU của khoảng, y <see cref="ThamSoKm"/>.</param>
/// <param name="Loai">2 = giảm %, 3 = giảm SỐ TIỀN, 1 = giá đặc biệt (đúng <c>value</c> của ô radio).</param>
/// <param name="MuaSl">Mức ưu đãi — mua bao nhiêu sản phẩm (<c>tiers.0.minAmount</c>).</param>
/// <param name="Giam">Mức giảm: ĐỒNG nếu <paramref name="Loai"/> = 3, PHẦN TRĂM nếu = 2.</param>
/// <param name="GioiHan">Giới hạn đặt hàng (<c>usageLimit</c>).</param>
public sealed record ThamSoCombo(
    string Ten, DateTime NgayBatDau, DateTime NgayKetThuc, string Gio,
    int Loai, int MuaSl, int Giam, int GioiHan)
{
    /// <summary>PURE — dòng MÔ TẢ chương trình cho nhật ký, đọc từ CHÍNH gói này. Mọi nơi cần nói "lượt combo
    /// sắp tạo cái gì" phải gọi hàm này, đừng ghép chuỗi từ biến rời.</summary>
    public string MoTa()
        => $"\"{Ten}\" · {NgayBatDau.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)} {Gio}"
           + $" → {NgayKetThuc.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)} {Gio}"
           + $" · {MoTaUuDai()} · giới hạn đặt hàng {GioiHan}";

    /// <summary>
    /// <b>Cách viết SỐ TIỀN trong nhật ký tiếng Việt</b>: dấu CHẤM phân nhóm nghìn — <c>2.000đ</c>.
    /// <para><b>Vì sao không dùng <see cref="CultureInfo.InvariantCulture"/></b> (phản biện 25/08/2026, NHẸ-8):
    /// Invariant phân nhóm bằng dấu PHẨY nên dòng log tiếng Việt in <i>"giảm 2,000đ"</i> — người đọc quen mốc
    /// Việt hiểu thành 2 phẩy 000. Cũng KHÔNG dùng culture của máy: nhật ký phải đọc giống nhau trên mọi máy
    /// trong nhóm, không phụ thuộc máy nào đang đặt lịch gì. Đây chỉ là cách VIẾT cho người đọc; gói gửi xuống
    /// extension (<see cref="Goi"/>) vẫn là số nguyên thô, không đụng tới.</para>
    /// </summary>
    private static readonly NumberFormatInfo DinhDangTienVn =
        new() { NumberGroupSeparator = ".", NumberGroupSizes = [3], NumberDecimalDigits = 0 };

    /// <summary>PURE — vế "mua N giảm X" viết đúng theo <see cref="Loai"/>.</summary>
    public string MoTaUuDai() => Loai switch
    {
        2 => $"mua {MuaSl} sp giảm {Giam}%",
        3 => $"mua {MuaSl} sp giảm {Giam.ToString("N0", DinhDangTienVn)}đ",
        _ => $"mua {MuaSl} sp, giá đặc biệt {Giam.ToString("N0", DinhDangTienVn)}",
    };

    /// <summary>
    /// GÓI gửi xuống extension. Khuôn ngày y hệt <see cref="ThamSoKm"/>: HAI mốc <c>bd</c>/<c>kt</c> theo
    /// <see cref="KhuonNgayGoi"/> + MỘT <c>gio</c>.
    /// <para>Người nhận phía extension: <c>flow-combo.js</c> (<c>doChayCombo</c>, chặng B xong 26/08/2026) —
    /// <c>docThamSoCombo</c> ở đó KIỂM lại đủ mọi trường và HỎNG NGAY ở "bước 0" nếu thiếu/sai, thay vì lấy giá
    /// trị mặc định. Đổi tên trường ở đây là phải đổi cả bên đó.</para>
    /// </summary>
    public object Goi() => new
    {
        ten = Ten,
        bd = KhuonNgayGoi.Goi(NgayBatDau),
        kt = KhuonNgayGoi.Goi(NgayKetThuc),
        gio = Gio,
        loai = Loai,
        muaSl = MuaSl,
        giam = Giam,
        gioiHan = GioiHan,
    };

    /// <summary>
    /// PURE — MỐC BẮT ĐẦU thật của chương trình: <see cref="NgayBatDau"/> + <see cref="Gio"/>; <c>null</c> khi
    /// giờ sai định dạng (cửa kiểm ở tầng form đã có câu lỗi riêng cho ca đó).
    /// <para><b>Vì sao gói phải tự nói được mốc này</b> (phản biện 26/08/2026, lỗi NẶNG): từ 26/08 mốc bắt đầu
    /// là <i>hôm nay lúc Gio</i> — một HẠN CHÓT ĐỒNG HỒ trôi qua NGAY TRONG lượt chạy. Vòng lặp phải kiểm lại
    /// nó trước MỖI shop, và thứ duy nhất vòng lặp cầm trong tay là gói này.</para>
    /// </summary>
    public DateTime? MocBatDau()
        => KmLichTuDong.DocGio(Gio) is TimeSpan t ? NgayBatDau.Date.Add(t) : null;
}

/// <summary>
/// Tham số một lượt chạy <b>"Flash Sale Của Shop"</b> — chương trình thứ BA (28/08/2026).
///
/// <para><b>Gói này KHÁC HẲN hai gói kia và đó là điểm chính:</b> Flash Sale không có tên/khoảng ngày/mức giảm.
/// Toàn bộ thông số của nó là <b>MỘT NGÀY ĐÍCH</b> — Shopee tự phát hành khung giờ cho ngày đó, luồng chỉ việc
/// nhặt hết khung chưa dùng.</para>
///
/// <para><b>⚠ <paramref name="NgayDich"/> phải được tính ĐÚNG MỘT LẦN lúc bắt đầu lượt</b>
/// (<c>KmLichTuDong.NgayDichFlash</c>) rồi rót vào gói này cho MỌI shop — xem xmldoc ở đó cho ca hỏng khi lượt
/// vắt qua nửa đêm. Gói bất biến (<c>record</c>) chính là thứ làm điều đó khả thi: dựng một lần, không ai đọc
/// lại đồng hồ được nữa.</para>
///
/// <para><b>KHÔNG có trần số khung (03/09/2026)</b> — người dùng chốt <i>"bỏ trần 8 khung, lấy hết khung Shopee
/// mở"</i>. Trần cũ <c>SoKhungToiDa = 8</c> đã gỡ khỏi cả gói này lẫn extension: số khung là thứ SHOPEE quyết,
/// vòng bên extension dừng khi hết khung chọn được. Chốt chống vòng vô tận nằm lại ở
/// <c>TRAN_VONG_KHUNG_KY_THUAT</c> của <c>flow-flashsale.js</c>, không phải ở gói tham số.</para>
/// </summary>
/// <param name="NgayDich">Ngày sẽ đặt Flash Sale — ngày BẮT ĐẦU LƯỢT + 1.</param>
/// <param name="PhanTramGiam">%GIẢM áp HÀNG LOẠT cho mọi sản phẩm của mọi khung (ô thứ nhất của khối "Chỉnh sửa
/// hàng loạt" trên form flash Shopee). Nguồn là <c>km_config.flash_phan_tram_giam</c>, người dùng gõ trên form
/// "KM của Shop" → tab Flash Sale.
/// <para><b>KHÔNG có giá trị mặc định, cố ý</b> (03/09/2026): trước đó con số này là hằng <c>5</c> nằm trong
/// <c>flow-flashsale.js</c>. Một mặc định ở đây là mọi chỗ gọi quên truyền đều lặng lẽ tụt về đúng cái hằng vừa
/// gỡ, và người dùng đổi số trên form mà Shopee vẫn nhận số cũ — không lỗi, không log.</para></param>
/// <param name="SoLuongThamGia">"SL sản phẩm khuyến mãi" áp HÀNG LOẠT — ô thứ HAI của cùng khối "Chỉnh sửa hàng
/// loạt", tức số lượng MỖI sản phẩm được bán trong khung. <b>Không phải</b> số sản phẩm mỗi khung (vẫn 1 trang
/// 10 SP bán chạy). Cũng KHÔNG có mặc định — cùng lý do với <paramref name="PhanTramGiam"/>.</param>
public sealed record ThamSoFlashSale(DateTime NgayDich, int PhanTramGiam, int SoLuongThamGia)
{
    /// <summary>PURE — dòng MÔ TẢ cho nhật ký, đọc từ CHÍNH gói này (cùng luật "một object, một nguồn" với
    /// <see cref="ThamSoCombo.MoTa"/>: ghép chuỗi từ biến rời là dòng log thành một sự thật song song).</summary>
    public string MoTa()
        => $"ngày đích {NgayDich.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)}"
           + " · lấy HẾT khung giờ Shopee mở cho ngày đó"
           + $" · giảm {PhanTramGiam}% · SL tham gia {SoLuongThamGia}/SP";

    /// <summary>
    /// GÓI gửi xuống extension. <b>Ba trường ngày RỜI NHAU</b> (<c>ngay</c>/<c>thang</c>/<c>nam</c>), không phải
    /// một chuỗi: lịch của Shopee được lái bằng SỐ NGÀY trong ô và bằng cặp tháng/năm trên header picker, nên
    /// gửi chuỗi xuống thì extension lại phải tách ra — và một phép tách là một chỗ hỏng nữa.
    /// <para><b>NĂM trường từ 03/09/2026</b>: <c>soKhungToiDa</c> đã gỡ cùng trần 8 khung, nhưng thêm
    /// <c>phanTramGiam</c> + <c>soLuongThamGia</c> — hai con số trước đây là hằng chết trong extension. Extension
    /// kiểm chúng FAIL-CLOSED y như ngày/tháng/năm (<c>docThamSoFlash</c>), nên một bản client cũ không gửi sẽ
    /// dừng ở bước giả "0. Tham số" chứ KHÔNG lặng lẽ rơi về 5%/10.</para>
    /// </summary>
    public object Goi() => new
    {
        ngay = NgayDich.Day,
        thang = NgayDich.Month,
        nam = NgayDich.Year,
        phanTramGiam = PhanTramGiam,
        soLuongThamGia = SoLuongThamGia,
    };
}

/// <summary>Kết quả KM của MỘT shop.</summary>
/// <param name="ShopId">Id shop.</param>
/// <param name="ShopName">Tên shop (để ghi nhật ký cho người đọc).</param>
/// <param name="DaTao">Đã tạo được chương trình.</param>
/// <param name="SoSanPham">Số sản phẩm được nạp vào chương trình.</param>
/// <param name="BoQua">Lý do bỏ qua (shop rỗng…), <c>null</c> nếu không bỏ qua.</param>
/// <param name="Loi">Lỗi khiến shop này hỏng, <c>null</c> nếu không.</param>
/// <param name="CanhBao">Cảnh báo của shop này: chương trình VẪN tạo được nhưng có thứ đáng ngờ — chạm trần
/// phân trang (còn sản phẩm chưa chọn), hoặc extension báo tạo được mà vẫn kèm bước hỏng. Khác <see cref="Loi"/>
/// ở chỗ nó KHÔNG làm shop này thành hỏng; <c>null</c> = sạch.</param>
/// <param name="Captcha">Extension khai TRONG GÓI KẾT QUẢ là đã rơi trang <c>/verify</c> giữa luồng. Lớp thứ hai
/// bên cạnh gói <c>{action:"captcha"}</c> (vốn đi đường riêng và có thể tới sau lượt đọc kết quả) — hai đường
/// cùng nói một chuyện thì mất một đường vẫn còn đường kia.</param>
/// <param name="NgayMaiKhongDo"><b>Riêng luồng FLASH SALE</b>: extension khai rằng NGÀY ĐÍCH không còn ĐỎ trên
/// lịch — Shopee chưa mở khung cho ngày đó, <b>HOẶC chính shop này đã kín khung</b>.
/// <para><b>⚠ ĐÂY KHÔNG PHẢI KẾT LUẬN MỨC NGÀY, đừng đọc nó như vậy</b> (sổ P-20260831-3, xử 01/09/2026). Bản
/// trước tin rằng "lịch khung là của SHOPEE nên một shop khai là đủ dừng cả lượt"; đo 16:31→16:33 ngày 31/08
/// bác bỏ: lịch MẤT ĐỎ cả khi chỉ riêng shop đó hết khung trống. Shop khai cờ này được xử như một
/// <paramref name="BoQua"/> bình thường; kết luận mức ngày là <see cref="KetQuaKm.NgayMaiKhongDo"/>.</para></param>
/// <param name="SoKhungDaTao">Số khung TẠO MỚI được ở shop này (0 nếu không tạo được gì).</param>
/// <param name="SoTaiChe">Số chương trình TÁI CHẾ (bước K0 dựng lại phần đã nở) ở shop này.
/// <para>Trường này từng bị RƠI ở gói extension dù luồng vẫn đếm — mà <c>daTao</c> tính cả nó, nên nhật ký đọc
/// ra "tạo 0 khung" cho một shop đã làm việc thật (sổ P-20260831-5). Nối lại 01/09/2026.</para></param>
/// <param name="ShopLogin"><b>KHÓA</b> của shop (<see cref="OrdersBridgeSession.KhoaShop"/>) — <c>LoginName</c>
/// ưu tiên, thiếu thì <c>ShopName</c>. Rỗng/null chỉ ở dữ liệu dựng tay trong test.
/// <para>Thêm 01/09/2026 vì tiến độ flash trong ngày (tập shop ĐÃ XONG, giữ in-memory ở
/// <c>AccountsViewModel</c>) phải khóa bằng ĐÚNG thứ mà bộ lọc <c>boQuaShop</c> so — và
/// <paramref name="ShopName"/> KHÔNG phải thứ đó: shop có tên hiển thị khác login sẽ không bao giờ khớp, nên
/// gói sau mở lại đúng shop vừa xong, câm lặng.</para></param>
public sealed record KetQuaKmMotShop(
    string ShopId, string ShopName, bool DaTao, int SoSanPham, string? BoQua, string? Loi,
    string? CanhBao = null, bool Captcha = false, bool NgayMaiKhongDo = false,
    int SoKhungDaTao = 0, int SoTaiChe = 0, string? ShopLogin = null);

/// <summary>Kết quả cả lượt KM.</summary>
/// <param name="Shops">Kết quả từng shop theo thứ tự chạy.</param>
/// <param name="Captcha">Rơi captcha ⇒ dừng vòng giữa chừng.</param>
/// <param name="Loi">Lỗi làm dừng cả lượt, <c>null</c> nếu chạy hết.</param>
/// <param name="HetKhung">Lượt TỰ ĐỘNG dừng vì đã tới hạn chót 06:00 — <b>không phải lỗi</b>, nên tách khỏi
/// <paramref name="Loi"/>: câu tổng kết phải nói đúng "hết khung" chứ không phải "dừng giữa chừng vì lỗi".</param>
/// <param name="SoShopChuaChay">Số shop chưa kịp chạy khi hết khung (0 nếu chạy hết).</param>
/// <param name="TranThuDaCham">Lượt này bị <b>TRẦN CHẾ ĐỘ THỬ</b> cắt ngang — giá trị là chính con số trần đang
/// áp (<see cref="KmLichTuDong.TranShopLuotCombo"/>); <c>0</c> = không bị cắt.
/// <para>Mang cả CỜ lẫn SỐ trong một trường để câu tổng kết nói được <i>"trần 2 shop"</i> mà không phải đọc lại
/// hằng — dòng log đọc từ chính kết quả của lượt là dòng log không trôi lệch được.</para>
/// <para><b>Tách hẳn khỏi <paramref name="HetKhung"/> và <paramref name="Captcha"/></b>: ba lý do dừng sớm,
/// người đọc nhật ký phải phân biệt được ngay đâu là hỏng và đâu là chế độ thử.</para></param>
/// <param name="MocBatDauDaTroiQua">Lượt này DỪNG vì <b>mốc bắt đầu của chính chương trình</b> đã trôi qua giữa
/// vòng — giá trị là chính cái mốc đó; <c>null</c> = không phải lý do này.
/// <para><b>LÝ DO DỪNG SỚM THỨ TƯ</b> (phản biện 26/08/2026, lỗi NẶNG). Mốc bắt đầu nay là <i>hôm nay lúc
/// GioBatDau</i>: một hạn chót đồng hồ trôi qua NGAY TRONG lượt chạy, mà cửa kiểm ở tầng form/nhịp chỉ chạy MỘT
/// LẦN, trước vòng. Khi nó trôi qua giữa vòng thì picker Shopee vô hiệu hoá mọi mốc lùi giờ ⇒ mọi shop còn lại
/// đỏ với câu <c>"li '14' đang disabled"</c> — một câu trông y hệt lỗi selector, nên người đọc nhật ký đi sửa
/// DOM cho một sự cố về ĐỒNG HỒ. Mang cả cờ lẫn MỐC trong một trường (y khuôn <paramref name="TranThuDaCham"/>)
/// để câu tổng kết nói được đúng con số mà không phải dựng lại từ chỗ khác.</para></param>
/// <param name="NgayMaiKhongDo"><b>BÁO CÁO MỨC NGÀY của vòng FLASH SALE</b>: <b>MỌI shop đã chạy trong lượt
/// này</b> đều khai <c>ngayMaiKhongDo</c> mà KHÔNG shop nào tạo được gì. Lượt rỗng ⇒ <c>false</c>.
/// <para><b>⚠ CHỈ ĐỂ BÁO CÁO — KHÔNG cửa nào đọc cờ này để dừng việc</b> (chốt 01/09/2026, phản biện vòng 1).
/// Bản đầu của đợt dùng nó để ĐÓNG CỬA cả ngày ở tầng nhịp; đã gỡ, vì cờ này chỉ nói được "mọi shop ĐÃ CHẠY của
/// GÓI này không còn đỏ" — mà một gói là ~2 shop. Một shop kín khung cộng một lượt <c>vePicker</c> hỏng, hoặc
/// một gói toàn shop-đã-kín bị hạn cắt, là đủ dựng ra cờ này; giá của một lần sai-dương là MẤT TRỌN ngày flash,
/// trong khi cái nó định tiết kiệm (vài gói thừa) đã có giãn cách 30' và trần gói/ngày lo.</para>
/// <para><b>⚠ NGHĨA ĐÃ ĐỔI 01/09/2026</b> (sổ P-20260831-3). Trước đó: "một shop khai là dừng cả lượt ngay ở
/// shop đầu" — sai, vì lịch mất đỏ cả khi chỉ RIÊNG shop đó kín khung, nên một shop kín giết oan 11 shop còn
/// lại.</para>
/// <para>Tách hẳn khỏi <paramref name="Loi"/> vì nó KHÔNG phải lỗi — nhưng trong câu tổng kết nó xếp SAU
/// <paramref name="Loi"/>: một lượt vừa hỏng vừa mang cờ này phải nói ra cái HỎNG trước.</para></param>
/// <param name="HanChotDaCham">HẠN THẬT đã chặn lượt này ở nhánh <paramref name="HetKhung"/> — chính
/// <c>hanChot</c> mà nơi gọi rót xuống; <c>null</c> = không phải lý do này (hoặc lượt không có hạn).
/// <para><b>Vì sao phải mang theo</b> (sổ P-20260903-7): câu tổng kết in giờ ĐÓNG KHUNG của loại chương trình
/// (<c>KmLichTuDong.Khung(loai).Dong</c>) — với vòng flash là <c>23:00</c>. Nhưng từ 01/09/2026 một gói flash
/// bị chặn bởi <b>hạn của GÓI</b> = <c>min(cuối khe − 45', 22:15)</c>, tức 18:07 chẳng hạn. Nhật ký ghi "DỪNG vì
/// hết khung tự động (23:00)" lúc 18:07 là gửi người đọc đi tìm một mốc không liên quan — và đó là dòng DUY
/// NHẤT nói ra vì sao gói chỉ làm được một shop.</para></param>
/// <param name="NhuongChoCheckDon"><b>Lượt này DỪNG vì NHƯỜNG CHỖ cho một lượt chạy lại check đơn đang chờ</b>
/// (<c>AccountsViewModel.ChayLaiCheckDonSauKhiChoAsync</c> bật cờ, vòng shop thấy cờ thì thôi bắt đầu shop mới).
/// <c>false</c> = không phải lý do này.
/// <para><b>LÝ DO DỪNG SỚM THỨ NĂM</b> (03/09/2026, sổ P-20260903-1) — và KHÔNG phải lỗi. Cùng hình dạng kết quả
/// với <paramref name="HetKhung"/>: shop còn lại đếm vào <paramref name="SoShopChuaChay"/> nên ngày KHÔNG bị
/// tính là trọn, gói sau làm tiếp.</para>
/// <para>Trong câu tổng kết nó xếp <b>SAU <paramref name="Loi"/></b> — cùng bài học 01/09/2026 với
/// <paramref name="NgayMaiKhongDo"/>: một lượt vừa nhường vừa hỏng thật phải nói ra cái HỎNG trước.</para></param>
public sealed record KetQuaKm(
    IReadOnlyList<KetQuaKmMotShop> Shops, bool Captcha, string? Loi,
    bool HetKhung = false, int SoShopChuaChay = 0, int TranThuDaCham = 0,
    DateTime? MocBatDauDaTroiQua = null, bool NgayMaiKhongDo = false,
    DateTime? HanChotDaCham = null, bool NhuongChoCheckDon = false)
{
    /// <summary>Số shop đã tạo được chương trình.</summary>
    public int SoTao => Shops.Count(s => s.DaTao);

    /// <summary>Số shop bỏ qua vì không có sản phẩm.</summary>
    public int SoBoQua => Shops.Count(s => s.BoQua is not null);

    /// <summary>Số shop hỏng.</summary>
    public int SoHong => Shops.Count(s => s.Loi is not null);

    /// <summary>
    /// TỔNG số shop của lượt — đã chạy cộng chưa kịp chạy. Đây mới là MẪU SỐ đúng cho câu "tạo N/M shop".
    /// <para><b>Vì sao không dùng thẳng <c>Shops.Count</c></b> (phản biện vòng 3, 19/08/2026): hết khung ở shop
    /// ĐẦU thì <c>Shops</c> rỗng, và câu tổng kết ra "tạo 0/0 shop … còn 12 shop chưa chạy" — một mẫu số tự mâu
    /// thuẫn với chính vế sau của nó. Lượt chạy trọn thì <see cref="SoShopChuaChay"/> bằng 0 nên số không đổi.</para>
    /// </summary>
    public int TongShop => Shops.Count + SoShopChuaChay;
}

/// <summary>
/// Vòng đời MỘT phiên cầu nối: mở cổng loopback (<see cref="OrdersBridgeChannel"/>) → mở trình duyệt SẠCH
/// (không CDP, không remote-debugging-port) qua <see cref="OrdersBridgeLauncher"/> với <c>startUrl</c> có hash
/// <c>#_od_ws=&lt;port&gt;</c> để extension đọc cổng → chờ extension báo <c>ready</c> → đăng nhập → lặp shop.
/// <list type="bullet">
/// <item><see cref="RunLoginThenSliceAsync"/> (GĐ2 pivot): đăng nhập bằng trình duyệt điều khiển Playwright
/// (tái dùng luồng production) → đóng → mở lại bằng trình duyệt sạch + extension → lát cắt Seller Centre.</item>
/// <item><see cref="RunAllShopsAsync"/> (GĐ4): y hệt phần đầu rồi LẶP QUA MỌI shop.</item>
/// </list>
/// Parse dữ liệu qua các hàm THUẦN sẵn có (<see cref="ShopeeLoginService.ParseShopListJson"/>,
/// <see cref="ShopeeDashboard.ParseToShipCount"/>); mọi việc BÊN TRONG một shop nằm ở <see cref="ShopFlowRunner"/>.
/// <para>Một phiên/lần chạy (chưa đa-lane) — cổng cầu nối là hằng <see cref="OrdersBridgeChannel.BridgePort"/>.</para>
/// </summary>
public sealed class OrdersBridgeSession : IDisposable
{
    private readonly string _userDataDir;
    private readonly Action<string>? _log;
    private readonly string _province;
    // Tab "Shops": callback do App rót (Core không ref App/DB) — CHỈ THÊM lời gọi, không đổi luồng. Null-safe.
    //  · _onShopListRead: gọi ngay sau khi parse xong danh sách shop → App lưu account_shops (mọi shop, kể cả 0 đơn).
    //  · _onShopCheckStarted/_onShopCheckFinished: cột tiến độ — bắt đầu/xong MỘT shop (nhãn shop = khóa prepare_daily).
    private readonly Action<IReadOnlyList<ShopListItem>>? _onShopListRead;
    private readonly Action<string>? _onShopCheckStarted;
    private readonly Action<string>? _onShopCheckFinished;
    //  · _onShopChuongTrinh: ba cột KM | Combo | Flash Sale — bắt đầu/xong MỘT shop của vòng TẠO CHƯƠNG TRÌNH.
    private readonly Action<LoaiChuongTrinh, string, bool, int?>? _onShopChuongTrinh;
    // Vòng check đơn: mốc shop hoàn tất gần nhất (để khi chạy lại tiếp tục từ shop kế tiếp). Null-safe.
    private readonly Func<string?>? _layShopDaCheckCuoi;
    private readonly Action<string?>? _luuShopDaCheckCuoi;
    // GĐ4: khoảng nghỉ ngẫu nhiên giữa các shop (ms) — kiểu người, tránh dồn dập.
    private readonly Random _rng = new();

    /// <summary>Khoảng nghỉ "kiểu người" giữa hai shop — cận DƯỚI. Rút ngắn nữa là vòng chạy dồn dập bất thường
    /// so với người thật đang bấm tay trên Seller Centre.</summary>
    private static readonly TimeSpan NghiGiuaShopToiThieu = TimeSpan.FromMinutes(3);

    /// <summary>Khoảng nghỉ "kiểu người" giữa hai shop — cận TRÊN. Nới nữa là một vòng 12 shop không kịp gọn
    /// trong nhịp chạy của app.</summary>
    private static readonly TimeSpan NghiGiuaShopToiDa = TimeSpan.FromMinutes(5);

    // Kênh lệnh (WebSocket + các chặng chờ) và flow bên trong một shop — dựng một lần, dùng lại qua các vòng.
    private readonly OrdersBridgeChannel _channel;
    private readonly ShopFlowRunner _flow;

    /// <summary>Tiến trình trình duyệt sạch đã mở (để tầng UI theo dõi/kill). Set ngay sau khi launch.
    /// <para><c>internal set</c> chỉ để TEST cấp một tiến trình rẻ tiền THẬT (vd <c>cmd /c timeout</c>) — không
    /// có nó thì không test nào dựng nổi trạng thái "lát cắt xong nhưng cửa sổ CÒN SỐNG", tức luật
    /// giữ-khoá-tới-lúc-trả và nhả-khoá-cũ-trước-khi-mượn-lại nằm ngoài tầm kiểm chứng. App KHÔNG gán được
    /// (khác assembly).</para></summary>
    public System.Diagnostics.Process? Process { get; internal set; }

    /// <param name="invoiceDir">GĐ3 Phần B: thư mục lưu phiếu giao PDF; null/rỗng → chỉ đọc đơn, không tải phiếu.</param>
    /// <param name="province">GĐ3 Phần B: tỉnh của địa chỉ lấy hàng cần đặt (mặc định "Thanh Hóa").</param>
    /// <param name="syncCallback">GĐ4: gọi SAU khi đọc xong đơn mỗi shop — App lưu DB/GSheet/hub, kèm tên shop. null → chỉ log.</param>
    /// <param name="finalDoneSns">GĐ4: tập <c>order_sn</c> ĐÃ có "Số tiền cuối cùng" trong DB (App rót) — đơn nằm trong
    /// tập này KHÔNG mở lại chi tiết. null → không lọc (mở chi tiết cho MỌI đơn pending chưa có final).</param>
    /// <param name="onShopListRead">Tab "Shops": gọi ngay sau khi parse xong danh sách shop → App lưu account_shops. null → bỏ qua.</param>
    /// <param name="onOrderPrepared">Tab "Shops": gọi mỗi khi chuẩn bị xong 1 đơn (tham số = nhãn shop, MÃ ĐƠN
    /// <c>order_sn</c>) → App +1 đếm ngày + đánh dấu đơn đó đã chuẩn bị hàng (để đẩy lên hub). null → bỏ qua.</param>
    /// <param name="onShopCheckStarted">Tab "Shops" (cột tiến độ): gọi NGAY khi bắt đầu xử một shop (tham số = nhãn
    /// shop, ĐÚNG khóa <c>prepare_daily</c>) → App chuyển chấm sang shop đó + bật vòng quay. null → bỏ qua.</param>
    /// <param name="onShopCheckFinished">Tab "Shops" (cột tiến độ): gọi khi XONG shop đó — kể cả shop lỗi/captcha/bỏ
    /// qua (gọi trong <c>finally</c>) → App tắt vòng quay nhưng GIỮ chấm ở shop này tới khi shop kế bắt đầu. null → bỏ qua.</param>
    /// <param name="returnCountLast">Check đơn trả hàng: mốc "số yêu cầu" lần check TRƯỚC của shop (tham số = nhãn shop);
    /// null trả về = shop chưa từng check → lượt này CHỈ ghi nhớ số. Callback null → BỎ HẲN bước check trả hàng.</param>
    /// <param name="saveReturnCount">Check đơn trả hàng: ghi mốc mới cho shop (nhãn shop, số vừa đọc). null → bỏ hẳn bước.</param>
    /// <param name="saveReturnCodes">Check đơn trả hàng: lưu các cặp (mã đơn, mã yêu cầu) vào đơn tương ứng; trả chuỗi
    /// tóm tắt để phiên log. null → bỏ hẳn bước.</param>
    /// <param name="layDonThieuPhieu">Tự tải lại phiếu thiếu (bước BÙ cuối flow shop): tham số = nhãn shop; App trả
    /// danh sách <c>order_sn</c> của ĐÚNG shop đó đang có mã vận đơn nhưng thiếu file PDF hợp lệ, xếp MỚI NHẤT
    /// TRƯỚC. null → BỎ HẲN bước (đường "Chạy thử" chỉ đọc, không lưu, nên không rót).</param>
    /// <param name="dangCoCanhBaoDiaChi">Shop này (tham số = nhãn shop) có đang treo BANNER lỗi địa chỉ trên tab
    /// Shops không. Shop đang treo banner mà lượt này không có đơn Chờ Lấy Hàng thì vẫn chạy riêng bước đặt địa chỉ,
    /// để banner có đường TỰ hết (xem <c>ShopFlowRunner.QuyetDinhBuocDiaChi</c>). null → coi như không shop nào có
    /// banner ⇒ hành vi y hệt trước 10/08/2026.</param>
    /// <param name="conSotTraHang">Lượt check trả hàng TRƯỚC của shop này (tham số = nhãn shop) có biết là còn sót
    /// không (<c>account_shops.tra_hang_con_sot</c>). Bật ⇒ lượt này vẫn lật trang dù trang đầu không có mã mới
    /// (chế độ rút tồn đọng — xem <c>ShopFlowRunner.CheckDonTraHangAsync</c>). null → coi như không shop nào còn
    /// sót ⇒ hành vi y hệt trước 11/08/2026.</param>
    /// <param name="luuConSotTraHang">Ghi lại cờ trên sau mỗi lượt check (nhãn shop, còn sót hay không, LÝ DO —
    /// hằng <c>ShopFlowRunner.LyDoSot*</c>, chuỗi rỗng khi không sót → cột <c>tra_hang_sot_ly_do</c>). null →
    /// không ghi (đường "Chạy thử").</param>
    /// <param name="onShopChuongTrinh">Tab "Shops" (ba cột KM | Combo | Flash Sale): vòng TẠO CHƯƠNG TRÌNH gọi khi
    /// BẮT ĐẦU một shop (<c>dangChay: true</c>) và khi XONG shop đó — <b>kể cả shop bỏ qua, shop hỏng, shop rơi
    /// captcha</b> (<c>dangChay: false</c>, kèm số sản phẩm tạo được). Tham số: loại chương trình · nhãn shop
    /// (ĐÚNG khóa <c>km_program_daily</c>: <c>LoginName</c>, rỗng thì <c>ShopName</c>) · đang-chạy · số sản phẩm.
    /// null → bỏ qua.
    /// <para><b>Số sản phẩm là <c>int?</c>, và <c>null</c> nghĩa là KHÔNG CÓ SỐ ĐỂ GHI</b> — khác hẳn <c>0</c>
    /// ("đã chạy mà không tạo được sp nào"). Hai ca dùng <c>null</c>: lượt bắn BẮT ĐẦU shop (chưa biết gì), và
    /// shop bị HUỶ giữa chừng (người dùng bấm Dừng lúc shop đang chờ) — ghi <c>0</c> cho ca huỷ là nói dối "đã
    /// chạy xong mà không tạo được gì", làm mờ đúng ranh giới <c>—</c>/<c>0</c>.</para></param>
    public OrdersBridgeSession(string userDataDir, Action<string>? log = null,
        string? invoiceDir = null, string? province = null,
        Func<string, string, IReadOnlyList<SyncedOrder>, CancellationToken, Task>? syncCallback = null,
        Func<IReadOnlySet<string>>? finalDoneSns = null,
        Action<IReadOnlyList<ShopListItem>>? onShopListRead = null,
        Action<string, string>? onOrderPrepared = null,
        Action<string>? onShopCheckStarted = null,
        Action<string>? onShopCheckFinished = null,
        Func<string, int?>? returnCountLast = null,
        Action<string, int>? saveReturnCount = null,
        Func<IReadOnlyList<YeuCauTraHang>, string>? saveReturnCodes = null,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>>? layDonThieuPhieu = null,
        Func<IReadOnlyList<YeuCauTraHang>, int>? demMaTraChuaBiet = null,
        Func<string, bool>? dangCoCanhBaoDiaChi = null,
        Func<string, bool>? conSotTraHang = null,
        Action<string, bool, string>? luuConSotTraHang = null,
        Action<LoaiChuongTrinh, string, bool, int?>? onShopChuongTrinh = null,
        Func<string?>? layShopDaCheckCuoi = null,
        Action<string?>? luuShopDaCheckCuoi = null)
    {
        _userDataDir = userDataDir;
        _log = log;
        _province = string.IsNullOrWhiteSpace(province) ? "Thanh Hóa" : province;
        _onShopListRead = onShopListRead;
        _onShopCheckStarted = onShopCheckStarted;
        _onShopCheckFinished = onShopCheckFinished;
        _onShopChuongTrinh = onShopChuongTrinh;
        _layShopDaCheckCuoi = layShopDaCheckCuoi;
        _luuShopDaCheckCuoi = luuShopDaCheckCuoi;

        _channel = new OrdersBridgeChannel(log);
        _flow = new ShopFlowRunner(_channel, log, invoiceDir, _province, syncCallback, finalDoneSns,
            onOrderPrepared, returnCountLast, saveReturnCount, saveReturnCodes, layDonThieuPhieu,
            demMaTraChuaBiet, dangCoCanhBaoDiaChi, conSotTraHang, luuConSotTraHang);
    }

    private void L(string m) => _log?.Invoke(m);

    /// <summary>Thay MỚI mọi chặng chờ + xóa hai cờ dừng (captcha, không-đặt-được-địa-chỉ) trước một lần chạy.</summary>
    private void ResetState()
    {
        _channel.ResetStages();
        _flow.PickupFailedShop = null;
    }

    // ── Khởi động cầu + mở trình duyệt sạch tại startUrl (kèm hash cổng WS) ─────────────────────────────
    private void StartBridgeAndLaunch(string baseUrl)
    {
        // Cổng mở ĐÚNG MỘT LẦN cho cả phiên: một vòng có thể phóng trình duyệt sạch HAI lần (thử trước bằng
        // cookie hồ sơ, rồi mở lại sau khi đăng nhập Playwright). Gọi Start lần hai là dựng HttpListener MỚI
        // trên cổng ta ĐANG giữ đăng ký → ném, và retry trong Channel.Start không cứu được vì kẻ chiếm cổng
        // chính là mình. WebSocketServer tự thay socket khi extension của lượt sau nối vào ("kết nối mới nhất
        // là sống"), nên giữ nguyên server là đủ.
        if (!_channel.Started)
        {
            _channel.Start();
        }
        L($"Cầu nối: WebSocket lắng nghe ws://localhost:{_channel.ActivePort} — mở trình duyệt sạch...");

        // Vẫn nhúng hash (extension đọc nếu còn) nhưng KHÔNG phụ thuộc: mất hash → extension dùng cổng đã lưu trong session.
        var startUrl = $"{baseUrl}#_od_ws={_channel.ActivePort}";
        Process = OrdersBridgeLauncher.Launch(_userDataDir, startUrl, _log);
        TheoDoiTrinhDuyetThoat(Process);
    }

    /// <summary>
    /// Ghi nhật ký khi TIẾN TRÌNH trình duyệt sạch thoát, kèm MÃ THOÁT. Không có dòng này thì trình duyệt chết
    /// giữa vòng chỉ hiện ra dưới dạng "cầu nối đứt", và câu chờ-nối-lại còn đổ oan cho service worker MV3
    /// ("service worker ngủ trong lúc nghỉ?") — đúng ca 10/08/2026: vòng 11:00 thấy Brave biến mất sạch lúc
    /// 11:23:46 mà nhật ký chỉ nói cầu nối đứt. Mã thoát phân biệt THOÁT ÊM (0) với CHẾT (mã lỗi Windows), tức
    /// phân biệt "có ai đó đóng cửa sổ / tab cuối vừa đóng" với "trình duyệt sập".
    /// <para>Best-effort tuyệt đối: hỏng ở đây tuyệt đối không được ảnh hưởng vòng chạy.</para>
    /// </summary>
    private void TheoDoiTrinhDuyetThoat(System.Diagnostics.Process? p)
    {
        if (p is null) { return; }
        try
        {
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) =>
            {
                int? ma = null;
                try { ma = p.ExitCode; } catch { /* không đọc được mã */ }
                L($"⚠ Trình duyệt sạch (PID {p.Id}) đã THOÁT lúc {DateTime.Now:HH:mm:ss}"
                  + (ma is null ? " (không đọc được mã thoát)." : $" — mã thoát {ma} (0x{ma:X})."));
            };
        }
        catch (Exception ex) { L("Không gắn được theo dõi tiến trình trình duyệt: " + ex.ToString()); }
    }

    // ── GĐ2 (pivot): đăng nhập bằng Playwright (an toàn — subaccount + /portal/shop KHÔNG bị captcha) → đóng
    //    → mở lại bằng trình duyệt SẠCH + extension để đọc Seller Centre (chỉ "Chi tiết" mới dính captcha). ──────
    /// <summary>
    /// GĐ2: đăng nhập Nền tảng tài khoản phụ bằng <b>trình duyệt điều khiển Playwright/CDP CŨ</b> (tái dùng NGUYÊN
    /// <see cref="ShopeeLoginService.OpenAsync"/> + <c>TryLoginSubaccountAsync</c> — tự điền form, mở hộp thư cho user
    /// đọc mã, chờ mã, SSO tới Seller Centre). Đăng nhập xong thì ĐÓNG trình duyệt điều khiển (nhả khoá hồ sơ), rồi
    /// mở lại bằng <b>trình duyệt SẠCH + extension</b> qua <see cref="RunSliceCoreAsync"/> (hồ sơ đã đăng nhập nên vào
    /// thẳng <c>/portal/shop</c>) → đọc shop → "Chi tiết" (trusted click, né captcha) → "Chờ Lấy Hàng".
    /// KHÔNG tự nhập mã hộ (mã là thao tác tay). Hủy giữa chừng → đóng cả trình duyệt điều khiển (finally) lẫn sạch.
    /// </summary>
    public async Task<OrdersBridgeSliceResult> RunLoginThenSliceAsync(OrdersLoginParams login, CancellationToken ct = default)
    {
        try
        {
            var err = await LoginAndReachPickerAsync(login, ct).ConfigureAwait(false);
            if (_channel.CaptchaSeen)
            {
                return new OrdersBridgeSliceResult(Array.Empty<ShopListItem>(), null, null, true,
                    "Rơi vào trang verify/captcha khi vào Seller Centre.");
            }
            if (err is not null)
            {
                return Fail(err);
            }

            L("Đã về trang chọn shop — đọc shop...");
            return await RunSliceCoreAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (TimeoutException)
        {
            L("Cầu nối: hết thời gian chờ phản hồi từ extension.");
            return Fail("Hết thời gian chờ phản hồi từ extension.");
        }
        catch (Exception ex)
        {
            L("Cầu nối lỗi: " + ex.ToString());
            return Fail(ex.Message);
        }
    }

    /// <summary>
    /// <b>PHA 1 "KM của Shop" — LÁT CẮT DÒ DOM. Đây là một THÍ NGHIỆM, không phải luồng nghiệp vụ.</b>
    /// <para>Mục tiêu: chứng minh hoặc BÁC BỎ rằng điều khiển được form "Tạo chương trình" của Shopee bằng
    /// <b>DOM thuần</b> (người dùng chốt không dùng CDP), và lấy về cấu trúc thật của hai ô thời gian
    /// <c>eds-date-picker</c> + modal chọn sản phẩm. Kết quả ÂM TÍNH có giá trị ngang dương tính — nó là căn cứ
    /// để quay lại hỏi người dùng, nên extension được lệnh ghi rõ "KHÔNG ĂN" chứ tuyệt đối không lặng lẽ đổi
    /// sang trusted click cho nó chạy.</para>
    /// <para><b>KHÔNG tạo chương trình thật:</b> extension không bấm "Xác nhận" ở footer modal cũng như ở thanh
    /// đáy trang; modal đóng bằng "Hủy". Ràng buộc này nằm trong <c>flow-km.js</c>.</para>
    /// <para>Khúc đầu dùng lại NGUYÊN đường đã chứng minh của nút "Mở list shop": đăng nhập/SSO → trang chọn shop
    /// → đọc danh sách. Rồi mở chi tiết shop ĐẦU TIÊN bằng <c>openShopDetail</c> — bước này CỐ Ý vẫn là trusted
    /// click sẵn có, vì nó thuộc luồng check đơn đã chạy thật hàng tháng, không phải "action KM".</para>
    /// <para>Cửa sổ được ĐỂ NGUYÊN như "Mở list shop" — người dùng còn phải nhìn tận nơi trang đang ở đâu.</para>
    /// </summary>
    public async Task<KetQuaDoDomKm> DoDomKmAsync(OrdersLoginParams login, CancellationToken ct = default, int viTriShop = 0)
    {
        try
        {
            var err = await LoginAndReachPickerAsync(login, ct).ConfigureAwait(false);
            if (_channel.CaptchaSeen)
            {
                return new KetQuaDoDomKm(null, null, true, "Rơi vào trang verify/captcha khi vào Seller Centre.");
            }
            if (err is not null)
            {
                return new KetQuaDoDomKm(null, null, false, err);
            }

            return await DoDomKmSauPickerAsync(_channel, _onShopListRead, _log, ct, viTriShop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; } // người dùng bấm "Đóng cửa sổ"
        catch (TimeoutException)
        {
            L("Cầu nối: hết thời gian chờ phản hồi từ extension (dò DOM KM).");
            return new KetQuaDoDomKm(null, null, _channel.CaptchaSeen, "Hết thời gian chờ phản hồi từ extension.");
        }
        catch (Exception ex)
        {
            L("Cầu nối lỗi (dò DOM KM): " + ex.ToString());
            return new KetQuaDoDomKm(null, null, _channel.CaptchaSeen, ex.Message);
        }
    }

    /// <summary>
    /// Toàn bộ phần SAU khi đã về trang chọn shop của <see cref="DoDomKmAsync"/>: đọc danh sách shop → mở chi
    /// tiết shop ĐẦU TIÊN → bảo extension dò DOM. <b>Hết. Ba lệnh, không có lệnh thứ tư.</b>
    /// <para><b>Vì sao tách <c>static</c> nhận <paramref name="channel"/></b> (khuôn <see cref="SsoQuaCauNoiAsync"/>
    /// và <see cref="DocXongThiDungAsync"/>): đây là chỗ DUY NHẤT có thể lặng lẽ mọc thêm một lệnh tác động lên
    /// shop THẬT của người dùng. Ở dạng này <c>BridgeTestRig</c> lái được nó bằng cầu nối thật và khẳng định
    /// chuỗi lệnh ĐÚNG BẰNG <c>[readShopList, openShopDetail, dumpKm]</c>. Gói kín trong
    /// <see cref="DoDomKmAsync"/> (có bước đăng nhập phóng trình duyệt) thì test chỉ còn cách tự chép lại chuỗi
    /// lệnh — mà bản chép thì không khoá được mã production, đúng cái bẫy đã mắc một lần ở việc trước.</para>
    /// </summary>
    internal static async Task<KetQuaDoDomKm> DoDomKmSauPickerAsync(
        OrdersBridgeChannel channel, Action<IReadOnlyList<ShopListItem>>? onShopListRead,
        Action<string>? log, CancellationToken ct, int viTriShop = 0)
    {
        var shops = await DocDanhSachShopAsync(channel, onShopListRead, ct).ConfigureAwait(false);
        if (shops.Count == 0)
        {
            return new KetQuaDoDomKm(null, null, false, "Không đọc được shop nào (đã tới /portal/shop chưa?).");
        }

        // Shop ĐẦU TIÊN — lát cắt chẩn đoán chỉ cần một shop.
        // Chọn shop theo VỊ TRÍ, không cứng shops[0]. Vì sao: dò lại CÙNG một shop nhiều lần trong thời gian
        // ngắn là đúng thứ đã làm Shopee bật `verify/traffic/error` (2 lần mở cùng shop trong 17 phút, đêm
        // 18/08). Nút tạm của pha 1 nay mỗi lần bấm đi một shop khác; pha 3 sẽ thay bằng vòng lặp thật.
        // Quay vòng khi vượt số shop để không bao giờ ném IndexOutOfRange.
        var viTri = shops.Count == 0 ? 0 : ((viTriShop % shops.Count) + shops.Count) % shops.Count;
        var shopId = shops[viTri].ShopId;
        log?.Invoke($"[Dò DOM KM] Mở chi tiết shop #{viTri + 1}/{shops.Count} (id={shopId}, {shops[viTri].ShopName}) bằng trusted click (bước của luồng check đơn)...");
        var detailTcs = channel.ArmDetail();
        await channel.SendAsync(new { action = "openShopDetail", shopId }).ConfigureAwait(false);
        var detail = await channel.AwaitAsync(detailTcs, OrdersBridgeChannel.ChoChang.Detail, ct).ConfigureAwait(false);
        if (detail == "captcha" || channel.CaptchaSeen)
        {
            return new KetQuaDoDomKm(shopId, null, true, "Rơi vào trang verify/captcha khi mở Chi tiết shop.");
        }

        log?.Invoke("[Dò DOM KM] Đã vào shop — bắt đầu dò cấu trúc form 'Tạo chương trình' bằng DOM thuần...");
        var dumpTcs = channel.ArmKmDump();
        await channel.SendAsync(new { action = "dumpKm" }).ConfigureAwait(false);
        var json = await channel.AwaitAsync(dumpTcs, OrdersBridgeChannel.ChoChang.KmDump, ct).ConfigureAwait(false);
        return new KetQuaDoDomKm(shopId, json, false, null);
    }

    /// <summary>
    /// <b>"Flash Sale Của Shop" — LÁT CẮT DÒ DOM. Đây là bước CHẨN ĐOÁN chạy TRƯỚC khi viết luồng thật.</b>
    /// <para>Mục tiêu: đo bốn ẩn số của <c>plans/2026-08-27-flash-sale-cua-shop.md</c> §3 — dấu hiệu "sắp xếp
    /// giảm dần" của cột Doanh số (className cụm mũi tên tại ba mốc T0/T1/T2), số ngày ĐỎ thật của một tháng,
    /// mặc định của ô "Xem sản phẩm có sẵn", và ca đêm cuối tháng (ngày mai nằm ở tháng sau).</para>
    /// <para><b>⚠ KHÁC HẲN <see cref="DoDomKmAsync"/>: lượt này TUYỆT ĐỐI KHÔNG TẠO CHƯƠNG TRÌNH NÀO.</b> Nút dò
    /// KM đi chung hàm với luồng thật nên nó bấm cả nút tạo — đêm 27/08/2026 nó để lại một chương trình rác
    /// <c>DO DOM KM …</c> mà người dùng phải xoá tay. Lượt Flash Sale dừng ở "Hủy" modal sản phẩm; ràng buộc
    /// nằm trong <c>flow-flashsale.js</c> và được khoá bằng test đọc chính nguồn file đó.</para>
    /// <para>Cửa sổ được ĐỂ NGUYÊN như "Mở list shop" — người dùng còn phải nhìn tận nơi trang đang ở đâu.</para>
    /// </summary>
    public async Task<KetQuaDoDomFlash> DoDomFlashSaleAsync(
        OrdersLoginParams login, CancellationToken ct = default, int viTriShop = 0)
    {
        try
        {
            var err = await LoginAndReachPickerAsync(login, ct).ConfigureAwait(false);
            if (_channel.CaptchaSeen)
            {
                return new KetQuaDoDomFlash(null, null, true, "Rơi vào trang verify/captcha khi vào Seller Centre.");
            }
            if (err is not null)
            {
                return new KetQuaDoDomFlash(null, null, false, err);
            }

            return await DoDomFlashSaleSauPickerAsync(_channel, _onShopListRead, _log, ct, viTriShop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; } // người dùng bấm "Đóng cửa sổ"
        catch (TimeoutException)
        {
            L("Cầu nối: hết thời gian chờ phản hồi từ extension (dò DOM Flash Sale).");
            return new KetQuaDoDomFlash(null, null, _channel.CaptchaSeen, "Hết thời gian chờ phản hồi từ extension.");
        }
        catch (Exception ex)
        {
            L("Cầu nối lỗi (dò DOM Flash Sale): " + ex.ToString());
            return new KetQuaDoDomFlash(null, null, _channel.CaptchaSeen, ex.Message);
        }
    }

    /// <summary>
    /// Toàn bộ phần SAU khi đã về trang chọn shop của <see cref="DoDomFlashSaleAsync"/>: đọc danh sách shop →
    /// mở chi tiết MỘT shop → bảo extension dò DOM. <b>Hết. Ba lệnh, không có lệnh thứ tư.</b>
    /// <para><b>Vì sao tách <c>static</c> nhận <paramref name="channel"/></b> (đúng khuôn
    /// <see cref="DoDomKmSauPickerAsync"/>): đây là chỗ DUY NHẤT có thể lặng lẽ mọc thêm một lệnh tác động lên
    /// shop THẬT. Ở dạng này <c>BridgeTestRig</c> lái được nó bằng cầu nối thật và khẳng định chuỗi lệnh ĐÚNG
    /// BẰNG <c>[readShopList, openShopDetail, dumpFlashSale]</c> — và đặc biệt là KHÔNG có lệnh thứ tư nào.</para>
    /// </summary>
    internal static async Task<KetQuaDoDomFlash> DoDomFlashSaleSauPickerAsync(
        OrdersBridgeChannel channel, Action<IReadOnlyList<ShopListItem>>? onShopListRead,
        Action<string>? log, CancellationToken ct, int viTriShop = 0)
    {
        var shops = await DocDanhSachShopAsync(channel, onShopListRead, ct).ConfigureAwait(false);
        if (shops.Count == 0)
        {
            return new KetQuaDoDomFlash(null, null, false, "Không đọc được shop nào (đã tới /portal/shop chưa?).");
        }

        // Chọn shop theo VỊ TRÍ, không cứng shops[0] — cùng lý do đã ghi ở `DoDomKmSauPickerAsync`: dò lại CÙNG
        // một shop nhiều lần trong thời gian ngắn đúng là thứ đã làm Shopee bật `verify/traffic/error` (2 lần mở
        // cùng shop trong 17 phút, đêm 18/08). Quay vòng khi vượt số shop để không bao giờ ném IndexOutOfRange.
        var viTri = ((viTriShop % shops.Count) + shops.Count) % shops.Count;
        var shopId = shops[viTri].ShopId;
        log?.Invoke($"[Dò DOM Flash] Mở chi tiết shop #{viTri + 1}/{shops.Count} (id={shopId}, {shops[viTri].ShopName}) bằng trusted click (bước của luồng check đơn)...");
        var detailTcs = channel.ArmDetail();
        await channel.SendAsync(new { action = "openShopDetail", shopId }).ConfigureAwait(false);
        var detail = await channel.AwaitAsync(detailTcs, OrdersBridgeChannel.ChoChang.Detail, ct).ConfigureAwait(false);
        if (detail == "captcha" || channel.CaptchaSeen)
        {
            return new KetQuaDoDomFlash(shopId, null, true, "Rơi vào trang verify/captcha khi mở Chi tiết shop.");
        }

        log?.Invoke("[Dò DOM Flash] Đã vào shop — dò DOM 'Flash Sale Của Shop' (CHỈ ĐỌC, không tạo chương trình nào)...");
        var dumpTcs = channel.ArmFlashDump();
        // Gửi kèm SHOP ĐANG ĐO để bản dump tự khai nó đo shop nào — không có nó thì đọc lại một bản dump cũ
        // là phải đoán, mà cả kết luận "shop này toàn 0 doanh số" lẫn "thứ tự sản phẩm đổi/không đổi" đều phụ
        // thuộc hẳn vào shop nào. `viTri` là chỉ số ĐẾM TỪ 0 (xem biến môi trường FLASH_VITRI_SHOP).
        await channel.SendAsync(new
        {
            action = "dumpFlashSale",
            shop = new { viTri, shopId, ten = shops[viTri].ShopName },
        }).ConfigureAwait(false);
        var json = await channel.AwaitAsync(dumpTcs, OrdersBridgeChannel.ChoChang.FlashDump, ct).ConfigureAwait(false);
        return new KetQuaDoDomFlash(shopId, json, false, null);
    }


    /// <summary>
    /// <b>"KM của Shop" — LUỒNG THẬT (pha 3).</b> Đi ĐÚNG đường của luồng check đơn: mở Brave sạch + extension
    /// → SSO "Kênh Người bán" → trang chọn shop → LẶP QUA MỌI SHOP: mở Chi tiết (trusted click) → bảo extension
    /// tạo chương trình bằng DOM thuần → đóng tab về trang chọn shop → nghỉ 3–5 phút → shop kế.
    /// <para><b>Một shop hỏng KHÔNG giết cả lượt.</b> Ghi lỗi vào kết quả của shop đó rồi đi tiếp — 12 shop mà
    /// chết vì shop thứ 2 thì 10 shop sau mất chương trình, trong khi lỗi thường chỉ của riêng một shop (đúng
    /// bài học đã rút ở luồng đặt địa chỉ, 2026-08-04). Riêng CAPTCHA thì dừng hẳn: cố đi tiếp lúc Shopee đang
    /// nghi ngờ là tự khai bot.</para>
    /// <para>Cửa sổ để nguyên ở mọi lối ra — tầng App quyết định đóng, giống <see cref="MoDenDanhSachShopAsync"/>.</para>
    /// </summary>
    /// <param name="login">Tham số đăng nhập.</param>
    /// <param name="thamSo">Thông số chương trình khuyến mãi.</param>
    /// <param name="hanChot">Mốc KHÔNG được bắt đầu shop mới nữa (lượt tự động: 06:00 cùng ngày — xem
    /// <see cref="KmLichTuDong.HanChotLuotTuDong"/>). <c>null</c> = đường bấm tay, chạy tới hết.</param>
    /// <param name="ct">Token huỷ.</param>
    /// <param name="phaiNhuongSauShop">Cờ NHƯỜNG CHỖ cho check đơn — xem tham số cùng tên ở
    /// <see cref="ChayVongTaoChuongTrinhAsync"/>. <b>Vòng KM cũng nhận nó</b> (03/09/2026, phản biện vòng 2):
    /// luật "không đè lên một lượt đang mở Brave" áp cho MỌI lát cắt cầu nối, và một vòng KM tự động 2h sáng
    /// giữ hồ sơ Brave + cổng 47821 y hệt một gói flash.</param>
    public async Task<KetQuaKm> ChayKmMoiShopAsync(
        OrdersLoginParams login, ThamSoKm thamSo, DateTime? hanChot = null, CancellationToken ct = default,
        Func<bool>? phaiNhuongSauShop = null)
    {
        try
        {
            var err = await LoginAndReachPickerAsync(login, ct).ConfigureAwait(false);
            if (_channel.CaptchaSeen)
            {
                return new KetQuaKm(Array.Empty<KetQuaKmMotShop>(), true, "Rơi vào trang verify/captcha khi vào Seller Centre.");
            }
            if (err is not null) { return new KetQuaKm(Array.Empty<KetQuaKmMotShop>(), false, err); }

            return await ChayKmSauPickerAsync(
                _channel, _onShopListRead, _log, thamSo,
                vePicker: c => _flow.DongTabShopAsync(c),
                nghiGiuaShop: NghiGiuaShopAsync,
                hanChot: hanChot,
                dongHo: () => DateTime.Now,
                ct: ct,
                onShopChuongTrinh: _onShopChuongTrinh,
                phaiNhuongSauShop: phaiNhuongSauShop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            L("Cầu nối lỗi (KM): " + ex.ToString());
            return new KetQuaKm(Array.Empty<KetQuaKmMotShop>(), _channel.CaptchaSeen, ex.Message);
        }
    }

    /// <summary>
    /// <b>"Combo Khuyến Mãi" — LUỒNG THẬT.</b> Y hệt <see cref="ChayKmMoiShopAsync"/> về hạ tầng (SSO → trang
    /// chọn shop → lặp shop → nghỉ 3–5 phút), chỉ khác lệnh gửi xuống extension: <c>chayCombo</c> thay cho
    /// <c>chayKm</c>.
    /// <para>Phía extension: <c>flow-combo.js</c> nhận lệnh đó (chặng B của
    /// <c>plans/2026-08-25-combo-khuyen-mai.md</c>, xong 26/08/2026) và trả gói <c>kmKetQua</c> — CÙNG khuôn với
    /// luồng KM, vì cả hai đi chung <see cref="ChayVongTaoChuongTrinhAsync"/>. <c>combo_bat</c> vẫn mặc định TẮT:
    /// người dùng tự bật trên form.</para>
    /// </summary>
    /// <param name="phaiNhuongSauShop">Cờ NHƯỜNG CHỖ cho check đơn — xem tham số cùng tên ở
    /// <see cref="ChayKmMoiShopAsync"/>.</param>
    public async Task<KetQuaKm> ChayComboMoiShopAsync(
        OrdersLoginParams login, ThamSoCombo thamSo, DateTime? hanChot = null, CancellationToken ct = default,
        Func<bool>? phaiNhuongSauShop = null)
    {
        try
        {
            var err = await LoginAndReachPickerAsync(login, ct).ConfigureAwait(false);
            if (_channel.CaptchaSeen)
            {
                return new KetQuaKm(Array.Empty<KetQuaKmMotShop>(), true, "Rơi vào trang verify/captcha khi vào Seller Centre.");
            }
            if (err is not null) { return new KetQuaKm(Array.Empty<KetQuaKmMotShop>(), false, err); }

            // KHÔNG truyền `tranShop`: giá trị mặc định của tham số CHÍNH LÀ
            // `KmLichTuDong.TranShopLuotCombo`, nên cả hai đường của vòng combo (nút bấm tay và lịch tự động
            // 06–08h) đi qua đây đều nhận đúng một con số. Test rig gọi `ChayComboSauPickerAsync` KHÔNG truyền
            // tham số đó chính là đo đường này.
            return await ChayComboSauPickerAsync(
                _channel, _onShopListRead, _log, thamSo,
                vePicker: c => _flow.DongTabShopAsync(c),
                nghiGiuaShop: NghiGiuaShopAsync,
                hanChot: hanChot,
                dongHo: () => DateTime.Now,
                ct: ct,
                onShopChuongTrinh: _onShopChuongTrinh,
                phaiNhuongSauShop: phaiNhuongSauShop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            L("Cầu nối lỗi (Combo): " + ex.ToString());
            return new KetQuaKm(Array.Empty<KetQuaKmMotShop>(), _channel.CaptchaSeen, ex.Message);
        }
    }

    /// <summary>
    /// Toàn bộ phần SAU khi đã về trang chọn shop của <see cref="ChayKmMoiShopAsync"/>: đọc danh sách shop →
    /// LẶP từng shop (mở Chi tiết → <c>chayKm</c> → về picker → nghỉ).
    /// <para><b>Vì sao tách <c>static</c> nhận <paramref name="channel"/></b> (cùng khuôn
    /// <see cref="DoDomKmSauPickerAsync"/> và <see cref="DocXongThiDungAsync"/>): đây là vòng lặp gửi lệnh TÁC
    /// ĐỘNG THẬT lên mọi shop của người dùng, và ở dạng này <c>BridgeTestRig</c> lái được nó bằng cầu nối thật
    /// để khoá CHUỖI LỆNH cho cả bốn ca (shop có SP · shop rỗng · shop hỏng · captcha). Gói kín trong hàm có
    /// bước đăng nhập thì test chỉ còn cách chép lại chuỗi lệnh — bản chép không khoá được mã production.</para>
    /// <para>Hai móc <paramref name="vePicker"/> / <paramref name="nghiGiuaShop"/> nhận từ ngoài vì chúng thuộc
    /// <see cref="ShopFlowRunner"/> (instance) và vì kỳ nghỉ 3–5 phút không thể chờ thật trong test.</para>
    /// <para><b><paramref name="hanChot"/> — mốc thôi bắt đầu shop mới.</b> Lượt tự động khởi động được tới
    /// 05:59 mà vòng 12 shop không có mốc dừng nào của riêng nó: hạn chặng 900s + nghỉ 3–5 phút mỗi shop ⇒ ca
    /// xấu chạy tới gần 10h sáng, check đơn chết cả buổi. Chỉ chặn shop MỚI, không cắt ngang shop đang chạy dở.
    /// <c>null</c> = đường bấm tay, chạy tới hết.</para>
    /// </summary>
    internal static Task<KetQuaKm> ChayKmSauPickerAsync(
        OrdersBridgeChannel channel,
        Action<IReadOnlyList<ShopListItem>>? onShopListRead,
        Action<string>? log,
        ThamSoKm thamSo,
        Func<CancellationToken, Task<KetQuaVePicker>> vePicker,
        Func<CancellationToken, Task> nghiGiuaShop,
        DateTime? hanChot,
        Func<DateTime> dongHo,
        CancellationToken ct,
        Action<LoaiChuongTrinh, string, bool, int?>? onShopChuongTrinh = null,
        Func<bool>? phaiNhuongSauShop = null)
        => ChayVongTaoChuongTrinhAsync(
            channel, onShopListRead, log,
            loai: LoaiChuongTrinh.ChuongTrinhShop,
            nhan: "KM",
            action: "chayKm",
            // MÔ TẢ và GÓI đi qua ĐÚNG MỘT object `thamSo`. Xem khối chú thích ở `ThamSoKm`: hai lượt đọc độc
            // lập là dòng log thành chuỗi song song tự nhất quán, và đổi thứ gửi xuống Shopee không đỏ test nào.
            moTaChuongTrinh: thamSo.MoTa(),
            goiThamSo: thamSo.Goi(),
            // Vòng KM KHÔNG có trần chế độ thử — nó đang chạy thật và ổn định (người dùng chốt 26/08/2026).
            tranShop: 0,
            // Mốc bắt đầu đọc từ CHÍNH gói sắp gửi đi — cùng lý do "một object, một nguồn" với `moTaChuongTrinh`.
            mocBatDau: thamSo.MocBatDau(),
            vePicker, nghiGiuaShop, hanChot, dongHo, ct, onShopChuongTrinh,
            phaiNhuongSauShop: phaiNhuongSauShop);

    /// <summary>
    /// Phần SAU trang chọn shop của lượt <b>COMBO</b> — cùng vòng lặp với <see cref="ChayKmSauPickerAsync"/>,
    /// chỉ đổi lệnh (<c>chayCombo</c>), gói tham số (<see cref="ThamSoCombo.Goi"/>) và <paramref name="tranShop"/>.
    /// </summary>
    /// <param name="tranShop">Trần số shop của lượt (<c>0</c> = không giới hạn). Mặc định là
    /// <see cref="KmLichTuDong.TranShopLuotCombo"/> — <b>chỗ nối DUY NHẤT</b> giữa hằng công tắc giai đoạn thử và
    /// vòng chạy, nên cả đường bấm tay lẫn đường lịch tự động đều nhận đúng một giá trị. Test rót số riêng để đo
    /// được cả vế có trần lẫn vế đối chứng không trần.</param>
    internal static Task<KetQuaKm> ChayComboSauPickerAsync(
        OrdersBridgeChannel channel,
        Action<IReadOnlyList<ShopListItem>>? onShopListRead,
        Action<string>? log,
        ThamSoCombo thamSo,
        Func<CancellationToken, Task<KetQuaVePicker>> vePicker,
        Func<CancellationToken, Task> nghiGiuaShop,
        DateTime? hanChot,
        Func<DateTime> dongHo,
        CancellationToken ct,
        int tranShop = KmLichTuDong.TranShopLuotCombo,
        Action<LoaiChuongTrinh, string, bool, int?>? onShopChuongTrinh = null,
        Func<bool>? phaiNhuongSauShop = null)
        => ChayVongTaoChuongTrinhAsync(
            channel, onShopListRead, log,
            loai: LoaiChuongTrinh.Combo,
            nhan: "Combo",
            action: "chayCombo",
            moTaChuongTrinh: thamSo.MoTa(),
            goiThamSo: thamSo.Goi(),
            tranShop: tranShop,
            mocBatDau: thamSo.MocBatDau(),
            vePicker, nghiGiuaShop, hanChot, dongHo, ct, onShopChuongTrinh,
            phaiNhuongSauShop: phaiNhuongSauShop);

    /// <summary>
    /// <b>"Flash Sale Của Shop" — LUỒNG THẬT.</b> Cùng hạ tầng với hai vòng kia (SSO → trang chọn shop → lặp
    /// shop → nghỉ 3–5 phút), chỉ khác lệnh gửi xuống extension: <c>chayFlashSale</c>.
    /// <para><b>Cái khác thật sự nằm ở BÊN TRONG một shop, không ở vòng lặp</b>: mỗi shop tạo MỘT chương trình
    /// cho MỖI khung giờ Shopee mở cho ngày đích — không còn trần nào phía C# (03/09/2026), số khung do Shopee
    /// quyết. Vì thế chặng chờ là <see cref="OrdersBridgeChannel.ChoChang.FlashChay"/> chứ không phải
    /// <see cref="OrdersBridgeChannel.ChoChang.KmChay"/>.</para>
    /// <para><b>KHÔNG có mốc bắt đầu để kiểm</b> (<c>mocBatDau: null</c>): Flash Sale không đặt giờ bắt đầu —
    /// khung giờ là thứ Shopee phát hành sẵn và luồng chỉ chọn. Cửa "mốc bắt đầu đã trôi qua giữa vòng" vì thế
    /// không áp dụng; thứ thay nó là NGÀY ĐÍCH, đã được đóng băng trong <paramref name="thamSo"/>.</para>
    /// </summary>
    /// <param name="hanChot">Mốc thôi bắt đầu shop mới. Lượt TỰ ĐỘNG truyền <b>hạn của GÓI</b> =
    /// <c>KmLichTuDong.HanChotGoiFlash(bayGio, cuoiKhe)</c> — sớm hơn hạn ngày khi khe nghỉ ngắn.
    /// <para><b>⚠ KHÁC hai vòng kia: đường BẤM TAY của Flash cũng truyền hạn (22:15), KHÔNG truyền
    /// <c>null</c></b> (xem <c>AccountsViewModel.ChayFlashSaleTayAsync</c>). Ở hai vòng kia hạn chót chỉ để khỏi
    /// tràn sang buổi sáng nên người dùng ngồi trước máy tự quyết được; ở đây nó chặn một ca SAI THẬT — Shopee
    /// đóng đăng ký cho ngày đích quanh ~22:50, nên shop bắt đầu muộn hơn chạy nửa sau vào vùng chết.</para></param>
    /// <param name="boQuaShop">Shop đã XONG trong ngày, lọc khỏi gói này — xem tham số cùng tên ở
    /// <see cref="ChayVongTaoChuongTrinhAsync"/>.</param>
    /// <param name="phaiNhuongSauShop">Cờ NHƯỜNG CHỖ cho check đơn — xem tham số cùng tên ở
    /// <see cref="ChayVongTaoChuongTrinhAsync"/>.</param>
    public async Task<KetQuaKm> ChayFlashSaleMoiShopAsync(
        OrdersLoginParams login, ThamSoFlashSale thamSo, DateTime? hanChot = null,
        IReadOnlySet<string>? boQuaShop = null, CancellationToken ct = default,
        Func<bool>? phaiNhuongSauShop = null)
    {
        try
        {
            var err = await LoginAndReachPickerAsync(login, ct).ConfigureAwait(false);
            if (_channel.CaptchaSeen)
            {
                return new KetQuaKm(Array.Empty<KetQuaKmMotShop>(), true, "Rơi vào trang verify/captcha khi vào Seller Centre.");
            }
            if (err is not null) { return new KetQuaKm(Array.Empty<KetQuaKmMotShop>(), false, err); }

            // KHÔNG truyền `tranShop`: mặc định của tham số CHÍNH LÀ `KmLichTuDong.TranShopLuotFlash`, nên cả
            // đường bấm tay lẫn đường lịch tự động đi qua đây đều nhận đúng một con số.
            return await ChayFlashSaleSauPickerAsync(
                _channel, _onShopListRead, _log, thamSo,
                vePicker: c => _flow.DongTabShopAsync(c),
                nghiGiuaShop: NghiGiuaShopAsync,
                hanChot: hanChot,
                dongHo: () => DateTime.Now,
                ct: ct,
                onShopChuongTrinh: _onShopChuongTrinh,
                boQuaShop: boQuaShop,
                phaiNhuongSauShop: phaiNhuongSauShop).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            L("Cầu nối lỗi (Flash Sale): " + ex.ToString());
            return new KetQuaKm(Array.Empty<KetQuaKmMotShop>(), _channel.CaptchaSeen, ex.Message);
        }
    }

    /// <summary>
    /// Phần SAU trang chọn shop của lượt <b>FLASH SALE</b> — cùng vòng lặp với hai hàm trên, chỉ đổi lệnh
    /// (<c>chayFlashSale</c>), gói tham số (<see cref="ThamSoFlashSale.Goi"/>), CHẶNG CHỜ
    /// (<see cref="OrdersBridgeChannel.ChoChang.FlashChay"/>) và <paramref name="tranShop"/>.
    /// </summary>
    /// <param name="tranShop">Trần số shop của lượt (<c>0</c> = không giới hạn). Mặc định
    /// <see cref="KmLichTuDong.TranShopLuotFlash"/> — <b>chỗ nối DUY NHẤT</b> giữa hằng công tắc giai đoạn thử
    /// và vòng chạy. <b>TIÊM qua tham số, KHÔNG đọc hằng trong thân vòng</b>: test phải lái được cả vế có trần
    /// lẫn vế đối chứng không trần, và phải sống được khi người dùng đặt hằng về 0.</param>
    internal static Task<KetQuaKm> ChayFlashSaleSauPickerAsync(
        OrdersBridgeChannel channel,
        Action<IReadOnlyList<ShopListItem>>? onShopListRead,
        Action<string>? log,
        ThamSoFlashSale thamSo,
        Func<CancellationToken, Task<KetQuaVePicker>> vePicker,
        Func<CancellationToken, Task> nghiGiuaShop,
        DateTime? hanChot,
        Func<DateTime> dongHo,
        CancellationToken ct,
        int tranShop = KmLichTuDong.TranShopLuotFlash,
        Action<LoaiChuongTrinh, string, bool, int?>? onShopChuongTrinh = null,
        IReadOnlySet<string>? boQuaShop = null,
        Func<bool>? phaiNhuongSauShop = null)
        => ChayVongTaoChuongTrinhAsync(
            channel, onShopListRead, log,
            loai: LoaiChuongTrinh.FlashSale,
            nhan: "Flash",
            action: "chayFlashSale",
            moTaChuongTrinh: thamSo.MoTa(),
            goiThamSo: thamSo.Goi(),
            tranShop: tranShop,
            // Flash Sale KHÔNG có mốc bắt đầu — xem xmldoc của `ChayFlashSaleMoiShopAsync`.
            mocBatDau: null,
            vePicker, nghiGiuaShop, hanChot, dongHo, ct, onShopChuongTrinh,
            armChay: (c, token) => c.ArmFlashChay(token),
            hanChang: OrdersBridgeChannel.ChoChang.FlashChay,
            boQuaShop: boQuaShop,
            phaiNhuongSauShop: phaiNhuongSauShop);

    /// <summary>
    /// <b>VÒNG LẶP DÙNG CHUNG của mọi lượt "tạo chương trình trên mọi shop"</b> — KM của Shop và Combo Khuyến
    /// Mãi đi qua ĐÚNG hàm này, chỉ khác <paramref name="action"/> và <paramref name="goiThamSo"/>.
    /// <para><b>Vì sao dùng chung chứ không chép:</b> phần khó của vòng này không phải cái lệnh gửi xuống mà là
    /// bốn nhánh thoát — hết khung · captcha từ HAI nguồn · shop hỏng không giết cả lượt · không về được trang
    /// chọn shop — cộng <c>SoShopChuaChay</c> phải đúng ở CẢ BA lối ra sớm. Chép bản thứ hai là hai bản trôi
    /// lệch ở đúng mấy chỗ đó, mà triệu chứng chỉ là một câu tổng kết nói sai.</para>
    /// </summary>
    /// <param name="loai">Loại chương trình của lượt — đi thẳng vào callback tiến độ để ba cột KM | Combo |
    /// Flash Sale biết điền vào Ô NÀO. Tách khỏi <paramref name="nhan"/> (một chuỗi cho người đọc log) vì một
    /// chuỗi thì không tra được về enum mà không dựng bảng ánh xạ thứ hai.</param>
    /// <param name="nhan">Nhãn đầu dòng nhật ký: <c>"KM"</c> hoặc <c>"Combo"</c>.</param>
    /// <param name="action">Lệnh gửi xuống extension: <c>"chayKm"</c> / <c>"chayCombo"</c>.</param>
    /// <param name="moTaChuongTrinh">Dòng mô tả chương trình cho nhật ký — <b>phải dựng TỪ CHÍNH
    /// <paramref name="goiThamSo"/></b>, xem chú thích ở <see cref="ThamSoCombo"/>.</param>
    /// <param name="goiThamSo">Gói <c>thamSo</c> gửi kèm mỗi lệnh — DỰNG MỘT LẦN, dùng lại cho mọi shop.</param>
    /// <param name="tranShop">Trần số shop của lượt (<c>0</c> = không giới hạn) — xem
    /// <see cref="KmLichTuDong.TranShopLuotCombo"/>. Chỉ vòng COMBO truyền khác 0.</param>
    /// <param name="mocBatDau">MỐC BẮT ĐẦU của chính chương trình sắp tạo (<c>ThamSoKm.MocBatDau()</c> /
    /// <c>ThamSoCombo.MocBatDau()</c>); <c>null</c> = không kiểm được (giờ sai định dạng — cửa kiểm ở tầng form
    /// đã lo ca đó). <b>Đọc từ CHÍNH gói sắp gửi đi</b>, đừng nhận một mốc rời từ nơi gọi: một mốc rời là một
    /// lượt đọc thứ hai, và nó lệch được với thứ Shopee thật sự nhận.</param>
    /// <param name="onShopChuongTrinh">Callback tiến độ + GHI SỐ theo từng shop — xem xmldoc tham số cùng tên ở
    /// hàm dựng <see cref="OrdersBridgeSession"/>.</param>
    /// <param name="boQuaShop">Khóa (<see cref="KhoaShop"/>) của những shop <b>ĐÃ XONG trong ngày</b>, cần LỌC
    /// khỏi lượt này; <c>null</c>/rỗng = chạy cả danh sách. Chỉ vòng FLASH truyền khác <c>null</c> — từ
    /// 01/09/2026 một ngày flash chia thành nhiều GÓI, mỗi gói chiếm một khe nghỉ, nên gói thứ hai trở đi phải
    /// biết bỏ qua phần đã làm. <b>Tiến độ ấy giữ IN-MEMORY ở tầng App</b>, không xuống DB/hub (plan §0).</param>
    /// <param name="phaiNhuongSauShop">
    /// <b>Cờ "THÔI bắt đầu shop mới — NHƯỜNG CHỖ cho một lượt chạy lại check đơn đang chờ"</b>, đọc TRƯỚC MỖI
    /// shop cùng cụm với cửa hết khung; <c>null</c> = không có ai nhường (đường test/rig).
    /// <para><b>CẢ BA vòng đều truyền cờ này</b> (sửa 03/09/2026, phản biện vòng 2): bản đầu chỉ rót cho vòng
    /// flash, nhưng lượt hoãn nhường cho <i>bất kỳ</i> lát cắt cầu nối nào (<c>_bridgeRunning</c>) — nên một
    /// vòng KM tự động 2h sáng nhận cờ bật mà không đọc thì lượt hoãn ngồi hết trần 90 phút rồi BỎ LUÔN check
    /// đơn, trong khi vòng KM vẫn thong thả đi tiếp 11 shop.</para>
    /// <para><b>Vì sao là <see cref="Func{TResult}"/> chứ không phải <see cref="bool"/></b>: cờ được bật GIỮA
    /// vòng, từ MỘT THREAD KHÁC (lượt hoãn chạy lại check đơn tỉnh dậy trong lúc gói còn đang làm shop). Nhận
    /// một <c>bool</c> là chụp giá trị lúc bắt đầu lượt, tức không bao giờ thấy cú bật — mà cả cơ chế này sinh
    /// ra chỉ để phục vụ đúng cú bật đó. Xem <c>AccountsViewModel.NhuongChoCheckDon</c> (<c>volatile</c>).</para>
    /// <para><b>Kiểm ở RANH GIỚI SHOP, không cắt ngang shop đang chạy</b> — cùng luật đã dùng cho hết khung:
    /// một shop flash đang giữa chuỗi "tạo khung → điền bulk → kích hoạt" mà bị cắt là để lại chương trình
    /// 0-sản-phẩm CHIẾM KHUNG trên Shopee, và lượt sau coi khung đó "đã dùng" (xem <c>thanBuocTaiChe</c>).</para>
    /// </param>
    private static async Task<KetQuaKm> ChayVongTaoChuongTrinhAsync(
        OrdersBridgeChannel channel,
        Action<IReadOnlyList<ShopListItem>>? onShopListRead,
        Action<string>? log,
        LoaiChuongTrinh loai,
        string nhan,
        string action,
        string moTaChuongTrinh,
        object goiThamSo,
        int tranShop,
        DateTime? mocBatDau,
        Func<CancellationToken, Task<KetQuaVePicker>> vePicker,
        Func<CancellationToken, Task> nghiGiuaShop,
        DateTime? hanChot,
        Func<DateTime> dongHo,
        CancellationToken ct,
        Action<LoaiChuongTrinh, string, bool, int?>? onShopChuongTrinh = null,
        Func<OrdersBridgeChannel, string, TaskCompletionSource<string?>>? armChay = null,
        TimeSpan? hanChang = null,
        IReadOnlySet<string>? boQuaShop = null,
        Func<bool>? phaiNhuongSauShop = null)
    {
        // CHẶNG CHỜ của lệnh chạy — mặc định là chặng KM (dùng cho cả KM lẫn Combo). Vòng FLASH tiêm chặng
        // riêng: gói của nó đi về `kind:"flashKetQua"` và hạn là 2700s chứ không phải 1800s (xem
        // `OrdersBridgeChannel.ArmFlashChay`). Tiêm qua tham số chứ KHÔNG rẽ nhánh theo `loai` trong thân vòng:
        // một `switch` ở đây là chỗ luồng thứ tư sẽ bị quên, và quên thì gói tới không ai nhận.
        var armLuot = armChay ?? ((c, token) => c.ArmKmChay(token));
        var hanLuot = hanChang ?? OrdersBridgeChannel.ChoChang.KmChay;

        var ketQua = new List<KetQuaKmMotShop>();

        // CẢ LƯỢT ĐỀU KHÔNG ĐỎ — kết luận mức NGÀY, chỉ rút ra khi MỌI shop đã chạy của lượt cùng khai cờ mà
        // KHÔNG shop nào tạo được gì. Lượt rỗng ⇒ false (không có bằng chứng nào thì không kết luận gì).
        //
        // ⚠ Dùng ở CẢ các lối ra sớm không-phải-captcha, không chỉ ở cuối vòng: từ 01/09/2026 một lượt flash là
        // một GÓI ~2 shop, nên "chạy hết danh sách" là ca HIẾM — chỉ tính ở cuối vòng thì cờ gần như không bao
        // giờ bật và câu tổng kết không bao giờ nói ra lý do dừng. Hai lối ra CAPTCHA cố ý ĐỨNG NGOÀI: ở đó
        // lượt dừng vì một lý do khác hẳn, và tập shop đã chạy không còn là bằng chứng sạch.
        //
        // ⚠ Cờ này CHỈ ĐỂ BÁO CÁO — xem xmldoc `KetQuaKm.NgayMaiKhongDo`. Đừng nối nó vào một cửa "thôi làm
        // việc" nào nữa: bằng chứng của MỘT GÓI ~2 shop không đủ để kết luận cho cả ngày.
        bool CaLuotKhongDo() => ketQua.Count > 0 && ketQua.All(s => s.NgayMaiKhongDo && !s.DaTao);

        var shops = await DocDanhSachShopAsync(channel, onShopListRead, ct).ConfigureAwait(false);
        if (shops.Count == 0) { return new KetQuaKm(ketQua, false, "Không đọc được shop nào."); }

        // LỌC SHOP ĐÃ XONG HÔM NAY (gói flash thứ 2 trở đi) — NGAY SAU khi đọc picker, TRƯỚC mọi phép đếm.
        // `onShopListRead` ở trên vẫn nhận danh sách ĐẦY ĐỦ: lưới "Shops" phải hiện đủ 12 shop của tài khoản,
        // cái bị lọc chỉ là việc của LƯỢT NÀY. Khoá so sánh dùng ĐÚNG biểu thức `shopLogin` của vòng lặp dưới.
        if (boQuaShop is { Count: > 0 })
        {
            var truoc = shops.Count;
            shops = shops.Where(s => !boQuaShop.Contains(KhoaShop(s))).ToList();
            var daBo = truoc - shops.Count;
            if (daBo > 0)
            {
                log?.Invoke($"[{nhan}] Bỏ qua {daBo} shop đã xong hôm nay: "
                    + string.Join(", ", boQuaShop.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                    + $". Còn {shops.Count} shop cho gói này.");
            }
            if (shops.Count == 0)
            {
                return new KetQuaKm(ketQua, false, null);
            }
        }

        log?.Invoke($"[{nhan}] {shops.Count} shop — bắt đầu. Chương trình: {moTaChuongTrinh}."
            + (hanChot is DateTime h ? $" Hạn chót lượt tự động: {h:HH:mm} (không bắt đầu shop mới sau mốc này)." : "")
            + (tranShop > 0 ? $" CHẾ ĐỘ THỬ: chỉ chạy tối đa {tranShop} shop rồi dừng." : ""));

        for (var i = 0; i < shops.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            // TRẦN CHẾ ĐỘ THỬ ⇒ thôi, không bắt đầu shop mới. Kiểm TRƯỚC cửa hết khung: đây là lý do dừng
            // CHỦ ĐỘNG của giai đoạn thử, không phải một sự cố về thời gian — nói nhầm tên là người đọc log
            // đi tìm một mốc hết khung không liên quan. Xem `KmLichTuDong.TranShopLuotCombo`.
            if (tranShop > 0 && i >= tranShop)
            {
                var conLaiThu = shops.Count - i;
                log?.Invoke($"[{nhan}] CHẾ ĐỘ THỬ: đã chạy {i}/{shops.Count} shop — DỪNG theo trần {tranShop} shop "
                    + $"mỗi lượt, còn {conLaiThu} shop chưa chạy. ĐÂY KHÔNG PHẢI LỖI: bỏ trần (đặt hằng "
                    + $"KmLichTuDong.TranShopLuot{loai} về 0) khi muốn chạy hết danh sách shop.");
                return new KetQuaKm(ketQua, false, null, SoShopChuaChay: conLaiThu, TranThuDaCham: tranShop,
                    NgayMaiKhongDo: CaLuotKhongDo());
            }

            // MỘT lượt đọc đồng hồ cho cả hai cửa thời gian dưới đây: hai lượt đọc là hai câu trả lời khác nhau
            // cho cùng một câu hỏi "bây giờ là mấy giờ", và dòng log sẽ in một mốc khác mốc vừa dùng để quyết.
            var nay = dongHo();

            // HẾT KHUNG ⇒ thôi, không bắt đầu shop mới. Kiểm TRƯỚC mỗi shop (kể cả shop đầu): lượt tự động có
            // thể khởi động lúc 05:59 và mất vài phút cho SSO, tới đây đã qua 06:00.
            if (!KmLichTuDong.ConDuocBatDauShopMoi(nay, hanChot))
            {
                var conLai = shops.Count - i;
                log?.Invoke($"[{nhan}] Hết khung tự động ({hanChot:HH:mm}) — DỪNG, còn {conLai} shop chưa chạy "
                    + "(sẽ vào lượt kế theo chu kỳ; chạy tiếp lúc này là giành hồ sơ Brave với vòng check đơn buổi sáng).");
                // MANG THEO HẠN THẬT cho câu tổng kết — sổ P-20260903-7: dòng ở tầng này in đúng `hanChot`, còn
                // câu tổng kết ở tầng App lại in giờ đóng khung của LOẠI (flash: 23:00). Hai dòng nói hai mốc
                // khác nhau cho cùng một sự kiện, và người đọc tin dòng nào cũng sai một nửa.
                return new KetQuaKm(ketQua, false, null, HetKhung: true, SoShopChuaChay: conLai,
                    NgayMaiKhongDo: CaLuotKhongDo(), HanChotDaCham: hanChot);
            }

            // NHƯỜNG CHỖ CHO CHECK ĐƠN ⇒ thôi, không bắt đầu shop mới. Kiểm cùng cụm với cửa hết khung và cùng
            // luật "ở RANH GIỚI shop": một lượt hoãn chạy lại check đơn đã tới mốc và đang NẰM CHỜ gói này xong
            // (xem `AccountsViewModel.ChayLaiCheckDonSauKhiChoAsync`). Trước 03/09/2026 không có cửa này: lượt
            // hoãn cứ mở phiên check đơn đè lên gói đang chạy ⇒ `HttpListenerException` trên cổng 47821 ⇒ lượt
            // dọn hồ sơ GIẾT Brave của gói ⇒ shop đang dở HỎNG (đo thật 18:50–18:52 và 21:2x, 22:0x ngày 03/09).
            if (phaiNhuongSauShop?.Invoke() == true)
            {
                var conLaiNhuong = shops.Count - i;
                log?.Invoke($"[{nhan}] Nhường chỗ cho check đơn — DỪNG sau shop vừa xong, còn {conLaiNhuong} shop "
                    + "cho gói sau. ĐÂY KHÔNG PHẢI LỖI: một lượt chạy lại check đơn đã tới mốc và đang chờ gói "
                    + "này xong; chạy tiếp là hai luồng tranh cùng một hồ sơ Brave và cùng cổng cầu nối.");
                return new KetQuaKm(ketQua, false, null, SoShopChuaChay: conLaiNhuong,
                    NgayMaiKhongDo: CaLuotKhongDo(), NhuongChoCheckDon: true);
            }

            // MỐC BẮT ĐẦU CỦA CHÍNH CHƯƠNG TRÌNH đã trôi qua ⇒ DỪNG. Cửa thứ HAI, và là cửa duy nhất chạy
            // TRONG vòng: cửa kiểm ở tầng form/nhịp (`KmDialogViewModel.KiemTraMocTuongLai` + biên an theo số
            // shop) chỉ chạy MỘT LẦN trước vòng, mà vòng 12 shop mất ~1 tiếng. Ước lượng biên an nào cũng có
            // thể sai (shop chậm, captcha, SSO lâu) — khi nó sai thì phải DỪNG bằng một câu nói đúng nguyên
            // nhân, chứ không phải để 8 shop cuối tự đỏ với câu `"li '14' đang disabled"` trông y hệt lỗi
            // selector. Xem `KetQuaKm.MocBatDauDaTroiQua`.
            if (mocBatDau is DateTime mocBd && mocBd <= nay)
            {
                var conLaiMoc = shops.Count - i;
                log?.Invoke($"[{nhan}] MỐC BẮT ĐẦU của chương trình ({mocBd:dd-MM-yyyy HH:mm}) ĐÃ TRÔI QUA GIỮA "
                    + $"VÒNG (bây giờ {nay:dd-MM-yyyy HH:mm}) — DỪNG, còn {conLaiMoc} shop chưa chạy. "
                    + "ĐÂY KHÔNG PHẢI LỖI SELECTOR: picker của Shopee vô hiệu hoá mọi mốc lùi giờ, nên chạy "
                    + "tiếp thì mọi shop còn lại đều đỏ ở bước đặt thời gian. Cách gỡ: đặt Giờ muộn hơn để cả "
                    + "vòng kịp chạy xong trước mốc đó.");
                return new KetQuaKm(ketQua, false, null,
                    SoShopChuaChay: conLaiMoc, MocBatDauDaTroiQua: mocBd, NgayMaiKhongDo: CaLuotKhongDo());
            }

            var shop = shops[i];
            var shopLogin = KhoaShop(shop);
            log?.Invoke($"[{nhan} {i + 1}/{shops.Count}] {shop.ShopName} (id={shop.ShopId}) — mở Chi tiết...");

            // Số sản phẩm của shop NÀY — phải khai NGOÀI khối try: khối `finally` bên dưới đọc nó để báo "xong
            // shop", và mọi lối ra của thân shop (xong · bỏ qua · hỏng · captcha · huỷ) đều đi qua đó.
            var soSpShop = 0;
            // Thân shop đã CHỐT được một kết quả (thành công · bỏ qua · hỏng) hay chưa. Chỉ dùng cho ca HUỶ —
            // xem khối `finally`. Không phải "đã tạo được": shop hỏng cũng là đã chốt, với số 0.
            var daChotShopNay = false;
            try
            {
                // BẮT ĐẦU shop: ô của loại này hiện "đang chạy…". `soSanPham: null` = chưa có số nào để ghi.
                BaoTienDoChuongTrinh(onShopChuongTrinh, log, loai, shopLogin, true, null);
                try
                {
                    var detailTcs = channel.ArmDetail();
                    await channel.SendAsync(new { action = "openShopDetail", shopId = shop.ShopId }).ConfigureAwait(false);
                    var d = await channel.AwaitAsync(detailTcs, OrdersBridgeChannel.ChoChang.Detail, ct).ConfigureAwait(false);
                    if (channel.CaptchaSeen || d == "captcha")
                    {
                        // `SoShopChuaChay` ở CẢ ba lối ra sớm (phản biện vòng 4): thiếu nó thì `TongShop` co lại
                        // bằng số shop ĐÃ chạy ⇒ câu tổng kết "tạo 0/0 shop" (captcha ngay shop 1) hoặc "tạo 2/3
                        // shop" cho một lượt 12 shop — người đọc tưởng 9 shop kia đã xong từ trước.
                        return new KetQuaKm(ketQua, true, $"Rơi captcha khi mở Chi tiết shop {shop.ShopName}.",
                            SoShopChuaChay: shops.Count - i);
                    }

                    // `token = shopId`: extension trả lại y nguyên, channel bỏ gói LẠC LƯỢT (xem ArmKmChay).
                    var kmTcs = armLuot(channel, shop.ShopId);
                    await channel.SendAsync(new
                    {
                        action,
                        token = shop.ShopId,
                        thamSo = goiThamSo,
                    }).ConfigureAwait(false);
                    var json = await channel.AwaitAsync(kmTcs, hanLuot, ct).ConfigureAwait(false);
                    var mot = DocKetQuaKmMotShop(json, shop);
                    ketQua.Add(mot);
                    // ⚠ HỎI `DaTao` — `SoSanPham` MỘT MÌNH KHÔNG PHẢI "số sp đã vào chương trình". Extension
                    // vẫn trả `soSanPham` ở các lối ra HỎNG (`flow-km.js` bước 7/8, `flow-combo.js` cùng khuôn):
                    // đó là số sp đã nạp vào FORM, còn bước TẠO thật là bước 9. Shop 240 sp hỏng ở bước 8 mà ô
                    // lưới hiện 240 là phần mềm nói dối — Shopee không có chương trình nào.
                    // Cùng luật với `CauKetQuaShop` (chỉ in số ở nhánh thành công) và `KetQuaKm.SoTao`
                    // (`Count(s => s.DaTao)`). GHI 0 chứ KHÔNG bỏ ghi: 0 giữ nghĩa "đã chạy mà không tạo được gì".
                    soSpShop = mot.DaTao ? mot.SoSanPham : 0;
                    daChotShopNay = true;
                    log?.Invoke(CauKetQuaShop(i + 1, shops.Count, mot, nhan));

                    // CAPTCHA GIỮA CHẶNG chayKm — extension gửi {action:"captcha"} rồi dừng luồng. Bản trước chỉ
                    // kiểm sau `openShopDetail`, nên rơi verify ở bước 4 của shop #2 là vòng vẫn đi tiếp 10 shop
                    // nữa trên một phiên Shopee đang nghi ngờ. Giữ quyết định "captcha thì dừng hẳn" (plan §11).
                    // HAI NGUỒN, không phải một: cờ `CaptchaSeen` đến từ gói `{action:"captcha"}` đi ĐƯỜNG RIÊNG,
                    // còn `mot.Captcha` nằm trong CHÍNH gói kết quả. Đường riêng có thể tới muộn hơn lượt đọc kết
                    // quả này (hai gói, hai lượt bơm WebSocket) — lúc đó chỉ còn cờ trong gói cứu được cả vòng.
                    if (channel.CaptchaSeen || mot.Captcha)
                    {
                        return new KetQuaKm(ketQua, true, $"Rơi captcha khi chạy {nhan} ở shop {shop.ShopName} — dừng cả lượt.",
                            SoShopChuaChay: shops.Count - i - 1);
                    }

                    // NGÀY ĐÍCH KHÔNG ĐỎ (chỉ vòng FLASH SALE) — shop này coi như BỎ QUA rồi ĐI TIẾP.
                    //
                    // ⚠ BẢN TRƯỚC DỪNG CẢ LƯỢT Ở ĐÂY, và đó là một lỗi thật (sổ P-20260831-3, xử 01/09/2026).
                    // Lý do cũ — "lịch khung là của SHOPEE, 11 shop kia đọc cùng cái lịch ấy" — sai một nửa: đo
                    // 16:31→16:33 ngày 31/08 cho thấy lịch MẤT ĐỎ cả khi chỉ RIÊNG shop này đã kín khung. Tức
                    // một shop kín giết oan mọi shop còn lại, câm lặng. Và cái giá mà early-return mua về ("tiết
                    // kiệm 11 lượt chờ render") không còn tồn tại từ khi lượt flash là GÓI ≤2–3 shop.
                    //
                    // Kết luận CẢ NGÀY nay chỉ rút ra khi MỌI shop của lượt cùng khai cờ mà không shop nào tạo
                    // được — xem `CaLuotKhongDo` ở cuối hàm.
                    if (mot.NgayMaiKhongDo)
                    {
                        log?.Invoke($"[{nhan} {i + 1}/{shops.Count}] {shop.ShopName}: ngày đích KHÔNG còn đỏ "
                            + "(shop này đã kín khung, HOẶC Shopee chưa mở / đã đóng đăng ký cho ngày đó — từ "
                            + "một shop thì KHÔNG phân biệt được) — bỏ qua shop này, đi tiếp. ĐÂY KHÔNG PHẢI LỖI.");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // Shop này hỏng — GHI rồi đi tiếp. Xem xmldoc: một shop không được giết cả lượt.
                    ketQua.Add(new KetQuaKmMotShop(shop.ShopId, shop.ShopName, false, 0, null, ex.Message,
                        ShopLogin: shopLogin));
                    daChotShopNay = true;   // hỏng CŨNG là đã chốt: ô lưới phải hiện `0`, không phải `—`
                    log?.Invoke($"[{nhan} {i + 1}/{shops.Count}] {shop.ShopName}: HỎNG — {ex.Message}. Bỏ qua shop này, đi tiếp.");
                }
            }
            finally
            {
                // XONG shop — GHI SỐ NGAY TẠI ĐÂY, tuyệt đối KHÔNG gom về cuối lượt. Vòng này có NĂM lối ra sớm
                // (trần thử · hết khung · mốc bắt đầu trôi qua · captcha ×2 · không về được picker); gom về cuối
                // là mọi lối ra sớm đều mất sạch số của các shop đã chạy xong.
                //
                // Và phải nằm trong `finally`: shop bỏ qua (modal rỗng), shop hỏng, shop rơi captcha đều VẪN ghi
                // một dòng — kể cả với số 0. Đó là điều kiện để chữ `0` ở ô lưới có nghĩa "đã chạy mà không tạo
                // được sp nào", phân biệt được với `—` = "hôm đó không chạy loại này".
                //
                // NGOẠI LỆ DUY NHẤT — HUỶ GIỮA SHOP: người dùng bấm Dừng lúc shop đang chờ ⇒ `OperationCanceled`
                // ⇒ vào đây với thân shop CHƯA chốt được gì. Ghi `0` lúc đó là nói "đã chạy mà không tạo được sp
                // nào", trong khi sự thật là "bị cắt ngang" — làm mờ đúng ranh giới `—`/`0` mà cả thiết kế dựng
                // lên. Vẫn PHẢI bắn lượt "xong" (với `null`) để tắt chữ "đang chạy…", kẻo dòng đó quay mãi.
                // Hỏi thêm `daChotShopNay`, KHÔNG chỉ hỏi token: shop vừa chốt xong mà cú bấm Dừng tới ngay sau
                // đó thì số ĐÃ CÓ THẬT — vứt nó đi là lưới hiện `—` cho một shop mà câu tổng kết đã đếm là "đã tạo".
                var biCatGiuaChung = ct.IsCancellationRequested && !daChotShopNay;
                BaoTienDoChuongTrinh(
                    onShopChuongTrinh, log, loai, shopLogin, false, biCatGiuaChung ? null : soSpShop);
            }

            // Về trang chọn shop. Hỏng chỗ này thì shop kế không mở được ⇒ dừng, khác với hỏng THÂN một shop.
            var ve = await vePicker(ct).ConfigureAwait(false);
            if (ve != KetQuaVePicker.VeDuoc)
            {
                return new KetQuaKm(ketQua, false, $"Không về được trang chọn shop sau shop {shop.ShopName} — dừng lượt.",
                    SoShopChuaChay: shops.Count - i - 1, NgayMaiKhongDo: CaLuotKhongDo());
            }

            // NGHỈ CHỈ KHI THẬT SỰ CÒN SHOP SẼ CHẠY. BA vế, không phải một — mỗi vế ứng với một cửa dừng ở ĐẦU
            // vòng lặp kế, và thiếu vế nào là nằm nghỉ 3–5 phút rồi mới dừng vì đúng cửa đó:
            //  · `i < shops.Count - 1` — hết danh sách thì không nghỉ (vế cũ);
            //  · `tranShop` — trần chế độ thử. ⚠ Với `TranShopLuotFlash`/`TranShopLuotCombo` đã gỡ về 0 thì vế
            //    này là NHÁNH CHẾT (luôn true); giữ vì nó là công tắc, bật lại lúc nào cũng được;
            //  · `ConDuocBatDauShopMoi` — HẠN của gói/khung. Đây là vế SỐNG của bản gói–khe nghỉ 01/09/2026:
            //    một gói flash bị hạn cắt ở shop thứ hai mà vẫn nằm nghỉ 3–5 phút thì ngần ấy phút nằm TRONG
            //    khe nghỉ mà gói đang mượn, và check đơn ở MỌI máy vẫn đang bị dừng vì khoá xuyên máy chưa nhả.
            //
            // Ca thật 16:51→16:56 ngày 26/08/2026 (vòng combo, vế trần): shop 2 xong lúc 16:51:52, nghỉ tới
            // 16:56:28 mới in dòng "DỪNG theo trần". Bốn phút rưỡi đó KHÔNG chạy gì. Từ 01/09 vế trần không còn
            // dựng lại được ca ấy (trần 0), nhưng vế HẠN thì dựng lại y hệt — nên vế mới không phải cho gọn.
            //
            // Đọc đồng hồ MỘT LẦN nữa ở đây, không dùng lại `nay` của đầu vòng: giữa hai mốc ấy là cả một shop
            // (~35–45 phút), tức `nay` gần như luôn nói "còn kịp" cho một gói thật ra đã hết giờ.
            var conShopSeChay = i < shops.Count - 1
                && (tranShop <= 0 || i + 1 < tranShop)
                && KmLichTuDong.ConDuocBatDauShopMoi(dongHo(), hanChot);
            if (conShopSeChay) { await nghiGiuaShop(ct).ConfigureAwait(false); }
        }

        return new KetQuaKm(ketQua, false, null, NgayMaiKhongDo: CaLuotKhongDo());
    }

    /// <summary>
    /// Bắn MỘT lượt tiến độ ba cột KM | Combo | Flash Sale — <b>và NUỐT MỌI lỗi của callback</b>, chỉ để lại dòng
    /// nhật ký. Ghi số là việc PHỤ; nó không có quyền giết vòng chạy.
    /// <para><b>Vì sao bắt buộc phải bọc</b> (phản biện 27/08/2026, lỗi NẶNG về hậu quả): thân callback mở SQLite
    /// và <c>ExecuteNonQuery</c> — một <c>SQLITE_BUSY</c> là ném ngoại lệ, mà lượt bắn "xong shop" nằm TRONG khối
    /// <c>finally</c>. Chuỗi hậu quả: ngoại lệ ném từ <c>finally</c> ⇒ thoát thẳng
    /// <see cref="ChayVongTaoChuongTrinhAsync"/> ⇒ <b>vứt luôn <c>ketQua</c> đã tích luỹ</b> ⇒ câu tổng kết báo
    /// "tạo 0 shop" dù 8 shop đã tạo THẬT trên Shopee ⇒ <c>ChotLuotKm</c> thấy <c>SoTao == 0</c> nên KHÔNG ghi
    /// <c>LanChayCuoi</c> ⇒ đêm sau lại tới hạn ⇒ 8 shop đó nhận chương trình THỨ HAI.</para>
    /// <para>Phụ, nhưng cũng thật: ngoại lệ ném từ <c>finally</c> trong lúc một <see cref="OperationCanceledException"/>
    /// đang bay sẽ THAY THẾ nó ⇒ <c>catch (OperationCanceledException) { throw; }</c> ở tầng ngoài không còn khớp ⇒
    /// nhật ký ghi "Cầu nối lỗi" thay vì "Đã hủy".</para>
    /// </summary>
    private static void BaoTienDoChuongTrinh(
        Action<LoaiChuongTrinh, string, bool, int?>? onShopChuongTrinh, Action<string>? log,
        LoaiChuongTrinh loai, string shopLogin, bool dangChay, int? soSanPham)
    {
        try
        {
            onShopChuongTrinh?.Invoke(loai, shopLogin, dangChay, soSanPham);
        }
        catch (Exception ex)
        {
            log?.Invoke($"Ghi tiến độ chương trình cho shop {shopLogin} lỗi (BỎ QUA, vòng chạy tiếp): "
                + ex.ToString());
        }
    }

    /// <summary>
    /// <b>KHÓA của một shop</b>: <c>LoginName</c> ưu tiên, thiếu thì <c>ShopName</c> — ĐÚNG biểu thức của
    /// <c>ResultsRepository.UpsertShops</c> và của vòng check đơn.
    /// <para>⚠ ĐỪNG lấy <c>ShopName</c> cho tiện vì đó là thứ dòng log đang in: shop nào có tên hiển thị khác
    /// login sẽ ghi vào một khóa KHÔNG dòng lưới nào khớp — không lỗi, không log, chỉ là ba cột trống trơn.</para>
    /// <para>Tách thành hàm từ 01/09/2026: bộ lọc <c>boQuaShop</c> của gói flash phải so bằng ĐÚNG khóa mà vòng
    /// lặp dùng, và hai bản chép tay cùng biểu thức là hai bản sẽ trôi khỏi nhau.</para>
    /// </summary>
    internal static string KhoaShop(ShopListItem shop)
        => string.IsNullOrWhiteSpace(shop.LoginName) ? shop.ShopName : shop.LoginName;

    /// <summary>Đọc gói JSON <c>kmKetQua</c> của extension thành <see cref="KetQuaKmMotShop"/>. Tách riêng để
    /// test được không cần trình duyệt.</summary>
    internal static KetQuaKmMotShop DocKetQuaKmMotShop(string? json, ShopListItem shop)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new KetQuaKmMotShop(shop.ShopId, shop.ShopName, false, 0, null, "extension không trả kết quả",
                ShopLogin: KhoaShop(shop));
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var daTao = root.TryGetProperty("daTao", out var t) && t.ValueKind == JsonValueKind.True;
            var soSp = root.TryGetProperty("soSanPham", out var n) && n.TryGetInt32(out var v) ? v : 0;
            var boQua = root.TryGetProperty("boQua", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;

            var buocHongDau = DocBuocHongDau(root);

            string? loi = null;
            if (!daTao && boQua is null)
            {
                // KHÔNG tạo được mà cũng không phải "bỏ qua có chủ đích" thì là HỎNG. Nêu ĐÍCH DANH bước hỏng đầu
                // tiên, đừng chỉ nói "thất bại" — nhật ký không chỉ ra bước nào thì lượt sau lại phải dò lại từ đầu.
                loi = buocHongDau is null ? "không tạo được chương trình" : "hỏng ở bước: " + buocHongDau;
            }

            // CẢNH BÁO — chương trình VẪN tạo được nhưng có thứ đáng ngờ. Hai nguồn:
            //  · `canhBao` của extension (hiện có: chạm trần phân trang ⇒ shop còn sản phẩm CHƯA được chọn);
            //  · PHÒNG THỦ: `daTao == true` mà vẫn kèm bước hỏng. Luồng mới dừng shop ngay khi có bước hỏng nên
            //    ca này KHÔNG được phép xảy ra — lọt tới đây nghĩa là chốt kia đã vỡ, và im lặng nuốt nó thì
            //    không ai biết. Ghi thành cảnh báo chứ không thành lỗi: chương trình có thật trên Shopee rồi.
            var dsCanhBao = new List<string>();
            if (root.TryGetProperty("canhBao", out var cb) && cb.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in cb.EnumerateArray())
                {
                    if (c.ValueKind == JsonValueKind.String && c.GetString() is { Length: > 0 } s) { dsCanhBao.Add(s); }
                }
            }
            if (daTao && buocHongDau is not null)
            {
                dsCanhBao.Add("báo TẠO ĐƯỢC nhưng vẫn có bước hỏng: " + buocHongDau);
            }
            var canhBao = dsCanhBao.Count == 0 ? null : string.Join(" · ", dsCanhBao);

            // Cờ captcha NẰM TRONG gói kết quả (extension đặt khi một bước bắt được URL /verify). Xem xmldoc
            // của `KetQuaKmMotShop.Captcha`: đây là đường thứ hai, độc lập với gói `{action:"captcha"}`.
            var captcha = root.TryGetProperty("captcha", out var cp) && cp.ValueKind == JsonValueKind.True;

            // Cờ RIÊNG của luồng Flash Sale — hai luồng kia không gửi trường này nên nó luôn `false` ở đó.
            var ngayMaiKhongDo = root.TryGetProperty("ngayMaiKhongDo", out var nm)
                && nm.ValueKind == JsonValueKind.True;

            // Hai con số RIÊNG của luồng Flash Sale: khung tạo mới · chương trình tái chế. VẮNG ⇒ 0, không phải
            // lỗi — hai luồng kia không gửi, và bản extension cũ chưa gửi `soTaiChe`.
            var soKhungDaTao = DocSoNguyen(root, "soKhungDaTao");
            var soTaiChe = DocSoNguyen(root, "soTaiChe");

            return new KetQuaKmMotShop(
                shop.ShopId, shop.ShopName, daTao, soSp, boQua, loi, canhBao, captcha, ngayMaiKhongDo,
                soKhungDaTao, soTaiChe, KhoaShop(shop));
        }
        catch (JsonException ex)
        {
            return new KetQuaKmMotShop(shop.ShopId, shop.ShopName, false, 0, null,
                "kết quả KM không đọc được: " + ex.Message, ShopLogin: KhoaShop(shop));
        }
    }

    /// <summary>Đọc một trường SỐ NGUYÊN của gói kết quả; vắng / sai kiểu ⇒ <c>0</c>. Trường vắng KHÔNG phải lỗi:
    /// hai luồng KM/Combo không gửi mấy trường riêng của Flash, và bản extension cũ trên máy người dùng có thể
    /// chưa gửi trường mới thêm.</summary>
    private static int DocSoNguyen(JsonElement root, string ten)
        => root.TryGetProperty(ten, out var el) && el.ValueKind == JsonValueKind.Number
            && el.TryGetInt32(out var v)
            ? v
            : 0;

    /// <summary>Mô tả bước hỏng ĐẦU TIÊN trong gói <c>kmKetQua</c> (<c>"tên — lý do"</c>); không có ⇒ null.</summary>
    private static string? DocBuocHongDau(JsonElement root)
    {
        if (!root.TryGetProperty("buocHong", out var bh) || bh.ValueKind != JsonValueKind.Array || bh.GetArrayLength() == 0)
        {
            return null;
        }
        var b0 = bh[0];
        var ten = b0.TryGetProperty("ten", out var bt) ? bt.GetString() : null;
        var chiTiet = b0.TryGetProperty("loi", out var bl) && bl.ValueKind == JsonValueKind.String ? bl.GetString() : null;
        return (ten ?? "?") + (chiTiet is null ? "" : " — " + chiTiet);
    }

    /// <summary>Câu nhật ký cho MỘT shop — tách riêng để test được từng ca (tạo được / bỏ qua / hỏng).</summary>
    internal static string CauKetQuaShop(int thuTu, int tong, KetQuaKmMotShop kq, string nhan = "KM")
    {
        var dau = $"[{nhan} {thuTu}/{tong}] {kq.ShopName}: ";
        // Cảnh báo đi kèm MỌI ca: nó nói về thứ đã xảy ra trên shop thật, không được rơi mất ở ca nào.
        var duoi = kq.CanhBao is null ? string.Empty : " ⚠ CẢNH BÁO: " + kq.CanhBao + ".";
        if (kq.Loi is not null) { return dau + "HỎNG — " + kq.Loi + ". Bỏ qua shop này, đi tiếp." + duoi; }
        if (kq.BoQua is not null) { return dau + "BỎ QUA — " + kq.BoQua + "." + duoi; }

        // Hai số RIÊNG của vòng Flash (khung tạo mới · chương trình tái chế) — chỉ in khi CÓ, nên hai vòng kia
        // giữ nguyên câu cũ từng chữ. Không in ra thì một shop tái chế 3 chương trình trông y hệt một shop tạo
        // mới 3 khung, mà hai việc đó tốn thời gian khác hẳn nhau (sổ P-20260831-5).
        var soKhung = kq.SoKhungDaTao > 0 || kq.SoTaiChe > 0
            ? $" ({kq.SoKhungDaTao} khung tạo mới, {kq.SoTaiChe} tái chế)"
            : string.Empty;
        return dau + $"đã tạo chương trình cho {kq.SoSanPham} sản phẩm{soKhung}." + duoi;
    }

    /// <summary>
    /// <b>Nút "Mở list shop"</b> — đi ĐÚNG luồng chạy tài khoản (mở trình duyệt sạch + extension bằng hồ sơ của
    /// tài khoản → SSO "Kênh Người bán" → trang chọn shop) rồi <b>DỪNG NGAY tại trang chọn shop</b>: đọc xong danh
    /// sách shop là hết việc, KHÔNG mở "Chi tiết" shop nào, KHÔNG lặp shop.
    /// <para><b>KHÔNG đóng trình duyệt ở BẤT KỲ lối ra nào — kể cả lối LỖI. ĐỪNG SỬA.</b> Cả mục đích của nút là
    /// giao lại cửa sổ Brave đang đăng nhập cho người dùng thao tác tay; và khi hỏng thì chính cái cửa sổ đang kẹt
    /// mới là bằng chứng để biết kẹt ở đâu. Tầng App (<c>AccountsViewModel.TryKillPoc</c>) mới là chỗ đóng, theo
    /// các nút Dừng/Chạy.</para>
    /// <para>Cửa sổ để lại nằm ở <see cref="Process"/> — caller PHẢI giữ lấy nó rồi <see cref="Dispose"/> kênh
    /// (nhả cổng cầu nối 47821), kẻo phiên production sau không mở nổi cổng.</para>
    /// </summary>
    public async Task<OrdersBridgeSliceResult> MoDenDanhSachShopAsync(
        OrdersLoginParams login, CancellationToken ct = default)
    {
        try
        {
            var err = await LoginAndReachPickerAsync(login, ct).ConfigureAwait(false);
            if (_channel.CaptchaSeen)
            {
                return new OrdersBridgeSliceResult(Array.Empty<ShopListItem>(), null, null, true,
                    "Rơi vào trang verify/captcha khi vào Seller Centre.");
            }
            if (err is not null)
            {
                return Fail(err);
            }

            return await DocXongThiDungAsync(_channel, _onShopListRead, _log, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; } // người dùng bấm Dừng — KHÔNG gói thành "lỗi"
        catch (TimeoutException)
        {
            L("Cầu nối: hết thời gian chờ phản hồi từ extension.");
            return Fail("Hết thời gian chờ phản hồi từ extension.");
        }
        catch (Exception ex)
        {
            L("Cầu nối lỗi: " + ex.ToString());
            return Fail(ex.Message);
        }
    }

    /// <summary>
    /// Toàn bộ phần SAU khi đã về được trang chọn shop của <see cref="MoDenDanhSachShopAsync"/>: đọc danh sách
    /// shop → ghi câu tổng kết → nhả <c>chrome.debugger</c> → trả kết quả. <b>Hết. Không có bước thứ tư.</b>
    /// <para><b>Vì sao tách ra <c>static</c> nhận <paramref name="channel"/></b> (khuôn
    /// <see cref="SsoQuaCauNoiAsync"/>): đây là chỗ DUY NHẤT có thể lặng lẽ mọc thêm một lệnh tác động lên shop
    /// (<c>openShopDetail</c>) và biến "mở list shop" thành "chạy nửa vòng". Ở dạng này, <c>BridgeTestRig</c> lái
    /// được nó bằng cầu nối thật và khẳng định chuỗi lệnh gửi đi ĐÚNG BẰNG
    /// <c>[readShopList, detachDebugger]</c> — không thiếu, không thừa. Gói kín trong
    /// <see cref="MoDenDanhSachShopAsync"/> (có bước đăng nhập phóng trình duyệt thật) thì không test được, và
    /// một test chỉ phủ mỗi hàm đọc-danh-sách 4 dòng là test đo hụt phạm vi: hàm đó vốn KHÔNG THỂ gửi thêm lệnh
    /// nên khẳng định của nó luôn đúng bất kể ai nhét gì vào đây.</para>
    /// </summary>
    internal static async Task<OrdersBridgeSliceResult> DocXongThiDungAsync(
        OrdersBridgeChannel channel, Action<IReadOnlyList<ShopListItem>>? onShopListRead,
        Action<string>? log, CancellationToken ct)
    {
        var shops = await DocDanhSachShopAsync(channel, onShopListRead, ct).ConfigureAwait(false);
        log?.Invoke($"Đã tới trang chọn shop — {shops.Count} shop. DỪNG tại đây, cửa sổ Brave để nguyên cho bạn xem.");
        await ThuNhaDebuggerAsync(channel, log, ct).ConfigureAwait(false);
        return new OrdersBridgeSliceResult(shops, null, null, false, null);
    }

    /// <summary>
    /// Bảo extension NHẢ <c>chrome.debugger</c> khỏi tab picker trước khi ta buông cửa sổ cho người dùng —
    /// <c>gotoSellerCentre</c> attach vào tab đó và cố ý KHÔNG detach (giữ attach xuyên suốt để cú click trusted
    /// không trượt), nên cửa sổ để lại sẽ đeo banner "đang gỡ lỗi trình duyệt" chình ình trên đầu.
    /// <para><b>Best-effort tuyệt đối</b>: không Arm chặng nào, không chờ phản hồi (extension cố ý không trả lời),
    /// chỉ nghỉ một nhịp ngắn cho lệnh kịp bay. Extension bản CŨ không có nhánh <c>detachDebugger</c> thì action bị
    /// nuốt im lặng — <c>background.js</c> không có nhánh <c>default</c> — nên gửi thừa vô hại.</para>
    /// <para>HỦY vẫn ném tiếp: người dùng bấm Dừng ngay lúc này thì đó là Dừng thật, không phải "lỗi nhả debugger".
    /// Mọi hỏng hóc KHÁC chỉ ghi nhật ký, tuyệt đối không được biến kết quả đã đọc xong thành lỗi.</para>
    /// </summary>
    private static async Task ThuNhaDebuggerAsync(
        OrdersBridgeChannel channel, Action<string>? log, CancellationToken ct)
    {
        try
        {
            await channel.SendAsync(new { action = "detachDebugger" }).ConfigureAwait(false);
            await Task.Delay(NhipNhaDebugger, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { log?.Invoke("Không nhả được chrome.debugger khỏi tab picker: " + ex.ToString()); }
    }

    /// <summary>Nghỉ cho lệnh <c>detachDebugger</c> kịp bay sang extension và chạy xong — không có chặng chờ nào
    /// để bám (extension cố ý không trả lời), nên đây là tất cả những gì ta làm được.</summary>
    private static readonly TimeSpan NhipNhaDebugger = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// PURE — việc phải làm sau MỘT lượt thử bản sạch. Bảng quyết định của thứ tự "sạch trước, Playwright chỉ khi
    /// cần" (user chốt 2026-08-07): cookie hồ sơ còn hạn thì KHỎI mở trình duyệt điều khiển; hỏng vì bất kỳ lý do
    /// nào KHÁC captcha thì đăng nhập lại rồi thử NỐT một lượt nữa; đã đăng nhập lại mà vẫn hỏng thì báo lỗi, chờ
    /// vòng sau.
    /// <para><b>Vì sao fallback cho MỌI lỗi, không chỉ "gặp form đăng nhập":</b> extension báo lý do bằng một câu
    /// tiếng Việt trong <c>action:"error"</c> — so khớp câu chữ đó là mong manh, sửa một dấu phẩy bên extension là
    /// mất nhánh đăng nhập lại. Lỗi SSO nào cũng có thể là cookie hỏng, nên cứ đăng nhập lại một lần.</para>
    /// <para><b>Riêng captcha KHÔNG đăng nhập lại:</b> đẩy Playwright vào lúc Shopee đang nghi ngờ là tự khai bot.</para>
    /// <para><b>Treo (không phản hồi) cũng KHÔNG đăng nhập lại</b> — xem <see cref="KetQuaSso.Treo"/>: hết giờ
    /// chặng có thể là captcha mà extension chưa kịp báo.</para>
    /// </summary>
    /// <param name="ket">Kết quả lượt thử vừa rồi.</param>
    /// <param name="daFallback">Lượt này CÓ PHẢI đã đi qua bước đăng nhập lại chưa (true = lượt hai, hết đường lui).</param>
    internal static HanhDongSauThuSach QuyetDinhSauThuBanSach(KetQuaSso ket, bool daFallback) => ket switch
    {
        KetQuaSso.Ok => HanhDongSauThuSach.ChayTiep,
        KetQuaSso.Captcha => HanhDongSauThuSach.DungVongCaptcha,
        KetQuaSso.Treo => HanhDongSauThuSach.BaoLoi,
        _ => daFallback ? HanhDongSauThuSach.BaoLoi : HanhDongSauThuSach.DangNhapLai,
    };

    /// <summary>
    /// TRẦN số shop HỎNG LIÊN TIẾP trước khi bỏ cả vòng. Hỏng lẻ tẻ thường chỉ là chuyện của một shop (trang
    /// không render, extension kẹt một nhịp) nên vòng phải đi tiếp; nhưng hỏng liên tiếp tới ngần này thì gần
    /// như chắc chắn cầu nối/trình duyệt đã chết hẳn, mỗi shop còn lại chỉ đốt thêm vài phút chờ vô ích.
    /// <para>3 = đủ rộng để tha cho một chuỗi xui ngắn, đủ hẹp để không nướng cả 12 shop.</para>
    /// </summary>
    internal const int TranShopHongLienTiep = 3;

    /// <summary>
    /// PURE — việc phải làm sau khi thân MỘT shop ném ngoại lệ. Luật: <b>một shop hỏng KHÔNG được giết cả vòng</b>
    /// (13:37 ngày 10/08/2026: một <c>TimeoutException</c> ở shop 6/12 kéo theo 6 shop chưa chạy), nhưng có hai
    /// đường vẫn phải dừng — chạm trần hỏng liên tiếp, và không quay lại được trang chọn shop.
    /// <para>Trần xét TRƯỚC: cả hai cùng đúng thì lý do "cầu nối chết" mới là lý do thật, còn "không về được
    /// picker" chỉ là hệ quả của nó.</para>
    /// <para><b>Chạm trần KHÔNG còn nghĩa là dừng vòng</b> (sửa 18/08/2026). Bản trước dừng thẳng với lý lẽ "mở
    /// lại chỉ đổi chỗ đốt thời gian"; nhật ký 8 ngày nói ngược — xem
    /// <see cref="SauShopHong.MoLaiTrinhDuyetHongLienTiep"/>. Nay chạm trần chỉ dừng khi hết đường cứu.</para>
    /// <para>Ba điều kiện cứu phải xét ĐÚNG THỨ TỰ của <see cref="QuyetDinhKhiMatPicker"/> — captcha trước, rồi
    /// còn-việc-phía-sau, rồi trần mở lại — vì chỗ gọi dùng kết luận của hàm này để rơi vào ĐÚNG khối mở lại đã
    /// có, mà khối đó hỏi lại <see cref="QuyetDinhKhiMatPicker"/>. Hai hàm lệch kết luận là vòng vừa quyết mở lại
    /// đã rơi vào <see cref="KhiMatPicker.DungVong"/> — mở lại thành câu nói suông.</para>
    /// </summary>
    /// <param name="hongLienTiep">Số shop hỏng LIÊN TIẾP tính CẢ shop vừa hỏng (chuỗi đứt khi có shop chạy trọn).</param>
    /// <param name="conShopPhiaSau">Còn shop chưa chạy hay không — shop CUỐI thì picker hỏng không hại ai.</param>
    /// <param name="veDuocPicker">Lượt đóng tab shop có đưa được về trang chọn shop không (không cần thử thì
    /// truyền <c>true</c>).</param>
    /// <param name="conLuotMoLai">Còn lượt mở lại trình duyệt trong vòng này không (xem
    /// <see cref="TranMoLaiTrinhDuyet"/>).</param>
    /// <param name="captcha">Phiên đang dính cờ captcha/verify — đẩy thêm một lượt mở trình duyệt vào đúng lúc
    /// Shopee đang nghi là bot chính là tự khai, cùng lý lẽ với <see cref="QuyetDinhKhiMatPicker"/>.</param>
    internal static SauShopHong QuyetDinhSauShopHong(
        int hongLienTiep, bool conShopPhiaSau, bool veDuocPicker, bool conLuotMoLai, bool captcha)
    {
        if (hongLienTiep >= TranShopHongLienTiep)
        {
            return !captcha && conShopPhiaSau && conLuotMoLai
                ? SauShopHong.MoLaiTrinhDuyetHongLienTiep
                : SauShopHong.DungVongHongLienTiep;
        }
        if (conShopPhiaSau && !veDuocPicker) { return SauShopHong.DungVongKhongVePicker; }
        return SauShopHong.ChayTiepShopKe;
    }

    /// <summary>
    /// TRẦN tổng số shop được XẾP VÀO HÀNG ĐỢI THỬ LẠI trong MỘT vòng (mỗi shop nhiều nhất 1 lượt). Thử lại là
    /// đường cứu cho shop <b>rơi oan</b> vì cầu nối đứt — trang không hỏng, chỉ là ta mất câu trả lời — nên nó
    /// đáng một lượt nữa sau khi hết 12 shop, lúc nhịp đứt ~240s của network service trình duyệt đã dịch đi.
    /// <para>3 = đủ cứu một vòng xui bình thường (vòng 19:01 ngày 10/08/2026 rơi đúng 3 shop kiểu này), đủ hẹp để
    /// một vòng hỏng nặng KHÔNG biến thành lượt hai chạy lại gần hết danh sách shop.</para>
    /// </summary>
    internal const int TranThuLaiShopMoiVong = 3;

    /// <summary>
    /// PURE — lỗi làm hỏng một shop có phải kiểu "RƠI OAN VÌ CẦU NỐI" không: cầu nối rớt giữa chặng
    /// (<see cref="CauNoiRotGiuaChangException"/>) hoặc hết hạn chờ phản hồi extension
    /// (<see cref="TimeoutException"/>). Cả hai đều nói "ta mất câu trả lời", KHÔNG nói "trang Seller Centre hỏng".
    /// <para>Một dòng <c>is TimeoutException</c> là đủ vì <see cref="CauNoiRotGiuaChangException"/> KẾ THỪA
    /// <see cref="TimeoutException"/> — hàm này tồn tại để ghi rõ luật đó ra, kẻo ai đó tách cây thừa kế rồi
    /// lặng lẽ mất nhánh thử lại.</para>
    /// <para>Thành viên thứ ba (11/08/2026): extension tự khai "MẤT NGỮ CẢNH TAB SHOP" (service worker MV3 vừa bị
    /// giết + khôi phục hỏng — <see cref="OrdersBridgeChannel.MocLoiMatTabShop"/>). Cùng bản chất hạ tầng cầu nối
    /// chết đi sống lại; xếp khác nhóm với TimeoutException chỉ vì nó đi qua đường <c>error</c> →
    /// <c>InvalidOperationException</c>. Không nhận diện thì shop dính nó mất TRỌN vòng (không được thử lại) và 3
    /// shop liên tiếp là dừng cả vòng — trong khi cùng cú SW chết mà biểu hiện bằng rớt socket thì lại được cứu.
    /// An toàn vì <see cref="NenThuLaiShopRoiOan"/> vẫn xét <c>DaGuiChuanBiHang</c> TRƯỚC mọi điều kiện khác.</para>
    /// <para>Thành viên thứ tư (18/08/2026): extension bấm Chi tiết rồi chờ hết 30s mà tab shop không mở
    /// (<see cref="OrdersBridgeChannel.MocLoiKhongMoDuocTabShop"/>) — 18 lần trong 8 ngày, gom thành 6 chuỗi đúng
    /// 3 shop liên tiếp. Cùng gốc hạ tầng: tab shop mồ côi để lại từ lượt hết giờ trước + SW MV3 chết theo chu kỳ
    /// làm <c>listTabId</c> bám nhầm tab. Cũng an toàn tuyệt đối với ràng buộc in phiếu: lỗi rơi ở bước ĐẦU TIÊN
    /// của shop nên <c>DaGuiChuanBiHang</c> chắc chắn còn tắt.</para>
    /// </summary>
    internal static bool LaLoiCauNoi(Exception ex) =>
        ex is TimeoutException
        || (ex is InvalidOperationException
            && (ex.Message.Contains(OrdersBridgeChannel.MocLoiMatTabShop, StringComparison.Ordinal)
                || ex.Message.Contains(OrdersBridgeChannel.MocLoiKhongMoDuocTabShop, StringComparison.Ordinal)));

    /// <summary>
    /// PURE — shop vừa hỏng có được XẾP VÀO HÀNG ĐỢI THỬ LẠI cuối vòng không.
    /// <para><b>RÀNG BUỘC SỐNG CÒN, xét TRƯỚC mọi điều kiện khác:</b> shop đã gửi <c>prepareNextOrder</c>
    /// (<see cref="ShopFlowRunner.DaGuiChuanBiHang"/>) thì TUYỆT ĐỐI KHÔNG chạy lại — lệnh đó chuẩn bị hàng + in
    /// phiếu trên đơn THẬT, chạy lại là đếm trùng, in phiếu trùng, đụng đơn của người dùng. Điều này quan trọng
    /// hơn cả mục tiêu đi trọn 12/12 shop.</para>
    /// </summary>
    /// <param name="daGuiChuanBiHang">Shop này đã gửi <c>prepareNextOrder</c> lần nào chưa.</param>
    /// <param name="loiCauNoi">Lỗi làm hỏng shop có thuộc nhóm cầu nối không (xem <see cref="LaLoiCauNoi"/>).</param>
    /// <param name="daXongViecShop">Thân shop ĐÃ chạy trọn, chỉ bước dọn dẹp sau đó (đóng tab về picker) mới ném.
    /// Shop như vậy không có gì để làm lại — chạy lại là cộng đôi <c>shopsDone</c> và đọc lại đơn vô ích.</param>
    /// <param name="daThuLaiShopNay">Shop này đã được xếp/đã chạy lượt thử lại rồi (mỗi shop nhiều nhất 1 lượt).</param>
    /// <param name="soShopDaXepThuLai">Số shop ĐÃ nằm trong hàng đợi thử lại của vòng này.</param>
    internal static bool NenThuLaiShopRoiOan(
        bool daGuiChuanBiHang, bool loiCauNoi, bool daXongViecShop, bool daThuLaiShopNay, int soShopDaXepThuLai)
    {
        if (daGuiChuanBiHang) { return false; }
        if (!loiCauNoi) { return false; }
        if (daXongViecShop) { return false; }
        if (daThuLaiShopNay) { return false; }
        return soShopDaXepThuLai < TranThuLaiShopMoiVong;
    }

    /// <summary>
    /// TRẦN số lần MỞ LẠI trình duyệt sạch trong MỘT vòng. Mất trang chọn shop giữa vòng KHÔNG còn là án tử của
    /// các shop còn lại: đóng trình duyệt sạch rồi mở lại + SSO về picker chỉ tốn ~6 giây và TÁI DÙNG cookie hồ sơ
    /// (nhật ký thật: "Cookie hồ sơ còn hạn — đã về trang chọn shop, BỎ QUA bước đăng nhập Playwright"), rẻ hơn
    /// hẳn cái giá bỏ trắng vài shop rồi chờ trọn một vòng sau.
    /// <para>2 = đủ để tha cho hai cú sập hạ tầng trong một vòng 12 shop, đủ hẹp để trình duyệt hỏng HẲN không kéo
    /// vòng vào chuỗi mở-đóng vô tận (mỗi lượt còn kèm một pha đăng nhập có thể chờ người nhập mã).</para>
    /// </summary>
    internal const int TranMoLaiTrinhDuyet = 2;

    /// <summary>
    /// PURE — vòng còn được phép mở lại trình duyệt nữa không. Tồn tại để NGƯỠNG chỉ nằm ở MỘT chỗ: cả
    /// <see cref="QuyetDinhKhiMatPicker"/> lẫn nhánh chạm-trần trong <c>RunAllShopsAsync</c> đều hỏi qua đây.
    /// Hai chỗ tự so <c>&lt; TranMoLaiTrinhDuyet</c> là hai chỗ có thể lệch nhau mà không test nào thấy.
    /// </summary>
    internal static bool ConLuotMoLai(int soLanDaMoLai) => soLanDaMoLai < TranMoLaiTrinhDuyet;

    /// <summary>
    /// PURE — việc phải làm khi vòng MẤT trang chọn shop sau một shop.
    /// <para>Thứ tự xét: <paramref name="captcha"/> TRƯỚC (đẩy thêm một lượt mở trình duyệt vào đúng lúc Shopee
    /// đang nghi ngờ là tự khai bot — cùng lý lẽ với <see cref="QuyetDinhSauThuBanSach"/>), rồi mới tới "còn shop
    /// chưa chạy không" và trần mở lại.</para>
    /// </summary>
    /// <param name="conShopChuaChay">Còn shop nào chưa được đụng tới trong vòng này không.</param>
    /// <param name="soLanDaMoLai">Số lần ĐÃ mở lại trình duyệt trong vòng này.</param>
    /// <param name="captcha">Phiên đang dính cờ captcha/verify.</param>
    internal static KhiMatPicker QuyetDinhKhiMatPicker(bool conShopChuaChay, int soLanDaMoLai, bool captcha)
    {
        if (captcha) { return KhiMatPicker.DungVong; }
        if (!conShopChuaChay) { return KhiMatPicker.KetThucBinhThuong; }
        if (!ConLuotMoLai(soLanDaMoLai)) { return KhiMatPicker.DungVong; }
        return KhiMatPicker.MoLaiTrinhDuyet;
    }

    /// <summary>
    /// PURE — câu LÝ DO dừng vòng khi không đưa được về trang chọn shop. Hai nguyên nhân phải nói KHÁC NHAU vì
    /// người trực xử khác nhau: <paramref name="viCauNoi"/> = không lượt thử nào gửi đi được (WebSocket/trình duyệt
    /// gãy) — đi soi trình duyệt; ngược lại là extension/trang picker thật sự không về được — đi soi Seller Centre.
    /// <para>Bản trước chỉ có MỘT câu, nên ca cầu nối gãy lúc 16:17:15 ngày 10/08/2026 bị ghi thành "không quay lại
    /// được trang chọn shop" và lượt truy sau đó đi nhầm chỗ.</para>
    /// </summary>
    /// <param name="shopName">Nhãn shop vòng vừa đứng lại.</param>
    /// <param name="shopVuaHong">Shop đó kết thúc bằng LỖI hay không (đổi vế "sau shop X" ↔ "sau shop X bị lỗi").</param>
    /// <param name="viCauNoi">Mọi lượt thử đều trượt vì cầu nối không sống lại.</param>
    internal static string LyDoDungVongKhongVePicker(string shopName, bool shopVuaHong, bool viCauNoi)
    {
        var sauShop = shopVuaHong ? $"sau shop {shopName} bị lỗi" : $"sau shop {shopName}";
        return viCauNoi
            ? $"Cầu nối extension KHÔNG sống lại nên không gửi nổi lệnh đóng tab shop {sauShop} — đã dừng vòng "
              + "(chưa rõ trang chọn shop có hỏng hay không). Sẽ thử lại ở vòng sau."
            : $"Không quay lại được trang chọn shop {sauShop} — đã dừng vòng (shop kế sẽ không mở được). "
              + "Sẽ thử lại ở vòng sau.";
    }

    /// <summary>Nhãn shop cho nhật ký/thông báo: <c>LoginName</c>, không có thì <c>ShopId</c>.</summary>
    private static string NhanShop(ShopListItem s)
        => string.IsNullOrWhiteSpace(s.LoginName) ? s.ShopId : s.LoginName;

    /// <summary>
    /// PURE — Tìm vị trí (0-based index) của shop đã hoàn tất gần nhất trong danh sách shop.
    /// So khớp theo <see cref="ShopListItem.ShopId"/>, <see cref="ShopListItem.LoginName"/>,
    /// <see cref="ShopListItem.ShopName"/> hoặc <see cref="KhoaShop(ShopListItem)"/>.
    /// Trả về -1 nếu không tìm thấy hoặc <paramref name="lastCheckedShop"/> rỗng.
    /// </summary>
    internal static int TimViTriShop(IReadOnlyList<ShopListItem> shops, string? lastCheckedShop)
    {
        if (string.IsNullOrWhiteSpace(lastCheckedShop) || shops == null || shops.Count == 0)
        {
            return -1;
        }

        var target = lastCheckedShop.Trim();
        for (int i = 0; i < shops.Count; i++)
        {
            var s = shops[i];
            if (s == null) continue;
            if (string.Equals(s.ShopId, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.LoginName, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.ShopName, target, StringComparison.OrdinalIgnoreCase)
                || string.Equals(KhoaShop(s), target, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// PURE — ghép danh sách shop VỪA ĐỌC LẠI (sau khi mở lại trình duyệt) với phần chưa chạy của vòng, khớp theo
    /// <see cref="ShopListItem.ShopId"/>.
    /// <para><b>Vì sao không tin chỉ số của danh sách cũ:</b> picker đọc lại có thể trả THỨ TỰ khác, nên lấy theo
    /// vị trí là chạy lại shop đã xong — đếm trùng, in phiếu trùng, đụng đơn THẬT. Khóa an toàn duy nhất là ShopId.</para>
    /// <para>Shop MỚI xuất hiện (chưa từng đụng tới trong vòng này) được nhận vào phần còn lại; shop chưa chạy mà
    /// biến mất khỏi danh sách mới thì bỏ, và trả nhãn ra <see cref="ShopConLaiSauMoLai.BienMat"/> để caller ghi
    /// nhật ký — cấm im lặng nuốt.</para>
    /// </summary>
    /// <param name="danhSachMoi">Danh sách shop đọc lại từ picker sau khi mở lại trình duyệt.</param>
    /// <param name="chuaChayCu">Phần CHƯA chạy theo danh sách CŨ — chỉ dùng để chỉ ra shop nào biến mất.</param>
    /// <param name="daChayShopId">ShopId đã ĐỤNG TỚI trong vòng này (xong / bỏ qua / hỏng) — tuyệt đối không chạy lại.</param>
    internal static ShopConLaiSauMoLai GhepShopConLai(
        IReadOnlyList<ShopListItem> danhSachMoi,
        IReadOnlyList<ShopListItem> chuaChayCu,
        IReadOnlySet<string> daChayShopId)
    {
        var conLai = danhSachMoi.Where(s => !daChayShopId.Contains(s.ShopId)).ToList();
        var coTrongMoi = new HashSet<string>(danhSachMoi.Select(s => s.ShopId), StringComparer.Ordinal);
        var bienMat = chuaChayCu.Where(s => !coTrongMoi.Contains(s.ShopId)).Select(NhanShop).ToList();
        return new ShopConLaiSauMoLai(conLai, bienMat);
    }

    /// <summary>
    /// PURE — phần CÒN LẠI của <b>hàng đợi thử lại</b> sau khi mở lại trình duyệt GIỮA LƯỢT THỬ LẠI.
    /// <para><b>Vì sao KHÔNG dùng <see cref="GhepShopConLai"/> ở đây:</b> hàm kia dựng danh sách bằng "picker mới
    /// TRỪ <c>daChayShopId</c>", mà trong lượt thử lại MỌI shop đều đã nằm trong tập đó (lượt chính đã đụng hết)
    /// ⇒ nó luôn trả RỖNG. Tệ hơn: <c>BienMat</c> cũng rỗng (shop vẫn còn trong picker) nên KHÔNG dòng log nào
    /// kêu lên. Kết quả là phần hàng đợi còn lại bị bỏ IM LẶNG trong khi câu tổng kết vẫn khoe "đã thử lại N
    /// shop" (phản biện 18/08/2026).</para>
    /// <para>Ở lượt thử lại, chạy lại một shop thuộc <c>daChayShopId</c> CHÍNH LÀ mục đích, nên tập đó không
    /// được dùng làm bộ lọc. Lưới chống in phiếu trùng nằm chỗ khác và vẫn nguyên: <see cref="NenThuLaiShopRoiOan"/>
    /// xét <c>DaGuiChuanBiHang</c> TRƯỚC khi xếp hàng, mỗi shop chỉ được xếp một lượt, và <c>laLuotThuLai</c> là
    /// cờ một chiều nên không có lượt thứ ba.</para>
    /// </summary>
    /// <param name="danhSachMoi">Danh sách shop đọc lại từ picker sau khi mở lại trình duyệt.</param>
    /// <param name="chuaChayCu">Phần hàng đợi thử lại CHƯA chạy.</param>
    internal static ShopConLaiSauMoLai GhepShopThuLaiConLai(
        IReadOnlyList<ShopListItem> danhSachMoi,
        IReadOnlyList<ShopListItem> chuaChayCu)
    {
        var coTrongMoi = new HashSet<string>(danhSachMoi.Select(s => s.ShopId), StringComparer.Ordinal);
        var conLai = chuaChayCu.Where(s => coTrongMoi.Contains(s.ShopId)).ToList();
        var bienMat = chuaChayCu.Where(s => !coTrongMoi.Contains(s.ShopId)).Select(NhanShop).ToList();
        return new ShopConLaiSauMoLai(conLai, bienMat);
    }

    /// <summary>
    /// MỘT lượt: mở trình duyệt SẠCH + extension tại <c>/account</c> → chờ extension nối cầu → SSO "Kênh Người bán"
    /// → trang chọn shop. KHÔNG mở thẳng <c>/portal/shop</c> vì Shopee sticky-redirect vào shop mở lần trước
    /// (server-side).
    /// <para>Dùng cho CẢ hai lượt của một vòng: lượt đầu (ăn cookie hồ sơ sẵn có) và lượt sau khi đã đăng nhập lại
    /// bằng Playwright. Mọi đường lỗi gói về <see cref="KetQuaSso.Loi"/> kèm lý do — trừ HỦY (người dùng bấm Dừng)
    /// thì ném tiếp, kẻo nuốt thành "lỗi" rồi đi mở trình duyệt đăng nhập ngay sau khi vừa bấm Dừng.</para>
    /// </summary>
    private async Task<KetQuaSsoChiTiet> SsoVePickerAsync(CancellationToken ct)
    {
        try
        {
            ResetState();
            StartBridgeAndLaunch(ShopeeLoginService.SubaccountAccountUrl);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Không mở được cổng cầu nối / không phóng được trình duyệt → cũng là một đường "Lỗi": lượt đầu sẽ
            // dẫn tới đăng nhập lại, lượt hai trả lỗi cho caller (không treo vòng).
            L("Mở trình duyệt sạch lỗi: " + ex.ToString());
            return new KetQuaSsoChiTiet(KetQuaSso.Loi, "Không mở được trình duyệt sạch: " + ex.Message);
        }

        return await SsoQuaCauNoiAsync(_channel, _log, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Phần SSO CHỈ nói chuyện qua cầu nối (trình duyệt sạch đã mở sẵn ở ngoài): chờ extension báo <c>ready</c> →
    /// gửi <c>gotoSellerCentre</c> → chờ chặng <c>AtSeller</c> → phân loại Ok / Captcha / Lỗi.
    /// <para>Tách khỏi <see cref="SsoVePickerAsync"/> (phần phóng trình duyệt) để test được nguyên luật phân loại
    /// bằng một client WebSocket giả làm extension, KHÔNG cần mở trình duyệt — xem <c>OrdersBridgeSsoTests</c>.</para>
    /// <para><b>Không gọi <c>ResetStages</c> ở đây</b>: chặng <c>Ready</c> phải được thay mới TRƯỚC khi phóng trình
    /// duyệt (việc của caller), kẻo xoá mất tín hiệu <c>ready</c> mà extension đã kịp gửi.</para>
    /// </summary>
    /// <param name="channel">Kênh cầu nối của phiên (cổng đã mở, trình duyệt đã phóng).</param>
    /// <param name="log">Hàm ghi nhật ký của phiên.</param>
    /// <param name="ct">Hủy của phiên (người dùng bấm Dừng).</param>
    /// <param name="hanReady">Hạn chờ extension nối cầu — mặc định <see cref="OrdersBridgeChannel.ChoChang.Ready"/>.
    /// Chỉ test rót hạn ngắn để canh được đường TREO mà không phải chờ thật 45s.</param>
    /// <param name="hanAtSeller">Hạn chờ về trang chọn shop — mặc định
    /// <see cref="OrdersBridgeChannel.ChoChang.AtSeller"/>. Chỉ test rót hạn ngắn.</param>
    internal static async Task<KetQuaSsoChiTiet> SsoQuaCauNoiAsync(
        OrdersBridgeChannel channel, Action<string>? log, CancellationToken ct,
        TimeSpan? hanReady = null, TimeSpan? hanAtSeller = null)
    {
        void L(string m) => log?.Invoke(m);

        // Captcha PHẢI được nhận diện ở MỌI lối ra, kể cả khi chặng vỡ vì lỗi/hết giờ: extension bật cờ qua
        // message "captcha" độc lập với việc chặng nào đang chờ, nên có ca cờ đã bật mà chặng vẫn hỏng.
        KetQuaSsoChiTiet? CaptchaNeuCo()
        {
            if (!channel.CaptchaSeen)
            {
                return null;
            }
            L("PHÁT HIỆN captcha/verify khi vào Seller Centre.");
            return new KetQuaSsoChiTiet(KetQuaSso.Captcha, null);
        }

        // Hai hỏng hóc rất khác nhau cùng ném TimeoutException; cờ này để câu lỗi chỉ ĐÚNG thủ phạm. Không có nó
        // thì ca "bản chép extension hỏng nên service worker chết câm" bị báo là lỗi ở bước SSO — chỉ sai chỗ cho
        // người đọc nhật ký (đã mất một đợt truy vì kiểu này: xem plans/…-orders-bridge-extension-copy-recursive).
        var daNoiCau = false;
        var hanCho = hanReady ?? OrdersBridgeChannel.ChoChang.Ready;
        try
        {
            L($"Chờ extension nối cầu (ready) — tối đa {hanCho.TotalSeconds:0}s...");
            await channel.AwaitAsync(channel.Ready, hanCho, ct).ConfigureAwait(false);
            daNoiCau = true;
            L("Extension đã nối cầu — SSO 'Kênh Người bán' để về trang chọn shop...");

            var atSellerTcs = channel.ArmAtSeller();
            await channel.SendAsync(new { action = "gotoSellerCentre" }).ConfigureAwait(false);
            var atSeller = await channel.AwaitAsync(atSellerTcs, hanAtSeller ?? OrdersBridgeChannel.ChoChang.AtSeller, ct).ConfigureAwait(false);

            if (CaptchaNeuCo() is { } captcha)
            {
                return captcha;
            }
            if (!atSeller)
            {
                return new KetQuaSsoChiTiet(KetQuaSso.Loi,
                    "Không về được trang chọn shop (/portal/shop) sau SSO — có thể sticky shop cũ / cookie hết hạn.");
            }
            return new KetQuaSsoChiTiet(KetQuaSso.Ok, null);
        }
        catch (OperationCanceledException) { throw; } // người dùng Dừng — KHÔNG biến thành lỗi rồi đi đăng nhập lại
        catch (InvalidOperationException ex)
        {
            // Extension báo error (hay gặp nhất: "bản sạch gặp trang đăng nhập subaccount (cookie hết hạn)")
            // hoặc extension chưa/không còn kết nối (SendAsync fail-fast).
            return CaptchaNeuCo() ?? new KetQuaSsoChiTiet(KetQuaSso.Loi, ex.Message);
        }
        catch (TimeoutException)
        {
            // Hết giờ = KHÔNG biết đang ở đâu → Treo (nghỉ vòng), KHÔNG đăng nhập lại. Xem KetQuaSso.Treo.
            return CaptchaNeuCo() ?? new KetQuaSsoChiTiet(KetQuaSso.Treo, daNoiCau
                ? "Hết thời gian chờ phản hồi từ extension khi vào trang chọn shop."
                : $"Extension không nối cầu trong {hanCho.TotalSeconds:0}s (bản chép extension hỏng? trình duyệt chặn?).");
        }
    }

    /// <summary>
    /// Đọc bảng shop ở picker: <c>ArmShopList</c> → <c>readShopList</c> → <c>ParseShopListJson</c> → bắn callback
    /// tab "Shops". BA đường gọi (vòng chạy, mở lại giữa vòng, mở-list-shop) đi CHUNG hàm này — trước 18/08/2026
    /// là ba bản chép tay, mà bản chép thứ tư chính là chỗ ai đó quên <c>ArmShopList</c> rồi ngồi chờ 30s một
    /// chặng không bao giờ tới.
    /// <para><b>KHÔNG gộp dòng log vào đây</b>: ba chỗ gọi đang nói ba câu khác nhau ("Đọc được N shop — bắt đầu
    /// lặp", "đã mở lại và đọc lại được N shop", "đã tới trang chọn shop, DỪNG") và chính câu chữ đó là thứ người
    /// đọc nhật ký dùng để biết vòng đang ở nhánh nào.</para>
    /// <para><c>static</c> + nhận <paramref name="channel"/> theo đúng khuôn <see cref="SsoQuaCauNoiAsync"/>: chỉ
    /// dạng này mới lái được bằng <c>BridgeTestRig</c> (cầu nối thật trên cổng loopback, không cần trình duyệt) —
    /// xem <c>MoListShopTests</c>.</para>
    /// <para><b>KHÔNG dùng cho <see cref="RunSliceCoreAsync"/></b>: bản ở đó cố ý KHÔNG <c>Arm</c> (ngữ nghĩa khác,
    /// chặng đã được <c>ResetState</c> thay mới ngay trước đó).</para>
    /// </summary>
    /// <param name="channel">Kênh cầu nối của phiên (extension đã nối, đang đứng ở picker).</param>
    /// <param name="onShopListRead">Callback đổ danh sách vào lưới tab "Shops" (null = không đổ).</param>
    /// <param name="ct">Hủy của phiên (người dùng bấm Dừng).</param>
    internal static async Task<IReadOnlyList<ShopListItem>> DocDanhSachShopAsync(
        OrdersBridgeChannel channel, Action<IReadOnlyList<ShopListItem>>? onShopListRead, CancellationToken ct)
    {
        var tcs = channel.ArmShopList();
        await channel.SendAsync(new { action = "readShopList" }).ConfigureAwait(false);
        var json = await channel.AwaitAsync(tcs, OrdersBridgeChannel.ChoChang.ShopList, ct).ConfigureAwait(false);
        var shops = ShopeeLoginService.ParseShopListJson(json);
        onShopListRead?.Invoke(shops);
        return shops;
    }

    /// <summary>
    /// Về TRANG CHỌN SHOP (picker /portal/shop) — <b>bản sạch TRƯỚC, Playwright chỉ khi cần</b> (user chốt
    /// 2026-08-07). Thứ tự: mở trình duyệt sạch + extension ăn cookie hồ sơ → về được picker thì XONG (bỏ hẳn lượt
    /// Playwright); hỏng vì lý do khác captcha → đóng bản sạch, đăng nhập bằng trình duyệt điều khiển Playwright
    /// (<see cref="ShopeeLoginService.OpenAsync"/> + <c>TryLoginSubaccountAsync</c>), rồi mở bản sạch LẦN NỮA.
    /// <para><b>Vì sao đảo:</b> bản cũ luôn chạy Playwright trước, nên hồ sơ đã đăng nhập vẫn phải chịu một lượt
    /// mở/đóng trình duyệt (~30–60s mỗi vòng, mỗi tài khoản) và một lần bàn giao hồ sơ — chính là nguồn của lỗi
    /// "Trình duyệt thoát ngay khi khởi động".</para>
    /// Trả <c>null</c> nếu đã về picker HOẶC gặp captcha (caller phân biệt bằng
    /// <see cref="OrdersBridgeChannel.CaptchaSeen"/>); trả CHUỖI LỖI nếu hết đường. Dùng chung cho
    /// <see cref="RunLoginThenSliceAsync"/> (một shop) và <see cref="RunAllShopsAsync"/> (mọi shop). Trình duyệt
    /// điều khiển login LUÔN được đóng (finally).
    /// </summary>
    private async Task<string?> LoginAndReachPickerAsync(OrdersLoginParams login, CancellationToken ct)
    {
        // 1) THỬ BẢN SẠCH TRƯỚC — hồ sơ còn cookie thì vào thẳng picker, khỏi đụng Playwright.
        L("Mở trình duyệt sạch + extension (dùng cookie hồ sơ nếu còn hạn) — /account → SSO → trang chọn shop...");
        var lanDau = await SsoVePickerAsync(ct).ConfigureAwait(false);
        var hanhDong = QuyetDinhSauThuBanSach(lanDau.Ket, daFallback: false);
        if (hanhDong == HanhDongSauThuSach.ChayTiep)
        {
            L("Cookie hồ sơ còn hạn — đã về trang chọn shop, BỎ QUA bước đăng nhập Playwright.");
            return null;
        }
        if (hanhDong == HanhDongSauThuSach.DungVongCaptcha)
        {
            return null; // caller kiểm CaptchaSeen
        }

        // 2) Bản sạch không vào được → ĐÓNG hẳn nó rồi mới đăng nhập bằng trình duyệt điều khiển. Phải dùng
        //    DongTrinhDuyetSach (kill handle RỒI quét theo hồ sơ): Brave fork tiến trình browser thật sang PID
        //    khác nên kill-theo-handle một mình trượt, để lại cửa sổ mồ côi GIỮ HỒ SƠ → Playwright chết ngay ở
        //    bước mở ("Trình duyệt thoát ngay khi khởi động").
        L($"Bản sạch chưa vào được trang chọn shop ({lanDau.LyDo ?? "không rõ lý do"}) — đóng bản sạch, đăng nhập lại bằng Playwright.");
        DongTrinhDuyetSach();
        // Quên dòng này thì suốt pha đăng nhập (có thể vài phút — chờ người nhập mã) Process vẫn trỏ tiến trình
        // ĐÃ CHẾT, tức "bản sạch đang sống" là tín hiệu SAI với mọi chỗ đọc nó (nút Dừng, WindowFocus).
        // KHÔNG Dispose: UI thread có thể đang đọc cùng lúc, để GC lo còn hơn ném ObjectDisposedException chỗ khác.
        Process = null;
        await Task.Delay(800, ct).ConfigureAwait(false); // settle cho chắc nhả khoá file hồ sơ

        // 3) Đăng nhập bằng trình duyệt điều khiển (Playwright). try/finally: user Dừng giữa chừng vẫn đóng trình duyệt.
        var entered = false;
        ILoginSession? session = null;
        try
        {
            L("Đăng nhập Nền tảng tài khoản phụ bằng trình duyệt điều khiển (Playwright)...");
            var svc = new ShopeeLoginService();
            session = await svc.OpenAsync(_userDataDir, ct, _log).ConfigureAwait(false);
            entered = await session.TryLoginSubaccountAsync(
                login.User, login.Pass, login.VerifyEmail, login.VerifyEmailPassword, _log, ct).ConfigureAwait(false);
        }
        finally
        {
            if (session is not null)
            {
                try { await session.DisposeAsync().ConfigureAwait(false); } catch { /* bỏ qua */ }
            }
        }

        if (!entered)
        {
            return "Đăng nhập subaccount chưa xong (nhập mã?). Bấm lại để thử tiếp.";
        }

        // 4) Settle ngắn cho chắc nhả khoá file hồ sơ (Brave vừa kill) trước khi mở lại bằng trình duyệt sạch.
        await Task.Delay(800, ct).ConfigureAwait(false);

        L("Đăng nhập xong — mở lại bằng trình duyệt sạch + extension (subaccount /account → SSO → trang chọn shop)...");
        var lanHai = await SsoVePickerAsync(ct).ConfigureAwait(false);
        // Liệt kê TƯỜNG MINH từng nhánh: gom BaoLoi với DangNhapLai vào `_` thì nếu ai sửa bảng quyết định cho
        // phép đăng nhập lại lần nữa, lượt đó sẽ IM LẶNG biến thành lỗi thay vì gãy ra để người sửa thấy.
        return QuyetDinhSauThuBanSach(lanHai.Ket, daFallback: true) switch
        {
            HanhDongSauThuSach.ChayTiep => null,          // đã về picker
            HanhDongSauThuSach.DungVongCaptcha => null,   // caller kiểm CaptchaSeen
            HanhDongSauThuSach.BaoLoi => lanHai.LyDo ?? "Không về được trang chọn shop sau khi đăng nhập lại.",
            var khac => throw new InvalidOperationException(
                $"Bảng quyết định trả {khac} ở lượt ĐÃ đăng nhập lại — không có đường lui nào nữa, phải sửa QuyetDinhSauThuBanSach."),
        };
    }

    /// <summary>Nghỉ ngắn cho hệ điều hành NHẢ KHOÁ file hồ sơ sau khi kill trình duyệt sạch, trước khi mở lại
    /// (Brave/Chrome giữ khoá thêm một nhịp; mở đè là "Trình duyệt thoát ngay khi khởi động").</summary>
    private static readonly TimeSpan SettleNhaKhoaHoSo = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// MỞ LẠI trình duyệt sạch GIỮA một vòng shop rồi đọc lại danh sách shop ở picker — đường cứu khi vòng mất
    /// trang chọn shop nhưng CÒN shop chưa chạy. Đóng bản sạch đang hỏng (nhả khoá hồ sơ) → đi lại
    /// <see cref="LoginAndReachPickerAsync"/> (thường chỉ ~6 giây vì cookie hồ sơ còn hạn, không đụng Playwright)
    /// → <c>readShopList</c>.
    /// <para>Trả danh sách shop đọc lại được; <c>null</c> = không về được picker — caller DỪNG vòng, phân biệt
    /// captcha bằng <see cref="OrdersBridgeChannel.CaptchaSeen"/>.</para>
    /// <para>Ngoại lệ (hết giờ / cầu nối rớt) để NÉM tiếp: nó nằm ngoài thân một shop nên phải rơi vào khối catch
    /// cuối <see cref="RunAllShopsAsync"/>, chỗ đã mang sẵn mọi tín hiệu địa chỉ tích lũy của vòng.</para>
    /// </summary>
    private async Task<IReadOnlyList<ShopListItem>?> MoLaiTrinhDuyetVePickerAsync(
        OrdersLoginParams login, CancellationToken ct)
    {
        DongTrinhDuyetSach();
        // Quên dòng này thì "bản sạch đang sống" là tín hiệu SAI với mọi chỗ đọc Process (nút Dừng, WindowFocus).
        Process = null;
        await Task.Delay(SettleNhaKhoaHoSo, ct).ConfigureAwait(false);

        var err = await LoginAndReachPickerAsync(login, ct).ConfigureAwait(false);
        if (_channel.CaptchaSeen)
        {
            L("Mở lại trình duyệt nhưng rơi vào trang verify/captcha — KHÔNG thử tiếp, dừng vòng.");
            return null;
        }
        if (err is not null)
        {
            L("Mở lại trình duyệt nhưng vẫn không về được trang chọn shop: " + err);
            return null;
        }

        // Callback tab "Shops" bắn lại ở đây là CỐ Ý: danh sách shop có thể đã đổi giữa hai lượt đọc.
        var shops = await DocDanhSachShopAsync(_channel, _onShopListRead, ct).ConfigureAwait(false);
        L($"Đã mở lại trình duyệt và về trang chọn shop — đọc lại được {shops.Count} shop.");
        return shops;
    }

    /// <summary>Nghỉ "kiểu người" một khoảng ngẫu nhiên trong
    /// [<see cref="NghiGiuaShopToiThieu"/>, <see cref="NghiGiuaShopToiDa"/>] trước shop kế.</summary>
    private async Task NghiGiuaShopAsync(CancellationToken ct)
    {
        var restMs = _rng.Next((int)NghiGiuaShopToiThieu.TotalMilliseconds, (int)NghiGiuaShopToiDa.TotalMilliseconds + 1);
        L($"Nghỉ ~{restMs / 60000}' trước shop kế...");
        await Task.Delay(restMs, ct).ConfigureAwait(false);
    }

    // ── GĐ4: MỘT VÒNG qua MỌI shop: login → SSO → readShopList → từng shop (detail → toShip → syncOrders +
    //    callback lưu DB → nếu ToShip>0 xử đơn + revert địa chỉ) → đóng tab shop → nghỉ → shop kế. ─────────────
    /// <summary>
    /// GĐ4: đăng nhập + SSO về picker rồi LẶP QUA MỌI shop trong danh sách. Mỗi shop: mở Chi tiết → đọc "Chờ Lấy
    /// Hàng" → đọc đơn (tab Tất cả) → GỌI <c>syncCallback</c> (App lưu DB/GSheet/hub) → nếu ToShip&gt;0 thì đặt địa
    /// chỉ lấy hàng + Chuẩn bị hàng từng đơn + in phiếu + revert địa chỉ → đóng tab shop (về picker) → nghỉ 3-5' →
    /// shop kế. Captcha giữa chừng → dừng vòng (Captcha=true). Dùng cho nút "▶ Chạy" (production, chạy liên tục).
    /// </summary>
    public async Task<OrdersBridgeRunResult> RunAllShopsAsync(OrdersLoginParams login, CancellationToken ct = default)
    {
        int shopCount = 0, shopsDone = 0, totalOrders = 0, totalSlips = 0;
        var pickupFailedShops = new List<string>();
        var pickupOkShops = new List<string>();

        // Số shop hỏng LIÊN TIẾP (ngoại lệ từ thân shop) — chạm TranShopHongLienTiep thì dừng vòng; shop nào
        // chạy trọn là đứt chuỗi. Xem QuyetDinhSauShopHong.
        var hongLienTiep = 0;

        // Nhãn các shop ĐẶT ĐƯỢC địa chỉ trong vòng này (null khi chưa shop nào) — phải điền vào MỌI lối ra
        // NẰM TRONG/SAU vòng shop, kẻo vòng thoát giữa chừng là mất tín hiệu gỡ banner của shop đã chạy xong.
        string? NhanShopDatDuoc() => pickupOkShops.Count > 0 ? string.Join(", ", pickupOkShops) : null;

        // Gom tín hiệu địa chỉ của shop VỪA chạy vào danh sách của vòng rồi DỌN cờ ngay: gọi ở cả lối thường
        // lẫn lối shop-hỏng nên không dọn là một shop vào danh sách hai lần (banner ghi "shop, shop").
        void ThuTinHieuDiaChi()
        {
            if (_flow.PickupOkShop is not null) { pickupOkShops.Add(_flow.PickupOkShop); _flow.PickupOkShop = null; }
            if (_flow.PickupFailedShop is not null) { pickupFailedShops.Add(_flow.PickupFailedShop); _flow.PickupFailedShop = null; }
        }

        // Đóng tab shop để về trang chọn shop SAU KHI shop hỏng: chính lượt đóng cũng có thể ném (cầu nối chết
        // hẳn) — nuốt tại đây, kẻo đường dọn dẹp lại giết cả vòng đúng như cái bug đang sửa.
        // Ngoại lệ lạ ⇒ PickerHong: ta KHÔNG chứng minh được là lỗi cầu nối (mọi đường cầu nối đã được
        // DongTabShopAsync nuốt sẵn), mà đoán bừa "cầu nối" là đúng cái tội báo sai nguyên nhân đang đi sửa.
        async Task<KetQuaVePicker> ThuDongTabShopAsync(CancellationToken ctDong)
        {
            try { return await _flow.DongTabShopAsync(ctDong).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                L("Đóng tab shop sau lỗi cũng hỏng: " + ex.Message);
                return KetQuaVePicker.PickerHong;
            }
        }

        // MỌI lối ra LỖI của vòng đi qua đây: tín hiệu địa chỉ tích lũy TỪ ĐẦU VÒNG phải theo ra bằng hết, kể cả
        // phần trước một lần mở lại trình duyệt. Mất PickupOkShops là banner địa chỉ của shop đã chạy xong treo
        // oan; mất PickupFailedShop là shop hỏng địa chỉ không ai được báo.
        OrdersBridgeRunResult KetQuaDungVong(string? loi, bool captcha = false)
            => new(shopCount, shopsDone, totalOrders, totalSlips, captcha, loi,
                PickupAddressFailed: pickupFailedShops.Count > 0,
                PickupFailedShop: pickupFailedShops.Count > 0 ? string.Join(", ", pickupFailedShops) : null,
                PickupOkShops: NhanShopDatDuoc());

        try
        {
            var err = await LoginAndReachPickerAsync(login, ct).ConfigureAwait(false);
            if (_channel.CaptchaSeen)
            {
                return new OrdersBridgeRunResult(0, 0, 0, 0, true, "Rơi vào trang verify/captcha khi vào Seller Centre.");
            }
            if (err is not null)
            {
                return new OrdersBridgeRunResult(0, 0, 0, 0, false, err);
            }

            // Đọc danh sách shop (picker) — callback tab "Shops" nằm trong helper: App lưu MỌI shop, kể cả 0 đơn.
            var shops = await DocDanhSachShopAsync(_channel, _onShopListRead, ct).ConfigureAwait(false);
            shopCount = shops.Count;
            L($"Đọc được {shops.Count} shop — bắt đầu lặp qua từng shop.");
            if (shops.Count == 0)
            {
                return new OrdersBridgeRunResult(0, 0, 0, 0, false, "Không đọc được shop nào (đã tới /portal/shop chưa?).");
            }

            // Phần CHƯA chạy của vòng. Đọc lại picker sau mỗi lần mở lại trình duyệt sẽ THAY danh sách này (ghép
            // theo ShopId qua GhepShopConLai), nên vòng for phải bám conLai chứ không bám `shops`.
            IReadOnlyList<ShopListItem> conLai = shops;
            // ShopId đã ĐỤNG TỚI trong vòng (xong / bỏ qua vì địa chỉ / hỏng giữa chừng). Chạy lại một shop trong
            // tập này là đếm trùng + in phiếu trùng + đụng đơn THẬT — ràng buộc quan trọng nhất của đường mở lại.
            var daChayShopId = new HashSet<string>(StringComparer.Ordinal);
            var soLanMoLai = 0;

            var startIdx = 0;
            var lastShop = _layShopDaCheckCuoi?.Invoke();
            if (!string.IsNullOrWhiteSpace(lastShop))
            {
                var idx = TimViTriShop(shops, lastShop);
                if (idx >= 0)
                {
                    if (idx < shops.Count - 1)
                    {
                        startIdx = idx + 1;
                        L($"Tiếp tục từ shop {startIdx + 1}/{shops.Count} ({NhanShop(shops[startIdx])}) — trước đó đã xong shop {NhanShop(shops[idx])}.");
                    }
                    else
                    {
                        L($"Vòng trước đã xong shop cuối ({NhanShop(shops[idx])}) — bắt đầu vòng mới từ shop 1.");
                        _luuShopDaCheckCuoi?.Invoke(null);
                    }
                }
                else
                {
                    L($"Không tìm thấy shop trước đó ({lastShop}) trong danh sách — bắt đầu từ shop 1.");
                    _luuShopDaCheckCuoi?.Invoke(null);
                }
            }

            if (startIdx > 0)
            {
                for (int s = 0; s < startIdx; s++)
                {
                    daChayShopId.Add(shops[s].ShopId);
                }
            }

            // HÀNG ĐỢI THỬ LẠI: shop rơi OAN vì cầu nối (chưa hề gửi prepareNextOrder) — chạy nốt SAU KHI hết
            // danh sách chính, không chen ngang, mỗi shop đúng 1 lượt. Xem NenThuLaiShopRoiOan.
            var hangDoiThuLai = new List<ShopListItem>();
            var laLuotThuLai = false;
            // Số shop THẬT SỰ được chạy lại — KHÁC hangDoiThuLai.Count khi lượt thử lại bị cắt giữa chừng. Câu
            // tổng kết cuối vòng phải nói số THẬT, kẻo báo "đã thử lại 3 shop" trong khi mới thử được 1.
            var soShopDaThuLai = 0;

            // "Còn việc phía sau trong vòng này": còn shop chưa chạy của lượt hiện tại, HOẶC hàng đợi thử lại sắp
            // chạy sau lượt chính. Dùng cho CẢ hai câu hỏi "có cần dọn trang chọn shop không" và "picker hỏng có
            // hại ai không" — bỏ vế hàng đợi là lượt thử lại khởi động trên một picker bẩn, chết ngay từ shop đầu.
            bool ConViecPhiaSau(int i, IReadOnlyList<ShopListItem> ds)
                => i < ds.Count - 1 || (!laLuotThuLai && hangDoiThuLai.Count > 0);

            // Vòng NGOÀI chỉ có tối đa hai lượt: danh sách chính, rồi hàng đợi thử lại (nếu có). `laLuotThuLai`
            // chốt cửa — lượt thử lại KHÔNG được đẻ ra hàng đợi thử lại nữa.
            while (true)
            {
                var batDau = startIdx;
                startIdx = 0;
                for (int i = batDau; i < conLai.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var shop = conLai[i];
                    var shopName = NhanShop(shop);
                    // Nhãn shop cho cột Tên Shop (GSheet) + khóa đếm prepare_daily: LoginName, fallback ShopName
                    // (KHÁC shopName ở trên fallback ShopId). Tính SỚM để cột tiến độ báo được ngay lúc bắt đầu.
                    var shopLogin = string.IsNullOrWhiteSpace(shop.LoginName) ? shop.ShopName : shop.LoginName;
                    L($"[{(laLuotThuLai ? "Thử lại " : "")}Shop {i + 1}/{conLai.Count}] {shopName} — mở Chi tiết...");

                    // Đánh dấu NGAY, TRƯỚC khi làm gì: mọi lối ra của shop này (xong, bỏ qua, hỏng, captcha) đều phải
                    // tính là "đã đụng". Đánh dấu ở cuối thì lối hỏng sẽ trượt và shop được chạy lại sau lần mở lại.
                    daChayShopId.Add(shop.ShopId);
                    if (laLuotThuLai) { soShopDaThuLai++; }

                    // Cờ "đã gửi chuẩn bị hàng" phải sạch TỪ TRƯỚC lệnh đầu tiên của shop: shop có thể hỏng ngay ở
                    // openShopDetail, và lúc đó khối catch đọc cờ để quyết định có thử lại không. Thừa hưởng cờ của
                    // shop trước là hoặc bỏ oan một shop, hoặc (tệ hơn nhiều) chạy lại một shop ĐÃ in phiếu.
                    // ShopFlowRunner cũng tự reset ở đầu RunShopOrdersAsync — hai lớp cho ràng buộc sống còn này.
                    _flow.DaGuiChuanBiHang = false;

                    // != null ⇒ shop này đã xử xong nhưng picker CHƯA sạch; xử lý NGOÀI khối try (mở lại trình duyệt
                    // là việc của cả vòng, không được rơi vào catch dành cho thân một shop).
                    KetQuaVePicker? matPicker = null;
                    var shopVuaHong = false;
                    // Thân shop đã chạy trọn, chỉ còn dọn dẹp (đóng tab về picker). Ngoại lệ SAU mốc này không
                    // được biến shop thành ứng viên chạy lại — chạy lại là cộng đôi shopsDone + đọc lại đơn vô ích.
                    var daXongViecShop = false;

                    // Cột tiến độ tab "Shops": bắt đầu check shop này → chấm nhảy sang đây + bật vòng quay.
                    _onShopCheckStarted?.Invoke(shopLogin);
                    try
                    {
                        // Mở Chi tiết shop (trusted click).
                        var detailTcs = _channel.ArmDetail();
                        await _channel.SendAsync(new { action = "openShopDetail", shopId = shop.ShopId }).ConfigureAwait(false);
                        var d = await _channel.AwaitAsync(detailTcs, OrdersBridgeChannel.ChoChang.Detail, ct).ConfigureAwait(false);
                        if (_channel.CaptchaSeen || d == "captcha")
                        {
                            return KetQuaDungVong("Rơi vào captcha khi mở Chi tiết.", captcha: true);
                        }

                        // Đọc "Chờ Lấy Hàng".
                        var toShipTcs = _channel.ArmToShip();
                        await _channel.SendAsync(new { action = "readToShip" }).ConfigureAwait(false);
                        var raw = await _channel.AwaitAsync(toShipTcs, OrdersBridgeChannel.ChoChang.ToShip, ct).ConfigureAwait(false);
                        var toShip = ShopeeDashboard.ParseToShipCount(raw);
                        L($"[Shop {i + 1}] Chờ Lấy Hàng: {(toShip?.ToString() ?? "?")}.");

                        // Đọc đơn (Phần A) + callback lưu DB + xử đơn (Phần B) + revert địa chỉ.
                        // Reset cờ "đặt được địa chỉ" NGAY TRƯỚC lời gọi: quên là shop sau thừa hưởng cờ của shop
                        // trước ⇒ GỠ NHẦM banner của shop chưa hề chạy bước địa chỉ (vd shop 0 đơn chờ lấy hàng).
                        _flow.PickupOkShop = null;
                        var (orders, slips) = await _flow.RunShopOrdersAsync(shop.ShopId, shopLogin, toShip ?? 0, ct).ConfigureAwait(false);
                        if (_channel.CaptchaSeen)
                        {
                            // Captcha giữa chừng vẫn phải mang theo tín hiệu của shop đã đặt được địa chỉ trước đó.
                            if (_flow.PickupOkShop is not null) { pickupOkShops.Add(_flow.PickupOkShop); }
                            // Không dùng KetQuaDungVong ở đây: hai con số đơn/phiếu của CHÍNH shop này chưa kịp cộng
                            // vào tổng. Phần tín hiệu địa chỉ thì giữ y hệt — tích lũy từ đầu vòng.
                            return new OrdersBridgeRunResult(shopCount, shopsDone, totalOrders + orders, totalSlips + slips,
                                true, "Rơi vào captcha khi đọc/xử đơn.",
                                PickupAddressFailed: pickupFailedShops.Count > 0,
                                PickupFailedShop: pickupFailedShops.Count > 0 ? string.Join(", ", pickupFailedShops) : null,
                                PickupOkShops: NhanShopDatDuoc());
                        }
                        if (_flow.PickupFailedShop is not null)
                        {
                            // Không đặt được địa chỉ → BỎ QUA shop này (không in phiếu), vẫn chạy shop kế.
                            // Lỗi địa chỉ thường chỉ của một shop; giả thuyết cũ "modal hỏng = mọi shop hỏng" đã bỏ (2026-08-04).
                            var failed = _flow.PickupFailedShop;
                            pickupFailedShops.Add(failed);
                            L($"⛔ Không đặt được địa chỉ lấy hàng ở shop {failed} — BỎ QUA shop này, KHÔNG in phiếu; sang shop kế.");
                            totalOrders += orders; // Phần A có thể đã sync đơn; slips = 0
                            _flow.PickupFailedShop = null; // tránh shop sau dính cờ cũ
                        }
                        else
                        {
                            totalOrders += orders;
                            totalSlips += slips;
                            shopsDone++;
                            // Shop này ĐẶT ĐƯỢC địa chỉ (bước đó thực sự chạy) → vòng ngoài tự gỡ banner cũ của nó.
                            ThuTinHieuDiaChi();
                            if (laLuotThuLai)
                            {
                                L($"↻ Thử lại shop {shopName}: XONG (tính vào số shop đã chạy của vòng).");
                            }
                        }

                        // Từ đây chỉ còn dọn dẹp — xem daXongViecShop.
                        daXongViecShop = true;

                        // Đóng tab shop → về picker /portal/shop (listTabId picker giữ nguyên; extension đóng shopTabId).
                        // ⚠ PHẢI đọc cờ ok: bản trước vứt giá trị trả về, nên hễ picker không sẵn sàng là shop KẾ chết
                        // với thông báo lạc đề "chờ 30s chưa thấy tab shop mở" (3/3 lần trong nhật ký production, luôn
                        // đi ngay sau một lượt trang trả hàng không render).
                        // Cũng bắt buộc sau nhánh bỏ-qua-địa-chỉ — quên thì shop kế chết.
                        var vePicker = await _flow.DongTabShopAsync(ct).ConfigureAwait(false);
                        if (vePicker != KetQuaVePicker.VeDuoc)
                        {
                            // Quyết định (mở lại trình duyệt / dừng / kệ vì đã hết shop) nằm NGOÀI khối try.
                            matPicker = vePicker;
                        }

                        hongLienTiep = 0; // shop này chạy trọn → đứt chuỗi hỏng liên tiếp
                        // Shop này đã hoàn tất trọn vẹn (không ném ngoại lệ) → lưu mốc để nếu app đóng/dừng thì chạy tiếp từ shop sau.
                        _luuShopDaCheckCuoi?.Invoke(shopLogin);
                    }
                    catch (OperationCanceledException)
                    {
                        // Người dùng bấm Dừng — KHÔNG phải shop hỏng. Ném ra như cũ để cả vòng kết thúc ngay.
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // MỘT SHOP HỎNG KHÔNG ĐƯỢC GIẾT CẢ VÒNG. Trước 10/08/2026 ngoại lệ từ thân shop xuyên thẳng
                        // ra khối catch cuối hàm ⇒ mọi shop phía sau mất trắng (13:37: cầu nối rớt giữa chặng Chuẩn
                        // bị hàng ở shop 6/12, 6 shop cuối không hề được mở).
                        hongLienTiep++;
                        shopVuaHong = true;
                        L($"⛔ Shop {shopName} HỎNG giữa chừng ({ex.Message}) — bỏ shop này, "
                          + $"hỏng liên tiếp {hongLienTiep}/{TranShopHongLienTiep}.");
                        L("Chi tiết lỗi shop: " + ex.ToString());

                        // Tín hiệu địa chỉ của shop vừa hỏng vẫn phải theo vòng ra ngoài: bước đặt địa chỉ có thể ĐÃ
                        // chạy xong (chỉ bước sau mới gãy), mà mất tín hiệu là banner của nó treo lại oan.
                        ThuTinHieuDiaChi();

                        if (laLuotThuLai)
                        {
                            L($"↻ Thử lại shop {shopName}: VẪN hỏng — thôi, để vòng sau.");
                        }
                        else if (NenThuLaiShopRoiOan(
                                     _flow.DaGuiChuanBiHang, LaLoiCauNoi(ex), daXongViecShop,
                                     hangDoiThuLai.Any(s => string.Equals(s.ShopId, shop.ShopId, StringComparison.Ordinal)),
                                     hangDoiThuLai.Count))
                        {
                            hangDoiThuLai.Add(shop);
                            L($"↻ Shop {shopName} rơi vì CẦU NỐI và chưa hề gửi lệnh chuẩn bị hàng — xếp vào hàng đợi "
                              + $"thử lại cuối vòng ({hangDoiThuLai.Count}/{TranThuLaiShopMoiVong}).");
                        }

                        // "Còn việc phía sau" = còn shop chưa chạy của lượt này, HOẶC hàng đợi thử lại (đã gồm cả
                        // shop vừa xếp vào ngay trên) sắp chạy. Cả hai đường đều cần trang chọn shop SẠCH.
                        var conShopPhiaSau = ConViecPhiaSau(i, conLai);
                        // Chỉ đóng tab khi việc đó còn ý nghĩa: shop CUỐI thì vòng xong rồi, còn chạm trần thì vòng
                        // dừng ngay — mà cầu nối đã chết thì mỗi lệnh gửi đi còn ngốn trọn hạn chờ nối lại.
                        var canVePicker = conShopPhiaSau && hongLienTiep < TranShopHongLienTiep;
                        var vePicker = canVePicker
                            ? await ThuDongTabShopAsync(ct).ConfigureAwait(false)
                            : KetQuaVePicker.VeDuoc;

                        // Cờ captcha do LUỒNG CẦU NỐI ghi — đọc đúng MỘT lần rồi dùng cho cả quyết định lẫn kết
                        // quả trả về. Đọc hai lần là hai câu trả lời khác nhau cho cùng một tình huống.
                        var captchaLuc = _channel.CaptchaSeen;
                        var quyetDinh = QuyetDinhSauShopHong(
                            hongLienTiep, conShopPhiaSau, vePicker == KetQuaVePicker.VeDuoc,
                            conLuotMoLai: ConLuotMoLai(soLanMoLai), captcha: captchaLuc);
                        if (quyetDinh == SauShopHong.MoLaiTrinhDuyetHongLienTiep)
                        {
                            // Chạm trần mà CÒN đường cứu ⇒ mở lại trình duyệt (nhật ký 6/6 nói mở lại là thứ chữa
                            // khỏi, giá 5 giây — xem SauShopHong.MoLaiTrinhDuyetHongLienTiep). KHÔNG chép lại code
                            // mở lại: gán matPicker rồi để đúng khối mở lại phía dưới (ngoài try) tự chạy. Chọn
                            // CauNoiChet vì ta còn KHÔNG thử đóng tab shop (canVePicker đã tắt khi chạm trần) —
                            // tức chưa có bằng chứng nào nói trang picker hỏng, y hệt nghĩa của nhãn đó.
                            // Nói ĐÚNG cái đang cứu: chạm trần ở shop CUỐI thì phần cứu được là HÀNG ĐỢI THỬ
                            // LẠI, không phải "shop chưa chạy" — khi đó vế kia bằng 0 và câu log tự mâu thuẫn
                            // (phản biện 18/08/2026).
                            var soChuaChay = conLai.Count - i - 1;
                            var moTaCuu = soChuaChay > 0
                                ? $"{soChuaChay} shop chưa chạy"
                                : $"{hangDoiThuLai.Count} shop trong hàng đợi thử lại";
                            L($"⛔ {hongLienTiep} shop LIÊN TIẾP hỏng (shop cuối: {shopName} — {ex.Message}) — hạ tầng "
                              + $"cầu nối coi như chết; MỞ LẠI trình duyệt để cứu {moTaCuu} của CHÍNH vòng này.");
                            matPicker = KetQuaVePicker.CauNoiChet;
                        }
                        else if (quyetDinh == SauShopHong.DungVongHongLienTiep)
                        {
                            // Chạm trần và hết đường cứu: hết lượt mở lại, hoặc đang dính captcha (đẩy thêm một lượt
                            // mở trình duyệt lúc Shopee đang nghi là bot chính là tự khai), hoặc không còn việc phía sau.
                            var lyDo = $"{hongLienTiep} shop LIÊN TIẾP hỏng (shop cuối: {shopName} — {ex.Message}) "
                                + "— coi như cầu nối đã chết, dừng vòng.";
                            // Đếm CẢ hàng đợi thử lại: dừng vòng ở lượt chính là bỏ luôn phần đó, nói thiếu là
                            // người trực tưởng chỉ mất mấy shop cuối.
                            var soBo = conLai.Count - i - 1 + (laLuotThuLai ? 0 : hangDoiThuLai.Count);
                            L($"⛔ Dừng cả vòng của tài khoản (bỏ {soBo} shop còn lại) — {lyDo}");
                            return KetQuaDungVong(lyDo, captchaLuc);
                        }
                        if (quyetDinh == SauShopHong.DungVongKhongVePicker)
                        {
                            matPicker = vePicker; // xử lý NGOÀI khối try (có thể còn cứu được bằng mở lại trình duyệt)
                        }
                    }
                    finally
                    {
                        // XONG shop này — kể cả lỗi/captcha/hủy giữa chừng (đừng để vòng quay quay mãi). Tắt vòng quay,
                        // chấm VẪN ở lại shop này cho tới khi shop kế gọi _onShopCheckStarted. Đặt TRƯỚC nhịp nghỉ 3-5'.
                        _onShopCheckFinished?.Invoke(shopLogin);
                    }

                    if (matPicker is { } lyDoMatPicker)
                    {
                        var conShopChuaChay = ConViecPhiaSau(i, conLai);
                        var quyet = QuyetDinhKhiMatPicker(conShopChuaChay, soLanMoLai, _channel.CaptchaSeen);

                        if (quyet == KhiMatPicker.DungVong)
                        {
                            var lyDo = _channel.CaptchaSeen
                                ? $"Rơi vào trang verify/captcha khi đóng tab shop {shopName}."
                                : LyDoDungVongKhongVePicker(
                                    shopName, shopVuaHong, lyDoMatPicker == KetQuaVePicker.CauNoiChet);
                            L($"⛔ Dừng cả vòng của tài khoản (bỏ {conLai.Count - i - 1} shop còn lại) — {lyDo}");
                            return KetQuaDungVong(lyDo, _channel.CaptchaSeen);
                        }

                        if (quyet == KhiMatPicker.MoLaiTrinhDuyet)
                        {
                            soLanMoLai++;
                            var chuaChayCu = conLai.Skip(i + 1).ToList();
                            L($"Mất trang chọn shop ({(lyDoMatPicker == KetQuaVePicker.CauNoiChet ? "cầu nối không sống lại" : "picker không về được trạng thái sạch")}) "
                              + $"— MỞ LẠI trình duyệt lần {soLanMoLai}/{TranMoLaiTrinhDuyet}, còn {chuaChayCu.Count} shop chưa chạy.");

                            var shopsMoi = await MoLaiTrinhDuyetVePickerAsync(login, ct).ConfigureAwait(false);
                            if (shopsMoi is null)
                            {
                                var lyDo = _channel.CaptchaSeen
                                    ? "Rơi vào trang verify/captcha khi mở lại trình duyệt giữa vòng."
                                    : $"Mở lại trình duyệt (lần {soLanMoLai}/{TranMoLaiTrinhDuyet}) vẫn không về được "
                                      + $"trang chọn shop sau shop {shopName} — dừng vòng, thử lại ở vòng sau.";
                                L($"⛔ Dừng cả vòng của tài khoản (bỏ {chuaChayCu.Count} shop còn lại) — {lyDo}");
                                return KetQuaDungVong(lyDo, _channel.CaptchaSeen);
                            }

                            // Lượt THỬ LẠI đi đường RIÊNG — xem GhepShopThuLaiConLai. Dùng nhầm GhepShopConLai
                            // ở đây là bỏ IM LẶNG toàn bộ phần hàng đợi còn lại.
                            var ghep = laLuotThuLai
                                ? GhepShopThuLaiConLai(shopsMoi, chuaChayCu)
                                : GhepShopConLai(shopsMoi, chuaChayCu, daChayShopId);
                            if (ghep.BienMat.Count > 0)
                            {
                                L($"Sau khi mở lại: {ghep.BienMat.Count} shop chưa chạy KHÔNG còn trong danh sách mới "
                                  + $"({string.Join(", ", ghep.BienMat)}) — bỏ qua, để vòng sau.");
                            }
                            conLai = ghep.ConLai;
                            // Mở lại trình duyệt thành công = hạ tầng đã lành → chuỗi hỏng liên tiếp coi như đứt. Giữ
                            // số cũ là để một chuỗi xui TRƯỚC lúc mở lại giết oan vòng vừa được dựng lại sạch sẽ.
                            hongLienTiep = 0;
                            L($"Chạy tiếp {conLai.Count} shop chưa chạy trong CHÍNH vòng này.");
                            i = -1;   // for++ đưa về 0 — duyệt danh sách còn lại từ đầu
                            continue; // bỏ luôn nhịp nghỉ: vừa mất mấy chục giây mở lại rồi
                        }

                        // KetThucBinhThuong: hết shop chưa chạy — picker hỏng không hại ai, vòng sau mở cửa sổ mới.
                        L($"closeShopTab không về được picker sau shop CUỐI ({shopName}) — vòng đã xong, bỏ qua.");
                    }

                    // Nghỉ kiểu người 3-5' giữa các shop (trừ shop cuối của lượt).
                    if (i < conLai.Count - 1)
                    {
                        await NghiGiuaShopAsync(ct).ConfigureAwait(false);
                    }
                }

                // Hết danh sách chính → chạy nốt hàng đợi thử lại (ĐƯỜNG RIÊNG, sau khi đã đi hết 12 shop: lúc này
                // nhịp đứt ~240s của network service trình duyệt đã dịch đi, cơ may khác hẳn lúc shop rơi).
                if (laLuotThuLai || hangDoiThuLai.Count == 0)
                {
                    break;
                }
                laLuotThuLai = true;
                conLai = hangDoiThuLai.ToList(); // BẢN SAO: `hangDoiThuLai` còn dùng để đếm ở dòng tổng kết cuối vòng
                L($"↻ Thử lại {hangDoiThuLai.Count} shop rơi vì cầu nối: "
                  + string.Join(", ", hangDoiThuLai.Select(NhanShop)) + ".");
                await NghiGiuaShopAsync(ct).ConfigureAwait(false);
            }

            if (soShopDaThuLai < hangDoiThuLai.Count)
            {
                L($"⛔ Lượt thử lại KHÔNG đi hết: mới chạy lại {soShopDaThuLai}/{hangDoiThuLai.Count} shop — "
                  + $"{hangDoiThuLai.Count - soShopDaThuLai} shop rơi vì cầu nối phải đợi vòng sau.");
            }
            L($"Xong 1 vòng: {shopsDone}/{shopCount} shop, {totalOrders} đơn, {totalSlips} phiếu"
              + (hangDoiThuLai.Count > 0
                  ? $" (đã thử lại {soShopDaThuLai}/{hangDoiThuLai.Count} shop rơi vì cầu nối)"
                  : "")
              + (pickupFailedShops.Count > 0 ? $", bỏ qua {pickupFailedShops.Count} shop lỗi địa chỉ." : "."));
            // Đã đi hết toàn bộ shop của vòng → xoá mốc shop đã check để vòng sau (hoặc lần chạy sau) bắt đầu lại từ shop 1.
            _luuShopDaCheckCuoi?.Invoke(null);
            if (pickupFailedShops.Count > 0)
            {
                var tenLoi = string.Join(", ", pickupFailedShops);
                return new OrdersBridgeRunResult(shopCount, shopsDone, totalOrders, totalSlips, false,
                    $"Không đặt được địa chỉ lấy hàng ({_province}) ở shop {tenLoi} — đã bỏ qua shop đó, chưa in phiếu; các shop khác vẫn chạy.",
                    PickupAddressFailed: true, PickupFailedShop: tenLoi, PickupOkShops: NhanShopDatDuoc());
            }
            return new OrdersBridgeRunResult(shopCount, shopsDone, totalOrders, totalSlips, false, null,
                PickupOkShops: NhanShopDatDuoc());
        }
        catch (OperationCanceledException) { throw; }
        catch (CauNoiRotGiuaChangException ex)
        {
            // Chỉ tới đây khi chặng gãy nằm NGOÀI vòng shop (đăng nhập/SSO/đọc danh sách shop) — trong vòng shop
            // đã có khối catch riêng từng shop. Phân biệt với timeout thường vì thuốc khác nhau: lệnh MẤT chứ
            // extension không hề chậm, nên đọc log đừng đi tìm bên extension.
            L("⛔ " + ex.Message);
            return KetQuaDungVong(ex.Message);
        }
        catch (TimeoutException)
        {
            // Vẫn mang theo tín hiệu của những shop ĐÃ đặt được địa chỉ trước lúc gãy — banner của chúng phải
            // được gỡ dù vòng kết thúc bằng lỗi (hai đường đụng những shop KHÁC NHAU, không giẫm nhau).
            L("Cầu nối: hết thời gian chờ phản hồi từ extension.");
            return KetQuaDungVong("Hết thời gian chờ phản hồi từ extension.");
        }
        catch (Exception ex)
        {
            L("Cầu nối lỗi: " + ex.ToString());
            return KetQuaDungVong(ex.Message);
        }
    }

    /// <summary>
    /// <b>Tải LẠI phiếu MỘT đơn qua cầu nối extension</b> (nút "Tải phiếu" màn Đơn hàng) — thân nằm ở
    /// <see cref="ShopFlowRunner.RedownloadSlipAsync"/> (thao tác trên tab shop đang mở). Ném
    /// <see cref="InvalidOperationException"/> khi cầu nối chưa khởi động / extension chưa kết nối (fail-fast, để
    /// caller báo đúng lý do thay vì ngồi chờ timeout).
    /// </summary>
    public Task<bool> RedownloadSlipAsync(string orderSn, CancellationToken ct = default)
        => _flow.RedownloadSlipAsync(orderSn, ct);

    // Lát cắt dùng chung (GĐ1 + đuôi GĐ2): readShopList → openShopDetail(shop đầu) → readToShip.
    private async Task<OrdersBridgeSliceResult> RunSliceCoreAsync(CancellationToken ct)
    {
        // 1) Đọc danh sách shop.
        await _channel.SendAsync(new { action = "readShopList" }).ConfigureAwait(false);
        var shopListJson = await _channel.AwaitAsync(_channel.ShopList, OrdersBridgeChannel.ChoChang.ShopList, ct).ConfigureAwait(false);
        var shops = ShopeeLoginService.ParseShopListJson(shopListJson);
        _onShopListRead?.Invoke(shops); // tab "Shops": App lưu danh sách shop (mọi shop, kể cả 0 đơn).
        L($"Đọc được {shops.Count} shop từ /portal/shop.");
        if (shops.Count == 0)
        {
            return new OrdersBridgeSliceResult(shops, null, null, false,
                "Không đọc được shop nào (đã tới /portal/shop chưa?).");
        }

        // 2) Mở "Chi tiết" shop đầu bằng trusted click (kỳ vọng KHÔNG captcha).
        var firstShopId = shops[0].ShopId;
        // Nhãn shop cho khớp chữ ký (callback null ở "Chạy thử" nên không dùng, nhưng phải truyền). shops[0] an toàn (đã guard rỗng ở trên).
        // "Chạy thử" dựng phiên KHÔNG rót callback nào (xem AccountsViewModel.Phien) ⇒ bước BÙ "tự tải lại phiếu
        // thiếu" trong RunShopOrdersAsync tự tắt: lát cắt này chỉ ĐỌC, không lưu DB, nên không được kéo theo nó.
        var firstShopLogin = string.IsNullOrWhiteSpace(shops[0].LoginName) ? shops[0].ShopName : shops[0].LoginName;
        L($"Mở 'Chi tiết' shop đầu (id={firstShopId}) bằng trusted click...");

        // Cột tiến độ tab "Shops": lát cắt chỉ chạy shop đầu — vẫn báo bắt đầu/xong y như vòng RunAllShopsAsync.
        _onShopCheckStarted?.Invoke(firstShopLogin);
        try
        {
            await _channel.SendAsync(new { action = "openShopDetail", shopId = firstShopId }).ConfigureAwait(false);
            var detail = await _channel.AwaitAsync(_channel.Detail, OrdersBridgeChannel.ChoChang.Detail, ct).ConfigureAwait(false);
            if (detail == "captcha" || _channel.CaptchaSeen)
            {
                L("PHÁT HIỆN captcha/verify khi mở Chi tiết — cần soi lại.");
                return new OrdersBridgeSliceResult(shops, firstShopId, null, true,
                    "Rơi vào trang verify/captcha khi mở Chi tiết.");
            }
            L("Đã mở tab shop (không captcha).");

            // 3) Đọc số "Chờ Lấy Hàng".
            await _channel.SendAsync(new { action = "readToShip" }).ConfigureAwait(false);
            var raw = await _channel.AwaitAsync(_channel.ToShip, OrdersBridgeChannel.ChoChang.ToShip, ct).ConfigureAwait(false);
            var toShip = ShopeeDashboard.ParseToShipCount(raw);
            L($"Số 'Chờ Lấy Hàng' đọc được: {(toShip?.ToString() ?? "null")} (raw='{raw}').");

            // 4) GĐ3: đọc đơn (Phần A) + nếu ToShip>0 thì xử đơn (Phần B).
            var (ordersCount, slipsSaved) = await _flow.RunShopOrdersAsync(firstShopId, firstShopLogin, toShip ?? 0, ct).ConfigureAwait(false);
            if (_channel.CaptchaSeen)
            {
                return new OrdersBridgeSliceResult(shops, firstShopId, toShip, true,
                    "Rơi vào trang verify/captcha khi đọc/xử đơn.", ordersCount, slipsSaved);
            }
            if (_flow.PickupFailedShop is not null)
            {
                // "Chạy thử" chỉ 1 shop — bỏ qua shop đó, đừng báo OK (người soi sẽ tưởng đã chạy trọn).
                return new OrdersBridgeSliceResult(shops, firstShopId, toShip, false,
                    $"Không đặt được địa chỉ lấy hàng ({_province}) — đã bỏ qua shop, KHÔNG in phiếu.", ordersCount, slipsSaved);
            }

            return new OrdersBridgeSliceResult(shops, firstShopId, toShip, false, null, ordersCount, slipsSaved);
        }
        finally
        {
            // XONG shop (kể cả lỗi/captcha) → tắt vòng quay, chấm ở lại.
            _onShopCheckFinished?.Invoke(firstShopLogin);
        }
    }

    private OrdersBridgeSliceResult Fail(string message) =>
        new(Array.Empty<ShopListItem>(), null, null, _channel.CaptchaSeen, message);

    /// <summary>
    /// Đóng trình duyệt sạch của phiên này: kill <see cref="Process"/> (nếu còn) RỒI quét theo hồ sơ để chắc không
    /// sót tiến trình nào.
    /// <para><b>Vì sao không chỉ kill handle:</b> Brave/Chrome có thể fork tiến trình browser THẬT sang PID khác rồi
    /// stub thoát — lúc đó <c>Process.HasExited</c> đã true nên kill trượt, browser thật sống mồ côi giữ hồ sơ, và
    /// vòng SAU chết ngay ở bước đăng nhập ("Trình duyệt thoát ngay khi khởi động"). Quét theo <c>--user-data-dir</c>
    /// bắt được cả bản fork.</para>
    /// KHÔNG match 'shopee-orders' — chỉ đụng hồ sơ của chính phiên này, không cướp cửa sổ của phiên khác.
    /// Best-effort: nuốt mọi lỗi.
    /// </summary>
    public void DongTrinhDuyetSach()
    {
        try
        {
            var p = Process;
            if (p is { HasExited: false })
            {
                p.Kill(entireProcessTree: true);
            }
        }
        catch { /* bỏ qua */ }

        try { BrowserProfileGuard.FreeProfile(_userDataDir, alsoMatchBridgeExtension: false, _log); }
        catch { /* bỏ qua */ }
    }

    public void Dispose() => _channel.Dispose();
}
