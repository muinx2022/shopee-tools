# Plan: Ghi mốc hoàn tất Combo/KM khi mọi shop rỗng hợp lệ & Chờ lắng trang trước khi click

- **Ngày:** 2026-09-17
- **Trạng thái:** đang làm
- **Người lập:** phiên chính

## 1. Bối cảnh & mục tiêu

KM và Combo Khuyến Mãi được người dùng cấu hình chạy tự động định kỳ **3 ngày/lần**. Mục đích chính là: định kỳ quét qua danh sách shop, shop nào có **sản phẩm mới đăng** thì tạo KM / Combo cho sản phẩm mới đó.

Tuy nhiên thực tế vận hành ngày 16–17/09/2026 phát sinh 2 vấn đề:

1. **Bẫy logic không ghi mốc khi không có sản phẩm mới:**
   - Khi toàn bộ 16 shop đều đã có Combo dài hạn (180 ngày), không có sản phẩm mới nào được đăng trong 3 ngày qua → bot duyệt qua 16 shop và đều kết luận hợp lệ: `BỎ QUA — modal KHÔNG còn sản phẩm nào để thêm (bảng tự khai rỗng)`.
   - Nhưng `ChotLuotCombo` và `ChotLuotKm` hiện chỉ ghi mốc `ComboLanChayCuoi` / `LanChayCuoi` khi `kq.SoTao > 0`.
   - Do `SoTao == 0`, app coi như lượt chạy chưa thành công → **đêm nào cũng bật dậy lúc 01:00 mở trình duyệt quét 16 shop**, lặp đi lặp lại vô ích và tăng nguy cơ bị Shopee quét.
   - **Mục tiêu:** Khi một lượt quét đã hoàn tất trọn vẹn mọi shop và mọi shop đều có kết luận hợp lệ (hoặc tạo được hoặc bỏ qua do rỗng, không có lỗi kỹ thuật / captcha) → GHI MỐC để app yên tâm nghỉ đúng chu kỳ `SoNgay` (3 ngày) mới quét lại.

2. **Bị Shopee kích hoạt `verify/traffic/error` vì click sidebar quá sớm:**
   - Trong `flow-combo.js` và `flow-km.js` (Bước 1), ngay khi phát hiện link Khuyến Mãi trong sidebar (`coLink > 0`), extension thực hiện click ngay lập tức (chỉ 1–2 giây sau khi tab shop mở).
   - Trang quản trị người bán của Shopee vừa render HTML thô, các script xác thực phiên / WAF / telemetry ngầm chưa nạp xong. Thao tác click DOM quá nhanh khiến Shopee chuyển hướng tab sang `shopee.vn/verify/traffic/error`.
   - **Mục tiêu:** Thêm nhịp chờ 3 giây (`await sleep(3000)`) sau khi sidebar xuất hiện để trang thực sự "lắng" trước khi click.

## 2. Phạm vi

### Làm:
- Thêm hàm thuần `KmLichTuDong.NenGhiMoc(KetQuaKm kq)` để dùng chung cho cả KM và Combo.
- Cập nhật `ChotLuotKm` và `ChotLuotCombo` trong `AccountsViewModel.Phien.cs` dùng `KmLichTuDong.NenGhiMoc`.
- Thêm độ trễ 3s chờ lắng trang trong `flow-combo.js` và `flow-km.js` bước 1.
- Viết unit tests bao phủ:
  - Ca mọi shop `BoQua` (bảng rỗng hợp lệ) → GHI mốc.
  - Ca có shop lỗi `Loi != null` → KHÔNG ghi mốc.
  - Ca dính captcha / lỗi cả lượt → KHÔNG ghi mốc.
  - Ca dở dang (còn shop chưa chạy) mà `SoTao == 0` → KHÔNG ghi mốc.
  - Ca `SoTao > 0` → VẪN GHI mốc như cũ.
- Chờ lượt check đơn hiện tại của `ShopeeSuite.exe` hoàn tất.
- Bump version `version.txt`, cập nhật `CHANGELOG.md`, chạy `release-suite.cmd` đóng gói và push git.

### Không làm:
- Không thay đổi cấu trúc bảng database `km_config` (các cột `combo_lan_chay_cuoi`, `lan_chay_cuoi` giữ nguyên).
- Không thay đổi luồng Flash Sale (Flash Sale đã có cơ chế gói riêng từ 01/09).

## 3. Các bước thực hiện

1. **`orders/XuLyDonShopee.Core/Services/KmLichTuDong.cs`**:
   - Thêm hàm `public static bool NenGhiMoc(KetQuaKm kq)`:
     ```csharp
     if (kq.SoTao > 0) return true;
     return kq.Shops.Count > 0
         && kq.SoShopChuaChay == 0
         && kq.SoHong == 0
         && !kq.Captcha
         && kq.Loi == null
         && kq.TranThuDaCham == 0
         && kq.MocBatDauDaTroiQua == null
         && !kq.NhuongChoCheckDon
         && kq.Shops.All(s => s.DaTao || s.BoQua is not null);
     ```
2. **`orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs`**:
   - Trong `ChotLuotKm`: đổi `if (kq.SoTao > 0)` thành `if (KmLichTuDong.NenGhiMoc(kq))`.
   - Trong `ChotLuotCombo`: đổi `if (kq.SoTao > 0)` thành `if (KmLichTuDong.NenGhiMoc(kq))`.
3. **`extensions/shopee-orders/flow-combo.js`**:
   - Trong Bước 1, sau `if (!coLink) { ... }`, thêm `await sleep(3000);` trước `pageClickTheoSelector`.
4. **`extensions/shopee-orders/flow-km.js`**:
   - Trong Bước 1, tương tự thêm `await sleep(3000);` trước `pageClickTheoSelector`.
5. **Unit Tests**:
   - Thêm test trong `KmMocLanChayCuoiTests.cs` và `ComboKhuyenMaiTests.cs` đo ca "tất cả shop bỏ qua vì không có sản phẩm".
   - Chạy toàn bộ test solution, kiểm tra 0 warning, 0 error. Thử phá.
6. **Publish & Release**:
   - Chờ phiên check đơn của máy dev kết thúc.
   - Bump version `version.txt` (1.9.5 -> 1.9.6), ghi `CHANGELOG.md`.
   - Commit git, chạy `release-suite.cmd`, push git lên remote.

## 4. Tiêu chí nghiệm thu

- [ ] `KmLichTuDong.NenGhiMoc(kq)` trả về `true` khi `kq.Shops` toàn shop `BoQua` hợp lệ, không lỗi.
- [ ] `KmLichTuDong.NenGhiMoc(kq)` trả về `false` khi có shop bị lỗi, captcha, hoặc chạy dở dang chưa hết shop.
- [ ] `ChotLuotKm` và `ChotLuotCombo` ghi đúng mốc vào `KmConfig` khi mọi shop rỗng hợp lệ.
- [ ] `flow-combo.js` và `flow-km.js` có độ trễ 3s chờ lắng trước khi click sidebar Khuyến Mãi.
- [ ] Toàn bộ unit tests của `XuLyDonShopee.Tests` và solution pass 100%, 0 warning.
- [ ] Thử phá thành công (sửa ngược code làm test đỏ).
- [ ] Chờ check đơn xong, build thành công, release v1.9.6 và push git.
