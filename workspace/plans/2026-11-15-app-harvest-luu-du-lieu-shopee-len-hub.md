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
- Chưa gặp: SP có ảnh trong mô tả.
- **Chạy thật bằng extension (2026-11-15, user, Brave đã đăng nhập)** — fixture ở
  `workspace/tests/Shopee.Core.Tests/Fixtures/pdp/` (đã cắt `data.account` + `delivery_address`):
  - `pdp-khong-bien-the.json`: SP **không biến thể** có `tier_variations = [{"name":"","options":[""]}]` (KHÔNG rỗng),
    1 model `name:""`, `tier_index:[0]`, model `price_before_discount = 0` (0 = không giảm → dùng `price`).
    `size_chart = ""` (chuỗi rỗng, không phải null) nhưng có `size_chart_info` dạng bảng (cột Size/Height/Weight).
  - `pdp-1tang-70model-4capdm.json`: danh mục **4 cấp** (không cố định 3), 1 tầng biến thể **70 lựa chọn / 70 model**
    (bước up phải kiểm giới hạn số lựa chọn của Shopee), `rich_text_description` có giá trị.
  - `pdp-danurl-1tang-6model-anhsg.json` (bản extension sau sửa): **mở bằng cách dán URL** → hook vẫn bắt được
    (rủi ro "chỉ bắt khi click" đã loại). Ảnh có tiền tố `sg-11134201-…` lẫn `vn-11134207-…` trong cùng SP — đã thử
    `https://down-vn.img.susercontent.com/file/{id}` với cả id `sg-` lẫn `vn-` đều trả 200 image/jpeg. Danh mục 4 cấp; `item_has_video:false`, không video.
  - `pdp-video-1tang-11model-2capdm.json`: có **video** (`product_images.video`, 35 s, chỉ 1 format `V720P` +
    `default_format.url` dạng `mms.vod.susercontent.com/…default.mp4`); danh mục chỉ **2 cấp**; 17 thuộc tính;
    `brand` null dù tiêu đề ghi thương hiệu.
  - `pdp-2tang-8model.json`: **2 tầng** "Màu sắc" (4, có ảnh) × "Hộp" (2, `images: null`) = 8 model; model `name` =
    "Màu,Hộp", `tier_index` = [i,j]; có video. → ảnh lựa chọn chỉ ở tầng 1; tầng 2 `images` null.
  - `pdp-motaanh-brand-voucher.json` (extension vòng 5, Shopee Mall): **mô tả có ảnh** — đoạn
    `{"type":1,"img_id":"sg-11134202-…","ratio":1}` xen giữa đoạn chữ `type:0` (có cả trong `item.rich_text_description`
    lẫn `data.product_description`); `item.description` (chữ thuần) KHÔNG chứa ảnh → bước up phải dựng mô tả từ
    `paragraph_list`. Có `brand`/`brand_id` thật. Có `data.shop_vouchers[]` kèm `signature`, `customer_reference_id`.
  - **Trường phụ thuộc người xem / thời điểm** (làm `raw_hash` đổi liên tục, không cần cho đăng SP): `data.shop_vouchers`,
    `data.top_banner`, `data.coin_info`, `data.product_review.liked`, `data.product_price.final_price_info`,
    `data.product_price.final_price_vouchers`, `data.price_breakdown`, `data.button_group`, toàn bộ kênh/phí/ngày giao trong
    `data.product_shipping` (trừ `shipping_fee_info.ship_from_location`). GĐ4 bỏ các khối này trước khi tính hash.
  - **Riêng tư:** ngoài `data.account`, JSON còn `data.product_shipping.delivery_address` = địa chỉ + toạ độ của tài
    khoản đang đăng nhập → phải cắt trước khi tính hash / đẩy Hub / lưu fixture.

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
     *(Ghi chú: đã đổi khoá sang (`account_id`,`sheet`,`item_id`) ở migration 5 — xem GĐ5.5.)*
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
   kho ở content script ISOLATED (xem "Hook ít dấu vết" bên dưới) — chỉ giữ bản có `error` null/vắng/0 và
   `data.item.item_id` khớp; bản có mã lỗi khác thì ghi lại mã lỗi theo itemId.
   Không sửa request, không chặn response (trang phải chạy y như thường).
3. Hàm RPC `__launcherExecuteHarvestStep(link)` trong background: mở link → chờ trang → nếu verify/captcha/lỗi mạng/không
   phải trang SP thì trả đúng cờ như bước scrape → không thì chờ tối đa 30 s tới khi có PDP khớp `itemId` của link →
   trả `{ok, captcha, proxyError, notProduct, itemId, shopId, pageUrl, json, source}`. Thêm cờ probe `hasHarvestStep`
   để SW cũ cache bị nhận ra.
   - **Dự phòng (đổi sau phản biện GĐ2):** quá 30 s chưa bắt được, trang SP vẫn bình thường và Shopee không từ chối →
     `chrome.tabs.reload` **một lần** để trang tự gọi lại get_pc với đủ chữ ký anti-bot, chờ thêm ≤30 s,
     `source = "hook-reload"`. **Không** gọi thẳng get_pc (thiếu header ký, dễ bị gắn cờ). Shopee trả `error` cho
     get_pc → không dự phòng; mã thuộc `blockErrorCodes` (mặc định `[90309999]`, payload đè được) → `trafficError` +
     `blocked`; mã khác → lỗi dòng kèm `pdpError`. `"error":0` coi là thành công (Hub cũng nhận).
   - Hook ít dấu vết: bọc bằng `Proxy`, store giữ ở content script world ISOLATED (không đặt lên `window` của trang).
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
   - Lưu ý từ GĐ2: probe đang cứng `hasScrapeStep` (`RunnerExtensionRpc.cs:392,524`, `RunnerSwLifecycle.cs:503`) và tên
     "Shopee Data Runner" (`BraveProfileManager.cs:207`) → tham số hoá. Payload bước harvest **không** để extension tự ghi
     `lastCompletedRow` (C# ghi sau khi outbox lưu xong). Bước harvest chạy > 6 s nên **không được** rơi xuống đường
     popup 6 s của RPC (`RunnerExtensionRpc.cs:381`) rồi bị gọi lại chồng. Bỏ `PageCdpHelper.ClearShopeeApiDataAsync`
     trước mỗi bước. Kết quả bước có `trafficError`/`blocked` khi Shopee chặn get_pc → dừng tài khoản như ca traffic error;
     **cầu dao**: N dòng liên tiếp (vd 3) cùng có `pdpError` (mã chưa biết) → xử như traffic (xoay IP/tài khoản), để
     mã chặn mới không đốt cả sheet thành dòng lỗi; `superseded` → bỏ qua kết quả, không ghi lỗi dòng;
     regex itemId của C# (`ShopeeApiDataHelper.TryExtractShopeeItemId`) chưa neo `-i.` → dùng bản đã neo;
     extension neo itemId cả đầu lẫn đuôi `(?:^|[/\-])i\.(\d+)\.(\d+)(?=$|[?#&/])` → link trong sổ dính đuôi lạ
     (vd `…i.1.2%7DD`) sẽ ra `notProduct`: C# **phải làm sạch link** (`ShopeeApiDataHelper.CanonicalProductUrl`)
     trước khi gửi bước; `superseded`/`aborted` không bao giờ tính là dòng đã xong;
     log/cảnh báo khi tỉ lệ `source = "hook-reload"` cao.
   - Harvest dùng chung khoá `runnerState`/`lastCompletedRow` với Scrape và `ExtensionProgressReader` chọn thư mục
     extension ghi gần nhất trong profile → **bắt buộc** profile Brave riêng cho Harvest (GĐ5) hoặc đổi khoá.
3. Tiến độ riêng: `CoordOp.Harvest` (Hub ledger + lease phải nhận op này — sửa phía Hub nếu đang whitelist op) và file
   `harvest-progress.json` / `harvest-targets.json` riêng.

### GĐ4 — Lưu trữ phía client (`Shopee.Core/Harvest/`)

1. `ShopeePdpParser` — từ JSON `get_pc` rút các trường theo bảng đường dẫn ở mục 1 (itemId, shopId, tên, cat_id +
   category ids, brand_id, giá min/max/trước giảm đã chia 100000, số tầng biến thể, số model, URL ảnh, URL video lấy từ
   `product_images.video`, URL bảng size). Chịu được: `tier_variations` rỗng/null **hoặc dạng SP không biến thể
   `[{"name":"","options":[""]}]` (→ `tier_count = 0`)**, `brand` null, `size_chart` null **hoặc ""**, `video` null,
   model `price_before_discount = 0` (→ lấy `price`), danh mục 3–4+ cấp, **`item.attributes = null`**
   (`pdp-1tang-4model-1anh.json`), `rich_text_description` null hoặc có giá trị. Test bằng các fixture GĐ2.
   **Lưu ý từ GĐ3:** cài `IHarvestSink` (`workspace/src/Shopee.Core/Harvest/IHarvestSink.cs`); `SaveAsync` trả về bình
   thường = đã ghi bền, ném = chưa ghi (engine dừng job, không bỏ dòng); **phải có giới hạn thời gian** (pha `saving`
   không được watchdog canh); **idempotent theo (account, sheet, row)** — engine có thể gửi lại cùng dòng sau khi
   Brave/process chết giữa lúc lưu và ghi tiến độ. `HarvestedItem.SourceLink` (link gốc) → `harvested_product_sources.url`.
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
   - MỌI phản hồi khác 200 (5xx kể cả 500, 503 `pg-not-ready`/`pg-unavailable`, 401, timeout, mất mạng) → giữ nguyên
     lô, **lùi dần CHUNG** cho cả worker (tối đa 5'), **không đặt trần** số lần; 200 thì reset.
   - (Chốt sau phản biện GĐ4 vòng 2 — bỏ cơ chế chia đôi/ghim vì gây kẹt vĩnh viễn.) Lô nhiều mục bị 500 → mọi mục
     thành **nghi vấn**; chọn lô **xen kẽ**: sau một lô thường gửi MỘT mục nghi vấn một mình (xoay vòng), lô thường
     không bao giờ bị nghi vấn chặn. Mục nghi vấn gửi lẻ bị 500 chỉ được đếm khi có một 200 xảy ra **sau** lần 500 gần
     nhất của chính nó (không thì có thể Hub đang sập); đủ 5 lần có bằng chứng → `failed`. Mục độc nằm một mình
     trong hàng thì không bao giờ bị `failed`, chỉ lùi dần (chấp nhận).
   - 400 `batch-too-large` hoặc 413 → chia nhỏ lô. 400 khác (`client-now`, body hỏng) là lỗi code client → đánh dấu
     lỗi cả lô, ghi log rõ, không lặp gửi lại.
   - Trước khi tính `raw_hash`: bỏ ký tự `\u0000` và surrogate UTF-16 lẻ (vd emoji bị cắt đôi) khỏi JSON và các trường
     text (Postgres không nhận), **xoá khối `data.account`** và **`data.product_shipping.delivery_address`** (thông tin
     + địa chỉ tài khoản Shopee đang đăng nhập, không được đưa lên Hub; extension đã cắt sẵn, C# cắt thêm một lớp);
     đồng thời bỏ các khối phụ thuộc người xem/thời điểm (danh sách ở mục 1 "Trường phụ thuộc người xem") để cùng một
     SP cào lại không đổi `raw_hash` vô cớ.
   - Hub từ chối `error` khác null và khác số 0 (`"error":0` = thành công); 6 fixture thật đều có `"error":null`.
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
2. `WorkspaceRole.Harvest` (chỉ vào được qua `ShopeeHarvest.exe`; đối số `--harvest` / `--role harvest` trên
   `ShopeeWorkspace.exe` đã bỏ sau phản biện vì bản cài không gói extension harvest); tiêu đề "Shopee Harvest";
   tách profile Brave riêng (như `IsScrapeOnly` → `SetManagedSubRoot`, dùng sub-root khác `profiles`).
3. UI: dùng lại màn Workspace chế độ scrape-only, đổi nhãn (Cào → Lấy dữ liệu), ẩn mọi thứ BigSeller-login/video;
   thêm 3 số: đã lưu Hub / chờ đẩy / lỗi đẩy; cửa sổ thống kê dùng tiến độ Harvest.
4. **Lưu ý từ GĐ3** (cách dựng runner, xem "Báo cáo thực thi GĐ3"): gọi `MultiBraveRuntime.SetMode(RunnerMode.Harvest)`
   lúc khởi động process (exe riêng — một process không chạy được cả hai chế độ); `new ScrapeRunner(..., mode:
   RunnerMode.Harvest) { Harvest = new HarvestRunOptions(sink, shopId) }`; dùng `ScrapeProgressStore.Harvest`,
   `ScrapeTargetConfigStore.Harvest`, `CoordKey(..., CoordOp.Harvest)`; bỏ kiểm `HasCookie`, `BigSellerNeedLogin`,
   `ProductScraped`, video trong VM; `HttpCoordinationHub` hiện chỉ fold ledger `scrape` → cần fold riêng cho harvest
   nếu muốn tiếp quản xuyên máy; **nối báo "fatal" của job harvest lên Hub** (`TakeJobFatal`/`AssignmentWorker` hiện
   chỉ xử op `scrape`; Hub `OpFamily` chưa có `harvest`) — nếu không, job dừng vì sink lỗi không hiện "failed" trên Hub.
   **Chặn chạy 2 bản `ShopeeHarvest.exe` cùng máy** (dùng chung `harvest-outbox.db` → 2 worker đẩy trùng). Khởi tạo
   outbox/worker theo "Báo cáo thực thi GĐ4"; `Nudge()` sau mỗi lần lưu; khi đóng app `StopAsync` + `FlushAsync`.
5. **Tiến độ Harvest theo itemId đã lấy xong** (user chốt 2026-11-17, thay cơ chế dấu/vân tay workbook — bỏ hẳn
   `SharedProgressGuard`, mọi stamp; `ScrapeProgressStore.cs` về đúng HEAD):
   - **Tập đã xong**: bảng `done (target, item_key, saved_at)` trong `harvest-outbox.db`, ghi CÙNG giao dịch SQLite với lần
     lưu SP vào outbox (lưu vào outbox = thành công, không chờ Hub). `target` = `HarvestDoneTarget.Of(tk, khoá tiến độ)`
     (khoá tiến độ không phân biệt hoa thường). Khoá SP (`HarvestItemKey`): `id:{shop}.{item}` tách từ link gốc của dòng
     (`-i.{shop}.{item}` lấy cặp cuối, `/product/{shop}/{item}`; mọi query/fragment/www/http(s)/hoa thường); không tách
     được → `url:{host thường}{path bỏ / cuối}`; không phải URL → `txt:…`. JSON trả id khác link → ghi thêm `id:` từ JSON.
     Engine truyền target qua `HarvestRunOptions.DoneTarget` → `HarvestedItem.DoneTarget`.
   - **Dựng lại mỗi lần Chạy / Tiếp tục** (hai nút như nhau ở Harvest, Chạy KHÔNG xoá tập): đọc workbook hiện tại MỘT lần
     (`HarvestWorkbookReader.ReadRangeAsync`, trong dải Từ/Đến dòng của `HarvestRunConfigStore`) → hàm thuần
     `HarvestDoneRebuild.Build(dải, dòng hợp lệ, tập)`: dòng có khoá thuộc tập → xong; dòng thiếu tên / thiếu link → xong
     (engine cũng không có gì để lấy); còn lại → các đoạn phải chạy. Ghi đè `harvest-progress.json` (BeginFresh +
     MarkCompleted) rồi chạy đúng các đoạn còn lại. Workbook chèn / xoá / đổi thứ tự / gộp lại / đổi nguồn, đổi giữa lượt,
     tiến độ fold từ ledger Hub: không cần phát hiện — lượt sau dựng lại từ tập.
   - **Reset tiến độ**: mọi nút reset (Reset tất cả shop, menu "xoá tiến độ" của shop, Thống kê → Xoá tiến độ, Auto Run)
     đi chung `ScrapeProgressReset.ClearKey` → ở Harvest xoá CẢ tập của đúng target (`HarvestOutbox.ClearDone`); app Scrape
     y như `Clear` cũ. Việc Hub huỷ / nút "Hủy việc dở" chỉ xoá tiến độ dở, KHÔNG xoá tập (SP đã lấy vẫn là đã lấy).
   - **"Đánh dấu đã xong"** tắt ở Harvest (lượt sau dựng lại sẽ ghi đè; ghi khoá vào tập mà không có dữ liệu SP là sai nghĩa).
   - **Resolve của Harvest** (`HarvestWorkbookResolve`, đăng ký job): như `SharedWorkbookCatalog.ResolveForUpdate` khi đọc
     được, nhưng file nguồn đã chọn mà vắng / khoá độc quyền / app Update đang ghi / đọc lỗi → DỪNG job với lý do rõ, KHÔNG
     rơi sang file gộp (dòng mọi shop dưới khoá một shop). Chỉ dùng file gộp khi đọc được mà không có sheet khớp shop.
   - **Workbook đang bị app Update ghi** (`<file>.lock`): đọc chờ bằng `Task.Delay(…, ct)` (huỷ được) tới
     `HarvestWorkbookReader.LockWait` (2 phút); quá hạn → `HarvestWorkbookLockedException` (tiền tố "Workbook đang bận: ").
     Đầu lượt: báo rõ "app Update đang ghi file workbook — thử lại sau", không mở Brave. Trong khối: `ScrapeFailurePolicy`
     ra `StopJob` (nhả tk nguyên vẹn, không cooldown, không tính kẹt, không bỏ dòng).
   - Harvest vẫn chỉ đọc workbook chung (`SharedWorkbookPolicy`); `SharedWorkbookStore.ReloadIfExternalChange` giữ để
     Harvest thấy nguồn / bản gộp app Scrape vừa đổi (trước khi resolve job, mỗi nhịp 4 s, khi mở tab Cấu hình BigSeller).
   - **Khoá theo SP, không theo vị trí dòng** (phản biện vòng 12): workbook dời dòng mà Hub đang sập thì SP B lưu vào đúng số
     dòng của SP A đè mất A (A đã vào `done` → không bao giờ lên Hub).
     - Outbox schema v2 (`PRAGMA user_version = 2`): `pending` UNIQUE (`account_id`,`sheet`,`item_id`), `row_no` = dòng thấy
       gần nhất (thông tin). Migration v1 → v2 dựng lại bảng trong MỘT giao dịch, giữ `id` / `version` / trạng thái / đếm thử,
       trùng SP thì giữ bản `harvested_at` mới nhất. `done` chỉ ghi khi giao dịch thấy pending đang giữ một bản của chính SP.
     - Hub migration 5 (KHÔNG sửa migration 4): gộp trùng (`account_id`,`sheet`,`item_id`) giữ bản `harvested_at` mới nhất,
       bỏ UNIQUE theo `row_no`, thêm `ux_hps_acct_sheet_item` + index (`account_id`,`sheet`,`row_no`). Upsert liên kết nguồn
       theo SP (cập nhật `row_no`/`url` khi không cũ hơn); `stats` đếm liên kết theo SP.
   - **Khoá `url:`** (khi link không tách được id): GIỮ query (tham số sắp theo tên, bỏ tham số theo dõi `sp_atk`, `xptdk`,
     `utm_*`, `af_*`, `gclid`, `fbclid`, …). Trước đó tách id cả trong query: cặp `shopid`/`itemid`, link lồng đã URL-decode
     (tới 5 lớp — `s.shopee.vn/an_redir?origin_link=…`, `universal-link?redir=…`).
   - **Lỗi vĩnh viễn vs lỗi tạm** (`HarvestStepPolicy.IsPermanentRowFailure` + chỗ lưu):
     - VĨNH VIỄN → bảng `failed (target, item_key, reason, at)` trong DB outbox (`IHarvestFailureSink`, truyền qua
       `HarvestRunOptions.FailureSink`): `notProduct`; mã `pdpError` đã được cầu dao xác nhận SP chết (tái diễn ngay sau khi
       đổi IP); JSON bị từ chối vì nội dung (`HarvestItemRejectedException`).
     - TẠM → không ghi gì: hết giờ, thiếu JSON, lỗi RPC/CDP, `pdpError` chưa xác nhận, captcha, traffic, proxy, lỗi chỗ lưu
       (đĩa/SQLite), khoá workbook. Không chắc thì coi là tạm.
     - Dựng lại: khoá trong `failed` (chưa có trong `done`) không lên lịch, vào sổ bỏ qua của kho tiến độ (Thống kê hiện "bỏ N
       dòng"). Lưu thành công thì gỡ khỏi `failed`. "Cào lại dòng đã bỏ" (Thống kê) xoá `failed` của target; Reset xoá cả
       `done` lẫn `failed`. Hub từ chối mục lúc đẩy: mục nằm ở trạng thái lỗi đẩy của outbox ("Đẩy lại mục lỗi"), khoá vẫn ở `done`.
   - **Chống biến lỗi tạm thành bỏ sót vĩnh viễn** (phản biện vòng 13):
     - Tài khoản bị đăng xuất: extension nhận trang `/buyer/login|signup|register`, `/user/login`, `accounts.shopee.*` →
       cờ `loginRequired` (KHÔNG `notProduct`). Engine: `HarvestStepAction.LoginRequired` → dừng khối, dòng không tính lỗi,
       phase "error" → `ScrapeFailurePolicy` cho tài khoản nghỉ (Cooldown) như lỗi tạm của tk; job vẫn chạy bằng tk khác.
     - Cầu dao notProduct: `NotProductBreakerThreshold` = 3 dòng `notProduct` LIÊN TIẾP trong khối → lỗi phiên/tài khoản: dừng
       khối (Cooldown), cả chuỗi KHÔNG vào sổ lỗi / `failed`. Chuỗi bị cắt (dòng khác có kết quả / hết khối) mới ghi.
     - Kiểm chứng phiên khi cầu dao nhảy (link chết thật đứng liền nhau — shop nguồn bị khoá — không được làm khối dừng ở
       đúng chỗ mãi): mở LINK MẪU trên cùng tài khoản / Brave (`IHarvestRowHost.ProbeSampleAsync` = cùng bước harvest của
       extension, overlay "Đang kiểm tra phiên…", không qua bộ xử lý dòng → không tính tiến độ / số đếm / không lưu). Mẫu theo
       thứ tự: link đã lưu được gần nhất trong khối → `HarvestRunOptions.SampleLink` (outbox `LastSavedLink()`: bảng `meta`,
       khoá `sample_link` = link đã làm sạch của SP lưu được gần nhất, ghi cùng giao dịch lưu). Mẫu lấy được JSON
       (`HarvestStepPolicy.SampleProvesSession`) → chuỗi là lỗi thật: xả (sổ lỗi + `failed` theo luật 2 lượt), chạy tiếp, đếm
       lại từ 0. Mẫu `notProduct` / `loginRequired` / captcha / lỗi, hoặc không có mẫu → dừng khối (Cooldown), không ghi gì.
     - Không lặp tại chỗ (phản biện vòng 15 — cụm ≥ 12 link chết làm khối dừng đúng một chỗ ở mọi lần vá):
       - kiểm chứng THÀNH CÔNG → xả chuỗi + ĐÓNG chuỗi (`LastCompletedRow` / `NextRunRow` tiến tới dòng đó — `ScrapeRunner` vá
         từ `LastCompletedRow + 1`). Phần còn lại của CHÍNH chuỗi đó được GIỮ CHỜ (không ghi, không tiến `LastCompletedRow`), cứ
         đủ `HarvestStepPolicy.ProvenChainRecheckEvery` = 10 dòng thì kiểm chứng lại (phản biện vòng 16: phiên hỏng giữa chuỗi mà
         Shopee không đẩy về trang đăng nhập): mẫu sống → xả nhóm vào `failed` + đóng chuỗi; mẫu hỏng / captcha → nhánh dừng
         hiện có (nhóm vào sổ bỏ qua, KHÔNG vào `failed`). Nhóm còn giữ chờ được xả khi chuỗi bị cắt bởi kết quả dòng khác hoặc
         hết khối; khối dừng vì lý do khác thì nhóm đó làm lại (chưa tiến `LastCompletedRow`). Chọn phương án "giữ chờ" (không
         phải "ghi ngay rồi xoá theo RunId"): dòng chưa có lần kiểm nào phía sau bảo lãnh không bao giờ vào `failed`.
         Chuỗi mới (sau kết quả dòng khác) kiểm từ đầu. Cụm 20 SP chết = 2 lần mở mẫu, 30 = 3.
       - KHÔNG CÓ link mẫu (máy mới, khối chưa lưu được dòng nào): không chứng minh được phiên hỏng → KHÔNG dừng khối, không đụng
         tài khoản. Chuỗi tính là đã duyệt (tiến `LastCompletedRow`, KHÔNG ghi `failed` → lượt sau thử lại), log MỘT lần mỗi chuỗi
         "chưa có link mẫu để kiểm phiên — N dòng không phải trang SP sẽ thử lại lượt sau"; phần còn lại của chuỗi bỏ qua lặng
         lẽ. Khối lưu được một dòng thì link đó thành mẫu cho các chuỗi sau. Mất đăng nhập rõ ràng vẫn bắt qua `loginRequired`.
         Cụm 20 dòng chết trên máy mới: 1 lượt, 0 tài khoản bị loại, 0 lần mở mẫu.
       - kiểm chứng KHÔNG thành công (CÓ mẫu mà mẫu hỏng) / mẫu captcha → dừng khối (tài khoản nghỉ / cách ly như cũ) nhưng chuỗi
         TÍNH LÀ ĐÃ DUYỆT (tiến `LastCompletedRow`, KHÔNG ghi `failed`, KHÔNG có trong `done` → lượt sau thử lại): lần vá bằng
         tài khoản khác đi tiếp từ sau chuỗi, mỗi chuỗi tối đa MỘT tài khoản nghỉ. Đánh đổi: nếu phiên hỏng thật thì ≤ N dòng
         của chuỗi bị bỏ ở lượt này (lấy lại lượt sau) — đổi lấy việc không đốt tài khoản / phiên Brave vô hạn.
       - Dòng "đã duyệt" mà CHƯA kết luận (chuỗi dừng khối, chuỗi không có mẫu — kể cả phần còn lại của chuỗi đó) và các dòng
         pdpError đang chờ bị `AdvanceTo` vượt qua → vào SỔ BỎ QUA của lượt (`Book.RowFailed`, lý do "chưa kết luận (không kiểm
         chứng được phiên) — thử lại lượt sau"; pdpError xả bằng `FlushPendingPdpRows` thay cho `Discard`). KHÔNG vào `failed`,
         KHÔNG vào `done`. Tổng kết cuối job hiện "⚠ BỎ QUA n dòng (chưa lấy được)…", không báo hoàn thành trọn vẹn sai.
       - bỏ chặn trên số lần kiểm (`MaxSessionChecksPerBlock`): lần không thành công dừng khối ngay; lần thành công chỉ có sau
         một kết quả dòng khác chen vào → ≤ số dòng / (N + 1).
       - lần mở LẠI mẫu sau khi giải captcha cũng bắt lỗi + `SafeForClassify` như lần đầu.
     - Xác nhận 2 lượt: outbox v3 — `failed` thêm `hits` + `last_run_id` (v2→v3 `ALTER TABLE ADD COLUMN` nếu bảng đã có;
       khoá cũ hits = 1). Mỗi lượt chạy (`HarvestRunOptions.RunId`, một Guid / job) chỉ tăng 1; dựng lại chỉ bỏ qua khoá có
       `hits ≥ 2` (`HarvestOutbox.FailedConfirmRuns`). Áp dụng mọi loại lỗi vĩnh viễn.
     - Cầu dao pdpError: mã đã xác nhận "SP chết" GIỮ tới hết phiên như GĐ3 (vòng 13 từng xoá khi có dòng lưu được — đảo lại
       ở vòng 14: quên đi thì mỗi cụm SP chết lại bật cầu dao → đổi IP rồi đổi tài khoản vô ích; ghi nhầm vĩnh viễn đã có luật
       2 lượt chặn).
     - Vòng dòng (`HarvestRunnerLoop.RunRowsAsync`, tách khỏi `RunAsync` qua `IHarvestLoopIo` để test cả khối bằng IO giả):
       bước "kiểm trước link kế" CHỈ chạy sau dòng lưu được — dòng không lấy được để tab ở trang không phải SP, extension chờ
       30 s rồi báo "URL hiện tại không phải SP" → khối dừng oan, tài khoản bị loại, dòng chết không vào `failed`. Link kế tự
       điều hướng; bước harvest kế tự dò traffic / captcha. Mọi lối dừng sớm bỏ chuỗi notProduct đang chờ; hết khối thì xả.
     - Duyệt hết khối → `LastCompletedRow = endRow` (kể cả dòng cuối lỗi): `ScrapeRunner` không coi khối là dở rồi vá đúng
       dòng lỗi tới khi kẹt 3 lần. Không mất dòng: tiến độ Harvest dựng lại từ `done` mỗi lượt, dòng lỗi tạm không có trong đó.
     - Thông điệp dừng đi qua `Classify` KHÔNG chèn tên tài khoản (ghi ở log riêng); `ex.Message` ghép vào thì qua
       `HarvestStepPolicy.SafeForClassify` (bỏ chữ captcha / verify / proxy). Link mẫu gặp captcha: y như dòng thường — chờ giải
       tay một lần, giải được thì mở lại mẫu, không thì dừng như captcha (cách ly tài khoản).
     - Khoá theo SP thật: `ForSaved` chỉ ghi khoá `id:` suy từ link khi khớp item (và shop nếu JSON có) trong JSON; lệch → khoá
       `url:` của link + khoá `id:` từ JSON. Tách id trong query CHỈ trên cặp `shopid`/`itemid` ngay ở query hoặc giá trị tham
       số trông như URL (`http(s)://`, `//`) sau decode — `search?keyword=ao-i.5.6`, `shop?shopid=7&redir=x%26itemid%3D5` ra
       `url:`.
     - Migration v1→v2 gộp trùng: bản `pending` thắng bản `failed`, rồi mới tới `harvested_at`, rồi id.
     - Log dòng bỏ qua ở Harvest chỉ "lượt sau tự thử lại / Cào lại dòng đã bỏ", không khuyên "Chạy (reset)".
   - **API mới của kho tiến độ** (chỉ THÊM, app Scrape không gọi): `ScrapeProgressStore.ReplaceProgress(acc, sheet, name,
     totalRows, completed, skippedRows)` — ghi đè vùng phủ + sổ bỏ qua bằng một lượt sắp-gộp, MỘT lần ghi đĩa, MỘT lần
     `Changed` (20 000 dòng xen kẽ: ~10 ms thay cho ~23 s).
   - Huỷ trong lúc chờ khoá `.lock` đầu lượt (trước khi dựng lại) không đụng trạng thái tiến độ; `StartJob` bị resolve chặt
     từ chối thì log đúng lý do (không còn "đang chạy rồi").

### GĐ6 — Harvest theo file Excel, bỏ BigSeller, bỏ qua SP đã có trên Hub

User chốt (2026-11-17): Harvest chỉ cần **danh sách link**, không có khái niệm shop hay BigSeller (chọn shop để đăng là việc
của app đăng SP). File Excel do Harvest **tự chọn** (một sheet dùng chung, hoặc mỗi shop một sheet); Hub nhận "shop" = **tên
sheet**. SP đã có dữ liệu trên Hub (lấy từ file/sheet/máy khác) thì **bỏ qua, không mở trang**; bản trên Hub cũ hơn **N ngày**
(mặc định 30, đặt được) thì lấy lại. Thay cho mục "Để ngỏ" ở mục 5 (luôn cào lại).

1. **Giao diện** (chỉ app Harvest; Scrape / Update không đổi):
   - Shell Harvest 3 tab: **Lấy dữ liệu** | Tài khoản & Proxy | Cài đặt. Không dựng màn Workspace / Cấu hình BigSeller / Update /
     Auto Run / Scrape Multi; thanh trạng thái ẩn cụm "N tài khoản BigSeller · M shop". Không còn chữ "BigSeller" ở chỗ user
     Harvest nhìn thấy (tab, nhãn, tooltip, hộp thoại, log, overlay — overlay ghi "Sheet: …" thay "Bigseller Account: …").
   - Màn **Lấy dữ liệu** (view/VM riêng `Modules/Harvest/HarvestView*` + `HarvestViewModel`, không nhồi vào `WorkspaceView`): nút
     "Chọn file Excel" + đường dẫn (nhớ qua lần mở sau, `harvest\harvest-settings.json`); "Đọc lại"; bảng sheet (ô tick — mặc
     định tick hết, sheet bỏ tick được nhớ —, Tổng link, Đã có, Còn phải lấy, Bỏ qua/lỗi, Lượt này); Chạy / Tiếp tục, Dừng, Reset…
     (có hỏi), Cào lại dòng đã bỏ; 3 số Hub + "Đẩy lại mục lỗi"; "Lấy lại nếu dữ liệu cũ hơn N ngày"; số cửa sổ, dòng mỗi lần,
     khung tk, nghỉ giữa link. Dưới là lưới tiến trình + log (bấm dòng tiến trình → đưa Brave lên trước). Overlay từng dòng /
     tổng kết khối giữ như GĐ5.
   - "Đã có" = SP lấy trên máy này còn hạn + SP Hub báo đã có còn hạn. Hub không trả lời → ghi chú "chỉ tính SP trên máy này".
   - Định dạng file (luật riêng của `HarvestWorkbookReader`; app Scrape vẫn dùng `ScrapeWorkbook` — link A, tiêu đề dòng 1, bắt
     buộc tên SP cột F): **chỉ cần một cột link SP Shopee**. Cột link = cột có NHIỀU ô là link SP tách được id
     (`HarvestItemKey` ra `id:`) nhất trong 200 dòng đầu, hoà → cột trái (một ô "Ghi chú" lẻ hay cột "Link shop" đứng trước không cướp
     được); không cột nào có id SP → cột nhiều link Shopee nhất; không có → cột A. Host Shopee gom một chỗ
     (`HarvestLinks.IsShopeeHost`: `shopee.{vùng}`, `*.shopee.{vùng}`, `shp.ee`, `shope.ee`). Ô chỉ là link khi không có khoảng trắng
     và (có `http(s)://` / `www.` hoặc có path) — tên miền trơn "Shopee.vn" / "Shopee.Link" (hay gặp ở tiêu đề) không tính. Dòng 1 là
     tiêu đề, TRỪ KHI ô cột link ở dòng 1 đã là link. Ô cột link là link → phải lấy (tên SP không bắt buộc — overlay lấy tên từ
     JSON); ô trống hoặc chữ không phải link ("hết hàng", "x", tiêu đề gộp) → dòng không có link, bỏ. Cột chọn có < ½ số dòng là link
     SP, hoặc có dòng chữ không phải link → cảnh báo (`HarvestSheetInfo.Warning`, vd "sheet X: chỉ 3/200 dòng có link SP ở cột C"):
     cột "Ghi chú" trên màn + log đầu lượt sheet. File GĐ5 (tiêu đề + link cột A) đánh số dòng y như cũ.
   - Ô số (N ngày, số cửa sổ, dòng mỗi lần, khung, nghỉ giữa link) nhập ngoài khoảng → ô hiện ngay giá trị đã kẹp (0 ngày → 30,
     > 3650 → 3650, < 1 → 1, cặp nghỉ theo `ScrapeRestWindow.Resolve`) và lưu đúng giá trị đó.
2. **Định danh:** đơn vị chạy là (file, sheet); các sheet tick chạy **lần lượt** trong một phiên (sheet sau thấy SP sheet trước
   vừa lưu). Khoá tiến độ `HarvestSource.SheetKey` = đường dẫn đầy đủ chuẩn hoá (chữ thường, `\`) + `|` + tên sheet, "tài khoản"
   của kho `harvest-progress.json` = `HarvestSource.AccountId` (`"harvest"`).
   - **Tập `done` / `failed` TOÀN CỤC theo khoá SP** (`HarvestItemKey`): SP đã lưu ở sheet/file nào cũng tính là đã có. Luật lỗi
     vĩnh viễn 2 lượt giữ nguyên (mỗi lần bấm Chạy = một `RunId`, dùng chung cho mọi sheet của lượt).
   - Outbox **schema v4** (`MigrateV3ToV4`, một giao dịch): `done(item_key PK, saved_at)` gộp các target, giữ `saved_at` mới nhất;
     `failed(item_key PK, …)` gộp, `hits` = lớn nhất (không cộng — cùng lượt ở 2 sheet không thành 2 lượt), lý do/mốc/lượt của bản
     mới nhất, khoá đã có trong `done` bị bỏ. `pending` / `counters` / `meta` không đổi. Bản app GĐ5 không mở được DB v4.
   - Tập `done` cục bộ cũng theo hạn N ngày (`DoneKeys(savedSince)`): SP lấy trên máy quá N ngày thì lấy lại như bản trên Hub.
   - SP Hub TỪ CHỐI (mục outbox của SP đang `failed`, không còn mục `pending` nào của SP đó) KHÔNG tính là đã có → lượt sau lấy lại;
     "Đẩy lại mục lỗi" đưa về `pending` → lại là đã có. Mục chờ đẩy bình thường vẫn là đã có (không lấy trùng). `done` có thêm cột
     `item_id` (v4; migration suy từ khoá `id:` cũ, khoá `url:` cũ để NULL = giữ như cũ).
   - API: `HarvestedItem.RecordDone` / `HarvestRunOptions.RecordDone` (bool) thay `DoneTarget`; `IHarvestFailureSink` bỏ tham số
     target; `HarvestOutbox.DoneKeys(since)`, `ClearDone(keys)`, `FailedKeys(minRuns)`, `ClearFailed(keys)`.
3. **Gửi Hub:** `account_id` = `"harvest"` (hằng), `sheet` = tên sheet, `row_no` như cũ (chỉ số dòng nén). Hub không ràng buộc gì
   thêm với `account_id` (chỉ độ dài ≤ 256; `stats` lọc theo account/sheet).
   - **Ảnh hưởng app đăng SP:** nguồn trên Hub nay là ("harvest", tên sheet) — không còn "TK BigSeller × sheet". Hai sheet cùng
     tên ở hai file khác nhau chung một nguồn trên Hub. App đăng nên tự đọc Excel ra itemId rồi gọi API theo itemId
     (`by-items`), hoặc chọn nguồn theo sheet với `account_id` cố định `"harvest"`.
4. **Bỏ qua SP đã có trên Hub (còn hạn):**
   - Hub: `POST /api/harvested-products/known` (`HubRoutes.HarvestedProductsKnown`), body `{itemIds (≤ 500, đếm cả trùng, Hub khử
     trùng), maxAgeDays (1..3650)}` → `{items:[{itemId, harvestedAt}]}` các id có dữ liệu với mốc lấy gần nhất còn trong hạn. KHÔNG
     trả raw. Lỗi: thiếu itemIds → 400 `item-ids-required`; quá trần → 400 `too-many-ids`; maxAgeDays sai → 400 `max-age-days`.
   - Mốc "lần lấy gần nhất" = `harvested_products.harvested_at`: ghi bản mới → đặt bằng mốc mới; hash TRÙNG → `sqlBumpAt` nhích lên
     GREATEST → lấy lại SP (kể cả JSON y hệt) luôn làm mới mốc. `updated_at` KHÔNG dùng (hash trùng không đụng tới). Không cần
     migration 6.
   - Client: `HubClient.GetHarvestedKnownAsync` (khử trùng, bỏ id ≤ 0, chia lô 500, một lô hỏng là ném cả lời gọi);
     `HarvestPlanning.AskHubAsync` (không ném — Hub không trả lời / chưa nối Hub → `Known = null` + lý do, chỉ dựa vào `done` cục
     bộ, log rõ; huỷ của caller thì ném); `HarvestDoneRebuild.Build(…, hubKnown)` — dòng có khoá `id:` trùng itemId Hub báo → đã
     có. Khoá `url:` / `txt:` không hỏi Hub được. SP đã có trong `done` cục bộ không hỏi Hub.
5. **Engine:** không đổi luồng; job Harvest dựng `ScrapeRunner` thẳng với `bigSellerAccountId = "harvest"`, sheet = tên sheet,
   workbook = file đã chọn (`ScrapeViewModel.RunHarvestAsync` / `HarvestSheetBodyAsync`, dùng chung `RunSession`, khung tk,
   `SessionAccountPool`, watchdog, link mẫu, khoá `.lock` / xử lý chờ như GĐ5). `JobHandle.Target/Shop` nullable cho job Harvest.
   Lỗi hạ tầng toàn cục (key proxy chết / chỗ lưu hỏng) hoặc file đang bị khoá → dừng cả lượt; sheet đọc lỗi → bỏ sheet đó.
   Vòng sheet tách thành `ScrapeViewModel.RunSheetsAsync` (delegate chạy một sheet) + `EndAfterReadError` / `EndAfterRun` để test
   thứ tự, `RunId` chung cả lượt, dừng khi `StopRun`, huỷ thì dừng ngay.
6. **Gỡ phần chỉ phục vụ mô hình cũ:** `HarvestRunConfigStore` + `RunConfigSource`, `HarvestWorkbookResolve`, `SharedWorkbookPolicy`,
   `RoleText`, `HarvestDoneTarget`, `HarvestRuntime.PendingSaveWait`; mọi nhánh `IsHarvest` trong `ScrapeViewModel` (đường Scrape),
   `ScrapeTargetViewModel`, `ScrapeProgressReset`, `ScrapeStatsViewModel`, `WorkspaceViewModel` / `WorkspaceView` /
   `WorkspaceShopViewModel` / `WorkspaceAccountViewModel`, `BigSellerViewModel` / `BigSellerView`, `UpdateRunTargetViewModel`,
   `ShellViewModel` — các file này về đúng bản trước GĐ3 (đường Scrape không đổi hành vi).

**Tiêu chí nghiệm thu GĐ6:**
- [ ] `ShopeeHarvest.exe` mở ra màn "Lấy dữ liệu" với nút chọn file Excel; shell 3 tab, không có chữ "BigSeller" ở tab / ribbon /
      màn chính / thanh trạng thái (có test dựng shell + view). App Scrape vẫn đủ 4 tab, danh sách tài khoản như cũ (có test).
- [ ] Chọn file nhiều sheet và file một sheet đều liệt kê đúng sheet + số link; sheet tick chạy lần lượt; đường dẫn + sheet bỏ
      tick nhớ qua lần mở sau.
- [ ] File chỉ một cột link (có và không có tiêu đề), link ở cột khác A, dòng thiếu tên SP: đều lấy đúng các dòng có link. Cột
      "Ghi chú" / "Link shop" không cướp cột link SP; chữ không phải link bị bỏ và có cảnh báo; `shope.ee` được nhận.
- [ ] SP Hub từ chối không bị coi là đã có; mục chờ đẩy bình thường vẫn là đã có.
- [ ] Vòng nhiều sheet: đúng thứ tự, `RunId` chung, sheet đọc lỗi bị bỏ riêng, lỗi hạ tầng / file khoá / huỷ dừng cả lượt (test).
- [ ] Cùng một SP ở 2 sheet / 2 file chỉ mở trang một lần (tập `done` toàn cục); SP Hub đã có còn hạn không mở trang; quá N ngày
      thì lấy lại; Hub sập → vẫn chạy, chỉ bỏ qua theo `done` cục bộ, log nói rõ.
- [ ] DB outbox v3 thật lên v4 không mất mục chờ đẩy, đếm, link mẫu; `done` gộp đúng (test migration).
- [ ] `POST /api/harvested-products/known`: lấy lại với `raw_hash` trùng làm mới mốc; trần 500; `maxAgeDays` cắt đúng; không trả raw
      (Postgres thật).
- [ ] Hub nhận `account_id = "harvest"`, `sheet` = tên sheet.
- [ ] Build Release 0 warning hai solution; test Core / Workspace / Hub / JS xanh.

### GĐ7 — Ext bấm nút kiểu BigSeller, ext tự đẩy Hub

User chốt (2026-11-18): extension `shopee-harvest` có **hộp nổi** góc dưới phải trên trang SP, bấm **"Lấy dữ liệu"** thì mới lấy
JSON và **đẩy thẳng lên Hub bằng token chung** (`X-Api-Token` + `X-Machine-Id`). Hai cách dùng cùng một nút: (a) cài tay trên máy
không có app, người dùng tự bấm — SP lưu **không gắn nhóm** (không account / sheet / dòng); (b) app Harvest mở trang rồi **tự bấm
đúng nút đó bằng chuột thật qua CDP**, ext tự đẩy Hub kèm nguồn, app chỉ chờ kết quả.

1. **Hộp nổi** (`panel-core.js` thuần + `panel.js`, content script ISOLATED `document_idle`, Shadow DOM đóng): hiện SP đã có trên
   Hub chưa + lấy lúc nào (`known`, chỉ khi bấm tay), số chờ gửi / lỗi của hàng đợi ext, kết quả lần bấm gần nhất; chưa cấu hình
   Hub → nút "Mở cài đặt". Chỉ nhận cú bấm `isTrusted`. Bị đẩy khỏi trang SP (đăng nhập / xác minh / không phải SP) → hộp vẫn hiện
   đúng loại lỗi trong 2 phút. SPA đổi SP → hỏi lại trạng thái.
2. **Lấy JSON**: đi đúng `__launcherExecuteHarvestStep` với `inPlace` (không điều hướng lại — giữ JSON đã bắt; chưa bắt thì chờ / tải
   lại một lần / captcha / notProduct / loginRequired / traffic như cũ).
3. **Làm sạch phía ext** (`pdp-clean.js`): bản JS của `HarvestPayloadCleaner` + phần tóm tắt của `ShopeePdpParser`; ra GIỐNG HỆT
   C# (cùng chuỗi, cùng `raw_hash`, cùng cột). Bảng ký tự bị escape của System.Text.Json (.NET 8, UnsafeRelaxed) sinh từ chính .NET.
   Bảo đảm bằng file kỳ vọng chung `tests/Shopee.Core.Tests/Fixtures/pdp-clean/{cases,expected}.json` (7 fixture thật + ca tổng
   hợp): test C# giữ bản C# đúng file, test JS giữ bản JS đúng file.
4. **Gửi Hub** (`hub-client.js`, `click-flow.js`): request y như client C# (`{machineId, clientNow, items:[HarvestedProductItem]}`,
   số nguyên ghi đúng từng chữ số). Kết quả gắn vào kết quả bước: `hub.status` = `saved` | `queued` | `rejected` | `invalid` |
   `failed`. Quyền host tối thiểu: Shopee + Hub mặc định + localhost; Hub khác xin quyền lúc bấm Lưu ở trang Cài đặt.
5. **Hàng đợi ext** (`hub-queue.js`, `chrome.storage.local`, mỗi mục một khoá): mất mạng / 5xx / 503 / 408 / 429 / 401 (bấm tay)
   → giữ, lùi dần 30 s → 15 phút (`chrome.alarms`), không trần số lần; 200 + mục ok=false / 4xx dữ liệu → "lỗi", hiện ở popup,
   nút "Gửi lại"; 413 nhiều mục → gửi lẻ. Trần 100 mục / 6 MB / 2 MB một mục — đầy thì báo lỗi, không âm thầm bỏ mục cũ.
   - (Sau phản biện GĐ7) Lùi dần **theo từng profile** (`manual` = cấu hình trang Cài đặt, `auto` = cấu hình app truyền): profile
     gặp lỗi tạm / token / chưa cấu hình chỉ hoãn chính nó, profile kia gửi tiếp (trước đây mục `manual` đầu hàng chặn cả `auto`).
     Sau 413, profile đó gửi lẻ cho tới khi hết mục chờ (không xen lô / lẻ).
   - Bấm tay mà Hub từ chối ngay lần gửi đầu (ok=false / 4xx) → SP vẫn vào hàng đợi ở trạng thái **lỗi** kèm lý do (popup hiện,
     "Gửi lại" được); chế độ tự động thì không (app tự lấy lại lượt sau).
   - Hub của app là `http://<máy khác>` (vd IP LAN) → báo đúng lý do: ext không gửi token qua http ngoài localhost, cần https hoặc
     localhost. `chrome.storage.local.setAccessLevel(TRUSTED_CONTEXTS)` lúc nạp SW (content script của ext không đọc storage).
6. **Chế độ tự động** (`HarvestClickStep`, `RunnerExtensionRpc.HarvestClick`): RPC `prepareHarvestClick` (mở link, gắn ngữ cảnh
   `account_id="harvest"`, sheet, dòng, link sạch + link gốc, RunId, Hub URL / token / mã máy của app; chờ hộp ≤ 15 s; trả toạ độ
   nút đã cuộn vào tầm nhìn; không có hộp → ext tự chạy bước tại chỗ, trả đúng loại kết quả) → app bấm chuột thật
   (`Input.dispatchMouseEvent`, trong 40% giữa nút) → `getHarvestClickResult(token)` tới khi xong (cú bấm không tới sau 8 s → hỏi
   lại toạ độ, bấm lại, tối đa 2 lần; trần 600 s). Luật GĐ5–GĐ6 giữ nguyên (`HarvestRowProcessor`): `saved` → sink
   `ExtensionPushedHarvestSink` chỉ ghi `done` + link mẫu (outbox không nhận mục mới, worker vẫn đẩy nốt mục cũ); `queued` /
   `rejected` → lỗi tạm, KHÔNG `done`; `invalid` → lỗi vĩnh viễn (luật 2 lượt); `failed` / ext không báo kết quả Hub / app chưa
   nối Hub → dừng job như lỗi chỗ lưu.
   - (Sau phản biện GĐ7) **Dừng** tới giữa lúc prepare (tải trang / chờ URL / chờ hộp) → trả kết quả `aborted`, KHÔNG chạy bước lấy,
     KHÔNG đẩy Hub; bước lấy của luồng hộp nổi không tự xoá cờ huỷ (`keepAbort`); keepalive trả về theo trạng thái launcher.
   - (Sau phản biện GĐ7) **Chống bấm trượt có hệ thống**: khi PING, hộp kiểm `elementFromPoint(tâm nút)` đúng là hộp (bị che → không
     sẵn sàng, `covered: …`), báo `visualViewport.scale` + `devicePixelRatio`; background báo zoom tab (`chrome.tabs.getZoom`). Zoom tab
     / thu phóng trang khác 100% → app KHÔNG bấm (không quy đổi chắc được toạ độ CSS px ↔ toạ độ chuột CDP), báo rõ "đặt zoom 100%".
     Cầu dao app: `HarvestPanelClickFailure.BreakerThreshold` = 3 dòng LIÊN TIẾP bấm trượt (không nhận / bị che / ngoài khung nhìn /
     zoom / CDP bấm lỗi) → dừng CẢ job (StopJob, tiền tố "Bấm nút hộp nổi lỗi: ", không chữ captcha / verify / proxy), không bỏ dòng;
     dòng khác chen vào thì đếm lại.
   - (Sau nghiệm thu GĐ7) Ba số Hub trên màn Harvest + dòng "Hub: …" của bảng tổng kết khối: "đã nhận" = outbox app đẩy + SP ext tự đẩy
     (bộ đếm bền `counters.ext_saved`, tăng cùng giao dịch ghi `done`); "app chờ đẩy" / "lỗi" = hàng đợi PHÍA APP.
   - **Hub sập ở chế độ tự động — chọn "ext giữ trong hàng đợi riêng + app coi là lỗi tạm"**: SP đã lấy không mất (nằm bền ở ext,
     tự gửi khi Hub lên); app không ghi `done` vì chưa chắc Hub nhận (hàng đợi ext có thể bị xoá / đầy / bị từ chối) — lượt sau
     hỏi Hub (`known`): đã có thì bỏ qua, chưa có thì lấy lại. Đổi lại: Hub sập lâu thì lượt sau có thể lấy lại vài SP còn kẹt.
7. **Hub**: batch nhận mục không nguồn (đã có từ GĐ1: không trường nguồn nào → chỉ upsert `harvested_products`). Thêm
   `HarvestRawGuard`: cắt `data.account` → `{}`, `product_shipping` chỉ còn nơi gửi, bỏ `shop_vouchers` dù client nào gửi; raw sạch
   thì giữ nguyên raw + `raw_hash` của client; phải cắt thì tính lại hash từ chuỗi đã cắt; cần cắt mà có khoá trùng → từ chối mục
   (`raw-private`). KHÔNG chuyển sang "Hub luôn tự tính `raw_hash`": hợp đồng hiện có (và test) coi hash là định danh nội dung do
   client gửi; hai client đã chứng minh ra cùng chuỗi + cùng hash nên không lệch.
8. **Cài máy khác**: Load unpacked từ `extensions\shopee-harvest` cạnh `ShopeeHarvest.exe` hoặc thư mục giải nén từ zip; nhập Hub
   URL / token / mã máy ở trang Cài đặt (README trong thư mục ext). `manifest.json` 1.1.0.
9. **JSON giả từ script trang** (phản biện GĐ7, ngoài phạm vi ban đầu): script bất kỳ trên shopee.vn bắn được event `get_pc` giả vào
   `pdp-store`. Kênh bí mật hook ↔ store **KHÔNG làm**: mọi đường trao bí mật giữa world MAIN và ISOLATED đi qua DOM / `window`
   (event, `postMessage`, `MessagePort` chuyển qua `postMessage`) mà script trang nghe được — tin `postMessage` gửi lúc
   `document_start` tới SAU khi script inline đầu tiên của trang có thể đã gắn listener; bí mật gửi kèm từng tin thì trang đọc được
   ngay ở tin đầu. Chặn rẻ thay thế: trước khi đẩy Hub, `item_id` VÀ `shop_id` của JSON phải khớp URL trang đang xem (shop lệch →
   `rejected`, lỗi tạm, không vào hàng đợi — có thể là link cũ của SP đổi shop); Hub vẫn kiểm `item_id`/`shop_id` khớp mục.

**Tiêu chí nghiệm thu GĐ7:**
- [ ] Trang SP Shopee có hộp nổi góc dưới phải (Shadow DOM đóng); trang không phải SP thì không có; bấm bằng script trang
      (`isTrusted=false`) không làm gì (test JS).
- [ ] Bấm tay: SP lên Hub **không** có dòng `harvested_product_sources`; hộp báo "✓ Đã lưu Hub"; chưa cấu hình → hộp nhắc mở cài
      đặt; mất mạng / Hub 503 → "đã vào hàng đợi", alarm gửi lại, popup có số chờ / lỗi + "Gửi lại".
- [ ] `pdp-clean.js` ra cùng chuỗi + cùng `raw_hash` + cùng cột với C# trên 7 fixture thật + mọi ca tổng hợp (cả ca bị từ chối).
- [ ] JSON gửi Hub không còn `data.account` (khác `{}`), `delivery_address`, `shop_vouchers`; Hub tự cắt nếu client gửi lên (test
      Postgres thật); raw sạch thì Hub giữ nguyên hash client.
- [ ] Chế độ tự động: app chờ hộp, bấm đúng toạ độ nút bằng `Input.dispatchMouseEvent`, bấm lại khi trượt, ánh xạ kết quả ext sang
      các loại kết quả của app; `done` chỉ ghi khi Hub đã nhận; Hub sập không ghi `done`, không mất SP (test IO giả).
- [ ] Không có token / URL Hub viết cứng trong code ext; quyền host chỉ Shopee + Hub (+ localhost).
- [ ] Dừng giữa lúc chờ hộp → không lấy, không đẩy Hub; mục `manual` hỏng không chặn mục `auto` (và ngược lại); hộp bị che / trang
      zoom ≠ 100% → không bấm, báo rõ; 3 dòng liên tiếp bấm trượt → dừng job; ba số Hub tính cả SP ext tự đẩy (test).
- [ ] Build Release 0 warning (workspace, Hub); test Core / Workspace / Hub / JS xanh; chạy thật trên Brave (bấm tay + app) do user
      xác nhận.

### GĐ7.1 — sửa giao diện + nối Hub cho Harvest

User chạy bản GĐ6+GĐ7 (2026-11-18) báo: dải ô số trên màn Lấy dữ liệu bị xén chữ và đè lên hàng nút; thiếu chọn tất cả sheet; màn
báo "Không hỏi được Hub (chưa nối Hub)" — Workspace đọc `SuitePaths.RootFile("hub-client.json")` (`%AppData%\ShopeeBackend\Workspace`)
mà máy user chỉ có cấu hình ở `%AppData%\ShopeeSuite\hub-client.json`, và Workspace không có màn nhập Hub → `CoordinationRuntime.Client`
null → `known` không chạy, extension không nhận được cấu hình Hub ở chế độ tự động, outbox không đẩy được.

1. **Giao diện** (`Modules/Harvest/HarvestView.xaml`): ô số bỏ chiều cao cố định (theme TextBox MinHeight 30 + Padding 9,4 — đặt
   Height 28 là xén chữ), rộng 72 (đủ 4 chữ số), bỏ margin âm; dải cấu hình / hàng nút / dòng trạng thái là 3 khối xếp dọc trong một
   StackPanel, mỗi khối là WrapPanel theo NHÓM (nhãn + ô + đơn vị) → cửa sổ hẹp thì xuống dòng nguyên nhóm, không chồng nhau. Test dựng
   màn ở 1100 / 1900 px × 100 / 125 %: chữ mọi ô số không xén (ExtentHeight ≤ ViewportHeight), không ô nào chồng lên nút; biến môi
   trường `HARVEST_RENDER_DIR` lưu ảnh PNG.
2. **Chọn tất cả sheet:** ô ở tiêu đề cột "Chạy" (`HarvestViewModel.AllSheetsChecked`: true = tất cả, false = không, null = một phần);
   bấm → tick hết / bỏ hết (đang một phần → tick hết), nhớ như tick tay (lưu một lần).
3. **Nối Hub cho Harvest:**
   - Cài đặt → **Kết nối Hub** (`HarvestHubView` / `HarvestHubViewModel`, CHỈ app Harvest): URL, token (ô mật khẩu, token đã lưu không
     hiện lại — để trống = giữ nguyên), mã máy chỉ đọc; **Lưu** ghi `hub-client.json` của Workspace rồi `CoordinationRuntime.Reconnect()`
     ngay; **Kiểm tra kết nối** gọi `/health` rồi `stats` có token (phân biệt "không tới được Hub" / "token sai"); **Lấy cấu hình từ
     Shopee Suite** đọc `%AppData%\ShopeeSuite\hub-client.json` CHỈ khi người dùng bấm, điền URL, giữ token ẩn chờ Lưu.
   - Không tự đọc file của Suite lúc khởi động: đọc ngầm token của app khác (có thể là Hub / token khác người dùng muốn) là thao tác
     người dùng không thấy; thay vào đó màn Lấy dữ liệu GỢI Ý ("có cấu hình Hub của Shopee Suite… bấm Lấy cấu hình") khi chưa nối mà
     file Suite có mặt.
   - Màn Lấy dữ liệu: dòng trạng thái Hub (đã nối: URL / chưa nối: hậu quả + cách sửa) + nút "Cài đặt Hub"; lưu Hub xong cập nhật ngay.
   - **Init chỉ-client:** `CoordinationRuntime.ClientOnly = true` cho Harvest (đặt trước init) → `InitForCurrentMode` / `Reconnect` chỉ
     dựng `Client` (`InitClientOnlyFromConfig`): không `HttpCoordinationHub` (không heartbeat máy, không poll `/fleet`, không fold
     ledger, không kéo cấu hình / cookie BigSeller, không tranh việc với app Scrape cùng máy, không cùng mã máy heartbeat 2 process).
     Harvest từ GĐ6 không dùng gì của `HttpCoordinationHub` (không lease theo dòng, không ledger; tiến độ dựng lại từ `done`); bỏ dòng đặt
     `HttpCoordinationHub.ProgressFoldOp = Harvest`. Các app khác: `ClientOnly` mặc định false, không đổi.
   - Token không ra log / câu báo (kể cả câu báo "token sai").
4. **Vòng sửa sau phản biện (user chốt V2):**
   - **V2 — file cấu hình Hub RIÊNG của Harvest:** `<SuitePaths.Root>\harvest\hub-client.json` (`HarvestHubPort.Store` =
     `new HubClientConfigStore(path)` — kho nhận đường dẫn). App Harvest gán `CoordinationRuntime.ClientStore` = kho này trước init →
     init / Reconnect / `HarvestHubAccess` đọc file riêng; app Scrape / Update vẫn `HubClientConfigStore.Shared`
     (`<Root>\hub-client.json`), không đổi. Lưu ở Harvest không tạo / sửa file chung (test). Sửa chú thích sai đường dẫn
     (`%AppData%\ShopeeSuite\…` → `<SuitePaths.Root>` = `%AppData%\ShopeeBackend\Workspace\…`) ở `HubClientConfig.cs`,
     `HubServerConfig.cs`, `MachineIdentity.cs`.
   - **V1 — luật URL chung với extension** (`HarvestHubUrl`, bản C# của `normalizeHubUrl`): chỉ https, hoặc http tới localhost /
     127.0.0.1; chuẩn hoá về gốc `scheme://host[:port]`. http ra ngoài máy → cảnh báo đỏ, không Lưu, Kiểm tra chỉ gọi `/health`
     (KHÔNG gửi token). Đổi URL khác URL đã lưu (hoặc khác URL lấy từ Suite) → không tự gửi token cũ, bắt nhập lại. URL khác Hub mặc
     định (không phải localhost) → nhắc cấp quyền ở trang Cài đặt của extension. (Chưa làm: app tự hỏi extension có quyền trước khi
     chạy — extension hiện báo "Chưa cấp quyền truy cập …" vào hàng đợi của nó.)
   - **V3 — test hành vi `ClientOnly`** (Core, cấu hình giả hợp lệ ở kho tạm): sau `InitForCurrentMode()` và `Reconnect()`: `Hub` null,
     `Coordination.Hub` là NoOp, `Client` có, `OrdersSlot` / `Relogin` null; reset static sau test.
   - **V4:** `HubClientConfigStore.Save` trả `bool`; Lưu hỏng → báo lỗi (đường dẫn file), không nối lại, không "Đã nối".
   - N1 Lưu xong tự Kiểm tra kết nối (token sai → báo rõ, không xanh; màn chính "Hub chưa dùng được"); N2 Lưu xong, app rảnh + có file
     → quét lại; N3 lấy từ Suite: 3 câu báo thiếu file / file hỏng / thiếu URL-token; N4 Lưu xong → worker outbox bỏ mốc lùi dần + mốc
     hẹn của mục chờ (`ResetBackoffAsync` / `HarvestOutbox.ClearDeferrals`) rồi đẩy ngay; N5 `ObjectDisposedException` lúc Reconnect →
     lỗi tạm "kết nối Hub vừa đổi" (không phải "lỗi outbox"); N6 bỏ `xUnit1031` (test dựng màn: VM quét file async trước, view dựng
     trên STA).

**Tiêu chí nghiệm thu GĐ7.1:**
- [ ] Ô số không xén chữ, không đè hàng nút ở 1100 / 1900 px × 100 / 125 % (test dựng màn + ảnh).
- [ ] Ô tiêu đề "Chạy" 3 trạng thái, bấm tick / bỏ hết, nhớ qua lần mở sau (test VM).
- [ ] Lưu cấu hình Hub → nạp lại đúng, nối lại ngay; lấy từ file Suite (file giả); chưa cấu hình → báo "Chưa nối Hub" kèm cách sửa;
      app Scrape không có mục Kết nối Hub (test).
- [ ] Harvest init chỉ-client (không suất workspace); user nhập Hub trên máy thật → màn báo "Đã nối Hub", `known` chạy, outbox đẩy được.
- [ ] Cấu hình Hub của Harvest nằm ở `harvest\hub-client.json`; Lưu ở Harvest không tạo / sửa `hub-client.json` chung (test).
- [ ] URL http ra ngoài máy: cảnh báo, không lưu, không gửi token; đổi URL không tự gửi token cũ (test); lưu hỏng / token sai báo rõ.

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
- Bổ sung sau GĐ2: Hub nhận `"error":0` là hợp lệ (308/308).

### GĐ2 — Extension `workspace/extensions/shopee-harvest` (2026-11-15): XONG

- File: `manifest.json` (2 content script: `pdp-hook.js` MAIN + `pdp-store.js` ISOLATED, document_start), `pdp-core.js`
  (thuần: itemId neo `(?:^|[/\-])i\.(\d+)\.(\d+)(?=$|[?#&/])`, `checkPdpBody`, `redactPrivate` đóng an toàn),
  `background.js` (`__launcherExecuteHarvestStep`, probe `hasHarvestStep`), `popup.*` (chẩn đoán + Xuất JSON luôn cắt
  riêng tư), `shared/*` (giống hệt scrape), `tests/*.test.mjs` (106 test Node: `node --test
  "extensions/shopee-harvest/tests/*.test.mjs"` từ `workspace/`).
- Kết quả bước trả C#: `ok, scrapeOk, harvestOk, captcha, proxyError, trafficError, blocked, notProduct, aborted,
  superseded, message, tabId, pageUrl, itemId, shopId, json (đã cắt account + delivery_address), source
  ("hook"|"hook-reload"), pdpError, capturedUrl, capturedAt`. Payload tuỳ chọn `blockErrorCodes`. Bước KHÔNG tự ghi
  `lastCompletedRow`; trần tổng 540 s; dự phòng = reload 1 lần; mã chặn mặc định `[90309999]`.
- Chạy thật trên Brave (user): click và **dán URL mở thẳng** đều bắt được; trang Shopee chạy bình thường. 7 fixture thật
  ở `workspace/tests/Shopee.Core.Tests/Fixtures/pdp/` (đã cắt riêng tư).
- 5 vòng nghiệm thu + 4 vòng phản biện. Rủi ro chấp nhận (ghi đầu `pdp-hook.js`): dò `toString` từ realm khác,
  phép thử vòng `__proto__`, khung stack `chrome-extension://`.

### GĐ3 — Engine chế độ Harvest (2026-11-16): XONG (chưa chạy thật — cần exe GĐ5)

- `RunnerMode { Scrape, Harvest }` (`Shopee.Module.MultiBrave/RunnerMode.cs`), mode toàn process
  `MultiBraveRuntime.SetMode`; `RunnerModeProfile` (thư mục/tên extension, cờ probe) — bản Scrape giữ nguyên từng chữ.
- Vòng Harvest: `Engine/HarvestRunnerLoop.cs` (gọi bước, nghỉ giữa link) + `Engine/HarvestRowProcessor.cs` (bảng kết
  quả → hành động, `IHarvestRowHost`, `HarvestRowBook`); RPC `Engine/RunnerExtensionRpc.Harvest.cs` (timeout 600 s,
  chỉ gửi lại khi chắc chưa tới extension, không đường popup 6 s).
- `Shopee.Core/Harvest/`: `IHarvestSink` (`EnsureReadyAsync`, `SaveAsync`) + `HarvestedItem` (có `Url` đã làm sạch và
  `SourceLink` gốc, `HarvestedAt` từ `capturedAt`), `HarvestStepResult`, `HarvestStepPolicy` + `HarvestPdpBreaker`
  (chỉ đếm cùng mã; nhớ mã "SP chết" chỉ khi tái diễn ngay sau đổi IP), `HarvestLinks.CleanProductUrl` (neo `-i.`).
- Sink lỗi → tiền tố "Lưu dữ liệu lỗi: " → `StopJob` (nhả tài khoản, không kẹt, không bỏ dòng); sink chưa sẵn sàng →
  dừng trước khi mở Brave. Tiến độ riêng `harvest-progress.json` / `harvest-targets.json`, `CoordOp.Harvest`; lease
  harvest gửi `BigsellerId=""`. Harvest bỏ Product Copy (không kiểm, không nạp), bypass bigseller, token BigSeller,
  NeedLogin, video, `HasCookie`.
- Test workspace 290 + 821, build Release 0 warning. 2 vòng nghiệm thu + 2 vòng phản biện + lượt dọn.

### GĐ4 — Outbox client + đẩy Hub (2026-11-16): XONG (chưa chạy thật — cần exe GĐ5 + Hub deploy)

- `workspace/src/Shopee.Core/Harvest/`: `HarvestPayloadCleaner` (bỏ `\0`/surrogate lẻ, cắt riêng tư lớp 2, bỏ khối
  phụ thuộc người xem; số giữ nguyên văn; MaxDepth 64; `raw_hash` = SHA-256 chuỗi sạch), `ShopeePdpParser` (đúng trên
  7 fixture), `HarvestItemRejectedException` (JSON không xử lý được → engine coi là lỗi dòng), `HarvestOutbox :
  IHarvestSink` (SQLite WAL, UNIQUE account/sheet/row, upsert theo `harvested_at`, cột `version` chống xoá oan, BUSY
  thử lại ≤10 s, `EnsureReadyAsync` ghi thử + đĩa ≥500 MB, `user_version=1`, đường dẫn
  `<SuitePaths.Root>\harvest\harvest-outbox.db`), `HarvestOutboxWorker` (lùi dần chung ±20% nhiễu, mục nghi vấn gửi
  xen kẽ, 5 lần 500 có bằng chứng → failed, mồ côi lùi theo Attempts, 413 chia/nới, Requeue, Flush, thống kê bền
  `counters`).
- `HubClient.PostHarvestedBatchAsync`; DTO + route chép sang workspace (`HarvestedProductDtos.cs`, sửa thì sửa cả 2).
- Hub: `SourceUrl` → `harvested_product_sources.url`; `WithPgHarvest` → 503 `pg-unavailable` cho lỗi phía
  Hub/Postgres (mất kết nối, lớp 53/57/08/40).
- GĐ5 khởi tạo: `var box = new HarvestOutbox(HarvestOutbox.DefaultDbPath(), log)`; `var w = new
  HarvestOutboxWorker(box, () => CoordinationRuntime.Client, log: …)`; `w.Start()`; `w.Nudge()` sau mỗi lần lưu;
  `w.GetStatsAsync()` → `(Pending, Failed, PushedTotal, AppliedTotal)`, event `w.Changed`; `w.RequeueFailedAsync()`;
  đóng app `await w.StopAsync(); await w.FlushAsync(timeout)`.
- Test: workspace 292 + 905, Hub 310 (Postgres thật). 3 vòng nghiệm thu + 3 vòng phản biện (vòng 2 bắt lỗi NẶNG kẹt
  hàng đợi vĩnh viễn → thay thiết kế ghim/chia đôi bằng nghi vấn xen kẽ).

### GĐ5 — Exe `ShopeeHarvest.exe` + tiến độ theo itemId (2026-11-17): XONG (chưa chạy thật, chưa deploy Hub)

- Exe riêng `workspace/src/Shopee.Harvest` (icon xanh ngọc + trụ DB), mutex một bản, hồ sơ Brave `harvest-profiles`,
  UI ẩn các lệnh không dùng (Scrape Multi, Đẩy video, workbook local), Harvest chỉ đọc workbook chung; 3 số Hub
  (đã nhận / chờ đẩy / lỗi) + "Đẩy lại mục lỗi"; overlay từng dòng ("✓ Đã xong dòng N: … · 🎬 Có video (đã lưu
  link)") và bảng tổng kết khối giữ 5 s. Video chỉ lưu link.
- Tiến độ: vòng 1–10 thử đoán workbook đổi theo vị trí dòng → lộ lỗi bỏ sót / xoá oan mỗi vòng → bỏ hẳn, đổi sang
  **tập khoá SP đã lưu** (`done`, cùng giao dịch với outbox) + dựng lại mỗi lượt Chạy/Tiếp tục (xem GĐ5.5).
  `ScrapeProgressStore.cs` chỉ thêm `ReplaceProgress`.
- Outbox lên v3: `pending` khoá (account, sheet, item_id); bảng `done`, `failed(hits, last_run_id)`, `meta`.
  Hub migration 5: `harvested_product_sources` UNIQUE (account, sheet, item_id). Bản app cũ không mở được DB v3.
- Lỗi vĩnh viễn (notProduct, pdpError đã xác nhận, JSON bị từ chối) chỉ bỏ qua khi thấy ở ≥ 2 lượt; trang đăng nhập →
  `loginRequired` (dừng khối, tài khoản nghỉ, không ghi lỗi); chuỗi 3 notProduct → kiểm phiên bằng link mẫu (kiểm
  lại mỗi 10 dòng; không có mẫu thì không dừng khối); khối duyệt hết thì báo xong cả khối; khoá `.lock` của Update →
  chờ huỷ được, không phạt tài khoản; resolve chặt không rơi sang file gộp khi file nguồn bị khoá.
- Ý "báo fatal lên Hub" trong plan không áp dụng: workspace không có AssignmentWorker.
- Test: Workspace 386, Core 970, Hub 312 (Postgres thật), JS 110; build Release 0 warning cả hai solution.
  16 vòng nghiệm thu / phản biện. Chưa chạy end-to-end trên Brave thật; Hub chưa deploy migration 4–5.

### GĐ6 — Harvest theo file Excel, bỏ BigSeller, bỏ qua SP đã có trên Hub (2026-11-18): XONG (chưa chạy thật, chưa deploy Hub)

- Harvest tự chọn file Excel, chạy các sheet được tick lần lượt, chung một `RunId`. Không còn tài khoản BigSeller /
  shop / tab "Cấu hình BigSeller"; shell Harvest còn 3 tab (Lấy dữ liệu, Tài khoản & Proxy, Cài đặt). Màn riêng
  `Modules/Harvest/*`; các file Workspace dùng chung trả về đúng bản trước GĐ3 (`5b1f96f`).
- File tối thiểu: một cột link SP Shopee bất kỳ (tự chọn cột có nhiều link SP nhất), dòng 1 có thể là tiêu đề, không
  cần tên SP; ô chữ không phải link bị bỏ và đếm riêng, cảnh báo ở cột "Ghi chú" khi cột link có ít link.
- Hub: `account_id = "harvest"`, `sheet` = tên sheet. Endpoint mới `POST /api/harvested-products/known` (≤ 500 id,
  `maxAgeDays` 1..3650, lọc theo `harvested_at`, không trả raw). SP Hub đã có trong N ngày (mặc định 30) thì không mở
  trang; Hub lỗi thì chỉ dựa vào `done` cục bộ.
- Outbox v4: `done` (có `item_id`) / `failed` toàn cục theo khoá SP, migration v3→v4 (hits lấy max). SP bị Hub từ chối
  (pending failed) không tính là đã có.
- Test: Core 1235 (gồm test của phiên đăng SP), Workspace 369, Hub 362 (Postgres thật), JS 110. 1 vòng nghiệm thu ĐẠT,
  1 vòng phản biện (không chặn; đã sửa nhận cột link, `shope.ee`, chữ không phải link, SP Hub từ chối, kẹp ô số, test
  vòng sheet).

### GĐ7 — Ext bấm nút kiểu BigSeller, ext tự đẩy Hub (2026-11-18): XONG (chưa chạy thật trên Brave, chưa deploy Hub)

- Extension `shopee-harvest` 1.1.0: hộp nổi góc dưới trang SP (Shadow DOM đóng), nút "Lấy dữ liệu" → làm sạch JSON
  bằng `pdp-clean.js` (giống hệt bản C#, file kỳ vọng chung `Fixtures/pdp-clean/expected.json`) → gửi thẳng Hub bằng
  token chung nhập ở trang Cài đặt. Bấm tay không gắn nguồn. Hàng đợi riêng trong ext (lùi dần theo từng profile,
  413 gửi lẻ, 4xx vào danh sách lỗi có "Gửi lại"). Quyền host chỉ Shopee / Hub / localhost. README tiếng Việt.
- Chế độ tự động: app truyền ngữ cảnh + cấu hình Hub qua RPC, bấm nút bằng chuột CDP; hộp kiểm nút bị che, app từ
  chối bấm khi zoom ≠ 100%; 3 dòng liên tiếp bấm trượt thì dừng job. `saved` → ghi `done` (cùng giao dịch, đếm
  `ext_saved`); `queued` (Hub sập) → lỗi tạm. Dừng giữa lúc chờ hộp không còn đẩy Hub.
- Hub: `HarvestRawGuard` tự cắt khối riêng tư client gửi sót và tính lại hash; batch nhận mục không nguồn.
- Test: JS 235, Core 1434 (+1 skip của phiên đăng SP), Workspace 399, Hub 382 (Postgres thật). 1 vòng nghiệm thu ĐẠT,
  1 vòng phản biện (không chặn; đã sửa Dừng lúc chờ hộp, chặn đầu hàng đợi, bấm trượt/zoom, số Hub, 4xx bấm tay).
