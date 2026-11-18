// GĐ7: hộp nổi — panel-core.js (thuần: nhận diện trang, nội dung hiển thị) + panel.js chạy NGUYÊN trong vm với DOM giả
// tối thiểu (createElement / appendChild / attachShadow / getBoundingClientRect…) và chrome.runtime giả.
// Chạy: node --test extensions/shopee-harvest/tests/*.test.mjs (từ workspace/).
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import { fileURLToPath } from "node:url";
import { extractProductIds } from "../pdp-core.js";

const read = (f) => fs.readFileSync(fileURLToPath(new URL(`../${f}`, import.meta.url)), "utf8");
const CORE_SRC = read("panel-core.js");
const PANEL_SRC = read("panel.js");

const loadCore = () => {
  const ctx = vm.createContext({ URL, Date });
  vm.runInContext(CORE_SRC, ctx);
  return ctx.__shopeeHarvestPanelCore;
};
const core = loadCore();

const PRODUCT = "https://shopee.vn/Ao-thun-nu-i.123456.987654321?sp_atk=x";

// ── panel-core ────────────────────────────────────────────────────────────────────────────────────────────────────

test("productIds: trùng pdp-core.extractProductIds trên mọi dạng link TRANG SP; trang khác → null", () => {
  const product = [
    PRODUCT,
    "https://shopee.vn/product/123456/987654321",
    "https://shopee.vn/product/123456/987654321?x=1#y",
    "https://shopee.vn/a-b-i.1.2/",
    "https://shopee.vn/x?itemid=5&shopid=6",
    "https://shopee.vn/Wifi-i.2.4G-Router-i.123.456",
  ];
  for (const u of product) {
    assert.deepEqual({ ...core.productIds(u) }, extractProductIds(u), u);
  }
  for (const u of [
    "https://shopee.vn/", "https://shopee.vn/buyer/login?next=x", "https://shopee.vn/verify/traffic",
    "https://shopee.vn/search?keyword=ao-i.5.6", "https://google.com/x-i.1.2", "không phải url",
  ]) {
    assert.equal(core.productIds(u), null, u);
  }
});

const NOW = Date.UTC(2026, 10, 18, 2, 30);
const base = { href: PRODUCT, now: NOW, hubConfigured: true, known: { status: "no" }, queue: { pending: 0, error: 0 }, busy: null, last: null };
// Đối tượng sinh trong vm khác realm (deepStrictEqual so cả prototype) → chép sang realm của test.
const plain = (x) => JSON.parse(JSON.stringify(x));
const texts = (v) => plain(v.rows.map((r) => r.text));

test("view: chưa cấu hình Hub → nhắc mở cài đặt, nút Mở cài đặt", () => {
  const v = core.view({ ...base, hubConfigured: false });
  assert.equal(v.visible, true);
  assert.match(texts(v).join("\n"), /Chưa cấu hình Hub/);
  assert.deepEqual({ ...v.button }, { text: "Mở cài đặt", enabled: true, action: "options" });
});

test("view: đã có trên Hub + giờ lấy; chưa có; không hỏi được; đang hỏi", () => {
  const at = new Date(2026, 10, 17, 9, 5).getTime();
  assert.match(texts(core.view({ ...base, known: { status: "yes", at } }))[0], /^Đã có trên Hub · lấy lúc 17\/11 09:05$/);
  assert.deepEqual(texts(core.view(base)), ["Chưa có trên Hub"]);
  assert.match(texts(core.view({ ...base, known: { status: "error", reason: "HTTP 503" } }))[0], /Không hỏi được Hub \(HTTP 503\)/);
  assert.match(texts(core.view({ ...base, known: { status: "loading" } }))[0], /Đang hỏi Hub/);
  const v = core.view(base);
  assert.deepEqual({ ...v.button }, { text: "Lấy dữ liệu", enabled: true, action: "harvest" });
  assert.equal(v.itemId, "987654321");
});

test("view: số chờ gửi / lỗi của hàng đợi; đang bận thì khoá nút", () => {
  assert.ok(texts(core.view({ ...base, queue: { pending: 3, error: 1 } })).includes("Hàng đợi: 3 chờ gửi · 1 lỗi"));
  const busy = core.view({ ...base, busy: "push" });
  assert.deepEqual({ ...busy.button }, { text: "Đang gửi Hub…", enabled: false, action: null });
  assert.equal(core.view({ ...base, busy: "capture" }).button.text, "Đang lấy dữ liệu…");
});

test("view: kết quả gần nhất — đã lưu / hàng đợi / từ chối kèm lý do; kết quả của SP khác không hiện", () => {
  const last = (o) => ({ at: NOW, itemId: "987654321", ...o });
  assert.ok(texts(core.view({ ...base, last: last({ status: "saved", applied: true }) })).includes("✓ Đã lưu Hub"));
  assert.ok(texts(core.view({ ...base, last: last({ status: "saved", applied: false }) })).includes("✓ Đã lưu Hub (dữ liệu không đổi)"));
  assert.match(texts(core.view({ ...base, last: last({ status: "queued", reason: "HTTP 503" }) })).at(-1), /hàng đợi.*HTTP 503/);
  assert.match(texts(core.view({ ...base, last: last({ status: "rejected", reason: "raw-error" }) })).at(-1), /Hub từ chối dữ liệu: raw-error/);
  assert.deepEqual(texts(core.view({ ...base, last: last({ status: "saved", itemId: "1" }) })), ["Chưa có trên Hub"]);
});

test("view: bị đẩy khỏi trang SP (đăng nhập / xác minh) → hộp vẫn hiện ĐÚNG loại lỗi trong 2 phút, quá thì ẩn; trang thường thì ẩn", () => {
  const login = { ...base, href: "https://shopee.vn/buyer/login?next=x" };
  assert.equal(core.view(login).visible, false);
  const v = core.view({ ...login, last: { status: "loginRequired", at: NOW - 60_000, itemId: "987654321" } });
  assert.equal(v.visible, true);
  assert.match(texts(v).join("\n"), /mất đăng nhập/);
  assert.equal(v.button.enabled, false);
  const verify = core.view({ ...base, href: "https://shopee.vn/verify/captcha", last: { status: "captcha", at: NOW - 1000 } });
  assert.match(texts(verify).join("\n"), /captcha/);
  assert.equal(core.view({ ...login, last: { status: "loginRequired", at: NOW - core.OFF_PAGE_RESULT_MS - 1 } }).visible, false);
  assert.equal(core.view({ ...login, last: { status: "saved", at: NOW } }).visible, false);
  const np = core.view({ ...base, href: "https://shopee.vn/", last: { status: "notProduct", at: NOW } });
  assert.match(texts(np).join("\n"), /Không phải trang sản phẩm/);
});

test("view: chế độ tự động hiện sheet + dòng, không hỏi Hub", () => {
  const v = core.view({ ...base, auto: { sheet: "Shop A", rowNo: 12 }, known: { status: "off" } });
  assert.deepEqual(texts(v), ["Tự động (app Harvest) · sheet Shop A · dòng 12"]);
});

// ── panel.js với DOM giả ─────────────────────────────────────────────────────────────────────────────────────────

class El {
  constructor(tag) {
    this.tagName = tag;
    this.children = [];
    this.style = { cssText: "", display: "" };
    this.listeners = {};
    this.textContent = "";
    this.disabled = false;
    this.className = "";
    this.isConnected = false;
    this.rect = { left: 1000, top: 740, width: 276, height: 31 };
    this.scrolled = 0;
  }
  appendChild(c) { this.children.push(c); c.isConnected = true; return c; }
  removeChild(c) { this.children = this.children.filter((x) => x !== c); c.isConnected = false; return c; }
  get firstChild() { return this.children[0] || null; }
  attachShadow(opts) { this.shadowMode = opts.mode; this.shadow = new El("#shadow-root"); return this.shadow; }
  addEventListener(type, fn) { (this.listeners[type] ||= []).push(fn); }
  getBoundingClientRect() { return this.rect; }
  scrollIntoView() { this.scrolled++; }
}

const ticks = async (n = 5) => { for (let i = 0; i < n; i++) await new Promise((r) => setImmediate(r)); };

const makePanel = ({ href = PRODUCT, status = { hubConfigured: true, known: { status: "no" }, queue: { pending: 0, error: 0 } } } = {}) => {
  const sent = [];
  const listeners = [];
  const intervals = [];
  const body = new El("body");
  const env = {
    status,
    clickReply: { accepted: true },
    cover: null,
    points: [],
  };
  const ctx = {
    URL, Date, setImmediate,
    location: { href },
    window: { innerWidth: 1280, innerHeight: 800, devicePixelRatio: 1.25, visualViewport: { scale: 1 } },
    document: {
      createElement: (t) => new El(t), body, documentElement: new El("html"),
      // Tâm nút: mặc định trúng host của hộp; env.cover = phần tử trang đè lên.
      elementFromPoint: (x, y) => { env.points.push([x, y]); return env.cover ?? body.children[0] ?? null; },
    },
    setInterval: (fn) => { intervals.push(fn); return intervals.length; },
    chrome: {
      runtime: {
        lastError: undefined,
        sendMessage: (msg, cb) => {
          sent.push(msg);
          const reply = msg.op === "status" ? env.status : msg.op === "click" ? env.clickReply : { ok: true };
          setImmediate(() => cb(structuredClone(reply)));
        },
        onMessage: { addListener: (fn) => listeners.push(fn) },
      },
    },
  };
  vm.createContext(ctx);
  vm.runInContext(CORE_SRC, ctx);
  vm.runInContext(PANEL_SRC, ctx);
  const host = () => body.children[0] || null;
  const box = () => host().shadow.children[1];
  const button = () => box().children[2];
  const rows = () => box().children[1].children.map((c) => c.textContent);
  const dispatch = (message) => {
    let response;
    for (const fn of listeners) fn(message, {}, (r) => { response = r; });
    return response;
  };
  const click = (isTrusted) => { for (const fn of button().listeners.click) fn({ isTrusted }); };
  return { ctx, env, sent, host, button, rows, dispatch, click, tickInterval: () => intervals.forEach((f) => f()) };
};

test("panel.js: trang SP → hỏi trạng thái, vẽ hộp trong shadow ĐÓNG, góc dưới phải, z-index cao nhất", async () => {
  const p = makePanel({ status: { hubConfigured: true, known: { status: "yes", at: NOW }, queue: { pending: 2, error: 1 } } });
  await ticks();
  assert.deepEqual(plain(p.sent[0]), { type: "HARVEST_PANEL", op: "status", href: PRODUCT });
  const host = p.host();
  assert.ok(host, "chưa vẽ hộp");
  assert.equal(host.shadowMode, "closed");
  assert.match(host.style.cssText, /position: fixed; right: 16px; bottom: 16px; z-index: 2147483647/);
  assert.equal(host.style.display, "block");
  assert.match(p.rows()[0], /^Đã có trên Hub · lấy lúc /);
  assert.ok(p.rows().includes("Hàng đợi: 2 chờ gửi · 1 lỗi"));
  assert.equal(p.button().textContent, "Lấy dữ liệu");
  assert.equal(p.button().disabled, false);
});

test("panel.js: cú bấm KHÔNG isTrusted (script trang) bị bỏ qua; bấm thật → báo background + khoá nút, kết quả về thì hiện", async () => {
  const p = makePanel();
  await ticks();
  p.click(false);
  await ticks();
  assert.equal(p.sent.filter((m) => m.op === "click").length, 0);

  p.click(true);
  await ticks();
  const clicks = p.sent.filter((m) => m.op === "click");
  assert.equal(clicks.length, 1);
  assert.deepEqual(plain(clicks[0]), { type: "HARVEST_PANEL", op: "click", href: PRODUCT, itemId: "987654321" });
  assert.equal(p.button().textContent, "Đang lấy dữ liệu…");
  assert.equal(p.button().disabled, true);

  p.dispatch({ type: "HARVEST_PANEL_UPDATE", state: { busy: null, last: { status: "saved", applied: true, at: Date.now(), itemId: "987654321" } } });
  assert.ok(p.rows().includes("✓ Đã lưu Hub"));
  assert.equal(p.button().disabled, false);
});

test("panel.js: background từ chối cú bấm (đang bận) → mở khoá + báo lỗi", async () => {
  const p = makePanel();
  p.env.clickReply = { accepted: false, reason: "Đang xử lý lần bấm trước." };
  await ticks();
  p.click(true);
  await ticks();
  assert.equal(p.button().disabled, false);
  assert.match(p.rows().at(-1), /Đang xử lý lần bấm trước/);
});

test("panel.js: PING → cuộn nút vào tầm nhìn, trả toạ độ nút + viewport; chưa sẵn sàng thì ready=false", async () => {
  const p = makePanel();
  await ticks();
  const r = p.dispatch({ type: "HARVEST_PANEL_PING", state: { hubConfigured: true, auto: { sheet: "S", rowNo: 3 } } });
  assert.equal(r.ready, true);
  assert.equal(r.itemId, "987654321");
  assert.deepEqual({ ...r.rect }, { x: 1000, y: 740, width: 276, height: 31 });
  assert.deepEqual({ ...r.viewport }, { width: 1280, height: 800 });
  assert.equal(r.pageScale, 1);
  assert.equal(r.devicePixelRatio, 1.25);
  assert.deepEqual(p.env.points.at(-1), [1000 + 276 / 2, 740 + 31 / 2]); // kiểm đúng TÂM nút
  assert.equal(p.button().scrolled, 1);
  assert.ok(p.rows()[0].startsWith("Tự động (app Harvest)"));

  p.dispatch({ type: "HARVEST_PANEL_UPDATE", state: { busy: "capture" } });
  assert.equal(p.dispatch({ type: "HARVEST_PANEL_PING" }).ready, false);
  p.dispatch({ type: "HARVEST_PANEL_UPDATE", state: { busy: null } });
  p.button().rect = { left: 0, top: 0, width: 0, height: 0 };
  assert.equal(p.dispatch({ type: "HARVEST_PANEL_PING" }).reason, "hidden");
});

test("panel.js: nút bị phần tử trang che (tâm nút không phải hộp) → PING báo covered, không sẵn sàng", async () => {
  const p = makePanel();
  await ticks();
  p.env.cover = Object.assign(new El("DIV"), { id: "modal" });
  const r = p.dispatch({ type: "HARVEST_PANEL_PING", state: { hubConfigured: true } });
  assert.equal(r.ready, false);
  assert.match(r.reason, /^covered: .*div#modal/);
  p.env.cover = null;
  assert.equal(p.dispatch({ type: "HARVEST_PANEL_PING" }).ready, true);
});

test("panel.js: chưa cấu hình Hub → nút Mở cài đặt mở trang Cài đặt, PING không sẵn sàng", async () => {
  const p = makePanel({ status: { hubConfigured: false, queue: {} } });
  await ticks();
  assert.equal(p.button().textContent, "Mở cài đặt");
  p.click(true);
  await ticks();
  assert.ok(p.sent.some((m) => m.op === "openOptions"));
  assert.equal(p.sent.some((m) => m.op === "click"), false);
  assert.equal(p.dispatch({ type: "HARVEST_PANEL_PING" }).ready, false);
});

test("panel.js: trang không phải SP → không vẽ hộp; SPA chuyển sang trang SP → hỏi lại trạng thái và hiện", async () => {
  const p = makePanel({ href: "https://shopee.vn/" });
  await ticks();
  assert.equal(p.host(), null);
  assert.equal(p.dispatch({ type: "HARVEST_PANEL_PING" }).reason, "not-product");
  p.ctx.location.href = PRODUCT;
  p.tickInterval();
  await ticks();
  assert.equal(p.sent.filter((m) => m.op === "status").at(-1).href, PRODUCT);
  assert.equal(p.host().style.display, "block");
});
