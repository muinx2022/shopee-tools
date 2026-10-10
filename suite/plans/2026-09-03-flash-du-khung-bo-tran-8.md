# Flash Sale: đủ mọi khung Shopee mở — trả vòng chờ K*.7, bỏ trần 8 khung, quay vòng trang — 2026-09-03

- **Ngày:** 2026-09-03 · **Trạng thái:** hoàn thành (commit a77b3d0 + chạy thật 16:53–18:09 03/09)
- **Người lập:** phiên chính · **Thực thi:** `opus-dev` · **Nghiệm thu:** `nghiem-thu` · **Phản biện:** `phan-bien`

User chốt 13:4x 03/09: *"sửa luôn đi, bỏ trần 8 khung, lấy hết khung Shopee mở"* — sau khi hỏi *"sao chỉ tạo
1/8 khung? nó phải đủ tất cả các khung chứ? … khung 1 → page 1, khung 2 → page 2 … nếu chỉ có 2–3 page mà
8 khung thì khung 4 quay lại page 1, khung 5 quay lại page 2"*.

Gộp xử lý sổ: **P-20260903-3** (hồi quy f9e118d — mỗi shop chỉ còn 1 khung/ngày), **P-20260903-4**
(`trangChoKhung` không quay vòng), và yêu cầu mới **bỏ trần 8 khung**.

## 1. Bối cảnh — đọc kỹ, người thực thi KHÔNG cần đọc lại chat

### 1.1 Vì sao 3 ngày liền mỗi shop chỉ tạo được 1/8 khung (P-20260903-3)

Vòng khung ở `extensions/shopee-orders/flow-flashsale.js` (`chayFlashSaleChoTabDangMo`, vòng `for` ~:3648)
định làm đủ 8 khung, mỗi khung K*.1 → K*.7. Từ 01/09, **9/9 lượt shop** (01/09 15:19–15:33, 02/09 01:2x,
03/09 01:07–01:21) dừng ở `K2.2 … không thấy ô ngày 'N' đang ĐỎ (tổng 35 ô, 0 ô đỏ)` → bước đỏ →
`ketThuc` với `daTao=true` → C# tính shop **XONG hôm nay** → 9 gói sau bỏ qua shop.

Bằng chứng hồi quy (log `%AppData%\XuLyDonShopee\logs\hoatdong-2026083{1}.log`, `…0901/0902/0903.log`):

| | Lượt 31/08 19:10–19:25 (code f08dd86) | Từ 01/09 (code f9e118d trở đi) |
|---|---|---|
| Bước G `K*.7` tốn | 2'08"–2'09" mỗi khung (chờ trọn 240 nhịp) | ~16s (20 nhịp) |
| Khung 2, 3, 4 | **đều TẠO ĐƯỢC** (`K2.6 Trang 2`, `K3.6 Trang 3`, `K4.6 Trang 4` ok) | K2.2 "0 ô đỏ" — 9/9 |
| Chết vì | trần C# 900s ở khung 5 (nay 2700s) | — |

Giữa hai mốc extension **không đổi gì khác**: `a2e89ac` chỉ sửa comment + thêm `soTaiChe`; `background.js`
chỉ sửa comment; cây `extensions/` sạch. Con số duy nhất khác: commit **f9e118d** (31/08 19:35) đổi vòng chờ
dialog "Tạo thành công" ở bước G từ `for (let i = 0; i < 240; i++)` xuống `i < 20` (`flow-flashsale.js:4155`).

Đo khoảng cách: *kích hoạt → mở lịch khung kế* ~60s ở **cả hai bên** ⇒ không phải "Shopee cần nghỉ sau kích
hoạt". Cái ngắn đi là **xác nhận khung giờ (K*.3 — Shopee bắt đầu tạo nháp ngầm) → Xác nhận + Kích hoạt
hàng loạt (K*.7)**: 2'15" → 30s. Comment ngay tại bước G tự ghi "Dialog Tạo-thành-công nở muộn 1–2 phút sau
xác nhận khung". Kích hoạt lên nháp chưa dựng xong ⇒ lịch của shop mất sạch `div.timeslots` ở khung kế. Đó là
giả thuyết cơ chế; **tương quan 4/4 trước – 9/9 sau là số đo thật.** Lý do f9e118d cắt (8 × 3,5' vượt trần
900s) nay không còn: `ChoChang.FlashChay` = 2700s.

Câu cảnh báo ở `:4195` vẫn nói "KHÔNG nở sau 2 phút" trong khi vòng chỉ 10s — trả vòng về 240 nhịp thì câu
đó đúng trở lại.

### 1.2 Trang sản phẩm theo khung (P-20260903-4)

`trangChoKhung(soKhungDaTao)` (`:2736`) trả thẳng `n + 1`, không biết shop có bao nhiêu trang. Shop 3 trang
(≤30 sp) tới khung 4 ⇒ bước F lật quá trang ⇒ `pageSangTrangSauFlash` `ok:false` ⇒ bF ĐỎ "không lật được
tới trang 4 (dừng ở trang 3)" ⇒ `ketThuc` ⇒ **mất luôn khung 4–8 của shop**. Chưa cắn vì §1.1 chặn ở khung
2, sẽ cắn ngay khi §1.1 được sửa. Vật liệu: `pageDemTickFlash` (`:2960`) đã trả `nhanCacTrang` (nhãn mọi
`li.eds-pager__page` trong modal) và `trangHienTai`.

### 1.3 Trần 8 khung hiện nằm ở đâu

- JS: `const SO_KHUNG_TOI_DA = 8` (`:2439`); `docThamSoFlash` kẹp `soKhungToiDa` vào `[1, 8]` (`:2760–2763`);
  vòng `for (let vong = 0; vong < tp.soKhungToiDa; vong++)` (`:3648`); chuỗi `Khung thứ ${vong+1}/${tp.soKhungToiDa}`
  (`:3715`) và `đã TẠO + KÍCH HOẠT ${soKhungDaTao}/${tp.soKhungToiDa} khung` (`:4219`).
- C#: `record ThamSoFlashSale(DateTime NgayDich, int SoKhungToiDa = 8)` +`MoTa()` "tối đa {SoKhungToiDa} khung
  giờ mỗi shop" + `Goi()` gửi `soKhungToiDa` (`orders/XuLyDonShopee.Core/Services/OrdersBridgeSession.cs:337–360`);
  hai chỗ gọi `new ThamSoFlashSale(ngayDich)` (`AccountsViewModel.Phien.cs:890, :2326`) không truyền trần.
- Xmldoc `OrdersBridgeChannel.ChoChang.FlashChay` (`OrdersBridgeChannel.cs:~314`) có đoạn "RÀNG BUỘC HAI CHIỀU
  với `SO_KHUNG_TOI_DA`" và test `HanChang_FlashChay_RiengVaDuChoTamKhung` (orders tests `:732`) đòi chuỗi
  `SO_KHUNG_TOI_DA` phải có trong file đó.
- Số khung THẬT vốn đã do Shopee quyết: K*.2 dump khung của ngày (`pageDumpKhungGioFlash`), K*.3
  `khungChuaDung` chọn khung chưa khoá/chưa dùng, hết thì `hetKhung` → xong shop (`:3833–3890`). Trần chỉ là
  cận trên của vòng `for`.

### 1.4 Test hiện có sẽ đụng

`suite/Shopee.Core.Tests/FlashSaleLuongThatTests.cs` (Jint chạy NGUYÊN VĂN hàm thuần, nạp qua
`MayLuatThuan()` — danh sách tên hàm ở `:29`; `HangSo("SO_KHUNG_TOI_DA")` ở `:39`):
`TrangChoKhung_KhungThuK_RaTrangK` (`:273`), `TrangChoKhung_MaTran_HaiNgayBaKhung_Ra123123` (`:660`),
`DocThamSoFlash_ThieuHoacRac_ThiCoCauLoiCoTen` (`:472` — kẹp 200 → 8), `DocLich_DungMotLanMoiShop_KhongLapLaiMoiKhung`
(`:626` — neo chuỗi header vòng `for`, cửa `phaiDocLich`, **đúng MỘT** `await choLichLang(` và nằm SAU cửa),
`:695` neo `if (hetKhung) {`. `orders/XuLyDonShopee.Tests/FlashSaleLuongThatTests.cs:84` đòi payload có
`soKhungToiDa == 8`; `:732` đòi `SO_KHUNG_TOI_DA` trong `OrdersBridgeChannel.cs`.

### 1.5 Môi trường

- `dotnet` trên PATH hỏng — dùng `"C:\Users\Ng Xuan Mui\AppData\Local\Microsoft\dotnet\dotnet.exe"` (8.0.423).
- App **đang tắt** (user dừng 13:22 03/09) ⇒ không khoá DLL. Bản user chạy: `suite\Shopee.Suite\bin\Debug\net8.0-windows\ShopeeSuite.exe`;
  `dotnet build ShopeeSuite.sln` tự chép extension vào bin. **Không mở app, không chạy lượt thật** ở chặng 2–4.
- Bộ test orders có bài phóng Brave thật / có thể treo (sổ P-20260901-2, -3: `FlashGoiKheNghiTests`,
  `BridgeTestRig`) — chạy với `--blame-hang --blame-hang-timeout 8m`; treo thì báo tên bài, không lặp.

## 2. Phạm vi

**Làm**
1. Trả vòng chờ dialog ở bước G về 240 nhịp, đặt tên hằng, xmldoc ghi bằng chứng (§3.1).
2. Cửa "ô ngày đích đỏ" ở K*.2: chờ có trần (≤20 nhịp) trước khi kết luận, KHÔNG thêm lượt `choLichLang` (§3.2).
3. Bỏ trần 8 khung: JS + C# + chuỗi log + xmldoc + test (§3.3).
4. `trangChoKhung(soKhungDaTao, soTrang)` quay vòng theo số trang thật đọc từ pager modal; hàm thuần
   `soTrangTuNhan` (§3.4).
5. Test mới có THỬ PHÁ; cập nhật test cũ theo §1.4. 0 warning.

**Không làm**
- P-20260903-1 (vòng check đơn chạy lại đè gói flash — cổng 47821) — đợt riêng.
- P-20260903-2 (câu "0 sản phẩm") — đợt riêng.
- KHÔNG đổi `FlashChay` 2700s, `PhutChotShopMoiTruocCuoiKhe` 45', nhịp gói, khe nghỉ.
- KHÔNG đọc lại lịch mỗi khung (quyết định "đọc lịch một lần mỗi shop" + test của nó GIỮ NGUYÊN).
- KHÔNG đụng luồng KM / Combo. KHÔNG ghi `SO-TAY.md` (phiên chính ghi). KHÔNG commit.

## 3. Các bước

### 3.1 Bước G — vòng chờ dialog "Tạo thành công" (`flow-flashsale.js` ~:4148–4160, :4195)

- Thêm hằng top-level `const CHO_DIALOG_TAO_NHIP = 240;` (cạnh `CHO_DONG_MODAL_NHIP`), xmldoc nêu: nhịp
  500ms ⇒ 2 phút; f9e118d hạ 240→20 làm khung 2 trở đi chết (0 ô đỏ) 9/9 lượt 01–03/09, trong khi 240 nhịp
  chạy 4 khung liền 31/08 19:10; khoảng thật bị cắt là K*.3→K*.7 (nháp Shopee dựng ngầm 1–2'); trần 2700s
  đủ cho ~11–12 khung ở 3,5–4'/khung nên KHÔNG hạ lại.
- Vòng `for (let i = 0; i < CHO_DIALOG_TAO_NHIP; i++) { await sleep(500); … }`. Giữ break sớm khi dialog nở.
- Câu cảnh báo `:4195` giữ "sau 2 phút" (đúng trở lại) — hoặc ghép từ hằng: `sau ${CHO_DIALOG_TAO_NHIP/2}s`.
- Sửa comment cũ ("20 nhịp (10s) — dialog CHƯA BAO GIỜ nở…") thành lý do mới; giữ lại một dòng lịch sử.

### 3.2 K*.2 — chờ ô ngày đích ĐỎ có trần (`flow-flashsale.js` ~:3800–3812)

- Thêm hằng `const CHO_O_NGAY_DO_NHIP = 20;` (×500ms = 10s), xmldoc: ô ngày render TRƯỚC khi dữ liệu khung
  về; khung ≥2 không đọc lại lịch (`phaiDocLich=false`) nên trước đây bấm ngay khi lưới vừa vẽ.
- Thay lời gọi đơn `const click = await chay(pageClickNgayFlash, [tp.ngay]);` bằng vòng: gọi
  `pageClickNgayFlash`; `ok` ⇒ thoát; `!ok` mà `loi` khớp /đang ĐỎ/ (chưa có ô đỏ) ⇒ `sleep(500)` rồi thử lại
  tới `CHO_O_NGAY_DO_NHIP`; `loi` khác (thiếu luật, không thấy rows) ⇒ thoát ngay, không thử lại. Ghi
  `soNhipChoNgayDo` vào `sau`. `pageClickNgayFlash` chỉ `click()` khi tìm thấy ô đỏ nên gọi lặp không bấm
  thừa.
- **TUYỆT ĐỐI không thêm `await choLichLang(`** — test `DocLich_DungMotLanMoiShop_KhongLapLaiMoiKhung` đếm
  đúng 1 lời gọi và phải xanh nguyên vẹn (chỉ đổi chuỗi header vòng `for` theo §3.3).

### 3.3 Bỏ trần 8 khung

**JS**
- Xoá `SO_KHUNG_TOI_DA`. Thêm `const TRAN_VONG_KHUNG_KY_THUAT = 24;` xmldoc: **không phải trần nghiệp vụ** —
  vòng dừng thật ở `hetKhung` (Shopee hết khung chọn được); 24 là chốt chống vòng vô tận nếu memo `khungDaDung`
  hỏng (một ngày không thể có quá 24 khung).
- `docThamSoFlash`: bỏ đọc/kẹp/trả `soKhungToiDa` (payload C# không còn gửi; nếu có gửi cũng bỏ qua).
- Vòng: `for (let vong = 0; vong < TRAN_VONG_KHUNG_KY_THUAT; vong++)`. Nếu vòng chạy hết 24 mà chưa
  `hetKhung` ⇒ `canhBao.push("chạm trần kỹ thuật … vòng mà Shopee vẫn còn khung — kiểm memo khungDaDung")`.
- Chuỗi: `:3715` → `Khung thứ ${vong + 1} của ngày ${tp.moTa}.`; `:4219` → `đã TẠO + KÍCH HOẠT ${soKhungDaTao}
  khung (trang ${trangDich}/${soTrang}, ${soTickKhungNay} sản phẩm).`; mọi chỗ khác nhắc "tối đa 8"/"8 khung"
  như LUẬT (không phải số đo lịch sử) sửa theo.

**C#**
- `ThamSoFlashSale(DateTime NgayDich)` — bỏ `SoKhungToiDa`; `MoTa()` → `"ngày đích dd-MM-yyyy · lấy HẾT khung
  giờ Shopee mở cho ngày đó"`; `Goi()` bỏ `soKhungToiDa`; sửa xmldoc `:337`, `:1002`, và xmldoc nút
  `ChayFlashSaleTayAsync` (`Phien.cs:~2308` "tới 8 cái mỗi shop").
- Xmldoc `FlashChay` (`OrdersBridgeChannel.cs:~314`): thay đoạn "RÀNG BUỘC HAI CHIỀU với SO_KHUNG_TOI_DA" bằng:
  không còn trần khung; 2700s ÷ (~210–270s/khung sau khi trả vòng chờ 2') ≈ 10–12 khung; ngày Shopee mở nhiều
  hơn thì shop chạm `FlashChay` → HỎNG ở khung đang dở, chương trình đã tạo vẫn còn, shop KHÔNG bị tính xong
  nên gói sau mở lại và tiếp phần còn lại (khung đã dùng bị Shopee khoá ⇒ `khungChuaDung` tự bỏ qua). Nhắc
  `TRAN_VONG_KHUNG_KY_THUAT`.
- `grep -rn "tối đa .* khung\|8 khung" --include=*.cs --include=*.js orders suite extensions` — mọi chỗ còn lại
  phải là số đo lịch sử trong xmldoc/comment, không phải câu luật hay chuỗi log.

**Test**
- suite `DocThamSoFlash_…`: bỏ hai assert về `soKhungToiDa`; thêm assert kết quả **không có** trường
  `soKhungToiDa` dù đầu vào có `soKhungToiDa: 8`. `MayLuatThuan`: bỏ nạp `SO_KHUNG_TOI_DA`, nạp
  `TRAN_VONG_KHUNG_KY_THUAT` (qua `HangSo`) + `soTrangTuNhan`.
- suite `DocLich_DungMotLanMoiShop_…`: đổi chuỗi neo header vòng `for` sang bản mới. Giữ mọi assert khác.
- suite thêm `VongKhung_KhongConTranNghiepVu`: `HangSo("TRAN_VONG_KHUNG_KY_THUAT") >= 24`; nguồn hàm không còn
  `soKhungToiDa`; header vòng dùng đúng hằng.
- orders `:84`: `Assert.False(ts.TryGetProperty("soKhungToiDa", out _))`. orders `:732`: `SO_KHUNG_TOI_DA` →
  `TRAN_VONG_KHUNG_KY_THUAT`.
- suite thêm `ChoDialogTao_240Nhip_KhoaHoiQuyF9e118d`: `HangSo("CHO_DIALOG_TAO_NHIP") == 240` và trong nguồn
  `chayFlashSaleChoTabDangMo` có `i < CHO_DIALOG_TAO_NHIP`. Xmldoc test ghi bằng chứng §1.1.
- suite thêm `ChoONgayDo_CoTran_KhongThemChoLichLang`: nguồn có `CHO_O_NGAY_DO_NHIP`, số `await choLichLang(`
  vẫn = 1.

### 3.4 Quay vòng trang

- Hàm thuần mới `export function soTrangTuNhan(nhanCacTrang)`: lấy **max số nguyên** trong mảng nhãn (bỏ
  "...", "‹", "›", rỗng, không phải số); không có số nào ⇒ `1`. Xmldoc: pager EDS hiện "1 2 3 … 12" — nhãn
  cuối là tổng trang; shop 1 trang có thể không render pager.
- `trangChoKhung(soKhungDaTao, soTrang)`: `n = floor(max(0, soKhungDaTao))`, `t = floor(soTrang)`; `t < 1`
  hoặc không hữu hạn ⇒ `t = 1`; trả `(n % t) + 1`. Xmldoc: luật user 03/09 (3 trang: 1,2,3,1,2,3,1,2).
- Bước F (`~:3966`): TRƯỚC `buoc(...)` gọi `const demTruocLat = await chay(pageDemTickFlash, [])` (modal đang
  mở sau K*.4/K*.5), `const soTrang = soTrangTuNhan(demTruocLat && demTruocLat.nhanCacTrang)`,
  `const trangDich = trangChoKhung(soKhungDaTao, soTrang)`. Tên bước `${K}.6 Trang ${trangDich}/${soTrang} + chọn
  tất cả + Xác nhận modal`. Nếu `nhanCacTrang` rỗng mà `demTruocLat.soDong >= SO_SP_MOI_TRANG` ⇒
  `canhBao.push(\`${K}: pager không đọc được — coi như 1 trang\`)`. Ghi `soTrang` vào `sau` + `ghiChu`.
- Test suite: `TrangChoKhung_KhungThuK_RaTrangK` → Theory `(soKhungDaTao, soTrang, mongDoi)`:
  (0,8,1) (1,8,2) (7,8,8) (3,3,1) (4,3,2) (5,3,3) (6,3,1) (0,1,1) (5,1,1) (2,0,1) (2,-1,1).
  `TrangChoKhung_MaTran_HaiNgayBaKhung_Ra123123` giữ (truyền `soTrang=3`), thêm ma trận 8 khung × 3 trang ⇒
  `1,2,3,1,2,3,1,2`. `SoTrangTuNhan_*`: `["1","2","3"]→3`, `["1","2","...","12"]→12`, `[]→1`,
  `["‹","1","›"]→1`, `null→1`, `["a","b"]→1`.
- **THỬ PHÁ bắt buộc** (luật 4): với mỗi test mới, đổi ngược đúng một luật trong file production (vd bỏ `% t`,
  đổi 240→20, đổi max→first) ⇒ test phải ĐỎ ⇒ khôi phục. Ghi từng cặp (đột biến → tên test đỏ) vào báo cáo.
  **Khôi phục xong phải `git diff --stat` đối chiếu** — đợt 01/09 từng để sót đột biến trên đĩa.

## 4. Tiêu chí nghiệm thu (đo được)

- [ ] **N1** `dotnet build ShopeeSuite.sln` (dotnet ở đường dẫn §1.5): 0 error, **0 warning**.
- [ ] **N2** `dotnet test suite/Shopee.Core.Tests` xanh toàn bộ; số test ≥ 422 + số bài mới (báo con số).
- [ ] **N3** `dotnet test orders/XuLyDonShopee.Tests --blame-hang --blame-hang-timeout 8m` xanh toàn bộ; số test
  ≥ 2303 (báo con số; bài treo nếu có: nêu tên, không lặp).
- [ ] **N4** `flow-flashsale.js`: `CHO_DIALOG_TAO_NHIP = 240` và vòng bước G dùng nó; không còn chuỗi
  `SO_KHUNG_TOI_DA`; header vòng khung dùng `TRAN_VONG_KHUNG_KY_THUAT` (≥ 24); `docThamSoFlash` không trả
  `soKhungToiDa`; đúng **1** `await choLichLang(` trong `chayFlashSaleChoTabDangMo`; có `CHO_O_NGAY_DO_NHIP`.
- [ ] **N5** C#: `ThamSoFlashSale` chỉ còn `NgayDich`; `Goi()` không có `soKhungToiDa`; `MoTa()` nói "lấy HẾT
  khung"; `grep -rn "tối đa .* khung" --include=*.cs --include=*.js orders suite extensions` không còn câu luật
  / chuỗi log (chỉ còn ghi chú lịch sử, nếu có).
- [ ] **N6** Jint: `trangChoKhung` ma trận §3.4 đúng; `soTrangTuNhan` 6 ca đúng.
- [ ] **N7** Báo cáo có bảng THỬ PHÁ: mỗi test mới ≥ 1 đột biến làm nó đỏ, đã khôi phục, `git diff --stat`
  sau khôi phục chỉ còn đúng các file của đợt.
- [ ] **N8** (phiên chính, chặng 5) Chạy thật 1 lượt tay trên máy này sau khi build: log
  `hoatdong-20260903.log` có ít nhất một shop qua `K2.2 … ok` và `đã TẠO + KÍCH HOẠT 2 khung`. Nếu mọi shop
  báo ngày 04/09 không đỏ ngay từ K1.2 (Shopee đã đóng) ⇒ ghi "không đo được hôm nay", không tính ĐẠT/KHÔNG.

## 5. Rủi ro & lưu ý

- Trả vòng chờ 2' ⇒ mỗi khung ≈ 3,5–4,5'. Ngày Shopee mở >11–12 khung thì shop chạm `FlashChay` 2700s ở khung
  đang dở → HỎNG ở gói đó, gói sau tiếp — chấp nhận, KHÔNG nới `FlashChay` trong đợt này (nó khớp biên 45' của
  khe). Ghi rõ trong xmldoc `FlashChay`.
- `pageDemTickFlash` phải gọi khi modal sản phẩm đang mở — đúng thời điểm trước bước F (sau K*.5). Không gọi
  ở chỗ khác.
- Không được "tiện tay" đọc lại lịch mỗi khung để "chắc" — đó là quyết định đã chốt có test; §3.2 chỉ chờ ô
  đích đỏ, không đọc lại danh sách ngày đỏ.
- Chuỗi log là thứ người vận hành đọc hằng ngày và là thứ test neo — đổi chuỗi nào thì grep test trước.
- Báo cáo của `opus-dev`: đã làm gì · số build/test THẬT · bảng thử phá · `git status --short` cuối ·
  mục **NGOÀI PHẠM VI — đề nghị ghi sổ** (hoặc "không có").

---

## Báo cáo thực thi (điền sau)

**Trạng thái: CODE XONG + kiểm chứng độc lập ĐẠT — chưa commit, chưa chạy thật (N8).** (chốt 03/09 ~14:5x)

### Đã làm (6 file + 1 fixup của phiên chính)
- `opus-dev` thi công §3.1–§3.4: `flow-flashsale.js` (+188), `OrdersBridgeSession.cs`, `OrdersBridgeChannel.cs`,
  `AccountsViewModel.Phien.cs`, hai file test. Thêm 18 ca test (suite 422→440), 6 bài mới, 10 cặp thử phá.
- Lệch spec có chủ đích (opus-dev tự khai): thêm `try/catch` quanh `pageDemTickFlash` ngoài `buoc(...)` — vì
  lời gọi nằm ngoài thân bước, ném thì shop treo trọn `FlashChay` 2700s; bắt lỗi ⇒ `canhBao` ⇒ coi như 1
  trang. Phản biện đã soi: hợp lý, không nuốt câm (canhBao lên gói `ketThuc`).
- **Fixup phiên chính (chặng 5)**: `Phien.cs:478` `trangThaiBatDau` còn nói "mỗi shop tới 8 chương trình" —
  phản biện bắt (lọt cả N5 lẫn test vì chuỗi không chứa "tối đa"/"khung"). Đã sửa → "nhặt HẾT khung Shopee
  mở cho ngày đích". Build lại worktree sạch: 0 warning.

### Kiểm chứng THẬT (đo trong `git worktree --detach HEAD` + 6→7 file — app thật PID 4444 khoá bin cây chính)
- `nghiem-thu` (tự đo): build **0 error 0 warning**; suite **440/440**; orders **2303/2303** (`--blame-hang 8m`,
  không treo, không nhiễu cổng 47821). N1–N7 = **7/7 ĐẠT**.
- `phan-bien`: **không có lỗi CHẶN/NẶNG**. Xác nhận bất biến an toàn: timeout ⇒ shop KHÔNG bị đánh dấu xong
  oan (`ShopDaXongFlash` = false khi `DaTao=false, BoQua=null`) ⇒ gói sau thử lại; vòng khung có lối thoát
  chắc (`hetKhung`); cửa ngày-đỏ không nuốt ca "Shopee chưa mở"; C# không còn đọc `SoKhungToiDa`.
- Phiên chính build lại sau fixup: **0 warning 0 error** (worktree, 18,6s).

### Còn treo
- **N8 (chạy thật)** — CHƯA làm: cần dừng app đang chạy check đơn + deploy bản mới + bấm "Chạy Flash Sale
  (THẬT)" (tạo chương trình THẬT trên 12 shop). Là hành động hướng-ngoại + dừng nghiệp vụ đang chạy ⇒ **chờ
  user quyết** khi nào chạy. App hiện chạy BẢN CŨ (bin Sep 1) — fix chưa tới lượt auto tối nay nếu không deploy.
- **Chưa commit** (chờ user).

### Điểm nhỏ phản biện nêu (ghi sổ, chưa sửa)
- NHẸ · cảnh báo "pager không đọc được" bắn nhầm cho shop đúng 10 sp/1 trang (`soDong >= 10`) — chỉ nhiễu
  log, và `soDong` không phân biệt được "1 trang 10 sp" với "nhiều trang, pager hỏng" (trang 1 luôn 10 dòng)
  ⇒ sửa `>` cũng mất tín hiệu; để lại, ghi sổ P-20260903-5.
- NGHI NGỜ · `soTrangTuNhan` lấy max — vỡ nếu Shopee đổi markup pager chèn số không-phải-trang; chưa dựng
  được ca thật ⇒ P-20260903-6.

### N8 — chạy thật (phiên chính, 03/09 16:53 gói auto trong khe nghỉ, commit a77b3d0 đã build vào bin thật)
- Deploy: dừng app cũ lúc app rảnh (gói cũ xong 15:37:41, check đơn hoãn tới 16:37), `dotnet build` cây chính 0 warning 0 lỗi
  0 khoá file, bin extension có `CHO_DIALOG_TAO_NHIP = 240`; mở lại 15:39:14; bật check đơn 16:04 (user yêu cầu test
  giữa hai lần nghỉ); vòng 1 xong 16:52:59 → **MỞ GÓI 16:53:09** trong khe nghỉ (tới 18:52).
- **ĐẠT** — Alina Store1, ngày đích 04/09 (log `hoatdong-20260903.log`):
  `16:54:19 K1.6b ok` → `16:56:29 K1.7 ok` (**2'10"**, vòng chờ 240 nhịp đã về) → `16:56:29 đã TẠO + KÍCH HOẠT 1 khung
  (trang 1/4)` (đọc được 4 trang thật) → **`16:57:27 K2.2 Chọn ngày 4/9/2026 + chờ khung giờ: ok`** — cùng shop, cùng
  ngày đích, cùng extension trừ đúng 20→240, chỗ 9/9 lượt 01–03/09 đỏ "0 ô đỏ" nay xanh. Giả thuyết hồi quy f9e118d
  xác nhận trên Shopee thật. (Số khung cuối của shop + bằng chứng quay vòng trang: bổ sung khi gói xong.)
- Bài học vận hành lúc deploy (đã ghi vào memory `tu-chay-app-va-doc-log`): kill app ngay sau nhịp mở gói ⇒ DB đã ghi
  `flash_lan_thu_tu_dong` nhưng dòng "MỞ GÓI" mất (log xả 1s) ⇒ gói kế im lặng đúng 30' giãn cách — không phải lỗi code.
- **Bổ sung 17:11 — Alina trọn lượt**: 5 khung trong MỘT lượt (16:56:29 → 17:10:06, ~3'25"/khung): trang 1/4 → 2/4 → 3/4 → 4/4 →
  **1/4 (quay vòng đúng luật user)**. Khung 6: `K6.2 … không thấy ô ngày '4' đang ĐỎ (tổng 35 ô, 12 ô đỏ)` ⇒ shop KÍN thật
  (3 khung của 3 lượt ghé trước + 5 = 8/8 khung Shopee mở cho 04/09), khác hẳn "0 ô đỏ" (lịch chết) của lỗi cũ. Tổng
  kết shop: "5 khung tạo mới, 0 tái chế". Ước lượng thời gian: 8 khung ≈ 27' < FlashChay 45' — đúng biên plan §5.
- **Gói 16:53 kết thúc 18:09:18**: Alina **5** khung (17:11) · Shop9x **6** khung (17:36, ~21') · Vtinho **6** khung (18:00, ~20') —
  mỗi shop lấy tới KÍN 8/8 của 04/09 tính cả khung các lượt ghé trước; Piko "không đỏ"; 8 shop còn lại chưa chạy vì tới hạn
  bắt đầu shop mới của gói (18:52 − 45' = 18:07) — đúng thiết kế khe. ~3,4'/khung. **N8: ĐẠT.** Plan: **hoàn thành**.
