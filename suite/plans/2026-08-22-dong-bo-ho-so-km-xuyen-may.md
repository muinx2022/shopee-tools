# Plan: Đồng bộ hồ sơ KM + bắt tay check đơn XUYÊN MÁY

- **Ngày:** 2026-08-22
- **Trạng thái:** pha A xong (vòng 1 + vòng 2 + vòng 3 sửa sau phản biện) — chờ nghiệm thu/phản biện vòng 3; pha B–D CHƯA làm
- **Người lập:** phiên chính · **Người thực thi:** `opus-dev`

## 1. Bối cảnh & mục tiêu

### 1.1 Hiện trạng — ba chỗ hỏng, đã đọc code xác nhận

Tính năng "KM của Shop" (plan `2026-08-18-km-cua-shop.md`) chạy đúng **trên một máy**. Nhiều máy thì gãy ở ba chỗ:

**(a) Cấu hình KM không đồng bộ.** Bảng `km_config` nằm trong SQLite **cục bộ từng máy** (`%AppData%`,
[Database.cs:41](../orders/XuLyDonShopee.Core/Data/Database.cs:41), [KmConfigRepository.cs](../orders/XuLyDonShopee.Core/Data/KmConfigRepository.cs)),
không có đường nào lên hub. Cờ nhường khung đọc từ đó:
`_services.KmConfig.Get(_accountId).TuDong` ([AccountSession.cs:640](../orders/XuLyDonShopee.App/Services/AccountSession.cs:640)).
⇒ Bật "Chạy tự động" ở máy A thì máy B có `TuDong = false` → **B không nhường khung 2–6h**, cứ đặt vòng check đơn mới.

**(b) Nhường khung KHÔNG nhả khoá — chỗ nặng nhất, và không sửa được bằng riêng việc đồng bộ.**
`HoanVongCheckDon` ([KmLichTuDong.cs:125](../orders/XuLyDonShopee.Core/Services/KmLichTuDong.cs:125)) chỉ đẩy **mốc vòng kế**
ra sau 06:00. Nhưng lease `orders:<login>` xin **một lần trước vòng lặp**
([AccountSession.cs:467](../orders/XuLyDonShopee.App/Services/AccountSession.cs:467)) và chỉ nhả ở `finally` khi phiên
dừng hẳn; kỳ nghỉ nằm **trong** vòng lặp, heartbeat 60s vẫn chạy
([OrdersModuleHost.AccountLease.cs:22](../suite/Shopee.Suite/Infrastructure/OrdersModuleHost.AccountLease.cs:22)).
⇒ Máy B "nhường khung" mà **vẫn ôm khoá tới 06:00** → máy A vẫn không xin được.

**(c) Không có bắt tay xuyên máy.** `MuonHoSoAsync`
([AccountsViewModel.Phien.cs:393](../orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs:393)) chỉ dừng phiên
**trên chính máy nó**; hub `ReserveAccounts` ([HubDatabase.Leases.cs:118](../server/Shopee.Hub.Web/Data/HubDatabase.Leases.cs:118))
**từ chối** chứ không cướp (khoá tài khoản không có cờ `Force`). Lượt tự động bị từ chối thì đã kịp ghi
`LanThuTuDong` ([Phien.cs:232](../orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs:232)) ⇒
`DaThuTrongKhungNay` chặn nốt đêm đó ⇒ **mất trắng một đêm**, đêm sau lặp lại y hệt.
Chiều ngược cũng hỏng: máy B khởi động phiên trong lúc A chạy KM thì bị từ chối → `return` → `finally` đặt
`Stopped`, **không xếp hàng, không thử lại** ([AccountSession.cs:462](../orders/XuLyDonShopee.App/Services/AccountSession.cs:462)).

### 1.2 Hạ tầng ĐÃ CÓ, dùng lại — không đẻ cơ chế mới

| Có sẵn | Ở đâu | Dùng cho việc này thế nào |
|---|---|---|
| Gương danh bạ client→hub (đẩy 3s khi bẩn / 60s khi rảnh) | [OrdersModuleHost.Mirror.cs](../suite/Shopee.Suite/Infrastructure/OrdersModuleHost.Mirror.cs) | **Khuôn** cho worker đẩy hồ sơ KM |
| Cấu hình dùng chung kéo về mỗi 60s + TTL | [OrdersModuleHost.GsheetConfig.cs](../suite/Shopee.Suite/Infrastructure/OrdersModuleHost.GsheetConfig.cs), `HubOrdersConfig` | **Khuôn** cho chiều kéo hồ sơ KM |
| Kênh lệnh hub→client `run`/`stop`, đi kèm heartbeat 12s, có ack + sweep 5' | [OrdersModuleHost.Mirror.cs:212](../suite/Shopee.Suite/Infrastructure/OrdersModuleHost.Mirror.cs:212), [HubDatabase.OrdersAccounts.cs:371](../server/Shopee.Hub.Web/Data/HubDatabase.OrdersAccounts.cs:371) | **Dùng thẳng** để cưỡng chế dừng/chạy lại |
| Khoá tài khoản xuyên máy + heartbeat + hết hạn 5' | [HubDatabase.Leases.cs:118](../server/Shopee.Hub.Web/Data/HubDatabase.Leases.cs:118) | Giữ nguyên ngữ nghĩa, chỉ đổi *ai giữ tới lúc nào* |
| Luật gộp "rỗng không xoá giá trị đang giữ" | `GiuLaiNeuRong` ([HubDatabase.OrdersAccounts.cs](../server/Shopee.Hub.Web/Data/HubDatabase.OrdersAccounts.cs)) | **Tiền lệ** cho luật gộp hồ sơ KM |
| DTO mở rộng bằng property `init` trong thân record | [HubDtos.cs:299](../suite/Shopee.Core/Coordination/HubDtos.cs:299) | Deploy lệch bản client/hub vẫn parse được |

### 1.3 Ba quyết định người dùng đã chốt (22/08/2026)

1. **Máy chủ trì = máy bật form KM.** Máy nào mở form "KM của Shop" và lưu cấu hình thì ghi luôn `machine_id`
   của nó vào hồ sơ KM trên hub. Máy khác đọc thấy "không phải tôi" ⇒ **chỉ nhường khung, không đua chạy**.
   Đổi máy chủ trì = mở form ở máy mới.
2. **Có cưỡng chế dừng xuyên máy, làm luôn đợt này.** Máy chủ trì gửi lệnh Dừng qua hub, chờ nhả khoá, chạy KM,
   xong gửi Chạy lại.
3. **Cấu hình KM: máy nào sửa sau thắng** (last-write-wins theo mốc sửa). Giữ nguyên thói quen hiện tại — mở form
   ở bất kỳ máy nào cũng được.

### 1.4 Mục tiêu đo được của cả đợt

Hai máy A, B cùng có tài khoản `X`, B đang chạy check đơn liên tục cho `X`, bật KM tự động ở A:

- 01:00 → B tự nhường khung **và nhả khoá** (không phải chỉ dời mốc).
- 02:0x → A giành được khoá, chạy KM; nếu B vẫn ôm khoá thì A **cưỡng chế dừng B** rồi mới chạy.
- Xong KM (hoặc tới 06:00) → **B tự chạy lại check đơn**, kể cả khi A tắt máy giữa chừng.
- Đêm sau → B **không** chạy lại KM đã chạy ở A (mốc `LanChayCuoi` là mốc CHUNG).

## 2. Phạm vi

**Làm:**

- Bảng `km_profiles` trên hub (khoá `login`) + 2 route đẩy/kéo + luật gộp THUẦN dùng chung hai phía.
- Client: thêm cột mốc-sửa + máy-chủ-trì vào `km_config`, worker đẩy/kéo, gộp bằng đúng hàm thuần của hub.
- Vòng đời khoá của phiên check đơn: **nhả khoá khi nhường khung**, xin lại theo kiểu **chờ-và-thử-lại**.
- `NhipLichKmAsync`: chỉ máy chủ trì mới chạy; ghi `LanThuTuDong` lên hub **trước** khi chạy.
- Bắt tay xin/trả khung qua hub (2 route mới) + bảng `km_paused` để **hub** nhớ máy nào bị dừng vì KM, kèm quét
  tự trả khung khi máy chủ trì chết giữa chừng.
- Hiện trạng thái KM trên trang `/orders` của Hub (pha D, cắt được nếu hết thời gian).

**Không làm:**

- Không đồng bộ cookie (luật cũ, tuyệt đối).
- Không đổi khung `[02:00, 06:00)` và biên an 60 phút — hai hằng số đó đã chốt, plan này chỉ làm chúng **có hiệu lực xuyên máy**.
- Không làm con trỏ tiến độ shop ("lượt sau bắt đầu từ shop i") — vẫn thuộc mục CÒN LẠI của plan 18/08.
- Không đụng khoá tài khoản của Scrape/Search (tiền tố khác).
- Không dựng form KM thứ hai bằng Blazor trên Hub (đã bác ở câu hỏi 3).
- Không đổi ngữ nghĩa "một lượt thử mỗi khung" — chỉ đổi *phạm vi* của mốc thử từ máy sang tài khoản.

## 3. Các bước thực hiện

### Pha A — Hồ sơ KM trên hub + đồng bộ hai chiều

**A1. Luật gộp THUẦN** — file mới `suite/Shopee.Core/Coordination/KmHoSoGop.cs`, dùng **chung** cho cả hub và
client (một bộ luật, không hai; đây là bài học `KmConfigRepository.ChuanHoaSoNgay`).

Hai nhóm trường, **hai luật khác nhau — đừng gộp làm một**:

| Nhóm | Trường | Luật gộp | Vì sao |
|---|---|---|---|
| Cấu hình | `TuDong`, `SoNgay`, `PhanTram`, `GioBatDau`, `GioKetThuc`, `OwnerMachine` | bản có `CfgUpdatedAt` **mới hơn** thắng; bằng nhau ⇒ giữ bản đang có (idempotent) | người dùng chốt "máy nào sửa sau thắng" |
| Mốc chạy | `LanChayCuoi`, `LanThuTuDong` | **MAX** — chỉ tiến, không lùi, null thua mọi giá trị | đây là *sự kiện đã xảy ra*; để một máy có mốc cũ ghi đè là mở đường chạy KM trùng |

**A2. Mốc thời gian đi dây là UTC.** `km_config` hiện lưu `DateTime` local, `Kind = Unspecified`
(`ToString("o")` không kèm offset). Xuyên máy thì đó là **mìn**. Quy ước bắt buộc, ghi vào xmldoc:

- Trên dây và trong hub: `DateTimeOffset` **UTC**.
- Trong DB client và trong mọi luật của `KmLichTuDong`: **giờ địa phương** (khung 2–6h là giờ địa phương).
- Chuyển đổi ở đúng **hai cửa** (đẩy / kéo) bằng hàm thuần có test, không rải `ToUniversalTime()` khắp nơi.

**A3. Hub** — `server/Shopee.Hub.Web/Data/HubDatabase.KmProfiles.cs` (partial mới, khuôn `HubDatabase.OrdersAccounts.cs`):

```sql
CREATE TABLE IF NOT EXISTS km_profiles(
  login TEXT PRIMARY KEY,
  tu_dong INTEGER DEFAULT 0, so_ngay INTEGER DEFAULT 7, phan_tram INTEGER DEFAULT 5,
  gio_bat_dau TEXT DEFAULT '', gio_ket_thuc TEXT DEFAULT '',
  owner_machine TEXT DEFAULT '', cfg_updated_at TEXT DEFAULT '',
  lan_chay_cuoi TEXT DEFAULT '', lan_thu_tu_dong TEXT DEFAULT '',
  updated_at TEXT);
```

Khoá là `login` **chứ không phải** `(machine_id, login)` — cố ý khác gương danh bạ: danh bạ là "máy này có
những tài khoản nào" (thuộc về máy), hồ sơ KM là "tài khoản này có lịch gì" (thuộc về tài khoản). Ghi rõ trong
xmldoc, kẻo người sau chép nhầm hợp đồng "thay toàn bộ theo machine_id" sang đây và xoá sạch hồ sơ của máy khác.

**A4. Route + DTO:**

- `HubRoutes.KmProfiles = "/orders/km"` — `GET` kéo toàn bộ, `POST` đẩy một lô. Đặt **ngoài** tiền tố `config/`
  (như `OrdersAccounts`) để không dính chặn `AllowClientConfigPush`.
- DTO `KmProfileItem` trong `HubDtos.cs`: trường mới **phải là property `init` trong thân record**, không phải
  tham số positional — deploy hub/client lệch bản vẫn parse được (luật đã ghi ở [HubDtos.cs:306](../suite/Shopee.Core/Coordination/HubDtos.cs:306)).

**A5. Client — DB:** `KmConfig` thêm `CfgUpdatedAt` (DateTime?) và `OwnerMachine` (string). Cột mới thêm bằng
`EnsureColumn` **y hệt cách `lan_thu_tu_dong` đã làm** ([Database.cs:223](../orders/XuLyDonShopee.Core/Data/Database.cs:223)) —
thiếu bước này là app cũ mở màn Tài khoản là ném.

**A6. Client — worker:** `suite/Shopee.Suite/Infrastructure/OrdersModuleHost.KmSync.cs`

- Kéo mỗi 60s (khuôn `GsheetConfig.cs`) + kéo NGAY sau khi đẩy (invalidate TTL).
- Đẩy khi **bẩn**: form KM lưu cấu hình, hoặc `ChotLuotKm`/`NhipLichKmAsync` ghi mốc.
- Đẩy **đồng bộ, chờ kết quả** ở đúng MỘT chỗ: `LanThuTuDong` trước lượt KM tự động (xem C2). Mọi chỗ khác
  fire-and-forget.
- Mất hub ⇒ **degrade như một máy**: dùng nguyên `km_config` cục bộ, hành vi y hiện tại. Đây là luật đã có ở
  `AcquireAccountLease` ([OrdersModuleHost.AccountLease.cs](../suite/Shopee.Suite/Infrastructure/OrdersModuleHost.AccountLease.cs)) — giữ cho nhất quán.

### Pha A-vòng 2 — sửa sau phản biện (chốt 22/08/2026, phiên chính)

Vòng 1 build xanh, 46 test mới xanh, nghiệm thu chấm ĐẠT. Phản biện vẫn tìm ra **một lỗi CHẶN PHÁT HÀNH**, và
phiên chính đã **dựng test chứng minh nó ĐỎ thật** (không phải suy luận từ đọc code). Bảy hạng mục dưới đây là
bắt buộc trước khi sang pha B.

**V1. [CHẶN PHÁT HÀNH] Hub nuốt sạch cấu hình ở lượt đẩy ĐẦU TIÊN của mọi dòng `km_config` đang có.**

`HubDatabase.KmProfiles.cs:55` lấy `DocHoSoLocked(tx, login) ?? new KmProfileItem(login)` rồi đưa vào
`KmHoSoGop.Gop`. Dòng chưa tồn tại ⇒ `dangCo` là **hồ sơ mặc định tổng hợp** (`TuDong=false, SoNgay=7,
PhanTram=5, giờ rỗng, CfgUpdatedAt=null`). Bản client đẩy lên cũng có `CfgUpdatedAt = null` vì
`EnsureColumn(… "cfg_updated_at" …)` là `ALTER TABLE ADD COLUMN` **không backfill** ⇒ `MoiHon(null, null) == false`
⇒ `nen = dangCo` ⇒ **hub ghi mặc định, vứt trọn cấu hình thật**, trả HTTP 200, không log.

Ca đầy đủ (phản biện dựng, phiên chính xác nhận): cấu hình 25%/3 ngày/08:00–09:00 của máy A bị thay bằng mặc
định → máy B kéo về vẫn không biết acc có KM → người dùng mở form trên B để XEM rồi bấm Đóng → B đóng dấu
`CfgUpdatedAt = now` + `OwnerMachine = B` cho bản mặc định → A kéo về trong ≤60s → **`TuDong` của A về `false`**.
KM tắt trên mọi máy, không một dòng log.

Sửa — **cần cả hai vế, thiếu một là vẫn hỏng**:
1. Hub: dòng CHƯA TỒN TẠI thì **nhận trọn `den`**, không gộp với mặc định tổng hợp.
2. Client: **backfill `cfg_updated_at`** cho dòng cũ ngay trong migration (một mốc quá khứ cố định, ví dụ
   `2026-01-01T00:00:00Z`) — để cấu hình thật thắng hồ sơ rỗng nhưng vẫn thua mọi lượt sửa sau này.
   Chỉ sửa (1) thì hai máy cùng có dòng cũ vẫn "ai đẩy trước thắng" một cách ngẫu nhiên.

**V2. [PHẢI SỬA] Kéo cửa chủ trì (C1) từ pha C về pha A.** Vì `TuDong` nay đồng bộ, **sửa xong V1 là mọi máy giữ
tài khoản đó đều vào `DenGioChay == true` lúc 02:00** — cùng dừng phiên check đơn cục bộ, cùng đốt `LanThuTuDong`,
máy thắng khoá là ngẫu nhiên chứ không phải máy chủ trì. Nặng hơn: máy kéo danh bạ về từ hub có bản ghi
**rỗng mật khẩu** (`HubRead.cs`), nay `TuDong=true`, giành được khoá lúc 2h sáng rồi SSO hỏng — mà máy chủ trì
thật thì đã mất lượt của đêm đó. Đây là hành vi MỚI do pha A sinh ra, không có tiêu chí nghiệm thu nào phủ.
⇒ Cài `if` chủ trì trong `NhipLichKmAsync` **ngay vòng này**, đúng luật C1 (rỗng / mất hub ⇒ giữ hành vi hiện tại).

**V3. [PHẢI SỬA] Không có dấu vết nào khi cấu hình bị nuốt.** `OrdersModuleHost.KmSync.cs:145` vứt giá trị trả về
của `ApHoSoKmVeMay`; cả ba tầng (client A, client B, hub) đều không log. ⇒ Khi lượt gộp làm ĐỔI cấu hình, ghi
`services.Log.Append(login, …)` nêu **máy chủ trì + mốc sửa + tự động on/off + chu kỳ + %**. Đây là thứ khiến V1
không thể chẩn đoán từ nhật ký.

**V4. [PHẢI SỬA] `LanChayCuoi` là bánh cóc một chiều không trần.** Máy sai đồng hồ (CMOS chết, chỉnh nhầm năm)
ghi `LanChayCuoi = 2028` ⇒ `Max` giữ ⇒ mọi máy kéo về ⇒ `DenGioChay` false tới 2028, **KM chết vĩnh viễn**, không
log. Đường gỡ duy nhất là sửa tay DB trên hub: form không lộ `LanChayCuoi`, xoá dòng cục bộ cũng vô ích vì
`ApHoSoKmVeMay` dựng lại ở nhịp 60s kế. Trước bản vá này thiệt hại gói trong một máy.
⇒ Kẹp trần khi gộp: mốc > `now + biên` (đề xuất 6 giờ) thì **bỏ, kèm log**.

**V5. [NÊN SỬA] Mở form ra XEM rồi bấm Đóng cũng cướp chủ trì.** `AccountsViewModel.Phien.cs:109-120` đóng dấu
`CfgUpdatedAt` + `OwnerMachine` vô điều kiện, không so với bản nạp lúc mở. Người dùng chốt "đổi chủ trì = mở form
ở máy mới", nhưng *mở ra nhìn* và *mở ra đổi* là hai việc khác nhau — và đây là bước 5–6 của ca V1.
⇒ Chỉ đóng dấu khi `vm.LayCauHinh()` **khác** bản nạp lúc mở.

**V6. [NÊN SỬA] Form ghi đè mốc chạy bằng ảnh chụp lúc MỞ.** `KmDialogViewModel.LayCauHinh()` trả `_lanChayCuoi`
/`_lanThuTuDong` chụp lúc dựng VM. Form là modal, mở hàng phút; trong lúc đó worker kéo (60s) hoặc `ChotLuotKm`
có thể đã đẩy mốc lên ⇒ bấm Đóng là **mốc lùi**. Hub `Max` kéo lại được — nhưng chỉ khi hub sống.
⇒ Đường lưu form KHÔNG được đụng hai mốc chạy; đọc lại từ DB lúc lưu.

**V7. [PHẢI CÓ TEST] Bốn hàm gánh nặng nhất của pha A không có một dòng test nào** — `ApHoSoKmVeMay`,
`ChupHoSoKm`, `NenThuDayLai`, `KmSyncTickAsync`. Nguy nhất là `ApHoSoKmVeMay`: nó **có** tạo dòng mới cho tài
khoản chưa từng mở form (phản biện đã đọc và xác nhận) — tức nó gánh trọn mục tiêu của plan — nhưng **không một
assert nào khoá hành vi đó**. Ai "tối ưu" nó thành `GetAll()` + `TryGetValue` (đúng khuôn `ChupHoSoKm` chỉ cách
20 dòng) là mọi máy chưa mở form vĩnh viễn không nhận hồ sơ, mà **46 test vẫn xanh**.

### Tiêu chí nghiệm thu vòng 2

- [ ] Test `DongCu_ChuaTungSua_DayLenHubLanDau_KhongDuocMatCauHinh` (phiên chính đã dựng, hiện **ĐỎ**) chuyển XANH.
- [ ] Test ca đầy đủ V1 bảy bước: dòng cũ → đẩy → máy B kéo → B mở-đóng form → A kéo về ⇒ `TuDong` của A **vẫn true**.
- [ ] Migration backfill: DB cũ có dòng `km_config` ⇒ sau `Initialize` `cfg_updated_at` **khác null** và là mốc quá khứ;
      dòng ghi mới sau đó vẫn thắng nó.
- [ ] `OwnerMachine` là máy khác ⇒ `NhipLichKmAsync` không chạy và **không ghi** `LanThuTuDong`; rỗng/mất hub ⇒ chạy như cũ.
- [ ] Gộp làm đổi cấu hình ⇒ có **đúng một** dòng log nêu máy chủ trì + mốc; không đổi ⇒ **không** log (không spam 60s/lần).
- [ ] Mốc `LanChayCuoi` tương lai quá biên bị bỏ + có log; mốc trong biên vẫn nhận.
- [ ] Mở form không sửa gì rồi Đóng ⇒ `CfgUpdatedAt` và `OwnerMachine` **không đổi**.
- [ ] Đường lưu form không làm lùi `LanChayCuoi`/`LanThuTuDong` khi worker vừa đẩy mốc mới.
- [ ] `ApHoSoKmVeMay`: tài khoản **chưa có** dòng `km_config` ⇒ **tạo dòng mới**; đã có và không đổi ⇒ **không ghi**.

### Pha A-vòng 3 — sửa sau phản biện vòng 2 (chốt 22/08/2026)

Vòng 2 nghiệm thu chấm ĐẠT 8/9, build 0 warning, 2456 test xanh. Phản biện vòng 2 vẫn ra **hai lỗi CHẶN PHÁT
HÀNH**; phiên chính đã đọc code xác nhận cả hai cùng bốn lỗi PHẢI SỬA. Bài học lặp lại lần thứ ba trong đợt này:
*build xanh + test xanh + nghiệm thu ĐẠT vẫn không đủ.*

**W1. [CHẶN] Pha A đứng một mình là BƯỚC LÙI — chặn tạm cho tới khi pha B xong.**

`AccountSession.cs:640` đọc `KmConfig.Get(_accountId).TuDong`, nay là giá trị ĐỒNG BỘ. Nên máy B (không chủ trì)
bắt đầu dời mọi vòng rơi vào `[01:00, 06:00)` sang 06:00 — **nhưng vẫn ôm khoá `orders:<login>`**, vì nhả khoá là
việc của pha B (chưa làm). Kết quả: A vẫn không xin được khoá ⇒ **KM vẫn không chạy**, mà B **mất thêm 5 tiếng
check đơn mỗi đêm**. Trước pha A thì B chạy suốt đêm. Đây là hồi quy thuần, không tiêu chí nghiệm thu nào của
vòng 2 chạm tới.

⇒ Cờ nhường khung đổi thành `cf.TuDong && LaMayChuTri(cf.OwnerMachine, mayNay)`: máy CHỦ TRÌ nhường khung y như
trước pha A; máy khác **không nhường** cho tới khi pha B cho nó nhả khoá thật. Ghi rõ trong xmldoc rằng đây là
**chặn tạm của pha A** và **pha B phải gỡ** — kèm tên hạng mục B1 để người làm pha B tìm ra.

**W2. [CHẶN] Nhường khung nổ MỖI ĐÊM, kể cả chu kỳ 7 ngày.** `HoanVongCheckDon` chỉ nhận một `bool`, không hỏi
`MocChayKe`. Với `SoNgay = 7` là đổi **5h × 7 đêm** check đơn lấy **một** lượt KM.
⇒ Chỉ nhường khung khi **đêm nay thực sự tới hạn** theo `MocChayKe`. Hàm thuần, test ma trận: tới hạn ⇒ nhường;
chưa tới hạn ⇒ giữ nguyên mốc; sát hạn (đêm mai) ⇒ không nhường đêm nay.

**W3. [CHẶN] Cửa chủ trì V2 VÔ HIỆU trên mọi máy đang có dữ liệu.** Backfill (`Database.cs:416-441`) chỉ đóng
dấu `cfg_updated_at`, **không đụng `owner_machine`** ⇒ dòng cũ có `owner_machine` NULL ⇒ `LaMayChuTri("", …)`
trả `true` ⇒ **mọi máy đều được chạy** ⇒ đúng cuộc đua V2 sinh ra để chặn, ngay đêm đầu. Chỗ ghi `OwnerMachine`
duy nhất là `KmDialogViewModel.BanLuuSauForm`, mà nó trả `null` khi người dùng không đổi gì.
⇒ Backfill **cũng đóng dấu `owner_machine` = máy này cho dòng `tu_dong = 1`** — máy đang bật KM chính là máy đã
mở form. Dòng `tu_dong = 0` để rỗng (không ai chủ trì một lịch đang tắt).
⇒ Test `MayKhacChuTri_…` hiện xanh nhờ tự tay `Save` một chủ trì — cảnh đó **không tồn tại** trên máy người dùng
ngày đầu. Thêm test dựng đúng cảnh nâng cấp: DB cũ → `Initialize` → đẩy → kéo ở máy hai ⇒ máy hai **không** chạy.

**W4. [PHẢI SỬA] Nút "Máy này nhận chủ trì" trên form KM** (người dùng chốt 22/08). Hiện chủ trì trỏ vào máy đã
nghỉ hưu (cài lại Windows ⇒ đổi machine id) là **KM chết vĩnh viễn trên cả fleet**: form không hiện chủ trì,
không có nút nhận quyền, `BanLuuSauForm` trả `null` khi không đổi gì, và `ChotLuotKm` chỉ ghi `LanChayCuoi`.
Tệ hơn, dòng log của V2 bảo người dùng *"mở form 'KM của Shop' ở đây và lưu lại"* — **làm đúng vậy thì không có
gì xảy ra**. Hai hạng mục cùng vòng 2 đá nhau.
⇒ Form hiện **máy đang chủ trì** + một nút nhận quyền (đóng dấu `OwnerMachine` + `CfgUpdatedAt` tường minh, dù
cấu hình không đổi). ⇒ **Sửa câu log cho khớp hành vi thật.**

**W5. [PHẢI SỬA] V5 so SAI CẶP — "mở ra xem rồi đóng" vẫn lùi cấu hình máy khác.** `BanLuuSauForm(tuoi, tuForm…)`
lấy `tuoi` = dòng **đọc lại lúc lưu**, `tuForm` = ảnh chụp **lúc mở form**. Nó đang trả lời *"DB có khác form
không"*, chứ không phải *"người dùng có sửa gì không"*. Ca thật: B mở form (25%) để xem → A đổi 25%→40% và đẩy
lên → worker của B kéo về (nhịp 60s chạy trên thread Timer, **modal không chặn**) → B bấm Đóng, không chạm gì ⇒
`CungCauHinh(40%, 25%)` false ⇒ **lưu 25% + đóng dấu mốc mới + cướp chủ trì** ⇒ A bị lùi về 25%. Đúng hình dạng
lỗi vòng 1, dịch vào cửa sổ 60 giây.
⇒ `KmDialogViewModel` giữ **bản nạp lúc mở** (`_banNap`); "có sửa không" = so `tuForm` với `_banNap`. `tuoi` chỉ
dùng lấy hai mốc chạy (V6). Nếu `tuoi` đã khác `_banNap` (worker vừa kéo bản mới về trong lúc form mở) thì
**báo cho người dùng**, đừng ghi đè lặng.

**W6. [PHẢI SỬA] Cảnh báo mốc rác của V4 không bao giờ bắn trong ca nó sinh ra để bắn.**
`OrdersModuleHost.KmSync.cs` đặt `NoiMocRacNeuCo` **SAU** cửa `if (moi == cu) continue;`. Máy đã có dòng khớp
sẵn ⇒ mốc rác bị bỏ ⇒ `moi == cu` ⇒ `continue` ⇒ **không log gì**, lặp lại mỗi 60 giây mãi mãi. Test hiện xanh
chỉ vì nó dựng cảnh tài khoản **chưa có dòng**.
⇒ Đưa `NoiMocRacNeuCo` lên **trước** cửa thoát, kèm chặn lặp theo NGÀY như `BaoKhongPhaiChuTri`. Test phải dựng
cảnh **đã có dòng khớp sẵn**.

**W7. [PHẢI SỬA] `CfgUpdatedAt` không có trần VÀ tự LAN.** `KmHoSoGop.cs` chép `nen.CfgUpdatedAt` nguyên, không
qua `LocMoc`; `KmSync.cs` ghi nó xuống `km_config` của mọi máy kéo về. Một mốc 2028 thắng vĩnh viễn **và tự nhân
bản ra cả fleet** — xoá dòng trên hub vô ích vì máy kế tiếp đẩy lại chính mốc đó. Gỡ = sửa tay `hub.db` **và**
`app.db` từng máy. Khác hẳn `LanChayCuoi` (lọc cả hai phía nên tự rụng).
⇒ Mốc sửa vượt `bayGio + biên` thì **KẸP VỀ `bayGio`**, không bỏ về `null` (bỏ null = vứt trọn cấu hình, ngữ
nghĩa khác). Kèm log.

**W8. [NÊN SỬA] Backfill mốc cố định KHÔNG chữa được "ai đẩy trước thắng".** Plan vòng 2 (mục V1) khẳng định vế
(2) chữa được — **sai**. Cùng một chuỗi `2026-01-01…` cho mọi máy ⇒ `MoiHon` dùng `d > c` ⇒ **bằng nhau = thua**
⇒ máy đẩy trước thắng. Ca thật: A (25%, tự động BẬT) và B (mặc định) cùng nâng cấp; B khởi động trước, `_kmDirty`
bật lúc khởi tạo nên **B đẩy trước khi kéo** ⇒ hub giữ bản B; A đẩy lên, mốc bằng nhau ⇒ hub vẫn giữ B; A kéo về,
mốc bằng nhau ⇒ A giữ bản A. **Hub, A, B ba bản khác nhau, ổn định vĩnh viễn.**
⇒ Phá hoà có thứ tự khi hai bên **cùng mốc mà khác cấu hình**: ưu tiên bản có `TuDong = true` (lịch đang bật là
thứ người dùng cố ý đặt), hoà tiếp thì `machine_id` nhỏ hơn thắng. **Bắt buộc log** ở lượt phá hoà — hiện lượt
đó im hoàn toàn.

**W9. [NÊN SỬA] `_kmTimer` không được Dispose lúc thoát app.** `OrdersModuleHost` teardown dispose
`_gsheetTimer`, `_mirrorTimer`, `_outboxWorker` — thiếu `_kmTimer`. Nhịp 3 giây vẫn bắn trong lúc `StopAllAsync`
chờ (tới 30s) và **sau** `svc.Log.Dispose()` ⇒ dòng log V3 mất, và `KmConfig.Save` vẫn ghi SQLite lúc app đang
đóng / đang được Velopack thay file.

**W10. [NHẸ] Hai file mô tả cùng một trường, nói ngược nhau.** xmldoc `KmProfileItem.OwnerMachine` vẫn viết
*"Ở đợt pha A chưa ai ĐỌC trường này"* — V2 đã kéo phần đọc về pha A. `KmConfigRepository` đã sửa, DTO thì chưa.

**W11. [GHI NHẬN, không sửa vội] V4 đổi một kiểu chết lấy một kiểu chạy trùng.** `LocMoc` bỏ mốc "tương lai"
theo đồng hồ của **chính máy đọc**. Máy chậm hơn thật > 6h thấy mốc ĐÚNG của hub là tương lai ⇒ bỏ ⇒
`LanChayCuoi = null` ⇒ `DenGioChay` trả `true` ngay ⇒ chạy KM mỗi đêm. Trước V4 máy đó chỉ đơn giản không chạy.
Sau W3 thì thiệt hại giới hạn ở máy chủ trì. ⇒ Đợt này **chỉ cần một dòng log** khi `LocMoc` bỏ mốc; đừng thiết
kế lại.

**W12. [lỗ phủ nghiệm thu vòng 2 chỉ ra]** Ca "máy khác chủ trì ⇒ không chạy" mới chỉ assert
`LanThuTuDong == null`, suy ra gián tiếp từ thứ tự trong impl. ⇒ Thêm assert **trực tiếp**: không phóng trình
duyệt / không vào thân lượt KM.

**W13. [ghi nợ spec]** `login TEXT PRIMARY KEY **COLLATE NOCASE**` (vòng 1) đúng và có test, nhưng plan mục A3
viết `login TEXT PRIMARY KEY` trơn và người thực thi không khai vào mục lệch spec. Plan đã được sửa; ghi lại ở
đây để lần sau khai đủ.

### Tiêu chí nghiệm thu vòng 3

- [ ] Máy **không** chủ trì: mốc vòng check đơn rơi vào `[01:00, 06:00)` ⇒ **KHÔNG** bị dời (chặn tạm W1);
      máy **chủ trì** ⇒ vẫn dời như trước pha A.
- [ ] `SoNgay = 7`, đêm nay **chưa** tới hạn ⇒ **không** nhường khung; đêm tới hạn ⇒ nhường (W2).
- [ ] DB cũ có `tu_dong = 1` ⇒ sau `Initialize`, `owner_machine` = máy này; `tu_dong = 0` ⇒ vẫn rỗng (W3).
- [ ] Ca nâng cấp thật: DB cũ → đẩy → máy hai kéo về ⇒ máy hai **không** chạy lượt KM và **không** ghi
      `LanThuTuDong` (W3 — cảnh dựng phải là cảnh nâng cấp, KHÔNG tự tay `Save` chủ trì).
- [ ] Nút nhận chủ trì: bấm ⇒ `OwnerMachine` + `CfgUpdatedAt` đổi dù cấu hình y nguyên; không bấm ⇒ không đổi (W4).
- [ ] Câu log "không phải chủ trì" nêu đúng thao tác thật (W4).
- [ ] Mở form → worker kéo bản mới về giữa chừng → bấm Đóng không chạm gì ⇒ **KHÔNG** lưu, **KHÔNG** đóng dấu,
      và người dùng **được báo** cấu hình đã đổi (W5).
- [ ] Mốc rác từ hub, máy **đã có dòng khớp sẵn** ⇒ vẫn có **đúng một** dòng log mỗi ngày (W6).
- [ ] `CfgUpdatedAt` tương lai quá biên ⇒ **kẹp về `bayGio`** (không phải null) + log; trong biên ⇒ giữ nguyên (W7).
- [ ] Hai máy cùng mốc backfill mà khác cấu hình ⇒ ra **cùng một** bản trên cả hub lẫn hai máy, + log phá hoà (W8).
- [ ] `_kmTimer` được dispose trong teardown (W9).
- [ ] Ca "máy khác chủ trì" có assert **trực tiếp** không vào thân lượt KM (W12).

### Pha A-vòng 4 — VÒNG CUỐI của pha A (chốt 22/08/2026, phiên chính)

Vòng 3: nghiệm thu 10/12 ĐẠT + 2 ĐẠT MỘT PHẦN; phản biện **lần đầu không ra lỗi CHẶN PHÁT HÀNH**. Xu hướng
đúng chiều (vòng 1: 1 blocker · vòng 2: 2 · vòng 3: 0). Vòng 4 đóng nốt các mục có tác động thật lên người dùng,
rồi **sang pha B bất kể còn mục nhỏ nào** — phần còn lại ghi thành NỢ ở mục dưới.

**Chủ đề của vòng này, một câu:** *vòng phản hồi không khép kín, và ba chỗ phần mềm NÓI SAI với người dùng.*

**X1. [PHẢI SỬA — sát mức chặn] Kẹp `CfgUpdatedAt` không tự gỡ ở máy NGUỒN.** `KmHoSoGop.KepMocSua` trả
`bayGio` **của bên đang đọc**, không ghi ngược về nguồn. Máy sai đồng hồ (2028) kéo hồ sơ về thì trên chính nó
`2028` không phải "tương lai" ⇒ `moi == cu` ⇒ **không sửa gì**; mà `_kmDirty = true` lúc khởi tạo nên **mỗi lần
mở app nó lại đẩy `2028` lên**, hub lại kẹp về `now`, bản của nó **lại thắng và lùi mất sửa đổi của máy khác**.
Lặp vô hạn. Nhật ký người dùng thấy là *"cấu hình vừa ĐỔI, sửa lúc \<now\>"* — tức **nói dối**: không ai sửa gì.

Đây là tính chất `LocMoc` có mà `KepMocSua` không: mốc chạy bị **BỎ** ở cả hai phía nên tự rụng khỏi hub; mốc
sửa bị **THAY** nên nguồn không bao giờ biết mình sai.

⇒ Khép vòng: `POST /orders/km` **trả lại bản đã gộp**, client **ghi đè `km_config` của chính mình** bằng bản hub
trả về. Hub là nguồn giờ ngoài máy đó — đồng hồ của máy sai không tự tố cáo được. (DTO trả về mở rộng theo đúng
luật property `init`; hub cũ trả body khác ⇒ client phải chịu được `null` và bỏ qua.)

**X2. [PHẢI SỬA] `CauPhaHoa()` khuyên một thao tác KHÔNG TỒN TẠI.** Chuỗi mới của vòng 3
(`OrdersModuleHost.KmSync.cs`) nói *"mở form 'KM của Shop' ở máy đó và **LƯU** lại một lần"*. Form có đúng ba
nút: **Chạy chương trình** · **Đóng** · **Máy này nhận chủ trì** — **không có nút Lưu**, và đóng mà không sửa gì
thì `BanLuuSauForm` trả `null`. Làm đúng lời khuyên ⇒ không có gì xảy ra ⇒ đêm sau lại đúng dòng đó.

Đây là **lần thứ hai cùng một hình dạng lỗi** trong đợt: W4 sinh ra để sửa đúng câu này ở `BaoKhongPhaiChuTri`,
và cùng vòng đó lại đẻ ra một câu y hệt ở `CauPhaHoa`. ⇒ Sửa câu, **và quét TOÀN BỘ chuỗi người dùng đọc được**
của pha A để tìm câu thứ ba cùng lỗi. Mỗi câu hướng dẫn phải trỏ vào một nút/thao tác **có thật**.

**X3. [PHẢI SỬA] Thiếu hẳn vế "log" của W7 — và KHÔNG được khai.** Tiêu chí W7 viết "kẹp về `bayGio` **+ log**".
`NoiMocRacNeuCo` chỉ xét `LanChayCuoi` và `LanThuTuDong`; **không nhánh nào xét `CfgUpdatedAt`**. Nghiệm thu và
phản biện **độc lập cùng chỉ ra**. Hệ quả: đúng ca X1 — ca nguy hiểm nhất của nhóm W7 — diễn ra hoàn toàn im
lặng ở phía mốc.
⇒ Thêm nhánh log, chặn lặp theo NGÀY như các cảnh báo khác. ⇒ **Và khai vào mục lệch spec những gì không làm** —
đây là lần thứ hai thiếu khai (lần một: `COLLATE NOCASE`, W13).

**X4. [PHẢI SỬA] Backfill chủ trì chạy KỂ CẢ khi máy không có hub — W3 tự tạo một hồi quy.**
`WireKmSync` gọi `BackfillOwnerMachineOnce(LeaseMachineId)` **vô điều kiện**, không kiểm `CoordinationRuntime.Client`;
mà `LeaseMachineId` không bao giờ rỗng (`machine.json` là file cục bộ) nên nhánh "mayNay rỗng ⇒ không làm gì"
không đỡ được. Ca thật: người dùng **một máy, không hub**. Nâng cấp ⇒ `owner_machine = <guid>:orders`, latch
đóng. Sau đó `machine.json` bị tạo lại (dọn AppData, hồ sơ Windows mới, cài lại máy) ⇒ id mới ⇒ `LaMayChuTri`
false ⇒ **KM tự động ngừng hẳn trên máy duy nhất**, và vòng check đơn thôi nhường khung. Trước W3, chủ trì rỗng
nghĩa là "ai cũng chạy" nên ca này không tồn tại. Backfill đã latch, không chạy lại.
⇒ Chỉ backfill khi hub **thật sự cấu hình**. Chủ trì là khái niệm chỉ có nghĩa khi có nhiều máy.

**X5. [NÊN SỬA] Máy chậm giờ > 6h ghi đĩa IM LẶNG mỗi 60 giây, vĩnh viễn.** Mốc hub luôn "quá xa tương lai" với
máy đó ⇒ `mocDen` bị kẹp về `bayGio` mới mỗi lượt ⇒ `moi.CfgUpdatedAt != cu.CfgUpdatedAt` ⇒ không `continue` ⇒
`Save` mỗi nhịp; mà `CungCauHinh` cố ý bỏ qua `CfgUpdatedAt` nên **không log**. Hành vi MỚI của vòng 3, không
nằm trong mục lệch spec nào.
⇒ Chỉ khác nhau **ở riêng mốc đã bị kẹp** thì đừng ghi đĩa (X1 có thể đã chữa; nếu vậy thì ghi rõ là đã chữa và
thêm test khoá lại).

**X6. [NÊN SỬA] Form và nhật ký in GUID THÔ làm tên máy chủ trì.** Người dùng đọc *"Máy chủ trì:
3f8a91c2…:orders"*. Repo đã có quy ước ngược lại: `AccountSession.CauBiTuChoiKhoa` dùng **hostname**
(`MachineIdentity.DisplayName`, tra qua `/fleet`). W4 sinh ra vì "form không hiện chủ trì nên không đối chiếu
được" — hiện bằng GUID thì fleet 3+ máy vẫn phải mở form từng máy để đoán ra ai.
⇒ Hiện "tên máy (id ngắn)" theo khuôn `MachineIdentity.Label`.

**X7. [NÊN SỬA] `StopAsync` → `DungNhipHoSoKm` không có lưới, và xmldoc khẳng định mạnh hơn thực tế.**
Bỏ dòng gọi ở `OrdersModuleHost.cs:121` thì **không test nào đỏ** (nghiệm thu xác nhận bằng grep). Thêm nữa
`Timer.Dispose()` **không chờ callback đang bay**, mà callback là fire-and-forget có `await` mạng tới 8s ⇒ một
nhịp vẫn có thể `Log.Append` **sau** `Log.Dispose()`.
⇒ Thêm lưới cho chỗ nối; và **hạ câu xmldoc xuống cho đúng sự thật** (hoặc thêm cờ "đang thoát" mà
`KmSyncTickAsync` kiểm ở đầu).

### Tiêu chí nghiệm thu vòng 4

- [ ] Máy sai đồng hồ đẩy mốc sửa rác ⇒ sau MỘT lượt đẩy, `km_config` của **chính máy đó** mang mốc hub trả về;
      lượt đẩy kế **không** làm lùi cấu hình máy khác (X1). Có test dựng đúng chuỗi đẩy→nhận→ghi đè.
- [ ] Hub cũ / body trả về không đọc được ⇒ client **không ném, không đổi gì** (X1, nhánh degrade).
- [ ] Mọi chuỗi hướng dẫn người dùng của pha A trỏ vào **nút/thao tác có thật** — liệt kê trong báo cáo từng
      chuỗi đã rà và nút tương ứng (X2).
- [ ] `CfgUpdatedAt` bị kẹp ⇒ có **đúng một** dòng log mỗi ngày; không bị kẹp ⇒ không log (X3).
- [ ] Không có hub ⇒ backfill chủ trì **không chạy** và **không đóng latch**; có hub ⇒ chạy như vòng 3 (X4).
- [ ] Máy chậm > 6h: hai lượt kéo liên tiếp **không** sinh lượt ghi đĩa thứ hai (X5).
- [ ] Form + nhật ký hiện **tên máy**, không phải GUID trần (X6).
- [ ] Bỏ dòng gọi `DungNhipHoSoKm` trong `StopAsync` ⇒ **có test ĐỎ** (X7).

### Pha A-vòng 5 — đóng blocker do chính vòng 4 sinh ra (chốt 22/08/2026)

Vòng 4 nghiệm thu ĐẠT 7/8 và kết luận "đủ sang pha B". **Phản biện tìm ra một lỗi CHẶN PHÁT HÀNH mà nghiệm thu
không thấy — và cả hai đều đúng vai:** tiêu chí 1 hỏi *"mốc SỬA có khép vòng không"* (có), còn lỗi nằm ở chỗ bản
cài đặt tiện tay ghi đè luôn hai mốc **CHẠY** — thứ không tiêu chí nào hỏi tới. Phiên chính đã đọc code xác nhận.

Vòng 5 **hẹp, không gom việc**. Xong là sang pha B.

**Y1. [CHẶN PHÁT HÀNH] `ApVaoDong` ghi đè cả hai mốc CHẠY bằng bản hub đã lọc ⇒ KM chạy LẶP trong cùng khung.**

`OrdersModuleHost.KmSync.cs:446-457` — `ApVaoDong` gán thẳng `LanChayCuoi`/`LanThuTuDong` từ hồ sơ hub, **không
`Max`, không cửa chặn**. Mà mốc chạy đi qua `LocMoc` ở hub (`KmHoSoGop.cs:109-110`) — **bỏ về `null`** khi lệch
đồng hồ > 6h, tính theo **đồng hồ của hub**.

Ca thật (không cần máy client nào sai giờ — chỉ cần **hub** chậm > 6h, ví dụ VM khôi phục snapshot / NTP chết):
máy A chạy xong lượt KM 02:10 ⇒ `ChotLuotKm` ghi `LanChayCuoi` ⇒ ≤3s sau worker đẩy ⇒ hub `LocMoc` → `null` ⇒
trả về `null` ⇒ `ApBanHubTraVe` ghi đè ⇒ **hai mốc chạy của A bị xoá trắng** ⇒ nhịp 60s kế `DenGioChay` trả
`true` (`KmLichTuDong.cs:83-84`) ⇒ **chạy lại trọn vòng 12 shop, tạo chương trình khuyến mãi THẬT lần thứ hai**.
Lặp tới 06:00, mỗi đêm, trên mọi máy. Im lặng tuyệt đối: `CungCauHinh` bỏ qua mốc nên không có dòng
`CauDoiCauHinh`; `NoiHubKepMocSuaNeuCo` chỉ xét `CfgUpdatedAt`; `NoiMocRacNeuCo` không được gọi trên đường đẩy.

**Vì sao 39 test vòng 4 không đỏ:** helper `Dong(...)` (`KmVongBonTests.cs:57-62`) **luôn** đặt
`LanChayCuoi: null` và không bao giờ đặt `LanThuTuDong` ⇒ không một assert nào chạm hai mốc chạy trên đường
đẩy-nhận-lại.

⇒ `ApBanHubTraVe` chỉ được ghi đè **nhóm cấu hình + `CfgUpdatedAt`**; hai mốc chạy giữ `KmHoSoGop.Max(cu, hub)`.
Chính lý lẽ của X1 đã nói mốc chạy **không cần** khép vòng — chúng bị *BỎ* ở cả hai phía nên tự rụng; chỉ mốc
sửa bị *THAY* nên nguồn không tự biết. Bản cài đặt gói chung hai thứ có ngữ nghĩa ngược nhau.
⇒ Test bắt buộc: đẩy lô có `LanChayCuoi` **hợp lệ** mà hub trả `null` ⇒ mốc cục bộ **KHÔNG đổi**.

**Y2. [PHẢI SỬA] X4 mới đóng MỘT trong hai cửa.** `services.MayNay = () => LeaseMachineId` (`KmSync.cs:67`) đặt
**ngoài** `if (coHub)`. Ca: người dùng **một máy, không hub** — backfill bị X4 chặn đúng, nhưng họ mở form đổi
5%→25% rồi Đóng ⇒ `OwnerMachine = <guid>:orders`. Sau đó `machine.json` bị tạo lại (dọn AppData — đã có tiền lệ
trên máy này; hoặc hồ sơ Windows mới, hoặc cài lại máy) ⇒ id mới ⇒ `LaMayChuTri` false ⇒ **KM tự động chết trên
máy duy nhất**. Luật X4 tuyên bố *"chủ trì chỉ có nghĩa khi có nhiều máy"* — chưa được áp ở cửa đông người đi hơn.
⇒ Rót `services.MayNay` **cũng chỉ khi `coHub`**. Test: không hub → sửa % → lưu ⇒ `OwnerMachine` **vẫn rỗng**.

**Y3. [NÊN SỬA — làm vì pha B sắp cần] Lưới rà chuỗi X2 là DANH SÁCH KHAI TAY.** `CacCauNguoiDungDoc()`
(`KmVongBonTests.cs:488-509`) là 8 `yield return` cứng. Pha B đã hẹn thêm một câu `CauHoanVong` mới ("phải nói
thêm *đã nhả khoá*") ⇒ **câu đó không nằm trong danh sách ⇒ test vẫn xanh**. Bài học của chính X2 là "câu thứ ba
sinh ra ở vòng sau"; lưới hiện tại chặn hồi quy, không chặn cái mới. Thêm nữa **tooltip của "Chạy chương trình"
và "Máy này nhận chủ trì" không nằm trong vòng rà**.
⇒ Gom thêm (a) `ToolTip` của **mọi** `Button` trên form thật (`CacNutTrenForm()` đã trả `Button`, chỉ cần
`.ToolTip`); (b) phản chiếu mọi `internal static string Cau*(…)` của `OrdersModuleHost` / `KmDialogViewModel` /
`KmLichTuDong`, **đỏ khi có hàm `Cau*` chưa được liệt kê**.

**Y4. [NÊN SỬA] xmldoc `_kmDangThoat` khẳng định mạnh hơn hành vi thật.** xmldoc viết cờ này khiến "nhịp đã
khởi động và đang `await` mạng tự dừng ở cửa đầu tiên nó đi qua" — nhưng nhịp đó **đã đi qua cửa duy nhất rồi**
(`KmSync.cs:137-140`, `grep _kmDangThoat` ra đúng một chỗ đọc). Cờ chỉ cứu nhịp **chưa vào** hàm.
Đây là **lần thứ tư trong đợt** một câu chữ khẳng định mạnh hơn hành vi thật.
⇒ Hoặc kiểm cờ thêm sau mỗi `await` trong `KmSyncTickAsync`, hoặc **hạ câu xmldoc xuống đúng phạm vi**. Chọn cái
nào cũng được, nhưng câu chữ và hành vi phải khớp.

**Y5. [ghi vào plan, không phải code] THỨ TỰ DEPLOY: hub TRƯỚC client.** X1 là thay đổi giao thức. Client mới +
hub cũ ⇒ `POST /orders/km` 404 ⇒ `EnsureSuccessStatusCode` ném ⇒ mỗi máy thử lại mỗi 60s vĩnh viễn, chỉ thấy
trong `Trace` chứ **không vào nhật ký người dùng** — degrade đúng nhưng CÂM.

### Tiêu chí nghiệm thu vòng 5

- [ ] Đẩy lô có `LanChayCuoi`/`LanThuTuDong` **hợp lệ**, hub trả `null` (đồng hồ hub chậm) ⇒ mốc cục bộ
      **KHÔNG đổi**; và `DenGioChay` sau đó vẫn trả `false` (không chạy lặp) — Y1.
- [ ] Hub trả mốc chạy **mới hơn** ⇒ mốc cục bộ tiến lên (vẫn phải là `Max`, không phải "giữ nguyên `cu`") — Y1.
- [ ] Nhóm cấu hình + `CfgUpdatedAt` vẫn được ghi đè như vòng 4 (không làm hỏng X1) — Y1.
- [ ] Không hub → sửa % trên form → lưu ⇒ `OwnerMachine` **vẫn rỗng**; có hub ⇒ đóng dấu như vòng 4 — Y2.
- [ ] Thêm một hàm `Cau*` mới mà quên liệt kê ⇒ test rà chuỗi **ĐỎ**; tooltip mọi nút đều nằm trong vòng rà — Y3.
- [ ] Câu xmldoc `_kmDangThoat` khớp hành vi thật (nếu chọn hạ câu chữ thì phải nêu rõ cờ KHÔNG che ca nào) — Y4.

### Pha A-vòng 6 — đóng blocker do vòng 5 sinh ra + vá lỗ lưới trước khi mở pha B

Vòng 5: nghiệm thu ĐẠT 6/6, 2552 test xanh. **Phản biện tìm ra một lỗi CHẶN PHÁT HÀNH do chính Y2 tạo ra**;
phiên chính đã đọc code xác nhận đủ bốn mắt xích.

**Hai vòng liền bản vá tự đẻ blocker, cùng một gốc: sửa mà không soi VÒNG ĐỜI của thứ mình đụng vào.**
Vòng 4 gói chung hai nhóm có ngữ nghĩa ngược nhau; vòng 5 đóng băng một giá trị đổi được lúc chạy. Cả hai lần
đều có sẵn khuôn đúng **trong cùng file, cách vài trăm dòng**, mà không ai nhìn.
⇒ **Luật cho vòng này:** với mỗi hook/giá trị bị sửa, phải trả lời trong báo cáo *"nó có đổi được lúc chạy
không"* và **dẫn ra khuôn mẫu tương đương đang có trong repo**.

**Z1. [CHẶN PHÁT HÀNH] Y2 đóng băng "có hub" tại lúc khởi động.**

`WireKmSync(services)` chụp `CoordinationRuntime.Client is not null` **một lần** (`KmSync.cs:55`), gọi đúng một
chỗ lúc khởi tạo module (`OrdersModuleHost.cs:58`), và `services.MayNay` nằm **trong** `if (coHub)`. Nhưng
`CoordinationRuntime.Reconnect()` đổi `Client` **lúc chạy** — nút "Kết nối ngay" ở Cài đặt
(`SettingsViewModel.cs:289`), và app tự quảng cáo *"áp dụng ngay, không cần khởi động lại"*.

Ca thật (luồng cài máy mới vào fleet): cài app trên B → chưa cấu hình hub → `MayNay` **không** được rót → người
dùng cấu hình hub + "Kết nối ngay" → ≤3s sau worker kéo hồ sơ về (cổng của worker đọc tươi) → B nhận
`TuDong = true`, `OwnerMachine = "may-A:orders"` → 02:00 `mayNay = ""` ⇒ `LaMayChuTri(owner, "")` trả **`true`**
⇒ **B chạy trọn lượt KM của tài khoản A đang chủ trì, tạo khuyến mãi THẬT**, rồi ghi `LanThuTuDong` (mốc CHUNG)
⇒ **A mất trắng đêm**. Kèm hai nhánh: người dùng sửa form trên B ⇒ `OwnerMachine` bị ghi đè **rỗng** ⇒ xoá chủ
trì trên cả fleet; và `NenNhuongKhung` nhận `mayNay` rỗng ⇒ W1 thủng.

**Khuôn đúng nằm cách 200 dòng, cùng thư mục:** `services.HubDaCauHinh = () => CoordinationRuntime.Client is not null;`
(`OrdersModuleHost.HubRead.cs:25`) — đọc TƯƠI mỗi lần. Và hợp đồng của chính hook này (`AppServices.cs:244`)
viết: *"Phải đọc TƯƠI mỗi lần gọi."* Sau Y2, `MayNay` là hook DUY NHẤT trong `OrdersModuleHost` bị đóng băng.

⇒ (a) `services.MayNay = () => CoordinationRuntime.Client is not null ? LeaseMachineId : "";` — rót vô điều
kiện, đọc tươi; giữ đúng kết quả Y2 muốn (không hub ⇒ rỗng ⇒ không đóng dấu) mà không đóng băng.
⇒ (b) `BackfillOwnerMachineOnce` chuyển vào **nhịp đầu tiên thấy `Client` khác null** (giữ latch) — nay nó nằm
trong `if (coHub)` nên máy cấu hình hub sau khởi động **vĩnh viễn không backfill**.
⇒ (c) **Sửa cái test đang khoá con bug thành hợp đồng.** `KmVongNamTests.cs:259` `Assert.Null(svc.MayNay)` khoá
*hình dạng cài đặt* chứ không khoá *hành vi* — bản vá đúng sẽ làm nó ĐỎ. Đổi thành assert hành vi
(`Assert.Equal("", svc.MayNay?.Invoke() ?? "")`) + thêm ca thứ ba: wire khi chưa có hub → hub bật lên → hook
phải trả id thật.
⇒ (d) Cân nhắc: `BanLuuSauForm` **giữ nguyên** `tuoi.OwnerMachine` khi `mayNay` rỗng — *"không biết mình là ai"*
khác *"không ai chủ trì"*.

**Z2. [PHẢI SỬA] `CauHubKepMocSua` chẩn đoán SAI đúng ca Y1 sinh ra để chống.** Câu hiện tại khẳng định một
chiều: *"Gần như chắc chắn ĐỒNG HỒ MÁY NÀY chạy nhanh"*. Nhưng ca chuẩn của Y1 là **HUB chậm giờ**, mọi client
đúng giờ ⇒ dòng đó bắn ở **mọi máy trong fleet**, bảo người dùng đi sửa đồng hồ máy client, trong khi thủ phạm
là hub — thứ câu này không hề nhắc tới. `CauMocRac` và `CauMocSuaRac` ngay bên cạnh đã nêu **cả hai** chiều.
Đây là **lần thứ năm trong đợt** một câu chữ nói mạnh hơn/khác sự thật; và lưới Y3 **đã rà câu này rồi vẫn cho
qua**, vì lưới chỉ kiểm "tên nút có thật", không kiểm "chẩn đoán có đúng".
⇒ Nêu đủ ba khả năng (máy này nhanh · **hub chậm** · lệch múi giờ), theo khuôn `CauMocSuaRac`.

**Z3. [PHẢI CÓ TEST — nặng nhất sau Z1] Luật gộp `LanThuTuDong` phía HUB không có MỘT phép đo nào trong toàn repo.**
Nghiệm thu vòng 5 **đột biến chứng minh**: làm hỏng vế `LanThuTuDong` trong `KmHoSoGop.Gop:110` ⇒ Hub 186 xanh,
Core 212 xanh, Suite 59 xanh — **không một test nào trong cả repo đỏ**. Gốc: helper `HoSo(...)`
(`KmHoSoHubTests.cs:27`) không có tham số `lanThuTuDong`, mọi hồ sơ đẩy lên đều mang `null`, và phép đo duy nhất
là `Assert.Null`. Mất mốc cũng ra `null` ⇒ assert vẫn xanh.
Đây là mốc pha C dựa vào để chốt *"đêm nay ai đã dùng lượt"* (C2).
⇒ Thêm `lanThuTuDong` vào helper + test `Max` **hai chiều** qua `UpsertKmProfiles` (đẩy mốc cũ hơn ⇒ hub giữ mốc
mới; đẩy mốc mới hơn ⇒ hub tiến).

**Z4. [NÊN SỬA — làm TRƯỚC pha B, một dòng] Lưới phản chiếu Y3 không phủ nơi pha B sẽ viết.**
`CacHamSinhCau()` chỉ phản chiếu `OrdersModuleHost`, `KmDialogViewModel`, `KmLichTuDong`. Các hàm `Cau*` người
dùng đọc được của **đúng vùng pha B sắp mổ** nằm ngoài: `AccountSession.CauBiTuChoiKhoa` (chính câu mà C7 nói
đang sai với người bấm tay, và là nơi B1 sẽ thêm dòng "đã nhả khoá"), `AccountsViewModel.CauTongKetKm` /
`CauTrangThaiKm`, `OrdersBridgeSession.CauKetQuaShop`.
⇒ Thêm hai lớp `AccountSession` và `AccountsViewModel` vào `CacHamSinhCau()`.

**Z5. [NÊN SỬA] Seam `MayNay` ở `AccountsViewModel.Phien.cs:104` trần.** Nghiệm thu đột biến xác nhận: đổi
`_services.MayNay?.Invoke() ?? ""` thành hằng bịa ⇒ **2552 test vẫn xanh**. Đây là **lần thứ ba** trong đợt cùng
hình dạng "hàm đúng, chỗ nối không ai đo" (P28 vòng 3 · X4/Y2 vòng 4–5 · nay). Pha B đụng đúng vùng này.
⇒ Thêm một phép đo đi ĐƯỜNG THẬT cho chỗ nối form ⇄ `MayNay`.

**Z6. [đính chính plan]** Lời khai vòng 5 rằng `KmCotDongBoTests` là "bộ migration DUY NHẤT của `km_config`" là
**SAI** — nghiệm thu đột biến bác bỏ: bỏ `EnsureColumn` cho `lan_thu_tu_dong` làm **3 test đỏ** ở
`KmCauHinhLuuTruTests`. Ghi lại để không ai vá một lỗ không tồn tại.

### Tiêu chí nghiệm thu vòng 6

- [ ] Wire khi **chưa** có hub → hub bật lên giữa chừng (`Reconnect`) ⇒ `MayNay` trả **id thật**, không cần
      khởi động lại; vẫn chưa có hub ⇒ trả **rỗng** — Z1(a)(c).
- [ ] Máy cấu hình hub **sau** khởi động ⇒ backfill chủ trì vẫn chạy đúng một lần — Z1(b).
- [ ] Máy không hub sửa form ⇒ `OwnerMachine` **không bị ghi đè thành rỗng** — Z1(d).
- [ ] Đột biến `MayNay` thành hằng bịa ở `Phien.cs:104` ⇒ **có test ĐỎ** — Z5.
- [ ] `CauHubKepMocSua` nêu đủ ba khả năng, có nhắc **hub chậm giờ** — Z2.
- [ ] Làm hỏng vế `LanThuTuDong` trong `KmHoSoGop.Gop` ⇒ **có test hub ĐỎ** — Z3.
- [ ] Thêm một hàm `Cau*` mới trong `AccountSession` mà quên liệt kê ⇒ lưới rà chuỗi **ĐỎ** — Z4.

### NỢ KỸ THUẬT — chấp nhận mang sang pha B, ghi để không ai tưởng đã xong

1. **Chỗ nối `AccountSession` → `NenNhuongKhung` không có test** (nằm giữa `RunBridgeContinuousAsync`, cần cầu
   nối + trình duyệt). Truyền nhầm `mayNay` ⇒ `LaMayChuTri` trả `true` ⇒ máy không chủ trì vẫn nhường khung, mà
   2050 test vẫn xanh. **Pha B đụng đúng hàm này — người làm pha B phải biết nó không có lưới.**
2. **W2 chưa tính `DaThuTrongKhungNay`**: lượt KM đêm nay đã thử mà hỏng thì vòng check đơn vẫn nhường tới 06:00
   cho một lượt sẽ không bao giờ chạy lại. Không phải hồi quy (trước pha A cũng vậy), nhưng vẫn là 4 tiếng lãng phí.
3. **Chưa chạy app thật** để nhìn form KM bằng mắt. Bù bằng 2 test dựng `KmDialog` thật + 3 lượt phá XAML.
4. **Cây làm việc bẩn** ⇒ không tách được số test thuộc pha A với số của đợt 18–19/08.
5. **Chỗ nối `StopAsync` → `DonDepKhiThoatAsync` không có lưới** (nghiệm thu vòng 4 tự đột biến xác nhận: bỏ
   chính dòng gọi ⇒ build 0 warning, 52/52 vẫn XANH; `grep OrdersModuleHost.StopAsync` trong test ra rỗng).
6. **`HubClient.PushKmProfilesAsync` chưa từng chạy thật qua HTTP trong test** — cả repo chưa dựng `HubClient`
   trong test lần nào. X1 là thay đổi giao thức mà khúc giữa (`PostAsJsonAsync` + `EnsureSuccessStatusCode` +
   đọc body) trống lưới. `KmRouteHttpTests` đo phía hub, `KmDayNhanLaiDtoTests` đo hàm thuần.
7. **Ba nơi trả lời cùng một câu hỏi "cùng cấu hình?" bằng hai phép so khác nhau**: `KmConfig.CungCauHinh` dùng
   `Ordinal`, còn `KmHoSoGop.CungCauHinh` và `LaMayChuTri` dùng `OrdinalIgnoreCase`. Lệch casing id máy ⇒ một
   dòng log "cấu hình vừa ĐỔI" thừa (một lần, không lặp). Rủi ro thấp, nhưng nên thống nhất khi có dịp.
8. **`/fleet` được `await` nội tuyến** trong nhịp KM (`KmSync.cs:154`, tối đa 1 lần/5 phút): `/fleet` treo tới
   timeout 8s sẽ **hoãn** lượt đẩy/kéo của đúng nhịp đó. Không ném, không chết nhịp; chưa có test.
9. **Lệnh Dừng của hub KHÔNG chạm được lượt KM đang chạy.** `ExecuteOrdersCommand` gọi `Sessions.Stop(acc.Id)`
   — thứ đó dừng *phiên check đơn*, còn lượt KM là một **lát cắt** (`ChayLatCatAsync`), không nằm trong tầm của
   nó. Hệ quả khi cả hai máy đều có code KM: máy A đang chạy KM và giữ khoá; máy B tới hạn, nhờ hub dừng A →
   lệnh tới nơi nhưng KHÔNG dừng được lát cắt → B chờ hết 3 phút rồi bỏ lượt.
   Hiện **vô hại** vì bản đã phát hành (1.9.3) chưa có phần KM, nên chỉ một máy trong fleet chạy KM
   (người dùng xác nhận 23/08/2026). Thành lỗ thật ngay khi bản có KM được phát hành cho máy thứ hai.
   Hướng sửa khi tới đó: cửa chủ trì (`LaMayChuTri`) đã chặn sẵn phần lớn ca này — chỉ còn ca chủ trì đổi giữa
   chừng; hoặc cho `ExecuteOrdersCommand` huỷ luôn `_bridgeCts` của lát cắt đang chạy.
10. **Nhánh "không phải chủ trì thì đứng ngoài" chưa chạy thật lần nào** — cần máy thứ hai chạy bản có KM.
   Đêm 23/08 chỉ chứng minh được nhánh CHỦ TRÌ đi tiếp.

### Pha B — Nhường khung PHẢI nhả khoá

**B1.** `AccountSession.RunBridgeContinuousAsync`: khi `HoanVongCheckDon` **có dời mốc** (tức sắp ngủ dài qua
khung KM), trước `Task.Delay`:

1. `await NhaKhoaChayAsync(acc.Email)` — nhả thật.
2. Ghi log nói rõ đã nhả khoá (câu `CauHoanVong` hiện chỉ nói dời giờ; phải nói thêm "đã nhả khoá cho luồng KM").
3. Ngủ tới mốc mới.
4. Ngủ dậy: **xin lại khoá theo kiểu chờ-và-thử-lại** — thử mỗi 60s, tối đa `HanChoLayLaiKhoa` (đề xuất 30
   phút), rồi mới kết thúc phiên với câu log rõ ràng.

**B2.** Vì sao **không** dùng thẳng `XinKhoaChayAsync`: hàm đó "bỏ qua lượt, không xếp hàng" và làm phiên chết
hẳn — đúng ngữ nghĩa cho lúc **khởi động** phiên, sai cho lúc **quay lại** giữa phiên (KM có thể còn đang chạy
nốt shop cuối quá 06:00). Viết hàm riêng `XinLaiKhoaSauKhiNhuongAsync`, **không sửa** hành vi của
`XinKhoaChayAsync`.

**B3.** ⚠ **Vùng "một khoá hai chủ" — đã cắn 4 lần.** Đọc bốn khối chú thích "ĐỪNG SỬA/ĐỪNG ĐẢO" ở
[AccountSession.cs:660](../orders/XuLyDonShopee.App/Services/AccountSession.cs:660) và
[Phien.cs:1135](../orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs:1135) **trước khi gõ dòng đầu tiên**.
Ràng buộc bắt buộc:

- `_dangGiuKhoa` là cờ Interlocked ⇒ nhả rồi xin lại phải cập nhật đúng cờ, không thì `finally` nhả sót hoặc nhả thừa.
- `ReleaseAccountLease` là `DELETE … WHERE account_id AND machine_id`, **không đếm chủ** ⇒ trong lúc phiên đang
  "ngủ đã nhả khoá", nếu một lát cắt trên **cùng máy** xin khoá rồi nhả, lượt DELETE của nó vô hại (phiên đang
  không giữ) — nhưng nếu phiên vừa xin lại xong thì nó xoá đúng dòng vừa cấp. Phải kiểm ca này bằng test.

**B4.** Cờ `kmTuDong` nay lấy từ hồ sơ đã đồng bộ (`TuDong` của hồ sơ hub gộp về), không phải cấu hình chỉ có ở
máy chủ trì. Đây chính là mảnh vá cho hỏng (a).

### Pha C — Máy chủ trì + xin/trả khung + cưỡng chế

**C1. Chỉ máy chủ trì chạy lượt tự động.** `NhipLichKmAsync` thêm cửa trước `DenGioChay`:

- `OwnerMachine` == máy này ⇒ chạy.
- `OwnerMachine` khác ⇒ bỏ qua, **không** ghi `LanThuTuDong` (không phải việc của mình).
- `OwnerMachine` **rỗng** hoặc **hub không với tới được** ⇒ hành vi **y như hiện nay** (có `TuDong` thì chạy).
  Bắt buộc phải có nhánh này: dữ liệu cũ chưa có chủ trì, và mất mạng không được làm chết tính năng.

**C2. Mốc thử là mốc CHUNG.** Trước khi phóng trình duyệt, `LanThuTuDong` phải **đẩy lên hub và chờ xong** rồi
mới chạy. Fire-and-forget ở đây là hở: hai máy cùng thấy "chưa ai thử" trong cùng một phút.

**C3. Xin khung — route mới `POST /orders/km/xin-khung`** `{ login, machineId }`. **Hub quyết, không phải client**:

- Tra `account_leases` cho `orders:<login>`.
- Trống (hoặc chính máy xin đang giữ) ⇒ trả `{ trong: true }`.
- Máy khác giữ ⇒ tạo lệnh `stop` cho máy đó (`CreateOrdersCommand`, đã idempotent), **ghi một dòng `km_paused`**
  `(login, machine_id, paused_at, deadline)`, trả `{ trong: false, dangDung: "<hostname>" }`.

Vì sao đi qua hub chứ không để client tự tra `/fleet` rồi tự tạo lệnh: mở cho client tuỳ ý ra lệnh cho client
khác là một quyền mới, rộng và khó thu lại. Ở đây hub chỉ cho phép đúng một việc hẹp — dừng đúng máy đang giữ
đúng khoá đó — và tự nó ghi sổ để còn trả lại.

**C4. Chờ nhả khoá.** Máy chủ trì thử `ReserveAccounts` mỗi 10s, tối đa `HanChoNhaKhoaKm` (đề xuất 3 phút).
Được ⇒ chạy KM. Hết hạn ⇒ bỏ lượt, log rõ tên máy còn giữ (`LanThuTuDong` đã ghi nên đêm nay không thử lại).

**C5. Trả khung — route `POST /orders/km/tra-khung`** `{ login, machineId }`, gọi ở **`finally` của
`ChayLatCatAsync`**, cùng chỗ `TraHoSo()` — mọi đường ra (xong / lỗi / huỷ) đều phải đi qua. Hub tạo lệnh `run`
cho đúng các máy trong `km_paused` của login đó rồi xoá dòng.

**C6. Lưới an toàn — hub tự trả khung.** Worker quét `km_paused` (khuôn `MachineOfflineWatch`): dòng nào quá
`deadline` (mặc định `min(06:00 giờ VN, paused_at + 90')`) thì tạo lệnh `run` và xoá. Đây là thứ cứu ca **máy chủ
trì tắt/chết giữa lượt KM** — không có nó thì máy kia nằm im tới khi người dùng tự bấm Chạy, đúng cái hỏng (c)
đang có, chỉ khác nguyên nhân.

**C7. Câu từ chối cho đường bấm tay.** Mục "việc còn phải sửa (nhỏ)" của plan 18/08 vẫn chưa làm:
`CauBiTuChoiKhoa` nói "— bỏ qua lượt này" là chữ của vòng tự động; người **bấm nút** đọc câu đó không hiểu gì.
Làm nốt trong pha này: đường bấm tay nói rõ máy nào đang giữ + phải làm gì.

### Pha D — Hiện trên Hub (cắt được)

Trang `/orders` (`DispatchOrdersTab.razor`) thêm cột **KM**: `tự động on/off · mỗi N ngày · lần cuối · máy chủ
trì`, và badge **"đang bị dừng vì KM"** cho máy có dòng `km_paused`. Không có UI này thì mọi thứ ở pha A–C là
hộp đen — nhưng nó không chặn hành vi, nên xếp cuối.

## 4. Tiêu chí nghiệm thu

Build & test:

- [ ] `dotnet build ShopeeSuite.sln` — **0 warning**; `dotnet build server/ShopeeHub.sln` — 0 warning.
- [ ] Toàn bộ test cũ xanh (không được đỏ một cái nào vì đợt này).
- [ ] **Mọi test mới THỬ PHÁ được** — sửa ngược code cho hỏng ⇒ test ĐỎ ⇒ khôi phục. Báo cáo phải ghi *từng* test
      mới đã thử phá bằng cách nào và nó đỏ ở dòng nào.

Pha A — luật gộp (test thuần, không cần hub thật):

- [ ] Cấu hình: bản `CfgUpdatedAt` mới hơn thắng; **cũ hơn KHÔNG ghi đè**; bằng nhau ⇒ kết quả không đổi (gọi 2 lần ra 1 kết quả).
- [ ] Mốc chạy: `LanChayCuoi` **chỉ tiến** — đẩy mốc cũ hơn lên hub thì hub giữ mốc mới; `null` thua mọi giá trị.
- [ ] Đổi UTC↔local đi vòng tròn (local → UTC → local) ra **đúng mốc ban đầu**, kể cả `Kind = Unspecified`.
- [ ] `so_ngay = 0` trong DB cũ vẫn đọc ra ≥ 1 sau khi qua đồng bộ (không được làm hỏng `ChuanHoaSoNgay`).
- [ ] Hub: `POST` rồi `GET` trả đúng bản đã gộp; login lạ ⇒ tạo dòng mới, không đụng dòng khác.
- [ ] DB client **thiếu 2 cột mới** (mô phỏng bản cũ) ⇒ `EnsureColumn` thêm được, `Get` không ném.

Pha B — nhả khoá khi nhường khung:

- [ ] Phiên check đơn có `kmTuDong = true`, mốc vòng kế rơi vào `[01:00, 06:00)` ⇒ **`ReleaseAccountLease` được
      gọi ĐÚNG 1 lần** trước khi ngủ, và log có câu nói đã nhả khoá.
- [ ] `kmTuDong = false` ⇒ **KHÔNG** nhả khoá, không dời mốc (giữ nguyên hành vi cũ, không cắt 5 tiếng check đơn vô cớ).
- [ ] Ngủ dậy xin lại được ⇒ vòng chạy tiếp, `_dangGiuKhoa` đúng trạng thái (nhả một lần nữa ở `finally` là no-op).
- [ ] Ngủ dậy bị từ chối ⇒ **thử lại**, không chết ngay; hết `HanChoLayLaiKhoa` mới kết thúc, có log nêu tên máy giữ.
- [ ] Cả vòng đời: số lần gọi acquire == số lần gọi release (test đếm, chạy qua ca nhường khung 2 lần liên tiếp).

Pha C — chủ trì + bắt tay:

- [ ] `OwnerMachine` là máy khác ⇒ `NhipLichKmAsync` **không chạy** và **không ghi** `LanThuTuDong`.
- [ ] `OwnerMachine` rỗng / hub null ⇒ chạy y hành vi hiện tại (test này khoá nhánh degrade).
- [ ] `LanThuTuDong` đẩy lên hub **xong** rồi mới phóng trình duyệt (test bắt thứ tự, không chỉ bắt "có gọi").
- [ ] `xin-khung`: khoá trống ⇒ `trong: true`, **không** tạo lệnh nào. Máy khác giữ ⇒ tạo **đúng 1** lệnh `stop`
      cho **đúng** máy đó + **đúng 1** dòng `km_paused`; gọi lại lần hai **không đẻ lệnh thứ hai**.
- [ ] `tra-khung`: tạo lệnh `run` cho đúng tập máy đã dừng rồi **xoá** `km_paused`; không có dòng nào ⇒ no-op.
- [ ] `finally` của `ChayLatCatAsync` gọi `tra-khung` trên **cả ba** đường ra: xong · `than` ném · bị huỷ.
- [ ] Worker quét: dòng `km_paused` quá hạn ⇒ sinh lệnh `run` + xoá dòng (mô phỏng máy chủ trì chết).
- [ ] Câu từ chối đường **bấm tay** khác câu đường **tự động**, và có nêu tên máy đang giữ.

Chạy thật (bắt buộc, không thay bằng test được):

- [ ] Hai máy thật (máy dev + `NUC`), một tài khoản: B chạy check đơn, A bật KM tự động. Giả mốc thời gian để
      lượt tự động rơi vào khung. Nhật ký **hai máy** phải khớp chuỗi:
      B nhường + nhả khoá → A xin được → A chạy KM → A trả khung → **B tự chạy lại**.
- [ ] Ca cưỡng chế: B đang giữ khoá giữa vòng lúc A tới hạn ⇒ B nhận lệnh Dừng, A chạy được trong hạn 3 phút.
- [ ] Ca máy chủ trì chết: tắt app A giữa lượt KM ⇒ B vẫn được hub trả khung (chậm nhất theo `deadline`).

## 5. Rủi ro & lưu ý

1. **Mốc thời gian là rủi ro số 1.** `km_config` đang lưu local `Kind = Unspecified`. Đồng bộ sai múi giờ thì
   `LanChayCuoi` lệch 7 tiếng ⇒ chu kỳ "mỗi 7 ngày" thành 6 hoặc 8, hoặc mốc kế rơi ra ngoài khung 2–6h và
   **KM không bao giờ chạy nữa** — im lặng, không log. Chuyển đổi ở đúng hai cửa, có test đi vòng tròn.
2. **Vòng đời khoá (pha B) là chỗ đã cắn 4 lần.** Đây là phần nguy hiểm nhất của đợt. Nếu phải cắt bớt vì hết
   thời gian thì cắt **pha D**, không bao giờ cắt test của pha B.
3. **Client lệch bản.** Máy chưa cập nhật sẽ không đẩy/kéo hồ sơ KM và **không nhả khoá khi nhường khung** —
   đúng ca mà pha C (cưỡng chế) phải cứu. May là lệnh `stop` đã tồn tại từ bản hiện tại nên client cũ **vẫn hiểu**.
   Ngược lại, route mới `xin-khung`/`tra-khung` cần **hub deploy trước** client (client cũ không gọi nên vô hại).
4. **Mở quyền client→client.** `xin-khung` cho phép một máy gián tiếp dừng máy khác. Đã thu hẹp bằng cách để hub
   quyết và chỉ dừng đúng máy đang giữ đúng khoá đó — nhưng vẫn là bề mặt mới, phải ghi rõ trong xmldoc của route.
5. **Đừng để đồng bộ làm chết tính năng khi mất mạng.** Mọi nhánh "hub không với tới được" phải rơi về hành vi
   hiện tại. Kiểm bằng test riêng, không chỉ bằng mắt.
6. **Cây làm việc đang bẩn.** Đợt KM 18–19/08 còn nguyên chưa commit (`flow-km.js`, `KmDialog*`,
   `KmConfigRepository`, `KmLichTuDong`, 12 file test `Km*`). Plan này **xây tiếp lên đó**, không phải lên `HEAD`.
   Người thực thi chạy `git status` trước, và nếu cần con số kiểm chứng sạch thì tách `git worktree` theo luật ở
   `D:\Projects\CLAUDE.md`.
7. **Điểm chưa chốt, hỏi khi tới nơi:** khi hồ sơ KM đã đồng bộ thì `HoanVongCheckDon` có nên nhường khung cho
   **mọi** tài khoản có KM tự động, hay chỉ cho tài khoản mà **chính phiên đó** đang chạy? (Hiện là cái thứ hai
   và có lý; nhưng khoá là theo tài khoản nên hai tài khoản khác nhau trên hai máy không đụng nhau — nhiều khả
   năng giữ nguyên là đúng.)

---

## Báo cáo thực thi

### Vòng 1 — pha A (22/08/2026, `opus-dev`)

Bảng `km_profiles` trên hub + luật gộp dùng chung `KmHoSoGop` + hai cột `km_config` mới + worker đẩy/kéo.
Build 0 warning cả hai solution, 46 test mới xanh, nghiệm thu chấm ĐẠT. **Phản biện tìm ra một lỗi chặn phát
hành** (V1) và bốn điểm phải sửa nữa — xem mục "Pha A-vòng 2".

### Vòng 2 — sửa sau phản biện (22/08/2026, `opus-dev`)

Làm đủ **V1→V7**. Tự làm bởi `opus-dev`, **chưa qua nghiệm thu/phản biện độc lập** ở thời điểm viết mục này.

#### File đã sửa / tạo

| File | Thay đổi |
|---|---|
| `suite/Shopee.Core/Coordination/KmHoSoGop.cs` | **(V4)** thêm `BienMocTuongLai` (6h), `MocQuaXaTuongLai`, `LocMoc`; `Gop` nhận thêm `DateTimeOffset bayGio` và **lọc trần mốc chạy ở CẢ HAI phía** (đường tự gỡ mốc rác đã nằm trên hub). |
| `server/Shopee.Hub.Web/Data/HubDatabase.KmProfiles.cs` | **(V1 vế 1)** dòng chưa tồn tại ⇒ `dangCo = den` thay vì `new KmProfileItem(login)`; truyền `bayGio` vào luật gộp. |
| `orders/XuLyDonShopee.Core/Data/Database.cs` | **(V1 vế 2)** `BackfillCfgUpdatedAtOnce` — đóng dấu `MocBackfillKmCfg = 2026-01-01T00:00:00` (giờ địa phương, **cố định**, không phải `Now`) cho dòng `km_config` đang có mà mốc còn NULL; chạy **đúng một lần**, chốt bằng khoá `backfill_km_cfg_updated_at_v1` trong `settings` (khuôn `BackfillHubFinalAmountOnce`). |
| `orders/XuLyDonShopee.Core/Data/KmConfigRepository.cs` | Thêm `KmConfig.CungCauHinh` — **một luật dùng cho hai chỗ**: cửa đóng dấu của form (V5) và cửa ghi nhật ký của worker (V3). Cập nhật xmldoc `OwnerMachine` ("đã có người đọc"). |
| `orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs` | **(V2)** `LaMayChuTri` (thuần) + cửa chủ trì trong `NhipLichKmAsync`: máy khác chủ trì ⇒ bỏ qua và **không ghi** `LanThuTuDong`; rỗng / không có hub ⇒ chạy như cũ. `BaoKhongPhaiChuTri` ghi **đúng một dòng mỗi tài khoản mỗi ngày**. **(V5+V6)** đường lưu form gọi `KmDialogViewModel.BanLuuSauForm` với dòng `km_config` **đọc lại lúc lưu**. |
| `orders/XuLyDonShopee.App/ViewModels/KmDialogViewModel.cs` | **(V5+V6)** `BanLuuSauForm` (thuần): không đổi gì ⇒ trả `null` (không lưu, không đóng dấu, không cướp chủ trì); có đổi ⇒ đóng dấu, và hai mốc chạy lấy từ dòng TƯƠI chứ không phải ảnh chụp lúc mở form. Cảnh báo trong xmldoc của `LayCauHinh`. |
| `suite/Shopee.Suite/Infrastructure/OrdersModuleHost.KmSync.cs` | **(V3)** `ApHoSoKmVeMay` ghi `services.Log.Append` khi lượt gộp **đổi cấu hình** (`CauDoiCauHinh`), không đổi ⇒ không log; **(V4)** `NoiMocRacNeuCo` + `CauMocRac` báo mốc rác bị bỏ. `GopVeMay`/`ApHoSoKmVeMay` nhận `bayGio`. `NenThuDayLai` chuyển sang **hàm thuần** nhận mốc hỏng qua tham số. `ChupHoSoKm`/`KmSyncTickAsync` mở `internal` cho test. |
| `server/Shopee.Hub.Web/Program.cs` | Thêm `public partial class Program { }` — chỉ để `WebApplicationFactory<Program>` dựng được host thật. **Không đổi một dòng hành vi nào**, không nới auth. |
| `server/Shopee.Hub.Web.Tests/ZzTempProofTests.cs` | **XOÁ** — đã dời vào `KmHoSoHubTests`. |

Test mới (7 file chạm tới): `KmHoSoHubTests` (+4), `KmRouteHttpTests` (mới, +3), `KmHoSoGopTests` (+6),
`KmCotDongBoTests` (+3), `KmChuTriVaLuuFormTests` (mới, +24), `KmApHoSoVeMayTests` (mới, +17).

#### Build & test THẬT

Cây làm việc **bẩn sẵn** từ đợt 18–19/08 (không commit, không stash — đo trên chính cây đó, đúng phạm vi việc).
Rebuild sạch (`-t:Rebuild`) rồi `dotnet test --no-build`:

```
dotnet build ShopeeSuite.sln       -t:Rebuild → Build succeeded. 0 Warning(s) 0 Error(s)
dotnet build server/ShopeeHub.sln  -t:Rebuild → Build succeeded. 0 Warning(s) 0 Error(s)

XuLyDonShopee.Tests    Failed: 0, Passed: 2020   (baseline 1993 → +27)
Shopee.Core.Tests      Failed: 0, Passed:  191   (baseline  185 → +6)
Shopee.Suite.Tests     Failed: 0, Passed:   28   (baseline   11 → +17)
Shopee.Module.Search   Failed: 0, Passed:   34   (baseline   34 → không đổi)
Shopee.Hub.Web.Tests   Failed: 0, Passed:  183   (baseline  176 → +7)
```

Test ĐỎ do phiên chính dựng (`ZzTempProofTests.DongCu_ChuaTungSua_DayLenHubLanDau_KhongDuocMatCauHinh`,
đo được `Failed: 1, Passed: 0`) nay **XANH**, đã dời vào `KmHoSoHubTests` với cùng tên + xmldoc đầy đủ; file
`Zz…` đã xoá.

#### Thử phá — 22 lượt, tất cả ĐỎ đúng chỗ

| # | Phá gì | Test ĐỎ |
|---|---|---|
| M1 | Hub quay lại `?? new KmProfileItem(login)` (bug vòng 1) | `DongCu_ChuaTungSua_DayLenHubLanDau_KhongDuocMatCauHinh`, `CaBayBuoc_DongCu_QuaMayB_RoiVeMayA_ThiTuDongVanBat` |
| M2 | Bỏ lượt gọi `BackfillCfgUpdatedAtOnce` | `DbCu_SauMigration_DongCuDuocDongDauMocQuaKhu`, `DbCu_ThieuHaiCotDongBo_…` |
| M2b | Backfill chạy MỌI lần khởi động (bỏ cờ `settings`) | `Backfill_ChayDungMotLan_KhongDongDauChoDongGhiSau` |
| M2c | Backfill dùng `DateTime.Now.AddDays(1)` thay mốc cố định | `DbCu_SauMigration_DongCuDuocDongDauMocQuaKhu` |
| M2d | Mốc backfill đặt ở tương lai (2099) | `DbCu_SauMigration_…`, `MocBackfill_ThuaMoiLuotSuaSauDo` |
| M3 | `LocMoc` trả nguyên (bỏ trần mốc tương lai) | 4 test `KmHoSoGopTests` + `MocChayQuaXaTuongLai_ThiBiBo` (hub) + `MocRacTuHub_ThiBoVaGhiNhatKy` (suite) |
| M3b | Chỉ lọc phía `den`, không lọc `dangCo` | `MocRacOPhiaDangCo_CungBiBo` |
| M17 | Bỏ MỌI mốc tương lai (không còn biên 6h) | `MocChay_TuongLaiTrongBien_ThiVanNhan(1h/5h)`, `MocChayTuongLaiTrongBien_ThiVanNhan` (hub) |
| M4a | `LaMayChuTri` luôn `true` (bỏ cửa chủ trì) | `MayKhacChuTri_ThiKhongChay_VaKhongGhiMocThu`, `MayKhacChuTri_ChiGhiMotDongLogMoiDem`, `LaMayChuTri_MaTran(may-khac…)` |
| M4b | Bỏ hai nhánh độ lùi (rỗng ⇒ đứng ngoài) | `ChuTriRong_ThiChayNhuCu`, `KhongCoHub_ThiChayNhuCu_DuHoSoGhiMayKhac`, `LaMayChuTri_ThamSoNull_ThiVanChay`, 2 ca `LaMayChuTri_MaTran` |
| M4c | Cửa chủ trì vẫn ghi `LanThuTuDong` | `MayKhacChuTri_ThiKhongChay_VaKhongGhiMocThu` |
| M4d | Bỏ chặn "một dòng log mỗi đêm" | `MayKhacChuTri_ChiGhiMotDongLogMoiDem` |
| M5 | `BanLuuSauForm` đóng dấu vô điều kiện (bug V5) | `LuuForm_KhongDoiGi_ThiKhongLuu_KhongDoiChuTri` |
| M6 | Lấy hai mốc chạy từ FORM (bug V6) | `LuuForm_KhongLamLuiHaiMocChay`, `LuuForm_KhongDoiGi_…` |
| M7 | `CungCauHinh` bỏ qua `OwnerMachine` | `CungCauHinh_DoiChuTri_ThiKhacNhau` |
| M8 | `CungCauHinh` tính cả `CfgUpdatedAt` | `CungCauHinh_BoQuaMocChay_VaBoQuaMocSua` |
| M9 | Bỏ dòng nhật ký khi gộp đổi cấu hình (hiện trạng vòng 1) | `DoiCauHinh_ThiGhiDungMotDongNhatKy` |
| M10 | Log vô điều kiện mỗi lượt ghi (spam 60s) | `ChiMocChayTien_ThiGhiDbNhungKhongLog` |
| M11 | `ApHoSoKmVeMay` đổi sang khuôn `GetAll()+TryGetValue` | 6 test, trong đó `ChuaCoDong_ThiTaoDongMoi` — **đúng lỗ phủ V7 nêu** |
| M12 / M15 / M16 | `ChupHoSoKm` đẩy cả tài khoản chưa đặt lịch · không bỏ tài khoản thiếu login · `ApHoSoKmVeMay` không bỏ login lạ | `ChupHoSoKm_ChiLayTaiKhoanDaCoDong`+`_KhongCoDongNao_ThiRong` · `ChupHoSoKm_BoTaiKhoanKhongCoLogin` · `LoginLaTrenHub_ThiBoQua_KhongDeTaiKhoanMoi` |
| M13 | `NenThuDayLai` luôn `true` | `NenThuDayLai_VuaHong_ThiCho` |
| M14 | Bỏ cửa degrade "hub chưa cấu hình" trong `KmSyncTickAsync` | `ChuaCauHinhHub_ThiNhipWorkerKhongDungKmConfig` |
| M18 | Cảnh báo mốc rác vô điều kiện | `MocBinhThuong_ThiKhongCanhBao`, `ChiMocChayTien_…` |
| M-route | Đổi đường dẫn `MapGet(HubRoutes.KmProfiles)` | `PostRoiGet_DiQuaRouteThat_TraDungHoSo` |

**Ba lượt phá làm lại** (vòng 1 khai "không chứng minh được đỏ", phản biện nói phá được):

- `Gop` luôn lấy `dangCo.Login` ⇒ `Gop_GiuLogin_KhongDeRong` ĐỎ.
- `Max` trả `DateTimeOffset.MinValue` thay `null` ⇒ `MocChay_CaHaiNull_ThiNull` ĐỎ (+2 test khác).
- `VeMay` trả `DateTime.MinValue` thay `null` ⇒ `DoiMuiGio_Null_ThiNull` ĐỎ.

> **Bẫy của chính lượt thử phá, ghi lại kẻo lần sau dính:** kịch bản khôi phục ban đầu dùng `shutil.move` bản
> `.bak` về chỗ cũ — thao tác đó giữ **mtime CŨ**, MSBuild thấy nguồn cũ hơn DLL vừa build (bản đột biến) nên
> **không build lại**, và bản đột biến sống tiếp sang lượt đo sau. Triệu chứng là 4 test suite "đỏ" hoàn toàn
> vô lý ở lượt M7. Đã sửa thành ghi đè bằng `write`, rebuild sạch cả hai solution, đo lại toàn bộ — con số ở
> trên là con số SAU khi sửa.

#### Việc thêm (nghiệm thu khuyến nghị) — LÀM ĐƯỢC, không nới auth

`grep -rln WebApplicationFactory server/` trước đợt này ra **rỗng**. Nay có
`server/Shopee.Hub.Web.Tests/KmRouteHttpTests.cs`: dựng host thật, gọi `GET`/`POST /orders/km` bằng
`HttpClient`. Chi phí: một `public partial class Program { }` + gói `Microsoft.AspNetCore.Mvc.Testing`.
**Auth giữ nguyên**: test seed token qua `HubDatabase.SetSetting(SettingKeys.ApiToken, …)` (đúng việc admin
làm trên trang cài đặt) rồi gửi header `X-Api-Token` như client thật; thư mục dữ liệu tạm truyền qua
`UseSetting("Hub:DataDir", …)` chứ **không** dùng biến môi trường (env là trạng thái toàn tiến trình, hai bộ
test song song sẽ giẫm lên nhau). Có ca `KhongCoToken_Thi401` khoá đúng điều đó — bộ test này không được
phép âm thầm biến thành cái cớ nới lỏng xác thực. Pha C thêm 2 route thì đã có khuôn sẵn.

#### Lệch so với spec / điểm cần phiên chính soi lại

1. **V4 giữ đúng biên 6 giờ đề xuất.** Lý do ghi trong xmldoc `BienMocTuongLai`: mốc chạy hợp lệ luôn ở quá
   khứ, phần đệm chỉ để nuốt lệch đồng hồ thật (lệch timezone là bội số 1 giờ, nặng nhất ±7 — nhưng ±7 rơi
   vào phía nào cũng còn dưới 6h nếu tính từ mốc quá khứ; lệch NTP vài giây). 6h nằm gọn dưới chu kỳ ngắn
   nhất (1 ngày) nên không nuốt nhầm mốc thật.
2. **V4 log ở phía CLIENT, hub kẹp trần nhưng KHÔNG log.** `HubDatabase` không có hạ tầng log nào (cả file
   `HubDatabase.KmProfiles.cs` không có một dòng log). Hệ quả: máy sai đồng hồ đẩy mốc rác lên thì hub lặng lẽ
   bỏ, và **chính máy sai đó** không thấy cảnh báo (mốc rác của nó không "quá xa" theo đồng hồ của nó). Máy
   khác kéo về mà gặp mốc rác thì có log. Nếu muốn cảnh báo tận nơi thì phải mở một đường log ở tầng hub —
   ngoài phạm vi vòng này, đề xuất phiên chính quyết.
3. **V4 KHÔNG kẹp trần `CfgUpdatedAt`** — cố ý. Nó cũng là bánh cóc (mốc 2028 khoá cứng cấu hình vĩnh viễn),
   nhưng bỏ nó về `null` nghĩa là **vứt trọn cấu hình của bản đó**, ngữ nghĩa khác hẳn "bỏ một mốc sự kiện".
   Plan chỉ nói `LanChayCuoi`. **Đây là một lỗ còn lại, đề nghị đưa vào danh sách phản biện vòng sau.**
4. **V5 có một đánh đổi người dùng cần biết:** nay muốn đổi máy chủ trì thì phải **thực sự sửa** một giá trị
   trên form; mở ra rồi bấm Đóng nguyên xi sẽ không cướp chủ trì nữa. Người dùng chốt "đổi chủ trì = mở form ở
   máy mới" — câu đó nay đúng theo nghĩa "mở form và LƯU thay đổi". Không có đường nào khác mà không mở lại
   đúng bước 5–6 của ca V1. Nếu người dùng muốn một nút "nhận chủ trì máy này" tường minh thì đó là việc mới.
5. **V5 kéo theo:** mở form cho tài khoản **chưa có** dòng `km_config` rồi bấm Đóng mà không sửa gì ⇒ **không
   tạo dòng nữa** (trước đây có). Đúng ý đồ "không đẻ hồ sơ rác", nhưng là đổi hành vi, ghi ra đây cho rõ.
6. **Mốc backfill lưu theo GIỜ ĐỊA PHƯƠNG** (`2026-01-01T00:00:00`, `Kind = Unspecified`) đúng quy ước của
   bảng `km_config`, nên trên dây nó thành `2025-12-31T17:00Z` ở múi giờ VN. Plan viết `2026-01-01T00:00:00Z`.
   Chọn giờ địa phương để **không mở cửa đổi múi giờ thứ ba** ngoài `KmMocThoiGian`. Máy ở hai múi giờ khác
   nhau sẽ ra hai mốc UTC lệch vài giờ — vô hại vì cả hai đều thua mọi lượt sửa thật (cách hàng tháng).

#### Phát hiện NGOÀI phạm vi (KHÔNG tự sửa)

- **`KmMocThoiGian.LenDay(DateTime.MinValue)` NÉM `ArgumentOutOfRangeException`** ở múi giờ dương (UTC+7):
  `new DateTimeOffset(SpecifyKind(MinValue, Local))` quy về UTC là năm 0. Hiện **không đường nào ghi
  `MinValue` vào `km_config`** nên chưa phát tác — nhưng nếu có (sửa tay DB, một bản vá sau trả `MinValue`
  thay `null`) thì cả hai chiều đồng bộ chết câm: `ChupHoSoKm` ném trong `try` của `DayHoSoKmAsync`,
  `GopVeMay` ném trong `try` của `KeoHoSoKmAsync`, cả hai chỉ ra `Trace.WriteLine` — người dùng không thấy gì.
  Lượt thử phá của chính đợt này dựng lại được đúng ca đó. Đề nghị: `LenDay` chặn `MinValue`/`MaxValue` trả
  `null`, kèm test. (Chưa sửa — ngoài phạm vi V1→V7.)
- `KmCotDongBoTests.MocBackfill_ThuaMoiLuotSuaSauDo` **không đỏ** khi bỏ hẳn backfill (M2) — nó chỉ đỏ khi mốc
  backfill bị đẩy ra tương lai (M2d). Vế "dòng ghi mới thắng backfill" của tiêu chí nghiệm thu do test đó cộng
  với `DbCu_SauMigration_DongCuDuocDongDauMocQuaKhu` gánh chung, không phải một mình nó.
- `DanhSachRong_ThiKhongLamGi` là test guard rẻ tiền, **chưa dựng được lượt phá riêng** (bỏ guard
  `hoSo.Count == 0` thì vòng lặp vẫn ra 0). Giữ vì nó rẻ, nhưng đừng tính là một lượt phủ.

#### Chưa làm (đúng phạm vi được giao)

Pha B, pha C (trừ đúng cửa chủ trì C1 đã kéo về theo V2), pha D. **Không commit, không push.**


### Vòng 3 — sửa sau phản biện vòng 2 (22/08/2026, `opus-dev`)

Làm đủ **W1→W13**. Tự làm bởi `opus-dev`, **chưa qua nghiệm thu/phản biện độc lập** ở thời điểm viết mục này.

#### File đã sửa / tạo

| File | Thay đổi |
|---|---|
| `orders/XuLyDonShopee.Core/Services/KmLichTuDong.cs` | **(W1+W2)** thêm `NenNhuongKhung(cf, mayNay, mocDuKien, bayGio)` — ba điều kiện: bật tự động · **máy này chủ trì** (chặn tạm pha A, xmldoc nêu đích danh **hạng mục B1** phải gỡ) · **đêm nay thực sự tới hạn** theo `MocChayKe`. **`LaMayChuTri` DỜI từ `AccountsViewModel` sang đây** (Core) vì nay cả nhịp lịch lẫn cửa nhường khung đều dùng. `HoanVongCheckDon` đổi tên tham số `kmTuDong` → `nhuongKhung` + xmldoc "hàm này chỉ biết DỜI MỐC". |
| `orders/XuLyDonShopee.App/Services/AccountSession.cs` | **(W1+W2)** cờ nhường khung nay là `KmLichTuDong.NenNhuongKhung(...)` chứ không phải `KmConfig.Get(_accountId).TuDong`. |
| `orders/XuLyDonShopee.Core/Data/KmConfigRepository.cs` | **(W3)** `BackfillOwnerMachineOnce(mayNay)` — đóng dấu chủ trì cho dòng `tu_dong = 1` chưa có chủ trì, chạy ĐÚNG MỘT LẦN (khoá `backfill_km_owner_machine_v1` trong `settings`), `mayNay` rỗng ⇒ không làm gì **và không đánh dấu đã chạy**. Cập nhật xmldoc `OwnerMachine` (hai cửa đọc). |
| `orders/XuLyDonShopee.App/ViewModels/KmDialogViewModel.cs` | **(W4)** `BanNap`, `_mayNay`, `DaBamNhanChuTri`, `CoTheNhanChuTri`, `MoTaChuTri`, lệnh `NhanChuTri`. **(W5)** `BanLuuSauForm(tuoi, banNap, tuForm, mayNay, luc, nhanChuTri)` — "có sửa không" so `tuForm` với **`banNap`**, `tuoi` chỉ để lấy hai mốc chạy; thêm `CauCanhBaoSauForm` (trả `null` = không có gì phải nói). |
| `orders/XuLyDonShopee.App/Views/KmDialog.xaml` | **(W4)** khối "Máy chủ trì" + nút **"Máy này nhận chủ trì"** (`ChuTriText`, `NhanChuTriButton`). |
| `orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs` | **(W4)** câu log "không phải chủ trì" nay chỉ đúng nút có thật; `MoFormKm` truyền `mayNay` vào VM, gọi `BanLuuSauForm` chữ ký mới và **ghi dòng cảnh báo W5**. `LaMayChuTri` đã dời sang `KmLichTuDong`. |
| `suite/Shopee.Core/Coordination/KmHoSoGop.cs` | **(W7)** `KepMocSua` — mốc SỬA quá xa tương lai **KẸP VỀ `bayGio`** (không bỏ về `null`), kẹp ở **cả hai phía TRƯỚC khi so**. **(W8)** `CungCauHinh(KmProfileItem…)`, `CanPhaHoa(...)` (cờ để nơi gọi ghi log) và `ThangKhiHoaMoc` — cùng mốc mà khác cấu hình ⇒ bản `TuDong = true` thắng, hoà tiếp thì `OwnerMachine` nhỏ hơn thắng, cấu hình y hệt ⇒ giữ bản đang có (idempotent). |
| `suite/Shopee.Suite/Infrastructure/OrdersModuleHost.KmSync.cs` | **(W6)** `NoiMocRacNeuCo` + `NoiPhaHoaNeuCo` chuyển lên **TRƯỚC** cửa `if (moi == cu) continue;`, chặn lặp theo NGÀY bằng `NenBaoMotLanMoiNgay` (sổ truyền vào được ⇒ test cách ly). **(W8)** `CauPhaHoa`. **(W11)** `CauMocRac` nêu cả hai khả năng sai đồng hồ (máy khác nhanh giờ / **chính máy này chậm giờ**) và nói rõ hệ quả "lượt KM kế có thể chạy sớm hơn chu kỳ". **(W3)** `WireKmSync` gọi `BackfillOwnerMachineOnce(LeaseMachineId)` trước khi bật timer; `WireKmSync` mở `internal`. **(W9)** `DungNhipHoSoKm()` + `NhipHoSoKmDangChay`. |
| `suite/Shopee.Suite/Infrastructure/OrdersModuleHost.cs` | **(W9)** `StopAsync` gọi `DungNhipHoSoKm()` cạnh `_gsheetTimer`/`_mirrorTimer`. |
| `suite/Shopee.Core/Coordination/HubDtos.cs` | **(W10)** xmldoc `KmProfileItem.OwnerMachine` — bỏ câu "pha A chưa ai ĐỌC trường này", nêu đúng hai cửa đọc. |
| `orders/XuLyDonShopee.App/XuLyDonShopee.App.csproj` | `InternalsVisibleTo` thêm `Shopee.Suite.Tests` — để ca nâng cấp W3 dựng được CẢ HAI tầng trong một test (xem "Lệch so với spec" mục 2). |
| `suite/Shopee.Suite.Tests/Shopee.Suite.Tests.csproj` + `data-dir.txt` | Cách ly kho dữ liệu khi chạy test (khuôn `Shopee.Core.Tests`) — `WireKmSync` đọc `MachineIdentity.Shared`, tức `machine.json` THẬT của máy đang chạy nếu không cách ly. |

Test mới: `KmNhuongKhungTests` (mới, 12), `KmNhanChuTriVaSoDungCapTests` (mới, 13 — gồm 2 ca dựng `KmDialog`
THẬT), `KmCotDongBoTests` (+4), `KmChuTriVaLuuFormTests` (+1 test + 4 assert trực tiếp cho W12),
`KmHoSoGopTests` (+10), `KmVongBaWorkerTests` (mới, 8), `KmHoSoHubTests` (+2).

#### Build & test THẬT

Cây làm việc **bẩn sẵn** từ đợt 18–19/08 (không commit, không stash — đo trên chính cây đó, đúng phạm vi việc).
Rebuild sạch (`-t:Rebuild`) rồi `dotnet test --no-build`:

```
dotnet build ShopeeSuite.sln       -t:Rebuild → Build succeeded. 0 Warning(s) 0 Error(s)
dotnet build server/ShopeeHub.sln  -t:Rebuild → Build succeeded. 0 Warning(s) 0 Error(s)

XuLyDonShopee.Tests    Failed: 0, Passed: 2050   (baseline vòng 2: 2020 → +30)
Shopee.Core.Tests      Failed: 0, Passed:  201   (baseline  191 → +10)
Shopee.Suite.Tests     Failed: 0, Passed:   36   (baseline   28 → +8)
Shopee.Module.Search   Failed: 0, Passed:   34   (baseline   34 → không đổi)
Shopee.Hub.Web.Tests   Failed: 0, Passed:  185   (baseline  183 → +2)
```

#### Thử phá — 34 lượt, tất cả ĐỎ đúng chỗ

Kịch bản khôi phục **ghi đè nội dung** (mtime mới) + rebuild giữa các lượt — đúng bài học vòng 2.

| # | Phá gì | Test ĐỎ |
|---|---|---|
| P1 | `NenNhuongKhung` bỏ cửa chủ trì (bug W1) | `MayKhongChuTri_ThiKhongNhuong_VaMocGiuNguyen` |
| P2 | `NenNhuongKhung` bỏ vế `MocChayKe` (bug W2) | `ChuKy7Ngay_ChuaToiHan_ThiKhongNhuong`, `ToiHanDemMai_ThiDemNayKhongNhuong` |
| P3 | `NenNhuongKhung` bỏ vế `TuDong` | `TatKmTuDong_ThiKhongNhuong` |
| P4 | `HoanVongCheckDon` bỏ qua cờ nhường (dời MỌI mốc trong biên an) | 2 ca `KmNhuongKhungTests` + `HoanVongCheckDon_TatKmTuDong_ThiKHONG_chan_gi_ca` |
| P5 | `BackfillOwnerMachineOnce` không làm gì (bug W3) | `DbCu_DangBatKm_ThiBackfillDongDauChuTri_DongTatThiVanRong`, `Backfill_KhongCoHub_ThiKhongLamGi_VaVanChayDuocSau` |
| P6 | Backfill đóng dấu cả dòng `tu_dong = 0` | `DbCu_DangBatKm_…_DongTatThiVanRong` |
| P7 | Backfill bỏ cờ `settings` (chạy mọi lần khởi động) | `BackfillChuTri_ChayDungMotLan_KhongDongDauChoDongGhiSau` |
| P8 | `BanLuuSauForm` dựng bản so từ `tuoi` thay vì `banNap` | **GREEN lượt đầu** ⇒ đẻ thêm test `MayThuBaNhanChuTriTrongLucMoForm_KhongSuaGi_ThiKhongLuu`, chạy lại ⇒ ĐỎ |
| P8b | `BanLuuSauForm` so `tuoi` với bản form (bug W5 nguyên bản) | `MayKhacDoiTrongLucMoForm_NguoiDungKhongSuaGi_ThiKhongLuu` |
| P9 | `BanLuuSauForm` bỏ vế `nhanChuTri` (bug W4) | `BamNhanChuTri_ThiLuu_DuCauHinhKhongDoi` |
| P10 | `CauCanhBaoSauForm` luôn trả `null` | `CanhBao_KhiMayKhacDoiGiuaChung` |
| P11 | Lệnh `NhanChuTri` bỏ cờ | `BamNhanChuTri_…`, `MoTaChuTri_NeuTenMayDangGiu_VaDoiSauKhiBam` |
| P12 | `CoTheNhanChuTri` bỏ kiểm `mayNay` rỗng | `KhongCoHub_ThiNutTat` |
| P13 | `BaoKhongPhaiChuTri` quay lại câu log CŨ ("mở form … và lưu lại") | `MayKhacChuTri_ChiGhiMotDongLogMoiDem` |
| P14 | Cửa chủ trì trong `NhipLichKmAsync` luôn cho chạy | `MayKhacChuTri_ThiKhongChay_VaKhongGhiMocThu`, `MayKhacChuTri_ChiGhiMotDongLogMoiDem` |
| P15 | `KepMocSua` trả nguyên (bỏ trần mốc SỬA — bug W7) | `MocSuaQuaXaTuongLai_ThiKepVeBayGio_VaGiuNguyenCauHinh`, `MocSuaRacOPhiaDangCo_CungBiKep` |
| P16 | `KepMocSua` **bỏ về `null`** thay vì kẹp về `bayGio` | 2 test trên (đúng vế "không được vứt cấu hình") |
| P17 | `ThangKhiHoaMoc` luôn giữ `dangCo` (luật CŨ — bug W8) | 4 test `KmHoSoGopTests` gồm `HoaMoc_ChuoiDayKeo_ThiCaBaBenHoiTu` |
| P18 | Phá hoà bỏ ưu tiên `TuDong` | `HoaMoc_BanBatTuDongThang_DuODauVao`, `HoaMoc_ChuoiDayKeo_…` |
| P19 | `CanPhaHoa` luôn `false` (cờ ghi log tắt hẳn) | `CanPhaHoa_ChiBatKhiCungMocMaKhacCauHinh` |
| P20 | `CungCauHinh(KmProfileItem)` bỏ qua `OwnerMachine` | **GREEN lượt đầu** ⇒ đẻ thêm `HoaMoc_CungCauHinh_KhacChuTri_ThiVanHoiTuVeMotChuTri`, chạy lại ⇒ ĐỎ |
| P21 | Trần mốc sửa vô hiệu — đo **phía HUB** | `KmHoSoHubTests.MocSuaQuaXaTuongLai_ThiHubKepVeBayGio` |
| P22 | Phá hoà vô hiệu — đo **phía HUB** | `KmHoSoHubTests.CungMocBackfill_KhacCauHinh_ThiBanBatKmThang_DuAiDayTruoc` |
| P23 | `KmSync` đưa hai cảnh báo lại SAU cửa thoát (bug W6) | `MocRac_MayDaCoDongKhopSan_ThiVanCanhBao_VaChiMotDongMoiNgay` |
| P24 | Bỏ chặn "một dòng mỗi ngày" (spam 60 giây) | `MocRac_MayDaCoDongKhopSan_…` |
| P25 | Bỏ hẳn dòng log phá hoà | `PhaHoa_ThiGhiDungMotDongNhatKyMoiNgay` |
| P26 | `CauMocRac` bỏ vế "CHÍNH MÁY NÀY" (W11) | `CauMocRac_NeuCaHaiKhaNangSaiDongHo` |
| P27 | `DungNhipHoSoKm` không dispose (bug W9) | `Teardown_DungNhipHoSoKm` |
| P28 | `WireKmSync` bỏ lượt gọi backfill chủ trì | **GREEN lượt đầu** ⇒ đẻ thêm `WireKmSync_ThiChayBackfillChuTriChoDongCu`, chạy lại ⇒ ĐỎ |
| P29 | Phá hoà đảo chiều so id máy (`>=` thay `<=`) | 3 test `KmHoSoGopTests` |
| P30 | XAML bỏ ràng buộc `IsEnabled` của nút nhận chủ trì | `KhongCoHub_ThiNutTat_TrenFormThat`, `NutNhanChuTri_HienThat_TrenFormThat` |
| P31 | XAML bỏ dòng chữ `MoTaChuTri` | `NutNhanChuTri_HienThat_TrenFormThat` |
| P32 | XAML bỏ `Command` của nút (nút thành đồ trang trí) | `NutNhanChuTri_HienThat_TrenFormThat` |

> **Ba lượt GREEN (P8 · P20 · P28) là phần đáng giá nhất của vòng thử phá này** — cả ba đều là lỗ phủ THẬT, và
> cả ba cùng một hình dạng "hai nửa đều xanh, chỗ nối thì không ai đo": P8 = so sai cặp khi CHỈ `OwnerMachine`
> đổi (máy thứ ba nhận chủ trì lúc form đang mở); P20 = hai máy cùng cấu hình khác chủ trì thì fleet bất đồng
> vĩnh viễn; P28 = hàm backfill đúng nhưng **không ai gọi**. Đã thêm test cho cả ba rồi phá lại.

#### Lệch so với spec / điểm cần phiên chính soi lại

1. **W3 KHÔNG nằm trong `Database.Initialize`** như câu chữ tiêu chí nghiệm thu ("sau `Initialize`,
   `owner_machine` = máy này"). Không làm được: tầng `XuLyDonShopee.Core` **không biết máy này là máy nào** — id
   suất đơn hàng do shell suite rót qua `AppServices.MayNay`, và đó chính là lý do hook ấy tồn tại. Đặt ở
   `KmConfigRepository.BackfillOwnerMachineOnce`, gọi từ `OrdersModuleHost.WireKmSync` ngay sau khi rót hook và
   TRƯỚC lượt đẩy đầu tiên. Chỗ nối này có test riêng (`WireKmSync_ThiChayBackfillChuTriChoDongCu`, sinh ra từ
   lượt phá P28).
2. **Mở `InternalsVisibleTo` của `XuLyDonShopee.App` cho `Shopee.Suite.Tests`.** Tiêu chí W3 đòi ca nâng cấp
   assert "máy hai **không chạy** lượt KM" — mà `NhipLichKmAsync` là `internal` của module Đơn hàng còn worker
   đẩy/kéo chỉ có ở `Shopee.Suite`. Tách đôi thì mỗi nửa xanh riêng trong khi chỗ nối vẫn hỏng.
3. **Ca nâng cấp W3 mô phỏng lượt "đẩy lên hub" bằng chính `KmHoSoGop.Gop`** thay vì gọi `HubDatabase` thật
   (`Shopee.Suite.Tests` không tham chiếu project hub, và hub LINK sẵn file nguồn của `XuLyDonShopee.Core` nên
   ref thẳng sẽ trùng kiểu CS0433). Hub gọi đúng hàm đó, và với dòng CHƯA TỒN TẠI thì `UpsertKmProfiles` truyền
   `dangCo = den` — tức lượt đẩy đầu tiên là phép đồng nhất. Vế hub được đo riêng bằng P21/P22 trên
   `KmHoSoHubTests` (dữ liệu cùng hình dạng).
4. **Đổi hai chữ ký + một chỗ ở của hàm, kéo theo sửa test CŨ (không phải sửa test cho vừa bug):**
   `HoanVongCheckDon(moc, kmTuDong:)` → `nhuongKhung:` (6 chỗ trong `KmLichTuDongTests`);
   `AccountsViewModel.LaMayChuTri` → `KmLichTuDong.LaMayChuTri` (3 chỗ);
   `BanLuuSauForm` thêm tham số `banNap` (4 chỗ). Cả ba đều là đổi tên/thêm tham số, không nới lỏng assert nào.
5. **MỘT test cũ bị đổi KHẲNG ĐỊNH vì luật đổi:** `KmHoSoGopTests.CauHinh_MocBangNhau_GiuBanDangCo_…` khoá đúng
   câu "bằng nhau ⇒ giữ bản đang có" mà W8 bác bỏ. Nay là
   `CauHinh_MocBangNhau_PhaHoaCoThuTu_VaGopHaiLanRaMotKetQua`, assert MẠNH HƠN (thêm vế "đảo chiều ra cùng kết
   quả") và xmldoc ghi rõ vì sao luật đổi. **Đề nghị phiên chính soi kỹ đúng chỗ này.**
6. **`KmApHoSoVeMayTests.MocRacTuHub_ThiBoVaGhiNhatKy` được truyền sổ cảnh báo RIÊNG.** Sổ mặc định là `static`
   dùng chung cả assembly mà cảnh báo chặn lặp theo NGÀY ⇒ để nguyên thì thứ tự chạy test quyết định xanh/đỏ.
   Không nới assert nào.
7. **W2 CHƯA tính tới `DaThuTrongKhungNay`.** Lượt KM đêm nay đã thử mà HỎNG (không ghi `LanChayCuoi`) thì vòng
   check đơn vẫn nhường tới 06:00 dù KM không thử lại nữa. Đúng hành vi trước pha A nên không phải hồi quy, và
   plan chỉ nói `MocChayKe` — ghi ra để phiên chính quyết có làm tiếp không.
8. **W11 làm đúng phạm vi "chỉ một dòng log":** không thiết kế lại `LocMoc`. Máy chậm giờ > 6h vẫn bỏ mốc đúng
   của hub (⇒ có thể chạy KM sớm hơn chu kỳ), nay ít nhất có dòng nhật ký nói đúng cả hai khả năng.
9. **W13 (ghi nợ spec vòng 1) — khai lại cho đủ:** `login TEXT PRIMARY KEY **COLLATE NOCASE**` trong
   `HubDatabase.KmProfiles.cs` là ĐÚNG và có test, plan mục A3 nay đã sửa. Vòng này không đụng schema hub.
10. **Cảnh báo W5 đi qua NHẬT KÝ, không phải hộp thoại.** Form đã đóng ở thời điểm đó nên MessageBox là chen
    ngang; nhật ký là kênh sẵn có và hiện ngay trên panel. Nếu người dùng muốn hộp thoại thì đó là việc mới.

#### Chỗ CHƯA có test, khai thẳng

- **Chỗ nối `AccountSession` → `NenNhuongKhung`.** Cờ nhường khung nằm giữa `RunBridgeContinuousAsync` (cần cầu
  nối + trình duyệt thật) nên không có test nào chứng minh nó truyền đúng `mayNay`/`bayGio`. Luật thuần thì có
  12 test + 4 lượt phá. Lỗ này có từ trước vòng 3 — nêu ra để phiên chính biết, chưa sửa.
- **`StopAsync` có gọi `DungNhipHoSoKm` hay không.** Test khoá được hàm dừng nhịp (P27 ĐỎ), nhưng `StopAsync`
  đụng `Services` static + `StopAllAsync` nên không dựng được trong test; bỏ dòng gọi trong `StopAsync` thì
  không test nào đỏ.
- **Chưa chạy app thật** để nhìn nút "Máy này nhận chủ trì" bằng mắt. Bù bằng 2 test dựng `KmDialog` THẬT (đo
  `IsEnabled` + `Text` sau một lượt layout thật) và 3 lượt phá XAML P30–P32.

#### Chưa làm (đúng phạm vi được giao)

Pha B (nhả khoá — **và nó phải GỠ vế chủ trì trong `NenNhuongKhung`**, xem hạng mục B1), pha C, pha D.
**Không commit, không push.**


### Vòng 4 — VÒNG CUỐI của pha A (22/08/2026, `opus-dev`)

Làm đủ **X1→X7**. Tự làm bởi `opus-dev`, **chưa qua nghiệm thu/phản biện độc lập** ở thời điểm viết mục này.

#### File đã sửa / tạo

| File | Thay đổi |
|---|---|
| `suite/Shopee.Core/Coordination/HubDtos.cs` | **(X1)** DTO mới `KmProfilesPushResponse` — `Profiles` (giữ đúng tên trường của body cũ) + `HoSoDaGop` (bản đã gộp), **cả hai là property `init` trong thân record**, `HoSoDaGop` khởi tạo `[]` nên body của hub CŨ (`{"profiles": n}`) parse ra danh sách RỖNG chứ không null. |
| `server/Shopee.Hub.Web/Data/HubDatabase.KmProfiles.cs` | **(X1)** thêm `UpsertKmProfilesTraVe` trả `List<KmProfileItem>` (bản đã gộp của từng dòng vừa ghi); `UpsertKmProfiles` cũ nay chỉ là `…TraVe(r).Count` — **không đổi chữ ký cũ**, 13 test hub sẵn có không phải sửa. |
| `server/Shopee.Hub.Web/Api/ClientApiEndpoints.cs` | **(X1)** `POST /orders/km` trả `KmProfilesPushResponse` thay cho `new { profiles = n }`. |
| `suite/Shopee.Core/Coordination/HubClient.cs` | **(X1)** `PushKmProfilesAsync` nay trả `List<KmProfileItem>?`; tách cửa đọc body thành hàm THUẦN `internal static DocBanGopKm(string? json)` — `null`/rỗng/HTML/JSON sai hình dạng ⇒ `null`, **tuyệt đối không ném** (ném ở đó là tự biến một lượt đẩy THÀNH CÔNG thành lượt hỏng). `EnsureSuccessStatusCode` giữ nguyên nên hub cũ 404 vẫn đi đường "thử lượt sau" như trước. |
| `suite/Shopee.Suite/Infrastructure/OrdersModuleHost.KmSync.cs` | **(X1)** `ApBanHubTraVe` — ghi ĐÈ `km_config` bằng bản hub trả về (không gộp lại: lượt gộp chạy trên đồng hồ của chính máy sai giờ nên vô tác dụng), kèm cửa chặn "dòng cục bộ vừa đổi trong lúc request đang bay" và dòng nhật ký `CauHubKepMocSua` (1 dòng/ngày). Tách `ApVaoDong` dùng chung cho cả chiều KÉO lẫn chiều ĐẨY-nhận-lại. **(X3)** `NoiMocRacNeuCo` thêm nhánh `CfgUpdatedAt` + câu `CauMocSuaRac` (nói KẸP, khác hẳn câu của mốc CHẠY vốn nói BỎ). **(X5)** `ChiKhacMocSuaDaKep` — chỉ khác nhau ở riêng cái mốc vừa bị kẹp thì KHÔNG ghi đĩa. **(X4)** `WireKmSync` tách bản 2 tham số `(services, coHub)`; backfill chủ trì chỉ chạy khi `CoordinationRuntime.Client is not null`. **(X6)** bảng tên máy chụp từ `/fleet` (làm mới tối đa 5 phút một lượt, hỏng thì im lặng giữ bảng cũ) + hook `TraTenMay`; `CauDoiCauHinh` nhận thêm tên. **(X7)** cờ `_kmDangThoat` + `KmSyncTickAsync` trả `NhipKm` (DangThoat / DangBanLuotTruoc / ChuaCoHub / DaLam). **(X2)** `CauPhaHoa` viết lại. |
| `suite/Shopee.Suite/Infrastructure/OrdersModuleHost.cs` | **(X7)** tách thân dọn dẹp của `StopAsync` thành `DonDepKhiThoatAsync(svc, vm)` nhận tham số ⇒ chỗ nối "thoát app ⇒ dừng nhịp hồ sơ KM" test được. |
| `orders/XuLyDonShopee.Core/Services/KmLichTuDong.cs` | **(X6)** hàm thuần `NhanMay(machineId, tenMay)` → `"Máy kho 2 (3f8a91)"`, không tra được tên thì `"3f8a91"` — **không bao giờ** id thô kèm `:orders`. |
| `orders/XuLyDonShopee.App/Services/AppServices.cs` | **(X6)** hook `TenMay` (`Func<string,string?>?`). |
| `orders/XuLyDonShopee.App/ViewModels/KmDialogViewModel.cs` | **(X6)** ctor nhận `traTenMay`; `MoTaChuTri` hiện nhãn thay id thô. |
| `orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs` | **(X6)** `BaoKhongPhaiChuTri` hiện nhãn máy; **(X2)** câu đó nói rõ phải bấm "Đóng" sau khi bấm "Máy này nhận chủ trì"; câu "CẤU HÌNH KHÔNG DÙNG ĐƯỢC" đổi từ "Sửa trong form…" thành thao tác đủ ba bước (mở form · sửa · bấm "Đóng"); `MoFormKm` truyền hook tra tên vào VM. |
| `orders/XuLyDonShopee.App/Views/KmDialog.xaml` | **(X2)** tooltip nút "Đóng" nói đúng hành vi thật: không sửa gì thì KHÔNG ghi gì (đây là **câu thứ ba** cùng hình dạng lỗi, tìm ra khi rà). |

Test mới: `KmVongBonTests` (mới, 16), `KmNhanMayTests` (mới, 11), `KmDayNhanLaiDtoTests` (mới, 11),
`KmRouteHttpTests` (+1). Test cũ phải sửa KHẲNG ĐỊNH vì X6 đổi hành vi hiển thị: 3 chỗ (xem mục lệch spec).

#### X2 — DANH SÁCH ĐÃ RÀ (tiêu chí nghiệm thu đòi liệt kê từng chuỗi)

Nút CÓ THẬT trên `KmDialog.xaml`, đo bằng cách dựng form THẬT rồi duyệt visual tree:
**"Chạy chương trình"** · **"Máy này nhận chủ trì"** · **"Đóng"**. (Không có nút "Lưu".)

| # | Chuỗi người dùng đọc | Ở đâu | Nhắc tới thao tác nào | Phán |
|---|---|---|---|---|
| 1 | `CauPhaHoa()` | `OrdersModuleHost.KmSync.cs` | trước: *"mở form … và **LƯU lại** một lần"* | **SAI — đã sửa** thành "sửa thông số rồi bấm **Đóng**", hoặc bấm **"Máy này nhận chủ trì"** rồi **"Đóng"** |
| 2 | `BaoKhongPhaiChuTri` | `AccountsViewModel.Phien.cs` | nút **"Máy này nhận chủ trì"** | ĐÚNG (W4 đã sửa) — **bổ sung** vế "sau đó bấm **Đóng**", vì nút chỉ có hiệu lực khi đóng form |
| 3 | "Lịch KM tự động: … CẤU HÌNH KHÔNG DÙNG ĐƯỢC" | `AccountsViewModel.Phien.cs` | trước: *"Sửa trong form 'KM của Shop'"* | MƠ HỒ — **đã sửa**: mở form · sửa thông số · bấm **"Đóng"** |
| 4 | Tooltip nút **Đóng** | `KmDialog.xaml` | *"Cấu hình được lưu nếu HỢP LỆ"* | **SAI kể từ V5/W5** (mở ra xem rồi đóng thì KHÔNG ghi gì) — **đã sửa**, có test `TooltipNutDong_NoiDungHanhViThat` |
| 5 | Tooltip nút **Máy này nhận chủ trì** | `KmDialog.xaml` | "Có hiệu lực khi đóng form" | ĐÚNG |
| 6 | Tooltip nút **Chạy chương trình** | `KmDialog.xaml` | mô tả hành vi, không hướng dẫn nút | ĐÚNG |
| 7 | `MoTaChuTri` (3 nhánh) | `KmDialogViewModel` | "sẽ ghi khi đóng form" | ĐÚNG |
| 8 | `CauCanhBaoSauForm` (2 nhánh) | `KmDialogViewModel` | "mở lại form" | ĐÚNG |
| 9 | `CauMocRac` | `KmSync.cs` | "Kiểm lại ngày giờ của tất cả các máy" (thao tác HỆ ĐIỀU HÀNH, không phải nút) | ĐÚNG |
| 10 | `CauMocSuaRac` (mới, X3) | `KmSync.cs` | như trên | ĐÚNG |
| 11 | `CauHubKepMocSua` (mới, X1) | `KmSync.cs` | "kiểm lại ngày giờ hệ thống" | ĐÚNG |
| 12 | `CauDoiCauHinh` | `KmSync.cs` | không hướng dẫn gì | ĐÚNG |
| 13 | `CauHoanVong` | `KmLichTuDong.cs` | không hướng dẫn gì | ĐÚNG |

Việc rà này **được khoá bằng test**, không phải chỉ làm một lần bằng mắt:
`KmVongBonTests.MoiCauHuongDan_ChiNhacToiNutCoThat` dựng form THẬT, lấy nhãn mọi nút, rồi với **12 câu lấy qua
đúng đường sinh ra chúng** (kể cả hai dòng chỉ ra đời khi chạy `NhipLichKmAsync`) kiểm hai luật: (a) mọi cụm
trong ngoặc kép phải là tên một nút có thật hoặc nằm trong danh sách ngoại lệ khai tường minh; (b) cấm hẳn cụm
"lưu lại" / "nút lưu" / "bấm lưu" / "và lưu" — vì cụm đó thường viết KHÔNG có ngoặc kép nên luật (a) không thấy.
Test còn tự chống rỗng: nếu không rà được ≥ 10 câu và ≥ 6 cụm thì ĐỎ.

#### Build & test THẬT

Cây làm việc **bẩn sẵn** từ đợt 18–19/08 (không commit, không stash — đo trên chính cây đó).
`-t:Rebuild` sạch rồi `dotnet test --no-build`:

```
dotnet build ShopeeSuite.sln       -t:Rebuild → Build succeeded. 0 Warning(s) 0 Error(s)
dotnet build server/ShopeeHub.sln  -t:Rebuild → Build succeeded. 0 Warning(s) 0 Error(s)

XuLyDonShopee.Tests    Failed: 0, Passed: 2061   (baseline vòng 3: 2050 → +11)
Shopee.Core.Tests      Failed: 0, Passed:  212   (baseline  201 → +11)
Shopee.Suite.Tests     Failed: 0, Passed:   52   (baseline   36 → +16)
Shopee.Module.Search   Failed: 0, Passed:   34   (baseline   34 → không đổi)
Shopee.Hub.Web.Tests   Failed: 0, Passed:  186   (baseline  185 → +1)
```

**Cách ly dữ liệu người dùng còn nguyên** (yêu cầu của phiên chính): sau trọn đợt build + test + 19 lượt thử
phá, `%AppData%\XuLyDonShopee\app.db` vẫn mang mốc **19/08/2026 20:43** và `%AppData%\ShopeeSuite\machine.json`
mốc **10/08/2026 05:52** — không lượt nào chạm vào. `hub.db` không tồn tại trên máy này.

#### Thử phá — 19 lượt

Khôi phục bằng **ghi đè nội dung** (mtime mới) + rebuild giữa các lượt; cuối đợt `grep "PHA P"` toàn repo ra
rỗng và rebuild lại cả hai sln.

| # | Phá gì | Test ĐỎ |
|---|---|---|
| P1 | Hub KHÔNG trả bản đã gộp (danh sách rỗng) | `KmRouteHttpTests.Post_TraVeBanDaGop_VaKepMocSuaRac` |
| P2 | Route trả BODY CŨ `{"profiles": n}` (đúng hình dạng hub cũ) | như trên |
| P3 | Client bỏ hẳn lượt áp bản hub trả về (vòng phản hồi hở như trước X1) | `MaySaiDongHo_DayLenRoiNhanLai_…`, `MaySaiDongHo_ThiGhiDungMotDongNhatKyMoiNgay` |
| P4 | Áp bản hub bằng **GỘP** (đồng hồ máy này) thay vì **GHI ĐÈ** — đúng lỗi X1 nguyên bản | `MaySaiDongHo_DayLenRoiNhanLai_…` |
| P5 | Bỏ cửa "dòng cục bộ vừa đổi trong lúc request đang bay" | `DongCucBoDoiTrongLucRequestDangBay_ThiKhongGhiDe` |
| P6 | `HubDaKepMocSua` luôn false (hub kẹp mốc mà máy nguồn im lặng) | `MaySaiDongHo_ThiGhiDungMotDongNhatKyMoiNgay` |
| P7 | Bỏ nhánh log mốc SỬA bị kẹp (đúng thứ vòng 3 làm thiếu — X3) | `MayChamGio_HaiLuotKeoLienTiep_…` |
| P8 | Bỏ cửa `ChiKhacMocSuaDaKep` ⇒ ghi đĩa mỗi nhịp, im lặng (X5) | `MayChamGio_HaiLuotKeoLienTiep_…` |
| P9 | Cửa X5 chặn **quá tay** (bỏ mọi khác biệt ở riêng mốc sửa, kể cả mốc HỢP LỆ) | **GREEN lượt đầu** ⇒ đẻ thêm `MocSuaMoiHonMaHopLe_DuChiMinhNoKhac_ThiVanGhi`, chạy lại ⇒ ĐỎ |
| P10 | Backfill chủ trì chạy VÔ ĐIỀU KIỆN (hồi quy W3 tự tạo — X4) | `KhongCoHub_ThiWireKmSyncKhongBackfillChuTri_VaKhongDongLatch` |
| P11 | Cổng "có hub" nối sai: bản 1 tham số luôn báo CÓ hub | như trên |
| P12 | `NhanMay` trả id THÔ (hành vi vòng 3) | 10 test: cả `KmNhanMayTests`, `KmNhanChuTriVaSoDungCapTests`, `KmChuTriVaLuuFormTests` |
| P13 | `NhanMay` bỏ id ngắn (chỉ còn tên) | 4 test `KmNhanMayTests` |
| P14 | **Bỏ dòng dừng nhịp hồ sơ KM trong đường dọn dẹp lúc thoát** (đúng tiêu chí X7) | `DonDepKhiThoat_ThiDungNhipHoSoKm` |
| P15 | Nhịp đang bay KHÔNG kiểm cờ "đang thoát" | `DangThoat_ThiNhipTuDungOCuaDauTien` |
| P16 | `CauPhaHoa` quay lại câu "LƯU lại" (X2) | `CauPhaHoa_ThoiKhuyenMotThaoTacKhongTonTai` **và** `MoiCauHuongDan_ChiNhacToiNutCoThat` |
| P17 | Tooltip nút "Đóng" quay lại câu cũ (câu thứ ba của X2) | `TooltipNutDong_NoiDungHanhViThat` |
| P18 | Cửa đọc body NÉM khi hub cũ / body lạ (nhánh degrade X1) | 5 ca `BodyLa_ThiTraKhongCoBanGop_VaKhongNem` |
| P19 | Trường `HoSoDaGop` bỏ giá trị khởi tạo `[]` (phá luật chống lệch bản) | `BodyCuaHubCu_ThiParseDuoc_VaHoSoDaGopRong`, `BodyRong_ThiVanKhongNem` |

> **Lượt GREEN duy nhất (P9) lại đúng là lượt đáng giá nhất**, và cùng hình dạng với ba lượt GREEN vòng 3: bản
> vá đúng ở ca nó sinh ra để chữa, nhưng **quá tay ở ca kế bên** — chặn ghi đĩa cho mọi khác biệt "chỉ mỗi mốc
> sửa" thì mốc sửa HỢP LỆ cũng không bao giờ được lưu, tức làm hỏng đúng trọng tài của luật "máy nào sửa sau
> thắng", mà 51 test còn lại vẫn xanh. Đã thêm test vế đối chứng rồi phá lại.

#### Lệch so với spec / điểm cần phiên chính soi lại

1. **X4 KHÔNG kiểm được nhánh "có hub" bằng `CoordinationRuntime.Client` thật** — nó là static setter private,
   test không gán được. Đã tách `WireKmSync(services, bool coHub)`; bản MỘT tham số (đúng bản production gọi)
   truyền `CoordinationRuntime.Client is not null`. Test gọi **bản một tham số** ở môi trường không hub nên vẫn
   khoá luôn cái cổng đó (P11 ĐỎ); nhánh `coHub: true` đo bằng test vòng 3 đã cập nhật.
2. **X7 làm CẢ HAI vế** plan cho chọn một: thêm cờ "đang thoát" **và** hạ câu xmldoc cho đúng sự thật
   (`Timer.Dispose()` không chờ callback đang bay). Ngoài ra **đổi cấu trúc `StopAsync`**: thân dọn dẹp dời sang
   `DonDepKhiThoatAsync(svc, vm)`, `StopAsync` chỉ còn cờ chống dừng đúp + gọi nó. Nhờ vậy tiêu chí "bỏ dòng gọi
   ⇒ có test ĐỎ" mới thoả được (P14). **Lưu ý cho người soi:** nếu ai đó bỏ chính dòng gọi
   `DonDepKhiThoatAsync` trong `StopAsync` thì vẫn KHÔNG có test nào đỏ — chỗ nối cuối cùng ấy vẫn trần, chỉ là
   nó đã co lại còn một dòng duy nhất trong một hàm 5 dòng.
3. **Ba test CŨ bị đổi KHẲNG ĐỊNH vì X6 đổi hành vi hiển thị** (không phải nới lỏng — cả ba nay assert MẠNH
   HƠN, thêm vế `DoesNotContain` id thô): `KmApHoSoVeMayTests.DoiCauHinh_ThiGhiDungMotDongNhatKy`,
   `KmNhanChuTriVaSoDungCapTests.MoTaChuTri_NeuTenMayDangGiu_VaDoiSauKhiBam` +
   `NutNhanChuTri_HienThat_TrenFormThat`, `KmChuTriVaLuuFormTests.MayKhacChuTri_ChiGhiMotDongLogMoiDem`.
   Và một test vòng 3 đổi lời gọi: `WireKmSync_ThiChayBackfillChuTriChoDongCu` nay gọi
   `WireKmSync(svc, coHub: true)`.
4. **X6 lấy tên máy bằng một lượt gọi `/fleet` mỗi 5 phút** trong nhịp worker KM (chỉ khi có hub). Đây là
   request MẠNG MỚI mà plan không nói tới — chọn vậy vì hai phương án kia đắt hơn: thêm cột `owner_hostname`
   vào cả `km_profiles` lẫn `km_config` (đổi schema hai phía), hoặc để form tự gọi mạng lúc mở (chậm và có thể
   treo UI). Hỏng thì im lặng giữ bảng cũ, hiển thị rơi về id ngắn. **Chưa có test cho lượt làm mới này**
   (cần hub thật) — chỉ hàm thuần `NhanMay` và đường hiển thị là có.
5. **Lượt ĐẨY-nhận-lại KHÔNG chạy `NoiMocRacNeuCo`/`NoiPhaHoaNeuCo`** (hai cảnh báo đó nói về hồ sơ hub gửi
   XUỐNG). Cố ý: mỗi lượt đẩy đều kéo ngay sau đó (`banDay || …`), nên đường KÉO vẫn bắn đủ cảnh báo. Riêng
   cảnh "hub kẹp mốc của CHÍNH MÁY NÀY" thì chỉ lượt đẩy nhìn thấy, nên nó có câu riêng.
6. **Cảnh báo "hub kẹp mốc sửa của máy này" (`CauHubKepMocSua`) là việc THÊM, plan không đòi.** X1 chỉ yêu cầu
   ghi đè; nhưng ghi đè im lặng đúng là hình dạng lỗi mà cả vòng này sinh ra để chống, và máy sai đồng hồ là ca
   người dùng KHÔNG thể tự chẩn đoán (trên máy đó mọi mốc đều trông bình thường). 1 dòng/tài khoản/ngày.
7. **Nhánh degrade X1 đo ở HAI tầng, không phải ở `HubClient` chạy thật:** hợp đồng JSON (`DocBanGopKm` —
   `null`/rỗng/HTML/JSON sai hình dạng đều ra "không có bản gộp") và tầng áp (`ApBanHubTraVe` với `null`/rỗng ⇒
   0 dòng, không log). **Phần HTTP thật của `HubClient` vẫn không có test** — cả repo chưa từng có test nào
   dựng `HubClient` (nó tự tạo `HttpClient` trong ctor, không có chỗ tiêm).
8. **`UpsertKmProfiles` cũ giữ nguyên chữ ký** (trả `int`) và nay uỷ quyền cho `…TraVe`. Không xoá vì 13 test
   hub hiện dùng nó và nó vẫn là API đúng cho nơi chỉ cần đếm.
9. **`KmSyncTickAsync` đổi kiểu trả về** `Task` → `Task<NhipKm>`. Chỗ gọi production là lambda của timer
   (`_ = KmSyncTickAsync(services)`) nên không đổi hành vi; kiểu trả về sinh ra CHỈ để lượt "đang thoát" quan
   sát được (một nhịp không làm gì thì không có tác dụng phụ nào để assert).
10. **X5 chữa được ca của nó, và X1 chữa ca ngược lại — hai chỗ khác nhau, không thay nhau.** X1 (ghi đè bản hub
    trả về) chỉ chạm dòng của lượt ĐẨY từ máy này; X5 nằm ở đường KÉO. Máy chậm giờ > 6h không đẩy gì cũng vẫn
    kéo mỗi 60 giây, nên vẫn cần cửa X5.

#### Chỗ CHƯA có test, khai thẳng

- **Lượt làm mới bảng tên máy từ `/fleet`** (X6) — xem mục lệch spec #4.
- **Dòng gọi `DonDepKhiThoatAsync` trong `StopAsync`** — xem mục lệch spec #2.
- **`HubClient.PushKmProfilesAsync` chạy thật qua HTTP** — xem mục lệch spec #7.
- **Chỗ nối `AccountSession` → `NenNhuongKhung`** — vẫn nguyên như vòng 3 (mục NỢ KỸ THUẬT 1), vòng này không đụng.
- **Chưa chạy app thật.** Bù bằng 3 test dựng `KmDialog` THẬT (đo nhãn nút + tooltip + dòng chữ chủ trì sau một
  lượt layout thật) và các lượt phá XAML/tooltip (P17) + hiển thị (P12, P13).

#### Chưa làm (đúng phạm vi được giao)

Pha B (nhả khoá — **và nó phải GỠ vế chủ trì trong `NenNhuongKhung`**, xem hạng mục B1), pha C, pha D.
Các mục trong **NỢ KỸ THUẬT** giữ nguyên, không đụng. **Không commit, không push.**

---

### VÒNG 5 (22/08/2026) — đóng blocker do chính vòng 4 sinh ra

**Phạm vi:** Y1 · Y2 · Y3 · Y4. Y5 là ghi chú deploy (không phải code). Mục **NỢ KỸ THUẬT** giữ nguyên, không đụng.

#### File đã sửa / tạo

| File | Thay đổi |
|---|---|
| `suite/Shopee.Suite/Infrastructure/OrdersModuleHost.KmSync.cs` | **Y1** — tách `ApVaoDong` làm ba: `ApNhomCauHinh` (private, nhóm cấu hình + `CfgUpdatedAt`), `ApVaoDong` (= nhóm cấu hình + gán thẳng hai mốc chạy, CHỈ cho đường KÉO nơi `KmHoSoGop.Gop` đã chạy với đồng hồ máy này), `ApBanKhepVong` (= nhóm cấu hình + `KmHoSoGop.Max(cục bộ, hub)` cho hai mốc chạy). `ApBanHubTraVe` đổi sang gọi `ApBanKhepVong`. **Y2** — `services.MayNay = () => LeaseMachineId` chuyển vào trong `if (coHub)`. **Y4** — hạ câu xmldoc `_kmDangThoat` + `DungNhipHoSoKm` + comment cửa đầu `KmSyncTickAsync` xuống đúng phạm vi thật. |
| `suite/Shopee.Suite.Tests/KmVongNamTests.cs` *(mới)* | 6 test cho Y1 (4) và Y2 (2). Helper `Dong(...)` bắt hai mốc chạy làm **tham số BẮT BUỘC** — đúng chỗ helper vòng 4 để lọt blocker. |
| `suite/Shopee.Suite.Tests/KmVongBonTests.cs` | **Y3** — `CacCauNguoiDungDoc()` đổi kiểu trả về thành `(Ham, Cau)`, thêm `CauBatDauChay` + `CauHoanVong` (hai câu trước nay nằm ngoài lưới); `MoiCauHuongDan_ChiNhacToiNutCoThat` gom thêm `ToolTip` của **mọi** `Button` trên form thật; thêm test mới `MoiHamSinhCau_DeuPhaiNamTrongVongRaChuoi` dùng **phản chiếu** — mọi `static string Cau*` của `OrdersModuleHost` / `KmDialogViewModel` / `KmLichTuDong` chưa được liệt kê ⇒ ĐỎ kèm tên hàm. |

#### Build / test THẬT (cây làm việc bẩn 18–19/08, đã `-t:Rebuild` cả hai sln trước khi đo)

| | Vòng 4 | Vòng 5 |
|---|---|---|
| `ShopeeSuite.sln` rebuild | 0 warning | **0 warning / 0 error** |
| `server/ShopeeHub.sln` rebuild | 0 warning | **0 warning / 0 error** |
| `XuLyDonShopee.Tests` | 2061 | **2061 pass / 0 fail** |
| `Shopee.Core.Tests` | 212 | **212 pass / 0 fail** |
| `Shopee.Suite.Tests` | 52 | **59 pass / 0 fail** (+7) |
| `Shopee.Hub.Web.Tests` | 186 | **186 pass / 0 fail** |
| `Shopee.Module.Search.Tests` | 34 | **34 pass / 0 fail** |
| **Tổng** | 2545 | **2552** |

`server/Shopee.Hub.Web/hub-data/hub.db` — mtime `1785250545`, size `4096` **trước và sau** trọn lượt chạy 5 bộ:
KHÔNG bị đụng. *(Đính chính báo cáo vòng 4: file này CÓ tồn tại trên máy này, vòng 4 khai nhầm là không.)*

#### Thử phá (8 lượt — khôi phục bằng ghi đè nội dung + rebuild giữa các lượt)

| # | Phá gì | Test ĐỎ |
|---|---|---|
| P1 | `ApBanHubTraVe` quay lại gọi `ApVaoDong` (đúng bản vòng 4) | `HubChamGio_TraVeMocChayRong_ThiMocCucBoKhongDoi_VaKhongChayLap` |
| P2 | `ApBanKhepVong`: `Max(cu, hub)` → `cu` (bản vá "quá tay", giữ nguyên mốc cũ) | `HubTraMocChayMoiHon_ThiMocCucBoTienLen` · `ApBanKhepVong_MocChayChiTien_KhongBaoGioLui` |
| P3 | `ApNhomCauHinh` thôi ghi đè `PhanTram` | `NhomCauHinhVaMocSua_VanBiGhiDeTronGoi` (+9 test cũ) |
| P4 | `services.MayNay` rót vô điều kiện (đúng bản vòng 4) | `KhongCoHub_ThiLuuFormKhongDongDauChuTri` |
| P5 | Bỏ hẳn lượt rót `services.MayNay` | `CoHub_ThiLuuFormVanDongDauChuTri` · `WireKmSync_ThiChayBackfillChuTriChoDongCu` |
| P6 | Thêm `KmLichTuDong.CauHoanVongDaNhaKhoa(…)` mà quên liệt kê (**mô phỏng đúng pha B**) | `MoiHamSinhCau_DeuPhaiNamTrongVongRaChuoi` — báo lỗi nêu thẳng tên hàm thiếu |
| P7 | Tooltip nút "Máy này nhận chủ trì" nhắc tới nút `"Lưu cấu hình"` (không tồn tại) | `MoiCauHuongDan_ChiNhacToiNutCoThat` |
| P8 | `ApNhomCauHinh` đánh rơi `TuDong` + `SoNgay` | `NhomCauHinhVaMocSua_VanBiGhiDeTronGoi` |

#### Sáu tiêu chí nghiệm thu vòng 5 — đối chiếu

1. Hub trả mốc chạy `null` ⇒ mốc cục bộ **không đổi** + `DenGioChay` vẫn `false` → `HubChamGio_…` (assert cả hai
   vế, và assert tiền đề "hub ĐÃ bỏ trắng mốc" để phép đo không rỗng). **P1 đỏ.**
2. Hub trả mốc chạy **mới hơn** ⇒ mốc cục bộ tiến lên → `HubTraMocChayMoiHon_…` + `ApBanKhepVong_MocChayChiTien_…`
   (gồm cả vế "hub trả mốc CŨ HƠN ⇒ vẫn giữ mốc cũ"). **P2 đỏ.**
3. Nhóm cấu hình + `CfgUpdatedAt` vẫn ghi đè → `NhomCauHinhVaMocSua_VanBiGhiDeTronGoi` (đổi **cả 7** trường của
   nhóm, kể cả `TuDong`/`SoNgay`, + vẫn phải có dòng log "cấu hình vừa ĐỔI"). **P3, P8 đỏ.**
4. Không hub ⇒ `OwnerMachine` vẫn rỗng; có hub ⇒ đóng dấu → `KhongCoHub_…` + `CoHub_…`. **P4, P5 đỏ.**
5. Thêm hàm `Cau*` mới mà quên liệt kê ⇒ ĐỎ; tooltip mọi nút nằm trong vòng rà → `MoiHamSinhCau_…` +
   `MoiCauHuongDan_…`. **P6, P7 đỏ.**
6. xmldoc `_kmDangThoat` khớp hành vi thật → **chọn HẠ CÂU CHỮ**, và nêu thẳng phần còn hở (xem dưới).

#### Y4 — chọn hạ câu chữ, và cờ KHÔNG che ca nào

`_kmDangThoat` được đọc **đúng một lần**, ở dòng đầu `KmSyncTickAsync`. xmldoc nay nói rõ:

- **Cờ CHE được:** nhịp mà callback của `Timer` đã được **xếp hàng** trên thread pool trước lượt `Timer.Dispose()`
  — `Dispose()` không huỷ được lượt đã xếp, cờ chặn nó ngay khi nó bắt đầu chạy.
- **Cờ KHÔNG che được:** nhịp đã **đi qua** cửa đó và đang `await` mạng (tới 8 giây) — nó chạy tiếp tới hết, tức
  vẫn có thể `Log.Append` sau `Log.Dispose()` và `KmConfig.Save` lúc Velopack đang thay file. **Đúng ca mà
  `DungNhipHoSoKm` muốn chặn nhưng không chặn được.**

**Không chọn phương án "kiểm cờ sau mỗi `await`"** vì trong test không có hub, nhịp dừng ở cổng
`CoordinationRuntime.Client` **trước** mọi `await` — mấy dòng kiểm thêm sẽ là code KHÔNG test nào chạm tới, tức
đổi một câu chữ nói quá thành một đoạn code nói quá. Ghi vào NỢ KỸ THUẬT thay vì làm mù.

#### ⚠ Trả lời câu hỏi bắt buộc: helper nào khác còn khoá cứng trường ở giá trị "tiện"

Đã rà **14 file test pha A**. Cùng hình dạng lỗi đã để lọt Y1 còn ở những chỗ sau — **KHÔNG sửa vòng này** (ngoài
phạm vi), ghi để pha B biết mình đang đứng trên lưới nào:

**Rủi ro CAO**

1. `server/Shopee.Hub.Web.Tests/KmHoSoHubTests.cs:27` — helper `HoSo(...)`: `LanThuTuDong` **không có trong
   initializer** và không có tham số ⇒ luôn `null`; `LanChayCuoi` thì CÓ tham số (đúng cái bất đối xứng làm lỗi
   vô hình). Cả file chỉ có `Assert.Null(p.LanThuTuDong)`. ⇒ **Luật gộp `LanThuTuDong` phía HUB chưa từng được
   đo qua `UpsertKmProfiles`** — đúng tầng Y1 đi qua.
2. `suite/Shopee.Suite.Tests/KmVongBonTests.cs:57` — `Dong(...)`, thủ phạm đã biết. Ngoài hai mốc chạy còn khoá
   cứng `TuDong: true` (cả file không có ca `false`).
3. `orders/XuLyDonShopee.Tests/KmNhipTuDongTests.cs:42` — `CauHinh(...)`: khoá `TuDong: true`, `SoNgay: 1`,
   `LanChayCuoi: null`, `LanThuTuDong: null`, `OwnerMachine` luôn `""`. Cả 3 test đi qua nó ⇒ **nhánh "đêm nay
   đã đốt lượt thử rồi" và cửa chủ trì không có phép đo nào trong chính bộ test của nhịp tự động.**
4. `orders/XuLyDonShopee.Tests/KmCotDongBoTests.cs:19` — `DungSchema1908(...)`: câu `INSERT` (dòng 42–44) không
   liệt kê `lan_thu_tu_dong`; cả file không có chữ `LanThuTuDong` nào. Đây là bộ test **migration duy nhất** của
   `km_config` ⇒ một lượt `ALTER`/`SELECT` đánh rơi cột đó sẽ xanh trọn.
5. `suite/Shopee.Suite.Tests/KmVongBaWorkerTests.cs:41` — `DungDbCu(...)`: `INSERT` không chèn
   `lan_thu_tu_dong`; `lan_chay_cuoi` cố định `2026-07-18` (chọn để LUÔN tới hạn) ⇒ không ca nào dựng được cảnh
   "chưa tới hạn".
6. `orders/XuLyDonShopee.Tests/KmChuTriVaLuuFormTests.cs:59` — `LichBat(...)`: `SoNgay: 1` + `LanChayCuoi: null`
   + không bao giờ đặt `LanThuTuDong`/`CfgUpdatedAt`; 6 test "cửa chủ trì" đều qua nó. (Helper `Dong(...)` ở
   dòng 205 CÙNG file thì tốt — tham số hoá đủ.)

**Rủi ro TRUNG BÌNH**

7. `suite/Shopee.Suite.Tests/KmApHoSoVeMayTests.cs:43` — `HoSo(...)`: `GioKetThuc = "10:30"` khoá cứng, mà
   "10:30" **đúng bằng giá trị mặc định** của dòng `km_config` mới ⇒ đường gộp đánh rơi `GioKetThuc` cũng không
   đỏ. Kèm biến thể nhẹ: tham số `lanThuTuDong` **tồn tại nhưng không test nào truyền**.
8. `orders/XuLyDonShopee.Tests/KmNhanChuTriVaSoDungCapTests.cs:28` — `Dong(...)`: `LanThuTuDong` không nằm trong
   ctor; cả file không có chữ `LanThuTuDong`, trong khi `BanLuuSauForm` là hàm phải giữ hai mốc chạy (V6).
9. `suite/Shopee.Suite.Tests/KmDongBoHoSoTests.cs:26` — `CauHinh(...)`: `GioKetThuc` khoá `"10:30"`, và
   **không assert `GioKetThuc` ở đâu cả** ⇒ đường "hub thắng ⇒ máy này nhận `GioKetThuc`" không có lưới.
10. `suite/Shopee.Suite.Tests/KmVongNamTests.cs` *(file của chính vòng này)* — bản đầu khoá cứng `TuDong: true`
    và không assert `TuDong`/`SoNgay` lần nào. **Đã sửa ngay trong vòng**: `NhomCauHinhVaMocSua_…` nay đổi cả 7
    trường của nhóm cấu hình và assert đủ; lượt phá P8 xác nhận.

**Rủi ro THẤP:** `KmNhuongKhungTests.cs:30` (`Lich(...)`), `KmChuongDayHoSoTests.cs:45` (`Lot(tao)` khoá
`HetKhung: false`, `SoShopChuaChay: 0`).

**Sạch:** `KmRouteHttpTests.cs`, `KmMocLanChayCuoiTests.cs`. *(Ghi chú ở `KmRouteHttpTests`: hai DTO của vòng
`POST → GET` đều không mang `LanChayCuoi`/`LanThuTuDong` ⇒ **hợp đồng JSON của hai mốc chạy chưa được khoá** —
thiếu CA chứ không phải helper khoá cứng.)

#### Điểm lệch spec / điểm phiên chính nên soi lại

1. **Tên hàm mới `ApBanKhepVong` là tôi tự đặt** — plan chỉ mô tả hành vi ("ghi đè nhóm cấu hình + `CfgUpdatedAt`;
   hai mốc chạy đi qua `KmHoSoGop.Max`"), không chỉ định chỗ cắt. Tôi cắt thành **ba** hàm (`ApNhomCauHinh` dùng
   chung) thay vì thêm `if` vào `ApVaoDong`, để chỗ gọi buộc phải chọn một trong hai ngữ nghĩa.
2. **`ApVaoDong` vẫn gán thẳng hai mốc chạy** — đúng cho đường KÉO (`GopVeMay` đã chạy `KmHoSoGop.Gop` với đồng
   hồ MÁY NÀY, nên `Max` đã xảy ra ở đó rồi). Nếu sau này có nơi thứ ba gọi `ApVaoDong` với hồ sơ THÔ thì lỗi Y1
   quay lại; xmldoc đã cảnh báo, **nhưng không có test nào khoá được luật "ai được gọi hàm nào"**.
3. **Y2 đo ở SEAM, không đi đường thật.** Test lặp lại đúng ba dòng của `AccountsViewModel.MoFormKm`
   (đọc `MayNay` → `BanLuuSauForm` → `Save`) vì đường thật đi qua `DialogService.ShowKm` — modal WPF không dựng
   headless. **Chỗ nối form ⇄ hook `MayNay` vẫn trần**: đổi `_services.MayNay?.Invoke() ?? ""` trong
   `AccountsViewModel.Phien.cs:104` thành một hằng số bịa thì 2552 test vẫn xanh.
4. **Lưới phản chiếu Y3 bắt theo TÊN HÀM, không bắt theo "câu này có tới tay người dùng không".** Một câu mới đặt
   trong hàm KHÔNG tên `Cau*` (hoặc nội tuyến thẳng vào `Log.Append`, như hai dòng của `NhipLichKmAsync` hiện
   nay) vẫn lọt. Ngưỡng chống đo rỗng đang là `>= 7` hàm (hiện có 8).
5. **`_kmDangThoat` vẫn hở ca "nhịp đang bay"** — xem mục Y4 ở trên. Đề nghị thêm vào NỢ KỸ THUẬT.
6. `git status` trước khi sửa: **cả 3 file tôi đụng đều là `??` (untracked)**, không đè lên file `M` của phiên
   khác. Cây bẩn 18–19/08 vẫn nguyên; hai file bị đột biến lúc thử phá (`KmDialog.xaml`,
   `KmLichTuDong.cs`) đã `diff` xác nhận khôi phục bit-for-bit.

#### Không làm (đúng phạm vi)

**Y5** (ghi chú deploy hub-trước-client) là việc của plan, không phải code. **8 khoản NỢ KỸ THUẬT** giữ nguyên.
Pha B/C/D chưa động. **Không commit, không push.**

---

### VÒNG 6 (22/08/2026) — đóng blocker do vòng 5 sinh ra + vá lỗ lưới trước khi mở pha B

Làm Z1→Z6. Build **0 warning** cả hai sln (`-t:Rebuild` trước mỗi lượt đo). Cây làm việc **BẨN sẵn** 18–19/08 —
không commit / stash / revert; mọi file đụng tới đều đã đối chiếu hash trước/sau lượt thử phá.

#### File đã sửa / tạo

| File | Thay đổi |
|---|---|
| `suite/Shopee.Suite/Infrastructure/OrdersModuleHost.KmSync.cs` | **Z1(a)**: `WireKmSync(services, bool coHub)` → `WireKmSync(services, Func<bool> coHub)`; `services.MayNay = () => coHub() ? LeaseMachineId : "";` rót VÔ ĐIỀU KIỆN, đọc TƯƠI. **Z1(b)**: bỏ lượt backfill khỏi thân wire, thêm `BackfillChuTriMotLan(AppServices)` + latch `ConditionalWeakTable`, gọi ở đầu `KmSyncTickAsync` TRƯỚC lượt đẩy. **Z2**: `CauHubKepMocSua` nêu đủ ba khả năng. |
| `orders/XuLyDonShopee.App/ViewModels/KmDialogViewModel.cs` | **Z1(d)**: `BanLuuSauForm` giữ `tuoi.OwnerMachine` khi `mayNay` rỗng. **Z5**: thêm `internal string MayNay => _mayNay;`. |
| `orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs` | **Z5**: tách `internal KmDialogViewModel DungFormKm(...)` khỏi `MoFormKm`; lượt lưu đọc lại `vm.MayNay` thay vì gọi hook lần hai. |
| `server/Shopee.Hub.Web.Tests/KmHoSoHubTests.cs` | **Z3**: helper `HoSo(...)` thêm tham số `lanThuTuDong` + **4 test mới** (Max hai chiều qua `UpsertKmProfiles`, hub chưa có mốc, mốc rác tương lai bị bỏ). |
| `suite/Shopee.Suite.Tests/KmVongSauTests.cs` (**MỚI**) | **Z1(a)(b)(c) + Z2** — 5 test. |
| `orders/XuLyDonShopee.Tests/KmChoNoiMayNayTests.cs` (**MỚI**) | **Z5 + Z1(d)** — 6 test. |
| `suite/Shopee.Suite.Tests/AssemblyInfo.cs` (**MỚI**) | Tắt chạy song song cho assembly (lý do ở mục "lệch spec"). |
| `suite/Shopee.Suite.Tests/KmVongBonTests.cs` | **Z4**: `CacHamSinhCau()` thêm `AccountSession` + `AccountsViewModel`; liệt kê `CauBiTuChoiKhoa` (2 ca) · `CauTongKetKm` · `CauTrangThaiKm` (3 kết cục); sàn chống đo rỗng 7 → **11**. |
| `suite/Shopee.Suite.Tests/KmVongNamTests.cs` | **Z1(c)**: `Assert.Null(svc.MayNay)` → `Assert.Equal("", svc.MayNay?.Invoke() ?? "")`; 2 call-site `coHub:` sang `Func<bool>`. |
| `suite/Shopee.Suite.Tests/KmVongBaWorkerTests.cs` | Đổi vế đo của test backfill sang NHỊP (`WireKmSync_ThiChayBackfillChuTriChoDongCu` → `WorkerChayBackfillChuTriChoDongCu`). |

#### Trả lời câu hỏi BẮT BUỘC của vòng này — mỗi hook/giá trị đụng vào

| Thứ bị sửa | Đổi được lúc chạy? | Khuôn mẫu tương đương ĐANG CÓ trong repo |
|---|---|---|
| `services.MayNay` | **CÓ**. `CoordinationRuntime.Reconnect()` gán lại `Client` (`SettingsViewModel.cs:289`, nút "Kết nối ngay"). | `services.HubDaCauHinh = () => CoordinationRuntime.Client is not null;` — `OrdersModuleHost.HubRead.cs:25`, kèm comment "Đọc TƯƠI mỗi lần gọi (người dùng có thể cấu hình Hub rồi Reconnect giữa chừng)". Và hợp đồng của chính `AppServices.MayNay` (`AppServices.cs:244`): *"Phải đọc TƯƠI mỗi lần gọi."* |
| Cổng `coHub` của `WireKmSync` | **CÓ** (cùng lý do). Vì vậy đổi từ `bool` sang `Func<bool>` — giá trị đóng băng chính là con bug Z1. | `services.QueryOrderStatistics` (`HubRead.cs:31`) đọc `CoordinationRuntime.Client` TRONG thân lambda mỗi lượt gọi, không chụp sẵn. Cùng khuôn: `KmSyncTickAsync` cũng đọc `Client` mỗi nhịp. |
| Vị trí gọi `BackfillOwnerMachineOnce` | **CÓ** — điều kiện chạy được của nó (`mayNay` khác rỗng) chỉ đúng sau khi hub bật. | Khuôn "việc một lần nhưng phải đợi điều kiện" đã có ở `HubOutboxWorker` (hoãn ~15s rồi lặp) và `LamMoiTenMayAsync` (`KmSync.cs`) — cả hai đặt việc trong NHỊP + tự chốt bằng mốc, không đặt trong lượt wire. |
| Latch của backfill | Không đổi lúc chạy; **khoá BỀN** là `settings.backfill_km_owner_machine_v1`. | `Database.BackfillCfgUpdatedAtOnce` — cùng khuôn khoá trong bảng `settings` (chính xmldoc của `BackfillOwnerMachineOnce` dẫn ra). Latch bộ nhớ chỉ để nhịp 3 giây khỏi mở SQLite. |
| `BanLuuSauForm.OwnerMachine` | `mayNay` truyền vào **đổi được** (hub bật/tắt giữa chừng). | `KmLichTuDong.LaMayChuTri` (`KmLichTuDong.cs:178`) đã có sẵn luật *"`MayNay` chưa rót ⇒ không biết mình là ai ⇒ GIỮ NGUYÊN hành vi cũ"*; Z1(d) chỉ áp đúng luật đó sang đường GHI. |
| `KmDialogViewModel.MayNay` (mới) | Bất biến trong vòng đời form — **cố ý**: form phải đóng dấu đúng danh tính nó vừa HIỂN THỊ, không phải danh tính lúc bấm Đóng. | `KmDialogViewModel.BanNap` — cùng lớp, cùng lý lẽ "chụp lúc mở form mới là vế so đúng" (xmldoc W5). |

#### Build & test THẬT (cây bẩn 18–19/08, `-t:Rebuild` cả hai sln trước khi đo)

| Assembly | Vòng 5 | Vòng 6 | Δ |
|---|---|---|---|
| `XuLyDonShopee.Tests` | 2061 | **2067** | +6 (`KmChoNoiMayNayTests`) |
| `Shopee.Core.Tests` | 212 | **212** | — |
| `Shopee.Suite.Tests` | 59 | **64** | +5 (`KmVongSauTests`) |
| `Shopee.Hub.Web.Tests` | 186 | **190** | +4 (Z3) |
| `Shopee.Module.Search.Tests` | 34 | **34** | — |
| **Tổng** | 2552 | **2567** | **+15** |

`Failed: 0` mọi assembly. Build `ShopeeSuite.sln` và `server/ShopeeHub.sln`: **0 warning, 0 error**.
Cách ly DB thật còn nguyên sau cả đợt: `app.db` 118784 B / 19-08 20:43:42 · `machine.json` 191 B / 10-08
05:52:03 · `server/Shopee.Hub.Web/hub-data/hub.db` 4096 B / 28-07 21:55.

#### Thử phá — 12 lượt (khôi phục bằng ghi đè nội dung + rebuild; hash trước/sau khớp)

| # | Phá gì | Test ĐỎ |
|---|---|---|
| 1 | Dựng lại đúng bản vòng 5: `if (coHub()) { services.MayNay = () => LeaseMachineId; }` | 4 đỏ: `WireKhiChuaCoHub_RoiHubBatLenGiuaChung…` · `HubTatGiuaChung…` · `CauHinhHubSauKhoiDong…` · `ChuaCoHub_ThiBackfillKhongChay…` |
| 2 | Rót vô điều kiện nhưng **chụp** giá trị: `var coHubDaChup = coHub();` | 4 đỏ (như trên) |
| 3 | Bỏ dòng `BackfillChuTriMotLan(services);` trong `KmSyncTickAsync` | `KmVongBaWorkerTests.WorkerChayBackfillChuTriChoDongCu` · `CauHinhHubSauKhoiDong…` |
| 4 | Chốt latch TRƯỚC khi biết id máy | `CauHinhHubSauKhoiDong…` · `ChuaCoHub_ThiBackfillKhongChay_VaKhongChotLatch` |
| 5 | Bỏ hẳn latch bộ nhớ (`if (false && …)`) | `ChuaCoHub_ThiBackfillKhongChay_VaKhongChotLatch` |
| 6 | Trả `CauHubKepMocSua` về câu một chiều "Gần như chắc chắn ĐỒNG HỒ MÁY NÀY chạy nhanh" | `CauHubKepMocSua_NeuDuBaKhaNang_CoNhacHubChamGio` |
| 7 | Trả `OwnerMachine = mayNay ?? ""` (bỏ Z1d) | `KhongCoHub_SuaForm_ThiGiuNguyenChuTriDangCo` |
| 8 | Đổi lượt đọc hook ở `DungFormKm` thành hằng bịa `"may-bia:orders"` — **đúng đột biến vòng 5 để lọt** | 3 đỏ: `MoFormKm_LayDanhTinhMayTuHookMayNay` · `MoFormKm_MayKhacChuTri…` · `MoFormKm_ChuaCoHub…` |
| 9 | `KmHoSoGop.Gop`: `LanThuTuDong = null` — **đúng đột biến vòng 5 không ai đỏ** | 3 đỏ: `DayMocThuTuDongCuHon…` · `DayMocThuTuDongMoiHon…` · `HubChuaCoMocThuTuDong…` |
| 10 | `LanThuTuDong = LocMoc(den.LanThuTuDong, …)` (lấy bản đến thay vì `Max`) | `DayMocThuTuDongCuHon_ThiHubGiuMocMoi` |
| 11 | `LanThuTuDong = Max(dangCo.…, den.…)` (bỏ lọc trần tương lai) | `MocThuTuDongQuaXaTuongLai_ThiBiBo` |
| 12 | Thêm `AccountSession.CauDaNhaKhoaMutation()` mà quên liệt kê — mô phỏng đúng câu B1 sắp viết | `MoiHamSinhCau_DeuPhaiNamTrongVongRaChuoi` |

#### Bảy tiêu chí nghiệm thu vòng 6 — đối chiếu

1. **Wire chưa có hub → hub bật giữa chừng ⇒ `MayNay` trả id thật; chưa có hub ⇒ rỗng** — ĐẠT
   (`WireKhiChuaCoHub_RoiHubBatLenGiuaChung_ThiMayNayTraIdThat` + vế ngược `HubTatGiuaChung_ThiMayNayTraLaiRong`;
   thử phá 1–2).
2. **Máy cấu hình hub sau khởi động ⇒ backfill vẫn chạy đúng một lần** — ĐẠT
   (`CauHinhHubSauKhoiDong_ThiNhipVanBackfillChuTri_VaChiMotLan`, đi qua ĐÚNG `KmSyncTickAsync`; thử phá 3–4).
3. **Máy không hub sửa form ⇒ `OwnerMachine` không bị ghi đè thành rỗng** — ĐẠT
   (`KhongCoHub_SuaForm_ThiGiuNguyenChuTriDangCo` + hai vế đối chứng; thử phá 7).
4. **Đột biến `MayNay` thành hằng bịa ở `Phien.cs` ⇒ có test ĐỎ** — ĐẠT (thử phá 8, ba test đỏ).
5. **`CauHubKepMocSua` nêu đủ ba khả năng, có nhắc hub chậm giờ** — ĐẠT (thử phá 6).
6. **Làm hỏng vế `LanThuTuDong` trong `KmHoSoGop.Gop` ⇒ có test hub ĐỎ** — ĐẠT (thử phá 9, 10, 11).
7. **Thêm hàm `Cau*` mới trong `AccountSession` mà quên liệt kê ⇒ lưới ĐỎ** — ĐẠT (thử phá 12).

#### Z6 — đính chính đã ghi nhận

Lời khai vòng 5 rằng `KmCotDongBoTests` là "bộ migration DUY NHẤT của `km_config`" là **SAI**:
`KmCauHinhLuuTruTests` cũng khoá migration `lan_thu_tu_dong` (3 test). Không sửa code gì cho mục này.

#### Điểm LỆCH spec / điểm phiên chính nên soi lại

1. **Đổi CHỮ KÝ `WireKmSync(AppServices, bool)` → `(AppServices, Func<bool>)`, và sửa 3 call-site test.** Spec
   Z1(a) chỉ nói "rót vô điều kiện, đọc tươi". Nhưng `CoordinationRuntime.Client` có setter `private` nên test
   không gán được — giữ overload `bool` là để nguyên một seam ĐÓNG BĂNG trong API, tức đúng cái bẫy vòng 6 vừa
   gỡ. Ba call-site chỉ đổi `coHub: true` → `coHub: () => true` (thuần cơ học, không đụng assert nào).
2. **Sửa thêm MỘT test ngoài Z1(c): `KmVongBaWorkerTests.WireKmSync_ThiChayBackfillChuTriChoDongCu`.** Nó khoá
   *chỗ đứng* của lượt backfill (trong thân `WireKmSync`) — mà Z1(b) yêu cầu chuyển chỗ đứng đó, nên nó ĐỎ theo
   thiết kế. Đổi tên thành `WorkerChayBackfillChuTriChoDongCu` và thêm một dòng `await KmSyncTickAsync(svc)`;
   **câu hỏi nó đặt ra không đổi** ("worker có backfill dòng cũ không"), assert không nới. Khai ra vì luật vòng
   này chỉ cho phép ngoại lệ Z1(c).
3. **Thêm `suite/Shopee.Suite.Tests/AssemblyInfo.cs` tắt chạy song song cả assembly** — KHÔNG có trong spec.
   Lý do: `OrdersModuleHost` giữ trạng thái worker ở biến `static` (`_kmTimer`, `_kmDangThoat`, `_kmDirty`,
   `_daBaoTrongNgay`), mà vòng 6 lần đầu có test đi qua ĐÚNG `KmSyncTickAsync` thật. Chạy song song thì
   `DungNhipHoSoKm()` của lớp này bật `_kmDangThoat` đúng lúc lớp kia gọi nhịp ⇒ nhịp đó quay đầu ở cửa đầu
   tiên và phép đo thành đo một nhịp KHÔNG LÀM GÌ. Rủi ro này **đã tồn tại từ vòng 4** (`KmVongBonTests` cũng
   gọi `KmSyncTickAsync`); nay chỉ đóng lại. Giá: `Shopee.Suite.Tests` chạy 10s thay vì 8s.
4. **Latch backfill chốt theo BỘ DỊCH VỤ (`ConditionalWeakTable<AppServices, object>`), không phải cờ `bool`
   toàn cục.** Production chỉ có một `AppServices` nên hai cách y hệt; chọn cách này để bộ test (nhiều lớp, mỗi
   lớp một DB riêng) không đóng latch của nhau. Nếu phiên chính thấy quá lạ so với phần còn lại của repo thì có
   thể hạ về `bool` — nhưng khi đó phải giữ mục 3 ở trên, không thì phép đo backfill lệ thuộc thứ tự chạy.
5. **Thử phá 5 cho kết quả HẸP hơn tôi tưởng — khai thẳng:** bỏ hẳn latch bộ nhớ chỉ làm ĐỎ
   `ChuaCoHub_ThiBackfillKhongChay_VaKhongChotLatch`, **không** làm đỏ vế "chỉ một lần" trong
   `CauHinhHubSauKhoiDong…`. Vì luật "một lần" thật sự do khoá BỀN trong bảng `settings` giữ, latch bộ nhớ chỉ
   là tối ưu I/O. Đã sửa lại comment trong test để nó không khẳng định điều nó không đo.
6. **`BackfillChuTriMotLan` đặt TRƯỚC cổng `CoordinationRuntime.Client` trong `KmSyncTickAsync`, không phải
   sau.** Về hành vi là tương đương (cổng thật của nó là hook `MayNay`, rỗng khi chưa có hub), và đặt trước là
   điều kiện để đo được bằng test mà không dựng `HubClient` thật. Nhưng đây là chỗ **hai cổng phải luôn đồng
   pha**: ai sau này rót `MayNay` khác đi (ví dụ trả id kể cả khi chưa có hub) là backfill chạy trên máy không
   hub — đúng ca X4 vòng 4 sinh ra để chặn. Đã ghi cảnh báo tại chỗ, nhưng **không có test nào khoá được ràng
   buộc đó**.

#### Chỗ CHƯA có lưới, khai thẳng

1. **Lưới Y3/Z4 vẫn chỉ bắt HÀM `Cau*`, không bắt HẰNG.** `AccountsViewModel` còn hai chuỗi
   `internal const string` người dùng đọc được — `CauTuChoiViLatCatDangChay`, `CauDaDongCuaSoXem` — mà
   `GetMethods` không thấy. Chưa kéo vào vì chúng nhắc nút "Đóng cửa sổ" của màn Tài khoản, trong khi vòng rà
   hiện tại đối chiếu với danh sách nút của **form KM** ⇒ kéo vào là đỏ oan. Muốn phủ thì phải tách vòng rà
   theo MÀN, không phải theo lớp — việc của vòng sau.
2. **`OrdersBridgeSession.CauKetQuaShop` vẫn ngoài lưới.** Spec Z4 chỉ đòi hai lớp `AccountSession` và
   `AccountsViewModel`; làm đúng phạm vi. Nêu lại để không ai tưởng đã phủ hết.
3. **Chỗ nối `MoFormKm` ⇄ `DialogService.ShowKm` vẫn không đo được** (modal WPF). Z5 đóng được vế "form nhận
   đúng danh tính máy" và vế "lượt lưu dùng đúng danh tính form đang hiển thị" (nay là cùng MỘT lượt đọc),
   nhưng "người dùng bấm Đóng thì `MoFormKm` có gọi `Save` không" vẫn là mã KHÔNG có test.
4. **Ràng buộc "ai được gọi `ApVaoDong` / `ApBanKhepVong`"** (nợ vòng 5) chưa được khoá — giữ nguyên.

#### Không làm (đúng phạm vi)

Không commit, không push. Pha B/C/D chưa động. Mục **NỢ KỸ THUẬT** giữ nguyên, không đụng.
