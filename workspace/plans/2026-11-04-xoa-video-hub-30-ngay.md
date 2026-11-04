# Xóa video trên hub — hộp thoại + hạn 30 ngày — 04/11

**Trạng thái:** XONG (04/11 — HubVideoRetention 19/19, HubVideoNames 14/14, build Release workspace + hub 0 error. Hub đã deploy 10:19, health ok, DELETE không token trả 401. App Release build lại sau deploy, 0 error.)

## Bối cảnh

- User chốt: xóa những video **cách đây từ 30 ngày**, dựa trên datetime của từng file. Không xóa sạch theo một lịch 30 ngày tới.
- Hub đã ghi datetime lúc PUT: cột `files.mtime` (UTC), client đọc qua `FileManifestEntry.Mtime`. Cào lại cùng SKU thì PUT đè và `mtime` tính lại từ đầu.
- App workspace (`workspace/src/Shopee.Workspace/App.xaml.cs`) **chưa** gọi `CoordinationRuntime.InitForCurrentMode()`. `CoordinationRuntime.Client` vì thế luôn null: đẩy/kéo video vừa làm không chạy, và app cũng không xóa được gì. App suite có gọi init; app đang cào là workspace. Phải nối init ở workspace.
- Client chưa có API xóa file. `HubDatabase.DeleteFile` chỉ được trang admin Files gọi. Cần `DELETE /files/{name}` cho client, **chỉ** nhận key video.

Working tree đang có phần hub video chưa commit (`SkuVideoSync`, `LauncherRunnerLoop`, `BigSellerProductUpdateRunner.*`, test, plan 04/11). Giữ nguyên. Không revert.

## Mục tiêu hành vi

1. Bấm scrape mới (nút shop hoặc Scrape Multi): nếu đã nối hub, hỏi «Xóa toàn bộ video trên hub trước khi scrape?». Có → xóa hết key `videos/*.mp4` rồi mới cào. Không → cào, giữ video đang có. Video trên đĩa máy không đụng.
2. App workspace, khi đang chạy, tự xóa video hub có `Mtime` cũ hơn hoặc bằng 30 ngày. App tắt thì chưa xóa; lần mở sau xóa luôn những file đã quá hạn (kể cả quá 31, 40 ngày).
3. Tiếp tục, Auto Run, hub giao việc: không hiện hộp thoại, không xóa sạch. Vòng 30 ngày vẫn chạy vì app đang mở.

## LÀM

### 1. Luật tên + hạn — `workspace/src/Shopee.Core/Scrape/HubVideoRetention.cs` (mới)

- `MaxAge` = 30 ngày.
- `IsVideoName(name)` đúng khi khớp `^videos/[^/\\:]+\.mp4$` và đoạn sau `videos/` không phải `.` hay `..` và không chứa `..`.
  - Nhận: `videos/ABC.mp4`, `videos/a_b.mp4`, `videos/_.mp4`, `videos/video.mp4`.
  - Từ chối: `config/accounts.json`, `cookies/x.json`, `images/default-update.jpg`, `videos/../config/accounts.json`, `videos/a/b.mp4`, `Videos/ABC.mp4`, `videos/ABC.MP4`, chuỗi rỗng.
- `IsExpired(mtime, now)`: `mtime` khác `default` và `now - mtime >= MaxAge`. `Mtime` mặc định (`default`) → không hết hạn (không có ngày thì không xóa).
- `ExpiredNames(entries, now)` và `AllVideoNames(entries)` chỉ trả tên qua `IsVideoName`.

### 2. Client xóa — `workspace/src/Shopee.Core/Coordination/HubClient.cs`

`DeleteFileAsync(name, ct)`: `DELETE` cùng đường `HubRoutes.Files` + `EncodePath` như GET/PUT, dùng `_http`. 200 hoặc 404 → `true`. Khác → `false`, không ném. `OperationCanceledException` ném tiếp.

### 3. Quét xóa — `workspace/src/Shopee.Core/Scrape/HubVideoMaintenance.cs` (mới)

Không tự lấy manifest trong lớp test được: nhận `IReadOnlyList<FileManifestEntry>` và delegate `delete(name, ct) → bool`.

- `SweepExpiredAsync(entries, now, delete, ct)`: xóa đúng `ExpiredNames`. Đếm số lần delete trả `true`. Một delete `false` hoặc ném (trừ hủy) → bỏ qua tên đó, xóa tiếp. Hủy thì ném tiếp.
- `DeleteAllVideosAsync(entries, delete, ct)`: cùng kiểu, với `AllVideoNames`.
- Một `SemaphoreSlim` tĩnh: hai lượt quét (mở app và bấm scrape) không chạy chồng. Lượt sau chờ lượt trước, rồi làm trên danh sách nó được đưa (caller tự tải manifest mới).

Lớp gọi hub (cùng file hoặc file kế, được phép đọc `CoordinationRuntime.Client`):

- `RunExpiredSweepAsync(log, ct)`: client null → return 0, không log. Có client: `ManifestAsync`, rồi `SweepExpiredAsync` với `now = DateTimeOffset.UtcNow`. Log một dòng: `Đã xóa {n} video trên hub cũ hơn 30 ngày` khi n > 0. n = 0 thì không log. Lỗi manifest: một dòng `Không quét được video cũ trên hub`, không ném.
- `RunDeleteAllAsync(log, ct)`: cùng kiểu, log `Đã xóa {n} video trên hub` khi đã gọi (kể cả n = 0). Lỗi: `Không xóa được video trên hub`, không ném.

### 4. Lịch trong app — `workspace/src/Shopee.Workspace/App.xaml.cs`

Sau khi BraveFleet cấu hình xong (cùng chỗ app suite gọi init, khoảng sau `StartMaintenance`):

- `CoordinationRuntime.InitForCurrentMode()` trong try/catch, lỗi thì `TryLog`, app vẫn mở.
- Bật lịch: gọi `RunExpiredSweepAsync` một lần nền ngay sau init, rồi mỗi 6 giờ khi app còn mở. Log qua `TryLog` (không cần UI). `RunShutdownHooks` dừng timer.

Không đụng `suite/suite/Shopee.Suite/App.xaml.cs`.

### 5. Hộp thoại — chỉ scrape do người bấm

`Dialogs.ConfirmAsync` là Có/Không, không có Hủy.

- `WorkspaceViewModel.RunScrapeShop` khi `resume == false`: nếu `CoordinationRuntime.Client` khác null, hiện «Xóa toàn bộ video trên hub trước khi scrape? Video trên máy này không bị xóa.» tiêu đề «Video trên hub». Có → `await RunDeleteAllAsync` (log vào log scrape nếu có chỗ ghi công khai; không có thì `Trace`/`TryLog` của app — ưu tiên log scrape đang dùng cho dòng `▶ Scrape`). Rồi mới `RunSingleAsync`. Không → cào ngay.
- `resume == true`: không hỏi, không xóa sạch.
- `ScrapeMultiViewModel.StartScrapeMultiAsync`: cùng một hộp thoại, một lần, trước khi kiểm tra Lead. Có → xóa sạch rồi chạy tiếp. Client null → không hỏi.
- `RunSingleAsync(..., silent: true)`, Auto Run, `TryStartSingleAsync`: không hỏi, không xóa sạch.

`RunScrapeShop` đang fire-and-forget. Giữ vậy: hàm async nội bộ, không await từ `RelayCommand` theo comment khóa nút hiện có.

### 6. Hub — `suite/server/Shopee.Hub.Web/Api/ClientApiEndpoints.cs`

`DELETE /files/{*name}` cùng nhóm client (token). Chỉ xóa khi tên qua **cùng luật** `IsVideoName` (chép hàm vào `suite/server/Shopee.Hub.Web/Api/HubVideoNames.cs`, internal, test được). Khớp → `db.DeleteFile`. Không khớp → 403, không xóa. Tên video không có trong DB → `DeleteFile` vẫn xóa manifest (hàm hiện trả true nếu tên hợp lệ) — endpoint trả 200.

Không đụng cookie, config, ảnh.

## KHÔNG LÀM

- Không deploy hub, không commit, không bump version.
- Không xóa file local, không đẩy lại kho mp4 cũ, không thêm hàng đợi đẩy lại khi PUT lỗi.
- Không hiện hộp thoại ở Tiếp tục / Auto Run / việc hub giao.
- Không sửa app suite.
- Không đổi `SkuVideoSync` / vòng tải video, trừ khi build gãy vì đổi chữ ký (không được đổi chữ ký).

## Tiêu chí đo được

SDK: `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`. Chạy từ `D:\Projects\shopee-tools`.

1. `dotnet test workspace/tests/Shopee.Core.Tests/Shopee.Core.Tests.csproj --filter FullyQualifiedName~HubVideoRetention --nologo` exit 0. File `workspace/tests/Shopee.Core.Tests/HubVideoRetentionTests.cs` có đủ case tên (nhận/từ chối ở mục 1), biên 30 ngày (đúng 30 ngày thì xóa, 30 ngày trừ 1 giây thì giữ, `default` thì giữ), `SweepExpiredAsync` không gọi delete cho file config, một delete ném `IOException` thì tên sau vẫn bị xóa, `OperationCanceledException` ném tiếp.

2. `dotnet test suite/server/Shopee.Hub.Web.Tests/Shopee.Hub.Web.Tests.csproj --filter FullyQualifiedName~HubVideoNames --nologo` exit 0. Case: `videos/ABC.mp4` được phép; `config/accounts.json` và `videos/../config/accounts.json` không. Thêm một test DB: `PutFile` `videos/ABC.mp4` và `config/accounts.json`, `DeleteFile` video, `ListFiles` không còn video và vẫn còn config.

3. `dotnet build workspace/src/Shopee.Workspace/Shopee.Workspace.csproj -c Release --nologo` exit 0, 0 error.

4. `dotnet build suite/server/Shopee.Hub.Web/Shopee.Hub.Web.csproj -c Release --nologo` exit 0, 0 error.

5. `rg "ConfirmAsync" workspace/src/Shopee.Workspace` thấy hộp thoại video ở `RunScrapeShop` (chỉ nhánh không resume) và `StartScrapeMultiAsync`, không thấy trong `AutoRunViewModel`.
