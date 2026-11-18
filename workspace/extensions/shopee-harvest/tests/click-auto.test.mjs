// GĐ7: chạy THẬT background.js (chrome.* giả + đồng hồ ảo + fetch giả) cho luồng hộp nổi:
// - chế độ tự động: prepareHarvestClick (mở link, gắn ngữ cảnh, chờ hộp) → hộp báo cú bấm → lấy + đẩy Hub kèm nguồn →
//   getHarvestClickResult(token);
// - không có hộp để bấm (trang đăng nhập) → chạy bước tại chỗ, trả đúng loại kết quả;
// - Hub 503 → hàng đợi + alarm; 401 ở chế độ tự động → failed; bấm tay → không nguồn.
// Chạy: node --test extensions/shopee-harvest/tests/*.test.mjs (từ workspace/).
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

// ── Đồng hồ ảo (như harvest-step.test.mjs) ─────────────────────────────────────────────────────────────────────────
let clock = 1_700_000_000_000;
const realNow = Date.now;
Date.now = () => clock;
const realSetTimeout = globalThis.setTimeout;
globalThis.setTimeout = (fn, ms = 0, ...args) => {
  const due = clock + Math.max(0, Number(ms) || 0);
  const h = { cancelled: false };
  setImmediate(() => { if (!h.cancelled) { clock = Math.max(clock, due); fn(...args); } });
  return h;
};
globalThis.clearTimeout = (h) => { if (h) h.cancelled = true; };
globalThis.setInterval = () => 0;
globalThis.clearInterval = () => {};
const realLog = console.log;
console.log = (...a) => { if (!String(a[0]).startsWith("[harvest]")) realLog(...a); };

const HERE = path.dirname(fileURLToPath(import.meta.url));
const FIXTURE = fs.readFileSync(path.resolve(HERE, "../../../tests/Shopee.Core.Tests/Fixtures/pdp/pdp-khong-bien-the.json"), "utf8");
const FIX = JSON.parse(FIXTURE).data.item;
const ITEM = String(FIX.item_id);
const SHOP = String(FIX.shop_id);
const LINK = `https://shopee.vn/San-pham-i.${SHOP}.${ITEM}`;

// ── chrome.* giả ──────────────────────────────────────────────────────────────────────────────────────────────────
const sc = {};
const storage = {};
const tab = { id: 7, url: "about:blank", status: "complete", active: true };
const msgListeners = [];
const alarms = {};
const fetches = [];

globalThis.fetch = async (url, init) => {
  fetches.push({ url, body: JSON.parse(init.body), headers: init.headers });
  const r = sc.hub ? sc.hub(url, init) : { status: 200 };
  if (r.throws) throw new TypeError("Failed to fetch");
  const body = JSON.parse(init.body);
  const json = r.json ?? (url.endsWith("/batch")
    ? { results: body.items.map((_, i) => ({ index: i, itemId: Number(ITEM), ok: true, applied: true })) }
    : { items: [] });
  return { status: r.status ?? 200, json: async () => json };
};

globalThis.chrome = {
  runtime: {
    getPlatformInfo: () => {},
    sendMessage: async () => {},
    openOptionsPage: () => { sc.optionsOpened = true; },
    onMessage: { addListener: (fn) => msgListeners.push(fn) },
  },
  alarms: {
    create: (name, info) => { alarms[name] = info; },
    clear: (name) => { delete alarms[name]; sc.cleared = [...(sc.cleared || []), name]; },
    onAlarm: { addListener: () => {} },
  },
  permissions: { contains: async ({ origins }) => (sc.denyPermission ? false : origins.length > 0) },
  storage: {
    local: {
      get: async (keys) => {
        const ks = Array.isArray(keys) ? keys : [keys];
        return Object.fromEntries(ks.filter((k) => k in storage).map((k) => [k, structuredClone(storage[k])]));
      },
      set: async (obj) => { Object.assign(storage, structuredClone(obj)); },
      remove: async (keys) => { for (const k of keys) delete storage[k]; },
    },
  },
  tabs: {
    get: async (id) => { if (id !== tab.id) throw new Error("No tab"); return { ...tab }; },
    query: async () => [{ ...tab }],
    update: async (id, { url }) => { tab.url = sc.redirect ? sc.redirect(url) : url; sc.opened = (sc.opened || 0) + 1; return { ...tab }; },
    create: async ({ url }) => { tab.url = url; return { ...tab }; },
    reload: async () => { sc.reloaded = (sc.reloaded || 0) + 1; },
    getZoom: async () => sc.zoom ?? 1,
    // Hộp nổi giả: trả lời PING theo kịch bản; ghi lại các bản cập nhật trạng thái gửi tới hộp.
    sendMessage: async (tabId, msg) => {
      if (msg.type === "HARVEST_PANEL_PING") {
        sc.pings = (sc.pings || 0) + 1;
        if (sc.panelReadyAfterPings != null && sc.pings < sc.panelReadyAfterPings) return { ready: false, reason: "not-ready" };
        if (sc.noPanel) throw new Error("Receiving end does not exist");
        return { ready: true, itemId: ITEM, href: tab.url, rect: { x: 1000, y: 740, width: 276, height: 31 }, viewport: { width: 1280, height: 800 }, pageScale: 1, devicePixelRatio: 1.25 };
      }
      if (msg.type === "HARVEST_PANEL_UPDATE") (sc.updates ||= []).push(msg.state);
      return undefined;
    },
    onUpdated: { addListener: () => {}, removeListener: () => {} },
    onRemoved: { addListener: () => {}, removeListener: () => {} },
  },
  scripting: {
    executeScript: ({ func, files }) => {
      if (files) return Promise.resolve([{ result: undefined }]);
      const src = String(func);
      if (src.includes("clearError")) return Promise.resolve([{ result: true }]);
      if (src.includes("__shopeeHarvestStore")) {
        const hit = sc.store !== false && tab.url.includes(`i.${SHOP}.${ITEM}`);
        return Promise.resolve([{ result: { entry: hit ? { itemId: ITEM, url: "get_pc", at: 1_700_000_000_123, via: "fetch", body: FIXTURE } : null, error: null } }]);
      }
      if (src.includes("verify to continue")) return Promise.resolve([{ result: { detected: false, captchaVisible: false, title: "", url: tab.url } }]);
      if (src.includes("list.some")) return Promise.resolve([{ result: false }]);
      return Promise.resolve([{ result: undefined }]);
    },
  },
};

await import("../background.js");

const ticks = async (n = 20) => { for (let i = 0; i < n; i++) await new Promise((r) => setImmediate(r)); };

const reset = (over = {}) => {
  for (const k of Object.keys(sc)) delete sc[k];
  for (const k of Object.keys(storage)) delete storage[k];
  for (const k of Object.keys(alarms)) delete alarms[k];
  fetches.length = 0;
  tab.url = "about:blank";
  Object.assign(sc, over);
};

/** Gửi tin như content script trên tab 7 gửi lên background; trả phản hồi. */
const fromPanel = (message) => new Promise((resolve) => {
  let answered = false;
  for (const fn of msgListeners) {
    const keep = fn({ type: "HARVEST_PANEL", ...message }, { tab: { id: tab.id, url: tab.url } }, (r) => { answered = true; resolve(r); });
    if (answered || keep === true) return;
  }
  if (!answered) resolve(undefined);
});

const APP_HUB = { url: "http://localhost:8088", token: "tok-app", machineId: "MAY-1" };
const prepare = (over = {}) => globalThis.__launcherPrepareHarvestClick({
  token: over.token || "t-1",
  link: LINK,
  rowNumber: 12,
  statusText: "Đang lấy dữ liệu dòng 12…",
  instanceName: "P1",
  tabId: 7,
  context: { accountId: "harvest", sheet: "Shop A", rowNo: 12, url: `https://shopee.vn/product/${SHOP}/${ITEM}`, sourceUrl: `${LINK}?sp_atk=x`, runId: "run-1", hub: APP_HUB },
  ...over,
});
const result = (token = "t-1") => globalThis.__launcherGetHarvestClickResult({ token });

const waitDone = async (token = "t-1") => {
  for (let i = 0; i < 200; i++) {
    const r = await result(token);
    if (r.state === "done") return r.result;
    await ticks(5);
  }
  throw new Error("lượt bấm không xong");
};

test.after(() => { Date.now = realNow; globalThis.setTimeout = realSetTimeout; console.log = realLog; });

test("tự động: prepare mở link → hộp sẵn sàng → trả toạ độ nút; CHƯA lấy dữ liệu khi chưa bấm (state pending)", async () => {
  reset();
  const p = await prepare();
  assert.equal(p.ready, true);
  assert.equal(p.tabId, 7);
  assert.equal(p.itemId, ITEM);
  assert.deepEqual(p.rect, { x: 1000, y: 740, width: 276, height: 31 });
  assert.equal(p.zoom, 1);              // zoom của tab (chrome.tabs.getZoom) để app tự kiểm
  assert.equal(p.pageScale, 1);
  assert.equal(p.devicePixelRatio, 1.25);
  assert.equal(tab.url, LINK);
  assert.equal((await result()).state, "pending");
  assert.equal(fetches.length, 0, "chưa bấm mà đã gửi Hub");
  assert.deepEqual(storage.hubAuto, APP_HUB); // hàng đợi profile auto gửi lại bằng cấu hình này

  // Hộp hỏi trạng thái trong lúc chờ bấm: thấy chế độ tự động, đã có cấu hình (của app), không hỏi known.
  const st = await fromPanel({ op: "status", href: tab.url });
  assert.equal(st.hubConfigured, true);
  assert.deepEqual(st.auto, { sheet: "Shop A", rowNo: 12 });
  assert.equal(st.known.status, "off");
  assert.equal(fetches.length, 0);
});

test("tự động: cú bấm trên hộp → lấy JSON tại chỗ (KHÔNG mở lại link) → đẩy Hub KÈM nguồn → kết quả saved cho app", async () => {
  reset();
  await prepare();
  const opened = sc.opened;
  const ack = await fromPanel({ op: "click", href: tab.url, itemId: ITEM });
  assert.deepEqual(ack, { accepted: true });
  const r = await waitDone();
  assert.equal(sc.opened, opened, "bấm nút không được điều hướng lại trang");
  assert.equal(r.ok, true);
  assert.equal(r.hub.status, "saved");
  assert.equal(r.itemId, ITEM);
  assert.equal(fetches.length, 1);
  assert.equal(fetches[0].url, "http://localhost:8088/api/harvested-products/batch");
  assert.equal(fetches[0].headers["X-Api-Token"], "tok-app");
  const item = fetches[0].body.items[0];
  assert.deepEqual([item.accountId, item.sheet, item.rowNo], ["harvest", "Shop A", 12]);
  assert.equal(item.sourceUrl, `${LINK}?sp_atk=x`);
  assert.equal(item.url, `https://shopee.vn/product/${SHOP}/${ITEM}`);
  assert.equal(item.harvestedAt, new Date(1_700_000_000_123).toISOString());
  assert.equal(r.json, item.rawJson); // app nhận JSON sạch (cùng chuỗi đã gửi Hub)
  const last = sc.updates.at(-1);
  assert.equal(last.busy, null);
  assert.equal(last.last.status, "saved");

  // Ngữ cảnh dùng MỘT lần: bấm lần nữa = bấm tay (không nguồn) — chưa cấu hình tay nên không gửi gì.
  await fromPanel({ op: "click", href: tab.url, itemId: ITEM });
  await ticks(200);
  assert.equal(fetches.length, 1);
});

test("tự động: zoom trình duyệt khác 100% → lời đáp báo đúng hệ số (app từ chối bấm)", async () => {
  reset({ zoom: 1.25 });
  const p = await prepare();
  assert.equal(p.ready, true);
  assert.equal(p.zoom, 1.25);
});

test("tự động: DỪNG lúc đang chờ hộp nổi → không lấy, không gửi Hub, kết quả aborted, keepalive tắt theo trạng thái launcher", async () => {
  reset({ noPanel: true });
  storage.runnerState = { running: false };
  const pr = prepare({ token: "t-abort" });
  for (let i = 0; i < 2000 && !(sc.pings >= 1); i++) await ticks(1);
  assert.ok(sc.pings >= 1, "chưa tới bước chờ hộp");
  await globalThis.__launcherAbortStep();
  const p = await pr;
  assert.equal(p.ready, false);
  assert.equal(p.result.aborted, true);
  assert.equal(p.result.ok, false);
  assert.equal(p.result.hub, undefined);
  assert.equal(fetches.filter((f) => f.url.endsWith("/batch")).length, 0);
  assert.equal(sc.updates, undefined, "không được bắt đầu lượt lấy (hộp không nhận trạng thái 'đang lấy')");
  assert.equal((await result("t-abort")).result.aborted, true);
  await ticks(20);
  assert.ok((sc.cleared || []).includes("sw-keepalive"), "keepalive phải tắt khi launcher không chạy");
  // Hộp không còn ngữ cảnh lượt app: cú bấm sau đó là bấm tay (không nguồn).
  const st = await fromPanel({ op: "status", href: LINK });
  assert.equal(st.auto, null);
});

test("bước lấy của luồng hộp nổi (keepAbort) KHÔNG xoá lệnh Dừng đã tới trước; bước thường thì xoá như cũ", async () => {
  reset();
  tab.url = LINK;
  await globalThis.__launcherAbortStep();
  const kept = await globalThis.__launcherExecuteHarvestStep({ link: LINK, tabId: 7, inPlace: true, keepAbort: true });
  assert.equal(kept.aborted, true);
  assert.equal(kept.ok, false);
  const normal = await globalThis.__launcherExecuteHarvestStep({ link: LINK, tabId: 7, inPlace: true });
  assert.equal(normal.ok, true);
});

test("tự động: DỪNG lúc đang tải trang → không chờ hộp, không lấy", async () => {
  reset();
  const pr = prepare({ token: "t-abort2" });
  await globalThis.__launcherAbortStep(); // tới ngay khi prepare vừa mở link
  const p = await pr;
  assert.equal(p.ready, false);
  assert.equal(p.result.aborted, true);
  assert.equal(sc.pings ?? 0, 0);
  assert.equal(fetches.length, 0);
});

test("tự động: hộp chưa sẵn sàng ngay → prepare chờ tới khi sẵn sàng", async () => {
  reset({ panelReadyAfterPings: 4 });
  const p = await prepare();
  assert.equal(p.ready, true);
  assert.ok(sc.pings >= 4);
});

test("tự động: không có hộp (bị đẩy sang trang đăng nhập) → chạy bước tại chỗ, kết quả loginRequired, không gửi Hub", async () => {
  reset({ redirect: () => "https://shopee.vn/buyer/login?next=x", store: false });
  const p = await prepare();
  assert.equal(p.ready, false);
  assert.equal(p.result.loginRequired, true);
  assert.equal(p.result.hub, undefined);
  assert.equal(fetches.length, 0);
  assert.equal((await result()).result.loginRequired, true);
});

test("tự động: Hub 503 → mục vào hàng đợi extension (queued) + hẹn alarm gửi lại; app KHÔNG được coi là đã lưu", async () => {
  reset({ hub: () => ({ status: 503, json: { error: "pg-unavailable" } }) });
  await prepare();
  await fromPanel({ op: "click", href: tab.url });
  const r = await waitDone();
  assert.equal(r.hub.status, "queued");
  assert.match(r.hub.reason, /503/);
  assert.equal(storage.hubQueue.items.length, 1);
  assert.equal(storage.hubQueue.items[0].profile, "auto");
  assert.ok(alarms["hub-queue-retry"], "chưa hẹn alarm gửi lại");

  // Hub sống lại: gửi lại bằng cấu hình app đã lưu (hubAuto), mục rời hàng đợi.
  sc.hub = null;
  const out = await globalThis.__launcherGetHarvestClickResult({ token: "x" }) && await new Promise((resolve) => {
    for (const fn of msgListeners) fn({ type: "HUB_QUEUE", op: "retry" }, {}, resolve);
  });
  assert.equal(out.saved, 1);
  assert.equal(storage.hubQueue.items.length, 0);
  assert.equal(fetches.at(-1).headers["X-Api-Token"], "tok-app");
});

test("tự động: Hub 401 → failed (app dừng job), không dồn hàng đợi", async () => {
  reset({ hub: () => ({ status: 401 }) });
  await prepare();
  await fromPanel({ op: "click", href: tab.url });
  const r = await waitDone();
  assert.equal(r.hub.status, "failed");
  assert.equal(storage.hubQueue?.items?.length ?? 0, 0);
});

test("tự động: trang đã sang SP khác trước khi bấm → lượt của app nhận lỗi, cú bấm thành bấm tay", async () => {
  reset();
  await prepare();
  const other = "https://shopee.vn/Khac-i.1.2";
  await fromPanel({ op: "click", href: other });
  const r = await waitDone();
  assert.equal(r.ok, false);
  assert.match(r.message, /SP khác/);
  // Cú bấm (đã thành bấm tay) vẫn đang chạy trên tab — chờ xong để không vướng test sau.
  for (let i = 0; i < 400 && (await fromPanel({ op: "status", href: other })).busy; i++) await ticks(5);
});

test("bấm tay: dùng cấu hình Cài đặt, gửi Hub KHÔNG nguồn; hộp hỏi known khi trang SP", async () => {
  reset();
  storage.hubSettings = { url: "https://hub.example.com", token: "tok-tay", machineId: "ext-may" };
  tab.url = LINK;
  sc.hub = (url) => (url.endsWith("/known") ? { status: 200, json: { items: [{ itemId: Number(ITEM), harvestedAt: "2026-11-01T01:00:00Z" }] } } : { status: 200 });
  // "Kiểm tra kết nối" của trang Cài đặt: hỏi known bằng cấu hình tay + xoá cache known (test trước vừa lưu SP này).
  const probe = await new Promise((resolve) => { for (const fn of msgListeners) fn({ type: "HUB_TEST" }, {}, resolve); });
  assert.equal(probe.kind, "ok");
  const st = await fromPanel({ op: "status", href: LINK });
  assert.equal(st.hubConfigured, true);
  assert.equal(st.auto, null);
  assert.deepEqual(st.known, { status: "yes", at: "2026-11-01T01:00:00Z" });

  await fromPanel({ op: "click", href: LINK });
  for (let i = 0; i < 200 && sc.updates?.at(-1)?.last == null; i++) await ticks(5);
  const batch = fetches.find((f) => f.url.endsWith("/batch"));
  assert.ok(batch, "chưa gửi Hub");
  assert.equal(batch.headers["X-Api-Token"], "tok-tay");
  assert.equal(batch.body.machineId, "ext-may");
  const item = batch.body.items[0];
  assert.deepEqual([item.accountId, item.sheet, item.rowNo, item.sourceUrl], [null, null, null, null]);
  assert.equal(sc.updates.at(-1).last.status, "saved");
});

test("bấm tay khi chưa cấu hình: hộp mở trang Cài đặt", async () => {
  reset();
  const st = await fromPanel({ op: "status", href: LINK });
  assert.equal(st.hubConfigured, false);
  await fromPanel({ op: "openOptions" });
  assert.equal(sc.optionsOpened, true);
});

test("probe báo hasHarvestClick; lệnh launcher gọi được 3 hàm mới", async () => {
  const res = await new Promise((resolve) => {
    for (const fn of msgListeners) fn({ type: "LAUNCHER_INVOKE", method: "probe" }, {}, resolve);
  });
  assert.equal(res.result.hasHarvestClick, true);
  assert.equal(res.result.hasHarvestStep, true);
  const unknown = await new Promise((resolve) => {
    for (const fn of msgListeners) fn({ type: "LAUNCHER_INVOKE", method: "getHarvestClickResult", payload: { token: "nope" } }, {}, resolve);
  });
  assert.deepEqual(unknown, { ok: true, result: { state: "unknown" } });
});
