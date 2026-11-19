// GĐ9 — ẢNH GỐC lên Hub: sau khi đẩy JSON SP, extension tải ảnh SP (cùng proxy / IP lúc cào) rồi đẩy lên kho ảnh gốc của Hub,
// để app Upload xuất file không phải gọi CDN Shopee nữa. Không đụng chrome.* — storage / fetch / đồng hồ truyền vào (test Node).
//
// Luồng: imageIdsOf(JSON sạch) → enqueue việc (1 SP = 1 việc, BỀN trong chrome.storage.local) → runRound trong khoảng nghỉ giữa 2
// SP (app gọi startSourceImages) hoặc ngay sau lần bấm tay: hỏi Hub id nào còn thiếu (POST /api/source-images/missing) → tải
// https://cf.shopee.vn/file/{id} → PUT /api/source-images/{id}. Lượt có hạn chót; hết giờ / lỗi tạm thì giữ việc, lượt sau làm tiếp
// (Hub trả lại đúng các id còn thiếu, ảnh đã đẩy không tải lại).
//
// Lưu trong chrome.storage.local, khoá STATE_KEY = { seq, jobs:[việc…], hubFullUntil, hubFullCode, droppedFull }. Việc =
// { seq, key: profile+"|"+itemId, itemId, ids, skipped, profile, addedAt, attempts, lastError, timeouts:{id:số lượt} }. Profile như
// hub-queue.js ("auto": cấu hình app truyền; "manual": trang Cài đặt) — lượt chỉ làm việc của đúng profile nó được giao.
//
// Lỗi RIÊNG ảnh (ghi lỗi, không thử lại): CDN 404 / 410, CDN trả WebP, ảnh > 2 MB, Hub 400 bad-id / empty / corrupt-image, 413, 415.
// Lỗi CHUNG (giữ ảnh cho lượt sau, KHÔNG tính vào việc): CDN 401 / 403 / 407 / 408 / 429 / 5xx / mất mạng / 200 mà không phải ảnh
// (trang chặn HTML), Hub mất mạng / 5xx / 4xx lạ. Cầu dao: CDN lỗi chung liên tiếp, WebP liên tiếp, hay quá trần số request CDN
// mỗi lượt → dừng lượt, giữ việc (CDN đang chặn / đổi hành vi thì không đốt cả hàng đợi).

import { normalizeHubUrl, isHubConfigured, hubConfigProblem } from "./hub-client.js";

export const STATE_KEY = "sourceImages";

export const SOURCE_IMAGE_ROUTES = Object.freeze({
  missing: "/api/source-images/missing",
  put: "/api/source-images/", // + id
});

/** CDN gốc của ảnh SP (không qua biến đổi / nén lại của down-vn.img.susercontent.com). */
export const CDN_BASE = "https://cf.shopee.vn/file/";

/** Luật id ảnh của Hub (SourceImageLimits.IsValidId): id sai luật không hỏi Hub, không tải. */
export const IMAGE_ID_RULE = /^[A-Za-z0-9][A-Za-z0-9_-]{7,127}$/;
export const isValidImageId = (id) => typeof id === "string" && IMAGE_ID_RULE.test(id);

/** Số ảnh SP đầu lấy mỗi SP — khớp MassUploadExportOptions.ProductImagesPerItem (9 dùng + 3 dự phòng). */
export const PRODUCT_IMAGES_PER_ITEM = 12;

export const LIMITS = Object.freeze({
  maxBytes: 2 * 1024 * 1024,   // SourceImageLimits.MaxBytes
  maxIdsPerCall: 200,          // SourceImageLimits.MaxIdsPerCall
  maxJobs: 1000,               // ~1000 SP chờ (mỗi việc vài trăm byte – vài KB)
  maxAttempts: 5,              // lượt hỏng RIÊNG việc đó liên tiếp (CDN vẫn chạy với việc khác) thì bỏ việc
  cdnTempBreaker: 5,           // lỗi chung CDN liên tiếp (kể cả ảnh của SP khác) thì dừng lượt
  webpBreaker: 5,              // WebP liên tiếp thì dừng lượt (CDN đổi hành vi / bỏ Accept) — không đánh lỗi các ảnh đó
  maxCdnPerRound: 150,         // trần số request CDN mỗi lượt
  imageTimeoutRounds: 3,       // một ảnh quá giờ ở chừng này lượt liên tiếp mới là lỗi riêng ảnh
  imageTimeoutMs: 20_000,      // trần tải 1 ảnh từ CDN
  putTimeoutMs: 30_000,        // trần đẩy 1 ảnh lên Hub
  missingTimeoutMs: 15_000,    // trần 1 lần hỏi id còn thiếu
  minStepMs: 1_000,            // còn ít hơn chừng này tới hạn chót thì không bắt đầu bước mới
  parallel: 2,                 // số ảnh tải / đẩy song song
  hubFullPauseMs: 30 * 60_000, // Hub báo hết chỗ (507) → các lượt trong 30 phút tới trả ngay "hubFull"
  maxErrors: 20,               // số lỗi chi tiết giữ trong kết quả lượt
});

// ── Luật id ảnh (khớp MassUploadExporter.ImageKeys + ShopeeListingReader.Read / ReadTiers) ─────────────────────────────

const isObj = (v) => v !== null && typeof v === "object" && !Array.isArray(v);

// char.IsWhiteSpace của .NET (như pdp-clean.js: .NET tính U+0085, không tính U+FEFF).
const isDotNetSpace = (c) => (c >= 0x09 && c <= 0x0d) || c === 0x20 || c === 0x85 || c === 0xa0 || c === 0x1680
  || (c >= 0x2000 && c <= 0x200a) || c === 0x2028 || c === 0x2029 || c === 0x202f || c === 0x205f || c === 0x3000;
const isNullOrWhiteSpace = (s) => {
  if (s == null) return true;
  for (let i = 0; i < s.length; i++) if (!isDotNetSpace(s.charCodeAt(i))) return false;
  return true;
};

/**
 * MassUploadText.Clean: bỏ ký tự điều khiển < 0x20 (trừ tab / xuống dòng), surrogate lẻ, U+FFFE / U+FFFF; CR / CRLF → LF; NFC.
 * Ở đây chỉ dùng để quyết "tên nhóm / lựa chọn có rỗng không" — đúng chỗ ReadTiers dùng nó.
 */
export function massUploadClean(s) {
  if (typeof s !== "string" || s.length === 0) return "";
  let out = "";
  for (let i = 0; i < s.length; i++) {
    const c = s.charCodeAt(i);
    if (c >= 0xd800 && c <= 0xdbff) {
      const n = i + 1 < s.length ? s.charCodeAt(i + 1) : 0;
      if (n >= 0xdc00 && n <= 0xdfff) { out += s[i] + s[i + 1]; i++; }
      continue;
    }
    if (c >= 0xdc00 && c <= 0xdfff) continue;
    if (c === 0x0d) { if (!(i + 1 < s.length && s.charCodeAt(i + 1) === 0x0a)) out += "\n"; continue; }
    if ((c < 0x20 && c !== 0x09 && c !== 0x0a) || c === 0xfffe || c === 0xffff) continue;
    out += s[i];
  }
  return out.normalize("NFC");
}

/** ReadTiers: các nhóm (phần tử object của tier_variations) → null khi SP KHÔNG biến thể, không thì nhóm 1 { options, images }. */
function firstTier(item) {
  const tvs = isObj(item) && Array.isArray(item.tier_variations) ? item.tier_variations : [];
  const tiers = [];
  for (const t of tvs) {
    if (!isObj(t)) continue;
    const options = Array.isArray(t.options) ? t.options.map((o) => (typeof o === "string" ? massUploadClean(o) : "")) : [];
    const images = Array.isArray(t.images)
      ? t.images.map((im) => (typeof im === "string" && im.length > 0 ? im : null))
      : [];
    while (images.length < options.length) images.push(null);
    images.length = options.length; // ReadTiers cắt ảnh theo số lựa chọn
    tiers.push({ name: massUploadClean(typeof t.name === "string" ? t.name : null), options, images });
  }
  // SP không biến thể: 0 nhóm, hoặc đúng 1 nhóm tên rỗng / trắng với ≤ 1 lựa chọn và mọi lựa chọn rỗng / trắng.
  const none = tiers.length === 0
    || (tiers.length === 1 && isNullOrWhiteSpace(tiers[0].name)
      && tiers[0].options.length <= 1 && tiers[0].options.every(isNullOrWhiteSpace));
  return none ? null : tiers[0];
}

/**
 * Mã ảnh gốc của một SP từ JSON get_pc (đã làm sạch): ảnh SP theo thứ tự (12 đầu) + ảnh từng lựa chọn của nhóm phân loại 1
 * (chỉ khi SP có biến thể), khử trùng giữ thứ tự xuất hiện đầu. Không lấy ảnh mô tả. id sai luật Hub → `skipped`.
 * KHÔNG ném: JSON hỏng → { ids:[], skipped:[] }.
 */
export function imageIdsOf(cleanJsonText) {
  let root;
  try { root = JSON.parse(typeof cleanJsonText === "string" ? cleanJsonText : ""); } catch (_) { return { ids: [], skipped: [] }; }
  if (!isObj(root)) return { ids: [], skipped: [] };
  const data = isObj(root.data) ? root.data : root;

  const product = [];
  const pi = isObj(data.product_images) ? data.product_images.images : null;
  if (Array.isArray(pi)) for (const x of pi) if (typeof x === "string" && x.length > 0) product.push(x);

  const tier = firstTier(isObj(data.item) ? data.item : null);
  const tierImages = tier ? tier.images.filter((x) => x !== null && !isNullOrWhiteSpace(x)) : [];

  const all = [...new Set([...product.slice(0, PRODUCT_IMAGES_PER_ITEM), ...tierImages])];
  return { ids: all.filter(isValidImageId), skipped: all.filter((x) => !isValidImageId(x)) };
}

/** Loại ảnh theo byte đầu: "image/jpeg" | "image/png" | "image/webp" (Hub từ chối — báo riêng "CDN trả WebP") | null. */
export function detectImageType(bytes) {
  const b = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes || []);
  if (b.length >= 3 && b[0] === 0xff && b[1] === 0xd8 && b[2] === 0xff) return "image/jpeg";
  const png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
  if (b.length >= 8 && png.every((v, i) => b[i] === v)) return "image/png";
  const ascii = (from, to) => String.fromCharCode(...b.subarray(from, to));
  if (b.length >= 12 && ascii(0, 4) === "RIFF" && ascii(8, 12) === "WEBP") return "image/webp";
  return null;
}

// ── Phân loại phản hồi Hub ──────────────────────────────────────────────────────────────────────────────────────────

const errCode = (json) => (json && typeof json.error === "string" ? json.error : "");

/**
 * Phản hồi PUT /api/source-images/{id}:
 * - ok (200 + body object): { existed, replaced };
 * - bad (400 bad-id / empty / corrupt-image, 413 too-large, 415 unsupported-type): lỗi RIÊNG ảnh đó;
 * - hubFull (507 source-quota-exceeded / disk-low): dừng đẩy;
 * - auth (401 / 403); temp (408 / 429 / 5xx / 0 = mất mạng, 200 mà body lạ, 4xx khác — vd 404 / 405: Hub / proxy chưa có route,
 *   lỗi môi trường chứ không phải lỗi ảnh).
 */
export function classifyPut(status, json) {
  const code = errCode(json);
  if (status === 200) {
    if (!isObj(json)) return { kind: "temp", status, reason: "Hub trả phản hồi lạ cho ảnh (HTTP 200, body không đọc được)" };
    return { kind: "ok", existed: json.existed === true, replaced: json.replaced === true };
  }
  if (status === 413 || status === 415 || (status === 400 && ["bad-id", "empty", "corrupt-image"].includes(code))) {
    return { kind: "bad", status, code, reason: `Hub từ chối ảnh (HTTP ${status}${code ? ` ${code}` : ""})` };
  }
  return classifyOther(status, code);
}

/**
 * Phản hồi POST /api/source-images/missing: ok (200 + mảng missing); bad CHỈ khi 400 bad-id / too-many-ids (lỗi dữ liệu của việc);
 * auth / hubFull (507, phòng xa); còn lại (5xx, mất mạng, 404 / 405 / 415 / 400 lạ…) là temp — lỗi môi trường, giữ việc.
 */
export function classifyMissing(status, json) {
  const code = errCode(json);
  if (status === 200) {
    if (!isObj(json) || !Array.isArray(json.missing)) return { kind: "temp", status, reason: "Hub trả danh sách ảnh thiếu không đọc được" };
    return { kind: "ok", missing: json.missing.filter((x) => typeof x === "string") };
  }
  if (status === 400 && (code === "bad-id" || code === "too-many-ids")) {
    return { kind: "bad", status, code, reason: `Hub từ chối danh sách ảnh (HTTP 400 ${code})` };
  }
  return classifyOther(status, code);
}

function classifyOther(status, code) {
  const tail = `HTTP ${status}${code ? ` ${code}` : ""}`;
  if (status === 507) return { kind: "hubFull", status, code: code || "hub-full", reason: `Hub hết chỗ lưu ảnh gốc (${tail})` };
  if (status === 401 || status === 403) return { kind: "auth", status, reason: `Hub từ chối token (${tail})` };
  if (status === 0 || status === 408 || status === 429 || status >= 500) return { kind: "temp", status, reason: `Hub tạm lỗi (${tail})` };
  return { kind: "temp", status, reason: `Hub trả ${tail} — lỗi cấu hình / đường tới Hub, không phải lỗi ảnh` };
}

/**
 * Phản hồi CDN khi tải ảnh: 200 ok; CHỈ 404 / 410 là lỗi riêng ảnh (ảnh không còn); còn lại (401 / 403 / 407 chặn, 408 / 429 / 5xx…)
 * là lỗi chung — CDN đang chặn / lỗi, giữ ảnh cho lượt sau.
 */
export function classifyCdn(status) {
  if (status === 200) return { kind: "ok" };
  if (status === 404 || status === 410) return { kind: "bad", reason: `CDN trả HTTP ${status}` };
  return { kind: "temp", reason: `CDN chặn / tạm lỗi (HTTP ${status})` };
}

// ── Hàng đợi + lượt xử lý ───────────────────────────────────────────────────────────────────────────────────────────

const emptyState = () => ({ seq: 0, jobs: [], hubFullUntil: 0, hubFullCode: null, droppedFull: 0 });

/** Định danh lượt (app dùng để không cộng hai lần cùng một lượt). */
export const newRoundId = (now = Date.now) =>
  `${Number(now()).toString(36)}-${Math.random().toString(36).slice(2, 8)}`;

const emptyRound = (roundId, startedAt) => ({
  roundId, startedAt, endedAt: null,
  checked: 0, present: 0, pushed: 0, existed: 0, failed: 0, skipped: 0, dropped: 0,
  errors: [], stoppedBy: null, hubFullCode: null, lastError: null,
  queue: { jobs: 0, ids: 0 },
});

const copyRound = (r) => (r ? { ...r, errors: r.errors.map((e) => ({ ...e })), queue: { ...r.queue } } : null);

/**
 * @param storage { get(keys[]) → Promise<obj>, set(obj) → Promise } (chrome.storage.local)
 * @param fetchImpl fetch (mặc định globalThis.fetch)
 * @param now () → ms
 * @param onChange () → void sau mỗi lần đổi kho (popup làm mới số)
 */
export function createSourceImageQueue({ storage, fetchImpl, now = Date.now, limits = LIMITS, onChange = () => {} }) {
  const doFetch = (...a) => (fetchImpl || globalThis.fetch)(...a);
  // Mọi thao tác đọc-sửa-ghi STATE chạy nối tiếp (lượt ảnh, lần bấm thêm việc, popup đọc số chạy song song). Lượt KHÔNG giữ khoá
  // suốt lượt: thêm việc trong lúc lượt chạy vẫn xong ngay.
  let chain = Promise.resolve();
  const serial = (fn) => {
    const run = chain.then(fn, fn);
    chain = run.catch(() => {});
    return run;
  };
  const readState = async () => {
    const got = await storage.get([STATE_KEY]);
    const s = got?.[STATE_KEY];
    if (!isObj(s) || !Array.isArray(s.jobs)) return emptyState();
    return {
      seq: Number(s.seq) || 0,
      jobs: s.jobs.filter((j) => isObj(j) && Array.isArray(j.ids)),
      hubFullUntil: Number(s.hubFullUntil) || 0,
      hubFullCode: typeof s.hubFullCode === "string" ? s.hubFullCode : null,
      droppedFull: Number(s.droppedFull) || 0,
    };
  };
  const writeState = async (s) => { await storage.set({ [STATE_KEY]: s }); };
  const changed = () => { try { onChange(); } catch (_) {} };
  const countOf = (jobs) => ({ jobs: jobs.length, ids: jobs.reduce((n, j) => n + j.ids.length, 0) });

  let live = null;        // lượt đang chạy (số liệu tới lúc hỏi)
  let lastRound = null;   // lượt xong gần nhất
  let abortAll = false;   // abort() của background (lệnh Dừng của launcher)
  const inflight = new Set();

  /**
   * Thêm việc ảnh của một SP. Cùng profile + itemId → bản mới THAY bản cũ (vị trí cuối hàng). Không có id hợp lệ → không tạo
   * việc. Đầy (limits.maxJobs) → từ chối, đếm droppedFull — KHÔNG xoá việc cũ. Trả { ok, reason?, empty? }.
   */
  const enqueue = ({ profile, itemId, ids, skipped = 0 }) => serial(async () => {
    const list = [...new Set((Array.isArray(ids) ? ids : []).filter(isValidImageId))];
    if (list.length === 0) return { ok: true, empty: true };
    const key = `${profile}|${itemId}`;
    const s = await readState();
    const others = s.jobs.filter((j) => j.key !== key);
    if (others.length + 1 > limits.maxJobs) {
      s.droppedFull++;
      try { await writeState(s); } catch (_) {}
      return { ok: false, reason: `Hàng đợi ảnh gốc đã đầy (${others.length} SP) — ảnh SP ${itemId} không được xếp lượt đẩy.` };
    }
    s.jobs = [...others, {
      seq: ++s.seq, key, itemId: String(itemId ?? ""), ids: list, skipped: Number(skipped) || 0, profile,
      addedAt: now(), attempts: 0, lastError: null, timeouts: {},
    }];
    await writeState(s);
    changed();
    return { ok: true };
  });

  /** Sửa việc theo seq (việc đã bị bản mới thay / đã xoá → bỏ qua). fn(job) → "remove" | undefined (đã sửa tại chỗ). */
  const updateJob = (seq, fn) => serial(async () => {
    const s = await readState();
    const job = s.jobs.find((j) => j.seq === seq);
    if (!job) return;
    if (fn(job) === "remove") s.jobs = s.jobs.filter((j) => j !== job);
    await writeState(s);
  });

  /**
   * Bỏ mốc "Hub hết chỗ" NGAY (app vừa bấm Chạy lại sau khi dọn kho) — kể cả khi đang có lượt khác chạy. Trả true nếu có mốc để bỏ.
   */
  const clearHubFull = () => serial(async () => {
    const st = await readState();
    if (!st.hubFullUntil && !st.hubFullCode) return false;
    st.hubFullUntil = 0;
    st.hubFullCode = null;
    await writeState(st);
    changed();
    return true;
  });

  /** Chạy fn(signal) với trần thời gian ms; lệnh abort() cũng huỷ. */
  const withTimeout = async (ms, fn) => {
    const ctrl = new AbortController();
    inflight.add(ctrl);
    const timer = setTimeout(() => ctrl.abort(), Math.max(1, ms));
    try { return await fn(ctrl.signal); } finally { clearTimeout(timer); inflight.delete(ctrl); }
  };

  const hubHeaders = (cfg, extra) => ({ ...extra, "X-Api-Token": cfg.token.trim(), "X-Machine-Id": cfg.machineId.trim() });
  const readJson = async (res) => { try { return await res.json(); } catch (_) { return null; } };

  /**
   * Một lượt: làm các việc của `profile` từ cũ tới mới tới hạn chót now + budgetMs. KHÔNG ném. Kết quả: { roundId, startedAt,
   * endedAt, checked, present, pushed, existed, failed, skipped, dropped, errors (≤ 20), stoppedBy, hubFullCode, lastError,
   * queue:{jobs, ids} }. stoppedBy: null (hết việc / còn ảnh để lượt sau) | deadline | abort | hubFull | auth | temp | cdnTemp |
   * cdnWebp | cdnCap | nocfg. clearHubFull: bỏ mốc "Hub hết chỗ" đang giữ (app vừa bấm Chạy lại sau khi dọn kho).
   *
   * attempts của việc CHỈ tăng khi việc hỏng riêng (lượt có ảnh của việc khác tải được mà ảnh việc này vẫn lỗi chung / quá giờ),
   * về 0 khi việc đẩy được ít nhất 1 ảnh; lỗi chung của cả lượt (Hub / mạng / CDN chặn) không tính.
   */
  const runRound = async ({ profile, cfg, budgetMs, isAborted = () => false, roundId = null, clearHubFull: clear = false }) => {
    const startedAt = now();
    const deadline = startedAt + Math.max(0, Number(budgetMs) || 0);
    const round = emptyRound(roundId || newRoundId(now), startedAt);
    live = round;
    abortAll = false;
    const aborted = () => abortAll || Boolean(isAborted());
    const left = () => deadline - now();
    const addError = (itemId, id, reason) => {
      round.lastError = reason;
      if (round.errors.length < limits.maxErrors) round.errors.push({ itemId, id, reason });
    };
    const stop = (by, reason = null) => {
      round.stoppedBy = by;
      if (reason) round.lastError = reason;
    };
    // Cầu dao của cả lượt (2 ảnh song song, cùng một luồng sự kiện → đếm tuần tự).
    let cdnRequests = 0;
    let cdnOk = 0;          // số lần CDN trả lời bình thường (ảnh / 404) trong lượt
    let cdnTempStreak = 0;
    let webpStreak = 0;
    const suspects = [];    // seq các việc chỉ toàn lỗi chung trong lượt

    /** Đọc body tối đa limits.maxBytes (không có content-length vẫn chặn được ảnh khổng lồ). → Uint8Array | { tooLarge } */
    const readCapped = async (res) => {
      const reader = res.body?.getReader?.();
      if (!reader) {
        const bytes = new Uint8Array(await res.arrayBuffer());
        return bytes.length > limits.maxBytes ? { tooLarge: bytes.length } : bytes;
      }
      const parts = [];
      let total = 0;
      for (;;) {
        const { done, value } = await reader.read();
        if (done) break;
        total += value.length;
        if (total > limits.maxBytes) { try { await reader.cancel(); } catch (_) {} return { tooLarge: total }; }
        parts.push(value);
      }
      const out = new Uint8Array(total);
      let at = 0;
      for (const p of parts) { out.set(p, at); at += p.length; }
      return out;
    };

    /**
     * Tải 1 ảnh từ CDN + đẩy Hub. →
     * { done:"pushed"|"existed"|"failed", reason? } | { webp:true } (chờ quyết: lỗi riêng ảnh hay cầu dao) |
     * { skip:"cdnTemp"|"timeout", reason } (giữ ảnh cho lượt sau) | { stop, reason, code? } (dừng lượt).
     */
    const pushOne = async (id) => {
      if (aborted()) return { stop: "abort" };
      if (left() < limits.minStepMs) return { stop: "deadline" };
      if (cdnRequests >= limits.maxCdnPerRound) return { stop: "cdnCap", reason: `Đã gọi CDN ${cdnRequests} lần trong lượt — để lượt sau.` };
      cdnRequests++;
      const perImage = Math.min(limits.imageTimeoutMs, left());
      let got;
      let timedOut = false;
      try {
        got = await withTimeout(perImage, async (signal) => {
          signal.addEventListener("abort", () => { timedOut = true; });
          const res = await doFetch(CDN_BASE + id, {
            method: "GET", headers: { Accept: "image/jpeg,image/png" }, credentials: "omit", signal,
          });
          if (res.status !== 200) { try { await res.body?.cancel?.(); } catch (_) {} return { status: res.status }; }
          const len = Number(res.headers?.get?.("content-length"));
          if (Number.isFinite(len) && len > limits.maxBytes) {
            try { await res.body?.cancel?.(); } catch (_) {}
            return { status: 200, tooLarge: len };
          }
          const body = await readCapped(res);
          return body instanceof Uint8Array ? { status: 200, bytes: body } : { status: 200, tooLarge: body.tooLarge };
        });
      } catch (e) {
        if (aborted()) return { stop: "abort" };
        if (left() <= 0) return { stop: "deadline" };
        cdnTempStreak++;
        if (timedOut || e?.name === "AbortError") return { skip: "timeout", reason: `Ảnh ${id}: CDN quá ${Math.round(perImage / 1000)} s` };
        return { skip: "cdnTemp", reason: `Không tải được ảnh ${id} từ CDN (${e?.message || e})` };
      }
      const cdn = classifyCdn(got.status);
      if (cdn.kind === "temp") { cdnTempStreak++; return { skip: "cdnTemp", reason: `${cdn.reason} — ảnh ${id}` }; }
      if (cdn.kind === "bad") { cdnOk++; cdnTempStreak = 0; return { done: "failed", reason: cdn.reason }; }
      if (got.tooLarge !== undefined) {
        cdnOk++; cdnTempStreak = 0;
        return { done: "failed", reason: `Ảnh quá lớn (${got.tooLarge} byte > ${limits.maxBytes})` };
      }
      const type = detectImageType(got.bytes);
      if (type === null) {
        // 200 mà không phải ảnh (trang chặn / xác minh trả HTML…) — CDN đang chặn, không phải lỗi của ảnh.
        cdnTempStreak++;
        return { skip: "cdnTemp", reason: `CDN trả dữ liệu không phải ảnh (${got.bytes.length} byte) — ảnh ${id}` };
      }
      cdnOk++;
      cdnTempStreak = 0;
      if (type === "image/webp") { webpStreak++; return { webp: true }; }
      webpStreak = 0;

      if (aborted()) return { stop: "abort" };
      if (left() < limits.minStepMs) return { stop: "deadline" };
      let put;
      try {
        put = await withTimeout(Math.min(limits.putTimeoutMs, left()), async (signal) => {
          const res = await doFetch(normalizeHubUrl(cfg.url) + SOURCE_IMAGE_ROUTES.put + encodeURIComponent(id), {
            method: "PUT", headers: hubHeaders(cfg, { "Content-Type": type }), body: got.bytes, signal,
          });
          return classifyPut(res.status, await readJson(res));
        });
      } catch (e) {
        if (aborted()) return { stop: "abort" };
        if (left() <= 0) return { stop: "deadline" };
        return { stop: "temp", reason: `Không đẩy được ảnh lên Hub (${e?.name === "AbortError" ? "quá giờ" : e?.message || e})` };
      }
      if (put.kind === "ok") return { done: put.existed ? "existed" : "pushed" };
      if (put.kind === "bad") return { done: "failed", reason: put.reason };
      return { stop: put.kind, reason: put.reason, code: put.code };
    };

    /** Hỏi Hub id nào còn thiếu (chia ≤ 200 id / lần). → { missing:[…] } hoặc { stop, reason, code? } hoặc { bad, reason }. */
    const askMissing = async (ids) => {
      const missing = [];
      for (let i = 0; i < ids.length; i += limits.maxIdsPerCall) {
        const chunk = ids.slice(i, i + limits.maxIdsPerCall);
        if (aborted()) return { stop: "abort" };
        if (left() < limits.minStepMs) return { stop: "deadline" };
        let c;
        try {
          c = await withTimeout(Math.min(limits.missingTimeoutMs, left()), async (signal) => {
            const res = await doFetch(normalizeHubUrl(cfg.url) + SOURCE_IMAGE_ROUTES.missing, {
              method: "POST", headers: hubHeaders(cfg, { "Content-Type": "application/json" }), body: JSON.stringify({ ids: chunk }), signal,
            });
            return classifyMissing(res.status, await readJson(res));
          });
        } catch (e) {
          if (aborted()) return { stop: "abort" };
          if (left() <= 0) return { stop: "deadline" };
          return { stop: "temp", reason: `Không hỏi được Hub ảnh còn thiếu (${e?.name === "AbortError" ? "quá giờ" : e?.message || e})` };
        }
        if (c.kind === "bad") return { bad: true, reason: c.reason };
        if (c.kind !== "ok") return { stop: c.kind, reason: c.reason, code: c.code };
        const asked = new Set(chunk);
        for (const id of c.missing) if (asked.has(id)) missing.push(id);
      }
      return { missing: [...new Set(missing)] };
    };

    /** Một việc. → null (đi tiếp việc sau) hoặc { stop, reason, code? } (dừng lượt). */
    const runJob = async (job) => {
      if (job.attempts >= limits.maxAttempts) {
        round.dropped++;
        addError(job.itemId, null, `Bỏ ảnh SP ${job.itemId} sau ${job.attempts} lượt lỗi riêng${job.lastError ? `: ${job.lastError}` : ""}`);
        await updateJob(job.seq, () => "remove");
        return null;
      }
      const ids = job.ids.filter(isValidImageId);
      const asked = await askMissing(ids);
      if (asked.bad) {
        round.failed += ids.length;
        addError(job.itemId, null, asked.reason);
        await updateJob(job.seq, () => "remove");
        return null;
      }
      if (asked.stop) {
        if (asked.reason) await updateJob(job.seq, (j) => { j.lastError = asked.reason; });
        return asked;
      }
      round.checked += ids.length;
      round.present += ids.length - asked.missing.length;

      const timeouts = isObj(job.timeouts) ? { ...job.timeouts } : {};
      for (const k of Object.keys(timeouts)) if (!asked.missing.includes(k)) delete timeouts[k]; // đã lên Hub → bỏ đếm
      const done = new Set();
      const kept = [];       // lỗi chung: giữ cho lượt sau (thứ tự cũ)
      const late = [];       // quá giờ: xuống cuối danh sách
      const webp = [];       // WebP: lỗi riêng ảnh, trừ khi cầu dao WebP nhảy
      let jobPushed = 0;
      let jobTempOnly = 0;   // ảnh của việc này lỗi chung / quá giờ
      let lastJobReason = null;
      let halt = null;
      let next = 0;
      const worker = async () => {
        while (!halt && next < asked.missing.length) {
          const id = asked.missing[next++];
          const r = await pushOne(id);
          if (r.stop) { halt = halt || r; kept.push(id); continue; }
          if (r.webp) {
            webp.push(id);
            if (webpStreak >= limits.webpBreaker) {
              halt = halt || { stop: "cdnWebp", reason: `CDN trả WebP ${webpStreak} ảnh liên tiếp (bỏ qua Accept JPEG / PNG?) — dừng lượt, giữ ảnh.`, webp: true };
            }
            continue;
          }
          if (r.skip) {
            jobTempOnly++;
            lastJobReason = r.reason;
            round.lastError = r.reason;
            if (r.skip === "timeout") {
              timeouts[id] = (timeouts[id] || 0) + 1;
              if (timeouts[id] >= limits.imageTimeoutRounds) {
                delete timeouts[id];
                done.add(id);
                round.failed++;
                addError(job.itemId, id, `${r.reason} — ${limits.imageTimeoutRounds} lượt liền`);
              } else late.push(id);
            } else kept.push(id);
            if (cdnTempStreak >= limits.cdnTempBreaker) {
              halt = halt || { stop: "cdnTemp", reason: `CDN lỗi ${cdnTempStreak} lần liên tiếp — dừng lượt, giữ ảnh. Lần cuối: ${r.reason}` };
            }
            continue;
          }
          done.add(id);
          delete timeouts[id];
          if (r.done === "pushed") { round.pushed++; jobPushed++; }
          else if (r.done === "existed") { round.existed++; jobPushed++; }
          else { round.failed++; addError(job.itemId, id, r.reason); }
        }
      };
      await Promise.all(Array.from({ length: Math.max(1, limits.parallel) }, worker));

      // WebP chỉ là lỗi riêng ảnh khi cầu dao WebP KHÔNG nhảy (CDN chỉ đổi hành vi với vài ảnh).
      if (!halt?.webp) {
        for (const id of webp) { done.add(id); round.failed++; addError(job.itemId, id, "CDN trả WebP (Hub chỉ nhận JPEG / PNG)"); }
      }
      const untouched = asked.missing.slice(next).filter((id) => !kept.includes(id));
      const remaining = [...kept, ...(halt?.webp ? webp : []), ...untouched, ...late].filter((id) => !done.has(id));
      const remainingSet = new Set(remaining);
      const finalRemaining = [...remainingSet];
      if (finalRemaining.length === 0) {
        round.skipped += Number(job.skipped) || 0;
        await updateJob(job.seq, () => "remove");
        return halt;
      }
      // Nghi hỏng RIÊNG: ảnh việc này chỉ toàn lỗi chung / quá giờ, không ảnh nào lên — xét ở CUỐI lượt (CDN có trả ảnh của việc
      // khác thì mới tính, xem suspects).
      if (jobPushed === 0 && jobTempOnly > 0) suspects.push(job.seq);
      const rotate = halt && (halt.stop === "cdnTemp" || halt.stop === "cdnWebp");
      await updateJob(job.seq, (j) => {
        j.ids = finalRemaining;
        j.timeouts = Object.fromEntries(Object.entries(timeouts).filter(([k]) => remainingSet.has(k)));
        if (jobPushed > 0) j.attempts = 0;
        if (lastJobReason || halt?.reason) j.lastError = halt?.reason || lastJobReason;
        // Cầu dao nhảy giữa việc này: xuống cuối hàng để lượt sau bắt đầu bằng việc khác (một SP hỏng không chặn cả hàng đợi).
        if (rotate) j.addedAt = now();
      });
      return halt;
    };

    const refreshQueue = async () => {
      try { round.queue = countOf((await serial(readState)).jobs.filter((j) => j.profile === profile)); } catch (_) {}
    };

    try {
      if (!isHubConfigured(cfg)) {
        stop("nocfg", `Cấu hình Hub không dùng được: ${hubConfigProblem(cfg)}`);
      } else {
        if (clear) await clearHubFull();
        let s = await serial(readState);
        round.queue = countOf(s.jobs.filter((j) => j.profile === profile));
        if (s.hubFullUntil > now()) {
          round.hubFullCode = s.hubFullCode || "hub-full";
          stop("hubFull");
        } else {
          const seen = new Set();
          for (;;) {
            if (aborted()) { stop("abort"); break; }
            if (left() < limits.minStepMs) { stop("deadline"); break; }
            s = await serial(readState);
            const job = s.jobs.filter((j) => j.profile === profile && !seen.has(j.seq))
              .sort((a, b) => a.addedAt - b.addedAt || a.seq - b.seq)[0];
            if (!job) break;
            seen.add(job.seq);
            const halt = await runJob(job);
            await refreshQueue();
            if (!halt) continue;
            stop(halt.stop, halt.reason || null);
            if (halt.stop === "hubFull") {
              round.hubFullCode = halt.code || "hub-full";
              const until = now() + limits.hubFullPauseMs;
              await serial(async () => {
                const st = await readState();
                st.hubFullUntil = until;
                st.hubFullCode = round.hubFullCode;
                await writeState(st);
              });
            }
            break;
          }
        }
      }
      // CDN có trả ảnh trong lượt (không phải chặn toàn bộ) → các việc chỉ toàn lỗi chung là hỏng riêng: tính 1 lần lỗi riêng.
      if (cdnOk > 0) {
        for (const seq of suspects) await updateJob(seq, (j) => { j.attempts = (Number(j.attempts) || 0) + 1; });
      }
    } catch (e) {
      stop("temp", `Lỗi hàng đợi ảnh gốc: ${e?.message || e}`);
    }

    try {
      const s = await serial(async () => {
        const st = await readState();
        if (st.droppedFull) { round.dropped += st.droppedFull; st.droppedFull = 0; await writeState(st); }
        return st;
      });
      round.queue = countOf(s.jobs.filter((j) => j.profile === profile));
    } catch (_) {}
    round.endedAt = now();
    lastRound = round;
    live = null;
    changed();
    return copyRound(round);
  };

  /**
   * Trạng thái cho app / popup: lượt đang chạy (số tới lúc hỏi) hoặc lượt gần nhất, hàng đợi (mọi profile) + theo từng profile
   * (app đọc `profiles.auto.ids` làm "còn thiếu" cả khi không có lượt nào), mốc Hub hết chỗ.
   */
  const status = async () => {
    let s = emptyState();
    try { s = await serial(readState); } catch (_) {}
    const profiles = {};
    for (const p of new Set(s.jobs.map((j) => j.profile))) profiles[p] = countOf(s.jobs.filter((j) => j.profile === p));
    return {
      running: live !== null,
      round: copyRound(live || lastRound),
      queue: countOf(s.jobs),
      profiles,
      hubFullUntil: s.hubFullUntil > now() ? s.hubFullUntil : 0,
      hubFullCode: s.hubFullUntil > now() ? s.hubFullCode : null,
    };
  };

  /** Lệnh Dừng: huỷ lượt đang chạy ngay (huỷ cả request đang bay); việc dở giữ nguyên cho lượt sau. */
  const abort = () => {
    if (!live) return false;
    abortAll = true;
    for (const c of inflight) { try { c.abort(); } catch (_) {} }
    return true;
  };

  return { enqueue, runRound, status, abort, clearHubFull, isRunning: () => live !== null };
}
