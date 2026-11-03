# Import một lần cho mọi shop của acc (03/11)

## Vấn đề

- 15 sheet shop trong `data/Sản phẩm shopee resell - Linh QA - 18 shops.xlsx` dùng chung đúng 3.703 item id.
  Scrape một lần vào acc là đủ nguồn cho mọi shop của acc.
- Runner import hiện chọn đúng 1 shop trong hộp "Import to Draft Listing / Import to hộp nháp"
  (`SelectImportShopAndConfirmAsync`, BigSellerImportToStoreRunner.cs:672 — bỏ tick mọi shop khác, tick 1 shop).
- SP import xong rời Crawl List (tab Đã nhận chỉ còn 10–14 SP), nên shop chạy đầu (shop9x) lấy hết,
  shop sau (alina99) gần như không còn gì để import → phải scrape lại cho từng shop.
- Auto Run gọi Import trong vòng lặp từng shop (AutoRunViewModel.cs:453–540).

## Hộp chọn shop (DOM user gửi, EN + VN giống cấu trúc)

- Tiêu đề: "Import to Draft Listing" / "Import to hộp nháp".
- Danh sách shop: `.cont_btm label.ant-checkbox-wrapper` (alina99.store, shop9x.store, six6shop.store),
  có "Select All" / "Chọn tất cả" ở `.cont_up`. Nhiều checkbox → tick nhiều shop được.
- Nút "Import to Stores" (cả 2 ngôn ngữ), `disabled` khi chưa tick.
- Cạnh đó có "Draft: 0/10,000" / "Hộp nháp: 0/10,000".

## Thiết kế

1. **Runner nhận danh sách shop đích** của acc (tên shop + ProgressKey + tập item id của sheet shop đó),
   thay cho 1 shop. Gọi từ 1 shop như cũ vẫn chạy (danh sách 1 phần tử).
2. **SP cần import** = id có trong tập của ít nhất 1 shop đích mà shop đó chưa MarkDone.
   Seed tiến độ: đọc Import Done của TỪNG ProgressKey.
3. **Mỗi trang:** gom SP khớp theo "tập shop còn cần SP đó". Mỗi nhóm = 1 lần Import, tick đúng các shop
   của nhóm (bình thường cả trang chung 1 nhóm = mọi shop → 1 lần Import/trang như hiện nay).
   Không tick shop đã có SP đó → không tạo nháp trùng.
4. **Chọn shop:** `ImportShopMatcher.Pick` từng shop; thiếu hoặc mơ hồ shop nào thì KHÔNG import lô đó
   (ném trước click, như hiện nay). Bỏ tick shop ngoài nhóm, tick đủ shop trong nhóm, đọc lại xác nhận
   đúng tập đã tick trước khi bấm.
5. **Hộp kết quả:** chưa có mẫu khi tick nhiều shop. Chấp nhận Total = số SP × số shop hoặc = số SP;
   vẫn đòi Successfully = Total và Failed = 0. Log nguyên văn hộp để đối chiếu lần chạy đầu.
6. **MarkDone:** lô thành công → MarkDone từng id cho TỪNG ProgressKey trong nhóm. Lô thất bại/chưa chắc:
   giữ đường dừng hiện tại, không MarkDone shop nào.
7. **Auto Run** — user chốt 03/11: thêm checkbox **"Import cho tất cả các shop"** ở AutoRunView, MẶC ĐỊNH TẮT,
   lưu lại giữa các lần mở app.
   - Tắt: chạy đúng flow cũ — từng shop: Import (1 shop) rồi Update, lần lượt.
   - Bật: mỗi acc chạy Import MỘT lần cho mọi shop chưa ImportDone (trước vòng lặp shop), rồi vòng
     lặp shop chỉ còn Update. Giữ: nút Dừng, bỏ qua shop, luôn Update sau Import, log số SP theo từng shop.
8. **Import thủ công (tab Workspace, khung tài khoản)** — user chốt 03/11:
   - Ở header tài khoản (WorkspaceView.xaml ~dòng 576, cạnh "Reset tất cả shop") thêm checkbox
     **"Import cho tất cả các shop"** và nút **"Import"** riêng (import 1 lần cho mọi shop của acc).
   - Checkbox bật: nút Import của từng shop (cột `ColImport`, `ImportToggleEnabled`, ~dòng 753–784) bị
     disable; nút "Import" của tài khoản dùng được. Checkbox tắt: nút "Import" của tài khoản disable,
     nút từng shop hoạt động như cũ (import 1 shop, đường cũ, danh sách 1 shop).
   - Nút "Import" tài khoản khi đang chạy thì thành nút Dừng (như nút từng shop), dừng được bằng CancelJob.
   - Trạng thái checkbox lưu theo tài khoản (giữ qua lần mở app), mặc định BẬT.
9. Không đụng Update, không bật lại Clear.

## Test

- Core: gom nhóm theo tập shop còn cần; seed tiến độ nhiều shop; phán hộp kết quả Total = n và n × k.
- Matcher nhiều shop: đủ / thiếu 1 shop / mơ hồ → không import.
- Auto Run: Import gọi 1 lần/acc với đủ shop chưa xong; Update vẫn chạy từng shop.

## Sau khi sửa

- Reset tiến độ Import của shop9x.store (3.177 id ghi đã import nhưng nháp đã bị Clear xoá) — cần user đồng ý.
- Scrape lại 1 lần vào acc andre-giulibr, chạy Auto Run.
