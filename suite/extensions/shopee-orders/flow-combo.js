// "Combo Khuyến Mãi" — chương trình thứ HAI của "KM của Shop" (plan `plans/2026-08-25-combo-khuyen-mai.md`,
// chặng B). Tạo combo trên Seller Centre bằng DOM thuần, cho SHOP ĐANG MỞ (tab do `openShopDetail` để lại).
//
// ══════════════════════════════════════════════════════════════════════════════════════════════════════════
// BỐN RÀNG BUỘC CỨNG — ĐỪNG NỚI (ba cái đầu bê nguyên từ `flow-km.js`, cái thứ tư là của riêng form React):
//
//  (1) MỘT BƯỚC CỐT LÕI HỎNG ⇒ DỪNG SHOP NGAY, không đi tiếp tới bước 10. Bước 10
//      (`footer button[type=submit].src-components---confirm--3svOo`) là nút TẠO THẬT: đi tới đó trên một
//      trạng thái đã vỡ nghĩa là tạo combo sai giờ / sai mức giảm rồi báo thành công.
//
//  (2) DOM THUẦN — KHÔNG chrome.debugger, KHÔNG CDP, KHÔNG Playwright để tương tác với trình duyệt sạch
//      (người dùng chốt 18/08/2026, khẳng định lại 26/08/2026). Bước nào không ăn bằng DOM thuần thì GHI RÕ
//      `ok:false` — kết quả ÂM TÍNH có giá trị NGANG dương tính, nó là căn cứ để hỏi lại người dùng.
//
//      ⚠ ĐÃ THỬ LẬT MỘT LẦN VÀ ĐÃ HOÀN NGUYÊN (26/08/2026) — đọc trước khi định thử lại. Bằng chứng dẫn tới
//      ý định lật, ghi lại nguyên vẹn vì nó vẫn ĐÚNG và vấn đề vẫn CHƯA có lời giải:
//        · 16:45 và 16:51 — hai shop tạo combo THÀNH CÔNG bằng `.click()` DOM thuần.
//        · 20:29 và 21:33 — HAI lượt liên tiếp hỏng Y HỆT: bước 1 `ok`, rồi cú bấm "Tạo" ở bước 2 làm Shopee
//          đá thẳng sang `shopee.vn/verify/traffic/error`. Cùng shop, cùng bước, tái hiện được.
//        · Cùng lúc đó vòng CHECK ĐƠN trên chính tài khoản ấy chạy bình thường ⇒ KHÔNG phải anti-bot toàn cục.
//        · Người dùng bấm TAY đúng luồng đó trên đúng shop đó: vào form bình thường, KHÔNG verify.
//      ⇒ Thứ bị chặn là CÚ BẤM CỦA APP, không phải tài khoản. `.click()` của JS cho `isTrusted: false`.
//      Bản vá thử nghiệm đổi bước 2 + bước 10 sang `trustedClick` đã bị HOÀN NGUYÊN: `chrome.debugger` chính
//      là CDP, mà ràng buộc trên cấm đúng thứ đó. **Chưa có cách nào khác cho ra `isTrusted: true` từ trong
//      trang.** Ai tìm được đường khác thì ghi tiếp vào đây.
//
//  (3) `ok` NGHĨA LÀ "ĐỌC LẠI KHỚP KỲ VỌNG", KHÔNG PHẢI "click không ném". Bước nào khai `kyVong` thì khung
//      `buoc()` (dùng chung, ở `flow-chung.js`) TỰ so với `docLai` và tự hạ `ok`.
//
//  (4) ⚠ FORM NÀY LÀ REACT (`eds-react-*`), KHÔNG phải Vue (`eds-*`) như luồng KM — xem `B-BẪY-1` của plan
//      §5d. React gắn `_valueTracker` lên TỪNG input; gán thẳng `el.value` cập nhật luôn giá trị cache của
//      tracker nên lượt `input` bắn ra bị coi là "không có gì đổi" ⇒ state React giữ nguyên RỖNG trong khi
//      màn hình hiện đúng số. Hậu quả đúng hình dạng tệ nhất: ô hiện `2000`, submit đi giá trị rỗng.
//      ⇒ MỌI ô đi qua setter gốc của `HTMLInputElement.prototype` (xem {@link pageGoOReact}) và PHẢI ĐỌC LẠI.
//      Và ⚠ TUYỆT ĐỐI KHÔNG chép selector `eds-*` của `flow-km.js` sang đây: hai bộ component khác nhau.
// ══════════════════════════════════════════════════════════════════════════════════════════════════════════
//
// Mọi selector trong file này CHÉP TỪ DOM THẬT người dùng dán ngày 25/08/2026 (plan §5b/§5), phần dấu-rỗng của
// bảng chép từ hai mẫu DOM dán ngày 26/08/2026. Chỗ nào plan KHÔNG có số đo thì file này KHÔNG đoán — nó trả
// bước ĐỎ kèm nguyên văn thứ đọc được, để lượt sau chốt bằng một số đo thật (xem `chuanHoaMocThoiGian`).
// Nhánh "bảng 0 dòng" ở bước 9 là ví dụ đúng của cách làm đó: nó ĐỎ suốt cho tới khi có số đo, và ngày
// 26/08/2026 số đo về thì nó mới mọc thêm nhánh kết luận "không còn sản phẩm để thêm".
//
// Kết quả gửi về C# qua { action:"pageData", kind:"kmKetQua"|"kmDump", data:<json> } — CÙNG khuôn gói với
// luồng KM, vì `OrdersBridgeSession.ChayVongTaoChuongTrinhAsync` dùng chung một vòng lặp cho cả hai lệnh.

import { ctx, send } from "./core.js";
import { execInTab } from "./exec.js";
import { sleep } from "./shared/util.js";
// DÙNG LẠI của `flow-chung.js` — hạ tầng chung của cả ba luồng, tách khỏi `flow-km.js` ngày 29/08/2026. Chỉ
// những thứ KHÔNG mang selector của Vue: khung chấm bước, hai page-func thuần và các hàm PURE. Thân các hàm đó
// KHÔNG được sửa: luồng KM đang chạy thật, và đây nay là bản DUY NHẤT — sửa một chữ là sửa cho cả ba luồng.
import { buoc, pageClickTheoSelector, pageDemPhanTuHien, gomCanhBao, soTrangLonNhat,
         docMocNgay, mocNgayLuotDo, cauLoiThamSo, NGAY_DAT_TRUOC_LUOT_DO } from "./flow-chung.js";

/// Trần số trang duyệt trong modal sản phẩm. Pager có ô "…" nên KHÔNG đọc trước được tổng số trang (y hệt KM)
/// ⇒ đây là chốt chặn để một pager hỏng không biến vòng lặp thành vô tận. Chạm trần KHÔNG được lặng lẽ
/// `break` — nó nghĩa là shop còn sản phẩm CHƯA được chọn, phải nổi lên thành cảnh báo.
///
/// NỚI 105 → 200 ngày 27/08/2026 (người dùng chốt). Bản trước để 105 dựa trên niềm tin "trần Shopee là 1000
/// SP ÷ 10 SP/trang = 100 trang, dư 5" — **niềm tin đó SAI, đã có số bác bỏ**: đêm 27/08 shop Minoa tạo được
/// `1050` sản phẩm (đúng 105 × 10, tức chạm trần CỦA MÌNH) và Shopee NHẬN chương trình đó. Vậy 1000 không
/// phải trần của Shopee, mà 105 là trần tự đặt đang cắt cụt shop lớn — Minoa chạm trần cả hai đêm liên tiếp
/// (26 và 27/08) trong khi vẫn còn hàng.
///
/// ⚠ Trần THẬT của Shopee vẫn CHƯA BIẾT: mới chứng minh được "≥ 1050", chưa ai chạy tới 2000. Nếu Shopee có
/// trần thật nằm giữa hai số đó thì nó sẽ lộ ra ở bước 9 (bấm Tạo) chứ không phải ở đây — và lộ ra thành một
/// shop HỎNG có nhật ký, không phải mất số âm thầm.
///
/// GIÁ PHẢI TRẢ, đã tính: số đo thật đêm 27/08 là **~4,25s/trang** (Minoa 105 trang hết 506s cả shop, trừ
/// ~60s cho tám bước ngoài modal). 200 trang ⇒ ~850s, cộng phần ngoài modal ⇒ vượt hạn chặng 900s cũ. Vì thế
/// nới trần này BẮT BUỘC đi kèm nới `OrdersBridgeChannel.ChoChang.KmChay` — xem xmldoc ở đó, hai con số này
/// ràng buộc nhau và sửa một cái mà quên cái kia là shop lớn bị ghi HỎNG oan sau khi ĐÃ tạo chương trình thật.
const TRAN_TRANG_MODAL = 200;

/// Trần số lần bấm mũi tên chuyển tháng. Khoảng 180 ngày lệch nhiều nhất 6 tháng (số đo ở `flow-km.js`), trần
/// 8 vẫn dư 2 và nó chặn một nút mũi tên hỏng biến vòng thành vô tận.
const TRAN_CHUYEN_THANG = 8;

/// Số nhịp (×500ms) chờ modal "Chọn Sản Phẩm" mở và bảng có dòng. Hết nhịp mà bảng KHÔNG tự khai rỗng thì kết
/// luận LÀ HỎNG (xem nhánh "bảng 0 dòng" ở bước 9 — từ 26/08/2026 có thêm nhánh "rỗng THẬT").
const CHO_MODAL_NHIP = 90;

/// Nhịp đọc bảng khi chờ nó LẮNG (ms) và trần số lần đọc.
///
/// ⚠ ĐÂY LÀ BẢN VÁ CỦA LỖI TỐN NHẤT ĐÊM 26/08/2026: bản trước tick rồi `sleep(900)` CỐ ĐỊNH. Bảng React của
/// modal render KHÔNG ĐỀU — trang 1 đã sẵn từ lúc mở modal, trang sâu phải nạp lại, nên cảnh báo "tick không đủ"
/// hầu như luôn bắt đầu từ trang 6–12 trở đi. Minoa duyệt 105 trang ≈ 1.050 dòng mà chỉ 640 sản phẩm vào combo.
/// Ngân sách CỐ ĐỊNH sai cả hai chiều: máy nhanh thì phí 105 × 2s = 3,5 phút/shop, máy chậm thì vẫn tick hụt.
/// ⇒ Đọc số dòng vài lượt cách nhau {@link LANG_NHIP_MS}, chỉ tick khi HAI lượt liên tiếp bằng nhau.
/// Trần 27 nhịp ≈ 8s để một bảng không bao giờ lắng không treo cả vòng (105 trang × 8s vẫn nằm trong hạn chặng
/// `ChoChang.KmChay` = 900s ở C# CHỈ KHI đó là ca hiếm — đường xanh chỉ tốn 2 lượt đọc ≈ 0,3s).
const LANG_NHIP_MS = 300;
const LANG_TRAN_NHIP = 27;

/// Số lượt đọc LIÊN TIẾP phải bằng nhau thì mới coi là "lắng" — hai mức, và sự khác nhau là CÓ CHỦ ĐÍCH:
///  · {@link LANG_LAN_BANG_NHAU} = 2 (cửa sổ yên 300ms) cho lượt chờ TRƯỚC KHI TICK. Rẻ, và nó chỉ cần chặn ca
///    bảng đang chảy dòng liên tục — tick sớm ở đây còn có lưới BÙ đỡ phía sau.
///  · {@link LANG_LAN_BANG_NHAU_SAU_TICK} = 4 (cửa sổ yên 900ms) cho lượt ĐỌC LẠI SAU KHI TICK. Đây là con số
///    quyết định "trang này đã đủ hàng chưa", tức là chốt cuối chống MẤT HÀNG ÂM THẦM, nên phải khó tính hơn.
///    900ms đúng bằng `sleep(900)` cố định của bản trước ⇒ KHÔNG đắt hơn bản cũ, mà co giãn được: bảng còn động
///    thì chờ tiếp tới trần, bảng yên thì thôi.
/// Rig (scratchpad, 26/08/2026) đo được: cửa sổ 300ms bỏ lọt ca "bảng vẽ 5 dòng, khựng 700ms, vẽ nốt 5 dòng";
/// cửa sổ 900ms vá được ca đó. Ca khựng LÂU HƠN 900ms ở MỌI trang thì vẫn lọt — xem {@link LANG_TRAN_NHIP_THIEU}
/// cho lưới cuối và phần "chưa vá được" của báo cáo.
const LANG_LAN_BANG_NHAU = 2;
const LANG_LAN_BANG_NHAU_SAU_TICK = 4;

/// Số nhịp tối thiểu phải đọc trước khi CHẤP NHẬN "bảng lắng ở 0 dòng". Bảng 0 dòng đứng yên có thể là đang nạp
/// dở, nên hai lượt bằng nhau ở 0 CHƯA đủ — nhưng cũng không được chờ hết trần, kẻo một trang rỗng thật ngốn 8s.
const LANG_TRAN_NHIP_RONG = 10;

/// Số nhịp tối đa CHỜ THÊM khi bảng đã đứng yên nhưng ĐANG THIẾU DÒNG so với chuẩn học được (số dòng nhiều nhất
/// từng thấy ở một trang của chính modal này).
///
/// Vì sao cần: rig dựng lại được ca "bảng vẽ 5 dòng, KHỰNG hơn một giây, rồi vẽ nốt 5 dòng" — cú khựng ấy dài
/// hơn cửa sổ "hai lượt đọc bằng nhau" nên phép chờ-lắng tưởng bảng đã xong và tick mất một nửa trang, KHÔNG
/// cảnh báo. Trần Shopee là 10 SP/trang và mọi trang trừ trang cuối đều đủ 10 ⇒ "số dòng nhiều nhất từng thấy"
/// là một cận dưới ĐO ĐƯỢC, không phải phỏng đoán.
/// Trần 10 nhịp (3s) chứ không chờ vô hạn: TRANG CUỐI có ít dòng hơn chuẩn một cách hợp lệ, mà trang cuối thì
/// shop nào cũng có — chờ hết trần ở đó là mỗi shop mất thêm 8s cho một chuyện bình thường.
const LANG_TRAN_NHIP_THIEU = 10;

/// Số lượt BÙ TICK tối đa cho MỘT trang (lưới thứ hai sau cú "chọn tất cả"). Mỗi lượt đọc lại trạng thái thật
/// nên 3 lượt là đủ cho một bảng còn đang render; hơn nữa là bảng hỏng, phải nổi thành cảnh báo chứ không lặp mãi.
const TRAN_BU_TICK = 3;

/// Chờ bảng ĐỔI SANG TRANG KHÁC sau cú bấm "trang sau": nhịp (ms) × trần nhịp cho MỖI lần bấm.
///
/// ⚠ 6 giây của bản trước (20 × 300ms) KHÔNG ĐỦ — ca THẬT "Sully Store" tối 26/08/2026 dừng ở trang 6/7 với
/// "bảng không đổi sau khi bấm sang trang 7", tức bỏ OAN trang cuối. Nay 40 × 300ms = 12s cho lần bấm ĐẦU, và
/// nếu vẫn không đổi thì BẤM LẠI một lần rồi chờ thêm 12s nữa trước khi kết luận. Ngân sách này chỉ tiêu tốn ở
/// ca dừng — mà ca dừng `break` khỏi vòng ⇒ nhiều nhất MỘT lần cho mỗi shop.
const CHO_DOI_TRANG_NHIP_MS = 300;
const CHO_DOI_TRANG_TRAN_NHIP = 40;

/// Trần số MỤC cảnh báo gửi về C# cho MỘT shop (cảnh báo đi vào MỘT dòng nhật ký, C# nối bằng " · ").
/// ⚠ CÙNG TÊN CÙNG TRỊ với hằng `TRAN_CANH_BAO` của `flow-chung.js` — hai bản là CÓ CHỦ ĐÍCH tạm thời (chốt
/// 29/08/2026): ở đây truyền tường minh vào `gomCanhBao`, bên kia chỉ là giá trị dự phòng. Hợp nhất để đợt sau.
const TRAN_CANH_BAO = 10;

/// Trần độ dài TÊN chương trình — ô `input[name="name"]` của form combo mang `maxlength="25"`. Gửi dài hơn là
/// Shopee cắt đuôi ÂM THẦM (đúng bài học `maxlength=150` của KM), nên ở đây dài hơn = bước 0 ĐỎ, không tự cắt.
const TRAN_DO_DAI_TEN = 25;

// Tab đang thao tác: sau `openShopDetail` shop nằm ở tab RIÊNG (luật `orderTabIdStrict`).
function tabShop() {
  return ctx.shopTabId;
}

// ── Hàm chạy TRONG TRANG ────────────────────────────────────────────────────────────────────────────────────
// LƯU Ý sống còn (xem exec.js): mỗi page-func được chrome.scripting serialize ĐỘC LẬP, KHÔNG kèm hàm nào ngoài
// nó. Mọi helper (`gon`, `hien`, `timModal`…) phải viết LẶP LẠI bên trong từng hàm — đừng "dọn cho gọn" bằng
// cách rút ra ngoài, rút ra là `ReferenceError` trong trang.
//
// ⚠ `hien()` ở đây dùng `getClientRects()` chứ KHÔNG `offsetParent`: popup ngày/giờ và modal nằm trong lớp phủ
// `position: fixed`, mà `offsetParent` của phần tử fixed là `null` — đúng cái bẫy đã ăn hai lượt ở `flow-km.js`.

/**
 * Bấm nút "Tạo" của thẻ chương trình khớp ĐÚNG CHỮ TIÊU ĐỀ.
 *
 * ⚠ TUYỆT ĐỐI KHÔNG lấy theo chỉ số cột (plan §5b). Ba thẻ cùng markup nằm cạnh nhau: "Chương Trình Của Shop" ·
 * "Combo Khuyến Mãi" · "Mua Kèm Deal Sốc". Shopee đổi thứ tự là tạo nhầm loại chương trình THẬT trên shop thật.
 */
function pageBamTheChuongTrinh(tieuDeCan) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const dsThe = Array.from(document.querySelectorAll(
    ".src-components---createDiscountList--1PwpY .src-components---card--XK9_s"));
  const tieuDeDaThay = dsThe.map(
    (t) => gon(((t.querySelector(".src-components---cardTitle--3gs6L") || {}).textContent) || ""));
  if (!dsThe.length) {
    return { ok: false, loi: "không thấy thẻ chương trình nào (.src-components---card--XK9_s)", tieuDeDaThay };
  }
  const can = gon(tieuDeCan).toLowerCase();
  const chiSo = tieuDeDaThay.findIndex((t) => t.toLowerCase() === can);
  if (chiSo < 0) {
    return { ok: false, loi: "không thấy thẻ mang tiêu đề '" + tieuDeCan + "'", tieuDeDaThay };
  }
  const nut = dsThe[chiSo].querySelector(".src-components---actionRow--3_dWQ button");
  if (!nut) {
    return { ok: false, loi: "thẻ '" + tieuDeCan + "' không có nút trong .src-components---actionRow--3_dWQ",
             tieuDeDaThay };
  }
  if (!hien(nut)) {
    return { ok: false, loi: "nút Tạo của thẻ '" + tieuDeCan + "' đang ẩn", tieuDeDaThay };
  }
  nut.click();
  return { ok: true, tieuDe: tieuDeDaThay[chiSo], chiSoThe: chiSo, tieuDeDaThay };
}

/**
 * Gõ vào một ô của form combo — SETTER GỐC + gõ từng ký tự (xem ràng buộc (4) đầu file).
 *
 * Hai việc trong một hàm, cố ý:
 *  · **Vượt `_valueTracker` của React**: gán qua `Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,
 *    "value").set` — descriptor của PROTOTYPE là descriptor GỐC, còn tracker của React dựng bằng
 *    `defineProperty` trên chính phần tử. Đi vòng qua prototype nên cache của tracker giữ giá trị CŨ, lượt
 *    `input` bắn ra được React tính là "có đổi".
 *  · **Gõ như người** (người dùng chốt 19/08: "cho like human chút, đừng ốp phát tiêu đề luôn"): từng ký tự,
 *    mỗi ký tự `keydown/input/keyup`, nhịp ngẫu nhiên, thỉnh thoảng khựng lâu hơn.
 *
 * Trả `docLai` = `el.value` ĐỌC NGAY SAU KHI GÕ — thước duy nhất phân biệt "React đã nhận" với "chỉ vẽ lên DOM".
 * Vòng gọi mới là chỗ so `docLai` với kỳ vọng; ô tiền tệ có thể tự chèn dấu chấm nên phép so đó chỉ so CHỮ SỐ.
 */
async function pageGoOReact(sel, giaTri, nhipMs) {
  const el = document.querySelector(sel);
  if (!el) { return { ok: false, loi: "không thấy ô " + sel }; }
  const ngu = (ms) => new Promise((r) => setTimeout(r, ms));
  const dat = (v) => {
    try {
      const desc = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value");
      if (desc && desc.set) { desc.set.call(el, v); return; }
    } catch (e) { /* rơi xuống gán thẳng */ }
    el.value = v;
  };
  const nhip = nhipMs || 70;

  el.focus();
  el.dispatchEvent(new Event("focus", { bubbles: true }));
  dat("");
  el.dispatchEvent(new Event("input", { bubbles: true }));

  const chuoi = String(giaTri);
  let keTiepNghi = 8 + Math.floor(Math.random() * 7);
  for (let i = 0; i < chuoi.length; i++) {
    const ky = chuoi[i];
    el.dispatchEvent(new KeyboardEvent("keydown", { key: ky, bubbles: true }));
    dat(chuoi.slice(0, i + 1));
    el.dispatchEvent(new Event("input", { bubbles: true }));
    el.dispatchEvent(new KeyboardEvent("keyup", { key: ky, bubbles: true }));
    let cho = nhip / 2 + Math.random() * nhip;
    if (i >= keTiepNghi) { cho += 180 + Math.random() * 320; keTiepNghi = i + 8 + Math.floor(Math.random() * 7); }
    await ngu(cho);
  }
  el.dispatchEvent(new Event("change", { bubbles: true }));
  el.blur();
  el.dispatchEvent(new Event("blur", { bubbles: true }));

  return { ok: true, docLai: el.value, maxlength: el.getAttribute("maxlength"), soKyTu: chuoi.length };
}

/** Giá trị hiện tại của một input (null = không thấy ô). Dùng cho phép đọc lại SAU khi trang đã lắng. */
function pageDocGiaTriO(sel) {
  const el = document.querySelector(sel);
  return el ? String(el.value === undefined || el.value === null ? "" : el.value) : null;
}

/**
 * Mở popup ngày+giờ của MỘT ô thời gian: bấm vào `.text-box input` bên trong `div[name="period.startTime"]`
 * (hoặc `period.endTime`). Bấm vào khối bọc không bung popup — phải bấm đúng ô nhập.
 */
function pageMoOThoiGian(tenO) {
  const khung = document.querySelector('div[name="' + tenO + '"]');
  if (!khung) { return { ok: false, loi: 'không thấy khung thời gian div[name="' + tenO + '"]' }; }
  const inp = khung.querySelector(".text-box input") || khung.querySelector("input");
  if (!inp) { return { ok: false, loi: "không thấy ô nhập (.text-box input) trong " + tenO }; }
  inp.focus();
  inp.click();
  return { ok: true, truoc: String(inp.value || "") };
}

/** Text đang hiển thị trong ô thời gian `tenO` (null = không thấy ô). */
function pageDocOThoiGian(tenO) {
  const khung = document.querySelector('div[name="' + tenO + '"]');
  if (!khung) { return null; }
  const inp = khung.querySelector(".text-box input") || khung.querySelector("input");
  return inp ? String(inp.value || "") : null;
}

/**
 * Đọc THÁNG/NĂM của popup ngày ĐANG MỞ.
 *
 * ⚠ Tháng và năm là HAI THẺ RỜI (`.month` = `"Th08"`, `.year` = `"2026"`) nên KHÔNG dính ca `"Tháng 82026"`
 * đã giết một đêm ở `flow-km.js` — nhưng vẫn phải bóc SỐ từ `"Th08"`, đừng `Number("Th08")`.
 * ĐỎ KHI: không có popup hiện · CÓ HAI popup cùng hiện (hai ô thời gian, không được đoán ô nào) · không thấy
 * thẻ tháng/năm · số nằm ngoài miền hợp lệ.
 */
function pageDocThangNamPopup() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ds = Array.from(document.querySelectorAll(".eds-react-popover.eds-react-date-picker__popup"));
  const dangHien = ds.filter(hien);
  if (dangHien.length === 0) { return { loi: "không có popup ngày/giờ nào đang hiện (" + ds.length + " popup trong DOM)" }; }
  if (dangHien.length > 1) { return { loi: "CÓ " + dangHien.length + " popup ngày/giờ cùng hiện — từ chối thao tác" }; }
  const p = dangHien[0];
  const elThang = p.querySelector(".date-box .date-default-style.month") || p.querySelector(".date-box .month");
  const elNam = p.querySelector(".date-box .date-default-style.year") || p.querySelector(".date-box .year");
  if (!elThang || !elNam) {
    return { loi: "không thấy thẻ tháng/năm (.date-box .month/.year) trong popup",
             chuTrongDateBox: gon(((p.querySelector(".date-box") || {}).textContent) || "").slice(0, 60) };
  }
  const chuThang = gon(elThang.textContent);
  const chuNam = gon(elNam.textContent);
  const thang = Number((chuThang.match(/\d+/) || [""])[0]);
  const nam = Number((chuNam.match(/\d+/) || [""])[0]);
  if (!(thang >= 1 && thang <= 12) || !(nam >= 2000 && nam <= 2999)) {
    return { loi: "tháng/năm đọc được không hợp lệ: month='" + chuThang + "' year='" + chuNam + "'" };
  }
  return { thang, nam, tieuDe: chuThang + " " + chuNam };
}

/**
 * Bấm mũi tên chuyển THÁNG của popup đang mở (`tien` = true: tháng sau).
 *
 * ⚠ Header có BỐN nút mũi tên; hai nút mang thêm class `double` là CHUYỂN NĂM — bấm nhầm là nhảy 12 tháng một
 * phát (plan §5b). Nên lọc `:not(.double)` rồi mới phân trái/phải theo icon con.
 */
function pageBamChuyenThang(tien) {
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ds = Array.from(document.querySelectorAll(".eds-react-popover.eds-react-date-picker__popup")).filter(hien);
  if (ds.length !== 1) { return { ok: false, loi: "cần ĐÚNG 1 popup đang hiện, đang có " + ds.length }; }
  const p = ds[0];
  const header = p.querySelector(".eds-react-date-picker__header") || p;
  const nuts = Array.from(header.querySelectorAll(".btn-arrow-default"))
    .filter((n) => !n.classList.contains("double"));   // bỏ hai nút CHUYỂN NĂM
  const icon = tien ? ".icon-arrow-right" : ".icon-arrow-left";
  const nut = nuts.find((n) => n.querySelector(icon) || n.matches(icon));
  if (!nut) {
    return { ok: false, loi: "không thấy nút chuyển tháng (" + (tien ? "tiến" : "lùi") + ") trong header popup",
             soNutKhongDouble: nuts.length };
  }
  nut.click();
  return { ok: true, cls: String(nut.className || "") };
}

/**
 * Click ô ngày theo ĐÚNG SỐ NGÀY, chỉ trong `.eds-react-date-picker__table-row`.
 *
 * ⚠ HAI cái bẫy, cả hai đã trả giá ở luồng KM hoặc được plan §5b ghi rõ:
 *  · Ô TIÊU ĐỀ THỨ ("CN", "T2"…) mang ĐÚNG cùng class `__table-cell` nhưng nằm trong `__table-header` ⇒ BẮT
 *    BUỘC scope trong `__table-row`.
 *  · `out-of-range` = ngày của tháng liền kề và nó KHÔNG có `disabled` ⇒ bấm được, mà bấm là đặt SAI THÁNG.
 * `disabled` là chỗ trần 180 ngày của Shopee lộ ra: hết trần thì không còn ô hợp lệ ⇒ bước ĐỎ với câu "không
 * thấy ô ngày", đúng thứ cần thấy.
 */
function pageClickNgayTheoSo(soNgay) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ds = Array.from(document.querySelectorAll(".eds-react-popover.eds-react-date-picker__popup")).filter(hien);
  if (ds.length !== 1) { return { ok: false, loi: "cần ĐÚNG 1 popup đang hiện, đang có " + ds.length }; }
  const p = ds[0];
  const tatCa = Array.from(p.querySelectorAll(
    ".eds-react-date-picker__table-row .eds-react-date-picker__table-cell"));
  const hopLe = tatCa.filter((c) => !c.classList.contains("disabled") && !c.classList.contains("out-of-range"));
  const hit = hopLe.find((c) => gon(c.textContent) === String(soNgay));
  if (!hit) {
    return {
      ok: false,
      loi: "không thấy ô ngày '" + soNgay + "' còn chọn được (tổng " + tatCa.length + " ô, "
        + tatCa.filter((c) => c.classList.contains("disabled")).length + " disabled, "
        + tatCa.filter((c) => c.classList.contains("out-of-range")).length + " out-of-range)",
      coSan: hopLe.slice(0, 12).map((c) => gon(c.textContent)),
    };
  }
  hit.click();
  return { ok: true, text: gon(hit.textContent), cls: String(hit.className || "") };
}

/**
 * Click ô giờ (cột 0) hoặc phút (cột 1) theo NHÃN 2 CHỮ SỐ, trong popup đang mở.
 * `daChonSan` đọc TRƯỚC cú click: ô đã chọn sẵn thì click "đổi" mà không đổi gì — đúng ca đã làm hai thí nghiệm
 * picker của KM báo `ok:true` trong khi ô thời gian đứng nguyên.
 */
function pageClickOThoiGian(chiSoCot, nhan) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ds = Array.from(document.querySelectorAll(".eds-react-popover.eds-react-date-picker__popup")).filter(hien);
  if (ds.length !== 1) { return { ok: false, loi: "cần ĐÚNG 1 popup đang hiện, đang có " + ds.length }; }
  const p = ds[0];
  const cots = Array.from(p.querySelectorAll("ul.time-list"));
  const cot = cots[chiSoCot];
  if (!cot) { return { ok: false, loi: "không thấy cột " + chiSoCot + " (popup có " + cots.length + " cột ul.time-list)" }; }
  const os = Array.from(cot.querySelectorAll("li > .time-box"));
  const hit = os.find((x) => gon(x.textContent) === String(nhan));
  if (!hit) {
    return { ok: false, loi: "không thấy ô '" + nhan + "' ở cột " + chiSoCot,
             coSan: os.slice(0, 8).map((x) => gon(x.textContent)) };
  }
  if (hit.classList.contains("disabled")) { return { ok: false, loi: "ô '" + nhan + "' đang disabled" }; }
  const daChonSan = hit.classList.contains("selected");
  hit.scrollIntoView({ block: "center" });
  hit.click();
  return { ok: true, daChonSan, dangChon: gon(((cot.querySelector(".time-box.selected") || {}).textContent) || "") };
}

/**
 * Bấm "Xác nhận" TRONG popup ngày/giờ đang mở (`.eds-react-date-picker__btn-wrap button`).
 * Khớp theo CHỮ: khối nút có thể còn nút "Huỷ"/"Xoá" — bấm phần tử đầu tiên là đóng popup mà không chốt gì.
 */
function pageXacNhanPopup() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ds = Array.from(document.querySelectorAll(".eds-react-popover.eds-react-date-picker__popup")).filter(hien);
  if (ds.length !== 1) { return { ok: false, loi: "cần ĐÚNG 1 popup đang hiện, đang có " + ds.length }; }
  const nuts = Array.from(ds[0].querySelectorAll(".eds-react-date-picker__btn-wrap button"));
  const nut = nuts.find((b) => gon(b.textContent).toLowerCase().indexOf("xác nhận") >= 0);
  if (!nut) {
    return { ok: false, loi: "không thấy nút Xác nhận trong .eds-react-date-picker__btn-wrap",
             coSan: nuts.map((b) => gon(b.textContent)).slice(0, 6) };
  }
  nut.click();
  return { ok: true };
}

/**
 * Tick ô radio "Loại combo" theo `value` (3 = giảm SỐ TIỀN, 2 = giảm %, 1 = giá đặc biệt).
 * `soONhanDuoc` gửi kèm để phát hiện ca selector không còn duy nhất trên trang — bước gọi biến nó thành cảnh báo.
 */
function pageTickLoaiCombo(loai) {
  const os = Array.from(document.querySelectorAll('input[type=radio][value="' + loai + '"]'));
  if (!os.length) { return { ok: false, loi: 'không thấy input[type=radio][value="' + loai + '"]' }; }
  const r = os[0];
  const daChonSan = !!r.checked;
  if (!r.checked) { r.click(); }
  return { ok: true, daChonSan, dangChon: !!r.checked, soONhanDuoc: os.length };
}

/**
 * ĐO hàng "Mức ưu đãi" — đây là CỬA CHẶN của plan §5, đọc kỹ trước khi gõ số tiền.
 *
 * Hai biến thể khác nhau HẲN: loại 2 dùng `tiers.0.discountPercentage` (hậu tố `%GIẢM`), loại 3 dùng
 * `tiers.0.discountValue` (tiền tố `₫`, `maxlength=13`, class bọc `currency-input`). Gõ `2000` vào ô
 * `discountPercentage` là khai báo **giảm 2000%** — và Shopee vẫn cho bấm "Xác nhận".
 *
 * `name` là cửa chặn; tiền tố/hậu tố + `currency-input` + `maxlength` là ba phép đọc lại độc lập đi kèm.
 */
function pageDoOMucUuDai() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const mota = (el) => {
    if (!el) { return null; }
    const boc = el.closest(".eds-react-input") || el.parentElement || el;
    return {
      maxlength: el.getAttribute("maxlength"),
      tienTo: gon(((boc.querySelector(".eds-react-input__prefix") || {}).textContent) || ""),
      hauTo: gon(((boc.querySelector(".eds-react-input__suffix") || {}).textContent) || ""),
      trongCurrencyInput: !!el.closest(".currency-input"),
      giaTri: String(el.value === undefined || el.value === null ? "" : el.value),
    };
  };
  const oTien = document.querySelector('input[name="tiers.0.discountValue"]');
  const oPhanTram = document.querySelector('input[name="tiers.0.discountPercentage"]');
  const oSl = document.querySelector('input[name="tiers.0.minAmount"]');
  return {
    coDiscountValue: !!oTien,
    coDiscountPercentage: !!oPhanTram,
    coMinAmount: !!oSl,
    tien: mota(oTien),
    phanTram: mota(oPhanTram),
    soLuong: mota(oSl),
  };
}

/** Mở modal "Chọn Sản Phẩm" — nút `.src-components---addItems--2wv99` của form combo. */
function pageMoModalSanPham() {
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const nut = document.querySelector(".src-components---addItems--2wv99");
  if (!nut) { return { ok: false, loi: "không thấy nút thêm sản phẩm (.src-components---addItems--2wv99)" }; }
  if (!hien(nut)) { return { ok: false, loi: "nút thêm sản phẩm đang ẩn" }; }
  nut.click();
  return { ok: true };
}

/**
 * ĐO modal "Chọn Sản Phẩm" đang mở. Neo modal theo CHỮ TIÊU ĐỀ (`.eds-react-modal__title` = "Chọn Sản Phẩm"),
 * không theo "hộp modal đang hiện" — trang có thể còn hộp modal khác (đúng ca 22:48 và 00:44 của luồng KM đã
 * chộp nhầm hộp hướng dẫn rồi kết luận "không thấy nút Xác nhận").
 *
 * ⚠ `tr.eds-react-table-measure-row` cao 0px KHÔNG có `data-row-key` ⇒ đếm bằng `[data-row-key]`, đừng đếm `tr`.
 * `hien()` là phép lọc ƯU TIÊN, không có quyền phủ quyết (modal nằm trong lớp phủ fixed).
 *
 * <b>HAI DẤU RỖNG (26/08/2026)</b> — `soBangRong` và `soPlaceholderHien`. Chúng là NGUYÊN LIỆU của luật "rỗng
 * THẬT" ở bước 9, và chỉ có giá trị khi đi CÙNG NHAU với `soDong === 0`:
 *  · `soBangRong` — bảng mang class `eds-react-table-empty` (mẫu DOM người dùng dán: bảng RỖNG có class ấy,
 *    bảng CÓ HÀNG thì `eds-react-table … ping-right fixed-column scroll-horizontal has-fix-left`, KHÔNG có).
 *  · `soPlaceholderHien` — `tr.eds-react-table-placeholder` ĐANG HIỆN.
 *    ⚠ ĐẾM RIÊNG `soPlaceholder` (tồn tại) với `soPlaceholderHien` (đang hiện) là CỐ Ý, và người dùng đã đính
 *    chính đúng chỗ này một lần: dòng placeholder **TỒN TẠI CẢ KHI CÓ SẢN PHẨM**, chỉ bị ẩn. Lấy sự TỒN TẠI
 *    làm dấu rỗng là kết luận MỌI shop đều rỗng ⇒ bỏ oan cả 12 shop trong im lặng.
 */
function pageDoModalSanPham() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(
    ".eds-react-modal__content--x-large, .eds-react-modal__container"));
  const khop = (m) => {
    const t = m.querySelector(".eds-react-modal__title");
    return !!t && gon(t.textContent).toLowerCase() === "chọn sản phẩm";
  };
  const modal = ungVien.find((m) => khop(m) && hien(m)) || ungVien.find(khop) || null;
  if (!modal) {
    return {
      coModal: false,
      soHopModalTrongDom: ungVien.length,
      tieuDeDaThay: Array.from(document.querySelectorAll(".eds-react-modal__title"))
        .map((t) => gon(t.textContent)).slice(0, 6),
    };
  }
  const dong = Array.from(modal.querySelectorAll("tbody.eds-react-table-tbody tr[data-row-key]"));
  const trang = Array.from(modal.querySelectorAll("li.eds-react-pagination-pager__page"))
    .filter((li) => !li.classList.contains("eds-react-pagination-pager__dot"));
  const nutSau = modal.querySelector("button.eds-react-pagination-pager__button-next");
  const bang = Array.from(modal.querySelectorAll(".eds-react-table"));
  const placeholder = Array.from(modal.querySelectorAll("tr.eds-react-table-placeholder"));
  return {
    coModal: true,
    soDong: dong.length,
    soTrTat: modal.querySelectorAll("tbody.eds-react-table-tbody tr").length,
    soBang: bang.length,
    // `classList.contains` chứ KHÔNG so chuỗi className: `eds-react-table-empty` là KHÚC ĐẦU của nhiều class
    // khác cùng tiền tố, so chuỗi là chộp nhầm.
    soBangRong: bang.filter((b) => b.classList.contains("eds-react-table-empty")).length,
    soPlaceholder: placeholder.length,
    soPlaceholderHien: placeholder.filter(hien).length,
    nhanCacTrang: trang.map((li) => gon(li.textContent)),
    trangHienTai: gon(((modal.querySelector("li.eds-react-pagination-pager__page.active") || {}).textContent) || ""),
    coNutTrangSau: !!nutSau,
    // CHỈ để người đọc dump nhìn ra ngay "bảng có tự khai rỗng không" — KHÔNG dùng để kết luận shop rỗng: plan
    // §5b không có selector khối rỗng của `eds-react`, mà đoán selector ở đây là bỏ OAN cả một shop, im lặng.
    chuNghiRong: Array.from(modal.querySelectorAll("div,span,p"))
      .filter((e) => hien(e) && (e.textContent || "").length < 80
        && /không có dữ liệu|khong co du lieu|no data|chưa có sản phẩm/i.test(e.textContent || ""))
      .map((e) => gon(e.textContent)).slice(0, 3),
  };
}

/**
 * Tick ô "chọn tất cả" ở HEADER BẢNG của modal.
 *
 * ⚠ Modal còn một checkbox **"Xem sản phẩm có sẵn"** ở vùng `actions` và nó đang `checked` — đó là BỘ LỌC, tick
 * nhầm là ĐỔI TẬP SẢN PHẨM (plan §5b). Vì vậy selector neo cứng vào
 * `.eds-react-table-header thead th.eds-react-table-selection-column`, không quét checkbox toàn modal.
 */
function pageTickChonTatCa() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(
    ".eds-react-modal__content--x-large, .eds-react-modal__container"));
  const khop = (m) => {
    const t = m.querySelector(".eds-react-modal__title");
    return !!t && gon(t.textContent).toLowerCase() === "chọn sản phẩm";
  };
  const modal = ungVien.find((m) => khop(m) && hien(m)) || ungVien.find(khop) || null;
  if (!modal) { return { ok: false, loi: "không thấy modal Chọn Sản Phẩm — KHÔNG tick ngoài modal" }; }
  const o = modal.querySelector(
    ".eds-react-table-header thead th.eds-react-table-selection-column input.eds-react-checkbox__input");
  if (!o) { return { ok: false, loi: "không thấy ô tick 'chọn tất cả' ở header bảng của modal" }; }
  const daTickSan = !!o.checked;
  if (!o.checked) { o.click(); }
  return { ok: true, daTickSan, dangTick: !!o.checked };
}

/**
 * ĐẾM dòng đã tick của TRANG ĐANG MỞ — phép ĐỌC LẠI của chính cú "chọn tất cả".
 * (Modal combo không có ô đếm "Đã chọn N/1000" như luồng KM, nên số này là thước duy nhất.)
 */
function pageDemDaTick() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(
    ".eds-react-modal__content--x-large, .eds-react-modal__container"));
  const khop = (m) => {
    const t = m.querySelector(".eds-react-modal__title");
    return !!t && gon(t.textContent).toLowerCase() === "chọn sản phẩm";
  };
  const modal = ungVien.find((m) => khop(m) && hien(m)) || ungVien.find(khop) || null;
  if (!modal) { return { coModal: false }; }
  const dong = Array.from(modal.querySelectorAll("tbody.eds-react-table-tbody tr[data-row-key]"));
  const oCua = (r) => r.querySelector("td.eds-react-table-selection-column input.eds-react-checkbox__input");
  const daTick = dong.filter((r) => { const c = oCua(r); return !!c && c.checked; });

  // ĐẾM RIÊNG DÒNG BỊ KHOÁ — đây là vế phân biệt "Shopee không cho tick" với "ta tick hụt".
  //
  // Shopee vô hiệu hoá checkbox của sản phẩm ĐANG NẰM TRONG MỘT CHƯƠNG TRÌNH KHÁC. Những dòng ấy không bao
  // giờ tick được, và đó là chuyện BÌNH THƯỜNG — không mất sản phẩm nào của combo này. Bản trước so
  // `soDaTick < soDong` nên gộp cả hai vào một cảnh báo: đọc log thấy "trang 6, trang 13 tick không đủ" mà
  // không biết là Shopee khoá (kệ nó) hay dòng chưa render kịp (mất hàng thật). Ca thật 16:46 ngày
  // 26/08/2026 trên Alina Store1.
  const biKhoa = dong.filter((r) => { const c = oCua(r); return !c || c.disabled; });

  return {
    coModal: true,
    soDong: dong.length,
    soDaTick: daTick.length,
    soBiKhoa: biKhoa.length,
    soTickDuoc: dong.length - biKhoa.length,
    vanTay: dong.slice(0, 3).map((r) => r.getAttribute("data-row-key")).join("|"),
    // NHÃN TRANG ĐANG ACTIVE — phân biệt "cú bấm sang trang KHÔNG ăn" với "đã ăn mà bảng chưa vẽ xong". Thiếu nó
    // thì lượt bấm lại ở {@link choDoiTrang} có thể bấm chồng lên một cú đã ăn ⇒ NHẢY CÓC MẤT MỘT TRANG.
    trangHienTai: gon(((modal.querySelector("li.eds-react-pagination-pager__page.active") || {}).textContent) || ""),
  };
}

/**
 * BÙ TICK — tick RIÊNG từng dòng của trang đang mở mà checkbox còn BẬT nhưng CHƯA `checked`.
 *
 * ⚠ <b>TUYỆT ĐỐI KHÔNG bù bằng cách bấm lại ô "chọn tất cả"</b> (26/08/2026). Ô ấy là CÔNG TẮC BẬT/TẮT: bấm
 * lần hai khi nó đang bật là BỎ TICK SẠCH CẢ TRANG — bản vá "bù" kiểu đó biến một trang tick hụt 3 dòng thành
 * một trang mất trắng 10 dòng, mà không bước nào đỏ.
 *
 * ⚠ <b>GOM THEO `data-row-key`, bấm nhiều nhất MỘT checkbox cho mỗi khoá.</b> Bảng này chạy chế độ cột đông cứng
 * (`has-fix-left`), mà kiểu bảng đó có thể vẽ MỘT DÒNG THÀNH HAI phần tử (một ở lớp cột cố định, một ở lớp cuộn
 * ngang). Bấm cả hai là toggle hai lần = trở về CHƯA tick. Đọc `checked` ngay trước mỗi cú bấm vẫn chưa đủ vì
 * React có thể chưa kịp đồng bộ phần tử song sinh trong cùng một nhịp.
 */
function pageTickDongConSot() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(
    ".eds-react-modal__content--x-large, .eds-react-modal__container"));
  const khop = (m) => {
    const t = m.querySelector(".eds-react-modal__title");
    return !!t && gon(t.textContent).toLowerCase() === "chọn sản phẩm";
  };
  const modal = ungVien.find((m) => khop(m) && hien(m)) || ungVien.find(khop) || null;
  if (!modal) { return { ok: false, loi: "không thấy modal Chọn Sản Phẩm — KHÔNG tick ngoài modal" }; }
  const dong = Array.from(modal.querySelectorAll("tbody.eds-react-table-tbody tr[data-row-key]"));
  const daBamKhoa = Object.create(null);
  let daBam = 0;
  let boQuaTrung = 0;
  const khoaDaBam = [];
  for (const r of dong) {
    const o = r.querySelector("td.eds-react-table-selection-column input.eds-react-checkbox__input");
    if (!o || o.disabled || o.checked) { continue; }
    const khoa = String(r.getAttribute("data-row-key") || "");
    if (khoa && daBamKhoa[khoa]) { boQuaTrung++; continue; }
    if (khoa) { daBamKhoa[khoa] = true; }
    o.click();
    daBam++;
    if (khoaDaBam.length < 5) { khoaDaBam.push(khoa); }
  }
  return { ok: true, soDong: dong.length, daBam, boQuaTrung, khoaDaBam };
}

/**
 * Sang TRANG SAU trong modal bằng `button.eds-react-pagination-pager__button-next`.
 * Ba lối trả khác hẳn nhau — vòng lật trang ở bước 9 dựa vào đúng chúng để quyết có đẻ cảnh báo không:
 *  · `hetTrang: true` — nút có mà đang `disabled` ⇒ BÌNH THƯỜNG, đã tới trang cuối.
 *  · `khongCoNut: true` — modal còn đó mà KHÔNG có nút trang sau ⇒ shop ít hàng không render pager (bình
 *    thường) HOẶC pager vừa biến mất giữa chừng (hỏng). Vòng gọi phân xử bằng "lúc mở modal có pager không".
 *  · không cờ nào (mất modal) — HỎNG, vòng gọi luôn cảnh báo.
 */
function pageSangTrangSau() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(
    ".eds-react-modal__content--x-large, .eds-react-modal__container"));
  const khop = (m) => {
    const t = m.querySelector(".eds-react-modal__title");
    return !!t && gon(t.textContent).toLowerCase() === "chọn sản phẩm";
  };
  const modal = ungVien.find((m) => khop(m) && hien(m)) || ungVien.find(khop) || null;
  if (!modal) { return { ok: false, loi: "không thấy modal Chọn Sản Phẩm — KHÔNG bấm pager ngoài modal" }; }
  const nhan = Array.from(modal.querySelectorAll("li.eds-react-pagination-pager__page"))
    .filter((li) => !li.classList.contains("eds-react-pagination-pager__dot"))
    .map((li) => gon(li.textContent));
  const nut = modal.querySelector("button.eds-react-pagination-pager__button-next");
  if (!nut) { return { ok: false, khongCoNut: true, loi: "modal không có nút trang sau", coSan: nhan }; }
  if (nut.disabled || nut.classList.contains("disabled")) {
    return { ok: false, hetTrang: true, loi: "nút trang sau đang disabled (đã tới trang cuối)", coSan: nhan };
  }
  if (!hien(nut)) { return { ok: false, loi: "nút trang sau đang ẩn", coSan: nhan }; }
  nut.click();
  return { ok: true, coSan: nhan };
}

/**
 * Bấm "Xác nhận" của MODAL — chỉ NẠP sản phẩm vào form, CHƯA tạo combo.
 *
 * ⚠ SCOPE CỨNG trong `.eds-react-modal__footer` của đúng modal "Chọn Sản Phẩm". Trên trang này còn nút
 * `footer button[type=submit].src-components---confirm--3svOo` — đó là nút TẠO THẬT. Một lượt tìm toàn trang mà
 * trượt modal là bấm trúng nó, tức tạo combo thật trên shop thật với form còn dở. Không thấy modal ⇒ TRẢ LỖI,
 * tuyệt đối không nới phạm vi ra ngoài.
 */
function pageXacNhanModal() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  const ungVien = Array.from(document.querySelectorAll(
    ".eds-react-modal__content--x-large, .eds-react-modal__container"));
  const khop = (m) => {
    const t = m.querySelector(".eds-react-modal__title");
    return !!t && gon(t.textContent).toLowerCase() === "chọn sản phẩm";
  };
  const modal = ungVien.find((m) => khop(m) && hien(m)) || ungVien.find(khop) || null;
  if (!modal) { return { ok: false, loi: "không thấy modal Chọn Sản Phẩm — KHÔNG tìm nút Xác nhận ngoài modal" }; }
  const footer = modal.querySelector(".eds-react-modal__footer");
  if (!footer) { return { ok: false, loi: "modal Chọn Sản Phẩm không có .eds-react-modal__footer" }; }
  const nut = footer.querySelector("button.eds-react-button--primary");
  if (!nut) {
    return { ok: false, loi: "không thấy nút primary trong footer của modal",
             coSan: Array.from(footer.querySelectorAll("button")).map((b) => gon(b.textContent)).slice(0, 6) };
  }
  // LƯỚI CUỐI: nút tìm được mà lại là nút TẠO THẬT thì từ chối bấm (y kỷ luật `.fix-container` của luồng KM).
  if (nut.matches(".src-components---confirm--3svOo") || nut.closest("footer.src-components---footer--1Kx7d")) {
    return { ok: false, loi: "nút tìm được là nút TẠO THẬT của form, không phải nút của modal — từ chối bấm" };
  }
  nut.click();
  return { ok: true, chu: gon(nut.textContent) };
}

/** Modal "Chọn Sản Phẩm" còn đang mở không — phép đọc lại của cú Xác nhận modal. */
function pageModalConMo() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.getClientRects().length > 0;
  return Array.from(document.querySelectorAll(".eds-react-modal__content--x-large, .eds-react-modal__container"))
    .some((m) => {
      const t = m.querySelector(".eds-react-modal__title");
      return !!t && gon(t.textContent).toLowerCase() === "chọn sản phẩm" && hien(m);
    });
}

/**
 * Bấm nút TẠO THẬT ở footer form: `footer button[type=submit].src-components---confirm--3svOo`.
 * Cuộn tới nút trước khi bấm (form dài, footer có thể nằm ngoài khung nhìn).
 */
function pageBamTaoThat() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const nut = document.querySelector('footer button[type=submit].src-components---confirm--3svOo');
  if (!nut) {
    return { ok: false, loi: "không thấy nút Xác nhận ở footer (button[type=submit].src-components---confirm--3svOo)",
             nutTheoChu: Array.from(document.querySelectorAll("button"))
               .filter((b) => /xác nhận/i.test(b.textContent || ""))
               .map((b) => String(b.className || "").slice(0, 60)).slice(0, 6) };
  }
  if (nut.disabled || nut.classList.contains("disabled")) {
    return { ok: false, loi: "nút Xác nhận đang disabled — form còn thiếu/sai trường" };
  }
  nut.scrollIntoView({ block: "center" });
  nut.click();
  return { ok: true, chu: gon(nut.textContent) };
}

/** Mọi thông báo ĐANG HIỆN trên trang (thành công lẫn lỗi) — để bước 10 nói được "Shopee đã nói gì". */
function pageDocBaoTrenTrang() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim().slice(0, 140);
  const hien = (e) => !!e && e.getClientRects().length > 0;
  return Array.from(document.querySelectorAll(
    ".eds-react-message, .eds-react-toast, .eds-react-notification, .eds-react-form-item__error, "
    + ".eds-message, .eds-toast, .error-message, [class*='success']"))
    .filter(hien).map((e) => gon(e.textContent)).filter((t) => t).slice(0, 6);
}

// ── Hàm THUẦN (không đụng trang, không đụng tab) ─────────────────────────────────────────────────────────────

/**
 * PURE — tên ô "mức giảm" ứng với loại combo, theo plan §5.
 * Loại 1 ("giá đặc biệt") CHƯA có DOM ⇒ trả `null`: {@link docThamSoCombo} biến nó thành lỗi tham số ngay ở
 * bước 0 thay vì để luồng mò một cái tên không ai đo bao giờ.
 */
function oGiamCuaLoai(loai) {
  if (loai === 3) { return "tiers.0.discountValue"; }
  if (loai === 2) { return "tiers.0.discountPercentage"; }
  return null;
}

/** PURE — chỉ giữ CHỮ SỐ. Ô tiền tệ của React tự chèn dấu chấm (`2000` → `2.000`) nên phép đọc lại phải so
 * phần số, không so nguyên văn. */
function chiSo(s) {
  return String(s === null || s === undefined ? "" : s).replace(/\D/g, "");
}

/**
 * PURE — chuẩn hoá text của ô thời gian về `yyyy-MM-dd HH:mm` để so BẰNG.
 *
 * ⚠ <b>ĐÂY LÀ CHỖ PLAN CÒN THIẾU SỐ ĐO</b>: §5b cho selector của ô `period.startTime` nhưng KHÔNG cho ĐỊNH DẠNG
 * ô đó hiển thị sau khi chốt. Vì vậy hàm này KHÔNG đoán một định dạng rồi so chuỗi thô — nó nhận cả hai bố cục
 * còn khả dĩ (`dd-MM-yyyy` và `yyyy-MM-dd`, giờ đứng trước hoặc sau) rồi quy về một dạng; không nhận ra được
 * thì trả `null` ⇒ bước ĐỎ và in NGUYÊN VĂN chuỗi đọc được ra nhật ký, để lượt chạy thật đầu tiên chốt lại
 * bằng một số đo thật thay vì bằng phỏng đoán.
 *
 * Ca không phân biệt được: `dd-MM-yyyy` với `MM-dd-yyyy` khi ngày ≤ 12. Chọn `dd-MM-yyyy` theo tiền lệ ĐO ĐƯỢC
 * của cùng tenant (ô thời gian của luồng KM đọc ra `HH:mm dd-MM-yyyy`).
 */
function chuanHoaMocThoiGian(text) {
  const s = String(text === null || text === undefined ? "" : text).trim();
  const hai = (n) => String(n).padStart(2, "0");
  const gio = s.match(/(\d{1,2}):(\d{2})/);
  if (!gio) { return null; }
  const ngay = s.match(/(\d{1,4})[-/.](\d{1,2})[-/.](\d{1,4})/);
  if (!ngay) { return null; }
  let d;
  let m;
  let y;
  if (ngay[1].length === 4) { y = Number(ngay[1]); m = Number(ngay[2]); d = Number(ngay[3]); }
  else if (ngay[3].length === 4) { d = Number(ngay[1]); m = Number(ngay[2]); y = Number(ngay[3]); }
  else { return null; }
  const h = Number(gio[1]);
  if (!(m >= 1 && m <= 12) || !(d >= 1 && d <= 31) || !(y >= 2000 && y <= 2999) || !(h >= 0 && h <= 23)) {
    return null;
  }
  return y + "-" + hai(m) + "-" + hai(d) + " " + hai(h) + ":" + gio[2];
}

/**
 * PURE — đọc + KIỂM tham số một lượt COMBO. Trả giá trị đã chuẩn hoá kèm câu lỗi cho TỪNG NHÓM trường.
 *
 * <b>KHÔNG MỘT `||`-DEFAULT NÀO trên đường chạy thật</b> — đúng bài học `phanTram || 5` của `flow-km.js`: mặc
 * định ngầm làm `kyVong` được dựng từ chính biến vừa bị thay, nên phép đọc lại TỰ NHẤT QUÁN và luôn xanh trong
 * khi chương trình mọc lên sai. Thiếu/sai trường nào thì lượt HỎNG ngay ở bước giả "0. Tham số", gọi ĐÍCH DANH
 * tên trường — trước khi đụng vào trang.
 *
 * <b>KHUÔN GÓI TỪ 26/08/2026 — HAI MỐC NGÀY, MỘT GIỜ.</b> Combo cũng chạy một KHOẢNG 180 ngày (bắt đầu HÔM
 * NAY, kết thúc hôm nay + 180) với CÙNG một giờ ở hai đầu.
 * Tên ô của C#: xem `ThamSoCombo.Goi()` (`ten, bd{…}, kt{…}, gio, loai, muaSl, giam, gioiHan`).
 */
function docThamSoCombo(ts) {
  const nguon = (ts === null || ts === undefined) ? thamSoLuotDo() : ts;
  const loi = {};

  const ten = typeof nguon.ten === "string" ? nguon.ten.trim() : "";
  if (!ten) {
    loi.ten = `thiếu tham số 'ten' (tên chương trình): '${nguon.ten}'`;
  } else if (ten.length > TRAN_DO_DAI_TEN) {
    // KHÔNG tự cắt: ô Shopee mang maxlength=25 và cắt ÂM THẦM. Tên bị cụt thì không bước nào đỏ, chỉ có một
    // chương trình mang tên sai nằm trên shop thật. Tầng form C# đã cắt sẵn (`KmDialogViewModel.CatTenCombo`),
    // tới đây mà còn dài là hợp đồng giữa hai tầng đã vỡ — phải ĐỎ.
    loi.ten = `'ten' dài ${ten.length} ký tự, ô Shopee chỉ nhận ${TRAN_DO_DAI_TEN}: '${ten}'`;
  }

  // HAI MỐC NGÀY, cùng một hàm kiểm — xem `docMocNgay`. Tên lỗi tách riêng để người đọc nhật ký biết ngay
  // ĐẦU NÀO của khoảng hỏng.
  const bd = docMocNgay(nguon.bd);
  if (bd.loi) { loi.ngayBd = "mốc BẮT ĐẦU — " + bd.loi; }
  const kt = docMocNgay(nguon.kt);
  if (kt.loi) { loi.ngayKt = "mốc KẾT THÚC — " + kt.loi; }

  // MỘT GIỜ cho CẢ HAI đầu khoảng (26/08/2026).
  const docGio = (s) => (typeof s === "string" && /^([01]\d|2[0-3]):[0-5]\d$/.test(s)) ? s.split(":") : null;
  const gio = docGio(nguon.gio);
  if (!gio) { loi.gio = `giờ không hợp lệ (cần HH:mm): '${nguon.gio}'`; }

  // LOẠI COMBO — cửa chặn của plan §5. Loại 1 ("giá đặc biệt") chưa dò được DOM ô mức giảm; nhận nó là mò một
  // tên field không ai đo bao giờ, rồi gõ số vào một ô sai.
  const loai = Number(nguon.loai);
  const tenOGiam = oGiamCuaLoai(loai);
  if (!tenOGiam) {
    loi.loai = `loại combo '${nguon.loai}' chưa chạy được — extension mới có DOM của loại 3 (giảm số tiền) và `
      + "loại 2 (giảm %); loại 1 (giá đặc biệt) chưa dò DOM";
  }

  const muaSl = Number(nguon.muaSl);
  if (!(Number.isInteger(muaSl) && muaSl >= 2)) {
    loi.muaSl = `'muaSl' không hợp lệ: '${nguon.muaSl}' — phải là số nguyên ≥ 2 (mua 1 sp thì không thành combo)`;
  }

  const giam = Number(nguon.giam);
  if (!(Number.isInteger(giam) && giam >= 1)) {
    loi.giam = `'giam' không hợp lệ: '${nguon.giam}' — phải là số nguyên ≥ 1, extension KHÔNG tự thay giá trị`;
  } else if (loai === 2 && giam > 99) {
    loi.giam = `'giam'=${giam} không hợp lệ cho combo giảm % (phải 1–99)`;
  }

  const gioiHan = Number(nguon.gioiHan);
  if (!(Number.isInteger(gioiHan) && gioiHan >= 1)) {
    loi.gioiHan = `'gioiHan' không hợp lệ: '${nguon.gioiHan}' — phải là số nguyên ≥ 1`;
  }

  return { ten, bd, kt, gio, loai, tenOGiam, muaSl, giam, gioiHan, loi };
}

/**
 * Tham số MẶC ĐỊNH của lượt DÒ. Đặt ở đây chứ không nhét làm giá trị dự phòng trong
 * {@link chayComboChoTabDangMo} — luồng thật KHÔNG được có mặc định ngầm nào.
 *
 * KHOẢNG = hôm nay → hôm nay + {@link NGAY_DAT_TRUOC_LUOT_DO}, đúng đường của luồng thật: có vậy lượt dò mới
 * phủ được đoạn CHUYỂN THÁNG ở bước 5 (~6 lần bấm mũi tên) và trần 180 ngày của Shopee — xem `thamSoLuotDo`
 * của `flow-km.js` cho ca "công cụ chẩn đoán có 0% độ phủ trên đoạn code bắt buộc".
 * Thông số combo lấy đúng mặc định người dùng chốt 25/08/2026 (plan §1): loại 3, mua 2 giảm 2.000đ, giới hạn 3.
 */
function thamSoLuotDo() {
  const bd = new Date();
  const kt = new Date();
  kt.setDate(kt.getDate() + NGAY_DAT_TRUOC_LUOT_DO);
  const mocBd = mocNgayLuotDo(bd);
  return {
    // Tên phải ≤ 25 ký tự nên KHÔNG gắn `Date.now()` như lượt dò KM — dùng đúng khuôn tên thật của combo
    // (`KmDialogViewModel.SinhTenComboMacDinh`): "Combo dd-MM-yyyy" = 16 ký tự, mang ngày BẮT ĐẦU.
    ten: "Combo " + mocBd.ngayDayDu,
    bd: mocBd,
    kt: mocNgayLuotDo(kt),
    // 23:00 chứ không phải 11:00: mốc BẮT ĐẦU nay là HÔM NAY, mà nút dò được bấm giữa ban ngày — đặt 11:00 thì
    // mọi lượt dò sau 11h đều đỏ ở bước 4 vì mốc đã ở quá khứ, một cái đỏ vô nghĩa che mất mọi cái đỏ đáng đọc.
    gio: "23:00",
    loai: 3,
    muaSl: 2,
    giam: 2000,
    gioiHan: 3,
  };
}

// ── Khung chạy từng bước ────────────────────────────────────────────────────────────────────────────────────

/**
 * Bấm mũi tên cho tới khi popup đang mở đứng ĐÚNG tháng/năm mục tiêu.
 *
 * ĐỎ KHI: không đọc được tháng/năm · không thấy nút mũi tên · bấm rồi mà tiêu đề KHÔNG ĐỔI · quá
 * {@link TRAN_CHUYEN_THANG} lần vẫn chưa tới. Cả bốn đều trả `{ok:false, loi}`.
 * <para>Đây là bước TRỢ GIÚP chứ không phải cửa chặn — trọng tài thật là phép ĐỌC LẠI chuỗi thời gian ở cuối
 * bước gọi (xem `flow-km.js`: bản thoát sớm ở đây đã giết cả 12 shop đêm 23/08/2026). Vì vậy bước gọi KHÔNG
 * return khi hàm này đỏ, chỉ ghi lại vào nhật ký bước.</para>
 */
async function chuyenToiThang(chay, thangDich, namDich) {
  const nhatKy = [];
  for (let i = 0; i <= TRAN_CHUYEN_THANG; i++) {
    const doc = await chay(pageDocThangNamPopup, []);
    if (!doc || doc.loi) { return { ok: false, loi: (doc && doc.loi) || "không đọc được tháng/năm", nhatKy }; }
    if (doc.thang === thangDich && doc.nam === namDich) { return { ok: true, tieuDe: doc.tieuDe, nhatKy }; }
    if (i === TRAN_CHUYEN_THANG) {
      return { ok: false, nhatKy,
               loi: `bấm ${TRAN_CHUYEN_THANG} lần vẫn chưa tới ${thangDich}-${namDich} (đang ở "${doc.tieuDe}")` };
    }

    const tien = (namDich * 12 + thangDich) > (doc.nam * 12 + doc.thang);
    const bam = await chay(pageBamChuyenThang, [tien]);
    nhatKy.push({ tu: doc.tieuDe, tien, bam });
    if (!bam || !bam.ok) { return { ok: false, loi: (bam && bam.loi) || "không bấm được nút chuyển tháng", nhatKy }; }

    // CHỜ tiêu đề ĐỔI THẬT (React vẽ lại bảng ngày) — không sleep mù. Đây cũng là phép đọc lại của cú bấm.
    let daDoi = false;
    for (let j = 0; j < 20; j++) {
      await sleep(250);
      const lai = await chay(pageDocThangNamPopup, []);
      if (lai && !lai.loi && lai.tieuDe !== doc.tieuDe) { daDoi = true; break; }
    }
    if (!daDoi) { return { ok: false, loi: `bấm chuyển tháng mà tiêu đề vẫn "${doc.tieuDe}"`, nhatKy }; }
  }
  return { ok: false, loi: "vòng chuyển tháng thoát bất thường", nhatKy };
}

/**
 * Đặt MỘT ô thời gian của form: mở popup → CHUYỂN THÁNG tới đúng tháng/năm → click ô ngày → cột giờ → cột phút
 * → "Xác nhận". Phép ĐỌC LẠI do bước gọi làm — so chuỗi ĐẦY ĐỦ ngày + giờ đã chuẩn hoá.
 */
async function datOThoiGian(chay, tenO, gio, ngayTrongThang, thangDich, namDich) {
  const moO = await chay(pageMoOThoiGian, [tenO]);
  await sleep(1200);
  const thangNamKhiMo = await chay(pageDocThangNamPopup, []);
  const chuyenThang = await chuyenToiThang(chay, thangDich, namDich);
  const ngay = await chay(pageClickNgayTheoSo, [ngayTrongThang]);

  // ⚠ CHỌN NGÀY HỎNG ⇒ DỪNG NGAY, **KHÔNG** bấm Xác nhận — chuyển từ bản km (vá 27/08/2026, sổ P-20260829-2).
  //
  // Bằng chứng bên km (dump `logs/km-dom-20260827-091851.json`): `ngay.ok=false` mà `xacNhan.ok=true` — luồng
  // vẫn chốt một picker chưa chọn được gì, ô thời gian giữ nguyên GIÁ TRỊ CŨ và bằng chứng về cú trượt bị xoá
  // sạch, chỉ còn câu "ĐỌC LẠI KHÔNG KHỚP" không nói được vì sao. Bản combo trước đây đi TUYẾN TÍNH nên dính
  // đúng lớp lỗi đó. Hỏng ở đâu thì dừng ở đó; trọng tài cuối vẫn là phép ĐỌC LẠI của bước gọi.
  if (!ngay || !ngay.ok) {
    const manh = ["chọn NGÀY " + ngayTrongThang + "-" + thangDich + "-" + namDich
      + " hỏng ⇒ KHÔNG bấm Xác nhận: " + ((ngay && ngay.loi) || "(không rõ)")];
    if (ngay && ngay.coSan) { manh.push("ngày chọn được: [" + ngay.coSan.join(",") + "]"); }
    if (chuyenThang && chuyenThang.ok === false) {
      manh.push("chuyển tháng hỏng: " + (chuyenThang.loi || "(không rõ)"));
    }
    return { moO, thangNamKhiMo, chuyenThang, ngay, gio: null, phut: null, xacNhan: null, loi: manh.join(" · ") };
  }

  await sleep(900);
  const gioClick = await chay(pageClickOThoiGian, [0, gio[0]]);
  await sleep(500);
  const phut = await chay(pageClickOThoiGian, [1, gio[1]]);
  await sleep(500);
  const xacNhan = await chay(pageXacNhanPopup, []);
  await sleep(1200);
  return { moO, thangNamKhiMo, chuyenThang, ngay, gio: gioClick, phut, xacNhan };
}

/**
 * Điền MỘT ô số của form rồi ĐỌC LẠI — dùng cho `tiers.0.minAmount`, ô mức giảm và `usageLimit`.
 * So theo CHỮ SỐ (xem {@link chiSo}): ô tiền tệ của React tự chèn dấu chấm.
 */
async function goSoRoiDocLai(chay, sel, giaTri) {
  const go = await chay(pageGoOReact, [sel, String(giaTri), 90]);
  await sleep(500);
  const docLai = await chay(pageDocGiaTriO, [sel]);
  return { go, docLai, khop: chiSo(docLai) === chiSo(giaTri) };
}

/**
 * CHỜ BẢNG MODAL LẮNG rồi mới cho tick — trị GỐC của lỗi tick hụt (xem {@link LANG_TRAN_NHIP}).
 *
 * "Lắng" = HAI lượt đọc liên tiếp cho CÙNG một vân tay `số dòng # 3 khoá dòng đầu`. Số dòng bắt được ca bảng
 * đang mọc thêm dòng; khoá dòng bắt được ca bảng thay ruột mà số dòng tình cờ không đổi.
 *
 * Ca 0 DÒNG cố ý khó tính hơn: hai lượt bằng nhau ở 0 chưa được tính là lắng cho tới nhịp
 * {@link LANG_TRAN_NHIP_RONG} — "chưa nạp xong" và "trang không có dòng nào" nhìn giống hệt nhau ở nhịp đầu.
 *
 * `soDongToiThieu` (chuẩn học được từ các trang trước, 0 = chưa biết) là lưới cho ca KHỰNG GIỮA CHỪNG — xem
 * {@link LANG_TRAN_NHIP_THIEU}. Đứng yên mà THIẾU dòng so với chuẩn thì chờ thêm, tối đa 10 nhịp.
 *
 * KHÔNG ném và KHÔNG hạ `ok` của ai: hết trần mà chưa lắng thì trả `lang:false` để bước gọi vẫn tick (thà tick
 * trên bảng chưa lắng rồi BÙ còn hơn bỏ trắng cả trang) và ghi một cảnh báo.
 */
async function choBangLang(chay, soDongToiThieu, soLanBangNhau) {
  const chuan = Number.isInteger(soDongToiThieu) && soDongToiThieu > 0 ? soDongToiThieu : 0;
  const canBang = Number.isInteger(soLanBangNhau) && soLanBangNhau >= 2 ? soLanBangNhau : LANG_LAN_BANG_NHAU;
  let vanTruoc = null;
  let doc = null;
  let soNhip = 0;
  let giong = 1;
  for (let i = 0; i < LANG_TRAN_NHIP; i++) {
    doc = await chay(pageDemDaTick, []);
    soNhip = i + 1;
    const van = (doc && doc.coModal) ? (doc.soDong + "#" + (doc.vanTay || "")) : null;
    giong = (van !== null && van === vanTruoc) ? giong + 1 : 1;
    if (van !== null && giong >= canBang) {
      const duBangChung = doc.soDong > 0 || soNhip >= LANG_TRAN_NHIP_RONG;
      const duChuan = doc.soDong >= chuan || soNhip >= LANG_TRAN_NHIP_THIEU;
      if (duBangChung && duChuan) { return { lang: true, soNhip, doc, duChuan: doc.soDong >= chuan }; }
    }
    vanTruoc = van;
    await sleep(LANG_NHIP_MS);
  }
  return { lang: false, soNhip, doc, duChuan: !!(doc && doc.soDong >= chuan) };
}

/**
 * CHỜ bảng modal đổi sang TRANG KHÁC sau một cú bấm "trang sau".
 *
 * Điều kiện dừng là `vanTay` ĐỔI **và** trang mới đã có dòng: vân tay của một bảng vừa bị dọn sạch cũng "khác"
 * vân tay cũ, nhận nó là đổi thì lượt tick kế tiếp chạy trên một bảng rỗng.
 *
 * `pagerDaNhay` là câu trả lời cho "cú bấm có ăn không" khi hết ngân sách: nhãn trang active đã nhích ⇒ cú bấm
 * ĐÃ ăn, chỉ là bảng vẽ chậm ⇒ bước gọi phải CHỜ THÊM, tuyệt đối không bấm lại (bấm lại là nhảy cóc một trang,
 * mất trắng ~10 sản phẩm mà không cảnh báo nào nổi lên).
 */
async function choDoiTrang(chay, vanTayTruoc, nhanTrangTruoc) {
  let doc = null;
  for (let i = 0; i < CHO_DOI_TRANG_TRAN_NHIP; i++) {
    await sleep(CHO_DOI_TRANG_NHIP_MS);
    doc = await chay(pageDemDaTick, []);
    if (doc && doc.coModal && doc.soDong > 0 && doc.vanTay !== vanTayTruoc) {
      return { doi: true, soNhip: i + 1, doc, pagerDaNhay: true };
    }
  }
  return {
    doi: false,
    soNhip: CHO_DOI_TRANG_TRAN_NHIP,
    doc,
    pagerDaNhay: !!(doc && doc.coModal && doc.trangHienTai && doc.trangHienTai !== nhanTrangTruoc),
  };
}

/**
 * Chạy TRỌN luồng "Tạo Combo Khuyến Mãi" cho SHOP ĐANG MỞ (tab do `openShopDetail` để lại).
 *
 * Dùng chung cho hai lệnh: `dumpCombo` (nút dò) và `chayCombo` (luồng thật) — CỐ Ý KHÔNG chép thành hai bản,
 * y kỷ luật của `flow-km.js`: hai bản song song thì bản ít chạy sẽ mục dần mà không ai biết. Nghĩa là **nút dò
 * cũng TẠO THẬT**.
 *
 * <b>MỘT BƯỚC CỐT LÕI HỎNG ⇒ DỪNG SHOP NGAY</b> (ràng buộc (1) đầu file): mỗi bước `ok:false` là `return` luôn,
 * không đi tiếp tới bước 10. Ngoại lệ DUY NHẤT là nhánh "modal không còn sản phẩm nào" ở bước 9 — đó là kết
 * luận NGHIỆP VỤ (`boQua`), không phải bước hỏng.
 *
 * @param {object} ts Tham số — xem {@link docThamSoCombo}. `null` = lượt DÒ (dùng {@link thamSoLuotDo}).
 * @returns {Promise<{buoc:Array, soSanPham:number, daTao:boolean, boQua:string|null, canhBao:Array,
 *                    captcha:boolean}>}
 */
export async function chayComboChoTabDangMo(ts) {
  const tabId = tabShop();
  const ds = [];
  /** Cảnh báo THÔ (một phần tử một lần xảy ra) — `ketThuc` mới gom lại theo loại. */
  const canhBaoTho = [];
  const themCanhBao = (loai, cau, gom, chiTiet) => canhBaoTho.push({ loai, cau, gom: gom || cau, chiTiet });
  /**
   * Gói kết quả CHUẨN của một lượt — mọi lối ra đi qua đây để không lối nào quên trường nào.
   * `captcha` suy từ chính các bước (khung `buoc` đánh dấu `captcha:true` khi bắt được `/verify`), nên cờ này
   * không thể lệch với thứ đã gửi lên C#.
   */
  const ketThuc = (them) =>
    Object.assign(
      { buoc: ds, soSanPham: 0, daTao: false, boQua: null,
        canhBao: gomCanhBao(canhBaoTho, TRAN_CANH_BAO),
        captcha: ds.some((b) => b.captcha === true) },
      them || {});

  if (tabId == null) {
    // HỎNG HẠ TẦNG, KHÔNG PHẢI "shop rỗng" — `boQua` chỉ dành cho kết luận nghiệp vụ, xem `flow-km.js`.
    ds.push({
      ten: "0. Tab shop", ok: false, selectorDung: null, kyVong: null, kieuSo: null, docLai: null,
      truoc: null, sau: null, ghiChu: null,
      loi: "chưa có tab shop (openShopDetail chưa chạy, hoặc service worker đã chết giữa chừng)",
    });
    return ketThuc();
  }
  const chay = (fn, args) => execInTab(tabId, fn, args || []);

  // BƯỚC GIẢ "0. Tham số" — TRẢ VỀ TRƯỚC KHI ĐỤNG TRANG. Tham số hỏng thì không có việc gì để làm trên trang;
  // để bước 7/8 tự báo là tốn ~12 phút lật modal rồi mới đỏ, và bỏ lại một form dở dang trên shop THẬT.
  const tp = docThamSoCombo(ts);
  const loiThamSo = cauLoiThamSo(tp.loi, "Combo");
  if (loiThamSo) {
    ds.push({
      ten: "0. Tham số", ok: false, selectorDung: null, kyVong: null, kieuSo: null, docLai: null,
      truoc: null, sau: null, ghiChu: null, loi: loiThamSo,
    });
    send({ action: "progress", message: `[Combo] 0. Tham số: DỪNG — ${loiThamSo}` });
    return ketThuc();
  }

  const { ten: tenCt, bd, kt, gio, loai, tenOGiam, muaSl, giam, gioiHan } = tp;
  const hai = (n) => String(n).padStart(2, "0");
  const khuonMoc = (m) => m.nam + "-" + hai(m.thang) + "-" + hai(m.ngayTrongThang) + " " + gio[0] + ":" + gio[1];
  // HAI mốc KHÁC ngày, CÙNG giờ — đây là chỗ bản trước dựng hai chuỗi từ cùng một ngày.
  const mocBd = khuonMoc(bd);
  const mocKt = khuonMoc(kt);

  // 1) Vào "Khuyến Mãi của Shop" qua SIDEBAR (không gọi thẳng URL) — DÙNG LẠI đúng đường của luồng KM: ngay sau
  //    SSO sidebar chưa render, và mốc "sidebar đã hiện" là mốc "trang đã lắng" đáng tin nhất (hai lần dính
  //    verify/traffic đều xảy ra khi chạm vào trang trong 3–4 giây đầu).
  const b1 = await buoc(ds, "1. Vào Khuyến Mãi của Shop", async () => {
    const truoc = await chay(() => location.href);
    const SEL = 'li.ps_menu_marketing a[href="/portal/marketing/list/discount"]';

    let coLink = 0;
    for (let i = 0; i < 40; i++) {
      coLink = await chay(pageDemPhanTuHien, [SEL]);
      if (coLink > 0) { break; }
      await sleep(500);
    }
    if (!coLink) {
      const sauHong = await chay(() => location.href);
      return { selectorDung: null, truoc, sau: sauHong, kyVong: "/list/discount", kieuSo: "chua", docLai: sauHong,
               ghiChu: "KHÔNG thấy link 'Khuyến Mãi của Shop' trong sidebar sau 20s" };
    }
    // Chờ 3s cho trang Kênh Người Bán lắng và các script xác thực phiên / WAF nạp xong trước khi click
    await sleep(3000);
    const sel = await chay(pageClickTheoSelector, [[SEL], null]);

    let sau = "";
    for (let i = 0; i < 30; i++) {
      await sleep(500);
      sau = await chay(() => location.href);
      if (String(sau || "").indexOf("/list/discount") >= 0) { break; }
    }
    return { selectorDung: sel, truoc, sau, kyVong: "/list/discount", kieuSo: "chua", docLai: sau,
             ghiChu: "sidebar → link 'Khuyến Mãi của Shop' (đang hiện)" };
  }, tabId, "Combo");
  if (!b1.ok) { return ketThuc(); }

  // 2) Bấm "Tạo" ở THẺ "Combo Khuyến Mãi" — khớp theo CHỮ TIÊU ĐỀ, không theo chỉ số cột (plan §5b).
  let urlFormTao = "";
  const b2 = await buoc(ds, "2. Bấm Tạo ở thẻ Combo Khuyến Mãi", async () => {
    const truoc = await chay(() => location.href);
    const bam = await chay(pageBamTheChuongTrinh, ["Combo Khuyến Mãi"]);
    // Form mở = ô TÊN của form combo đã có trong DOM. Bám dấu hiệu của FORM, không bám URL.
    let coO = 0;
    for (let i = 0; i < 30; i++) {
      await sleep(500);
      coO = await chay(pageDemPhanTuHien, ['input[name="name"]']);
      if (coO > 0) { break; }
    }
    urlFormTao = await chay(() => location.href);
    return {
      selectorDung: ".src-components---actionRow--3_dWQ button", truoc,
      sau: { url: urlFormTao, bam }, loi: bam && bam.loi,
      kyVong: "1", docLai: String(coO > 0 ? 1 : 0),
      ghiChu: "Kỳ vọng: form mở ⇒ có input[name=\"name\"] đang hiện. Thẻ khớp theo CHỮ tiêu đề — Shopee đổi thứ "
        + "tự cột là tạo nhầm loại chương trình THẬT.",
    };
  }, tabId, "Combo");
  if (!b2.ok) { return ketThuc(); }

  // 3) Tên chương trình (ô maxlength=25) — setter gốc + đọc lại.
  const b3 = await buoc(ds, "3. Tên chương trình", async () => {
    const go = await chay(pageGoOReact, ['input[name="name"]', tenCt, 70]);
    await sleep(600);
    const docLai = await chay(pageDocGiaTriO, ['input[name="name"]']);
    return {
      truoc: null, sau: { go }, loi: go && go.loi,
      kyVong: tenCt, docLai,
      ghiChu: "Setter gốc của HTMLInputElement.prototype (vượt _valueTracker của React) + ĐỌC LẠI. Đọc lại cụt "
        + "hơn kỳ vọng ⇒ ô maxlength cắt đuôi, phải sửa ở tầng form C#.",
    };
  }, tabId, "Combo");
  if (!b3.ok) { return ketThuc(); }

  // 4) Ô thời gian BẮT ĐẦU.
  const b4 = await buoc(ds, `4. Thời gian BẮT ĐẦU (${bd.ngayDayDu} ${gio[0]}:${gio[1]})`, async () => {
    const truoc = await chay(pageDocOThoiGian, ["period.startTime"]);
    const dat = await datOThoiGian(chay, "period.startTime", gio, bd.ngayTrongThang, bd.thang, bd.nam);
    const thoText = await chay(pageDocOThoiGian, ["period.startTime"]);
    const chuan = chuanHoaMocThoiGian(thoText);
    return {
      truoc, sau: Object.assign({}, dat, { textOSauCung: thoText }),
      // Đọc không ra định dạng ⇒ in NGUYÊN VĂN vào `loi` (C# chỉ chuyển tiếp trường `loi` lên nhật ký).
      loi: dat.loi || (chuan === null ? `KHÔNG đọc được định dạng ô thời gian, nguyên văn: '${thoText}'` : null),
      kyVong: mocBd, docLai: chuan,
      ghiChu: "Mọi truy vấn SCOPE trong popup đang hiện; chuyển tháng tới đúng tháng/năm RỒI mới khớp ĐÚNG số ngày.",
    };
  }, tabId, "Combo");
  if (!b4.ok) { return ketThuc(); }

  // 5) Ô thời gian KẾT THÚC — cùng đường, cùng phép đọc lại, nhưng NGÀY KHÁC (mốc đóng khoảng) và CÙNG giờ.
  //
  // ⚠ ĐÂY LÀ BƯỚC PHẢI CHUYỂN THÁNG ~6 LẦN: bước 4 đặt cho HÔM NAY nên picker mở ra đã đúng tháng.
  // `TRAN_CHUYEN_THANG` = 8 vẫn dư (đo brute-force 2024→2032: tối đa 6).
  const b5 = await buoc(ds, `5. Thời gian KẾT THÚC (${kt.ngayDayDu} ${gio[0]}:${gio[1]})`, async () => {
    const truoc = await chay(pageDocOThoiGian, ["period.endTime"]);
    const dat = await datOThoiGian(chay, "period.endTime", gio, kt.ngayTrongThang, kt.thang, kt.nam);
    const thoText = await chay(pageDocOThoiGian, ["period.endTime"]);
    const chuan = chuanHoaMocThoiGian(thoText);
    return {
      truoc, sau: Object.assign({}, dat, { textOSauCung: thoText }),
      loi: dat.loi || (chuan === null ? `KHÔNG đọc được định dạng ô thời gian, nguyên văn: '${thoText}'` : null),
      kyVong: mocKt, docLai: chuan,
    };
  }, tabId, "Combo");
  if (!b5.ok) { return ketThuc(); }

  // 6) Loại combo — TICK RADIO RỒI XÁC NHẬN Ô MỨC GIẢM ĐÚNG TỒN TẠI (plan §5, LUẬT BẮT BUỘC).
  //
  // ⚠ ĐÂY LÀ CỬA CHẶN, ĐỪNG NỚI. Chỉ thấy `tiers.0.discountPercentage` mà ta đang chạy loại 3 nghĩa là cú click
  // radio KHÔNG ăn (React chưa vẽ lại) — gõ `2000` vào ô ấy là khai báo **giảm 2000%**, và Shopee vẫn cho bấm
  // "Xác nhận". `name` là cửa chặn; tiền tố `₫` / hậu tố `%GIẢM` / class `currency-input` là ba phép đọc lại
  // độc lập, ghi vào `sau` để người đọc dump đối chiếu.
  const b6 = await buoc(ds, `6. Loại combo (radio value=${loai}) + xác nhận ô mức ưu đãi`, async () => {
    const tick = await chay(pageTickLoaiCombo, [loai]);
    let doO = null;
    for (let i = 0; i < 20; i++) {
      await sleep(400);
      doO = await chay(pageDoOMucUuDai, []);
      if (doO && ((loai === 3 && doO.coDiscountValue) || (loai === 2 && doO.coDiscountPercentage))) { break; }
    }
    if (tick && tick.soONhanDuoc > 1) {
      themCanhBao("radio-khong-duy-nhat",
        `selector input[type=radio][value="${loai}"] khớp ${tick.soONhanDuoc} ô trên trang — đã bấm ô ĐẦU TIÊN`);
    }
    const coODung = !!(doO && ((loai === 3 && doO.coDiscountValue) || (loai === 2 && doO.coDiscountPercentage)));
    return {
      selectorDung: `input[type=radio][value="${loai}"]`,
      truoc: { tick }, sau: { doOMucUuDai: doO }, loi: tick && tick.loi,
      kyVong: tenOGiam, docLai: coODung ? tenOGiam : ((doO && doO.coDiscountPercentage) ? "tiers.0.discountPercentage"
        : ((doO && doO.coDiscountValue) ? "tiers.0.discountValue" : "(không thấy ô mức giảm nào)")),
      ghiChu: "ĐỎ KHI: click radio không ăn ⇒ ô mức giảm còn là biến thể của loại KHÁC. TUYỆT ĐỐI không gõ tiếp.",
    };
  }, tabId, "Combo");
  if (!b6.ok) { return ketThuc(); }

  // 7) Mức ưu đãi: mua N sản phẩm (`tiers.0.minAmount`) + giảm X (ô của ĐÚNG loại, đã xác nhận ở bước 6).
  const b7 = await buoc(ds, `7. Mức ưu đãi: mua ${muaSl} · giảm ${giam}`, async () => {
    const oSl = await goSoRoiDocLai(chay, 'input[name="tiers.0.minAmount"]', muaSl);
    const oGiam = await goSoRoiDocLai(chay, 'input[name="' + tenOGiam + '"]', giam);
    return {
      selectorDung: 'input[name="tiers.0.minAmount"] + input[name="' + tenOGiam + '"]',
      sau: { oSoLuong: oSl, oMucGiam: oGiam },
      loi: [(oSl.go && oSl.go.loi) || null, (oGiam.go && oGiam.go.loi) || null].filter((x) => x).join(" · ") || null,
      // Hai ô một kỳ vọng: chỉ cần MỘT ô không ăn là bước đỏ và shop dừng — không có combo nào được tạo với
      // đúng một nửa thông số.
      kyVong: "OK|OK",
      docLai: (oSl.khop ? "OK" : "SL=" + chiSo(oSl.docLai)) + "|" + (oGiam.khop ? "OK" : "GIAM=" + chiSo(oGiam.docLai)),
      ghiChu: "So theo CHỮ SỐ: ô tiền tệ của React tự chèn dấu chấm (2000 → 2.000).",
    };
  }, tabId, "Combo");
  if (!b7.ok) { return ketThuc(); }

  // 8) Giới hạn đặt hàng.
  const b8 = await buoc(ds, `8. Giới hạn đặt hàng = ${gioiHan}`, async () => {
    const o = await goSoRoiDocLai(chay, 'input[name="usageLimit"]', gioiHan);
    return {
      selectorDung: 'input[name="usageLimit"]', sau: { o }, loi: (o.go && o.go.loi) || null,
      kyVong: chiSo(gioiHan), docLai: chiSo(o.docLai),
    };
  }, tabId, "Combo");
  if (!b8.ok) { return ketThuc(); }

  // 9) Modal "Chọn Sản Phẩm": mở → chờ bảng LẮNG → tick "chọn tất cả" → BÙ dòng còn sót → sang trang →
  //    Xác nhận MODAL.
  //
  // ⚠ NHÁNH "shop rỗng" nay CÓ, nhưng CHỈ khi HAI DẤU ĐỘC LẬP CÙNG ĐỒNG Ý (0 dòng + bảng tự khai rỗng) — xem
  // nhánh `soDong === 0` bên dưới. Trước 26/08/2026 ở đây không có nhánh nào cả vì chưa dò được dấu rỗng của
  // `eds-react`, và đoán bừa một dấu là bỏ OAN cả một shop trong im lặng (ca Alina Store1 đêm 23/08/2026 của
  // luồng KM: hàng nghìn sản phẩm bị kết luận "shop KHÔNG có sản phẩm nào"). Dấu lệch nhau ⇒ vẫn ĐỎ, ồn ào.
  let soSpDaTick = 0;
  const b9 = await buoc(ds, "9. Modal Chọn Sản Phẩm (tick hết trang)", async () => {
    const moModal = await chay(pageMoModalSanPham, []);

    // CHỜ-ĐIỀU-KIỆN: modal mở VÀ bảng CÓ DÒNG. Có dòng là bằng chứng DƯƠNG, chắc chắn — thấy là thoát ngay.
    let doModal = null;
    for (let i = 0; i < CHO_MODAL_NHIP; i++) {
      doModal = await chay(pageDoModalSanPham, []);
      if (doModal && doModal.coModal && doModal.soDong > 0) { break; }
      await sleep(500);
    }
    const giay = (CHO_MODAL_NHIP * 500) / 1000;
    if (!doModal || !doModal.coModal) {
      const soHop = doModal && typeof doModal.soHopModalTrongDom === "number" ? doModal.soHopModalTrongDom : "?";
      const tieuDe = (doModal && doModal.tieuDeDaThay) || [];
      return { selectorDung: ".src-components---addItems--2wv99", sau: { moModal, doModal }, kyVong: "1", docLai: "0",
               loi: `modal Chọn Sản Phẩm KHÔNG mở sau ${giay}s (${soHop} hộp modal trong DOM; tiêu đề thấy được: `
                 + (tieuDe.length ? tieuDe.join(" / ") : "không có") + ")" };
    }
    if (doModal.soDong === 0) {
      // BẢNG RỖNG THẬT hay BẢNG CHƯA TẢI? Hai kết luận trái ngược nhau trên cùng một quan sát "0 dòng", và chọn
      // sai chiều nào cũng đắt: chấm HỎNG một shop rỗng thật thì người dùng đi soi một shop không có gì để sửa
      // (ca THẬT "Shop9x Store" 26/08/2026 — shop có ĐÚNG 1 sản phẩm, mà sản phẩm ấy đã nằm trong combo tạo lúc
      // 16:51 nên Shopee loại khỏi modal ⇒ modal rỗng THẬT); ngược lại, chấm "rỗng" một bảng chưa tải là bỏ OAN
      // cả một shop trong im lặng (ca Alina Store1 đêm 23/08/2026 của luồng KM: hàng nghìn sản phẩm).
      //
      // ⇒ LUẬT: chỉ kết luận rỗng khi HAI DẤU ĐỘC LẬP CÙNG ĐỒNG Ý — 0 dòng, VÀ bảng tự khai rỗng (class
      // `eds-react-table-empty` HOẶC dòng placeholder ĐANG HIỆN). Lệch nhau ⇒ giữ nguyên HỎNG.
      const dauRong = (doModal.soBangRong > 0) || (doModal.soPlaceholderHien > 0);
      const moTaDau = `bảng mang class -empty: ${doModal.soBangRong}/${doModal.soBang}; dòng placeholder đang `
        + `hiện: ${doModal.soPlaceholderHien}/${doModal.soPlaceholder}`;
      if (dauRong) {
        // `ok: true` — đây là KẾT LUẬN NGHIỆP VỤ (không còn sản phẩm nào để thêm), không phải bước hỏng: bước đã
        // đo được đúng thứ nó khẳng định. Bước này KHÔNG khai `kyVong` ở nhánh đây nên `ok` phải tự khai.
        return {
          selectorDung: ".src-components---addItems--2wv99", sau: { moModal, doModal }, ok: true, shopRong: true,
          ghiChu: `Modal ĐANG MỞ, 0 dòng + bảng TỰ KHAI rỗng (${moTaDau}) ⇒ không còn sản phẩm nào để thêm vào `
            + "combo (sản phẩm đang nằm trong chương trình khác cũng bị Shopee loại khỏi modal). BỎ QUA shop, "
            + "KHÔNG tạo combo trống.",
        };
      }
      return { selectorDung: ".src-components---addItems--2wv99", sau: { moModal, doModal }, kyVong: "1", docLai: "0",
               loi: `modal mở nhưng bảng 0 dòng sau ${giay}s mà KHÔNG có dấu rỗng nào (${moTaDau}; tr không `
                 + `data-row-key: ${doModal.soTrTat}; modal tự khai: `
                 + `${(doModal.chuNghiRong || []).join(" | ") || "không có chữ nào"}) — nhiều khả năng bảng CHƯA `
                 + "TẢI, chưa đủ căn cứ kết luận shop không còn sản phẩm" };
    }

    const nhatKyTrang = [];
    let trang = 1;
    let chamTran = false;
    let soTrangThayDuoc = soTrangLonNhat(doModal.nhanCacTrang || []);
    let lyDoDung = null;
    // CHUẨN SỐ DÒNG/TRANG học được từ chính modal này (số dòng nhiều nhất từng thấy) + số dòng của từng trang.
    // Hai thứ này là lưới cuối cho ca "bảng khựng giữa chừng" — xem {@link LANG_TRAN_NHIP_THIEU} và cảnh báo
    // `trang-thieu-dong` ở cuối vòng.
    let soDongChuan = 0;
    const soDongTheoTrang = [];
    while (trang <= TRAN_TRANG_MODAL) {
      // ① CHỜ BẢNG LẮNG RỒI MỚI TICK. Không còn `sleep` cố định nào ở đây — xem {@link choBangLang}.
      const lang = await choBangLang(chay, soDongChuan);
      if (!lang.lang) {
        themCanhBao("bang-khong-lang",
          `trang ${trang}: bảng KHÔNG lắng sau ${(LANG_TRAN_NHIP * LANG_NHIP_MS) / 1000}s `
            + `(đọc cuối: ${(lang.doc && lang.doc.soDong) || 0} dòng) — vẫn tick, rồi bù`,
          "bảng modal không lắng trong ngân sách chờ", `trang ${trang}`);
      }

      const tick = await chay(pageTickChonTatCa, []);

      // ② ĐỌC LẠI + BÙ TICK — lưới thứ hai. Cú "chọn tất cả" chỉ phủ những dòng ĐANG có lúc bấm; dòng nào React
      // vẽ xong sau đó thì nằm ngoài. Bù bằng cách tick RIÊNG từng dòng còn thiếu (xem {@link pageTickDongConSot}
      // — tuyệt đối không bù bằng cách bấm lại ô "chọn tất cả", nó là công tắc).
      //
      // ⚠ MỖI LƯỢT BÙ CHỜ BẢNG LẮNG LẠI TRƯỚC KHI ĐẾM. Đây là chỗ rig bắt được một lỗ: bảng có thể vẽ 5 dòng,
      // KHỰNG một nhịp (đủ để lượt chờ ở ① tưởng đã lắng), rồi vẽ nốt 5 dòng nữa. Bù theo nhịp cố định thì mấy
      // lượt bù tiêu hết trước khi 5 dòng cuối kịp hiện ⇒ vẫn mất hàng, mà lại KHÔNG cảnh báo (số đọc cuối cùng
      // "đủ" so với số dòng thấy được lúc ấy). Chờ lắng lại rồi mới đếm thì con số đem so mới là con số THẬT.
      let dem = null;
      const buTick = [];
      for (let lan = 0; lan <= TRAN_BU_TICK; lan++) {
        const langLai = await choBangLang(chay, soDongChuan, LANG_LAN_BANG_NHAU_SAU_TICK);
        dem = langLai.doc;
        if (!dem || !dem.coModal || dem.soDaTick >= dem.soTickDuoc) { break; }
        if (lan === TRAN_BU_TICK) { break; }   // hết lượt bù — phần cảnh báo bên dưới lo tiếp
        const bu = await chay(pageTickDongConSot, []);
        buTick.push({ lan: lan + 1, bu, truocKhiBu: { soDaTick: dem.soDaTick, soTickDuoc: dem.soTickDuoc },
                      bangDaLang: langLai.lang });
      }
      nhatKyTrang.push({ trang, lang: { lang: lang.lang, soNhip: lang.soNhip }, tick, buTick, dem });

      // ĐỌC LẠI CỦA CHÍNH CÚ TICK TRANG NÀY: mọi dòng của trang phải ở trạng thái đã tick.
      // ⚠ KHÔNG hạ `ok` cả bước — trang cuối HOÀN TOÀN có thể có dòng không tick được vì sản phẩm đang nằm
      // trong một chương trình khác và bị Shopee vô hiệu hoá (ca THẬT ở luồng KM, không phải lỗi). Nhưng người
      // dùng phải THẤY, nên nó vào `canhBao` kèm số trang.
      if (!dem || !dem.coModal) {
        themCanhBao("mat-modal-giua-vong", `trang ${trang}: MẤT modal giữa vòng tick — các trang sau không được chọn`);
      } else if (dem.soDaTick < dem.soTickDuoc) {
        // ⚠ Chỉ cảnh báo khi SAU CẢ CÁC LƯỢT BÙ (trần TRAN_BU_TICK) vẫn còn thiếu — cảnh báo cho một cú tick đã tự
        // vá xong là cảnh báo kêu oan, mà kêu oan đủ nhiều lần thì người dùng thôi đọc nó.
        // ⚠ So với `soTickDuoc` (dòng TICK ĐƯỢC), KHÔNG phải `soDong` (mọi dòng). Dòng bị Shopee khoá vì sản
        // phẩm đang ở chương trình khác không bao giờ tick được — đếm chúng vào đây cũng là đẻ cảnh báo oan.
        themCanhBao("tick-khong-du",
          `trang ${trang}: mới tick ${dem.soDaTick}/${dem.soTickDuoc} dòng tick được sau ${buTick.length} lượt bù`
            + (dem.soBiKhoa > 0 ? ` (${dem.soBiKhoa} dòng bị Shopee khoá, không tính)` : ""),
          "tick không phủ hết dòng của trang KỂ CẢ sau khi bù", `trang ${trang}`);
      }
      if (dem && dem.coModal) {
        soSpDaTick += dem.soDaTick;
        soDongTheoTrang.push({ trang, soDong: dem.soDong });
        soDongChuan = Math.max(soDongChuan, dem.soDong);
      }

      // ⚠ CHỤP VÂN TAY BẢNG **TRƯỚC** CÚ BẤM SANG TRANG. Đọc sau cú bấm là một cuộc đua: React có thể đã vẽ
      // xong trang mới, lúc đó "vân tay trước" chính là trang MỚI ⇒ vòng chờ dưới đây ngồi hết ngân sách rồi
      // kết luận "bảng không đổi" ⇒ dừng sớm + cảnh báo OAN, và các trang còn lại KHÔNG được tick.
      // `dem` đọc ở lượt chờ-lắng cuối của TRANG NÀY nên nó là ảnh chụp đúng thời điểm cần.
      // `nhanTrangTruoc` = nhãn trang active ĐỌC CÙNG LÚC — trọng tài của câu "cú bấm sang trang có ăn không".
      const vanTayTruoc = dem && dem.vanTay;
      const nhanTrangTruoc = dem && dem.trangHienTai;
      const sang = await chay(pageSangTrangSau, []);
      if (sang && sang.coSan) { soTrangThayDuoc = Math.max(soTrangThayDuoc, soTrangLonNhat(sang.coSan)); }
      if (!sang || !sang.ok) {
        // Ba ca RẤT KHÁC NHAU, đừng gộp (y phân loại của `flow-km.js`):
        //  · `hetTrang` — nút trang sau disabled ⇒ BÌNH THƯỜNG, đây là cách vòng kết thúc.
        //  · `khongCoNut` — bình thường VỚI ĐIỀU KIỆN lúc mở modal cũng không có nút (shop ít hàng); lúc mở CÓ
        //    mà giờ MẤT là hỏng hạ tầng giữa chừng.
        //  · còn lại (mất modal, click trượt) — HỎNG ⇒ luôn cảnh báo: shop còn sản phẩm chưa được chọn mà bước
        //    vẫn xanh vì form đã có hàng.
        lyDoDung = (sang && sang.loi) || "không bấm được nút trang sau";
        const khongCoNutBinhThuong = !!(sang && sang.khongCoNut) && !doModal.coNutTrangSau;
        if (!sang || (!sang.hetTrang && !khongCoNutBinhThuong)) {
          themCanhBao("dung-som-pager", `dừng ở trang ${trang}/${soTrangThayDuoc || "?"} — ${lyDoDung}`);
        }
        break;
      }

      // ③ Chờ bảng ĐỔI THẬT, không chờ mù theo thời gian — và ngân sách 6s của bản trước ĐÃ BỎ OAN trang cuối
      //    của "Sully Store" (26/08/2026). Nay: 12s cho cú bấm đầu, rồi MỘT trong hai đường trước khi bỏ cuộc.
      let doi = await choDoiTrang(chay, vanTayTruoc, nhanTrangTruoc);
      let daBamLai = false;
      if (!doi.doi) {
        // ⚠ CHỈ ĐƯỢC BẤM LẠI KHI ĐỌC ĐƯỢC NHÃN TRANG. Không đọc được nhãn (Shopee đổi markup pager) thì KHÔNG
        // có cách nào biết cú bấm đầu đã ăn hay chưa — mà bấm lại lên một cú đã ăn là NHẢY CÓC MẤT MỘT TRANG
        // trong im lặng. Lúc ấy thà chờ thêm rồi dừng + cảnh báo (đúng hành vi cũ, chỉ rộng ngân sách hơn).
        if (doi.pagerDaNhay || !nhanTrangTruoc) {
          // Nhãn trang active ĐÃ nhích (hoặc KHÔNG đọc được nhãn nào) ⇒ chỉ CHỜ THÊM một ngân sách nữa.
          nhatKyTrang.push({ trang: trang + 1,
                             ghiChu: `bảng chưa vẽ xong sau ${(CHO_DOI_TRANG_TRAN_NHIP * CHO_DOI_TRANG_NHIP_MS) / 1000}s `
                               + (nhanTrangTruoc
                                 ? `nhưng pager đã sang trang '${(doi.doc && doi.doc.trangHienTai) || "?"}' — CHỜ THÊM, `
                                   + "không bấm lại"
                                 : "và KHÔNG đọc được nhãn trang nào — CHỜ THÊM, không bấm lại (bấm lại có thể "
                                   + "nhảy cóc mất một trang)") });
          doi = await choDoiTrang(chay, vanTayTruoc, nhanTrangTruoc);
        } else {
          // Không có dấu nào cho thấy cú bấm đã ăn ⇒ BẤM LẠI đúng MỘT lần rồi mới kết luận.
          const sangLai = await chay(pageSangTrangSau, []);
          daBamLai = true;
          nhatKyTrang.push({ trang: trang + 1, bamLaiTrangSau: sangLai });
          if (sangLai && sangLai.coSan) { soTrangThayDuoc = Math.max(soTrangThayDuoc, soTrangLonNhat(sangLai.coSan)); }
          if (sangLai && sangLai.ok) {
            doi = await choDoiTrang(chay, vanTayTruoc, nhanTrangTruoc);
          } else {
            // Bấm lại cũng không ăn. KHÔNG im lặng kể cả khi nút báo `hetTrang`: bảng chưa từng đổi mà nút trang
            // sau vừa tắt là một trạng thái KHÔNG giải thích được, và cái giá của cảnh báo oan rẻ hơn hẳn cái giá
            // của một trang sản phẩm biến mất trong im lặng.
            doi = { doi: false, soNhip: 0, doc: doi.doc, pagerDaNhay: false,
                    loiBamLai: (sangLai && sangLai.loi) || "không bấm lại được nút trang sau" };
          }
        }
      }
      if (!doi.doi) {
        lyDoDung = "bảng không đổi sau khi bấm sang trang " + (trang + 1)
          + (daBamLai ? " (đã bấm lại 1 lần)" : " (đã chờ gấp đôi ngân sách)")
          + (doi.loiBamLai ? " — " + doi.loiBamLai : "");
        nhatKyTrang.push({ trang: trang + 1, loi: lyDoDung });
        themCanhBao("dung-som-bang-khong-doi", `dừng ở trang ${trang}/${soTrangThayDuoc || "?"} — ${lyDoDung}`);
        break;
      }
      trang++;
      // Chạm TRẦN = còn trang mà ta thôi đi ⇒ shop còn sản phẩm CHƯA chọn. Không được lặng lẽ `break`: combo
      // vẫn tạo được nên không bước nào đỏ, người dùng sẽ không bao giờ biết bị thiếu hàng.
      if (trang > TRAN_TRANG_MODAL) { chamTran = true; }
    }
    if (chamTran) {
      themCanhBao("cham-tran",
        `chạm trần ${TRAN_TRANG_MODAL} trang trong modal chọn sản phẩm — shop có thể còn sản phẩm CHƯA được chọn`);
    }
    // TRANG VẼ THIẾU DÒNG — lưới CUỐI CÙNG cho ca bảng khựng giữa chừng. Mọi trang trừ trang CUỐI đều phải đủ
    // `soDongChuan` dòng; trang nào ít hơn là trang đó đã bị tick khi bảng còn vẽ dở ⇒ mất hàng. Không sửa được
    // nữa ở đây (đã sang trang khác rồi), nhưng TUYỆT ĐỐI phải nói ra: mất hàng ÂM THẦM là thứ đắt nhất.
    // Trang cuối bị loại khỏi phép so vì nó ÍT HƠN CHUẨN MỘT CÁCH HỢP LỆ — shop nào cũng có một trang như vậy.
    const trangCuoiDaDi = soDongTheoTrang.length ? soDongTheoTrang[soDongTheoTrang.length - 1].trang : 0;
    for (const t of soDongTheoTrang) {
      if (t.trang !== trangCuoiDaDi && t.soDong < soDongChuan) {
        themCanhBao("trang-thieu-dong",
          `trang ${t.trang}: bảng chỉ vẽ ${t.soDong}/${soDongChuan} dòng lúc tick — sản phẩm ở phần thiếu KHÔNG `
            + "được chọn",
          `trang vẽ thiếu dòng so với chuẩn ${soDongChuan} dòng/trang`, `trang ${t.trang}`);
      }
    }
    if (!chamTran && soTrangThayDuoc > trang) {
      themCanhBao("con-trang-chua-duyet",
        `mới duyệt ${trang}/${soTrangThayDuoc} trang của modal — sản phẩm ở các trang còn lại KHÔNG được chọn`
        + (lyDoDung ? ` (${lyDoDung})` : ""));
    }

    // Xác nhận MODAL — chỉ NẠP sản phẩm vào form, CHƯA tạo combo (nút footer mới là bước 10).
    const selXn = await chay(pageXacNhanModal, []);
    let conMo = true;
    for (let i = 0; i < 30; i++) {
      await sleep(400);
      conMo = await chay(pageModalConMo, []);
      if (!conMo) { break; }
    }
    return {
      selectorDung: ".src-components---addItems--2wv99",
      truoc: { doModalKhiVuaMo: doModal },
      sau: { nhatKyTrang, soTrangDaDi: Math.min(trang, TRAN_TRANG_MODAL), soTrangThayDuoc, lyDoDung, chamTran,
             soSpDaTick, nutXacNhanModal: selXn },
      loi: (selXn && selXn.loi) || null,
      // ⚠ Kỳ vọng của cả bước là "modal ĐÃ ĐÓNG sau khi Xác nhận". CHƯA đo được "sản phẩm đã vào form" — plan
      // §5b không có selector bảng sản phẩm TRONG FORM của combo (luồng KM đếm ô %GIẢM, combo không có ô đó vì
      // mức giảm đặt ở cấp combo). Ghi rõ ra đây để lượt sau bổ sung bằng số đo thật, đừng đoán một selector.
      kyVong: "DONG", docLai: conMo ? "CON_MO" : "DONG",
      ghiChu: "Đếm dòng bằng tr[data-row-key] (bỏ tr.eds-react-table-measure-row cao 0px). Ô tick neo cứng vào "
        + "header bảng — checkbox 'Xem sản phẩm có sẵn' ở vùng actions là BỘ LỌC, tick nhầm là đổi tập sản phẩm. "
        + "Mỗi trang: CHỜ BẢNG LẮNG (2 lượt đọc bằng nhau) → tick 'chọn tất cả' → BÙ từng dòng còn sót (≤"
        + TRAN_BU_TICK + " lượt) → chỉ cảnh báo khi bù xong vẫn thiếu.",
    };
  }, tabId, "Combo");
  // KHÔNG CÒN SẢN PHẨM NÀO ĐỂ THÊM ⇒ DỪNG TẠI ĐÂY, không tạo combo trống. Nhánh này chỉ tới được khi bước 1–8
  // đều `ok` (mỗi bước hỏng đã `return` ở trên) ⇒ không còn đường nào biến một lỗi thao tác thành "shop rỗng".
  // ⚠ Phải đứng TRƯỚC `if (!b9.ok)`: đây là kết luận NGHIỆP VỤ (`boQua`), không phải bước hỏng.
  if (b9.shopRong) {
    return ketThuc({ boQua: "modal KHÔNG còn sản phẩm nào để thêm (bảng tự khai rỗng) — bỏ qua, không tạo combo" });
  }
  if (!b9.ok) { return ketThuc({ soSanPham: soSpDaTick }); }

  // 10) Bấm "Xác nhận" ở footer — ĐÂY LÀ BƯỚC TẠO THẬT.
  const b10 = await buoc(ds, "10. Xác nhận footer (TẠO THẬT)", async () => {
    const truoc = await chay(() => location.href);
    const bam = await chay(pageBamTaoThat, []);
    if (!bam || !bam.ok) {
      return { selectorDung: null, truoc, sau: truoc, loi: (bam && bam.loi) || "không bấm được nút Xác nhận",
               sauChiTiet: bam, kyVong: "DA_ROI", docLai: "CON_O_FORM" };
    }

    // Đọc lại: tạo xong thì Shopee rời trang tạo. So với URL ĐÃ CHỤP lúc form mở (bước 2) chứ không đoán một
    // chuỗi đường dẫn — luồng KM khoá cứng "/discount/create", ở đây ta chưa có số đo nào cho đường dẫn combo.
    // 120 nhịp = 60s: đêm 23/08/2026 ba shop LỚN của luồng KM bị chấm HỎNG sau 20s trong khi chương trình ĐÃ
    // được tạo — âm tính giả ở đây không vô hại, lượt sau sẽ tạo chương trình THỨ HAI cho đúng những shop đó.
    let sau = "";
    for (let i = 0; i < 120; i++) {
      await sleep(500);
      sau = await chay(() => location.href);
      if (String(sau || "") !== String(urlFormTao || "")) { break; }
    }
    const baoTrenTrang = await chay(pageDocBaoTrenTrang, []);
    const conOForm = String(sau || "") === String(urlFormTao || "");
    // ⚠ ĐẶT VÀO `loi`, KHÔNG PHẢI `ghiChu`: C# chỉ chuyển tiếp trường `loi` lên nhật ký (`DocBuocHongDau`).
    const loiOLai = conOForm
      ? "VẪN ở trang tạo combo sau 60s — Shopee nói: "
        + (baoTrenTrang.length ? baoTrenTrang.join(" | ") : "(KHÔNG có thông báo nào trên trang)")
        + ". ⚠ ĐÃ CÓ TIỀN LỆ ở luồng KM (23/08/2026): chương trình VẪN ĐƯỢC TẠO dù ở lại trang này — KIỂM trên "
        + "Shopee TRƯỚC khi chạy lại, kẻo tạo TRÙNG"
      : null;
    return {
      selectorDung: "footer button[type=submit].src-components---confirm--3svOo",
      truoc, sau: { url: sau, urlFormTao, baoTrenTrang, bam }, loi: loiOLai,
      kyVong: "DA_ROI", docLai: conOForm ? "CON_O_FORM" : "DA_ROI",
      ghiChu: "Rời trang tạo ⇒ Shopee nhận. Ở lại KHÔNG chắc là hỏng — xem baoTrenTrang.",
    };
  }, tabId, "Combo");

  return ketThuc({ soSanPham: soSpDaTick, daTao: !!b10.ok });
}

/** Lệnh `dumpCombo` — nút DÒ (chẩn đoán). Chạy ĐÚNG luồng thật (tức TẠO THẬT) rồi gửi nguyên nhật ký từng bước
 * về app, cùng khuôn gói `kmDump` của luồng KM. */
export async function doDumpCombo() {
  const kq = await chayComboChoTabDangMo(thamSoLuotDo());
  send({
    action: "pageData",
    kind: "kmDump",
    data: JSON.stringify({
      luc: new Date().toISOString(),
      luong: "combo",
      ketQua: { soSanPham: kq.soSanPham, daTao: kq.daTao, boQua: kq.boQua, canhBao: kq.canhBao,
                captcha: !!kq.captcha },
      buoc: kq.buoc,
    }, null, 2),
  });
}

/**
 * Lệnh `chayCombo` — LUỒNG THẬT: tạo combo cho shop đang mở theo tham số của form.
 * <para><b>`token` trả lại NGUYÊN VĂN</b> (C# gửi `shopId`): channel chỉ nhận gói khớp token của lượt đang chờ,
 * không có nó thì một gói tới muộn của shop TRƯỚC được tính là kết quả của shop ĐANG chạy.</para>
 * <para>Gói đi về `kind:"kmKetQua"` — cố ý CÙNG kind với luồng KM, vì phía C#
 * (`OrdersBridgeSession.ChayVongTaoChuongTrinhAsync`) dùng CHUNG một vòng lặp và chờ đúng gói đó cho cả
 * `chayKm` lẫn `chayCombo`.</para>
 */
export async function doChayCombo(thamSo, token) {
  const kq = await chayComboChoTabDangMo(thamSo || {});
  const buocHong = (kq.buoc || []).filter((b) => !b.ok).map((b) => ({ ten: b.ten, loi: b.loi || null }));
  send({
    action: "pageData",
    kind: "kmKetQua",
    token: token === undefined ? null : token,
    data: JSON.stringify({
      luc: new Date().toISOString(),
      soSanPham: kq.soSanPham,
      daTao: kq.daTao,
      boQua: kq.boQua,
      canhBao: kq.canhBao || [],
      captcha: !!kq.captcha,
      buocHong,
      buoc: kq.buoc,
    }, null, 2),
  });
}
