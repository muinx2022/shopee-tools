# Plan: Flash Sale Của Shop (chương trình thứ 3)

- **Ngày:** 2026-08-27
- **Trạng thái:** chờ làm (đang vướng: ba cột KM/Combo chưa chạy test)
- **Nguồn DOM:** người dùng dump tay từ shop thật, 27/08/2026

## 1. Luồng, theo đúng mô tả người dùng

1. Vào Chi tiết shop → sidebar **Kênh Marketing → Flash Sale Của Shop**
2. Bấm **Tạo** ở `landing-page-header`
3. **Thông tin cơ bản** → bấm ô **Khung thời gian**
4. Modal *"Chọn khung giờ Flash sale Của Shop"*: chọn NGÀY (chỉ ngày còn khung) → tick radio một KHUNG → **Xác nhận**
5. Bấm **Thêm sản phẩm**
6. Modal *"Chọn Sản Phẩm"*: bấm mũi tên sắp xếp cạnh **Doanh số** **HAI LẦN** (⇒ cao→thấp) → tick ô chọn-tất-cả ở **trang N** → **Xác nhận**
7. Đóng modal → **Xác nhận** lần nữa ⇒ chương trình được tạo
8. Khung kế của CÙNG ngày: lặp lại, nhưng lấy **trang N+1** trong modal sản phẩm
9. Hết khung của ngày ⇒ sang ngày đỏ kế, **chỉ số trang quay lại 1** (người dùng chốt 27/08)
10. Hết ngày đỏ của tháng hiện tại ⇒ xong shop

**Ghi nhớ trong lượt:** khung nào đã dùng thì lần sau KHÔNG chọn lại.

## 2. Selector đã có (từ dump thật)

> ⚠ **Đây là họ `eds-*` (Vue), KHÔNG phải `eds-react-*`.** Modal sản phẩm của Flash Sale khác HẲN modal của
> Combo — `flow-combo.js` dùng `eds-react-table`/`data-row-key`, ở đây là `eds-table`/`tr.mkt-product-selector-row-in-root`.
> Chép selector từ `flow-combo.js` sang là hỏng ngay bước 6. Gần `flow-km.js` hơn.

| Bước | Selector |
|---|---|
| Sidebar | `a[href="/portal/marketing/shop-flash-sale/list"]` (`test-id="In-Shop Flash Sale"`) |
| Nút Tạo | `.landing-page-header button.landing-page-btn` (span `Tạo`) |
| Ô khung thời gian | `button.time-slot-btn` (có `.has-value` khi đã chọn) |
| Ô ngày | `.eds-date-table__cell` |
| — ngày DÙNG ĐƯỢC | chứa `div.timeslots`, **không** có class `disabled` |
| — ngày KHÔNG dùng được | chứa `div.no-timeslot` (kèm `disabled`) |
| — số khung của ngày | `p.slots-count` → `"7 khung giờ"` |
| Radio khung giờ | `input.eds-radio__input[value=…]`, nhãn ở `span.eds-radio__label` (`"02:00:00 - 09:00:00"`) |
| — khung qua đêm | `span.cross-day-tag` → `" +1"` |
| — sp của khung | `.products .available` → `"Số sản phẩm tham gia 10"` |
| Xác nhận khung | `button.confirm-btn` |
| Thêm sản phẩm | `.detail-products-container .empty-content button` (span `Thêm sản phẩm`) |
| Cột Doanh số | `th` có `.eds-table__cell-label` = `Doanh số` → `.eds-table__sort-icons` |
| Chọn tất cả trang | `.eds-table__fix-left .eds-table__header th:first-child input.eds-checkbox__input` |
| Dòng sp | `tr.mkt-product-selector-row-in-root`; mã sp ở `p` → `"Mã: 57813898569"` |
| Phân trang | `.eds-pagination li.eds-pager__page`, kế tiếp `.eds-pager__button-next` |
| Xác nhận modal sp | footer `button.eds-button--primary` (disabled tới khi có tick) |

**10 dòng mỗi trang** — khớp "mỗi lần chỉ chọn 10 sp" và khớp `Số sản phẩm tham gia 10`.

## 3. Ba chỗ CHƯA đo được từ dump, phải dò khi chạy thật

1. **Sắp xếp giảm dần xác nhận bằng gì.** — ✅ **ĐÃ ĐO XONG, HẾT ẨN SỐ** (lượt dò 11, Minoa Store, 28/08/2026
   23:02). Không còn chữ "CHỜ ĐO" nào trong mục này.

   ### SỐ ĐO CHỐT — lượt 11 (Minoa Store, 28/08/2026 23:02)

   Cả ba mốc đều có `nguonTinHieu = "lop-phu"`, tức mốc đọc trên một bảng Shopee ĐÃ báo sắp xếp xong.

   | mốc | Doanh số 10 dòng đầu | kết luận |
   |---|---|---|
   | M0 (chưa bấm) | `1,0,0,0,0,0,0,0,0,0` | thứ tự mặc định |
   | M1 (1 cú `down`) | `0,0,0,0,0,0,0,0,0,0` | **tăng dần** |
   | M2 (2 cú `down`) | **`14,4,4,4,3,3,2,2,2,2`** | **GIẢM DẦN** ✅ |
   | M3 (3 cú `down`) | `1,0,0,…` + mã **giống hệt M0** | về đúng ban đầu ✅ |

   **Ba kết luận, dùng thẳng, đừng dò lại:**

   1. **2 cú bấm `down` = giảm dần = bán chạy nhất trước.** Chu kỳ ba trạng thái người dùng mô tả ĐÚNG hoàn
      toàn, và M3 quay về giống hệt M0 — tức cú thứ BA thật sự xoá sạch sắp xếp.
   2. **Lớp phủ `.eds-table__loading` CÓ chạy thật** (hiện sau 1 nhịp, tắt sau 3) ⇒ `choBangSauBamSapXep` dùng
      được NGUYÊN cho luồng thật, không cần tầng chờ thứ ba.
   3. **CỬA KIỂM của luồng thật = dãy Doanh số phải KHÔNG TĂNG DẦN.** Dãy tăng dần ⇒ HỎNG shop, không tạo;
      dãy giảm dần ⇒ đạt; **dãy toàn 0 ⇒ vẫn ĐẠT** (mọi giá trị bằng nhau thì không có cặp nào tăng). Đây là
      thứ THAY cho cửa kiểm dựa trên class đã bị bác bỏ ở dưới — xem `dayDoanhSoKhongTangDan` trong
      `flow-flashsale.js`.

   Bốn ẩn số còn lại cũng chốt cùng lượt: **8 khung/ngày** · **10 sp/trang** · ô "Xem sản phẩm có sẵn" mặc định
   **BẬT** · lịch **đọc được ngày đỏ**.

   Phần dưới đây giữ lại làm HỒ SƠ vì sao cửa kiểm không đặt ở class nút — đừng lấy nó ra dùng lại.

   > Nguyên văn người dùng: *"bấm 1 lần là up, lần nữa là down, lần nữa là không sắp xếp, như chưa bấm gì"*

   Chu kỳ **BA trạng thái**: `chưa sắp xếp → up (tăng) → down (giảm) → chưa sắp xếp → …`
   ⇒ **ĐÚNG 2 CÚ BẤM = giảm dần = doanh số cao nhất trước.** Khớp yêu cầu gốc.

   ⚠ **RÀNG BUỘC CỨNG — cú thứ BA là tai hoạ âm thầm.** Nó xoá sạch sắp xếp và trả bảng về thứ tự ban đầu:
   luồng sẽ chọn 10 sản phẩm **tuỳ ý** thay vì 10 sản phẩm bán chạy nhất, mà mọi bước sau vẫn `ok` và nhật ký
   vẫn xanh. Ba điều bắt buộc, đã cài trong `flow-flashsale.js` (`SO_LAN_BAM_SAP_XEP`):
   1. **CẤM mọi cơ chế thử-lại quanh cú bấm sắp xếp** — một vòng retry bọc ngoài đẻ ra cú thứ ba mà không ai
      cố ý, và không ai nhìn thấy.
   2. Số cú bấm phải **BẰNG** 2, không phải "ít nhất 2".
   3. Trạng thái đích là **down**; trạng thái "như chưa bấm gì" phải bị chấm **HỎNG**, không phải "bấm thêm".

   ### ⚠ CỬA KIỂM DỰA TRÊN TRẠNG THÁI NÚT — **BỎ HẲN** (số đo lượt 9, 27/08/2026)

   Bản đầu của plan này chốt cửa kiểm là `T0 ≠ T1` và `T1 ≠ T2` trên **class của nút**. **Số đo bác bỏ dứt
   điểm** — bốn mốc, cùng một lượt, đã bấm đúng thẻ `<i>`:

   | mốc | `clsIcon` | thứ tự 10 mã | `doanhSo` |
   |---|---|---|---|
   | M0 | `up` + `down` | **A** | 10 giá trị, toàn `"0"` |
   | M1 (sau 1 cú `down`) | y hệt | **A — không đổi** | toàn `"0"` |
   | M2 (sau 2 cú `down`) | y hệt | **B — ĐÃ ĐỔI** | toàn `"0"` |
   | M3 (sau 1 cú `up`) | y hệt | B — không đổi | toàn `"0"` |

   ⇒ **`clsIcon`/`clsCum` GIỐNG HỆT ở cả bốn mốc, `ariaSort` null ở cả bốn.** EDS **không** gắn class trạng
   thái sắp xếp. Cửa kiểm dựa trên class là một cửa **luôn trượt** — không phải "chờ đo", mà **BỎ HẲN**.

   Cùng lượt đó xác nhận cú bấm ĐÃ ăn: thứ tự mã đổi sau 2 cú. Khớp chu kỳ người dùng mô tả — cú 1 = *up*, mà
   shop toàn 0 doanh số nên tăng dần trùng thứ tự mặc định (M1 = M0); cú 2 = *down* ⇒ đảo.

   ### TRỌNG TÀI DUY NHẤT: **THỨ TỰ MÃ SẢN PHẨM**

   Lượt dò đọc 10 mã theo đúng thứ tự bảng (`"Mã: 57813898569"`, …). Đây là trọng tài duy nhất còn lại, và nó
   tốt hơn class ở mọi mặt: **quan sát trực tiếp**, không phụ thuộc EDS có gắn class hay không, và không chết
   ở ca shop toàn 0 doanh số.

   Phép so Doanh số vẫn **BỎ** làm cửa (lý do cũ vẫn đúng: dữ liệu có thể toàn 0 ⇒ proof đo rỗng). Doanh số
   chỉ đi vào nhật ký.

   ### ⚠ HẠN CHẾ ĐÃ BIẾT: shop toàn 0 doanh số thì KHÔNG xác nhận được CHIỀU

   Alina Store1 (vị trí #1) có cả 10 giá trị Doanh số = `"0"`. Với dữ liệu đó, "tăng dần" và "giảm dần" là hai
   thứ tự **hợp lệ như nhau**, nên thứ tự KHÔNG ĐỔI cũng là kết quả đúng (đó chính là M1 = M0). ⇒ Muốn chốt
   "cao nhất trước" phải chạy trên shop **có doanh số thật**.

   **Nhắm shop khác:** đặt biến môi trường `FLASH_VITRI_SHOP` (chỉ số **đếm từ 0**) trước khi mở app; mỗi bản
   dump tự khai `shop {viTri, shopId, ten}`.

   ### Câu hỏi cuối — ĐÃ TRẢ LỜI (lượt 11, 28/08/2026)

   ```
   M0 → down → M1 → down → M2 → down (cú thứ BA) → M3
   ```

   **M3 có quay về giống hệt M0 không? — CÓ.** Mã sản phẩm ở M3 giống hệt M0 và Doanh số quay về `1,0,0,…`.
   Chu kỳ ba trạng thái được xác nhận bằng số đo, không còn là lời kể.

   ⚠ Cú thứ ba CHỈ hợp lệ trong lượt dò (lượt dò kết thúc bằng Hủy, không tạo gì). **Luồng thật không được
   chép đoạn đó.**

2. **Số ngày đỏ thật của một tháng.** — ✅ ĐÃ ĐO: **8 khung/ngày**, lịch đọc được ngày đỏ. Bước 4 vẫn **đếm và
   ghi nhật ký** số ngày đỏ + tổng số khung ngay lượt đầu.
3. **Ô "Xem sản phẩm có sẵn"** (`data-cy="product-modal-available-checkbox"`) — ✅ ĐÃ ĐO: mặc định **BẬT**.
   Luồng thật **KHÔNG đụng vào ô này**: mặc định đã đúng thứ ta cần (chỉ sp còn nhận được), và tick nhầm là đổi
   cả tập sản phẩm.

4. **Đêm CUỐI THÁNG: ngày mai thuộc tháng SAU.** Đặt cho ngày mai thì đêm 31 phải chọn ngày 1 tháng sau —
   picker đang mở tháng hiện tại, phải bấm mũi tên sang tháng. Trong dump, `eds-month-table__col` của Tháng
   9–12 **không** có class `disabled` nên nhiều khả năng sang được, nhưng **chưa ai thử**.

   ⇒ **Luật "chỉ chọn ngày đỏ trong THÁNG HIỆN TẠI" (đặt ra ở §1 bước 9 hồi thiết kế theo lô) nay BỎ.** Nó là
   sản phẩm của bản cũ — bản đó quét cả tháng một lượt nên cần một biên để dừng. Bản nhật ngày chỉ nhắm đúng
   MỘT ngày, nên biên đó vô nghĩa và giữ lại thì mỗi tháng mất đúng một đêm (đêm 31) mà không có lý do gì.
   Luật đúng: **luôn nhắm ngày mai, bất kể nó rơi vào tháng nào.**

   Lượt dò DOM đầu tiên phải kiểm ca này: mở picker, bấm sang tháng sau, xác nhận có ngày đỏ và chọn được.

## 4. Ngân sách thời gian — số, không phải cảm tính

Đơn giá một chương trình, suy từ nhịp đo THẬT của lượt combo đêm 27/08 (mỗi thao tác DOM 1–6s, mỗi lần lật
trang modal 3,6s):

| Chặng | Ước |
|---|---|
| Về danh sách + bấm Tạo | ~8s |
| Modal khung: mở → chọn ngày → radio → Xác nhận | ~10s |
| Modal sp: mở + sắp xếp ×2 + lật tới trang N + tick + Xác nhận | ~16s + lật trang |
| Xác nhận tạo + chờ | ~5s |
| **Cộng** (lật trung bình 3,5 trang vì trang reset mỗi ngày) | **~53s** |

| Cửa sổ ngày đỏ | 1 shop, đủ 8 khung | Khung 4h chứa |
|---|---|---|
| 15 ngày | 120 chương trình ≈ **1h47** | **2 shop** |
| 30 ngày | 240 chương trình ≈ **3h32** | **1 shop** |

### Quyết định của người dùng (27/08) — bản CHỐT

> ⚠ **Bản này THAY THẾ thiết kế "1 lượt/tháng, 2 shop mỗi lượt" ở đoạn trước.** Giữ lại đoạn đó ở dưới cùng
> mục này để biết vì sao đã bỏ, đừng lấy nó ra dùng lại.

**Lịch: MỖI NGÀY MỘT LƯỢT, khung CỨNG `21:00 → 23:59`.**

```
21:00 hàng ngày  (đang check đơn cũng DỪNG, y như KM/Combo)
        ↓
   mở Brave → SSO → shop ĐẦU TIÊN → trang Flash Sale → mở lịch
        ↓
   NGÀY MAI có phải NGÀY ĐỎ không?
        ├── KHÔNG → đóng cửa sổ, gọi lại check đơn lúc 22:00
        └── CÓ    → set flash sale MỌI shop cho ĐỦ 8 khung của NGÀY MAI
                        ↓
                    xong → gọi lại check đơn ngay
```

**Đọc lịch MỘT LẦN ở shop đầu tiên, không đọc lại ở từng shop.** Ngày nào có khung là lịch phát hành của
Shopee, không phải thuộc tính riêng của shop (thứ riêng từng shop là *số sp còn nhận được* trong mỗi khung).
Đọc 12 lần là 12 lượt chờ render cho cùng một câu trả lời.

### Vì sao nhánh KHÔNG đỏ phải chờ tới 22:00 — ĐỪNG rút xuống "gọi lại ngay"

Nhìn qua thì chờ 55 phút là phí: lượt đọc lịch chỉ tốn ~3–5 phút, gọi check đơn lại ngay có vẻ không mất gì.
**Sai, và đây là chỗ dễ "tối ưu" hỏng.**

Vòng check đơn **luôn bắt đầu lại từ shop 1**. Cắt ngang lúc nó mới đi được 1–2 shop rồi gọi lại sau 5 phút
⇒ đúng hai shop đó bị mở lại lần thứ hai **trong vòng vài phút**. Đó chính xác là hành vi đã làm Shopee bật
`verify/traffic/error` — ca thật ghi trong xmldoc của `DoDomKmSauPickerAsync`: *"dò lại CÙNG một shop nhiều
lần trong thời gian ngắn… 2 lần mở cùng shop trong 17 phút, đêm 18/08"*.

Mốc 22:00 làm hai nhánh cân nhau:

| Nhánh | Shop 1–2 mở lại sau bao lâu |
|---|---|
| CÓ đỏ | flash sale chạy tới ~23:09 ⇒ **~2 tiếng**, giãn sẵn, không cần làm gì |
| KHÔNG đỏ, gọi lại ngay | **~5 phút** ⇒ đúng mẫu bị chặn |
| KHÔNG đỏ, chờ 22:00 | **~1 tiếng** ⇒ cân với nhánh trên |

⇒ 22:00 là **biên an**, không phải thời gian chết.

### CÁI GIÁ LÊN CHECK ĐƠN — con số, đã đo bằng test (28/08/2026)

Khung Flash 21–24h kéo theo **biên an `[20:00, 24:00)`** (60 phút trước khung, y hai vòng kia). Hệ quả khi bật
công tắc `flash_bat` và máy này là máy chủ trì:

| | |
|---|---|
| Check đơn **KHÔNG bắt đầu vòng mới** trong | **20:00 → 24:00** |
| Mất mỗi đêm | **4 tiếng**, **MỌI đêm** (chu kỳ flash là 1 ngày, không phải 3) |
| Vòng đang chạy dở | **KHÔNG bị cắt** — chỉ dời điểm BẮT ĐẦU của vòng kế |
| Mốc bị dời tới | **00:00** hôm sau (giờ đóng khung) |

**Con số này KHÔNG đổi giữa đêm ĐỎ và đêm không đỏ** — quyết định nhường khung do lịch quyết, đặt ra TRƯỚC khi
biết ngày mai có đỏ hay không. Khác biệt nằm ở phần *trong* 4 tiếng đó:

- **đêm ĐỎ**: lượt flash chiếm hồ sơ Brave từ 21:00 tới ~23:09 (hoặc ~11 phút khi còn trần 1 shop), xong thì
  check đơn chạy lại ngay — nhưng vẫn nằm trong 4 tiếng đã nhường;
- **đêm KHÔNG đỏ**: lượt flash chỉ tốn ~5 phút rồi đóng cửa sổ, và check đơn chờ tới **22:00** mới chạy lại
  (biên an ở mục trên). Khoảng chờ ấy nằm gọn trong 4 tiếng, **không cộng thêm**.

So sánh: KM (02–04h) và Combo (06–08h) mỗi vòng ăn 3 tiếng nhưng **chỉ những đêm tới hạn** (mặc định 3 ngày một
lần). Flash ăn 4 tiếng **mỗi đêm** — đây là vòng đắt nhất về giờ check đơn, và người dùng phải biết trước khi
tick. Dòng chữ này có trên chính tab "Flash Sale" của form; con số 4 được khoá bằng
`FlashLichTuDongTests.BatFlash_ThiCheckDon_MatDung4TiengMoiDem`.

**KM/Combo KHÔNG còn liên quan.** Người dùng từng yêu cầu "combo và km chạy sau khi flash sale xong"; với
khung cứng 21–24h thì flash sale và KM/Combo (02–04h) **rời hẳn nhau**, yêu cầu đó tự thoả bằng đồng hồ.
⇒ Đề xuất nới `HanChotLuotTuDong` 04:00 → 06:00 của bản trước **BỎ**, không cần nữa.

Xong ~23:09 (2h09 + khởi động), dư gần một tiếng trước nửa đêm.

**Đặt cho NGÀY MAI, không phải hôm nay** (người dùng chốt 27/08 — *"shopee cho set trước mà"*). Hai cái được:

- **Đủ 8 khung** thay vì ~7. Chạy lúc 02:00 mà đặt cho hôm nay thì khung `02:00–09:00` đã bắt đầu — mất một
  khung mỗi ngày, và còn phải đoán xem Shopee có nhận một khung đang chạy dở hay không.
- **Hết ca nửa vời.** Mọi khung của ngày mai đều nằm ở tương lai ⇒ không có khung nào ở trạng thái mập mờ.

Giá: 8 × 12 = 96 chương trình ≈ **2h09** (thay vì ~2h00). Đêm ĐẦU TIÊN sau khi bật thì hôm đó không có flash
sale — chỉ xảy ra đúng một lần, không đáng làm gì thêm.

- **Thứ tự ưu tiên cứng: Flash Sale → KM/Combo → check đơn.**
- Mỗi đêm chỉ làm khung của **một ngày**, nhưng làm cho **đủ 12 shop**.
- Vẫn **đủ tất cả khung giờ** của ngày đó; **chỉ số trang quay lại 1** ở mỗi ngày.

### Vì sao bản này thắng bản "2 shop mỗi lượt, 1 lượt/tháng"

Bản cũ cho một shop ăn trọn một đêm ⇒ shop xếp sau chạy vào đêm thứ 6 thì cửa sổ đã co mất 5 ngày, và nếu
lượt rơi vào cuối tháng thì **mất trắng** (ca 27/08: chỉ còn 5 ngày). Người dùng chỉ ra đúng chỗ này.

Bản mới đảo trục: **NGÀY ở ngoài, SHOP ở trong.** Hết giờ thì mọi shop cùng dừng ở mức tương đương — không có
khái niệm "shop xếp sau". Cửa sổ co lại không còn hại ai vì mỗi đêm chỉ đụng tới đúng ngày hôm đó.

⇒ Hai chốt chống bỏ đói của bản cũ (**khởi động từ đầu tháng**, **xoay shop mở đầu**) **KHÔNG còn cần** — bỏ hẳn.

### Cái được LỚN NHẤT: không còn trạng thái nào phải lưu

Bản cũ phải nạp trước hàng nghìn chương trình nên kéo theo cả một tầng trạng thái: con trỏ *(shop → đã tới
ngày nào, khung nào)*, danh sách *shop đã xong trong tháng*, cơ chế chạy tiếp qua nhiều đêm, luật xoay shop.
Bản này **xoá sạch** cả tầng đó — việc của một đêm là **hàm THUẦN của ngày hôm đó**.

**Vì sao điều này quyết định chứ không phải tiện nghi:** hồ sơ đồng bộ xuyên máy và fleet có nhiều máy. Một
con trỏ tiến độ sẽ **trôi lệch** ngay khi một máy bỏ lỡ một đêm (tắt máy, mất mạng, thua khoá) — máy A tưởng
đã làm tới ngày 12, máy B tưởng mới tới ngày 9, và không ai biết ai đúng. Suy từ lịch thì mọi máy tự khớp
nhau mà không cần đồng bộ thêm một trường nào. Đây đúng lý do đã chốt cho `LoaiChuongTrinhCuaNgay` ở
`plans/2026-08-27-mot-khung-xoay-vong-theo-ngay.md` §B.

Thứ DUY NHẤT còn phải nhớ là **khung nào đã dùng TRONG lượt đang chạy** (để không chọn lại) — sống trong bộ
nhớ của lượt, KHÔNG ghi DB.

### Khối lượng — đính chính cách so

Đừng đọc "2.500/tháng so với 1.440/tháng" thành *đắt gấp đôi*. Bản cũ chỉ phủ ~15 ngày, bản mới phủ đủ 30
ngày; **mức phủ MỖI NGÀY là như nhau** (mọi shop, mọi khung). Khác biệt thật là trải đều ~2h mỗi đêm thay vì
dồn cục 6 đêm liền.

### Ngân sách một đêm

| | |
|---|---|
| Khung của một ngày | ~7 (khung 02–09h thường đã bắt đầu lúc chạy) |
| × 12 shop | 84 chương trình |
| × 53s | ~74 phút |
| + 11 kỳ nghỉ giữa shop | ~44 phút |
| **Cộng** | **~2 tiếng** — bắt đầu 02:00, xong ~04:00 |

Đây cũng là lời giải cho chi tiết trong dump: ngày 27 ghi *"7 khung giờ"* còn 28–31 ghi *"8 khung giờ"* — ngày
hôm nay luôn mất những khung đã trôi qua.

### PHẢI XONG TRƯỚC NỬA ĐÊM — và đây không phải chuyện cho gọn

Vắt qua 00:00 thì **chữ "ngày mai" đổi nghĩa giữa lượt**. Shop 1–8 chạy lúc 23:5x nhắm ngày `D+1`; shop 9–12
chạy lúc 00:0x, mà lúc đó "ngày mai" đã là `D+2`. Một lượt tạo flash sale cho HAI ngày khác nhau, còn ngày
`D+1` thì bốn shop cuối không có gì — **không lỗi, không log**.

Hai chốt, cần cả hai:

1. **Ngày đích tính MỘT LẦN lúc bắt đầu lượt** (`ngayBatDauLuot.AddDays(1)`), rót vào gói tham số dùng cho mọi
   shop. **Tuyệt đối không đọc lại đồng hồ trong vòng lặp.** Đây đúng bài học của `ChayVongTaoChuongTrinhAsync`:
   *"MỘT lượt đọc đồng hồ cho cả hai cửa thời gian — hai lượt đọc là hai câu trả lời khác nhau cho cùng một
   câu hỏi"*.
2. **Hạn chót bắt đầu shop mới = 23:45** (dùng lại `ConDuocBatDauShopMoi`). Một shop tốn ~11 phút (8 chương
   trình × 53s + mở shop), nên bắt đầu sau 23:45 là chắc chắn vắt qua nửa đêm. Còn shop chưa chạy thì ghi log
   nói rõ còn mấy shop — đêm mai lượt mới lại đi từ đầu danh sách.

Chốt 1 vẫn cần dù đã có chốt 2: chốt 2 chỉ chặn shop MỚI, không cắt shop đang chạy dở.

## 5. Tiêu chí nghiệm thu

- [ ] Chỉ chọn ngày có `div.timeslots` **và không** `disabled`; ngày `no-timeslot` không bao giờ được bấm.
- [ ] ~~Chỉ ngày thuộc **tháng hiện tại**~~ — **BỎ** (xem §3.4): luôn nhắm NGÀY MAI, rơi vào tháng nào cũng được.
- [ ] Khung đã dùng trong lượt ⇒ KHÔNG chọn lại (test: 3 khung, chạy 3 lượt, ra 3 giá trị `value` khác nhau).
- [ ] Chỉ số trang **reset về 1** khi sang ngày mới (test ma trận 2 ngày × 3 khung ⇒ dãy trang 1,2,3,1,2,3).
- [ ] Sau khi bấm sắp xếp, **đọc lại dãy Doanh số** và chấm bằng `dayDoanhSoKhongTangDan`; dãy TĂNG DẦN ⇒ HỎNG
      có tên, không tạo. (Xem §3.1 — đây là ca sai âm thầm nguy hiểm nhất của luồng này.)
- [ ] **Ca "mọi Doanh số = 0" phải ĐẠT bình thường** (không có nhánh riêng, không có câu "không kiểm chứng
      được"): dãy toàn bằng nhau thì không có cặp nào tăng ⇒ đạt. Có test cho ca này.
- [ ] **CỬA SẮP XẾP PHẢI FAIL-CLOSED: không đọc được ⇒ HỎNG.** Cửa nhận TRỌN gói đọc (`coCot`, `soDong`,
      `giaTri`, `loi`), không phải mỗi mảng giá trị. Bốn lối ra "không đọc được" của
      `pageDocDoanhSoMoiDongFlash` (chưa cài luật · không thấy modal · không thấy cột · bảng thân 0 dòng) đều
      phải chấm HỎNG — bản đầu cho ĐẠT hết bốn, và test khi ấy còn KHOÁ CỨNG hành vi sai đó.
- [ ] **THỬ PHÁ bắt buộc cho cửa sắp xếp, hai phép:** (1) đảo chiều luật (chấm dãy tăng dần là ĐẠT) ⇒ ma trận
      ĐỎ; (2) cho cửa trả `dat:true` khi `coCot:false` ⇒ lưới fail-closed ĐỎ.

      > ⚠ **Bỏ vế "kể cả ở ca bảng toàn 0 doanh số"** (đính chính 29/08/2026 — tiêu chí cũ BẤT KHẢ THI). Ở dãy
      > toàn bằng nhau thì **không có cặp nào tăng cũng không có cặp nào giảm**, nên đảo chiều luật KHÔNG đổi
      > kết quả của riêng ca đó: cả hai chiều đều cho ĐẠT, và đó là câu trả lời ĐÚNG (xem §3.1 — với dữ liệu
      > ấy tăng dần và giảm dần là cùng một thứ tự). Ca toàn 0 vẫn phải có test, nhưng nó khoá mệnh đề
      > *"không bị chấm hỏng oan"*, không phải mệnh đề *"phá được"*. Phép phá thật đi qua hai ca có chênh lệch
      > (giảm dần ⇒ đạt · tăng dần ⇒ hỏng) và qua cửa fail-closed.
- [ ] Đếm và ghi nhật ký số ngày đỏ + tổng khung ở lượt đầu mỗi shop.
- [ ] Nổ đúng **21:00 hàng ngày**; đang check đơn thì DỪNG check đơn trước, xong thì bật lại (đi đúng khung
      `ChayLatCatAsync` + `MuonHoSoAsync`/`TraHoSo` như KM/Combo, không chế đường mới).
- [ ] Ngày mai KHÔNG đỏ ⇒ đóng cửa sổ, ghi MỘT dòng nói vì sao, gọi lại check đơn **lúc 22:00** — mốc này là
      BIÊN AN chống mở lại shop 1–2 sau vài phút, KHÔNG được "tối ưu" thành gọi ngay (xem §4, có ca thật).
      Test: mốc gọi lại phải là 22:00 kể cả khi lượt đọc lịch xong lúc 21:04.
- [ ] Lịch đọc **MỘT LẦN** ở shop đầu, không đọc lại mỗi shop — grep chứng minh.
- [ ] **Ngày đích tính MỘT LẦN lúc bắt đầu lượt.** Test: đồng hồ giả nhảy qua nửa đêm giữa shop 8 và shop 9 ⇒
      **cả 12 shop vẫn nhắm CÙNG một ngày**. Đây là ca giết bản "đọc lại `DateTime.Now` mỗi shop".
- [ ] Không bắt đầu shop mới sau **23:45** (`ConDuocBatDauShopMoi`); còn shop chưa chạy ⇒ ghi rõ còn mấy shop.
- [ ] **KHÔNG có trạng thái nào lưu xuống DB** (không con trỏ ngày/khung, không danh sách shop đã xong tháng):
      grep chứng minh. Việc của một đêm phải là hàm thuần của ngày.
- [ ] Cột "Flash Sale" ở lưới tab Shops (đã dựng sẵn ở plan `2026-08-27-cot-km-combo-flashsale.md`) hiện được
      số sp — hết cảnh luôn `—`.
- [ ] Build sln + hub 0 warning; 4 bộ test xanh; **THỬ PHÁ** từng luật mới.

## 6. Rủi ro

1. **Một shop = tới 240 lượt bấm Tạo trong một đêm.** Combo chỉ 1 lượt/shop. Nhịp thao tác dày hơn hai bậc so
   với mọi thứ đã chạy — rủi ro `verify/traffic` cao hơn hẳn. Cần nghỉ giữa các chương trình, và lượt chạy
   thật đầu tiên nên giới hạn 1 shop để đo phản ứng.
2. **`ChoChang` chưa có hạn cho Flash Sale.** Một shop chạy 1h47 trong khi hạn chặng dài nhất hiện là 1800s
   (30 phút). Không thể gói cả shop vào MỘT chặng — phải cắt mỗi *chương trình* thành một chặng riêng.
3. Modal sản phẩm là `eds-*` chứ không `eds-react-*` — xem cảnh báo ở §2.
