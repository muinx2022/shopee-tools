// Làm sạch JSON get_pc + rút cột tóm tắt + dựng mục gửi Hub — bản JS của
// src/Shopee.Core/Harvest/HarvestPayloadCleaner.cs và ShopeePdpParser.cs (GĐ7: extension tự đẩy Hub).
//
// PHẢI ra GIỐNG HỆT bản C#: cùng chuỗi JSON sạch (từng ký tự), cùng raw_hash, cùng cột tóm tắt — Hub coi hash trùng là
// "SP không đổi", nên hai client lệch nhau thì cùng một SP cứ bị ghi đè qua lại. Bảo đảm bằng test đối chiếu
// (tests/pdp-clean.test.mjs) trên file chung tests/Shopee.Core.Tests/Fixtures/pdp-clean/expected.json do bản C# sinh ra,
// và test C# PdpCleanGoldenTests kiểm bản C# vẫn ra đúng file đó. Sửa luật ở bên nào thì sửa cả hai rồi sinh lại file.
//
// Không đụng chrome.* — dùng ở background (module) và test Node.

/**
 * Ký tự BMP mà System.Text.Json (encoder UnsafeRelaxedJsonEscaping, .NET 8) ghi dạng escape. Sinh bằng cách cho bản C# ghi
 * từng ký tự 0x0000–0xFFFF (bỏ vùng surrogate) rồi gom các dải bị escape. Ký tự ngoài BMP (cặp surrogate) LUÔN bị escape
 * thành `\uXXXX\uXXXX`. Dạng escape: \b \t \n \f \r \" \\ cho 7 ký tự đó, còn lại `\u` + 4 chữ số HOA.
 */
const ESCAPED_BMP_RANGES = (
  "0-1f 22 5c 7f-a0 378-379 380-383 38b 38d 3a2 530 557-558 58b-58c 590 5c8-5cf 5eb-5ee 5f5-5ff 70e 74b-74c " +
  "7b2-7bf 7fb-7fc 82e-82f 83f 85c-85d 85f 86b-86f 88f 892-897 984 98d-98e 991-992 9a9 9b1 9b3-9b5 9ba-9bb " +
  "9c5-9c6 9c9-9ca 9cf-9d6 9d8-9db 9de 9e4-9e5 9ff-a00 a04 a0b-a0e a11-a12 a29 a31 a34 a37 a3a-a3b a3d a43-a46 " +
  "a49-a4a a4e-a50 a52-a58 a5d a5f-a65 a77-a80 a84 a8e a92 aa9 ab1 ab4 aba-abb ac6 aca ace-acf ad1-adf ae4-ae5 " +
  "af2-af8 b00 b04 b0d-b0e b11-b12 b29 b31 b34 b3a-b3b b45-b46 b49-b4a b4e-b54 b58-b5b b5e b64-b65 b78-b81 b84 " +
  "b8b-b8d b91 b96-b98 b9b b9d ba0-ba2 ba5-ba7 bab-bad bba-bbd bc3-bc5 bc9 bce-bcf bd1-bd6 bd8-be5 bfb-bff c0d " +
  "c11 c29 c3a-c3b c45 c49 c4e-c54 c57 c5b-c5c c5e-c5f c64-c65 c70-c76 c8d c91 ca9 cb4 cba-cbb cc5 cc9 cce-cd4 " +
  "cd7-cdc cdf ce4-ce5 cf0 cf4-cff d0d d11 d45 d49 d50-d53 d64-d65 d80 d84 d97-d99 db2 dbc dbe-dbf dc7-dc9 " +
  "dcb-dce dd5 dd7 de0-de5 df0-df1 df5-e00 e3b-e3e e5c-e80 e83 e85 e8b ea4 ea6 ebe-ebf ec5 ec7 ecf eda-edb " +
  "ee0-eff f48 f6d-f70 f98 fbd fcd fdb-fff 10c6 10c8-10cc 10ce-10cf 1249 124e-124f 1257 1259 125e-125f 1289 " +
  "128e-128f 12b1 12b6-12b7 12bf 12c1 12c6-12c7 12d7 1311 1316-1317 135b-135c 137d-137f 139a-139f 13f6-13f7 " +
  "13fe-13ff 1680 169d-169f 16f9-16ff 1716-171e 1737-173f 1754-175f 176d 1771 1774-177f 17de-17df 17ea-17ef " +
  "17fa-17ff 181a-181f 1879-187f 18ab-18af 18f6-18ff 191f 192c-192f 193c-193f 1941-1943 196e-196f 1975-197f " +
  "19ac-19af 19ca-19cf 19db-19dd 1a1c-1a1d 1a5f 1a7d-1a7e 1a8a-1a8f 1a9a-1a9f 1aae-1aaf 1acf-1aff 1b4d-1b4f " +
  "1b7f 1bf4-1bfb 1c38-1c3a 1c4a-1c4c 1c89-1c8f 1cbb-1cbc 1cc8-1ccf 1cfb-1cff 1f16-1f17 1f1e-1f1f 1f46-1f47 " +
  "1f4e-1f4f 1f58 1f5a 1f5c 1f5e 1f7e-1f7f 1fb5 1fc5 1fd4-1fd5 1fdc 1ff0-1ff1 1ff5 1fff-200a 2028-2029 202f " +
  "205f 2065 2072-2073 208f 209d-209f 20c1-20cf 20f1-20ff 218c-218f 2427-243f 244b-245f 2b74-2b75 2b96 " +
  "2cf4-2cf8 2d26 2d28-2d2c 2d2e-2d2f 2d68-2d6e 2d71-2d7e 2d97-2d9f 2da7 2daf 2db7 2dbf 2dc7 2dcf 2dd7 2ddf " +
  "2e5e-2e7f 2e9a 2ef4-2eff 2fd6-2fef 2ffc-3000 3040 3097-3098 3100-3104 3130 318f 31e4-31ef 321f a48d-a48f " +
  "a4c7-a4cf a62c-a63f a6f8-a6ff a7cb-a7cf a7d2 a7d4 a7da-a7f1 a82d-a82f a83a-a83f a878-a87f a8c6-a8cd " +
  "a8da-a8df a954-a95e a97d-a97f a9ce a9da-a9dd a9ff aa37-aa3f aa4e-aa4f aa5a-aa5b aac3-aada aaf7-ab00 " +
  "ab07-ab08 ab0f-ab10 ab17-ab1f ab27 ab2f ab6c-ab6f abee-abef abfa-abff d7a4-d7af d7c7-d7ca d7fc-d7ff " +
  "e000-f8ff fa6e-fa6f fada-faff fb07-fb12 fb18-fb1c fb37 fb3d fb3f fb42 fb45 fbc3-fbd2 fd90-fd91 fdc8-fdce " +
  "fdd0-fdef fe1a-fe1f fe53 fe67 fe6c-fe6f fe75 fefd-ff00 ffbf-ffc1 ffc8-ffc9 ffd0-ffd1 ffd8-ffd9 ffdd-ffdf " +
  "ffe7 ffef-fff8 fffe-ffff"
);

let escapedBmp = null;
const isEscapedBmp = (code) => {
  if (!escapedBmp) {
    escapedBmp = new Uint8Array(0x10000);
    for (const part of ESCAPED_BMP_RANGES.split(" ")) {
      const [a, b] = part.split("-").map((x) => parseInt(x, 16));
      for (let c = a; c <= (b ?? a); c++) escapedBmp[c] = 1;
    }
  }
  return escapedBmp[code] === 1;
};

/** Độ sâu tối đa (khớp HarvestPayloadCleaner.MaxDepth và mặc định của System.Text.Json mà Hub dùng). */
export const MAX_DEPTH = 64;

/** CDN ảnh Shopee VN (ShopeePdpParser.ImageBase). */
export const IMAGE_BASE = "https://down-vn.img.susercontent.com/file/";

const PRICE_DIVISOR = 100000n;
const INT64_MIN = -(2n ** 63n);
const INT64_MAX = 2n ** 63n - 1n;

/** Lỗi của RIÊNG SP này (JSON không xử lý được) — bản JS của HarvestItemRejectedException: gửi lại y nguyên vẫn hỏng. */
export class HarvestRejectedError extends Error {
  constructor(message) {
    super(message);
    this.name = "HarvestRejectedError";
  }
}

const isHigh = (c) => c >= 0xd800 && c <= 0xdbff;
const isLow = (c) => c >= 0xdc00 && c <= 0xdfff;

/** Bỏ \0 và surrogate UTF-16 lẻ (HarvestPayloadCleaner.StripInvalidText) — Postgres text không nhận. */
export function stripInvalidText(s) {
  if (typeof s !== "string" || s === "") return typeof s === "string" ? s : "";
  let out = null;
  for (let i = 0; i < s.length; i++) {
    const c = s.charCodeAt(i);
    const bad = c === 0
      || (isHigh(c) && (i + 1 >= s.length || !isLow(s.charCodeAt(i + 1))))
      || (isLow(c) && (i === 0 || !isHigh(s.charCodeAt(i - 1))));
    if (bad) {
      if (out === null) out = [s.slice(0, i)];
      continue;
    }
    if (out !== null) out.push(s[i]);
  }
  return out === null ? s : out.join("");
}

const hex4 = (s, start) => {
  if (start + 4 > s.length) return -1;
  let v = 0;
  for (let k = 0; k < 4; k++) {
    const h = s.charCodeAt(start + k);
    let d;
    if (h >= 48 && h <= 57) d = h - 48;
    else if (h >= 97 && h <= 102) d = h - 87;
    else if (h >= 65 && h <= 70) d = h - 55;
    else return -1;
    v = v * 16 + d;
  }
  return v;
};

/**
 * Như stripInvalidText nhưng hiểu JSON: trong chuỗi JSON còn bỏ escape `\u0000` và escape surrogate lẻ (cặp hợp lệ giữ
 * nguyên) — HarvestPayloadCleaner.StripInvalidJsonText, chép từng nhánh.
 */
export function stripInvalidJsonText(json) {
  const s = stripInvalidText(json);
  if (!/\\u/i.test(s)) return s;
  const out = [];
  let inString = false;
  let i = 0;
  while (i < s.length) {
    const c = s[i];
    if (!inString) {
      if (c === '"') inString = true;
      out.push(c);
      i++;
      continue;
    }
    if (c === '"') { inString = false; out.push(c); i++; continue; }
    if (c !== "\\" || i + 1 >= s.length) { out.push(c); i++; continue; }
    const code = s[i + 1] === "u" ? hex4(s, i + 2) : -1;
    if (code < 0) {
      out.push(c, s[i + 1]); // escape khác (\" \\ \n …): chép nguyên 2 ký tự
      i += 2;
      continue;
    }
    if (code === 0) { i += 6; continue; }
    if (code >= 0xd800 && code <= 0xdbff) {
      // Cao: giữ khi ngay sau là escape surrogate thấp; không thì bỏ.
      const low = i + 7 < s.length && s[i + 6] === "\\" && s[i + 7] === "u" ? hex4(s, i + 8) : -1;
      if (low >= 0xdc00 && low <= 0xdfff) {
        out.push(s.slice(i, i + 12));
        i += 12;
      } else {
        i += 6;
      }
      continue;
    }
    if (code >= 0xdc00 && code <= 0xdfff) { i += 6; continue; } // thấp đứng một mình
    out.push(s.slice(i, i + 6));
    i += 6;
  }
  return out.join("");
}

// ── Bộ đọc JSON CHẶT (khớp JsonDocument mặc định: RFC 8259, không comment / dấu phẩy thừa, độ sâu ≤ MAX_DEPTH) ──────────
// Nút: { t:"o", keys:[], vals:[] } | { t:"a", items:[] } | { t:"s", v } | { t:"n", raw } (số giữ NGUYÊN văn) |
// { t:"l", raw: "true"|"false"|"null" }. Khoá trùng ở bất kỳ đâu → từ chối (như RejectDuplicateKeys).

const fail = (msg) => { throw new HarvestRejectedError(`JSON PDP không xử lý được: ${msg}`); };

function parseStrict(src) {
  const n = src.length;
  let i = 0;
  const ws = () => {
    while (i < n) {
      const c = src.charCodeAt(i);
      if (c === 0x20 || c === 0x09 || c === 0x0a || c === 0x0d) i++;
      else break;
    }
  };
  const readString = () => {
    i++; // "
    const parts = [];
    let start = i;
    for (;;) {
      if (i >= n) fail("chuỗi chưa đóng");
      const c = src.charCodeAt(i);
      if (c === 0x22) { parts.push(src.slice(start, i)); i++; return parts.join(""); }
      if (c < 0x20) fail(`ký tự điều khiển trong chuỗi ở ${i}`);
      if (c !== 0x5c) { i++; continue; }
      parts.push(src.slice(start, i));
      const e = src[i + 1];
      switch (e) {
        case '"': parts.push('"'); i += 2; break;
        case "\\": parts.push("\\"); i += 2; break;
        case "/": parts.push("/"); i += 2; break;
        case "b": parts.push("\b"); i += 2; break;
        case "f": parts.push("\f"); i += 2; break;
        case "n": parts.push("\n"); i += 2; break;
        case "r": parts.push("\r"); i += 2; break;
        case "t": parts.push("\t"); i += 2; break;
        case "u": {
          const v = hex4(src, i + 2);
          if (v < 0) fail(`escape \\u sai ở ${i}`);
          parts.push(String.fromCharCode(v));
          i += 6;
          break;
        }
        default: fail(`escape sai ở ${i}`);
      }
      start = i;
    }
  };
  const isDigit = (c) => c >= 48 && c <= 57;
  const readNumber = () => {
    const s = i;
    if (src[i] === "-") i++;
    if (src[i] === "0") i++;
    else if (isDigit(src.charCodeAt(i)) && src[i] !== "0") { while (isDigit(src.charCodeAt(i))) i++; }
    else fail(`số sai ở ${s}`);
    if (src[i] === ".") {
      i++;
      if (!isDigit(src.charCodeAt(i))) fail(`số sai ở ${s}`);
      while (isDigit(src.charCodeAt(i))) i++;
    }
    if (src[i] === "e" || src[i] === "E") {
      i++;
      if (src[i] === "+" || src[i] === "-") i++;
      if (!isDigit(src.charCodeAt(i))) fail(`số sai ở ${s}`);
      while (isDigit(src.charCodeAt(i))) i++;
    }
    return { t: "n", raw: src.slice(s, i) };
  };
  const value = (depth) => {
    ws();
    if (i >= n) fail("hết dữ liệu");
    const c = src[i];
    if (c === "{" || c === "[") {
      if (depth + 1 > MAX_DEPTH) fail(`sâu quá ${MAX_DEPTH}`);
      i++;
      if (c === "[") {
        const items = [];
        ws();
        if (src[i] === "]") { i++; return { t: "a", items }; }
        for (;;) {
          items.push(value(depth + 1));
          ws();
          if (src[i] === ",") { i++; continue; }
          if (src[i] === "]") { i++; return { t: "a", items }; }
          fail(`thiếu , hoặc ] ở ${i}`);
        }
      }
      const keys = [];
      const vals = [];
      const seen = new Set();
      ws();
      if (src[i] === "}") { i++; return { t: "o", keys, vals }; }
      for (;;) {
        ws();
        if (src[i] !== '"') fail(`thiếu tên khoá ở ${i}`);
        const key = readString();
        if (seen.has(key)) throw new HarvestRejectedError(`JSON PDP có khoá trùng "${key}"`);
        seen.add(key);
        ws();
        if (src[i] !== ":") fail(`thiếu : ở ${i}`);
        i++;
        keys.push(key);
        vals.push(value(depth + 1));
        ws();
        if (src[i] === ",") { i++; continue; }
        if (src[i] === "}") { i++; return { t: "o", keys, vals }; }
        fail(`thiếu , hoặc } ở ${i}`);
      }
    }
    if (c === '"') return { t: "s", v: readString() };
    if (c === "-" || isDigit(src.charCodeAt(i))) return readNumber();
    for (const lit of ["true", "false", "null"]) {
      if (src.startsWith(lit, i)) { i += lit.length; return { t: "l", raw: lit }; }
    }
    fail(`ký tự lạ ở ${i}`);
  };
  const root = value(0);
  ws();
  if (i !== n) fail(`dư dữ liệu sau giá trị JSON ở ${i}`);
  return root;
}

// ── Thao tác trên nút object (giữ thứ tự khoá: gán đè khoá có sẵn giữ nguyên vị trí như JsonObject) ──────────────────────
const idx = (o, k) => o.keys.indexOf(k);
const get = (o, k) => { const j = idx(o, k); return j < 0 ? undefined : o.vals[j]; };
const has = (o, k) => idx(o, k) >= 0;
const setVal = (o, k, v) => { const j = idx(o, k); if (j < 0) { o.keys.push(k); o.vals.push(v); } else o.vals[j] = v; };
const remove = (o, k) => { const j = idx(o, k); if (j >= 0) { o.keys.splice(j, 1); o.vals.splice(j, 1); } };
const isObj = (x) => x !== undefined && x.t === "o";
const isArr = (x) => x !== undefined && x.t === "a";
const NULL = () => ({ t: "l", raw: "null" });

// ── Bộ ghi: khớp ToJsonString(UnsafeRelaxedJsonEscaping, không thụt dòng) ───────────────────────────────────────────
const SHORT_ESCAPES = { 8: "\\b", 9: "\\t", 10: "\\n", 12: "\\f", 13: "\\r", 34: '\\"', 92: "\\\\" };
const u4 = (c) => `\\u${c.toString(16).toUpperCase().padStart(4, "0")}`;

function quote(s) {
  const out = ['"'];
  for (let i = 0; i < s.length; i++) {
    const c = s.charCodeAt(i);
    if (isHigh(c) && i + 1 < s.length && isLow(s.charCodeAt(i + 1))) {
      out.push(u4(c), u4(s.charCodeAt(i + 1)));
      i++;
      continue;
    }
    if (isHigh(c) || isLow(c)) fail("surrogate lẻ khi ghi");
    if (isEscapedBmp(c)) out.push(SHORT_ESCAPES[c] ?? u4(c));
    else out.push(s[i]);
  }
  out.push('"');
  return out.join("");
}

function write(node, out) {
  switch (node.t) {
    case "o":
      out.push("{");
      for (let k = 0; k < node.keys.length; k++) {
        if (k) out.push(",");
        out.push(quote(node.keys[k]), ":");
        write(node.vals[k], out);
      }
      out.push("}");
      return;
    case "a":
      out.push("[");
      for (let k = 0; k < node.items.length; k++) {
        if (k) out.push(",");
        write(node.items[k], out);
      }
      out.push("]");
      return;
    case "s": out.push(quote(node.v)); return;
    default: out.push(node.raw);
  }
}

const deepClone = (node) => {
  switch (node.t) {
    case "o": return { t: "o", keys: [...node.keys], vals: node.vals.map(deepClone) };
    case "a": return { t: "a", items: node.items.map(deepClone) };
    default: return { ...node };
  }
};

/** data.product_shipping → chỉ còn {"shipping_fee_info":{"ship_from_location":…}} (danh sách GIỮ); không phải object → null. */
function keepShipFrom(shipping) {
  if (!isObj(shipping)) return NULL();
  const kept = { t: "o", keys: [], vals: [] };
  const fee = get(shipping, "shipping_fee_info");
  if (isObj(fee) && has(fee, "ship_from_location")) {
    setVal(kept, "shipping_fee_info", { t: "o", keys: ["ship_from_location"], vals: [deepClone(get(fee, "ship_from_location"))] });
  }
  return kept;
}

/**
 * JSON sạch từ JSON extension bắt được (HarvestPayloadCleaner.Clean): bỏ \0 / surrogate lẻ, data.account → {},
 * product_shipping chỉ còn nơi gửi, bỏ khối phụ thuộc người xem; số giữ nguyên văn, thứ tự khoá giữ, xuất gọn.
 * NÉM HarvestRejectedError khi không xử lý được (không phải object có data.item, sâu quá, khoá trùng, cú pháp hỏng).
 */
export function cleanPayload(json) {
  const text = stripInvalidJsonText(typeof json === "string" ? json : "");
  const root = parseStrict(text);
  const data = isObj(root) ? get(root, "data") : undefined;
  if (!isObj(data) || !isObj(get(data, "item"))) throw new HarvestRejectedError("JSON PDP thiếu data.item");

  if (has(data, "account")) setVal(data, "account", { t: "o", keys: [], vals: [] });
  for (const k of ["shop_vouchers", "top_banner", "coin_info", "price_breakdown", "button_group"]) remove(data, k);
  const price = get(data, "product_price");
  if (isObj(price)) { remove(price, "final_price_info"); remove(price, "final_price_vouchers"); }
  const review = get(data, "product_review");
  if (isObj(review)) remove(review, "liked");
  if (has(data, "product_shipping")) setVal(data, "product_shipping", keepShipFrom(get(data, "product_shipping")));

  const out = [];
  write(root, out);
  return out.join("");
}

/** raw_hash = SHA-256 (hex thường) của chuỗi UTF-8 đã làm sạch (HarvestPayloadCleaner.Hash). */
export async function rawHash(cleanJson) {
  const bytes = new TextEncoder().encode(cleanJson);
  const digest = await globalThis.crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

// ── Cột tóm tắt (ShopeePdpParser.Parse) ────────────────────────────────────────────────────────────────────────────

// char.IsWhiteSpace của .NET (KHÔNG giống String.prototype.trim: .NET tính U+0085, không tính U+FEFF).
const isDotNetSpace = (c) => (c >= 0x09 && c <= 0x0d) || c === 0x20 || c === 0x85 || c === 0xa0 || c === 0x1680
  || (c >= 0x2000 && c <= 0x200a) || c === 0x2028 || c === 0x2029 || c === 0x202f || c === 0x205f || c === 0x3000;
const isNullOrWhiteSpace = (s) => {
  if (s == null) return true;
  for (let i = 0; i < s.length; i++) if (!isDotNetSpace(s.charCodeAt(i))) return false;
  return true;
};
const dotNetTrim = (s) => {
  let a = 0;
  let b = s.length;
  while (a < b && isDotNetSpace(s.charCodeAt(a))) a++;
  while (b > a && isDotNetSpace(s.charCodeAt(b - 1))) b--;
  return s.slice(a, b);
};

/** Số nguyên int64 (số JSON dạng nguyên, hoặc chuỗi CHỈ gồm chữ số) → BigInt; khác đi → null (ShopeePdpParser.Long). */
const longOf = (node) => {
  if (node === undefined) return null;
  let v = null;
  if (node.t === "n" && /^-?\d+$/.test(node.raw)) v = BigInt(node.raw);
  else if (node.t === "s" && /^\d+$/.test(node.v)) v = BigInt(node.v);
  return v !== null && v >= INT64_MIN && v <= INT64_MAX ? v : null;
};
const strOf = (node) => (node !== undefined && node.t === "s" ? node.v : null);
const vnd = (v) => (v === null ? null : v / PRICE_DIVISOR); // BigInt chia cắt về 0 — như phép chia long của C#

const imageUrl = (id) => {
  if (isNullOrWhiteSpace(id)) return null;
  const t = dotNetTrim(id);
  return /^https?:\/\//i.test(t) ? t : IMAGE_BASE + t;
};
const distinct = (arr) => [...new Set(arr)];

function countTiers(item) {
  const tiers = get(item, "tier_variations");
  if (!isArr(tiers)) return 0;
  let n = 0;
  for (const t of tiers.items) {
    if (!isObj(t)) continue;
    const named = !isNullOrWhiteSpace(strOf(get(t, "name")));
    const opts = get(t, "options");
    const hasOption = isArr(opts) && opts.items.some((o) => o.t === "s" && !isNullOrWhiteSpace(o.v));
    if (named || hasOption) n++;
  }
  return n;
}

/**
 * Cột tóm tắt từ JSON (đã làm sạch). Số nguyên trả BigInt (id Shopee có thể vượt 2^53), mảng id cũng vậy. NÉM
 * HarvestRejectedError khi không có data.item với item_id dương.
 */
export function summarize(cleanJson) {
  const root = parseStrict(cleanJson);
  const data = isObj(root) ? get(root, "data") : undefined;
  const item = isObj(data) ? get(data, "item") : undefined;
  if (!isObj(item)) throw new HarvestRejectedError("JSON PDP thiếu data.item");
  const itemId = longOf(get(item, "item_id")) ?? 0n;
  if (itemId <= 0n) throw new HarvestRejectedError("JSON PDP thiếu data.item.item_id");
  const shopId = longOf(get(item, "shop_id")) ?? 0n;

  const categoryIds = [];
  const cats = get(item, "categories");
  if (isArr(cats)) {
    for (const c of cats.items) {
      if (!isObj(c)) continue;
      const id = longOf(get(c, "catid"));
      if (id !== null && id > 0n) categoryIds.push(id);
    }
  }
  const catIdRaw = longOf(get(item, "cat_id"));
  const catId = categoryIds.length > 0 ? categoryIds[categoryIds.length - 1] : (catIdRaw !== null && catIdRaw > 0n ? catIdRaw : null);

  let modelCount = 0;
  let maxBefore = null;
  const models = get(item, "models");
  if (isArr(models)) {
    for (const m of models.items) {
      if (!isObj(m)) continue;
      modelCount++;
      const price = vnd(longOf(get(m, "price")));
      const before = vnd(longOf(get(m, "price_before_discount")));
      const eff = before !== null && before > 0n ? before : price;
      if (eff !== null && eff > 0n && (maxBefore === null || eff > maxBefore)) maxBefore = eff;
    }
  }
  let priceBefore = vnd(longOf(get(item, "price_before_discount")));
  if (!(priceBefore !== null && priceBefore > 0n)) priceBefore = maxBefore ?? vnd(longOf(get(item, "price_max")));

  const images = [];
  const videos = [];
  const pi = isObj(data) ? get(data, "product_images") : undefined;
  if (isObj(pi)) {
    const imgs = get(pi, "images");
    if (isArr(imgs)) for (const x of imgs.items) { const u = x.t === "s" ? imageUrl(x.v) : null; if (u !== null) images.push(u); }
    const video = get(pi, "video");
    if (isObj(video)) {
      const def = get(video, "default_format");
      const d = isObj(def) ? strOf(get(def, "url")) : null;
      if (d) videos.push(d);
      const formats = get(video, "formats");
      if (isArr(formats)) for (const f of formats.items) { const u = isObj(f) ? strOf(get(f, "url")) : null; if (u) videos.push(u); }
    }
  }

  return {
    itemId, shopId,
    name: strOf(get(item, "title")),
    catId, categoryIds,
    brandId: longOf(get(item, "brand_id")),
    priceMin: vnd(longOf(get(item, "price_min"))),
    priceMax: vnd(longOf(get(item, "price_max"))),
    priceBeforeDiscount: priceBefore,
    tierCount: countTiers(item),
    modelCount,
    imageUrls: distinct(images),
    videoUrls: distinct(videos),
    sizeChartUrl: imageUrl(strOf(get(item, "size_chart"))),
  };
}

// ── Mục gửi Hub (HarvestedProductItem — HarvestOutbox.BuildRow) ─────────────────────────────────────────────────────

/** Độ dài tối đa của url / sourceUrl (HarvestedProductLimits.MaxUrlLength). */
export const MAX_URL_LENGTH = 2048;

/**
 * Mục SP gửi `POST /api/harvested-products/batch` — cùng luật BuildRow của outbox C#: bộ ba accountId/sheet/rowNo đủ cả hoặc
 * bỏ cả (bấm tay thì không có), link gốc quá 2048 ký tự thì bỏ (Hub dùng url), chuỗi đã bỏ \0 / surrogate lẻ.
 * Số nguyên là BigInt — ghi ra bằng toJson().
 */
export function buildHubItem({ clean, hash, summary, url, sourceUrl = null, accountId = null, sheet = null, rowNo = null, harvestedAtMs, fallbackShopId = null }) {
  const acct = stripInvalidText(accountId ?? "");
  const sh = stripInvalidText(sheet ?? "");
  const hasSource = !isNullOrWhiteSpace(acct) && !isNullOrWhiteSpace(sh) && Number.isInteger(rowNo) && rowNo >= 1;
  let src = stripInvalidText(sourceUrl ?? "");
  if (src.length > MAX_URL_LENGTH) src = "";
  const fb = fallbackShopId != null && /^\d+$/.test(String(fallbackShopId)) ? BigInt(fallbackShopId) : 0n;
  return {
    itemId: summary.itemId,
    shopId: summary.shopId > 0n ? summary.shopId : fb,
    url: stripInvalidText(url ?? ""),
    name: summary.name,
    catId: summary.catId,
    categoryIds: summary.categoryIds,
    brandId: summary.brandId,
    priceMin: summary.priceMin,
    priceMax: summary.priceMax,
    priceBeforeDiscount: summary.priceBeforeDiscount,
    tierCount: summary.tierCount,
    modelCount: summary.modelCount,
    imageUrls: summary.imageUrls,
    videoUrls: summary.videoUrls,
    sizeChartUrl: summary.sizeChartUrl,
    rawJson: clean,
    rawHash: hash,
    accountId: hasSource ? acct : null,
    sheet: hasSource ? sh : null,
    rowNo: hasSource ? rowNo : null,
    harvestedAt: new Date(Number.isFinite(harvestedAtMs) ? harvestedAtMs : Date.now()).toISOString(),
    sourceUrl: isNullOrWhiteSpace(src) ? null : src,
  };
}

/** JSON.stringify nhận BigInt (ghi thành số nguyên đúng từng chữ số, không qua Number). */
export function toJson(value) {
  if (value === null || value === undefined) return "null";
  switch (typeof value) {
    case "bigint": return value.toString();
    case "number": return Number.isFinite(value) ? String(value) : "null";
    case "boolean": return value ? "true" : "false";
    case "string": return JSON.stringify(value);
    default: break;
  }
  if (Array.isArray(value)) return `[${value.map(toJson).join(",")}]`;
  return `{${Object.entries(value).filter(([, v]) => v !== undefined).map(([k, v]) => `${JSON.stringify(k)}:${toJson(v)}`).join(",")}}`;
}
