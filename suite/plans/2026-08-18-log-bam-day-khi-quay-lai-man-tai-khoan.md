# Plan: Cửa sổ nhật ký bám đáy khi quay lại màn Tài khoản

- **Ngày:** 2026-08-18
- **Trạng thái:** hoàn thành
- **Người lập & thực thi:** phiên chính · **Kiểm:** `nghiem-thu` + `phan-bien`

## 1. Bối cảnh & mục tiêu

Yêu cầu người dùng, nguyên văn: *"khi vào phần shopee, click vào tài khoản, ra thông tin chi tiết về tài khoản
và log của tài khoản. hiện tại, khi bấm sang mục khác, vd Thống kê, sau đó quay lại phần tài khoản, log chuyển
về đầu, sửa lại, cửa sổ log luôn bám vào nội dung cuối cùng để hiển thị, không phải kéo lại xuống để nhìn nữa"*

**Gốc rễ.** `MainView.xaml:41` đặt màn con bằng `ContentControl Content="{Binding CurrentViewModel}"` + ba
`DataTemplate` theo KIỂU VM (`MainView.xaml:23-31`). Đổi nav là đổi template ⇒ `ContentPresenter` **dựng lại**
cây ⇒ một `AccountsView` MỚI với `LogBox` chưa cuộn đi đâu. Mà `MainViewModel` giữ **một** `AccountsViewModel`
suốt đời app nên `LogText` không đổi giá trị lúc đó ⇒ `OnVmPropertyChanged` không bắn ⇒ log đứng ở dòng đầu.

## 2. Phạm vi

**Làm:** cuộn xuống đáy nhật ký mỗi khi view vào cây; sửa luôn hai lỗi mà lượt phản biện bắt được ở đúng vùng
code này.

**Không làm:** không đụng XAML, không đụng VM, không đổi cơ chế điều hướng.

## 3. Đã làm

Chỉ một file nguồn: `orders/XuLyDonShopee.App/Views/AccountsView.xaml.cs`.

1. **Bám đáy khi vào cây** — móc `Loaded`, tách thân cuộn thành `CuonLogXuongCuoi()` dùng chung với đường cũ
   (`OnVmPropertyChanged` khi `LogText` đổi).
2. **RÒ RỈ view (phản biện bắt).** VM sống suốt đời app, view bị vứt mà KHÔNG đổi `DataContext` ⇒ gỡ đăng ký chỉ
   nằm trong `DataContextChanged` thì không bao giờ chạy. Mỗi lượt điều hướng để lại một view chết bị VM giữ
   sống, và cứ ~250ms cả đám view chết cùng đi cuộn một `LogBox` đã tháo. **Đúng lỗi `OrdersView` đã vá ở commit
   `2957563` nhưng bỏ sót file này.** Chuyển sang khuôn `Loaded`/`Unloaded` của `OrdersView`.
3. **MẤT VÙNG BÔI ĐEN (phản biện bắt).** Setter `TextBox.CaretIndex` thực chất là `Select(value, 0)` — nó xoá
   vùng chọn. Log dội ~250ms/nhịp nên đúng lúc người dùng bôi đen dòng lỗi để Ctrl+C thì vệt chọn biến mất, trong
   khi chính `AccountsView.xaml:851` quảng cáo "Ctrl+A chọn HẾT + Ctrl+C". Thêm chốt: đang bôi đen thì để yên.
4. **Sửa một lý lẽ SAI tôi tự viết ra.** xmldoc cũ bảo `UiDispatch.Post` "chắc chắn chạy SAU lượt layout đang
   dở". Ngược: `Post` là `BeginInvoke` ở `DispatcherPriority.Normal` (9), **cao hơn** `Render` (7) và `Loaded`
   (6). Code vẫn chạy đúng nhưng vì lý do khác, và comment sai kiểu này là thứ maintainer sau sẽ tin.
5. **Guard `IsLoaded` → cờ `_trongCay` tự giữ.** `IsLoaded` chỉ đúng khi cây gắn vào `PresentationSource` thật,
   nên test không dựng cửa sổ thì KHÔNG BAO GIỜ kiểm được nhánh có guard — hai test đầu tiên đỏ vì đúng chuyện
   đó. Cờ do chính `Loaded`/`Unloaded` lật: tương đương lúc chạy thật, mà kiểm chứng được.

## 4. Tiêu chí nghiệm thu — kết quả

- [x] `dotnet build ShopeeSuite.sln` → **0 Warning, 0 Error**.
- [x] `dotnet test orders/XuLyDonShopee.Tests` → **Failed: 0, Passed: 1820**.
- [x] `nghiem-thu` chấm **5/5 ĐẠT**, tự chạy lại build + test, tự xác minh chẩn đoán bằng code.
- [x] `AccountsViewVongDoiTests` — 5 `[StaFact]` khoá tính đối xứng đăng ký ↔ gỡ đăng ký.
- [x] **THỬ PHÁ** (luật 4):

| Phá | Sửa ngược | Kết quả |
|---|---|---|
| P | bỏ hook `Unloaded` (dựng lại đúng lỗi rò rỉ cũ) | **ĐỎ 3/5** |
| Q | bỏ guard `_trongCay` | **ĐỎ 1/5** |
| R | bỏ `ThoiNgheVm()` đầu `OnDataContextChanged` | **ĐỎ 1/5** |

Khôi phục xong: 5/5 xanh, cả bộ 1820 xanh.

## 4b. ĐÍNH CHÍNH (18/08/2026, sau lượt phản biện của plan kế tiếp)

Mục 3.3 ở trên nói chốt "đang bôi đen thì để yên" vá được chuyện *"đúng lúc người dùng bôi đen dòng lỗi để
Ctrl+C thì vệt chọn biến mất"*. **SAI.** Probe WPF đo được: binding `OneWay` của `LogBox.Text` đăng ký từ lúc
gắn `DataContext`, tức SỚM HƠN handler của view, nên khi `LogText` đổi nó ghi `Text` trước — mà gán `Text` làm
WPF tự gọi `Select(0,0)` ⇒ vùng chọn đã chết trước khi chốt kịp nhìn. Chốt đó **chỉ** cứu được đường `Loaded`
và đường `SizeChanged` (hai đường không ghi lại `Text`).

Comment sai trong code đã sửa; hạn chế đã được chốt bằng test đặc tả
`AccountsViewCuonLogTests.NhipLogMoi_LamMatVungBoiDen_HanCheDaBiet`. Chi tiết ở
`plans/2026-08-18-log-bam-day-khi-doi-kich-thuoc.md`, chặng 5 mục 2.

## 5. CÒN THIẾU — nói thẳng

1. **Chốt "đang bôi đen thì để yên" (mục 3.3) KHÔNG có test.** `CuonLogXuongCuoi` là private và đi qua
   `UiDispatch.Post`; kiểm nó cần dựng cửa sổ thật + bơm chuột. Chỉ soát bằng mắt.
2. **Chưa chạy app thật.** Không có công cụ lái chuột WPF trong phiên này, nên hành vi "bấm sang Thống kê rồi
   quay lại" chưa có ai bấm thử — đó là việc của người dùng.
3. **Còn một ca hở** phản biện nêu, cố ý không sửa: đổi thu phóng Ctrl +/− hoặc kéo giãn cửa sổ làm text cuộn
   lại dòng (`TextWrapping="Wrap"`), `ExtentHeight` đổi mà offset giữ nguyên ⇒ trôi khỏi đáy. Không đường nào
   chạy lại. Muốn kín phải bắt `SizeChanged` của `LogBox` + nhớ trạng thái "đang ở đáy". Ngoài phạm vi yêu cầu
   (người dùng nói về chuyện đổi màn), để việc riêng.
4. **`catch { }` trắng vẫn còn**, trái quy ước "nuốt lỗi phải để lại dấu vết" — nhưng CỐ Ý: hàm này chạy vì nhật
   ký vừa đổi, ghi thêm một dòng nhật ký là tự gọi lại chính mình. Đã ghi rõ lý do vào comment.
