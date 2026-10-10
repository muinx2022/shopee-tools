# Plan: Refactor `flow-km.js` + `flow-combo.js` — tách hạ tầng dùng chung, gom hàm thuần song sinh, GIỮ NGUYÊN HÀNH VI

- **Ngày:** 2026-08-29
- **Trạng thái:** hoàn thành (trừ 2 mục CHƯA ĐO khách quan — xem Báo cáo thực thi cuối file)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-dev`)
- **Căn cứ:** lượt khảo sát 4 góc (bản đồ phụ thuộc · kiểm kê neo test · phân loại trùng lặp · ràng buộc
  cấu trúc) + lượt phản biện plan 4 góc, đều chạy 29/08/2026. Mọi khẳng định có file:dòng.

## 1. Bối cảnh & mục tiêu

User (29/08/2026): *"2 phần km và combo, có vẻ code cũng dài, lên kế hoạch để refactor nó luôn"*.

Hiện trạng: `flow-km.js` 1902 dòng, `flow-combo.js` 1631 dòng. "Dài" có ba nguồn RẤT khác nhau, chỉ một
nguồn refactor được an toàn:

1. **Hạ tầng dùng chung đang nằm nhờ trong file luồng KM.** `flow-combo.js:53` import
   `{buoc, pageClickTheoSelector, pageDemPhanTuHien, gomCanhBao, soTrangLonNhat}` từ `./flow-km.js`;
   `flow-flashsale.js:40-41` (import viết trên HAI dòng) import
   `{buoc, pageClickTheoSelector, pageDemPhanTuHien, pageCaiLuatONgayEds, pageCaiLuatMuiTenThang}` cũng
   từ `./flow-km.js`. "File luồng" đang kiêm "module hạ tầng" — tách ra là đúng tầng.
2. **Hàm thuần SONG SINH chép tay giữa km và combo** — trùng lặp THẬT, xoá được:
   - `docMocNgay` (km:1065-1095 vs combo:870-898): **PHẦN CODE trùng từng byte**; bản km DƯ 2 dòng
     comment nội bộ (km:1081-1082) và xmldoc hai bản lệch chữ — khác biệt ĐÃ BIẾT, khai sẵn ở Đợt 2.1.
   - `mocNgayLuotDo` (km:1825-1833 vs combo:915-923): thân trùng nguyên văn 100%; hằng
     `NGAY_DAT_TRUOC_LUOT_DO = 180` khai hai nơi (km:1822, combo:141) với comment dài ngắn khác nhau.
   - `cauLoiThamSo` (km:1132-1135 vs combo:901-904): thân trùng, chỉ khác chữ "KM"/"Combo" trong chuỗi.
     ⚠ Hợp đồng thật của hàm: **nhận OBJECT lỗi theo trường**, `Object.keys(loi || {}).map(k => k+": "+loi[k])`
     rồi `join(" · ")`; không có key ⇒ trả `null` (đường chạy sạch hằng đêm là `{}` ⇒ `null`). Chuỗi ra đi
     thẳng lên nhật ký C# (mẫu thật: `"…KHÔNG dùng được — phanTram: % giảm không hợp lệ…"`,
     KmVongMoiShopTests.cs:367).
3. **Phần còn lại dài CÓ CHỦ ĐÍCH, không được gom**: (a) page-func trùng TÊN giữa hai file là hai bộ
   component khác nhau — KM là Vue `eds-*`, combo là React `eds-react-*` (`flow-combo.js:35` cấm chép
   selector chéo; lỗi 12/12 shop 27/08 sinh từ hai bản luật trôi lệch); (b) helper trong page-func BẮT
   BUỘC lặp từng hàm (exec.js:1-2 — chrome.scripting serialize ĐỘC LẬP); (c) hai hàm chạy chính
   (`chayKmChoTabDangMo` ~480 dòng, `chayComboChoTabDangMo` ~494 dòng) cố ý một khối (km:1326-1327);
   (d) comment dày là tài sản (mỗi hằng/bước mang số đo thật và tiền lệ lỗi).

**Mục tiêu:** làm (1) và (2) — di chuyển NGUYÊN VĂN + gom hàm thuần trùng thật, kiểm bằng máy rằng hành
vi không đổi. KHÔNG đụng (3). Sau plan: flow-km ~1700 dòng, flow-combo ~1580 — phần dài còn lại là dài
có chủ đích, plan nói thẳng điều đó thay vì đại phẫu rủi ro trên hai luồng chạy thật hằng đêm.

### Ràng buộc đã kiểm kê (căn cứ chọn phương án)

- **Ghi chú "THÂN HÀM KHÔNG ĐƯỢC SỬA"** (km:72, 989, 1011, 1159; combo:50-52; flashsale:38-39) cấm SỬA
  THÂN và cấm CHÉP BẢN THỨ HAI — không chỗ nào cấm DI CHUYỂN nguyên văn. Rào cản di chuyển nằm ở TEST.
- **5 hàm bị test C# bóc NGUYÊN VĂN nguồn theo (file cứng `flow-km.js`, tên hàm) — PHẢI Ở LẠI
  `flow-km.js`**, giữ function declaration top-level, giữ nguyên từng chuỗi bị Assert (kể cả comment
  "KHÔNG bấm Xác nhận" trong `datOThoiGian`):
  1. `pageCaiLuatONgayEds` (km:247) — EdsONgayHopLeTests.cs:32,72 + FlashSaleDoDomTests.cs:113;
  2. `pageCaiLuatMuiTenThang` (km:300) — FlashSaleDoDomTests.cs:820-894;
  3. `pageClickNgayTheoSo` (km:368) — EdsONgayHopLeTests.cs:127-139 (Assert nguyên văn cả tên biến);
  4. `pageBamChuyenThang` (km:837) — FlashSaleDoDomTests.cs:901-915;
  5. `datOThoiGian` (km:1257) — EdsONgayHopLeTests.cs:146-163 + FlashSaleDoDomTests.cs:918-924.
- **Hai hàm bị nhận diện THEO TÊN tại chỗ gọi trong flow-flashsale.js** — DỜI FILE được, **CẤM ĐỔI TÊN**:
  `pageClickTheoSelector` (FlashSaleDoDomTests.cs:2196, AST Identifier) và `buoc`
  (FlashSaleDoDomTests.cs:2263 — regex `buoc\(ds, "…"` đếm nhãn bước trong `doDomFlashSaleChoTabDangMo`).
- **`flow-combo.js` KHÔNG bị test nào đọc nguồn** — 39 hàm top-level của nó tự do về mặt test; chỉ giữ
  import (combo:53) và export `doDumpCombo`/`doChayCombo` (background.js:53).
- **`ExtensionJsCuPhapTests` glob toàn thư mục** (dòng 41-42): file mới TỰ vào lưới — parse ES module,
  import tương đối trỏ file có thật, TÊN import phải có export tương ứng. Chưa bao `export * from`
  ⇒ **không dùng re-export dạng đó**.
- **manifest.json** service worker `type: "module"` — thêm file mới không cần khai manifest.
- **Serialize**: dời NGUYÊN HÀM page-func sang file khác không đổi chuỗi serialize; cái cấm là TÁCH RUỘT.
- **`buoc` kéo cụm**: gọi `urlCuaTab` (km:1140-1142) + `send` (core.js); `gomCanhBao` fallback đọc hằng
  module `TRAN_CANH_BAO` (km:1014) — dời CẢ CỤM. core.js không import ngược ⇒ không vòng.
- **orders/XuLyDonShopee.Tests không đọc nguồn hai file này** (chỉ test khuôn gói qua rig C#).
- **Cây làm việc:** 3 file luồng **untracked (`??`)**; `background.js`, `core.js`, `flow-shop.js` đang
  `M` (có thể việc dở phiên khác — KHÔNG đụng).
- **Vận hành extension:** launcher CHÉP extension ra thư mục GUID MỚI mỗi lần phóng trình duyệt
  (OrdersBridgeLauncher.cs:23-26 — né cache service worker MV3 theo extension ID) ⇒ code mới chỉ sống ở
  PHIÊN TRÌNH DUYỆT MỚI, không cần reload tay nhưng phép đo phải là lượt phóng mới. Nút "Dò DOM Flash
  Sale" chỉ HIỆN khi biến môi trường `SHOPEE_DEV_TOOLS=1` (AccountsViewModel.Phien.cs:1922).

## 2. Phạm vi

- **Làm (Đợt 1 + Đợt 2):**
  - Tạo `extensions/shopee-orders/flow-chung.js`.
  - `flow-km.js`: xoá hàm/hằng đã dời (thay bằng import); sửa chỗ gọi `cauLoiThamSo` thêm nhãn `"KM"`.
  - `flow-combo.js`: đổi 1 dòng import; xoá 3 hàm/1 hằng song sinh; sửa chỗ gọi `cauLoiThamSo` thêm
    nhãn `"Combo"`; cập nhật các câu comment trỏ "flow-km.js" tại chỗ import/gọi.
  - `flow-flashsale.js`: đổi khối import 2 dòng (40-41) + câu comment 38-39 cho khớp.
  - `suite/Shopee.Core.Tests`: file test MỚI `FlowChungTests.cs` (4 test, xem Đợt 2.4) + thử phá.
    KHÔNG sửa một file test cũ nào (kể cả comment — giữ tiêu chí "0 test cũ bị đụng" chấm được bằng git).
- **Không làm (lý do đo được):**
  - KHÔNG gom page-func trùng tên km/combo (Vue vs React — combo:35 cấm; thân khác thật).
  - KHÔNG gom `chuyenToiThang`/`datOThoiGian` (gọi page-func của bộ component riêng; bản km bị test neo;
    bản combo THIẾU guard "ngày hỏng ⇒ không bấm Xác nhận" của km — gom là đổi hành vi. Việc thêm guard
    cho combo là SỬA HÀNH VI, đã ghi sổ `P-20260829-2`).
  - KHÔNG gom `pageGoNhuNguoi`/`pageGoOReact` (setter khác bản chất — combo:31-34 `_valueTracker` React).
  - KHÔNG gom bộ máy modal + `TRAN_TRANG_MODAL` (km=105 vs combo=200, ràng buộc `ChoChang` C# —
    combo:69-72), `thamSoLuotDo`, khung b4/b5.
  - KHÔNG gom các khối TRONG THÂN hai hàm chạy chính (khung mở lượt, b1 sidebar, vòng 120 nhịp) — xem §6.
  - KHÔNG gom cặp entry `doDump*/doChay*` (đụng đường gửi gói của luồng chạy thật).
  - KHÔNG đụng `background.js`, `core.js`, `flow-shop.js` (đang `M`); Đợt 1-2 không cần đụng chúng
    (background.js vẫn import `doDumpKm/doChayKm` từ flow-km.js — hai export đó ở lại).
  - KHÔNG đổi tên bất kỳ hàm nào; KHÔNG dùng `export * from`.
  - `TRAN_CANH_BAO`: sau Đợt 1 tồn tại HAI hằng cùng tên cùng trị 10 (flow-chung.js export — cho km và
    fallback của `gomCanhBao`; combo giữ bản cục bộ combo:137 truyền tường minh). CHỐT: chấp nhận, đổi
    tối thiểu; thêm một dòng comment ở hằng combo trỏ về flow-chung.js; hợp nhất để vào §6.
- **Tuỳ chọn cần user gật riêng (KHÔNG trong đợt này):** Đợt 3 ở §6 — dời 2 luật EDS + sửa neo test.

## 3. Các bước thực hiện

### Đợt 0 — Mốc an toàn (BẮT BUỘC, cần user)

1. **User cho commit MỐC nguyên trạng** 3 file luồng (hiện untracked). Không mốc ⇒ không chứng minh được
   "di chuyển nguyên văn" bằng máy, không lùi được. (Đáng làm kể cả không refactor — sổ `P-20260829-1`.)
2. **User xác nhận** `background.js`/`core.js`/`flow-shop.js` đang `M` có phải việc dở phiên khác không.
3. Sau mốc: **mỗi đợt = MỘT commit riêng**; riêng Đợt 1 tách làm **HAI commit** (1a di chuyển nguyên văn,
   1b chỉ-sửa-comment) để diff máy đọc được từng loại thay đổi.
4. **Trình tự:** plan `2026-08-29-flash-sale-sap-xep-truoc-khi-lat-trang.md` đi TRƯỚC plan này (cả hai
   đụng khối import flow-flashsale.js + Shopee.Core.Tests).

### Đợt 1 — Tách hạ tầng dùng chung sang `flow-chung.js`

**Commit 1a — DI CHUYỂN NGUYÊN VĂN, không sửa một byte thân/comment của hàm dời:**

1. Tạo `extensions/shopee-orders/flow-chung.js`, header ghi: *"Hạ tầng dùng chung của ba luồng tạo
   chương trình (KM · Combo · Flash Sale). Mọi hàm DI CHUYỂN NGUYÊN VĂN từ flow-km.js ngày 2026-08-29 —
   thân không đổi một byte; luật 'THÂN HÀM KHÔNG ĐƯỢC SỬA' đi theo từng hàm."* Import:
   `import { send } from "./core.js";`
2. Dời nguyên văn (cả xmldoc/comment), giữ `export`:
   - `buoc` (km:1165-1214) + `urlCuaTab` (km:1140-1142, private);
   - `pageClickTheoSelector` (km:73-88) — GIỮ TÊN;
   - `pageDemPhanTuHien` (km:92-96);
   - `gomCanhBao` (km:1013-1063) + `export const TRAN_CANH_BAO = 10;` (km:59);
   - `soTrangLonNhat` (km:991-1011).
3. `flow-km.js`: xoá bản cũ, thêm
   `import { buoc, pageClickTheoSelector, pageDemPhanTuHien, gomCanhBao, soTrangLonNhat, TRAN_CANH_BAO } from "./flow-chung.js";`
   **KHÔNG re-export** — mọi importer đổi nguồn trực tiếp.
4. `flow-combo.js:53` → import 5 tên từ `./flow-chung.js`.
5. `flow-flashsale.js:40-41` → HAI import: `{buoc, pageClickTheoSelector, pageDemPhanTuHien}` từ
   `./flow-chung.js`; `{pageCaiLuatONgayEds, pageCaiLuatMuiTenThang}` VẪN từ `./flow-km.js` (neo test).
6. `background.js` KHÔNG đụng.

**Commit 1b — CHỈ COMMENT (diff chỉ được chứa dòng comment):**

7. Sửa các câu comment trỏ file nay đã sai tại CHỖ IMPORT/GỌI: flashsale:38-39 ("DÙNG LẠI của
   flow-km.js…" → nói rõ hai nguồn mới); combo:28 (buoc "mượn của flow-km.js") và combo:50-52; câu nào
   trong xmldoc của HÀM ĐÃ DỜI nói "export cho flow-combo.js dùng lại" vẫn đúng công dụng thì GIỮ.
8. Ghi nhận (KHÔNG sửa vì file bị cấm đụng / tiêu chí 0-test-cũ): mục lục module đầu `background.js`
   thiếu flow-chung.js; vài xmldoc C# nói pageClickTheoSelector "của flow-km.js" thành trỏ cũ — liệt kê
   vào báo cáo, dọn ở Đợt 3/§6.

**Kiểm sau đợt:** toàn bộ test hiện có xanh KHÔNG SỬA MỘT TEST NÀO; `git diff` 1a chứng minh di chuyển
nguyên văn; diff 1b chỉ có dòng comment; grep: combo/flashsale không còn import gì từ flow-km.js ngoài
2 luật EDS của flashsale.

### Đợt 2 — Gom hàm THUẦN song sinh về `flow-chung.js`

> Lật quyết định "hai luồng cố ý không import chéo" (combo:864-865) — CHỈ cho 3 hàm thuần không đụng
> DOM, qua module thứ ba (không phải import chéo). Comment "song sinh" hai phía viết lại, ghi ngày lật
> + danh sách hàm VẪN cố ý song sinh (chuyenToiThang, datOThoiGian, thamSoLuotDo, mọi page-func).

1. **`docMocNgay`**: dời BẢN KM (km:1065-1095 — bản đủ comment hơn) sang flow-chung.js, export; xoá bản
   combo (combo:870-898). **Khác biệt ĐÃ BIẾT giữa hai bản, khai trước để executor đối chiếu chứ không
   dừng oan:** phần CODE trùng từng byte; bản km dư 2 dòng comment nội bộ (km:1081-1082); xmldoc lệch
   chữ ("phép đọc-lại" vs "phép ĐỐI CHIẾU", combo có thêm câu "Bản song sinh…"). Luật DỪNG: chỉ dừng khi
   lệch **NGOÀI danh sách trên** (tức lệch phần code, hoặc lệch comment ở chỗ chưa khai).
2. **`mocNgayLuotDo` + `export const NGAY_DAT_TRUOC_LUOT_DO = 180;`**: thân hàm hai bản trùng 100%
   (đã diff máy); dời bản km (hằng km:1822 + xmldoc km:1817-1821 — bản 4 dòng đầy đủ; hàm km:1825-1833);
   xoá bản combo (combo:139-141, 915-923). Giữ câu "phải bằng `KmLichTuDong.SoNgayDatTruoc` bên C#".
3. **`cauLoiThamSo` — GIỮ NGUYÊN THÂN, chỉ tham số hoá NHÃN.** Bản hợp nhất:
   ```js
   export function cauLoiThamSo(loi, nhan) {
     const ds = Object.keys(loi || {}).map((k) => `${k}: ${loi[k]}`);
     return ds.length === 0 ? null : "tham số lượt " + nhan + " KHÔNG dùng được — " + ds.join(" · ");
   }
   ```
   (đúng thân km:1132-1135/combo:901-904, chỉ thay chữ "KM"/"Combo" bằng `nhan`). Chỗ gọi:
   km (trong `chayKmChoTabDangMo`, km:1379) → `cauLoiThamSo(tp.loi, "KM")`; combo (combo:1128) →
   `cauLoiThamSo(tp.loi, "Combo")`. ⚠ KHÔNG được "đơn giản hoá" thành nối chuỗi `+ loi` — `loi` là
   OBJECT (`{}` khi sạch ⇒ phải trả `null`; nối thẳng là mọi lượt sạch đỏ với "— [object Object]").
4. **Test mới — `suite/Shopee.Core.Tests/FlowChungTests.cs`** (dùng
   `FlashSaleDoDomTests.NguonHam("flow-chung.js", …)` — internal cùng assembly, gọi được — + Jint):
   1. `CauLoiThamSo_NguyenVanCu_KhongDoiMotKyTu` — truyền OBJECT đúng hợp đồng:
      `cauLoiThamSo({phanTram:"hỏng"}, "KM")` == `"tham số lượt KM KHÔNG dùng được — phanTram: hỏng"`;
      ca HAI trường khoá `join(" · ")`; `cauLoiThamSo({}, "KM")` == `null` (**đường chạy sạch hằng đêm —
      ca bắt buộc**); bản "Combo" tương tự.
   2. `CauLoiThamSo_CallSite_TruyenDungNhan` — Assert.Contains trên
      `NguonHam("flow-km.js", "chayKmChoTabDangMo")` chuỗi `cauLoiThamSo(tp.loi, "KM")` và trên
      `NguonHam("flow-combo.js", "chayComboChoTabDangMo")` chuỗi `cauLoiThamSo(tp.loi, "Combo")` —
      lưới cho rủi ro MỚI duy nhất mà tham-số-hoá sinh ra (nhãn tại chỗ gọi).
   3. `DocMocNgay_ChayThatQuaJint_CaDungCaHong` — 2-3 ca vào-ra (mốc đúng đủ 4 trường; `ngayDayDu` lệch
      chuỗi rời ⇒ lỗi có tên).
   4. `GomCanhBaoVaSoTrangLonNhat_ChayThatTuNguonMoi` — chạy Jint `gomCanhBao` (ca gom nhiều loại + ca
      chạm trần DÙNG FALLBACK `TRAN_CANH_BAO` — đúng chỗ fallback resolve theo MODULE MỚI sau khi dời)
      và `soTrangLonNhat` (ca nhãn có "…" + nhãn số) từ nguồn flow-chung.js — bịt lỗ "hai hàm không được
      lượt dò Flash phủ" (xem §5).
5. **Thử phá** (luật 4 — mỗi test ≥1 ca đỏ):
   | # | Phá (rồi khôi phục) | Test phải ĐỎ |
   |---|---|---|
   | 1 | Thân `cauLoiThamSo` bản chung: đổi khuôn chuỗi (bỏ `" · "` → `","`) | test 1 |
   | 2 | Chỗ gọi flow-km.js: `"KM"` → `"km"` | test 2 (test 1 vẫn xanh — chứng minh cần cả hai lưới) |
   | 3 | Đảo một vế kiểm trong `docMocNgay` bản chung | test 3 |
   | 4 | `gomCanhBao` bản chung: bỏ fallback `TRAN_CANH_BAO` (thay bằng số cứng 999) | test 4 (ca chạm trần) |
6. Viết lại comment "song sinh" (combo:864-865 chỗ cũ + chỗ mới trong flow-chung.js).

**Kiểm sau đợt:** test cũ xanh nguyên trạng + 4 test mới xanh + biên bản thử phá 4 ca; grep mỗi tên
(`docMocNgay`, `mocNgayLuotDo`, `NGAY_DAT_TRUOC_LUOT_DO`, `cauLoiThamSo`) chỉ còn MỘT định nghĩa trong
`extensions/`.

### Đợt kiểm chứng cuối (sau cả hai đợt)

1. `dotnet test suite/Shopee.Core.Tests` + `dotnet test orders/XuLyDonShopee.Tests` — xanh 100%,
   0 warning (`dotnet` PATH hỏng — dùng `C:\Users\Ng Xuan Mui\AppData\Local\Microsoft\dotnet\dotnet.exe`).
2. `dotnet build ShopeeSuite.sln` — 0 warning. Khai rõ cây đo (đang nhiễm file `M` phiên khác).
3. **Chạy thật AN TOÀN — nút Dò DOM Flash Sale** (lượt dò KHÔNG TẠO GÌ — 3 lưới test khoá:
   FlashSaleDoDomTests.cs:2162, 2188, 2260): đi qua đủ `buoc` (mọi bước b1-b10) + `pageClickTheoSelector`
   + `pageDemPhanTuHien` từ flow-chung.js + 2 luật EDS từ flow-km.js. **Điều kiện vận hành:** đặt
   `SHOPEE_DEV_TOOLS=1` trước khi mở app (nút chỉ hiện khi có biến này); phép đo PHẢI là lượt phóng
   trình duyệt MỚI sau khi sửa file (launcher chép extension ra thư mục GUID mới mỗi lần phóng — lượt
   Dò DOM Flash tự "mở Brave sạch" nên thoả, đừng đo trên phiên trình duyệt mở từ trước). Theo memory
   "tự chạy app và đọc log": tự mở app, chọn shop, bấm, tự đọc nhật ký.
   ⚠ KHÔNG dùng nút "Dò DOM KM" để nghiệm thu — nó TẠO CHƯƠNG TRÌNH THẬT (tiền lệ `DO DOM KM 1756…`).
4. `gomCanhBao`/`soTrangLonNhat` KHÔNG được lượt dò Flash phủ (flashsale không import chúng) — bằng
   chứng runtime của chúng là test 4 (Jint chạy từ nguồn mới) + lượt chạy ĐÊM đầu tiên của KM/Combo,
   user canh nhật ký (plan không tự kích hoạt).

## 4. Tiêu chí nghiệm thu

- [x] Đợt 0: mốc `c1b1cf4`; Đợt 1 = 2 commit (`914e329` 1a / `e559de6` 1b), Đợt 2 = `cc81f19`; vá sau
      phản biện = `ba86c56`.
- [x] Nghiệm thu diff máy: 7/7 hàm/hằng dời ở 1a nguyên văn từng byte (chỉ lệch header/vạch section/tiền
      tố `export ` đúng plan); diff 1b CHỈ chứa dòng comment. ĐIỀU CHỈNH đã khai: comment 1b của
      flow-combo.js dính cùng hunk với import Đợt 2 (`git add -p` là lệnh tương tác, môi trường không
      dùng được) nên đi cùng commit Đợt 2 — tính chất "1b chỉ comment" vẫn giữ.
- [x] KHÔNG file test C# cũ nào bị sửa trong cả 4 commit (git show --name-status; 2 file neo untracked
      chứng minh bằng mtime trước commit đầu); cả hai project test xanh 100%.
- [x] `FlowChungTests.cs`: 4 test gốc + 2 test bổ sung sau phản biện (mocNgayLuotDo 4 trường; khoá
      liên-ngôn-ngữ 180 ↔ `KmLichTuDong.SoNgayDatTruoc`) = 6 test xanh; thử phá 4 + 3 ca đều đỏ đúng
      test rồi khôi phục (nghiệm thu tái hiện ngẫu nhiên 2/4 ca gốc, khớp); grep 4 tên gom — mỗi tên MỘT
      định nghĩa trong `extensions/`.
- [x] Chuỗi lỗi tham số giữ nguyên văn theo hợp đồng OBJECT (kể cả `{}` ⇒ `null`); thêm guard thiếu nhãn
      ⇒ ném to tiếng (vá sau phản biện, có test + thử phá).
- [x] grep: combo hết import từ flow-km.js; flashsale chỉ còn 2 luật EDS từ flow-km.js; background.js
      không đổi trong cả 4 commit.
- [x] Số dòng: km 1902 → 1670 (−232 ≥ 150); combo 1631 → 1576 (−55 ≥ 40); tổng 4 file 6900 (+3 guard) so
      trần 6956 — còn GIẢM so 6916 gốc.
- [x] Lượt Dò DOM Flash Sale chạy thật (đo 10:28 ngày 29/08, app phóng mới với `SHOPEE_DEV_TOOLS=1`,
      user cho đóng app cũ): shop Alina Store1 — **10/10 bước ok**, dump
      `%APPDATA%\XuLyDonShopee\logs\flash-dom-20260829-102845.json`, không tạo gì. Chuỗi import mới sống
      thật trong service worker: `buoc`/`pageClickTheoSelector`/`pageDemPhanTuHien` chạy từ flow-chung.js,
      2 luật EDS từ flow-km.js. Ngay sau đó lượt check đơn thật cũng nối cầu + đọc 12 shop bình thường.
- [x] `dotnet build ShopeeSuite.sln` (sau khi user cho đóng app): **0 Warning / 0 Error** — đo 29/08,
      cây vẫn nhiễm ~36 file M của phiên khác (khai rõ; code của họ cũng compile sạch).

## 5. Rủi ro & lưu ý

- **Rủi ro số 1: chép tay lệch khi dời.** Chống: commit mốc trước, dời từng hàm, diff máy 1a; cấm "tiện
  tay" sửa format/comment trong commit 1a (comment sửa ở 1b).
- **ĐƯỜNG LÙI khi hỏng giữa chừng** (cây đang nhiễm file `M` của phiên khác):
  `git restore --source=<mốc> -- extensions/shopee-orders/flow-km.js extensions/shopee-orders/flow-combo.js extensions/shopee-orders/flow-flashsale.js`
  rồi XOÁ TAY `flow-chung.js` (file mới untracked — restore không đụng nó, bỏ quên là nó thành module mồ
  côi vẫn lọt lưới glob của ExtensionJsCuPhapTests). **CẤM `git reset --hard` / `git checkout .` toàn
  cây** — sẽ đè mất việc dở của phiên kia trên background.js/core.js/flow-shop.js.
- **`gomCanhBao` fallback hằng module**: dời cùng file với `TRAN_CANH_BAO` (Đợt 1.2); combo giữ hằng cục
  bộ (combo:137) — hai hằng cùng tên cùng trị là CÓ CHỦ ĐÍCH tạm thời, comment trỏ nhau, hợp nhất ở §6.
- **`buoc` mang mặc định `nhan = "dò KM"`** (km:1160-1163 khoá cả mặc định) — dời nguyên văn giữ đúng.
- **Import 2 dòng flashsale**: giữ đủ 5 tên chia 2 nguồn — lưới `MoiImportTenGoi_PhaiCoExportTuongUng`
  đỡ sau lưng nhưng tự soát trước.
- **Phủ runtime:** `buoc`/`pageClickTheoSelector`/`pageDemPhanTuHien` được lượt Dò DOM Flash phủ thật;
  `gomCanhBao`/`soTrangLonNhat` KHÔNG — phải trông vào test 4 + đêm chạy đầu user canh. Đó là lý do
  test 4 là bắt buộc, không phải trang trí.
- **Hai plan cùng file**: plan flash-sale đi TRƯỚC; nếu đảo thứ tự, người thực thi plan kia phải đọc lại
  khối import mới của flashsale.
- **Phiên song song**: thấy `background.js`/`core.js`/`flow-shop.js` đổi thêm trong lúc thực thi thì
  KHÔNG liên quan — đừng "tiện tay" sửa lỗi build do chúng gây ra.
- Tài liệu tạm lệch sau Đợt 1 (đã cân nhắc, chấp nhận): mục lục module đầu `background.js` thiếu
  flow-chung.js (file bị cấm đụng); vài xmldoc C# nói "của flow-km.js" cho hàm đã dời (giữ tiêu chí
  0-test-cũ-bị-sửa) — liệt kê trong báo cáo, dọn ở Đợt 3.
- Quyết định để ngỏ cho người thực thi: vạch section trong `flow-chung.js`.

## 6. Hướng mở rộng KHÔNG thuộc plan này (user quyết riêng, từng cái)

1. **Đợt 3:** dời 2 luật EDS sang flow-chung.js + sửa neo `EdsONgayHopLeTests.cs:32`,
   `FlashSaleDoDomTests.cs:28,113,824` + dọn các xmldoc/mục lục tạm lệch ở §5 — cơ học nhưng đụng lưới
   khoá, cần user gật.
2. **Gom khối khung trong thân hai hàm chạy chính** (~90-130 dòng) — chỉ sau vài đêm chạy xanh nền Đợt 1-2.
3. **Giảm boilerplate tìm-modal trong page-func** bằng mẫu "cài luật lên window" (~110-130 dòng/file kể
   cả comment) — sửa thân hàng loạt page-func luồng chạy đêm; nếu làm, làm combo trước.
4. **Hợp nhất `TRAN_CANH_BAO`** (combo bỏ hằng cục bộ, import từ flow-chung) — một dòng, gộp vào Đợt 3.
5. **Guard `datOThoiGian` cho combo** — sửa hành vi, sổ `P-20260829-2`.

---

## Báo cáo thực thi (chốt 2026-08-29, phiên chính tổng hợp)

**4 commit:** `914e329` (1a — dời nguyên văn 7 hàm/hằng sang flow-chung.js, kiểm sed-diff 4 khối rỗng),
`e559de6` (1b — comment flashsale; phần comment combo dính hunk import nên đi cùng Đợt 2), `cc81f19`
(Đợt 2 — gom docMocNgay/mocNgayLuotDo/NGAY_DAT_TRUOC_LUOT_DO/cauLoiThamSo, FlowChungTests.cs 4 test,
thử phá 4/4), `ba86c56` (vá 3 lỗ lưới sau phản biện).

- **Nghiệm thu (độc lập):** 12 ĐẠT / 2 ĐẠT MỘT PHẦN / 2 CHƯA ĐO / 0 KHÔNG ĐẠT; tự chạy 416/416 +
  2264/2264; tự diff máy từng hàm dời; tái hiện ngẫu nhiên 2/4 ca phá — khớp.
- **Phản biện (độc lập):** KHÔNG lỗi NẶNG/CHẶN — chứng minh 8/8 hàm dời + 9/9 hàm giữ IDENTICAL từng ký
  tự; nạp thật 4 module Node ESM OK; không vòng import; không tên hở. 3 phát hiện lưới đã VÁ ở `ba86c56`:
  (i) mocNgayLuotDo + hằng 180 không test nào chạm (sửa lệch tháng thì mọi test vẫn xanh) → 2 test mới;
  (ii) regex `NguonHang` neo `$` đỏ giả trên checkout CRLF → bỏ neo; (iii) cauLoiThamSo thiếu guard nhãn
  → ném to tiếng. Sau vá: Core.Tests 416 → **418 xanh, 0 warning**; thử phá 3/3 đỏ đúng test.
- **Khẳng định phản biện bị BÁC bằng số đo:** "flashsale gọi `buoc` chỉ 3 tham số ⇒ mất chốt captcha"
  — SAI: grep ra 17/17 lời gọi đều truyền `, tabId, NHAN);`. Không ghi sổ mục đó. (Riêng nhánh
  captcha-TRƯỚC-bước của `buoc` hardcode `[KM]` là THẬT, có từ trước — sổ `P-20260829-9`.)
- **Số đo cây:** cây đang nhiễm ~36 file M + nhiều ?? của phiên khác; hai project test chỉ phụ thuộc cây
  sạch của việc này nên số ít nhiễu — vẫn khai rõ.

**Còn lại:** 2 mục CHƯA ĐO ở §4 (Dò DOM Flash chạy thật; build sln trọn). **Việc treo cần user quyết**
(sổ `P-20260829-8`): `EdsONgayHopLeTests.cs` + `FlashSaleDoDomTests.cs` vẫn untracked trong khi
`FlashSaleLuongThatTests.cs`/`FlowChungTests.cs` ĐÃ commit và gọi helper của chúng ⇒ HEAD chưa tự chứa
cho một máy checkout mới — nên commit 2 file đó (kèm soát csproj đang M) khi xác nhận không phải việc dở
của phiên khác.
