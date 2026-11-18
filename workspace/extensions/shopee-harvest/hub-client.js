// Gọi Hub trung tâm (Shopee.Hub.Web) từ extension — GĐ7: extension tự đẩy SP lên Hub bằng token chung (header X-Api-Token,
// X-Machine-Id như HubClient C#). Chỉ gọi 2 route harvested-products: batch (ghi) và known (hỏi SP đã có chưa).
// KHÔNG chứa URL / token mặc định: người dùng nhập ở trang Cài đặt (chrome.storage.local), app Harvest truyền qua RPC.
// Không đụng chrome.* — fetch truyền vào được (test Node).

export const HUB_ROUTES = Object.freeze({
  batch: "/api/harvested-products/batch",
  known: "/api/harvested-products/known",
});

/** Trần thời gian một lần gửi lô (JSON SP ~60–80 KB/mục) và một lần hỏi known. */
export const BATCH_TIMEOUT_MS = 60_000;
export const KNOWN_TIMEOUT_MS = 8_000;

/**
 * URL gốc Hub đã chuẩn hoá ("https://host[:port]", bỏ "/" cuối và mọi path/query) hoặc null nếu không dùng được.
 * Chỉ nhận https, hoặc http tới localhost / 127.0.0.1 (máy này là Hub) — không gửi token qua http trơn ra ngoài.
 */
export function normalizeHubUrl(url) {
  if (typeof url !== "string" || !url.trim()) return null;
  let u;
  try { u = new URL(url.trim()); } catch (_) { return null; }
  const local = u.hostname === "localhost" || u.hostname === "127.0.0.1";
  if (u.protocol !== "https:" && !(u.protocol === "http:" && local)) return null;
  return u.origin;
}

/** Mẫu quyền host cho chrome.permissions ("https://host/*" — Chrome không tính cổng trong mẫu). */
export function hubPermissionPattern(url) {
  const origin = normalizeHubUrl(url);
  if (!origin) return null;
  const u = new URL(origin);
  return `${u.protocol}//${u.hostname}/*`;
}

/**
 * Lý do cấu hình KHÔNG dùng được (để báo đúng cho người dùng / app), null = dùng được. Phân biệt riêng ca Hub là
 * http://<máy khác> (vd IP LAN): extension không gửi token qua http trơn ra ngoài localhost.
 */
export function hubConfigProblem(cfg) {
  if (!cfg || typeof cfg !== "object") return "chưa có cấu hình Hub";
  const raw = typeof cfg.url === "string" ? cfg.url.trim() : "";
  if (!raw) return "thiếu URL Hub";
  if (!normalizeHubUrl(raw)) {
    let u = null;
    try { u = new URL(raw); } catch (_) {}
    if (u && u.protocol === "http:") {
      return `Hub là ${u.origin} (http, không phải localhost) — extension không gửi token qua http ra ngoài máy; cần Hub https hoặc http://localhost`;
    }
    return `URL Hub không hợp lệ (${raw})`;
  }
  if (typeof cfg.token !== "string" || !cfg.token.trim()) return "thiếu token Hub";
  if (typeof cfg.machineId !== "string" || !cfg.machineId.trim()) return "thiếu mã máy";
  return null;
}

/** Cấu hình đủ để gửi: URL hợp lệ + token + machine id. */
export function isHubConfigured(cfg) {
  return Boolean(cfg && normalizeHubUrl(cfg.url) && typeof cfg.token === "string" && cfg.token.trim()
    && typeof cfg.machineId === "string" && cfg.machineId.trim());
}

const headers = (cfg) => ({
  "Content-Type": "application/json",
  "X-Api-Token": cfg.token.trim(),
  "X-Machine-Id": cfg.machineId.trim(),
});

async function call(cfg, route, body, { fetchImpl = globalThis.fetch, timeoutMs }) {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), timeoutMs);
  try {
    const res = await fetchImpl(normalizeHubUrl(cfg.url) + route, { method: "POST", headers: headers(cfg), body, signal: ctrl.signal });
    let json = null;
    try { json = await res.json(); } catch (_) {}
    return { status: res.status, json };
  } finally {
    clearTimeout(timer);
  }
}

/**
 * Phân loại phản hồi Hub (dùng chung cho batch / known):
 * - kind "ok": 200.
 * - kind "temp": mất mạng / hết giờ / 5xx (kể cả 503 pg-unavailable, pg-not-ready) / 408 / 429 → GIỮ, gửi lại sau.
 * - kind "auth": 401 / 403 (token sai, chưa cấp quyền) — không phải lỗi dữ liệu.
 * - kind "bad": 4xx khác (400, 413…) — dữ liệu / request hỏng, gửi lại y nguyên vẫn hỏng. tooLarge = 413 hoặc batch-too-large.
 */
export function classifyHttp(status, json) {
  const code = json && typeof json.error === "string" ? json.error : "";
  if (status === 200) return { kind: "ok" };
  if (status === 401 || status === 403) return { kind: "auth", status, reason: `Hub từ chối token (HTTP ${status})` };
  if (status === 408 || status === 429 || status >= 500 || status === 0) {
    return { kind: "temp", status, reason: `Hub tạm lỗi (HTTP ${status}${code ? ` ${code}` : ""})` };
  }
  return {
    kind: "bad", status, error: code,
    tooLarge: status === 413 || code === "batch-too-large",
    reason: `Hub từ chối request (HTTP ${status}${code ? ` ${code}` : ""})`,
  };
}

/**
 * POST lô SP (mỗi phần tử itemBodies là CHUỖI JSON của một HarvestedProductItem — giữ nguyên số BigInt). clientNow đặt NGAY
 * trước lời gọi (Hub quy giờ cào về đồng hồ Hub theo nó). Trả { kind:"ok", results } với results đúng thứ tự / đủ số mục,
 * hoặc temp / auth / bad như classifyHttp. 200 mà results lệch số mục → temp (không đoán, giữ lại gửi sau).
 */
export async function postBatch(cfg, itemBodies, { fetchImpl, now = Date.now, timeoutMs = BATCH_TIMEOUT_MS } = {}) {
  if (!isHubConfigured(cfg)) return { kind: "nocfg", reason: "Chưa cấu hình Hub (URL / token / mã máy)." };
  const body = `{"machineId":${JSON.stringify(cfg.machineId.trim())},"clientNow":${JSON.stringify(new Date(now()).toISOString())},` +
    `"items":[${itemBodies.join(",")}]}`;
  let r;
  try {
    r = await call(cfg, HUB_ROUTES.batch, body, { fetchImpl, timeoutMs });
  } catch (e) {
    return { kind: "temp", status: 0, reason: `Không kết nối được Hub (${e?.name === "AbortError" ? "quá giờ" : e?.message || e})` };
  }
  const c = classifyHttp(r.status, r.json);
  if (c.kind !== "ok") return c;
  const results = Array.isArray(r.json?.results) ? r.json.results : null;
  if (!results || results.length !== itemBodies.length) {
    return { kind: "temp", status: 200, reason: "Hub trả kết quả lệch số mục — giữ lại gửi sau." };
  }
  return { kind: "ok", results };
}

/** Hỏi Hub SP nào đã có dữ liệu (≤ 500 id). maxAgeDays 3650 = "đã từng lấy". { kind:"ok", items:[{itemId, harvestedAt}] }. */
export async function postKnown(cfg, itemIds, { fetchImpl, maxAgeDays = 3650, timeoutMs = KNOWN_TIMEOUT_MS } = {}) {
  if (!isHubConfigured(cfg)) return { kind: "nocfg", reason: "Chưa cấu hình Hub." };
  const body = `{"itemIds":[${itemIds.map((x) => String(x)).filter((x) => /^\d+$/.test(x)).join(",")}],"maxAgeDays":${maxAgeDays}}`;
  let r;
  try {
    r = await call(cfg, HUB_ROUTES.known, body, { fetchImpl, timeoutMs });
  } catch (e) {
    return { kind: "temp", status: 0, reason: `Không kết nối được Hub (${e?.name === "AbortError" ? "quá giờ" : e?.message || e})` };
  }
  const c = classifyHttp(r.status, r.json);
  if (c.kind !== "ok") return c;
  return { kind: "ok", items: Array.isArray(r.json?.items) ? r.json.items : [] };
}
