# Tick checkbox Crawl List bằng click chuột thật — 06/11

**Trạng thái:** ĐÃ SỬA LẦN 2 (nguyên nhân thật: chọn nhầm bản checkbox ẩn — xem "ĐÍNH CHÍNH" cuối file), BUILD + TEST XANH, **CHƯA CHẠY THẬT** trên BigSeller (chờ user chạy Auto Run xác nhận). Chưa commit.

> ⚠ Giả thuyết «click JS không đổi được trạng thái» ở các mục dưới đã được log 18:53 bác bỏ một phần — đọc mục ĐÍNH CHÍNH trước.

## Vấn đề

Log Auto Run 06/11 (`hoangdh200392`, tab Tất cả) lặp ở MỌI trang, vòng 1 import 0/3703 SP:

```
Trang N (tab Tất cả) có 150 SP — 50 SP khớp item id → Import to Stores (1 shop)…
⚠ Ensure tick: đọc lại 0 dòng đang tick (cần 50) — tick lại 1 lần rồi kiểm tra…
⚠ Chẩn đoán checkbox Crawl List: 50 rowid, 50 có checkbox — sample: icon=[…vxe-icon-checkbox-unchecked] … checked=false
Import lô lỗi — CHƯA gửi … khong khop lo import (50 SP) nen KHONG bam Import.
```

Cổng Ensure tick chặn đúng thiết kế; hỏng là bước TICK. Checkbox tìm đúng (50/50), icon vẫn `unchecked` sau cả lần tick lại
→ click không đổi được trạng thái (không phải lỗi tìm phần tử, nhận biết checked, hay timing).

Cùng triệu chứng đã thấy 03/11 trên bản click `cell`; bản vá 04/11 (`9b3b864`: click `icon`, thêm delay + retry, mở rộng `isChecked`)
**không** trị được gốc. Điểm chung của cả hai: `element.click()` trong JS — chỉ phát 1 sự kiện `click` không đáng tin.
(Suy luận — chưa tái hiện được vì cần đăng nhập BigSeller.)

## Đã làm (`BigSellerImportToStoreRunner.cs`)

1. `CheckMatchingRowsOnPageAsync` chia 2 pha: JS chỉ ĐỌC + ĐÁNH DẤU `data-ws-tick=i` checkbox cần đổi (cần tick mà chưa tick / ngoài lô mà đang tick);
   C# bấm từng cái bằng `Locator.ClickAsync` (chuột thật, có kiểm tra hit-target). Bỏ hàm JS `toggle`.
2. Dọn popup/lớp phủ TRƯỚC khi tick (`DismissGuideMasksAsync` + `DismissStrayModalsAsync` + `DismissLanguageGuideAsync`) — click thật bị popup
   chặn, khác click JS. Bị chặn giữa chừng ("intercepts pointer events") → dọn lại + thử lại 1 lần.
3. Dự phòng: 3 dòng liền click thật thất bại → các dòng còn lại click JS như bản cũ (không treo 3s × 50). Log `⚠ Click chuột thật … lỗi n/m dòng`.
4. `EnsureTickedMatchBatchAsync` (lần trước khi mở hộp chọn shop) retick khi LỆCH BẤT KỲ (trước chỉ khi đúng 0 dòng); retick idempotent, gồm bỏ tick dòng dư.
   Cổng `TickedMatchesBatch` KHÔNG nới.
5. Chẩn đoán khi vẫn lệch: thêm phần tử nằm TRÊN checkbox đầu (`elementFromPoint`, ghi rõ BỊ CHE hay không) + số hộp thoại đang hiện.

## Quan hệ với sửa popup `b006210` (04/11)

`b006210` chỉ dọn popup SAU cổng tick (trước nút Import toolbar, khi tìm hộp chọn shop, sau mỗi lô) → không chạm tới lỗi này.
Click JS vốn không bị popup chặn nên popup không giải thích được lỗi hôm nay; nhưng click thật thì bị chặn, nên bước 2 ở trên dùng lại bộ dọn đó.

## Không làm

- Không nới cổng Ensure tick, không đổi selector nút «Import to Stores», không đụng Update / Clear / multi-shop.

## Nếu vẫn lỗi

Đọc dòng `Chẩn đoán checkbox …` mới: `BỊ CHE` + tên phần tử → lớp phủ lạ cần dọn; `(chính checkbox)` mà vẫn unchecked → BigSeller không nhận cả click thật,
cần gọi API vxe (`setCheckboxRow`) hoặc dùng ô "chọn tất cả" của header.

## ĐÍNH CHÍNH sau log 18:53 (bản click chuột thật đã chạy trên máy user)

Log `workspace-update-hoangdh200392@gmail.com.log` (18:52) cho thấy click chuột thật cũng lỗi, kèm chi tiết Playwright:

```
Timeout 3000ms exceeded … locator resolved to <span data-ws-tick="0" class="vxe-checkbox--icon vxe-icon-checkbox-unchecked"></span>
… waiting for element to be visible, enabled and stable — element is not visible
Chẩn đoán: trên checkbox đầu = SPAN.vxe-checkbox--icon … (phần tử KHÁC nằm trên)   ← tại cùng toạ độ có MỘT span.vxe-checkbox--icon khác
```

**Nguyên nhân gốc:** vxe vẽ mỗi hàng 2–3 lần (main + fixed-left + fixed-right). Bản checkbox trong **main** bị `visibility:hidden` nhưng VẪN có kích thước;
bản hiển thị nằm ở cột **fixed-left**. `vis` cũ chỉ kiểm tra `getBoundingClientRect` > 0 nên `findCheckbox` luôn chọn bản ẩn ở main (nó đứng trước trong DOM,
và `byId` lấy bản đầu tiên khác null). Hệ quả: ĐỌC lại (`ReadTickedRowsAsync`) và BẤM đều nhắm bản ẩn → luôn "chưa tick" → cổng Ensure chặn mọi lô.
Cổng Ensure tick xuất hiện cùng ngày 03/11 với lỗi đầu tiên — rất có thể lúc trước (bấm mù, không đọc lại) tick vẫn ăn và chỉ phần ĐỌC LẠI sai.

**Sửa lần 2:** `vis` trong `VxeRowCheckboxJs` loại thêm `visibility:hidden` / `display:none` → chọn bản fixed-left hiển thị cho cả bấm lẫn đọc.
Giữ click chuột thật (giờ nhắm đúng phần tử hiển thị), timeout click 3s → 2s, nhãn chẩn đoán "BỊ CHE" đổi thành "phần tử KHÁC nằm trên" + in `visibility`.

**Đã kiểm tra:** harness DOM giả lập (main ẩn 16×16 + fixed-left hiển thị), chạy đúng đoạn JS trích từ file .cs trong trình duyệt: chọn bản FIXED-LEFT,
click làm nó `checked`. Harness chỉ phản ánh GIẢ ĐỊNH về DOM (suy từ log) — xác nhận thật vẫn phải qua một lần chạy Auto Run.

**Nếu vẫn lỗi:** dòng `Chẩn đoán checkbox …` giờ in `visibility=` của phần tử đã chọn; `visibility=hidden` nghĩa là vẫn chọn nhầm bản ẩn.
