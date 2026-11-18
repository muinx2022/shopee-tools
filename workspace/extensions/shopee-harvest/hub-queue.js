// Hàng đợi gửi Hub của extension (GĐ7) — SP đã lấy + làm sạch mà Hub chưa nhận (mất mạng, Hub 5xx / 503 pg-unavailable,
// token sai…) nằm ở đây, gửi lại theo chrome.alarms với lùi dần. Không đụng chrome.* trực tiếp: storage / đồng hồ / hàm gửi
// truyền vào (test Node).
//
// Lưu trong chrome.storage.local: khoá INDEX_KEY = { seq, items:[meta…], backoffs:{profile:{failures,nextAt,lastError}},
// singles:{profile:true} } và mỗi mục một khoá ITEM_PREFIX+id = chuỗi JSON HarvestedProductItem (60–80 KB) — đổi trạng thái
// không phải ghi lại cả kho. chrome.storage.local không có unlimitedStorage (quyền tối thiểu) → trần ~10 MB: giới hạn ở
// LIMITS, đầy thì từ chối nhận thêm (người gọi báo lỗi rõ, KHÔNG âm thầm bỏ mục cũ).
//
// Profile = bộ cấu hình Hub dùng để gửi ("manual": trang Cài đặt; "auto": cấu hình app Harvest truyền). Mỗi profile lùi dần
// RIÊNG: token tay sai / chưa cấu hình không được chặn các mục của app (và ngược lại).
//
// Luật (như outbox C#, gọn hơn):
// - 200 + mục ok → xoá; mục ok=false (Hub từ chối dữ liệu) → trạng thái "error", giữ để người dùng xem, nút "Gửi lại".
// - mất mạng / 5xx / 408 / 429 / 401 / 403 / chưa cấu hình → giữ nguyên, lùi dần profile đó (BACKOFF_STEPS_MS), không đặt
//   trần số lần; profile khác gửi tiếp.
// - 413 / batch-too-large với lô > 1 mục → profile đó gửi LẺ từng mục cho tới khi hết mục chờ (không xen lô / lẻ); 4xx khác
//   (hoặc 413 của một mục lẻ) → "error".

export const INDEX_KEY = "hubQueue";
export const ITEM_PREFIX = "hubQueue:item:";

export const LIMITS = Object.freeze({
  maxItems: 100,
  maxBytes: 6_000_000,
  maxItemBytes: 2_000_000,
  batchSize: 5,
  batchesPerFlush: 10,
});

/** Lùi dần sau mỗi lần gửi hỏng liên tiếp (chrome.alarms không hẹn được dưới ~30 s). */
export const BACKOFF_STEPS_MS = Object.freeze([30_000, 60_000, 120_000, 300_000, 600_000, 900_000]);

export const backoffDelay = (failures) =>
  BACKOFF_STEPS_MS[Math.min(Math.max(1, failures), BACKOFF_STEPS_MS.length) - 1];

const emptyIndex = () => ({ seq: 0, items: [], backoffs: {}, singles: {} });
const NO_BACKOFF = Object.freeze({ failures: 0, nextAt: 0, lastError: null });

/**
 * @param storage { get(keys[]) → Promise<obj>, set(obj) → Promise, remove(keys[]) → Promise } (chrome.storage.local)
 * @param send (profile, bodies[]) → Promise<{ kind:"ok", results } | { kind:"temp"|"auth"|"nocfg", reason } | { kind:"bad", reason, tooLarge }>
 * @param now () → ms
 * @param onChange () → void, gọi sau mỗi lần đổi kho (popup / hộp nổi làm mới số).
 */
export function createHubQueue({ storage, send, now = Date.now, onChange = () => {}, limits = LIMITS }) {
  // Mọi thao tác đọc-sửa-ghi INDEX chạy nối tiếp (SW chạy song song lần bấm, alarm, nút Gửi lại).
  let chain = Promise.resolve();
  const serial = (fn) => {
    const run = chain.then(fn, fn);
    chain = run.catch(() => {});
    return run;
  };
  const readIndex = async () => {
    const got = await storage.get([INDEX_KEY]);
    const idx = got?.[INDEX_KEY];
    if (!idx || !Array.isArray(idx.items)) return emptyIndex();
    return {
      seq: Number(idx.seq) || 0,
      items: idx.items,
      backoffs: idx.backoffs && typeof idx.backoffs === "object" ? idx.backoffs : {},
      singles: idx.singles && typeof idx.singles === "object" ? idx.singles : {},
    };
  };
  const writeIndex = async (idx) => { await storage.set({ [INDEX_KEY]: idx }); };
  const changed = () => { try { onChange(); } catch (_) {} };
  const bytesOf = (s) => new TextEncoder().encode(s).length;
  const backoffOf = (idx, profile) => idx.backoffs[profile] || NO_BACKOFF;
  const pendingProfiles = (idx) => [...new Set(idx.items.filter((m) => m.status === "pending").map((m) => m.profile))];

  /**
   * Nhận một mục. key = định danh để KHỬ TRÙNG (cùng SP + cùng nguồn): mục cũ cùng key bị thay bằng bản mới. status
   * "error" + lastError: giữ mục Hub vừa từ chối để người dùng xem / "Gửi lại" (không tự gửi). Trả { ok:true, id } hoặc
   * { ok:false, reason } khi đầy / quá cỡ / ghi kho lỗi — người gọi phải báo lỗi, không coi là đã nhận.
   */
  const enqueue = ({ profile, key, itemId, body, status = "pending", lastError = null }) => serial(async () => {
    const bytes = bytesOf(body);
    if (bytes > limits.maxItemBytes) return { ok: false, reason: `Mục quá lớn (${bytes} byte) cho hàng đợi.` };
    const idx = await readIndex();
    const old = idx.items.find((m) => m.key === key);
    const others = idx.items.filter((m) => m !== old);
    const usedBytes = others.reduce((s, m) => s + m.bytes, 0);
    if (others.length + 1 > limits.maxItems || usedBytes + bytes > limits.maxBytes) {
      return { ok: false, reason: `Hàng đợi gửi Hub đã đầy (${others.length} mục, ${Math.round(usedBytes / 1024)} KB) — kiểm tra kết nối Hub rồi bấm "Gửi lại".` };
    }
    const id = old ? old.id : ++idx.seq;
    const meta = {
      id, profile, key, itemId: String(itemId ?? ""), bytes, addedAt: now(),
      status: status === "error" ? "error" : "pending", attempts: 0, lastError: status === "error" ? lastError : null,
    };
    try {
      await storage.set({ [ITEM_PREFIX + id]: body });
      idx.items = [...others, meta];
      await writeIndex(idx);
    } catch (e) {
      return { ok: false, reason: `Không ghi được hàng đợi (${e?.message || e}).` };
    }
    changed();
    return { ok: true, id };
  });

  const stats = async () => {
    const idx = await readIndex();
    const pending = idx.items.filter((m) => m.status === "pending");
    const profiles = pendingProfiles(idx);
    const due = profiles.map((p) => backoffOf(idx, p));
    const waiting = due.filter((b) => b.nextAt > 0);
    const latest = due.filter((b) => b.lastError).sort((a, b) => b.nextAt - a.nextAt)[0];
    return {
      pending: pending.length,
      error: idx.items.length - pending.length,
      bytes: idx.items.reduce((s, m) => s + m.bytes, 0),
      nextAt: waiting.length ? Math.min(...waiting.map((b) => b.nextAt)) : 0,
      lastError: latest ? latest.lastError : null,
    };
  };

  const list = async () => (await readIndex()).items.map((m) => ({ ...m }));

  /** Mốc hẹn lần gửi kế (để đặt alarm): profile có mục chờ tới hạn sớm nhất; 0 = không còn mục chờ. */
  const nextAlarmAt = async () => {
    const idx = await readIndex();
    const profiles = pendingProfiles(idx);
    if (profiles.length === 0) return 0;
    return Math.max(Math.min(...profiles.map((p) => backoffOf(idx, p).nextAt)), now());
  };

  /**
   * Gửi các mục "pending" của các profile đã tới hạn lùi dần (force: bỏ qua hạn). Profile gặp lỗi tạm / token / cấu hình
   * thì hoãn RIÊNG nó, profile khác gửi tiếp. Trả số liệu lượt gửi; deferred = không gửi gì vì mọi mục chờ chưa tới hạn.
   */
  const flush = ({ force = false } = {}) => serial(async () => {
    const out = { sent: 0, saved: 0, rejected: 0, deferred: false, lastError: null };
    const idx = await readIndex();
    const blocked = new Set(); // profile vừa hỏng trong lượt này
    let notDue = false;
    for (let round = 0; round < limits.batchesPerFlush; round++) {
      const all = idx.items.filter((m) => m.status === "pending" && !blocked.has(m.profile));
      const pending = all.filter((m) => force || backoffOf(idx, m.profile).nextAt <= now())
        .sort((a, b) => a.addedAt - b.addedAt || a.id - b.id);
      if (pending.length < all.length) notDue = true;
      if (pending.length === 0) break;
      const profile = pending[0].profile;
      const batch = pending.filter((m) => m.profile === profile).slice(0, idx.singles[profile] ? 1 : limits.batchSize);
      const got = await storage.get(batch.map((m) => ITEM_PREFIX + m.id));
      const live = batch.filter((m) => typeof got?.[ITEM_PREFIX + m.id] === "string");
      if (live.length < batch.length) {
        // Thân mục mất (kho hỏng / bị xoá tay): bỏ meta mồ côi, không gửi rỗng.
        const lost = new Set(batch.filter((m) => !live.includes(m)).map((m) => m.id));
        idx.items = idx.items.filter((m) => !lost.has(m.id));
        await writeIndex(idx);
        continue;
      }
      const res = await send(profile, live.map((m) => got[ITEM_PREFIX + m.id]));
      out.sent += live.length;
      if (res.kind === "ok") {
        const drop = [];
        for (const r of res.results) {
          const m = live[r.index];
          if (!m) continue;
          if (r.ok) { drop.push(m.id); out.saved++; }
          else { m.status = "error"; m.lastError = `Hub từ chối: ${r.reason || "không rõ lý do"}`; out.rejected++; }
        }
        idx.items = idx.items.filter((m) => !drop.includes(m.id));
        delete idx.backoffs[profile];
        await writeIndex(idx);
        if (drop.length) await storage.remove(drop.map((id) => ITEM_PREFIX + id));
        continue;
      }
      if (res.kind === "bad") {
        if (res.tooLarge && live.length > 1) {
          idx.singles[profile] = true; // lô quá cỡ → gửi lẻ tới khi hết mục chờ, không đánh lỗi mục nào
          await writeIndex(idx);
          continue;
        }
        for (const m of live) { m.status = "error"; m.lastError = res.reason; out.rejected++; }
        await writeIndex(idx);
        continue;
      }
      // temp / auth / nocfg: giữ nguyên, lùi dần RIÊNG profile này, profile khác gửi tiếp.
      for (const m of live) { m.attempts++; m.lastError = res.reason; }
      const failures = backoffOf(idx, profile).failures + 1;
      idx.backoffs[profile] = { failures, nextAt: now() + backoffDelay(failures), lastError: res.reason };
      blocked.add(profile);
      out.lastError = res.reason;
      await writeIndex(idx);
    }
    // Chế độ gửi lẻ chỉ hết khi profile không còn mục chờ.
    const still = new Set(pendingProfiles(idx));
    let dirty = false;
    for (const p of Object.keys(idx.singles)) if (!still.has(p)) { delete idx.singles[p]; dirty = true; }
    if (dirty) await writeIndex(idx);
    out.deferred = out.sent === 0 && notDue;
    changed();
    return out;
  });

  /** Nút "Gửi lại": mục lỗi về "pending", bỏ lùi dần mọi profile. */
  const requeueErrors = () => serial(async () => {
    const idx = await readIndex();
    let n = 0;
    for (const m of idx.items) if (m.status === "error") { m.status = "pending"; m.attempts = 0; m.lastError = null; n++; }
    idx.backoffs = {};
    await writeIndex(idx);
    changed();
    return n;
  });

  return { enqueue, stats, list, flush, requeueErrors, nextAlarmAt };
}
