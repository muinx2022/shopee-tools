# Hub điều khiển KM / Combo / Flash (chạy + dừng) và ƯU TIÊN FLASH — 2026-09-03

- **Ngày:** 2026-09-03 (đêm) · **Trạng thái:** đang chờ (thi công SAU khi đợt `2026-09-03-check-don-khong-de-flash-va-tham-so-flash-tu-app.md` xong — cùng file)
- **Người lập:** phiên chính · **Thực thi:** `opus-dev` · **Nghiệm thu:** `nghiem-thu` · **Phản biện:** `phan-bien`

User chốt 23:1x–23:2x 03/09:
1. *"bạn làm phần dừng từ hub đi, cho phép dừng các chương trình km, combo, flash sale từ hub, cho chạy từ hub"*.
2. *"và ưu tiên flash sale, nếu flash sale đang chạy, không chạy các flow khác"*.

Gộp xử lý sổ: **P-20260903-8** (Dừng từ Hub chỉ dừng check đơn, không tắt lượt đang chạy, không huỷ lượt hoãn).

## 1. Bối cảnh — đủ để người thực thi không đọc lại chat

### 1.1 Đường lệnh Hub → client hiện có
- DTO: `suite/Shopee.Core/Coordination/HubDtos.cs:523` `OrdersCommandActions { run, stop, sync-once, relogin }`,
  `OrdersCommandStatuses { pending, sent, done, failed }`, `OrdersCommandDto(Id, Login, Action)`, `OrdersCommandAckRequest(Id, Status, Error)`.
- Server tạo lệnh: `HubDatabase.CreateOrdersCommand(machineId, login, action)` (`server/Shopee.Hub.Web/Data/HubDatabase.OrdersAccounts.cs:~389`,
  idempotent theo (máy, login, action) đang mở); lệnh đi trong phản hồi heartbeat (`HubDatabase.Machines.cs:169 TakeOrdersCommandsLocked`);
  ack `Api/ClientApiEndpoints.cs:262`. UI: `Components/Shared/DispatchOrdersTab.razor:345 OrdersRunBtn` (MỘT nút/tài khoản:
  "▶ Chạy"=`run` hoặc "✖ Dừng"=`stop` khi `SessionState` khác rỗng) + `Pages/Dispatch.razor.cs:258 OnOrdersStop` (bảng KPI).
  Nhãn: `Components/DispatchViewLogic.cs:93 OrdersActionLabel`. Test: `Shopee.Hub.Web.Tests/DispatchViewLogicTests.cs`, `KmKhungTests.cs`.
- **Hub TỰ phát `stop`/`run`** cho khung xuyên máy: `HubDatabase.KmKhung.cs:76` (Stop máy đang giữ check đơn), `:109/:149`
  (Run trả lại) ⇒ **KHÔNG được đổi nghĩa `stop`** (nó cố ý chỉ dừng check đơn để nhường khung).
- Client nhận: `suite/Shopee.Suite/Infrastructure/OrdersModuleHost.Mirror.cs:176 RunOrdersCommands` (khử trùng lặp `_cmdSeen`)
  → `:212 ExecuteOrdersCommand(AppServices services, cmd)` **static, chạy trên luồng poll**: `run` → `services.Sessions.Start(acc.Id)`;
  `stop` → `services.Sessions.Stop(acc.Id)`; khác → `Failed "bản client này chưa hỗ trợ lệnh"`. `AppServices` là
  `OrdersModuleHost.Services` (`OrdersModuleHost.cs:25`, `WireOrdersMirror(Services)` `:57`) — CÙNG instance với
  `XuLyDonShopee.App.ViewModels.AccountsViewModel` (dựng qua `orders/XuLyDonShopee.App/ViewModels/MainViewModel.cs:27
  new AccountsViewModel(services)`). ⚠ `new AccountsViewModel()` ở `ShellViewModel.cs:130` là lớp KHÁC (`Modules/Accounts` của
  suite), đừng nhầm.
- Hệ quả hôm nay (P-20260903-8): Hub `stop` không cancel `_bridgeCts` (lượt KM/Combo/Flash chạy tiếp), không đặt
  `_nguoiDungDungTay` (gói xong app tự chạy lại check đơn), không huỷ lượt hoãn. Hub `run` gọi thẳng `Sessions.Start` ⇒ **đi vòng
  qua cửa từ chối** `CauTuChoiViLatCatDangChay` (`AccountsViewModel.Phien.cs:1631`) của app ⇒ từ Hub có thể mở check đơn đè lên
  gói flash đang chạy (đúng cổng 47821 — P-20260903-1).

### 1.2 Các đường chạy/dừng trong app (để tái dùng, KHÔNG viết lại)
- Ribbon "Dừng tất cả" = `StopAllAsync` (`Phien.cs:~1750`): `_nguoiDungDungTay = true` → `HuyLuotHoanChayLaiCheckDon` →
  `DongCuaSoXemVaBao` → `TryKillPoc()` (cancel `_bridgeCts` + đóng Brave của lượt) → `Sessions.Stop` mọi tài khoản.
- Chạy tay: `ChayFlashSaleTayAsync` (`:2320`, ẩn sau `SHOPEE_DEV_TOOLS`), KM/Combo qua form (`KmDialogViewModel`); đường nhịp dựng
  tham số từ cấu hình đã lưu: `DungThamSoKm(cf, bayGio)` (`:1109`), `DungThamSoCombo(cf, bayGio)` (`:1089`), flash
  `new ThamSoFlashSale(NgayDichFlash(bayGio), cf.FlashPhanTramGiam, cf.FlashSoLuongThamGia)` (sau đợt trước). Entry:
  `ChayKmAsync`/`ChayComboAsync(accountId, email, thamSo, hanChot)` (`:400`), `ChayFlashSaleAsync(accountId, email, thamSo,
  hanChot, boQuaShop, phaiNhuongSauShop)` (`:467`, chữ ký SAU đợt trước — đọc lại file khi thi công).
- Mọi lát cắt đi qua `ChayLatCatAsync` (`:2359`): cờ `_bridgeRunning` + `_bridgeCts`, tự tạm dừng check đơn, trả hồ sơ ở cuối.
  Nhịp tự động: `AccountsViewModel.KetQua.cs:225 ChayNhipLichKmAsync` gọi tuần tự KM → Combo → Flash mỗi 60s; mỗi nhịp có cửa
  "đang chạy lát cắt" (`_bridgeRunning`) và cờ riêng `_dangChayKmTuDong/_dangChayComboTuDong/_dangChayFlashTuDong`.

### 1.3 Thiết kế chốt
**Hành động Hub mới** (`OrdersCommandActions`): `stop-all`, `run-km`, `run-combo`, `run-flash`. Giữ nguyên `run`, `stop`
(dùng bởi tự động hoá khung xuyên máy). Nhãn: "✖ Dừng tất cả", "▶ KM", "▶ Combo", "▶ Flash".

**Client: MỘT cửa vào duy nhất qua VM.** `AppServices` thêm hook `Func<string action, long accountId, string email,
(string Status, string? Error)>? LenhTuHub { get; set; }` (đăng ký trong ctor `AccountsViewModel(services)`, huỷ ở Dispose).
`ExecuteOrdersCommand` với `run`, `stop-all`, `run-km`, `run-combo`, `run-flash` ⇒ **gọi hook** (hook tự `RunOnUi` và trả kết
quả); hook null (VM chưa dựng) ⇒ `Failed "app chưa sẵn sàng"`. `stop` giữ nguyên `Sessions.Stop` (tự động hoá khung). `sync-once`,
`relogin` giữ nguyên.

**Ưu tiên flash (luật user):**
- `run-km` / `run-combo` khi `_dangChayFlashTuDong || (_bridgeRunning && lát cắt hiện tại là Flash)` ⇒ `Failed "Flash Sale đang chạy
  — ưu tiên flash, thử lại sau khi gói xong"`. Khi lát cắt KHÁC đang chạy (KM/Combo/Mở list shop) ⇒ `Failed` với
  `CauTuChoiViLatCatDangChay` (không cắt ngang lát cắt đang chạy — cắt giữa shop để lại chương trình rác).
- `run-flash` khi lát cắt khác đang chạy ⇒ `Failed` (không tiên phong cắt KM/Combo giữa shop; "ưu tiên" = không ai được chạy ĐÈ lên
  flash, không phải flash được giết flow khác giữa chừng). Khi check đơn đang chạy ⇒ đi đúng đường tay hiện có (tạm dừng check đơn,
  xong tự chạy lại).
- `run` (check đơn) khi flash đang chạy ⇒ **KHÔNG Failed, KHÔNG đè**: bật cờ nhường (`_nhuongChoCheckDon`, đợt trước) và xếp lượt
  chạy lại qua đúng máy `ChayLaiCheckDonSauKhiChoAsync(id, email, TimeSpan.Zero, ct)` (nó đã biết chờ shop xong) ⇒ ack `Done` với
  `Error = "đang chờ gói Flash Sale xong shop hiện tại rồi mới chạy"` (UI hub hiện ghi chú). Khi lát cắt KM/Combo đang chạy ⇒ cùng
  cách (chờ xong). Không có gì chạy ⇒ `Sessions.Start` như cũ.
- Nhịp tự động: đảo thứ tự trong `ChayNhipLichKmAsync` thành **Flash → KM → Combo** (cùng tick mà cả hai tới hạn thì flash đi trước).
- `stop-all` ⇒ gọi đúng `StopAllAsync` (dừng check đơn + cancel lát cắt đang chạy + huỷ lượt hoãn + `_nguoiDungDungTay`), log
  "Hub ra lệnh: ✖ Dừng tất cả — …", ack `Done`. Nhịp tự động KHÔNG bị tắt (như ribbon) — nói rõ trong tooltip hub.

**Chạy từ hub = chạy với cấu hình ĐÃ LƯU của tài khoản** (`KmConfig`), y hệt nhịp tự động nhưng bỏ cửa "tới hạn"/"chủ trì"; giữ
cửa hợp lệ (`KiemTraForm`/`KiemTraFormCombo`/`KiemTraFormFlash`) ⇒ cấu hình rác ⇒ `Failed` có câu lỗi; `hanChot` = như đường tay
(flash 22:15; KM/Combo theo `HanChotLuotTuDong`). Không ghi mốc `LanThuTuDong` (lượt tay không đốt lượt auto — luật sẵn có).
Ack `Done` nghĩa là **ĐÃ NHẬN + BẮT ĐẦU** (như `run`), không phải đã xong.

**Server UI** (`DispatchOrdersTab.razor`): ô "Hành động" thêm 3 nút nhỏ "▶ KM · ▶ Combo · ▶ Flash" (cùng khoá `blocked` khi tài khoản đang
chạy ở máy khác — `OrdersHolder`); nút "✖ Dừng" khi có phiên đổi sang `stop-all` (tooltip: "dừng check đơn + lượt KM/Combo/Flash đang
chạy + huỷ lượt hoãn; KHÔNG tắt lịch tự động"). `Dispatch.razor.cs OnOrdersStop` cũng chuyển sang `stop-all`. Hub không biết
"đang chạy flash" (mirror chỉ có `SessionState` check đơn) — hiển thị ghi chú lỗi/ack `LastOrdersCmdError` như hiện tại; mở rộng
mirror là việc khác (ghi sổ nếu cần).

## 2. Phạm vi
**Làm**: DTO + nhãn + UI hub (3 nút, Dừng→stop-all) + test server · client hook `LenhTuHub` + dispatch + luật ưu tiên + đảo thứ tự
nhịp + test · 0 warning 2 solution.
**Không làm**: không đổi nghĩa `stop`/`run` với tự động hoá khung; không mở rộng mirror; không tiên phong cắt KM/Combo cho flash;
không đụng luồng extension; không ghi `SO-TAY.md`; không commit; không deploy (chặng 5).

## 3. Các bước
1. `HubDtos.cs`: 4 hằng hành động mới + xmldoc (vì sao không đổi `stop`).
2. `DispatchViewLogic.OrdersActionLabel` + `DispatchOrdersTab.razor` (nút, `OnOrders*Click`, tooltip) + `Dispatch.razor.cs OnOrdersStop`.
   Test `DispatchViewLogicTests`: nhãn 4 hành động; test UI logic thuần (hàm chọn action theo trạng thái) nếu có sẵn khuôn.
3. `AppServices.LenhTuHub` + đăng ký/huỷ trong `AccountsViewModel`; `OrdersModuleHost.Mirror.ExecuteOrdersCommand` dispatch.
4. VM: `XuLyLenhTuHub(action, accountId, email)` → `RunOnUi` + `Task.Wait`/`TaskCompletionSource` để trả kết quả đồng bộ cho luồng poll
   (đừng deadlock UI: kết quả là "đã nhận/bắt đầu", không await lượt chạy); các nhánh theo §1.3; câu log "Hub ra lệnh: …" mỗi nhánh.
5. `ChayNhipLichKmAsync`: Flash → KM → Combo; xmldoc lý do (ưu tiên flash — user 03/09).
6. Test (THỬ PHÁ từng bài): 
   - Mirror: `ExecuteOrdersCommand` với `run-km` khi hook null ⇒ Failed; với hook ⇒ gọi đúng (action, id); `stop` KHÔNG qua hook.
   - VM (khuôn `FlashHoanChayLaiTests`/`FlashGoiKheNghiTests`): `run-km` khi cờ flash bật ⇒ Failed có chữ "ưu tiên flash", không mở gì;
     `run` khi flash bật ⇒ Done + ghi chú "đang chờ", `NhuongChoCheckDon == true`, chưa `Sessions.Start`; hạ cờ ⇒ chạy đúng id;
     `stop-all` ⇒ `_nguoiDungDungTay` true + lượt hoãn bị huỷ + `TryKillPoc` được gọi; `run-flash` khi cấu hình rác ⇒ Failed có câu
     `KiemTraFormFlash`; thứ tự nhịp: neo nguồn `ChayNhipLichKmAsync` (Flash trước KM).
   - Server: `CreateOrdersCommand` idempotent với action mới; nhãn.

## 4. Tiêu chí nghiệm thu
- [ ] N1 build `ShopeeSuite.sln` + `server/ShopeeHub.sln`: 0 error 0 warning (worktree sạch nếu app khoá bin).
- [ ] N2 suite / orders (`--blame-hang 8m`) / `Shopee.Suite.Tests` / server tests: xanh, báo số.
- [ ] N3 `ExecuteOrdersCommand`: 5 hành động qua hook, `stop`+`sync-once`+`relogin` giữ nguyên đường cũ (grep + test).
- [ ] N4 Luật ưu tiên: ma trận (flash chạy × {run, run-km, run-combo, run-flash, stop-all}) đúng §1.3, có test.
- [ ] N5 Nhịp: Flash → KM → Combo (test neo nguồn).
- [ ] N6 UI hub: 3 nút + Dừng=`stop-all` + tooltip; nhãn test xanh.
- [ ] N7 Bảng thử phá; `git diff --stat` chỉ file của đợt.
- [ ] N8 (phiên chính) deploy hub + build app + chạy thật: từ Hub bấm ▶ Flash ⇒ client log "Hub ra lệnh: ▶ Flash…" và gói chạy; đang
  flash mà bấm ▶ Chạy ⇒ ack Done kèm "đang chờ", check đơn chỉ mở sau khi shop flash xong; ✖ Dừng tất cả ⇒ lượt tắt + không tự chạy lại.

## 5. Rủi ro & lưu ý
- `ExecuteOrdersCommand` chạy trên luồng poll; hook phải marshal về UI và KHÔNG chờ lượt chạy xong (deadlock/timeout heartbeat).
- `_cmdSeen` khử trùng lặp theo Id — lệnh mới có Id mới, không vấn đề; nhưng hub idempotent theo (máy, login, action) đang mở ⇒ bấm ▶ Flash
  hai lần chỉ ra một lệnh — đúng ý.
- Đừng đổi `stop` (KmKhung tự động dùng). Đừng cho `run-flash` cắt KM/Combo giữa shop.
- Thi công SAU đợt check-đơn-không-đè-flash (cùng `Phien.cs`, cùng `HubDtos.cs`): đọc lại chữ ký `ChayFlashSaleAsync`,
  `ThamSoFlashSale`, cờ `_nhuongChoCheckDon`, `ChayLaiCheckDonSauKhiChoAsync` ở trạng thái mới trước khi gõ.

---

## Báo cáo thực thi (điền sau)

---

## Báo cáo thực thi + PHẢN BIỆN (04/09 03:0x)

**Trạng thái: CODE XONG — nghiệm thu 7/7 ĐẠT — PHẢN BIỆN CHẶN (3 NẶNG + 3 VỪA có ca tái hiện) — CHƯA COMMIT, CHƯA DEPLOY. Dừng chờ user chốt hướng (đặc biệt NẶNG-1 chạm thiết kế) trước khi sửa tiếp.**

- Thi công (opus-dev 1 lượt): 11 file + 3 file test (52 bài). Build 0/0 hai sln; orders 2362, Core 457, Suite 87, Search 34, Hub 222.
- Nghiệm thu: N1–N7 = 7/7 ĐẠT (tự đo worktree sạch, thử phá M3/M8 đỏ đúng chỗ).
- Phản biện (đọc + probe): **6 phát hiện có ca vào→ra**, cộng lưới yếu (test không đo `hanChot`, không dựng 2 tài khoản, `Chay_KhiGoiFlashXong` đo đường `HetLatCat` không đi qua `TraHoSo`, `ThiHanhLenhTuHub` không có test).

### Phải sửa trước khi commit (theo phản biện)
1. **NẶNG · `stop-all` máy-rộng** (`Phien.cs LenhHubDungTatCa` → `StopAllAsync` → `Sessions.StopAllAsync` clear MỌI phiên; bỏ `accountId`). Bấm ✖ Dừng trên dòng `b@x` ⇒ `a@x`/`c@x` cùng máy cũng bị đóng + huỷ lượt hoãn + không tự chạy lại. **CẦN USER CHỐT**: dừng-từ-hub là PER-TÀI-KHOẢN (khớp `stop` cũ + khuôn `StopSelected` — dừng đúng phiên id đó + `TryKillPoc` nếu lát cắt thuộc nó + huỷ lượt hoãn của riêng nó) hay PER-MÁY (giữ, nhưng đổi nhãn/tooltip "Dừng tất cả trên máy X" + xác nhận 2 bước)? Tooltip `DispatchOrdersTab.razor:365-367` + `Dispatch.razor.cs:260` đang nói sai "của {login}".
2. **NẶNG · `run-km`/`run-combo` mượn hạn tự-động** (`Phien.cs:365 hanChot = HanChotLuotTuDong` = 04:00/08:00 của ngày). Đường tay truyền `hanChot: null` (`:164/:171`, xmldoc `:207`). Sửa: `run-km`/`run-combo` truyền `null` (đúng nút TAY); `run-flash` giữ 22:15 (khớp `ChayFlashSaleTayAsync`). Thêm test đo `hanChot`.
3. **NẶNG · `run` giữa gói flash bị `TraHoSo` nuốt** (`TraHoSo:1698` ghi đè `_dsTkDangHoanChayLai[A]` bằng lượt mốc cuối-khe MUỘN hơn lượt hub mốc-bây-giờ ⇒ lượt hub `RutLuotHoan` false ⇒ rút lui, không mở; check đơn trễ tới cuối khe). Sửa: `TraHoSo` KHÔNG ghi đè lượt hoãn đang tồn tại bằng mốc MUỘN HƠN (giữ mốc sớm nhất); HOẶC `LenhHubChayCheckDon` rút A khỏi `_dsTkDaDung`. Test phải đi qua `TraHoSo` thật (không `HetLatCat()` tay).
4. **VỪA · nhịp dùng chung `bayGio` cũ** (`KetQua.cs:240` await trọn gói flash rồi `NhipLichKmAsync(bayGio cũ)` ⇒ KM mở lúc 05:00 với hạn 04:00 ⇒ 0 shop, đốt `LanThuTuDong`). Sửa: mỗi nhịp đọc `DateTime.Now` riêng, hoặc return sau khi nhịp flash thật chạy một gói.
5. **VỪA · `run` do KmKhung tự phát cắt ngắn flash** (`HubDatabase.KmKhung.cs:109/149` phát `run` → nay qua hook → nhường ⇒ gói flash máy nhận dừng sớm). **CẦN USER**: trả-khung-xuyên-máy có được quyền cắt gói flash không (ngược "ưu tiên flash")? Cấu hình thật 1 máy/1 tài khoản (sổ P-20260901-4/-5) nên chưa cắn — có thể chỉ ghi tooltip/xmldoc + để đó.
6. **VỪA · luồng poll đứng chờ 20s/lệnh** (`Mirror.cs:194` gọi `ExecuteOrdersCommand` đồng bộ; lô 3 lệnh = 60s). Sửa: hạ `HanChoLenhTuHub` xuống ~5s hoặc cộng dồn trần cho lô.
- NHẸ: `HanChoLenhTuHub` static mutable dùng chung mọi VM; `PendingOrdersCmd` khoá cả nút ✖ Dừng khi có lệnh chưa ack.

### Đề xuất
- Đây là lượt sửa THỨ HAI khả dĩ cho đợt (opus-dev đã 1 lượt). Vì 3 NẶNG + 2 điểm cần-user-chốt (NẶNG-1, VỪA-5) + tính năng không cấp bách + 3h sáng ⇒ **để user chốt hướng NẶNG-1/VỪA-5 rồi mới giao lượt sửa** (hoặc phiên chính tự sửa 4 cái kỹ thuật rõ: 2,3,4,6). Code để nguyên trong cây, chưa commit.

### CHỐT của user 04/09 (~sáng): dừng-từ-hub là PER-TÀI-KHOẢN
> "khi click dừng từ hub thì dừng máy đang chạy tài khoản nào đó, 1 acc chỉ có thể chạy trên 1 máy tại 1 thời điểm, nên nếu dừng sẽ là dừng ACC chứ không dừng client, vì client có thể đang chạy acc khác"

⇒ **NẶNG-1 hướng sửa CHỐT**: `stop-all` = dừng MỌI hoạt động của ĐÚNG `accountId` đó (phiên check đơn của nó + cắt lát cắt cầu nối NẾU acc đó đang giữ + huỷ lượt hoãn của riêng nó), KHÔNG đụng acc khác trên cùng máy. Nhãn nút hub đổi "✖ Dừng tất cả" → **"✖ Dừng"**; tooltip: "Dừng mọi hoạt động của {login}: check đơn + lượt KM/Combo/Flash đang chạy của tài khoản này + huỷ lượt hoãn. Máy vẫn chạy tài khoản khác." Giữ chuỗi action `stop-all` (nghĩa: "dừng tất cả CỦA acc này"), phân biệt với `stop` cũ (chỉ dừng phiên, dùng cho KmKhung nhường khung).

VỪA-5 (trả-khung `run` cắt flash): user CHƯA chốt; cấu hình 1 máy/1 acc không cắn ⇒ GIỮ hành vi, chỉ thêm xmldoc nêu tương tác; không chặn commit.

### Lượt 2 (opus-dev, 04/09 sáng) — 6 điểm — nghiệm thu 12/13 ĐẠT, phản biện chặn lại 2 NẶNG

- opus-dev sửa NẶNG-1/2/3 + VỪA-4/5/6 (báo cáo trong transcript): `TaiKhoanLatCatDangChay`, `DungMotTaiKhoanAsync`,
  `HanChotLenhVongTuHub`, guard `TraHoSo` `moc > mocCu` (chọn `>` nghiêm thay `>=` của spec vì `>=` phá bất biến
  "gói thứ hai cùng mốc phải THAY lượt trước" — phiên chính chấp nhận), `DongHoNhipLich`, `HanChoLenhTuHub` 5s.
  Build 0/0; orders 2370, Core 457, Suite 87, Search 34, Hub 222 (worktree).
- **Nghiệm thu** (worktree `wt-nt`, tự đo lại đúng các số trên; 6 đột biến tự chạy đều ĐỎ): N1–N7 ĐẠT, N8 CHƯA ĐO;
  NẶNG-1 **ĐẠT MỘT PHẦN**: `_nguoiDungDungTay` là cờ TOÀN MÁY ⇒ Dừng A giữa lát cắt của A làm `TraHoSo` bỏ luôn
  việc mở lại check đơn của B.
- **Phản biện** (đọc + 3 đột biến trong `wt-pb`):
  - **NẶNG-A** = phát hiện trên + chiều ngược: Dừng B giữa lát cắt của A ⇒ cờ không bật ⇒ B tự mở lại sau khi user
    đã bảo dừng. Chứng minh: đổi `:2406` về `if (_bridgeRunning)` ⇒ 47/47 test vẫn xanh (bàn thử không có lượt mượn
    nào đang bay).
  - **NẶNG-B**: hub KHÔNG hiện được "✖ Dừng" khi đang chạy lát cắt — gương chỉ có `SessionState` check đơn (rỗng
    trong lát cắt) ⇒ nút là "▶ Chạy", bấm vào là ép gói flash nhường. = sổ P-20260904-12, nay thành chặn mục đích
    đợt ⇒ tách **đợt nhỏ riêng** `plans/2026-09-04-guong-mang-lat-cat-dang-chay.md`, làm ngay sau commit đợt này.
  - VỪA-C: bước (4) `stop-all` A huỷ `_bridgeCts` của lát cắt B (cửa sổ xem của A còn để lại, lát cắt B đang chờ khung).
  - VỪA-D: hết hạn 5s ack `failed` nhưng thân lệnh đã xếp hàng vẫn chạy khi UI rảnh ⇒ ack nói dối.
  - VỪA-E: test VỪA-4 đo rỗng (đếm lượt đọc đồng hồ; truyền lại `bayGio` cũ vẫn xanh — đã chứng minh).
  - NHẸ-F: `Assert.Empty(ban.DaChayLai)` trong bài stop-all là mệnh đề rỗng.

### Lượt 3 (PHIÊN CHÍNH TỰ SỬA, 04/09) — không giao lượt thứ ba cho subagent (luật 2 lượt + luật 3 lần)

**⚠ Tự làm, chưa ai kiểm độc lập** — người đọc cần biết số dưới đây do người làm tự đo (worktree `wt-main`).

| # | Sửa | Ở đâu |
|---|---|---|
| NẶNG-A | `_nguoiDungDungTay` (bool toàn máy) → **`HashSet<long> _dsTkNguoiDungDungTay`**; `DungMotTaiKhoanAsync` LUÔN `Add(accountId)` (bất kể lát cắt của ai); `TraHoSo` lọc theo từng id, log rõ tài khoản bị bỏ qua; `StopSelected` `UnionWith(ids)`; `StopAll` `UnionWith(_dsTkDaDung)`; ba đường ▶ Chạy (tay, đã chọn, hub) `Remove(id)` = người dùng đổi ý | `Phien.cs` khai báo `_dsTkNguoiDungDungTay`, `TraHoSo`, `DungMotTaiKhoanAsync`, `StopSelected`, `StopAllAsync`, `Run`, `RunSelected`, `LenhHubChayCheckDon` |
| VỪA-C | bước (4) chỉ đóng cửa sổ xem khi **không** có lát cắt của tài khoản khác đang chạy; ngược lại log "sẽ được đóng sau" | `DungMotTaiKhoanAsync` |
| VỪA-D | giao thức giành quyền `Interlocked` (0 chờ · 1 thân lệnh đã nhận · 2 hết hạn): hết hạn ⇒ thân lệnh xếp hàng **bỏ qua**; thân lệnh nhận sát hạn ⇒ chờ nốt, ack kết quả thật. `HanChoLenhTuHub` thành thuộc tính instance; seam `UiCoQuyen`/`UiXepHang` theo instance (không đụng `UiDispatch` tĩnh) | `ThiHanhLenhTuHub` (nay `internal`) |
| VỪA-E | test đo **giá trị** hai nhịp nhận qua seam `MocNhipDaNhanChoTest("KM"/"Combo", mốc)` thay vì đếm lượt đọc | `KetQua.cs` + test |
| NHẸ-F | bỏ `Assert.Empty(ban.DaChayLai)` rỗng, ghi lý do | test |
| NHỎ (nghiệm thu) | câu log "Bấm ✖ Dừng tất cả là huỷ hẳn lượt xếp này" → "Bấm ✖ Dừng…" khớp nhãn nút | `LenhHubChayCheckDon` |
| Ngoài phạm vi (phản biện) | `StopSelected` cờ toàn máy — **đóng luôn** vì cùng biến đổi kiểu: tick B không còn chặn lượt chạy lại của A | `StopSelected` |

**Test mới** (`LenhTuHubUuTienFlashTests.cs`, mục 5): `DungA_GiuaLatCatCuaA_ThiBVanDuocChayLaiKhiTraHoSo`,
`DungB_GiuaLatCatCuaA_ThiBKhongTuMoLaiKhiTraHoSo`, `DungBRoiChayB_TrongCungLuotMuon_ThiBDuocChayLai`,
`DungA_CuaSoXemCuaA_NhungLatCatCuaB_ThiKhongHuyTokenCuaB` (+ đối chứng), `LenhHub_HetHanCho_ThiThanLenhKhongChayNua`
(+ đối chứng). Seam mới: `DatTenLatCatChoTest(..., giuCauNoi)`, `DatCuaSoXemChoTest`, `UiCoQuyen`, `UiXepHang`.

**Sự cố khi tự sửa (ghi để lần sau khỏi dẫm):** script vá Python ghi file với LF trong khi cây làm việc là CRLF
(`autocrlf`) ⇒ bài neo nguồn `CoNhuong_DuocRESET…` (mốc có `\r\n`) đỏ giả; đã ép CRLF lại. Hai bài đỏ thật khác do
chính tôi: thêm `UpdateSelectedSessionStatus()` ở nhánh trả-rỗng xoá mất `BusyStatus` "MAY-KHO-1"; câu log mới chứa
"chạy lại check đơn" trúng `DoesNotContain` của bài Dừng-tất-cả — cả hai đã sửa.

**Số đo lượt 3 (worktree `wt-main`, tự đo):** build ShopeeSuite.sln 0 warning 0 error; lớp LenhTuHub + FlashHoanChayLai
+ MoListShop + FlashGoiKheNghi + KmTraHoSoKhiNem = 116/116. Thử phá + 4 bộ đầy đủ: xem dòng "KẾT QUẢ CUỐI" bên dưới.

**KẾT QUẢ CUỐI lượt 3 (worktree `wt-main`, tự đo, 04/09 ~10:30):** thử phá 5/5 đột biến ĐỎ đúng bài mong
(M-A bỏ `Add(accountId)` ⇒ 2 bài; M-A2 `TraHoSo` không lọc ⇒ 2 bài; M-C ⇒ 1; M-D ⇒ 1; M-E ⇒ 1), khôi phục sạch;
build 0 warning 0 error; **orders 2377/2377** (2370 + 7 mới), **Core 457**, **Suite 87**, **Search 34**; Hub 222 không
chạy lại (hub không đổi từ lượt nghiệm thu). **N8 (chạy thật) CHƯA ĐO** — làm chung với đợt nhỏ NẶNG-B sau khi nó hạ
cánh (một lần deploy hub + một lần build app ở khe an toàn).
