# Plan: Dừng mọi lane của shop khi BigSeller báo toast vượt giới hạn sản phẩm

- **Ngày:** 2026-11-02
- **Trạng thái:** user quyết giữ cả tự dừng, chấp nhận các lỗi còn lại ở mục 7; chưa commit, chờ nguyên văn toast thật
- **Người lập:** phiên chính · **Người thực thi:** `opus-dev`
- **Mốc code:** `1755d7f` trên `main`

## 1. Bối cảnh & mục tiêu

Khi Update (bấm Lưu & Publish), BigSeller có thể báo bằng **toast** rằng số sản phẩm vượt giới hạn được phép. User chưa
chụp được nguyên văn. Hiện app chỉ nhận listing limit từ **hộp thoại** (`IsListingLimitFailure`), còn toast ở bước lưu không
được đọc: mỗi lượt lưu chờ hết 60s, mỗi SP fail rồi sang SP kế, lặp lại trên mọi lane.

Mục tiêu: gặp toast vượt giới hạn thì dừng ngay mọi lane đang update shop đó, dùng cơ chế sẵn có
`UpdateTargetTracker.MarkListingLimitReached()` (hủy `shopLimitCts` trong `UpdateProductRunner`).

## 2. Phạm vi

**Làm** (chỉ trong `workspace/`):
- Bộ nhận diện thuần text cho toast vượt giới hạn, có test.
- Đọc toast (DOM sống và buffer máy ghi sẵn có) trong lúc chờ xác nhận lưu; khớp thì chốt listing limit.
- Log nguyên văn toast khi khớp, và log nguyên văn toast lỗi khi lưu hết giờ mà không khớp (để lấy mẫu thật).
- Đổi các câu log đang khẳng định "trên Shopee (listing limit)" thành trung lập, vì nguồn giới hạn có thể là BigSeller.

**Không làm:**
- Không dừng theo cả tài khoản; chỉ dừng shop đang chạy (user yêu cầu theo shop).
- Không đổi trạng thái cuối: vẫn đi đường listing limit hiện có (`ShouldMarkCompleted` trả true để Auto Run sang shop kế).
- Không sửa Import, Scrape Multi, `suite/`.
- Không sửa các ca báo thành công oan (URL đổi, tab đóng).

## 3. Các bước

1. Thêm `workspace/src/Shopee.Core/BigSeller/ProductLimitToastClassifier.cs`: `public static bool IsProductLimitToast(string? text)`,
   tự chuẩn hóa (bỏ dấu, đ→d, thường, gộp khoảng trắng). Khớp khi có **tín hiệu giới hạn** VÀ **từ chỉ sản phẩm**, và KHÔNG có từ loại trừ:
   - Giới hạn: `listing limit`; `vuot qua` + `gioi han`; `vuot qua so luong`; `dat gioi han`; `gioi han dang`; `so luong` + `toi da`;
     `exceed` + (`limit`|`maximum`|`quota`); `reach` + (`limit`|`maximum`).
   - Sản phẩm: `san pham`, `product`, `listing`. Riêng `listing limit` tự đủ.
   - Loại trừ (giới hạn của trường hoặc media, không phải số SP): `ky tu`, `character`, `ten san pham`, `product name`, `mo ta`,
     `description`, `hinh anh`, `image`, `video`, `media`, `dung luong`, `kich thuoc`, `bien the`, `phan loai`, `variation`.
2. `BigSellerMaterialCenterCleaner`: thêm hàm đọc toast khớp (DOM sống `CustomContent` + `ReadRecordedToastsAsync`), trả nguyên văn hoặc null.
3. `BigSellerSaveSuccessHelper.ConfirmSavedThenCloseAsync`: trong vòng poll, kiểm tra toast này **trước** các tín hiệu thành công;
   khớp thì log, gọi callback riêng báo giới hạn, trả false. Không gộp vào `IsPublishFailureText`.
4. `BigSellerProductUpdateRunner.Save.cs`: callback đó bật `_listingLimitDetected` + `MarkListingLimitReached()` như nhánh dòng 115-121
   và return false ngay, không thử lượt tiếp. Hết giờ mà không khớp thì gọi `DumpErrorToastOnceAsync`.
5. Đổi câu log (Runner, Runner.Save, UpdateProductRunner, UpdateProductViewModel) sang dạng "đã đạt giới hạn số sản phẩm (listing limit)", bỏ "trên Shopee".

## 4. Tiêu chí nghiệm thu

| # | Kiểm tra | Mong đợi |
|---|---|---|
| 1 | `dotnet build` các project bị đụng | 0 error, 0 warning mới |
| 2 | `dotnet test workspace/tests/Shopee.Core.Tests` | Xanh, có `ProductLimitToastClassifierTests` |
| 3 | Test: "Số lượng sản phẩm vượt quá giới hạn", "Vượt quá giới hạn sản phẩm được phép thêm", "The number of products exceeds the limit", "The shop has reached its listing limit" | true |
| 4 | Test: "Tên sản phẩm vượt quá giới hạn 120 ký tự", "Product name exceeds the limit", "The Media Center space is insufficient", "Lưu thành công", null, rỗng | false |
| 5 | Thử phá: bỏ danh sách loại trừ hoặc bỏ điều kiện từ chỉ sản phẩm | Test đỏ; khôi phục lại |
| 6 | `dotnet test workspace/tests/Shopee.Workspace.Tests` | Xanh |
| 7 | Đọc code: nhánh toast gọi `MarkListingLimitReached()` và không thử lượt lưu tiếp | Đúng |

Không test được trên site thật trong plan này: chưa có nguyên văn toast.

## 5. Rủi ro

- Câu thật không khớp bộ nhận diện: app vẫn chạy như cũ, nhưng log có nguyên văn để bổ sung.
- Khớp nhầm: shop bị dừng và ghi completed, phần SP còn lại không được thử lại. Giảm bằng điều kiện kép và danh sách loại trừ.
- Toast cũ trong buffer: buffer theo từng tab edit, mỗi SP một tab mới, nên không lẫn sang SP khác.

## 6. Vòng 2 (đổi hướng sau phản biện)

Phản biện bác hai giả định của plan, phiên chính đã đối chiếu với code:
- **Auto Run không cần `completed` để sang shop kế** (`AutoRunViewModel.cs:523-545` đi tiếp bất kể `UpdateDone`). `completed` chỉ khiến lượt sau bỏ qua shop. Nên đường toast, do đoán câu chữ, sẽ ghi `stopped`. Thay mục "Không đổi trạng thái cuối" ở phần 2. Đường hộp thoại giữ `completed`.
- **Buffer theo tab vẫn nhiễu trong cùng tab**, và bộ lọc khớp cả lỗi từng SP. Toast cũ không được thắng tín hiệu lưu thành công.

Sửa thêm:
1. Tracker ghi nguồn giới hạn từ toast; `UpdateProductRunner.ListingLimitFromToast`; ViewModel ghi `stopped` cho nguồn này, log nói rõ lượt sau sẽ thử lại.
2. Thu hẹp bộ nhận diện, so từ loại trừ theo ranh giới từ, bỏ `so luong` + `toi da`.
3. Máy ghi lưu loại toast và đọc thêm `.ant-notification-notice`, `.el-message`, `[role='alert']`. Chỉ xét toast không phải success/info, có `ts` từ lúc bấm Lưu trở đi.
4. Kiểm tra toast đặt SAU tín hiệu thành công, trước bước (4).
5. Cờ dump riêng cho bước lưu, ghi cả dòng "không có toast nào".
6. Test cho từng nhánh và từng từ loại trừ, test hàm lọc theo thời điểm và loại, test chọn trạng thái.

**Không làm vòng này:** lane khác đang lưu bị hủy mất tiến độ (cơ chế có từ trước, cả nút Dừng); dừng theo cả tài khoản.

Tiêu chí thêm: build 0 warning; Core.Tests và Workspace.Tests xanh; các câu ở mục "Câu kiểm" trong prompt giao việc đúng kết quả; đột biến từng nhánh đều làm test đỏ; đọc code thấy thành công được xét trước toast.

## 6b. Vòng 3 (vòng sửa cuối theo quy trình)

Phản biện vòng 2 chứng minh: chỉ một toast khớp nhầm ở một SP có thể chặn shop ở mọi lượt, vì `stopped` khiến lượt sau lại dừng đúng chỗ đó. Bộ nhận diện theo đoán không thể đủ chắc nếu một lần khớp đã dừng shop, nên đổi điều kiện:

- **Chỉ dừng shop khi ít nhất 2 SP khác nhau trong lượt nhận toast khớp.** SP đầu đi đường fail thường (không thử thêm lượt lưu), nên SP lỗi riêng sẽ bị bỏ qua sau 2 lần fail như trước bản vá.
- `[role=alert]` và banner chỉ để lấy mẫu log, không được kích hoạt dừng shop.
- Hộp thoại được xét trước toast.
- Sửa bộ nhận diện theo các câu phản biện đã chạy; suất log không bị dòng "không có toast" chiếm mất.

**Không làm:** toast mới kèm tab đóng, URL đổi hoặc modal thành công vẫn tính là thành công (để user quyết); lane khác bị hủy mất tiến độ; dừng theo tài khoản. Vẫn chưa kiểm tra trên site thật.

## 7. Báo cáo thực thi

Đã hết 2 vòng sửa theo quy trình; dừng lại, chờ user quyết. Chưa commit, chưa thử trên site thật.

- **Kiểm chứng (cây chưa commit, nghiệm thu tự chạy lại):** build 0 warning; Core.Tests 517/517; Workspace.Tests 88/88. Phản biện ghi nhận `Parallel_ManyItems_DecidesExactlyOnce` đỏ 1 lần trong 70 lần chạy cả bộ.
- **Phản biện vòng 3, chưa sửa:**
  - Nặng: 2 SP lỗi riêng ở đầu danh sách, nếu toast của chúng khớp nhầm, vẫn chặn shop ở mọi lượt; SP sau không được làm.
  - Bộ đếm không xét các lần lưu thành công xen giữa.
  - Toast đến nhịp trước modal thành công hoặc hộp thoại thì toast thắng.
  - Bộ nhận diện vẫn nhầm và hụt nhiều với bộ câu đối kháng; lỗi tần suất đánh vào mọi lane cùng lúc.
  - Test không canh phần nối dây và máy ghi JS.
- **Nguyên nhân gốc:** chưa có nguyên văn toast, nên quyết định dừng shop đang dựa trên đoán câu chữ.
- **Hướng đề nghị:** bước Lưu ghi log mẫu toast trước; có nguyên văn thì khớp đúng câu đó, không đoán.
