# Plan: MỘT khung giờ, xoay vòng chương trình theo NGÀY

- **Ngày:** 2026-08-27
- **Trạng thái:** chờ làm (lượt chạy kiểm chứng combo đang chạy, build sẽ phải tắt app)
- **Người lập:** phiên chính · **Người thực thi:** `opus-dev`

## 1. Bối cảnh — đổi cái gì, vì sao

Ngày 25/08 người dùng chốt **ba khung giờ trong MỘT ngày**, mỗi khung một loại chương trình, chu kỳ 3 ngày:
KM `02–04h` · Combo `06–08h` · Flash Sale `10–12h`.

**Người dùng đổi ý 27/08** sau khi nhìn hệ thống chạy thật: *"ta có thể thay đổi thành 1 khung giờ cho 3 ngày
không? vd, hnay chạy km, mai chạy combo, kia chạy flash, rồi lại quay lại"*. Và chốt ba điểm:

- **Chọn theo NGÀY** (suy từ lịch, không lưu trạng thái).
- **Ngày của chương trình chưa dùng được thì ĐỂ TRỐNG đêm đó** — không nhường cho loại khác.
- **Giữ khung `02:00–04:00`.**

### Vì sao đổi là đúng — số, không phải cảm tính

| | 3 khung/ngày (đang chạy) | 1 khung, xoay vòng |
|---|---|---|
| Check đơn mất mỗi ngày | **6 tiếng** — biên an `[1,4) ∪ [5,8) ∪ [9,12)` | **3 tiếng** — chỉ `[1,4)` |
| Chạy giữa ban ngày | có (khung `10–12h`) | **không** |
| Khe cho check đơn BẮT ĐẦU một vòng | hai khe hẹp `[4,5)` và `[8,9)` | liền mạch `04:00 → 01:00` hôm sau |
| Nhịp mỗi chương trình | 3 ngày/lần | 3 ngày/lần — **KHÔNG đổi** |

Khung `10–12h` giữa ban ngày là rủi ro đã ghi ở `plans/2026-08-25-combo-khuyen-mai.md` §6.1; bản này xoá nó mà
không mất gì.

## 2. Thiết kế

### A. MỘT khung cho cả ba loại

`Khung(loai)` thôi trả ba giá trị khác nhau — mọi loại dùng chung `02:00–04:00`. Xoá
`GioMoKhungCombo`/`GioDongKhungCombo`/`GioMoKhungFlashSale`/`GioDongKhungFlashSale`.

**Trình biên dịch phải chỉ ra mọi chỗ gọi** — đừng giữ hàm `Khung(loai)` với thân trả cùng một giá trị cho ba
nhánh, vì nó để lại ấn tượng sai rằng khung còn phụ thuộc loại. Nếu bỏ tham số thì bỏ hẳn.

Kéo theo, tự nhiên:
- `TrongBienAn(moc)` chỉ còn `[01:00, 04:00)`.
- `HoanVongCheckDon` chỉ dời tới `04:00`.
- `HanChotLuotTuDong` luôn `04:00`.

### B. Chọn loại theo NGÀY — hàm THUẦN, KHÔNG lưu trạng thái

```
LoaiChuongTrinhCuaNgay(DateTime ngay)  →  ChuongTrinhShop | Combo | FlashSale
```

Suy từ **số thứ tự ngày tuyệt đối** (ví dụ `DateOnly.FromDateTime(ngay).DayNumber % 3`), KHÔNG phải từ
`LanChayCuoi` hay một con trỏ lưu trong DB.

**Vì sao bắt buộc không trạng thái** — đây là lý do quyết định, không phải sở thích: hồ sơ KM đồng bộ xuyên
máy, và fleet có nhiều máy. Một con trỏ "lần trước chạy loại nào" sẽ **trôi lệch** ngay khi một máy bỏ lỡ một
đêm (tắt máy, mất mạng, thua khoá) — lúc đó máy A tưởng đêm nay tới lượt Combo còn máy B tưởng tới lượt KM.
Suy từ ngày lịch thì **mọi máy tự khớp nhau** mà không cần đồng bộ thêm một trường nào.

⚠ Dùng `DayNumber` (số ngày tuyệt đối), **KHÔNG** dùng `DayOfYear % 3`: `DayOfYear` nhảy về 1 mỗi năm nên
ngày 31/12 và 01/01 có thể trùng loại, làm một loại bị bỏ một vòng ở ranh giới năm. Có test cho ca đó.

### C. Hai cửa, giữ CẢ HAI — đừng gộp

`DenGioChay(cf, bayGio, loai)` nay đòi thêm **đúng lượt của ngày**:

1. `LoaiChuongTrinhCuaNgay(bayGio) == loai` — **hôm nay tới lượt loại này chưa**;
2. `VongBat(cf, loai)` — **loại này có đang bật không**;
3. `TrongKhung` · `DaThuTrongKhungNay` · `LanChayCuoi + SoNgay` — y như hiện tại.

Vế 3 giữ nguyên vì `SoNgay` là **chu kỳ người dùng đặt**, còn xoay vòng là **thứ tự**. Hai thứ khác nhau:
`SoNgay = 3` thì hai cái trùng khít; `SoNgay = 7` thì tới lượt mà chưa tới hạn ⇒ **để trống**, đúng ý người
dùng đã chốt. Gộp hai vế làm một là mất khả năng đặt chu kỳ dài hơn 3 ngày.

### D. ĐỂ TRỐNG, không nhường

Đêm tới lượt Flash Sale (chưa có luồng) hoặc tới lượt một vòng đang TẮT ⇒ **không chạy gì**. KHÔNG dò tiếp
sang loại khác.

Người dùng chốt vậy, và nó cũng đúng: đêm trống là đêm check đơn chạy trọn vẹn, không phải đêm lãng phí. Nhường
sẽ làm KM và Combo thành cách nhau 2 ngày thay vì 3 — tức **nhiều lượt SSO hơn ý người dùng**.

### E. `MocChayKe` phải nói THẬT

Câu "lần kế dự kiến" hiện tính `LanChayCuoi + SoNgay` kéo về `02:00`. Nay mốc đó còn phải **rơi đúng ngày của
loại đó**. Sửa để nó trả về ngày hợp lệ ĐẦU TIÊN thoả cả hai; nếu không sửa thì câu tổng kết hứa một đêm mà
đêm ấy không phải lượt của nó — đúng loại "phần mềm nói sai" mà đợt này đã phải sửa ba lần.

## 3. Tiêu chí nghiệm thu

- [ ] `LoaiChuongTrinhCuaNgay` là hàm THUẦN, chỉ phụ thuộc ngày. Ba ngày liên tiếp ⇒ ba loại KHÁC nhau; ngày
      thứ tư quay lại loại đầu.
- [ ] **Ca ranh giới năm**: 31/12 và 01/01 kề nhau ⇒ loại phải KHÁC nhau (đây là ca giết `DayOfYear % 3`).
- [ ] Hai máy khác nhau, cùng một ngày ⇒ cùng một loại (test bằng cách gọi hàm hai lần với hai `KmConfig` khác
      nhau — nó không được phụ thuộc `cf`).
- [ ] `TrongKhung` = `[02,04)` cho **cả ba** loại; ma trận đủ 24 giờ.
- [ ] `TrongBienAn` chỉ `[01,04)`; `HoanVongCheckDon` dời `01:30 → 04:00`, và **GIỮ NGUYÊN** `05:30`, `09:30`,
      `13:00` (các mốc trước đây bị dời, nay không còn lý do).
- [ ] `HanChotLuotTuDong` = `04:00` cho cả ba loại.
- [ ] Tới lượt Combo mà `ComboBat = false` ⇒ **KHÔNG chạy gì**, và KHÔNG rơi sang KM.
- [ ] Tới lượt Flash Sale ⇒ **KHÔNG chạy gì** (chưa có luồng).
- [ ] Tới lượt KM mà `LanChayCuoi` mới hôm qua + `SoNgay = 7` ⇒ **KHÔNG chạy** (vế chu kỳ vẫn sống).
- [ ] `MocChayKe` trả về một ngày **đúng là lượt của loại đó**; có test cho ca `SoNgay = 7`.
- [ ] Build sln + hub **0 warning**; 4 bộ test xanh.
- [ ] **THỬ PHÁ** từng vế mới, ghi danh sách test đỏ. Bắt buộc có: bỏ vế "đúng lượt của ngày" ⇒ phải ĐỎ;
      đổi `DayNumber` thành `DayOfYear` ⇒ ca ranh giới năm phải ĐỎ.

## 4. Rủi ro

1. **Đụng lịch của vòng KM đang chạy THẬT hằng đêm.** `KmVongRigTests` + `KmLichTuDongTests` là lưới chính.
2. **Đêm đầu sau khi đổi có thể lệch một nhịp** so với thói quen hiện tại — không hại, nhưng nói trước để
   người dùng không tưởng là hỏng.
3. Combo vẫn giữ `ComboSanSang` và `TranShopLuotCombo = 0`; **KHÔNG đụng** trong việc này.
