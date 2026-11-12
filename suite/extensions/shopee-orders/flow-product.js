// Bước CUỐI vòng shop (sau check đơn trả hàng): đọc số sp đang có / tổng hạn mức (xxx / yyy) của shop.
// Gọi thẳng API nội bộ Seller Centre ngay trên tab shop hiện tại (same-origin, kèm cookie) — KHÔNG mở trang
// Quản lý sản phẩm nữa (trang đó bật cả loạt popup chặn). Chốt 12/11/2026 sau khi đối chiếu payload thật:
// {"code":0,"data":{"current_published":3000,"publish_limit":3000,"unlisted_count":1}}.
import { send, orderTabIdStrict, LOI_MAT_TAB_SHOP } from "./core.js";
import { execInTab } from "./exec.js";
import { pageFetchProductCountLimit } from "./page-funcs-product.js";
import { sleep } from "./shared/util.js";

// Số lần gọi API (lần đầu + thử lại) và nhịp nghỉ giữa hai lần.
const SO_LAN_GOI_API = 2;
const NGHI_GIUA_LAN_MS = 1500;

export async function doReadProductCount() {
  const tabId = orderTabIdStrict();
  if (tabId == null) {
    send({ action: "error", message: "đọc số sản phẩm shop: " + LOI_MAT_TAB_SHOP });
    return;
  }

  const traVe = (tongSp, gioiHanSp, rawText) => send({
    action: "pageData",
    kind: "productCount",
    data: JSON.stringify({
      tongSp: tongSp != null ? tongSp : null,
      gioiHanSp: gioiHanSp != null ? gioiHanSp : null,
      rawText: rawText || "",
      tabTatCa: false,
    }),
  });

  try {
    // API là của Seller Centre: tab phải đang ở banhang.shopee.* (bước trước — check đơn trả hàng — để tab ở đó).
    let curUrl = "";
    try { curUrl = (await chrome.tabs.get(tabId)).url || ""; } catch (e) {}
    if (!/^https:\/\/banhang\.shopee\./i.test(curUrl)) {
      send({ action: "progress", message: "Không đọc được số sản phẩm shop: tab không ở Seller Centre (" + curUrl.substring(0, 80) + ")." });
      traVe(null, null, "tab ngoài Seller Centre");
      return;
    }

    let api = null;
    for (let lan = 1; lan <= SO_LAN_GOI_API; lan++) {
      try {
        api = await execInTab(tabId, pageFetchProductCountLimit, []);
      } catch (e) {
        api = { ok: false, tongSp: null, gioiHanSp: null, raw: "Lỗi: " + String((e && e.message) || e) };
      }
      send({ action: "progress", message: (lan > 1 ? "API hạn mức sp (thử lại): " : "API hạn mức sp: ") + ((api && api.raw) || "(rỗng)") });
      if (api && api.ok && api.tongSp != null && api.gioiHanSp != null) break;
      if (lan < SO_LAN_GOI_API) await sleep(NGHI_GIUA_LAN_MS);
    }

    if (api && api.ok && (api.tongSp != null || api.gioiHanSp != null)) {
      const spHien = api.tongSp != null ? api.tongSp : "—";
      const ghHien = api.gioiHanSp != null ? api.gioiHanSp : "—";
      send({ action: "progress", message: `Đã đọc số sản phẩm shop: ${spHien} / ${ghHien}` });
      traVe(api.tongSp, api.gioiHanSp, `${spHien} / ${ghHien} (api)`);
    } else {
      send({ action: "progress", message: "Không đọc được số lượng và hạn mức sản phẩm (API lỗi — xem dòng API hạn mức sp)." });
      traVe(null, null, api ? api.raw : "");
    }
  } catch (e) {
    send({
      action: "progress",
      message: "Lỗi khi đọc số sản phẩm shop: " + String((e && e.message) || e),
    });
    traVe(null, null, String(e));
  }
}
