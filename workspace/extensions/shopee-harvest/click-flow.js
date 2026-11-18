// Phần THUẦN của luồng "bấm nút Lấy dữ liệu" (GĐ7): từ kết quả bước lấy JSON (__launcherExecuteHarvestStep) → làm sạch →
// dựng mục Hub → gửi Hub → (lỗi tạm) vào hàng đợi; và ánh xạ kết quả ra trạng thái cho hộp nổi. Không đụng chrome.*: cấu
// hình / quyền / hàng đợi / fetch truyền vào (test Node).

import { cleanPayload, summarize, rawHash, buildHubItem, toJson } from "./pdp-clean.js";
import { postBatch, isHubConfigured, normalizeHubUrl, hubConfigProblem } from "./hub-client.js";
import { extractProductIds } from "./pdp-core.js";

/**
 * Trạng thái gửi Hub đính vào kết quả bước (`result.hub.status`) — app Harvest (HarvestStepResult.HubPush) đọc đúng các chữ này:
 * - saved: Hub đã nhận (ok=true, kể cả trùng hash) → app ghi done.
 * - queued: Hub chưa nhận (mất mạng / 5xx / 503…), mục đã nằm BỀN trong hàng đợi extension, tự gửi lại → app KHÔNG ghi done.
 * - rejected: Hub từ chối dữ liệu (ok=false / 4xx) → lỗi dòng, lượt sau lấy lại.
 * - invalid: JSON không làm sạch / đọc được (lỗi nội dung của SP) → lỗi vĩnh viễn như HarvestItemRejectedException.
 * - failed: không gửi được và KHÔNG giữ được (chưa cấu hình / thiếu quyền / token sai ở chế độ tự động / hàng đợi đầy).
 */
export const HUB_STATUS = Object.freeze({
  saved: "saved", queued: "queued", rejected: "rejected", invalid: "invalid", failed: "failed",
});

/** Link SP làm sạch khi bấm tay (không có link từ sheet): https://shopee.vn/product/{shop}/{item}. */
export const canonicalProductUrl = (summary) => `https://shopee.vn/product/${summary.shopId}/${summary.itemId}`;

/** Khoá khử trùng trong hàng đợi: cùng SP + cùng nguồn (bấm tay: không nguồn). */
export const queueKey = (profile, dto) => `${profile}|${dto.itemId}|${dto.accountId ?? ""}|${dto.sheet ?? ""}`;

/**
 * Đẩy SP vừa lấy được lên Hub. step = kết quả __launcherExecuteHarvestStep; ctx = ngữ cảnh chế độ tự động do app truyền
 * ({ auto:true, hub:{url,token,machineId}, accountId, sheet, rowNo, url, sourceUrl }) hoặc null khi bấm tay.
 * deps = { manualConfig: () → Promise<cfg|null>, hasPermission: (cfg) → Promise<bool>, queue, fetchImpl?, now? }.
 * Trả kết quả bước kèm `hub: { status, reason, applied }` và `json` = chuỗi ĐÃ làm sạch. Bước không lấy được JSON → trả nguyên.
 * KHÔNG ném (mọi lỗi thành hub.status).
 */
export async function pushCaptured(step, ctx, deps) {
  if (!step || !step.ok || typeof step.json !== "string" || !step.json) return step;
  const done = (status, reason = null, extra = {}) => ({ ...step, ...extra, hub: { status, reason, applied: false, ...(extra.hub || {}) } });

  let clean;
  let summary;
  try {
    clean = cleanPayload(step.json);
    summary = summarize(clean);
  } catch (e) {
    return done(HUB_STATUS.invalid, e?.message || String(e));
  }
  if (step.itemId != null && String(step.itemId) !== summary.itemId.toString()) {
    return done(HUB_STATUS.invalid, `JSON là SP ${summary.itemId}, không phải SP ${step.itemId} đang xem.`);
  }
  // shop_id trong JSON phải khớp shop trên URL trang đang xem (khi URL có shop): chặn rẻ JSON giả / lạc từ script khác trên
  // trang. Lệch có thể là link cũ của SP đã đổi shop → lỗi TẠM (rejected, không vào hàng đợi), không đánh lỗi vĩnh viễn.
  const pageShop = extractProductIds(String(step.pageUrl || ""))?.shopId ?? null;
  if (pageShop && summary.shopId > 0n && summary.shopId.toString() !== pageShop) {
    return done(HUB_STATUS.rejected, `JSON là SP của shop ${summary.shopId}, trang đang xem là shop ${pageShop} — không gửi Hub.`);
  }

  const auto = Boolean(ctx?.auto);
  const profile = auto ? "auto" : "manual";
  let cfg;
  try { cfg = auto ? ctx.hub : await deps.manualConfig(); } catch (_) { cfg = null; }
  const withJson = { json: clean };
  if (!isHubConfigured(cfg)) {
    const why = hubConfigProblem(cfg);
    return done(HUB_STATUS.failed, auto
      ? `Cấu hình Hub app truyền không dùng được: ${why}.`
      : `Chưa cấu hình Hub dùng được (${why}) — mở Cài đặt của extension.`, withJson);
  }
  let allowed = false;
  try { allowed = await deps.hasPermission(cfg); } catch (_) {}
  if (!allowed) {
    return done(HUB_STATUS.failed,
      `Extension chưa được cấp quyền truy cập ${normalizeHubUrl(cfg.url)} — mở Cài đặt của extension, bấm Lưu để cấp.`, withJson);
  }

  let hash;
  try { hash = await rawHash(clean); } catch (e) { return done(HUB_STATUS.failed, `Không tính được raw_hash (${e?.message || e}).`, withJson); }
  const dto = buildHubItem({
    clean, hash, summary,
    url: auto && ctx.url ? ctx.url : canonicalProductUrl(summary),
    sourceUrl: auto ? ctx.sourceUrl : null,
    accountId: auto ? ctx.accountId : null,
    sheet: auto ? ctx.sheet : null,
    rowNo: auto ? ctx.rowNo : null,
    harvestedAtMs: Number(step.capturedAt) || (deps.now ? deps.now() : Date.now()),
    fallbackShopId: step.shopId,
  });
  const body = toJson(dto);

  const key = queueKey(profile, dto);
  const itemId = summary.itemId.toString();
  // Bấm tay mà Hub từ chối ngay lần đầu: vẫn giữ SP trong hàng đợi ở trạng thái LỖI (kèm lý do) để popup hiện + "Gửi lại";
  // tự động thì không (app tự lấy lại lượt sau theo luật GĐ6).
  const rejected = async (reason) => {
    if (auto) return done(HUB_STATUS.rejected, reason, withJson);
    const q = await deps.queue.enqueue({ profile, key, itemId, body, status: "error", lastError: `Hub từ chối: ${reason}` });
    return done(HUB_STATUS.rejected, q.ok ? `${reason} — đã giữ trong hàng đợi (popup → Gửi lại)` : `${reason} — ${q.reason}`, withJson);
  };

  const res = await postBatch(cfg, [body], { fetchImpl: deps.fetchImpl, now: deps.now });
  if (res.kind === "ok") {
    const r = res.results[0];
    return r.ok
      ? done(HUB_STATUS.saved, null, { ...withJson, hub: { applied: Boolean(r.applied) } })
      : rejected(r.reason || "Hub từ chối mục");
  }
  if (res.kind === "bad") return rejected(res.reason);
  // Token sai ở chế độ tự động: lỗi cấu hình của app (client của app cũng hỏng) — không dồn vào hàng đợi, app dừng job.
  if (res.kind === "auth" && auto) return done(HUB_STATUS.failed, res.reason, withJson);

  // temp / auth (bấm tay) / nocfg → giữ BỀN trong hàng đợi, tự gửi lại.
  const q = await deps.queue.enqueue({ profile, key, itemId, body });
  return q.ok
    ? done(HUB_STATUS.queued, res.reason, withJson)
    : done(HUB_STATUS.failed, `${res.reason} — ${q.reason}`, withJson);
}

/**
 * Trạng thái hiển thị của một kết quả (hộp nổi): { status, reason }. Thứ tự: gửi Hub → huỷ → đăng nhập → captcha → traffic →
 * proxy → không phải trang SP → lỗi get_pc → lỗi khác.
 */
export function outcomeOf(result) {
  if (!result) return { status: "error", reason: "Không có kết quả." };
  if (result.hub?.status) return { status: result.hub.status, reason: result.hub.reason || null, applied: Boolean(result.hub.applied) };
  if (result.superseded || result.aborted) return { status: "error", reason: result.superseded ? "Bị lần bấm khác thay." : "Đã huỷ." };
  if (result.loginRequired) return { status: "loginRequired", reason: null };
  if (result.captcha && !result.ok) return { status: "captcha", reason: null };
  if (result.trafficError) return { status: "traffic", reason: result.pdpError != null ? `error=${result.pdpError}` : null };
  if (result.proxyError) return { status: "proxy", reason: null };
  if (result.notProduct) return { status: "notProduct", reason: null };
  if (result.pdpError != null) return { status: "pdpError", reason: `error=${result.pdpError}` };
  return { status: "error", reason: result.message || "Không lấy được dữ liệu." };
}
