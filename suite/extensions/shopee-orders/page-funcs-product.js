// Page-func đọc số sản phẩm đang có / tổng hạn mức của shop qua API nội bộ Seller Centre.
// (Bản cũ dò DOM trang Quản lý sản phẩm đã bỏ 12/11/2026 — trang đó bật cả loạt popup chặn.)

// Gọi API nội bộ của Shopee (same-origin) lấy số sản phẩm / hạn mức — không phụ thuộc DOM hay popup.
// GET /api/v3/opt/mpsku/list/get_product_count_limit?SPC_CDS=<uuid>&SPC_CDS_VER=2
// Trả { ok, tongSp, gioiHanSp, raw } (raw = text phản hồi cắt ~600 ký tự để log). TỰ CHỨA (chạy trong trang).
export async function pageFetchProductCountLimit() {
  // Heuristic (một chỗ duy nhất, dễ chỉnh khi thấy payload thật): duyệt JSON (nhánh `data` trước),
  // lấy số >= 0 theo TÊN KHÓA:
  //  - hạn mức: khóa chứa limit/max/quota, KHÔNG chứa remain/left/available/min (ưu tiên max/quota hơn limit trần)
  //  - tổng sp: khóa chứa current/used/published/uploaded/count/total, KHÔNG chứa limit/remain/left/available/max
  const phanTich = (json) => {
    const out = { tongSp: null, gioiHanSp: null };
    let diemGh = -1;
    const xet = (k, v) => {
      if (typeof v !== "number" || !isFinite(v) || v < 0) return;
      const key = String(k).toLowerCase();
      // Hạn mức shop thật luôn >= 50 (cùng ngưỡng nhánh DOM) — chặn nhầm cờ kiểu limit_type: 1
      if (/(limit|max|quota)/.test(key) && !/(remain|left|available|min)/.test(key)) {
        if (v < 50) return;
        const diem = /(max|quota)/.test(key) ? 2 : 1;
        if (diem > diemGh) { out.gioiHanSp = v; diemGh = diem; }
      } else if (/(current|used|published|uploaded|count|total)/.test(key) && !/(limit|remain|left|available|max)/.test(key)) {
        if (out.tongSp == null) out.tongSp = v;
      }
    };
    const duyet = (node, depth) => {
      if (node == null || typeof node !== "object" || depth > 6) return;
      const keys = Object.keys(node);
      for (const k of keys) {
        const v = node[k];
        if (v !== null && typeof v === "object") continue;
        xet(k, v);
      }
      for (const k of keys) {
        const v = node[k];
        if (v !== null && typeof v === "object") duyet(v, depth + 1);
      }
    };
    if (json && typeof json === "object") {
      if (json.data && typeof json.data === "object") duyet(json.data, 0);
      if (out.tongSp == null || out.gioiHanSp == null) {
        const roi = {};
        for (const k of Object.keys(json)) if (k !== "data") roi[k] = json[k];
        duyet(roi, 0);
      }
    }
    return out;
  };

  let raw = "";
  try {
    let cds = "";
    const m = (document.cookie || "").match(/(?:^|;\s*)SPC_CDS=([^;]+)/);
    if (m) cds = decodeURIComponent(m[1]);
    if (!cds) {
      try { cds = crypto.randomUUID(); } catch (e) { cds = ""; }
    }
    const url = location.origin + "/api/v3/opt/mpsku/list/get_product_count_limit?"
      + (cds ? "SPC_CDS=" + encodeURIComponent(cds) + "&" : "") + "SPC_CDS_VER=2";
    const ac = new AbortController();
    const timer = setTimeout(() => ac.abort(), 8000);
    let resp;
    try {
      resp = await fetch(url, { credentials: "include", signal: ac.signal });
      raw = await resp.text();
    } finally {
      clearTimeout(timer);
    }
    const rawCat = raw.length > 600 ? raw.substring(0, 600) : raw;
    if (!resp.ok) return { ok: false, tongSp: null, gioiHanSp: null, raw: "HTTP " + resp.status + " " + rawCat };
    let json = null;
    try { json = JSON.parse(raw); } catch (e) {
      return { ok: false, tongSp: null, gioiHanSp: null, raw: rawCat };
    }
    // code != 0 = Shopee báo lỗi (hết phiên, sai shop...) — không tin số trong payload.
    if (json && typeof json.code === "number" && json.code !== 0) {
      return { ok: false, tongSp: null, gioiHanSp: null, raw: rawCat };
    }
    // Tên trường thật (đối chiếu log 12/11/2026): data.current_published = số sp đang có (Đã đăng tải),
    // data.publish_limit = tổng hạn mức đăng bán. Thiếu trường nào thì mới rơi về heuristic.
    const d = json && json.data && typeof json.data === "object" ? json.data : null;
    const soHopLe = (v) => (typeof v === "number" && isFinite(v) && v >= 0 ? v : null);
    const kq = phanTich(json);
    if (d) {
      const tong = soHopLe(d.current_published);
      const gh = soHopLe(d.publish_limit);
      if (tong != null) kq.tongSp = tong;
      if (gh != null && gh >= 50) kq.gioiHanSp = gh;
    }
    return { ok: kq.tongSp != null || kq.gioiHanSp != null, tongSp: kq.tongSp, gioiHanSp: kq.gioiHanSp, raw: rawCat };
  } catch (e) {
    return { ok: false, tongSp: null, gioiHanSp: null, raw: "Lỗi: " + String((e && e.message) || e) + (raw ? " " + raw.substring(0, 300) : "") };
  }
}
