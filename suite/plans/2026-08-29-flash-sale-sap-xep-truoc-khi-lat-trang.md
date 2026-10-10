# Plan: Flash Sale — RÀNG BUỘC "sắp xếp ĐẠT rồi mới lật tới trang k" + kiểm sort còn đứng trên từng trang

- **Ngày:** 2026-08-29
- **Trạng thái:** hoàn thành (trừ 2 mục CHƯA ĐO khách quan — xem Báo cáo thực thi cuối file)
- **Người lập:** Fable · **Người thực thi:** Opus (`opus-dev`)
- **Đã qua phản biện plan:** 4 lăng kính độc lập (lưới test cũ · ca biên · khớp code thật · tính đầy đủ),
  toàn bộ phát hiện đã gộp vào bản này.

## 1. Bối cảnh & mục tiêu

Yêu cầu của user (29/08/2026): *"refactor phần flash sale, khi chuyển sang khung thứ k thì bắt buộc phải
sắp xếp trước rồi mới chuyển sang page k"*.

**Hiện trạng** (`extensions/shopee-orders/flow-flashsale.js`, luồng thật `chayFlashSaleChoTabDangMo`):
mỗi vòng khung giờ chạy bước D (mở modal sản phẩm + bấm ĐÚNG `SO_LAN_BAM_SAP_XEP` = 2 cú `down` cột
Doanh số) → bước E (cửa kiểm `dayDoanhSoKhongTangDan`, fail-closed, đọc dãy Doanh số **trang 1**) →
bước F (lật tới trang k = `trangChoKhung(soKhungDaTao)`, tick "chọn tất cả", Xác nhận modal). Tức thứ tự
"sắp xếp trước, lật sau" **đã đúng theo control-flow**, nhưng có ba lỗ:

1. **Thứ tự chỉ được giữ bằng vị trí dòng code.** Bước F không đòi bằng chứng nào rằng cửa kiểm E đã ĐẠT —
   ai đó sửa sau này (đảo bước, bỏ bước E, thêm nhánh đi tắt) thì không test nào đỏ. "Bắt buộc" của user
   nghĩa là phải thành RÀNG BUỘC có lưới, không phải quy ước.
2. **Sau khi lật trang, không ai đọc lại Doanh số.** Cửa E chỉ chấm trang 1. Nếu sort tụt mất trong lúc
   phân trang, trang k sẽ bị tick 10 sản phẩm SAI trong im lặng — đúng lớp lỗi "âm thầm nguy hiểm nhất"
   mà plan 2026-08-27 §3.1 sinh ra để chặn. Hiện chỉ có lưới "bảng phải ĐỔI sau cú lật"
   (`choBangDoiRoiLang`), lưới đó không nói gì về THỨ TỰ.
3. **Không kiểm đã đứng ĐÚNG trang k trước khi tick** — chỉ đếm số cú lật thành công.

**Mục tiêu:** biến "sắp xếp trước → lật sau" thành ràng buộc CÓ CỬA + CÓ TEST, và kiểm chứng CHỈ-ĐỌC trên
từng trang đã lật qua. Nói cho đúng bản chất phép đo: các cửa này **phát hiện bằng chứng NGƯỢC với giảm
dần** (một cặp tăng trong trang, hoặc trang sau mở cao hơn trang trước kết). Dãy tình cờ không-tăng (nhiều
giá trị bằng nhau/nhiều 0) thì không phân biệt được với sort thật — với dữ liệu ấy mọi thứ tự là như nhau
nên cũng không có gì bị chọn sai; cửa xanh KHÔNG phải bằng chứng dương tuyệt đối, và người đọc nhật ký
phải hiểu như vậy.

### Ràng buộc BẤT DI BẤT DỊCH (đã có lưới test, tuyệt đối không phá)

- **Đúng 2 cú bấm sắp xếp, không bao giờ 3, không retry quanh cú bấm.** Mọi kiểm mới là CHỈ ĐỌC — không
  đẻ thêm bất kỳ đường bấm sắp xếp nào. Cú thứ ba xoá sạch sắp xếp (đo thật lượt 11: M3 = M0).
- **KHÔNG di chuyển vòng bấm sắp xếp ra khỏi `chayFlashSaleChoTabDangMo`.** Test
  `LuongThat_DungHaiCuBamSapXep_KhongBaoGioBa` (FlashSaleLuongThatTests.cs) đếm bằng AST số lời gọi
  `chay(pageBamSapXepDoanhSo, …)` trong NGUỒN CỦA CHÍNH HÀM ĐÓ (phải = 1) và đòi nguyên văn
  `for (let i = 0; i < SO_LAN_BAM_SAP_XEP; i++)` nằm trong đó.
- **Nguồn hàm luồng thật không được chứa chuỗi `thuLai` / `bamLai`** (cùng test trên assert
  DoesNotContain) — đặt tên biến mới phải né hai token này.
- **Cửa kiểm nhận TRỌN GÓI đọc được** (`{coCot, soDong, giaTri, loi}`), không bóc mảng trần — bài học
  fail-open của bản đầu `dayDoanhSoKhongTangDan` (test `ChoGoiCuaKiem_TruyenTronGoiDoc_KhongBocMangTran`).
- **Fail-closed:** không đọc được ⇒ HỎNG có tên, không phải "đạt vì không có gì để so". Và KHÔNG có nhánh
  "bấm thêm một cú cho chắc" — trạng thái sai là ĐỎ, không phải "chưa xong".
- **Khuôn gói kết quả `flashKetQua` không đổi** (test `GoiKetQuaLuongThat_CungKhuonKmKetQua_NhungKindRieng`)
  — C# không phải sửa gì.
- **Hàm THUẦN mới phải là `export function` top-level** (FunctionDeclaration) — `NguonHam` của test bóc
  bằng Acornima chỉ nhận dạng đó, không nhận `const f = () => {}`.
- **Page-func serialize độc lập** (exec.js): không rút helper ra khỏi thân page-func nào.
- Tên bước (`${K}.6 Trang …`) giữ nguyên.
- Lưới whitelist cú bấm `MoiHamBamChuot_PhaiNamTrongWhitelist` + `KhongCoDuongNaoBamNutTaoThat`
  (FlashSaleDoDomTests.cs) quét **mọi hàm top-level của cả file** — hàm thuần mới không có `.click()`
  nên tự xanh; mảng `HamDuocBamChuot` KHÔNG được phải sửa (phải sửa tức là đã mọc đường bấm mới — sai).

### Trạng thái cây làm việc (đo được lúc lập plan)

`flow-flashsale.js`, `FlashSaleLuongThatTests.cs`, `FlashSaleDoDomTests.cs` đều **untracked (`??`)** —
chưa có commit nào làm mốc; `Shopee.Core.Tests.csproj`, `ShopeeSuite.sln` và ~37 file khác đang `M`
(việc dở của các phiên khác trên cùng repo). Hệ quả:

- "Khôi phục sau thử phá" KHÔNG chứng minh được bằng `git diff` với file untracked ⇒ **trước khi thử phá,
  chụp bản sao 3 file này vào scratchpad làm baseline**, khôi phục xong so bằng `fc`/`diff` với bản chụp.
- Số build/test đo trên cây ĐANG NHIỄM việc dở của phiên khác — báo cáo phải khai rõ. Cần số sạch thì
  `git worktree` + CHÉP TAY các file untracked của việc này vào (worktree từ HEAD không chứa chúng).
- ĐỀ NGHỊ (user quyết): commit một mốc cho cụm KM/Combo/FlashSale hiện tại TRƯỚC khi thực thi plan này.

## 2. Phạm vi

- **Làm:**
  - `extensions/shopee-orders/flow-flashsale.js`: 3 hàm THUẦN mới + sửa thân bước F (và một dòng hoisting
    ở bước E) trong `chayFlashSaleChoTabDangMo`.
  - `suite/Shopee.Core.Tests/FlashSaleLuongThatTests.cs`: test Jint cho 3 hàm mới + test NGUỒN (AST) khoá
    thứ tự "cổng trước — lật sau — tick cuối".
  - Thử phá đủ TỪNG test mới (luật 4, ma trận phá ở 3.5).
- **Không làm:**
  - KHÔNG đụng lượt DÒ (`doDomFlashSaleChoTabDangMo`) và các ràng buộc của nó.
  - KHÔNG đụng `flow-km.js`, `flow-combo.js`, C# (`orders/`, `server/`, `suite/` ngoài file test nêu trên).
  - KHÔNG đổi `SO_LAN_BAM_SAP_XEP`, không đổi `dayDoanhSoKhongTangDan` (kể cả luật 3 "lệch cột" của nó
    — xem rủi ro §5), không đổi khuôn kết quả, không đổi tên bước, không thêm lượt bấm nào.
  - KHÔNG thêm cơ chế chờ mới — dùng lại `choBangDoiRoiLang` / `pageDocDoanhSoMoiDongFlash` /
    `pageDemTickFlash` sẵn có.

## 3. Các bước thực hiện

### 3.1. Hàm THUẦN `congLatTrang(cuaTrang1)` — "được phép lật khỏi trang 1 chưa"

`export function congLatTrang(cuaTrang1)` (đặt ngay dưới `dayDoanhSoKhongTangDan`):

- Nhận **NGUYÊN KẾT QUẢ** mà `dayDoanhSoKhongTangDan` trả cho trang 1 (`{dat, so, cap, soDong, ly}`).
- Trả `{dat:true, soCuoi, ly}` **CHỈ KHI** `cuaTrang1 && cuaTrang1.dat === true`; `soCuoi` = phần tử CUỐI
  của `cuaTrang1.so` (hoặc `null` nếu mảng rỗng — ca trang ĐẠT mà không đọc được số nào).
- Mọi ca khác (`null`/`undefined`/`dat` không phải `true`/mất trường) ⇒ `{dat:false, soCuoi:null, ly}` với
  lý do CÓ TÊN: *"chưa có cửa kiểm sắp xếp ĐẠT cho trang 1 — KHÔNG lật trang"*. Fail-closed: quên gọi cửa
  cũng bị chặn, không chỉ cửa đỏ.

### 3.2. Hàm THUẦN `cuaKiemTrangSauLat(docTrangNay, soCuoiTrangTruoc)` — "sort còn đứng ở trang vừa đáp"

`export function cuaKiemTrangSauLat(docTrangNay, soCuoiTrangTruoc)`:

- **Vế trong-trang:** gọi CHÍNH `dayDoanhSoKhongTangDan(docTrangNay)` — một nguồn luật, không chép phép
  parse/so sánh lần hai. Cửa con hỏng ⇒ hỏng luôn, mang nguyên `ly` của nó.
- **Vế NỐI TIẾP giữa trang:** lấy `so` (dãy ĐÃ LỌC — bỏ dòng không đọc được, KHÔNG coi rỗng là 0) từ kết
  quả cửa con. Điều kiện chạy vế này: `typeof soCuoiTrangTruoc === "number" && isFinite(soCuoiTrangTruoc)`
  và `so.length > 0` — **TUYỆT ĐỐI không kiểm truthy** (`if (soCuoiTrangTruoc)` nuốt mất số `0`, mà `0` là
  giá trị hợp lệ và phổ biến nhất; test 4 ở 3.4 khoá đúng ca này). `so[0] > soCuoiTrangTruoc` ⇒
  `{dat:false, ly}` nêu rõ cặp vỡ (*"trang trước kết ở X mà trang này mở ở Y > X — thứ tự giảm dần đã đứt
  giữa hai trang"*). Hoà (`so[0] === soCuoiTrangTruoc`) là ĐẠT.
- **Chuỗi hoá:** trả `{dat, soCuoi, ly}` — `soCuoi` = phần tử cuối của `so`; khi trang ĐẠT mà `so` rỗng
  thì **kế thừa** `soCuoiTrangTruoc` (viết tường minh bằng kiểm `so.length`, không dùng `|| null` — cùng
  bẫy falsy).
- `soCuoiTrangTruoc` không phải số (mắt xích đầu) ⇒ chỉ chấm vế trong-trang.

### 3.3. Hàm THUẦN `cuaDungTrangDich(trangHienTai, trangDich)` — "đã đứng đúng trang k chưa"

`export function cuaDungTrangDich(trangHienTai, trangDich)` — luật 4 nhánh, tách THUẦN vì đây là hành vi
mới có nhánh quyết định, để inline trong bước F là không lưới nào chạm được:

- `trangDich === 1` ⇒ `{dat:true, canhBao:null, ly:"trang 1 — không lật, cửa E vừa chấm xong"}`.
- nhãn đọc được và `=== String(trangDich)` ⇒ `{dat:true, canhBao:null}`.
- nhãn đọc được mà LỆCH ⇒ `{dat:false, canhBao:null, ly:"đứng ở trang X, không phải trang k=Y — không tick"}`.
- nhãn `null`/rỗng (markup nhãn active đổi) ⇒ `{dat:true, canhBao:"không đọc được nhãn trang active — đã
  có hai lưới khác (bảng ĐỔI + cửa Doanh số) đứng trước"}` — cảnh báo, KHÔNG đỏ (đỏ ở đây là đỏ oan theo
  markup).

### 3.4. Sửa `chayFlashSaleChoTabDangMo` — bước E hoisting + bước F đi qua ba cửa

1. **Bước E:** khai `let cuaTrang1 = null;` ở scope vòng khung (cạnh `let docDoanhSo = null;`), trong thân
   bước E gán `cuaTrang1 = cua;`. Không đổi gì khác ở E.
2. **Bước F — đầu bước, TRƯỚC vòng lật:** `const cong = congLatTrang(cuaTrang1);` — `!cong.dat` ⇒
   `return { …, loi: cong.ly, kyVong: "co", docLai: "khong" }` (bước ĐỎ, không lật, không tick). Khởi tạo
   `let soCuoiTrangTruoc = cong.soCuoi;`.
3. **Bước F — trong vòng lật** (`for t = 1 .. trangDich-1`), SAU `choBangDoiRoiLang` + kiểm `daDoi` như
   cũ, thêm:
   ```js
   const docTrang = await chay(pageDocDoanhSoMoiDongFlash, []);
   const cuaTrang = cuaKiemTrangSauLat(docTrang, soCuoiTrangTruoc);
   ```
   - ⚠ Phần tử `nhatKyLat` của cú lật này **ĐÃ được push trước chỗ kiểm `daDoi`** (dòng
     `nhatKyLat.push({ tuTrang: t, … })` hiện tại) — **GÁN BỔ SUNG** trường vào chính phần tử đó
     (`nhatKyLat[nhatKyLat.length - 1].doanhSo = …; ….cua = { dat, ly }`), KHÔNG push phần tử mới
     (push mới là dump đổi hình dạng, 2 phần tử/lật).
   - `!cuaTrang.dat` ⇒ return bước ĐỎ với `loi` = *"sau khi lật tới trang (t+1), dãy Doanh số không còn
     giảm dần: "* + `cuaTrang.ly`. KHÔNG có nhánh sửa chữa (không bấm sắp xếp lại — cấm tuyệt đối).
   - Đạt ⇒ `soCuoiTrangTruoc = cuaTrang.soCuoi;` và đi tiếp.
4. **Bước F — trước cú tick, CHỈ KHI `trangDich > 1`:** đọc `pageDemTickFlash` MỘT lượt (chỉ để lấy
   `trangHienTai`) rồi `const dung = cuaDungTrangDich(dem.trangHienTai, trangDich);`:
   - `!dung.dat` ⇒ bước ĐỎ với `loi = dung.ly`;
   - `dung.canhBao` ⇒ `canhBao.push(K + ": " + dung.canhBao)`;
   - **`trangDich === 1` ⇒ KHÔNG gọi lượt đọc này** (không đọc, không kiểm, không cảnh báo) — khung 1
     không đổi một lượt gọi nào ngoài `congLatTrang` thuần.
5. `ghiChu` của bước F nói rõ khuôn mới: *"lật qua từng trang có cửa Doanh số chỉ-đọc — cửa phát hiện
   bằng chứng NGƯỢC với giảm dần, vỡ ở đâu dừng ở đó"*. KHÔNG đổi tên bước.
6. **Đặt tên biến tránh token cấm:** không dùng `thuLai`/`bamLai`.

Chi phí thêm mỗi khung k: `(k−1)` lượt `pageDocDoanhSoMoiDongFlash` + `(k>1 ? 1 : 0)` lượt
`pageDemTickFlash` — toàn chỉ-đọc, k ≤ 8, không thêm trần chờ nào. Khung 1: chỉ thêm một lời gọi hàm
thuần, không thêm round-trip nào.

### 3.5. Test mới trong `FlashSaleLuongThatTests.cs`

Thêm `congLatTrang`, `cuaKiemTrangSauLat`, `cuaDungTrangDich` vào mảng của `MayLuatThuan()` (Jint resolve
tên hàm toàn cục LÚC GỌI nên thứ tự nạp không quan trọng — thêm vào cuối mảng cho dễ đọc).

1. `CongLatTrang_ChiMoKhiCuaTrang1Dat` — ma trận: `{dat:true, so:[14,…,2]}` ⇒ mở + `soCuoi = 2`;
   `{dat:false}` ⇒ đóng; `null`/`undefined`/`{}` ⇒ đóng, `ly` có chữ "KHÔNG lật"; `{dat:true, so:[]}` ⇒
   mở + `soCuoi = null`.
2. `CuaTrangSauLat_GiamNoiTiep_ThiDat` — trang trước kết `3`, trang này `{3,2,2,1}` ⇒ đạt, `soCuoi = 1`;
   hoà biên (`2` → `{2,2}`) ⇒ đạt.
3. `CuaTrangSauLat_MoLonHonKetTrangTruoc_ThiHong` — trang trước kết `2`, trang này `{5,4}` ⇒ hỏng, `ly`
   nêu cặp `2` và `5`.
4. `CuaTrangSauLat_TrangTruocKet0_TrangNayMoLonHon_ThiHong` — `soCuoiTrangTruoc = 0`, trang này `{5,4}` ⇒
   hỏng nêu cặp `0` và `5`. **Chốt chống bug 0-falsy**: bản cài `if (soCuoiTrangTruoc && …)` sẽ cho ĐẠT
   sai ở đúng ca này (0 là giá trị phổ biến nhất của cột Doanh số).
5. `CuaTrangSauLat_TrongTrangTang_ThiHong` — `{2,3}` ⇒ hỏng (mượn nguyên luật cửa con — chứng minh không
   chép luật: sửa `dayDoanhSoKhongTangDan` là ca này đổi theo).
6. `CuaTrangSauLat_KhongDocDuocCot_ThiHong` — `{coCot:false}` ⇒ hỏng (fail-closed xuyên qua cửa con).
7. `CuaTrangSauLat_TrangKhongCoSoNaoNhungDat_KeThuaSoCuoi` — trang 1 dòng không đọc được số, trang trước
   kết `4` ⇒ đạt và `soCuoi` vẫn `4`.
8. `CuaTrangSauLat_ToanKhong_HaiTrang_VanDat` — `0` → `{0,0,…}` ⇒ đạt (shop 0 doanh số không đỏ oan).
9. `CuaDungTrangDich_MaTranNamNhanh` — khớp ⇒ đạt; nhãn SỐ THUẦN lệch ⇒ hỏng `ly` nêu hai số trang;
   nhãn `null`/rỗng ⇒ đạt kèm `canhBao`; nhãn có nội dung mà KHÔNG phải số thuần ("Trang 3", "3/8"…) ⇒
   đạt kèm `canhBao` nêu nguyên văn nhãn (chỉ số thuần mới được kết tội — nới sau phản biện, vì định
   dạng nhãn pager chưa từng có số đo); `trangDich = 1` ⇒ đạt không cảnh báo.
10. `LuongThat_CongTruocLat_CuaTruocTick` — **AST, KHÔNG indexOf chuỗi** (chuỗi thô dính comment nhắc tên
    hàm — đúng bài học `DemLoiGoiBamSapXep` đã ghi). CẢ BA VẾ chạy trên
    `NguonHam(FileFlash, "chayFlashSaleChoTabDangMo")` — **tuyệt đối không đếm trên NguonFile**: lượt DÒ
    cũng gọi `pageDocDoanhSoMoiDongFlash` (hiện ở ~dòng 2251) nên đếm cả file ra 3 và test sai từ lúc sinh.
    Ba vế, so vị trí bằng `Node.Range.Start` của CallExpression (khuôn `DemLoiGoiBamSapXep`):
    - (v1) có đúng 1 CallExpression callee `congLatTrang`, đứng TRƯỚC CallExpression
      `chay(pageSangTrangSauFlash,…)` đầu tiên;
    - (v2) có ≥1 CallExpression callee `cuaKiemTrangSauLat`, đứng TRƯỚC CallExpression
      `chay(pageTickChonTatCaFlash,…)`;
    - (v3) số CallExpression `chay(pageDocDoanhSoMoiDongFlash,…)` trong nguồn hàm = **2** (một ở bước D,
      một trong vòng lật).
11. `LuongThat_CuaCoRangCuong_KhongPhaiTrangTri` (bổ sung sau phản biện — lưới 10 chỉ khoá "có gọi +
    đúng thứ tự", không khoá phần CƯỠNG CHẾ): AST thuần trên cùng nguồn hàm — (a)(b) tồn tại
    `IfStatement` phủ định `cong.dat` / `cuaTrang.dat` với `ReturnStatement` trong consequent; (c) mọi
    lời gọi `cuaKiemTrangSauLat` có đối số 2 là Identifier `soCuoiTrangTruoc`; (d) tồn tại phép gán
    `soCuoiTrangTruoc = cuaTrang.soCuoi`.

Bổ sung sau phản biện (đã thực thi): `cuaKiemTrangSauLat` trả thêm cờ `viThuTu` (true = hỏng vì THỨ TỰ:
cặp tăng trong trang hoặc nối tiếp vỡ; false = hỏng vì KHÔNG ĐỌC ĐƯỢC; null khi đạt — phân biệt bằng
`cap >= 0` của cửa con, không đoán qua chuỗi) và bước F chọn câu lỗi theo cờ: "dãy Doanh số không còn
giảm dần" chỉ nói khi đúng là chuyện thứ tự, còn lại nói "KHÔNG kiểm được thứ tự ở trang N" — người đọc
nhật ký không bị dẫn nhầm sang nghi cú bấm khi thủ phạm là phép đọc. Các test 2–8 có assert cờ tương ứng;
ca luật-3 (lệch cột) gộp thêm vào test 6.

**Chạy lại nguyên trạng, không sửa một ký tự** (phải vẫn xanh): trong `FlashSaleLuongThatTests.cs` —
`LuongThat_DungHaiCuBamSapXep_KhongBaoGioBa`, `CuBamThuBaCuaLuotDo_KhongLotSangLuongThat`, toàn bộ
`CuaKiemDoanhSo_*`, `TrangChoKhung_*`, `GoiKetQuaLuongThat_*`; trong `FlashSaleDoDomTests.cs` (lưới của
lượt DÒ + lưới quét cả file, không liên quan sửa đổi này nhưng là lưới chặn) —
`SapXep_KhongCoCoCheThuLaiQuanhCuBam`, `MoiHamBamChuot_PhaiNamTrongWhitelist`,
`KhongCoDuongNaoBamNutTaoThat`.

### 3.6. Thử phá (luật 4 — ma trận: MỖI test mới có ít nhất một ca làm nó ĐỎ)

Trước khi phá: chụp `flow-flashsale.js` + 2 file test vào scratchpad làm baseline (file untracked — không
có `git diff` để chứng minh khôi phục); phá xong so lại bằng `fc` với bản chụp.

| # | Phá gì (rồi khôi phục) | Test phải ĐỎ |
|---|---|---|
| 1 | `congLatTrang`: đảo `=== true` thành `!== true` | test 1 |
| 2 | `cuaKiemTrangSauLat`: `so[0] > soCuoi…` thành `>=` | test 2 (hoà biên) **và** test 8 (0 ≥ 0) |
| 3 | Xoá hẳn vế nối tiếp trong `cuaKiemTrangSauLat` | test 3 và test 4 |
| 4 | Đổi kiểm `typeof soCuoiTrangTruoc === "number"` thành kiểm truthy | test 4 (test 3 vẫn xanh vì 2 truthy — đó chính là chứng minh test 4 không thừa) |
| 5 | Thay lời gọi cửa con trong `cuaKiemTrangSauLat` bằng `{dat:true, so:[]}` cứng | test 3, 5, 6 |
| 6 | Đổi nhánh kế thừa `soCuoi` thành trả `null` | test 7 |
| 7 | `cuaDungTrangDich`: đảo nhánh "lệch ⇒ hỏng" thành đạt | test 9 |
| 8 | Xoá lời gọi `congLatTrang` khỏi bước F | test 10 (v1) |
| 9 | Xoá lời gọi `cuaKiemTrangSauLat` khỏi vòng lật (giữ mọi thứ khác) | test 10 (v2) |
| 10 | Thêm tạm một lượt `chay(pageDocDoanhSoMoiDongFlash, [])` thứ ba vào luồng thật | test 10 (v3) |

Biên bản thử phá ghi từng dòng: ca phá → test đỏ (tên + message) → đã khôi phục (fc khớp baseline).

### 3.7. Build + test + chạy thật

- `dotnet test suite/Shopee.Core.Tests/Shopee.Core.Tests.csproj` — xanh toàn bộ, 0 warning. (`dotnet`
  trên PATH hỏng — dùng `C:\Users\Ng Xuan Mui\AppData\Local\Microsoft\dotnet\dotnet.exe`.)
- `dotnet build ShopeeSuite.sln` — 0 warning. **Khai rõ trong báo cáo: số đo trên cây đang nhiễm việc dở
  của phiên khác** (xem §1); cần số sạch thì worktree + chép các file untracked của việc này.
- Chạy thật theo memory "tự chạy app và đọc log": mở app, chọn shop, chạy lượt flash sale, đọc nhật ký.
  Kỳ vọng: shop toàn 0 doanh số KHÔNG đỏ oan; bước `${K}.6` hỏng (nếu có) phải ra câu `loi` có tên.
  ⚠ Đọc nhật ký cho đúng khuôn C# hiện có: bước F đỏ ở **khung 1** ⇒ dòng `HỎNG — hỏng ở bước: K1.6…`;
  bước F đỏ ở **khung ≥ 2** ⇒ `daTao` vẫn `true` nên C# hiện nó thành **⚠ cảnh báo** *"báo TẠO ĐƯỢC nhưng
  vẫn có bước hỏng: K{k}.6…"* chứ KHÔNG phải dòng HỎNG — đó là hành vi sẵn có của khuôn `kmKetQua`,
  không phải lưới mới bị nuốt.

## 4. Tiêu chí nghiệm thu

- [x] `dotnet test suite/Shopee.Core.Tests/Shopee.Core.Tests.csproj` xanh 100% — **412/412, 0 warning**
      (401 trước vá); đủ 11 test mới ở 3.5 đúng tên; test khoá cũ không bị sửa (FlashSaleDoDomTests.cs
      mtime trước mốc, các test cũ trong FlashSaleLuongThatTests.cs còn nguyên tên + xanh).
- [x] Test 10 chạy trên `NguonHam("flow-flashsale.js", "chayFlashSaleChoTabDangMo")`, so vị trí bằng AST
      (`Node.Range.Start`), không indexOf chuỗi thô (nghiệm thu soi: FlashSaleLuongThatTests.cs:829-869).
- [x] AST đếm trong `chayFlashSaleChoTabDangMo`: `chay(pageBamSapXepDoanhSo,…)` = 1;
      `chay(pageDocDoanhSoMoiDongFlash,…)` = 2; không `thuLai`/`bamLai`; nguyên văn
      `for (let i = 0; i < SO_LAN_BAM_SAP_XEP; i++)` còn nguyên.
- [x] Biên bản thử phá **16 ca** (10 ca bảng 3.6 + 6 ca bổ sung sau phản biện) — mỗi lưới ≥1 ca đỏ,
      khôi phục kiểm bằng hash/git diff; nghiệm thu tái hiện ngẫu nhiên 2/10 ca (2, 7) khớp chính xác.
      Biên bản đầy đủ ở Báo cáo thực thi cuối file.
- [x] Khung 1 (trangDich = 1) không thêm round-trip: vòng lật không chạy, cửa nhãn bọc `if (trangDich > 1)`
      — nghiệm thu xác nhận bằng đọc code + test 9 nhánh `trangDich = 1`.
- [x] `MoiHamBamChuot_PhaiNamTrongWhitelist` + `KhongCoDuongNaoBamNutTaoThat` xanh, mảng
      `HamDuocBamChuot` không phải sửa.
- [x] `dotnet build ShopeeSuite.sln` (sau khi user cho đóng app, 29/08): **0 Warning / 0 Error** — cây
      vẫn nhiễm ~36 file M của phiên khác, khai rõ.
- [ ] **CHƯA ĐO** — Chạy thật LUỒNG FLASH THẬT 1 shop theo 3.7: kiểm ở đêm chạy tự động đầu tiên (khung
      21–24h; user canh nhật ký — cách đọc đúng khuôn ghi ở 3.7). Đã có bằng chứng runtime GIÁN TIẾP
      29/08: lượt Dò DOM Flash 10/10 bước ok trên hạ tầng mới + check đơn nối cầu đọc 12 shop bình
      thường — service worker và các flow nạp sạch.

## 5. Rủi ro & lưu ý

- **Rủi ro lớn nhất: vô tình phá lưới AST cũ.** Không di vòng bấm sắp xếp đi đâu, không đổi chữ ký
  `pageBamSapXepDoanhSo`, không thêm lời gọi nó. Mọi thứ mới là ĐỌC.
- **NGHI NGỜ đỏ-có-tên trên trang trắng số** (chưa có số đo thật): trang giữa/cuối đủ ≥2 dòng nhưng MỌI ô
  Doanh số không có chữ số sẽ bị luật 3 của cửa con chấm HỎNG "lệch cột". Sản phẩm doanh số thấp nhất nằm
  đúng các trang sau — chỗ dễ gặp ô trống nhất. **QUYẾT: chấp nhận fail-closed** (đúng triết lý file —
  không nới luật 3, nó đang chặn lớp lỗi lệch cột thật), nhưng lượt chạy thật 3.7 phải để mắt ca này:
  gặp đỏ với `ly` "lệch cột" trên trang ≥2 thì đem dump `giaTri` thô về đối chiếu — nếu ca này CÓ THẬT
  trên Shopee thì dừng theo luật 3 lần và đưa số đo về cho user quyết nới hay giữ.
- **NGHI NGỜ định dạng viết tắt "1,2k"**: phép parse giữ-chữ-số của cửa con biến `"1,2k"` thành `12` —
  trong-trang thì cửa E hiện hành cũng đã dính (không phải lỗi mới), nhưng vế nối tiếp mở rộng bề mặt
  sang biên trang (`"1,1k"`→11 rồi trang sau `999 > 11` ⇒ đỏ). Chưa có bằng chứng Shopee dùng dạng "k" ở
  cột này (số đo lượt 11 là số trơn). Chạy thật gặp đỏ với `ly` chứa số nhỏ bất thường cạnh số lớn ⇒ nghi
  đầu tiên là định dạng, đối chiếu dump `giaTri` thô trước khi kết luận sort hỏng.
- **Bằng chứng cửa trang của LƯỢT XANH không tới được người đọc**: C# (`DocKetQuaKmMotShop`) chỉ đọc
  `daTao/soSanPham/boQua/canhBao/captcha/ngayMaiKhongDo` + `buocHong[0]` — mảng `buoc` (chứa `nhatKyLat`)
  bị vứt, và không có đường ghi file cho `flashKetQua` (chỉ lượt DÒ có `GhiFileDumpFlash`). Các trường
  `doanhSo`/`cua` ghi vào `nhatKyLat` vì thế chỉ phục vụ được lượt ĐỎ (qua `loi`) và debug tương lai.
  **Quyết định để ngỏ cho user:** có muốn mở phạm vi sang C# (thêm đường ghi file JSON kết quả luồng
  thật) để đọc được bằng chứng của lượt xanh không — plan này KHÔNG tự làm.
- `pageDocDoanhSoMoiDongFlash` đọc bảng ĐANG vẽ ⇒ gọi nó SAU `choBangDoiRoiLang` (đã lắng) — đúng vị trí
  3.4.3, đừng dời lên trước.
- Nhãn trang active (`active|current|selected`) là điểm markup mỏng nhất ⇒ luật 4 nhánh của
  `cuaDungTrangDich` đã chọn "không đọc được ⇒ cảnh báo, chỉ đỏ khi đọc được mà LỆCH". Đừng "làm chặt cho
  chắc" thành đỏ cả ca không đọc được.
- Trang cuối ít dòng hơn là hợp lệ — cửa con đã xử; đừng thêm đòi hỏi "đủ 10 dòng".
- **Trình tự với plan refactor km/combo** (plan riêng, đang lập): plan NÀY đi trước — cả hai cùng đụng
  `flow-flashsale.js` (khối import) và `Shopee.Core.Tests`, chạy song song là giẫm chân.
- Quyết định để ngỏ cho người thực thi: tên trường trong `nhatKyLat` (miễn có `dat`/`ly` từng trang);
  vị trí đặt 3 hàm thuần (đề nghị: ngay dưới `dayDoanhSoKhongTangDan`).
- Sổ (`SO-TAY.md`): không có mục MỞ nào liên quan — không gộp gì thêm.

---

## Báo cáo thực thi (chốt 2026-08-29, phiên chính tổng hợp)

**Thực thi 2 lượt bởi `opus-dev`** trên nền mốc `c1b1cf4`; nghiệm thu + phản biện chạy song song giữa
hai lượt. Diff cuối: `flow-flashsale.js` +192/−1 (dòng xoá duy nhất là `ghiChu` cũ của bước F);
`FlashSaleLuongThatTests.cs` chỉ THÊM (584→~900 dòng), 0 test cũ bị sửa.

- **Lượt 1:** 3 hàm thuần + bước E/F đúng §3.1-3.4; 10 test §3.5; thử phá 10/10 ca bảng 3.6 (ca 5 làm đỏ
  5 test — tập cha của dự đoán 3 test). Test: 401 → 411 xanh, 0 warning.
- **Nghiệm thu (độc lập):** 8 ĐẠT / 2 ĐẠT MỘT PHẦN / 2 CHƯA ĐO / 0 KHÔNG ĐẠT; tự chạy 411/411; tái hiện
  ngẫu nhiên ca phá 2 và 7 — khớp chính xác kể cả chi tiết "ca 2 phải đỏ đúng HAI test"; hash khôi phục
  khớp byte-for-byte.
- **Phản biện (độc lập):** 6 phát hiện. Đã SỬA ở lượt 2: (i) câu lỗi bước F chẩn đoán sai hướng cho ca
  đọc-không-được → thêm cờ `viThuTu`, hai giọng lỗi; (ii) lưới cưỡng chế → test 11 (4 assert AST if-return
  / đối số mắt xích / phép gán); (iii) nhãn trang "đọc được mà lạ" bị xử nghiêm hơn nhãn rỗng → chỉ số
  thuần mới kết tội; (iv) JSDoc mắt xích nói đúng "giá trị ĐỌC ĐƯỢC cuối". GIỮ NGUYÊN có chủ đích:
  (v) ca NẶNG-có-điều-kiện "trang ≥2 trắng số ⇒ luật-3 đỏ ⇒ dừng các khung còn lại của shop" — giữ
  fail-closed vì khung k dùng trang k, trang sau chỉ sâu hơn nên thử tiếp cũng không tạo thêm được; câu
  lỗi mới ("KHÔNG kiểm được thứ tự ở trang N") làm ca này tự lộ trên nhật ký, dính thật thì đem dump về
  quyết nới; (vi) mắt xích "giá trị đọc được cuối" hơi nới (fail-open nhẹ) — chấp nhận, đã sửa mô tả.
- **Lượt 2 (delta):** 412/412 xanh, 0 warning; thử phá bổ sung 6/6 đỏ; xác nhận quan trọng: 4 ca phá
  cưỡng chế làm lưới CŨ vẫn xanh — đúng chỗ phản biện chỉ, lưới mới bịt được.

**Biên bản thử phá (16 ca, tất cả đã khôi phục — hash/diff khớp):**
Lượt 1 (bảng 3.6): ca 1→test 1; ca 2→test 2+8; ca 3→test 3+4; ca 4→CHỈ test 4 (test 3 vẫn xanh — chứng
minh test 4 không thừa); ca 5→test 2,3,4,5,6 (tập cha dự đoán); ca 6→test 7; ca 7→test 9; ca 8→test 10
v1; ca 9→test 10 v2; ca 10→test 10 v3. Lượt 2: xoá if-return `cong.dat`→test 11a; xoá if-return
`cuaTrang.dat`→test 11b; đối số 2 = null→test 11c; xoá phép gán mắt xích→test 11d; đảo `viThuTu`→test
5+6 đỏ; nới luật số-thuần→test 9 nhánh 5 đỏ.

**Số đo cây:** cây làm việc ĐANG NHIỄM ~37 file M + nhiều ?? của phiên khác; `Shopee.Core.Tests` chỉ
phụ thuộc `Shopee.Core` + `Shopee.Toolkit` (cả hai build 0/0) nên số 412 ít nhiễu — vẫn khai rõ.

**Còn lại (2 mục CHƯA ĐO, ghi ở §4):** build sln trọn (app đang chạy khoá DLL) và chạy thật 1 shop
(đêm chạy tự động đầu tiên — cách đọc nhật ký đúng khuôn ở 3.7: khung 1 đỏ ⇒ dòng HỎNG, khung ≥2 đỏ ⇒
⚠ cảnh báo "báo TẠO ĐƯỢC nhưng vẫn có bước hỏng").
