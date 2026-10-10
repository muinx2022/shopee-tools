# Plan: Trình bày lại trang BigSeller ("/")

- **Ngày:** 2026-08-14
- **Trạng thái:** đã thực thi xong (chờ nghiệm thu + phản biện) — KHÔNG commit
- **Người lập:** phiên chính · **Người thực thi:** `opus-dev`

## 1. Bối cảnh & mục tiêu

User gửi ảnh chụp trang BigSeller trên hub THẬT (7 tài khoản × 3 shop, 9 máy) và nói **"trình bày lại phần
bigseller"**. Đã chốt qua hỏi đáp:

- **Phạm vi: LÀM LẠI CẢ TRANG**, trọng tâm **danh sách trái** và **panel phải**.
- **Trang này phục vụ CẢ 4 mục đích** (user chọn hết): ① liếc xem shop nào đang kẹt/lỗi · ② sửa cấu hình
  acc/shop · ③ soi số liệu tiến độ từng shop · ④ đặt tay trạng thái / chạy rewrite.
  ⇒ Không được tối ưu cho một mục đích mà hy sinh ba cái kia.

### Hiện trạng đã ĐO trên DỮ LIỆU GIỐNG HUB THẬT

Ba đợt trước đều hỏng vì đo trên DB rỗng. Lần này local hub đã được seed **đúng hình dạng ảnh user gửi**:
7 acc (email dài thật) × 3 shop = 21 shop, 9 máy, ledger trộn lẫn xong/dừng-dở/chưa chạy.

Cách seed lại nếu mất: `scratchpad/bigseller.json` → `PUT /files/config/bigseller.json`, rồi
`bash scratchpad/seed-fleet.sh <token>`. Token `7B4497B89CC944239AA4FD5B8B9A326F6A10FF8F82146F76`,
hub `http://127.0.0.1:5099` (config `hub` trong `.claude/launch.json`), admin `admin` / `localdev-throwaway-1234`.

Số đo tại **1280×800**:

| Chỗ đo | Hiện trạng |
|---|---|
| Chrome phía trên `.workspace` | **247px (31% viewport)** |
| `.workspace` | 531px |
| Hàng 5 thẻ KPI | 68px |
| Dải `.statstrip` | 70px |
| Danh sách trái: **cần** chiều cao | **1255px** |
| Danh sách trái: **có** chiều cao | **529px** ⇒ phải cuộn **726px**, chỉ thấy **42%** |
| Dòng tài khoản **gãy 2 dòng** | **5/7** (53px thay vì ~31px) |
| Chiều cao TB dòng acc / dòng shop | 47px / 32px |
| Panel phải: 4 thẻ số | 70px |
| Panel phải: bảng shop | 232px (3 shop + hàng Tổng) |

### Bốn chỗ TRÙNG LẶP đo được

1. **Số máy hiện 3 lần trong 2 hàng liền nhau**: thẻ KPI `0/9 Máy offline` · dải `0 Máy online` ·
   câu tóm tắt `… 9 máy …`.
2. `0 Việc đang chạy` (dải) ↔ `0 đang chạy` (câu tóm tắt, ngay dòng dưới).
3. `0 Việc chờ` (dải) ↔ `0 chờ` (câu tóm tắt).
4. **Panel phải**: 4 thẻ số (`AcctDashboard.razor:5-16`) hiện `Rows` của từng op, mà **hàng `Tổng` cuối bảng**
   (`:52-59`) tính đúng cùng phép cộng đó ⇒ 4 con số y hệt, cách nhau 250px.

> Trùng lặp 1–3 là do đợt trước CHÍNH TÔI gộp hàng KPI thứ hai và câu `_summary` vào một dải, mà lúc đó đo
> trên DB rỗng nên mọi số đều là 0 — không nhận ra chúng lặp nhau.

### LỖI LOGIC phát hiện kèm (không phải chuyện trình bày)

`"Máy online"` dùng `FleetStateService.IsOnline` = nhịp < **45 giây** (`FleetStateService.cs:98,101`).
`"Máy offline"` dùng `FleetStateService.MachineOffline` = nhịp ≥ **180 giây** (`:166-171`).

⇒ Máy có nhịp cuối trong khoảng **45–180 giây** thì **không được tính là online, cũng không tính là offline**.
Đã tái hiện: màn hình hiện đồng thời `0/9 Máy offline` (⇒ 9 máy online) và `0 Máy online`. Hai con số trên
cùng một màn nói ngược nhau.

`Summarize` / `FleetSummary` **KHÔNG có test nào** che (grep test source = 0 hit).

## 2. Phạm vi

**Làm:**

- A. Dải thống kê: bỏ trùng lặp với hàng KPI và với câu tóm tắt.
- B. Danh sách trái (`WorkspaceShopList.razor`): hết gãy chữ, gọn dòng, thấy được nhiều shop hơn, lọc theo trạng thái.
- C. Panel phải (`AcctDashboard.razor`): bỏ trùng 4 thẻ ↔ hàng Tổng.
- D. Bump `app.css?v=43` → `v=44`.

**Không làm:**

- KHÔNG đổi ngưỡng `IsOnline`/`MachineOffline` (dùng chung với sidebar badge + trang `/machines` +
  `MachineOfflineWatch` có test) — chỉ **gỡ mâu thuẫn TRÊN TRANG NÀY**, xem A2. Việc thống nhất ngưỡng
  là quyết định của user, ghi vào mục "còn nợ".
- KHÔNG bỏ dữ liệu/chức năng nào: 4 tab, đặt-tay ledger, rewrite, cấu hình acc/shop, lối sang Giao việc,
  section cảnh báo địa chỉ — giữ nguyên hành vi.
- KHÔNG đụng trang khác, KHÔNG đụng `Orders.razor` (vừa làm xong), KHÔNG commit, KHÔNG deploy.

## 3. Các bước thực hiện

### A. Dải thống kê (`Fleet.razor` + `FleetRowsBuilder.cs`)

- **A1.** Câu `_summary` đang lặp `đang chạy`/`chờ`/`máy` với các mục ngay cạnh nó. Bỏ ba cụm đó khỏi chuỗi
  trong `FleetRowsBuilder.Summarize` (`FleetRowsBuilder.cs:144-145`), giữ lại phần KHÔNG lặp:
  `dừng/lỗi · scrape · import · update · tên SP · cập nhật HH:mm:ss`.
  (Đây là sửa CHUỖI HIỂN THỊ trong builder, không đụng luật tính. Không test nào che — phải tự thử phá.)
- **A2.** Bỏ mục `Máy online` khỏi dải: thẻ KPI `N/9 Máy offline` ngay trên đã nói đúng việc đó, mà lại
  dùng ngưỡng khác ⇒ giữ cả hai là để hai con số cãi nhau trên cùng màn hình. Bỏ mục ở dải là **hết mâu
  thuẫn mà không phải đụng ngưỡng dùng chung**.
- **A3.** `_kMachines` sau đó không còn chỗ dùng → gỡ khỏi `Fleet.razor.cs` nếu thành code chết (kiểm grep).

### B. Danh sách trái (`Components/Shared/WorkspaceShopList.razor`)

Đây là phần user kêu nhiều nhất. Ba việc:

- **B1. Hết gãy chữ.** Dòng acc hiện `@g.AccountName <span class="cnt">N shop</span>` cùng một dòng chảy tự do
  ⇒ email dài đẩy `N shop` xuống dòng (5/7 dòng bị). Sửa: tên acc `flex:1 min-width:0` + `text-overflow:
  ellipsis` + `title` mang tên đầy đủ; `N shop` thành badge `flex-shrink:0`.
- **B2. Gọn dòng.** Dòng acc 47px → mục tiêu ≤ 34px; dòng shop 32px → ≤ 28px. Giữ nguyên glyph trạng thái,
  badge "chạy", viền trái xanh của shop đang chạy (đó là thứ phục vụ mục đích ① của user).
- **B3. Lọc theo trạng thái.** Thêm hàng chip trên ô tìm: `Tất cả · Đang chạy · Dừng/lỗi · Chưa xong`.
  Lọc THUẦN CLIENT trên danh sách đã dựng (không đọc thêm DB). Dùng lại `.chip` sẵn có của trang Giao việc.
  Trạng thái chip vào URL (`?st=`) như mọi view-state khác của trang. Mặc định `Tất cả` (vắng khỏi URL).
  Đây là thứ phục vụ mục đích ① mà không phải giấu bớt gì.

> Vì sao KHÔNG thu gọn nhóm acc (collapse): thu gọn thì shop bị giấu, mà "liếc thấy shop nào đang kẹt" là
> mục đích số ① của user. Chip lọc đạt cùng mục tiêu mà không giấu gì khi ở chế độ mặc định.

### C. Panel phải (`Components/Shared/AcctDashboard.razor`)

- **C1.** Bỏ hẳn khối `<div class="kpis acct">` (4 thẻ, 70px) — `Rows` của nó trùng y hệt hàng `Tổng` cuối bảng.
- **C2.** Phần KHÔNG trùng của 4 thẻ là tỉ lệ `Done/Total shop ✓` → đưa vào `th` của bảng:
  `SCRAPE (1/3 ✓)`. Không mất thông tin nào.
- **C3.** Giữ nguyên: dòng `busyline` việc đang chạy, dòng khoá acc, bảng shop bấm được, nút Giao việc.

### D. Bump cache-buster

`Components/App.razor`: `app.css?v=43` → `v=44`.

## 4. Tiêu chí nghiệm thu

**Mọi tiêu chí layout phải đo trên DB ĐÃ SEED** (7 acc × 21 shop × 9 máy). Đo trên DB rỗng là xanh giả —
đó là lỗi đã làm hỏng ba đợt liên tiếp.

**Build & test**
- [ ] P1. `dotnet build server/ShopeeHub.sln` → 0 Warning, 0 Error.
- [ ] P2. `dotnet test server/ShopeeHub.sln` → ≥151 passed, 0 failed.

**Hết trùng lặp**
- [ ] P3. Trên toàn trang, số máy chỉ xuất hiện **một lần** (ở thẻ KPI "Máy offline"). Dải thống kê không còn
      mục "Máy online"; câu tóm tắt không còn cụm "N máy".
- [ ] P4. Câu tóm tắt không còn cụm `đang chạy` và `chờ` (đã có mục riêng ngay cạnh).
- [ ] P5. Panel phải: `document.querySelectorAll('.kpis.acct')` = **0**; hàng `Tổng` vẫn còn và vẫn đúng số;
      `th` của 4 cột op mang tỉ lệ `N/M ✓`.

**Danh sách trái gọn lại (đo @1280×800, 7 acc × 21 shop)**
- [ ] P6. **0/7** dòng acc bị gãy 2 dòng (hiện **5/7**). Tên acc dài bị cắt ellipsis và có `title` đủ.
- [ ] P7. Chiều cao dòng acc **≤ 34px** (hiện 47), dòng shop **≤ 28px** (hiện 32).
- [ ] P8. `wslist.scrollHeight` **≤ 900px** (hiện **1255**) — tức thấy được ≥ 58% danh sách thay vì 42%.
- [ ] P9. Chip lọc: `Dừng/lỗi` → chỉ còn acc/shop có op dừng-lỗi; `Đang chạy` → chỉ shop đang chạy;
      `Tất cả` → về đủ 7 acc + 21 shop. Chip đi vào URL, F5 giữ nguyên.
- [ ] P10. Ô tìm vẫn hoạt động và **kết hợp được** với chip (tìm + lọc cùng lúc).

**Chiều cao giành lại**
- [ ] P11. `.workspace` top **≤ 215px** (hiện 247) và cao **≥ 560px** (hiện 531) @1280×800.

**Giữ nguyên chức năng — phần dễ hỏng âm thầm**
- [ ] P12. Chọn acc → panel phải hiện dashboard; chọn shop → 4 tab (Hành động · Thống kê · Dữ liệu · Cấu hình)
      đều mở được; `?acc=&shop=&tab=` vào URL, F5 giữ nguyên lựa chọn.
- [ ] P13. Đặt-tay ledger (combo ⋯ / ✓ Xong / Chưa / ■ Dừng) trên tab Hành động vẫn chạy, vẫn nhảy `_cellsetGen`.
- [ ] P14. Nút "🎯 Giao việc cho shop này" / "cho tài khoản này" vẫn dẫn đúng link `?acct=&q=&f=all`.
- [ ] P15. Section cảnh báo địa chỉ + nút "✓ Đã xử lý" + 5 thẻ KPI bấm sang đúng trang — không hồi quy.
- [ ] P16. Shop đang chạy vẫn nổi bật ở danh sách trái (nền xanh, chấm nhấp nháy, badge "chạy").
- [ ] P17. "+ Thêm tài khoản" vẫn tạo acc mới và nhảy sang tab Cấu hình.

**Responsive & không hồi quy**
- [ ] P18. 375 / 768 / 1280 × sáng+tối: không tràn ngang, console không lỗi.
- [ ] P19. `/orders` (vừa làm đợt trước) không hồi quy: bảng 5 cột nhóm vẫn đúng, không tràn @1280 và @921.
- [ ] P20. `/dispatch` dùng chung `.chip` và `.kpis` — mở ở 375 và 1280, không tràn, không lỗi.
- [ ] P21. `Components/App.razor` có `app.css?v=44`.

## 5. Rủi ro & lưu ý

- **Ba đợt liên tiếp hỏng vì đo trên dữ liệu giả.** Lần này DB local đã giống hub thật — dùng nó, và nếu
  seed bị mất thì seed lại theo hướng dẫn ở mục 1 TRƯỚC khi đo.
- **151 test C# không có test nào chạm CSS/markup**, và `Summarize`/`FleetSummary` **không có test nào cả**.
  A1 sửa chuỗi trong `Summarize` ⇒ **bắt buộc thử phá**: sửa ngược cho hỏng, xác nhận phép đo đỏ, rồi khôi phục.
- **`.chip` và `.kpis` là class dùng chung với `/dispatch`** — B3 tái dùng `.chip` thì phải kiểm `/dispatch` (P20).
- **`FleetRowsBuilder.Filter`** hiện lọc theo tên (acc hoặc shop). B3 thêm lọc trạng thái phải **kết hợp** với
  lọc tên chứ không thay thế (P10). Chú ý ca "acc khớp tên nhưng không shop nào khớp trạng thái".
- **Trang `/` khoá chiều cao viewport** trên desktop (`.main:has(.fleetpage){height:100dvh;overflow:hidden}`)
  nhưng **mở khoá ở ≤920px**. Mọi thay đổi chiều cao phải đúng ở cả hai chế độ.
- **`OpCell` được gọi rất nhiều lần** (mỗi shop × mỗi op, mỗi nhịp 2s). Lọc theo trạng thái ở B3 sẽ gọi thêm —
  đừng gọi lồng trong vòng lặp render nhiều lần cho cùng một ô; tính một lượt rồi dùng lại.
- `git status` sạch trước khi bắt đầu.

---

## Báo cáo thực thi

**Người thực thi:** `opus-dev` · **Ngày:** 2026-08-14 · **KHÔNG commit** (cây làm việc còn nguyên 7 file `M`).

### File đã sửa (7)

| File | Sửa gì |
|---|---|
| `Components/FleetRowsBuilder.cs` | **A1** bỏ `đang chạy`/`chờ`/`N máy` khỏi chuỗi `Summarize`. **B3** thêm `FleetShopState` (record struct gộp Running/Stopped/AllDone) + `StateOf` + `StateChips` + `NormalizeState` + `FilterByState`. |
| `Components/Pages/Fleet.razor` | **A2** bỏ mục `Máy online` khỏi `.statstrip`; truyền `State`/`StateChanged` xuống danh sách trái; viết lại comment khối. |
| `Components/Pages/Fleet.razor.cs` | **A3** gỡ `_kMachines`. **B3** thêm `_state`, `OnStateChanged`, đọc/ghi `?st=`, `Filtered()` xếp chồng lọc tên → lọc trạng thái, `ListEmptyText` thêm 2 ca mới. |
| `Components/Shared/WorkspaceShopList.razor` | **B1** dòng acc thành flex + `.nm` ellipsis + `title`, badge `.cnt` không co. **B2** dòng shop bọc `.nm`. **B3** hàng chip trong khối `.wslist-head` dính. `GlyphOf` nhận sẵn `FleetShopState`, bỏ `IsRunningShop` ⇒ **1 lượt `OpCell`/shop thay vì 2**. |
| `Components/Shared/AcctDashboard.razor` | **C1** bỏ hẳn `<div class="kpis acct">`. **C2** tỉ lệ `N/M ✓` vào `th`; ở ≤920px (thead bị `stack-sm` giấu) hiện lại ở hàng `Tổng` qua `.m-only`. `AcctOpSummary` → `AcctOpDone` (bỏ `Rows` đã thành dead compute). |
| `wwwroot/app.css` | Mật độ `.wslist`/`.wsacct`/`.wsshop`; `.wslist-head` dính; `.chips.wslist-chips`; `.thsub`; **xoá 3 dòng `.kpis.acct` chết** + gỡ khỏi selector media; `.content:has(.fleetpage)` `padding-top:20px`; margin `.kpis.overview`/`.statstrip` 12→10. |
| `Components/App.razor` | **D** `app.css?v=43` → `v=44`. |

### Build & test (cây SẠCH, chỉ 7 file của việc này)

- `dotnet build server/ShopeeHub.sln --no-incremental` → **0 Warning, 0 Error**.
- `dotnet test server/ShopeeHub.sln` → **151 passed, 0 failed, 0 skipped**.

### Chấm tiêu chí — số đo THẬT @1280×800, DB đã seed (7 acc · 21 shop · 9 máy · 2 lease tươi)

| # | Kết quả | Số đo |
|---|---|---|
| P1 | ĐẠT | 0 Warning / 0 Error |
| P2 | ĐẠT | 151 passed / 0 failed |
| P3 | ĐẠT | chữ "máy" xuất hiện **1 lần** trên cả trang (thẻ "Máy offline"); dải còn `Việc đang chạy · Việc chờ · Acc có cookie`; câu tóm tắt hết "N máy" |
| P4 | ĐẠT | câu tóm tắt = `16 dừng/lỗi · 0 scrape · 0 import · 0 update · 0 tên SP · cập nhật HH:mm:ss` — hết "đang chạy"/"chờ" |
| P5 | ĐẠT | `.kpis.acct` = **0**; `th` = `Scrape 1/3 ✓ · Import 2/3 ✓ · Update 1/3 ✓ · Tên SP 0/3 ✓`; hàng `Tổng` còn + **đúng số** (seed 120+80 dòng → `Tổng` scrape = **200** trong khi `th` = 1/3 ⇒ hai con số KHÁC nhau, hết trùng) |
| P6 | ĐẠT | gãy dòng **0/7** (trước 5/7); 5 tên dài bị cắt ellipsis; **7/7** có `title` đủ |
| P7 | ĐẠT | dòng acc max **24,9px** (≤34, trước 47) · dòng shop max **22,9px** (≤28, trước 32) |
| P8 | ĐẠT | `scrollHeight` **863px** (≤900, trước 1261) · thấy **65%** (≥58%, trước 42%) |
| P9 | ĐẠT | Đang chạy → 2 shop/2 acc · Dừng/lỗi → 12 shop/5 acc · Chưa xong → 17 shop/6 acc · Tất cả → 21/7. `?st=run\|err\|todo`, F5 giữ nguyên; `?st=` giá trị lạ → về Tất cả |
| P10 | ĐẠT | `store`+Dừng/lỗi → 10 shop/4 acc (12 − 2 shop không chứa "store") · **`andre`+Dừng/lỗi → 0** (acc khớp TÊN nhưng không shop nào dừng/lỗi ⇒ nhóm rụng hẳn, không hiện nhóm rỗng) |
| P11 | ĐẠT | `.workspace` top **211,8px** (≤215, trước 247) · cao **566,3px** (≥560, trước 531) |
| P12 | ĐẠT | chọn acc → dashboard; chọn shop → đủ 4 tab, mở được hết; `?acc=&shop=&tab=` đúng, F5 giữ nguyên |
| P13 | ĐẠT | đặt `✓ Xong` → pill đổi `■ dừng dở`→`✓ xong`, combo tự về `⋯` (`_cellsetGen` nhảy); đặt lại `■ Dừng` về đúng cũ |
| P14 | ĐẠT | shop → `/dispatch?acct=acc1&q=babysak.store&f=all` · acc → `/dispatch?acct=acc3` |
| P15 | ĐẠT | seed 2 cảnh báo → section hiện 2 dòng; `✓ Đã xử lý` → ConfirmDialog → đóng OK, số 2→1→0, có dòng xác nhận `rev`; 5 thẻ KPI đúng href |
| P16 | ĐẠT | `Cicily Store`, `fflqam22bl` có `.run` + `.rundot` + badge "chạy" |
| P17 | ĐẠT | acc 7→8, nhảy tab `⚙ Cấu hình`, h2 = "Tài khoản mới" (đã khôi phục config sau khi đo) |
| P18 | ĐẠT | 375 · 768 · 921 · 1280 × sáng+tối: `docOverflowX = 0`, không phần tử nào tràn ngoài vùng cuộn, **console 0 lỗi**. Tối: chip bật `#93a1ff` trên `rgba(60,80,224,.22)` — đúng nhánh `html.dark` |
| P19 | ĐẠT | `/orders` 5 cột nhóm nguyên vẹn, tràn **0px** ở cả 1280 và 921 |
| P20 | ĐẠT | `/dispatch` @375 + @1280: chip vẫn `padding 5px 12px · 12.5px · flex-wrap:wrap` (override `.wslist-chips` KHÔNG rò), 0 tràn, 0 lỗi |
| P21 | ĐẠT | `app.css?v=44` |

### Thử phá (4 lượt — có A1 bắt buộc)

| # | Phá gì | Kết quả |
|---|---|---|
| 1 (**A1**) | Trả `{running} đang chạy · {queued} chờ · {N} máy ·` vào `Summarize` | **P3 + P4 ĐỎ** — chữ "máy" tăng 1→2, câu tóm tắt lại chứa "đang chạy"/"chờ" |
| 2 (C1) | Dựng lại khối `<div class="kpis acct">` | **P5 ĐỎ** — `.kpis.acct` = 1 |
| 3 (B3) | `Filtered()` bỏ lớp `Filter` theo tên, chỉ còn `FilterByState` | **P10 ĐỎ** — `andre`+Dừng/lỗi ra **12 shop/5 acc** thay vì 0 (chip nuốt ô tìm) |
| 4 (B1) | Trả dòng acc về chảy chữ tự do (`display:block`, `.nm` `white-space:normal`) | **P6 + P7 + P8 ĐỎ** — gãy **5/7**, cao max 41,8px, `scrollHeight` **949px** |

Cả 4 lượt đều khôi phục xong và đo lại xanh.

### Điểm TỰ QUYẾT (plan chưa nói rõ) — cần soi lại

1. **Khối `.wslist-head` DÍNH (`position:sticky`)** — plan không yêu cầu. Lý do: chip lọc cuộn mất thì user
   tưởng shop biến mất. Giá: **78px** của khung 564px bị chiếm vĩnh viễn khi cuộn. Đã hạ từ 112px xuống 78px
   bằng cách để nút "+ Thêm tài khoản" NGOÀI khối dính (việc hiếm). Nếu thấy không đáng thì bỏ `sticky` là xong,
   không ảnh hưởng tiêu chí nào (P8 = 863 vẫn ≤900).
2. **Tỉ lệ `N/M ✓` ở `th` biến mất trên mobile** — khuôn `stack-sm` giấu `thead` ở ≤920px, tức C2 làm mất
   thông tin ở khổ hẹp (mà hàng thẻ cũ thì có). Đã vá bằng `<span class="thsub m-only">` trong hàng `Tổng`:
   ≤920px hiện `0 1/3 ✓`, >920px chỉ hiện `0`. Đo được cả hai khổ. Đây là phần plan không lường.
3. **Nhãn chip rút gọn đệm** — 4 chip ở cột 270px chỉ có 233px: để nguyên cỡ `.chip` của `/dispatch` thì gãy
   2 hàng (+26px). Đã thu `padding 2px 4px · font 11px · gap 2px` ⇒ **226,9px trong 233px, dư 6,1px**, một hàng.
   Chữ nhãn GIỮ NGUYÊN như plan. Selector phải là `.chips.wslist-chips` (2 lớp) vì `.chips` khai báo SAU trong file.
4. **`.content:has(.fleetpage)` hạ `padding-top` 26→20px** — plan chỉ giao A/B/C/D, nhưng nếu không cắt thì
   P11 (`top ≤215`) KHÔNG đạt: chỉ riêng A1+A2 đưa top về 221,8px. Cắt 6px ở đây + 2px margin `.kpis.overview`
   + 2px margin `.statstrip` mới xuống 211,8px. Chỉ áp cho trang khoá-viewport này, không đụng `.content` chung.
5. **`FleetSummary.Machines` GIỮ NGUYÊN** dù sau A3 không còn ai đọc. Cố ý theo phạm vi plan (A3 chỉ nói gỡ
   `_kMachines` trong `Fleet.razor.cs`); đổi record công khai của builder là việc ngoài phạm vi. **Đây là dead
   compute còn lại** (`snap.Machines.Count(IsOnline)` mỗi nhịp 2s) — phiên chính quyết có gỡ nốt không.

### Phát hiện NGOÀI phạm vi (không tự sửa)

- **Mâu thuẫn ngưỡng vẫn còn nguyên**, chỉ là hết phô ra ở trang này: `IsOnline` 45s vs `MachineOffline` 180s,
  máy im nhịp 45–180s không thuộc bên nào. Sidebar badge + `/machines` + `/dispatch` vẫn dùng `IsOnline`.
  Đúng như plan ghi ở mục "còn nợ" — cần user quyết.
- **`/dispatch` cũng đếm máy bằng `IsOnline`** (`Dispatch.razor.cs:177`) trong khi trang đó có thẻ KPI riêng —
  nếu thống nhất ngưỡng thì phải sửa cả chỗ đó.
- **`.wsmain .tablewrap` tràn ngang ở 921px**: bảng cần 441px trong khung 313px (tràn 128px, cuộn trong
  `.tablewrap`). **CÓ TRƯỚC bản vá** — đo bằng cách ẩn `.thsub`: bề ngang bảng KHÔNG đổi (441px cả hai),
  tức phần thêm của C2 góp **0px**. Nguyên nhân là `.wslist` giữ cứng `flex: 0 0 270px` ở dải 921–1099px,
  đúng dải hẹp nhất của app (sidebar nhảy 64→250px). Không sửa vì ngoài phạm vi.
- `/orders` hiện **34 dòng** chứ không phải 38 như plan ghi (KPI "Đơn chờ hôm nay" cũng 34) — chỉ là số liệu
  fixture, cấu trúc 5 cột nhóm không hồi quy.

### Môi trường sau khi đo (đã trả về đúng hình dạng seed)

Acc phụ của P17 đã xoá (PUT lại `bigseller.json`); 2 cảnh báo địa chỉ đã đóng; 2 bản ghi ledger có vùng phủ
120/80 dòng đã reset về đúng trạng thái seed gốc. Kiểm lại: **7 acc · 21 shop · Tổng = 0 0 0 0**.
Script mới thêm: `scratchpad/seed-run.sh <token>` — cấp 2 lease tươi để kiểm chip "Đang chạy" + tô nổi shop
đang chạy (lease chỉ tươi 120s, chạy lại trước mỗi lượt đo).

---

## Chặng 4 — Phản biện (`phan-bien`, chỉ đọc code)

Tìm ra **1 lỗi NẶNG + 5 trung bình + 6 nhẹ**, trong đó lỗi nặng nằm ngoài mọi vùng mà 21 tiêu chí đo tới.

## Chặng 5 — Phiên chính chốt việc

### NẶNG-1 — chip lọc mù hẳn việc "Tên SP" (rewrite). ĐÃ SỬA + CÓ TEST GHIM

`StateOf` quét `SummaryOps` (3 việc) trong khi **mọi bảng trên trang vẽ `OpOrder` (4 việc)**. Shop xong
scrape/import/update mà kẹt rewrite → mang glyph ✓, và **biến mất khỏi cả chip "Dừng/lỗi" lẫn "Chưa xong"**,
trong khi panel phải ngay cạnh vẽ đúng shop đó là `■ dừng dở`. Bộ lọc báo thành công, trả về tập thiếu.

21 tiêu chí không thể bắt được vì các con số của P9/P10 **đọc ra từ chính hàm bị lỗi**.

Sửa: `StateOf` quét `OpOrder`. **Đo trên dữ liệu thật, chip "Chưa xong": 6 acc/17 shop → 7 acc/21 shop** —
4 shop của `andre-giulibr@hotmail.com` (xong 3 việc, chưa chạy Tên SP) trước đây bị giấu khỏi đúng cái chip
dùng để truy việc tồn.

### Đã sửa tiếp

| # | Vấn đề | Xử lý | Bằng chứng |
|---|---|---|---|
| TB-3 | Tên shop cắt ellipsis **không có `title`** (tên acc thì đã có) — mất thông tin không đường lấy lại; bản cũ gãy 2 dòng nhưng đọc trọn | Thêm `title` | **21/21** shop có title |
| TB-4 | `.wslist-chips .chip` thu nhỏ **không bọc media query** ⇒ rò xuống mobile, chip cao 19px trong khi cùng khối media ép mọi nút khác lên 36px | Bọc `@media (min-width: 921px)` | @375 chip về `5px 12px`, cao **28px**; @1280 vẫn `2px 4px` |
| TB-5 | `flex-wrap: nowrap` chỉ dư **6,1px (2,6%)**, mà **font Inter KHÔNG được nhúng** (`wwwroot` chỉ có `app.css`, không `@font-face`) ⇒ máy khác chạy Segoe UI/SF/Roboto, lệch quá 2,6% là tràn, mà `.chip` nowrap nên không co được → thanh cuộn ngang | Bỏ `nowrap`, để `wrap` mặc định | @1280 vẫn 1 hàng (227/233px), `.wslist` tràn ngang **0**; ca xấu chỉ xuống 2 hàng |
| TB-6 | Acc **chưa có shop nào biến mất** với mọi chip ≠ Tất cả — kể cả acc vừa bấm "+ Thêm tài khoản" khi đang bật chip | Giữ nhóm rỗng | Có test ghim, thử phá đỏ |
| NHẸ-7 | `.content:has(.fleetpage)` viết trần có độ đặc hiệu (0,2,0) thắng `.content` (0,1,0) của khối ≤920px ⇒ ở mobile đệm trên bị **đẩy 16px → 20px**, ngược mục tiêu | Bọc `@media (min-width: 921px)` | @375 về **16px**; @1280 vẫn 20px |
| NHẸ-9 | `FleetSummary.Machines` = dead compute chạy mỗi nhịp 2s, không ai đọc | Gỡ khỏi record + `Summarize` | grep = 0 |
| NHẸ-10 | Badge "N shop" nền `--gray` **trùng đúng màu hover** ⇒ rê chuột là badge tan vào nền | Đổi sang `--stroke` | `rgb(226,232,240)` |

### Đã viết TEST — thứ mà cả ba đợt trước đều thiếu

`server/Shopee.Hub.Web.Tests/FleetRowsBuilderStateTests.cs` — **16 test** cho `StateOf` / `FilterByState` /
`NormalizeState` / `Summarize`. Test **151 → 167**.

Cả hai lượt kiểm đều chỉ ra cùng một điều: 4 lượt thử phá của người thực thi đều là **thử phá phép đo trình
duyệt**, không đẻ ra lưới an toàn nào; logic mới có 0 test. Nay đã có, và **thử phá đúng luật 4**:

| Phá | Kết quả |
|---|---|
| `StateOf`: `OpOrder` → `SummaryOps` (trả về đúng lỗi NẶNG-1) | **2 test ĐỎ** → khôi phục → 167 xanh |
| `FilterByState`: bỏ nhánh giữ acc rỗng | **1 test ĐỎ** → khôi phục → 167 xanh |

### KHÔNG sửa, và vì sao

**TB-2 — phản biện nói A2 bỏ "Máy online" là vứt mất tín hiệu chặt hơn (45s), giữ lại tín hiệu lỏng hơn (180s).**
Lập luận đúng về nguyên tắc, nhưng nghiệm thu chỉ ra **tín hiệu 45s vẫn còn ở badge sidebar** (`2/9 máy online`,
hiện trên MỌI trang) ⇒ không mất. Đổi lại, tiêu chí P3 tôi viết ("số máy chỉ xuất hiện một lần trên cả trang")
**là SAI**: thực tế còn 2 lần (badge sidebar 45s + thẻ KPI 180s), tức mâu thuẫn ngưỡng **chỉ chuyển chỗ chứ
chưa hết**.

Không sửa ngưỡng vì đó là quyết định ngữ nghĩa của user và đụng vào là đụng mọi trang. Bản đồ hiện trạng:

- `MachineOffline` (**180s**) — 10 chỗ, gồm MỌI quyết định vận hành: chọn máy giao việc (`DispatchRowsBuilder`,
  `Dispatch.razor.cs`), `Search.razor`, `DispatchOrdersTab`, `OpCell`, thẻ KPI của trang này, và
  `MachineOfflineWatch` (webhook, **có test**).
- `IsOnline` (**45s**) — sau bản vá chỉ còn **2 chỗ**: badge sidebar `MainLayout.razor:109` và
  `DispatchRowsBuilder.cs:183`.

⇒ Đề xuất cho user: thống nhất hiển thị về **180s** (đổi badge sidebar sang `!MachineOffline`) thì cả app
nói cùng một thứ tiếng. **Cần user chốt** vì nó làm máy chết hiện "online" thêm tới 3 phút.

### Còn nợ

- **Mâu thuẫn ngưỡng 45s/180s vẫn còn** giữa badge sidebar và thẻ KPI — xem trên, chờ user quyết.
- **P11 biên rất mỏng (3,2px)** và chỉ đạt nhờ 3 thay đổi CSS ngoài phạm vi plan (`padding-top` 26→20 +
  2 margin 12→10). Nghiệm thu đã chứng minh: làm ĐÚNG A+B+C+D như plan viết thì P11 ra 221,8px = **trượt**.
  Đợt sau đụng chiều cao thẻ KPI/dải thống kê là rơi lại.
- `.ss-sum` bị `display:none` ở ≤920px (quy tắc có TRƯỚC) ⇒ phần A1 giữ lại (`dừng/lỗi · scrape · …`)
  **không hiện trên mobile**; ở 375 dải chỉ còn `Việc đang chạy · Việc chờ`.
- `.wsmain .tablewrap` tràn 128px ở 921px — **có trước bản vá**, C2 không góp thêm px nào.
- Ở ≤920px khối `.wslist-head` dính + nút Thêm chiếm ~113px trong 276px ⇒ **mobile gần như không hưởng phần
  lợi mật độ** mà P7/P8 đo được trên desktop.
- `POST /ledger/set` chỉ đổi status, KHÔNG mang số dòng — ai đo lại hàng `Tổng` bằng nó sẽ thấy 0 và tưởng
  đúng. Phải dùng `POST /ledger`.

### Trạng thái

Build **0 Warning / 0 Error** · test **167 passed / 0 failed** (151 + 16 test mới).
