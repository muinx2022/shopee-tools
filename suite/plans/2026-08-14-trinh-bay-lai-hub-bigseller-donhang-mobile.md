# Plan: Trình bày lại Hub — BigSeller + Đơn hàng, và refactor mobile

- **Ngày:** 2026-08-14
- **Trạng thái:** đang làm
- **Người lập:** phiên chính · **Người thực thi:** `opus-dev`

## 1. Bối cảnh & mục tiêu

User yêu cầu 2 việc trên hub web (`server/Shopee.Hub.Web`):

1. **Trình bày lại 2 page**: BigSeller (`/` → `Fleet.razor`) và Đơn hàng (`/orders` → `Orders.razor`).
   Đã chốt với user: **mức "sắp xếp lại bố cục, GIỮ NGUYÊN chức năng"** — không thêm/bớt tính năng,
   không đổi luồng thao tác.
2. **Refactor phần mobile**: đã chốt với user gồm **cả hai** —
   (a) dọn CSS (gom media query, bỏ `!important` ngược chiều), và
   (b) sửa cả trải nghiệm mobile (chấp nhận đổi giao diện). Áp cho **toàn hub**, không chỉ 2 page trên.

### Hiện trạng đã ĐO (không phải phỏng đoán)

Hub chạy local được: `.claude/launch.json` có config tên `hub` → `http://127.0.0.1:5099`,
data dir nằm ở scratchpad, admin local `admin` / `localdev-throwaway-1234` (DB rỗng, chỉ để soi layout).

Số đo bằng JS trong trình duyệt, ngày 2026-08-14:

| Chỗ đo | Hiện trạng |
|---|---|
| BigSeller @1280×720 — `.workspace` bắt đầu ở y | **328px** (46% viewport là chrome) |
| BigSeller @1280×720 — chiều cao `.workspace` | **370px** |
| BigSeller @1280×720 — 2 hàng `.kpis` cộng lại | 152px (67 + 85) |
| BigSeller @375×812 — `.workspace` bắt đầu ở y | **486px** (60% màn hình là chrome) |
| BigSeller @375×812 — dải `.kpis` fleet bị giấu ngang | **128px** (scrollW 475 / clientW 347 → khuất hẳn 1 thẻ) |
| Orders @375×812 — chiều cao `.bar.bar-stack` | **469px** trên màn 812px |
| Orders @375×812 — `.tablewrap` bắt đầu ở y | **655px** (mở lên gần như không thấy đơn nào) |
| Orders — số control trong bar lọc | 9 phần tử con / 8 control |
| `app.css` | 693 dòng · **12 `!important`** · 3 khối media (`920px` ở dòng 395 **và** 604, `640px` ở 646) |
| Build `server/ShopeeHub.sln` | **0 warning, 0 error** |
| Test `server/ShopeeHub.sln` | **151 passed / 0 failed** |

### Khuyết tật cấu trúc của CSS mobile (gốc của việc refactor)

Khối `@media (max-width: 920px)` dựng **icon-rail 64px** như thể đó là mặc định của mọi màn hẹp.
Rồi khối `@media (max-width: 640px)` phải **tháo ngược lại bằng 6 rule `!important`**
(dòng 673, 676, 679, 687, 690 + `.side` width) để làm drawer.

Tức là rail thật ra chỉ đúng cho dải **641–920px**, nhưng lại được viết ở dải `≤920px` rồi đảo ngược.
Thêm nữa, rule mobile của trang Giao việc nằm ở **khối 920px THỨ HAI** (dòng 604) tách rời khối gốc,
đến mức phải có comment ở dòng 602–603 dặn "khối này PHẢI nằm SAU khối gốc" — một sự mong manh
do thứ tự file quyết định.

## 2. Phạm vi

**Làm:**

- A. Refactor cấu trúc responsive trong `wwwroot/app.css` (3 dải breakpoint rạch ròi, hết `!important` ngược chiều).
- B. Sửa trải nghiệm mobile: dải KPI hết bị cắt ngang; bar lọc Orders hết chiếm 469px.
- C. Sắp xếp lại bố cục BigSeller (`Fleet.razor`): gộp 2 hàng KPI → giành lại chiều cao cho workspace.
- D. Sắp xếp lại bố cục Orders (`Orders.razor`): nhóm lại bar lọc.
- E. Bump `app.css?v=41` → `v=42` trong `Components/App.razor`.

**Không làm:**

- KHÔNG đổi logic C# / DB / API / service. Chỉ markup + CSS. (Ngoại lệ duy nhất được phép:
  thêm class/`<div>` bọc trong `.razor` để phục vụ bố cục.)
- KHÔNG thêm/bớt tính năng, KHÔNG bỏ control nào, KHÔNG đổi luồng thao tác của user.
- KHÔNG đổi bố cục các trang khác (Dispatch, Machines, Search, Shops, AllData, Settings, Logs…) —
  nhưng vì CSS mobile là dùng chung nên **phải kiểm chúng không hồi quy**.
- KHÔNG commit, KHÔNG deploy, KHÔNG đụng `version.txt` / `CHANGELOG.md`.

## 3. Các bước thực hiện

### A. Tái cấu trúc responsive `wwwroot/app.css`

Gom TOÀN BỘ rule responsive về một mục "Responsive" cuối file, đúng **3 khối**, đúng thứ tự sau:

1. `@media (max-width: 920px)` — **chung cho tablet + phone**: `.m-hide`/`.m-only`, bảng `stack-sm` → thẻ,
   `.bar-stack`, mật độ/touch-target, và **gộp luôn** khối 920px thứ hai (dòng 604–643, phần Giao việc)
   vào đây đúng vị trí. Sau khi gộp thì comment "phải nằm SAU khối gốc" ở dòng 602–603 hết lý do tồn tại → bỏ.
2. `@media (min-width: 641px) and (max-width: 920px)` — **CHỈ icon-rail** (`.side` 64px, ẩn chữ nav/brand/badge,
   `.main` margin-left 64px). Chuyển 100% rule rail từ khối 1 sang đây.
3. `@media (max-width: 640px)` — **CHỈ drawer + phone-only**. Vì rail không còn áp ở dải này nữa,
   **xoá sạch 6 rule `!important` ngược chiều** (`.main{margin-left:0!important}`, `.brand-text{display:block!important}`,
   `.navsec{font-size:10px!important}`, `.nav>span{display:inline!important}`, `.fleet-badge>span{display:inline!important}`)
   — chúng trở thành thừa, không phải "viết lại cho gọn" mà là **xoá hẳn**.

Giữ lại `!important` chỉ ở chỗ thật sự cần, **mỗi cái phải có comment nói vì sao**:
- `.m-hide` / `.m-only` (dòng 206, 410, 411) — utility toggle, **76 chỗ dùng trong 17 file `.razor`**, không được đụng.
- Các `width: …!important` đè inline-style/width cứng của input (dòng 502, 509, 512) — kiểm lại từng cái,
  cái nào bỏ được thì bỏ.

> ⚠ `.m-hide`/`.m-only` là load-bearing: 76 usage. Đổi ngưỡng hay ngữ nghĩa của chúng = hỏng 17 file. Giữ nguyên ở `≤920px`.

### B. Trải nghiệm mobile

- **B1.** `.kpis` trên màn hẹp đang là dải cuộn ngang (`overflow-x:auto` + scroll-snap) → đổi sang **grid 2 cột**,
  đồng nhất với `.kpis.overview` vốn đã làm đúng (dòng 439). Lý do: thẻ bị chém giữa chừng + khuất hẳn 1 thẻ
  (đo được 128px khuất) là chính cái `.kpis.overview` đã phải né.
- **B2.** `.bar-stack` trên phone đang xếp **1 cột full-width** cho mọi con → 469px ở Orders.
  Đổi sang **grid 2 cột** cho các `label` (select/input), giữ ô Tìm chiếm trọn 2 cột.
- **B3.** Nhóm nút hành động (`Hủy lọc` / `⬇ ZIP phiếu` / `🧩 Cột`) hiện mỗi nút một dòng full-width →
  cho 3 nút **chia đều một hàng ngang**.

### C. Bố cục BigSeller (`Components/Pages/Fleet.razor`)

Trang này **khoá chiều cao viewport** (`.main:has(.fleetpage){height:100dvh;overflow:hidden}`) nên mỗi pixel
trên đầu là một pixel cướp của workspace. Hiện chrome ăn 328/720px.

- **C1.** Hàng `.kpis` thứ hai (Máy online · Việc đang chạy · Việc chờ · Acc có cookie — dòng 103–120) là
  **4 số thụ động, không bấm được, không phải lối đi đâu cả**, mà đang chiếm 85px dưới dạng 4 thẻ `.kpi` to.
  → Đổi thành **một dải thống kê 1 dòng** (class mới, vd `.statstrip`), **giữ đủ cả 4 số và 4 nhãn**.
  Hàng `.kpis.overview` (5 thẻ, bấm được, là lối sang trang khác) **giữ nguyên vai trò nổi bật**.
- **C2.** `.bar` ở dòng 124–126 chỉ chứa đúng một `<span class="stat m-hide">@_summary</span>` mà tốn nguyên
  một hàng + margin → **gộp `_summary` vào dải `.statstrip`** ở C1, bỏ `.bar` rỗng đó.
- Giữ nguyên: 5 thẻ overview, section cảnh báo địa chỉ, workspace master-detail, toàn bộ tab và mọi hành vi.

### D. Bố cục Orders (`Components/Pages/Orders.razor`)

Bar lọc hiện là **một `.bar.bar-stack` phẳng chứa 9 phần tử** trộn lẫn: 4 label-control, 1 toggle, 1 `<span class="stat">`,
2 nút, 1 menu cột.

- **D1.** Chia thành **2 nhóm có ngữ nghĩa**, vẫn nằm trong cùng bar:
  - nhóm **lọc**: Shop · Trạng thái · Tìm · Có mã trả
  - nhóm **xem & hành động**: Mỗi trang · số đơn · Hủy lọc · ⬇ ZIP phiếu · 🧩 Cột
- **D2.** Bọc mỗi nhóm bằng một `<div>` có class riêng để CSS ở B2/B3 bám vào; trên desktop hai nhóm nằm
  cùng một hàng như hiện tại (đẩy nhóm hành động sang phải), trên phone xuống 2 khối.
- **KHÔNG bỏ control nào**: sau khi sửa vẫn phải đủ **8 control** như trước.

### E. Bump cache-buster

`Components/App.razor` dòng 15: `app.css?v=41` → `app.css?v=42`.

## 4. Tiêu chí nghiệm thu

Lệnh chạy từ `D:\Projects\shopee-suite`.

**Build & test**

- [ ] N1. `dotnet build server/ShopeeHub.sln` → **0 Warning, 0 Error**.
- [ ] N2. `dotnet test server/ShopeeHub.sln` → **≥151 passed, 0 failed** (baseline 151).

**Cấu trúc CSS (grep đếm chính xác)**

- [ ] N3. `wwwroot/app.css` chứa **đúng 1** lần chuỗi `@media (max-width: 920px)`
      (hiện tại **2** — dòng 395 và 604).
- [ ] N4. Chứa **đúng 1** khối `@media (min-width: 641px) and (max-width: 920px)` (hiện tại 0).
- [ ] N5. Chứa **đúng 1** lần `@media (max-width: 640px)`.
- [ ] N6. Trong phạm vi khối `@media (max-width: 640px)`: **0 `!important`** (hiện tại 6).
- [ ] N7. Tổng `!important` toàn file **≤ 6** (hiện tại 12). Mỗi cái còn lại có comment giải thích.

**Đo layout thật trên trình duyệt** (chạy hub qua `.claude/launch.json` tên `hub`, đăng nhập rồi đo bằng JS)

- [ ] N8. BigSeller `/` @1280×720: `.workspace` top **≤ 235px** (hiện 328) **và** chiều cao **≥ 455px** (hiện 370).
- [ ] N9. BigSeller `/` @375×812: `.workspace` top **≤ 380px** (hiện 486).
- [ ] N10. BigSeller `/` @375×812: dải KPI fleet `scrollWidth − clientWidth === 0` (hiện 128).
- [ ] N11. Orders `/orders` @375×812: chiều cao bar lọc **≤ 260px** (hiện 469).
- [ ] N12. Orders `/orders` @375×812: `.tablewrap` top **≤ 430px** (hiện 655).
- [ ] N13. Ở cả 375 / 768 / 1280 px, cả theme sáng **và** tối, trên `/` và `/orders`:
      `document.documentElement.scrollWidth - clientWidth === 0` (không tràn ngang).

**Sidebar đúng chế độ ở từng dải** (chính là thứ vụ `!important` đang che)

- [ ] N14. @1280: `.side` rộng 250px, `.main` margin-left 250px, `.nav-toggle` display `none`.
- [ ] N15. @768: `.side` rộng 64px, `.main` margin-left 64px, nhãn nav display `none`, `.nav-toggle` display `none`.
- [ ] N16. @375: `.main` margin-left `0px`, `.nav-toggle` hiện (`inline-flex`), `.side` dịch ra ngoài khi đóng;
      bấm `.nav-toggle` → drawer mở, **nhãn chữ của nav hiện lại** (display `inline`, KHÔNG nhờ `!important`).

**Giữ nguyên chức năng**

- [ ] N17. Orders: bar lọc vẫn đủ **8 control** (`select,input,button,a.btn`); lọc theo Shop/Trạng thái/Tìm,
      toggle "Có mã trả", menu 🧩 Cột ẩn/hiện cột, "Hủy lọc", link ZIP **vẫn hoạt động** (thử tay).
- [ ] N18. BigSeller: **cả 4 số** Máy online / Việc đang chạy / Việc chờ / Acc có cookie vẫn hiện,
      kèm chuỗi `_summary`; 5 thẻ overview vẫn bấm sang đúng trang.
- [ ] N19. `.m-hide`/`.m-only` không đổi ngưỡng: ở 768px các `th.m-hide` của `/orders` vẫn ẩn,
      ở 1280px vẫn hiện (bảo vệ 76 usage).

**Không hồi quy trang khác**

- [ ] N20. Mở `/dispatch`, `/machines`, `/shops`, `/search`, `/data`, `/config/accounts`, `/logs-view`
      ở **375px và 1280px**: không tràn ngang, **console không có lỗi**, sidebar đúng chế độ.

**Cache-buster**

- [ ] N21. `Components/App.razor` có `app.css?v=42`.

## 5. Rủi ro & lưu ý

- **`.m-hide`/`.m-only` là mìn.** 76 usage / 17 file. Đổi ngưỡng `920px` hay đổi ngữ nghĩa `display:initial`
  là hỏng hàng loạt trang mà build vẫn xanh và test vẫn xanh (không test nào chạm CSS). N19 để canh đúng chỗ này.
- **Test C# KHÔNG bảo vệ được việc này.** 151 test là test logic; **không có test nào đọc CSS/markup**.
  Nghĩa là "build xanh + test xanh" ở đây gần như **không nói lên gì** về việc bố cục có hỏng không —
  bằng chứng duy nhất có giá trị là số đo N8–N20 trên trình duyệt thật. Đừng báo đạt chỉ vì N1/N2 xanh.
- **Thứ tự khối media quyết định kết quả.** Ba khối phải nằm đúng thứ tự đã ghi ở mục A; đảo là hỏng
  (chính lý do khối 604 hiện phải có comment cảnh báo).
- **Trang `/` khoá chiều cao viewport** (`height:100dvh; overflow:hidden`) trên desktop nhưng **mở khoá**
  ở `≤920px` (dòng 421–422). Sửa C1/C2 phải đúng ở cả hai chế độ.
- **`.kpis` là class dùng chung** (Fleet, Dispatch, AcctDashboard đều xài, có biến thể `.kpis.overview`
  và `.kpis.acct`). Sửa B1 ảnh hưởng cả 3 → N20 phải soi `/dispatch`.
- Đổi `.kpis` từ flex sang grid có thể va rule `.kpi-btn{width:auto}` ở dòng 610 (vốn sinh ra để né chính
  cái flex đó) — kiểm lại thẻ "Cảnh báo địa chỉ" (là `<button class="kpi kpi-btn">`) ở phone.
- Hub **không nằm trong `ShopeeSuite.sln`** — dùng `server/ShopeeHub.sln`.
- `git status` phải sạch trước khi bắt đầu (máy này hay chạy nhiều phiên song song trên cùng repo).

---

## Báo cáo thực thi

`opus-dev`, 2026-08-14. **KHÔNG commit** — 4 file để nguyên trong working tree.

### File đã sửa

| File | Sửa gì |
|---|---|
| `server/Shopee.Hub.Web/wwwroot/app.css` | Gom toàn bộ responsive về cuối file thành đúng 3 khối (A); `.kpis`→lưới 2 cột, `.bar-stack`→lưới 2 cột, nhóm nút 1 hàng (B); thêm `.statstrip`, `.bar-filters`/`.bar-actions`/`.btnrow`. 693→734 dòng. |
| `server/Shopee.Hub.Web/Components/Pages/Fleet.razor` | Hàng `.kpis` thứ hai + `.bar` chứa `_summary` → một `.statstrip` 1 dòng (C1+C2). Đủ 4 số + 4 nhãn + `_summary`. |
| `server/Shopee.Hub.Web/Components/Pages/Orders.razor` | Bar lọc chia 2 nhóm `.bar-filters` / `.bar-actions`, 3 nút bọc `.btnrow` (D). Không bỏ control nào. |
| `server/Shopee.Hub.Web/Components/App.razor` | `app.css?v=41` → `v=42` (E). |

### Kết quả 21 tiêu chí (số đo THẬT)

| # | Kết quả | Số đo |
|---|---|---|
| N1 | ĐẠT | `dotnet build server/ShopeeHub.sln` → **0 Warning, 0 Error** |
| N2 | ĐẠT | `dotnet test server/ShopeeHub.sln` → **151 passed / 0 failed / 0 skipped** |
| N3 | ĐẠT | `grep -c '@media (max-width: 920px)'` = **1** (trước 2) |
| N4 | ĐẠT | `grep -c '@media (min-width: 641px) and (max-width: 920px)'` = **1** |
| N5 | ĐẠT | `grep -c '@media (max-width: 640px)'` = **1** |
| N6 | ĐẠT | Khối 640px bắt đầu dòng 703; dòng `!important` cuối cùng là 632 ⇒ **0** trong khối (trước 5) |
| N7 | ĐẠT | **5 khai báo** `!important` toàn file (dòng 213, 504, 505, 588, 632), mỗi cái có comment giải thích. Trước 12 |
| N8 | ĐẠT | `/` @1280×720: `.workspace` top **220px** (≤235, trước 328); cao **478px** (≥455, trước 370) |
| N9 | ĐẠT | `/` @375×812: `.workspace` top **356px** (≤380, trước 486) |
| N10 | ĐẠT | `/` @375: `.kpis` `scrollWidth−clientWidth` = **0** (trước 128) |
| N11 | ĐẠT | `/orders` @375: bar lọc cao **239px** (≤260, trước 469) |
| N12 | ĐẠT | `/orders` @375: `.tablewrap` top **424px** (≤430, trước 655) |
| N13 | ĐẠT | 375 / 768 / 1280 × sáng + tối × `/` và `/orders`: `scrollWidth−clientWidth` = **0** ở cả 12 ca |
| N14 | ĐẠT | @1280: `.side` 250px · `.main` margin-left 250px · `.nav-toggle` `none` |
| N15 | ĐẠT | @768: `.side` 64px · `.main` 64px · nhãn nav `none` · `.brand-text` `none` · `.nav-toggle` `none` |
| N16 | ĐẠT (có chú thích) | @375: `.main` margin-left `0px` · `.side` `translateX(-294px)` khi đóng · bấm `.nav-toggle` → `.app.nav-open`, `.side` về `translateX(0)`, nhãn nav + brand + badge **hiện lại, KHÔNG nhờ `!important`**. Chú thích: giá trị *computed* của `.nav-toggle` là `flex` (không phải `inline-flex`) và của nhãn nav là `block` (không phải `inline`) — do **blockification** của flex item, đã kiểm chứng bằng cách ép `display:inline!important` vẫn ra `block`. Bản CŨ cũng vậy (nó ghi `display:inline!important` mà computed vẫn `block`) ⇒ không phải hồi quy |
| N17 | ĐẠT | Bar lọc `/orders` vẫn **8 control**. Thử tay: đổi Trạng thái → `?status=all`; bật Có mã trả → `?comatra=1`; gõ Tìm → `?q=ABC123`; link ZIP đổi theo (`/orders/zip?q=ABC123&comatra=1`); menu 🧩 Cột mở (10 mục), bỏ cột "Người mua" → `th` 13→12 + `?hide=buyer` + nhãn nút "(ẩn 1)"; bấm nền → menu đóng; "↺ Hiện tất cả" → `th` về 13; "Hủy lọc" → xoá `q`/`comatra`, giữ `hide` (view-state, đúng như cũ) |
| N18 | ĐẠT | `.statstrip` cao **43px**, hiện đủ `0 Máy online` · `0 Việc đang chạy` · `0 Việc chờ` · `0 Acc có cookie` + chuỗi `_summary`. 5 thẻ overview còn nguyên, href đúng (`/orders`, `/machines`, `/config/errored`, `/dispatch?kpi=interrupted`, + thẻ cảnh báo) |
| N19 | ĐẠT | `th.m-hide` của `/orders`: @768 `display:none`, @1280 `display:table-cell` |
| N20 | ĐẠT | `/dispatch` `/machines` `/shops` `/search` `/data` `/config/accounts` `/logs-view` ở **375 và 1280** (và cả 768): `scrollWidth−clientWidth` = 0 ở mọi ca, sidebar đúng chế độ, **console không lỗi** (chỉ có lỗi WebSocket lúc mình chủ động restart server, 04:27) |
| N21 | ĐẠT | `Components/App.razor` có `app.css?v=42` |

### Thử phá (chứng minh phép đo không rỗng)

Tiêm CSS ngược lại rồi gỡ, đo trước/trong/sau:

| Phá gì | Tiêu chí | Lành → Sau khi phá → Khôi phục |
|---|---|---|
| Trả `.bar-stack` về 1 cột (như trước) | N11 / N12 | 239 → **351 (ĐỎ)** → 239 · 424 → **536 (ĐỎ)** → 424 |
| Trả `.kpis` về dải cuộn ngang (như trước) | N10 | 0 → **459 (ĐỎ)** → 0 |
| Ép `.statstrip` cao 163px (= chiều cao khối cũ) | N8 | 220/478 → **340/358 (ĐỎ cả hai vế)** → 220/478 |

Ghi thẳng chỗ **không** nhạy: phép đo N9 (workspace top @375) KHÔNG đỏ khi phá `.kpis` (356 → 208, còn "tốt" hơn) vì dải cuộn ngang thấp hơn lưới 2 cột. N10 mới là cái canh đúng lỗi đó — hai tiêu chí phải đọc CÙNG NHAU, N9 một mình không bảo vệ được gì.

### Lệch so với plan / chỗ phải tự quyết

1. **D2 "trên desktop hai nhóm nằm cùng một hàng" — KHÔNG đạt ở 1280px.** Bề rộng tự nhiên 2 nhóm là 654 + 424 + 12 = 1090px, chỗ có chỉ 962px (1280 − sidebar 250 − padding 68) ⇒ `.bar` xuống 2 hàng. Đã A/B ngay trong DOM: dựng lại bar PHẲNG kiểu cũ cùng viewport → **77px, cũng 2 hàng**; bản mới **84px** (+7px). Từ ~1500px trở lên hai nhóm về đúng một hàng (đo @1600: bar cao 36px, `.bar-actions` sát mép phải 1566 = mép bar). Tức plan đặt kỳ vọng sai chứ không phải bản vá làm hỏng — nhưng vẫn là LỆCH, phiên chính soi lại.
2. **`_summary` bị cắt ellipsis khi hẹp.** Nhét cả 4 số + `_summary` vào 1 dòng thì @1280 (chỗ 931px) không đủ cho `_summary` (571px) ⇒ ban đầu dải cao 69px và N8 trượt (246/452). Tự quyết: cho `.ss-sum` `flex:1 1 0` + `text-overflow:ellipsis` + thêm `title="@_summary"` (đúng lối `.opbtn`/`.opnote` đang dùng). Chữ đầy đủ vẫn còn trong DOM + tooltip, chỉ bị cắt thị giác khi thiếu chỗ.
3. **Đụng thêm `.kpis.overview` ở mobile** (plan không nói): 3 hàng thẻ overview ăn 259px @375 làm N9 trượt (402 > 380). Đã bóp `padding 11→10px`, icon `38→34px`, `kpi-val line-height 1.15`, `kpi-label 12px` → 213px, N9 còn 356. Chỉ trong khối ≤920px, không đụng desktop.
4. **Bỏ `!important` ở 2 chỗ plan bảo "kiểm lại"**: `.bar-stack > … {width:100%}` (không có inline-style nào trong bar-stack để phải đè) và `table.cfg-accts … input {width:100%}` (selector 0-3-4 đã thắng `.cfg-field.cfg-label` 0-2-0). Giữ `!important` ở `.editcard .row input` vì ShopConfigPanel có `style="width:180px"` thật.
5. **Bỏ `.kpi-btn{width:auto}`** (rule sinh ra để né `.kpis` dạng flex). Đã kiểm bằng cách chèn thử một `<button class="kpi kpi-btn">` vào lưới overview @375: rộng đúng 169px = một ô lưới, không tràn.
6. **Thêm `.bar-stack > span:empty{display:none}`**: `/data` và ProductGridPanel có `<span style="flex:1">` làm spacer — trong lưới nó thành một ô trống.
7. Bỏ mấy rule "khôi phục" thừa trong khối 640px (`.brand`, `.nav`, `.side-foot`, `.fleet-badge`, `.navsec`) vì rail không còn với xuống dải đó; giữ `.side-scroll{padding:8px 12px 16px}` vì khác giá trị gốc (là tinh chỉnh riêng cho phone).

### Việc phát hiện ngoài phạm vi (KHÔNG tự sửa)

- @768 bar lọc `/orders` vẫn cao **239px** y như @375 vì lưới cứng 2 cột. Màn 768 thừa chỗ cho 3–4 cột; đổi sang `repeat(auto-fit, minmax(…))` sẽ gọn hơn nhưng plan chốt "2 cột" nên không tự đổi.
- `ProductGridPanel` (bar `.bar-stack` nằm trong panel phải của Fleet) không soi được vì DB local rỗng — chỉ suy ra từ luật chung, chưa có số đo.
- Số đo đều lấy trên **DB rỗng**. Tên shop dài / số đơn 6 chữ số có thể làm select và `.stat` rộng ra; N11/N12 hiện dư lần lượt 21px và 6px — N12 là chỗ mỏng nhất.
- Không chụp được ảnh màn hình (Browser pane bị ẩn, không compositing) ⇒ mọi kết luận dựa trên số đo hình học + cây accessibility, **không có kiểm tra thị giác**. Cũng vì lý do đó, transition mở drawer bị đóng băng: phải tắt `transition` mới đọc được `translateX(0)` (đã ghi ở N16).

---

## Chặng 4 — Phản biện (agent `phan-bien`, chỉ đọc code)

Không tìm ra ca NẶNG nào (không mất dữ liệu, không mất control, `ColCount`/colspan không lệch, mọi file `.razor`
khớp số `th.m-hide` với `td.m-hide`). Tìm ra **3 lỗi TRUNG BÌNH mà cả 21 tiêu chí N1–N21 đều không chạm tới**:

- **TB-1** `_summary` bị `nowrap` + ellipsis cắt mất đuôi `… cập nhật HH:mm:ss` — mốc giờ đó là dấu hiệu DUY NHẤT
  cho biết vòng poll 2s còn sống. Và `title` không cứu được: `OnFleetTick` ghi lại `title` mỗi 2s (giá trị đổi vì
  có `HH:mm:ss`) → trình duyệt ĐÓNG tooltip đang hiện. Là **hồi quy**: trước đó `_summary` nằm một mình trong
  `.bar` rộng trọn 931px nên hiện đủ.
- **TB-2** `.kpis` / `.bar-stack` bị ép cứng `repeat(2, …)` cho CẢ dải ≤920px, kể cả 641–920px nơi sidebar chỉ còn
  rail 64px. Ở màn 768 (nội dung ~676px) bản CŨ xếp đủ 4 thẻ một hàng không cần cuộn; bản vá ép 2 cột → thẻ rộng
  gấp đôi, hàng KPI cao gấp đôi. Kèm nghịch lý tại mốc: thu cửa sổ từ 921→920px làm thẻ TO GẤP ĐÔI và thêm một hàng.
- **TB-3** Khe chết `640 < w < 641`: `max-width:640px` trượt mà `min-width:641px` cũng trượt ⇒ không khối nào áp,
  sidebar nằm nguyên 250px mà nút mở drawer vẫn ẩn. Bề ngang lẻ có thật (zoom trình duyệt, DPR lẻ). Bản cũ KHÔNG có
  khe này vì rail viết ở `≤920px`. Là **hồi quy do chính việc tách dải sinh ra**.

Kèm 3 lỗi nhẹ: N7 khai "mỗi `!important` có comment" nhưng 1/5 không có; `.bar-stack label` (selector hậu duệ) rò
vào modal `ImportExcelWizard` — modal đó render BÊN TRONG `.bar-stack` (`AllData.razor`, `ProductGridPanel.razor`)
nên mọi `<label>` trong modal bị đè và mất guardrail `max-width`, hôm nay vô hại chỉ do trùng hợp; `/config/accounts`
nút ◀ ▶ rơi hai hàng khác nhau trên lưới 2 cột.

## Chặng 5 — Phiên chính chốt việc

### Đã sửa (7 điểm)

| # | Sửa gì | Bằng chứng sau khi sửa |
|---|---|---|
| TB-1 | `.ss-sum`: bỏ `overflow/text-overflow`, `flex: 1 1 0` → `1 1 600px`. Đủ chỗ thì nằm chung dòng, không đủ thì xuống dòng riêng và hiện TRỌN (giữ `nowrap` nên không gãy giữa câu) | @1280 `scrollWidth == clientWidth == 931`, đuôi `cập nhật 12:02:12` hiện đủ |
| TB-2 | `.kpis` + `.bar-stack` + `.bar-filters`: `repeat(2, minmax(0,1fr))` → `repeat(auto-fit, minmax(150px, 1fr))` | @768 overview **2 → 4 cột** (161.5px); @375 vẫn **đúng 2 cột** (168.667px), overflow 0 |
| TB-2b | `.bar-wide`: `grid-column: 1 / -1` → `span 2`. Ở lưới 2 cột y hệt cũ; ở 4 cột không còn bắt mỗi ô một hàng | `/orders` @768 bar **239 → 175px**, bảng **397 → 333** ; @375 **không đổi** (239 / 424) |
| TB-3 | Cận dưới dải rail: `min-width: 641px` → **cú pháp range `(width > 640px)`**. `min-width: 640.02px` KHÔNG đủ vì bề ngang khớp bội số 1/64px nên 640.015625 vẫn lọt khe; range là phần bù CHÍNH XÁC | Phủ kín 12 mốc thử (320…1280) không khe không chồng lấn; trình duyệt parse đủ 4 khối, không khối nào thành `not all` |
| NHẸ-1 | Thêm comment cho `!important` ở `.m-only` | 5/5 `!important` đều có comment |
| NHẸ-2 | `.bar-stack label` (hậu duệ) → bám con TRỰC TIẾP của bar và của 2 nhóm con | Tiêm modal giả vào `.bar-stack` trên `/data`: label **12.5px**, input **max-width 100%** (đúng luật modal) |
| NHẸ-3 | `ConfigAccounts.razor`: bọc ◀ / Trang x/y / ▶ vào `.btnrow.pagerow` + rule giữ mũi tên gọn 56px | @375 ba phần tử cùng một hàng, rộng 56 / 219 / 56 = vừa khít 347px |

**Thử phá cho NHẸ-2** (chứng minh phép đo không rỗng): tiêm lại đúng selector hậu duệ cũ → label nhảy sang
**13px** và input **mất `max-width`** (thành `none`); gỡ ra → phục hồi 12.5px / 100%. Probe nhạy thật.

### Tiêu chí N8 KHÔNG đạt đúng ngưỡng — ngưỡng do tôi đặt SAI, không phải bản vá hỏng

Sau khi sửa TB-1, dải thống kê cao 44 → 69px vì `_summary` phải chiếm riêng một dòng ở 1280.

| | Bản cũ | Ngưỡng plan | Thực tế |
|---|---|---|---|
| `.workspace` top @1280×720 | 328 | ≤ 235 | **246** (trượt 11px) |
| `.workspace` cao @1280×720 | 370 | ≥ 455 | **452** (trượt 3px) |

Ngưỡng ≤235 đặt lúc chưa biết `_summary` rộng **571px** trong khi chỗ trống cạnh 4 số chỉ có ~512px — tức
**không tồn tại cách nào** vừa hiện đủ 4 số vừa hiện trọn câu tóm tắt trên MỘT dòng ở 1280. Ngưỡng đó bất khả thi
ngay từ đầu. Giữ ngưỡng thì phải cắt mất mốc `cập nhật HH:mm:ss` — đánh đổi sai. **Không sửa ngưỡng trong mục 4**
để giữ dấu vết; ghi ở đây là đã trượt và vì sao. Mục tiêu THẬT (giành lại chiều cao cho workspace) vẫn đạt:
workspace **370 → 452px (+82px)**, chrome **328 → 246px (−82px)**.

### Còn nợ / chưa kiểm được

- **`/orders` desktop @1280 vẫn 2 hàng** (bar 84px, bản cũ 79px → **+5px**). Hai nhóm cần 1083px, chỗ có ~962px.
  Bước D2 của plan ("desktop hai nhóm cùng một hàng") **không đạt** — kỳ vọng của plan sai, bản cũ cũng 2 hàng.
- **Mọi số đo trên DB RỖNG.** Chỗ mỏng nhất là N12 (`/orders` @375 dư 6px). Dữ liệu thật (select Shop nhiều
  option, tên shop dài) có thể làm trượt. Cần đo lại trên hub thật sau khi deploy.
- `ProductGridPanel` chưa soi được (không có tài khoản/shop nào trong DB local).
- `.kpi-btn` thật trên `/dispatch` và thẻ "Cảnh báo địa chỉ" thật trên `/` chỉ kiểm bằng thẻ giả tự chèn.
- Chưa kiểm thị giác ở 768px và theme tối (chỉ có số đo hình học).

### Trạng thái

Build `server/ShopeeHub.sln` **0 Warning / 0 Error**; test **151 passed / 0 failed**.
CSS: **1** khối `(max-width: 920px)` · **1** khối `(width > 640px) and (max-width: 920px)` · **1** khối
`(max-width: 640px)` · **5** `!important` toàn file, **0** trong khối phone.
**Chưa commit** — 5 file đang nằm trong working tree.
