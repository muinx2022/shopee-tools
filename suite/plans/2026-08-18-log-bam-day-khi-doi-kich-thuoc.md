# Plan: Log bám đáy cả khi đổi thu phóng / kéo giãn, và khoá chốt "đang bôi đen"

- **Ngày:** 2026-08-18
- **Trạng thái:** hoàn thành
- **Người lập:** phiên chính · **Người thực thi:** `opus-dev`

## 1. Bối cảnh & mục tiêu

Đây là phần **CÒN THIẾU** mà lượt phản biện 18/08/2026 nêu ra ở
`plans/2026-08-18-log-bam-day-khi-quay-lai-man-tai-khoan.md` (mục 5.3 và 5.1), người dùng chốt làm nốt.

**Hở 1 — log trôi khỏi đáy khi đổi kích thước.** Style `logBox` đặt `TextWrapping="Wrap"`
(`AccountsView.xaml:28`). Đổi bề rộng ⇒ text cuộn lại dòng ⇒ `ExtentHeight` đổi, nhưng `VerticalOffset` giữ
nguyên pixel ⇒ khung nhìn trôi khỏi đáy. Hiện KHÔNG đường nào chạy lại: `OnVmPropertyChanged` chỉ bắn khi
`LogText` đổi, `OnLoaded` chỉ bắn khi view vào cây.

Hai đường dẫn tới nó, **đã kiểm chứng bằng code chứ không đoán**:
- **Thu phóng Ctrl +/−**: `suite/Shopee.Suite/Services/UiZoom.cs:14,29` dùng MỘT `ScaleTransform` gán vào
  `FrameworkElement.LayoutTransform`. `LayoutTransform` ⇒ **layout chạy lại** ⇒ bề rộng khả dụng của `LogBox`
  trong hệ toạ độ của chính nó ĐỔI ⇒ `SizeChanged` bắn. (Nếu là `RenderTransform` thì đã không re-wrap, cũng
  không cần vá — nên chi tiết này là điều kiện sống còn của bản vá.)
- **Kéo giãn cửa sổ / kéo chia panel**: đổi bề rộng trực tiếp.

**Hở 2 — chốt "đang bôi đen thì để yên" chưa có test.** Chốt này (`AccountsView.xaml.cs`, trong
`CuonLogXuongCuoi`) bảo vệ đúng cái `AccountsView.xaml:851-852` quảng cáo: "Ctrl+A chọn HẾT + Ctrl+C". Không có
test thì lần sau ai đó gỡ nó đi cũng không ai biết.

Điều kiện thuận lợi: `UiDispatch.Post` chạy **thẳng trên luồng gọi** khi `Application.Current` là null
(`Services/UiDispatch.cs:22-27`, xmldoc nói rõ là để test headless) ⇒ trong test, gọi xong là hiệu ứng có ngay,
không cần bơm Dispatcher. Và `x:Name="LogBox"` sinh field `internal`, mà project mở `InternalsVisibleTo` cho
Tests ⇒ test chạm thẳng được `view.LogBox`.

## 2. Phạm vi

**Làm:**
- **G1** `LogBox.SizeChanged` → bám đáy lại.
- **G2** Test khoá chốt "đang bôi đen thì để yên" + test khoá G1.

**Không làm:**
- Không đụng XAML (style `logBox`, layout).
- Không đổi cơ chế thu phóng.
- Không thêm logic "chỉ bám lại nếu TRƯỚC ĐÓ đang ở đáy". Người dùng nói rõ *"luôn bám vào nội dung cuối cùng"*,
  và đường `LogText` sẵn có cũng đã bám vô điều kiện mỗi ~250ms — thêm luật riêng cho một đường là để hai đường
  cư xử khác nhau, khó hiểu hơn chứ không tử tế hơn. Chốt "đang bôi đen" đã che ca người dùng đang đọc/chép.
- Không đụng gì trong `.claude/worktrees/` — đó là hai phiên khác đang chạy song song.

## 3. Các bước thực hiện

### G1 — `orders/XuLyDonShopee.App/Views/AccountsView.xaml.cs`

1. Trong ctor, đăng ký `LogBox.SizeChanged += OnLogBoxSizeChanged;`.
   - Đăng ký thẳng trong ctor được, **không** cần theo Loaded/Unloaded như đăng ký vào VM: `LogBox` là con của
     chính view này, cùng vòng đời — view chết thì cả hai cùng chết, không có bên nào sống dai giữ bên kia.
     Ghi rõ lý do đó vào comment kẻo lần sau có người "sửa cho đồng bộ" rồi làm hỏng.
2. Handler: bỏ qua khi cả hai chiều đều không đổi (`!e.WidthChanged && !e.HeightChanged`), còn lại gọi
   `CuonLogXuongCuoi()`.
3. Xác nhận KHÔNG có vòng lặp: `ScrollToEnd` đổi `VerticalOffset`, không đổi kích thước ⇒ không tự bắn lại
   `SizeChanged`. Ghi nhận xét này vào comment.

### G2 — `orders/XuLyDonShopee.Tests/` (thêm vào `AccountsViewVongDoiTests.cs` hoặc file mới cùng khuôn)

4. Dựng view như `AccountsViewVongDoiTests` đang làm (`new AccountsView { DataContext = vm }` + bắn
   `FrameworkElement.LoadedEvent`). Đặt nội dung log bằng cách gán thẳng `view.LogBox.Text` (binding là OneWay,
   gán cục bộ đè lên là đủ cho test).
5. **Test A — có bôi đen thì KHÔNG bị cướp:** `LogBox.Select(0, 10)` → kích hoạt đường cuộn → khẳng định
   `SelectionStart == 0 && SelectionLength == 10` (và `CaretIndex` không nhảy về cuối).
6. **Test B — không bôi đen thì CÓ cuộn thật:** `SelectionLength == 0` → kích hoạt → khẳng định
   `CaretIndex == Text.Length`. Test này là cái chống "test rỗng": không có nó thì Test A vẫn xanh kể cả khi
   toàn bộ đường cuộn bị vô hiệu.
7. **Test C — G1:** cho view qua một lượt `Measure`/`Arrange` ở bề rộng X, đặt `CaretIndex = 0`, rồi
   `Measure`/`Arrange` ở bề rộng KHÁC → khẳng định `CaretIndex == Text.Length` (chứng tỏ `SizeChanged` đã kéo
   bám đáy lại).
   - **Nếu không kích được `SizeChanged` một cách tất định trong test** (cây tách rời có thể không chạy đủ lượt
     layout): **NÓI THẲNG trong báo cáo**, đừng bịa một test đo chuyện khác rồi gọi là xong. Khi đó ghi nhận G1
     chỉ được soát bằng mắt, và đề xuất cách kiểm khác.
8. **THỬ PHÁ bắt buộc** (luật 4, `D:\Projects\CLAUDE.md`): với MỖI test mới — gỡ chốt `SelectionLength > 0` →
   Test A phải ĐỎ; gỡ hook `SizeChanged` → Test C phải ĐỎ; vô hiệu cả thân `CuonLogXuongCuoi` → Test B phải ĐỎ.
   Khôi phục và xác nhận xanh lại.

## 4. Tiêu chí nghiệm thu

- [ ] **U1** `dotnet build ShopeeSuite.sln` → 0 error, **0 warning**. Dán nguyên dòng tổng kết.
- [ ] **U2** `dotnet test orders/XuLyDonShopee.Tests/XuLyDonShopee.Tests.csproj` → Failed: 0. Ghi tổng số
      trước → sau (trước là **1820**; sau phải LỚN HƠN).
- [ ] **U3** Có bôi đen → cuộn KHÔNG cướp vùng chọn (Test A xanh).
- [ ] **U4** Không bôi đen → `CaretIndex` về cuối (Test B xanh) — chứng minh đường cuộn thật sự chạy.
- [ ] **U5** Đổi kích thước ô log → bám đáy lại (Test C xanh), HOẶC báo cáo nói rõ vì sao không kích được
      `SizeChanged` tất định.
- [ ] **U6** Bảng THỬ PHÁ: mỗi test mới ↔ phá gì ↔ test nào đỏ ↔ đã khôi phục.
- [ ] **U7** `git status --porcelain` sau khi xong KHÔNG có file nào ngoài phần việc này + phần việc đã chốt
      trước đó (8 M + 4 test mới + 3 plan). Không đụng `.claude/worktrees/`.

## 5. Rủi ro & lưu ý

- **Vòng lặp layout.** Nếu handler `SizeChanged` làm gì đó đổi kích thước thì sẽ tự bắn lại vô tận. `ScrollToEnd`
  chỉ đổi offset nên an toàn — nhưng đừng thêm việc gì khác vào handler đó.
- **Đừng đăng ký `SizeChanged` theo Loaded/Unloaded.** Khác hẳn ca đăng ký vào VM: `LogBox` cùng vòng đời với
  view, không có chuyện giữ nhau sống dai.
- **Test C có thể mong manh.** Layout trên cây tách rời không phải lúc nào cũng bắn `SizeChanged` như trong cửa
  sổ thật. Thà báo "không kiểm được" còn hơn viết một test đo chuyện khác — đúng bài học "proof đo rỗng"
  (`D:\Projects\CLAUDE.md`, đợt 2026-08-13).
- **Máy đang có hai phiên Claude khác chạy** trong `.claude/worktrees/` (harness `RunAllShopsAsync`; chặn tab mồ
  côi ở `openShopDetail`). Thấy file `M` lạ ngoài danh sách trên thì DỪNG và báo, đừng sửa.
- **Không commit.**

---

## Báo cáo thực thi

### Chặng 2 — `opus-dev`

G1 + G2 làm đủ. Build 0/0; test 1820 → 1823. Kích được `SizeChanged` **tất định** trên cây tách rời nhờ
`Measure` → `Arrange` → **`UpdateLayout()`** (thiếu mảnh cuối thì sự kiện không bắn — `Arrange` chỉ xếp hàng).

### Chặng 3 — `nghiem-thu`

6/7 ĐẠT, U6 đạt một phần (plan chưa điền). Tự chạy lại build + test, tự làm lại **cả 3** lượt thử phá, khớp lời
khai. Xác nhận bằng chứng độc lập rằng Test C đỏ ở dòng 126 chứ không phải 125 ⇒ khẳng định "bề rộng đã đổi" đã
PASS ⇒ test không đo một lượt layout chưa xảy ra.

### Chặng 4 — `phan-bien`

Dựng **probe WPF thật** để đo chứ không lập luận suông. Xác nhận SẠCH đúng chỗ nghi nhất: thanh cuộn `Auto` nằm
BÊN TRONG template nên hiện/ẩn không đổi `ActualWidth`, cột chứa log là `2*` ⇒ **không có vòng dao động**; hook
không gỡ cũng **không rò rỉ** (view đã vứt không nhận thêm sự kiện). Nhưng bắt được **hai lỗi NẶNG**, cả hai
đúng loại "proof đo rỗng".

### Chặng 5 — phiên chính chốt việc

| # | Lỗi | Sửa |
|---|---|---|
| 1 | **Cả 3 test đo `CaretIndex`** — đại lượng TÁCH RỜI khỏi triệu chứng. Probe: chỉ đặt `CaretIndex` về cuối thì offset đứng im ở 474.8 trong khi đáy là 1247.6. Xoá `ScrollToEnd()` (dòng DUY NHẤT vá lỗi) thì test vẫn xanh còn lỗi quay lại nguyên vẹn | Viết lại cả bộ, khẳng định thẳng `VerticalOffset` so với `ExtentHeight - ViewportHeight`, kèm chốt chống rỗng `PhaiCoGiDeCuon` |
| 2 | **Chốt "đang bôi đen" vô dụng đúng ở đường Test A đang đo.** Binding `OneWay` đăng ký từ lúc gắn `DataContext` (sớm hơn handler của view) nên nó ghi `LogBox.Text` trước, mà gán `Text` làm WPF gọi `Select(0,0)` ⇒ vùng chọn chết trước khi chốt kịp nhìn. Test A chỉ xanh vì `ClearBinding` đã xoá mất cấu hình chạy thật | Bỏ `ClearBinding`; chĩa test vùng-bôi-đen vào đường `SizeChanged` (đường chốt THẬT SỰ bảo vệ); thêm test đặc tả hạn chế; **sửa comment sai trong code sản phẩm** |
| 3 | **Bám đáy vô điều kiện khi resize phá thao tác đọc log cũ.** Lý lẽ trong plan ("đằng nào cũng giật mỗi 250ms") chỉ đúng khi phiên ĐANG CHẠY; lúc phiên đã dừng — ca dùng chính của panel — không có nhịp nào. Lăn lên đọc rồi bấm Ctrl+= là mất chỗ | Cờ `_dangOSatDay` (cập nhật từ `ScrollChanged`, bỏ qua lượt do layout); đường `SizeChanged` chỉ bám lại khi trước đó đang ở sát đáy. Đường `LogText` giữ nguyên vô điều kiện — đúng yêu cầu gốc |
| 4 | Nhánh `!e.WidthChanged && !e.HeightChanged` là **code chết** (WPF chỉ bắn khi kích thước thật sự đổi) | Bỏ, kèm comment nói vì sao |

**Nhận lỗi:** đại lượng sai ở mục 1 là do **plan của tôi chỉ định sai** (bước 5–7 viết rõ "khẳng định
`CaretIndex == Text.Length`"). Người thực thi làm đúng plan; sai từ chỗ tôi.

### Số kiểm chứng cuối

- `dotnet build ShopeeSuite.sln` → **0 Warning, 0 Error**.
- `dotnet test` → **Failed: 0, Passed: 1825**.
- **THỬ PHÁ** (5 test mới, mỗi lượt phá → chạy → khôi phục, xác minh file khớp backup):

| Phá | Sửa ngược | Kết quả |
|---|---|---|
| S1 | **xoá `LogBox.ScrollToEnd()`** | **ĐỎ 2/5** ← bộ test CŨ không bắt được |
| S2 | bỏ hook `SizeChanged` | ĐỎ 2/5 |
| S3 | bỏ guard `_dangOSatDay` | ĐỎ 1/5 |
| S4 | bỏ chốt `SelectionLength` | ĐỎ 1/5 |

### CÒN THIẾU

1. **Không chép được log lúc phiên đang chạy** — vùng bôi đen bị nhịp log ~250ms xoá, chốt hiện tại không cứu
   nổi (xem mục 2). Đã chốt lại bằng test đặc tả `NhipLogMoi_LamMatVungBoiDen_HanCheDaBiet`. Vá thật thì phải
   neo vùng chọn theo NỘI DUNG rồi tìm lại sau khi `Text` bị ghi — chuỗi log bị cắt bớt đầu ở mốc 200 dòng nên
   neo theo offset là trượt. Việc riêng.
2. **Chưa chạy app thật.** Không có công cụ lái chuột WPF; hành vi Ctrl +/− và kéo giãn cửa sổ cần mắt người
   dùng.
