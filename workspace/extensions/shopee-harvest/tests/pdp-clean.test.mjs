// Đối chiếu bản JS (pdp-clean.js) với bản C# (HarvestPayloadCleaner + ShopeePdpParser): cùng file kỳ vọng
// tests/Shopee.Core.Tests/Fixtures/pdp-clean/expected.json do bản C# sinh — test C# PdpCleanGoldenTests kiểm bản C# vẫn ra
// đúng file đó, test này kiểm bản JS ra đúng từng ký tự / hash / cột tóm tắt. Hai bên cùng khớp một file = khớp nhau.
// Chạy: node --test extensions/shopee-harvest/tests/*.test.mjs (từ workspace/).
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import {
  cleanPayload, rawHash, summarize, buildHubItem, toJson, stripInvalidText, HarvestRejectedError,
} from "../pdp-clean.js";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const FIX = path.resolve(HERE, "../../../tests/Shopee.Core.Tests/Fixtures");
const expected = JSON.parse(fs.readFileSync(path.join(FIX, "pdp-clean", "expected.json"), "utf8"));
const cases = JSON.parse(fs.readFileSync(path.join(FIX, "pdp-clean", "cases.json"), "utf8"));

/** Mở rộng ký hiệu trong cases.json — PHẢI giống hàm Expand của test C#. */
const expand = (c) => {
  let s = c.input;
  if (c.depth != null) s = s.replace("@@NEST@@", "[".repeat(c.depth) + "]".repeat(c.depth));
  return s.replace(/\{\{U\+([0-9A-F]{4})\}\}/g, (_, h) => String.fromCharCode(parseInt(h, 16)));
};

const inputs = new Map();
for (const f of fs.readdirSync(path.join(FIX, "pdp")).filter((x) => x.endsWith(".json"))) {
  inputs.set(`fixture:${f}`, fs.readFileSync(path.join(FIX, "pdp", f), "utf8"));
}
for (const c of cases) inputs.set(c.name, expand(c));

const summaryText = (s) => ({
  itemId: s.itemId.toString(),
  shopId: s.shopId.toString(),
  name: s.name,
  catId: s.catId === null ? null : s.catId.toString(),
  categoryIds: s.categoryIds.map(String),
  brandId: s.brandId === null ? null : s.brandId.toString(),
  priceMin: s.priceMin === null ? null : s.priceMin.toString(),
  priceMax: s.priceMax === null ? null : s.priceMax.toString(),
  priceBeforeDiscount: s.priceBeforeDiscount === null ? null : s.priceBeforeDiscount.toString(),
  tierCount: s.tierCount,
  modelCount: s.modelCount,
  imageUrls: s.imageUrls,
  videoUrls: s.videoUrls,
  sizeChartUrl: s.sizeChartUrl,
});

test("file kỳ vọng phủ đủ 7 fixture thật + mọi ca tổng hợp (chống xanh giả khi thiếu file)", () => {
  assert.equal(expected.filter((e) => e.name.startsWith("fixture:")).length, 7);
  assert.equal(expected.length, inputs.size);
  for (const e of expected) assert.ok(inputs.has(e.name), e.name);
});

for (const e of expected) {
  test(`JS khớp C#: ${e.name}`, async () => {
    const input = inputs.get(e.name);
    let clean;
    try {
      clean = cleanPayload(input);
    } catch (err) {
      assert.ok(err instanceof HarvestRejectedError, `lỗi lạ: ${err}`);
      assert.equal(e.rejected, true, `JS từ chối mà C# nhận: ${err.message}`);
      return;
    }
    assert.equal(e.rejected, false, "C# từ chối mà JS nhận");
    if (e.clean !== undefined) assert.equal(clean, e.clean);
    assert.equal(await rawHash(clean), e.hash);
    if (e.summaryRejected) {
      assert.throws(() => summarize(clean), HarvestRejectedError);
    } else {
      assert.deepEqual(summaryText(summarize(clean)), e.summary);
    }
  });
}

test("JSON sạch không còn khối riêng tư (account rỗng, không delivery_address / shop_vouchers) trên 7 fixture", () => {
  for (const [name, input] of inputs) {
    if (!name.startsWith("fixture:")) continue;
    const data = JSON.parse(cleanPayload(input)).data;
    assert.deepEqual(data.account, {}, name);
    assert.equal(JSON.stringify(data.product_shipping ?? {}).includes("delivery_address"), false, name);
    assert.equal("shop_vouchers" in data, false, name);
  }
});

test("buildHubItem: bấm tay không có nguồn; đủ bộ ba thì có; thiếu một trường thì bỏ cả ba; link gốc quá dài bị bỏ", () => {
  const clean = cleanPayload(inputs.get("fixture:pdp-2tang-8model.json"));
  const summary = summarize(clean);
  const base = { clean, hash: "h", summary, url: "https://shopee.vn/product/1/2", harvestedAtMs: Date.UTC(2026, 10, 18, 1, 2, 3) };

  const manual = buildHubItem(base);
  assert.equal(manual.accountId, null);
  assert.equal(manual.sheet, null);
  assert.equal(manual.rowNo, null);
  assert.equal(manual.sourceUrl, null);
  assert.equal(manual.harvestedAt, "2026-11-18T01:02:03.000Z");
  assert.equal(manual.rawJson, clean);

  const auto = buildHubItem({ ...base, accountId: "harvest", sheet: "Shop A", rowNo: 7, sourceUrl: "https://shopee.vn/x-i.1.2?sp_atk=1" });
  assert.equal(auto.accountId, "harvest");
  assert.equal(auto.sheet, "Shop A");
  assert.equal(auto.rowNo, 7);
  assert.equal(auto.sourceUrl, "https://shopee.vn/x-i.1.2?sp_atk=1");

  const partial = buildHubItem({ ...base, accountId: "harvest", sheet: " ", rowNo: 7 });
  assert.deepEqual([partial.accountId, partial.sheet, partial.rowNo], [null, null, null]);

  const longSrc = buildHubItem({ ...base, accountId: "harvest", sheet: "S", rowNo: 1, sourceUrl: "https://shopee.vn/" + "a".repeat(2100) });
  assert.equal(longSrc.sourceUrl, null);
  assert.equal(longSrc.accountId, "harvest");
});

test("toJson: BigInt ghi đúng từng chữ số (id > 2^53), bỏ undefined, chuỗi escape như JSON.stringify", () => {
  assert.equal(toJson({ a: 9007199254740993n, b: [1n, null], c: "x\"y", d: undefined, e: true }),
    '{"a":9007199254740993,"b":[1,null],"c":"x\\"y","e":true}');
});

test("stripInvalidText: bỏ \\0 và surrogate lẻ, giữ cặp hợp lệ", () => {
  assert.equal(stripInvalidText("a\u0000b\ud800c\udc00d😀"), "abcd😀");
  assert.equal(stripInvalidText(null), "");
});
