// Chạy THẬT background.js với chrome.* giả + đồng hồ ảo, để soi luồng __launcherExecuteHarvestStep và shape
// object trả về (C# đọc các trường này). Không thay được chốt chặn chạy tay trên Brave — chỉ bắt lỗi logic/shape.
// Chạy: node --test extensions/shopee-harvest/tests/*.test.mjs (từ workspace/).
import test from "node:test";
import assert from "node:assert/strict";

// ── Đồng hồ ảo: setTimeout chạy ngay ở lượt sau (setImmediate) và đẩy Date.now() tới mốc hẹn ──────────────
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

// ── chrome.* giả ──────────────────────────────────────────────────────────────────────────────────
const sc = {}; // kịch bản của từng test
const storage = {};
const tab = { id: 7, url: "", status: "complete", active: true };
const calls = [];
const never = new Promise(() => {});

globalThis.chrome = {
  runtime: {
    getPlatformInfo: () => {},
    sendMessage: async () => {},
    onMessage: { addListener: () => {} },
  },
  alarms: { create: () => {}, clear: () => {}, onAlarm: { addListener: () => {} } },
  storage: {
    local: {
      get: async (keys) => {
        const ks = Array.isArray(keys) ? keys : [keys];
        if (sc.onStorageGet) sc.onStorageGet(ks);
        return Object.fromEntries(ks.filter((k) => k in storage).map((k) => [k, structuredClone(storage[k])]));
      },
      set: (obj) => { if (sc.setHang) return never; Object.assign(storage, structuredClone(obj)); return Promise.resolve(); },
    },
  },
  tabs: {
    get: (id) => {
      sc.getN = (sc.getN || 0) + 1;
      const n = sc.getN;
      if (sc.onGet) sc.onGet(n);
      if (sc.getHang && sc.getHang()) return never;
      return (async () => {
        if (id !== tab.id || sc.tabGone || (sc.getGone && sc.getGone(n))) throw new Error("No tab");
        return { ...tab, url: sc.urlAt ? sc.urlAt(tab.url) : tab.url };
      })();
    },
    query: async () => { calls.push({ kind: "query", at: clock }); if (sc.onQuery) sc.onQuery(); return [{ ...tab }]; },
    update: async (id, { url }) => {
      sc.updN = (sc.updN || 0) + 1;
      if (typeof sc.updateThrows === "function" ? sc.updateThrows(sc.updN) : sc.updateThrows) throw new Error("update boom");
      tab.url = sc.redirect ? sc.redirect(url) : url;
      calls.push({ kind: "open", url: tab.url, at: clock });
      return { ...tab };
    },
    create: async ({ url }) => { if (sc.createThrows) throw new Error("create boom"); tab.url = url; return { ...tab }; },
    reload: async () => { calls.push({ kind: "reload", at: clock }); sc.reloaded = (sc.reloaded || 0) + 1; if (sc.onReload) sc.onReload(); },
    sendMessage: async () => {},
    onUpdated: { addListener: () => {}, removeListener: () => {} },
    onRemoved: { addListener: () => {}, removeListener: () => {} },
  },
  scripting: {
    executeScript: ({ func, files, args, world }) => {
      if (files) { calls.push({ kind: "overlay", at: clock }); return Promise.resolve([{ result: undefined }]); }
      const src = String(func);
      if (src.includes("clearError")) {
        calls.push({ kind: "clearError", world, args, at: clock });
        if (sc.onClearError) sc.onClearError(args[0]);
        return Promise.resolve([{ result: true }]);
      }
      if (src.includes("__shopeeHarvestStore")) {
        calls.push({ kind: "readStore", world, args, at: clock });
        if (sc.storeHang && sc.storeHang()) return never;
        const r = sc.store ? sc.store(args[0], tab.url) : null;
        return Promise.resolve([{ result: { entry: r?.entry || null, error: r?.error || null } }]);
      }
      if (src.includes("verify to continue")) {
        calls.push({ kind: "captcha", at: clock });
        const detected = sc.captchaAt ? sc.captchaAt() : false;
        return Promise.resolve([{ result: { detected, captchaVisible: detected, title: "", url: tab.url } }]);
      }
      if (src.includes("list.some")) {
        const pe = typeof sc.proxyError === "function" ? sc.proxyError() : sc.proxyError;
        return Promise.resolve([{ result: Boolean(pe) }]);
      }
      if (src.includes("__launcherOriginalTitle")) calls.push({ kind: "mark", at: clock });
      return Promise.resolve([{ result: undefined }]); // markCaptchaTab…
    },
  },
};

await import("../background.js");
const step = (payload) => globalThis.__launcherExecuteHarvestStep(payload);

const LINK = "https://shopee.vn/Ao-thun-i.123456.987654321";
const LINK2 = "https://shopee.vn/But-i.123456.555";
// Có khoảng trắng + chuỗi thoát (String.raw giữ nguyên): parse rồi stringify lại sẽ ra chuỗi KHÁC → bắt được nếu ai đó "chuẩn hoá" json.
const BODY = String.raw`{"error":null, "data":{"item":{"item_id":987654321,"shop_id":123456,"title":"Áo"}}}`;
const BODY2 = JSON.stringify({ error: null, data: { item: { item_id: 555, shop_id: 123456 } } });
// Body CÓ dữ liệu riêng tư — step phải trả json đã qua redactPrivate (account {}, delivery_address null).
const BODY_PRIVATE = '{"error":null,"data":{"account":{"userid":9},"item":{"item_id":987654321,"shop_id":123456},' +
  '"product_shipping":{"delivery_address":{"state":"HN","latitude":21.06}}}}';
// Body có khoá "data" TRÙNG, account nằm trong cái đầu (parse giữ cái sau). redactPrivate phải cắt cái đầu.
const BODY_DUP = '{"data":{"account":{"secret":1},"x":2},"data":{"item":{"item_id":987654321,"shop_id":1}},"error":null}';
const entry = (body = BODY) => ({ entry: { itemId: "x", url: "https://shopee.vn/api/v4/pdp/get_pc?item_id=987654321", at: 5, via: "fetch", body } });

let t0 = 0;
const reset = (over = {}) => {
  for (const k of Object.keys(sc)) delete sc[k];
  for (const k of Object.keys(storage)) delete storage[k];
  calls.length = 0;
  tab.url = "about:blank";
  t0 = clock;
  Object.assign(sc, over);
};
const count = (kind) => calls.filter((c) => c.kind === kind).length;
const since = (ms) => clock >= t0 + ms;

// Các trường C# (RunnerExtensionRpc.ScrapeStepResult) + phần Harvest — phải có mặt ở MỌI lối ra.
const assertFields = (r) => {
  for (const k of ["ok", "scrapeOk", "harvestOk", "captcha", "proxyError", "trafficError", "blocked", "notProduct", "loginRequired", "aborted", "superseded"]) {
    assert.equal(typeof r[k], "boolean", `thiếu cờ ${k}`);
  }
  assert.equal(typeof r.message, "string");
  for (const k of ["tabId", "pageUrl", "itemId", "shopId", "json", "source", "pdpError"]) assert.ok(k in r, `thiếu ${k}`);
};

test.after(() => { Date.now = realNow; globalThis.setTimeout = realSetTimeout; console.log = realLog; });

test("kho có dữ liệu ngay → ok, json chuỗi gốc, source hook, KHÔNG dò captcha, KHÔNG ghi lastCompletedRow", async () => {
  reset({ store: (id) => (id === "987654321" ? entry() : null) });
  const r = await step({ link: LINK, rowNumber: 12, instanceName: "P1", sku: "S1", tabId: 7, persistProgress: true });
  assertFields(r);
  assert.equal(r.ok, true);
  assert.equal(r.scrapeOk, true);
  assert.equal(r.harvestOk, true);
  assert.equal(r.captcha, false);
  assert.equal(r.itemId, "987654321");
  assert.equal(r.shopId, "123456");
  assert.equal(r.json, BODY);
  assert.equal(r.source, "hook");
  assert.equal(r.tabId, 7);
  assert.equal(r.pageUrl, LINK);
  assert.equal(storage.runnerState?.lastCompletedRow, undefined, "bước Harvest không được tự ghi tiến độ");
  assert.equal(storage.harvestStats.hook, 1);
  assert.ok(calls.filter((c) => c.kind === "readStore").every((c) => c.world === "ISOLATED"), "đọc kho phải ở world ISOLATED");
  assert.equal(count("reload"), 0);
});

test("đọc kho TRƯỚC captcha: trang có chữ giống captcha nhưng đã có PDP → vẫn ok, không gọi detectCaptcha", async () => {
  reset({ captchaAt: () => true, store: () => entry() });
  const r = await step({ link: LINK, rowNumber: 2, tabId: 7 });
  assert.equal(r.ok, true);
  assert.equal(r.captcha, false);
  assert.equal(count("captcha"), 0);
});

test("dữ liệu tới sau ~10s → ok source hook, không tải lại", async () => {
  reset({ store: () => (since(10_000) ? entry() : null) });
  const r = await step({ link: LINK, rowNumber: 3, tabId: 7 });
  assert.equal(r.ok, true);
  assert.equal(r.source, "hook");
  assert.equal(count("reload"), 0);
});

test("mốc chờ 30s: không có dữ liệu → tải lại MỘT lần sau đúng ~30s, rồi chờ thêm 30s mới báo không lấy được", async () => {
  reset();
  const r = await step({ link: LINK, rowNumber: 4, tabId: 7 });
  assertFields(r);
  assert.equal(r.ok, false);
  assert.equal(r.captcha, false);
  assert.equal(r.json, null);
  assert.match(r.message, /Không lấy được dữ liệu/);
  assert.equal(count("reload"), 1);
  const reloadAt = calls.find((c) => c.kind === "reload").at;
  const waited = reloadAt - t0; // = ổn định URL (2,5s) + chờ hook
  assert.ok(waited >= 30_000 && waited <= 36_000, `tải lại ở +${waited}ms`);
  const after = clock - reloadAt; // = ổn định URL sau reload + chờ lần 2
  assert.ok(after >= 30_000 && after <= 36_000, `báo hỏng sau reload +${after}ms`);
  assert.equal(storage.harvestStats.reload, 1);
  assert.equal(storage.harvestStats.miss, 1);
});

test("dữ liệu chỉ có sau khi tải lại → source hook-reload", async () => {
  reset({ store: () => (sc.reloaded ? entry() : null) });
  const r = await step({ link: LINK, rowNumber: 5, tabId: 7 });
  assert.equal(r.ok, true);
  assert.equal(r.source, "hook-reload");
  assert.equal(r.json, BODY);
  assert.equal(storage.harvestStats.reload, 1);
  assert.equal(storage.harvestStats.hookReload, 1);
});

test("Shopee trả get_pc có mã lỗi → trafficError + blocked, mã lỗi trong message, KHÔNG tải lại", async () => {
  reset({ store: () => (since(5_000) ? { error: { itemId: "987654321", error: "90309999" } } : null) });
  const r = await step({ link: LINK, rowNumber: 6, tabId: 7 });
  assertFields(r);
  assert.equal(r.ok, false);
  assert.equal(r.trafficError, true);
  assert.equal(r.blocked, true);
  assert.equal(r.pdpError, "90309999");
  assert.match(r.message, /90309999/);
  assert.equal(count("reload"), 0);
  assert.equal(storage.harvestStats.blocked, 1);
});

test("giải captcha giữa chừng → tính lại hạn 30s (dữ liệu tới ở +50s vẫn là hook, không tải lại)", async () => {
  reset({
    captchaAt: () => since(22_000) && !since(26_000),
    store: () => (since(50_000) ? entry() : null),
  });
  const r = await step({ link: LINK, rowNumber: 7, tabId: 7 });
  assert.equal(r.ok, true);
  assert.equal(r.captcha, true); // đã giải tay giữa chừng, như bước scrape
  assert.equal(r.source, "hook");
  assert.equal(count("reload"), 0);
});

test("trang đã rời SP khi hết 30s → notProduct, không tải lại", async () => {
  reset({ urlAt: (url) => (since(20_000) ? "https://shopee.vn/" : url) });
  const r = await step({ link: LINK, rowNumber: 8, tabId: 7 });
  assertFields(r);
  assert.equal(r.notProduct, true);
  assert.equal(count("reload"), 0);
  assert.equal(storage.harvestStats.miss, 1);
});

test("hủy trong lúc tải lại → aborted NGAY sau khi trang tải xong (không ổn định URL, không chờ lần 2)", async () => {
  reset({ onReload: () => globalThis.__launcherAbortStep() });
  const r = await step({ link: LINK, rowNumber: 9, tabId: 7 });
  assertFields(r);
  assert.equal(r.aborted, true);
  assert.equal(r.ok, false);
  const reloadAt = calls.find((c) => c.kind === "reload").at;
  assert.ok(clock - reloadAt < 2_000, `trả kết quả sau reload +${clock - reloadAt}ms`);
  assert.equal(calls.filter((c) => c.kind === "readStore" && c.at >= reloadAt).length, 0);
});

test("bước chồng bước: bước mới chạy thì bước cũ dừng (superseded), không ghi gì, không lẫn kết quả", async () => {
  reset({ store: (id) => (id === "555" ? { entry: { ...entry(BODY2).entry } } : null) });
  const first = step({ link: LINK, rowNumber: 10, tabId: 7 });
  for (let i = 0; i < 30; i++) await new Promise((r) => setImmediate(r)); // bước 1 đang chờ dữ liệu
  const second = step({ link: LINK2, rowNumber: 11, tabId: 7 });
  const [a, b] = await Promise.all([first, second]);
  assertFields(a);
  assert.equal(a.ok, false);
  assert.equal(a.aborted, true);
  assert.equal(a.superseded, true);
  assert.equal(a.json, null);
  assert.equal(b.ok, true);
  assert.equal(b.itemId, "555");
  assert.equal(b.json, BODY2);
  assert.equal(count("reload"), 0, "bước cũ không được tải lại trang");
  assert.deepEqual(storage.harvestStats, { ...storage.harvestStats, hook: 1, miss: 0, reload: 0 });
});

test("đọc kho bị treo → có trần thời gian, bước vẫn chạy tiếp và lấy được dữ liệu", async () => {
  let n = 0;
  reset({ storeHang: () => ++n <= 2, store: () => entry() });
  const r = await step({ link: LINK, rowNumber: 12, tabId: 7 });
  assert.equal(r.ok, true);
  assert.ok(n >= 3);
});

test("trang /verify/traffic/error → trafficError (không blocked)", async () => {
  reset({ redirect: () => "https://shopee.vn/verify/traffic/error?home_url=x" });
  const r = await step({ link: LINK, rowNumber: 13, tabId: 7 });
  assertFields(r);
  assert.equal(r.trafficError, true);
  assert.equal(r.blocked, false);
});

test("captcha không giải trong 3' → captcha=true, ok=false", async () => {
  reset({ captchaAt: () => true });
  const r = await step({ link: LINK, rowNumber: 14, instanceName: "P1", tabId: 7 });
  assertFields(r);
  assert.equal(r.captcha, true);
  assert.equal(r.ok, false);
  assert.match(r.message, /captcha/i);
});

test("lỗi proxy → proxyError", async () => {
  reset({ proxyError: true });
  const r = await step({ link: LINK, rowNumber: 15, tabId: 7 });
  assertFields(r);
  assert.equal(r.proxyError, true);
});

test("trang không phải SP (bị đẩy về trang chủ) → notProduct ngay, không ngồi chờ", async () => {
  reset({ redirect: () => "https://shopee.vn/" });
  const r = await step({ link: LINK, rowNumber: 16, tabId: 7 });
  assertFields(r);
  assert.equal(r.notProduct, true);
  assert.equal(count("reload"), 0);
  assert.ok(count("readStore") <= 1, "chỉ được đọc kho một lần (lượt đọc ngay sau khi tải)");
});

for (const loginUrl of [
  "https://shopee.vn/buyer/login?next=https%3A%2F%2Fshopee.vn%2Fao-i.1.2",
  "https://shopee.vn/buyer/signup?next=x",
  "https://accounts.shopee.vn/login?next=y",
]) {
  test(`trang đăng nhập (${loginUrl.slice(0, 40)}…) → loginRequired, KHÔNG notProduct`, async () => {
    reset({ redirect: () => loginUrl });
    const r = await step({ link: LINK, rowNumber: 17, tabId: 7 });
    assertFields(r);
    assert.equal(r.loginRequired, true);
    assert.equal(r.notProduct, false);
    assert.equal(r.ok, false);
    assert.equal(count("reload"), 0);
  });
}

test("trang không phải SP và không phải đăng nhập (vd /buyer/orders) → vẫn notProduct", async () => {
  reset({ redirect: () => "https://shopee.vn/user/purchase" });
  const r = await step({ link: LINK, rowNumber: 18, tabId: 7 });
  assert.equal(r.notProduct, true);
  assert.equal(r.loginRequired, false);
});

test("thiếu link → ok=false đủ cờ", async () => {
  reset();
  const r = await step({ rowNumber: 1 });
  assertFields(r);
  assert.equal(r.ok, false);
});

test("hủy giữa lúc chờ dữ liệu → aborted", async () => {
  let polls = 0;
  reset({ store: () => { if (++polls === 3) globalThis.__launcherAbortStep(); return null; } });
  const r = await step({ link: LINK, rowNumber: 17, tabId: 7 });
  assertFields(r);
  assert.equal(r.aborted, true);
  assert.equal(r.superseded, false);
  assert.equal(count("reload"), 0);
});

test("nhánh catch (trình duyệt ném lỗi khi mở tab) → vẫn đủ mọi cờ", async () => {
  reset({ updateThrows: true, createThrows: true });
  const r = await step({ link: LINK, rowNumber: 18, tabId: 7 });
  assertFields(r);
  assert.equal(r.ok, false);
  assert.match(r.message, /create boom/);
});

test("json trả về ĐÃ qua redactPrivate (account {}, delivery_address null), phần còn lại giữ byte", async () => {
  reset({ store: () => entry(BODY_PRIVATE) });
  const r = await step({ link: LINK, rowNumber: 19, tabId: 7 });
  assert.equal(r.ok, true);
  const j = JSON.parse(r.json);
  assert.deepEqual(j.data.account, {});
  assert.equal(j.data.product_shipping.delivery_address, null);
  // Không có userid/latitude của tài khoản trong chuỗi trả về.
  assert.equal(/"userid"|"latitude"/.test(r.json), false);
  // item vẫn nguyên.
  assert.equal(j.data.item.item_id, 987654321);
});

test("body khoá 'data' trùng → json trả về đã cắt account trong cái đầu (không lộ secret)", async () => {
  reset({ store: () => entry(BODY_DUP) });
  const r = await step({ link: LINK, rowNumber: 20, tabId: 7 });
  assert.equal(r.ok, true);
  assert.equal(/"secret"/.test(r.json), false, "secret của tài khoản không được lọt");
  assert.match(r.json, /"account":\{\}/);
});

test("mã lỗi KHÔNG thuộc danh sách chặn → lỗi dòng (ok=false, pdpError), không traffic/blocked", async () => {
  reset({ store: () => (since(5_000) ? { error: { itemId: "987654321", error: "4000123" } } : null) });
  const r = await step({ link: LINK, rowNumber: 21, tabId: 7 });
  assertFields(r);
  assert.equal(r.ok, false);
  assert.equal(r.trafficError, false);
  assert.equal(r.blocked, false);
  assert.equal(r.pdpError, "4000123");
  assert.match(r.message, /4000123/);
  assert.equal(count("reload"), 0);
});

test("payload.blockErrorCodes đè danh sách chặn → mã truyền vào thành chặn bot", async () => {
  reset({ store: () => (since(5_000) ? { error: { itemId: "987654321", error: "4000123" } } : null) });
  const r = await step({ link: LINK, rowNumber: 22, tabId: 7, blockErrorCodes: [4000123] });
  assert.equal(r.trafficError, true);
  assert.equal(r.blocked, true);
  assert.equal(r.pdpError, "4000123");
});

test("có mã lỗi NHƯNG đang hiện captcha → đi đường captcha, KHÔNG kết luận chặn", async () => {
  reset({ captchaAt: () => true, store: () => ({ error: { itemId: "987654321", error: "90309999" } }) });
  const r = await step({ link: LINK, rowNumber: 23, tabId: 7 });
  assertFields(r);
  assert.equal(r.captcha, true);
  assert.equal(r.blocked, false);
  assert.equal(r.trafficError, false);
  assert.equal(count("reload"), 0);
});

test("cuối lượt chờ: dò verify/captcha TRƯỚC nhánh notProduct (giải xong lấy được data, không notProduct)", async () => {
  reset({
    // Sau 30s trang nhảy sang /verify (nhìn như rời SP), giải xong (sau 34s) về SP; data có từ 35s.
    urlAt: (url) => (since(30_000) && !since(34_000) ? "https://shopee.vn/verify" : url),
    store: () => (since(35_000) ? entry() : null),
  });
  const r = await step({ link: LINK, rowNumber: 24, tabId: 7 });
  assertFields(r);
  assert.equal(r.ok, true, r.message);
  assert.equal(r.notProduct, false);
  assert.equal(r.captcha, true);
  assert.equal(count("reload"), 0);
});

test("trần TỔNG 540s: captcha lặp [0,170)[200,370)[420,590)s → bước kết thúc ≤540s, lỗi dòng (không captcha)", async () => {
  reset({
    captchaAt: () => {
      const t = clock - t0;
      return (t >= 0 && t < 170_000) || (t >= 200_000 && t < 370_000) || (t >= 420_000 && t < 590_000);
    },
    store: () => null,
  });
  const r = await step({ link: LINK, rowNumber: 25, tabId: 7 });
  assertFields(r);
  const elapsed = clock - t0;
  assert.ok(elapsed <= 545_000, `bước phải kết thúc ≤540s, thực tế ${Math.round(elapsed / 1000)}s`);
  assert.equal(r.ok, false);
  assert.equal(r.captcha, false, "quá trần là lỗi dòng, không phải captcha");
  assert.equal(r.trafficError, false);
  assert.match(r.message, /trần|tran/i);
});

test("trần TỔNG 540s (M2a): captcha CHỚP ngắn lặp lại (reset hạn liên tục) → waitForPdp vẫn chốt ≤~541s", async () => {
  // Captcha hiện ~500ms mỗi 20s: waitForCaptchaToClear vào rồi thoát nhanh (reset deadline), nên deadline 30s
  // KHÔNG tự hết; chỉ trần TỔNG ở ĐẦU vòng waitForPdp mới dừng được. Episode tránh xa mốc 540s để nếu bỏ guard
  // này thì phải chạy tới tận episode kế (545s) mới dừng (qua guard khác) → elapsed vượt ngưỡng.
  // Episode rộng 4s (poll 1s luôn trúng) mỗi 25s; episode cuối trước mốc là 525s, kế là 550s → quanh 540s
  // KHÔNG có captcha. Guard ở đầu waitForPdp chốt tại ~540s; bỏ guard đó thì phải chờ tới episode 550s (guard
  // khác) → elapsed vượt 542s. Lower-bound 500s tự kiểm: nếu episode bị bỏ lỡ, bước chốt sớm (~65s) → test đỏ.
  reset({
    captchaAt: () => { const t = (clock - t0) % 25_000; return t < 4_000; },
    store: () => null,
  });
  const r = await step({ link: LINK, rowNumber: 30, tabId: 7 });
  assertFields(r);
  const elapsed = clock - t0;
  assert.ok(elapsed >= 500_000, `phải thật sự chạy tới ~540s (episode không bị bỏ lỡ), thực tế ${Math.round(elapsed / 1000)}s`);
  assert.ok(elapsed <= 543_000, `phải chốt ở ~540s, thực tế ${Math.round(elapsed / 1000)}s`);
  assert.equal(r.ok, false);
  assert.equal(r.captcha, false);
});

test("stopped() TRƯỚC khi vẽ overlay (M5b): huỷ ngay sau kiểm proxy → không vẽ overlay, trả aborted", async () => {
  // proxyError (giả) chạy ngay trước đoạn vẽ overlay; cho nó HUỶ bước rồi trả false. Guard stopped() phải chặn
  // trước injectOverlayManager → không có lệnh "overlay" nào. Bỏ guard → overlay bị vẽ dù đã huỷ.
  reset({ store: () => null, proxyError: () => { globalThis.__launcherAbortStep(); return false; } });
  const r = await step({ link: LINK, rowNumber: 32, tabId: 7 });
  assertFields(r);
  assert.equal(r.aborted, true);
  assert.equal(count("overlay"), 0, "đã huỷ thì không được vẽ overlay");
  assert.equal(count("reload"), 0);
});

test("bump stats khi chrome.storage.local.set TREO (M6a) → có trần, bước vẫn trả ok", async () => {
  reset({ store: () => entry(), setHang: true });
  const r = await step({ link: LINK, rowNumber: 31, tabId: 7 });
  assert.equal(r.ok, true, "set treo không được làm treo bước");
  assert.equal(r.json, BODY);
});

test("Mục9 (writeState): storage.set TREO khi stopRun (chỉ đi qua writeState) → có trần, KHÔNG treo", async () => {
  // stopRun gọi writeState (và không set nào khác trước đó) → cô lập được trần của writeState.set.
  reset({ setHang: true });
  const r = await Promise.race([
    globalThis.__launcherStopRun().catch(() => "rejected"),
    new Promise((res) => realSetTimeout(() => res("HANG"), 3000)),
  ]);
  assert.notEqual(r, "HANG", "writeState.set không có trần → stopRun treo");
});

test("bước bị thay giữa lúc chờ captcha → lối ra mang superseded:true", async () => {
  reset({ captchaAt: () => true, store: () => null });
  const first = step({ link: LINK, rowNumber: 26, tabId: 7 });
  for (let i = 0; i < 40; i++) await new Promise((r) => setImmediate(r)); // bước 1 đang chờ captcha
  reset({ store: () => entry() }); // reset sc cho bước 2 có data (giữ storage)
  const second = step({ link: LINK, rowNumber: 27, tabId: 7 });
  const [a, b] = await Promise.all([first, second]);
  assertFields(a);
  assert.equal(a.superseded, true);
  assert.equal(a.aborted, true);
  assert.equal(b.ok, true);
});

// ── Mục 1: bước cũ KHÔNG được kéo tab về link cũ sau khi bị thay (khối thử tải lại) ────────────────
test("Mục1: bước cũ đang thử tải lại (trang chậm) bị thay → KHÔNG điều hướng tab về link cũ", async () => {
  let second = null, nWait = 0, bStart = 0;
  reset({
    store: (id, url) => (id === "555" && url === LINK2 && bStart && clock >= bStart + 45_000 ? { entry: { ...entry(BODY2).entry } } : null),
    // Trong khối thử tải lại của A (waitForTabComplete lần 2), chen bước B rồi để A đi tiếp các dòng tabsUpdate/open.
    urlAt: (url) => {
      if (!second && /waitForTabComplete/.test(new Error().stack) && ++nWait === 2) {
        tab.status = "complete";
        bStart = clock;
        second = step({ link: LINK2, rowNumber: 2, tabId: 7 });
      }
      return url;
    },
  });
  tab.status = "loading";
  const a = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assert.ok(second, "không kích được bước B");
  const b = await second;
  tab.status = "complete";
  assert.equal(a.superseded, true, a.message);
  const backToOld = calls.filter((c) => c.kind === "open" && c.at >= bStart && c.url === LINK);
  assert.equal(backToOld.length, 0, "bước cũ đã kéo tab về link cũ sau khi bước mới chạy");
  assert.equal(b.ok, true);
});

// ── Mục 2: superseded gom ở finish() — bị thay trong detectCaptcha/isProxyErrorPage không được rò cờ sai ──
test("Mục2: bị thay trong detectCaptcha (có lỗi get_pc) → KHÔNG trả trafficError, phải superseded", async () => {
  let second = null;
  reset({
    store: () => ({ error: { itemId: "987654321", error: "90309999" } }), // sẽ thành trafficError nếu không bị thay
    captchaAt: () => { if (!second) second = step({ link: LINK2, rowNumber: 2, tabId: 7 }); return false; },
  });
  const a = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assert.ok(second); await second;
  assert.equal(a.superseded, true, JSON.stringify(a));
  assert.equal(a.trafficError, false);
  assert.equal(a.blocked, false);
  assert.equal(a.ok, false);
});

test("Mục2: bị thay trong isProxyErrorPage → KHÔNG trả proxyError, phải superseded", async () => {
  let second = null;
  reset({
    store: () => null,
    proxyError: () => { if (!second) second = step({ link: LINK2, rowNumber: 2, tabId: 7 }); return true; },
  });
  const a = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assert.ok(second); await second;
  assert.equal(a.superseded, true, JSON.stringify(a));
  assert.equal(a.proxyError, false);
});

// ── Mục 3: 4 chốt từng gọi "equivalent" + cancelled() trong chờ captcha ──────────────────────────
test("Mục3 (M5a): huỷ Ở ĐÚNG đoạn trước reload → KHÔNG reload tab", async () => {
  // Huỷ chỉ khi detectCaptcha được gọi TỪ thân bước (đoạn dò captcha cuối lượt), KHÔNG từ waitForPdp/waitForCaptcha
  // — tức đã qua lượt chờ 1, đang ở ngay trước reload. Guard stopped() trước reload phải chặn. Bỏ guard → reload.
  let armed = false;
  reset({
    store: () => null,
    captchaAt: () => {
      const st = new Error().stack;
      if (!armed && since(30_000) && !/waitForPdp|waitForCaptchaToClear/.test(st)) { armed = true; globalThis.__launcherAbortStep(); }
      return false;
    },
  });
  const r = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assertFields(r);
  assert.ok(armed, "không kích được huỷ ở đoạn trước reload");
  assert.equal(r.aborted, true);
  assert.equal(count("reload"), 0, "đã huỷ trước reload thì không được reload");
});

test("Mục3 (M5d): /verify hiện ĐÚNG ở đoạn cuối (sau waitForPdp, trước notProduct) → dò captcha cuối lượt bắt được", async () => {
  // urlAt trả SP suốt waitForPdp (stack có waitForPdp/UrlStable); chỉ khi beforeReloadUrl (thân bước) mới ARM /verify.
  // Giải xong (sau 5s ảo) về SP, có data. Bỏ khối dò captcha cuối lượt → isShopeeProductUrl(/verify)=false → notProduct.
  let armClock = 0;
  reset({
    urlAt: (url) => {
      const st = new Error().stack;
      if (!armClock && since(30_000) && !/waitForPdp|waitForCaptchaToClear|waitForUrlStable/.test(st)) armClock = clock;
      return armClock && clock < armClock + 5_000 ? "https://shopee.vn/verify" : url;
    },
    store: () => (armClock && clock >= armClock + 6_000 ? entry() : null),
  });
  const r = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assertFields(r);
  assert.ok(armClock, "không ARM được /verify ở đoạn cuối");
  assert.equal(r.notProduct, false, r.message);
  assert.equal(r.ok, true, r.message);
  assert.equal(r.captcha, true);
});

test("Mục3 (M6b): chrome.tabs.get TREO 1 lần trong waitForTabComplete → bước vẫn kết thúc (dùng đồng hồ thật)", async () => {
  let n = 0;
  reset({ store: () => entry(), getHang: () => /waitForTabComplete/.test(new Error().stack) && ++n === 1 });
  tab.status = "loading"; // ép vào waitForTabComplete (không complete ngay)
  const r = await Promise.race([
    step({ link: LINK, rowNumber: 1, tabId: 7 }),
    new Promise((res) => realSetTimeout(() => res("HANG"), 3000)),
  ]);
  tab.status = "complete";
  assert.notEqual(r, "HANG", "bước treo vì tabs.get không có trần");
  assert.ok(n >= 1, "không kích được treo");
});

test("Mục3 (cancelled trong chờ captcha): bị thay khi đang chờ giải captcha → thoát SỚM (không kẹt 3')", async () => {
  // A kẹt captcha (captchaAt=true cho mọi SP không có data). B chen vào có data ngay (555) → không chạm captcha.
  // Có cancelled(): A thoát sau ~1 nhịp poll → ít lần dò captcha. Bỏ cancelled(): A poll suốt 3' (~60 lần).
  let second = null;
  reset({
    store: (id) => (id === "555" ? { entry: { ...entry(BODY2).entry } } : null),
    captchaAt: () => { if (!second) second = step({ link: LINK2, rowNumber: 2, tabId: 7 }); return true; },
  });
  const a = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  const b = await second;
  assert.equal(a.superseded, true, JSON.stringify(a));
  assert.equal(b.ok, true);
  assert.ok(count("captcha") < 20, `A phải thoát sớm nhờ cancelled(), nhưng dò captcha ${count("captcha")} lần`);
});

// ── Mục 7: mã lỗi cũ sau captcha ─────────────────────────────────────────────────────────────────
test("Mục7: giải xong captcha → xoá lỗi get_pc trong kho (clearError được gọi cho itemId)", async () => {
  let cleared = false;
  // store: trước giải captcha có lỗi; sau giải (clearError được gọi) trả entry tốt.
  reset({
    captchaAt: () => since(1_000) && !since(4_000),
    store: () => (cleared ? entry() : { error: { itemId: "987654321", error: "90309999" } }),
    onClearError: () => { cleared = true; },
  });
  const r = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assert.equal(cleared, true, "phải gọi clearError sau khi giải captcha");
  assert.equal(r.ok, true, r.message);
});

test("Mục7: có lỗi mà KHÔNG captcha → chờ ~3s đọc lại kho; trang vừa gọi lại get_pc thành công → ok", async () => {
  // Lần đọc đầu: lỗi, không captcha. Sau PDP_ERROR_RECHECK (~3s) store trả entry tốt → ok, không traffic.
  let errStart = 0;
  reset({
    captchaAt: () => false,
    store: () => {
      if (!errStart) { errStart = clock; return { error: { itemId: "987654321", error: "90309999" } }; }
      return clock >= errStart + 3_000 ? entry() : { error: { itemId: "987654321", error: "90309999" } };
    },
  });
  const r = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assert.equal(r.ok, true, r.message);
  assert.equal(r.trafficError, false);
  assert.equal(r.source, "hook");
});


// ══ Vòng 5 ════════════════════════════════════════════════════════════════════════════════════════
// Đếm theo VỊ TRÍ trong calls (không theo đồng hồ — nhiều sự kiện trùng mốc giờ ảo).
const after = (idx, kind) => calls.slice(idx).filter((c) => c.kind === kind);
const opensOfAfter = (url, idx) => after(idx, "open").filter((c) => c.url === url).length;
const B_DATA = (id) => (id === "555" ? { entry: { ...entry(BODY2).entry } } : null);

test("V5 Mục2: B chen vào ĐÚNG lúc bump('reload') đang chạy → A KHÔNG reload", async () => {
  let second = null;
  reset({
    store: B_DATA,
    onStorageGet: (ks) => { if (!second && ks.includes("harvestStats") && since(30_000)) second = step({ link: LINK2, rowNumber: 2, tabId: 7 }); },
  });
  const a = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assert.ok(second, "không kích được B trong lúc bump");
  const b = await second;
  assert.equal(a.superseded, true, JSON.stringify(a));
  assert.equal(count("reload"), 0, "A đã reload tab sau khi B chen vào");
  assert.equal(b.ok, true);
});

test("V5 Mục2 (dòng 599): bị thay đúng lúc getTabSafe trả null → A KHÔNG đi tìm/mở tab khác", async () => {
  // Gọi tabs.get #3 (trong waitForTabComplete) và #4 (getTabSafe ngay sau) báo tab mất; B chen ở #4.
  let second = null, armAt = 0;
  reset({
    store: B_DATA,
    // #3 (trong waitForTabComplete) và #4 (getTabSafe dòng 598) LUÔN báo mất — không phụ thuộc `second`, vì B được
    // kích ngay trong onGet(#4) nên lúc getGone(#4) chạy `second` đã có.
    getGone: (n) => n === 3 || n === 4,
    onGet: (n) => { if (!second && n === 4) { armAt = calls.length; second = step({ link: LINK2, rowNumber: 2, tabId: 7 }); } },
  });
  const a = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assert.ok(second, "không kích được");
  await second;
  assert.equal(a.superseded, true, JSON.stringify(a));
  assert.equal(after(armAt, "query").length, 0, "A đã đi tìm tab khác sau khi bị thay");
  assert.equal(opensOfAfter(LINK, armAt), 0);
});

test("V5 Mục2 (nhánh else): bị thay trong findReusableWorkTab → A KHÔNG mở link cũ", async () => {
  let second = null, armAt = 0;
  reset({
    store: B_DATA,
    getGone: (n) => !second && n >= 3,
    onQuery: () => { if (!second) { armAt = calls.length; second = step({ link: LINK2, rowNumber: 2, tabId: 7 }); } },
  });
  const a = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assert.ok(second, "không kích được");
  await second;
  assert.equal(a.superseded, true, JSON.stringify(a));
  assert.equal(opensOfAfter(LINK, armAt), 0, "A đã mở link cũ sau khi bị thay");
});

test("V5 Mục2 (dòng 609, catch tabsUpdate): bị thay khi tabsUpdate ném → A KHÔNG đi tìm tab khác", async () => {
  let second = null, armAt = 0;
  tab.status = "loading";
  reset({
    store: B_DATA,
    updateThrows: (n) => {
      if (n === 2 && !second) { armAt = calls.length; tab.status = "complete"; second = step({ link: LINK2, rowNumber: 2, tabId: 7 }); return true; }
      return false;
    },
  });
  const a = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  tab.status = "complete";
  assert.ok(second, "không kích được");
  await second;
  assert.equal(a.superseded, true, JSON.stringify(a));
  assert.equal(after(armAt, "query").length, 0, "A đã đi tìm tab khác sau khi bị thay");
});

test("V5 Mục2 (catch, sau findReusableWorkTab): bị thay trong lúc tìm tab → A KHÔNG mở link cũ", async () => {
  let second = null, armAt = 0;
  tab.status = "loading";
  reset({
    store: B_DATA,
    updateThrows: (n) => n === 2 && !second,
    onQuery: () => { if (!second && sc.updN >= 2) { armAt = calls.length; tab.status = "complete"; second = step({ link: LINK2, rowNumber: 2, tabId: 7 }); } },
  });
  const a = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  tab.status = "complete";
  assert.ok(second, "không kích được");
  await second;
  assert.equal(a.superseded, true, JSON.stringify(a));
  assert.equal(opensOfAfter(LINK, armAt), 0, "A đã mở link cũ sau khi bị thay");
});

test("V5 Mục3: huỷ ngay lúc dò captcha đầu tiên → KHÔNG vẽ overlay captcha / đổi tiêu đề tab", async () => {
  let armAt = null;
  reset({ store: () => null, captchaAt: () => { if (armAt === null) { armAt = calls.length; globalThis.__launcherAbortStep(); } return true; } });
  const r = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assert.equal(r.aborted, true);
  assert.equal(count("mark"), 0, "đã huỷ mà vẫn đổi tiêu đề tab CAPTCHA");
  assert.equal(after(armAt, "overlay").length, 0, "đã huỷ mà vẫn vẽ overlay captcha");
});

const ERR = { error: { itemId: "987654321", error: "90309999" } };

test("V5 Mục6: clearPdpError ở nhánh /verify ngay sau tải (dòng verify)", async () => {
  let cleared = false;
  reset({
    urlAt: (url) => (since(5_000) ? url : "https://shopee.vn/verify/captcha"),
    store: () => (cleared ? entry() : ERR),
    onClearError: () => { cleared = true; },
  });
  const r = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assert.equal(cleared, true, "không xoá lỗi sau khi giải /verify");
  assert.equal(r.ok, true, r.message);
});

test("V5 Mục6: clearPdpError ở nhánh captcha GIỮA lượt chờ", async () => {
  let cleared = false;
  reset({
    captchaAt: () => since(10_000) && !since(13_000),
    store: () => (cleared ? entry() : since(10_000) ? ERR : null),
    onClearError: () => { cleared = true; },
  });
  const r = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assert.equal(cleared, true, "không xoá lỗi sau khi giải captcha giữa lượt");
  assert.equal(r.ok, true, r.message);
  assert.equal(r.trafficError, false);
});

test("V5 Mục6: clearPdpError ở nhánh captcha CUỐI lượt", async () => {
  let cleared = false, armClock = 0;
  reset({
    urlAt: (url) => {
      const st = new Error().stack;
      if (!armClock && since(30_000) && !/waitForPdp|waitForCaptchaToClear|waitForUrlStable/.test(st)) armClock = clock;
      return armClock && clock < armClock + 5_000 ? "https://shopee.vn/verify" : url;
    },
    store: () => (cleared ? entry() : armClock ? ERR : null),
    onClearError: () => { cleared = true; },
  });
  const r = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assert.ok(armClock, "không ARM được /verify cuối lượt");
  assert.equal(cleared, true, "không xoá lỗi sau khi giải captcha cuối lượt");
  assert.equal(r.ok, true, r.message);
});

test("V5 Mục6: huỷ ngay trước giấc ngủ 3s đọc lại lỗi → sau giấc ngủ KHÔNG đọc kho nữa", async () => {
  let armAt = null;
  reset({
    store: () => ERR,
    // detectCaptcha trong nhánh lỗi (ngay trước sleep 3s): huỷ rồi trả false.
    captchaAt: () => { if (armAt === null) { armAt = calls.length; globalThis.__launcherAbortStep(); } return false; },
  });
  const r = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assert.equal(r.aborted, true);
  assert.equal(after(armAt, "readStore").length, 0, "sau giấc ngủ vẫn đọc kho dù đã huỷ");
});

test("V5 Mục6: finish() phủ cả ca HUỶ (không chỉ bị thay): huỷ trong isProxyErrorPage → aborted, không proxyError", async () => {
  reset({ store: () => null, proxyError: () => { globalThis.__launcherAbortStep(); return true; } });
  const r = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assertFields(r);
  assert.equal(r.aborted, true, JSON.stringify(r));
  assert.equal(r.proxyError, false);
  assert.equal(r.superseded, false);
});

test("V5 Mục6: bump KHÔNG ghi thống kê khi đã huỷ (nhánh notProduct cuối lượt)", async () => {
  let armed = false;
  reset({
    store: () => null,
    urlAt: (url) => {
      const st = new Error().stack;
      if (since(20_000) && !armed && since(30_000) && !/waitForPdp|waitForCaptchaToClear|waitForUrlStable/.test(st)) {
        armed = true; globalThis.__launcherAbortStep();
      }
      return since(20_000) ? "https://shopee.vn/" : url;
    },
  });
  const r = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assert.ok(armed, "không kích được");
  assert.equal(r.aborted, true);
  assert.equal(storage.harvestStats?.miss ?? 0, 0, "đã huỷ mà vẫn ghi thống kê miss");
});

test("V5 Mục4: link dạng /i.<shop>.<item> (không có tên SP) KHÔNG bị coi là notProduct", async () => {
  const L = "https://shopee.vn/i.123456.987654321";
  reset({ store: () => (since(5_000) ? entry() : null) }); // chưa có dữ liệu ngay → phải đi qua kiểm notProduct
  const r = await step({ link: L, rowNumber: 1, tabId: 7 });
  assert.equal(r.notProduct, false, r.message);
  assert.equal(r.ok, true, r.message);
});

test("V5 Mục4 (neo đuôi isShopeeProductUrl): giữa lượt chờ tab rời sang /Wifi-i.2.4G → notProduct, không reload", async () => {
  // "/Wifi-i.2.4G" có "-i.2.4" nhưng theo sau là "G" → KHÔNG phải trang SP. Bỏ neo đuôi (?=$|\/) thì bị coi là SP
  // → đi reload thay vì notProduct.
  reset({ urlAt: (url) => (since(20_000) ? "https://shopee.vn/Wifi-i.2.4G" : url) });
  const r = await step({ link: LINK, rowNumber: 1, tabId: 7 });
  assertFields(r);
  assert.equal(r.notProduct, true, r.message);
  assert.equal(count("reload"), 0);
});
