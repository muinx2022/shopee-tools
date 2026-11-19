// GĐ9: ảnh gốc lên Hub (source-images.js) — luật mã ảnh (đối chiếu fixture chung với C#), loại ảnh theo magic, phân loại
// phản hồi Hub / CDN, hàng đợi việc ảnh + lượt xử lý với fetch / storage / đồng hồ giả.
// Chạy: node --test extensions/shopee-harvest/tests/*.test.mjs (từ workspace/).
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  imageIdsOf, detectImageType, classifyPut, classifyMissing, classifyCdn, createSourceImageQueue, isValidImageId,
  massUploadClean, STATE_KEY, LIMITS, CDN_BASE, SOURCE_IMAGE_ROUTES,
} from "../source-images.js";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const FIX = path.resolve(HERE, "../../../tests/Shopee.Core.Tests/Fixtures/source-image-ids/cases.json");
const CASES = JSON.parse(fs.readFileSync(FIX, "utf8")).cases;

const CFG = { url: "https://hub.example.com/", token: " tok ", machineId: "ext-1" };
const HUB = "https://hub.example.com";
const id = (n) => `vn-11134207-img${String(n).padStart(4, "0")}`;

// ── Luật mã ảnh ────────────────────────────────────────────────────────────────────────────────────────────────────

test("fixture chung với C#: có đủ ca (chống xanh giả khi đường dẫn hỏng)", () => {
  assert.ok(CASES.length >= 8, `chỉ thấy ${CASES.length} ca`);
});

for (const c of CASES) {
  test(`imageIdsOf khớp fixture: ${c.name}`, () => {
    const r = imageIdsOf(JSON.stringify(c.json));
    assert.deepEqual(r.ids, c.expected);                       // đúng thứ tự: ảnh SP trước, ảnh nhóm 1 sau
    assert.deepEqual([...r.skipped].sort(), [...c.skipped].sort());
    for (const x of r.ids) assert.ok(isValidImageId(x), x);
  });
}

test("imageIdsOf: JSON hỏng / không phải object / rỗng → không có id, không ném", () => {
  for (const bad of ["{", "", "null", "[1,2]", "42", undefined, null, '"x"']) {
    assert.deepEqual(imageIdsOf(bad), { ids: [], skipped: [] }, String(bad));
  }
});

test("massUploadClean: bỏ ký tự điều khiển (trừ tab / LF), surrogate lẻ, U+FFFE/FFFF; CRLF → LF; NFC", () => {
  assert.equal(massUploadClean("a\u0001b\tc\r\nd\re"), "ab\tc\nd\ne");
  assert.equal(massUploadClean("x\ud800y\udc00z￾￿"), "xyz");
  assert.equal(massUploadClean("😀"), "😀");
  assert.equal(massUploadClean("é"), "é");
  assert.equal(massUploadClean(null), "");
});

test("detectImageType: JPEG / PNG theo magic, WebP nhận ra riêng, còn lại null", () => {
  assert.equal(detectImageType(new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 0])), "image/jpeg");
  assert.equal(detectImageType(new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0])), "image/png");
  assert.equal(detectImageType(new TextEncoder().encode("RIFF\u0001\u0002\u0003\u0004WEBPVP8 ")), "image/webp");
  assert.equal(detectImageType(new Uint8Array([0x89, 0x50, 0x4e, 0x47])), null);          // PNG cụt
  assert.equal(detectImageType(new Uint8Array([0xff, 0xd8])), null);
  assert.equal(detectImageType(new TextEncoder().encode("<html>")), null);
  assert.equal(detectImageType(new Uint8Array([])), null);
});

test("classifyPut / classifyMissing / classifyCdn: đủ các mã của hợp đồng Hub", () => {
  assert.deepEqual(classifyPut(200, { id: "x", existed: true, replaced: false }), { kind: "ok", existed: true, replaced: false });
  assert.equal(classifyPut(200, { existed: false }).existed, false);
  assert.equal(classifyPut(200, null).kind, "temp");
  for (const [s, code] of [[400, "bad-id"], [400, "empty"], [400, "corrupt-image"], [413, "too-large"], [415, "unsupported-type"]]) {
    const c = classifyPut(s, { error: code });
    assert.equal(c.kind, "bad", `${s} ${code}`);
    assert.match(c.reason, new RegExp(code));
  }
  for (const code of ["source-quota-exceeded", "disk-low"]) {
    const c = classifyPut(507, { error: code });
    assert.equal(c.kind, "hubFull");
    assert.equal(c.code, code);
  }
  assert.equal(classifyPut(507, null).code, "hub-full");
  for (const s of [401, 403]) assert.equal(classifyPut(s, null).kind, "auth");
  for (const s of [0, 408, 429, 500, 502, 503]) assert.equal(classifyPut(s, null).kind, "temp", String(s));
  // 4xx lạ (route chưa có, proxy chặn) là lỗi môi trường, không phải lỗi ảnh.
  for (const [s, code] of [[404, null], [405, null], [400, "lạ"], [409, null]]) assert.equal(classifyPut(s, { error: code }).kind, "temp", `${s} ${code}`);

  assert.deepEqual(classifyMissing(200, { missing: ["a", 1, "b"] }), { kind: "ok", missing: ["a", "b"] });
  assert.equal(classifyMissing(200, {}).kind, "temp");
  assert.equal(classifyMissing(400, { error: "too-many-ids" }).kind, "bad");
  assert.equal(classifyMissing(400, { error: "bad-id" }).kind, "bad");
  for (const [s, code] of [[404, null], [405, null], [415, null], [400, null], [400, "khác"]]) {
    assert.equal(classifyMissing(s, { error: code }).kind, "temp", `${s} ${code}`);
  }
  assert.equal(classifyMissing(401, null).kind, "auth");
  assert.equal(classifyMissing(507, { error: "disk-low" }).kind, "hubFull");
  assert.equal(classifyMissing(503, null).kind, "temp");

  assert.equal(classifyCdn(200).kind, "ok");
  for (const s of [404, 410]) assert.equal(classifyCdn(s).kind, "bad", String(s));
  for (const s of [401, 403, 407, 408, 429, 500, 503]) assert.equal(classifyCdn(s).kind, "temp", String(s));
});

// ── Hàng đợi + lượt ────────────────────────────────────────────────────────────────────────────────────────────────

const JPEG = new Uint8Array([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3, 4]);
const PNG = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 9, 9]);
const WEBP = new TextEncoder().encode("RIFF\u0000\u0000\u0000\u0000WEBPVP8 ");

const memStorage = () => {
  const data = {};
  return {
    data,
    get: async (keys) => Object.fromEntries(keys.filter((k) => k in data).map((k) => [k, structuredClone(data[k])])),
    set: async (obj) => { for (const [k, v] of Object.entries(obj)) data[k] = structuredClone(v); },
  };
};

const response = (status, { bytes = null, json, headers = {} } = {}) => ({
  status,
  headers: { get: (h) => headers[h.toLowerCase()] ?? null },
  arrayBuffer: async () => (bytes ?? new Uint8Array()).buffer.slice(0),
  json: async () => { if (json === undefined) throw new Error("no json"); return json; },
  body: { cancel: async () => {} },
});

/**
 * Môi trường giả: đồng hồ t (mỗi lần gọi fetch tốn `cost` ms), CDN theo bảng `cdn` (id → hàm/kq, mặc định JPEG), Hub: missing
 * theo `missing(ids)` (mặc định: thiếu hết), PUT theo `put(id, init)` (mặc định 200 existed=false).
 */
const makeEnv = ({ cdn = {}, missing = (ids) => ({ status: 200, json: { missing: ids } }), put = () => null, cost = 10, limits } = {}) => {
  const env = { t: 1_000_000, calls: [], cdnCalls: [], putCalls: [], missingCalls: [] };
  env.storage = memStorage();
  env.fetch = async (url, init) => {
    env.calls.push({ url, init });
    env.t += cost;
    if (init.signal?.aborted) throw Object.assign(new Error("aborted"), { name: "AbortError" });
    if (url.startsWith(CDN_BASE)) {
      const imgId = url.slice(CDN_BASE.length);
      env.cdnCalls.push({ id: imgId, init });
      const spec = cdn[imgId];
      const r = typeof spec === "function" ? await spec(init) : spec;
      if (r instanceof Error) throw r;
      return r ?? response(200, { bytes: JPEG });
    }
    if (url === HUB + SOURCE_IMAGE_ROUTES.missing) {
      const ids = JSON.parse(init.body).ids;
      env.missingCalls.push({ ids, init });
      const r = missing(ids);
      if (r instanceof Error) throw r;
      return response(r.status, { json: r.json });
    }
    if (url.startsWith(HUB + SOURCE_IMAGE_ROUTES.put)) {
      const imgId = decodeURIComponent(url.slice((HUB + SOURCE_IMAGE_ROUTES.put).length));
      env.putCalls.push({ id: imgId, init });
      const r = put(imgId, init) ?? { status: 200, json: { id: imgId, existed: false, replaced: false, size: 8, contentType: "image/jpeg" } };
      if (r instanceof Error) throw r;
      return response(r.status, { json: r.json });
    }
    throw new Error(`URL lạ ${url}`);
  };
  env.q = createSourceImageQueue({ storage: env.storage, fetchImpl: env.fetch, now: () => env.t, limits: limits ? { ...LIMITS, ...limits } : LIMITS });
  env.state = () => env.storage.data[STATE_KEY];
  env.round = (o = {}) => env.q.runRound({ profile: "auto", cfg: CFG, budgetMs: 60_000, ...o });
  return env;
};

test("lượt: chỉ tải id Hub báo thiếu; CDN đúng Accept + credentials omit; PUT đúng route / Content-Type / header; xong việc thì xoá", async () => {
  const env = makeEnv({
    missing: (ids) => ({ status: 200, json: { missing: ids.filter((x) => x !== id(2)) } }),
    cdn: { [id(3)]: response(200, { bytes: PNG }) },
    put: (x) => (x === id(3) ? { status: 200, json: { id: x, existed: true, replaced: false } } : null),
  });
  assert.deepEqual(await env.q.enqueue({ profile: "auto", itemId: "77", ids: [id(1), id(2), id(3)], skipped: 2 }), { ok: true });
  const r = await env.round();

  assert.deepEqual(env.missingCalls.map((c) => c.ids), [[id(1), id(2), id(3)]]);
  assert.equal(env.missingCalls[0].init.method, "POST");
  assert.equal(env.missingCalls[0].init.headers["X-Api-Token"], "tok");
  assert.equal(env.missingCalls[0].init.headers["X-Machine-Id"], "ext-1");
  assert.deepEqual(env.cdnCalls.map((c) => c.id).sort(), [id(1), id(3)]);  // id(2) Hub đã có → không tải
  for (const c of env.cdnCalls) {
    assert.equal(c.init.headers.Accept, "image/jpeg,image/png");
    assert.equal(c.init.credentials, "omit");
    assert.equal(c.init.method, "GET");
  }
  const puts = Object.fromEntries(env.putCalls.map((c) => [c.id, c.init]));
  assert.equal(puts[id(1)].method, "PUT");
  assert.equal(puts[id(1)].headers["Content-Type"], "image/jpeg");
  assert.equal(puts[id(3)].headers["Content-Type"], "image/png");
  assert.equal(puts[id(3)].headers["X-Api-Token"], "tok");
  assert.deepEqual([...puts[id(3)].body], [...PNG]);

  assert.equal(r.stoppedBy, null);
  assert.equal(r.checked, 3);
  assert.equal(r.present, 1);
  assert.equal(r.pushed, 1);
  assert.equal(r.existed, 1);
  assert.equal(r.failed, 0);
  assert.equal(r.skipped, 2);
  assert.deepEqual(r.queue, { jobs: 0, ids: 0 });
  assert.equal(env.state().jobs.length, 0);
  assert.ok(r.roundId && r.endedAt >= r.startedAt);
});

test("lỗi riêng ảnh (Hub 400 bad-id / empty / corrupt-image, 413, 415; CDN 404 / 410 / WebP lẻ / quá cỡ) → ghi lỗi, đi tiếp, việc xong", async () => {
  const putErr = { [id(1)]: [400, "bad-id"], [id(2)]: [400, "empty"], [id(3)]: [400, "corrupt-image"], [id(4)]: [413, "too-large"], [id(5)]: [415, "unsupported-type"] };
  const env = makeEnv({
    put: (x) => (putErr[x] ? { status: putErr[x][0], json: { error: putErr[x][1] } } : null),
    cdn: {
      [id(6)]: response(404),
      [id(7)]: response(200, { bytes: WEBP }),
      [id(8)]: response(200, { bytes: JPEG, headers: { "content-length": String(LIMITS.maxBytes + 1) } }),
      [id(9)]: response(410),
    },
  });
  const ids = Array.from({ length: 10 }, (_, i) => id(i + 1));
  await env.q.enqueue({ profile: "auto", itemId: "5", ids });
  const r = await env.round();
  assert.equal(r.stoppedBy, null);
  assert.equal(r.failed, 9);
  assert.equal(r.pushed, 1);
  assert.equal(env.state().jobs.length, 0);
  const why = Object.fromEntries(r.errors.map((e) => [e.id, e.reason]));
  for (const code of ["bad-id", "empty", "corrupt-image", "too-large", "unsupported-type"]) {
    assert.ok(Object.values(why).some((w) => w.includes(code)), code);
  }
  assert.match(why[id(6)], /CDN trả HTTP 404/);
  assert.match(why[id(7)], /CDN trả WebP/);
  assert.match(why[id(8)], /quá lớn/);
  assert.match(why[id(9)], /CDN trả HTTP 410/);
  assert.equal(env.putCalls.filter((c) => [id(6), id(7), id(8), id(9)].includes(c.id)).length, 0); // ảnh hỏng không lên Hub
  assert.ok(r.errors.every((e) => e.itemId === "5"));
});

test("CDN chặn (401 / 403 / 407 / 429 / 5xx / mất mạng / 200 trả HTML / 200 rỗng) KHÔNG phải lỗi ảnh: giữ ảnh, không đếm lỗi, ảnh khác vẫn đẩy", async () => {
  for (const spec of [response(401), response(403), response(407), response(429), response(502), new TypeError("proxy chết"),
    response(200, { bytes: new TextEncoder().encode("<html>chặn</html>") }), response(200, { bytes: new Uint8Array() })]) {
    const env = makeEnv({ cdn: { [id(2)]: spec }, limits: { parallel: 1 } });
    await env.q.enqueue({ profile: "auto", itemId: "4", ids: [id(1), id(2), id(3)] });
    const r = await env.round();
    assert.equal(r.stoppedBy, null);
    assert.equal(r.failed, 0);
    assert.equal(r.pushed, 2);
    assert.ok(r.lastError);
    assert.deepEqual(env.state().jobs[0].ids, [id(2)]);
    assert.equal(env.state().jobs[0].attempts, 0);   // lượt không có ảnh nào khác của việc khác → chưa quy lỗi riêng
    assert.ok(!env.putCalls.some((c) => c.id === id(2)));
  }
});

/** Kịch bản phản biện (probe1.mjs): 50 SP × 12 ảnh, CDN chặn toàn bộ. */
const probe = async (cdnSpec, rounds = 1, missing = (ids) => ({ status: 200, json: { missing: ids } })) => {
  const env = makeEnv({ cdn: new Proxy({}, { get: () => cdnSpec }), missing, cost: 50 });
  for (let j = 0; j < 50; j++) {
    await env.q.enqueue({ profile: "auto", itemId: String(j), ids: Array.from({ length: 12 }, (_, n) => `vn-11134207-j${j}i${String(n).padStart(4, "0")}`) });
  }
  const out = [];
  for (let k = 0; k < rounds; k++) { out.push(await env.round()); env.t += 120_000; }
  return { env, out };
};

test("hồi quy phản biện: CDN 403 / 200-HTML / 429 nhiều lượt với 50 SP × 12 ảnh → cầu dao dừng sớm, không xoá việc nào, không tăng attempts", async () => {
  for (const spec of [response(403), response(200, { bytes: new TextEncoder().encode("<html>blocked</html>") })]) {
    const { env, out } = await probe(spec);
    const r = out[0];
    assert.equal(r.stoppedBy, "cdnTemp");
    assert.equal(r.failed, 0);
    assert.ok(env.cdnCalls.length <= LIMITS.cdnTempBreaker + LIMITS.parallel, `cdnCalls=${env.cdnCalls.length}`);
    assert.deepEqual(r.queue, { jobs: 50, ids: 600 });
    assert.equal(env.putCalls.length, 0);
  }
  const { env, out } = await probe(response(429), 7);
  for (const r of out) {
    assert.equal(r.stoppedBy, "cdnTemp");
    assert.equal(r.failed + r.dropped, 0);
    assert.deepEqual(r.queue, { jobs: 50, ids: 600 });
  }
  assert.ok(env.cdnCalls.length <= 7 * (LIMITS.cdnTempBreaker + LIMITS.parallel));
  assert.ok(env.state().jobs.every((j) => j.attempts === 0));
});

test("cầu dao CDN tính cả ảnh của SP khác; SP vấp cầu dao xuống cuối hàng", async () => {
  const bad = response(403);
  const env = makeEnv({
    cdn: { [id(1)]: bad, [id(2)]: bad, [id(3)]: bad, [id(11)]: bad, [id(12)]: bad, [id(13)]: bad },
    limits: { parallel: 1 },
  });
  await env.q.enqueue({ profile: "auto", itemId: "A", ids: [id(1), id(2), id(3)] });
  env.t += 1;
  await env.q.enqueue({ profile: "auto", itemId: "B", ids: [id(11), id(12), id(13)] });
  env.t += 1;
  await env.q.enqueue({ profile: "auto", itemId: "C", ids: [id(21)] });
  const r = await env.round();
  assert.equal(r.stoppedBy, "cdnTemp");
  assert.equal(env.cdnCalls.length, 5);                       // 3 của A + 2 của B
  const order = [...env.state().jobs].sort((a, b) => a.addedAt - b.addedAt).map((j) => j.itemId);
  assert.deepEqual(order, ["A", "C", "B"]);                    // B (vấp cầu dao) xuống cuối
  assert.deepEqual(env.state().jobs.find((j) => j.itemId === "B").ids, [id(11), id(12), id(13)]);
});

test("trần số request CDN mỗi lượt → dừng 'cdnCap', giữ ảnh chưa làm", async () => {
  const env = makeEnv({ limits: { maxCdnPerRound: 4, parallel: 1 } });
  await env.q.enqueue({ profile: "auto", itemId: "1", ids: Array.from({ length: 10 }, (_, i) => id(i + 1)) });
  const r = await env.round();
  assert.equal(r.stoppedBy, "cdnCap");
  assert.equal(env.cdnCalls.length, 4);
  assert.equal(r.pushed, 4);
  assert.equal(env.state().jobs[0].ids.length, 6);
});

test("cầu dao WebP: 5 ảnh WebP liên tiếp → dừng 'cdnWebp', KHÔNG đánh lỗi, giữ cả 12 ảnh", async () => {
  const env = makeEnv({ cdn: new Proxy({}, { get: () => response(200, { bytes: WEBP }) }), limits: { parallel: 1 } });
  await env.q.enqueue({ profile: "auto", itemId: "1", ids: Array.from({ length: 12 }, (_, i) => id(i + 1)) });
  const r = await env.round();
  assert.equal(r.stoppedBy, "cdnWebp");
  assert.equal(r.failed, 0);
  assert.equal(env.cdnCalls.length, LIMITS.webpBreaker);
  assert.equal(env.state().jobs[0].ids.length, 12);
  assert.equal(env.putCalls.length, 0);
});

test("Hub /missing trả 404 / 405 / 415 / 400 lạ → lỗi môi trường: dừng 'temp', giữ việc, không đếm lỗi; 400 bad-id mới bỏ việc", async () => {
  for (const [status, code] of [[404, null], [405, null], [415, null], [400, null]]) {
    const { env, out } = await probe(response(200, { bytes: JPEG }), 1, () => ({ status, json: code ? { error: code } : undefined }));
    assert.equal(out[0].stoppedBy, "temp", String(status));
    assert.equal(out[0].failed, 0);
    assert.deepEqual(out[0].queue, { jobs: 50, ids: 600 });
    assert.equal(env.cdnCalls.length, 0);
  }
  const env = makeEnv({ missing: () => ({ status: 400, json: { error: "bad-id" } }) });
  await env.q.enqueue({ profile: "auto", itemId: "1", ids: [id(1), id(2)] });
  const r = await env.round();
  assert.equal(r.failed, 2);
  assert.equal(env.state().jobs.length, 0);
});

test("lỗi chung (Hub 5xx / mất mạng) KHÔNG tăng attempts, không bỏ việc dù nhiều lượt", async () => {
  const env = makeEnv({ put: () => ({ status: 503, json: { error: "x" } }) });
  await env.q.enqueue({ profile: "auto", itemId: "3", ids: [id(1)] });
  const r = await env.round();
  assert.equal(r.stoppedBy, "temp");
  assert.match(r.lastError, /503/);

  const env2 = makeEnv({ missing: () => new TypeError("Failed to fetch") });
  await env2.q.enqueue({ profile: "auto", itemId: "3", ids: [id(1)] });
  for (let i = 0; i < LIMITS.maxAttempts + 2; i++) {
    const ri = await env2.round();
    assert.equal(ri.stoppedBy, "temp");
    assert.equal(ri.dropped, 0);
  }
  assert.equal(env2.state().jobs[0].attempts, 0);
  assert.equal(env.state().jobs[0].attempts, 0);
});

test("attempts tăng khi việc hỏng RIÊNG (ảnh việc khác vẫn tải được), về 0 khi đẩy được ảnh; tới 5 thì bỏ việc", async () => {
  let blocked = true;
  const env = makeEnv({ cdn: { [id(1)]: () => (blocked ? response(403) : response(200, { bytes: JPEG })) } });
  await env.q.enqueue({ profile: "auto", itemId: "X", ids: [id(1)] });
  const step = async (n) => {
    env.t += 1;
    await env.q.enqueue({ profile: "auto", itemId: `Y${n}`, ids: [id(100 + n)] });   // ảnh việc khác tải được
    return env.round();
  };
  await step(1);
  await step(2);
  const job = () => env.state().jobs.find((j) => j.itemId === "X");
  assert.equal(job().attempts, 2);
  blocked = false;
  await step(3);
  assert.equal(job(), undefined);                // đẩy được → xong việc

  blocked = true;
  await env.q.enqueue({ profile: "auto", itemId: "X", ids: [id(1), id(2)] });
  env.t += 1;
  await step(4);
  assert.equal(job().attempts, 0);               // đẩy được id(2) → attempts về 0, còn id(1)
  let r;
  for (let n = 5; n <= 5 + LIMITS.maxAttempts; n++) r = await step(n);
  assert.equal(job(), undefined);
  assert.equal(r.dropped, 1);
  assert.match(r.errors.map((e) => e.reason).join("|"), /Bỏ ảnh SP X sau 5 lượt/);
});

test("ảnh quá giờ: xuống cuối, ảnh khác đi tiếp; quá giờ 3 lượt liền mới là lỗi riêng ảnh", async () => {
  const hang = (init) => new Promise((_, reject) => {
    init.signal.addEventListener("abort", () => reject(Object.assign(new Error("aborted"), { name: "AbortError" })));
  });
  const env = makeEnv({ cdn: { [id(1)]: hang }, limits: { parallel: 1, imageTimeoutMs: 30 } });
  await env.q.enqueue({ profile: "auto", itemId: "1", ids: [id(1), id(2), id(3)] });
  const r1 = await env.round();
  assert.equal(r1.pushed, 2);
  assert.equal(r1.failed, 0);
  assert.deepEqual(env.state().jobs[0].ids, [id(1)]);
  assert.equal(env.state().jobs[0].timeouts[id(1)], 1);
  await env.round();
  assert.equal(env.state().jobs[0].timeouts[id(1)], 2);
  const r3 = await env.round();
  assert.equal(r3.failed, 1);
  assert.match(r3.errors[0].reason, /quá .* — 3 lượt liền/);
  assert.equal(env.state().jobs.length, 0);
});

test("ảnh quá 2 MB (body không content-length, không có stream) → lỗi riêng ảnh, không PUT", async () => {
  const big = new Uint8Array(LIMITS.maxBytes + 1);
  big.set([0xff, 0xd8, 0xff]);
  const env = makeEnv({ cdn: { [id(1)]: response(200, { bytes: big }) } });
  await env.q.enqueue({ profile: "auto", itemId: "1", ids: [id(1)] });
  const r = await env.round();
  assert.equal(r.failed, 1);
  assert.equal(env.putCalls.length, 0);
});

test("đọc body không content-length: vượt 2 MB thì huỷ stream, lỗi riêng ảnh, không PUT", async () => {
  let pulled = 0;
  const chunk = new Uint8Array(512 * 1024);
  chunk.set([0xff, 0xd8, 0xff]);
  const streamResponse = () => ({
    status: 200,
    headers: { get: () => null },
    body: new ReadableStream({ pull(c) { pulled++; if (pulled > 50) c.close(); else c.enqueue(chunk.slice()); } }),
    arrayBuffer: async () => { throw new Error("không được đọc cả body"); },
  });
  const env = makeEnv({ cdn: { [id(1)]: streamResponse } });
  await env.q.enqueue({ profile: "auto", itemId: "1", ids: [id(1)] });
  const r = await env.round();
  assert.equal(r.failed, 1);
  assert.match(r.errors[0].reason, /quá lớn/);
  assert.equal(env.putCalls.length, 0);
  assert.ok(pulled <= 7, `đọc ${pulled} khúc`);
});

test("clearHubFull() bỏ mốc NGAY kể cả khi đang có lượt chạy; status có hàng đợi theo profile và round.queue giữa lượt", async () => {
  let release;
  const env = makeEnv({
    cdn: { [id(2)]: () => new Promise((res) => { release = () => res(response(200, { bytes: JPEG })); }) },
    limits: { parallel: 1 },
  });
  env.storage.data[STATE_KEY] = { seq: 0, jobs: [], hubFullUntil: env.t + 999_999, hubFullCode: "disk-low", droppedFull: 0 };
  await env.q.enqueue({ profile: "auto", itemId: "1", ids: [id(1)] });
  await env.q.enqueue({ profile: "auto", itemId: "2", ids: [id(2), id(3)] });
  await env.q.enqueue({ profile: "manual", itemId: "9", ids: [id(9)] });
  const p = env.round({ clearHubFull: true });
  for (let i = 0; i < 50 && !release; i++) await new Promise((res) => setTimeout(res, 2));
  const st = await env.q.status();
  assert.equal(st.running, true);
  assert.deepEqual(st.round.queue, { jobs: 1, ids: 2 });       // việc 1 đã xong, giữa lượt không về 0
  assert.deepEqual(st.profiles.auto, { jobs: 1, ids: 2 });
  assert.deepEqual(st.profiles.manual, { jobs: 1, ids: 1 });
  env.storage.data[STATE_KEY].hubFullUntil = env.t + 999_999;  // giả: mốc mới trong lúc lượt chạy
  assert.equal(await env.q.clearHubFull(), true);
  assert.equal(env.state().hubFullUntil, 0);
  assert.equal(await env.q.clearHubFull(), false);
  release();
  const r = await p;
  assert.equal(r.pushed, 3);
});

test("hạn chót: dừng 'deadline', giữ đúng id chưa đẩy, không tăng attempts; lượt sau làm tiếp", async () => {
  const env = makeEnv({ cost: 3_000, limits: { parallel: 1 } });
  await env.q.enqueue({ profile: "auto", itemId: "8", ids: [id(1), id(2), id(3), id(4)] });
  const r = await env.round({ budgetMs: 10_000 });
  assert.equal(r.stoppedBy, "deadline");
  assert.ok(r.pushed >= 1 && r.pushed < 4, `pushed=${r.pushed}`);
  const left = env.state().jobs[0];
  assert.equal(left.ids.length, 4 - r.pushed);
  assert.equal(left.attempts, 0);
  const r2 = await env.round({ budgetMs: 60_000 });
  assert.equal(r2.stoppedBy, null);
  assert.equal(r.pushed + r2.pushed, 4);
});

test("isAborted / abort(): dừng 'abort', giữ việc; abort() huỷ cả request đang bay", async () => {
  const env = makeEnv();
  await env.q.enqueue({ profile: "auto", itemId: "1", ids: [id(1)] });
  const r = await env.round({ isAborted: () => true });
  assert.equal(r.stoppedBy, "abort");
  assert.equal(env.state().jobs.length, 1);

  let cdnCalled = false;
  const env2 = makeEnv({
    cdn: {
      [id(1)]: (init) => new Promise((_, reject) => {
        cdnCalled = true;
        init.signal.addEventListener("abort", () => reject(Object.assign(new Error("aborted"), { name: "AbortError" })));
      }),
    },
    limits: { parallel: 1 },
  });
  await env2.q.enqueue({ profile: "auto", itemId: "1", ids: [id(1)] });
  const p = env2.round();
  await new Promise((res) => setTimeout(res, 20));
  assert.ok(cdnCalled, "CDN đã được gọi");
  const st = await env2.q.status();
  assert.equal(st.running, true);
  assert.equal(st.round.checked, 1);
  assert.equal(env2.q.abort(), true);
  const r2 = await p;
  assert.equal(r2.stoppedBy, "abort");
  assert.equal(env2.state().jobs.length, 1);
  assert.equal((await env2.q.status()).running, false);
  assert.equal(env2.q.abort(), false); // không có lượt nào chạy
});

test("hàng đợi: khử trùng theo profile+itemId (bản mới thay), đầy thì từ chối + đếm dropped (không xoá việc cũ); id rỗng / sai luật không tạo việc", async () => {
  const env = makeEnv({ limits: { maxJobs: 2 } });
  assert.equal((await env.q.enqueue({ profile: "auto", itemId: "1", ids: [id(1)] })).ok, true);
  assert.equal((await env.q.enqueue({ profile: "auto", itemId: "2", ids: [id(2)] })).ok, true);
  assert.equal((await env.q.enqueue({ profile: "auto", itemId: "1", ids: [id(5), id(6)] })).ok, true); // thay, không tính thêm
  const full = await env.q.enqueue({ profile: "auto", itemId: "3", ids: [id(3)] });
  assert.equal(full.ok, false);
  assert.match(full.reason, /đầy/);
  assert.deepEqual(env.state().jobs.map((j) => [j.key, j.ids]), [["auto|2", [id(2)]], ["auto|1", [id(5), id(6)]]]);
  assert.deepEqual(await env.q.enqueue({ profile: "auto", itemId: "4", ids: ["bad", ""] }), { ok: true, empty: true });
  assert.equal(env.state().jobs.length, 2);
  const st = await env.q.status();
  assert.deepEqual(st.queue, { jobs: 2, ids: 3 });
  const r = await env.round();
  assert.equal(r.dropped, 1);   // lần từ chối vì đầy báo ở lượt kế
  assert.equal(r.pushed, 3);
  assert.equal((await env.round()).dropped, 0);
});

test("profile: lượt 'auto' không làm việc của 'manual'; việc cũ trước", async () => {
  const env = makeEnv();
  await env.q.enqueue({ profile: "manual", itemId: "1", ids: [id(1)] });
  env.t += 1;
  await env.q.enqueue({ profile: "auto", itemId: "2", ids: [id(2)] });
  env.t += 1;
  await env.q.enqueue({ profile: "auto", itemId: "3", ids: [id(3)] });
  const r = await env.round();
  assert.equal(r.pushed, 2);
  assert.deepEqual(env.putCalls.map((c) => c.id), [id(2), id(3)]);
  assert.deepEqual(r.queue, { jobs: 0, ids: 0 });
  assert.deepEqual((await env.q.status()).queue, { jobs: 1, ids: 1 });
});

test("cấu hình Hub không dùng được → 'nocfg', không gọi mạng", async () => {
  const env = makeEnv();
  await env.q.enqueue({ profile: "auto", itemId: "1", ids: [id(1)] });
  const r = await env.round({ cfg: { url: "http://hub.example.com", token: "t", machineId: "m" } });
  assert.equal(r.stoppedBy, "nocfg");
  assert.match(r.lastError, /http/);
  assert.equal(env.calls.length, 0);
  assert.equal((await env.round({ cfg: null })).stoppedBy, "nocfg");
});

test("missing: chia ≤ 200 id / lần; Hub 400 → bỏ việc (lỗi dữ liệu), đếm lỗi", async () => {
  const many = Array.from({ length: 450 }, (_, i) => id(i + 1));
  const env = makeEnv({ missing: () => ({ status: 200, json: { missing: [] } }) });
  await env.q.enqueue({ profile: "auto", itemId: "1", ids: many });
  const r = await env.round();
  assert.deepEqual(env.missingCalls.map((c) => c.ids.length), [200, 200, 50]);
  assert.equal(r.present, 450);
  assert.equal(r.pushed, 0);
  assert.equal(env.state().jobs.length, 0);

  const env2 = makeEnv({ missing: () => ({ status: 400, json: { error: "too-many-ids" } }) });
  await env2.q.enqueue({ profile: "auto", itemId: "1", ids: [id(1), id(2)] });
  const r2 = await env2.round();
  assert.equal(r2.failed, 2);
  assert.match(r2.lastError, /too-many-ids/);
  assert.equal(env2.state().jobs.length, 0);
});

test("tải song song tối đa 2 ảnh", async () => {
  let active = 0;
  let peak = 0;
  const slow = async () => {
    active++;
    peak = Math.max(peak, active);
    await new Promise((res) => setTimeout(res, 5));
    active--;
    return response(200, { bytes: JPEG });
  };
  const ids = Array.from({ length: 6 }, (_, i) => id(i + 1));
  const env = makeEnv({ cdn: Object.fromEntries(ids.map((x) => [x, slow])) });
  await env.q.enqueue({ profile: "auto", itemId: "1", ids });
  const r = await env.round();
  assert.equal(r.pushed, 6);
  assert.equal(peak, 2);
});
