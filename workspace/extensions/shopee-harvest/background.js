/**
 * Shopee Harvest Runner — thin executor cho app Shopee Harvest (engine MultiBrave, điều khiển qua CDP).
 * Launcher điều khiển vòng lặp dòng / log / start-stop; extension chỉ:
 * - mở tab + hiện overlay thông báo
 * - chờ trang sản phẩm, lấy JSON get_pc mà trang tự tải (pdp-hook.js bắt, pdp-store.js giữ ở world ISOLATED);
 *   chưa có sau 30s thì tải lại trang MỘT lần rồi chờ thêm 30s. KHÔNG tự gọi get_pc.
 * - KHÔNG ghi tiến độ (lastCompletedRow): dữ liệu chỉ nằm trong kết quả trả về, launcher lưu outbox rồi mới ghi.
 *
 * Phần chung (mở/reuse tab, chờ tải, settle URL, dò verify/captcha + chờ giải tay, dò lỗi mạng/proxy, overlay,
 * keepalive SW, runnerState, các hàm __launcher*) chép từ extensions/shopee-scrape/background.js (2026-11-15),
 * đã bỏ mọi phần BigSeller (bấm nút Scrape, báo mất đăng nhập, bỏ qua SP đã cào…). Sửa hành vi chung thì soi cả hai bản.
 */

// shared/ chép NGUYÊN từ extensions/shopee-scrape/shared/ (2026-11-15) — sửa thì sửa cả hai bản.
import { sleep } from "./shared/util.js";
import { getTabSafe as getTabSafeRaw, waitForTabComplete } from "./shared/tab-wait.js";
import { isTrafficErrorUrl, isVerifyUrl, isNetworkErrorPage } from "./shared/net-detect.js";
import { extractProductIds, checkPdpBody, redactPrivate } from "./pdp-core.js";

const OVERLAY_SCRIPT = "overlay.js";
const TAB_LOAD_TIMEOUT_MS = 30_000;
// Chờ hook bắt được get_pc của đúng itemId (tính từ lúc trang SP đã ổn định / vừa giải xong captcha / vừa reload).
const PDP_WAIT_MS = 30_000;
const PDP_POLL_MS = 1_000;
// Trần cho mỗi lời gọi chrome.* có thể treo (executeScript vào tab đơ, tabs.get…) — bước không được kẹt vô hạn.
const CALL_TIMEOUT_MS = 10_000;
// Đọc kho PDP chỉ là đọc biến trong isolated world — quá 3s là tab đơ, đừng để vòng chờ mất nhịp.
const STORE_READ_TIMEOUT_MS = 3_000;
// Trần TỔNG cho một bước (khớp SCRAPE_WAIT_TIMEOUT_MS của shopee-scrape): captcha giải tay nhiều lần + nhiều lượt
// chờ 30s có thể dồn rất lâu; quá trần → kết thúc bước với lỗi dòng. Chốt chặn cuối, watchdog C# vẫn là chính.
const STEP_TOTAL_TIMEOUT_MS = 540_000;
// Mã lỗi get_pc coi là CHẶN BOT (đi đường traffic như scrape); mã khác là lỗi dòng. Payload đè được blockErrorCodes.
const DEFAULT_BLOCK_ERROR_CODES = [90309999];

let abortRequested = false;
// Mỗi lần gọi __launcherExecuteHarvestStep nhận một số thứ tự; bước cũ thấy số hiện hành khác số của mình thì
// dừng ở điểm kiểm kế tiếp, không ghi gì.
let harvestStepSeq = 0;
let activeHarvestStep = 0;

/** Chờ promise tối đa ms; quá hạn thì REJECT (giữ đúng ngữ nghĩa "lời gọi lỗi" cho các try/catch sẵn có). */
const timed = (promise, ms = CALL_TIMEOUT_MS) => new Promise((resolve, reject) => {
  const timer = setTimeout(() => reject(new Error(`Quá ${ms / 1000}s chờ trình duyệt phản hồi.`)), ms);
  Promise.resolve(promise).then(
    (v) => { clearTimeout(timer); resolve(v); },
    (e) => { clearTimeout(timer); reject(e); },
  );
});

/** Như timed nhưng quá hạn / lỗi → fallback. */
const withTimeout = (promise, ms, fallback) => timed(promise, ms).catch(() => fallback);

// Mọi lời gọi chrome.* có thể treo trong file này đi qua các bọc có trần thời gian.
const getTabSafe = (tabId) => withTimeout(getTabSafeRaw(tabId), CALL_TIMEOUT_MS, null);
const execScript = (opts) => timed(chrome.scripting.executeScript(opts));
const tabsUpdate = (tabId, props) => timed(chrome.tabs.update(tabId, props));
const tabsCreate = (props) => timed(chrome.tabs.create(props));
const tabsQuery = (query) => timed(chrome.tabs.query(query));

const readState = async () => {
  const data = await chrome.storage.local.get("runnerState");
  return data.runnerState || { running: false };
};

const writeState = async (patch) => {
  const current = await readState();
  const next = { ...current, ...patch };
  await timed(chrome.storage.local.set({ runnerState: next }));
  syncKeepAlive(next);
  return next;
};

const broadcastState = async () => {
  try {
    const state = await readState();
    await chrome.runtime.sendMessage({ type: "RUNNER_STATE", state });
  } catch (_) {}
};

// ── Keep-alive cho service worker (MV3) ──────────────────────────────────────
// SW bị Chromium evict sau ~30s idle. Khi có khoảng nghỉ 2–4 phút giữa các link, SW chết giữa chừng →
// launcher mất kết nối (SW_NO_RESPONSE / mất hook). Giữ SW sống bằng ping API mỗi 20s (mỗi lời gọi chrome.*
// reset bộ đếm idle 30s) + alarm dự phòng để dựng/rearm lại nếu SW vẫn bị recycle. CHỈ bật khi đang chạy.
let __keepAliveTimer = null;
const KEEPALIVE_PING_MS = 20_000;

const startKeepAlive = () => {
  if (__keepAliveTimer === null) {
    __keepAliveTimer = setInterval(() => {
      try { chrome.runtime.getPlatformInfo(() => {}); } catch (_) {}
    }, KEEPALIVE_PING_MS);
  }
  try { chrome.alarms.create("sw-keepalive", { periodInMinutes: 0.4 }); } catch (_) {}
};

const stopKeepAlive = () => {
  if (__keepAliveTimer !== null) {
    clearInterval(__keepAliveTimer);
    __keepAliveTimer = null;
  }
  try { chrome.alarms.clear("sw-keepalive"); } catch (_) {}
};

const syncKeepAlive = (state) => {
  if (state && state.running) startKeepAlive();
  else stopKeepAlive();
};

// SW vừa bị recycle giữa lúc đang chạy → alarm đánh thức rồi tự bật lại keepalive.
try {
  chrome.alarms.onAlarm.addListener((alarm) => {
    if (alarm && alarm.name === "sw-keepalive") {
      readState().then(syncKeepAlive).catch(() => {});
    }
  });
} catch (_) {}

// SW (re)spawn khi run vẫn đang chạy → bật lại keepalive ngay khi load.
readState().then((s) => { if (s && s.running) startKeepAlive(); }).catch(() => {});

// KHÔNG có persistCompletedRow như shopee-scrape (Fix #3 bên đó ghi lastCompletedRow TRƯỚC khi trả kết quả):
// với Harvest dữ liệu CHỈ nằm trong kết quả trả về — ghi sớm mà kết quả rớt (SW/CDP đứt) thì lần relaunch
// ExtensionProgressReader resume qua dòng này dù nó chưa vào outbox → mất dòng âm thầm. Launcher ghi tiến độ
// (setDisplayState) SAU khi đã lưu outbox.

// Đếm theo dõi: hook (bắt ngay), hookReload (bắt sau khi tải lại), reload (số lần phải tải lại), blocked (Shopee
// từ chối get_pc), miss (không lấy được). Khóa riêng: runnerState bị launcher ghi đè cả cục qua setDisplayState.
const HARVEST_STAT_KEYS = ["hook", "hookReload", "reload", "blocked", "miss"];
const bumpHarvestStat = async (key) => {
  try {
    const data = await withTimeout(chrome.storage.local.get("harvestStats"), CALL_TIMEOUT_MS, {});
    const stats = { ...Object.fromEntries(HARVEST_STAT_KEYS.map((k) => [k, 0])), ...(data.harvestStats || {}) };
    stats[key] = (Number(stats[key]) || 0) + 1;
    stats.updatedAt = Date.now();
    await timed(chrome.storage.local.set({ harvestStats: stats })); // set cũng có thể treo → có trần
  } catch (_) {}
};

/** waitForTabComplete từ shared/ (không sửa shared) — bọc trần ở chỗ gọi để tab đơ không treo bước. */
const waitTabComplete = (tabId, ms) =>
  withTimeout(waitForTabComplete(tabId, ms), ms + CALL_TIMEOUT_MS, false);

const normalizeLink = (value) => {
  if (typeof value !== "string") return "";
  const trimmed = value.trim();
  if (!trimmed) return "";
  if (/^https?:\/\//i.test(trimmed)) return trimmed;
  if (trimmed.startsWith("www.")) return `https://${trimmed}`;
  return trimmed;
};

const isWorkTabUrl = (url) => {
  if (!url || typeof url !== "string") return false;
  if (url.startsWith("chrome-extension://")) return false;
  if (url.startsWith("chrome://") || url.startsWith("brave://")) return false;
  try {
    const parsed = new URL(url);
    if (!/(^|\.)shopee\./i.test(parsed.hostname)) return false;
    if (/\/buyer\/login/i.test(parsed.pathname)) return false;
    return true;
  } catch (_) {
    return /shopee/i.test(url) && !/buyer\/login/i.test(url);
  }
};

/** Tìm tab Shopee đang dùng để chạy — tránh chrome.tabs.create khi tab cũ vẫn còn. */
const findReusableWorkTab = async (preferTabId = null) => {
  if (preferTabId) {
    const preferred = await getTabSafe(preferTabId);
    if (preferred?.id && isWorkTabUrl(preferred.url)) return preferred.id;
  }

  const tabs = await tabsQuery({});
  const candidates = tabs.filter((t) => t.id && isWorkTabUrl(t.url || ""));
  if (candidates.length === 0) return null;

  const active = candidates.find((t) => t.active);
  return active?.id ?? candidates[candidates.length - 1].id;
};

const openLinkInTab = async (tabId, url) => {
  const existing = await getTabSafe(tabId);
  if (existing?.id) {
    try {
      await tabsUpdate(existing.id, { url, active: true });
      return existing.id;
    } catch (_) {
      // tab biến mất giữa get và update
    }
  }

  const reuseId = await findReusableWorkTab(tabId);
  if (reuseId) {
    try {
      await tabsUpdate(reuseId, { url, active: true });
      return reuseId;
    } catch (_) {
      // reuse tab cũng không update được
    }
  }

  const created = await tabsCreate({ url, active: true });
  return created.id;
};

const getCurrentTabUrl = async (tabId, fallback = "") => {
  const tab = await getTabSafe(tabId);
  return tab?.url || fallback || "";
};

const isShopeeProductUrl = (url) => {
  try {
    const parsed = new URL(url);
    const host = parsed.hostname.toLowerCase();
    if (!/(^|\.)shopee\./i.test(host)) return false;
    if (isVerifyUrl(url)) return false;
    // Nhận cả 3 dạng link sản phẩm Shopee:
    //  - SEO:   /<ten>-i.<shopid>.<itemid> hoặc /i.<shopid>.<itemid> — neo đầu (sau / hoặc -) VÀ đuôi (hết path
    //           hoặc /), cùng quy tắc với pdp-core.extractProductIds
    //  - /product/<shopid>/<itemid>   (dạng Shopee hay redirect tới)
    //  - query: ?itemid=...&shopid=...
    return /(?:^|[/\-])i\.\d+\.\d+(?=$|\/)/i.test(parsed.pathname) ||
           /\/product\/\d+\/\d+/i.test(parsed.pathname) ||
           /[?&](itemid|shopid)=/i.test(parsed.search);
  } catch (_) {
    return false;
  }
};

const waitForUrlStable = async (tabId, settleMs = 2500) => {
  let lastUrl = await getCurrentTabUrl(tabId);
  const deadline = Date.now() + settleMs;
  while (Date.now() < deadline) {
    await sleep(400);
    const current = await getCurrentTabUrl(tabId, lastUrl);
    if (current !== lastUrl) {
      lastUrl = current;
      continue;
    }
  }
  return lastUrl;
};

const injectOverlayManager = async (tabId) => {
  if (!(await getTabSafe(tabId))) return false;
  try {
    await execScript({ target: { tabId }, files: [OVERLAY_SCRIPT] });
    return true;
  } catch (_) {
    return false;
  }
};

const showOverlay = async (tabId, text) => {
  try {
    await timed(chrome.tabs.sendMessage(tabId, { type: "SHOW_NEXT_LINK_MESSAGE", text }));
  } catch (_) {}
};

const hideOverlay = async (tabId) => {
  try {
    await timed(chrome.tabs.sendMessage(tabId, { type: "HIDE_NEXT_LINK_MESSAGE" }));
  } catch (_) {}
};

// Trang lỗi mạng/proxy: dùng bộ dấu hiệu GỘP ở shared/net-detect.js. Giữ world ISOLATED và coi "không inject
// được" là lỗi trang — y như shopee-scrape.
// Quá hạn cũng coi là lỗi trang (cùng chiều với onInjectError).
const isProxyErrorPage = (tabId) =>
  withTimeout(isNetworkErrorPage(tabId, { world: "ISOLATED", onInjectError: true }), CALL_TIMEOUT_MS, true);

const detectCaptcha = async (tabId) => {
  // URL của tab đọc qua chrome.tabs (KHÔNG cần inject). Trang /verify/captcha của Shopee có thể CHẶN
  // inject content script → executeScript ném lỗi → nếu chỉ dựa vào inject sẽ BỎ SÓT captcha. Vì vậy
  // bắt captcha theo URL ở ngay tầng background (kể cả khi inject lỗi).
  let tabUrl = "";
  try { tabUrl = await getCurrentTabUrl(tabId); } catch (_) {}
  const urlIsVerify = isVerifyUrl(tabUrl);
  try {
    const results = await execScript({
      target: { tabId },
      args: [tabUrl],
      func: (currentTabUrl) => {
        const isVisible = (element) => {
          if (!element) return false;
          const style = window.getComputedStyle(element);
          const rect = element.getBoundingClientRect();
          return (
            style &&
            style.display !== "none" &&
            style.visibility !== "hidden" &&
            style.opacity !== "0" &&
            rect.width > 0 &&
            rect.height > 0
          );
        };
        const bodyClone = document.body?.cloneNode(true);
        bodyClone?.querySelector("#shopee27052026-next-link-overlay")?.remove();
        const text = (bodyClone?.innerText || "").toLowerCase();
        const url = currentTabUrl || location.href || "";
        let verifyPath = /^\/verify(?:\/|$)/i.test(location.pathname);
        try {
          verifyPath = verifyPath || /^\/verify(?:\/|$)/i.test(new URL(url).pathname);
        } catch (_) {}
        const captchaText =
          // English
          text.includes("verify to continue") ||
          text.includes("complete the puzzle") ||   // "please slide to complete the puzzle" + biến thể
          text.includes("slide to verify") ||
          text.includes("drag the slider") ||
          text.includes("press & hold") || text.includes("press and hold") ||
          text.includes("verify it's you") || text.includes("verify its you") ||
          // Tiếng Việt (Shopee hiển thị captcha theo ngôn ngữ trang)
          text.includes("trượt để") ||              // "Vui lòng trượt để hoàn thành câu đố"
          text.includes("hoàn thành câu đố") ||
          text.includes("xác minh để tiếp tục") ||
          text.includes("vui lòng xác minh") ||
          text.includes("xác nhận bạn không phải") || // "...không phải người máy/robot"
          text.includes("nhấn giữ");                // "Nhấn giữ để xác minh"
        // Phòng captcha KHÔNG khớp text (vd nằm trong iframe/khung riêng): phát hiện khung captcha hiển thị.
        let captchaEl = false;
        try {
          for (const el of document.querySelectorAll(
            "iframe[src*='captcha' i], iframe[src*='verify' i], .secsdk-captcha-drag-icon, " +
            "[class*='captcha_verify' i], [class*='geetest' i], [class*='captcha-slide' i]")) {
            if (isVisible(el)) { captchaEl = true; break; }
          }
        } catch (_) {}
        const captchaVisible = captchaText || captchaEl;
        return { detected: captchaVisible || verifyPath, captchaVisible, title: document.title || "", url };
      },
    });
    const r = results?.[0]?.result ?? { detected: false, title: "", url: tabUrl };
    return { ...r, detected: r.detected || urlIsVerify, captchaVisible: r.captchaVisible || urlIsVerify };
  } catch (_) {
    // Inject lỗi (trang /verify chặn content script) → vẫn báo captcha dựa trên URL /verify của tab.
    return { detected: urlIsVerify, captchaVisible: urlIsVerify, title: "", url: tabUrl };
  }
};

const markCaptchaTab = async (tabId, label) => {
  try {
    await execScript({
      target: { tabId },
      args: [label],
      func: (titlePrefix) => {
        if (!window.__launcherOriginalTitle) {
          window.__launcherOriginalTitle = document.title || "";
        }
        document.title = `${titlePrefix} | ${window.__launcherOriginalTitle}`.slice(0, 120);
      },
    });
  } catch (_) {}
};

// Chờ giải captcha bằng TAY tối đa 3': hiện overlay nhắc giải, poll mỗi vài giây. Giải xong
// (captcha biến mất) → chạy tiếp (waited:true), KHÔNG đánh dấu. Quá 3' vẫn còn → mới báo captcha
// để launcher đánh dấu lỗi.
const CAPTCHA_MANUAL_WAIT_MS = 3 * 60_000;
const CAPTCHA_POLL_MS = 3_000;

const waitForCaptchaToClear = async (tabId, context) => {
  const firstCheck = await detectCaptcha(tabId);
  if (!firstCheck.detected) return { ok: true, waited: false };

  const instanceName = String(context?.instanceName || "Profile").trim();
  const rowNumber = Number(context?.rowNumber) || 0;
  const sku = String(context?.sku || "").trim();
  const rowText = rowNumber > 0 ? `dong ${rowNumber}` : "dong hien tai";
  const skuText = sku ? `\nSKU: ${sku}` : "";
  const message =
    `CAPTCHA - ${instanceName}\n${rowText}${skuText}\nGiai tay giup — tu dong chay tiep khi xong (toi da 3').`;

  // Bước đã bị huỷ/thay thì KHÔNG vẽ overlay/đổi tiêu đề tab (tab có thể đang thuộc bước mới).
  if (abortRequested || context?.cancelled?.())
    return { ok: false, captcha: true, aborted: true, message: `Da huy khi cho giai captcha - ${instanceName}, ${rowText}.` };
  await injectOverlayManager(tabId);
  await showOverlay(tabId, message);
  await markCaptchaTab(tabId, `CAPTCHA ${instanceName} ${rowText}`);

  // Chờ tối đa CAPTCHA_MANUAL_WAIT_MS; mỗi nhịp còn kiểm huỷ/bị thay (context.cancelled) và trần TỔNG của bước
  // (context.stepExpired) — hết trần bước thì thoát sớm dù 3' chưa hết.
  const deadline = Date.now() + CAPTCHA_MANUAL_WAIT_MS;
  while (Date.now() < deadline) {
    // cancelled(): bước Harvest đã bị bước mới thay thế → dừng như huỷ.
    if (abortRequested || context?.cancelled?.())
      return { ok: false, captcha: true, aborted: true, message: `Da huy khi cho giai captcha - ${instanceName}, ${rowText}.` };
    // Quá trần TỔNG của bước → trả stepTimedOut để người gọi kết luận lỗi dòng (không captcha).
    if (context?.stepExpired?.())
      return { ok: false, captcha: false, stepTimedOut: true, message: `Qua tran thoi gian buoc khi cho captcha - ${instanceName}, ${rowText}.` };
    await sleep(CAPTCHA_POLL_MS);
    const check = await detectCaptcha(tabId);
    if (!check.detected)
      return { ok: true, waited: true }; // đã giải tay xong → chạy tiếp, không đánh dấu
  }

  // Quá 3' vẫn còn captcha → để launcher đánh dấu captcha/lỗi.
  return {
    ok: false,
    captcha: true,
    message: `Dung vi captcha (qua 3' khong giai tay) - ${instanceName}, ${rowText}.`,
  };
};

const checkCurrentLinkBeforeNext = async (tabId, context) => {
  const deadline = Date.now() + 30_000;
  let currentUrl = "";
  let stableProductSince = 0;

  while (Date.now() < deadline) {
    currentUrl = await getCurrentTabUrl(tabId, currentUrl);
    if (isTrafficErrorUrl(currentUrl)) {
      return {
        ok: false,
        captcha: false,
        trafficError: true,
        message: "Shopee chan traffic (verify/traffic/error).",
        pageUrl: currentUrl,
      };
    }
    const captchaCheck = await detectCaptcha(tabId);
    if (isVerifyUrl(currentUrl) || captchaCheck.detected) {
      break;
    }

    if (isShopeeProductUrl(currentUrl)) {
      stableProductSince ||= Date.now();
      if (Date.now() - stableProductSince >= 3000) {
        return { ok: true, waited: false, pageUrl: currentUrl };
      }
    } else {
      stableProductSince = 0;
    }

    await sleep(700);
  }

  currentUrl = await getCurrentTabUrl(tabId, currentUrl);
  if (isTrafficErrorUrl(currentUrl)) {
    return {
      ok: false,
      captcha: false,
      trafficError: true,
      message: "Shopee chan traffic (verify/traffic/error).",
      pageUrl: currentUrl,
    };
  }
  const captchaCheck = await detectCaptcha(tabId);
  if (isVerifyUrl(currentUrl) || captchaCheck.detected) {
    const captchaWait = await waitForCaptchaToClear(tabId, context);
    if (!captchaWait.ok) {
      return {
        ok: false,
        captcha: true,
        aborted: Boolean(captchaWait.aborted),
        message: captchaWait.message || `Dang dung vi captcha - ${context?.instanceName || "Profile"}, dong ${context?.rowNumber || ""}.`,
        pageUrl: await getCurrentTabUrl(tabId, currentUrl),
      };
    }
    return {
      ok: true,
      waited: true,
      pageUrl: await getCurrentTabUrl(tabId, currentUrl),
    };
  }

  if (!isShopeeProductUrl(currentUrl)) {
    return {
      ok: false,
      captcha: false,
      message: `URL hien tai khong phai link san pham Shopee: ${currentUrl || "(trong)"}`,
      pageUrl: currentUrl,
    };
  }

  return { ok: true, waited: false, pageUrl: currentUrl };
};

// ── Đọc PDP từ kho ISOLATED ─────────────────────────────────────────────────────────────────────

/**
 * { entry, error } cho itemId từ kho của pdp-store.js. Hàm chèn chạy ở world ISOLATED — cùng isolated world của
 * extension với content script pdp-store.js — nên thấy globalThis.__shopeeHarvestStore (trang không thấy).
 * Chưa có kho (document đang tải / trang ngoài Shopee) hoặc lỗi/quá hạn → cả hai null.
 */
const readPdpStore = async (tabId, itemId) => {
  try {
    const [res] = await timed(chrome.scripting.executeScript({
      target: { tabId },
      world: "ISOLATED",
      args: [String(itemId)],
      func: (id) => {
        const store = globalThis.__shopeeHarvestStore;
        return store ? store.get(id) : { entry: null, error: null };
      },
    }), STORE_READ_TIMEOUT_MS);
    return { entry: res?.result?.entry || null, error: res?.result?.error || null };
  } catch (_) {
    return { entry: null, error: null };
  }
};

/** Xoá lỗi get_pc đã ghi của itemId trong kho ISOLATED (lỗi cũ trước captcha không còn đúng). Lỗi/quá hạn → bỏ qua. */
const clearPdpError = async (tabId, itemId) => {
  try {
    await timed(chrome.scripting.executeScript({
      target: { tabId },
      world: "ISOLATED",
      args: [String(itemId)],
      func: (id) => { globalThis.__shopeeHarvestStore?.clearError?.(id); return true; },
    }), STORE_READ_TIMEOUT_MS);
  } catch (_) {}
};

// Có lỗi get_pc mà không có captcha: chờ ngần này rồi đọc kho lại — trang có thể vừa tự gọi lại get_pc thành công.
const PDP_ERROR_RECHECK_MS = 3_000;

/** Kết quả bước Harvest: MỌI lối ra (kể cả catch) có đủ các cờ C# đọc (ScrapeStepResult) + phần riêng Harvest. */
const harvestResult = (fields) => ({
  ok: false,
  scrapeOk: false,
  harvestOk: false,
  captcha: false,
  proxyError: false,
  trafficError: false,
  blocked: false,
  notProduct: false,
  aborted: false,
  superseded: false,
  message: "",
  tabId: null,
  pageUrl: "",
  itemId: null,
  shopId: null,
  json: null,
  source: null,
  pdpError: null,
  ...fields,
});

/**
 * Một bước: mở link → chờ trang SP → lấy JSON get_pc trang tự tải. Launcher lưu dữ liệu / ghi tiến độ / vòng lặp.
 * Thứ tự: đọc kho TRƯỚC (có dữ liệu khớp itemId là xong, không dò captcha) → chỉ khi chưa có mới xét verify/captcha/
 * proxy → chờ 30s → tải lại trang MỘT lần → chờ thêm 30s. Shopee trả get_pc có `error` → coi như chặn traffic, không
 * reload. Gọi chồng: bước mới làm bước cũ dừng ở điểm kiểm kế tiếp (superseded), bước cũ không ghi gì.
 */
globalThis.__launcherExecuteHarvestStep = async (payload) => {
  const myStep = ++harvestStepSeq;
  activeHarvestStep = myStep;
  const superseded = () => activeHarvestStep !== myStep;
  const stopped = () => abortRequested || superseded();
  const stopResult = (extra) => harvestResult({
    aborted: true,
    superseded: superseded(),
    message: superseded() ? "Bước đã bị bước mới thay thế." : "Đã hủy.",
    ...extra,
  });
  // Bước đã bị huỷ hoặc bị thay thì không ghi thống kê.
  const bump = async (key) => { if (!stopped()) await bumpHarvestStat(key); };

  // Trần TỔNG cho bước: captcha giải tay + nhiều lượt chờ 30s dồn lại không được kéo dài quá mức.
  const stepDeadline = Date.now() + STEP_TOTAL_TIMEOUT_MS;
  const stepExpired = () => Date.now() > stepDeadline;

  // MỌI lối ra đi qua đây: bước đã bị huỷ / bị thay thì kết quả (dù là gì) bị ghi đè thành stopResult — không trả
  // trafficError/ok/json của một bước đã không còn quyền quyết định.
  const finish = (r) => (stopped()
    ? stopResult({ tabId: r?.tabId ?? null, pageUrl: r?.pageUrl || payload?.link || "", itemId: r?.itemId ?? null, shopId: r?.shopId ?? null })
    : r);

  return finish(await (async () => {
  try {
    abortRequested = false;
    startKeepAlive(); // đang chạy → chắc chắn giữ SW sống dù launcher chưa kịp set running.
    const link = normalizeLink(payload?.link);
    const rowNumber = Number(payload?.rowNumber) || 0;
    const statusText =
      payload?.statusText ||
      (rowNumber > 0 ? `Đang lấy dữ liệu dòng ${rowNumber}…` : "Đang lấy dữ liệu…");
    const instanceName = String(payload?.instanceName || "Profile").trim();
    const sku = String(payload?.sku || "").trim();
    const blockErrorCodes = Array.isArray(payload?.blockErrorCodes)
      ? payload.blockErrorCodes.map(Number).filter((c) => Number.isFinite(c))
      : DEFAULT_BLOCK_ERROR_CODES;
    const ctx = { instanceName, rowNumber, sku, cancelled: superseded, stepExpired };

    if (!link) {
      return harvestResult({ message: "Thiếu link." });
    }

    let tabId = payload?.tabId ? Number(payload.tabId) : null;
    if (tabId && !(await getTabSafe(tabId))) {
      tabId = null;
    }

    tabId = await openLinkInTab(tabId, link);

    // Khối thử tải lại: sau MỖI lần chờ đều kiểm stopped() — bước đã bị thay KHÔNG được điều hướng tab (tab dùng
    // chung, bước mới có thể đang dùng nó) về link cũ.
    let loaded = await waitTabComplete(tabId, TAB_LOAD_TIMEOUT_MS);
    if (!loaded && !stopped()) {
      const stillThere = await getTabSafe(tabId);
      if (stopped()) return stopResult({ tabId, pageUrl: link });
      if (stillThere?.id) {
        await sleep(1000);
        loaded = await waitTabComplete(tabId, TAB_LOAD_TIMEOUT_MS);
        if (stopped()) return stopResult({ tabId, pageUrl: link });
        if (!loaded) {
          try {
            await tabsUpdate(tabId, { url: link, active: true });
            loaded = await waitTabComplete(tabId, TAB_LOAD_TIMEOUT_MS);
          } catch (_) {
            if (stopped()) return stopResult({ tabId, pageUrl: link });
            const reuse = await findReusableWorkTab();
            if (stopped()) return stopResult({ tabId, pageUrl: link });
            tabId = await openLinkInTab(reuse, link);
            loaded = await waitTabComplete(tabId, TAB_LOAD_TIMEOUT_MS);
          }
        }
      } else {
        // getTabSafe trả null (tab cũ đã mất) → mở tab khác: vẫn phải kiểm stopped() trước khi mở.
        const reuse = await findReusableWorkTab();
        if (stopped()) return stopResult({ tabId, pageUrl: link });
        tabId = await openLinkInTab(reuse, link);
        loaded = await waitTabComplete(tabId, TAB_LOAD_TIMEOUT_MS);
      }
    }
    if (stopped()) return stopResult({ tabId, pageUrl: link });
    if (!loaded) {
      const surviving = await getTabSafe(tabId);
      return harvestResult({
        message: "Tab đã đóng hoặc không tải được trang.",
        tabId: surviving?.id ?? tabId,
        pageUrl: link,
      });
    }

    let currentPageUrl = await waitForUrlStable(tabId);
    if (stopped()) return stopResult({ tabId, pageUrl: currentPageUrl || link });
    if (isTrafficErrorUrl(currentPageUrl)) {
      return harvestResult({
        trafficError: true,
        message: `Shopee chan traffic (verify/traffic/error) dong ${rowNumber}.`,
        tabId,
        pageUrl: currentPageUrl,
      });
    }

    // itemId lấy từ LINK (đích người dùng muốn); link rút gọn/không nhận ra thì lấy từ URL trang đã ổn định.
    const ids = extractProductIds(link) || extractProductIds(currentPageUrl);
    const itemId = ids?.itemId ?? null;
    const shopId = ids?.shopId ?? null;
    let captchaWaited = false;
    let lastReject = "";
    let recheckedError = false; // đã chờ-đọc-lại một lần cho lỗi get_pc hiện tại chưa

    // Có dữ liệu khớp → kết quả ok; Shopee từ chối get_pc → chặn bot (mã trong blockErrorCodes) hoặc lỗi dòng;
    // chưa có gì → null. Có lỗi nhưng đang hiện verify/captcha → trả { deferToCaptcha } để caller đi đường captcha.
    const probe = async (source) => {
      const { entry, error } = await readPdpStore(tabId, itemId);
      if (entry) {
        const check = checkPdpBody(entry.body, itemId);
        if (check.ok) {
          if (stopped()) return stopResult({ tabId, pageUrl: link, itemId, shopId });
          // Cắt dữ liệu riêng tư NGAY tại nguồn (đóng an toàn: lỗi → NÉM, không trả body chưa cắt). Gặp lỗi →
          // coi như lỗi dòng (ok=false, message redact-failed), KHÔNG để body có địa chỉ/tài khoản lọt ra.
          let safeJson;
          try {
            safeJson = redactPrivate(entry.body);
          } catch (e) {
            await bump("miss");
            return harvestResult({
              message: `${e?.message || "redact-failed"} — SP ${itemId} dòng ${rowNumber}.`,
              tabId,
              pageUrl: await getCurrentTabUrl(tabId, link),
              itemId,
              shopId: check.shopId || shopId,
            });
          }
          await bump(source === "hook" ? "hook" : "hookReload");
          return harvestResult({
            ok: true,
            scrapeOk: true,
            harvestOk: true,
            // Giống bước scrape: thành công mà captcha=true nghĩa là ĐÃ giải tay captcha giữa chừng.
            captcha: captchaWaited,
            message: source === "hook"
              ? `Đã lấy dữ liệu sản phẩm ${itemId}.`
              : `Đã lấy dữ liệu sản phẩm ${itemId} (sau khi tải lại trang).`,
            tabId,
            pageUrl: await getCurrentTabUrl(tabId, link),
            itemId,
            shopId: check.shopId || shopId,
            json: safeJson, // chuỗi GỐC đã cắt riêng tư, phần còn lại nguyên từng byte
            source,
            capturedUrl: entry.url,
            capturedAt: entry.at,
          });
        }
        lastReject = check.reason;
      }
      if (error) {
        if (stopped()) return stopResult({ tabId, pageUrl: link, itemId, shopId });
        // Có lỗi mà chưa có entry: dò verify/captcha TRƯỚC khi kết luận chặn — có captcha thì đi đường captcha.
        const url = await getCurrentTabUrl(tabId, currentPageUrl || link);
        if (isVerifyUrl(url) || (await detectCaptcha(tabId)).detected) return { deferToCaptcha: true };
        // Không captcha: chờ thêm rồi đọc kho lại — trang có thể vừa tự gọi lại get_pc thành công (kho xoá lỗi).
        if (!recheckedError) {
          recheckedError = true;
          await sleep(PDP_ERROR_RECHECK_MS);
          if (stopped()) return stopResult({ tabId, pageUrl: link, itemId, shopId });
          return probe(source);
        }
        const code = Number(error.error);
        if (blockErrorCodes.includes(code)) {
          await bump("blocked");
          // C# xử lý trafficError: lần đầu đổi IP thử lại, lần sau đổi tài khoản (MarkTrafficBlock).
          return harvestResult({
            trafficError: true,
            blocked: true,
            pdpError: error.error,
            message: `Shopee chặn get_pc SP ${itemId} (error=${error.error}) dòng ${rowNumber} — coi như chặn bot.`,
            tabId,
            pageUrl: url,
            itemId,
            shopId,
          });
        }
        // Mã lỗi KHÔNG thuộc danh sách chặn → lỗi dòng (không traffic), giữ pdpError để soi.
        await bump("miss");
        return harvestResult({
          pdpError: error.error,
          message: `get_pc SP ${itemId} trả lỗi (error=${error.error}) dòng ${rowNumber} — lỗi dòng, không coi là chặn.`,
          tabId,
          pageUrl: url,
          itemId,
          shopId,
        });
      }
      return null;
    };

    // Hết trần TỔNG của bước → lỗi dòng (không captcha, không traffic).
    const timedOutResult = (pageUrl) => harvestResult({
      message: `Quá trần ${STEP_TOTAL_TIMEOUT_MS / 1000}s cho một bước (dòng ${rowNumber}) — bỏ dòng, launcher xử tiếp.`,
      tabId,
      pageUrl,
      itemId,
      shopId,
    });

    // Dừng vì captcha: bị bước mới thay → stopResult; quá trần bước → lỗi dòng; còn lại giữ đúng cờ như bước scrape.
    const captchaResult = (wait, pageUrl) => {
      if (superseded()) return stopResult({ tabId, pageUrl, itemId, shopId });
      if (wait.stepTimedOut) return timedOutResult(pageUrl);
      return harvestResult({
        captcha: true,
        aborted: Boolean(wait.aborted),
        message: wait.message || `Dang dung vi captcha - ${instanceName}, dong ${rowNumber}.`,
        tabId,
        pageUrl,
        itemId,
        shopId,
      });
    };

    // 1) Đọc kho NGAY: trang đã tải xong mà có dữ liệu thì xong luôn (chữ giống captcha trên trang không làm lỡ).
    //    deferToCaptcha → có lỗi get_pc nhưng đang captcha: KHÔNG kết luận, để nhánh captcha bên dưới lo.
    if (itemId) {
      const hit = await probe("hook");
      if (hit && !hit.deferToCaptcha) return hit;
    }

    // 2) Chưa có dữ liệu → mới xét verify / proxy / captcha.
    if (isVerifyUrl(currentPageUrl)) {
      await injectOverlayManager(tabId);
      const verifyWait = await waitForCaptchaToClear(tabId, ctx);
      if (!verifyWait.ok) return captchaResult(verifyWait, currentPageUrl || link);
      captchaWaited ||= Boolean(verifyWait.waited);
      if (verifyWait.waited && itemId) { await clearPdpError(tabId, itemId); recheckedError = false; }
      currentPageUrl = await waitForUrlStable(tabId);
    }
    if (stopped()) return stopResult({ tabId, pageUrl: currentPageUrl || link, itemId, shopId });

    if (await isProxyErrorPage(tabId)) {
      return harvestResult({
        proxyError: true,
        message: `Lỗi proxy / không tải được trang (dòng ${rowNumber}).`,
        tabId,
        pageUrl: currentPageUrl || link,
        itemId,
        shopId,
      });
    }

    if (stopped()) return stopResult({ tabId, pageUrl: currentPageUrl || link, itemId, shopId });
    await injectOverlayManager(tabId);
    await showOverlay(tabId, statusText);

    const firstCaptcha = await waitForCaptchaToClear(tabId, ctx);
    if (!firstCaptcha.ok) return captchaResult(firstCaptcha, await getCurrentTabUrl(tabId, currentPageUrl || link));
    captchaWaited ||= Boolean(firstCaptcha.waited);
    if (firstCaptcha.waited && itemId) { await clearPdpError(tabId, itemId); recheckedError = false; }
    if (firstCaptcha.waited) currentPageUrl = await waitForUrlStable(tabId);
    if (stopped()) return stopResult({ tabId, pageUrl: currentPageUrl || link, itemId, shopId });

    if (!itemId || !isShopeeProductUrl(await getCurrentTabUrl(tabId, currentPageUrl))) {
      const pageUrl = await getCurrentTabUrl(tabId, currentPageUrl || link);
      return harvestResult({
        notProduct: true,
        message: `URL hien tai khong phai link san pham Shopee: ${pageUrl || "(trong)"}`,
        tabId,
        pageUrl,
        itemId,
        shopId,
      });
    }

    // 3) Chờ tối đa PDP_WAIT_MS. Trong lúc chờ vẫn dò traffic/verify/captcha (trang SP có thể bị đá sang /verify giữa
    // chừng); giải tay xong thì tính lại hạn (trang thường tải lại → hook bắt lượt gọi mới). null = hết hạn.
    const waitForPdp = async (source) => {
      let deadline = Date.now() + PDP_WAIT_MS;
      while (Date.now() < deadline) {
        if (stopped()) return stopResult({ tabId, pageUrl: link, itemId, shopId });
        if (stepExpired()) { await bump("miss"); return timedOutResult(await getCurrentTabUrl(tabId, link)); }
        if (!(await getTabSafe(tabId))) {
          return harvestResult({ message: "Tab đã đóng khi đang chờ dữ liệu sản phẩm.", tabId, pageUrl: link, itemId, shopId });
        }

        const hit = await probe(source);
        if (hit && !hit.deferToCaptcha) return hit; // deferToCaptcha → rơi xuống nhánh captcha bên dưới
        if (stopped()) return stopResult({ tabId, pageUrl: link, itemId, shopId });

        const url = await getCurrentTabUrl(tabId, currentPageUrl);
        if (isTrafficErrorUrl(url)) {
          return harvestResult({
            trafficError: true,
            message: `Shopee chan traffic (verify/traffic/error) dong ${rowNumber}.`,
            tabId,
            pageUrl: url,
            itemId,
            shopId,
          });
        }
        if (isVerifyUrl(url) || (await detectCaptcha(tabId)).detected) {
          const midWait = await waitForCaptchaToClear(tabId, ctx);
          if (!midWait.ok) return captchaResult(midWait, await getCurrentTabUrl(tabId, url));
          captchaWaited ||= Boolean(midWait.waited);
          if (midWait.waited) {
            await clearPdpError(tabId, itemId);
            recheckedError = false;
            currentPageUrl = await waitForUrlStable(tabId);
            deadline = Date.now() + PDP_WAIT_MS;
            continue;
          }
        }

        await sleep(PDP_POLL_MS);
      }
      return null;
    };

    const firstWait = await waitForPdp("hook");
    if (firstWait) return firstWait;

    // 4) Dự phòng: trang SP vẫn bình thường, Shopee không báo lỗi → tải lại trang MỘT lần (không tự gọi get_pc).
    if (stopped()) return stopResult({ tabId, pageUrl: link, itemId, shopId });
    if (stepExpired()) { await bump("miss"); return timedOutResult(await getCurrentTabUrl(tabId, link)); }
    const beforeReloadUrl = await getCurrentTabUrl(tabId, currentPageUrl || link);
    // Cuối lượt chờ: dò verify/captcha TRƯỚC nhánh notProduct (trang bị đá sang /verify trông như "rời SP").
    if (isVerifyUrl(beforeReloadUrl) || (await detectCaptcha(tabId)).detected) {
      const endWait = await waitForCaptchaToClear(tabId, ctx);
      if (!endWait.ok) return captchaResult(endWait, await getCurrentTabUrl(tabId, beforeReloadUrl));
      captchaWaited ||= Boolean(endWait.waited);
      if (endWait.waited) {
        await clearPdpError(tabId, itemId);
        recheckedError = false;
        currentPageUrl = await waitForUrlStable(tabId);
        const afterCaptcha = await waitForPdp("hook");
        if (afterCaptcha) return afterCaptcha;
      }
    }
    if (!isShopeeProductUrl(await getCurrentTabUrl(tabId, beforeReloadUrl))) {
      await bump("miss");
      return harvestResult({
        notProduct: true,
        message: `Trang roi khoi san pham khi cho du lieu: ${beforeReloadUrl || "(trong)"}`,
        tabId,
        pageUrl: beforeReloadUrl,
        itemId,
        shopId,
      });
    }

    if (stopped()) return stopResult({ tabId, pageUrl: link, itemId, shopId });
    console.log(`[harvest] Chưa bắt được get_pc SP ${itemId} sau ${PDP_WAIT_MS / 1000}s → tải lại trang một lần.`);
    await bump("reload");
    if (stopped()) return stopResult({ tabId, pageUrl: link, itemId, shopId }); // B chen vào lúc bump
    try {
      await timed(chrome.tabs.reload(tabId));
    } catch (_) {}
    await waitTabComplete(tabId, TAB_LOAD_TIMEOUT_MS);
    if (stopped()) return stopResult({ tabId, pageUrl: link, itemId, shopId });
    currentPageUrl = await waitForUrlStable(tabId);
    if (stopped()) return stopResult({ tabId, pageUrl: currentPageUrl || link, itemId, shopId });

    const secondWait = await waitForPdp("hook-reload");
    if (secondWait) return secondWait;

    if (stopped()) return stopResult({ tabId, pageUrl: link, itemId, shopId });
    await bump("miss");
    // captcha=false CỐ Ý (khác bước scrape): đã giải xong mà vẫn không có dữ liệu là lỗi dòng, không phải captcha.
    return harvestResult({
      message: `Không lấy được dữ liệu sản phẩm ${itemId} (dòng ${rowNumber}) sau khi tải lại trang` +
        `${lastReject ? ` — ${lastReject}` : ""}.`,
      tabId,
      pageUrl: await getCurrentTabUrl(tabId, link),
      itemId,
      shopId,
    });
  } catch (error) {
    // superseded/aborted KHÔNG tự gán ở đây: finish() ở ngoài là nơi DUY NHẤT quyết định (stopped → stopResult),
    // nên lối ra lỗi của một bước đã bị thay vẫn ra superseded:true qua finish().
    return harvestResult({
      message: error?.message || String(error),
      pageUrl: payload?.link || "",
    });
  }
  })());
};

globalThis.__launcherCheckBeforeNextLink = async (payload) => {
  const tabId = payload?.tabId ? Number(payload.tabId) : null;
  if (!tabId || !(await getTabSafe(tabId))) {
    return { ok: true, waited: false, tabId: null, pageUrl: "" };
  }

  const context = {
    instanceName: String(payload?.instanceName || "Profile").trim(),
    rowNumber: Number(payload?.rowNumber) || 0,
    sku: String(payload?.sku || "").trim(),
  };
  const result = await checkCurrentLinkBeforeNext(tabId, context);
  return { ...result, tabId, pageUrl: result.pageUrl || await getCurrentTabUrl(tabId, "") };
};

globalThis.__launcherShowOverlay = async ({ tabId, text }) => {
  if (!tabId || !(await getTabSafe(tabId))) return { ok: false };
  try {
    await injectOverlayManager(tabId);
    await showOverlay(tabId, text || "");
    return { ok: true };
  } catch (_) {
    return { ok: false };
  }
};

globalThis.__launcherHideOverlay = async ({ tabId }) => {
  if (!tabId || !(await getTabSafe(tabId))) return { ok: false };
  try {
    await hideOverlay(tabId);
    return { ok: true };
  } catch (_) {
    return { ok: false };
  }
};

globalThis.__launcherAbortStep = async () => {
  abortRequested = true;
  return { ok: true };
};

/** Launcher ghi trạng thái hiển thị cho popup (read-only). */
globalThis.__launcherSetDisplayState = async (state) => {
  await timed(chrome.storage.local.set({ runnerState: state || { running: false } }));
  syncKeepAlive(state || { running: false });
  await broadcastState();
  return { ok: true };
};

globalThis.__launcherGetRunnerState = async () => {
  const data = await chrome.storage.local.get(["runnerState", "lastRunConfig"]);
  return {
    runnerState: data.runnerState || { running: false },
    lastRunConfig: data.lastRunConfig || {},
  };
};

globalThis.__launcherApplyFormConfig = async (config) => {
  const sheetName = String(config?.sheetName || "").trim();
  const startRow = Number(config?.startRow);
  const endRow = Number(config?.endRow);
  const lastRunConfig = {
    sheetName,
    startRow: Number.isInteger(startRow) && startRow > 0 ? startRow : "",
    endRow: Number.isInteger(endRow) && endRow > 0 ? endRow : "",
  };
  await timed(chrome.storage.local.set({ lastRunConfig }));

  const state = await readState();
  const nextSheet = sheetName || state.sheetName;
  const nextStart = lastRunConfig.startRow || state.startRow;
  const nextEnd = lastRunConfig.endRow || state.endRow;

  // ĐỔI KHỐI (bộ sheet/từ dòng/đến dòng khác bộ đang lưu) → tiến độ trong runnerState là RÁC của khối TRƯỚC
  // (profile Brave dùng lại theo tk Shopee nên Local Extension Settings không bị dọn). TRÙNG cả 3 → GIỮ NGUYÊN:
  // watchdog relaunch giữa chừng chạy lại đúng khối này và resume nhờ chính lastCompletedRow đó.
  const same = (a, b) => String(a ?? "") === String(b ?? "");
  const sameBlock =
    same(state.sheetName, nextSheet) && same(state.startRow, nextStart) && same(state.endRow, nextEnd);

  const patch = { sheetName: nextSheet, startRow: nextStart, endRow: nextEnd };
  if (!sameBlock) {
    // 2 mốc tiến độ THẬT của runnerState (extension này không lưu stoppedAtRow — C# tự suy ra từ 2 mốc này).
    patch.lastCompletedRow = null;
    patch.currentRow = null;
  }
  await writeState(patch);
  await broadcastState();
  return { ok: true, lastRunConfig, blockChanged: !sameBlock };
};

/** Hủy bước đang chạy + đánh dấu đã dừng. */
globalThis.__launcherStopRun = async () => {
  await globalThis.__launcherAbortStep();
  const next = await writeState({
    running: false,
    phase: "stopped",
    lastMessage: "Đã dừng từ Shopee Harvest.",
  });
  await broadcastState();
  return {
    ok: true,
    lastCompletedRow: next.lastCompletedRow ?? null,
    currentRow: next.currentRow ?? null,
    sheetName: next.sheetName || next.lastSheetName || "",
    phase: next.phase,
  };
};

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (!message || message.type !== "LAUNCHER_INVOKE") return undefined;

  const invoke = async () => {
    if (message.method === "probe") {
      return {
        ok: true,
        result: {
          // "sẵn sàng" = có ĐỦ các hàm launcher (kể cả applyFormConfig). Cờ RIÊNG hasHarvestStep (không trả
          // hasScrapeStep) → launcher Harvest không nhận nhầm SW của shopee-scrape và ngược lại.
          hasHarvestStep:
            typeof globalThis.__launcherExecuteHarvestStep === "function" &&
            typeof globalThis.__launcherApplyFormConfig === "function",
        },
      };
    }

    const handlers = {
      executeHarvestStep: globalThis.__launcherExecuteHarvestStep,
      checkBeforeNextLink: globalThis.__launcherCheckBeforeNextLink,
      setDisplayState: globalThis.__launcherSetDisplayState,
      getRunnerState: globalThis.__launcherGetRunnerState,
      applyFormConfig: globalThis.__launcherApplyFormConfig,
      showOverlay: globalThis.__launcherShowOverlay,
      hideOverlay: globalThis.__launcherHideOverlay,
      abortStep: globalThis.__launcherAbortStep,
      stopRun: globalThis.__launcherStopRun,
      notifyRunnerUi: async () => {
        await broadcastState();
        return { ok: true };
      },
    };

    const fn = handlers[message.method];
    if (typeof fn !== "function") {
      return { ok: false, error: `Extension chưa hỗ trợ ${message.method}.` };
    }

    const result = await fn(message.payload);
    return { ok: true, result };
  };

  invoke()
    .then((response) => sendResponse(response))
    .catch((err) => sendResponse({ ok: false, error: String(err?.message || err) }));
  return true;
});
