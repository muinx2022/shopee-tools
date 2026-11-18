// GĐ7: gọi Hub (hub-client.js) + hàng đợi gửi lại (hub-queue.js) — fetch / storage / đồng hồ giả.
// Chạy: node --test extensions/shopee-harvest/tests/*.test.mjs (từ workspace/).
import test from "node:test";
import assert from "node:assert/strict";
import {
  normalizeHubUrl, hubPermissionPattern, isHubConfigured, classifyHttp, postBatch, postKnown, HUB_ROUTES,
} from "../hub-client.js";
import { createHubQueue, backoffDelay, BACKOFF_STEPS_MS, INDEX_KEY, ITEM_PREFIX, LIMITS } from "../hub-queue.js";

const CFG = { url: "https://hub.example.com/", token: " tok ", machineId: "ext-1" };

const fakeFetch = (handler) => {
  const calls = [];
  const fn = async (url, init) => {
    calls.push({ url, init, body: init.body });
    const r = await handler(url, init, calls.length);
    return { status: r.status, json: async () => { if (r.json === undefined) throw new Error("no json"); return r.json; } };
  };
  fn.calls = calls;
  return fn;
};

// ── hub-client ────────────────────────────────────────────────────────────────────────────────────────────────────

test("normalizeHubUrl: chỉ https, hoặc http tới localhost / 127.0.0.1; bỏ path và / cuối", () => {
  assert.equal(normalizeHubUrl("https://hub.example.com/abc?x=1"), "https://hub.example.com");
  assert.equal(normalizeHubUrl(" http://localhost:8088/ "), "http://localhost:8088");
  assert.equal(normalizeHubUrl("http://127.0.0.1:9"), "http://127.0.0.1:9");
  assert.equal(normalizeHubUrl("http://hub.example.com"), null); // token không đi qua http trơn ra ngoài
  assert.equal(normalizeHubUrl("ftp://x"), null);
  assert.equal(normalizeHubUrl("không phải url"), null);
  assert.equal(normalizeHubUrl(""), null);
  assert.equal(hubPermissionPattern("http://localhost:8088/x"), "http://localhost/*");
  assert.equal(hubPermissionPattern("https://hub.example.com:8443"), "https://hub.example.com/*");
});

test("isHubConfigured: cần đủ URL hợp lệ + token + mã máy", () => {
  assert.equal(isHubConfigured(CFG), true);
  assert.equal(isHubConfigured({ ...CFG, token: " " }), false);
  assert.equal(isHubConfigured({ ...CFG, machineId: "" }), false);
  assert.equal(isHubConfigured({ ...CFG, url: "http://hub.example.com" }), false);
  assert.equal(isHubConfigured(null), false);
});

test("classifyHttp: 200 ok; 401/403 auth; 5xx/503 pg-unavailable/408/429 tạm; 4xx khác bad (413 / batch-too-large = quá cỡ)", () => {
  assert.equal(classifyHttp(200, {}).kind, "ok");
  assert.equal(classifyHttp(401, null).kind, "auth");
  assert.equal(classifyHttp(403, null).kind, "auth");
  for (const s of [500, 502, 503, 408, 429]) assert.equal(classifyHttp(s, null).kind, "temp", String(s));
  assert.match(classifyHttp(503, { error: "pg-unavailable" }).reason, /pg-unavailable/);
  const bad = classifyHttp(400, { error: "client-now" });
  assert.equal(bad.kind, "bad");
  assert.equal(bad.tooLarge, false);
  assert.equal(classifyHttp(413, null).tooLarge, true);
  assert.equal(classifyHttp(400, { error: "batch-too-large" }).tooLarge, true);
});

test("postBatch: đúng route + header, clientNow lúc gửi, mục giữ nguyên chuỗi (BigInt không qua Number)", async () => {
  const f = fakeFetch(() => ({ status: 200, json: { results: [{ index: 0, itemId: 1, ok: true, applied: true }] } }));
  const item = '{"itemId":9007199254740993,"rawJson":"{}"}';
  const r = await postBatch(CFG, [item], { fetchImpl: f, now: () => Date.UTC(2026, 10, 18, 3, 4, 5) });
  assert.equal(r.kind, "ok");
  assert.equal(f.calls[0].url, "https://hub.example.com" + HUB_ROUTES.batch);
  assert.equal(f.calls[0].init.method, "POST");
  assert.equal(f.calls[0].init.headers["X-Api-Token"], "tok");
  assert.equal(f.calls[0].init.headers["X-Machine-Id"], "ext-1");
  assert.equal(f.calls[0].body, `{"machineId":"ext-1","clientNow":"2026-11-18T03:04:05.000Z","items":[${item}]}`);
});

test("postBatch: lỗi mạng / hết giờ → temp; 200 lệch số mục → temp; 401 auth; 400 bad; thiếu cấu hình → nocfg", async () => {
  assert.equal((await postBatch(CFG, ["{}"], { fetchImpl: async () => { throw new TypeError("Failed to fetch"); } })).kind, "temp");
  const lech = fakeFetch(() => ({ status: 200, json: { results: [] } }));
  assert.equal((await postBatch(CFG, ["{}"], { fetchImpl: lech })).kind, "temp");
  const hong = fakeFetch(() => ({ status: 200 })); // body không phải JSON
  assert.equal((await postBatch(CFG, ["{}"], { fetchImpl: hong })).kind, "temp");
  assert.equal((await postBatch(CFG, ["{}"], { fetchImpl: fakeFetch(() => ({ status: 401 })) })).kind, "auth");
  assert.equal((await postBatch(CFG, ["{}"], { fetchImpl: fakeFetch(() => ({ status: 400, json: { error: "x" } })) })).kind, "bad");
  assert.equal((await postBatch({ url: "", token: "t", machineId: "m" }, ["{}"], { fetchImpl: fakeFetch(() => ({ status: 200 })) })).kind, "nocfg");
});

test("postKnown: gửi itemIds + maxAgeDays, đọc items", async () => {
  const f = fakeFetch(() => ({ status: 200, json: { items: [{ itemId: 5, harvestedAt: "2026-11-01T00:00:00Z" }] } }));
  const r2 = await postKnown(CFG, ["5", "x", "6"], { fetchImpl: f });
  assert.equal(r2.kind, "ok");
  assert.equal(f.calls[0].url, "https://hub.example.com" + HUB_ROUTES.known);
  assert.equal(f.calls[0].body, '{"itemIds":[5,6],"maxAgeDays":3650}');
  assert.equal(r2.items[0].itemId, 5);
});

// ── hub-queue ─────────────────────────────────────────────────────────────────────────────────────────────────────

const memStorage = () => {
  const data = {};
  return {
    data,
    get: async (keys) => Object.fromEntries(keys.filter((k) => k in data).map((k) => [k, structuredClone(data[k])])),
    set: async (obj) => { Object.assign(data, structuredClone(obj)); },
    remove: async (keys) => { for (const k of keys) delete data[k]; },
  };
};

const makeQueue = (sendImpl, extra = {}) => {
  const storage = memStorage();
  let now = 1_000_000;
  const sent = [];
  const q = createHubQueue({
    storage,
    now: () => now,
    send: async (profile, bodies) => { sent.push({ profile, bodies }); return sendImpl(profile, bodies, sent.length); },
    ...extra,
  });
  return { q, storage, sent, tick: (ms) => { now += ms; }, now: () => now };
};

const okAll = (_p, bodies) => ({ kind: "ok", results: bodies.map((_, i) => ({ index: i, ok: true, applied: true })) });

test("hàng đợi: gửi được thì xoá cả meta lẫn thân mục", async () => {
  const { q, storage, sent } = makeQueue(okAll);
  await q.enqueue({ profile: "manual", key: "manual|1||", itemId: "1", body: '{"itemId":1}' });
  await q.enqueue({ profile: "manual", key: "manual|2||", itemId: "2", body: '{"itemId":2}' });
  const out = await q.flush();
  assert.equal(out.saved, 2);
  assert.deepEqual(sent[0].bodies, ['{"itemId":1}', '{"itemId":2}']);
  assert.deepEqual(await q.stats(), { pending: 0, error: 0, bytes: 0, nextAt: 0, lastError: null });
  assert.deepEqual(Object.keys(storage.data).filter((k) => k.startsWith(ITEM_PREFIX)), []);
  assert.equal(await q.nextAlarmAt(), 0);
});

test("hàng đợi: Hub từ chối từng mục (ok=false) → mục đó thành lỗi, mục kia xoá; Gửi lại đưa mục lỗi về chờ", async () => {
  let reject = true;
  const { q } = makeQueue((_p, bodies) => ({
    kind: "ok",
    results: bodies.map((b, i) => ({ index: i, ok: !(reject && b.includes('"itemId":2')), reason: "raw-item-mismatch" })),
  }));
  await q.enqueue({ profile: "manual", key: "a", itemId: "1", body: '{"itemId":1}' });
  await q.enqueue({ profile: "manual", key: "b", itemId: "2", body: '{"itemId":2}' });
  await q.flush();
  const items = await q.list();
  assert.equal(items.length, 1);
  assert.equal(items[0].status, "error");
  assert.match(items[0].lastError, /raw-item-mismatch/);
  assert.equal((await q.stats()).error, 1);
  assert.equal(await q.nextAlarmAt(), 0); // mục lỗi không tự gửi lại

  reject = false;
  assert.equal(await q.requeueErrors(), 1);
  await q.flush();
  assert.equal((await q.list()).length, 0);
});

test("hàng đợi: mất mạng / 5xx → giữ nguyên, lùi dần CHUNG (30s, 60s, 120s…), chưa tới hạn thì không gửi, force thì gửi", async () => {
  let mode = "temp";
  const { q, sent, tick, now } = makeQueue((p, bodies) => (mode === "temp" ? { kind: "temp", reason: "Hub tạm lỗi (HTTP 503 pg-unavailable)" } : okAll(p, bodies)));
  await q.enqueue({ profile: "manual", key: "a", itemId: "1", body: "{}" });

  await q.flush();
  let st = await q.stats();
  assert.equal(st.pending, 1);
  assert.equal(st.nextAt, now() + BACKOFF_STEPS_MS[0]);
  assert.match(st.lastError, /503/);
  assert.equal((await q.list())[0].attempts, 1);

  const before = sent.length;
  assert.equal((await q.flush()).deferred, true); // chưa tới hạn
  assert.equal(sent.length, before);

  tick(BACKOFF_STEPS_MS[0]);
  await q.flush();
  st = await q.stats();
  assert.equal(st.nextAt, now() + BACKOFF_STEPS_MS[1]); // lần hỏng thứ 2 → bậc 2

  mode = "ok";
  await q.flush({ force: true });
  assert.equal((await q.stats()).pending, 0);
});

test("lùi dần: bậc tăng rồi giữ ở trần 15 phút, không đặt trần số lần", () => {
  assert.equal(backoffDelay(1), 30_000);
  assert.equal(backoffDelay(2), 60_000);
  assert.equal(backoffDelay(6), 900_000);
  assert.equal(backoffDelay(50), 900_000);
});

test("hàng đợi: 401 / chưa cấu hình cũng chỉ lùi dần (không phải lỗi dữ liệu); 400 → lỗi mục", async () => {
  const { q } = makeQueue(() => ({ kind: "auth", reason: "Hub từ chối token (HTTP 401)" }));
  await q.enqueue({ profile: "manual", key: "a", itemId: "1", body: "{}" });
  await q.flush();
  assert.equal((await q.list())[0].status, "pending");

  const b = makeQueue(() => ({ kind: "bad", reason: "Hub từ chối request (HTTP 400 client-now)", tooLarge: false }));
  await b.q.enqueue({ profile: "manual", key: "a", itemId: "1", body: "{}" });
  await b.q.flush();
  const it = (await b.q.list())[0];
  assert.equal(it.status, "error");
  assert.match(it.lastError, /client-now/);
});

test("hàng đợi: lô quá cỡ (413) nhiều mục → chuyển gửi lẻ, không đánh lỗi; 413 của một mục lẻ → lỗi mục", async () => {
  const { q, sent } = makeQueue((p, bodies) => {
    if (bodies.length > 1) return { kind: "bad", tooLarge: true, reason: "HTTP 413" };
    if (bodies[0].includes("to")) return { kind: "bad", tooLarge: true, reason: "HTTP 413" };
    return okAll(p, bodies);
  });
  await q.enqueue({ profile: "manual", key: "a", itemId: "1", body: '"nho"' });
  await q.enqueue({ profile: "manual", key: "b", itemId: "2", body: '"to"' });
  await q.flush();
  assert.equal(sent[0].bodies.length, 2);
  assert.ok(sent.slice(1).every((s) => s.bodies.length === 1));
  const items = await q.list();
  assert.equal(items.length, 1);
  assert.equal(items[0].itemId, "2");
  assert.equal(items[0].status, "error");
});

test("hàng đợi: giới hạn số mục / tổng byte / cỡ một mục → từ chối nhận (báo rõ), không âm thầm bỏ mục cũ", async () => {
  const limits = { ...LIMITS, maxItems: 2, maxBytes: 90, maxItemBytes: 60 };
  const { q } = makeQueue(okAll, { limits });
  assert.equal((await q.enqueue({ profile: "m", key: "a", itemId: "1", body: "x".repeat(40) })).ok, true);
  const full = await q.enqueue({ profile: "m", key: "b", itemId: "2", body: "x".repeat(61) });
  assert.equal(full.ok, false);
  assert.match(full.reason, /quá lớn/);
  const bytes = await q.enqueue({ profile: "m", key: "c", itemId: "3", body: "x".repeat(60) });
  assert.equal(bytes.ok, false);
  assert.match(bytes.reason, /đầy/);
  assert.equal((await q.enqueue({ profile: "m", key: "d", itemId: "4", body: "y" })).ok, true);
  const count = await q.enqueue({ profile: "m", key: "e", itemId: "5", body: "z" });
  assert.equal(count.ok, false);
  assert.equal((await q.list()).map((m) => m.itemId).join(","), "1,4"); // mục cũ còn nguyên
});

test("hàng đợi: cùng khoá (cùng SP + nguồn) → thay bản cũ bằng bản mới, về chờ gửi", async () => {
  const { q, storage } = makeQueue(() => ({ kind: "bad", reason: "x" }));
  await q.enqueue({ profile: "m", key: "k", itemId: "1", body: '"cu"' });
  await q.flush();
  assert.equal((await q.list())[0].status, "error");
  await q.enqueue({ profile: "m", key: "k", itemId: "1", body: '"moi"' });
  const items = await q.list();
  assert.equal(items.length, 1);
  assert.equal(items[0].status, "pending");
  assert.equal(storage.data[ITEM_PREFIX + items[0].id], '"moi"');
});

test("hàng đợi: gửi theo từng profile (manual / auto dùng cấu hình khác nhau), thân mục mất thì bỏ meta mồ côi", async () => {
  const { q, sent, storage } = makeQueue(okAll);
  await q.enqueue({ profile: "auto", key: "a", itemId: "1", body: "1" });
  await q.enqueue({ profile: "manual", key: "b", itemId: "2", body: "2" });
  await q.enqueue({ profile: "auto", key: "c", itemId: "3", body: "3" });
  const id3 = (await q.list()).find((m) => m.key === "c").id;
  delete storage.data[ITEM_PREFIX + id3];
  await q.flush();
  assert.deepEqual(sent.map((s) => [s.profile, s.bodies.join("+")]), [["auto", "1"], ["manual", "2"]]);
  assert.deepEqual(await q.list(), []);
  assert.ok(storage.data[INDEX_KEY]);
});

test("hàng đợi: profile manual hỏng (401 / chưa cấu hình) KHÔNG chặn mục auto phía sau; mỗi profile lùi dần riêng", async () => {
  const { q, sent, tick, now } = makeQueue((p, bodies) => (p === "manual" ? { kind: "auth", reason: "Hub từ chối token (HTTP 401)" } : okAll(p, bodies)));
  await q.enqueue({ profile: "manual", key: "m1", itemId: "1", body: '"m1"' });
  tick(1000);
  for (let i = 2; i <= 4; i++) { await q.enqueue({ profile: "auto", key: `a${i}`, itemId: String(i), body: `"a${i}"` }); tick(1000); }
  await q.flush();
  assert.deepEqual(sent.map((s) => s.profile), ["manual", "auto"]); // manual hỏng → auto vẫn đi ngay trong lượt
  let items = await q.list();
  assert.deepEqual(items.map((m) => m.profile), ["manual"]);
  const st = await q.stats();
  assert.equal(st.nextAt, now() + BACKOFF_STEPS_MS[0]);
  assert.match(st.lastError, /401/);

  // Mục auto mới tới trong lúc manual đang lùi dần → gửi được ngay, manual vẫn chờ hạn của RIÊNG nó.
  await q.enqueue({ profile: "auto", key: "a9", itemId: "9", body: '"a9"' });
  const before = sent.length;
  await q.flush();
  assert.deepEqual(sent.slice(before).map((s) => s.profile), ["auto"]);
  items = await q.list();
  assert.deepEqual(items.map((m) => m.profile), ["manual"]);
  assert.equal(items[0].attempts, 1);
});

test("hàng đợi: sau 413 profile gửi LẺ cho tới khi hết mục chờ (không quay lại gửi lô giữa chừng)", async () => {
  let down = false;
  const { q, sent } = makeQueue((p, bodies) => {
    if (bodies.length > 1) return { kind: "bad", tooLarge: true, reason: "HTTP 413" };
    if (down) return { kind: "temp", reason: "Hub tạm lỗi (HTTP 503)" };
    return okAll(p, bodies);
  });
  for (const k of ["a", "b", "c"]) await q.enqueue({ profile: "m", key: k, itemId: k, body: `"${k}"` });
  down = true;            // lô 3 mục bị 413 → chuyển lẻ; mục lẻ đầu gặp 503 → lùi dần
  await q.flush();
  assert.equal(sent[0].bodies.length, 3);
  assert.equal(sent[1].bodies.length, 1);
  down = false;
  await q.enqueue({ profile: "m", key: "d", itemId: "d", body: '"d"' });
  await q.flush({ force: true });
  assert.ok(sent.slice(1).every((s) => s.bodies.length === 1), JSON.stringify(sent.map((s) => s.bodies.length)));
  assert.equal((await q.list()).length, 0);
  // Hết mục chờ → về gửi lô.
  for (const k of ["e", "f"]) await q.enqueue({ profile: "m", key: k, itemId: k, body: `"${k}"` });
  const n = sent.length;
  await q.flush();
  assert.equal(sent[n].bodies.length, 2);
});

test("hàng đợi: nhận mục ở trạng thái lỗi kèm lý do (Hub vừa từ chối) — không tự gửi, Gửi lại đưa về chờ", async () => {
  const { q, sent } = makeQueue(okAll);
  await q.enqueue({ profile: "manual", key: "k", itemId: "7", body: '"x"', status: "error", lastError: "Hub từ chối: raw-error" });
  const it = (await q.list())[0];
  assert.equal(it.status, "error");
  assert.equal(it.lastError, "Hub từ chối: raw-error");
  await q.flush();
  assert.equal(sent.length, 0);
  assert.equal(await q.nextAlarmAt(), 0);
  await q.requeueErrors();
  await q.flush();
  assert.equal(sent.length, 1);
});

test("hàng đợi: enqueue song song không đè mất mục (thao tác nối tiếp)", async () => {
  const { q } = makeQueue(okAll);
  await Promise.all(Array.from({ length: 10 }, (_, i) => q.enqueue({ profile: "m", key: `k${i}`, itemId: String(i), body: String(i) })));
  assert.equal((await q.list()).length, 10);
});
