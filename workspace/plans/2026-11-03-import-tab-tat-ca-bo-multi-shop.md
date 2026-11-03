# Import quét tab Tất cả + bỏ «Import cho tất cả các shop» (03/11)

**Trạng thái:** XONG (review + phản biện phiên chính 03/11 — tiêu chí đo ĐẠT; tick DOM=0 vẫn là rủi ro ngoài phạm vi)

## Vấn đề

- Auto Run tối 03/11 (`witkoxatlinpbgld`): khớp 50 SP/trang nhưng tick = 0 → không bấm Import. Đang quét tab Chưa nhận / Đã nhận.
- User chốt: chỉ quét tab **Tất cả**; mỗi trang tick SP khớp → **Import to Stores** → Next; dừng khi hết trang hoặc đủ id.
- User chốt: **bỏ hẳn** tùy chọn «Import cho tất cả các shop» ở Auto Run và Workspace manual — không còn chỗ nào import nhiều shop một lần.

## LÀM

### A. Bỏ «Import cho tất cả các shop» (UI + luồng)

1. **AutoRunView.xaml / AutoRunViewModel**: gỡ checkbox, property `ImportAllShops` / `ShowImportAllOption`, nhánh `importAllShops` trong `RunAccountAsync` — luôn flow cũ: từng shop Import (1 shop) rồi Update.
2. **WorkspaceView.xaml / WorkspaceAccountViewModel**: gỡ checkbox + nút Import của header tài khoản (`ImportAllToggle`, `ImportAllShops`, tip/label/enabled liên quan).
3. **WorkspaceShopViewModel**: gỡ khoá nút Import theo `Parent.ImportAllShops`; `ImportToggleEnabled` trở lại điều kiện cũ (đang import / CanStartUpdate).
4. **UpdateProductViewModel**: gỡ / không còn gọi `RunImportAllSingleAsync` từ UI/AutoRun. Có thể xoá hẳn method + `IsImportAllRunning` / `IsImportAllJob` / cờ `AllShops` trên job nếu không còn caller.
5. **UpdateProductRunner**: gỡ `RunImportAllAsync` (hoặc tương đương) nếu chỉ phục vụ multi-shop.
6. **BigSellerImportToStoreRunner**: quay về **1 shop / lần** — bỏ `shopTargets` nhiều shop, bỏ gom nhóm theo tập shop, hộp chọn shop chỉ tick **1** shop đích (như trước plan multi-shop). Có thể giữ file `ImportBatchPlanner` chỉ phần khớp tick/lô 1 shop, hoặc đơn giản hoá cho 1 shop; không để đường «tick nhiều shop».
7. **Lưu trữ**: gỡ `BigSellerAccount.ImportAllShops`, `UpdateProductUiSettings.AutoRunImportAllShops` + `SetAutoRunImportAllShops`. JSON cũ còn field → bỏ qua (không crash).
8. **Test**: xoá hoặc viết lại `ImportAllShopsSettingTests`, `AutoRunImportOncePerAccountTests`, `ImportMultiShopTests` (và chỗ assert multi-shop) cho khớp hành vi 1 shop. Giữ / chỉnh test Auto Run Import-then-Update từng shop.

### B. Tab Crawl List = «Tất cả»

1. **BigSellerCrawlHelper**: thêm `BigSellerCrawlTab.All` + `SelectAllTabByTextAsync` / `IsAllTabActiveAsync` (text «Tất cả» / «All», `#activeTab` / `.tabs_module` như HTML user gửi). `SelectCrawlTabAsync` / `IsCrawlTabActiveAsync` nhận tab All.
2. **BigSellerImportToStoreRunner.RunAsync**: chỉ quét **một** tab `All` («Tất cả») — không còn vòng Chưa nhận rồi Đã nhận.
3. **Sau mỗi lô trên tab Tất cả**: luôn **ClickNext** (không RecheckSamePage). SP import xong vẫn nằm trong «Tất cả» → ở lại trang không dồn SP mới lên; RecheckSamePage sai ngữ cảnh.
   - Đổi `ImportScanPolicy.NextStepAfterBatch` (hoặc caller): với chế độ tab All / luôn Next sau lô đã gửi. Cập nhật unit test policy cho khớp.
4. **Luồng trang** (mỗi trang khi còn id cần import):
   - Tick các dòng khớp item id còn cần.
   - Xác nhận tick khớp lô → bấm **Import to Stores** → chọn **1** shop → xác nhận hộp kết quả → MarkDone.
   - Hết xử lý trang → Next. Hết trang hoặc `AllImported` → dừng (giữ MaxPasses / ShouldRescan hợp lý với 1 tab).
5. Log: ghi rõ đang quét tab «Tất cả».

## KHÔNG LÀM

- Không sửa Update product / Clear nháp / scrape.
- Không commit / push.
- Không đụng file `M` ngoài phạm vi import/AutoRun/Workspace liên quan (nếu gặp conflict với WIP multi-shop trong cùng file → hoàn tất theo plan này, gỡ multi-shop).
- Không sửa selector tick checkbox trừ khi bắt buộc để tab Tất cả chạy (ngoài phạm vi trừ khi block tiêu chí).

## File chính

- `src/Shopee.Module.UpdateProduct/Engine/BigSellerCrawlHelper.cs`
- `src/Shopee.Module.UpdateProduct/Engine/BigSellerImportToStoreRunner.cs`
- `src/Shopee.Module.UpdateProduct/UpdateProductRunner.cs`
- `src/Shopee.Workspace/Modules/Workspace/AutoRunView.xaml`, `AutoRunViewModel.cs`
- `src/Shopee.Workspace/Modules/Workspace/WorkspaceView.xaml`, `WorkspaceAccountViewModel.cs`, `WorkspaceShopViewModel.cs`
- `src/Shopee.Workspace/Modules/UpdateProduct/UpdateProductViewModel.cs`
- `src/Shopee.Core/BigSeller/BigSellerAccount.cs`, `ImportScanPolicy.cs`, (ImportBatchPlanner / ImportShopMatcher nếu còn multi)
- `src/Shopee.Core/Infrastructure/UpdateProductUiStore.cs`
- Tests tương ứng dưới `tests/`

## Tiêu chí đo được

1. `rg -n "Import cho tất cả các shop|ImportAllShops|AutoRunImportAllShops|RunImportAll" src tests` → **0** khớp còn dùng (cho phép comment/plan cũ ngoài src/tests nếu có; trong src+tests không còn API/UI).
2. `rg -n "Chưa nhận|Đã nhận|Unclaimed|Claimed" src/Shopee.Module.UpdateProduct/Engine/BigSellerImportToStoreRunner.cs` → vòng quét chính không còn 2 tab đó; chỉ «Tất cả» / `All`.
3. `dotnet test tests/Shopee.Core.Tests/Shopee.Core.Tests.csproj --filter "FullyQualifiedName~Import" -v q` → pass.
4. `dotnet test tests/Shopee.Workspace.Tests/Shopee.Workspace.Tests.csproj --filter "FullyQualifiedName~AutoRun|FullyQualifiedName~Import" -v q` → pass.
5. Build Workspace (`dotnet build src/Shopee.Workspace/Shopee.Workspace.csproj -v q`) → 0 error.

## Ghi chú hành vi tab Tất cả

- Sau Import, SP có thể vẫn hiện trên «Tất cả» (đổi trạng thái) → MarkDone + luôn Next, không ở lại trang vì «dồn danh sách».
