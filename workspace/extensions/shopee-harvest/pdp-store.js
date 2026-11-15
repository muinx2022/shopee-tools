/**
 * Shopee Harvest — kho PDP ở world ISOLATED (content script document_start, cùng matches với pdp-hook.js).
 *
 * pdp-hook.js (world MAIN) không giữ gì trên window của trang: mỗi lượt get_pc nó bắn CustomEvent PDP_EVENT lên
 * document, detail là CHUỖI JSON có `type` ("ready" | "pdp" | "error" | "drop") + bộ đếm seen/dropped (xem đầu
 * pdp-hook.js). type "error" chỉ gửi khi Shopee trả error khác null VÀ khác 0 (error:0 = thành công → type "pdp").
 * Script này nghe event đó và giữ dữ liệu trong biến của world ISOLATED — trang không nhìn thấy.
 *
 * Bắt tay: thứ tự chạy 2 content script document_start khác world không được đảm bảo. Gắn listener xong, script
 * này bắn REQ_EVENT → hook (nếu đã gắn) phát lại các tin đã đệm + "ready". Hook gắn sau thì tin "ready" lúc gắn tự
 * tới đây. Chuyển trang kiểu SPA: cùng document, cả hai script sống tiếp, kho cộng dồn theo itemId. Tải lại trang:
 * document mới → cả hai gắn lại từ đầu, kho rỗng.
 *
 * Background/popup đọc qua chrome.scripting.executeScript({ world: "ISOLATED" }): hàm chèn vào chạy chung isolated
 * world của extension với content script này nên thấy globalThis.__shopeeHarvestStore (globalThis của ISOLATED,
 * không phải window của trang).
 */
(() => {
  // Phải TRÙNG hằng trong pdp-hook.js (tests/pdp-hook.test.mjs kiểm).
  const PDP_EVENT = "shopee-harvest:pdp:7f3a9c1e";
  const REQ_EVENT = "shopee-harvest:pdp:7f3a9c1e:req";
  const MAX_ENTRIES = 20;

  if (globalThis.__shopeeHarvestStore) return; // đã chạy ở document này

  const data = new Map();   // itemId → { itemId, url, at, via, body }
  const errors = new Map(); // itemId → { itemId, url, at, via, error }
  const status = { hookReady: false, seen: 0, dropped: 0, storeAt: Date.now() };

  // Map giữ thứ tự chèn → xoá rồi chèn lại = đưa lên mới nhất; vượt trần thì bỏ mục cũ nhất.
  const put = (map, itemId, value) => {
    map.delete(itemId);
    map.set(itemId, value);
    while (map.size > MAX_ENTRIES) map.delete(map.keys().next().value);
  };

  const onPdp = (event) => {
    try {
      if (typeof event.detail !== "string") return;
      const msg = JSON.parse(event.detail);
      if (!msg || typeof msg !== "object") return;
      status.hookReady = true;
      // Bộ đếm lấy số lớn nhất (tin phát lại mang số cũ).
      status.seen = Math.max(status.seen, Number(msg.seen) || 0);
      status.dropped = Math.max(status.dropped, Number(msg.dropped) || 0);
      if (msg.type !== "pdp" && msg.type !== "error") return;
      const itemId = String(msg.itemId ?? "");
      if (!/^\d+$/.test(itemId)) return;
      const base = { itemId, url: String(msg.url || ""), at: Number(msg.at) || Date.now(), via: String(msg.via || "") };
      if (msg.type === "pdp" && typeof msg.body === "string") {
        put(data, itemId, { ...base, body: msg.body });
        errors.delete(itemId); // trang thử lại và lấy được → lỗi cũ hết hiệu lực
      } else if (msg.type === "error" && msg.error != null) {
        put(errors, itemId, { ...base, error: String(msg.error) });
      }
    } catch (_) {}
  };

  document.addEventListener(PDP_EVENT, onPdp);

  Object.defineProperty(globalThis, "__shopeeHarvestStore", {
    value: Object.freeze({
      /** { entry, error } cho itemId (mỗi cái có thể null). */
      get: (itemId) => ({
        entry: data.get(String(itemId)) || null,
        error: errors.get(String(itemId)) || null,
      }),
      /** Xoá lỗi get_pc của itemId (background gọi sau khi giải captcha: lỗi trước captcha không còn đúng). */
      clearError: (itemId) => { errors.delete(String(itemId)); },
      /** Mọi mục + trạng thái bắt tay (cho popup xuất JSON / chẩn đoán). */
      list: () => ({ entries: [...data.values()], errors: [...errors.values()], status: { ...status } }),
    }),
  });

  // Hook có thể đã gắn TRƯỚC listener ở trên (và đã bắn tin) → xin phát lại.
  try { document.dispatchEvent(new CustomEvent(REQ_EVENT)); } catch (_) {}
})();
