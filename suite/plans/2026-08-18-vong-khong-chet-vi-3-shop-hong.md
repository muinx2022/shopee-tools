# Plan: Vòng không chết vì 3 shop hỏng liên tiếp (lỗi "chờ 30s chưa thấy tab shop mở")

- **Ngày:** 2026-08-18
- **Trạng thái:** đang làm
- **Người lập:** phiên chính · **Người thực thi:** `opus-dev`

## 1. Bối cảnh & mục tiêu

Điều tra log 8 ngày (`%APPDATA%\XuLyDonShopee\logs\hoatdong-202608*.log`) tìm ra **18 lần** lỗi
`extension LỖI: chờ 30s chưa thấy tab shop mở`, gom thành **6 chuỗi, mỗi chuỗi ĐÚNG 3 shop liên tiếp**,
và mỗi chuỗi giết trọn một vòng.

| Ngày | Vòng bắt đầu | Lỗi đầu | Trôi | Shop hỏng |
|---|---|---|---|---|
| 11/08 | 10:31:25 | 10:44:39 | 13' | 3,4,5 |
| 12/08 | 11:07:30 | 11:22:20 | 15' | 4,5,6 |
| 14/08 | 12:16:13 | 12:49:34 | 33' | 8,9,10 |
| 17/08 | 15:40:44 | 16:10:19 | 30' | 7,8,9 |
| 17/08 | 20:10:49 | 20:30:47 | 20' | 5,6,7 |
| 17/08 | 22:09:39 | 22:43:18 | 34' | 7,8,9 |

Bốn dữ kiện chốt hướng sửa:

1. **Không phải shop cụ thể** (chỉ số 3→10), **không phải hết giờ** (trôi 13'→34').
2. **Không bao giờ tự khỏi trong cùng vòng** — 0/6.
3. **Mở lại trình duyệt là khỏi — 6/6.** Vòng NGAY SAU mỗi lần đổ vỡ đều chạy trọn 12/12 shop.
   Chi phí mở lại đo được: **5 giây** (17:49:58 mở → 17:50:03 đã về trang chọn shop, cookie hồ sơ còn hạn
   nên bỏ qua đăng nhập Playwright).
4. Cả 18 lỗi rơi trong 10:00–23:00, **không lỗi nào từ 00:00–10:00** dù đêm nào cũng chạy đủ vòng.

Đã LOẠI ba hướng sai: (a) lỗi sort dropdown — ngày 10/08 hỏng **86 lần**, chẩn đoán giống hệt từng chữ
(`menu tong=2 hien=0`), mà KHÔNG một lỗi tab nào ⇒ bug cũ, độc lập; (b) `visibility=hidden` — 100% mẫu đều
hidden, không phân biệt được ca hỏng/ca chạy; (c) "trình duyệt chết trước" — dòng `extension ĐỨT` +
`mã thoát -1` là khối `finally` của app TỰ giết, tức hệ quả.

**Cơ chế (suy từ code, CHƯA có bằng chứng log trực tiếp — xem H5):** đường hết giờ 30s ở
`flow-shop.js:230` `return` mà KHÔNG gọi `nhoTabShop`, nên tab mở ở giây thứ 31 thành **mồ côi**;
`doCloseShopTab` thấy `ctx.shopTabId == null` nên chỉ điều hướng lại picker, không đóng nó. Cộng với
`ensureListTab` (`core.js:135`) lấy **tab khớp CUỐI CÙNG** mà không ưu tiên `/portal/shop`, và
`ctx.listTabId` không được lưu bền (chỉ đồng bộ theo gói wake 20s) trong khi SW MV3 chết theo chu kỳ ~240s
còn nhịp nghỉ giữa shop là 3–5' ⇒ gần như lần nào cũng phải dò lại và có thể bám nhầm tab mồ côi.

**Sai lầm thiết kế lớn nhất:** `OrdersBridgeSession.cs:1002` CỐ Ý không mở lại trình duyệt ở nhánh chạm trần,
với lý lẽ *"mở lại chỉ đổi chỗ đốt thời gian"*. Dữ kiện (3) nói ngược: mở lại chính là thứ chữa khỏi, 6/6,
giá 5 giây. Giá đang trả cho niềm tin đó: **90 phút chờ + 6–9 shop không có dữ liệu, mỗi lần**.

**Mục tiêu:** vòng không còn chết vì chuỗi này; nếu có chết thì mất ít shop hơn và chờ ngắn hơn; và để lại
bằng chứng đủ để chốt cơ chế ở lần sau.

## 2. Phạm vi

**Làm** (5 hạng mục, đúng thứ tự giá trị đã chốt với user):

- **H1 (C#)** Chạm trần 3 shop hỏng liên tiếp → **MỞ LẠI trình duyệt** rồi chạy tiếp phần chưa chạy của
  CHÍNH vòng đó; chỉ dừng vòng khi hết lượt mở lại / đang dính captcha / không còn việc phía sau.
- **H2a (extension)** `openShopDetail` **quét dọn tab `banhang` mồ côi** trước khi bấm Chi tiết.
- **H2b (extension)** `ensureListTab` **ưu tiên tab đang ở `/portal/shop`**.
- **H3 (C#)** `LaLoiCauNoi` nhận thêm mốc `"chưa thấy tab shop mở"` ⇒ shop rơi kiểu này được vào hàng đợi thử lại.
- **H4 (C#)** Vòng bị cắt → **nghỉ ngắn 15'** thay vì trọn 90', nhưng CHỈ lần đầu (vòng cắt thứ hai liên tiếp
  quay về interval đầy, tránh dập Shopee khi hỏng dai).
- **H5 (extension)** In `ctx.lastTabUrls` vào chính câu lỗi 30s — mảnh bằng chứng còn thiếu.

**Không làm:**

- Không đụng bug sort dropdown (`KHÔNG đổi được sắp xếp`) — đã chứng minh độc lập, để việc riêng.
- Không nâng hạn 30s chờ tab shop (chưa có bằng chứng Shopee chậm; nâng mù làm mỗi shop hỏng đắt thêm).
- Không đụng `TranShopHongLienTiep = 3` và `TranMoLaiTrinhDuyet = 2`.
- Không commit / push / phát hành.

## 3. Các bước thực hiện

### H1 — `orders/XuLyDonShopee.Core/Services/OrdersBridgeSession.cs`

1. Thêm giá trị `MoLaiTrinhDuyetHongLienTiep` vào `enum SauShopHong` (~dòng 107) kèm xmldoc tiếng Việt nói rõ
   bằng chứng 6/6 ở trên.
2. Đổi hàm thuần `QuyetDinhSauShopHong` (~dòng 378) thành
   `QuyetDinhSauShopHong(int hongLienTiep, bool conShopPhiaSau, bool veDuocPicker, bool conLuotMoLai, bool captcha)`
   với luật:
   - `hongLienTiep >= TranShopHongLienTiep` ⇒ `(!captcha && conShopPhiaSau && conLuotMoLai)`
     → `MoLaiTrinhDuyetHongLienTiep`, ngược lại → `DungVongHongLienTiep`.
   - Còn lại giữ nguyên: `conShopPhiaSau && !veDuocPicker` → `DungVongKhongVePicker`; mặc định `ChayTiepShopKe`.
   - Xét captcha theo ĐÚNG lý lẽ đã có ở `QuyetDinhKhiMatPicker`: đang bị nghi là bot thì đừng đẩy thêm một
     lượt mở trình duyệt.
   - Cập nhật xmldoc, và sửa luôn câu *"mở lại chỉ đổi chỗ đốt thời gian"* ở khối comment dòng ~1002 — nó là
     căn cứ SAI, để nguyên là lần sau có người khôi phục hành vi cũ.
3. Chỗ gọi (~dòng 998): truyền thêm `conLuotMoLai: soLanMoLai < TranMoLaiTrinhDuyet`, `captcha: _channel.CaptchaSeen`.
   Nhánh mới `MoLaiTrinhDuyetHongLienTiep`: ghi log nêu rõ lý do + số shop chưa chạy, rồi **gán
   `matPicker = KetQuaVePicker.CauNoiChet`** để rơi vào ĐÚNG khối mở lại đã có (dòng ~1021–1067) — KHÔNG chép lại
   code mở lại. Khối đó đã tự `hongLienTiep = 0`, `i = -1`, `continue` khi mở lại thành công.
4. Soát lại `canVePicker` (~dòng 993): chạm trần thì vẫn KHÔNG cần đóng tab shop (trình duyệt sắp bị đóng cả) —
   giữ nguyên biểu thức, chỉ kiểm cho chắc.

### H3 — cùng file + `OrdersBridgeChannel.cs`

5. Thêm hằng `MocLoiKhongMoDuocTabShop = "chưa thấy tab shop mở"` cạnh `MocLoiMatTabShop`
   (`OrdersBridgeChannel.cs:152`), **khớp đúng chuỗi extension đang bắn** (`flow-shop.js:230`).
6. `LaLoiCauNoi` (~dòng 408) nhận thêm mốc này. An toàn vì `NenThuLaiShopRoiOan` xét `DaGuiChuanBiHang` TRƯỚC,
   mà lỗi này rơi ở bước ĐẦU TIÊN của shop (chưa hề gửi `prepareNextOrder`).

### H4 — `orders/XuLyDonShopee.App/Services/AccountSession.cs`

7. Thêm hằng có tên `PhutNghiSauVongCat = 15` + hàm THUẦN
   `internal static int PhutNghiTruocVongKe(bool vongBiCat, int soVongCatLienTiepTruocDo, int intervalMin)`
   → `vongBiCat && soVongCatLienTiepTruocDo == 0 ? Math.Min(PhutNghiSauVongCat, intervalMin) : intervalMin`.
   (`Math.Min` chặn ca người dùng đặt interval < 15'.)
8. Trong vòng chạy: đếm `soVongCatLienTiep`; "vòng bị cắt" = `result.Error is not null && !result.Captcha`.
   Vòng thành công (hoặc captcha — captcha đã có nhánh nghỉ riêng) reset về 0. Dùng kết quả hàm thuần cho
   `nextRunAt` + `Task.Delay` (dòng ~549–551) và cho câu log "Vòng kế tiếp dự kiến bắt đầu lúc ...".

### H2a + H2b + H5 — extension

9. `extensions/shopee-orders/flow-shop.js`, trong `openShopDetail`, **sau** `ensureShopPicker` và **trước** khi
   chụp `before`: quét `chrome.tabs.query({url:"https://banhang.shopee.vn/*"})`, đóng mọi tab **không phải**
   `ctx.listTabId` và **không phải** `ctx.shopTabId`; đóng được cái nào thì `send({action:"progress"})` nói rõ
   số tab đã dọn (cấm im lặng nuốt).
10. `flow-shop.js:230`: câu lỗi 30s kèm `ctx.lastTabUrls` (H5). **GIỮ NGUYÊN cụm chữ `chưa thấy tab shop mở`**
    — H3 khớp theo chuỗi này.
11. `extensions/shopee-orders/core.js` `ensureListTab` (~dòng 131–137): trong tập `hit` của mỗi `s`, **ưu tiên
    tab có `/portal/shop` trong URL** (lấy cái cuối trong nhóm ưu tiên); không có thì giữ nguyên hành vi cũ
    (`hit[hit.length - 1]`).
12. `extensions/shared/` là NGUỒN CHUẨN — nếu (và chỉ nếu) có sửa file trong `shared/`, chạy
    `extensions/sync-shared.cmd`. Kế hoạch hiện tại KHÔNG đụng `shared/`.

### Test

13. Cập nhật `orders/XuLyDonShopee.Tests/ShopHongKhongGietCaVongTests.cs` cho chữ ký mới; **thêm** ma trận ca cho
    `MoLaiTrinhDuyetHongLienTiep`: hết lượt mở lại → `DungVongHongLienTiep`; captcha → `DungVongHongLienTiep`;
    không còn shop phía sau → `DungVongHongLienTiep`; đủ ba điều kiện → `MoLaiTrinhDuyetHongLienTiep`.
14. Thêm test cho `LaLoiCauNoi` với chuỗi THẬT lấy từ log
    (`"Extension báo lỗi: chờ 30s chưa thấy tab shop mở"`) và test `NenThuLaiShopRoiOan` VẪN từ chối khi
    `daGuiChuanBiHang: true`.
15. Thêm test ma trận cho `PhutNghiTruocVongKe` (gồm ca `intervalMin = 10` < 15).
16. **THỬ PHÁ bắt buộc** (luật 4, `D:\Projects\CLAUDE.md`): với MỖI test mới, sửa ngược code cho hỏng → chạy →
    test phải ĐỎ → khôi phục. Báo cáo phải ghi rõ đã phá cái gì và test nào đỏ.

## 4. Tiêu chí nghiệm thu

- [ ] **T1** `dotnet build ShopeeSuite.sln` — **0 error, 0 warning**. Dán số thật.
- [ ] **T2** `dotnet test orders/XuLyDonShopee.Tests/XuLyDonShopee.Tests.csproj` — **0 failed**; ghi tổng số test
      trước/sau (số sau phải LỚN HƠN).
- [ ] **T3** `QuyetDinhSauShopHong(3, conShopPhiaSau: true, veDuocPicker: false, conLuotMoLai: true, captcha: false)`
      → `MoLaiTrinhDuyetHongLienTiep`.
- [ ] **T4** Cùng đầu vào nhưng `conLuotMoLai: false` → `DungVongHongLienTiep`; `captcha: true` →
      `DungVongHongLienTiep`; `conShopPhiaSau: false` → `DungVongHongLienTiep`.
- [ ] **T5** `hongLienTiep = 2` giữ nguyên hành vi cũ: `veDuocPicker: true` → `ChayTiepShopKe`;
      `conShopPhiaSau: true, veDuocPicker: false` → `DungVongKhongVePicker`.
- [ ] **T6** `LaLoiCauNoi(new InvalidOperationException("Extension báo lỗi: chờ 30s chưa thấy tab shop mở"))` →
      `true`; và `NenThuLaiShopRoiOan(daGuiChuanBiHang: true, loiCauNoi: true, ...)` → `false`.
- [ ] **T7** `PhutNghiTruocVongKe(true, 0, 90)` → `15`; `PhutNghiTruocVongKe(true, 1, 90)` → `90`;
      `PhutNghiTruocVongKe(false, 0, 90)` → `90`; `PhutNghiTruocVongKe(true, 0, 10)` → `10`.
- [ ] **T8** `node --check` xanh cho `extensions/shopee-orders/flow-shop.js` và `core.js`.
- [ ] **T9** `grep -c "chưa thấy tab shop mở" extensions/shopee-orders/flow-shop.js` ≥ 1 — cụm chữ H3 khớp vẫn còn.
- [ ] **T10** Đọc lại diff `OrdersBridgeSession.cs`: nhánh mở lại **KHÔNG chép lại** code mở lại trình duyệt, mà
      đi qua khối `matPicker` đã có.
- [ ] **T11** Báo cáo có mục THỬ PHÁ: mỗi test mới ↔ sửa ngược gì ↔ test nào đỏ ↔ đã khôi phục.

## 5. Rủi ro & lưu ý

- **Nguy hiểm nhất — chạy lại shop ĐÃ in phiếu.** Đường mở lại đi qua `GhepShopConLai` + `daChayShopId`; tuyệt đối
  không được nới tập đó. Shop hỏng đã được `daChayShopId.Add` NGAY đầu thân shop (dòng ~861) nên mở lại sẽ KHÔNG
  chạy lại nó — giữ nguyên tính chất này.
- **Đừng để hai đường cùng quyết một việc.** `QuyetDinhSauShopHong` mới trả `MoLaiTrinhDuyetHongLienTiep` rồi khối
  `matPicker` lại hỏi `QuyetDinhKhiMatPicker`; hai hàm phải cho cùng kết luận với cùng đầu vào (đều xét captcha
  trước, rồi còn-việc-phía-sau, rồi trần mở lại). Lệch là vòng rơi vào `DungVong` ngay sau khi vừa quyết mở lại.
- **H2a có thể đóng nhầm tab.** Chỉ đóng tab khớp `banhang.shopee.vn` mà KHÁC cả `listTabId` lẫn `shopTabId`.
  Lúc `openShopDetail` chạy thì `shopTabId` đáng lẽ đã null (shop trước đã đóng) — nhưng vẫn phải loại trừ nó,
  vì SW có thể vừa khôi phục id từ storage.
- **H4 có thể dập Shopee** nếu vòng hỏng dai. Đó chính là lý do có chốt "chỉ nghỉ ngắn ở vòng cắt ĐẦU TIÊN".
- **Cây làm việc:** `git status` sạch, branch `main`, lúc bắt đầu 18/08/2026. Máy hay có nhiều phiên Claude song
  song — thấy file `M` mà mình không đụng thì DỪNG, báo lại, đừng sửa.
- **Không commit.** Phiên chính chốt việc mới quyết.

---

## Báo cáo thực thi

### Chặng 2 — `opus-dev`

H1–H5 làm đủ. Build `0 Warning / 0 Error`; test `1773 → 1790` (Failed: 0). Tự thử phá 10 lượt (A–J), lượt nào
cũng có test đỏ đúng chỗ.

Ba việc làm NGOÀI plan, đều tự khai: (1) nhánh `DungVongHongLienTiep` truyền thêm cờ captcha ra ngoài;
(2) H5 chụp lại `chrome.tabs.query({})` tại chỗ lỗi thay vì in `ctx.lastTabUrls` cũ (mạnh hơn plan — giá trị cũ
có thể là ảnh của vài phút trước); (3) thêm test `MocLoiKhongMoDuocTabShop_KhopBanExtension` khoá khớp chuỗi
JS↔C#.

### Chặng 3 — `nghiem-thu`

**10/11 ĐẠT, 1 đạt một phần, 0 KHÔNG ĐẠT.** Tự chạy lại build + test (số khớp lời khai), tự làm lại 3/10 lượt
thử phá (A, E, H) — cả 3 đỏ đúng như khai, khôi phục xong `git diff` khớp md5 ban đầu. Xác nhận cây làm việc
chỉ có file của việc này. T11 đạt một phần vì lúc đó mục báo cáo này còn trống.

### Chặng 4 — `phan-bien`

Không dựng nổi ca in phiếu trùng / đếm trùng (đã lần `daChayShopId`, `GhepShopConLai`, khoá `ShopId`, ba tầng
chặn của hàng đợi thử lại) — vùng nguy hiểm nhất SẠCH. Xác nhận `soLanMoLai` chỉ có một chỗ `++`, không đường
reset ⇒ trần 2 chặn thật. Xác nhận `QuyetDinhSauShopHong` và `QuyetDinhKhiMatPicker` KHÔNG lệch kết luận.
Xác nhận `donTabShopMoCoi` không có ca đóng nhầm, và `ensureListTab` giữ nguyên hành vi cũ ở đường
`gotoSellerCentre`.

Bắt được 7 lỗi. Tất cả đã sửa ở chặng 5.

### Chặng 5 — phiên chính chốt việc

Bảy sửa thêm, mỗi cái kèm hàm thuần + test:

| # | Lỗi | Sửa |
|---|---|---|
| 1 | Vòng `PickupAddressFailed` (chạy TRỌN mọi shop, chỉ bỏ shop lỗi địa chỉ) bị tính là "vòng cắt" ⇒ nghỉ ngắn oan, lao vào một vòng 12 shop sớm hơn 75' mà không cứu gì | hàm thuần `AccountSession.LaVongBiCat(coLoi, captcha, loiDiaChi)` |
| 2 | `soVongCatLienTiep` là biến CỤC BỘ ⇒ bốc hơi mỗi lần bấm Dừng→Chạy; đúng ca "hỏng dai" thì chốt không chặn gì (nhật ký 10/08: 13 vòng cắt liên tiếp xen giữa hàng loạt lần bấm lại) | đưa lên field `_soVongCatLienTiep` (manager giữ nguyên một `AccountSession` mỗi tài khoản) |
| 3 | Mở lại trình duyệt GIỮA LƯỢT THỬ LẠI làm `GhepShopConLai` trả rỗng ⇒ phần hàng đợi còn lại bị bỏ IM LẶNG, mà tổng kết vẫn khoe "đã thử lại 3 shop" | hàm thuần `GhepShopThuLaiConLai` (lọc theo picker, KHÔNG theo `daChayShopId`) + đếm `soShopDaThuLai` thật + dòng ⛔ khi lượt thử lại không đi hết |
| 4 | Đảo hai dòng "tính phút / cộng chuỗi" thì H4 chết im lặng, không test nào đỏ | gộp thành MỘT hàm `NghiSauVong` trả cả hai — không còn thứ tự để đảo |
| 5 | Cờ captcha mới truyền ra làm nhánh `if (result.Captcha)` nuốt mất banner + cảnh báo lỗi địa chỉ (lỗi này CÓ SẴN từ trước, bản vá chỉ làm nó dễ gặp hơn) | tách hai lời gọi `_persist` ra khỏi chuỗi `if/else` — chạy ở MỌI lối ra |
| 6 | Ngưỡng mở lại nằm ở hai chỗ tự so; đổi một bên thành `<=` không làm đỏ test nào | hàm thuần `ConLuotMoLai` + test khoá nó khớp `QuyetDinhKhiMatPicker` |
| 7 | Câu log "MỞ LẠI trình duyệt để cứu 0 shop chưa chạy" tự mâu thuẫn; dòng "bỏ N shop còn lại" không đếm hàng đợi thử lại | nói đúng cái đang cứu; `soBo` cộng cả hàng đợi |

Còn một điểm phản biện nêu mà **chưa sửa, cố ý**: `openShopDetail` vẫn `return` ở đường hết giờ 30s mà không
`nhoTabShop`, nên tab mở ở giây thứ 31 vẫn thành mồ côi — H2a chỉ dọn hậu quả ở lượt sau. Chặn tận gốc là việc
riêng, ngoài 5 hạng mục.

### Số kiểm chứng cuối (phiên chính tự chạy)

- `dotnet build ShopeeSuite.sln` → **0 Warning, 0 Error**.
- `dotnet test orders/XuLyDonShopee.Tests` → **Failed: 0, Passed: 1815** (1773 → 1790 → **1815**, +42 so với gốc).
- `node --check` xanh cho `flow-shop.js` + `core.js`.
- **Thử phá vòng chốt** (4 hàm thuần mới, mỗi lượt phá → chạy → khôi phục):

| Phá | Sửa ngược | Kết quả |
|---|---|---|
| K | `ConLuotMoLai`: `<` → `<=` | ĐỎ 1/32 |
| L | `GhepShopThuLaiConLai`: đảo bộ lọc `conLai` | ĐỎ 4 khẳng định |
| M | `LaVongBiCat`: bỏ vế `!loiDiaChi` | ĐỎ 1/32 |
| N | `NghiSauVong`: chuỗi không bao giờ reset | ĐỎ 1/32 |

Khôi phục xong chạy lại: 32/32 xanh.

### CÒN THIẾU — nói thẳng

1. **Chỗ nối hàm thuần ↔ vòng chạy vẫn không có lưới.** Xoá đúng một dòng `matPicker = KetQuaVePicker.CauNoiChet;`
   là vô hiệu hoàn toàn trần 3-shop, mà 1815 test vẫn xanh. Đã thu hẹp mặt tấn công (gom ngưỡng về `ConLuotMoLai`,
   gộp `NghiSauVong`) nhưng KHÔNG đóng được: `RunAllShopsAsync` tự dựng channel + phóng trình duyệt, không có
   harness nào drive được nó. Dựng harness đó là việc riêng.
2. **H2a/H2b/H5 (extension) có 0 test.** Repo không có hạ tầng test JS. Chỉ `node --check` + đọc code.
3. **Cơ chế gốc vẫn là suy luận.** Chưa có bằng chứng log trực tiếp cho chuỗi "tab mồ côi → bám nhầm picker".
   H5 sinh ra chính là để lấy bằng chứng đó ở lần hỏng sau.
4. **Chưa chạy thật.** Toàn bộ bản vá chưa qua một vòng production nào.
