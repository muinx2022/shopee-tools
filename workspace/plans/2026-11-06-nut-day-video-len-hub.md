# Nút đẩy video lên hub — 06/11

**Trạng thái:** XONG, CHƯA COMMIT (06/11 — phiên chính tự chạy lại: test HubVideoBulkPush + SkuVideoSync + HubVideoRetention 57/57; build Release Shopee.Workspace 0 warning 0 error. Chưa chạy app thật, chưa gọi hub thật, hộp thoại và nút ribbon chỉ kiểm bằng đọc code.)

## Bối cảnh

- Scrape đẩy từng video ngay sau khi tải (`LauncherRunnerLoop.PushLocalVideoToHubAsync`). Lỗi mạng, quá 90s, hoặc
  hub tắt lúc đó thì video chỉ còn trên máy cào, không có hàng đợi đẩy lại. Máy update khi đó thiếu video.
- Hub có manifest `GET /manifest` → `FileManifestEntry { Name, Hash (SHA256 hex), Size, Mtime }`. Video có key
  `videos/{file}.mp4`, sống 30 ngày tính từ `Mtime` (lúc PUT gần nhất).
- Thư mục video dùng chung của máy: `WorkspaceViewModel.VideoDir` (= `Update.VideoFolder`, fallback `Scrape.VideoDir`).
- Mọi PUT/xóa video đã đi qua một khóa `HubVideoMaintenance.Gate` (`RunExclusiveAsync`).
- `Dialogs` chỉ có `ConfirmAsync` (Có/Không) và `InfoAsync`.
- User chốt 06/11:
  - Một nút **chung toàn app**, không gắn với shop hay Scrape Multi.
  - Một nút, bấm xong hỏi chọn: **sync file lỗi** hoặc **đẩy lại toàn bộ**.
  - Chỉ lấy `.mp4` trong thư mục video có ngày sửa **≤ 30 ngày**. Kho cũ hơn không đẩy.
  - Đây là ngoại lệ có chủ đích cho mục "Không đẩy kho mp4 có sẵn" của plan 04/11, và chỉ khi người dùng bấm nút.

## Mục tiêu hành vi

1. Ribbon tab Workspace, nhóm "Hành động", thêm nút **"Đẩy video"** (tooltip nói rõ là đẩy lên hub) cạnh "Dừng jobs". Chỉ hiện khi
   `WorkspaceAppConfig.ShowScrape`.
2. Bấm nút:
   - Hub chưa nối (`CoordinationRuntime.Client` null) → `InfoAsync` «Chưa nối hub, không đẩy được video.» rồi dừng.
   - Đang có scrape chạy (`_scrape.IsBusy` hoặc `ScrapeMulti.IsRunning` hoặc `AutoRun.IsRunning`) → `InfoAsync`
     «Đang scrape. Đợi scrape xong rồi bấm lại.» rồi dừng.
   - Thư mục video trống hoặc không tồn tại → `InfoAsync` báo, dừng.
3. Quét (chạy nền, không khóa UI): lấy `*.mp4` trong thư mục (không đệ quy), sửa trong 30 ngày, tên hợp lệ
   `HubVideoRetention.IsVideoName("videos/" + tên)`. Tải manifest một lần.
   Đếm `X` = số file hợp lệ, `Y` = số file **hub chưa có hoặc khác** (không có key, hoặc `Size` khác, hoặc
   cùng `Size` mà SHA256 khác `Hash`, so không phân biệt hoa-thường).
4. Một hộp thoại riêng `PushVideosDialog` (tiêu đề «Đẩy video lên hub»), một lần hỏi:
   - Dòng chữ: «Thư mục có **X** video trong 30 ngày qua. Hub đang thiếu hoặc khác **Y** video.»
   - Nút **«Chỉ đẩy Y video thiếu/khác»**: nút chính (`IsDefault`). Mờ khi Y = 0, lúc đó Enter không làm gì.
   - Nút **«Đẩy lại toàn bộ X video»**, dưới có chữ nhỏ «Ghi đè video trên hub, tính lại hạn 30 ngày.».
     Mờ khi X = 0.
   - Nút **«Hủy»** (`IsCancel`). Esc hoặc đóng cửa sổ = Hủy.
   - X = 0 → không mở hộp thoại, `InfoAsync` «Thư mục không có video nào trong 30 ngày qua.» và dừng.
   - Chọn thiếu/khác → đẩy danh sách Y. Chọn toàn bộ → đẩy cả X. Hủy → dừng, không đẩy gì.
5. Đẩy lần lượt từng file. Mỗi file: giữ `HubVideoMaintenance.RunExclusiveAsync` **chỉ quanh một file** (không giữ
   suốt lô), gọi `SkuVideoSync.TryPushAsync`-cùng-luật (trần 80MB, timeout 90s/file). File lỗi → đếm lỗi, đẩy tiếp.
6. Log vào log Scrape Multi (`ScrapeMulti.Log`): đầu lô «Bắt đầu đẩy N video lên hub», mỗi file lỗi một dòng
   «Không đẩy được {file} lên hub ({lý do})», cuối lô «Đã đẩy a/N video lên hub, lỗi b».
   Xong lô → `InfoAsync` cùng nội dung dòng cuối.
7. Đang đẩy: nút mờ (AsyncRelayCommand tự chặn bấm lại). "Dừng jobs" hủy lô đang đẩy (file đang PUT dở bị hủy,
   file sau không đẩy), log «Đã dừng đẩy video: a/N xong».

## LÀM

Chỉ trong `workspace/`. Không sửa `suite/`, không sửa hub.

### 1. Lõi chọn file — `workspace/src/Shopee.Core/Scrape/HubVideoBulkPush.cs` (mới)

Không gọi mạng. Nhận dữ liệu và delegate để test không cần hub.

- `record LocalVideo(string Path, string RemoteName, long Size, DateTime LastWriteUtc)`.
- `ListLocal(folder, nowUtc)` → `IReadOnlyList<LocalVideo>`: `Directory.EnumerateFiles(folder, "*.mp4",
  TopDirectoryOnly)`, bỏ file có đuôi khác đúng `.mp4` (Windows match `*.mp4` cả `.MP4`), bỏ `Size <= 0` hoặc
  `> SkuVideoSync.MaxPushBytes`, bỏ `nowUtc - LastWriteUtc > HubVideoRetention.MaxAge`, `RemoteName` =
  `"videos/" + Path.GetFileName(path)` phải qua `HubVideoRetention.IsVideoName`. Folder rỗng/không có → list rỗng.
- `SelectMissing(locals, manifest, hashOf)` → các `LocalVideo` cần đẩy: không có entry cùng `Name` (Ordinal) →
  đẩy; `Size` khác → đẩy; cùng `Size` → gọi `hashOf(path)`, khác `Hash` (OrdinalIgnoreCase) → đẩy. `hashOf`
  ném (file đang bị khóa) → coi là cần đẩy, không ném.
- `Sha256Hex(path)`: cùng cách `HubConfigSync.LocalSha256` (`Convert.ToHexString`). Không đổi `HubConfigSync`.
- `PushAllAsync(items, push, onFail, ct)` → `(int ok, int fail)`: lần lượt từng item, mỗi item
  `HubVideoMaintenance.RunExclusiveAsync(ct => SkuVideoSync-style push)`. Đọc byte rồi gọi
  `push(RemoteName, bytes, ct)`. `push` false/ném (trừ hủy) → `onFail(item, reason)`, `fail++`, đi tiếp. Hủy → ném
  tiếp (caller đếm `ok` đã xong qua callback hoặc kết quả từng phần — chọn một cách, ghi rõ trong code).

  Nếu `SkuVideoSync.TryPushAsync` dùng được nguyên (nó nhận `sku` rồi tự dựng tên) thì **không** dùng nó cho lô:
  tên file trên đĩa đã là tên đã sanitize, dựng lại từ sku có thể lệch. Tách phần đọc+kiểm trần thành hàm private
  dùng chung trong `HubVideoBulkPush`, không đổi chữ ký `SkuVideoSync`.

### 2. Lớp gọi hub — cùng file, `RunAsync(folder, mode, log, ct)` hoặc tách 2 bước

Được phép đọc `CoordinationRuntime.Client`. Tách 2 bước để ViewModel hỏi người dùng ở giữa:

- `ScanAsync(folder, ct)` → `(IReadOnlyList<LocalVideo> all, IReadOnlyList<LocalVideo> missing)`.
  Manifest lỗi (trừ hủy) → ném `HubVideoScanException` với thông điệp ngắn để ViewModel `InfoAsync`.
  Hash chạy trên thread pool (`Task.Run`), không trên UI thread.
- `PushAsync(items, log, ct)` → `(ok, fail)`: `push` = `client.UploadAsync(name, bytes, null, timeoutCts)` với
  timeout 90s nối `ct`, `resp.Ok == false` → lý do `resp.Conflict`. Lý do cắt 120 ký tự như `ShortHubReason`.

### 3. Hộp thoại — `workspace/src/Shopee.Workspace/Modules/Workspace/PushVideosDialog.xaml` + `.xaml.cs` (mới)

- `Window` nhỏ, `SizeToContent="WidthAndHeight"`, `ResizeMode="NoResize"`, `WindowStartupLocation="CenterOwner"`,
  `ShowInTaskbar="False"`. Dùng style nút có sẵn (`primary` cho nút chính, nút thường cho 2 nút kia), cùng
  font/màu với các cửa sổ khác của app (xem `ScrapeStatsWindow.xaml`).
- Ctor nhận `(int total, int missing)`, tự đặt chữ và `IsEnabled` của 2 nút đẩy.
- `public enum PushVideosChoice { Cancel, Missing, All }` và thuộc tính `Choice` (mặc định `Cancel`).
  Bấm nút đẩy → đặt `Choice`, `DialogResult = true`. Hủy/Esc/đóng → giữ `Cancel`.
- Mở bằng `WindowHost.ShowDialogAsync(dialog)` (đã tự lấy owner và chạy trên UI thread). Không thêm hàm vào
  `IDialogService`/`Dialogs`.

### 4. Nút — `workspace/src/Shopee.Workspace/ViewModels/ShellViewModel.cs`

- `[RelayCommand] private async Task PushVideosToHubAsync()` (AsyncRelayCommand mặc định không cho chạy chồng →
  nút mờ khi đang đẩy).
- Thêm `new RibbonActionItem("Đẩy video", "IconSync", PushVideosToHubCommand, "Đẩy video trong thư mục video
  (30 ngày qua) lên hub: chỉ file hub thiếu/khác, hoặc đẩy lại toàn bộ")` vào nhóm "Hành động" tab Workspace, chỉ
  khi `WorkspaceAppConfig.ShowScrape`.
- Luồng đúng mục 2–7 phần Mục tiêu. Thư mục lấy từ `_workspace.VideoDir`. Bước chọn chế độ dùng
  `PushVideosDialog` ở mục 3, không dùng `ConfirmAsync`.
- Một `CancellationTokenSource _videoPushCts`; `StopWorkspaceJobs` gọi `_videoPushCts?.Cancel()`.
- `RefreshJobStatus` đếm lô đẩy video là một job khi đang chạy.

Icon: dùng `IconSync` có sẵn trong `Themes/Icons.xaml`. Không vẽ icon mới.

## KHÔNG LÀM

- Không sửa hub, không endpoint mới, không deploy.
- Không đổi vòng scrape, không đổi `SkuVideoSync` (chữ ký và hành vi), không đổi `HubVideoMaintenance` ngoài việc
  gọi `RunExclusiveAsync`.
- Không đẩy file cũ hơn 30 ngày, không đệ quy thư mục con, không đẩy `.part`.
- Không xóa file local, không xóa video trên hub.
- Không đẩy song song nhiều file.
- Không thêm hàm mới vào `IDialogService`/`Dialogs` (hộp thoại 3 nút là cửa sổ riêng, mở qua `WindowHost`).
- Không sửa app `suite/`.
- Không commit, không bump version.

## Tiêu chí đo được

SDK: `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`. Chạy từ `D:\Projects\shopee-tools`.

1. `dotnet test workspace/tests/Shopee.Core.Tests/Shopee.Core.Tests.csproj --filter FullyQualifiedName~HubVideoBulkPush --nologo`
   exit 0. File `workspace/tests/Shopee.Core.Tests/HubVideoBulkPushTests.cs` có:
   - `ListLocal`: thư mục temp có `A.mp4` (mới), `B.mp4` sửa 31 ngày trước, `C.mp4.part`, `D.txt`, `E.mp4` rỗng,
     thư mục con `sub/F.mp4` → chỉ còn `A.mp4`, `RemoteName` = `videos/A.mp4`. Đúng 30 ngày trừ 1 giây → giữ.
   - Folder không tồn tại → rỗng, không ném.
   - `SelectMissing`: hub không có key → chọn; `Size` khác → chọn, **không** gọi `hashOf`; cùng `Size` cùng hash
     (khác hoa-thường) → không chọn; cùng `Size` khác hash → chọn; `hashOf` ném `IOException` → chọn.
   - `PushAllAsync`: 3 item, item 2 `push` ném `HttpRequestException` → ok 2, fail 1, item 3 vẫn được đẩy, `onFail`
     gọi đúng 1 lần với item 2. `push` ném `OperationCanceledException` → ném tiếp, item sau không được gọi.
   - `PushAllAsync` gọi `push` với đúng byte của file và đúng `RemoteName`.
2. `dotnet test workspace/tests/Shopee.Core.Tests/Shopee.Core.Tests.csproj --filter "FullyQualifiedName~SkuVideoSync|FullyQualifiedName~HubVideoRetention" --nologo`
   exit 0 (không vỡ test cũ).
3. `dotnet build workspace/src/Shopee.Workspace/Shopee.Workspace.csproj -c Release --nologo` exit 0, 0 error.
4. `rg "PushVideosToHubCommand" workspace/src/Shopee.Workspace` thấy đúng chỗ khai báo ribbon trong `ShellViewModel.cs`.
5. `rg "_videoPushCts" workspace/src/Shopee.Workspace/ViewModels/ShellViewModel.cs` thấy có trong `StopWorkspaceJobs`.

6. `rg "ConfirmAsync" workspace/src/Shopee.Workspace/ViewModels/ShellViewModel.cs` không thấy trong
   `PushVideosToHubAsync`; `rg "PushVideosDialog" workspace/src/Shopee.Workspace` thấy chỗ mở bằng `WindowHost.ShowDialogAsync`.

Không có test UI. Phiên chính kiểm bằng cách đọc code luồng hộp thoại (mục 3, 4): Y = 0 thì nút thiếu/khác mờ;
X = 0 thì không mở hộp thoại; Hủy/Esc/đóng không đẩy gì.

## Báo cáo thực thi (06/11)

Quy trình: phiên chính lập plan → `opus-dev` (Haiku high) code → `nghiem-thu` (Haiku high) → `phan-bien` (Sonnet high) → `opus-dev` sửa 1 vòng → phiên chính đọc lại code, tự chạy test/build.

**Nghiệm thu:** 24/28 đạt, 4 đạt một phần, 0 trượt. Cả 6 tiêu chí đo được đạt.

**Phản biện:** không có lỗi NẶNG. Ba lỗi TRUNG BÌNH đã sửa ở vòng 1:
1. `ListLocal` chạy đồng bộ trên UI thread → nay `Task.Run`; kiểm ngày trước kích thước; `Directory.Exists` ở ViewModel cũng chạy nền.
2. `Sha256Hex` và `PushOneAsync` mở file với `FileShare.Read` làm `VideoDownloader` không xóa được file cũ → nay `FileShare.ReadWrite | FileShare.Delete`. Thử phá: đổi về `File.OpenRead` / `ReadAllBytesAsync` thì 2 test mới đỏ.
3. `WindowHost` mở dialog không modal khi không có cửa sổ chính hiển thị, ViewModel đọc `Choice` sớm → thêm `PushVideosDialog.WaitClosedAsync()`, ViewModel chờ đóng rồi mới đọc; `ShowInTaskbar="True"` để không lạc hộp thoại.

**Lệch plan, giữ nguyên có chủ đích:**
- Tên nút ribbon là «Đẩy video» (plan mục 1 đã sửa cho khớp).
- Nút đóng hộp thoại bằng `Close()` sau khi đặt `Choice`, không gán `DialogResult = true`.
- Có thêm một dòng log «Đã dừng đẩy video khi đang quét thư mục».

**Chưa xử lý, đã báo user:**
- Đẩy bù theo mtime local có thể làm video sống quá 30 ngày kể từ lúc cào gốc (máy update ghi mtime lúc kéo từ hub).
- Scrape có thể bắt đầu giữa lúc đang đẩy; chỉ kiểm lúc bấm nút. Không còn làm hỏng file sau sửa lỗi 2, nhưng hai việc chạy song song.
- File rỗng, quá 80MB, tên không hợp lệ bị loại âm thầm khỏi lô (cùng luật scrape).
- File chỉ khác hoa-thường với tên có trên hub bị hub từ chối, nên chế độ «thiếu/khác» không về 0.
- `HubVideoMaintenance.Gate` không có timeout chờ (ngoài phạm vi, nghi ngờ). Dự án workspace chưa có sổ nên chưa ghi.
