# Quy trình làm việc

**Quy trình chung nằm ở `D:\Projects\CLAUDE.md`** (áp cho mọi dự án trong `D:\Projects`) — `Bước 0` phân loại
trước: việc KHÔNG sửa code (hỏi đáp, khảo sát ảnh hưởng) thì trả lời thẳng; có sửa code thì chạy đủ 5 chặng
*nhận việc → `opus-dev` → `nghiem-thu` → `phan-bien` → chốt việc*, **không cần gõ `/lam`** (chốt 2026-08-13,
thay cho mặc định cũ "phiên chính tự thực thi"). Định nghĩa agent/lệnh ở `~/.claude/agents/` và
`~/.claude/commands/`. **Đừng chép lại quy trình đó vào đây** — mục này chỉ ghi phần riêng của repo shopee-suite.

Riêng repo này (chốt 2026-08-02, nới phần subagent 2026-08-04):

- Bản này **thay thế** mô hình 2 tầng cũ (Fable plan → Opus thực thi).
- Việc lớn viết plan trong `plans/` (mẫu `plans/TEMPLATE.md`) — vừa để bám tiến độ, vừa làm căn cứ chấm nghiệm thu.
- Ví dụ vì sao không được bỏ lượt phản biện: đợt 2026-08-04 hai bản vá build xanh + test xanh vẫn hỏng logic
  (một bản làm banner kẹt vĩnh viễn, một bản làm Hub từ chối thao tác của user); đợt 2026-08-09 bước check đơn
  trả hàng có 4 đường mất mã âm thầm mà toàn bộ 1600+ test không hề đỏ.

# Build

- .NET/C#: app desktop WPF trong `suite/`, hub web Blazor trong `server/Shopee.Hub.Web/`.
- Build: `dotnet build` project bị ảnh hưởng; sửa nhiều project thì build cả solution.

# Deploy

- **Hub web** (`server/Shopee.Hub.Web`) chạy trên VM Ubuntu — systemd service `shopee-hub`, thư mục `/opt/shopee-hub`, health `curl 127.0.0.1:8088/health` (public: `https://api.schedra.net/health`). SSH từ máy dev đã cài key + alias: **`ssh vps-muinx`** (vào thẳng, không cần mật khẩu; alias trong `~/.ssh/config` của máy dev). Quy trình: `dotnet publish server/Shopee.Hub.Web -c Release -p:PublishProfile=linux-x64` → scp `Shopee.Hub.Web.dll` (+ `wwwroot/app.css` nếu đổi, nhớ bump `app.css?v=N` trong `Components/App.razor`) lên `vps-muinx:/tmp/` → sudo backup bản cũ + `install` vào `/opt/shopee-hub` + `systemctl restart shopee-hub` (bước sudo cần mật khẩu — hỏi user) → check health.
  * **Lưu ý Playwright Node (rất quan trọng)**: Khi deploy mới hoặc copy lại thư mục `.playwright` từ Windows sang Linux VPS, binary Node sẽ mất cờ thực thi `+x` do umask Windows/tar/zip. Bắt buộc chạy: `sudo chmod +x /opt/shopee-hub/.playwright/node/linux-x64/node` (nếu thiếu, `BigSellerReloginScheduler` sẽ chết âm thầm do `Permission denied`).
- **App desktop client**: KHÔNG build từng máy — phát hành qua Velopack + GitHub Releases: bump `version.txt` + ghi `CHANGELOG.md` + commit, rồi theo các bước trong `release-suite.cmd` (vpk download → dotnet publish → vpk pack → vpk upload github, token lấy từ `gh auth token`); client tự tải delta, bấm "Cập nhật & khởi động lại" trong Settings → Phiên bản & cập nhật.
