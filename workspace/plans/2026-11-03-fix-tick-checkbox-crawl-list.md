# Sửa tick checkbox Crawl List (chặn bấm Import to Stores) — 03/11

**Trạng thái:** XONG (03/11 — phiên chính review + build Release sau khi đóng ShopeeUpdate; chờ user chạy Auto Run xác nhận runtime)

## Vấn đề

User thấy app không bấm được «Import to Stores». Log Auto Run 22:48 (`andre-giulibr`, tab **Tất cả**) lặp:

```
Trang N (tab Tất cả) có 50 SP — 50 SP khớp item id → Import to Stores (1 shop)…
Import lô lỗi … (0 dong, 0 nhan ra id) khong khop lo import (50 SP) nen KHONG bam Import.
```

→ `CheckMatchingRowsOnPageAsync` báo đã tick 50 id, nhưng `ReadTickedRowsAsync` / `EnsureTickedMatchBatchAsync` đọc lại **0 dòng** đang tick → **cố ý không** gọi `ClickToolbarImportAsync`. Nút toolbar (HTML user gửi: `.bs-antd-button.mode-sucess` + «Import to Stores») không phải chỗ hỏng; cổng tick chặn trước.

Cùng lỗi đã thấy 22:08 (`witkoxatlinpbgld`). Đổi tab Tất cả không hết.

## Nguyên nhân khả dĩ (DOM vxe)

User gửi HTML bảng thật 03/11 — cấu trúc checkbox:

```html
<td class="… col--checkbox">
  <div class="vxe-cell">
    <span class="vxe-cell--checkbox">
      <span class="vxe-checkbox--icon vxe-icon-checkbox-unchecked"></span>
    </span>
  </div>
</td>
```

Class checked vẫn là `vxe-icon-checkbox-checked` (đổi từ `-unchecked`). Khả năng cao: click nhầm target / bảng fixed trùng rowid / Vue chưa kịp cập nhật trước khi đọc lại — **không** phải đổi class name.

**Cột `col_8` `.vxe-cell--label`:** dump user mọi dòng = `84588075445` (trùng) → **không** dùng làm Shopee item id. Vẫn khớp ảnh→id từ API pageList.

## LÀM

1. **Một khối JS dùng chung** (tick + đọc) trong `BigSellerImportToStoreRunner` (hoặc helper cạnh đó):
   - Tìm checkbox theo thứ tự: `.vxe-cell--checkbox` → `.vxe-checkbox--icon` → `input[type=checkbox]` trong `tr.vxe-body--row` (vẫn gom theo `rowid` body + fixed).
   - `isChecked`: `input.checked` **hoặc** class trên icon/cell chứa `checkbox-checked` / `is--checked` / `vxe-icon-checkbox-checked` / `aria-checked="true"`.
   - `toggle`: click icon nếu có, không thì input, không thì cell (dispatch click).
2. **`CheckMatchingRowsOnPageAsync` + `ReadTickedRowsAsync`**: dùng khối trên (cùng định nghĩa checked).
3. **Sau tick, trước Ensure**: `Delay` ngắn (~300ms); nếu Ensure fail vì 0 tick → **tick lại 1 lần** + delay + Ensure lần 2; lần 2 vẫn fail thì log thêm **chẩn đoán** (số row có checkbox tìm được, sample `className` của 1–3 icon/cell) rồi ném như cũ (không bấm Import khi lệch).
4. **Không** nới `TickedMatchesBatch` (vẫn đòi khớp đủ lô) — tránh gửi nhầm.
5. **Không** đổi selector nút «Import to Stores» trừ khi log chứng minh click nút fail *sau khi* tick đã khớp.
6. Test: mở rộng unit quanh logic checked thuần nếu tách được (string/helper); tối thiểu giữ test `TickedMatchesBatch` xanh. Build Release `Shopee.Update`.

## KHÔNG LÀM

- Không commit/push.
- Không đụng Update / Clear / multi-shop UI.
- Không bỏ cổng Ensure tick.

## File

- `src/Shopee.Module.UpdateProduct/Engine/BigSellerImportToStoreRunner.cs` (chính)
- Test liên quan nếu tách helper thuần

## Tiêu chí đo

1. `dotnet build src/Shopee.Update/Shopee.Update.csproj -c Release -v q` → 0 error.
2. `dotnet test tests/Shopee.Core.Tests --filter "FullyQualifiedName~Import" -v q` → pass.
3. Code tick/đọc dùng chung `isChecked` rộng hơn chỉ `vxe-icon-checkbox-checked` (rg xác nhận).
4. Có delay và/hoặc 1 lần retry trước khi ném «KHONG bam Import» vì 0 tick.

## Sau khi code xong (phiên chính)

Đóng `ShopeeUpdate` nếu đang chạy → build Release lại → user chạy thử Auto Run.
