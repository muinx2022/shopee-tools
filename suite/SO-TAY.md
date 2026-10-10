# Sổ dọc đường — shopee-suite

> Lập 2026-08-27.
>
> **Đây là SỔ, không phải kế hoạch.** Không mục nào ở đây đã được duyệt để code.
> Nó là chỗ ĐỖ cho mọi thứ phát hiện dọc đường mà **không thuộc phạm vi việc đang làm** —
> ghi xuống rồi quay lại việc cũ ngay, để không ai phải chọn giữa "quên nó đi" và "bỏ dở
> việc đang làm". Luật đầy đủ: `D:\Projects\CLAUDE.md`, mục *Một việc một lúc*.
>
> **Cách dùng**
> - Chỉ **THÊM vào cuối**. Không sửa/sắp lại mục cũ khi đang làm việc khác — máy này hay
>   có nhiều phiên chạy song song trên cùng repo.
> - **Phiên chính là người ghi duy nhất.** Subagent nêu ở mục `## NGOÀI PHẠM VI` cuối báo
>   cáo, phiên chính chép vào đây ở chặng 5.
> - Mục `MỞ` chỉ được lôi ra làm khi **user duyệt** ở chặng 1 của một việc mới. Không tự nhặt.
> - Sửa xong thì đổi trạng thái **tại chỗ**, đừng xoá — lịch sử lỗi là thứ dạy được nhiều nhất.
>
> Mã: `P-YYYYMMDD-n` (theo ngày, không phải số chạy toàn file — hai phiên song song không giành nhau).
> Trạng thái: `MỞ` · `ĐANG SỬA` · `ĐÓNG (<commit>)` · `KHÔNG SỬA (<lý do>)`.
> Hạng: **CHẶN** (không ra mắt được) · **NẶNG** · **VỪA** · **NHỎ**.

## Khuôn một mục — chép nguyên rồi điền

```markdown
### P-20260827-1 · [MỞ] · VỪA — <một câu: vấn đề là gì>
- **Thấy lúc**: đang làm `plans/2026-08-27-<việc>.md` (hoặc: trả lời câu hỏi X của user)
- **Ở đâu**: `đường/dẫn/file.ts:123`
- **Bằng chứng**: <ca cụ thể vào-gì-ra-gì, hoặc output lệnh — không phải cảm nhận>
- **Vì sao không sửa ngay**: ngoài phạm vi việc đang làm
```

Không dựng nổi bằng chứng cụ thể ⇒ ghi rõ là **nghi ngờ**, đừng ghi như lỗi chắc chắn.
Sổ đầy phỏng đoán thì mục thật bị loãng, và loãng đủ lâu thì cả sổ bị bỏ.

---

## Các mục

### P-20260829-1 · [ĐÓNG (c1b1cf4 + estate 30/08)] · VỪA — Ba file luồng KM/Combo/FlashSale chạy thật hằng đêm mà CHƯA TỪNG được commit
- **Thấy lúc**: đang lập `plans/2026-08-29-refactor-flow-km-combo.md` (khảo sát 4 góc)
- **Ở đâu**: `extensions/shopee-orders/flow-km.js` · `flow-combo.js` · `flow-flashsale.js` (git `??`); kèm `background.js`/`core.js`/`flow-shop.js` đang `M`
- **Bằng chứng**: `git status --short -- extensions/shopee-orders/` → `?? flow-km.js · ?? flow-combo.js · ?? flow-flashsale.js`
- **Vì sao không sửa ngay**: cần user cho commit (luật "không commit trừ khi user bảo"); một `git clean`/hỏng đĩa là mất trắng luồng sản xuất — đề xuất commit mốc SỚM bất kể có refactor hay không

### P-20260829-2 · [ĐÓNG (9666b32, 30/08)] · VỪA — `datOThoiGian` của combo KHÔNG có guard "chọn ngày hỏng ⇒ KHÔNG bấm Xác nhận" như bản km
- **Thấy lúc**: đang lập `plans/2026-08-29-refactor-flow-km-combo.md` (so hai bản song sinh)
- **Ở đâu**: `extensions/shopee-orders/flow-combo.js:989-1003` (tuyến tính, ngày hỏng vẫn bấm tiếp giờ/phút/Xác nhận) vs `flow-km.js:1296-1310` (guard, vá 27/08)
- **Bằng chứng**: km:1296-1310 có nhánh "chọn NGÀY hỏng ⇒ DỪNG NGAY, KHÔNG bấm Xác nhận" (vá từ dump `logs/km-dom-20260827-091851.json` — bấm Xác nhận trên picker chưa chọn được gì là chốt giá trị CŨ); combo không có nhánh tương ứng
- **Vì sao không sửa ngay**: là SỬA HÀNH VI luồng combo đang chạy đêm, không phải refactor — cần user quyết

### P-20260829-3 · [MỞ] · NHỎ — km bước 9 quét thông báo trang bằng page-func inline, combo đã có `pageDocBaoTrenTrang` quét cả hai bộ selector
- **Thấy lúc**: đang lập `plans/2026-08-29-refactor-flow-km-combo.md`
- **Ở đâu**: `extensions/shopee-orders/flow-km.js:1789-1795` (inline, chỉ `.eds-*`) vs `flow-combo.js:735-742` (`.eds-react-*` lẫn `.eds-*`)
- **Bằng chứng**: hai bản quét khác tập selector — bản km sẽ mù với thông báo React nếu Shopee trộn component; đúng mẫu "hai bản trôi lệch" mà chính flow-km cảnh báo
- **Vì sao không sửa ngay**: ngoài phạm vi refactor giữ-hành-vi; đổi tập selector của km là đổi hành vi

### P-20260829-4 · [MỞ] · NHỎ — NGHI NGỜ: nhánh LÙI của `pageBamChuyenThang` (km) chưa bao giờ chạy thật
- **Thấy lúc**: đang lập `plans/2026-08-29-refactor-flow-km-combo.md`
- **Ở đâu**: `extensions/shopee-orders/flow-km.js:837` (nhánh `tien=false`)
- **Bằng chứng**: chính `FlashSaleDoDomTests.cs:898-899` tự ghi nhận "flow-km.js có cùng lỗi tiềm ẩn: nhánh lùi… chưa bao giờ chạy thật" — có lưới test nguồn nhưng chưa có lượt chạy thật phủ. NGHI NGỜ, chưa dựng được ca hỏng cụ thể
- **Vì sao không sửa ngay**: chưa có bằng chứng hỏng; cần một lượt đo thật (shop có mốc lùi tháng) trước khi kết luận

### P-20260829-5 · [MỞ] · NHỎ — `dotnet build ShopeeSuite.sln` không đo được 0-warning khi app desktop đang chạy
- **Thấy lúc**: thực thi plan `plans/2026-08-29-flash-sale-sap-xep-truoc-khi-lat-trang.md` (cả opus-dev lẫn nghiệm thu cùng gặp)
- **Ở đâu**: `suite/Shopee.Suite/Shopee.Suite.csproj` (thư mục output bị `ShopeeSuite.exe` giữ)
- **Bằng chứng**: `dotnet build -v:m` → 70 Warning / 14 Error, TOÀN BỘ là MSB3021/MSB3026/MSB3027 "The file is locked by: ShopeeSuite (31560)"; 11/11 project còn lại build riêng 0/0
- **Vì sao không sửa ngay**: không tắt app đang là phiên làm việc của user; chỉ cần build lại khi app tắt

### P-20260829-6 · [MỞ] · NHỎ — Hai file test TRÙNG TÊN khác nội dung hoàn toàn `FlashSaleLuongThatTests.cs` ở hai project
- **Thấy lúc**: nghiệm thu plan flash sale 29/08
- **Ở đâu**: `orders/XuLyDonShopee.Tests/FlashSaleLuongThatTests.cs` (test cầu nối C#) vs `suite/Shopee.Core.Tests/FlashSaleLuongThatTests.cs` (test Jint/AST)
- **Bằng chứng**: `diff -u` hai file khác 100% từ dòng 1; dễ mở nhầm/sửa nhầm khi nghiệm thu
- **Vì sao không sửa ngay**: đổi tên file test là việc riêng, cần quyết tên mới cho khỏi lệch quy ước

### P-20260829-7 · [MỞ] · NHỎ — NGHI NGỜ: định dạng nhãn trang active của modal Flash Sale chưa từng được đo
- **Thấy lúc**: phản biện + lượt delta plan flash sale 29/08
- **Ở đâu**: `extensions/shopee-orders/flow-flashsale.js` (`pageDemTickFlash` → `trangHienTai`; luật `cuaDungTrangDich`)
- **Bằng chứng**: grep toàn repo không có số đo nào về nhãn pager Vue của modal này; luật hiện chỉ kết tội nhãn SỐ THUẦN lệch, nhãn lạ chỉ cảnh báo kèm nguyên văn — lượt chạy thật đầu trên khung ≥2 sẽ tự đẻ ra số đo trong `canhBao`
- **Vì sao không sửa ngay**: đúng-sai chỉ biết sau lượt chạy thật; đọc nhật ký rồi mới siết

### P-20260829-8 · [ĐÓNG (32fc87b, 30/08 — commit toàn bộ estate)] · VỪA — HEAD chưa TỰ CHỨA: hai file test nền vẫn untracked trong khi test đã commit gọi helper của chúng
- **Thấy lúc**: nghiệm thu plan `2026-08-29-refactor-flow-km-combo.md`
- **Ở đâu**: `suite/Shopee.Core.Tests/EdsONgayHopLeTests.cs` + `FlashSaleDoDomTests.cs` (git `??`); `Shopee.Core.Tests.csproj` đang `M`
- **Bằng chứng**: `FlashSaleLuongThatTests.cs` và `FlowChungTests.cs` (ĐÃ commit) gọi `FlashSaleDoDomTests.NguonHam/NguonFile/DuongFile` — máy checkout mới sẽ không compile được project test
- **Vì sao không sửa ngay**: hai file `??` + csproj `M` có thể thuộc đợt làm dở của phiên khác — cần user xác nhận rồi cho commit bổ sung

### P-20260829-9 · [MỞ] · NHỎ — Nhánh captcha-TRƯỚC-bước của `buoc` hardcode tiền tố `[KM]` bất kể `nhan`
- **Thấy lúc**: phản biện plan refactor 29/08
- **Ở đâu**: `extensions/shopee-orders/flow-chung.js` (nhánh verify đầu hàm `buoc`; có từ `b28370a:flow-km.js:1174`)
- **Bằng chứng**: lượt Combo/Flash rơi `/verify` trước bước sẽ in `[KM] <tên bước>: DỪNG…` — sai luồng trên nhật ký; nhánh progress thường thì đã dùng `[${nhan}]`
- **Vì sao không sửa ngay**: thân `buoc` bị khoá "THÂN HÀM KHÔNG ĐƯỢC SỬA" (luồng KM chạy thật hằng đêm) — sửa cần lượt riêng có user gật

### P-20260829-10 · [ĐÓNG (8f06ce0, 30/08)] · NHỎ — `.gitattributes` không khai `eol` cho `*.js` (máy đang `core.autocrlf=true`)
- **Thấy lúc**: thực thi + phản biện plan refactor 29/08
- **Ở đâu**: `.gitattributes` (chỉ khai `*.sh`/`*.cmd`/`*.bat`)
- **Bằng chứng**: mọi lệnh git trên file JS đều cảnh báo "LF will be replaced by CRLF"; clone mới sẽ ra CRLF — `NguonHang` của FlowChungTests đã vá phòng thủ, nhưng các test đọc-nguồn khác có thể còn chỗ nhạy CRLF
- **Vì sao không sửa ngay**: sửa `.gitattributes` ảnh hưởng cả repo (ngoài phạm vi 3 flow); cần user quyết chuẩn eol

### P-20260830-1 · [ĐÓNG (deploy hub 30/08 ~10:50)] · NẶNG — Hub VPS bản cũ VỨT cờ `FlashBat` khi đồng bộ hồ sơ KM: mọi cú bật flash trên client bị đè ngược về TẮT trong ≤60 giây
- **Thấy lúc**: thực hiện lệnh "sửa để 21h chạy được" 30/08 — bật cờ qua form, lưu thành công (10:41:57) rồi 2 giây sau nhịp kéo hub đè về 0
- **Ở đâu**: hub VPS `/opt/shopee-hub` (bản deploy trước 28/08, thiếu 3 cột flash + trường DTO); code server MỚI có sẵn ở `server/Shopee.Hub.Web/Data/HubDatabase.KmProfiles.cs` (untracked) với `AddColumnIfMissing("km_profiles","flash_bat",…)`
- **Bằng chứng**: log 10:37/10:39/10:41 "Hồ sơ KM đồng bộ từ hub: cấu hình vừa ĐỔI — … flash sale TẮT"; `cfg_updated_at` sau revert MẤT hậu tố `+07:00` (bản ghi từ hub). Sau deploy (test hub 204/204 xanh, health ok): bật lại 10:50:14, qua ≥2 nhịp vẫn `flash_bat=1`, không còn dòng revert
- **Vì sao đóng được ngay**: memory "Deploy Hub làm thẳng"; đây chính là phần server còn thiếu của feature flash 28/08 chưa từng được deploy

### P-20260830-2 · [MỞ] · NHỎ — UIA `TogglePattern.Toggle()` đổi trạng thái CheckBox trên UI nhưng KHÔNG đẩy được vào ViewModel (binding không nhận) ở app WPF này
- **Thấy lúc**: 30/08, hai lần bật `FlashBatCheck` qua TogglePattern — UI báo On, `vm.FlashBat` vẫn false ⇒ `BanLuuSauForm` thấy "không sửa gì" và bỏ lưu im lặng
- **Ở đâu**: form `KmDialog` (`FlashBatCheck`); nghi áp cho mọi CheckBox styled của app
- **Bằng chứng**: 2 lần TogglePattern ⇒ DB không đổi; lần 3 click chuột VẬT LÝ (SetCursorPos + mouse_event) vào cùng checkbox ⇒ lưu ăn ngay (`flash_bat=1`, 10:41:57)
- **Vì sao không sửa ngay**: là bài học tự động hoá/nghi vấn automation peer, không phải lỗi app với người dùng thật; ghi để các lượt UIA sau dùng click vật lý

### P-20260830-3 · [ĐÓNG (a777d23..5ab2e9c, 30/08 — 10 commit estate)] · VỪA — Cụm đồng bộ hồ sơ KM/khung (client + server + luật gộp `KmHoSoGop`) đang chạy PRODUCTION mà chưa commit dòng nào
- **Thấy lúc**: chẩn đoán bug P-20260830-1
- **Ở đâu**: `server/Shopee.Hub.Web/Data/HubDatabase.KmProfiles.cs` + `HubDatabase.KmKhung.cs` + 3 file test hub (`??`); `suite/Shopee.Core/Coordination/HubDtos.cs`/`HubClient.cs`/`HubRoutes.cs`/`KmHoSoGop.cs` + wiring server (`M`); `OrdersModuleHost.KmSync.cs`
- **Bằng chứng**: `git status` — code đã deploy lên VPS hôm nay được publish từ working tree, HEAD không dựng lại được bản hub đang chạy
- **Vì sao không sửa ngay**: commit cần user xác nhận không phải việc dở phiên khác (cùng họ P-20260829-1/-8 — đề xuất commit sớm cả cụm)

### P-20260830-4 · [ĐÓNG (839760f/5ab2e9c)] · VỪA — FlashHoanChayLaiTests là BOM HẸN GIỜ: ghim mốc lịch sử 29/08 mà so với đồng hồ thật
- **Thấy lúc**: 30/08, chạy lại bộ orders trước loạt commit — 5 test đỏ dù không ai đổi code liên quan
- **Ở đâu**: `orders/XuLyDonShopee.Tests/FlashHoanChayLaiTests.cs` + `AccountsViewModel.Phien.cs/TraHoSo`
- **Bằng chứng**: test ghim `29/08 21:04` → mốc hoãn 22:00 CỦA 29/08; `TraHoSo` so với `DateTime.Now` trần ⇒ sau 22:00 đêm 29/08 nhánh hoãn không bao giờ vào, `DaCho` rỗng, cả lớp đỏ vĩnh viễn. Chính xmldoc test 3 đã tự thú "tính theo ĐỒNG HỒ THẬT"
- **Đã sửa**: tiêm hook `DongHoHoanChayLai` (production = DateTime.Now), test đông lạnh 21:10; phép đo khoảng chờ thành số chính xác 50 phút; 2264/2264 xanh lại. Bài học: test dùng mốc tuyệt đối phải tiêm đồng hồ, không được dựa "hôm nay"

### P-20260830-5 · [ĐANG SỬA] · CHẶN — Flash K1.7 câm: modal sản phẩm Xác nhận ĐÓNG-mà-KHÔNG-LƯU, form đếm 0/10
- **Thấy lúc**: đêm 30/08, truy vết vì sao lượt auto 21h tạo 0 chương trình (K1.1–K1.6 "ok", K1.7 câm 60s)
- **Ở đâu**: `extensions/shopee-orders/flow-flashsale.js` — bước F (modal sản phẩm) → bước G (TẠO THẬT)
- **Bằng chứng** (đo thật 22:40–22:53, shop Alina Store1): khung giờ chốt thật (`time-slot-btn has-value`); footer modal sau tick tự vẽ "Đã chọn 10/10 sản phẩm"; cú Xác nhận trúng ĐÚNG nút (`button.eds-button--primary`, text "xác nhận" sạch); modal đóng; form vẫn "Bạn đã chọn 0/10 sản phẩm" ⇒ nút TẠO THẬT khoá mềm "Vui lòng thêm sản phẩm trước". HAI giả thuyết ĐÃ BÁC: (1) tick không ăn state — bác (footer 10/10; riêng `dem.soDaTick` đọc DOM `checked` ra 0 = phép đọc nói dối); (2) isTrusted — bác (trusted click qua chrome.debugger x=1037,y=680 cũng ra 0/10). 28/08 CÙNG luồng từng tạo thành công ⇒ Shopee đổi hành vi modal trong khoảng 29–30/08.
- **Đã làm** (commit 439c393): cửa fail-closed ở bước F (form 0/x ⇒ ĐỎ CÓ TÊN thay vì K1.7 câm) + dump hiện trường (toast, modal còn hiện, form đầy đủ) ngay trong nhánh đỏ — lượt auto 21h 31/08 sẽ TỰ chụp bằng chứng; Xác nhận modal chuyển sang trustedClick (giữ vì gần cú bấm thật hơn với anti-bot).
- **Vì sao dừng**: ~22:50 Shopee đóng cửa đăng ký ngày 31/08 (xem P-20260830-6) — hết đường thử trên DOM thật đêm nay. Nghi phạm còn lại: modal cần thao tác phụ sau Xác nhận, hoặc lựa chọn phải gắn khung giờ theo cách mới. Hiện trường lượt 21h mai sẽ phân xử.

### P-20260830-6 · [MỞ] · NẶNG — Shopee đóng cửa đăng ký flash ngày D+1 lúc ~22:50: hạn chót nội bộ 23:45 là ảo
- **Thấy lúc**: đêm 30/08 — lượt 22:50 còn thấy 31/08 là ngày đỏ, lượt 22:52 + 22:53 đọc "KHÔNG phải ngày đỏ" (2 lượt liên tiếp, không phải flake render)
- **Ở đâu**: `orders/XuLyDonShopee.Core/Services/KmLichTuDong.cs` — hạn chót lượt flash tự động 23:45
- **Bằng chứng**: log hoatdong-20260830.log 22:52:24 và 22:53:18 "ngày đích 31/8/2026 KHÔNG phải ngày đỏ" ngay sau chuỗi 4 lượt 22:42–22:50 vẫn vào được tới K1.6 với cùng ngày
- **Vì sao không sửa ngay**: ngoài phạm vi việc đang làm (truy vết K1.7); đề xuất: dồn lượt flash tự động chạy sớm trong khung 21h–22:30 và hạ hạn chót xuống ~22:30, cần user quyết

### P-20260831-1 · [MỞ] · CHẶN — Form flash MỚI: cú "Bật" hàng loạt CHÍNH LÀ cú TẠO chương trình — flow đã tạo thật 1 chương trình trong lúc chẩn đoán
- **Thấy lúc**: 12:45 31/08, lượt chẩn đoán bước F2 (áp 5% + Bật) trên shop Alina Store1
- **Ở đâu**: `extensions/shopee-orders/flow-flashsale.js` bước F2 (chưa commit) + hành vi Shopee đổi 29–30/08
- **Bằng chứng**: hiện trường ~8s sau cú Bật: dialog "Select image (re-select default product image) Hủy/Xác nhận" + dialog "**Tạo thành công** — Chương trình Flash Sale của Shop sẽ diễn ra từ 22:00 01-09-2026 và kết thúc lúc 00:00 02-09-2026". Trước đó cùng lượt: 5% áp 104/104 ô %GIẢM, "đã chọn 103 phân loại hàng", cú Bật bấm trúng nút duy nhất. Flow chưa hề tới K1.7 (thanh đáy) — tức "Bật" đã tạo.
- **Hệ quả cần kiểm**: (1) shop Alina Store1 có thể đang mang ≥1 chương trình flash 01/09 tạo bởi các lượt chẩn đoán; (2) khung 22:00 (không phải khung 12:00–13:00 các lượt trước điền) — nghi các lượt điền-rồi-bỏ có "chiếm"/để lại vết; (3) toàn bộ phép đọc-lại của F2/K1.7 phải viết lại theo hành vi mới ("Bật"=TẠO, có dialog Select image + dialog Tạo thành công).
- **Đã làm ngay**: DỪNG mọi lượt thử (mỗi lượt mới từ giờ là một cú tạo thật tiềm năng). Code F2 để nguyên CHƯA commit chờ chốt hành vi.
- **Cần user**: mở Seller Centre shop Alina Store1 → Flash Sale Của Shop → kiểm danh sách chương trình ngày 01/09 (bao nhiêu cái, khung nào, sp/giá bên trong) + xoá những cái không muốn giữ.

### P-20260831-2 · [ĐANG SỬA] · CHẶN — Form flash mới: đã thích nghi gần trọn (commit b7b6abb), còn MỘT bí ẩn cuối: chương trình dựng sau ~16:30 không xuất hiện trên danh sách
- **Thấy lúc**: cả ngày 31/08, ~26 lượt chẩn đoán trên Alina Store1 (tiếp P-20260830-5, P-20260831-1)
- **Đã chứng minh & vào code (b7b6abb)**: mọi nút HÀNH ĐỘNG của trang flash mới nuốt click DOM thường ⇒ chuyển hết sang toạ độ + trustedClick (chrome.debugger); "hiện" = rect có kích thước (trang có shell pre-render ẩn đánh lừa getClientRects); bước F2 áp 5% + tick + Bật + xử dialog Select image/Tạo thành công; cửa "sp đã vào form" đo bằng ô %GIẢM; form create sau tạo là XÁC SỐNG (state khung rỗng vĩnh viễn — tooltip toggle "Vui lòng lựa chọn khung giờ" bất chấp chuột thật); danh sách hiện hàng mới trễ 5–8 phút (server-side); mô hình TÁI CHẾ K0/K*.7 (Chỉnh sửa hàng 0-bật đã nở → áp giá → Bật → Lưu trên trang edit); vòng DỌN tắt (hàng 0-bật là nguyên liệu).
- **Bí ẩn còn lại**: các "chương trình" flow dựng 16:39–17:05 (10 khung, đủ K1.1–K1.6b xanh) KHÔNG xuất hiện trên danh sách sau 10–35 phút (đo soHangNgayDich=0 lúc 17:14) — trong khi chương trình các lượt sáng TỪNG hiện (user chụp ảnh 5+ hàng lúc ~13h, các lượt dọn từng thấy/xoá hàng nở sau 4–8 phút, muộn nhất là hàng của lượt 16:09 nở lúc 16:17). Nghi: draft bị Shopee thu hồi, hoặc điều kiện tạo-thật thay đổi trong ngày. Khung vẫn bị GIỮ CHỖ (ngày đỏ tắt khi full) → cái gì đó tồn tại phía server mà list không cho xem.
- **Cần user**: chụp lại danh sách Flash Sale 01/09 của Alina Store1 (Seller Centre) — phân xử: (a) danh sách RỖNG → mọi thứ dựng chiều nay là ảo; (b) có hàng "Bật Flash Sale 10" → đã xong từ lâu, phép đọc phía extension lệch; (c) có hàng "Bật Flash Sale 0" → tái chế chạy được ngay.
- **Ràng buộc giờ**: cửa đăng ký 01/09 đóng ~22:50 (P-20260830-6). Check đơn đã bật lại 17:16.

### P-20260831-3 · [MỞ] · VỪA — Cờ ngayMaiKhongDo dừng CẢ 12 shop, nhưng "ngày đỏ" tắt cả khi CHỈ shop hiện tại hết khung trống
- **Thấy lúc**: 16:31–16:33 31/08 — K1.2 đỏ, K1 dựng nốt khung cuối, K2.2 hết đỏ (2 phút sau, cùng shop)
- **Ở đâu**: `extensions/shopee-orders/flow-flashsale.js` (nhánh ngayMaiKhongDo) + C# đọc cờ để dừng cả lượt
- **Bằng chứng**: log 16:31:44 K1.2 ok (đỏ) → 16:33:22 K2.2 "KHÔNG phải ngày đỏ"; comment cũ "lịch là của Shopee, không phải thuộc tính riêng của shop" sai với thực tế mới
- **Vì sao không sửa ngay**: trần thử đang 1 shop nên chưa cắn; khi mở 12 shop, shop 1 full khung sẽ làm 11 shop còn lại bị bỏ oan — cần tách "shop này hết khung" (đi shop kế) khỏi "Shopee chưa mở ngày" (dừng cả lượt)

### P-20260831-2 · CẬP NHẬT 19:1x · [ĐANG SỬA → GẦN ĐÓNG] — THÔNG LUỒNG: 19:03 lần đầu K1.1→K1.7 xanh trọn ("đã TẠO + KÍCH HOẠT 1/8 khung"), commit f08dd86
- **Quy trình chuẩn (user cung cấp DOM + mô tả tay)**: chọn sp → 5% + SL 10 → Cập nhật hàng loạt → Xác nhận thanh đáy → modal "Chú ý" → "Kích hoạt hàng loạt". Cụm toggle/Bật/dialog-ảnh cả chiều là đi nhầm cửa — đã xoá.
- **3 thủ phạm cuối của chuỗi "câm"**: (1) page-func gọi helper ngoài hàm → ReferenceError khi serialize; (2) nút thanh đáy y=6285 / viewport 1241 — chuột thật bấm NGOÀI màn hình, CDP nuốt lặng lẽ (fix scrollIntoView + chặn toạ độ ngoài viewport); (3) widget chat CSKH Shopee đè đúng tâm nút (fix ẩn chat + hit-test elementFromPoint).
- **Còn xác minh**: hàng đã kích hoạt lên danh sách với "Bật Flash Sale 10"? (list trễ 5–8 phút + chỉ tươi khi vào lại shop — lượt sau/K0 đọc, hoặc user xem tay). Vòng chờ dialog 2' mỗi khung ở K1.7 hơi phí — cân giảm khi chốt.

### P-20260831-2 · CHỐT 19:36 · [ĐÓNG (f08dd86, f9e118d)] — Flow flash form mới THÔNG TOÀN TUYẾN; ngày 01/09 của Alina Store1 đã kín khung
- **Kết quả cuối ngày**: 5 chương trình ngày 01/09 đã TẠO + KÍCH HOẠT qua đường chuẩn (lượt 19:03 ×1, lượt 19:10–19:23 ×4, mỗi khung 3,5'); các khung còn lại bị draft cũ chiếm → 19:36 Shopee báo hết khung ("không phải ngày đỏ") — flow dừng đúng. Trần FlashChay 900→1800s (commit f9e118d) sau khi lượt 8-khung chết oan ở khung 5 đúng như xmldoc hằng này cảnh báo.
- **Xác minh còn treo**: hàng "Bật Flash Sale 10" trên danh sách (list trễ 5–8'+, chỉ tươi khi vào lại shop) — mắt user hoặc lượt sau xác nhận.
- **Lỗ đã biết, chấp nhận**: K0 tái chế chỉ nhìn NGÀY ĐÍCH của lượt — lượt auto 21h (ngày 02/09) sẽ KHÔNG tái chế hàng 0-bật còn sót của 01/09 (vô hại: chương trình rỗng không giảm giá, chỉ chiếm khung đã qua đăng ký).
- **Tối nay 21h**: lượt auto chạy cho 02/09, trần thử vẫn 1 shop (TranShopLuotFlash=1) — đêm xanh thì mai nâng trần + cân bật Combo.

### P-20260831-4 · [MỞ] · NẶNG — App chết stack-overflow (0xc00000fd) khi WpfDialogService.Notify mở dialog đúng lúc phiên hiển thị đổi (remote làm đổi độ phân giải)
- **Thấy lúc**: 20:50:25 31/08 — user thấy "app trắng tinh"; APPCRASH coreclr c00000fd; app mở từ 19:33 chạy bình thường tới đó
- **Ở đâu**: `suite/Shopee.Suite/Services/WpfDialogService.cs:17,26` (Notify → ShowDialog) + `UiThread.cs:62`
- **Bằng chứng**: crash.log 20:50:24 lặp nhiều khối `Win32Exception (0x80004005): The operation completed successfully` tại `HwndWrapper..ctor ← Window.ShowDialog ← WpfDialogService.Show ← Notify`; Windows Event 20:50:25 APPCRASH c00000fd (stack overflow). Ngữ cảnh: độ phân giải màn đổi 3840×2160 → 2560×1440 (phiên remote của user) — Windows từ chối cấp HWND cho dialog mới, và đường lỗi có vẻ gọi lại chính Notify ⇒ tràn stack.
- **Vì sao không sửa ngay**: 20:53 phải dựng app lên gấp cho lượt flash auto 21h (đã dựng xong, check đơn chạy lại). Đề xuất sửa sớm: (1) try-catch trong `WpfDialogService.Show` — HWND fail thì ghi log rồi thôi, tuyệt đối không Notify tiếp từ nhánh lỗi; (2) tìm chỗ nào Notify được gọi lúc 20:50 (không có vết trong log hoạt động — Notify nền, nghi nhắc lịch/캡tcha/hub).

### P-20260831-5 · [MỞ] · VỪA — Phép đếm hàng trên trang danh sách flash (neo text "Xóa"/"Chỉnh sửa") trả 0 dù mắt người thấy hàng
- **Thấy lúc**: lượt auto 23:20 31/08 — K0 báo "Chưa có hàng nào của ngày đích nở" (soHangNgayDich=0) trong khi 5 chương trình 01/09 đã kích hoạt từ 19:03–19:23 (4 tiếng trước) và user từng thấy/thao tác hàng trên list bằng tay
- **Ở đâu**: `extensions/shopee-orders/flow-flashsale.js` — `pageDemChuongTrinhRong` + `pageToaDoNutChinhSuaHangRong` (neo từ link text exact "xóa"/"chỉnh sửa", leo tổ tiên ≤600 chars)
- **Bằng chứng**: chuỗi đo cả chiều (v19→v24, K0 các lượt) đều 0 hàng — kể cả sau reload cứng + browser mới; mâu thuẫn với quan sát tay của user. Nghi: hàng render với markup khác neo (nút trong dropdown? text khác? bảng ảo?), hoặc list mặc định lọc/tab khác trong browser sạch.
- **Vì sao không sửa ngay**: 23:21 cửa 01/09 đã đóng, mốc thử đêm nay đã dùng. Mai BAN NGÀY chạy 1 lượt tay (giờ vô hại — lượt tay ngoài khung không đốt lượt auto, commit 6582e64) + dump markup vài hàng đầu của list để chốt neo đúng.

### P-20260901-1 · [MỞ] · NẶNG — Quy mô 12 shop × 8 khung KHÔNG vừa cửa sổ 21–24h (cần ~7–8 tiếng), và trần FlashChay 1800s sẽ không đủ cho shop chạy đủ 8 khung
- **Thấy lúc**: trả lời câu hỏi user "8 khung/ngày × 12 shop thì 2h có đủ không" (01/09)
- **Ở đâu**: `orders/XuLyDonShopee.Core/Services/KmLichTuDong.cs` (GioMoKhungFlashSale=21, PhutHanChotBatDauShopMoiFlash=23:45, ThoiLuongMoiShopUocTinh=8' — ước tính của form CŨ ~50s/khung) + `OrdersBridgeChannel.ChoChang.FlashChay=1800s`
- **Bằng chứng**: đo thật 31/08 với form MỚI: 5 khung mất ~20–23' (19:03→19:23) ≈ 4–4,5'/khung ⇒ 8 khung ≈ 32–36' — VƯỢT trần FlashChay 30'/shop (sẽ chết như v34 chết ở khung 5 với trần 900s); cộng mở shop + nghỉ 3–5' ⇒ ~40'/shop ⇒ 12 shop ≈ 8 tiếng. Cửa thật còn hẹp hơn 21→23:45: đêm 31/08 Shopee đóng đăng ký D+1 từ ~22:50 (lượt 23:20 đã "không phải ngày đỏ") ⇒ cửa hữu dụng chỉ ~1h50 ≈ 3 shop.
- **Vì sao không sửa ngay**: câu hỏi khảo sát, chưa có lệnh sửa; phương án (dời giờ chạy sớm hơn — bằng chứng lượt tay 19:03 vẫn tạo được cho D+1; giảm số khung/shop; nới FlashChay theo số khung) cần user quyết trước khi nâng TranShopLuotFlash.

### P-20260901-1 · CHỐT 15:0x · [ĐÓNG (đợt flash-cả-ngày 01/09)] — xử bằng thiết kế mới: bỏ cửa 21–24h, flash chạy CẢ NGÀY thành gói nhỏ trong khe nghỉ check đơn
- Plan `plans/2026-09-01-flash-ca-ngay-xen-khe-nghi.md`: khung [0,23) · hạn shop mới 22:15 · gói đàn hồi theo khe (hạn gói = min(cuối khe − 45', 22:15)) · nghỉ check đơn mặc định 120' · FlashChay 1800→2700s · tiến độ ngày in-memory (KHÔNG DB) · mốc `FlashLanChayCuoi` chỉ ghi khi ngày trọn mọi shop.

### P-20260831-3 · [ĐÓNG (đợt flash-cả-ngày 01/09)] — cờ "ngày không đỏ" hết dừng oan cả lượt
- Early-return giữa danh sách đã gỡ: shop không-đỏ xử như BoQua thường, đi shop kế; cờ `KetQuaKm.NgayMaiKhongDo` chỉ còn để báo cáo (đã thử thêm cơ chế "đóng cửa ngày" rồi GỠ theo phản biện — 1 shop kín + 1 lỗi vePicker là đóng oan cả ngày). Shop không-đỏ-chưa-tạo KHÔNG bị tính là xong — gói sau thử lại.

### P-20260830-6 · [ĐÓNG (đợt flash-cả-ngày 01/09)] — hạ hạn chót: bị nuốt vào thiết kế mới, hạn bắt đầu shop mới nay 22:15

### P-20260901-2 · [MỞ] · NẶNG — Bộ test orders có thể TREO VÔ HẠN thay vì đỏ ở các bài đụng nhịp flash / cầu nối
- **Thấy lúc**: thi công + thử phá đợt flash-cả-ngày (opus-dev lượt 1 và 2, nghiệm thu đều dính)
- **Ở đâu**: `orders/XuLyDonShopee.Tests/FlashGoiKheNghiTests.cs` (bài nhịp mở gói thật, phóng Brave cho tài khoản không hồ sơ → không bao giờ quay ra); `orders/XuLyDonShopee.Tests/BridgeTestRig.cs` (`NhanTenLenhAsync` chờ vô hạn khi vòng gửi lệnh ngoài kịch bản); `FlashSaleLuongThatTests.cs` `BoQuaShop_GatSachDanhSach_ThiVeEm` (hồi quy lọc → chờ trọn `ChoChang.FlashChay` 2700s)
- **Bằng chứng**: một lượt `dotnet test` đứng ở 2292/2303, Brave sống 20+ phút; thử phá M19/M20 treo >100s, một lần để SÓT đột biến trên đĩa phải khôi phục tay
- **Vì sao không sửa ngay**: ngoài phạm vi đợt; đề nghị sửa sớm — rig cần hạn chờ + fail rõ, các bài nhịp cần chặn ở `AcquireAccountLease`

### P-20260901-3 · [MỞ] · VỪA — Bài test nhịp flash phóng Brave THẬT (phụ thuộc môi trường, chậm)
- **Ở đâu**: `orders/XuLyDonShopee.Tests/FlashGoiKheNghiTests.cs` (~5 bài đi tới `MuonHoSoAsync`/khoá xuyên máy, ~17s/bài, cả lớp 1m47s)
- **Đề nghị**: chặn tất cả ở cửa khoá như bài `TranGoiDemRieng...` đã làm

### P-20260901-4 · [MỞ] · VỪA — Fleet nhiều máy chưa khớp thiết kế flash mới
- (a) Mỗi gói vẫn `xinKhungXuyenMay` → máy KHÁC bị hub cắt vòng check đơn tới ~6–10 lần/ngày (thay vì 1 lần/đêm) — đúng mẫu mở-lại-shop-sớm từng gây verify; (b) máy chạy BUILD CŨ đọc `FlashLanChayCuoi` nghĩa mới (ghi giữa ngày) sẽ qua nhánh `!TrongKhung(cuoi)` của 6582e64 → 21h tự nổ lượt 12-shop kiểu cũ
- **Đề nghị**: deploy bản mới cho MỌI máy trước khi bật rộng; kiểm `OwnerMachine` không rỗng; về dài hạn cân nhắc hub-level khe

### P-20260901-5 · [MỞ] · VỪA — ≥2 tài khoản: check đơn lẫn flash đều chưa chạy đúng
- Check đơn: `AccountSessionManager.AnyOccupyingSlot` giữ tài khoản 2 ở `Queued` vĩnh viễn (vòng không tự dừng) — tick nhiều tài khoản thì chỉ tài khoản đầu được check (`orders/XuLyDonShopee.App/Services/AccountSessionManager.cs:60-98`); flash: cửa "mọi phiên đang nghỉ" đo phần GIAO các kỳ nghỉ — ≥3 tài khoản tuần tự thì giao có thể không bao giờ ≥75' → flash im cả ngày, chỉ còn dòng log tiết chế 60' báo ra (`AccountsViewModel.Phien.cs:787-792`)
- **Vì sao để đó**: cấu hình thật hiện 1 tài khoản/máy

### P-20260901-6 · [MỞ] · NHỎ — Gom vụn đợt 01/09
- `CauTongKetKm` là chuỗi else-if theo lý-do-dừng — cờ mới chèn trước `Loi` sẽ nuốt lỗi thật (đợt này dính 1 lần, đã đảo xuống dưới) · `AccountsViewModel.Phien.cs:~1100`
- `_flashTienDo` ghi từ thread-pool, đọc từ UI, không khoá — an toàn nhờ bất biến ngầm `_dangChayFlashTuDong` tuần tự hoá, chưa ai viết ra thành luật
- `luotChayMoi: true` mỗi gói reset dấu tick check đơn (nay 6–10×/ngày thay vì 1×/đêm)
- `CauNoiRotGiuaChangTests.RutNganHan_KhongBaoGioNOI` đo 200ms — flaky khi máy tải (đỏ 1/4 lượt full 01/09)

### P-20260902-1 · [MỞ] · VỪA — 3/10 gói ngày 02/09 chết ở "Không về được trang chọn shop" SAU shop cuối, shop cuối bị ghi HỎNG oan
- **Thấy lúc**: đọc log trả lời user trưa 02/09
- **Ở đâu**: `orders/XuLyDonShopee.Core/Services/OrdersBridgeSession.cs` (vePicker sau shop cuối của lượt) — gói 3 (03:25, sau Rudi), gói 6 (06:34, sau Sully), gói 9 (09:38, sau Bizly)
- **Bằng chứng**: log 02/09 03:25:42 / 06:34:40 / 09:38:02 "Dừng giữa chừng: Không về được trang chọn shop sau shop X — dừng lượt. Shop hỏng: X" — cả 3 lần đều là shop CUỐI của lượt (mọi việc đã xong), nhưng shop bị ghi Loi ⇒ không vào tập xong ⇒ gói sau mở lại vô ích
- **Vì sao không sửa ngay**: đang lượt trả lời; vô hại về kết quả (chỉ tốn mở lại), nhưng 30% gói dính ⇒ nên sửa sớm: shop cuối đã chốt kết quả thì vePicker hỏng không nên ghi Loi cho shop

### P-20260902-2 · [MỞ] · VỪA — NGHI NGỜ: shop "xong" bị lọc tới hết ngày, trong khi Shopee có thể MỞ THÊM khung trong ngày
- **Bằng chứng gián tiếp**: 02/09 gói 1 (01:2x) mỗi shop tốt chỉ nhặt được 1/8 khung rồi ngày tắt đỏ → bị đánh dấu xong, 9 gói sau bỏ qua. Nhưng hôm 31/08 cùng shop lúc 19:03 nhặt 1 khung, 19:10 lại nhặt thêm được 4 — tức số khung trống có thể TĂNG trong ngày (Shopee mở dần / nháp rác được xoá). Thiết kế hiện tại không bao giờ quay lại shop đã xong trong ngày.
- **Vì sao không sửa ngay**: cần user xác nhận bằng mắt trước (list các shop cho 03/09); nếu đúng thì cân nhắc: shop DaTao nhưng <8 khung chỉ "xong tạm" — gói sau N tiếng quay lại một lần

### P-20260903-1 · [MỞ] · NẶNG — Vòng check đơn CHẠY LẠI giữa lúc gói flash đang chạy → giành cổng 47821 + đóng Brave của flash ⇒ 3/10 gói chết mỗi ngày
- **Thấy lúc**: đọc log trả lời câu hỏi user "flow flash sale đã chạy chưa" (03/09) — đây là NGUYÊN NHÂN THẬT của hiện tượng đã ghi ở P-20260902-1 (giả thuyết cũ "vePicker sau shop cuối" KHÔNG đủ: hôm nay 2/3 ca chết ở shop 1/9 và 5/9, không phải shop cuối)
- **Ở đâu**: `orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs:1612` (`ChayLaiCheckDonSauKhiChoAsync` → `ChayLaiPhienCheckDon`: hai cửa kiểm là token + `_dsTkDangHoanChayLai`, KHÔNG có cửa nào hỏi "gói flash có đang chạy không") + cổng cố định 47821 ở `OrdersBridgeChannel.cs:490` / `shared/Shopee.Toolkit/Ws/WebSocketServer.cs:224`
- **Bằng chứng** (log `hoatdong-20260903.log`, 3 lần, cùng một chuỗi):
  - 03:05:47 "Hết biên an sau lượt Flash Sale — chạy lại check đơn" (gói 3 đang ở shop 5/9) → 03:05:49 `HttpListenerException (183) ... conflicts with an existing registration` → 03:05:49 cầu nối flash ĐỨT (`I/O operation has been aborted`) → 03:05:50 "Dọn hồ sơ trình duyệt: đã đóng 10 cửa sổ" (đóng luôn Brave của flash) → 03:08:09 Onicom store HỎNG → 03:10:24 "Không về được trang chọn shop — dừng lượt"
  - 06:10:41 (gói 7, shop 1/9 Piko) → cùng chuỗi → 06:14:28 dừng lượt
  - 09:17:23 (gói 10, giữa shop 8→9) → cùng chuỗi → 09:21:27 dừng lượt, Sully Store HỎNG
  - Đếm cả ngày: 12 dòng `conflicts with an existing registration` (02/09 cũng đúng 12) · 3/10 gói chết (02/09 cũng 3/10) — tỉ lệ 30% ổn định hai ngày liền
- **Vì sao không sửa ngay**: user chỉ hỏi tình trạng, chưa giao sửa. Hướng đề nghị: (a) cửa kiểm `_dangChayFlashTuDong` ngay trước `ChayLaiPhienCheckDon` — đang có gói flash thì HOÃN tiếp thay vì mở phiên; (b) đường "dọn hồ sơ" không được đóng cửa sổ của phiên khác đang sống; (c) dài hạn: cổng cầu nối cấp phát động thay vì hằng 47821

### P-20260903-2 · [MỞ] · VỪA — NGHI NGỜ: mọi chương trình flash tạo được đều báo "0 sản phẩm", chưa ai xác minh bên trong có 10 sp hay rỗng
- **Thấy lúc**: cùng lượt đọc log 03/09
- **Ở đâu**: câu tổng kết của `flow-flashsale.js` ("đã TẠO + KÍCH HOẠT 1/8 khung (trang 1, **0 sản phẩm**)") + `[Flash n/12] <shop>: đã tạo chương trình cho **0 sản phẩm**`
- **Bằng chứng**: 31/08 (4 dòng), 01/09 (3), 02/09 (3), 03/09 (3) — KHÔNG có lần nào ra số khác 0, kể cả các lượt tay 19:03–19:23 ngày 31/08 mà user đã xác nhận bằng mắt là tạo thật. Cùng lượt đó K1.6b "Áp 5%GIẢM + SL 10 hàng loạt: ok" ⇒ số 0 nhiều khả năng là PHÉP ĐỌC hỏng (cùng họ với "phép đọc DOM checked nói dối" ở P-20260830-5), nhưng chưa loại được khả năng chương trình rỗng thật
- **Vì sao không sửa ngay**: cần user liếc Seller Centre một shop (Alina/Shop9x/Vtinho, ngày 04/09) xem chương trình có 10 sp không — phân xử "đọc hỏng" vs "tạo rỗng" trước khi đụng code

### P-20260903-3 · [MỞ] · NẶNG — HỒI QUY f9e118d: cắt vòng chờ sau cú TẠO THẬT 240→20 nhịp ⇒ mỗi shop chỉ còn 1/8 khung mỗi ngày
- **Thấy lúc**: user hỏi "sao chỉ tạo 1/8 khung?" (03/09)
- **Ở đâu**: `extensions/shopee-orders/flow-flashsale.js:4155` (bước G / `K*.7`, `for (let i = 0; i < 20; i++)`) — commit f9e118d (31/08 19:35)
- **Bằng chứng** (log thật, cùng một bản extension trừ đúng con số này — a2e89ac chỉ sửa comment + thêm `soTaiChe`):
  - TRƯỚC f9e118d, lượt 31/08 19:10–19:25 (Alina Store1): mỗi `K*.7` tốn đúng 2'08"–2'09" (chờ trọn 240 nhịp) và khung 2/3/4 đều TẠO ĐƯỢC — `K2.6 Trang 2`, `K3.6 Trang 3`, `K4.6 Trang 4` đều ok. Lượt chỉ chết ở khung 5 vì trần `ChoChang.FlashChay` hồi đó là 900s
  - SAU f9e118d: `K*.7` chỉ tốn ~16s, và **9/9 lượt shop** của 01/09 (15:19–15:33), 02/09 (01:2x) và 03/09 (01:07–01:21) đều dừng ở khung 2 với `K2.2 ... không thấy ô ngày 'N' đang ĐỎ (tổng 35 ô, 0 ô đỏ)`
  - 100% trước = chạy được, 100% sau = hỏng, không có yếu tố nào khác đổi giữa hai mốc
- **Đọc ra**: 2 phút đó KHÔNG phải chờ dialog (dialog chưa bao giờ nở — đúng như comment của f9e118d), nó là thời gian Shopee commit chương trình xong. Chờ 10s rồi mở lịch khung kế ⇒ lịch trả về không ô đỏ nào ⇒ flow tưởng "Shopee đóng ngày" ⇒ thoát shop + C# đánh dấu shop XONG hôm nay ⇒ 9 gói sau bỏ qua (nối thẳng vào P-20260902-2, và giải thích luôn P-20260902-2 mà không cần giả thuyết "Shopee mở thêm khung")
- **Vì sao không sửa ngay**: user mới hỏi chẩn đoán, chưa giao sửa. Lý do f9e118d cắt (8 khung × 3,5' vượt trần 900s) nay KHÔNG còn: trần `FlashChay` đã là 2700s ⇒ 8 × 3,5' ≈ 28' vẫn dư. Đề nghị: trả vòng chờ về ~240 nhịp (hoặc chờ-theo-điều-kiện: chờ tới khi lịch khung kế có ô đỏ, trần 2'), và sửa câu cảnh báo "KHÔNG nở sau 2 phút" ở dòng 4195 cho khớp số nhịp thật
- **Cách xác nhận rẻ**: 1 lượt tay với vòng chờ 240 nhịp — khung 2 lên được là chốt

### P-20260903-4 · [MỞ] · VỪA — `trangChoKhung` không VÒNG LẠI theo số trang thật: shop ít hơn 8 trang sẽ chết ở khung thứ (số trang + 1)
- **Thấy lúc**: user chốt luật 03/09 — "chỉ có 2–3 trang mà 8 khung thì khung 4 quay lại trang 1, khung 5 quay lại trang 2"
- **Ở đâu**: `extensions/shopee-orders/flow-flashsale.js:2736` (`trangChoKhung` trả thẳng `soKhungDaTao + 1`, không biết tổng số trang) + bước F `flow-flashsale.js:3966`
- **Bằng chứng**: shop 3 trang (≤30 sp) mà tới khung 4 ⇒ vòng lật ở bước F gọi `pageSangTrangSauFlash` quá số trang ⇒ `ok:false` ⇒ bF ĐỎ với câu "không lật được tới trang 4 (dừng ở trang 3)" ⇒ `ketThuc` ⇒ **mất luôn các khung còn lại của shop**, không chỉ khung đó. Chưa cắn vì P-20260903-3 đang chặn ở khung 2, nhưng sẽ cắn ngay khi P-20260903-3 được sửa
- **Vật liệu có sẵn**: `pageDemTickFlash` đã trả `nhanCacTrang` (nhãn mọi trang trong modal) ⇒ đọc tổng số trang rồi `trang = ((soKhungDaTao) mod soTrang) + 1`
- **Vì sao không sửa ngay**: chưa được giao; nên gộp làm một đợt với P-20260903-3 vì cùng một vòng khung

### P-20260903-3 · CẬP NHẬT (đọc lại code K*.2 + đo khoảng cách thời gian) — cái bị cắt KHÔNG phải "thời gian sau kích hoạt", mà là khoảng cách K*.3 → K*.7
- Khoảng cách **kích hoạt → mở lịch khung kế** bằng nhau ở cả hai bên (~60s: 31/08 19:13:27→19:14:31; 03/09 01:08:13→01:09:10) ⇒ KHÔNG phải "Shopee cần thời gian sau kích hoạt".
- Khoảng cách **xác nhận khung giờ (K*.3, Shopee bắt đầu tạo nháp ngầm) → Xác nhận + Kích hoạt hàng loạt (K*.7)**: 31/08 ≈ 2'15" (chờ trọn 240 nhịp) · 03/09 ≈ 30s (20 nhịp). Comment ngay tại bước G tự ghi: "Dialog Tạo-thành-công nở muộn 1–2 phút sau xác nhận khung" — tức kích hoạt sau 30s là kích hoạt lên một nháp Shopee chưa dựng xong; hệ quả quan sát được là lịch của shop mất sạch `div.timeslots` (0 ô đỏ trên cả 35 ô) ở khung kế.
- Yếu tố phụ (`flow-flashsale.js:3737`): khung 2..8 đặt `phaiDocLich=false` ⇒ KHÔNG gọi `choLichLang` (chờ lịch lắng) mà bấm `pageClickNgayFlash` ngay khi ô ngày vừa render — cửa hở để bấm lên lưới chưa có dữ liệu khung. Chưa phải thủ phạm chính (31/08 4 khung liền vẫn qua), nhưng nên chờ-lắng cho mọi khung khi sửa.
- Đường thoát thật ở K2.2 là nhánh `!bB.ok` (click ô ngày đỏ hỏng), KHÔNG phải cờ `ngayMaiKhongDo`; shop vẫn ra `daTao=true` ⇒ C# tính là XONG hôm nay.

### P-20260903-5 · [MỞ] · NẶNG — Khởi động lại app GIỮA NGÀY: trần 10 gói flash/ngày reset về 0 VÀ gói tự mở ngay dù chưa có phiên check đơn nào ⇒ check đơn không bao giờ tự chạy lại
- **Thấy lúc**: dựng lại app sau khi user reboot máy trưa 03/09 (user nhờ chạy lại flow check đơn)
- **Ở đâu**: `orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs:647` (`_soGoiDaMoTrongNgay` in-memory) + `:866` (nhánh `dangChay.Count == 0` → khe GIẢ = bây giờ + interval) + `:1455` `TraHoSo` (chỉ chạy lại check đơn cho tài khoản trong `_dsTkDaDung`)
- **Bằng chứng** (log `hoatdong-20260903.log`):
  - 01:06:47 → 08:37:52 đã mở đủ **GÓI 1/10 … GÓI 10/10** của ngày (trần `TranGoiThuMoiNgayFlash` đã dùng hết)
  - 13:22:10 dừng phiên (reboot) · 13:39:17 mở lại app · **13:40:18 "MỞ GÓI 1/10 của ngày (ngày đích đã xong 0 shop)"** — cả bộ đếm gói lẫn tiến độ ngày đều về 0 ⇒ ngày 03/09 được cấp thêm 10 gói nữa (tổng 20)
  - Gói mở khi CHƯA có phiên check đơn nào: không có khe thật nên lấy khe giả "tới 15:40" (bây giờ + 120'), rồi chiếm cầu nối — 13:41:55 cú bấm "Chạy đã chọn" bị từ chối bằng `CauTuChoiViLatCatDangChay`
  - `_dsTkDaDung` rỗng (không phiên nào bị tạm dừng cho gói) ⇒ `TraHoSo` KHÔNG đặt lượt chạy lại ⇒ gói xong là nhịp sau (giãn cách 30') mở gói kế → **chuỗi gói flash chạy hết ngày, check đơn im lặng**
- **Đã xử tay lượt này**: 13:44:29 bấm "Đóng cửa sổ" huỷ gói (đúng lúc nghỉ giữa shop 1→2), 13:44:45 bấm "Chạy đã chọn" — check đơn chạy lại bình thường
- **Vì sao không sửa ngay**: user chỉ nhờ dựng lại app. Hướng đề nghị: (a) nhịp flash KHÔNG mở gói khi `dangChay.Count == 0` (không có phiên check đơn thì không có khe để mượn — khe giả là khe của một vòng chưa ai chạy); hoặc (b) gói mở trong ca đó phải tự đặt lượt chạy lại check đơn thay vì dựa vào `_dsTkDaDung`; (c) cân nhắc cho bộ đếm gói/tiến độ ngày sống qua lần khởi động lại (mâu thuẫn với luật "KHÔNG xuống DB" ở `ChotLuotFlash` — cần user quyết)

### P-20260903-3 · CHỐT 14:5x · [ĐANG SỬA → CODE XONG, chờ commit + chạy thật] — đã trả vòng chờ K*.7 về 240 nhịp + bỏ trần khung
- Plan `plans/2026-09-03-flash-du-khung-bo-tran-8.md`. Sửa: `CHO_DIALOG_TAO_NHIP=240` (đảo hồi quy f9e118d) + cửa chờ ô ngày đỏ `CHO_O_NGAY_DO_NHIP` ở K*.2 (không thêm `choLichLang`). Kiểm chứng độc lập: build 0 warning, suite 440/440, orders 2303/2303, phản biện không thấy lỗi CHẶN/NẶNG. CHƯA commit, CHƯA chạy thật (N8).

### P-20260903-4 · CHỐT 14:5x · [ĐANG SỬA → CODE XONG, chờ commit + chạy thật] — `trangChoKhung` quay vòng theo số trang thật
- `trangChoKhung(soKhungDaTao, soTrang)` = `(n % soTrang) + 1`; hàm thuần `soTrangTuNhan(nhanCacTrang)` đọc pager modal; bỏ trần 8 khung (JS `TRAN_VONG_KHUNG_KY_THUAT=24` chống vòng vô tận, C# `ThamSoFlashSale` bỏ `SoKhungToiDa`). Test ma trận 3 trang × 8 khung ⇒ 1,2,3,1,2,3,1,2. Cùng đợt P-20260903-3.

### P-20260903-5 · [MỞ] · NHỎ — Cảnh báo "pager không đọc được" bắn nhầm cho shop đúng 10 sp/1 trang
- **Thấy lúc**: phản biện đợt flash-đủ-khung (03/09)
- **Ở đâu**: `extensions/shopee-orders/flow-flashsale.js` bước F (`demTruocLat.soDong >= SO_SP_MOI_TRANG` mà `nhanCacTrang` rỗng)
- **Bằng chứng**: shop có đúng 10 sp ⇒ EDS không render pager ⇒ `nhanCacTrang=[]`, `soDong=10` ⇒ đẩy cảnh báo dù `soTrang=1` hoàn toàn đúng. Chỉ nhiễu log, không sai số. Sửa `>` thì lại mất tín hiệu ca "nhiều trang mà pager hỏng" (trang 1 luôn hiện đúng 10 dòng dù shop nhiều trang) ⇒ `soDong` vốn không phân biệt được hai ca
- **Vì sao không sửa ngay**: đổi hành vi cảnh báo, không có cách sửa hiển nhiên; để user quyết có bỏ hẳn cảnh báo dựa-soDong hay không

### P-20260903-6 · [MỞ] · NHỎ (NGHI NGỜ) — `soTrangTuNhan` lấy MAX số nguyên có thể vỡ nếu Shopee đổi markup pager
- **Ở đâu**: `extensions/shopee-orders/flow-flashsale.js` `soTrangTuNhan`
- **Bằng chứng**: `soTrangTuNhan(["10","20","30"]) → 30`. Nếu `li.eds-pager__page` từng chứa nhãn số KHÔNG phải số trang (vd bộ chọn kích thước trang), `soTrang` sai ⇒ quay vòng lệch, tạo chương trình trên trang không mong muốn (âm thầm). CHƯA dựng được ca thật — EDS hiện chỉ nhét số-trang + "..." vào selector đó, page-size là component riêng
- **Vì sao không sửa ngay**: là NGHI NGỜ, chưa phải lỗi; điểm gãy im lặng nếu Shopee đổi markup — theo dõi

### P-20260903-3 · [ĐÓNG (a77b3d0, chạy thật 16:57 03/09)] — K2.2 xanh trên Shopee thật sau khi trả vòng chờ 240 nhịp
- Gói auto 16:53:09 (khe nghỉ sau vòng check đơn 1, bản a77b3d0 build vào bin thật): Alina K1.7 tốn 2'10" → K2.2 `ok` lúc 16:57:27 (3 ngày trước: "0 ô đỏ" 9/9 lượt). Bằng chứng đủ để đóng; số khung cuối/quay vòng trang ghi ở plan khi gói xong.

### P-20260903-4 · [ĐÓNG (a77b3d0)] — `trangChoKhung` quay vòng theo số trang thật, `soTrangTuNhan` đọc pager
- Chạy thật 16:56:29: "trang 1/4" — pager đọc được 4 trang thật của Alina. Quay vòng (khung 5 → trang 1/4) sẽ hiện trong gói 16:53 nếu Shopee còn ≥5 khung; không thì test Jint 8 khung × 3 trang ⇒ 1,2,3,1,2,3,1,2 là lưới.

### P-20260903-4 · BỔ SUNG 17:11 — quay vòng trang chạy thật: khung 5 của Alina lấy trang 1/4 (sau 1/4→2/4→3/4→4/4). ĐÓNG chắc.

### P-20260902-2 · [ĐÓNG (giải thích bởi P-20260903-3)] — không có chuyện "Shopee mở thêm khung trong ngày"
- 03/09: Alina qua 3 lượt ghé (01:08, 13:41, 14:39) mỗi lượt đúng 1 khung rồi K2.2 "0 ô đỏ"; lượt 16:53 bản a77b3d0 lấy tiếp 5 khung liền cho tới kín 8/8 (K6.2: lịch 12 ô đỏ, ngày 4 không đỏ = kín thật). Khung vốn còn nguyên, chỉ là lỗi K2 (hồi quy f9e118d) chặn mỗi lượt ở khung 1 nên trông như "mở dần". Lọc shop-đã-xong trong ngày vì thế đúng: shop xong = đã lấy hết khung trong một lượt.

### P-20260903-7 · [MỞ] · NHỎ — Câu tổng kết gói nói sai lý do dừng: "DỪNG vì hết khung tự động (23:00)" trong khi thật ra là tới hạn bắt đầu shop mới của GÓI (cuối khe − 45')
- **Thấy lúc**: 18:09:18 03/09, gói 16:53 dừng sau Piko với "còn 8 shop chưa chạy"
- **Ở đâu**: câu tổng kết flash (`CauTongKetKm`/nhánh hết-hạn) — in `GioDongKhungFlashSale` (23:00) thay vì hạn thật của gói (18:07 = khe 18:52 − 45')
- **Bằng chứng**: log 18:09:18 "DỪNG vì hết khung tự động (23:00) — còn 8 shop chưa chạy" lúc mới 18:09; dòng MỞ GÓI 16:53:09 đã ghi rõ "không bắt đầu shop mới sau 18:07"
- **Vì sao không sửa ngay**: chỉ là lời log, hành vi đúng; ngoài phạm vi đợt vừa đóng

### P-20260903-1 · CẬP NHẬT 22:4x · vẫn cắn trên bản mới a77b3d0 — 2 gói/6 gói tối nay chết, và tìm ra vì sao gói mở đúng vào lúc sắp bị đè
- **Hai lần tối nay** (log 03/09): 18:50:09 MỞ GÓI 3 → 18:52:59 "Hết biên an — chạy lại check đơn" → 18:53:01 `HttpListenerException 47821` → Brave của flash bị "Dọn hồ sơ… đóng 10 cửa sổ" → 18:57:34 Delica HỎNG → 18:59:49 dừng lượt. Lặp y hệt: 21:25:09 MỞ GÓI 6 → 22:01:44 chạy lại check đơn → conflict → 22:07:21 Sully HỎNG → 22:09:36 dừng lượt. Cả vòng check đơn 22:01 cũng hỏng theo ("Vòng cầu nối chưa trọn", phải chờ tới 22:17).
- **Cơ chế đầy đủ (mảnh còn thiếu so với ghi nhận 14:xx)**: sau khi một gói xong, app "HOÃN chạy lại check đơn tới <cuối khe>" ⇒ lúc đó KHÔNG có phiên nào ⇒ nhịp kế coi `dangChay.Count == 0` là khe giả định trọn 120' (`AccountsViewModel.Phien.cs:~866`) và MỞ GÓI MỚI, **bỏ qua mốc hoãn `MocChayLaiCheckDonSauFlash` chỉ còn 2'–36' nữa** (18:50:09 vs hoãn 18:52 · 21:25:09 vs hoãn 22:01). Gói mới tất yếu chạy đè qua mốc hoãn ⇒ `ChayLaiCheckDonSauKhiChoAsync` (`:1612`) nổ giữa gói ⇒ giành cổng + đóng Brave.
- **Đề nghị sửa (2 chỗ, cả hai đều cần)**: (1) khe giả định khi không có phiên = `min(bayGio + interval, MocChayLaiCheckDonSauFlash)` — còn < 75' tới mốc hoãn thì từ chối mở gói; (2) `ChayLaiCheckDonSauKhiChoAsync` kiểm `_dangChayFlashTuDong` ngay trước `ChayLaiPhienCheckDon` — đang có gói thì hoãn tiếp tới khi gói xong thay vì mở phiên. Hạng giữ NẶNG: nay mỗi shop chạy dài hơn (8 khung ≈ 27') nên xác suất bị đè tăng.

### P-20260903-3 · XÁC NHẬN BẰNG MẮT 22:5x 03/09 — user xem Seller Centre: Alina đủ 8 khung ngày 04/09, khung cuối 21:00 04/09 → 00:00 05/09. Khớp log (3 lượt cũ + 5 lượt 16:53) và khớp K6.2 "ngày 4 không còn đỏ" = kín thật.

### P-20260903-2 · XÁC MINH 22:5x 03/09 · [MỞ → hạ hạng NHỎ] — chương trình THẬT có đủ 10 sp, 5% cho mọi sp (user mở xem Alina). "0 sản phẩm" trong log là PHÉP ĐỌC nói dối, không phải tạo rỗng
- Còn lại chỉ là lỗi hiển thị: `soTickKhungNay = dem.soDaTick` đọc DOM `checked` ra 0 (cùng họ P-20260830-5) ⇒ câu "đã TẠO + KÍCH HOẠT n khung (trang k/N, 0 sản phẩm)" và "đã tạo chương trình cho 0 sản phẩm" sai số. Sửa khi tiện: đếm bằng `bangPhanLoai.soO`/số dòng đã tick theo footer Shopee vẽ, hoặc bỏ con số khỏi câu. Không ảnh hưởng nghiệp vụ.

### P-20260903-8 · [MỞ] · VỪA — Lệnh "✖ Dừng" từ Hub chỉ dừng PHIÊN CHECK ĐƠN, không dừng lượt KM/Combo/Flash đang chạy và không tắt nhịp tự động
- **Thấy lúc**: trả lời câu hỏi user "nút Dừng từ hub có dừng KM/combo/flash không" (23:0x 03/09)
- **Ở đâu**: `suite/Shopee.Suite/Infrastructure/OrdersModuleHost.Mirror.cs:237` (`OrdersCommandActions.Stop` → chỉ `services.Sessions.Stop(acc.Id)`); so với ribbon `StopAllAsync` (`AccountsViewModel.Phien.cs:~1750`: `_nguoiDungDungTay = true` + huỷ lượt hoãn + `TryKillPoc()` cancel `_bridgeCts`)
- **Bằng chứng**: đường Hub không gọi `TryKillPoc`/`_bridgeCts.Cancel`, không đặt `_nguoiDungDungTay`, không huỷ lượt hoãn ⇒ (1) gói flash đang chạy vẫn chạy tiếp; (2) hết phiên ⇒ nhịp coi là khe giả định ⇒ có thể MỞ GÓI flash mới trong ≤1' nếu `DenGioGoiFlash` cho (đúng mẫu 14:38:18 "Đang dừng phiên" → cùng giây "MỞ GÓI 2/10"); (3) khi gói xong, `TraHoSo` với `_nguoiDungDungTay=false` sẽ tự chạy lại check đơn — ngược ý "Dừng" của người bấm trên Hub. Cả ribbon "Dừng đã chọn/tất cả" cũng KHÔNG tắt nhịp tự động — chỉ ô "Flash Sale — chạy tự động" (`flash_bat`) mới tắt.
- **Vì sao không sửa ngay**: câu hỏi khảo sát; cần user quyết ngữ nghĩa "Dừng từ Hub" (chỉ check đơn như hiện tại, hay = "Dừng tất cả" như ribbon)

### P-20260903-8 · BỔ SUNG 23:2x · [MỞ → đã có plan `plans/2026-09-03-hub-dieu-khien-km-combo-flash-uu-tien-flash.md`, thi công sau đợt check-đơn-không-đè-flash]
- Thêm phát hiện: lệnh Hub `run` gọi thẳng `services.Sessions.Start(acc.Id)` (`OrdersModuleHost.Mirror.cs:~226`) ⇒ **đi vòng qua** cửa từ chối `CauTuChoiViLatCatDangChay` của app ⇒ từ Hub có thể mở check đơn ĐÈ lên gói flash đang chạy (đúng cổng 47821, cùng họ P-20260903-1). Lệnh `stop` còn được chính Hub tự phát để cắt check đơn máy khác khi xin khung xuyên máy (`HubDatabase.KmKhung.cs:76`) ⇒ KHÔNG đổi nghĩa `stop`, thêm `stop-all` + `run-km/run-combo/run-flash`, mọi lệnh đi qua MỘT hook VM có luật ưu tiên flash (user chốt 03/09: flash đang chạy thì không flow nào khác được chạy).

### P-20260904-1 · [MỞ] · NHỎ — Hai bài test orders flaky theo tải máy (đỏ ngẫu nhiên khi chạy trọn bộ, xanh khi chạy riêng)
- **Thấy lúc**: opus-dev thi công đợt check-đơn-không-đè-flash (đêm 03/09), 3 lượt chạy trọn 2331–2332 bài ⇒ 2 lần đỏ ở hai bài KHÁC nhau, cả hai xanh 8/8 khi chạy riêng
- **Ở đâu**: `orders/XuLyDonShopee.Tests/BridgeTestRig.cs:183` (`CongTrong` bind cổng 0 rồi NHẢ rồi mới dùng lại — TOCTOU giữa các rig song song) · `orders/XuLyDonShopee.Tests/CauNoiRotGiuaChangTests.cs:69` (`Assert.True(elapsed < 5s)` cho hạn 200ms — cùng họ mục cuối P-20260901-6)
- **Bằng chứng**: `ComboVongRigTests.CaB_ShopHong_GhiHONG_DiTiepShopKe_VaVanLaLenhChayCombo`, `CauNoiRotGiuaChangTests.RutNganHan_KhongBaoGioNOI_ChangVonNganGiuHanGoc`
- **Vì sao không sửa ngay**: ngoài phạm vi đợt; đề nghị: rig giữ socket đã bind và trao thẳng cho server thay vì nhả rồi bind lại

### P-20260904-2 · [MỞ] · NHỎ — Câu log nhánh nhường "DỪNG sau shop vừa xong" sai chữ khi cờ bật từ trước shop đầu (i = 0)
- **Ở đâu**: `orders/XuLyDonShopee.Core/Services/OrdersBridgeSession.cs:~1240`
- **Bằng chứng**: chỉ là lời log; thực tế không dựng nổi vì cờ được reset ngay trước mỗi gói. Sửa khi tiện cùng câu chữ khác.

### P-20260904-3 · [MỞ] · NHỎ — Cặp mặc định flash 5%/10 SP nay nằm ở 3 nơi độc lập (record `KmConfig`, `DEFAULT` SQL client+server, `const` trong DTO); chỉ 2 nơi có test đối chiếu
- **Thấy lúc**: nghiệm thu đợt tham-số-flash (đêm 03/09)
- **Ở đâu**: `suite/Shopee.Core/Coordination/HubDtos.cs:507` (const) · `orders/XuLyDonShopee.Core/Data/Database.cs:308-309` (DEFAULT trong chuỗi SQL — không test nào so với hằng) · `server/…/HubDatabase.KmProfiles.cs:72-73`
- **Bằng chứng**: grep ra 5 chỗ ghi số 5/10 rời nhau; test `Flash_HangMacDinh_KhopSoDangChayThat` chỉ khoá const DTO ↔ record
- **Vì sao không sửa ngay**: ngoài phạm vi; đề nghị: SQL DEFAULT sinh từ hằng (nội suy chuỗi) hoặc thêm test đọc `PRAGMA table_info` so với hằng

### P-20260904-4 · [MỞ] · NHỎ — 4 worktree cũ `.claude/worktrees/*` treo ở commit `91f00ec`, rác phiên trước
- **Bằng chứng**: `git worktree list` — `cool-liskov-1fc6ed`, `objective-keller-2530ca`, `sleepy-tu-3dda83`, `youthful-ishizaka-089dca`
- **Vì sao không dọn ngay**: có thể là của phiên khác đang mở; user quyết rồi `git worktree remove` + `prune`

### P-20260904-5 · [MỞ] · NHỎ — `dienVaLuuKichHoat` / `thanBuocTaiChe` đọc tham số `tp` (phanTramGiam, soLuongThamGia) qua CLOSURE, không có ràng buộc tĩnh
- **Thấy lúc**: nghiệm thu đợt tham-số-flash (đêm 03/09)
- **Ở đâu**: `extensions/shopee-orders/flow-flashsale.js:~3583` (helper khai trong thân `chayFlashSaleChoTabDangMo`, đọc `tp` của scope ngoài)
- **Bằng chứng**: ai move hai hàm này ra ngoài scope là `tp` thành `undefined` lúc chạy; test neo nguồn hiện chỉ đếm chỗ dùng `tp.*`, không bắt được ca move. Biện pháp duy nhất đang giữ là comment "ĐỌC `tp` QUA CLOSURE"
- **Vì sao không sửa ngay**: ngoài phạm vi; đề nghị khi tiện: truyền `tp` làm tham số tường minh cho hai hàm

### P-20260904-6 · [MỞ] · VỪA — `_dsTkDangHoanChayLai` (HashSet) bị Add/Remove/ToList/Clear từ thread lượt hoãn lẫn UI mà không khoá — cùng lớp đua với ca vừa vá cho mốc hoãn
- **Thấy lúc**: opus-dev lượt sửa 2 đợt check-đơn-không-đè-flash (đêm 03–04/09)
- **Ở đâu**: `orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs` (`TraHoSo` `.Add(id)` rồi phóng task fire-and-forget; task `.Remove(accountId)` trên continuation thread-pool; `HuyLuotHoanChayLaiCheckDon` `.ToList()` + `.Clear()`)
- **Bằng chứng**: duyệt `ToList()` trong lúc thread khác `Remove` ⇒ `InvalidOperationException` (HashSet không thread-safe); đợt này đã phải thêm `lock` cho `_mocHoanChayLaiDangCho` vì cùng kiểu truy cập
- **Vì sao không sửa ngay**: ngoài phạm vi; đề nghị: cùng một `lock` cho cả hai cấu trúc (hoặc gom về một dictionary duy nhất)

### P-20260904-7 · [MỞ] · NHỎ — Vòng nhường QUÁ TRẦN (90') xoá mốc nhưng cố ý không hạ `_nhuongChoCheckDon` ⇒ gói đang chạy vẫn dừng thêm một shop cho một lượt check đơn đã bị bỏ
- **Ở đâu**: `AccountsViewModel.Phien.cs` nhánh `return false` của `DaNhuongXongChoGoiFlashAsync`
- **Bằng chứng**: cờ chỉ tắt khi lượt/gói KẾ reset; hậu quả: mất một shop của gói đang chạy, câu log nói lý do không còn đúng. Hiếm (chỉ khi một lượt giữ cầu nối > 90')
- **Vì sao không sửa ngay**: chủ ý của opus-dev để không mở khe cho gói vừa nhường lấy shop mới; cần user quyết ngữ nghĩa

### P-20260904-8 · [MỞ] · VỪA — `KmVongSauTests.CauHinhHubSauKhoiDong_ThiNhipVanBackfillChuTri_VaChiMotLan` đỏ 1/10 lần khi chạy cả bộ lúc máy tải, xanh khi chạy riêng
- **Thấy lúc**: nghiệm thu lượt 2 đợt check-đơn-không-đè-flash (01:3x 04/09), chạy song song với bộ orders
- **Ở đâu**: `suite/Shopee.Suite.Tests/KmVongSauTests.cs:121` — bài dùng static `OrdersModuleHost.WireKmSync` + latch bền trong bảng `settings` (trạng thái dùng chung giữa các class chạy song song)
- **Bằng chứng**: lần chạy nguội đầu `Failed: 1, Passed: 69/70`; 9 lần sau `70/70`; HEAD gốc `68/68`; file KHÔNG nằm trong diff đợt. Chưa loại trừ dứt điểm việc 2 bài mới cùng assembly làm lệch thứ tự đủ để kích flake
- **Vì sao không sửa ngay**: ngoài phạm vi; đề nghị: cô lập latch/static theo test (hoặc `[Collection]` tuần tự cho các bài đụng `WireKmSync`)

### P-20260903-1 · CẬP NHẬT 02:0x 04/09 · [ĐANG SỬA → CODE XONG, đã deploy hub, app đang mở lại để đo thật] — 3 lượt sửa, bất biến cuối: mỗi lượt hoãn có danh tính, id + mốc sống tới lối ra thật, chỉ chủ lượt được dọn
- Plan `plans/2026-09-03-check-don-khong-de-flash-va-tham-so-flash-tu-app.md` mục "Diễn tiến". Kiểm chứng: build 0 warning, orders 2341/2341, suite 457, Suite.Tests 71, server 207; 5 bài test hành vi cho vòng đời lượt hoãn, đều đã thử phá.

### P-20260904-9 · [MỞ] · NHỎ — Reset cờ nhường đặt TRƯỚC cửa `_bridgeRunning` của `ChayLatCatAsync` ⇒ một cú bấm KM/Combo/Flash bị TỪ CHỐI (vì lát cắt khác đang chạy) vẫn hạ cờ của gói đang nhường
- **Thấy lúc**: phản biện lượt 2 (04/09), phiên chính chưa sửa ở lượt 3 (2 test neo nguồn 4 chỗ reset cần viết lại)
- **Ở đâu**: `AccountsViewModel.Phien.cs` — `ChayKmAsync`/`ChayComboAsync`/`ChayFlashSaleTayAsync`/nhịp flash đặt `_nhuongChoCheckDon = false` rồi mới vào `ChayLatCatAsync` (cửa `_bridgeRunning` bail)
- **Bằng chứng**: lượt hoãn đang nhường cho gói G; user bấm "KM của Shop" → cờ hạ → từ chối "Đang … rồi" → G không thấy cờ tới nhịp nhường kế (≤10s, F1 vòng 2 đặt lại). Thiệt hại tối đa: G lấy thêm MỘT shop mỗi cú bấm rơi đúng ranh giới
- **Vì sao không sửa ngay**: NHỎ, có lưới F1; sửa đúng là dời reset vào trong `ChayLatCatAsync` NGAY SAU cửa `_bridgeRunning` và viết lại 2 test neo nguồn

### P-20260904-10 · [MỞ] · NHỎ — `StopAllAsync` đặt `_nguoiDungDungTay = true` rồi bị chính `TraHoSo` (gọi từ `DongCuaSoXemVaBao` bên trong nó) hạ về false trước cả `await Sessions.StopAllAsync()`
- **Thấy lúc**: opus-dev đợt Hub-điều-khiển (đêm 04/09)
- **Ở đâu**: `AccountsViewModel.Phien.cs` `StopAllAsync` (~:2172) và `TraHoSo` (~:1676, dòng `_nguoiDungDungTay = false;`)
- **Bằng chứng**: cờ chỉ sống đúng một tick; hiện vô hại vì lượt `TraHoSo` thứ hai (từ `finally` của lát cắt bị cancel) thấy `_dsTkDaDung` đã rỗng — may chứ không phải thiết kế. Phản biện đợt này đang soi ca "check đơn tự chạy lại sau stop-all"
- **Vì sao không sửa ngay**: chờ kết luận phản biện; nếu có ca thật thì sửa trong đợt

### P-20260904-11 · [MỞ] · NHỎ — `NhipLichComboAsync` không có cửa `_dangChayFlashTuDong` (chỉ Combo/KM/`_bridgeRunning`); luật ưu tiên flash đang dựa vào THỨ TỰ gọi nhịp chứ không vào một cửa tường minh
- **Ở đâu**: `AccountsViewModel.Phien.cs:~1194`
- **Vì sao không sửa ngay**: với thứ tự Flash → KM → Combo và `_bridgeRunning` thì không giẫm nhau; ghi để khi ai đảo thứ tự nhịp không mất luật

### P-20260904-12 · [ĐÓNG (454f1bd)] · VỪA→NẶNG-B — Hub không biết máy đang chạy KM/Combo/Flash (gương chỉ có `SessionState` check đơn) ⇒ 3 nút ▶ KM/Combo/Flash luôn sáng, người vận hành chỉ biết bị từ chối sau ack (~12s)
- **Ở đâu**: `suite/Shopee.Suite/Infrastructure/OrdersModuleHost.Mirror.cs:~151` (`MirrorSessionState`), `OrdersMirrorAccount` không có trường lát cắt đang chạy
- **Vì sao không sửa ngay**: plan đợt này cố ý để "mở rộng mirror là việc khác"; đề nghị đợt sau: gương thêm `LatCatDangChay` (tên việc) để hub khoá nút + hiện trạng thái

### P-20260904-13 · [MỞ] · NHỎ — Không có hạ tầng test render Razor (bUnit) ⇒ nút/tooltip ở `DispatchOrdersTab.razor` và `Dispatch.razor.cs OnOrdersStop` chỉ kiểm được bằng compile + đọc
- **Ở đâu**: `server/Shopee.Hub.Web.Tests/Shopee.Hub.Web.Tests.csproj` (không có PackageReference bUnit)
- **Vì sao không sửa ngay**: thêm bUnit là quyết định hạ tầng, cần user

### P-20260904-14 · [MỞ] · NHỎ — Bài test `stop-all` (`LenhTuHubUuTienFlashTests.cs:174`) không khoá trực tiếp `_nguoiDungDungTay`/`TryKillPoc` như plan §3 bước 6 liệt kê, chỉ suy gián tiếp qua câu log
- **Vì sao không sửa ngay**: nghiệm thu đợt Hub-điều-khiển; N4 vẫn ĐẠT qua đường gián tiếp; đề nghị thêm assert cờ dừng tay + cầu nối bị giết khi tiện

### P-20260904-15 · [MỞ] · NHỎ — Bảng nhãn hành động hub có HAI bản chép tay: `AccountsViewModel.Phien.cs NhanLenhTuHub` và `server/…/DispatchViewLogic.cs:96`
- **Bằng chứng**: có test `NhanLenhTuHub_KhopNhanHienTrenHub` khớp nên chưa trôi, nhưng thêm hành động thứ 7 phải nhớ sửa hai chỗ
- **Vì sao không sửa ngay**: khác assembly (orders App không ref server); gom về một nguồn là việc riêng

### P-20260904-16 · [MỞ] · NẶNG — Brave cầu nối bị THU NHỎ (minimized) ⇒ Chromium bóp nghẹt mở tab ⇒ MỌI shop "chờ 30s chưa thấy tab shop mở", cả gói hỏng câm; un-minimize là hết
- **Thấy lúc**: đêm 04/09 — tôi foreground app lúc 02:04 (đo N10), Brave cầu nối rơi xuống minimized; shop 2→10 (Shop9x…Cicily) HỎNG "chờ 30s chưa thấy tab shop mở" suốt 40'; `ShowWindow(brave, SW_RESTORE)` lúc 02:43 ⇒ Bizly (shop 11) mở LẠI ĐƯỢC ngay (K0.1/K1.1 ok). Nguyên nhân xác định: cửa sổ Brave minimized, không phải lỗi extension/selector
- **Ở đâu**: hạ tầng cầu nối — Brave mở bởi `OrdersBridgeLauncher`/`BraveLaunchArgs`; SW mở tab chi tiết shop khi cửa sổ nền/minimized bị Chromium throttle
- **Bằng chứng**: 02:00 Alina (Brave còn hiện) xong trọn; 02:04:57→02:43:35 chín shop liên tiếp cùng lỗi "chờ 30s" với SW chỉ thấy `/portal/shop`; 02:46:46 Bizly xong ngay sau un-minimize
- **Vì sao NẶNG**: bất kỳ cái gì đẩy Brave xuống minimized giữa lượt auto (user đưa cửa sổ khác lên, app foreground, OS) là CẢ GÓI hỏng câm — và log chỉ nói "chờ 30s", không nói "vì minimized". Đề nghị: (a) mở tab bằng `chrome.tabs.create({active:true})` / focus tab qua CDP thay vì phụ thuộc window hiện; (b) launcher giữ Brave ở trạng thái không-minimized (hoặc off-screen nhưng KHÔNG iconic); (c) khi "chờ 30s chưa thấy tab" thì thử un-minimize + báo đúng nghi phạm
- **Bài học thao tác (đã vào memory)**: TUYỆT ĐỐI không ShowWindow/foreground/click app khi lát cắt cầu nối đang chạy; đọc UI bằng UIA pattern qua HWND không đổi focus

### P-20260904-17 · [MỞ] · VỪA — Cửa từ chối mở phiên của app (`DaTuChoiViLatCatDangChay`) và cửa mới `DangChayFlashHoacCauNoi` KHÔNG kiểm `CuaSoXemDangMo` ⇒ nút ▶ Chạy (app lẫn hub) mở phiên đè lên một cửa sổ "Mở list shop" còn để lại (hồ sơ Brave đang mượn)
- **Thấy lúc**: phản biện đợt Hub-điều-khiển (04/09) — lỗi có TỪ TRƯỚC đợt này
- **Ở đâu**: `AccountsViewModel.Phien.cs:~2092` và `:~766`; chỉ `NhipLichFlashAsync:~996` có cửa `CuaSoXemDangMo` riêng (vì `_bridgeRunning` "KHÔNG bắt được ca này"), hai đường mở phiên kia thì chưa
- **Vì sao không sửa ngay**: ngoài phạm vi đợt; đề nghị thêm cửa `CuaSoXemDangMo` cho cả hai đường mở phiên

### P-20260904-18 · [ĐÓNG (8c2c71f)] · CHẶN — Đợt Hub-điều-khiển-KM/Combo/Flash: phản biện chặn 3 NẶNG, chưa commit → user chốt per-tài-khoản, sửa 3 lượt, commit 8c2c71f
- Chi tiết + hướng sửa: `plans/2026-09-03-hub-dieu-khien-km-combo-flash-uu-tien-flash.md` mục "Báo cáo thực thi + PHẢN BIỆN". Cần user chốt: (1) dừng-từ-hub per-tài-khoản hay per-máy; (2) trả-khung-xuyên-máy có được cắt gói flash không. Bốn phát hiện còn lại (hạn KM/Combo, TraHoSo nuốt lượt hub, đồng hồ nhịp, timeout poll) có hướng sửa rõ.

### P-20260904-19 · [MỞ] · NHỎ (NGHI NGỜ) — GSheet trả HTTP 404 một lần lúc 04:21:25 04/09 giữa vòng check đơn trả hàng
- **Thấy lúc**: rà log watcher N10 sau khi đóng đợt (04:30 04/09) — ngoài phạm vi mọi việc đêm nay
- **Bằng chứng**: `GSheet: lỗi — System.InvalidOperationException: trả HTTP 404: <!DOCTYPE html>…`; các vòng sau 04:23 không thấy lỗi GSheet lặp lại ⇒ có thể one-off (rate-limit/redirect tạm), nhưng 404 trả HTML là dấu hiệu URL/endpoint sai hoặc sheet bị đổi quyền
- **Vì sao không sửa ngay**: dịch vụ ngoài, một lần, không phải code đợt này; theo dõi — tái diễn thì kiểm cấu hình GSheet (sheet id/quyền) của tài khoản

### P-20260904-20 · [MỞ] · NHỎ — Bàn thử `Ban.DangChayFlash()/DangChayLatCatKhac()` không truyền chủ lát cắt ⇒ `TaiKhoanLatCatDangChay` luôn null trong mọi bài cũ của lớp
- **Thấy lúc**: nghiệm thu lượt 2 đợt Hub-điều-khiển (04/09)
- **Ở đâu**: `orders/XuLyDonShopee.Tests/LenhTuHubUuTienFlashTests.cs` (helper `DangChayFlash`, `DangChayLatCatKhac`)
- **Bằng chứng**: helper gọi `DatTenLatCatChoTest(ten)` bỏ tham số `taiKhoan`; các bài mới lượt 3 truyền tay `ban.Id` + `giuCauNoi: true` nên không dựa vào helper
- **Vì sao không sửa ngay**: đổi helper kéo theo soát lại ~10 bài đang xanh; làm khi tiện

### P-20260904-21 · [MỞ] · NHỎ — `NhipLichKmAsync`/`NhipLichComboAsync` không có cửa `CuaSoXemDangMo` như `NhipLichFlashAsync` ⇒ vòng KM 2h sáng vẫn `DongCuaSoCuLai()` giết cửa sổ "Mở list shop" người dùng để lại
- **Thấy lúc**: phản biện lượt 2 đợt Hub-điều-khiển (04/09) — ngoài phạm vi
- **Ở đâu**: `orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.Phien.cs` đầu `NhipLichKmAsync` và `NhipLichComboAsync` (so với cửa riêng trong `NhipLichFlashAsync`)
- **Bằng chứng**: ba nhịp cùng dùng một hồ sơ Brave nhưng chỉ nhịp flash kiểm cửa sổ xem; cùng họ với P-20260904-17
- **Vì sao không sửa ngay**: ngoài phạm vi; gộp với P-20260904-17 khi per-acc hoá ở đợt đa làn (đợt 2)

### P-20260904-22 · [ĐÓNG (8c2c71f)] · VỪA — `StopSelected` bật cờ dừng-tay TOÀN MÁY khi tập tick giao `_dsTkDaDung` ⇒ "Dừng đã chọn" B cũng chặn lượt chạy lại của A cùng lượt mượn
- **Thấy lúc**: phản biện lượt 2 đợt Hub-điều-khiển (04/09) — ngoài phạm vi, nhưng cùng biến với NẶNG-A
- **Ở đâu**: `AccountsViewModel.Phien.cs` `StopSelected` + `TraHoSo`
- **Bằng chứng / cách đóng**: `_nguoiDungDungTay` (bool) → `HashSet<long> _dsTkNguoiDungDungTay`; `StopSelected` `UnionWith(ids)`; `TraHoSo` lọc theo từng id. Hai bài cũ `StopSelected_TickTaiKhoanKhac_*` / `_TickDungTaiKhoanDangMuon_*` vẫn xanh

### P-20260904-17 · BỔ SUNG 18:0x 04/09 · [MỞ] — đợt gương lát cắt làm lỗ này thành lời hứa hụt trên Hub
- **Thấy lúc**: phản biện `plans/2026-09-04-guong-mang-lat-cat-dang-chay.md` (VỪA-1)
- **Bổ sung**: `ChayLatCatAsync` `finally` hạ `_bridgeRunning` + xoá `LatCat` rồi kích gương, trong khi "Mở list shop" cố ý để cửa sổ + hồ sơ Brave (`dongCuaSoKhiXong: false`). Hub vừa học được `LatCat` thì ngay lập tức thấy null ⇒ ba nút ▶ KM/Combo/Flash sáng lại dù máy còn bận thật. `LyDoTuChoiLenhVongTuHub` (`Phien.cs:2249`) chỉ hỏi `_bridgeRunning`. **Cần user chốt** có nhét `CuaSoXemDangMo` vào hook gương / cửa từ chối hub không. Không sửa trong đợt gương (plan không làm cửa sổ xem).

### P-20260904-23 · [MỞ] · VỪA — lượt đẩy gương HỎNG xoá cờ bẩn + đóng mốc ⇒ tín hiệu lát cắt trễ đúng 60s (nhịp nền)
- **Thấy lúc**: phản biện đợt gương lát cắt 04/09
- **Ở đâu**: `suite/Shopee.Suite/Infrastructure/OrdersModuleHost.Mirror.cs:110-121` (`_mirrorDirty = false` trước `await`; `catch` không bật lại; `finally` luôn ghi `_mirrorLastPush`)
- **Bằng chứng**: flash mở lúc máy không có phiên ⇒ không có `Sessions.Changed` cứu. Tunnel timeout một lần ⇒ hub thấy rảnh tới nhịp nền 60s. Xmldoc sẵn có "trễ tối đa 60s" / "hub offline không gọi mạng mỗi 3s" — đây là backoff CỐ Ý của worker, không phải mất dữ liệu (lượt idle 60s vẫn đẩy đủ LatCat).
- **Vì sao không sửa ngay**: bật lại `_mirrorDirty` trong `catch` sẽ đẩy mỗi 3s khi hub chết, ngược thiết kế. Đổi backoff là việc riêng; không chặn đợt này (hội tụ trong 60s).

### P-20260904-24 · [MỞ] · NHỎ — không bài nào đo roundtrip HTTP/JSON field `latCat`
- **Thấy lúc**: phản biện đợt gương lát cắt 04/09
- **Ở đâu**: `HubOrdersLatCatTests` gọi thẳng `db.UpsertOrdersAccounts`; `GuongLatCatQuaHookTests` dựng DTO in-process
- **Bằng chứng**: DTO là file LINK dùng chung + `JsonSerializerDefaults.Web` nên bind camelCase hiện đúng; nếu sau này tách DTO / thêm `JsonPropertyName` lệch thì không test nào đỏ
- **Vì sao không sửa ngay**: không phải lỗi hiện tại; thêm bài serialize/deserialize khi tiện

### P-20260904-25 · [MỞ] · VỪA — bấm ▶ Chạy ở dòng tài khoản KHÁC vẫn ép lát cắt đang chạy nhường chỗ; Hub chỉ cảnh báo bằng tooltip
- **Thấy lúc**: nghiệm thu đợt gương lát cắt 04/09 (`CanhBaoChayDeNhuong` làm dôi so với plan)
- **Ở đâu**: `server/Shopee.Hub.Web/Components/DispatchViewLogic.cs:167`, gắn tooltip `DispatchOrdersTab.razor:389`
- **Bằng chứng**: xmldoc tự nêu "Không chặn nút (đổi thành chặn là quyết định khác, cần user chốt)". `LenhHubChayCheckDon` vẫn bật cờ nhường.
- **Vì sao không sửa ngay**: plan cố ý không chặn; **cần user chốt** có đổi ▶ Chạy của B thành blocked khi A đang giữ lát cắt trên cùng máy không.

### P-20260904-26 · [MỞ] · NHỎ — `HubOrdersLatCatTests.Dispose` không `SqliteConnection.ClearPool` trước khi xoá thư mục temp
- **Thấy lúc**: nghiệm thu đợt gương lát cắt 04/09
- **Ở đâu**: `server/Shopee.Hub.Web.Tests/HubOrdersLatCatTests.cs:167`
- **Bằng chứng**: so với `GuongLatCatQuaHookTests.Dispose` có `ClearPool`; trên Windows `hub.db` có thể còn bị giữ, thư mục `hub-latcat-test-*` tích `%TEMP%`; `catch` nuốt nên test không đỏ
- **Vì sao không sửa ngay**: không làm fail test; dọn khuôn khi tiện

### P-20260905-1 · [MỞ] · NHẸ — Tick ô Chuẩn bị hàng dính theo phiên, không theo ngày lọc
- **Thấy lúc**: phản biện `plans/2026-09-05-tick-theo-cot-action.md`
- **Ở đâu**: `orders/XuLyDonShopee.App/ViewModels/AccountsViewModel.KetQua.cs` (`ApplyShopCheckFlags` / `_shopDaCheck`)
- **Bằng chứng**: `_shopDaCheck` không theo ngày; đổi ô ngày hoặc sang ngày mới thì ✓ vẫn đứng cạnh số của ngày khác. Trước đây ✓ ở cột tiến độ trung tính; nay nằm trong ô "Chuẩn bị hàng".
- **Vì sao không sửa ngay**: hành vi session có từ trước; plan không persist tick check đơn. Cần user chốt nếu muốn khóa tick theo ngày lọc.

### P-20260905-2 · [MỞ] · NHẸ — Gỡ `RaiseShopCheckChanged` khỏi vòng chương trình cũng gỡ luôn `RefreshHubCountsAsync` mỗi shop xong
- **Thấy lúc**: phản biện tick theo cột
- **Ở đâu**: `AccountsViewModel.KetQua.cs:757-762` (nhánh `!checking` của `OnShopCheckChanged`); caller cũ `GhiTienDoChuongTrinh`
- **Bằng chứng**: vòng KM/Combo/Flash không còn hỏi hub sau mỗi shop. Đầu vòng `OnShopListChanged` vẫn hỏi một lần. Tác động nhỏ khi `xinKhungXuyenMay` chặn check đơn máy khác.
- **Vì sao không sửa ngay**: không thuộc tiêu chí plan; hỏi hub giữa vòng chương trình gần như không đổi số.

### P-20260905-3 · [MỞ] · NHẸ — Tick KM/Combo = "đã có dòng DB hôm nay", không phải "xong trong lượt đang chạy"
- **Thấy lúc**: phản biện tick theo cột
- **Ở đâu**: `ShopPrepareRow.ShowTickKm` / `ShowTickCombo`
- **Bằng chứng**: chạy lại KM trong cùng ngày thì shop đã ghi DB tick sẵn; không biết lượt 2 đã tới shop nào trong nhóm đó. Shop chưa chạy vẫn `—`.
- **Vì sao không sửa ngay**: đúng công thức plan (`*SoSanPham != null`); đổi nghĩa là quyết định UX riêng.

### P-20260905-4 · [MỞ] · VỪA — `GhiTienDoChuongTrinh` ghi `km_program_daily.day` theo `DateTime.Now`, khác `NgayDichFlash`
- **Thấy lúc**: phản biện tick theo cột (ngoài phạm vi)
- **Ở đâu**: `AccountsViewModel.Phien.cs` (`CongSoSanPhamChuongTrinh` + `DateTime.Now`) vs mẫu số ngày trọn dùng `NgayDichFlash`
- **Bằng chứng**: gói Flash vắt nửa đêm có thể tách số sang hai khóa ngày; tick bám theo số bị tách. Có từ ~28/08, không do bản vá tick.
- **Vì sao không sửa ngay**: ngoài phạm vi việc tick UI.

### P-20260905-5 · [MỞ] · NHỎ — `_shopChuongTrinh` không bao giờ `Remove` khỏi dictionary
- **Thấy lúc**: phản biện tick theo cột
- **Ở đâu**: `AccountsViewModel.KetQua.cs` (`OnShopProgramChanged`)
- **Bằng chứng**: xong shop chỉ hạ cờ `DangChay`, giữ mục theo `accountId` suốt đời tiến trình.
- **Vì sao không sửa ngay**: số tài khoản hữu hạn, không đổi logic hiển thị.

### P-20260905-6 · [MỞ] · NHỎ — xmldoc `ShopCheckChanged` vẫn nói phục vụ cả check đơn lẫn vòng chương trình
- **Thấy lúc**: nghiệm thu tick theo cột
- **Ở đâu**: `orders/XuLyDonShopee.App/Services/AppServices.cs` (~451–470)
- **Bằng chứng**: production chỉ còn `AccountSession` bắn sự kiện này; `GhiTienDoChuongTrinh` đã ngừng gọi.
- **Vì sao không sửa ngay**: comment lệch, file ngoài danh sách plan.

### P-20260915-1 · [ĐÓNG (Code.gs 15/09/2026)] · NẶNG — Đổi tài khoản Google deploy Web App làm mất quyền DriveApp khiến trắng cột B (Mã vận đơn) và C (Ảnh phiếu)
- **Thấy lúc**: người dùng đổi tài khoản Google deploy bản mới, các đơn từ 11/09 đến 15/09 (từ dòng 51 đến 65) bị để trống cột B và C
- **Ở đâu**: `orders/gsheet-apps-script/Code.gs:187`
- **Bằng chứng**: log `hoatdong-20260915.log` ghi nhận hàng loạt: `Exception: Bạn không có quyền thực hiện lệnh gọi DriveApp.getFoldersByName. Các quyền cần có: (https://www.googleapis.com/auth/drive.readonly || https://www.googleapis.com/auth/drive)`. Do khối upload Drive nằm chung try-block trước lệnh ghi cột B nên khi DriveApp throw, toàn bộ lệnh ghi mã vận đơn bị bỏ qua.
- **Cách xử lý**:
  1. Cô lập upload Drive vào try/catch riêng trong `Code.gs` để lỗi Drive không bao giờ chặn việc ghi mã vận đơn (cột B).
  2. Thêm hàm `capQuyenDrive()` để người dùng bấm Chạy duyệt quyền OAuth (Review Permissions) khi đổi tài khoản deploy.
  3. Chi tiết xem tại `orders/gsheet-apps-script/README.md`.

### P-20260921-1 · [ĐÓNG (chmod +x 21/09)] · NẶNG — File nhị phân Playwright node trên VPS (/opt/shopee-hub/.playwright/node/linux-x64/node) thiếu quyền thực thi (+x) khiến BigSellerReloginScheduler chết âm thầm từ 12/07
- **Thấy lúc**: người dùng nhờ check log bất thường mấy ngày qua (21/09/2026)
- **Ở đâu**: VPS `vps-muinx`, file `/opt/shopee-hub/.playwright/node/linux-x64/node`
- **Bằng chứng**: `journalctl -u shopee-hub` ghi nhận từ 12/07 đến 21/09: `relogin: <email> ✘ thất bại — hoãn thử lại 24h. (✘ Lỗi: An error occurred trying to start process '/opt/shopee-hub/.playwright/node/linux-x64/node' with working directory '/opt/shopee-hub'. Permission denied)`; lệnh `stat` trên VPS xác nhận file tạo lúc `2026-07-12 15:48:09` với quyền gốc `0664` (`-rw-rw-r--`).
- **Nguyên nhân gốc**: Triển khai từ môi trường Windows dev sang Linux VPS qua zip/scp không lưu cờ thực thi POSIX; lúc deploy chỉ `chmod +x` cho file chính `Shopee.Hub.Web` mà bỏ sót file Node trong thư mục ẩn `.playwright`.
- **Đã sửa**: `sudo chmod +x /opt/shopee-hub/.playwright/node/linux-x64/node` (quyền nay là `0775`), restart service `shopee-hub`, xác nhận `node --version` trả về `v24.15.0`; đã note vào `CLAUDE.md` mục Deploy.

