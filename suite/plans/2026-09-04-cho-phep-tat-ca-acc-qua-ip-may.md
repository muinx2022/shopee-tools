# Plan: Checkbox Cài đặt — cho phép mọi tài khoản chạy qua IP máy

- **Ngày:** 2026-09-04
- **Trạng thái:** hoàn thành (2026-09-04 — Auto thực thi; chưa commit code, chưa nối `AccountSessionManager.Start`)
- **Người lập:** Fable · **Người thực thi:** Auto (Cursor)
- **Căn cứ:** user 04/09 — *"sửa thêm phần cài đặt cho app: đang ép nếu từ acc thứ 2 bắt buộc phải có proxy; thêm checkbox cho phép chạy tất cả acc qua IP của client; nếu checked thì bỏ qua phần check proxy."*

## 1. Bối cảnh & mục tiêu

**Luật user đã chốt** (plan `plans/2026-09-04-da-lan-moi-acc-mot-cau-noi.md` §1): trên một máy chỉ một chỗ "IP máy"; acc không-proxy thứ hai muốn chạy **cùng lúc** bị từ chối *"IP máy đang do tài khoản X dùng"*; acc song song phải có proxy riêng không trùng.

**Hiện trạng code (đo 04/09):** luật đó **chưa vào runtime** module Đơn hàng. `AccountSessionManager` vẫn 1 slot: acc 2 → `Queued` (chạy lần lượt, cùng IP máy lúc khác nhau), **không** reject vì thiếu proxy. Hàm thuần `ProxyLan.KiemTraChoLan` cũng chưa có file (đợt 1 đa-làn chưa ship). Checkbox "Bắt buộc có proxy mới chạy" ở màn Tài khoản **Shopee scrape** (`RequireProxy`, mặc định true, **mọi** acc kể cả acc 1) là việc **khác** — không phải luật acc-thứ-2.

**Việc này:** thêm cờ **thoát** trên màn Cài đặt của app. Bật → mọi acc được chạy qua IP client, **bỏ qua toàn bộ check proxy** (chỗ IP máy + trùng proxy). Tắt (mặc định) → giữ luật cũ.

Checkbox phải **lưu bền + có hàm thuần tôn trọng cờ**, để đợt 2 đa-làn (`AccountSessionManager.Start`) chỉ việc đọc setting rồi gọi. Không đợi đợt 2 mới làm UI.

## 2. Phạm vi

- **Làm:**
  - Key settings + Get/Set bool trên `SettingsRepository` (module Đơn hàng).
  - Checkbox trên màn **Cài đặt → tab Đơn hàng** (card Tự động hóa), lưu ngay khi tick.
  - Hàm thuần `ProxyLan.KiemTraChoLan` (file mới) — đủ luật a/b/c của đa-làn **cộng** cửa bỏ qua khi cờ bật.
  - Test repo + hàm thuần (kể cả thử phá).
  - Một dòng trong plan đa-làn bước 11: `KiemTraChoLan` nhận thêm cờ; đợt 2 đọc `GetChoPhepChayTatCaAccQuaIpMay()`.
- **Không làm:**
  - Không nối vào `AccountSessionManager.Start` (đợt 2 đa-làn). Trần vẫn 1: acc 2 vẫn `Queued`. Cờ bật **hôm nay không đổi** hàng đợi 1-làn — chỉ đổi khi đợt 2 gọi hàm.
  - Không đụng `RequireProxy` của scrape/search (`KiotProxyRotator` / `BraveManager`). Đó là cờ **từng tài khoản**, không phải luật acc-thứ-2.
  - Không thêm ô Proxy trên form tài khoản Đơn hàng (đã nằm đợt 1 đa-làn).
  - Không commit lẫn `AccountsViewModel.Phien.cs` (việc Flash Failed đang dở).
  - Không push / deploy.

## 3. Các bước thực hiện

1. **`orders/XuLyDonShopee.Core/Data/SettingsRepository.cs`**
   - Key: `cho_phep_chay_tat_ca_acc_qua_ip_may`.
   - `GetChoPhepChayTatCaAccQuaIpMay()`: `"true"`/`"1"` (không phân hoa thường) ⇒ true; thiếu/rỗng/lạ ⇒ **false** (mặc định TẮT — máy đang chạy không đổi hành vi).
   - `SetChoPhepChayTatCaAccQuaIpMay(bool)`: ghi `"true"`/`"false"`.
   - Copy đúng mẫu `GetSyncFreshProfile` / `SetSyncFreshProfile` (`:204-216`).

2. **`orders/XuLyDonShopee.App/ViewModels/SettingsViewModel.cs`**
   - Property `[ObservableProperty] bool ChoPhepChayTatCaAccQuaIpMay`.
   - `Reload()` nạp từ `GetChoPhepChayTatCaAccQuaIpMay()`.
   - Cờ `_loadingSettings` (như `AccountsViewModel._loadingSettings`): lúc `Reload`/ctor gán thì **không** ghi ngược; đổi tay thì `SetChoPhepChayTatCaAccQuaIpMay` **ngay** (không chờ nút Lưu chu kỳ). Xmldoc: cờ máy này, không đẩy Hub.

3. **`suite/Shopee.Suite/Modules/Settings/UnifiedSettingsView.xaml`** — tab Đơn hàng, card Tự động hóa, **sau** ô chu kỳ / **trước** nút Lưu (`:181`):
   ```xml
   <CheckBox Margin="0,14,0,0"
             IsChecked="{Binding Orders.ChoPhepChayTatCaAccQuaIpMay}"
             Content="Cho phép mọi tài khoản chạy qua IP máy này" />
   <TextBlock Style="{StaticResource caption}" Margin="0,4,0,0" TextWrapping="Wrap"
              Text="Bật: bỏ qua kiểm tra proxy — mọi acc không cấu hình proxy dùng chung IP của máy này khi chạy cùng lúc. Tắt (mặc định): chỉ một acc được dùng IP máy; acc khác phải có proxy riêng không trùng." />
   ```
   Tick lưu ngay nên **không** phụ thuộc nút Lưu chu kỳ.

4. **`orders/XuLyDonShopee.Core/Services/ProxyLan.cs`** (MỚI) — chỉ phần kiểm tra chỗ (PhanTich/GiaiAsync vẫn của đợt 1 đa-làn; nếu file đã tồn tại khi thực thi thì **thêm** overload/tham số, không đẻ file thứ hai).
   ```csharp
   public readonly record struct LanDangChay(long AccountId, string? CauHinhGoc, string? ProxyDaGiai);

   /// <param name="choPhepTatCaQuaIpMay">true = bỏ qua toàn bộ check (IP máy + trùng proxy).</param>
   public static (bool Ok, string? LyDo) KiemTraChoLan(
       bool choPhepTatCaQuaIpMay,
       IReadOnlyList<LanDangChay> dangChay,
       long accountId,
       string? cauHinhGoc,
       string? proxyDaGiai)
   ```
   Luật:
   - `choPhepTatCaQuaIpMay == true` ⇒ `(true, null)` — **không** xét danh sách làn. Đúng chữ user "bỏ qua phần check proxy".
   - Bỏ qua chính `accountId` trong `dangChay`.
   - `proxyDaGiai == null` và tồn tại làn khác `ProxyDaGiai == null` ⇒ `(false, "IP máy đang do tài khoản {X} dùng")`.
   - `proxyDaGiai != null` và tồn tại làn khác cùng `ProxyDaGiai` **hoặc** cùng `CauHinhGoc` (key Kiot, so sánh Ordinal sau Trim, bỏ qua khi `CauHinhGoc` rỗng) ⇒ `(false, "proxy trùng với tài khoản {Y}")`.
   - Còn lại `(true, null)`.
   Acc **có** proxy khi cờ bật: hàm không chặn; chỗ mở làn (đợt 2) vẫn đi proxy đã giải — cờ chỉ bỏ *check*, không lột proxy.

5. **Test**
   - `SettingsRepositoryTests`: chưa đặt → false; roundtrip true/false; mở lại DB còn nguyên. Copy 3 bài `GetSyncFreshProfile_*`.
   - `ProxyLanKiemTraChoLanTests` (mới):
     - Cờ tắt + 2 acc không proxy → false, lý do chứa id acc đang giữ.
     - Cờ tắt + 1 acc IP máy, acc kia có proxy khác → true.
     - Cờ tắt + 2 acc cùng `host:port` hoặc cùng key Kiot → false, lý do chứa id acc kia.
     - Cờ bật + 2 acc không proxy → **true**.
     - Cờ bật + 2 acc cùng proxy → **true** (bỏ qua cả trùng).
     - Acc tự xét chính nó trong `dangChay` → không tự chặn.
   - **Thử phá (bắt buộc):** sửa tạm `KiemTraChoLan` để cờ bật vẫn chạy luật IP máy → bài "cờ bật + 2 acc không proxy" phải **ĐỎ** → khôi phục → xanh.

6. **`plans/2026-09-04-da-lan-moi-acc-mot-cau-noi.md`** bước 11: chữ ký `KiemTraChoLan` thêm `bool choPhepTatCaQuaIpMay`; đợt 2 lúc `Start` truyền `_services.Settings.GetChoPhepChayTatCaAccQuaIpMay()`. Không làm đợt 2 trong plan này.

## 4. Tiêu chí nghiệm thu

- [x] `dotnet build orders/XuLyDonShopee.App/XuLyDonShopee.App.csproj` 0 lỗi 0 warning. (Suite cũng 0/0 — XAML Cài đặt nằm ở đó.)
- [x] `dotnet test orders/XuLyDonShopee.Tests` — bài mới xanh (13: repo + VM + `KiemTraChoLan`). Bộ đầy đủ **2399 passed / 3 failed**; 3 đỏ là `LenhTuHubUuTienFlashTests` vì `AccountsViewModel.Phien.cs` Flash Failed **đang dở từ trước**, không phải việc này.
- [x] Cờ mặc định false; tick lưu ngay; Reload/mở lại VM còn nguyên (`SettingsViewModelChoPhepIpMayTests` + 3 bài repo).
- [x] `KiemTraChoLan(choPhep: true, …)` Ok với 2 acc IP máy và 2 acc trùng proxy.
- [x] `KiemTraChoLan(choPhep: false, …)` từ chối IP máy / trùng host:port / trùng key Kiot.
- [x] Thử phá: bỏ cửa cờ → `CoBat_HaiAccKhongProxy_ChapNhan` ĐỎ → khôi phục → xanh.
- [x] Không đụng `AccountsViewModel.Phien.cs` (vẫn `M` sẵn việc Flash).

## 5. Rủi ro & lưu ý

- **Cờ bật hôm nay không đổi hành vi 1-làn.** Acc 2 vẫn xếp hàng. Báo cáo phải nói thẳng để user không tưởng tick xong là 10 acc cùng IP chạy song song.
- Cờ là **của máy này**, không đẩy Hub — máy khác tự tick.
- File `ProxyLan.cs` trùng tên với đợt 1 đa-làn: nếu phiên kia đã tạo file thì **sửa in-place** (thêm tham số cờ), không tạo lớp thứ hai.
- Cây đang `M` `AccountsViewModel.Phien.cs` (Flash Failed) — **không** sửa file đó trong việc này.

---

## Báo cáo thực thi (điền sau khi xong)

- **UI:** Cài đặt → tab Đơn hàng → card Tự động hóa → checkbox "Cho phép mọi tài khoản chạy qua IP máy này" (tick lưu ngay, không chờ nút Lưu chu kỳ).
- **Lưu:** `settings.cho_phep_chay_tat_ca_acc_qua_ip_may`, mặc định TẮT; của máy này, không đẩy Hub.
- **Luật:** `ProxyLan.KiemTraChoLan` — cờ bật ⇒ luôn Ok; cờ tắt ⇒ IP máy / trùng proxy như đa-làn.
- **Chưa nối runtime:** `AccountSessionManager` vẫn 1 slot, acc 2 vẫn `Queued`. Tick hôm nay **không** làm nhiều acc chạy song song trên IP máy.
- **Build:** App 0/0; Suite 0/0. SDK `8.0.424`.
- **Test:** filter bài mới 13 passed; full orders 2399 passed, 3 failed (`LenhTuHubUuTienFlashTests`) = Flash WIP trong `Phien.cs`, ngoài phạm vi.
- **Thử phá:** đã đo đỏ rồi khôi phục.
- **Chưa commit** code (user chưa bảo).
- **Sổ:** không thêm.
