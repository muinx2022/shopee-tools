// Popup: hiển thị trạng thái launcher + kho PDP của tab Shopee (chốt chặn chạy tay: mở link SP, bấm Xuất JSON).
//
// pdp-core.js nạp ĐỘNG trong try: file đó hỏng (vd đang sửa dở khi nạp extension) thì popup vẫn chạy và báo rõ lỗi,
// thay vì chết cả module và đứng mãi ở "Đang đọc…". Mọi lời gọi chrome.* ở đây có trần thời gian.

const POPUP_CALL_TIMEOUT_MS = 3_000;

/** Promise có trần thời gian; quá hạn / lỗi thì ném lỗi mang tên bước để hiện cho người dùng. */
const timed = (promise, step, ms = POPUP_CALL_TIMEOUT_MS) => new Promise((resolve, reject) => {
  const timer = setTimeout(() => reject(new Error(`${step}: quá ${ms / 1000}s không phản hồi`)), ms);
  Promise.resolve(promise).then(
    (v) => { clearTimeout(timer); resolve(v); },
    (e) => { clearTimeout(timer); reject(new Error(`${step}: ${e?.message || e}`)); },
  );
});

document.addEventListener("DOMContentLoaded", async () => {
  const sheetDisplay = document.getElementById("sheetDisplay");
  const startRowDisplay = document.getElementById("startRowDisplay");
  const endRowDisplay = document.getElementById("endRowDisplay");
  const statusEl = document.getElementById("status");
  const progressFill = document.getElementById("progressFill");
  const statsLine = document.getElementById("statsLine");
  const pdpSummary = document.getElementById("pdpSummary");
  const pdpDiag = document.getElementById("pdpDiag");
  const pdpList = document.getElementById("pdpList");
  const exportBtn = document.getElementById("exportBtn");
  const refreshBtn = document.getElementById("refreshBtn");
  const exportStatus = document.getElementById("exportStatus");

  let core = null;
  let coreError = "";
  try {
    core = await import("./pdp-core.js");
  } catch (err) {
    coreError = `Nạp pdp-core.js lỗi: ${err?.message || err}`;
  }

  const setStatus = (text) => { statusEl.textContent = text || ""; };

  const setProgress = (current, total) => {
    const pct = total > 0 ? Math.min(100, Math.round((current / total) * 100)) : 0;
    progressFill.style.width = `${pct}%`;
  };

  const formatEndRow = (value) => {
    if (value == null || value === "" || Number(value) <= 0) return "Hết file";
    return String(value);
  };

  const applyDisplay = (state, lastConfig) => {
    const sheet = state.sheetName || state.lastSheetName || lastConfig.sheetName || "—";
    const startRow = state.startRow ?? lastConfig.startRow ?? "—";
    const endRow = state.endRow ?? lastConfig.endRow;

    sheetDisplay.textContent = sheet || "—";
    startRowDisplay.textContent = startRow !== "" && startRow != null ? String(startRow) : "—";
    endRowDisplay.textContent = formatEndRow(endRow);

    const current = state.currentLinkIndex || 0;
    const total = state.totalLinks || 0;
    setProgress(current, total);

    if (state.running) {
      const phase = state.phase === "waiting" ? "Đang nghỉ" : "Đang chạy";
      const currentRow = state.currentRow ?? "—";
      const end = formatEndRow(endRow);
      setStatus(
        `${phase} (launcher)\nSheet: ${sheet}\nDòng: ${currentRow}/${end}\nLink: ${current}/${total || "?"}\n${state.lastMessage || ""}`.trim()
      );
      return;
    }

    const phaseLabel =
      state.phase === "stopped" ? "Đã dừng (launcher)." :
      state.phase === "finished" ? "Hoàn tất." :
      state.lastMessage || "Đang chờ launcher.";

    const lastRow = state.lastCompletedRow ?? state.currentRow ?? "—";
    setStatus(`${phaseLabel}\nSheet: ${sheet}\nDòng cuối: ${lastRow}`);
  };

  const applyStats = (stats) => {
    if (!stats) { statsLine.textContent = ""; return; }
    statsLine.textContent =
      `Nguồn dữ liệu: bắt ngay ${stats.hook || 0} · bắt sau tải lại ${stats.hookReload || 0} · ` +
      `số lần tải lại ${stats.reload || 0} · bị chặn ${stats.blocked || 0} · không lấy được ${stats.miss || 0}`;
  };

  const readLocal = async (key) => (await timed(chrome.storage.local.get(key), `Đọc storage ${key}`))[key];

  const renderState = async () => {
    try {
      const state = (await readLocal("runnerState")) || { running: false };
      const lastConfig = (await readLocal("lastRunConfig")) || {};
      applyDisplay(state, lastConfig);
      applyStats(await readLocal("harvestStats"));
    } catch (err) {
      setStatus(`Không đọc được trạng thái: ${err?.message || err}`);
    }
  };

  // ── PDP đã bắt (chốt chặn chạy tay: mở vài link SP rồi bấm Xuất JSON để có fixture) ───────────────
  const isShopeeUrl = (url) => {
    try { return /(^|\.)shopee\.vn$/i.test(new URL(url).hostname); } catch (_) { return false; }
  };

  // Tab hiện hành nếu là Shopee; không thì tab Shopee dùng gần nhất.
  const findShopeeTab = async () => {
    const [active] = await timed(chrome.tabs.query({ active: true, currentWindow: true }), "Tìm tab hiện hành");
    if (active?.id && isShopeeUrl(active.url || "")) return active;
    const tabs = (await timed(chrome.tabs.query({}), "Tìm tab Shopee")).filter((t) => t.id && isShopeeUrl(t.url || ""));
    tabs.sort((a, b) => (b.lastAccessed || 0) - (a.lastAccessed || 0));
    return tabs[0] || null;
  };

  let entries = [];

  // Đọc kho ở world ISOLATED (pdp-store.js). hasStore=false: content script ISOLATED chưa chạy ở document này.
  const readStore = async (tabId) => {
    const [res] = await timed(chrome.scripting.executeScript({
      target: { tabId },
      world: "ISOLATED",
      func: () => {
        const store = globalThis.__shopeeHarvestStore;
        return store ? { hasStore: true, ...store.list() } : { hasStore: false };
      },
    }), "Đọc kho PDP trên tab");
    return res?.result || { hasStore: false };
  };

  const renderPdp = async () => {
    entries = [];
    pdpList.textContent = "";
    pdpDiag.textContent = "";
    exportBtn.disabled = true;
    pdpSummary.textContent = "Đang đọc…";
    try {
      const tab = await findShopeeTab();
      if (!tab) {
        pdpSummary.textContent = "Không có tab Shopee nào đang mở.";
        return;
      }
      const urlItemId = core ? core.extractProductIds(tab.url || "")?.itemId || "(không nhận ra)" : "(?)";
      const store = await readStore(tab.id);
      if (!store.hasStore) {
        pdpSummary.textContent = "Tab Shopee chưa có pdp-store.js (reload extension rồi F5 tab).";
        pdpDiag.textContent = `itemId từ URL: ${urlItemId}`;
        return;
      }
      const st = store.status || {};
      const errs = store.errors || [];
      pdpDiag.textContent =
        `Hook: ${st.hookReady ? "đã gắn" : "CHƯA gắn (không nhận tin ready)"} · get_pc thấy ${st.seen || 0}, ` +
        `bỏ ${st.dropped || 0} · itemId từ URL: ${urlItemId}` +
        (errs.length ? `\nBị từ chối: ${errs.map((e) => `${e.itemId} (error=${e.error})`).join(", ")}` : "");
      entries = (store.entries || []).slice().sort((a, b) => b.at - a.at);
      pdpSummary.textContent = `${entries.length} SP trên tab: ${tab.title || tab.url || ""}`.slice(0, 160);
      for (const e of entries) {
        const div = document.createElement("div");
        div.className = "pdp-item";
        const kb = Math.round(e.body.length / 1024);
        div.textContent = `${e.itemId} · ${kb} KB · ${e.via} · ${new Date(e.at).toLocaleTimeString("vi-VN")}`;
        pdpList.appendChild(div);
      }
      exportBtn.disabled = entries.length === 0 || !core;
    } catch (err) {
      pdpSummary.textContent = `Lỗi — ${err?.message || err}`;
    }
  };

  // Tải từng body ra pdp-<itemId>.json (chrome.downloads — popup tự đóng không làm hỏng lượt tải như thẻ <a>).
  const exportEntries = async () => {
    exportBtn.disabled = true;
    let done = 0;
    const errors = [];
    for (const e of entries) {
      try {
        // LUÔN cắt riêng tư — không có đường xuất body chưa cắt. redactPrivate ĐÓNG AN TOÀN: cắt lỗi thì NÉM →
        // vào catch, KHÔNG tải file (dữ liệu riêng tư không lọt ra).
        const body = core.redactPrivate(e.body);
        await timed(chrome.downloads.download({
          url: `data:application/json;charset=utf-8,${encodeURIComponent(body)}`,
          filename: `pdp-${e.itemId}.json`,
          conflictAction: "overwrite",
          saveAs: false,
        }), `Tải pdp-${e.itemId}.json`, 10_000);
        done++;
      } catch (err) {
        errors.push(`${e.itemId}: ${err?.message || err}`);
      }
    }
    exportStatus.textContent =
      `Đã xuất ${done}/${entries.length} file vào thư mục Tải xuống.` + (errors.length ? `\nLỗi: ${errors.join("; ")}` : "");
    exportBtn.disabled = entries.length === 0;
  };

  exportBtn.addEventListener("click", () => { exportEntries(); });
  refreshBtn.addEventListener("click", () => { renderPdp(); });

  // ── Gửi Hub (GĐ7): cấu hình, hàng đợi chờ gửi / lỗi, nút Gửi lại ───────────────────────────────────────────────
  const hubConfigEl = document.getElementById("hubConfig");
  const hubQueueLine = document.getElementById("hubQueueLine");
  const hubErrors = document.getElementById("hubErrors");
  const hubRetryBtn = document.getElementById("hubRetryBtn");
  const hubStatus = document.getElementById("hubStatus");

  const askBackground = (msg) => timed(chrome.runtime.sendMessage(msg), "Hỏi background", 10_000);

  const renderHub = async () => {
    try {
      const cfg = await readLocal("hubSettings");
      hubConfigEl.textContent = cfg?.url && cfg?.token
        ? `Hub: ${cfg.url} · mã máy ${cfg.machineId || "?"}`
        : "Chưa cấu hình Hub cho bấm tay — bấm \"Cài đặt Hub\".";
      const items = (await askBackground({ type: "HUB_QUEUE", op: "list" })) || [];
      const waiting = items.filter((m) => m.status === "pending");
      const failed = items.filter((m) => m.status !== "pending");
      const lastErr = waiting.find((m) => m.lastError)?.lastError;
      hubQueueLine.textContent = `Hàng đợi: ${waiting.length} chờ gửi · ${failed.length} lỗi` + (lastErr ? `\nLần gửi gần nhất: ${lastErr}` : "");
      hubErrors.textContent = "";
      for (const m of failed.slice(0, 30)) {
        const div = document.createElement("div");
        div.className = "pdp-item";
        div.textContent = `SP ${m.itemId} · ${m.lastError || "lỗi"}`;
        hubErrors.appendChild(div);
      }
      hubRetryBtn.disabled = items.length === 0;
    } catch (err) {
      hubQueueLine.textContent = `Không đọc được hàng đợi — ${err?.message || err}`;
    }
    await renderSourceImages();
  };

  // GĐ9: ảnh gốc lên Hub — 1 dòng: còn thiếu (số ảnh trong các việc chờ) + lượt gần nhất.
  const sourceImagesLine = document.getElementById("sourceImagesLine");
  const renderSourceImages = async () => {
    try {
      const st = await askBackground({ type: "SOURCE_IMAGES", op: "status" });
      const r = st?.round;
      let text = `Ảnh gốc: còn thiếu ${st?.queue?.ids ?? 0}`;
      if (r) text += ` · lượt ${st.running ? "đang chạy" : "gần nhất"} đẩy ${r.pushed} / lỗi ${r.failed}`;
      if (st?.hubFullUntil) text += ` · Hub hết chỗ (${st.hubFullCode || "507"}) — tạm dừng đẩy ảnh`;
      if (r?.lastError) text += `\nLỗi gần nhất: ${r.lastError}`;
      sourceImagesLine.textContent = text;
    } catch (err) {
      sourceImagesLine.textContent = `Ảnh gốc: không đọc được trạng thái — ${err?.message || err}`;
    }
  };

  hubRetryBtn.addEventListener("click", async () => {
    hubRetryBtn.disabled = true;
    hubStatus.textContent = "Đang gửi lại…";
    try {
      const r = await askBackground({ type: "HUB_QUEUE", op: "retry" });
      hubStatus.textContent = r?.deferred
        ? "Đang lùi dần sau lỗi — thử lại sau."
        : `Đã gửi ${r?.sent || 0} mục: Hub nhận ${r?.saved || 0}, từ chối ${r?.rejected || 0}` + (r?.lastError ? ` · ${r.lastError}` : "");
    } catch (err) {
      hubStatus.textContent = `Lỗi — ${err?.message || err}`;
    }
    await renderHub();
  });
  document.getElementById("hubOptionsBtn").addEventListener("click", () => { chrome.runtime.openOptionsPage(); });

  chrome.runtime.onMessage.addListener((message) => {
    if (!message || message.type !== "RUNNER_STATE") return;
    readLocal("lastRunConfig").then((cfg) => applyDisplay(message.state || {}, cfg || {})).catch(() => {});
  });

  chrome.storage.onChanged.addListener((changes) => {
    if (changes.runnerState || changes.lastRunConfig || changes.harvestStats) {
      renderState();
    }
    if (changes.hubQueue || changes.hubSettings) renderHub();
    if (changes.sourceImages) renderSourceImages();
  });

  if (coreError) exportStatus.textContent = coreError;
  // Các phần độc lập: phần nào lỗi cũng không được chặn phần khác.
  await Promise.all([renderState(), renderPdp(), renderHub()]);
});
