// Test logic thuần của pdp-core.js. Chạy: node --test extensions/shopee-harvest/tests/*.test.mjs (từ workspace/).
import test from "node:test";
import assert from "node:assert/strict";
import { extractProductIds, checkPdpBody, redactPrivate } from "../pdp-core.js";

const pdp = (item, extra = {}) => JSON.stringify({ error: null, error_msg: null, data: { item, ...extra } });

test("extractProductIds: đủ các dạng link như ShopeeApiDataHelper.TryExtractShopeeItemId", () => {
  const cases = [
    ["https://shopee.vn/Ao-thun-nu-i.123456.987654321", { shopId: "123456", itemId: "987654321" }],
    ["https://shopee.vn/product/123456/987654321", { shopId: "123456", itemId: "987654321" }],
    ["https://shopee.vn/product/123456/987654321?sp_atk=abc", { shopId: "123456", itemId: "987654321" }],
    ["https://shopee.vn/x?itemid=55&shopid=66", { shopId: "66", itemId: "55" }],
    ["https://shopee.vn/x?shop_id=66&item_id=55", { shopId: "66", itemId: "55" }],
    ["https://shopee.vn/x?itemId=55", { shopId: null, itemId: "55" }],
    ["https://shopee.vn/pdp/item/777", { shopId: null, itemId: "777" }],
    ["https://shopee.vn/pdp/778", { shopId: null, itemId: "778" }],
    ["https://shopee.vn/abc/12345678901", { shopId: null, itemId: "12345678901" }],
  ];
  for (const [url, want] of cases) assert.deepEqual(extractProductIds(url), want, url);
});

test("extractProductIds: neo -i. — slug kiểu 'Wi.2.4G' không ra sai itemId", () => {
  // 'Wi.2.4G' trong tên SP (W ngay trước i) KHÔNG được đọc thành shopId=2/itemId=4.
  assert.deepEqual(extractProductIds("https://shopee.vn/Router-Wi.2.4G-Chuan-i.123456.987654321"),
    { shopId: "123456", itemId: "987654321" });
  assert.equal(extractProductIds("https://shopee.vn/Bang-gia-Wi.2.4G-Loai-tot"), null);
  // Vẫn nhận SEO hợp lệ (đầu path / sau '-' / sau '/').
  assert.deepEqual(extractProductIds("https://shopee.vn/i.1.2"), { shopId: "1", itemId: "2" });
  assert.deepEqual(extractProductIds("https://shopee.vn/Ten-SP-i.9.8"), { shopId: "9", itemId: "8" });
  // /product/<shop>/<item> giữ nguyên.
  assert.deepEqual(extractProductIds("https://shopee.vn/product/123/456"), { shopId: "123", itemId: "456" });
});

test("extractProductIds: neo CẢ ĐUÔI — 'Wifi-i.2.4G-Router-i.123.456' → 123/456; các dạng thật vẫn đúng", () => {
  assert.deepEqual(extractProductIds("https://shopee.vn/Wifi-i.2.4G-Router-i.123.456"), { shopId: "123", itemId: "456" });
  const ok = { shopId: "11", itemId: "22" };
  for (const u of [
    "https://shopee.vn/%C3%81o-thun-i.11.22?extraParams=%7B%22x%22%3A1%7D",
    "https://shopee.vn/Ao-i.11.22?sp_atk=abc",
    "https://shopee.vn/Ao-i.11.22#reviews",
    "https://shopee.vn/Ao-i.11.22/",
    "https://shopee.vn/i.11.22",
    "https://shopee.vn/product/11/22",
  ]) assert.deepEqual(extractProductIds(u), ok, u);
  // Đuôi lạ dính ngay sau itemId (vd "%7DD" trong sổ) KHÔNG còn khớp dạng SEO — launcher nên gửi link sạch.
  assert.equal(extractProductIds("https://shopee.vn/Ao-i.1.2%7DD"), null);
});

test("extractProductIds: không nhận ra → null", () => {
  for (const url of ["", "   ", null, undefined, 42, "https://shopee.vn/", "https://shopee.vn/search?keyword=ao", "https://shopee.vn/x/12345"]) {
    assert.equal(extractProductIds(url), null, String(url));
  }
});

test("checkPdpBody: hợp lệ khi error null/vắng/0 và có data.item.item_id", () => {
  assert.deepEqual(checkPdpBody(pdp({ item_id: 987654321, shop_id: 123456 })), { ok: true, itemId: "987654321", shopId: "123456" });
  const noErrorKey = JSON.stringify({ data: { item: { item_id: 5 } } });
  assert.deepEqual(checkPdpBody(noErrorKey), { ok: true, itemId: "5", shopId: null });
  assert.equal(checkPdpBody(pdp({ item_id: "77" })).ok, true);
  // error:0 = thành công (Shopee hay trả 0 thay cho null).
  assert.equal(checkPdpBody(JSON.stringify({ error: 0, data: { item: { item_id: 9 } } })).ok, true);
  assert.equal(checkPdpBody(JSON.stringify({ error: 0, data: { item: { item_id: 9 } } })).itemId, "9");
});

test("checkPdpBody: itemId phải khớp khi truyền expected", () => {
  const body = pdp({ item_id: 987654321, shop_id: 1 });
  assert.equal(checkPdpBody(body, "987654321").ok, true);
  assert.equal(checkPdpBody(body, 987654321).ok, true);
  const bad = checkPdpBody(body, "111");
  assert.equal(bad.ok, false);
  assert.match(bad.reason, /item-mismatch/);
});

test("checkPdpBody: từ chối body hỏng / lỗi Shopee / thiếu item_id", () => {
  const bad = [
    "",
    null,
    "<html>blocked</html>",
    "null",
    "123",
    JSON.stringify({ error: 90309999, error_msg: "x", data: null }),
    JSON.stringify({ error: "something", data: { item: { item_id: 1 } } }), // error chuỗi khác null/0
    JSON.stringify({ error: null, data: null }),
    JSON.stringify({ error: null, data: { item: {} } }),
    JSON.stringify({ error: null, data: { item: { item_id: "abc" } } }),
    JSON.stringify({ error: null, data: { item: { item_id: 1.5 } } }),
  ];
  for (const b of bad) assert.equal(checkPdpBody(b).ok, false, String(b));
});

test("redactPrivate: chỉ thay data.account, phần còn lại giữ nguyên từng byte", () => {
  // Số > 2^53 để chắc không đi qua parse/stringify (sẽ bị làm tròn).
  const raw = '{"error":null,"data":{"item":{"item_id":12345678901234567890,"title":"a\\"}{b"},' +
    '"account":{"userid":42,"email":"x@y.z","nested":{"k":[1,{"account":"inner"}]}},"shop_detailed":{"shopid":1}}}';
  const out = redactPrivate(raw);
  assert.equal(out,
    '{"error":null,"data":{"item":{"item_id":12345678901234567890,"title":"a\\"}{b"},' +
    '"account":{},"shop_detailed":{"shopid":1}}}');
});

test("redactPrivate: account null (đã là null) / có khoảng trắng / không có account NGAY trong data", () => {
  // account = null: valueAtPath là null (khác undefined) nên vẫn "có" → thay thành {} (kiểm lại: {} là rỗng, đạt).
  assert.equal(redactPrivate('{"data": { "account" : null , "x":1 } }'), '{"data": { "account" : {} , "x":1 } }');
  const noAccount = '{"data":{"item":{"account":{"a":1}}},"account":{"b":2}}'; // chỉ khóa NGAY trong data mới bị thay
  assert.equal(redactPrivate(noAccount), noAccount);
});

test("redactPrivate: ĐÓNG AN TOÀN — JSON hỏng/rỗng NÉM lỗi, KHÔNG trả body chưa cắt", () => {
  assert.throws(() => redactPrivate('{"data":{"account":{"a":1}'), /redact-failed/); // JSON cụt
  assert.throws(() => redactPrivate("not json"), /redact-failed/);
  assert.throws(() => redactPrivate(""), /redact-failed/);
  assert.throws(() => redactPrivate(null), /redact-failed/);
  // Kiểm lại sau khi cắt: rule có `value` KHÔNG phải JSON → output hỏng → phải NÉM (không trả output hỏng).
  const brokenRule = [{ path: ["data", "account"], value: "BROKEN" }];
  assert.throws(() => redactPrivate('{"data":{"account":{"a":1}}}', brokenRule), /redact-failed/);
});

test("redactPrivate: khoá TRÙNG — cắt MỌI span khớp path, không dừng ở span đầu (ca phản biện)", () => {
  // Hai product_shipping trong data: parse chỉ giữ cái cuối (null) — nếu precheck theo parse sẽ BỎ SÓT địa chỉ
  // trong cái đầu. Phải cắt cái đầu.
  const c1 = '{"data":{"product_shipping":{"delivery_address":{"a":1}},"item":{"item_id":1},"product_shipping":null}}';
  assert.equal(redactPrivate(c1),
    '{"data":{"product_shipping":{"delivery_address":null},"item":{"item_id":1},"product_shipping":null}}');
  // Hai data: account nằm trong cái đầu (parse giữ cái sau, không có account) — vẫn phải cắt.
  const c2 = '{"data":{"account":{"secret":1},"x":2},"data":{"item":{"item_id":1}}}';
  assert.equal(redactPrivate(c2), '{"data":{"account":{},"x":2},"data":{"item":{"item_id":1}}}');
  // HAI span THẬT cùng path (account trong CẢ HAI data) → phải cắt CẢ HAI (không dừng ở span đầu).
  const c3 = '{"data":{"account":{"a":1}},"data":{"account":{"b":2},"item":{"item_id":1}}}';
  assert.equal(redactPrivate(c3), '{"data":{"account":{}},"data":{"account":{},"item":{"item_id":1}}}');
});

test("redactPrivate: khoá viết bằng mã thoát unicode (\\u0061ccount, \\u0064ata) vẫn bị cắt", () => {
  const BS = "\\";
  const c1 = `{"data":{"${BS}u0061ccount":{"secret":1},"item":{"item_id":1}}}`;
  const o1 = redactPrivate(c1);
  assert.equal(/secret/.test(o1), false, o1);
  assert.deepEqual(JSON.parse(o1).data.account, {});
  const c2 = `{"${BS}u0064ata":{"account":{"secret":2},"product_shipping":{"delivery_address":{"lat":1}}}}`;
  const o2 = redactPrivate(c2);
  assert.equal(/secret|lat/.test(o2), false, o2);
});

test("redactPrivate: vòng KIỂM LẠI bắt được rule tự sinh lại khối riêng tư (lưới an toàn còn sống)", () => {
  // Rule A thay product_shipping (đang là số → B không có span ở body gốc) bằng object CHỨA delivery_address.
  // Sau khi cắt, vòng kiểm lại thấy delivery_address có dữ liệu → phải NÉM. Bỏ vòng kiểm lại → trả output rò.
  const rules = [
    { path: ["data", "product_shipping"], value: '{"delivery_address":{"leak":1}}' },
    { path: ["data", "product_shipping", "delivery_address"], value: "null" },
  ];
  assert.throws(() => redactPrivate('{"data":{"product_shipping":1}}', rules), /redact-failed/);
});

test("redactPrivate: body thật (fixture đã cắt) chạy lại vẫn hợp lệ, account {} và delivery_address null", async () => {
  const fs = await import("node:fs");
  const path = await import("node:path");
  const url = await import("node:url");
  const dir = path.resolve(path.dirname(url.fileURLToPath(import.meta.url)), "../../../tests/Shopee.Core.Tests/Fixtures/pdp");
  const files = fs.readdirSync(dir).filter((f) => f.endsWith(".json"));
  assert.ok(files.length >= 3, `cần ≥3 fixture, thấy ${files.length}`);
  for (const f of files) {
    const raw = fs.readFileSync(path.join(dir, f), "utf8");
    const out = redactPrivate(raw); // không được ném
    const j = JSON.parse(out);
    assert.deepEqual(j.data.account, {}, f);
    const addr = j.data.product_shipping?.delivery_address;
    assert.ok(addr === null || addr === undefined, `${f}: delivery_address phải null/vắng`);
  }
});

test("redactPrivate: cắt data.product_shipping.delivery_address → null, cùng lúc với data.account", () => {
  const raw = '{"error":null,"data":{"account":{"userid":42},"item":{"item_id":12345678901234567890},' +
    '"product_shipping":{"free_shipping":null,"delivery_address":{"state":"Hà Nội","city":"Quận \\"X\\"",' +
    '"longitude":105.34123456789012345,"latitude":21.06},"ship_from":"Hà Nội"}}}';
  assert.equal(redactPrivate(raw),
    '{"error":null,"data":{"account":{},"item":{"item_id":12345678901234567890},' +
    '"product_shipping":{"free_shipping":null,"delivery_address":null,"ship_from":"Hà Nội"}}}');
});

test("redactPrivate: không có delivery_address / product_shipping null → chỉ cắt account, còn lại giữ nguyên", () => {
  const noAddr = '{"data":{"product_shipping":{"ship_from":"HCM"},"account":{"a":1}}}';
  assert.equal(redactPrivate(noAddr), '{"data":{"product_shipping":{"ship_from":"HCM"},"account":{}}}');
  const shippingNull = '{"data":{"product_shipping":null,"account":{"a":1}}}';
  assert.equal(redactPrivate(shippingNull), '{"data":{"product_shipping":null,"account":{}}}');
  const neither = '{"data":{"product_shipping":null,"item":{"item_id":1}}}';
  assert.equal(redactPrivate(neither), neither);
  // delivery_address ở chỗ khác (không đúng đường dẫn) thì không đụng.
  const elsewhere = '{"data":{"item":{"delivery_address":{"x":1}}},"delivery_address":{"y":2}}';
  assert.equal(redactPrivate(elsewhere), elsewhere);
});
