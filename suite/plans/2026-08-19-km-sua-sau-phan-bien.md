# Plan: Sửa đợt lỗi KM sau nghiệm thu + phản biện 19/08

- **Ngày:** 2026-08-19
- **Trạng thái:** HOÀN THÀNH phần code (3 đợt Opus + đợt chốt của phiên chính, 20/08) — đã qua nghiệm thu +
  phản biện đủ 4 vòng. CHƯA commit; CHƯA chạy thật (vòng 12 shop, lịch tự động, shop rỗng, các nhánh DOM mới);
  extension trong repo LỆCH bản publish.
  vòng 3, và còn nợ MỘT lượt "Dò DOM KM" chạy thật (mọi nhánh DOM của cả ba đợt đều chưa có bằng chứng chạy)
- **Người lập:** Fable (phiên chính) · **Người thực thi:** Opus (`opus-dev`)

## 1. Bối cảnh & mục tiêu

Đợt "KM của Shop" (plan `2026-08-18-km-cua-shop.md`) đã qua nghiệm thu + phản biện độc lập 19/08.
Kết quả: **CHƯA ĐẠT** — 6 lỗi nặng, 8 lỗi trung bình, vài lỗi nhẹ. Các phát hiện nặng đã được phiên chính
tự mở code xác nhận từng cái. Việc này sửa TOÀN BỘ các phát hiện đó.

Người dùng đã chốt thêm (19/08): **khôi phục biên an 60 phút** của plan cũ §5 — vòng check đơn không được
đặt mốc trong `[01:00, 06:00)` khi KM tự động bật; khung KM vẫn là `[02:00, 06:00)`.

Bối cảnh kỹ thuật giữ nguyên từ plan cũ (đọc `2026-08-18-km-cua-shop.md` §2, §2b nếu cần): một hồ sơ Brave
một tiến trình; cổng cầu nối 47821; khoá xuyên máy `orders:<login>` một khoá cho mọi luồng; mọi thao tác KM
bằng DOM trong trang, KHÔNG CDP; luật "mỗi phép đọc-lại phải nêu được NÓ ĐỎ KHI NÀO".

⚠ Môi trường: `dotnet` trên PATH có thể báo "No SDKs found" — dùng
`C:\Users\Ng Xuan Mui\AppData\Local\Microsoft\dotnet\dotnet.exe` (SDK 8.0.423).

## 2. Phạm vi

- **Làm:** 14 hạng mục ở mục 3 (extension `shopee-orders`, `XuLyDonShopee.Core`, `XuLyDonShopee.App`, test).
- **Không làm:**
  - KHÔNG commit / push / phát hành. KHÔNG đồng bộ sang thư mục publish.
  - KHÔNG chạy vòng 12 shop thật / lượt tự động thật (việc chạy thật là đợt riêng, cần người dùng canh).
  - KHÔNG đụng các file thuộc việc khác đang dở trong cây (`MoListShopTests`, `NutChanDoanAn*`,
    `AccountsViewCuonLog*`, `VongBiCat*`, `NghiNganSauVong*`, `MoLaiTrinhDuyet*`, `AccountsViewVongDoi*`,
    plan khác) — TRỪ chỗ hạng mục dưới nêu đích danh.
  - KHÔNG xoá nút `DoDomKmButton` (người dùng đã chốt giữ) — chỉ sửa cho nó ẨN đúng (hạng mục 11).
  - KHÔNG điều tra vụ test host crash không tất định (ghi nhận riêng, ngoài đợt).

## 3. Các bước thực hiện

### Nhóm A — lỗi chặn

1. **`background.js:68` — `msg.thamSo` → `cmd.thamSo`.** `msg` không tồn tại trong scope `handleCommand`
   (tham số tên `cmd`) ⇒ `ReferenceError` ở MỌI lệnh `chayKm`; nút "Chạy chương trình" chưa từng chạy được.

2. **`flow-km.js` — bước hỏng thì DỪNG shop, không đi tiếp tới bước 9 (TẠO THẬT).**
   Hiện `await buoc(...)` vứt kết quả; `daTao` chỉ nhìn bước 9 (`flow-km.js:899`) ⇒ bước 3/4/5/8 đỏ vẫn tạo
   chương trình sai giờ/sai % và báo thành công. Sửa: trong `chayKmChoTabDangMo`, một bước cốt lõi (1–8)
   trả `ok:false` ⇒ dừng ngay, trả `{daTao:false, buocHong:[...]}` nêu đích danh bước hỏng đầu tiên; áp dụng
   cho CẢ `dumpKm` lẫn `chayKm` (dò cũng chỉ cần biết bước đầu tiên vỡ — các bước sau chạy trên trạng thái
   rác không có giá trị chẩn đoán). Riêng nhánh "shop rỗng" ở bước 6 vẫn trả `boQua` như spec hạng mục 7.
   Phía C# `DocKetQuaKmMotShop` (`OrdersBridgeSession.cs:586`): đọc `buocHong` cả khi `daTao == true`
   (phòng thủ — nếu vẫn lọt, ghi cảnh báo vào kết quả shop chứ không nuốt).

3. **`flow-km.js` — picker phải biết CHUYỂN THÁNG.** Hiện chỉ gửi `ngayTrongThang` ⇒ chọn 25-09 sẽ click
   nhầm 25-08 của tháng đang hiện. Sửa: C# gửi đủ ngày/tháng/năm; extension đọc tiêu đề tháng-năm của popper
   đang mở, bấm nút chuyển tháng (mũi tên của `eds-date-picker`) tới đúng tháng — có vòng chờ sau mỗi lần
   bấm — rồi mới click ô ngày. Phép đọc-lại giữ nguyên: so BẰNG chuỗi đầy đủ `HH:mm dd-MM-yyyy`.

4. **`flow-km.js:29` — trần trang modal 60 là sai gấp đôi.** Số đo thật của plan cũ §8: **10 SP/trang**
   (không phải 20) ⇒ 1000 SP = 100 trang. Sửa trần thành ≥ 105, sửa comment sai; **chạm trần thì trả cờ
   cảnh báo trong kết quả bước** (không lặng lẽ `break`) và C# ghi cảnh báo vào nhật ký shop đó.

5. **`% giảm` không được âm thầm đổi.** `flow-km.js:625` `tsAn.phanTram || 5` biến 0 thành 5. Sửa:
   extension KHÔNG tự thay giá trị — `phanTram` thiếu / không phải số 1–99 ⇒ bước 7 hỏng, nêu rõ lý do.
   Phía form (`KmDialogViewModel.KiemTraForm`): chặn % ngoài `[1, 99]` (0 cũng chặn — giảm 0% là vô nghĩa).

6. **Captcha giữa luồng phải DỪNG cả vòng.** Hiện chỉ kiểm sau `openShopDetail` (`OrdersBridgeSession.cs:516`).
   Sửa: `flow-km.js` phát hiện URL rơi vào `/verify` (kiểm ở đầu mỗi bước hoặc trong vòng chờ) ⇒
   `send({action:"captcha"})` như flow khác đang làm; C# kiểm `_channel.CaptchaSeen` cả SAU chặng `chayKm`
   của từng shop ⇒ dừng hẳn vòng (giữ quyết định "captcha thì dừng hẳn" của plan cũ §11).

7. **"Shop rỗng" phải đo TRONG modal đang mở, bằng chờ-điều-kiện.** Hiện bước 6 `sleep(2500)` rồi đọc một
   lần sau khi modal đóng ⇒ máy chậm là shop có hàng bị bỏ oan, và mọi hỏng bước 1–5 bị nguỵ trang thành
   "shop rỗng". Sửa: kết luận "rỗng" CHỈ từ phép đo khi modal chọn sản phẩm ĐANG mở (neo `.select-tips`,
   đọc số dòng bảng/`0/1000` với vòng chờ-điều-kiện như bước 7 đang dùng, không sleep cố định); và chỉ được
   trả `boQua` khi các bước trước đó đều `ok` (đã tự thoả nếu làm hạng mục 2).

8. **Chặng `KmChay` 240s có thể ngắn hơn thật + gói kết quả lạc shop.** `OrdersBridgeChannel.cs:234`.
   Sửa: (a) nâng hạn theo ngân sách thật — bước 6 tối đa ~105 trang × ~7s là chặng chi phối; đặt hằng có tên,
   ghi cách tính trong comment (gợi ý 900s, người thực thi tự tính lại và ghi số của mình);
   (b) chống gói lạc: `chayKm` gửi kèm token lượt (vd `shopId`), extension trả lại y nguyên trong `kmKetQua`,
   channel chỉ `TrySetResult` khi token khớp lượt đang armed — gói lạc thì ghi log và bỏ.

9. **Lịch tự động: mỗi tài khoản tối đa MỘT lượt thử mỗi khung 2–6h.** Hiện `LanChayCuoi` chỉ ghi khi
   `SoTao > 0` (`AccountsViewModel.Phien.cs:142`) ⇒ lượt hỏng (captcha, khoá máy khác giữ, lỗi 1) là nhịp
   60s thử lại vô hạn tới 6h sáng — hàng chục lượt SSO, đúng thứ làm Shopee nghi bot. Sửa: thêm mốc riêng
   `LanThuTuDong` (DB `km_config` thêm cột, migration `ALTER TABLE`-an-toàn hoặc `CREATE TABLE IF NOT EXISTS`
   theo khuôn `Database.cs` hiện có), ghi NGAY KHI lượt tự động bắt đầu; `DenGioChay` trả false nếu
   `LanThuTuDong` đã rơi trong khung 2–6h của CÙNG đêm. `LanChayCuoi` giữ nguyên ngữ nghĩa "lần TẠO ĐƯỢC
   gần nhất" (gốc tính xx ngày). Lượt bấm tay KHÔNG ghi `LanThuTuDong`.

10. **`dongCuaSoKhiXong` phải đóng + trả hồ sơ cả khi `than` NÉM.** Khối đóng cửa sổ + `TraHoSo()` hiện nằm
    trên đường thành công (`AccountsViewModel.Phien.cs:1093`); `than` ném thì `finally` bỏ qua `TraHoSo` vì
    `CuaSoXemDangMo == true` ⇒ lúc 2h sáng khoá xuyên máy kẹt, check đơn chết, Brave mở tới sáng. Sửa: khi
    `dongCuaSoKhiXong == true`, việc đóng cửa sổ + trả hồ sơ chuyển vào đường `finally` (chạy cả khi ném/huỷ).
    ⚠ ĐỌC KỸ comment dài trong `finally` hiện có (`Phien.cs:1110-1142`) trước khi sửa — đừng phá hành vi
    của "Mở list shop" (nút đó CỐ Ý để cửa sổ lại, `dongCuaSoKhiXong == false`).

11. **Nút "Dò DOM KM" phải ẨN THẬT khi không đặt `SHOPEE_DEV_TOOLS`.** `HienNutDev` là property **static**
    (`Phien.cs:202`) mà XAML dùng `{Binding HienNutDev}` (`AccountsView.xaml:699`) — WPF không phân giải
    static qua Binding path ⇒ binding fail ⇒ nút HIỆN với mọi người, mà bấm là tạo chương trình THẬT.
    Sửa: chuyển thành property instance trên VM (đọc env var một lần trong ctor là đủ), giữ nguyên xmldoc.
    Sửa luôn khối XAML của nút này đang bị dồn dòng hỏng format (`AccountsView.xaml:698-699`) về nhiều dòng
    như các nút xung quanh. Thay test trang trí: `NutChanDoanAnTests` hiện chỉ `Assert.Contains` chuỗi trong
    file .xaml — thêm test `[StaFact]` dựng control thật (khuôn `KmFormTests` đã dùng) đo `Visibility`
    theo cả hai giá trị env var. (File `NutChanDoanAnTests.cs` thuộc việc khác nhưng được đụng ĐÚNG phần này.)

12. **Khôi phục biên an 60 phút (người dùng chốt 19/08).** `KmLichTuDong`: `HoanVongCheckDon` dời mọi mốc
    rơi vào `[01:00, 06:00)` sang 06:00 (thêm hằng có tên, vd `GioBatDauBienAn = 1`); `TrongKhung` cho
    lượt KM giữ nguyên `[02:00, 06:00)`. Cập nhật xmldoc (comment hiện tại lập luận ngược §5 — viết lại cho
    khớp quyết định mới). Cập nhật test biên trong `KmLichTuDongTests`.

### Nhóm B — trung bình / nhẹ

13. **Đường "Đóng" của form KM không được lưu dữ liệu rác.** `MoFormKm` (`Phien.cs:104`) hiện `Save` vô điều
    kiện. Sửa: Đóng thì chỉ lưu khi `KiemTraForm` đạt; không đạt thì giữ nguyên cấu hình cũ (không lưu, không
    báo lỗi chặn — người dùng chỉ đóng form). `KiemTraForm` bổ sung: giờ định dạng `HH:mm` đúng 5 ký tự,
    `Ngay + GioBatDau` phải ở TƯƠNG LAI (đường bấm Chạy), `GioKetThuc > GioBatDau`, `SoNgay >= 1` khi bật
    tự động, % theo hạng mục 5.

14. **Câu chữ + vặt:**
    - Tooltip `KmDialog.xaml:40`, xmldoc `MoFormKm` (`Phien.cs:85-87`), header `KmDialog.xaml.cs:10-11`:
      còn nói "PHA 2 — CHƯA mở trình duyệt, CHƯA tạo chương trình" — sai từ khi nối pha 3. Viết lại: nút
      Chạy MỞ trình duyệt và TẠO chương trình THẬT trên mọi shop.
    - `MuonHoSoAsync` (`Phien.cs:306`) nhận tham số lý do để câu log đúng ngữ cảnh (KM tự động ≠ "mở list
      shop… đóng cửa sổ xong sẽ chạy lại").
    - `RunOnUi(() => _ = NhipLichKmAsync(...))` (`KetQua.cs:202`): bọc để exception không rơi vào task
      không ai quan sát — lỗi phải ra nhật ký.
    - `DenGioChay` cắt phần giờ của `LanChayCuoi` (`cuoi.Date`): GIỮ ngữ nghĩa theo ngày lịch (chạy tay
      23:50 ngày D, SoNgay=1 ⇒ lượt tự động 02:00 ngày D+1) nhưng thêm test khoá đúng ca này + xmldoc nói rõ.

15. **Rig test còn thiếu theo §6 plan cũ** (tiêu chí bị chấm KHÔNG ĐẠT): test khoá chuỗi lệnh của
    `ChayKmMoiShopAsync` theo khuôn `BridgeTestRig`/`MoListShopTests` cho ít nhất: (a) shop có SP — tạo được;
    (b) shop rỗng — `boQua`, không tạo; (c) shop hỏng giữa chừng — ghi HỎNG, vòng đi tiếp shop kế;
    (d) captcha — dừng cả vòng. Cộng test mới cho các hạng mục 8 (token lạc), 9 (một lượt thử mỗi khung),
    10 (ném vẫn trả hồ sơ — nếu test được ở mức VM), 12 (biên 60').

## 4. Tiêu chí nghiệm thu

- [ ] Build cả solution `ShopeeSuite.sln` (rebuild): **0 warning, 0 error**.
- [ ] Toàn bộ test xanh (chạy `orders/XuLyDonShopee.Tests` + 3 bộ suite hiện có). Ghi rõ con số.
- [ ] `background.js` không còn `msg.` trong `handleCommand`; grep `msg\.thamSo` = 0 hit.
- [ ] Rig test hạng mục 15 đủ 4 ca (a)–(d), và MỌI test mới đều đã **THỬ PHÁ** (sửa ngược code → đỏ →
      khôi phục → xanh; dùng cách sửa tạo timestamp mới — bẫy `Copy-Item` đã ghi ở plan cũ §12). Ghi lại
      từng ca phá: phá gì, test nào đỏ.
- [ ] `flow-km.js`: kịch bản "bước 4 hỏng" KHÔNG chạy tới bước 9 (chứng minh bằng test hoặc bằng đọc luồng
      + rig ca (c)); `phanTram = 0` không bao giờ thành 5; trần trang ≥ 105 và chạm trần có cờ cảnh báo.
- [ ] `KmLichTuDongTests`: có test `HoanVongCheckDon` dời mốc 01:00–01:59 sang 06:00; mốc 00:59 giữ nguyên;
      test `DenGioChay` không cho lượt thử thứ hai trong cùng khung sau lượt hỏng.
- [ ] Test `[StaFact]` đo `Visibility` thật của `DoDomKmButton` theo env var, cả hai chiều.
- [ ] Migration DB: mở DB cũ (không có cột mới) không nổ — có test hoặc bằng chứng chạy.
- [ ] Không đụng file ngoài phạm vi mục 2 (trừ `NutChanDoanAnTests.cs` đúng phần nêu ở hạng mục 11).

## 5. Rủi ro & lưu ý

- **Cây làm việc có việc khác đang dở** — `git status` trước khi sửa; file `M` không thuộc danh sách phạm vi
  thì KHÔNG đụng. Số test là của cả cây, nói rõ trong báo cáo.
- Sửa `flow-km.js`/`background.js` KHÔNG tự chạy được bằng test C# — rig test chỉ khoá phía C#. Phần JS phải
  đọc-lại thật kỹ; mỗi phép đọc-lại mới thêm phải trả lời được "nó ĐỎ khi nào" (luật plan cũ §9).
- Hạng mục 10 đụng khung `ChayLatCatAsync` dùng chung với "Mở list shop" — đọc cả hai chiều comment trong
  `finally` trước khi đổi; hành vi "để cửa sổ lại" của Mở list shop phải GIỮ NGUYÊN (test `MoListShopTests`
  hiện có phải còn xanh).
- Hạng mục 3 (chuyển tháng) là DOM thật chưa dò — selector nút mũi tên tháng của `eds-date-picker` phải viết
  phòng thủ (không thấy nút ⇒ bước hỏng nêu rõ, đừng đoán mò click); người dùng sẽ chạy dò thật sau.
- Sau đợt này extension trong repo LỆCH bản publish — KHÔNG tự đồng bộ (ngoài phạm vi), ghi nhắc trong báo cáo.

---

## Báo cáo thực thi (Opus, 2026-08-19)

### Kết quả kiểm chứng (số THẬT, đo trên cây làm việc HIỆN TẠI — cây có việc khác đang dở)

| Việc | Kết quả |
|---|---|
| `dotnet build ShopeeSuite.sln` (có cả lượt `-t:Rebuild`) | **0 warning, 0 error** |
| `orders/XuLyDonShopee.Tests` | **1954/1954 xanh** (4 phút 24) — trước đợt này là 1926 |
| `suite/Shopee.Core.Tests` | **168/168 xanh** |
| `suite/Shopee.Module.Search.Tests` | **34/34 xanh** |
| `suite/Shopee.Suite.Tests` | **2/2 xanh** |
| `grep msg\.thamSo extensions/` | **0 hit** |
| `node --check` cho `flow-km.js` + `background.js` | cú pháp hợp lệ |

⚠ `dotnet` trên PATH vẫn báo "No SDKs found"; mọi lệnh chạy bằng
`C:\Users\Ng Xuan Mui\AppData\Local\Microsoft\dotnet\dotnet.exe` (SDK 8.0.423).

### Đã làm theo từng hạng mục

1. **`background.js` `msg` → `cmd`** — sửa; thêm `cmd.token` cho lệnh `chayKm`; cập nhật khối chú thích protocol.
2. **Bước hỏng ⇒ DỪNG shop.** `chayKmChoTabDangMo` nay giữ kết quả từng `buoc()` (`b1`…`b9`) và
   `return ketThuc()` ngay khi một bước `ok:false`; mọi lối ra đi qua một hàm `ketThuc()` để không lối nào quên
   trường nào. Ngoại lệ duy nhất là nhánh "shop rỗng" (hạng mục 7). Phía C#: `DocKetQuaKmMotShop` nay đọc
   `buocHong` CẢ KHI `daTao == true` và ghi thành **cảnh báo** (trường mới `KetQuaKmMotShop.CanhBao`), không nuốt.
3. **Picker biết CHUYỂN THÁNG.** C# gửi thêm `thang` + `nam`; extension có `pageDocThangNamPicker` (đọc tiêu đề
   tháng-năm của popper đang mở) + `pageBamChuyenThang` (mũi tên tháng, LOẠI nút năm) + vòng `chuyenToiThang`
   (trần `TRAN_CHUYEN_THANG = 8`, sau mỗi cú bấm CHỜ tiêu đề đổi thật). Hai ô thời gian gộp vào `datOThoiGian`.
   Phép đọc-lại giữ nguyên: so BẰNG chuỗi đầy đủ `HH:mm dd-MM-yyyy`.
4. **Trần trang modal 60 → 105** (1000 SP ÷ 10 SP/trang = 100, dư 5) + sửa comment sai; chạm trần đẩy một dòng
   vào `canhBao` của gói kết quả, C# ghi ra nhật ký shop đó (`CauKetQuaShop` in cảnh báo ở **mọi** ca).
5. **`% giảm` không còn bị âm thầm đổi.** Bỏ `tsAn.phanTram || 5`; `phanTramHopLe` = số nguyên 1–99, không hợp lệ
   ⇒ bước 8 HỎNG kèm lý do. Mặc định của lượt DÒ chuyển ra `thamSoLuotDo()` (thuộc về caller, không phải luồng).
   Phía form `KiemTraForm` đã chặn sẵn `[1, 99]` từ trước — giữ nguyên.
6. **Captcha giữa luồng dừng cả vòng.** `buoc()` nhận `tabId` và kiểm URL `/verify` ở ĐẦU MỖI BƯỚC → gửi
   `{action:"captcha"}` rồi dừng shop. C# kiểm `_channel.CaptchaSeen` **sau** chặng `chayKm` của từng shop ⇒ trả
   `KetQuaKm(..., captcha: true)`. `OrdersBridgeChannel` nhánh `captcha` nay hoàn tất cả `_kmChayTcs`/`_kmDumpTcs`
   (không thì vòng đứng chờ hết 900s cho một trang verify đã hiện từ giây đầu).
7. **"Shop rỗng" đo TRONG modal đang mở.** `pageDoModalSanPhamDangMo` neo `.select-tips`, scope mọi truy vấn
   trong modal; bước 6 CHỜ-ĐIỀU-KIỆN tới 20s (`CHO_MODAL_NHIP`). Kết luận "rỗng" **chỉ khi** modal đang mở +
   bảng 0 dòng + có dấu rỗng; 0 dòng mà KHÔNG có dấu rỗng ⇒ **HỎNG** (nói rõ số đo), không bỏ oan. Nhánh `boQua`
   chỉ tới được khi bước 1–5 đều `ok` (đã tự thoả nhờ hạng mục 2).
8. **`ChoChang.KmChay` 240s → 900s**, kèm bảng tính ngân sách từng bước trong xmldoc (bước 6 chi phối: 105 trang
   × ~7s ≈ 735s; tổng ≈ 886s). **Chống gói lạc:** `ArmKmChay(token)` + extension trả `token` nguyên văn; channel
   bỏ gói có token khác lượt đang chờ (ghi log). Token null (bản extension cũ) vẫn nhận + ghi log — chốt này để
   chặn gói LẠC, không phải để làm chết cả lượt khi bản cài chưa đồng bộ.
9. **Một lượt thử mỗi khung 2–6h.** Cột DB mới `km_config.lan_thu_tu_dong` (CREATE TABLE + `EnsureColumn` cho DB
   cũ) → `KmConfig.LanThuTuDong` → `KmLichTuDong.DaThuTrongKhungNay` chặn trong `DenGioChay`.
   `NhipLichKmAsync` ghi mốc **NGAY TRƯỚC** khi chạy (ghi sau thì đúng ca cần chặn nhất — lượt ném — lại không
   bao giờ ghi được). `LanChayCuoi` giữ nguyên ngữ nghĩa "lần TẠO ĐƯỢC gần nhất"; lượt bấm tay không ghi mốc thử.
10. **`dongCuaSoKhiXong` chuyển vào `finally`.** Khối đóng cửa sổ rời khỏi đường thành công, nay là
    `if (dongCuaSoKhiXong) { DongCuaSoCuLai(); }` ở ĐẦU `finally` (trước lượt `Dispose` vì nó còn đọc
    `_bridgeSession.Process`). Nó xoá `_pocProcess` ⇒ nhánh `if (!CuaSoXemDangMo) TraHoSo()` sẵn có lo phần trả
    hồ sơ — một cửa, không hai. Hành vi "Mở list shop" (`dongCuaSoKhiXong == false`) KHÔNG đổi, có test đối chứng.
11. **`HienNutDev` thành property instance** + `DocCoDev` (hàm thuần) + hằng `BienMoiTruongDev`; khối XAML của nút
    đã được trả về nhiều dòng đúng khuôn các nút xung quanh. Thêm test `[StaFact]` **đo `Visibility` THẬT**.
    ⚠ **Nguyên nhân gốc mà plan nêu KHÔNG tái hiện được** — xem mục "Điểm lệch" bên dưới.
12. **Khôi phục biên an 60 phút.** `GioBatDauBienAn = 1` + `TrongBienAn` (`[01:00, 06:00)`); `HoanVongCheckDon`
    dùng `TrongBienAn`, `TrongKhung` (khung CHẠY KM) vẫn `[02:00, 06:00)`. Viết lại xmldoc (bản cũ lập luận
    ngược §5) + `CauHoanVong` nói rõ có biên an. Cập nhật/thêm test biên.
13. **Đường "Đóng" không lưu dữ liệu rác.** `KmDialogViewModel.CauHinhHopLe`; `MoFormKm` chỉ `Save` khi hợp lệ.
    Bổ sung `KiemTraMocTuongLai` (mốc `Ngày + GioBatDau` phải ở tương lai) — **chỉ áp cho đường bấm Chạy**
    (`ThuChay(bayGio)`), không áp cho đường Đóng, kẻo mở form lúc 14h là không lưu nổi một thay đổi hợp lệ.
    Giờ `HH:mm` đúng 5 ký tự đã có sẵn nhờ `TryParseExact(@"hh\:mm")` — ghi rõ vào xmldoc thay vì thêm luật trùng.
14. **Câu chữ + vặt:** tooltip nút Chạy + nút Đóng của `KmDialog.xaml`, header `KmDialog.xaml.cs`, xmldoc
    `MoFormKm` / `KmDialogViewModel` / `CauBatDauChay` — viết lại thành "MỞ trình duyệt và TẠO chương trình
    THẬT". `MuonHoSoAsync` nhận `tenViec` + `tuChayLaiKhiXong` (câu log không còn nói "mở list shop… đóng cửa sổ
    xong sẽ chạy lại" cho lượt KM tự động 2h sáng). `NhipSangNgay` gọi `ChayNhipLichKmAsync` — bọc try/catch, lỗi
    RA NHẬT KÝ thay vì chìm vào task không ai quan sát. `DenGioChay` giữ ngữ nghĩa ngày lịch + xmldoc + test khoá.
15. **Rig test** — `KmVongRigTests` (5 test) lái CHÍNH hàm production `OrdersBridgeSession.ChayKmSauPickerAsync`
    (tách `static`, nhận `channel` + hai móc `vePicker`/`nghiGiuaShop`) qua `BridgeTestRig`: ca (a) shop có SP,
    (b) shop rỗng, (c) shop hỏng → đi tiếp shop kế, (d) captcha → dừng cả vòng, và (e) gói kết quả lạc lượt.

### File đã sửa / tạo

**Extension**
- `extensions/shopee-orders/background.js` — `cmd.thamSo` + `cmd.token`; chú thích protocol.
- `extensions/shopee-orders/flow-km.js` — viết lại phần khung: hằng `TRAN_TRANG_MODAL=105`,
  `TRAN_CHUYEN_THANG`, `CHO_MODAL_NHIP`; 3 page-func mới; `urlCuaTab`, chốt captcha trong `buoc()`;
  `chuyenToiThang`, `datOThoiGian`; bước 6 đo rỗng trong modal; `ketThuc()`; `thamSoLuotDo()`; `doChayKm(ts, token)`.
  Header file viết lại (bản cũ khẳng định "KHÔNG TẠO CHƯƠNG TRÌNH THẬT" — sai từ khi có bước 9).

**Core**
- `Services/OrdersBridgeChannel.cs` — `KmChay` 900s + bảng ngân sách; `ArmKmChay(token)` + lọc gói lạc;
  captcha hoàn tất 2 chặng KM; `ResetStages` dọn token.
- `Services/OrdersBridgeSession.cs` — `KetQuaKmMotShop.CanhBao`; tách `ChayKmSauPickerAsync` (static, testable);
  gửi `token`/`thang`/`nam`; kiểm captcha sau chặng `chayKm`; `DocBuocHongDau`; đọc `canhBao`; `CauKetQuaShop`
  in cảnh báo.
- `Services/KmLichTuDong.cs` — `GioBatDauBienAn`, `TrongBienAn`, `DaThuTrongKhungNay`, `DenGioChay` +4 điều kiện.
- `Data/KmConfigRepository.cs` — `KmConfig.LanThuTuDong` + đọc/ghi.
- `Data/Database.cs` — cột `lan_thu_tu_dong` trong CREATE TABLE + `EnsureColumn` cho DB cũ.

**App**
- `ViewModels/AccountsViewModel.Phien.cs` — `MoFormKm` chỉ lưu khi hợp lệ; `NhipLichKmAsync` ghi `LanThuTuDong`;
  `HienNutDev` instance + `DocCoDev` + `BienMoiTruongDev`; `MuonHoSoAsync(email, tenViec, tuChayLaiKhiXong, ct)`;
  đóng cửa sổ chuyển vào `finally`; xoá một biến cục bộ chết (`cauHinh` trong `MoFormKm`).
- `ViewModels/AccountsViewModel.KetQua.cs` — `ChayNhipLichKmAsync` (bọc lỗi ra nhật ký).
- `ViewModels/KmDialogViewModel.cs` — `_lanThuTuDong`, `CauHinhHopLe`, `KiemTraMocTuongLai`, `ThuChay(bayGio)`,
  xmldoc `DocGio` / `CauBatDauChay`.
- `Views/KmDialog.xaml`, `Views/KmDialog.xaml.cs` — tooltip + header nói đúng là CHẠY THẬT.
- `Views/AccountsView.xaml` — khối nút `DoDomKmButton` trả về đúng format nhiều dòng + comment.

**Test** (mới: 3 file · sửa: 4 file)
- MỚI `KmVongRigTests.cs` (5), `KmCauHinhLuuTruTests.cs` (3), `KmTraHoSoKhiNemTests.cs` (2).
- SỬA `KmLichTuDongTests.cs` (+9, sửa test biên cũ), `KmVongMoiShopTests.cs` (+4),
  `KmFormTests.cs` (+4, và 2 test cũ nay truyền `bayGio` — không thì bộ test đỏ khi chạy sau 9h30 sáng),
  `NutChanDoanAnTests.cs` (đúng phần hạng mục 11: 2 test cũ chuyển sang `DocCoDev`, thêm test `[StaFact]`
  đo `Visibility`).

### THỬ PHÁ — 15 ca, ĐỎ cả 15 (mỗi ca: sửa ngược → chạy → khôi phục → xanh lại)

Mọi lượt phá/khôi phục đều bằng công cụ Edit (ghi file mới ⇒ timestamp mới ⇒ MSBuild build lại thật) — KHÔNG
dùng `Copy-Item`, đúng cảnh báo plan cũ §12.

| # | Phá gì | Test ĐỎ |
|---|---|---|
| 1 | Bỏ `thang`/`nam` khỏi gói `chayKm` | `CaA_ShopCoSanPham_TaoDuoc_ChuoiLenhDungBang` |
| 2 | Bỏ kiểm `CaptchaSeen` sau chặng `chayKm` | `CaD_Captcha_GiuaChangChayKm_ThiDUNG_CA_VONG` |
| 3 | Dừng vòng khi một shop hỏng | `CaC_…VongDiTiepShopKe` + `CaD_…` (2 test) |
| 4 | Tắt lọc token gói lạc lượt | `GoiKetQuaLacLuot_ThiBiBO_KhongTinhChoShopDangChay` |
| 5 | Nuốt nhánh `boQua` (shop rỗng thành hỏng) | `CaB_ShopRong…`, `DocKetQua_ShopRong_LaBoQua…`, `GoiKetQuaLacLuot…` (3) |
| 6 | Nuốt `canhBao` | `DocKetQua_ChamTranPhanTrang…`, `DocKetQua_DaTaoMaVanCoBuocHong…` (2) |
| 7 | `HoanVongCheckDon` quay lại `TrongKhung` (bỏ biên an 60') | `HoanVongCheckDon_MocTrongBIEN_AN_60phut…` (3 ca Theory) |
| 8 | Bỏ chốt `DaThuTrongKhungNay` | `DenGioChay_DaThuTrongKhungNay_ThiKHONG_thu_lai_du_luot_do_HONG` |
| 9 | Bỏ `EnsureColumn(km_config, lan_thu_tu_dong)` | 2 test của `KmCauHinhLuuTruTests` |
| 10 | Xoá ràng buộc `Visibility` trong XAML | `Visibility_ThatCuaNut…` + `Xaml_PhaiRangBuoc_Visibility…` (2) |
| 11 | Đưa lượt đóng cửa sổ ra khỏi `finally` | `ThanNEM_MaLatCatTuKetThuc_ThiVanDongCuaSo_VaNhaKhoa` |
| 12 | Đóng cửa sổ ở MỌI lát cắt (`if (true)`) | `ThanNEM_MaKHONG_tu_ket_thuc_ThiCuaSo_O_LAI` + 2 test `MoListShopTests` |
| 13 | `KiemTraMocTuongLai` luôn trả null | `KiemTraMocTuongLai_ChiChanMocDaTroiQua`, `VmForm_MocQuaKhu_ThiThuChayTuChoi` (2) |
| 14 | `CauHinhHopLe` luôn true | `VmForm_CauHinhHopLe_ChiTheoDinhDang_KhongTheoMocQuaKhu` |
| 15 | `LayCauHinh` đánh rơi `LanThuTuDong` | `VmForm_LayCauHinh_GiuNguyen_LanThuTuDong` |

Ca 10 đáng ghi riêng: lượt phá ĐẦU TIÊN (đổi `HienNutDev` về `static`) **KHÔNG làm test đỏ** — xem mục dưới.
Lượt phá thật sự có ý nghĩa là xoá ràng buộc `Visibility` trong XAML.

### ĐIỂM LỆCH so với spec — đọc kỹ

1. **Hạng mục 11: nguyên nhân gốc mà plan nêu KHÔNG tái hiện được.** Plan viết: `HienNutDev` là `static` nên
   "WPF không phân giải static qua Binding path ⇒ binding fail ⇒ nút HIỆN với mọi người". **Đo lại thì sai:**
   dựng `AccountsView` thật, `Measure`+`Arrange`+`UpdateLayout` rồi đọc `Visibility` — bản `static` cho
   `Collapsed` khi không đặt biến và `Visible` khi `SHOPEE_DEV_TOOLS=1`, `BindingExpression.Status = Active` cả
   hai chiều (lượt phân giải Binding path có nhánh dự phòng `Type.GetProperty`, mặc định gồm cả `Static`). Kết
   quả này KHỚP với bằng chứng chạy thật ghi ở plan cũ §13 ("không có biến ⇒ UIA không thấy nút"), tức lượt phản
   biện đã kết luận sai chỗ này. Tôi VẪN đổi sang instance theo spec (vô hại, và đọc biến môi trường một lần mỗi
   VM thay vì mỗi lượt binding), nhưng đã **viết lại xmldoc + comment XAML cho đúng sự thật đo được** thay vì
   chép nguyên lời buộc tội sai vào code. Cái CÓ THẬT là lỗ hổng kiểm chứng: 10 test cũ đọc thẳng property + soi
   chuỗi .xaml, xoá ràng buộc `Visibility` đi thì chúng vẫn xanh — nay đã có test đo thật.
2. **Toàn bộ phần JS KHÔNG có test tự động** (repo không có hạ tầng test JS). Các hạng mục 2·3·4·5·7 mới chỉ
   được kiểm bằng `node --check` + đọc lại luồng; rig test chỉ khoá phía C#. Ràng buộc "bước 4 hỏng không chạy
   tới bước 9" hiện được chứng minh bằng **đọc luồng**: 8/8 bước cốt lõi (1–8) đều có chốt
   `if (!bN.ok) { return ketThuc(...); }` ngay sau lượt `await buoc(...)` (đếm bằng grep = 8), còn bước 9 là
   bước cuối và chính `ok` của nó quyết định `daTao`. **Cộng** rig ca (c) khoá phía tiêu thụ.
3. **Hạng mục 3 (chuyển tháng) là DOM CHƯA DÒ THẬT.** Selector tiêu đề tháng-năm và nút mũi tên viết phòng thủ
   theo class (`prev|left` / `next|right`, loại `year|double|super`); không thấy ⇒ bước HỎNG kèm danh sách class
   đã thấy, KHÔNG click bừa. **Phải chạy một lượt "Dò DOM KM" thật trước khi tin nhánh này.** Ca dễ vỡ nhất đã
   lường trước: EDS tách tháng và năm thành hai thẻ — đã xử lý bằng cách duyệt ứng viên từ nhỏ tới lớn và lấy
   khối đầu tiên tách được CẢ hai vế.
4. **Câu log của `MuonHoSoAsync` (hạng mục 14) KHÔNG có test riêng.** Lớp `PhienGia` (seam để có phiên chạy giả)
   là private trong `MoListShopTests` — dựng bản sao chỉ để khoá một chuỗi chữ là không đáng. Test cũ
   `Assert.Contains("Tạm dừng check đơn")` vẫn xanh.

### Phát hiện NGOÀI phạm vi — ghi lại, KHÔNG tự sửa

1. **`ChoChang.KmDump` vẫn 240s** trong khi nút "Dò DOM KM" chạy CHÍNH chuỗi 9 bước đã được tính lại ngân sách
   ~900s. Hệ quả: lượt dò trên shop nhiều sản phẩm sẽ hết hạn chờ giữa chừng và trả về "hết thời gian chờ" thay
   vì bản dump — đúng lúc người ta cần nó nhất (Shopee đổi DOM). Đề nghị nâng bằng cùng hằng số ở đợt sau.
2. **Extension trong repo nay LỆCH bản publish** (`publish\ShopeeSuite\extensions\`). App đi ngược từ thư mục
   exe lên nên **sửa trong repo KHÔNG có tác dụng với bản đã publish** (bẫy plan cũ §8 mục 4). Chưa đồng bộ —
   ngoài phạm vi theo mục 2 của plan này.
3. **Chưa commit, chưa push, chưa phát hành, chưa đồng bộ publish** — đúng ràng buộc.
4. **Chưa chạy thật lần nào**: vòng 12 shop, lượt tự động 2–6h, shop rỗng thật, và nhánh chuyển tháng đều mới chỉ
   có test/đọc code.

---

## ĐỢT 2 — phát hiện của nghiệm thu + phản biện vòng 2 (19/08 đêm)

Vòng kiểm độc lập trên bản sửa đợt 1: nghiệm thu chấm **8/9 ĐẠT** (ô thử-phá là claim chưa kiểm độc lập);
phản biện tìm ra **2 lỗi nặng + 5 trung bình** (một lỗi TRUNG BÌNH do chính bản vá đợt 1 đẻ ra) + 2 nghi vấn.
Phiên chính đã tự xác minh ba phát hiện nặng nhất bằng đọc code. Các hạng mục 16–24 dưới đây dập nốt.

### Hạng mục đợt 2

16. **Bước 6 thiếu phép đọc-lại "tick HẾT trang"** (`flow-km.js` vòng lật trang ~:977–1020). Hiện `docLai`
    chỉ là "form có ≥1 sản phẩm", còn `ghiChu` nói dối rằng có so `daChon` cộng dồn giữa các trang — phép so đó
    KHÔNG tồn tại. Sửa: (a) so `daChon` trang N với trang N−1 — số PHẢI TĂNG; không tăng ⇒ đẩy vào `canhBao`
    (kèm trang nào) — đừng hạ `ok` cả bước vì trang cuối có thể trùng số do SP đã nằm chương trình khác, nhưng
    người dùng phải THẤY cảnh báo; (b) `pageClickCheckAll` (~:416) hiện `document.querySelector("thead")` KHÔNG
    scope trong modal — neo vào modal `.select-tips` như `pageDoModalSanPhamDangMo`; (c) `ghiChu` phải mô tả
    đúng phép đo THẬT.

17. **Captcha ở nhánh bước-HỎNG không được gửi** — chốt `/verify` chỉ nằm ở prologue của `buoc()`
    (`flow-km.js:687-698`), mà bước hỏng thì `return ketThuc()` ngay ⇒ không còn bước kế nào chạy prologue ⇒
    `send({action:"captcha"})` không bao giờ bắn cho ca captcha-giữa-bước (ca phổ biến nhất: bước 6 dài ~12').
    Sửa: khi một bước ra `ok:false`, kiểm URL `/verify` MỘT LẦN NỮA trước khi kết thúc (trong `buoc()` nhánh
    !ok, hoặc trong `ketThuc()`); dính thì gửi captcha + cờ `captcha:true` trong gói kết quả.

18. **`tabId == null` bị trả `boQua`** (`flow-km.js:815-817`) ⇒ C# đếm vào `SoBoQua` và câu tổng kết nói
    "bỏ qua N shop không có sản phẩm" — hỏng hạ tầng (SW chết giữa chừng) bị nguỵ trang thành kết luận nghiệp
    vụ. Sửa: ca này trả lỗi (`buocHong`/`loi`), C# xếp vào `SoHong`. `boQua` từ nay CHỈ dành cho "shop rỗng".

19. **Cấm default-ngầm cho tham số nghiệp vụ khi CHẠY THẬT.** `ten`/`gioBatDau`/`gioKetThuc`/`ngày-tháng-năm`
    (`flow-km.js:826-832`) vẫn `||`-default như `phanTram` trước đợt 1, và `kyVong` bước 4/5 dựng từ chính biến
    đã bị thay ⇒ tự nhất quán, không thước nào bắt được. Sửa: khi `ts != null` (đường `chayKm`), trường thiếu /
    không parse được ⇒ bước tương ứng HỎNG nêu rõ trường nào; default chỉ còn cho đường dò (`ts == null`).

20. **Lượt TỰ ĐỘNG phải có hạn chót 06:00.** `DenGioChay` cho khởi động tới 05:59 mà vòng shop không có mốc
    dừng; với hạn chặng 900s + nghỉ 3–5', ca xấu chạy tới ~09:45, check đơn chết suốt buổi sáng. Sửa:
    `ChayKmMoiShopAsync` nhận hạn chót tùy chọn (`DateTime? hanChot`); tới hạn thì KHÔNG bắt đầu shop mới,
    kết quả ghi rõ "còn N shop chưa chạy vì hết khung"; đường tự động truyền 06:00 cùng ngày, đường bấm tay
    truyền null. Hàm thuần quyết định "còn được bắt đầu shop mới không" đặt ở `KmLichTuDong` + test ma trận.

21. **Đường tự động không validate cấu hình trước khi ĐỐT lượt.** Ca thật: `GioBatDau = "01:00"` hợp lệ với
    form ⇒ lượt 02:05 gửi mốc quá khứ ⇒ 12 shop đỏ ⇒ và vì `LanThuTuDong` đã ghi, đêm nào cũng lặp — vĩnh viễn,
    không dòng log nào nói lý do. Sửa: `NhipLichKmAsync` kiểm cấu hình (dùng lại luật thuần của
    `KmDialogViewModel` — giờ hợp lệ, kết thúc > bắt đầu, mốc `bayGio.Date + GioBatDau` còn ở TƯƠNG LAI so
    `bayGio`) TRƯỚC khi ghi `LanThuTuDong`; không đạt ⇒ ghi MỘT dòng nhật ký nói rõ vì sao + vẫn ghi
    `LanThuTuDong` (tránh spam 60s), KHÔNG phóng trình duyệt.

22. **`finally` đợt 1 GIẾT nhầm cửa sổ "Mở list shop" của người dùng khi mượn hồ sơ THẤT BẠI.**
    `MuonHoSoAsync` false ⇒ `return` sớm (`Phien.cs:1070-1073`) nhưng `finally` vẫn `DongCuaSoCuLai()`
    (`:1152-1155`) — kill cửa sổ đang mở dở của lượt trước. Sửa: cờ "lượt này ĐÃ mượn được hồ sơ" đặt sau khi
    `MuonHoSoAsync` true; `finally` chỉ đóng cửa sổ khi cờ bật. Thêm test ca mượn-thất-bại vào
    `KmTraHoSoKhiNemTests` (khuôn sẵn có).

23. **`pageBamChuyenThang` có thể click nhầm Ô NGÀY tháng liền kề** — ứng viên lọc bằng
    `button, a, i, span, div` + class `/prev|next/` mà ô ngày ngoài tháng mang class `prev-month`/`next-month`.
    Sửa: loại mọi ứng viên `e.closest(".eds-date-table, .eds-date-table__rows")`.

24. **Vặt:** (a) `ChoChang.KmDump` 240s → cùng ngân sách 900s với `KmChay` (nút dò chạy CHÍNH chuỗi 9 bước);
    (b) xmldoc `DoDomKmAsync` (`Phien.cs:947-958`) còn "KHÔNG tạo chương trình khuyến mãi nào" + hai
    `<summary>` liên tiếp — sửa cho đúng sự thật và đúng cấu trúc; (c) `OrdersBridgeSession.cs:577`
    `ToString("dd-MM-yyyy")` thêm `CultureInfo.InvariantCulture` cho khớp toàn cục; (d) xoá page-func chết
    trong `flow-km.js` (`pageTickChonTatCaTrongForm`, `pageGoVaoInput`, `pageDumpDuongVaoKm`) — KIỂM bằng grep
    trước khi xoá từng cái; (e) thêm test biên `HoanVongCheckDon(05:59) ⇒ 06:00`; (f) `CauTongKetKm` không
    khẳng định "check đơn chạy lại" khi không có phiên nào để chạy lại — câu theo dữ kiện.

### Tiêu chí nghiệm thu đợt 2

- [ ] Build sln rebuild 0 warning; toàn bộ test xanh (ghi số).
- [ ] `node --check` hai file JS.
- [ ] Hạng mục 17: đọc luồng chứng minh ca "captcha giữa bước 6, bước đỏ" có gửi `{action:"captcha"}`;
      rig/parse test phía C# nhận cờ captcha trong gói kết quả nếu có thay đổi định dạng gói.
- [ ] Hạng mục 18: rig test ca "tabId null" (hoặc parse test) khẳng định shop vào `SoHong`, KHÔNG vào `SoBoQua`.
- [ ] Hạng mục 20: test hàm thuần hạn chót (trước 06:00 cho bắt đầu, đúng/qua 06:00 thì không) + test vòng
      (rig hoặc VM) "hết khung giữa vòng ⇒ shop sau không chạy, câu tổng kết nói rõ".
- [ ] Hạng mục 21: test "cấu hình giờ 01:00, nhịp 02:05 ⇒ không phóng, có log lý do, LanThuTuDong vẫn ghi".
- [ ] Hạng mục 22: test ca mượn-thất-bại — cửa sổ cũ KHÔNG bị đóng.
- [ ] Mọi test mới THỬ PHÁ (ghi bảng như đợt 1).
- [ ] Không đụng file ngoài phạm vi (như mục 2 đợt 1).

---

## Báo cáo thực thi ĐỢT 2 (Opus, 2026-08-19 đêm)

### Kết quả kiểm chứng (số THẬT, đo trên cây làm việc HIỆN TẠI — cây vẫn có việc khác đang dở)

| Việc | Kết quả |
|---|---|
| `dotnet build ShopeeSuite.sln -t:Rebuild` | **0 warning, 0 error** |
| `orders/XuLyDonShopee.Tests` | **1975/1975 xanh** (4 phút 25) — trước đợt 2 là 1954 ⇒ **+21 ca** |
| `suite/Shopee.Core.Tests` | **168/168 xanh** |
| `suite/Shopee.Module.Search.Tests` | **34/34 xanh** |
| `suite/Shopee.Suite.Tests` | **2/2 xanh** |
| `node --check flow-km.js` + `background.js` | cú pháp hợp lệ |

⚠ `dotnet` trên PATH vẫn "No SDKs found"; mọi lệnh chạy bằng
`C:\Users\Ng Xuan Mui\AppData\Local\Microsoft\dotnet\dotnet.exe` (SDK 8.0.423).

### Đã làm theo từng hạng mục

16. **Bước 6 có phép đọc-lại "tick HẾT trang" THẬT.** (a) Thêm hàm thuần `soDaChon(text)` + vòng lật trang nay
    so số "Đã chọn" trang N với N−1: không tăng ⇒ đẩy vào `canhBao` **kèm số trang**, KHÔNG hạ `ok` cả bước
    (đúng spec — trang cuối có thể trùng số vì SP đã nằm chương trình khác). Thêm hai ca cảnh báo nữa mà phép
    so ban đầu không phủ: trang 1 mà số vẫn 0 (tick không ăn ngay từ đầu), và không đọc nổi ô đếm.
    (b) `pageClickCheckAll` nay NEO modal `.select-tips` (không thấy modal ⇒ trả lỗi, không nới ra ngoài).
    (c) `pageDocDaChon` cũng neo modal — bản cũ quét `div,span,p` toàn trang tìm chữ "đã chọn", mà trang này còn
    khối "0 sản phẩm đã chọn" của Thiết lập hàng loạt, tức phép so hoàn toàn có thể đọc trúng số chẳng liên quan.
    (d) `ghiChu` viết lại mô tả ĐÚNG phép đo thật (kỳ vọng của cả bước vs. đọc-lại của từng trang).
17. **Captcha ở nhánh bước-HỎNG.** `buoc()` có CHỐT THỨ HAI: bước ra `ok:false` thì hỏi lại URL tab, dính
    `/verify` ⇒ `ban.captcha = true` + `send({action:"captcha"})` + nối lý do vào `loi`, rồi mới `ds.push`.
    Gói kết quả mang thêm cờ `captcha` (suy từ chính các bước, không thể lệch với thứ đã gửi). Phía C#:
    `KetQuaKmMotShop.Captcha` + vòng shop kiểm `channel.CaptchaSeen || mot.Captcha`.
18. **`tabId == null` ⇒ HỎNG, không còn `boQua`.** Đẩy một bước giả `"0. Tab shop"` (`ok:false`) vào danh sách
    rồi `ketThuc()` — C# đọc `buocHong` ra `Loi` ⇒ vào `SoHong`. `boQua` từ nay CHỈ dành cho shop rỗng thật.
19. **Cấm default-ngầm cho tham số nghiệp vụ.** Hàm thuần mới `docThamSoKm(ts)` gom toàn bộ đọc + kiểm:
    `ten` · ngày/tháng/năm · `ngayDayDu` · `gioBatDau` · `gioKetThuc` · `phanTram`; trả giá trị chuẩn hoá kèm
    `loi.<nhóm>`. Bước 3 hỏng khi thiếu `ten`; bước 4 khi hỏng ngày hoặc `gioBatDau`; bước 5 khi hỏng ngày hoặc
    `gioKetThuc`; bước 8 giữ nguyên luật `%`. **Thêm một chốt spec không yêu cầu nhưng cùng họ:** đối chiếu
    `ngayDayDu` với ngày/tháng/năm rời — hai vế lệch nhau thì mọi bước vẫn xanh mà chương trình sai ngày, vì một
    vế dùng để CLICK còn vế kia là chuỗi đọc-lại. Mặc định chỉ còn ở đường `ts == null` (gọi thẳng, không tham
    số) → `thamSoLuotDo()`.
20. **Hạn chót 06:00 cho lượt tự động.** Hai hàm thuần ở `KmLichTuDong`: `HanChotLuotTuDong(bayGio)` và
    `ConDuocBatDauShopMoi(bayGio, hanChot)` (null = bấm tay ⇒ luôn được). `ChayKmSauPickerAsync` nhận
    `hanChot` + `dongHo`, kiểm TRƯỚC MỖI shop (kể cả shop đầu — lượt 05:59 có thể mất vài phút cho SSO), tới hạn
    thì trả `KetQuaKm(HetKhung: true, SoShopChuaChay: n)`. **Tách khỏi `Loi`** vì hết khung không phải lỗi:
    gộp vào là câu tổng kết nói "Dừng giữa chừng: …" cho một chuyện hoàn toàn bình thường.
    `ChayKmMoiShopAsync` + `ChayKmAsync` truyền tiếp; đường tự động truyền 06:00 cùng ngày, bấm tay truyền null.
21. **Đường tự động validate cấu hình TRƯỚC khi đốt lượt.** `NhipLichKmAsync` dùng LẠI luật thuần của form
    (`KmDialogViewModel.KiemTraForm` + `KiemTraMocTuongLai`) — không đẻ bộ luật thứ hai. Không đạt ⇒ ghi
    `LanThuTuDong` (chặn spam nhịp 60s) + MỘT dòng nhật ký nói rõ lý do + `continue` (tài khoản khác vẫn được
    xét), KHÔNG phóng trình duyệt.
22. **`finally` không còn giết nhầm cửa sổ "Mở list shop".** Cờ `daMuonDuocHoSo` đặt NGAY SAU khi
    `MuonHoSoAsync` trả true; khối đóng cửa sổ nay là `if (dongCuaSoKhiXong && daMuonDuocHoSo)`.
23. **`pageBamChuyenThang` loại ứng viên trong bảng ngày** — thêm `e.closest(".eds-date-table, .eds-date-table__rows")`
    vào bộ lọc (ô ngày tháng liền kề mang đúng class `prev-month`/`next-month` và text ngắn ≤ 4 ký tự nên lọt
    thẳng qua bộ lọc cũ).
24. **Vặt:** (a) `ChoChang.KmDump` 240s → **900s**, hai xmldoc trỏ vào nhau + test khoá `KmDump == KmChay`;
    (b) xmldoc `DoDomKmAsync` viết lại (bỏ "KHÔNG tạo chương trình nào" — sai; gộp hai `<summary>` liên tiếp và
    trả đoạn xmldoc bị lạc chỗ về đúng field `_viTriShopKm`); (c) `ToString("dd-MM-yyyy", InvariantCulture)` cho
    cả `ngayDayDu` gửi extension lẫn câu log mở vòng, kèm `using System.Globalization`; (d) xoá 3 page-func chết
    `pageTickChonTatCaTrongForm`, `pageGoVaoInput`, `pageDumpDuongVaoKm` — **grep xác nhận từng cái chỉ có đúng
    một dòng định nghĩa, 0 chỗ gọi**, trước khi xoá (ba hàm còn lại trong nghi vấn — `pageDumpModalSanPham`,
    `pageDoBangTrongForm`, `pageDumpDongSanPham` — CÓ chỗ gọi nên giữ); (e) test biên
    `HoanVongCheckDon(05:59) ⇒ 06:00`; (f) `CauTongKetKm` nhận `coPhienChayLai`, không có phiên nào thì câu chốt
    là "Cửa sổ đã đóng (không có phiên check đơn nào đang chạy để bật lại)". `CauTrangThaiKm` cũng nói hết khung.

### File đã sửa / tạo (đợt 2)

**Extension**
- `extensions/shopee-orders/flow-km.js` — xoá 3 page-func chết; `pageDocDaChon` + `pageClickCheckAll` neo modal;
  `pageBamChuyenThang` loại ô ngày; hai hàm thuần mới `soDaChon`, `docThamSoKm`; `buoc()` chốt captcha lần hai;
  `ketThuc()` mang cờ `captcha`; nhánh `tabId == null` thành bước hỏng; guard tham số ở bước 3/4/5; vòng lật
  trang có phép so `daChon`; `doChayKm`/`doDumpKm` gửi kèm `captcha`.

**Core**
- `Services/KmLichTuDong.cs` — `HanChotLuotTuDong`, `ConDuocBatDauShopMoi`.
- `Services/OrdersBridgeSession.cs` — `KetQuaKmMotShop.Captcha`; `KetQuaKm.HetKhung` + `SoShopChuaChay`;
  `ChayKmMoiShopAsync(…, hanChot, ct)`; `ChayKmSauPickerAsync(…, hanChot, dongHo, ct)` + chốt hạn chót mỗi shop;
  đọc `captcha` trong gói; `InvariantCulture`; `using System.Globalization`.
- `Services/OrdersBridgeChannel.cs` — `ChoChang.KmDump` 240s → 900s + xmldoc mới.

**App**
- `ViewModels/AccountsViewModel.Phien.cs` — `NhipLichKmAsync` validate cấu hình + truyền hạn chót;
  `ChayKmAsync(…, hanChot)`; `CauTongKetKm(kq, coPhienChayLai)` + nhánh `HetKhung`; `CauTrangThaiKm` nhánh
  `HetKhung`; cờ `daMuonDuocHoSo` trong `ChayLatCatAsync`; xmldoc `DoDomKmAsync` + `_viTriShopKm`.

**Test** (mới: 1 file · sửa: 4 file — **+21 ca**)
- MỚI `KmNhipTuDongTests.cs` (3).
- SỬA `KmVongRigTests.cs` (+4: hết khung giữa vòng · đối chứng không hạn chót · gói "không có tab shop" ·
  gói tự khai captcha), `KmVongMoiShopTests.cs` (+5, sửa 7 chỗ gọi `CauTongKetKm`),
  `KmLichTuDongTests.cs` (+4 test = 8 ca do một Theory 5 dòng), `KmTraHoSoKhiNemTests.cs` (+1).

### THỬ PHÁ — 13 ca, ĐỎ cả 13 (mỗi ca: sửa ngược → chạy → khôi phục → xanh lại)

Mọi lượt phá/khôi phục bằng Edit/`sed` ghi thẳng file (timestamp mới ⇒ MSBuild build lại thật) — KHÔNG `Copy-Item`.

| # | Phá gì | Test ĐỎ |
|---|---|---|
| 1 | `CauTongKetKm`: bỏ nhánh `HetKhung` | `TongKet_HetKhung_NoiRoConBaoNhieuShop_VaKHONG_goi_la_loi` |
| 2 | `CauTongKetKm`: bỏ qua `coPhienChayLai` (luôn hứa "chạy lại") | `TongKet_KHONG_co_phien_nao_ThiKHONG_hua_chay_lai` |
| 3 | `DocKetQuaKmMotShop`: không đọc cờ `captcha` trong gói | `DocKetQua_GoiTuKhaiCaptcha…` + `GoiKetQuaTuKhaiCaptcha_ThiDUNG_CA_VONG` (2) |
| 4 | Bỏ chốt hạn chót trong vòng shop | `HetKhungGiuaVong_ThiKHONG_bat_dau_shop_moi…` |
| 5 | `ConDuocBatDauShopMoi`: `<` → `<=` (lệch biên 06:00) | `ConDuocBatDauShopMoi_DungHaiMocBien(6, 0, False)` |
| 6 | `ConDuocBatDauShopMoi`: `hanChot == null` ⇒ false | `ConDuocBatDauShopMoi_KhongCoHanChot…` + 8 test rig (9) |
| 7 | `HanChotLuotTuDong` dùng `GioMoKhung` | `HanChotLuotTuDong_La_0600_CungNgay` + `CauHinhHopLe_ThiVAN_bat_dau_luot` (2) |
| 8 | `TrongBienAn` hạ trần trên một tiếng | `HoanVongCheckDon_Moc0559_ThiDoiToi_0600` |
| 9 | `NhipLichKmAsync`: bỏ lượt validate cấu hình | `CauHinhMocQuaKhu…` + `CauHinhGioKetThucSaiThiCungBiChan` (2) |
| 10 | Nhánh cấu hình hỏng KHÔNG ghi `LanThuTuDong` | `CauHinhMocQuaKhu_ThiKHONG_phong_NhungVAN_ghi_LanThuTuDong` |
| 11 | Bỏ cờ `daMuonDuocHoSo` (đóng cửa sổ vô điều kiện) | `MuonHoSoTHAT_BAI_ThiKHONG_giet_cua_so_cua_luot_truoc` |
| 12 | `DocKetQuaKmMotShop`: biến "không tạo được" thành `boQua` | `DocKetQua_KhongCoTabShop_LaHONG…`, `GoiKhongCoTabShop_ThiVaoSoHong…` + 4 test cũ (6) |
| 13 | `ChoChang.KmDump` về lại 240s | `HanChang_KmDump_BangVoi_KmChay` |

Ca 6 và 12 kéo đỏ cả loạt test CŨ — đó là dấu hiệu tốt: chúng cho thấy hai chốt này nằm trên đường đi của mọi ca.

### ĐIỂM LỆCH so với spec — đọc kỹ

1. **Hạng mục 16·17·18·19·23·24d là JS, KHÔNG có test tự động** (repo vẫn không có hạ tầng test JS). Kiểm bằng
   `node --check` + đọc lại luồng + grep. Phần khoá được bằng test C# là phía TIÊU THỤ: gói "không có tab shop"
   (hạng mục 18) và gói tự khai `captcha` (hạng mục 17) đều có rig test khẳng định C# xử đúng — nhưng **việc
   extension có SINH ra đúng gói đó hay không thì chỉ có đọc code**.
2. **Hạng mục 17, đọc luồng chứng minh** (theo tiêu chí nghiệm thu): ca "captcha giữa bước 6, bước đỏ" đi đường
   `buoc()` → khối `if (!ban.ok && tabId != null && !ban.captcha)` ở ngay TRƯỚC `ds.push(ban)` → hỏi
   `urlCuaTab(tabId)` → khớp `/\/verify/i` → `send({action:"captcha"})` **và** `ban.captcha = true`. Vòng gọi
   `if (!b6.ok) return ketThuc()`, mà `ketThuc` tính `captcha: ds.some(b => b.captcha === true)` ⇒ cả gói
   `kmKetQua` cũng mang cờ. Hai đường độc lập, mất một đường vẫn còn đường kia — điều kiện `!ban.captcha` chặn
   gửi hai lần khi prologue đã bắt được.
3. **Hạng mục 20: hàm thuần đặt ở `KmLichTuDong` đúng spec**, nhưng phần "câu tổng kết nói rõ" tôi KHÔNG nhét
   vào `KetQuaKm.Loi` mà thêm hai trường `HetKhung` + `SoShopChuaChay`. Lý do: dùng `Loi` thì câu tổng kết in
   "Dừng giữa chừng: …" cho một chuyện bình thường, và người đọc log sáng hôm sau sẽ đi tìm một sự cố không hề
   xảy ra — đúng loại lỗi câu chữ mà hạng mục 24f đang sửa.
4. **Hạng mục 21: dùng `continue` chứ không `return`** khi cấu hình hỏng. Spec không nói; tôi chọn `continue` vì
   nhánh này không phóng gì nên không có lý do bắt các tài khoản còn lại đợi thêm một nhịp 60 giây.
5. **Hạng mục 19 làm RỘNG HƠN spec một chốt:** đối chiếu `ngayDayDu` với ngày/tháng/năm rời. Spec chỉ đòi "thiếu
   / không parse được ⇒ hỏng". Nhưng hai trường này đến từ cùng một nguồn C# mà đi vào hai chỗ khác nhau (một để
   click, một để đọc-lại), nên lệch nhau là đúng cái bẫy "tự nhất quán" mà hạng mục 19 sinh ra để chặn.
6. **Test hạng mục 21 KHÔNG chứng minh trực tiếp "không phóng trình duyệt"** — nó chứng minh bằng gián tiếp:
   dòng nhật ký "bắt đầu lượt KM" (ghi ngay trước `ChayKmAsync`) KHÔNG xuất hiện. Vế đối chứng
   `CauHinhHopLe_ThiVAN_bat_dau_luot` khoá chiều ngược lại. Cả hai chạy an toàn vì hook
   `AcquireAccountLease` được cho trả "máy khác đang giữ" ⇒ mọi lượt dừng ở cửa, không tiến trình Brave nào.

### Phát hiện NGOÀI phạm vi — ghi lại, KHÔNG tự sửa

1. **`cauBatDau` của "Dò DOM KM" vẫn ghi "(TẠM — pha 1)"** trong khi pha 3 đã xong và nút này nay là công cụ
   chẩn đoán thường trực. Câu đó không sai sự thật nguy hiểm (nó ĐÃ nói rõ "bước cuối TẠO CHƯƠNG TRÌNH THẬT")
   nên tôi để nguyên — hạng mục 24b chỉ nêu xmldoc.
2. **Extension trong repo vẫn LỆCH bản publish** (`publish\ShopeeSuite\extensions\`) — đợt này lệch thêm. App đi
   ngược từ thư mục exe lên nên sửa trong repo KHÔNG có tác dụng với bản đã publish. Chưa đồng bộ (ngoài phạm vi).
3. **Chưa commit, chưa push, chưa phát hành, chưa đồng bộ publish** — đúng ràng buộc.
4. **Chưa chạy thật lần nào.** Riêng ba nhánh sau vẫn CHƯA có bằng chứng chạy thật và đều là DOM chưa dò:
   chuyển tháng (hạng mục 3 đợt 1 + 23 đợt 2), phép so `daChon` giữa các trang (16a), và `pageClickCheckAll`
   sau khi neo modal (16b). Một lượt "Dò DOM KM" thật trên shop nhiều trang là thứ rẻ nhất kiểm được cả ba.

---

## ĐỢT 3 — phát hiện của phản biện vòng 3 (soi trên bản đợt 2)

Nghiệm thu đợt 2: **ĐẠT 8/9**. Phản biện vòng 3: 1 NẶNG + 2 TRUNG BÌNH + vặt. Hạng mục 25–30 dập nốt.
Người dùng đã chốt từ đầu việc: sửa toàn bộ phát hiện của chuỗi review này.

25. **Phân trang modal gãy giữa chừng vẫn IM LẶNG** (`flow-km.js:1084-1093`): hai lối `break` (click trang
    hỏng · vân tay bảng không đổi) chỉ ghi `nhatKyTrang` — thứ C# không bao giờ đọc ở lệnh `chayKm` ⇒ shop 240
    SP tạo chương trình với 10 SP, không một cảnh báo. Sửa: (a) neo `pageClickTrang` (:409) và `pageVanTayBang`
    (:419) vào modal `.select-tips` như `pageClickCheckAll`/`pageDocDaChon` — lý do y hệt xmldoc của chính
    `pageClickCheckAll`; (b) cả hai lối `break` phải `canhBao.push("dừng ở trang N/M — <lý do>")`;
    (c) dùng `soTrang` mà `pageDoModalSanPhamDangMo` (:639) đã trả: đi xong vòng mà `soTrangDaDi < soTrang`
    ⇒ cảnh báo "còn trang chưa duyệt".

26. **`LanChayCuoi` ghi cả khi HẾT KHUNG ⇒ câu "để đêm sau" nói sai + shop cuối danh sách đói vĩnh viễn.**
    (`Phien.cs` `than` của `ChayKmAsync`: `if (kq.SoTao > 0) Save(LanChayCuoi=now)` — với `SoNgay=7`, lượt
    3/12 shop hết khung nghĩa là 9 shop còn lại chờ 7 ngày, trái câu log.) Sửa: **hết khung thì KHÔNG ghi
    `LanChayCuoi`** (`kq.HetKhung == true` ⇒ bỏ Save) ⇒ đêm sau `DenGioChay` lại tới hạn, chạy lại từ đầu —
    các shop đã tạo hôm trước tạo thêm chương trình cho NGÀY MỚI (khác ngày, Shopee không coi là trùng khung
    giờ), các shop chưa chạy được chạy. Câu log "để đêm sau" giữ nguyên — nay thành đúng. Test khoá cả hai
    chiều (HetKhung ⇒ không ghi; chạy trọn ⇒ ghi).

27. **`canhBao` không trần** — chuỗi `.select-tips` đổi format là 104 dòng cảnh báo/shop dồn vào MỘT dòng nhật
    ký (~9 KB), dìm chết cảnh báo thật. Sửa: gom cảnh báo CÙNG LOẠI thành một mục có đếm ("N/105 trang: số đã
    chọn không tăng"), trần tổng số phần tử `canhBao` (hằng có tên, vd 10, kèm "+ k cảnh báo nữa").

28. **Lối `boQua` thứ hai (`tao=false`, `flow-km.js:1185-1187`) là nhánh CHẾT** (cả hai caller gửi `tao:true`)
    nhưng nếu sống lại sẽ bị tổng kết nguỵ trang thành "shop không có sản phẩm". Sửa: XOÁ nhánh + cờ
    `tao`/`choPhepTao` (YAGNI, git giữ lịch sử); comment ở :890 ("boQua chỉ dành cho shop rỗng") nhờ đó thành đúng.

29. **Tham số hỏng phải FAIL NGAY ĐẦU, không phải ở bước 5/8.** `docThamSoKm` biết `loi.*` từ trước khi đụng
    trang, nhưng bước 8 mới báo `loi.phanTram` ⇒ shop vẫn lật 105 trang (~12') và bỏ lại form dở trên Shopee.
    Sửa: đầu `chayKmChoTabDangMo`, có bất kỳ `loi.*` ⇒ đẩy bước giả `"0. Tham số"` (khuôn `"0. Tab shop"` của
    hạng mục 18) nêu đủ các trường hỏng, kết thúc luôn. Bỏ các chốt lẻ ở bước 5/8 nếu thành thừa.

30. **Vặt:** (a) banner sai sự thật còn sót `Phien.cs:906-912` ("PHA 1 … Extension KHÔNG tạo chương trình
    nào") — viết lại đúng hiện trạng; (b) `CauTongKetKm` khi hết khung ở shop đầu ra "tạo 0/0 shop … còn 12
    shop chưa chạy" — mẫu số phải là tổng shop của lượt, không phải `Shops.Count`; (c) dòng DB cũ
    `so_ngay = 0` nay bị hạng mục 21 chặn mỗi đêm một dòng log — `KmConfigRepository.Get` chuẩn hoá
    `SoNgay < 1 ⇒ 1` khi đọc (khớp `Math.Max` của `KmLichTuDong`), có test.

**KHÔNG sửa mù các nghi vấn DOM-chưa-dò** (bộ lọc `.eds-date-table` của nút chuyển tháng, cách rút tháng từ
tiêu đề picker, nhánh cờ `captcha` trong gói kết quả gần như không dùng vì `TrySetResult(null)` thắng trước):
chờ một lượt "Dò DOM KM" thật. Khe hở pre-existing `MuonHoSoAsync` nhả khoá lượt trước TRƯỚC khi xin khoá mới
(`Phien.cs:374-377`) — ngoài phạm vi, ghi nhận.

### Tiêu chí nghiệm thu đợt 3

- [ ] Build sln rebuild 0 warning; toàn bộ test xanh (ghi số); `node --check` hai file JS.
- [ ] Đọc luồng: hai lối break phân trang đều đẻ `canhBao`; `pageClickTrang`/`pageVanTayBang` neo modal.
- [ ] Test hạng mục 26 hai chiều (HetKhung ⇒ không ghi `LanChayCuoi`; trọn vòng ⇒ ghi).
- [ ] Grep `choPhepTao|"tao"` trong flow-km.js + caller C#: nhánh đã xoá sạch, không còn tham chiếu.
- [ ] Test/đọc luồng hạng mục 29: `phanTram` hỏng ⇒ bước "0. Tham số", KHÔNG có lệnh nào đụng trang.
- [ ] Test hạng mục 30c (`so_ngay = 0` đọc ra 1).
- [ ] Mọi test mới THỬ PHÁ, ghi bảng.

## Báo cáo thực thi ĐỢT 3 (Opus, 2026-08-19/20)

### Kết quả kiểm chứng (số THẬT, đo trên cây làm việc HIỆN TẠI — cây vẫn có việc khác đang dở)

| Việc | Kết quả |
|---|---|
| `dotnet build ShopeeSuite.sln -t:Rebuild` | **0 warning, 0 error** |
| `orders/XuLyDonShopee.Tests` | **1982/1982 xanh** (4 phút 26) — trước đợt 3 là 1975 ⇒ **+7 ca** |
| `suite/Shopee.Core.Tests` | **168/168 xanh** |
| `suite/Shopee.Module.Search.Tests` | **34/34 xanh** |
| `suite/Shopee.Suite.Tests` | **2/2 xanh** |
| `node --check flow-km.js` + `background.js` | cú pháp hợp lệ |
| `grep "choPhepTao|tao:"` trong `flow-km.js` + payload C# | **0 hit** (chỉ còn một comment mô tả việc đã xoá, KHÔNG dùng lại tên biến cũ) |

⚠ `dotnet` trên PATH vẫn "No SDKs found"; mọi lệnh chạy bằng
`C:\Users\Ng Xuan Mui\AppData\Local\Microsoft\dotnet\dotnet.exe` (SDK 8.0.423).

### Đã làm theo từng hạng mục

25. **Phân trang modal gãy giữa chừng KHÔNG còn im lặng.**
    (a) `pageClickTrang` và `pageVanTayBang` nay NEO modal `.select-tips` (cùng khuôn `pageClickCheckAll`);
    `pageClickTrang` không thấy modal ⇒ lỗi, KHÔNG bấm pager của bảng ngoài; `pageVanTayBang` mất modal ⇒ trả
    chuỗi mốc `"(KHÔNG THẤY MODAL)"` để vòng phân biệt được với "bảng thật không đổi".
    (b) Lối `break` "bảng không đổi" LUÔN đẻ cảnh báo `dừng ở trang N/M — <lý do>`. Lối `break` "click trang
    trượt" đẻ cảnh báo khi lý do KHÔNG phải "pager hết nhãn" (cờ mới `khongThayTrang`) — xem ĐIỂM LỆCH #1.
    (c) Sau vòng: `soTrangDaDi < soTrangThayDuoc` ⇒ cảnh báo "mới duyệt N/M trang — sản phẩm ở các trang còn
    lại KHÔNG được chọn". `soTrangThayDuoc` lấy từ NHÃN SỐ pager (`soTrangLonNhat`, hàm thuần mới), không lấy
    `doModal.soTrang` — xem ĐIỂM LỆCH #2. `sau` của bước 6 nay mang thêm `soTrangThayDuoc` + `lyDoDung`.
26. **Hết khung ⇒ KHÔNG ghi `LanChayCuoi`.** Tách hàm `AccountsViewModel.GhiMocSauLuotKm(accountId, kq, luc)`
    (điều kiện: `SoTao > 0` **và** `!HetKhung`), `than` của `ChayKmAsync` gọi nó thay cho khối `Save` cũ.
    4 test mới khoá cả hai chiều + ca "không đè mốc CŨ" + ca cũ "không tạo được gì thì không ghi".
27. **Cảnh báo có GOM + có TRẦN.** Hàm thuần mới `gomCanhBao(ds, tran)` + hằng `TRAN_CANH_BAO = 10`; mỗi lượt
    `themCanhBao(loai, cau, gom, chiTiet)` thay cho `canhBao.push(chuỗi)`. Đo thử bằng node: 104 cảnh báo
    "số đã chọn không tăng" + 1 "chạm trần" ⇒ **2 mục**, `104 lần — số "Đã chọn" KHÔNG tăng … (trang 2, trang 3,
    trang 4, trang 5, trang 6, …) · chạm trần 105 trang` (~120 ký tự thay vì ~9 KB). 15 loại khác nhau ⇒ 10 mục
    + dòng `… + 5 cảnh báo nữa (đã gom theo loại)`. Một-lần thì giữ NGUYÊN câu đầy đủ, không mất thông tin.
28. **Xoá nhánh chết `tao=false`.** Bỏ cờ trong `docThamSoKm`, biến `choPhepTao`, lối ra `boQua` trước bước 9,
    `tao: true` trong `thamSoLuotDo()` và trong payload `chayKm` của `OrdersBridgeSession`. Còn lại một comment
    giải thích vì sao xoá (cố ý KHÔNG nhắc lại tên biến cũ, để lượt grep nghiệm thu sạch).
29. **Tham số hỏng FAIL NGAY Ở CỬA.** Hàm thuần mới `cauLoiThamSo(loi)` gộp mọi trường hỏng thành một câu; đầu
    `chayKmChoTabDangMo` (sau chốt tab, TRƯỚC mọi `chay(...)`) đẩy bước giả `"0. Tham số"` rồi `ketThuc()`. Bốn
    chốt lẻ ở bước 3/4/5/8 và biến `phanTramHopLe` đã XOÁ vì thành không thể với tới.
30. **Vặt:** (a) banner `Phien.cs` viết lại — nút "Dò DOM KM" là công cụ chẩn đoán THƯỜNG TRỰC và nó **TẠO
    CHƯƠNG TRÌNH THẬT** (bản cũ khẳng định ngược); (b) `KetQuaKm.TongShop = Shops.Count + SoShopChuaChay` làm
    mẫu số cho `CauTongKetKm` **và** `CauTrangThaiKm`; (c) `KmConfigRepository.ChuanHoaSoNgay` (`< 1 ⇒ 1`) áp
    ngay lúc `Get`, khớp `Math.Max` của `KmLichTuDong`.

### File đã sửa / tạo (đợt 3)

**Extension**
- `extensions/shopee-orders/flow-km.js` — hằng `TRAN_CANH_BAO`; ba hàm thuần mới `soTrangLonNhat`,
  `gomCanhBao`, `cauLoiThamSo`; `pageClickTrang` + `pageVanTayBang` neo modal (`khongThayTrang`); bước giả
  `"0. Tham số"`; vòng lật trang có `soTrangThayDuoc`/`lyDoDung` + cảnh báo ở cả hai lối dừng + chốt "còn trang
  chưa duyệt"; `canhBaoTho`/`themCanhBao` và `ketThuc` gom cảnh báo; xoá cờ `tao` + nhánh chết + 4 chốt lẻ.

**Core**
- `Services/OrdersBridgeSession.cs` — `KetQuaKm.TongShop`; bỏ `tao = true` khỏi payload `chayKm`.
- `Data/KmConfigRepository.cs` — `ChuanHoaSoNgay` + dùng trong `Get`.

**App**
- `ViewModels/AccountsViewModel.Phien.cs` — `GhiMocSauLuotKm` (mới) + `than` gọi nó; `CauTongKetKm` /
  `CauTrangThaiKm` dùng `TongShop`; viết lại banner "PHA 1"; xmldoc `ChayKmAsync`.

**Test** (mới: 1 file · sửa: 2 file — **+7 ca**)
- MỚI `KmMocLanChayCuoiTests.cs` (4).
- SỬA `KmVongMoiShopTests.cs` (+2: gói `"0. Tham số"` ⇒ `SoHong` · mẫu số `TongShop`; và siết thêm một assert
  `tạo 1/12 shop` vào test `TongKet_HetKhung_…` sẵn có), `KmCauHinhLuuTruTests.cs` (+1: `so_ngay = 0` đọc ra 1,
  kèm vế đối chứng `so_ngay = 5` giữ nguyên).

### THỬ PHÁ — 5 ca, ĐỎ cả 5 (mỗi ca: sửa ngược → chạy → khôi phục → xanh lại)

Mọi lượt phá/khôi phục bằng công cụ Edit ghi thẳng file (timestamp mới ⇒ MSBuild build lại thật) — KHÔNG `Copy-Item`.

| # | Phá gì | Test ĐỎ |
|---|---|---|
| 1 | `DocBuocHongDau` bỏ phần chi tiết lỗi (chỉ còn tên bước) | `DocKetQua_ThamSoHong_LaHONG_VaNeuDichDanhTruong` + `DocKetQua_KhongTaoDuocVaKhongBoQua…` + `DocKetQua_DaTaoMaVanCoBuocHong…` (3) |
| 2 | `TongShop` quay lại `Shops.Count` | `TongKet_HetKhungNgayShopDau_MauSo_LaTONG_shop_cua_luot` + `TongKet_HetKhung_NoiRoConBaoNhieuShop…` (2) |
| 3 | `ChuanHoaSoNgay` thành hàm đồng nhất | `DbCu_SoNgay0_ThiDocRa1_KhongLamHongLuotKiemCauHinh` |
| 4 | `GhiMocSauLuotKm` bỏ điều kiện `HetKhung` (đúng lỗi hạng mục 26) | `HetKhung_ThiKHONG_ghi_LanChayCuoi…` + `HetKhung_ThiGiuNguyenMocCU…` (2) |
| 5 | `GhiMocSauLuotKm` KHÔNG ghi bao giờ (`|| true`) | `ChayTron_CoShopTaoDuoc_ThiGHI_LanChayCuoi` |

### Kiểm phần JS bằng CHẠY THẬT hàm thuần (repo không có hạ tầng test JS)

Trích hàm thuần từ `flow-km.js` rồi chạy bằng `node -e` (không nạp được cả module vì nó `import` API của Chrome):

- `soTrangLonNhat(["1","2","3","…","24"]) = 24`, `([]) = 0`, `(["1"]) = 1`.
- `gomCanhBao`: xem hạng mục 27 ở trên (104 → 2 mục; 15 loại → 10 mục + dòng "+5 nữa"; 1 lần thì giữ câu đầy đủ).
- `docThamSoKm` + `cauLoiThamSo`: tham số hợp lệ ⇒ `null` (luồng đi tiếp); `phanTram = 0` ⇒
  `tham số lượt KM KHÔNG dùng được — phanTram: % giảm không hợp lệ: '0' — …`; `ngayDayDu` lệch ngày rời ⇒ báo
  đúng cặp lệch; gói RỖNG ⇒ nêu ĐỦ 5 trường trong MỘT câu; đường DÒ (`ts == null`) ⇒ `null`.

**Đọc luồng (theo tiêu chí nghiệm thu):**
- Hạng mục 29: giữa `docThamSoKm(ts)` và chốt `"0. Tham số"` KHÔNG có một lượt `await chay(...)` nào — `chay`
  mới chỉ là một closure được khai báo, chưa gọi. Lối ra đi qua `ketThuc()` nên gói vẫn đủ trường, và
  `buocHong[0].ten = "0. Tham số"` ⇒ C# xếp `SoHong` (có rig/parse test khoá phía tiêu thụ).
- Hạng mục 25: hai lối `break` nay đều đi qua `themCanhBao` (một có điều kiện `!khongThayTrang`, xem ĐIỂM LỆCH
  #1), cộng chốt hậu-vòng `soTrangThayDuoc > trang`. Ca hỏng mà phản biện nêu (click trang trượt ở trang 1 của
  shop 240 SP) rơi vào chốt hậu-vòng: `coSan` của cú click trượt trả về đúng danh sách nhãn ⇒ `soTrangLonNhat`
  đọc ra 24 > 1 ⇒ cảnh báo. Ca "mất modal giữa chừng" rơi vào nhánh cảnh báo VÔ ĐIỀU KIỆN.

### ĐIỂM LỆCH so với spec — đọc kỹ

1. **Hạng mục 25b làm KHÁC spec ở một nửa:** spec đòi "cả hai lối `break` phải `canhBao.push`". Lối `break`
   thứ nhất (click trang kế trượt) là **cách vòng kết thúc BÌNH THƯỜNG** — pager có ellipsis nên mã cứ thử tới
   khi trượt. Cảnh báo vô điều kiện ở đó = một dòng "dừng ở trang 24/24" cho MỌI shop MỌI lượt, tức đúng thứ
   nhiễu mà hạng mục 27 (trần cảnh báo) vừa sinh ra để chống. Nên tôi tách hai ca bằng cờ mới `khongThayTrang`:
   "pager hết nhãn" = im lặng, mọi lý do khác (mất modal, mất pager) = cảnh báo. Ca im lặng nguy hiểm mà hạng
   mục 25 nhắm tới vẫn bị bắt, nhưng bằng chốt (c) chứ không bằng chốt (b).
2. **Hạng mục 25c KHÔNG dùng `doModal.soTrang`** như spec ghi, mà dùng nhãn SỐ của pager (`soTrangLonNhat`
   trên `sang.coSan`). Lý do: `soTrang` là **số phần tử `li`** trong `ul.eds-pager__pages` — nếu markup gói cả
   nút ‹ › vào trong `ul` đó thì shop 1 trang cũng đếm ra 3 ⇒ cảnh báo OAN mỗi shop mỗi lượt, mà DOM này CHƯA
   DÒ THẬT nên tôi không có căn cứ khẳng định nó không như vậy. Nhãn số miễn nhiễm với cả mũi tên lẫn ô "…", và
   sai số của nó chỉ theo hướng BỎ SÓT cảnh báo, không bao giờ báo oan.
3. **Hạng mục 30b sửa RỘNG HƠN spec một chỗ:** spec chỉ nêu `CauTongKetKm`, nhưng `CauTrangThaiKm` có đúng lỗi
   mẫu số ấy ở nhánh `HetKhung` (cùng dòng dữ liệu, cùng người đọc). Sửa cả hai; test khoá cả hai.
4. **Test hạng mục 26 khoá LUẬT + LƯỢT GHI DB, KHÔNG khoá chỗ GỌI.** `GhiMocSauLuotKm` được test thẳng trên
   `KmConfigRepository` thật, nhưng nếu ai đó xoá lượt gọi nó trong `than` của `ChayKmAsync` thì 4 test vẫn
   xanh — thân đó cần một phiên cầu nối thật để chạy tới. Ghi lại để phiên chính/nghiệm thu soi bằng mắt.
5. **Phần JS vẫn KHÔNG có test tự động** (repo chưa có hạ tầng test JS). Đợt này tôi bù bằng cách CHẠY THẬT ba
   hàm thuần qua `node -e` (số liệu ở mục trên) — mạnh hơn "đọc lại" của hai đợt trước, nhưng phần đụng DOM
   (neo modal cho `pageClickTrang`/`pageVanTayBang`) thì vẫn chỉ có đọc code.

### Phát hiện NGOÀI phạm vi — ghi lại, KHÔNG tự sửa

1. **Các nghi vấn DOM-chưa-dò giữ NGUYÊN theo đúng chỉ thị của plan:** bộ lọc `.eds-date-table` của nút chuyển
   tháng, cách rút tháng/năm từ tiêu đề picker, và nhánh cờ `captcha` trong gói kết quả (gần như không chạy vì
   `TrySetResult(null)` của đường `{action:"captcha"}` thắng trước). Đợt này lại thêm hai chỗ cùng loại: hai
   phép neo modal mới (`pageClickTrang`, `pageVanTayBang`) và cờ `khongThayTrang`. **Một lượt "Dò DOM KM" thật
   trên shop nhiều trang giờ kiểm được năm thứ cùng lúc** — vẫn là việc rẻ nhất còn nợ.
2. **Khe hở `MuonHoSoAsync` nhả khoá lượt trước TRƯỚC khi xin khoá mới** (`Phien.cs:374-377`) — pre-existing,
   ngoài phạm vi, chưa đụng (plan đã ghi nhận).
3. **Extension trong repo vẫn LỆCH bản publish** (`publish\ShopeeSuite\extensions\`) — đợt này lệch thêm.
   App đi ngược từ thư mục exe lên nên sửa trong repo KHÔNG có tác dụng với bản đã publish. Chưa đồng bộ.
4. **Chưa commit, chưa push, chưa phát hành, chưa đồng bộ publish** — đúng ràng buộc.
5. **Chưa chạy thật lần nào.** Toàn bộ hạng mục 25 (và các nhánh DOM của hai đợt trước) mới chỉ có đọc code +
   hàm thuần chạy rời.

---

## ĐỢT CHỐT — phiên chính tự sửa theo phản biện vòng 4 (20/08)

Nghiệm thu đợt 3: **ĐẠT** (5/7 trọn + 2 một phần có lý do). Phản biện vòng 4 còn 1 NẶNG + 2 TRUNG BÌNH + vặt.
Phát hiện đã hội tụ thành sửa phẫu thuật nhỏ ⇒ phiên chính tự sửa ở chặng chốt (đã tự xác minh từng phát hiện
bằng đọc code trước khi sửa).

### Đã sửa

1. **"Pager biến mất" ≠ "hết nhãn"** (NẶNG vòng 4): `pageClickTrang` nay trả cờ riêng `pagerVang` khi
   `pages.length === 0`; vòng lật trang chỉ coi là bình thường khi lúc MỞ modal cũng không thấy pager
   (`doModal.soTrang === 0` — shop ít trang), còn "lúc mở CÓ mà giờ MẤT" thì cảnh báo `dung-som-pager`.
   Hở còn lại (pager rút gọn không hiện nhãn trang cuối) đã ghi ở nghiệm thu đợt 3 — chỉ giải bằng dò DOM thật.
2. **LẬT LẠI hạng mục 26** (TB vòng 4): hết khung **VẪN GHI** `LanChayCuoi` (chỉ cần `SoTao > 0`). Lý do lật:
   không ghi ⇒ `DenGioChay` tới hạn lại MỖI ĐÊM ⇒ shop đầu danh sách nhận chương trình gấp `SoNgay` lần ý định,
   shop cuối vẫn đói (vòng luôn chạy từ shop 1). Câu log đổi từ "để đêm sau" → **"sẽ vào lượt kế theo chu kỳ"**
   (cả `CauTongKetKm` lẫn log trong `ChayKmSauPickerAsync`). Đường đi hai chiều ghi trong xmldoc `ChotLuotKm`.
3. **Bịt lỗ phủ chỗ gọi** (N4 vòng 4): gộp ghi-mốc + dựng-câu-tổng-kết vào `ChotLuotKm` (internal) — `than` của
   `ChayKmAsync` chỉ còn gọi nó; `KmMocLanChayCuoiTests` viết lại test QUA `ChotLuotKm`, khoá cả mốc DB lẫn câu.
4. **`SoShopChuaChay` ở CẢ ba lối ra sớm** (TB vòng 4): captcha-mở-shop (`shops.Count - i`), captcha-giữa-chayKm
   và vePicker-hỏng (`shops.Count - i - 1`) — hết ca "tạo 0/0 shop" / "tạo 2/3 shop" cho lượt 12 shop.
5. Vặt: `soTrangDaDi` hết lệch 1 khi chạm trần (N3) · xmldoc `pageVanTayBang` mô tả đúng cơ chế thật (N1) ·
   `cauBatDau` nút Dò bỏ "(TẠM — pha 1)" (N8).

### Kiểm chứng (phiên chính tự chạy, 20/08)

- Build `ShopeeSuite.sln` rebuild: **0 warning, 0 error**; `node --check` flow-km.js OK.
- Test: **1982/1982** (orders) + **168** + **34** + **2** — xanh hết. Số của CẢ CÂY (cây vẫn có việc khác dở).
- **Thử phá 2 lượt, 3 test đỏ đúng chỗ**: (a) trả lại điều kiện `!HetKhung` ⇒ `HetKhung...VAN_GHI` đỏ (1/4);
  (b) bỏ hẳn Save + trả câu "để đêm sau" ⇒ 3/4 đỏ (`ChayTron`, `HetKhung...VAN_GHI`, `CauTongKet`). Khôi phục
  bằng Edit (timestamp mới), chạy lại: 40/40 nhóm KM xanh, rồi toàn bộ 1982 xanh.
- Sửa JS (mục 1, 5) không có test tự động — như toàn bộ phần JS của việc này.

### KHÔNG sửa (ghi lại làm dư nợ)

- **Con trỏ tiến độ vòng shop** ("lượt sau bắt đầu từ shop i" khi hết khung) — thứ thật sự cho shop cuối chạy
  sớm; hiện chỉ có câu log nói đúng sự thật. Làm khi có bằng chứng chạy thật rằng hết-khung xảy ra thường xuyên.
- Các **nghi vấn DOM-chưa-dò**: bộ lọc `.eds-date-table` của nút chuyển tháng · cách rút tháng từ tiêu đề picker
  · pager rút gọn không hiện nhãn cuối · cờ `captcha` trong gói kết quả hầu như bị `TrySetResult(null)` thắng
  trước (chẩn đoán kém đi, vòng vẫn dừng đúng). **Một lượt "Dò DOM KM" thật kiểm được tất cả cùng lúc.**
- Khe hở **pre-existing** `MuonHoSoAsync` nhả khoá lượt trước TRƯỚC khi xin khoá mới (`Phien.cs:374-377`).
- Prefix log `"[dò KM]"` dùng cho cả lượt chạy thật (N9, pre-existing, câu chữ).
- `ChoChang.KmDump` 900s nghĩa là bấm Dò mà extension chết im thì 15' mới báo (huỷ được bằng nút Đóng cửa sổ).

### CÒN LẠI của cả việc (tổng, cho người đọc sau)

1. **Chưa commit** — chờ người dùng bảo.
2. **Chưa chạy thật**: vòng 12 shop · lịch tự động 2–6h · shop rỗng · nhánh chuyển tháng · các phép neo modal
   mới. Lượt rẻ nhất: một lần "Dò DOM KM" (SHOPEE_DEV_TOOLS=1) trên shop nhiều trang.
3. **Extension repo lệch bản publish** — bản đã cài KHÔNG có token/chuyển tháng/mọi sửa đợt này; phải đồng bộ
   hoặc phát hành trước khi người dùng bấm chạy trên bản publish (bẫy plan gốc §8.4).
