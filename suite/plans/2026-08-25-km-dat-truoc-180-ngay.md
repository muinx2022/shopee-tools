# Plan: KM của Shop — ngày chương trình = HÔM NAY + 180 ngày

- **Ngày:** 2026-08-25
- **Trạng thái:** hoàn thành
- **Người lập:** phiên chính · **Người thực thi:** phiên chính (TỰ LÀM — xem §6)

## 1. Bối cảnh & mục tiêu

Người dùng: *"sửa lại phần chạy km, thay vì chọn cùng ngày thì chọn ngày hiện tại + 180 ngày, khung giờ vẫn
như cũ"*.

Hiện trạng — ngày của chương trình khuyến mãi được quyết ở **hai** chỗ, cả hai đều là "HÔM NAY":

| Đường | Chỗ quyết ngày | Giá trị cũ |
|---|---|---|
| Bấm tay (form "KM của Shop") | `KmDialogViewModel` ctor, `_ngay = DateTime.Today` | hôm nay (người dùng đổi được bằng `DatePicker`) |
| Lịch tự động (2–6h sáng) | `AccountsViewModel.NhipLichKmAsync`, `new ThamSoKm(ten, bayGio.Date, …)` | hôm nay |

Khung giờ (`GioBatDau`/`GioKetThuc`, mặc định 09:30–10:30) **giữ nguyên** — chỉ đổi phần NGÀY.

## 2. Phạm vi

- **Làm:**
  - Một nguồn sự thật duy nhất cho độ trễ: `KmLichTuDong.SoNgayDatTruoc = 180` + hàm thuần `NgayChuongTrinh(bayGio)`.
  - Đường tự động và ngày mặc định của form đều dùng hàm đó.
  - Sửa `LanChayKe` / `CauBatDauChay`: "lần kế dự kiến" phải tính từ **mốc CHẠY**, không phải từ ngày chương
    trình (xem §5 — nếu không sửa thì câu tổng kết lệch đúng 180 ngày).
  - Cập nhật/bổ sung test, kể cả các test đang mượn cấu hình `01:00` để dựng ca "mốc quá khứ" (ca đó KHÔNG còn
    quá khứ khi ngày đẩy đi 180 ngày).
- **Không làm:**
  - KHÔNG đụng extension `flow-km.js` (trần chuyển tháng `TRAN_CHUYEN_THANG = 8` đã dư cho ~6 tháng) — chỉ thêm
    ghi chú vì từ nay bước chuyển tháng LUÔN phải chạy.
  - KHÔNG đổi khung giờ, % giảm, chu kỳ tự động, khung 2–6h, luật chủ trì.
  - KHÔNG đổi tham số của lượt DÒ DOM (`thamSoLuotDo` trong `flow-km.js`) — nút chẩn đoán chạy tay ban ngày,
    cố ý dùng ngày gần để bước đỏ là bằng chứng selector hỏng thật.

## 3. Các bước thực hiện

1. `orders/XuLyDonShopee.Core/Services/KmLichTuDong.cs` — thêm `SoNgayDatTruoc` + `NgayChuongTrinh(DateTime)`.
2. `orders/XuLyDonShopee.App/ViewModels/KmDialogViewModel.cs` —
   - ctor: `_ngay = KmLichTuDong.NgayChuongTrinh(DateTime.Now)`;
   - `LanChayKe(bayGio, soNgay)` đổi chữ ký: tính `bayGio.Date + max(1,soNgay)` kéo về `02:00` (đúng
     `KmLichTuDong.MocChayKe`), thay cho `ngay + gioBatDau + soNgay`;
   - `CauBatDauChay(…, DateTime bayGio)` nhận thêm mốc hiện tại và truyền xuống `LanChayKe`.
3. `orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs` — `NhipLichKmAsync`: tính `ngayCt` một lần
   rồi dùng cho CẢ BA chỗ (tên tự sinh · `KiemTraMocTuongLai` · `ThamSoKm`).
4. Test: cập nhật `KmFormTests`, `KmNhipTuDongTests`, `KmChuongDayHoSoTests`; thêm test khoá độ trễ 180 ngày ở
   cả hai đường (form + tự động) và test `LanChayKe` mới.
5. `extensions/shopee-orders/flow-km.js` — ghi chú ở `datOThoiGian`: chuyển tháng nay là bước BẮT BUỘC.

## 4. Tiêu chí nghiệm thu

- [x] `KmLichTuDong.NgayChuongTrinh(new DateTime(2026,8,25,3,0,0))` = `2027-02-21` (25/08/2026 + 180 ngày).
- [x] Form mới mở: `vm.Ngay == DateTime.Today.AddDays(180)`.
- [x] `NhipLichKmAsync` ở mốc 02:05 ngày 19/08/2026 với `GioBatDau = "09:30"` ⇒ log lượt bắt đầu chứa
      `15-02-2027 09:30-10:30` (ngày chương trình, KHÔNG phải 19-08-2026).
- [x] Tên tự sinh của lượt tự động mang ĐÚNG ngày chương trình (`KM ngày 15-02-2027 từ 09:30 - 10:30`).
- [x] Cấu hình `GioBatDau = "01:00"` ở nhịp 02:05 KHÔNG còn bị chặn "QUÁ KHỨ" (vì mốc đã ở tương lai xa) — vế
      chặn mốc quá khứ vẫn còn nguyên và vẫn đỏ khi người dùng tự chọn ngày lùi trên form.
- [x] `CauBatDauChay` với `bayGio = 18-08-2026`, `soNgay = 7` ⇒ "lần kế dự kiến 25-08-2026 02:00".
- [x] `dotnet build ShopeeSuite.sln` — **0 warning, 0 error**.
- [x] Toàn bộ `XuLyDonShopee.Tests` xanh.
- [x] THỬ PHÁ: đổi `SoNgayDatTruoc` về 0 ⇒ các test mới phải ĐỎ.

## 5. Rủi ro & lưu ý

1. **`180` là ĐÚNG TRẦN của Shopee** ("không quá 180 ngày", plan `2026-08-18-km-cua-shop.md` §3). Nếu Shopee
   tính trần theo mốc *now + 180 ngày* (chứ không phải theo NGÀY lịch) thì `09:30` của ngày thứ 180 nằm SAU
   trần vài giờ ⇒ ô ngày bị `disabled` ⇒ `pageClickNgayTheoSo` báo `không thấy ô ngày 'N'` ⇒ bước 4 đỏ ⇒ shop
   bị bỏ qua (KHÔNG tạo nhầm chương trình nào — `daTao` chỉ bật ở bước 9). Cách gỡ khi gặp: hạ
   `KmLichTuDong.SoNgayDatTruoc` xuống 179. Để ở MỘT hằng số chính là để đổi bằng một dòng.
2. **Bước chuyển tháng từ nay là bắt buộc.** Trước đây chương trình đặt cùng ngày nên picker đã mở sẵn đúng
   tháng; nay luôn phải bấm mũi tên ~6 lần. `TRAN_CHUYEN_THANG = 8` vẫn dư, và `chuyenToiThang` hỏng thì trọng
   tài vẫn là phép ĐỌC LẠI `HH:mm dd-MM-yyyy` ở cuối bước.
3. **Vế "mốc quá khứ" của lượt tự động trở thành vô hiệu** (ngày +180 thì giờ nào cũng ở tương lai). Cố ý GIỮ:
   nó vẫn là cửa đúng cho đường bấm tay, và là lưới an toàn nếu độ trễ bị hạ về 0.

## 6. Báo cáo thực thi

Chạy **KHÔNG** theo 5 chặng: phiên này bị cấu hình cấm gọi subagent, nên phiên chính tự viết plan, tự code, tự
build và tự chấm — **không có lượt nghiệm thu / phản biện độc lập**. Số liệu dưới đây là do chính người làm đo.

- Build `ShopeeSuite.sln`: **0 warning, 0 error**.
- `dotnet test orders/XuLyDonShopee.Tests`: **2080/2080 xanh** (4 phút 26).
- `dotnet test suite/Shopee.Suite.Tests`: **64/64 xanh**.
- **Thử phá vòng A** — hạ `SoNgayDatTruoc` 180 → 0 ⇒ đúng 5 test đỏ:
  `KmLichTuDongTests.NgayChuongTrinh_LaNgayChayCong180_VaBoPhanGio`,
  `KmLichTuDongTests.MocChuongTrinh_LuonOTuongLai_DuGioBatDauSomNhat`,
  `KmFormTests.VmForm_NgayMacDinh_LaHomNayCong180`,
  `KmNhipTuDongTests.NgayChuongTrinh_CuaLuotTuDong_LaNgayChayCong180`,
  `KmNhipTuDongTests.GioBatDauSomHonNhip_KhongConLaMocQuaKhu`. Khôi phục ⇒ xanh lại.
- **Thử phá vòng B** — trả `LanChayKe` về công thức cũ (`bayGio.Date + soNgay + 09:30`) ⇒ 2 test đỏ:
  `KmFormTests.LanChayKe_TinhTuMocChay_VaKhopMocChayKeCuaLich`,
  `KmFormTests.CauBatDauChay_NoiRoLaDANGCHAY_VaDuThongTin`. Khôi phục ⇒ xanh lại.

### Hai ca test PHẢI sửa vì bản vá làm chúng đo nhầm nhánh

Cả hai đều mượn `GioBatDau = "01:00"` để dựng cảnh "mốc quá khứ" — cấu hình ấy nay HỢP LỆ, nên chúng vẫn XANH
nhưng đi nhánh khác hẳn nhánh chúng tưởng mình đo. Đổi sang dựng cảnh bằng "giờ kết thúc ≤ giờ bắt đầu":

- `orders/XuLyDonShopee.Tests/KmChuongDayHoSoTests.NhipTuDong_CauHinhHong_VanGhiMocThu_VaVanRungChuong`
- `suite/Shopee.Suite.Tests/KmVongBonTests.DongNhipLichKm` — ca này thì ĐỎ THẬT
  (`MoiCauHuongDan_ChiNhacToiNutCoThat` mất dòng "CẤU HÌNH KHÔNG DÙNG ĐƯỢC" để rà).

### Vòng nghiệm thu + phản biện (chạy sau, 25/08/2026)

Người dùng cho phép gọi subagent ⇒ chạy bù chặng 3 và 4, SONG SONG, chia độc quyền tài nguyên (nghiệm thu giữ
build/test, phản biện chỉ đọc code).

**Nghiệm thu: ĐẠT** — 8/9 tiêu chí, 1 ĐẠT MỘT PHẦN (tiêu chí 4). Tự chạy lại build + 2 bộ test + thử phá vòng A,
danh sách 5 test đỏ trùng khít. Ghi lại một cạm bẫy: khôi phục file bằng `Copy-Item` kéo theo timestamp CŨ ⇒
MSBuild coi là up-to-date ⇒ test đỏ GIẢ; phải set lại `LastWriteTime`.

**Phản biện: tìm được 3 lỗ thật.** Đã sửa cả ba:

1. **LỖ PHỦ ĐÃ ĐO ĐƯỢC (nặng nhất).** Đổi ĐÚNG một chỗ — `ThamSoKm` nhận `bayGio.Date` thay vì `ngayCt`, tức
   trả lại nguyên hành vi CŨ — mà **216/216 test KM vẫn XANH**. Nguyên nhân: mọi test nhịp bị chặn ở cửa khoá
   xuyên máy nên `cauBatDau` (chỗ duy nhất `thamSo.Ngay` lộ ra) không bao giờ được ghi; thước duy nhất còn lại
   là dòng log, mà dòng log đọc `ngayCt` — một chuỗi song song tự nhất quán. **Sửa: dựng `ThamSoKm` MỘT LẦN rồi
   log in `thamSo.Ngay`/`thamSo.Gio*`.** Chạy lại đúng phép đột biến đó ⇒ **2 test ĐỎ**.
2. **`pageBamChuyenThang` là code chết trong production trước bản vá** (ngày = hôm nay ⇒ picker đã đúng tháng ⇒
   0 cú bấm), nay chạy ~144 lần/đêm — mà selector nút mũi tên tự khai là "CHƯA dò thật". Công cụ chẩn đoán duy
   nhất (`dumpKm`) lại vẫn để ngày HÔM NAY ⇒ 0% độ phủ trên đúng đoạn code mới bắt buộc. **Sửa: `thamSoLuotDo`
   nay dùng +180 ngày** — một lượt dò trên MỘT shop trả lời được cả "mũi tên có click ăn không" lẫn "trần 180
   có chặn không", trước khi đốt cả đêm 12 shop.
3. **Ba chỗ tài liệu nói sai**: xmldoc `SoNgayDatTruoc` chỉ nói bước 4 (thực tế còn hình dạng `li giờ disabled`,
   và có thể là bước 5) và bỏ qua hậu quả "chạy trắng MỖI ĐÊM, vĩnh viễn" khi nguyên nhân không tự hết;
   `TRAN_CHUYEN_THANG` ghi "khoảng 7 tháng" (đo brute-force 2024→2032: **tối đa 6**); `KiemTraMocTuongLai` lập
   luận từ cảnh "mở form lúc 14h, ngày mặc định hôm nay 09:30" — cảnh đó không còn tồn tại.

**Một đề xuất của phản biện tôi CỐ Ý KHÔNG làm:** "lượt tạo 0 shop thì vẫn ghi `LanChayCuoi` để khỏi bắn lại đêm
sau". Đó là lật ngược một quyết định đã cân nhắc và ghi rõ trong `ChotLuotKm` ("ghi bừa cho một lượt hỏng sạch là
đẩy lịch đi xx ngày cho một đêm chẳng tạo được gì"). Cách gỡ đúng cho rủi ro trần-180 là hạ hằng số, không phải
làm cùn cơ chế thử lại.

### Còn lại / chưa làm

- **Chưa chạy thật trên Shopee.** Rủi ro §5.1 (180 đúng bằng trần) chỉ một đêm chạy thật mới trả lời được.
  Đường rẻ nhất để biết trước: `SHOPEE_DEV_TOOLS=1` → nút DÒ trên MỘT shop (nay đi đúng +180 ngày).
- **NGÀY nay đã khoá được tại chỗ gọi** (xem lỗ phủ #1 ở trên). **TÊN chương trình thì chưa** — nó chỉ lộ ra
  qua `cauBatDau`, mà câu đó chỉ được ghi sau khi mượn được hồ sơ. Tên dựng từ cùng `ngayCt`, ngay dòng trên.
- **`suite/Shopee.Suite.Tests` KHÔNG chạy lại được ở lượt đo cuối**: Windows Application Control chặn
  `ShopeeSuite.dll` vừa build (`FileLoadException … 0x800711C7`). Đã thử build lại + xoá sạch `bin`/`obj`, vẫn
  chặn — chính sách máy, không phải code. Bộ này **đã xanh 64/64** trong cùng phiên trước khi đổi câu chữ dòng
  log; câu mới không chứa dấu ngoặc kép nên không đụng lưới rà của `MoiCauHuongDan_ChiNhacToiNutCoThat`, nhưng
  **chưa đo lại được**.
