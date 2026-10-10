# Hướng Dẫn & Lưu Ý Về Google Apps Script (GSheet Integration)

> Tài liệu hướng dẫn cấu hình, triển khai và xử lý lỗi quyền hạn (OAuth / Google Drive) cho script đồng bộ đơn hàng Shopee lên Google Sheets.

---

## 1. Cấu hình triển khai Web App bắt buộc

Khi triển khai Google Apps Script thành Web App để nhận dữ liệu từ ứng dụng Shopee Suite, bắt buộc phải chọn 2 cấu hình sau:

| Thiết lập | Giá trị bắt buộc | Giải thích |
|---|---|---|
| **Thực thi dưới dạng** (*Execute as*) | **Tôi** (*Me*) | Web App sẽ chạy dưới danh nghĩa và quyền hạn của tài khoản Google sở hữu/triển khai script. Nhờ đó, ứng dụng máy tính gửi HTTP POST không cần xác thực tài khoản Google mà vẫn ghi được vào Sheet. |
| **Người có quyền truy cập** (*Who has access*) | **Bất kỳ ai** (*Anyone*) | Cho phép ứng dụng từ máy tính gửi dữ liệu qua Web App URL mà không bị chặn bởi trang đăng nhập Google. |

---

## 2. Cạm bẫy quyền hạn khi Đổi Tài Khoản Google (Multi-Account OAuth Pitfall)

### Hiện tượng:
- Khi chuyển Sheet sang một tài khoản Google mới, hoặc dùng một tài khoản Google khác để mở Apps Script và bấm **Deploy (Triển khai)** phiên bản mới:
- Các đơn hàng mới vẫn được tạo dòng trên Sheet (có mã đơn cột A, Shop, Doanh thu, Ngày đặt...), nhưng:
  - **Cột B (Mã vận đơn)** bị bỏ trống.
  - **Cột C (Link ảnh phiếu PDF)** bị bỏ trống.
- Nhật ký ứng dụng trên máy tính (`hoatdong-YYYYMMDD.log`) liên tục báo lỗi:
  ```
  Exception: Bạn không có quyền thực hiện lệnh gọi DriveApp.getFoldersByName.
  Các quyền cần có: (https://www.googleapis.com/auth/drive.readonly || https://www.googleapis.com/auth/drive).
  ```

### Nguyên nhân gốc rễ:
1. **Quyền OAuth gắn chặt theo tài khoản bấm Triển khai:**
   - Khi chọn `Execute as: Me`, Web App sẽ chạy dưới danh tính của **tài khoản Google vừa bấm Deploy**.
   - Nếu tài khoản cũ đã từng cấp quyền Google Drive, nhưng bạn lại dùng **tài khoản mới** để Deploy, thì tài khoản mới này **chưa từng bấm duyệt quyền OAuth cho dự án script này**.
2. **Deploy KHÔNG tự động cấp quyền Drive:**
   - Thao tác bấm nút "Triển khai" (*Deploy*) trong Google Apps Script chỉ phát hành code, **không kích hoạt hộp thoại cấp quyền (Review permissions)**.
   - Muốn cấp quyền cho tài khoản mới, tài khoản đó phải **bấm nút "Chạy" (Run) một hàm trong trình biên tập Apps Script ít nhất một lần**.
3. **Lỗi thiết kế cũ (đã khắc phục ngày 15/09/2026):**
   - Trước đây, đoạn code upload file PDF lên Google Drive nằm chung trong khối `try` chính và đứng **trước** lệnh ghi mã vận đơn. Khi `DriveApp` ném lỗi phân quyền, toàn bộ việc ghi cột B (Mã vận đơn) bị văng ra `catch` và không bao giờ được ghi.

---

## 3. Quy trình chuẩn khi triển khai hoặc đổi tài khoản

Bất cứ khi nào **thay đổi tài khoản Google** hoặc **triển khai lại Web App**, thực hiện đúng 3 bước:

### Bước 1: Dán code mới vào `Code.gs`
- Mở file `orders/gsheet-apps-script/Code.gs` bằng Notepad/VS Code, `Ctrl + A` $ightarrow$ `Ctrl + C`.
- Mở **Tiện ích mở rộng $ightarrow$ Apps Script** trên Google Sheet, dán đè vào `Code.gs`, nhấn `Ctrl + S` để lưu.

### Bước 2: Cấp quyền Google Drive (BẮT BUỘC)
- Trên thanh công cụ Apps Script (cạnh nút "Chạy" / "Gỡ lỗi"), bấm ô chọn hàm và chọn: **`capQuyenDrive`**.
- Bấm nút **Chạy** (*Run*).
- Google sẽ mở hộp thoại **"Cần có quyền truy cập"** (*Authorization required*):
  1. Bấm **Xem lại quyền** (*Review permissions*).
  2. Chọn đúng tài khoản Google đang đăng nhập.
  3. Bấm **Nâng cao** (*Advanced*) $ightarrow$ Bấm **Đi tới [Tên dự án] (không an toàn)**.
  4. Bấm **Cho phép** (*Allow*).
- Khi thanh nhật ký hiện: `Đã cấp quyền truy cập Google Drive thành công...` là hoàn tất.

### Bước 3: Triển khai phiên bản mới
- Bấm nút **Triển khai** (*Deploy*) $ightarrow$ **Quản lý bản triển khai** (*Manage deployments*).
- Bấm biểu tượng cây bút chì ✏️ (**Chỉnh sửa**).
- Tại mục **Phiên bản** (*Version*), chọn **Phiên bản mới** (*New version*).
- Đảm bảo **Thực thi dưới dạng: Tôi** và **Người có quyền truy cập: Bất kỳ ai**.
- Bấm **Triển khai** (*Deploy*).

---

## 4. Cơ chế phòng thủ trong `Code.gs` (Cập nhật 15/09/2026)

1. **Cô lập lỗi Drive (`try/catch` riêng):**
   Khối upload Drive được bọc riêng biệt. Nếu Google Drive gặp lỗi quyền, hết dung lượng hoặc đứt mạng:
   - Script ghi lại cảnh báo vào `traVe.canhBaoDrive` và `r.driveError`.
   - Script **vẫn tiếp tục chạy bình thường**, ghi đầy đủ Cột A (Mã đơn), **Cột B (Mã vận đơn)**, Shop, Doanh thu, Ngày đặt, SKU, Phân loại...
2. **Tự phục hồi dữ liệu phía Client C#:**
   - Ứng dụng máy tính chỉ đánh dấu đã hoàn thành file phiếu khi nhận được `fileUrl` hợp lệ.
   - Nếu lúc đầu upload Drive lỗi, ô link ảnh tạm thời để trống, nhưng app vẫn giữ trạng thái cần đẩy file. Ngay khi bạn cấp quyền Drive thành công, ở chu kỳ chạy tiếp theo, app sẽ tự động upload lại file phiếu và điền bổ sung vào Cột C mà không mất dữ liệu.
