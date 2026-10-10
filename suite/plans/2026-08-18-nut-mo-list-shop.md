# Plan: Nút "Mở list shop" — chạy đúng luồng tài khoản nhưng DỪNG ở trang chọn shop

- **Ngày:** 2026-08-18
- **Trạng thái:** đang làm
- **Người lập:** phiên chính · **Người thực thi:** `opus-dev` · **Kiểm:** `nghiem-thu` + `phan-bien`

## 1. Bối cảnh & mục tiêu

Yêu cầu người dùng, nguyên văn: *"thêm cho tôi 1 nút Mở list shop ở đây, click vào thì mở đúng như flow chạy
acc, khác cái là đến list shop thì dừng"* — kèm ảnh chụp màn hình khoanh đỏ vùng **bên phải hàng đầu của tab con
"Shops"** (cạnh dòng chữ "Số toàn hệ thống (Hub)").

Mục đích: người dùng tự vào Seller Centre xem vài thứ bằng chính hồ sơ Brave + cookie mà app đang dùng, đi ĐÚNG
con đường app vẫn đi (SSO qua "Kênh Người bán") chứ không gõ thẳng URL — mở thẳng `/portal/shop` dễ dính
sticky-shop server-side (`ShopeeLoginService.cs:114-117`).

### Hiện trạng code (đã đọc, không phỏng đoán)

**Luồng chạy thật của một tài khoản** — `AccountSession.RunBridgeContinuousAsync`
(`orders/XuLyDonShopee.App/Services/AccountSession.cs:447`):

1. Xin khoá chạy xuyên máy → dựng `OrdersBridgeSession` (hồ sơ `profiles/<id>-brave`) với ~15 callback.
2. `RunAllShopsAsync` (`orders/XuLyDonShopee.Core/Services/OrdersBridgeSession.cs:819`):
   - `LoginAndReachPickerAsync` (`:690`) — **mở trình duyệt SẠCH + extension TRƯỚC** tại
     `https://subaccount.shopee.com/account` (`:587`); cookie hồ sơ còn hạn ⇒ vào thẳng picker, **bỏ qua
     Playwright**. Không vào được ⇒ đóng bản sạch → đăng nhập Playwright → mở lại bản sạch.
     Bên trong: chờ `ready` (45s) → `gotoSellerCentre` → chờ `atSellerCentre` (120s) (`:642-648`).
   - Đọc shop: `ArmShopList` → `readShopList` → `pageData kind=shopList` (30s) → `ParseShopListJson` →
     `_onShopListRead?.Invoke(shops)` (`:878-882`).
   - **`:884` `L("Đọc được {N} shop — bắt đầu lặp qua từng shop.")` → vòng `for` (`:916`), lệnh đầu tiên tác động
     lên shop là `openShopDetail` (`:951`).** ⇒ **Ranh giới cần dừng nằm ĐÚNG giữa `:882` và `:884`.**
3. Hết vòng: `finally` gọi `DongTrinhDuyetSach()` + `Dispose()` (`AccountSession.cs:558-566`) rồi nghỉ.

**Khối "đọc danh sách shop" hiện bị chép BA lần**: `:793-797` (`MoLaiTrinhDuyetVePickerAsync`), `:878-882`
(`RunAllShopsAsync`), `:1246-1249` (`RunSliceCoreAsync` — bản KHÔNG `Arm`, ngữ nghĩa khác).

**Extension hoàn toàn THỤ ĐỘNG** (báo cáo khảo sát, có file:line):

- 13 lệnh, dispatch `switch (cmd.action)` ở `background.js:48-62`, **không có nhánh `default`** ⇒ action lạ bị
  nuốt im lặng (an toàn cho lệnh mới khi bản extension cũ còn chạy).
- `readShopList` chỉ ĐỌC (`flow-shop.js:188`): không click, không mở/đóng tab.
- Cuối `gotoSellerCentre` tab subaccount bị đóng (`flow-shop.js:160`) ⇒ sau `readShopList` **chỉ còn ĐÚNG một
  tab** đứng ở `https://banhang.shopee.vn/portal/shop`.
- C# đóng WS ⇒ extension **không** đóng tab, **không** reload — chỉ vào vòng thử nối lại 1200ms/lần
  (`shared/ws-bridge.js:149`).
- `gotoSellerCentre` gắn `chrome.debugger` vào tab picker và **KHÔNG detach** (`flow-shop.js:140`) ⇒ cửa sổ để
  lại sẽ mang banner "đang gỡ lỗi trình duyệt".

**Ràng buộc cứng:**

- Một hồ sơ Brave chỉ chịu **MỘT** tiến trình Chromium. Cửa sổ mồ côi giữ khoá hồ sơ ⇒ phiên sau chết ngay ở
  bước mở (`OrdersBridgeSession.cs:711-720`).
- Cổng WS là **hằng 47821** (`OrdersBridgeChannel.cs:145` ↔ `core.js:8` ↔ `content.js:17`). Chỉ một kênh sống
  một lúc ⇒ xong việc phải `Dispose()` kênh, nếu không phiên production sau không mở nổi cổng.
- Trình duyệt được phóng trong **Job Object `KILL_ON_JOB_CLOSE`** (`BrowserProcessStarter.cs:8`) ⇒ tắt app là
  cửa sổ để lại chết theo. Không có nguy cơ cửa sổ mồ côi sống qua phiên app.
- `Sessions.IsRunning(id)` TRUE cả trong kỳ nghỉ 90' giữa hai vòng (`AccountSessionManager.cs:157,171`).

**Lối vào UI hiện có:** dải nút ▶/■ trong màn đã bỏ (`AccountsView.xaml:330`); ribbon chỉ bind
`SelectAll/RunSelected/StopSelected/StopAll` (`suite/Shopee.Suite/ViewModels/ShellViewModel.cs:234-244`).
⇒ `RunCommand`, `StopCommand`, `ChayThuBridgeCommand`, `CanStop`, `CanRun` **hiện KHÔNG được XAML nào bind**.
Nút mới sẽ là lối vào lát cắt cầu nối DUY NHẤT trên UI, nên mấy lỗ hổng dưới đây từ "vô hại" thành "gặp ngay".

**Lát cắt sẵn có để dùng lại:** `ChayThuBridge` (`AccountsViewModel.Phien.cs:208-310`) đã có đủ khung: chốt cửa,
`_bridgeCts`/`_bridgeSession`/`_bridgeRunning`, `TaoPhienCauNoi` (`:316`), `TryKillPoc` (`:324`), và **cố ý để
cửa sổ mở** sau khi xong (`finally { _pocProcess = session.Process; }` `:296-299`) để nút Dừng đóng sau.

### Quyết định đã chốt

Người dùng được hỏi hai câu (đang chạy thì làm gì · bao giờ đóng cửa sổ) nhưng không trả lời ⇒ phiên chính chốt
theo phương án khuyến nghị, và **phải nói rõ trong báo cáo** để người dùng đổi nếu muốn:

1. **Đang có phiên chạy (kể cả lúc nghỉ) ⇒ TỪ CHỐI + nhắc dừng.** Không tự ý cắt vòng đang chạy.
2. **Tới trang chọn shop thì để nguyên cửa sổ**, đóng kênh WS (nhả cổng 47821), giữ `Process` để còn đóng được.
   Cửa sổ đóng khi: "Dừng tất cả" · "Dừng đã chọn" (nếu tài khoản đó đang tick) · bấm lại chính nút này · mở
   phiên chạy cho tài khoản đó · tắt app (Job Object).
3. Nút **luôn bấm được** khi đang xem một tài khoản đã lưu (`CanRun`); lý do từ chối nói bằng nhật ký +
   `BusyStatus`, KHÔNG làm mờ nút câm.
4. Danh sách shop đọc được **có** đổ vào lưới tab "Shops" (giống hệt vòng chạy thật) — đi qua callback
   `onShopListRead`.

## 2. Phạm vi

**Làm:**

- Nút "Mở list shop" ở hàng đầu tab "Shops", canh phải.
- Đường chạy mới trong `OrdersBridgeSession`: đăng nhập/SSO → đọc danh sách shop → DỪNG, không đóng trình duyệt.
- Rút MỘT hàm đọc-danh-sách-shop dùng chung (thay 2 bản chép kiểu `Arm`) — không đẻ bản chép thứ tư.
- Lệnh VM mới, dùng CHUNG khung với `ChayThuBridge` (không chép khung 60 dòng lần hai).
- Vá các đường "đóng"/"chạy" để cửa sổ để lại không thành cửa sổ mồ côi giữ khoá hồ sơ + tranh cổng 47821.
- Nhả `chrome.debugger` khỏi tab picker trước khi buông cửa sổ (best-effort) để người dùng không phải nhìn
  banner gỡ lỗi.
- Test cho mọi hàm quyết định mới + test giữ nút trên XAML, kèm THỬ PHÁ từng cái.

**Không làm:**

- Không đụng vòng lặp shop, không đụng phần đang sửa dở của `plans/2026-08-18-vong-khong-chet-vi-3-shop-hong.md`
  ngoài việc rút chung hàm đọc danh sách shop.
- ~~Không đổi hành vi `ChayThuBridge` (chỉ rút khung dùng chung)~~ — **sửa 18/08 sau nghiệm thu:** `ChayThuBridge`
  dùng chung khung nên nay cũng xin/nhả khoá xuyên máy và cũng đặt `_pocAccountId`. CHẤP NHẬN: cho một trong hai
  lệnh đi vòng khác chính là chỗ hai bản trôi lệch nhau, mà lại lệch đúng ở khâu nhả khoá — khâu nguy hiểm nhất.
  Câu chữ nhật ký của nó vẫn giữ nguyên 100% (nghiệm thu đối chiếu từng câu). Không đụng `RunSliceCoreAsync`
  (bản không-`Arm`).
- Không thêm nút lên ribbon. Không thêm chặng chờ mới vào `OrdersBridgeChannel`.
- Không tự dừng phiên đang chạy hộ người dùng.

## 3. Các bước thực hiện

### Bước 1 — Core: rút hàm đọc danh sách shop (KHÔNG đổi hành vi)

File `orders/XuLyDonShopee.Core/Services/OrdersBridgeSession.cs`. Thêm — **`internal static`, nhận channel**,
đúng khuôn `SsoQuaCauNoiAsync:616` (nhờ vậy `BridgeTestRig` lái được, xem Bước 7):

```csharp
/// <summary>Đọc bảng shop ở picker: Arm → readShopList → parse → bắn callback tab "Shops". Ba đường gọi
/// (vòng chạy, mở lại giữa vòng, mở-list-shop) đi CHUNG hàm này — trước đây là ba bản chép tay.</summary>
internal static async Task<IReadOnlyList<ShopListItem>> DocDanhSachShopAsync(
    OrdersBridgeChannel channel, Action<IReadOnlyList<ShopListItem>>? onShopListRead, CancellationToken ct)
```

thân đúng bằng `:878-882` hiện tại. Thay vào `:793-797` và `:878-882` (gọi với `_channel, _onShopListRead, ct`);
**giữ nguyên dòng log riêng của từng chỗ** (hai chỗ đang nói hai câu khác nhau, không được gộp).
**KHÔNG đụng** `RunSliceCoreAsync:1246` (bản không-`Arm`, ngữ nghĩa khác).

### Bước 2 — Core: phương thức công khai `MoDenDanhSachShopAsync`

```csharp
public async Task<OrdersBridgeSliceResult> MoDenDanhSachShopAsync(
    OrdersLoginParams login, CancellationToken ct = default)
```

- Dùng lại `LoginAndReachPickerAsync` (nguyên xi — chính nó là "đúng như flow chạy acc").
- `_channel.CaptchaSeen` ⇒ trả `Captcha: true`. `err is not null` ⇒ trả `Error: err`.
- `DocDanhSachShopAsync` → log `"Đã tới trang chọn shop — {N} shop. DỪNG tại đây, cửa sổ Brave để nguyên cho bạn
  xem."` → best-effort nhả debugger (Bước 3) → trả `new OrdersBridgeSliceResult(shops, null, null, false, null)`.
- `OperationCanceledException` **NÉM TIẾP** (nút Dừng); `TimeoutException`/`Exception` khác → gói thành `Error`
  (khuôn `RunLoginThenSliceAsync:316-345`).
- **TUYỆT ĐỐI không gọi `DongTrinhDuyetSach()`** ở bất kỳ lối ra nào của hàm này — kể cả lối lỗi (người dùng cần
  nhìn thấy trình duyệt đang kẹt ở đâu). Tầng App lo đóng.

### Bước 3 — Nhả `chrome.debugger` khỏi tab picker (best-effort, không chặn luồng)

- `extensions/shopee-orders/background.js`: thêm nhánh `case "detachDebugger"` gọi hàm mới trong `flow-shop.js`:
  `chrome.debugger.detach({ tabId: ctx.listTabId })` bọc try/catch, KHÔNG gửi phản hồi (C# không chờ).
- C# trong `MoDenDanhSachShopAsync`: `try { await _channel.SendAsync(new { action = "detachDebugger" }); await
  Task.Delay(300, ct); } catch { }` — **không** thêm chặng/TCS mới, **không** để lỗi ở đây biến kết quả thành lỗi.
- Lý do an toàn: extension bản cũ không có nhánh này thì action bị nuốt im lặng (`background.js` không có
  `default`).

### Bước 4 — App/VM: khung dùng chung + lệnh mới

File `orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs`.

1. **Hàm quyết định THUẦN** (test được, khuôn `IsSessionReadyForActions:349`):

```csharp
public enum QuyetDinhLatCat { Mo, TuChoiViPhienChay, TuChoiViDangChay }

public static QuyetDinhLatCat QuyetDinhChayLatCat(bool phienProductionDangChay, bool latCatDangChay)
```

thứ tự chốt cửa: `latCatDangChay` trước, rồi `phienProductionDangChay` (giữ đúng thứ tự `ChayThuBridge:220-233`).

2. **Khung dùng chung** `ChayLatCatAsync(string tenViec, Func<OrdersBridgeSession, CancellationToken,
   Task<string>> than)`: gom nguyên phần đang có của `ChayThuBridge` — chốt cửa, `_bridgeRunning`, `_bridgeCts`,
   `TryKillPoc()` đầu, `TaoPhienCauNoi`, `finally { _pocProcess = session.Process; }`, bắt
   `OperationCanceledException`/`Exception`, `finally` hạ cờ + `Dispose` kênh. `than` trả về CÂU TỔNG KẾT để ghi
   log + `BusyStatus`. `ChayThuBridge` viết lại trên khung này (giữ nguyên từng câu chữ log của nó).

3. **Lệnh mới**:

```csharp
[RelayCommand]
private Task MoListShopAsync() => ChayLatCatAsync("Mở list shop", async (phien, ct) => { ... });
```

thân: gọi `phien.MoDenDanhSachShopAsync(login, ct)` rồi trả câu tổng kết (`Captcha`/`Error`/`{N} shop`).

4. `TaoPhienCauNoi` thêm tham số tuỳ chọn `Action<IReadOnlyList<ShopListItem>>? onShopListRead = null`; **chỉ**
   lệnh mới truyền vào: `shops => { _services.Results.UpsertShops(accountId, shops);
   _services.RaiseShopListChanged(accountId); }` (đúng như `AccountSession.cs:499-506`).

5. **Cửa sổ để lại phải theo dõi được**: thêm `_pocAccountId` (tài khoản của cửa sổ đang để lại) và cờ suy ra
   `CuaSoXemDangMo => _pocProcess is { HasExited: false }`. `TryKillPoc` xoá cả hai + bắn
   `OnPropertyChanged(nameof(CanStop))`.

6. **KHOÁ CHẠY XUYÊN MÁY (bắt buộc — máy này có 2 máy online).** Đường `ChayThuBridge` hiện đi TẮT qua khoá này
   (`AccountSession.XinKhoaChayAsync:395` chỉ nằm trên đường phiên production). Cửa sổ "Mở list shop" chiếm tài
   khoản trên Shopee y như một vòng chạy ⇒ mở lúc máy kia đang chạy tài khoản đó là phá vòng của máy kia.

   - TRƯỚC khi mở: nếu `_services.AcquireAccountLease` không null thì gọi; `Ok == false` ⇒ **từ chối**, ghi
     `AccountSession.CauBiTuChoiKhoa(kq.HolderMachine)` (dùng lại đúng câu chữ, `AccountSession.cs:380`) vào log
     + `BusyStatus`, KHÔNG mở gì.
   - Khi đóng cửa sổ (`TryKillPoc`) hoặc lệnh kết thúc bằng lỗi: gọi `_services.ReleaseAccountLease` đúng MỘT
     lần cho login đã xin (cờ kiểu `Interlocked` như `_dangGiuKhoa`), nuốt lỗi. Nhả sót = tài khoản bị coi là
     "đang chạy ở máy X" tới khi lease hết hạn ~5' (`AppServices.cs:230-235`).
   - Hook null (chạy độc lập, không hub) ⇒ chạy như thường, không log gì thêm — y hệt `XinKhoaChayAsync:397-401`.

### Bước 5 — Đóng cửa sổ để lại ở MỌI đường "dừng"/"chạy" (lỗ hổng thật, không phải phòng xa)

Hàm thuần: `public static bool NenDongCuaSoXem(long? pocAccountId, IReadOnlyCollection<long> idsTacDong)`
(null ⇒ false; có trong danh sách ⇒ true).

| Đường | Hiện tại | Sửa thành |
|---|---|---|
| `Run` (`:122`) | đã `TryKillPoc()` | giữ nguyên |
| `RunSelected` (`:153`) | **không** đóng ⇒ cửa sổ cũ giữ khoá hồ sơ + hai extension cùng nối cổng 47821 | `TryKillPoc()` khi `NenDongCuaSoXem(...)` |
| `StopSelected` (`:75`) | **không** đóng ⇒ bấm Dừng mà cửa sổ vẫn còn | `TryKillPoc()` khi `NenDongCuaSoXem(...)` |
| `StopAllAsync` (`:85`) | **không** đóng ⇒ "Dừng tất cả" không đóng hết Brave, sai cả tooltip ribbon | `TryKillPoc()` vô điều kiện |
| `CanStop` (`:39`) | `CanStopSeller \|\| _bridgeRunning` ⇒ cửa sổ còn mở mà nút Dừng đã tắt | `TinhCanStop(CanStopSeller, _bridgeRunning, CuaSoXemDangMo)` (hàm thuần) |

### Bước 6 — XAML: nút "Mở list shop"

File `orders/XuLyDonShopee.App/Views/AccountsView.xaml`, hàng tiêu đề tab Shops (`:669-687`):

- Bọc thành `<DockPanel DockPanel.Dock="Top" Margin="0,0,0,14" LastChildFill="False">`; `StackPanel` cũ giữ
  nguyên nội dung, `DockPanel.Dock="Left"`; nút `DockPanel.Dock="Right"`.
- Nút: `x:Name="MoListShopButton"`, `Style="{StaticResource secondary}"` (luật nút: không nền, chỉ icon màu),
  nội dung `c:PathIcon Data="{DynamicResource IconOpenExternal}"` + `TextBlock` style `btnLabel` text
  "Mở list shop", `Command="{Binding MoListShopCommand}"`, `IsEnabled="{Binding CanRun}"`.
- `ToolTip`: "Mở Brave bằng hồ sơ của chính tài khoản này, đi đúng luồng chạy (cookie hồ sơ → SSO Kênh Người bán
  → trang chọn shop) rồi DỪNG ở trang chọn shop. Cửa sổ để nguyên cho bạn thao tác tay; bấm 'Dừng tất cả' để
  đóng. Đang có phiên chạy thì phải dừng phiên trước."

### Bước 7 — Test (`orders/XuLyDonShopee.Tests/MoListShopTests.cs`, xunit)

Mốc đo được của bộ test hiện tại (đã chạy thật trong lượt khảo sát): **Passed 1825 / Failed 0 / Skipped 0**,
~4 phút 25 giây. Cả `XuLyDonShopee.App` lẫn `.Core` đều mở `InternalsVisibleTo` cho project test ⇒ hàm quyết
định mới để `internal static` là đủ, KHÔNG cần nới `public`.

1. **Hành vi THẬT của đường mới, không cần trình duyệt** — dùng `BridgeTestRig` (`orders/XuLyDonShopee.Tests/BridgeTestRig.cs`,
   14 file test đang dùng; nó dựng `OrdersBridgeChannel` thật trên cổng loopback trống + một `ClientWebSocket`
   đóng vai extension). Khuôn có sẵn: `OrdersBridgeSsoTests.cs:56`. Test cho `DocDanhSachShopAsync`:
   - rig nhận được ĐÚNG lệnh `readShopList`, trả `pageData kind=shopList` với 2-3 dòng shop;
   - hàm trả đúng số `ShopListItem` đã parse, và callback `onShopListRead` được gọi ĐÚNG MỘT lần với đúng danh sách;
   - **và rig KHÔNG nhận thêm lệnh nào nữa** (chờ ngắn rồi khẳng định không có `openShopDetail`) — đây chính là
     mệnh đề "đến list shop thì DỪNG", phải có test chứ không chỉ đọc mã.
2. `QuyetDinhChayLatCat`: 4 ca (rảnh → Mo; lát cắt đang chạy → TuChoiViDangChay; phiên đang chạy →
   TuChoiViPhienChay; cả hai → TuChoiViDangChay, khoá đúng thứ tự ưu tiên).
3. `TinhCanStop`: 4 ca — quan trọng nhất là (false,false,**true**) ⇒ true.
4. `NenDongCuaSoXem`: 3 ca (null · id thuộc danh sách · id ngoài danh sách).
5. **Khoá xuyên máy** (khuôn `OrdersAccountLeaseTests.cs:53` — hook là lambda tiêm được): đặt
   `services.AcquireAccountLease = (login, ct) => Task.FromResult(new OrdersLeaseResult(false, "MAY-KHO-1"))`
   → chọn một tài khoản → `MoListShopCommand.Execute(null)` → khẳng định `BusyStatus` chứa `MAY-KHO-1` và
   KHÔNG có tiến trình/phiên nào được mở.
6. VM thật (`TempDatabase` + `AppServices`, khuôn `AccountsViewModelTests:14`): chưa chọn tài khoản ⇒
   `MoListShopCommand.Execute(null)` không ném, không mở gì, không ghi log.
7. `[StaFact]` (khuôn `AccountsViewVongDoiTests:65`): `new AccountsView()` → tìm `MoListShopButton` →
   khẳng định `Command` bind tới `MoListShopCommand`, `IsEnabled` bind `CanRun`, và nút nằm trong tab Shops.

Việc "`MoDenDanhSachShopAsync` không gọi `DongTrinhDuyetSach`/`openShopDetail`" **không** viết thành test đọc
file nguồn (test trang trí) — nó là tiêu chí đọc mã ở mục 4.5.

**THỬ PHÁ bắt buộc** cho mọi test mới: sửa ngược code (vd cho `DocDanhSachShopAsync` gửi thêm `openShopDetail`,
đảo thứ tự chốt cửa, bỏ vế `CuaSoXemDangMo`, bỏ nhánh từ chối theo lease, đổi `x:Name` nút) → test phải ĐỎ →
khôi phục. Ghi rõ trong báo cáo từng cái sửa gì, test nào đỏ, thông báo lỗi ra sao.

## 4. Tiêu chí nghiệm thu

1. `dotnet build ShopeeSuite.sln -c Debug` → **0 error, 0 warning**.
2. `dotnet test orders/XuLyDonShopee.Tests/XuLyDonShopee.Tests.csproj` → xanh 100%; **mốc trước = 1825 passed**,
   nêu số sau và số test mới.
3. Thử phá: mỗi test mới có một cách sửa ngược làm nó ĐỎ, ghi lại lệnh + dòng đỏ.
4. `grep -n "readShopList" orders/XuLyDonShopee.Core/Services/OrdersBridgeSession.cs` → còn đúng **2** chỗ gửi
   lệnh (helper mới + `RunSliceCoreAsync` bản không-Arm), không còn 3 bản chép kiểu Arm.
5. Đọc mã `MoDenDanhSachShopAsync`: không có `DongTrinhDuyetSach`, không có `openShopDetail`, không có vòng lặp
   shop trên mọi lối ra.
6. `CanStop` trả true khi chỉ còn cửa sổ để lại (test 7.2 phủ).
7. **Chạy app thật** — ✅ **ĐÃ CHẠY 18/08 16:27–16:29** (phiên chính tự lái app bằng UI Automation, chạy bản
   `bin/Debug` SONG SONG với app production của người dùng đang nghỉ giữa hai vòng; không cắt vòng nào):
   - Bấm "Mở list shop" → **5 giây** sau: `16:27:26 Đã tới trang chọn shop — 12 shop. DỪNG tại đây, cửa sổ Brave
     để nguyên cho bạn xem.` · `16:27:26 Cookie hồ sơ còn hạn — … BỎ QUA bước đăng nhập Playwright.` ·
     **KHÔNG** một dòng `[Shop x/12] … mở Chi tiết` nào · cửa sổ Brave sống, tiêu đề
     `Shopee - Kênh Người bán - Brave` · `16:27:27 Cầu nối: extension ĐỨT` (kênh WS đóng ⇒ nhả cổng 47821) ·
     lưới tab "Shops" tự cập nhật **12 dòng**.
   - Bấm **"Đóng cửa sổ"** → `16:28:28 Đã đóng cửa sổ list shop.`, Brave về **0 tiến trình**, nút tự ẩn.
     *(Câu chữ đổi so với bản plan đầu: mục F thay "bấm ■ Dừng" — nút KHÔNG tồn tại — bằng nút "Đóng cửa sổ"
     mới và "Dừng tất cả" trên ribbon.)*
   - Bấm nút LẦN NỮA lúc đang có cửa sổ → đóng cái cũ (`PID 22048 đã THOÁT`), mở cái mới, cuối cùng đúng **MỘT**
     cửa sổ. Không chồng tiến trình, không tranh khoá hồ sơ.
   - ⚠ **CHƯA kiểm bằng tay:** ca "bấm nút khi phiên đang chạy → từ chối". Kiểm thật đòi phải cho chạy một vòng
     production thật trên máy người dùng (đụng đơn thật) nên dừng lại; đường đó hiện chỉ được khoá bằng test hàm
     quyết định `QuyetDinhChayLatCat`.

## 5. Rủi ro & lưu ý

- **GIẾT CHÉO THEO EXTENSION (phải nói cho người dùng biết).** Mỗi lần phóng trình duyệt cầu nối,
  `OrdersBridgeLauncher.Launch` gọi `BrowserProfileGuard.FreeProfile(..., alsoMatchBridgeExtension: true)`
  (`OrdersBridgeLauncher.cs:32`), mà bộ lọc khớp cả chuỗi `shopee-orders` trong dòng lệnh
  (`BrowserProfileGuard.cs:70`) ⇒ **giết MỌI trình duyệt cầu nối, kể cả của hồ sơ KHÁC**. Nghĩa là cửa sổ "Mở
  list shop" của tài khoản A sẽ bị đóng phắt ngay khi bất kỳ tài khoản nào bắt đầu một vòng. Đây là hành vi có
  sẵn, KHÔNG sửa trong việc này — nhưng phải ghi vào ToolTip/báo cáo, kẻo người dùng tưởng app tự dưng nuốt mất
  cửa sổ.
- **Cửa sổ mồ côi giữ khoá hồ sơ** là rủi ro số 1 ⇒ Bước 5 là bắt buộc, không phải tô điểm.
- **Hai extension cùng nối cổng 47821**: cửa sổ để lại vẫn thử nối lại mỗi 1200ms (`core.js:46`). Phiên
  production mở cổng mà cửa sổ cũ chưa đóng ⇒ HAI extension cùng một kênh, lệnh bay tới nhầm trình duyệt. Đây
  mới là lý do nặng nhất của Bước 5 (nặng hơn cả khoá hồ sơ).
- `LoginAndReachPickerAsync` **có thể chạy Playwright** khi cookie hết hạn, và bước đó **chờ người nhập mã** —
  lệnh mới có thể chạy vài phút chứ không phải vài giây. `BusyStatus` phải nói rõ đang làm gì.
- Cây làm việc đang có 6 file sửa dở chưa commit của các phiên trước (log bám đáy · vòng không chết vì 3 shop
  hỏng); không phiên nào còn chạy (đã kiểm bằng `list_sessions`). **Chỉ stage đúng file của việc này**, không
  `git add -A`, và KHÔNG commit trừ khi người dùng bảo.
- Không chạy được test tích hợp tranh cổng ở đây (module này không có bộ test cần DB/cổng) — `nghiem-thu` chạy
  build + test, `phan-bien` chỉ đọc mã + chạy unit test.

## 6. Kết quả khảo sát (workflow `map-mo-list-shop`, 5 người đọc song song)

Đã gộp vào mục 1. Ba phát hiện đổi hướng bản vá so với dự định ban đầu:

1. Khối đọc-danh-sách-shop đã bị chép 3 lần ⇒ rút helper thay vì thêm bản thứ tư.
2. Cổng WS là hằng số ⇒ bắt buộc `Dispose()` kênh sau khi buông cửa sổ.
3. `StopAll`/`StopSelected`/`RunSelected` **không** đóng cửa sổ lát cắt ⇒ lỗ hổng có sẵn, nút mới làm nó thành
   lỗi gặp hằng ngày.

## 7. Vòng 1: nghiệm thu + phản biện (18/08, chạy song song)

**Nghiệm thu:** build 0 warning · `1833 passed / 0 failed` (mốc trước 1825, +8 test mới) · tự làm lại 5/8 lượt
thử phá, cả 5 ĐỎ đúng test dự kiến, khôi phục xác nhận bằng md5. 5/7 tiêu chí ĐẠT, 4.6 ĐẠT MỘT PHẦN, 4.7 CHƯA
KIỂM. Lưu ý: **1833 là số của CÂY BẨN** (3 việc chồng nhau), không thuần bản vá này.

**Phản biện tìm ra 3 lỗi CHẶN CHỐT — tất cả ở cái khoá xuyên máy do chính plan này thêm vào** (mục 4.6 của Bước
4). Đã tự kiểm và xác nhận cả ba là thật, gốc rễ ở
`suite/Shopee.Suite/Infrastructure/OrdersModuleHost.AccountLease.cs`: `_heldLeases` là `HashSet` **không đếm
chủ** (`:40`), có **timer heartbeat 60s giữ lease sống tới khi tắt app** (`:100`) chứ KHÔNG tự hết hạn 5' như
plan giả định, và `ReleaseAccountLease` là DELETE thẳng theo (account, machine) (`:131-137`).

1. Nhánh `Error`/`Captcha` của `MoDenDanhSachShopAsync` **cố ý không ném** ⇒ không `catch` nào chạy ⇒ **không nhả
   khoá**. Lease treo tới khi tắt app; máy 2 bỏ qua tài khoản đó ở MỌI vòng, không dấu vết nào trên máy 1.
2. Người dùng tự bấm X đóng Brave — **thao tác tự nhiên nhất của chính tính năng này** — cũng treo khoá y hệt.
3. Một key hai chủ: lượt nhả của cửa sổ xem **XOÁ mất lease của phiên production đang chạy** cùng tài khoản
   (ca xác định qua `TruyCapTk` — đường Bước 5 bỏ sót; và ca race qua `Run` vì nhả là fire-and-forget).

### Vòng sửa đã giao (A–H)

- **A.** Nhả khoá LUÔN trong `finally` của `ChayLatCatAsync` ⇒ **cửa sổ để lại KHÔNG giữ khoá**; khoá chỉ bảo vệ
  đoạn MỞ (SSO/đăng nhập). Đổi lấy: máy 2 có thể bắt đầu vòng trong lúc người dùng đang xem — chấp nhận, vì rò
  một lease có heartbeat hỏng nặng hơn nhiều lần cái nó bảo vệ.
- **B.** `Run` · `RunSelected` · `TruyCapTk` từ chối khi đang chạy lát cắt (đối xứng với chốt cửa sẵn có).
- **C.** Gán lại CẶP `(_pocProcess, _pocAccountId)` ở `finally` trong.
- **D.** Câu tổng kết nhánh lỗi suy từ `phien.Process is { HasExited: false }` — câu cũ nói sai sự thật ở nhánh
  đăng-nhập-chưa-xong (bản sạch đã bị `LoginAndReachPickerAsync:809` đóng trước đó).
- **E.** Tách phần SAU-picker thành `internal static` nhận channel → test rig khẳng định chuỗi lệnh **đúng bằng**
  `[readShopList, detachDebugger]`. Test cũ chỉ khoá cái helper vốn không thể gửi thêm lệnh ⇒ đo hụt phạm vi.
- **F.** Thêm nút **"Đóng cửa sổ"** cạnh "Mở list shop" + sửa mọi câu chữ nhắc "■ Dừng" — `grep CanStop` trên
  mọi `.xaml` ra **0 chỗ bind**, tức UI đang chỉ người dùng bấm một nút KHÔNG TỒN TẠI.
- **G.** Dock hai nút TRƯỚC cụm trái, kẻo cửa sổ hẹp là nút biến mất im lặng.
- **H.** Ghi chú, không sửa: `ChayThuBridge` đổi hành vi (đã sửa mục 2) · khe hở cổng 47821 lúc phiên mới mở cổng
  trước khi `FreeProfile` giết cửa sổ cũ · `_dbgTab` không được xoá sau detach.

## 8. Vòng 2: nghiệm thu + phản biện (18/08, chạy song song)

**Nghiệm thu:** **8/8 hạng mục A–H ĐẠT** · build 0 warning (`-t:Rebuild`) · `1835 passed / 0 failed` (1825 → +10
test mới) · tự phá lại 5 lượt, đỏ đúng test + đúng dòng · duyệt HẾT lối ra của `ChayLatCatAsync` và xác nhận
không lối nào thoát mà không nhả khoá. Chỉ ra một lỗ **kiểm chứng** (không phải lỗ code): **hạng mục A — nặng
nhất — là hạng mục DUY NHẤT không có test hồi quy nào**; dời `NhaKhoaXem()` từ `finally` về `catch` thì 1835
test vẫn xanh.

**Phản biện tìm thêm 2 CHẶN CHỐT + 4 NÊN SỬA**, đã tự kiểm và xác nhận. Vòng sửa 3 (I–VIII):

- **I (chặn chốt).** Nút "Đóng cửa sổ" **luôn Collapsed suốt lúc lát cắt chạy** (`_pocProcess` chỉ được gán ở
  `finally` trong), mà hai câu duy nhất nhắc tên nút đó **chỉ phát ra khi lát cắt đang chạy** ⇒ trùng khớp 100%
  với đúng lỗi "chỉ người dùng bấm nút không tồn tại" của vòng 1. Nặng nhất ở nhánh cookie hết hạn (chờ nhập mã
  vài phút, không có nút huỷ). Sửa: cờ gộp `CoTheDongCuaSo = CuaSoXemDangMo || _bridgeRunning`.
- **II (chặn chốt).** `finally` hạ `_bridgeRunning` **trước** khi bắn `NhaKhoaXem()` fire-and-forget ⇒ lượt
  DELETE có thể hạ cánh SAU lượt reserve của phiên production vừa mở, **xoá đúng row vừa cấp** (cùng
  `machine_id`), và `HeartbeatLeases` là **UPDATE thuần không INSERT** (`HubDatabase.Leases.cs:173`) nên không
  hồi phục ⇒ hai máy cùng chạy một subaccount, không dòng log nào. Sửa: `await` lượt nhả TRƯỚC khi hạ cờ.
- **III.** Thêm test hồi quy cho A (`ChayLatCatAsync` thành `internal`, `than` giả, đếm `ReleaseAccountLease`).
- **IV.** `DongCuaSoXem` ghi log theo `_editingId` thay vì `_pocAccountId` ⇒ đóng cửa sổ của A mà ghi vào nhật ký
  của B.
- **V.** Người dùng tự bấm X đóng Brave ⇒ nút kẹt hiện, bấm vào im lặng. Đăng ký `Process.Exited`.
- **VI.** Chốt cửa chỉ xét tài khoản đang xem, trong khi **cổng 47821 là hằng toàn máy** ⇒ mở list shop cho B lúc
  A đang chạy ra lỗi cổng khó hiểu, và có khe **giết vòng của A**. Đổi sang xét mọi phiên đang chạy.
- **VII.** Nút "Đóng cửa sổ" hiện ra **đúng dưới con trỏ** sau ~5s, đẩy "Mở list shop" sang trái ⇒ bấm phát nữa
  theo thói quen là đóng luôn cửa sổ vừa mở. Sửa: luôn hiện, chỉ đổi `IsEnabled`.
- **VIII.** Xin khoá TRƯỚC, giết cửa sổ cũ SAU (đang ngược ⇒ bị từ chối thì mất cửa sổ mà chẳng được gì).

Ghi chú không sửa: `BusyStatus` bị `UpdateSelectedSessionStatus` xoá trắng khi có sự kiện phiên (bệnh sẵn có của
cả màn) · huỷ trúng `Task.Delay(300)` của detach làm mất câu tổng kết dù shop đã đọc xong · `_pocAccountId` có
thể thành "chủ ma" khi trình duyệt chưa kịp phóng.

## 9. Vòng 3 + vòng 4: chạy app thật bắt được thứ 1841 test không bắt được

Vòng sửa 3 (I–VIII) xong, build 0 warning, 1839 test xanh. **Phiên chính chạy app thật lúc 17:04 ⇒ TÍNH NĂNG
HỎNG HẲN.** Hai lỗi, cả hai do chính vòng 3 đẻ ra:

1. **Khung tự huỷ lượt của chính nó.** Mục VIII đảo thứ tự "xin khoá trước, giết cửa sổ cũ sau" — nhưng
   `TryKillPoc()` mở đầu bằng `_bridgeCts.Cancel()`. Thứ tự cũ: huỷ token lượt TRƯỚC (vô hại). Thứ tự mới: huỷ
   đúng token vừa tạo cho lượt SẮP CHẠY. Nhật ký thật: `17:04:32 Chờ extension nối cầu (ready)…` →
   `17:04:32 Đã hủy mở list shop.` — cùng giây, không ai bấm huỷ; Brave mở rồi đứng ở `Nền tảng tài khoản phụ`,
   thành cửa sổ mồ côi. **Mọi cú bấm đều tự sát.**
2. **`CoTheDongCuaSo` không bao giờ được báo đổi.** `BaoCoDoiCuaSoXem()` bắn `CanStop` + `CuaSoXemDangMo` nhưng
   quên đúng cờ mà XAML bind ⇒ nút "Đóng cửa sổ" chết cứng. Đo UI Automation: `enabled=False` cả lúc đang chạy
   lẫn lúc còn cửa sổ ⇒ **mục I của vòng 3 trên thực tế chưa tồn tại**.

**Vòng sửa 4:** tách `DongCuaSoCuLai()` (chỉ đóng cửa sổ cũ — không `Cancel`, không nhả khoá) khỏi `TryKillPoc()`
(huỷ + đóng + nhả, giữ cho các nút Dừng); `BaoCoDoiCuaSoXem()` bắn **cả ba** cờ. Thêm 2 test khoá đúng hai cơ chế
đã hở: `KhungKhongTuHuy_ThanLenhNhanDuocTokenCONSONG` (thân lệnh phải nhận token CÒN SỐNG) và
`CoTheDongCuaSo_PhaiDuocBaoDoi_TrongMotLuotChayKhung` (khoá NHỊP BÁO ĐỔI, không phải đường bind).

**Bài học ghi lại cho lần sau:** bộ test khoá rất chặt *giá trị hàm* và *đường bind*, nhưng không khoá *nhịp
`PropertyChanged`* lẫn *token của chính lượt đang chạy* — hai lớp chỉ lộ khi app chạy thật. Hạng mục nào không có
test chạm tới cơ chế runtime thì phải nói rõ là CHƯA kiểm chứng, đừng để lẫn vào danh sách "đã sửa".

### Chạy app thật lần cuối (17:15–17:18, bản cuối cùng) — tất cả ĐẠT

| Kiểm | Kết quả đo được |
|---|---|
| Mở list shop | `17:16:21 Đã tới trang chọn shop — 12 shop. DỪNG tại đây…` — 6 giây, **0 dòng "Đã hủy"**, 0 dòng `[Shop x/12]` |
| Cửa sổ để lại | Brave sống, tiêu đề `Shopee - Kênh Người bán - Brave`; kênh WS đóng ngay (nhả cổng 47821) |
| **Mục I** | Nút "Đóng cửa sổ" `enabled=True` **NGAY trong lúc lát cắt chạy** (trước vòng 4: False) |
| **Mục VII** | Toạ độ hai nút KHÔNG đổi ở mọi trạng thái (`1974,740` / `2153,740`) — hết bẫy bấm nhầm |
| Huỷ giữa chừng | Bấm "Đóng cửa sổ" sau 2s → `Đã đóng cửa sổ list shop.` + `Đã hủy mở list shop.`, brave = **0** |
| **Mục V** | Người dùng tự bấm **X** trên Brave → nút "Đóng cửa sổ" **tự mờ** (`Process.Exited` chạy đúng) |

### Nghiệm thu vòng cuối

**ĐẠT — 7/7 tiêu chí mục 4 · 8/8 A–H · 8/8 I–VIII.** Build 0 warning (`-t:Rebuild`), **1841 passed / 0 failed**
(1825 nền → 1833 → 1835 → 1839 → 1841; `MoListShopTests` = **16 test**). Tự làm lại cả 2 lượt thử phá vòng 4,
đỏ đúng test + đúng dòng, khôi phục xác nhận bằng md5. Xác nhận `DongCuaSoCuLai` chỉ có 2 chỗ gọi và **không**
đường Dừng nào đi nhầm vào nó.

Hai lưu ý KHÔNG chặn chốt: (a) test `CoTheDongCuaSo_PhaiDuocBaoDoi_…` chỉ khoá "có bắn", không khoá "bắn đúng
lúc" — chỗ trống đó hiện bù bằng bằng chứng chạy tay; (b) **mục VI (từ chối khi tài khoản KHÁC đang chạy) chưa
từng kiểm tay qua cả 4 vòng** — kiểm thật đòi cho chạy một vòng production thật, đụng đơn thật.

## 10. Vòng 5: phản biện cuối — cùng một lỗi quay lại qua cửa khác

Phản biện vòng cuối tìm **1 CHẶN CHỐT**: `TryKillPoc` vẫn nhả khoá kiểu bắn-đi-rồi-quên **và tiêu luôn cờ**
`_dangGiuKhoaXem`, nên `await NhaKhoaXemAsync()` trong `finally` — đúng lá chắn dựng ở vòng 2 để chống chính lỗi
này — thành **no-op**. Ca: bấm "Mở list shop" → bấm "Đóng cửa sổ" giữa chừng → bấm "Chạy" → lượt DELETE cũ hạ
cánh sau lượt RESERVE mới, **xoá đúng dòng lease vừa cấp**, heartbeat (UPDATE thuần) không hồi phục ⇒ hai máy
cùng chạy một subaccount.

Chứng minh đường đó không bao giờ là "lưới an toàn": cờ chỉ = 1 giữa `XinKhoaXemAsync` và `finally` — cả hai đều
trong khung ⇒ `_dangGiuKhoaXem == 1` **kéo theo** `_bridgeRunning == true` ⇒ nhánh fire-and-forget **100% chỉ nổ
đúng ca nguy hiểm**. Sửa: xoá hẳn `NhaKhoaXemKhongCho` khỏi codebase; khung luôn là nơi nhả, và nhả có `await`.

Kèm 4 mục nhỏ: test hạng mục I đo hụt (xoá đúng dòng gây lỗi thật vẫn xanh → đo lại trong hook xin khoá, khe duy
nhất phân biệt được) · `Dispose` kênh TRƯỚC khi `await` nhả khoá (cổng 47821 toàn máy bị giữ thêm tới 8s) ·
**"Mở list shop" đang XOÁ SẠCH tick tiến độ** (`RaiseShopListChanged` bị `OnShopListChanged` hiểu là "lượt chạy
mới") → thêm `luotChayMoi: false` · dọn 3 comment mô tả sai mã.

Kết quả: build 0 warning, **1843 passed** (+2), 3 lượt thử phá đỏ đúng chỗ.

## 12. Vòng 6 (bản CUỐI): bấm nút ⇒ TẠM DỪNG check đơn, đóng cửa sổ ⇒ CHẠY LẠI

Người dùng đổi ý giữa chừng, và luật mới **đơn giản hơn hẳn** spec ở mục 11 (mục 11 giữ lại làm lịch sử; toàn bộ
code của nó — `ThoiDiemVongKe`, chốt cửa ba nhánh, hẹn giờ tự đóng — **đã gỡ sạch**, `grep` = 0).

Nguyên văn: *"tôi không muốn phải login lại, khi nào chạy nút đó thì sẽ dừng việc check đơn lại."* và
*"Khi 1 client chạy chương trình km, các client khác không được chạy cho acc đó nữa (giống phần check đơn, đảm
bảo chỉ 1 client / 1 acc / tại 1 thời điểm)."*

**Luật:** bấm nút ⇒ tạm dừng phiên check đơn của tài khoản đó ⇒ đóng cửa sổ (nút · Stop\* · tự bấm X · lỗi/huỷ)
⇒ **tự chạy lại** phiên. Người dùng chủ động bấm "Dừng đã chọn"/"Dừng tất cả" ⇒ **không** chạy lại.

**THỨ TỰ BẮT BUỘC — lần thứ ba cùng một cơ chế "một khoá, hai chủ" suýt cắn:**
`dừng check đơn → CHỜ DỪNG HẲN (IsRunning == false VÀ BraveProcess == null, hạn 30s) → xin khoá xuyên máy →
đóng cửa sổ cũ → chạy thân lệnh`. Hết hạn chờ ⇒ **fail-closed**: không xin khoá, không mở gì.
Lý do: phiên check đơn chỉ nhả khoá trong `finally` của luồng nền; xin sớm ⇒ hub cấp (cùng `machine_id`) ⇒ lượt
DELETE của phiên cũ xoá đúng dòng vừa cấp ⇒ heartbeat UPDATE-thuần không hồi phục ⇒ máy 2 giành mất tài khoản.

**Kết quả:** build 0 warning · **1845 passed** (1843 → +5 test mới, −3 test đã hết công dụng) · 4 lượt thử phá,
trong đó lượt **đảo thứ tự** đỏ với đúng câu *"lượt nhả của nó sẽ xoá đúng lease vừa cấp cho ta, và máy 2 giành
được tài khoản"*, và lượt **fail-open** đỏ với *"Chưa dừng được check đơn mà đã xin khoá xuyên máy."*

### Chạy app thật lần cuối (18:43–18:44) — ĐẠT

```
18:43:08  "Chạy đã chọn" → vòng check đơn chạy THẬT, tới [Shop 1/12], đọc "Chờ Lấy Hàng: 0"
18:43:17  "Mở list shop" → "Tạm dừng check đơn … vòng đang chạy dở BỊ CẮT …" → "Đang dừng phiên…"
18:43:18  "Đã dừng phiên."            ← chỉ SAU dòng này mới có "Mở trình duyệt sạch…" của lát cắt
18:43:22  "Đã tới trang chọn shop — 12 shop. DỪNG tại đây."      (mượn xong mất 6 giây)
18:44:11  "Đóng cửa sổ" → "Đã đóng cửa sổ list shop." + "Đã đóng cửa sổ — chạy lại check đơn."
18:44:17  "Đọc được 12 shop — bắt đầu lặp qua từng shop." [Shop 1/12] alina99.store
```
Không phải đăng nhập lại lần nào. Dọn sạch sau test: 0 tiến trình Brave, app production của người dùng không bị đụng.

**Rủi ro còn lại, đã báo người dùng:** nút chỉ dừng phiên của ĐÚNG tài khoản đang mở. Tài khoản KHÁC đang chạy
vòng vẫn giữ cổng 47821 (hằng toàn máy) ⇒ lát cắt sẽ chết với lỗi hạ tầng. Máy hiện chỉ có 1 tài khoản nên chưa
chạm; thêm tài khoản thứ hai thì phải mở rộng bước "tạm dừng" sang mọi phiên đang giữ trình duyệt.

## 13. Vòng 8: gom "MƯỢN HỒ SƠ" thành một cụm (sau 2 CHẶN CHỐT + 5 NÊN SỬA của phản biện)

Phản biện chỉ ra hai gốc, và chúng là **một**: khái niệm "đang mượn hồ sơ" bị rải ra 5 chỗ với một biến `long?`.

- **CHẶN CHỐT 1 — chốt "tài khoản KHÁC đang chạy" biến mất.** Luật mới chỉ nói "dừng check đơn (của tài khoản
  đó)", nên mục VI của vòng 3 (xét MỌI phiên, vì cổng 47821 + `FreeProfile` là **toàn máy**) bị mất kèm. Hậu quả
  nặng nhất: mở list shop cho B trong lúc A chạy ⇒ trượt cổng ⇒ `QuyetDinhSauThuBanSach` = `DangNhapLai` ⇒
  **Playwright bắt đăng nhập lại**, đúng thứ người dùng mở lời cấm. Cộng ca hàng đợi tự bắn phiên mới ngay trong
  lock của `OnSessionChanged` ⇒ hai bên cùng phóng trình duyệt, bên sau `FreeProfile` giết bên trước.
- **CHẶN CHỐT 2 — mượn mà không trả**, 3 đường im lặng: "Dừng đã chọn" khi **0 tick** · mượn lượt hai cho tài
  khoản khác (ghi đè chủ nợ cũ) · `Run`/`RunSelected` giết cửa sổ mà không trả.
- **NÊN SỬA 3–7:** vòng chờ 30s không nghe token huỷ (bấm Đóng cửa sổ ⇒ treo đủ 30s rồi báo lỗi sai nguyên
  nhân) · **lỗ `Error`**: `SetError` đặt `State=Error` + `_bridge=null` TRƯỚC khi `finally` nhả khoá ⇒ điều kiện
  "dừng hẳn" bị qua mặt (lần thứ TƯ cùng cơ chế) · khoá nhả quá sớm ⇒ máy 2 giành mất, lượt trả hồ sơ bị từ chối
  và phiên chết hẳn dù log vừa hứa "sẽ chạy lại" · nhật ký nói dối ở lối hết hạn · seam `Sessions { internal set }`
  làm VM **không nghe** `Changed` của stub ⇒ 4 test vòng đời đo trong môi trường không có sự kiện phiên.

**Bản sửa:** `_dsTkDaDung` (HashSet) + `MuonHoSoAsync`/`TraHoSo` — **một chỗ mượn, một chỗ trả** (7 điểm gọi
`TraHoSo`); dừng **mọi** phiên `Active` (gồm `Queued`) và chờ tới khi `Active` **rỗng**; `StopSelected` chỉ bật cờ
khi tập tick **giao** tập đang mượn; vòng chờ nhận `ct`; `AccountSession` nhả khoá **trước** `SetError`; **giữ
khoá tới lúc trả** (đảo lại hạng mục A của vòng 1, có lý do mới: đã có `Process.Exited` + Job Object + một cửa
trả, và heartbeat chết theo tiến trình app nên ca xấu nhất chỉ là lease hết hạn ~5'); seam về **ctor**
`AppServices(dbPath, sessionFactory)`.

**Kết quả:** build 0 warning · **1852 passed** (1845 → +7) · `MoListShopTests` 26 test, chạy còn ~5s (trước 32s).

### Nghiệm thu vòng cuối: **ĐẠT 6/7**, 1 ĐẠT MỘT PHẦN

Tự đo lại khớp cả ba con số; tự làm lại 3 lượt thử phá (đỏ đúng chỗ); và **tự chứng minh cả ba lời khai "chưa
kiểm chứng được" là đúng sự thật**: lỗ `Error`, "giữ khoá tới lúc trả", "nhả khoá cũ trước lượt hai" — sửa ngược
từng cái mà 1852 test vẫn xanh. Vòng 9 bịt hai lỗ đầu bằng **seam phiên cầu nối + tiến trình thật rẻ tiền**
(`cmd /c timeout`), dọn 1 code chết (`DangMuonHoSo`) và 1 xmldoc mô tả ngược chính sách hiện hành.

### Chạy app thật (19:28) — ĐẠT

```
19:28:50  "Tạm dừng check đơn (hoangdh200392:muinx) để mở list shop …"   ← nay liệt kê TÊN tài khoản bị dừng
19:28:51  "Đã dừng phiên."  → mở trình duyệt mới
19:28:52  bấm "Đóng cửa sổ" ngay → "Đã đóng cửa sổ list shop." + "Đã đóng cửa sổ — chạy lại check đơn."
          + "Đã hủy mở list shop."  → phiên check đơn chạy lại NGAY.   Tổng 8 giây, KHÔNG treo đủ 30s.
```

**Chưa kiểm tay được (máy chỉ có 1 tài khoản trong DB):** ca "tài khoản KHÁC cũng bị tạm dừng và được chạy lại".

### ⚠ Stage: cây có BA việc chồng nhau

Sạch, stage nguyên file: `AccountsViewModel.Phien.cs` · `AccountsViewModel.KetQua.cs` · `AppServices.cs` ·
`AccountsView.xaml` · `extensions/shopee-orders/background.js` · `MoListShopTests.cs` (mới) ·
`ShopCheckProgressTests.cs` · 2 file plan.
**Phải tách hunk** (`git add -p`): `AccountSession.cs` · `OrdersBridgeSession.cs` · `flow-shop.js`.
**Không thuộc việc này**: `core.js` · `OrdersBridgeChannel.cs` · `AccountsView.xaml.cs` · 7 file test kia · 2 plan kia.

## 11. Vòng 6 — SPEC CŨ (đã bỏ, giữ làm lịch sử): mở được trong kỳ nghỉ, app tự đóng trước vòng kế

Phản biện chỉ ra hệ quả thật của chốt cửa: kỳ nghỉ 90' vẫn là `SessionState.Running`, mà máy người dùng chạy
liên tục ⇒ nút **gần như luôn từ chối**. Hỏi người dùng, họ chọn **"cho mở + tự đóng trước vòng kế"**.

Ba việc: (1) công khai `ThoiDiemVongKe` (nay chỉ là biến cục bộ `nextRunAt`, `AccountSession.cs:630`, ghi log rồi
vứt); (2) chốt cửa ba nhánh — có phiên đang GIỮ trình duyệt ⇒ từ chối; tất cả đang nghỉ nhưng vòng kế còn dưới
**biên an 2 phút** ⇒ từ chối kèm giờ; còn lại ⇒ cho mở; (3) hẹn giờ tự đóng tại `vòng kế − 2'`, huỷ hẹn ở mọi
đường cửa sổ biến mất (hẹn mồ côi bắn vào cửa sổ lượt sau là đúng loại lỗi đã mất 2 vòng để dập), và **ghi giờ
hẹn ngay vào câu tổng kết** để có bằng chứng chạy tay mà không phải ngồi đợi 90 phút.

Cơ sở phân biệt "đang nghỉ" với "đang chạy": lúc nghỉ `AccountSession` đã đóng trình duyệt + `Dispose` kênh sau
mỗi vòng ⇒ `BraveProcess == null`, cổng 47821 và hồ sơ đều rảnh.

---

## Báo cáo thực thi

*(điền sau)*
