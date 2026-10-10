# Plan: ĐA LÀN — mỗi tài khoản một cầu nối + một Brave riêng, N tài khoản chạy song song trên một máy

- **Ngày:** 2026-09-04
- **Trạng thái:** đang làm — **Đợt 1** (hạ tầng làn + proxy, chưa đổi hành vi)
- **Người lập:** Fable · **Người thực thi:** `opus-dev`
- **Căn cứ khảo sát:** bản đồ 6 mảng (Core · Extension · App · Suite+Hub · Test · Sổ/plan cũ) lưu tại
  scratchpad `ban-do-da-lan.md` của phiên 04/09 — mọi `file:dòng` dưới đây lấy từ đó và đã kiểm lại tay các điểm lệch.

## 1. Bối cảnh & mục tiêu

**Hiện trạng — MỘT LÀN.** App chỉ có một cầu nối WebSocket (`OrdersBridgeChannel.BridgePort = 47821`, hằng), một
Brave, một bộ cờ "đang chạy lát cắt" cho cả máy. `AccountSessionManager` cho đúng **một** tài khoản chiếm slot;
tài khoản thứ hai nằm `Queued` mãi vì phiên nghỉ giữa hai vòng vẫn giữ `State = Running`
(`AccountSession.cs:678`; sổ **P-20260901-5**). Nhịp flash chỉ mở gói khi **mọi** phiên đang nghỉ, nên ≥3 tài
khoản tuần tự thì flash im cả ngày.

**Yêu cầu của user (04/09):** sắp chạy **~10 tài khoản trên 3 máy**. Xếp hàng tuần tự thì quá chậm. Mỗi tài
khoản đã là một cookie/hồ sơ riêng (`profiles/{accountId}-{kind}`, `BrowserProfilePaths.cs:16`) nên phải
**chạy song song**: mỗi tài khoản một làn (cổng riêng · kênh riêng · Brave riêng · bản chép extension riêng),
mọi flow (check đơn · KM · Combo · Flash) đi theo tài khoản, ưu tiên flash cũng theo tài khoản.

**Luật proxy (user chốt 04/09, ba lượt trao đổi):**
1. Trên một máy chỉ có **một chỗ "IP máy"**. Tài khoản không cấu hình proxy chiếm chỗ đó; tài khoản không proxy
   thứ hai muốn chạy **cùng lúc** thì bị từ chối, log rõ *"IP máy đang do tài khoản X dùng"*.
2. Tài khoản muốn chạy song song **phải có proxy riêng**, không trùng làn nào đang chạy (trùng key KiotProxy hoặc
   trùng `host:port` ⇒ từ chối, log *"proxy trùng với tài khoản Y"*).
3. Tài khoản **có** proxy **luôn đi qua proxy của nó**, kể cả khi là tài khoản duy nhất đang chạy (giữ IP ổn định
   cho từng tài khoản; tránh ca proxy-acc chiếm nhầm chỗ IP máy rồi acc không-proxy khởi động sau bị từ chối oan).
4. Chạy **một** tài khoản đơn lẻ không proxy: chấp nhận như hiện nay.

**Hiện trạng proxy trong code:** proxy runtime của module orders đã bị GỠ ở `b2310c5`/`22f5ebc`
(`AppServices.cs:498`, `BraveLaunchArgs.cs:160`), hai test đang ghim "không `--proxy-server`"
(`BraveCleanPocArgsTests.cs:25`, `BraveLaunchArgsTests.cs:119` — bài sau thuộc đường `BuildBraveArgs` khác, GIỮ).
`KiotProxyClient` còn sống ở `suite/Shopee.Core/Proxy/` (11 nơi dùng) nhưng orders **không tham chiếu được**
(chiều tham chiếu là suite → orders; orders Core chỉ tham chiếu `shared/Shopee.Toolkit`). Cách cũ gắn proxy đã
chạy được: `--proxy-server=<addr>` **không** user/pass (Kiot cấp proxy whitelist IP).

### Quyết định kiến trúc (phiên chính chốt sau khảo sát — không hỏi lại)

| # | Câu hỏi | Chốt | Lý do |
|---|---|---|---|
| Q1 | Lát cắt KM/Combo/Flash per-acc hay 1 lát cắt/máy? | **Per-acc** (đợt 2) | user: "flow đi theo acc"; 1 lát cắt/máy thì flash 4 acc lại xếp hàng |
| Q2 | Ưu tiên flash per-acc hay per-máy? | **Per-acc** | flash của A không chặn run/run-km của B |
| Q3 | `run` do trả-khung xuyên máy có cắt gói flash của acc đó? Check đơn acc A có nhường khung KM cho acc B cùng máy? | **Giữ hành vi cũ** (VỪA-5 đợt hub) · **không nhường chéo acc** | chưa có ca thật; per-acc hoá đúng nghĩa là "chỉ acc của chính phiên" |
| Q4 | Acc thứ N+1 | **Queued FIFO** như nay; phiên đang nghỉ **không** nhả làn | trần N=4 ≥ số acc/máy dự kiến (10/3); "nhả làn khi nghỉ" là việc khác hẳn |
| Q5 | Cấp cổng | **Dải cố định** `47822 … 47822+31`, bind thử từng cổng, **không làn nào nhận 47821** | `HttpListener` không nhận cổng 0; instance Workspace không dựng module Đơn hàng (`ShellViewModel.cs:201`) nên chỉ một instance chạy orders/máy; 47821 bỏ trống để gói `wake` lạc không nối vào ai |
| Q6 | Nhịp lịch 60s | mỗi acc một nhịp phóng nền, nhiều acc mở gói trong một tick; **không** trần riêng ngoài trần làn | đợt 2 |
| Q7 | Hub | **Đợt 3** (heartbeat số làn, tách Queued khỏi Running, mirror lát cắt per-acc) | đợt 1–2 không cần: hub đã khoá theo (máy, login) |
| Q8 | Trình tự với đợt hub-điều-khiển chưa commit | **commit đợt hub trước**, đa làn xây trên HEAD sạch | cùng đụng `Phien.cs`/`Mirror.cs`/`HubDtos.cs` |
| Q9 | Trần N mặc định | **4**, cấu hình 1–8 (đợt 2); **đo thật N=2** trước khi bật rộng | chưa có số đo nào về 2 Brave song song |
| — | Đưa cổng vào extension | **nướng `lane.js` vào bản chép** (theo R2), content.js gửi `wsPort: null` khi không hash; **không** sed chuỗi | roadmap 07-23:99-100 đã chốt "bake vào copy"; sed trượt vì 47821 nằm cả trong chú thích |
| — | Thư mục bản chép per-làn | `%TEMP%\shopee-orders-bridge\L{accountId}\shopee-orders-<guid>` | dọn bản cũ chỉ trong `L{accountId}` của mình |
| — | Nguồn proxy | **ô mới trên tài khoản orders** (`Account.Proxy`): key KiotProxy hoặc `host:port` / `http://…` / `socks5://…` | tài khoản orders và tài khoản suite không nối nhau; pool key chung của orders **không** dùng (không đoán key nào của acc nào) |
| — | `KiotProxyClient` | **chuyển file** sang `shared/Shopee.Toolkit/Proxy/`, **giữ nguyên namespace `Shopee.Core.Proxy`** | 11 nơi ở suite không phải sửa; orders Core dùng được |
| — | Proxy có user/pass | **không hỗ trợ đợt này** — cấu hình có `user:pass@` ⇒ từ chối mở làn, log rõ | Chromium không nhận creds trong `--proxy-server`; xác thực qua extension là việc riêng |
| — | Proxy không giải được (Kiot lỗi / chết) | **từ chối mở làn**, KHÔNG âm thầm rơi về IP máy | rơi về IP máy là vi phạm luật 1–2 mà không ai biết |
| — | Đổi IP Kiot giữa chừng | **không theo dõi/relaunch** trong đợt này; giải proxy một lần lúc mở làn và mỗi lần relaunch | relaunch giữa lát cắt là phá gói; ghi sổ nếu thấy ca thật |

### Lộ trình 3 đợt (mỗi đợt một vòng 5 chặng riêng)

| Đợt | Nội dung | Hành vi người dùng thấy |
|---|---|---|
| **1 (plan này)** | Hạ tầng làn: cổng per-làn · `lane.js` · launcher không giết chéo · bản chép per-làn · proxy per-acc + luật IP máy (hàm thuần) · `LanCauNoi` cấp cổng | **Không đổi**: vẫn 1 acc/máy; chỉ khác cổng 47822 thay 47821 và có ô Proxy trên form tài khoản |
| 2 | `AccountSessionManager` trần N · per-acc hoá toàn bộ cờ/lát cắt/nhịp/khe trong `AccountsViewModel.Phien.cs` · áp luật proxy lúc `Start` · UI "Chờ làn" | N acc chạy song song |
| 3 | Hub: heartbeat `OrdersLanes/OrdersLanesBusy`, tách Queued khỏi Running, mirror `LatCatDangChay` per-acc (P-20260904-12) | Hub thấy đúng k/N làn |

Đợt 1 ship được độc lập và **an toàn để chạy production** vì trần vẫn 1; nó tách rủi ro "lệnh chéo tài khoản
không lỗi không log" (extension bám nhầm cổng) ra khỏi đợt đổi state machine.

## 2. Phạm vi ĐỢT 1

- **Làm:** mục 3 bước 1–14 dưới đây (Core · Extension · Proxy · App-wiring tối thiểu · test · tài liệu).
- **Không làm (đợt sau hoặc không bao giờ):** đổi trần làn / hàng đợi trong `AccountSessionManager` (đợt 2);
  per-acc hoá `_bridgeRunning`, `_nhuongChoCheckDon`, `_dangChay*TuDong`, khe nghỉ, `TraHoSo`, `MuonHoSoAsync`
  (đợt 2); hub/heartbeat (đợt 3); "nhả làn khi nghỉ"; proxy user/pass; watchdog đổi IP Kiot; pool key chung;
  đổi `FlashChay`/22:15/30'/75'/120' hay bất kỳ hằng lịch nào; đổi `BraveJobObject startMinimized`; tách
  `machine.json` cho instance test.

## 3. Các bước thực hiện (ĐỢT 1)

Thứ tự bắt buộc: **Core + extension trước (1–8), proxy (9–11), rồi mới nối vào App (12–13)**. Lý do: ba lỗi
"giết chéo làn" (`Launcher:32`), "xoá bản chép làn khác" (`Launcher:48`) và "extension bám cổng lạ" phải chết
trước khi bất kỳ chỗ nào mở được làn thứ hai — chốt 25/07 (plan `2026-07-25-don1:31,:35`).

### Core — `orders/XuLyDonShopee.Core/Services/`

1. **`OrdersBridgeChannel.cs`**
   - `:145` `public const int BridgePort = 47821` → đổi tên **`BridgePortBase`** (giá trị giữ 47821 — là gốc dải,
     KHÔNG cấp cho làn nào). Thêm hằng `public const int SoCongTrongDai = 32` (làn dùng `BridgePortBase+1 …
     BridgePortBase+SoCongTrongDai`).
   - `:492` `Start(int port = BridgePort, …)` → **bỏ default param**: `Start(int port, TimeSpan? nhipGiuSong = null,
     TimeSpan? choNoiLai = null)`. Compile lỗi ở mọi chỗ gọi `Start()` trần — đó là chủ ý. Giữ retry 5×400ms trên
     đúng cổng đó (ca "phiên trước chưa nhả").
   - Thêm `public int Port { get; private set; }` (gán khi mở được). Câu ném `:502` giữ nội dung.
   - **Kiểm `ready` theo làn:** `OnMessage` khi nhận `{action:"ready", lane, port}`: nếu `lane` có giá trị và khác
     `LaneId` mà kênh được cấu hình (`public string? LaneId { get; set; }`, App gán = accountId dạng chuỗi) ⇒ log
     `"Cầu nối: extension của làn {lane} nối nhầm cổng {Port} (làn này là {LaneId}) — đóng"` và đóng socket đó
     (`WebSocketServer` có `Dispose` socket hiện tại? nếu không có API đóng riêng socket thì thêm
     `internal void DongSocketHienTai()` ở `shared/Shopee.Toolkit/Ws/WebSocketServer.cs`). Gói `ready` không có
     `lane` (extension cũ) ⇒ nhận như trước.
2. **`OrdersBridgeSession.cs`**
   - Ctor `:573`: thêm tham số **bắt buộc** thứ hai `LanCauNoiSpec lan` (record mới trong Core:
     `public sealed record LanCauNoiSpec(long AccountId, int Cong, string? ProxyServer)` — `ProxyServer` đã giải
     xong, dạng `http://host:port` / `socks5://host:port`, `null` = IP máy). Mọi chỗ `new OrdersBridgeSession(`
     phải truyền (compile bắt: `AccountSession.cs:513`, `Phien.cs:3733`, mọi test).
   - `:622-629`: `_channel.LaneId = lan.AccountId.ToString(); _channel.Start(lan.Cong);` log dùng `_channel.Port`;
     `startUrl = $"{baseUrl}#_od_ws={_channel.Port}"` (hash giữ làm đường phụ/chẩn đoán).
   - Gọi `OrdersBridgeLauncher.Launch(_userDataDir, startUrl, _log, lan)`.
   - Xmldoc `:496` "chưa đa-lane" → sửa lý do.
3. **`OrdersBridgeLauncher.cs`**
   - Chữ ký `Launch(string userDataDir, string startUrl, Action<string>? log, LanCauNoiSpec lan)`.
   - `:32` `FreeProfile(userDataDir, alsoMatchBridgeExtension: **false**, log)` — chỉ giết Brave của **hồ sơ này**.
     Sửa xmldoc `:10-13`, `:29-31`: tiền đề "orphan cùng nối cổng cố định 47821 cướp lệnh" không còn vì mỗi làn
     một cổng và kênh từ chối `ready` sai làn.
   - `PrepareFreshExtensionCopy(srcDir)` → `internal static string ChuanBiBanChepExtension(string srcDir,
     LanCauNoiSpec lan)`: gốc `%TEMP%\shopee-orders-bridge\L{AccountId}\`; **chỉ** xoá các thư mục con trong gốc
     đó (bản cũ của chính làn này); chép sang `shopee-orders-<guid>`; **ghi `lane.js`** vào bản chép, nội dung
     đúng một dòng:
     `export const LANE = Object.freeze({ port: 47823, accountId: "12" });`
     (cổng số, accountId chuỗi). Ghi bằng `File.WriteAllText(..., new UTF8Encoding(false))`.
   - `BuildCleanPocArgs(userDataDir, extPath, startUrl, lan.ProxyServer)` (bước 10).
4. **`BrowserProfileGuard.cs`** — giữ chữ ký `FreeProfile(userDataDir, alsoMatchBridgeExtension, log)` và hàm
   thuần `BuildProcessFilter` (test `:22-30` là chuẩn mới, `:32-44` vẫn đúng với hàm thuần — **giữ cả hai**,
   chỉ sửa xmldoc `:33-36` nói rõ cờ `true` không còn ai dùng ở đường cầu nối). Không đổi hành vi hàm.

### Extension — `extensions/shopee-orders/` (+ nguồn `extensions/shared/`)

5. **`lane.js` (mới, trong repo)**: `export const LANE = Object.freeze({ port: null, accountId: null });` kèm chú
   thích: *"C# ghi đè file này vào bản chép lúc mở làn; bản trong repo cố ý `null` để dev nạp tay fail-closed và
   để `ExtensionJsCuPhapTests.MoiImportTuongDoi_PhaiTroToiFileCoThat` xanh."*
6. **`core.js`**: `:8` → `import { LANE } from "./lane.js"; export const DEFAULT_PORT = LANE.port;` (có thể `null`).
   `createWsBridge({ defaultPort: DEFAULT_PORT, khoaCong: true, … })`. Gói `ready` `:49` →
   `{ action: "ready", lane: LANE.accountId, port: LANE.port }`.
7. **`content.js`**: `:17` bỏ `DEFAULT_PORT`; `:19` `let port = null;` chỉ gán khi có hash `_od_ws`; `wake` `:29` và
   gói keepalive `:55/:61` gửi `wsPort: port` (có thể `null`). Sửa chú thích `:2-3`.
8. **`background.js`**: `:138` `bridge.connect(msg.wsPort || undefined)`; `:139` chỉ `storage.session.set({wsPort})`
   khi `bridge.port` truthy; `:150` đã đúng khuôn; `noiLaiTuStorage` `:189-192` giữ.
   **`extensions/shared/ws-bridge.js`** (SỬA Ở NGUỒN rồi chạy `extensions/sync-shared.cmd`; `--check` phải xanh):
   - option mới `khoaCong` (mặc định `false` — shopee-search/9111 không đổi): khi `true` và đã có `port` mà
     `nextPort && nextPort !== port` ⇒ **bỏ qua** cổng lạ, gọi `onProgress`/log `"bỏ qua cổng lạ {nextPort}, làn giữ
     {port}"`, return.
   - `if (!targetPort) { log "không có cổng làn (lane.js chưa được ghi?)"; return; }` **trước** `:112` — không còn
     `ws://localhost:undefined` quay vòng 1200ms.

### Proxy per tài khoản

9. **Chuyển `KiotProxyClient.cs`** từ `suite/Shopee.Core/Proxy/` sang `shared/Shopee.Toolkit/Proxy/`
   (`git mv`), **giữ `namespace Shopee.Core.Proxy;`**, thêm `using System.Text.Json;` `using System.Net.Http;` ở đầu
   file (Toolkit không có global usings của Shopee.Core). Build suite + orders đều phải xanh, 11 file dùng không đổi.
10. **`BraveLaunchArgs.BuildCleanPocArgs(userDataDir, extensionPath, startUrl, string? proxyServer = null)`**: khi
    `proxyServer` không rỗng ⇒ `.Add($"--proxy-server={proxyServer}")` **trước** `.LoadExtension`. Sửa xmldoc
    `:153-156, :160`. Test `BraveCleanPocArgsTests.KhongCo_ProxyServer` → đổi thành hai bài: không truyền ⇒ không
    có; truyền ⇒ có đúng một `--proxy-server=…`. `BraveLaunchArgsTests.KhongBaoGioCoProxyServer` (đường
    `BuildBraveArgs`) **giữ nguyên**.
11. **Proxy trên tài khoản + giải proxy + luật IP máy** (Core):
    - `Models/Account.cs`: thêm `public string? Proxy { get; set; }` (KHÔNG tái dụng `ProxyKey` — cột đó chứa giá
      trị đã migrate vào pool, để nguyên). `Data/Database.cs`: cột `Proxy TEXT` (CREATE + `EnsureColumn` như `:258`).
      `AccountRepository` SELECT/INSERT/UPDATE/Map thêm cột.
    - `Services/ProxyLan.cs` (mới):
      - `public static ProxyLanCauHinh? PhanTich(string? cauHinh)` — thuần: rỗng ⇒ `null` (IP máy);
        `http://h:p` / `socks5://h:p` / `h:p` (⇒ `http://h:p`) ⇒ `Manual(diaChi)`; chứa `@` ⇒ ném
        `ProxyKhongHoTro("proxy có user/pass chưa hỗ trợ")`; còn lại (không có `:`) ⇒ `Kiot(key)`.
      - `public static async Task<string?> GiaiAsync(string? cauHinh, Func<string, CancellationToken,
        Task<KiotResult>> layHienTai, Func<string, CancellationToken, Task<KiotResult>> layMoi, CancellationToken ct)`
        — `null` ⇒ `null`; Manual ⇒ địa chỉ; Kiot ⇒ `layHienTai(key)`, lỗi/rỗng ⇒ `layMoi(key)`, vẫn lỗi ⇒ ném
        `ProxyKhongSanSang(lyDo)`. Production rót `KiotProxyClient.FetchCurrentAsync/FetchNewAsync`; địa chỉ qua
        `KiotProxyClient.BuildProxyServer`-tương-đương (Kiot trả `Proxy` dạng `host:port` ⇒ thêm `http://`).
      - `public static (bool Ok, string? LyDo) KiemTraChoLan(bool choPhepTatCaQuaIpMay,
        IReadOnlyList<LanDangChay> dangChay, long accountId, string? cauHinhGoc, string? proxyDaGiai)` với
        `record LanDangChay(long AccountId, string? CauHinhGoc, string? ProxyDaGiai)`.
        `choPhepTatCaQuaIpMay == true` ⇒ `(true, null)` ngay (cờ Cài đặt
        `GetChoPhepChayTatCaAccQuaIpMay()`, plan `2026-09-04-cho-phep-tat-ca-acc-qua-ip-may.md`).
        Còn lại: (a) `proxyDaGiai == null` và tồn tại làn khác `ProxyDaGiai == null` ⇒
        `(false, "IP máy đang do tài khoản {X} dùng")`; (b) `proxyDaGiai != null` và tồn tại làn khác có
        `ProxyDaGiai == proxyDaGiai` **hoặc** `CauHinhGoc` (key Kiot) trùng ⇒ `(false, "proxy trùng với tài khoản
        {Y}")`; (c) còn lại `(true, null)`. Bỏ qua chính `accountId` trong `dangChay`.
      Đợt 1 **chỉ viết + test** `KiemTraChoLan` (đã có từ plan checkbox IP máy). Đợt 2 lúc `Start` truyền
      `_services.Settings.GetChoPhepChayTatCaAccQuaIpMay()`.
    - Form tài khoản: `AccountsView.xaml` thêm ô "Proxy (key KiotProxy hoặc host:port; trống = IP máy)" ngay dưới
      ô `EditVerifyEmail` (`:571`); `AccountsViewModel.Form.cs` thêm `EditProxy`, nạp/ghi ở hai nhánh `:137-160`
      (**trim**; rỗng ⇒ `null`). Khi lưu: `ProxyLan.PhanTich` ném ⇒ `ErrorMessage` và không lưu.

### App — nối tối thiểu (trần vẫn 1)

12. **`orders/XuLyDonShopee.App/Services/LanCauNoi.cs` (mới)** — bộ cấp làn trong process:
    - `public sealed class LanCauNoi { long AccountId; int Cong; string? CauHinhProxy; string? ProxyServer; }`
    - `public sealed class BoCapLan` (một instance trong `AppServices`): `LanCauNoi LayHoacCap(long accountId)` —
      đã có ⇒ trả; chưa ⇒ cấp cổng **nhỏ nhất chưa cấp** trong `[BridgePortBase+1, BridgePortBase+SoCongTrongDai]`
      (cổng gắn với acc suốt đời process; không bind ở đây — bind là việc của `Channel.Start`). `IReadOnlyList<LanCauNoi>
      DangChay()` (đợt 2 dùng). Hết dải ⇒ ném `InvalidOperationException("hết cổng làn")`.
    - **Bind thất bại** (cổng bị tiến trình lạ chiếm): `Channel.Start` ném sau 5×400ms ⇒ `AccountSession` gọi
      `BoCapLan.DoiCong(accountId)` (cấp cổng kế tiếp còn trống) và thử lại **tối đa 3 lần**, mỗi lần một dòng log
      `"Cầu nối: cổng {cũ} bận — đổi sang {mới}"`. Quá 3 ⇒ lỗi phiên như hiện nay.
13. **Nối vào hai đường mở phiên** (không đổi hành vi khác):
    - `AccountSession.cs:513`: trước `new OrdersBridgeSession(...)`: `var lan = _services.BoCapLan.LayHoacCap(_accountId);`
      giải proxy: `lan.ProxyServer = await ProxyLan.GiaiAsync(acc.Proxy, …, ct)` (ném ⇒ `SetStatus(Error,
      "Proxy không sẵn sàng: {lý do}")`, phiên dừng, **không** rơi về IP máy); truyền `new LanCauNoiSpec(_accountId,
      lan.Cong, lan.ProxyServer)`. Log một dòng lúc mở: `"Làn: cổng {Cong} · {IP máy | proxy http://…}"`.
    - `Phien.cs:3733` `TaoPhienCauNoi`: cùng công thức (cùng `LayHoacCap(accountId)`, cùng giải proxy) — lát cắt và
      check đơn **cùng acc dùng cùng làn**. `TaoPhienCauNoi` đang đồng bộ ⇒ giải proxy trước ở chỗ gọi (async) hoặc
      đổi thành `TaoPhienCauNoiAsync`; chọn cách ít lan toả nhất, nêu trong báo cáo.
    - `AppServices`: thêm `public BoCapLan BoCapLan { get; }`.
14. **Tài liệu cùng đợt**: `orders/CLAUDE.md:48`, `AppServices.cs:466-467`, `HubDtos.cs:586`,
    `OrdersModuleHost.Mirror.cs:213` — câu "cùng cổng 47821" đổi lý do (đa làn đợt 1: mỗi làn một cổng; đợt 2 mới
    mở N). `CHANGELOG.md` mục Unreleased: "Cầu nối chuyển sang cổng theo làn (47822+), ô Proxy trên tài khoản".
    `SO-TAY.md` **P-20260901-5** vẫn `MỞ` (đóng ở đợt 2), thêm dòng "đợt 1 đã xong hạ tầng".

### Test (mới / sửa) — mỗi bài mới phải **THỬ PHÁ**

| File | Bài | Đo gì |
|---|---|---|
| `OrdersBridgeLauncherTests.cs` (mới bài) | `ChuanBiBanChep_GhiLaneJs_DungCongVaAccountId` | đọc `lane.js` trong bản chép: đúng `port: <n>` số, `accountId: "<id>"` chuỗi |
| — | `ChuanBiBanChep_LanB_KhongXoaBanChepLanA` | chuẩn bị L1 rồi L2 ⇒ thư mục L1 còn nguyên, `lane.js` L1 không đổi |
| — | `ChuanBiBanChep_CungLan_XoaBanCu` | chuẩn bị L1 hai lần ⇒ chỉ còn 1 thư mục con trong `L1` |
| `ThuLaiShopRoiViCauNoiTests.cs` (khuôn đọc JS `:124-136`) | `Extension_KhongConLiteral47821` | `content.js`, `core.js`, `background.js` không chứa `47821` (chú thích được phép nhắc `BridgePortBase`); `core.js` có `from "./lane.js"`; `lane.js` repo có `port: null` |
| `BridgeTestRig.cs` + bài mới `HaiLanSongSongTests.cs` | `HaiKenhHaiCong_GoiKhongLotSangNhau` | 2 channel 2 cổng cùng process, extension giả A gửi vào cổng A ⇒ chỉ A nhận; B không thấy gì |
| — | `Ready_SaiLan_BiDongVaGhiLog` | extension giả gửi `ready{lane:"99"}` vào kênh `LaneId="1"` ⇒ socket bị đóng, log có "nối nhầm cổng" |
| — | `Ready_KhongCoLane_VanNhan` | extension cũ (không `lane`) ⇒ nhận bình thường |
| `BraveCleanPocArgsTests.cs` | `KhongTruyenProxy_ThiKhongCoProxyServer` · `TruyenProxy_ThiCoDungMotProxyServer` | như bước 10 |
| `ProxyLanTests.cs` (mới) | `PhanTich_*` (Theory ≥6 ca: rỗng, `h:p`, `http://`, `socks5://`, key, `user:pass@` ném) | thuần |
| — | `Giai_Kiot_HienTaiLoi_ThiLayMoi` · `Giai_Kiot_CaHaiLoi_ThiNem` · `Giai_Manual_KhongGoiKiot` | fake hai delegate |
| — | `KiemTraChoLan_MaTran` (Theory: 5 dòng bảng luật + trùng key Kiot + bỏ qua chính mình) | thuần |
| `LanCauNoiTests.cs` (mới) | `CapCong_KhacNhau_KhongBaoGio47821` · `LayHoacCap_CungAcc_CungCong` · `DoiCong_LayCongKeConTrong` · `HetDai_Nem` | thuần |
| `AccountRepositoryTests` (nếu có) / `DatabaseTests` | `Proxy_LuuVaDocLai` · `EnsureColumn_Proxy_TrenDbCu` | DB tạm |
| Giữ xanh | `OrdersBridgeSsoTests.CongCauNoi_MoLanHai_Nem` (`:196-212`), `AccountSessionManagerTests` (trần 1 chưa đổi), `ExtensionJsCuPhapTests.MoiImportTuongDoi_PhaiTroToiFileCoThat`, `BrowserProfileGuardTests` cả hai bài, `KhongLuuConTroTienDoNaoXuongDb` | không sửa kỳ vọng |
| `extensions/sync-shared.cmd --check` | — | xanh sau khi sửa ws-bridge ở nguồn |

## 4. Tiêu chí nghiệm thu ĐỢT 1 (đo được)

- [ ] **N1** Build `suite/ShopeeSuite.sln` và `server/ShopeeHub.sln` (Hub không đổi nhưng phải build vì
      `KiotProxyClient` đổi assembly): **0 warning, 0 error**.
- [ ] **N2** `grep -rn "Start()" orders/XuLyDonShopee.Core orders/XuLyDonShopee.App --include=*.cs | grep -i channel`
      = 0 dòng; `grep -rn "BridgePort\b"` (không `Base`) = 0 dòng ngoài CHANGELOG/plans/SO-TAY.
- [ ] **N3** `grep -n 47821 extensions/shopee-orders/content.js core.js background.js` = 0; `lane.js` tồn tại trong
      repo với `port: null`; `extensions/sync-shared.cmd --check` exit 0.
- [ ] **N4** `OrdersBridgeLauncher.Launch` gọi `FreeProfile(..., alsoMatchBridgeExtension: false, ...)` (đọc code,
      trích dòng) và `ChuanBiBanChepExtension` chỉ xoá trong `L{accountId}` (3 bài test mục 3 xanh + đã thử phá).
- [ ] **N5** Bộ test orders (`--filter "Loai!=Dai" --blame-hang-timeout 8m`) xanh: số cũ (2370 lượt hub) + số bài
      mới, **0 fail**; Core, Suite, Search, Hub xanh. Ghi rõ đo ở worktree nào.
- [ ] **N6** Mỗi bài test mới có bằng chứng thử phá (đột biến → ĐỎ → khôi phục) trong báo cáo, ít nhất: lane.js
      sai cổng; L2 xoá L1; `ready` sai làn không đóng; `KiemTraChoLan` đảo luật (a) và (b); `PhanTich` nhận `@`;
      `--proxy-server` không thêm.
- [ ] **N7 (chạy thật, trần 1, một acc)** — sau khi đợt hub đã deploy và app được build vào bin ở khe an toàn:
      mở phiên check đơn một tài khoản ⇒ log có `"Làn: cổng 4782x · IP máy"`, `netstat -ano | findstr 4782` thấy
      đúng **một** listener ở cổng đó và **không** có 47821; dòng lệnh Brave (`Get-CimInstance Win32_Process`)
      có `--load-extension` trỏ `…\shopee-orders-bridge\L{id}\shopee-orders-…` và **không** có `--proxy-server`;
      log C# thấy `ready` (kèm lane = id); vòng check đơn chạy trọn ít nhất 1 shop và **một gói flash/KM** (tuỳ khe)
      chạy được như trước.
- [ ] **N8 (chạy thật, proxy — CHỈ khi user cung cấp một proxy)**: điền proxy vào một tài khoản ⇒ Brave có
      `--proxy-server=…`, Seller Centre nạp được, log `"Làn: cổng … · proxy http://…"`. Không có proxy để thử ⇒ ghi
      "CHƯA ĐO — chờ proxy", **không** chấm ĐẠT.
- [ ] **N9** Form tài khoản: nhập `user:pass@host:port` ⇒ báo lỗi, không lưu; nhập `1.2.3.4:8080` ⇒ lưu, đọc lại
      đúng (UIA hoặc test VM).
- [ ] **N10** Không hành vi nào khác đổi: `AccountSessionManagerTests` nguyên kỳ vọng; acc thứ hai vẫn `Queued`
      (đợt 2 mới mở).

## 5. Rủi ro & lưu ý

- **Lệnh chéo tài khoản không lỗi không log** là rủi ro số một của đa làn (extension bám nhầm cổng ⇒ thao tác THẬT
  trên Seller Centre acc khác). Đợt 1 dựng **ba lớp**: `lane.js` nướng cổng · `khoaCong` bỏ qua cổng lạ · kênh
  từ chối `ready` sai làn. Không được bỏ lớp nào "cho gọn".
- `shared/ws-bridge.js` là bản copy — sửa nguồn `extensions/shared/`, chạy `sync-shared.cmd`; giữ mặc định
  `khoaCong=false` để shopee-search (9111) không đổi.
- Test không được phóng Brave thật; bài `HaiLanSongSongTests` dùng rig hai cổng **ngoài dải 47822–47853** để không
  va app production đang chạy trên máy dev (dùng cổng rig hiện có + 1).
- Đổi ctor `OrdersBridgeSession` là lan toả compile lớn (test stub) — chấp nhận; **không** thêm overload giữ chữ ký
  cũ (sẽ có người gọi và mở làn không cổng).
- `KiotProxyClient` dùng `HttpClient` tĩnh timeout 12s: giải proxy lúc mở phiên có thể chậm 12–24s khi Kiot lỗi —
  nằm trong `Opening`, chấp nhận; log trước khi gọi.
- Máy dev = máy production của user: **không** build vào bin / chạy instance thứ hai khi app đang chạy; chặng 2–4
  chỉ đo trong worktree; N7 làm ở khe do user duyệt (sau khi vòng check đơn xong). Memory
  `tu-chay-app-va-doc-log.md`: không foreground/click app trong lát cắt; Brave minimized ⇒ gói hỏng câm
  (P-20260904-16).
- Sau đợt 1 số cổng đổi 47821 → 47822+: tài liệu vận hành nào nhắc 47821 (firewall nội bộ? không có — loopback)
  chỉ cần sửa CHANGELOG.

---

## Báo cáo thực thi (opus-dev điền)

<để trống>
