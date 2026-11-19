/**
 * Shopee Harvest Runner — thin executor cho app Shopee Harvest (engine MultiBrave, điều khiển qua CDP).
 * Launcher điều khiển vòng lặp dòng / log / start-stop; extension chỉ:
 * - mở tab + hiện overlay thông báo
 * - chờ trang sản phẩm, lấy JSON get_pc mà trang tự tải (pdp-hook.js bắt, pdp-store.js giữ ở world ISOLATED);
 *   chưa có sau 30s thì tải lại trang MỘT lần rồi chờ thêm 30s. KHÔNG tự gọi get_pc.
 * - KHÔNG ghi tiến độ (lastCompletedRow): dữ liệu chỉ nằm trong kết quả trả về, launcher lưu outbox rồi mới ghi.
 * - GĐ7: hộp nổi "Lấy dữ liệu" (panel.js) — bấm tay hoặc app bấm qua CDP; extension tự làm sạch + đẩy Hub, Hub chưa nhận
 *   thì giữ trong hàng đợi riêng (xem mục "GĐ7" bên dưới).
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
import { pushCaptured, outcomeOf } from "./click-flow.js";
import { createHubQueue } from "./hub-queue.js";
import { postBatch, postKnown, isHubConfigured, hubPermissionPattern, normalizeHubUrl } from "./hub-client.js";
import { createSourceImageQueue, imageIdsOf, newRoundId } from "./source-images.js";

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

// GĐ7: token Hub nằm trong chrome.storage.local → chỉ ngữ cảnh tin cậy của extension (SW, popup, trang Cài đặt) được đọc;
// không content script nào của extension đọc storage (pdp-hook / pdp-store / panel / overlay chỉ nhắn tin). Trình duyệt
// không hỗ trợ setAccessLevel cho storage.local thì bỏ qua (mặc định của trình duyệt giữ nguyên).
try {
  const r = chrome.storage.local.setAccessLevel?.({ accessLevel: "TRUSTED_CONTEXTS" });
  if (r && typeof r.catch === "function") r.catch(() => {});
} catch (_) {}

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

/**
 * Trang ĐĂNG NHẬP / đăng ký của Shopee (tài khoản trong profile bị đăng xuất, Shopee đẩy link SP sang
 * /buyer/login?next=…): KHÔNG phải "không phải trang SP" — là lỗi phiên của tài khoản, launcher không được ghi SP vào
 * danh sách lỗi vĩnh viễn. Nhận: /buyer/login, /buyer/signup, /buyer/login/…, /user/login, accounts.shopee.*.
 */
const isShopeeLoginUrl = (url) => {
  try {
    const parsed = new URL(url);
    const host = parsed.hostname.toLowerCase();
    if (!/(^|\.)shopee\./i.test(host)) return false;
    if (/^accounts\.shopee\./i.test(host)) return true;
    return /^\/(buyer\/(login|signup|register)|user\/login|login)(\/|$)/i.test(parsed.pathname);
  } catch (_) {
    return false;
  }
};

/** Kết quả "tài khoản mất đăng nhập" (C# đọc cờ loginRequired — không tính lỗi dòng). */
const loginRequiredResult = ({ rowNumber, tabId, pageUrl, itemId, shopId }) => harvestResult({
  loginRequired: true,
  message: `Tai khoan Shopee mat dang nhap (bi chuyen toi trang dang nhap) dong ${rowNumber}: ${pageUrl || "(trong)"}`,
  tabId,
  pageUrl,
  itemId,
  shopId,
});

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
  loginRequired: false,
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
    // keepAbort (luồng hộp nổi GĐ7): lệnh Dừng đến TRƯỚC khi bước bắt đầu vẫn phải có hiệu lực — không xoá cờ huỷ.
    if (!payload?.keepAbort) abortRequested = false;
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

    // GĐ7 — inPlace: bấm nút trên hộp nổi (tay, hoặc app bấm sau khi đã tự mở trang) → lấy ngay trên tab đang mở, KHÔNG
    // điều hướng lại (điều hướng = tải lại trang, mất JSON đã bắt). Tab đã mất thì đi đường thường (mở link).
    const inPlace = Boolean(payload?.inPlace) && tabId != null;
    if (!inPlace) tabId = await openLinkInTab(tabId, link);

    // Khối thử tải lại: sau MỖI lần chờ đều kiểm stopped() — bước đã bị thay KHÔNG được điều hướng tab (tab dùng
    // chung, bước mới có thể đang dùng nó) về link cũ.
    let loaded = inPlace || await waitTabComplete(tabId, TAB_LOAD_TIMEOUT_MS);
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
      if (isShopeeLoginUrl(pageUrl)) return loginRequiredResult({ rowNumber, tabId, pageUrl, itemId, shopId });
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
      const leftUrl = await getCurrentTabUrl(tabId, beforeReloadUrl);
      if (isShopeeLoginUrl(leftUrl)) return loginRequiredResult({ rowNumber, tabId, pageUrl: leftUrl, itemId, shopId });
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

// ── GĐ7: hộp nổi "Lấy dữ liệu" (panel.js) + extension tự đẩy Hub ──────────────────────────────────────────────────
// Hai cách dùng, CÙNG một nút:
// - bấm tay (máy không có app Harvest): lấy JSON SP đang xem → làm sạch → đẩy Hub bằng cấu hình ở trang Cài đặt
//   (chrome.storage.local "hubSettings"), KHÔNG gắn nguồn (không account / sheet / dòng).
// - tự động (app Harvest): app gọi prepareHarvestClick (mở link + gửi ngữ cảnh: nguồn + cấu hình Hub của app) → chờ hộp sẵn
//   sàng → app BẤM NÚT bằng chuột thật qua CDP (isTrusted) → hộp báo về đây → lấy + đẩy Hub kèm nguồn → app đọc kết quả bằng
//   getHarvestClickResult(token).
// Lấy JSON đi đúng __launcherExecuteHarvestStep (inPlace: không điều hướng lại) nên luật chờ / tải lại một lần / captcha /
// notProduct / loginRequired / traffic giữ nguyên. Hub chưa nhận (mất mạng, 5xx) → hàng đợi BỀN (hub-queue.js) + alarm gửi lại.

const HUB_SETTINGS_KEY = "hubSettings"; // { url, token, machineId } người dùng nhập ở options.html
const HUB_AUTO_KEY = "hubAuto";         // cấu hình app Harvest truyền lần gần nhất — hàng đợi profile "auto" gửi lại bằng nó
const QUEUE_ALARM = "hub-queue-retry";
// Chờ hộp nổi báo sẵn sàng sau khi trang SP đã tải (content script document_idle + hỏi trạng thái).
const PANEL_READY_WAIT_MS = 15_000;
const PANEL_PING_TIMEOUT_MS = 3_000;
const KNOWN_CACHE_MS = 60_000;
// Kết quả lượt bấm giữ cho app đọc; quá hạn thì dọn.
const CLICK_RUN_KEEP_MS = 15 * 60_000;

const autoContexts = new Map(); // tabId → ngữ cảnh chế độ tự động (dùng MỘT lần, cho đúng SP app mở)
const clickRuns = new Map();    // token → { state: "pending"|"running"|"done", tabId, at, result }
const panelBusy = new Map();    // tabId → "capture" | "push"
const lastByTab = new Map();    // tabId → kết quả gần nhất (hiện trên hộp, kể cả khi trang bị đẩy sang đăng nhập / xác minh)
const knownCache = new Map();   // itemId → { at, value }

const readLocalKey = async (key) => {
  const got = await withTimeout(chrome.storage.local.get(key), CALL_TIMEOUT_MS, {});
  return got?.[key] ?? null;
};

/** Extension có quyền host tới Hub chưa (manifest cấp sẵn Hub mặc định + localhost; URL khác xin ở trang Cài đặt). */
const hasHubPermission = async (cfg) => {
  const pattern = hubPermissionPattern(cfg?.url);
  if (!pattern || !chrome.permissions?.contains) return false;
  try { return Boolean(await timed(chrome.permissions.contains({ origins: [pattern] }))); } catch (_) { return false; }
};

const hubQueue = createHubQueue({
  storage: {
    get: (keys) => timed(chrome.storage.local.get(keys)),
    set: (obj) => timed(chrome.storage.local.set(obj)),
    remove: (keys) => timed(chrome.storage.local.remove(keys)),
  },
  send: async (profile, bodies) => {
    const cfg = await readLocalKey(profile === "auto" ? HUB_AUTO_KEY : HUB_SETTINGS_KEY);
    if (!isHubConfigured(cfg)) return { kind: "nocfg", reason: "Chưa cấu hình Hub." };
    if (!(await hasHubPermission(cfg))) return { kind: "nocfg", reason: `Chưa cấp quyền truy cập ${normalizeHubUrl(cfg.url)}.` };
    return postBatch(cfg, bodies);
  },
});

/** Hẹn alarm gửi lại theo lùi dần của hàng đợi; hết mục chờ thì gỡ alarm. */
const scheduleQueue = async () => {
  try {
    const at = await hubQueue.nextAlarmAt();
    if (at > 0) chrome.alarms.create(QUEUE_ALARM, { when: Math.max(at, Date.now() + 1000) });
    else chrome.alarms.clear(QUEUE_ALARM);
  } catch (_) {}
};

const flushQueue = async (force = false) => {
  let out = null;
  try { out = await hubQueue.flush({ force }); } catch (_) {}
  await scheduleQueue();
  return out;
};

try {
  chrome.alarms.onAlarm.addListener((alarm) => {
    if (alarm && alarm.name === QUEUE_ALARM) flushQueue(false);
  });
} catch (_) {}
scheduleQueue();

const queueStats = async () => {
  try { return await hubQueue.stats(); } catch (_) { return { pending: 0, error: 0, bytes: 0 }; }
};

const pushDeps = {
  manualConfig: () => readLocalKey(HUB_SETTINGS_KEY),
  hasPermission: hasHubPermission,
  queue: hubQueue,
};

// ── GĐ9: ảnh gốc lên Hub (source-images.js) ─────────────────────────────────────────────────────────────────────────
// SP vừa lên Hub (saved / queued) → thêm việc ảnh vào hàng đợi BỀN. Lượt xử lý: app gọi startSourceImages đầu mỗi khoảng nghỉ
// giữa 2 SP (profile "auto", cấu hình app truyền) rồi hỏi sourceImagesStatus tới khi xong; bấm tay thì tự chạy 1 lượt ngắn sau
// lần bấm (profile "manual", cấu hình trang Cài đặt). Mỗi lúc chỉ MỘT lượt.
const MANUAL_SOURCE_BUDGET_MS = 90_000;
const MIN_SOURCE_BUDGET_MS = 5_000;
const MAX_SOURCE_BUDGET_MS = 10 * 60_000;

const sourceImages = createSourceImageQueue({
  storage: {
    get: (keys) => timed(chrome.storage.local.get(keys)),
    set: (obj) => timed(chrome.storage.local.set(obj)),
  },
});
let sourceRound = null; // { roundId, profile } của lượt đang chạy

/** Thêm việc ảnh cho SP vừa đẩy (JSON đã làm sạch). Lỗi KHÔNG được làm hỏng kết quả lấy dữ liệu → nuốt, trả lý do. */
const enqueueSourceImages = async (profile, result) => {
  try {
    const { ids, skipped } = imageIdsOf(result.json);
    let itemId = result.itemId != null ? String(result.itemId) : "";
    if (!itemId) { try { itemId = String(JSON.parse(result.json)?.data?.item?.item_id ?? ""); } catch (_) {} }
    return await sourceImages.enqueue({ profile, itemId, ids, skipped: skipped.length });
  } catch (e) {
    return { ok: false, reason: e?.message || String(e) };
  }
};

/** Bắt đầu một lượt chạy nền (không chờ). Đang có lượt khác → { started:false, roundId của lượt đó }. */
const startSourceRound = ({ profile, cfg, budgetMs, clearHubFull = false }) => {
  if (sourceRound) return { started: false, reason: "Đang chạy một lượt ảnh gốc khác.", roundId: sourceRound.roundId };
  const roundId = newRoundId();
  sourceRound = { roundId, profile };
  startKeepAlive(); // lượt dài hơn 30 s idle của SW (bấm tay: không có launcher giữ SW)
  sourceImages.runRound({ profile, cfg, budgetMs, roundId, clearHubFull })
    .catch(() => {})
    .finally(() => {
      sourceRound = null;
      readState().then(syncKeepAlive).catch(() => {});
    });
  return { started: true, roundId };
};

/** Bấm tay: SP vừa lên Hub → tự chạy 1 lượt ảnh nếu cấu hình tay dùng được và chưa có lượt nào. */
const startManualSourceRound = async () => {
  if (sourceRound) return;
  const cfg = await readLocalKey(HUB_SETTINGS_KEY);
  if (!isHubConfigured(cfg) || !(await hasHubPermission(cfg))) return;
  startSourceRound({ profile: "manual", cfg, budgetMs: MANUAL_SOURCE_BUDGET_MS });
};

const notifyPanel = async (tabId, patch) => {
  try { await timed(chrome.tabs.sendMessage(tabId, { type: "HARVEST_PANEL_UPDATE", state: patch }), PANEL_PING_TIMEOUT_MS); } catch (_) {}
};

/** Hỏi Hub SP đã có chưa (cache 60 s); chỉ khi bấm tay — chế độ tự động app đã tự hỏi trước khi mở trang. */
const knownFor = async (cfg, itemId) => {
  const cached = knownCache.get(itemId);
  if (cached && Date.now() - cached.at < KNOWN_CACHE_MS) return cached.value;
  if (!(await hasHubPermission(cfg))) return { status: "error", reason: "chưa cấp quyền truy cập Hub" };
  const r = await postKnown(cfg, [itemId]);
  if (r.kind !== "ok") return { status: "error", reason: r.reason };
  const hit = r.items.find((x) => String(x.itemId) === String(itemId));
  const value = hit ? { status: "yes", at: hit.harvestedAt } : { status: "no" };
  knownCache.set(itemId, { at: Date.now(), value });
  return value;
};

const panelStatus = async (tabId, href) => {
  const ctx = autoContexts.get(tabId) || null;
  const cfg = ctx ? ctx.hub : await readLocalKey(HUB_SETTINGS_KEY);
  const configured = isHubConfigured(cfg);
  const ids = isShopeeProductUrl(href) ? extractProductIds(href) : null;
  let known = { status: "off" };
  if (configured && ids && !ctx) {
    try { known = await knownFor(cfg, ids.itemId); } catch (e) { known = { status: "error", reason: e?.message || String(e) }; }
  }
  return {
    hubConfigured: configured,
    auto: ctx ? { sheet: ctx.sheet, rowNo: ctx.rowNo } : null,
    known,
    queue: await queueStats(),
    busy: panelBusy.get(tabId) || null,
    last: lastByTab.get(tabId) || null,
  };
};

const gcClickRuns = () => {
  const cutoff = Date.now() - CLICK_RUN_KEEP_MS;
  for (const [token, run] of clickRuns) if (run.at < cutoff) clickRuns.delete(token);
};

/**
 * Một lượt "Lấy dữ liệu" trên tab: lấy JSON (bước inPlace) → đẩy Hub (pushCaptured) → báo hộp + ghi kết quả cho app (token).
 * KHÔNG ném.
 */
const runCapture = async ({ tabId, link, ctx, token }) => {
  gcClickRuns();
  panelBusy.set(tabId, "capture");
  if (token) clickRuns.set(token, { state: "running", tabId, at: Date.now() });
  notifyPanel(tabId, { busy: "capture", last: null });
  let result;
  try {
    const step = await globalThis.__launcherExecuteHarvestStep({
      link,
      tabId,
      inPlace: true,
      keepAbort: true,
      rowNumber: ctx?.rowNumber ?? 0,
      statusText: ctx?.statusText,
      instanceName: ctx?.instanceName,
      blockErrorCodes: ctx?.blockErrorCodes,
    });
    if (step?.ok) {
      panelBusy.set(tabId, "push");
      notifyPanel(tabId, { busy: "push" });
    }
    result = await pushCaptured(step, ctx, pushDeps);
    // GĐ9: SP đã lên Hub (hoặc nằm bền trong hàng đợi gửi) → xếp việc ảnh gốc. Không bao giờ làm hỏng kết quả lấy dữ liệu.
    if ((result?.hub?.status === "saved" || result?.hub?.status === "queued") && typeof result.json === "string") {
      await enqueueSourceImages(ctx ? "auto" : "manual", result);
      if (!ctx) startManualSourceRound().catch(() => {});
    }
  } catch (e) {
    result = harvestResult({ message: e?.message || String(e), tabId, pageUrl: link });
  } finally {
    panelBusy.delete(tabId);
  }
  const outcome = outcomeOf(result);
  if (outcome.status === "queued") scheduleQueue();
  const itemId = result?.itemId != null ? String(result.itemId) : null;
  if (outcome.status === "saved" && itemId) knownCache.set(itemId, { at: Date.now(), value: { status: "yes", at: new Date().toISOString() } });
  const last = { ...outcome, at: Date.now(), itemId };
  lastByTab.set(tabId, last);
  if (token) clickRuns.set(token, { state: "done", tabId, at: Date.now(), result });
  await notifyPanel(tabId, {
    busy: null,
    last,
    queue: await queueStats(),
    ...(outcome.status === "saved" ? { known: { status: "yes", at: new Date().toISOString() } } : {}),
  });
  // Bấm tay: bước đã bật keepalive — trả về đúng trạng thái launcher (không giữ SW sống mãi).
  if (!ctx) readState().then(syncKeepAlive).catch(() => {});
  return result;
};

/** Hộp báo người dùng / app vừa bấm "Lấy dữ liệu" (cú bấm isTrusted). */
const onPanelClick = (tabId, href) => {
  if (panelBusy.has(tabId)) return { accepted: false, reason: "Đang xử lý lần bấm trước." };
  const ctx = autoContexts.get(tabId) || null;
  if (ctx) autoContexts.delete(tabId); // ngữ cảnh tự động chỉ dùng MỘT lần
  const ids = isShopeeProductUrl(href) ? extractProductIds(href) : null;
  const useCtx = ctx && (!ctx.expectItemId || ids?.itemId === ctx.expectItemId) ? ctx : null;
  // Bấm tay = ý định mới của người dùng: lệnh Dừng cũ của launcher không áp cho cú bấm này. Cú bấm của lượt app thì giữ cờ
  // (Dừng giữa lúc chờ bấm phải dừng thật).
  if (!useCtx) abortRequested = false;
  if (ctx && !useCtx) {
    // Trang đã sang SP khác trước khi bấm: lượt của app không có kết quả hợp lệ (lỗi tạm), cú bấm này thành bấm tay.
    clickRuns.set(ctx.token, {
      state: "done", tabId, at: Date.now(),
      result: harvestResult({ message: `Trang đã chuyển sang SP khác (${ids?.itemId ?? "?"}) trước khi bấm.`, tabId, pageUrl: href }),
    });
  }
  runCapture({ tabId, link: useCtx ? useCtx.link : href, ctx: useCtx, token: useCtx?.token ?? null });
  return { accepted: true };
};

/** Hỏi hộp trên tab: sẵn sàng bấm chưa + toạ độ nút. null = không trả lời / chưa sẵn sàng. */
const pingPanel = async (tabId, patch) => {
  try {
    return await timed(chrome.tabs.sendMessage(tabId, { type: "HARVEST_PANEL_PING", state: patch }), PANEL_PING_TIMEOUT_MS);
  } catch (_) {
    return null;
  }
};

const autoPanelPatch = (ctx) => ({ hubConfigured: isHubConfigured(ctx.hub), auto: { sheet: ctx.sheet, rowNo: ctx.rowNo } });

/** Mức phóng to của TAB (zoom trình duyệt, 1 = 100%) — app không quy đổi được toạ độ khi khác 1. null = không đọc được. */
const tabZoom = async (tabId) => {
  if (!chrome.tabs.getZoom) return null;
  try {
    const z = await timed(chrome.tabs.getZoom(tabId), PANEL_PING_TIMEOUT_MS);
    return Number.isFinite(z) ? z : null;
  } catch (_) {
    return null;
  }
};

/** Lời đáp "hộp sẵn sàng" gửi app: toạ độ nút + các hệ số phóng to để app tự kiểm. */
const readyAnswer = async (token, tabId, res, pageUrl) => ({
  ready: true,
  token,
  tabId,
  rect: res.rect,
  viewport: res.viewport,
  pageUrl: res.href || pageUrl,
  itemId: res.itemId,
  zoom: await tabZoom(tabId),
  pageScale: res.pageScale ?? null,
  devicePixelRatio: res.devicePixelRatio ?? null,
});

/** Lệnh Dừng tới giữa lúc chuẩn bị lượt bấm: không lấy, không gửi Hub; trả keepalive về đúng trạng thái launcher. */
const abortedPrepare = (token, tabId, link) => {
  autoContexts.delete(tabId);
  const result = harvestResult({ aborted: true, message: "Đã hủy.", tabId, pageUrl: link });
  clickRuns.set(token, { state: "done", tabId, at: Date.now(), result });
  readState().then(syncKeepAlive).catch(() => {});
  return { ready: false, token, tabId, result };
};

/** Chờ hộp sẵn sàng cho ĐÚNG SP app mở. Trang không còn là trang SP (đăng nhập / xác minh / lỗi) → thôi chờ ngay. */
const waitPanelReady = async (tabId, ctx) => {
  const deadline = Date.now() + PANEL_READY_WAIT_MS;
  while (Date.now() < deadline) {
    if (abortRequested) return null;
    const url = await getCurrentTabUrl(tabId, "");
    if (url && !isShopeeProductUrl(url)) return null;
    const res = await pingPanel(tabId, autoPanelPatch(ctx));
    if (res?.ready && (!ctx.expectItemId || res.itemId === ctx.expectItemId)) return res;
    await sleep(500);
  }
  return null;
};

/**
 * Chế độ tự động — bước 1: mở link, gắn ngữ cảnh cho tab, chờ hộp sẵn sàng. Trả { ready:true, token, tabId, rect, viewport,
 * pageUrl, itemId } để app bấm chuột thật vào nút; hoặc { ready:false, token, tabId, result } khi KHÔNG có hộp để bấm (trang
 * không phải SP, xác minh / captcha, đăng nhập, lỗi mạng, hộp không lên) — khi đó đã chạy bước lấy tại chỗ để ra đúng loại
 * kết quả như bước cũ (lấy được thì vẫn tự đẩy Hub kèm nguồn).
 */
globalThis.__launcherPrepareHarvestClick = async (payload) => {
  abortRequested = false;
  startKeepAlive();
  const link = normalizeLink(payload?.link);
  const token = String(payload?.token || "");
  if (!link || !token) return { ready: false, token, result: harvestResult({ message: "Thiếu link hoặc mã lượt bấm." }) };
  const c = payload?.context || {};
  const ctx = {
    auto: true,
    token,
    link,
    expectItemId: extractProductIds(link)?.itemId ?? null,
    hub: c.hub || null,
    accountId: c.accountId ?? null,
    sheet: c.sheet ?? null,
    rowNo: Number.isInteger(c.rowNo) ? c.rowNo : null,
    url: c.url || link,
    sourceUrl: c.sourceUrl ?? null,
    runId: c.runId ?? null,
    rowNumber: Number(payload?.rowNumber) || 0,
    statusText: payload?.statusText,
    instanceName: payload?.instanceName,
    blockErrorCodes: payload?.blockErrorCodes,
  };
  // Hàng đợi profile "auto" gửi lại bằng cấu hình này (app không ở đó để truyền lại).
  if (isHubConfigured(ctx.hub)) {
    try { await timed(chrome.storage.local.set({ [HUB_AUTO_KEY]: ctx.hub })); } catch (_) {}
  }

  let tabId = payload?.tabId ? Number(payload.tabId) : null;
  if (tabId && !(await getTabSafe(tabId))) tabId = null;
  try {
    tabId = await openLinkInTab(tabId, link);
  } catch (e) {
    return { ready: false, token, result: harvestResult({ message: `Không mở được link: ${e?.message || e}`, pageUrl: link }) };
  }
  for (const [t, old] of autoContexts) if (t === tabId || old.token === token) autoContexts.delete(t);
  autoContexts.set(tabId, ctx); // gắn TRƯỚC khi chờ: hộp hỏi trạng thái ngay khi trang vừa tải
  clickRuns.set(token, { state: "pending", tabId, at: Date.now() });

  let loaded = await waitTabComplete(tabId, TAB_LOAD_TIMEOUT_MS);
  if (abortRequested) return abortedPrepare(token, tabId, link);
  if (!loaded) loaded = await waitTabComplete(tabId, TAB_LOAD_TIMEOUT_MS);
  if (abortRequested) return abortedPrepare(token, tabId, link);
  let pageUrl = link;
  if (loaded) pageUrl = await waitForUrlStable(tabId);
  if (abortRequested) return abortedPrepare(token, tabId, link);
  const ready = loaded ? await waitPanelReady(tabId, ctx) : null;
  if (abortRequested) return abortedPrepare(token, tabId, link);
  if (ready) return readyAnswer(token, tabId, ready, pageUrl);

  autoContexts.delete(tabId);
  const result = await runCapture({ tabId, link, ctx, token });
  return { ready: false, token, tabId, result };
};

/** Chế độ tự động — bước 3: kết quả của lượt bấm. state: pending (chưa nhận cú bấm) | running | done | unknown (SW đã khởi
 * động lại / token lạ). */
globalThis.__launcherGetHarvestClickResult = async (payload) => {
  gcClickRuns();
  const run = clickRuns.get(String(payload?.token || ""));
  if (!run) return { state: "unknown" };
  return run.state === "done" ? { state: "done", result: run.result } : { state: run.state, tabId: run.tabId };
};

/** Toạ độ nút để app bấm lại (lần bấm trước không tới). */
globalThis.__launcherPanelButtonRect = async (payload) => {
  const tabId = Number(payload?.tabId);
  const ctx = autoContexts.get(tabId);
  if (!tabId || !ctx) return { ready: false, reason: "Không còn ngữ cảnh lượt bấm trên tab." };
  const res = await pingPanel(tabId, autoPanelPatch(ctx));
  return res?.ready ? readyAnswer(ctx.token, tabId, res, res.href) : { ready: false, reason: res?.reason || "Hộp không trả lời." };
};

/**
 * GĐ9 — app (đầu khoảng nghỉ giữa 2 SP): chạy NỀN một lượt ảnh gốc profile "auto" với cấu hình Hub app truyền ({ hub, budgetMs,
 * clearHubFull }). Trả NGAY { started:true, roundId } hoặc { started:false, reason[, roundId của lượt đang chạy] }. Có token — KHÔNG
 * ghi payload ra log.
 */
globalThis.__launcherStartSourceImages = async (payload) => {
  const cfg = payload?.hub || null;
  const budgetMs = Math.min(Math.max(0, Number(payload?.budgetMs) || 0), MAX_SOURCE_BUDGET_MS);
  // Bỏ mốc "Hub hết chỗ" NGAY, kể cả khi đang có lượt khác chạy (app chỉ gửi cờ này một lần sau mỗi lần bấm Chạy).
  let hubFullCleared = false;
  if (payload?.clearHubFull === true) { try { await sourceImages.clearHubFull(); hubFullCleared = true; } catch (_) {} }
  const answer = (r) => ({ ...r, hubFullCleared });
  if (sourceRound) return answer(startSourceRound({ profile: "auto", cfg, budgetMs }));
  if (budgetMs < MIN_SOURCE_BUDGET_MS) return answer({ started: false, reason: `Ngân sách lượt ảnh quá ngắn (${budgetMs} ms).` });
  if (isHubConfigured(cfg)) {
    // Như prepareHarvestClick: lượt sau / hàng đợi profile "auto" dùng lại cấu hình này.
    try { await timed(chrome.storage.local.set({ [HUB_AUTO_KEY]: cfg })); } catch (_) {}
    if (!(await hasHubPermission(cfg))) {
      return answer({ started: false, reason: `Extension chưa được cấp quyền truy cập ${normalizeHubUrl(cfg.url)}.` });
    }
  }
  return answer(startSourceRound({ profile: "auto", cfg, budgetMs }));
};

/** GĐ9 — trạng thái lượt ảnh: { running, round (lượt đang chạy, số tới lúc hỏi / lượt gần nhất), queue:{jobs,ids}, hubFullUntil }. */
globalThis.__launcherSourceImagesStatus = async () => sourceImages.status();

// Tin từ hộp nổi / popup / trang Cài đặt (KHÔNG phải lệnh của launcher).
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (!message) return undefined;
  const reply = (p) => { p.then((r) => sendResponse(r), (e) => sendResponse({ ok: false, error: String(e?.message || e) })); return true; };
  if (message.type === "HARVEST_PANEL") {
    const tabId = sender?.tab?.id;
    if (!tabId) return undefined;
    if (message.op === "status") return reply(panelStatus(tabId, String(message.href || sender.tab.url || "")));
    if (message.op === "click") { sendResponse(onPanelClick(tabId, String(message.href || sender.tab.url || ""))); return undefined; }
    if (message.op === "openOptions") { try { chrome.runtime.openOptionsPage(); } catch (_) {} sendResponse({ ok: true }); return undefined; }
    return undefined;
  }
  if (message.type === "HUB_QUEUE") {
    if (message.op === "stats") return reply(queueStats());
    if (message.op === "list") return reply(hubQueue.list());
    if (message.op === "retry") return reply((async () => { const n = await hubQueue.requeueErrors(); const out = await flushQueue(true); return { requeued: n, ...out }; })());
    return undefined;
  }
  if (message.type === "SOURCE_IMAGES") {
    if (message.op === "status") return reply(sourceImages.status());
    return undefined;
  }
  if (message.type === "HUB_TEST") {
    return reply((async () => {
      const cfg = await readLocalKey(HUB_SETTINGS_KEY);
      if (!isHubConfigured(cfg)) return { kind: "nocfg", reason: "Chưa đủ URL / token / mã máy." };
      if (!(await hasHubPermission(cfg))) return { kind: "nocfg", reason: `Chưa cấp quyền truy cập ${normalizeHubUrl(cfg.url)}.` };
      knownCache.clear();
      return postKnown(cfg, ["1"]);
    })());
  }
  return undefined;
});

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
  autoContexts.clear(); // lượt bấm chưa tới thì thôi — cú bấm sau đó (nếu có) là bấm tay
  sourceImages.abort(); // GĐ9: lượt ảnh gốc đang chạy dừng ngay (việc dở giữ cho lượt sau)
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
          hasHarvestClick: typeof globalThis.__launcherPrepareHarvestClick === "function",
          hasSourceImages: typeof globalThis.__launcherStartSourceImages === "function",
        },
      };
    }

    const handlers = {
      executeHarvestStep: globalThis.__launcherExecuteHarvestStep,
      prepareHarvestClick: globalThis.__launcherPrepareHarvestClick,
      getHarvestClickResult: globalThis.__launcherGetHarvestClickResult,
      panelButtonRect: globalThis.__launcherPanelButtonRect,
      startSourceImages: globalThis.__launcherStartSourceImages,
      sourceImagesStatus: globalThis.__launcherSourceImagesStatus,
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
