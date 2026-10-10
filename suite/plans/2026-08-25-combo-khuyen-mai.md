# Plan: Combo Khuyến Mãi — chương trình thứ 2 của "KM của Shop"

- **Ngày:** 2026-08-25
- **Trạng thái:** chặng A **XONG** · chặng B **XONG** (`flow-combo.js` + `background.js` + nút "Chạy Combo")
  · **CHƯA ai chạy thật trên Shopee** — đang ở GIAI ĐOẠN THỬ
- **Hai công tắc giai đoạn thử** (`KmLichTuDong`): `ComboSanSang = true` · `TranShopLuotCombo = 2`
  (`0` = chạy full list). Xong thử thì đặt trần về `0`.
- **Chưa commit.**

> ⚠ **BẪY CỦA GIAI ĐOẠN THỬ — đọc trước khi để lịch tự động chạy.** Lượt bị trần cắt vẫn có `SoTao = 2 > 0`
> nên `ChotLuotCombo` VẪN ghi `ComboLanChayCuoi` ⇒ lịch đẩy đi `SoNgay` ngày ⇒ **10 shop còn lại không bao giờ
> được chạy chừng nào trần còn 2**. Đúng ý trong lúc thử; thành đường hỏng IM LẶNG nếu ai quên gỡ trần.
> Cố ý KHÔNG sửa: đổi luật ghi mốc là lật lại quyết định đã cân nhắc hai vòng ở `ChotLuotKm`. Gỡ trần là hết.
- **Người lập:** phiên chính · **Người thực thi:** `opus-dev`

## 1. Bối cảnh & mục tiêu

"KM của Shop" sẽ chạy **3 loại chương trình**. App hiện làm được **1** (Chương Trình Của Shop). Việc này làm
loại thứ **2 — Combo Khuyến Mãi**. Loại thứ 3 (Flash Sale) làm sau.

**Người dùng chốt 25/08/2026 — ba vòng RIÊNG BIỆT, mỗi 3 ngày một lần:**

| Khung giờ CHẠY | Vòng | Nghỉ sau đó |
|---|---|---|
| `02:00–04:00` | Chương Trình Của Shop (**thu lại từ 02–06h**) | `04:00–06:00` cho check đơn |
| `06:00–08:00` | **Combo Khuyến Mãi** ← việc này | `08:00–10:00` cho check đơn |
| `10:00–12:00` | Flash Sale (**chưa làm**, chỉ đặt chỗ khung) | — |

**Thông số combo** (mặc định; người dùng sửa được trên form, app gửi xuống ext để điền "like human"):

| Trường | Giá trị | Ô trên Shopee |
|---|---|---|
| Tên | `Combo <dd-MM-yyyy>` → `Combo 15-02-2027` | `input[name="name"]` — **`maxlength="25"`** |
| Ngày | hôm nay + `KmLichTuDong.SoNgayDatTruoc` (180) | 2 ô `eds-react-date-picker` |
| Giờ | `11:00` – `12:00` | `div[name="period.startTime"]` / `period.endTime` |
| Loại combo | Giảm giá theo **số tiền** | `input[type=radio][value="3"]` (2=%, 1=đặc biệt) |
| Mức ưu đãi | mua **2** sp giảm **2.000đ** | `tiers.0.minAmount` + ô số tiền (**tên field CHƯA BIẾT**) |
| Giới hạn đặt hàng | **3** | `input[name="usageLimit"]` |

⚠ **Tên chỉ được 25 ký tự** — khuôn KM hiện tại (`KM ngày 15-02-2027 từ 11:00 - 12:00`, 35 ký tự) KHÔNG nhét
vừa. Người dùng chọn `Combo 15-02-2027` (16 ký tự).

⚠ **Form combo dùng họ `eds-react-*`, KHÁC hẳn `eds-*` của luồng KM.** Không dùng lại được selector nào của
`flow-km.js` cho phần picker / bảng / modal. Đây là lý do chặng B bị chặn (xem §5).

## 2. Phạm vi

### Chặng A — KHÔNG phụ thuộc DOM (làm ngay)

- **Lịch 3 khung** trong `KmLichTuDong`: mỗi loại chương trình một khung riêng, biên an của check đơn tính
  theo TỪNG khung.
- **Hồ sơ cấu hình combo** trong `km_config` (cột mới + migration + đồng bộ hub).
- **Form nhập thông số combo** + luật kiểm đầu vào thuần.
- **`ThamSoCombo`** + đường gửi xuống extension (chưa có phía ext nhận).

### Chặng B — CHẶN, chờ 3 mảnh DOM người dùng dán

- `extensions/shopee-orders/flow-combo.js`: bấm "Tạo" ở thẻ **Combo Khuyến Mãi** → điền form → chọn sp →
  "Xác nhận" ở `footer button[type=submit].src-components---confirm--3svOo`.
- Nút DÒ DOM riêng cho combo.

### KHÔNG làm

- Flash Sale (chỉ đặt chỗ khung giờ, không code luồng).
- KHÔNG đụng logic đã chốt của Chương Trình Của Shop ngoài việc **thu khung 02–06h xuống 02–04h**.

## 3. Chặng A — các bước

### A1. `KmLichTuDong`: một khung → BA khung

Thêm `enum LoaiChuongTrinh { ChuongTrinhShop, Combo, FlashSale }` và bảng khung thuần:

| Loại | Mở | Đóng | Biên an của check đơn |
|---|---|---|---|
| `ChuongTrinhShop` | 2 | 4 | `[1, 4)` |
| `Combo` | 6 | 8 | `[5, 8)` |
| `FlashSale` | 10 | 12 | `[9, 12)` |

Luật biên an giữ NGUYÊN ý nghĩa cũ (**mở − 60 phút → đóng**): một vòng check đơn chạy 30–45 phút, đặt lúc
`01:5x` thì vẫn đang chạy khi KM tới giờ.

Đổi chữ ký (tất cả đều là hàm THUẦN, nhận `bayGio` từ ngoài — giữ nguyên luật đó):

- `TrongKhung(bayGio, loai)` · `HanChotLuotTuDong(bayGio, loai)` = giờ ĐÓNG của khung đó
- `TrongBienAn(moc)` → rơi vào **bất kỳ** khung nào trong ba biên an ⇒ true
- `HoanVongCheckDon(mocDuKien, nhuongKhung)` → dời tới giờ ĐÓNG của **đúng khung nó đâm vào** (4h/8h/12h),
  KHÔNG phải luôn luôn 6h như bản cũ
- `MocChayKe(cf, bayGio, loai)` · `DenGioChay(cf, bayGio, loai)` · `DaThuTrongKhungNay(cf, bayGio, loai)`
- `NenNhuongKhung(cf, mayNay, mocDuKien, bayGio)` → nhường nếu **bất kỳ** vòng nào đang bật tới hạn trong
  khung mà `mocDuKien` đâm vào

Giữ `SoNgayDatTruoc = 180` và `NgayChuongTrinh` dùng chung cho cả ba loại.

### A2. `km_config`: mốc chạy phải TÁCH THEO LOẠI

**Đây là chỗ dễ hỏng nhất, đọc kỹ.** `LanChayCuoi` hiện dùng chung. Nếu để nguyên: KM chạy 02:05 ngày D ghi
`LanChayCuoi = D 02:xx`; tới 06:05 combo hỏi `DenGioChay` → `bayGio >= cuoi.Date + 3 ngày + 6h` = `D+3 06:00`
→ **false** ⇒ **combo KHÔNG BAO GIỜ CHẠY**, im lặng. (`LanThuTuDong` may mắn thoát nhờ phép `TrongKhung`,
nhưng vẫn tách cho cùng một ngữ nghĩa.)

Thêm cột (theo đúng khuôn `EnsureColumn` sẵn có trong `Database.cs`, KHÔNG phá dữ liệu cũ):

| Cột | Kiểu | Mặc định | Nghĩa |
|---|---|---|---|
| `combo_bat` | INTEGER | 0 | bật vòng combo tự động (độc lập với `tu_dong` của KM) |
| `combo_gio_bat_dau` | TEXT | `'11:00'` | giờ bắt đầu chương trình combo |
| `combo_gio_ket_thuc` | TEXT | `'12:00'` | giờ kết thúc |
| `combo_loai` | INTEGER | 3 | 2=%, 3=số tiền, 1=đặc biệt |
| `combo_mua_sl` | INTEGER | 2 | mua N sp |
| `combo_giam` | INTEGER | 2000 | giảm (đồng nếu loại 3, % nếu loại 2) |
| `combo_gioi_han` | INTEGER | 3 | giới hạn đặt hàng |
| `combo_lan_chay_cuoi` | TEXT | NULL | mốc TẠO ĐƯỢC gần nhất của vòng combo |
| `combo_lan_thu_tu_dong` | TEXT | NULL | mốc vòng combo bắt đầu THỬ gần nhất |

`SoNgay` **dùng CHUNG** cho cả ba vòng (người dùng: "3 ngày chạy 1 lần, 3 vòng riêng biệt"). Đổi mặc định
`so_ngay` từ 7 → **3** (chỉ ảnh hưởng dòng MỚI; dòng cũ giữ nguyên giá trị người dùng đã đặt).

**Đồng bộ hub:** hai mốc combo gộp theo đúng luật MAX như hai mốc cũ; 7 trường cấu hình combo đi theo luật
"máy nào sửa sau thắng" (`cfg_updated_at`) y hệt các trường KM. KHÔNG đẻ luật gộp thứ hai.

### A3. Form nhập thông số combo

Dùng lại khuôn `KmDialogViewModel` (mọi luật là hàm thuần `static`). Thêm luật kiểm:

- Tên ≤ **25** ký tự (hằng riêng `TranDoDaiTenCombo`, KHÔNG dùng chung `TranDoDaiTen = 150`).
- `combo_mua_sl` ≥ 2 (mua 1 sp không thành combo).
- `combo_giam` ≥ 1; nếu `combo_loai == 2` (%) thì 1–99.
- `combo_gioi_han` ≥ 1.
- Giờ kết thúc > giờ bắt đầu, định dạng `HH:mm` — dùng lại `DocGio`.
- `SinhTenComboMacDinh(ngay)` = `"Combo " + ngay.ToString("dd-MM-yyyy")`, cắt về trần 25.

### A4. `ThamSoCombo` + đường gửi xuống extension

`record ThamSoCombo(string Ten, DateTime Ngay, string GioBatDau, string GioKetThuc, int Loai, int MuaSl,
int Giam, int GioiHan)`.

Gói JSON gửi xuống ext theo đúng khuôn `ThamSoKm` hiện có trong `OrdersBridgeSession` (`ngayTrongThang`,
`thang`, `nam`, `ngayDayDu` dạng `dd-MM-yyyy`, `InvariantCulture`) + 4 trường combo.

**Dựng gói MỘT LẦN rồi log in TỪ CHÍNH NÓ** — đúng bài học lỗ phủ ngày 25/08/2026 ở
`plans/2026-08-25-km-dat-truoc-180-ngay.md` (đổi ngày gửi xuống Shopee mà 216 test vẫn xanh). Đừng giữ biến
rời rồi log một đằng gửi một nẻo.

### A5. Vòng combo đi ĐÚNG khung phối hợp xuyên máy của KM (người dùng chốt 25/08/2026)

Vòng combo phải dùng **y nguyên** `ChayLatCatAsync` với `xinKhungXuyenMay: true` như `ChayKmAsync`, tức đủ
chuỗi: **nhờ hub DỪNG check đơn ở máy khác → tạm dừng phiên check đơn cục bộ → chờ dừng hẳn → xin khoá
`orders:<login>` → Brave sạch → SSO → lặp shop → xong thì NHẢ khung + chạy lại check đơn ở mọi máy đã dừng.**

⚠ **Thứ tự ba bước là BẮT BUỘC, đừng đảo** — khối chú thích "ĐỪNG SỬA" trong `ChayLatCatAsync` đã ghi ca hỏng
thật (một khoá, hai chủ, không đếm chủ). Cách rẻ và đúng nhất: **gọi lại chính `ChayLatCatAsync`**, chỉ thay
phần `than`, KHÔNG viết đường phóng trình duyệt thứ hai cho combo.

`hanChot` của lượt combo = `HanChotLuotTuDong(bayGio, Combo)` = **08:00**.

## 4. Tiêu chí nghiệm thu (chặng A)

- [ ] `TrongKhung(03:00, ChuongTrinhShop)` = true; `TrongKhung(05:00, ChuongTrinhShop)` = **false** (khung đã
      thu còn 2–4h); `TrongKhung(07:00, Combo)` = true; `TrongKhung(11:00, FlashSale)` = true. Ma trận đủ 24 giờ
      × 3 loại.
- [ ] `HoanVongCheckDon`: mốc `01:30` → `04:00`; `05:30` → `08:00`; `09:30` → `12:00`; mốc `04:30` và `08:30`
      và `13:00` → **GIỮ NGUYÊN** (khe nghỉ của check đơn).
- [ ] **Ca hỏng §A2 có test khoá**: KM chạy 02:05 ngày D ghi mốc xong, `DenGioChay(cf, D 06:05, Combo)` vẫn
      phải = **true**. Test này phải ĐỎ nếu ai đó gộp mốc lại làm một.
- [ ] DB cũ (đã có `km_config` không có cột combo) mở lên KHÔNG ném, đọc ra mặc định đúng bảng §A2.
- [ ] Gộp hub: mốc combo mới hơn thắng mốc cũ; mốc cũ KHÔNG kéo lùi mốc mới; 7 trường cấu hình combo theo
      `cfg_updated_at`.
- [ ] `SinhTenComboMacDinh(2027-02-15)` = `"Combo 15-02-2027"`, dài 16 ≤ 25.
- [ ] Ma trận `KiemTraFormCombo`: từng vế sai → đúng câu lỗi của vế đó; hợp lệ → null.
- [ ] `dotnet build ShopeeSuite.sln` — **0 warning, 0 error**; toàn bộ `XuLyDonShopee.Tests` xanh.
- [ ] Vòng combo đi qua ĐÚNG `ChayLatCatAsync` với `xinKhungXuyenMay: true` — có test khoá CHỖ GỌI (cho hook
      khoá xuyên máy trả "máy khác đang giữ" ⇒ lượt dừng ở cửa, KHÔNG phóng trình duyệt, và có dòng log).
- [ ] `HanChotLuotTuDong(bayGio, Combo)` = **08:00** (KM = 04:00, FlashSale = 12:00).
- [ ] **THỬ PHÁ**: mỗi test mới phải ĐỎ khi sửa ngược code — làm và ghi lại danh sách.

## 5b. DOM ĐÃ CHỐT (người dùng dán 25/08/2026) — selector cho chặng B

> Chép từ DOM THẬT, không đoán. Mọi truy vấn phải SCOPE trong popup/modal đang hiện.

### Popup ngày+giờ — `.eds-react-date-picker__popup`

| Việc | Selector | Bẫy |
|---|---|---|
| Popup đang mở | `.eds-react-popover.eds-react-date-picker__popup` | Nằm NGOÀI form (cuối `<body>`) |
| Đọc tháng/năm | `.date-box .date-default-style.month` (`"Th08"`) + `.year` (`"2026"`) | **Hai thẻ RỜI** ⇒ không dính vào ca `"Tháng 82026"` của `flow-km.js`. Tháng phải bóc số từ `"Th08"` |
| Lùi tháng | `.eds-react-date-picker__header .btn-arrow-default:not(.double)` chứa `.icon-arrow-left` | 4 nút mũi tên; 2 nút `.double` là **CHUYỂN NĂM** — bấm nhầm nhảy 12 tháng |
| Tiến tháng | `.btn-arrow-default:not(.double)` chứa `.icon-arrow-right` | " |
| Ô ngày | `.eds-react-date-picker__table-row .eds-react-date-picker__table-cell` | ⚠ **Ô TIÊU ĐỀ THỨ ("CN", "T2"…) MANG ĐÚNG CÙNG CLASS** — chúng nằm trong `__table-header`. **Bắt buộc scope trong `__table-row`**, đúng ca đã giết một đêm ở `flow-km.js` |
| Loại ô ngày | bỏ `.disabled` và `.out-of-range` | `out-of-range` = ngày của tháng liền kề, **KHÔNG có `disabled`** ⇒ bấm được, và bấm là đặt SAI THÁNG |
| Cột giờ | `ul.time-list` **thứ 0** → `li > .time-box`, nhãn 2 chữ số (`"09"`) | |
| Cột phút | `ul.time-list` **thứ 1** → `li > .time-box` | |
| Ô đang chọn | `.time-box.selected` | Dùng để đọc lại |
| Chốt | `.eds-react-date-picker__btn-wrap button` (chữ "Xác nhận") | |

**Trần 180 ngày lộ ra ở đâu:** ngày quá khứ mang `disabled` (thấy rõ: 01→24/08 đều `disabled`, 25 là
`today selected`). Ngày vượt trần sẽ mang đúng cơ chế đó ⇒ bước đặt thời gian ĐỎ với câu "không thấy ô ngày".

### Modal "Chọn Sản Phẩm" — `.eds-react-modal__container`

| Việc | Selector | Bẫy |
|---|---|---|
| Neo modal | `.eds-react-modal__content--x-large` + `.eds-react-modal__title` = `"Chọn Sản Phẩm"` | |
| Chọn TẤT CẢ trang | `.eds-react-table-header thead th.eds-react-table-selection-column input.eds-react-checkbox__input` | ⚠ Có checkbox **"Xem sản phẩm có sẵn"** ở vùng `actions` (đang `checked`) — nó là BỘ LỌC, tick nhầm là đổi tập sản phẩm |
| Dòng sản phẩm | `tbody.eds-react-table-tbody tr[data-row-key]` | ⚠ Có `tr.eds-react-table-measure-row` cao 0px, **không có `data-row-key`** ⇒ lọc bằng `[data-row-key]`, đừng đếm `tr` |
| Checkbox một dòng | `td.eds-react-table-selection-column input.eds-react-checkbox__input` | |
| Trang hiện tại | `li.eds-react-pagination-pager__page.active` | |
| Các trang | `li.eds-react-pagination-pager__page` — bỏ `.eds-react-pagination-pager__dot` | Pager có `…` ⇒ **KHÔNG đọc trước được tổng số trang**, y hệt KM ⇒ vẫn cần trần + cảnh báo khi chạm trần |
| Trang sau | `button.eds-react-pagination-pager__button-next` (hết trang thì có `.disabled`) | |
| Xác nhận modal | `.eds-react-modal__footer button.eds-react-button--primary` | |

**Khác KM — nhẹ hơn một bước:** combo KHÔNG có "Cập nhật hàng loạt"/điền giá từng sản phẩm (mức giảm đặt ở
cấp combo). Bảng đo được **10 sản phẩm/trang**, khớp số đo của KM.

### Thẻ "Combo Khuyến Mãi" ở trang Khuyến Mãi

`.src-components---createDiscountList--1PwpY .src-components---card--XK9_s`, khớp bằng **chữ tiêu đề**
`.src-components---cardTitle--3gs6L` = `"Combo Khuyến Mãi"`, rồi bấm
`.src-components---actionRow--3_dWQ button`.
⚠ **TUYỆT ĐỐI KHÔNG lấy theo chỉ số cột.** Ba thẻ cùng markup: "Chương Trình Của Shop" · "Combo Khuyến Mãi" ·
"Mua Kèm Deal Sốc". Shopee đổi thứ tự là tạo nhầm loại chương trình THẬT.

### Form combo

`input[name="name"]` (maxlength 25) · `div[name="period.startTime"]` / `div[name="period.endTime"]` (bấm vào
`.text-box input` để bung popup) · `input[type=radio][value="3"]` · `input[name="tiers.0.minAmount"]` ·
`input[name="usageLimit"]` · nút thêm sp `.src-components---addItems--2wv99` · chốt
`footer button[type=submit].src-components---confirm--3svOo`.

## 5. Chặng B — CHẶN: DOM còn thiếu

Không code đoán mò. Bài học `plans/2026-08-18-km-cua-shop.md` §3: *"Bước đầu tiên KHÔNG phải là code luồng, mà
là một lát cắt DÒ DOM. Làm ngược lại là viết 500 dòng selector đoán mò rồi sửa mù."*

**✅ ĐỦ DOM — chặng B KHÔNG còn bị chặn** (người dùng dán nốt mảnh cuối 25/08/2026).

### Hàng "Mức ưu đãi" — HAI BIẾN THỂ, KHÁC NHAU HẲN

| | Loại 2 — `%` | **Loại 3 — SỐ TIỀN (ta dùng)** |
|---|---|---|
| `name` ô giảm | `tiers.0.discountPercentage` | **`tiers.0.discountValue`** |
| Ký hiệu tiền | **hậu tố** `.eds-react-input__suffix` = `%GIẢM` | **TIỀN TỐ** `.eds-react-input__prefix` = `₫` |
| Class bọc | `…discountInput--1hykQ` | **`currency-input`** |
| `maxlength` | (không có) | **13** |
| `type` | `text` | `text` |

Ô số lượng KHÔNG đổi giữa hai biến thể: `input[name="tiers.0.minAmount"]`, `type="numeric"`,
`precision="0"`, `maxlength="4"`.

### ⚠ LUẬT BẮT BUỘC cho `flow-combo.js` — ĐỌC LẠI TRƯỚC KHI GÕ SỐ TIỀN

Sau khi click radio `value="3"`, **PHẢI xác nhận `input[name="tiers.0.discountValue"]` TỒN TẠI rồi mới điền.**
Nếu chỉ thấy `tiers.0.discountPercentage` ⇒ cú click radio KHÔNG ăn (Vue/React chưa vẽ lại) ⇒ **bước ĐỎ, TUYỆT
ĐỐI KHÔNG gõ tiếp**. Gõ `2000` vào ô `discountPercentage` là khai báo **giảm 2000%** — và đây đúng là hình dạng
lỗi mà cả `flow-km.js` lẫn plan gốc đã cảnh báo: điền vào ô sai rồi vẫn bấm được "Xác nhận".

Hai biến thể phân biệt được bằng BA dấu hiệu độc lập (`name` · tiền tố `₫` vs hậu tố `%GIẢM` · class
`currency-input`) ⇒ dùng `name` làm cửa chặn, hai dấu hiệu kia làm phép đọc lại.

## 5c. Phản biện chặng A (25/08/2026) — 8 phát hiện, DANH SÁCH VIỆC PHẢI SỬA

### NẶNG-1 · Form ship một CÔNG TẮC ĐÃ LÊN ĐẠN cho luồng chưa tồn tại

`KmDialog.xaml` hiện ô **"Combo Khuyến Mãi — chạy tự động (khung 6-8h sáng)"** bình thường, **không một chữ**
nói luồng chưa làm được. Người dùng tick → 06:0x hôm sau `NhipLichComboAsync` nổ → hub **DỪNG check đơn ở MỌI
máy khác** (deadline 90 phút) → Brave + SSO → mỗi shop gửi `{action:"chayCombo"}` → `background.js` **không có
nhánh nào nhận, cũng không có `default`** → C# chờ hết `ChoChang.KmChay = 900s` → HỎNG → nghỉ 3–5' → shop kế.
Tới 08:00: **~6–7 shop bị mở thật, ~2 tiếng check đơn chết, 0 chương trình được tạo.**

**Và nó KHÔNG dừng ở một đêm.** `ChotLuotCombo` chỉ ghi `ComboLanChayCuoi` khi `SoTao > 0` — ở đây luôn 0 ⇒
`DenGioChay` đi nhánh "chưa chạy lần nào ⇒ true" ⇒ **tới hạn LẠI mỗi sáng, không phải mỗi 3 ngày, vĩnh viễn.**
Đúng nguyên văn ca đã ghi ở `KmLichTuDong.SoNgayDatTruoc`: *"nguyên nhân KHÔNG BAO GIỜ TỰ HẾT… đúng profile
lưu lượng làm Shopee bật `verify/traffic`"*.

⇒ **SỬA:** một hằng `ComboSanSang = false` ở ĐÚNG MỘT CHỖ, gác **cả hai** cửa — ẩn/khoá ô tick trên form
**và** `NhipLichComboAsync` trả ngay kèm đúng một dòng log mỗi ngày. Chặng B xong thì lật một dòng.
⚠ **Cách sửa SAI:** ghi `ComboLanChayCuoi` cho lượt 0-shop — mâu thuẫn luật đã chốt của `ChotLuotKm`.

### VỪA-2 · `action:"chayCombo"` + gói tham số KHÔNG có test nào khoá CHỖ GỌI

`grep` cả repo: `ChayComboSauPickerAsync` · `ChayComboMoiShopAsync` · chuỗi `"chayCombo"` — **0 lần trong test**.
Đối xứng: KM có 9 test rig lái cầu nối THẬT, khẳng định chuỗi lệnh ĐÚNG BẰNG `[readShopList, openShopDetail,
chayKm]` + từng trường tham số.

Đột biến để kiểm (dự đoán: **4 bộ test vẫn XANH**): đổi `action: "chayCombo"` → `"chayKm"`, và
`goiThamSo: thamSo.Goi()` → `new { }`. Đột biến thứ nhất biến lượt combo 6h sáng thành lượt **tạo Chương Trình
Của Shop THẬT** trên mọi shop — chỉ lộ ra khi chặng B lên, tức khi nó đã tạo khuyến mãi thật.
⇒ **SỬA:** test rig combo theo đúng khuôn `KmVongRigTests`.

### VỪA-3 · Câu tổng kết lượt COMBO nói SAI giờ khung và SAI tên việc

`CauTongKetKm` in `KmLichTuDong.GioDongKhung` — hằng của **Chương Trình Của Shop = 4**; `CauTrangThaiKm`
hard-code `"KM của Shop"`. ⇒ lượt combo chạm hạn chót 08:00 ghi: *"Combo Khuyến Mãi xong… DỪNG vì hết khung tự
động (**4:00**)"* và trạng thái *"**KM của Shop**: hết khung…"*. Người trực 8h sáng đi tìm một mốc 4:00 không
tồn tại. ⇒ **SỬA:** truyền `LoaiChuongTrinh` vào, đừng đọc hằng của vòng kia.

### VỪA-4 · Cửa sổ rollout CLIENT CŨ reset trọn cấu hình combo, và log không hề nhắc tới combo

Vá "hub cũ" của chặng A **kín** (đã soi đủ 7 trường, không đường nào cho rỗng/0 xuống). Cái hở là **client cũ**:
máy B chưa cập nhật Velopack (JSON không có trường `combo*`) chỉ đổi `%` lúc 10:05 ⇒ mốc mới hơn ⇒ hub lấy
TRỌN gói của B ⇒ `combo_bat` về 0, `combo_giam` về mặc định ⇒ máy A kéo về 60s sau, **combo tắt câm**. Dòng log
duy nhất không có một chữ nào về combo. ⇒ **SỬA (tối thiểu):** thêm vế combo vào `CauDoiCauHinh` để lượt reset
không im lặng. (Tách nhóm gộp riêng cho `combo*` là đổi hợp đồng — ngoài phạm vi chặng A.)

### NHẸ (5→8)

5. `KmProfileItem.SoNgay = 7` và cột hub `so_ngay DEFAULT 7` đã lệch `MacDinhSoNgay = 3`. Phản biện **không
   dựng được ca hỏng** — chỉ là mặc định trôi lệch. Sửa cho khỏi đẻ nợ.
6. Khối chú thích 5 dòng **nhân đôi** ở `Database.cs:252` và `:258`.
7. `ChoKhungXuyenMayAsync` hard-code `"KM"`/`"đêm nay"` ⇒ lượt combo 06:05 ghi *"BỎ lượt KM **đêm nay**"*. Tệ
   hơn: `ComboKhuyenMaiTests:405` **assert đúng chuỗi sai đó** — test đang đóng đinh câu chữ sai.
8. `ThamSoCombo.MoTaUuDai()` dùng `"N0"` + `InvariantCulture` ⇒ log tiếng Việt in `"giảm 2,000đ"`.

### Đã soi và SẠCH (đừng soi lại)

- **Refactor `ChayKmSauPickerAsync` → `ChayVongTaoChuongTrinhAsync`** — thứ tôi lo nhất. Đối chiếu từng nhánh
  với 9 test rig: `ConDuocBatDauShopMoi` trước MỖI shop kể cả shop đầu · `SoShopChuaChay` đúng ở **cả bốn** lối
  ra sớm · captcha hai nguồn · shop hỏng ghi rồi đi tiếp · `token = shopId` · `nghiGiuaShop` chỉ khi còn shop ·
  gói KM giữ nguyên 7 trường. **Không lệnh nào rơi, không quyết định nào đảo.**
- **Ba khung + biên an**: quét đủ 0h→23h. Ba biên an `[1,4) [5,8) [9,12)` rời nhau; không mốc nào bị dời vào
  giữa một khung khác; không mốc nào rơi vào biên an mà thoát.
- **`VongBat(FlashSale)` cứng false**: truy hết mọi cửa — không đường nào nổ vòng FlashSale.
- **Hai nhịp một đồng hồ**: `_bridgeRunning` + `_dangChayKmTuDong` phủ kín, không dựng được ca chồng.
- **`EnsureColumn` 9 cột trên DB cũ có dữ liệu thật**: hợp lệ, đã có test phủ.
- **37 test `ComboKhuyenMaiTests`**: có vế đối chứng, không đo rỗng; lỗ phủ duy nhất là VỪA-2 ở trên.

### Còn TREO: `KmDialog.xaml` đỏ CHẬP CHỜN ngay sau `-t:Rebuild` (25/08/2026)

Nghiệm thu ghi nhận 2 test đỏ ở lượt chạy `XuLyDonShopee.Tests` **ngay sau `-t:Rebuild`**, chạy lại ngay thì
xanh:

```
System.IO.Packaging.PackagePart.GetStream(FileMode, FileAccess)
  at XuLyDonShopee.App.Views.KmDialog.InitializeComponent() ... KmDialog.xaml:line 1
  at KmNhanChuTriVaSoDungCapTests.NutNhanChuTri_HienThat_TrenFormThat()
```

**KHÔNG tái hiện được** ở lượt vá này: `dotnet build ShopeeSuite.sln -t:Rebuild` rồi
`dotnet test orders/XuLyDonShopee.Tests` ngay sau đó → **2138/2138 xanh**, kể cả test WPF mới thêm
(`OTickCombo_KhoaKhiLuongChuaSanSang_TrenFormThat`, dựng đúng `KmDialog` đó). Dừng điều tra tại đây theo đúng
phạm vi được giao — ghi lại để lượt sau không mất công dựng lại bối cảnh.

- **Cách tái hiện cần thử tiếp:** `-t:Rebuild` **rồi chạy CẢ BỘ** (không filter), lặp vài lượt. Đỏ chỉ xuất
  hiện ở lượt đầu sau rebuild ⇒ nghi lượt đọc BAML **lần đầu** của assembly vừa ghi xong.
- **Hai hướng đáng nghi, chưa loại trừ được cái nào:** (a) xunit chạy các lớp SONG SONG, hai lớp cùng chạm
  `KmDialog` lần đầu ⇒ hai lượt mở cùng một `PackagePart` của resource `.g.resources`; (b) file `.dll`/`.pdb`
  vừa ghi còn bị quét (Defender / Application Control) nên lượt mở stream đầu tiên trượt.
- **Bằng chứng ủng hộ (b), gặp NGAY trong lượt vá này:** cùng máy, cùng lượt, `Shopee.Hub.Web.Tests` bị
  `Could not load file or assembly 'Shopee.Hub.Web.dll'. **An Application Control policy has blocked this
  file.** (0x800711C7)` — chỉ với bản DLL vừa build lại; bản trước đó (đã "quen") chạy 204/204 bình thường.
  Tức máy này CÓ một chính sách chặn nhị phân mới, và nó cắn không đều tay. Đây là hạ tầng máy, không phải
  `KmDialog.xaml`.
- ⚠ Nếu lượt sau tái hiện được: **đừng sửa XAML trước.** Đo (a) bằng cách ép hai lớp WPF vào cùng một
  `[Collection]` (khuôn `TenCollection`) rồi chạy lại — hết đỏ tức là (a).

## 5d. Chặng B — HAI CÁI BẪY PHẢI BIẾT TRƯỚC KHI VIẾT DÒNG NÀO

### B-BẪY-1 · React NUỐT lượt gán `value` — cách của `flow-km.js` KHÔNG dùng lại được

`flow-km.js` điền ô nhập theo khuôn Vue: `el.value = x` rồi `dispatchEvent(new Event('input',{bubbles:true}))`.
**Với form combo (`eds-react-*` = React) khuôn đó KHÔNG ăn.** React gắn `_valueTracker` lên mỗi input; gán
thẳng `.value` cập nhật luôn giá trị cache của tracker, nên lượt `input` bắn ra bị coi là "không có gì đổi" và
state của React giữ nguyên RỖNG. Hậu quả đúng hình dạng tệ nhất: **ô hiện đúng số trên màn hình, nhưng submit
đi giá trị rỗng.**

Phải đi qua setter gốc của prototype để vượt tracker:

```js
const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value").set;
setter.call(el, String(giaTri));
el.dispatchEvent(new Event("input",  { bubbles: true }));
el.dispatchEvent(new Event("change", { bubbles: true }));
```

Áp cho **mọi** ô của form combo: `name` · `tiers.0.minAmount` · `tiers.0.discountValue` · `usageLimit`.
**Và phải ĐỌC LẠI `el.value` sau mỗi ô** — đó là thước duy nhất phân biệt "React đã nhận" với "chỉ vẽ lên DOM".

### B-BẪY-2 · `background.js` KHÔNG có nhánh `default` ⇒ lệnh lạ treo 900 giây

`handleCommand` là một `switch` không có `default`: action không khớp case nào thì hàm **return im lặng**, C#
chờ hết `ChoChang.KmChay = 900s` rồi mới kết luận HỎNG. Đây chính là cơ chế biến "quên đăng ký lệnh" thành
"~6–7 shop bị mở thật, 2 tiếng check đơn chết" ở ca NẶNG-1 §5c.

⇒ Thêm `default:` trả lời NGAY một gói lỗi kiểu `"lệnh không nhận diện được: <action>"`. Đổi một lượt treo 15
phút thành một bước ĐỎ tức thì. **Việc này làm kể cả khi `chayCombo` đã đăng ký** — nó là lưới cho MỌI lệnh
tương lai.

### Dùng lại gì của `flow-km.js`, và KHÔNG được đụng gì

- **DÙNG LẠI (export thêm, KHÔNG sửa hành vi):** đường điều hướng tới trang Khuyến Mãi
  (`li.ps_menu_marketing a[href="/portal/marketing/list/discount"]`) · khung `buoc()` (ghi bước + đọc lại +
  bắt captcha) · tiện ích `gon`/`hien`.
- **TUYỆT ĐỐI KHÔNG dùng lại:** mọi selector của picker, bảng, modal — `eds-*` (Vue) ≠ `eds-react-*` (React).
- **TUYỆT ĐỐI KHÔNG sửa** thân các hàm của luồng KM đang chạy thật. Chỉ được THÊM `export`.

### Thứ tự bước của `flow-combo.js`

0. Kiểm tham số (khuôn `docThamSoKm`) — thiếu/sai trường nào thì hỏng NGAY ở "bước 0", gọi đích danh tên trường.
1. Tới trang Khuyến Mãi (dùng lại đường của KM).
2. Bấm "Tạo" ở thẻ **khớp theo CHỮ TIÊU ĐỀ** `"Combo Khuyến Mãi"` — không theo chỉ số cột.
3. Điền tên (≤ 25) — setter gốc + đọc lại.
4. Ô thời gian BẮT ĐẦU: mở picker → chuyển tháng → click ngày → giờ → phút → "Xác nhận" → **đọc lại chuỗi đầy
   đủ `HH:mm dd-MM-yyyy`**.
5. Ô thời gian KẾT THÚC — y vậy.
6. Tick radio `value="3"` → **XÁC NHẬN `input[name="tiers.0.discountValue"]` TỒN TẠI** rồi mới đi tiếp (xem §5).
7. `tiers.0.minAmount` = MuaSl · `tiers.0.discountValue` = Giam — setter gốc + đọc lại từng ô.
8. `usageLimit` = GioiHan — setter gốc + đọc lại.
9. Modal sản phẩm: "Thêm sản phẩm" → chọn-tất-cả từng trang → sang trang → "Xác nhận". Trần trang + cảnh báo
   khi chạm trần (y luật của KM). Đếm dòng bằng `tr[data-row-key]`, KHÔNG đếm `tr`.
10. Bấm `footer button[type=submit].src-components---confirm--3svOo` → đọc lại URL đã rời trang tạo chưa.

**MỘT BƯỚC CỐT LÕI HỎNG ⇒ DỪNG SHOP NGAY**, không đi tiếp tới bước 10 — y luật của `flow-km.js`.

## 6. Rủi ro & lưu ý

1. **Khung 10–12h là GIỮA BAN NGÀY.** Toàn bộ thiết kế biên an sinh ra từ giả định "KM chỉ chạy 2–6h đêm".
   Ba khung này cắt check đơn 6 tiếng mỗi ngày chạy, 2 tiếng trong đó rơi vào giờ làm việc. Người dùng đã chốt
   — ghi ra đây để sau này không ai tưởng là sót.
2. **Khe check đơn chỉ còn 1 tiếng để BẮT ĐẦU** (`[4,5)` và `[8,9)`), vì biên an ăn 60 phút trước mỗi khung.
   Vòng dài 30–45' nên vẫn kịp, nhưng biên đã hẹp — đừng nới khung chương trình mà không tính lại chỗ này.
3. **Ô tên 25 ký tự** là trần CỨNG của Shopee. Gửi dài hơn là mất đuôi âm thầm (đúng bài học `maxlength=150`
   của KM).
4. **`eds-react-*` ≠ `eds-*`.** Đừng chép selector từ `flow-km.js` sang.
5. Rủi ro trần 180 ngày của `SoNgayDatTruoc` áp cho combo y hệt KM — xem plan KM +180 §5.

## 7. LƯỢT CHẠY THẬT ĐẦU TIÊN — 26/08/2026, 12 shop (21:45 → 22:53)

Luồng ĐI ĐƯỢC: 11/12 shop tạo được combo, **5.446 sản phẩm** vào chương trình. Nhưng lượt chạy làm lộ 4 lỗi mà
không lượt dò nào lộ ra được — vì cả bốn chỉ xuất hiện khi bảng React của modal render **KHÔNG ĐỀU** dưới tải thật.

| Shop | SP | Cảnh báo |
|---|---|---|
| Piko | 658 | 3 trang tick hụt |
| Delica | 573 | 3 |
| Hanily | 597 | 8 |
| **Minoa** | 640 | **39** · chạm trần 105 trang |
| Onicom | 693 | 10 |
| Rudi | 624 | 13 |
| **Cicily** | 780 | **27** · chạm trần 105 trang |
| Bizly | 766 | 16 |
| Sully | 50 | **dừng ở trang 6/7** — "bảng không đổi sau khi bấm sang trang 7" |
| Shop9x | — | **HỎNG**: "modal mở nhưng bảng 0 dòng sau 45s" |

Dấu hiệu nhất quán: cảnh báo tick hụt **bắt đầu từ trang 6–12 trở đi**, không phải trang đầu — trang đầu đã tải
sẵn lúc mở modal, trang sâu phải nạp lại. Minoa duyệt 105 trang × 10 dòng ≈ 1.050 dòng mà chỉ 640 sản phẩm vào
combo ⇒ **sót ~400 sản phẩm**. Người dùng chẩn đoán đúng gốc: *"tick nhanh quá, modal chưa kịp load dữ liệu"*.

### Bốn lỗi và cách vá (tất cả nằm trong `extensions/shopee-orders/flow-combo.js`, bước 9)

| # | Lỗi | Cách vá |
|---|---|---|
| 1 | Tick theo **ngân sách CHỜ CỐ ĐỊNH** (`sleep(900)`) trên bảng render không đều | `choBangLang()` — đọc số dòng + 3 khoá dòng đầu mỗi 300ms, chỉ tick khi HAI lượt liên tiếp bằng nhau (trần 8s). Máy nhanh không phải trả 105 × 0,9s; máy chậm được chờ đủ |
| 2 | Cú "chọn tất cả" chỉ phủ những dòng ĐANG có lúc bấm | `pageTickDongConSot()` — tick RIÊNG từng dòng còn sót, ≤3 lượt, **mỗi lượt chờ bảng LẮNG LẠI rồi mới đếm** (cửa sổ yên 900ms). Chỉ cảnh báo khi bù xong vẫn thiếu. ⚠ TUYỆT ĐỐI không bù bằng cách bấm lại ô "chọn tất cả" — nó là CÔNG TẮC, bấm lần hai là bỏ tick sạch cả trang |
| 3 | Chờ đổi trang 6s rồi bỏ cuộc (ca Sully) | Ngân sách 12s/lượt; hết lượt đầu thì **bấm lại nút sang trang MỘT lần** rồi chờ tiếp. Chỉ bấm lại khi ĐỌC ĐƯỢC nhãn trang active và nhãn KHÔNG đổi — pager đã nhảy (hoặc không đọc được nhãn) thì CHỜ THÊM, vì bấm lại lên một cú đã ăn là nhảy cóc mất một trang trong im lặng |
| 4 | Bảng 0 dòng ⇒ luôn chấm HỎNG (ca Shop9x: shop 1 SP, SP đó đã nằm trong combo 16:51 nên Shopee loại khỏi modal ⇒ modal rỗng THẬT) | Kết luận "không còn sản phẩm để thêm" ⇒ `boQua` khi **HAI dấu độc lập cùng đồng ý**: `tr[data-row-key]` = 0 **VÀ** (bảng mang class `eds-react-table-empty` **HOẶC** `tr.eds-react-table-placeholder` **ĐANG HIỆN**). ⚠ Placeholder **TỒN TẠI CẢ KHI CÓ HÀNG**, chỉ bị ẩn — dựa vào sự tồn tại là bỏ oan mọi shop. Dấu lệch nhau ⇒ giữ nguyên HỎNG |

Lưới cuối kèm theo (rig phát hiện, không có trong yêu cầu ban đầu): học **chuẩn số dòng/trang** = số dòng nhiều
nhất từng thấy trong chính modal đó; trang nào đứng yên mà THIẾU so với chuẩn thì chờ thêm ≤3s, và cuối vòng
trang nào (trừ trang cuối) vẫn dưới chuẩn thì đẻ cảnh báo `trang-thieu-dong` — mất hàng phải ỒN ÀO.

### Kiểm chứng (26/08/2026)

- Repo KHÔNG có hạ tầng test JS ⇒ dựng rig tạm trong scratchpad (KHÔNG thêm vào repo): (a) 13 kịch bản chạy TRỌN
  `chayComboChoTabDangMo` trên một mô hình trang giả với ĐỒNG HỒ ẢO; (b) 21 khẳng định chạy THÂN THẬT của
  `pageTickDongConSot` / `pageDoModalSanPham` / `pageDemDaTick` trên một mini-DOM tự viết. Cả hai xanh.
- Thử phá: 11 đột biến, **10 làm rig ĐỎ**. Cái duy nhất không đỏ là bóp `CHO_DOI_TRANG_TRAN_NHIP` 40→20 (tổng
  ngân sách vẫn 12s nhờ lượt chờ thứ hai); bóp tiếp xuống 10 (tổng 6s = đúng bản trước) thì ĐỎ.
- `dotnet build ShopeeSuite.sln` 0 lỗi 0 warning; 4 bộ test: 2190 · 217 · 68 · 204 = **2.679 test xanh**.
- **CHƯA đo được**: 3 phép đo chỉ có trên Shopee thật — (i) class `eds-react-table-empty` có xuất hiện đúng lúc
  modal rỗng của luồng combo không (mẫu DOM người dùng dán là của FORM, không phải MODAL); (ii) một dòng của
  bảng `has-fix-left` có bị vẽ thành hai phần tử cùng `data-row-key` không (đã phòng bằng phép gom theo khoá);
  (iii) chi phí thật của một shop 105 trang sau bản vá (rig ước 519s cho ca chậm nhất, hạn chặng `KmChay` = 900s).
- **Giới hạn còn lại, ĐO ĐƯỢC**: nếu MỌI trang đều khựng > ~0,9s giữa chừng khi vẽ (rig: khựng 1,4s) thì vẫn
  tick thiếu mà KHÔNG cảnh báo — vì chuẩn số dòng/trang không học được từ đâu. Trang nào khựng LẺ thì chuẩn cứu
  được (rig 8b). Cần một lượt chạy thật nữa để biết ca này có xảy ra không.
