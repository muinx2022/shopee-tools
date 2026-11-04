# Plan: Đóng popup lạ trước khi chọn shop import

- **Ngày:** 2026-11-04
- **Trạng thái:** code xong, chờ chạy Auto run trên site; chưa commit
- **Người lập:** phiên chính · **Người thực thi:** agent `dev`

## 1. Bối cảnh

Auto run 04/11, 08:59, shop `fflqam22bl`: bấm «Import to Stores» xong thì báo `không tìm thấy shop`, `Available` trống, rồi dừng cả import vì còn hộp thoại `BigSeller 11.11 Mega Sale is officially here! … Close`.

Nguyên nhân ở `BigSellerImportToStoreRunner.ImportBatchAsync`: hộp được đọc là `.ant-modal-content:visible` cuối cùng. Popup sale đã mở sẵn nên `WaitFor` trả ngay, không đợi hộp chọn shop. Popup không có checkbox shop. Sau lô chưa gửi, đoạn «còn hộp thoại thì DỪNG import» thấy popup còn mở và dừng cả shop. Cùng kiểu với 5 shop sau. Import tay được vì popup đã đóng từ trước.

Lớp phủ hướng dẫn (guide mask) đã có `DismissGuideMasksAsync`. Log «lớp phủ hướng dẫn» in cả khi click bị popup sale chặn — câu log đó không phải bằng chứng còn guide.

## 2. LÀM

Không có danh sách popup. Không so chữ chiến dịch, không nhánh «nếu sale 11.11 / nếu guide / nếu popup mai». Popup ngày sau, chưa từng thấy, vẫn đóng được nếu nó là hộp thoại hoặc lớp che.

1. Hàm thuần `ShouldCloseStrayModal` trong `workspace/src/Shopee.Core/BigSeller/` (file mới). Input chỉ là cờ cấu trúc, không phải nội dung quảng cáo:
   - Giữ khi hộp có nút `Import to Stores`, hoặc có checkbox shop, hoặc có nút `Finish` / `Hoàn thành`, hoặc là hộp listing limit (caller đặt cờ bằng `BigSellerSaveSuccessHelper.IsListingLimitFailure` — tín hiệu lỗi đã có, không phải tên popup).
   - Còn lại → đóng.
2. `BigSellerCrawlHelper`: với mỗi hộp đang hiện (`[role=dialog]`, `.ant-modal-wrap`), hỏi hàm trên. Hộp cần đóng: bấm nút đóng có sẵn trên hộp (`.ant-modal-close`, `[aria-label=Close]`, nút chữ `Close` / `Đóng` / `×`), không có thì Escape. Lớp che click (phần tử fixed phủ nút, mask) thì gỡ khỏi DOM, không cần đúng class `language_switch_guide`. Best-effort, lặp vài vòng, không ném.
3. `ClickToolbarImportAsync`: trước click thật, gỡ guide mask như cũ và đóng popup lạ. Click vẫn bị chặn thì mới click JS. Đổi câu log JS thành «click thường bị chặn, đã bấm Import to Stores bằng JS» — không khẳng định đó là lớp phủ hướng dẫn.
4. `ImportBatchAsync`: sau khi bấm toolbar, chờ tối đa 15 giây hộp **có nút Import to Stores**. Mỗi vòng đóng popup lạ rồi tìm lại. Hết giờ mà không có hộp đó thì ném lỗi chưa gửi (đường `NotSent` sẵn có), không đọc checkbox trong popup sale.
5. Chỗ «Còn N hộp thoại → DỪNG import»: gọi đóng popup lạ một lần rồi đếm lại. Chỉ dừng nếu vẫn còn hộp. Popup sale vừa đóng thì import sang trang kế, không dừng shop.
6. Test thuần hàm quyết định, không mở Brave.

## 3. KHÔNG LÀM

- Không đổi cách khớp tên shop (`ImportShopMatcher`).
- Không lập danh sách tên popup. Không thêm nhánh cho 11.11 hay bất kỳ chiến dịch nào.
- Không đóng hộp chọn shop, hộp tiến trình Importing/Finish, hộp listing limit, hộp «Hộp nháp».
- Không sửa Update, listing limit, cột tiến độ.
- Không sửa file phiên khác đang `M`, và không sửa file việc «Đã đạt giới hạn SP» đang code: `OpProgressStore.cs`, `UpdateProductViewModel.cs`, `WorkspaceShopViewModel.cs`.
- Không sửa `suite/`. Không commit.

## 4. Hành vi

| Trang lúc bấm Import | Kết quả |
|---|---|
| Popup sale (hoặc popup chỉ có Close) đang che | Đóng popup, rồi chọn shop trong hộp Import to Stores |
| Chỉ có hộp chọn shop | Giữ, chọn shop như hiện tại |
| Hộp Finish / Hoàn thành | Không bấm Close của hộp đó |
| 15 giây không thấy hộp Import to Stores | Lô chưa gửi, không MarkDone, không báo «Available» trống từ popup sale |

## 5. Tiêu chí

1. `dotnet test workspace/tests/Shopee.Core.Tests/Shopee.Core.Tests.csproj --filter "FullyQualifiedName~StrayModal" --nologo` — xanh. Ca: không nút import / không checkbox / không Finish → đóng; có nút Import to Stores → giữ; có checkbox shop → giữ; có Finish hoặc Hoàn thành → giữ.
2. `dotnet build workspace/src/Shopee.Module.UpdateProduct/Shopee.Module.UpdateProduct.csproj --nologo` — 0 error, 0 warning mới từ file vừa sửa.
3. Đọc lại `ImportBatchAsync`: locator hộp shop là hộp có nút `Import to Stores`, không còn `.ant-modal-content:visible` + `.Last` cho bước chọn shop.
