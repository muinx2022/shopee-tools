# Check đơn KHÔNG đè gói flash (P-20260903-1) + %giảm / SL tham gia nhập từ app — 2026-09-03

- **Ngày:** 2026-09-03 (tối) · **Trạng thái:** đang làm
- **Người lập:** phiên chính · **Thực thi:** `opus-dev` · **Nghiệm thu:** `nghiem-thu` · **Phản biện:** `phan-bien`

User chốt 22:5x–23:1x 03/09:
1. *"sửa luôn P-20260903-1 đi, khi đang chạy flash sale của shop nào đó, thì check đơn chờ đến khi flash sale của
   shop đó xong thì mới chạy, không đè lên flash sale"*.
2. *"số 5% và 10 sp cho phép nhập từ client, không fix trong code, khi nhập ở app thì lưu lại … lần sau là 10%,
   20 sp thì không phải sửa code"* — hỏi lại, user chốt **"10 sp" = ô "Số lượng sản phẩm tham gia"** (ô thứ hai của
   khối *Chỉnh sửa hàng loạt*, gõ cùng % giảm), KHÔNG phải số sản phẩm mỗi khung (vẫn 1 trang 10 SP bán chạy).

Gộp xử lý sổ: **P-20260903-1** (NẶNG), **P-20260903-7** (câu tổng kết nói sai lý do dừng — cùng đường code).

## 1. Bối cảnh — đủ để người thực thi không cần đọc chat

### 1.1 Cơ chế lỗi P-20260903-1 (đo thật 03/09, bản a77b3d0)

Gói flash chạy trong khe nghỉ check đơn. Khi gói kết thúc, `TraHoSo` (`orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs:1455`)
đọc `MocChayLaiCheckDonSauFlash` (= cuối khe, do nhịp đặt ở `:898`), ghi "HOÃN chạy lại check đơn tới HH:MM",
`_dsTkDangHoanChayLai.Add(id)` và phóng `ChayLaiCheckDonSauKhiChoAsync(id, email, moc − bayGio, ct)` (`:1595`), rồi
đặt `MocChayLaiCheckDonSauFlash = null` (`:1464`). Lượt hoãn chỉ có HAI cửa (token huỷ + còn trong `_dsTkDangHoanChayLai`)
rồi gọi `ChayLaiPhienCheckDon(accountId)` (`:1613`) — **không hỏi gói flash có đang chạy không**.

Vì sao có gói đang chạy đúng lúc đó: nhịp `NhipLichFlashAsync` (`:805`) khi **không có phiên nào** (đúng trạng thái
sau khi gói vừa xong + đang hoãn) lấy `cuoiKhe = bayGio + interval(120')` làm khe giả định (`:866`) — **bỏ qua mốc hoãn
đang chờ**. Bằng chứng: 18:09 gói 1 xong → hoãn tới 18:52 → 18:10:09 MỞ GÓI 2 (khe giả định 20:10) → 18:50:06 xong →
**18:50:09 MỞ GÓI 3** (mốc hoãn còn 2'50") → 18:52:59 "Hết biên an — chạy lại check đơn" → `HttpListenerException` cổng
47821 → "Dọn hồ sơ… đóng 10 cửa sổ" (giết Brave của gói) → Delica HỎNG → gói dừng. Lặp y hệt 21:25:09 / 22:01:44 (Sully).
Cả vòng check đơn 22:01 cũng hỏng theo ("Vòng cầu nối chưa trọn"), phải chờ 22:17.

Hạn bắt đầu shop mới của gói = `KmLichTuDong.HanChotGoiFlash(bayGio, cuoiKhe)` = min(cuối khe − 45', 22:15) (`:893`);
vòng shop dùng chung `ChayVongTaoChuongTrinhAsync` (`orders/XuLyDonShopee.Core/Services/OrdersBridgeSession.cs:1119`)
kiểm `KmLichTuDong.ConDuocBatDauShopMoi(nay, hanChot)` TRƯỚC mỗi shop (`:1211`) và log "Hết khung tự động ({hanChot:HH:mm})
— DỪNG, còn N shop chưa chạy" (`:1214`). Một shop bị chặn bởi `ChoChang.FlashChay` = 2700s.

### 1.2 Thiết kế chốt cho hạng mục 1 — hai chốt, cần CẢ HAI

**(A) Nhịp không mở gói đè lên mốc hoãn.** Khi có lượt hoãn đang chờ tới mốc M (tài khoản trong
`_dsTkDangHoanChayLai`), khe giả định khi không có phiên = **min(bayGio + interval, M)**. `KheDuChoGoiFlash` (≥75')
tự từ chối khi M gần; câu từ chối (đã tiết chế 60') phải nói rõ "mốc chạy lại check đơn đã hẹn HH:MM". Khi mở được,
`MocChayLaiCheckDonSauFlash = cuoiKhe` (= M) — không đẩy mốc muộn hơn; lượt hoãn thứ hai cho cùng tài khoản là no-op
nhờ `Remove` trả false (giữ nguyên hành vi đó). Cần một trường mới `MocHoanChayLaiDangCho` (`DateTime?`, internal,
set ở `TraHoSo` khi phóng lượt hoãn, xoá khi lượt hoãn chạy/huỷ) vì `MocChayLaiCheckDonSauFlash` đã bị xoá ngay khi
lên lịch.

**(B) Lượt hoãn CHỜ shop flash hiện tại xong, không đè.** Trong `ChayLaiCheckDonSauKhiChoAsync`, SAU hai cửa hiện có
và TRƯỚC `ChayLaiPhienCheckDon`: nếu `_dangChayFlashTuDong || _bridgeRunning` ⇒
  1. bật cờ `_nhuongChoCheckDon = true` (volatile/Interlocked) — gói đang chạy sẽ **không bắt đầu shop mới** (xem C);
  2. log MỘT dòng: "Gói Flash Sale đang chạy — chờ shop hiện tại xong rồi mới chạy lại check đơn (không đè lên flash)";
  3. chờ-lặp qua hook tiêm được `ChoNhipNhuongFlash` (`Func<CancellationToken, Task>`, mặc định `Task.Delay(10s, ct)`)
     tới khi cả hai cờ tắt; trần `TranChoNhuongFlash = 2 × ChoChang.FlashChay` (90') — quá trần: log to "quá hạn chờ gói
     flash — KHÔNG mở check đơn để không đè; bấm ▶ Chạy khi cần" và **return, không mở**;
  4. token huỷ trong lúc chờ ⇒ đi đúng nhánh "ĐÃ BỊ HUỶ" hiện có.
  Áp cho cả lượt tay (`_bridgeRunning`) — user ngồi máy nhưng luật "không đè" không có ngoại lệ.

**(C) Gói nhường chỗ ở ranh giới shop.** Thêm tham số tuỳ chọn **cuối cùng** `Func<bool>? phaiNhuongSauShop = null`
cho `ChayFlashSaleMoiShopAsync` → `ChayFlashSaleSauPickerAsync` → `ChayVongTaoChuongTrinhAsync`; tại chỗ kiểm `:1211`
thêm: `phaiNhuongSauShop?.Invoke() == true` ⇒ log `[{nhan}] Nhường chỗ cho check đơn — DỪNG sau shop vừa xong, còn
{conLai} shop cho gói sau` và thoát **cùng hình dạng kết quả với nhánh hết hạn** (các shop còn lại = chưa chạy
`SoShopChuaChay`, KHÔNG tính xong ngày). KM/Combo không truyền (null). VM truyền `() => _nhuongChoCheckDon` ở cả đường
nhịp (`:906`) lẫn đường tay (`ChayFlashSaleTayAsync`), và **reset cờ về false** ngay trước mỗi lượt/gói.

**(D) P-20260903-7:** câu tổng kết C# hiện in "DỪNG vì hết khung tự động (23:00)" (lấy `GioDongKhungFlashSale`) trong
khi lý do thật là hạn gói 18:07. Sửa: câu tổng kết nói đúng **hạn của gói** (`hanChot`) và, ở ca (C), nói "nhường chỗ
cho check đơn". Tìm câu này bằng `grep -rn "hết khung tự động" orders/` (C# + Core).

### 1.3 Hạng mục 2 — % giảm và SL tham gia nhập từ app (đi TRỌN 4 lớp, vì `km_config` được đồng bộ Hub)

Hiện: `extensions/shopee-orders/flow-flashsale.js:52` `PHAN_TRAM_GIAM_FLASH = 5`, `:57` `SO_LUONG_KM_FLASH = 10`;
dùng ở bước F2 (`:4231–4250`) và K0 tái chế (`:3574–3575`). C#: `ThamSoFlashSale(DateTime NgayDich)`
(`OrdersBridgeSession.cs:~342`), `Goi()` gửi `ngay/thang/nam`; extension `docThamSoFlash` (`:~2818`) đọc gói.

`km_config` (`orders/XuLyDonShopee.Core/Data/KmConfigRepository.cs`, `Database.cs:248/295`) đồng bộ lên Hub theo CỘT:
DTO `KmProfileItem` (`suite/Shopee.Core/Coordination/HubDtos.cs:463 FlashBat`), luật gộp `KmHoSoGop` (`:120` nhóm "máy
sửa sau thắng" đi cùng `CfgUpdatedAt`; đẳng thức `:163`), ánh xạ `OrdersModuleHost.KmSync.cs` (`ApNhomCauHinh` :528 hub→local
bằng `cu with {…}`; `ThanhHoSo` :862 local→hub), server `server/Shopee.Hub.Web/Data/HubDatabase.KmProfiles.cs` (cột
`:41`, `AddColumnIfMissing` `:60`, upsert `:116–124/:144`, select `:187/:210`). **Nếu chỉ thêm ở client**, `ApNhomCauHinh`
sẽ ghi đè 10%/20 về mặc định ở lượt đồng bộ kế ⇒ mất cấu hình — vì thế phải đi trọn, và DTO phải **nullable** để server
CŨ (chưa có cột, không trả trường) không xoá giá trị local: `hoSo.X ?? cu.X`.

### 1.4 Môi trường
- `dotnet`: `"C:\Users\Ng Xuan Mui\AppData\Local\Microsoft\dotnet\dotnet.exe"` (8.0.423). Hub KHÔNG trong `ShopeeSuite.sln`
  — build/test riêng: `server/ShopeeHub.sln` (`Shopee.Hub.Web`, `Shopee.Hub.Web.Tests`).
- **App đang CHẠY check đơn** (PID 23252, bản a77b3d0) và giữ khoá DLL trong `suite\Shopee.Suite\bin\` + cổng 47821 ⇒
  build cây chính ra MSB3021/MSB3027 GIẢ; đo sạch bằng `git worktree add --detach` + chép file của đợt. **KHÔNG tắt app,
  KHÔNG chạy lượt thật, KHÔNG deploy** ở chặng 2–4 (phiên chính làm ở chặng 5).
- Bộ test orders có bài phóng Brave / có thể treo (sổ P-20260901-2/-3) ⇒ `--blame-hang --blame-hang-timeout 8m`.

## 2. Phạm vi

**Làm**
1. Hạng mục 1 (A)(B)(C)(D) như §1.2, kèm test + thử phá.
2. Hạng mục 2: 2 trường cấu hình flash (`FlashPhanTramGiam`, `FlashSoLuongThamGia`) — client DB + form + `ThamSoFlashSale`
   + extension + DTO/gộp/ánh xạ + server cột + test, kèm thử phá. Mặc định 5 / 10 (seed DB DEFAULT + record default).
3. 0 warning ở CẢ `ShopeeSuite.sln` và `server/ShopeeHub.sln`.

**Không làm**
- Không đổi `FlashChay`, khe nghỉ, `PhutChotShopMoiTruocCuoiKhe`, nhịp gói.
- Không đụng số sản phẩm mỗi khung (vẫn 1 trang 10 SP) / luật quay vòng trang.
- Không sửa P-20260903-2 (câu "0 sản phẩm"), P-20260903-5, -6. Không đụng KM/Combo ngoài chỗ chữ ký dùng chung.
- KHÔNG ghi `SO-TAY.md`. KHÔNG commit. KHÔNG deploy hub.

## 3. Các bước

### 3.1 (A) Nhịp — `AccountsViewModel.Phien.cs`
- Thêm `internal DateTime? MocHoanChayLaiDangCho { get; private set; }`: set = `moc` trong `TraHoSo` ngay khi phóng lượt
  hoãn (`:1495–1496`); xoá (null) ở đầu nhánh chạy của `ChayLaiCheckDonSauKhiChoAsync` sau khi `Remove` thành công, ở nhánh
  "ĐÃ BỊ HUỶ", và trong `HuyLuotHoanChayLaiCheckDon`.
- `:866`: `cuoiKhe = dangChay.Count == 0 ? Min(bayGio + interval, MocHoanChayLaiDangCho ?? DateTime.MaxValue) : …`.
  Câu từ chối khi khe ngắn: nếu bị chặn bởi mốc hoãn thì nói "… mốc chạy lại check đơn đã hẹn {M:HH:mm} …".
- Test hook: `internal void DatMocHoanDangChoChoTest(DateTime? m)`.

### 3.2 (B) Lượt hoãn chờ shop xong — `AccountsViewModel.Phien.cs:1595`
- Field `private volatile bool _nhuongChoCheckDon;` + `internal bool NhuongChoCheckDon => _nhuongChoCheckDon;`.
- Hook `internal Func<CancellationToken, Task> ChoNhipNhuongFlash { get; set; } = ct => Task.Delay(TimeSpan.FromSeconds(10), ct);`
  và `internal Func<bool> DangChayFlashHoacCauNoi { get; set; }` mặc định `() => _dangChayFlashTuDong || _bridgeRunning`
  (test đổi được).
- Hằng `internal static readonly TimeSpan TranChoNhuongFlash = OrdersBridgeChannel.ChoChang.FlashChay * 2;` + xmldoc.
- Thứ tự trong hàm: chờ `doi` → cửa token/`Remove` (giữ nguyên) → **mới**: nếu `DangChayFlashHoacCauNoi()` ⇒ bật cờ,
  log 1 dòng, vòng chờ tới khi tắt hoặc quá trần (quá trần ⇒ log + return, KHÔNG mở) → `ChayLaiPhienCheckDon`.

### 3.3 (C) Nhường ở ranh giới shop — `OrdersBridgeSession.cs`
- Thêm `Func<bool>? phaiNhuongSauShop = null` (tham số cuối) ở `ChayFlashSaleMoiShopAsync`, `ChayFlashSaleSauPickerAsync`,
  `ChayVongTaoChuongTrinhAsync`. Tại `:1211`: kiểm thêm cờ; log câu nhường; kết quả cùng hình với nhánh hết hạn.
- VM: `ChayFlashSaleAsync` nhận thêm `Func<bool>? phaiNhuongSauShop` và truyền xuống; nhịp + đường tay truyền
  `() => _nhuongChoCheckDon`; reset `_nhuongChoCheckDon = false` ngay trước khi gọi.

### 3.4 (D) Câu tổng kết — lý do dừng đúng
- `grep -rn "hết khung tự động" orders/` → câu C# in `(23:00)` đổi sang in hạn của gói; thêm nhánh "nhường chỗ cho check
  đơn". Cờ/lý do phải đi qua `KetQuaKm` hiện có (không đẻ khuôn mới) — đọc `CauTongKetKm` và chuỗi else-if theo lý-do-dừng
  (sổ P-20260901-6 cảnh báo: cờ mới chèn TRƯỚC `Loi` sẽ nuốt lỗi thật — đặt SAU `Loi`).

### 3.5 Hạng mục 2 — client
- `KmConfig` (record body, `init`, cạnh `FlashLanChayCuoi`): `int FlashPhanTramGiam = MacDinhFlashPhanTramGiam (5)`,
  `int FlashSoLuongThamGia = MacDinhFlashSoLuongThamGia (10)`; đưa cả hai vào `CungCauHinh`.
- `Database.cs`: CREATE TABLE + `EnsureColumn(conn, "km_config", "flash_phan_tram_giam", "INTEGER NOT NULL DEFAULT 5")`,
  `flash_so_luong_tham_gia … DEFAULT 10`. `KmConfigRepository`: Read (cột kế tiếp sau `cot0 + 20`, `IsDBNull` ⇒ mặc định),
  Save (INSERT + UPDATE + params).
- `KmDialogViewModel`: `[ObservableProperty] int _flashPhanTramGiam`, `_flashSoLuongThamGia`; nạp từ `cauHinh` (`:117`),
  đưa vào record `TuForm` (`:675`), hai chỗ ánh xạ (`:726`, `:753`); `public static string? KiemTraFormFlash(int phanTram,
  int soLuong)`: % ∈ [1, 99] (lời gợi ý "Shopee hiện đòi tối thiểu 5%"), SL ∈ [1, 9999]; gọi trong luồng Lưu, câu lỗi lên `Loi`.
- `KmDialog.xaml` tab `TabFlashSale`, dưới CheckBox: hai ô `TextBox` "Giảm (%)" và "Số lượng SP tham gia (mỗi SP)" bind
  `UpdateSourceTrigger=PropertyChanged`, ToolTip nói rõ là ô của khối "Chỉnh sửa hàng loạt" trên form flash Shopee.
- `ThamSoFlashSale(DateTime NgayDich, int PhanTramGiam, int SoLuongThamGia)` — **không default** (nguồn là cấu hình, không
  phải code); `MoTa()` thêm " · giảm {P}% · SL tham gia {S}/SP"; `Goi()` thêm `phanTramGiam`, `soLuongThamGia`. Hai chỗ
  gọi (`Phien.cs:890`, `:2326`) đọc từ `cf`.
- Extension: `docThamSoFlash` đọc + kiểm (1–99, 1–9999; thiếu/rác ⇒ `loi` có tên — fail-closed như ngày/tháng); F2 và K0 tái
  chế dùng `tp.phanTramGiam`/`tp.soLuongThamGia`; **xoá hai hằng**; tên bước `${K}.6b Áp ${tp.phanTramGiam}%GIẢM + SL
  ${tp.soLuongThamGia} hàng loạt`. `thanBuocTaiChe` (K0) phải nhận `tp` (closure/tham số) — đừng để nó đọc hằng chết.

### 3.6 Hạng mục 2 — đồng bộ Hub
- `HubDtos.KmProfileItem`: `int? FlashPhanTramGiam { get; init; }`, `int? FlashSoLuongThamGia { get; init; }` (nullable —
  server cũ không trả ⇒ null ⇒ giữ local).
- `KmHoSoGop`: gộp theo nhóm "máy sửa sau thắng" (`nen.X`), đẳng thức `CungCauHinh` so cả hai (null == null).
- `OrdersModuleHost.KmSync`: `ApNhomCauHinh`: `FlashPhanTramGiam = hoSo.FlashPhanTramGiam ?? cu.FlashPhanTramGiam` (tương tự
  SL); `ThanhHoSo`: gán từ `cf`; dòng mô tả log (`:685–695`) thêm "flash 5%/10".
- Server `HubDatabase.KmProfiles.cs`: CREATE TABLE + `AddColumnIfMissing("km_profiles", "flash_phan_tram_giam", "INTEGER DEFAULT 5")`,
  `flash_so_luong_tham_gia INTEGER DEFAULT 10`; upsert/select/đọc DTO. Server tests `KmHoSoHubTests`/`KmRouteHttpTests`:
  thêm ca round-trip 2 trường + ca client cũ không gửi (giữ DEFAULT).

### 3.7 Test (mỗi bài mới có THỬ PHÁ: đột biến → đỏ → khôi phục → `git diff --stat` đối chiếu)
- **FlashGoiKheNghiTests**: `CoLuotHoanDangCho_ThiKheGiaDinhChanOMocHoan`: không phiên, chu kỳ 120', mốc hoãn +40' ⇒ KHÔNG
  mở gói, câu từ chối có "đã hẹn"; mốc hoãn +100' ⇒ mở, `MocChayLaiCheckDonSauFlash == mốc`, hạn gói = mốc − 45'.
  Hai bài `KhongPhienNaoChay_*` hiện có phải xanh nguyên.
- **FlashHoanChayLaiTests** (harness `Dung`/`VaoNhanhHoan`): `DangChayFlash_ThiChoShopXong_RoiMoiChayLai` (cờ flash bật ⇒ chưa
  `ChayLaiPhienCheckDon`, `NhuongChoCheckDon == true`; tắt cờ + nhịp ⇒ chạy đúng id); `HuyTrongLucChoNhuong_ThiKhongMo`;
  `QuaTranChoNhuong_ThiKhongMo_VaLogKhongDe`.
- **Core/rig** (`FlashSaleLuongThatTests` mẫu `BoQuaShop_*`): `PhaiNhuongSauShop_ThiDungSauShopDangChay_ConLaiChuaChay`
  (`SoShopChuaChay == n−1`, không ghi mốc ngày). Rig phải có hạn chờ (P-20260901-2) — không để treo.
- **Câu tổng kết**: bài neo chuỗi không còn `(23:00)`/`GioDongKhungFlashSale` trong câu "hết khung tự động"; có nhánh
  "nhường chỗ cho check đơn" nằm SAU nhánh `Loi`.
- **KmConfigRepository**: round-trip 2 trường; DB cũ (thiếu cột) ⇒ đọc ra 5/10; `CungCauHinh` đổi % ⇒ false.
- **KmDialogViewModel**: `KiemTraFormFlash` ma trận (0, 1, 5, 99, 100 / 0, 1, 9999, 10000); form đổi % ⇒ có lưu.
- **Suite Jint**: `docThamSoFlash` thiếu/rác/biên ⇒ `loi`; hợp lệ ⇒ đúng số; nguồn F2/K0 không còn `PHAN_TRAM_GIAM_FLASH`
  / `SO_LUONG_KM_FLASH`; `MayLuatThuan` cập nhật.
- **orders payload** (`FlashSaleLuongThatTests:~84`): `phanTramGiam`/`soLuongThamGia` có mặt và đúng cấu hình; vẫn KHÔNG
  có `phanTram`/`bd`.
- **KmHoSoGop / KmSync**: gộp lấy theo máy sửa sau; `ApNhomCauHinh` với DTO null ⇒ giữ local; `CungCauHinh` DTO so 2 trường.
- **Server**: round-trip + client cũ.

## 4. Tiêu chí nghiệm thu (đo được)
- [ ] **N1** `dotnet build ShopeeSuite.sln` (worktree sạch): 0 error, 0 warning. **N1b** `dotnet build server/ShopeeHub.sln`: 0/0.
- [ ] **N2** `dotnet test suite/Shopee.Core.Tests` xanh; số ≥ 440 + bài mới (báo số).
- [ ] **N3** `dotnet test orders/XuLyDonShopee.Tests --blame-hang --blame-hang-timeout 8m` xanh; ≥ 2303 + mới; không treo.
- [ ] **N3b** `dotnet test server/Shopee.Hub.Web.Tests` xanh; báo số.
- [ ] **N4** (A): test khe-giả-định-chặn-ở-mốc-hoãn xanh; `MocHoanChayLaiDangCho` set/xoá đúng 3 chỗ.
- [ ] **N5** (B): 3 bài lượt hoãn xanh; thứ tự cửa trong `ChayLaiCheckDonSauKhiChoAsync` đúng §3.2; trần = 2×FlashChay.
- [ ] **N6** (C): tham số `phaiNhuongSauShop` là tham số CUỐI, tuỳ chọn, KM/Combo không đổi chữ ký gọi; test rig xanh.
- [ ] **N7** (D): `grep -rn "hết khung tự động" orders/` không còn in `GioDongKhungFlashSale`; câu nhường có mặt SAU `Loi`.
- [ ] **N8** Hạng mục 2 xuyên suốt: form → DB → `ThamSoFlashSale.Goi()` → `docThamSoFlash` → F2/K0; hai hằng đã xoá khỏi
  extension; DTO nullable; `ApNhomCauHinh` giữ local khi null; server có 2 cột + test round-trip.
- [ ] **N9** Bảng thử phá: mỗi bài mới ≥ 1 đột biến đỏ, đã khôi phục, `git diff --stat` chỉ còn file của đợt.
- [ ] **N10** (phiên chính, chặng 5) Deploy hub (publish linux-x64 → scp → install → restart → `/health`), build app vào bin
  thật lúc app rảnh, mở lại; chạy thật: đặt thử 6%/12 trên form, gói kế phải log `K*.6b Áp 6%GIẢM + SL 12`; và một lượt
  hoãn rơi trúng gói đang chạy phải log "chờ shop hiện tại xong" thay vì `HttpListenerException`.

## 5. Rủi ro & lưu ý
- Thứ tự deploy: client trước cũng an toàn (server cũ bỏ qua trường lạ khi ghi; đọc về null ⇒ giữ local). Server trước cũng
  an toàn (client cũ không gửi ⇒ DEFAULT 5/10).
- Cờ `_nhuongChoCheckDon` đọc từ thread khác (vòng shop) ⇒ `volatile`. Reset ĐÚNG một chỗ trước mỗi lượt — quên reset là gói
  sau dừng ngay sau shop 1.
- Ca (B) trần 90' rồi KHÔNG mở check đơn là chủ ý (thà thiếu check đơn một khe còn hơn đè) — log phải nói rõ để user biết bấm ▶.
- `KetQuaKm` chuỗi else-if theo lý do dừng: cờ mới đặt SAU `Loi` (P-20260901-6).
- Đừng "tiện tay" sửa P-20260903-2/-5/-6. Báo cáo có mục **NGOÀI PHẠM VI — đề nghị ghi sổ**.

---

## Báo cáo thực thi (điền sau)

**Trạng thái: CODE XONG sau 3 lượt sửa — kiểm chứng độc lập ĐẠT — Hub đã deploy 01:51 04/09 — app build vào bin thật + mở lại ~02:00 04/09; N10 (đo thật) đang chạy.**

### Diễn tiến
- **Lượt 1 (`opus-dev`)**: §3.1–§3.7 + hạng mục 2 trọn 4 lớp (22 file). Nghiệm thu 9/9 ĐẠT (457/2332/70/206, 0 warning).
  Phản biện: **1 NẶNG** (đua nhịp 60s ↔ vòng nhường 10s: mốc xoá trước vòng nhường + cờ chỉ bật một lần ⇒ gói G2 mở
  đè, cờ bị reset, check đơn bị bỏ sau 90') + 2 TRUNG BÌNH (log sai tên lượt / chỉ flash đọc cờ; client cũ thắng gộp
  ⇒ hub ghi DEFAULT đè cấu hình thật) + 3 NHẸ.
- **Lượt 2 (`opus-dev`)**: F1–F6 (mốc giữ tới lối ra; cờ đặt lại mỗi nhịp; `TenLatCatDangChay` + cờ nhường xuống
  KM/Combo; `nen.X ?? khac.X` + server `COALESCE`; hạ cờ khi huỷ; mốc theo tài khoản có lock; chú thích/neo test).
  Nghiệm thu: F1–F6 đúng chỗ, 457/2339/70/207. Phản biện lượt 2: **1 NẶNG** ("Dừng đã chọn" giữa lúc nhường không
  huỷ được vì id đã `Remove` khỏi tập trước vòng nhường ⇒ app tự mở Brave sau khi user bảo dừng) + 1 VỪA (lượt hoãn
  thứ hai cùng tài khoản xoá mốc của lượt đang nhường) + 4 NHẸ.
- **Lượt 3 (PHIÊN CHÍNH tự sửa — hết 2 lượt qua lại với subagent; cùng một vùng code chạm lần thứ 3 nên gom về MỘT
  bất biến thay vì vá tiếp)**: mỗi lượt hoãn có DANH TÍNH (`Dictionary<long, Guid> _dsTkDangHoanChayLai` + lock
  `_khoaHoan`); id + mốc sống tới lối ra THẬT; chỉ CHỦ lượt (`RutLuotHoan`) được dọn; lượt bị lượt mới hơn thay thế
  RÚT LUI im lặng (không đụng mốc); `DaNhuongXongChoGoiFlashAsync(email, ct)` không đụng mốc nữa; `StopSelected` đọc
  từ điển dưới khoá (`ContainsKey`). Kèm: câu từ chối gói khi mốc đã tới nói "đang chờ lượt hiện tại nhả cầu nối" (hết
  in phút âm); chú thích XAML tab Flash; `KmSync.ApNhomCauHinh` kẹp biên cặp %/SL từ hub qua `KiemTraFormFlash`
  (rác ⇒ giữ cục bộ). Test mới: `DungDaChonTrongLucNhuong_ThiHuyDuoc_KhongMoLai`,
  `LuotHoanThuHai_CungTaiKhoan_KhongXoaMocCuaLuotDangNhuong_VaChiMoMotLan` (đo HÀNH VI, không neo nguồn),
  `HubTraThamSoFlashRac_ThiGiuNguyenSoCucBo`. **Thử phá**: lượt bị thay xoá mốc ⇒ bài 2 ĐỎ; cửa Dừng mù ⇒ bài 1 +
  `BaDuongDung_DeuGoiLuotHuy` ĐỎ; khôi phục, worktree khớp cây chính (`diff -q` identical).
  Đo (worktree sạch): build **0 warning**; FlashHoanChayLai+FlashGoiKheNghi 48/48; Suite.Tests 71/71 (lần đầu đỏ
  `KmVongSauTests` — flake P-20260904-8, xanh khi chạy riêng + 2 lượt sau); Core 457/457; **orders 2341/2341**
  (`--blame-hang 8m`, không treo). Server không đổi ở lượt 3 (207/207 từ lượt 2).
- Không làm (ghi sổ): reset cờ nhường TRƯỚC cửa `_bridgeRunning` (cú bấm bị từ chối vẫn hạ cờ — F1 vòng 2 bù trong
  ≤10s) — P-20260904-9.

### N10 — đo thật (đang làm)
- Hub: publish linux-x64 từ cây làm việc (01:21) → scp → `.bak-20260903-1851` → install → restart → `/health` ok.
- App: tắt lúc gói 05/09 đang nghỉ giữa shop 8→9 (01:5x), build cây chính vào bin thật, mở lại. Không đổi 5%/10 thật
  của user (đó là quyết định kinh doanh) — N10 đo (a) payload `MoTa()` "giảm 5% · SL tham gia 10/SP" trong dòng MỞ
  GÓI, (b) form tab Flash hiện 5/10 nạp từ DB, (c) "chờ shop hiện tại xong" nếu có lượt hoãn rơi trúng gói — ca này
  hiếm hơn sau fix A nên có thể không tái hiện trong đêm; lưới là 5 bài test + thử phá.
- **N10 tiến độ 02:0x**: (a) **ĐẠT** — dòng MỞ GÓI 01:59:56 in `Chương trình: ngày đích 05-09-2026 · lấy HẾT khung giờ
  Shopee mở cho ngày đó · giảm 5% · SL tham gia 10/SP` (hai số đi từ `km_config`, extension bin không còn `const`
  hằng cũ — chỉ còn comment mộ chí). (b) KHÔNG đo live: cửa sổ app bị thu nhỏ lúc 02:0x, khi tôi khôi phục +
  foreground để mở form KM thì đúng lúc gói đang mở tab Shop9x ⇒ 02:04:57 "chờ 30s chưa thấy tab shop mở" — shop
  bị ghi HỎNG oan (vô hại, gói sau thử lại) ⇒ DỪNG mọi thao tác UI lên app khi lát cắt đang chạy; form KM đã có
  test WPF `KmDialog_HaiOFlash_BindDung` + `VmForm_HaiOFlash_*` làm lưới. (c) chờ ca lượt hoãn rơi trúng gói.
  Check đơn: cú "Chạy đã chọn" 02:00:07 bị app từ chối đúng luật vì lát cắt đang chạy; vòng nền sẽ bấm lại ở khe rảnh.
- **N10(b) ĐẠT (02:08)**: form KM (mở từ ribbon lúc 02:04, đọc bằng UIA pattern qua HWND, không chuột) → tab Flash Sale:
  `FlashPhanTramGiamBox = '5'`, `FlashSoLuongThamGiaBox = '10'`, `FlashBatCheck = On` — hai ô nạp đúng từ `km_config`;
  đóng bằng Invoke "Đóng", không đổi ⇒ không lưu/không đẩy hub.
