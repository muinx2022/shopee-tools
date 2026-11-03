# Plan: Import nhận ra hộp tiến trình "Importing… Finish"

- **Ngày:** 2026-11-03
- **Trạng thái:** code xong qua 5g, chờ user commit và chạy thử trên site thật
- **Người lập:** phiên chính · **Người thực thi:** `opus-dev`
- **Mốc code:** `cf7816b` trên `main`

## 1. Bối cảnh & mục tiêu

Commit `1755d7f` thêm `ImportPostClickClassifier`: còn hộp lạ sau khi bấm Import thì coi là chưa chắc đã gửi và dừng import shop. Trên site thật, sau khi bấm Import to Stores luôn có hộp tiến trình, nguyên văn từ log ngày 03/11:

- `Importing Shopee Total: 50 Importing... Finish`
- `Importing Finish`

Hậu quả: mỗi shop dừng sau lô đầu, lô không được MarkDone, `after == 0` nên Auto Run bỏ qua Update (`AutoRunViewModel.cs:487`). Log có 6 lô như vậy trong sáng 03/11.

Mục tiêu: hộp tiến trình không có lỗi thì coi lô đã gửi, import đi tiếp hết sheet (3.703 item id), Auto Run sang được Update.

## 2. Phạm vi

**Làm:**
- `ImportPostClickClassifier`: thêm nhận diện hộp tiến trình; hộp này không còn là "hộp lạ". Có từ lỗi với số khác 0 thì vẫn chưa chắc.
- `MonitorPostClickAsync`: log nội dung hộp tiến trình mỗi khi đổi, để lấy mẫu trạng thái cuối.
- Kiểm tra hộp tiến trình được đóng trước khi sang trang kế; nếu `DismissPostImportDialogsAsync` chưa đóng được thì bổ sung nút Finish.
- Test.

**Không làm:**
- Không đánh dấu tự động 6 lô đã gửi sáng nay; user đối chiếu trên BigSeller trước.
- Không đổi nhánh bỏ qua Update khi `after == 0`.
- Không sửa `suite/`, không đổi hộp "Hộp nháp".

## 3. Các bước

1. `ImportPostClickClassifier.IsImportProgressBox(text)`: chứa đồng thời `importing` và `finish` (không phân biệt hoa thường).
2. `Classify`: thứ tự kiểm tra cũ giữ nguyên; hộp tiến trình có `BoxReportsError` thì chưa chắc, không lỗi thì bỏ qua như hộp đã biết. Điều hướng mà chỉ có hộp tiến trình vẫn chưa chắc.
3. Monitor log nội dung hộp tiến trình khi đổi; không `return` sớm, vẫn theo dõi đủ 5 giây để bắt toast lỗi.
4. Đọc `DismissPostImportDialogsAsync`; nếu không đóng được hộp tiến trình thì thêm bấm Finish, và chỉ bấm khi nút bật.

## 4. Tiêu chí nghiệm thu

| # | Kiểm tra | Mong đợi |
|---|---|---|
| 1 | `dotnet build ShopeeWorkspace.sln --no-incremental` | 0 error, 0 warning |
| 2 | `dotnet test tests/Shopee.Core.Tests`, `tests/Shopee.Workspace.Tests` | Xanh |
| 3 | Classify với 2 câu nguyên văn trên, URL đúng | `Sent = true` |
| 4 | Hộp tiến trình kèm `Failed: 3` | `Sent = false` |
| 5 | Hộp tiến trình kèm `Failed: 0` | `Sent = true` |
| 6 | `Import failed: shop quota exceeded`, hộp chỉ có `Finish`, hộp chỉ có `Importing` | `Sent = false` |
| 7 | Hộp tiến trình + toast lỗi, hoặc + URL lệch, hoặc + điều hướng | `Sent = false` |
| 8 | Thử phá: bỏ điều kiện `finish`, bỏ kiểm tra lỗi | Test đỏ; khôi phục |
| 9 | Đọc code: hộp tiến trình được đóng trước khi sang trang kế | Đúng |

Cần user chạy thử trên site thật sau khi xong.

## 5. Rủi ro

- Chưa thấy trạng thái cuối của hộp; câu báo lỗi có thể không dùng từ lỗi đã biết. Log nội dung để bổ sung.
- Sang trang kế khi BigSeller còn đang xử lý lô.

## 5b. Đổi hướng theo ảnh user gửi (thay các mục 2-4 ở chỗ khác nhau)

User chụp hộp kết quả thật, gồm các phần: tiêu đề `Importing`, thanh tiến trình, `Shopee`, `Total: 1`, `Successfully: 1`, `Failed: 0`, link `Draft Listing`, nút `Finish`, nút X. Hộp chọn shop có `Draft: 9,514/10,000`.

Luồng user muốn: chọn shop → Import → hộp này → bấm Finish → sang trang kế. Không đóng Brave giữa các lô. Item id đã import lưu theo tài khoản + shop, chạy lại thì bỏ qua. Kho này đã có sẵn: `OpProgressStore` (`op-progress.json`), được seed ở `RunAsync`, nên KHÔNG tạo bảng mới. Brave chỉ bị đóng vì runner dừng shop, nên khi lô thành công không dừng thì Brave không đóng.

Đổi so với bản đang có trong cây làm việc:
1. Parser thuần đọc `Total`, `Successfully`, `Failed` từ nội dung hộp. Hộp `Importing` chưa đủ số, hoặc `Successfully + Failed < Total`, là đang chạy.
2. `MonitorPostClickAsync` chờ hộp đến trạng thái cuối, tối đa 120 giây; hết giờ là chưa chắc.
3. Kết quả cuối:
   - `Successfully == Total`, `Failed == 0`, `Total` bằng số SP đã tick: đã gửi, MarkDone cả lô.
   - `Failed == Total`: chưa gửi lô nào, không MarkDone, dừng shop, log nguyên văn.
   - Thất bại một phần: không biết SP nào lỗi, không MarkDone, dừng shop, log nguyên văn và danh sách id.
   - `Total` khác số đã tick: chưa chắc.
4. Link `Draft Listing` được tính như `Hộp nháp`.
5. `DismissPostImportDialogsAsync`: nếu thấy nút `Finish` trong hộp đang hiện thì bấm và chờ hộp đóng; không có mới dùng cách cũ. Việc này chạy sau MarkDone.

Tiêu chí thêm: test parser và Classify với nguyên văn `Importing Shopee Total: 1 Successfully: 1 Failed: 0 Draft Listing Finish`; lô 50/50; `Failed: 50`; một phần; đang chạy; `Total` lệch. Đọc code thấy lô thành công không đi qua `return` nào của `RunAsync` trừ khi đủ hết.

## 5c. Xóa sạch nháp trước khi import (user yêu cầu 03/11)

**User đã chốt:** Clear trước import MỖI shop; áp dụng cả Auto Run lẫn Import thủ công; SP đã import mà chưa Update xong thì xóa khỏi bảng import để lấy lại.

Thao tác (DOM user dán, bản tiếng Anh):
1. Mở `https://www.bigseller.com/web/listing/shopee/index.htm?bsStatus=1`.
2. Di chuột vào nút `button.ant-btn-primary` có chữ `Bulk Actions` (trong `.dropdown_box`, `ant-dropdown-trigger`).
3. Menu `.ant-dropdown:visible li.ant-dropdown-menu-item` chữ `Clear` → bấm.
4. Hộp `.ant-modal-confirm` có câu `The task of clearing all filtered products cannot be stopped...` → bấm `.ant-modal-confirm-btns button.ant-btn-primary` chữ `Confirm`.

Yêu cầu thiết kế:
- Nằm trong `BigSellerImportToStoreRunner.RunAsync`, sau đăng nhập, trước Crawl List, nên áp cho cả thủ công lẫn Auto Run.
- Nếu không còn nháp nào thì bỏ qua Clear và log.
- Chỉ bấm Confirm khi hộp đúng câu trên; hộp khác thì Cancel và dừng import, không đoán.
- Sau Confirm, chờ danh sách nháp về 0 (poll, có hạn giờ, ví dụ 10 phút). Hết giờ thì dừng import shop, log rõ.
- **Bảng import:** Clear xóa nháp của CẢ tài khoản, nên ngay sau khi bấm Confirm phải bỏ khỏi Done của Import mọi id chưa có trong Done của Update cùng khóa, cho MỌI khóa tiến độ của tài khoản đó, không chỉ shop đang chạy. Làm trong `OpProgressStore` bằng hàm thuần có test.
- Sau đó mới seed `_importedIds` và nạp danh sách.

Gộp thêm từ phản biện (03/11):
- Sau khi bấm Finish phải kiểm tra không còn modal hiện; còn thì dừng, không chạy lô sau.
- Chụp danh sách modal đang hiện trước khi bấm Import, loại chúng khỏi phần theo dõi để hộp cũ không làm lô sau "đã gửi" oan.
- Tab `Chưa nhận`: SP đã import rời tab nên sau lô thành công KHÔNG bấm Next, mà tải lại đúng trang hiện tại. Tab `Đã nhận` giữ Next.
- Chỉ coi là đã gửi khi đọc được số `Total/Successfully/Failed` ở trạng thái cuối, hoặc có link `Draft Listing`/`Hộp nháp` mà không có lỗi. Không có hai thứ đó thì không coi là đã gửi.

Ghi chú: 6 lô sáng 03/11 không cần nạp tay vào bảng, vì Clear sẽ xóa nháp của chúng và bảng không chứa chúng nên chúng được import lại một lần.

## 5d. Sửa theo phản biện Clear (03/11)

1. **N1:** khóa Import của tài khoản bị bỏ id thì `Status` về `stopped`, để `ImportDone` của shop thành false (xét cả các khóa ứng viên mà `WorkspaceShopViewModel` dùng). Có test.
2. **N2:** mọi nhánh sau Confirm đều seed lại `_importedIds` trước khi return; ngoài `Cleared`/`AlreadyEmpty` thì không được ghi completed.
3. **T1:** chỉ tải lại trang khi lô đã `committed`; lô lỗi đi Next như cũ.
4. **T2:** tải lại đưa về trang 1, nên đặt lại `pageNum = 1` và sửa comment/log cho đúng.
5. **T3 + AlreadyEmpty:** luôn thử Clear kể cả khi trang trông rỗng. Có hộp confirm đúng câu thì Confirm và reset; menu/hộp không hiện thì coi là không có gì để xóa, không reset. Chờ về 0 ưu tiên đọc tổng số từ phân trang hoặc response API nếu code có sẵn cách đọc, không thì giữ đếm dòng nhưng đòi thêm thời gian ổn định.
6. **T4:** click Confirm ném lỗi mà hộp confirm đã biến mất thì coi là đã Confirm và reset. Lệch về phía reset vì nháp trùng sẽ bị xóa ở lần Clear sau, còn không reset thì mất SP.

**Không sửa N3** (SP đã publish mà thiếu Update Done bị import lại, có thể ra listing trùng): cần đọc item id nguồn của từng nháp trước khi Clear, chưa có cách tin cậy. User xác nhận (03/11): BigSeller tự đưa SP trùng vào một danh sách riêng, nên chấp nhận, không cần sửa.

## 5e. Vòng sửa cuối theo phản biện lần 3 (03/11)

1. **Vòng lặp giữa các shop (nặng):** reset KHÔNG bỏ id của khóa có Update đang completed, vì đó là shop đã xong hoặc user đã bấm Next shop.
2. **Dòng không phải mục tiêu Update** (cột tên đã sửa trống) không bao giờ có Update Done, nên reset không được bỏ chúng; nếu không, chúng bị import lại mỗi lượt. Runner đọc sheet của từng khóa bị ảnh hưởng (khóa = tên sheet trong file nguồn) để biết id nào là mục tiêu Update. Không đọc được sheet của khóa nào thì giữ nguyên id của khóa đó và log.
3. Hộp confirm không hiện mà bảng nháp còn dòng thì `Aborted`, không phải "không có gì để xóa".
4. Reset bảng ngay khi đã kiểm hộp confirm đúng câu, TRƯỚC khi bấm Confirm (lệch về phía reset).
5. Log Auto Run không in số âm.

**Chấp nhận, ghi cho user:** Clear chạy trước khi biết lượt import có làm được gì (shop đổi tên, map sai cột thì vẫn xóa nháp); tải lại về trang 1 sau mỗi lô tốn thời gian nếu nhiều trang đầu không có SP khớp.

## 5f. Vòng sửa thêm (user đồng ý 03/11)

1. Hễ sắp Clear (không thoát sớm) thì LUÔN đọc mục tiêu cho mọi khóa của tài khoản còn id thiếu Update Done (`ImportKeysPendingUpdate`), ngay trước Clear, sau điều kiện thoát sớm.
2. Còn khóa có id chờ Update (Update chưa completed) mà không biết mục tiêu (khóa `shared:`, sheet không có, đọc file lỗi) thì KHÔNG Clear: dừng import shop trước khi mở menu, log tên khóa và lý do. Khóa của chính shop đang chạy ở chế độ workbook chung: đọc mục tiêu theo `_settings.DataSheet`.
3. `NoClearItem` mà bảng nháp còn dòng thì `Aborted`, chỉ bảng rỗng mới `NothingToClear`; sửa test đang khóa hành vi cũ.
4. Test điều phối: tách phần quyết định "đọc khóa nào / có được Clear không" thành hàm thuần và test các ca A (import S1 rồi S2 trước Update), B (Update S1 dừng giữa chừng), khóa không đọc được.

## 5g. Vòng sửa tiếp (user đồng ý 03/11)

1. Workbook chung đang TẮT: khóa `shared:` (dữ liệu cũ) không được tính là "chờ Update" khi xét cổng Clear, và reset không đụng tới chúng. Log một dòng liệt kê khóa cũ bị bỏ qua (kèm tên shop nếu đổi được từ id qua `account.Shops`). Workbook chung BẬT thì giữ như 5f.
2. Import ghi thêm Done vào một khóa mà Update cùng khóa đang completed thì hạ Update của khóa đó về `stopped` (trong `OpProgressStore`, có test).
3. Tách phần điều phối của runner (thoát sớm, khóa cần đọc, chặn/Clear) thành hàm thuần, test các ca A, A2, B, E (khóa `shared:` cũ), F (shop đã ✓ Update import thêm).
4. Auto Run: Import bị chặn bởi cổng Clear thì log rõ "bị chặn: …", không ghi "0 SP".
5. Reset còn khóa bị bỏ qua vì không biết mục tiêu thì trả false (không Confirm). Log thoát sớm khi đọc mục tiêu lỗi không được nói "mọi SP đã Update".

## 6. Báo cáo thực thi

- **Trạng thái:** đã hết 2 vòng sửa (5d, 5e), dừng lại chờ user quyết; chưa commit, chưa chạy trên BigSeller thật.
- **Kiểm chứng:** nghiệm thu tự chạy lại: build 0 warning; Core.Tests 642/642; Workspace.Tests 144/144.
- **Phản biện 5e:** vòng lặp giữa các shop đã hết. Còn các lỗi chưa sửa:
  1. Nặng: shop đang chạy không còn SP chờ Update thì không đọc mục tiêu của shop khác, Clear xóa nháp của shop khác mà ✓ Import vẫn giữ, SP mất vĩnh viễn (đã mô phỏng). Sửa: luôn đọc mục tiêu trước Clear.
  2. Nặng: khóa không biết mục tiêu (workbook chung `shared:`, file đang bị khóa, sheet lệch tên) vẫn bị Clear. Sửa: có khóa còn id chờ Update mà không biết mục tiêu thì không Clear.
  3. Trung bình: menu không có mục Clear mà bảng còn dòng vẫn báo "không có gì để xóa". Sửa: bảng còn dòng thì Aborted.
  4. Nhẹ: Hub-mode (đang tắt), cột map khác giữa shop, khóa `ShopeeDataSheet`.
- Bản vá đang để trong cây làm việc.
- **Sau 5f (03/11):** nghiệm thu đạt 4/4 (build 0 warning; Core.Tests 649/649; Workspace.Tests 146/146). Phản biện: ca A/B/C không còn mất SP. Còn:
  1. Nặng: khóa `shared:` cũ (workbook chung đã tắt) bị coi là chờ Update mà không đọc được, nên tài khoản `hoangdh10031992@gmail.com` bị chặn Import vĩnh viễn; Auto Run ghi "0 SP" che lý do.
  2. Nặng: shop đã ✓ Update, import thủ công thêm dòng mới rồi shop khác Clear thì mất SP mới (Update completed không bị hạ khi Import ghi thêm).
  3. Trung bình: phần điều phối trong runner chưa có test.
- **Sau 5g (03/11):** nghiệm thu đạt 5/5 (build 0 warning; Core.Tests 664/664; Workspace.Tests 146/146). Phản biện chạy mô phỏng trên dữ liệu thật của cả 6 tài khoản khi workbook chung tắt: không mất SP, không bị chặn. Còn lại, chưa sửa:
  1. Trung bình: bật workbook chung → tắt → bật lại thì SP thuộc khóa `shared:` bị Clear xóa nháp mà không được import lại.
  2. Trung bình: hạ Update khi Import thêm id chỉ áp vào đúng khóa đó, không áp các khóa ứng viên khác của shop (chưa gặp trên dữ liệu thật).
  3. Nhẹ: Auto Run có thể in lý do "bị chặn" cũ của shop trước khi shop sau lỗi kiểm tra cấu hình.
  - Ngoài phạm vi: đổi bật/tắt workbook chung làm đổi khóa tiến độ nên Import/Update làm lại cả shop.
- Trạng thái: dừng sửa, chờ user commit và chạy thử trên site thật. Đừng bật/tắt workbook chung cho tới khi xử lý mục 1.
