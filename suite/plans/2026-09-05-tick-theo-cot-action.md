# Plan: Tick tiến độ theo từng cột action (bỏ tick đầu hàng)

- **Ngày:** 2026-09-05
- **Trạng thái:** hoàn thành
- **Người lập:** Cursor (plan) · **Người thực thi:** Auto

## 1. Bối cảnh & mục tiêu

Tab **Shopee → Tài khoản → lưới shop** (`AccountsView` DataGrid, 6 cột) hiện có **một** dấu tick xanh ở cột 40px đầu hàng (`ShopPrepareRow.ShowTick`). Cột đó được **cả check đơn lẫn KM/Combo/Flash dùng chung**:

- Check đơn: `AccountSession` bắn `ShopCheckChanged` → `_shopDaCheck` → `DaKiemTra` → tick.
- KM/Combo/Flash: `GhiTienDoChuongTrinh` **cũng** gọi `RaiseShopCheckChanged` (cố ý tái sử dụng cột 40px — comment tại `AccountsViewModel.Phien.cs` ~3846). Xong Flash Sale nên cả hàng mang tick, dễ hiểu nhầm là “shop xong hết”.
- Bắt đầu vòng KM/Combo/Flash còn `luotChayMoi: true` → xóa `_shopDaCheck` (tick check đơn biến mất) để người xem “theo” vòng chương trình trên **cùng một** dấu tick.

User muốn: **bỏ tick đầu hàng**; mỗi flow có tick **nhỏ hơn, trong đúng ô** (Chuẩn bị hàng / KM / Combo / Flash Sale). Không còn một dấu V cho cả dòng.

Hiện từng ô action đã có số hoặc `—` hoặc chữ “đang kiểm tra…” / “đang chạy…”. Việc này chỉ đổi **chỗ hiện tick** + **ngắt dùng chung** `ShopCheckChanged` cho vòng chương trình.

## 2. Phạm vi

- **Làm:**
  - Cột 40px: **bỏ IconCheck** (và bỏ vòng quay — chữ trạng thái đã nằm trong ô action). **Giữ X đỏ** lỗi địa chỉ (user chỉ đòi bỏ tick, không đòi bỏ X).
  - Ô **Chuẩn bị hàng**: tick nhỏ khi shop đã check xong lượt hiện tại (`DaKiemTra && !IsChecking`); đang check thì vẫn chữ “đang kiểm tra…”, không tick.
  - Ô **KM / Combo / Flash Sale**: tick nhỏ khi ngày đang lọc **đã có dòng** `km_program_daily` (`*SoSanPham != null`) và ô đó **không** đang `*DangChay`. `—` = chưa chạy loại đó → không tick. Số (kể cả `0`) + tick đứng cạnh nhau.
  - `GhiTienDoChuongTrinh` **ngừng** gọi `RaiseShopCheckChanged` — vòng chương trình chỉ còn `ShopProgramChanged` + ghi DB.
  - KM/Combo/Flash **không** xóa `_shopDaCheck` khi đọc list shop (`luotChayMoi: false` ở ba `ChayKmAsync` / `ChayComboAsync` / `ChayFlashSale…`). `doVaoLuoiShops: true` giữ nguyên (shop mới vẫn có dòng). Check đơn vẫn reset tick của chính nó qua `AccountSession.RaiseShopListChanged` (default `luotChayMoi: true`).
- **Không làm:**
  - Đổi luật ghi `prepare_daily` / `km_program_daily`, lịch chạy, Hub, extension.
  - Persist tick check đơn qua restart (vẫn session `_shopDaCheck` như cũ).
  - Đổi banner lỗi địa chỉ / nút Đóng.
  - Refactor DataGrid / style ngoài 4 ô + cột 40px.

## 3. Các bước thực hiện

1. **`orders/XuLyDonShopee.App/ViewModels/ShopPrepareRow.cs`**
   - Đổi xmldoc: tick không còn “cột tiến độ một dấu”.
   - Giữ `ShowTick` = `DaKiemTra && !IsChecking && !CoLoiDiaChi` (tick **Chuẩn bị hàng**; test cũ còn gọi tên này thì giữ hoặc đổi hết sang `ShowTickChuanBi` — chọn **một** tên, cập nhật test cho khớp).
   - Thêm `ShowTickKm` / `ShowTickCombo` / `ShowTickFlash`: `*SoSanPham.HasValue && !*DangChay`.
   - `[NotifyPropertyChangedFor]` trên `KmSoSanPham`, `ComboSoSanPham`, `FlashSoSanPham`, `LoaiDangChay` phải bắn thêm các `ShowTick*`.

2. **`orders/XuLyDonShopee.App/Views/AccountsView.xaml`**
   - Cột 40px: chỉ còn IconClose (`ShowLoiDiaChi`). Bỏ IconCheck + PathIcon `spin`.
   - Mỗi ô action: `StackPanel` ngang, `HorizontalAlignment="Right"`: `PathIcon` `IconCheck` **9×9** `SuccessBrush` + số/`—`. Khi đang chạy: chỉ chữ “đang kiểm tra…” / “đang chạy…”, ẩn tick + ẩn số như hiện tại.
   - Comment lưới: 6 cột, tick theo ô, cột đầu chỉ X.

3. **`orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs`**
   - `GhiTienDoChuongTrinh`: xóa dòng `RaiseShopCheckChanged`. Cập nhật xmldoc (không còn “cột 40px dùng lại”).
   - Ba chỗ `luotChayMoi: true` của KM/Combo/Flash → `false`. Comment: reset tick check đơn **không** thuộc vòng chương trình; `doVaoLuoiShops` vẫn upsert shop mới.

4. **Test — sửa + thêm (thử phá bắt buộc với test mới / test đổi nghĩa)**
   - `orders/XuLyDonShopee.Tests/KmCotChuongTrinhTests.cs`
     - `VongChuongTrinh_CungBanShopCheckChanged_VongQuayVaTick`: **đổi nghĩa** — Combo bắt đầu: `ComboDangChay`, **không** `IsChecking`, **không** `ShowTick` (check đơn). Combo xong: `ComboSoSanPham==3`, `ShowTickCombo`, `IsChecking==false`.
     - `VongChuongTrinh_XoaTickLuotTruoc_VaDungDongChoShopMoi`: **đổi vế 1** — sau khi Combo đọc list (`luotChayMoi: false`), tick check đơn **còn** trên A/B; ô Combo A/B vẫn `—` / chưa tick Combo. Vế shop mới + số shop mới **giữ**. Cuối lượt: shop mới có `ComboSoSanPham` + `ShowTickCombo`, **không** bắt `ShowTick` (check đơn) trên shop mới.
   - `orders/XuLyDonShopee.Tests/ShopCheckProgressTests.cs`: cập nhật comment “cột 40px”; assert `ShowTick` vẫn đúng cho **ô Chuẩn bị hàng**. Không đòi tick hiện ở cột đầu (không test XAML).
   - Thêm 1–2 case thuần trên `ShopPrepareRow`: `ShowTickFlash` true khi `FlashSoSanPham=0` (đã chạy, 0 sp); false khi `null`; false khi `FlashDangChay`.
   - Thử phá: gán `FlashSoSanPham=null` → test tick Flash phải đỏ; khôi phục.

5. **Build / test**
   ```
   dotnet build orders/XuLyDonShopee.App/XuLyDonShopee.App.csproj
   dotnet test orders/XuLyDonShopee.Tests/XuLyDonShopee.Tests.csproj
   ```
   0 warning.

## 4. Tiêu chí nghiệm thu

- [x] Cột 40px XAML không còn `IconCheck` / spinner; còn X khi `ShowLoiDiaChi`.
- [x] Check đơn xong shop A: `ShowTick` (Chuẩn bị hàng) true; `ShowTickKm/Combo/Flash` không bật chỉ vì check đơn.
- [x] Combo/KM/Flash xong shop A: đúng `ShowTick*` của loại đó; `IsChecking` vẫn false; `ShowTick` check đơn không đổi vì vòng chương trình.
- [x] Ô loại chưa chạy ngày lọc (`null` / `—`): không tick loại đó.
- [x] Ô loại đã ghi DB (`0` hoặc >0): có tick loại đó khi không đang chạy.
- [x] Bắt đầu Combo sau khi đã check hết shop: tick Chuẩn bị hàng **không** bị xóa; shop mới vẫn có dòng lưới.
- [x] `dotnet build` App 0 warning; `dotnet test` XuLyDonShopee.Tests xanh (ghi số thật vào báo cáo).

## 5. Rủi ro & lưu ý

- **Hai luồng không chạy đồng thời trên một TK** (đã ghi trong code cũ) — ngắt `ShopCheckChanged` khỏi chương trình không đụng khóa phiên.
- Mở lại app: tick KM/Combo/Flash **còn** (đọc DB theo ngày lọc); tick check đơn **mất** (session) — đúng phạm vi “không persist”.
- `LoadResults` dựng lại dòng → `ApplyShopCheckFlags` gắn lại `DaKiemTra` từ `_shopDaCheck`; số chương trình từ DB tự kéo `ShowTick*`.
- Gương Hub / `LatCat` không đụng.
- Sổ: `luotChayMoi` mỗi gói Flash từng xóa tick check đơn — sau plan này vòng Flash không xóa `_shopDaCheck` nữa (hệ quả tốt, không mở việc sổ).

---

## Báo cáo thực thi (điền sau khi xong)

- Cột 40px chỉ còn X lỗi địa chỉ. Tick 9×9 trong ô Chuẩn bị hàng / KM / Combo / Flash Sale.
- `GhiTienDoChuongTrinh` không còn `RaiseShopCheckChanged`. KM/Combo/Flash `luotChayMoi: false`.
- Nghiệm thu ([nghiệm thu](12329f1a-2af3-4868-a8ca-57b7f53b18d2)): **7/7 ĐẠT**. Rebuild 0 warning. `dotnet test` **2403 passed**.
- Phản biện ([phản biện](08c886b6-f315-4139-a877-11f9aeb42414)): VỪA-1/2 (X và tick vẫn loại trừ dù đã tách cột) + VỪA-3 (`ShowTickKm` không có assert dương) — **đã sửa sau phản biện**. NHẸ-4/5/6 + ngoài phạm vi → sổ P-20260905-1…6.
- Sau sửa: `ShowLoiDiaChi = CoLoiDiaChi`; `ShowTick = DaKiemTra && !IsChecking`; test KM dương trong `ShowTickChuongTrinh_TheoSoVaDangChay`.
