# Plan: "KM của Shop" — tạo Chương trình khuyến mãi hàng loạt cho mọi shop

- **Ngày:** 2026-08-18
- **Trạng thái:** pha 2 (ribbon + form) XONG và đã publish bản local · pha 1 (dò DOM) gần xong — còn mắt xích "chọn dòng trong form" · pha 3–4 chưa làm
- **Người lập:** phiên chính

## 1. Yêu cầu (nguyên văn người dùng, gom lại)

Thêm **một mục ribbon ở tab Shopee: "KM của Shop"** → mở một **form mới** gồm:

| Trường | Mặc định | Ràng buộc |
|---|---|---|
| Tên chương trình KM | `KM ngày <ngày set> từ <giờ bắt đầu> - <giờ kết thúc>` | tối đa **150** ký tự |
| Thời gian khuyến mãi (từ → đến) | **9:30 – 10:30** ngày set chương trình | — |
| % giảm | **5** | (người dùng chốt: đưa lên form cùng tên + thời gian) |
| ☑ Chạy tự động | tắt | — |
| Sau **xx** ngày | — | số ngày |
| ▶ Chạy chương trình | — | — |

- **Chạy tự động:** tính từ **thời điểm bắt đầu chạy**, ghi nhớ; lần kế = mốc đó + `xx` ngày.
- Lượt tự động chạy **trong khoảng 2–6h sáng**.
- **Luồng check đơn:** tới gần 2h sáng thì **không tự đặt vòng kế nữa**, phải đặt **sau 6h** (sau khi luồng KM xong).
  Xong luồng KM ⇒ **khởi tạo lại tiến trình check đơn** và chạy tiếp như cũ.
- Bấm nút chạy (kể cả nút "Mở list shop") ⇒ **dừng việc check đơn**; **không phải đăng nhập lại** (cùng hồ sơ).

### Luồng của extension KM (người dùng mô tả từng bước, kèm DOM thật)

1. Mở **list shop** y như flow check đơn → duyệt từng shop.
2. Vào chi tiết shop → click **"Kênh Marketing"** ở menu trái (`li.sidebar-menu-box.ps_menu_marketing`).
3. Click **"Khuyến Mãi của Shop"** (submenu `a[test-id="Discount"]` → `/portal/marketing/list/discount`, hoặc thẻ
   `#discount_promotion-all` trong lưới công cụ marketing).
4. Trong "Chương Trình Của Shop" → click nút **"Tạo"**.
5. Điền **Tên chương trình** + **Thời gian khuyến mãi** (2 ô `eds-date-picker`, mỗi ô có lịch + 2 cột giờ/phút +
   nút "Xác nhận").
6. Mục "Sản phẩm khuyến mãi" → **"Thêm sản phẩm"** → modal "Chọn Sản Phẩm":
   - click **checkbox check-all** ở đầu bảng (`thead … label.eds-checkbox input`),
   - đọc `ul.eds-pager__pages` xem có mấy trang, **click từng trang**, mỗi trang lại **check-all**,
   - hết trang → click **"Xác nhận"** ở footer modal.
   - **Không có sản phẩm nào ⇒ bỏ shop đó**, nghỉ **3–5 phút**, sang shop khác.
7. Modal đóng → khối "Thiết lập hàng loạt": nhập **% giảm** vào ô `%GIẢM` → click **"Cập nhật hàng loạt"**.
8. Click **"Xác nhận"** ở thanh đáy (`.fix-container.fixed-bottom .confirm-btn`).
9. Xong một shop → nghỉ **3–5 phút** → shop kế.

### Người dùng đã chốt (hỏi 18/08)

1. **Nghỉ giữa hai shop = 3–5 PHÚT** (khớp nhịp luồng check đơn, an toàn anti-bot hơn hẳn).
2. **Phạm vi = mọi shop của TÀI KHOẢN ĐANG CHỌN** (giống luồng check đơn).
3. **Trần chọn sản phẩm:** Shopee cho tới ~10.000 SP; "10/…" trong ảnh là do người dùng tự tick 10. ⇒ **không có
   xử lý trần riêng**, cứ đi hết phân trang; nhưng vẫn ghi log số đã chọn để phát hiện sớm nếu Shopee chặn.
4. **Đường chạy = ĐÚNG cái nút vừa làm xong.** Nguyên văn: *"cái nút chạy đó chính là cái nút mà bạn vừa làm
   xong, giờ viết thêm logic cho nó."* ⇒ KHÔNG dựng đường mở trình duyệt / SSO / đọc danh sách shop thứ hai.
   `MoDenDanhSachShopAsync` (mở Brave sạch → SSO → `/portal/shop` → đọc shop) là **khúc đầu dùng chung**; luồng KM
   = khúc đó **cộng thêm** vòng lặp shop. Đây cũng là lý do bỏ hẳn ý "extension thứ hai chạy song song".
5. **Mọi thao tác trên trang do EXTENSION tự làm, KHÔNG qua CDP.** Nguyên văn: *"các action trên browser như chọn
   ngày, giờ, gõ tiêu đề, click chuyển page đều do ext làm, không gửi qua cdp."* ⇒ dùng DOM trong trang
   (`element.click()`, gán `value` + bắn `input`/`change`), KHÔNG dùng `chrome.debugger` / `dbg-input.js` cho các
   bước KM.

## 2. Ràng buộc kỹ thuật đã biết (từ việc "Mở list shop", không phải đoán)

- **Một hồ sơ Brave = một tiến trình.** KM và check đơn KHÔNG chạy song song được — đây chính là lý do người
  dùng chốt "chạy KM thì dừng check đơn".
- **Cổng cầu nối `47821` là hằng toàn máy** (`OrdersBridgeChannel.cs:145` ↔ `extensions/shopee-orders/core.js:8`),
  và **`BrowserProfileGuard` chỉ nhận dạng chuỗi `shopee-orders`** (`BrowserProfileGuard.cs:70`, gọi từ
  `OrdersBridgeLauncher.cs:32`). Hai chi tiết này là lý do kỹ thuật để **KHÔNG đẻ extension thứ hai**: một
  extension mới sẽ cần cổng riêng, và cửa sổ mồ côi của nó sẽ **không bị dọn** bởi guard ⇒ lượt phóng sau chết vì
  khoá hồ sơ.
  ⇒ **ĐÃ CHỐT (người dùng 18/08: *"nếu viết km vào ext đang sử dụng không v.đ j thì bạn viết luôn vào đó, khỏi
  phải viết thêm ext"*):** thêm các lệnh KM vào **chính extension `shopee-orders`**, KHÔNG tạo `shopee-km`. Được
  cả ba: dùng lại nguyên bước SSO + đọc danh sách shop đã chứng minh chạy (6 giây, không captcha), không đụng
  cổng, không hở guard.
- Trình duyệt nằm trong **Job Object `KILL_ON_JOB_CLOSE`** ⇒ tắt app là mọi cửa sổ chết theo.
- Hạ tầng dùng lại được gần như nguyên vẹn: `BridgeTestRig` (test không cần trình duyệt), `shared/ws-bridge.js`,
  `shared/dbg-input.js` (trusted click), khuôn `flow-shop.js` cho bước SSO + đọc danh sách shop.

### 2b. Chỉ MỘT client chạy một tài khoản tại một thời điểm — **CHỐT CUỐI 18/08 22:5x**

Nguyên văn: *"mỗi sub chỉ chạy 1 list tk, nếu có sub khác thì là list tk khác, không hợp lí, chốt, mỗi acc chỉ
chạy trên 1 client tại đúng 1 thời điểm"*.

**Một khoá duy nhất `orders:<login>` dùng chung cho MỌI luồng** (check đơn · KM · cửa sổ "Mở list shop").
**KHÔNG tách `orders-km:`** — đề xuất tách của phiên chính giữa chừng đã bị bác, và bác đúng.

Đường đi tới quyết định, ghi lại để lần sau không ai lật lại mà không biết vì sao:

1. Người dùng thử mở list shop trên máy `NUC` trong lúc máy `Ng Xuân Mùi` đang chạy check đơn ⇒ bị từ chối:
   `Tài khoản đang chạy ở máy Ng Xuân Mùi — bỏ qua lượt này.` Chốt khoá làm ĐÚNG (đã xác minh `machine.json`
   của máy này: `DisplayName: NUC`, `Hostname: DESKTOP-D13CE3U` ⇒ máy giữ khoá đúng là máy KIA).
2. Thoạt đầu người dùng nói KM và check đơn ở hai client thì chạy song song được ⇒ phiên chính đề xuất tách khoá
   theo luồng. **Sai hướng.**
3. Lý do sai: **2 máy = 2 kho cookie riêng, nhưng chỉ MỘT phiên đăng nhập ở phía Shopee.** Cho hai máy cùng thao
   tác một subaccount ⇒ máy có cookie cũ rơi form login ⇒ tự đăng nhập lại (Playwright, có thể phải nhập mã) ⇒
   **đá phiên của máy kia** ⇒ vòng đang chạy gãy / rơi trang verify ⇒ máy kia lại đăng nhập lại ⇒ **ping-pong**.
   Đúng thứ khoá sinh ra để chặn, và đúng thứ người dùng cấm ngay câu đầu: *"tôi không muốn phải login lại"*.
4. Hướng "mỗi máy một subaccount riêng" cũng bị bác: **mỗi subaccount chỉ quản một danh sách shop riêng**, nên
   hai subaccount là hai tập shop khác nhau — không giải quyết được việc chia tải cho CÙNG một tập shop.

**Hệ quả vận hành:** muốn chạy KM trên máy A thì phải **dừng check đơn ở máy B** (hoặc đợi vòng bên đó xong);
khoá nhả rồi máy A mới xin được. Trên cùng một máy, KM và check đơn vẫn loại trừ nhau bằng cơ chế "mượn hồ sơ"
(một hồ sơ Brave, một cổng cầu nối) — không liên quan hub.

**Việc còn phải sửa (nhỏ):** câu từ chối `"— bỏ qua lượt này"` là chữ của vòng chạy TỰ ĐỘNG; khi người dùng BẤM
NÚT mà đọc câu đó thì không hiểu chuyện gì. Câu cho đường bấm tay phải nói rõ: máy nào đang giữ, và phải làm gì
(dừng bên đó rồi bấm lại).

### 2b-cũ. Cơ chế (giữ nguyên)

Người dùng chốt 18/08: *"Khi 1 client chạy chương trình km, các client khác không được chạy cho acc đó nữa (giống
phần check đơn, đảm bảo chỉ 1 client / 1 acc / tại 1 thời điểm)."*

**Đã thoả sẵn** nhờ quyết định dùng chung khung `ChayLatCatAsync`: khung đó xin **đúng khoá xuyên máy**
`orders:<login>` mà phiên check đơn xin (`AppServices.AcquireAccountLease` → hub), giữ suốt thân lệnh, nhả trong
`finally`. Với KM thì "thân lệnh" là **cả vòng 12 shop (~1 giờ)** ⇒ khoá được giữ trọn lượt chạy, máy khác bị từ
chối kèm tên máy đang giữ (`AccountSession.CauBiTuChoiKhoa`). Hub có heartbeat 60s nên lượt dài không bị rớt khoá.

⚠ **Bẫy thứ tự — đã chặn bằng chỉ dẫn riêng cho người thực thi.** Luật mới "bấm nút ⇒ dừng check đơn" khiến HAI
chủ cùng đòi MỘT khoá trên CÙNG máy: phiên check đơn chỉ nhả khoá trong `finally` của luồng nền. Xin khoá trước
khi nó nhả xong ⇒ hub cấp (cùng `machine_id`) rồi lượt DELETE của phiên cũ **xoá đúng dòng vừa cấp**, mà
`HeartbeatLeases` là UPDATE thuần nên không hồi phục ⇒ chạy cả tiếng trong khi hub thấy tài khoản TRỐNG ⇒ máy 2
nhảy vào. Thứ tự bắt buộc: **dừng check đơn → CHỜ dừng hẳn (`IsRunning == false` và `BraveProcess == null`) → mới
xin khoá → mới mở trình duyệt.** Đây là lần thứ ba cùng một cơ chế (một key, hai chủ, không đếm chủ) cắn vào việc
này.

## 3. Rủi ro lớn nhất — nói trước

**Điền 2 ô thời gian của Shopee (`eds-date-picker`) là phần khó nhất**, và người dùng đã chốt **không dùng CDP**
nên phải làm hoàn toàn bằng DOM trong trang.

Đó là component Vue tự vẽ: ô hiển thị là `div.eds-selector__inner` (**KHÔNG phải `<input>`** — không "gõ" vào
được), lịch là bảng `div.eds-date-table__cell`, giờ/phút là hai danh sách `ul.eds-time-spinner__list`, chốt bằng
nút "Xác nhận". Nghĩa là phải **click ô ngày → click `li` giờ → click `li` phút → Xác nhận**, mỗi bước chờ Vue
render. Ngày quá khứ và một phần giờ bị `disabled` (thấy rõ trong DOM người dùng gửi), còn có ràng buộc "không
quá 180 ngày".

Hai điểm phải cẩn thận khi làm bằng DOM thuần:
- **Ô "Tên chương trình" LÀ `<input>` thật** (`input.eds-input__input[maxlength="150"]`) ⇒ gán `value` rồi **bắn
  `new Event('input', {bubbles:true})`**; không bắn sự kiện thì Vue không nhận, ô đếm vẫn `0/150` và giá trị bị
  nuốt khi submit. Ô `%GIẢM` cũng vậy.
- **Checkbox check-all và các `li` phân trang** phản ứng với `click()` thường, nhưng phải click đúng phần tử Vue
  gắn handler (`input.eds-checkbox__input` / `li.eds-pager__page`), không phải cái `<span>` bọc ngoài.

⇒ **Bước đầu tiên của việc này KHÔNG phải là code luồng, mà là một lát cắt DÒ DOM**: mở đúng một shop, đi tới
form tạo chương trình, rồi **dump** cấu trúc thật của hai picker + modal sản phẩm ra nhật ký. Làm ngược lại là
viết 500 dòng selector đoán mò rồi sửa mù.

## 4. Chia pha (đề xuất — cần người dùng chốt thứ tự)

| Pha | Nội dung | Kiểm chứng được bằng |
|---|---|---|
| **0** | Xong việc "Mở list shop" đang dở (đang ở vòng sửa cuối) | đã có plan riêng |
| **1** | **Lát cắt dò DOM**: nối tiếp ĐÚNG đường "Mở list shop" (mở 1 shop → Kênh Marketing → Khuyến Mãi của Shop → bấm Tạo) rồi **dump** cấu trúc thật của 2 picker + modal sản phẩm ra nhật ký. Chưa điền gì, chưa tạo gì. | chạy app thật, đọc dump |
| **2** | **Form + lưu trữ**: ribbon "KM của Shop", dialog, sinh tên mặc định, validate 150 ký tự, % giảm, lưu cấu hình theo tài khoản | test thuần + test VM + chạy tay |
| **3** | **Luồng KM đầy đủ** trong extension: điền tên/thời gian → chọn SP theo trang → % giảm → Cập nhật hàng loạt → Xác nhận; nghỉ 3–5' giữa shop; shop không có SP thì bỏ qua | rig test + chạy thật 1 shop |
| **4** | **Lịch tự động 2–6h + bắt tay với check đơn**: dừng check đơn → chạy KM → chạy lại check đơn; check đơn không đặt vòng rơi vào 2–6h | hàm thuần + test VM (không cần trình duyệt) |

Pha 4 phụ thuộc pha 3; pha 2 chạy song song được với pha 1.

## 5. Những chỗ tôi sẽ tự quyết nếu không có ý kiến khác

- Ribbon: thêm **nhóm "Khuyến mãi"** trong tab Shopee với một nút "KM của Shop"; bấm mở **dialog** (không phải
  màn con thứ tư) — nhật ký của lượt chạy đổ vào đúng panel nhật ký của tài khoản, chỗ người dùng đang quen nhìn.
- Ô thời gian trên FORM của app: `DatePicker` (ngày) + 2 ô `HH:mm` (WPF không có time picker sẵn). Không cố vẽ
  lại picker của Shopee.
- Tên chương trình **tự sinh lại** khi đổi ngày/giờ, **trừ khi** người dùng đã tự sửa tên (sửa tay thì tôn trọng).
- Lưu cấu hình theo **tài khoản** (bảng mới trong DB orders): bật/tắt tự động · số ngày · % giảm · giờ bắt đầu ·
  giờ kết thúc · mốc chạy gần nhất.
- Luồng KM ghi nhật ký vào **cùng nguồn log của tài khoản** (người dùng theo dõi ở màn Tài khoản như hiện nay).
- **Dùng lại extension `shopee-orders`** + cổng 47821 (xem mục 2). Các bước KM thành module mới
  `extensions/shopee-orders/flow-km.js`, đăng ký thêm lệnh trong `background.js` — cùng khuôn `flow-shop.js`.
- **Không `chrome.debugger` cho các bước KM**: mọi thao tác bằng DOM trong trang (`execInTab`). Ghi rõ trong
  chú thích module: đây là chỗ CỐ Ý khác `flow-orders.js` (bên đó dùng trusted click vì thao tác trên đơn thật),
  và nếu sau này Shopee chặn click tổng hợp thì đó là chỗ đầu tiên phải soi.
- Nút "Chạy chương trình" trên form đi qua **cùng khung `ChayLatCatAsync`** của "Mở list shop" (đã có chốt cửa,
  khoá xuyên máy, dừng/chạy lại check đơn, đóng cửa sổ) — chỉ khác phần thân.
- Khung giờ tự động = `[02:00, 06:00)`, **biên an trước khung = 60 phút**: vòng check đơn nào rơi vào
  `[01:00, 06:00)` bị đẩy sang 06:00. (Vòng check chạy ~30–45' nên biên 60' là chỗ để một vòng đã bắt đầu kịp
  kết thúc trước 2h.)

## 6. Tiêu chí nghiệm thu (khung — chi tiết hoá theo từng pha khi giao)

- Build 0 warning; toàn bộ test xanh; **mọi test mới phải THỬ PHÁ được**.
- Hàm thuần bắt buộc có test ma trận: sinh tên mặc định · cắt 150 ký tự · giờ mặc định 9:30–10:30 · tính mốc chạy
  kế (`mốc trước + xx ngày`, rơi vào khung 2–6h) · đẩy vòng check đơn khỏi khung 2–6h · quyết định dừng/chạy lại
  check đơn.
- Luồng extension: rig test khẳng định **chuỗi lệnh đúng bằng** dãy mong đợi cho một shop có SP và một shop
  KHÔNG có SP (không thừa, không thiếu — khuôn đã dùng ở `MoListShopTests`).
- Chạy app thật: một shop chạy trọn luồng, tạo được chương trình THẬT trên Shopee, nhật ký khớp từng bước; và
  một lượt tự động (giả mốc thời gian) dừng → chạy KM → chạy lại check đơn.

## 7. Câu hỏi còn để ngỏ (chưa chặn, sẽ hỏi khi tới pha tương ứng)

- Shop đã có sẵn một chương trình KM đang chạy/trùng giờ thì làm gì (Shopee thường chặn trùng khung giờ trên cùng
  sản phẩm): bỏ qua shop, hay vẫn tạo và để Shopee báo lỗi rồi ghi log?
- Sản phẩm đang nằm trong chương trình khác sẽ không tick được — chấp nhận tick được bao nhiêu hay coi là lỗi?
- `% giảm` áp CHUNG mọi sản phẩm (bulk) — có shop/sản phẩm nào cần loại trừ không?

---

## 8. Pha 1 — kết quả DÒ DOM THẬT (đêm 18→19/08, 9 lượt chạy trên shop `alina99.store`)

**KHÔNG chương trình khuyến mãi nào được tạo** trong toàn bộ 9 lượt (luôn dừng trước nút "Xác nhận" thanh đáy).

### Đã CHỨNG MINH chạy được bằng DOM thuần (không CDP)

| Bước | Bằng chứng đọc-lại |
|---|---|
| Vào Kênh Marketing → Khuyến Mãi của Shop | URL `…/portal/marketing/list/discount?…` (phải **poll** URL, xem bẫy 3) |
| Bấm "Tạo" → trang `/discount/create` | ok |
| Gõ Tên chương trình | ô đếm `0/150` → **`23/150`** ⇒ Vue NHẬN giá trị JS gán (`nativeSetter` + `input`/`change`) |
| Ô thời gian BẮT ĐẦU | **`09:30 19-08-2026`** (khớp chuỗi đầy đủ) |
| Ô thời gian KẾT THÚC | **`10:30 19-08-2026`** ⇒ **9:30→10:30 CÙNG NGÀY được phép** |
| Chọn sản phẩm | **24 trang**, tick từng trang, đếm **cộng dồn** `10 → 240/1000`; 10 SP/trang (20 `<tr>` vì bảng có cột trái cố định) |
| Xác nhận modal | `nutXacNhanModal.ok = true` ⇒ sản phẩm VÀO form (đếm được **117** ô `%GIẢM`) |
| Gõ `5` vào ô `%GIẢM` hàng loạt | `docLai: "5"`; nút "Cập nhật hàng loạt" **hết `disabled`** |

⇒ Quyết định "không dùng CDP" của người dùng **đứng vững**: kể cả `eds-date-picker` (component Vue tự vẽ, không phải `<input>`) cũng điều khiển được bằng click DOM.

### CHƯA giải được — mắt xích cuối

Bấm "Cập nhật hàng loạt" xong **bảng KHÔNG đổi**, vì khối thiết lập vẫn ghi **"0 sản phẩm đã chọn"**: chưa biết
cách chọn dòng trong bảng của FORM. Bảng đó **không có `<thead>`** (`soThead: 0`), và hai lượt dò cuối trả
`soCheckboxNgoaiModal: 0`, `khoiBang: []` — **mâu thuẫn** với lượt trước đó đếm được 117 ô `%GIẢM` cùng thời
điểm. Nghĩa là phép đo còn phụ thuộc thời điểm render, chưa tin được. **Việc đầu tiên của lượt sau: dựng hàm
CHỜ-ĐIỀU-KIỆN dùng chung** (chờ tới khi phần tử/số liệu thoả điều kiện rồi mới đọc) thay vì rải `sleep` cố định,
rồi mới dò lại cấu trúc bảng trong form.

### BỐN cái bẫy đã trả giá — pha 3 phải tránh

1. **Nhiều bản cùng loại nằm ẩn trong DOM.** Hai `eds-date-picker` (98 ô ngày, 4 cột giờ/phút) và nhiều
   `.eds-modal` (có modal hướng dẫn với nút "xem chi tiết"/"quay lại trang danh sách"). Lấy "cái đầu tiên" SAI;
   lấy "cái đang hiện" VẪN SAI với modal. Phải **neo theo nội dung đặc trưng** — modal chọn sản phẩm là modal
   duy nhất chứa `.select-tips` ("Đã chọn N/1000 sản phẩm").
2. **Phép so lỏng làm báo cáo NÓI DỐI.** `ok` từng so "có chứa": kỳ vọng `'doi'` mà đọc `'khong-doi'` vẫn tính
   là khớp ⇒ báo `ok` cho bước thất bại. Nay mặc định **so BẰNG**, muốn "chứa" phải khai `kieuSo: "chua"`.
   Kỳ vọng cũng phải là **chuỗi đầy đủ** (`09:30 19-08-2026`), không phải mảnh (`09:30`) — mảnh đã che mất lỗi
   ngày kết thúc lệch ngày.
3. **Đo quá sớm ⇒ kết luận sai.** Bước 1 đọc URL ngay sau click → bắt được `banhang.shopee.vn/` rồi báo hỏng dù
   thao tác đúng; bước 6 đếm dòng form ngay sau khi đóng modal → `0`.
4. **Bản publish có thư mục `extensions` RIÊNG.** App đi ngược từ thư mục exe lên, gặp
   `publish\ShopeeSuite\extensions\` là dừng — sửa trong repo KHÔNG có tác dụng. Hai lượt chạy đã lãng phí vì
   chuyện này. Sửa extension xong phải đồng bộ sang publish (hoặc publish lại) rồi mới test.

### Hai dữ kiện vận hành

- **Shopee chặn lưu lượng** một lần (`shopee.vn/verify/traffic/error`) khi mở lại Chi tiết **cùng một shop 2 lần
  trong 17 phút** cộng 4 lượt SSO trong 20 phút. Tài khoản KHÔNG bị khoá (thử lại vào bình thường ngay sau đó).
  ⇒ Pha 3 phải giữ nhịp người: nghỉ 3–5' giữa shop, và **không mở lại cùng một shop trong thời gian ngắn**.
- **Trần chọn sản phẩm là `/1000`** (Shopee hiển thị), không phải 10000.

### Dừng máy khác qua hub — đã dùng thật, có đường sẵn

`HubDatabase.CreateOrdersCommand(machineId, login, 'stop')` → client nhận ở nhịp heartbeat → `Sessions.Stop`.
Đêm 19/08 đã dùng để dừng máy `Ng Xuân Mùi` (`68bebf7a…:orders`): **ack sau 3 giây**, lease `orders:<login>` nhả
ngay. DB hub ở `/var/lib/shopee-hub/hub.db` trên `vps-muinx`, ghi cần `sudo -u shopeehub` (user thường chỉ đọc).

---

## 9. PHA 1 XONG — luồng chạy trọn vẹn, đã tạo chương trình THẬT (19/08 01:34)

Lượt `km-dom-20260819-013403.json`: **9/9 bước thao tác thành công**, kết thúc bằng chương trình có thật trên
shop `1843718137`: **`DO DOM KM 1787077958307`** · "Sắp diễn ra" · Chương Trình Của Shop · **240 sản phẩm** ·
19/08 09:30–10:30 · giảm 5%. Xác minh bằng ảnh chụp màn hình trang `/portal/marketing/list/discount`, không chỉ
bằng "URL đã rời trang tạo" (bấm Hủy cũng rời trang — thước đó một mình thì yếu).

### Chốt đường đi (đừng dò lại)

| Bước | Cách làm ĐÃ CHẠY |
|---|---|
| Vào Khuyến Mãi | **CHỜ sidebar render** rồi bấm `li.ps_menu_marketing a[href="/portal/marketing/list/discount"]` |
| Tạo | `pageClickTheoSelector(["button"...], "tạo")` |
| Tên / thời gian | gán `value` + bắn `input`/`change`; picker chọn ngày + 2 cột giờ-phút + "Xác nhận" |
| Chọn sản phẩm | modal neo theo `.select-tips`, tick check-all từng trang, hết trang bấm "Xác nhận" |
| Áp % | gõ vào `.detail-batch .discount-box input` rồi bấm **"Cập nhật hàng loạt"** |
| Chốt | **CUỘN xuống** (thanh đáy chỉ hiện khi đã cuộn) rồi bấm `.fix-container.fixed-bottom .confirm-btn` |

### HAI đính chính quan trọng — người dùng chỉ ra, tôi đã kết luận SAI

1. **KHÔNG cần chọn sản phẩm trong form.** Nguyên văn: *"nếu bấm vào cập nhật hàng loạt thì không phải chọn sp,
   nếu chọn từng sp thì chỉ cập nhật cho các sp đã chọn"*. Dòng "0 sản phẩm đã chọn" là trạng thái BÌNH THƯỜNG.
   Tôi đã đuổi theo hướng "phải tick được dòng trong bảng form" suốt hơn một giờ — sai hoàn toàn.
2. **Đường vào không phải link submenu ẩn.** Người dùng chỉ đúng: thẻ `#discount_promotion-all .marketing-card`
   là `div` có handler Vue, KHÔNG có `href` — nên selector `a[href*=...]` cũ **không bao giờ trúng thẻ**, nó trúng
   link sidebar. (Thử đường thẻ ở lượt 01:26: bấm được nhưng URL đứng nguyên `/portal/marketing` ⇒ thẻ không
   điều hướng trong cùng tab. Đang dùng link sidebar — cũng là thao tác bấm tay, đã chạy 8 lượt.)

### NĂM cái thước hỏng trong một đêm — luật cho pha 3

Cả năm đều báo "ok" hoặc "hỏng" mà **không thật sự đo được thứ nó khẳng định**:

| # | Thước hỏng | Vì sao vô nghĩa |
|---|---|---|
| 1 | so `ok` bằng "có chứa" | `'khong-doi'` chứa `'doi'` ⇒ báo ok cho bước hỏng |
| 2 | đọc URL ngay sau click | trang còn đang điều hướng ⇒ báo hỏng cho bước đúng |
| 3 | so bảng bằng `textContent` | `textContent` KHÔNG chứa `input.value` ⇒ luôn "KHONG_DOI" |
| 4 | duyệt `tbody tr` | bảng form không có tbody/tr (dòng là div) ⇒ `[] === []` ⇒ luôn "KHONG_DOI" |
| 5 | bước 6 đọc `.select-tips` | đọc sau khi modal đã đóng ⇒ luôn 0, dù sản phẩm ĐÃ vào form (**CHƯA SỬA**) |

Hai kiểu: **đo cái không chứa thông tin** (1, 3, 4) và **đo đúng chỗ nhưng sai thời điểm** (2, 5).

⇒ **Luật pha 3: mỗi phép đọc-lại phải nêu được NÓ ĐỎ KHI NÀO.** Không nêu được thì không tính là bằng chứng —
đúng tinh thần luật "thử phá" của repo, áp cho cả phép đo trong extension chứ không riêng unit test.

### Còn nợ

- **Sửa thước bước 6** (đo số sản phẩm đã vào form bằng số ô `%GIẢM`, không đọc `.select-tips` sau khi modal đóng).
  Để nguyên thì luồng thật sẽ tưởng shop không có sản phẩm và **bỏ qua oan**.
- Bỏ nút tạm `DoDomKmButton`.
- Pha 3: vòng 12 shop + nghỉ 3–5' + không mở lại cùng shop trong thời gian ngắn.
- Pha 4: hẹn giờ 2–6h sáng, chặn vòng check đơn vào khung đó, xong thì khởi động lại check đơn.

## 10. Shop thứ HAI — và cái lỗi chỉ shop nhỏ mới lộ (19/08 01:44–01:47)

Chạy shop `#2/12` = **`Shop9x Store`** (id `1843715002`), **chỉ có 1 sản phẩm**. Kết quả: tạo thành công
**`DO DOM KM 1787078799321`** · "Sắp diễn ra" · 1 sản phẩm · 19/08 09:30–10:30 (xác minh bằng ảnh chụp danh sách).

Nhưng lượt ĐẦU của shop này **chết ở bước 9**, và lỗi đáng ghi:

> **Điều kiện CHỜ chặt hơn điều kiện HÀNH ĐỘNG.** Vòng chờ bắt buộc `.fix-container.fixed-bottom .confirm-btn`,
> trong khi lúc click lại chấp nhận cả `.fix-container .confirm-btn`. Trang 1 sản phẩm thì NGẮN — không cuộn
> được — nên Shopee **không gắn class `fixed-bottom`** ⇒ vòng chờ loại đúng cái nút mà bước bấm sẵn sàng dùng,
> và bỏ cuộc trước khi bấm. Shop 240 sản phẩm không lộ lỗi này vì trang dài, class có đủ.

⇒ **Luật:** điều kiện chờ và điều kiện hành động phải nói về **CÙNG một tập phần tử**. Đây là họ hàng gần của 5
cái thước hỏng ở §9 — vẫn là "hai câu lệnh nói về hai tập khác nhau mà mình tưởng là một".

⇒ **Hệ quả cho pha 3:** 12 shop sẽ có shop nhiều sản phẩm, shop 1 sản phẩm, và **shop rỗng** (chưa gặp).
Mới chứng minh được 2 trong 3 hình dạng. Shop rỗng phải test riêng trước khi thả vòng 12 shop.

### Sửa kèm trong đợt này

- **Thước bước 6 đã xanh** sau khi đổi từ `tbody tr` (không tồn tại) sang đếm ô `%GIẢM`: shop 240 SP đọc 240,
  shop 1 SP đọc 1. Trước đó bước 6 **luôn đỏ** kể cả lượt tạo thành công.
- **Nút dò nay đi shop khác mỗi lần bấm** (`viTriShop`, quay vòng theo `shops.Count`), mồi bằng biến môi trường
  `KM_VITRI_SHOP`. Lý do: mở lại CÙNG shop trong thời gian ngắn là thứ đã làm Shopee bật `verify/traffic/error`.
- **Sửa câu log nói sai:** "KHÔNG chương trình nào được tạo" — sai từ khi có bước 9. Nay nói rõ lượt chạy TẠO THẬT.
- Build App: **0 warning**.

### Tự làm, KHÔNG có kiểm độc lập

Toàn bộ đợt 19/08 rạng sáng do phiên chính tự viết, tự chạy, tự chấm — **chưa qua `nghiem-thu` / `phan-bien`**.
Bằng chứng là log chạy thật + ảnh chụp, không phải test tự động. Chưa chạy lại bộ test của repo sau các sửa đổi
ở `OrdersBridgeSession` / `AccountsViewModel.Phien` (`KmDoDomTests` khoá chuỗi lệnh `[readShopList,
openShopDetail, dumpKm]` — thêm tham số `viTriShop` có giá trị mặc định nên KỲ VỌNG vẫn xanh, nhưng **chưa chạy
để xác nhận**).

---

## 11. PHA 3 — nối ribbon + form vào luồng thật (19/08 02:0x)

### Đã làm

| Chỗ | Việc |
|---|---|
| `flow-km.js` | Chuỗi 9 bước thành **hàm dùng chung có tham số** `chayKmChoTabDangMo(ts)` — `dumpKm` và `chayKm` gọi CÙNG một hàm, không chép hai bản |
| `flow-km.js` | **Gõ tiêu đề như người**: `pageGoNhuNguoi` gõ từng ký tự, nhịp ngẫu nhiên `nhip/2…nhip*1.5`, chèn nhịp khựng dài mỗi ~8–14 ký tự |
| `flow-km.js` | **Shop rỗng ⇒ dừng ngay sau bước 6**, trả `boQua`, KHÔNG tạo chương trình trống |
| `background.js` | Lệnh mới `chayKm` (kèm `thamSo`), trả gói `kmKetQua` |
| `OrdersBridgeChannel` | Chặng `KmChay` (240s) + `ArmKmChay()` + nhận `kmKetQua` |
| `OrdersBridgeSession` | `ChayKmMoiShopAsync` — lặp MỌI shop, dùng lại `NghiGiuaShopAsync` (3–5') và `DongTabShopAsync` sẵn có |
| `AccountsViewModel` | Form "Chạy chương trình" → chạy thật; `ChayLatCatAsync(dongCuaSoKhiXong: true)` |

**Hai quyết định đáng ghi:**

1. **Một shop hỏng KHÔNG giết cả lượt** — ghi lỗi vào kết quả shop đó rồi đi tiếp. 12 shop mà chết vì shop thứ 2
   thì 10 shop sau mất chương trình. Riêng **captcha thì dừng hẳn**: đi tiếp lúc Shopee đang nghi là tự khai bot.
2. **`dongCuaSoKhiXong: true`** — khác "Mở list shop" (cố ý để cửa sổ lại cho người dùng). Luồng KM không có ai
   đóng hộ: để nguyên là **mượn-không-trả**, khoá xuyên máy kẹt tới lúc tắt app và check đơn KHÔNG BAO GIỜ chạy
   lại — trái đúng yêu cầu "xong km thì chạy lại flow check đơn".

### Kiểm chứng

- Build solution: **0 warning**.
- **16 test mới** (`KmVongMoiShopTests`) cho hai chỗ dễ nói dối nhất: phân biệt TẠO / BỎ QUA / HỎNG, và câu tổng kết.
- **THỬ PHÁ 4 lần, đỏ cả 4** (shop rỗng→tính thành lỗi · nuốt tên shop hỏng · bỏ cảnh báo captcha · nuốt gói JSON
  hỏng thành "tạo được"), khôi phục lại xanh.
- Toàn bộ: **1896/1897**. Một đỏ là `ActivityLogTests.Append_5000DongTu5Luong…` — test ĐO TỐC ĐỘ, chạy riêng thì
  xanh (11/11); nó đỏ khi máy đang chạy build + Brave. Không liên quan thay đổi đợt này.

### CHƯA làm — đừng tưởng đã xong

- **Pha 4 (lịch tự động) CHƯA CÓ.** `SoNgay` + `LanChayCuoi` đã lưu và câu tổng kết tính được "lần kế dự kiến",
  nhưng **không có bộ đếm giờ nào tự kích hoạt**, và vòng check đơn **chưa bị chặn** khỏi khung 2–6h sáng.
  Nghĩa là "chạy tự động mỗi xx ngày" hiện **chỉ là con số hiển thị**, chạy được là do bấm tay.
- **Vòng 12 shop CHƯA chạy thật lần nào** — mới chứng minh từng shop lẻ (240 SP và 1 SP). Chạy trọn mất ~50–60'
  và tạo 12 chương trình thật.
- **Shop RỖNG chưa gặp lần nào** — nhánh `boQua` mới chỉ có test, chưa có bằng chứng chạy thật.
- Nút tạm `DoDomKmButton` vẫn còn.
- **Chưa commit. Chưa qua `nghiem-thu` / `phan-bien`** — toàn bộ do phiên chính tự làm tự chấm.

---

## 12. PHA 4 — lịch tự động 2–6h sáng, mỗi xx ngày (19/08 chiều)

### Cách làm

`KmLichTuDong` (Core) — **toàn bộ là hàm thuần, nhận `bayGio` từ ngoài, không đụng đồng hồ hệ thống**. Đây là
điều kiện bắt buộc chứ không phải sở thích: luật "2–6h sáng, xx ngày một lần" **không kiểm bằng tay được** trừ
khi thức tới 2h sáng và chờ đúng ngày thứ xx. Nhận `bayGio` làm tham số là cách duy nhất test được mọi ca trong
một phần nghìn giây.

| Hàm | Luật |
|---|---|
| `TrongKhung(bayGio)` | `2 <= giờ < 6` — 06:00 KHÔNG thuộc khung |
| `DenGioChay(cf, bayGio)` | bật tự động **và** trong khung **và** `bayGio >= LanChayCuoi + SoNgay` (kéo về 02:00) |
| `MocChayKe(cf, bayGio)` | mốc kế để hiển thị; chưa chạy lần nào ⇒ khung 2h GẦN NHẤT (không nổ ngay khi vừa bật) |
| `HoanVongCheckDon(moc, kmTuDong)` | mốc vòng check đơn rơi vào 2–6h ⇒ dời tới **06:00** |

Ba quyết định đáng ghi:

1. **`Math.Max(1, SoNgay)`** — cấu hình rác (0 ngày) KHÔNG được biến thành "chạy lại mỗi nhịp 60 giây", tức 12
   shop mỗi phút.
2. **Mốc đã trôi qua thì KHÔNG dồn lượt bù.** App tắt hai tuần rồi mở lại ⇒ chạy MỘT lượt ở khung gần nhất,
   không chạy bù 4 lượt đã lỡ.
3. **Tắt KM tự động thì KHÔNG chặn khung.** Cấm check đơn chạy 2–6h cho một tài khoản chẳng bao giờ chạy KM là
   tự cắt 4 tiếng mỗi đêm không vì cái gì.

Nối vào hai chỗ: `AccountSession` (vòng check đơn hoãn mốc rơi vào khung, có ghi nhật ký nói rõ lý do) và
`AccountsViewModel.NhipLichKmAsync` treo vào **đồng hồ 60 giây SẴN CÓ** (`_timerSangNgay`) — không dựng đồng hồ
thứ hai, vì hai đồng hồ là hai chỗ phải nhớ dọn khi thoát app, quên một cái là rò bộ nhớ im lặng.
`ChayLatCatAsync` nhận thêm `accountIdChiDinh` vì lượt tự động chạy lúc 2h sáng, không có "tài khoản đang chọn"
đáng tin.

### Kiểm chứng

- **19 test mới** (`KmLichTuDongTests`), **THỬ PHÁ 5 ca, đỏ cả 5**: lệch giờ đóng khung (2–7h) · bỏ kiểm khung
  (KM nổ giữa ban ngày) · bỏ kiểm cờ tự động · `SoNgay=0` không còn coi là 1 · hoãn vòng cả khi đã tắt KM.
- Build solution **0 warning**; toàn bộ **1916/1916 xanh**.

### Một cái bẫy của chính bộ thử phá — ghi để lần sau khỏi mất công

Lượt khôi phục dùng `Copy-Item`, mà **`Copy-Item` giữ nguyên timestamp cũ** ⇒ MSBuild tưởng file không đổi,
dùng lại DLL còn chứa code đã phá ⇒ test báo đỏ SAU KHI đã khôi phục. Sờ lại timestamp thì 19/19 xanh. Năm lượt
phá dùng `Set-Content` (timestamp mới) nên có build lại thật — kết quả đỏ của chúng vẫn có giá trị.
**Bài học: bộ thử phá cũng cần được kiểm rằng nó thật sự build lại**, kẻo "đỏ" và "xanh" đều là của bản cũ.

### Sự cố môi trường (không phải code)

Sáng 19/08, **SDK .NET biến mất khỏi `C:\Program Files\dotnet`** (chỉ còn runtime 8.0.30); SDK 8.0.423 nằm ở
`%LOCALAPPDATA%\Microsoft\dotnet`, mà `dotnet` trên PATH lại trỏ Program Files trước ⇒ mọi lệnh `dotnet` báo
"SDK not found". Đang đi vòng bằng cách gọi thẳng
`C:\Users\<user>\AppData\Local\Microsoft\dotnet\dotnet.exe`. **Cần sửa PATH hoặc cài lại SDK.**

### CÒN LẠI

- **Lịch tự động chưa từng chạy thật** — mới có test. Bằng chứng thật sớm nhất là rạng sáng mai.
- **Vòng 12 shop chưa chạy thật lần nào** (mới chứng minh shop lẻ: 240 SP và 1 SP).
- **Shop RỖNG chưa gặp thật** — nhánh `boQua` mới chỉ có test.
- Nút tạm `DoDomKmButton` vẫn còn. **Chưa commit. Chưa qua `nghiem-thu` / `phan-bien`.**

---

## 13. Ẩn nút chẩn đoán — GIỮ code, không xoá (19/08 tối)

Người dùng chốt: *"những nút test bạn ẩn đi, để phòng khi cần phải sửa lại dom khi shopee thay đổi"*.
**Thay cho mục "bỏ nút tạm `DoDomKmButton`" ở §9/§11** — nút KHÔNG bị gỡ nữa.

- Ẩn bằng `Visibility="{Binding HienNutDev, Converter={StaticResource BoolToVis}}"`.
- `AccountsViewModel.HienNutDev` đọc biến môi trường **`SHOPEE_DEV_TOOLS=1`** (chấp cả `true`/`TRUE`).
- **Vì sao biến môi trường chứ không phải ô tick trong Cài đặt:** bật lại KHÔNG cần build (máy dev 19/08 đang
  hỏng SDK .NET), và người dùng thường không vô tình bật trúng.
- Chỉ có MỘT nút test còn gắn UI. `ChayThuBridge` đã bị gỡ khỏi giao diện từ trước, lệnh vẫn còn trong VM.

**Sửa kèm — hai chỗ chú thích/tooltip đã thành lời nói dối** từ khi thêm bước 9: cả hai đều ghi *"KHÔNG tạo
chương trình khuyến mãi nào"*, trong khi nút nay **CÓ TẠO thật**. Người bấm tin là an toàn thì đó là kiểu sai
nguy hiểm nhất ở đây. Nay tooltip nói rõ **"⚠ CÓ TẠO chương trình khuyến mãi THẬT"**, và có test khoá lại.

### Kiểm chứng

- **10 test mới** (`NutChanDoanAnTests`): mặc định ẩn · đúng giá trị biến môi trường (`1`/`true`/`TRUE` bật,
  `0`/rỗng/`bat` tắt) · XAML phải ràng buộc `Visibility`→`HienNutDev` · tooltip phải cảnh báo CÓ TẠO THẬT ·
  lệnh `DoDomKmCommand` phải còn tồn tại.
- **THỬ PHÁ 4 ca:** bỏ ràng buộc `Visibility` → **đỏ**; tooltip quay về lời nói dối → **đỏ**; cờ luôn `true`
  → **đỏ 4 test**. Ca "xoá/đổi tên nút" **không biên dịch được** — `KmDoDomTests.cs:217` bám thẳng vào field
  sinh từ `x:Name`, tức đã có sẵn một chặn CỨNG hơn cả test chuỗi.
- Chạy thật hai chiều trên app đã publish: **không có biến** ⇒ UIA không thấy nút; **`SHOPEE_DEV_TOOLS=1`**
  ⇒ nút xuất hiện trở lại. App hiện đang chạy ở chế độ ẩn.
- Build solution **0 warning**; toàn bộ **1926/1926 xanh**.

### Một cái bẫy hạ tầng đã cắn hai lần trong đợt này

`testhost` mồ côi (PID 22204, 18496) giữ `XuLyDonShopee.App.dll` ⇒ build báo `MSB3027 file is locked`, và bộ
thử phá bằng Node **không đọc được kết quả** (im lặng, không phải báo lỗi). Dọn tiến trình rồi build lại thì
0 warning. Cùng họ với bẫy `Copy-Item` giữ timestamp ở §12: **bộ kiểm chứng hỏng thì mọi kết luận rút ra từ nó
đều vô giá trị, kể cả kết luận "xanh"**.
