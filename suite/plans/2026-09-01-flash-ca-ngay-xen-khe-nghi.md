# Flash Sale chạy CẢ NGÀY xen khe nghỉ check đơn — 2026-09-01

Chốt với user (10:4x–11:1x 01/09): vì flash đặt cho NGÀY HÔM SAU nên cả ngày hôm nay đều là giờ hợp lệ.
Bỏ cửa sổ 21–24h; flash chạy thành **nhiều GÓI nhỏ trong ngày**, mỗi gói chiếm **khe nghỉ giữa hai vòng
check đơn**, không cắt vòng đang chạy nữa. Khoảng nghỉ check đơn nới 90'→**120'** (user: "90' vẫn hơi dày").
User hỏi "sao không chạy 1–2 shop 1 lần" → chốt **gói đàn hồi theo khe**: hết một shop thì nhìn đồng hồ,
khe còn đủ chỗ thì làm shop kế — với nghỉ 120' tự ra ~2 shop/gói, ~6 gói/ngày, đủ 12 shop trước ~19h.

Căn cứ đo thật 31/08: mỗi shop đủ 8 khung ≈ 32–36' (4–4,5'/khung, form mới) + mở shop + nghỉ giữa shop
⇒ ~40'/shop. Shopee đóng đăng ký D+1 quanh ~22:50 (lượt 23:20 đêm 31/08 đã hết đỏ) ⇒ hạn bắt đầu
shop mới **22:15**. Không gói nào được vắt qua 00:00 (ngày đích đổi nghĩa) — hạn 22:15 + trần 45'/shop
tự bảo đảm.

Gộp xử lý sổ: **P-20260831-3** (cờ ngày-không-đỏ dừng oan cả lượt — xác nhận bằng code, xem §5),
**P-20260830-6** (hạ hạn chót — bị nuốt vào thiết kế mới), **P-20260901-1** (2h không đủ 12 shop — chính
là lý do đợt này).

## 0. Nguyên tắc bất di bất dịch của đợt này

- **KHÔNG lưu con trỏ tiến độ flash xuống DB / hub.** Test `KhongLuuConTroTienDoNaoXuongDb`
  (orders/XuLyDonShopee.Tests/FlashSaleLuongThatTests.cs:448) đang cấm CÓ CHỦ ĐÍCH (con trỏ trôi lệch
  xuyên máy) — đợt này **tôn trọng**, test đó phải còn XANH NGUYÊN VẸN. Tiến độ ngày giữ **trong bộ
  nhớ** (field của AccountsViewModel); nguồn sự thật là Shopee: app khởi động lại giữa ngày thì gói kế
  mở lại shop đã xong, thấy hết khung thì lướt qua, tự lành.
- **Không đụng luồng KM Shop / Combo** ngoài chỗ bắt buộc (biên an, chữ ký dùng chung). Test hai vòng đó
  phải xanh nguyên.
- **Không đổi hành vi extension per-shop** trừ 2 điểm nêu ở §5/§6.
- Mỗi hằng mới có TÊN + xmldoc lý do (quy ước repo). 0 warning.

## 1. Hằng số & khung giờ — `orders/XuLyDonShopee.Core/Services/KmLichTuDong.cs`

| Hằng | Cũ | Mới | Ghi chú |
|---|---|---|---|
| `GioMoKhungFlashSale` (:71) | 21 | **0** | khung = cả ngày |
| `GioDongKhungFlashSale` (:72) | 24 | **23** | shop cuối bắt đầu ≤22:15 + trần 45' ⇒ xong ≤ ~23:00; giữ kiến trúc int-giờ, KHÔNG rework Khung sang phút |
| `PhutHanChotBatDauShopMoiFlash` (:82) | 1425 (23:45) | **1335 (22:15)** | Shopee đóng đăng ký D+1 ~22:50, đệm 35' |
| `GioGoiLaiCheckDonSauFlash` (:101) + `MocGoiLaiCheckDonSauFlash` (:369) | 22h | **XOÁ** | thay bằng hoãn-tới-cuối-khe (§4) |
| `PhutGianCachGoiFlash` | — | **30** | mới: không mở gói mới trong 30' sau lần thử gần nhất (chặn vòng lặp thử khi gói fail nhanh) |
| `PhutChotShopMoiTruocCuoiKhe` | — | **45** | mới: trong gói, không bắt đầu shop mới khi còn <45' tới cuối khe (khớp trần FlashChay) |
| `PhutKheToiThieuChoGoi` | — | **60** | mới: khe nghỉ <60' thì không mở gói (khe 15' sau vòng cắt là để hạ tầng dịu — không đụng) |
| `TranGoiThuMoiNgayFlash` | — | **8** | mới: trần số lần MỞ GÓI mỗi ngày (đếm in-memory, app restart thì reset — chấp nhận), kế thừa vai trò chống "phóng Brave vô hạn" của chốt một-lượt-mỗi-khung cũ |
| `TranShopLuotFlash` (:331) | 1 | **0** | gỡ trần thử; gói giới hạn bằng THỜI GIAN khe, không bằng số shop. Sửa xmldoc |

Cập nhật toàn bộ xmldoc quanh :57–101 (lý do khung 21h cũ đã lỗi thời — user bác bỏ "ban ngày Shopee
chưa mở khung" bằng bằng chứng lượt 19:03 và draft D+2 hôm 30/08).

## 2. `DenGioChay` tách vòng flash ra hàm riêng

- Thêm PURE `DenGioGoiFlash(KmConfig cf, DateTime bayGio)`:
  1. `VongBat(cf, FlashSale)` — giữ nguyên.
  2. `TrongKhung(bayGio, FlashSale)` — giờ ∈ [0,23).
  3. `ConDuocBatDauShopMoi(bayGio, HanChotLuotTuDong(bayGio, FlashSale))` — 22:15.
  4. **Giãn cách gói**: `cf.FlashLanThuTuDong` cách `bayGio` < `PhutGianCachGoiFlash` ⇒ false.
     (`DaThuTrongKhungNay` không dùng cho flash nữa — chốt một-lượt-mỗi-khung chết theo khung cũ.)
  5. **Hôm nay đã XONG TRỌN**: `cf.FlashLanChayCuoi is DateTime cuoi && cuoi.Date == bayGio.Date` ⇒
     false. (Ý nghĩa mốc ĐỔI: từ "lượt gần nhất có tạo" thành "ngày đích của hôm đó đã đủ mọi shop" — §4.)
- `DenGioChay` cũ: **gỡ hết nhánh FlashSale** (:525–529 cửa 23:45, :536 ngoại lệ mốc-ngoài-khung của
  6582e64 — cả hai hết lý do tồn tại; lỗi "lượt tay đốt lượt auto" nay chặn bằng luật mốc-chỉ-ghi-khi-trọn).
  KM/Combo giữ nguyên hành vi từng bit.
- `NhipLichFlashAsync` chuyển sang gọi `DenGioGoiFlash`.

## 3. Gỡ flash khỏi biên an nhường khung — ⚠ mìn to nhất

Khung flash mới [0,23) + `SoGioBienAn=1` ⇒ nếu giữ nguyên, `KhungCuaBienAn`/`NenNhuongKhung`/
`HoanVongCheckDon` sẽ thấy CẢ NGÀY là biên an flash → check đơn tự dời vòng vô hạn. Sửa:

- `KhungCuaBienAn` (:435–443): bỏ `FlashSale` khỏi phép quét (`MoiLoai` giữ nguyên cho chỗ khác dùng, hoặc
  lọc tại chỗ) + xmldoc: *flash từ 01/09 chờ KHE NGHỈ, không đòi khung — check đơn không phải nhường gì*.
- Hệ quả: `NenNhuongKhung`/`HoanVongCheckDon`/`TrongBienAn` tự hết thấy flash. KM (2,4)/Combo (6,8) giữ nguyên.
- Test `BatFlash_ThiCheckDon_MatDung4TiengMoiDem` (FlashLichTuDongTests.cs:268) đổi thành vế NGƯỢC:
  bật flash ⇒ check đơn KHÔNG bị dời giờ nào nữa (0 giờ, thay vì 4 giờ).

## 4. Tầng App — khe nghỉ, gói, tiến độ, mốc

### 4a. Phơi mốc vòng kế (dựng lại spec cũ plans/2026-08-18-nut-mo-list-shop.md:541–548)

- `IAccountSession` thêm `DateTime? ThoiDiemVongKe { get; }` — non-null ⇔ phiên đang NGHỈ giữa hai vòng.
- `AccountSession.RunBridgeContinuousAsync`: đặt trước `Task.Delay` (:652) bằng `nextRunAt` đã tính
  (:630, sau cả `HoanVongCheckDon`); xoá (null) ngay khi hết nghỉ/bắt đầu vòng/thoát vòng lặp/Stop.

### 4b. `NhipLichFlashAsync` (AccountsViewModel.Phien.cs:588–635) — điều kiện mở gói

Giữ các guard `_dangChay*`/`_bridgeRunning` + `LaMayChuTri`. Thêm, theo thứ tự rẻ→đắt:

1. `DenGioGoiFlash(cf, bayGio)`.
2. Tiến độ in-memory (§4d): còn shop chưa xong cho `NgayDichFlash(bayGio)` (chưa từng chạy hôm nay ⇒ coi
   là còn) VÀ ngày chưa bị đánh dấu "Shopee đóng cửa" (§5) VÀ `_soGoiDaMoTrongNgay < TranGoiThuMoiNgayFlash`.
3. **Mọi phiên đang hoạt động đều đang nghỉ**: `Sessions.Active` rỗng HOẶC tất cả có `ThoiDiemVongKe != null`.
   Có phiên đang giữa vòng ⇒ return êm (nhịp sau thử lại) — **không bao giờ cắt vòng dở nữa**.
4. `cuoiKhe = min(ThoiDiemVongKe các phiên Active)`; `Active` rỗng ⇒ `cuoiKhe = bayGio + interval hiện hành`.
   `cuoiKhe - bayGio < PhutKheToiThieuChoGoi` ⇒ return.
5. Mở gói: ghi `FlashLanThuTuDong = bayGio` (giữ nguyên vị trí ghi-trước-khi-chạy :611), `_soGoiDaMoTrongNgay++`,
   `hanChotGoi = min(cuoiKhe - PhutChotShopMoiTruocCuoiKhe, hôm nay 22:15)`, truyền xuống
   `ChayFlashSaleAsync(...)` làm `hanChot` — cơ chế `ConDuocBatDauShopMoi` kiểm-trước-mỗi-shop có sẵn
   (OrdersBridgeSession.cs:1143) chính là "gói đàn hồi": khe 120' tự ra ~2 shop.
6. `MocChayLaiCheckDonSauFlash = cuoiKhe` đặt cho MỌI gói (không riêng ca ngày-không-đỏ):
   gói xong sớm ⇒ `TraHoSo()` đi nhánh hoãn sẵn có (:1140–1152) chờ đúng phần nghỉ còn lại ⇒ nhịp vòng
   check đơn giữ nguyên như không có flash; gói lố giờ ⇒ chạy lại ngay. Cơ chế 22:00 cũ xoá.

### 4c. Lọc shop đã xong

- `ChayFlashSaleSauPickerAsync`/`ChayVongTaoChuongTrinhAsync` thêm tham số tuỳ chọn
  `boQuaShop: IReadOnlySet<string>?` (khoá = `shopLogin` như :1175) — lọc danh sách NGAY SAU khi đọc
  picker, log MỘT dòng "Bỏ qua N shop đã xong hôm nay: ...". KM/Combo truyền null.

### 4d. Tiến độ in-memory + mốc ngày

Field mới trong AccountsViewModel (KHÔNG persist — xem §0):
`_flashTienDo: (long accountId, DateOnly ngayDich) -> HashSet<string> shopXong`, `_flashNgayDongCua`,
`_soGoiDaMoTrongNgay` (reset khi ngày lịch đổi).

`ChotLuotFlash` (Phien.cs:509–534) đổi luật:
- Cập nhật tiến độ từ `kq.Shops`: shop XONG = `DaTao == true` HOẶC (`BoQua != null` — hết khung/không có
  sản phẩm/không đỏ, đều nghĩa là "hôm nay không còn gì làm ở shop này"). Shop có `Loi` ⇒ KHÔNG xong (gói
  sau thử lại).
- **Ghi `FlashLanChayCuoi = luc` CHỈ KHI ngày đích đã TRỌN**: `kq.SoShopChuaChay == 0 && !kq.Captcha`
  và mọi shop của danh sách lượt này (kể cả số bị lọc ở §4c) đã nằm trong tập XONG. Giữ nguyên chuỗi
  `with { FlashLanChayCuoi = luc }` (test grep :448 đang neo). Lượt tay lẻ 1 shop ⇒ KHÔNG ghi ⇒ hết
  lỗi "lượt tay đốt lượt auto" mà không cần ngoại lệ :536.

## 5. P-20260831-3 — bỏ "dừng cả lượt giữa danh sách" của ngày-không-đỏ

Hiện: `OrdersBridgeSession.cs:1239–1248` return sớm khi `mot.NgayMaiKhongDo` ⇒ shop kín khung (đo 31/08:
lịch MẤT ĐỎ cả khi chỉ shop này kín) giết oan các shop còn lại. Với gói ≤2–3 shop, cái "tiết kiệm 11 lượt
render" mà early-return mua về không còn tồn tại. Sửa:

- **Xoá early-return :1239–1248**; shop không-đỏ xử như mọi `BoQua` khác: ghi kết quả, `vePicker`, sang
  shop kế. `KetQuaKm.NgayMaiKhongDo` đặt ở CUỐI lượt = "MỌI shop trong lượt đều `NgayMaiKhongDo && !DaTao`"
  (lượt rỗng ⇒ false).
- `ChotLuotFlash`: `kq.NgayMaiKhongDo` (nghĩa mới) VÀ các shop đó chưa từng xong trong tiến độ ⇒ đặt
  `_flashNgayDongCua = ngayDich` — nhịp thôi mở gói tới hết ngày. (Ca xấu sau app-restart: 2 shop đầu đều
  đã kín từ gói trước ⇒ đóng oan phần còn lại của ngày — chấp nhận, ghi xmldoc; tiến độ in-memory làm ca
  này hiếm.)
- Extension `flow-flashsale.js` `doChayFlashSale` (~:4232–4256): **thêm `soTaiChe` và giữ `soKhungDaTao`
  trong gói gửi** (hiện `soTaiChe` bị rơi); C# `DocKetQuaKmMotShop` (:1343–1385) parse `soKhungDaTao`,
  `soTaiChe` (vắng ⇒ 0) vào `KetQuaKmMotShop` — phục vụ log + điều kiện DaTao đã có. Cập nhật comment
  :3812–3815 (ghi "đã xử 01/09").

## 6. Trần thời gian + interval

- `OrdersBridgeChannel.ChoChang.FlashChay` (:321): 1800 → **2700s** (8 khung × 4,5' ≈ 36' + K0 + đệm; đo
  thật 31/08 suýt vượt 1800). Sửa luôn 3 chú thích còn ghi 900s: OrdersBridgeSession.cs:1108,
  OrdersBridgeChannel.cs:439–440, :801–802. Test grep `HanChang_FlashChay...` (:502) cập nhật chuỗi 2700.
- `AppGeneralSettings.DefaultOrderIntervalMinutes` (Core/Models/AppGeneralSettings.cs:18): 30 → **120**
  + xmldoc (khe nghỉ phải chứa được một gói flash ~2 shop; user chốt 01/09). Kẹp [1,1440] giữ nguyên.
  Giá trị ĐANG LƯU của user (key `order_interval_minutes`, app.db) cập nhật lúc deploy — bước §9.

## 7. UI text

- `KmDialog.xaml` :276, :287–288, :299, :305: khung "21-24h"/"Mỗi đêm 21:00"/"23:45"/"1 shop mỗi lượt" →
  mô tả mới (cả ngày tới 22:15, chạy từng gói trong khe nghỉ check đơn, đủ danh sách shop).
- `AccountsView.xaml` :733–738, :744: tooltip trần 1 shop + 23:45 → cập nhật.

## 8. Test — danh sách phải sửa (từ khảo sát) + test mới + thử phá

Sẽ ĐỎ và phải viết lại theo luật mới (không xoá trắng — đổi vế + giữ lý do):
FlashLichTuDongTests: `DenGioChayFlash_MaTranGioVaCongTac` (ma trận giờ mới: 00:30 T · 11:00 T · 22:14 T ·
22:20 F · 23:30 F), `LuotDauHong_ThiNhipPhutSau_KHONG_BanLai` (→ giãn cách 30'), `MocChayKeFlash_La2100`
(→ 00:00), `ChuKyFlash_CungMotNgay`, `BatFlash_ThiCheckDon_MatDung4TiengMoiDem` (→ 0 giờ, §3),
`SauHanChot...` (→ 22:14/22:15). FlashSaleLuongThatTests: `HanChotFlash_La2345...` (→22:15),
`Vong_KhongBatDauShopMoiSau2345` (→22:15), `MocGoiLaiCheckDon_*2200` ×2 + `ChotLuotFlash_NgayMaiKhongDo_
ThiDatMocGoiLaiLuc2200` (→ hoãn-cuối-khe), `NgayMaiKhongDo_ThiDungCaLuot` (→ nghĩa mới §5).
KmLichTuDongTests: `TrongKhung_MaTran...`, `TrongKhung_BonMoc...`, `HoanVongCheckDon_*` ×3,
`HanChotLuotTuDong_*`. FlashHoanChayLaiTests: cả lớp → mốc cuối-khe. AppGeneralSettingsTests: default 120.
KmFormTests.SoShopSeChay: đã viết chống-ghim, tự xanh với trần 0 — kiểm lại.

PHẢI CÒN XANH NGUYÊN: `KhongLuuConTroTienDoNaoXuongDb` (:448) — không đụng Database.cs/KmConfigRepository.cs
ngoài xmldoc; field in-memory chỉ nằm ở AccountsViewModel.

Test MỚI (kèm THỬ PHÁ từng cái — đảo logic phải đỏ):
1. `DenGioGoiFlash` ma trận: giờ, hạn 22:15, giãn cách 30' (29' F / 31' T), mốc-hôm-nay-xong F, mốc-hôm-qua T.
2. Mốc chỉ ghi khi trọn: lượt SoTao>0 nhưng `SoShopChuaChay>0` ⇒ không ghi; lượt phủ nốt shop cuối ⇒ ghi.
3. Lượt tay 1 shop KHÔNG đốt lượt auto (thay test của 6582e64).
4. `ThoiDiemVongKe`: đặt khi vào nghỉ, null khi vòng chạy/Stop.
5. Nhịp không mở gói khi có phiên `ThoiDiemVongKe == null` (đang giữa vòng); mở khi tất cả nghỉ; khe <60' bỏ.
6. `hanChotGoi = min(cuoiKhe-45', 22:15)` — hai vế min.
7. `boQuaShop` lọc đúng + log; KM/Combo không bị ảnh hưởng (null).
8. `KetQuaKm.NgayMaiKhongDo` nghĩa mới: 1 shop không-đỏ giữa 3 shop ⇒ lượt vẫn đủ 3 shop, cờ false;
   cả 3 không-đỏ-0-tạo ⇒ true.
9. Parse `soKhungDaTao`/`soTaiChe` (JSON thiếu ⇒ 0).
10. `TranGoiThuMoiNgayFlash`: gói thứ 9 trong ngày không mở.

## 9. Trình tự thi công + deploy (phiên chính làm ở chặng 5)

1. opus-dev: sửa theo §1→§8, build `ShopeeSuite.sln` 0 warning, toàn bộ test orders + suite xanh,
   KHÔNG commit.
2. Nghiệm thu + phản biện song song (nghiệm thu độc quyền build/test; phản biện chỉ đọc + unit không cổng).
3. Chốt: commit; copy extension → bin (`Copy-Item -Recurse -Force extensions\shopee-orders\* <bin>\extensions\shopee-orders\`);
   tắt app đang chạy; cập nhật `order_interval_minutes` = 120 trong app.db (sqlite, lúc app tắt);
   mở app (`SHOPEE_DEV_TOOLS=1`), tick "Chọn tất cả", Chạy; canh log: nhịp phải mở GÓI 1 cho ngày đích
   02/09 ngay khe nghỉ đầu tiên, xem gói tự đóng ở ~2 shop và check đơn hoãn tới cuối khe.

## 10. Tiêu chí nghiệm thu ĐO ĐƯỢC

1. `dotnet build ShopeeSuite.sln`: 0 lỗi, **0 warning**.
2. `dotnet test orders/XuLyDonShopee.Tests` + `suite/Shopee.Core.Tests` (+ suite khác nếu đụng): xanh 100%,
   số test ≥ hiện trạng (422 + orders hiện có) — không được xanh bằng cách xoá test.
3. `KhongLuuConTroTienDoNaoXuongDb` xanh, KHÔNG bị sửa nội dung khẳng định.
4. Mỗi test mới ở §8 có biên bản thử phá (đảo code → đỏ → khôi phục) trong báo cáo opus-dev.
5. Grep nguội: không còn tham chiếu `GioGoiLaiCheckDonSauFlash`/`MocGoiLaiCheckDonSauFlash`/nhánh FlashSale
   trong `DenGioChay`/`KhungCuaBienAn`; không còn chuỗi "23:45"/"21-24h" trong xmldoc+XAML nói về flash.
6. Live (chặng 5): log app cho thấy MỘT gói thật mở trong khe nghỉ, tạo/kích hoạt chương trình 02/09 cho
   ≥1 shop, `ConDuocBatDauShopMoi` chặn shop kế đúng hạn gói, check đơn chạy lại đúng mốc cuối khe.
