// Trang Cài đặt Hub (GĐ7): URL / token / mã máy cho chế độ bấm tay, lưu chrome.storage.local "hubSettings". URL Hub ngoài
// danh sách quyền sẵn có của manifest thì xin quyền host NGAY trong cú bấm "Lưu" (chrome.permissions.request cần cử chỉ
// người dùng). KHÔNG có token mặc định trong code.
import { normalizeHubUrl, hubPermissionPattern } from "./hub-client.js";

const KEY = "hubSettings";
const $ = (id) => document.getElementById(id);

const setStatus = (text, ok) => {
  const el = $("status");
  el.textContent = text;
  el.className = ok ? "ok" : "bad";
};

const randomMachineId = () => `ext-${Math.random().toString(16).slice(2, 10)}`;

const load = async () => {
  const cfg = (await chrome.storage.local.get(KEY))[KEY] || {};
  $("url").value = cfg.url || "";
  $("token").value = cfg.token || "";
  $("machineId").value = cfg.machineId || randomMachineId();
};

$("saveBtn").addEventListener("click", async () => {
  const url = normalizeHubUrl($("url").value);
  const token = $("token").value.trim();
  const machineId = $("machineId").value.trim();
  if (!url) { setStatus("URL Hub không hợp lệ (cần https://…, hoặc http://localhost).", false); return; }
  if (!token) { setStatus("Thiếu token.", false); return; }
  if (!machineId) { setStatus("Thiếu mã máy.", false); return; }
  const origins = [hubPermissionPattern(url)];
  try {
    // Gọi request NGAY (không await gì trước — cử chỉ người dùng phải còn): quyền đã có (Hub mặc định / localhost cấp sẵn
    // trong manifest) thì trả true không hỏi.
    const granted = await chrome.permissions.request({ origins });
    if (!granted) { setStatus(`Chưa được cấp quyền truy cập ${url} — extension không gửi được tới Hub này.`, false); return; }
  } catch (e) {
    setStatus(`Không xin được quyền truy cập ${url}: ${e?.message || e}`, false);
    return;
  }
  await chrome.storage.local.set({ [KEY]: { url, token, machineId } });
  $("url").value = url;
  setStatus("Đã lưu. Bấm \"Kiểm tra kết nối\" để thử token.", true);
});

$("testBtn").addEventListener("click", async () => {
  setStatus("Đang kiểm tra…", true);
  try {
    const r = await chrome.runtime.sendMessage({ type: "HUB_TEST" });
    if (r?.kind === "ok") setStatus("✓ Kết nối Hub được, token đúng.", true);
    else setStatus(`✗ ${r?.reason || r?.error || "Không rõ lỗi"}`, false);
  } catch (e) {
    setStatus(`✗ ${e?.message || e}`, false);
  }
});

load().catch((e) => setStatus(`Không đọc được cài đặt: ${e?.message || e}`, false));
