// Logic THUẦN của Shopee Harvest (không đụng chrome.*) — dùng ở background + popup, test bằng Node
// (`node --test extensions/shopee-harvest/tests/*.test.mjs`).
//
// pdp-hook.js là content script thường nên không import được file này: hàm kiểm body bên đó là bản trùng của
// checkPdpBody — sửa luật ở đây thì sửa cả bên kia (tests/pdp-hook.test.mjs đối chiếu hai bản).

/**
 * Lấy { shopId, itemId } (chuỗi số) từ link sản phẩm Shopee. Thứ tự dạng y như
 * Shopee.Core/Scrape/ShopeeApiDataHelper.TryExtractShopeeItemId để C# và extension hiểu cùng một link:
 *  1. SEO      .../<ten>-i.<shop>.<item>
 *  2. /product/<shop>/<item>
 *  3. query    ?itemid=<item>&shopid=<shop> (cả item_id/itemId, shop_id/shopId)
 *  4. /pdp/<item> hoặc /pdp/item/<item>        → không có shopId
 *  5. 9–16 chữ số ở cuối URL                    → không có shopId
 * Không nhận ra → null. Dạng 4–5 không có shopId → shopId null.
 *
 * Dạng SEO neo CẢ HAI ĐẦU: trước `i.` là đầu chuỗi / `/` / `-`; sau `<item>` là hết chuỗi hoặc `?` `#` `&` `/`.
 * Nhờ vậy tên SP kiểu "Wifi-i.2.4G-Router-i.123.456" không bị đọc nhầm thành 2/4 ("i.2.4" bị chặn vì theo sau là
 * "G"). background.js#isShopeeProductUrl dùng cùng quy tắc trên pathname.
 */
export function extractProductIds(url) {
  if (typeof url !== "string" || !url.trim()) return null;
  let m = /(?:^|[/\-])i\.(\d+)\.(\d+)(?=$|[?#&/])/i.exec(url);
  if (m) return { shopId: m[1], itemId: m[2] };

  m = /\/product\/(\d+)\/(\d+)/i.exec(url);
  if (m) return { shopId: m[1], itemId: m[2] };

  m = /[?&](?:item_?id|itemId)=(\d+)/i.exec(url);
  if (m) {
    const shop = /[?&](?:shop_?id|shopId)=(\d+)/i.exec(url);
    return { shopId: shop ? shop[1] : null, itemId: m[1] };
  }

  m = /\/pdp\/(?:item\/)?(\d+)/i.exec(url);
  if (m) return { shopId: null, itemId: m[1] };

  m = /[./-](\d{9,16})(?:[?#&/]|$)/.exec(url);
  if (m) return { shopId: null, itemId: m[1] };

  return null;
}

/**
 * Kiểm body get_pc. Hợp lệ khi: parse được JSON, `error` null/vắng, có data.item.item_id (số), và nếu truyền
 * expectedItemId thì phải khớp. Trả { ok:true, itemId, shopId } hoặc { ok:false, reason }.
 * KHÔNG trả bản parse — người gọi giữ nguyên chuỗi gốc.
 */
export function checkPdpBody(text, expectedItemId = null) {
  if (typeof text !== "string" || !text) return { ok: false, reason: "empty" };
  let parsed;
  try { parsed = JSON.parse(text); } catch (_) { return { ok: false, reason: "not-json" }; }
  if (!parsed || typeof parsed !== "object") return { ok: false, reason: "not-object" };
  // `error:0` = thành công (Shopee hay trả 0 thay cho null); chỉ error khác null/undefined/0 mới là từ chối.
  if (parsed.error !== null && parsed.error !== undefined && parsed.error !== 0) return { ok: false, reason: `error=${String(parsed.error)}` };
  const id = parsed.data?.item?.item_id;
  if ((typeof id !== "number" && typeof id !== "string") || !/^\d+$/.test(String(id))) {
    return { ok: false, reason: "no-item-id" };
  }
  const itemId = String(id);
  if (expectedItemId != null && String(expectedItemId) !== itemId) {
    return { ok: false, reason: `item-mismatch(${itemId})` };
  }
  const shop = parsed.data?.item?.shop_id;
  return { ok: true, itemId, shopId: shop == null ? null : String(shop) };
}

// Khối dữ liệu CÁ NHÂN của tài khoản Shopee đang đăng nhập có trong body get_pc — không được vào fixture hay lên Hub.
// path = chuỗi khóa tính từ gốc; value = chuỗi JSON thay vào.
export const PRIVATE_PATHS = [
  { path: ["data", "account"], value: "{}" },                                  // thông tin tài khoản
  { path: ["data", "product_shipping", "delivery_address"], value: "null" },   // địa chỉ giao hàng (cả toạ độ)
];

/**
 * Mọi vị trí [đầu, cuối) của GIÁ TRỊ theo path trong chuỗi JSON `src` — duyệt TOÀN BỘ object (kể cả khoá TRÙNG:
 * không dừng ở lần khớp đầu). Đây là điểm sửa vòng 4: JSON.parse chỉ giữ khoá cuối, nên precheck theo bản parse
 * bỏ sót dữ liệu riêng tư nằm trong khoá bị trùng che (vd hai khoá `data`, hai `product_shipping`). Parser chuỗi
 * lệch (JSON hỏng) → NÉM.
 */
function collectSpans(src, path) {
  const n = src.length;
  let i = 0;
  const ws = () => { while (i < n && /\s/.test(src[i])) i++; };
  const readString = () => { // i ở dấu " mở → i sau dấu " đóng
    i++;
    while (i < n && src[i] !== '"') i += src[i] === "\\" ? 2 : 1;
    if (i >= n) throw new Error("eof");
    i++;
  };
  const skipValue = () => {
    ws();
    const c = src[i];
    if (c === '"') { readString(); return; }
    if (c === "{" || c === "[") {
      let depth = 0;
      while (i < n) {
        const ch = src[i];
        if (ch === '"') { readString(); continue; }
        if (ch === "{" || ch === "[") depth++;
        else if (ch === "}" || ch === "]") { depth--; if (depth === 0) { i++; return; } }
        i++;
      }
      throw new Error("eof");
    }
    while (i < n && !/[,}\]\s]/.test(src[i])) i++;
  };
  const spans = [];
  // Duyệt object bắt đầu ở i; với MỖI khoá: khớp path[depth] thì (lần cuối → ghi span giá trị; chưa cuối & là
  // object → đệ quy), rồi vẫn tiếp tục các khoá còn lại (khoá trùng).
  const walk = (depth) => {
    ws();
    if (src[i] !== "{") { skipValue(); return; } // không phải object ở mắt xích này → bỏ qua
    i++;
    for (;;) {
      ws();
      if (src[i] === "}") { i++; return; }
      if (src[i] !== '"') throw new Error("bad-key");
      const kStart = i;
      readString();
      // Khoá có mã thoát (vd "account" = "account") → giải mã rồi mới so, kẻo lọt khối riêng tư.
      const rawKey = src.slice(kStart + 1, i - 1);
      const key = rawKey.includes("\\") ? JSON.parse(`"${rawKey}"`) : rawKey;
      ws();
      if (src[i] !== ":") throw new Error("bad-colon");
      i++;
      ws();
      if (key === path[depth]) {
        if (depth === path.length - 1) { const s = i; skipValue(); spans.push([s, i]); }
        else walk(depth + 1);
      } else {
        skipValue();
      }
      ws();
      if (src[i] === ",") { i++; continue; }
      if (src[i] === "}") { i++; return; }
      throw new Error("bad-sep");
    }
  };
  walk(0);
  return spans;
}

/**
 * Cắt các khối PRIVATE_PATHS — sửa TRÊN CHUỖI, phần còn lại giữ nguyên từng byte (parse + stringify lại có thể
 * làm tròn số > 2^53). Cắt MỌI span khớp path (kể cả khoá trùng); đường dẫn không xuất hiện thì không có span.
 *
 * ĐÓNG AN TOÀN: hàm này gác dữ liệu riêng tư trước khi lên Hub / vào fixture, nên KHÔNG BAO GIỜ trả về body chưa
 * cắt. Mọi trục trặc (không phải JSON, parser chuỗi lệch, span chồng nhau, kiểm lại sau khi cắt thấy khối riêng tư
 * VẪN còn) → NÉM lỗi. Người gọi bắt lỗi và coi là thất bại (bước: ok=false message "redact-failed"; popup: báo lỗi).
 */
export function redactPrivate(text, rules = PRIVATE_PATHS) {
  if (typeof text !== "string" || !text) throw new Error("redact-failed: body rỗng");
  try { JSON.parse(text); } catch (e) { throw new Error(`redact-failed: body không phải JSON (${e.message})`); }

  let out;
  try {
    const edits = [];
    for (const rule of rules) for (const span of collectSpans(text, rule.path)) edits.push({ span, value: rule.value });
    edits.sort((a, b) => b.span[0] - a.span[0]); // thay từ cuối lên để vị trí phía trước không xê dịch
    for (let k = 1; k < edits.length; k++) {
      if (edits[k].span[1] > edits[k - 1].span[0]) throw new Error("span riêng tư chồng nhau");
    }
    out = text;
    for (const { span, value } of edits) out = out.slice(0, span[0]) + value + out.slice(span[1]);
  } catch (e) {
    throw new Error(`redact-failed: cắt chuỗi lỗi (${e.message})`);
  }

  // KIỂM LẠI: output phải parse được, và MỌI span còn khớp path giờ đúng bằng giá trị đã cắt (soát cả khoá trùng).
  try { JSON.parse(out); } catch (e) { throw new Error(`redact-failed: kết quả không còn là JSON (${e.message})`); }
  for (const rule of rules) {
    for (const span of collectSpans(out, rule.path)) {
      if (out.slice(span[0], span[1]).trim() !== rule.value.trim()) {
        throw new Error(`redact-failed: ${rule.path.join(".")} vẫn còn dữ liệu sau khi cắt`);
      }
    }
  }
  return out;
}
