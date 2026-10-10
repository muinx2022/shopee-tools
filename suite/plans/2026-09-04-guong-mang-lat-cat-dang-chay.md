# Plan: Gương (mirror) mang tên LÁT CẮT đang chạy của từng tài khoản — hub hiện đúng "✖ Dừng" khi KM/Combo/Flash đang chạy

- **Ngày:** 2026-09-04
- **Trạng thái:** hoàn thành code (`454f1bd`); N8 (deploy / chạy thật) còn lại
- **Người lập:** Fable · **Người thực thi:** `opus-dev`
- **Gốc:** sổ **P-20260904-12** (VỪA) — phản biện lượt 2 đợt Hub-điều-khiển nâng thành **NẶNG-B** vì nó chặn đúng
  mục đích của đợt: *"cho phép dừng các chương trình KM, Combo, Flash Sale từ hub"* (user 03/09).

## 1. Bối cảnh & mục tiêu

Gương đẩy lên hub chỉ có **trạng thái phiên check đơn** (`OrdersModuleHost.Mirror.cs:151-158 MirrorSessionState(
services.Sessions.Get(acc.Id)?.State)`), và `OrdersSessionStates.Idle == ""` (`HubDtos.cs:346`). Khi một lát cắt
KM/Combo/Flash chạy, `MuonHoSoAsync` đã **dừng mọi phiên** và chờ `Sessions.Active` rỗng ⇒ gương báo `""` (rảnh).

Hệ quả trên hub (bằng chứng phản biện 04/09):
- Tab Đơn hàng `DispatchOrdersTab.razor:364-366`: `if (a.SessionState.Length > 0) return new("✖ Dừng", StopAll…)`
  ⇒ đang chạy flash mà nút hiện là **"▶ Chạy"**. Bấm nó ⇒ `LenhHubChayCheckDon` bật nhường ⇒ **gói flash bị ép
  nhường sau shop hiện tại** — ngược ý "dừng" lẫn luật ưu tiên flash.
- Panel KPI `DispatchRowsBuilder.cs:214-225` chỉ xếp dòng vào `running/queued` theo state ∈ {opening, running,
  stopping, queued} ⇒ **không có dòng, không có nút ✖**.
- Ba nút ▶ KM/Combo/Flash luôn sáng; người vận hành chỉ biết bị từ chối sau ack (~5–12s).

Tooltip vừa sửa hứa *"Dừng mọi hoạt động của {login}: check đơn + lượt KM/Combo/Flash đang chạy"* — lời hứa
không bấm tới được.

**Mục tiêu:** hub biết **tài khoản nào trên máy đang giữ lát cắt gì** và (a) hiện "✖ Dừng" cho tài khoản đó,
(b) hiện tên lát cắt ở ô trạng thái, (c) khoá ba nút ▶ KM/Combo/Flash của **mọi tài khoản trên máy đó** khi máy
đang chạy một lát cắt (một máy = một lát cắt tại một thời điểm — đợt đa làn sẽ nới, không phải việc này).

**Đã chốt, không mở lại:** lệnh `stop-all` per-tài-khoản (04/09); danh tính gương là (máy, login); DTO thêm trường
**nullable** để client cũ/hub cũ không hỏng; không persist tiến độ flash.

## 2. Phạm vi

- **Làm:** hook client → gương → DTO → hub DB → 2 chỗ UI + rows builder + đếm running; test đủ 3 tầng.
- **Không làm:** đổi nghĩa `stop`/`stop-all`; per-acc hoá `_bridgeRunning` (đợt đa làn); tiến độ shop k/n lên hub;
  bUnit (chưa có hạ tầng — P-20260904-13); heartbeat máy (`OrdersSlotHeartbeat`).

## 3. Các bước thực hiện

1. **Client — nguồn sự thật** (`orders/XuLyDonShopee.App`):
   - `Services/AppServices.cs`: thêm hook `public Func<long, string?>? LatCatDangChayCua { get; set; }` cạnh
     `LenhTuHub` (cùng khuôn xmldoc): *trả tên việc (`TenLatCatDangChay`) nếu máy đang chạy lát cắt VÀ lát cắt
     thuộc `accountId`; ngược lại `null`.*
   - `ViewModels/AccountsViewModel.Phien.cs`: nơi gán `LenhTuHub` (constructor/`GanHookHub`) gán thêm
     `LatCatDangChayCua = id => _bridgeRunning && TaiKhoanLatCatDangChay == id ? TenLatCatDangChay : null;`
     (đọc ba trường thật, không seam). Thêm hàm thuần `internal static string? LatCatCua(bool bridgeRunning,
     long? taiKhoanLatCat, string? ten, long accountId)` và dùng nó — để test ma trận.
2. **DTO** (`suite/Shopee.Core/Coordination/HubDtos.cs`): `OrdersMirrorAccount` thêm `public string? LatCat { get;
   init; }` (nullable, **không** `[Required]`; xmldoc: "tên lát cắt KM/Combo/Flash/Mở list shop mà máy đang chạy
   cho login này; null = không").
3. **Gương** (`suite/Shopee.Suite/Infrastructure/OrdersModuleHost.Mirror.cs:151-158`): khi dựng từng
   `OrdersMirrorAccount`, `LatCat = services.LatCatDangChayCua?.Invoke(acc.Id)`. Đảm bảo gương **được đẩy lại**
   khi lát cắt bắt đầu/kết thúc: nếu nhịp gương chỉ chạy theo `Sessions.Changed`, thêm một nguồn kích
   (`AppServices.LatCatDoi` event hoặc gọi thẳng hàm đẩy gương) ở hai điểm `_bridgeRunning = true/false`
   (`Phien.cs:3328`, `:3525`) — nêu rõ trong báo cáo cách chọn và vì sao.
4. **Hub DB** (`server/Shopee.Hub.Web/Data/HubDatabase.OrdersAccounts.cs`): cột `lat_cat TEXT NULL` theo khuôn
   migration hiện có của hub (tìm `EnsureColumn`/`ALTER TABLE` trong `HubDatabase*.cs`); upsert ghi `LatCat`;
   model dòng đọc ra `LatCat`. `:333` phép đếm "Running" của máy **cộng** các dòng có `LatCat != null` (không đếm
   đúp nếu vừa Running vừa LatCat).
5. **Rows builder** (`server/Shopee.Hub.Web/Components/DispatchRowsBuilder.cs:214-225`): dòng có `LatCat != null`
   vào nhóm `running` với nhãn trạng thái = `LatCat` (ví dụ "Flash Sale", "KM của Shop"); giữ nguyên các nhóm khác.
6. **UI** (`DispatchOrdersTab.razor:364-366` + nơi render 3 nút ▶ KM/Combo/Flash `:83-89`; `DispatchKpiPanel.razor`):
   - nút chính = "✖ Dừng" khi `SessionState.Length > 0 || !string.IsNullOrEmpty(LatCat)`;
   - ô trạng thái hiện `LatCat` (kèm ký hiệu ⚡/🏷/📦 tuỳ tên — tra `DispatchViewLogic` nếu đã có bảng nhãn);
   - ba nút ▶ KM/Combo/Flash `disabled` + tooltip *"Máy đang chạy {LatCat} cho {login}"* khi **bất kỳ** tài khoản
     nào cùng `MachineId` có `LatCat != null`. Logic đặt ở `DispatchViewLogic` (hàm thuần, test được), Razor chỉ gọi.
7. **Tài liệu:** sổ P-20260904-12 → `ĐANG SỬA` rồi `ĐÓNG (<commit>)`; CHANGELOG Unreleased một dòng.

## 4. Tiêu chí nghiệm thu

- [x] **N1** `ShopeeSuite.sln` + `server/ShopeeHub.sln`: 0 warning 0 error.
- [x] **N2** Client: test ma trận `LatCatCua` (4 ca: không chạy → null; chạy của A hỏi A → tên; chạy của A hỏi B →
      null; tên null → null). Thử phá: đảo điều kiện chủ ⇒ ĐỎ. *(nghiệm thu lượt 2: 12/12 xanh; thử phá do opus-dev, nghiệm thu không tự dựng lại)*
- [x] **N3** Suite: test gương (khuôn `LenhHubQuaHookTests`) — hook trả "Flash Sale" cho acc A ⇒ DTO A `LatCat ==
      "Flash Sale"`, B `null`; hook null ⇒ mọi `LatCat` null (client cũ). Thử phá: gương không gọi hook ⇒ ĐỎ. *(4/4 xanh; thử phá cùng ghi chú N2)*
- [x] **N4** Hub DB: upsert dòng có `LatCat` rồi đọc lại đúng; dòng không có ⇒ null; DB cũ (chưa cột) mở được và
      migration thêm cột. Thử phá: bỏ cột khỏi UPDATE ⇒ ĐỎ.
- [x] **N5** Rows builder + `DispatchViewLogic`: (a) state "" + LatCat "Flash Sale" ⇒ nhóm running, nhãn "Flash
      Sale", nút chính "✖ Dừng"/`StopAll`; (b) máy có một dòng LatCat ⇒ ba nút ▶ vòng của **mọi** dòng cùng máy
      disabled với tooltip đúng; máy khác không bị; (c) không LatCat ⇒ hành vi cũ nguyên (test cũ xanh). Thử phá
      từng vế ⇒ ĐỎ.
- [x] **N6** Đếm Running của máy (`:333`) cộng LatCat, không đếm đúp — test.
- [x] **N7** 5 bộ test như đợt trước xanh (orders/Core/Suite/Search/Hub), đo ở worktree, ghi số.
- [ ] **N8 (chạy thật, chặng 5)**: deploy hub + app; khi gói flash đang chạy trên máy này, hub tab Đơn hàng hiện
      "✖ Dừng" + "Flash Sale" ở dòng tài khoản đó, ba nút ▶ vòng mờ; bấm ✖ Dừng ⇒ log máy "Hub ra lệnh… Dừng",
      gói dừng, tài khoản khác (nếu có) không bị đụng.

## 5. Rủi ro & lưu ý

- Gương chỉ đẩy khi có thay đổi ⇒ nếu quên kích ở lúc lát cắt bắt đầu/kết thúc thì hub thấy trễ tới nhịp kế
  (60s) hoặc không bao giờ — bước 3 phải chứng minh đường kích (test hoặc đọc code có trích dòng).
- Nullable DTO: hub cũ nhận client mới → bỏ qua trường lạ (System.Text.Json mặc định) — xác nhận không bật
  `UnmappedMemberHandling.Disallow`. Client cũ → hub mới: `LatCat` null ⇒ hành vi cũ.
- Không đụng `Phien.cs` ngoài một dòng gán hook + hàm thuần + (có thể) hai lời gọi kích gương; file này vừa qua 3
  lượt sửa — diff phải nhỏ và đọc được.
- Môi trường: app thật đang chạy trên máy dev (khoá bin + cổng); build/test ở worktree tách; không chạy app.

---

## Báo cáo thực thi (opus-dev điền)

**Bối cảnh phiên này:** tiếp nối một agent trước bị cắt giữa chừng do hết hạn mức (không phải lỗi logic). Cây
làm việc đã có sẵn 8 bước đầu của mục 3 + phần lớn bước 6 (UI). Việc của phiên này: soát lại đúng-ý-plan từng
bước, tự làm nốt phần UI/test còn thiếu, thử phá toàn bộ test mới.

### Đối chiếu bước 1–7 với plan mục 3

1. **Client — nguồn sự thật** (`AppServices.cs`, `AccountsViewModel.Phien.cs`, `AccountsViewModel.cs`): **ĐÃ CÓ
   SẴN đúng.** Hook `LatCatDangChayCua` đúng chữ ký `Func<long, string?>?`, cùng khuôn xmldoc với `LenhTuHub`.
   Hàm THUẦN `internal static string? LatCatCua(bool bridgeRunning, long? taiKhoanLatCat, string? ten, long
   accountId)` tách đúng khỏi VM (đọc 3 trường thật qua wrapper `LatCatDangChayCua(long)`), test ma trận 4 ca đủ
   trong `LatCatChoGuongTests.cs` (10 test, N2 đạt). Rót/gỡ hook đúng khuôn `_hookLenhHub` (giữ tham chiếu để
   `Dispose` gỡ đúng hook của chính VM).
2. **DTO** (`HubDtos.cs`): **ĐÃ CÓ SẴN đúng.** `OrdersMirrorAccount.LatCat` là `string?` trong THÂN record
   (property init), KHÔNG `[Required]`, không tham số vị trí — đúng luật "client cũ/hub cũ an toàn" mà class đã
   nêu.
3. **Gương** (`OrdersModuleHost.Mirror.cs`): **ĐÃ CÓ SẴN đúng, đây là điểm dễ sai nhất và đã làm ĐÚNG.** Gương
   ĐƯỢC ĐẨY LẠI ở cả hai đầu lát cắt, không chỉ theo nhịp 60s — xem "Cách gương được đẩy lại" bên dưới.
4. **Hub DB** (`HubDatabase.OrdersAccounts.cs`, `HubDatabase.cs`): **ĐÃ CÓ SẴN đúng.** Cột `lat_cat TEXT DEFAULT
   ''` có cả trong `CREATE TABLE IF NOT EXISTS` (DB mới) lẫn `AddColumnIfMissing` (DB cũ). Upsert ghi `$lc =
   a.LatCat?.Trim() ?? ""`, **KHÔNG** đi qua `GiuLaiNeuRong` (đúng ý: lát cắt là trạng thái sống, không sticky
   như 3 ô đăng nhập). `OrdersMirrorCounts()` đếm bằng MỘT `CASE WHEN session_state<>'' OR
   COALESCE(lat_cat,'')<>''` — không phải hai SUM cộng lại, nên không đếm đúp. `OrdersRunningAccounts()` cùng
   luật OR ở WHERE.
5. **Rows builder** (`DispatchRowsBuilder.cs`): **ĐÃ CÓ SẴN đúng.** Nhánh `LatCat != null` xét TRƯỚC switch
   `SessionState` và `continue` ngay sau khi add vào `running` — đây chính là chỗ chống đếm đúp (dòng có cả hai
   trường chỉ vào bảng một lần).
6. **UI** (`DispatchOrdersTab.razor`, `DispatchViewLogic.cs`, `DispatchKpiPanel.razor`): **ĐÃ CÓ SẴN đúng, KHÔNG
   dở dang như báo cáo cuối của agent trước lo ngại.** Đọc kỹ toàn bộ diff xác nhận đủ 3 phần plan yêu cầu:
   - Nút chính "✖ Dừng" khi `DispatchViewLogic.OrdersDangBan(a.SessionState, a.LatCat)` — hàm THUẦN, không
     `[Required]` gì thêm, Razor chỉ gọi.
   - Ô trạng thái: `OrdersTrangThaiLabel`/`OrdersTrangThaiCss` (LatCat thắng session, icon theo
     `LatCatIcon`) + tooltip `OrdersTrangThaiTitle` giải thích "không phải phiên check đơn".
   - Ba nút ▶ KM/Combo/Flash: `DispatchViewLogic.LatCatCuaMay(_oAccounts)` quét MỌI tài khoản của máy đang chọn
     (`_oAccounts = Db.OrdersAccountsOf(_oMach)` — đã tự nhiên scope theo máy), trả `(LatCat, Login)` đầu tiên
     tìm thấy hoặc `null`; Razor dùng để khoá cả ba nút + tooltip "Máy đang chạy … cho …".
   - Cột "Việc" của bảng KPI (`DispatchKpiPanel.razor`): `WorkLabel(w)` thay `OpLabel(w.Op)` — dòng Đơn hàng có
     LatCat nói đúng tên lát cắt.

   **KHÔNG có phần nào phải TỰ LÀM MỚI ở tầng nguồn** — toàn bộ 8 bước đã đúng ý plan. Việc thật sự còn thiếu là
   **TEST**: `DispatchViewLogicTests.cs` (cũ) hoàn toàn chưa có test nào cho `OrdersDangBan` / `LatCatIcon` /
   `OrdersTrangThaiLabel`/`Css` / `WorkLabel` / `LatCatCuaMay`, và `DispatchRowsBuilder`/`HubDatabase` chưa có
   test LatCat nào — đây là phần tôi viết mới (mục dưới).
7. **Tài liệu** (sổ + CHANGELOG): **CHƯA LÀM** — nằm ngoài phạm vi giao cho phiên này (hướng dẫn cấm sửa
   `SO-TAY.md`, không nhắc CHANGELOG). Để phiên chính xử lý ở chặng 5.

### Lỗi build đã sửa (nằm trong file test có sẵn, không phải phần mới)

`suite/Shopee.Suite.Tests/GuongLatCatQuaHookTests.cs` gọi `_services.Dispose()` nhưng `AppServices` không có
`Dispose` (không implement `IDisposable`) → **build lỗi CS1061**, chặn cả `ShopeeSuite.sln`. Đây CHẶN đúng việc
đang làm (không sửa thì không build được để chạy test) nên đã sửa luôn, theo đúng khuôn `Dispose()` của
`LenhHubQuaHookTests.cs`/`KmApHoSoVeMayTests.cs` trong cùng thư mục (`_services.Log.Dispose()` +
`SqliteConnection.ClearPool` + xoá file DB), thêm `using Microsoft.Data.Sqlite;`. Không đổi ý đồ test, chỉ sửa
phần dọn dẹp.

### Cách gương được đẩy lại khi lát cắt bắt đầu/kết thúc (trích dòng)

Hai nguồn kích cờ bẩn `_mirrorDirty` gộp trong `OrdersModuleHost.RotNguonKichGuong`:
```csharp
internal static void RotNguonKichGuong(AppServices services)
{
    services.Sessions.Changed += () => _mirrorDirty = true;
    services.LatCatDoi = () => _mirrorDirty = true;
}
```
`AppServices.LatCatDoi` là "tiếng chuông bẩn" không mang dữ liệu, được `AccountsViewModel.Phien.cs` gọi ở ĐÚNG
hai điểm đặt/xoá cờ cầu nối:
- Lúc bắt đầu (`_bridgeRunning = true` xong): `_services.LatCatDoi?.Invoke();` ngay sau khi gán
  `TaiKhoanLatCatDangChay = accountId;`.
- Lúc kết thúc (khối `finally`, `_bridgeRunning = false`): `_services.LatCatDoi?.Invoke();` ngay sau khi xoá
  `TenLatCatDangChay`/`TaiKhoanLatCatDangChay`.

Test `GuongLatCatQuaHookTests.LatCatDoi_BatCoBanCuaWorkerGuong` đo trực tiếp đường kích này (gọi
`RotNguonKichGuong` rồi `services.LatCatDoi!.Invoke()`, xác nhận `OrdersModuleHost._mirrorDirty` bật).

### Khuôn migration hub dùng cho cột `lat_cat`

Theo đúng khuôn `AddColumnIfMissing` đã có (vd `password`, `verify_email` ngay phía trên):
```csharp
AddColumnIfMissing("orders_accounts", "lat_cat", "TEXT DEFAULT ''");
```
gọi trong `MigrateSchema()` (chạy sau `EnsureSchema()` ở ctor `HubDatabase`) — DB mới đã có cột nhờ `CREATE
TABLE IF NOT EXISTS` trong `EnsureOrdersAccountsSchema`, DB cũ (bảng đã tồn tại, thiếu cột) được vá bởi dòng
trên. Test `HubOrdersLatCatTests.DbCu_ThieuCotLatCat_MigrationThemVaVanUpsertDuoc` dựng bảng `orders_accounts`
kiểu cũ (thiếu `lat_cat`) bằng SQL thô rồi mở `HubDatabase` lại — đúng khuôn `DispatchRunParamsTests` dùng cho
migration `rest_min_sec`/`rest_max_sec`.

### File đã sửa/tạo trong phiên này

- `suite/Shopee.Suite.Tests/GuongLatCatQuaHookTests.cs` — sửa `Dispose()` (lỗi build, xem trên); sau đó chuẩn
  hoá về CRLF (file gốc do agent trước ghi ra là LF thuần trong khi cây làm việc dùng CRLF — `git diff` cảnh
  báo "LF will be replaced by CRLF"; nội dung không đổi, chỉ thêm `\r` trước mỗi `\n`).
- `server/Shopee.Hub.Web.Tests/DispatchLatCatViewTests.cs` — **MỚI**, 20 test cho `DispatchViewLogic`:
  `OrdersDangBan` (5 ca), `LatCatIcon` (7 ca), `OrdersTrangThaiLabel`/`Css` (2), `WorkLabel` (2),
  `LatCatCuaMay` (4, gồm ca "máy khác không bị").
- `server/Shopee.Hub.Web.Tests/DispatchRowsBuilderLatCatTests.cs` — **MỚI**, 5 test cho
  `DispatchRowsBuilder.BuildWorkLists`: state rỗng + LatCat vào nhóm running; chống đếm đúp khi có cả hai
  trường; máy offline bị lọc dù có LatCat; hai ca (c) hồi quy hành vi cũ (queued/running không LatCat).
- `server/Shopee.Hub.Web.Tests/HubOrdersLatCatTests.cs` — **MỚI**, 7 test cho `HubDatabase`: upsert/đọc LatCat,
  LatCat KHÔNG sticky (lượt sau xoá được nhãn), migration DB cũ, `OrdersMirrorCounts` cộng LatCat không đếm
  đúp, `OrdersRunningAccounts` thấy dòng chỉ có LatCat.

Không sửa file nguồn nào khác trong 8 bước 1–6 — đúng như đối chiếu ở trên, chúng đã đúng ý plan.

### Build

- `ShopeeSuite.sln` (worktree tách, `--no-incremental`): **Build succeeded — 0 Warning(s), 0 Error(s)**.
- `server/ShopeeHub.sln` (worktree tách, `--no-incremental`): **Build succeeded — 0 Warning(s), 0 Error(s)**.

### Test (đo ở worktree tách, một bộ tại một thời điểm)

| Bộ | Kỳ vọng plan | Kết quả thật |
|---|---|---|
| `orders/XuLyDonShopee.Tests` | ≥2377 + bài mới | **2387/2387 pass** (2377 nền + 10 `LatCatChoGuongTests`) |
| `suite/Shopee.Core.Tests` | 457 | **457/457 pass** |
| `suite/Shopee.Suite.Tests` | 87 + mới | **91/91 pass** (87 nền + 4 `GuongLatCatQuaHookTests`) |
| `suite/Shopee.Module.Search.Tests` | 34 | **34/34 pass** |
| `server/Shopee.Hub.Web.Tests` | 222 + mới | **254/254 pass** (222 nền + 32 test LatCat mới của phiên này) |

Không gặp bài flaky nào trong cả 5 lượt chạy (kể cả `CauNoiRotGiuaChangTests` không xuất hiện trong bộ
`orders`).

### Bảng thử phá (mutate → ĐỎ → khôi phục, tất cả trong worktree, khôi phục xác nhận bằng `diff -q` khớp cây
chính)

| # | Đột biến | File | Bài bị bắt | Kết quả |
|---|---|---|---|---|
| 1 | `OrdersDangBan`: `\|\|` → `&&` | `DispatchViewLogic.cs` | `OrdersDangBan_XetCaSessionLanLatCat` | ĐỎ (2/5 ca) → khôi phục |
| 2 | `LatCatIcon`: icon Flash → 📦 (đổi icon) | `DispatchViewLogic.cs` | `LatCatIcon_TraKyHieuDungNhom` | ĐỎ (3/7 ca) → khôi phục |
| 3 | `OrdersTrangThaiCss`: bỏ nhánh LatCat, luôn trả `OrdersStateCss` | `DispatchViewLogic.cs` | `OrdersTrangThai_CoLatCat_NhanTenLatCatMauRun` | ĐỎ ("run"→"idle") → khôi phục |
| 4 | `WorkLabel`: bỏ nhánh LatCat | `DispatchViewLogic.cs` | `WorkLabel_DongDonHangCoLatCat_NoiTenLatCat` | ĐỎ → khôi phục |
| 5 | `LatCatCuaMay`: luôn trả `null` | `DispatchViewLogic.cs` | `LatCatCuaMay_MotDongChayLatCat_TraDungTenVaLogin` + `..._MayKhac_KhongThay...` | ĐỎ (2 bài) → khôi phục |
| 6 | Bỏ `continue` sau nhánh LatCat | `DispatchRowsBuilder.cs` | `CoCaHaiTruong_ChiMotDongDuyNhat` | ĐỎ (2 dòng thay vì 1) → khôi phục |
| 7 | Bỏ hẳn nhánh `if (!string.IsNullOrEmpty(s.LatCat))` | `DispatchRowsBuilder.cs` | `StateRong_CoLatCat_VaoNhomRunning` | ĐỎ (rỗng) → khôi phục |
| 8 | Vô hiệu lọc `MachineOffline` (`if (false) continue;`) | `DispatchRowsBuilder.cs` | `MayOffline_DuCoLatCat_ThiKhongVaoBangNao` | ĐỎ → khôi phục |
| 9 | `case Queued` add vào `running` thay vì `queued` | `DispatchRowsBuilder.cs` | `KhongLatCat_StateQueued_HanhViCuNguyen` | ĐỎ → khôi phục |
| 10 | Bỏ `case Running` khỏi switch | `DispatchRowsBuilder.cs` | `KhongLatCat_StateRunning_HanhViCuNguyen` | ĐỎ (rỗng) → khôi phục |
| 11 | Mô phỏng áp `GiuLaiNeuRong` lên `lat_cat` (đọc giá trị cũ, giữ nếu mới rỗng) | `HubDatabase.OrdersAccounts.cs` | `LatCatXong_LuotDayKeTiepKhongMangTheo_ThiXoaNhan` | ĐỎ (kẹt "Flash Sale") → khôi phục |
| 12 | `OrdersMirrorCounts`: 2 `SUM` cộng lại thay vì 1 `CASE...OR` | `HubDatabase.OrdersAccounts.cs` | `MirrorCounts_CoCaHaiTruong_ChiDemMotKhongDemDup` | ĐỎ (2 thay vì 1) → khôi phục |
| 13 | Bỏ dòng `AddColumnIfMissing("orders_accounts","lat_cat",...)` | `HubDatabase.cs` | `DbCu_ThieuCotLatCat_MigrationThemVaVanUpsertDuoc` | ĐỎ (`SqliteException: no such column: lat_cat`) → khôi phục |
| 14 | `OrdersAccountsOf`: luôn gán `LatCat = null` | `HubDatabase.OrdersAccounts.cs` | `Upsert_CoLatCat_DocLaiDung` | ĐỎ (null thay vì "Flash Sale") → khôi phục |
| 15 | `OrdersRunningAccounts`: bỏ vế `OR COALESCE(lat_cat,'')<>''` khỏi WHERE | `HubDatabase.OrdersAccounts.cs` | `RunningAccounts_ChiCoLatCat_VanXuatHienChoKpi` | ĐỎ (rỗng) → khôi phục |
| 16 | `OrdersMirrorCounts`: bỏ vế LatCat khỏi CASE (chỉ còn `session_state<>''`) | `HubDatabase.OrdersAccounts.cs` | `MirrorCounts_ChiCoLatCat_VanDemLaRunning` | ĐỎ (0 thay vì 1) → khôi phục |

Tổng 16 đột biến, mọi test MỚI của phiên này (32 test Hub.Web.Tests) đều có ít nhất một đột biến bắt được. Sau
mỗi đột biến đã `cp -p` từ cây chính đè lại và xác nhận `diff -q` khớp; **không có đột biến nào chạm cây chính**
(mọi thao tác mutate chỉ trong worktree tách).

10 test `LatCatChoGuongTests.cs` và 4 test `GuongLatCatQuaHookTests.cs` (bao gồm cả bài `LatCatDoi_BatCoBanCuaWorkerGuong`
đo đường kích gương) đã được agent trước thử phá — xem lịch sử hội thoại trước, không lặp lại ở đây vì file
không đổi (ngoại trừ sửa `Dispose()` — mutation ảnh hưởng cleanup, không ảnh hưởng assertion, nên không cần thử
phá lại).

### Điểm LỆCH so với plan

- **Không có lệch về logic/hành vi.** 8/8 bước nguồn (client, DTO, gương, Hub DB, rows builder, UI) đã đúng ý
  plan ngay từ khi tiếp nhận — báo cáo cuối của agent trước ("Now the orders tab UI") gây hiểu nhầm là UI dở
  dang, nhưng đọc kỹ diff xác nhận UI đã hoàn chỉnh; phần thật sự thiếu chỉ là test tầng Hub.Web (N5/N4/N6).
- Bước 7 (tài liệu: sổ + CHANGELOG) **chưa làm** — nằm ngoài phạm vi hướng dẫn của phiên này (cấm sửa
  `SO-TAY.md`), để phiên chính xử lý.
- N8 (chạy thật qua deploy) **chưa làm** — hướng dẫn phiên này giới hạn build/test worktree, không được đụng app
  thật đang chạy trên máy dev; đây là bước "chặng 5" của plan, thuộc phiên chính.
- Một lỗi build (`GuongLatCatQuaHookTests.Dispose()`) đã phải sửa để build được — đây là ca CHẶN đúng việc đang
  làm (không sửa thì `ShopeeSuite.sln` không build, không thể chạy bất kỳ test nào), đã nêu rõ ở trên theo đúng
  luật "một việc một lúc — CHẶN thì xử, nói rõ vì sao".
- Một file (`GuongLatCatQuaHookTests.cs`, tạo bởi agent trước) có line-ending LF thuần trong khi cây làm việc
  quy ước CRLF (`git diff` cảnh báo). Đã chuẩn hoá về CRLF trong lượt này (không đổi nội dung).

## NGOÀI PHẠM VI — đề nghị ghi sổ

- không có.

## Chặng 5 — phiên chính (04/09 tối)

- Nghiệm thu lượt 2 (worktree `_wt-nt-guong`, SDK 8.0.424 user-local): N1/N4/N5/N6/N7 **ĐẠT**; N2/N3 **ĐẠT MỘT PHẦN** (12/12 và 4/4 xanh, thiếu tự dựng lại thử phá). Tổng **3229 passed / 0 failed** (orders 2389 · Core 457 · Suite 91 · Search 34 · Hub.Web 258). Build 2 sln 0 warning 0 error.
- Phản biện: không NẶNG. Hai VỪA ngoài phạm vi (cửa sổ "Mở list shop" / backoff 60s khi đẩy gương hỏng) → sổ P-20260904-17 bổ sung, P-20260904-23.
- Bước 7: CHANGELOG Unreleased + sổ (P-20260904-12 chờ đóng theo commit).
- **N8 còn lại:** deploy Hub + build/restart app ở khe an toàn (sudo + không restart giữa lát cắt).
