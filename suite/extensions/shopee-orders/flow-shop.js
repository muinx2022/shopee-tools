// Lệnh cấp SHOP: SSO sang Seller Centre → trang chọn shop → mở "Chi tiết" một shop → đọc "Chờ Lấy Hàng"
// → đóng tab shop về picker. Chạy TRƯỚC mọi việc cấp đơn (flow-orders/flow-address/flow-returns).
import { ctx, send, ensureListTab, LOI_MAT_TAB_SHOP } from "./core.js";
import { execInTab } from "./exec.js";
import { BANHANG_HOSTS, SUBACCOUNT_HOSTS, SHOP_LIST_URL } from "./constants.js";
import {
  pageIsLoginForm, pageLocateByText, pageDumpClickables, pageShopRowCount,
  pageScanShopList, pageReadToShip, pageScrollDetailIntoView, pageLocateDetailRect,
} from "./page-funcs.js";
import { sleep } from "./shared/util.js";
import { waitForTabComplete } from "./shared/tab-wait.js";
import { dbgDetach, ensureDbg, trustedClick } from "./shared/dbg-input.js";

// Ghi ctx.shopTabId + LƯU BỀN vào chrome.storage.session. Vì sao phải bền: `ctx` nằm trong bộ nhớ service
// worker, mà SW MV3 bị giết/dựng lại liên tục giữa hai shop; SW mới có shopTabId = null nên MỌI lệnh cấp đơn
// sau đó không còn tab shop (trước 11/08/2026 chúng lùi im lặng về tab picker và thao tác THẬT lên shop sai).
// Đi qua ĐÚNG một hàm này để không có đường nào gán ctx.shopTabId mà quên lưu.
function nhoTabShop(tabId) {
  // Tăng THẾ HỆ trước khi ghi: `khoiPhucTabShop` (background.js) đang validate dở giữa chừng sẽ thấy thế hệ
  // đổi và BỎ kết quả — ghi chủ động ở đây luôn thắng bản khôi phục từ storage (vốn có thể đã cũ vài ms).
  ctx.theHeTabShop++;
  ctx.shopTabId = tabId;
  try { chrome.storage.session.set({ shopTabId: tabId }); } catch (e) {}
}

// Dọn tab banhang MỒ CÔI ngay trước khi bấm "Chi tiết".
//
// Vì sao cần: đường hết giờ 30s bên dưới `return` mà KHÔNG gọi `nhoTabShop`, nên một tab shop mở ở giây thứ 31
// không ai còn cầm id — `doCloseShopTab` thấy `ctx.shopTabId == null` nên chỉ điều hướng picker chứ không đóng
// nó. Tab thừa đó ở lại mãi, và vì `ensureListTab` lấy tab khớp CUỐI CÙNG (SW MV3 chết theo chu kỳ nên gần như
// lần nào cũng phải dò lại), `listTabId` có thể bám thẳng vào nó ⇒ cú click "Chi tiết" kế tiếp bắn vào tab sai
// và lượt chờ 30s lại hết giờ. Đúng cái vòng xoáy làm 18 lỗi trong 8 ngày gom thành 6 chuỗi 3 shop liên tiếp.
//
// CHỈ đóng tab khác CẢ `listTabId` LẪN `shopTabId`: `shopTabId` đáng lẽ đã null ở đây (shop trước đã đóng tab),
// nhưng SW vừa dựng lại có thể vừa khôi phục id từ storage — đóng nhầm nó là tự cắt tay mình.
// Đóng được cái nào thì NÓI RA (`progress` chỉ để log): dọn im lặng thì lần sau không ai biết đã dọn gì.
async function donTabShopMoCoi() {
  let tabs;
  try { tabs = await chrome.tabs.query({ url: "https://banhang.shopee.vn/*" }); } catch (e) { return; }
  const daDong = [];
  for (const t of tabs) {
    if (t.id === ctx.listTabId || t.id === ctx.shopTabId) continue;
    try { await chrome.tabs.remove(t.id); daDong.push(t.url || t.pendingUrl || String(t.id)); } catch (e) {}
  }
  if (daDong.length) {
    send({
      action: "progress",
      message: "dọn " + daDong.length + " tab banhang MỒ CÔI trước khi bấm Chi tiết: [" + daDong.join(" | ") + "]",
    });
  }
}

// GĐ4: đóng tab shop hiện tại rồi VỀ picker /portal/shop (giữa các shop). Shop thường mở ở TAB RIÊNG (shopTabId
// khác listTabId) → chỉ đóng tab đó, picker (listTabId) còn nguyên. Nếu shop mở CÙNG tab picker → điều hướng
// listTabId về /portal/shop. Cuối cùng poll tr[data-row-key] để chắc chắn picker sẵn sàng cho shop kế.
export async function doCloseShopTab() {
  if (ctx.shopTabId != null && ctx.shopTabId !== ctx.listTabId) {
    try { await chrome.tabs.remove(ctx.shopTabId); } catch (e) {}
    nhoTabShop(null);
  } else if (ctx.listTabId != null) {
    // Shop mở cùng tab picker (hoặc không rõ) → đưa picker về /portal/shop.
    try { await chrome.tabs.update(ctx.listTabId, { url: SHOP_LIST_URL }); } catch (e) {}
    await waitForTabComplete(ctx.listTabId, 20000);
    nhoTabShop(null);
  }
  if (ctx.listTabId == null) { send({ action: "shopTabClosed", ok: false }); return; }
  const st = await ensureShopPicker(ctx.listTabId); // "ok" | "verify" | "stuck"
  if (st === "verify") {
    let u = "";
    try { u = (await chrome.tabs.get(ctx.listTabId)).url || ""; } catch (e) {}
    send({ action: "captcha", message: u });
    return;
  }
  send({ action: "shopTabClosed", ok: st === "ok" });
}

// SSO từ trang tài khoản subaccount (/account, đã đăng nhập nhờ cookie) → "Kênh Người bán" → tab banhang →
// trang CHỌN SHOP (/portal/shop, picker tất-cả-shop — né sticky-shop server-side khi mở thẳng /portal/shop).
export async function gotoSellerCentre() {
  if (!(await ensureListTab(SUBACCOUNT_HOSTS))) {
    send({ action: "error", message: "chưa thấy tab subaccount/account để mở Kênh Người bán — SW thấy: [" + ctx.lastTabUrls.join(" | ") + "]" });
    return;
  }

  // Trang /account có thể ra FORM LOGIN nếu cookie hết hạn — bản sạch KHÔNG tự điền (đã bỏ khi pivot GĐ2).
  let isLogin = false;
  try { isLogin = await execInTab(ctx.listTabId, pageIsLoginForm, []); } catch (e) {}
  if (isLogin) {
    send({ action: "error", message: "bản sạch gặp trang đăng nhập subaccount (cookie hết hạn) — cần đăng nhập lại" });
    return;
  }

  // Trên /account (đã đăng nhập) → tìm "Kênh Người bán". POLL ~10s (SPA render trễ); mỗi vòng thử thêm click
  // "Tài khoản của tôi" (có thể là menu xổ ra chứa entry). Không thấy → DUMP các mục bấm được để biết nhãn thật.
  const sellerSel = ["span.entry-text", ".entry", "a", "span", "div", "[role='button']", "[role='menuitem']", "li"];
  const sellerRe = "kenh nguoi ban|seller\\s*cent(re|er)|seller\\s*channel|nguoi ban";
  let seller = null;
  let triedAcc = false;
  const sdl = Date.now() + 10000;
  while (Date.now() < sdl) {
    try { seller = await execInTab(ctx.listTabId, pageLocateByText, [sellerSel, sellerRe]); } catch (e) { seller = null; }
    if (seller) break;
    if (!triedAcc) {
      const acc = await execInTab(ctx.listTabId, pageLocateByText, [["li", "a", "div", "span", "[role='menuitem']"], "tai khoan cua toi|my account"]);
      if (acc) { await trustedClick(ctx.listTabId, acc.x, acc.y); await sleep(1500); }
      triedAcc = true;
    }
    await sleep(600);
  }
  if (!seller) {
    let dump = "[]"; try { dump = await execInTab(ctx.listTabId, pageDumpClickables, []); } catch (e) {}
    let u = ""; try { u = (await chrome.tabs.get(ctx.listTabId)).url || ""; } catch (e) {}
    send({ action: "error", message: "không thấy 'Kênh Người bán' trên " + u + " — các mục bấm được: " + dump });
    return;
  }

  const before = (await chrome.tabs.query({ url: "https://banhang.shopee.vn/*" })).map((t) => t.id);
  const subTabId = ctx.listTabId;
  await trustedClick(ctx.listTabId, seller.x, seller.y);

  // Theo tab banhang MỚI hoặc tab subaccount tự điều hướng sang banhang.
  const deadline = Date.now() + 90000;
  let found = null;
  while (Date.now() < deadline) {
    const tabs = await chrome.tabs.query({ url: "https://banhang.shopee.vn/*" });
    const cand = tabs.find((t) => before.indexOf(t.id) === -1);
    if (cand) { found = cand; break; }
    try {
      const lt = await chrome.tabs.get(subTabId);
      if (lt && lt.url && lt.url.indexOf("banhang.shopee.vn") >= 0) { found = lt; break; }
    } catch (e) {}
    await sleep(600);
  }
  if (!found) { send({ action: "error", message: "bấm 'Kênh Người bán' xong chờ 90s chưa thấy Seller Centre" }); return; }

  ctx.listTabId = found.id;
  nhoTabShop(null);
  // Áp lệnh chặn SDK chat NGAY khi biết tab Seller Centre, TRƯỚC lúc chờ nó load xong: chặn kịp thì
  // chateasy/minichat không bao giờ được nạp trên tab này. Nuốt lỗi sẵn bên trong, không phá luồng.
  await ensureDbg(ctx.listTabId);
  await waitForTabComplete(ctx.listTabId, 15000);
  let url = "";
  try { url = (await chrome.tabs.get(ctx.listTabId)).url || ""; } catch (e) {}
  if (/\/verify/i.test(url)) { send({ action: "captcha", message: url }); return; }

  // Đảm bảo VỀ ĐƯỢC picker /portal/shop (poll tr[data-row-key]); có thể vẫn bị sticky-redirect → điều hướng lại 1 lần.
  const st = await ensureShopPicker(ctx.listTabId);
  if (st === "verify") {
    let u = "";
    try { u = (await chrome.tabs.get(ctx.listTabId)).url || ""; } catch (e) {}
    send({ action: "captcha", message: u });
    return;
  }
  if (st !== "ok") {
    send({ action: "error", message: "không về được trang chọn shop (/portal/shop) — có thể vẫn dính shop cũ (sticky) hoặc bảng chưa render" });
    return;
  }

  // Picker OK → đóng tab subaccount (nếu là tab riêng) rồi báo sẵn sàng.
  if (subTabId !== ctx.listTabId) { try { await chrome.tabs.remove(subTabId); } catch (e) {} }
  send({ action: "atSellerCentre" });
}

// Về/chờ trang chọn shop: poll tr[data-row-key] tới ~30s; nếu chưa ở /portal/shop thì điều hướng lại MỘT lần.
// Trả "ok" (thấy bảng shop) | "verify" (rơi trang xác minh) | "stuck" (hết giờ, có thể vẫn dính shop cũ).
async function ensureShopPicker(tabId) {
  const overall = Date.now() + 30000;
  let navigated = false;
  while (Date.now() < overall) {
    let u = "";
    try { u = (await chrome.tabs.get(tabId)).url || ""; } catch (e) {}
    if (/\/verify/i.test(u)) return "verify";
    let n = 0;
    try { n = (await execInTab(tabId, pageShopRowCount, [])) || 0; } catch (e) { n = 0; }
    if (n > 0) return "ok";
    if (u.indexOf("/portal/shop") < 0 && !navigated) {
      navigated = true;
      try { await chrome.tabs.update(tabId, { url: SHOP_LIST_URL }); } catch (e) {}
      await waitForTabComplete(tabId, 20000);
      continue;
    }
    await sleep(700);
  }
  return "stuck";
}

// Đọc danh sách shop.
export async function doReadShopList() {
  if (!(await ensureListTab(BANHANG_HOSTS))) { send({ action: "error", message: "chưa thấy tab /portal/shop — SW thấy các tab: [" + ctx.lastTabUrls.join(" | ") + "]" }); return; }
  // Poll chờ bảng shop render (tr[data-row-key]) — production chờ tới ~20s; ở đây 15s. Đọc một phát dễ trúng lúc
  // bảng CHƯA render → 0 shop.
  const deadline = Date.now() + 15000;
  let json = "[]";
  while (Date.now() < deadline) {
    try { json = (await execInTab(ctx.listTabId, pageScanShopList, [])) || "[]"; } catch (e) { json = "[]"; }
    let n = 0; try { n = JSON.parse(json).length; } catch (e) {}
    if (n > 0) break;
    await sleep(500);
  }
  send({ action: "pageData", kind: "shopList", data: json });
}

// Nút "Mở list shop" (C#): NHẢ chrome.debugger khỏi tab picker rồi thôi. Chỉ chạy ở nước cờ CUỐI của lượt đó —
// C# đọc xong danh sách shop, buông cửa sổ lại cho người dùng thao tác tay và đóng luôn kênh WS.
//
// Vì sao cần: `gotoSellerCentre` attach debugger vào tab picker (ensureDbg) và CỐ Ý không detach — giữ attach
// xuyên suốt thì cú click trusted không trượt. Nhưng cửa sổ để lại cho NGƯỜI DÙNG mà còn đeo banner "Brave đang
// gỡ lỗi trình duyệt này" thì vừa chướng vừa dễ bị bấm nhầm "Huỷ".
//
// KHÔNG gửi phản hồi: phía C# cố ý không Arm chặng nào cho lệnh này (best-effort, nghỉ 300ms rồi đi tiếp).
//
// ⚠ Biến `_dbgTab` bên dbg-input.js KHÔNG được xoá theo (chrome.debugger.onDetach chỉ bắn khi TRÌNH DUYỆT tự
// ngắt, không bắn cho lượt detach chủ động). Chấp nhận được vì đây là lệnh CUỐI của phiên: sau nó C# đóng kênh,
// và cửa sổ để lại luôn bị đóng trước khi có phiên mới (AccountsViewModel.TryKillPoc + FreeProfile). Nếu sau này
// có ai gửi thêm lệnh cần trusted click SAU lệnh này, phải xoá `_dbgTab` — không thì ensureDbg tưởng còn attach.
export async function doDetachDebugger() {
  const tabId = ctx.listTabId;
  if (tabId == null) { return; }
  try { await dbgDetach({ tabId }); } catch (e) {}
}

// GĐ1: đọc số "Chờ Lấy Hàng". KHÔNG lùi về tab picker khi mất shopTabId — cùng luật `orderTabIdStrict`
// (core.js): trên tab picker thì hoặc treo 8s rồi trả null (che mất chẩn đoán), hoặc tệ hơn là đọc số của shop
// sticky SAI. Thà bỏ lượt: C# nhận `error` → fault chặng ToShip → shop ghi lỗi, vòng sau chạy lại từ đầu shop.
export async function doReadToShip() {
  const tabId = ctx.shopTabId;
  if (tabId == null) { send({ action: "error", message: LOI_MAT_TAB_SHOP }); return; }
  const deadline = Date.now() + 8000;
  let raw = null;
  while (Date.now() < deadline) {
    try { raw = await execInTab(tabId, pageReadToShip, []); } catch (e) { raw = null; }
    if (raw != null) break;
    await sleep(400);
  }
  send({ action: "pageData", kind: "toShip", data: raw });
}

// GĐ1: mở "Chi tiết" shop đầu bằng trusted click, theo tab shop mới.
export async function openShopDetail(shopId) {
  if (!(await ensureListTab(BANHANG_HOSTS))) { send({ action: "error", message: "chưa thấy tab /portal/shop — SW thấy các tab: [" + ctx.lastTabUrls.join(" | ") + "]" }); return; }

  // Sau thời gian NGHỈ giữa shop (3–5'), picker có thể đã drift: sticky-redirect về trang đơn của shop trước,
  // tự refresh, hoặc bảng chưa render lại. ĐẢM BẢO về /portal/shop + bảng shop có dòng TRƯỚC khi tìm dòng shopId.
  const pk = await ensureShopPicker(ctx.listTabId);
  if (pk === "verify") { send({ action: "captcha", message: "rơi trang verify khi mở lại picker shop" }); return; }

  await donTabShopMoCoi();

  // POLL chờ ĐÚNG dòng shopId render (KHÔNG đọc 1 phát — dòng có thể chưa render / picker vừa nav lại sau nghỉ).
  let scrolled = false;
  const rowDeadline = Date.now() + 15000;
  while (Date.now() < rowDeadline) {
    scrolled = await execInTab(ctx.listTabId, pageScrollDetailIntoView, [shopId]);
    if (scrolled) break;
    await sleep(500);
  }
  if (!scrolled) { send({ action: "error", message: "không thấy nút Chi tiết của shop " + shopId }); return; }
  await sleep(350);
  const c = await execInTab(ctx.listTabId, pageLocateDetailRect, [shopId]);
  if (!c) { send({ action: "error", message: "không đọc được toạ độ nút Chi tiết" }); return; }

  // Chụp tập tab banhang NGAY trước khi click (sau ensureShopPicker) để phát hiện tab shop MỚI mở.
  const before = (await chrome.tabs.query({ url: "https://banhang.shopee.vn/*" })).map((t) => t.id);
  await trustedClick(ctx.listTabId, c.x, c.y);

  const deadline = Date.now() + 30000;
  let found = null;
  while (Date.now() < deadline) {
    const tabs = await chrome.tabs.query({ url: "https://banhang.shopee.vn/*" });
    const cand = tabs.find((t) => before.indexOf(t.id) === -1);
    if (cand) { found = cand; break; }
    try {
      const lt = await chrome.tabs.get(ctx.listTabId);
      if (lt && lt.url && lt.url.indexOf("/portal/shop") === -1) { found = lt; break; }
    } catch (e) {}
    await sleep(500);
  }
  if (!found) {
    // Chụp LẠI toàn bộ tab ngay tại đây (không dùng lại ảnh chụp cũ của `ensureListTab`: hàm đó chỉ query khi
    // `listTabId` đã chết, nên `ctx.lastTabUrls` có thể là ảnh của vài phút trước — bằng chứng cũ còn tệ hơn
    // không có). Đây là mảnh còn thiếu để chốt cơ chế: 18 lần lỗi này mà không lần nào biết lúc đó có những tab
    // nào, tab picker đang đứng ở URL gì, có tab shop mồ côi nào không.
    try {
      const all = await chrome.tabs.query({});
      ctx.lastTabUrls = all.map((t) => t.url || t.pendingUrl || "").filter(Boolean);
    } catch (e) {}
    send({
      action: "error",
      message: "chờ 30s chưa thấy tab shop mở — SW thấy các tab: [" + ctx.lastTabUrls.join(" | ") + "]",
    });
    return;
  }
  nhoTabShop(found.id);
  // Tab shop VỪA mở, trang chưa load xong — đây là lúc duy nhất chặn kịp SDK chat trước khi nó chạy.
  await ensureDbg(ctx.shopTabId);

  const loadDeadline = Date.now() + 15000;
  let url = found.url || "";
  while (Date.now() < loadDeadline) {
    try {
      const t = await chrome.tabs.get(ctx.shopTabId);
      url = t.url || url;
      if (t.status === "complete") break;
    } catch (e) { break; }
    await sleep(400);
  }

  if (/\/verify/i.test(url)) { send({ action: "captcha", message: url }); return; }
  send({ action: "shopOpened" });
}
