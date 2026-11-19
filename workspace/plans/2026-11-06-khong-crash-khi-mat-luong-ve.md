# Plan: Không tràn stack khi luồng vẽ cửa sổ chết

- **Ngày:** 2026-11-06
- **Trạng thái:** code xong, chưa commit
- **Người lập:** phiên chính · **Người thực thi:** agent `dev`

## 1. Bối cảnh

`ShopeeUpdate.exe` chết ba lần, cùng mã `0xc00000fd` (tràn stack):

| Lúc | Module |
|---|---|
| 2026-11-05 23:13 | `coreclr.dll` |
| 2026-11-05 23:24 | `ntdll.dll` |
| 2026-11-06 00:24 | `coreclr.dll` |

Ngay trước mỗi lần, `%TEMP%\shopeeworkspace-crash.log` ghi hàng trăm `COMException` `UCEERR_RENDERTHREADFAILURE` (`0x88980406`) từ `DUCE.Channel.SyncFlush`. Windows ghi `dwm.exe` chết trong `udwm.dll` ở hai lần đầu; lần 00:24 thì `brave.exe` chết khoảng 1 giây trước. App đang update shop `pb7zqxypew`.

`HandleUiCallbackException` trong `workspace/src/Shopee.Workspace/App.xaml.cs` log rồi gọi `Dialogs.Notify` cho mọi lỗi không thuộc teardown. Luồng vẽ đã chết nên `ShowDialog` ném `Win32Exception`. `UiThread.Run` bắt lỗi đó và gọi lại `OnError` — chính là handler này. Vòng «vẽ hỏng → hộp thoại → hộp thoại hỏng → hộp thoại nữa» chạy hàng trăm lần trong khoảng một giây rồi tràn stack.

`suite/suite/Shopee.Suite/App.xaml.cs` chép cùng handler. Lần này chưa thấy Suite chết, nhưng cùng một vòng sẽ giết Suite nếu `dwm` sập lúc Suite đang mở.

Không cứu được cửa sổ sau khi luồng vẽ chết. Việc cần làm là đóng app một lần, không mở thêm hộp thoại.

## 2. LÀM

1. Thêm hàm thuần `internal static` (workspace: class mới cạnh `UiThread`, ví dụ `UiErrorPolicy` trong `workspace/src/Shopee.Workspace/Services/UiErrorPolicy.cs`). Hàm nhận exception và cờ «đang ở trong handler», trả một trong ba: `Ignore`, `Notify`, `Shutdown`.
   - HRESULT `0x88980406` (kể cả bọc trong `InnerException`) → `Shutdown`.
   - Đang ở trong handler → `Ignore` (chỉ log, không `Notify`).
   - Teardown lành tính như `IsBenignTeardown` hiện tại → `Ignore`.
   - Còn lại → `Notify`.
2. `HandleUiCallbackException` dùng hàm đó. `Shutdown`: log một dòng, gọi `Shutdown()` đúng một lần, không `Dialogs.Notify`. `Ignore`: log, không popup. `Notify`: giữ popup hiện tại. Cờ «đang xử lý» phải bật trước khi `Notify`, tắt trong `finally`, để lỗi của chính hộp thoại không mở hộp thoại thứ hai.
3. `OnDispatcherUnhandledException` vẫn đặt `e.Handled = true` trước khi quyết định, để WPF không tự tắt bằng hộp thoại mặc định trong lúc ta đóng.
4. Chép cùng luật sang `suite/suite/Shopee.Suite/App.xaml.cs`. Được phép một class `UiErrorPolicy` riêng trong suite (hai project không dùng chung file). Hành vi phải giống từng nhánh.
5. Test thuần, không mở cửa sổ, không đụng `%AppData%`.
6. Publish lại đúng thư mục shortcut đang trỏ, tuần tự, không song song hai project workspace:
   - `workspace/src/Shopee.Update/Shopee.Update.csproj` → `workspace/publish/ShopeeUpdate`
   - `workspace/src/Shopee.Scrape/Shopee.Scrape.csproj` → `workspace/publish/ShopeeScrape`
   - `workspace/src/Shopee.Workspace/Shopee.Workspace.csproj` → `workspace/publish/ShopeeWorkspace`
   - `suite/suite/Shopee.Suite/Shopee.Suite.csproj` → `suite/publish/ShopeeSuite`
   Lệnh: `dotnet publish <csproj> -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o <dir>`. Không publish Backend, không build Hub, không deploy.

## 3. KHÔNG LÀM

- Không bật `RenderMode.SoftwareOnly` cho cả app. Lần vẽ chết đã xảy ra thì đổi mode không cứu cửa sổ, và đổi cách vẽ mọi lúc là việc khác.
- Không thêm hộp thoại «sắp đóng app». Mở cửa sổ lúc `dwm` chết chính là vòng vừa giết process.
- Không đổi luồng update sản phẩm, scrape, video hub, cột tiến độ.
- Không sửa các file đang `M` của việc cột tiến độ, trừ khi một file đó chính là `App.xaml.cs` (hiện `App.xaml.cs` không nằm trong diff cột tiến độ).
- Không commit, không push.

## 4. Hành vi

| Tình huống | Việc app làm |
|---|---|
| `0x88980406` lần đầu | Ghi một dòng vào crash log, đóng app một lần, không popup |
| `0x88980406` lặp lại trong lúc đang đóng | Bỏ qua, không popup, không `Shutdown` lần hai |
| Popup lỗi tự ném (Win32 khi tạo cửa sổ) | Log, không popup tiếp |
| `ObjectDisposedException` / hủy token | Như cũ: log, không popup |
| Lỗi UI thường | Như cũ: log và một popup |

## 5. Tiêu chí

1. Test hàm chọn nhánh: HRESULT `0x88980406` trần và bọc `InnerException` đều `Shutdown`; đang trong handler thì `Ignore` kể cả lỗi thường; teardown lành tính thì `Ignore`; lỗi thường khi không trong handler thì `Notify`.
2. `dotnet test workspace/tests/Shopee.Workspace.Tests/Shopee.Workspace.Tests.csproj -c Release --filter "FullyQualifiedName~UiErrorPolicy"` — xanh.
3. `dotnet build workspace/src/Shopee.Workspace/Shopee.Workspace.csproj -c Release --nologo` và `dotnet build suite/suite/Shopee.Suite/Shopee.Suite.csproj -c Release --nologo` — 0 error. Không thêm warning mới từ file vừa sửa.
4. Bốn thư mục publish ở mục 2 có exe ghi đè thành công sau lệnh publish.
