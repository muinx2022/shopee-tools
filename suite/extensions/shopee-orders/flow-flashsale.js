// "Flash Sale Của Shop" — LƯỢT DÒ DOM (chẩn đoán). Chương trình thứ BA, plan
// `plans/2026-08-27-flash-sale-cua-shop.md` (§2 bảng selector đã dump từ DOM thật, §3 bốn ẩn số phải đo).
//
// ══════════════════════════════════════════════════════════════════════════════════════════════════════════
// LUẬT SỐ MỘT CỦA FILE NÀY: **KHÔNG TẠO GÌ.** Đây là lượt CHỈ ĐỌC.
//
// Khác HẲN nút "Dò DOM KM": lượt dò KM đi CHUNG hàm với luồng thật nên bước 9 của nó bấm nút Xác nhận ở thanh
// đáy và TẠO CHƯƠNG TRÌNH THẬT — đêm 27/08/2026 nó để lại một chương trình rác tên `DO DOM KM 1756...` trên
// shop người dùng, phải vào Seller Centre xoá tay. Lượt dò Flash Sale KHÔNG được lặp lại chuyện đó.
//
//  ĐƯỢC PHÉP: mở trang · bấm "Tạo" · mở modal khung giờ · bấm mũi tên đổi tháng · chọn một ngày · tick một
//             khung · bấm "Xác nhận" CỦA MODAL KHUNG GIỜ (chỉ điền vào form, chưa tạo gì) · mở modal sản
//             phẩm · bấm mũi tên sắp xếp · đọc bảng.
//  CẤM TUYỆT ĐỐI: bấm nút "Xác nhận" CUỐI CÙNG của trang tạo chương trình (thanh đáy `.fix-container` — nút
//             tạo THẬT), và bấm "Xác nhận" ở footer modal sản phẩm. Lượt dò kết thúc bằng **HỦY** modal sản
//             phẩm rồi rời trang.
//
//  ⚠ BẪY TÊN CLASS: `confirm-btn` dùng cho CẢ HAI — nút Xác nhận của modal khung giờ (được phép) và nút TẠO
//    THẬT ở thanh đáy (`.fix-container.fixed-bottom .confirm-btn`, xem `flow-km.js` bước 9). Vì vậy
//    {@link pageBamXacNhanKhungFlash} vừa neo cứng vào modal khung giờ, vừa TỪ CHỐI khi nút tìm được nằm
//    trong `.fix-container`. Đừng gỡ một trong hai chốt đó.
//
//  Ràng buộc này được KHOÁ BẰNG TEST đọc chính nguồn file này (`FlashSaleDoDomTests`): danh sách hàm có
//  `.click()` và danh sách chữ dùng để lọc nút đều là whitelist — mọc thêm một đường bấm là test đỏ.
// ══════════════════════════════════════════════════════════════════════════════════════════════════════════
//
// DÙNG LẠI, KHÔNG CHÉP: lịch của Flash Sale là CÙNG component EDS với luồng KM (`eds-date-table__cell`, có
// `disabled-next`/`disabled-prev`). Luật "ô ngày này có chọn được không" vì thế lấy NGUYÊN của `flow-km.js`
// (`pageCaiLuatONgayEds` → `window._edsONgayHopLe`). Viết bản luật thứ hai ở đây là đúng cái vừa trả giá 12/12
// shop sáng 27/08/2026 — hai bản luật trôi lệch nhau một phép so chuỗi.
//
// Kết quả gửi về C# qua { action:"pageData", kind:"flashDump", data:<json> } — kind RIÊNG, không dùng chung
// `kmDump`: hai lượt dò có hạn chặng khác nhau và một gói tới muộn của lượt kia không được tính cho lượt này.

import { ctx, send } from "./core.js";
import { execInTab } from "./exec.js";
import { sleep } from "./shared/util.js";
// Trusted click qua chrome.debugger — cú Xác nhận modal sản phẩm bằng `.click()` DOM bị Shopee ĐÓNG-mà-KHÔNG-LƯU
// (đo thật 22:42 30/08/2026, xem docblock pageToaDoNutXacNhanModalSp).
import { dbgSend, ensureDbg, trustedClick } from "./shared/dbg-input.js";
// DÙNG LẠI, nay từ HAI nguồn (tách 29/08/2026): hạ tầng chung — khung chấm bước + hai page-func thuần — nằm ở
// `flow-chung.js`; còn LUẬT Ô NGÀY vẫn ở `flow-km.js` (hai luật này bị test C# bóc nguyên văn theo tên file nên
// cố ý chưa dời). Thân các hàm đó KHÔNG được sửa: luồng KM đang chạy thật trên shop người dùng, và bản ở
// `flow-chung.js` nay là bản DUY NHẤT cho cả ba luồng — sửa một chữ là sửa cho cả ba.
import { buoc, pageClickTheoSelector, pageDemPhanTuHien } from "./flow-chung.js";
// pageGoPhanTram + pageAnhBangSanPham (31/08/2026): form TẠO FLASH mới của Shopee dùng đúng khối bulk
// `.detail-batch` + ô %GIẢM từng dòng như form KM — tái dùng đồ nghề đã chạy thật hằng đêm của km.
import { pageCaiLuatONgayEds, pageCaiLuatMuiTenThang, pageGoPhanTram, pageAnhBangSanPham } from "./flow-km.js";

// ⚠ HAI HẰNG `PHAN_TRAM_GIAM_FLASH = 5` / `SO_LUONG_KM_FLASH = 10` ĐÃ XOÁ (03/09/2026). Người dùng chốt: "số 5%
// và 10 sp cho phép nhập từ client, không fix trong code — lần sau là 10%, 20 sp thì không phải sửa code". Hai
// con số nay tới từ GÓI THAM SỐ C# (`ThamSoFlashSale.Goi()` → `tp.phanTramGiam` / `tp.soLuongThamGia`), nguồn là
// hai ô trên tab Flash Sale của form "KM của Shop". `docThamSoFlash` kiểm chúng FAIL-CLOSED y như ngày/tháng/năm,
// nên một bản client cũ không gửi sẽ dừng ở bước giả "0. Tham số" chứ KHÔNG lặng lẽ rơi về 5/10 —
// rơi-về-mặc-định là dựng lại đúng hai hằng vừa gỡ, và người dùng đổi số trên form mà Shopee vẫn nhận số cũ.

/// Số nhịp (×500ms) chờ một khối DOM hiện ra (sidebar, modal, bảng). 60 nhịp = 30 giây.
/// Hết nhịp thì bước KẾT LUẬN LÀ HỎNG kèm số đếm từng neo — tuyệt đối không "coi như rỗng rồi đi tiếp".
const CHO_NHIP = 60;

/// Số nhịp (×500ms) chờ BẢNG VẼ LẠI sau một cú bấm sắp xếp. Ngắn hơn {@link CHO_NHIP} vì đây là render lại
/// tại chỗ, không phải tải modal; nhưng KHÔNG được thay bằng `sleep` mù: ba mốc T0/T1/T2 chỉ có nghĩa khi mỗi
/// mốc đọc trên một bảng đã lắng (xem plan §3.1).
const CHO_VE_LAI_NHIP = 24;

/// Nhịp đọc lịch khi CHỜ NÓ LẮNG (ms) + trần số nhịp.
///
/// ══════════════════════════════════════════════════════════════════════════════════════════════════════
/// ⚠ BẢN VÁ CỦA LỖI ĐÃ LÀM KẾT LUẬN SAI HẲN MỘT SHOP (lượt 10:27:07 ngày 27/08/2026, Alina Store1):
/// dump báo tháng 8 có `soNgayDo: 0` và cả phiên chẩn đoán đi tới kết luận "shop này hết ngày đỏ tháng 8".
/// SAI. Người dùng mở tay đúng modal đó và dump DOM: ngày 27 có `timeslots` + `"5 khung giờ"`, ngày 28–31 có
/// `"8 khung giờ"`. Tức tháng 8 CÓ ngày đỏ.
///
/// Mốc thời gian nói rõ nguyên nhân: bước 3 mở modal xong lúc 10:26:48, bước 4 dump lúc 10:27:07 — mà 19 giây
/// đó gồm CẢ lật sang tháng sau lẫn lật về. Tháng này bị chụp NGAY KHI modal vừa bung, tháng sau chụp vài giây
/// sau và ra 10 ngày đỏ. Lưới ngày render NGAY với toàn `no-timeslot`; số khung giờ về SAU từ một lượt gọi API
/// rồi Vue mới vẽ lại. Bước 3 chỉ chờ `.eds-date-table__cell` TỒN TẠI — mà nó tồn tại từ giây đầu.
///
/// ⇒ Chờ **BẢNG LẮNG**, không phải chờ phần tử tồn tại. Đúng lớp lỗi `flow-combo.js` đã trả giá đêm 26/08 và
/// đã có bản vá (`choBangLang` bên đó); đây là bản cho lịch.
/// ══════════════════════════════════════════════════════════════════════════════════════════════════════
const LANG_NHIP_MS = 400;
const LANG_TRAN_NHIP = 30;

/// Số lượt đọc LIÊN TIẾP phải cho CÙNG vân tay thì mới coi là lắng (cửa sổ yên = 3 × 400ms = 1,2s).
/// Một lượt gọi API rồi Vue vẽ lại hiếm khi có khoảng lặng dài hơn thế ở giữa; 2 nhịp (0,8s) thì mỏng.
const LANG_CAN_GIONG = 3;

/// Nhịp TỐI THIỂU phải chờ trước khi được phép công nhận "lắng" cho một tháng đang có **0 ngày đỏ**.
///
/// ⚠ ĐÂY KHÔNG PHẢI cửa "phải có ít nhất một ngày đỏ mới tính là lắng" — cửa như vậy sẽ treo đủ trần rồi báo
/// sai cho tháng THẬT SỰ hết khung, mà tháng hết khung là kết quả hợp lệ. Đây chỉ là đòi hỏi KIÊN NHẪN HƠN ở
/// đúng ca không phân biệt được: "API chưa về" và "tháng không còn khung nào" nhìn GIỐNG HỆT nhau ở nhịp đầu —
/// cả hai đều là lưới ngày toàn `no-timeslot` đứng yên. Tháng có khung thì lắng sau ~3 nhịp như thường.
/// 12 nhịp ≈ 4,8s: dài hơn hẳn lượt gọi API của Shopee, và chỉ tốn thêm chừng ấy cho tháng rỗng thật.
/// Cùng khuôn `LANG_TRAN_NHIP_RONG` của `flow-combo.js` (ca "bảng 0 dòng").
const LANG_NHIP_TOI_THIEU_KHI_RONG = 12;

/// Nhịp + trần chờ **THÂN TRANG** render sau một cú điều hướng.
///
/// ══════════════════════════════════════════════════════════════════════════════════════════════════════
/// ⚠ LỖI THẬT LẦN THỨ HAI CỦA CÙNG MỘT LỚP (lượt 12:01 ngày 27/08/2026). Nhật ký:
///     12:01:31  bước 1. Vào Flash Sale Của Shop: ok
///     12:01:31  bước 2. Bấm Tạo: KHÔNG ăn          ← CÙNG MỘT GIÂY
/// Dump trang lúc đó: `landingPageHeader: 0`, `landingPageBtn: 0`, `soDongTbody: 0`, `coKhoiRong: false`,
/// `soPhanTuChuDungBangTao: 0`, và cả 39 phần tử bấm được đều là breadcrumb + link sidebar — **KHÔNG một
/// phần tử nào của thân trang**. Tức trang chưa vẽ thân, không phải selector sai.
///
/// Luật neo-theo-chữ (`_flashNutTao`) KHÔNG sai — lúc nó chạy thì chưa có gì để neo. Lượt 1 (10:26:48) qua
/// được cũng trong CÙNG một giây, tức chỉ là trang tình cờ vẽ kịp.
///
/// Đây đúng lớp lỗi đã vá cho lịch khung giờ (`LANG_NHIP_MS`): đọc TRƯỚC khi nội dung bất đồng bộ về. Lần đó
/// là dữ liệu khung giờ, lần này là cả thân trang.
///
/// ⚠ CHỜ THEO DẤU HIỆU, KHÔNG `sleep` MÙ. Repo này đã bỏ `sleep` cố định ở luồng combo vì đúng lý do đó:
/// máy nhanh thì phí, máy chậm thì vẫn trượt (xem `flow-combo.js/LANG_NHIP_MS`).
/// 40 nhịp × 500ms = 20s: rộng hơn hẳn một lượt render thân trang, và chạm trần là bước ĐỎ có kèm dump.
/// ══════════════════════════════════════════════════════════════════════════════════════════════════════
const THAN_TRANG_NHIP_MS = 500;
const THAN_TRANG_TRAN_NHIP = 40;

/// Trần nhịp chờ **LỚP PHỦ ĐANG-TẢI HIỆN** (× {@link LANG_NHIP_MS} = 13 × 400ms ≈ 5,2s) và chờ nó **TẮT**
/// (75 × 400ms = 30s).
///
/// ══════════════════════════════════════════════════════════════════════════════════════════════════════
/// ⚠ TÍN HIỆU ĐÚNG CỦA "SHOPEE ĐÃ SẮP XẾP XONG CHƯA" — người dùng chỉ ra 27/08/2026:
///     "với danh sách tầm 1k sp, việc sắp xếp lại rồi list ra cần thời gian, trong khi app gần như click liên tục"
/// Số đo xác nhận: cả bước 9 chạy 22:39:30 → 22:39:35 — **5 giây cho 3 cú bấm + 4 lần chụp**.
///
/// VÌ SAO "CHỜ ĐỔI NỘI DUNG" MỘT MÌNH KHÔNG ĐỦ (và chờ lâu hơn cũng không vá được): nó KHÔNG phân biệt được
/// "chưa vẽ xong" với "đã vẽ xong nhưng thứ tự TRÙNG". Ca thứ hai CÓ THẬT — shop toàn 0 doanh số thì tăng dần
/// cho ra đúng thứ tự cũ. Hết trần chờ-đổi là vẫn phải đoán.
///
/// Lớp phủ thì dứt khoát. Modal "Chọn Sản Phẩm" có sẵn:
///     <div data-v-f2ae152c="" class="eds-table__loading" style="display: none;"></div>
/// ⇒ chờ nó HIỆN rồi TẮT. Không suy đoán từ nội dung, và không chết ở ca thứ tự trùng.
///
/// ⚠ HAI TẦNG, KHÔNG PHẢI THAY THẾ: lớp phủ có thể chớp quá nhanh để bắt kịp, hoặc Shopee đổi markup. Không
/// thấy lớp phủ ⇒ rơi về {@link choBangDoiRoiLang}. `nguonTinHieu` trong dump nói mốc đó tin được tới mức nào.
///
/// ⚠ PHẢI NEO TRONG ĐÚNG MODAL SẢN PHẨM: modal khung giờ cũng có một `.eds-table__loading`. Bắt nhầm cái của
/// bảng khác là chờ nhầm thứ. `soLopPhuTrongDom` ghi vào dump để thấy ngay có bao nhiêu cái.
/// ══════════════════════════════════════════════════════════════════════════════════════════════════════
const LOP_PHU_HIEN_TRAN_NHIP = 13;
const LOP_PHU_TAT_TRAN_NHIP = 75;

/// Trần số nhịp chờ bảng BẮT ĐẦU ĐỔI sau một cú bấm sắp xếp (× {@link LANG_NHIP_MS} = 20 × 400ms = 8s).
///
/// ══════════════════════════════════════════════════════════════════════════════════════════════════════
/// ⚠ BẢN VÁ CỦA LỖI "PHÉP ĐO TRỄ MỘT CÚ BẤM" — số đo lượt 10 (Minoa Store, 27/08/2026).
///
/// Ba cú bấm mà chỉ MỘT lần thứ tự đổi:
///     M0 = `1,0,0,…` (A) · M1 = A (không đổi) · M2 = B · M3 = B (không đổi)
///
/// Nguyên nhân: {@link choBangSanPhamLang} hỏi "bảng đã ĐỨNG YÊN chưa" — mà NGAY SAU cú bấm, bảng vẫn đang
/// đứng yên ở trạng thái CŨ vì Shopee chưa kịp bắt đầu vẽ lại. Vòng chờ thấy vân tay không đổi qua
/// {@link LANG_CAN_GIONG} nhịp rồi chốt luôn vào **ảnh cũ**. Mọi mốc vì thế trễ đúng một cú bấm.
///
/// Đây là biến thể THỨ NĂM của lớp lỗi cả đợt, và là biến thể mới: bốn lần trước là "đọc trước khi dữ liệu
/// VỀ"; lần này là **"đọc trước khi việc vẽ lại BẮT ĐẦU"**. Chờ lắng KHÔNG bao hàm chờ đổi.
///
/// ⇒ Sau mỗi cú bấm: chụp vân tay TRƯỚC → bấm → chờ vân tay KHÁC → rồi mới chờ lắng. Đúng khuôn
/// {@link choDoiThang} vẫn dùng cho lịch.
/// ══════════════════════════════════════════════════════════════════════════════════════════════════════
const DOI_TRAN_NHIP = 20;

/// **ĐÚNG HAI CÚ BẤM để có "giảm dần". KHÔNG BAO GIỜ BA.**
///
/// ══════════════════════════════════════════════════════════════════════════════════════════════════════
/// LUẬT NÀY ĐẾN TỪ NGƯỜI DÙNG, nguyên văn (27/08/2026):
///     "bấm 1 lần là up, lần nữa là down, lần nữa là không sắp xếp, như chưa bấm gì"
///
/// Tức chu kỳ BA trạng thái: `chưa sắp xếp → up (tăng) → down (giảm) → chưa sắp xếp → …`
/// ⇒ đúng 2 cú bấm = **giảm dần** = doanh số cao nhất trước, khớp yêu cầu gốc.
///
/// ⚠ VÌ SAO CÚ THỨ BA LÀ TAI HOẠ ÂM THẦM: nó XOÁ SẠCH sắp xếp và trả bảng về thứ tự ban đầu. Luồng sẽ lặng
/// lẽ chọn 10 sản phẩm **tuỳ ý** thay vì 10 sản phẩm bán chạy nhất — mà mọi bước sau vẫn `ok`, nhật ký vẫn
/// xanh, không một dòng lỗi nào. Đây đúng lớp lỗi nguy hiểm nhất mà plan §3.1 sinh ra để chặn.
///
/// BA RÀNG BUỘC, ĐỪNG NỚI CÁI NÀO:
///  1. **CẤM mọi cơ chế THỬ LẠI quanh cú bấm sắp xếp.** Một vòng retry bọc ngoài sẽ đẻ ra cú thứ ba mà
///     không ai cố ý — và không ai nhìn thấy, vì cú thứ ba không hề báo lỗi.
///  2. Số cú bấm phải **BẰNG** hằng này, không phải "ít nhất".
///  3. Khi viết cửa kiểm sau này: trạng thái đích là **down**; trạng thái "như chưa bấm gì" phải bị chấm là
///     **HỎNG**, tuyệt đối không phải "chưa xong, bấm thêm phát nữa".
/// ══════════════════════════════════════════════════════════════════════════════════════════════════════
const SO_LAN_BAM_SAP_XEP = 2;

/// Trần số phần tử bấm được liệt kê trong bản dump chẩn đoán của trang danh sách.
///
/// ⚠ CẮT THÌ PHẢI NÓI ĐÃ CẮT BAO NHIÊU (trường `daCat`). Bản `slice(0, 12)` IM LẶNG bên `flow-km.js` đã làm
/// một phiên chẩn đoán đọc danh sách `["1".."12"]` thành "Shopee chỉ cho tới ngày 12" và suýt đi sửa nhầm hẳn
/// một cái trần không tồn tại. Cắt là để dump khỏi phình, không phải để giấu.
const TRAN_UNG_VIEN_DUMP = 60;

/// Trần số lần lật sang THÁNG SAU khi tháng đang hiện đã hết ngày đỏ.
///
/// Vì sao cần (số đo thật, lượt 10:27:07 ngày 27/08/2026): tháng 8 hết sạch ngày đỏ mà tháng 9 còn 10 ngày —
/// bỏ cuộc ngay ở tháng hiện tại là vứt cả lượt dò. Vì sao chỉ 2: Shopee phát hành khung Flash Sale theo
/// tuần/tháng gần, lật quá xa thì hoặc là markup đã đổi, hoặc là shop thật sự không còn khung — cả hai đều
/// phải nổi lên thành bước ĐỎ chứ không phải một vòng lặp tự đi tiếp mãi.
const TRAN_THANG_SANG = 2;

// Tab đang thao tác: sau `openShopDetail` shop nằm ở tab RIÊNG. Không lùi về tab picker (luật `orderTabIdStrict`).
function tabShop() {
  return ctx.shopTabId;
}

// ── Hàm THUẦN (không đụng trang, không đụng tab) ─────────────────────────────────────────────────────────────

/**
 * PURE — chọn NGÀY ĐÍCH cho lượt Flash Sale: **ưu tiên tuyệt đối NGÀY MAI**, không có thì lấy ngày đỏ gần nhất.
 *
 * <p>Luật (plan §4 chốt "luôn nhắm ngày mai, bất kể nó rơi vào tháng nào"):</p>
 * <list>
 *  · ngày mai CÓ trong danh sách và ĐỎ ⇒ lấy nó. Đây là đường duy nhất luồng thật sẽ dùng.
 *  · không ⇒ ngày đỏ NHỎ NHẤT lớn hơn ngày mai (gần nhất về phía TƯƠNG LAI).
 *  · vẫn không ⇒ ngày đỏ LỚN NHẤT nhỏ hơn ngày mai. Nhánh này CHỈ để lượt DÒ còn chạy tiếp được và luôn kèm
 *    `ly` nói rõ là đi lùi — luồng thật tuyệt đối không được mượn nhánh này, vì đặt flash sale cho một ngày
 *    đã qua là vô nghĩa.
 *  · không còn ngày đỏ nào ⇒ `{ soNgay: null }` và bước gọi HỎNG có tên.
 * </list>
 *
 * <p><b>Vì sao tách hàm THUẦN thay vì quyết định ngay trong page-func:</b> page-func chạy trong trang nên
 * không có test nào lái được; luật "ngày mai, không thì gần nhất" thì lại là chỗ sai âm thầm điển hình (lấy
 * nhầm ngày đỏ đầu danh sách ⇒ đặt flash sale cho một ngày chẳng ai chờ, nhật ký vẫn báo ok).</p>
 *
 * <p><b>Nhận THẲNG danh sách SỐ NGÀY ĐỎ</b>, không nhận mảng ô thô. Trước 27/08/2026 hàm này nhận `lich.o`
 * rồi tự lọc `o.do === true` — tức có HAI đường suy ra "ngày nào đỏ": trường `ngayDo` (dựng trong page-func,
 * là thứ đi vào dump) và phép lọc ở đây. Hai đường suy ra cùng một sự thật là mẫu đã cắn nhiều lần trong
 * chính file này; nay chỉ còn MỘT nguồn, và {@link ngayDoCuaLich} là cửa duy nhất đọc nó.</p>
 *
 * @param {Array<number>} dsNgayDo Các SỐ NGÀY đỏ của tháng đang hiện (chính là `lich.ngayDo`).
 * @param {number} ngayMai Số ngày trong tháng của NGÀY MAI.
 * @returns {{soNgay:number|null, ly:string}}
 */
export function chonNgayDich(dsNgayDo, ngayMai) {
  const do_ = (Array.isArray(dsNgayDo) ? dsNgayDo : [])
    .filter((n) => typeof n === "number" && isFinite(n));
  if (do_.length === 0) {
    return { soNgay: null, ly: "tháng đang hiện KHÔNG còn ngày đỏ nào" };
  }
  if (do_.indexOf(ngayMai) >= 0) {
    return { soNgay: ngayMai, ly: "ngày mai (" + ngayMai + ") ĐỎ — đúng ngày luồng thật nhắm tới" };
  }
  const sau = do_.filter((n) => n > ngayMai).sort((a, b) => a - b);
  if (sau.length > 0) {
    return { soNgay: sau[0], ly: "ngày mai (" + ngayMai + ") KHÔNG đỏ — lấy ngày đỏ gần nhất SAU nó: " + sau[0] };
  }
  const truoc = do_.filter((n) => n < ngayMai).sort((a, b) => b - a);
  return {
    soNgay: truoc[0],
    ly: "ngày mai (" + ngayMai + ") KHÔNG đỏ và không còn ngày đỏ nào SAU nó — lượt DÒ lùi về " + truoc[0]
      + " (luồng thật KHÔNG được dùng nhánh này)",
  };
}

/**
 * PURE — ngày ĐỎ ĐẦU TIÊN của một tháng KHÁC tháng đang đứng (dùng khi đã phải lật lịch sang tháng sau).
 *
 * <p>Cố ý gọi lại {@link chonNgayDich} với mốc `1` thay vì viết một vòng `min` thứ hai: `chonNgayDich(o, 1)`
 * nghĩa đen là "nhắm ngày 1, không đỏ thì lấy ngày đỏ gần nhất SAU nó" — đúng bằng "ngày đỏ đầu tháng". Một
 * luật, hai chỗ dùng.</p>
 *
 * <p>Vì sao CẦN nhánh này (số đo thật, lượt 10:27:07 ngày 27/08/2026): tháng 8 đã hết sạch ngày đỏ
 * (`soNgayDo: 0`) trong khi tháng 9 còn **10 ngày đỏ**. Bản trước dừng luôn ở "tháng đang hiện KHÔNG còn ngày
 * đỏ nào" ⇒ lượt dò bỏ cuộc ở bước 5 và ba ẩn số còn lại (sắp xếp Doanh số, ô "Xem sản phẩm có sẵn", cấu trúc
 * modal sản phẩm) không đo được cái nào. Đây cũng đúng luật đã chốt ở plan §3: **bỏ ràng buộc "chỉ tháng hiện
 * tại"**, luôn nhắm ngày đỏ gần nhất, rơi vào tháng nào cũng được.</p>
 */
/**
 * PURE — **"URL này đã RỜI trang danh sách để sang trang TẠO chưa"**: phép đọc lại của bước 2.
 *
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 * Bản trước đọc lại bước 2 bằng `button.time-slot-btn` (KHÔNG phải `.eds-date-table__cell` — ô ngày là dấu
 * hiệu của modal khung giờ ở bước 3). Dấu hiệu ấy KHÔNG sai: `time-slot-btn` chỉ có trên trang tạo, nên lượt
 * 1 báo `ok` là ok thật, không phải may. Nhưng nó YẾU HƠN URL ở đúng ca đang gặp: nút Tạo không ăn thì trang
 * đứng nguyên `/list` và phép đọc chỉ nói được "không thấy ô khung giờ" — không nói được là **chưa hề rời
 * trang**. Nay đọc lại bằng URL, còn `time-slot-btn` xuống làm bằng chứng đi kèm.
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 *
 * HAI vế, cần cả hai:
 *  · vẫn phải nằm trong khu `/shop-flash-sale` — click bắn đi đâu đó khác (hết phiên → về trang đăng nhập)
 *    thì "đã rời /list" đúng về chữ nghĩa nhưng là một cái ĐẠT sai;
 *  · và đường dẫn (bỏ query/hash) KHÔNG còn kết thúc bằng `/shop-flash-sale/list`.
 * `?type=0` là trạng thái tab mặc định của trang danh sách nên phải cắt query trước khi so.
 */
export function daRoiTrangList(url) {
  const u = String(url === null || url === undefined ? "" : url).trim();
  if (!u) { return false; }
  const duong = u.split("#")[0].split("?")[0].replace(/\/+$/, "");
  if (duong.indexOf("/shop-flash-sale") < 0) { return false; }
  return !/\/shop-flash-sale\/list$/.test(duong);
}

/**
 * Cài luật **"phần tử nào là nút Tạo"** lên `window._flashNutTao`.
 *
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 * LỖI THẬT: lượt 10:26 ngày 27/08 bước 2 `ok`; lượt 10:48 và 11:34 **cùng shop, cùng code** đều hỏng với
 * `selectorDung: null` — tức KHÔNG selector nào khớp, không hề có cú bấm nào. Trang danh sách render KHÁC
 * nhau tuỳ trạng thái, và `.landing-page-header button.landing-page-btn` chỉ đúng cho MỘT trạng thái (nhiều
 * khả năng là màn hình trống, chưa có chương trình nào).
 *
 * ⇒ Neo theo CHỮ, không theo class bố cục. Class bố cục là thứ Shopee đổi theo trạng thái màn hình; chữ trên
 * nút thì không.
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 *
 * Ba luật, theo đúng thứ tự:
 *  1. **Chữ phải ĐÚNG BẰNG `"tạo"`** (bỏ hoa/thường, gọn khoảng trắng). `"Tạo Chương Trình Mới"` KHÔNG khớp —
 *     khớp "chứa" là mở đường cho mọi nút có chữ "tạo" trong đó, và trang này còn có nút TẠO THẬT ở cuối luồng.
 *  2. **LOẠI ứng viên nằm trong vùng TAB / LỌC** — thanh tab của trang danh sách có mục chữ "Tạo"/"Đang diễn
 *     ra"… và bấm vào đó chỉ đổi bộ lọc, không điều hướng, nhưng vẫn `ok` theo kiểu "click không ném".
 *     Khớp theo TOKEN, không theo chuỗi con: `eds-table` KHÔNG được tính là tab vì có chứa chữ "tab".
 *  3. Trong số còn lại, ƯU TIÊN ứng viên dính `landing-page` (đường của lượt 1, giữ lại nhưng KHÔNG độc quyền).
 *
 * @param {Array<{text:string, tag:string, cls:string, role:string, toTien:Array}>} dsUngVien theo thứ tự DOM.
 * @returns {{chiSo:number, nguon:string|null, soNhan:number, ly:string}}
 */
export function pageCaiLuatNutTaoFlash() {
  window._flashNutTao = function (dsUngVien) {
    const gon = (s) => String(s === null || s === undefined ? "" : s).replace(/\s+/g, " ").trim();
    const na = (s) => gon(s).toLowerCase();
    // Khớp TOKEN — `eds-table` chứa chuỗi con "tab" nhưng KHÔNG phải vùng tab. Cùng bài học với luật ô ngày.
    const laTabHoacLoc = (mo) => {
      if (!mo) { return false; }
      const vai = na(mo.role);
      if (vai === "tab" || vai === "tablist") { return true; }
      if (na(mo.tag) === "li") { return true; }
      const token = na(mo.cls).split(/\s+/).filter(Boolean);
      return token.some((t) => t === "tab" || t.indexOf("tab-") === 0 || t.indexOf("tabs") >= 0
                            || t.indexOf("filter") >= 0);
    };
    const ds = Array.isArray(dsUngVien) ? dsUngVien : [];
    const nhan = [];
    for (let i = 0; i < ds.length; i++) {
      const u = ds[i];
      if (!u || na(u.text) !== "tạo") { continue; }
      const chuoi = [u].concat(Array.isArray(u.toTien) ? u.toTien : []);
      if (chuoi.some(laTabHoacLoc)) { continue; }
      nhan.push(i);
    }
    if (nhan.length === 0) {
      return { chiSo: -1, nguon: null, soNhan: 0,
               ly: "không thấy phần tử bấm được nào có chữ ĐÚNG BẰNG 'Tạo' ngoài vùng tab/lọc (đã xét "
                 + ds.length + " ứng viên)" };
    }
    const uuTien = nhan.filter((i) => {
      const u = ds[i];
      const chuoi = [u].concat(Array.isArray(u.toTien) ? u.toTien : []);
      return chuoi.some((m) => na(m && m.cls).indexOf("landing-page") >= 0);
    });
    if (uuTien.length > 0) {
      return { chiSo: uuTien[0], nguon: "landing-page (đường ưu tiên)", soNhan: nhan.length,
               ly: "nút chữ 'Tạo' nằm trong vùng landing-page" };
    }
    return { chiSo: nhan[0], nguon: "chữ 'Tạo' (KHÔNG thấy vùng landing-page)", soNhan: nhan.length,
             ly: "không có ứng viên nào dính landing-page — lấy nút chữ 'Tạo' đầu tiên ngoài vùng tab/lọc" };
  };
  return { ok: true };
}

/**
 * PURE — **CỬA DUY NHẤT** rút danh sách ngày ĐỎ ra khỏi một bản dump lịch.
 *
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 * Vì sao phải là MỘT cửa có tên, chứ không phải `lich.o` đọc thẳng ở chỗ gọi: bản dump lịch đi qua nhiều
 * hình dạng (bản THÔ của `pageDumpLichFlash`, bản GỌN của `gonLich` đưa vào `anSo`), và chỗ gọi đọc nhầm
 * hình dạng thì `undefined` lặng lẽ thành "không có ngày đỏ nào" — hỏng mà không một câu lỗi nào nói ra.
 * Gom về một cửa thì đổi tên trường là đỏ ở đúng một chỗ, và có lưới khoá được nó.
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 */
export function ngayDoCuaLich(lich) {
  const ds = lich && Array.isArray(lich.ngayDo) ? lich.ngayDo : [];
  return ds.filter((n) => typeof n === "number" && isFinite(n));
}

/**
 * PURE — tháng KẾ TIẾP (hoặc LIỀN TRƯỚC) của một mốc tháng/năm.
 */
export function thangKeTiep(thang, nam, tien) {
  if (typeof thang !== "number" || typeof nam !== "number" || !isFinite(thang) || !isFinite(nam)) {
    return null;
  }
  const moc = nam * 12 + (thang - 1) + (tien ? 1 : -1);
  return { thang: (moc % 12) + 1, nam: Math.floor(moc / 12) };
}

/**
 * PURE — **"cú bấm mũi tên có đưa lịch tới ĐÚNG tháng mong đợi không"**.
 *
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 * ⚠ LỖI THẬT lượt 12:20:04 ngày 27/08/2026 — CHỖ NÀY, KHÔNG PHẢI CHỖ NÀO KHÁC.
 * Bước 4 chạy đúng: đọc Tháng 8/2026 (5 ngày đỏ 27–31, khớp DOM người dùng dump tay), sang Tháng 9/2026
 * (10 ngày đỏ), rồi bấm `eds-picker-header__prev` để về. Nút bấm ĐÚNG (dump ghi rõ `veLai.cls`), nhưng lịch
 * đáp xuống **Tháng 9/2025** — lùi 12 tháng chứ không phải 1.
 *
 * Bản cũ của `daDoiThang` chỉ hỏi "tháng/năm có KHÁC không". 9/2026 → 9/2025 là KHÁC, nên nó báo `daDoi:true`
 * và cả luồng đi tiếp trên một cái lịch cách đích 11 tháng. Bước 5 rồi lật thêm 2 tháng nữa (10/2025,
 * 11/2025), tháng nào cũng 0 ngày đỏ — và báo "không tìm được ngày đỏ nào" trong khi bước 4 vừa đo được 5.
 *
 * "Khác" KHÔNG PHẢI "đúng". Từ nay phải đáp xuống ĐÚNG một tháng theo ĐÚNG chiều đã bấm; lệch bao nhiêu cũng
 * là HỎNG CÓ TÊN, kèm cả mốc đi lẫn mốc đến để đọc dump là biết ngay lệch mấy tháng.
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 */
export function dungThangMongDoi(truoc, sau, tien) {
  if (!truoc || !sau) { return false; }
  const mong = thangKeTiep(truoc.thang, truoc.nam, tien);
  if (!mong) { return false; }
  if (typeof sau.thang !== "number" || typeof sau.nam !== "number") { return false; }
  return sau.thang === mong.thang && sau.nam === mong.nam;
}

/**
 * PURE — **VÂN TAY** của một bản dump lịch: đủ để biết "picker còn đang vẽ lại hay đã đứng yên".
 *
 * Gộp CẢ BA thứ đổi khi dữ liệu khung giờ về: tháng/năm trên header, `class` từng ô (ô đỏ mọc thêm
 * `timeslots`/đổi trạng thái), và `textDayDu` từng ô (dòng `"8 khung giờ"` là một phần của nó). Chỉ đếm số ngày
 * đỏ thì KHÔNG đủ — lượt vẽ lại có thể đổi ruột mà con số tình cờ không đổi, đúng cái bẫy `flow-combo.js` vá
 * bằng "số dòng # khoá dòng đầu".
 *
 * Trả `null` khi chưa đọc được lịch — vòng chờ coi `null` là "chưa có gì để so", không bao giờ tính là lắng.
 */
export function vanTayLich(lich) {
  if (!lich || lich.coLich !== true || !Array.isArray(lich.o)) { return null; }
  const dau = String(lich.thang === null || lich.thang === undefined ? "?" : lich.thang)
    + "/" + String(lich.nam === null || lich.nam === undefined ? "?" : lich.nam);
  return dau + "#" + lich.o.map((o) => String(o.cls || "") + ":" + String(o.textDayDu || "")).join("|");
}

/** PURE — hai lần chụp có CÙNG vân tay không. `null` (chưa đọc được) KHÔNG bao giờ giống cái gì, kể cả `null`. */
export function vanTayGiongNhau(a, b) {
  if (a === null || a === undefined || b === null || b === undefined) { return false; }
  return a === b;
}

/**
 * PURE — **có được phép kết luận "tháng này còn/hết bao nhiêu ngày đỏ" không.**
 *
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 * ĐÂY LÀ CHỐT CHỐNG ĐÚNG CÁI KẾT LUẬN SAI ĐÃ XẢY RA. Bản dump 10:27:07 ghi `soNgayDo: 0` trông y hệt một số
 * đo chắc chắn, không có một dấu hiệu nào nói rằng lịch lúc ấy còn đang vẽ dở — nên cả phiên chẩn đoán đọc nó
 * thành "Alina Store1 hết ngày đỏ tháng 8", và thiết kế nhánh "lật sang tháng sau" trên một tiền đề SAI.
 *
 * Từ nay: **chưa lắng ⇒ KHÔNG có kết luận nào**, con số vẫn được ghi ra dump nhưng mang cờ `chac:false` và một
 * câu nói rõ là không đáng tin. Số 0 đọc lúc bảng đang chảy và số 0 đọc lúc bảng đã đứng yên là HAI thứ khác
 * hẳn nhau, và dump phải phân biệt được chúng.
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 */
export function ketLuanNgayDo(lich, daLang) {
  if (!lich || lich.coLich !== true) {
    return { chac: false, soNgayDo: null, ly: "KHÔNG đọc được lịch" };
  }
  if (daLang !== true) {
    return {
      chac: false,
      soNgayDo: lich.soNgayDo,
      ly: "lịch CHƯA LẮNG — con số " + lich.soNgayDo + " ngày đỏ KHÔNG ĐÁNG TIN (dữ liệu khung giờ có thể "
        + "chưa về; đây đúng ca đã làm lượt 27/08 kết luận sai 'hết ngày đỏ')",
    };
  }
  return {
    chac: true,
    soNgayDo: lich.soNgayDo,
    ly: lich.soNgayDo > 0
      ? "lịch đã lắng: " + lich.soNgayDo + " ngày đỏ"
      : "lịch đã lắng và THẬT SỰ không còn ngày đỏ nào",
  };
}

export function chonNgayDoDauThang(dsNgayDo) {
  const kq = chonNgayDich(dsNgayDo, 1);
  return kq.soNgay === null
    ? { soNgay: null, ly: "tháng này KHÔNG có ngày đỏ nào" }
    : { soNgay: kq.soNgay, ly: "ngày đỏ ĐẦU TIÊN của tháng: " + kq.soNgay };
}

// ── Hàm chạy TRONG TRANG ────────────────────────────────────────────────────────────────────────────────────
// LƯU Ý sống còn (xem exec.js): mỗi page-func được chrome.scripting serialize ĐỘC LẬP, KHÔNG kèm hàm nào ngoài
// nó. Mọi helper phải viết LẶP LẠI bên trong từng hàm — đừng "dọn cho gọn" bằng cách rút ra ngoài.

/**
 * Cài luật **"ô ngày này có ĐỎ không"** lên `window._flashNgayDo`.
 *
 * ⚠ Hàm này KHÔNG chứa luật ô ngày — nó GỌI luật chung `window._edsONgayHopLe` do
 * {@link pageCaiLuatONgayEds} của `flow-km.js` cài. Phần riêng của Flash Sale chỉ có đúng một vế: ngày phải
 * CÓ khung giờ (`div.timeslots`). Chưa cài luật chung thì NÉM ngay chứ không có bản luật dự phòng tại chỗ —
 * bản dự phòng chính là bản chép thứ hai, và nó sẽ âm thầm chạy đúng cái sai cũ (12/12 shop, 27/08/2026).
 *
 * "ĐỎ" = ô ngày Shopee tô đỏ vì còn khung giờ nhận đăng ký: có `div.timeslots` **và** hợp lệ theo luật chung
 * (tức `disabled-next`/`disabled-prev` — chỉ là bo góc dải — vẫn ĐỎ; `disabled`, `out-of-month`, `is-empty`
 * thì không).
 */
export function pageCaiLuatNgayDoFlash() {
  window._flashNgayDo = function (className, coTimeslots) {
    if (typeof _edsONgayHopLe !== "function") {
      throw new Error("chưa cài window._edsONgayHopLe (thiếu bước chạy pageCaiLuatONgayEds của flow-km.js)");
    }
    if (!coTimeslots) { return false; }
    return _edsONgayHopLe(className);
  };
  return { ok: true };
}

/**
 * Cài luật **"số ngày của ô này là bao nhiêu"** lên `window._flashSoNgay`.
 *
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 * LỖI THẬT lượt dò 10:27:07 ngày 27/08/2026 (`logs/flash-dom-20260827-102707.json`): MỌI ô đều ra
 * `soNgay: null`, kể cả 10 ngày ĐỎ của tháng 9. Bản trước CLONE ô rồi xoá `div.timeslots`/`div.no-timeslot`
 * và đọc phần còn lại — nhưng DOM thật đặt số ngày NẰM TRONG chính hai khối đó, nên xoá xong còn lại chuỗi
 * rỗng. Hệ quả: `anSo.lichThangSau.ngayDo = [null ×10]`, và bước chọn ngày không có gì để bấm.
 *
 * SỐ ĐO THẬT của lượt đó, dùng làm căn cứ cho luật này:
 *   textContent="112 khung giờ"  slots-count="12 khung giờ"  ⇒ ngày 1
 *   textContent="28 khung giờ"   slots-count="8 khung giờ"   ⇒ ngày 2
 *   textContent="918 khung giờ"  slots-count="18 khung giờ"  ⇒ ngày 9
 *   textContent="10"             slots-count=(không có)      ⇒ ngày 10
 *
 * ⚠ VÌ SAO KHÔNG BÓC "CỤM CHỮ SỐ ĐẦU TIÊN" BẰNG REGEX: chuỗi `"288 khung giờ"` có HAI nghĩa hợp lệ —
 * ngày 2 với 88 khung, hoặc ngày 28 với 8 khung. Regex `^(\d{1,2})` luôn trả 28 nên nó SAI câm ở đúng một
 * nửa số ca. Phép TRỪ HẬU TỐ thì không mơ hồ: `p.slots-count` đọc riêng cho biết chính xác phần đuôi dài bao
 * nhiêu, cắt đi là còn lại số ngày. Regex chỉ còn là lưới CUỐI, và khi phải dùng thì `nguon` nói rõ
 * "KHÔNG CHẮC".
 *
 * ⚠ VÀ KHI ĐÃ TRỪ HẬU TỐ THÌ **KHÔNG ĐƯỢC RƠI TIẾP XUỐNG REGEX**: ô chỉ có mỗi dòng khung giờ
 * (`textContent="7 khung giờ"`, `slots="7 khung giờ"`) trừ xong còn chuỗi rỗng — đó là "KHÔNG có số ngày",
 * mà regex lại đọc ra ngày 7 từ chính con số ĐẾM KHUNG. Đọc nhầm số khung thành số ngày là đặt flash sale
 * cho một ngày ngẫu nhiên.
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 *
 * Thứ tự bốn đường, dừng ở đường đầu tiên cho câu trả lời:
 *  1. `p.date-text` — thẻ riêng cho số ngày, đường thẳng nhất (chưa xác nhận có thật trên DOM Shopee, nên
 *     phải VALIDATE chứ không tin sẵn).
 *  2. TRỪ HẬU TỐ `slots-count` khỏi textContent — đường CHÍNH, không mơ hồ. Trừ xong rỗng ⇒ trả null, HẾT.
 *  3. textContent vốn chỉ có số ngày (ô không còn khung giờ).
 *  4. Lưới cuối: cụm chữ số đầu, đánh dấu KHÔNG CHẮC.
 */
export function pageCaiLuatSoNgayFlash() {
  window._flashSoNgay = function (textDayDu, textSlots, textDateText) {
    const gon = (s) => String(s === null || s === undefined ? "" : s).replace(/\s+/g, " ").trim();
    const hopLe = (s) => (/^\d{1,2}$/.test(s) && Number(s) >= 1 && Number(s) <= 31 ? Number(s) : null);

    const truc = hopLe(gon(textDateText));
    if (truc !== null) { return { soNgay: truc, nguon: "p.date-text" }; }

    const day = gon(textDayDu);
    const slots = gon(textSlots);
    if (slots && day.length >= slots.length && day.slice(day.length - slots.length) === slots) {
      // ⚠ LỐI RA CỨNG — đừng nối thêm `if` nào sau khối này (xem cảnh báo thứ hai ở xmldoc).
      const con = hopLe(gon(day.slice(0, day.length - slots.length)));
      return con !== null
        ? { soNgay: con, nguon: "tru-hau-to-slots" }
        : { soNgay: null, nguon: "tru-hau-to-slots (còn rỗng — ô KHÔNG mang số ngày)" };
    }

    const chiSo = hopLe(day);
    if (chiSo !== null) { return { soNgay: chiSo, nguon: "textContent" }; }

    const m = day.match(/^(\d{1,2})/);
    const mo = m ? hopLe(m[1]) : null;
    return mo !== null
      ? { soNgay: mo, nguon: "MÒ theo cụm chữ số đầu (KHÔNG CHẮC — không đọc được slots-count)" }
      : { soNgay: null, nguon: "không đọc được số ngày" };
  };
  return { ok: true };
}

/**
 * Cài luật **"tiêu đề tháng đang hiện là gì"** lên `window._flashTieuDeThang`.
 *
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 * LỖI THẬT cùng lượt 10:27:07: tiêu đề đọc ra `"2026"` — MẤT hẳn chữ "Tháng 8".
 *
 * Header picker có BA `span.eds-picker-header__label`: `"Tháng 8"`, `"2026"`, và `"2020 – 2029"` (bảng thập
 * kỷ, `display:none`). Bản trước gom mọi khối có chữ "header/title/label" rồi **sắp theo ĐỘ DÀI tăng dần và
 * lấy cái ngắn nhất** — cái ngắn nhất đúng là `"2026"`.
 *
 * Hệ quả NẶNG hơn hẳn một dòng nhật ký xấu: cờ `tieuDeDaDoi` so hai tiêu đề bằng chuỗi, mà tháng 8 và tháng 9
 * đều ra `"2026"` ⇒ cờ báo `false` trong khi picker ĐÃ chuyển tháng thật (số ngày đỏ nhảy 0 → 10). Một cờ
 * "đã đổi tháng chưa" mà nói dối chính là thứ sẽ làm luồng thật đặt chương trình vào SAI THÁNG.
 *
 * ⇒ Luật: ghép MỌI label ĐANG HIỆN theo ĐÚNG THỨ TỰ DOM (bỏ label `display:none`), rồi tách `thang`/`nam`
 * thành hai trường SỐ. Phép so "đã đổi tháng chưa" từ nay dùng cặp số đó, không dùng chuỗi.
 *
 * Đối chiếu `flow-km.js/pageDocThangNamPicker`: nó gộp cả ba label thành `"Tháng 2 2027 2020 – 2029"` — không
 * mất tháng nên vẫn tách đúng, nhưng chuỗi bẩn. Bản này sạch hơn cả hai: lọc theo tính HIỆN, không theo độ dài.
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 *
 * @param {Array<{text:string, hien:boolean}>} dsNhan Các label theo ĐÚNG thứ tự DOM, kèm cờ đang hiện.
 */
export function pageCaiLuatTieuDeThangFlash() {
  window._flashTieuDeThang = function (dsNhan) {
    const gon = (s) => String(s === null || s === undefined ? "" : s).replace(/\s+/g, " ").trim();
    const hien = (Array.isArray(dsNhan) ? dsNhan : [])
      .filter((n) => n && n.hien === true)
      .map((n) => gon(n.text))
      .filter((t) => t.length > 0);
    const tieuDe = hien.join(" ");
    const so = (tieuDe.match(/\d+/g) || []).map(Number);
    let thang = null;
    let nam = null;
    for (const n of so) {
      if (thang === null && n >= 1 && n <= 12) { thang = n; continue; }
      if (nam === null && n >= 1900 && n <= 2999) { nam = n; }
    }
    return { tieuDe: tieuDe || null, thang, nam, soNhanHien: hien.length };
  };
  return { ok: true };
}

/**
 * Tìm và BẤM nút "Tạo" trên trang danh sách Flash Sale.
 *
 * ⚠ Luật "phần tử nào là nút Tạo" KHÔNG nằm ở đây — nó ở `window._flashNutTao`
 * ({@link pageCaiLuatNutTaoFlash}) và phải được cài TRƯỚC. Chưa cài ⇒ hỏng TO TIẾNG, không có luật dự phòng
 * tại chỗ: bản dự phòng chính là bản chép thứ hai.
 */
function pageBamNutTaoFlash() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const cls = (e) => String(e.className && e.className.baseVal !== undefined ? e.className.baseVal : (e.className || ""));
  if (typeof _flashNutTao !== "function") {
    return { ok: false, loi: "chưa cài window._flashNutTao (thiếu bước chạy pageCaiLuatNutTaoFlash)" };
  }
  const els = Array.from(document.querySelectorAll('button, a, [role="button"]')).filter(hien);
  const mo = (e) => ({ tag: e.tagName, cls: cls(e), role: e.getAttribute("role") });
  const ds = els.map((e) => {
    const toTien = [];
    let p = e.parentElement;
    for (let i = 0; i < 3 && p; i++) { toTien.push(mo(p)); p = p.parentElement; }
    return Object.assign({ text: gon(e.textContent), id: e.id || null }, mo(e), { toTien });
  });
  const chon = _flashNutTao(ds);
  if (!chon || chon.chiSo < 0 || !els[chon.chiSo]) {
    return { ok: false, soUngVien: ds.length, loi: (chon && chon.ly) || "không chọn được nút Tạo" };
  }
  const el = els[chon.chiSo];
  el.click();
  return {
    ok: true, nguonNutTao: chon.nguon, soNhan: chon.soNhan, soUngVien: ds.length,
    text: ds[chon.chiSo].text, cls: ds[chon.chiSo].cls, tag: ds[chon.chiSo].tag,
  };
}

/**
 * PURE — **"bốn con số đếm này đã đủ nói thân trang render chưa"**; trả TÊN dấu hiệu đã thoả, hoặc `null`.
 *
 * Bốn dấu hiệu phủ CẢ HAI trạng thái của trang danh sách, và chỉ cần MỘT là đủ:
 *  1. `.landing-page-header` — khung tiêu đề (trạng thái shop CHƯA có chương trình nào);
 *  2. bảng danh sách có `tbody tr` (trạng thái shop ĐÃ có chương trình);
 *  3. khối rỗng "chưa có chương trình" đang hiện;
 *  4. một phần tử bấm được có chữ ĐÚNG BẰNG "Tạo".
 *
 * ⚠ ĐÒI HAI DẤU HIỆU CÙNG LÚC LÀ HỎNG: mỗi trạng thái trang chỉ có một phần trong số đó, nên cửa "và" sẽ
 * treo đủ trần ở đúng trạng thái nó cần chạy.
 *
 * Tách THUẦN khỏi page-func là có chủ đích: page-func chạy trong trang nên không test nào lái được. Đo thật
 * 27/08/2026 — bản để phép quyết định nằm trong page-func bị bỏ mất 2 trong 4 dấu hiệu mà **cả 7 test vẫn
 * xanh**, vì test chỉ mớm sẵn tên dấu hiệu chứ không chạy phép chọn.
 */
export function nguonDauHieuThanTrang(dem) {
  const d = dem || {};
  const so = (x) => (typeof x === "number" && isFinite(x) ? x : 0);
  if (so(d.landingPageHeader) > 0) { return "landing-page-header"; }
  if (so(d.dongBangDanhSach) > 0) { return "bảng danh sách có dòng"; }
  if (so(d.khoiRong) > 0) { return "khối rỗng 'chưa có chương trình'"; }
  if (so(d.nutChuTao) > 0) { return "nút chữ 'Tạo'"; }
  return null;
}

/**
 * Đếm BỐN DẤU HIỆU "thân trang danh sách đã render" — nguyên liệu của {@link choThanTrangDanhSach}.
 *
 * Bốn dấu hiệu phủ CẢ HAI trạng thái của trang, và chỉ cần MỘT là đủ:
 *  1. `.landing-page-header` — khung tiêu đề (trạng thái shop CHƯA có chương trình nào);
 *  2. bảng danh sách có `tbody tr` (trạng thái shop ĐÃ có chương trình);
 *  3. khối rỗng "chưa có chương trình" đang hiện;
 *  4. một phần tử bấm được có chữ ĐÚNG BẰNG "Tạo".
 *
 * ⚠ Dấu hiệu 4 đi qua CHÍNH luật `window._flashNutTao` ({@link pageCaiLuatNutTaoFlash}), không chép lại phép
 * so chữ. Chưa cài luật thì dấu hiệu 4 tự tắt (`nutChuTao: null`) và ba dấu hiệu kia vẫn chạy — nói rõ trong
 * `dem` để đọc dump biết là đã tắt chứ không phải "không thấy".
 *
 * Một lượt gọi đếm CẢ BỐN (thay vì bốn lượt gọi) — vòng chờ lật tới 40 nhịp nên mỗi nhịp một round-trip là
 * ngân sách đáng kể.
 */
function pageDemDauHieuThanTrang() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const cls = (e) => String(e.className && e.className.baseVal !== undefined ? e.className.baseVal : (e.className || ""));
  const demHien = (sel) => {
    let els = [];
    try { els = Array.from(document.querySelectorAll(sel)); } catch (e) { return 0; }
    return els.filter(hien).length;
  };

  const dem = {
    landingPageHeader: demHien(".landing-page-header"),
    dongBangDanhSach: demHien("tbody tr"),
    khoiRong: demHien(".eds-table__empty, .eds-table__empty-text, .eds-empty, .no-data, .empty-content"),
    nutChuTao: null,
    soUngVienBam: null,
  };

  if (typeof _flashNutTao === "function") {
    const els = Array.from(document.querySelectorAll('button, a, [role="button"]')).filter(hien);
    const mo = (e) => ({ tag: e.tagName, cls: cls(e), role: e.getAttribute("role") });
    const ds = els.map((e) => {
      const toTien = [];
      let q = e.parentElement;
      for (let i = 0; i < 3 && q; i++) { toTien.push(mo(q)); q = q.parentElement; }
      return Object.assign({ text: gon(e.textContent), id: e.id || null }, mo(e), { toTien });
    });
    const chon = _flashNutTao(ds);
    dem.nutChuTao = chon && chon.chiSo >= 0 ? 1 : 0;
    dem.soUngVienBam = ds.length;
  }

  // ⚠ CHỈ ĐẾM, KHÔNG QUYẾT ĐỊNH. Việc "đếm này đã đủ coi là thân trang render chưa" là luật THUẦN
  // {@link nguonDauHieuThanTrang}, chạy ở service worker nên test lái được thật. Để phép quyết định trong
  // page-func thì không test nào chạm tới nó — đo được: bỏ 2 trong 4 dấu hiệu mà cả 7 test vẫn xanh.
  return { dem };
}

/**
 * ĐO lớp phủ đang-tải CỦA BẢNG SẢN PHẨM — neo trong đúng modal sản phẩm.
 *
 * ⚠ Modal khung giờ cũng có `.eds-table__loading`. `soLopPhuTrongDom` đếm TOÀN DOM để dump thấy ngay có bao
 * nhiêu cái; `coTrongModal`/`dangHien` chỉ nói về cái nằm trong modal sản phẩm.
 * "Hiện" đo bằng `getClientRects()` — lớp phủ tắt bằng `style="display: none"` nên không có rect nào.
 */
function pageDoLopPhuBangSanPham() {
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  const neo = (m) => m.querySelector('label[data-cy="product-modal-available-checkbox"], tr.mkt-product-selector-row-in-root, .eds-table__sort-icons');
  const modal = ungVien.find((m) => neo(m) && hien(m)) || ungVien.find(neo) || null;
  const toanDom = document.querySelectorAll(".eds-table__loading").length;
  if (!modal) { return { coModal: false, soLopPhuTrongDom: toanDom, coTrongModal: false, dangHien: false }; }
  const ds = Array.from(modal.querySelectorAll(".eds-table__loading"));
  return {
    coModal: true,
    soLopPhuTrongDom: toanDom,
    soLopPhuTrongModal: ds.length,
    coTrongModal: ds.length > 0,
    dangHien: ds.some(hien),
  };
}

/**
 * Vân tay bảng sản phẩm trong modal — để biết bảng còn đang chảy dòng hay đã đứng yên.
 * Neo modal như {@link pageDumpModalSanPhamFlash}; mất modal ⇒ `null` (vòng chờ không tính là lắng).
 */
function pageVanTayModalSanPham() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  const neo = (m) => m.querySelector('label[data-cy="product-modal-available-checkbox"], tr.mkt-product-selector-row-in-root, .eds-table__sort-icons');
  const modal = ungVien.find((m) => neo(m) && hien(m)) || ungVien.find(neo) || null;
  if (!modal) { return null; }
  // ⚠ ĐẾM TRONG MỘT BẢNG. Quét cả modal thì hai bảng chồng cho ra 20 trong khi thật ra 10 sp/trang
  // (số đo lượt 8) — xem {@link pageCaiLuatBangSanPham}.
  const bang = Array.from(modal.querySelectorAll("table"))
    .map((t) => ({ laBangChinh: !t.closest(".eds-table__fix-left"),
                   dong: Array.from(t.querySelectorAll("tr.mkt-product-selector-row-in-root")) }))
    .filter((x) => x.dong.length > 0);
  if (bang.length === 0) { return { soDong: 0, van: "0#" }; }
  const soDong = typeof _flashDemDongMotBang === "function"
    ? _flashDemDongMotBang(bang.map((x) => ({ laBangChinh: x.laBangChinh, soDong: x.dong.length })))
    : bang[0].dong.length;
  const mau = (bang.find((x) => x.laBangChinh) || bang[0]).dong;
  return {
    soDong,
    van: soDong + "#" + mau.slice(0, 3).map((r) => gon(r.textContent).slice(0, 80)).join("|"),
  };
}

/**
 * DUMP TRANG DANH SÁCH — chỉ chạy khi bước 2 HỎNG.
 *
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 * Đây là thứ THIẾU đã làm mỗi lần hỏng mất trọn một lượt chạy thật: lượt 10:48 và 11:34 chỉ để lại đúng
 * `selectorDung: null`, không một dòng nào nói trang lúc ấy có nút gì. Ba lượt chạy thật mới suy ra được
 * "trang render khác nhau tuỳ trạng thái" — lẽ ra một bản dump là đủ.
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 */
function pageDumpTrangDanhSach(tran) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const cls = (e) => String(e.className && e.className.baseVal !== undefined ? e.className.baseVal : (e.className || ""));
  const gioiHan = tran > 0 ? tran : 60;
  const nen = (e) => e.tagName.toLowerCase() + (cls(e) ? "." + cls(e).split(/\s+/).filter(Boolean).join(".") : "");

  const els = Array.from(document.querySelectorAll('button, a, [role="button"]')).filter(hien);
  const chiTiet = els.slice(0, gioiHan).map((e) => {
    const data = Array.from(e.attributes || [])
      .filter((a) => a.name.indexOf("data-") === 0)
      .map((a) => a.name + "=" + a.value)
      .slice(0, 6);
    const toTien = [];
    let p = e.parentElement;
    for (let i = 0; i < 3 && p; i++) { toTien.push(nen(p)); p = p.parentElement; }
    return { text: gon(e.textContent).slice(0, 60), tag: e.tagName, cls: cls(e), id: e.id || null,
             href: e.getAttribute("href"), role: e.getAttribute("role"), data, toTien };
  });

  const chuTao = Array.from(document.querySelectorAll("*"))
    .filter((e) => hien(e) && gon(e.textContent) === "Tạo")
    .slice(0, 20)
    .map((e) => ({ tag: e.tagName, cls: cls(e), toTien: (function () {
      const t = []; let p = e.parentElement;
      for (let i = 0; i < 3 && p; i++) { t.push(nen(p)); p = p.parentElement; }
      return t;
    })() }));

  return {
    url: location.href,
    soPhanTuBamDuoc: els.length,
    // ⚠ NÓI RÕ ĐÃ CẮT BAO NHIÊU — xem {@link TRAN_UNG_VIEN_DUMP}.
    daCat: Math.max(0, els.length - gioiHan),
    phanTuBamDuoc: chiTiet,
    soPhanTuChuDungBangTao: chuTao.length,
    phanTuChuDungBangTao: chuTao,
    neoCu: {
      landingPageHeader: document.querySelectorAll(".landing-page-header").length,
      landingPageBtn: document.querySelectorAll(".landing-page-btn").length,
      timeSlotBtn: document.querySelectorAll("button.time-slot-btn").length,
    },
    bangDanhSach: {
      soBang: document.querySelectorAll(".eds-table, table").length,
      soDongTbody: document.querySelectorAll("tbody tr").length,
      coKhoiRong: Array.from(document.querySelectorAll(".eds-table__empty, .eds-empty, .no-data"))
        .some((e) => hien(e)),
    },
  };
}

/**
 * DUMP toàn bộ bảng lịch đang hiện: mọi `.eds-date-table__cell` + tiêu đề tháng + số ngày ĐỎ.
 *
 * Trả lời **ẩn số #2** (một tháng có bao nhiêu ngày đỏ thật) và góp cho **ẩn số #4** (tháng sau có ngày đỏ
 * không).
 *
 * ⚠ HAI PHÉP ĐỌC KHÓ đều KHÔNG nằm ở đây mà là luật cài trên `window` — vì cả hai đã đọc SAI ở lượt
 * 10:27:07 ngày 27/08/2026, và cả hai còn được {@link pageClickNgayFlash} dùng lại:
 *  · số ngày của một ô ⇒ {@link pageCaiLuatSoNgayFlash} (`_flashSoNgay`)
 *  · tiêu đề tháng     ⇒ {@link pageCaiLuatTieuDeThangFlash} (`_flashTieuDeThang`)
 * Chép chúng vào đây là đẻ bản thứ hai của đúng hai luật vừa trả giá một lượt chạy thật.
 *
 * `htmlOMau` chụp nguyên văn MỘT ô có khung và MỘT ô không khung: lượt dò đầu đoán sai cấu trúc ô ngày (tưởng
 * số ngày nằm NGOÀI khối `timeslots`, thực ra nằm TRONG), mà trong dump không có mẫu HTML nào để đối chiếu
 * nên phải suy ngược từ chuỗi `"112 khung giờ"`. Lần sau nhìn thẳng là biết.
 */
function pageDumpLichFlash() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const dsRows = Array.from(document.querySelectorAll(".eds-date-table__rows"));
  const rows = dsRows.find(hien) || dsRows[0] || null;
  if (!rows) {
    return {
      coLich: false,
      soKhoiRows: dsRows.length,
      soOTrongDom: document.querySelectorAll(".eds-date-table__cell").length,
      soModalTrongDom: document.querySelectorAll(".eds-modal, .eds-modal__box, .eds-modal__content").length,
    };
  }
  for (const ten of ["_flashNgayDo", "_flashSoNgay", "_flashTieuDeThang"]) {
    if (typeof window[ten] !== "function") {
      return { coLich: true, loi: "chưa cài window." + ten + " (thiếu bước cài luật)" };
    }
  }

  // ⚠ CHÈN KHOẢNG TRẮNG GIỮA CÁC THẺ CON khi đọc tiêu đề — bài học của `flow-km.js/pageDocThangNamPicker`:
  // EDS tách tiêu đề thành hai thẻ `Tháng 8` + `2026`, `textContent` trần nối THẲNG thành `"Tháng 82026"`.
  const chuCoKhoang = (e) => {
    const manh = [];
    const di = (n) => {
      if (n.nodeType === 3) { manh.push(n.nodeValue || ""); return; }
      for (const c of n.childNodes) { di(c); }
    };
    for (const c of e.childNodes) { di(c); manh.push(" "); }
    return gon(manh.join(" "));
  };
  let khung = rows;
  for (let i = 0; i < 6 && khung.parentElement; i++) { khung = khung.parentElement; }
  // Label của header picker THEO ĐÚNG THỨ TỰ DOM, giữ nguyên cả cái đang ẩn — việc lọc là của luật chung.
  // Neo chính `span.eds-picker-header__label` (đo thật 27/08: ba label "Tháng 8" · "2026" · "2020 – 2029",
  // cái cuối `display:none`); lưới phụ phòng khi Shopee đổi class.
  let dsLabel = Array.from(khung.querySelectorAll("span.eds-picker-header__label"));
  let nguonLabel = "span.eds-picker-header__label";
  if (dsLabel.length === 0) {
    dsLabel = Array.from(khung.querySelectorAll('[class*="picker-header"] span, [class*="header"] [class*="label"]'));
    nguonLabel = "lưới phụ (KHÔNG thấy span.eds-picker-header__label)";
  }
  const nhanLabel = dsLabel.map((e) => ({ text: chuCoKhoang(e), hien: hien(e), cls: String(e.className || "") }));
  const tieuDe = _flashTieuDeThang(nhanLabel);

  const cells = Array.from(rows.querySelectorAll(".eds-date-table__cell"));
  const o = cells.map((c) => {
    const cls = String(c.className || "");
    const coTimeslots = !!c.querySelector("div.timeslots");
    const slots = c.querySelector("p.slots-count");
    const dateText = c.querySelector("p.date-text");
    const textDayDu = gon(c.textContent);
    const textSlots = slots ? gon(slots.textContent) : null;
    const textDateText = dateText ? gon(dateText.textContent) : null;
    const doc = _flashSoNgay(textDayDu, textSlots, textDateText);
    return {
      soNgay: doc.soNgay,
      nguonSoNgay: doc.nguon,
      textDayDu,
      dateText: textDateText,
      cls,
      coTimeslots,
      coNoTimeslot: !!c.querySelector("div.no-timeslot"),
      slots: textSlots,
      do: _flashNgayDo(cls, coTimeslots),
    };
  });

  const oDo = o.filter((x) => x.do);
  const mau = (coKhung) => {
    const el = cells.find((c) => !!c.querySelector("div.timeslots") === coKhung);
    return el ? String(el.outerHTML || "").slice(0, 500) : null;
  };
  return {
    coLich: true,
    tieuDe: tieuDe.tieuDe,
    thang: tieuDe.thang,
    nam: tieuDe.nam,
    soLabel: nhanLabel.length,
    nguonLabel,
    nhanLabel,
    soO: o.length,
    soNgayDo: oDo.length,
    ngayDo: oDo.map((x) => x.soNgay),
    slotsCuaNgayDo: oDo.map((x) => x.slots),
    htmlOMau: { coKhung: mau(true), khongKhung: mau(false) },
    o,
  };
}

/**
 * Bấm mũi tên đổi THÁNG, neo trong KHỐI LỊCH đang hiện (không phải popper).
 *
 * <p>Vì sao KHÔNG dùng thẳng `pageBamChuyenThang` của `flow-km.js`: hàm đó neo vào
 * `.eds-date-picker__picker/.eds-picker-panel/.eds-popper` và lọc bằng `offsetParent !== null` — lịch Flash
 * Sale nằm trong MODAL (`position: fixed`, `offsetParent` là null) nên hai điều kiện ấy đều trượt. Đây là
 * khác biệt về CÁCH TÌM KHỐI, không phải bản chép của một luật nghiệp vụ: luật duy nhất của lịch là "ô ngày
 * nào chọn được", và luật đó vẫn dùng chung (xem {@link pageCaiLuatNgayDoFlash}).</p>
 *
 * Giữ nguyên hai chốt phòng thủ đã trả giá bên `flow-km.js`: LOẠI nút chuyển NĂM (`year|double|super`) và
 * LOẠI mọi ứng viên NẰM TRONG bảng ngày (ô `prev-month`/`next-month` khớp đúng bộ lọc class, click trúng nó
 * là CHỌN NGÀY chứ không phải đổi tháng).
 *
 * ⚠ NHƯNG HAI CHỐT ĐÓ KHÔNG ĐỦ, và đây là chỗ đã trả giá thật ngày 27/08/2026: header EDS có BỐN mũi tên,
 * hai cặp DÙNG CHUNG CLASS `__prev`/`__next` — không có chữ `year`/`super` nào để mà lọc. Lấy phần tử đầu
 * tiên thì chiều tiến tình cờ đúng còn chiều lùi bắt trúng mũi tên NĂM (9/2026 → 9/2025).
 * ⇒ Việc chọn giao cho luật CHUNG {@link pageCaiLuatMuiTenThang} của `flow-km.js` (`_edsChonMuiTenThang`),
 * có sơ đồ 4 mũi tên trong xmldoc bên đó. KHÔNG chép luật sang đây.
 */
function pageBamMuiTenThangFlash(tien) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const cls = (e) => String(e.className && e.className.baseVal !== undefined ? e.className.baseVal : (e.className || ""));
  const dsRows = Array.from(document.querySelectorAll(".eds-date-table__rows"));
  const rows = dsRows.find(hien) || dsRows[0] || null;
  if (!rows) { return { ok: false, loi: "không thấy .eds-date-table__rows nào" }; }
  let khung = rows;
  for (let i = 0; i < 6 && khung.parentElement; i++) { khung = khung.parentElement; }

  const re = tien ? /next|right|forward/i : /prev|left|back/i;
  const moiUngVien = Array.from(khung.querySelectorAll("button, a, i, span, div"));
  const hopLe = moiUngVien.filter((e) => {
    const c = cls(e);
    if (!re.test(c)) { return false; }
    if (/year|double|super/i.test(c)) { return false; }
    if (e.closest(".eds-date-table, .eds-date-table__rows")) { return false; }
    return gon(e.textContent).length <= 4;
  });
  if (!hopLe.length) {
    return {
      ok: false,
      loi: "không thấy nút đổi tháng (" + (tien ? "tiến" : "lùi") + ") trong khối lịch",
      coSan: moiUngVien.map((e) => cls(e)).filter((c) => c).slice(0, 12),
    };
  }
  if (typeof _edsChonMuiTenThang !== "function") {
    return { ok: false, loi: "chưa cài window._edsChonMuiTenThang (thiếu bước chạy pageCaiLuatMuiTenThang)" };
  }

  const nhan = Array.from(khung.querySelectorAll(".eds-picker-header__label"));
  const nhanDau = nhan.length ? nhan[0] : null;
  const ds = hopLe.map((e, i) => ({
    chiSo: i,
    truocNhan: nhanDau
      ? !!(e.compareDocumentPosition(nhanDau) & Node.DOCUMENT_POSITION_FOLLOWING)
      : undefined,
  }));
  const chiSo = _edsChonMuiTenThang(ds, tien);
  if (chiSo < 0 || !hopLe[chiSo]) {
    return { ok: false, loi: "luật chọn mũi tên tháng không chọn được cái nào", soUngVien: hopLe.length };
  }

  let nut = hopLe[chiSo];
  const boc = nut.closest ? nut.closest("button") : null;
  if (boc) { nut = boc; }
  nut.click();
  // ⚠ GHI SỐ ỨNG VIÊN + CHỈ SỐ ĐÃ CHỌN vào dump: Shopee đổi thứ tự mũi tên là nhìn ra ngay, không phải suy
  // ngược từ "lịch đáp xuống sai tháng" như lượt 27/08.
  return { ok: true, cls: cls(nut), tag: nut.tagName, chiSoDaChon: chiSo, soUngVien: hopLe.length,
           soNhan: nhan.length, clsMoiUngVien: hopLe.map((e) => cls(e)) };
}

/**
 * Click ô ngày theo ĐÚNG SỐ NGÀY, chỉ trong khối lịch đang hiện, và CHỈ ô ĐỎ.
 *
 * ⚠ Luật "ô nào ĐỎ" KHÔNG nằm ở đây: nó ở `window._flashNgayDo` (gọi tiếp `window._edsONgayHopLe` của
 * `flow-km.js`) và phải được cài TRƯỚC. Chưa cài ⇒ hỏng TO TIẾNG, không có luật dự phòng tại chỗ.
 *
 * Khi không tìm thấy, câu trả về mang đủ thứ để chẩn đoán ngay từ nhật ký: ĐỦ danh sách ngày đỏ (không cắt —
 * bản cắt `slice(0,12)` bên KM đã làm phiên chẩn đoán 27/08 đọc ra một cái trần không hề tồn tại) và class
 * THẬT của ô mang đúng số ngày cần tìm.
 */
function pageClickNgayFlash(soNgay) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const dsRows = Array.from(document.querySelectorAll(".eds-date-table__rows"));
  const rows = dsRows.find(hien) || dsRows[0] || null;
  if (!rows) { return { ok: false, loi: "không thấy .eds-date-table__rows nào" }; }
  for (const ten of ["_flashNgayDo", "_flashSoNgay"]) {
    if (typeof window[ten] !== "function") {
      return { ok: false, loi: "chưa cài window." + ten + " (thiếu bước cài luật)" };
    }
  }
  const tatCa = Array.from(rows.querySelectorAll(".eds-date-table__cell"));
  // ⚠ ĐỌC SỐ NGÀY QUA LUẬT CHUNG `_flashSoNgay`, ĐỪNG tự bóc chuỗi ở đây. Bản trước clone ô rồi xoá khối
  // `timeslots` — số ngày nằm TRONG khối đó nên mọi ô ra rỗng, và lượt dò 10:27:07 không bấm được ô nào.
  const soCuaO = (c) => {
    const slots = c.querySelector("p.slots-count");
    const dateText = c.querySelector("p.date-text");
    const doc = _flashSoNgay(
      gon(c.textContent),
      slots ? gon(slots.textContent) : null,
      dateText ? gon(dateText.textContent) : null);
    return doc.soNgay === null ? "" : String(doc.soNgay);
  };
  const oDo = tatCa.filter((c) => _flashNgayDo(String(c.className || ""), !!c.querySelector("div.timeslots")));
  const can = String(soNgay);
  const hit = oDo.find((c) => soCuaO(c) === can);
  if (!hit) {
    return {
      ok: false,
      loi: "không thấy ô ngày '" + can + "' đang ĐỎ (tổng " + tatCa.length + " ô, " + oDo.length + " ô đỏ)",
      coSan: oDo.map((c) => soCuaO(c)),
      clsOCungSo: tatCa.filter((c) => soCuaO(c) === can).map((c) => String(c.className || "")),
    };
  }
  hit.click();
  return { ok: true, text: soCuaO(hit), cls: String(hit.className || "") };
}

/**
 * DUMP mọi KHUNG GIỜ của ngày đang chọn: `value`, nhãn, cờ qua-đêm, dòng "Số sản phẩm tham gia N".
 *
 * `.cross-day-tag` và `.products .available` không nằm cố định ở cấp nào so với `input` (DOM dump chỉ cho thấy
 * chúng cùng một hàng), nên hàm LEO LÊN tối đa 6 cấp và ghi lại `capDoTim` — lượt dò phải nói ra được cấu trúc
 * thật, không phải đoán rồi im lặng trả null.
 */
function pageDumpKhungGioFlash() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const inputs = Array.from(document.querySelectorAll("input.eds-radio__input"));
  const khung = inputs.map((inp, i) => {
    let nhan = null;
    let crossDay = null;
    let sanPham = null;
    let capDoTim = null;
    let el = inp;
    for (let cap = 0; cap < 6 && el; cap++) {
      const l = el.querySelector ? el.querySelector("span.eds-radio__label") : null;
      const cd = el.querySelector ? el.querySelector("span.cross-day-tag") : null;
      const sp = el.querySelector ? el.querySelector(".products .available") : null;
      if (l && nhan === null) { nhan = gon(l.textContent); capDoTim = cap; }
      if (cd && crossDay === null) { crossDay = gon(cd.textContent); }
      if (sp && sanPham === null) { sanPham = gon(sp.textContent); }
      if (nhan !== null && sanPham !== null) { break; }
      el = el.parentElement;
    }
    return {
      chiSo: i,
      value: inp.value === undefined ? null : String(inp.value),
      nhan,
      coCrossDayTag: crossDay !== null,
      crossDayTag: crossDay,
      sanPham,
      capDoTim,
      disabled: !!inp.disabled,
      checked: !!inp.checked,
      cls: String(inp.className || ""),
    };
  });
  return { soKhung: khung.length, khung };
}

/**
 * Cài BA LUẬT của bảng sản phẩm lên `window`. Gom một chỗ vì cả ba đều sinh ra từ CÙNG một phát hiện:
 * **modal có HAI bảng chồng nhau** (`.eds-table__fix-left` giữ checkbox + tên; bảng chính giữ Doanh số / Giá
 * / Kho), và cả hai dùng CHUNG lớp `tr.mkt-product-selector-row-in-root`.
 *
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 * SỐ ĐO THẬT lượt 8 (27/08/2026) — ba lỗi, một nguyên nhân:
 *  · `soDongMoiTrang: 20` trong khi `maSanPham` = 10 mã thật + 10 `null` ⇒ **đếm ĐÔI**. Thật ra 10 sp/trang,
 *    khớp "Số sản phẩm tham gia 10".
 *  · Doanh số đọc ra `[]` ⇒ cột nằm ở bảng CHÍNH, mà chỉ số cột lại tính theo `th` toàn cục.
 *  · `bam1.cls = bam2.cls = "eds-table__cell-action eds-table__cell-icon eds-table__sort-icons"` ⇒ đã bấm
 *    vào `<div>` BAO chứ không vào `<i>`, nên `T0 = T1 = T2`, không cú bấm nào được ghi nhận.
 *
 * DOM thật của cụm sắp xếp:
 *     <div class="eds-table__cell-action eds-table__cell-icon eds-table__sort-icons">   ← thẻ BAO, đừng bấm
 *       <i class="eds-icon eds-table__sort-icon up">…</i>                               ← bấm CÁI NÀY
 *       <i class="eds-icon eds-table__sort-icon down">…</i>
 *     </div>
 * Lưu ý `sort-icons` (số nhiều, thẻ bao) vs `sort-icon` (số ít, thẻ bấm được) — khớp theo TOKEN, khớp chuỗi
 * con là dính cả hai.
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 */
export function pageCaiLuatBangSanPham() {
  const token = function (cls) {
    return String(cls === null || cls === undefined ? "" : cls).split(/\s+/).filter(Boolean);
  };

  /** Chọn thẻ `<i>` mũi tên sắp xếp theo CHIỀU. Trả chỉ số trong `dsCon`, hoặc -1. */
  window._flashChonNutSapXep = function (dsCon, chieu) {
    const ds = Array.isArray(dsCon) ? dsCon : [];
    const can = String(chieu || "").toLowerCase();
    for (let i = 0; i < ds.length; i++) {
      const t = token(ds[i] && ds[i].cls);
      // `sort-icon` SỐ ÍT — thẻ bao mang `sort-icons` SỐ NHIỀU và phải bị loại.
      if (t.indexOf("eds-table__sort-icon") < 0) { continue; }
      if (t.indexOf(can) < 0) { continue; }
      return i;
    }
    return -1;
  };

  /** Số dòng của MỘT bảng (không phải tổng hai bảng chồng). */
  window._flashDemDongMotBang = function (dsBang) {
    const ds = (Array.isArray(dsBang) ? dsBang : []).filter((b) => b && typeof b.soDong === "number");
    if (ds.length === 0) { return 0; }
    const chinh = ds.filter((b) => b.laBangChinh === true);
    if (chinh.length > 0) { return chinh[0].soDong; }
    // Không biết bảng nào là chính ⇒ lấy LỚN NHẤT, tuyệt đối không CỘNG.
    return ds.reduce(function (a, b) { return b.soDong > a ? b.soDong : a; }, 0);
  };

  /** Chỉ số cột "Doanh số" TRONG MỘT bảng, tính theo danh sách nhãn `th` của chính bảng đó. */
  window._flashChiSoCotDoanhSo = function (dsNhan) {
    const ds = Array.isArray(dsNhan) ? dsNhan : [];
    for (let i = 0; i < ds.length; i++) {
      if (String(ds[i] === null || ds[i] === undefined ? "" : ds[i]).replace(/\s+/g, " ").trim() === "Doanh số") {
        return i;
      }
    }
    return -1;
  };
  return { ok: true };
}

/**
 * ĐO bảng khung giờ của ngày đang chọn — nguyên liệu của {@link choKhungGioLang}.
 *
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 * ⚠ ĐẾM THEO SỰ TỒN TẠI, KHÔNG THEO KÍCH THƯỚC — đây là nửa thứ hai của lỗi lượt 7 (27/08/2026).
 *
 * Bước 5 cũ đọc lại bằng `choHien(chay, "input.eds-radio__input")`, mà `pageDemPhanTuHien` lọc bằng
 * `getBoundingClientRect().width > 0 || height > 0`. EDS **ẩn hẳn thẻ `<input>` gốc** và vẽ một `<span>` thay
 * cho nó, nên ô input có kích thước 0 ⇒ phép đếm ấy trả **0 vĩnh viễn**, chờ bao lâu cũng vô ích. Số đo khớp:
 * bước 5 tốn **31 giây** — đúng bằng trọn 60 nhịp × 500ms của `choHien` rồi vẫn `soRadio: 0`, trong khi cú
 * click ngày `ok:true` và `anSo.ngayDaChon` ghi đúng ngày 28.
 *
 * ⇒ `soRadio` (không lọc) là con số DÙNG ĐỂ QUYẾT ĐỊNH; `soRadioHien` chỉ ghi kèm làm bằng chứng, và chính
 * cặp số này sẽ xác nhận dứt điểm giả thuyết "EDS ẩn input" ở lượt tới.
 * ⚠ Mọi chỗ khác trong file thao tác radio (`pageTickKhungFlash`, `pageDumpKhungGioFlash`) vốn ĐÃ đếm theo
 * sự tồn tại — bước 5 là chỗ DUY NHẤT lệch, và nó lệch vì mượn `choHien` cho một loại phần tử không hợp.
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 */
function pageDemKhungGioFlash() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const inputs = Array.from(document.querySelectorAll("input.eds-radio__input"));
  const nhan = Array.from(document.querySelectorAll("span.eds-radio__label"));
  return {
    soRadio: inputs.length,
    soRadioHien: inputs.filter(hien).length,
    soNhan: nhan.length,
    // Vân tay: số radio + nhãn 3 khung đầu. Đổi ngày làm bảng vẽ lại, và số radio có thể nhảy giữa chừng —
    // chỉ đếm số thì lượt vẽ lại "thay ruột mà giữ nguyên số" sẽ lọt.
    van: inputs.length + "#" + nhan.slice(0, 3).map((e) => gon(e.textContent)).join("|"),
  };
}

/** Tick khung giờ thứ `chiSo` (0 = khung ĐẦU TIÊN). Đọc lại `checked` ngay để bước gọi tự chấm. */
function pageTickKhungFlash(chiSo) {
  const inputs = Array.from(document.querySelectorAll("input.eds-radio__input"));
  const inp = inputs[chiSo];
  if (!inp) { return { ok: false, loi: "không thấy radio khung giờ thứ " + chiSo + " (có " + inputs.length + ")" }; }
  if (inp.disabled) { return { ok: false, loi: "radio khung giờ thứ " + chiSo + " đang disabled" }; }
  if (!inp.checked) { inp.click(); }
  return { ok: true, value: inp.value === undefined ? null : String(inp.value), checked: !!inp.checked };
}

/**
 * Bấm "Xác nhận" **CỦA MODAL KHUNG GIỜ** — chỉ điền giá trị vào form, KHÔNG tạo chương trình nào.
 *
 * ⚠ HAI CHỐT, CẦN CẢ HAI, ĐỪNG GỠ CÁI NÀO (xem khối ràng buộc đầu file):
 *  (1) neo cứng vào hộp modal ĐANG chứa CẢ bảng ngày LẪN radio khung giờ — đó là dấu hiệu chắc chắn của modal
 *      "Chọn khung giờ Flash sale Của Shop". Không thấy ⇒ TRẢ LỖI, tuyệt đối không nới phạm vi tìm ra ngoài.
 *  (2) từ chối khi nút tìm được nằm trong `.fix-container`: lớp `confirm-btn` DÙNG CHUNG với nút "Xác nhận"
 *      ở thanh đáy trang, mà nút thanh đáy là nút TẠO CHƯƠNG TRÌNH THẬT (`flow-km.js` bước 9).
 */
function pageBamXacNhanKhungFlash() {
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  const laModalKhung = (m) => m.querySelector(".eds-date-table__cell") && m.querySelector("input.eds-radio__input");
  const modal = ungVien.find((m) => laModalKhung(m) && hien(m)) || ungVien.find(laModalKhung) || null;
  if (!modal) {
    return {
      ok: false,
      loi: "không thấy modal khung giờ (neo: bảng ngày + radio) — KHÔNG tìm nút Xác nhận ngoài modal",
      soModalTrongDom: ungVien.length,
    };
  }
  const nut = modal.querySelector("button.confirm-btn");
  if (!nut) {
    return { ok: false, loi: "không thấy button.confirm-btn TRONG modal khung giờ" };
  }
  if (nut.closest(".fix-container") || nut.closest(".fixed-container")) {
    return { ok: false, loi: "nút confirm-btn tìm được nằm ở THANH ĐÁY (.fix-container/.fixed-container) — TỪ CHỐI bấm, đó là nút TẠO THẬT" };
  }
  if (nut.disabled) { return { ok: false, loi: "nút Xác nhận của modal khung giờ đang disabled" }; }
  nut.click();
  return { ok: true, cls: String(nut.className || "") };
}

/**
 * Bản TOẠ ĐỘ của {@link pageBamXacNhanKhungFlash} — cho LUỒNG THẬT bấm bằng chuột thật qua chrome.debugger.
 *
 * ⚠ VÌ SAO (đo 15:12–15:16 31/08/2026): cú Xác nhận modal khung giờ bằng click DOM ĐÓNG modal + điền chữ
 * vào nút time-slot (has-value) nhưng state "khung giờ đã chọn" của form KHÔNG nhận — tooltip của MỌI
 * toggle Bật/Tắt ghi "Vui lòng lựa chọn khung giờ" và mọi cú bật (kể cả chuột thật) bị từ chối im lặng.
 * Cùng họ lỗi ĐÓNG-mà-KHÔNG-LƯU với modal sản phẩm. Lượt DÒ giữ bản click cũ — nó kết thúc bằng Hủy.
 * Chốt an toàn GIỮ NGUYÊN: từ chối nút nằm trong .fix-container/.fixed-container (TỪ CHỐI thanh đáy).
 */
function pageToaDoNutXacNhanKhung() {
  const hien = (e) => { if (!e) { return false; } const r = e.getBoundingClientRect(); return r.width > 2 && r.height > 2; };
  const ungVien = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  const laModalKhung = (m) => m.querySelector(".eds-date-table__cell") && m.querySelector("input.eds-radio__input");
  const modal = ungVien.find((m) => laModalKhung(m) && hien(m)) || null;
  if (!modal) { return { ok: false, loi: "không thấy modal khung giờ ĐANG HIỆN (neo: bảng ngày + radio)" }; }
  const nut = modal.querySelector("button.confirm-btn");
  if (!nut) { return { ok: false, loi: "không thấy button.confirm-btn TRONG modal khung giờ" }; }
  if (nut.closest(".fix-container") || nut.closest(".fixed-container")) {
    return { ok: false, loi: "nút confirm-btn tìm được nằm ở THANH ĐÁY (.fix-container/.fixed-container) — TỪ CHỐI, đó là nút TẠO THẬT" };
  }
  if (nut.disabled) { return { ok: false, loi: "nút Xác nhận của modal khung giờ đang disabled" }; }
  if (!hien(nut)) { return { ok: false, loi: "nút Xác nhận modal khung giờ không hiện — không có toạ độ để bấm chuột thật" }; }
  const r = nut.getBoundingClientRect();
  return { ok: true, x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2),
           cls: String(nut.className || "") };
}

/**
 * ĐỌC trạng thái cụm sắp xếp của cột **Doanh số** — nguồn của **ẩn số #1**.
 *
 * Trả `className` của CỤM `.eds-table__sort-icons` và của CẢ HAI `.eds-table__sort-icon` con. Ba lượt đọc
 * (T0 → bấm → T1 → bấm → T2) là toàn bộ bằng chứng để sau này viết cửa kiểm "đang là giảm dần".
 * <p><b>Cửa kiểm đặt ở CHÍNH CÁI NÚT, không ở dữ liệu</b> (plan §3.1): trong dump 27/08 cả 10 dòng đều có
 * Doanh số = 0, nên phép so `dòng1 >= dòng10` ĐẠT bất kể mã làm gì — một proof đo RỖNG.</p>
 */
function pageDocMocSapXepFlash() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const ths = Array.from(document.querySelectorAll("th"));
  const th = ths.find((t) => {
    const l = t.querySelector(".eds-table__cell-label");
    return l && gon(l.textContent) === "Doanh số";
  });
  if (!th) {
    return {
      coCot: false,
      soTh: ths.length,
      nhanCacCot: ths.map((t) => {
        const l = t.querySelector(".eds-table__cell-label");
        return l ? gon(l.textContent) : gon(t.textContent).slice(0, 24);
      }).slice(0, 20),
    };
  }
  const cum = th.querySelector(".eds-table__sort-icons");
  const icons = Array.from(th.querySelectorAll(".eds-table__sort-icon"));
  return {
    coCot: true,
    coCum: !!cum,
    clsCum: cum ? String(cum.className || "") : null,
    soIcon: icons.length,
    clsIcon: icons.map((e) => String(e.className && e.className.baseVal !== undefined ? e.className.baseVal : (e.className || ""))),
    ariaSort: th.getAttribute("aria-sort"),
    clsTh: String(th.className || ""),
  };
}

/**
 * Bấm mũi tên sắp xếp của cột **Doanh số** theo CHIỀU (`"up"` / `"down"`).
 *
 * ⚠ BẤM VÀO `<i>`, KHÔNG BẤM `<div>` BAO. Lượt 8 (27/08/2026) bấm trúng thẻ bao
 * `eds-table__sort-icons` (số nhiều) nên `T0 = T1 = T2` — không cú bấm nào được ghi nhận, mà hàm vẫn trả
 * `ok:true`. "Có phần tử để bấm" KHÔNG bằng "bấm có tác dụng".
 * ⚠ Việc CHỌN thẻ nào là luật chung `window._flashChonNutSapXep` ({@link pageCaiLuatBangSanPham}) — đừng
 * chép phép so class vào đây.
 */
function pageBamSapXepDoanhSo(chieu) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const cls = (e) => String(e.className && e.className.baseVal !== undefined ? e.className.baseVal : (e.className || ""));
  if (typeof _flashChonNutSapXep !== "function") {
    return { ok: false, loi: "chưa cài window._flashChonNutSapXep (thiếu bước chạy pageCaiLuatBangSanPham)" };
  }
  const th = Array.from(document.querySelectorAll("th"))
    .filter((t) => !t.closest(".eds-table__fix-left"))
    .find((t) => {
      const l = t.querySelector(".eds-table__cell-label");
      return l && gon(l.textContent) === "Doanh số";
    });
  if (!th) { return { ok: false, loi: "không thấy th có nhãn 'Doanh số' ngoài bảng trái" }; }
  const cum = th.querySelector(".eds-table__sort-icons");
  if (!cum) { return { ok: false, loi: "th 'Doanh số' không có .eds-table__sort-icons" }; }

  const con = Array.from(cum.children).map((e) => ({ tag: e.tagName, cls: cls(e) }));
  const i = _flashChonNutSapXep(con, chieu);
  if (i < 0 || !cum.children[i]) {
    return { ok: false, loi: "không thấy mũi tên sắp xếp chiều '" + chieu + "' trong cụm",
             clsCum: cls(cum), clsCon: con.map((c) => c.cls) };
  }
  const nut = cum.children[i];
  nut.click();
  return { ok: true, chieu, cls: cls(nut), tag: nut.tagName, chiSoDaChon: i,
           clsCum: cls(cum), clsCon: con.map((c) => c.cls) };
}

/**
 * Đọc DANH SÁCH MÃ SẢN PHẨM của trang hiện tại, theo ĐÚNG thứ tự bảng — **trọng tài của phép sắp xếp**.
 *
 * Vì sao mã sản phẩm mạnh hơn class trạng thái của nút: thứ tự đổi hay không là quan sát TRỰC TIẾP, không
 * phụ thuộc EDS có gắn class trạng thái hay không, và không chết ở ca shop toàn 0 doanh số (mọi giá trị bằng
 * nhau thì chiều nào cũng như nhau, nên "không đổi" lúc đó cũng là câu trả lời đúng).
 *
 * ⚠ Chỉ đọc trong MỘT bảng — xem {@link pageCaiLuatBangSanPham} cho ca đếm đôi 20/10.
 */
function pageDocMaSanPhamFlash() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  const neo = (m) => m.querySelector('label[data-cy="product-modal-available-checkbox"], tr.mkt-product-selector-row-in-root, .eds-table__sort-icons');
  const modal = ungVien.find((m) => neo(m) && hien(m)) || ungVien.find(neo) || null;
  if (!modal) { return { coModal: false, ma: [] }; }

  // Bảng TRÁI giữ tên + mã sản phẩm. Lấy bảng ĐẦU TIÊN có dòng, ưu tiên bảng trái vì mã nằm ở đó.
  const bang = Array.from(modal.querySelectorAll("table"))
    .map((t) => ({ t, dong: Array.from(t.querySelectorAll("tr.mkt-product-selector-row-in-root")) }))
    .filter((x) => x.dong.length > 0);
  if (bang.length === 0) { return { coModal: true, soBangCoDong: 0, ma: [] }; }
  const coMa = (x) => x.dong.some((r) => Array.from(r.querySelectorAll("p")).some((q) => /Mã\s*:/i.test(q.textContent || "")));
  const chon = bang.find(coMa) || bang[0];
  return {
    coModal: true,
    soBangCoDong: bang.length,
    soDong: chon.dong.length,
    trongBangTrai: !!chon.t.closest(".eds-table__fix-left"),
    ma: chon.dong.map((r) => {
      const q = Array.from(r.querySelectorAll("p")).find((x) => /Mã\s*:/i.test(x.textContent || ""));
      return q ? gon(q.textContent) : null;
    }),
  };
}

/**
 * Đọc giá trị cột **Doanh số** của MỌI dòng — CHỨNG CỨ ĐỌC THÊM, không phải cửa kiểm.
 *
 * ⚠ Không một quyết định nào của luồng được phép dựa vào các con số này (plan §3.1): shop toàn 0 doanh số là
 * chuyện thường, mà `0 >= 0` đúng ở cả hai chiều sắp xếp.
 */
function pageDocDoanhSoMoiDongFlash() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  if (typeof _flashChiSoCotDoanhSo !== "function") {
    return { coCot: false, giaTri: [], loi: "chưa cài window._flashChiSoCotDoanhSo" };
  }
  const ungVien = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  const neo = (m) => m.querySelector('label[data-cy="product-modal-available-checkbox"], tr.mkt-product-selector-row-in-root, .eds-table__sort-icons');
  const modal = ungVien.find((m) => neo(m) && hien(m)) || ungVien.find(neo) || null;
  if (!modal) { return { coCot: false, giaTri: [], loi: "không thấy modal" }; }

  // ⚠ LOẠI BẢNG TRÁI. Modal có HAI bảng chồng dùng chung `tr.mkt-product-selector-row-in-root`; Doanh số chỉ
  // có ở bảng CHÍNH. Lượt 8 đọc ra `[]` vì lấy `th` toàn cục rồi tìm dòng trong bảng của chính `th` đó — mà
  // EDS tách HEADER thành `<table>` riêng, nên bảng ấy có 0 dòng.
  const thNgoaiTrai = Array.from(modal.querySelectorAll("th")).filter((t) => !t.closest(".eds-table__fix-left"));
  const th = thNgoaiTrai.find((t) => {
    const l = t.querySelector(".eds-table__cell-label");
    return l && gon(l.textContent) === "Doanh số";
  });
  const nhanCacCot = thNgoaiTrai.map((t) => {
    const l = t.querySelector(".eds-table__cell-label");
    return l ? gon(l.textContent) : gon(t.textContent).slice(0, 24);
  });
  if (!th) { return { coCot: false, giaTri: [], nhanCacCot }; }

  // Chỉ số cột tính theo `th` CỦA CHÍNH BẢNG chứa nó (header có thể là <table> riêng) — luật chung.
  const bangTh = th.closest("table");
  const nhanTh = bangTh
    ? Array.from(bangTh.querySelectorAll("th")).map((t) => {
        const l = t.querySelector(".eds-table__cell-label");
        return l ? gon(l.textContent) : gon(t.textContent).slice(0, 24);
      })
    : nhanCacCot;
  const cot = _flashChiSoCotDoanhSo(nhanTh);
  if (cot < 0) { return { coCot: false, giaTri: [], nhanCacCot, nhanTh }; }

  // Bảng CÓ DÒNG và KHÔNG phải bảng trái — đây mới là bảng mang giá trị.
  const bangCoDong = Array.from(modal.querySelectorAll("table"))
    .filter((t) => !t.closest(".eds-table__fix-left"))
    .map((t) => ({ t, dong: Array.from(t.querySelectorAll("tr.mkt-product-selector-row-in-root")) }))
    .filter((x) => x.dong.length > 0);
  if (bangCoDong.length === 0) {
    return { coCot: true, chiSoCot: cot, soDong: 0, giaTri: [], nhanTh,
             loi: "không thấy bảng nào (ngoài bảng trái) có dòng sản phẩm" };
  }
  const dong = bangCoDong[0].dong;
  return {
    coCot: true,
    chiSoCot: cot,
    soDong: dong.length,
    soBangCoDong: bangCoDong.length,
    headerLaBangRieng: bangTh !== bangCoDong[0].t,
    nhanTh,
    giaTri: dong.map((r) => {
      const td = r.children[cot];
      return td ? gon(td.textContent) : null;
    }),
  };
}

/**
 * DUMP modal "Chọn Sản Phẩm": ô "Xem sản phẩm có sẵn" (**ẩn số #3**), số dòng mỗi trang, pager, ô chọn-tất-cả.
 *
 * ⚠ Modal này là họ `eds-*` (Vue), KHÔNG phải `eds-react-*` như modal của Combo — chép selector từ
 * `flow-combo.js` sang là hỏng ngay (plan §2, cảnh báo in đậm).
 */
function pageDumpModalSanPhamFlash() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  const neo = (m) => m.querySelector('label[data-cy="product-modal-available-checkbox"], tr.mkt-product-selector-row-in-root, .eds-table__sort-icons');
  const modal = ungVien.find((m) => neo(m) && hien(m)) || ungVien.find(neo) || null;
  if (!modal) {
    return {
      coModal: false,
      soModalTrongDom: ungVien.length,
      soNeo: {
        oCoSan: document.querySelectorAll('label[data-cy="product-modal-available-checkbox"]').length,
        dong: document.querySelectorAll("tr.mkt-product-selector-row-in-root").length,
        cumSapXep: document.querySelectorAll(".eds-table__sort-icons").length,
      },
    };
  }

  const nhanCoSan = modal.querySelector('label[data-cy="product-modal-available-checkbox"]');
  const inpCoSan = nhanCoSan ? nhanCoSan.querySelector("input") : null;
  const cotTrai = modal.querySelector(".eds-table__fix-left");
  const thDau = cotTrai ? cotTrai.querySelector("th") : null;
  const inpTatCa = thDau ? thDau.querySelector("input.eds-checkbox__input, input[type=checkbox]") : null;
  const trang = Array.from(modal.querySelectorAll("li.eds-pager__page"));
  const nutSau = modal.querySelector(".eds-pager__button-next");
  // ⚠ ĐẾM TRONG MỘT BẢNG (xem {@link pageCaiLuatBangSanPham}): lượt 8 ghi `soDongMoiTrang: 20` trong khi
  // `maSanPham` chỉ có 10 mã thật + 10 `null` — hai bảng chồng bị cộng lại.
  const bangCoDong = Array.from(modal.querySelectorAll("table"))
    .map((t) => ({ laBangChinh: !t.closest(".eds-table__fix-left"),
                   dong: Array.from(t.querySelectorAll("tr.mkt-product-selector-row-in-root")) }))
    .filter((x) => x.dong.length > 0);
  const soDongMotBang = typeof _flashDemDongMotBang === "function"
    ? _flashDemDongMotBang(bangCoDong.map((x) => ({ laBangChinh: x.laBangChinh, soDong: x.dong.length })))
    : (bangCoDong[0] ? bangCoDong[0].dong.length : 0);
  const coMa = (x) => x.dong.some((r) => Array.from(r.querySelectorAll("p")).some((q) => /Mã\s*:/i.test(q.textContent || "")));
  const dong = (bangCoDong.find(coMa) || bangCoDong[0] || { dong: [] }).dong;

  return {
    coModal: true,
    soBangTrongModal: modal.querySelectorAll("table").length,
    soBangCoDong: bangCoDong.length,
    soDongTungBang: bangCoDong.map((x) => ({ laBangChinh: x.laBangChinh, soDong: x.dong.length })),
    oXemSanPhamCoSan: {
      coNhan: !!nhanCoSan,
      coInput: !!inpCoSan,
      checked: inpCoSan ? !!inpCoSan.checked : null,
      disabled: inpCoSan ? !!inpCoSan.disabled : null,
      clsNhan: nhanCoSan ? String(nhanCoSan.className || "") : null,
      text: nhanCoSan ? gon(nhanCoSan.textContent) : null,
    },
    oChonTatCa: {
      coCotTrai: !!cotTrai,
      coThDau: !!thDau,
      coInput: !!inpTatCa,
      clsInput: inpTatCa ? String(inpTatCa.className || "") : null,
      checked: inpTatCa ? !!inpTatCa.checked : null,
      clsTh: thDau ? String(thDau.className || "") : null,
    },
    soDongMoiTrang: soDongMotBang,
    maSanPham: dong.map((r) => {
      const p = Array.from(r.querySelectorAll("p")).find((x) => /Mã\s*:/i.test(x.textContent || ""));
      return p ? gon(p.textContent) : null;
    }),
    phanTrang: {
      soTrangHienThi: trang.length,
      nhanCacTrang: trang.map((t) => gon(t.textContent)),
      coNutSau: !!nutSau,
      nutSauCls: nutSau ? String(nutSau.className || "") : null,
      nutSauDisabled: nutSau
        ? (!!nutSau.disabled || /disabled/.test(String(nutSau.className || "")) || nutSau.getAttribute("aria-disabled") === "true")
        : null,
    },
  };
}

/**
 * **HỦY** ra khỏi modal chọn sản phẩm — lối ra DUY NHẤT của lượt dò.
 *
 * Chỉ nhận nút có chữ đúng bằng "hủy"/"huỷ" trong footer modal; không thấy thì thử nút đóng `×`. TUYỆT ĐỐI
 * không rơi xuống "nút footer đầu tiên": nút cạnh Hủy chính là "Xác nhận" — bấm nó là nạp sản phẩm vào form,
 * đúng thứ lượt dò không được làm.
 */
function pageHuyModalSanPham() {
  const na = (s) => (s || "").replace(/\s+/g, " ").trim().toLowerCase();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  const neo = (m) => m.querySelector('label[data-cy="product-modal-available-checkbox"], tr.mkt-product-selector-row-in-root, .eds-table__sort-icons');
  const modal = ungVien.find((m) => neo(m) && hien(m)) || ungVien.find(neo) || null;
  if (!modal) { return { ok: false, loi: "không thấy modal chọn sản phẩm để hủy", soModalTrongDom: ungVien.length }; }

  const nut = Array.from(modal.querySelectorAll("button")).find((b) => na(b.textContent) === "hủy" || na(b.textContent) === "huỷ");
  if (nut) {
    nut.click();
    return { ok: true, duong: "nút Hủy", cls: String(nut.className || "") };
  }
  const dong = modal.querySelector(".eds-modal__close, .eds-modal__close-icon");
  if (dong) {
    dong.click();
    return { ok: true, duong: "nút đóng ×", cls: String(dong.className || "") };
  }
  return {
    ok: false,
    loi: "không thấy nút Hủy hay nút đóng trong modal — KHÔNG bấm nút nào khác",
    coSan: Array.from(modal.querySelectorAll("button")).map((b) => na(b.textContent)).slice(0, 8),
  };
}

// ── Chuỗi bước của lượt dò ──────────────────────────────────────────────────────────────────────────────────

/**
 * Chờ `sel` có ít nhất một phần tử ĐANG HIỆN. Trả số đếm cuối cùng (0 = hết nhịp mà vẫn không thấy).
 * Không sleep mù: mốc "trang đã lắng" đáng tin nhất là chính phần tử sắp thao tác đã render.
 */
async function choHien(chay, sel, nhip) {
  return (await choHienCoNhip(chay, sel, nhip)).soThay;
}

/**
 * Như {@link choHien} nhưng TRẢ THÊM số nhịp đã chờ — để bằng chứng "đã chờ bao lâu" đi được vào dump.
 * <para>Người đọc dump phải phân biệt được "chốt ngay nhịp đầu" và "phải chờ 12 nhịp mới thấy": cái thứ hai
 * là dấu hiệu trang đang chậm dần, và nó báo trước ca sắp chạm trần.</para>
 */
async function choHienCoNhip(chay, sel, nhip) {
  let n = 0;
  const tran = nhip > 0 ? nhip : CHO_NHIP;
  for (let i = 0; i < tran; i++) {
    n = await chay(pageDemPhanTuHien, [sel]);
    if (n > 0) { return { soThay: n, soNhipCho: i + 1 }; }
    await sleep(500);
  }
  return { soThay: n, soNhipCho: tran };
}

/**
 * **CHỜ THÂN TRANG DANH SÁCH RENDER** — trị gốc của lỗi bước 2 hỏng câm (xem {@link THAN_TRANG_NHIP_MS}).
 *
 * Thoát ngay khi MỘT trong bốn dấu hiệu của {@link pageDemDauHieuThanTrang} xuất hiện. Bốn dấu hiệu phủ cả
 * hai trạng thái trang (shop chưa có chương trình / đã có), nên vòng này KHÔNG giả định trạng thái nào.
 *
 * Hết trần ⇒ `thay:false`; bước gọi HỎNG với câu nói rõ "thân trang chưa render sau N nhịp" **và vẫn dump
 * trang** — đúng thứ đã cho phép chốt nguyên nhân trong một lượt thay vì ba.
 *
 * @returns {Promise<{thay:boolean, soNhipCho:number, nguonDauHieu:string|null, dem:object|null}>}
 */
async function choThanTrangDanhSach(chay, nhip) {
  const tran = nhip > 0 ? nhip : THAN_TRANG_TRAN_NHIP;
  let doc = null;
  for (let i = 0; i < tran; i++) {
    doc = await chay(pageDemDauHieuThanTrang, []);
    const nguon = nguonDauHieuThanTrang(doc ? doc.dem : null);
    if (nguon) {
      return { thay: true, soNhipCho: i + 1, nguonDauHieu: nguon, dem: doc.dem };
    }
    await sleep(THAN_TRANG_NHIP_MS);
  }
  return { thay: false, soNhipCho: tran, nguonDauHieu: null, dem: doc ? doc.dem : null };
}

/**
 * **CHỜ BẢNG SẢN PHẨM TRONG MODAL LẮNG** — cùng HÌNH DẠNG và dùng chung hằng/luật so với
 * {@link choLichLang} (`vanTayGiongNhau`, `LANG_CAN_GIONG`, `LANG_TRAN_NHIP`,
 * `LANG_NHIP_TOI_THIEU_KHI_RONG`), chỉ khác nguồn vân tay.
 *
 * Vì sao cần dù bước 8 đã chờ `tr.mkt-product-selector-row-in-root` XUẤT HIỆN: "có dòng đầu tiên" ≠ "bảng đã
 * đủ hàng". Bước 9 đọc mốc `T0` của cụm sắp xếp và giá trị Doanh số của MỌI dòng ngay sau đó — đọc lúc bảng
 * còn chảy thì `T0` và dãy Doanh số đều là ảnh của một trạng thái dở dang, mà `T0` lại là mốc gốc để sau này
 * viết cửa kiểm "đang là giảm dần". Đúng bài học `flow-combo.js` (tick khi bảng chưa lắng ⇒ thiếu hàng).
 */
async function choBangSanPhamLang(chay) {
  let vanTruoc = null;
  let doc = null;
  let giong = 1;
  let soNhip = 0;
  for (let i = 0; i < LANG_TRAN_NHIP; i++) {
    const d = await chay(pageVanTayModalSanPham, []);
    soNhip = i + 1;
    if (d) { doc = d; }
    const van = d ? d.van : null;
    giong = vanTayGiongNhau(van, vanTruoc) ? giong + 1 : 1;
    if (van !== null && giong >= LANG_CAN_GIONG) {
      // Bảng 0 dòng khó tính hơn: "chưa nạp xong" và "modal thật sự rỗng" nhìn giống hệt nhau ở nhịp đầu.
      if (d.soDong > 0 || soNhip >= LANG_NHIP_TOI_THIEU_KHI_RONG) {
        return { lang: true, soNhip, soDong: d.soDong, van };
      }
    }
    vanTruoc = van;
    await sleep(LANG_NHIP_MS);
  }
  return { lang: false, soNhip, soDong: doc ? doc.soDong : null, van: vanTruoc };
}

/** PURE — nhãn người đọc được của tháng một bản dump lịch đang đứng ("Tháng 9/2026", hoặc tiêu đề thô). */
export function moTaThang(lich) {
  if (!lich) { return "(không đọc được lịch)"; }
  if (lich.thang && lich.nam) { return "Tháng " + lich.thang + "/" + lich.nam; }
  return lich.tieuDe ? String(lich.tieuDe) : "(không đọc được tháng)";
}

/**
 * PURE — **"hai bản dump lịch này có đang ở HAI tháng khác nhau không"**.
 *
 * ⚠ So bằng CẶP SỐ `thang`+`nam`, KHÔNG so chuỗi tiêu đề. Lượt 10:27:07 ngày 27/08/2026 so chuỗi và cả hai
 * tháng đều đọc ra `"2026"` (xem {@link pageCaiLuatTieuDeThangFlash}) ⇒ cờ "đã đổi tháng" báo `false` trong
 * khi picker đã chuyển thật. Thiếu `thang` hoặc `nam` ở một trong hai bên ⇒ trả `false`: KHÔNG ĐOÁN. Bước gọi
 * sẽ hỏng có tên, còn hơn đi tiếp trên một cái tháng không ai biết là tháng nào.
 */
export function daDoiThang(truoc, sau) {
  if (!truoc || !sau) { return false; }
  if (!truoc.thang || !truoc.nam || !sau.thang || !sau.nam) { return false; }
  return truoc.thang !== sau.thang || truoc.nam !== sau.nam;
}

/** PURE — rút gọn một bản dump lịch xuống phần đáng đưa vào `anSo` (bỏ mảng 42 ô). */
function gonLich(lich) {
  if (!lich || !lich.coLich) { return null; }
  return {
    tieuDe: lich.tieuDe, thang: lich.thang, nam: lich.nam,
    soLabel: lich.soLabel, nhanLabel: lich.nhanLabel,
    soO: lich.soO, soNgayDo: lich.soNgayDo, ngayDo: lich.ngayDo, slotsCuaNgayDo: lich.slotsCuaNgayDo,
    htmlOMau: lich.htmlOMau,
  };
}

/**
 * **CHỜ LỊCH LẮNG** — dump lại theo nhịp cho tới khi {@link vanTayLich} không đổi qua {@link LANG_CAN_GIONG}
 * nhịp liên tiếp. Trị GỐC của lỗi "đọc lịch trước khi dữ liệu khung giờ về" (xem {@link LANG_NHIP_MS}).
 *
 * ⚠ ĐIỀU KIỆN LẮNG LÀ "NGƯNG THAY ĐỔI", **KHÔNG** phải "đã có ít nhất một ngày đỏ". Tháng thật sự hết khung
 * cũng là một kết quả hợp lệ, và một cửa đòi phải có ngày đỏ sẽ treo đủ trần rồi báo sai đúng cho tháng đó.
 * Ca "0 ngày đỏ" chỉ bị đòi KIÊN NHẪN hơn — xem {@link LANG_NHIP_TOI_THIEU_KHI_RONG}.
 *
 * KHÔNG ném và KHÔNG tự hạ `ok` của ai: hết trần thì trả `lang:false` kèm đủ bằng chứng, để bước gọi tự quyết
 * (bước 4 vẫn ghi dump nhưng đánh dấu KHÔNG ĐÁNG TIN; bước 5 thì DỪNG — xem {@link ketLuanNgayDo}).
 *
 * @returns {Promise<{lang:boolean, soNhip:number, vanTayDau:string|null, vanTayCuoi:string|null,
 *                    vanTayDoi:boolean, lich:object|null}>}
 */
async function choLichLang(chay) {
  let lich = null;
  let vanTruoc = null;
  let vanTayDau = null;
  let giong = 1;
  let soNhip = 0;
  for (let i = 0; i < LANG_TRAN_NHIP; i++) {
    const l = await chay(pageDumpLichFlash, []);
    soNhip = i + 1;
    if (l && l.coLich) { lich = l; }
    const van = vanTayLich(l);
    if (vanTayDau === null) { vanTayDau = van; }
    giong = vanTayGiongNhau(van, vanTruoc) ? giong + 1 : 1;
    if (van !== null && giong >= LANG_CAN_GIONG) {
      // Tháng CÓ ngày đỏ ⇒ lắng ngay. Tháng 0 ngày đỏ ⇒ chờ thêm cho hết ca "API chưa về".
      const duKienNhan = (l.soNgayDo > 0) || soNhip >= LANG_NHIP_TOI_THIEU_KHI_RONG;
      if (duKienNhan) {
        return { lang: true, soNhip, vanTayDau, vanTayCuoi: van,
                 vanTayDoi: !vanTayGiongNhau(vanTayDau, van), lich };
      }
    }
    vanTruoc = van;
    await sleep(LANG_NHIP_MS);
  }
  return { lang: false, soNhip, vanTayDau, vanTayCuoi: vanTruoc,
           vanTayDoi: !vanTayGiongNhau(vanTayDau, vanTruoc), lich };
}

/**
 * **CHỜ BẢNG KHUNG GIỜ LẮNG** sau khi click một ô ngày — cùng HÌNH DẠNG và dùng chung hằng/luật với
 * {@link choLichLang} và {@link choBangSanPhamLang} (`vanTayGiongNhau`, `LANG_CAN_GIONG`, `LANG_TRAN_NHIP`,
 * `LANG_NHIP_TOI_THIEU_KHI_RONG`), chỉ khác nguồn vân tay.
 *
 * Đây là lần thứ TƯ của cùng một lớp lỗi trong file này (thân trang · lịch khung giờ · ô khung giờ trang tạo
 * · và nay là bảng khung giờ của ngày vừa chọn). Danh sách khung giờ được Shopee nạp bất đồng bộ THEO NGÀY
 * vừa click, nên "click xong đọc ngay" luôn là ảnh của trạng thái cũ.
 *
 * ⚠ "0 radio" LÀ KẾT QUẢ HỢP LỆ cho một ngày không còn khung — y như "0 ngày đỏ" ở lịch. Nên áp đúng luật đã
 * có: lắng rồi mới kết luận, và ca 0 phải kiên nhẫn hơn (`LANG_NHIP_TOI_THIEU_KHI_RONG`) vì "chưa nạp xong"
 * và "ngày này hết khung" nhìn giống hệt nhau ở nhịp đầu.
 */
async function choKhungGioLang(chay) {
  let vanTruoc = null;
  let doc = null;
  let vanTayDau = null;
  let giong = 1;
  let soNhip = 0;
  for (let i = 0; i < LANG_TRAN_NHIP; i++) {
    const d = await chay(pageDemKhungGioFlash, []);
    soNhip = i + 1;
    if (d) { doc = d; }
    const van = d ? d.van : null;
    if (vanTayDau === null) { vanTayDau = van; }
    giong = vanTayGiongNhau(van, vanTruoc) ? giong + 1 : 1;
    if (van !== null && giong >= LANG_CAN_GIONG) {
      if (d.soRadio > 0 || soNhip >= LANG_NHIP_TOI_THIEU_KHI_RONG) {
        return { lang: true, soNhip, soRadio: d.soRadio, soRadioHien: d.soRadioHien,
                 vanTayDoi: !vanTayGiongNhau(vanTayDau, van), doc };
      }
    }
    vanTruoc = van;
    await sleep(LANG_NHIP_MS);
  }
  return { lang: false, soNhip, soRadio: doc ? doc.soRadio : 0, soRadioHien: doc ? doc.soRadioHien : 0,
           vanTayDoi: !vanTayGiongNhau(vanTayDau, vanTruoc), doc };
}

/**
 * **CHỜ BẢNG SẢN PHẨM ĐỔI, RỒI MỚI CHỜ LẮNG** — dùng sau MỖI cú bấm sắp xếp.
 *
 * Hai vế, thiếu vế nào cũng đọc sai (xem {@link DOI_TRAN_NHIP}):
 *  · thiếu chờ ĐỔI ⇒ chốt vào ảnh CŨ, mọi mốc trễ một cú bấm (đúng lỗi lượt 10);
 *  · thiếu chờ LẮNG ⇒ chốt vào ảnh vẽ dở.
 *
 * ⚠ **HẾT TRẦN CHỜ-ĐỔI KHÔNG PHẢI LÀ HỎNG.** Ca "đổi mà nhìn giống hệt" là CÓ THẬT: cú bấm `up` trên shop
 * toàn 0 doanh số cho ra đúng thứ tự cũ, vì mọi giá trị bằng nhau thì tăng dần trùng thứ tự mặc định. Lúc đó
 * `daDoi:false` là câu trả lời ĐÚNG, không phải lỗi — cứ đi tiếp, và ghi `daDoi` + `soNhipChoDoi` vào dump để
 * người đọc phân biệt được "chờ mãi không đổi" với "đổi rồi".
 *
 * @param {string|null} vanTruocBam Vân tay chụp NGAY TRƯỚC cú bấm.
 */
async function choBangDoiRoiLang(chay, vanTruocBam) {
  let daDoi = false;
  let soNhipChoDoi = 0;
  let vanSau = vanTruocBam;
  for (let i = 0; i < DOI_TRAN_NHIP; i++) {
    const d = await chay(pageVanTayModalSanPham, []);
    soNhipChoDoi = i + 1;
    const van = d ? d.van : null;
    if (van !== null) { vanSau = van; }
    // ĐỔI = vân tay KHÁC lúc trước bấm. Dùng lại `vanTayGiongNhau` — không viết bản thứ hai của phép so.
    if (van !== null && !vanTayGiongNhau(van, vanTruocBam)) { daDoi = true; break; }
    await sleep(LANG_NHIP_MS);
  }
  const cho = await choBangSanPhamLang(chay);
  return {
    daDoi, soNhipChoDoi, vanTayTruocBam: vanTruocBam === undefined ? null : vanTruocBam,
    vanTaySauBam: cho.van !== undefined && cho.van !== null ? cho.van : vanSau,
    lang: cho.lang, soNhip: cho.soNhip, soDong: cho.soDong,
  };
}

/**
 * **CHỜ SAU MỘT CÚ BẤM SẮP XẾP — HAI TẦNG.** Đây là hàm bước 9 dùng; {@link choBangDoiRoiLang} là tầng 2.
 *
 * TẦNG 1 — **lớp phủ đang-tải** ({@link LOP_PHU_HIEN_TRAN_NHIP}): chờ `.eds-table__loading` HIỆN rồi TẮT.
 * Đây là tín hiệu dứt khoát "Shopee đã sắp xếp xong", và là tín hiệu DUY NHẤT sống được ở ca **thứ tự sau khi
 * sắp xếp TRÙNG thứ tự cũ** (shop toàn 0 doanh số) — ca mà tầng 2 một mình chỉ biết hết trần rồi đoán.
 *
 * TẦNG 2 — **chờ đổi nội dung** ({@link choBangDoiRoiLang}): chỉ chạy khi KHÔNG bắt được lớp phủ (nó chớp quá
 * nhanh, hoặc Shopee đổi markup).
 *
 * Cả hai tầng đều kết thúc bằng {@link choBangSanPhamLang} để chắc DOM đã lắng.
 *
 * `nguonTinHieu` nói mốc đó tin được tới mức nào:
 *  · `"lop-phu"`  — bắt được lớp phủ hiện RỒI tắt. Tin cậy nhất.
 *  · `"cho-doi"`  — không thấy lớp phủ, nhưng nội dung đã đổi thật.
 *  · `"het-tran"` — lớp phủ hiện mà không tắt trong hạn, HOẶC không lớp phủ và nội dung cũng không đổi.
 *                   Mốc này KHÔNG đáng tin; đọc dump phải thấy ngay.
 */
async function choBangSauBamSapXep(chay, vanTruocBam) {
  // ── TẦNG 1: lớp phủ ────────────────────────────────────────────────────────────────────────────────
  let thayLopPhu = false;
  let soNhipChoHien = 0;
  let soLopPhu = null;
  for (let i = 0; i < LOP_PHU_HIEN_TRAN_NHIP; i++) {
    const d = await chay(pageDoLopPhuBangSanPham, []);
    soNhipChoHien = i + 1;
    if (d) { soLopPhu = d.soLopPhuTrongDom; }
    if (d && d.dangHien) { thayLopPhu = true; break; }
    await sleep(LANG_NHIP_MS);
  }

  if (thayLopPhu) {
    let daTat = false;
    let soNhipChoTat = 0;
    for (let i = 0; i < LOP_PHU_TAT_TRAN_NHIP; i++) {
      await sleep(LANG_NHIP_MS);
      const d = await chay(pageDoLopPhuBangSanPham, []);
      soNhipChoTat = i + 1;
      if (d && !d.dangHien) { daTat = true; break; }
    }
    const cho = await choBangSanPhamLang(chay);
    return {
      nguonTinHieu: daTat ? "lop-phu" : "het-tran",
      thayLopPhu: true, soNhipChoHien, soNhipChoTat, soLopPhuTrongDom: soLopPhu,
      daDoi: null, soNhipChoDoi: null,
      vanTayTruocBam: vanTruocBam === undefined ? null : vanTruocBam,
      vanTaySauBam: cho.van === undefined ? null : cho.van,
      lang: cho.lang, soNhip: cho.soNhip, soDong: cho.soDong,
    };
  }

  // ── TẦNG 2: không bắt được lớp phủ ⇒ rơi về chờ đổi nội dung ────────────────────────────────────────
  const kq = await choBangDoiRoiLang(chay, vanTruocBam);
  return Object.assign({
    nguonTinHieu: kq.daDoi ? "cho-doi" : "het-tran",
    thayLopPhu: false, soNhipChoHien, soNhipChoTat: null, soLopPhuTrongDom: soLopPhu,
  }, kq);
}

/**
 * Chờ picker chuyển sang THÁNG KHÁC sau một cú bấm mũi tên, **RỒI chờ tháng mới LẮNG**.
 *
 * Hai vế, thiếu vế nào cũng hỏng: chỉ chờ đổi tháng thì lại đọc đúng lúc lưới ngày mới vẽ mà số khung giờ chưa
 * về (đúng lỗi cũ, chỉ dời sang tháng khác); chỉ chờ lắng thì tháng cũ đang đứng yên cũng bị tính là "đã đổi".
 *
 * ⚠ VẾ THỨ BA, THÊM 27/08/2026: phải đáp xuống **ĐÚNG tháng mong đợi**, không phải "một tháng nào đó khác"
 * — xem {@link dungThangMongDoi} cho ca thật đã lùi 12 tháng mà vẫn được tính là thành công.
 *
 * Trả `{daDoi, dungThang, lang, lich, mongDoi, soNhipDoi, cho}` — `lich` luôn là bản dump MỚI NHẤT, kể cả khi
 * hỏng, để bước gọi có bằng chứng mà báo lỗi.
 *
 * @param {boolean} tien true = bấm sang tháng SAU, false = về tháng TRƯỚC.
 */
async function choDoiThang(chay, truoc, tien) {
  let lich = truoc;
  const mongDoi = truoc ? thangKeTiep(truoc.thang, truoc.nam, tien) : null;
  for (let i = 0; i < 20; i++) {
    await sleep(LANG_NHIP_MS);
    const l = await chay(pageDumpLichFlash, []);
    if (l && l.coLich) {
      lich = l;
      if (daDoiThang(truoc, l)) {
        // Lịch đã rời tháng cũ. Bây giờ mới hỏi câu QUAN TRỌNG: có đúng tháng mong đợi không?
        if (!dungThangMongDoi(truoc, l, tien)) {
          return { daDoi: true, dungThang: false, lang: false, lich: l, mongDoi, soNhipDoi: i + 1, cho: null };
        }
        const cho = await choLichLang(chay);
        return { daDoi: true, dungThang: true, lang: cho.lang, lich: cho.lich || l, mongDoi,
                 soNhipDoi: i + 1, cho };
      }
    }
  }
  return { daDoi: false, dungThang: false, lang: false, lich, mongDoi, soNhipDoi: 20, cho: null };
}

/**
 * Chạy TRỌN lượt dò DOM Flash Sale cho SHOP ĐANG MỞ (tab do `openShopDetail` để lại).
 *
 * <b>MỘT BƯỚC HỎNG ⇒ DỪNG NGAY</b> (đúng ràng buộc (1) của `flow-km.js`): các bước sau chạy trên trạng thái
 * rác thì không có giá trị chẩn đoán nào, mà lại là lúc dễ bấm trúng thứ không định bấm nhất.
 *
 * @returns {Promise<{buoc:Array, anSo:object, captcha:boolean}>}
 */
export async function doDomFlashSaleChoTabDangMo() {
  const tabId = tabShop();
  const ds = [];
  /** Câu trả lời cho 4 ẩn số của plan §3 — gom riêng để đọc dump không phải lần theo từng bước. */
  const anSo = {
    sapXepDoanhSo: null,   // #1: className cụm sắp xếp tại T0/T1/T2
    lichThangNay: null,    // #2: số ngày đỏ + tổng khung của tháng đang hiện
    oXemSanPhamCoSan: null,// #3: checked mặc định của label[data-cy=product-modal-available-checkbox]
    lichThangSau: null,    // #4: tháng sau có ngày đỏ không (đêm cuối tháng)
    ngayDaChon: null,      // ngày/tháng bước 5 chọn được + đã phải lật mấy tháng
  };
  const ketThuc = (them) =>
    Object.assign({ buoc: ds, anSo, captcha: ds.some((b) => b.captcha === true) }, them || {});

  if (tabId == null) {
    ds.push({
      ten: "0. Tab shop", ok: false, selectorDung: null, kyVong: null, kieuSo: null, docLai: null,
      truoc: null, sau: null, ghiChu: null,
      loi: "chưa có tab shop (openShopDetail chưa chạy, hoặc service worker đã chết giữa chừng)",
    });
    return ketThuc();
  }
  const chay = (fn, args) => execInTab(tabId, fn, args || []);
  const NHAN = "dò Flash";

  // NGÀY MAI tính MỘT LẦN ở đây, không đọc lại đồng hồ trong các bước sau (plan §4 chốt 1).
  const mai = new Date();
  const homNay = mai.getMonth();
  mai.setDate(mai.getDate() + 1);
  const ngayMai = mai.getDate();
  const ngayMaiThuocThangSau = mai.getMonth() !== homNay;

  // 1) Vào "Flash Sale Của Shop" qua SIDEBAR (không gọi thẳng URL).
  //    CHỜ sidebar render y như bước 1 của `flow-km.js`: ngay sau SSO trang CHƯA có sidebar, và hai lần dính
  //    `verify/traffic/error` đều xảy ra khi code chạm vào trang trong 3–4 giây đầu.
  const SEL_SIDEBAR = 'a[href="/portal/marketing/shop-flash-sale/list"]';
  const b1 = await buoc(ds, "1. Vào Flash Sale Của Shop", async () => {
    const truoc = await chay(() => location.href);
    const coLink = await choHien(chay, SEL_SIDEBAR);
    if (!coLink) {
      const sauHong = await chay(() => location.href);
      return { selectorDung: null, truoc, sau: sauHong,
               ghiChu: "KHÔNG thấy link 'Flash Sale Của Shop' trong sidebar sau 30s",
               kyVong: "/shop-flash-sale", kieuSo: "chua", docLai: sauHong };
    }
    const sel = await chay(pageClickTheoSelector, [[SEL_SIDEBAR], null]);
    let sau = "";
    for (let i = 0; i < 30; i++) {
      await sleep(500);
      sau = await chay(() => location.href);
      if (String(sau || "").indexOf("/shop-flash-sale") >= 0) { break; }
    }
    return { selectorDung: sel, truoc, sau, ghiChu: "sidebar → link 'Flash Sale Của Shop' (đang hiện)",
             kyVong: "/shop-flash-sale", kieuSo: "chua", docLai: sau };
  }, tabId, NHAN);
  if (!b1.ok) { return ketThuc(); }

  // 2) Bấm "Tạo" ở landing-page-header. Kỳ vọng: form mở ⇒ có ô khung thời gian.
  const b2 = await buoc(ds, "2. Bấm Tạo", async () => {
    // Cài luật TRƯỚC vòng chờ: dấu hiệu thứ 4 ("có nút chữ Tạo") đi qua chính `window._flashNutTao`.
    await chay(pageCaiLuatNutTaoFlash, []);
    const truoc = await chay(() => location.href);

    // ⚠ CHỜ THÂN TRANG RENDER RỒI MỚI TÌM NÚT. Bản trước tìm ngay và hỏng câm trong CÙNG một giây với bước 1
    // (12:01:31 → 12:01:31, lượt 27/08/2026): 39 phần tử bấm được đều là breadcrumb + sidebar, thân trang
    // chưa vẽ. Xem {@link THAN_TRANG_NHIP_MS}.
    const choThan = await choThanTrangDanhSach(chay);
    if (!choThan.thay) {
      const dumpSom = await chay(pageDumpTrangDanhSach, [TRAN_UNG_VIEN_DUMP]);
      return {
        selectorDung: null, truoc,
        sau: { url: truoc, choThanTrang: choThan, dumpTrangDanhSach: dumpSom },
        loi: "THÂN TRANG danh sách chưa render sau " + choThan.soNhipCho + " nhịp (~"
          + Math.round((choThan.soNhipCho * THAN_TRANG_NHIP_MS) / 1000) + "s) — chưa có gì để neo nút Tạo",
        kyVong: "roi-list", docLai: "con-list",
      };
    }

    const bam = await chay(pageBamNutTaoFlash, []);

    // Chờ trang RỜI /list. Đọc lại bằng URL chứ không bằng `time-slot-btn`: nút không ăn thì trang đứng
    // nguyên, và phép đọc phải nói được "CHƯA HỀ rời trang" — xem {@link daRoiTrangList}.
    let sau = truoc;
    let choOKhung = null;
    if (bam && bam.ok) {
      for (let i = 0; i < CHO_NHIP; i++) {
        await sleep(500);
        sau = await chay(() => location.href);
        if (daRoiTrangList(sau)) { break; }
      }
      // ⚠ CHỜ (không phải đếm MỘT LẦN) ô khung giờ của TRANG TẠO hiện ra. Bước 3 bấm đúng nút này, nên
      // "URL đã đổi" chưa đủ: trang tạo cũng render thân bất đồng bộ y như trang danh sách. Bản trước đếm
      // một phát rồi đi tiếp — cùng cái bẫy vừa vá ở trên, chỉ khác trang.
      choOKhung = await choHienCoNhip(chay, "button.time-slot-btn");
    }
    const soO = choOKhung ? choOKhung.soThay : 0;
    const roi = daRoiTrangList(sau);

    // ⚠ HỎNG ⇒ DUMP TRANG DANH SÁCH. Không có nó thì mỗi lần hỏng mất trọn một lượt chạy thật (ba lượt
    // 27/08 mới suy ra được "trang render khác nhau tuỳ trạng thái").
    const dumpTrang = roi ? null : await chay(pageDumpTrangDanhSach, [TRAN_UNG_VIEN_DUMP]);

    return {
      selectorDung: bam && bam.ok ? bam.nguonNutTao : null,
      truoc,
      sau: { url: sau, roiTrangList: roi, soOKhungThoiGian: soO, choThanTrang: choThan,
             choOKhungThoiGian: choOKhung, bam, dumpTrangDanhSach: dumpTrang },
      loi: bam && bam.ok ? null : (bam && bam.loi) || "không tìm được nút Tạo",
      kyVong: "roi-list", docLai: roi ? "roi-list" : "con-list",
      ghiChu: "Thân trang thấy sau " + choThan.soNhipCho + " nhịp qua dấu hiệu '" + choThan.nguonDauHieu
        + "'. Nút Tạo neo theo CHỮ (đúng bằng 'Tạo', loại vùng tab/lọc), ưu tiên vùng landing-page. "
        + "Đọc lại bằng URL đã RỜI /shop-flash-sale/list; số ô khung giờ chỉ là bằng chứng đi kèm.",
    };
  }, tabId, NHAN);
  if (!b2.ok) { return ketThuc(); }

  // 3) Mở modal khung giờ.
  //    RÀ SOÁT 27/08/2026 — KHÔNG cần thêm vòng chờ ở đây, và đây là lý do (đừng bỏ câu này, nó tồn tại để
  //    lần sau khỏi phải dò lại): nút `button.time-slot-btn` đã được bước 2 CHỜ hiện ra rồi mới sang bước
  //    này; còn phần NỘI DUNG của modal (dữ liệu khung giờ về sau) thì đã có `choLichLang` ở bước 4 lo —
  //    `choHien` dưới đây chỉ cần khẳng định modal đã BUNG, không kết luận gì về dữ liệu.
  const b3 = await buoc(ds, "3. Mở modal khung thời gian", async () => {
    const sel = await chay(pageClickTheoSelector, [["button.time-slot-btn"], null]);
    const soO = await choHien(chay, ".eds-date-table__cell");
    return {
      selectorDung: sel, sau: { soONgay: soO },
      kyVong: "co", docLai: soO > 0 ? "co" : "khong",
      ghiChu: "Kỳ vọng: modal 'Chọn khung giờ Flash sale Của Shop' mở ⇒ có ô .eds-date-table__cell.",
    };
  }, tabId, NHAN);
  if (!b3.ok) { return ketThuc(); }

  // 4) DUMP LỊCH — ẩn số #2 (số ngày đỏ thật của một tháng) và #4 (tháng sau có ngày đỏ không).
  //    Luật ô ngày CÀI TỪ `flow-km.js`; cài lại mỗi lượt là idempotent và rẻ.
  let lichNay = null;
  let langNay = false;
  const b4 = await buoc(ds, "4. DUMP LỊCH (tháng này + tháng sau)", async () => {
    await chay(pageCaiLuatONgayEds, []);
    await chay(pageCaiLuatMuiTenThang, []);
    await chay(pageCaiLuatNgayDoFlash, []);
    await chay(pageCaiLuatSoNgayFlash, []);
    await chay(pageCaiLuatTieuDeThangFlash, []);

    // ⚠ CHỜ LỊCH LẮNG RỒI MỚI DUMP — cả BA chỗ: tháng này · sau khi sang tháng sau · sau khi về lại.
    // Bản trước dump THẲNG ngay khi modal vừa mở, và đúng cú đó đọc ra `soNgayDo: 0` cho một tháng CÓ ngày đỏ
    // (lượt 10:27:07 ngày 27/08/2026 — xem {@link LANG_NHIP_MS}).
    const choNay = await choLichLang(chay);
    lichNay = choNay.lich;
    langNay = choNay.lang;
    const ketLuanNay = ketLuanNgayDo(lichNay, choNay.lang);
    anSo.lichThangNay = Object.assign(gonLich(lichNay) || {}, {
      daLang: choNay.lang, soNhipCho: choNay.soNhip, vanTayDoi: choNay.vanTayDoi,
      vanTayDau: choNay.vanTayDau, vanTayCuoi: choNay.vanTayCuoi,
      chac: ketLuanNay.chac, ketLuan: ketLuanNay.ly,
    });

    // Sang THÁNG SAU rồi dump lại — rồi VỀ LẠI tháng cũ (bước 5 bắt đầu từ tháng hiện tại).
    const bamTien = await chay(pageBamMuiTenThangFlash, [true]);
    let lichSau = null;
    let veLai = null;
    let doi = null;
    let ve = null;
    if (bamTien && bamTien.ok) {
      doi = await choDoiThang(chay, lichNay, true);
      lichSau = doi.lich;
      const ketLuanSau = ketLuanNgayDo(lichSau, doi.lang);
      anSo.lichThangSau = Object.assign(gonLich(lichSau) || {}, {
        daDoiThang: doi.daDoi, dungThangMongDoi: doi.dungThang, mongDoi: doi.mongDoi,
        daLang: doi.lang,
        soNhipCho: doi.cho ? doi.cho.soNhip : null,
        vanTayDoi: doi.cho ? doi.cho.vanTayDoi : null,
        chac: ketLuanSau.chac, ketLuan: ketLuanSau.ly,
      });

      veLai = await chay(pageBamMuiTenThangFlash, [false]);
      if (veLai && veLai.ok) {
        ve = await choDoiThang(chay, lichSau, false);
        // ⚠ LẠC THÁNG ⇒ HỎNG CÓ TÊN, KHÔNG nhận cái lịch đó làm `lichNay`.
        // Ca thật 12:20:04 ngày 27/08/2026: cú bấm về đáp xuống Tháng 9/2025 (lùi 12 tháng), bản cũ nhận
        // luôn, và bước 5 đi tìm ngày đỏ trên một tháng cách đích 11 tháng rồi báo "không có ngày đỏ nào"
        // — trong khi chính bước 4 vừa đo được 5 ngày đỏ ở Tháng 8/2026.
        if (ve.daDoi && !ve.dungThang) {
          return {
            sau: { lichThangNay: anSo.lichThangNay, bamTien, lichThangSau: anSo.lichThangSau, veLai,
                   veLac: { mongDoi: ve.mongDoi, thucTe: moTaThang(ve.lich) } },
            loi: "bấm VỀ tháng trước mà lịch đáp xuống " + moTaThang(ve.lich) + " (mong đợi "
              + (ve.mongDoi ? "Tháng " + ve.mongDoi.thang + "/" + ve.mongDoi.nam : "?")
              + ") — LẠC THÁNG, không đi tiếp",
            kyVong: "co", docLai: "khong",
          };
        }
        if (ve.lich && ve.lich.coLich) { lichNay = ve.lich; langNay = ve.lang; }
      }
    }

    return {
      sau: { lichThangNay: anSo.lichThangNay, bamTien, lichThangSau: anSo.lichThangSau,
             doiThang: doi ? { daDoi: doi.daDoi, dungThang: doi.dungThang, mongDoi: doi.mongDoi,
                               lang: doi.lang, soNhipDoi: doi.soNhipDoi } : null,
             veLai, veThang: ve ? { daDoi: ve.daDoi, dungThang: ve.dungThang, mongDoi: ve.mongDoi } : null,
             langSauKhiVe: langNay, thangDangDung: moTaThang(lichNay) },
      ghiChu: "ngày mai = " + ngayMai + (ngayMaiThuocThangSau ? " (THUỘC THÁNG SAU)" : "")
        + " · tháng đang hiện = " + moTaThang(lichNay)
        + " · " + ketLuanNgayDo(lichNay, langNay).ly
        + " · ngày đỏ tháng sau = " + (lichSau ? lichSau.soNgayDo : "?"),
      kyVong: "co", docLai: lichNay && lichNay.coLich ? "co" : "khong",
    };
  }, tabId, NHAN);
  if (!b4.ok) { return ketThuc(); }

  // 5) Chọn NGÀY MAI; mai không đỏ ⇒ ngày đỏ gần nhất TRONG THÁNG NÀY; tháng này sạch ngày đỏ ⇒ LẬT SANG
  //    THÁNG SAU và lấy ngày đỏ đầu tháng (tối đa TRAN_THANG_SANG lần).
  //
  //    ⚠ ĐỪNG bỏ nhánh lật tháng cho "gọn". Lượt 10:27:07 ngày 27/08/2026 dừng đúng ở đây với câu "tháng đang
  //    hiện KHÔNG còn ngày đỏ nào" — trong khi CHÍNH bước 4 của lượt ấy đã đo được tháng 9 còn 10 ngày đỏ.
  //    Bỏ cuộc khi vẫn còn chỗ để dò nghĩa là ba ẩn số sau (sắp xếp Doanh số, ô "Xem sp có sẵn", modal sản
  //    phẩm) mất trắng, và phải chờ tới lượt chạy thật sau mới đo lại được.
  const b5 = await buoc(ds, "5. Chọn ngày đỏ", async () => {
    let lich = lichNay;
    let lang = langNay;
    let chon = chonNgayDich(ngayDoCuaLich(lich), ngayMai);
    let thangDaSang = 0;
    const ghiThang = (l, la, c) => ({ thang: moTaThang(l), soNgayDo: l ? l.soNgayDo : null,
                                      ketLuan: ketLuanNgayDo(l, la).ly, chon: c.ly });
    const nhatKyThang = [ghiThang(lich, lang, chon)];

    // ⚠ CỬA CHẶN "QUYẾT ĐỊNH TRÊN DỮ LIỆU RÁC" — đọc trước khi sửa.
    // Thấy 0 ngày đỏ mà lịch CHƯA LẮNG thì con số đó vô nghĩa: nó là lưới ngày lúc dữ liệu khung giờ chưa về.
    // Lật sang tháng sau dựa trên nó là đi tiếp trên một tiền đề sai — và tiền đề sai đó chính là thứ đã làm
    // cả một phiên chẩn đoán kết luận "Alina Store1 hết ngày đỏ tháng 8" trong khi tháng 8 có 5 ngày đỏ.
    // Chưa lắng ⇒ HỎNG CÓ TÊN, không đi tiếp.
    const ketLuan = ketLuanNgayDo(lich, lang);
    if (!ketLuan.chac) {
      return {
        sau: { nhatKyThang, ketLuan, soNgayDoDocDuoc: lich ? lich.soNgayDo : null },
        loi: "KHÔNG kết luận được ngày đỏ: " + ketLuan.ly + " — DỪNG, không lật tháng và không chọn ngày nào",
        kyVong: "co", docLai: "khong",
      };
    }

    while (chon.soNgay === null && thangDaSang < TRAN_THANG_SANG) {
      const bam = await chay(pageBamMuiTenThangFlash, [true]);
      if (!bam || !bam.ok) {
        return { sau: { nhatKyThang, bam }, loi: "hết ngày đỏ ở tháng đang hiện mà KHÔNG lật được sang tháng sau: "
                 + ((bam && bam.loi) || "(không rõ)"), kyVong: "co", docLai: "khong" };
      }
      const doi = await choDoiThang(chay, lich, true);
      thangDaSang++;
      if (!doi.daDoi) {
        return { sau: { nhatKyThang, bam, lichSauKhiBam: doi.lich },
                 loi: "bấm sang tháng sau mà tháng/năm trên header KHÔNG đổi", kyVong: "co", docLai: "khong" };
      }
      // ⚠ "Đổi rồi" chưa đủ — phải ĐÚNG tháng kế tiếp. Xem {@link dungThangMongDoi}.
      if (!doi.dungThang) {
        return {
          sau: { nhatKyThang, bam, lac: { mongDoi: doi.mongDoi, thucTe: moTaThang(doi.lich) } },
          loi: "bấm sang tháng sau mà lịch đáp xuống " + moTaThang(doi.lich) + " (mong đợi "
            + (doi.mongDoi ? "Tháng " + doi.mongDoi.thang + "/" + doi.mongDoi.nam : "?") + ") — LẠC THÁNG",
          kyVong: "co", docLai: "khong",
        };
      }
      lich = doi.lich;
      lang = doi.lang;
      // Cùng cửa chặn cho MỌI tháng lật tới — không phải chỉ tháng đầu.
      const ketLuanMoi = ketLuanNgayDo(lich, lang);
      if (!ketLuanMoi.chac) {
        nhatKyThang.push(ghiThang(lich, lang, { ly: "(chưa xét)" }));
        return {
          sau: { nhatKyThang, ketLuan: ketLuanMoi },
          loi: "sang " + moTaThang(lich) + " nhưng KHÔNG kết luận được: " + ketLuanMoi.ly + " — DỪNG",
          kyVong: "co", docLai: "khong",
        };
      }
      chon = chonNgayDoDauThang(ngayDoCuaLich(lich));
      nhatKyThang.push(ghiThang(lich, lang, chon));
    }

    if (chon.soNgay === null) {
      return { sau: { nhatKyThang, thangDaSang },
               loi: "không tìm được ngày đỏ nào sau khi đã lật " + thangDaSang + " tháng",
               kyVong: "co", docLai: "khong" };
    }

    const click = await chay(pageClickNgayFlash, [chon.soNgay]);
    // ⚠ CHỜ BẢNG KHUNG GIỜ LẮNG — KHÔNG dùng `choHien` cho radio (xem {@link pageDemKhungGioFlash}):
    // EDS ẩn thẻ <input> gốc nên phép đếm theo kích thước trả 0 vĩnh viễn, chờ 30s vẫn 0.
    const choKhung = click && click.ok ? await choKhungGioLang(chay) : null;
    const soRadio = choKhung ? choKhung.soRadio : 0;
    anSo.ngayDaChon = { soNgay: chon.soNgay, thang: moTaThang(lich), thangDaSang, ly: chon.ly,
                        soRadio, daLang: choKhung ? choKhung.lang : null,
                        soNhipCho: choKhung ? choKhung.soNhip : null };
    const loiKhung = click && click.ok && choKhung && !choKhung.lang
      ? "bảng khung giờ CHƯA LẮNG sau " + choKhung.soNhip + " nhịp — con số " + soRadio
        + " radio KHÔNG ĐÁNG TIN"
      : (click && click.ok && soRadio === 0
          ? "ngày " + chon.soNgay + " ĐỎ (có khung giờ trên lịch) mà bảng khung giờ đã lắng vẫn 0 radio — mâu thuẫn"
          : null);
    return {
      sau: { chon, thangDaSang, nhatKyThang, click, choKhungGio: choKhung, soRadio },
      loi: click && click.ok ? loiKhung : (click && click.loi) || "không click được ô ngày",
      ghiChu: "ĐÃ CHỌN ngày " + chon.soNgay + " của " + moTaThang(lich)
        + (thangDaSang > 0 ? " (đã lật " + thangDaSang + " tháng vì tháng hiện tại hết ngày đỏ)" : "")
        + " · " + chon.ly
        + (choKhung ? " · khung giờ: " + choKhung.soRadio + " radio (hiện " + choKhung.soRadioHien
                      + "), lắng sau " + choKhung.soNhip + " nhịp" : ""),
      kyVong: "co", docLai: soRadio > 0 ? "co" : "khong",
    };
  }, tabId, NHAN);
  if (!b5.ok) { return ketThuc(); }

  // 6) DUMP KHUNG GIỜ của ngày vừa chọn.
  //    RÀ SOÁT LẦN HAI 27/08/2026 — đủ, và lần này lý do là SỐ ĐO chứ không phải suy luận: bước 5 nay chờ
  //    bảng khung giờ LẮNG (`choKhungGioLang`, vân tay = số radio + nhãn 3 khung đầu), nên tới đây bảng đã
  //    đứng yên. Bản rà soát TRƯỚC xếp bước 5 là "Đủ" và đã SAI — nó tin `choHien` chờ được radio, trong khi
  //    `pageDemPhanTuHien` lọc theo kích thước còn EDS thì ẩn hẳn thẻ <input>. Bài học: "có gọi hàm chờ"
  //    KHÔNG bằng "hàm chờ đó đếm được thứ mình cần".
  //    ⚠ Nếu dump lượt tới cho `sanPham: null` hàng loạt trong khi `nhan` đọc được thì ĐÂY là chỗ phải thêm
  //    chờ theo dấu hiệu — đừng đi tìm chỗ khác.
  let dumpKhung = null;
  const b6 = await buoc(ds, "6. DUMP KHUNG GIỜ", async () => {
    dumpKhung = await chay(pageDumpKhungGioFlash, []);
    return {
      sau: dumpKhung,
      ghiChu: "Số khung đọc được: " + (dumpKhung ? dumpKhung.soKhung : "?"),
      kyVong: "co", docLai: dumpKhung && dumpKhung.soKhung > 0 ? "co" : "khong",
    };
  }, tabId, NHAN);
  if (!b6.ok) { return ketThuc(); }

  // 7) Tick khung ĐẦU TIÊN → Xác nhận CỦA MODAL KHUNG GIỜ (chỉ điền vào form — KHÔNG tạo gì).
  //    RÀ SOÁT 27/08/2026 — đủ: phép đọc lại `button.time-slot-btn.has-value` đã là một vòng CHỜ
  //    (`choHien`), tức nó tự cover cả độ trễ Vue vẽ lại ô thời gian sau khi modal đóng.
  const b7 = await buoc(ds, "7. Tick khung đầu + Xác nhận modal khung giờ", async () => {
    const tick = await chay(pageTickKhungFlash, [0]);
    if (!tick || !tick.ok || !tick.checked) {
      return { sau: { tick }, loi: (tick && tick.loi) || "tick khung đầu không ăn", kyVong: "co", docLai: "khong" };
    }
    await sleep(600);
    const xacNhan = await chay(pageBamXacNhanKhungFlash, []);
    if (!xacNhan || !xacNhan.ok) {
      return { sau: { tick, xacNhan }, loi: (xacNhan && xacNhan.loi) || "không bấm được Xác nhận modal khung giờ",
               kyVong: "co", docLai: "khong" };
    }
    const daDien = await choHien(chay, "button.time-slot-btn.has-value");
    return {
      sau: { tick, xacNhan, soODaCoGiaTri: daDien },
      ghiChu: "Xác nhận này CHỈ điền khung giờ vào form. Nút TẠO THẬT nằm ở thanh đáy và lượt dò KHÔNG đụng tới.",
      kyVong: "co", docLai: daDien > 0 ? "co" : "khong",
    };
  }, tabId, NHAN);
  if (!b7.ok) { return ketThuc(); }

  // 8) Bấm "Thêm sản phẩm".
  //    RÀ SOÁT 27/08/2026 — `choHien` ở đây chỉ khẳng định modal đã BUNG và có dòng đầu tiên; phần "bảng đã
  //    đủ hàng chưa" do `choBangSanPhamLang` ở bước 9 lo. Hai câu hỏi khác nhau, cố ý tách làm hai.
  const b8 = await buoc(ds, "8. Bấm Thêm sản phẩm", async () => {
    // Cài luật bảng sản phẩm TRƯỚC khi modal mở — mọi phép đếm dòng / đọc cột / chọn nút sắp xếp sau đó đều
    // đi qua nó (xem {@link pageCaiLuatBangSanPham}).
    await chay(pageCaiLuatBangSanPham, []);
    const sel = await chay(pageClickTheoSelector,
      [[".detail-products-container .empty-content button", ".detail-products-container button"], "thêm sản phẩm"]);
    const soDong = await choHien(chay, "tr.mkt-product-selector-row-in-root");
    return {
      selectorDung: sel, sau: { soDong },
      kyVong: "co", docLai: soDong > 0 ? "co" : "khong",
      ghiChu: "Kỳ vọng: modal 'Chọn Sản Phẩm' mở ⇒ có tr.mkt-product-selector-row-in-root.",
    };
  }, tabId, NHAN);
  if (!b8.ok) { return ketThuc(); }

  // 9) DUMP MODAL SẢN PHẨM — phần quan trọng nhất. Ẩn số #1 (ba mốc T0/T1/T2) + #3 (ô "Xem sp có sẵn").
  const b9 = await buoc(ds, "9. ĐO SẮP XẾP DOANH SỐ (M0 → down → M1 → down → M2 → up → M3)", async () => {
    // ⚠ CHỜ BẢNG LẮNG RỒI MỚI ĐỌC MỐC. Bước 8 mới chỉ chờ dòng ĐẦU TIÊN xuất hiện, mà "có dòng" ≠ "đủ hàng":
    // đọc mốc lúc bảng còn chảy là chụp một trạng thái dở dang. Xem {@link choBangSanPhamLang}.
    const choBang = await choBangSanPhamLang(chay);
    const dumpModal = await chay(pageDumpModalSanPhamFlash, []);
    anSo.oXemSanPhamCoSan = dumpModal ? dumpModal.oXemSanPhamCoSan : null;

    /** Vân tay bảng NGAY LÚC NÀY — chụp TRƯỚC mỗi cú bấm để vòng chờ biết "đổi" nghĩa là khác cái gì. */
    const vanTay = async () => {
      const d = await chay(pageVanTayModalSanPham, []);
      return d ? d.van : null;
    };

    /**
     * Một MỐC = chờ bảng ĐỔI (nếu là mốc sau cú bấm) → chờ LẮNG → đọc CẢ BA: trạng thái nút · thứ tự mã
     * sản phẩm · Doanh số.
     * <p>`vanTruocBam === undefined` ⇒ mốc M0, chưa bấm gì nên chỉ cần chờ lắng.</p>
     */
    const doMoc = async (ten, vanTruocBam) => {
      const cho = vanTruocBam === undefined
        ? await choBangSanPhamLang(chay)
        : await choBangSauBamSapXep(chay, vanTruocBam);
      const t = await chay(pageDocMocSapXepFlash, []);
      const ma = await chay(pageDocMaSanPhamFlash, []);
      const dso = await chay(pageDocDoanhSoMoiDongFlash, []);
      return {
        ten,
        daLang: cho.lang, soNhipCho: cho.soNhip, soDong: cho.soDong,
        // Bằng chứng "mốc này là ảnh MỚI hay ảnh CŨ" — thiếu nó thì không ai phát hiện được phép đo trễ một
        // cú bấm (lỗi lượt 10). `daDoi:false` KHÔNG phải lỗi: xem {@link choBangDoiRoiLang}.
        nguonTinHieu: cho.nguonTinHieu === undefined ? null : cho.nguonTinHieu,
        thayLopPhu: cho.thayLopPhu === undefined ? null : cho.thayLopPhu,
        soNhipChoHien: cho.soNhipChoHien === undefined ? null : cho.soNhipChoHien,
        soNhipChoTat: cho.soNhipChoTat === undefined ? null : cho.soNhipChoTat,
        soLopPhuTrongDom: cho.soLopPhuTrongDom === undefined ? null : cho.soLopPhuTrongDom,
        daDoi: cho.daDoi === undefined ? null : cho.daDoi,
        soNhipChoDoi: cho.soNhipChoDoi === undefined ? null : cho.soNhipChoDoi,
        vanTayTruocBam: cho.vanTayTruocBam === undefined ? null : cho.vanTayTruocBam,
        vanTaySauBam: cho.vanTaySauBam === undefined ? null : cho.vanTaySauBam,
        clsCum: t ? t.clsCum : null, clsIcon: t ? t.clsIcon : null, ariaSort: t ? t.ariaSort : null,
        // TRỌNG TÀI: thứ tự mã sản phẩm. Mạnh hơn class trạng thái vì quan sát trực tiếp và không chết ở ca
        // shop toàn 0 doanh số.
        ma: ma ? ma.ma : null,
        doanhSo: dso ? dso.giaTri : null,
      };
    };

    const M0 = await doMoc("M0 (chưa bấm)");

    // ══════════════════════════════════════════════════════════════════════════════════════════════════
    // PHA "SẮP XẾP THẬT" — ĐÚNG {@link SO_LAN_BAM_SAP_XEP} cú bấm `down`, KHÔNG BAO GIỜ hơn.
    // Người dùng (27/08/2026): "bấm 1 lần là up, lần nữa là down, lần nữa là không sắp xếp, như chưa bấm gì".
    // ⚠ VÒNG NÀY TUYỆT ĐỐI KHÔNG ĐƯỢC BỌC THỬ LẠI: một cú bấm thừa là XOÁ SẠCH sắp xếp, và mọi bước sau vẫn
    // `ok` — luồng sẽ chọn 10 sản phẩm tuỳ ý thay vì 10 sản phẩm bán chạy nhất, im lặng.
    // ══════════════════════════════════════════════════════════════════════════════════════════════════
    const bam = [];
    const mocSauMoiCu = [];
    for (let i = 0; i < SO_LAN_BAM_SAP_XEP; i++) {
      const vanTruocBam = await vanTay();
      bam.push(await chay(pageBamSapXepDoanhSo, ["down"]));
      mocSauMoiCu.push(await doMoc("M" + (i + 1) + " (sau cú bấm down thứ " + (i + 1) + ")", vanTruocBam));
    }
    const M1 = mocSauMoiCu[0] || null;
    const M2 = mocSauMoiCu[1] || null;

    // ══════════════════════════════════════════════════════════════════════════════════════════════════
    // CÚ BẤM THỨ BA — CHỈ ĐỂ ĐO, VÀ CHỈ HỢP LỆ TRONG LƯỢT DÒ NÀY.
    //
    // CÂU HỎI DUY NHẤT NÓ TRẢ LỜI: **M3 có quay về GIỐNG HỆT M0 không?**
    // Người dùng: "bấm 1 lần là up, lần nữa là down, lần nữa là không sắp xếp, như chưa bấm gì". Nếu đúng thì
    // ta có một phép kiểm rất rẻ và rất mạnh cho luồng thật: chỉ cần so thứ tự mã ở M2 với M0 là biết sắp xếp
    // đã ăn hay chưa, KHÔNG cần biết class trạng thái tên gì (số đo lượt 9: class KHÔNG BAO GIỜ đổi).
    //
    // ⚠ PHẢI LÀ "down" — CÙNG CHIỀU với hai cú trên. Đây là cú thứ BA CỦA CÙNG MỘT CHU KỲ, không phải một cú
    // bấm chiều ngược. Bản trước bấm "up" ở đây nên chu kỳ ba trạng thái CHƯA HỀ được kiểm.
    //
    // ⚠ LUỒNG THẬT KHÔNG ĐƯỢC CHÉP ĐOẠN NÀY. Lượt dò kết thúc bằng HỦY nên bảng có bị xoá sắp xếp cũng không
    // hại ai; luồng thật thì cú thứ ba là chọn nhầm 10 sản phẩm mà không một dòng lỗi nào.
    // ══════════════════════════════════════════════════════════════════════════════════════════════════
    const vanTruocDo = await vanTay();
    const bamDo = await chay(pageBamSapXepDoanhSo, ["down"]);
    const M3 = await doMoc("M3 (sau cú bấm down thứ BA — CHỈ ĐỂ ĐO)", vanTruocDo);

    anSo.sapXepDoanhSo = {
      ghiChu: "BƯỚC ĐO. Chu kỳ 3 trạng thái theo người dùng: chưa sắp xếp → up → down → chưa sắp xếp. Đúng "
        + SO_LAN_BAM_SAP_XEP + " cú bấm 'down' = giảm dần. M3 là cú 'down' THỨ BA, CHỈ ĐỂ ĐO — câu hỏi: M3 có "
        + "quay về giống hệt M0 không. TRỌNG TÀI là thứ tự mã sản phẩm (`ma`); class nút KHÔNG mang tín hiệu "
        + "(số đo lượt 9: clsIcon/clsCum giống hệt ở cả bốn mốc, ariaSort null).",
      soCuBamThat: bam.length,
      M0, M1, M2, M3,
      bam, bamDo,
    };

    return {
      sau: { choBangSanPham: choBang, dumpModal, M0, M1, M2, M3, bam, bamDo },
      ghiChu: "Trọng tài là THỨ TỰ MÃ SẢN PHẨM (`ma`), không phải class nút. Đã bấm đúng "
        + SO_LAN_BAM_SAP_XEP + " cú 'down' cho pha sắp xếp thật, cộng 1 cú 'down' THỨ BA CHỈ ĐỂ ĐO "
        + "(kiểm chu kỳ 3 trạng thái).",
      kyVong: "co", docLai: dumpModal && dumpModal.coModal ? "co" : "khong",
    };
  }, tabId, NHAN);
  if (!b9.ok) { return ketThuc(); }

  // 10) HỦY ra khỏi modal sản phẩm — LỐI RA DUY NHẤT. Không bấm gì tạo thật.
  await buoc(ds, "10. HỦY modal sản phẩm (KHÔNG tạo gì)", async () => {
    const huy = await chay(pageHuyModalSanPham, []);
    let conModal = 1;
    for (let i = 0; i < 20; i++) {
      await sleep(400);
      conModal = await chay(pageDemPhanTuHien, ["tr.mkt-product-selector-row-in-root"]);
      if (conModal === 0) { break; }
    }
    return {
      sau: { huy, soDongConLai: conModal },
      loi: huy && huy.ok ? null : (huy && huy.loi) || "không hủy được modal",
      ghiChu: "Lượt dò kết thúc ở đây. KHÔNG bấm nút Xác nhận nào của trang tạo chương trình.",
      kyVong: "dong", docLai: conModal === 0 ? "dong" : "con-mo",
    };
  }, tabId, NHAN);

  return ketThuc();
}

/**
 * Lệnh `dumpFlashSale` — nút DÒ (chẩn đoán) của "Flash Sale Của Shop".
 * <b>CHỈ ĐỌC: không tạo chương trình nào</b> (xem khối ràng buộc đầu file).
 */
export async function doDumpFlashSale(shop) {
  const kq = await doDomFlashSaleChoTabDangMo();
  send({
    action: "pageData",
    kind: "flashDump",
    data: JSON.stringify({
      luc: new Date().toISOString(),
      luong: "flashsale",
      // MỖI BẢN DUMP TỰ KHAI NÓ ĐO SHOP NÀO. Không có trường này thì đọc lại một bản dump cũ là phải đoán,
      // mà cả kết luận "toàn 0 doanh số" lẫn "thứ tự đổi/không đổi" đều phụ thuộc hẳn vào shop nào.
      shop: shop || null,
      anSo: kq.anSo,
      ketQua: { captcha: !!kq.captcha },
      buoc: kq.buoc,
    }, null, 2),
  });
}

// ══════════════════════════════════════════════════════════════════════════════════════════════════════════
// LUỒNG THẬT — `chayFlashSale`. Từ đây xuống là phần TẠO CHƯƠNG TRÌNH THẬT trên shop của người dùng.
//
// ⚠ RANH GIỚI VỚI LƯỢT DÒ Ở TRÊN, ĐỌC TRƯỚC KHI SỬA:
//  · Lượt dò ({@link doDomFlashSaleChoTabDangMo}) kết thúc bằng HỦY và có CÚ BẤM SẮP XẾP THỨ BA chỉ để đo chu
//    kỳ ba trạng thái. **Luồng thật KHÔNG được chép cú thứ ba đó** — nó xoá sạch sắp xếp và luồng sẽ chọn 10
//    sản phẩm tuỳ ý thay vì 10 sản phẩm bán chạy nhất, mà mọi bước sau vẫn `ok`.
//  · Luồng thật dùng ĐÚNG {@link SO_LAN_BAM_SAP_XEP} = 2 cú, và có thêm một CỬA KIỂM mà lượt dò không có:
//    {@link dayDoanhSoKhongTangDan}.
//
// SỐ ĐO CHỐT LUẬT NÀY — lượt dò 11 (Minoa Store, 28/08/2026 23:02), cả ba mốc `nguonTinHieu = "lop-phu"`:
//     M0 (chưa bấm) : 1,0,0,0,0,0,0,0,0,0     ← thứ tự mặc định
//     M1 (1 cú down): 0,0,0,0,0,0,0,0,0,0     ← TĂNG DẦN
//     M2 (2 cú down): 14,4,4,4,3,3,2,2,2,2    ← GIẢM DẦN  ✅ đây là trạng thái đích
//     M3 (3 cú down): 1,0,0,…  + mã GIỐNG HỆT M0          ← cú thứ ba xoá sạch, đúng lời người dùng
// ⇒ 2 cú = giảm dần = bán chạy nhất trước. Lớp phủ `.eds-table__loading` CÓ chạy thật ⇒ `choBangSauBamSapXep`
//   dùng được nguyên, không cần tầng chờ thứ ba.
// ══════════════════════════════════════════════════════════════════════════════════════════════════════════

/// Trần số vòng khung — **CHỐT KỸ THUẬT CHỐNG VÒNG VÔ TẬN, KHÔNG PHẢI TRẦN NGHIỆP VỤ.**
///
/// Người dùng chốt 03/09/2026: *"bỏ trần 8 khung, lấy hết khung Shopee mở"*. Điểm dừng THẬT của vòng là
/// `hetKhung` — {@link khungChuaDung} trả `chiSo: -1` khi Shopee không còn khung nào chọn được cho ngày đích
/// (khung đã dùng bị Shopee khoá ngay, nên số khung còn lại giảm đơn điệu). Trần dưới đây chỉ đứng đó cho ca
/// memo `khungDaDung` hỏng (Shopee đổi `value` radio giữa lượt ⇒ khung vừa dùng lại trông như chưa dùng): một
/// NGÀY không thể có quá 24 khung giờ, nên chạm 24 là bằng chứng memo hỏng chứ không phải shop nhiều khung.
/// Chạm trần ⇒ đẩy một câu vào `canhBao`, không im lặng.
const TRAN_VONG_KHUNG_KY_THUAT = 24;

/// Số sản phẩm MỘT TRANG của modal chọn sản phẩm — đo thật: **10 sp/trang**, khớp dòng "Số sản phẩm tham gia 10".
/// Chỉ dùng để ƯỚC khi không đếm được số dòng đã tick; con số ĐI VÀO kết quả luôn là số đếm THẬT.
const SO_SP_MOI_TRANG = 10;

/// Trần số nhịp (×{@link LANG_NHIP_MS}) chờ modal sản phẩm ĐÓNG sau khi bấm Xác nhận, và chờ trang rời form
/// sau khi bấm nút TẠO THẬT (×500ms).
const CHO_DONG_MODAL_NHIP = 40;
const CHO_ROI_FORM_NHIP = 120;

/// Số nhịp (×500ms ⇒ **2 phút**) chờ dialog "Tạo thành công" nở ở bước K*.7.
///
/// ⚠ CON SỐ NÀY LÀ MỘT HỒI QUY ĐÃ ĐO, ĐỪNG HẠ LẠI. Commit **f9e118d** (31/08/2026 19:35) hạ 240 → 20 nhịp; từ
/// đó **9/9 lượt shop** (01/09 15:19–15:33, 02/09 01:2x, 03/09 01:07–01:21) chết ở `K2.2` với câu
/// *"không thấy ô ngày 'N' đang ĐỎ (tổng 35 ô, 0 ô đỏ)"* — mỗi shop chỉ tạo được ĐÚNG 1 khung/ngày. Lượt
/// 31/08 19:10–19:25 với 240 nhịp tạo được liền 4 khung (`K2.6 Trang 2`, `K3.6 Trang 3`, `K4.6 Trang 4`).
/// Giữa hai mốc extension không đổi gì khác.
///
/// Cơ chế (giả thuyết, khớp mọi số đo): khoảng bị cắt ngắn là **K*.3 (xác nhận khung — Shopee bắt đầu dựng
/// chương trình nháp NGẦM phía server) → K*.7 (Kích hoạt hàng loạt)**: 2'15" xuống còn 30s. Kích hoạt lên một
/// bản nháp chưa dựng xong ⇒ lịch của shop mất sạch `div.timeslots` ở khung kế ⇒ không còn ô ngày đỏ nào.
/// Khoảng "kích hoạt → mở lịch khung kế" đo được ~60s ở CẢ HAI bên, nên đây không phải "Shopee cần nghỉ".
///
/// Lý do f9e118d cắt (8 khung × 3,5' vượt trần chờ C# 900s) nay không còn: `ChoChang.FlashChay` = 2700s, đủ
/// cho ~10–12 khung ở 3,5–4,5'/khung.
const CHO_DIALOG_TAO_NHIP = 240;

/// Số nhịp (×500ms ⇒ **10s**) chờ ô NGÀY ĐÍCH chuyển ĐỎ ở bước K*.2 trước khi kết luận "Shopee chưa mở ngày".
///
/// Lưới ô ngày render TRƯỚC khi dữ liệu khung của tháng về, nên ngay sau khi modal mở có một khoảnh khắc
/// "35 ô, 0 ô đỏ" hoàn toàn bình thường. Khung ≥2 của cùng một shop KHÔNG đọc lại lịch (`phaiDocLich = false`,
/// quyết định "đọc lịch một lần mỗi shop") nên trước bản này nó bấm NGAY khi lưới vừa vẽ xong.
///
/// ⚠ Đây KHÔNG phải lượt đọc lại lịch: chỉ hỏi đúng một câu "ô ngày đích đã đỏ chưa" qua
/// {@link pageClickNgayFlash} (hàm chỉ `click()` khi tìm thấy ô đỏ, nên gọi lặp không bấm thừa). Tuyệt đối
/// không thêm `choLichLang` ở đây.
const CHO_O_NGAY_DO_NHIP = 20;

/**
 * PURE — **CỬA KIỂM CỦA LUỒNG THẬT: dãy Doanh số đọc được có KHÔNG TĂNG DẦN không.**
 *
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 * Đây là thứ THAY cho cửa kiểm dựa trên class nút (đã BỎ HẲN — số đo lượt 9 cho thấy EDS không gắn class
 * trạng thái sắp xếp, xem plan §3.1). Cửa này đọc được vì lượt 11 đã đo ra một shop CÓ doanh số thật:
 * hai cú bấm `down` cho `14,4,4,4,3,3,2,2,2,2`.
 *
 * LUẬT: **có BẤT KỲ cặp liền kề nào TĂNG ⇒ HỎNG.** Ba ca, đủ cả ba:
 *  · giảm dần (`14,4,4,…`) ⇒ ĐẠT — đúng trạng thái đích;
 *  · tăng dần (`0,…,14`) ⇒ HỎNG — mới bấm MỘT cú, hoặc cú bấm không ăn;
 *  · **toàn 0 (hoặc toàn bằng nhau) ⇒ ĐẠT** — mọi giá trị bằng nhau thì không có cặp nào tăng, và đó là câu
 *    trả lời ĐÚNG chứ không phải "không kiểm chứng được": với dữ liệu ấy tăng dần và giảm dần là cùng một
 *    thứ tự, nên không có gì để phân biệt và cũng không có gì bị chọn sai.
 *
 * ⚠ KHÔNG có nhánh "bấm thêm một cú cho chắc". Trạng thái "như chưa bấm gì" phải bị chấm HỎNG, tuyệt đối
 * không phải "chưa xong" — xem {@link SO_LAN_BAM_SAP_XEP}, cú thứ ba là tai hoạ âm thầm.
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 *
 * Chuỗi Shopee trả về có dấu phân cách hàng nghìn (`"1.234"`, `"1,234"`) và có thể rỗng; luật đọc số là: giữ
 * lại CHỮ SỐ, chuỗi không còn chữ số nào ⇒ bỏ dòng đó khỏi phép so (không coi là 0 — `null` là "không đọc
 * được", mà đoán nó bằng 0 là tự chế ra một cặp tăng/giảm không có thật).
 *
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 * ⚠ CỬA NÀY **FAIL-CLOSED**: KHÔNG ĐỌC ĐƯỢC ⇒ **HỎNG**, không phải "đạt vì không có gì để so".
 *
 * Bản đầu (28/08/2026) nhận MỖI mảng `giaTri` và trả `dat:true` khi mảng rỗng — tức cửa dựng ra để chặn "ca sai
 * âm thầm nguy hiểm nhất của luồng này" lại TRƯỢT ở đúng ca không đọc được. {@link pageDocDoanhSoMoiDongFlash}
 * có **BỐN** lối ra trả `giaTri: []`: chưa cài luật · không thấy modal · không thấy cột "Doanh số" · không thấy
 * bảng THÂN có dòng. Cả bốn đều dẫn thẳng tới nút TẠO THẬT với 10 sản phẩm TUỲ Ý, `daTao:true`, nhật ký xanh.
 *
 * Ca cụ thể đã chỉ ra: chỉ số cột lấy từ bảng **HEADER** (EDS tách header thành `<table>` riêng — chính hàm đọc
 * khai cờ `headerLaBangRieng`) rồi áp vào bảng **THÂN**. Hai bảng không bảo đảm cùng bộ cột; lệch một cột là
 * đọc trúng ô không có chữ số ⇒ mảng rỗng ⇒ cửa cũ cho ĐẠT.
 *
 * BỐN LUẬT HỎNG, theo đúng thứ tự kiểm:
 *  1. `coCot !== true` — không đọc được cột Doanh số (ba lối ra đầu);
 *  2. `soDong < 1` — không thấy dòng nào ở bảng thân, TRONG KHI bước trước vừa chờ được dòng hiện ra;
 *  3. `soDong >= 2` mà đọc được **dưới 2** giá trị có chữ số — dấu hiệu kinh điển của lệch cột;
 *  4. có BẤT KỲ cặp liền kề nào TĂNG.
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 *
 * @param {{coCot:boolean, soDong:number, giaTri:Array<string|number|null>, loi:string|null}} doc Gói TRỌN VẸN
 *   do {@link pageDocDoanhSoMoiDongFlash} trả về. <b>Đừng bóc `giaTri` ra rồi truyền mảng trần</b> — đó chính
 *   là cách bản đầu đánh rơi `coCot`/`loi` và thành fail-open.
 * @returns {{dat:boolean, so:Array<number>, cap:number, soDong:number, ly:string}}
 */
export function dayDoanhSoKhongTangDan(doc) {
  const d = doc || {};
  const soDong = typeof d.soDong === "number" && isFinite(d.soDong) ? d.soDong : -1;
  const cauLoi = d.loi ? " (" + d.loi + ")" : "";

  if (d.coCot !== true) {
    return { dat: false, so: [], cap: -1, soDong,
             ly: "KHÔNG đọc được cột Doanh số" + cauLoi + " — không có căn cứ nào để nói 10 sản phẩm sắp nạp "
               + "là 10 sản phẩm bán chạy nhất. Cửa này FAIL-CLOSED: không đọc được là HỎNG." };
  }
  if (soDong < 1) {
    return { dat: false, so: [], cap: -1, soDong,
             ly: "bảng THÂN không có dòng sản phẩm nào" + cauLoi + " — trong khi bước trước vừa chờ được dòng "
               + "hiện ra, tức chỉ số bảng/cột đang lệch" };
  }

  const so = (Array.isArray(d.giaTri) ? d.giaTri : [])
    .map((x) => {
      if (typeof x === "number") { return isFinite(x) ? x : null; }
      const chuoi = String(x === null || x === undefined ? "" : x).replace(/[^\d]/g, "");
      return chuoi.length === 0 ? null : Number(chuoi);
    })
    .filter((n) => n !== null);

  if (soDong >= 2 && so.length < 2) {
    return { dat: false, so, cap: -1, soDong,
             ly: "bảng có " + soDong + " dòng mà chỉ đọc được " + so.length + " giá trị Doanh số có chữ số"
               + cauLoi + " — nhiều khả năng CHỈ SỐ CỘT LỆCH (header là <table> riêng với bảng thân)" };
  }
  if (so.length < 2) {
    return { dat: true, so, cap: -1, soDong,
             ly: "bảng chỉ có " + soDong + " dòng ⇒ đọc được " + so.length + " giá trị — không có cặp nào để "
               + "so, và cũng không có gì bị chọn sai" };
  }
  for (let i = 0; i + 1 < so.length; i++) {
    if (so[i] < so[i + 1]) {
      return {
        dat: false, so, cap: i, soDong,
        ly: "dãy Doanh số TĂNG ở cặp thứ " + (i + 1) + " (" + so[i] + " < " + so[i + 1]
          + ") — bảng đang sắp xếp TĂNG DẦN hoặc cú bấm không ăn; 10 sản phẩm của trang này KHÔNG phải 10 sản "
          + "phẩm bán chạy nhất",
      };
    }
  }
  const bang = so.every((n) => n === so[0]);
  return {
    dat: true, so, cap: -1, soDong,
    ly: bang
      ? "cả " + so.length + " giá trị Doanh số BẰNG NHAU (" + so[0] + ") — tăng dần và giảm dần là cùng một "
        + "thứ tự, không có gì bị chọn sai ⇒ ĐẠT"
      : "dãy Doanh số KHÔNG tăng dần (" + so.slice(0, 4).join(",") + "…) — đang là cao→thấp",
  };
}

/**
 * PURE — **CỔNG "được phép rời trang 1 chưa"**: bước F chỉ được đụng nút sang trang khi cửa kiểm sắp xếp của
 * TRANG 1 (bước E) đã ĐẠT, và cổng đòi thấy TẬN MẮT kết quả cửa đó.
 *
 * <p>Trước bản này thứ tự "sắp xếp trước → lật sau" chỉ được giữ bằng VỊ TRÍ DÒNG CODE: bước F không đòi bằng
 * chứng nào rằng bước E đã chạy và đã đạt. Đảo hai bước, bỏ bước E, hay thêm một nhánh đi tắt thì không lưới
 * nào đỏ. Cổng này biến quy ước ấy thành RÀNG BUỘC: <b>quên gọi cửa cũng bị chặn</b>, không riêng cửa đỏ —
 * `cuaTrang1` còn `null` (bước E chưa chạy) ra đúng cùng một câu từ chối như cửa đỏ.</p>
 *
 * <p>`soCuoi` là mắt xích đầu của chuỗi kiểm giữa các trang: giá trị Doanh số <b>ĐỌC ĐƯỢC CUỐI CÙNG</b> của
 * trang 1 — phần tử cuối của dãy ĐÃ LỌC (`so`), tức đã bỏ mọi ô không có chữ số. <b>Không nhất thiết là giá trị
 * của DÒNG CUỐI bảng</b>: dòng cuối có ô Doanh số trống thì mắt xích lùi lên dòng đọc được gần nhất. Trang 2 mở
 * cao hơn nó nghĩa là thứ tự đã đứt ở biên trang — xem {@link cuaKiemTrangSauLat}.</p>
 *
 * @param {{dat:boolean, so:Array<number>, cap:number, soDong:number, ly:string}|null} cuaTrang1 NGUYÊN kết quả
 *   {@link dayDoanhSoKhongTangDan} trả cho trang 1. Trọn gói, đừng bóc `dat` ra truyền riêng.
 * @returns {{dat:boolean, soCuoi:number|null, ly:string}}
 */
export function congLatTrang(cuaTrang1) {
  const c = cuaTrang1 || {};
  if (c.dat !== true) {
    return {
      dat: false, soCuoi: null,
      ly: "chưa có cửa kiểm sắp xếp ĐẠT cho trang 1 — KHÔNG lật trang"
        + (c.ly ? " (" + c.ly + ")" : " (bước cửa kiểm chưa chạy, hoặc không trả về kết quả nào)"),
    };
  }
  const so = Array.isArray(c.so) ? c.so : [];
  const soCuoi = so.length > 0 ? so[so.length - 1] : null;
  return {
    dat: true, soCuoi,
    ly: soCuoi === null
      ? "cửa kiểm sắp xếp trang 1 ĐẠT nhưng không đọc được giá trị nào — không có mắt xích để nối sang trang sau"
      : "cửa kiểm sắp xếp trang 1 ĐẠT, trang 1 kết ở " + soCuoi,
  };
}

/**
 * PURE — **cửa kiểm CHỈ-ĐỌC cho trang VỪA ĐÁP XUỐNG sau một cú lật**: sort còn đứng không.
 *
 * <p>Cửa của bước E chỉ chấm TRANG 1. Nếu sắp xếp tụt mất trong lúc phân trang thì trang k bị tick 10 sản phẩm
 * SAI trong im lặng — mọi bước vẫn `ok`, nhật ký vẫn xanh. Cửa này chấm hai vế:</p>
 * <ol>
 *   <li><b>trong-trang</b> — gọi CHÍNH {@link dayDoanhSoKhongTangDan}, một nguồn luật duy nhất. Không chép lại
 *   phép parse/so sánh lần hai: sửa luật ở đó là cửa này đổi theo ngay.</li>
 *   <li><b>nối tiếp giữa trang</b> — trang này mở CAO HƠN chỗ trang trước kết là thứ tự đã đứt ở biên trang,
 *   dù trong từng trang vẫn giảm dần đẹp.</li>
 * </ol>
 *
 * <p>⚠ Điều kiện chạy vế nối tiếp kiểm <b>`typeof === "number"`, TUYỆT ĐỐI không kiểm truthy</b>: `0` là giá
 * trị hợp lệ và phổ biến nhất của cột Doanh số, `if (soCuoiTrangTruoc)` nuốt sạch nó và cửa thành fail-open ở
 * đúng ca hay gặp nhất.</p>
 *
 * <p>Nói cho đúng bản chất phép đo: cửa này phát hiện <b>bằng chứng NGƯỢC với giảm dần</b>. Dãy tình cờ
 * không-tăng (nhiều giá trị bằng nhau) không phân biệt được với sort thật — nhưng với dữ liệu ấy mọi thứ tự là
 * như nhau nên cũng không có gì bị chọn sai. Cửa xanh KHÔNG phải bằng chứng dương tuyệt đối.</p>
 *
 * @param {{coCot:boolean, soDong:number, giaTri:Array<string|number|null>, loi:string|null}} docTrangNay Gói
 *   TRỌN VẸN {@link pageDocDoanhSoMoiDongFlash} đọc được ở trang vừa đáp.
 * @param {number|null} soCuoiTrangTruoc Giá trị Doanh số <b>ĐỌC ĐƯỢC CUỐI CÙNG</b> của trang trước — phần tử
 *   cuối của dãy ĐÃ LỌC, <b>không nhất thiết là giá trị của dòng cuối bảng</b>. Không phải số (mắt xích đầu)
 *   thì chỉ chấm vế trong-trang.
 * @returns {{dat:boolean, soCuoi:number|null, viThuTu:boolean|null, ly:string}} `viThuTu` chỉ có nghĩa khi
 *   `dat === false`: `true` = hỏng vì THỨ TỰ (cặp tăng trong trang, hoặc trang này mở cao hơn chỗ trang trước
 *   kết), `false` = hỏng vì KHÔNG ĐỌC ĐƯỢC (mất cột / 0 dòng / lệch cột). Hai loại hỏng ấy chỉ về hai thủ phạm
 *   khác hẳn nhau, nên câu lỗi bước F phải chọn lời theo cờ này thay vì đổ hết cho cú bấm sắp xếp. `dat === true`
 *   ⇒ `null` (không có nghĩa).
 */
export function cuaKiemTrangSauLat(docTrangNay, soCuoiTrangTruoc) {
  const con = dayDoanhSoKhongTangDan(docTrangNay);
  const so = Array.isArray(con.so) ? con.so : [];
  if (con.dat !== true) {
    // `cap` là chỉ số cặp TĂNG mà cửa con tìm được ⇒ `>= 0` nghĩa là hỏng vì THỨ TỰ. Ba lối hỏng còn lại của
    // cửa con (mất cột / 0 dòng / lệch cột) đều trả `cap = -1`: đó là hỏng vì KHÔNG ĐỌC ĐƯỢC, đổ cho cú bấm
    // sắp xếp là chỉ sai thủ phạm cho người vận hành.
    return { dat: false, soCuoi: null, viThuTu: typeof con.cap === "number" && con.cap >= 0, ly: con.ly };
  }

  // ⚠ `typeof`, KHÔNG truthy — xem khối chú thích trên. `0` là giá trị hợp lệ.
  const coMatXich = typeof soCuoiTrangTruoc === "number" && isFinite(soCuoiTrangTruoc);
  if (coMatXich && so.length > 0 && so[0] > soCuoiTrangTruoc) {
    return {
      dat: false, soCuoi: null, viThuTu: true,
      ly: "trang trước kết ở " + soCuoiTrangTruoc + " mà trang này mở ở " + so[0] + " > " + soCuoiTrangTruoc
        + " — thứ tự giảm dần đã đứt giữa hai trang",
    };
  }

  // Trang ĐẠT mà không đọc được số nào (trang cuối một dòng, ô trống) ⇒ KẾ THỪA mắt xích cũ. Viết tường minh
  // bằng `so.length`, KHÔNG `|| null`: `so[so.length-1]` bằng `0` là giá trị THẬT, `||` sẽ vứt nó đi.
  const soCuoi = so.length > 0 ? so[so.length - 1] : (coMatXich ? soCuoiTrangTruoc : null);
  return { dat: true, soCuoi, viThuTu: null, ly: con.ly };
}

/**
 * PURE — **đã đứng ĐÚNG trang k chưa** trước khi tick "chọn tất cả".
 *
 * <p>Trước bản này bước F chỉ đếm số cú lật THÀNH CÔNG, không ai hỏi cuối cùng đang đứng ở trang nào. Tách
 * THUẦN vì đây là một quyết định bốn nhánh: viết inline trong thân bước F thì không lưới nào chạm tới được.</p>
 *
 * <p>Nhãn trang active (`active|current|selected`) là điểm markup MỎNG NHẤT của cả luồng, nên luật cố ý lệch:
 * <b>chỉ đỏ khi nhãn là SỐ THUẦN và LỆCH; mọi ca còn lại ⇒ cảnh báo, không đỏ</b>. Đỏ ở ca không đọc được là đỏ
 * oan theo markup, trong khi hai lưới nặng hơn (bảng phải ĐỔI sau cú lật + cửa Doanh số từng trang) đã đứng
 * trước rồi.</p>
 *
 * <p>⚠ <b>Vì sao đòi SỐ THUẦN chứ không so thẳng chuỗi:</b> chưa có số đo nào về định dạng nhãn pager của
 * Shopee — `"Trang 3"`, `"3/8"`, `"‹ 3 ›"` đều khả dĩ. So thẳng `!== String(trangDich)` thì mọi định dạng ngoài
 * số trơn đều thành ĐỎ, tức nhãn "đọc được mà lạ" bị xử NGHIÊM HƠN nhãn rỗng — trong khi cả hai đều là "không
 * biết đang ở trang nào". Không có bằng chứng thì không kết tội: chưa parse được ⇒ cảnh báo, và câu cảnh báo
 * nêu NGUYÊN VĂN nhãn đọc được để lượt sau có số đo mà siết cho đúng.</p>
 *
 * @param {string|null} trangHienTai Nhãn trang đang active, do `pageDemTickFlash` đọc.
 * @param {number} trangDich Trang phải đứng (khung thứ k ⇒ trang k).
 * @returns {{dat:boolean, canhBao:string|null, ly:string}}
 */
export function cuaDungTrangDich(trangHienTai, trangDich) {
  if (trangDich === 1) {
    return { dat: true, canhBao: null, ly: "trang 1 — không lật đi đâu, cửa kiểm Doanh số vừa chấm xong" };
  }
  const nhan = trangHienTai === null || trangHienTai === undefined ? "" : String(trangHienTai).trim();
  // CHỈ nhãn số thuần mới đủ tư cách kết tội — xem khối chú thích trên.
  if (/^\d+$/.test(nhan)) {
    if (nhan === String(trangDich)) {
      return { dat: true, canhBao: null, ly: "đang đứng đúng trang " + trangDich };
    }
    return {
      dat: false, canhBao: null,
      ly: "đứng ở trang " + nhan + ", không phải trang k=" + trangDich + " — không tick",
    };
  }
  if (nhan.length === 0) {
    return {
      dat: true,
      canhBao: "không đọc được nhãn trang active — đã có hai lưới khác (bảng ĐỔI + cửa Doanh số) đứng trước",
      ly: "không đọc được nhãn trang active, không đủ căn cứ để nói đang đứng sai — KHÔNG đỏ ở đây",
    };
  }
  return {
    dat: true,
    canhBao: "nhãn trang active đọc được là \"" + nhan + "\" — KHÔNG phải số thuần nên không so được với trang "
      + "k=" + trangDich + "; đã có hai lưới khác (bảng ĐỔI + cửa Doanh số) đứng trước",
    ly: "nhãn trang active là \"" + nhan + "\", không phải số thuần — chưa có số đo nào về định dạng nhãn pager "
      + "nên KHÔNG đủ căn cứ nói đang đứng sai trang " + trangDich,
  };
}

/**
 * PURE — **khung giờ nào của ngày đích còn CHƯA DÙNG trong lượt này.**
 *
 * <p>"Ghi nhớ trong lượt" là thứ DUY NHẤT luồng này phải nhớ (plan §4) — và nó sống trong bộ nhớ của lượt,
 * <b>KHÔNG ghi DB</b>. Một con trỏ tiến độ dưới DB sẽ trôi lệch ngay khi một máy bỏ lỡ một đêm.</p>
 *
 * <p>Loại cả khung `disabled` (Shopee đã khoá) lẫn khung không đọc được `value` — không có `value` thì không
 * có cách nào ghi nhớ là đã dùng, và bấm mù là bấm lại đúng khung vừa tạo.</p>
 *
 * @param {Array<{chiSo:number, value:string|null, disabled:boolean, nhan:string|null}>} dsKhung
 * @param {Array<string>} daDung Các `value` đã dùng trong lượt.
 * @returns {{chiSo:number, value:string|null, nhan:string|null, ly:string}}
 */
export function khungChuaDung(dsKhung, daDung) {
  const ds = Array.isArray(dsKhung) ? dsKhung : [];
  const da = Array.isArray(daDung) ? daDung.map((v) => String(v)) : [];
  for (const k of ds) {
    if (!k || k.disabled === true) { continue; }
    const v = k.value === null || k.value === undefined ? null : String(k.value);
    if (v === null || v.length === 0) { continue; }
    if (da.indexOf(v) >= 0) { continue; }
    return { chiSo: k.chiSo, value: v, nhan: k.nhan === undefined ? null : k.nhan,
             ly: "khung '" + (k.nhan || v) + "' chưa dùng trong lượt này" };
  }
  return {
    chiSo: -1, value: null, nhan: null,
    ly: "hết khung chưa dùng (tổng " + ds.length + " khung, đã dùng " + da.length + ")",
  };
}

/**
 * PURE — **SỐ TRANG THẬT của modal sản phẩm**, đọc từ dãy nhãn pager (`nhanCacTrang` của
 * {@link pageDemTickFlash}).
 *
 * <p>Pager EDS vẽ dạng <code>1 2 3 … 12</code>: nhãn CUỐI là tổng số trang, còn ở giữa có ô rút gọn
 * (<code>"..."</code>) và nút mũi tên (<code>"‹"</code>, <code>"›"</code>) không phải số. Luật vì thế là
 * <b>lấy SỐ NGUYÊN LỚN NHẤT trong dãy</b>, không phải phần tử cuối — phần tử cuối có thể là mũi tên.</p>
 *
 * <p>Không có số nào đọc được ⇒ <b>1</b>, chứ không phải 0: shop chỉ có một trang có thể KHÔNG render pager,
 * và "0 trang" sẽ làm {@link trangChoKhung} chia cho 0.</p>
 *
 * @param {Array<string>|null|undefined} nhanCacTrang Nhãn nguyên văn của mọi `li.eds-pager__page`.
 * @returns {number} Số trang ≥ 1.
 */
export function soTrangTuNhan(nhanCacTrang) {
  const ds = Array.isArray(nhanCacTrang) ? nhanCacTrang : [];
  let max = 0;
  for (const nhan of ds) {
    const chu = String(nhan === null || nhan === undefined ? "" : nhan).trim();
    if (!/^\d+$/.test(chu)) { continue; }
    const n = parseInt(chu, 10);
    if (isFinite(n) && n > max) { max = n; }
  }
  return max >= 1 ? max : 1;
}

/**
 * PURE — **khung thứ k của ngày ⇒ TRANG nào** trong modal sản phẩm (đếm từ 1), QUAY VÒNG theo số trang thật.
 *
 * <p>Người dùng chốt 27/08: mỗi khung lấy MỘT trang khác nhau để các khung của một ngày phủ nhiều sản phẩm
 * bán chạy nhất thay vì cùng một 10 sản phẩm. Chỉ số trang quay về 1 ở mỗi NGÀY — mà bản nhật-ngày chỉ nhắm
 * đúng một ngày, nên "quay về 1" chính là việc lượt sau bắt đầu với <c>soKhungDaTao = 0</c>.</p>
 *
 * <p><b>QUAY VÒNG (chốt 03/09/2026)</b>: <i>"khung 1 → page 1, khung 2 → page 2 … nếu chỉ có 2–3 page mà 8
 * khung thì khung 4 quay lại page 1, khung 5 quay lại page 2"</i>. Bản trước trả thẳng <c>n + 1</c> mà không
 * biết shop có mấy trang: shop 3 trang tới khung 4 là bước F lật quá trang ⇒ <c>pageSangTrangSauFlash</c>
 * <c>ok:false</c> ⇒ bước ĐỎ ⇒ <c>ketThuc</c> ⇒ MẤT nốt mọi khung còn lại của shop. Với 3 trang, 8 khung ra
 * dãy <c>1,2,3,1,2,3,1,2</c>.</p>
 *
 * <p><c>soTrang</c> không đọc được (0, âm, không hữu hạn) ⇒ coi như <b>1 trang</b>: fail-safe về phía KHÔNG
 * LẬT, vì lật quá trang là bước đỏ còn tick lại trang 1 chỉ là trùng sản phẩm.</p>
 *
 * @param {number} soKhungDaTao Số khung ĐÃ TẠO ĐƯỢC của ngày này (0 ở khung đầu).
 * @param {number} soTrang Số trang THẬT của modal, do {@link soTrangTuNhan} đọc.
 */
export function trangChoKhung(soKhungDaTao, soTrang) {
  const n0 = typeof soKhungDaTao === "number" && isFinite(soKhungDaTao) ? Math.floor(soKhungDaTao) : 0;
  const n = n0 < 0 ? 0 : n0;
  const t0 = typeof soTrang === "number" && isFinite(soTrang) ? Math.floor(soTrang) : 1;
  const t = t0 < 1 ? 1 : t0;
  return (n % t) + 1;
}

/**
 * PURE — đọc gói tham số C# gửi xuống thành thứ luồng dùng được, kèm câu lỗi CÓ TÊN.
 *
 * <p><b>NGÀY ĐÍCH do C# tính MỘT LẦN lúc bắt đầu lượt rồi rót vào gói này cho MỌI shop</b> (plan §4 chốt 1) —
 * extension TUYỆT ĐỐI không tự đọc đồng hồ để suy ra "ngày mai". Một lượt vắt qua nửa đêm là chữ "ngày mai" đổi
 * nghĩa GIỮA LƯỢT: shop 1–8 nhắm `D+1`, shop 9–12 nhắm `D+2`, không lỗi và không log. Từ 01/09/2026 C# chạy
 * thành nhiều GÓI NHỎ cả ngày và không bắt đầu shop mới sau 22:15, nên ca ấy gần như không dựng nổi nữa —
 * chốt vẫn giữ vì nó rẻ, và vì nó là thứ chặn ca "một gói chạy lâu bất thường".</p>
 */
export function docThamSoFlash(ts) {
  const t = ts || {};
  const so = (x) => (typeof x === "number" && isFinite(x) ? Math.floor(x) : Number.NaN);
  const ngay = so(t.ngay);
  const thang = so(t.thang);
  const nam = so(t.nam);
  // HAI THÔNG SỐ BULK (03/09/2026) — thay hai hằng `PHAN_TRAM_GIAM_FLASH`/`SO_LUONG_KM_FLASH` đã xoá.
  const phanTramGiam = so(t.phanTramGiam);
  const soLuongThamGia = so(t.soLuongThamGia);
  const loi = [];
  if (!(ngay >= 1 && ngay <= 31)) { loi.push("ngày đích không hợp lệ: " + JSON.stringify(t.ngay)); }
  if (!(thang >= 1 && thang <= 12)) { loi.push("tháng đích không hợp lệ: " + JSON.stringify(t.thang)); }
  if (!(nam >= 2000 && nam <= 2999)) { loi.push("năm đích không hợp lệ: " + JSON.stringify(t.nam)); }
  // ⚠ FAIL-CLOSED, KHÔNG rơi về 5/10. Biên khớp `KmDialogViewModel.KiemTraFormFlash` (1–99 / 1–9999) — hai bên
  // lệch biên là một con số qua được form rồi chết ở đây, hoặc ngược lại. Thiếu trường (client cũ) rơi vào đúng
  // nhánh này: thà dừng ở bước giả "0. Tham số" còn hơn gửi xuống Shopee một con số người dùng không đặt.
  if (!(phanTramGiam >= 1 && phanTramGiam <= 99)) {
    loi.push("%giảm hàng loạt không hợp lệ: " + JSON.stringify(t.phanTramGiam));
  }
  if (!(soLuongThamGia >= 1 && soLuongThamGia <= 9999)) {
    loi.push("SL sản phẩm tham gia không hợp lệ: " + JSON.stringify(t.soLuongThamGia));
  }
  // ⚠ KHÔNG còn trường `soKhungToiDa` (03/09/2026): số khung là thứ SHOPEE quyết, vòng dừng ở `hetKhung`.
  // Gói C# cũ (bản client chưa cập nhật) vẫn có thể gửi trường đó xuống — cứ BỎ QUA, đừng đọc lại thành trần.
  return {
    ngay, thang, nam, phanTramGiam, soLuongThamGia,
    loi: loi.length ? loi.join(" · ") : null,
    moTa: (ngay >= 1 ? ngay : "?") + "/" + (thang >= 1 ? thang : "?") + "/" + (nam >= 2000 ? nam : "?"),
  };
}

/**
 * PURE — ngày KẾ TIẾP của (ngay, thang, nam) bằng SỐ HỌC THUẦN. Luồng thật bị CẤM đọc đồng hồ
 * ({@link chayFlashSaleChoTabDangMo} — test LuongThat_KhongDocDongHo), và ngày kế của NGÀY ĐÍCH là hàm
 * thuần của tham số C# rót xuống — không phải của "hôm nay".
 */
export function ngayKeTiep(ngay, thang, nam) {
  const nhuan = (n) => (n % 4 === 0 && n % 100 !== 0) || n % 400 === 0;
  const soNgay = [31, nhuan(nam) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][thang - 1];
  if (ngay < soNgay) { return { ngay: ngay + 1, thang, nam }; }
  if (thang < 12) { return { ngay: 1, thang: thang + 1, nam }; }
  return { ngay: 1, thang: 1, nam: nam + 1 };
}

// ── Page-func RIÊNG của luồng thật ──────────────────────────────────────────────────────────────────────────

/**
 * DỌN chương trình flash RỖNG của NGÀY ĐÍCH trên trang danh sách (31/08/2026 — người dùng yêu cầu xoá hộ
 * đống rác các lượt chẩn đoán để lại, và giữ luôn làm cơ chế tự-chữa-lành: form mới của Shopee TẠO chương
 * trình từ bước sớm, nên một lượt hỏng giữa chừng để lại chương trình 0-sản-phẩm-bật CHIẾM KHUNG — lượt
 * sau coi khung đó "đã dùng" và không bao giờ tạo lại được).
 *
 * MỖI CÚ GỌI BẤM XOÁ MỘT HÀNG — hàng ĐẦU TIÊN thoả CẢ BA điều kiện, thiếu một là KHÔNG đụng:
 *   (1) "Bật Flash Sale 0" — 0 sản phẩm đang bật (chương trình rỗng theo định nghĩa);
 *   (2) "Sắp diễn ra" — chưa chạy, không cắt chương trình đang sống;
 *   (3) khung giờ mang đúng NGÀY ĐÍCH (hoặc ngày kế — khung 00:00–02:00 của đêm ngày đích hiển thị ngày hôm sau).
 * Neo từ link "Xóa" leo lên tổ tiên NHỎ NHẤT chứa đúng MỘT chữ "Xóa" — leo quá là vớ nguyên bảng và điều
 * kiện ngày/0-bật của hàng khác trộn vào nhau.
 */
function pageToaDoNutXoaChuongTrinhRong(ngayD1, ngayD2) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const na = (s) => gon(s).toLowerCase();
  // "Hiện" = rect có kích thước thật (đo 14:26–14:33: trang có BẢN SAO ẨN pre-render — chữ "Xóa" của bản
  // ẩn cũng qua cửa `getClientRects().length > 0`, bấm vào hư không suốt 20 cú, hàng rác 9 → 9).
  const hien = (e) => { if (!e) { return false; } const r = e.getBoundingClientRect(); return r.width > 2 && r.height > 2; };
  const dsNutXoa = Array.from(document.querySelectorAll("a, button, span, div"))
    .filter((e) => hien(e) && na(e.textContent) === "xóa" && !e.closest(".eds-modal"));
  let soHangRac = 0;
  let daChon = null, x = null, y = null;
  for (const nutXoa of dsNutXoa) {
    let hang = nutXoa.parentElement;
    for (let i = 0; i < 8 && hang; i++) {
      const t = gon(hang.textContent);
      if ((t.match(/Xóa/g) || []).length > 1 || t.length > 600) { break; } // đã leo quá — sang nút kế
      const dungHang = /Bật Flash Sale\s*0(?!\d)/i.test(t)
        && t.indexOf("Sắp diễn ra") >= 0
        && (t.indexOf(ngayD1) >= 0 || t.indexOf(ngayD2) >= 0);
      if (dungHang) {
        soHangRac++;
        if (!daChon) {
          // TRẢ TOẠ ĐỘ, KHÔNG click (đo 14:26–14:42: cú click DOM thường vào link Xóa bị nuốt — không
          // dialog nào mở, hàng rác 9→9, và rác chiếm hết khung làm 01/09 mất luôn "ngày đỏ". Cú bấm phải
          // là chuột thật qua chrome.debugger, như cú Xác nhận modal sản phẩm).
          const r = nutXoa.getBoundingClientRect();
          x = Math.round(r.left + r.width / 2); y = Math.round(r.top + r.height / 2);
          daChon = t.slice(0, 120);
        }
        break;
      }
      hang = hang.parentElement;
    }
  }
  return { daBam: !!daChon, hang: daChon, x, y, soHangRacThay: soHangRac };
}

/**
 * ĐẾM (tuyệt đối KHÔNG bấm) hàng chương trình của NGÀY ĐÍCH trên trang danh sách — phép ĐỌC LẠI cuối của
 * một khung vừa tạo qua dialog "Tạo thành công": bước DỌN đầu lượt đã xoá sạch hàng 0-bật, nên một hàng
 * 0-bật còn lại lúc này chỉ có thể là của CHÍNH khung vừa tạo — tức cú Bật không ăn tới nơi.
 * Cùng cách neo hàng với {@link pageXoaMotChuongTrinhRong}.
 */
function pageDemChuongTrinhRong(ngayD1, ngayD2) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const na = (s) => gon(s).toLowerCase();
  // "Hiện" = rect có kích thước thật — cùng lý do chống bản-sao-ẩn với {@link pageXoaMotChuongTrinhRong}.
  const hien = (e) => { if (!e) { return false; } const r = e.getBoundingClientRect(); return r.width > 2 && r.height > 2; };
  const dsNutXoa = Array.from(document.querySelectorAll("a, button, span, div"))
    .filter((e) => hien(e) && na(e.textContent) === "xóa" && !e.closest(".eds-modal"));
  let soHangRac = 0, soHangNgayDich = 0;
  const daDem = [];
  for (const nutXoa of dsNutXoa) {
    let hang = nutXoa.parentElement;
    for (let i = 0; i < 8 && hang; i++) {
      const t = gon(hang.textContent);
      if ((t.match(/Xóa/g) || []).length > 1 || t.length > 600) { break; }
      if (/Bật Flash Sale\s*\d/i.test(t) && (t.indexOf(ngayD1) >= 0 || t.indexOf(ngayD2) >= 0)) {
        if (daDem.indexOf(t) < 0) {
          daDem.push(t);
          soHangNgayDich++;
          if (/Bật Flash Sale\s*0(?!\d)/i.test(t) && t.indexOf("Sắp diễn ra") >= 0) { soHangRac++; }
        }
        break;
      }
      hang = hang.parentElement;
    }
  }
  return { soHangNgayDich, soHangRac };
}

/**
 * Toạ độ nút "Chỉnh sửa" của hàng chương trình RỖNG (0 sp bật) ngày đích trên trang danh sách — đường BẬT
 * sản phẩm của form mới (31/08): form create sau khi tạo là XÁC SỐNG (state khung rỗng, toggle khoá vĩnh
 * viễn), phải vào trang CHỈNH SỬA — nó load chương trình từ server nên state khung CHUẨN. Cùng cách neo
 * hàng + điều kiện kép với {@link pageToaDoNutXoaChuongTrinhRong}.
 */
function pageToaDoNutChinhSuaHangRong(ngayD1, ngayD2) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const na = (s) => gon(s).toLowerCase();
  const hien = (e) => { if (!e) { return false; } const r = e.getBoundingClientRect(); return r.width > 2 && r.height > 2; };
  const dsNut = Array.from(document.querySelectorAll("a, button, span, div"))
    .filter((e) => hien(e) && na(e.textContent) === "chỉnh sửa" && !e.closest(".eds-modal"));
  for (const nut of dsNut) {
    let hang = nut.parentElement;
    for (let i = 0; i < 8 && hang; i++) {
      const t = gon(hang.textContent);
      if ((t.match(/Chỉnh sửa/g) || []).length > 1 || t.length > 600) { break; }
      const dungHang = /Bật Flash Sale\s*0(?!\d)/i.test(t)
        && t.indexOf("Sắp diễn ra") >= 0
        && (t.indexOf(ngayD1) >= 0 || t.indexOf(ngayD2) >= 0);
      if (dungHang) {
        // Cuộn hàng vào giữa màn trước khi đo — cùng bài học y-ngoài-viewport 18:38 31/08.
        nut.scrollIntoView({ block: "center", inline: "nearest" });
        const r = nut.getBoundingClientRect();
        return { daBam: true, hang: t.slice(0, 120),
                 x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) };
      }
      hang = hang.parentElement;
    }
  }
  return { daBam: false };
}

/** Bấm "Quay lại trang Danh sách" trong dialog "Tạo thành công" — KHÔNG đụng "Tạo Quảng cáo Shopee" /
 * "Xem chi tiết" (đo 14:00 31/08: dialog gợi ý quảng cáo đứng cạnh nút quay lại). */
function pageBamQuayLaiDanhSach() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  // "Hiện" = rect có kích thước thật — chống bản-sao-ẩn, xem {@link pageXacNhanDialogChonAnh}.
  const hien = (e) => { if (!e) { return false; } const r = e.getBoundingClientRect(); return r.width > 2 && r.height > 2; };
  const nut = Array.from(document.querySelectorAll("button, a"))
    .filter((b) => hien(b) && /quay lại trang danh sách/i.test(b.textContent || ""))
    .find((b) => gon(b.textContent).length < 40);
  if (!nut) { return { ok: false, loi: "không thấy nút 'Quay lại trang Danh sách'" }; }
  nut.click();
  return { ok: true };
}

/** Bấm nút "Xóa" trong DIALOG XÁC NHẬN xoá chương trình (người dùng 31/08: "khi bấm xóa, nó hiện ra 1 hộp
 * thoại, bạn click vào xóa là xóa đc"). Chỉ bấm nút text CHÍNH XÁC "xóa" nằm TRONG modal đang hiện. */
function pageToaDoNutXoaTrongDialog() {
  const na = (s) => (s || "").replace(/\s+/g, " ").trim().toLowerCase();
  // "Hiện" = rect có kích thước thật — chặn shell pre-render ẨN, xem {@link pageXacNhanDialogChonAnh}.
  const hien = (e) => { if (!e) { return false; } const r = e.getBoundingClientRect(); return r.width > 2 && r.height > 2; };
  // Quét cả `.eds-modal` + hộp NHỎ NHẤT — xem ghi chú ở {@link pageXacNhanDialogChonAnh} (đo 14:09: cùng
  // chứng mù selector này làm vòng DỌN bấm Xóa 20 lần mà không xác nhận được hộp thoại nào — K1.1 tốn 2.5').
  // Hộp NHỎ NHẤT **CÓ NÚT** — cùng bẫy body-không-nút với pageXacNhanDialogChonAnh (đo 14:16).
  const timNut = (m) => Array.from(m.querySelectorAll("button, [role='button'], .eds-button, .confirm-btn"))
    .find((b) => hien(b) && na(b.textContent) === "xóa");
  const ungVien = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"))
    .filter(hien)
    .filter((m) => /xóa/i.test(m.textContent || ""))
    .sort((a, b) => (a.textContent || "").length - (b.textContent || "").length);
  if (!ungVien.length) { return { thayDialog: false }; }
  const modal = ungVien.find((m) => timNut(m)) || null;
  if (!modal) { return { thayDialog: true, ok: false, loi: "dialog xoá đang hiện mà không hộp nào chứa nút 'Xóa'" }; }
  // TRẢ TOẠ ĐỘ, KHÔNG click — nút trong dialog nhiều khả năng cùng chứng nuốt-click-thường với link Xóa.
  const r = timNut(modal).getBoundingClientRect();
  return { thayDialog: true, ok: true, x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) };
}

/** Tick ô "chọn tất cả" ở header bảng TRÁI của modal sản phẩm; đọc lại `checked` ngay để bước gọi tự chấm.
 *  ⚠ KHÔNG đụng `label[data-cy="product-modal-available-checkbox"]` — ô đó là BỘ LỌC "Xem sản phẩm có sẵn"
 *  (đo thật: mặc định BẬT, tức đã đúng thứ ta cần), tick nhầm là đổi cả tập sản phẩm. */
function pageTickChonTatCaFlash() {
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  const neo = (m) => m.querySelector('label[data-cy="product-modal-available-checkbox"], tr.mkt-product-selector-row-in-root, .eds-table__sort-icons');
  const modal = ungVien.find((m) => neo(m) && hien(m)) || ungVien.find(neo) || null;
  if (!modal) { return { ok: false, loi: "không thấy modal chọn sản phẩm để tick" }; }
  const trai = modal.querySelector(".eds-table__fix-left");
  const th = trai ? trai.querySelector(".eds-table__header th") || trai.querySelector("th") : null;
  const inp = th ? th.querySelector("input.eds-checkbox__input, input[type=checkbox]") : null;
  if (!inp) {
    return { ok: false, coBangTrai: !!trai, coTh: !!th,
             loi: "không thấy ô 'chọn tất cả' ở header bảng trái của modal sản phẩm" };
  }
  if (inp.disabled) { return { ok: false, loi: "ô 'chọn tất cả' đang disabled" }; }
  // Ô này là CÔNG TẮC: đã tick rồi mà bấm nữa là BỎ tick sạch cả trang.
  if (!inp.checked) { inp.click(); }
  return { ok: true, checked: !!inp.checked, cls: String(inp.className || "") };
}

/** Đếm số dòng ĐÃ TICK / tick được của trang đang hiện (CHỈ ĐỌC) — bằng chứng của cú "chọn tất cả". */
function pageDemTickFlash() {
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  const neo = (m) => m.querySelector('label[data-cy="product-modal-available-checkbox"], tr.mkt-product-selector-row-in-root, .eds-table__sort-icons');
  const modal = ungVien.find((m) => neo(m) && hien(m)) || ungVien.find(neo) || null;
  if (!modal) { return { coModal: false, soDaTick: 0, soTickDuoc: 0, soDong: 0 }; }
  const trai = modal.querySelector(".eds-table__fix-left");
  const goc = trai || modal;
  const dong = Array.from(goc.querySelectorAll("tr.mkt-product-selector-row-in-root"));
  const o = dong.map((r) => r.querySelector("input.eds-checkbox__input, input[type=checkbox]")).filter((x) => x);
  const trang = Array.from(modal.querySelectorAll("li.eds-pager__page"));
  const dangO = trang.find((t) => /active|current|selected/i.test(String(t.className || "")));
  return {
    coModal: true,
    soDong: dong.length,
    soTickDuoc: o.filter((x) => !x.disabled).length,
    soDaTick: o.filter((x) => x.checked).length,
    trangHienTai: dangO ? (dangO.textContent || "").replace(/\s+/g, " ").trim() : null,
    nhanCacTrang: trang.map((t) => (t.textContent || "").replace(/\s+/g, " ").trim()),
  };
}

/** Bấm nút TRANG SAU của pager trong modal sản phẩm. Nút tắt (hết trang) ⇒ `ok:false` kèm `hetTrang:true`. */
function pageSangTrangSauFlash() {
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  const neo = (m) => m.querySelector('label[data-cy="product-modal-available-checkbox"], tr.mkt-product-selector-row-in-root, .eds-table__sort-icons');
  const modal = ungVien.find((m) => neo(m) && hien(m)) || ungVien.find(neo) || null;
  if (!modal) { return { ok: false, loi: "không thấy modal chọn sản phẩm để lật trang" }; }
  const nut = modal.querySelector(".eds-pager__button-next");
  if (!nut) { return { ok: false, khongCoNut: true, loi: "modal không có nút trang sau" }; }
  const tat = !!nut.disabled || /disabled/.test(String(nut.className || ""))
    || nut.getAttribute("aria-disabled") === "true";
  if (tat) { return { ok: false, hetTrang: true, loi: "nút trang sau đang tắt — hết trang" }; }
  if (!hien(nut)) { return { ok: false, loi: "nút trang sau không hiện" }; }
  nut.click();
  return { ok: true, cls: String(nut.className || "") };
}

/**
 * Tìm nút "Xác nhận" ở FOOTER MODAL SẢN PHẨM và trả TOẠ ĐỘ TÂM — chỉ NẠP sản phẩm vào form, CHƯA tạo
 * chương trình. KHÔNG click từ trong trang nữa.
 *
 * ⚠ VÌ SAO TRẢ TOẠ ĐỘ thay vì `nut.click()` (số đo thật 22:42–22:44 30/08/2026, shop Alina Store1): cú
 * `.click()` DOM trúng ĐÚNG nút (`button.eds-button--primary`, text "xác nhận" sạch), modal ĐÓNG, nhưng
 * form nhận **0/10 sản phẩm** dù footer modal vừa vẽ "Đã chọn 10/10 sản phẩm" — tức handler LƯU của Shopee
 * bỏ qua click `isTrusted:false`, còn handler ĐÓNG thì không. Bấm phải là cú chuột thật qua
 * `chrome.debugger` (`trustedClick` — cùng hạ tầng flow-shop dùng hằng ngày).
 *
 * ⚠ CHỐT NHƯ {@link pageBamXacNhanKhungFlash}: neo cứng trong modal sản phẩm và TỪ CHỐI mọi nút nằm trong
 * `.fix-container` (thanh đáy) — nút thanh đáy là nút TẠO THẬT, và bấm nó ở đây là tạo một chương trình
 * CHƯA CÓ sản phẩm nào.
 */
function pageToaDoNutXacNhanModalSp() {
  const na = (s) => (s || "").replace(/\s+/g, " ").trim().toLowerCase();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  const neo = (m) => m.querySelector('label[data-cy="product-modal-available-checkbox"], tr.mkt-product-selector-row-in-root, .eds-table__sort-icons');
  const modal = ungVien.find((m) => neo(m) && hien(m)) || ungVien.find(neo) || null;
  if (!modal) { return { ok: false, loi: "không thấy modal sản phẩm — KHÔNG tìm nút Xác nhận ngoài modal" }; }
  const dsNut = Array.from(modal.querySelectorAll("button")).filter((b) => !b.closest(".fix-container") && !b.closest(".fixed-container"));
  const nut = dsNut.find((b) => na(b.textContent) === "xác nhận")
    || dsNut.find((b) => /eds-button--primary/.test(String(b.className || "")));
  if (!nut) {
    return { ok: false, loi: "không thấy nút Xác nhận trong footer modal sản phẩm",
             coSan: dsNut.map((b) => na(b.textContent)).slice(0, 8) };
  }
  if (nut.closest(".fix-container") || nut.closest(".fixed-container")) {
    return { ok: false, loi: "nút tìm được nằm ở THANH ĐÁY (.fix-container/.fixed-container) — TỪ CHỐI, đó là nút TẠO THẬT" };
  }
  if (nut.disabled) { return { ok: false, loi: "nút Xác nhận của modal sản phẩm đang disabled (chưa tick dòng nào?)" }; }
  if (!hien(nut)) { return { ok: false, loi: "nút Xác nhận của modal sản phẩm không hiện — không có toạ độ để bấm chuột thật" }; }
  const o = nut.getBoundingClientRect();
  const x = Math.round(o.left + o.width / 2), y = Math.round(o.top + o.height / 2);
  // CHẨN ĐOÁN 31/08 (hiện trường 11:09: toast rỗng + modal phụ rỗng + form vẫn 0/10 sau cú bấm trúng
  // "đúng nút" — nghi phạm còn lại là BẤM NHẦM nút Xác nhận KHÁC cùng text trong modal, hoặc toạ độ bị
  // phần tử khác ĐÈ): dump mọi ứng viên + phần tử trên cùng tại đúng toạ độ sắp bấm.
  const ungVienNut = dsNut.filter(hien).map((b) => {
    const r = b.getBoundingClientRect();
    return { text: na(b.textContent).slice(0, 25), cls: String(b.className || "").slice(0, 60),
             x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) };
  }).slice(0, 10);
  const tren = document.elementFromPoint(x, y);
  const phanTuTrenCung = tren
    ? { tag: tren.tagName, cls: String(tren.className || "").slice(0, 60), text: na(tren.textContent).slice(0, 25),
        laNut: tren === nut || nut.contains(tren) }
    : null;
  return { ok: true, x, y, cls: String(nut.className || ""), text: na(nut.textContent),
           ungVienNut, phanTuTrenCung };
}

/**
 * Gõ %GIẢM vào Ô BULK của form flash — neo từ nút "Cập nhật hàng loạt" (khối bulk của flash tên "Chỉnh
 * sửa hàng loạt", KHÔNG phải `.detail-batch` như km — pageGoPhanTram của km rơi vào nhánh MÒ-theo-chữ và
 * có thể gõ trúng ô %GIẢM của MỘT HÀNG: đọc lại vẫn "5" mà ô bulk RỖNG, "Cập nhật hàng loạt" áp 0% cho
 * tất, mọi hàng dưới mức tối thiểu 5% nên toggle/Bật bị Shopee từ chối IM LẶNG — khớp chuỗi đo 12:41→14:59).
 */
// ⚠ HAI HÀM DƯỚI LẶP LÕI CÓ CHỦ ĐÍCH — page-func được chrome.scripting serialize ĐỘC LẬP, helper ngoài
// hàm KHÔNG tồn tại trong trang (đã dẫm đúng bẫy này 18:20 31/08: gọi lõi chung ⇒ ReferenceError câm).
// Neo theo DOM THẬT người dùng cung cấp 17:4x 31/08: `form.batch-setting-panel` chứa `.discount` (ô %GIẢM)
// và `.campaign-stock` (ô SL sp khuyến mãi); fallback leo tổ tiên từ nút "Cập nhật hàng loạt".
function pageGoPhanTramBulkFlash(giaTri) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => { if (!e) { return false; } const r = e.getBoundingClientRect(); return r.width > 2 && r.height > 2; };
  const chonO = ".discount";
  let hit = Array.from(document.querySelectorAll(
    ".batch-setting-panel " + chonO + " input.eds-input__input, .batch-setting-wrapper " + chonO + " input.eds-input__input"))
    .find(hien) || null;
  let nguon = hit ? "khối .batch-setting-panel" : null;
  if (!hit) {
    const nutCapNhat = Array.from(document.querySelectorAll("button"))
      .find((b) => /cập nhật hàng loạt/i.test(b.textContent || "") && hien(b));
    if (nutCapNhat) {
      let khoi = nutCapNhat.parentElement;
      for (let i = 0; i < 6 && khoi && !hit; i++) {
        hit = Array.from(khoi.querySelectorAll(chonO + " input")).find(hien);
        if (!hit) { khoi = khoi.parentElement; }
      }
      nguon = hit ? "leo từ nút Cập nhật hàng loạt" : null;
    }
  }
  if (!hit) { return { ok: false, loi: "không thấy ô " + chonO + " trong khối Chỉnh sửa hàng loạt" }; }
  try {
    const desc = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(hit), "value");
    if (desc && desc.set) { desc.set.call(hit, String(giaTri)); } else { hit.value = String(giaTri); }
  } catch (e) { hit.value = String(giaTri); }
  hit.dispatchEvent(new Event("input", { bubbles: true }));
  hit.dispatchEvent(new Event("change", { bubbles: true }));
  return { ok: true, nguon, docLai: hit.value,
           chuQuanhO: gon(((hit.closest(".eds-input") || hit.parentElement || {}).textContent) || "").slice(0, 40) };
}

/** Gõ "SL sản phẩm khuyến mãi" vào ô bulk thứ hai — LẶP LÕI của {@link pageGoPhanTramBulkFlash}, xem ghi
 * chú serialize ở trên. */
function pageGoSoLuongKmBulkFlash(giaTri) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => { if (!e) { return false; } const r = e.getBoundingClientRect(); return r.width > 2 && r.height > 2; };
  const chonO = ".campaign-stock";
  let hit = Array.from(document.querySelectorAll(
    ".batch-setting-panel " + chonO + " input.eds-input__input, .batch-setting-wrapper " + chonO + " input.eds-input__input"))
    .find(hien) || null;
  let nguon = hit ? "khối .batch-setting-panel" : null;
  if (!hit) {
    const nutCapNhat = Array.from(document.querySelectorAll("button"))
      .find((b) => /cập nhật hàng loạt/i.test(b.textContent || "") && hien(b));
    if (nutCapNhat) {
      let khoi = nutCapNhat.parentElement;
      for (let i = 0; i < 6 && khoi && !hit; i++) {
        hit = Array.from(khoi.querySelectorAll(chonO + " input")).find(hien);
        if (!hit) { khoi = khoi.parentElement; }
      }
      nguon = hit ? "leo từ nút Cập nhật hàng loạt" : null;
    }
  }
  if (!hit) { return { ok: false, loi: "không thấy ô " + chonO + " trong khối Chỉnh sửa hàng loạt" }; }
  try {
    const desc = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(hit), "value");
    if (desc && desc.set) { desc.set.call(hit, String(giaTri)); } else { hit.value = String(giaTri); }
  } catch (e) { hit.value = String(giaTri); }
  hit.dispatchEvent(new Event("input", { bubbles: true }));
  hit.dispatchEvent(new Event("change", { bubbles: true }));
  return { ok: true, nguon, docLai: hit.value,
           chuQuanhO: gon(((hit.closest(".eds-input") || hit.parentElement || {}).textContent) || "").slice(0, 40) };
}

/**
 * Toạ độ nút "Kích hoạt hàng loạt" trong modal "Chú ý" — MẢNH GHÉP CUỐI người dùng cung cấp 17:4x 31/08:
 * bấm Xác nhận thanh đáy xong, Shopee hỏi <i>"Không sản phẩm nào được kích hoạt. Bạn có muốn kích hoạt
 * toàn bộ sản phẩm?"</i> với hai nút "Lưu và Thoát" / "Kích hoạt hàng loạt" (primary). KHÔNG bấm nút này
 * là chương trình lưu ở trạng thái 0-sp-bật — đúng cái chết câm 60s của mọi lượt trước. Sản phẩm không
 * thoả điều kiện sẽ bị Shopee báo noti và bỏ qua — chấp nhận.
 */
function pageToaDoNutKichHoatHangLoat() {
  const na = (s) => (s || "").replace(/\s+/g, " ").trim().toLowerCase();
  const hien = (e) => { if (!e) { return false; } const r = e.getBoundingClientRect(); return r.width > 2 && r.height > 2; };
  const modal = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box"))
    .filter(hien)
    .filter((m) => /kích hoạt/i.test(m.textContent || ""))
    .sort((a, b) => (a.textContent || "").length - (b.textContent || "").length)
    .find((m) => Array.from(m.querySelectorAll("button")).some((b) => na(b.textContent) === "kích hoạt hàng loạt" && hien(b)));
  if (!modal) { return { ok: false }; }
  const nut = Array.from(modal.querySelectorAll("button"))
    .find((b) => na(b.textContent) === "kích hoạt hàng loạt" && hien(b));
  const r = nut.getBoundingClientRect();
  return { ok: true, x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) };
}

/** Toạ độ nút "Xem chi tiết" trong dialog "Tạo thành công" (31/08: chương trình được TẠO ngay khi Xác
 * nhận KHUNG GIỜ — phải vào trang chỉnh sửa GẮN với chương trình; ở lại form create là thao tác trên xác
 * sống: state khung rỗng, toggle + nút LƯU khoá vĩnh viễn "Vui lòng lựa chọn khung giờ"). Match text
 * CHÍNH XÁC — dialog gợi ý quảng cáo đứng cạnh có nút khác. */
function pageToaDoNutXemChiTiet() {
  const na = (s) => (s || "").replace(/\s+/g, " ").trim().toLowerCase();
  const hien = (e) => { if (!e) { return false; } const r = e.getBoundingClientRect(); return r.width > 2 && r.height > 2; };
  const nut = Array.from(document.querySelectorAll("button, a"))
    .find((b) => hien(b) && na(b.textContent) === "xem chi tiết");
  if (!nut) { return { ok: false, loi: "không thấy nút 'Xem chi tiết' đang hiện" }; }
  const r = nut.getBoundingClientRect();
  return { ok: true, x: Math.round(r.left + r.width / 2), y: Math.round(r.top + r.height / 2) };
}

/** Đọc dialog "Tạo thành công" (nếu đang hiện) — bằng chứng chương trình ĐÃ ĐƯỢC TẠO, kèm khung giờ Shopee
 * nói (đo 12:45 31/08: "Tạo thành công … diễn ra từ 22:00 01-09-2026…"). CHỈ ĐỌC, không bấm gì. */
function pageDocDialogTaoThanhCong() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  // "Hiện" = rect có kích thước thật — chặn shell pre-render ẨN, xem {@link pageXacNhanDialogChonAnh}.
  const hien = (e) => { if (!e) { return false; } const r = e.getBoundingClientRect(); return r.width > 2 && r.height > 2; };
  // Quét cả `.eds-modal` + lấy hộp NHỎ NHẤT — xem ghi chú ở {@link pageXacNhanDialogChonAnh} (đo 14:09).
  const modal = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"))
    .filter(hien)
    .filter((m) => /tạo thành công/i.test(m.textContent || ""))
    .sort((a, b) => (a.textContent || "").length - (b.textContent || "").length)[0] || null;
  return modal ? { thay: true, text: gon(modal.textContent).slice(0, 200) } : { thay: false };
}

/**
 * Bấm "Xác nhận" ở **THANH ĐÁY** — <b>ĐÂY LÀ BƯỚC TẠO THẬT.</b> Chỉ luồng thật được gọi; lượt dò tuyệt đối
 * không (xem khối ràng buộc đầu file).
 *
 * ⚠ Neo NGƯỢC LẠI với hai hàm trên: BẮT BUỘC nằm trong `.fix-container` và BẮT BUỘC không nằm trong bất kỳ
 * `.eds-modal` nào. Cùng lớp `confirm-btn` dùng cho cả nút của modal khung giờ, nên thiếu chốt này là bấm
 * nhầm sang một nút chỉ điền form rồi báo "đã tạo".
 */
function pageToaDoNutTaoThat() {
  const na = (s) => (s || "").replace(/\s+/g, " ").trim().toLowerCase();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  // ⚠ SỐ ĐO THẬT 19:00 29/08/2026 (dump NEO tại chỗ, shop Alina Store1): trang tạo FLASH dùng class
  // `fixed-container` (chuỗi tổ tiên: footer → fixed-container → shopee-fixed-bottom-card), KHÔNG phải
  // `fix-container` như trang KM — neo cũ đếm ra 0 và K*.7 hỏng "không thấy thanh đáy" dù nút đang hiện.
  // Nhận CẢ HAI class: markup KM giữ đường cũ, markup Flash đi đường mới.
  // Trang có thể có NHIỀU phần tử khớp (vd một thanh cố định trên cùng cũng mang class họ fixed) — gom ứng
  // viên từ TẤT CẢ các thanh rồi mới lọc, đừng chọn một thanh rồi mò trong đó (đo thật 19:08 29/08: chọn
  // thanh đầu tiên đang hiện là vớ nhầm cái không chứa nút).
  const cacThanh = Array.from(document.querySelectorAll(".fix-container, .fixed-container"));
  if (cacThanh.length === 0) { return { ok: false, loi: "không thấy thanh đáy .fix-container/.fixed-container của trang tạo chương trình" }; }
  // ⚠ ĐO THẬT 19:04 29/08/2026: footer flash KHÔNG có thẻ <button> nào — phần tử bấm là
  // `div.eds-popover.eds-popover--light.confirm-btn` (textContent dính chữ tooltip "Xác nhậnVui lòng thê…").
  // Ứng viên vì thế phải gồm cả [role=button] lẫn chính `.confirm-btn`; phép so chữ thêm vế BẮT ĐẦU BẰNG.
  const tatCa = [];
  for (const t of cacThanh) {
    for (const b of t.querySelectorAll("button, [role='button'], .confirm-btn")) {
      if (!b.closest(".eds-modal") && tatCa.indexOf(b) < 0) { tatCa.push(b); }
    }
  }
  const chon = (ds) => ds.find((b) => na(b.textContent) === "xác nhận")
    || ds.find((b) => na(b.textContent).indexOf("xác nhận") === 0)
    || ds.find((b) => /confirm-btn/.test(String(b.className || "")))
    || ds.find((b) => b.closest(".confirm-btn"));
  // CHỈ chọn trong tập ĐANG HIỆN (phản biện 30/08): bấm một phần tử ẩn là vô nghĩa mà vẫn báo ok — thà rơi
  // vào lỗi "không thấy nút" có tên.
  const nut = chon(tatCa.filter(hien));
  if (!nut) {
    return { ok: false,
             loi: "không thấy nút Xác nhận ĐANG HIỆN trong thanh đáy (soThanh=" + cacThanh.length + ", ứng viên: "
               + JSON.stringify(tatCa.slice(0, 6).map((b) => b.tagName + ":" + na(b.textContent).slice(0, 15))) + ")",
             coSan: tatCa.map((b) => na(b.textContent)).slice(0, 8) };
  }
  // Chốt KHOÁ phải phủ cả đường DIV (phản biện 30/08): `disabled` chỉ có nghĩa trên <button>; nút Flash là
  // div.eds-popover.confirm-btn kèm tooltip lý do khoá — bấm div khoá là không gì xảy ra rồi đốt 60s chờ.
  const clsNut = String(nut.className && nut.className.baseVal !== undefined ? nut.className.baseVal : (nut.className || ""));
  if (nut.disabled || /\bdisabled\b/.test(clsNut) || nut.getAttribute("aria-disabled") === "true") {
    return { ok: false, loi: "nút Xác nhận thanh đáy đang KHOÁ (cls='" + clsNut.slice(0, 60)
      + "', text='" + na(nut.textContent).slice(0, 40) + "')" };
  }
  // TRẢ TOẠ ĐỘ, KHÔNG click (31/08/2026): cú LƯU cuối cùng đi bằng CHUỘT THẬT qua chrome.debugger như mọi
  // nút HÀNH ĐỘNG khác của trang flash mới — click DOM thường vào DIV này đã câm suốt các đêm 29–30/08.
  //
  // ⚠ CUỘN NÚT VÀO GIỮA MÀN TRƯỚC KHI ĐO (18:38 31/08 — thủ phạm cuối cùng của chuỗi "câm": nút nằm ở
  // y=6285 trên trang dài, viewport cao 1241 — chuột thật bấm vào toạ độ NGOÀI màn hình và CDP lặng lẽ
  // nuốt. Phép đếm "đang hiện" rect>0 không phân biệt được trong/ngoài viewport nên vòng cuộn không chạy).
  nut.scrollIntoView({ block: "center", inline: "nearest" });
  // ẨN WIDGET CHAT CSKH (18:50 31/08 — thủ phạm kế tiếp: bong bóng chat Shopee nổi góc phải-dưới ĐÈ đúng
  // điểm tâm nút, hit-test trả "shopee-cs-chat-scope-style" — chuột thật bấm trúng chat). Chat không thuộc
  // luồng, ẩn trong browser automation vô hại.
  for (const e of document.querySelectorAll("[class*='shopee-cs-chat'], [id*='shopee-chat']")) {
    e.style.display = "none";
  }
  const rNut = nut.getBoundingClientRect();
  const x = Math.round(rNut.left + rNut.width / 2), y = Math.round(rNut.top + rNut.height / 2);
  if (y < 0 || y > (window.innerHeight || 0)) {
    return { ok: false, loi: "nút Xác nhận thanh đáy vẫn NGOÀI viewport sau scrollIntoView (y=" + y
      + ", cao màn=" + window.innerHeight + ") — không có toạ độ bấm được" };
  }
  // HIT-TEST + VIEWPORT (18:35 31/08: trusted click vào nút này vẫn câm cả trên trang edit — đo xem điểm
  // bấm có bị phần tử khác đè, và toạ độ có nằm trong viewport CSS không).
  const tren = document.elementFromPoint(x, y);
  return { ok: true, x, y, cls: String(nut.className || ""), text: na(nut.textContent),
           phanTuTrenCung: tren ? { tag: tren.tagName, cls: String(tren.className || "").slice(0, 50),
                                    laNut: tren === nut || nut.contains(tren) || (tren.contains && tren.contains(nut)) } : null,
           viewport: { iw: window.innerWidth, ih: window.innerHeight, dpr: window.devicePixelRatio } };
}

/**
 * Quét MỌI thông báo đang hiện trên trang tạo (CHỈ ĐỌC) — toast/message/notification/lỗi form, đúng bộ
 * selector b9 của KM. Dùng ở bước G cho CẢ baseline-trước-bấm lẫn vòng chờ-sau-bấm: hai lượt quét phải cùng
 * MỘT phép đo thì phép trừ "chuỗi mới xuất hiện" mới có nghĩa.
 */
function pageQuetBaoFlash() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim().slice(0, 140);
  const hien = (e) => !!e && e.getClientRects().length > 0;
  return Array.from(document.querySelectorAll(
    ".eds-message, .eds-toast, .eds-notification, .eds-form-item__error, .error-message, [class*='success']"))
    .filter(hien).map((e) => gon(e.textContent)).filter((t) => t).slice(0, 6);
}

/** Đọc URL + hai neo của trang tạo (CHỈ ĐỌC) — bằng chứng "đã rời form sau khi bấm TẠO THẬT". */
function pageDocMocTrangTaoFlash() {
  const hien = (e) => !!e && e.getClientRects().length > 0;
  return {
    url: location.href,
    soOThoiGian: Array.from(document.querySelectorAll("button.time-slot-btn")).filter(hien).length,
    soThanhDay: Array.from(document.querySelectorAll(".fix-container")).filter(hien).length,
  };
}

// ── VÒNG THẬT ───────────────────────────────────────────────────────────────────────────────────────────────

/**
 * Chạy TRỌN lượt Flash Sale THẬT cho SHOP ĐANG MỞ: với mỗi khung giờ CHƯA DÙNG của NGÀY ĐÍCH, tạo một
 * chương trình "Flash Sale Của Shop" nạp 10 sản phẩm bán chạy nhất của TRANG thứ k.
 *
 * <p><b>Gói trả về CÙNG KHUÔN `kmKetQua`</b> của luồng KM/Combo (`soSanPham`, `daTao`, `boQua`, `canhBao`,
 * `buocHong`, `captcha`) để phía C# dùng lại `OrdersBridgeSession.DocKetQuaKmMotShop` — <b>đừng đẻ khuôn thứ
 * hai</b>. Thêm đúng MỘT trường riêng: `ngayMaiKhongDo`, xem dưới.</p>
 *
 * <p><b>MỘT BƯỚC HỎNG ⇒ DỪNG SHOP</b> (đúng luật của hai luồng kia): các bước sau chạy trên trạng thái rác thì
 * vô nghĩa, mà lại là lúc dễ bấm trúng thứ không định bấm nhất. Khung đã tạo được vẫn tính.</p>
 */
export async function chayFlashSaleChoTabDangMo(ts) {
  const tabId = tabShop();
  const ds = [];
  const canhBao = [];
  /** Gói kết quả CHUẨN — mọi lối ra đi qua đây để không lối nào quên trường nào. */
  const ketThuc = (them) =>
    Object.assign(
      { buoc: ds, soSanPham: 0, daTao: false, boQua: null, canhBao,
        ngayMaiKhongDo: false, soKhungDaTao: 0,
        captcha: ds.some((b) => b.captcha === true) },
      them || {});

  if (tabId == null) {
    ds.push({
      ten: "0. Tab shop", ok: false, selectorDung: null, kyVong: null, kieuSo: null, docLai: null,
      truoc: null, sau: null, ghiChu: null,
      loi: "chưa có tab shop (openShopDetail chưa chạy, hoặc service worker đã chết giữa chừng)",
    });
    return ketThuc();
  }
  const chay = (fn, args) => execInTab(tabId, fn, args || []);
  const NHAN = "Flash";

  // BƯỚC GIẢ "0. Tham số" — TRẢ VỀ TRƯỚC KHI ĐỤNG TRANG (khuôn `flow-combo.js`): tham số hỏng thì không có
  // việc gì để làm trên trang, mà để bước 5 tự báo là đã bỏ lại một form dở dang trên shop THẬT.
  const tp = docThamSoFlash(ts);
  if (tp.loi) {
    ds.push({
      ten: "0. Tham số", ok: false, selectorDung: null, kyVong: null, kieuSo: null, docLai: null,
      truoc: null, sau: tp, ghiChu: null, loi: tp.loi,
    });
    send({ action: "progress", message: `[${NHAN}] 0. Tham số: DỪNG — ${tp.loi}` });
    return ketThuc();
  }

  // Ghi nhớ khung ĐÃ DÙNG — sống trong BỘ NHỚ CỦA LƯỢT, tuyệt đối KHÔNG ghi DB (plan §4: "việc của một đêm là
  // hàm THUẦN của ngày"). Một con trỏ dưới DB sẽ trôi lệch ngay khi một máy bỏ lỡ một đêm.
  const khungDaDung = [];
  let soKhungDaTao = 0;
  let soSpDaNap = 0;

  // ══════════════════════════════════════════════════════════════════════════════════════════════════════
  // ĐỌC LỊCH **ĐÚNG MỘT LẦN CHO CẢ SHOP** — biến memo này KHAI NGOÀI vòng lặp, cố ý.
  //
  // Plan §4: *"Ngày nào có khung là lịch phát hành của Shopee, không phải thuộc tính riêng của shop… Đọc 12
  // lần là 12 lượt chờ render cho cùng một câu trả lời."* Cùng lý lẽ ấy áp cho 8 KHUNG của một shop: mỗi khung
  // mở lại form nên modal khung giờ mở lại, nhưng **DANH SÁCH NGÀY ĐỎ thì không đổi giữa hai khung**.
  //
  // Bản đầu (28/08/2026) để nguyên lượt `choLichLang` trong thân vòng ⇒ 8 lần/shop, ~96 lần/đêm, mỗi lần tới
  // 12s trần chờ lắng — cho một câu trả lời đã biết từ khung đầu.
  //
  // ⚠ NGOẠI LỆ DUY NHẤT — `soLatThang > 0` (đêm cuối tháng, ngày mai thuộc tháng SAU): modal mở lại là quay về
  // THÁNG HIỆN TẠI nên phải lật lại. Mà lật thì BẮT BUỘC xác nhận đã đáp xuống đúng tháng (ca thật 12:20:04
  // ngày 27/08: bấm lùi 1 tháng mà đáp xuống 9/2025 — lùi 12 tháng), và phép xác nhận đó chính là một lượt đọc
  // lịch. Đổi lấy đúng ~1 đêm mỗi tháng; mọi đêm còn lại đọc một lần.
  // ══════════════════════════════════════════════════════════════════════════════════════════════════════
  let lichShop = null;
  let soLanDocLich = 0;

  // Ngày đích + ngày kế dạng "DD-MM-YYYY" như cột Khung giờ của trang danh sách hiển thị — dùng cho bước
  // DỌN (bA vòng đầu) và phép đọc-lại trên list (bF2). Ngày kế tính THUẦN (ngayKeTiep) — luồng thật cấm
  // đọc đồng hồ.
  const padSo = (n) => (n < 10 ? "0" + n : "" + n);
  const nhanNgayDich = padSo(tp.ngay) + "-" + padSo(tp.thang) + "-" + tp.nam;
  const ke = ngayKeTiep(tp.ngay, tp.thang, tp.nam);
  const nhanNgayKe = padSo(ke.ngay) + "-" + padSo(ke.thang) + "-" + ke.nam;

  // ══ LƯU + KÍCH HOẠT — dùng chung cho K0/K*.7 (trang edit) và K*.7 (form create): cuộn thanh đáy →
  // Xác nhận (chuột thật) → modal "Kích hoạt hàng loạt" (mảnh ghép cuối, người dùng 17:4x 31/08) →
  // chờ rời-trang/toast. Trả {thanhCong, truoc, sau, bam, kichHoat, baoThay, toastThanhCong, loi}. ══
  async function luuVaKichHoat() {
    const truoc = await chay(pageDocMocTrangTaoFlash, []);

    // CUỘN CHO THANH ĐÁY HIỆN RỒI MỚI TÌM NÚT — vá theo số đo thật 18:44 29/08/2026: K1.7 hỏng "không
    // thấy thanh đáy .fix-container" NGAY SAU khi modal sản phẩm đóng, trong khi K1.1–K1.6 đều ok.
    // Đúng bài học b9 của luồng KM (người dùng 19/08: "phải nhích chuột xuống dưới 1 chút thì mới có
    // xác nhận"): thanh đáy chỉ hiện khi trang đã cuộn, và trang vừa đóng modal còn đứng giữa form.
    // Điều kiện chờ RỘNG BẰNG danh sách ứng viên lúc click (luật km b9; phản biện 30/08 bắt bản đầu hẹp
    // hơn click — ".confirm-btn" trần vừa hụt biến thể <button> trong thanh, vừa khớp nhầm nút modal):
    // đếm đúng tập mà pageBamTaoThatFlash gom — button/[role=button]/.confirm-btn TRONG hai họ thanh đáy.
    const SEL_THANH_DAY = ".fix-container button, .fixed-container button, "
      + ".fix-container [role='button'], .fixed-container [role='button'], "
      + ".fix-container .confirm-btn, .fixed-container .confirm-btn";
    let coNutTaoThat = await chay(pageDemPhanTuHien, [SEL_THANH_DAY]);
    let soNhipCuon = 0;
    for (let i = 0; i < 12 && !coNutTaoThat; i++) {
      await chay(() => { window.scrollBy(0, 400); return 1; }, []);
      await sleep(400);
      soNhipCuon = i + 1;
      coNutTaoThat = await chay(pageDemPhanTuHien, [SEL_THANH_DAY]);
    }
    if (!coNutTaoThat) {
      const nutTheoChu = await chay(() => {
        const hien = (el) => { const r = el.getBoundingClientRect(); return r.width > 0 || r.height > 0; };
        // Quét cả DIV .confirm-btn/[role=button] — biến thể ĐO THẬT của trang Flash (button thuần là của KM).
        return Array.from(document.querySelectorAll("button, [role='button'], .confirm-btn")).filter(hien)
          .filter((b) => /xác nhận/i.test(b.textContent || ""))
          .map((b) => b.tagName + ":" + String(b.className || "").slice(0, 60));
      }, []);
      return { thanhCong: false, truoc, sau: truoc, bam: null, kichHoat: null, baoThay: [], toastThanhCong: false,
               loi: "cuộn " + soNhipCuon + " nhịp vẫn KHÔNG thấy nút Xác nhận thanh đáy (đếm theo "
                 + SEL_THANH_DAY + ")", chiTiet: { soNhipCuon, nutTheoChu } };
    }

    // BASELINE thông báo TRƯỚC cú bấm (phản biện 30/08): toast của bước F vài giây trước có thể còn sống,
    // và phần tử tĩnh khớp [class*='success'] tồn tại sẵn — chỉ chuỗi MỚI xuất hiện sau cú bấm mới được tính.
    const baoTruocBam = (await chay(pageQuetBaoFlash, [])) || [];

    // Cú LƯU cuối bằng CHUỘT THẬT qua debugger — click DOM thường vào DIV thanh đáy câm suốt 29–30/08.
    await ensureDbg(tabId);
    const bam = await chay(pageToaDoNutTaoThat, []);
    if (bam && bam.ok) { await trustedClick(tabId, bam.x, bam.y); }
    if (!bam || !bam.ok) {
      // CHẨN ĐOÁN TẠI CHỖ (số đo 18:50 29/08: nút confirm-btn HIỆN ngay mà .fix-container = 0 — nghi Shopee
      // đổi class thẻ bao thanh đáy). Đếm neo + tổ tiên các nút đang hiện, nhét vào `loi` vì C# chỉ chuyển
      // tiếp trường này lên nhật ký. BỌC try/catch RIÊNG (phản biện 30/08): tab chết giữa chừng thì dump
      // ném và nuốt mất bam.loi — chẩn đoán phụ không được phép giết chẩn đoán chính.
      let neoDay = null;
      try {
      neoDay = await chay(() => {
        const hien = (e) => !!e && e.getClientRects().length > 0;
        const cls = (e) => String(e.className && e.className.baseVal !== undefined ? e.className.baseVal : (e.className || ""));
        const con = Array.from(document.querySelectorAll(".confirm-btn")).filter(hien).slice(0, 3).map((b) => {
          const toTien = []; let p = b.parentElement;
          for (let i = 0; i < 4 && p; i++) { toTien.push(cls(p).slice(0, 60)); p = p.parentElement; }
          return { tag: b.tagName, cls: cls(b).slice(0, 60), text: (b.textContent || "").trim().slice(0, 60), toTien };
        });
        // TRẠNG THÁI SẢN PHẨM TRONG FORM (30/08 21:01): tooltip nút là "Vui lòng thê[m sản phẩm]" ⇒ nghi
        // K*.6 đóng modal mà sản phẩm KHÔNG vào form (bF chỉ kiểm "modal đã đóng") — nút bị khoá mềm, click câm.
        const formSp = document.querySelector(".detail-products-container");
        return { fixContainer: document.querySelectorAll(".fix-container").length,
                 fixedBottom: document.querySelectorAll(".fixed-bottom").length,
                 confirmHien: con,
                 formSanPham: formSp ? {
                   coEmpty: !!formSp.querySelector(".empty-content"),
                   text: (formSp.textContent || "").replace(/\s+/g, " ").trim().slice(0, 150),
                 } : "(khong thay .detail-products-container)" };
      }, []);
      } catch (e) { neoDay = "(dump NEO ném: " + ((e && e.message) || e) + ")"; }
      return { thanhCong: false, truoc, sau: truoc, bam, kichHoat: null, baoThay: [], toastThanhCong: false,
               loi: ((bam && bam.loi) || "không bấm được nút Xác nhận thanh đáy")
                 + " · NEO: " + JSON.stringify(neoDay), chiTiet: { neoDay } };
    }
    // ── MODAL "Chú ý — Không sản phẩm nào được kích hoạt. Bạn có muốn kích hoạt toàn bộ sản phẩm?" ─────
    // MẢNH GHÉP CUỐI (người dùng cung cấp DOM 17:4x 31/08): sau cú Xác nhận thanh đáy Shopee hỏi hộp thoại
    // này; PHẢI bấm "Kích hoạt hàng loạt" — không bấm là chương trình lưu 0-sp-bật và mọi tín hiệu im re
    // đúng 60s (cái chết câm của MỌI lượt 29–31/08). Sản phẩm không thoả điều kiện sẽ bị Shopee báo noti
    // rồi bỏ qua — chấp nhận theo lời người dùng.
    let kichHoat = null;
    for (let i = 0; i < 30; i++) {
      await sleep(500);
      const kh = await chay(pageToaDoNutKichHoatHangLoat, []);
      if (kh && kh.ok) {
        await trustedClick(tabId, kh.x, kh.y);
        kichHoat = kh;
        break;
      }
      const uNhanh = await chay(() => location.href);
      if (String(uNhanh || "") !== String(truoc.url || "")) { break; }
    }
    if (!kichHoat) {
      canhBao.push("Lưu/Kích hoạt: KHÔNG thấy modal 'Kích hoạt hàng loạt' trong 15s sau cú Xác nhận — "
        + "hoặc Shopee lưu thẳng, hoặc chương trình sẽ 0-sp-bật (xem cột Bật Flash Sale trên danh sách)");
    }

    // ĐỌC LẠI HAI TÍN HIỆU: (1) rời trang tạo (khuôn KM); (2) TOAST thành công — đo thật 19:15 29/08/2026:
    // cú bấm ĐÃ ăn (nút DIV .confirm-btn được bấm thật) mà URL đứng nguyên 60s ⇒ chỉ nhìn URL là mỗi khung
    // tạo xong đều bị chấm HỎNG oan và vòng khung dừng ngay khung đầu. Quét toast bằng đúng bộ selector
    // b9 của KM; chữ "thành công/success" trong toast được tính là TẠO XONG kể cả khi trang không rời.
    // 120 nhịp = 60s — đêm 23/08/2026 ba shop LỚN của KM bị chấm HỎNG sau 20s trong khi chương trình ĐÃ tạo.
    let sau = truoc;
    let baoThay = [];
    let toastThanhCong = false;
    let kichHoatXong = false;
    let soNhipModalVang = 0;
    for (let i = 0; i < CHO_ROI_FORM_NHIP; i++) {
      await sleep(500);
      sau = await chay(pageDocMocTrangTaoFlash, []);
      if (String(sau.url || "") !== String(truoc.url || "")) { break; }
      const bao = await chay(pageQuetBaoFlash, []);
      if (bao && bao.length) {
        // Chỉ tính chuỗi MỚI so với baseline trước cú bấm; và "thành công" phải KHÔNG dính vế phủ định —
        // phản biện 30/08: "Tạo KHÔNG thành công" chứa "thành công" nên regex trần chấm XANH một cú tạo hỏng.
        const moi = bao.filter((t) => baoTruocBam.indexOf(t) < 0);
        for (const t of moi) { if (baoThay.indexOf(t) < 0) { baoThay.push(t); } }
        if (moi.some((t) => /thành công|success/i.test(t)
            && !/không thành công|chưa thành công|thất bại|lỗi|unsuccess|fail|error/i.test(t))) {
          toastThanhCong = true; break;
        }
      }
      // Dialog "Tạo thành công" nở thay cho toast/rời-trang (đã thấy 14:00/14:37) — tín hiệu XONG; bấm
      // "Quay lại trang Danh sách" để vòng khung kế không bị modal che.
      const dlgXong = await chay(pageDocDialogTaoThanhCong, []);
      if (dlgXong && dlgXong.thay) {
        toastThanhCong = true;
        await chay(pageBamQuayLaiDanhSach, []);
        await sleep(1000);
        break;
      }
      // TÍN HIỆU THỨ BA (v32 18:56: "Kích hoạt hàng loạt" ĐÃ bấm trúng mà 60s không rời trang, không
      // toast — với NGƯỜI THẬT thì bấm kích hoạt là XONG, Shopee không tiễn khỏi trang): modal Chú ý đã
      // ĐÓNG và không mọc lại 6 nhịp liên tiếp (3s) sau cú kích hoạt ⇒ server đã nhận.
      if (kichHoat) {
        const conKh = await chay(pageToaDoNutKichHoatHangLoat, []);
        soNhipModalVang = (conKh && conKh.ok) ? 0 : soNhipModalVang + 1;
        if (soNhipModalVang >= 6) { kichHoatXong = true; break; }
      }
    }
    const daRoi = String(sau.url || "") !== String(truoc.url || "");
    const thanhCong = daRoi || toastThanhCong || kichHoatXong;
    return { thanhCong, truoc, sau, bam, kichHoat, kichHoatXong, baoThay, toastThanhCong, loi: null };
  }

  // ══ ĐIỀN BULK + LƯU + KÍCH HOẠT trên trang ĐANG MỞ (edit/create có state chuẩn) — quy trình tay người
  // dùng 17:4x 31/08: gõ 5% + SL 10 → Cập nhật hàng loạt → Xác nhận thanh đáy → modal Kích hoạt. ══════════
  // ⚠ ĐỌC `tp` QUA CLOSURE, không đọc hằng: `tp` là gói tham số của CHÍNH lượt này (khai ở bước giả "0. Tham
  // số" phía trên), nên hai con số dưới đây luôn là hai con số người dùng vừa đặt trên form.
  async function dienVaLuuKichHoat() {
    const go = await chay(pageGoPhanTramBulkFlash, [tp.phanTramGiam]);
    const goSl = await chay(pageGoSoLuongKmBulkFlash, [tp.soLuongThamGia]);
    let capNhat = null;
    if (go && go.ok) {
      await sleep(400);
      capNhat = await chay(pageClickTheoSelector, [["button"], "cập nhật hàng loạt"]);
      await sleep(2000);
    }
    const luu = await luuVaKichHoat();
    return { go, goSl, capNhat, luu };
  }

  // ══ THÂN TÁI CHẾ MỘT CHƯƠNG TRÌNH 0-SP-BẬT ĐÃ NỞ — dùng chung cho K0.n (đầu lượt) và K*.7 ══════════
  // ── G) TÁI CHẾ một chương trình 0-sp-bật ĐÃ NỞ trên danh sách (đường form MỚI, chốt ~16:35 31/08) ──
  // Form create sau khi tạo là XÁC SỐNG (chuỗi đo 15:12–15:48: state khung rỗng vĩnh viễn — toggle Bật
  // + nút LƯU khoá "Vui lòng lựa chọn khung giờ" bất chấp chuột thật), và danh sách hiển thị hàng MỚI
  // trễ ~5–8 phút (đo v19–v22: reload cứng cũng không xuyên được — trễ phía server). Vậy: khung VỪA dựng
  // không thể bật ngay; bước này bật hàng CŨ nào đã kịp nở (khung trước trong cùng lượt, hoặc lượt
  // trước) — Chỉnh sửa → áp giá → BẬT → LƯU trên trang edit (nó load chương trình từ server nên state
  // khung CHUẨN). Không có hàng nào đã nở ⇒ XANH — khung vừa dựng để lượt/khung sau bật.
  async function thanBuocTaiChe() {
    await chay(pageClickTheoSelector, [['a[href="/portal/marketing/shop-flash-sale/list"]'], null]);
    for (let i = 0; i < 20; i++) {
      await sleep(500);
      const u = await chay(() => location.href);
      if (String(u || "").indexOf("/shop-flash-sale/list") >= 0) { break; }
    }
    // Một cú reload cứng cho danh sách tươi (SPA cache), rồi tìm NGẮN — hàng đã nở thì thấy ngay.
    await chay(() => { location.reload(); return 1; }, []);
    await sleep(5000);
    await choThanTrangDanhSach(chay);
    let nutSua = null;
    for (let i = 0; i < 6; i++) {
      await sleep(1000);
      nutSua = await chay(pageToaDoNutChinhSuaHangRong, [nhanNgayDich, nhanNgayKe]);
      if (nutSua && nutSua.daBam) { break; }
    }
    if (!nutSua || !nutSua.daBam) {
      // Đếm TỔNG hàng ngày đích để phân biệt ba ca: (a) hàng chưa nở (cả hai số 0), (b) hàng đã nở và ĐÃ
      // CÓ SP BẬT — tức cú Bật ở K*.6b thật ra ăn trên server dù UI form zombie nói 0 (soHangNgayDich > 0,
      // soHangRac = 0 ⇒ KHÔNG còn gì để làm), (c) có rác mà phép tìm nút Chỉnh sửa trượt (soHangRac > 0 —
      // phải xem lại pageToaDoNutChinhSuaHangRong).
      const demHang = await chay(pageDemChuongTrinhRong, [nhanNgayDich, nhanNgayKe]);
      if (demHang && demHang.soHangRac > 0) {
        return { truoc: null, sau: { nutSua, demHang },
                 loi: "danh sách CÓ " + demHang.soHangRac + " hàng 0-sp-bật ngày đích mà phép tìm nút"
                   + " 'Chỉnh sửa' không thấy — xem lại pageToaDoNutChinhSuaHangRong",
                 kyVong: "HET", docLai: "TRUOT_NUT_SUA" };
      }
      const cauHet = (demHang && demHang.soHangNgayDich > 0
        ? "Danh sách có " + demHang.soHangNgayDich + " chương trình ngày đích và KHÔNG hàng nào 0-sp-bật"
          + " — các chương trình đã CÓ sản phẩm bật, không còn gì để tái chế."
        : "Chưa có hàng nào của ngày đích nở trên danh sách (hàng mới trễ ~5–8 phút)"
          + " — khung vừa dựng sẽ được BẬT ở khung/lượt sau.");
      // Phát thẳng lên nhật ký — ghiChu của bước xanh không được C# in, mà đây là câu trả lời phân xử.
      send({ action: "progress", message: `[${NHAN}] Tái chế: ${cauHet}` });
      return { truoc: null, sau: { nutSua, demHang }, kyVong: "HET", docLai: "HET", ghiChu: cauHet };
    }
    await ensureDbg(tabId);
    await trustedClick(tabId, nutSua.x, nutSua.y);
    let bangEdit = null;
    for (let i = 0; i < 30; i++) {
      await sleep(500);
      bangEdit = await chay(pageAnhBangSanPham, []);
      if (bangEdit && bangEdit.soO > 0) { break; }
    }
    if (!bangEdit || !(bangEdit.soO > 0)) {
      return { truoc: null, sau: { nutSua, bangEdit, url: await chay(() => location.href) },
               loi: "đã bấm Chỉnh sửa mà trang không mọc ô %GIẢM nào sau 15s",
               kyVong: "DA_ROI", docLai: "EDIT_KHONG_LOAD" };
    }
    // QUY TRÌNH TAY người dùng (17:4x 31/08): gõ 5% + SL sp khuyến mãi 10 → "Cập nhật hàng loạt" → cú
    // Xác nhận thanh đáy → modal "Kích hoạt hàng loạt". KHÔNG đụng tick phân loại / nút Bật — nút Bật
    // disabled cả với người thật khi 0 phân loại chọn, và việc kích hoạt do modal cuối lo.
    const dien = await dienVaLuuKichHoat();
    const buocEdit = { nutSua, bangEdit, goEdit: dien.go, goSlEdit: dien.goSl, capNhatEdit: dien.capNhat };
    const luu = dien.luu;
    if (!luu.thanhCong && luu.loi) {
      return { truoc: luu.truoc, sau: { buocEdit, luu }, loi: luu.loi, kyVong: "DA_ROI", docLai: "CON_O_FORM" };
    }
    const truoc = luu.truoc, sau = luu.sau, bam = luu.bam, kichHoat = luu.kichHoat;
    const baoThay = luu.baoThay, toastThanhCong = luu.toastThanhCong, thanhCong = luu.thanhCong;

    // Sau lưu, đếm tham khảo (KHÔNG đỏ theo soHangRac: hàng của khung VỪA dựng trong lượt này có thể
    // vừa nở ra — 0-bật là trạng thái chờ hợp lệ của nó, khung/lượt sau tái chế).
    if (thanhCong) {
      await sleep(2000);
      const demSauLuu = await chay(pageDemChuongTrinhRong, [nhanNgayDich, nhanNgayKe]);
      if (demSauLuu && demSauLuu.soHangRac > 0) {
        canhBao.push(`Tái chế: sau lưu còn ${demSauLuu.soHangRac} hàng 0-sp-bật ngày đích trên danh sách `
          + `— hàng mới nở chờ tái chế ở lượt gọi sau`);
      }
    }

    // TIMEOUT ⇒ CHẨN ĐOÁN TRẠNG THÁI FORM (30/08 22:35): cú bấm "ăn" mà trang trơ và KHÔNG toast nào —
    // tooltip nút là "Vui lòng thê[m sản phẩm]" ⇒ nghi K*.6 đóng modal mà sản phẩm KHÔNG vào form (bF chỉ
    // kiểm "modal đã đóng"), nút bị khoá mềm nên click câm. Nhét vào `loi` vì chỉ trường này lên nhật ký.
    let chanDoanForm = "";
    if (!thanhCong) {
      try {
        const cd = await chay(() => {
          const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
          const formSp = document.querySelector(".detail-products-container");
          const nut = Array.from(document.querySelectorAll(".confirm-btn"))
            .filter((e) => e.getClientRects().length > 0)[0];
          const oKhung = Array.from(document.querySelectorAll("button.time-slot-btn")).map((b) => ({
            cls: gon(String(b.className || "")).slice(0, 60), text: gon(b.textContent).slice(0, 40),
          }));
          return {
            formCoEmpty: formSp ? !!formSp.querySelector(".empty-content") : null,
            formText: formSp ? gon(formSp.textContent).slice(0, 150) : "(khong thay container)",
            nutText: nut ? gon(nut.textContent).slice(0, 80) : null,
            oKhung,
            htmlSanPham: formSp ? formSp.innerHTML.replace(/\s+/g, " ").slice(0, 700) : null,
          };
        }, []);
        chanDoanForm = " · FORM: " + JSON.stringify(cd);
      } catch (e) { chanDoanForm = " · FORM: (dump ném: " + ((e && e.message) || e) + ")"; }
    }
    return {
      truoc, sau: { moc: sau, bam, kichHoat, toastThanhCong, baoThay, buocEdit },
      loi: thanhCong
        ? null
        : "VẪN ở trang sau 60s, không toast thành công — Shopee nói: "
          + (baoThay.length ? baoThay.join(" | ") : "(KHÔNG có thông báo nào trên trang)")
          + "; modal Kích hoạt hàng loạt: " + (kichHoat ? "ĐÃ bấm (x=" + kichHoat.x + ",y=" + kichHoat.y + ")" : "KHÔNG thấy")
          + chanDoanForm
          + ". ⚠ TIỀN LỆ KM 23/08/2026: chương trình VẪN CÓ THỂ ĐÃ ĐƯỢC TẠO — KIỂM trên Shopee trước khi "
          + "chạy lại, kẻo tạo TRÙNG",
      kyVong: "DA_ROI", docLai: thanhCong ? "DA_ROI" : "CON_O_FORM",
    };
  }

  // ══ K0. TÁI CHẾ đầu lượt — BẮT BUỘC đứng TRƯỚC vòng dựng khung (chốt 16:33 31/08: khi ngày ĐÍCH
  // hết khung trống, Shopee tắt luôn 'ngày đỏ' và vòng dựng dừng ở K*.2 — không có K0 thì các chương
  // trình 0-bật không bao giờ được bật nữa). Lặp tới khi hết hàng đã nở.
  let soTaiChe = 0;
  for (let lanK0 = 1; lanK0 <= 12; lanK0++) {
    const bK0 = await buoc(ds, `K0.${lanK0} Tái chế chương trình 0-sp-bật đã nở (bật + lưu)`, thanBuocTaiChe, tabId, NHAN);
    if (!bK0.ok) { return ketThuc({ soSanPham: soSpDaNap, daTao: soTaiChe > 0, soKhungDaTao, soTaiChe }); }
    if (bK0.docLai === "HET") { break; }
    soTaiChe++;
  }

  // ⚠ VÒNG NÀY KHÔNG CÓ TRẦN NGHIỆP VỤ (chốt 03/09/2026): lấy HẾT khung Shopee mở cho ngày đích. Điểm dừng
  // thật là `hetKhung` ở K*.3. {@link TRAN_VONG_KHUNG_KY_THUAT} chỉ là chốt chống vòng vô tận.
  for (let vong = 0; vong < TRAN_VONG_KHUNG_KY_THUAT; vong++) {
    const K = "K" + (vong + 1);

    // ── A) Về trang danh sách → chờ THÂN TRANG → bấm "Tạo" ────────────────────────────────────────────
    const bA = await buoc(ds, `${K}.1 Vào Flash Sale Của Shop + bấm Tạo`, async () => {
      await chay(pageCaiLuatNutTaoFlash, []);
      const SEL_SIDEBAR = 'a[href="/portal/marketing/shop-flash-sale/list"]';
      const truoc = await chay(() => location.href);
      // CHƯA ở trang danh sách ⇒ đi qua SIDEBAR. Hai vế, cần cả hai: đã RỜI /list (khung sau, vừa tạo xong,
      // Shopee còn ở trang chi tiết), hoặc chưa hề vào khu `/shop-flash-sale` (khung đầu, vừa mở shop).
      if (daRoiTrangList(truoc) || String(truoc || "").indexOf("/shop-flash-sale/list") < 0) {
        const coLink = await choHien(chay, SEL_SIDEBAR);
        if (!coLink) {
          return { selectorDung: null, truoc, sau: await chay(() => location.href),
                   loi: "KHÔNG thấy link 'Flash Sale Của Shop' trong sidebar sau 30s",
                   kyVong: "roi-list", docLai: "con-list" };
        }
        await chay(pageClickTheoSelector, [[SEL_SIDEBAR], null]);
        for (let i = 0; i < CHO_NHIP; i++) {
          await sleep(500);
          const u = await chay(() => location.href);
          if (String(u || "").indexOf("/shop-flash-sale/list") >= 0) { break; }
        }
      }

      // ⚠ CHỜ THÂN TRANG RENDER RỒI MỚI TÌM NÚT — xem {@link THAN_TRANG_NHIP_MS} cho ca hỏng câm 12:01:31.
      let choThan = await choThanTrangDanhSach(chay);
      if (!choThan.thay) {
        // SPA kẹt sau cú Kích hoạt hàng loạt của khung trước (đo 19:03 31/08: K2.1 "thân trang chưa render
        // sau 40 nhịp") — reload cứng một phát rồi chờ lại.
        await chay(() => { location.reload(); return 1; }, []);
        await sleep(5000);
        choThan = await choThanTrangDanhSach(chay);
      }
      if (!choThan.thay) {
        const dumpSom = await chay(pageDumpTrangDanhSach, [TRAN_UNG_VIEN_DUMP]);
        return {
          selectorDung: null, truoc, sau: { choThanTrang: choThan, dumpTrangDanhSach: dumpSom },
          loi: "THÂN TRANG danh sách chưa render sau " + choThan.soNhipCho + " nhịp — chưa có gì để neo nút Tạo",
          kyVong: "roi-list", docLai: "con-list",
        };
      }

      // ⚠ KHÔNG DỌN hàng 0-sp-bật nữa (chốt ~16:30 31/08): với form mới, hàng 0-bật là chương trình ĐÃ
      // DỰNG (khung + sp + giá) chờ BẬT — bước K*.7 tái chế nó (Chỉnh sửa → Bật → Lưu). Xoá nó là vứt
      // nguyên liệu. Đường xoá trusted (pageToaDoNutXoaChuongTrinhRong + dialog) giữ trong kho — đã chạy
      // thật, dùng khi cần dọn tay.
      const soDaXoa = 0;

      const bam = await chay(pageBamNutTaoFlash, []);
      let sau = truoc;
      let choOKhung = null;
      if (bam && bam.ok) {
        for (let i = 0; i < CHO_NHIP; i++) {
          await sleep(500);
          sau = await chay(() => location.href);
          if (daRoiTrangList(sau)) { break; }
        }
        choOKhung = await choHienCoNhip(chay, "button.time-slot-btn");
      }
      const roi = daRoiTrangList(sau);
      return {
        selectorDung: bam && bam.ok ? bam.nguonNutTao : null,
        truoc,
        sau: { url: sau, roiTrangList: roi, choThanTrang: choThan, choOKhungThoiGian: choOKhung, bam, soDaXoa },
        loi: bam && bam.ok ? null : (bam && bam.loi) || "không tìm được nút Tạo",
        kyVong: "roi-list", docLai: roi ? "roi-list" : "con-list",
        ghiChu: `Khung thứ ${vong + 1} của ngày ${tp.moTa}.`
          + (soDaXoa > 0 ? ` Đã DỌN ${soDaXoa} chương trình rỗng trước khi tạo.` : ""),
      };
    }, tabId, NHAN);
    if (!bA.ok) { return ketThuc({ soSanPham: soSpDaNap, daTao: soKhungDaTao > 0 || soTaiChe > 0, soKhungDaTao, soTaiChe }); }

    // ── B) Mở modal khung giờ → chờ LỊCH LẮNG → tới đúng tháng đích → chọn NGÀY ĐÍCH → chờ KHUNG LẮNG ──
    let dumpKhung = null;
    let ngayMaiKhongDo = false;
    const bB = await buoc(ds, `${K}.2 Chọn ngày ${tp.moTa} + chờ khung giờ`, async () => {
      await chay(pageCaiLuatONgayEds, []);
      await chay(pageCaiLuatMuiTenThang, []);
      await chay(pageCaiLuatNgayDoFlash, []);
      await chay(pageCaiLuatSoNgayFlash, []);
      await chay(pageCaiLuatTieuDeThangFlash, []);

      await chay(pageClickTheoSelector, [["button.time-slot-btn"], null]);
      const soO = await choHien(chay, ".eds-date-table__cell");
      if (!soO) {
        return { sau: { soONgay: 0 }, loi: "modal khung giờ KHÔNG mở (0 ô ngày sau 30s)",
                 kyVong: "co", docLai: "khong" };
      }

      // ⚠ ĐỌC LỊCH ĐÚNG MỘT LẦN MỖI SHOP — xem khối chú thích ở chỗ khai `lichShop`. Khung thứ hai trở đi của
      // cùng một shop KHÔNG đọc lại: danh sách ngày đỏ là của SHOPEE, nó không đổi giữa hai khung.
      const phaiDocLich = lichShop === null || lichShop.soLatThang > 0;
      let nhatKyThang = lichShop ? lichShop.nhatKyThang : [];

      if (phaiDocLich) {
        soLanDocLich++;
        let choLich = await choLichLang(chay);
        let lich = choLich.lich;
        nhatKyThang = [{ thang: moTaThang(lich), soNgayDo: lich ? lich.soNgayDo : null, daLang: choLich.lang }];

        // NGÀY ĐÍCH có thể nằm ở THÁNG SAU (đêm cuối tháng) ⇒ lật tới đúng tháng, tối đa 1 bước.
        let daLat = 0;
        while (lich && lich.coLich && (lich.thang !== tp.thang || lich.nam !== tp.nam) && daLat < TRAN_THANG_SANG) {
          const tien = (tp.nam * 12 + tp.thang) > (lich.nam * 12 + lich.thang);
          const bamThang = await chay(pageBamMuiTenThangFlash, [tien]);
          if (!bamThang || !bamThang.ok) {
            return { sau: { nhatKyThang, bamThang },
                     loi: "không lật được lịch tới tháng " + tp.thang + "/" + tp.nam + ": "
                       + ((bamThang && bamThang.loi) || "(không rõ)"),
                     kyVong: "co", docLai: "khong" };
          }
          const doi = await choDoiThang(chay, lich, tien);
          daLat++;
          if (!doi.daDoi || !doi.dungThang) {
            return { sau: { nhatKyThang, lac: { mongDoi: doi.mongDoi, thucTe: moTaThang(doi.lich) } },
                     loi: "bấm đổi tháng mà lịch đáp xuống " + moTaThang(doi.lich) + " — LẠC THÁNG",
                     kyVong: "co", docLai: "khong" };
          }
          lich = doi.lich;
          choLich = doi.cho || choLich;
          nhatKyThang.push({ thang: moTaThang(lich), soNgayDo: lich ? lich.soNgayDo : null, daLang: doi.lang });
        }

        // CỬA CHẶN "QUYẾT ĐỊNH TRÊN DỮ LIỆU RÁC": lịch chưa lắng thì con số ngày đỏ vô nghĩa.
        const ketLuan = ketLuanNgayDo(lich, choLich.lang);
        if (!ketLuan.chac) {
          return { sau: { nhatKyThang, ketLuan },
                   loi: "KHÔNG kết luận được ngày đỏ: " + ketLuan.ly + " — DỪNG, không chọn ngày nào",
                   kyVong: "co", docLai: "khong" };
        }

        // NGÀY ĐÍCH có ĐỎ không? KHÔNG đỏ ⇒ đây KHÔNG phải lỗi: đêm nay Shopee không mở khung cho ngày mai.
        if (ngayDoCuaLich(lich).indexOf(tp.ngay) < 0) {
          ngayMaiKhongDo = true;
          return {
            sau: { nhatKyThang, ngayDo: ngayDoCuaLich(lich), soLanDocLich }, ok: true, ngayMaiKhongDo: true,
            ghiChu: "NGÀY ĐÍCH " + tp.moTa + " KHÔNG phải ngày đỏ (Shopee chưa mở khung Flash Sale cho ngày đó). "
              + "Ngày đỏ đang có: [" + ngayDoCuaLich(lich).join(",") + "]. BỎ QUA shop, KHÔNG tạo gì.",
          };
        }

        // GHI NHỚ cho các khung sau của CHÍNH shop này.
        lichShop = { soLatThang: daLat, ngayDo: ngayDoCuaLich(lich), nhatKyThang };
      }

      // ── CỬA CHỜ "Ô NGÀY ĐÍCH ĐÃ ĐỎ CHƯA" — xem {@link CHO_O_NGAY_DO_NHIP} ──────────────────────────────
      // Lưới ô ngày render TRƯỚC khi dữ liệu khung của tháng về, nên "0 ô đỏ" ngay sau khi modal mở là trạng
      // thái TẠM, không phải kết luận. Chỉ thử lại đúng ca ĐÓ (câu lỗi "đang ĐỎ" của `pageClickNgayFlash`);
      // mọi lỗi khác (chưa cài luật, không thấy `.eds-date-table__rows`) là hỏng THẬT ⇒ thoát ngay, đừng
      // đứng chờ 10s cho một thứ không bao giờ tự lành.
      let click = null;
      let soNhipChoNgayDo = 0;
      for (let i = 0; i < CHO_O_NGAY_DO_NHIP; i++) {
        if (i > 0) { soNhipChoNgayDo++; await sleep(500); }
        click = await chay(pageClickNgayFlash, [tp.ngay]);
        if (click && click.ok) { break; }
        if (!/đang ĐỎ/.test(String((click && click.loi) || ""))) { break; }
      }
      if (!click || !click.ok) {
        return { sau: { nhatKyThang, click, soNhipChoNgayDo },
                 loi: (click && click.loi) || "không click được ô ngày đích",
                 kyVong: "co", docLai: "khong" };
      }
      // ⚠ CHỜ BẢNG KHUNG GIỜ LẮNG — KHÔNG dùng `choHien` cho radio (EDS ẩn hẳn thẻ <input>, đếm theo kích
      // thước trả 0 vĩnh viễn).
      const choKhung = await choKhungGioLang(chay);
      dumpKhung = await chay(pageDumpKhungGioFlash, []);
      return {
        sau: { nhatKyThang, click, soNhipChoNgayDo, choKhungGio: choKhung,
               soKhung: dumpKhung ? dumpKhung.soKhung : 0,
               daDocLaiLich: phaiDocLich, soLanDocLich },
        loi: choKhung.lang ? null : "bảng khung giờ CHƯA LẮNG sau " + choKhung.soNhip + " nhịp",
        kyVong: "co", docLai: choKhung.soRadio > 0 ? "co" : "khong",
        ghiChu: "Đã chọn ngày " + tp.moTa + " (ô ngày đỏ sau " + soNhipChoNgayDo + " nhịp chờ) · "
          + choKhung.soRadio + " khung giờ, lắng sau "
          + (phaiDocLich ? "ĐỌC (lần " + soLanDocLich + ")" : "dùng lại bản đọc ở khung đầu") + ".",
      };
    }, tabId, NHAN);
    if (ngayMaiKhongDo) {
      // ⚠ 16:33 31/08: "ngày đỏ" tắt CẢ khi shop này hết khung trống (đo: đỏ lúc 16:31, K1 dựng nốt khung
      // cuối, 16:33 hết đỏ) — không chỉ khi Shopee chưa mở ngày. K0 đã tái chế xong phần đã nở nên daTao
      // phải tính cả soTaiChe.
      // ĐÃ XỬ 01/09/2026 (sổ P-20260831-3): cờ này KHÔNG còn dừng cả lượt ở phía C#. Shop này tính là BỎ QUA
      // như mọi `boQua` khác rồi vòng đi tiếp; chỉ khi MỌI shop của lượt cùng khai cờ mà không shop nào tạo
      // được thì `KetQuaKm.NgayMaiKhongDo` mới bật — lúc đó mới thật là "Shopee chưa mở ngày".
      return ketThuc({
        soSanPham: soSpDaNap, daTao: soKhungDaTao > 0 || soTaiChe > 0, soKhungDaTao, soTaiChe, ngayMaiKhongDo: true,
        boQua: "ngày đích " + tp.moTa + " KHÔNG phải ngày đỏ — Shopee chưa mở khung Flash Sale cho ngày đó"
          + (soTaiChe > 0 ? " (đã TÁI CHẾ " + soTaiChe + " chương trình trước đó)" : ""),
      });
    }
    if (!bB.ok) { return ketThuc({ soSanPham: soSpDaNap, daTao: soKhungDaTao > 0 || soTaiChe > 0, soKhungDaTao, soTaiChe }); }

    // ── C) Tick khung CHƯA DÙNG trong lượt này → Xác nhận modal khung giờ ─────────────────────────────
    let hetKhung = false;
    let cauHetKhung = null;
    const bC = await buoc(ds, `${K}.3 Tick khung giờ chưa dùng + Xác nhận`, async () => {
      const dsKhung = (dumpKhung && dumpKhung.khung) || [];
      const chon = khungChuaDung(dsKhung, khungDaDung);
      if (chon.chiSo < 0) {
        hetKhung = true;
        // ⚠ CÂU NÀY LÀ THỨ PHÂN BIỆT "shop hết khung" (nghiệp vụ) VỚI "Shopee đổi markup" (hỏng thật).
        // Bản đầu chỉ `ok:true` rồi trả `daTao:false` mà KHÔNG đặt `boQua` ⇒ phía C# rơi vào nhánh cuối và ghi
        // `"không tạo được chương trình"` — một câu không nêu bước nào, cho một shop chẳng có gì sai. Khi
        // Shopee đổi `value` của radio thì MỌI shop ra đúng câu đó, đêm nào cũng lặp, và không ai lần ra.
        const soBiKhoa = dsKhung.filter((k) => k && k.disabled === true).length;
        const soKhongValue = dsKhung.filter((k) => {
          if (!k || k.disabled === true) { return false; }
          return k.value === null || k.value === undefined || String(k.value).length === 0;
        }).length;
        cauHetKhung = "ngày " + tp.moTa + " không còn khung nào chọn được (tổng " + dsKhung.length
          + " khung: " + soBiKhoa + " bị Shopee khoá, " + soKhongValue + " không đọc được value, "
          + khungDaDung.length + " đã dùng trong lượt này)";
        return { sau: { chon, khungDaDung: khungDaDung.slice(), soKhungTong: dsKhung.length,
                        soKhungBiKhoa: soBiKhoa, soKhungKhongValue: soKhongValue }, ok: true,
                 ghiChu: "HẾT khung chưa dùng — " + cauHetKhung + ". Xong shop." };
      }
      const tick = await chay(pageTickKhungFlash, [chon.chiSo]);
      if (!tick || !tick.ok || !tick.checked) {
        return { sau: { chon, tick }, loi: (tick && tick.loi) || "tick khung giờ không ăn",
                 kyVong: "co", docLai: "khong" };
      }
      // GHI NHỚ NGAY SAU KHI TICK ĂN, đừng đợi tới lúc tạo xong: khung hỏng ở bước sau mà không ghi nhớ thì
      // vòng kế chọn lại đúng nó và hỏng y hệt, tới tận trần kỹ thuật.
      khungDaDung.push(String(tick.value === null || tick.value === undefined ? chon.value : tick.value));
      await sleep(600);
      // Xác nhận khung giờ bằng CHUỘT THẬT (đo 15:12–15:16: click DOM đóng modal + điền chữ nhưng state
      // form không nhận khung ⇒ mọi toggle Bật bị khoá "Vui lòng lựa chọn khung giờ").
      await ensureDbg(tabId);
      const xacNhan = await chay(pageToaDoNutXacNhanKhung, []);
      if (!xacNhan || !xacNhan.ok) {
        return { sau: { chon, tick, xacNhan }, loi: (xacNhan && xacNhan.loi) || "không tìm được nút Xác nhận modal khung giờ",
                 kyVong: "co", docLai: "khong" };
      }
      await trustedClick(tabId, xacNhan.x, xacNhan.y);
      const daDien = await choHien(chay, "button.time-slot-btn.has-value");

      // FORM MỚI 31/08: Xác nhận khung = Shopee TẠO chương trình NGAY phía server ("dò dom test tạo ra
      // rất nhiều chương trình, dù không click vào xác nhận" — người dùng 17:4x). Dialog "Tạo thành công"
      // nở MUỘN 1–2 phút — bước K*.7 chờ và xử; ở đây đi tiếp điền form ngay như người thật.
      return {
        sau: { chon, tick, xacNhan, soODaCoGiaTri: daDien },
        kyVong: "co", docLai: daDien > 0 ? "co" : "khong",
        ghiChu: "Khung '" + (chon.nhan || chon.value) + "' (value=" + chon.value + "). Đã dùng "
          + khungDaDung.length + " khung trong lượt này. Shopee TẠO chương trình ngay từ cú xác nhận này"
          + " — dialog 'Tạo thành công' nở muộn, K*.7 xử.",
      };
    }, tabId, NHAN);
    if (hetKhung) {
      // KHÔNG tạo được khung nào ⇒ đây là KẾT LUẬN NGHIỆP VỤ, phải khai `boQua` (shop được đếm vào `SoBoQua`,
      // không phải `SoHong`). Tạo được rồi thì `break` như thường: lượt vẫn thành công, chỉ là hết khung.
      if (soKhungDaTao === 0) {
        return ketThuc({ soSanPham: soSpDaNap, daTao: false, soKhungDaTao,
                         boQua: cauHetKhung || "ngày " + tp.moTa + " không còn khung nào chọn được" });
      }
      break;
    }
    if (!bC.ok) { return ketThuc({ soSanPham: soSpDaNap, daTao: soKhungDaTao > 0 || soTaiChe > 0, soKhungDaTao, soTaiChe }); }

    // ── D) Thêm sản phẩm → chờ bảng LẮNG → bấm sắp xếp ĐÚNG SO_LAN_BAM_SAP_XEP cú ─────────────────────
    // ══════════════════════════════════════════════════════════════════════════════════════════════════
    // PHA "SẮP XẾP THẬT" — ĐÚNG {@link SO_LAN_BAM_SAP_XEP} cú `down`, KHÔNG BAO GIỜ BA.
    // ⚠ VÒNG NÀY TUYỆT ĐỐI KHÔNG ĐƯỢC BỌC THỬ LẠI: một cú bấm thừa là XOÁ SẠCH sắp xếp (đo thật lượt 11: M3
    // quay về giống hệt M0), và mọi bước sau vẫn `ok` — luồng sẽ nạp 10 sản phẩm TUỲ Ý, im lặng.
    // ══════════════════════════════════════════════════════════════════════════════════════════════════
    let docDoanhSo = null;
    // Kết quả cửa kiểm của TRANG 1 — khai ở đây (scope vòng khung) để bước F đòi được BẰNG CHỨNG cửa đã đạt,
    // thay vì tin vào vị trí dòng code. Xem {@link congLatTrang}.
    let cuaTrang1 = null;
    const bD = await buoc(ds, `${K}.4 Thêm sản phẩm + sắp xếp ${SO_LAN_BAM_SAP_XEP} cú`, async () => {
      await chay(pageCaiLuatBangSanPham, []);
      const sel = await chay(pageClickTheoSelector,
        [[".detail-products-container .empty-content button", ".detail-products-container button"], "thêm sản phẩm"]);
      const soDong = await choHien(chay, "tr.mkt-product-selector-row-in-root");
      if (!soDong) {
        return { selectorDung: sel, sau: { soDong: 0 },
                 loi: "modal 'Chọn Sản Phẩm' KHÔNG mở (0 dòng sau 30s)", kyVong: "co", docLai: "khong" };
      }
      const choBang = await choBangSanPhamLang(chay);

      const vanTay = async () => {
        const d = await chay(pageVanTayModalSanPham, []);
        return d ? d.van : null;
      };
      const bam = [];
      const moc = [];
      for (let i = 0; i < SO_LAN_BAM_SAP_XEP; i++) {
        const vanTruocBam = await vanTay();
        bam.push(await chay(pageBamSapXepDoanhSo, ["down"]));
        moc.push(await choBangSauBamSapXep(chay, vanTruocBam));
      }
      const hongBam = bam.filter((b) => !b || !b.ok);
      // ⚠ GIỮ TRỌN GÓI, ĐỪNG BÓC `giaTri` RA. `coCot`/`soDong`/`loi` là thứ làm cửa kiểm ở bước E
      // FAIL-CLOSED — bản đầu bóc mảng ra và bốn lối ra "không đọc được" đều lọt thành ĐẠT.
      docDoanhSo = await chay(pageDocDoanhSoMoiDongFlash, []);
      return {
        selectorDung: sel,
        sau: { choBangSanPham: choBang, bam, moc, docDoanhSo,
               nguonTinHieu: moc.map((m) => m.nguonTinHieu) },
        loi: hongBam.length ? "cú bấm sắp xếp KHÔNG ăn: " + ((hongBam[0] && hongBam[0].loi) || "(không rõ)") : null,
        kyVong: String(SO_LAN_BAM_SAP_XEP), docLai: String(bam.length - hongBam.length),
        ghiChu: "Đúng " + SO_LAN_BAM_SAP_XEP + " cú 'down' = giảm dần (số đo lượt 11). KHÔNG có cú thứ ba, và "
          + "KHÔNG có cơ chế thử lại quanh cú bấm.",
      };
    }, tabId, NHAN);
    if (!bD.ok) { return ketThuc({ soSanPham: soSpDaNap, daTao: soKhungDaTao > 0 || soTaiChe > 0, soKhungDaTao, soTaiChe }); }

    // ── E) CỬA KIỂM: dãy Doanh số phải KHÔNG TĂNG DẦN ────────────────────────────────────────────────
    const bE = await buoc(ds, `${K}.5 CỬA KIỂM Doanh số (phải KHÔNG tăng dần)`, async () => {
      const cua = dayDoanhSoKhongTangDan(docDoanhSo);
      cuaTrang1 = cua;
      return {
        sau: {
          cua,
          // Bằng chứng ĐỌC ĐƯỢC GÌ — không có mấy trường này thì một cửa đỏ chỉ nói "không đạt" mà không nói
          // vì sao, và người đọc nhật ký lại phải chạy một lượt dò nữa.
          coCot: docDoanhSo ? docDoanhSo.coCot : null,
          soDongDocDuoc: docDoanhSo ? docDoanhSo.soDong : null,
          loiDoc: docDoanhSo ? (docDoanhSo.loi || null) : null,
          nhanCacCot: docDoanhSo ? (docDoanhSo.nhanTh || docDoanhSo.nhanCacCot || null) : null,
          headerLaBangRieng: docDoanhSo ? docDoanhSo.headerLaBangRieng : null,
          doanhSo: docDoanhSo ? docDoanhSo.giaTri : null,
        },
        loi: cua.dat ? null : "CỬA KIỂM SẮP XẾP KHÔNG ĐẠT — " + cua.ly,
        kyVong: "khong-tang-dan", docLai: cua.dat ? "khong-tang-dan" : "TANG-DAN",
        ghiChu: cua.ly,
      };
    }, tabId, NHAN);
    if (!bE.ok) { return ketThuc({ soSanPham: soSpDaNap, daTao: soKhungDaTao > 0 || soTaiChe > 0, soKhungDaTao, soTaiChe }); }

    // ── F) Lật tới TRANG thứ k → tick "chọn tất cả" → Xác nhận modal sản phẩm ────────────────────────
    // ⚠ ĐỌC SỐ TRANG THẬT TRƯỚC KHI ĐẶT TÊN BƯỚC — modal sản phẩm đang mở từ K*.4/K*.5, pager của nó là chỗ
    // DUY NHẤT nói shop có mấy trang. Không biết số trang thì `trangChoKhung` lật quá trang ở khung thứ 4 của
    // một shop 3 trang ⇒ bước ĐỎ ⇒ mất nốt mọi khung còn lại (P-20260903-4).
    // ⚠ CÓ try/catch VÌ LỜI GỌI NÀY NẰM NGOÀI `buoc(...)`: mọi `chay` khác của vòng đều chạy trong thân một
    // bước, nên `buoc` biến cú ném thành bước ĐỎ + gói kết quả. Ném ở ĐÂY thì nó thoát ra tận
    // `core.js` (`commandHandler(cmd).catch`) và shop này KHÔNG có gói `flashKetQua` nào — C# ngồi chờ hết
    // `ChoChang.FlashChay` = 2700s cho một lỗi đọc pager. Không đọc được ⇒ coi như 1 trang, để bước F tự chấm.
    let demTruocLat = null;
    try {
      demTruocLat = await chay(pageDemTickFlash, []);
    } catch (e) {
      canhBao.push(`${K}: không đọc được pager modal (${(e && e.message) || e}) — coi như 1 trang`);
    }
    const soTrang = soTrangTuNhan(demTruocLat && demTruocLat.nhanCacTrang);
    if (demTruocLat && demTruocLat.coModal
        && (!demTruocLat.nhanCacTrang || demTruocLat.nhanCacTrang.length === 0)
        && demTruocLat.soDong >= SO_SP_MOI_TRANG) {
      canhBao.push(`${K}: pager không đọc được (${demTruocLat.soDong} dòng mà 0 nhãn trang) — coi như 1 trang`);
    }
    const trangDich = trangChoKhung(soKhungDaTao, soTrang);
    let soTickKhungNay = 0;
    const bF = await buoc(ds, `${K}.6 Trang ${trangDich}/${soTrang} + chọn tất cả + Xác nhận modal`, async () => {
      // ── CỔNG: chưa có cửa kiểm sắp xếp ĐẠT cho trang 1 thì KHÔNG lật, KHÔNG tick ──────────────────────
      // Fail-closed cả ca "cửa đỏ" lẫn ca "quên gọi cửa" — xem {@link congLatTrang}.
      const cong = congLatTrang(cuaTrang1);
      if (!cong.dat) {
        return { sau: { cong, trangDich }, loi: cong.ly, kyVong: "co", docLai: "khong" };
      }
      let soCuoiTrangTruoc = cong.soCuoi;

      const nhatKyLat = [];
      for (let t = 1; t < trangDich; t++) {
        const d = await chay(pageVanTayModalSanPham, []);
        const vanTruoc = d ? d.van : null;
        const sang = await chay(pageSangTrangSauFlash, []);
        if (!sang || !sang.ok) {
          return { sau: { nhatKyLat, sang, trangDich },
                   loi: "không lật được tới trang " + trangDich + " (dừng ở trang " + t + "): "
                     + ((sang && sang.loi) || "(không rõ)"),
                   kyVong: "co", docLai: "khong" };
        }
        const doi = await choBangDoiRoiLang(chay, vanTruoc);
        nhatKyLat.push({ tuTrang: t, daDoi: doi.daDoi, lang: doi.lang, soDong: doi.soDong });
        if (!doi.daDoi) {
          return { sau: { nhatKyLat, trangDich },
                   loi: "bảng KHÔNG đổi sau khi bấm sang trang " + (t + 1)
                     + " — không dám tick, sẽ chọn nhầm sản phẩm của trang khác",
                   kyVong: "co", docLai: "khong" };
        }

        // ⚠ ĐỌC SAU KHI BẢNG ĐÃ LẮNG (`choBangDoiRoiLang` ngay trên) — đọc sớm là đọc bảng đang vẽ.
        const docTrang = await chay(pageDocDoanhSoMoiDongFlash, []);
        const cuaTrang = cuaKiemTrangSauLat(docTrang, soCuoiTrangTruoc);
        // GÁN BỔ SUNG vào ĐÚNG phần tử vừa push ở trên — KHÔNG push phần tử thứ hai cho cùng một cú lật, dump
        // đổi hình dạng (2 phần tử/lật) là thứ người đọc nhật ký sau này phải đoán.
        const mucLat = nhatKyLat[nhatKyLat.length - 1];
        mucLat.doanhSo = docTrang ? docTrang.giaTri : null;
        mucLat.cua = { dat: cuaTrang.dat, ly: cuaTrang.ly };
        if (!cuaTrang.dat) {
          // KHÔNG có nhánh sửa chữa: bấm sắp xếp thêm một cú là XOÁ SẠCH sắp xếp (đo thật lượt 11). Vỡ ở đâu
          // dừng ở đó.
          // ⚠ CÂU LỖI PHẢI CHỈ ĐÚNG THỦ PHẠM. Cửa đỏ vì KHÔNG ĐỌC ĐƯỢC (mất cột / 0 dòng / lệch cột) mà vẫn
          // nói "không còn giảm dần" thì người vận hành đi nghi cú bấm sắp xếp, trong khi hỏng nằm ở phép ĐỌC.
          // Vẫn ĐỎ như nhau (fail-closed không đổi) — chỉ đổi lời.
          return { sau: { nhatKyLat, trangDich },
                   loi: cuaTrang.viThuTu === true
                     ? "sau khi lật tới trang " + (t + 1) + ", dãy Doanh số không còn giảm dần: " + cuaTrang.ly
                     : "KHÔNG kiểm được thứ tự ở trang " + (t + 1) + ": " + cuaTrang.ly,
                   kyVong: "co", docLai: "khong" };
        }
        soCuoiTrangTruoc = cuaTrang.soCuoi;
      }

      // ── CỬA: đang đứng ĐÚNG trang k chứ? CHỈ hỏi khi có lật — khung 1 không thêm một lượt gọi nào ───────
      if (trangDich > 1) {
        const demTrang = await chay(pageDemTickFlash, []);
        const dung = cuaDungTrangDich(demTrang ? demTrang.trangHienTai : null, trangDich);
        if (!dung.dat) {
          return { sau: { nhatKyLat, demTrang, trangDich }, loi: dung.ly, kyVong: "co", docLai: "khong" };
        }
        if (dung.canhBao) { canhBao.push(`${K}: ${dung.canhBao}`); }
      }

      const tick = await chay(pageTickChonTatCaFlash, []);
      if (!tick || !tick.ok || !tick.checked) {
        return { sau: { nhatKyLat, tick }, loi: (tick && tick.loi) || "tick 'chọn tất cả' không ăn",
                 kyVong: "co", docLai: "khong" };
      }
      const dem = await chay(pageDemTickFlash, []);
      soTickKhungNay = dem && dem.coModal ? dem.soDaTick : 0;
      if (dem && dem.coModal && dem.soDaTick < dem.soTickDuoc) {
        canhBao.push(`${K}: trang ${trangDich} mới tick ${dem.soDaTick}/${dem.soTickDuoc} dòng tick được`);
      }
      // BỘ ĐẾM CHỮ trong modal SAU cú tick (30/08 22:40): `dem.soDaTick` đọc DOM property `checked` — mà số
      // đo thật cho thấy form nhận 0/10 dù dem=10, tức DOM property có thể nói dối state Vue. Chuỗi "đã chọn N"
      // do CHÍNH Shopee vẽ là phép đọc lại không tự lừa được.
      const chuDaChonModal = await chay(() => {
        const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
        const hien = (e) => !!e && e.getClientRects().length > 0;
        const ungVien = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
        const modal = ungVien.find((m) => m.querySelector("tr.mkt-product-selector-row-in-root") && hien(m)) || null;
        if (!modal) { return null; }
        return Array.from(modal.querySelectorAll("div, span, p"))
          .map((e) => gon(e.textContent))
          .filter((t) => t && t.length < 60 && /đã chọn|da chon/i.test(t))
          .slice(0, 4);
      }, []);

      // Attach debugger TRƯỚC khi đọc toạ độ (luật dbg-input.js: toạ độ + cú click phải cùng trạng thái
      // banner "đang gỡ lỗi", lệch là bấm trượt).
      await ensureDbg(tabId);
      const xn = await chay(pageToaDoNutXacNhanModalSp, []);
      if (!xn || !xn.ok) {
        return { sau: { nhatKyLat, tick, dem, xn }, loi: (xn && xn.loi) || "không tìm được nút Xác nhận modal sản phẩm",
                 kyVong: "co", docLai: "khong" };
      }
      // CỬA "TICK CÒN SỐNG NGAY TRƯỚC CÚ BẤM" (chẩn đoán 31/08 giữ lại làm lưới): checkbox từng dòng +
      // bộ đếm footer phải còn giữ số — tick bay giữa chừng (bảng re-render) thì Xác nhận mang con số 0 đi.
      const truocBam = await chay(() => {
        const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
        const oTick = Array.from(document.querySelectorAll("tr.mkt-product-selector-row-in-root input[type='checkbox']"));
        const chuFooter = Array.from(document.querySelectorAll("div, span"))
          .map((e) => gon(e.textContent))
          .find((t) => t && t.length < 40 && /đã chọn\s*\d+\s*\/\s*\d+/i.test(t)) || null;
        return { soOTick: oTick.length, soDangChecked: oTick.filter((c) => c.checked).length, chuFooter };
      }, []);
      if (truocBam && truocBam.soOTick > 0 && truocBam.soDangChecked === 0) {
        return { sau: { nhatKyLat, tick, dem, chuDaChonModal, truocBam, xn },
                 loi: "tick BAY MẤT ngay trước cú Xác nhận (0/" + truocBam.soOTick
                   + " dòng còn checked; footer: " + (truocBam.chuFooter || "(không đọc được)")
                   + ") — bảng modal có thể vừa re-render",
                 kyVong: "co", docLai: "khong" };
      }
      await trustedClick(tabId, xn.x, xn.y);
      let conMo = 1;
      for (let i = 0; i < CHO_DONG_MODAL_NHIP; i++) {
        await sleep(LANG_NHIP_MS);
        conMo = await chay(pageDemPhanTuHien, ["tr.mkt-product-selector-row-in-root"]);
        if (conMo === 0) { break; }
      }

      // ── CỬA "SẢN PHẨM ĐÃ VÀO FORM" (đúc kết 3 đêm chẩn đoán 29–31/08): sau Xác nhận modal, sp vào
      // BẢNG PHÂN LOẠI của form ở trạng thái TẮT — bộ đếm "Bạn đã chọn N/10 sản phẩm" đếm sp ĐÃ BẬT nên
      // đứng ở 0 là BÌNH THƯỜNG tại thời điểm này (bẫy đã dính: coi 0/10 là "sp không vào form" trong khi
      // graphql + 10 thumbnail + ảnh chụp màn hình 11:42:54 chứng minh sp ĐANG NẰM trong bảng). Phép đo
      // đúng là của km: đếm ô %GIẢM từng dòng ngoài khối `.detail-batch` — mỗi phân loại một ô, có ô là sp
      // đã vào. CHỜ-LẶP thay vì đọc một phát (bài học km b6: máy chậm ⇒ shop có hàng bị bỏ oan).
      let bangPhanLoai = null;
      for (let i = 0; i < 20; i++) {
        bangPhanLoai = await chay(pageAnhBangSanPham, []);
        if (bangPhanLoai && bangPhanLoai.soO > 0) { break; }
        await sleep(500);
      }
      if (!bangPhanLoai || !(bangPhanLoai.soO > 0)) {
        return {
          sau: { nhatKyLat, tick, dem, chuDaChonModal, truocBam, xn, soDongConLai: conMo, bangPhanLoai },
          loi: "modal đã đóng mà form KHÔNG mọc ô %GIẢM nào sau 10s — sản phẩm không vào bảng phân loại"
            + " (modal sau tick nói: " + JSON.stringify(chuDaChonModal) + ")",
          kyVong: "DONG", docLai: "DONG_NHUNG_RONG",
        };
      }
      return {
        sau: { nhatKyLat, tick, dem, chuDaChonModal, xn, soDongConLai: conMo, bangPhanLoai,
               soTrang, nhanCacTrang: demTruocLat ? demTruocLat.nhanCacTrang : null },
        kyVong: "DONG", docLai: conMo === 0 ? "DONG" : "CON_MO",
        ghiChu: "Khung thứ " + (soKhungDaTao + 1) + " ⇒ TRANG " + trangDich + "/" + soTrang
          + " (đếm từ 1, quay vòng theo số trang thật). Đã tick "
          + soTickKhungNay + " sản phẩm (chuẩn " + SO_SP_MOI_TRANG + " sp/trang); form mọc "
          + bangPhanLoai.soO + " ô %GIẢM (mỗi phân loại một ô). Lật qua từng trang có cửa "
          + "Doanh số chỉ-đọc — cửa phát hiện bằng chứng NGƯỢC với giảm dần, vỡ ở đâu dừng ở đó.",
      };
    }, tabId, NHAN);
    if (!bF.ok) { return ketThuc({ soSanPham: soSpDaNap, daTao: soKhungDaTao > 0 || soTaiChe > 0, soKhungDaTao, soTaiChe }); }

    // ── F2) ÁP %GIẢM + SL SP KHUYẾN MÃI hàng loạt (quy trình tay người dùng 17:4x 31/08: "chọn sp
    // xong, nhập 5% giảm, số lượng sản phẩm tham gia 10, click cập nhật hàng loạt" — KHÔNG tick phân
    // loại, KHÔNG bấm nút Bật: nút đó disabled cả với NGƯỜI THẬT khi 0 phân loại được chọn, và việc kích
    // hoạt do modal "Kích hoạt hàng loạt" ở bước K*.7 lo. Cả buổi 31/08 đánh vật với toggle/Bật là đi
    // nhầm cửa). Ô bulk neo theo DOM thật `.batch-setting-panel` người dùng cung cấp.
    const bF2 = await buoc(ds, `${K}.6b Áp ${tp.phanTramGiam}%GIẢM + SL ${tp.soLuongThamGia} hàng loạt`, async () => {
      const bangTruoc = await chay(pageAnhBangSanPham, []);
      const go = await chay(pageGoPhanTramBulkFlash, [tp.phanTramGiam]);
      if (!go || !go.ok) {
        return { sau: { bangTruoc, go }, loi: (go && go.loi) || "không gõ được ô %GIẢM hàng loạt",
                 kyVong: "co", docLai: "khong" };
      }
      const goSl = await chay(pageGoSoLuongKmBulkFlash, [tp.soLuongThamGia]);
      await sleep(600);
      const capNhat = await chay(pageClickTheoSelector, [["button"], "cập nhật hàng loạt"]);
      if (!capNhat) {
        return { sau: { bangTruoc, go, goSl }, loi: "không thấy nút 'Cập nhật hàng loạt'",
                 kyVong: "co", docLai: "khong" };
      }
      await sleep(2500);
      const bangSau = await chay(pageAnhBangSanPham, []);
      return {
        sau: { bangTruoc, go, goSl, capNhat, bangSau },
        kyVong: "co", docLai: (bangSau && bangSau.soO > 0) ? "co" : "khong",
        ghiChu: "Đã áp " + tp.phanTramGiam + "%GIẢM + SL " + tp.soLuongThamGia + " hàng loạt (gõ %: "
          + JSON.stringify(go) + "; gõ SL: " + JSON.stringify(goSl) + "); ô %GIẢM có số: "
          + (bangSau ? bangSau.soODaCoSo + "/" + bangSau.soO : "?") + ".",
      };
    }, tabId, NHAN);
    if (!bF2.ok) { return ketThuc({ soSanPham: soSpDaNap, daTao: soKhungDaTao > 0 || soTaiChe > 0, soKhungDaTao, soTaiChe }); }

    // ── G) CHỜ dialog "Tạo thành công" nở → "Xem chi tiết" → trang EDIT (state chuẩn) → điền + Xác nhận
    // + "Kích hoạt hàng loạt" (v28 18:25: cú Xác nhận trên form create XÁC SỐNG câm hoàn toàn — không cả
    // modal Chú ý; modal đó chỉ hiện trên form có state khung chuẩn = trang chỉnh sửa. Dialog Tạo-thành-công
    // nở muộn 1–2 phút sau xác nhận khung — đo 14:00/14:37 — đúng tầm bước này chạy). ────────────────────
    const bG = await buoc(ds, `${K}.7 Xem chi tiết → Xác nhận + Kích hoạt hàng loạt (TẠO THẬT)`, async () => {
      // ⚠ VÒNG CHỜ NÀY LÀ THỨ ĐÃ GÂY HỒI QUY f9e118d — xem {@link CHO_DIALOG_TAO_NHIP} trước khi hạ số.
      // LỊCH SỬ: 31/08 19:35 hạ 240 → 20 nhịp với lý do "dialog chưa bao giờ nở ở đây, 2 phút chờ làm mỗi
      // khung tốn 3,5' và lượt 8 khung vượt trần chờ C# 900s". Lý do đó nay hết hiệu lực (FlashChay = 2700s),
      // còn cái giá thì đo được: 9/9 lượt shop 01–03/09 chỉ tạo được 1 khung/ngày. Trả lại 240 nhịp 03/09.
      // Đường chính vẫn là điền + Xác nhận + Kích hoạt ngay trên form hiện tại — dialog nở thì đi qua trang
      // chi tiết, không nở thì cảnh báo rồi làm thẳng.
      let dlgTao = null;
      for (let i = 0; i < CHO_DIALOG_TAO_NHIP; i++) {
        await sleep(500);
        dlgTao = await chay(pageDocDialogTaoThanhCong, []);
        if (dlgTao && dlgTao.thay) { break; }
      }
      let vaoChiTiet = null;
      if (dlgTao && dlgTao.thay) {
        const xct = await chay(pageToaDoNutXemChiTiet, []);
        if (!xct || !xct.ok) {
          return { sau: { dlgTao, xct },
                   loi: "dialog 'Tạo thành công' đã nở (" + dlgTao.text.slice(0, 100)
                     + ") mà không thấy nút 'Xem chi tiết'",
                   kyVong: "DA_ROI", docLai: "CON_O_FORM" };
        }
        await ensureDbg(tabId);
        await trustedClick(tabId, xct.x, xct.y);
        let urlEdit = "";
        for (let i = 0; i < 20; i++) {
          await sleep(500);
          urlEdit = await chay(() => location.href);
          if (String(urlEdit || "").indexOf("/create") < 0) { break; }
        }
        // Chờ bảng phân loại của trang edit mọc rồi mới điền.
        let bangEdit = null;
        for (let i = 0; i < 30; i++) {
          await sleep(500);
          bangEdit = await chay(pageAnhBangSanPham, []);
          if (bangEdit && bangEdit.soO > 0) { break; }
        }
        vaoChiTiet = { urlEdit, bangEdit };
        if (!bangEdit || !(bangEdit.soO > 0)) {
          return { sau: { dlgTao, vaoChiTiet },
                   loi: "đã sang trang chi tiết (" + urlEdit + ") mà không mọc ô %GIẢM nào sau 15s",
                   kyVong: "DA_ROI", docLai: "EDIT_KHONG_LOAD" };
        }
      } else {
        canhBao.push(`${K}.7: dialog 'Tạo thành công' KHÔNG nở sau ${CHO_DIALOG_TAO_NHIP / 2}s`
          + " — thử Xác nhận thẳng trên form hiện tại");
      }
      const dien = await dienVaLuuKichHoat();
      const luu = dien.luu;
      if (!luu.thanhCong) {
        return { truoc: luu.truoc, sau: { dlgTao, vaoChiTiet, dien },
                 loi: luu.loi || ("đã " + (vaoChiTiet ? "sang trang chi tiết + " : "") + "điền + Xác nhận mà 60s"
                   + " không tín hiệu — modal Kích hoạt: " + (luu.kichHoat ? "ĐÃ bấm" : "KHÔNG thấy")
                   + "; Shopee nói: " + (luu.baoThay && luu.baoThay.length ? luu.baoThay.join(" | ") : "(im lặng)")
                   + "; nút thanh đáy: " + JSON.stringify(luu.bam)
                   + "; gõ bulk: " + JSON.stringify(dien.go) + "/" + JSON.stringify(dien.goSl)),
                 kyVong: "DA_ROI", docLai: "CON_O_FORM" };
      }
      return { truoc: luu.truoc, sau: { dlgTao, vaoChiTiet, dien }, kyVong: "DA_ROI", docLai: "DA_ROI",
               ghiChu: (vaoChiTiet ? "Xem chi tiết → " : "") + "điền bulk + Xác nhận"
                 + (luu.kichHoat ? " + Kích hoạt hàng loạt" : " (Shopee không hỏi kích hoạt)")
                 + " — " + (luu.toastThanhCong ? "tín hiệu thành công" : "đã rời trang") + "." };
    }, tabId, NHAN);
    if (!bG.ok) { return ketThuc({ soSanPham: soSpDaNap, daTao: soKhungDaTao > 0 || soTaiChe > 0, soKhungDaTao, soTaiChe }); }

    soKhungDaTao++;
    soSpDaNap += soTickKhungNay;
    send({
      action: "progress",
      message: `[${NHAN}] ngày ${tp.moTa}: đã TẠO + KÍCH HOẠT ${soKhungDaTao} khung `
        + `(trang ${trangDich}/${soTrang}, ${soTickKhungNay} sản phẩm).`,
    });

    // Chạm TRẦN KỸ THUẬT mà Shopee VẪN còn khung chọn được ⇒ memo `khungDaDung` không còn khớp thực tế (xem
    // {@link TRAN_VONG_KHUNG_KY_THUAT}). Không im lặng: một ngày không thể có quá 24 khung thật.
    if (vong === TRAN_VONG_KHUNG_KY_THUAT - 1) {
      canhBao.push(`chạm trần kỹ thuật ${TRAN_VONG_KHUNG_KY_THUAT} vòng mà Shopee vẫn còn khung `
        + "— kiểm memo khungDaDung (Shopee có thể đã đổi `value` radio giữa lượt)");
    }
  }

  return ketThuc({ soSanPham: soSpDaNap, daTao: soKhungDaTao > 0 || soTaiChe > 0, soKhungDaTao, soTaiChe });
}

/**
 * Lệnh `chayFlashSale` — LUỒNG THẬT: tạo Flash Sale cho shop đang mở, đủ mọi khung giờ của NGÀY ĐÍCH.
 *
 * <p><b>`token` trả lại NGUYÊN VĂN</b> (C# gửi `shopId`): channel chỉ nhận gói khớp token của lượt đang chờ,
 * không có nó thì một gói tới muộn của shop TRƯỚC được tính là kết quả của shop ĐANG chạy.</p>
 * <p>Gói đi về <b>`kind:"flashKetQua"`</b> — CÙNG KHUÔN dữ liệu với `kmKetQua` (để C# dùng lại
 * `DocKetQuaKmMotShop`) nhưng KIND RIÊNG, đúng lý do đã ghi cho `flashDump`: một gói `kmKetQua` tới muộn của
 * lượt KM/Combo trước không được phép tính thành kết quả của lượt Flash Sale đang chờ.</p>
 */
export async function doChayFlashSale(thamSo, token) {
  const kq = await chayFlashSaleChoTabDangMo(thamSo || {});
  const buocHong = (kq.buoc || []).filter((b) => !b.ok).map((b) => ({ ten: b.ten, loi: b.loi || null }));
  send({
    action: "pageData",
    kind: "flashKetQua",
    token: token === undefined ? null : token,
    data: JSON.stringify({
      luc: new Date().toISOString(),
      luong: "flashsale",
      soSanPham: kq.soSanPham,
      daTao: kq.daTao,
      boQua: kq.boQua,
      canhBao: kq.canhBao || [],
      captcha: !!kq.captcha,
      // Trường RIÊNG của luồng này: "ngày đích KHÔNG còn đỏ". C# đọc nó để biết ngày đích có thể đã đóng cửa
      // (từ 01/09/2026 nó KHÔNG còn dừng cả lượt — shop này chỉ tính là BỎ QUA, xem `KetQuaKm.NgayMaiKhongDo`).
      ngayMaiKhongDo: !!kq.ngayMaiKhongDo,
      // Hai con số của lượt: khung TẠO MỚI và khung TÁI CHẾ (K0 dựng lại phần đã nở). `soTaiChe` trước đây bị
      // rơi ở gói này dù `chayFlashSaleChoTabDangMo` vẫn trả — mà nó là một nửa của phép đếm `daTao`, nên nhật
      // ký C# đọc ra "tạo 0 khung" cho một shop đã tái chế thật (sổ P-20260831-5).
      soKhungDaTao: kq.soKhungDaTao || 0,
      soTaiChe: kq.soTaiChe || 0,
      buocHong: buocHong,
      buoc: kq.buoc,
    }, null, 2),
  });
}
