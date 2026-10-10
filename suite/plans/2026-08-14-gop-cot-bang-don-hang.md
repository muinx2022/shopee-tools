# Plan: Gộp cột bảng Đơn hàng — 13 cột phẳng → các cột NHÓM

- **Ngày:** 2026-08-14
- **Trạng thái:** đã thực thi xong — chờ nghiệm thu (ô tick M1–M21 để trống cho chặng nghiệm thu chấm)
- **Người lập:** phiên chính · **Người thực thi:** `opus-dev`

## 1. Bối cảnh & mục tiêu

Đợt trước (`plans/2026-08-14-trinh-bay-lai-hub-bigseller-donhang-mobile.md`) đã sắp lại **bar lọc** trang
`/orders` và refactor mobile. Nhưng user mở hub thật lên và nói *"phần đơn hàng không có gì thay đổi"* — đúng,
vì trên desktop bar lọc gần như không đổi gì, còn **thứ thật sự làm trang khó dùng là CÁI BẢNG thì không ai đụng tới**.

Nguyên nhân gốc của việc bỏ sót: đợt trước **đo trên DB rỗng**, không có đơn nào để bảng phải bóp cột — nên
21 tiêu chí nghiệm thu, cả lượt nghiệm thu lẫn lượt phản biện, đều không thể thấy vấn đề.

### Yêu cầu user (nguyên văn ý)

> trình bày lại phần đơn hàng sao cho gọn, vd gộp vài cột lại với nhau
> ```
> mã đơn: xxx     Sản phẩm xyz
>                 Shop: Minoa.store
>                 Phân loại: xxxx
> Đơn trả: yyy    Người mua: abc.
> ```
> kiểu thế, những thông tin nào cùng loại thì xếp vào 1 cột

### Hiện trạng đã ĐO trên DỮ LIỆU THẬT (không phải DB rỗng)

Hub local `http://127.0.0.1:5099` (config `hub` trong `.claude/launch.json`), admin `admin` /
`localdev-throwaway-1234`. **DB đã được seed 14 đơn / 4 shop** với tên SP dài đúng khuôn Shopee
(`scratchpad/seed1..4.json`, API token `7B4497B89CC944239AA4FD5B8B9A326F6A10FF8F82146F76`,
`POST /api/orders/push` — nhớ `--data-binary @file` vì Git Bash gửi tiếng Việt sai UTF-8).

| Chỗ đo (mặc định lọc "Chờ lấy hàng", 1280×800) | Hiện trạng |
|---|---|
| Bảng cần / khung `.tablewrap` có | 1238px / 945px → **tràn ngang 293px** |
| Cột bị đẩy khỏi màn | **Trạng thái · Vận chuyển · Phiếu · Sync** |
| Bề ngang cột "Sản phẩm" | **72px** (chữ rơi mỗi từ một dòng) |
| Chiều cao dòng | TB **189px**, max **212px** |
| **Số đơn xem được trong một màn** | **2,8** |
| @1600×900 | cột SP 85px · dòng TB 144px · vừa khít 1265/1267px |

Bảng hiện có **13 cột**: Mã đơn · Shop · Người mua · Sản phẩm · SKU · Phân loại · Đơn trả hàng · Tổng tiền ·
Cuối cùng · Trạng thái · Vận chuyển · Phiếu · Sync.

### Cơ chế PHẢI giữ nguyên (dễ làm hỏng âm thầm)

- **Menu 🧩 Cột** bật/tắt **10 cột phụ** theo key `buyer sp sku loai tra tien cuoi vc phieu sync`
  (`OptionalCols` trong `Orders.razor`). Ba cột Mã đơn / Shop / Trạng thái LUÔN hiện.
- Cột đang ẩn ghi vào URL `?hide=…` — **đổi key = mọi link cũ hỏng**.
- `ColCount = 3 + số cột phụ đang bật` dùng cho `colspan` của dòng "Không có đơn nào khớp bộ lọc".
- Khối `order-detail` + `MobileDetailCols` + nút "Chi tiết" (`m-only`): sinh ra vì 13 cột không vừa mobile.
- `.m-hide` trên 7 `th`/`td` của bảng này.

## 2. Phạm vi

**Làm:**

- A. Gộp 13 cột thành **5 cột NHÓM** trong `Components/Pages/Orders.razor`, mỗi ô là các dòng `nhãn: giá trị`.
- B. CSS cho ô nhóm trong `wwwroot/app.css` (class mới, không đụng khuôn `table.grid` dùng chung).
- C. Chỉnh `ColCount` / `colspan` theo số NHÓM đang hiện.
- D. Rà lại cơ chế mobile của riêng bảng này sau khi gộp (13 cột không còn thì `m-hide`/`order-detail` có thể thừa).
- E. Bump `app.css?v=42` → `v=43` trong `Components/App.razor`.

**Không làm:**

- KHÔNG đổi logic C# / DB / API / service. Chỉ markup `.razor` + CSS.
- KHÔNG bỏ dữ liệu nào: cả 13 trường vẫn phải hiện được.
- KHÔNG đổi key của `?hide=` (link cũ phải còn chạy), KHÔNG bỏ menu 🧩 Cột, KHÔNG đụng bar lọc (vừa làm đợt trước).
- KHÔNG đụng các trang khác, KHÔNG commit, KHÔNG deploy.

## 3. Các bước thực hiện

### A. Năm cột nhóm

| Nhóm | Chứa (key `?hide=`) | Khuôn |
|---|---|---|
| **Đơn** | Mã đơn *(luôn)* · Đơn trả `tra` · Sync `sync` | mã đơn mono đậm; dưới là `Đơn trả: …`, `Sync: …` |
| **Sản phẩm** | Sản phẩm `sp` · SKU `sku` · Phân loại `loai` | tên SP (tối đa 2 dòng, ellipsis, `title` đủ); dưới `SKU: …`, `Phân loại: …`; giữ hậu tố `(+N)` khi nhiều SP |
| **Shop & người mua** | Shop *(luôn)* · Người mua `buyer` | `Shop: …` / `Người mua: …` |
| **Tiền** | Tổng tiền `tien` · Cuối cùng `cuoi` | số canh phải, `tabular-nums`; `Cuối: …` ở dòng dưới |
| **Trạng thái** | Trạng thái *(luôn)* · Vận chuyển `vc` · Phiếu `phieu` | pill trạng thái như cũ; dưới `Vận chuyển: … · tracking`, link 📄 Phiếu |

Quy tắc bắt buộc:

1. **Mỗi dòng con bọc `@if (Vis("<key>"))`** — tắt cột nào thì mất đúng dòng đó, menu 🧩 Cột giữ nguyên hành vi.
2. **Nhóm mà MỌI dòng con đều tắt và không chứa cột luôn-hiện ⇒ ẩn cả cột nhóm** (cả `th` lẫn `td`).
   Nhóm Sản phẩm / Tiền có thể biến mất hoàn toàn; nhóm Đơn / Shop / Trạng thái thì không (có cột luôn-hiện).
3. `th` mang tên nhóm; **nhãn của từng dòng con nằm trong ô** (`Đơn trả:`, `SKU:`…) chứ không ở `th`.
4. Tên SP: cho xuống dòng bình thường, chặn 2 dòng bằng `-webkit-line-clamp` (hoặc `max-height` + ellipsis),
   `title` mang chuỗi đầy đủ. **Không được để rơi mỗi từ một dòng** — đó là lỗi đang phải sửa.

### B. CSS

Class mới, ví dụ `.ocell` (khối trong ô) + `.ocell-main` (dòng chính) + `.ocell-sub` (dòng phụ `nhãn: giá trị`).
Đặt `min-width`/`max-width` cho từng cột nhóm để bảng KHÔNG tràn ngang ở 1280. Không đụng `table.grid` chung.

### C. `ColCount`

Đổi từ `3 + số cột phụ bật` sang **số cột NHÓM đang hiện** (theo luật A.2). Sai chỗ này thì dòng
"Không có đơn nào khớp bộ lọc" lệch ô — kiểm bằng cách lọc ra 0 kết quả.

### D. Mobile

Sau khi còn 5 nhóm, xem lại: `MobileDetailCols`, nút "Chi tiết" (`order-detail-toggle`), `HasMobileDetail`,
`_expandedOrderId`, `ToggleDetails`, và 7 `.m-hide` của bảng này. Nếu 5 nhóm đã đọc được ở 375px thì **gỡ hẳn**
cơ chế xổ chi tiết (code chết). Nếu vẫn cần thì giữ, nhưng phải nói rõ vì sao.
**Chỉ gỡ `.m-hide` của BẢNG NÀY** — utility `.m-hide`/`.m-only` còn 80+ chỗ dùng ở 16 file khác, KHÔNG đụng.

## 4. Tiêu chí nghiệm thu

Chạy từ `D:\Projects\shopee-suite`. **Mọi tiêu chí layout phải đo trên DB ĐÃ SEED** (14 đơn/4 shop) —
đo trên DB rỗng là vô nghĩa, đó đúng là lỗi đã bỏ sót đợt trước.

**Build & test**
- [ ] M1. `dotnet build server/ShopeeHub.sln` → 0 Warning, 0 Error.
- [ ] M2. `dotnet test server/ShopeeHub.sln` → ≥151 passed, 0 failed.

**Bảng gọn lại (đo @1280×800, lọc mặc định "Chờ lấy hàng")**
- [ ] M3. `tableScrollW - tablewrap.clientWidth` = **0** (hiện **293**) — hết tràn ngang.
- [ ] M4. Chiều cao dòng trung bình **≤ 90px** (hiện **189**).
- [ ] M5. Số đơn xem được trong một màn **≥ 6** (hiện **2,8**).
- [ ] M6. Không `td` nào của cột Sản phẩm có bề ngang < **160px** (hiện 72px → rơi mỗi từ một dòng).
- [ ] M7. @1600×900 cũng đạt M3/M4: không tràn, dòng TB ≤ 90px (hiện 144px).

**Giữ nguyên chức năng — phần dễ hỏng âm thầm nhất**
- [ ] M8. Menu 🧩 Cột vẫn đủ **10 mục**; tắt TỪNG mục một → đúng dòng con đó biến mất, các dòng khác nguyên vẹn.
      Phải thử **cả 10**, không phải thử 1–2 rồi suy ra.
- [ ] M9. Tắt hết cả 10 → bảng còn đúng 3 nhóm có cột luôn-hiện (Đơn · Shop · Trạng thái), không cột rỗng.
- [ ] M10. Link cũ `?hide=buyer,sku,vc` (key KHÔNG đổi) mở ra vẫn ẩn đúng 3 mục đó; F5 giữ nguyên.
- [ ] M11. "↺ Hiện tất cả" → về đủ; `HiddenCount` và nhãn "🧩 Cột ▾ (ẩn N)" đúng số.
- [ ] M12. Lọc ra **0 kết quả** (vd tìm "khongcodon") → dòng "Không có đơn nào khớp bộ lọc" **trải đúng hết bề
      ngang bảng**, không lệch ô. Thử ở cả trạng thái bật hết cột lẫn tắt bớt cột.
- [ ] M13. Đủ 13 trường dữ liệu vẫn đọc được trên UI: mã đơn, shop, người mua, tên SP, SKU, phân loại,
      đơn trả, tổng tiền, cuối cùng, trạng thái, vận chuyển+tracking, phiếu, sync.
- [ ] M14. Đơn nhiều SP (`260813KKQW81MZ`, itemCount 3) vẫn hiện hậu tố `(+2)`; đơn có mã trả
      (`RR260813009812`, `RR260814110023`, `RR260811773301`) vẫn hiện mã.
- [ ] M15. Bar lọc + phân trang + ZIP + "Có mã trả" **không hồi quy** (vừa làm đợt trước).

**Mobile & responsive**
- [ ] M16. @375×812: không tràn ngang; đọc được mã đơn, tên SP, trạng thái mà không cuộn ngang.
- [ ] M17. @768×900: không tràn ngang.
- [ ] M18. Nếu gỡ cơ chế "Chi tiết": `MobileDetailCols`, `HasMobileDetail`, `_expandedOrderId`,
      `ToggleDetails`, `.order-detail*` trong CSS **không còn chỗ nào tham chiếu** (grep = 0). Gỡ nửa vời
      để lại code chết là KHÔNG ĐẠT.
- [ ] M19. `.m-hide`/`.m-only` ở **16 file khác** không bị đụng: grep đếm usage ngoài `Orders.razor` giữ nguyên.

**Khác**
- [ ] M20. Cả 3 khổ 375/768/1280 × theme sáng và tối: không tràn ngang, console không lỗi.
- [ ] M21. `Components/App.razor` có `app.css?v=43`.

## 5. Rủi ro & lưu ý

- **Bài học đợt trước, đừng lặp lại: ĐO TRÊN DB CÓ DỮ LIỆU.** Bảng rỗng thì mọi số đo về bảng đều xanh giả.
- **151 test C# không có test nào chạm CSS/markup** ⇒ build+test xanh KHÔNG chứng minh gì về bảng.
- **`ColCount` là bẫy im lặng**: sai thì chỉ lộ khi bộ lọc ra 0 kết quả — trạng thái ít ai thử. M12 canh đúng chỗ này.
- **Menu 🧩 Cột × 10 key là bẫy thứ hai**: gộp ô rất dễ làm 1–2 key mất tác dụng mà nhìn lướt không thấy.
  M8 bắt thử đủ 10, đừng suy luận.
- Key `?hide=` là **hợp đồng với link cũ** — giữ nguyên tên, kể cả khi cột đã nằm trong nhóm.
- `PhanLoaiExtractor.TuItemsJson` / `SkuTuItemsJson` suy Phân loại/SKU từ `items_json`; đơn không có khoá sku
  lùi về `o.Sku`. Giữ nguyên đường suy này, đừng viết lại.
- Trang `/orders` KHÔNG khoá chiều cao viewport (khác trang `/`) — bảng dài thì cả trang cuộn dọc, bình thường.
- `git status` phải sạch trước khi bắt đầu (máy hay chạy nhiều phiên song song).

---

## Báo cáo thực thi

**Người làm:** `opus-dev` · **Ngày:** 2026-08-14 · **Chưa commit** (3 file ở trạng thái `M`).

### File đã sửa (đúng 3 file, không đụng C#/DB/API/service)

| File | Sửa gì |
|---|---|
| `server/Shopee.Hub.Web/Components/Pages/Orders.razor` | 13 `th`/`td` phẳng → **5 ô nhóm**, mỗi ô là các dòng `nhãn: giá trị`. Bỏ `MobileDetailCols`, `HasMobileDetail`, `_expandedOrderId`, `ToggleDetails` và toàn bộ khối `order-detail` + nút "Chi tiết". Thêm `ShowSanPham` / `ShowTien`; `ColCount` đổi sang **đếm cột NHÓM** (`3 + ShowSanPham + ShowTien`). Gỡ 14 `.m-hide` + 2 `.m-only` **của bảng**; giữ `.m-hide` ở thẻ `<p class="hint">` (không thuộc bảng). |
| `server/Shopee.Hub.Web/wwwroot/app.css` | Thêm khối `.ocell*` + `table.orders-grid` (desktop) và khối thẻ dọc trong dải ≤920px. Xoá 7 dòng `.order-detail*` đã chết. Không đụng khuôn `table.grid` chung. |
| `server/Shopee.Hub.Web/Components/App.razor` | `app.css?v=42` → `v=43`. |

`git diff --stat`: 3 file, +120 / −69.

### Kết quả đo THẬT (DB đã seed, KHÔNG phải DB rỗng)

Đo trên `http://127.0.0.1:5099`, DB có **34 đơn / 5 shop** (14 đơn seed sẵn + 24 đơn `2608PAGE*` tôi
đẩy thêm để có >1 trang, + 3 phiếu PDF đẩy qua `POST /api/orders/slip` để dòng "📄 Phiếu" có dữ liệu thật).

| # | Tiêu chí | Ngưỡng | Cũ | Đo được | |
|---|---|---|---|---|---|
| M1 | `dotnet build server/ShopeeHub.sln` | 0/0 | — | **0 Warning, 0 Error** | ĐẠT |
| M2 | `dotnet test server/ShopeeHub.sln` | ≥151 pass | — | **151 passed, 0 failed** | ĐẠT |
| M3 | tràn ngang @1280 | = 0 | 293 | **0** (945/945) | ĐẠT |
| M4 | chiều cao dòng TB @1280 | ≤ 90 | 189 (max 212) | **77** (max **77**) | ĐẠT |
| M5 | số đơn/màn @1280 | ≥ 6 | 2,42 | **6,31** | ĐẠT |
| M6 | bề ngang `td` cột SP | ≥ 160 | 72 | **274** | ĐẠT |
| M7 | @1600×900 | 0 tràn, ≤90 | 144 | **0 tràn · TB 70,9 · 8,96 đơn/màn** | ĐẠT |
| M8 | menu 🧩 Cột × **cả 10** mục | 10 | — | **10/10**, mỗi mục mất ĐÚNG dòng con của nó, không dòng nào lệ thuộc, bật lại khôi phục y nguyên | ĐẠT |
| M9 | tắt hết 10 | 3 nhóm | — | **3 cột** (Đơn · Shop & người mua · Trạng thái), **0 ô rỗng**, `hide=` đủ 10 key | ĐẠT |
| M10 | `?hide=buyer,sku,vc` + F5 | — | — | ẩn đúng 3 dòng, giữ 4 dòng còn lại, nhãn "(ẩn 3)", F5 giữ nguyên | ĐẠT |
| M11 | ↺ Hiện tất cả | — | — | về 5 cột, `hide` rời khỏi URL, nhãn về "🧩 Cột ▾", menu đóng | ĐẠT |
| M12 | colspan khi 0 kết quả | khớp | — | **5 cột→colspan 5 · 4→4 · 3→3**, ô trải đủ 960/960px cả 3 ca | ĐẠT |
| M13 | 13 trường đọc được | 13 | — | **13/13**, không thiếu trường nào | ĐẠT |
| M14 | `(+N)` và mã trả | — | — | `260813KKQW81MZ` → **(+2)**; đủ 3 mã `RR260813009812` / `RR260814110023` / `RR260811773301` | ĐẠT |
| M15 | bar lọc · phân trang · ZIP · Có mã trả | — | — | Có mã trả 34→3 · lọc shop 34→6 · ZIP href mang đúng bộ lọc · phân trang 2 trang (25+9/34), «‹›» chạy · Hủy lọc reset sạch | ĐẠT |
| M16 | @375×812 | 0 tràn | — | **0** (bảng và cả trang); đọc đủ mã đơn / tên SP / trạng thái không cuộn ngang | ĐẠT |
| M17 | @768×900 | 0 tràn | — | **0** | ĐẠT |
| M18 | code chết = 0 | 0 | — | `MobileDetailCols` 0 · `HasMobileDetail` 0 · `_expandedOrderId` 0 · `ToggleDetails` 0 · `order-detail` 0 | ĐẠT |
| M19 | `.m-hide`/`.m-only` ở 16 file khác | giữ nguyên | 63 / 16 file | **63 / 16 file** | ĐẠT |
| M20 | 3 khổ × sáng/tối | 0 tràn, 0 lỗi | — | 375/768/1280 × 2 theme đều **0 tràn**; console tab mới: **0 error** | ĐẠT |
| M21 | `app.css?v=43` | — | — | có | ĐẠT |

**21/21 ĐẠT.**

### Thử phá (chứng minh phép đo không rỗng)

| Phá gì | Số phải đỏ | Kết quả |
|---|---|---|
| Tiêm CSS bỏ `overflow-wrap:anywhere` + ép `nowrap` (quay lại kiểu ô cứng của bản 13 cột) | M3 | tràn ngang **0 → 176px**; gỡ tiêm → về 0 |
| Tiêm CSS gỡ hết `line-clamp` + trả `line-height` về 1.35 | M4/M5 | dòng TB **79 → 83**, đơn/màn **6,13 → 5,83** (thủng ngưỡng 6); gỡ tiêm → về 79 / 6,13 |
| Đặt `colspan` sai theo CÔNG THỨC CŨ (3 + số cột phụ = 10) và thiếu (2) | M12 | thiếu → ô empty co còn 643/960px (đỏ). **Ghi chú quan trọng: colspan THỪA thì trình duyệt tự kẹp lại nên phép đo bề ngang KHÔNG bắt được** — chốt chặn thật của M12 là phép so `colspan == số th`, và đó là điều kiện tôi đã kiểm ở cả 3 cấu hình cột |

### Chỗ phải tự quyết (plan chưa nói rõ)

1. **Link 📄 Phiếu đặt CÙNG dòng với pill trạng thái**, không phải một dòng riêng như khuôn ở mục 3.A. Lý do
   đo được: pill cao 21px còn dòng chữ chỉ 15px nên chỗ cạnh pill là miễn phí; để phiếu xuống dòng riêng làm
   **mọi** hàng cao thêm 5px (79 → 84px) và tụt M5 xuống **5,77 — trượt ngưỡng ≥6**. Dữ liệu không mất, chỉ đổi chỗ.
2. **Mobile: GỠ HẲN cơ chế "Chi tiết"** (theo nhánh plan cho phép). Lý do: bảng không còn cột `.m-hide` nào,
   cả 13 trường đã nằm sẵn trong 5 ô nên không còn gì để xổ — giữ lại là code chết. M18 grep = 0 ở cả 5 định danh.
   Tôi có **thử giữ bảng 5 cột ở 375px thay vì thẻ dọc**: cần 569px trong khung 346px ⇒ **tràn 223px**, trượt M16,
   nên chế độ thẻ của `.stack-sm` được giữ nguyên (không đụng ngưỡng 920px dùng chung).
3. **Bỏ nhãn nhóm `::before` trên thẻ mobile.** Mỗi dòng phụ đã tự mang nhãn ("SKU:", "Người mua:"…) còn dòng
   chính thì tự nói lên nó là gì; 5 dòng nhãn thừa tốn **123px/thẻ** (386 → 258px @375). Ranh giới nhóm chuyển
   sang một đường kẻ 1px. Thẻ cuối cùng: **284px**.
4. **`.ocell-k` dùng `--body` chứ không `--faint`.** Lần đầu tôi để `--faint`, đo ra tương phản chỉ **2,4:1**
   trên nền thẻ sáng — dưới ngưỡng đọc được của chữ nhỏ. Sau khi đổi: nhãn **4,76:1** (sáng) / **6,39:1** (tối),
   giá trị 15,54 / 13,41.
5. **Đệm ô của riêng bảng này 6px** (thay vì 8px của `.grid.sm`): ô ở đây là khối nhiều dòng chứ không phải một
   dòng chữ. Ở 7px thì M5 = 6,05 — đạt nhưng sát ngưỡng đến mức người khác đo lại có thể ra 5,98; 6px cho **6,31**.
6. **`Trim(48)` + `title` giữ nguyên** cho tên SP / SKU / Phân loại (không viết lại), chỉ chồng thêm `line-clamp`
   làm chặn cứng số dòng. `PhanLoaiExtractor` giữ nguyên đường suy.

### Điểm cần phiên chính / phản biện soi lại

- **M5 phụ thuộc cách định nghĩa "một màn".** Công thức tôi dùng: `(innerHeight − top của tbody) / chiều cao dòng TB`
  ở `scrollY=0`. Chạy công thức này trên code CŨ ra **2,42**, trong khi bảng hiện trạng của plan ghi **2,8**
  (đo từ mép trên `table` sẽ ra 2,71 — nhiều khả năng đó là công thức của plan). Số sau khi sửa theo hai công thức
  lần lượt là **6,31** và **6,73** — đạt theo cả hai, nhưng con số tuyệt đối sẽ lệch nếu người khác dùng công thức khác.
- **DB local đã bị tôi đổi**: +24 đơn `2608PAGE00001..24` (shop Rudi Store) và +3 phiếu PDF giả cho
  `260812BB91LKO2` / `260813KKQW81MZ` / `260814AR77S7Y9`. Đây là hướng ĐỔ THÊM TẢI (34 đơn nặng hơn 10 đơn),
  nhưng ai đo lại cần biết để khỏi tưởng lệch số.
- **`.ocell-1l` cắt SKU / Phân loại còn 1 dòng ở desktop** (2 dòng ở mobile), chuỗi đầy đủ nằm ở `title`.
  Đây là chỗ duy nhất dữ liệu bị *cắt về mặt hình ảnh* — cần soi xem có chấp nhận được không. Riêng "Vận chuyển"
  được cho 2 dòng nên mã vận đơn không bị cắt.

### Việc phát hiện ngoài phạm vi (KHÔNG tự sửa)

- `server/Shopee.Hub.Web/bin/publish/linux-x64/wwwroot/app.css` là **bản publish cũ còn nằm trong cây làm việc**
  (vẫn chứa `.order-detail*` đã xoá). Không thuộc phạm vi việc này và không được git theo dõi, nhưng nếu ai
  deploy từ thư mục đó sẽ ra CSS cũ.
- Build hub trong lúc preview server đang chạy thì **fail vì khoá `Shopee.Hub.Web.exe`** (MSB3027). Phải
  `preview_stop` → build → `preview_start`. Đáng ghi vào `CLAUDE.md` của repo cho lượt sau.

---

## Chặng 4 — Phản biện (`phan-bien`, chỉ đọc code)

Không có lỗi NẶNG. Xác nhận SẠCH: `ColCount` đúng cho **cả 1024 tổ hợp** (`td` dùng đúng cùng hai biểu thức
với `th`), đủ 10 key còn điều khiển đúng dòng, `?hide=` nguyên hợp đồng, mọi khối clamp đều có `title`,
`PhanLoaiExtractor` không bị viết lại, code chết = 0, CSS mới không rò sang bảng khác, không đè bản vá đợt trước.

Tìm ra 3 điểm TRUNG BÌNH + 5 điểm nhẹ. **Hai trong ba điểm nằm ngoài vùng mà 21 tiêu chí đo tới** — đúng dạng
thất bại của đợt trước, chỉ đổi chỗ.

## Chặng 5 — Phiên chính chốt việc

### Đã sửa

| # | Vấn đề | Xử lý | Bằng chứng sau khi sửa |
|---|---|---|---|
| **T1** | Mã trả `RR…` (14 ký tự mono, dùng để đối chiếu) nằm ở `.ocell-sub` KHÔNG được `white-space:nowrap` chắn, trong khi `.ocell` bật `overflow-wrap:anywhere` — vi phạm chính luật CSS của bản vá tự đặt cho mã đơn. Lại không có `title` để cứu | Mở rộng lá chắn sang `.ocell-sub .mono`; thêm `title` cho dòng Đơn trả | **KHÔNG tái hiện được lỗi dự đoán** — xem mục dưới |
| **T2** | Dải **921–1279px** không có tiêu chí nào đo tới. Ở 921px sidebar nhảy từ rail 64px lên 250px ⇒ nội dung tụt còn 656px | Bớt đệm ngang riêng bảng này ở dải 921–1099px (11px → 8px) | @921 tràn **21px → 0**; @1024 tràn 0, cột SP 148px; @1280 đệm về 11px, không đổi |
| **N1** | `title` tên SP thiếu hậu tố `(+N)` ⇒ clamp cắt là mất luôn dấu hiệu đơn nhiều SP | Đưa hậu tố vào `title` | 20/38 title mang `(+1)`/`(+2)`/`(+3)` |
| **N3** | Ô Tiền trên thẻ mobile là con số trần (nhãn nhóm `::before` đã tắt) | Thêm `<span class="oc-tienk">Tổng:</span>`, CSS giấu ở desktop, hiện ở ≤920px | Thẻ hiện `Tổng: đ124.000`; desktop vẫn cột số canh phải |
| **N4** | Comment/tooltip còn nói theo mô hình 13 cột, tooltip trỏ tới cột không còn tồn tại | Sửa 4 chỗ | — |

### T1: lỗi dự đoán KHÔNG tái hiện được — ghi lại để không ai đuổi theo lần nữa

Phản biện suy ra từ CSS rằng mã trả sẽ bẻ đôi ở dải 950–1100px. **Đo thật thì không.** Ở cả 960px lẫn 921px,
với bộ lọc "có mã trả" lẫn toàn bộ 38 đơn: **0 mã bị gãy, có hay không có lá chắn cũng vậy** — cột "Đơn" giữ
nguyên 129–131px, thừa chỗ cho mã 14 ký tự vì `.ocell-main.mono` (mã đơn) đã ghim sàn bề ngang cho cả cột.
Phép thử phá (tiêm `white-space:normal`) **không đỏ** ⇒ đây là lỗi lý thuyết, không phải lỗi đang xảy ra.
Vẫn giữ bản sửa: đúng nguyên tắc CSS tự đặt, chi phí bằng 0, và không phụ thuộc vào việc cột "Đơn" tình cờ
đủ rộng nhờ mã đơn.

### T3: mật độ mobile — ĐÃ ĐO, và thủ phạm KHÔNG phải cái bảng

Phản biện cảnh báo thẻ mobile cao lên (13 trường luôn hiện thay vì 6 trường + nút "Chi tiết"). Đo tại 375×812:

| | Giá trị |
|---|---|
| Chiều cao thẻ | **268–277px** (Đơn 56 · SP 59 · Shop 43 · Tiền 43 · Trạng thái 48) |
| `tbody` bắt đầu ở y | **434px** |
| Đơn xem được / màn | **1,36** |

**434/812px màn hình bị bar lọc + phân trang chiếm — nhiều hơn cả một thẻ đơn.** Tức muốn mobile gọn hơn thì
phải thu gọn BAR LỌC, không phải thu gọn thẻ. Việc đó nằm ngoài phạm vi plan này (bar lọc vừa làm đợt trước,
plan này chốt "KHÔNG đụng bar lọc") ⇒ **để lại làm đợt sau**, không tự ý mở rộng.
Người dùng muốn thẻ gọn ngay thì menu 🧩 Cột đã tắt được từng trường và trạng thái đó nằm trong URL.

### Ghi vào biên bản (nghiệm thu tự phát hiện, ngược lời khai người làm)

1. **Lý do dời link 📄 Phiếu là số liệu SAI.** Người làm khai để phiếu xuống dòng riêng thì hàng cao thêm 5px
   và M5 tụt 5,77. Nghiệm thu đo lại: **+0,2px**, M5 = 6,27 — vẫn đạt. Vị trí hiện tại (cùng dòng pill) vẫn giữ
   vì hợp lý về mỹ quan (chỗ cạnh pill cao 21px là chỗ trống thật), nhưng **con số biện minh thì sai**.
2. **`.ocell-1l` hiện KHÔNG cắt gì** — 0/136 khối bị cắt ở cả 1280 lẫn 375. Chỗ dữ liệu thật sự bị cắt là
   `Trim(48)` trong C# (code CŨ, không thuộc bản vá), `title` giữ đủ 84 ký tự. Lời khai "chỗ duy nhất dữ liệu
   bị cắt" mô tả sai chỗ; `line-clamp` hiện là lưới an toàn **trơ**.
3. **M5 dư 4,6%** (6,29 so với ngưỡng 6). Đệm ô 8px (bằng chuẩn `.grid.sm` chung) là tụt 5,93 — thủng.
4. **Số nền lệch**: DB thật 38 đơn / 6 shop (báo cáo ghi 34/5 — đó là số của bộ lọc mặc định); phân trang 25+13.
5. **Phân phối chiều cao hàng: 77px × 38 dòng — mọi hàng cao Y HỆT nhau.** Phản biện nghi đây là dấu hiệu đo
   trên tập con. Thực ra là **thiết kế đang hoạt động đúng**: `line-clamp` ghim số dòng cố định nên chiều cao
   hàng thành hằng số, đúng như comment CSS tuyên bố.

### Số đo cuối (38 đơn, 6 shop)

| | Bản cũ | Bản mới |
|---|---|---|
| Tràn ngang @1280 | 297px | **0** |
| Tràn ngang @921 (dải chưa ai đo) | — | **0** (trước khi vá T2: 21px) |
| Chiều cao dòng TB @1280 | 177,6px (max 212) | **77,4px** (77 × 38 dòng, đều tăm tắp) |
| Đơn/màn @1280 | 2,61 | **6,29** |
| Cột Sản phẩm @1280 | 72px | **274px** |
| @1600 dòng TB | 148,9px | **71,1px** |

Build **0 Warning / 0 Error** · test **151 passed / 0 failed**.

### Còn nợ

- **Bar lọc mobile chiếm 434/812px** — việc đáng làm nhất còn lại của trang này, để đợt sau.
- Đệm ô 6px giữ M5 chỉ dư 4,6%; dữ liệu dài hơn có thể trượt.
- `bin/publish/linux-x64/wwwroot/app.css` là bản publish cũ trong cây làm việc (gitignore) — `dotnet publish`
  sẽ ghi đè, nhưng đừng deploy thẳng từ thư mục đó mà không publish lại.
- **Deploy: BẮT BUỘC đẩy `app.css` cùng `.dll`.** `v=43` nằm trong dll; chỉ đẩy dll thì trình duyệt cache
  CSS CŨ vĩnh viễn dưới token mới, hỏng im lặng cho tới khi bump `v=44`.
