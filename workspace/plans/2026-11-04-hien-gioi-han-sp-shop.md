# Plan: Cột tiến độ hiện «Đã đạt giới hạn SP của shop»

- **Ngày:** 2026-11-04
- **Trạng thái:** code xong, chờ lượt Update chạm giới hạn; chưa commit
- **Người lập:** phiên chính · **Người thực thi:** agent `dev`

## 1. Bối cảnh

Auto Run shop `six6shop.store` (04/11, 08:57): BigSeller trả hộp thoại listing limit, update OK 0/3703, nhưng `ShouldMarkCompleted` ghi Update **completed**. Cột TIẾN ĐỘ chỉ hiện khi `UpdateDone`, nên dòng shop thành «Đã cập nhật 0 dòng». Shop `alina99.store` cùng lý do nhưng hiện «Đã cập nhật 2667 dòng». Cả hai đều không nói shop đã đầy.

Quyết định cũ (plan 2026-11-02) giữ nguyên: hộp thoại listing limit vẫn tính Update xong để Auto Run sang shop kế; toast thì không tính xong. Việc này chỉ đổi **chữ trên cột TIẾN ĐỘ**.

## 2. LÀM

1. `OpProgress` thêm `bool ListingLimit`. Gắn `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]` để `false` không ghi ra JSON — file cũ và test `OpProgressStore_RoundTrip_BangByte` giữ nguyên từng byte.
2. `FinishRun(..., bool listingLimit = false)` ghi cờ này mỗi lần kết thúc (true bật, false tắt). `BeginRun` tắt cờ khi mở lượt mới. Tách phần gán field thành `internal static` để test không đụng `%AppData%`.
3. `UpdateProductViewModel`, nhánh kết thúc bình thường (không hủy, không exception): gọi `FinishRun` với `listingLimit: kind == Update && runner.ListingLimitReached`. Cả hộp thoại và toast đều bật cờ. Nhánh hủy / lỗi giữ `listingLimit: false`.
4. `WorkspaceShopViewModel.ProgressLine` và `ProgressTooltip`:
   - còn dòng live thì hiện dòng live;
   - không thì nếu tiến độ Update của shop (cùng các khoá sheet mà `GetCompletedUpdateProgressText` đang hỏi) có `ListingLimit` → đúng câu `Đã đạt giới hạn SP của shop`;
   - không thì giữ «Đã cập nhật … dòng» khi Update đã xong, còn lại để trống.
   - Câu giới hạn thắng cả khi Update **chưa** xong (toast) và cả khi đã có số dòng (alina99).
5. Test thuần, không mở app, không ghi file tiến độ thật.

## 3. KHÔNG LÀM

- Không đổi `ShouldMarkCompleted` / `ListingLimitCountsAsCompleted`. Hộp thoại vẫn đánh dấu Update xong; toast vẫn `stopped`. Auto Run vẫn sang shop kế.
- Không đổi log, không đổi nút ✓, không đổi banner «việc dở».
- Không suy ra cờ cho các shop đã ghi xong từ lần chạy trước. `six6shop` / `alina99` chỉ hiện câu mới sau lượt Update **sau bản này** mà lại chạm giới hạn.
- Không sửa file đang `M` của phiên khác: `WorkspaceViewModel.cs`, `HubClient.cs`, `VideoDownloader.cs`, `LauncherRunnerLoop.cs`, `BigSellerProductUpdateRunner.Fields.cs`, `BigSellerProductUpdateRunner.Process.cs`, `App.xaml.cs`, `ScrapeMultiViewModel.cs`, mọi file `suite/`.
- Không commit.

## 4. Hành vi

| Tình huống | Cột TIẾN ĐỘ |
|---|---|
| Update chạm listing limit (hộp thoại hoặc toast), kể cả 0 SP hay 2667 SP | `Đã đạt giới hạn SP của shop` |
| Update đủ SP, không chạm giới hạn | `Đã cập nhật N dòng` như cũ |
| Đang chạy, có dòng live | dòng live, không phải câu giới hạn |
| Hủy / lỗi / Next shop (FinishRun không kèm cờ) | không hiện câu giới hạn |
| Bắt đầu lượt Update mới | cờ cũ tắt cho đến khi lượt này kết thúc |

## 5. Tiêu chí

1. `dotnet test workspace/tests/Shopee.Core.Tests/Shopee.Core.Tests.csproj --filter "FullyQualifiedName~OpProgress|FullyQualifiedName~JsonAtomicFileRoundTrip"` — xanh, gồm: cờ bật/tắt qua ApplyFinish; `BeginRun` logic tắt cờ; JSON `false` không có field `ListingLimit`, `true` có, file thiếu field đọc ra `false`; `OpProgressStore_RoundTrip_BangByte` vẫn xanh.
2. `dotnet test workspace/tests/Shopee.Workspace.Tests/Shopee.Workspace.Tests.csproj --filter "FullyQualifiedName~ProgressLine|FullyQualifiedName~UpdateProductCompletion"` — xanh. Hàm chọn chữ: live thắng; có cờ thì đúng câu `Đã đạt giới hạn SP của shop` dù `updateDone` và dù chữ cũ là «Đã cập nhật 2667 dòng»; không cờ + đã xong thì trả chữ cũ; không cờ + chưa xong thì rỗng. `ShouldMarkCompleted` giữ kết quả cũ.
3. `dotnet build workspace/src/Shopee.Workspace/Shopee.Workspace.csproj --nologo` — 0 error, 0 warning mới từ file vừa sửa.
