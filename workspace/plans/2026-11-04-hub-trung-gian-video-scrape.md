# Hub trung gian cho video scrape — 04/11

**Trạng thái:** XONG (04/11 — phiên chính: test SkuVideoSync 22/22; build Release MultiBrave + UpdateProduct 0 error 0 warning. Chưa phát hành app, chưa gọi hub thật. Kho mp4 cũ không đẩy.)

## Bối cảnh

- Git `main`, working tree sạch lúc lập plan (không có file `M` của phiên khác).
- Scrape (`LauncherRunnerLoop` → `VideoDownloader`) lưu `{tên}.mp4` vào thư mục video của **máy đang cào** (`ScrapeNativeSettings.VideoOutputDir` / ô Video dùng chung).
- Update (`BigSellerProductUpdateRunner.ResolveVideoPath`) chỉ tìm `VideoFolder\{sku}.mp4` trên **máy đang update**. Không có file local thì bỏ video (bước non-fatal).
- Hub đã có kho blob `PUT/GET /files/{*name}` (`HubClient.UploadAsync` / `DownloadAsync`, timeout bulk 5 phút, trần body 256MB). Ảnh update đã dùng prefix `images/`. Manifest đồng bộ acc chỉ kéo `cookies/` và vài `config/…`, không kéo mọi file.
- User chốt: **chỉ video scrape từ nay**. Không đẩy kho mp4 đang nằm sẵn trên đĩa.

## Mục tiêu hành vi

Máy cào, sau khi ghi file local thành công, đẩy đúng file đó lên hub. Máy update thiếu file local thì kéo bản đó về `VideoFolder` rồi đăng BigSeller bằng đường cũ. Hub tắt, lỗi mạng, hoặc hub không có file: scrape vẫn coi video local là tải được; update vẫn bỏ video và chạy tiếp sản phẩm.

## LÀM

Chỉ trong `workspace/`. Không sửa `suite/`.

### 1. Lõi tên file + đẩy/kéo — `workspace/src/Shopee.Core/Scrape/SkuVideoSync.cs` (mới)

Dùng chung cho scrape và update. Không gọi mạng bên trong lớp này: nhận delegate, để test không cần hub.

- `FileName(sku)` = cùng luật `VideoDownloader` đang dùng: tách `Path.GetInvalidFileNameChars()`, nối `_`, `Trim` + `Trim('.')`, rỗng → `video`, rồi cộng `.mp4`.
- `RemoteName(sku)` = `videos/` + `FileName(sku)`. Một đoạn tên file, không có `\` `/` `:` thêm.
- `LocalPath(folder, sku)` = `Path.Combine(folder, FileName(sku))`.
- Trần đẩy: `MaxPushBytes` = 80MB. File không tồn tại, rỗng, hoặc lớn hơn trần → không gọi delegate đẩy, trả `false`.
- `TryPushAsync(savedPath, sku, push, ct)`: đọc byte, gọi `push(RemoteName(sku), bytes, ct)`. Delegate trả `false` hoặc ném (trừ `OperationCanceledException`) → trả `false`, không ném. `OperationCanceledException` ném tiếp.
- `TryEnsureLocalAsync(folder, sku, pull, ct)`:
  - `folder` trống → `null`, không gọi `pull`.
  - File local đã có → trả đường đó, **không** gọi `pull`.
  - Ổ đích không đủ chỗ (`DiskSpaceGuard.HasFreeSpace(folder, DiskSpaceGuard.VideoMinFreeBytes)` = false) → `null`, không gọi `pull`.
  - `pull` trả `null`, mảng rỗng, hoặc ném (trừ hủy) → `null`, không để file `.part`.
  - Có byte: ghi `{LocalPath}.part` rồi `Move` đè file đích (xóa đích cũ nếu có), trả đường đích. Hủy giữa chừng thì ném tiếp và xóa `.part` nếu mình đã tạo.

`VideoDownloader` bỏ `SanitizeFileName` private, gọi `SkuVideoSync.FileName` để tên file local và key hub là một.

### 2. Scrape đẩy sau khi tải được — `LauncherRunnerLoop.cs`

Ngay sau chỗ đã có `videoPath` từ `DownloadBestVideoAsync` (cả scrape thường và scrape multi đều đi vòng này):

- Chỉ đẩy khi `videoPath` trỏ tới file tồn tại.
- `push` = `CoordinationRuntime.Client` lúc gọi (null → không đẩy, không lỗi). Upload qua `HubClient.UploadAsync` (bulk). Bọc timeout **90 giây** (CTS nối với `cancellationToken` của vòng scrape) để một lần đẩy không chiếm hết timeout 5 phút của client.
- `UploadAsync` trả `Ok == false` → coi là đẩy thất bại.
- Log có sẵn của vòng: thành công `Đã đẩy {file} lên hub`; thất bại `Không đẩy được {file} lên hub` (kèm lý do ngắn nếu có).
- **Không** đổi `videoOk`. Hub lỗi vẫn giữ `videoOk = true` nếu file local đã có. Hủy của user vẫn dừng vòng.

### 3. Update kéo khi thiếu local — `BigSellerProductUpdateRunner.Process.cs`

Trước `ResolveVideoPath(rec.Sku)`:

- Gọi `TryEnsureLocalAsync` với `_settings.VideoFolder`, `rec.Sku`, `pull` đọc `CoordinationRuntime.Client.DownloadAsync`. Client null → `pull` trả `null`. Timeout **90 giây**, nối `ct` của bước update.
- `DownloadAsync` trả `null` (404) → thiếu, không lỗi.
- Log `_log`: kéo được thì `Kéo video {file} từ hub`; hub lỗi (exception, không phải 404) thì một dòng `Không kéo được video {sku} từ hub`. 404 im lặng (giống hôm nay không có file).
- Sau đó `ResolveVideoPath` như cũ (kể cả luật bỏ video ≥ 60s). `ResolveVideoPath` tìm bằng `SkuVideoSync.LocalPath`, không còn `sku + ".mp4"` raw — tên trùng file scrape đã ghi.

Bước upload BigSeller giữ nguyên. Không có file sau khi kéo → bỏ video, sản phẩm vẫn đi tiếp.

## KHÔNG LÀM

- Không đẩy kho mp4 có sẵn trong thư mục video.
- Không xóa file local sau khi đẩy.
- Không lưu URL Shopee thay file.
- Không sửa server hub, không endpoint mới, không deploy hub, không commit, không bump version.
- Không sửa app trong `suite/` (bản desktop song song).
- Không lọc trang Files của hub, không tách video khỏi bảng `files`.
- Không đổi `videoOk`, không làm scrape/update fail vì hub.
- Không so hash với manifest (mỗi lần tải mới thì PUT đè). Không backfill.

## Tiêu chí đo được

Chạy từ `D:\Projects\shopee-tools`:

1. `dotnet test workspace/tests/Shopee.Core.Tests/Shopee.Core.Tests.csproj --filter FullyQualifiedName~SkuVideoSync --nologo`

   ĐẠT khi exit 0 và có các case sau (trong `workspace/tests/Shopee.Core.Tests/SkuVideoSyncTests.cs`):

   - `FileName("ABC")` = `ABC.mp4`, `RemoteName` = `videos/ABC.mp4`.
   - SKU `a/b`, `..\..`, `C:foo` → `RemoteName` khớp `^videos/[^/\\:]+$` và không chứa `..`.
   - SKU rỗng → `video.mp4`.
   - `VideoDownloader` ghi file cùng tên `SkuVideoSync.FileName` (test gọi `DownloadBestAsync` vào thư mục temp với 1 URL local `HttpListener` **hoặc** test thuần tên file nếu không mở socket: tối thiểu đối chiếu `FileName` với kết quả public. Nếu không test được downloader mà không có mạng, chỉ cần `FileName` là hàm duy nhất downloader dùng — review sẽ đọc call site).
   - File local đã có → `TryEnsureLocalAsync` không gọi `pull`.
   - Thiếu file, `pull` trả byte `1,2,3` → file đích đúng nội dung, không còn `.part`.
   - `pull` trả `null` hoặc ném `IOException` → `null`, không ném, không file rác.
   - File > `MaxPushBytes` → `TryPushAsync` trả `false`, không gọi `push`.
   - `push` trả `false` hoặc ném `HttpRequestException` → `false`.
   - `push`/`pull` ném `OperationCanceledException` → ném tiếp.

2. `dotnet build workspace/src/Shopee.Module.MultiBrave/Shopee.Module.MultiBrave.csproj -c Release --nologo` exit 0, 0 error.

3. `dotnet build workspace/src/Shopee.Module.UpdateProduct/Shopee.Module.UpdateProduct.csproj -c Release --nologo` exit 0, 0 error.

4. `rg "TryPushAsync|TryEnsureLocalAsync" workspace/src` thấy đúng một call site đẩy trong `LauncherRunnerLoop.cs` và một call site kéo trong `BigSellerProductUpdateRunner.Process.cs`.
