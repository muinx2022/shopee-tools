# Khôi phục Clear hộp nháp trước Import — 03/11

**Trạng thái:** XONG (03/11 — phiên chính review: Clear sau login trước Crawl List; 214+105 test pass; Release 0 error; lệch hub NothingToImport chấp nhận theo e0a9a33)

## Bối cảnh (git)

- Commit `e0a9a33` (03/11 15:17) đã làm đủ: Clear nháp trước mỗi shop + cổng chặn + reset Import Done cho SP chưa Update.
- Working tree hiện tại **tắt** Clear trong `BigSellerImportToStoreRunner` (comment «KHÔNG Clear nháp…»; không còn `ClearDraftsBeforeImportAsync`).
- Hạ tầng vẫn còn: `BigSellerDraftClearer`, `ImportScanPolicy.DecideImportStart` / `DecideClearGate` / `IsAllImported`, `OpProgressStore.ResetImportWithoutUpdate` / `ImportKeysPendingUpdate`, `WorkbookRecordCache.LoadUpdateTargetsByKeyAsync`, test `ClearGateTests`, `ImportStartDecisionTests`, `OpProgressImportResetTests`, `DraftClearerLogicTests`.

## Mục tiêu hành vi

Mỗi lần Import (thủ công + Auto Run), **sau đăng nhập, trước Crawl List**:

1. Mở trang nháp BigSeller → Bulk Actions → Clear → Confirm (đúng câu) → chờ nháp về 0.
2. Trước Confirm: reset bảng Import Done (mọi khoá tài khoản biết mục tiêu Update, SP chưa Update) qua `OpProgressStore.ResetImportWithoutUpdate`.
3. Seed lại tiến độ shop (`SeedImportedFromProgress`) rồi mới vào Crawl List (tab **Tất cả**) và import như hiện tại (giữ tick checkbox fix).

## LÀM

Chỉ sửa wiring trong runner (và test nếu cần adapt chữ ký). **Không** viết lại `BigSellerDraftClearer` / Core policy trừ khi build lỗi vì lệch API.

Nguồn tham chiếu: `git show e0a9a33:workspace/src/Shopee.Module.UpdateProduct/Engine/BigSellerImportToStoreRunner.cs` (khoảng field `_clearSettled` / `_updateTargetsByKey`; đầu `RunAsync` pendingKeys→LoadUpdateTargets→ApplyStartDecision; `ClearDraftsBeforeImportAsync` + helpers `GetPendingUpdateKeys` / `DecideStart` / `ApplyStartDecision` / `CountImportedWithoutUpdate` / `CountImportRemovableOfShop` / `LoadUpdateTargetsAsync` / `ResetImportProgressBeforeConfirmAsync` / `ResetHubImportMarksAsync`).

### Adapt với model hiện tại (`ImportShopState`)

Runner hiện dùng `_shop.State` (`ImportShopState`) thay `_importedIds` HashSet cũ:

| Chỗ cũ (e0a9a33) | Adapt |
|---|---|
| `_importedIds.Count` / seed HashSet | `_shop.State.DoneCount` + `SeedImportedFromProgress()` (đã có `State.SeedDone`) |
| `AllImported => IsAllImported(..., _clearSettled)` | `AllImported => _shop is { State.IsComplete: true } && _clearSettled` **hoặc** `ImportScanPolicy.IsAllImported(_shop.State.ImportIds.Count, _shop.State.DoneCount, _clearSettled)` |
| Early exit «đủ tiến độ → không mở Brave» | **Không** thoát chỉ vì `State.IsComplete` trước Clear. Phải `DecideImportStart` / `ApplyStartDecision` như e0a9a33 (AllDone chỉ khi Clear không làm mất SP shop). |
| Sau Clear reset | Gọi lại `SeedImportedFromProgress()` (State clear + seed từ store đã reset) |

### `RunAsync` — thứ tự bắt buộc

1. `LoadShopAsync` (như hiện tại).
2. Nếu `_shop == null` → kết thúc (như hiện tại).
3. `SeedImportedFromProgress()`.
4. `pendingKeys = GetPendingUpdateKeys()`; `hubPending`; nếu `NothingToImport` → return.
5. `LoadUpdateTargetsAsync` → `ApplyStartDecision(..., braveOpen: false)` → false thì return (có thể set `ClearBlockedReason`).
6. StartBrave → CDP → cookie → Connect → FindBigSellerPage → AttachApiCapture → EnsureFreshLogin.
7. **`if (!await ClearDraftsBeforeImportAsync(page, ct)) return;`** — thay comment «KHÔNG Clear…».
8. GoToCrawlPage (Tất cả) → vòng import hiện tại (tick JS, Ensure tick, …). **Giữ nguyên** tick/tab Tất cả.

### `ClearBlockedReason`

Khôi phục setter thật (không còn «luôn null»). `UpdateProductRunner.ImportBlockedReason = runner.ClearBlockedReason` đã có.

### KHÔNG LÀM

- Không commit/push.
- Không đụng Update runner / Clear UI riêng.
- Không bỏ cổng Clear / không nới `TickedMatchesBatch`.
- Không `git checkout` cả file runner (sẽ mất tick fix + tab Tất cả) — chỉ ghép lại khối Clear.

## File chính

- `src/Shopee.Module.UpdateProduct/Engine/BigSellerImportToStoreRunner.cs`
- Test liên quan nếu chữ ký/assert sớm thoát đổi (ưu tiên giữ test Core Clear/ImportStart xanh)

## Tiêu chí đo

1. `rg -n "ClearDraftsBeforeImportAsync|XOÁ SẠCH NHÁP TRƯỚC KHI IMPORT" src/Shopee.Module.UpdateProduct/Engine/BigSellerImportToStoreRunner.cs` → có gọi thật (không chỉ comment).
2. `rg -n "KHÔNG Clear nháp trước import" src/.../BigSellerImportToStoreRunner.cs` → **0** (đã bỏ tắt).
3. `dotnet test tests/Shopee.Core.Tests --filter "FullyQualifiedName~Import|FullyQualifiedName~Clear|FullyQualifiedName~OpProgressImport" -v q` → pass (SDK: `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` nếu PATH thiếu).
4. `dotnet test tests/Shopee.Workspace.Tests --filter "FullyQualifiedName~DraftClearer|FullyQualifiedName~AutoRunImport" -v q` → pass.
5. `dotnet build src/Shopee.Update/Shopee.Update.csproj -c Release -v q` → 0 error (đóng `ShopeeUpdate` nếu khóa DLL).

## Sau code (phiên chính)

Review diff · chạy lại tiêu chí · đóng app nếu cần · báo user chạy Auto Run / Import thủ công.
