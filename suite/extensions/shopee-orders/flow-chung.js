// Hạ tầng DÙNG CHUNG của ba luồng tạo chương trình (KM · Combo · Flash Sale).
//
// Mọi hàm trong file này DI CHUYỂN NGUYÊN VĂN từ `flow-km.js` ngày 29/08/2026 — thân KHÔNG đổi một byte.
// Luật "THÂN HÀM KHÔNG ĐƯỢC SỬA" ĐI THEO TỪNG HÀM: luồng KM đang chạy THẬT trên shop người dùng, và từ
// nay cả ba luồng dùng CHÍNH bản này — sửa một chữ ở đây là sửa cho cả ba, không còn bản chép thứ hai.

import { send } from "./core.js";

/// Trần số MỤC cảnh báo gửi về C# cho MỘT shop. Cảnh báo đi vào MỘT dòng nhật ký duy nhất (C# nối bằng " · "),
/// nên không có trần thì một thay đổi markup của Shopee đẻ ra 104 dòng "trang N: …" ≈ 9 KB đè lên đúng những
/// cảnh báo đáng đọc. Gom theo loại trước (xem {@link gomCanhBao}), còn dư thì cắt và nói rõ cắt bao nhiêu.
export const TRAN_CANH_BAO = 10;

// ── Hàm chạy TRONG TRANG ────────────────────────────────────────────────────────────────────────────────────
// LƯU Ý sống còn (xem exec.js): mỗi page-func được chrome.scripting serialize ĐỘC LẬP, KHÔNG kèm hàm nào ngoài
// nó. Mọi helper phải viết LẶP LẠI bên trong từng hàm — đừng "dọn cho gọn" bằng cách rút ra ngoài.

/** Bấm phần tử đầu tiên khớp selector (lọc theo chữ nếu có); trả selector đã ăn hoặc null.
 * <para>`export` cho `flow-combo.js` dùng lại (25/08/2026) — hàm THUẦN DOM, không mang selector riêng của luồng
 * KM nên dùng chung được. THÂN HÀM KHÔNG ĐƯỢC SỬA: luồng KM đang chạy thật trên shop người dùng.</para> */
export function pageClickTheoSelector(dsSelector, chuCanCo) {
  const na = (s) => (s || "").replace(/\s+/g, " ").trim().toLowerCase();
  const can = na(chuCanCo);
  for (const sel of dsSelector) {
    let els = [];
    try { els = Array.from(document.querySelectorAll(sel)); } catch (e) { continue; }
    for (const el of els) {
      if (can && na(el.textContent).indexOf(can) < 0) continue;
      const r = el.getBoundingClientRect();
      if (r.width === 0 && r.height === 0) continue;
      el.click();
      return sel;
    }
  }
  return null;
}

/** Đếm phần tử ĐANG HIỆN khớp selector — để CHỜ trang render xong rồi mới thao tác, thay cho sleep mù.
 * <para>`export` cho `flow-combo.js` dùng lại (25/08/2026) — xem ghi chú ở {@link pageClickTheoSelector}.</para> */
export function pageDemPhanTuHien(sel) {
  let els = [];
  try { els = Array.from(document.querySelectorAll(sel)); } catch (e) { return 0; }
  return els.filter((el) => { const r = el.getBoundingClientRect(); return r.width > 0 || r.height > 0; }).length;
}

// ── Hàm PURE (không đụng DOM) ───────────────────────────────────────────────────────────────────────────────

/**
 * PURE — số trang LỚN NHẤT đọc được trên pager (danh sách nhãn `["1","2","3","…","24"]` ⇒ 24). Không có nhãn số
 * nào ⇒ 0.
 * <b>Đây là CẬN DƯỚI của tổng số trang, cố ý:</b> pager có ellipsis nên nhãn hiện ra chỉ là một phần. Cận dưới
 * thì phép so "đi được mấy trang / có mấy trang" chỉ có thể BỎ SÓT cảnh báo, không bao giờ báo oan — và báo oan
 * mỗi shop mỗi lượt là cách nhanh nhất để người dùng thôi đọc cảnh báo.
 * <para>`export` cho `flow-combo.js` dùng lại (25/08/2026) — pager của combo cũng có ô "…". THÂN KHÔNG SỬA.</para>
 */
export function soTrangLonNhat(coSan) {
  let lon = 0;
  for (const nhan of coSan || []) {
    const n = Number(String(nhan).trim());
    if (Number.isInteger(n) && n > lon) { lon = n; }
  }
  return lon;
}

/**
 * PURE — GOM cảnh báo cùng loại thành một mục có ĐẾM, rồi chặn TRẦN tổng số mục.
 *
 * <b>Vì sao cần</b> (phản biện vòng 3, 19/08/2026): mọi cảnh báo của một shop đi vào ĐÚNG MỘT dòng nhật ký
 * (C# nối bằng " · "). Shopee đổi chuỗi trong `.select-tips` là 104 trang cùng đẻ một cảnh báo giống hệt nhau
 * ⇒ một dòng ~9 KB, và cảnh báo THẬT (vd "chạm trần 105 trang") bị dìm ở giữa đống đó.
 *
 * @param {Array<{loai:string, cau:string, gom?:string, chiTiet?:string}|string>} ds Cảnh báo thô theo thứ tự.
 * @param {number} tran Trần số MỤC sau khi gom.
 * @returns {string[]} Mục nào chỉ có 1 lần thì giữ NGUYÊN câu đầy đủ; nhiều lần thì "N lần — <gom> (chi tiết)".
 * <para>`export` cho `flow-combo.js` dùng lại (25/08/2026): hàm PURE, không mang selector nào — hai luồng gom
 * cảnh báo theo hai luật khác nhau thì bản ít chạy sẽ mục dần. THÂN HÀM KHÔNG ĐƯỢC SỬA.</para>
 */
export function gomCanhBao(ds, tran) {
  const gioiHan = Number.isInteger(tran) && tran > 0 ? tran : TRAN_CANH_BAO;
  const thuTu = [];
  const theoLoai = new Map();
  for (const c of ds || []) {
    const m = (c && typeof c === "object") ? c : { loai: String(c), cau: String(c) };
    const khoa = m.loai || m.cau;
    if (!theoLoai.has(khoa)) {
      theoLoai.set(khoa, { so: 0, cau: m.cau, gom: m.gom || m.cau, chiTiet: [] });
      thuTu.push(khoa);
    }
    const nhom = theoLoai.get(khoa);
    nhom.so++;
    if (m.chiTiet) { nhom.chiTiet.push(String(m.chiTiet)); }
  }
  const dong = thuTu.map((khoa) => {
    const n = theoLoai.get(khoa);
    if (n.so === 1) { return n.cau; }
    const dau = n.chiTiet.slice(0, 5).join(", ") + (n.chiTiet.length > 5 ? ", …" : "");
    return `${n.so} lần — ${n.gom}` + (n.chiTiet.length ? ` (${dau})` : "");
  });
  if (dong.length <= gioiHan) { return dong; }
  return dong.slice(0, gioiHan).concat([`… + ${dong.length - gioiHan} cảnh báo nữa (đã gom theo loại)`]);
}

// ── Hàm PURE SONG SINH đã gom (lật quyết định 29/08/2026) ───────────────────────────────────────────────────
// Tới 29/08/2026 `flow-km.js` và `flow-combo.js` cố ý KHÔNG import chéo nhau, nên ba hàm dưới đây tồn tại HAI
// bản chép tay. Lật quyết định đó cho ĐÚNG BA HÀM NÀY — chúng THUẦN, không đụng DOM, không mang selector của
// bộ component nào — và gom qua module thứ ba này; đó KHÔNG phải import chéo, nên luật cũ vẫn đứng nguyên cho
// phần còn lại. Lý do gom: bản ít chạy sẽ mục dần (bản combo đã lệch chữ trong xmldoc so với bản KM).
//
// VẪN CỐ Ý SONG SINH — ĐỪNG GOM: `chuyenToiThang`, `datOThoiGian`, `thamSoLuotDo`, và MỌI page-func trùng tên.
// KM là Vue (`eds-*`), combo là React (`eds-react-*`): hai bộ component khác nhau, thân hàm khác THẬT (lỗi
// 12/12 shop sáng 27/08/2026 sinh từ hai bản luật trôi lệch). Riêng `datOThoiGian` hai bản còn khác HÀNH VI —
// bản KM có guard "ngày hỏng ⇒ KHÔNG bấm Xác nhận", bản combo chưa có — nên gom là ĐỔI HÀNH VI, không phải
// refactor.

/**
 * ĐỘ DÀI KHOẢNG của lượt dò, tính bằng ngày — phải bằng `KmLichTuDong.SoNgayDatTruoc` bên C# (180). Hai bên
 * lệch nhau thì lượt dò đi một đường còn luồng thật đi một đường khác, tức nút dò thôi chẩn đoán được cho
 * luồng thật.
 */
export const NGAY_DAT_TRUOC_LUOT_DO = 180;

/**
 * PURE — đọc + KIỂM **một** mốc ngày trong gói (khuôn `KhuonNgayGoi` bên C#: `ngayTrongThang` + `thang` +
 * `nam` + `ngayDayDu`). Trả giá trị đã chuẩn hoá kèm `loi` (chuỗi) hoặc `loi: null`.
 *
 * Ba số rời là thứ dùng để CHUYỂN THÁNG rồi CLICK ô ngày; `ngayDayDu` là chuỗi đi vào phép ĐỌC LẠI. Hai vế
 * lệch nhau thì mọi bước vẫn xanh mà chương trình mọc sai ngày — nên chúng được đối chiếu ngay tại đây.
 */
export function docMocNgay(o) {
  const hai = (n) => String(n).padStart(2, "0");
  const nguon = (o !== null && typeof o === "object") ? o : {};

  const ngayTrongThang = Number(nguon.ngayTrongThang);
  const thang = Number(nguon.thang);
  const nam = Number(nguon.nam);
  const soNgayOk = Number.isInteger(ngayTrongThang) && ngayTrongThang >= 1 && ngayTrongThang <= 31
    && Number.isInteger(thang) && thang >= 1 && thang <= 12
    && Number.isInteger(nam) && nam >= 2000 && nam <= 2999;
  let loi = null;
  if (!soNgayOk) {
    loi = "ngày/tháng/năm không hợp lệ: ngayTrongThang='" + nguon.ngayTrongThang
      + "', thang='" + nguon.thang + "', nam='" + nguon.nam + "'";
  }

  // `ngayDayDu` là chuỗi ĐI VÀO phép đọc-lại `HH:mm dd-MM-yyyy`, còn ngày/tháng/năm rời là thứ dùng để CLICK.
  // Hai vế lệch nhau thì mọi bước vẫn xanh mà chương trình sai ngày ⇒ đối chiếu chúng ngay tại đây.
  const ngayDayDu = typeof nguon.ngayDayDu === "string" && /^\d{2}-\d{2}-\d{4}$/.test(nguon.ngayDayDu)
    ? nguon.ngayDayDu : null;
  if (!ngayDayDu) {
    loi = loi || `thiếu/sai 'ngayDayDu' (cần dd-MM-yyyy): '${nguon.ngayDayDu}'`;
  } else if (soNgayOk) {
    const mong = hai(ngayTrongThang) + "-" + hai(thang) + "-" + nam;
    if (ngayDayDu !== mong) {
      loi = loi || `'ngayDayDu'='${ngayDayDu}' KHÔNG khớp ngày/tháng/năm rời (${mong})`;
    }
  }

  return { ngayTrongThang, thang, nam, ngayDayDu, loi };
}

/** PURE — khuôn bốn trường của MỘT mốc ngày, đúng `KhuonNgayGoi.Goi` bên C#. */
export function mocNgayLuotDo(t) {
  const hai = (n) => String(n).padStart(2, "0");
  return {
    ngayTrongThang: t.getDate(),
    thang: t.getMonth() + 1,
    nam: t.getFullYear(),
    ngayDayDu: hai(t.getDate()) + "-" + hai(t.getMonth() + 1) + "-" + t.getFullYear(),
  };
}

/**
 * PURE — câu lỗi gộp của bước giả "0. Tham số": nêu ĐỦ mọi trường hỏng trong một lượt, không phải từng cái một.
 * Sạch tham số ⇒ null.
 *
 * ⚠ `loi` là OBJECT LỖI THEO TRƯỜNG (`{ten, ngayBd, ngayKt, gio, phanTram, …}`), KHÔNG phải chuỗi. Đường chạy
 * SẠCH hằng đêm truyền `{}` ⇒ hàm PHẢI trả `null`. Đừng "đơn giản hoá" thành nối chuỗi `+ loi`: làm vậy thì
 * mọi lượt sạch đỏ với câu "… — [object Object]".
 *
 * <b>`nhan`</b> là chữ phân biệt luồng trong câu lỗi ("KM" / "Combo"). Gom hai bản song sinh 29/08/2026 bằng
 * cách THAM SỐ HOÁ đúng chữ đó — thân hàm giữ nguyên từng byte của cả hai bản cũ, chuỗi ra không đổi một ký tự.
 *
 * <para>Vì sao gộp và báo NGAY ĐẦU (phản biện vòng 3): tầng đọc tham số biết trường nào hỏng từ TRƯỚC khi đụng
 * trang, nhưng bản trước để bước 5/8 mới báo — nghĩa là một `phanTram` sai vẫn tốn ~12 phút lật 105 trang modal
 * rồi mới đỏ, và bỏ lại một form dở dang trên shop thật. Sai tham số thì không có gì để làm trên trang cả.</para>
 */
export function cauLoiThamSo(loi, nhan) {
  // HỎNG TO TIẾNG khi thiếu nhãn (phản biện 29/08): nhãn vốn nằm CỨNG trong thân hai bản cũ nên không thể sai;
  // tham-số-hoá mở ra ca quên truyền — mà "tham số lượt undefined KHÔNG dùng được" lên nhật ký là chẩn đoán mù.
  if (!nhan) { throw new Error("cauLoiThamSo: thiếu nhãn luồng (KM/Combo/…) — chỗ gọi phải truyền tường minh"); }
  const ds = Object.keys(loi || {}).map((k) => `${k}: ${loi[k]}`);
  return ds.length === 0 ? null : "tham số lượt " + nhan + " KHÔNG dùng được — " + ds.join(" · ");
}

// ── Khung chạy từng bước ────────────────────────────────────────────────────────────────────────────────────

/** URL hiện tại của một tab (rỗng nếu tab đã chết). */
async function urlCuaTab(tabId) {
  try { return (await chrome.tabs.get(tabId)).url || ""; } catch (e) { return ""; }
}

/**
 * Chạy một bước và CHẤM nó.
 * <b>Luật `ok` (ràng buộc (3) đầu file):</b> bước nào khai `kyVong` thì `ok` do KHUNG quyết định bằng cách so
 * `docLai` với `kyVong` — thân bước KHÔNG tự khai `ok` được nữa. Không có `kyVong` thì đó là bước CHỈ ĐỌC
 * (dump), lúc ấy `ok` mới mang nghĩa "chạy xong không ném".
 *
 * <b>CHỐT CAPTCHA HAI ĐẦU: trước khi chạy bước, VÀ khi bước ra `ok:false`.</b> Rơi `/verify` giữa luồng thì mọi
 * thao tác sau đó chạy trên trang xác minh: vô nghĩa, và là tự khai bot. Bắt được thì báo `{action:"captcha"}`
 * như các flow khác đang làm, đánh dấu `captcha:true` để vòng gọi DỪNG HẲN (C# kiểm `CaptchaSeen` sau chặng
 * chayKm của từng shop, và còn đọc thêm cờ `captcha` trong gói kết quả).
 * <para><b>Vì sao cần chốt THỨ HAI</b> (phản biện vòng 2, 19/08/2026): bước hỏng là vòng gọi `return ketThuc()`
 * NGAY, nên KHÔNG còn bước nào chạy prologue để bắt captcha — mà ca phổ biến nhất lại đúng là rơi verify GIỮA
 * một bước dài (bước 6 lật tới 105 trang, ~12 phút). Chỉ có chốt đầu bước thì lượt captcha giữa bước không bao
 * giờ bắn `{action:"captcha"}`, và vòng đi tiếp 11 shop nữa trên một phiên Shopee đang nghi ngờ.</para>
 * <para>`export` cho `flow-combo.js` dùng lại (25/08/2026): luật chấm bước + hai chốt captcha là phần ĐẮT nhất
 * của module này, chép bản thứ hai là hai bản trôi lệch. THÂN HÀM KHÔNG ĐƯỢC SỬA — luồng KM đang chạy thật.
 * <para><b>`nhan`</b> là tiền tố của dòng `progress`. MẶC ĐỊNH `"dò KM"` nên hành vi của luồng KM KHÔNG
 * đổi một ký tự; luồng combo truyền `"Combo"`. Thêm tham số CÓ MẶC ĐỊNH là cách duy nhất sửa được dòng
 * `[dò KM]` mọc trên nhật ký của lượt combo (ca thật 13:28 ngày 26/08/2026 — người đọc log tưởng đang
 * xem lượt KM) mà không phải đụng vào thân hàm của luồng đang chạy thật.</para>
 */
export async function buoc(ds, ten, than, tabId, nhan = "dò KM") {
  const ban = { ten, ok: false, selectorDung: null, kyVong: null, kieuSo: null, docLai: null, truoc: null, sau: null, loi: null, ghiChu: null };
  if (tabId != null) {
    const url = await urlCuaTab(tabId);
    if (/\/verify/i.test(url)) {
      ban.captcha = true;
      ban.loi = "rơi trang verify/captcha trước khi chạy bước: " + url;
      ds.push(ban);
      send({ action: "captcha", message: url });
      send({ action: "progress", message: `[KM] ${ten}: DỪNG — ${ban.loi}` });
      return ban;
    }
  }
  try {
    Object.assign(ban, (await than(ban)) || {});
    if (ban.kyVong !== null && ban.kyVong !== undefined) {
      // MẶC ĐỊNH SO BẰNG. Trước 18/08/2026 chỗ này so "có chứa", nên bước 8 khai kỳ vọng 'doi' mà đọc lại
      // 'khong-doi' vẫn được tính là KHỚP ("khong-doi" chứa "doi") ⇒ báo ok cho một bước THẤT BẠI.
      // Bước nào thật sự cần so "chứa" (vd URL) phải khai tường minh kieuSo:"chua". ĐỪNG đổi mặc định lại.
      const soChua = ban.kieuSo === "chua";
      const khop = soChua
        ? String(ban.docLai || "").indexOf(String(ban.kyVong)) >= 0
        : String(ban.docLai || "") === String(ban.kyVong);
      if (!khop) {
        ban.ok = false;
        ban.loi = (ban.loi ? ban.loi + " · " : "")
          + "ĐỌC LẠI KHÔNG KHỚP: kỳ vọng " + (soChua ? "chứa" : "bằng") + " '" + ban.kyVong
          + "', đọc được '" + (ban.docLai || "(rỗng)") + "'";
      } else {
        ban.ok = true;
      }
    }
  } catch (e) {
    ban.ok = false;
    ban.loi = String((e && e.message) || e);
  }
  // CHỐT CAPTCHA LẦN HAI — nhánh bước HỎNG (xem xmldoc). Chỉ hỏi URL khi bước đã đỏ nên không thêm chi phí cho
  // đường xanh; hỏi xong mới `ds.push` để gói bước mang luôn cờ `captcha`.
  if (!ban.ok && tabId != null && !ban.captcha) {
    const urlSau = await urlCuaTab(tabId);
    if (/\/verify/i.test(urlSau)) {
      ban.captcha = true;
      ban.loi = (ban.loi ? ban.loi + " · " : "") + "rơi trang verify/captcha GIỮA bước: " + urlSau;
      send({ action: "captcha", message: urlSau });
    }
  }
  ds.push(ban);
  send({ action: "progress", message: `[${nhan}] ${ten}: ${ban.ok ? "ok" : "KHÔNG ăn"}${ban.loi ? " — " + ban.loi : ""}` });
  return ban;
}
