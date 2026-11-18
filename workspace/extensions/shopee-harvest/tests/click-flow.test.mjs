// GĐ7: pushCaptured (làm sạch → mục Hub → gửi → hàng đợi) + outcomeOf (trạng thái hộp nổi) — fetch / hàng đợi giả,
// JSON là fixture get_pc THẬT (có dữ liệu riêng tư chèn thêm để soi việc cắt).
// Chạy: node --test extensions/shopee-harvest/tests/*.test.mjs (từ workspace/).
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { pushCaptured, outcomeOf, HUB_STATUS, canonicalProductUrl } from "../click-flow.js";
import { cleanPayload, rawHash } from "../pdp-clean.js";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const FIXTURE = fs.readFileSync(path.resolve(HERE, "../../../tests/Shopee.Core.Tests/Fixtures/pdp/pdp-2tang-8model.json"), "utf8");
const ITEM_ID = String(JSON.parse(FIXTURE).data.item.item_id);
const SHOP_ID = String(JSON.parse(FIXTURE).data.item.shop_id);
// Chèn khối riêng tư vào JSON (fixture đã cắt sẵn) — bản gửi Hub phải không còn.
const PRIVATE_JSON = FIXTURE.replace('"account":{}', '"account":{"userid":99,"email":"a@b.c"}')
  .replace('"delivery_address":null', '"delivery_address":{"state":"Hà Nội","latitude":21.06}');

const CFG = { url: "https://hub.example.com", token: "tok", machineId: "ext-1" };
assert.ok(PRIVATE_JSON.includes("21.06") && PRIVATE_JSON.includes("a@b.c"), "fixture đổi dạng — chèn dữ liệu riêng tư không được");
const step = (over = {}) => ({ ok: true, harvestOk: true, itemId: ITEM_ID, shopId: SHOP_ID, json: PRIVATE_JSON, capturedAt: Date.UTC(2026, 10, 18), tabId: 7, ...over });

const makeDeps = ({ status = 200, results, throws = false, manual = CFG, permission = true, enqueueOk = true } = {}) => {
  const posts = [];
  const queued = [];
  return {
    posts,
    queued,
    deps: {
      manualConfig: async () => manual,
      hasPermission: async () => permission,
      now: () => Date.UTC(2026, 10, 18, 5),
      queue: { enqueue: async (x) => { queued.push(x); return enqueueOk ? { ok: true, id: 1 } : { ok: false, reason: "Hàng đợi gửi Hub đã đầy" }; } },
      fetchImpl: async (url, init) => {
        posts.push({ url, body: JSON.parse(init.body) });
        if (throws) throw new TypeError("Failed to fetch");
        return { status, json: async () => ({ results: results ?? [{ index: 0, itemId: Number(ITEM_ID), ok: true, applied: true }] }) };
      },
    },
  };
};

test("bấm tay: làm sạch (cắt riêng tư), gửi Hub KHÔNG nguồn, link sạch /product, raw_hash = SHA-256 chuỗi sạch → saved", async () => {
  const { deps, posts } = makeDeps();
  const r = await pushCaptured(step(), null, deps);
  assert.equal(r.hub.status, HUB_STATUS.saved);
  assert.equal(r.hub.applied, true);
  const item = posts[0].body.items[0];
  assert.equal(posts[0].body.machineId, "ext-1");
  assert.equal(item.accountId, null);
  assert.equal(item.sheet, null);
  assert.equal(item.rowNo, null);
  assert.equal(item.sourceUrl, null);
  assert.equal(item.url, `https://shopee.vn/product/${SHOP_ID}/${ITEM_ID}`);
  assert.equal(String(item.itemId), ITEM_ID);
  assert.equal(item.harvestedAt, "2026-11-18T00:00:00.000Z");
  const clean = cleanPayload(PRIVATE_JSON);
  assert.equal(item.rawJson, clean);
  assert.equal(item.rawHash, await rawHash(clean));
  assert.equal(item.rawJson.includes("delivery_address"), false);
  assert.equal(item.rawJson.includes("a@b.c"), false);
  assert.equal(r.json, clean); // kết quả trả app/hộp là JSON SẠCH
  assert.ok(item.imageUrls.length >= 1);
  assert.equal(item.modelCount, 8);
});

test("tự động: mục mang nguồn của app (harvest / sheet / dòng / link gốc) và link sạch do app truyền", async () => {
  const { deps, posts } = makeDeps();
  const ctx = {
    auto: true, hub: { url: "http://localhost:8088", token: "app", machineId: "MAY-1" },
    accountId: "harvest", sheet: "Shop A", rowNo: 12, url: "https://shopee.vn/product/1/2", sourceUrl: "https://shopee.vn/x-i.1.2?sp_atk=1",
  };
  const r = await pushCaptured(step(), ctx, deps);
  assert.equal(r.hub.status, "saved");
  assert.equal(posts[0].url, "http://localhost:8088/api/harvested-products/batch");
  assert.equal(posts[0].body.machineId, "MAY-1");
  const item = posts[0].body.items[0];
  assert.deepEqual([item.accountId, item.sheet, item.rowNo], ["harvest", "Shop A", 12]);
  assert.equal(item.url, "https://shopee.vn/product/1/2");
  assert.equal(item.sourceUrl, "https://shopee.vn/x-i.1.2?sp_atk=1");
});

test("bước không lấy được JSON → trả nguyên, không gửi gì", async () => {
  const { deps, posts } = makeDeps();
  const s = { ok: false, notProduct: true, json: null };
  assert.equal(await pushCaptured(s, null, deps), s);
  assert.equal(posts.length, 0);
});

test("JSON không xử lý được / JSON của SP khác → invalid (không gửi)", async () => {
  const { deps, posts } = makeDeps();
  assert.equal((await pushCaptured(step({ json: '{"data":{"item":{"item_id":1},"item":{}}}', itemId: "1" }), null, deps)).hub.status, "invalid");
  const lech = await pushCaptured(step({ itemId: "123" }), null, deps);
  assert.equal(lech.hub.status, "invalid");
  assert.match(lech.hub.reason, /không phải SP 123/);
  assert.equal(posts.length, 0);
});

test("chưa cấu hình / thiếu quyền host → failed rõ lý do, không gửi, không vào hàng đợi", async () => {
  const a = makeDeps({ manual: null });
  const r1 = await pushCaptured(step(), null, a.deps);
  assert.equal(r1.hub.status, "failed");
  assert.match(r1.hub.reason, /Chưa cấu hình Hub/);
  const b = makeDeps({ permission: false });
  const r2 = await pushCaptured(step(), null, b.deps);
  assert.equal(r2.hub.status, "failed");
  assert.match(r2.hub.reason, /quyền truy cập https:\/\/hub\.example\.com/);
  assert.equal(a.posts.length + b.posts.length, 0);
  assert.equal(a.queued.length + b.queued.length, 0);
});

test("bấm tay: Hub từ chối mục (ok=false) / 4xx ngay lần đầu → rejected + giữ trong hàng đợi ở trạng thái LỖI kèm lý do", async () => {
  const a = makeDeps({ results: [{ index: 0, itemId: 1, ok: false, applied: false, reason: "raw-error" }] });
  const r1 = await pushCaptured(step(), null, a.deps);
  assert.equal(r1.hub.status, "rejected");
  assert.match(r1.hub.reason, /^raw-error — đã giữ trong hàng đợi/);
  assert.equal(a.queued.length, 1);
  assert.equal(a.queued[0].status, "error");
  assert.equal(a.queued[0].lastError, "Hub từ chối: raw-error");
  assert.equal(JSON.parse(a.queued[0].body).itemId.toString(), ITEM_ID);
  const b = makeDeps({ status: 400 });
  const r2 = await pushCaptured(step(), null, b.deps);
  assert.equal(r2.hub.status, "rejected");
  assert.equal(b.queued[0].status, "error");
  assert.match(b.queued[0].lastError, /HTTP 400/);
});

test("tự động: Hub từ chối → rejected, KHÔNG vào hàng đợi (app tự lấy lại lượt sau)", async () => {
  const a = makeDeps({ results: [{ index: 0, itemId: 1, ok: false, applied: false, reason: "raw-error" }] });
  const r = await pushCaptured(step(), { auto: true, hub: CFG, accountId: "harvest", sheet: "S", rowNo: 1 }, a.deps);
  assert.equal(r.hub.status, "rejected");
  assert.equal(r.hub.reason, "raw-error");
  assert.equal(a.queued.length, 0);
});

test("Hub của app là http://IP LAN → failed nói ĐÚNG lý do (không gửi token qua http ngoài localhost)", async () => {
  const { deps, posts } = makeDeps();
  const r = await pushCaptured(step(), { auto: true, hub: { url: "http://192.168.1.20:8088", token: "t", machineId: "M" } }, deps);
  assert.equal(r.hub.status, "failed");
  assert.match(r.hub.reason, /http:\/\/192\.168\.1\.20:8088 \(http, không phải localhost\).*https hoặc http:\/\/localhost/);
  assert.doesNotMatch(r.hub.reason, /không truyền đủ/);
  assert.equal(posts.length, 0);
});

test("JSON là SP của shop khác shop trên URL trang đang xem → rejected (không gửi, không vào hàng đợi); URL không có shop thì bỏ qua", async () => {
  const a = makeDeps();
  const r = await pushCaptured(step({ pageUrl: `https://shopee.vn/Ao-i.999.${ITEM_ID}` }), null, a.deps);
  assert.equal(r.hub.status, "rejected");
  assert.match(r.hub.reason, new RegExp(`shop ${SHOP_ID}, trang đang xem là shop 999`));
  assert.equal(a.posts.length + a.queued.length, 0);
  const b = makeDeps();
  assert.equal((await pushCaptured(step({ pageUrl: `https://shopee.vn/Ao-i.${SHOP_ID}.${ITEM_ID}` }), null, b.deps)).hub.status, "saved");
  const c = makeDeps();
  assert.equal((await pushCaptured(step({ pageUrl: `https://shopee.vn/x?itemid=${ITEM_ID}` }), null, c.deps)).hub.status, "saved");
});

test("mất mạng / 503 → vào hàng đợi (queued); hàng đợi đầy → failed (không mất âm thầm)", async () => {
  const a = makeDeps({ throws: true });
  const r1 = await pushCaptured(step(), null, a.deps);
  assert.equal(r1.hub.status, "queued");
  assert.equal(a.queued.length, 1);
  assert.equal(a.queued[0].profile, "manual");
  assert.equal(a.queued[0].key, `manual|${ITEM_ID}||`);
  assert.equal(JSON.parse(a.queued[0].body).rawJson, cleanPayload(PRIVATE_JSON));

  const b = makeDeps({ status: 503 });
  const ctx = { auto: true, hub: CFG, accountId: "harvest", sheet: "S", rowNo: 3 };
  const r2 = await pushCaptured(step(), ctx, b.deps);
  assert.equal(r2.hub.status, "queued");
  assert.equal(b.queued[0].profile, "auto");
  assert.equal(b.queued[0].key, `auto|${ITEM_ID}|harvest|S`);

  const c = makeDeps({ status: 503, enqueueOk: false });
  const r3 = await pushCaptured(step(), null, c.deps);
  assert.equal(r3.hub.status, "failed");
  assert.match(r3.hub.reason, /đầy/);
});

test("token sai: bấm tay → giữ trong hàng đợi (sửa token rồi Gửi lại); tự động → failed (app dừng job), KHÔNG dồn hàng đợi", async () => {
  const a = makeDeps({ status: 401 });
  assert.equal((await pushCaptured(step(), null, a.deps)).hub.status, "queued");
  const b = makeDeps({ status: 401 });
  const r = await pushCaptured(step(), { auto: true, hub: CFG, accountId: "harvest", sheet: "S", rowNo: 1 }, b.deps);
  assert.equal(r.hub.status, "failed");
  assert.equal(b.queued.length, 0);
});

test("outcomeOf: thứ tự ưu tiên + chữ trạng thái cho hộp", () => {
  assert.deepEqual(outcomeOf({ ok: true, hub: { status: "saved", applied: false } }), { status: "saved", reason: null, applied: false });
  assert.equal(outcomeOf({ superseded: true, aborted: true }).status, "error");
  assert.equal(outcomeOf({ loginRequired: true, notProduct: false }).status, "loginRequired");
  assert.equal(outcomeOf({ captcha: true, ok: false }).status, "captcha");
  assert.equal(outcomeOf({ trafficError: true, pdpError: 90309999 }).reason, "error=90309999");
  assert.equal(outcomeOf({ proxyError: true }).status, "proxy");
  assert.equal(outcomeOf({ notProduct: true }).status, "notProduct");
  assert.equal(outcomeOf({ pdpError: 4 }).status, "pdpError");
  assert.equal(outcomeOf({ ok: false, message: "Tab đã đóng" }).reason, "Tab đã đóng");
  assert.equal(outcomeOf(null).status, "error");
});

test("canonicalProductUrl: https://shopee.vn/product/{shop}/{item}", () => {
  assert.equal(canonicalProductUrl({ shopId: 5n, itemId: 6n }), "https://shopee.vn/product/5/6");
});
