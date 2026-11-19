# Plan: App "Shopee Upload": đăng SP từ Hub lên Shopee bằng Đăng Hàng Loạt, bỏ BigSeller

- **Ngày:** 2026-11-17
- **Trạng thái:** chờ duyệt
- **Người lập:** phiên chính · **Người thực thi:** `opus-dev` (từng giai đoạn) → `nghiem-thu` → `phan-bien`

## 1. Bối cảnh & mục tiêu

Dữ liệu SP giờ do app Harvest cào và lưu lên Hub (`harvested_products.raw` = JSON `get_pc`, nối dòng nguồn qua
`harvested_product_sources(account_id, sheet, row_no)`). App Update cũ (`Shopee.Update` +
`Shopee.Module.UpdateProduct`) đi hoàn toàn qua UI BigSeller, nên không dùng được nữa.

**Mục tiêu:** một exe mới. Với mỗi **tài khoản seller Shopee**, app mở Brave có extension, đi **proxy cố định của tài
khoản đó**, vào `https://banhang.shopee.vn/`, rồi đăng SP lấy từ Hub bằng công cụ **Đăng Hàng Loạt** của Shopee.
Các bước:

1. Lấy SP từ Hub theo nguồn đã chọn.
2. Tải file mẫu của chính shop đó.
3. Điền SP vào file mẫu.
4. Tải file lên Shopee.
5. Đọc kết quả, đánh dấu SP nào đã đăng, SP nào lỗi kèm lý do.

Đã chốt với user (2026-11-17):
- **Đăng nhập:** chỉ làm **cách 1**, vào thẳng `banhang.shopee.vn`. Cách subdomain (`subaccount.shopee.com`, như app
  check đơn) để sau. App **không tự đăng nhập**: mở profile lên mà chưa đăng nhập thì dừng tài khoản đó, báo user.
  **Cập nhật 2026-11-18:** hai chế độ, cách chạy chung với app check đơn xem "Chốt thiết kế" mục 8. Cách 1 (link trực
  tiếp) là đường chính; cách subaccount (mượn hồ sơ của app check đơn) làm ở GĐ7.
  **Đổi 2026-11-18 (user yêu cầu): app TỰ ĐĂNG NHẬP Shopee seller** bằng user/pass của shop. Nếu Shopee đòi mã xác
  minh thì app tự đăng nhập Hotmail cho user tự lấy mã. Xem GĐ5b. Thay quyết định "không tự đăng nhập" ở trên.
- **Luồng UI:** menu trái "Tất Cả Sản Phẩm" (`a[href="/portal/product/list/live/all"]`) → di chuột vào nút
  "Công cụ xử lý hàng loạt" (`button.batch-action-btn`) → menu `.eds-dropdown-menu` → mục "Đăng Hàng Loạt"
  (`li.mass-update-item` có text đó).
- **Proxy:** **không bao giờ đi IP máy**. Dùng **1 key KiotProxy gói "Đổi IP VIP - VN" cho cả lượt; sang shop mới thì
  đổi IP mới** (chi tiết ở GĐ4 bước 3). Thiết kế vẫn cho từng tài khoản ghi đè bằng key riêng hoặc proxy tĩnh
  `host:port[:user:pass]`.
- **Tồn kho / cân nặng / kích thước:** JSON không có các trường này. Dùng **giá trị mặc định theo từng tài khoản
  seller**.
- **SP vượt giới hạn của file mẫu:** **cắt bớt cho vừa và ghi log**, SP vẫn được đăng.
- **Nguồn SP:** chọn **TK BigSeller × sheet + dải dòng** (giống Harvest/Scrape). Đăng các SP mà Harvest đã lấy ở các
  dòng đó.
- **Chạy tuần tự:** mỗi lúc một shop, khoảng 16 shop. Không chạy song song.
- **Ảnh phải đổi MD5 trước khi đăng**, như bước "Đồng bộ ảnh (MD5)" của BigSeller (user xác nhận 2026-11-17: BigSeller
  đổi MD5 ảnh xong rồi mới lưu SP). Không dùng thẳng URL ảnh gốc của shop khác. Cách làm ở GĐ1b + GĐ2 bước 5.
- **Video:** file mẫu basic không có cột video, nên bổ sung sau khi đăng (GĐ6).
- **Không cần nhanh, chạy rải vài ngày cũng được.** Ưu tiên an toàn tài khoản hơn tốc độ (xem GĐ5 bước 5, "Nhịp chạy").

## 2. Kết quả khảo sát

### File mẫu `Shopee_mass_upload_<ngày>_basic_template.xlsx` (bản user tải 2026-11-17)

- 7 sheet: `Hướng dẫn`, **`Bản đăng tải`** (sheet cần điền), `Đăng tải bản mẫu` (ví dụ), `Phạm vi ngày chuẩn bị hàng`
  (1.819 ngành hàng kèm khoảng ngày DTS), `Danh sách Bảng quy đổi kích cỡ` (rỗng), và 2 sheet ẩn `HiddenShopBrand`,
  `HiddenTax`.
- **`Bản đăng tải`:**
  - Dòng 1 là mã cột máy đọc (`ps_category|0|0`, …). Dòng 2 là `basic | <hash 32 hex> | 0 | 214522044`. **D2 trông như
    shop_id**, B2 có thể là checksum. Dòng 3 là tên cột, dòng 4 là Bắt buộc/Tùy chọn, dòng 5–6 là ghi chú.
    **Dữ liệu bắt đầu từ dòng 7**; validation áp tới dòng 1007.
  - Sheet có `sheetProtection`.
- **File mẫu gắn với từng shop:** các cột vận chuyển `channel_id.5000 / 5001 / 5004 / 5012 / 50039 / 50052 / 50053`
  là các kênh shop đó đang bật. Shop khác sẽ có bộ cột khác, và dòng 2 có shop_id. ⇒ **Mỗi lượt phải tải file mẫu của
  đúng shop đang đăng, rồi điền vào chính file đó.** Không tự dựng file từ đầu.
- **File do thư viện Go sinh ra, XML không chuẩn** (`activePane="bottom_left"`), nên openpyxl không mở được. ClosedXML
  rất có thể cũng lỗi, hoặc mở được nhưng khi lưu lại làm rơi validation/protection. ⇒ **Ghi bằng cách sửa thẳng
  part XML** `xl/worksheets/sheet2.xml` trong zip: chỉ thay các `<row>` từ dòng 7 trở đi, dùng inline string, các part
  khác giữ nguyên từng byte. Tìm sheet theo tên `Bản đăng tải` qua `workbook.xml` + rels, không cứng `sheet2`.
- Bảng cột (38 cột, A..AL):

  | Cột | Mã | Bắt buộc | Lấy từ |
  |---|---|---|---|
  | A | `ps_category` | Tùy chọn | **Ngành lá = phần tử cuối của `item.categories[].catid`**. Không dùng `item.cat_id`: soi 7 fixture thì đó là ngành **cấp 1** (vd 100637). Mã ngành trong JSON khớp mã ở sheet `Phạm vi ngày…` (vd 100483). |
  | B | `ps_product_name` | Bắt buộc | `item.title`, 10–120 ký tự |
  | C | `ps_product_description` | Bắt buộc | Chữ trong `product_description.paragraph_list` (`type` 0), bỏ ảnh; 100–3000 ký tự |
  | D | `ps_sku_parent_short` | Tùy chọn | **`item_id` gốc**: để tìm lại SP mới trên shop (lọc theo SKU) cho GĐ6 và đối soát |
  | E | `et_title_variation_integration_no` | ĐK (có biến thể) | Mã gom các dòng thành 1 SP; duy nhất trong file. Dùng `item_id` gốc. |
  | F, G | tên nhóm 1 / lựa chọn | ĐK | `tier_variations[0].name / options[i]`; tên nhóm ≤14, lựa chọn ≤20 ký tự |
  | H | `et_title_image_per_variation` | ĐK | `tier_variations[0].images[i]` → URL; chỉ nhóm 1, phải đủ cho mọi lựa chọn hoặc bỏ hết |
  | I, J | tên nhóm 2 / lựa chọn | ĐK | `tier_variations[1]` |
  | K | `ps_price` | Bắt buộc | giá model: `price_before_discount` nếu > 0, không thì `price`; chia 100000; 1.000–120.000.000; **đắt nhất / rẻ nhất ≤ 5** |
  | L | `ps_stock` | ĐK | mặc định của tài khoản seller (0–999.999) |
  | M | `ps_sku_short` | Tùy chọn | trống |
  | N / O | size chart template / ảnh | ĐK | O = URL từ `item.size_chart` nếu khác rỗng |
  | P | GTIN | Tùy chọn | trống |
  | Q | ảnh bìa | Tùy chọn* | `product_images.images[0]` (*cần có trước khi SP được hiển thị) |
  | R..Y | ảnh 1–8 | Tùy chọn | `images[1..8]` (tối đa 9 ảnh tính cả bìa) |
  | Z | `ps_weight` | Bắt buộc | mặc định của tài khoản seller (**đơn vị chưa chắc**, GĐ0 phải xác nhận) |
  | AA–AC | dài / rộng / cao | ĐK | mặc định của tài khoản seller |
  | AD..AJ | `channel_id.*` | ĐK | "Bật"/"Tắt" theo cấu hình kênh của tài khoản seller. Cột có ghi chú "The system automatically sets…" thì để trống. Đọc danh sách kênh từ dòng 1 của file mẫu vừa tải. |
  | AK | DTS đặt trước | Tùy chọn | trống (hàng có sẵn) |
  | AL | `et_title_reason` | (Shopee ghi) | lý do thất bại trong file kết quả |

- **Mỗi dòng là một model.** SP có biến thể trải ra nhiều dòng cùng E. SP không biến thể có dạng
  `tier_variations = [{"name":"","options":[""]}]` + 1 model: ghi 1 dòng, để trống E..J.
- Hướng dẫn trong file ghi giới hạn: **1 nhóm ≤ 20 lựa chọn, 2 nhóm ≤ 50 tổ hợp**. Fixture thật có SP 70 lựa chọn.

### Hiện trạng code

- Hub **chưa có API đọc** SP đã harvest. Chỉ có `POST /api/harvested-products/batch` và
  `GET /api/harvested-products/stats` (`suite/server/Shopee.Hub.Web/Api/ProductApiEndpoints.cs:236,250`;
  `ProductDb.Harvest.cs`).
- `ShopeePdpParser` (`workspace/src/Shopee.Core/Harvest/ShopeePdpParser.cs`) chỉ rút tóm tắt và model. Nó **không**
  đọc tên/lựa chọn nhóm phân loại, ảnh lựa chọn hay mô tả. ⇒ Cần thêm bộ đọc riêng cho bước đăng (đọc từ `raw`),
  **không sửa** parser của Harvest.
- Các phần dùng lại được:
  - Khung mở Brave, CDP, dọn dẹp: `BigSellerBraveRunner`, `BraveArgs`, `BraveJobObject`, `BraveFleet`.
  - Proxy: `KiotProxyClient` / `ProxyPool`.
  - Mẫu cầu nối extension ↔ C# **không dùng CDP** của app check đơn: `suite/orders/.../OrdersBridgeLauncher.cs`,
    `BraveLaunchArgs.cs`, kênh WebSocket, extension `suite/extensions/shopee-orders` (`trustedClick` qua
    `chrome.debugger`).
  - Dò trạng thái đăng nhập: `ShopeeSessionState` / `ShopeeLoginCookies` (`SPC_EC` / `SPC_ST` / `SPC_U`).
- Hiện **không có mô hình "tài khoản seller Shopee"** trong workspace. `BigSellerShop` không có thông tin đăng nhập
  Shopee. ⇒ Phải tạo kho tài khoản seller mới.

## 3. Phạm vi

- **Làm:**
  - kho tài khoản seller (profile Brave, proxy cố định, giá trị mặc định, nguồn SP);
  - API đọc Hub;
  - bộ ánh xạ JSON → dòng file mẫu, kèm luật cắt cho vừa giới hạn;
  - bộ ghi xlsx giữ nguyên file mẫu;
  - extension `shopee-seller-upload`;
  - exe `Shopee.Upload` + UI tối thiểu;
  - sổ đăng (SP nào đã lên shop nào, lỗi gì);
  - test.
- **Không làm:**
  - đăng nhập tự động / cách subdomain;
  - ~~thuộc tính~~: **đã đưa vào phạm vi** sau GĐ0 (bản mẫu nâng cao, xem "Chốt thiết kế sau GĐ0"); thương hiệu
    để trống (Shopee tự điền No brand);
  - video;
  - ảnh trong mô tả;
  - sửa SP đã đăng;
  - xoá hay sửa app Update cũ.

## 4. Các bước thực hiện

### Chốt thiết kế sau GĐ0 (2026-11-17). Chỗ nào mô tả bên dưới khác thì theo mục này

Kết quả GĐ0 xem ở mục "Kết quả GĐ0".

1. **Dùng bản mẫu nâng cao**, không dùng basic.
   - Gom SP theo **ngành cấp 1** (`categories[0].catid`). Mỗi nhóm × mỗi shop tải một bản mẫu nâng cao (tab "Tải về
     Bản Mẫu" → "Bản mẫu nâng cao" → chọn ngành cấp 1 → "Tải về"), rồi điền vào đúng file đó.
   - Writer ghi theo mã cột ở dòng 1, sửa thẳng XML (đã chứng minh Shopee nhận).
   - Writer phải đọc sheet 5 MB bằng cách đọc luồng (stream), không dùng regex trên cả chuỗi.
2. **Cột ghi**:
   - A = ngành lá; B, C; **D = item_id gốc**; E..J phân loại; K giá; L tồn kho **mỗi phân loại**; **O = URL ảnh bảng
     size dùng chung** (đã đổi MD5).
   - Q..Y ảnh (đã đổi MD5); Z cân nặng **gram**; AA..AC cm; kênh vận chuyển để trống (theo cài đặt shop).
   - **AL thương hiệu để trống** (Shopee tự điền No brand).
   - **Cột thuộc tính `ps_product_global_attribute.<id>`**: điền mọi thuộc tính `MANDATORY` của ngành lá (đọc
     `HiddenCatProps`) và các `OPTIONAL` có ánh xạ. Giá trị dịch qua **từ điển (attr_id, tên EN) → tên VN**; kiểu danh
     sách thì kiểm với `HiddenAttr`.
3. **Kết quả đã chứng minh:** SP đăng bằng bản mẫu nâng cao, có thuộc tính bắt buộc, vào Bản nháp với **link
   "Hiển thị" bấm được ngay ở danh sách**. Ví dụ: vòng tay DW SKU `26393229156`; 2 bản Ahellogirl SKU `41355991937`,
   có cả nhãn "Có nguy cơ vi phạm" mà vẫn bấm được. SP đăng bằng basic (không thuộc tính) thì link mờ.
   ⇒ **GĐ3b = ở tab Chưa được đăng → Bản nháp, lọc theo SKU, bấm link "Hiển thị" ở từng dòng.**
   - Link mờ → `needs_fix` + đọc tooltip lý do (vd *"Hãy điền đầy đủ các thuộc tính bắt buộc…"*).
   - Không mở trang sửa, trừ GĐ6 (video).
   - Còn phải khảo sát khi làm GĐ3: tick nhiều dòng có ra nút "Hiển thị" hàng loạt không.
4. **Bảng size** bắt buộc ở một số ngành (áo nữ 100242: *"Không thể để trống Bảng quy đổi kích cỡ"*). Cách sửa tay đã
   chứng minh: chọn "Hình ảnh" rồi tải 1 ảnh lên. App ghi URL ảnh dùng chung vào cột O cho mọi SP.
   - **Chưa chứng minh** cột O làm sáng "Hiển thị" ở ngành bắt buộc bảng size. Kiểm ở lần chạy thật đầu tiên với 1 SP
     ngành áo.
5. **Chống trùng:** tải cùng file 2 lần tạo trùng SP (đã xảy ra). Sổ đăng ghi file trước khi tải; mất kết nối thì đối
   chiếu bảng "Đã lưu" theo tên file.
   - **Shopee không tự chặn SP trùng SKU**: file GĐ2a tạo thêm một bản Ahellogirl thứ 3, cùng SKU `41355991937` với
     các bản thử trước.
   - ⇒ Chống trùng gồm 3 lớp. **Không** tra từng SKU trên giao diện (shop ~5.000 SP thì quá chậm).
     1. **Sổ đăng là nguồn chính.** SP đã `uploading`/`uploaded`/`published` thì không đưa vào file nữa. Trường hợp
        bình thường không cần hỏi Shopee.
     2. **Đối chiếu cả shop mỗi ngày một lần.** Lấy danh sách SKU của shop bằng **một lần tải file** từ "Cập Nhật Hàng
       Loạt" (file xuất SP của shop), hoặc bắt JSON API danh sách SP khi lật trang. Gộp vào sổ những SP sổ chưa biết
       (sổ mất, máy khác, người đăng tay). Cần khảo sát: file "Cập Nhật Hàng Loạt" có cột SKU sản phẩm không, có gồm
       Bản nháp / Đã ẩn không.
     3. **Tra từng SKU chỉ cho ca nghi ngờ:** SP đang `uploading` mà mất kết nối / app tắt. Số lượng nhỏ.
   - **Khảo sát file "Cập Nhật Hàng Loạt" (2026-11-18)**: `mass_update_basic_info_<shop_id>_<giờ>.xlsx`, 1 sheet.
     - Dòng 1 là mã cột: `et_title_product_id | et_title_parent_sku | et_title_product_name |
       et_title_product_description | et_title_reason`. Dòng 2 là `basic_info | <hash> | 0 | <shop_id> |
       {"search_condition":{}}`. Dữ liệu từ dòng 7.
     - **Có Mã SP + SKU sản phẩm** → dùng được cho lớp 2: một file là đủ cả shop.
     - **Chỉ gồm SP đã đăng (Đang hoạt động / Đã ẩn), KHÔNG gồm Bản nháp**: shop thử có 8 bản nháp + 2 SP đã ẩn, file
       chỉ có 2 SP kia.
     - ⇒ Bản nháp do sổ đăng lo (app đăng xong là bấm Hiển thị ngay, nên Bản nháp thường ít). Cần thì quét tab Bản
       nháp bằng lọc SKU.
     - File basic_info **không có cột video**.
   - **File "Cập Nhật Hàng Loạt" loại `media_info`** (`mass_update_media_info_<shop_id>_<giờ>.xlsx`):
     - Cột: Mã SP, SKU, Tên, Ngành, ảnh bìa + ảnh 1–8, Size Chart Template / Image, tên nhóm phân loại 1 + tên phân
       loại (không sửa được), ảnh phân loại, lý do lỗi.
     - **Không có cột video** ⇒ **GĐ6 phải sửa từng SP** trên trang sửa SP (ô "Video sản phẩm"), không làm theo lô
       được.
     - Dùng được làm **đường dự phòng sửa ảnh / bảng size theo lô** cho SP đã đăng: nếu cột O lúc đăng không ăn ở
       ngành bắt buộc bảng size, thì sửa sau qua file này.
     - SP đã đăng có ảnh nằm trên `cf.shopee.vn/file/<id mới>`. Ví dụ: ảnh của Test 11K đổi id từ `vn-11134207-…`
       sang `sg-11134275-…` ⇒ Shopee tự lưu lại ảnh vào kho của họ với id mới.
   - Kết quả GĐ2a (2026-11-18): file do code thật tạo được nhận **2/2**; bản Ahellogirl mới bấm được "Hiển thị".
6. **Kiểm IP hai lớp** (chốt sau nghiệm thu GĐ4, 2026-11-18):
   - **GĐ4**: kiểm phía .NET trước khi mở Brave. IP ra phải ≠ IP máy và ≠ IP shop trước.
   - **GĐ3**: Brave mở `about:blank`. Extension gọi trang trả IP **từ trong trình duyệt**, báo IP về C#. C# so với IP
     .NET vừa kiểm: khớp thì extension mới điều hướng sang `banhang.shopee.vn`; lệch thì đóng Brave, bỏ shop.
   - Region Kiot: mặc định `random` + cảnh báo trong UI để user chọn vùng cố định (chưa biết mã vùng của Kiot).
   - **Yêu cầu cho GĐ3 rút từ phản biện GĐ4** (đo trên Brave 157):
     - Extension tự kiểm WebRTC: tạo `RTCPeerConnection` với một STUN; ra bất kỳ ứng viên ICE nào thì dừng shop.
     - Extension nghiệp vụ **không** xin quyền `proxy` / `privacy`.
     - Proxy tĩnh có mật khẩu: service worker xác thực proxy cần vài giây mới sẵn sàng → bước kiểm IP trong trình
       duyệt phải chờ / thử lại, không fail ngay.
     - Trước khi đóng Brave: đóng bớt về 1 tab. Có từ 2 tab thì Brave không đóng êm, phải kill, có thể mất cookie.
     - Kênh WebSocket localhost: cổng ngẫu nhiên mỗi lượt + token bắt buộc lúc bắt tay (kiểu `#_od_ws=` của app check
       đơn), để JS của trang khó dò ra.
   - **Bài học từ phản biện GĐ5d (2026-11-18):** Brave nhận `--proxy-server` có host không hợp lệ (ký tự vô hình, chữ
     số toàn khổ) thì **lặng lẽ bỏ proxy và đi thẳng bằng IP máy**. Trong khi đó .NET "làm sạch" host qua `IdnHost` nên
     bước kiểm IP phía .NET vẫn báo đạt. Kiểm chuỗi dòng lệnh không bắt được lỗi này. Vì vậy **lớp kiểm IP trong trình
     duyệt (GĐ3) là bắt buộc**, không phải phòng hờ. Trước khi có GĐ3, host / port phải được kiểm chặt ở parser và ở
     `SellerBrowserArgs.Build`.
7. **Còn cần HTML trước GĐ3:**
   - tab "Tải về Bản Mẫu": radio nâng cao, ô tìm ngành, danh sách ngành, nút "Tải về";
   - tab "Đăng lên": input file, bảng "Đã lưu";
   - tab Bản nháp: ô tìm, dòng SP, link "Hiển thị", tooltip.

   GĐ1, GĐ1b, GĐ2 không cần các HTML này.
8. **Chạy chung với app check đơn** (user chốt 2026-11-18). App check đơn (`suite/orders`) đăng nhập qua
   **subaccount** và đi **IP máy** (`BraveLaunchArgs.cs`: "đã bỏ hẳn proxy runtime"). Có hai chế độ đăng SP, chọn theo
   từng tài khoản seller (`Mode`):
   - **`Direct` (đường chính, GĐ4 đã làm):**
     - vào thẳng `banhang.shopee.vn` bằng tài khoản chính, hồ sơ Brave riêng của app đăng SP, **bắt buộc proxy**;
     - không đụng cookie của check đơn (check đơn đi subaccount) ⇒ **check đơn vẫn chạy song song**, không phải dừng;
     - hồ sơ riêng: không nạp extension `shopee-orders`, và đường dẫn hồ sơ không trùng hồ sơ check đơn. Lý do:
       `OrdersBridgeLauncher.Launch` gọi `FreeProfile(alsoMatchBridgeExtension: true)`, giết mọi Brave nạp extension
       đó.
   - **`Subaccount` (làm sau, GĐ7):** mượn hồ sơ Brave của check đơn cho login đó, giống nút "Mở list shop".
     - Không cần lo IP hay cookie: dùng đúng cookie và đúng IP máy như check đơn. Đây là **ngoại lệ có chủ ý** của luật
       "không đi IP máy", chỉ áp cho chế độ này.
     - **Bắt buộc dừng check đơn** của login đó (tức mọi shop dưới subaccount ấy) trong lúc đăng. Lý do: check đơn bắt
       đầu vòng mới sẽ `FreeProfile`, giết Brave đang giữ hồ sơ.
     - Chỉ chạy được trên **đúng máy có hồ sơ** đó.

### GĐ0: Chốt chặn chạy thật trước khi viết app (bắt buộc)

1. Viết script tạm, đặt ngoài repo, nhận đầu vào là file mẫu thật của user + 3 fixture
   (`pdp-khong-bien-the`, `pdp-2tang-8model`, `pdp-1tang-70model-4capdm` sau khi cắt còn 20). Script sửa XML để điền
   các SP vào từ dòng 7, giá trị mặc định đặt tạm.
2. User tải file đó lên Shopee bằng tay, trên đúng shop đã tải mẫu. Cần xác nhận:
   - file được nhận, không bị báo sai mẫu hay checksum dòng 2;
   - ảnh URL `down-vn.img.susercontent.com` được Shopee kéo về;
   - ngành lá đúng;
   - **đơn vị cân nặng**;
   - SP 2 nhóm phân loại gom đúng;
   - tên lựa chọn bị cắt không gây lỗi.
3. User gửi HTML (như đã gửi menu) của:
   - trang Đăng Hàng Loạt: nút tải mẫu basic, ô chọn file, nút tải lên;
   - danh sách bản ghi tải lên và trạng thái;
   - cách tải file kết quả lỗi.

   Thiếu bước này thì GĐ3 không làm được.

Nếu Shopee từ chối file sửa XML thì DỪNG, báo lại để chọn cách khác (ví dụ điền qua Excel COM).

4. **GĐ0b (sau GĐ1b):** tạo thêm 1 file thử, ảnh đã đổi MD5 và lấy URL từ Hub. Mục đích: xác nhận Shopee nhận ảnh từ
   **domain ngoài Shopee** (`api.schedra.net`). Nếu Shopee chỉ nhận ảnh từ domain của Shopee thì DỪNG, chuyển sang
   cách dự phòng: extension tải ảnh đã đổi MD5 lên kho media của chính shop trong Seller Centre qua UI, lấy URL
   `susercontent` mới. Cách này phải khảo sát UI trước.
5. Tải luôn file mẫu "Cập Nhật Hàng Loạt" để xem có cột video không (quyết định cách làm GĐ6).

### GĐ1: Hub: API đọc SP đã harvest (`suite/server/Shopee.Hub.Web`)

1. `GET /api/harvested-products/rows?account=&sheet=&from=&to=` (auth Client) trả về mỗi dòng nguồn:
   - `{rowNo, url (link của dòng nguồn), itemId, shopId, harvestedAt, raw}`;
   - phân trang, tối đa 50 dòng/lần (raw ~60–80 KB/SP);
   - dòng chưa có dữ liệu thì không trả.
2. Route và DTO thêm vào cả `suite/Shopee.Core/Coordination/HubRoutes.cs` lẫn bản chép ở
   `workspace/src/Shopee.Core/Coordination/` (sửa thì sửa cả 2, như GĐ4 Harvest).
3. Test chạy Postgres thật: phân trang, lọc dải dòng, account/sheet không phân biệt hoa thường (nếu ledger đang như
   vậy), không lộ dữ liệu account khác.

**Sửa sau phản biện GĐ1 (2026-11-17):** `row_no` trên Hub là chỉ số dòng nén và bị cũ sau khi chèn/xoá dòng
(sổ `P-20261117-15`). ⇒ **Bước đăng không tin `row_no`.**
- Đọc nguồn hiện tại (workbook hoặc Hub `product_rows`, giống Harvest) cho dải dòng.
- Tách item_id từ link bằng `HarvestItemKey`.
- Tra Hub theo **danh sách item_id**: route mới `POST /api/harvested-products/by-items`, ≤ 50 id/lần, trả
  `{itemId, shopId, harvestedAt, raw}`. Item chưa có dữ liệu thì Hub không trả, client ghi "chưa harvest".
- Route rows giữ lại, ghi rõ đơn vị là "dòng nén", và **không** dùng cho bước đăng.

### GĐ1b: Hub: kho ảnh công khai có hạn

Shopee tải ảnh từ URL **không kèm token**, nên route `/files` hiện có (cần auth) không dùng được.

1. `PUT /api/upload-media` (auth Client): nhận 1 ảnh JPG/PNG ≤ 2 MB, trả URL công khai
   `https://api.schedra.net/m/<token ngẫu nhiên 128 bit>.jpg`.
2. `GET /m/{token}.jpg`: **không auth**, trả đúng `Content-Type`, không liệt kê được thư mục. Token không đoán được.
3. Tự xoá sau N ngày (mặc định 7, đủ dài cho bước đệm MD5 chạy trước nhiều ngày) qua job dọn, giống cơ chế xoá
   video 30 ngày đã có. Response upload trả thêm thời điểm hết hạn để client biết.
4. Có giới hạn dung lượng tổng; vượt thì Hub từ chối upload (507), không xoá bừa. Phía client (GĐ2b) coi 507 là
   **lỗi hệ thống**: cầu dao lùi cả worker, ảnh không bao giờ thành `failed` vì 507.
5. Test: ảnh hết hạn trả 404; token sai trả 404; ảnh quá cỡ hoặc sai định dạng bị từ chối.

**Hợp đồng GĐ1b đã làm (2026-11-17, sau 2 vòng phản biện).** Đường dẫn khác bản nháp ở trên:
- `PUT /api/media` (auth Client) → `{path, url, expiresAt}`.
  - 413 / 415: quá cỡ / sai định dạng.
  - 507 `media-quota-exceeded` / `disk-low`: vượt trần / đĩa thấp.
  - 503 `public-base-url-missing`: Hub chưa cấu hình base URL, hoặc cấu hình sai dạng.
- `GET|HEAD /api/media/{hạn}-{32hex}.jpg|png`: công khai. Hết hạn / sai tên → 404.
- `DELETE /api/media/{name}` (auth Client).
- Cấu hình:
  - `Hub:Media:RetentionDays` (7);
  - `Hub:Media:MaxTotalMb` (mặc định 20480, **lúc deploy đặt ~5000** vì VPS chỉ còn 19 GB trống, đĩa dùng chung với dự án khác);
  - `Hub:Media:MinFreeMb` (5120);
  - **`HUB_PUBLIC_BASE_URL=https://api.schedra.net` bắt buộc** lúc deploy.
- Client `UploadPublicMediaAsync` tự gán **502** khi response hỏng / thiếu URL / URL không công khai.
  - Phân biệt với 502 thật của Cloudflare bằng **`ErrorCode != null`**.
  - 502 có mã = cấu hình hỏng: dừng lượt, không thử lại.
  - 502 không mã = lỗi tạm: lùi rồi thử lại.

### GĐ2: Core: ánh xạ + ghi file mẫu (`workspace/src/Shopee.Core/MassUpload/`)

1. `ShopeeListingReader`: từ `raw` rút đủ cho bước đăng:
   - tên, mô tả chữ, ngành lá;
   - nhóm phân loại (tên, lựa chọn, ảnh), model (`tier_index`, giá);
   - ảnh, size chart.
2. `MassUploadMapper` (hàm thuần) nhận `(listing, SellerDefaults)` và trả `MassUploadProduct { rows[], warnings[] }`
   hoặc `Skip(reason)`. Luật "cắt cho vừa, ghi log" (mỗi lần cắt đều sinh 1 warning):
   - **Tên** > 120 ký tự: cắt ở ranh giới từ. Tên < 10 ký tự: Skip.
   - **Mô tả**:
     - ghép đoạn chữ, giữ xuống dòng;
     - nếu < 100 ký tự: thêm tên SP + các dòng "Thuộc tính: giá trị" từ `item.attributes` cho tới khi đủ;
     - vẫn thiếu thì Skip;
     - nếu > 3000 ký tự: cắt ở ranh giới đoạn.
   - **Nhóm phân loại**:
     - Tên nhóm cắt còn ≤ 14 ký tự, lựa chọn cắt còn ≤ 20 ký tự. Sau khi cắt mà trùng tên thì thêm hậu tố `" 2"`, `" 3"`
       (vẫn giữ ≤ 20).
     - 1 nhóm: giữ 20 lựa chọn đầu.
     - 2 nhóm: bớt lựa chọn ở cuối nhóm 2, rồi đến nhóm 1, cho tới khi tích ≤ 50.
     - Model trỏ tới lựa chọn đã bỏ thì bỏ theo. Tổ hợp thiếu model thì không ghi dòng.
   - **Giá**:
     - bỏ model giá 0 hoặc < 1.000;
     - nếu đắt nhất / rẻ nhất > 5: giữ cụm model quanh giá trung vị sao cho tỉ lệ ≤ 5, bỏ phần còn lại;
     - sau khi bỏ mà một lựa chọn không còn model nào thì bỏ lựa chọn đó;
     - giá làm tròn tới đồng.
   - **Ảnh**: bìa + tối đa 8 ảnh. Ảnh lựa chọn chỉ ghi khi mọi lựa chọn còn lại đều có ảnh.
   - Giới hạn số (20 / 50 / 5 / 14 / 20 / 120 / 3000 / 100) để trong một lớp `MassUploadLimits`, dễ sửa khi Shopee đổi.
3. `MassUploadTemplateWriter`:
   - mở zip file mẫu, tìm sheet theo tên;
   - đọc dòng 1 để biết mã từng cột: **ghi theo mã cột, không theo chữ cái**, vì shop khác có cột kênh khác;
   - xoá các `<row>` ≥ 7, ghi dòng mới bằng inline string;
   - giữ nguyên dòng 1–6, protection, validation, các sheet và part khác;
   - cập nhật `<dimension>`;
   - cột bắt buộc theo dòng 1 mà mapper không biết thì báo lỗi rõ, không ghi bừa.
   - Mỗi file tối đa `N` SP (mặc định 100) và ≤ 1000 dòng.
4. **Bước đệm MD5 ảnh** (user yêu cầu 2026-11-17). Đây là công đoạn riêng, chạy **trước và độc lập** với lượt đăng,
   thay bước MD5 của BigSeller.
   - **Hàng đợi `image_prep`** trong `upload.db`, khoá theo (shop, id ảnh gốc). Trạng thái:
     `pending → hosted → verified`, hoặc `failed` kèm lý do. Có thêm trạng thái nội bộ `deleting`.
     - Bỏ trạng thái `processed`, vì MD5 tính lại được, cùng (shop, ảnh) luôn ra cùng kết quả.
     - `hosted` = đã up lên Hub, đã ghi `hub_path`, nhưng chưa kiểm. App tắt giữa chừng thì lần sau kiểm tiếp ảnh đó,
       hoặc xoá nó trước khi up lại, nên không để lại ảnh mồ côi.
     - Đẩy vào hàng: khi chọn nguồn (TK BigSeller × sheet × dải dòng) cho một shop, mọi ảnh của các SP đó (ảnh SP,
       ảnh phân loại, ảnh bảng size) được đưa vào hàng chờ.
     - Ảnh dùng lại trong cùng SP (ảnh bìa trùng ảnh phân loại) chỉ xử lý một lần.
   - **Worker `ImagePrepWorker`** chạy nền, có thể chạy trước cả ngày (vd ban đêm, ngoài khung giờ đăng). Với mỗi ảnh:
     1. Tải ảnh gốc từ CDN Shopee **bằng IP máy** (user chốt 2026-11-17). Bước này không đăng nhập tài khoản seller nào,
        nên không thuộc luật "tài khoản seller không đi IP máy". Áp luật đó cho mọi request tới `banhang.shopee.vn`.
     2. **Đổi MD5 ở mức byte, không mã hoá lại ảnh** (chốt khi giao GĐ2b, 2026-11-17).
        - JPEG: chèn một đoạn COM (`FF FE`) **sau dãy segment APPn đầu file** (JFIF / Exif; không có APPn thì ngay
          sau SOI), chứa chuỗi ngẫu nhiên sinh theo (shop, ảnh).
        - PNG: chèn chunk `tEXt` (có CRC) ngay sau IHDR.
        - Định dạng khác, hoặc ảnh gốc > 2 MB → `failed` kèm lý do.
        - Ảnh giữ nguyên chất lượng, không cần thư viện ảnh. Cùng một ảnh ở 2 shop vẫn ra 2 MD5 khác nhau.
        - Đây đúng là việc "đổi MD5" như bước của BigSeller. Đổi điểm ảnh (nhiễu) cũng không qua mặt được so khớp kiểu
          perceptual hash, nên không làm.
     3. Ghi MD5 mới vào sổ.
     4. Đẩy lên Hub (GĐ1b), lấy URL công khai.
     5. **Kiểm**: `GET` URL công khai **không kèm token**, phải ra 200, đúng `Content-Type`, và MD5 tải về khớp MD5 đã
        ghi. Đạt thì `verified`.
   - Ảnh gốc hỏng hoặc 404 → `failed`. Khi map, ảnh `failed` bị bỏ và ghi warning; SP không còn ảnh nào thì Skip.
   - **Lượt đăng chỉ lấy những SP mà mọi ảnh đã `verified`.** SP chưa sẵn sàng thì để lượt sau, không chờ.
   - **Hạn của ảnh**: TTL trên Hub (GĐ1b, mặc định 7 ngày) phải dài hơn khoảng thời gian từ lúc chuẩn bị tới lúc Shopee
     tải. Lúc dựng file, ảnh nào còn dưới 1 ngày là hết hạn thì đưa về `pending` để làm lại. Lúc đó SP đó lùi sang lượt
     sau.
   - UI có số liệu: ảnh chờ / đã sẵn sàng / lỗi theo từng shop.
   - **Xoá ảnh trên Hub** (sau phản biện GĐ1 vòng 2): một URL có thể được nhiều SP của shop dùng chung (ảnh bảng size
     dùng chung, ảnh trùng giữa các SP). ⇒ **Đếm tham chiếu.**
     - Chỉ gọi `DELETE` khi mọi SP dùng URL đó đã ở trạng thái Shopee đã kéo ảnh (file "Thành công" + SP có trong Bản
       nháp).
     - **Ảnh bảng size dùng chung không bao giờ xoá**, để TTL tự dọn.
     - Khi xoá thì đưa dòng `image_prep` về `pending` cùng lúc.
   - Ảnh giữ kích cỡ gốc từ CDN (thường 100–300 KB), nên vừa ngân sách đĩa VPS.
   - **Hợp đồng GĐ5 với bước đệm** (sau phản biện GĐ2b):
     - **Chuẩn bị vừa đủ, không ôm trước:** mỗi shop chỉ đưa vào hàng các SP sẽ đăng trong khoảng 1 ngày tới (theo
       trần SP/ngày). Lý do: trần kho ảnh Hub khoảng 5000 MB, mà chuẩn bị 16 shop trong 1 ngày đã ~3 GB.
     - Thứ tự gọi: Enqueue → resolve (đưa danh sách ảnh cần) → map → ghi file → tải lên.
     - Enqueue cho mỗi SP: ảnh lựa chọn nhóm 1 + **tối đa 12 ảnh SP đầu** (9 ảnh dùng + 3 ảnh dự phòng thay ảnh lỗi),
       không đưa hết, để đỡ tốn kho Hub.
     - SP bị Skip/bỏ → `ForgetItemAsync`. SP đã vào Bản nháp → `ReleaseItemAsync`.
     - Worker bị dừng vì cấu hình hỏng → phiên phải gọi `Start()` lại sau khi sửa.
     - Ảnh `failed` → UI có nút "thử lại ảnh lỗi" (`RetryFailedAsync`).

   Mapper nhận một hàm `resolveImageUrl(shop, idẢnhGốc)` (đọc từ `image_prep`) thay vì tự dựng URL, để đổi nguồn ảnh
   (Hub hay kho media Shopee) không phải sửa mapper.
5. Test:
   - mapper chạy trên 7 fixture PDP, mỗi luật cắt có ca riêng;
   - writer chạy trên **file mẫu thật** (chép vào `tests/.../Fixtures/mass-upload/`), sau đó mở lại để kiểm từng ô và
     kiểm các part khác giữ nguyên (so hash);
   - một ca file mẫu có bộ cột kênh khác.

### GĐ3: Extension `workspace/extensions/shopee-seller-upload`

Khung lấy theo extension `shopee-orders`: MV3, `chrome.debugger` để bấm/hover bằng input thật, kênh WebSocket về C#,
**không dùng CDP từ C#**.

1. `checkLogin`:
   - mở `https://banhang.shopee.vn/`;
   - chưa đăng nhập (URL `/account/signin`, form đăng nhập, hoặc thiếu cookie `SPC_EC`/`SPC_ST`) → trả `needLogin`,
     không làm gì thêm;
   - gặp captcha/verify → trả `verify`, giữ cửa sổ chờ user.
2. `openMassUpload`: bấm "Tất Cả Sản Phẩm" ở menu trái → hover `button.batch-action-btn` → bấm "Đăng Hàng Loạt".
   Mỗi bước chờ phần tử có timeout và trả lỗi có tên bước. Selector theo text + class đã gửi; HTML GĐ0 bổ sung phần sau.
3. `downloadTemplate`: bấm tải mẫu **basic**, bắt file qua `chrome.downloads`, trả đường dẫn file cho C#.
4. `uploadFile(path)`:
   - gán file vào input bằng `chrome.debugger` `DOM.setFileInputFiles`;
   - bấm tải lên;
   - chờ Shopee xử lý xong (theo bản ghi mới nhất trong danh sách);
   - trả `{total, success, failed}` + tải file kết quả lỗi (nếu có) → đường dẫn.
5. Probe phiên bản (`hasUploadStep`) để C# nhận ra Service Worker cũ.
6. Test Node cho các hàm thuần (dò login, parse trạng thái).

### GĐ4: Tài khoản seller + mở Brave với proxy cố định

1. `SellerAccountStore`: lưu JSON ở thư mục dữ liệu app, ghi nguyên tử. Mỗi tài khoản có:
   - `Id`, `Label`, thư mục profile Brave riêng;
   - `Proxy`: `{Kind: Kiot|Static, KiotKey, Region (cố định 1 tỉnh, không random), Static "host:port[:user:pass]",
     Type http|socks5}`;
   - `Defaults`: tồn kho, cân nặng, dài/rộng/cao, cấu hình kênh vận chuyển (Bật/Tắt theo mã kênh), số SP mỗi file;
   - `Source`: TK BigSeller, sheet, từ dòng, đến dòng.
   - (thêm ở GĐ5) `Mode`: `Direct` (mặc định) | `Subaccount`, kèm login subaccount khi là `Subaccount`. Xem "Chốt
     thiết kế" mục 8. GĐ4 chỉ làm `Direct`.
2. Mở Brave:
   - **bắt buộc có proxy**. Không lấy được proxy, proxy sống không qua, hoặc không xác minh được IP ra thì **dừng tài
     khoản đó, không mở Brave**. Không có nhánh rơi về IP máy;
   - `--proxy-server` + extension trả lời xác thực proxy (`webRequestAuthProvider`) cho proxy tĩnh có user/pass;
   - chặn rò IP qua WebRTC: cờ `--webrtc-ip-handling-policy=disable_non_proxied_udp` + ghi `webrtc.ip_handling_policy`
     vào Preferences. **Không dùng `--force-webrtc-ip-handling-policy`**: cờ này không tồn tại, đo trên Brave 157 vẫn lộ
     IP thật;
   - trước khi mở Shopee, extension gọi một trang trả IP để xác minh IP ra là IP proxy (≠ IP máy), lệch thì dừng.
3. Kiot, gói "Đổi IP VIP - VN" (user chốt 2026-11-17). Đặc điểm gói: IP không hết hạn, địa chỉ proxy cố định, đổi IP
   không giới hạn nhưng mỗi IP phải dùng tối thiểu 5 phút, IP riêng.
   - **Một key Kiot dùng chung cho cả lượt, sang shop mới thì đổi IP mới.** Cấu hình proxy chung cho cả app; từng tài
     khoản có thể ghi đè bằng key riêng hoặc proxy tĩnh.
   - Đổi IP **trước khi mở Brave của mỗi shop**:
     1. Gọi `/new` (vùng VN cố định).
     2. Nếu chưa qua mốc `nextRequestAt` (chưa đủ 5 phút) thì chờ tới mốc rồi mới gọi, có hiện đếm ngược.
     3. Xác minh IP ra **khác IP của shop trước** và **khác IP máy**.
   - Ghi IP đã dùng vào log và vào sổ của shop (để tra khi shop bị đòi xác minh).
   - Không bao giờ đổi IP giữa lúc đang làm một shop. IP không hết hạn nên không cần xử lý ca hết hạn giữa chừng. Proxy
     chết giữa chừng thì dừng shop đó, không đổi IP để làm tiếp.
   - `/new` lỗi, hoặc xác minh IP không qua: thử lại có giới hạn, rồi bỏ qua shop đó (báo lỗi) và sang shop kế. Không
     bao giờ chạy bằng IP máy.
   - Vẫn đóng Brave giữa 2 shop (profile khác nhau), nên dù địa chỉ proxy cố định thì mỗi shop vẫn mở Brave mới.
4. **Chạy lần lượt, mỗi lúc một shop** (user chốt 2026-11-17, khoảng 16 shop). Không có làn song song, nên không cần
   luật chia chỗ proxy giữa các làn. Shop này xong (hoặc dừng vì lỗi) thì đóng Brave rồi mới sang shop kế.
   Khi lưu cấu hình, chỉ cảnh báo nếu 2 tài khoản nhập trùng một key/proxy.

### GĐ5: Exe `workspace/src/Shopee.Upload` + luồng chạy + sổ đăng

1. Project mới theo khuôn `Shopee.Harvest` (AssemblyName `ShopeeUpload`, icon mới cùng phong cách, copy extension vào
   output, thêm vào `.sln`, chặn chạy 2 bản cùng máy).
2. Luồng mỗi tài khoản seller:
   1. kiểm proxy;
   2. mở Brave;
   3. `checkLogin` (chưa đăng nhập thì dừng tài khoản, báo "Chưa đăng nhập Shopee");
   4. đọc SP từ Hub theo `Source`, bỏ SP đã có trong sổ là "đã đăng" ở shop này;
   5. map;
   6. `openMassUpload` → `downloadTemplate`;
   7. ghi file (lưu bản đã điền vào `…\upload\<tk>\<ngày giờ>.xlsx` để tra lại);
   8. `uploadFile`;
   9. đọc kết quả, cập nhật sổ;
   10. sang file tiếp theo.

   Thêm trường `Mode` vào `SellerAccount` (đọc file cũ thiếu trường thì coi là `Direct`). GĐ5 chỉ chạy `Direct`.
   Tài khoản `Subaccount` thì báo "chưa hỗ trợ, chờ GĐ7" rồi bỏ qua. Luồng chạy tách bước "mở trình duyệt" và
   "điều hướng tới Đăng Hàng Loạt" thành 2 chỗ cắm theo `Mode`, để GĐ7 thêm vào mà không sửa luồng chính.
3. **Sổ đăng** (SQLite `upload.db`): `(seller_account, source_item_id, row_no, status: pending|uploaded|failed|skipped,
   reason, warnings, file, uploaded_at)`.
   - Tải lên thành công ⇒ `uploaded`. Dòng lỗi trong file kết quả ⇒ `failed` + lý do.
     - Đọc lý do theo **mã cột `et_title_reason`**: ở bản mẫu nâng cao là cột BQ, còn AL là `ps_brand`.
     - Khớp dòng theo **cột D `ps_sku_parent_short`** (= `item_id` gốc). Không khớp theo E, vì SP không biến thể để
       trống E.
   - SP `uploaded` không đăng lại. SP `failed` có nút "Đăng lại mục lỗi".
5. **Nhịp chạy** (user chốt 2026-11-17: không cần nhanh). Tất cả đều cấu hình được; số trong ngoặc là mặc định:
   - **SP mỗi file**: nhỏ (30). Mỗi file tải lên xong thì nghỉ ngẫu nhiên (3–8 phút) mới tải file kế.
   - **Trần SP mỗi shop mỗi ngày** (100). Shop chạm trần thì sang shop kế; SP còn lại để ngày sau làm tiếp từ sổ đăng.
   - **Nghỉ giữa 2 shop** ngẫu nhiên (10–20 phút), luôn ≥ 5 phút để đủ điều kiện đổi IP của gói Kiot.
   - **Khung giờ chạy** (8h–22h). Ngoài khung thì tạm dừng, vào khung thì chạy tiếp.
   - Shopee báo **đạt giới hạn số SP của shop** → dừng shop đó, ghi trạng thái, không thử lại trong ngày.
   - Thao tác trong trang có độ trễ giống người (chờ có biên độ ngẫu nhiên giữa các lần bấm).
   - Chạy bền nhiều ngày: tắt app hoặc máy giữa chừng thì lần sau đọc sổ đăng để làm tiếp. Không đăng lại SP đã
     `uploaded`. File đã tải lên mà chưa đọc được kết quả thì đọc lại kết quả từ danh sách bản ghi của Shopee trước khi
     đăng tiếp.
6. UI tối thiểu:
   - danh sách tài khoản seller: thêm/sửa proxy, giá trị mặc định, nguồn;
   - Chạy/Dừng;
   - từng tài khoản hiện IP proxy, trạng thái đăng nhập, đã đăng / lỗi / bỏ qua / cảnh báo cắt;
   - xem log cảnh báo cắt theo SP.

### GĐ5a: Exe `ShopeeUpload` bản đầu: cấu hình proxy + tài khoản, mở Brave thử (user yêu cầu 2026-11-18)

Mục đích: có app chạy được ngay để user nhập proxy, thêm tài khoản, bấm mở Brave qua proxy, rồi tự đăng nhập Shopee
trong profile đó. Chưa đăng SP, vì GĐ3 (extension) chưa có.

1. **Project** `workspace/src/Shopee.Upload/Shopee.Upload.csproj`:
   - WinExe, WPF, `net8.0-windows`, AssemblyName `ShopeeUpload`;
   - **chỉ tham chiếu `Shopee.Core`**, không tham chiếu và không sửa `Shopee.Workspace`, vì phiên Harvest đang sửa
     project đó;
   - **không thêm vào `ShopeeWorkspace.sln`** ở bước này (sẽ thêm lúc commit);
   - chặn chạy 2 bản cùng máy (Mutex có tên);
   - dùng `app.manifest` chung như Harvest.
2. **Icon mới** `workspace/assets/upload-icon.{png,ico}`:
   - thêm `RenderUploadIcon` vào `workspace/assets/IconGenerator.cs`, cùng phong cách 3 icon có sẵn;
   - phải khác hẳn `update-icon` (app Update cũ) và `harvest-icon`. Gợi ý: nền cam Shopee, hình hộp/túi kèm mũi tên
     đi lên;
   - chạy generator bằng một project tạm trong scratchpad, không thêm project vào repo. Đặt icon làm `ApplicationIcon`
     và icon cửa sổ.
3. **Màn hình chính:**
   - **Proxy chung**:
     - key Kiot (ô mật khẩu, chỉ hiện 4 ký tự cuối);
     - vùng (`random | bac | trung | nam`, mặc định `random`, kèm cảnh báo "nên chọn vùng cố định");
     - kiểu http/socks5.
   - **Danh sách tài khoản seller**:
     - Thêm / Sửa / Xoá / Bật-Tắt; nhãn; Id tự sinh hợp lệ theo `SellerAccountStore.IsValidId`; thư mục profile
       (hiện đường dẫn thật qua `ResolveProfileDir`);
     - proxy: "Dùng proxy chung" / "Key Kiot riêng" (kèm vùng, kiểu, ô "giữ IP") / "Proxy tĩnh" (`host:port[:user:pass]`,
       kiểu);
     - giá trị mặc định (tồn kho, cân nặng, kích thước, SP mỗi file);
     - nguồn (TK BigSeller, sheet, từ dòng, đến dòng);
     - trần SP mỗi ngày.
   - **Nhịp chạy** (`SellerPacing`): các ô số.
   - Lưu bằng `SellerAccountStore.Save`. Hiện lỗi và cảnh báo của `Validate` ngay trên màn, có lỗi thì không lưu.
4. **Nút "Mở Brave qua proxy"** cho tài khoản đang chọn:
   - chạy `SellerRunGate.OpenAsync` với launcher thật (`SellerBrowserLauncher` + `BraveSellerHost`), không nạp
     extension nghiệp vụ;
   - log hiện từng bước: lấy proxy, đếm ngược chờ Kiot, IP máy, IP ra, mở Brave. Lỗi thì hiện lý do;
   - mở được thì hiện IP ra, bật nút **"Đóng Brave"** (`SellerBrowserSession.CloseAsync`);
   - Brave mở `about:blank`. User tự gõ `browserleaks.com/webrtc` để kiểm, rồi tự vào `banhang.shopee.vn` đăng nhập.
     App không điều hướng Shopee ở GĐ5a;
   - chỉ một Brave mỗi lúc. Đang mở thì khoá nút mở của tài khoản khác. Tắt app thì đóng Brave;
   - `SellerRunGate` giữ suốt phiên app, để luật "IP khác mọi IP đã dùng" có tác dụng giữa các lần bấm.
5. **Bí mật:**
   - không ghi key Kiot hay mật khẩu proxy ra log hay thanh trạng thái, chỉ 4 ký tự cuối;
   - JSON vẫn lưu key như Store đang làm. Ghi rõ trong báo cáo là lưu thường, không mã hoá.
6. **Test:** logic chuyển đổi form ↔ `SellerAccount` / `SellerUploadSettings` đặt trong lớp thường, không phụ thuộc
   WPF. Test ở project mới `workspace/tests/Shopee.Upload.Tests`. Không test bằng Brave thật.

Tiêu chí: build xanh; test mới + `Shopee.Core.Tests` xanh; mở exe hiện đúng icon; lưu rồi mở lại app thấy đủ dữ liệu;
thiếu key hoặc proxy sai thì nút mở Brave báo lỗi, không mở Brave.

### GĐ5b: Tự đăng nhập Shopee seller + mở Hotmail khi bị đòi mã (user yêu cầu 2026-11-18)

User chốt:
- dùng CDP + Playwright **chỉ trong lúc đăng nhập**, như app check đơn;
- mật khẩu lưu **chữ thường** trong `seller-accounts.json`, như key Kiot;
- gặp captcha thì **chờ user làm tay**.

Làm tương tự `SubaccountLoginFlow` của app check đơn: app không đọc mail, user tự lấy mã và tự nhập, app chỉ theo dõi
xem đăng nhập được chưa.

1. **Dữ liệu tài khoản** (`SellerAccount`, chỉ thêm trường):
   - `ShopeeLogin`, `ShopeePassword`, `VerifyEmail`, `VerifyEmailPassword`.
   - `Validate`: có mật khẩu mà thiếu user thì lỗi; có email mà thiếu mật khẩu email thì lỗi (và ngược lại). Mật khẩu
     Shopee dài quá 16 ký tự thì cảnh báo (ô mật khẩu của form có `maxlength=16`).
   - Form: user và email là TextBox; hai mật khẩu là PasswordBox, chỉ hiện 4 ký tự cuối.
   - `Secrets()` và `Redact` che cả hai mật khẩu (và user/email nếu đã có trong danh sách che).
2. **Mở Brave chế độ đăng nhập.** `SellerRunGate.OpenAsync` thêm tuỳ chọn `Login`:
   - Giữ nguyên mọi luật GĐ4: proxy bắt buộc, kiểm IP, WebRTC, DoH, policy, kiểm dòng lệnh sau khi mở.
   - Thêm `--remote-debugging-port=0`, chỉ nghe loopback. Đọc cổng từ `DevToolsActivePort` trong profile.
   - `CheckMain` chỉ chấp nhận cờ CDP khi ở chế độ `Login`. Chế độ thường vẫn cấm CDP như cũ.
   - Nối Playwright bằng `ConnectOverCDPAsync`, không dùng `--enable-automation`, không tiêm script vá fingerprint.
     Cách làm theo `suite/orders/XuLyDonShopee.Core/Services/LoginBrowserBootstrap.cs`.
3. **Luồng đăng nhập** (lớp mới trong `Shopee.Core/MassUpload/Seller/Login/`):
   - Không tham chiếu project `suite`. Phần cần thì **chép** từ app check đơn, ghi rõ nguồn trong chú thích:
     `LoginHumanInput` (gõ và bấm kiểu người), `MicrosoftMailLogin` + `MsLoginSelectors` (đăng nhập Hotmail), các
     hàm dò trang trong `LoginPageProbe`.
   1. Vào `https://banhang.shopee.vn/`. Phân loại trang: **đã đăng nhập** / **form đăng nhập** / **trang xác minh** /
      **captcha** / không rõ.
   2. Đã đăng nhập thì xong ngay.
   3. Form đăng nhập:
      - thiếu user/pass thì dừng, báo "chưa nhập tài khoản Shopee";
      - gõ kiểu người vào `input[name=loginKey]`, rồi `input[name=password]`;
      - bấm nút "Log In" / "Đăng nhập" (nút trong `form`, chờ hết `disabled`).
   4. Sau khi bấm, theo dõi:
      - **Lỗi đăng nhập**: `div[role=alert]`, ví dụ "Your account and/or password is incorrect, please try again".
        **Dừng, không thử lại** (tránh khoá tài khoản). Báo đúng câu Shopee, câu này không chứa mật khẩu.
      - **Trang xác minh / đòi mã**: có email thì mở **tab mới**, đăng nhập Hotmail (chỉ đăng nhập, không đọc mail,
        không bấm gì trong mail), rồi đưa tab Shopee lên trước. Báo "Đang chờ mã — đã mở Hotmail". Không có email
        thì báo "Đang chờ mã — tự lấy mã".
      - **Captcha**: báo "Có captcha — làm tay trong cửa sổ Brave".
      - Hai ca trên theo dõi **tối đa 15 phút**, tới khi trang chuyển sang "đã đăng nhập". Quá hạn thì báo hết giờ, để
        nguyên cửa sổ.
   5. **Nhận biết đã đăng nhập:**
      - Host `banhang.shopee.vn`, đường dẫn không phải trang đăng nhập / xác minh / captcha, và không còn ô
        `input[name=loginKey]`.
      - Đủ điều kiện liên tục vài giây mới tính, để tránh nhầm lúc trang đang chuyển.
      - Mọi nhánh không nhận ra trang thì log `title` + `url`, không log nội dung form.
   6. Xong (thành công hay dừng): **ngắt Playwright, không tắt Brave**. Cửa sổ để nguyên cho user xem.
4. **UI GĐ5a:**
   - Ô cấu hình: thêm 4 ô tài khoản Shopee / email xác minh.
   - Nút mới **"Mở + đăng nhập Shopee"** cạnh nút "Mở Brave qua proxy". Mở Brave chế độ `Login` rồi chạy luồng đăng nhập.
   - Trạng thái hiện từng bước. Có nút Huỷ.
   - Vẫn chỉ một Brave mỗi lúc. "Đóng Brave" đóng được cả Brave chế độ `Login`.
5. **Không làm ở GĐ5b:**
   - Đăng nhập kiểu subaccount (GĐ7).
   - Tự đọc mã từ mail.
   - Luồng chạy GĐ5. Khi đó: đăng nhập xong thì đóng Brave và mở lại sạch, không CDP, **dùng lại đúng IP vừa dùng**.
     Chi tiết làm ở GĐ5.
6. **Test:**
   - Hàm phân loại trang (URL + dấu hiệu DOM) viết dạng hàm thuần, có test.
   - Cờ CDP chỉ có ở chế độ `Login`; `CheckMain` chặn CDP ở chế độ thường.
   - Không có mật khẩu nào lọt vào log, trạng thái hay câu lỗi.
   - Form ↔ data round-trip.
   - Có smoke test Brave thật, mặc định Skip.

Tiêu chí:
- Build và test xanh.
- Chế độ thường không có CDP (test cũ GĐ4 vẫn xanh).
- Sai mật khẩu thì dừng, hiện câu lỗi của Shopee.
- Không log mật khẩu.
- User tự chạy thật: một shop đăng nhập được; một shop bị đòi mã thì Hotmail mở ra và app tự nhận khi user nhập xong.

### GĐ5c: Nhập shop từ BigSeller + nhập mật khẩu từ Excel (user yêu cầu 2026-11-18)

Khảo sát:
- App Update cũ **không lấy shop từ BigSeller**. User tự thêm shop vào cấu hình từng tài khoản BigSeller, lưu ở
  `%AppData%\ShopeeBackend\Workspace\shared\bigseller.json` (đọc qua `BigSellerStore.Shared`, đồng bộ Hub qua
  `config/bigseller.json`).
- Hiện có 6 tài khoản × 3 shop = 18 shop. Mỗi shop có `Name` và `ShopeeDataSheet`; không có shop id hay tài khoản
  đăng nhập Shopee.
- File mật khẩu user gửi (`shop sơn.xlsx`, sheet "Trang tính1") có các cột `ACC` (tài khoản Shopee), `Pass`, `SĐT`,
  `pass Mail`, `mail`, cùng các cột thông tin cá nhân / ngân hàng **không được đọc**.
- Chỉ 2/18 shop khớp tên với `ACC`. User đang hỏi lại thông tin. Vì vậy làm thành **chức năng trong app**, nhập lại
  được nhiều lần, không làm một lần bằng script.

1. **Nút "Nhập shop từ BigSeller":**
   - Đọc `BigSellerStore`. Với mỗi tài khoản BigSeller × shop, tạo `SellerAccount` mới nếu chưa có tài khoản nào cùng
     `Source.BigSellerAccount` + `Source.Sheet`:
     - `Label` = tên shop;
     - `Source.BigSellerAccount` = Id tài khoản BigSeller;
     - `Source.Sheet` = `ShopeeDataSheet` (rỗng thì lấy tên shop);
     - Id tự sinh như nút Thêm;
     - proxy chung.
   - Hiện bảng xem trước: sẽ thêm gì, đã có gì. User bấm đồng ý mới thêm vào form. **Chưa lưu**, user bấm Lưu như
     thường.
   - Không ghi gì vào `bigseller.json`.
2. **Nút "Nhập mật khẩu từ Excel":**
   - Chọn file `.xlsx`. Tìm hàng tiêu đề và các cột theo tên, không phân biệt hoa thường và dấu: `ACC` / tài khoản,
     `Pass`, `mail`, `pass Mail`. Thiếu cột nào thì báo rõ.
   - **Chỉ đọc 4 cột đó.** Bỏ qua các cột khác (SĐT, định danh, ngân hàng).
   - Khớp mỗi dòng với tài khoản trên form theo thứ tự: `ShopeeLogin`, rồi `Label` (so sau khi chuẩn hoá: hạ chữ, bỏ
     dấu, bỏ khoảng trắng hai đầu). Khớp đúng, không khớp mờ.
   - Khớp được thì điền `ShopeeLogin` = ACC, `ShopeePassword`, `VerifyEmail`, `VerifyEmailPassword`.
     - Ô đang có giá trị khác thì **hỏi trước khi ghi đè**: ghi đè tất cả / chỉ điền ô trống.
     - Chưa lưu, user bấm Lưu.
   - Báo kết quả theo **tên**: bao nhiêu tài khoản được điền, tài khoản nào trên app không tìm thấy trong file, bao
     nhiêu dòng trong file không khớp tài khoản nào (chỉ ghi số, không liệt kê 236 tên).
   - Không bao giờ log hay hiện mật khẩu. Dòng nào trong file có ACC trùng nhau thì báo, không chọn bừa.
3. Đặt logic đọc và khớp trong lớp thường, có test (dùng ClosedXML sẵn có trong Core, fixture xlsx giả). Không dùng
   file thật của user trong test.

Tiêu chí:
- Nhập shop 2 lần không tạo trùng.
- Nhập mật khẩu không đụng tài khoản không khớp.
- File thiếu cột thì báo lỗi, không ghi gì.
- Không có mật khẩu trong log hay trạng thái.
- Lưu rồi mở lại thấy đủ.

### GĐ5d: Proxy tĩnh dùng chung ở mục "Proxy chung" (user yêu cầu 2026-11-18)

User muốn mục Proxy chung có thêm **ô proxy tĩnh**, nhập kiểu `193.233.210.148	8080` (IP, dấu tab, cổng; chép từ danh
sách proxy). **Có key Kiot thì dùng Kiot**, không có thì dùng proxy tĩnh này.

1. `KiotProxySettings` (proxy chung) thêm trường `Static` (string, chỉ thêm, giữ `[JsonExtensionData]`).
   `SellerProxySource.Resolve` cho tài khoản đi proxy chung:
   - có key Kiot → Kiot như cũ;
   - không có key, có `Static` hợp lệ → proxy tĩnh;
   - không có cả hai → lỗi "chưa cấu hình proxy chung".
2. `StaticProxyParser` nhận thêm dạng **cách nhau bằng khoảng trắng / tab**: `host<ws>port`, `host<ws>port<ws>user<ws>pass`.
   - Áp dụng cho cả proxy tĩnh riêng của tài khoản.
   - Các dạng cũ (`host:port[:user:pass]`, URL) giữ nguyên.
   - Câu lỗi vẫn không chứa input.
   - Dán vào ô thì trim hai đầu (cả xuống dòng khi chép từ Excel).
3. UI cột trái, mục Proxy chung:
   - thêm ô "Proxy tĩnh (dùng khi không có key Kiot)", có dòng xem trước `ProxyLease.ToString()` như ô proxy tĩnh riêng;
   - hiện rõ **"Đang dùng: Kiot"** / **"Đang dùng: proxy tĩnh"** / **"Chưa có proxy chung"**;
   - có cả hai thì ghi chú "proxy tĩnh đang bị bỏ qua vì đã có key Kiot".
4. **Cảnh báo, không chặn:** nếu từ 2 tài khoản đang bật trở lên cùng đi một proxy tĩnh (proxy chung tĩnh, hoặc trùng proxy
   tĩnh riêng), `Validate` cảnh báo: "N shop dùng chung 1 IP tĩnh — Shopee thấy các shop này cùng một IP". Đây là đánh đổi
   user tự chọn so với luật "mỗi shop một IP". Luật IP ra ≠ IP máy vẫn giữ nguyên.
5. Proxy tĩnh không user/pass thì nhà cung cấp phải cho phép IP máy (whitelist). Lỗi kết nối thì báo như proxy chết hiện
   có, không thêm gì.

Tiêu chí:
- Build và test xanh.
- Thứ tự ưu tiên Kiot > tĩnh có test.
- Parser nhận `193.233.210.148\t8080`, `193.233.210.148 8080`, có cả dạng có user/pass.
- Cảnh báo trùng IP tĩnh có test.
- Không có nhánh nào đi IP máy.

### GĐ8: ĐỔI HƯỚNG (user chốt 2026-11-18 tối): app chỉ XUẤT FILE, user tự sửa rồi tự đẩy lên Shopee

User thấy cách đơn giản hơn:
- App Harvest đọc file Excel rồi cào dữ liệu lên Hub.
- App Update vào Hub, lấy file Excel đã cào làm nguồn, lọc ra mọi SP đã có dữ liệu, rồi **xuất file xlsx theo mẫu
  Shopee**.
- User tự sửa file (viết lại tên, mô tả) và **tự đẩy lên shop**.
- App **không truy cập Shopee**: không proxy, không Brave, không đăng nhập.

Các quyết định user đã chốt:
- **Nguồn:** chọn file Excel + sheet + dải dòng. Lấy itemId từ link, rồi kéo dữ liệu từ Hub bằng `by-items`. Không sửa
  Hub.
- **File mẫu:** user tự tải bản mẫu **nâng cao** của shop / ngành cấp 1 từ Shopee rồi chọn trong app. App điền vào đúng
  mẫu đó.
- **Ảnh:** mỗi lượt xuất tạo một "thư mục tạm" trên Hub, đổi MD5 mọi ảnh cần, tải lên đó. Hết lượt thì xoá.
- **Phần GĐ3 / GĐ4 / GĐ5b / GĐ5d** (proxy, Brave, tự đăng nhập): **ẩn khỏi giao diện, giữ code**. GĐ3, GĐ5, GĐ7 dừng.

1. **Shop** (dùng lại `SellerAccount`, chỉ thêm trường):
   - `Source.WorkbookPath` (đường dẫn file Excel), `Sheet`, `FromRow`, `ToRow`;
   - giá trị mặc định (tồn kho, cân nặng, kích thước, SP mỗi file);
   - đường dẫn file mẫu đã dùng lần trước (`TemplatePaths`, có thể nhiều file, mỗi file một ngành cấp 1).

   Ảnh bảng size dùng chung (`SizeChartImagePath`). Các ô proxy / đăng nhập ẩn đi nhưng dữ liệu vẫn giữ nguyên trong
   JSON.
2. **Đọc nguồn:**
   - dùng `Shopee.Core/Harvest/HarvestWorkbookReader` (+ `HarvestLinks` / `HarvestItemKey`) để lấy itemId từ sheet trong
     dải dòng, khử trùng;
   - `HubClient.GetHarvestedByItemsAsync` để lấy dữ liệu;
   - SP chưa có dữ liệu trên Hub thì liệt kê "chưa cào" (số dòng + link), không xuất.
3. **Mẫu:**
   - user chọn 1 hoặc nhiều file mẫu nâng cao;
   - mỗi mẫu đọc ngành cấp 1 ở dòng 2 (`TemplateSchema`);
   - SP thuộc ngành nào thì vào mẫu ngành đó; ngành không có mẫu thì báo "thiếu mẫu ngành X (N SP)", không xuất SP đó;
   - mẫu basic vẫn nhận, khi đó mọi SP vào cùng một mẫu, kèm cảnh báo "basic không có thuộc tính → SP vào Bản nháp".
4. **Ảnh, theo lượt:**
   - mỗi lần bấm Xuất có `batchId`;
   - dùng `ImagePrepWorker` / `UploadStore` (GĐ2b): tải ảnh gốc bằng IP máy, đổi MD5, đưa lên kho ảnh Hub;
   - **ghi ảnh của lượt vào `upload.db` theo `batchId`**;
   - file xlsx trỏ link Hub.

   Nút **"Xoá ảnh tạm của lượt"**:
   - user bấm sau khi đã đẩy file lên Shopee, vì Shopee tự lưu lại ảnh về `cf.shopee.vn`;
   - app gọi `DELETE` từng ảnh của lượt, ảnh nào đang được lượt khác dùng thì không xoá (đếm tham chiếu);
   - danh sách các lượt hiện: ngày, shop, số ảnh, đã xoá chưa;
   - hạn 7 ngày của Hub là lưới an toàn nếu user quên;
   - có cảnh báo: **phải đẩy file lên Shopee trước khi xoá ảnh và trong vòng 7 ngày**.
5. **Xuất:**
   - nút **"Xuất xlsx"**: chọn thư mục ra, ghi bằng `MassUploadTemplateWriter` (chia theo SP / file, 1000 dòng, 3 MB,
     không ghi đè);
   - tên file: `<shop>_<ngành>_<ngày giờ>_<n>.xlsx`;
   - báo cáo xuất:
     - số SP đã xuất, chưa cào, thiếu mẫu, lỗi ảnh;
     - cảnh báo cắt (tên, mô tả, giá, phân loại…);
     - mở thư mục khi xong.
   - **File phải mở và lưu được bằng Excel mà Shopee vẫn nhận:** user sẽ sửa tên / mô tả bằng Excel. Kiểm writer ghi
     được file Excel mở bình thường. Nếu có điểm nào Excel lưu lại làm hỏng thì báo.
6. **Tiến trình:** thanh tiến trình theo bước (đọc nguồn → lấy Hub → ảnh → ghi file). Có nút Huỷ: huỷ thì không để lại
   file dở, ảnh đã tải lên vẫn ghi theo `batchId` để xoá được.
7. **Không làm:** sổ đăng, chống trùng, đăng nhập, mở Brave, extension.

Tiêu chí:
- build và test xanh;
- có test cho: chia ngành theo mẫu, SP chưa cào, `batchId` + xoá ảnh lượt không xoá ảnh đang dùng ở lượt khác, huỷ
  giữa chừng;
- chạy bản cách ly với Hub giả hoặc fixture: xuất ra file mở được, đúng cột;
- user chạy thật một shop: file mở được bằng Excel, sửa rồi đẩy lên Shopee được, ảnh hiện đúng (cũng là GĐ0b).

### GĐ6: Bổ sung video sau khi đăng

Làm sau khi GĐ1–5 chạy ổn. Cách làm chốt theo kết quả GĐ0 bước 5:
- Nếu file "Cập Nhật Hàng Loạt" có cột video: làm theo lô.
- Nếu không: sửa từng SP.

1. Lấy các SP `uploaded` mà `raw` có `product_images.video`, chưa có video trên shop. Tìm mã SP mới bằng cách lọc
   danh sách SP theo SKU = `item_id` gốc.
2. Tải video từ URL trong `raw` (`default_format.url`) qua proxy của lượt:
   - URL hết hạn hoặc 403 → bỏ, ghi lý do;
   - bỏ video ≥ 60 s hoặc > 30 MB (như app cũ, dùng lại `Mp4Duration`);
   - đổi MD5 bằng cách ghi lại container (remux), không mã hoá lại hình.
3. Extension mở trang sửa SP, gán file vào ô tải video bằng `DOM.setFileInputFiles`, chờ Shopee xử lý xong, bấm Lưu,
   đọc thông báo kết quả.
4. Theo nhịp chạy chậm của GĐ5, xen giữa các lượt đăng. Sổ đăng thêm cột `video_status`.

### GĐ7: Chế độ `Subaccount`: mượn hồ sơ của app check đơn (làm sau GĐ5)

Dùng lại cơ chế "xin khung / trả khung" mà luồng KM đã có trên Hub (`suite/server/Shopee.Hub.Web/Data/
HubDatabase.KmKhung.cs`, client ở `suite/suite/Shopee.Suite/Infrastructure/OrdersModuleHost.AccountLease.cs`). Hub
giữ sổ `km_paused`, quá hạn thì tự gửi lệnh Chạy lại cho check đơn, kể cả khi máy đăng SP đã tắt.

1. App đăng SP dùng suất riêng `<id máy>:upload`, không dùng suất `:orders`. Nhờ vậy Hub coi check đơn là "máy
   khác" và gửi lệnh Dừng, kể cả khi hai app chạy trên cùng một PC.
2. **Xin khung** cho login subaccount, kèm hạn đủ cho cả lượt đăng các shop của login đó. Chờ, gọi lại tới khi
   `Trong = true`. Có trần thời gian chờ: quá trần thì bỏ login đó, báo user, sang tài khoản kế.
3. **Giữ khoá `orders:<login>`** bằng suất `:upload` và heartbeat (nhịp 60 s), để check đơn không chiếm lại hồ sơ
   giữa chừng. Mất Hub thì **không chạy** chế độ này. Khác check đơn ("mất hub vẫn cho chạy"): không có khoá thì không
   ngăn được check đơn giết Brave.
4. Kiểm hồ sơ không còn Brave nào đang giữ (check đơn đã dừng hẳn), rồi mở Brave bằng hồ sơ của check đơn, **không
   proxy**, nạp extension đăng SP, **không nạp** `shopee-orders`.
5. Điều hướng: SSO "Kênh Người bán" → trang chọn shop → chọn đúng shop → "Tất Cả Sản Phẩm" → "Đăng Hàng Loạt".
   **Không cần HTML mới** (user xác nhận 2026-11-18): check đơn lần nào cũng phải đi qua SSO → chọn shop, code đã có
   sẵn. Phía C#: `OrdersBridgeSession.LoginAndReachPickerAsync` (nút "Mở list shop" là `MoDenDanhSachShopAsync`).
   Phía extension: `suite/extensions/shopee-orders/flow-shop.js`, `core.js`. Cookie hết hạn thì dừng, báo user.
   Không tự đăng nhập.

   **Chốt khi làm GĐ7, hai cách:**
   - **(A) Thêm "vòng đăng SP" vào chính app check đơn**, giống các vòng KM / Combo / Flash Sale (`flow-km.js`,
     `flow-combo.js`, `flow-flashsale.js`). Dùng lại nguyên đăng nhập, chọn shop, kênh WS, khoá và cách nhường khung
     của KM. Không có hai app tranh một hồ sơ. Đổi lại, phải đưa phần dựng file (`MassUpload/` đang ở
     `workspace/src/Shopee.Core`) sang được bên `suite` (hai repo có hai bản `Shopee.Core` riêng).
   - **(B) App đăng SP mượn hồ sơ** như các bước 1–4, 6–7. Phải chép hoặc gọi lại luồng SSO → chọn shop của check đơn.

   Nghiêng về **(A)** vì luồng SSO → chọn shop đã chạy ổn trên production. (B) phải viết lại phần đó và tự giữ khoá.
   Nếu chọn (A) thì các bước 1–4, 7 thay bằng cơ chế sẵn có của vòng KM.
6. Đăng lần lượt các shop dưới login đó trong cùng một lần mượn, theo nhịp chạy của GĐ5. Hết trần ngày hoặc hết
   khung giờ thì trả khung sớm, không giữ check đơn bị dừng qua đêm.
7. Xong hoặc lỗi: đóng Brave (về 1 tab trước) → nhả khoá → **trả khung** (đặt trong `finally`). Hub gửi lệnh Chạy,
   check đơn chạy lại.
8. Cần chốt khi làm: đường dẫn hồ sơ của check đơn lấy từ đâu (DB của module Đơn hàng hay cấu hình), và check đơn có
   chạy trên máy khác không. Nếu khoá đang nằm ở máy khác thì hồ sơ không có trên máy này, phải bỏ qua.

## Kết quả GĐ0

- **2026-11-17 16:16 và 16:17:** user tải file `Shopee_mass_upload_GD0_TEST_3sp.xlsx` (sửa XML, 3 SP / 29 dòng) lên
  2 lần, cả 2 lần đều **3/3 Thành công**.
  - ⇒ Shopee **nhận file sửa thẳng XML**, kể cả checksum dòng 2. Không cần Excel COM.
- **Trang "Đăng Hàng Loạt"** có 2 tab: "Tải về Bản Mẫu" và "Đăng lên".
  - Tab "Đăng lên": vùng kéo-thả + nút "Chọn tập tin". File **≤ 3.0 MB, chỉ nhận xlsx**.
  - Bảng "Đã lưu" có cột Ngày / Tên File / Sản Phẩm (`3/3`) / Trạng thái (`Thành công`) / Hoạt động. Shopee giữ bản
    ghi 30 ngày.
- **Phát hiện quan trọng 1: SP đăng hàng loạt chỉ vào mục "Sản phẩm Chưa được đăng".** Trang ghi rõ phải kiểm tra rồi
  chọn **"Lưu và Hiển Thị"**. ⇒ Thêm bước **GĐ3b: hiển thị SP**. Sau khi file "Thành công", extension vào "Sản phẩm
  Chưa được đăng", chọn đúng các SP vừa đăng (lọc theo SKU = `item_id` gốc) rồi bấm "Lưu và Hiển Thị". SP bị Shopee
  đòi bổ sung (thuộc tính bắt buộc…) thì ghi `needs_fix` kèm lý do. Sổ đăng thêm trạng thái `draft` → `published`.
  - Ảnh chụp 2026-11-17: danh sách Sản phẩm có tab "Chưa được đăng (7)", trong đó có 2 tab con "Đã ẩn (1)" (một SP
    test cũ, không phải của GĐ0) và **"Bản nháp (6)"**, tức 3 SP × 2 lần tải. ⇒ SP đăng hàng loạt nằm ở **Chưa được đăng
    → Bản nháp**. Ô tìm kiếm nhận "Tên sản phẩm, SKU sản phẩm…", nên lọc theo SKU gốc làm được. Shop thử có "Đang hoạt
    động (0)".
  - Ảnh chụp tab "Bản nháp (6)":
    - **Ảnh bìa hiện đủ** ⇒ Shopee kéo được ảnh từ URL `susercontent`.
    - **Giá đúng**: 2.500.000; 360.000–410.000; 139.000–169.000.
    - **Tồn kho cộng theo model**: 100 / 800 (8×100) / 2k (20×100). ⇒ Tồn kho mặc định nên hiểu là **mỗi phân loại**
      (cấu hình ghi rõ như vậy).
    - "SKU sản phẩm: -", vì script GĐ0 chưa ghi cột D. App thật sẽ ghi.
    - Mỗi dòng có thao tác **Cập nhật / Hiển thị / Xem thêm**. **"Hiển thị" bị mờ (không bấm được)** ở SP Test 11K và
      Vòng tay; **bấm được** ở SP Bóng đá.
      - Vòng tay có nhãn vàng **"Có nguy cơ vi phạm"**: tên có thương hiệu Daniel Wellington nhưng không khai thương
        hiệu (file basic không có cột này).
      - Test 11K: chưa rõ lý do, chờ user mở "Cập nhật" xem.
    - ⇒ **GĐ3b chi tiết hoá:**
      - Với mỗi bản nháp vừa đăng (lọc theo SKU): "Hiển thị" bấm được thì bấm, chờ SP chuyển sang tab khác, ghi
        `published`.
      - "Hiển thị" mờ thì ghi `needs_fix` kèm nhãn trên dòng (vd "Có nguy cơ vi phạm"), không cố sửa.
      - UI có danh sách `needs_fix` để user sửa tay. Hướng tự sửa (thương hiệu / thuộc tính qua "Cập Nhật Thuộc Tính")
        để GĐ sau, khi đã biết các lý do hay gặp.
    - **Rủi ro nội dung:** SP có tên thương hiệu thật (DW, …) sẽ bị gắn "nguy cơ vi phạm". Có thể cân nhắc lọc hoặc
      xoá tên thương hiệu khỏi tiêu đề ở bước map. Chờ user quyết, chưa đưa vào phạm vi.
  - Ảnh chụp trang sửa bản nháp Vòng tay ("Chi tiết sản phẩm", bấm "Cập nhật"):
    - **Ảnh SP 5/9 + ảnh bìa đúng.** **Ngành đúng**: "Phụ Kiện Thời Trang > Vòng tay & Lắc tay" (ngành lá, 2 cấp).
    - **Shopee tự điền sẵn thuộc tính** (8/12): Thương hiệu = No brand, Giới tính, Xuất xứ, Chất liệu, Dịp, Phong cách…,
      kèm dòng "Thông tin được cung cấp hoặc điền sẵn bởi nền tảng chỉ mang tính tham khảo". ⇒ Thiếu cột thuộc tính trong
      file basic **không chặn** việc đăng.
    - Trang có ô **"Video sản phẩm"**: tối đa 30 MB, 10–60 s, MP4.
    - Cuối trang có ô tick "Tôi đồng ý với Điều khoản và Điều kiện" (đã tick) và 3 nút **Hủy / Lưu & Ẩn / Hiển thị**,
      trong đó nút "Hiển thị" **bấm được**, dù ở danh sách link "Hiển thị" của SP này bị mờ.
    - ⇒ **Đổi GĐ3b và gộp với GĐ6:** hiển thị SP **qua trang sửa từng bản nháp**, không qua link ở danh sách. Với mỗi
      bản nháp (lọc theo SKU), app làm lần lượt:
      1. Bấm "Cập nhật".
      2. (GĐ6) Gán video nếu có.
      3. Đọc các ô báo đỏ, nếu có.
      4. Đảm bảo ô "Tôi đồng ý" đã tick.
      5. Bấm **"Hiển thị"**.
      6. Đọc thông báo kết quả: thành công → `published`; Shopee chặn → `needs_fix` + lý do.
      - Mỗi SP một lần mở trang, nên chậm. Hợp với nhịp chạy chậm đã chốt.
    - **Lý do "Hiển thị" bị mờ** (di chuột vào link, 2026-11-17): *"Hãy điền đầy đủ các thuộc tính bắt buộc bằng cách
      chỉnh sửa sản phẩm này hoặc vào cập nhật thuộc tính hàng loạt để chỉnh sửa trước khi sản phẩm được hiển thị."*
      ⇒ Bị chặn vì **thiếu thuộc tính bắt buộc**. Phần Shopee tự điền chưa đủ, hoặc chưa được tính khi chưa lưu.
      **Phải có thuộc tính thì SP mới hiển thị được.** Nguồn có sẵn: `item.attributes[]` trong `raw`
      (`id, name, value, val_id`). 3 hướng, chọn sau khi xem tab "Tải về Bản Mẫu":
      1. **Bản mẫu theo ngành** có cột thương hiệu + thuộc tính (sheet "Đăng tải bản mẫu" có `ps_brand` và các cột thuộc
         tính dạng "Chất liệu (Nhiều lựa chọn)"). Điền luôn lúc đăng, mỗi file một nhóm ngành. Đây là hướng ưu tiên.
      2. Công cụ **"Cập Nhật Thuộc Tính"** hàng loạt sau khi đăng.
      3. Điền trên trang sửa từng SP lúc bấm "Hiển thị".
    - **Đơn vị cân nặng = gram** (tab "Vận chuyển" hiện `200 gr`). Kích thước tính bằng cm (`10 × 10 × 10 cm`). Cột
      `ps_weight` ghi gram; cấu hình mặc định ghi rõ đơn vị g / cm.
    - **Đã thử "Hiển thị" trên trang sửa Test 11K** (URL `banhang.shopee.vn/portal/product/unpublished/<id>`): bị chặn,
      thanh đỏ báo *"Không thể lưu vì sản phẩm đang có 1 lỗi cần được chỉnh sửa. Chỉnh sửa ngay"*. ⇒ Shopee tự điền
      thuộc tính **không đủ** để hiển thị. **Phải điền thuộc tính bắt buộc từ file** (bản mẫu nâng cao).
      - Trang sửa có tab "Thông tin khác": Tình trạng = Mới, ô "SKU sản phẩm".
      - Kênh vận chuyển trên trang theo cài đặt của shop: Nhanh bật, Tủ SPX bật, Viettel Smartbox tắt ("Quá chiều cao").
      - **Lỗi đó là "Bảng quy đổi kích cỡ"** (bấm "Chỉnh sửa ngay"): ô bắt buộc (*), báo *"Không thể để trống Bảng
        quy đổi kích cỡ"*, cho chọn Template hoặc Hình ảnh. Ngành áo nữ (100242) bắt buộc bảng size. JSON gốc có
        `size_chart = ""` (chỉ có `size_chart_info` dạng bảng).
      - **App Update cũ đã xử lý đúng ô này**: mọi SP đều được tải lên **một "Ảnh Update" dùng chung** vào ô size chart
        (radio "Tải lên hình ảnh"). Ảnh nằm ở `D:\images\1.jpeg`, đồng bộ qua Hub `images/default-update.jpg`; xem
        `BigSellerProductUpdateRunner.Process.cs:34-70` và `UpdateProductViewModel.cs:32`.
        ⇒ App mới làm tương tự qua cột **`et_title_size_chart` (O)**:
        - **User chốt 2026-11-17:** bảng size = **1 ảnh dùng chung**, như app cũ (`D:\images\1.jpeg` / Hub
          `images/default-update.jpg`). **Mọi SP** dùng ảnh này (không dùng ảnh bảng size gốc), cho qua bước đệm MD5
          như ảnh khác (mỗi shop một MD5), host trên Hub rồi ghi URL vào cột O.
        - Cột N (Template) để trống. UI cho chọn ảnh khác, giống ô "Ảnh Update" của app cũ.
        - Thiếu ảnh này thì không chạy lượt, báo rõ "Chưa có ảnh bảng size".
        - Nếu Shopee báo lỗi ô O ở ngành không có bảng size thì chỉ điền cho ngành cần. Theo dõi khi chạy thật.
      - Bản cũ còn chờ (không bắt buộc): thử "Hiển thị" trên trang sửa với SP không có thương hiệu (Test 11K) để biết bấm có được không, và
      Shopee báo gì; xem **đơn vị cân nặng** ở tab "Vận chuyển".
- **Bản mẫu nâng cao** (tab "Tải về Bản Mẫu" → "Bản mẫu nâng cao" → chọn **ngành cấp 1**, chỉ 1 ngành mỗi lần; file
  `Shopee_mass_upload_<ngày>_<mã ngành cấp 1>.xlsx`, mẫu 100009 = Phụ Kiện Thời Trang, nén 148 KB):
  - Dòng 2: `advanced | <hash> | 100009 | <shop_id>`. Cột A..AK giống bản basic.
  - Thêm cột `ps_brand` (mã thương hiệu, tra ở sheet ẩn `HiddenShopBrand`; **để trống = No brand**).
  - Thêm các cột **`ps_product_global_attribute.<attr_id>`**. **Mã thuộc tính trùng `item.attributes[].id` trong JSON
    get_pc** (đã đối chiếu: 100022 Giới tính, 100037 Xuất xứ, 100134 Chất liệu, 100155 Dịp, 100169 Phong cách,
    100194 Kiểu vòng tay…).
  - Sheet ẩn **`HiddenCatProps`**: với mỗi **ngành lá**, mỗi cột thuộc tính được đánh `MANDATORY` / `OPTIONAL` /
    `IRRELEVANT`. Ví dụ Vòng tay & Lắc tay (100026): chỉ **Giới tính** là MANDATORY. Cột A chọn ngành lá theo danh sách
    `"<id>-<đường dẫn>"`.
  - Sheet ẩn **`HiddenAttr`**: các giá trị hợp lệ của thuộc tính kiểu "chọn theo danh sách" (vd Giới tính: Nữ / Nam /
    Unisex; Sản phẩm đặt theo yêu cầu: Đúng / Không).
  - Sheet **"Ánh xạ giá trị thuộc tính"**: giá trị đề xuất theo ngành lá × thuộc tính. Kiểu "đề xuất hoặc tùy chỉnh"
    nhận giá trị tự do, tối đa 5 giá trị ngăn bằng `;` không cách.
  - **Vướng:** JSON trả **giá trị tiếng Anh** (`Female`, `Elegant`, `Chain & Link`) kèm `val_id`; bản mẫu cần **tên
    tiếng Việt** (`Nữ`, `Thanh lịch`, `Dạng chuỗi`) và không có `val_id`. ⇒ Cần **từ điển (attr_id, val_id / tên EN) →
    tên VN**.
    - Gieo sẵn bằng tay cho các thuộc tính hay gặp.
    - Thuộc tính MANDATORY kiểu danh sách chưa có trong từ điển → để trống, SP vào `needs_fix`, ghi cặp giá trị thiếu
      để bổ sung từ điển.
    - Kiểu tùy chỉnh chưa ánh xạ → bỏ qua nếu là OPTIONAL.
    - (Khảo sát sau: trang sửa SP của Seller Centre có thể tải danh sách thuộc tính kèm `val_id` + tên VN; nếu có thì tự
      dựng được từ điển.)
  - ⇒ **Đổi thiết kế:**
    - Dùng **bản mẫu nâng cao**: gom SP theo **ngành cấp 1** (`categories[0]`), mỗi nhóm tải một bản mẫu nâng cao của
      shop.
    - Writer đọc `HiddenCatProps` để biết thuộc tính bắt buộc của ngành lá, và đọc `HiddenAttr` để kiểm giá trị kiểu
      danh sách trước khi ghi.
    - Bản basic chỉ làm dự phòng.
  - **GĐ0c (2026-11-17):** file `Shopee_mass_upload_GD0c_nangcao_vongtay.xlsx` (bản mẫu nâng cao 100009, 2 SP vòng
    tay, có Giới tính = Nữ và vài thuộc tính tùy chọn, SKU = item_id). Chờ user tải lên, rồi xem link "Hiển thị" ở
    danh sách bản nháp có bấm được không.
  - **Kết quả GĐ0c:** đã lên thêm 2 bản nháp (danh sách thành 8 SP), nhưng link **"Hiển thị" vẫn mờ** dù đã điền Giới
    tính (thuộc tính MANDATORY duy nhất của ngành 100026).
    - Nghi ngờ chính: **thương hiệu**. Ghi chú cột AL ghi *"Bắt buộc phải điền thương hiệu hợp lệ hoặc không có thương
      hiệu trên shopee"*, mà GĐ0c để trống AL. Sheet `HiddenShopBrand` của shop rỗng.
    - **GĐ0d** (`Shopee_mass_upload_GD0d_thuonghieu.xlsx`): cùng 2 SP, nhưng SP `26393229156` có AL = `0`, còn SP
      `41355991937` có AL = `No brand`. Xem SP nào được Shopee nhận và bấm được "Hiển thị". Song song đó, mở trang sửa
      bản nháp GĐ0c → "Hiển thị" → "Chỉnh sửa ngay" để thấy đúng ô lỗi.
  - **Kết quả mở trang sửa bản nháp GĐ0c (user, 2026-11-17):** ô lỗi là **Bảng quy đổi kích cỡ**. Chọn "Hình ảnh",
    tải 1 ảnh lên là bấm "Hiển thị" được. ⇒ Thương hiệu để trống **không** chặn (Shopee tự điền No brand). Thuộc tính
    bắt buộc (Giới tính) đã điền qua file thì nhận đúng. **Thứ chặn duy nhất còn lại là bảng size.** GĐ0d (thử thương
    hiệu) không cần nữa.
  - **GĐ0e** (`Shopee_mass_upload_GD0e_bangsize.xlsx`): 1 SP (Ahellogirl, SKU `41355991937`), bản mẫu nâng cao, Giới
    tính = Nữ, thương hiệu trống, **cột O = URL ảnh** (tạm lấy 1 ảnh `susercontent` của chính SP, vì kho ảnh Hub chưa
    có). Nếu link "Hiển thị" ở danh sách sáng lên thì **cả luồng chỉ cần file + bấm Hiển thị theo lô**, không phải mở
    từng SP. Khi đó GĐ3b chuyển về cách bấm ở danh sách (từng dòng, hoặc tick nhiều rồi thao tác hàng loạt nếu có).
- **Phát hiện quan trọng 2: tải cùng file 2 lần là tạo trùng SP.** ⇒ Sổ đăng phải chặn gửi lại một file đã tải lên.
  - Trước khi tải, ghi trạng thái `uploading` + tên file duy nhất (có mã lượt).
  - Mất kết nối giữa chừng: đối chiếu bảng "Đã lưu" theo tên file trước khi gửi lại.
  - Dọn trùng: lọc theo SKU.
- **Kích cỡ file ≤ 3 MB:** mô tả lặp lại ở mọi dòng model. Writer phải đo kích cỡ file sau khi nén; vượt thì bớt SP
  trong file.
- **Còn chờ user:**
  - kiểm tra 3 SP trong "Sản phẩm Chưa được đăng": ảnh / ảnh phân loại, ngành, cân nặng gam hay kg, mô tả, thuộc tính
    bắt buộc;
  - **xoá các bản trùng** do tải 2 lần;
  - HTML tab "Tải về Bản Mẫu", ô input file của tab "Đăng lên", bảng "Đã lưu", và danh sách "Sản phẩm Chưa được đăng"
    (ô chọn, nút "Lưu và Hiển Thị");
  - file mẫu "Cập Nhật Hàng Loạt".

## Báo cáo thực thi

### GĐ1 + GĐ1b: Hub đọc SP + kho ảnh công khai (2026-11-17). XONG, chưa commit, chưa deploy

- **Hub** (`suite/server/Shopee.Hub.Web`):
  - `Data/ProductDb.HarvestRows.cs`: rows + by-items.
  - `Api/ProductApiEndpoints.cs`: route `GET /api/harvested-products/rows` (dòng nén, có `linkedAt`, không dùng để
    đăng) và `POST /api/harvested-products/by-items` (≤ 50 id, giữ thứ tự gửi).
  - `Services/PublicMediaStore.cs`, `Api/PublicMediaEndpoints.cs`: `PUT /api/media`, `GET|HEAD|DELETE
    /api/media/{name}`.
  - `HubOptions.cs`, `Program.cs`, `MaintenanceService.cs` (dọn ảnh hết hạn mỗi 60 s).
- **Dùng chung**: `HubRoutes.cs`, `HarvestedProductDtos.cs`, 2 bản giống hệt.
- **Client** `HubClient`: `GetHarvestedByItemsAsync`, `GetHarvestedRowsAsync`, `UploadPublicMediaAsync` (kiểm URL
  https + host công khai), `DeletePublicMediaAsync`.
- **Test**: Hub 312 → 356; Core.Tests 970 → 1031; Workspace.Tests 386. Build 0 warning.
- **Quy trình**: 3 vòng thợ, 3 vòng nghiệm thu (vòng 3 bắt chữ ở tiêu chí 5, phiên chính xác nhận đạt vì cảnh báo đếm
  tham chiếu chỉ đòi ở DELETE), 2 vòng phản biện.
  - Lỗi chính đã sửa: `row_no` cũ → thêm by-items; trần đĩa / xoá ảnh / HEAD; base URL bắt buộc https.
  - Ghi sổ: `P-20261117-15/16/17`.
- **Phụ thuộc commit**: test rows dựa vào migration 5 của Harvest (đang sửa dở) → commit cùng hoặc sau Harvest.
- **Checklist deploy Hub**:
  - đặt `HUB_PUBLIC_BASE_URL=https://api.schedra.net`;
  - đặt `Hub:Media:MaxTotalMb` khoảng 5000 trong `/etc/shopee-hub.env`;
  - kiểm `df -h` (còn 19 GB);
  - sau deploy làm GĐ0b: tải 1 ảnh qua `/api/media`, đặt URL vào cột O / ảnh SP, tải file lên Shopee, xem Shopee có
    kéo ảnh từ domain ngoài không.

### GĐ2a + GĐ2b: dựng file + bước đệm MD5 ảnh (2026-11-17). XONG, chưa commit

- **GĐ2a** (`workspace/src/Shopee.Core/MassUpload/`):
  - `ShopeeListingReader`.
  - `AttributeDictionary` (+ JSON nhúng).
  - `MassUploadMapper`:
    - luật cắt;
    - ma trận 2 nhóm luôn đủ;
    - thuộc tính theo `HiddenCatProps` / `HiddenAttr`;
    - resolver 3 trạng thái.
  - `TemplateSchema`.
  - `MassUploadTemplateWriter`:
    - sửa XML trực tiếp;
    - chia file theo N SP / 1000 dòng / 3 MB;
    - không ghi đè;
    - chặn ItemId trùng.
  - `MassUploadText.Clean` (một luật làm sạch, NFC).
  - Fixture: `Fixtures/mass-upload/{basic,advanced-100009,gd0-basic-3sp}.xlsx`.
  - Quy trình: 4 vòng thợ, 3 vòng nghiệm thu, 1 vòng phản biện (không có lỗi NẶNG).
- **GĐ2b** (`MassUpload/Images/`):
  - `ImageMd5Changer`: đổi byte. JPEG chèn COM sau dãy APPn; PNG chèn `tEXt`.
  - `UploadStore`: `upload.db`; trạng thái `pending → hosted → verified`, `failed`, `deleting`; đếm tham chiếu;
    Forget / Release / RetryFailed; resolver theo danh sách ảnh cần.
  - `ImagePrepWorker`:
    - cầu dao chung cho lỗi hệ thống;
    - lỗi CDN 4xx tính cho riêng ảnh;
    - 3 lần 404 ngay sau upload → dừng (URL công khai sai);
    - dọn các dòng `deleting`.
  - `SizeChartImage`: khoá theo SHA-256 nội dung file.
  - Quy trình: 4 vòng thợ, 2 vòng nghiệm thu, 1 vòng phản biện. Lỗi NẶNG đã sửa: hàng đợi bị đốt khi Hub sập, dòng
    `deleting` kẹt.
- **Test**: Core.Tests 1251/1251; MassUpload 203/203 (phiên chính tự chạy lại: 203 pass). Build 0 warning.
- **Chưa thử thật:** file do code GĐ2a tạo (`Downloads\Shopee_mass_upload_GD2a_kiemtra.xlsx`) chưa tải lên Shopee.
  Bước đệm ảnh chưa chạy với Hub thật (cần deploy GĐ1b + GĐ0b).

### GĐ4: tài khoản seller + proxy + mở Brave (2026-11-18). XONG, nghiệm thu vòng cuối ĐẠT 12/12, chưa commit

- **Code**: `workspace/src/Shopee.Core/MassUpload/Seller/*`.
  - Store.
  - `SellerKiotApi`: tính giờ theo đồng hồ server Kiot.
  - `SellerProxyProvider`.
  - `ExitIpVerifier`: IP ra ≠ IP máy (lấy lại mỗi shop) và ≠ mọi IP đã dùng trong lượt.
  - `SellerBrowserArgs`: mở `about:blank`; cờ `--webrtc-ip-handling-policy=disable_non_proxied_udp`.
  - `SellerProfilePrefs`: ghi WebRTC vào Preferences, tắt DoH trong Local State.
  - `SellerBraveProcesses`: so profile đã chuẩn hoá đường dẫn; kiểm dòng lệnh tiến trình sau khi mở; chặn khi máy có
    chính sách proxy.
  - `SellerBrowserLauncher`, `SellerRunGate`, `ProxyAuthExtension`.
- **Test**: Core.Tests 1434 pass / 1 skip (smoke). Riêng `MassUpload.Seller`: 110 pass / 1 skip.
- **Quy trình**: 3 vòng thợ, 2 nghiệm thu, 1 phản biện. Phản biện đo trên Brave 157 thật, bắt lỗi NẶNG cờ WebRTC sai
  tên làm lộ IP thật (lỗi do phiên chính chỉ đạo ở vòng 2), đã sửa.
- **Nghiệm thu vòng cuối (2026-11-18): ĐẠT 12/12.** Thử phá 13 chỗ, ở các tiêu chí 2–8 (cờ WebRTC, trang đầu,
  Preferences, DoH, kiểm sau khi mở, chuẩn hoá profile, policy, lệch đồng hồ Kiot, UsedIps, IP máy, thử lại): chỗ nào
  test cũng đỏ. Đã hoàn nguyên, phiên chính kiểm lại file khớp bản sao lưu.
- **Còn lại**: user chạy smoke test Brave thật. Đặt biến `SELLER_SMOKE_PROXY`, kiểm WebRTC bằng tay trên
  `browserleaks.com/webrtc`.

### GĐ5a: exe ShopeeUpload bản đầu (2026-11-18). XONG, chưa commit

- **Code:**
  - `workspace/src/Shopee.Upload/**` (WPF, chỉ tham chiếu Core, chưa thêm vào `.sln`);
  - `workspace/tests/Shopee.Upload.Tests/**`;
  - icon `workspace/assets/upload-icon.{png,ico}` (thêm `RenderUploadIcon` vào `IconGenerator.cs`).
- **Core sửa thêm** (chỉ trong `MassUpload/Seller/`):
  - `StaticProxyParser`: câu lỗi không chứa input; từ chối dạng gõ ngược thứ tự; `@` trong user/pass.
  - `[JsonExtensionData]` ở mọi lớp cấu hình. `RetiredIds`.
  - `Validate`: profile phải là đường dẫn tuyệt đối; profile lồng nhau coi như trùng; chặn thư mục User Data của trình
    duyệt cá nhân.
  - `SellerBrowserSession.CloseAsync` cho đóng lại khi lần trước thất bại.
- **Test:** Upload.Tests 55 pass; Core.Tests 1470 pass / 1 skip.
- **Quy trình:** 4 vòng thợ, 2 nghiệm thu, 2 phản biện. Các lỗi VỪA đã sửa:
  - lộ mật khẩu proxy khi nhập sai dạng;
  - nút Mở Brave bỏ qua Validate (trùng profile, phạm "mỗi shop một IP");
  - file có Id trùng làm app chết lúc mở (lỗi do vòng 3 sinh ra).
- **Lưu ý:** `seller-accounts.json` lưu key Kiot và mật khẩu proxy dạng chữ thường, không mã hoá.
- **Còn biết, chấp nhận:**
  - `myuser:12345:proxy.vn:8080` vẫn đọc thành host=`myuser`, cổng=`12345`: số đó có thể hiện ra log. Proxy vẫn sai nên
    không kết nối được, không đi IP máy.
  - Mật khẩu dạng URL có `%xx` bị giải mã (có từ GĐ4).
  - Khởi động lỗi thì tự thoát; bấm X hai lần không hỏi lại. Hai đường này chưa có test tự động.

### GĐ5b: tự đăng nhập Shopee seller (2026-11-18). XONG, chưa commit, chưa chạy thật với shop thật

- **Code:**
  - `MassUpload/Seller/Login/*`: chép và chỉnh từ app check đơn;
  - `SellerCdp.cs`;
  - chế độ `Login` trong launcher / gate / `CheckMain`;
  - 4 trường đăng nhập trong `SellerAccount`;
  - nút "Mở + đăng nhập Shopee";
  - nút con mắt `RevealPasswordBox`.
- **Test:** Upload.Tests 69; Core.Tests 1578 pass / 2 skip (smoke Brave thật).
- **Quy trình:** 3 vòng thợ, 1 nghiệm thu, 2 phản biện. Cả hai phản biện đều đo trên Brave thật, dùng profile tạm và
  proxy giả. Lỗi đã sửa:
  - mật khẩu gõ nhầm sang ô tài khoản khi trang vẽ lại ô;
  - alert có sẵn từ trước bị coi là sai mật khẩu;
  - trang 502 / bảo trì bị coi là đã đăng nhập;
  - cookie giữ chỗ `-` bị tính là phiên, dẫn tới tải lại trang xác minh làm mất mã user đang gõ;
  - huỷ bị treo 30 s;
  - URL có query lọt vào log.
- **Nhận biết đã đăng nhập** cần đủ ba điều kiện:
  - cookie `SPC_ST` / `SPC_EC` hợp lệ (theo `ShopeeAuth.IsSessionCookie`);
  - menu Seller Centre `a[href^='/portal/']` đang hiện;
  - các điều kiện cũ.
- **Chưa chắc, cần HTML thật:** trang xác minh, trang captcha của banhang, trang chọn shop / onboarding sau khi đăng
  nhập (nếu có).
- **Đã biết, chấp nhận:**
  - Cổng CDP còn mở trên loopback tới khi đóng Brave; UI có nhắc.
  - Ở chế độ Login, `navigator.webdriver = true` (sổ P-20261118-16).

### GĐ5c: nhập shop từ BigSeller + nhập mật khẩu từ Excel (2026-11-18). XONG, chưa commit

- **Code:** `workspace/src/Shopee.Upload/Import/*`, hai lệnh `ImportShops` / `ImportPasswords` trong `MainViewModel`.
  Không sửa Core.
- **Test:** Upload.Tests 126.
- **Quy trình:** 3 vòng thợ, 1 nghiệm thu, 1 phản biện.
- **Luật khớp:**
  - **Nhập shop:**
    - khoá trùng là (TK BigSeller, Sheet) theo `ImportText.Key`;
    - trùng tên với `Label` / `ShopeeLogin` của tài khoản đã có thì xếp vào "Có thể đã có", không thêm;
    - nhãn TK BigSeller rỗng thì hiện `TK BigSeller N (6 ký tự Id)`.
  - **Nhập mật khẩu:**
    - chỉ đọc 4 cột: ACC / Pass / mail / pass Mail;
    - khớp theo `ShopeeLogin` trước, rồi tới `Label`;
    - khớp qua Label mà tài khoản Shopee trên form khác file thì bỏ qua;
    - ACC trùng, ô lỗi Excel, ô chỉ có khoảng trắng: bỏ dòng;
    - ô trống trong file không xoá dữ liệu trên form;
    - ưu tiên sheet đang hiện.
- Chỉ điền vào form; user bấm Lưu mới ghi file. Không ghi `bigseller.json`.
- **Sửa kèm:** `SecretText.Mask` với bí mật ≤ 4 ký tự chỉ hiện "(đã nhập)".
- **Chưa làm:** tick chọn từng shop trong bảng xem trước.

### Deploy Hub GĐ1 + GĐ1b (2026-11-18 18:31)

- Chỉ thay `Shopee.Hub.Web.dll`. Các DLL khác và `app.css` trên VPS đã khớp HEAD; `app.css` chỉ khác kiểu xuống dòng.
- Bản cũ sao lưu ở `/opt/shopee-hub-backups/Shopee.Hub.Web.dll.20261118-183139` và
  `/opt/shopee-hub-backups/shopee-hub.env.20261118-183139`.
- `/etc/shopee-hub.env` thêm `HUB_PUBLIC_BASE_URL=https://api.schedra.net` và `Hub__Media__MaxTotalMb=5000`. Ổ VPS còn
  trống 36G.
- Kiểm thật:
  - health ok;
  - `PUT /api/media` → URL `https://api.schedra.net/api/media/…`; tải công khai được 200 `image/jpeg`; `DELETE` 200;
  - `by-items` 200.
- Lưu ý: token client thật nằm trong `hub.db` (`settings.api.token`). Biến `HUB_API_TOKEN` trong env chỉ dùng để khởi
  tạo lần đầu và đã cũ.
- **Còn GĐ0b:** cần một lần upload thật lên Shopee bằng file có ảnh trỏ về `api.schedra.net`.

### GĐ5d: proxy tĩnh dùng chung (2026-11-18). XONG, chưa commit

- **Đã làm:**
  - `KiotProxySettings.Static`. Ưu tiên Kiot rồi mới tới tĩnh.
  - Parser nhận dạng tab / khoảng trắng.
  - Cảnh báo khi nhiều shop đi chung một IP tĩnh.
  - UI hiện dòng "Đang dùng".
- **Phản biện bắt lỗi NẶNG (có từ GĐ4):** host chứa ký tự vô hình hoặc chữ số toàn khổ làm Brave lặng lẽ đi IP máy. Đã
  sửa 4 lớp:
  - parser chỉ nhận host ASCII theo `ProxyHostRules`;
  - `SellerBrowserArgs.Build` chặn chốt cuối;
  - `ExitIpVerifier` so `IdnHost` với host đưa cho Brave (`BadProxyHost`);
  - kiểm host do Kiot trả về.

  Đã chạy lại trên Brave headless với proxy giả: chuỗi lỗi bị chặn trước khi mở Brave.
- **Test:** Upload 133; Core 1640 pass / 2 skip.

### GĐ8: xuất file xlsx từ Hub (2026-11-18). XONG, chưa commit, chưa chạy thật với shop thật

- **Code:**
  - Core `MassUpload/Export/*`;
  - `UploadStore` lên schema v3: bảng `export_batch` / `batch_image`, cột `expires_at` và `template_shop_id`;
  - app: `MainViewModel.Export.cs`, `Services/ExportService.cs`, `UploadFeatures.ShowBrowserFeatures = false`.
- **Test:** Upload 147; Core 1681 pass / 2 skip.
- **Quy trình:** 3 vòng thợ, 1 nghiệm thu, 1 phản biện.
  - Phản biện bắt lỗi NẶNG: ảnh dùng lại giữa các lượt sắp hết hạn trên Hub, nhưng app vẫn báo "7 ngày".
  - Đã sửa: mỗi lượt làm mới các ảnh (kể cả bảng size) còn hạn dưới 6 ngày, và hiện **hạn thật của lượt**.
- **Luật ảnh theo lượt:**
  - chỉ lượt `running` / `done` chưa xoá mới giữ ảnh;
  - chờ ảnh tối đa 3 phút kể từ lần cuối có ảnh xong;
  - Hub hết chỗ thì dừng lượt;
  - có nút "Làm lại ảnh lỗi".
- **Kiểm bằng Excel COM:** mở, sửa, lưu, đọc lại đều được. Excel bỏ 2 dấu xuống dòng cuối ở 30 ô ghi chú dòng 6. **Chưa
  biết Shopee có nhận file đã lưu qua Excel không** → user thử thật (cũng là GĐ0b).

### Thử thật 2026-11-19: mã phân loại + ảnh từ Hub (GĐ0b)

- File `Downloads\Shopee_mass_upload_TEST_ma_phan_loai.xlsx`:
  - 1 SP chân váy, mẫu nâng cao 100017;
  - phân loại ghi bằng **mã**: `101054` / `6258`…, `101056` / `9845`…; riêng `Nâu tây` để chữ;
  - bảng size là ảnh trên `api.schedra.net`.
- Kết quả user chụp:
  - Shopee nhận file;
  - nhóm `101054` hiện là "Màu sắc", mã màu hiện đúng tên chuẩn (Đen, Trắng, Trắng kem, Hồng dâu, Đỏ đô, Xám khói, Nâu nhạt);
  - `Nâu tây` giữ nguyên chữ;
  - **ảnh bảng size từ Hub hiện đúng**, tức Shopee kéo được ảnh từ `api.schedra.net` ⇒ **GĐ0b đạt**.
- Kết luận: cột phân loại nhận **cả mã chuẩn lẫn chữ**; có thể trộn mã và chữ trong cùng một nhóm.

### GĐ8b: phân loại khớp danh sách chuẩn thì ghi bằng mã (user chốt 2026-11-19)

1. **Dữ liệu chuẩn:** nhúng `MassUpload/VariationStandard.json`, lấy từ trang "Danh sách liên kết Ngành hàng – Phân loại
   hàng" user gửi (ngành Thời Trang Nữ):
   - Màu sắc `101054`;
   - Size `101056`, gồm các nhóm phụ US / Inch / EU / Tuổi / Quốc Tế / cm / mm;
   - Giới tính `101270`.

   **Chỉ áp cho SP thuộc ngành cấp 1 100017.** Ngành khác giữ chữ như cũ, cho tới khi có danh sách của ngành đó.
2. **Luật đổi:**
   - Tên nhóm phân loại khớp đúng tên nhóm chuẩn ("Màu sắc", "Size", "Giới tính"; không phân biệt hoa thường, NFC, trim,
     gộp khoảng trắng) thì ghi **mã nhóm**.
   - Từng giá trị trong nhóm đó khớp đúng tên chuẩn thì ghi **mã giá trị**. Không khớp thì giữ nguyên chữ. Shopee nhận trộn
     mã và chữ (đã thử thật).
   - Tên **trùng ở nhiều nhóm phụ** (ví dụ "16", "18", "20" có ở cả US lẫn EU) thì **giữ chữ**, không đoán.
   - Không khớp gần đúng ("Dài váy 90cm" không thành "90 cm").
   - Tên nhóm không khớp nhóm chuẩn thì giữ chữ cả nhóm.
   - Tên nhóm khớp nhưng **không giá trị nào** trong nhóm ra mã thì **giữ chữ cả tên nhóm** (vd "Màu sắc" + "TRẮNG (3lớp)
     SẴN"… → F = "Màu sắc", không ghi `101054`). Chỉ ghi mã nhóm khi có **ít nhất 1** giá trị ra mã, giống ca đã thử thật
     (tổ hợp "mã nhóm + toàn chữ" chưa thử trên Shopee). Chốt 2026-11-19.
   - Sau khi đổi, các giá trị trong nhóm phải vẫn khác nhau. Hai chữ khác nhau mà cùng ra một mã thì giữ chữ cả hai.
3. **Báo cáo:** mỗi SP ghi "phân loại chuẩn: x/y giá trị đổi sang mã".

### GĐ9: ảnh gốc nằm trên Hub, Harvest đẩy lên lúc cào (user chốt 2026-11-19)

**Vì sao:** 3–5 nghìn SP mà shop nào cũng tải lại ảnh từ CDN Shopee thì gọi Shopee quá nhiều. User chốt:
- **app Harvest tải ảnh gốc ngay lúc cào**, tận dụng 1–2 phút nghỉ giữa các SP, rồi đẩy lên Hub;
- app xuất file **không gọi Shopee nữa**.

**Luồng:**
1. **Harvest** (sau khi đã đẩy JSON SP lên Hub):
   - lấy danh sách ảnh cần: ảnh SP theo thứ tự (tối đa 12) + ảnh từng lựa chọn phân loại nhóm 1;
   - hỏi Hub những ảnh nào **chưa có**;
   - tải phần còn thiếu từ CDN **trong trình duyệt**, cùng đường mạng đang cào;
   - đẩy lên Hub.

   Làm trong lúc nghỉ, không kéo dài thời gian mỗi SP. Lỗi tải hay đẩy thì ghi lại, lượt sau làm tiếp. Không chặn việc cào.
2. **Hub — kho ảnh gốc** (khác kho ảnh tạm công khai):
   - khoá theo **mã ảnh Shopee** (`vn-11134207-…`), lưu nguyên bytes;
   - kiểm magic JPEG / PNG / WebP, trần cỡ mỗi ảnh;
   - route:
     - `POST /api/source-images/missing` {ids} → các id chưa có;
     - `PUT /api/source-images/{id}` (idempotent; id đã có thì 200 và không ghi lại);
     - `HEAD` / `GET /api/source-images/{id}`.
   - **Không công khai:** cần X-Api-Token. Trần tổng dung lượng và đĩa trống tối thiểu như kho ảnh tạm. Quá trần thì từ
     chối ghi (507), không tự xoá.
3. **ĐỔI 2026-11-19 (user chốt): ảnh đổi MD5 được tạo ĐỘNG, chỉ khi Shopee đến lấy ảnh.**
   - Link dạng `GET /api/v/<token>.<ext>`, **công khai**, không cần token API. `<token>` mang id ảnh + shopKey, có **chữ
     ký HMAC bằng khoá bí mật của Hub**, để không ai tự tạo link cho id hay shop tuỳ ý.
   - Khi Shopee gọi: Hub kiểm chữ ký → đọc ảnh gốc → đổi MD5 theo (shop, id), tất định → trả bytes. Có
     `Cache-Control: public, max-age=…, immutable`, ETag, hỗ trợ `HEAD`.
     - Ảnh gốc không có → 404.
     - Chữ ký sai → 404, không lộ lý do.
   - **Không lưu file bản đổi MD5, không hạn 7 ngày, không chỉ mục (shop, id).** Link sống chừng nào ảnh gốc còn.
   - `POST /api/source-images/links` (X-Api-Token) {shopKey, ids} → mỗi id trả {url} hoặc `missing`. Chỉ tính link và
     kiểm ảnh gốc có hay không.
   - Thuật toán đổi MD5: giống `ImageMd5Changer` (chép sang Hub). Không bắt buộc khớp byte với client nữa, nhưng giữ khớp
     để có một cách duy nhất.
   - Khoá ký: Hub tự sinh một lần, lưu bền (settings `hub.db` hoặc file trong `HUB_DATA_DIR`). Mất khoá thì mọi link cũ
     hỏng ⇒ không bao giờ tự xoay khoá.
   - Hệ quả cho app: bỏ ảnh tạm theo lượt **cho ảnh SP**. Ảnh bảng size vẫn dùng kho tạm như cũ, hoặc đưa vào kho gốc với
     một id riêng — chốt khi làm phần app.
   - Hộp xác nhận "Xoá ảnh gốc Harvest" phải cảnh báo: **file đã xuất mà chưa đẩy lên Shopee sẽ hỏng ảnh.**

   (Bản cũ, đã bỏ: tạo sẵn lúc xuất, `POST /api/source-images/variants`)
   {shopKey, ids} → mỗi id trả {url, path, expiresAt} trong **kho ảnh tạm công khai** hiện có (hạn 7 ngày).
   - Thuật toán đổi MD5 **giống hệt** `ImageMd5Changer` (HMAC theo shop + ảnh, muối v1). Chép code sang Hub; test so bytes
     với bản phía client.
   - Ảnh chưa có trong kho gốc thì trả `missing`.
   - Cùng (shop, id) gọi lại khi bản cũ còn hạn > N ngày thì trả lại bản cũ (theo tham số `minRemainingDays`), không thì
     tạo bản mới.
4. **App Upload (GĐ8):** bước ảnh của lượt xuất gọi `variants` thay vì `ImagePrepWorker` tải CDN.
   - URL / path ghi vào `upload.db` theo `batchId` như cũ, để nút "Xoá ảnh tạm của lượt" vẫn chạy (xoá bản tạm công
     khai, **không** xoá ảnh gốc).
   - Ảnh `missing` trên Hub: dùng đường cũ (tải CDN bằng IP máy) làm dự phòng, ghi rõ số ảnh phải tải CDN trong báo cáo.
     Hoặc bỏ SP, nếu user tắt dự phòng; mặc định bật.
   - Ảnh bảng size vẫn đi đường cũ (file trên máy → kho tạm).
   - **shopKey = `SellerAccount.ImageKey`** (chuỗi ngẫu nhiên 128-bit, sinh một lần, lưu trong `seller-accounts.json`, không đổi),
     dùng cho cả đổi MD5 đường CDN lẫn `links` của Hub. Trước đó shopKey là `seller-N`, chỉ duy nhất trên một máy: hai máy hoặc
     tạo lại tài khoản thì hai shop khác nhau ra cùng MD5 / cùng URL. **Đổi shopKey nghĩa là MD5 của các lượt sau khác các lượt
     trước** — không sao, vì file cũ đã đẩy lên Shopee rồi. Tài khoản cũ chưa có khoá: app sinh lúc mở và lưu ngay vào file
     (chỉ thêm trường đó). Chốt 2026-11-19.
5. **Thứ tự làm:** Hub (2, 3) + DTO / route dùng chung → deploy → app Upload (4) → Harvest (1).
   - Phần Harvest do phiên Harvest phụ trách code, giao theo hợp đồng API ở trên.
   - Kho ảnh gốc là **dữ liệu lâu dài**. Quy định xoá (ví dụ theo SP không còn trên Hub) chốt sau.

6. **Quản lý dung lượng trong app Upload** (user yêu cầu 2026-11-19):
   - **Hub:** `GET /api/source-images/stats` trả về:
     - ổ đĩa Hub: tổng, còn trống;
     - kho ảnh gốc: số ảnh, tổng dung lượng, trần;
     - kho ảnh tạm: số file, tổng dung lượng, trần.
   - **Hub:** `DELETE /api/source-images` xoá **toàn bộ** ảnh gốc do Harvest đẩy lên.
     - Bắt buộc body `{confirm:"XOA-HET-ANH-GOC"}`, để không xoá nhầm.
     - Xoá cả chỉ mục bản đổi MD5 trỏ tới ảnh gốc. Các bản tạm công khai đang dùng không xoá, để file đã xuất còn đẩy
       lên được; chúng tự hết hạn.
     - Trả về số ảnh đã xoá, số ảnh còn lại (phải bằng 0) và dung lượng đã giải phóng.
   - **App:** một mục "Dung lượng Hub":
     - hiện các số trên, có nút Làm mới;
     - nút **"Xoá ảnh gốc Harvest trên Hub"**: hộp xác nhận nêu rõ số ảnh và dung lượng sẽ xoá, và rằng SP sau đó phải cào
       lại ảnh, nếu không lúc xuất sẽ tải từ CDN;
     - xoá xong app gọi lại stats và **báo xác nhận "Hub còn 0 ảnh gốc"**. Nếu chưa về 0 thì báo rõ còn bao nhiêu.

7. **Chốt sau phản biện GĐ9 Hub (2026-11-19):**
   - PUT từ chối WebP và ảnh hỏng cấu trúc. Bản đang giữ không dùng được thì bản mới được thay vào. Có DELETE theo id.
   - Token **mã hoá tất định (SIV)**, chứa thêm dấu vân tay nội dung ảnh ⇒ ảnh đổi thì URL đổi, không lộ mã ảnh gốc hay
     shopKey.
   - Rate limit dạng token bucket, cho burst lớn, có log khi chặn.
   - Nghi ngờ (để sau): đoạn COM / tEXt 64 ký tự hex mà bước đổi MD5 chèn vào có thể là dấu vân tay chung giữa các shop,
     nếu Shopee không xoá metadata.

8. **Deploy Hub GĐ9: 2026-11-19 14:09.**
   - Backup: `/opt/shopee-hub-backups/Shopee.Hub.Web.dll.20261119-140943`.
   - Khoá ký sinh lần đầu.
   - Thử thật:
     - PUT 1 ảnh CDN thật, `missing` đúng;
     - `links` cho 2 shop ra 2 URL mã hoá khác nhau;
     - GET qua Cloudflare: 200 `image/jpeg`, `immutable`; MD5 shop A ≠ shop B ≠ ảnh gốc; cùng shop gọi lại ra cùng MD5;
     - sửa token → 404, thêm query → 404;
     - đã xoá ảnh thử.
   - Test: Hub 437, Core 1699.

**Dung lượng:** khoảng 1–1,5 MB/SP ⇒ 5.000 SP ≈ 5–7 GB. VPS còn trống 36 GB. Trần mặc định cho kho gốc 15 GB, chỉnh qua
`Hub:SourceImages:MaxTotalMb`.

### GĐ0b: ĐẠT (2026-11-19)

Shopee kéo được ảnh bảng size từ `api.schedra.net`.

## 5. Tiêu chí nghiệm thu

- [ ] GĐ0 xác nhận Shopee nhận file sửa XML (có ghi lại kết quả thật trong plan này).
- [ ] `dotnet build workspace/ShopeeWorkspace.sln -c Release` và Hub build xanh. Test cũ + mới xanh.
- [ ] App Update cũ, Scrape, Harvest không đổi hành vi (diff ở file dùng chung chỉ thêm, không sửa nhánh cũ).
- [ ] Chạy thật 1 tài khoản seller × 5 SP (gồm SP không biến thể, 2 nhóm, SP 70 lựa chọn): SP lên Shopee đúng ngành,
      đúng tên/giá/biến thể/ảnh; sổ đăng đúng trạng thái; chạy lại không đăng trùng.
- [ ] Rút proxy, hoặc key Kiot hết hạn: tài khoản dừng, **không có request nào ra bằng IP máy** (kiểm bằng log IP).
- [ ] Profile chưa đăng nhập: dừng tài khoản đó, không bấm gì trên Shopee.

## 6. Rủi ro & lưu ý

- **Proxy Kiot với tài khoản seller (câu user hỏi):** theo code hiện có, IP của key Kiot chỉ giữ khoảng 30 phút
  (`KiotProxyClient.cs`, `/current` "sticky ~30'"), hết hạn phải lấy IP mới.
  - Hệ quả: mỗi lần chạy, tài khoản seller sẽ đăng nhập từ một IP khác.
  - IP Kiot là IP dân cư dùng chung pool, nên có thể đã từng được tài khoản Shopee khác dùng. Shopee hay phản ứng với
    đổi IP bằng cách bắt xác minh lại (OTP/email), đăng xuất phiên, hoặc gắn cờ nhiều tài khoản cùng đi qua một IP.
  - Rủi ro này nhỏ với tài khoản dùng thử, nhưng **lớn với shop thật**.
  - Khuyên: **mua proxy tĩnh riêng cho từng tài khoản** (IP tĩnh dân cư/ISP Việt Nam, 1 IP – 1 tài khoản).
  - Nếu tạm dùng Kiot: chọn **region cố định** (không `random`) và chỉ đổi IP khi hết hạn. App sẽ dừng tài khoản khi bị
    văng đăng nhập, không tự đăng nhập lại.
  - Thiết kế GĐ4 nhận cả hai kiểu, nên đổi dần từng tài khoản không cần sửa code.
  - **Cập nhật 2026-11-17:** user chọn gói "Đổi IP VIP - VN" (IP không hết hạn), 1 key cho mọi shop, đổi IP mỗi khi
    sang shop. Cách này tách các shop khỏi nhau (không có 2 shop nào chạy trên cùng IP liền nhau). Đổi lại, mỗi lần chạy
    mỗi shop lại thấy một IP mới. Nếu shop hay bị đòi xác minh thì chuyển các shop đó sang key riêng, giữ IP lâu
    (gói cho phép), không phải sửa code.
- **Chạy cùng app check đơn (2026-11-18):** ở chế độ `Direct`, Shopee thấy cùng một shop từ 2 IP (IP máy qua subaccount
  của check đơn, IP Kiot qua tài khoản chính). User chấp nhận vì hai đường đăng nhập bằng hai tài khoản khác nhau.
  Shop nào hay bị đòi xác minh thì chuyển sang `Subaccount` (GĐ7) hoặc key Kiot riêng giữ IP lâu.
- Shopee có thể đổi file mẫu (thêm hoặc bớt cột). Writer đọc mã cột ở dòng 1 nên chịu được việc đổi thứ tự. Cột bắt buộc
  mới thì báo lỗi rõ.
- File mẫu basic **không có thương hiệu / thuộc tính**. Ngành hàng có thuộc tính bắt buộc có thể làm SP rơi vào trạng
  thái "cần bổ sung". Theo dõi ở GĐ0. Nếu nhiều, mở GĐ sau dùng "Cập Nhật Thuộc Tính".
- Ảnh lấy URL từ CDN Shopee của shop khác. Shopee có thể từ chối ảnh trùng ("không được sao chép với các danh sách
  khác"). GĐ0 sẽ lộ ra.
- Giá lấy `price_before_discount`. Nếu muốn đăng theo giá đang bán, hoặc nhân hệ số, thì thêm vào `Defaults` sau.
- Cột `cat_id` trên Hub (do `ShopeePdpParser` ghi) là **ngành cấp 1**, không phải ngành lá. Bước đăng phải lấy ngành từ
  `raw`, không dùng cột đó.
