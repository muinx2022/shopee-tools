# Plan: App "Shopee Harvest" — cào toàn bộ dữ liệu sản phẩm Shopee, lưu Hub, bỏ BigSeller

- **Ngày:** 2026-11-15
- **Trạng thái:** chờ duyệt
- **Người lập:** phiên chính · **Người thực thi:** `opus-dev` (từng giai đoạn) → `nghiem-thu` → `phan-bien`

## 1. Bối cảnh & mục tiêu

App Scrape hiện tại là `workspace/src/Shopee.Scrape` (exe `ShopeeScrape.exe`, chỉ gọi
`Shopee.Workspace.Program.Run(WorkspaceRole.Scrape, args)`). Luồng: chia link (workbook hoặc Hub `product_rows`) cho
N cửa sổ Brave → mỗi Brave đăng nhập Shopee, đi proxy Kiot, nạp extension `extensions/shopee-scrape` → với từng link,
`content.js` bấm nút **Scrape của BigSeller** (extension "Product Copy") và chờ toast kết quả → BigSeller giữ dữ liệu.

**Mục tiêu:** một exe mới cùng khuôn, nhưng ở mỗi link **tự lấy toàn bộ dữ liệu sản phẩm** (JSON PDP của Shopee) và
**lưu lên Hub trung tâm** để giai đoạn sau up thẳng Shopee. Không bấm BigSeller, không cần đăng nhập BigSeller,
không cần extension Product Copy.

Đã chốt với user (2026-11-15):
- DB: **Hub trung tâm** (Postgres `ProductDb` trên `api.schedra.net`, code ở `suite/server/Shopee.Hub.Web`).
- Ảnh/video: **chỉ lưu URL**, không tải file (bỏ `VideoDownloader` trong app mới).
- **App exe riêng** (theo khuôn `Shopee.Scrape` / `Shopee.Update`).
- Nguồn link: **giống app Scrape** (BigSeller account × shop → workbook hoặc Hub `product_rows`).

Hiện trạng code liên quan (để thợ không phải dò lại):
- Engine dùng lại được gần như nguyên: `src/Shopee.Module.MultiBrave/**` (`ScrapeRunner`, `WorkAllocator`,
  `ScrapeFailurePolicy`, `BraveInstanceSession*`, `KiotProxyRotator`, `ShopeeSessionBootstrapper`, `SessionMonitor`,
  `RunnerExtensionRpc`, `RunnerSwLifecycle`, `PortCdpHub`), `src/Shopee.Core/Scrape/*` (`ScrapeLinkSource`,
  `ScrapeWorkbook`, `ScrapeChunkMath`, `ScrapeRestWindow`).
- Chỗ dính BigSeller phải tắt khi chạy chế độ Harvest:
  - `Engine/LauncherRunnerLoop.cs:174` gọi `ExecuteScrapeStepAsync`; nhánh `NeedLogin` `:258`; `onScrapeSucceeded` `:306`.
  - `Engine/BraveInstanceSession.RunnerLoop.cs:152` (`_bigSeller.WriteBackTokenAsync`) và các lời gọi `BigSellerTokenGuard`.
  - `Engine/BraveProfileManager.cs:122` (proxy-bypass bigseller) và `:145-158` (bắt buộc có Product Copy → throw).
  - Validate `HasCookie` BigSeller và xử lý needLogin ở ViewModel / `ScrapeRunner`.
- Workspace đã có tiền lệ đọc JSON PDP: `Shopee.Core/Scrape/ShopeeApiDataHelper.cs` + `PageCdpHelper.ExtractShopeeApiDataAsync`
  — nhưng JSON đó do **Product Copy của BigSeller** ghi vào `localStorage["shopee-api-data-*"]`. App mới KHÔNG dùng
  nguồn này; nó tự bắt response API PDP. Dùng lại được: `TryExtractShopeeItemId`, `CanonicalProductUrl`, `IsBlockedPageUrl`.
- Tiến độ đang khoá theo `CoordKey(BigsellerId, ShopId, Sheet, CoordOp.Scrape)` (`Shopee.Core/Coordination/ICoordinationHub.cs:8`)
  và `ScrapeProgressStore` (`%AppData%\…\scrape-progress.json`). App mới phải có **khoá riêng** để không đánh dấu nhầm
  dòng Scrape BigSeller là xong (và ngược lại).
- Hub: Postgres qua Npgsql, migration đánh số trong `schema_version` (`server/Shopee.Hub.Web/Data/ProductDb.cs`),
  endpoint sản phẩm ở `Api/ProductApiEndpoints.cs`, auth token client ở `Auth/ApiTokenAuthentication.cs`.
- Vì sao làm được mà không cần API BigSeller: `BigSellerFanOutClient` gửi BigSeller (`clientCrawl.json`) đúng `url` +
  `jsonData` = JSON PDP mà trình duyệt bắt được → BigSeller cũng chỉ dùng dữ liệu trang Shopee tự tải, không có API riêng.

### Kết quả khảo sát JSON thật (2026-11-15, trình duyệt đã đăng nhập Shopee)

Thử 3 SP: 2 áo thun nữ (100017›100099›100352, 16 và 9 model) + 1 lọ bút (100636›100721›101258, 12 model).
- Khi user click vào SP, **trang tự gọi** `GET /api/v4/pdp/get_pc?item_id=…&shop_id=…&display_model_id=…&tz_offset_in_minutes=420&detail_level=0…`
  (thấy 2 lần mỗi SP), trả 200, JSON 62–77 KB. Gọi lại thẳng `fetch('/api/v4/pdp/get_pc?item_id=&shop_id=&tz_offset_in_minutes=420&detail_level=0', {credentials:'include'})`
  từ trong trang cũng trả đủ dữ liệu.
- Hook gắn muộn bị mất khi trang tải lại (Shopee reload sau đăng nhập, `is_from_login=true`) → hook PHẢI là content
  script `document_start`.
- Đường dẫn trường (gốc `data`):

  | Cần cho đăng SP | Đường dẫn | Ghi chú |
  |---|---|---|
  | Tên | `item.title` | |
  | Mô tả | `item.description` (chữ) + `product_description.paragraph_list[]` (`type` 0 = chữ, 10001 = dòng trống, có `img_id` khi là ảnh) | 3 SP thử đều không có ảnh trong mô tả; `item.rich_text_description` = null |
  | Ảnh | `product_images.images[]` (id) → `https://down-vn.img.susercontent.com/file/{id}` | 9 ảnh/SP |
  | Video | `product_images.video.formats[].url`, `.default_format.url`, `.duration` (giây) | **Không** tin `item.item_has_video` (báo false dù có video) |
  | Biến thể | `item.tier_variations[]`: `name`, `options[]`, `images[]` (ảnh từng lựa chọn, chỉ ở tầng 1) | 1 hoặc 2 tầng |
  | Model | `item.models[]`: `model_id`, `name` ("Màu,Size"), `extinfo.tier_index[]`, `price`, `price_before_discount` | giá có thể khác nhau từng model |
  | Giá SP | `item.price_min/max`, `item.price_before_discount`, `product_price.price.single_value` | micro-VND: **chia 100000**; `price` đã gồm khuyến mãi → khi đăng nên lấy `price_before_discount` |
  | Danh mục | `item.categories[]` (`catid`, `display_name`, 3 cấp), `item.cat_id` | `fe_categories` là danh mục hiển thị, không dùng |
  | Thuộc tính | `item.attributes[]`: `id`, `name`, `value`, `val_id` | `val_id = 0` = người bán tự gõ, có thể nhiều giá trị chung một chuỗi |
  | Thương hiệu | `item.brand`, `item.brand_id` | `brand_id = 0` → "No brand" |
  | Bảng size | `item.size_chart` (id ảnh) | có thể null |
  | Khác | `item.condition`, `item.estimated_days`, `item.is_pre_order`, `item.shop_location`, `shop_detailed.{shopid,name}`, `product_review` | |
- **Không có** trong JSON công khai: tồn kho (`stock`/`normal_stock` = null, chỉ có `stock_display = "IN STOCK"`,
  `has_stock`), số đã bán (null), cân nặng, kích thước gói, SKU người bán.
- Chưa gặp: SP **không biến thể** (`tier_variations` rỗng?), SP có ảnh trong mô tả → kiểm ở chốt chặn GĐ2.

## 2. Phạm vi

- **Làm:** extension mới bắt JSON PDP; chế độ Harvest trong engine MultiBrave; hàng đợi cục bộ + đẩy Hub; bảng + API
  trên Hub; exe `Shopee.Harvest`; UI tối thiểu (chạy/dừng/tiến độ/đếm đã lưu – chờ đẩy – lỗi); test đơn vị.
- **Không làm:** bước up sản phẩm lên Shopee; tải ảnh/video; trang Hub web xem sản phẩm (để GĐ sau nếu user muốn);
  sửa app Scrape/Update hiện có ngoài phần tham số hoá cần thiết (hành vi của chúng phải giữ nguyên 100%).

## 3. Các bước thực hiện

Giao `opus-dev` theo từng giai đoạn, mỗi giai đoạn qua nghiệm thu + phản biện rồi mới sang giai đoạn kế.

### GĐ1 — Hub: bảng + API (code ở `suite/server/Shopee.Hub.Web`)

1. Migration mới trong `ProductDb.cs`:
   - `harvested_products`: `item_id bigint PK`, `shop_id bigint`, `url text`, `name text`, `cat_id bigint`,
     `category_ids jsonb`, `brand_id bigint`, `price_min bigint`, `price_max bigint`, `price_before_discount bigint`
     (đơn vị VND, đã chia 100000), `tier_count int`, `model_count int`, `image_urls jsonb`, `video_urls jsonb`,
     `size_chart_url text`, `raw jsonb` (nguyên JSON `get_pc`), `raw_hash text`, `machine_id text`,
     `harvest_count int`, `first_seen_at timestamptz`, `updated_at timestamptz`.
     Không có cột tồn kho/đã bán: JSON công khai luôn trả null (xem khảo sát ở mục 1).
   - `harvested_product_sources`: `account_id text`, `sheet text`, `row_no int`, `item_id bigint`, `harvested_at
     timestamptz`, UNIQUE(`account_id`,`sheet`,`row_no`) — nối dòng của shop đích với sản phẩm.
2. Endpoint (cùng auth token client như `ProductApiEndpoints`):
   - `POST /api/harvested-products/batch` — upsert tối đa 50 bản ghi/lần; idempotent (gửi lại cùng `raw_hash` không
     tăng `harvest_count`, không lỗi). Trả về danh sách `item_id` đã nhận để client xoá khỏi hàng đợi.
   - `GET /api/harvested-products/stats?account=&sheet=` — đếm số dòng đã có dữ liệu.
3. Test trong `server/Shopee.Hub.Web.Tests` cho upsert idempotent, batch quá cỡ bị từ chối, JSON hỏng bị từ chối từng bản ghi
   (không làm hỏng cả batch).

### GĐ2 — Extension `workspace/extensions/shopee-harvest`

1. Tách thư mục riêng (copy phần chung của `shopee-scrape`: background mở link/chờ tải/settle URL, dò verify/captcha,
   `shared/net-detect.js`, `shared/tab-wait.js`, overlay, keepalive SW, `runnerState`). **Bỏ** `content.js` BigSeller.
   Tên extension khác ("Shopee Harvest Runner") để C# nhận diện đúng.
2. Content script `pdp-hook.js` khai trong manifest: `world: "MAIN"`, `run_at: "document_start"`, `matches` các host
   Shopee. Bọc `fetch` + `XMLHttpRequest`, bắt response của `/api/v4/pdp/get_pc` (đã xác nhận trang tự gọi), lưu vào
   `window.__shopeeHarvestPdp[itemId] = {url, at, body}` — chỉ giữ bản có `error == null` và `data.item.item_id` khớp.
   Không sửa request, không chặn response (trang phải chạy y như thường).
3. Hàm RPC `__launcherExecuteHarvestStep(link)` trong background: mở link → chờ trang → nếu verify/captcha/lỗi mạng/không
   phải trang SP thì trả đúng cờ như bước scrape → không thì chờ tối đa 30 s tới khi có PDP khớp `itemId` của link →
   trả `{ok, captcha, proxyError, notProduct, itemId, shopId, pageUrl, json, source}`. Thêm cờ probe `hasHarvestStep`
   để SW cũ cache bị nhận ra.
   - **Dự phòng:** quá 30 s chưa bắt được mà trang SP vẫn bình thường → gọi thẳng **một lần**
     `fetch('/api/v4/pdp/get_pc?item_id=&shop_id=&tz_offset_in_minutes=420&detail_level=0', {credentials:'include'})`
     trong world MAIN, `source = "fetch"`. Không gọi thẳng làm đường chính (dễ bị gắn cờ bot); đếm số lần dùng dự phòng
     vào log để theo dõi.
4. **Chốt chặn GĐ2 (bắt buộc):** chạy thật trên 3 link bằng Brave đã đăng nhập: **1 SP không biến thể** (chưa gặp
   lúc khảo sát), 1 SP 2 tầng biến thể có video, 1 SP có ảnh trong mô tả nếu tìm được. Lưu JSON thu được (đã bỏ
   cookie/token nếu có; khối `account` thay bằng `{}`) làm fixture tại `workspace/tests/Shopee.Core.Tests/Fixtures/pdp/*.json`.
   Nếu Shopee không còn gọi `get_pc` hoặc trả lỗi thì DỪNG và báo phiên chính — không tự nghĩ đường khác.

### GĐ3 — Engine: chế độ Harvest trong `Shopee.Module.MultiBrave`

1. Thêm `RunnerMode { Scrape, Harvest }` vào cấu hình runner (mặc định `Scrape` — app cũ không đổi gì).
2. Khi `Harvest`:
   - gọi `ExecuteHarvestStepAsync` thay `ExecuteScrapeStepAsync`; nạp extension `shopee-harvest` (tham số hoá
     `RunnerExtensionPaths` / tên hiển thị);
   - bỏ: `BigSellerTokenGuard` (auto-login, import cookie, write-back token), nhánh NeedLogin, kiểm tra Product Copy,
     proxy-bypass bigseller, `VideoDownloader`/`CollectVideoCandidatesAsync`, yêu cầu `HasCookie`;
   - thành công = có JSON khớp itemId → gọi callback mới `onProductHarvested(row, url, json)` **trước** khi ghi
     `lastCompletedRow`; callback ném lỗi thì dòng KHÔNG được tính xong;
   - không có JSON sau 30 s (trang SP bình thường) → `AddFailedRow` như lỗi scrape; captcha/proxy/watchdog/xoay tài
     khoản/nghỉ giữa link giữ nguyên logic hiện có.
3. Tiến độ riêng: `CoordOp.Harvest` (Hub ledger + lease phải nhận op này — sửa phía Hub nếu đang whitelist op) và file
   `harvest-progress.json` / `harvest-targets.json` riêng.

### GĐ4 — Lưu trữ phía client (`Shopee.Core/Harvest/`)

1. `ShopeePdpParser` — từ JSON `get_pc` rút các trường theo bảng đường dẫn ở mục 1 (itemId, shopId, tên, cat_id +
   category ids, brand_id, giá min/max/trước giảm đã chia 100000, số tầng biến thể, số model, URL ảnh, URL video lấy từ
   `product_images.video`, URL bảng size). Chịu được: `tier_variations` rỗng/null, `brand` null, `size_chart` null,
   `video` null. Test bằng các fixture GĐ2.
2. `HarvestOutbox` — SQLite (`Microsoft.Data.Sqlite`, theo khuôn `SearchTaskStore`) tại thư mục dữ liệu app:
   `pending(item_id, account_id, sheet, row_no, url, json, raw_hash, created_at, attempts, last_error)`.
   `onProductHarvested` ghi vào đây (đồng bộ, xong mới tính dòng hoàn thành) → **không mất dữ liệu khi Hub sập**.
   Hợp đồng Hub (chốt sau phản biện GĐ1): request mỗi mục có `harvestedAt` (giờ cào, bắt buộc), bộ ba
   `accountId/sheet/rowNo` đủ cả hoặc bỏ cả; response `results[{index, itemId, ok, reason?}]` theo chỉ số mục.
   Request còn có `clientNow` (giờ máy lúc gửi, bắt buộc — **đặt ngay trước lời gọi HTTP**, không phải lúc dựng lô,
   kể cả khi gửi lại) để Hub tự chỉnh lệch đồng hồ; mỗi kết quả có thêm `applied`
   (false = trùng hash hoặc bản cũ hơn, không ghi gì).
   Quy tắc client (chốt sau phản biện GĐ1 vòng 3):
   - `ok=true` → xoá mục (đếm `applied` để thống kê).
   - `ok=false` → đánh dấu lỗi, giữ lại; UI có nút **"Đẩy lại mục lỗi"** đưa các mục này về hàng chờ.
   - 5xx, 503 `pg-not-ready`, 401, timeout, mất mạng → giữ nguyên lô, lùi dần (tối đa 5'), **không đặt trần** số lần.
   - Cùng một lô bị 500 liên tiếp ≥3 lần → **chia đôi** lô để cô lập mục gây lỗi; chỉ khi lô còn 1 mục mới tính trần
     (vd 20 lần) rồi đánh dấu lỗi mục đó.
   - 400 `batch-too-large` hoặc 413 → chia nhỏ lô. 400 khác (`client-now`, body hỏng) là lỗi code client → đánh dấu
     lỗi cả lô, ghi log rõ, không lặp gửi lại.
   - Trước khi tính `raw_hash`: bỏ ký tự `\u0000` và surrogate UTF-16 lẻ (vd emoji bị cắt đôi) khỏi JSON và các trường
     text (Postgres không nhận), và **xoá khối `data.account`** (thông tin tài khoản Shopee đang đăng nhập, không được
     đưa lên Hub).
   - Hub từ chối `error` khác null; khảo sát 2026-11-15 thấy bản thành công có `"error":null` — chốt chặn GĐ2 xác nhận lại.
3. `HarvestOutboxWorker` — mỗi 10 s lấy ≤50 bản ghi đẩy `POST /api/harvested-products/batch`, xoá đúng các item Hub báo
   đã nhận; lỗi mạng thì lùi dần (tối đa 5'), không chặn vòng cào. Đẩy nốt khi đóng app (có giới hạn thời gian).

### GĐ5 — Exe `workspace/src/Shopee.Harvest`

1. Project mới theo khuôn `Shopee.Scrape.csproj` (AssemblyName `ShopeeHarvest`, icon `assets/harvest-icon.ico`, copy
   `extensions/shopee-harvest` vào output), thêm vào `ShopeeWorkspace.sln`.
   - **Icon** (user chốt 2026-11-15): thêm `RenderHarvestIcon` vào `workspace/assets/IconGenerator.cs`, cùng phong cách
     ô bo tròn của `scrape-icon` (cam, kính lúp) và `update-icon` (xanh dương, mũi tên xoay): nền **xanh ngọc lục**
     (gradient kiểu `#34D399 → #10B981 → #047857`), giữa là **trụ cơ sở dữ liệu** màu trắng (3 tầng đĩa) có **mũi tên
     đổ xuống** vào miệng trụ, có bóng đổ như 2 icon kia. Xuất `harvest-icon.png` + `harvest-icon.ico` (đủ cỡ 16–256).
     Phiên chính xem PNG trước khi chốt. Thêm `WorkspaceRole.Harvest => …/harvest-icon.ico` vào `WindowIcon` và nhúng
     icon làm Resource như `Shopee.Scrape.csproj`.
2. `WorkspaceRole.Harvest` + đối số `--harvest` / `--role harvest` trong `WorkspaceAppConfig`; tiêu đề "Shopee Harvest";
   tách profile Brave riêng (như `IsScrapeOnly` → `SetManagedSubRoot`, dùng sub-root khác `profiles`).
3. UI: dùng lại màn Workspace chế độ scrape-only, đổi nhãn (Cào → Lấy dữ liệu), ẩn mọi thứ BigSeller-login/video;
   thêm 3 số: đã lưu Hub / chờ đẩy / lỗi đẩy; cửa sổ thống kê dùng tiến độ Harvest.

## 4. Tiêu chí nghiệm thu

- [ ] `dotnet build workspace/ShopeeWorkspace.sln -c Release` và `dotnet build suite/server/ShopeeHub.sln` xanh; toàn bộ
      test cũ + mới xanh.
- [ ] App Scrape (`ShopeeScrape.exe`) và Update không đổi hành vi: diff ở các file dùng chung chỉ thêm nhánh `Harvest`,
      mặc định đi nhánh cũ (phản biện soi riêng điểm này).
- [ ] Chạy `ShopeeHarvest.exe` với 1 shop × 5 link thật: 5 bản ghi có trên Hub (`raw` đầy đủ, `image_urls` ≥1,
      biến thể đủ `model_count`), `harvested_product_sources` có 5 dòng đúng `row_no`.
- [ ] Tắt mạng tới Hub giữa chừng: dòng vẫn được cào, dồn trong outbox; bật lại thì đẩy hết, không trùng, không mất.
- [ ] Không cần Product Copy, không cần cookie BigSeller vẫn chạy được.
- [ ] Gặp captcha/verify/lỗi proxy: xử lý y như app Scrape (giữ cửa sổ chờ, cách ly tài khoản, dừng job khi key chết).
- [ ] Chạy Harvest không làm thay đổi `scrape-progress.json` hay ledger `scrape` trên Hub.

## 5. Rủi ro & lưu ý

- **Rủi ro lớn nhất:** Shopee đổi cách tải PDP (endpoint/mã hoá) → GĐ2 có chốt chặn chạy thật trước khi làm tiếp.
- Thiếu cho bước đăng SP (ghi nhận cho giai đoạn up, **không** xử lý ở đây): **tồn kho, cân nặng, kích thước gói**
  (Shopee bắt buộc) → cần giá trị mặc định hoặc cấu hình theo danh mục; **SKU** phải tự sinh; **thuộc tính tự gõ**
  (`val_id = 0`) phải đối chiếu lại với danh sách thuộc tính chuẩn của danh mục hoặc bỏ; `brand_id = 0` → "No brand".
- `harvested_product_sources` khoá theo (account, sheet, row_no) nhưng KHÔNG đi theo khi `product_rows` bị import
  `replace` hoặc xoá dòng (phản biện GĐ1, nghi ngờ có cơ chế chắc) → bước up phải đối chiếu `url` của dòng nguồn với
  link hiện tại của dòng trước khi dùng; `stats.rows` có thể đếm cả dòng cũ.
- Giá trong JSON là giá đang khuyến mãi; giữ cả `price` và `price_before_discount` từng model trong `raw` để bước up chọn.
- URL ảnh/video CDN có thể hết hạn (video hay hơn ảnh) — đã chấp nhận theo lựa chọn "chỉ lưu URL".
- `raw jsonb` mỗi SP ~60–80 KB (đo thực tế 3 SP) → 100.000 SP ≈ 7 GB trước nén TOAST; có `raw_hash` để không ghi lại khi không đổi.
- Sửa Hub phải deploy lên VPS (bước sudo cần mật khẩu — hỏi user lúc deploy). Trước khi deploy: kiểm
  `SELECT * FROM schema_version` trên Postgres VPS chưa có bản 4 (bản nháp cũ chưa có cột `harvested_at` → mọi lô 500),
  và giới hạn body của proxy trước Hub đủ cho lô ~4–9 MB.
- Để ngỏ: có bỏ qua link mà Hub đã có dữ liệu mới (vd < 7 ngày) không? Mặc định plan: **luôn cào lại và upsert**.

---

## Báo cáo thực thi

### GĐ1 — Hub (2026-11-15): XONG, chưa commit, chưa deploy

- Migration 4 (`ProductDb.cs`): `harvested_products` (thêm `harvested_at`), `harvested_product_sources` (+ `url`,
  `harvested_at`, UNIQUE account/sheet/row, index item_id).
- `ProductDb.Harvest.cs`: validate + upsert từng mục 1 tx + stats. Endpoint trong `ProductApiEndpoints.cs`; route + DTO
  ở `suite/suite/Shopee.Core/Coordination/{HubRoutes,HarvestedProductDtos}.cs` (GĐ4 phải chép sang
  `workspace/src/Shopee.Core`).
- Hợp đồng cuối: request `{machineId, clientNow, items[…harvestedAt…]}`; response `results[{index,itemId,ok,applied,reason}]`;
  lỗi mục chỉ lớp SQLSTATE 22/23/54, còn lại 500; mốc cào quy về giờ Hub và kẹp ≤ giờ Hub; bản cũ / trùng hash không ghi.
- 4 vòng phản biện (lỗi chính đã sửa: xoá nhầm theo itemId, lô độc kẹt vĩnh viễn, JSON lỗi Shopee ghi đè bản tốt, bản
  cũ ghi đè bản mới, lệch đồng hồ máy). Test Hub 307/307 (35 test Harvest chạy Postgres thật).
