/**
 * Shopee Harvest — HỘP NỔI góc dưới phải trên trang SP Shopee (content script world ISOLATED, nạp sau panel-core.js).
 *
 * Giống hộp của extension BigSeller: người dùng (hoặc app Harvest, bằng chuột thật qua CDP) bấm "Lấy dữ liệu" thì background
 * mới lấy JSON get_pc đã bắt của SP đang xem, làm sạch, đẩy Hub và gửi kết quả về hộp. Hộp chỉ là giao diện: KHÔNG giữ dữ
 * liệu SP, KHÔNG gọi mạng.
 * - Shadow DOM ĐÓNG: CSS của trang không vướng vào hộp, script trang không đọc được nội dung hộp.
 * - Chỉ nhận cú bấm event.isTrusted (chuột / bàn phím thật, kể cả Input.dispatchMouseEvent của CDP) — script của trang không
 *   bấm hộ được.
 * - Trang SPA đổi URL không tải lại: kiểm location mỗi giây, đổi SP thì hỏi lại trạng thái.
 * - Background hỏi "hộp sẵn sàng chưa" (HARVEST_PANEL_PING): hộp cuộn nút vào tầm nhìn rồi trả toạ độ nút (CSS px theo
 *   viewport) để app bấm đúng chỗ.
 */
(() => {
  if (globalThis.__shopeeHarvestPanel) return;
  const core = globalThis.__shopeeHarvestPanelCore;
  if (!core) return;

  const STYLE = `
    :host { all: initial; }
    .box { box-sizing: border-box; width: 300px; max-width: calc(100vw - 32px); padding: 10px 12px; border-radius: 12px;
      background: rgba(15, 23, 42, 0.95); color: #e5e7eb; font: 12px/1.45 "Segoe UI", Arial, sans-serif;
      box-shadow: 0 8px 24px rgba(0,0,0,0.3); border: 1px solid rgba(52, 211, 153, 0.45); }
    .title { font-weight: 600; font-size: 13px; color: #34d399; margin: 0 0 6px; }
    .row { margin: 2px 0; white-space: pre-line; word-break: break-word; }
    .ok { color: #6ee7b7; } .warn { color: #fcd34d; } .bad { color: #fca5a5; } .info { color: #cbd5e1; }
    button { margin-top: 8px; width: 100%; padding: 7px 10px; border: 0; border-radius: 8px; cursor: pointer;
      background: #10b981; color: #052e1f; font: 600 13px "Segoe UI", Arial, sans-serif; }
    button:disabled { background: #374151; color: #9ca3af; cursor: default; }
  `;

  const state = {
    href: location.href,
    hubConfigured: false,
    auto: null,
    known: { status: "loading" },
    queue: { pending: 0, error: 0 },
    busy: null,
    last: null,
  };

  let host = null;
  let rowsEl = null;
  let button = null;
  let lastView = null;

  const ensureHost = () => {
    if (host && host.isConnected) return;
    host = document.createElement("div");
    host.style.cssText = "all: initial; position: fixed; right: 16px; bottom: 16px; z-index: 2147483647; display: none;";
    const root = host.attachShadow({ mode: "closed" });
    const style = document.createElement("style");
    style.textContent = STYLE;
    const box = document.createElement("div");
    box.className = "box";
    const title = document.createElement("div");
    title.className = "title";
    title.textContent = "Shopee Harvest";
    rowsEl = document.createElement("div");
    button = document.createElement("button");
    button.type = "button";
    button.addEventListener("click", onClick);
    box.appendChild(title);
    box.appendChild(rowsEl);
    box.appendChild(button);
    root.appendChild(style);
    root.appendChild(box);
    (document.body || document.documentElement).appendChild(host);
  };

  const render = () => {
    lastView = core.view({ ...state, now: Date.now() });
    if (!lastView.visible) {
      if (host) host.style.display = "none";
      return lastView;
    }
    ensureHost();
    host.style.display = "block";
    while (rowsEl.firstChild) rowsEl.removeChild(rowsEl.firstChild);
    for (const r of lastView.rows) {
      const div = document.createElement("div");
      div.className = `row ${r.tone || "info"}`;
      div.textContent = r.text;
      rowsEl.appendChild(div);
    }
    button.textContent = lastView.button.text;
    button.disabled = !lastView.button.enabled;
    return lastView;
  };

  const send = (msg) => new Promise((resolve) => {
    try {
      chrome.runtime.sendMessage({ type: "HARVEST_PANEL", ...msg }, (resp) => {
        void chrome.runtime.lastError; // SW chưa dậy / extension vừa nạp lại → bỏ qua, lần sau hỏi lại
        resolve(resp || null);
      });
    } catch (_) {
      resolve(null);
    }
  });

  const refresh = async () => {
    const resp = await send({ op: "status", href: state.href });
    if (resp && typeof resp === "object") Object.assign(state, resp);
    render();
  };

  function onClick(event) {
    if (!event.isTrusted) return; // script của trang không được bấm hộ
    const v = lastView || render();
    if (!v.button.enabled) return;
    if (v.button.action === "options") { send({ op: "openOptions" }); return; }
    if (v.button.action !== "harvest") return;
    state.busy = "capture";
    state.last = null;
    render();
    send({ op: "click", href: location.href, itemId: v.itemId }).then((resp) => {
      if (resp && resp.accepted) return; // kết quả về qua HARVEST_PANEL_UPDATE
      state.busy = null;
      state.last = { status: "error", reason: resp?.reason || "Extension không phản hồi — tải lại trang rồi thử lại.", at: Date.now(), itemId: v.itemId };
      render();
    });
  }

  /** Trả lời background: hộp sẵn sàng bấm chưa + toạ độ nút (sau khi cuộn nút vào tầm nhìn). */
  const ping = (patch) => {
    if (patch && typeof patch === "object") Object.assign(state, patch);
    state.href = location.href;
    const v = render();
    if (!v.visible || !v.itemId) return { ready: false, reason: "not-product", href: location.href };
    if (!v.button.enabled || v.button.action !== "harvest") return { ready: false, reason: state.busy ? "busy" : "not-ready", href: location.href, itemId: v.itemId };
    try { button.scrollIntoView({ block: "nearest", inline: "nearest" }); } catch (_) {}
    const r = button.getBoundingClientRect();
    if (!(r.width > 0 && r.height > 0)) return { ready: false, reason: "hidden", href: location.href, itemId: v.itemId };
    // Tâm nút phải thật sự là hộp (shadow đóng → elementFromPoint trả về chính host): bị phần tử trang che thì cú bấm trúng
    // trang chứ không trúng nút.
    const cx = r.left + r.width / 2;
    const cy = r.top + r.height / 2;
    let top = null;
    try { top = document.elementFromPoint(cx, cy); } catch (_) {}
    if (top !== host) {
      const what = top ? `${String(top.tagName || "?").toLowerCase()}${top.id ? `#${top.id}` : ""}` : "không có phần tử";
      return { ready: false, reason: `covered: nút bị che bởi ${what}`, href: location.href, itemId: v.itemId };
    }
    const vv = window.visualViewport;
    return {
      ready: true,
      itemId: v.itemId,
      href: location.href,
      rect: { x: r.left, y: r.top, width: r.width, height: r.height },
      viewport: { width: window.innerWidth, height: window.innerHeight },
      pageScale: vv && Number.isFinite(vv.scale) ? vv.scale : 1, // thu phóng bằng chạm / ctrl+cuộn trên trang (pinch)
      devicePixelRatio: Number.isFinite(window.devicePixelRatio) ? window.devicePixelRatio : null,
    };
  };

  chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    if (!message || typeof message.type !== "string") return undefined;
    if (message.type === "HARVEST_PANEL_UPDATE") {
      if (message.state && typeof message.state === "object") Object.assign(state, message.state);
      render();
      return undefined;
    }
    if (message.type === "HARVEST_PANEL_PING") {
      sendResponse(ping(message.state));
      return undefined;
    }
    return undefined;
  });

  // SPA: Shopee đổi SP không tải lại trang.
  setInterval(() => {
    if (location.href === state.href) return;
    state.href = location.href;
    state.known = { status: "loading" };
    refresh();
  }, 1000);

  Object.defineProperty(globalThis, "__shopeeHarvestPanel", { value: Object.freeze({ render, ping, state }) });
  refresh();
})();
