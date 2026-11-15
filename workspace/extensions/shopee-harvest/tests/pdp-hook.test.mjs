// Test pdp-hook.js (world MAIN) + pdp-store.js (world ISOLATED): chạy NGUYÊN hai file trong hai vm context dùng
// chung một `document` (EventTarget), giống hai world của Chrome chỉ chia sẻ DOM. Trang giả có fetch/XHR định nghĩa
// TRONG context (để so toString/length/prototype với hàm gốc đúng realm).
// Chạy: node --test extensions/shopee-harvest/tests/*.test.mjs (từ workspace/).
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import { fileURLToPath } from "node:url";
import { checkPdpBody } from "../pdp-core.js";

const read = (f) => fs.readFileSync(fileURLToPath(new URL(`../${f}`, import.meta.url)), "utf8");
const HOOK_SRC = read("pdp-hook.js");
const STORE_SRC = read("pdp-store.js");
const PAGE = "https://shopee.vn/Ao-i.123456.987654321";
const GET_PC = "/api/v4/pdp/get_pc?item_id=987654321&shop_id=123456&tz_offset_in_minutes=420&detail_level=0";

const body = (itemId, extra = {}) => JSON.stringify({ error: null, data: { item: { item_id: itemId, shop_id: 1 } }, ...extra });
const ticks = async (n = 6) => { for (let i = 0; i < n; i++) await new Promise((r) => setImmediate(r)); };

// url/responseURL/responseType/responseText/response để trên PROTOTYPE dạng getter — hook chụp descriptor getter
// từ prototype (như trình duyệt thật), nên bản giả cũng phải là getter prototype chứ không phải own property.
class FakeResponse {
  constructor(text, url = "") { this._text = text; this._url = url; this.clones = 0; this.cloneThrows = false; }
  get url() { return this._url; }
  clone() {
    if (this.cloneThrows) throw new TypeError("clone boom");
    this.clones++;
    return new FakeResponse(this._text, this._url);
  }
  text() { return Promise.resolve(this._text); }
}

class FakeRequest {
  constructor(url) { this._url = url; }
  get url() { return this._url; }
}

// Trang giả: fetch là METHOD (không prototype, không `new` được, length 1 — như fetch thật), XHR là class.
const PAGE_SETUP = `
  globalThis.window = globalThis;
  globalThis.fetch = ({ fetch(input) { return __hostFetch.apply(this, arguments); } }).fetch;
  // fireLoad dùng dispatchEvent GỐC chụp lúc dựng trang (mô phỏng trình duyệt tự bắn load, không qua prototype).
  const nativeDispatch = EventTarget.prototype.dispatchEvent;
  globalThis.XMLHttpRequest = class XMLHttpRequest extends EventTarget {
    constructor() { super(); this._rt = ""; this._rurl = ""; this._text = ""; this._resp = null; }
    open(method, url) { this.openArgs = [...arguments]; return "open-ret"; }
    send(body) { this.sendArgs = [...arguments]; if (this.sendThrows) throw new Error("send boom"); return "send-ret"; }
    get responseURL() { return this._rurl; }
    get responseType() { return this._rt; }
    set responseType(v) { this._rt = v; }
    get responseText() {
      if (this._rt !== "" && this._rt !== "text") throw new Error("InvalidStateError");
      return this._text;
    }
    get response() { return this._rt === "json" ? this._resp : this._text; }
    fireLoad() { nativeDispatch.call(this, new Event("load")); }
  };
  globalThis.__describe = (f) => {
    let ctorError = null;
    try { new f(); } catch (e) { ctorError = e.constructor.name; }
    return {
      typeof: typeof f, name: f.name, length: f.length, str: String(f),
      fnToString: Function.prototype.toString.call(f), hasPrototype: "prototype" in f,
      own: Object.getOwnPropertyNames(f).sort().join(","), ctorError,
    };
  };
`;

/**
 * Dựng trang (MAIN) + world ISOLATED. order: "store-first" | "hook-first" | "hook-only" (gọi runStore() sau).
 */
const makeWorlds = ({ fetchImpl = () => Promise.resolve(new FakeResponse("x")), href = PAGE, order = "store-first" } = {}) => {
  const doc = new EventTarget();
  const main = { URL, console, setTimeout, setImmediate, EventTarget, Event, CustomEvent, Response: FakeResponse, Request: FakeRequest, document: doc, location: { href }, __hostFetch: fetchImpl };
  vm.createContext(main);
  vm.runInContext(PAGE_SETUP, main);
  const iso = { console, EventTarget, Event, CustomEvent, document: doc };
  vm.createContext(iso);

  const runStore = () => vm.runInContext(STORE_SRC, iso, { filename: "pdp-store.js" });
  const runHook = () => vm.runInContext(HOOK_SRC, main, { filename: "pdp-hook.js" });
  if (order === "store-first") { runStore(); runHook(); }
  else { runHook(); if (order === "hook-first") runStore(); }

  const store = () => vm.runInContext("globalThis.__shopeeHarvestStore", iso);
  // list() đổi về object của realm test (deepStrictEqual so cả prototype — mảng của context khác thì lệch).
  return { main, iso, doc, store, runStore, list: () => JSON.parse(JSON.stringify(store().list())) };
};

test("fetch get_pc hợp lệ → kho ISOLATED có chuỗi gốc theo itemId; trang nhận ĐÚNG promise gốc", async () => {
  const text = body(987654321);
  const resp = new FakeResponse(text, `https://shopee.vn${GET_PC}`);
  const original = Promise.resolve(resp);
  const w = makeWorlds({ fetchImpl: () => original });

  const got = w.main.fetch(GET_PC, { credentials: "include" });
  assert.equal(got, original);
  assert.equal(await got, resp);
  await ticks();

  const { entry, error } = w.store().get("987654321");
  assert.ok(entry, "phải có mục 987654321");
  assert.equal(entry.body, text); // chuỗi gốc, không parse lại
  assert.equal(entry.via, "fetch");
  assert.equal(entry.url, `https://shopee.vn${GET_PC}`);
  assert.equal(typeof entry.at, "number");
  assert.equal(error, null);
  assert.equal(resp.clones, 1); // chỉ đọc bản clone
  const st = w.list().status;
  assert.equal(st.hookReady, true);
  assert.equal(st.seen, 1);
  assert.equal(st.dropped, 0);
});

test("fetch truyền đúng this/đối số cho hàm gốc; lỗi đồng bộ của hàm gốc đi ra nguyên vẹn", () => {
  let seen = null;
  const w = makeWorlds({
    fetchImpl: function (input) { seen = [...arguments]; if (input === "boom") throw new TypeError("sync"); return Promise.resolve(new FakeResponse("x")); },
  });
  const init = { method: "GET" };
  w.main.fetch("/a", init, "extra");
  assert.deepEqual(seen, ["/a", init, "extra"]);
  assert.throws(() => w.main.fetch("boom"), (err) => err instanceof TypeError && err.message === "sync");
});

test("fetch URL khác get_pc → không clone, không gửi gì", async () => {
  const resp = new FakeResponse(body(1));
  const w = makeWorlds({ fetchImpl: () => Promise.resolve(resp) });
  await w.main.fetch("/api/v4/pdp/get_rw?item_id=1");
  await w.main.fetch("/api/v4/pdp/get_pc_extra?item_id=1");
  await ticks();
  assert.equal(resp.clones, 0);
  assert.deepEqual(w.list().entries, []);
  assert.equal(w.list().status.seen, 0);
});

test("fetch: Request object và URL tuyệt đối/tương đối đều nhận", async () => {
  const idOf = (input) => Number(new URL(typeof input === "string" ? input : input.url || String(input), PAGE).searchParams.get("item_id"));
  const w = makeWorlds({ fetchImpl: (input) => Promise.resolve(new FakeResponse(body(idOf(input)))) });
  await w.main.fetch(new FakeRequest("https://shopee.vn/api/v4/pdp/get_pc?item_id=11")); // Request → .url qua getter đã chụp
  await w.main.fetch("https://shopee.vn/api/v4/pdp/get_pc/?item_id=12");
  await w.main.fetch(new URL("/api/v4/pdp/get_pc?item_id=13", PAGE));
  await ticks();
  assert.deepEqual(w.list().entries.map((e) => e.itemId).sort(), ["11", "12", "13"]);
});

test("body không phải JSON / JSON thiếu item_id → bỏ (đếm dropped), trang vẫn đọc được response", async () => {
  for (const text of ["<html>captcha</html>", JSON.stringify({ error: null, data: {} })]) {
    const resp = new FakeResponse(text);
    const w = makeWorlds({ fetchImpl: () => Promise.resolve(resp) });
    const r = await w.main.fetch("/api/v4/pdp/get_pc?shop_id=1");
    assert.equal(await r.text(), text);
    await ticks();
    const l = w.list();
    assert.deepEqual(l.entries, [], text);
    assert.deepEqual(l.errors, [], text);
    assert.equal(l.status.seen, 1);
    assert.equal(l.status.dropped, 1);
  }
});

test("body có `error` (đủ item_id) → KHÔNG vào kho dữ liệu nhưng có ghi lỗi; vắng item_id thì lấy từ URL", async () => {
  const errBody = JSON.stringify({ error: 90309999, error_msg: "blocked", data: { item: { item_id: 987654321 } } });
  const w = makeWorlds({ fetchImpl: () => Promise.resolve(new FakeResponse(errBody)) });
  await w.main.fetch(GET_PC);
  await ticks();
  const got = w.store().get("987654321");
  assert.equal(got.entry, null);
  assert.equal(got.error.error, "90309999");

  const w2 = makeWorlds({ fetchImpl: () => Promise.resolve(new FakeResponse(JSON.stringify({ error: 4, data: null }))) });
  await w2.main.fetch("/api/v4/pdp/get_pc?item_id=555&shop_id=1");
  await ticks();
  assert.equal(w2.store().get("555").error.error, "4");
  assert.equal(w2.store().get("555").entry, null);
});

test("fetch lỗi mạng → trang nhận đúng lỗi đó, hook không sinh unhandled rejection", async () => {
  const unhandled = [];
  const onUnhandled = (reason) => unhandled.push(reason);
  process.on("unhandledRejection", onUnhandled);
  try {
    const netErr = new TypeError("Failed to fetch");
    const w = makeWorlds({ fetchImpl: () => Promise.reject(netErr) });
    await assert.rejects(w.main.fetch(GET_PC), (err) => err === netErr);
    await ticks(10);
    assert.deepEqual(unhandled, []);
    assert.deepEqual(w.list().entries, []);
  } finally {
    process.off("unhandledRejection", onUnhandled);
  }
});

test("hook ném bên trong (clone lỗi, location hỏng) → trang không bị ảnh hưởng", async () => {
  const resp = new FakeResponse(body(5));
  resp.cloneThrows = true;
  const w = makeWorlds({ fetchImpl: () => Promise.resolve(resp) });
  assert.equal(await w.main.fetch(GET_PC), resp);
  await ticks();
  assert.deepEqual(w.list().entries, []);

  const w2 = makeWorlds({ fetchImpl: () => Promise.resolve(new FakeResponse(body(6))) });
  vm.runInContext(`Object.defineProperty(globalThis, "location", { get() { throw new Error("location boom"); } })`, w2.main);
  const r = await w2.main.fetch(GET_PC);
  assert.ok(r instanceof FakeResponse);
});

test("kho giữ 20 mục mới nhất; bắt lại cùng SP thì ghi đè và thành mới nhất", async () => {
  const w = makeWorlds({
    fetchImpl: (input) => Promise.resolve(new FakeResponse(body(Number(new URL(input, PAGE).searchParams.get("item_id"))))),
  });
  for (let i = 1; i <= 25; i++) {
    await w.main.fetch(`/api/v4/pdp/get_pc?item_id=${i}`);
    await ticks();
  }
  const keys = w.list().entries.map((e) => Number(e.itemId)).sort((a, b) => a - b);
  assert.deepEqual(keys, Array.from({ length: 20 }, (_, k) => k + 6)); // 1..5 bị bỏ (cũ nhất)

  await w.main.fetch("/api/v4/pdp/get_pc?item_id=6"); // 6 đang cũ nhất → bắt lại thành mới nhất
  await ticks();
  await w.main.fetch("/api/v4/pdp/get_pc?item_id=99");
  await ticks();
  const after = w.list().entries.map((e) => Number(e.itemId));
  assert.equal(after.length, 20);
  assert.ok(after.includes(6), "6 vừa bắt lại phải còn");
  assert.ok(!after.includes(7), "7 thành cũ nhất phải bị bỏ");
});

test("XHR get_pc (responseType rỗng) → kho có responseText; open/send trả nguyên giá trị và đối số", () => {
  const w = makeWorlds();
  const xhr = new w.main.XMLHttpRequest();
  assert.equal(xhr.open("GET", GET_PC, true), "open-ret");
  assert.deepEqual([...xhr.openArgs], ["GET", GET_PC, true]);
  assert.equal(xhr.send(null), "send-ret");
  assert.deepEqual([...xhr.sendArgs], [null]);
  xhr._text = body(987654321);
  xhr.fireLoad();
  const { entry } = w.store().get("987654321");
  assert.equal(entry.body, xhr._text);
  assert.equal(entry.via, "xhr");
  assert.equal(entry.url, GET_PC);
});

test("XHR responseType json → lưu bản stringify (via xhr-json); arraybuffer → bỏ qua, không ném", () => {
  const w = makeWorlds();
  const a = new w.main.XMLHttpRequest();
  a.open("GET", GET_PC);
  a.send();
  a.responseType = "json";
  a._resp = { error: null, data: { item: { item_id: 31 } } };
  a.fireLoad();
  assert.equal(w.store().get("31").entry.via, "xhr-json");

  const b = new w.main.XMLHttpRequest();
  b.open("GET", GET_PC);
  b.send();
  b.responseType = "arraybuffer";
  assert.doesNotThrow(() => b.fireLoad());
  assert.deepEqual(w.list().entries.map((e) => e.itemId), ["31"]);
});

test("XHR URL khác get_pc → không bắt; lỗi của send gốc đi ra nguyên vẹn; body không phải JSON → không ném", () => {
  const w = makeWorlds();
  const x = new w.main.XMLHttpRequest();
  x.open("GET", "/api/v4/item/get_ratings");
  x.send();
  x._text = body(1);
  x.fireLoad();
  assert.equal(w.list().status.seen, 0);

  const y = new w.main.XMLHttpRequest();
  y.sendThrows = true;
  y.open("GET", GET_PC);
  assert.throws(() => y.send(), /send boom/);

  const z = new w.main.XMLHttpRequest();
  z.open("GET", GET_PC);
  z.send();
  z._text = "<!doctype html>";
  assert.doesNotThrow(() => z.fireLoad());
  assert.deepEqual(w.list().entries, []);
});

test("hook bắn get_pc TRƯỚC khi script ISOLATED gắn listener → đệm và phát lại khi store sẵn sàng", async () => {
  const idOf = (input) => new URL(input, PAGE).searchParams.get("item_id");
  const w = makeWorlds({
    order: "hook-only",
    fetchImpl: (input) => Promise.resolve(new FakeResponse(
      idOf(input) === "9" ? JSON.stringify({ error: 77, data: null }) : body(Number(idOf(input))))),
  });
  await w.main.fetch("/api/v4/pdp/get_pc?item_id=41");
  await w.main.fetch("/api/v4/pdp/get_pc?item_id=9");
  await ticks();
  w.runStore(); // gắn SAU — chỉ nhận được nhờ phát lại
  assert.equal(w.store().get("41").entry.body, body(41));
  assert.equal(w.store().get("9").error.error, "77");
  const st = w.list().status;
  assert.equal(st.hookReady, true);
  assert.equal(st.seen, 2);
});

test("store gắn trước hook → nhận tin ready lúc hook gắn (hook đã gắn, chưa thấy get_pc nào)", () => {
  const w = makeWorlds({ order: "store-first" });
  const st = w.list().status;
  assert.equal(st.hookReady, true);
  assert.equal(st.seen, 0);
  // Chỉ có store, không có hook → hookReady false (popup báo "CHƯA gắn").
  const doc = new EventTarget();
  const iso = { console, EventTarget, Event, CustomEvent, document: doc };
  vm.createContext(iso);
  vm.runInContext(STORE_SRC, iso);
  assert.equal(vm.runInContext("__shopeeHarvestStore.list().status.hookReady", iso), false);
});

test("trang ghi đè EventTarget.prototype.addEventListener/dispatchEvent SAU khi hook gắn → hook vẫn chạy", () => {
  const w = makeWorlds();
  const proto = EventTarget.prototype;
  const { addEventListener, dispatchEvent } = proto;
  let pageAdd = 0;
  let pageDispatch = 0;
  try {
    proto.addEventListener = function () { pageAdd++; return addEventListener.apply(this, arguments); };
    proto.dispatchEvent = function () { pageDispatch++; return dispatchEvent.apply(this, arguments); };
    const x = new w.main.XMLHttpRequest();
    x.open("GET", GET_PC);
    x.send();
    x._text = body(321);
    x.fireLoad();
  } finally {
    proto.addEventListener = addEventListener;
    proto.dispatchEvent = dispatchEvent;
  }
  assert.equal(pageAdd, 0, "hook không được gọi addEventListener của trang");
  assert.equal(pageDispatch, 0, "hook không được gọi dispatchEvent của trang");
  assert.equal(w.store().get("321").entry.body, body(321));
});

test("trang ghi đè RegExp.test/Promise.then/Array.push/Date.now/String SAU khi hook gắn → hook vẫn chạy (H4…)", () => {
  // Hook gắn xong, trang phá các API toàn cục/prototype. Hook dùng tham chiếu đã chụp nên không bị ảnh hưởng;
  // nếu ai sửa hook gọi thẳng getPcRe.test()/Promise.then()/… thì các mutant đó chết ở đây.
  const doc = new EventTarget();
  const main = { URL, console, setTimeout, setImmediate, EventTarget, Event, CustomEvent, Response: FakeResponse, Request: FakeRequest,
    document: doc, location: { href: PAGE }, __hostFetch: () => Promise.resolve(new FakeResponse(body(432))) };
  vm.createContext(main);
  vm.runInContext(PAGE_SETUP, main);
  vm.runInContext(HOOK_SRC, main);
  const iso = { console, EventTarget, Event, CustomEvent, document: doc };
  vm.createContext(iso);
  vm.runInContext(STORE_SRC, iso);
  // Trang phá mọi thứ sau khi hook đã gắn.
  vm.runInContext(`
    RegExp.prototype.test = function () { return false; };
    RegExp.prototype.exec = function () { return null; };
    Promise.prototype.then = function () { throw new Error("then phá"); };
    Array.prototype.push = function () { throw new Error("push phá"); };
    Array.prototype.shift = function () { throw new Error("shift phá"); };
    Date.now = function () { throw new Error("now phá"); };
    globalThis.String = function () { return "PHÁ"; };
    globalThis.URL = function () { throw new Error("URL phá"); };
  `, main);
  return main.fetch(GET_PC).then(async () => {
    for (let i = 0; i < 8; i++) await new Promise((r) => setImmediate(r));
    const e = vm.runInContext("globalThis.__shopeeHarvestStore.get('432').entry", iso);
    assert.ok(e, "hook phải vẫn bắt được dù trang đã phá API");
    assert.equal(e.body, body(432));
    assert.ok(!/PHÁ/.test(e.url), "url không được dính String bị phá");
  });
});

// URL / FakeResponse / FakeRequest là của realm test, DÙNG CHUNG mọi test — test phá getter phải khôi phục sau.
const SHARED_PROTO_KEYS = [
  [URL.prototype, "pathname"], [URL.prototype, "searchParams"],
  [FakeResponse.prototype, "url"], [FakeRequest.prototype, "url"],
];
const withRestoredProtos = async (fn) => {
  const saved = SHARED_PROTO_KEYS.map(([o, k]) => [o, k, Object.getOwnPropertyDescriptor(o, k)]);
  try { return await fn(); } finally {
    for (const [o, k, d] of saved) { if (d) Object.defineProperty(o, k, d); else delete o[k]; }
  }
};

// Dựng trang + kho rồi chạy `sabotage` (mã trang) SAU khi hook gắn. Trả { main, S } với S() là kho ISOLATED.
const worldWithSabotage = (fetchImpl, sabotage) => {
  const doc = new EventTarget();
  const main = { URL, console, setTimeout, setImmediate, EventTarget, Event, CustomEvent, Response: FakeResponse, Request: FakeRequest,
    document: doc, location: { href: PAGE }, __hostFetch: fetchImpl };
  vm.createContext(main);
  vm.runInContext(PAGE_SETUP, main);
  vm.runInContext(HOOK_SRC, main);
  const iso = { console, EventTarget, Event, CustomEvent, document: doc };
  vm.createContext(iso);
  vm.runInContext(STORE_SRC, iso);
  vm.runInContext(sabotage, main);
  return { main, S: () => vm.runInContext("globalThis.__shopeeHarvestStore", iso) };
};

test("Mục1: trang ghi đè String SAU khi hook gắn → mã lỗi vẫn lưu đúng, String của trang KHÔNG nhận giá trị nào", async () => {
  const errBody = JSON.stringify({ error: 90309999, data: { item: { item_id: 811 } } });
  const { main, S } = worldWithSabotage(() => Promise.resolve(new FakeResponse(errBody)),
    `globalThis.__strCalls = []; globalThis.String = function (v) { globalThis.__strCalls.push(v); return "PHÁ"; };`);
  await main.fetch(GET_PC);
  await ticks();
  assert.equal(S().get("811").error.error, "90309999");
  assert.equal(vm.runInContext("globalThis.__strCalls.length", main), 0, "hook đã gọi String của trang");
});

test("Mục6: trang ghi đè getter responseType/response/responseURL/Request.url/URL.pathname SAU khi hook gắn → hook đọc bản gốc", async () => {
  await withRestoredProtos(async () => {
  const { main, S } = worldWithSabotage(() => Promise.resolve(new FakeResponse(body(921))), `
    const def = (o, k, g) => Object.defineProperty(o, k, { configurable: true, get: g });
    def(XMLHttpRequest.prototype, "responseType", () => "arraybuffer");          // giả: không phải text/json
    def(XMLHttpRequest.prototype, "response", () => ({ error: null, data: { item: { item_id: 999 } } }));
    def(XMLHttpRequest.prototype, "responseURL", () => "https://evil.test/khong-phai-get_pc");
    def(Request.prototype, "url", () => "https://evil.test/khong-phai-get_pc");
    def(URL.prototype, "pathname", () => "/khong-phai-get_pc");
  `);
  // fetch với Request (input.url) → URL.pathname bị phá; hook phải dùng getter gốc → vẫn nhận là get_pc.
  await main.fetch(new FakeRequest(`https://shopee.vn${GET_PC}`));
  await ticks();
  assert.ok(S().get("921").entry, "input.url / URL.pathname phải đọc qua getter gốc");
  // XHR json: responseType gốc là "json", response gốc là _resp → lưu 922, không phải 999 giả.
  const x = new main.XMLHttpRequest();
  x.open("GET", GET_PC);
  x.send();
  x._rt = "json";
  x._rurl = `https://shopee.vn${GET_PC}`;
  x._resp = { error: null, data: { item: { item_id: 922 } } };
  x.fireLoad();
  await ticks();
  assert.ok(S().get("922").entry, "responseType/response/responseURL phải đọc qua getter gốc");
  assert.equal(!!S().get("999").entry, false);
  });
});

test("xoá API thừa: pdp-hook.js không còn tham chiếu strIndexOf", () => {
  assert.equal(/strIndexOf/.test(HOOK_SRC), false);
});

test("trang ghi đè GETTER sau khi hook gắn (resp.url, xhr.responseText, URL.searchParams) → hook đọc bản GỐC", async () => {
  await withRestoredProtos(async () => {
  const doc = new EventTarget();
  const main = { URL, console, setTimeout, setImmediate, EventTarget, Event, CustomEvent, Response: FakeResponse, Request: FakeRequest,
    document: doc, location: { href: PAGE }, __hostFetch: null };
  // fetchImpl trả Response có url thật; sau khi hook gắn, trang bẻ getter url để trả url giả.
  const realUrl = `https://shopee.vn${GET_PC}`;
  main.__hostFetch = () => Promise.resolve(new FakeResponse(body(501), realUrl));
  vm.createContext(main);
  vm.runInContext(PAGE_SETUP, main);
  vm.runInContext(HOOK_SRC, main);
  const iso = { console, EventTarget, Event, CustomEvent, document: doc };
  vm.createContext(iso);
  vm.runInContext(STORE_SRC, iso);
  const S = () => vm.runInContext("globalThis.__shopeeHarvestStore", iso);

  // Trang bẻ các getter observable SAU khi hook đã chụp descriptor.
  vm.runInContext(`
    Object.defineProperty(Response.prototype, "url", { configurable: true, get() { return "https://evil.test/giả"; } });
    Object.defineProperty(XMLHttpRequest.prototype, "responseText", { configurable: true, get() { return '{"error":null,"data":{"item":{"item_id":999}}}'; } });
    Object.defineProperty(URL.prototype, "searchParams", { configurable: true, get() { throw new Error("searchParams phá"); } });
  `, main);

  // fetch: url phải là realUrl (getter gốc), không phải "evil".
  await main.fetch(GET_PC);
  await ticks();
  assert.equal(S().get("501").entry.url, realUrl, "resp.url phải đọc qua getter GỐC");

  // XHR: responseText phải đọc bản GỐC (_text), không phải bản giả 999.
  const x = new main.XMLHttpRequest();
  x.open("GET", GET_PC);
  x.send();
  x._text = body(777);
  x.fireLoad();
  await ticks();
  assert.ok(S().get("777").entry, "phải bắt được itemId 777 từ responseText GỐC");
  assert.equal(!!S().get("999").entry, false, "không được lưu bản responseText giả (999)");

  // itemIdFromUrl (body lỗi, item_id trong URL): URL.searchParams bị phá → phải dùng regex chuỗi, vẫn ghi lỗi.
  main.__hostFetch = () => Promise.resolve(new FakeResponse(JSON.stringify({ error: 90309999, data: null })));
  await main.fetch("/api/v4/pdp/get_pc?item_id=606&shop_id=1");
  await ticks();
  assert.equal(S().get("606").error.error, "90309999", "itemId lỗi phải lấy từ query qua regex (không searchParams)");
  });
});

test("Proxy giữ thuộc tính quan sát được y hàm gốc: fetch, XHR open/send, Function.prototype.toString", () => {
  const doc = new EventTarget();
  const main = { URL, console, setTimeout, EventTarget, Event, CustomEvent, Response: FakeResponse, Request: FakeRequest, document: doc, location: { href: PAGE }, __hostFetch: () => Promise.resolve(new FakeResponse("x")) };
  vm.createContext(main);
  vm.runInContext(PAGE_SETUP, main);
  const before = vm.runInContext(
    `({ fetch: __describe(fetch), open: __describe(XMLHttpRequest.prototype.open), send: __describe(XMLHttpRequest.prototype.send),
        toString: __describe(Function.prototype.toString) })`, main);
  const origFetch = main.fetch;
  vm.runInContext(HOOK_SRC, main);
  assert.notEqual(main.fetch, origFetch, "fetch phải đã được bọc");
  const after = vm.runInContext(
    `({ fetch: __describe(fetch), open: __describe(XMLHttpRequest.prototype.open), send: __describe(XMLHttpRequest.prototype.send),
        toString: __describe(Function.prototype.toString) })`, main);
  assert.deepEqual(after, before);
  assert.equal(before.fetch.hasPrototype, false);
  assert.equal(before.fetch.ctorError, "TypeError");
});

test("không có gì mới trên window của trang; kho không nhìn thấy từ world MAIN", async () => {
  const doc = new EventTarget();
  const main = { URL, console, setTimeout, setImmediate, EventTarget, Event, CustomEvent, Response: FakeResponse, Request: FakeRequest, document: doc, location: { href: PAGE },
    __hostFetch: () => Promise.resolve(new FakeResponse(body(987654321))) };
  vm.createContext(main);
  vm.runInContext(PAGE_SETUP, main);
  const names = () => vm.runInContext("Object.getOwnPropertyNames(globalThis).sort().join(',')", main);
  const before = names();
  vm.runInContext(HOOK_SRC, main);
  const iso = { console, EventTarget, Event, CustomEvent, document: doc };
  vm.createContext(iso);
  vm.runInContext(STORE_SRC, iso);
  await main.fetch(GET_PC);
  await ticks();
  assert.equal(names(), before);
  assert.equal(vm.runInContext("typeof __shopeeHarvestStore", main), "undefined");
  assert.equal(vm.runInContext("typeof __shopeeHarvestPdp", main), "undefined");
  assert.ok(vm.runInContext("__shopeeHarvestStore.get('987654321').entry", iso)); // còn bên ISOLATED thì có
});

test("hằng tên event trong pdp-hook.js và pdp-store.js trùng nhau", () => {
  const constOf = (src, name) => new RegExp(`const ${name} = "([^"]+)"`).exec(src)?.[1];
  for (const name of ["PDP_EVENT", "REQ_EVENT"]) {
    assert.ok(constOf(HOOK_SRC, name), `${name} trong hook`);
    assert.equal(constOf(HOOK_SRC, name), constOf(STORE_SRC, name), name);
  }
});

test("error:0 là THÀNH CÔNG → vào kho dữ liệu (không phải lỗi); error khác 0/null → ghi lỗi", async () => {
  const ok0 = JSON.stringify({ error: 0, data: { item: { item_id: 61, shop_id: 1 } } });
  const w = makeWorlds({ fetchImpl: () => Promise.resolve(new FakeResponse(ok0)) });
  await w.main.fetch(GET_PC);
  await ticks();
  assert.equal(w.store().get("61").entry.body, ok0);
  assert.equal(w.store().get("61").error, null);
});

test("errors.delete: sau khi có lỗi rồi lại lấy được body cùng SP → lỗi cũ bị xoá", async () => {
  let turn = 0;
  const w = makeWorlds({
    fetchImpl: () => Promise.resolve(new FakeResponse(
      turn++ === 0 ? JSON.stringify({ error: 90309999, data: { item: { item_id: 70 } } }) : body(70))),
  });
  await w.main.fetch("/api/v4/pdp/get_pc?item_id=70");
  await ticks();
  assert.equal(w.store().get("70").error.error, "90309999");
  assert.equal(w.store().get("70").entry, null);
  await w.main.fetch("/api/v4/pdp/get_pc?item_id=70");
  await ticks();
  assert.equal(w.store().get("70").entry.body, body(70));
  assert.equal(w.store().get("70").error, null, "lỗi cũ phải bị xoá");
});

test("store hỏi lại (REQ_EVENT) → hook trả 'ready' kể cả khi chưa thấy get_pc nào", () => {
  // Hook gắn trước, chưa có get_pc; store gắn sau bắn REQ → hook phải trả ready (hookReady=true).
  const w = makeWorlds({ order: "hook-only" });
  w.runStore();
  assert.equal(w.list().status.hookReady, true);
  assert.equal(w.list().status.seen, 0);
});

test("callback `then` của hook không lộ mã nguồn qua toString (đưa vào masked)", async () => {
  const doc = new EventTarget();
  const main = { URL, console, setTimeout, setImmediate, EventTarget, Event, CustomEvent, Response: FakeResponse,
    document: doc, location: { href: PAGE }, __hostFetch: () => Promise.resolve(new FakeResponse(body(80))) };
  vm.createContext(main);
  vm.runInContext(PAGE_SETUP, main);
  // Spy context Promise.prototype.then: ghi lại callback onFulfilled mà hook truyền cho .then của promise trang.
  vm.runInContext(`
    globalThis.__thenCallbacks = [];
    const origThen = Promise.prototype.then;
    Promise.prototype.then = function (onF, onR) { if (onF) globalThis.__thenCallbacks.push(onF); return origThen.call(this, onF, onR); };
  `, main);
  vm.runInContext(HOOK_SRC, main);
  await main.fetch(GET_PC);
  await ticks();
  // Callback hook truyền cho then phải: String(cb) và Function.prototype.toString.call(cb) đều ra native, không lộ "report"/"clone".
  const report = vm.runInContext(`
    (() => {
      const cbs = globalThis.__thenCallbacks;
      const bad = [];
      for (const cb of cbs) {
        const s = Function.prototype.toString.call(cb);
        if (/report|clone|resp|buffer/.test(s)) bad.push(s.slice(0, 40));
      }
      return { n: cbs.length, bad };
    })()
  `, main);
  assert.ok(report.n >= 1, "phải có ít nhất 1 callback hook truyền cho then");
  assert.deepEqual([...report.bad], [], "không callback nào của hook được lộ mã nguồn");
});

test("getter trên Object.prototype của trang KHÔNG đọc được detail / option (nhờ { __proto__: null })", async () => {
  const doc = new EventTarget();
  const main = { URL, console, setTimeout, setImmediate, EventTarget, Event, CustomEvent, Response: FakeResponse,
    document: doc, location: { href: PAGE }, __hostFetch: () => Promise.resolve(new FakeResponse(body(90))) };
  vm.createContext(main);
  vm.runInContext(PAGE_SETUP, main);
  // Trang cài getter rình các khoá mà trình duyệt đọc từ init object (CustomEvent: detail/bubbles; listener: once).
  vm.runInContext(`
    globalThis.__spy = [];
    for (const k of ["detail", "bubbles", "cancelable", "once", "capture", "passive"]) {
      Object.defineProperty(Object.prototype, k, { configurable: true, get() { globalThis.__spy.push(k); return undefined; } });
    }
  `, main);
  vm.runInContext(HOOK_SRC, main);
  const x = new main.XMLHttpRequest();
  x.open("GET", GET_PC);
  x.send();
  x._text = body(90);
  x.fireLoad();
  await main.fetch(GET_PC);
  await ticks();
  const spy = vm.runInContext("globalThis.__spy.slice()", main);
  assert.deepEqual([...spy], [], `getter của trang không được kích hoạt, nhưng thấy: ${spy}`);
});

test("luật nhận body của hook trùng pdp-core.checkPdpBody (hai bản không lệch nhau)", async () => {
  const samples = [
    body(1),
    JSON.stringify({ data: { item: { item_id: 2 } } }),
    JSON.stringify({ error: 0, data: { item: { item_id: 3 } } }),
    JSON.stringify({ error: null, data: { item: { item_id: "4" } } }),
    JSON.stringify({ error: null, data: { item: { item_id: "x5" } } }),
    JSON.stringify({ error: null, data: { item: { item_id: 6.5 } } }),
    JSON.stringify({ error: "bad", data: { item: { item_id: 7 } } }),
    JSON.stringify({ error: null, data: null }),
    "[]",
    "null",
    "not json",
  ];
  for (const text of samples) {
    const w = makeWorlds({ fetchImpl: () => Promise.resolve(new FakeResponse(text)) });
    await w.main.fetch("/api/v4/pdp/get_pc?shop_id=1");
    await ticks();
    assert.equal(w.list().entries.length === 1, checkPdpBody(text).ok, text);
  }
});
