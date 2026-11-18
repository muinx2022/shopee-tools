/**
 * Shopee Harvest — phần THUẦN của hộp nổi (panel.js): nhận diện trang, dựng nội dung hiển thị từ trạng thái. Content script
 * thường (không import được module) → đặt lên globalThis của world ISOLATED; test Node nạp file qua vm.
 *
 * productIds là bản chép của pdp-core.extractProductIds cho các dạng link TRANG SP (tests/panel.test.mjs đối chiếu hai bản):
 * itemId hộp nổi báo cho background phải trùng itemId background tự tách từ link, không thì app chờ hộp mãi.
 */
(() => {
  if (globalThis.__shopeeHarvestPanelCore) return;

  /** Trang SP Shopee (giống background.isShopeeProductUrl): SEO -i.<shop>.<item> neo hai đầu, /product/<shop>/<item>, query. */
  const isProductUrl = (href) => {
    try {
      const u = new URL(href);
      if (!/(^|\.)shopee\./i.test(u.hostname)) return false;
      if (/^\/verify(?:\/|$)/i.test(u.pathname)) return false;
      return /(?:^|[/\-])i\.\d+\.\d+(?=$|\/)/i.test(u.pathname) ||
        /\/product\/\d+\/\d+/i.test(u.pathname) ||
        /[?&](itemid|shopid)=/i.test(u.search);
    } catch (_) {
      return false;
    }
  };

  /** { shopId, itemId } của trang SP (chép pdp-core.extractProductIds); không phải trang SP → null. */
  const productIds = (href) => {
    if (!isProductUrl(href)) return null;
    let m = /(?:^|[/\-])i\.(\d+)\.(\d+)(?=$|[?#&/])/i.exec(href);
    if (m) return { shopId: m[1], itemId: m[2] };
    m = /\/product\/(\d+)\/(\d+)/i.exec(href);
    if (m) return { shopId: m[1], itemId: m[2] };
    m = /[?&](?:item_?id|itemId)=(\d+)/i.exec(href);
    if (m) {
      const shop = /[?&](?:shop_?id|shopId)=(\d+)/i.exec(href);
      return { shopId: shop ? shop[1] : null, itemId: m[1] };
    }
    return null;
  };

  const pad = (n) => String(n).padStart(2, "0");
  /** "dd/MM HH:mm" theo giờ máy. */
  const formatTime = (at) => {
    const d = new Date(typeof at === "number" ? at : Date.parse(at));
    if (Number.isNaN(d.getTime())) return "";
    return `${pad(d.getDate())}/${pad(d.getMonth() + 1)} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
  };

  /** Chữ cho trạng thái kết quả (click-flow.outcomeOf). tone: ok | warn | bad. */
  const RESULT_TEXT = {
    saved: ["✓ Đã lưu Hub", "ok"],
    queued: ["⏳ Hub chưa nhận — đã vào hàng đợi, tự gửi lại", "warn"],
    rejected: ["✗ Hub từ chối dữ liệu", "bad"],
    invalid: ["✗ Dữ liệu SP không xử lý được", "bad"],
    failed: ["✗ Không gửi được Hub", "bad"],
    notProduct: ["✗ Không phải trang sản phẩm", "bad"],
    loginRequired: ["✗ Tài khoản Shopee mất đăng nhập — đăng nhập lại rồi bấm lại", "bad"],
    captcha: ["✗ Gặp captcha / xác minh — giải xong rồi bấm lại", "bad"],
    traffic: ["✗ Shopee chặn truy cập (traffic) — đợi rồi thử lại", "bad"],
    proxy: ["✗ Lỗi mạng / proxy — không tải được trang", "bad"],
    pdpError: ["✗ Shopee trả lỗi dữ liệu SP", "bad"],
    error: ["✗ Không lấy được dữ liệu", "bad"],
  };

  /** Kết quả gần nhất còn đáng hiện trên trang KHÔNG phải SP (bị đẩy sang đăng nhập / xác minh…) trong chừng này. */
  const OFF_PAGE_RESULT_MS = 120_000;

  const resultLine = (last) => {
    const [text, tone] = RESULT_TEXT[last.status] || RESULT_TEXT.error;
    let t = text;
    if (last.status === "saved" && last.applied === false) t += " (dữ liệu không đổi)";
    if (last.reason) t += `: ${last.reason}`;
    return { text: t, tone };
  };

  /**
   * Nội dung hộp từ trạng thái:
   * state = { href, now, hubConfigured, auto:{sheet,rowNo}|null, known:{status:"loading"|"yes"|"no"|"error"|"off", at, reason},
   *           queue:{pending,error}, busy:null|"capture"|"push", last:{status,reason,applied,at,itemId}|null }.
   * Trả { visible, itemId, rows:[{text,tone}], button:{text,enabled,action:"harvest"|"options"|null} }.
   */
  const view = (state) => {
    const s = state || {};
    const ids = productIds(s.href || "");
    const now = typeof s.now === "number" ? s.now : Date.now();
    const last = s.last && (!ids || !s.last.itemId || s.last.itemId === ids.itemId) ? s.last : null;
    const offPageError = !ids && last && last.status !== "saved" && now - (last.at || 0) <= OFF_PAGE_RESULT_MS;
    const visible = Boolean(ids) || Boolean(offPageError) || Boolean(s.busy);
    const rows = [];
    if (s.auto) rows.push({ text: `Tự động (app Harvest) · sheet ${s.auto.sheet || "?"} · dòng ${s.auto.rowNo ?? "?"}`, tone: "info" });
    if (!s.hubConfigured) {
      rows.push({ text: "Chưa cấu hình Hub — bấm \"Mở cài đặt\" để nhập URL Hub / token", tone: "warn" });
    } else if (ids && !s.auto) {
      const k = s.known || { status: "loading" };
      if (k.status === "yes") rows.push({ text: `Đã có trên Hub · lấy lúc ${formatTime(k.at)}`, tone: "ok" });
      else if (k.status === "no") rows.push({ text: "Chưa có trên Hub", tone: "info" });
      else if (k.status === "error") rows.push({ text: `Không hỏi được Hub${k.reason ? ` (${k.reason})` : ""}`, tone: "warn" });
      else if (k.status === "loading") rows.push({ text: "Đang hỏi Hub…", tone: "info" });
    }
    const q = s.queue || {};
    if ((q.pending || 0) > 0 || (q.error || 0) > 0) {
      rows.push({ text: `Hàng đợi: ${q.pending || 0} chờ gửi · ${q.error || 0} lỗi`, tone: (q.error || 0) > 0 ? "warn" : "info" });
    }
    if (last) rows.push(resultLine(last));

    let button;
    if (!s.hubConfigured) button = { text: "Mở cài đặt", enabled: true, action: "options" };
    else if (s.busy) button = { text: s.busy === "push" ? "Đang gửi Hub…" : "Đang lấy dữ liệu…", enabled: false, action: null };
    else button = { text: "Lấy dữ liệu", enabled: Boolean(ids), action: ids ? "harvest" : null };
    return { visible, itemId: ids ? ids.itemId : null, rows, button };
  };

  Object.defineProperty(globalThis, "__shopeeHarvestPanelCore", {
    value: Object.freeze({ isProductUrl, productIds, formatTime, view, OFF_PAGE_RESULT_MS }),
  });
})();
