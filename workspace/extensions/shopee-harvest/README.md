# Shopee Harvest Runner (extension)

Lấy dữ liệu chi tiết sản phẩm Shopee (JSON `get_pc` mà trang tự tải) và **đẩy thẳng lên Hub**. Trên mỗi trang sản phẩm
Shopee có một **hộp nổi ở góc dưới bên phải**; bấm **"Lấy dữ liệu"** thì extension mới lấy và gửi Hub.

Dùng được 2 cách:

- **Bấm tay** (máy không có app Shopee Harvest): mở SP nào ưng thì bấm. SP lưu lên Hub **không gắn nhóm** (không sheet,
  không dòng).
- **Tự động** (app Shopee Harvest điều khiển): app tự mở link và tự bấm đúng nút đó. App truyền cấu hình Hub của app — không
  cần nhập gì trong extension.

## Cài trên máy khác

1. Lấy thư mục extension:
   - từ máy đã cài app Shopee Harvest: thư mục `extensions\shopee-harvest` nằm cạnh `ShopeeHarvest.exe`; hoặc
   - từ file zip người quản lý gửi: giải nén ra một thư mục cố định (đừng để trong Downloads rồi xoá).
2. Mở Chrome / Brave, vào `chrome://extensions` (Brave: `brave://extensions`).
3. Bật **Chế độ dành cho nhà phát triển** (Developer mode) ở góc trên bên phải.
4. Bấm **Tải tiện ích đã giải nén** (Load unpacked) → chọn thư mục `shopee-harvest` (thư mục có file `manifest.json`).
5. Ghim extension lên thanh công cụ cho tiện (biểu tượng mảnh ghép → ghim "Shopee Harvest Runner").

Cập nhật bản mới: chép đè thư mục cũ rồi bấm nút tải lại (↻) của extension trong `chrome://extensions`, sau đó F5 các tab
Shopee đang mở.

## Nhập Hub URL và token (một lần)

1. Bấm biểu tượng extension → **Cài đặt Hub** (hoặc trên hộp nổi bấm **Mở cài đặt**).
2. Nhập:
   - **URL Hub**: vd `https://api.schedra.net` (hỏi người quản lý Hub). Chỉ nhận `https://…`, hoặc `http://localhost:cổng`
     khi chính máy này là Hub.
   - **Token**: token chung các app đang dùng (header `X-Api-Token`).
   - **Mã máy**: tên ngắn để biết SP lấy từ máy nào (vd `ext-may-ke-toan`).
3. Bấm **Lưu**. Nếu URL Hub khác Hub mặc định, Chrome hỏi cấp quyền truy cập trang đó → bấm **Cho phép**.
4. Bấm **Kiểm tra kết nối**: thấy "✓ Kết nối Hub được, token đúng." là xong.

Token chỉ lưu trong trình duyệt này (`chrome.storage.local`), không nằm trong file extension.

## Cách dùng (bấm tay)

1. Đăng nhập Shopee trên trình duyệt (như mua hàng bình thường).
2. Mở trang sản phẩm. Hộp nổi hiện:
   - SP đã có trên Hub chưa, lấy lúc nào;
   - số mục đang chờ gửi / lỗi trong hàng đợi của extension;
   - kết quả lần bấm gần nhất.
3. Bấm **Lấy dữ liệu**. Chờ vài giây:
   - **✓ Đã lưu Hub** — xong.
   - **⏳ Hub chưa nhận — đã vào hàng đợi** — mất mạng hoặc Hub đang lỗi; extension tự gửi lại (lùi dần 30 giây → 15 phút),
     không cần bấm lại.
   - **✗ …** — lỗi, đọc lý do trên hộp: mất đăng nhập Shopee, gặp captcha (giải xong rồi bấm lại), không phải trang sản phẩm,
     Hub từ chối dữ liệu…
4. Popup của extension (bấm biểu tượng) có mục **Gửi Hub**: số chờ gửi / lỗi, danh sách mục lỗi và nút **Gửi lại** (gửi ngay
   các mục đang chờ và đưa mục lỗi về hàng chờ).

Hub từ chối dữ liệu ngay lần gửi đầu thì SP vẫn nằm trong hàng đợi ở trạng thái lỗi (kèm lý do) để xem và **Gửi lại**.

Chế độ tự động (app bấm nút): giữ zoom tab Shopee ở **100%** (Ctrl+0) và không để gì che góc dưới phải trang — trang đang
phóng to / nút bị che thì app không bấm; 3 dòng liên tiếp bấm không ăn thì app dừng job và báo lý do.

Hàng đợi giữ tối đa 100 SP (~6 MB). Đầy thì lần bấm sau báo lỗi rõ — kiểm tra kết nối Hub rồi bấm **Gửi lại**.

## Riêng tư

Trước khi gửi, extension cắt thông tin tài khoản Shopee đang đăng nhập (`data.account`), địa chỉ giao hàng
(`delivery_address`, cả toạ độ), phí / kênh giao theo địa chỉ, voucher của người xem. Hub cũng tự cắt các khối này nếu có
client nào gửi lên.

## Cho người phát triển

- Test: `node --test "extensions/shopee-harvest/tests/*.test.mjs"` (chạy từ thư mục `workspace/`).
- `pdp-clean.js` là bản JS của `Shopee.Core/Harvest/HarvestPayloadCleaner.cs` + `ShopeePdpParser.cs` — phải ra **giống
  hệt** (cùng JSON sạch, cùng `raw_hash`). Cả hai bên đối chiếu file `tests/Shopee.Core.Tests/Fixtures/pdp-clean/expected.json`.
  Cố ý đổi luật làm sạch: sửa CẢ HAI bản, thêm ca vào `cases.json`, sinh lại file kỳ vọng bằng
  `set HARVEST_PDP_GOLDEN_WRITE=1` rồi chạy test `PdpCleanGoldenTests.SinhLaiFileKyVong_ChiKhiDatBienMoiTruong`, xem diff.
- Đổi phiên bản .NET của app có thể đổi bảng ký tự bị escape (`ESCAPED_BMP_RANGES` trong `pdp-clean.js`) — test đối chiếu
  sẽ báo.
