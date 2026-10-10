# Plan: ba cột KM · Combo · Flash Sale ở lưới tab "Shops"

- **Ngày:** 2026-08-27
- **Trạng thái:** chờ làm (lượt combo kiểm chứng đang chạy tới ~01:05 — build sẽ phải tắt app)
- **Người lập:** phiên chính · **Người thực thi:** `opus-dev`

## 1. Yêu cầu người dùng — nguyên văn

> "phần shopee - list tk, hiện tại có 2 cột, shop và Chuẩn bị hàng. Thêm 3 cột, km, combo và flash sale, khi
> chạy đến phần nào thì trạng thái cũng thay đổi như check đơn (đã thực hiện đến shop nào, đang thực hiện shop
> nào), với check đơn, ô điền là số đơn, còn với các chương trình km, ô điền là số sp trong chương trình được
> tạo ra"

Chốt thêm 27/08 khi được hỏi: **số của ba cột bám ĐÚNG ngày đang chọn ở ô lọc**, cùng quy ước với cột
"Chuẩn bị hàng" — không phải "lượt gần nhất".

## 2. Hiện trạng (đã dò, không phải phỏng đoán)

| Thứ | Ở đâu | Ghi chú |
|---|---|---|
| Lưới | `Views/AccountsView.xaml` ~809–869 | `DataGrid` bind `ResultRows`, 3 cột: 40px tiến độ · Shop · Chuẩn bị hàng |
| Dòng | `ViewModels/ShopPrepareRow.cs` | `ShopName` · `ShopLogin` · `PreparedCount` · `DaKiemTra` · `IsChecking` · `CoLoiDiaChi` |
| Dựng dòng | `AccountsViewModel.KetQua.cs:LoadResults` | `Results.GetShops` LEFT JOIN `Results.GetPreparedByDay` |
| Số đơn | bảng `prepare_daily(account_id, shop_login, day, count)` | `IncrementPrepared` cộng dồn +1/đơn |
| Tiến độ | sự kiện `AppServices.ShopCheckChanged(accountId, shopLabel, checking)` | phiên bắn → `OnShopCheckChanged` → `_shopCheck` / `_shopDaCheck` → `ApplyShopCheckFlags` |
| Vòng KM/Combo | `OrdersBridgeSession.ChayVongTaoChuongTrinhAsync` | **KHÔNG bắn sự kiện tiến độ nào** — chỉ có `onShopListRead` + `log` |
| Số sp | `KetQuaKmMotShop.SoSanPham` | đã có sẵn, đọc từ gói `kmKetQua` của extension |

### 2b. Khoá nhận dạng shop — chỗ dễ hỏng ÂM THẦM nhất, đã kiểm

Nhật ký hai luồng gọi tên shop khác nhau nên trông như hai nguồn dữ liệu khác nhau:

```
[Shop 1/12] alina99.store          ← check đơn
[Combo 1/12] Alina Store1 (id=…)   ← combo
```

**Không phải hai nguồn.** Cả hai đi qua đúng một hàm `DocDanhSachShopAsync` đọc đúng một trang
`/portal/shop`, trả cùng `ShopListItem(ShopId, ShopName, LoginName)`; khác nhau chỉ ở chỗ dòng log nào in
trường nào. Và `ResultsRepository.UpsertShops` ghi:

- `account_shops.shop_login` = `LoginName` (thiếu thì `ShopName`)
- `account_shops.shop_name` = `ShopName` (thiếu thì login)

⇒ **Khoá phải là `LoginName` ưu tiên, thiếu thì `ShopName`** — chép ĐÚNG biểu thức của `UpsertShops`, không
được lấy `shop.ShopName` cho tiện vì nó là cái vòng combo đang in ra log. Lấy nhầm thì mọi shop có tên hiển
thị khác login sẽ ghi vào một khoá không dòng lưới nào khớp: **không lỗi, không log, chỉ là ba cột trống**.

## 3. Thiết kế

### A. Bảng mới `km_program_daily`

```sql
CREATE TABLE IF NOT EXISTS km_program_daily (
    account_id  INTEGER NOT NULL,
    shop_login  TEXT NOT NULL,
    day         TEXT NOT NULL,          -- yyyy-MM-dd giờ địa phương, cùng khuôn prepare_daily
    loai        INTEGER NOT NULL,       -- LoaiChuongTrinh: 0=KM Shop, 1=Combo, 2=Flash Sale
    so_san_pham INTEGER NOT NULL DEFAULT 0,
    UNIQUE(account_id, shop_login, day, loai)
);
```

**CỘNG DỒN, không ghi đè** (`DO UPDATE SET so_san_pham = so_san_pham + excluded.so_san_pham`) — cùng luật với
`prepare_daily`. Chạy combo hai lượt trong một ngày là tạo **hai chương trình**, tổng sp của ngày phải là tổng
của cả hai. Ghi đè sẽ nuốt mất lượt trước.

### B. `int?` chứ KHÔNG phải `int` — phân biệt "không chạy" với "chạy mà ra 0"

Chương trình chạy 3 ngày/lần, nên **phần lớn ngày ba cột sẽ rỗng**. Hiện `0` cho một ngày không hề chạy là
phần mềm nói sai: người đọc hiểu thành "đã chạy và không tạo được sản phẩm nào".

- **không có dòng trong DB** ⇒ `null` ⇒ ô hiện `—` (hôm đó không chạy loại này)
- **có dòng, số 0** ⇒ ô hiện `0` (đã chạy shop này nhưng bỏ qua/hỏng, không tạo được sp nào)

⇒ Vòng chạy phải **GHI DÒNG CHO MỌI SHOP ĐÃ CHẠY QUA**, kể cả `SoSanPham = 0` (bỏ qua vì modal rỗng, hoặc
shop hỏng). Đây là điều kiện để chữ `0` có nghĩa; thiếu nó thì `0` và `—` lẫn vào nhau.

### C. Sự kiện tiến độ

**Cột 40px (vòng quay / tick) dùng LẠI `ShopCheckChanged`** — vòng KM/Combo bắn chính sự kiện đó. Ý nghĩa của
nó vốn là "phiên đang xử shop này", đúng cho cả hai luồng, và hai luồng KHÔNG BAO GIỜ chạy đồng thời trên
cùng tài khoản (KM tạm dừng check đơn trước khi chạy). Được cả vòng quay lẫn dấu tick mà không phải dựng
đường ống thứ hai.

**Sự kiện MỚI cho phần số + chữ "đang chạy…" trong đúng ô của loại:**

```csharp
event Action<long, string, LoaiChuongTrinh, bool, int>? ShopProgramChanged;
// (accountId, shopLabel, loai, dangChay, soSanPham)
```

- bắt đầu shop → `(dangChay: true, soSanPham: 0)`
- xong shop (kể cả bỏ qua / hỏng) → `(dangChay: false, soSanPham: n)`

VM nhớ `_shopChuongTrinh[accountId] = (ShopLabel, Loai, DangChay)` và áp bằng một hàm cùng khuôn
`ApplyShopCheckFlags`, khớp nhãn bằng chính `MatchesShopLabel` đang có (đã lo hoa/thường + khoảng trắng).

**Đường rót:** `OrdersBridgeSession` đã có sẵn field `_onShopListRead` nhận từ ctor — thêm
`_onShopChuongTrinh` cạnh nó, `AccountsViewModel.Phien.cs:MoPhienCauNoiAsync` (~1920) rót vào, trỏ tới
`_services.RaiseShopProgramChanged(accountId, …)`.

**Ghi DB ở đâu:** ngay tại chỗ bắn sự kiện "xong shop" trong `ChayVongTaoChuongTrinhAsync`, KHÔNG phải ở cuối
lượt. Vòng có **năm lối ra sớm** (trần thử · hết khung · mốc bắt đầu trôi qua · captcha × 2 · không về được
picker); gom ghi vào cuối là mọi lối ra sớm đều mất sạch số của các shop đã chạy xong.

### D. `ShopPrepareRow` thêm

```csharp
int? KmSoSanPham, ComboSoSanPham, FlashSoSanPham
LoaiChuongTrinh? LoaiDangChay        // null = không chạy chương trình nào trên dòng này
bool KmDangChay    => LoaiDangChay == LoaiChuongTrinh.ChuongTrinhShop;
bool ComboDangChay => LoaiDangChay == LoaiChuongTrinh.Combo;
bool FlashDangChay => LoaiDangChay == LoaiChuongTrinh.FlashSale;
```

MỘT trường `LoaiDangChay` chứ không ba cờ bool rời: ba cờ cho phép biểu diễn trạng thái "đang chạy KM và
Combo cùng lúc trên một shop" — thứ không tồn tại, nhưng một khi biểu diễn được thì sẽ có đường code tạo ra
nó. `[NotifyPropertyChangedFor]` cho cả ba bool.

### E. XAML

Ba `DataGridTemplateColumn` `Width="Auto"`, header `KM` · `Combo` · `Flash Sale`, cell dựng theo ĐÚNG khuôn ô
"Chuẩn bị hàng" đang có (`Grid` + hai `TextBlock` chồng nhau, `BoolToVis` / `InvBoolToVis`). Cột `Shop` giữ
`Width="*"`.

### F. Flash Sale — cột có, luồng chưa có

Chưa có `flow-flashsale.js`, nên cột Flash Sale **luôn hiện `—`**. Cố ý dựng đủ ba cột ngay: đường dữ liệu và
enum đã có `LoaiChuongTrinh.FlashSale`, làm sau sẽ phải sửa lại XAML + row + repo lần nữa.

### G. KHÔNG làm trong việc này

- Không thêm ô TỔNG cho ba cột mới (chỉ `TongChuanBiHang` như cũ) — người dùng không yêu cầu.
- Không đẩy số này lên Hub (số đơn có đường hub riêng; chương trình thì chưa cần đồng bộ đa máy).
- Không đụng `KmLichTuDong` / lịch xoay vòng — đó là plan `2026-08-27-mot-khung-xoay-vong-theo-ngay.md`.

## 4. Tiêu chí nghiệm thu (đo được)

- [ ] Bảng `km_program_daily` tạo được trên DB TRỐNG **và** trên DB CŨ đã có dữ liệu (đường `CREATE TABLE IF
      NOT EXISTS` trong `Database.cs` — kiểm cả hai, đừng chỉ kiểm cái đầu).
- [ ] Repo: ghi 2 lượt cùng `(acc, shop, day, loai)` ⇒ số **CỘNG DỒN**; khác `loai` ⇒ hai dòng riêng; khác
      `day` ⇒ hai dòng riêng.
- [ ] `LoadResults` đổ số vào đúng ba cột theo `ResultDayKey`; đổi ngày lọc ⇒ số đổi theo.
- [ ] Shop **không có dòng** ⇒ `null` ⇒ hiện `—`. Shop **có dòng số 0** ⇒ hiện `0`. Hai ca này phải có test
      RIÊNG, không gộp.
- [ ] Khoá ghi là `LoginName` (thiếu thì `ShopName`): test một shop có `LoginName="alina99.store"`,
      `ShopName="Alina Store1"` ⇒ số phải rơi vào dòng lưới `ShopLogin="alina99.store"`. **Đây là test bắt buộc**
      — xem §2b.
- [ ] `ShopProgramChanged(dangChay: true)` ⇒ đúng MỘT dòng, đúng MỘT cột hiện "đang chạy…"; hai cột kia không
      đổi. `dangChay: false` ⇒ tắt chữ đó và điền số.
- [ ] Vòng KM/Combo bắn `ShopCheckChanged` ⇒ vòng quay + dấu tick chạy y như check đơn.
- [ ] **Lối ra sớm giữ được số**: dựng ca captcha ở shop 3/12 ⇒ số của shop 1 và 2 vẫn ở trong DB.
- [ ] Ca `SoSanPham = 0` do BỎ QUA (modal rỗng) vẫn ghi dòng ⇒ hiện `0`, không phải `—`.
- [ ] Build sln + hub **0 warning**; 4 bộ test xanh.
- [ ] **THỬ PHÁ** — bắt buộc có đủ ba, ghi rõ test nào đỏ:
  1. đổi khoá ghi từ `LoginName` sang `ShopName` ⇒ test §2b phải ĐỎ;
  2. đổi cộng dồn thành ghi đè ⇒ test cộng dồn phải ĐỎ;
  3. đổi `int?` thành `int` (0 thay cho null) ⇒ test phân biệt `—`/`0` phải ĐỎ.

## 4b. Phản biện tìm ra (27/08/2026) — sáu lỗi LỌT qua 11/11 tiêu chí

Nghiệm thu chấm ĐẠT 11/11. **Tiêu chí ở §4 có lỗ**, không phải người chấm sai — cả sáu lỗi dưới đây đều thoả
mọi tiêu chí đã viết. Ghi lại đây để lần sau viết tiêu chí biết phải hỏi thêm gì.

| # | Lỗi | Sửa | Test khoá |
|---|---|---|---|
| 1 | **Cột hiện SỐ cho shop KHÔNG tạo được chương trình.** `soSpShop = mot.SoSanPham` không hỏi `DaTao`, mà extension VẪN trả `soSanPham` ở các lối ra HỎNG (`flow-km.js` b7/b8, `flow-combo.js` cùng khuôn) — đó là số sp đã nạp vào FORM, bước TẠO thật là b9. Shop 240 sp hỏng ở b8: log ghi "HỎNG", Shopee không có chương trình, ô lưới hiện **240**. | `soSpShop = mot.DaTao ? mot.SoSanPham : 0` — vẫn GHI dòng, để `0` giữ nghĩa "đã chạy mà không tạo được gì". Cùng luật với `CauKetQuaShop` và `KetQuaKm.SoTao`. | `VongThat_HongOBuocTao_ODem0_ChuKhongPhaiSoSpDaNapVaoForm` |
| 2 | **Ghi DB trong `finally` không bọc `try/catch`.** `SQLITE_BUSY` ⇒ ngoại lệ ném TỪ `finally` ⇒ thoát vòng ⇒ **vứt `ketQua` đã tích luỹ** ⇒ tổng kết "0 shop" dù 8 shop đã tạo thật ⇒ `ChotLuotKm` không ghi `LanChayCuoi` ⇒ đêm sau 8 shop nhận chương trình THỨ HAI. Phụ: ngoại lệ từ `finally` THAY THẾ `OperationCanceledException` đang bay ⇒ log ghi "Lỗi" thay vì "Đã hủy". | Gom cả hai lượt bắn (bắt đầu + xong) vào `OrdersBridgeSession.BaoTienDoChuongTrinh` — nuốt mọi lỗi, chỉ để lại dòng log `ex.ToString()`. Ghi số là việc PHỤ, không có quyền giết vòng. | `CallbackGhiSoNem_VongVanChayHet_VaKetQuaConDuSoShop` |
| 3 | **Dấu tick ĐẦY SẴN từ giây đầu vòng KM/Combo.** `_shopDaCheck` chỉ bị xoá ở callback shop-list của vòng CHECK ĐƠN; vòng KM/Combo dùng lại `ShopCheckChanged` nhưng không có mốc reset của riêng nó. 05:50 check đơn tick hết 12 shop → 06:05 Combo khởi động với **cả 12 dòng đã tick**. | Vòng KM/Combo rót `onShopListRead` với `luotChayMoi: true` (`ChayLatCatAsync` thêm tham số `luotChayMoi`; `DoDanhSachShopVaoLuoi` nhận nó). | `VongChuongTrinh_XoaTickLuotTruoc_VaDungDongChoShopMoi` (vế 1) |
| 4 | **Số ghi vào DB mà KHÔNG dòng lưới nào hiện.** `ChayKmAsync`/`ChayComboAsync` không truyền `doVaoLuoiShops` ⇒ `onShopListRead = null` ⇒ `UpsertShops` không chạy. Shop mới thêm vào subaccount, chưa vòng check đơn nào xong ⇒ lượt Combo ghi đủ số, lưới không có dòng nào cho nó. | CÙNG MỘT NƯỚC với #3: `doVaoLuoiShops: true` cho cả hai vòng. | `VongChuongTrinh_XoaTickLuotTruoc_VaDungDongChoShopMoi` (vế 2) |
| 5 | **Huỷ giữa shop vẫn ghi dòng `0`.** Bấm Dừng lúc shop 3 đang chờ ⇒ OCE ⇒ `finally` ⇒ ghi `(shop3, hôm nay, 0)`. `0` = "đã chạy mà không tạo được gì", sự thật là "bị cắt ngang" — làm mờ đúng ranh giới `—`/`0` của §B. | Số sản phẩm của callback đổi thành `int?`: `null` = KHÔNG có số để ghi. Nhánh huỷ bắn `null` (vẫn bắn, để TẮT chữ "đang chạy…"). Điều kiện là `ct.IsCancellationRequested && !daChotShopNay` — xem "Lệch so với đề bài" bên dưới. | `HuyGiuaShop_KhongGhiDong_OLuoiVanLaGachNgang` |
| 6 | **Comparer lệch.** `GetChuongTrinhByDay` trả `Dictionary<(string, LoaiChuongTrinh), int>` mặc định ⇒ so chuỗi PHÂN BIỆT hoa/thường, trong khi `ChuanHoaKhoaShop` và `MatchesShopLabel` CỐ Ý dùng `OrdinalIgnoreCase` sau một sự cố "ra 0 một cách LẶNG". | `ResultsRepository.KhoaShopLoaiComparer` (chuỗi bỏ hoa/thường, enum so bằng) + CỘNG DỒN khi hai dòng chỉ khác hoa/thường. | `KhoaLechHoaThuong_VanRaSo_VaCongDon` |

**Lệch so với đề bài, ở #5.** Đề bài nói "kiểm `ct.IsCancellationRequested`". Bản làm kiểm
`ct.IsCancellationRequested && !daChotShopNay`. Lý do: shop vừa chốt xong mà cú bấm Dừng tới ngay sau đó thì số
ĐÃ CÓ THẬT và `ketQua` đã đếm shop đó là "đã tạo" — vứt nó đi là lưới hiện `—` cho một shop mà câu tổng kết nói
đã tạo được. Kiểm token một mình biến một ca hiếm thành mâu thuẫn giữa hai mặt của cùng một lượt.

**HẠN CHẾ ĐÃ BIẾT — ngày ghi lấy `DateTime.Now` LÚC GHI.** Không phải ngày lượt chạy bắt đầu. Lượt vắt qua nửa
đêm (vòng 12 shop mất ~1 tiếng, lượt tự động 02–06h thì không, nhưng lượt bấm tay 23:40 thì có) sẽ chia số làm
HAI ngày: shop chạy trước 00:00 vào ngày cũ, shop sau vào ngày mới. **Không mất dữ liệu**, chỉ là một lượt nằm
trên hai dòng ngày — và đây là hành vi NHẤT QUÁN với `prepare_daily` (`IncrementPrepared` cũng đọc `DateTime.Now`
lúc ghi). Đổi sang "ngày của lượt" thì phải đổi cả hai bảng cùng lúc, không làm lẻ.

**Chưa khoá bằng test:** chỗ gọi `ChayKmAsync`/`ChayComboAsync` có thật sự truyền `doVaoLuoiShops: true,
luotChayMoi: true` hay không. Cờ đó chỉ có tác dụng SAU khi trình duyệt đã phóng, nên không đo được nếu không mở
Brave. Test đo đúng thân callback mà production rót vào (`DoDanhSachShopVaoLuoi(…, luotChayMoi: true)`), nhưng
nếu ai đó gỡ hai cờ ở chỗ gọi thì không test nào đỏ.

## 5. Rủi ro

1. **Lưới hẹp lại.** Thêm 3 cột vào một `DataGrid` đang có cột `Shop` `Width="*"` — cần nhìn thật ở độ rộng
   cửa sổ mặc định, không chỉ tin build xanh.
2. **`LoadResults` chạy sau MỖI đơn** (`OnPrepareCountChanged`) và dựng lại toàn bộ dòng ⇒ mỗi lần dựng lại là
   một lượt đọc thêm bảng mới. Đo: 12 dòng × 1 truy vấn nhỏ, cùng cỡ với `GetPreparedByDay` đang có. Đọc MỘT
   map cho cả tài khoản (như `GetPreparedByDay`), tuyệt đối không truy vấn theo từng dòng.
3. **Cờ tiến độ bị xoá khi dựng lại dòng** — `LoadResults` đã phải gọi `ApplyShopCheckFlags` vì đúng lý do
   này; hàm áp cờ chương trình mới **phải được gọi ở cùng chỗ**, không thì chữ "đang chạy…" nhấp nháy rồi biến
   mất giữa lượt.
