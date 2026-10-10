// "KM của Shop" — luồng TẠO CHƯƠNG TRÌNH KHUYẾN MÃI trên Seller Centre, bằng DOM thuần.
//
// ⚠ ĐÃ ĐỔI BẢN CHẤT so với pha 1: từ khi có bước 9, module này TẠO CHƯƠNG TRÌNH THẬT trên shop thật. Cả hai
//   lệnh (`chayKm` luồng thật và `dumpKm` nút chẩn đoán) đi CÙNG một chuỗi bước, nên nút dò cũng tạo thật.
//
// ══════════════════════════════════════════════════════════════════════════════════════════════════════════
// BA RÀNG BUỘC CỨNG CỦA MODULE NÀY — ĐỪNG NỚI:
//
//  (1) MỘT BƯỚC CỐT LÕI HỎNG ⇒ DỪNG SHOP NGAY, không đi tiếp tới bước 9. Bước 9 (`.fix-container … .confirm-btn`)
//      là nút TẠO THẬT: đi tới đó trên một trạng thái đã vỡ nghĩa là tạo chương trình sai giờ / sai % rồi báo
//      thành công. Đây là lỗi nặng nhất mà lượt phản biện 19/08/2026 tìm ra.
//
//  (2) DOM THUẦN, KHÔNG chrome.debugger, KHÔNG trusted click (người dùng chốt 18/08/2026). Bước nào không ăn
//      bằng DOM thuần thì GHI RÕ `ok:false`, TUYỆT ĐỐI không lặng lẽ đổi sang trusted click "cho nó chạy".
//      Kết quả ÂM TÍNH có giá trị NGANG dương tính — nó là căn cứ để hỏi lại người dùng.
//
//  (3) `ok` NGHĨA LÀ "ĐỌC LẠI KHỚP KỲ VỌNG", KHÔNG PHẢI "click không ném".
//      Lượt chạy thật 20:24 ngày 18/08/2026 cho thấy vì sao: hai thí nghiệm picker đều báo `ok:true` trong khi
//      ô thời gian vẫn nguyên `20:30 18-08-2026` — click trúng ô tiêu đề "CN" của bảng ngày và trúng đúng cái
//      `li` phút đã chọn sẵn, nên "đổi" mà không đổi gì. Cờ `ok` kiểu cũ là một PROOF ĐO RỖNG: đúng bất kể mã
//      làm gì, và nó suýt làm báo cáo sai lên người dùng.
//      ⇒ Nay bước nào khai `kyVong` thì khung `buoc()` TỰ so với `docLai` và tự hạ `ok` — xem `buoc()`.
// ══════════════════════════════════════════════════════════════════════════════════════════════════════════
//
// Mỗi bước trả { ten, ok, selectorDung, kyVong, docLai, truoc, sau, loi, ghiChu }; BƯỚC HỎNG DỪNG SHOP (ràng
// buộc (1)). Kết quả gửi về C# qua { action:"pageData", kind:"kmDump"|"kmKetQua", data:<json> }.

import { ctx, send } from "./core.js";
import { execInTab } from "./exec.js";
import { sleep } from "./shared/util.js";
import { buoc, pageClickTheoSelector, pageDemPhanTuHien, gomCanhBao, soTrangLonNhat, TRAN_CANH_BAO,
         docMocNgay, mocNgayLuotDo, cauLoiThamSo, NGAY_DAT_TRUOC_LUOT_DO } from "./flow-chung.js";

/// Trần số trang duyệt trong modal sản phẩm — pager có ellipsis nên không đọc trước được tổng số trang; đây là
/// chốt chặn để một pager hỏng không biến vòng lặp thành vô tận.
///
/// ⚠ SỐ ĐO THẬT (plan §8, shop `alina99.store`): **10 SP/trang** — 24 trang cho 240 sản phẩm. Bản trước ghi
/// "1000 SP / 20 dòng ⇒ 50 trang" và đặt trần 60: SAI GẤP ĐÔI, vì 20 là số thẻ `<tr>` (bảng có cột trái cố
/// định nên mỗi sản phẩm có HAI dòng), không phải số sản phẩm. Trần Shopee là 1000 SP ⇒ **100 trang**; để 105
/// cho dư 5 trang. Chạm trần KHÔNG được lặng lẽ `break` — nó nghĩa là shop còn sản phẩm CHƯA được chọn, phải
/// nổi lên thành cảnh báo trong kết quả shop.
const TRAN_TRANG_MODAL = 105;

/// Trần số lần bấm mũi tên chuyển tháng trên picker. Shopee chặn chương trình xa quá 180 ngày.
///
/// SỐ ĐO, không phải ước lượng (phản biện 25/08/2026, brute-force mọi ngày 2024→2032): khoảng 180 ngày lệch
/// **nhiều nhất 6 tháng** (ví dụ 03-01-2024 → 01-07-2024), không phải "khoảng 7 tháng" như bản cũ ghi. Trần 8
/// vẫn dư 2, và nó chặn một nút mũi tên hỏng biến vòng thành vô tận.
const TRAN_CHUYEN_THANG = 8;

/// Số nhịp (×500ms) chờ modal "Chọn Sản Phẩm" mở và bảng lắng. Hết nhịp thì KẾT LUẬN LÀ HỎNG chứ không phải
/// "shop rỗng" (xem bước 6).
/// <para>Nâng 40 → 90 nhịp (20s → 45s) đêm 23/08/2026: chưa loại được khả năng modal chỉ mở CHẬM. Chờ thừa 25
/// giây cho một shop hỏng là rẻ; bỏ oan một shop vì thiếu 3 giây thì phải chờ tới đêm sau mới biết. Câu lỗi nay
/// đếm riêng từng neo nên lượt hỏng kế sẽ tự nói là "chưa mở" hay "mở rồi mà đổi markup".</para>
const CHO_MODAL_NHIP = 90;

// Tab đang thao tác: sau `openShopDetail` shop nằm ở tab RIÊNG. Không lùi về tab picker (luật `orderTabIdStrict`).
function tabShop() {
  return ctx.shopTabId;
}

// ── Hàm chạy TRONG TRANG ────────────────────────────────────────────────────────────────────────────────────
// LƯU Ý sống còn (xem exec.js): mỗi page-func được chrome.scripting serialize ĐỘC LẬP, KHÔNG kèm hàm nào ngoài
// nó. Mọi helper phải viết LẶP LẠI bên trong từng hàm — đừng "dọn cho gọn" bằng cách rút ra ngoài.

/**
 * Chụp NGUYÊN VĂN một dòng sản phẩm trong bảng của FORM + đếm ô tick theo NHIỀU cách.
 * Vì sao cần: bản trước chỉ đếm `input[type="checkbox"]` nên ra 0 và tôi kết luận "bảng không có checkbox" —
 * nhưng EDS của Shopee có thể vẽ ô tick bằng span/i chứ không phải input thật. Đếm hụt ⇒ kết luận sai.
 */
function pageDumpDongSanPham() {
  const gon = (s, n) => (s || "").replace(/\s+/g, " ").trim().slice(0, n || 40);
  const batch = document.querySelector(".detail-batch");
  const ngoaiBatch = (el) => !batch || !batch.contains(el);
  const tatCaSuffix = Array.from(document.querySelectorAll(".eds-input__suffix"));
  const oGiam = tatCaSuffix.filter((s) => (s.textContent || "").indexOf("%GIẢM") >= 0).filter(ngoaiBatch);

  const dem = {
    inputCheckbox: document.querySelectorAll('input[type="checkbox"]').length,
    edsCheckbox: document.querySelectorAll(".eds-checkbox").length,
    edsCheckboxNgoaiBatch: Array.from(document.querySelectorAll(".eds-checkbox")).filter(ngoaiBatch).length,
    roleCheckbox: document.querySelectorAll('[role="checkbox"]').length,
    edsTable: document.querySelectorAll(".eds-table").length,
    thead: document.querySelectorAll("thead").length,
    oGiamNgoaiBatch: oGiam.length,
  };
  if (!oGiam.length) return { loi: "không thấy ô %GIẢM nào ngoài khối Thiết lập hàng loạt", dem };

  // Leo lên từ ô %GIẢM đầu tiên để tìm khối "một dòng sản phẩm".
  let el = oGiam[0];
  const duongLen = [];
  let dong = el;
  for (let i = 0; i < 9 && dong.parentElement; i++) {
    dong = dong.parentElement;
    duongLen.push(dong.tagName.toLowerCase() + (dong.className ? "." + gon(String(dong.className), 50).split(" ").join(".") : ""));
  }
  const meBang = oGiam[0].closest(".eds-table") || oGiam[0].closest("table") || null;
  return {
    dem,
    duongLen,
    htmlDong: gon(dong.outerHTML, 2600),
    coTickTrongDong: {
      input: dong.querySelectorAll('input[type="checkbox"]').length,
      eds: dong.querySelectorAll(".eds-checkbox").length,
    },
    meBang: meBang ? gon(meBang.className, 80) : null,
    htmlDauBang: meBang ? gon(meBang.outerHTML, 1800) : null,
  };
}

/** Text (gọn khoảng trắng) của selector đầu tiên khớp. */
function pageDocText(sel) {
  let el = null;
  try { el = document.querySelector(sel); } catch (e) { return null; }
  return el ? (el.textContent || "").replace(/\s+/g, " ").trim() : null;
}

/**
 * Gõ vào input theo KIỂU NGƯỜI: focus → gõ từng ký tự, mỗi ký tự bắn `keydown/input/keyup` với nhịp ngẫu
 * nhiên, thỉnh thoảng nghỉ lâu hơn — thay vì ốp cả chuỗi một phát rồi bắn đúng một sự kiện `input`.
 *
 * Vì sao: người dùng yêu cầu 19/08 01:5x ("cho like human chút, đừng ốp phát tiêu đề luôn"). Gán một phát là
 * dấu hiệu tự động rõ nhất trên một ô text dài 23–150 ký tự — trang đã bật `verify/traffic/error` hai lần
 * đêm 18/08, không có lý do gì để lại thêm dấu vết dễ thấy như vậy.
 *
 * `nhipMs`: nhịp cơ sở giữa hai ký tự; nhịp thật = nhipMs/2 … nhipMs*1.5, thêm 1 nhịp nghỉ dài ngẫu nhiên
 * mỗi ~8–14 ký tự (người gõ hay khựng). Chuỗi 23 ký tự với nhipMs=70 tốn khoảng 2 giây — chấp nhận được cho
 * 12 shop.
 */
async function pageGoNhuNguoi(sel, giaTri, selDem, nhipMs) {
  const el = document.querySelector(sel);
  if (!el) { return { daGan: false, dem: null, loi: "không thấy input " + sel }; }
  const nhip = nhipMs || 70;
  const ngu = (ms) => new Promise((r) => setTimeout(r, ms));
  const datValue = (v) => {
    try {
      const desc = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(el), "value");
      if (desc && desc.set) { desc.set.call(el, v); return; }
    } catch (e) { /* rơi xuống gán thẳng */ }
    el.value = v;
  };

  el.focus();
  el.dispatchEvent(new Event("focus", { bubbles: true }));
  datValue("");
  el.dispatchEvent(new Event("input", { bubbles: true }));

  const chuoi = String(giaTri);
  let keTiepNghi = 8 + Math.floor(Math.random() * 7);
  for (let i = 0; i < chuoi.length; i++) {
    const ky = chuoi[i];
    el.dispatchEvent(new KeyboardEvent("keydown", { key: ky, bubbles: true }));
    datValue(chuoi.slice(0, i + 1));
    el.dispatchEvent(new Event("input", { bubbles: true }));
    el.dispatchEvent(new KeyboardEvent("keyup", { key: ky, bubbles: true }));
    let cho = nhip / 2 + Math.random() * nhip;
    if (i >= keTiepNghi) { cho += 180 + Math.random() * 320; keTiepNghi = i + 8 + Math.floor(Math.random() * 7); }
    await ngu(cho);
  }
  el.dispatchEvent(new Event("change", { bubbles: true }));
  el.blur();
  el.dispatchEvent(new Event("blur", { bubbles: true }));

  const dem = selDem ? (document.querySelector(selDem) || {}).textContent : null;
  return { daGan: true, dem: dem ? dem.replace(/\s+/g, " ").trim() : null, giaTriDoc: el.value, soKyTu: chuoi.length };
}

/** Text ô hiển thị của picker thứ chiSo. */
function pageDocOPicker(chiSo) {
  const os = Array.from(document.querySelectorAll(".eds-selector__inner"));
  return os[chiSo] ? (os[chiSo].textContent || "").replace(/\s+/g, " ").trim() : null;
}

/** Mở picker thứ chiSo (click ô hiển thị). */
function pageMoPicker(chiSo) {
  const os = Array.from(document.querySelectorAll(".eds-selector__inner"));
  if (!os[chiSo]) { return { ok: false, loi: "không thấy ô picker thứ " + chiSo + " (có " + os.length + ")" }; }
  os[chiSo].click();
  return { ok: true, textTruoc: (os[chiSo].textContent || "").replace(/\s+/g, " ").trim() };
}

/**
 * Page-func CÀI LUẬT "ô ngày này có chọn được không" lên `window._edsONgayHopLe`.
 *
 * ⚠ THÂN HÀM NÀY LÀ **NƠI DUY NHẤT** CHỨA LUẬT — đừng chép luật vào page-func nào khác. Page-func được
 * chrome.scripting serialize ĐỘC LẬP (xem exec.js) nên không gọi thẳng hàm ngoài được; đường duy nhất để
 * MỘT luật dùng chung cho nhiều page-func là cài lên `window` rồi gọi bare `_edsONgayHopLe(...)`, đúng cách
 * `_na`/`_provCore` đang làm. Nhờ vậy test C# lái được CHÍNH luật này (Jint chạy thân hàm với `window` giả)
 * thay vì chấm một bản chép.
 *
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 * VÌ SAO CÓ HÀM NÀY — LỖI GIẾT 12/12 SHOP ĐÊM 27/08/2026:
 *
 * Bản cũ lọc bằng `/disabled/i.test(className)` — khớp CHUỖI CON, nên nó loại nhầm cả `disabled-next` và
 * `disabled-prev`. Hai class đó KHÔNG có nghĩa "ô này bị vô hiệu": chúng đánh dấu "ô KẾ BÊN bị vô hiệu"
 * (EDS dùng để bo góc dải chọn được). Hệ quả: ô ở BIÊN của dải LUÔN mang một trong hai ⇒ LUÔN bị loại oan.
 *
 * DOM thật (người dùng dump 27/08):
 *   class="eds-date-table__cell normal current selected disabled-next"   ← HỢP LỆ, ô hôm nay
 *   class="eds-date-table__cell out-of-month is-empty disabled-prev"     ← không hợp lệ, nhưng vì out-of-month
 *
 * Lượt `logs/km-dom-20260827-091851.json`: bước 5 muốn ngày 23 (23-02-2027, ĐÚNG mốc 180 ngày ⇒ ô CUỐI dải
 * ⇒ mang `disabled-next`) và báo "không thấy ô ngày '23'". 180 ngày là HỢP LỆ — người dùng chọn tay được, và
 * form Shopee ghi rõ "không được quá 180 ngày". Đối chứng: `flow-combo.js` dùng `classList.contains("disabled")`
 * (khớp CHÍNH XÁC) và đặt được đúng 23-02-2027 trên 9/12 shop đêm 26/08. Khác đúng một phép so chuỗi.
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 *
 * Luật khớp theo TOKEN (tách khoảng trắng), không bao giờ theo chuỗi con:
 *  · `disabled`      — ô thật sự bị vô hiệu (ngoài khoảng Shopee cho phép) ⇒ LOẠI.
 *  · `out-of-range`  — ngày của tháng liền kề trên picker react (`flow-combo.js`) ⇒ LOẠI (bấm là sai tháng).
 *  · `out-of-month` / `prev-month` / `next-month` / `other-month` — ngoài tháng đang hiện ⇒ LOẠI.
 *  · `is-empty`      — ô đệm không mang ngày nào ⇒ LOẠI.
 *  · `disabled-next` / `disabled-prev` — CHỈ là bo góc dải ⇒ **GIỮ**.
 */
export function pageCaiLuatONgayEds() {
  window._edsONgayHopLe = function (className) {
    const token = String(className || "").split(/\s+/).filter(Boolean);
    const co = (t) => token.indexOf(t) >= 0;
    if (co("disabled")) return false;
    if (co("out-of-range")) return false;
    if (co("out-of-month") || co("prev-month") || co("next-month") || co("other-month")) return false;
    if (co("is-empty")) return false;
    return true;
  };
  return { ok: true };
}

/**
 * Page-func CÀI LUẬT **"trong header EDS, mũi tên nào là mũi tên THÁNG"** lên `window._edsChonMuiTenThang`.
 *
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 * VÌ SAO CÓ HÀM NÀY — LỖI LÙI 12 THÁNG, đo thật trên lịch Flash Sale ngày 27/08/2026.
 *
 * Header picker EDS có **BỐN** mũi tên, chia hai cặp **DÙNG CHUNG CLASS**, phân biệt nhau CHỈ bằng số path
 * trong svg (2 path = năm, 1 path = tháng) và bằng VỊ TRÍ so với cụm nhãn:
 *
 *     ‹‹  eds-picker-header__prev     ← LÙI MỘT NĂM   (svg 2 path)
 *     ‹   eds-picker-header__prev     ← lùi 1 tháng   (svg 1 path)   ┐
 *         eds-picker-header__label      "Tháng 8"                    │ hai mũi tên THÁNG
 *         eds-picker-header__label      "2026"                       │ nằm SÁT cụm nhãn
 *         eds-picker-header__label      "2020 – 2029" (display:none) │
 *     ›   eds-picker-header__next     ← tiến 1 tháng  (svg 1 path)   ┘
 *     ››  eds-picker-header__next     ← TIẾN MỘT NĂM  (svg 2 path)
 *
 * Bản cũ lấy phần tử ĐẦU TIÊN khớp class. Hệ quả BẤT ĐỐI XỨNG, và chính sự bất đối xứng đó giấu lỗi rất lâu:
 *  · `__next` — cái đầu tiên TÌNH CỜ đúng là mũi tên THÁNG ⇒ chiều tiến chạy tốt, không ai nghi ngờ;
 *  · `__prev` — cái đầu tiên là mũi tên NĂM ⇒ mỗi cú "lùi một tháng" thật ra lùi 12 tháng.
 *
 * Ca thật: lịch đang ở Tháng 9/2026, bấm lùi một cái, đáp xuống **Tháng 9/2025**.
 *
 * ⇒ LUẬT: **mũi tên THÁNG là cái GẦN CỤM NHÃN NHẤT.** Trong nhóm cùng chiều đã lọc sẵn:
 *    nhóm đứng TRƯỚC nhãn (lùi) ⇒ lấy cái CUỐI; nhóm đứng SAU nhãn (tiến) ⇒ lấy cái ĐẦU.
 * Neo theo vị trí tương đối với nhãn bền hơn "đầu/cuối thuần" vì nó không giả định header có đủ 4 mũi tên —
 * picker chỉ có 2 mũi tên vẫn ra đúng.
 *
 * ⚠ ĐỪNG lọc bằng `/year|double|super/` rồi tin là đủ: ở header này hai cặp DÙNG CHUNG CLASS, không có chữ
 * `year`/`super` nào để mà lọc. Bộ lọc đó vẫn giữ (nó chặn các bản EDS khác có đặt tên), nhưng nó KHÔNG
 * phải cửa quyết định.
 *
 * ⚠ `export` để `flow-flashsale.js` dùng lại — hai picker cùng là header EDS với `eds-picker-header__label`,
 * nên đây là MỘT luật cho cả hai. Chép bản thứ hai là đúng cái bẫy đã trả giá ở luật ô ngày.
 * ══════════════════════════════════════════════════════════════════════════════════════════════════════
 *
 * @param {Array<{chiSo:number, truocNhan:boolean}>} dsMuiTen Các mũi tên CÙNG CHIỀU, theo ĐÚNG thứ tự DOM.
 * @param {boolean} tien true = chiều tiến (nhóm sau nhãn), false = chiều lùi (nhóm trước nhãn).
 * @returns {number} chỉ số của mũi tên THÁNG, hoặc -1.
 */
export function pageCaiLuatMuiTenThang() {
  window._edsChonMuiTenThang = function (dsMuiTen, tien) {
    const ds = Array.isArray(dsMuiTen) ? dsMuiTen.filter((x) => x && typeof x.chiSo === "number") : [];
    if (ds.length === 0) { return -1; }
    // Nhóm đúng phía so với cụm nhãn. Không xác định được nhãn ⇒ dùng cả danh sách: nó đã được lọc CÙNG
    // CHIỀU từ trước, nên "cái cuối của nhóm lùi / cái đầu của nhóm tiến" vẫn ra đúng mũi tên tháng.
    const cungPhia = ds.filter((x) => (tien ? x.truocNhan !== true : x.truocNhan === true));
    const nhom = cungPhia.length > 0 ? cungPhia : ds;
    return tien ? nhom[0].chiSo : nhom[nhom.length - 1].chiSo;
  };
  return { ok: true };
}

/**
 * Dump picker ĐANG MỞ — mọi truy vấn SCOPE vào popper đang hiện.
 * Vì sao (lượt 20:24): cả hai picker nằm sẵn trong DOM, `document.querySelectorAll` gom 98 ô ngày và 4 cột
 * giờ/phút của CẢ HAI picker + popper ẩn ⇒ mọi cú click bắn nhầm chỗ.
 */
function pageDumpPickerDangMo() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.offsetParent !== null && getComputedStyle(e).display !== "none";
  const ds = Array.from(document.querySelectorAll(".eds-date-picker__picker, .eds-picker-panel, .eds-popper"));
  const p = ds.find(hien) || null;
  if (!p) {
    return { coPopperHien: false, soPopperTrongDom: ds.length };
  }
  const rows = p.querySelector(".eds-date-table__rows");
  const cells = Array.from((rows || p).querySelectorAll(".eds-date-table__cell"));
  const lists = Array.from(p.querySelectorAll(".eds-time-spinner__list"));
  return {
    coPopperHien: true,
    soPopperTrongDom: ds.length,
    classPopperHien: p.className,
    coKhoiRows: !!rows,
    soONgayTrongRows: cells.length,
    // ⚠ ĐẾM THEO TOKEN, đừng quay lại `/disabled/i.test(...)`: chuỗi con gộp `disabled-next`/`disabled-prev`
    // (chỉ là bo góc dải) vào cùng một con số với ô THẬT SỰ bị khoá, nên số này từng nói dối đúng chỗ cần
    // nó nói thật. Tách hẳn hai con số — xem {@link pageCaiLuatONgayEds}.
    soONgayDisabled: cells.filter((c) => c.classList.contains("disabled")).length,
    soONgayBienDai: cells.filter((c) => c.classList.contains("disabled-next") || c.classList.contains("disabled-prev")).length,
    soONgayNgoaiThang: cells.filter((c) => ["out-of-month", "prev-month", "next-month", "other-month"]
      .some((t) => c.classList.contains(t))).length,
    viDuONgay: cells.slice(0, 5).map((c) => ({ text: gon(c.textContent), cls: c.className })),
    soCotGioPhut: lists.length,
    soLiMoiCot: lists.map((l) => l.querySelectorAll("li").length),
    soLiDisabledMoiCot: lists.map((l) => Array.from(l.querySelectorAll("li")).filter((x) => x.classList.contains("disabled")).length),
    liDangChonMoiCot: lists.map((l) => {
      const s = l.querySelector("li.selected, li[class*='selected']");
      return s ? gon(s.textContent) : null;
    }),
  };
}

/**
 * Click ô ngày theo ĐÚNG SỐ NGÀY, chỉ trong `.eds-date-table__rows` của popper đang hiện.
 * Bỏ ô KHOÁ và ô ngoài tháng — lượt 20:24 click trúng ô tiêu đề thứ ("CN") vì lấy "ô hợp lệ đầu tiên".
 *
 * ⚠ Luật "ô nào chọn được" KHÔNG nằm ở đây: nó nằm ở {@link pageCaiLuatONgayEds} và được cài lên
 * `window._edsONgayHopLe` TRƯỚC khi gọi hàm này. Đừng chép luật vào đây — bản chép sẽ trôi lệch, mà đây
 * đúng là chỗ vừa trôi lệch và trả giá 12/12 shop đêm 27/08/2026.
 *
 * Khi KHÔNG tìm thấy, câu trả về mang đủ thứ để chẩn đoán ngay từ nhật ký, KHỎI phải chạy nút dò:
 *  · `coSan` — **ĐỦ** danh sách ngày chọn được (tối đa 31 phần tử). Bản cũ `slice(0, 12)` cắt còn 12 và
 *    danh sách `["1".."12"]` đã làm phiên chẩn đoán 27/08 đọc thành "Shopee chỉ cho tới ngày 12", suýt đi
 *    sửa nhầm hẳn một cái trần không tồn tại.
 *  · `clsOCungSo` — class THẬT của ô mang đúng số ngày cần tìm, kể cả ô đã bị loại. Chính nó nói ngay ra
 *    `disabled-next`.
 */
function pageClickNgayTheoSo(soNgay) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.offsetParent !== null && getComputedStyle(e).display !== "none";
  const p = Array.from(document.querySelectorAll(".eds-date-picker__picker, .eds-picker-panel, .eds-popper")).find(hien);
  if (!p) { return { ok: false, loi: "không có popper nào đang hiện" }; }
  const rows = p.querySelector(".eds-date-table__rows");
  if (!rows) { return { ok: false, loi: "không thấy .eds-date-table__rows (chỉ có header?)" }; }
  // Chưa cài luật ⇒ HỎNG TO TIẾNG. Đừng thay bằng một bản luật dự phòng tại chỗ: bản dự phòng chính là bản
  // chép thứ hai, và nó sẽ âm thầm chạy đúng cái sai cũ.
  if (typeof _edsONgayHopLe !== "function") {
    return { ok: false, loi: "chưa cài window._edsONgayHopLe (thiếu bước chạy pageCaiLuatONgayEds)" };
  }
  const tatCa = Array.from(rows.querySelectorAll(".eds-date-table__cell"));
  const cells = tatCa.filter((c) => _edsONgayHopLe(c.className));
  const can = String(soNgay);
  const hit = cells.find((c) => gon(c.textContent) === can);
  if (!hit) {
    const cungSo = tatCa.filter((c) => gon(c.textContent) === can).map((c) => String(c.className || ""));
    return {
      ok: false,
      loi: "không thấy ô ngày '" + can + "' còn chọn được (tổng " + tatCa.length + " ô, "
        + cells.length + " chọn được)",
      coSan: cells.map((c) => gon(c.textContent)),
      clsOCungSo: cungSo,
    };
  }
  hit.click();
  return { ok: true, text: gon(hit.textContent), cls: String(hit.className || "") };
}

/** Click <li> giờ/phút theo nhãn, SCOPE trong popper đang hiện. Báo rõ nếu li đó vốn ĐÃ được chọn sẵn. */
function pageClickLiThoiGian(chiSoCot, nhan) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.offsetParent !== null && getComputedStyle(e).display !== "none";
  const p = Array.from(document.querySelectorAll(".eds-date-picker__picker, .eds-picker-panel, .eds-popper")).find(hien);
  if (!p) { return { ok: false, loi: "không có popper nào đang hiện" }; }
  const lists = Array.from(p.querySelectorAll(".eds-time-spinner__list"));
  const l = lists[chiSoCot];
  if (!l) { return { ok: false, loi: "không thấy cột " + chiSoCot + " (popper có " + lists.length + " cột)" }; }
  const lis = Array.from(l.querySelectorAll("li"));
  const hit = lis.find((x) => gon(x.textContent) === nhan);
  if (!hit) { return { ok: false, loi: "không thấy li '" + nhan + "'", coSan: lis.slice(0, 8).map((x) => gon(x.textContent)) }; }
  // Khớp TOKEN, không phải chuỗi con — cùng lý do với ô ngày ({@link pageCaiLuatONgayEds}): một class nào đó
  // chứa chuỗi "disabled" mà không có nghĩa "bị vô hiệu" là loại oan đúng cái li cần bấm.
  if (hit.classList.contains("disabled")) { return { ok: false, loi: "li '" + nhan + "' đang disabled" }; }
  const daChonSan = /selected/i.test(hit.className);
  hit.scrollIntoView({ block: "center" });
  hit.click();
  return { ok: true, cls: hit.className, daChonSan };
}

/** Bấm "Xác nhận" TRONG popper đang hiện (không phải nút Xác nhận nào khác trên trang). */
function pageXacNhanPicker() {
  const na = (s) => (s || "").replace(/\s+/g, " ").trim().toLowerCase();
  const hien = (e) => !!e && e.offsetParent !== null && getComputedStyle(e).display !== "none";
  const p = Array.from(document.querySelectorAll(".eds-date-picker__picker, .eds-picker-panel, .eds-popper")).find(hien);
  if (!p) { return { ok: false, loi: "không có popper nào đang hiện" }; }
  const nut = Array.from(p.querySelectorAll("button")).find((b) => na(b.textContent).indexOf("xác nhận") >= 0);
  if (!nut) { return { ok: false, loi: "không thấy nút Xác nhận trong popper", coSan: Array.from(p.querySelectorAll("button")).map((b) => na(b.textContent)).slice(0, 6) }; }
  nut.click();
  return { ok: true };
}

/**
 * Bấm "Xác nhận" của MODAL chọn sản phẩm — SCOPE CỨNG trong hộp modal.
 *
 * ĐỪNG thay bằng querySelectorAll("button") lọc theo chữ. Trên trang này còn MỘT nút "Xác nhận" nữa ở THANH ĐÁY
 * (.fix-container.fixed-bottom .confirm-btn) và nó là nút TẠO CHƯƠNG TRÌNH THẬT. Một lượt tìm toàn trang mà
 * trượt modal là bấm trúng nó — tạo chương trình khuyến mãi thật trên shop thật của người dùng.
 * Không thấy modal ⇒ TRẢ LỖI, tuyệt đối không nới phạm vi tìm ra ngoài.
 */
function pageXacNhanModal() {
  const na = (s) => (s || "").replace(/\s+/g, " ").trim().toLowerCase();
  // ⚠ Trang có NHIỀU .eds-modal nằm sẵn/ẨN (hộp hướng dẫn "xem chi tiết / quay lại trang danh sách"…).
  // Lấy cái ĐẦU TIÊN là chộp nhầm modal ẩn rồi kết luận "không thấy nút Xác nhận" — ĐÃ XẢY RA THẬT lúc
  // 22:48 18/08/2026 trong khi modal thật đang mở với "Đã chọn 240/1000 sản phẩm". PHẢI lấy cái ĐANG HIỆN.
  // ⚠ NEO THEO NỘI DUNG, đừng quay lại "modal đang hiện". Trang có modal hướng dẫn (hai nút "xem chi tiết" /
  // "quay lại trang danh sách") cũng tính là hiện ⇒ hai lượt 22:48 và 00:44 đều chộp nhầm nó rồi kết luận
  // "không thấy nút Xác nhận", trong khi modal sản phẩm đang mở với 240/1000 đã chọn. Dấu hiệu CHẮC CHẮN của
  // modal chọn sản phẩm là dòng đếm .select-tips ("Đã chọn N/1000 sản phẩm").
  const hienModal = (e) => !!e && e.offsetParent !== null && getComputedStyle(e).display !== "none";
  const ungVienModal = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  const modal = ungVienModal.find((m) => m.querySelector(".select-tips, .product-selector-modal, .product-modal-header") && hienModal(m))
             || ungVienModal.find((m) => m.querySelector(".select-tips, .product-selector-modal, .product-modal-header"))
             || null;
  if (!modal) { return { ok: false, loi: "không thấy hộp modal — KHÔNG tìm nút Xác nhận ngoài modal" }; }
  if (modal.closest(".fix-container")) { return { ok: false, loi: "modal nằm trong thanh đáy — từ chối bấm" }; }
  // Khớp ĐÚNG BẰNG "xác nhận" và ưu tiên đúng cụm nút footer của modal — "chứa" thì trúng cả nút khác.
  const trongFooter = Array.from(modal.querySelectorAll(".eds-modal__footer-buttons button"));
  const ungVien = trongFooter.length > 0 ? trongFooter : Array.from(modal.querySelectorAll("button"));
  const nut = ungVien.find((b) => na(b.textContent) === "xác nhận");
  if (nut && nut.closest(".fix-container")) { return { ok: false, loi: "nút Xác nhận tìm được nằm ở THANH ĐÁY — từ chối bấm" }; }
  if (!nut) {
    return {
      ok: false,
      loi: "không thấy nút Xác nhận TRONG modal",
      coSan: Array.from(modal.querySelectorAll("button")).map((b) => na(b.textContent)).slice(0, 6),
    };
  }
  nut.click();
  return { ok: true, cls: nut.className };
}

/** Dump modal chọn sản phẩm. */
function pageDumpModalSanPham() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  // ⚠ Trang có NHIỀU .eds-modal nằm sẵn/ẨN (hộp hướng dẫn "xem chi tiết / quay lại trang danh sách"…).
  // Lấy cái ĐẦU TIÊN là chộp nhầm modal ẩn rồi kết luận "không thấy nút Xác nhận" — ĐÃ XẢY RA THẬT lúc
  // 22:48 18/08/2026 trong khi modal thật đang mở với "Đã chọn 240/1000 sản phẩm". PHẢI lấy cái ĐANG HIỆN.
  // ⚠ NEO THEO NỘI DUNG, đừng quay lại "modal đang hiện". Trang có modal hướng dẫn (hai nút "xem chi tiết" /
  // "quay lại trang danh sách") cũng tính là hiện ⇒ hai lượt 22:48 và 00:44 đều chộp nhầm nó rồi kết luận
  // "không thấy nút Xác nhận", trong khi modal sản phẩm đang mở với 240/1000 đã chọn. Dấu hiệu CHẮC CHẮN của
  // modal chọn sản phẩm là dòng đếm .select-tips ("Đã chọn N/1000 sản phẩm").
  const hienModal = (e) => !!e && e.offsetParent !== null && getComputedStyle(e).display !== "none";
  const ungVienModal = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  const modal = ungVienModal.find((m) => m.querySelector(".select-tips, .product-selector-modal, .product-modal-header") && hienModal(m))
             || ungVienModal.find((m) => m.querySelector(".select-tips, .product-selector-modal, .product-modal-header"))
             || null;
  const thead = document.querySelector("thead");
  const checkAll = thead ? thead.querySelector("input.eds-checkbox__input, input[type=checkbox]") : null;
  const rows = Array.from(document.querySelectorAll("tbody tr"));
  const pages = Array.from(document.querySelectorAll("ul.eds-pager__pages li"));
  const daChon = Array.from(document.querySelectorAll("div,span,p"))
    .filter((e) => /đã chọn/i.test(e.textContent || "") && (e.textContent || "").length < 80)
    .map((e) => ({ cls: e.className, text: gon(e.textContent) }))
    .slice(0, 3);
  return {
    coModal: !!modal,
    coCheckAll: !!checkAll,
    checkAllDangTick: checkAll ? !!checkAll.checked : null,
    soDongTbody: rows.length,
    soTrangHienThi: pages.length,
    nhanCacTrang: pages.map((p) => gon(p.textContent)),
    dongDaChon: daChon,
    vanTayBang: rows.slice(0, 2).map((r) => gon(r.textContent).slice(0, 60)),
  };
}

/**
 * Đọc ô đếm "Đã chọn X/1000 sản phẩm" của MODAL chọn sản phẩm ĐANG MỞ (neo `.select-tips`).
 *
 * Đây là NGUỒN của phép đọc-lại "tick hết trang" ở bước 6, nên phải đọc ĐÚNG ô đếm của modal. Bản trước quét
 * `div,span,p` toàn trang tìm chữ "đã chọn": trên trang tạo chương trình còn khối "0 sản phẩm đã chọn" của
 * khối Thiết lập hàng loạt, tức hoàn toàn có thể đọc trúng một con số chẳng liên quan tới modal.
 * ĐỎ KHI: modal không mở, hoặc modal mất `.select-tips` — cả hai trả `null` và bước 6 ghi CẢNH BÁO "không đọc
 * được số đã chọn" thay vì lặng lẽ coi như đã tick.
 */
function pageDocDaChon() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.offsetParent !== null && getComputedStyle(e).display !== "none";
  const ungVienModal = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  // ⚠ BA NEO, KHÔNG PHẢI MỘT — và `hien()` chỉ là phép lọc ƯU TIÊN, không có quyền phủ quyết.
  //
  // `.select-tips` ("Đã chọn N/1000") là dòng đếm ở footer, và nó CHỈ xuất hiện SAU KHI đã chọn được sản
  // phẩm — modal vừa mở, chưa tick gì, thì dòng đó chưa render. Neo mỗi vào nó là vòng luẩn quẩn: muốn tìm
  // modal phải có dòng đếm, muốn có dòng đếm phải tick, muốn tick phải tìm được modal.
  // Đo thật lúc 03:26 23/08/2026: 15 hộp `.eds-modal*` trong DOM, KHÔNG hộp nào có `.select-tips`, trong khi
  // modal đang mở rành rành trên màn hình. `.product-selector-modal` và `.product-modal-header` là của riêng
  // modal này và có mặt NGAY từ lúc mở.
  //
  // `hien()` cũng không được phép loại: modal EDS nằm trong lớp phủ `position: fixed`, mà `offsetParent` của
  // phần tử fixed là `null` ⇒ hộp đang mở vẫn bị chấm là ẩn.
  const modal = ungVienModal.find((m) => m.querySelector(".select-tips, .product-selector-modal, .product-modal-header") && hien(m))
             || ungVienModal.find((m) => m.querySelector(".select-tips, .product-selector-modal, .product-modal-header"))
             || null;
  if (!modal) { return null; }
  const tips = modal.querySelector(".select-tips");
  return tips ? gon(tips.textContent) : null;
}

/**
 * Click checkbox check-all ở thead CỦA MODAL chọn sản phẩm (đúng input Vue gắn handler, không phải span bọc).
 *
 * ⚠ Neo vào modal `.select-tips` như {@link pageDoModalSanPhamDangMo}. Bản trước lấy `document.querySelector("thead")`
 * TOÀN TRANG — trang tạo chương trình có bảng riêng nằm sau modal, nên cái `thead` đầu tiên trong DOM hoàn toàn
 * có thể là của bảng ngoài: tick trúng một checkbox không liên quan rồi báo `ok`, còn 10 sản phẩm của trang đang
 * mở thì không được chọn. Không thấy modal ⇒ TRẢ LỖI, tuyệt đối không nới phạm vi tìm ra ngoài modal.
 */
function pageClickCheckAll() {
  const hien = (e) => !!e && e.offsetParent !== null && getComputedStyle(e).display !== "none";
  const ungVienModal = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  // ⚠ BA NEO, KHÔNG PHẢI MỘT — và `hien()` chỉ là phép lọc ƯU TIÊN, không có quyền phủ quyết.
  //
  // `.select-tips` ("Đã chọn N/1000") là dòng đếm ở footer, và nó CHỈ xuất hiện SAU KHI đã chọn được sản
  // phẩm — modal vừa mở, chưa tick gì, thì dòng đó chưa render. Neo mỗi vào nó là vòng luẩn quẩn: muốn tìm
  // modal phải có dòng đếm, muốn có dòng đếm phải tick, muốn tick phải tìm được modal.
  // Đo thật lúc 03:26 23/08/2026: 15 hộp `.eds-modal*` trong DOM, KHÔNG hộp nào có `.select-tips`, trong khi
  // modal đang mở rành rành trên màn hình. `.product-selector-modal` và `.product-modal-header` là của riêng
  // modal này và có mặt NGAY từ lúc mở.
  //
  // `hien()` cũng không được phép loại: modal EDS nằm trong lớp phủ `position: fixed`, mà `offsetParent` của
  // phần tử fixed là `null` ⇒ hộp đang mở vẫn bị chấm là ẩn.
  const modal = ungVienModal.find((m) => m.querySelector(".select-tips, .product-selector-modal, .product-modal-header") && hien(m))
             || ungVienModal.find((m) => m.querySelector(".select-tips, .product-selector-modal, .product-modal-header"))
             || null;
  if (!modal) { return { ok: false, loi: "không thấy modal chọn sản phẩm (neo .select-tips) — KHÔNG tick ngoài modal" }; }
  const thead = modal.querySelector("thead");
  if (!thead) { return { ok: false, loi: "không thấy thead trong modal chọn sản phẩm" }; }
  const inp = thead.querySelector("input.eds-checkbox__input") || thead.querySelector("input[type=checkbox]");
  if (!inp) { return { ok: false, loi: "không thấy input check-all trong thead của modal" }; }
  if (inp.checked) { return { ok: true, daTickSan: true, dangTick: true }; }
  inp.click();
  return { ok: true, daTickSan: false, dangTick: !!inp.checked };
}

/**
 * Click <li> phân trang theo nhãn, TRONG MODAL chọn sản phẩm.
 *
 * ⚠ Neo modal `.select-tips` — lý do y hệt {@link pageClickCheckAll}: trang "Tạo chương trình" có bảng sản phẩm
 * RIÊNG (và pager riêng) nằm sau modal, nên quét `ul.eds-pager__pages li` toàn trang có thể bấm trúng pager của
 * bảng NGOÀI: modal đứng nguyên trang 1, còn ta tưởng đã lật. Không thấy modal ⇒ TRẢ LỖI, không nới ra ngoài.
 *
 * Ba cờ cho ba ca RẤT khác nhau — vòng lật trang ở bước 6 dựa vào đúng chúng để quyết có đẻ cảnh báo không:
 *  · `khongThayTrang: true` — pager CÒN nhãn nhưng không có nhãn kế ⇒ BÌNH THƯỜNG, đã tới trang cuối.
 *  · `pagerVang: true` — modal còn đó mà KHÔNG còn `ul.eds-pager__pages` nào ⇒ hoặc shop ít trang không render
 *    pager (bình thường), hoặc pager VỪA BIẾN MẤT giữa chừng (hỏng hạ tầng). Vòng gọi phân xử bằng câu hỏi
 *    "lúc MỞ modal có thấy pager không" (`doModal.soTrang`) — phản biện vòng 4 (20/08): gộp ca này vào
 *    `khongThayTrang` là shop 240 SP tạo chương trình 10 SP mà không một dòng cảnh báo.
 *  · không cờ nào (mất modal) — HỎNG, vòng gọi luôn cảnh báo.
 */
function pageClickTrang(nhan) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.offsetParent !== null && getComputedStyle(e).display !== "none";
  const ungVienModal = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  // ⚠ BA NEO, KHÔNG PHẢI MỘT — và `hien()` chỉ là phép lọc ƯU TIÊN, không có quyền phủ quyết.
  //
  // `.select-tips` ("Đã chọn N/1000") là dòng đếm ở footer, và nó CHỈ xuất hiện SAU KHI đã chọn được sản
  // phẩm — modal vừa mở, chưa tick gì, thì dòng đó chưa render. Neo mỗi vào nó là vòng luẩn quẩn: muốn tìm
  // modal phải có dòng đếm, muốn có dòng đếm phải tick, muốn tick phải tìm được modal.
  // Đo thật lúc 03:26 23/08/2026: 15 hộp `.eds-modal*` trong DOM, KHÔNG hộp nào có `.select-tips`, trong khi
  // modal đang mở rành rành trên màn hình. `.product-selector-modal` và `.product-modal-header` là của riêng
  // modal này và có mặt NGAY từ lúc mở.
  //
  // `hien()` cũng không được phép loại: modal EDS nằm trong lớp phủ `position: fixed`, mà `offsetParent` của
  // phần tử fixed là `null` ⇒ hộp đang mở vẫn bị chấm là ẩn.
  const modal = ungVienModal.find((m) => m.querySelector(".select-tips, .product-selector-modal, .product-modal-header") && hien(m))
             || ungVienModal.find((m) => m.querySelector(".select-tips, .product-selector-modal, .product-modal-header"))
             || null;
  if (!modal) { return { ok: false, loi: "không thấy modal chọn sản phẩm (neo .select-tips) — KHÔNG bấm pager ngoài modal" }; }
  const pages = Array.from(modal.querySelectorAll("ul.eds-pager__pages li"));
  if (pages.length === 0) {
    return { ok: false, pagerVang: true, loi: "trong modal KHÔNG còn ul.eds-pager__pages nào", coSan: [] };
  }
  const hit = pages.find((p) => gon(p.textContent) === String(nhan));
  if (!hit) {
    return { ok: false, khongThayTrang: true, loi: "không thấy trang " + nhan,
             coSan: pages.map((p) => gon(p.textContent)) };
  }
  hit.click();
  return { ok: true, cls: hit.className };
}

/**
 * Dấu vân tay bảng CỦA MODAL — để biết "trang đã đổi chưa".
 * ⚠ Cũng neo `.select-tips` (cùng lý do {@link pageClickTrang}): `tbody tr` toàn trang có thể lấy nhầm bảng
 * ngoài modal, mà bảng ngoài thì KHÔNG đổi khi lật trang trong modal ⇒ vòng lật trang kết luận "bảng không đổi"
 * và dừng sớm ở trang 1. Mất modal ⇒ trả chuỗi mốc "(KHÔNG THẤY MODAL)". Lưu ý: vòng lật trang chỉ so `!==`
 * nên chuỗi mốc này được nó coi là "bảng ĐÃ đổi" và đi thêm một vòng — vòng đó sẽ tự đổ ra cảnh báo khi các
 * phép đọc/click sau trượt trên modal đã chết; chuỗi mốc ở đây là để NGƯỜI đọc dump nhìn ra ngay, không phải
 * một nhánh xử lý riêng của vòng.
 */
function pageVanTayBang() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.offsetParent !== null && getComputedStyle(e).display !== "none";
  const ungVienModal = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  // ⚠ BA NEO, KHÔNG PHẢI MỘT — và `hien()` chỉ là phép lọc ƯU TIÊN, không có quyền phủ quyết.
  //
  // `.select-tips` ("Đã chọn N/1000") là dòng đếm ở footer, và nó CHỈ xuất hiện SAU KHI đã chọn được sản
  // phẩm — modal vừa mở, chưa tick gì, thì dòng đó chưa render. Neo mỗi vào nó là vòng luẩn quẩn: muốn tìm
  // modal phải có dòng đếm, muốn có dòng đếm phải tick, muốn tick phải tìm được modal.
  // Đo thật lúc 03:26 23/08/2026: 15 hộp `.eds-modal*` trong DOM, KHÔNG hộp nào có `.select-tips`, trong khi
  // modal đang mở rành rành trên màn hình. `.product-selector-modal` và `.product-modal-header` là của riêng
  // modal này và có mặt NGAY từ lúc mở.
  //
  // `hien()` cũng không được phép loại: modal EDS nằm trong lớp phủ `position: fixed`, mà `offsetParent` của
  // phần tử fixed là `null` ⇒ hộp đang mở vẫn bị chấm là ẩn.
  const modal = ungVienModal.find((m) => m.querySelector(".select-tips, .product-selector-modal, .product-modal-header") && hien(m))
             || ungVienModal.find((m) => m.querySelector(".select-tips, .product-selector-modal, .product-modal-header"))
             || null;
  if (!modal) { return "(KHÔNG THẤY MODAL)"; }
  return Array.from(modal.querySelectorAll("tbody tr")).slice(0, 2).map((r) => gon(r.textContent).slice(0, 60)).join(" | ");
}

/** Dump khối "Thiết lập hàng loạt" + thanh đáy (chỉ xuất hiện SAU khi sản phẩm đã vào form). */
function pageDumpSauKhiCoSanPham() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const mota = (e) => (e ? { tag: e.tagName, cls: e.className, text: gon(e.textContent).slice(0, 40) } : null);
  const nut = Array.from(document.querySelectorAll("button"));
  const timNut = (re) => mota(nut.find((b) => new RegExp(re, "i").test(b.textContent || "")));
  const inputs = Array.from(document.querySelectorAll("input")).filter((i) => i.offsetParent !== null);
  return {
    // Đếm theo Ô %GIẢM của từng dòng — "tbody tr" đếm cả bảng của modal/nơi khác nên lượt 00:51 trả 0 trong
    // khi form rõ ràng đã có sản phẩm (dump cùng lượt liệt kê hàng loạt ô %GIẢM).
    soDongBangSanPhamTrongForm: Array.from(document.querySelectorAll("input.eds-input__input"))
      .filter((i) => i.offsetParent !== null && /%GIẢM/i.test((i.closest(".eds-input") || i.parentElement || {}).textContent || "")).length,
    moiInputDangHien: inputs.slice(0, 12).map((i) => ({
      cls: i.className,
      ph: i.placeholder || null,
      max: i.getAttribute("maxlength"),
      // Text của khối cha gần nhất — cách rẻ nhất để nhận ra ô nào là %GIẢM.
      choCha: gon((i.closest("td,div") || {}).textContent || "").slice(0, 50),
    })),
    nutCapNhatHangLoat: timNut("cập nhật hàng loạt|cap nhat hang loat"),
    coThanhDay: !!document.querySelector(".fix-container.fixed-bottom"),
    nutXacNhanThanhDay: mota(document.querySelector(".fix-container.fixed-bottom .confirm-btn")),
    urlHienTai: location.href,
  };
}

/**
 * Gõ % giảm vào ô của khối "Thiết lập hàng loạt".
 *
 * ⚠ BẢN CŨ mò "input đầu tiên đang hiện mà quanh nó có chữ %/giảm" — trên trang này có tới 117 ô %GIẢM
 * (mỗi dòng sản phẩm một ô) nên hoàn toàn có thể gõ trúng ô của MỘT DÒNG thay vì ô hàng loạt, rồi vẫn đọc
 * lại thấy "5" và báo ok. Nay neo thẳng vào khối `.detail-batch` theo DOM thật người dùng cung cấp.
 */
// `export` (31/08/2026) cho flow-flashsale.js dùng lại: form TẠO FLASH mới của Shopee (đổi 29–30/08) dùng
// đúng khối bulk + ô %GIẢM như form KM ("tương tự phần km, chỉ khác phần chọn sp" — người dùng 31/08).
export function pageGoPhanTram(giaTri) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (el) => { const r = el.getBoundingClientRect(); return r.width > 0 || r.height > 0; };
  const batch = document.querySelector(".detail-batch");
  let hit = batch ? batch.querySelector(".discount-box input.eds-input__input, .discount-input input.eds-input__input, input.eds-input__input") : null;
  let nguon = hit ? "khối .detail-batch" : null;
  if (!hit) {
    const inputs = Array.from(document.querySelectorAll("input")).filter(hien);
    hit = inputs.find((i) => /%|giảm|giam/i.test(gon((i.closest("td,div") || {}).textContent || "") + " " + (i.placeholder || "")));
    nguon = hit ? "MÒ theo chữ quanh input (KHÔNG neo được .detail-batch)" : null;
  }
  if (!hit) return { ok: false, loi: "không thấy ô %GIẢM của khối Thiết lập hàng loạt" };
  try {
    const desc = Object.getOwnPropertyDescriptor(Object.getPrototypeOf(hit), "value");
    if (desc && desc.set) { desc.set.call(hit, String(giaTri)); } else { hit.value = String(giaTri); }
  } catch (e) { hit.value = String(giaTri); }
  hit.dispatchEvent(new Event("input", { bubbles: true }));
  hit.dispatchEvent(new Event("change", { bubbles: true }));
  return { ok: true, nguon, cls: hit.className, docLai: hit.value };
}


/** DÒ cấu trúc bảng sản phẩm TRONG FORM: mọi checkbox đang hiện ngoài modal + ngữ cảnh, để biết chọn dòng
 * bằng gì. Lượt 00:55 cho thấy bảng form KHÔNG có <thead> (soThead=0) nên cách tick cũ trượt, mà nút
 * "Cập nhật hàng loạt" thì đã bật — chỉ thiếu "N sản phẩm đã chọn". */
function pageDoBangTrongForm() {
  const na = (s) => (s || "").replace(/s+/g, " ").trim();
  const hien = (e) => !!e && e.offsetParent !== null;
  const cbs = Array.from(document.querySelectorAll("input[type=checkbox]")).filter((c) => hien(c) && !c.closest(".eds-modal"));
  return {
    soCheckboxNgoaiModal: cbs.length,
    viDuCheckbox: cbs.slice(0, 6).map((c) => ({
      cls: c.className,
      chaGan: na((c.closest("label,th,td,div") || {}).className || ""),
      quanhDo: na(((c.closest("tr") || c.closest("div") || {}).textContent || "")).slice(0, 60),
      trongThead: !!c.closest("thead"),
      trongHeader: !!c.closest(".eds-table__header, .eds-table__header-container"),
    })),
    khoiBang: Array.from(document.querySelectorAll(".eds-table, table")).filter(hien).slice(0, 5)
      .map((t) => ({ cls: na(t.className), soTr: t.querySelectorAll("tr").length, coThead: !!t.querySelector("thead") })),
    nhanChonTatCa: Array.from(document.querySelectorAll("label,span,div")).filter((e) => hien(e) && /chọn tất cả|select all/i.test(e.textContent || "") && (e.textContent || "").length < 40).slice(0,3).map((e)=>na(e.className)),
  };
}

/** Đọc trạng thái nút "Cập nhật hàng loạt" (bật/tắt) + dòng "N sản phẩm đã chọn" của khối thiết lập hàng loạt. */
function pageTrangThaiNutBulk() {
  const na = (s) => (s || "").replace(/s+/g, " ").trim();
  const nut = Array.from(document.querySelectorAll("button")).find((b) => /cập nhật hàng loạt/i.test(b.textContent || ""));
  const info = Array.from(document.querySelectorAll(".info-hints, .left-info")).map((e) => na(e.textContent)).slice(0, 3);
  return nut ? { co: true, disabled: /disabled/.test(nut.className) || nut.disabled, cls: nut.className, info } : { co: false, info };
}

/**
 * Ảnh chụp %GIẢM của TỪNG DÒNG SẢN PHẨM, để so trước/sau khi bấm "Cập nhật hàng loạt".
 *
 * ⚠ Hai bản trước đều đo RỖNG và vì thế luôn báo "KHONG_DOI" bất kể code làm gì:
 *   - bản 1 so `tr.textContent` — textContent KHÔNG chứa value của <input>;
 *   - bản 2 vẫn duyệt `tbody tr` — mà bảng của form KHÔNG có tbody/tr nào (thead=0, .eds-table=0), các dòng
 *     là div ⇒ mảng rỗng, [] === [].
 * Bản này bám vào thứ ĐO ĐƯỢC và ĐÃ ĐẾM ĐƯỢC: 116 ô có hậu tố "%GIẢM" nằm NGOÀI khối .detail-batch.
 */
// `export` (31/08/2026) cho flow-flashsale.js — xem ghi chú ở {@link pageGoPhanTram}.
export function pageAnhBangSanPham() {
  const batch = document.querySelector(".detail-batch");
  const ngoaiBatch = (el) => !batch || !batch.contains(el);
  const giaTri = Array.from(document.querySelectorAll(".eds-input__suffix"))
    .filter((s) => (s.textContent || "").indexOf("%GIẢM") >= 0)
    .filter(ngoaiBatch)
    .map((s) => {
      const inp = s.parentElement ? s.parentElement.querySelector("input") : null;
      return inp ? String(inp.value === undefined ? "" : inp.value) : "?";
    });
  return {
    soO: giaTri.length,
    soODaCoSo: giaTri.filter((v) => v !== "" && v !== "?").length,
    mauDau: giaTri.slice(0, 10),
  };
}

/**
 * Đọc THÁNG/NĂM đang hiển thị trên popper picker ĐANG MỞ.
 *
 * Vì sao cần (lỗi thật, phản biện 19/08): bước 4/5 từng chỉ gửi `ngayTrongThang` rồi click ô ngày — nên đặt chương
 * trình ngày 25-09 mà picker đang ở tháng 08 thì nó click trúng **25-08**, và phép đọc-lại `HH:mm dd-MM-yyyy`
 * là thứ duy nhất bắt được. Nay chuyển tháng TRƯỚC rồi mới click ngày.
 *
 * ĐỎ KHI: không có popper hiện, không thấy khối tiêu đề chứa năm 4 chữ số, hoặc tiêu đề không tách được
 * tháng/năm. Cả ba đều trả `{loi}` ⇒ bước gọi hỏng và nói rõ lý do — TUYỆT ĐỐI không đoán mò rồi click.
 */
function pageDocThangNamPicker() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.offsetParent !== null && getComputedStyle(e).display !== "none";
  const p = Array.from(document.querySelectorAll(".eds-date-picker__picker, .eds-picker-panel, .eds-popper")).find(hien);
  if (!p) { return { loi: "không có popper nào đang hiện" }; }

  // ⚠ CHÈN KHOẢNG TRẮNG GIỮA CÁC THẺ CON — đừng quay lại dùng `textContent` trần.
  // Lỗi thật (đêm 23/08/2026, 12/12 shop hỏng): EDS tách tiêu đề thành hai thẻ `Tháng 8` + `2026`, nên
  // `textContent` của khối cha nối THẲNG thành `"Tháng 82026"`. `match(/\d+/g)` đọc ra MỘT số `82026` —
  // không phải năm (>2999), không phải tháng (>12) — nên hàm này báo "không tách được", bước 4 thoát sớm,
  // và ngày/giờ/phút/Xác nhận KHÔNG BAO GIỜ được click. Một dấu cách thiếu giết trọn một đêm chạy.
  const chuCoKhoang = (e) => {
    const manh = [];
    const di = (n) => {
      if (n.nodeType === 3) { manh.push(n.nodeValue || ""); return; }
      for (const c of n.childNodes) { di(c); }
    };
    for (const c of e.childNodes) { di(c); manh.push(" "); }
    return gon(manh.join(" "));
  };

  const ungVien = Array.from(p.querySelectorAll('[class*="header"], [class*="Header"], [class*="title"], [class*="label"]'))
    .filter(hien)
    .map(chuCoKhoang)
    .filter((t) => /\d{4}/.test(t) && t.length > 0 && t.length < 60);
  if (!ungVien.length) {
    return { loi: "không thấy khối tiêu đề tháng-năm trong popper", classPopper: gon(p.className) };
  }
  // Duyệt từ khối NHỎ NHẤT ra: khối cha cũng khớp nhưng text của nó lẫn cả bảng ngày. Lấy khối ĐẦU TIÊN tách
  // được CẢ tháng LẪN năm — EDS có bản tách tháng và năm thành hai thẻ con, lúc đó chỉ khối cha mới đủ hai vế.
  ungVien.sort((a, b) => a.length - b.length);
  for (const tieuDe of ungVien) {
    const so = (tieuDe.match(/\d+/g) || []).map(Number);
    const nam = so.find((n) => n >= 1900 && n <= 2999);
    const thang = so.find((n) => n >= 1 && n <= 12);
    if (nam && thang) { return { thang, nam, tieuDe }; }
  }

  // LƯỚI THỨ HAI cho đúng ca trên: nếu vì lý do nào đó tháng và năm vẫn dính liền (`"Tháng 82026"`,
  // `"82026"`), tách bằng hình dạng — 4 chữ số CUỐI là năm, 1–2 chữ số ngay trước là tháng. Chỉ nhận khi cả
  // hai vế đều nằm trong miền hợp lệ, không đoán mò.
  for (const tieuDe of ungVien) {
    const m = tieuDe.match(/(\d{1,2})(\d{4})(?!\d)/);
    if (!m) { continue; }
    const thang = Number(m[1]);
    const nam = Number(m[2]);
    if (thang >= 1 && thang <= 12 && nam >= 1900 && nam <= 2999) {
      return { thang, nam, tieuDe, ghiChu: "tách theo hình dạng (tháng/năm dính liền)" };
    }
  }
  return { loi: "không tách được tháng/năm từ tiêu đề picker", ungVien: ungVien.slice(0, 5) };
}

/**
 * Bấm mũi tên chuyển THÁNG của popper đang mở (`tien` = true: tháng sau; false: tháng trước).
 *
 * Viết PHÒNG THỦ vì DOM nút mũi tên CHƯA dò thật (plan 19/08 mục "Rủi ro"): nhận diện theo class chứa
 * prev/left (lùi) hoặc next/right (tiến), và LOẠI nút chuyển NĂM (class chứa year/double/super) — bấm nhầm nó
 * là nhảy 12 tháng. Không thấy nút ⇒ trả `{ok:false}` kèm danh sách class đã thấy, KHÔNG click bừa.
 *
 * ⚠ LOẠI luôn mọi ứng viên NẰM TRONG BẢNG NGÀY (phản biện vòng 2, 19/08/2026): ô ngày của tháng liền kề mang
 * đúng class `prev-month` / `next-month`, tức lọt thẳng qua bộ lọc trên và text của nó ("28", "1") lại ngắn
 * dưới 4 ký tự. Click trúng nó là CHỌN NGÀY chứ không phải chuyển tháng — mà picker đóng lại ngay sau đó, nên
 * vòng {@link chuyenToiThang} sẽ báo "tiêu đề không đổi" trong khi thật ra ta vừa đặt sai ngày.
 */
function pageBamChuyenThang(tien) {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.offsetParent !== null && getComputedStyle(e).display !== "none";
  const cls = (e) => String(e.className && e.className.baseVal !== undefined ? e.className.baseVal : (e.className || ""));
  const p = Array.from(document.querySelectorAll(".eds-date-picker__picker, .eds-picker-panel, .eds-popper")).find(hien);
  if (!p) { return { ok: false, loi: "không có popper nào đang hiện" }; }

  const re = tien ? /next|right|forward/i : /prev|left|back/i;
  const moiUngVien = Array.from(p.querySelectorAll("button, a, i, span, div")).filter(hien);
  const hopLe = moiUngVien.filter((e) => {
    const c = cls(e);
    if (!re.test(c)) { return false; }
    if (/year|double|super/i.test(c)) { return false; } // nút NĂM — nhảy 12 tháng một phát
    if (e.closest(".eds-date-table, .eds-date-table__rows")) { return false; } // Ô NGÀY `prev-month`/`next-month`
    return gon(e.textContent).length <= 4;              // mũi tên không có chữ; loại khối bọc lẫn cả bảng
  });
  if (!hopLe.length) {
    return {
      ok: false,
      loi: "không thấy nút chuyển tháng (" + (tien ? "tiến" : "lùi") + ") trong popper",
      coSan: moiUngVien.map((e) => cls(e)).filter((c) => c).slice(0, 12),
    };
  }
  if (typeof _edsChonMuiTenThang !== "function") {
    return { ok: false, loi: "chưa cài window._edsChonMuiTenThang (thiếu bước chạy pageCaiLuatMuiTenThang)" };
  }

  // Vị trí của mỗi mũi tên so với CỤM NHÃN — nguyên liệu của luật chung {@link pageCaiLuatMuiTenThang}.
  const nhan = Array.from(p.querySelectorAll(".eds-picker-header__label"));
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

  // Ưu tiên <button> BỌC NGOÀI phần tử đã chọn (phần tử Vue gắn handler) — giữ nguyên chủ đích của bản cũ,
  // nhưng nay áp cho ĐÚNG mũi tên đã chọn thay vì "button đầu tiên trong danh sách".
  let nut = hopLe[chiSo];
  const boc = nut.closest ? nut.closest("button") : null;
  if (boc) { nut = boc; }
  nut.click();
  return { ok: true, cls: cls(nut), tag: nut.tagName, chiSoDaChon: chiSo, soUngVien: hopLe.length,
           soNhan: nhan.length };
}

/**
 * ĐO modal "Chọn Sản Phẩm" ĐANG MỞ — neo `.select-tips`, mọi truy vấn SCOPE TRONG modal.
 *
 * Đây là phép đo DUY NHẤT được phép kết luận "shop rỗng". Bản trước `sleep(2500)` rồi đọc số ô %GIẢM SAU khi
 * modal đã đóng: máy chậm là shop có hàng bị bỏ oan, và mọi hỏng ở bước 1–5 đều bị nguỵ trang thành "shop
 * rỗng" (plan §9 thước hỏng #5, cùng họ).
 *
 * `coDauRong` = modal tự khai không có dữ liệu (khối empty của EDS hoặc chữ "không có dữ liệu"/"no data").
 * ĐỎ KHI: modal không mở, hoặc bảng 0 dòng mà KHÔNG có dấu rỗng nào — lúc đó bước 6 báo HỎNG chứ tuyệt đối
 * không được kết luận "rỗng" (kết luận sai ở đây là bỏ oan cả một shop, im lặng).
 */
function pageDoModalSanPhamDangMo() {
  const gon = (s) => (s || "").replace(/\s+/g, " ").trim();
  const hien = (e) => !!e && e.offsetParent !== null && getComputedStyle(e).display !== "none";
  const ungVienModal = Array.from(document.querySelectorAll(".eds-modal__content, .eds-modal__box, .eds-modal"));
  // ⚠ BA NEO, KHÔNG PHẢI MỘT — và `hien()` chỉ là phép lọc ƯU TIÊN, không có quyền phủ quyết.
  //
  // `.select-tips` ("Đã chọn N/1000") là dòng đếm ở footer, và nó CHỈ xuất hiện SAU KHI đã chọn được sản
  // phẩm — modal vừa mở, chưa tick gì, thì dòng đó chưa render. Neo mỗi vào nó là vòng luẩn quẩn: muốn tìm
  // modal phải có dòng đếm, muốn có dòng đếm phải tick, muốn tick phải tìm được modal.
  // Đo thật lúc 03:26 23/08/2026: 15 hộp `.eds-modal*` trong DOM, KHÔNG hộp nào có `.select-tips`, trong khi
  // modal đang mở rành rành trên màn hình. `.product-selector-modal` và `.product-modal-header` là của riêng
  // modal này và có mặt NGAY từ lúc mở.
  //
  // `hien()` cũng không được phép loại: modal EDS nằm trong lớp phủ `position: fixed`, mà `offsetParent` của
  // phần tử fixed là `null` ⇒ hộp đang mở vẫn bị chấm là ẩn.
  const modal = ungVienModal.find((m) => m.querySelector(".select-tips, .product-selector-modal, .product-modal-header") && hien(m))
             || ungVienModal.find((m) => m.querySelector(".select-tips, .product-selector-modal, .product-modal-header"))
             || null;
  if (!modal) {
    // ĐẾM TỪNG NEO RIÊNG — thất bại phải nói được VÌ SAO, không chỉ "không thấy".
    // `selectTips=0` mà `selectorModal>0` ⇒ modal ĐANG mở, chỉ là dòng đếm chưa render (chưa chọn gì).
    // Cả ba = 0 ⇒ modal THẬT SỰ chưa mở ⇒ là chuyện CHỜ hoặc nút "Thêm sản phẩm" không ăn, không phải markup.
    // Không có ba con số này thì mỗi lượt hỏng lại tốn một đêm chạy để đoán — đã tốn hai lượt rồi.
    return {
      coModal: false,
      soModalTrongDom: ungVienModal.length,
      soNeo: {
        selectTips: document.querySelectorAll(".select-tips").length,
        selectorModal: document.querySelectorAll(".product-selector-modal").length,
        header: document.querySelectorAll(".product-modal-header").length,
      },
    };
  }

  const tips = modal.querySelector(".select-tips");
  const soDong = modal.querySelectorAll("tbody tr").length;
  // ⚠ DẤU RỖNG PHẢI ĐANG HIỆN — đừng chỉ hỏi "có trong DOM không". ĐỪNG ĐỔI LẠI.
  //
  // EDS render SẴN khối `.eds-table__empty` ("Không có dữ liệu") và chỉ ẩn bằng `display:none` khi bảng CÓ
  // dòng. Bản trước dùng `!!modal.querySelector(...)` nên `coDauRong` LUÔN true; vòng chờ ở bước 6 thoát ngay
  // nhịp ĐẦU (trước khi bảng kịp render) ⇒ `soDong = 0` ⇒ kết luận "shop rỗng".
  // Đo thật 03:31 23/08/2026: Alina Store1 có 1000 sản phẩm mà bị bỏ oan, IM LẶNG — đúng cái mà xmldoc đầu
  // hàm này thề là "kết luận sai ở đây là bỏ oan cả một shop".
  // Chữ "Không có dữ liệu" trong `modal.textContent` cũng luôn khớp vì text của khối ẩn vẫn tính ⇒ vế thứ hai
  // hỏng cùng một kiểu, phải soi theo phần tử ĐANG HIỆN chứ không quét text cả modal.
  // Dùng `getClientRects()` chứ KHÔNG `offsetParent`: modal nằm trong lớp phủ `position: fixed`, mà
  // `offsetParent` của phần tử fixed là `null` — chính cái bẫy đã ăn ở khối neo phía trên.
  const dangHien = (e) => !!e && e.getClientRects().length > 0;
  const khoiRongEl = modal.querySelector(
    ".eds-table__empty, .eds-table__empty-text, .eds-table-empty, .eds-empty, .empty-text, .no-data");
  const khoiRong = dangHien(khoiRongEl);
  const chuRong = Array.from(modal.querySelectorAll("div,span,p")).some(
    (e) => dangHien(e) && (e.textContent || "").length < 80
        && /không có dữ liệu|khong co du lieu|no data|chưa có sản phẩm/i.test(e.textContent));
  // ⚠ BẢNG CÒN ĐANG TẢI thì dấu rỗng KHÔNG có giá trị kết luận. EDS hiện khối "Không có dữ liệu" NGAY khi
  // modal bung ra, TRƯỚC khi dữ liệu về — nên nó rỗng THẬT nhưng chỉ trong một khoảnh khắc.
  // Đo thật 03:36 23/08/2026: bước 6 xong trong 0 giây và kết luận "shop rỗng" cho Alina Store1 — shop có
  // hàng nghìn sản phẩm. Lần vá trước mới chặn được "dấu rỗng ẩn trong DOM", chưa chặn "dấu rỗng đang hiện vì
  // còn đang tải" — cùng một câu hỏi sai: hỏi "có dấu rỗng không" thay vì "bảng đã lắng chưa".
  const dangTai = dangHien(modal.querySelector(
    ".eds-table__loading, .eds-skeleton, .skeleton-item, .eds-loading, .eds-spinner"));

  return {
    coModal: true,
    soDong,
    dangTai,
    tips: tips ? gon(tips.textContent) : null,
    coDauRong: khoiRong || (soDong === 0 && chuRong),
    soTrang: modal.querySelectorAll("ul.eds-pager__pages li").length,
  };
}


// ── Hàm THUẦN (không đụng trang, không đụng tab) ─────────────────────────────────────────────────────────────

/**
 * PURE — rút số "đã chọn" từ ô đếm của modal ("Đã chọn 240/1000 sản phẩm" ⇒ 240). Không đọc được ⇒ null.
 * Lấy số ĐẦU TIÊN: vế sau là trần 1000 của Shopee, không phải số đã chọn.
 */
function soDaChon(text) {
  const m = String(text === null || text === undefined ? "" : text).match(/\d+/);
  return m ? Number(m[0]) : null;
}

/**
 * PURE — đọc + KIỂM tham số một lượt KM. Trả giá trị đã chuẩn hoá kèm câu lỗi cho TỪNG NHÓM trường (`loi.ten`,
 * `loi.ngayBd`, `loi.ngayKt`, `loi.gio`, `loi.phanTram`) để mỗi bước tự nêu đúng trường của mình.
 *
 * <b>KHÔNG CÒN MỘT `||`-DEFAULT NÀO TRÊN ĐƯỜNG CHẠY THẬT</b> (phản biện vòng 2, 19/08/2026). Bản trước viết
 * `tsAn.ten || "DO DOM KM …"`, `tsAn.gio || "09:30"`, `tsAn.thang || thángHiệnTại`. Cả ba dính CÙNG cái
 * bẫy với `phanTram || 5` đã sửa ở đợt 1, mà lần này còn kín hơn: `kyVong` của bước 4/5 được dựng từ CHÍNH biến
 * vừa bị thay, nên phép đọc-lại tự nhất quán và LUÔN XANH — chương trình mọc lên đúng ngày giờ SAI mà không
 * thước nào bắt được. Nay thiếu trường nào thì lượt HỎNG ngay ở bước giả "0. Tham số", gọi đích danh tên trường
 * (đợt 3 gom về một cửa: bản đợt 2 để bước 3/4/5/8 tự báo, tức sai `%` vẫn tốn ~12 phút lật modal trước khi đỏ).
 *
 * `ts == null` (gọi thẳng, không tham số) là đường DÒ duy nhất còn được dùng mặc định — xem {@link thamSoLuotDo}.
 * Cả hai lệnh thật (`chayKm`, `dumpKm`) đều truyền tham số đầy đủ.
 *
 * <b>KHUÔN GÓI TỪ 26/08/2026 — HAI MỐC NGÀY, MỘT GIỜ.</b> Chương trình nay chạy một KHOẢNG 180 ngày (bắt đầu
 * HÔM NAY, kết thúc hôm nay + 180) với CÙNG một giờ ở hai đầu, nên gói mang `bd` + `kt` (mỗi cái là một khuôn
 * ngày bốn trường, xem {@link docMocNgay}) và đúng MỘT `gio`. Trường `ngayTrongThang`/`gioBatDau`/`gioKetThuc`
 * phẳng của bản cũ đã biến mất — C# dựng gói ở `ThamSoKm.Goi()`, đổi một bên là phải đổi cả hai.
 */

function docThamSoKm(ts) {
  const nguon = (ts === null || ts === undefined) ? thamSoLuotDo() : ts;
  const loi = {};

  const ten = typeof nguon.ten === "string" ? nguon.ten.trim() : "";
  if (!ten) { loi.ten = `thiếu tham số 'ten' (tên chương trình): '${nguon.ten}'`; }

  // HAI MỐC NGÀY, cùng một hàm kiểm — xem `docMocNgay`. Tên lỗi tách riêng (`ngayBd` / `ngayKt`) để người đọc
  // nhật ký biết ngay ĐẦU NÀO của khoảng hỏng, thay vì một chữ "ngay" chung chung cho hai thứ khác nhau.
  const bd = docMocNgay(nguon.bd);
  if (bd.loi) { loi.ngayBd = "mốc BẮT ĐẦU — " + bd.loi; }
  const kt = docMocNgay(nguon.kt);
  if (kt.loi) { loi.ngayKt = "mốc KẾT THÚC — " + kt.loi; }

  // MỘT GIỜ cho CẢ HAI đầu khoảng (26/08/2026) — trường `gioKetThuc` cũ đã biến mất khỏi gói.
  const docGio = (s) => (typeof s === "string" && /^([01]\d|2[0-3]):[0-5]\d$/.test(s)) ? s.split(":") : null;
  const gio = docGio(nguon.gio);
  if (!gio) { loi.gio = `giờ không hợp lệ (cần HH:mm): '${nguon.gio}'`; }

  // % GIẢM: cùng luật, giữ nguyên từ đợt 1. Tầng form đã chặn sẵn cùng khoảng (KmDialogViewModel.KiemTraForm).
  const phanTram = Number(nguon.phanTram);
  if (!(Number.isInteger(phanTram) && phanTram >= 1 && phanTram <= 99)) {
    loi.phanTram = `% giảm không hợp lệ: '${nguon.phanTram}' — phải là số nguyên 1–99, extension KHÔNG tự thay giá trị`;
  }

  return { ten, bd, kt, gio, phanTram, loi };
}

// ── Khung chạy từng bước ────────────────────────────────────────────────────────────────────────────────────

/**
 * Bấm mũi tên cho tới khi popper đang mở đứng ĐÚNG tháng/năm mục tiêu.
 *
 * ĐỎ KHI: không đọc được tiêu đề tháng-năm · không thấy nút mũi tên · bấm rồi mà tiêu đề KHÔNG ĐỔI · quá
 * {@link TRAN_CHUYEN_THANG} lần vẫn chưa tới. Cả bốn đều trả `{ok:false, loi}` — bước gọi hỏng và DỪNG shop,
 * chứ không click ô ngày trong một tháng sai.
 */
async function chuyenToiThang(chay, thangDich, namDich) {
  const nhatKy = [];
  for (let i = 0; i <= TRAN_CHUYEN_THANG; i++) {
    const doc = await chay(pageDocThangNamPicker, []);
    if (!doc || doc.loi) { return { ok: false, loi: (doc && doc.loi) || "không đọc được tháng/năm", nhatKy }; }
    if (doc.thang === thangDich && doc.nam === namDich) { return { ok: true, tieuDe: doc.tieuDe, nhatKy }; }
    if (i === TRAN_CHUYEN_THANG) {
      return { ok: false, nhatKy, loi: `bấm ${TRAN_CHUYEN_THANG} lần vẫn chưa tới ${thangDich}-${namDich} `
        + `(đang ở "${doc.tieuDe}")` };
    }

    const tien = (namDich * 12 + thangDich) > (doc.nam * 12 + doc.thang);
    const bam = await chay(pageBamChuyenThang, [tien]);
    nhatKy.push({ tu: doc.tieuDe, tien, bam });
    if (!bam || !bam.ok) { return { ok: false, loi: (bam && bam.loi) || "không bấm được nút chuyển tháng", nhatKy }; }

    // CHỜ tiêu đề ĐỔI THẬT (Vue vẽ lại bảng ngày) — không sleep mù. Đây cũng là phép đọc-lại của chính cú bấm.
    let daDoi = false;
    for (let j = 0; j < 20; j++) {
      await sleep(250);
      const lai = await chay(pageDocThangNamPicker, []);
      if (lai && !lai.loi && lai.tieuDe !== doc.tieuDe) { daDoi = true; break; }
    }
    if (!daDoi) { return { ok: false, loi: `bấm chuyển tháng mà tiêu đề vẫn "${doc.tieuDe}"`, nhatKy }; }
  }
  return { ok: false, loi: "vòng chuyển tháng thoát bất thường", nhatKy };
}

/**
 * Đặt MỘT ô thời gian của form: mở picker → CHUYỂN THÁNG tới đúng tháng/năm → click ô ngày → cột giờ → cột
 * phút → "Xác nhận". `chiSo` 0 = ô BẮT ĐẦU, 1 = ô KẾT THÚC.
 * <para>Phép ĐỌC LẠI do bước gọi làm — so BẰNG chuỗi đầy đủ `HH:mm dd-MM-yyyy` (mảnh `09:30` từng che mất lỗi
 * lệch NGÀY, plan §9 thước hỏng #2).</para>
 */
async function datOThoiGian(chay, chiSo, gio, ngayTrongThang, thangDich, namDich) {
  // Cài luật "ô ngày nào chọn được" lên window của TRANG trước đã — `pageClickNgayTheoSo` và bản dump đều gọi
  // bare `_edsONgayHopLe(...)`. Cài lại mỗi lượt là idempotent và rẻ; thiếu nó thì bước hỏng TO TIẾNG chứ
  // không lặng lẽ chạy luật cũ.
  await chay(pageCaiLuatONgayEds, []);
  // Luật "mũi tên nào là mũi tên THÁNG" — bắt buộc, xem {@link pageCaiLuatMuiTenThang}. Cài lại mỗi lượt là
  // idempotent và rẻ.
  await chay(pageCaiLuatMuiTenThang, []);
  const moPicker = await chay(pageMoPicker, [chiSo]);
  await sleep(1200);
  const dumpKhiMo = await chay(pageDumpPickerDangMo, []);

  // ⚠ ĐỌC THÁNG HỎNG THÌ **KHÔNG** THOÁT SỚM. Đừng đổi lại thành `return`.
  //
  // (Cập nhật 26/08/2026) Chương trình nay chạy một KHOẢNG: ô BẮT ĐẦU (chiSo 0) đặt cho HÔM NAY nên picker mở
  // ra đã đúng tháng và vòng chuyển tháng khớp ngay lượt đầu; ô KẾT THÚC (chiSo 1) thì cách ~6 tháng nên vòng
  // ấy LUÔN phải bấm mũi tên. Lập luận bên dưới KHÔNG đổi: nó vẫn là bước TRỢ GIÚP, trọng tài vẫn là phép ĐỌC
  // LẠI ở cuối bước gọi.
  //
  // Bản trước thoát ngay tại đây, và đêm 23/08/2026 nó giết cả 12 shop vì một lý do vô lý: chương trình đặt
  // cho CHÍNH HÔM NAY, lịch đã mở sẵn đúng tháng, chẳng cần chuyển đi đâu — nhưng bước đọc tháng vẫn chạy vô
  // điều kiện và chết ở đó, nên ngày/giờ/phút/Xác nhận không bao giờ được click.
  //
  // Đây là bước TRỢ GIÚP, không phải cửa chặn:
  //  · `pageClickNgayTheoSo` đã tự loại ô ngoài tháng, nên đặt trong tháng đang hiện là an toàn sẵn;
  //  · trọng tài thật là phép ĐỌC LẠI cuối bước — nó so chuỗi ĐẦY ĐỦ `HH:mm dd-MM-yyyy`, mạnh hơn hẳn phép
  //    đọc tiêu đề, và chính nó đã bắt được lỗi này 12 lần.
  // Tháng sai thật mà đọc tiêu đề cũng hỏng ⇒ click nhầm ngày, đọc lại KHÔNG khớp ⇒ bước đỏ ⇒ shop bị bỏ qua.
  // KHÔNG có chương trình sai nào được tạo: `daTao` chỉ bật khi đi tới được bước 9.
  const chuyenThang = await chuyenToiThang(chay, thangDich, namDich);

  // Lý do hỏng của vòng chuyển tháng đi KÈM MỌI LỐI RA của hàm này. Nó là bước trợ giúp nên không tự làm
  // bước đỏ, nhưng khi có thứ khác đỏ thì nó thường là nguyên nhân — mà nhật ký đêm 27/08 không hề nói ra.
  const ghiChuThang = chuyenThang && chuyenThang.ok === false
    ? "chuyển tháng hỏng: " + (chuyenThang.loi || "(không rõ)")
    : null;

  const ngay = await chay(pageClickNgayTheoSo, [ngayTrongThang]);

  // ⚠ CHỌN NGÀY HỎNG ⇒ DỪNG NGAY, **KHÔNG** bấm "Xác nhận".
  //
  // Dump `logs/km-dom-20260827-091851.json` cho thấy `ngay.ok=false` mà `xacNhan.ok=true`: luồng vẫn chốt một
  // picker chưa chọn được gì, nên ô thời gian giữ nguyên GIÁ TRỊ CŨ và bằng chứng về cú trượt bị xoá sạch —
  // chỉ còn lại câu "ĐỌC LẠI KHÔNG KHỚP" không nói được vì sao. Hỏng ở đâu thì dừng ở đó.
  if (!ngay || !ngay.ok) {
    const manh = ["chọn NGÀY " + ngayTrongThang + "-" + thangDich + "-" + namDich
      + " hỏng ⇒ KHÔNG bấm Xác nhận: " + ((ngay && ngay.loi) || "(không rõ)")];
    if (ngay && ngay.coSan) { manh.push("ngày chọn được: [" + ngay.coSan.join(",") + "]"); }
    if (ngay && ngay.clsOCungSo && ngay.clsOCungSo.length) {
      manh.push("class ô mang số '" + ngayTrongThang + "': " + ngay.clsOCungSo.join(" | "));
    }
    if (ghiChuThang) { manh.push(ghiChuThang); }
    return { moPicker, dumpKhiMo, chuyenThang, ngay, gio: null, phut: null, xacNhan: null, loi: manh.join(" · ") };
  }

  await sleep(900);
  const gioClick = await chay(pageClickLiThoiGian, [0, gio[0]]);
  await sleep(500);
  const phut = await chay(pageClickLiThoiGian, [1, gio[1]]);
  await sleep(500);
  const xacNhan = await chay(pageXacNhanPicker, []);
  await sleep(1200);
  return { moPicker, dumpKhiMo, chuyenThang, ngay, gio: gioClick, phut, xacNhan, loi: ghiChuThang };
}

/**
 * Chạy TRỌN luồng "Tạo chương trình khuyến mãi" cho SHOP ĐANG MỞ (tab do `openShopDetail` để lại).
 *
 * Dùng chung cho hai lệnh: `dumpKm` (nút dò của pha 1) và `chayKm` (luồng thật của pha 3). CỐ Ý KHÔNG chép
 * thành hai bản: chuỗi 9 bước này mất một đêm mới dò ra và có 6 chỗ bẫy đã trả giá (xem plan §9, §10); hai bản
 * song song thì bản ít chạy sẽ mục dần mà không ai biết.
 *
 * <b>MỘT BƯỚC CỐT LÕI HỎNG ⇒ DỪNG SHOP NGAY.</b> Bản trước vứt kết quả `await buoc(...)` và chỉ nhìn bước 9
 * để kết luận `daTao` — nên bước 3/4/5/8 đỏ vẫn đi tiếp và TẠO chương trình sai giờ / sai %, rồi báo thành
 * công. Nay bước nào `ok:false` là trả về ngay: các bước sau chạy trên trạng thái rác, không có giá trị chẩn
 * đoán nào (áp cho CẢ `dumpKm` lẫn `chayKm`). Ngoại lệ DUY NHẤT là nhánh "shop rỗng" ở bước 6 — đó là kết
 * luận nghiệp vụ, không phải bước hỏng.
 *
 * @param {object} ts Tham số: { ten, bd:{ngayTrongThang,thang,nam,ngayDayDu}, kt:{…}, gio:"HH:mm",
 *                               phanTram }. `null` = lượt DÒ (dùng mặc định).
 * @returns {Promise<{buoc:Array, soSanPham:number, daTao:boolean, boQua:string|null, canhBao:Array,
 *                    captcha:boolean}>}
 */
export async function chayKmChoTabDangMo(ts) {
  const tabId = tabShop();
  const ds = [];
  /** Cảnh báo THÔ (một phần tử một lần xảy ra) — `ketThuc` mới gom lại theo loại, xem {@link gomCanhBao}. */
  const canhBaoTho = [];
  const themCanhBao = (loai, cau, gom, chiTiet) => canhBaoTho.push({ loai, cau, gom: gom || cau, chiTiet });
  /**
   * Gói kết quả CHUẨN của một lượt — mọi lối ra đi qua đây để không lối nào quên trường nào.
   * `captcha` suy từ chính các bước: bước nào bắt được `/verify` đã đánh dấu `captcha:true` (xem `buoc`), nên
   * cờ này không thể lệch với thứ đã gửi lên C#.
   */
  const ketThuc = (them) =>
    Object.assign(
      { buoc: ds, soSanPham: 0, daTao: false, boQua: null,
        canhBao: gomCanhBao(canhBaoTho, TRAN_CANH_BAO),
        captcha: ds.some((b) => b.captcha === true) },
      them || {});

  if (tabId == null) {
    // HỎNG HẠ TẦNG, KHÔNG PHẢI "shop rỗng". Bản trước trả `boQua` ⇒ C# đếm vào `SoBoQua` và câu tổng kết nói
    // "bỏ qua N shop không có sản phẩm" cho một service worker vừa chết giữa chừng — một sự cố hạ tầng bị nguỵ
    // trang thành kết luận nghiệp vụ. `boQua` từ nay CHỈ dành cho shop rỗng thật.
    ds.push({
      ten: "0. Tab shop", ok: false, selectorDung: null, kyVong: null, kieuSo: null, docLai: null,
      truoc: null, sau: null, ghiChu: null,
      loi: "chưa có tab shop (openShopDetail chưa chạy, hoặc service worker đã chết giữa chừng)",
    });
    return ketThuc();
  }
  const chay = (fn, args) => execInTab(tabId, fn, args || []);

  // Tham số nghiệp vụ: KHÔNG còn một mặc định ngầm nào trên đường CHẠY THẬT (xem `docThamSoKm`).
  const tp = docThamSoKm(ts);

  // BƯỚC GIẢ "0. Tham số" — TRẢ VỀ TRƯỚC KHI ĐỤNG TRANG (phản biện vòng 3, 19/08/2026).
  // Bản trước để bước 3/4/5/8 tự báo trường của mình, nên một `phanTram` sai vẫn đi hết bước 6: lật tới 105
  // trang modal (~12 phút), tick chọn cả nghìn sản phẩm, rồi mới đỏ ở bước 8 — và bỏ lại một form "Tạo chương
  // trình" điền dở trên shop THẬT. Tham số hỏng thì không có việc gì để làm trên trang cả, hỏng ngay từ cửa.
  // Cùng khuôn với "0. Tab shop": C# đọc `buocHong` ra `Loi` ⇒ shop vào `SoHong`, KHÔNG vào `SoBoQua`.
  const loiThamSo = cauLoiThamSo(tp.loi, "KM");
  if (loiThamSo) {
    ds.push({
      ten: "0. Tham số", ok: false, selectorDung: null, kyVong: null, kieuSo: null, docLai: null,
      truoc: null, sau: null, ghiChu: null, loi: loiThamSo,
    });
    send({ action: "progress", message: `[KM] 0. Tham số: DỪNG — ${loiThamSo}` });
    return ketThuc();
  }

  // HAI MỐC của khoảng chương trình + MỘT giờ dùng chung cho cả hai đầu.
  const bd = tp.bd;
  const kt = tp.kt;
  const gio = tp.gio;
  const tenCt = tp.ten;
  const phanTram = tp.phanTram;

  // 1) Vào "Khuyến Mãi của Shop" qua SIDEBAR (không gọi thẳng URL).
  //
  // Dữ kiện đo được, không phải suy đoán:
  //   a. Ngay sau SSO, trang CHƯA có sidebar (`sidebar: []`). Nó render trễ vài giây ⇒ CHỜ nó xuất hiện rồi mới
  //      chạm vào trang. Đây cũng là mốc "trang đã lắng" đáng tin nhất; hai lần dính `verify/traffic/error` đều
  //      xảy ra khi code chạm vào trang trong 3–4 giây đầu.
  //   b. Sidebar render xong thì mục "Kênh Marketing" ĐÃ BUNG SẴN — cả 9 link con `hien: true`, không cần click
  //      để mở. Link cần dùng: `a[href="/portal/marketing/list/discount"]`, là phần tử ĐANG HIỆN, click nó là
  //      thao tác người dùng bấm tay bình thường.
  //   c. Đường "lưới công cụ → thẻ Khuyến Mãi của Shop" (`#discount_promotion-all .marketing-card`) ĐÃ THỬ ở
  //      lượt 01:26: bấm được thẻ nhưng URL đứng nguyên `/portal/marketing` sau 15s ⇒ thẻ KHÔNG điều hướng
  //      trong cùng tab (nhiều khả năng mở tab mới). Chưa dùng được, ghi lại để pha 3 xét nếu cần.
  const b1 = await buoc(ds, "1. Vào Khuyến Mãi của Shop", async () => {
    const truoc = await chay(() => location.href);

    let coLink = 0;
    for (let i = 0; i < 40; i++) {
      coLink = await chay(pageDemPhanTuHien, ['li.ps_menu_marketing a[href="/portal/marketing/list/discount"]']);
      if (coLink > 0) break;
      await sleep(500);
    }
    if (!coLink) {
      const sauHong = await chay(() => location.href);
      return { selectorDung: null, truoc, sau: sauHong, ghiChu: "KHÔNG thấy link 'Khuyến Mãi của Shop' trong sidebar sau 20s",
               kyVong: "/list/discount", kieuSo: "chua", docLai: sauHong };
    }
    // Chờ 3s cho trang Kênh Người Bán lắng và các script xác thực phiên / WAF nạp xong trước khi click
    await sleep(3000);
    const sel = await chay(pageClickTheoSelector, [['li.ps_menu_marketing a[href="/portal/marketing/list/discount"]'], null]);

    let sau = "";
    for (let i = 0; i < 30; i++) {
      await sleep(500);
      sau = await chay(() => location.href);
      if (String(sau || "").indexOf("/list/discount") >= 0) break;
    }
    return { selectorDung: sel, truoc, sau, ghiChu: "sidebar → link 'Khuyến Mãi của Shop' (đang hiện)",
             kyVong: "/list/discount", kieuSo: "chua", docLai: sau };
  }, tabId);
  if (!b1.ok) { return ketThuc(); }

  // 2) Bấm "Tạo".
  const b2 = await buoc(ds, "2. Bấm Tạo (Chương Trình Của Shop)", async () => {
    const truoc = await chay(() => location.href);
    const sel = await chay(pageClickTheoSelector, [[".eds-react-button--primary", "button.eds-button--primary", "button"], "tạo"]);
    await sleep(2500);
    const sau = await chay(() => location.href);
    const soO = await chay(() => document.querySelectorAll(".eds-selector__inner").length);
    return {
      selectorDung: sel, truoc, sau: { url: sau, soOPicker: soO },
      // Form đã mở = có ít nhất 2 ô picker thời gian. Bám dấu hiệu của FORM, không bám URL (form có thể là modal).
      kyVong: "2", docLai: String(soO >= 2 ? 2 : soO),
      ghiChu: "Kỳ vọng: form mở ⇒ có ≥2 ô eds-selector__inner (hai ô thời gian).",
    };
  }, tabId);
  if (!b2.ok) { return ketThuc(); }

  // 3) Gõ tên chương trình.
  const b3 = await buoc(ds, "3. THÍ NGHIỆM A — gõ Tên chương trình", async () => {
    // Tham số đã được KIỂM TRỌN GÓI ở bước giả "0. Tham số" phía trên — tới đây `tenCt` chắc chắn dùng được.
    const truoc = await chay(pageDocText, [".eds-input__count"]);
    const ten = tenCt;
    const kq = await chay(pageGoNhuNguoi, ['input.eds-input__input[maxlength="150"]', ten, ".eds-input__count", 70]);
    await sleep(600);
    const sau = await chay(pageDocText, [".eds-input__count"]);
    return {
      truoc, sau, loi: kq && kq.loi,
      kyVong: "/150", kieuSo: "chua", docLai: sau && !/^0\//.test(sau) ? sau : "(vẫn 0/150)",
      ghiChu: "Ăn ⇒ Vue NHẬN giá trị JS gán (nativeSetter + input/change) ⇒ pha 3 gõ được tên và %GIẢM bằng DOM.",
    };
  }, tabId);
  if (!b3.ok) { return ketThuc(); }

  // 4) Picker BẮT ĐẦU. Có CHUYỂN THÁNG: mốc có thể nằm ở tháng khác tháng đang hiện.
  const b4 = await buoc(ds, `4. Đặt thời gian BẮT ĐẦU (${bd.ngayDayDu} ${gio[0]}:${gio[1]})`, async () => {
    const truoc = await chay(pageDocOPicker, [0]);
    const dat = await datOThoiGian(chay, 0, gio, bd.ngayTrongThang, bd.thang, bd.nam);
    const sau = await chay(pageDocOPicker, [0]);
    return {
      truoc, sau: Object.assign({}, dat, { textOSauCung: sau }), loi: dat.loi || null,
      kyVong: gio[0] + ":" + gio[1] + " " + bd.ngayDayDu, docLai: sau,
      ghiChu: "Mọi truy vấn SCOPE trong popper đang hiện; chuyển tháng tới đúng tháng/năm RỒI mới khớp ĐÚNG số ngày.",
    };
  }, tabId);
  if (!b4.ok) { return ketThuc(); }

  // 5) Picker KẾT THÚC — cùng đường, cùng phép đọc lại, nhưng NGÀY KHÁC (mốc kết thúc của khoảng) và CÙNG giờ.
  //
  // ⚠ ĐÂY LÀ BƯỚC PHẢI CHUYỂN THÁNG ~6 LẦN. Bước 4 đặt cho HÔM NAY nên picker mở ra đã đúng tháng và
  // `chuyenToiThang` gần như không chạy; bước 5 thì luôn cách ~6 tháng. `TRAN_CHUYEN_THANG` = 8 vẫn dư (đã đo
  // brute-force 2024→2032: tối đa 6), và trọng tài vẫn là phép ĐỌC LẠI `HH:mm dd-MM-yyyy` ở cuối bước.
  const b5 = await buoc(ds, `5. Đặt thời gian KẾT THÚC (${kt.ngayDayDu} ${gio[0]}:${gio[1]})`, async () => {
    const truoc = await chay(pageDocOPicker, [1]);
    const dat = await datOThoiGian(chay, 1, gio, kt.ngayTrongThang, kt.thang, kt.nam);
    const sau = await chay(pageDocOPicker, [1]);
    return {
      truoc, sau: Object.assign({}, dat, { textOSauCung: sau }), loi: dat.loi || null,
      kyVong: gio[0] + ":" + gio[1] + " " + kt.ngayDayDu, docLai: sau,
    };
  }, tabId);
  if (!b5.ok) { return ketThuc(); }

  // 6) Modal "Chọn Sản Phẩm": mở modal → ĐO SHOP RỖNG NGAY TRONG MODAL ĐANG MỞ → tick hết trang → Xác nhận.
  //
  // ⚠ "Shop rỗng" CHỈ được kết luận từ phép đo khi modal ĐANG MỞ (neo `.select-tips`), bằng vòng CHỜ-ĐIỀU-KIỆN.
  // Bản trước `sleep(2500)` rồi đọc số ô %GIẢM SAU khi modal đã đóng: máy chậm ⇒ shop có hàng bị bỏ OAN, và
  // mọi hỏng ở bước 1–5 đều bị nguỵ trang thành "shop rỗng" (cùng họ với 5 thước hỏng ở plan §9).
  const b6 = await buoc(ds, "6. Modal Chọn Sản Phẩm (đo rỗng + tick hết trang)", async (ban) => {
    const selMo = await chay(pageClickTheoSelector, [["button.empty-add", "button"], "thêm sản phẩm"]);

    // CHỜ-ĐIỀU-KIỆN: modal mở VÀ bảng đã LẮNG.
    //
    // ⚠ HAI VẾ KHÔNG ĐỐI XỨNG — đừng gộp lại cho gọn.
    //  · CÓ DÒNG là bằng chứng DƯƠNG, chắc chắn: thấy là thoát ngay.
    //  · DẤU RỖNG là bằng chứng ÂM, và nó XUẤT HIỆN SỚM: EDS hiện "Không có dữ liệu" ngay lúc modal bung ra,
    //    trước khi dữ liệu về. Nhận nó ở nhịp đầu là bỏ oan mọi shop có hàng — đã xảy ra thật 03:31 và 03:36
    //    ngày 23/08/2026 với Alina Store1 (hàng nghìn SP, bị kết luận "shop KHÔNG có sản phẩm nào", im lặng).
    //    Nên dấu rỗng chỉ được tin khi bảng THÔI TẢI và đã ổn định qua `NHIP_ON_DINH_RONG` nhịp.
    const NHIP_ON_DINH_RONG = 10;   // 5 giây
    let doModal = null;
    let nhipRongLienTiep = 0;
    for (let i = 0; i < CHO_MODAL_NHIP; i++) {
      doModal = await chay(pageDoModalSanPhamDangMo, []);
      if (doModal && doModal.coModal) {
        if (doModal.soDong > 0) { break; }
        nhipRongLienTiep = doModal.coDauRong && !doModal.dangTai ? nhipRongLienTiep + 1 : 0;
        if (nhipRongLienTiep >= NHIP_ON_DINH_RONG) { break; }
      }
      await sleep(500);
    }
    const giay = (CHO_MODAL_NHIP * 500) / 1000;
    if (!doModal || !doModal.coModal) {
      // Nhét SỐ ĐẾM vào chính câu lỗi: app chỉ chuyển tiếp `loi` lên nhật ký, mọi trường chẩn đoán khác
      // (`sau`, `ghiChu`) đều rụng dọc đường. Đêm 23/08/2026 extension đã đo đủ 12 lần mà không lần nào tới
      // được người đọc — phải mở app ra soi mới biết nó thấy bao nhiêu hộp modal.
      const soHop = doModal && typeof doModal.soModalTrongDom === "number" ? doModal.soModalTrongDom : "?";
      const n = (doModal && doModal.soNeo) || {};
      const neo = `select-tips=${n.selectTips ?? "?"} product-selector-modal=${n.selectorModal ?? "?"} `
                + `product-modal-header=${n.header ?? "?"}`;
      return { selectorDung: selMo, sau: { doModal }, kyVong: "1", docLai: "0",
               loi: `modal Chọn Sản Phẩm KHÔNG mở sau ${giay}s (${soHop} hộp .eds-modal* trong DOM; neo: ${neo})`,
               ghiChu: "ĐỎ KHI: nút 'Thêm sản phẩm' không ăn, hoặc modal đổi markup (mất .select-tips)." };
    }

    // Bảng rỗng: chỉ kết luận RỖNG khi modal TỰ KHAI không có dữ liệu. Rỗng-mà-không-có-dấu là HỎNG —
    // bỏ oan một shop im lặng còn tệ hơn một dòng nhật ký báo hỏng.
    if (doModal.soDong === 0) {
      // Còn ĐANG TẢI mà hết nhịp chờ ⇒ HỎNG, tuyệt đối không kết luận rỗng: bảng chưa bao giờ lắng thì
      // "không có dòng" chẳng nói lên điều gì về shop.
      if (doModal.dangTai) {
        return { selectorDung: selMo, sau: { doModal }, kyVong: "1", docLai: "0",
                 loi: `modal mở nhưng bảng VẪN ĐANG TẢI sau ${giay}s — chưa đủ căn cứ kết luận shop rỗng` };
      }
      if (!doModal.coDauRong) {
        return { selectorDung: selMo, sau: { doModal }, kyVong: "1", docLai: "0",
                 loi: `modal mở nhưng bảng 0 dòng sau ${giay}s mà KHÔNG có dấu "không có dữ liệu" — chưa đủ căn `
                   + "cứ kết luận shop rỗng" };
      }
      ban.shopRong = true;
      // `ok: true` — đây là KẾT LUẬN NGHIỆP VỤ (shop rỗng), không phải bước hỏng: bước đã đo được đúng thứ nó
      // khẳng định. Bước không khai `kyVong` thì `ok` phải tự khai (xem `buoc`).
      return { selectorDung: selMo, sau: { doModal }, ok: true,
               ghiChu: "Modal ĐANG MỞ, bảng 0 dòng + có dấu rỗng ⇒ SHOP RỖNG (bỏ qua, không tạo chương trình)." };
    }

    const nhatKyTrang = [];
    let trang = 1;
    let daChon = null;
    let soTruoc = null;
    let chamTran = false;
    // CẬN DƯỚI của tổng số trang (xem `soTrangLonNhat`) — để hỏi câu cuối vòng: "đã duyệt hết trang chưa?".
    // CỐ Ý chỉ nhận NHÃN SỐ pager trả về (`sang.coSan`), KHÔNG dùng `doModal.soTrang` (số phần tử `li`): nếu
    // markup gói cả nút ‹ › vào trong `ul.eds-pager__pages` thì shop 1 trang cũng đếm ra 3 ⇒ cảnh báo OAN mỗi
    // shop mỗi lượt. Nhãn số thì miễn nhiễm với cả nút mũi tên lẫn ô "…".
    let soTrangThayDuoc = 0;
    let lyDoDung = null;
    while (trang <= TRAN_TRANG_MODAL) {
      const tick = await chay(pageClickCheckAll, []);
      await sleep(900);
      daChon = await chay(pageDocDaChon, []);
      const so = soDaChon(daChon);
      nhatKyTrang.push({ trang, tick, daChon, so });

      // ĐỌC LẠI CỦA CHÍNH CÚ TICK TRANG NÀY: ô đếm "Đã chọn" của modal phải TĂNG so với trang trước.
      // ĐỎ KHI: check-all đổi markup nên `pageClickCheckAll` bấm hụt · modal đóng mất giữa vòng lật trang ·
      // trang mới chưa render mà ta đã tick. Cả ba đều làm số đứng yên.
      // ⚠ KHÔNG hạ `ok` cả bước: trang cuối HOÀN TOÀN có thể trùng số vì mọi sản phẩm của nó đang nằm trong một
      // chương trình khác và bị Shopee vô hiệu hoá — ca THẬT, không phải lỗi. Nhưng người dùng phải THẤY, nên
      // nó vào `canhBao` kèm số trang (bản trước `ghiChu` khai có phép so này mà mã thì không hề có).
      if (so === null) {
        themCanhBao("doc-o-dem",
          `trang ${trang}: KHÔNG đọc được ô đếm "Đã chọn" của modal — không kiểm được cú tick có ăn không`,
          'KHÔNG đọc được ô đếm "Đã chọn" của modal', `trang ${trang}`);
      } else if (soTruoc === null) {
        if (so === 0) {
          themCanhBao("tick-trang-dau",
            'trang 1: tick "chọn tất cả" xong mà ô đếm vẫn "Đã chọn 0" — cú tick không ăn');
        }
      } else if (so <= soTruoc) {
        themCanhBao("so-khong-tang",
          `trang ${trang}: số đã chọn KHÔNG tăng (${soTruoc} → ${so}) — cú tick "chọn tất cả" có thể không ăn`,
          'số "Đã chọn" KHÔNG tăng sau cú tick "chọn tất cả"', `trang ${trang}`);
      }
      if (so !== null) { soTruoc = so; }

      const vanTayTruoc = await chay(pageVanTayBang, []);
      const sang = await chay(pageClickTrang, [String(trang + 1)]);
      if (sang && sang.coSan) { soTrangThayDuoc = Math.max(soTrangThayDuoc, soTrangLonNhat(sang.coSan)); }
      if (!sang || !sang.ok) {
        // Ba ca RẤT KHÁC NHAU, đừng gộp:
        //  · `khongThayTrang` = pager còn nhãn nhưng không có nhãn kế ⇒ BÌNH THƯỜNG, đây là cách vòng kết thúc
        //    (pager có ellipsis nên không đọc trước được tổng số trang, cứ thử tới khi trượt). Cảnh báo ở đây
        //    là cảnh báo mỗi shop mỗi lượt — đúng loại nhiễu mà trần cảnh báo sinh ra để chống. Chốt "đã duyệt
        //    hết chưa" nằm SAU vòng, dựa trên số trang pager thật sự cho thấy.
        //  · `pagerVang` = KHÔNG còn ul pager nào trong modal. Bình thường VỚI ĐIỀU KIỆN lúc mở modal cũng
        //    không có pager (`doModal.soTrang === 0` — shop ít trang); còn lúc mở CÓ mà giờ MẤT là hỏng hạ
        //    tầng giữa chừng. Phản biện vòng 4 (20/08): bản trước gộp ca này vào `khongThayTrang`, mà `coSan`
        //    rỗng cũng làm chốt sau vòng câm ⇒ shop 240 SP tạo chương trình 10 SP, không một dòng cảnh báo.
        //  · còn lại (mất modal, click trượt) = HỎNG HẠ TẦNG ⇒ luôn cảnh báo: shop còn sản phẩm chưa được
        //    chọn, mà bước vẫn xanh vì form đã có hàng.
        lyDoDung = (sang && sang.loi) || "không bấm được trang " + (trang + 1);
        const pagerVangBinhThuong = !!(sang && sang.pagerVang) && !((doModal && doModal.soTrang) > 0);
        if (!sang || (!sang.khongThayTrang && !pagerVangBinhThuong)) {
          themCanhBao("dung-som-pager",
            `dừng ở trang ${trang}/${soTrangThayDuoc || "?"} — ${lyDoDung}`);
        }
        break;
      }
      // Chờ bảng ĐỔI THẬT, không chờ mù theo thời gian.
      let doi = false;
      for (let i = 0; i < 20; i++) {
        await sleep(300);
        if ((await chay(pageVanTayBang, [])) !== vanTayTruoc) { doi = true; break; }
      }
      if (!doi) {
        // Click ăn mà bảng KHÔNG đổi sau 6 giây ⇒ hoặc trang mới chưa render (ta bỏ dở giữa chừng), hoặc modal
        // đã biến mất. Cả hai đều nghĩa là các trang còn lại KHÔNG được tick — luôn cảnh báo.
        lyDoDung = "bảng không đổi sau khi click trang " + (trang + 1);
        nhatKyTrang.push({ trang: trang + 1, loi: lyDoDung });
        themCanhBao("dung-som-bang-khong-doi", `dừng ở trang ${trang}/${soTrangThayDuoc || "?"} — ${lyDoDung}`);
        break;
      }
      trang++;
      // Chạm TRẦN = pager còn trang mà ta thôi đi ⇒ shop còn sản phẩm CHƯA chọn. Không được lặng lẽ `break`:
      // chương trình vẫn tạo được nên không có bước nào đỏ, người dùng sẽ không bao giờ biết bị thiếu hàng.
      if (trang > TRAN_TRANG_MODAL) { chamTran = true; }
    }
    if (chamTran) {
      themCanhBao("cham-tran",
        `chạm trần ${TRAN_TRANG_MODAL} trang trong modal chọn sản phẩm — shop có thể còn sản phẩm CHƯA được chọn`);
    }
    // CHỐT "đã duyệt HẾT trang chưa" — dùng chính số trang pager cho thấy (cận dưới, xem `soTrangLonNhat`).
    // Đây là thứ bắt được ca im lặng nhất: click trang trượt ngay ở trang 1 ⇒ chương trình tạo với 10 sản phẩm
    // của một shop 240 sản phẩm, không bước nào đỏ vì form VẪN có hàng.
    if (!chamTran && soTrangThayDuoc > trang) {
      themCanhBao("con-trang-chua-duyet",
        `mới duyệt ${trang}/${soTrangThayDuoc} trang của modal — sản phẩm ở các trang còn lại KHÔNG được chọn`
        + (lyDoDung ? ` (${lyDoDung})` : ""));
    }

    const dumpCuoi = await chay(pageDumpModalSanPham, []);
    // Xác nhận MODAL — chỉ NẠP sản phẩm vào form, CHƯA tạo chương trình (nút thanh đáy mới là bước 9).
    const selXn = await chay(pageXacNhanModal, []);
    await sleep(2500);
    // ⚠ Bản cũ đếm `document.querySelectorAll("tbody tr").length` — bảng sản phẩm của FORM KHÔNG có tbody/tr
    // (các dòng là div; thead=0, .eds-table=0) nên LUÔN ra 0 ⇒ bước này LUÔN báo hỏng, kể cả lượt 01:34 tạo
    // thành công 240 sản phẩm. Đếm bằng số ô "%GIẢM" ngoài khối .detail-batch — thứ đã đếm được thật (116 ô).
    const anhForm = await chay(pageAnhBangSanPham, []);
    const soDongForm = anhForm ? anhForm.soO : 0;

    return {
      selectorDung: selMo,
      truoc: { doModalKhiVuaMo: doModal },
      // `Math.min`: chạm trần thì `trang` đã ++ quá một nhịp (106) trong khi chỉ tick 105 trang.
      sau: { nhatKyTrang, soTrangDaDi: Math.min(trang, TRAN_TRANG_MODAL), soTrangThayDuoc, lyDoDung, chamTran,
             dumpTruocKhiXacNhan: dumpCuoi, nutXacNhanModal: selXn,
             soDongBangTrongForm: soDongForm, anhForm },
      kyVong: "1", docLai: String(soDongForm > 0 ? 1 : 0),
      ghiChu: "Kỳ vọng của CẢ BƯỚC: sau Xác nhận MODAL, form có ≥1 ô %GIẢM (= ≥1 sản phẩm). ĐỎ KHI: modal "
        + "không nạp được sản phẩm nào vào form. Riêng từng trang có phép đọc-lại RIÊNG, chỉ ra CẢNH BÁO chứ "
        + "không hạ ok cả bước: ô đếm 'Đã chọn' của modal phải TĂNG so với trang trước (xem nhatKyTrang[].so). "
        + "Cuối vòng còn so soTrangDaDi với soTrangThayDuoc (số trang pager cho thấy) — dừng sớm ⇒ CẢNH BÁO.",
    };
  }, tabId);

  // SHOP RỖNG ⇒ DỪNG TẠI ĐÂY, không tạo chương trình trống. Người dùng chốt từ đầu: "không có sản phẩm nào ⇒
  // bỏ shop đó, nghỉ 3–5 phút, sang shop khác". Nhánh này chỉ tới được khi bước 1–5 đều `ok` (mỗi bước hỏng đã
  // return ở trên) — tức KHÔNG còn đường nào biến một lỗi thao tác thành "shop rỗng".
  if (b6.shopRong) {
    return ketThuc({ boQua: "shop KHÔNG có sản phẩm nào — bỏ qua, không tạo chương trình" });
  }
  if (!b6.ok) { return ketThuc(); }
  const soSpSauModal = (b6.sau && b6.sau.anhForm && typeof b6.sau.anhForm.soO === "number") ? b6.sau.anhForm.soO : 0;

  // 7) Dump khối "Thiết lập hàng loạt" + thanh đáy — nay mới tồn tại vì SP đã vào form.
  const b7 = await buoc(ds, "7. Dump %GIẢM / Cập nhật hàng loạt / thanh đáy", async () => {
    // CHỜ form render xong danh sách sản phẩm rồi mới dò. Lượt 00:57 dò ngay sau khi đóng modal nên thấy
    // 0 bảng / 0 checkbox, trong khi lượt 00:55 (chậm hơn chút) đếm được 117 ô %GIẢM — tức chưa render, không
    // phải form không có. Đây đúng loại kết luận sai mà việc đo sớm gây ra.
    let bangForm = null;
    for (let i = 0; i < 30; i++) {
      bangForm = await chay(pageDoBangTrongForm, []);
      if (bangForm && (bangForm.soCheckboxNgoaiModal > 0 || (bangForm.khoiBang || []).length > 0)) break;
      await sleep(500);
    }
    const sau = await chay(pageDumpSauKhiCoSanPham, []);
    const dongSanPham = await chay(pageDumpDongSanPham, []);
    return { sau: Object.assign({}, sau, { bangForm, dongSanPham }), ok: true, ghiChu: "CHỈ ĐỌC. Bước này không khai kyVong nên ok = 'chạy xong không ném'." };
  }, tabId);
  if (!b7.ok) { return ketThuc({ soSanPham: soSpSauModal }); }

  // 8) Gõ % GIẢM rồi "Cập nhật hàng loạt", đọc lại bảng xem giá đã áp chưa.
  const b8 = await buoc(ds, `8. %GIẢM = ${phanTram} + Cập nhật hàng loạt`, async () => {
    const bangTruoc = await chay(pageAnhBangSanPham, []);
    // KHÔNG tick chọn sản phẩm. Người dùng chốt 19/08 01:2x: "nếu bấm vào cập nhật hàng loạt thì không phải
    // chọn sp, nếu chọn từng sp thì chỉ cập nhật cho các sp đã chọn" ⇒ không chọn gì = áp cho TẤT CẢ.
    // Suốt đêm tôi đuổi theo hướng "phải tick được dòng trong form" là đuổi nhầm; dòng "0 sản phẩm đã chọn"
    // là trạng thái BÌNH THƯỜNG, không phải lỗi.
    const tickForm = { boQua: "không cần chọn sản phẩm — cập nhật hàng loạt áp cho tất cả" };
    const go = await chay(pageGoPhanTram, [phanTram]);
    await sleep(600);
    const nutTruocKhiBam = await chay(pageTrangThaiNutBulk, []);
    const selNut = await chay(pageClickTheoSelector, [["button"], "cập nhật hàng loạt"]);
    await sleep(2500);
    const bangSau = await chay(pageAnhBangSanPham, []);
    const doi = JSON.stringify(bangTruoc) !== JSON.stringify(bangSau);
    return {
      selectorDung: selNut,
      truoc: { bangTruoc, tickForm, oPhanTram: go, nutTruocKhiBam },
      sau: { bangSau },
      kyVong: "DOI", docLai: doi ? "DOI" : "KHONG_DOI",
      ghiChu: "Kỳ vọng: bảng sản phẩm ĐỔI sau khi áp % hàng loạt. Không đổi ⇒ hoặc gõ nhầm ô, hoặc nút không ăn "
        + "bằng DOM thuần — cả hai đều là kết quả cần báo.",
    };
  }, tabId);
  if (!b8.ok) { return ketThuc({ soSanPham: soSpSauModal }); }

  // 9) Cuộn xuống → bấm "Xác nhận" ở thanh đáy. ĐÂY LÀ BƯỚC TẠO CHƯƠNG TRÌNH THẬT.
  //
  // Người dùng chỉ 19/08 01:3x: "sau khi bấm cập nhật hàng loạt rồi, phải nhích chuột xuống dưới 1 chút thì mới
  // có xác nhận" — thanh `.fix-container.fixed-bottom` chỉ hiện khi trang đã cuộn. Nên phải CUỘN rồi mới tìm nút.
  //
  // ⚠ Ở đây TỪNG có một lối ra "dừng trước nút Xác nhận" theo một cờ bật/tắt việc tạo, gửi kèm trong tham số.
  // ĐÃ XOÁ cả cờ lẫn lối ra (phản biện vòng 3, 19/08/2026): cả hai caller đều bật nó nên đó là nhánh CHẾT — mà
  // nếu sống lại thì lối ra ấy trả `boQua`, và C# gộp `boQua` vào câu "bỏ qua N shop không có sản phẩm", tức
  // một quyết định kỹ thuật đội lốt kết luận nghiệp vụ. `boQua` CHỈ dành cho shop rỗng thật; git giữ lịch sử.
  const b9 = await buoc(ds, "9. Cuộn + Xác nhận thanh đáy (TẠO THẬT)", async () => {
    const truoc = await chay(() => location.href);

    // Cuộn dần cho thanh đáy hiện ra, kiểm sau mỗi nhịp thay vì cuộn mù một phát.
    //
    // ⚠ ĐIỀU KIỆN CHỜ PHẢI RỘNG BẰNG DANH SÁCH SELECTOR LÚC CLICK. Bản đầu chờ đúng
    // `.fix-container.fixed-bottom .confirm-btn` trong khi lúc click lại chấp nhận cả `.fix-container .confirm-btn`
    // ⇒ chờ CHẶT HƠN click, nên bỏ cuộc trước khi kịp bấm. Lượt 01:44 (shop "Shop9x Store", CHỈ 1 sản phẩm)
    // chết vì đúng chỗ này: trang ngắn, không cuộn được, Shopee không gắn class `fixed-bottom` khi nội dung
    // vừa màn hình. Bài học chung: điều kiện chờ và điều kiện hành động phải nói về CÙNG một tập phần tử.
    const SEL_XACNHAN = ".fix-container .confirm-btn, .confirm-btn";
    let coNut = await chay(pageDemPhanTuHien, [SEL_XACNHAN]);
    for (let i = 0; i < 12 && !coNut; i++) {
      await chay(() => { window.scrollBy(0, 400); return 1; });
      await sleep(400);
      coNut = await chay(pageDemPhanTuHien, [SEL_XACNHAN]);
    }
    if (!coNut) {
      const nutTheoChu = await chay(() => {
        const hien = (el) => { const r = el.getBoundingClientRect(); return r.width > 0 || r.height > 0; };
        return Array.from(document.querySelectorAll("button")).filter(hien)
          .filter((b) => /xác nhận/i.test(b.textContent || ""))
          .map((b) => (b.className || "").slice(0, 60));
      });
      return { selectorDung: null, truoc, sau: truoc,
               ghiChu: "cuộn 12 nhịp vẫn KHÔNG thấy nút Xác nhận thanh đáy", nutTheoChu,
               kyVong: "/list/discount", kieuSo: "chua", docLai: truoc };
    }

    const sel = await chay(pageClickTheoSelector, [[".fix-container.fixed-bottom .confirm-btn", ".fix-container .confirm-btn", ".confirm-btn"], null]);

    // Đọc lại: tạo xong thì Shopee rời trang /discount/create về danh sách chương trình.
    //
    // ⚠ 40 → 120 nhịp (20s → 60s). Đêm 23/08/2026: ba shop LỚN (Minoa 105 trang, Cicily, Sully) bị chấm HỎNG ở
    // bước này, nhưng người dùng mở Shopee kiểm thì **chương trình ĐÃ ĐƯỢC TẠO**. Tức Shopee nhận đơn rồi vẫn
    // ở lại `/discount/create` lâu hơn 20 giây — càng nhiều sản phẩm càng lâu.
    // ÂM TÍNH GIẢ ở đây KHÔNG vô hại: `daTao = false` nên lượt chạy lại sẽ tạo chương trình THỨ HAI cho đúng
    // những shop đó.
    let sau = "";
    for (let i = 0; i < 120; i++) {
      await sleep(500);
      sau = await chay(() => location.href);
      if (String(sau || "").indexOf("/discount/create") < 0) break;
    }

    // ⚠ ĐỌC MỌI THÔNG BÁO TRÊN TRANG, KHÔNG CHỈ THÔNG BÁO LỖI — và phải ĐƯA ĐƯỢC RA NHẬT KÝ.
    // Bản trước chỉ quét `.eds-message, .eds-toast, .eds-form-item__error, .error-message` rồi nhét vào
    // `loiTrenTrang` — mà C# chỉ chuyển tiếp trường `loi`, nên ba lượt hỏng đêm 23/08 không để lại một chữ nào
    // về việc Shopee đã nói gì. Nếu Shopee báo THÀNH CÔNG mà ta vẫn chấm hỏng thì phải nhìn thấy được điều đó.
    const baoTrenTrang = await chay(() => {
      const gon = (s) => (s || "").replace(/\s+/g, " ").trim().slice(0, 140);
      const hien = (e) => !!e && e.getClientRects().length > 0;
      return Array.from(document.querySelectorAll(
        ".eds-message, .eds-toast, .eds-notification, .eds-form-item__error, .error-message, [class*='success']"))
        .filter(hien).map((e) => gon(e.textContent)).filter((t) => t).slice(0, 6);
    });

    // ⚠ ĐẶT VÀO `loi`, KHÔNG PHẢI `ghiChu`. C# chỉ chuyển tiếp `loi` lên nhật ký (`DocBuocHongDau`), còn
    // `ghiChu`/`sau`/`baoTrenTrang` rụng hết dọc đường — đúng cái bẫy đã làm ba lượt hỏng đêm 23/08/2026 không
    // để lại một chữ nào về việc Shopee đã nói gì. `buoc()` GIỮ `loi` của bước rồi mới nối "ĐỌC LẠI KHÔNG KHỚP"
    // vào sau (xem chỗ gán `ban.loi`), nên viết vào đây là ra được tới người đọc.
    const conOCreate = String(sau || "").indexOf("/discount/create") >= 0;
    const loiOLai = conOCreate
      ? "VẪN ở /discount/create sau 60s — Shopee nói: "
        + (baoTrenTrang.length ? baoTrenTrang.join(" | ") : "(KHÔNG có thông báo nào trên trang)")
        + ". ⚠ ĐÃ CÓ TIỀN LỆ (23/08/2026, Minoa Store): chương trình VẪN ĐƯỢC TẠO dù ở lại trang này — KIỂM "
        + "trên Shopee TRƯỚC khi chạy lại, kẻo tạo TRÙNG"
      : null;

    return { selectorDung: sel, truoc, sau, baoTrenTrang, loi: loiOLai,
             ghiChu: "Rời /discount/create ⇒ Shopee nhận. Ở lại KHÔNG chắc là hỏng — xem baoTrenTrang.",
             kyVong: "/list/discount", kieuSo: "chua", docLai: sau };
  }, tabId);

  return ketThuc({ soSanPham: soSpSauModal, daTao: !!b9.ok });
}

/**
 * Tham số MẶC ĐỊNH của lượt DÒ. Đặt ở đây chứ không nhét làm giá trị dự phòng trong
 * {@link chayKmChoTabDangMo}: luồng thật KHÔNG được có mặc định ngầm nào (đúng bài học `phanTram || 5`).
 *
 * ⚠ ĐI ĐÚNG KHOẢNG CỦA LUỒNG THẬT — hôm nay → hôm nay + 180, CÙNG một giờ ở hai đầu (26/08/2026). Đừng rút
 * ngắn lại cho "dò nhanh": chính bước 5 (ô KẾT THÚC) mới là bước phải bấm mũi tên ~6 lần, và
 * {@link pageBamChuyenThang} tự khai selector nút mũi tên "CHƯA dò thật". Rút khoảng về 0 là công cụ chẩn đoán
 * DUY NHẤT có 0% độ phủ trên đúng đoạn code bắt buộc — đúng cái bẫy đã sửa ngày 25/08/2026.
 *
 * ⚠ GIỜ đặt 23:00 chứ không phải 09:30: mốc BẮT ĐẦU nay là HÔM NAY, mà nút dò được bấm giữa ban ngày. Đặt
 * 09:30 thì mọi lượt dò sau 09:30 đều đỏ ở bước 4 vì mốc đã ở quá khứ — một cái đỏ vô nghĩa che mất mọi cái đỏ
 * đáng đọc. 23:00 còn ở tương lai cho gần trọn ngày làm việc.
 */
function thamSoLuotDo() {
  const bd = new Date();
  const kt = new Date();
  kt.setDate(kt.getDate() + NGAY_DAT_TRUOC_LUOT_DO);
  return {
    ten: "DO DOM KM " + Date.now(),
    bd: mocNgayLuotDo(bd),
    kt: mocNgayLuotDo(kt),
    gio: "23:00",
    phanTram: 5,
  };
}

/** Lệnh `dumpKm` — nút DÒ (chẩn đoán). Chạy đúng luồng thật rồi gửi nguyên nhật ký từng bước về app. */
export async function doDumpKm() {
  const kq = await chayKmChoTabDangMo(thamSoLuotDo());
  send({
    action: "pageData",
    kind: "kmDump",
    data: JSON.stringify({
      luc: new Date().toISOString(),
      ketQua: { soSanPham: kq.soSanPham, daTao: kq.daTao, boQua: kq.boQua, canhBao: kq.canhBao,
                captcha: !!kq.captcha },
      buoc: kq.buoc,
    }, null, 2),
  });
}

/**
 * Lệnh `chayKm` — LUỒNG THẬT: tạo chương trình cho shop đang mở theo tham số của form.
 * Gửi về một gói gọn để C# ghi nhật ký và quyết định đi tiếp hay dừng.
 * <para><b>`token` trả lại NGUYÊN VĂN</b> (C# gửi `shopId`): channel chỉ nhận gói khớp token của lượt đang
 * chờ. Không có nó thì một gói tới muộn của shop TRƯỚC sẽ được tính là kết quả của shop ĐANG chạy.</para>
 * <para><b>`captcha` trong gói</b> là lớp thứ hai bên cạnh `{action:"captcha"}` đã gửi ngay lúc bắt được: gói
 * `captcha` đi CÙNG luồng với kết quả nên không thể tới sau lượt C# đã đọc xong và đi tiếp shop kế.</para>
 */
export async function doChayKm(thamSo, token) {
  const kq = await chayKmChoTabDangMo(thamSo || {});
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
