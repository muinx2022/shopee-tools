using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XuLyDonShopee.App.Services;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Models;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.App.ViewModels;

/// <summary>
/// <b>Phiên chạy</b> của màn Tài khoản: mở/dừng phiên (một tài khoản hoặc hàng loạt theo tick), lát cắt "Chạy
/// thử (bridge)" extension↔C#, đổ trạng thái phiên vào form và ghi cookie phiên bắt được về DB.
/// <para>Là phần <c>partial</c> của <see cref="AccountsViewModel"/> — property công khai vẫn nằm trên VM chính
/// (XAML bind thẳng <c>BusyStatus</c>/<c>OrderStatus</c>/<c>CanRun</c>/<c>CanStop</c>).</para>
/// </summary>
public partial class AccountsViewModel
{
    /// <summary>Dòng hướng dẫn/trạng thái hiển thị (đổ từ phiên của tài khoản đang chọn; null = ẩn).</summary>
    [ObservableProperty]
    private string? _busyStatus;

    /// <summary>Trạng thái theo dõi đơn "Chờ Lấy Hàng" (đổ từ phiên của tài khoản đang chọn; null = ẩn).</summary>
    [ObservableProperty]
    private string? _orderStatus;

    /// <summary>Cho dừng khi tài khoản đang chọn có phiên đang chạy.</summary>
    public bool CanStopSeller => _editingId is not null && _services.Sessions.IsRunning(_editingId ?? -1);

    /// <summary>
    /// Cho nút "Chạy" khi đang xem/sửa một tài khoản ĐÃ LƯU (có Id) — KHÔNG phụ thuộc phiên đang chạy. Bấm =
    /// MỞ PHIÊN (đăng nhập subaccount rồi tự lặp qua các shop). Tài khoản mới chưa lưu (IsNew) → tắt nút.
    /// </summary>
    public bool CanRun => IsEditing && !IsNew && _editingId is not null;

    /// <summary>Cho phép "dừng/đóng" khi có phiên production đang chạy, HOẶC đang chạy lát cắt cầu nối, HOẶC còn một
    /// cửa sổ "Mở list shop" để lại (xem <see cref="TinhCanStop"/>).
    /// <para><b>KHÔNG XAML nào bind cờ này</b> — đã grep cả <c>orders/</c> lẫn <c>suite/</c>: ribbon chỉ bind
    /// <c>StopSelected</c>/<c>StopAll</c>, còn nút "Đóng cửa sổ" bind <see cref="CoTheDongCuaSo"/>. Nó chỉ còn là
    /// cờ nội bộ/test; đừng tưởng sửa nó là đổi được nút nào trên màn hình.</para></summary>
    public bool CanStop => TinhCanStop(CanStopSeller, _bridgeRunning, CuaSoXemDangMo);

    /// <summary>
    /// PURE — có thứ gì đang mở cần đóng không. Ba nguồn ĐỘC LẬP, thiếu vế nào là có thứ đang mở mà không ai đóng
    /// được bằng UI.
    /// <para><b>Vế thứ ba là vế mới (18/08/2026) và là vế hay bị quên nhất:</b> "Mở list shop" cố ý ĐỂ NGUYÊN cửa
    /// sổ Brave sau khi chạy xong, nên đúng lúc người dùng cần nút Dừng nhất thì hai vế cũ đều false
    /// (<c>_bridgeRunning</c> đã hạ ở <c>finally</c>, phiên production thì chưa từng mở). Nút tắt = cửa sổ giữ khoá
    /// hồ sơ và extension của nó vẫn thử nối lại cổng 47821 mỗi 1200ms, cướp lệnh của phiên chạy kế.</para>
    /// </summary>
    internal static bool TinhCanStop(bool phienProductionDangChay, bool latCatDangChay, bool cuaSoXemDangMo)
        => phienProductionDangChay || latCatDangChay || cuaSoXemDangMo;

    /// <summary>
    /// Nút "Đóng cửa sổ" có bấm được không: còn cửa sổ để lại, HOẶC lát cắt đang chạy (lúc đó bấm = HUỶ lát cắt,
    /// vì <see cref="TryKillPoc"/> cancel <c>_bridgeCts</c>).
    /// <para><b>Vế <c>_bridgeRunning</c> là vế bắt buộc, đừng bỏ</b> (phản biện 18/08/2026). Chỉ dựa vào
    /// <see cref="CuaSoXemDangMo"/> thì nút TẮT suốt lúc lát cắt chạy — vì <c>_pocProcess</c> mãi tới
    /// <c>finally</c> trong mới được gán. Mà HAI câu duy nhất nhắc tên nút ("đợi xong hoặc bấm 'Đóng cửa sổ'" và
    /// <see cref="CauTuChoiViLatCatDangChay"/>) lại CHỈ phát ra khi <c>_bridgeRunning == true</c> — trùng khớp
    /// 100% với khoảng nút bị tắt, tức lại đúng cái lỗi "chỉ người dùng bấm nút không tồn tại". Nặng nhất ở nhánh
    /// cookie hết hạn: lát cắt đứng chờ nhập mã vài phút, người dùng muốn huỷ mà không có nút nào.</para>
    /// </summary>
    public bool CoTheDongCuaSo => CuaSoXemDangMo || _bridgeRunning;

    /// <summary>
    /// PURE — có phải đóng cửa sổ "Mở list shop" đang để lại không, khi một thao tác vừa tác động lên tập tài
    /// khoản <paramref name="idsTacDong"/> ("Dừng đã chọn" / "Chạy đã chọn").
    /// <para>Chỉ đóng khi cửa sổ thuộc ĐÚNG một trong những tài khoản bị tác động: "Dừng đã chọn" của tài khoản B
    /// không có lý gì cướp cửa sổ người dùng đang xem của tài khoản A. Chưa có cửa sổ nào (<c>null</c>) ⇒ false.</para>
    /// </summary>
    internal static bool NenDongCuaSoXem(long? pocAccountId, IReadOnlyCollection<long> idsTacDong)
        => pocAccountId is long id && idsTacDong.Contains(id);


    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // "KM của Shop" — form + luồng chạy thật (mở trình duyệt, tạo chương trình trên MỌI shop).
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mở form "KM của Shop" cho tài khoản đang xem: nạp cấu hình đã lưu → người dùng sửa → LƯU → nếu bấm
    /// "Chạy chương trình" thì chạy lượt KM THẬT.
    /// <para><b>Nút "Chạy chương trình" MỞ TRÌNH DUYỆT và TẠO CHƯƠNG TRÌNH THẬT trên MỌI shop của tài khoản</b>
    /// (tạm dừng check đơn, xong thì chạy lại). Không còn là pha 2 "chỉ lưu cấu hình" nữa — mọi câu chữ nói
    /// ngược lại đều là lời nói dối nguy hiểm ở đây.</para>
    /// <para><b>Đường "Đóng" chỉ lưu khi cấu hình HỢP LỆ.</b> Bản trước <c>Save</c> vô điều kiện: gõ dở giờ
    /// thành "9h3" rồi bấm Đóng là ghi đè cấu hình cũ bằng rác, và lượt tự động 2h sáng sau đó chạy bằng chính
    /// mớ rác ấy. Không hợp lệ ⇒ giữ nguyên cấu hình cũ, không báo lỗi chặn (người dùng chỉ đang đóng form).</para>
    /// </summary>
    [RelayCommand]
    private async Task MoFormKm()
    {
        if (_editingId is not long accountId)
        {
            return;
        }

        var acc = _services.Accounts.GetById(accountId);
        var email = acc?.Email ?? EditEmail;
        var soShop = _services.Results.GetShops(accountId).Count;

        var vm = DungFormKm(accountId, email, soShop);
        // Đọc LẠI từ form chứ không gọi hook lần thứ hai: hub bật/tắt được giữa chừng (nút "Kết nối ngay"), mà
        // lượt lưu phải đóng dấu ĐÚNG danh tính form đang hiển thị — không thì dòng "Máy chủ trì: … (máy này)"
        // người dùng vừa đọc và giá trị vừa ghi xuống là hai thứ khác nhau.
        var mayNay = vm.MayNay;
        var daBamChay = MoHopThoaiKm(vm);

        // LƯU cả khi chỉ bấm Đóng — nhưng CHỈ khi hợp lệ (xem xmldoc): người dùng chỉnh % giảm rồi đóng thì
        // lần sau mở lại phải thấy số đã chỉnh, còn cấu hình rác thì tuyệt đối không được ghi đè bản cũ.
        if (vm.CauHinhHopLe)
        {
            // ĐỌC LẠI dòng ngay TRƯỚC khi lưu (V6): form là modal, mở hàng phút, trong lúc đó worker kéo (60s)
            // hoặc `ChotLuotKm` có thể đã đẩy hai mốc chạy lên. Lấy ảnh chụp lúc MỞ mà ghi đè là mốc LÙI.
            var tuoi = _services.KmConfig.Get(accountId);
            var luuBan = KmDialogViewModel.BanLuuSauForm(
                tuoi, vm.BanNap, vm.LayCauHinh(), mayNay, DateTime.Now, vm.DaBamNhanChuTri);
            if (luuBan is not null)
            {
                _services.KmConfig.Save(luuBan);
                _services.KmHoSoDaDoi?.Invoke();
            }

            // (W5) Máy khác vừa đổi cấu hình trong lúc form mở ⇒ NÓI RA — ở CẢ hai kết cục (đã lưu đè hay
            // không lưu gì). Im lặng ở đây đúng là chỗ vòng 2 để lọt.
            if (KmDialogViewModel.CauCanhBaoSauForm(vm.BanNap, tuoi, luuBan is not null) is string canhBao)
            {
                _services.Log.Append(email, canhBao);
            }
        }

        if (!daBamChay || vm.TongKet is null || vm.VongYeuCauChay is not LoaiChuongTrinh vong)
        {
            return;
        }

        _services.Log.Append(email, vm.TongKet);
        BusyStatus = vm.TongKet;

        if (vong == LoaiChuongTrinh.Combo)
        {
            // ⚠ GỬI ĐÚNG GÓI ĐÃ KIỂM, không dựng lại (phản biện 26/08/2026, VỪA-3). Bản trước gọi
            // `DungThamSoCombo(vm.LayCauHinh(), DateTime.Now)` ở đây, tức một lượt đọc ĐỒNG HỒ THỨ HAI: bấm lúc
            // 23:59:5x thì câu tổng kết vừa ghi vào nhật ký nói `D → D+180` còn Shopee nhận `D+1 → D+181` — cái
            // được KIỂM không phải cái được GỬI. Nhánh KM ngay dưới vốn đã đúng; nhánh này nay theo cùng luật.
            //
            // `hanChot: null` vì đây là lượt BẤM TAY: người dùng đang ngồi trước máy, họ tự quyết khi nào dừng
            // (đúng luật đã ghi ở `KmLichTuDong.ConDuocBatDauShopMoi`). Trần chế độ thử thì vẫn áp — nó nằm ở
            // `ChayComboMoiShopAsync`, chung cho cả hai đường.
            if (vm.ThamSoComboDaKiem is not ThamSoCombo thamSoCombo)
            {
                // Bất biến của `ThuChayCombo`: `VongYeuCauChay == Combo` thì gói LUÔN có. Tới được đây nghĩa là
                // bất biến đó vỡ — KHÔNG đoán lấy một gói khác để chạy: đây là lượt tạo khuyến mãi THẬT trên
                // mọi shop.
                _services.Log.Append(email,
                    "Combo Khuyến Mãi: KHÔNG chạy — thông số của lượt vừa kiểm không còn (lỗi nội bộ). "
                    + "Mở lại form 'KM của Shop' rồi bấm nút \"Chạy Combo\" một lần nữa.");
                return;
            }
            GoiComboBamTayVuaGui = thamSoCombo;
            await ChayComboAsync(accountId, email, thamSoCombo, hanChot: null);
            return;
        }

        // Gói dựng bằng ĐÚNG builder của form — cùng object mà `ThuChay` vừa dùng để kiểm và để viết câu tổng
        // kết vừa ghi vào nhật ký. Dựng lại tay ở đây là hai lượt đọc độc lập: câu tổng kết nói một đằng, thứ
        // đi xuống Shopee một nẻo (§5c VỪA-2).
        await ChayKmAsync(accountId, email, vm.DungThamSoKm());
    }

    /// <summary>
    /// <b>Chỗ nối MỞ FORM "KM của Shop"</b> — production là <c>DialogService.ShowKm</c>, một modal WPF không
    /// dựng headless được. Trả <c>true</c> khi người dùng bấm một trong hai nút CHẠY và dữ liệu hợp lệ.
    /// <para><b>Vì sao phải là một chỗ nối rót được</b>: <see cref="MoFormKm"/> là nơi quyết định lượt bấm tay đi
    /// vào vòng KM hay vòng COMBO — tức nơi quyết định trình duyệt sắp tạo chương trình LOẠI NÀO trên mọi shop
    /// thật. Không có chỗ nối thì mọi test chỉ chạm được tới các hàm thuần hai bên, còn CHỖ RẼ thì không ai đo —
    /// đúng họ lỗ phủ "hàm đúng, chỗ nối không ai đo" đã cắn ba lần trong đợt này.</para>
    /// <para>Chữ ký <c>DialogService.ShowKm(vm) → bool</c> giữ NGUYÊN (nhiều test bám vào); vòng nào được yêu cầu
    /// chạy thì đọc ở <see cref="KmDialogViewModel.VongYeuCauChay"/>.</para>
    /// </summary>
    internal Func<KmDialogViewModel, bool> MoHopThoaiKm { get; set; } = DialogService.ShowKm;

    /// <summary>
    /// <b>Gói <see cref="ThamSoCombo"/> mà lượt BẤM TAY gần nhất đã GỬI ĐI CHẠY</b> — ghi ngay trước lượt gọi
    /// <see cref="ChayComboAsync"/> trong <see cref="MoFormKm"/>; <c>null</c> = chưa có lượt combo bấm tay nào.
    /// <para><b>Chỉ để ĐO, không ai đọc nó để quyết định gì</b> — và đó là lý do nó tồn tại. Mệnh đề cần khoá là
    /// "thứ GỬI đi đúng là thứ đã KIỂM" (phản biện 26/08/2026, VỪA-3), mà cả hai đầu đường thật đều nằm sau cửa
    /// khung xuyên máy + cửa khoá tài khoản: dòng nhật ký <c>cauBatDau</c> — chỗ duy nhất gói lộ ra ngoài — chỉ
    /// được ghi SAU khi đã phóng trình duyệt, tức không test nào chạm tới. Không có trường này thì đột biến
    /// "dựng lại gói bằng <c>DateTime.Now</c>" KHÔNG làm đỏ một test nào (đã đo).</para>
    /// <para>So bằng THAM CHIẾU với <c>KmDialogViewModel.ThamSoComboDaKiem</c> là phép đo duy nhất không lệ
    /// thuộc vào việc hai lượt đọc đồng hồ có tình cờ trùng ngày hay không.</para>
    /// </summary>
    internal ThamSoCombo? GoiComboBamTayVuaGui { get; private set; }

    /// <summary>
    /// <b>"KM của Shop" — chạy chương trình cho MỌI shop của tài khoản đang chọn.</b>
    /// <para>Đi đúng khung <see cref="ChayLatCatAsync"/> như "Mở list shop": tạm dừng check đơn → xin khoá xuyên
    /// máy (1 client / 1 acc / 1 thời điểm) → Brave sạch → SSO → lặp shop. Khác một điểm DUY NHẤT và quan trọng:
    /// <c>dongCuaSoKhiXong: true</c> — xong là đóng cửa sổ, nhả khoá và CHẠY LẠI check đơn.</para>
    /// <para>Ghi <c>LanChayCuoi</c> theo <see cref="GhiMocSauLuotKm"/> — mốc này là gốc tính "sau xx ngày" của
    /// lượt tự động.</para>
    /// </summary>
    /// <param name="hanChot">Mốc thôi bắt đầu shop mới — lượt TỰ ĐỘNG truyền 06:00 cùng ngày
    /// (<see cref="KmLichTuDong.HanChotLuotTuDong"/>), đường bấm tay truyền <c>null</c> (người dùng đang ngồi
    /// trước máy, họ tự quyết khi nào dừng).</param>
    private Task ChayKmAsync(long accountId, string email, ThamSoKm thamSo, DateTime? hanChot = null)
    {
        // RESET CỜ NHƯỜNG ngay trước lượt — xem `_nhuongChoCheckDon`. Cờ là của MÁY chứ không của gói flash,
        // nên một lượt hoãn đã nhường xong vẫn để nó bật cho tới lượt kế; quên reset là vòng KM dừng ngay sau
        // shop đầu, im lặng.
        _nhuongChoCheckDon = false;
        return ChayLatCatAsync(
            tenViec: "KM của Shop",
            cauBatDau: $"KM của Shop: mở Brave sạch → SSO → lặp qua MỌI shop, mỗi shop tạo chương trình \"{thamSo.Ten}\" "
                + $"({thamSo.NgayBatDau:dd-MM-yyyy} {thamSo.Gio} → {thamSo.NgayKetThuc:dd-MM-yyyy} {thamSo.Gio}, "
                + $"giảm {thamSo.PhanTram}%), nghỉ 3-5 phút giữa hai shop. "
                + (hanChot is DateTime h ? $"Không bắt đầu shop mới sau {h:HH:mm}. " : "")
                + "Check đơn tạm dừng, xong sẽ tự chạy lại.",
            trangThaiBatDau: "Đang chạy KM của Shop — mỗi shop vài phút, có nghỉ 3-5 phút giữa các shop...",
            than: async (phien, login, ct) =>
            {
                var kq = await phien.ChayKmMoiShopAsync(
                    login, thamSo, hanChot, ct, () => _nhuongChoCheckDon);
                return ChotLuotKm(accountId, kq, System.DateTime.Now);
            },
            dongCuaSoKhiXong: true,
            accountIdChiDinh: accountId,
            // CHỈ luồng KM nhờ hub dừng máy khác. Nút "Mở list shop" cố ý KHÔNG: đó là lượt xem vài giây, bắt máy
            // khác cắt vòng check đơn đang chạy dở để phục vụ nó là quá đắt.
            xinKhungXuyenMay: true,
            // Đổ list shop vào lưới (shop mới vẫn có dòng để nhận số). KHÔNG xóa tick check đơn —
            // tick chương trình nằm ở ô KM, không dùng chung `_shopDaCheck`.
            doVaoLuoiShops: true,
            luotChayMoi: false);
    }

    /// <summary>
    /// <b>Chốt MỘT lượt KM: ghi mốc <c>LanChayCuoi</c> + dựng câu tổng kết</b> — thân của <c>than</c> trong
    /// <see cref="ChayKmAsync"/>, tách thành một hàm để test khoá được CHỖ GỌI: mốc và câu tổng kết đi cùng
    /// nhau, bỏ ghi mốc là test câu tổng kết gãy chung (lỗ phủ mà phản biện vòng 4 chỉ ra — bốn test đo hàm
    /// ghi mốc lẻ đều xanh kể cả khi không ai gọi nó).
    /// <para><b>Luật ghi mốc:</b> chỉ cần <b>có ít nhất một shop tạo được</b> — ghi bừa cho một lượt hỏng sạch
    /// là đẩy lịch đi xx ngày cho một đêm chẳng tạo được gì.</para>
    /// <para><b>HẾT KHUNG vẫn GHI mốc — quyết định đã lật một lần, đọc trước khi lật lại.</b> Vòng 3 từng đổi
    /// thành "hết khung thì KHÔNG ghi" cho khớp câu log "để đêm sau"; vòng 4 chỉ ra hệ quả tệ hơn: không ghi
    /// thì <c>DenGioChay</c> tới hạn LẠI mỗi đêm ⇒ các shop ĐẦU danh sách nhận một chương trình MỖI ĐÊM (gấp
    /// <c>SoNgay</c> lần ý định người dùng) mà shop CUỐI vẫn đói — vòng luôn chạy lại từ shop 1. Nay ghi mốc
    /// để giữ đúng nhịp chu kỳ, và câu log nói thẳng "sẽ vào lượt kế theo chu kỳ" thay vì hứa "để đêm sau".
    /// Muốn shop cuối được chạy sớm hơn thì việc đúng là CON TRỎ TIẾN ĐỘ (lưu "lượt sau bắt đầu từ shop i") —
    /// ghi ở mục CÒN LẠI của plan, chưa làm.</para>
    /// </summary>
    /// <param name="accountId">Tài khoản của lượt vừa chạy.</param>
    /// <param name="kq">Kết quả cả lượt.</param>
    /// <param name="luc">Mốc ghi (nhận từ ngoài để test được, production truyền <c>DateTime.Now</c>).</param>
    internal KetLuanLatCat ChotLuotKm(long accountId, KetQuaKm kq, DateTime luc)
    {
        if (KmLichTuDong.NenGhiMoc(kq))
        {
            var cu = _services.KmConfig.Get(accountId);
            _services.KmConfig.Save(cu with { LanChayCuoi = luc });
            // Mốc CHUNG của tài khoản, không phải của máy: máy khác phải biết đêm nay đã chạy rồi.
            _services.KmHoSoDaDoi?.Invoke();
        }
        // `_dsTkDaDung` là tập phiên check đơn mà CHÍNH lượt này đã tạm dừng — nó còn nguyên tới lượt
        // `TraHoSo` ở `finally`, nên đọc ở đây là đọc đúng sự thật sắp xảy ra.
        return new KetLuanLatCat(CauTongKetKm(kq, _dsTkDaDung.Count > 0), CauTrangThaiKm(kq));
    }

    /// <summary>
    /// <b>Một nhịp của LỊCH KM TỰ ĐỘNG</b> (gọi từ đồng hồ 60 giây của VM). Quét mọi tài khoản, tài khoản nào
    /// tới hạn theo <see cref="KmLichTuDong.DenGioChay"/> thì chạy lượt KM cho nó.
    /// <para><b>Chỉ chạy MỘT tài khoản mỗi nhịp</b> và bỏ nhịp khi đang có lát cắt chạy: hai lượt KM chồng nhau
    /// sẽ tranh cùng một hồ sơ Brave và cùng cổng 47821. Tài khoản còn lại đợi nhịp sau — khung 2–6h dài 4 tiếng,
    /// thừa chỗ cho vài lượt nối đuôi.</para>
    /// <para><b>Cờ <see cref="_dangChayKmTuDong"/></b> chặn nhịp 60 giây bắn lượt thứ hai trong lúc lượt đầu còn
    /// chạy: một vòng 12 shop mất ~50 phút, tức khoảng 50 nhịp đồng hồ sẽ đi qua trong lúc nó chạy.</para>
    /// <para><b>KIỂM CẤU HÌNH TRƯỚC KHI ĐỐT LƯỢT</b> (phản biện vòng 2, 19/08/2026). Ca thật:
    /// <c>GioBatDau = "01:00"</c> hoàn toàn hợp lệ với form, nhưng lượt tự động 02:05 gửi xuống một mốc QUÁ KHỨ
    /// ⇒ picker chặn ⇒ 12 shop đỏ. Và vì <c>LanThuTuDong</c> đã ghi, đêm nào cũng lặp lại đúng như vậy — vĩnh
    /// viễn, không một dòng log nào nói vì sao. Nay không đạt thì ghi MỘT dòng nói rõ lý do, vẫn ghi
    /// <c>LanThuTuDong</c> (kẻo nhịp 60 giây spam lại dòng đó tới 6h sáng), và KHÔNG phóng trình duyệt.</para>
    /// <para><b>Đính chính 26/08/2026 — ca <c>"01:00"</c> ĐỎ TRỞ LẠI.</b> Bản 25/08 đẩy cả chương trình đi 180
    /// ngày nên mọi giờ đều ở tương lai và vế này tự tan; nay mốc BẮT ĐẦU là <b>hôm nay</b> lúc
    /// <c>GioBatDau</c>, nên đúng ca thật của phản biện vòng 2 (nhịp 02:05 với giờ 01:00) lại là mốc quá khứ và
    /// lại bị chặn ở đây — như thiết kế. Cấu hình đúng của vòng KM là giờ 09:30, muộn hơn khung 02–04h.</para>
    /// </summary>
    internal async Task NhipLichKmAsync(DateTime bayGio)
    {
        MocNhipDaNhanChoTest?.Invoke("KM", bayGio);
        if (_dangChayKmTuDong || _bridgeRunning) { return; }

        var mayNay = _services.MayNay?.Invoke() ?? "";

        foreach (var acc in _services.Accounts.GetAll())
        {
            var cf = _services.KmConfig.Get(acc.Id);
            if (!KmLichTuDong.DenGioChay(cf, bayGio, LoaiChuongTrinh.ChuongTrinhShop)) { continue; }

            if (!KmLichTuDong.LaMayChuTri(cf.OwnerMachine, mayNay))
            {
                // KHÔNG ghi `LanThuTuDong`: mốc đó là "tài khoản này đã dùng lượt thử của khung đêm nay", mà
                // lượt ấy không phải của mình — ghi vào là ĐỐT lượt của đúng máy chủ trì, và nó mất trắng đêm.
                BaoKhongPhaiChuTri(acc.Id, acc.Email, cf.OwnerMachine, bayGio);
                continue;
            }

            // DỰNG THAM SỐ MỘT LẦN, RỒI MỌI THỨ ĐỌC TỪ CHÍNH NÓ — kể cả dòng nhật ký. ĐỪNG tách ra thành vài
            // biến rời rồi mỗi chỗ tự dùng một biến.
            //
            // Vì sao (phản biện 25/08/2026, lỗ phủ ĐÃ ĐO): bản trước giữ `ngayCt` rời và dòng log in `ngayCt`
            // còn `ThamSoKm` nhận `ngayCt` — hai lượt đọc độc lập. Đổi ĐÚNG một chỗ (`ThamSoKm` về
            // `bayGio.Date`, tức trả lại hành vi CŨ) thì **cả 216 test KM vẫn XANH**: mọi test nhịp đều bị chặn
            // ở cửa khoá xuyên máy nên `cauBatDau` — chỗ duy nhất `thamSo.Ngay` lộ ra ngoài — không bao giờ
            // được ghi, và thước duy nhất còn lại là dòng log kia. Tức bộ test đang bảo vệ DÒNG NHẬT KÝ chứ
            // không bảo vệ THỨ ĐI XUỐNG SHOPEE.
            //
            // Nay log in `thamSo.Ngay`/`thamSo.Gio*`: một object, một nguồn. Đổi ngày trong `ThamSoKm` là dòng
            // log đổi theo ⇒ test đỏ. Không cần hạ tầng test mới, chỉ cần thôi đọc hai lần.
            var thamSo = DungThamSoKm(cf, bayGio);

            // Dùng LẠI đúng luật thuần của form — hai bộ luật song song thì bộ ít chạy sẽ mục dần.
            //
            // ⚠ Vế `KiemTraMocTuongLai` LẠI SỐNG (26/08/2026): mốc bắt đầu nay là HÔM NAY lúc `GioBatDau`, nên
            // một cấu hình `GioBatDau = "01:00"` gặp nhịp 02:05 là mốc quá khứ ⇒ chặn, KHÔNG phóng trình duyệt.
            // Đó đúng là ca thật của phản biện vòng 2 (19/08/2026) mà bản 25/08 đã vô tình làm tan mất.
            //
            // ⚠ SỐ SHOP phải truyền THẬT, không được để mặc: biên an của mốc bắt đầu tính theo nó. Ca hỏng nếu
            // bỏ qua (phản biện 26/08/2026, ca B): `GioBatDau = "02:05"` hợp lệ với mọi cửa kiểm cũ, mà khung KM
            // chạy 02–04h nên tới lúc mở được shop đầu thì mốc đã qua ⇒ MỌI shop đỏ ⇒ `SoTao = 0` ⇒ `ChotLuotKm`
            // cố ý không ghi mốc chạy ⇒ đêm nào cũng lặp y hệt, VĨNH VIỄN.
            var soShopTuDong = _services.Results.GetShops(acc.Id).Count;
            var loiCauHinh =
                KmDialogViewModel.KiemTraForm(thamSo.Ten, thamSo.Gio, thamSo.PhanTram, cf.TuDong, cf.SoNgay)
                ?? KmDialogViewModel.KiemTraMocTuongLai(
                    thamSo.NgayBatDau, thamSo.Gio, bayGio, soShopTuDong);
            if (loiCauHinh is not null)
            {
                // VẪN ghi mốc thử: không ghi thì nhịp 60 giây lặp lại chính dòng này mỗi phút tới 6h sáng.
                _services.KmConfig.Save(cf with { LanThuTuDong = bayGio });
                _services.KmHoSoDaDoi?.Invoke();
                _services.Log.Append(acc.Email,
                    "Lịch KM tự động: tới hạn nhưng CẤU HÌNH KHÔNG DÙNG ĐƯỢC — " + loiCauHinh
                    + " KHÔNG mở trình duyệt. Mở form 'KM của Shop' ở máy này, sửa lại thông số rồi bấm \"Đóng\" "
                    + "— đêm sau lượt tự động mới chạy được.");
                continue;
            }

            _dangChayKmTuDong = true;
            try
            {
                // ĐÁNH DẤU LƯỢT THỬ **TRƯỚC** KHI CHẠY — mốc này là thứ chặn nhịp 60 giây bắn lượt thứ hai
                // trong cùng khung 2–6h khi lượt này HỎNG (captcha / khoá / lỗi shop đầu). Ghi sau khi chạy thì
                // đúng ca cần chặn nhất — lượt ném giữa chừng — lại là ca không bao giờ ghi được.
                // Cờ `_dangChayKmTuDong` chỉ sống trong bộ nhớ một phiên app; mốc này bền qua restart.
                // (Đẩy lên hub ở đây vẫn là fire-and-forget — "đẩy XONG rồi mới phóng trình duyệt" thuộc pha sau.)
                _services.KmConfig.Save(cf with { LanThuTuDong = bayGio });
                _services.KmHoSoDaDoi?.Invoke();

                var hanChot = KmLichTuDong.HanChotLuotTuDong(bayGio, LoaiChuongTrinh.ChuongTrinhShop);
                _services.Log.Append(acc.Email,
                    $"Lịch KM tự động: tới hạn (mỗi {cf.SoNgay} ngày, lần cuối "
                    + (cf.LanChayCuoi is DateTime c ? c.ToString("dd-MM-yyyy HH:mm") : "chưa chạy")
                    + $") — bắt đầu lượt KM trong khung {KmLichTuDong.GioMoKhung}-{KmLichTuDong.GioDongKhung}h, "
                    + $"không bắt đầu shop mới sau {hanChot:HH:mm}. "
                    // NÓI RA NGÀY CHƯƠNG TRÌNH ngay ở dòng này, đừng để nó chỉ nằm trong câu `cauBatDau` của
                    // `ChayLatCatAsync`: câu kia chỉ được ghi SAU khi mượn được hồ sơ, nên đúng những đêm lượt
                    // chết ở cửa khoá là những đêm nhật ký không hề nói chương trình định đặt cho ngày nào. Mà
                    // "+180 ngày" là con số người đọc log sẽ giật mình lần đầu gặp — phải thấy được nó.
                    //
                    // ⚠ ĐỌC TỪ `thamSo`, KHÔNG phải từ `ngayCt`/`cf` — đây là thứ biến dòng log thành THƯỚC ĐO
                    // của gói gửi xuống Shopee thay vì một chuỗi song song tự nhất quán. Xem khối chú thích ở
                    // chỗ dựng `thamSo`.
                    + $"Chương trình chạy từ {thamSo.NgayBatDau:dd-MM-yyyy} {thamSo.Gio} tới "
                    + $"{thamSo.NgayKetThuc:dd-MM-yyyy} {thamSo.Gio} "
                    + $"(khoảng {KmLichTuDong.SoNgayDatTruoc} ngày, cùng giờ hai đầu). "
                    + "Đêm nay KHÔNG thử lại lượt nữa dù lượt này hỏng.");

                await ChayKmAsync(acc.Id, acc.Email, thamSo, hanChot);
            }
            finally
            {
                _dangChayKmTuDong = false;
            }
            return; // một tài khoản mỗi nhịp
        }
    }

    // ══════════════════════ COMBO KHUYẾN MÃI — vòng thứ HAI (25/08/2026) ══════════════════════

    /// <summary>
    /// <b>"Combo Khuyến Mãi" — chạy chương trình combo cho MỌI shop của một tài khoản.</b>
    /// <para><b>Đi ĐÚNG <see cref="ChayLatCatAsync"/> với <c>xinKhungXuyenMay: true</c>, y hệt
    /// <see cref="ChayKmAsync"/></b> — tức đủ chuỗi: nhờ hub DỪNG check đơn ở máy khác → tạm dừng phiên check
    /// đơn cục bộ → chờ dừng hẳn → xin khoá <c>orders:&lt;login&gt;</c> → Brave sạch → SSO → lặp shop → xong thì
    /// NHẢ KHUNG + chạy lại check đơn ở mọi máy đã dừng.</para>
    /// <para><b>⚠ ĐỪNG viết đường phóng trình duyệt thứ hai cho combo.</b> Thứ tự ba bước trong
    /// <see cref="ChayLatCatAsync"/> có một khối chú thích "ĐỪNG SỬA" ghi ca hỏng thật (một khoá, hai chủ,
    /// không đếm chủ) — bản sao thứ hai sẽ đảo mất đúng thứ tự đó. Cách rẻ và đúng nhất là gọi lại chính nó,
    /// chỉ thay phần <c>than</c>.</para>
    /// </summary>
    /// <param name="hanChot">Mốc thôi bắt đầu shop mới — lượt TỰ ĐỘNG truyền <b>08:00</b>
    /// (<see cref="KmLichTuDong.HanChotLuotTuDong"/> của <see cref="LoaiChuongTrinh.Combo"/>).</param>
    /// <remarks><c>internal</c> (không <c>private</c>) để test khoá được CHỖ GỌI — cờ <c>xinKhungXuyenMay</c>
    /// phải đo được ĐỘC LẬP với lịch tự động: đây cũng là đường của nút bấm tay.</remarks>
    internal Task ChayComboAsync(long accountId, string email, ThamSoCombo thamSo, DateTime? hanChot = null)
    {
        // RESET CỜ NHƯỜNG ngay trước lượt — cùng lý do với `ChayKmAsync`, xem `_nhuongChoCheckDon`.
        _nhuongChoCheckDon = false;
        return ChayLatCatAsync(
            tenViec: "Combo Khuyến Mãi",
            // Mô tả chương trình đọc TỪ CHÍNH `thamSo` (`ThamSoCombo.MoTa`) — không ghép lại từ biến rời.
            cauBatDau: "Combo Khuyến Mãi: mở Brave sạch → SSO → lặp qua MỌI shop, mỗi shop tạo combo "
                + thamSo.MoTa() + ", nghỉ 3-5 phút giữa hai shop. "
                + (hanChot is DateTime h ? $"Không bắt đầu shop mới sau {h:HH:mm}. " : "")
                + "Check đơn tạm dừng, xong sẽ tự chạy lại.",
            trangThaiBatDau: "Đang chạy Combo Khuyến Mãi — mỗi shop vài phút, có nghỉ 3-5 phút giữa các shop...",
            than: async (phien, login, ct) =>
            {
                var kq = await phien.ChayComboMoiShopAsync(
                    login, thamSo, hanChot, ct, () => _nhuongChoCheckDon);
                return ChotLuotCombo(accountId, kq, System.DateTime.Now);
            },
            dongCuaSoKhiXong: true,
            accountIdChiDinh: accountId,
            xinKhungXuyenMay: true,
            // Cùng lý do với `ChayKmAsync` — đổ lưới, không xóa tick check đơn.
            doVaoLuoiShops: true,
            luotChayMoi: false);
    }

    /// <summary>
    /// <b>Chốt MỘT lượt COMBO: ghi mốc <c>ComboLanChayCuoi</c> + dựng câu tổng kết</b> — thân của <c>than</c>
    /// trong <see cref="ChayComboAsync"/>, tách ra để test khoá được CHỖ GỌI (cùng lý do với
    /// <see cref="ChotLuotKm"/>).
    /// <para><b>⚠ GHI <c>ComboLanChayCuoi</c>, TUYỆT ĐỐI KHÔNG PHẢI <c>LanChayCuoi</c></b> — xem
    /// <c>KmLichTuDong.MocThuCua</c>: gộp hai mốc là một vòng tự tay khoá vòng kia, câm lặng và vĩnh viễn.</para>
    /// <para>Luật ghi mốc y vòng kia: chỉ cần <b>có ít nhất một shop tạo được</b>.</para>
    /// </summary>
    internal KetLuanLatCat ChotLuotCombo(long accountId, KetQuaKm kq, DateTime luc)
    {
        if (KmLichTuDong.NenGhiMoc(kq))
        {
            var cu = _services.KmConfig.Get(accountId);
            _services.KmConfig.Save(cu with { ComboLanChayCuoi = luc });
            // Mốc CHUNG của tài khoản, không phải của máy: máy khác phải biết đêm nay đã chạy rồi.
            _services.KmHoSoDaDoi?.Invoke();
        }
        return new KetLuanLatCat(
            CauTongKetCombo(kq, _dsTkDaDung.Count > 0), CauTrangThaiKm(kq, LoaiChuongTrinh.Combo));
    }

    /// <summary>Câu NHẬT KÝ tổng kết một lượt COMBO — cùng thân với <see cref="CauTongKetKm"/>, chỉ đổi
    /// <see cref="LoaiChuongTrinh"/>, để hai vòng không có hai bộ luật "đếm shop hỏng / hết khung" khác nhau
    /// (và để giờ đóng khung in ra là 8:00 của combo chứ không phải 4:00 của vòng kia).</summary>
    internal static string CauTongKetCombo(KetQuaKm kq, bool coPhienChayLai)
        => CauTongKetKm(kq, coPhienChayLai, LoaiChuongTrinh.Combo);

    // ══════════════════════ FLASH SALE CỦA SHOP — vòng thứ BA (28/08/2026) ══════════════════════

    /// <summary>TÊN VIỆC của một gói Flash Sale — cùng chuỗi đi vào <see cref="ChayLatCatAsync"/> và ra
    /// <see cref="TenLatCatDangChay"/>. Hằng chứ không phải hai chuỗi rời: câu nhường so ĐÚNG chuỗi này để biết
    /// nên nói "Gói Flash Sale" hay "Lượt '&lt;tên&gt;'", và hai bản chép tay lệch nhau một dấu cách là câu log
    /// im lặng nói sai tên.</summary>
    internal const string TenViecFlash = "Flash Sale";

    /// <summary>
    /// <b>"Flash Sale Của Shop" — chạy flash sale cho MỌI shop của một tài khoản.</b>
    /// <para><b>Đi ĐÚNG <see cref="ChayLatCatAsync"/> với <c>xinKhungXuyenMay: true</c>, y hệt
    /// <see cref="ChayComboAsync"/></b> — nhờ hub DỪNG check đơn ở máy khác → tạm dừng phiên cục bộ → xin khoá
    /// <c>orders:&lt;login&gt;</c> → Brave sạch → SSO → lặp shop → xong thì NHẢ KHUNG + chạy lại check đơn.
    /// <b>⚠ ĐỪNG viết đường phóng trình duyệt thứ ba</b> — lý do đã ghi ở <see cref="ChayComboAsync"/>.</para>
    /// <para><b>Khác hai vòng kia đúng MỘT chỗ ở tầng này:</b> lượt tự động chạy thành nhiều GÓI trong ngày, mỗi
    /// gói chiếm một khe nghỉ của check đơn và trả lại phần khe còn thừa qua
    /// <see cref="MocChayLaiCheckDonSauFlash"/> — xem <see cref="NhipLichFlashAsync"/>.</para>
    /// </summary>
    /// <param name="hanChot">Mốc thôi bắt đầu shop mới. Lượt TỰ ĐỘNG truyền <b>hạn của GÓI</b> =
    /// <c>min(cuối khe nghỉ − 45', 22:15)</c> (xem <see cref="NhipLichFlashAsync"/>); đường bấm tay truyền hạn
    /// ngày 22:15 (<see cref="KmLichTuDong.HanChotLuotTuDong"/>).</param>
    /// <param name="boQuaShop">Shop đã XONG trong ngày, lọc khỏi gói này — <c>null</c> ở đường bấm tay (người
    /// dùng ngồi trước máy, họ muốn chạy cái gì là việc của họ).</param>
    /// <param name="phaiNhuongSauShop">Cờ NHƯỜNG CHỖ cho check đơn, rót xuống vòng shop — hai đường gọi đều
    /// truyền <c>() =&gt; _nhuongChoCheckDon</c>. Xem <see cref="_nhuongChoCheckDon"/>.</param>
    /// <remarks><c>internal</c> (không <c>private</c>) để test khoá được CHỖ GỌI — cùng lý do với
    /// <see cref="ChayComboAsync"/>.</remarks>
    internal Task ChayFlashSaleAsync(
        long accountId, string email, ThamSoFlashSale thamSo, DateTime? hanChot = null,
        IReadOnlySet<string>? boQuaShop = null, Func<bool>? phaiNhuongSauShop = null)
        => ChayLatCatAsync(
            tenViec: TenViecFlash,
            // Mô tả đọc TỪ CHÍNH `thamSo` (`ThamSoFlashSale.MoTa`) — không ghép lại từ biến rời.
            cauBatDau: "Flash Sale Của Shop: mở Brave sạch → SSO → lặp qua MỌI shop, mỗi shop nhặt HẾT khung giờ "
                + "chưa dùng của " + thamSo.MoTa() + " (mỗi khung một chương trình, 10 sản phẩm bán chạy nhất "
                + "của trang tương ứng), nghỉ 3-5 phút giữa hai shop. "
                + (hanChot is DateTime h ? $"Không bắt đầu shop mới sau {h:HH:mm}. " : "")
                + "Check đơn tạm dừng, xong sẽ tự chạy lại.",
            trangThaiBatDau: "Đang chạy Flash Sale Của Shop — mỗi shop nhặt HẾT khung Shopee mở cho ngày đích, có "
                + "nghỉ 3-5 phút giữa các shop...",
            than: async (phien, login, ct) =>
            {
                var kq = await phien.ChayFlashSaleMoiShopAsync(
                    login, thamSo, hanChot, boQuaShop, ct, phaiNhuongSauShop);
                return ChotLuotFlash(accountId, email, kq, System.DateTime.Now);
            },
            dongCuaSoKhiXong: true,
            accountIdChiDinh: accountId,
            xinKhungXuyenMay: true,
            // Cùng lý do với `ChayKmAsync` — đổ lưới, không xóa tick check đơn.
            doVaoLuoiShops: true,
            luotChayMoi: false,
            // Hai việc RIÊNG của vòng flash ở tầng khung: ghi SỐ SHOP đọc được từ picker (mẫu số của luật
            // "ngày đã trọn") và XOÁ mốc hoãn khi không mượn được hồ sơ. Xem xmldoc `ChayLatCatAsync`.
            laLuotFlash: true);

    /// <summary>
    /// <b>Chốt MỘT GÓI FLASH SALE: cập nhật tiến độ ngày (in-memory) + ghi mốc CHỈ KHI ngày đã TRỌN + dựng câu
    /// tổng kết.</b>
    ///
    /// <para><b>⚠ Ý NGHĨA CỦA <c>FlashLanChayCuoi</c> ĐÃ ĐỔI 01/09/2026</b> — đây là chốt quan trọng nhất của
    /// cả đợt. Cũ: <i>"lượt gần nhất TẠO ĐƯỢC gì đó"</i> (ghi khi <c>SoTao &gt; 0</c>). Mới:
    /// <b><i>"ngày đích của hôm đó đã phủ ĐỦ MỌI SHOP"</i></b>. Vì một ngày nay chia thành nhiều GÓI nhỏ, luật
    /// cũ sẽ cho gói ĐẦU TIÊN ghi mốc rồi <c>DenGioGoiFlash</c> khoá sạch các gói còn lại — 10 shop cuối không
    /// bao giờ được chạy, không một dòng log nào nói ra.</para>
    /// <para>Hệ quả tốt kèm theo: một lượt BẤM TAY lẻ vài shop KHÔNG còn "đốt" lượt tự động của cả ngày (lỗi
    /// thật đêm 31/08: lượt tay 19:04 ghi mốc ⇒ 21h auto lặng lẽ nghỉ). Ngoại lệ mốc-ngoài-khung của commit
    /// <c>6582e64</c> vì thế đã gỡ được ở <c>KmLichTuDong.DenGioChay</c>.</para>
    ///
    /// <para><b>Thế nào là XONG một shop:</b> <c>DaTao</c> (tạo/tái chế được) HOẶC <c>BoQua</c> khác null
    /// <b>mà KHÔNG phải vì "ngày đích không còn đỏ"</b> (hết khung · không có sản phẩm). Shop có <c>Loi</c> thì
    /// KHÔNG xong — gói sau thử lại nó.</para>
    /// <para><b>⚠ Shop khai <c>NgayMaiKhongDo</c> mà chưa tạo được gì thì KHÔNG XONG</b> (sửa 01/09/2026, phản
    /// biện vòng 1). Bản đầu gộp nó vào <c>BoQua</c> nên shop ấy bị coi là xong VĨNH VIỄN trong ngày: nếu lịch
    /// mất đỏ vì Shopee render chậm — hoặc vì ngày đó Shopee mở khung muộn — thì shop bị gạt khỏi MỌI gói sau,
    /// câm lặng, và ngày vẫn được ghi mốc "đã trọn". Để nó ở lại tập chưa-xong thì gói sau thử lại: giá là một
    /// lượt render, còn giá của chiều kia là mất trắng shop đó cả ngày.</para>
    ///
    /// <para><b>⚠ TIẾN ĐỘ GIỮ TRONG BỘ NHỚ, KHÔNG XUỐNG DB — đừng "cho đủ bộ".</b> Hồ sơ KM đồng bộ xuyên máy
    /// và fleet có nhiều máy: một con trỏ tiến độ trong DB TRÔI LỆCH ngay khi một máy bỏ lỡ một lượt (máy A
    /// tưởng đã xong 8 shop, máy B tưởng mới 3, không ai biết ai đúng). Bộ nhớ thì mất khi tắt app — và đó là
    /// hành vi ĐÚNG: nguồn sự thật là Shopee, gói sau mở lại shop đã xong sẽ thấy hết khung và tự khai
    /// <c>BoQua</c>, tốn một lượt render chứ không tạo trùng.</para>
    /// </summary>
    /// <param name="accountId">Tài khoản của lượt vừa chạy (để ghi mốc + khoá tiến độ).</param>
    /// <param name="email">Tài khoản của lượt (chỉ để ghi nhật ký).</param>
    /// <param name="kq">Kết quả cả lượt.</param>
    /// <param name="luc">Mốc "bây giờ" (nhận từ ngoài để test được; production truyền <c>DateTime.Now</c>).</param>
    internal KetLuanLatCat ChotLuotFlash(long accountId, string email, KetQuaKm kq, DateTime luc)
    {
        var ngayDich = DateOnly.FromDateTime(KmLichTuDong.NgayDichFlash(luc));
        var xong = TapShopXongFlash(accountId, ngayDich);

        foreach (var s in kq.Shops)
        {
            var khoa = KhoaTienDo(s);
            if (khoa.Length > 0 && ShopDaXongFlash(s)) { xong.Add(khoa); }
        }

        // NGÀY ĐÃ TRỌN? NĂM vế, thiếu một là chưa. Bốn vế đầu nói về LƯỢT VỪA CHẠY: không hỏng cả cụm (`Loi`),
        // không bị captcha cắt, không còn shop nào chưa kịp chạy (`SoShopChuaChay`), và MỌI shop đã chạy đều
        // XONG theo nghĩa `ShopDaXongFlash`.
        //
        // Vế thứ NĂM nói về CẢ NGÀY, và nó là lưới chống một ca hỏng câm (phản biện vòng 1, 01/09/2026): bốn vế
        // trên chỉ đo được cái LƯỢT NÀY nhìn thấy, mà lượt này chạy trên danh sách ĐÃ BỊ LỌC bởi `boQuaShop`.
        // Picker Shopee render thiếu — 5/12 shop, đã xảy ra thật — là gói thấy 5, làm xong 5, rồi khai "trọn"
        // cho một ngày còn 7 shop chưa ai đụng tới. `SoShopTungThayFlash` giữ SỐ SHOP LỚN NHẤT từng đọc được
        // trong ngày, nên tập xong phải phủ tới con số ấy mới được ghi mốc.
        //
        // ⚠ Lưới này KHÔNG cứu được ca picker thiếu ngay từ GÓI ĐẦU của ngày (lúc đó số lớn nhất cũng là số
        // thiếu) — chấp nhận có chủ đích: nó cần một con trỏ xuống DB để làm hơn, mà đó là thứ §0 của plan cấm.
        var tungThay = SoShopTungThayFlash(accountId, ngayDich);
        var daTron = kq.Loi is null && !kq.Captcha && kq.SoShopChuaChay == 0
            && kq.Shops.All(ShopDaXongFlash)
            && tungThay > 0 && xong.Count >= tungThay;
        if (daTron)
        {
            var cu = _services.KmConfig.Get(accountId);
            // ⚠ `FlashLanChayCuoi`, TUYỆT ĐỐI KHÔNG PHẢI `LanChayCuoi`/`ComboLanChayCuoi` — xem
            // `KmLichTuDong.MocThuCua`: gộp mốc là một vòng tự tay khoá vòng kia, câm lặng và vĩnh viễn.
            _services.KmConfig.Save(cu with { FlashLanChayCuoi = luc });
            // Mốc CHUNG của tài khoản, không phải của máy: máy khác phải biết ngày này đã xong rồi.
            _services.KmHoSoDaDoi?.Invoke();
            _services.Log.Append(email,
                $"Flash Sale: ngày đích {ngayDich:dd-MM-yyyy} đã ĐỦ MỌI SHOP ({xong.Count}/{tungThay} shop) — "
                + "ghi mốc ngày, thôi mở gói mới cho tới sang ngày.");
        }

        // MỌI SHOP CỦA GÓI ĐỀU BÁO "NGÀY ĐÍCH KHÔNG CÒN ĐỎ" — ghi MỘT DÒNG rồi thôi.
        //
        // ⚠ CỐ Ý KHÔNG CÓ CỬA NÀO ĐÓNG NGÀY Ở ĐÂY (chốt 01/09/2026, phản biện vòng 1). Bản đầu của đợt đặt cờ
        // `_flashNgayDongCua` tại đúng chỗ này và nhịp thôi mở gói tới hết ngày; đã GỠ HẲN. Lý do: bằng chứng
        // của một GÓI ~2 shop không đủ để kết luận cho 12 shop. Ba đường dựng ra một cờ SAI-DƯƠNG mà không đường
        // nào hiếm — (1) gói gồm đúng những shop đã kín khung từ gói trước (app vừa khởi động lại nên mất tiến
        // độ); (2) một shop kín khung + một lượt `vePicker` hỏng cắt gói ở đó; (3) Shopee mở khung ngày hôm sau
        // muộn, gói sáng sớm thấy chưa đỏ. Giá của MỘT lần sai-dương là mất trắng cả ngày flash; còn cái mà cửa
        // ấy tiết kiệm — vài gói thừa cho một ngày Shopee đã đóng thật — đã có giãn cách 30 phút và trần
        // `TranGoiThuMoiNgayFlash` gói/ngày lo, và mỗi gói thừa chỉ tốn một lượt SSO chứ không tạo gì.
        if (kq.NgayMaiKhongDo)
        {
            _services.Log.Append(email,
                $"Flash Sale: MỌI shop của gói này đều báo ngày đích {ngayDich:dd-MM-yyyy} KHÔNG còn đỏ và không "
                + "shop nào tạo được gì — có thể các shop đó đã kín khung, cũng có thể Shopee chưa mở / đã đóng "
                + "đăng ký cho ngày đó (từ một gói thì KHÔNG phân biệt được). VẪN mở gói sau theo giãn cách bình "
                + "thường. ĐÂY KHÔNG PHẢI LỖI.");
        }
        return new KetLuanLatCat(
            CauTongKetKm(kq, _dsTkDaDung.Count > 0, LoaiChuongTrinh.FlashSale),
            CauTrangThaiKm(kq, LoaiChuongTrinh.FlashSale));
    }

    /// <summary>
    /// Mốc mà <see cref="TraHoSo"/> phải HOÃN lượt chạy lại check đơn tới — <c>null</c> = chạy lại ngay như
    /// thường. Chỉ <see cref="NhipLichFlashAsync"/> đặt (bằng CUỐI KHE nghỉ của gói), và <see cref="TraHoSo"/>
    /// XOÁ ngay khi dùng (một lượt, một lần: để sót là mọi lượt mượn hồ sơ SAU đó cũng bị hoãn theo, không vì
    /// cái gì).
    /// <para><b>Đặt cho MỌI gói, không riêng ca "ngày không đỏ"</b> (01/09/2026): gói chiếm khe nghỉ của check
    /// đơn, nên xong sớm thì phải trả lại đúng PHẦN CÒN LẠI của khe — có thế nhịp vòng check đơn mới giữ nguyên
    /// y như không có flash. Gói chạy lố qua cuối khe thì mốc đã ở quá khứ và <see cref="TraHoSo"/> chạy lại
    /// ngay, đúng như cũ.</para>
    /// <para><b>⚠ Có ĐƯỜNG THỨ HAI xoá mốc này</b> (sửa 01/09/2026): <see cref="ChayLatCatAsync"/> xoá nó trong
    /// <c>finally</c> khi gói flash KHÔNG mượn được hồ sơ. Mốc được đặt trước cả lượt xin khung xuyên máy, nên
    /// một gói bị từ chối khoá sẽ để lại mốc và bắt vòng check đơn nằm chờ tới cuối khe cho một gói chưa hề
    /// chạy.</para>
    /// </summary>
    internal DateTime? MocChayLaiCheckDonSauFlash { get; private set; }

    /// <summary>
    /// <b>MỐC mà một lượt hoãn chạy lại check đơn ĐANG NẰM CHỜ tới</b> — <c>null</c> = không có lượt hoãn nào
    /// treo. Đặt ở <see cref="TraHoSo"/> ngay khi phóng lượt hoãn; XOÁ ở cả ba lối ra của lượt đó (chạy · bị
    /// huỷ trong lúc chờ · <see cref="HuyLuotHoanChayLaiCheckDon"/>).
    ///
    /// <para><b>Vì sao KHÔNG dùng lại <see cref="MocChayLaiCheckDonSauFlash"/></b>: mốc kia bị
    /// <see cref="TraHoSo"/> XOÁ NGAY khi lên lịch (một lượt, một lần — xem xmldoc ở đó), nên đúng khoảng thời
    /// gian mà nhịp cần biết "có ai đang chờ không" thì nó đã là <c>null</c>.</para>
    ///
    /// <para><b>Ai đọc, và ca hỏng nếu thiếu</b> (03/09/2026, sổ P-20260903-1): <see cref="NhipLichFlashAsync"/>
    /// lấy <c>min(bayGio + chu kỳ, mốc này)</c> làm KHE GIẢ ĐỊNH khi không có phiên nào chạy. Không có nó, nhịp
    /// lấy trọn chu kỳ 120' làm khe và MỞ GÓI ngay trên đầu một lượt hoãn còn 3 phút nữa là tới — đo thật
    /// 18:50:09 ngày 03/09: gói 3 mở lúc mốc hoãn 18:52 còn 2'50", tới 18:52:59 lượt hoãn mở phiên check đơn
    /// ⇒ <c>HttpListenerException</c> cổng 47821 ⇒ lượt dọn hồ sơ đóng 10 cửa sổ Brave của gói ⇒ shop đang dở
    /// HỎNG. Lặp y hệt 21:25 và 22:01, và vòng check đơn 22:01 cũng hỏng theo.</para>
    ///
    /// <para><b>Không đẩy mốc muộn hơn khi mở được gói:</b> khe giả định lúc đó BẰNG mốc này, nên
    /// <c>MocChayLaiCheckDonSauFlash = cuoiKhe</c> chỉ đặt lại đúng con số đang chờ. Lượt hoãn THỨ HAI cho cùng
    /// tài khoản là no-op nhờ <c>_dsTkDangHoanChayLai.Remove</c> trả <c>false</c> — giữ nguyên hành vi đó.</para>
    ///
    /// <para><b>MỘT MỐC MỖI TÀI KHOẢN, và phép đọc lấy mốc SỚM NHẤT</b> (sửa vòng 2, 03/09/2026). Bản đầu giữ
    /// đúng MỘT <c>DateTime?</c> dùng chung: hai tài khoản cùng hoãn thì tài khoản sau ghi đè mốc của tài khoản
    /// trước, và lượt xoá của một trong hai xoá luôn cả mốc của bên kia ⇒ nhịp lại mở gói đè lên lượt hoãn còn
    /// sống. Cầu nối là TÀI NGUYÊN CHUNG của cả máy (một hồ sơ Brave, một cổng 47821), nên mốc của BẤT KỲ tài
    /// khoản nào cũng chặn được gói của mọi tài khoản — vì thế phép đọc là <c>Min</c>, không phải "mốc của tài
    /// khoản đang xét".</para>
    /// </summary>
    internal DateTime? MocHoanChayLaiDangCho
    {
        get
        {
            lock (_mocHoanChayLaiDangCho)
            {
                return _mocHoanChayLaiDangCho.Count == 0
                    ? null
                    : _mocHoanChayLaiDangCho.Values.Min();
            }
        }
    }

    /// <summary>Mốc đang chờ của TỪNG tài khoản — xem <see cref="MocHoanChayLaiDangCho"/>.
    /// <para><b>Có <c>lock</c>, khác <c>_dsTkDangHoanChayLai</c>:</b> ở đây phép đọc DUYỆT <c>Values</c> để lấy
    /// <c>Min</c>, mà lượt ghi đến từ thread của task hoãn (fire-and-forget, continuation trên thread pool).
    /// Duyệt trong lúc bên kia <c>Add</c>/<c>Remove</c> là <c>InvalidOperationException</c> ném ra giữa nhịp
    /// flash — một ca đua hiếm nhưng có thật, và nó giết luôn nhịp.</para></summary>
    private readonly Dictionary<long, DateTime> _mocHoanChayLaiDangCho = new();

    /// <summary>Ghi/xoá mốc chờ của MỘT tài khoản — <c>null</c> = xoá. Mọi lối vào/ra của lượt hoãn đi qua đây,
    /// không ai chạm thẳng vào từ điển.</summary>
    private void DatMocHoanDangCho(long accountId, DateTime? moc)
    {
        lock (_mocHoanChayLaiDangCho)
        {
            if (moc is DateTime m) { _mocHoanChayLaiDangCho[accountId] = m; }
            else { _mocHoanChayLaiDangCho.Remove(accountId); }
        }
    }

    /// <summary>XOÁ SẠCH mọi mốc chờ — chỉ dùng cho lượt huỷ TOÀN BỘ
    /// (<see cref="HuyLuotHoanChayLaiCheckDon"/>, vốn cũng <c>Clear</c> cả <c>_dsTkDangHoanChayLai</c>).</summary>
    private void XoaMoiMocHoanDangCho()
    {
        lock (_mocHoanChayLaiDangCho) { _mocHoanChayLaiDangCho.Clear(); }
    }

    /// <summary>CHỖ NỐI TEST: đặt mốc chờ của <paramref name="accountId"/> như thể một lượt hoãn đang chờ tới
    /// mốc đó (<c>null</c> = xoá).
    /// <para>Đường thật là <see cref="TraHoSo"/>, nhưng bài test đo NHỊP cần dựng riêng trạng thái ấy mà không
    /// phải kéo theo cả một lượt trả hồ sơ + một task hoãn đang sống.</para></summary>
    internal void DatMocHoanDangChoChoTest(long accountId, DateTime? m) => DatMocHoanDangCho(accountId, m);

    /// <summary>Đồng hồ của nhánh HOÃN chạy lại — chỗ nối test (production đọc <c>DateTime.Now</c>).
    /// <para><b>Vì sao phải tiêm</b> (30/08/2026): các test <c>FlashHoanChayLaiTests</c> ghim mốc lịch sử
    /// (29/08 21:0x) mà <see cref="TraHoSo"/> so với đồng hồ THẬT — qua 22:00 đêm đó là nhánh hoãn không bao
    /// giờ vào nữa và cả lớp test tự đỏ (bom hẹn giờ, không phải hồi quy). Test đông lạnh đồng hồ qua đây.</para></summary>
    internal Func<DateTime> DongHoHoanChayLai { get; set; } = () => DateTime.Now;

    /// <summary>Chỗ nối CHỜ trước khi chạy lại check đơn — production là <c>Task.Delay</c>; test rót bản trả về
    /// ngay để đo được luật mà không phải chờ thật một tiếng.
    /// <para><b>PHẢI nhận <see cref="System.Threading.CancellationToken"/></b>: không có nó thì cú "Dừng tất cả"
    /// không cắt được lượt chờ 55 phút, và app tự mở Brave sau khi đã được bảo dừng.</para></summary>
    internal Func<TimeSpan, System.Threading.CancellationToken, Task> ChoTruocKhiChayLaiCheckDon { get; set; }
        = (t, ct) => Task.Delay(t, ct);

    // ══════════ LƯỢT HOÃN KHÔNG ĐÈ LÊN GÓI FLASH ĐANG CHẠY (03/09/2026, sổ P-20260903-1) ══════════

    /// <summary>
    /// <b>Cờ "gói flash / lát cắt cầu nối đang chạy hãy THÔI bắt đầu shop mới — nhường chỗ cho check đơn".</b>
    /// Bật bởi <see cref="ChayLaiCheckDonSauKhiChoAsync"/> khi lượt hoãn tới mốc mà gói còn đang chạy; đọc bởi
    /// vòng shop dùng chung qua tham số <c>phaiNhuongSauShop</c>.
    ///
    /// <para><b><c>volatile</c> là bắt buộc:</b> ghi từ thread của lượt hoãn (một task fire-and-forget), đọc từ
    /// thread đang chạy vòng shop. Không có nó thì JIT được phép giữ giá trị trong thanh ghi suốt vòng lặp và
    /// gói sẽ KHÔNG BAO GIỜ thấy cú bật — mà cả cơ chế này sinh ra chỉ để phục vụ đúng cú bật đó.</para>
    ///
    /// <para><b>RESET về <c>false</c> ngay TRƯỚC MỖI lượt/gói</b> (nhịp lẫn đường tay). Quên reset là gói SAU
    /// dừng ngay sau shop 1 với câu "nhường chỗ cho check đơn" trong khi chẳng có ai chờ — và triệu chứng là
    /// "flash chỉ làm được 1 shop mỗi gói", không lỗi, không dòng nào nói ra.</para>
    /// </summary>
    private volatile bool _nhuongChoCheckDon;

    /// <summary>Cờ nhường đang bật hay không — chỗ đo của test (mệnh đề "đã bật cờ" phải khoá được RIÊNG, tách
    /// khỏi mệnh đề "chưa mở check đơn").</summary>
    internal bool NhuongChoCheckDon => _nhuongChoCheckDon;

    /// <summary>CHỖ NỐI TEST: HẠ cờ nhường y như một lượt/gói MỚI vừa reset nó ngay trước khi chạy.
    /// <para>Bốn chỗ reset thật (nhịp flash · flash bấm tay · KM · Combo) đã bị
    /// <c>FlashGoiKheNghiTests.CoNhuong_DuocRESET_TruocMoiLuot_*</c> khoá trên NGUỒN; hook này chỉ để bài đo
    /// ĐUA dựng đúng khoảnh khắc "cờ vừa bị một lượt khác reset trong lúc mình đang nhường" mà không phải phóng
    /// một lát cắt thật (lát cắt thật mở Brave).</para></summary>
    internal void HaCoNhuongChoTest() => _nhuongChoCheckDon = false;

    /// <summary>
    /// <b>TÊN VIỆC của lát cắt cầu nối đang chạy</b> (<c>"Flash Sale"</c> · <c>"KM của Shop"</c> ·
    /// <c>"Mở list shop"</c> …) — <c>null</c> = không lát cắt nào đang chạy. Đặt/xoá bởi
    /// <see cref="ChayLatCatAsync"/>, cùng cặp với <c>_bridgeRunning</c>.
    ///
    /// <para><b>Sinh ra cho câu NHƯỜNG CHỖ</b> (phản biện vòng 2, 03/09/2026): cửa
    /// <see cref="DangChayFlashHoacCauNoi"/> gồm cả <c>_bridgeRunning</c>, nên lượt hoãn có thể đang nhường cho
    /// một vòng KM tự động chứ không phải gói flash — mà bản đầu nói cứng "Gói Flash Sale đang chạy". Nhật ký
    /// khai một thứ không tồn tại thì người đọc thôi tin nó, và đây đúng là dòng duy nhất giải thích vì sao
    /// check đơn muộn 40 phút.</para>
    /// </summary>
    internal string? TenLatCatDangChay { get; private set; }

    /// <summary>
    /// <b>TÀI KHOẢN mà lát cắt cầu nối đang chạy THUỘC VỀ</b> — <c>null</c> = không lát cắt nào đang chạy.
    /// Đặt/xoá bởi <see cref="ChayLatCatAsync"/>, cùng cặp với <c>_bridgeRunning</c>/<see cref="TenLatCatDangChay"/>.
    /// <para><b>Sinh ra cho lệnh hub <c>stop-all</c> PER-TÀI-KHOẢN</b> (người dùng chốt 04/09/2026): "Dừng" từ
    /// hub chỉ được cắt lát cắt cầu nối khi nó thuộc ĐÚNG tài khoản bị dừng — máy có thể đang chạy một lát cắt
    /// của tài khoản KHÁC, và cú Dừng của tài khoản này không được đụng tới nó. <c>_pocAccountId</c> KHÔNG dùng
    /// được: nó chỉ được gán SAU khi mượn hồ sơ xong và bị <c>TryKillPoc</c> xoá về null giữa chừng.</para>
    /// </summary>
    internal long? TaiKhoanLatCatDangChay { get; private set; }

    /// <summary>Thân của hook <see cref="AppServices.LatCatDangChayCua"/> (rót trong ctor) — đọc BA trường thật
    /// rồi giao cho <see cref="LatCatCua"/> quyết.</summary>
    private string? LatCatDangChayCua(long accountId)
        => LatCatCua(_bridgeRunning, TaiKhoanLatCatDangChay, TenLatCatDangChay, accountId);

    /// <summary>(THUẦN) Tên lát cắt mà máy đang chạy CHO ĐÚNG <paramref name="accountId"/> — <c>null</c> = không
    /// có, hoặc lát cắt đang chạy thuộc tài khoản KHÁC.
    /// <para>Vế <paramref name="taiKhoanLatCat"/> là vế bắt buộc: một máy chạy Flash Sale cho tài khoản A không
    /// làm tài khoản B "đang chạy" — hub sẽ hiện "✖ Dừng" ở dòng B và cú bấm đó đi dừng nhầm chỗ.</para>
    /// <para>Vế <paramref name="bridgeRunning"/> cũng bắt buộc: hai trường tên/tài-khoản bị xoá ở <c>finally</c>
    /// NGAY SAU cờ cầu nối, nên chỉ đọc chúng là còn một khe báo "đang chạy" sau khi lát cắt đã tan.</para></summary>
    internal static string? LatCatCua(bool bridgeRunning, long? taiKhoanLatCat, string? ten, long accountId)
        => bridgeRunning && taiKhoanLatCat == accountId && !string.IsNullOrWhiteSpace(ten) ? ten : null;

    /// <summary>CHỖ NỐI TEST: dựng <see cref="TenLatCatDangChay"/> (và <see cref="TaiKhoanLatCatDangChay"/> nếu
    /// truyền) mà không phải chạy một lát cắt thật (lát cắt thật phóng Brave).</summary>
    internal void DatTenLatCatChoTest(string? ten, long? taiKhoan = null, bool giuCauNoi = false)
    {
        TenLatCatDangChay = ten;
        TaiKhoanLatCatDangChay = taiKhoan;
        // `giuCauNoi`: dựng luôn `_bridgeRunning` để đo các nhánh đọc cờ thật (bước (1)/(4) của `stop-all`).
        _bridgeRunning = giuCauNoi;
    }

    /// <summary>CHỖ NỐI TEST: dựng "cửa sổ Mở list shop của <paramref name="accountId"/> còn để lại" cùng token
    /// của lát cắt đang chạy (<paramref name="cts"/>) — để đo bước (4) của <c>stop-all</c> có huỷ nhầm token của
    /// lát cắt tài khoản khác không (VỪA-C, 04/09/2026).</summary>
    internal void DatCuaSoXemChoTest(long? accountId, System.Threading.CancellationTokenSource? cts)
    {
        _pocAccountId = accountId;
        _bridgeCts = cts;
    }

    /// <summary>Chỗ nối "gói flash HOẶC một lát cắt cầu nối có đang chạy không" — production đọc hai cờ thật.
    /// <para><b>Áp cho CẢ lượt tay</b> (<c>_bridgeRunning</c>): người dùng ngồi trước máy, nhưng luật "không đè
    /// lên một lượt đang mở Brave" không có ngoại lệ — hai luồng tranh cùng hồ sơ Brave và cùng cổng 47821 thì
    /// hỏng y như nhau, bất kể ai khởi động lượt kia.</para></summary>
    /// <remarks>Dựng LƯỜI ở getter (không phải field initializer): thân lambda đọc hai trường instance, mà khởi
    /// tạo trường thì không được chạm tới thành viên instance (CS0236) — cùng khuôn
    /// <see cref="ChayLaiPhienCheckDon"/>.</remarks>
    internal Func<bool> DangChayFlashHoacCauNoi
    {
        get => _dangChayFlashHoacCauNoi ??= () => _dangChayFlashTuDong || _bridgeRunning;
        set => _dangChayFlashHoacCauNoi = value;
    }

    private Func<bool>? _dangChayFlashHoacCauNoi;

    /// <summary>Một NHỊP CHỜ của vòng "nhường chỗ" — production ngủ 10 giây; test rót bản trả về ngay (hoặc bản
    /// đếm nhịp) để đo được cả vế QUÁ TRẦN mà không phải chờ thật 90 phút.
    /// <para><b>PHẢI nhận <see cref="System.Threading.CancellationToken"/></b>, cùng lý do với
    /// <see cref="ChoTruocKhiChayLaiCheckDon"/>: cú "Dừng tất cả" giữa lúc đang nhường phải cắt được ngay.</para></summary>
    internal Func<System.Threading.CancellationToken, Task> ChoNhipNhuongFlash { get; set; }
        = ct => Task.Delay(TimeSpan.FromSeconds(10), ct);

    // ══════════════════════ TIẾN ĐỘ FLASH TRONG NGÀY — GIỮ TRONG BỘ NHỚ, KHÔNG XUỐNG DB ══════════════════════
    //
    // Lý do đầy đủ ở xmldoc `ChotLuotFlash`. Tóm tắt: hồ sơ KM đồng bộ xuyên máy, nên một con trỏ tiến độ trong
    // DB sẽ trôi lệch giữa các máy và không ai biết bản nào đúng. Nguồn sự thật là SHOPEE — mở lại một shop đã
    // xong chỉ tốn một lượt render rồi shop tự khai `BoQua`, không tạo trùng.

    /// <summary>(tài khoản, NGÀY ĐÍCH) → tập KHÓA SHOP đã XONG cho ngày đó. Chỉ sống trong phiên app này.</summary>
    private readonly Dictionary<(long AccountId, DateOnly NgayDich), HashSet<string>> _flashTienDo = new();

    /// <summary>
    /// (tài khoản, NGÀY ĐÍCH) → SỐ SHOP LỚN NHẤT từng đọc được từ picker trong ngày đó.
    /// <para><b>Lưới chống ghi mốc "ngày đã trọn" OAN</b> (phản biện vòng 1, 01/09/2026): mỗi gói chạy trên
    /// danh sách ĐÃ BỊ <c>boQuaShop</c> lọc, nên "mọi shop của lượt đều xong" không nói được gì về cả ngày. Một
    /// lượt picker render thiếu (5/12 shop — đã xảy ra thật) sẽ khai TRỌN cho một ngày còn 7 shop chưa ai đụng
    /// tới, rồi <c>DenGioGoiFlash</c> khoá sạch phần còn lại của ngày. Lấy MAX chứ không lấy lượt gần nhất:
    /// thiếu là bệnh của một lượt đọc, còn số shop thật thì chỉ tăng.</para>
    /// </summary>
    private readonly Dictionary<(long AccountId, DateOnly NgayDich), int> _flashSoShopTungThay = new();

    /// <summary>
    /// TÀI KHOẢN → số GÓI flash đã MỞ trong ngày lịch <see cref="_ngayDemGoiFlash"/>; trần
    /// <see cref="KmLichTuDong.TranGoiThuMoiNgayFlash"/>.
    /// <para><b>Đếm THEO TỪNG TÀI KHOẢN, không phải một sổ chung cho cả máy</b> (sửa 01/09/2026, phản biện vòng
    /// 1). Sổ chung nghe có lý — "chi phí cần chặn là số lần phóng Brave trên máy này" — nhưng nó BỎ ĐÓI tài
    /// khoản đứng sau: nhịp duyệt danh sách theo thứ tự cố định và mở gói cho tài khoản đầu tiên đủ điều kiện,
    /// nên A tiêu hết trần rồi B không còn gói nào, MỌI ngày, không một dòng log nào nói ra. Trần đã nâng lên 10
    /// đúng vì nó nay là trần của một tài khoản.</para>
    /// </summary>
    private readonly Dictionary<long, int> _soGoiDaMoTrongNgay = new();

    /// <summary>Ngày lịch mà <see cref="_soGoiDaMoTrongNgay"/> đang đếm cho; đổi ngày ⇒ đếm lại từ 0.</summary>
    private DateOnly _ngayDemGoiFlash;

    /// <summary>Tập shop ĐÃ XONG của (tài khoản, ngày đích) — tạo rỗng nếu chưa có. So khóa KHÔNG phân biệt
    /// hoa/thường: khóa SQL so BINARY còn C# so Ordinal, cùng cạm bẫy đã ghi ở
    /// <c>OrderPersistPipeline.GoBannerLoiDiaChi</c>.</summary>
    private HashSet<string> TapShopXongFlash(long accountId, DateOnly ngayDich)
    {
        var khoa = (accountId, ngayDich);
        if (!_flashTienDo.TryGetValue(khoa, out var tap))
        {
            tap = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _flashTienDo[khoa] = tap;
        }
        return tap;
    }

    /// <summary>KHÓA tiến độ của một shop trong kết quả lượt — <c>ShopLogin</c> (đã là
    /// <c>OrdersBridgeSession.KhoaShop</c>), trim; rỗng nếu không có.
    /// <para><b>⚠ ĐỪNG lấy <c>ShopName</c> thay thế.</b> Vòng chạy lọc bằng khóa login, nên shop có tên hiển thị
    /// khác login sẽ không bao giờ khớp ⇒ gói sau mở lại đúng shop vừa xong, câm lặng.</para></summary>
    private static string KhoaTienDo(KetQuaKmMotShop s) => (s.ShopLogin ?? string.Empty).Trim();

    /// <summary>
    /// PURE — shop này đã XONG cho ngày đích chưa: <c>DaTao</c>, hoặc <c>BoQua</c> vì một lý do THẬT (hết khung ·
    /// không có sản phẩm).
    /// <para><b>⚠ <c>NgayMaiKhongDo</c> KHÔNG tính là xong</b> dù nó cũng đi kèm một <c>BoQua</c> — xem khối lý
    /// do ở xmldoc <see cref="ChotLuotFlash"/>. Gộp nó vào là shop bị gạt khỏi mọi gói sau của ngày, câm lặng.</para>
    /// </summary>
    private static bool ShopDaXongFlash(KetQuaKmMotShop s)
        => s.DaTao || (s.BoQua is not null && !s.NgayMaiKhongDo);

    /// <summary>CHỖ NỐI TEST: tập shop đã XONG của (tài khoản, ngày đích) — <b>chỉ ĐỌC</b>. Tiến độ là trạng
    /// thái trong bộ nhớ, không xuống DB, nên đây là cửa duy nhất đo được nó.</summary>
    internal IReadOnlyCollection<string> ShopFlashDaXong(long accountId, DateTime ngayDich)
        => TapShopXongFlash(accountId, DateOnly.FromDateTime(ngayDich));

    /// <summary>
    /// GHI số shop vừa đọc được từ picker cho (tài khoản, ngày đích) — chỉ giữ lại số LỚN NHẤT của ngày.
    /// <para>Gọi từ callback đọc danh sách shop của lượt FLASH (<see cref="ChayLatCatAsync"/>), tức TRƯỚC khi bộ
    /// lọc <c>boQuaShop</c> cắt bớt — đó chính là điều làm nó dùng được làm mẫu số. Cũng là chỗ nối TEST duy
    /// nhất cho lưới này: các bài test nhịp không bao giờ đi tới được picker.</para>
    /// </summary>
    internal void GhiSoShopThayFlash(long accountId, DateTime ngayDich, int soShop)
    {
        if (soShop <= 0) { return; }
        var khoa = (accountId, DateOnly.FromDateTime(ngayDich));
        if (!_flashSoShopTungThay.TryGetValue(khoa, out var cu) || soShop > cu)
        {
            _flashSoShopTungThay[khoa] = soShop;
        }
    }

    /// <summary>Số shop LỚN NHẤT từng thấy trong ngày đích đó; <c>0</c> = chưa lượt nào đọc được picker.</summary>
    private int SoShopTungThayFlash(long accountId, DateOnly ngayDich)
        => _flashSoShopTungThay.TryGetValue((accountId, ngayDich), out var n) ? n : 0;

    /// <summary>CHỖ NỐI TEST: số gói đã mở trong ngày lịch đang đếm, của MỘT tài khoản.</summary>
    internal int SoGoiFlashDaMoTrongNgay(long accountId)
        => _soGoiDaMoTrongNgay.TryGetValue(accountId, out var n) ? n : 0;

    /// <summary>
    /// CHỖ NỐI TEST: đặt thẳng bộ đếm gói của MỘT TÀI KHOẢN trong một ngày lịch.
    /// <para><b>Vì sao cần</b>: mở một gói THẬT trong test tốn ~17 giây (lượt chạy đi tới cửa khoá xuyên máy rồi
    /// mới quay ra), nên dựng trạng thái "đã chạm trần" bằng ngần ấy lượt thật là nhiều phút cho MỘT bài. Bài
    /// test vẫn phải đo riêng việc bộ đếm CÓ TĂNG — xem <see cref="SoGoiFlashDaMoTrongNgay"/>.</para>
    /// </summary>
    internal void DatSoGoiFlashDaMoChoTest(long accountId, DateTime ngay, int soGoi)
    {
        _ngayDemGoiFlash = DateOnly.FromDateTime(ngay);
        _soGoiDaMoTrongNgay[accountId] = soGoi;
    }

    /// <summary>Đếm lại số gói khi sang ngày lịch mới. Gọi ở ĐẦU mỗi nhịp — app chạy liên tục qua nửa đêm là
    /// chuyện thường, và không có lượt reset thì trần gói/ngày biến thành trần gói/lần-mở-app.</summary>
    private void DoiNgayThiDemLaiGoiFlash(DateTime bayGio)
    {
        var homNay = DateOnly.FromDateTime(bayGio);
        if (_ngayDemGoiFlash == homNay) { return; }
        _ngayDemGoiFlash = homNay;
        _soGoiDaMoTrongNgay.Clear();

        // DỌN tiến độ của những ngày đích đã qua. Máy này chạy liên tục hàng tháng, mà mỗi ngày để lại một
        // mục cho mỗi tài khoản — không dọn thì hai từ điển cứ phình mãi cho một dữ liệu không ai đọc nữa.
        // Ngưỡng là ngày đích của HÔM NAY: mọi thứ cũ hơn thuộc về những ngày đã trôi qua.
        var nguong = DateOnly.FromDateTime(KmLichTuDong.NgayDichFlash(bayGio));
        foreach (var khoa in _flashTienDo.Keys.Where(k => k.NgayDich < nguong).ToList())
        {
            _flashTienDo.Remove(khoa);
        }
        foreach (var khoa in _flashSoShopTungThay.Keys.Where(k => k.NgayDich < nguong).ToList())
        {
            _flashSoShopTungThay.Remove(khoa);
        }
    }

    // ══════════════════ LỊCH FLASH SALE TỰ ĐỘNG — GÓI NHỎ XEN KHE NGHỈ, CẢ NGÀY (01/09/2026) ══════════════════

    /// <summary>Cờ chặn lượt FLASH tự động thứ hai chồng lên lượt đang chạy — xem <see cref="NhipLichFlashAsync"/>.</summary>
    private bool _dangChayFlashTuDong;

    /// <summary>Tài khoản → NGÀY đã ghi dòng "không phải chủ trì" của vòng FLASH. Map RIÊNG với hai vòng kia:
    /// dùng chung thì dòng của vòng KM lúc 2h sáng nuốt mất dòng của vòng flash lúc 21h CÙNG NGÀY, và người
    /// dùng không bao giờ biết flash cũng đang đứng ngoài.</summary>
    private readonly Dictionary<long, DateTime> _daBaoChuTriFlash = new();

    /// <summary>
    /// <b>Một nhịp của LỊCH FLASH SALE TỰ ĐỘNG</b> (gọi từ CÙNG đồng hồ 60 giây với hai nhịp kia, nối đuôi
    /// trong <c>ChayNhipLichKmAsync</c>). Cấu trúc song song với <see cref="NhipLichComboAsync"/> — cùng ba
    /// cửa: đang chạy lát cắt · tới hạn · máy chủ trì.
    ///
    /// <para><b>KHÁC hai nhịp kia đúng hai chỗ, và cả hai đều CÓ CHỦ ĐÍCH:</b></para>
    /// <list type="number">
    /// <item><b>KHÔNG có cửa "cấu hình dùng được"</b> — vòng này không có thông số nào để sai. Không tên, không
    /// khoảng ngày, không mức giảm, không giờ bắt đầu: ngày đích suy từ chính ngày chạy
    /// (<see cref="KmLichTuDong.NgayDichFlash"/>) và khung giờ do Shopee phát hành. Dựng thêm một cửa kiểm rỗng
    /// ở đây chỉ là một chỗ nữa để trôi lệch.</item>
    /// <item><b>Chu kỳ CỨNG 1 ngày</b> (<see cref="KmLichTuDong.SoNgayChuKy"/>), không đọc <c>cf.SoNgay</c> —
    /// bỏ một đêm là mất trắng ngày đó, không lượt nào bù được.</item>
    /// </list>
    ///
    /// <para><b>⚠ MỐC THỬ GHI TRƯỚC KHI CHẠY — đây là chốt ĐẮT NHẤT của cả nhịp này.</b> Khung nay là CẢ NGÀY
    /// và nhịp đồng hồ là 60 giây; cửa chủ trì và mọi lối ra hỏng đều KHÔNG ghi mốc ngày, nên thiếu
    /// <c>FlashLanThuTuDong</c> thì vế GIÃN CÁCH của <see cref="KmLichTuDong.DenGioGoiFlash"/> không có gì để
    /// đo ⇒ <b>mở gói mới MỖI PHÚT</b>, mỗi gói một lượt phóng Brave + SSO. Ghi SAU khi chạy cũng hỏng: đúng ca
    /// cần chặn nhất — lượt ném giữa chừng — lại là ca không bao giờ ghi được.</para>
    ///
    /// <para><b>BỐN CỬA MỚI của bản GÓI–KHE NGHỈ (01/09/2026), xếp rẻ→đắt:</b></para>
    /// <list type="number">
    /// <item><b>Trần gói của TÀI KHOẢN ĐÓ</b> — <see cref="KmLichTuDong.TranGoiThuMoiNgayFlash"/> gói mỗi ngày,
    /// đếm riêng từng tài khoản (xem <see cref="_soGoiDaMoTrongNgay"/>).</item>
    /// <item><b>KHÔNG có cửa sổ Brave nào đang để lại</b> (<see cref="CuaSoXemDangMo"/>) — cửa sổ ấy là của một
    /// lượt "Mở list shop" mà người dùng đang thao tác dở, và mở gói sẽ giết nó.</item>
    /// <item><b>MỌI phiên đang hoạt động đều ĐANG NGHỈ</b> (<see cref="IAccountSession.ThoiDiemVongKe"/> khác
    /// null). <b>Đây là thay đổi lớn nhất của đợt</b>: gói KHÔNG BAO GIỜ cắt ngang một vòng check đơn đang chạy
    /// nữa — có phiên đang giữa vòng thì về êm, nhịp sau thử lại.</item>
    /// <item><b>Khe đủ dài</b> — <c>cuoiKhe − bayGio ≥ <see cref="KmLichTuDong.PhutKheToiThieuChoGoi"/></c>. Kỳ
    /// nghỉ NGẮN 15' sau một vòng bị cắt vì thế tự rơi ra ngoài: nó sinh ra để hạ tầng dịu lại.</item>
    /// </list>
    ///
    /// <para><b>⚠ BA CỬA SAU CÙNG PHẢI GHI MỘT DÒNG KHI TỪ CHỐI</b> (phản biện vòng 1): chúng có thể đóng suốt
    /// nhiều giờ liền mà không lối nào ghi gì, và người dùng chỉ thấy "flash bật mà chẳng bao giờ chạy". Dòng ấy
    /// bị TIẾT CHẾ còn tối đa một lần mỗi <see cref="PhutTietCheLogTuChoiGoiFlash"/> phút — nhịp là 60 giây, nên
    /// không tiết chế là 1440 dòng rác một ngày.</para>
    ///
    /// <para><b>⚠ HẠN CHẾ ĐÃ BIẾT, không sửa trong đợt này</b> (phản biện vòng 1 chứng minh bằng số học): với
    /// <b>từ 3 tài khoản trở lên chạy tuần tự</b>, khe nghỉ CHUNG mà mọi phiên cùng rảnh có thể không bao giờ đạt
    /// <see cref="KmLichTuDong.PhutKheToiThieuChoGoi"/> phút — mỗi phiên nghỉ lệch pha nhau, và cửa "mọi phiên
    /// đều đang nghỉ" thì đo phần GIAO. Lúc đó vòng flash im lặng cả ngày, và dòng log tiết chế ở trên là thứ
    /// duy nhất nói ra. Chấp nhận vì cấu hình thật hiện nay là MỘT tài khoản mỗi máy.</para>
    ///
    /// <para><b>Gói ĐÀN HỒI theo khe, không theo số shop</b> (người dùng hỏi thẳng "sao không chạy 1–2 shop một
    /// lần"): hạn gói = <c>min(cuối khe − 45', 22:15)</c> rót xuống làm <c>hanChot</c>, rồi cơ chế
    /// <c>ConDuocBatDauShopMoi</c> kiểm-trước-MỖI-shop sẵn có tự quyết gói này làm được mấy shop. Khe 120' tự ra
    /// ~2 shop mà không hằng nào phải nói ra con số 2.</para>
    /// </summary>
    internal async Task NhipLichFlashAsync(DateTime bayGio)
    {
        if (_dangChayFlashTuDong || _dangChayComboTuDong || _dangChayKmTuDong || _bridgeRunning) { return; }

        DoiNgayThiDemLaiGoiFlash(bayGio);

        var mayNay = _services.MayNay?.Invoke() ?? "";

        foreach (var acc in _services.Accounts.GetAll())
        {
            var cf = _services.KmConfig.Get(acc.Id);
            if (!KmLichTuDong.DenGioGoiFlash(cf, bayGio)) { continue; }

            // TRẦN GÓI CỦA CHÍNH TÀI KHOẢN NÀY — `continue`, KHÔNG `return`: tài khoản kia có thể còn quota.
            if (SoGoiFlashDaMoTrongNgay(acc.Id) >= KmLichTuDong.TranGoiThuMoiNgayFlash) { continue; }

            var ngayDich = KmLichTuDong.NgayDichFlash(bayGio);

            if (!KmLichTuDong.LaMayChuTri(cf.OwnerMachine, mayNay))
            {
                // KHÔNG ghi mốc thử: lượt ấy không phải của mình — ghi vào là ĐỐT lượt của đúng máy chủ trì,
                // và với vòng flash thì máy đó mất trắng NGÀY MAI chứ không phải "chờ chu kỳ sau".
                BaoKhongPhaiChuTriFlash(acc.Id, acc.Email, cf.OwnerMachine, bayGio);
                continue;
            }

            // ══ CỬA SỔ BRAVE ĐANG ĐỂ LẠI ⇒ VỀ ÊM ══
            // `CuaSoXemDangMo` = một lượt "Mở list shop" còn để cửa sổ cho người dùng thao tác tay, và hồ sơ
            // Brave vẫn đang được MƯỢN (chưa `TraHoSo`). Mở gói lúc này là `DongCuaSoCuLai` giết mất cửa sổ ấy
            // ngay dưới tay người đang dùng. `_bridgeRunning` ở đầu hàm KHÔNG bắt được ca này: lượt mở list shop
            // đã kết thúc từ lâu, chỉ có cửa sổ là còn sống.
            if (CuaSoXemDangMo)
            {
                BaoTuChoiGoiFlash(acc.Email, bayGio,
                    "một cửa sổ trình duyệt của lượt 'Mở list shop' vẫn đang mở (hồ sơ Brave đang được mượn) — "
                    + "đóng cửa sổ đó thì gói flash mới chen vào được");
                return;
            }

            // ══ KHE NGHỈ: có mở gói được không, và khe kéo dài tới đâu ══
            // Phiên nào đang giữa vòng (`ThoiDiemVongKe == null`) ⇒ VỀ ÊM. Không cắt vòng dở nữa: cắt ngang là
            // vòng check đơn bắt đầu lại từ shop 1 ở lượt sau, tức shop 1–2 bị mở hai lần trong ít phút — đúng
            // mẫu đã làm Shopee bật verify/traffic.
            // ⚠ BỎ PHIÊN ĐANG XẾP HÀNG (`Queued`) RA KHỎI PHÉP ĐO — và đây KHÔNG phải chuyện làm cho gọn.
            // `Sessions.Active` cố ý gồm cả `Queued` (để nút Chạy khoá / nút Dừng còn bật), mà chỉ MỘT phiên
            // được chiếm slot cầu nối tại một thời điểm: tick 3 tài khoản là 1 phiên chạy + 2 phiên nằm hàng
            // đợi tới khi phiên kia dừng hẳn — mà vòng check đơn thì không bao giờ tự dừng. Phiên xếp hàng có
            // `ThoiDiemVongKe == null` (nó chưa chạy vòng nào), nên tính cả chúng vào là cửa dưới LUÔN đóng ⇒
            // **vòng flash chết câm ở mọi máy có từ 2 tài khoản trở lên**. Phiên xếp hàng cũng không có vòng
            // nào để mà bị cắt ngang, tức nó không thuộc câu hỏi này.
            var dangChay = _services.Sessions.Active.Where(p => p.State != SessionState.Queued).ToList();
            if (dangChay.FirstOrDefault(p => p.ThoiDiemVongKe is null) is { } dangGiuaVong)
            {
                BaoTuChoiGoiFlash(acc.Email, bayGio,
                    $"phiên check đơn của tài khoản #{dangGiuaVong.AccountId} đang GIỮA một vòng "
                    + $"({dangGiuaVong.State}) — gói flash không cắt ngang vòng đang chạy, sẽ thử lại ở nhịp sau");
                return;
            }

            // Không phiên nào đang chạy ⇒ không có khe THẬT để đo; lấy chính chu kỳ check đơn làm khe giả định.
            // Nó đúng theo nghĩa "nếu người dùng bấm Chạy ngay bây giờ thì vòng đầu cũng chiếm chừng đó".
            //
            // ⚠ NHƯNG KHÔNG ĐƯỢC BỎ QUA MỘT LƯỢT HOÃN ĐANG CHỜ (sửa 03/09/2026, sổ P-20260903-1). Trạng thái
            // "không phiên nào chạy" là ĐÚNG trạng thái ngay sau khi một gói flash vừa xong: `TraHoSo` đã dừng
            // hết phiên và đang hẹn mở lại ở mốc `MocHoanChayLaiDangCho`. Lấy trọn chu kỳ 120' làm khe ở đây là
            // mở gói mới đè lên một lượt hoãn còn vài phút nữa là tới — xem xmldoc `MocHoanChayLaiDangCho` cho
            // chuỗi đo thật 18:50→18:52 ngày 03/09. Khe giả định vì thế bị KẸP ở mốc hoãn, và
            // `KheDuChoGoiFlash` (≥75') tự từ chối khi mốc ấy gần.
            var mocHoan = MocHoanChayLaiDangCho;
            var cuoiKhe = dangChay.Count == 0
                ? Min(bayGio.AddMinutes(Math.Max(1, _services.Settings.GetOrderIntervalMinutes())),
                      mocHoan ?? DateTime.MaxValue)
                : dangChay.Min(p => p.ThoiDiemVongKe!.Value);

            // KHUNG ĐÊM 01:00–04:00 (KM & Combo) — Flash Sale TUYỆT ĐỐI KHÔNG CHẠY trong khung này (08/09/2026).
            // Nếu thời điểm hiện tại trước 01:00, khe nghỉ của gói Flash Sale không được vượt quá 01:00 (đầu khung KM/Combo).
            DateTime? mocKmComboDem = null;
            if (bayGio.Hour < KmLichTuDong.GioMoKhung)
            {
                mocKmComboDem = bayGio.Date.AddHours(KmLichTuDong.GioMoKhung);
                cuoiKhe = Min(cuoiKhe, mocKmComboDem.Value);
            }

            if (!KmLichTuDong.KheDuChoGoiFlash(bayGio, cuoiKhe))
            {
                // NÊU ĐÍCH DANH CỬA ĐANG ĐÓNG. Hai cửa cùng ra một con số "khe còn N phút" nhưng cách gỡ khác
                // hẳn nhau: khe ngắn vì chu kỳ thì nới Cài đặt, còn khe ngắn vì một lượt hoãn thì chỉ cần CHỜ —
                // khuyên nới chu kỳ ở ca sau là gửi người dùng đi sửa một thứ không liên quan.
                var viKhungKm = mocKmComboDem is DateTime mKm && cuoiKhe == mKm;
                var viMocHoan = mocHoan is DateTime m && dangChay.Count == 0 && m <= cuoiKhe;
                // MỐC ĐÃ TỚI mà vẫn còn đó = lượt hoãn đang NHƯỜNG (chờ lượt hiện tại nhả cầu nối). Câu "khe còn
                // -47 phút (tới 18:52)… đang chờ tới đó" vừa in số âm vừa nói sai trạng thái — đây lại là dòng duy
                // nhất giải thích vì sao flash im cả tiếng, nên phải nói đúng việc đang xảy ra.
                var dangNhuong = viMocHoan && mocHoan is DateTime m2 && m2 <= bayGio;
                var conPhut = Math.Max(0, (int)Math.Round((cuoiKhe - bayGio).TotalMinutes));
                BaoTuChoiGoiFlash(acc.Email, bayGio,
                    dangNhuong
                        ? $"một lượt chạy lại check đơn đã TỚI MỐC ({mocHoan:HH:mm}) và đang chờ lượt hiện tại nhả "
                          + "cầu nối (không đè) — không mở gói mới cho tới khi vòng check đơn ấy đi qua"
                        : $"khe nghỉ check đơn chỉ còn {conPhut} phút (tới {cuoiKhe:HH:mm}), dưới sàn "
                          + $"{KmLichTuDong.PhutKheToiThieuChoGoi} phút cho một gói — "
                          + (viKhungKm
                              ? $"khe bị chặn ở KHUNG KM & COMBO ĐÊM ({mocKmComboDem:HH:mm}) — nhường toàn bộ khung "
                                + $"{KmLichTuDong.GioMoKhung:00}:00–{KmLichTuDong.GioDongKhung:00}:00 cho KM và Combo, không flash sale"
                              : viMocHoan
                                  ? $"khe bị chặn ở MỐC CHẠY LẠI CHECK ĐƠN đã hẹn {mocHoan:HH:mm} (một lượt hoãn của gói "
                                    + "trước đang chờ tới đó), nên gói kế chỉ mở được sau khi vòng check đơn ấy đi qua"
                                  : "nới 'Khoảng nghỉ giữa hai vòng check đơn' trong Cài đặt nếu muốn flash chạy được"));
                return;
            }

            _dangChayFlashTuDong = true;
            try
            {
                // ĐÁNH DẤU LƯỢT THỬ **TRƯỚC** KHI CHẠY — xem khối cảnh báo ở xmldoc.
                _services.KmConfig.Save(cf with { FlashLanThuTuDong = bayGio });
                _services.KmHoSoDaDoi?.Invoke();
                _soGoiDaMoTrongNgay[acc.Id] = SoGoiFlashDaMoTrongNgay(acc.Id) + 1;

                // MỘT GÓI, MỘT NGUỒN: ngày đích tính ĐÚNG MỘT LẦN ở đây rồi đóng băng trong `ThamSoFlashSale`
                // cho MỌI shop của lượt. Dòng nhật ký dưới đây đọc từ CHÍNH gói đó — bài học lỗ phủ 25/08/2026
                // (dòng log đọc một biến rời thì nó chỉ bảo vệ chính nó, không bảo vệ thứ đi xuống Shopee).
                // %GIẢM + SL THAM GIA đọc TỪ CẤU HÌNH (03/09/2026) — không còn là hằng trong extension. Đọc từ
                // `cf` (bản vừa dùng cho mọi cửa của nhịp này), không đọc lại DB: hai lượt đọc là hai câu trả
                // lời khác nhau khi worker đồng bộ kéo bản mới về giữa chừng.
                var thamSo = new ThamSoFlashSale(ngayDich, cf.FlashPhanTramGiam, cf.FlashSoLuongThamGia);

                // HẠN CỦA GÓI = min(cuối khe − trần một shop, hạn 22:15 của ngày) — hàm thuần, xem xmldoc ở đó.
                var hanChot = KmLichTuDong.HanChotGoiFlash(bayGio, cuoiKhe);

                // TRẢ LẠI PHẦN CÒN LẠI CỦA KHE cho check đơn — đặt cho MỌI gói. Gói xong sớm ⇒ `TraHoSo` chờ
                // tới đúng cuối khe ⇒ nhịp vòng check đơn giữ nguyên y như không có flash. Gói chạy lố ⇒ mốc đã
                // ở quá khứ ⇒ chạy lại ngay.
                MocChayLaiCheckDonSauFlash = cuoiKhe;

                // BẢN CHỤP, không phải chính tập sống: `ChotLuotFlash` sẽ ghi thêm vào tập ấy ngay khi gói xong,
                // và một bộ lọc thay đổi giữa chừng là thứ không ai lần lại nổi khi nó sai.
                var boQuaShop = new HashSet<string>(
                    TapShopXongFlash(acc.Id, DateOnly.FromDateTime(ngayDich)), StringComparer.OrdinalIgnoreCase);
                _services.Log.Append(acc.Email,
                    $"Lịch Flash Sale tự động: MỞ GÓI {SoGoiFlashDaMoTrongNgay(acc.Id)}/"
                    + $"{KmLichTuDong.TranGoiThuMoiNgayFlash} "
                    + $"của ngày (ngày đích đã xong {boQuaShop.Count} shop; ngày đích ghi mốc lần cuối: "
                    + (cf.FlashLanChayCuoi is DateTime c ? c.ToString("dd-MM-yyyy HH:mm") : "chưa lần nào")
                    + $"). Khe nghỉ check đơn tới {cuoiKhe:HH:mm} ⇒ không bắt đầu shop mới sau {hanChot:HH:mm}. "
                    + $"Chương trình: {thamSo.MoTa()}. "
                    + $"Gói kế sớm nhất sau {KmLichTuDong.PhutGianCachGoiFlash} phút nữa.");

                // RESET CỜ NHƯỜNG ngay trước gói — xem `_nhuongChoCheckDon`. Quên reset là gói này dừng ngay
                // sau shop đầu vì một lượt hoãn của gói TRƯỚC đã đi qua từ lâu.
                _nhuongChoCheckDon = false;
                await ChayFlashSaleAsync(acc.Id, acc.Email, thamSo, hanChot, boQuaShop,
                    () => _nhuongChoCheckDon);
            }
            finally
            {
                _dangChayFlashTuDong = false;
            }
            return; // một tài khoản mỗi nhịp
        }
    }

    /// <summary>PURE — mốc SỚM HƠN trong hai mốc. Tách ra vì <c>Math.Min</c> không nhận
    /// <see cref="DateTime"/>, và một biểu thức ba ngôi lồng trong phép tính khe nghỉ là chỗ dễ đọc sai
    /// chiều nhất của cả hàm nhịp.</summary>
    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    /// <summary>
    /// GIÃN CÁCH tối thiểu giữa hai dòng nhật ký "nhịp flash TỪ CHỐI mở gói", tính bằng PHÚT — <c>60</c>.
    /// <para>Nhịp chạy mỗi 60 giây và ba cửa từ chối (cửa sổ để lại · phiên đang giữa vòng · khe quá ngắn) có
    /// thể đóng liên tục hàng giờ, nên không tiết chế là 1440 dòng một ngày — nhật ký đó thì không ai đọc, mà
    /// đúng cái cần đọc lại nằm trong đó.</para>
    /// </summary>
    private const int PhutTietCheLogTuChoiGoiFlash = 60;

    /// <summary>Mốc lần gần nhất đã ghi một dòng "nhịp flash từ chối mở gói". IN-MEMORY, MỘT mốc cho cả máy:
    /// ba cửa từ chối đều là chuyện của MÁY (cửa sổ Brave, phiên check đơn, khe nghỉ) chứ không của riêng tài
    /// khoản nào, nên tách sổ theo tài khoản chỉ nhân số dòng lên mà không thêm thông tin.</summary>
    private DateTime? _mocLogTuChoiGoiFlash;

    /// <summary>
    /// Ghi MỘT dòng "nhịp flash TỚI HẠN nhưng KHÔNG mở gói được, vì …", có TIẾT CHẾ
    /// (<see cref="PhutTietCheLogTuChoiGoiFlash"/>).
    /// <para><b>Vì sao bắt buộc phải có</b> (phản biện vòng 1, 01/09/2026): ba cửa ấy về ÊM — không lỗi, không
    /// mốc, không dòng nào. Một máy 3 tài khoản có thể không bao giờ mở nổi gói nào, và triệu chứng duy nhất
    /// người dùng thấy là "bật flash mà chẳng bao giờ chạy". Dòng này mang theo SỐ ĐO (khe còn mấy phút / phiên
    /// nào đang giữ vòng) để người đọc biết phải nới cái gì.</para>
    /// </summary>
    /// <param name="lyDo">Vế sau chữ "vì" — nêu ĐÚNG cửa nào đóng, kèm số đo của nó.</param>
    private void BaoTuChoiGoiFlash(string email, DateTime bayGio, string lyDo)
    {
        if (_mocLogTuChoiGoiFlash is DateTime truoc
            && (bayGio - truoc).TotalMinutes < PhutTietCheLogTuChoiGoiFlash)
        {
            return;
        }
        _mocLogTuChoiGoiFlash = bayGio;
        _services.Log.Append(email,
            "Lịch Flash Sale tự động: tới hạn mở gói nhưng CHƯA mở được — " + lyDo
            + $". Nhịp vẫn thử lại mỗi phút; dòng này chỉ ghi lại tối đa {PhutTietCheLogTuChoiGoiFlash} phút một "
            + "lần. ĐÂY KHÔNG PHẢI LỖI.");
    }

    /// <summary>Ghi MỘT dòng nhật ký "lượt flash tự động là của máy khác" cho mỗi tài khoản mỗi NGÀY — cùng lý
    /// do chống spam với <see cref="BaoKhongPhaiChuTri"/> (cửa chủ trì cố ý không ghi mốc thử, nên
    /// <c>DenGioChay</c> còn trả <c>true</c> suốt khung 3 tiếng = 180 nhịp).</summary>
    private void BaoKhongPhaiChuTriFlash(long accountId, string email, string ownerMachine, DateTime bayGio)
    {
        if (_daBaoChuTriFlash.TryGetValue(accountId, out var ngayDaBao) && ngayDaBao == bayGio.Date)
        {
            return;
        }
        _daBaoChuTriFlash[accountId] = bayGio.Date;
        var nhan = KmLichTuDong.NhanMay(ownerMachine, _services.TenMay?.Invoke(ownerMachine ?? ""));
        _services.Log.Append(email,
            $"Lịch Flash Sale tự động: tới hạn nhưng MÁY CHỦ TRÌ là \"{nhan}\", không phải máy này — bỏ qua, "
            + "không đốt lượt thử của đêm nay. Muốn máy này chủ trì thì mở form 'KM của Shop' ở đây rồi bấm nút "
            + "\"Máy này nhận chủ trì\", sau đó bấm \"Đóng\" (nút đó chỉ có hiệu lực khi đóng form).");
    }

    /// <summary>Cờ chặn lượt COMBO tự động thứ hai chồng lên lượt đang chạy — xem <see cref="NhipLichComboAsync"/>.</summary>
    private bool _dangChayComboTuDong;

    /// <summary>Tài khoản → NGÀY đã ghi dòng "không phải chủ trì" của vòng COMBO. Riêng với
    /// <see cref="_daBaoChuTri"/>: dùng chung một map thì dòng của vòng KM lúc 2h sáng nuốt mất dòng của vòng
    /// combo lúc 6h cùng ngày, và người dùng không bao giờ biết combo cũng đang đứng ngoài.</summary>
    private readonly Dictionary<long, DateTime> _daBaoChuTriCombo = new();

    /// <summary>
    /// <b>Một nhịp của LỊCH COMBO TỰ ĐỘNG</b> (gọi từ cùng đồng hồ 60 giây với <see cref="NhipLichKmAsync"/>).
    /// Cấu trúc song song với nhịp kia — cùng bốn cửa: đang chạy lát cắt · tới hạn · máy chủ trì · cấu hình
    /// dùng được.
    /// <para><b>Hai nhịp KHÔNG BAO GIỜ nổ cùng lúc</b> vì hai khung rời nhau (02–04h và 06–08h), nhưng vẫn giữ
    /// cửa <c>_bridgeRunning</c> + cờ riêng: một vòng 12 shop có thể tràn qua khung sau, và hai luồng tranh
    /// cùng một hồ sơ Brave + cổng 47821 là hỏng thật.</para>
    /// <para><b>DỰNG <see cref="ThamSoCombo"/> MỘT LẦN RỒI LOG IN TỪ CHÍNH NÓ</b> — bài học lỗ phủ 25/08/2026
    /// (đổi ngày gửi xuống Shopee mà 216 test vẫn xanh vì dòng log đọc một biến rời). Mọi test nhịp đều bị chặn
    /// ở cửa khoá xuyên máy nên <c>cauBatDau</c> không được ghi; dòng log ở đây là THƯỚC ĐO duy nhất còn lại,
    /// và nó chỉ đo đúng khi đọc từ chính gói sắp gửi đi.</para>
    /// </summary>
    internal async Task NhipLichComboAsync(DateTime bayGio)
    {
        MocNhipDaNhanChoTest?.Invoke("Combo", bayGio);
        if (_dangChayComboTuDong || _dangChayKmTuDong || _bridgeRunning) { return; }

        var mayNay = _services.MayNay?.Invoke() ?? "";

        foreach (var acc in _services.Accounts.GetAll())
        {
            var cf = _services.KmConfig.Get(acc.Id);
            if (!KmLichTuDong.DenGioChay(cf, bayGio, LoaiChuongTrinh.Combo)) { continue; }

            if (!KmLichTuDong.LaMayChuTri(cf.OwnerMachine, mayNay))
            {
                // KHÔNG ghi mốc thử: lượt ấy không phải của mình — ghi vào là ĐỐT lượt của đúng máy chủ trì.
                BaoKhongPhaiChuTriCombo(acc.Id, acc.Email, cf.OwnerMachine, bayGio);
                continue;
            }

            // MỘT GÓI, MỘT NGUỒN — mọi thứ dưới đây (luật kiểm, dòng nhật ký, gói gửi xuống ext) đọc từ chính
            // `thamSo`. Xem xmldoc của `DungThamSoCombo`.
            var thamSo = DungThamSoCombo(cf, bayGio);

            // Dùng LẠI đúng luật thuần của form — hai bộ luật song song thì bộ ít chạy sẽ mục dần.
            var loiCauHinh =
                KmDialogViewModel.KiemTraFormCombo(
                    thamSo.Ten, thamSo.Gio,
                    thamSo.Loai, thamSo.MuaSl, thamSo.Giam, thamSo.GioiHan)
                // Vế chu kỳ tách riêng vì `KiemTraFormCombo` không nhận `SoNgay` (chu kỳ dùng CHUNG cho ba
                // vòng, không thuộc thông số combo). Lưới an toàn: `KmConfigRepository.ChuanHoaSoNgay` đã kẹp
                // ≥ 1 ở cửa đọc DB, nên vế này chỉ nổ nếu ai đó bỏ lượt chuẩn hoá đó.
                ?? (cf.SoNgay < 1 ? "Số ngày của chu kỳ tự động phải ≥ 1." : null)
                // SỐ SHOP LƯỢT NÀY THỰC SỰ CHẠY — biên an của mốc bắt đầu tính theo nó; xem khối chú thích
                // cùng chỗ ở `NhipLichKmAsync` cho ca hỏng vĩnh viễn nếu bỏ qua.
                //
                // ⚠ `SoShopSeChay`, KHÔNG phải tổng số shop: vòng combo bị `TranShopLuotCombo` cắt sau N shop,
                // nên tính biên an theo cả 12 shop là đòi 96 phút cho một vòng chỉ chạy 2 shop (~16 phút) —
                // chặn oan gần một tiếng rưỡi. Ca thật 16:16 ngày 26/08/2026.
                ?? KmDialogViewModel.KiemTraMocTuongLai(
                    thamSo.NgayBatDau, thamSo.Gio, bayGio,
                    KmLichTuDong.SoShopSeChay(_services.Results.GetShops(acc.Id).Count, LoaiChuongTrinh.Combo));
            if (loiCauHinh is not null)
            {
                // VẪN ghi mốc thử: không ghi thì nhịp 60 giây lặp lại chính dòng này mỗi phút tới hết khung.
                _services.KmConfig.Save(cf with { ComboLanThuTuDong = bayGio });
                _services.KmHoSoDaDoi?.Invoke();
                _services.Log.Append(acc.Email,
                    "Lịch Combo tự động: tới hạn nhưng CẤU HÌNH KHÔNG DÙNG ĐƯỢC — " + loiCauHinh
                    + " KHÔNG mở trình duyệt. Mở form 'KM của Shop' ở máy này, sửa lại thông số combo rồi bấm "
                    + "\"Đóng\" — đêm sau lượt tự động mới chạy được.");
                continue;
            }

            _dangChayComboTuDong = true;
            try
            {
                // ĐÁNH DẤU LƯỢT THỬ **TRƯỚC** KHI CHẠY — mốc này là thứ chặn nhịp 60 giây bắn lượt thứ hai
                // trong cùng khung 06–08h khi lượt này HỎNG (captcha / khoá / lỗi shop đầu).
                _services.KmConfig.Save(cf with { ComboLanThuTuDong = bayGio });
                _services.KmHoSoDaDoi?.Invoke();

                var (mo, dong) = KmLichTuDong.Khung(LoaiChuongTrinh.Combo);
                var hanChot = KmLichTuDong.HanChotLuotTuDong(bayGio, LoaiChuongTrinh.Combo);
                _services.Log.Append(acc.Email,
                    $"Lịch Combo tự động: tới hạn (mỗi {cf.SoNgay} ngày, lần cuối "
                    + (cf.ComboLanChayCuoi is DateTime c ? c.ToString("dd-MM-yyyy HH:mm") : "chưa chạy")
                    + $") — bắt đầu lượt Combo trong khung {mo}-{dong}h, không bắt đầu shop mới sau {hanChot:HH:mm}. "
                    // ⚠ ĐỌC TỪ `thamSo`, KHÔNG từ `cf` — đây là thứ biến dòng log thành THƯỚC ĐO của gói gửi
                    // xuống Shopee thay vì một chuỗi song song tự nhất quán.
                    + $"Chương trình: {thamSo.MoTa()} "
                    + $"(khoảng {KmLichTuDong.SoNgayDatTruoc} ngày, cùng giờ hai đầu). "
                    + "Đêm nay KHÔNG thử lại lượt nữa dù lượt này hỏng.");

                await ChayComboAsync(acc.Id, acc.Email, thamSo, hanChot);
            }
            finally
            {
                _dangChayComboTuDong = false;
            }
            return; // một tài khoản mỗi nhịp
        }
    }

    /// <summary>
    /// <b>PURE — dựng gói <see cref="ThamSoCombo"/> của MỘT lượt combo tự động</b> từ cấu hình + mốc hiện tại.
    /// Hai mốc ngày dùng chung <see cref="KmLichTuDong.NgayBatDau"/> / <see cref="KmLichTuDong.NgayKetThuc"/>
    /// với vòng kia: khoảng 180 ngày áp cho combo y hệt.
    /// <para><b><c>ngayBd</c> là MỘT biến dùng cho CẢ tên lẫn trường <see cref="ThamSoCombo.NgayBatDau"/>.</b>
    /// Gọi <see cref="KmLichTuDong.NgayBatDau"/> hai lần ở hai chỗ thì tên và ngày là hai lượt đọc độc lập — và
    /// đó đúng là chỗ lỗ phủ ĐÃ ĐO ĐƯỢC ngày 25/08/2026 khi thử phá: đổi riêng trường ngày mà tên vẫn mang ngày
    /// đúng, nên dòng nhật ký (thứ duy nhất test soi được) vẫn chứa ngày đúng và KHÔNG test nào đỏ.</para>
    /// <para>Tách <c>static</c> khỏi <see cref="NhipLichComboAsync"/> để đo được KHÔNG cần đồng hồ, DB hay trình
    /// duyệt — gói này là thứ đi thẳng xuống extension, phải đo được rẻ và không phụ thuộc lịch.</para>
    /// </summary>
    internal static ThamSoCombo DungThamSoCombo(KmConfig cf, DateTime bayGio)
    {
        var ngayBd = KmLichTuDong.NgayBatDau(bayGio);
        return new ThamSoCombo(
            KmDialogViewModel.SinhTenComboMacDinh(ngayBd, cf.ComboGioBatDau),
            ngayBd,
            KmLichTuDong.NgayKetThuc(bayGio),
            cf.ComboGioBatDau,
            cf.ComboLoai, cf.ComboMuaSl, cf.ComboGiam, cf.ComboGioiHan);
    }

    /// <summary>
    /// <b>PURE — dựng gói <see cref="ThamSoKm"/> của MỘT lượt KM TỰ ĐỘNG</b> từ cấu hình + mốc hiện tại. Bản đối
    /// xứng của <see cref="DungThamSoCombo"/> cho vòng "Chương Trình Của Shop".
    /// <para><b><c>ngayBd</c> là MỘT biến dùng cho CẢ tên lẫn trường <see cref="ThamSoKm.NgayBatDau"/>.</b> Gọi
    /// <see cref="KmLichTuDong.NgayBatDau"/> hai lần ở hai chỗ thì tên và ngày là hai lượt đọc độc lập — đúng
    /// chỗ lỗ phủ ĐÃ ĐO ĐƯỢC ngày 25/08/2026.</para>
    /// <para>Tách <c>static</c> khỏi <see cref="NhipLichKmAsync"/> để đo được KHÔNG cần đồng hồ, DB hay trình
    /// duyệt — gói này là thứ đi thẳng xuống extension.</para>
    /// </summary>
    internal static ThamSoKm DungThamSoKm(KmConfig cf, DateTime bayGio)
    {
        var ngayBd = KmLichTuDong.NgayBatDau(bayGio);
        return new ThamSoKm(
            KmDialogViewModel.SinhTenMacDinh(ngayBd, cf.GioBatDau),
            ngayBd,
            KmLichTuDong.NgayKetThuc(bayGio),
            cf.GioBatDau,
            cf.PhanTram);
    }

    /// <summary>Ghi MỘT dòng nhật ký "lượt combo tự động là của máy khác" cho mỗi tài khoản mỗi NGÀY — cùng lý
    /// do chống spam với <see cref="BaoKhongPhaiChuTri"/> (cửa chủ trì cố ý không ghi mốc thử, nên
    /// <c>DenGioChay</c> còn trả true suốt khung).</summary>
    private void BaoKhongPhaiChuTriCombo(long accountId, string email, string ownerMachine, DateTime bayGio)
    {
        if (_daBaoChuTriCombo.TryGetValue(accountId, out var ngayDaBao) && ngayDaBao == bayGio.Date)
        {
            return;
        }
        _daBaoChuTriCombo[accountId] = bayGio.Date;
        var nhan = KmLichTuDong.NhanMay(ownerMachine, _services.TenMay?.Invoke(ownerMachine ?? ""));
        _services.Log.Append(email,
            $"Lịch Combo tự động: tới hạn nhưng MÁY CHỦ TRÌ là \"{nhan}\", không phải máy này — bỏ qua, "
            + "không đốt lượt thử của đêm nay. Muốn máy này chủ trì thì mở form 'KM của Shop' ở đây rồi bấm nút "
            + "\"Máy này nhận chủ trì\", sau đó bấm \"Đóng\" (nút đó chỉ có hiệu lực khi đóng form).");
    }

    /// <summary>
    /// <b>Hiện các nút CHẨN ĐOÁN (dò DOM) hay không.</b> Mặc định ẨN — chúng không phải tính năng cho người
    /// dùng, và một nút "Dò DOM KM" bấm nhầm sẽ tạo một chương trình khuyến mãi THẬT.
    /// <para><b>Cố ý KHÔNG xoá code:</b> Shopee đổi DOM là chuyện sẽ xảy ra, và lúc đó thứ cần đầu tiên chính là
    /// lượt dò này — nó ghi ra nhật ký TỪNG BƯỚC kèm giá trị đọc lại, tức chỉ thẳng bước nào vỡ. Dò lại từ đầu
    /// mất một đêm (18→19/08/2026); bật lại một cờ mất một giây.</para>
    /// <para><b>Bật bằng biến môi trường <c>SHOPEE_DEV_TOOLS=1</c> rồi mở lại app</b> — cố ý chọn biến môi
    /// trường chứ không phải ô tick trong Cài đặt: không cần build lại (máy dev 19/08 đang hỏng SDK .NET), và
    /// người dùng thường không vô tình bật trúng.</para>
    /// <para><b>Property INSTANCE — và một đính chính, đọc trước khi đổi lại.</b> Lượt phản biện 19/08/2026 báo
    /// rằng bản <c>static</c> làm binding fail im lặng nên nút HIỆN với mọi người. <b>ĐO LẠI THÌ KHÔNG ĐÚNG</b>:
    /// dựng <c>AccountsView</c> thật, cho qua một lượt layout rồi đọc <c>Visibility</c> — bản <c>static</c> cho
    /// <c>Collapsed</c>/<c>Visible</c> ĐÚNG cả hai chiều, vì lượt phân giải Binding path có nhánh dự phòng
    /// <c>Type.GetProperty</c> (mặc định gồm cả <c>Static</c>). Khớp với bằng chứng chạy thật đêm 19/08 (UIA
    /// không thấy nút khi chưa đặt biến). Giữ dạng instance vì nó đọc biến môi trường ĐÚNG MỘT LẦN mỗi VM thay
    /// vì mỗi lượt binding, không phải vì bản kia hỏng.</para>
    /// <para>Cái CÓ THẬT là lỗ hổng KIỂM CHỨNG: mười test cũ đọc thẳng property + soi chuỗi trong file .xaml,
    /// không cái nào đo <c>Visibility</c> thật của nút — tức xoá ràng buộc <c>Visibility</c> trong XAML thì
    /// chúng vẫn xanh. Nay có <c>NutChanDoanAnTests.Visibility_ThatCuaNut_TheoDungBienMoiTruong</c>.</para>
    /// </summary>
    public bool HienNutDev { get; } = DocCoDev(Environment.GetEnvironmentVariable(BienMoiTruongDev));

    /// <summary>Tên biến môi trường bật các nút chẩn đoán.</summary>
    internal const string BienMoiTruongDev = "SHOPEE_DEV_TOOLS";

    /// <summary>PURE — giá trị biến môi trường nào được coi là BẬT. Tách ra để test ma trận không cần dựng VM.</summary>
    internal static bool DocCoDev(string? giaTri) => giaTri is "1" or "true" or "TRUE";

    /// <summary>
    /// Ghi MỘT dòng nhật ký "lượt tự động là của máy khác" cho mỗi tài khoản mỗi NGÀY.
    /// <para>Nhịp lịch chạy mỗi 60 giây và cửa chủ trì cố ý KHÔNG ghi <c>LanThuTuDong</c>, nên
    /// <c>DenGioChay</c> còn trả <c>true</c> suốt khung 2–6h ⇒ không chặn thì đúng một dòng này lặp 240 lần
    /// mỗi đêm. Nhật ký spam thì người dùng thôi đọc, tức là mất luôn tác dụng của chính dòng log.</para>
    /// <para><b>Câu chữ phải khớp HÀNH VI THẬT</b> (phản biện vòng 3): bản vòng 2 bảo người dùng "mở form và
    /// lưu lại", nhưng cùng vòng đó <c>BanLuuSauForm</c> lại trả <c>null</c> khi không có gì đổi ⇒ làm đúng lời
    /// khuyên thì KHÔNG có gì xảy ra, và một máy chủ trì đã nghỉ hưu (cài lại Windows ⇒ đổi id) khoá chết KM
    /// trên cả fleet. Nay chỉ đúng nút "Máy này nhận chủ trì" — nút có thật, xem
    /// <c>KmDialogViewModel.NhanChuTri</c>.</para>
    /// </summary>
    private void BaoKhongPhaiChuTri(long accountId, string email, string ownerMachine, DateTime bayGio)
    {
        if (_daBaoChuTri.TryGetValue(accountId, out var ngayDaBao) && ngayDaBao == bayGio.Date)
        {
            return;
        }
        _daBaoChuTri[accountId] = bayGio.Date;
        // TÊN máy + id ngắn, không phải GUID thô (X6): người đọc phải nhận ra ngay đó là máy nào trong nhà.
        var nhan = KmLichTuDong.NhanMay(ownerMachine, _services.TenMay?.Invoke(ownerMachine ?? ""));
        _services.Log.Append(email,
            $"Lịch KM tự động: tới hạn nhưng MÁY CHỦ TRÌ là \"{nhan}\", không phải máy này — bỏ qua, "
            + "không đốt lượt thử của đêm nay. Muốn máy này chủ trì thì mở form 'KM của Shop' ở đây rồi bấm nút "
            + "\"Máy này nhận chủ trì\", sau đó bấm \"Đóng\" (nút đó chỉ có hiệu lực khi đóng form).");
    }

    /// <summary>
    /// Dựng ViewModel của form "KM của Shop" — <b>tách khỏi <see cref="MoFormKm"/> để CHỖ NỐI hook
    /// <c>MayNay</c> ⇄ form đo được bằng test</b> (đường thật đi qua <c>DialogService.ShowKm</c>, một modal WPF
    /// không dựng headless được).
    /// <para><b>Vì sao đáng tách</b> (phản biện vòng 5, Z5): nghiệm thu đột biến đổi lượt đọc hook ở đây thành
    /// một hằng bịa và <b>cả 2552 test vẫn xanh</b> — lần thứ ba trong đợt cùng một hình dạng "hàm đúng, chỗ
    /// nối không ai đo". Chỗ nối này quyết định máy nào được đóng dấu chủ trì khi người dùng sửa form, tức
    /// quyết định đêm sau máy nào chạy lượt KM.</para>
    /// </summary>
    internal KmDialogViewModel DungFormKm(long accountId, string email, int soShop)
        => new(accountId, email, soShop, _services.KmConfig.Get(accountId),
            _services.MayNay?.Invoke() ?? "", _services.TenMay);

    /// <summary>Tài khoản → NGÀY đã ghi dòng "không phải chủ trì" (xem <see cref="BaoKhongPhaiChuTri"/>).</summary>
    private readonly Dictionary<long, DateTime> _daBaoChuTri = new();

    /// <summary>Cờ chặn lượt KM tự động thứ hai chồng lên lượt đang chạy — xem <see cref="NhipLichKmAsync"/>.</summary>
    private bool _dangChayKmTuDong;

    /// <summary>Câu NHẬT KÝ tổng kết một lượt KM. Tách riêng để test được từng ca mà không cần trình duyệt.</summary>
    /// <param name="kq">Kết quả cả lượt.</param>
    /// <param name="coPhienChayLai">Lượt mượn này CÓ tạm dừng phiên check đơn nào không. <c>false</c> ⇒ câu chốt
    /// KHÔNG được hứa "check đơn chạy lại": lượt tự động 2h sáng của một tài khoản không bật check đơn thì chẳng
    /// có phiên nào để chạy lại, mà dòng log vẫn khẳng định có — người đọc sáng hôm sau sẽ đi tìm một phiên
    /// không tồn tại.</param>
    /// <param name="loai">Vòng nào đang tổng kết — quyết định TÊN VIỆC mở đầu câu <b>và GIỜ ĐÓNG KHUNG</b> in ở
    /// nhánh hết khung. Tham số hoá thay vì chép hàm: hai vòng mà hai bộ luật "đếm shop hỏng / hết khung" là hai
    /// bản trôi lệch.
    /// <para><b>Vì sao phải là <see cref="LoaiChuongTrinh"/> chứ không phải một chuỗi tên việc</b> (phản biện
    /// 25/08/2026, VỪA-3): bản trước nhận <c>tenViec</c> nhưng vẫn đọc hằng <c>KmLichTuDong.GioDongKhung</c> = 4
    /// — hằng của RIÊNG vòng KM — nên lượt combo chạm hạn 08:00 ghi <i>"DỪNG vì hết khung tự động (4:00)"</i>, và
    /// người trực 8h sáng đi tìm một mốc 4:00 không tồn tại. Một tham số mang đủ cả hai thứ thì không lệch được
    /// nữa.</para></param>
    internal static string CauTongKetKm(
        KetQuaKm kq, bool coPhienChayLai, LoaiChuongTrinh loai = LoaiChuongTrinh.ChuongTrinhShop)
    {
        var tenViec = TenViecVong(loai);
        // Mẫu số là TỔNG shop của lượt (<see cref="KetQuaKm.TongShop"/>), KHÔNG phải số shop đã chạy: hết khung
        // ở shop đầu thì `Shops` rỗng ⇒ "tạo 0/0 shop … còn 12 shop chưa chạy", tự mâu thuẫn trong một câu.
        var dau = $"{tenViec} xong: tạo {kq.SoTao}/{kq.TongShop} shop";
        if (kq.SoBoQua > 0) { dau += $", bỏ qua {kq.SoBoQua} shop không có sản phẩm"; }
        if (kq.SoHong > 0) { dau += $", HỎNG {kq.SoHong} shop"; }
        dau += ".";
        if (kq.Captcha) { dau += " DỪNG GIỮA CHỪNG vì rơi captcha/verify."; }
        else if (kq.HetKhung)
        {
            // KHÔNG phải lỗi — nói đúng tên chuyện đã xảy ra, kẻo người dùng đi tìm một sự cố không có.
            // "sẽ vào lượt kế theo chu kỳ", KHÔNG hứa "để đêm sau": mốc LanChayCuoi vẫn được ghi (xem
            // ChotLuotKm) nên lượt kế là sau SoNgay ngày — hứa "đêm sau" là nói sai cho người đọc.
            // ⚠ IN HẠN THẬT ĐÃ CHẶN LƯỢT, không phải giờ đóng khung của loại (sửa 03/09/2026, sổ P-20260903-7).
            // Vòng flash chạy thành nhiều GÓI và mỗi gói bị chặn bởi hạn của GÓI = min(cuối khe − 45', 22:15) —
            // ví dụ 18:07. Bản trước in `KmLichTuDong.Khung(FlashSale).Dong` = 23:00, tức nhật ký lúc 18:07 nói
            // "hết khung tự động (23:00)" và gửi người đọc đi tìm một mốc không tồn tại; mà đây là dòng DUY NHẤT
            // nói ra vì sao gói chỉ làm được một shop.
            // Rơi về giờ đóng khung khi lượt không mang hạn (`HanChotDaCham == null`): hai vòng KM/Combo dừng vì
            // đúng cái khung của chúng (Shop 4:00 · Combo 8:00), và ở đó con số ấy MỚI là con số đúng.
            var mocDung = kq.HanChotDaCham is DateTime hc
                ? $"{hc:HH:mm}"
                : $"{KmLichTuDong.Khung(loai).Dong}:00";
            dau += $" DỪNG vì hết khung tự động ({mocDung}) — còn {kq.SoShopChuaChay} shop "
                   + "chưa chạy, sẽ vào lượt kế theo chu kỳ.";
        }
        else if (kq.TranThuDaCham > 0)
        {
            // LÝ DO DỪNG SỚM THỨ BA — và là lý do KHÔNG PHẢI HỎNG, phải nói ra bằng đúng tên của nó. Một lượt
            // dừng ở shop thứ 2 mà nhật ký im lặng thì y hệt ca "10 shop hỏng câm". Xem
            // `KmLichTuDong.TranShopLuotCombo`.
            dau += $" DỪNG vì đang ở CHẾ ĐỘ THỬ (trần {kq.TranThuDaCham} shop mỗi lượt) — còn {kq.SoShopChuaChay} "
                   + "shop chưa chạy. KHÔNG phải lỗi, và cũng không phải do hết khung tự động.";
        }
        else if (kq.MocBatDauDaTroiQua is DateTime mocBd)
        {
            // LÝ DO DỪNG SỚM THỨ TƯ — và là lý do dễ bị đọc nhầm nhất, nên phải LOẠI TRỪ tường minh cái tên mà
            // người đọc sẽ tự gán cho nó. Triệu chứng của ca này (nếu không dừng) là hàng loạt shop đỏ với câu
            // `"li 'HH' đang disabled"` — trông y hệt Shopee đổi DOM, trong khi nguyên nhân là ĐỒNG HỒ.
            dau += $" DỪNG vì MỐC BẮT ĐẦU của chương trình ({mocBd:HH:mm}) đã TRÔI QUA GIỮA VÒNG — còn "
                   + $"{kq.SoShopChuaChay} shop chưa chạy. KHÔNG phải lỗi selector, không phải hết khung tự "
                   + "động: đặt Giờ muộn hơn để cả vòng kịp chạy xong trước mốc đó.";
        }
        else if (kq.Loi is not null) { dau += " Dừng giữa chừng: " + kq.Loi; }
        else if (kq.NhuongChoCheckDon)
        {
            // LÝ DO DỪNG SỚM THỨ NĂM (03/09/2026) — và KHÔNG PHẢI LỖI.
            //
            // ⚠ PHẢI ĐỨNG SAU NHÁNH `Loi` (sổ P-20260901-6, cùng bài học với `NgayMaiKhongDo` ngay dưới): đứng
            // trước thì một gói vừa nhường chỗ VỪA hỏng thật — cầu nối đứt, `vePicker` không về — sẽ in ra một
            // câu êm ái và NUỐT MẤT câu lỗi duy nhất của lượt.
            dau += $" DỪNG để NHƯỜNG CHỖ CHO CHECK ĐƠN — còn {kq.SoShopChuaChay} shop chưa chạy, gói sau làm "
                   + "tiếp. KHÔNG phải lỗi và cũng không phải hết khung: một lượt chạy lại check đơn đã tới mốc "
                   + "và đang chờ gói này xong.";
        }
        else if (kq.NgayMaiKhongDo)
        {
            // GHI CHÚ CUỐI CÙNG (riêng Flash Sale) — và KHÔNG PHẢI LỖI.
            //
            // ⚠ PHẢI ĐỨNG SAU NHÁNH `Loi` (sửa 01/09/2026, phản biện vòng 1). Đứng trước thì một gói vừa mang cờ
            // này VỪA hỏng thật — `vePicker` không về được, kênh cầu nối đứt — sẽ in ra một câu êm ái "không phải
            // lỗi" và NUỐT MẤT câu lỗi duy nhất của lượt. Cờ này chỉ là quan sát; `Loi` là sự cố.
            //
            // Câu chữ nói ĐÚNG những gì đo được: một gói ~2 shop KHÔNG phân biệt nổi "shop này đã kín khung" với
            // "Shopee chưa mở / đã đóng ngày". Bản trước khẳng định một chiều ("Shopee chưa mở khung") và đếm cả
            // những shop chưa hề chạy là "không mở" — cả hai đều là suy diễn, và cả hai đều đẩy người đọc đi
            // sai hướng.
            dau += " Mọi shop ĐÃ CHẠY của gói đều báo NGÀY ĐÍCH không còn ĐỎ và không tạo được gì — các shop đó "
                   + "đã kín khung, hoặc Shopee chưa mở / đã đóng đăng ký cho ngày đó (từ một gói thì KHÔNG "
                   + "phân biệt được). KHÔNG phải lỗi selector; gói sau vẫn chạy bình thường.";
        }

        // Nêu ĐÍCH DANH shop hỏng: "HỎNG 3 shop" mà không nói shop nào thì người đọc vẫn phải mở lại toàn bộ
        // nhật ký để lần ra — đúng thứ nhật ký sinh ra để tránh.
        var hong = kq.Shops.Where(s => s.Loi is not null).Select(s => s.ShopName).ToList();
        if (hong.Count > 0) { dau += " Shop hỏng: " + string.Join(", ", hong) + "."; }
        dau += coPhienChayLai
            ? " Cửa sổ đã đóng, check đơn chạy lại."
            : " Cửa sổ đã đóng (không có phiên check đơn nào đang chạy để bật lại).";
        return dau;
    }

    /// <summary>Câu cho ô trạng thái — ngắn hơn câu nhật ký. Nhận <paramref name="loai"/> vì cùng lý do với
    /// <see cref="CauTongKetKm"/>: bản hard-code "KM của Shop" làm lượt COMBO báo sai tên việc ngay trên ô trạng
    /// thái, tức chỗ người dùng nhìn đầu tiên.</summary>
    internal static string CauTrangThaiKm(KetQuaKm kq, LoaiChuongTrinh loai = LoaiChuongTrinh.ChuongTrinhShop)
    {
        var ten = TenViecVong(loai);
        return kq.Captcha ? $"{ten}: dừng vì captcha."
             : kq.HetKhung ? $"{ten}: hết khung tự động"
                             + (kq.HanChotDaCham is DateTime hc ? $" ({hc:HH:mm})" : "")
                             + $" — tạo {kq.SoTao}/{kq.TongShop} shop, còn "
                             + $"{kq.SoShopChuaChay} shop chưa chạy."
             : kq.TranThuDaCham > 0 ? $"{ten}: CHẾ ĐỘ THỬ (trần {kq.TranThuDaCham} shop) — tạo "
                             + $"{kq.SoTao}/{kq.TongShop} shop, còn {kq.SoShopChuaChay} shop chưa chạy."
             : kq.MocBatDauDaTroiQua is DateTime mocBd
                             ? $"{ten}: mốc bắt đầu ({mocBd:HH:mm}) đã trôi qua giữa vòng — tạo "
                               + $"{kq.SoTao}/{kq.TongShop} shop, còn {kq.SoShopChuaChay} shop chưa chạy."
             // ⚠ `Loi` TRƯỚC `NgayMaiKhongDo` — cùng lý do với `CauTongKetKm`: một gói vừa mang cờ vừa hỏng
             // thật thì ô trạng thái phải nói cái HỎNG, không phải một câu êm ái về lịch Shopee.
             : kq.Loi is not null ? $"{ten}: " + kq.Loi
             // Nhánh NHƯỜNG CHỖ cũng xếp SAU `Loi`, cùng lý do — xem `CauTongKetKm`.
             : kq.NhuongChoCheckDon ? $"{ten}: dừng để nhường chỗ cho check đơn — tạo {kq.SoTao}/{kq.TongShop} "
                             + $"shop, còn {kq.SoShopChuaChay} shop chưa chạy."
             : kq.NgayMaiKhongDo ? $"{ten}: mọi shop đã chạy của gói đều báo ngày đích KHÔNG còn đỏ — không tạo "
                             + "gì (đã kín khung, hoặc Shopee chưa mở/đã đóng ngày đó)."
             : $"{ten} xong: tạo {kq.SoTao}/{kq.TongShop} shop.";
    }

    /// <summary>
    /// PURE — TÊN VIỆC của một vòng, đúng như nó hiện ra cho người dùng.
    /// <para>Vòng <see cref="LoaiChuongTrinh.ChuongTrinhShop"/> giữ tên <b>"KM của Shop"</b> chứ không lấy
    /// <see cref="KmLichTuDong.TenLoai"/> ("Chương Trình Của Shop"): đó là tên cái NÚT trên màn Tài khoản, tên
    /// cái FORM, và tên việc truyền vào <see cref="ChayLatCatAsync"/> — đổi ở đây là nhật ký nói một đằng giao
    /// diện nói một nẻo. Hai vòng còn lại thì tên Shopee và tên trong app trùng nhau.</para>
    /// </summary>
    internal static string TenViecVong(LoaiChuongTrinh loai)
        => loai == LoaiChuongTrinh.ChuongTrinhShop ? "KM của Shop" : KmLichTuDong.TenLoai(loai);

    // MƯỢN HỒ SƠ — một cụm, ĐÚNG một chỗ mượn, ĐÚNG một chỗ trả.
    //
    // "Mở list shop" cần độc quyền ba thứ TOÀN MÁY: hồ sơ Brave, cổng cầu nối 47821, và khoá chạy xuyên máy.
    // Trước 18/08/2026 khái niệm này bị rải ra 5 chỗ với một biến `long?` và đẻ ra một chuỗi lỗi cùng gốc
    // (ghi đè chủ nợ, đóng cửa sổ mà không trả, cờ "người dùng dừng tay" bật oan). Nay:
    //     MuonHoSoAsync : dừng MỌI phiên đang hoạt động → chờ Active RỖNG → xin khoá xuyên máy
    //     TraHoSo       : nhả khoá + chạy lại ĐÚNG TẬP đã dừng + xoá cờ
    // Mọi đường "cửa sổ biến mất / lượt mượn kết thúc" PHẢI đi qua `TraHoSo()`. Thêm đường mới mà quên gọi nó
    // là mượn-không-trả: phiên check đơn chết im, chỉ còn một dòng log hứa suông "sẽ chạy lại".
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Các tài khoản mà CHÍNH lượt mượn này đã tạm dừng — phải chạy lại BẰNG HẾT khi trả hồ sơ.
    /// <para><b>Tập chứ không phải một id</b>: cổng 47821 và <c>FreeProfile(alsoMatchBridgeExtension: true)</c>
    /// đều là TOÀN MÁY, nên mượn hồ sơ là phải dừng mọi phiên chứ không riêng tài khoản đang xem.</para>
    /// </summary>
    private readonly HashSet<long> _dsTkDaDung = new();

    /// <summary>
    /// Các tài khoản người dùng đã CHỦ ĐỘNG bảo dừng trong lúc lượt mượn hồ sơ này đang bay ⇒ khi trả hồ sơ
    /// KHÔNG chạy lại hộ đúng những tài khoản đó, phần còn lại của <see cref="_dsTkDaDung"/> vẫn chạy lại.
    /// <para><b>Tập PER-TÀI-KHOẢN, không phải một cờ cho cả máy</b> (NẶNG-A, phản biện 04/09/2026): bản cờ đơn
    /// làm cú "✖ Dừng" tài khoản A từ hub giết luôn đường chạy lại của B (B chết câm), còn cú Dừng B giữa lát
    /// cắt của A thì bị bỏ qua và B tự mở lại Brave sau khi người dùng đã bảo dừng.</para>
    /// <para>Dọn ở <see cref="TraHoSo"/> (cùng lúc với <see cref="_dsTkDaDung"/>) và ở đầu mỗi lượt mượn mới;
    /// một lệnh ▶ Chạy cho tài khoản nào thì gỡ tài khoản đó ra (người dùng đổi ý).</para>
    /// </summary>
    private readonly HashSet<long> _dsTkNguoiDungDungTay = new();

    /// <summary>
    /// Hạn chờ MỌI phiên dừng hẳn (nhả khoá + đóng Brave + <c>Dispose</c> kênh). 30s: rộng tay cho khối
    /// <c>finally</c> của phiên thật.
    /// <para><c>internal</c> + không <c>readonly</c> để test rút xuống ~1s — test đường hết-hạn mà chờ thật 30
    /// giây thì cả bộ test đắt thêm nửa phút. Giá trị mặc định được khoá bằng test riêng.</para>
    /// </summary>
    internal static TimeSpan HanChoDungHan = TimeSpan.FromSeconds(30);

    /// <summary>Nhịp hỏi lại "đã dừng hẳn chưa".</summary>
    private static readonly TimeSpan NhipHoiDungHan = TimeSpan.FromMilliseconds(300);

    /// <summary>Lượt nhả khoá gần nhất — có thể CÒN ĐANG BAY. Lượt mượn kế phải <c>await</c> nó trước khi xin
    /// khoá mới, kẻo DELETE cũ hạ cánh sau và xoá đúng dòng vừa cấp.</summary>
    private Task _luotNhaKhoaCuoi = Task.CompletedTask;

    /// <summary>Nối một lượt nhả khoá vào ĐUÔI lượt trước (nuốt lỗi của lượt trước để chuỗi không đứt).</summary>
    private async Task NoiTiepNhaKhoa(Task truoc)
    {
        try { await truoc; } catch { /* lượt trước hỏng cũng không được chặn lượt này */ }
        await NhaKhoaXemAsync();
    }

    /// <summary>
    /// MƯỢN hồ sơ: dừng MỌI phiên đang hoạt động → chờ <c>Sessions.Active</c> RỖNG → xin khoá chạy xuyên máy.
    /// <para><b>Vì sao dừng MỌI phiên, không riêng tài khoản đang xem</b> (phản biện 18/08/2026): cổng cầu nối
    /// 47821 là hằng số toàn máy và <c>OrdersBridgeLauncher.Launch</c> gọi
    /// <c>FreeProfile(alsoMatchBridgeExtension: true)</c> — quét giết MỌI trình duyệt cầu nối kể cả hồ sơ khác.
    /// Mở cho B trong lúc A chạy thì <c>_channel.Start()</c> trượt cổng → extension báo lỗi →
    /// <c>QuyetDinhSauThuBanSach</c> ra <c>DangNhapLai</c> → <b>Playwright mở ra bắt đăng nhập lại subaccount B</b>,
    /// đúng thứ người dùng mở lời bằng câu "tôi không muốn phải login lại".</para>
    /// <para><b>Vì sao chờ tới khi Active RỖNG, không chờ từng id</b>: dừng A xong, <c>OnSessionChanged</c> của
    /// manager gỡ A rồi <c>StartAsync(B)</c> NGAY trong cùng lock (hàng đợi). Vòng chờ chỉ hỏi A sẽ thấy "xong"
    /// trong khi B vừa được bắn lên — hai bên cùng phóng trình duyệt, bên sau <c>FreeProfile</c> giết bên trước.</para>
    /// <para>Hết hạn hoặc bị HUỶ ⇒ trả <c>false</c>, chỗ gọi KHÔNG mở gì (fail-closed).</para>
    /// </summary>
    /// <param name="email">Tài khoản của lượt mượn (nguồn ghi nhật ký).</param>
    /// <param name="tenViec">Tên việc đang mượn hồ sơ — ghép vào câu nhật ký. Không có nó thì lượt KM tự động
    /// lúc 2h sáng vẫn ghi "để mở list shop… đóng cửa sổ xong sẽ chạy lại", tức nói về một cửa sổ không ai mở
    /// và một thao tác không ai làm.</param>
    /// <param name="tuChayLaiKhiXong">Lát cắt TỰ kết thúc và tự trả hồ sơ (KM của Shop) — câu log phải nói
    /// "xong lượt là tự chạy lại", chứ không phải "đóng cửa sổ xong sẽ chạy lại" (nút "Mở list shop").</param>
    /// <param name="ct">Token huỷ.</param>
    private async Task<bool> MuonHoSoAsync(
        string email, string tenViec, bool tuChayLaiKhiXong, System.Threading.CancellationToken ct)
    {
        // Lượt mượn TRƯỚC còn treo (bấm "Mở list shop" lần hai, cho tài khoản KHÁC) ⇒ nhả khoá cũ trước khi xin
        // khoá mới. Không nhả là lease của tài khoản trước rò lại vĩnh viễn — `_loginKhoaXem` chỉ giữ được MỘT
        // login. Tập `_dsTkDaDung` là HashSet nên các phiên đã dừng của lượt trước vẫn được gộp và chạy lại đủ
        // ở lượt `TraHoSo` cuối cùng.
        if (_dangGiuKhoaXem == 1)
        {
            await NhaKhoaXemAsync();
        }

        var dangChay = _services.Sessions.Active;
        if (dangChay.Count > 0)
        {
            var ten = dangChay
                .Select(p => _services.Accounts.GetById(p.AccountId)?.Email ?? $"#{p.AccountId}")
                .ToList();
            _services.Log.Append(email,
                $"Tạm dừng check đơn ({string.Join(", ", ten)}) để {ThuongHoaChuDau(tenViec)} — vòng đang chạy dở "
                + "BỊ CẮT, "
                + (tuChayLaiKhiXong ? "xong lượt này app tự chạy lại" : "đóng cửa sổ xong sẽ chạy lại")
                + " TỪ ĐẦU vòng mới (không phải đăng nhập lại: vẫn hồ sơ + cookie cũ).");
            BusyStatus = "Đang tạm dừng check đơn để nhường hồ sơ...";

            foreach (var p in dangChay)
            {
                _dsTkDaDung.Add(p.AccountId);
                _services.Sessions.Stop(p.AccountId);
            }
            _dsTkNguoiDungDungTay.Clear();

            // Chờ RỖNG. `Active` gồm cả Queued nên hàng đợi tự bắn phiên mới cũng bị vòng này nhìn thấy.
            var han = DateTime.UtcNow + HanChoDungHan;
            while (_services.Sessions.Active.Count > 0)
            {
                if (DateTime.UtcNow >= han)
                {
                    var loi = $"Chờ {HanChoDungHan.TotalSeconds:0}s mà phiên check đơn chưa dừng hẳn — KHÔNG "
                              + $"{ThuongHoaChuDau(tenViec)} (chạy lúc này là tranh hồ sơ + cổng cầu nối + khoá "
                              + "chạy với chính nó). Thử lại sau.";
                    _services.Log.Append(email, loi);
                    BusyStatus = loi;
                    return false;
                }

                BusyStatus = "Đang chờ phiên check đơn đóng trình duyệt và nhả cổng cầu nối...";
                // Nghe token huỷ: bấm "Đóng cửa sổ" ở giây thứ 3 phải thoát NGAY, không đứng đủ 30 giây rồi
                // mới báo một lý do sai.
                await Task.Delay(NhipHoiDungHan, ct);
            }

            UpdateSelectedSessionStatus();
        }

        // Lượt TRẢ trước có thể còn đang bay lên hub — chờ nó xong rồi mới xin, kẻo DELETE cũ xoá đúng dòng
        // hub sắp cấp cho ta.
        try { await _luotNhaKhoaCuoi; } catch { /* lượt nhả trước hỏng thì thôi, vẫn xin tiếp */ }

        // Khoá chạy XUYÊN MÁY — xin SAU khi mọi phiên đã dừng hẳn (chúng giữ đúng khoá đó và chỉ nhả trong
        // `finally` của chúng; xin sớm là hub cấp cho ta rồi lượt DELETE của chúng xoá đúng dòng vừa cấp).
        return await XinKhoaXemAsync(email, ct);
    }

    /// <summary>
    /// TRẢ hồ sơ: nhả khoá xuyên máy + chạy lại ĐÚNG TẬP phiên mà lượt mượn này đã dừng. <b>Đúng MỘT nơi làm
    /// việc này</b> — mọi đường "cửa sổ biến mất" phải gọi nó.
    /// <para>Idempotent: dọn tập + cờ NGAY đầu hàm nên nhiều đường cùng bắn tới cũng chỉ trả một lần.</para>
    /// <para>Tài khoản nào người dùng đã chủ động bấm Dừng trong lượt mượn này ⇒ chỉ nhả khoá, KHÔNG chạy lại
    /// tài khoản đó (chạy lại là cãi lại người dùng); những tài khoản còn lại của tập vẫn chạy lại — quyết định
    /// theo TỪNG tài khoản, không phải một cờ cho cả máy (NẶNG-A, 04/09/2026).</para>
    /// </summary>
    private void TraHoSo()
    {
        // PER-TÀI-KHOẢN: lọc đúng những tài khoản người dùng đã bảo dừng; phần còn lại vẫn chạy lại.
        var ds = _dsTkDaDung.Where(id => !_dsTkNguoiDungDungTay.Contains(id)).ToList();
        var boQua = _dsTkDaDung.Where(_dsTkNguoiDungDungTay.Contains).ToList();
        _dsTkDaDung.Clear();
        _dsTkNguoiDungDungTay.Clear();
        // ĐỌC RỒI XOÁ NGAY: mốc hoãn là của ĐÚNG lượt vừa xong. Để sót thì lượt mượn hồ sơ SAU đó (nút "Mở list
        // shop", một lượt KM…) cũng bị hoãn theo, không vì cái gì.
        var hoanToi = MocChayLaiCheckDonSauFlash;
        MocChayLaiCheckDonSauFlash = null;

        // Nhả khoá: NỐI ĐUÔI vào chuỗi thay vì bắn tự do. `TraHoSo` chạy đồng bộ trên UI thread (từ các nút)
        // nên không await được, mà thả tự do thì lượt DELETE có thể còn đang bay khi lượt MƯỢN kế xin khoá mới
        // — cùng (account_id, machine_id) nên nó xoá đúng dòng vừa cấp. `MuonHoSoAsync` await chuỗi này trước
        // khi xin, nên hai lượt không bao giờ chồng nhau mà UI vẫn không bị chặn.
        _luotNhaKhoaCuoi = NoiTiepNhaKhoa(_luotNhaKhoaCuoi);

        // Nói ra lý do KHÔNG chạy lại — bản trước im lặng, người vận hành chỉ thấy check đơn "tự chết".
        foreach (var id in boQua)
        {
            _services.Log.Append(_services.Accounts.GetById(id)?.Email ?? EditEmail,
                "Đã đóng cửa sổ — check đơn KHÔNG mở lại: bạn đã bấm Dừng tài khoản này trong lúc mượn hồ sơ.");
        }
        if (ds.Count == 0)
        {
            return;
        }

        foreach (var id in ds)
        {
            var email = _services.Accounts.GetById(id)?.Email ?? EditEmail;

            // HOÃN TỚI MỐC — chỉ lượt GÓI FLASH đặt mốc này, và mốc là CUỐI KHE NGHỈ mà gói vừa mượn (xem
            // `NhipLichFlashAsync`). Gói xong sớm mà chạy lại check đơn NGAY là mở lại đúng shop 1–2 lần thứ
            // hai trong vòng ít phút — đúng mẫu đã làm Shopee bật verify/traffic — và còn làm trôi nhịp vòng
            // check đơn đi mỗi lần có một gói flash.
            // MỘT lượt đọc đồng hồ cho cả phép so lẫn khoảng chờ — và đọc qua CHỖ NỐI (30/08/2026): bản đọc
            // `DateTime.Now` trần làm 5 test FlashHoanChayLai thành bom hẹn giờ, tự đỏ ngay sau 22:00 đêm 29/08.
            var bayGioHoan = DongHoHoanChayLai();
            if (hoanToi is DateTime moc && moc > bayGioHoan)
            {
                // ══ KHÔNG GHI ĐÈ một lượt hoãn ĐANG SỐNG của cùng tài khoản bằng mốc MUỘN HƠN (NẶNG-3,
                //    04/09/2026). Ca thật: người dùng bấm ▶ Chạy giữa gói flash ⇒ `LenhHubChayCheckDon` xếp một
                //    lượt hoãn mốc-BÂY-GIỜ (check đơn lên NGAY khi shop hiện tại xong). Gói xong, `TraHoSo` này
                //    xếp lượt mốc CUỐI KHE (muộn hơn) ⇒ bản trước ghi đè, lượt hub `RutLuotHoan` trả false rồi
                //    RÚT LUI im lặng ⇒ check đơn trễ tới tận cuối khe. Bridge là tài nguyên chung nên mốc SỚM
                //    NHẤT thắng (khớp phép đọc `Min` ở `MocHoanChayLaiDangCho`): chỉ thay khi mốc mới SỚM HƠN. ══
                DateTime? mocDangCho;
                lock (_mocHoanChayLaiDangCho)
                {
                    mocDangCho = _mocHoanChayLaiDangCho.TryGetValue(id, out var mc) ? mc : (DateTime?)null;
                }
                //   • Dùng `>` NGHIÊM (không `>=`): mốc BẰNG NHAU phải rơi xuống đường thay-bằng-danh-tính sẵn có
                //     — đó là ca "gói thứ hai mở trong khe hoãn vừa xong, lên lịch lại CÙNG mốc" (bất biến
                //     đợt-1-lượt-3, test `LuotHoanThuHai_CungTaiKhoan…`). Ca hub cần chặn có mốc CUỐI KHE luôn
                //     MUỘN HƠN NGHIÊM mốc-bây-giờ của lượt hub, nên `>` là đủ và không đụng bất biến kia.
                if (DangHoanChayLai(id) && mocDangCho is DateTime mocCu && moc > mocCu)
                {
                    _services.Log.Append(email,
                        $"Đã đóng cửa sổ — GIỮ lượt hoãn chạy lại check đơn đang chờ tới {mocCu:HH:mm} (một lệnh "
                        + $"▶ Chạy trước đó đã xếp check đơn lên sớm hơn mốc {moc:HH:mm} mà lượt này định đặt).");
                    continue;
                }

                _services.Log.Append(email,
                    $"Đã đóng cửa sổ — HOÃN chạy lại check đơn tới {moc:HH:mm} (đúng cuối KHE NGHỈ mà gói Flash "
                    + "Sale vừa mượn, để nhịp vòng check đơn giữ nguyên). Bấm Dừng trong lúc chờ là HUỶ hẳn "
                    + "lượt hoãn này.");
                _ctsHoanChayLai ??= new System.Threading.CancellationTokenSource();
                // DANH TÍNH LƯỢT — ghi đè lượt cũ (nếu còn) của cùng tài khoản: lượt cũ tỉnh dậy sẽ tự thấy mình
                // không còn hiện hành mà rút lui, xem xmldoc `_dsTkDangHoanChayLai`.
                var luotId = Guid.NewGuid();
                lock (_khoaHoan) { _dsTkDangHoanChayLai[id] = luotId; }
                // CÔNG BỐ MỐC ĐANG CHỜ cho nhịp flash biết mà đừng mở gói đè lên — xem
                // `MocHoanChayLaiDangCho`. Đặt TRƯỚC lượt phóng task: task có thể chạy tới nhánh xoá ngay trong
                // cùng tick (khoảng chờ đã ở quá khứ ở đường test), và gán sau là dựng lại một mốc đã tới.
                DatMocHoanDangCho(id, moc);
                LuotHoanGanNhat = ChayLaiCheckDonSauKhiChoAsync(id, luotId, email, moc - bayGioHoan, _ctsHoanChayLai.Token);
                continue;
            }

            _services.Log.Append(email, "Đã đóng cửa sổ — chạy lại check đơn.");
            _services.Sessions.Start(id);
        }
        UpdateSelectedSessionStatus();
    }

    /// <summary>
    /// <b>CTS của các lượt HOÃN chạy lại check đơn đang treo.</b> <c>null</c> = không có lượt nào.
    /// <para><b>⚠ KHÔNG PHẢI CỜ THỪA</b> (phản biện 29/08/2026, NẶNG): lượt hoãn ngủ tới <b>55 phút</b>. Không
    /// có đường huỷ thì người dùng bấm "Dừng tất cả" lúc 21:10, tới 22:00 task tỉnh dậy và <b>tự mở Brave</b> —
    /// phần mềm phủ quyết thao tác của người dùng sau 50 phút, với đúng một dòng log không hề nhắc tới cú Dừng.
    /// Đây là hành vi MỚI do bản vá 28/08 sinh ra (trước đó <see cref="TraHoSo"/> chạy lại NGAY, trong cùng
    /// tick với cú Dừng, nên tập <c>_dsTkNguoiDungDungTay</c> đủ để chặn).</para>
    /// </summary>
    private System.Threading.CancellationTokenSource? _ctsHoanChayLai;

    /// <summary>
    /// <b>Lượt hoãn ĐANG SỐNG của từng tài khoản</b>: <c>accountId → danh tính lượt</c> (một <see cref="Guid"/> cấp
    /// lúc lên lịch). Sống từ lúc <see cref="TraHoSo"/> lên lịch cho tới <b>lối ra THẬT</b> của lượt — kể cả suốt
    /// quãng NHƯỜNG (tới 90') — chứ không phải tới lúc "tới mốc".
    /// <para><b>Vì sao là danh tính chứ không phải tập id (sửa lượt 3, 04/09/2026 — phản biện dựng được 2 ca):</b>
    /// (1) bản trước <c>Remove(id)</c> ngay khi tới mốc rồi mới vào vòng nhường ⇒ suốt 90' đó tài khoản KHÔNG còn
    /// trong tập, <c>StopSelected</c> không thấy để huỷ ⇒ người dùng bấm Dừng mà lượt vẫn tỉnh dậy mở Brave;
    /// (2) một gói mở TRONG khe hoãn xong lại lên lịch lượt hoãn THỨ HAI cho cùng tài khoản, tới mốc cả hai cùng
    /// tỉnh, bên thua <c>Remove</c> trả false rồi XOÁ mốc mà bên thắng đang dựa vào. Với danh tính: lượt mới
    /// THAY lượt cũ (ghi đè cùng khoá), lượt cũ tỉnh dậy thấy mình không còn hiện hành thì <b>rút lui im lặng,
    /// không đụng mốc</b>; id nằm trong từ điển tới tận lúc chủ lượt mở check đơn (hoặc bị huỷ).</para>
    /// <para>Đọc/ghi từ CẢ UI thread (<see cref="TraHoSo"/>, <c>StopSelected</c>) lẫn continuation thread-pool
    /// (lượt hoãn) ⇒ mọi truy cập đi qua <see cref="_khoaHoan"/>.</para>
    /// </summary>
    private readonly Dictionary<long, Guid> _dsTkDangHoanChayLai = new();

    /// <summary>Khoá cho <see cref="_dsTkDangHoanChayLai"/>. KHÔNG lồng với khoá của <c>_mocHoanChayLaiDangCho</c>.</summary>
    private readonly object _khoaHoan = new();

    /// <summary>Lượt <paramref name="luotId"/> có còn là lượt hoãn HIỆN HÀNH của tài khoản không (chưa bị lượt mới
    /// hơn thay thế, chưa bị huỷ)?</summary>
    private bool LaLuotHoanHienHanh(long accountId, Guid luotId)
    {
        lock (_khoaHoan)
        {
            return _dsTkDangHoanChayLai.TryGetValue(accountId, out var hienHanh) && hienHanh == luotId;
        }
    }

    /// <summary>Chủ lượt rút khỏi từ điển ở lối ra THẬT. Chỉ rút khi vẫn là lượt hiện hành — lượt đã bị thay thế
    /// không được xoá dấu của lượt mới. Trả <c>true</c> = đã rút (đúng là chủ).</summary>
    private bool RutLuotHoan(long accountId, Guid luotId)
    {
        lock (_khoaHoan)
        {
            if (_dsTkDangHoanChayLai.TryGetValue(accountId, out var hienHanh) && hienHanh == luotId)
            {
                _dsTkDangHoanChayLai.Remove(accountId);
                return true;
            }
            return false;
        }
    }

    /// <summary>CHỖ NỐI TEST: tài khoản này còn lượt hoãn đang sống không (kể cả đang nhường).</summary>
    internal bool DangHoanChayLai(long accountId)
    {
        lock (_khoaHoan) { return _dsTkDangHoanChayLai.ContainsKey(accountId); }
    }

    /// <summary>Task của lượt hoãn gần nhất — <b>chỗ nối TEST</b>, để bài test <c>await</c> được thay vì ngủ.
    /// <c>null</c> = chưa có lượt hoãn nào trong phiên VM này.</summary>
    internal Task? LuotHoanGanNhat { get; private set; }

    /// <summary>
    /// Chỗ nối CHẠY LẠI một phiên check đơn sau khi hết biên an — production gọi
    /// <c>_services.Sessions.Start</c>; test rót bản chỉ GHI LẠI id.
    /// <para><b>Bắt buộc phải có chỗ nối:</b> bản production PHÓNG TRÌNH DUYỆT THẬT, nên không test nào được
    /// phép chạm vào nó — mà mệnh đề cần khoá lại đúng là <i>"có gọi hay KHÔNG gọi"</i>.</para>
    /// </summary>
    /// <remarks>Dựng LƯỜI ở getter (không phải field initializer): thân lambda đọc <c>_services</c>, mà khởi
    /// tạo trường thì không được chạm tới thành viên instance (CS0236).</remarks>
    internal Action<long> ChayLaiPhienCheckDon
    {
        get => _chayLaiPhienCheckDon ??= id => _services.Sessions.Start(id);
        set => _chayLaiPhienCheckDon = value;
    }

    private Action<long>? _chayLaiPhienCheckDon;

    /// <summary>
    /// CHỖ NỐI TEST: mô phỏng "lượt mượn hồ sơ này đã tạm dừng phiên của <paramref name="ids"/>" rồi TRẢ hồ sơ.
    /// <para>Đường thật đi qua <c>MuonHoSoAsync</c> (dừng phiên + xin khoá xuyên máy + phóng Brave) nên không
    /// bài test nào dựng nổi; nhánh cần đo thì nằm trọn trong <see cref="TraHoSo"/>.</para>
    /// </summary>
    internal void TraHoSoChoTest(params long[] ids)
    {
        foreach (var id in ids) { _dsTkDaDung.Add(id); }
        TraHoSo();
    }

    /// <summary>
    /// CHỖ NỐI TEST: đặt <see cref="MocChayLaiCheckDonSauFlash"/> như một gói flash vừa mượn khe nghỉ tới mốc đó.
    /// <para>Đường thật là <see cref="NhipLichFlashAsync"/>, mà nhịp ấy đi qua khoá xuyên máy + phóng Brave nên
    /// không bài test nào chạm tới được; nhánh cần đo (hoãn hay chạy lại ngay) thì nằm trọn trong
    /// <see cref="TraHoSo"/>. Từ 01/09/2026 <see cref="ChotLuotFlash"/> KHÔNG còn đặt mốc này, nên test không
    /// mượn được nó nữa.</para>
    /// </summary>
    internal void DatMocChayLaiSauFlashChoTest(DateTime? moc) => MocChayLaiCheckDonSauFlash = moc;

    /// <summary>
    /// <b>HUỶ mọi lượt hoãn chạy lại check đơn đang treo.</b> Gọi từ CẢ BA đường: <c>Dispose</c> (thoát app),
    /// <c>StopAllAsync</c>, và <c>StopSelected</c> khi tập tick giao với tập đang hoãn.
    /// <para>Idempotent: dọn CTS + tập NGAY đầu hàm nên nhiều đường cùng bắn tới cũng chỉ huỷ một lần.</para>
    /// </summary>
    /// <param name="lyDo">Nói RÕ vì sao huỷ, để dòng nhật ký nối được với cú bấm của người dùng.</param>
    internal void HuyLuotHoanChayLaiCheckDon(string lyDo)
    {
        var cts = _ctsHoanChayLai;
        List<long> ds;
        lock (_khoaHoan)
        {
            ds = _dsTkDangHoanChayLai.Keys.ToList();
            _dsTkDangHoanChayLai.Clear();
        }
        _ctsHoanChayLai = null;
        // Không còn lượt hoãn nào chờ ⇒ nhịp flash thôi bị kẹp khe giả định. Để sót là mọi gói sau đó bị chặn
        // bởi một mốc của lượt hoãn ĐÃ HUỶ — vĩnh viễn, vì không còn ai xoá nó nữa.
        XoaMoiMocHoanDangCho();
        if (cts is null) { return; }

        try { cts.Cancel(); } catch { /* bỏ qua */ }
        try { cts.Dispose(); } catch { /* bỏ qua */ }

        foreach (var id in ds)
        {
            var email = _services.Accounts.GetById(id)?.Email ?? EditEmail;
            _services.Log.Append(email,
                $"ĐÃ HUỶ lượt hoãn chạy lại check đơn ({lyDo}) — sẽ KHÔNG tự mở lại trình duyệt. "
                + "Bấm ▶ Chạy khi nào muốn check đơn tiếp.");
        }
    }

    /// <summary>
    /// <b>HUỶ lượt hoãn chạy lại check đơn của RIÊNG một tài khoản.</b> Khác
    /// <see cref="HuyLuotHoanChayLaiCheckDon"/> (quét sạch MỌI tài khoản) — dùng cho lệnh hub <c>stop-all</c>
    /// per-tài-khoản, không được đụng lượt hoãn của tài khoản khác trên cùng máy.
    /// <para><b>Vì sao chỉ cancel CTS khi KHÔNG còn lượt nào khác:</b> <see cref="_ctsHoanChayLai"/> dùng CHUNG
    /// cho mọi lượt hoãn, cancel bừa là giết luôn lượt của tài khoản khác. Lượt của <paramref name="accountId"/>
    /// nếu đang ngủ mà CTS chưa cancel thì tự rút lui khi tỉnh: nó đã bị rút khỏi
    /// <see cref="_dsTkDangHoanChayLai"/> nên <see cref="LaLuotHoanHienHanh"/> trả false ⇒ không mở gì.</para>
    /// </summary>
    private void HuyLuotHoanMotTaiKhoan(long accountId, string lyDo)
    {
        bool coLuot;
        bool conLuotKhac;
        lock (_khoaHoan)
        {
            coLuot = _dsTkDangHoanChayLai.Remove(accountId);
            conLuotKhac = _dsTkDangHoanChayLai.Count > 0;
        }
        // Xoá mốc của RIÊNG id — phép đọc `MocHoanChayLaiDangCho` là Min mọi tài khoản, để sót là gói sau bị
        // một mốc ma của lượt đã huỷ chặn.
        DatMocHoanDangCho(accountId, null);
        if (!coLuot) { return; }

        // Chỉ khi không còn ai chờ mới cancel CTS chung — xem xmldoc.
        if (!conLuotKhac)
        {
            var cts = _ctsHoanChayLai;
            _ctsHoanChayLai = null;
            if (cts is not null)
            {
                try { cts.Cancel(); } catch { /* bỏ qua */ }
                try { cts.Dispose(); } catch { /* bỏ qua */ }
            }
        }

        var email = _services.Accounts.GetById(accountId)?.Email ?? EditEmail;
        _services.Log.Append(email,
            $"ĐÃ HUỶ lượt hoãn chạy lại check đơn ({lyDo}) — sẽ KHÔNG tự mở lại trình duyệt. "
            + "Bấm ▶ Chạy khi nào muốn check đơn tiếp.");
    }

    /// <summary>
    /// Chờ hết <paramref name="doi"/> rồi mới chạy lại phiên check đơn của <paramref name="accountId"/>.
    /// <para><b>Fire-and-forget có chủ đích</b> — <see cref="TraHoSo"/> chạy đồng bộ trên UI thread (nó được gọi
    /// từ cả các nút bấm lẫn khối <c>finally</c>), nên không await được. Nuốt mọi lỗi: đây là đường dọn dẹp, nó
    /// không có quyền giết cái gì.</para>
    /// <para><b>HAI cửa, cần cả hai</b> — token và tập <see cref="_dsTkDangHoanChayLai"/>. Token cắt lượt chờ
    /// ngay khi người dùng bấm Dừng; tập là cửa kiểm LẠI ngay TRƯỚC <c>Sessions.Start</c> cho ca token đã bị
    /// thay mới (một lượt mượn hồ sơ khác chen vào giữa) — thiếu nó thì vẫn còn một khe để task cũ mở Brave.</para>
    ///
    /// <para><b>CỬA THỨ BA, sau hai cửa trên: KHÔNG ĐÈ LÊN GÓI FLASH ĐANG CHẠY</b> (03/09/2026, sổ
    /// P-20260903-1). Tới mốc mà gói còn chạy thì lượt này bật <see cref="_nhuongChoCheckDon"/> (gói thôi bắt
    /// đầu shop mới) rồi NẰM CHỜ cho tới khi gói xong, trần
    /// <see cref="KmLichTuDong.TranChoNhuongFlash"/>. Trước bản này lượt hoãn mở phiên check đơn thẳng vào giữa
    /// gói: cổng cầu nối 47821 đã có chủ ⇒ <c>HttpListenerException</c> ⇒ lượt dọn hồ sơ đóng luôn 10 cửa sổ
    /// Brave của gói ⇒ shop đang dở HỎNG, và vòng check đơn cũng hỏng theo (đo thật 18:52 / 21:25 / 22:01 ngày
    /// 03/09/2026).</para>
    /// <para><b>Quá trần thì KHÔNG mở check đơn</b> — thà thiếu check đơn một khe còn hơn đè; dòng nhật ký ở đó
    /// nói rõ để người dùng bấm ▶ Chạy.</para>
    /// </summary>
    private async Task ChayLaiCheckDonSauKhiChoAsync(
        long accountId, Guid luotId, string email, TimeSpan doi, System.Threading.CancellationToken ct)
    {
        try
        {
            if (doi > TimeSpan.Zero) { await ChoTruocKhiChayLaiCheckDon(doi, ct); }

            // CỬA CHỦ LƯỢT — KHÔNG rút khỏi từ điển ở đây. Bản trước `Remove(id)` tại chỗ này rồi mới vào vòng
            // nhường: suốt quãng nhường (tới 90') tài khoản không còn trong tập ⇒ "Dừng đã chọn" không thấy để
            // huỷ ⇒ người dùng bảo dừng mà lượt vẫn tỉnh dậy mở Brave (phản biện 04/09/2026 dựng được, NẶNG).
            // Id phải nằm trong từ điển tới tận lối ra thật — xem xmldoc `_dsTkDangHoanChayLai`.
            if (ct.IsCancellationRequested)
            {
                BaoLuotHoanBiHuy(accountId, luotId, email);
                return;
            }
            if (!LaLuotHoanHienHanh(accountId, luotId))
            {
                // Một lượt hoãn MỚI HƠN đã thay chỗ (gói mở trong khe hoãn vừa xong và lên lịch lại). Lượt này
                // KHÔNG còn là chủ mốc/chủ từ điển ⇒ rút lui im lặng, KHÔNG đụng gì — bản trước rơi vào nhánh
                // "ĐÃ BỊ HUỶ" và xoá mốc của lượt đang nhường (phản biện 04/09/2026).
                _services.Log.Append(email,
                    "Lượt hoãn chạy lại check đơn đã được THAY bằng lượt hoãn mới hơn của cùng tài khoản — lượt cũ "
                    + "rút lui, không mở gì.");
                return;
            }

            // ⚠ MỐC **CHƯA** ĐƯỢC XOÁ Ở ĐÂY. ĐỪNG DỜI LƯỢT XOÁ LÊN TRƯỚC VÒNG NHƯỜNG.
            //
            // Bản đầu 03/09/2026 xoá mốc ngay tại dòng này rồi mới vào `DaNhuongXongChoGoiFlashAsync`. Ca hỏng
            // (phản biện dựng được): lượt hoãn L1 tới mốc trong lúc gói G còn chạy ⇒ bật cờ, XOÁ mốc, nằm chờ ⇒
            // G thấy cờ, nhường xong, `finally` hạ `_dangChayFlashTuDong` ⇒ **nhịp 60 giây bắn TRƯỚC nhịp chờ
            // 10 giây**: không phiên nào chạy, mốc đã null ⇒ khe giả định lại là trọn 120' ⇒ MỞ GÓI G2 và RESET
            // cờ nhường ⇒ L1 tỉnh dậy thấy vẫn "đang chạy" nhưng cờ đã bị tắt ⇒ G2 không bao giờ nhường ⇒ L1
            // ngồi hết trần 90' rồi BỎ LUÔN check đơn. Đúng cái hỏng mà cả cơ chế này sinh ra để chặn.
            //
            // Từ lượt 3 (04/09/2026) MỌI dọn dẹp (rút khỏi từ điển + xoá mốc) gom về ĐÚNG HÀM NÀY, ở lối ra thật:
            // mở được · quá trần · bị huỷ. `DaNhuongXongChoGoiFlashAsync` không đụng mốc nữa — hai chỗ cùng dọn
            // một thứ là hai chỗ có thể dọn nhầm của lượt khác.
            if (!await DaNhuongXongChoGoiFlashAsync(email, ct))
            {
                // QUÁ TRẦN — hàm kia đã ghi nhật ký. Lượt kết thúc mà KHÔNG mở: dọn dấu của chính mình.
                if (RutLuotHoan(accountId, luotId)) { DatMocHoanDangCho(accountId, null); }
                return;
            }

            // KIỂM LẠI SAU KHI NHƯỜNG: trong lúc chờ, người dùng có thể đã bấm Dừng (token huỷ ⇒ vòng nhường ném
            // và không tới đây — nhưng vẫn kiểm cho chắc), hoặc một lượt mới hơn đã thay chỗ (không mở đúp).
            if (ct.IsCancellationRequested)
            {
                BaoLuotHoanBiHuy(accountId, luotId, email);
                return;
            }
            if (!RutLuotHoan(accountId, luotId))
            {
                _services.Log.Append(email,
                    "Lượt hoãn chạy lại check đơn đã được THAY bằng lượt hoãn mới hơn trong lúc nhường — lượt cũ "
                    + "rút lui, không mở gì.");
                return;
            }
            DatMocHoanDangCho(accountId, null);

            _services.Log.Append(email, "Hết biên an sau lượt Flash Sale — chạy lại check đơn.");
            ChayLaiPhienCheckDon(accountId);
            UpdateSelectedSessionStatus();
        }
        catch (OperationCanceledException)
        {
            BaoLuotHoanBiHuy(accountId, luotId, email);
        }
        catch (Exception ex)
        {
            if (RutLuotHoan(accountId, luotId)) { DatMocHoanDangCho(accountId, null); }
            _services.Log.Append(email, "Chạy lại check đơn sau biên an Flash Sale lỗi: " + ex.ToString());
        }
    }

    /// <summary>Lối ra "ĐÃ BỊ HUỶ" của một lượt hoãn: dọn dấu của CHÍNH NÓ (chỉ khi còn là chủ — lượt đã bị thay
    /// không được xoá mốc của lượt mới) + một dòng nhật ký. <see cref="HuyLuotHoanChayLaiCheckDon"/> có thể đã dọn
    /// trước — mọi bước ở đây đều idempotent.</summary>
    private void BaoLuotHoanBiHuy(long accountId, Guid luotId, string email)
    {
        if (RutLuotHoan(accountId, luotId)) { DatMocHoanDangCho(accountId, null); }
        _services.Log.Append(email,
            "Lượt hoãn chạy lại check đơn ĐÃ BỊ HUỶ trong lúc chờ — KHÔNG mở lại trình duyệt.");
    }

    /// <summary>
    /// <b>NHƯỜNG CHỖ: gói flash / lát cắt cầu nối đang chạy thì bật cờ rồi CHỜ cho tới khi nó xong.</b> Trả
    /// <c>true</c> = đường đã sạch, nơi gọi được mở check đơn; <c>false</c> = <b>KHÔNG được mở</b> (quá trần
    /// <see cref="KmLichTuDong.TranChoNhuongFlash"/>).
    ///
    /// <para>Không có ai đang chạy ⇒ trả <c>true</c> NGAY, không ghi dòng nào: đó là đường đi thường ngày (gói
    /// xong sớm rồi mới tới mốc hoãn), và một dòng log cho mỗi lượt bình thường thì làm chìm đúng dòng cần đọc.</para>
    ///
    /// <para><b>Đúng MỘT dòng khi phải chờ</b>, không phải một dòng mỗi nhịp: nhịp là 10 giây, trần là 90 phút ⇒
    /// 540 dòng cho một lần chờ.</para>
    ///
    /// <para><b>Cờ CỐ Ý không hạ ở lối ra BÌNH THƯỜNG</b> (mở được / quá trần). Nó do lượt/gói KẾ TIẾP reset
    /// ngay trước khi chạy (xem <see cref="_nhuongChoCheckDon"/>): hạ ở đây là mở một khe cho gói vừa nhường
    /// tiếp tục lấy shop mới trong lúc phiên check đơn đang lên — đúng cái vừa tránh. <b>Lối ra vì HUỶ/ngoại lệ
    /// thì NGƯỢC LẠI: phải hạ</b> — không còn ai chờ nữa, để cờ treo là gói đang chạy dừng sau shop kế cho một
    /// lượt check đơn đã chết, và không dòng nào nói ra.</para>
    ///
    /// <para><b>Cờ được đặt lại MỖI NHỊP, không phải một lần</b> (sửa vòng 2, 03/09/2026): một gói MỚI mở giữa
    /// lúc nhường sẽ RESET cờ ngay trước khi chạy, nên bản đặt-một-lần để lại đúng trạng thái "vẫn có người
    /// chờ mà chẳng gói nào biết". Đặt lại mỗi 10 giây thì gói mới nhất cùng lắm chạy thêm một shop.</para>
    /// <para><b>Hàm này KHÔNG đụng mốc hoãn / từ điển lượt</b> (từ lượt 3, 04/09/2026): dọn dẹp thuộc về nơi gọi
    /// (<c>ChayLaiCheckDonSauKhiChoAsync</c>) ở lối ra thật, vì chỉ nơi đó biết mình có còn là CHỦ lượt hay không.</para>
    /// </summary>
    /// <param name="ct">Token của lượt hoãn — <c>Dừng</c> giữa lúc nhường phải thoát ra nhánh "ĐÃ BỊ HUỶ".</param>
    private async Task<bool> DaNhuongXongChoGoiFlashAsync(string email, System.Threading.CancellationToken ct)
    {
        if (!DangChayFlashHoacCauNoi())
        {
            return true;
        }

        // TÊN CÁI ĐANG GIỮ CẦU NỐI — không phải lúc nào cũng là gói flash: `DangChayFlashHoacCauNoi` gồm cả
        // `_bridgeRunning`, tức "Mở list shop", một lượt KM/Combo bấm tay, một vòng KM tự động 2h sáng. Bản đầu
        // nói cứng "Gói Flash Sale đang chạy" cho mọi ca ⇒ nhật ký khai một gói flash không tồn tại, và người
        // đọc đi tìm nó trong đúng cái file nhật ký đang bảo họ là có.
        var moTaViec = MoTaViecDangGiuCauNoi();
        _nhuongChoCheckDon = true;
        _services.Log.Append(email,
            $"{moTaViec} đang giữ cầu nối — chờ shop hiện tại xong rồi mới chạy lại check đơn (không đè). "
            + $"Chờ tối đa {KmLichTuDong.TranChoNhuongFlash.TotalMinutes:0} phút.");

        var het = DongHoHoanChayLai() + KmLichTuDong.TranChoNhuongFlash;
        try
        {
            while (DangChayFlashHoacCauNoi())
            {
                // ĐẶT LẠI CỜ MỖI NHỊP — xem đoạn "Cờ được đặt lại MỖI NHỊP" ở xmldoc. Một gói mở giữa chừng vừa
                // reset cờ về false; không đặt lại thì nó chạy trọn danh sách trong lúc lượt này ngồi chờ.
                _nhuongChoCheckDon = true;

                ct.ThrowIfCancellationRequested();

                if (DongHoHoanChayLai() >= het)
                {
                    _services.Log.Append(email,
                        $"QUÁ HẠN CHỜ {ThuongHoaChuDau(moTaViec)} "
                        + $"({KmLichTuDong.TranChoNhuongFlash.TotalMinutes:0} phút) — KHÔNG mở check đơn để "
                        + "không đè lên lượt đang chạy. Bấm ▶ Chạy khi cần. ĐÂY KHÔNG PHẢI LỖI: một lượt kẹt "
                        + "lâu bất thường thì thà thiếu check đơn một khe còn hơn hai luồng tranh cùng một hồ "
                        + "sơ Brave và cùng cổng cầu nối.");
                    return false;
                }

                await ChoNhipNhuongFlash(ct);
            }
        }
        catch
        {
            // HUỶ (hoặc bất kỳ ngoại lệ nào) GIỮA LÚC NHƯỜNG: không còn ai chờ ⇒ hạ cờ + xoá mốc rồi ném tiếp
            // cho nhánh "ĐÃ BỊ HUỶ" của nơi gọi ghi nhật ký. Bỏ khối này là để lại một cờ nhường TREO — gói
            // đang chạy dừng sau shop kế cho một lượt check đơn đã chết. (Mốc do nơi gọi dọn — nó biết mình có
            // còn là chủ lượt hay không.)
            _nhuongChoCheckDon = false;
            throw;
        }

        return true;
    }

    /// <summary>
    /// CỤM DANH TỪ nói ĐÚNG cái đang giữ cầu nối — <c>"Gói Flash Sale"</c> hoặc <c>"Lượt 'KM của Shop'"</c>.
    /// Viết hoa chữ đầu; nơi cần chữ thường thì bọc <see cref="ThuongHoaChuDau"/>.
    ///
    /// <para><b>Vì sao không nói cứng "gói Flash Sale"</b> (phản biện vòng 2, 03/09/2026):
    /// <see cref="DangChayFlashHoacCauNoi"/> gồm cả <c>_bridgeRunning</c>, tức "Mở list shop", một lượt
    /// KM/Combo bấm tay, hay vòng KM tự động 2h sáng. Câu cứng khai ra một gói flash không tồn tại, và người
    /// đọc đi tìm nó trong chính cái nhật ký đang bảo họ là có.</para>
    ///
    /// <para>Nguồn là <see cref="TenLatCatDangChay"/> (tên việc THẬT của lát cắt). <c>null</c> = không lát cắt
    /// nào đang chạy ⇒ cửa duy nhất còn lại là <c>_dangChayFlashTuDong</c> ⇒ đúng là gói flash.</para>
    /// </summary>
    private string MoTaViecDangGiuCauNoi()
        => LaGoiFlashDangGiuCauNoi() ? "Gói Flash Sale" : $"Lượt '{TenLatCatDangChay}'";

    /// <summary>Cái đang giữ cầu nối có phải một GÓI FLASH SALE không (để áp luật ưu tiên flash và để gọi đúng
    /// tên trong nhật ký). <c>TenLatCatDangChay</c> null = không lát cắt nào chạy ⇒ cửa duy nhất còn lại là
    /// <c>_dangChayFlashTuDong</c> ⇒ đúng là gói flash. Một luật, dùng chung
    /// <see cref="MoTaViecDangGiuCauNoi"/> và <see cref="LyDoTuChoiLenhVongTuHub"/> — hai bản chép tay là hai
    /// bản trôi lệch.</summary>
    private bool LaGoiFlashDangGiuCauNoi()
        => TenLatCatDangChay is not string ten || ten == TenViecFlash;

    /// <summary>
    /// Câu từ chối khi người dùng bấm một nút MỞ PHIÊN trong lúc lát cắt cầu nối còn chạy. Một câu dùng chung
    /// cho cả ba đường mở phiên (<see cref="Run"/>, <see cref="RunSelected"/>, <see cref="TruyCapTk"/>).
    /// </summary>
    internal const string CauTuChoiViLatCatDangChay =
        "Đang mở list shop / chạy thử — đợi xong hoặc bấm 'Đóng cửa sổ' rồi hãy mở phiên.";

    /// <summary>
    /// Chốt cửa: lát cắt cầu nối đang chạy thì MỌI đường mở phiên
    /// production phải lùi. Trả <c>true</c> = đã từ chối (chỗ gọi return ngay).
    /// <para><b>Vì sao bắt buộc, và vì sao ba đường chứ không một</b> (phản biện 18/08/2026): thiếu chốt này thì
    /// một khoá chạy có HAI CHỦ — lát cắt và phiên production cùng giữ lease của một tài khoản, mà
    /// <c>ReleaseAccountLease</c> là DELETE thẳng theo (tài khoản, máy) chứ không đếm chủ, nên lượt nhả của lát
    /// cắt XOÁ luôn lease của phiên production đang chạy. Chưa kể hai tiến trình Chromium cùng một hồ sơ.</para>
    /// </summary>
    private bool DaTuChoiViLatCatDangChay(string logSource)
    {
        if (!_bridgeRunning)
        {
            return false;
        }

        _services.Log.Append(logSource, CauTuChoiViLatCatDangChay);
        BusyStatus = CauTuChoiViLatCatDangChay;
        return true;
    }

    /// <summary>Nhãn nguồn log cho các thông báo cấp-BATCH (không thuộc một shop cụ thể) — ghi file &amp; phân
    /// biệt với log per-account (per-account dùng email của shop).</summary>
    private const string BatchLogSource = "Hàng loạt";

    // ══════════ LỆNH NGƯỜI DÙNG BẤM TRÊN HUB — MỘT CỬA VÀO, ƯU TIÊN FLASH (03/09/2026) ══════════
    //
    // Trước bản này luồng poll heartbeat gọi THẲNG `Sessions.Start`/`Sessions.Stop`, tức đi VÒNG qua mọi cửa từ
    // chối của app: từ hub mở được phiên check đơn ĐÈ LÊN một gói Flash Sale đang chạy (cùng hồ sơ Brave, cùng
    // cổng cầu nối 47821 — đo thật 03/09/2026), và "Dừng" từ hub không tắt nổi lượt đang chạy.
    //
    // Nay mọi lệnh của NGƯỜI DÙNG đi qua đúng một cửa: `AppServices.LenhTuHub` → `ThiHanhLenhTuHub` (marshal về
    // UI thread) → `XuLyLenhTuHub` (luật). Riêng `stop` giữ đường cũ ở `OrdersModuleHost.Mirror` — nó là công cụ
    // của tự động hoá khung xuyên máy, không phải nút của người dùng.

    /// <summary>
    /// LUẬT ƯU TIÊN FLASH, vế "không ai được chạy ĐÈ lên flash" (người dùng chốt 03/09/2026).
    /// <para>Vế còn lại — flash KHÔNG được giết flow khác giữa chừng — nằm ở chính chỗ dùng câu này: lát cắt
    /// KHÁC đang chạy thì cả ba lệnh vòng (kể cả <c>run-flash</c>) đều lùi bằng
    /// <see cref="CauTuChoiViLatCatDangChay"/>, vì cắt ngang giữa shop để lại chương trình rác trên Shopee.</para>
    /// </summary>
    internal const string CauTuChoiViUuTienFlash =
        "Flash Sale đang chạy — ưu tiên flash, thử lại sau khi gói xong.";

    /// <summary>Lý do phải TỪ CHỐI một lệnh chạy vòng từ hub, hoặc <c>null</c> = đường sạch, chạy được.</summary>
    private string? LyDoTuChoiLenhVongTuHub()
    {
        if (!DangChayFlashHoacCauNoi()) { return null; }
        return LaGoiFlashDangGiuCauNoi() ? CauTuChoiViUuTienFlash : CauTuChoiViLatCatDangChay;
    }

    /// <summary>Nhãn NGƯỜI ĐỌC của một hành động hub — phải khớp <c>DispatchViewLogic.OrdersActionLabel</c> bên
    /// hub để nhật ký máy và nút trên web gọi cùng một tên.</summary>
    internal static string NhanLenhTuHub(string action) => action switch
    {
        HanhDongLenhTuHub.Chay => "▶ Chạy",
        // "✖ Dừng" (không phải "Dừng tất cả"): lệnh này dừng đúng MỘT tài khoản, không phải cả máy — người dùng
        // chốt 04/09/2026. Khớp `DispatchViewLogic.OrdersActionLabel(StopAll)` bên hub.
        HanhDongLenhTuHub.DungTatCa => "✖ Dừng",
        HanhDongLenhTuHub.ChayKm => "▶ KM",
        HanhDongLenhTuHub.ChayCombo => "▶ Combo",
        HanhDongLenhTuHub.ChayFlash => "▶ Flash",
        _ => action,
    };

    /// <summary>
    /// Hạn LUỒNG POLL đứng đợi UI thread xử xong một lệnh hub. Thân lệnh chỉ dựng tham số + phóng lượt (KHÔNG
    /// chờ lượt chạy) nên bình thường là vài mili-giây; hạn này chỉ để một UI thread kẹt không kéo theo cả nhịp
    /// heartbeat đứng hình vô hạn — thà ack 'failed' và để người dùng bấm lại.
    /// <para><b>5 giây, hạ từ 20 (04/09/2026):</b> <c>ExecuteOrdersCommand</c> ở luồng poll gọi hàm này ĐỒNG BỘ
    /// cho TỪNG lệnh trong lô, nên trần cũ 20s × một lô nhiều lệnh (Dừng + 3 vòng) là cả phút nhịp heartbeat
    /// đứng hình ⇒ hub coi máy mất tích. Thân lệnh chỉ marshal + phóng fire-and-forget nên 5s đã rộng gấp bội
    /// cho một UI thread khỏe; UI thực sự kẹt quá 5s thì ack 'failed' và bấm lại vẫn đúng hơn là treo cả nhịp.</para>
    /// <para><c>readonly</c>: không test nào ghi đè (mọi bài VM gọi thẳng <see cref="XuLyLenhTuHub"/> trên UI
    /// thread nên không đi qua nhánh <c>Wait</c> này) — để mutable là mời một seam không ai dùng.</para>
    /// </summary>
    internal TimeSpan HanChoLenhTuHub { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>CHỖ NỐI TEST cho hai phép marshal về UI thread của <see cref="ThiHanhLenhTuHub"/> — production trỏ
    /// <c>UiDispatch</c>; test dựng "UI đang bận" (xếp hàng nhưng chưa chạy) để đo giao thức hết hạn (VỪA-D).</summary>
    internal Func<bool> UiCoQuyen { get; set; } = Services.UiDispatch.CheckAccess;
    internal Action<Action> UiXepHang { get; set; } = Services.UiDispatch.Post;

    /// <summary>
    /// <b>Cửa vào của <see cref="AppServices.LenhTuHub"/></b> — chạy trên LUỒNG POLL heartbeat: marshal thân
    /// lệnh về UI thread rồi chờ ĐỒNG BỘ kết quả (<see cref="HanChoLenhTuHub"/>).
    /// <para><b>Chờ kết quả thì được, chờ LƯỢT CHẠY thì không:</b> thân lệnh phóng lượt bằng
    /// fire-and-forget và trả ngay "đã nhận/đã bắt đầu". Chờ một vòng KM 12 shop xong là treo nhịp heartbeat
    /// hàng giờ ⇒ hub coi máy như mất tích.</para>
    /// </summary>
    internal (string Status, string? Error) ThiHanhLenhTuHub(string action, long accountId, string email)
    {
        // Đã ở UI thread (đường test, hoặc lệnh đến từ chính UI) ⇒ chạy thẳng: `RunOnUi` cũng chạy thẳng, nhưng
        // đi qua TCS + Wait ở đây là tự khoá mình nếu ai đó đổi `UiDispatch.Run` thành xếp hàng.
        if (UiCoQuyen())
        {
            return XuLyLenhTuHub(action, accountId, email);
        }

        // GIAO THỨC GIÀNH QUYỀN (VỪA-D, phản biện 04/09/2026): 0 = chờ · 1 = thân lệnh đã nhận · 2 = hết hạn.
        // Bản trước hết hạn thì trả `failed` nhưng KHÔNG huỷ cái đã xếp hàng: UI rảnh lại là thân lệnh vẫn chạy
        // đủ (đóng check đơn, phóng Brave, tạo flash) trong khi hub vừa ghi "lỗi, bấm lại" — ack nói dối, và
        // người vận hành bấm lại thì nhận câu thứ hai mâu thuẫn ("đang chạy, ưu tiên flash").
        var trangThai = 0;
        var oCho = new TaskCompletionSource<(string Status, string? Error)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        UiXepHang(() =>
        {
            if (System.Threading.Interlocked.CompareExchange(ref trangThai, 1, 0) != 0)
            {
                // Hết hạn trước khi UI rảnh ⇒ BỎ QUA, không chạy thân lệnh nữa (hub đã được báo failed).
                oCho.TrySetResult((KetQuaLenhTuHub.Failed, "lệnh đã quá hạn chờ, app bỏ qua"));
                return;
            }
            try { oCho.TrySetResult(XuLyLenhTuHub(action, accountId, email)); }
            catch (Exception ex) { oCho.TrySetException(ex); }
        });

        try
        {
            if (!oCho.Task.Wait(HanChoLenhTuHub))
            {
                if (System.Threading.Interlocked.CompareExchange(ref trangThai, 2, 0) == 0)
                {
                    return (KetQuaLenhTuHub.Failed,
                        "app chưa xử kịp lệnh (giao diện đang bận) — bấm lại sau ít phút");
                }
                // Thân lệnh vừa giành được quyền ngay sát hạn ⇒ nó ĐANG chạy (đồng bộ, ngắn): chờ nốt để ack
                // đúng kết quả THẬT thay vì báo failed cho một lệnh đã được thi hành.
                if (!oCho.Task.Wait(HanChoLenhTuHub))
                {
                    return (KetQuaLenhTuHub.Failed, "app đã nhận lệnh nhưng chưa trả kết quả kịp — kiểm tra nhật ký máy");
                }
            }
            return oCho.Task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            return (KetQuaLenhTuHub.Failed, ex.Message);
        }
    }

    /// <summary>
    /// <b>THÂN của một lệnh hub — chạy trên UI THREAD.</b> Trả (status, lý do) đúng khuôn ack:
    /// <see cref="KetQuaLenhTuHub.Done"/> nghĩa là <i>đã nhận / đã bắt đầu</i>, không phải "đã xong".
    /// <para>Lệnh không biết ⇒ 'failed' kèm tên lệnh, KHÔNG im lặng: hub mới + client cũ là ca có thật sau mỗi
    /// lượt deploy lệch nhau.</para>
    /// </summary>
    internal (string Status, string? Error) XuLyLenhTuHub(string action, long accountId, string email)
    {
        try
        {
            return action switch
            {
                HanhDongLenhTuHub.Chay => LenhHubChayCheckDon(accountId, email),
                HanhDongLenhTuHub.DungTatCa => LenhHubDungTaiKhoan(accountId, email),
                HanhDongLenhTuHub.ChayKm =>
                    LenhHubChayVong(accountId, email, action, LoaiChuongTrinh.ChuongTrinhShop),
                HanhDongLenhTuHub.ChayCombo =>
                    LenhHubChayVong(accountId, email, action, LoaiChuongTrinh.Combo),
                HanhDongLenhTuHub.ChayFlash =>
                    LenhHubChayVong(accountId, email, action, LoaiChuongTrinh.FlashSale),
                _ => (KetQuaLenhTuHub.Failed, $"bản client này chưa hỗ trợ lệnh '{action}'"),
            };
        }
        catch (Exception ex)
        {
            _services.Log.Append(email, $"Hub ra lệnh '{action}' LỖI: " + ex.ToString());
            return (KetQuaLenhTuHub.Failed, ex.Message);
        }
    }

    /// <summary>Ghi chú ack khi lệnh <c>run</c> bị xếp hàng chờ thay vì mở ngay — hub hiện nó ở dòng "⚠ …" nên
    /// nó phải TỰ nói đủ chuyện, không dựa vào nhật ký máy.</summary>
    private static string GhiChuChoLuotDangGiu(string moTaViec)
        => $"đang chờ {ThuongHoaChuDau(moTaViec)} xong shop hiện tại rồi mới chạy";

    /// <summary>
    /// <b><c>run</c> — mở phiên CHECK ĐƠN.</b> Ba nhánh:
    /// <list type="number">
    /// <item>phiên đã chạy ⇒ 'done' + "đã chạy sẵn" (Start vốn idempotent, nhưng nói rõ cho người vận hành);</item>
    /// <item><b>gói FLASH đang giữ cầu nối ⇒ 'failed'</b> (người dùng chốt 04/09/2026:
    /// <i>"flash luôn được ưu tiên, khi đang chạy flash, các action khác không thể chèn vào giữa flow"</i>).
    /// Không bật cờ nhường, không xếp lượt hoãn — chen check đơn vào giữa gói (kể cả "chờ hết shop hiện tại")
    /// vẫn là chèn. Cùng câu <see cref="CauTuChoiViUuTienFlash"/> với <c>run-km</c>/<c>run-combo</c>.
    /// <c>stop-all</c> vẫn dừng được gói (đó là DỪNG, không phải chèn). Nhịp khe nghỉ của CHÍNH gói flash vẫn
    /// tự mở lại check đơn khi gói xong — không đụng;</item>
    /// <item><b>lát cắt KHÁC</b> (KM/Combo/Mở list shop) đang giữ cầu nối ⇒ KHÔNG 'failed' và KHÔNG đè: bật cờ
    /// nhường rồi XẾP một lượt chạy lại (<see cref="ChayLaiCheckDonSauKhiChoAsync"/>, chờ 0). Ack 'done' kèm
    /// ghi chú "đang chờ …";</item>
    /// <item>đường sạch ⇒ <c>Sessions.Start</c> như cũ.</item>
    /// </list>
    /// <para>VỪA-5 (KmKhung tự phát <c>run</c> trả khung): cùng cửa flash ở trên — lệnh đó <b>failed</b>, gói
    /// flash không bị cắt. Người dùng đã chốt ưu tiên flash (04/09 tối).</para>
    /// </summary>
    private (string Status, string? Error) LenhHubChayCheckDon(long accountId, string email)
    {
        var nhan = NhanLenhTuHub(HanhDongLenhTuHub.Chay);
        if (_services.Sessions.IsRunning(accountId))
        {
            return (KetQuaLenhTuHub.Done, "đã chạy sẵn");
        }

        if (DangChayFlashHoacCauNoi())
        {
            if (LaGoiFlashDangGiuCauNoi())
            {
                _services.Log.Append(email,
                    $"Hub ra lệnh: {nhan} — từ chối: {CauTuChoiViUuTienFlash} Không chen check đơn vào giữa gói.");
                return (KetQuaLenhTuHub.Failed, CauTuChoiViUuTienFlash);
            }

            var moTaViec = MoTaViecDangGiuCauNoi();

            // Cùng ba bước với `TraHoSo` khi nó lên lịch một lượt hoãn — CÙNG máy, không phải đường thứ hai:
            // danh tính lượt (ghi đè lượt cũ của cùng tài khoản) → công bố mốc đang chờ cho nhịp flash → phóng.
            // Mốc là BÂY GIỜ vì không có khe nghỉ nào phải trả lại: người dùng muốn check đơn chạy ngay khi
            // shop hiện tại xong.
            _nhuongChoCheckDon = true;
            _ctsHoanChayLai ??= new System.Threading.CancellationTokenSource();
            var luotId = Guid.NewGuid();
            lock (_khoaHoan) { _dsTkDangHoanChayLai[accountId] = luotId; }
            DatMocHoanDangCho(accountId, DongHoHoanChayLai());
            _dsTkNguoiDungDungTay.Remove(accountId); // ▶ Chạy từ hub = người dùng đổi ý sau một cú Dừng

            _services.Log.Append(email,
                $"Hub ra lệnh: {nhan} — {moTaViec} đang giữ cầu nối. KHÔNG mở đè: đã xếp lượt chạy lại, check "
                + "đơn sẽ lên ngay khi shop hiện tại xong. Bấm ✖ Dừng là huỷ hẳn lượt xếp này.");

            LuotHoanGanNhat = ChayLaiCheckDonSauKhiChoAsync(
                accountId, luotId, email, TimeSpan.Zero, _ctsHoanChayLai.Token);
            return (KetQuaLenhTuHub.Done, GhiChuChoLuotDangGiu(moTaViec));
        }

        _services.Log.Append(email,
            $"Hub ra lệnh: {nhan} — mở phiên (đăng nhập rồi lặp qua các shop)...");
        _dsTkNguoiDungDungTay.Remove(accountId); // ▶ Chạy từ hub = người dùng đổi ý sau một cú Dừng
        _services.Sessions.Start(accountId);
        UpdateSelectedSessionStatus();
        return (KetQuaLenhTuHub.Done, null);
    }

    /// <summary>
    /// <b><c>stop-all</c> — DỪNG MỌI HOẠT ĐỘNG CỦA ĐÚNG MỘT TÀI KHOẢN</b> (người dùng chốt 04/09/2026:
    /// <i>"khi click dừng từ hub thì dừng máy đang chạy tài khoản nào đó… nên nếu dừng sẽ là dừng ACC chứ không
    /// dừng client, vì client có thể đang chạy acc khác"</i>).
    /// <para><b>KHÁC nút "Dừng tất cả" trên ribbon</b> (<see cref="StopAllAsync"/> quét sạch MỌI phiên của máy):
    /// ở đây chỉ đụng <paramref name="accountId"/> — xem <see cref="DungMotTaiKhoanAsync"/>. Tài khoản khác đang
    /// chạy trên cùng máy KHÔNG bị đụng.</para>
    /// <para>Giữ chuỗi hành động <c>stop-all</c> (nghĩa nay là "dừng tất cả CỦA acc này"), phân biệt với
    /// <c>stop</c> cũ (chỉ dừng phiên, dùng cho tự động hoá khung nhường khung). Lịch tự động KHÔNG bị tắt.</para>
    /// </summary>
    private (string Status, string? Error) LenhHubDungTaiKhoan(long accountId, string email)
    {
        var nhan = NhanLenhTuHub(HanhDongLenhTuHub.DungTatCa);
        _services.Log.Append(email,
            $"Hub ra lệnh: {nhan} — dừng mọi hoạt động của tài khoản này: phiên check đơn + lượt KM/Combo/Flash "
            + "Sale đang chạy (nếu là của tài khoản này) + huỷ lượt hoãn chạy lại. Máy vẫn chạy tài khoản khác. "
            + "Lịch tự động KHÔNG tắt: tới khung là máy vẫn tự chạy tiếp.");
        ChayNenChoLenhHub(DungMotTaiKhoanAsync(accountId), email, nhan);
        return (KetQuaLenhTuHub.Done, null);
    }

    /// <summary>
    /// Thân của lệnh hub <c>stop-all</c>: dừng MỌI hoạt động của đúng một tài khoản (khuôn <see cref="StopSelected"/>
    /// nhưng phạm vi một id). Lý do per-tài-khoản: <see cref="LenhHubDungTaiKhoan"/>.
    /// <para>Bốn bước: (1) lát cắt cầu nối đang chạy THUỘC tài khoản này ⇒ cắt (<see cref="TryKillPoc"/>) + bật
    /// cờ "người dùng dừng tay" để <see cref="TraHoSo"/> KHÔNG chạy lại check đơn của nó; (2) huỷ lượt hoãn chạy
    /// lại của RIÊNG nó; (3) dừng phiên check đơn của nó; (4) đóng cửa sổ "Mở list shop" nếu là của nó.</para>
    /// <para>Không <c>await</c> gì thật (mọi bước là fire-and-forget như <see cref="StopSelected"/>) nhưng trả
    /// <c>Task</c> để đi chung khuôn <see cref="ChayNenChoLenhHub"/>.</para>
    /// </summary>
    private Task DungMotTaiKhoanAsync(long accountId)
    {
        // (0) Ghi tài khoản này vào tập "người dùng đã bảo dừng" — LUÔN LUÔN, bất kể lát cắt đang chạy thuộc ai
        //     (NẶNG-A, 04/09/2026). `TraHoSo` của lượt mượn đang bay (của chính nó hay của tài khoản khác) sẽ
        //     nhờ tập này mà KHÔNG mở lại đúng tài khoản này, nhưng VẪN mở lại các tài khoản khác cùng bị tạm dừng.
        //     Bản trước bật một cờ cho cả máy và chỉ khi lát cắt thuộc tài khoản này ⇒ hai ca hỏng ngược nhau:
        //     Dừng A giữa lát cắt của A thì B chết câm; Dừng B giữa lát cắt của A thì B tự mở lại sau đó.
        _dsTkNguoiDungDungTay.Add(accountId);

        // (1) Lát cắt cầu nối đang chạy THUỘC đúng tài khoản này ⇒ cắt. `_bridgeRunning` là cờ TOÀN MÁY (một lát
        //     cắt tại một thời điểm), nên phải kèm điều kiện `TaiKhoanLatCatDangChay == accountId`: máy có thể
        //     đang chạy lát cắt của tài khoản KHÁC, và cú Dừng của tài khoản này không được cắt ngang nó.
        //     `TryKillPoc` huỷ `_bridgeCts` ⇒ `ChayLatCatAsync` chạy tới `finally` ⇒ `TraHoSo` (đọc tập ở (0)).
        if (LatCatDangChayThuocTaiKhoan(_bridgeRunning, TaiKhoanLatCatDangChay, accountId))
        {
            TryKillPoc();
        }

        // (2) Huỷ lượt hoãn chạy lại của RIÊNG tài khoản này — KHÔNG đụng lượt hoãn của tài khoản khác.
        HuyLuotHoanMotTaiKhoan(accountId, "người dùng bấm Dừng từ hub");

        // (3) Dừng phiên check đơn của nó (Stop tự no-op nếu không có phiên nào).
        _services.Sessions.Stop(accountId);

        // (4) Đóng cửa sổ "Mở list shop" nếu nó thuộc đúng tài khoản này (không phải tài khoản đang xem) —
        //     NHƯNG KHÔNG khi máy đang chạy lát cắt của tài khoản KHÁC (VỪA-C, 04/09/2026): `DongCuaSoXemVaBao`
        //     đi qua `TryKillPoc` ⇒ huỷ `_bridgeCts`, mà token đó lúc này là của lát cắt kia (nó đã đặt
        //     `_bridgeRunning` nhưng chưa ghi đè `_pocAccountId` cho tới khi qua khung xuyên máy + mượn hồ sơ).
        //     Cửa sổ để lại sẽ bị chính lát cắt kia đóng ở `DongCuaSoCuLai()` khi nó thật sự mở Brave.
        if (NenDongCuaSoXem(_pocAccountId, new[] { accountId }))
        {
            if (_bridgeRunning && TaiKhoanLatCatDangChay != accountId)
            {
                _services.Log.Append(NhatKyCuaCuaSoXem(),
                    "Hub ra lệnh Dừng: cửa sổ \"Mở list shop\" để lại sẽ được đóng sau — máy đang chạy lát cắt "
                    + "của tài khoản khác, không cắt ngang nó.");
            }
            else
            {
                DongCuaSoXemVaBao(NhatKyCuaCuaSoXem());
            }
        }

        UpdateSelectedSessionStatus();
        return Task.CompletedTask;
    }

    /// <summary>
    /// <b>Lệnh hub <c>stop-all</c> của tài khoản này có được CẮT lát cắt cầu nối đang chạy không?</b> Chỉ khi có
    /// lát cắt đang chạy (<paramref name="bridgeRunning"/>) VÀ nó thuộc ĐÚNG tài khoản này
    /// (<paramref name="taiKhoanLatCat"/> == <paramref name="accountId"/>). Máy chạy lát cắt của tài khoản KHÁC
    /// thì cú Dừng của tài khoản này không được cắt ngang — pure để test được ma trận (người dùng chốt 04/09/2026).
    /// </summary>
    internal static bool LatCatDangChayThuocTaiKhoan(bool bridgeRunning, long? taiKhoanLatCat, long accountId)
        => bridgeRunning && taiKhoanLatCat == accountId;

    /// <summary>
    /// <b><c>run-km</c> / <c>run-combo</c> / <c>run-flash</c> — chạy một vòng chương trình bằng CẤU HÌNH ĐÃ LƯU
    /// của tài khoản.</b> Y đường lịch tự động, bỏ đúng hai cửa: "tới hạn" và "máy chủ trì" (người dùng đang chủ
    /// động bấm ở máy này). GIỮ cửa cấu hình hợp lệ, và KHÔNG ghi mốc <c>LanThuTuDong</c> — lượt bấm tay không
    /// được đốt lượt tự động của khung (luật sẵn có, xem <see cref="ChotLuotFlash"/>).
    /// <para><b>Cửa ƯU TIÊN FLASH đứng TRƯỚC mọi thứ khác</b>: gói flash đang chạy ⇒ từ chối cả ba; lát cắt KHÁC
    /// đang chạy ⇒ cũng từ chối cả ba (kể cả <c>run-flash</c>) — cắt ngang một vòng giữa shop để lại chương
    /// trình rác trên Shopee, nên "ưu tiên" chỉ có nghĩa là không ai chạy ĐÈ lên flash.</para>
    /// <para><b>Cửa cấu hình dùng LẠI luật thuần của form</b> (<c>KiemTraForm*</c> + <c>KiemTraMocTuongLai</c>) —
    /// đúng bộ mà cả hai đường sẵn có dùng: lịch tự động (<see cref="NhipLichKmAsync"/>,
    /// <see cref="NhipLichComboAsync"/>) và nút bấm tay trên form (<c>KmDialogViewModel.ThuChay</c>). Bỏ vế mốc
    /// tương lai là đường hub thành đường DUY NHẤT phóng được trình duyệt với một mốc quá khứ ⇒ mọi shop đỏ,
    /// đúng ca hỏng đã ghi ở <see cref="NhipLichKmAsync"/>.</para>
    /// </summary>
    private (string Status, string? Error) LenhHubChayVong(
        long accountId, string email, string action, LoaiChuongTrinh loai)
    {
        var nhan = NhanLenhTuHub(action);
        if (LyDoTuChoiLenhVongTuHub() is string chan)
        {
            _services.Log.Append(email, $"Hub ra lệnh: {nhan} — TỪ CHỐI. {chan}");
            return (KetQuaLenhTuHub.Failed, chan);
        }

        var cf = _services.KmConfig.Get(accountId);
        var bayGio = DateTime.Now;
        var hanChot = HanChotLenhVongTuHub(loai, bayGio);

        string? loiCauHinh;
        string moTa;
        Func<Task> phong;

        switch (loai)
        {
            case LoaiChuongTrinh.ChuongTrinhShop:
            {
                var thamSo = DungThamSoKm(cf, bayGio);
                loiCauHinh =
                    KmDialogViewModel.KiemTraForm(thamSo.Ten, thamSo.Gio, thamSo.PhanTram, cf.TuDong, cf.SoNgay)
                    ?? KmDialogViewModel.KiemTraMocTuongLai(
                        thamSo.NgayBatDau, thamSo.Gio, bayGio, _services.Results.GetShops(accountId).Count);
                moTa = $"chương trình \"{thamSo.Ten}\" ({thamSo.NgayBatDau:dd-MM-yyyy} {thamSo.Gio} → "
                       + $"{thamSo.NgayKetThuc:dd-MM-yyyy} {thamSo.Gio}, giảm {thamSo.PhanTram}%)";
                phong = () => ChayKmAsync(accountId, email, thamSo, hanChot);
                break;
            }

            case LoaiChuongTrinh.Combo:
            {
                var thamSo = DungThamSoCombo(cf, bayGio);
                loiCauHinh =
                    KmDialogViewModel.KiemTraFormCombo(
                        thamSo.Ten, thamSo.Gio, thamSo.Loai, thamSo.MuaSl, thamSo.Giam, thamSo.GioiHan)
                    // Số shop lượt này THỰC SỰ chạy — vòng combo bị `TranShopLuotCombo` cắt sau N shop, tính
                    // biên an theo cả danh sách là chặn oan (ca thật 26/08/2026, xem `NhipLichComboAsync`).
                    ?? KmDialogViewModel.KiemTraMocTuongLai(
                        thamSo.NgayBatDau, thamSo.Gio, bayGio,
                        KmLichTuDong.SoShopSeChay(
                            _services.Results.GetShops(accountId).Count, LoaiChuongTrinh.Combo));
                moTa = thamSo.MoTa();
                phong = () => ChayComboAsync(accountId, email, thamSo, hanChot);
                break;
            }

            case LoaiChuongTrinh.FlashSale:
            {
                // KHÔNG có vế `KiemTraMocTuongLai`: gói flash không đặt mốc "bắt đầu lúc HH:mm" nào cả — nó
                // nhặt các KHUNG GIỜ Shopee đang mở cho ngày đích. Chặn giờ là chặn nhầm khái niệm.
                var thamSo = new ThamSoFlashSale(
                    KmLichTuDong.NgayDichFlash(bayGio), cf.FlashPhanTramGiam, cf.FlashSoLuongThamGia);
                loiCauHinh = KmDialogViewModel.KiemTraFormFlash(
                    cf.FlashPhanTramGiam, cf.FlashSoLuongThamGia);
                moTa = thamSo.MoTa();
                // RESET CỜ NHƯỜNG ngay trước gói — cùng luật với `ChayFlashSaleTayAsync`; hai vòng kia tự reset
                // bên trong `ChayKmAsync`/`ChayComboAsync`.
                phong = () =>
                {
                    _nhuongChoCheckDon = false;
                    return ChayFlashSaleAsync(
                        accountId, email, thamSo, hanChot, phaiNhuongSauShop: () => _nhuongChoCheckDon);
                };
                break;
            }

            default:
                return (KetQuaLenhTuHub.Failed, $"bản client này chưa chạy được vòng '{loai}'");
        }

        if (loiCauHinh is not null)
        {
            _services.Log.Append(email,
                $"Hub ra lệnh: {nhan} — KHÔNG chạy, cấu hình của tài khoản này không dùng được: {loiCauHinh} "
                + "Mở form 'KM của Shop' ở máy này, sửa lại thông số rồi bấm \"Đóng\".");
            return (KetQuaLenhTuHub.Failed, loiCauHinh);
        }

        var cauHan = hanChot is DateTime hc
            ? $"Không bắt đầu shop mới sau {hc:HH:mm}."
            : "Chạy tới hết shop (không hạn giờ, đúng như bấm tay).";
        _services.Log.Append(email,
            $"Hub ra lệnh: {nhan} — chạy bằng cấu hình đã lưu: {moTa}. {cauHan} "
            + "Check đơn tạm dừng, xong sẽ tự chạy lại.");
        ChayNenChoLenhHub(phong(), email, nhan);
        return (KetQuaLenhTuHub.Done, null);
    }

    /// <summary>
    /// <b>HẠN CHÓT cho một lệnh chạy vòng TỪ HUB</b> (<c>run-km</c> / <c>run-combo</c> / <c>run-flash</c>).
    /// <para><b>KM và Combo: <c>null</c> = đúng đường BẤM TAY</b> (người dùng chủ động bấm ▶ trên hub, họ tự quyết
    /// khi nào dừng — xem <see cref="KmLichTuDong.ConDuocBatDauShopMoi"/> "hanChot null = luôn được"). Bản trước
    /// mượn hạn TỰ ĐỘNG (<see cref="KmLichTuDong.HanChotLuotTuDong"/> = giờ đóng khung 04:00/08:00): bấm ▶ KM
    /// lúc 10 giờ sáng là hạn đã ở quá khứ ⇒ 0 shop chạy được, im lặng (NẶNG, phản biện 04/09/2026).</para>
    /// <para><b>Flash GIỮ 22:15</b> (không phải giờ đóng khung): Shopee đóng đăng ký cho ngày D+1 quanh ~22:50 —
    /// khớp <c>ChayFlashSaleTayAsync</c>. Đây là hạn THẬT của gói, không phải "giờ đóng khung tự động".</para>
    /// </summary>
    internal static DateTime? HanChotLenhVongTuHub(LoaiChuongTrinh loai, DateTime bayGio)
        => loai == LoaiChuongTrinh.FlashSale
            ? KmLichTuDong.HanChotLuotTuDong(bayGio, loai)
            : (DateTime?)null;

    /// <summary>
    /// Phóng lượt của một lệnh hub CHẠY NỀN và bảo đảm ngoại lệ của nó ĐI RA NHẬT KÝ.
    /// <para>Bắt buộc phải bọc: <c>_ = ChayKmAsync(...)</c> trần là một task không ai quan sát — một vòng 12
    /// shop ném giữa chừng sẽ rơi vào <c>TaskScheduler.UnobservedTaskException</c>, không dấu vết, mà ack gửi
    /// về hub thì đã là 'done' từ lâu (đúng nghĩa: đã BẮT ĐẦU). Cùng lý do đã ghi ở <c>NhipSangNgay</c>.</para>
    /// </summary>
    private void ChayNenChoLenhHub(Task luot, string email, string nhan)
        => _ = TheoDoiLuotLenhHubAsync(luot, email, nhan);

    private async Task TheoDoiLuotLenhHubAsync(Task luot, string email, string nhan)
    {
        try
        {
            await luot;
        }
        catch (OperationCanceledException)
        {
            // Người dùng bấm Dừng / thoát app — không phải lỗi.
        }
        catch (Exception ex)
        {
            try { _services.Log.Append(email, $"Lượt '{nhan}' do Hub ra lệnh LỖI: " + ex.ToString()); }
            catch { /* ghi log mà cũng hỏng thì thôi */ }
        }
    }

    /// <summary>
    /// "Truy cập TK" (nút trên dòng TK chưa xác nhận): CHỌN tài khoản đó (đổ Chi tiết + nhật ký) rồi TỰ MỞ phiên
    /// trình duyệt để người dùng xác minh tay trên cửa sổ Brave. Phiên đang chạy sẵn → chỉ chọn + báo (KHÔNG mở
    /// trùng). Mở phiên bằng ĐÚNG đường sẵn có <see cref="AccountSessionManager.Start"/> (idempotent) — không chế
    /// đường mở mới. Gọi từ code-behind (giống <see cref="ToggleRowTick"/>).
    /// </summary>
    public void TruyCapTk(AccountRowViewModel row)
    {
        // Chọn dòng: OnSelectedRowChanged tự nạp form + đưa cửa sổ Brave (nếu có) ra trước.
        SelectedRow = row;

        var id = row.Id;
        var email = row.Email;

        // Lát cắt cầu nối đang chạy → KHÔNG mở phiên (một khoá hai chủ + hai Chromium một hồ sơ). Đường này
        // trước 18/08/2026 không có chốt nào cả.
        if (DaTuChoiViLatCatDangChay(email))
        {
            return;
        }

        if (_services.Sessions.IsRunning(id))
        {
            const string msg = "Phiên đang mở — xác minh trên cửa sổ Brave của tài khoản này.";
            _services.Log.Append(email, msg);
            BusyStatus = msg;
            return;
        }

        _services.Log.Append(email, "Truy cập TK: mở trang bán hàng để xác minh tay...");
        _services.Sessions.Start(id);
        UpdateSelectedSessionStatus();
    }

    /// <summary>"Dừng đã chọn" — dừng phiên của mọi tài khoản đang tick (Stop tự no-op nếu không có phiên), và
    /// đóng luôn cửa sổ "Mở list shop" nếu nó thuộc một trong những tài khoản đó (xem
    /// <see cref="NenDongCuaSoXem"/>): bấm Dừng mà cửa sổ vẫn còn thì người dùng chỉ còn đường tắt app.</summary>
    [RelayCommand]
    private void StopSelected()
    {
        var ids = Accounts.Where(r => r.IsSelected).Select(r => r.Id).ToList();
        // Ghi ĐÚNG các tài khoản được tick vào tập "người dùng đã bảo dừng"; `TraHoSo` tự lọc theo từng tài
        // khoản (NẶNG-A, 04/09/2026). Tick một tài khoản chẳng liên quan tới lượt mượn thì vô hại: nó không nằm
        // trong `_dsTkDaDung` nên không ai đọc tới. Bản cờ-đơn trước đây: tick B (đang mượn) chặn luôn lượt chạy
        // lại của A cùng tập.
        _dsTkNguoiDungDungTay.UnionWith(ids);
        // …và HUỶ lượt hoãn đang treo nếu tập tick giao với nó. Cửa RIÊNG với cửa trên: `_dsTkDaDung` đã bị
        // `TraHoSo` dọn sạch từ lúc đặt lượt hoãn, nên vế kia không còn nhìn thấy tài khoản đang hoãn nữa.
        bool giaoLuotHoan;
        lock (_khoaHoan) { giaoLuotHoan = ids.Any(_dsTkDangHoanChayLai.ContainsKey); }
        if (giaoLuotHoan)
        {
            HuyLuotHoanChayLaiCheckDon("người dùng bấm Dừng đã chọn");
        }
        foreach (var id in ids)
        {
            _services.Sessions.Stop(id);
        }

        if (NenDongCuaSoXem(_pocAccountId, ids))
        {
            DongCuaSoXemVaBao(NhatKyCuaCuaSoXem());
        }

        UpdateSelectedSessionStatus();
    }

    /// <summary>"Dừng tất cả" — dừng mọi phiên đang chạy (đóng &amp; kill hết Brave) VÀ đóng cửa sổ "Mở list shop"
    /// để lại (vô điều kiện: "tất cả" nghĩa là tất cả, và tooltip trên ribbon đang hứa đúng điều đó).</summary>
    [RelayCommand]
    private async Task StopAllAsync()
    {
        // Chủ động dừng HẲN ⇒ không chạy lại check đơn (người dùng chốt 18/08/2026). Đặt TRƯỚC lượt đóng cửa sổ
        // vì chính lượt đó là chỗ TraHoSo chạy. "Tất cả" = mọi tài khoản đang bị tạm dừng bởi lượt mượn.
        _dsTkNguoiDungDungTay.UnionWith(_dsTkDaDung);
        // "Tất cả" nghĩa là tất cả — kể cả một lượt hoãn đang ngủ chờ tới 22:00. Không huỷ nó thì app tự mở
        // Brave sau khi người dùng đã bảo dừng (phản biện 29/08/2026, NẶNG).
        HuyLuotHoanChayLaiCheckDon("người dùng bấm Dừng tất cả");
        DongCuaSoXemVaBao(NhatKyCuaCuaSoXem());
        await _services.Sessions.StopAllAsync();
        UpdateSelectedSessionStatus();
    }

    /// <summary>Câu báo đã đóng cửa sổ xem — dùng chung cho nút "Đóng cửa sổ" và hai đường Dừng.</summary>
    internal const string CauDaDongCuaSoXem = "Đã đóng cửa sổ list shop.";

    /// <summary>
    /// "Đóng cửa sổ" (nút cạnh "Mở list shop") — đóng cửa sổ Brave mà "Mở list shop" cố ý để lại.
    /// <para>Nút này TỒN TẠI vì trước 18/08/2026 mọi câu chữ đều bảo người dùng "bấm ■ Dừng", mà
    /// <c>grep CanStop</c> trên toàn bộ <c>.xaml</c> ra 0 chỗ bind: không hề có nút ■ Dừng nào. Người dùng chỉ
    /// còn hai đường là "Dừng tất cả" trên ribbon (giết luôn mọi phiên khác) hoặc tắt app.</para>
    /// </summary>
    [RelayCommand]
    private void DongCuaSoXem() => DongCuaSoXemVaBao(NhatKyCuaCuaSoXem());

    /// <summary>
    /// Nhật ký nào phải nhận dòng "đã đóng cửa sổ": nhật ký của tài khoản SỞ HỮU cửa sổ
    /// (<see cref="_pocAccountId"/>), không phải tài khoản đang xem.
    /// <para>Hai cái đó tách nhau thường xuyên: mở list shop cho A rồi bấm sang B xem cho tiện. Ghi theo
    /// <c>_editingId</c> thì cửa sổ của A bị đóng mà vết lại nằm trong nhật ký của B, còn nhật ký A — chỗ người
    /// ta sẽ mở ra tìm — không có gì cả. Chưa có cửa sổ nào thì lùi về tài khoản đang xem.</para>
    /// </summary>
    private string NhatKyCuaCuaSoXem()
    {
        var id = _pocAccountId ?? _editingId;
        return (id is long i ? _services.Accounts.GetById(i)?.Email : null) ?? EditEmail;
    }

    /// <summary>Đóng cửa sổ xem rồi BÁO — chỉ ghi nhật ký/trạng thái khi THẬT SỰ có cửa sổ vừa bị đóng, kẻo mỗi
    /// lượt "Dừng tất cả" lại đẻ một dòng log rỗng nghĩa. Đóng im lặng cũng không được: người dùng vừa mất một
    /// cửa sổ thì phải thấy lý do.</summary>
    private void DongCuaSoXemVaBao(string logSource)
    {
        // Dùng THẲNG property, đừng chép lại biểu thức của nó: "có cái gì để đóng không" và "nút có bấm được
        // không" phải là CÙNG một câu hỏi, hai bản chép tay là hai bản trôi lệch.
        var coCuaSo = CoTheDongCuaSo;
        TryKillPoc();
        if (!coCuaSo)
        {
            // Vẫn phải dọn cờ mượn hồ sơ: không có cửa sổ nào để đóng nhưng có thể vẫn còn phiên chờ chạy lại.
            TraHoSo();
            return;
        }

        _services.Log.Append(logSource, CauDaDongCuaSoXem);
        BusyStatus = CauDaDongCuaSoXem;
        TraHoSo(); // cửa sổ đã đóng ⇒ trả hồ sơ (nhả khoá + chạy lại check đơn)
    }

    /// <summary>Dừng phiên của tài khoản đang chọn (đóng &amp; kill Brave của phiên đó, không ảnh hưởng phiên khác).
    /// Chạy được CẢ khi chỉ có phiên "chạy thử bridge" (không có phiên production): vẫn hủy + đóng trình duyệt.</summary>
    [RelayCommand]
    private void Stop()
    {
        var wasBridge = _bridgeRunning;

        // Hủy lát cắt bridge (cancel _bridgeCts → đóng cả trình duyệt điều khiển Playwright lẫn trình duyệt sạch)
        // + đóng cửa sổ POC. Chạy KỂ CẢ khi _editingId null (chỉ có bridge, chưa mở phiên production nào).
        TryKillPoc();
        TraHoSo(); // cửa sổ đi rồi ⇒ trả hồ sơ (nhả khoá + chạy lại tập đã dừng)

        if (_editingId is long accountId)
        {
            _services.Sessions.Stop(accountId);
            UpdateSelectedSessionStatus();
        }

        if (wasBridge)
        {
            var email = (_editingId is long id ? _services.Accounts.GetById(id)?.Email : null) ?? EditEmail;
            _services.Log.Append(email, "Đã dừng chạy thử + đóng trình duyệt.");
        }
    }

    /// <summary>
    /// "Chạy" — nút hành động chính màn Tài khoản (mô hình 1 subaccount = nhiều shop): MỞ PHIÊN cho tài khoản
    /// đang xem (khởi động Brave → đăng nhập subaccount → tự lặp qua các shop). Idempotent qua
    /// <see cref="AccountSessionManager.Start"/> (đang chạy thì thôi, không mở trùng). Vòng lặp shop tự chạy
    /// trong RunAsync sau đăng nhập nên KHÔNG gọi <c>SyncFullAsync</c> thủ công (tránh giẫm vòng lặp). Phiên
    /// đang chạy vòng lặp shop → chỉ log rồi thôi.
    /// </summary>
    [RelayCommand]
    private void Run()
    {
        // Chụp accountId + email — bám theo tài khoản đang mở trên form.
        if (_editingId is not long accountId)
        {
            return;
        }

        var email = _services.Accounts.GetById(accountId)?.Email ?? EditEmail;

        // Lát cắt cầu nối đang chạy → KHÔNG mở phiên (xem DaTuChoiViLatCatDangChay).
        if (DaTuChoiViLatCatDangChay(email))
        {
            return;
        }

        // Phiên đang chạy vòng lặp shop → không mở lại (Start vốn idempotent, nhưng báo cho rõ rồi thôi).
        if (_services.Sessions.Get(accountId) is { IsShopLoopRunning: true })
        {
            _services.Log.Append(email, "Đang chạy rồi.");
            return;
        }

        TryKillPoc(); // đóng cửa sổ POC "mở sạch" (nếu còn) trước khi phiên production launch — tránh khoá hồ sơ chung.
        TraHoSo(); // vừa giết cửa sổ xem ⇒ PHẢI trả hồ sơ, kẻo mượn mà không trả
        _services.Log.Append(email, "Chạy: mở phiên — đăng nhập rồi tự lặp qua các shop...");
        _dsTkNguoiDungDungTay.Remove(accountId); // ▶ Chạy = người dùng đổi ý sau một cú Dừng
        _services.Sessions.Start(accountId); // mở phiên; vòng lặp shop tự chạy trong RunAsync
        UpdateSelectedSessionStatus();
    }

    /// <summary>
    /// "Chạy đã chọn" (HÀNG LOẠT) — với MỌI tài khoản đang tick: MỞ PHIÊN (<see cref="AccountSessionManager.Start"/>,
    /// idempotent). Mỗi phiên tự đăng nhập subaccount rồi lặp qua các shop của nó (RunAsync) nên KHÔNG chạy hành
    /// động thủ công (Sync/Kiểm tra) — vòng lặp shop tự làm. Chụp danh sách (id, email) các dòng tick MỘT LẦN —
    /// KHÔNG giữ tham chiếu <see cref="AccountRowViewModel"/>. Rỗng → log "Chưa tick tài khoản nào." rồi thôi;
    /// phiên đang chạy vòng lặp shop → bỏ qua (log "Đang chạy rồi.").
    /// </summary>
    [RelayCommand]
    private void RunSelected()
    {
        // Lát cắt cầu nối đang chạy → KHÔNG mở phiên nào (xem DaTuChoiViLatCatDangChay). Chốt TRƯỚC cả việc
        // chụp danh sách tick: từ chối là từ chối cả mẻ, không mở nửa vời.
        if (DaTuChoiViLatCatDangChay(BatchLogSource))
        {
            return;
        }

        // Chụp (id, email) của các dòng ĐANG tick MỘT LẦN.
        var targets = Accounts
            .Where(r => r.IsSelected)
            .Select(r => (Id: r.Id, Email: r.Email))
            .ToList();

        if (targets.Count == 0)
        {
            _services.Log.Append(BatchLogSource, "Chưa tick tài khoản nào.");
            return;
        }

        // Cửa sổ "Mở list shop" của một trong những tài khoản sắp chạy PHẢI đóng trước khi phiên phóng trình
        // duyệt: hai tiến trình Chromium trên cùng hồ sơ là phiên mới chết ngay ở bước mở, và tệ hơn — extension
        // của cửa sổ cũ vẫn nối lại cổng 47821 mỗi 1200ms nên lệnh của phiên mới có thể bay sang nhầm trình duyệt.
        // Đường `Run` (một tài khoản) đã đóng vô điều kiện từ trước; đường hàng loạt này thì chưa.
        if (NenDongCuaSoXem(_pocAccountId, targets.Select(t => t.Id).ToList()))
        {
            TryKillPoc();
            TraHoSo(); // vừa giết cửa sổ xem ⇒ PHẢI trả hồ sơ
        }

        foreach (var target in targets)
        {
            // Phiên đang chạy vòng lặp shop → không mở lại (Start idempotent, nhưng báo cho rõ).
            if (_services.Sessions.Get(target.Id) is { IsShopLoopRunning: true })
            {
                _services.Log.Append(target.Email, "Đang chạy rồi.");
                continue;
            }

            _dsTkNguoiDungDungTay.Remove(target.Id); // ▶ Chạy = người dùng đổi ý sau một cú Dừng
            _services.Sessions.Start(target.Id); // mở phiên; vòng lặp shop tự chạy trong RunAsync
        }

        UpdateSelectedSessionStatus();
        _services.Log.Append(BatchLogSource, $"Đã mở phiên chạy cho {targets.Count} tài khoản đã chọn.");
    }

    // ===================== Lát cắt "Chạy thử (bridge)" — extension ↔ C# =====================

    /// <summary>Tiến trình trình duyệt SẠCH (không CDP) đang mở cho tài khoản đang chọn; null = không có.</summary>
    private System.Diagnostics.Process? _pocProcess;

    /// <summary>Phiên cầu nối GĐ1 đang chạy (WebSocket ↔ extension) cho tài khoản đang chọn; null = không có.</summary>
    private OrdersBridgeSession? _bridgeSession;

    /// <summary>Nguồn huỷ cho lát cắt cầu nối đang chạy ("Đóng cửa sổ" / "Dừng tất cả" → cancel).</summary>
    private System.Threading.CancellationTokenSource? _bridgeCts;

    /// <summary>Đang chạy lát cắt cầu nối → chặn bấm lại (tránh mở trùng phiên/khoá hồ sơ chung).</summary>
    private bool _bridgeRunning;

    /// <summary>Tài khoản của cửa sổ lát cắt đang mở/để lại; null = không có. Đặt NGAY khi bắt đầu lát cắt (chứ
    /// không đợi lát cắt xong) — trong lúc lát cắt còn chạy, <see cref="_pocProcess"/> vẫn null nên nếu chỉ dựa
    /// vào nó thì "Chạy đã chọn" đúng tài khoản đó sẽ KHÔNG đóng cửa sổ, và phiên mới đâm đầu vào hồ sơ đang khoá.</summary>
    private long? _pocAccountId;

    /// <summary>Login THÔ đã xin được khoá chạy xuyên máy cho cửa sổ đang để lại (null = chưa xin/đã nhả).</summary>
    private string? _loginKhoaXem;

    /// <summary>Cờ 0/1 "đang giữ khoá chạy" — <see cref="System.Threading.Interlocked"/> để nhả ĐÚNG MỘT lần dù
    /// nhiều đường dọn dẹp cùng gọi (khuôn <c>AccountSession._dangGiuKhoa</c>).</summary>
    private int _dangGiuKhoaXem;

    /// <summary>
    /// Còn cửa sổ trình duyệt của lát cắt đang mở hay không — nguồn thứ ba của <see cref="CanStop"/> và là một
    /// trong hai vế của <see cref="CoTheDongCuaSo"/>. <b>XAML KHÔNG bind thẳng cờ này</b>: nút "Đóng cửa sổ"
    /// bind <c>CoTheDongCuaSo</c> (<c>AccountsView.xaml:687</c>). Vẫn để <c>public</c> vì nó là cờ suy ra được
    /// báo đổi kèm và test đọc tới.
    /// <para>Nuốt lỗi khi đọc <c>HasExited</c>: đây là property BINDING đọc, ném ở đây là vỡ cả màn hình; mà
    /// "không đọc được trạng thái tiến trình" thì coi như không còn gì để đóng là suy đoán an toàn hơn.</para>
    /// </summary>
    public bool CuaSoXemDangMo
    {
        get
        {
            try { return _pocProcess is { HasExited: false }; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Báo UI cập nhật CẢ BA cờ suy ra từ cửa sổ lát cắt. Đi qua một hàm để không có chỗ nào bắn cờ này mà quên
    /// cờ kia — quên là nút kẹt sai trạng thái.
    /// <para><b><see cref="CoTheDongCuaSo"/> là cờ XAML thật sự bind vào (<c>IsEnabled</c> của nút "Đóng cửa
    /// sổ"), và chính nó từng bị quên</b> (app thật 17:04 ngày 18/08/2026): hàm này bắn <c>CanStop</c> +
    /// <c>CuaSoXemDangMo</c> nhưng KHÔNG bắn <c>CoTheDongCuaSo</c>, nên binding không bao giờ đọc lại và nút
    /// chết cứng ở giá trị lúc dựng cây — đo bằng UI Automation ra <c>enabled=False</c> ở cả ba thời điểm, kể cả
    /// lúc đang chạy và lúc còn cửa sổ để lại. Cả bộ test vẫn xanh vì test chỉ khoá ĐƯỜNG BIND, không khoá nhịp
    /// báo đổi. Thêm cờ suy ra mới thì PHẢI thêm vào đây.</para>
    /// </summary>
    private void BaoCoDoiCuaSoXem()
    {
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CuaSoXemDangMo));
        OnPropertyChanged(nameof(CoTheDongCuaSo));
    }

    /// <summary>
    /// Câu tổng kết của một lát cắt: dòng ghi NHẬT KÝ và dòng hiện ở ô trạng thái.
    /// <para>Hai dòng chứ không một, vì "Chạy thử (bridge)" cố ý nói khác nhau ở nhánh lỗi (nhật ký thêm chữ
    /// "chưa xong") — gộp làm một là lặng lẽ đổi câu chữ của một lệnh mà việc này không được phép đụng tới.</para>
    /// </summary>
    internal sealed record KetLuanLatCat(string Nhat, string TrangThai)
    {
        /// <summary>Cùng một câu cho cả nhật ký lẫn ô trạng thái (ca thường).</summary>
        public static KetLuanLatCat Chung(string cau) => new(cau, cau);
    }

    /// <summary>Hạ chữ cái ĐẦU của tên việc để ghép vào giữa câu ("Chạy thử (bridge)" → "Đang chạy thử
    /// (bridge) rồi…"). Giữ nguyên phần còn lại — tên việc có thể chứa dấu ngoặc, chữ hoa riêng.</summary>
    private static string ThuongHoaChuDau(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s[1..];

    /// <summary>
    /// Đuôi câu cho nhánh lỗi/captcha: <b>HỎI tiến trình</b> xem cửa sổ còn không rồi mới nói, chứ không hằng
    /// một câu.
    /// <para>Vì sao (phản biện 18/08/2026): "cửa sổ để nguyên cho bạn xem nó kẹt ở đâu" SAI ở đúng nhánh hay gặp
    /// nhất — <c>LoginAndReachPickerAsync</c> gọi <c>DongTrinhDuyetSach()</c> + <c>Process = null</c> TRƯỚC khi
    /// rẽ sang Playwright, nên đường "đăng nhập subaccount chưa xong (nhập mã?)" trả lỗi lúc KHÔNG còn cửa sổ
    /// nào cả. Bảo người dùng đi nhìn một cửa sổ không tồn tại là dạy họ ngừng tin nhật ký.</para>
    /// </summary>
    private static string DuoiCauTheoCuaSo(OrdersBridgeSession phien)
    {
        bool conCuaSo;
        try { conCuaSo = phien.Process is { HasExited: false }; }
        catch { conCuaSo = false; }

        return conCuaSo
            ? " — cửa sổ Brave để nguyên để bạn xem nó đang kẹt ở đâu."
            : " — trình duyệt đã đóng, không còn cửa sổ nào để xem.";
    }

    /// <summary>
    /// "🧪 Chạy thử (đăng nhập + shop)" — GĐ2 CẦU NỐI extension↔C#: mở trình duyệt SẠCH (KHÔNG Playwright/CDP,
    /// KHÔNG remote-debugging-port, KHÔNG proxy) với ĐÚNG hồ sơ persistent của tài khoản đang xem → mở
    /// <c>subaccount.shopee.com</c> kèm hash <c>#_od_ws=&lt;port&gt;</c>; extension nối WebSocket rồi: tự điền form
    /// đăng nhập subaccount → CHỜ user nhập mã (mở hộp thư Playwright riêng cho user tự đọc mã) → SSO sang
    /// "Kênh Người bán" → <c>/portal/shop</c> → chạy lát cắt: đọc shop → mở "Chi tiết" shop đầu bằng trusted click
    /// (kỳ vọng KHÔNG captcha) → đọc số "Chờ Lấy Hàng". Kết quả đổ ra panel log.
    /// Gate như CanRun (đang xem 1 acc đã lưu). Phiên production của acc đang chạy → từ chối (đụng khoá hồ sơ chung).
    /// </summary>
    [RelayCommand]
    private Task ChayThuBridge() => ChayLatCatAsync(
        tenViec: "Chạy thử (bridge)",
        cauBatDau: "Chạy thử (đăng nhập + shop): đăng nhập subaccount bằng trình duyệt điều khiển → chờ bạn nhập mã → đóng → mở lại sạch + extension → đọc shop.",
        trangThaiBatDau: "Đang chạy thử (đăng nhập + shop)...",
        than: async (phien, login, ct) =>
        {
            var result = await phien.RunLoginThenSliceAsync(login, ct);
            if (result.Captcha)
            {
                return KetLuanLatCat.Chung(
                    "Chạy thử (bridge): PHÁT HIỆN captcha/verify — kiến trúc CHƯA né được, cần soi lại.");
            }
            if (result.Error is not null)
            {
                // Hai câu KHÁC nhau, cố ý: nhật ký nói "chưa xong", ô trạng thái nói gọn.
                return new KetLuanLatCat(
                    "Chạy thử (bridge) chưa xong: " + result.Error,
                    "Chạy thử (bridge): " + result.Error);
            }
            return KetLuanLatCat.Chung(
                $"Chạy thử (bridge) OK: {result.Shops.Count} shop; shop đầu id={result.FirstShopId}; " +
                $"Chờ Lấy Hàng={result.ToShipCount?.ToString() ?? "?"}; đọc {result.OrdersCount} đơn" +
                (result.SlipsSaved > 0 ? $"; lưu {result.SlipsSaved} phiếu" : string.Empty) +
                " — KHÔNG captcha.");
        });

    /// <summary>
    /// <b>"Mở list shop"</b> (nút ở hàng đầu tab "Shops") — đi ĐÚNG luồng chạy tài khoản: mở Brave SẠCH +
    /// extension bằng chính hồ sơ của tài khoản đang xem → SSO "Kênh Người bán" → trang chọn shop
    /// (<c>/portal/shop</c>) → đọc danh sách shop (đổ luôn vào lưới tab "Shops") → <b>DỪNG</b>. Không mở "Chi tiết"
    /// shop nào, không lặp shop, và <b>không đóng cửa sổ</b>: cả mục đích là giao cửa sổ đang đăng nhập lại cho
    /// người dùng tự thao tác.
    /// <para>Vì sao không mở thẳng <c>/portal/shop</c> hộ người dùng: Shopee sticky-redirect vào shop mở lần trước
    /// (server-side) — phải đi qua SSO như vòng chạy thật mới chắc về được trang chọn shop.</para>
    /// <para>Cửa sổ để lại đóng khi: "Dừng tất cả" · "Dừng đã chọn" (nếu tài khoản đó đang tick) · bấm lại chính
    /// nút này · mở phiên chạy cho tài khoản đó · tắt app (trình duyệt nằm trong Job Object KILL_ON_JOB_CLOSE) ·
    /// và — hành vi CÓ SẴN, không sửa trong việc này — bất kỳ tài khoản nào bắt đầu một vòng chạy, vì
    /// <c>OrdersBridgeLauncher.Launch</c> gọi <c>FreeProfile(alsoMatchBridgeExtension: true)</c> nên quét giết MỌI
    /// trình duyệt cầu nối kể cả của hồ sơ khác.</para>
    /// </summary>
    [RelayCommand]
    private Task MoListShopAsync() => ChayLatCatAsync(
        tenViec: "Mở list shop",
        cauBatDau: "Mở list shop: mở Brave sạch + extension bằng hồ sơ của tài khoản này → SSO 'Kênh Người bán' → trang chọn shop, rồi DỪNG (cửa sổ để nguyên cho bạn xem).",
        trangThaiBatDau: "Đang mở list shop — cookie hồ sơ hết hạn thì phải đăng nhập lại + chờ bạn nhập mã, có thể vài phút...",
        than: async (phien, login, ct) =>
        {
            var result = await phien.MoDenDanhSachShopAsync(login, ct);
            if (result.Captcha)
            {
                return KetLuanLatCat.Chung(
                    "Mở list shop: PHÁT HIỆN captcha/verify" + DuoiCauTheoCuaSo(phien)
                    + " Xác minh xong thì bấm lại nút.");
            }
            if (result.Error is not null)
            {
                return new KetLuanLatCat(
                    "Mở list shop chưa xong: " + result.Error + DuoiCauTheoCuaSo(phien),
                    "Mở list shop: " + result.Error);
            }
            return KetLuanLatCat.Chung(
                $"Mở list shop OK: {result.Shops.Count} shop — cửa sổ Brave đang ở trang chọn shop, để nguyên cho bạn thao tác. "
                + "Bấm 'Đóng cửa sổ' (cạnh nút này) khi xem xong.");
        },
        // Khác "Chạy thử (bridge)": danh sách đọc được ĐỔ THẲNG vào lưới tab "Shops", y như vòng chạy thật.
        doVaoLuoiShops: true);

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // "Dò DOM KM" — LÁT CẮT CHẨN ĐOÁN, GIỮ LÂU DÀI (không còn là thí nghiệm tạm của pha 1).
    //
    // ⚠ NÓ TẠO CHƯƠNG TRÌNH THẬT. Từ khi nối pha 3, lệnh `dumpKm` đi CHÍNH chuỗi 9 bước của luồng thật, và bước
    // 9 là nút Xác nhận thanh đáy — bản trước khối comment này khẳng định "Extension KHÔNG tạo chương trình nào",
    // sai kể từ đó. Cái nút khác luồng thật ở chỗ: nó chỉ chạy MỘT shop đang mở, và ghi ra nhật ký TỪNG BƯỚC kèm
    // giá trị đọc lại (file dump JSON) — thứ cần đầu tiên khi Shopee đổi DOM. Nút bị ẩn sau `SHOPEE_DEV_TOOLS`
    // đúng vì nó tạo thật (xem `HienNutDev`).
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>PURE — tên file dump theo mốc thời gian. Mỗi lượt dò một file: dump dài, và so hai lượt với nhau
    /// chính là cách thấy Shopee vừa đổi DOM.</summary>
    internal static string TenFileDumpKm(DateTime luc)
        => $"km-dom-{luc:yyyyMMdd-HHmmss}.json";

    /// <summary>
    /// Ghi JSON dump ra thư mục <c>logs</c> cạnh database (production: <c>%APPDATA%\XuLyDonShopee\logs</c>).
    /// Trả ĐƯỜNG DẪN đã ghi, hoặc <c>null</c> nếu ghi hỏng.
    /// <para>Ghi ra FILE chứ không đổ vào panel nhật ký: bản dump dài hàng chục KB, panel giữ tối đa 200 dòng
    /// mỗi nguồn nên đổ vào đó là mất sạch phần đầu — đúng thứ cần đọc nhất.</para>
    /// </summary>
    internal string? GhiFileDumpKm(string json, DateTime luc)
        => GhiFileDump(json, TenFileDumpKm(luc));

    /// <summary>PURE — tên file dump của lượt dò <b>Flash Sale</b>. Tiền tố KHÁC <see cref="TenFileDumpKm"/>
    /// để hai lượt dò không nằm lẫn vào nhau trong thư mục <c>logs</c>: đọc dump là việc so hai bản với nhau,
    /// mà so nhầm bản của luồng khác thì kết luận sai từ dòng đầu.</summary>
    internal static string TenFileDumpFlash(DateTime luc)
        => $"flash-dom-{luc:yyyyMMdd-HHmmss}.json";

    /// <summary>Ghi dump của lượt dò Flash Sale — cùng thư mục, cùng cách ghi với <see cref="GhiFileDumpKm"/>.</summary>
    internal string? GhiFileDumpFlash(string json, DateTime luc)
        => GhiFileDump(json, TenFileDumpFlash(luc));

    /// <summary>Ghi một bản dump ra <c>logs/&lt;tenFile&gt;</c> cạnh database. Trả đường dẫn, hoặc <c>null</c>
    /// nếu ghi hỏng. Gom về một chỗ để hai (rồi ba) lượt dò không có hai đường ghi file trôi lệch nhau.</summary>
    private string? GhiFileDump(string json, string tenFile)
    {
        try
        {
            var thuMuc = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(_services.Database.Path) ?? ".", "logs");
            System.IO.Directory.CreateDirectory(thuMuc);
            var duong = System.IO.Path.Combine(thuMuc, tenFile);
            System.IO.File.WriteAllText(duong, json, System.Text.Encoding.UTF8);
            return duong;
        }
        catch (System.Exception ex)
        {
            _services.Log.Append(BatchLogSource, $"Không ghi được file dump ({tenFile}): " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// PURE — rút bản tóm tắt MỘT DÒNG MỖI BƯỚC từ JSON dump, để panel nhật ký đọc được ngay mà không phải mở
    /// file. JSON hỏng/rỗng ⇒ danh sách rỗng (không ném: đây là đường chẩn đoán, hỏng thì báo chứ đừng đổ lượt chạy).
    /// </summary>
    internal static IReadOnlyList<string> TomTatDumpKm(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return System.Array.Empty<string>();
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("buoc", out var buoc)
                || buoc.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return System.Array.Empty<string>();
            }

            var ds = new List<string>();
            foreach (var b in buoc.EnumerateArray())
            {
                var ten = b.TryGetProperty("ten", out var t) ? t.GetString() : null;
                var ok = b.TryGetProperty("ok", out var o) && o.ValueKind == System.Text.Json.JsonValueKind.True;
                var loi = b.TryGetProperty("loi", out var l) ? l.GetString() : null;
                var kyVong = b.TryGetProperty("kyVong", out var kv) ? kv.GetString() : null;
                var docLai = b.TryGetProperty("docLai", out var dl) ? dl.GetString() : null;

                // BẰNG CHỨNG ĐỌC LẠI phải hiện ra ngay trên nhật ký, không chìm trong file: lượt 20:24 ngày
                // 18/08/2026 báo "ok" cho cả hai picker trong khi ô thời gian không hề đổi. Từ nay bước nào có
                // khai kỳ vọng thì nhật ký nói luôn kỳ vọng gì / đọc lại được gì — người đọc tự chấm được, không
                // phải tin một chữ "ok".
                var chungCu = kyVong is null
                    ? string.Empty
                    : $" [kỳ vọng '{kyVong}' · đọc lại '{docLai ?? "(rỗng)"}']";

                ds.Add($"  {(ok ? "ok      " : "KHÔNG ăn")} · {ten}{chungCu}"
                       + (string.IsNullOrWhiteSpace(loi) ? string.Empty : $" — {loi}"));
            }
            return ds;
        }
        catch (System.Text.Json.JsonException)
        {
            return System.Array.Empty<string>();
        }
    }

    /// <summary>
    /// Shop sẽ dò ở lần bấm "Dò DOM KM" KẾ TIẾP. Mỗi lần bấm đi một shop khác — dò lại CÙNG một shop trong thời
    /// gian ngắn đúng là thứ đã làm Shopee bật <c>verify/traffic/error</c> đêm 18/08/2026 (2 lần mở cùng shop
    /// trong 17 phút).
    /// <para>Mồi được bằng biến môi trường <c>KM_VITRI_SHOP</c> để test tiếp đúng shop cần sau khi khởi động lại
    /// app (bộ đếm trong bộ nhớ mất theo tiến trình).</para>
    /// </summary>
    private int _viTriShopKm =
        int.TryParse(Environment.GetEnvironmentVariable("KM_VITRI_SHOP"), out var _moi) ? _moi : 0;

    /// <summary>
    /// <b>"Dò DOM KM" — nút CHẨN ĐOÁN, ẩn sau biến môi trường <see cref="BienMoiTruongDev"/>.</b> Đi đúng đường
    /// của "Mở list shop" (mượn hồ sơ → khoá xuyên máy → Brave sạch → SSO → trang chọn shop) rồi mở MỘT shop và
    /// chạy trọn chuỗi 9 bước tạo chương trình bằng DOM thuần, ghi lại nhật ký TỪNG BƯỚC kèm giá trị đọc lại.
    /// <para><b>⚠ NÓ TẠO CHƯƠNG TRÌNH KHUYẾN MÃI THẬT.</b> Từ khi có bước 9, lượt dò đi CHUNG hàm
    /// <c>chayKmChoTabDangMo</c> với luồng thật — bước 9 chính là nút "Xác nhận" ở thanh đáy. Mọi câu chữ nói
    /// "chỉ dò, không tạo gì" (bản trước ghi đúng như vậy) đều là lời nói dối nguy hiểm ở đây.</para>
    /// <para>Cửa sổ để nguyên để người dùng nhìn tận nơi trang đang ở đâu (<c>dongCuaSoKhiXong</c> mặc định
    /// <c>false</c>) — chính cái cửa sổ đang kẹt mới là bằng chứng để biết kẹt ở đâu.</para>
    /// </summary>
    [RelayCommand]
    private Task DoDomKmAsync() => ChayLatCatAsync(
        tenViec: "Dò DOM KM",
        cauBatDau: $"Dò DOM KM (chẩn đoán, chỉ hiện khi SHOPEE_DEV_TOOLS=1): mở Brave sạch → SSO → trang chọn shop → mở shop #{_viTriShopKm + 1} → chạy trọn luồng tạo chương trình bằng DOM thuần, ghi nhật ký từng bước. LƯU Ý: bước cuối TẠO CHƯƠNG TRÌNH THẬT.",
        trangThaiBatDau: "Đang dò DOM KM — có thể vài phút (đi qua 9 bước, mỗi bước còn chờ Vue render)...",
        than: async (phien, login, ct) =>
        {
            var kq = await phien.DoDomKmAsync(login, ct, _viTriShopKm);
            _viTriShopKm++;
            if (kq.Captcha)
            {
                return KetLuanLatCat.Chung("Dò DOM KM: PHÁT HIỆN captcha/verify" + DuoiCauTheoCuaSo(phien));
            }
            if (kq.Error is not null)
            {
                return new KetLuanLatCat(
                    "Dò DOM KM chưa xong: " + kq.Error + DuoiCauTheoCuaSo(phien),
                    "Dò DOM KM: " + kq.Error);
            }

            var email = _services.Accounts.GetById(_pocAccountId ?? _editingId ?? -1)?.Email ?? EditEmail;
            var duong = kq.DumpJson is null ? null : GhiFileDumpKm(kq.DumpJson, System.DateTime.Now);

            // Tóm tắt vào nhật ký để liếc là biết bước nào không ăn; chi tiết nằm trong file.
            foreach (var dong in TomTatDumpKm(kq.DumpJson))
            {
                _services.Log.Append(email, dong);
            }

            return KetLuanLatCat.Chung(
                $"Dò DOM KM xong (shop {kq.ShopId}) — dump: {duong ?? "KHÔNG ghi được file"}. "
                + "Cửa sổ để nguyên. LƯU Ý: từ khi có bước 9, lượt chạy này TẠO CHƯƠNG TRÌNH THẬT nếu bước 9 xanh "
                + "— xem dòng '9. Cuộn + Xác nhận thanh đáy' ở trên.");
        });

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // "Dò DOM Flash" — LÁT CẮT CHẨN ĐOÁN, **CHỈ ĐỌC**.
    //
    // ⚠ ĐỌC KỸ CHỖ NÀY KHÁC "Dò DOM KM" RA SAO: nút KM đi CHUNG hàm với luồng thật nên bước cuối của nó bấm
    // nút Xác nhận ở thanh đáy và TẠO CHƯƠNG TRÌNH THẬT — đêm 27/08/2026 nó để lại một chương trình rác
    // `DO DOM KM 1756…` mà người dùng phải vào Seller Centre xoá tay. Lượt Flash Sale KHÔNG được lặp lại chuyện
    // đó: nó dừng ở "Hủy" modal sản phẩm, không đụng nút tạo nào. Ràng buộc nằm ở `flow-flashsale.js` và được
    // khoá bằng test đọc chính nguồn file đó (`FlashSaleDoDomTests`).
    //
    // Vẫn ẩn sau `HienNutDev` như nút KM — không phải vì nó nguy hiểm, mà vì nó mở Brave + SSO + đụng vào shop
    // thật của người dùng, tức không phải tính năng cho người dùng bấm.
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Shop sẽ dò ở lần bấm "Dò DOM Flash" KẾ TIẾP. Bộ đếm RIÊNG với <see cref="_viTriShopKm"/>: hai nút dùng
    /// chung một bộ đếm thì bấm xen kẽ hai nút là mỗi nút nhảy hai shop một lần, và hai lượt dò liên tiếp của
    /// cùng một nút lại rơi vào cùng một shop — đúng thứ bộ đếm này sinh ra để tránh.
    ///
    /// <para><b>NHẮM MỘT SHOP CỤ THỂ:</b> đặt biến môi trường <c>FLASH_VITRI_SHOP</c> TRƯỚC khi mở app.
    /// Giá trị là chỉ số <b>ĐẾM TỪ 0</b> theo đúng thứ tự bảng <c>/portal/shop</c>:
    /// <c>0</c> = shop thứ nhất, <c>3</c> = shop thứ tư. Đọc MỘT LẦN lúc dựng ViewModel, nên đổi biến thì
    /// phải mở lại app. Mỗi lần bấm nút xong bộ đếm tự +1 để lượt sau sang shop kế — dò lại CÙNG một shop
    /// trong thời gian ngắn đúng là thứ đã làm Shopee bật <c>verify/traffic/error</c> (2 lần mở cùng shop
    /// trong 17 phút, đêm 18/08/2026).</para>
    /// <para>Vượt quá số shop thì QUAY VÒNG (<c>viTri % shops.Count</c>) nên không bao giờ ném; và
    /// <c>viTri</c> + tên shop được ghi vào bản dump để mỗi bản tự khai nó đo shop nào.</para>
    /// </summary>
    private int _viTriShopFlash =
        int.TryParse(Environment.GetEnvironmentVariable("FLASH_VITRI_SHOP"), out var _moiFlash) ? _moiFlash : 0;

    /// <summary>
    /// <b>"Dò DOM Flash" — nút CHẨN ĐOÁN, ẩn sau biến môi trường <see cref="BienMoiTruongDev"/>.</b> Đi đúng
    /// đường của "Mở list shop" (mượn hồ sơ → khoá xuyên máy → Brave sạch → SSO → trang chọn shop) rồi mở MỘT
    /// shop và dò cấu trúc "Flash Sale Của Shop" bằng DOM thuần, ghi nhật ký từng bước + file dump JSON.
    /// <para><b>KHÔNG tạo chương trình nào</b> — xem khối chú thích ngay trên.</para>
    /// </summary>
    [RelayCommand]
    private Task DoDomFlashAsync() => ChayLatCatAsync(
        tenViec: "Dò DOM Flash",
        cauBatDau: $"Dò DOM Flash Sale (chẩn đoán, chỉ hiện khi SHOPEE_DEV_TOOLS=1): mở Brave sạch → SSO → trang chọn shop → mở shop #{_viTriShopFlash + 1} → dò DOM 'Flash Sale Của Shop'. LƯỢT NÀY CHỈ ĐỌC: KHÔNG tạo chương trình nào, kết thúc bằng Hủy modal sản phẩm.",
        trangThaiBatDau: "Đang dò DOM Flash Sale — có thể vài phút (10 bước, mỗi bước còn chờ Vue render)...",
        than: async (phien, login, ct) =>
        {
            var kq = await phien.DoDomFlashSaleAsync(login, ct, _viTriShopFlash);
            _viTriShopFlash++;
            if (kq.Captcha)
            {
                return KetLuanLatCat.Chung("Dò DOM Flash: PHÁT HIỆN captcha/verify" + DuoiCauTheoCuaSo(phien));
            }
            if (kq.Error is not null)
            {
                return new KetLuanLatCat(
                    "Dò DOM Flash chưa xong: " + kq.Error + DuoiCauTheoCuaSo(phien),
                    "Dò DOM Flash: " + kq.Error);
            }

            var email = _services.Accounts.GetById(_pocAccountId ?? _editingId ?? -1)?.Email ?? EditEmail;
            var duong = kq.DumpJson is null ? null : GhiFileDumpFlash(kq.DumpJson, System.DateTime.Now);

            // Dùng lại NGUYÊN bộ tóm tắt của lượt dò KM: gói dump của hai luồng cùng khuôn `buoc[]` với
            // ten/ok/loi/kyVong/docLai, nên viết bản tóm tắt thứ hai chỉ tạo ra hai bản trôi lệch.
            foreach (var dong in TomTatDumpKm(kq.DumpJson))
            {
                _services.Log.Append(email, dong);
            }

            return KetLuanLatCat.Chung(
                $"Dò DOM Flash xong (shop {kq.ShopId}) — dump: {duong ?? "KHÔNG ghi được file"}. "
                + "Cửa sổ để nguyên. Lượt này CHỈ ĐỌC, không tạo chương trình nào. "
                + "Bốn ẩn số nằm ở khối 'anSo' đầu file dump.");
        });

    /// <summary>
    /// <b>Nút "Chạy Flash Sale" — ĐƯỜNG BẤM TAY của luồng thật.</b> Ẩn sau <see cref="HienNutDev"/> vì nó TẠO
    /// CHƯƠNG TRÌNH THẬT — một cho MỖI khung giờ Shopee mở cho ngày đích, không còn trần (03/09/2026) — trên CẢ
    /// danh sách shop (trần thử đã gỡ về 0 ngày 01/09/2026).
    /// <para><b>NGÀY ĐÍCH tính ĐÚNG MỘT LẦN ở đây</b> rồi đóng băng trong <see cref="ThamSoFlashSale"/> — không
    /// một chỗ nào sau đó được đọc lại đồng hồ. Xem <see cref="KmLichTuDong.NgayDichFlash"/>.</para>
    /// <para><b>Vẫn truyền hạn chót 22:15 dù đây là đường bấm tay</b> — khác luật của hai vòng kia, và cố ý: ở
    /// đó hạn chót chỉ để khỏi tràn sang buổi sáng, còn ở đây nó chặn một ca SAI THẬT. Shopee đóng đăng ký cho
    /// <c>D+1</c> quanh ~22:50, nên một shop bắt đầu lúc 22:3x chạy nửa sau vào vùng chết.</para>
    /// <para><b>KHÔNG truyền <c>boQuaShop</c></b>: người dùng ngồi trước máy và tự quyết chạy cái gì. Lượt tay
    /// vì thế cũng KHÔNG đốt lượt tự động — mốc ngày chỉ ghi khi phủ đủ mọi shop, xem
    /// <see cref="ChotLuotFlash"/>.</para>
    /// </summary>
    [RelayCommand]
    private Task ChayFlashSaleTayAsync()
    {
        if (_editingId is not long accountId) { return Task.CompletedTask; }
        var email = _services.Accounts.GetById(accountId)?.Email ?? EditEmail;
        var bayGio = DateTime.Now;
        // %GIẢM + SL THAM GIA đọc từ CẤU HÌNH đã lưu, y đường nhịp (03/09/2026): lượt tay và lượt tự động phải
        // gửi xuống Shopee CÙNG một cặp số, không thì "thử tay thấy đúng, đêm chạy ra khác".
        var cf = _services.KmConfig.Get(accountId);
        // RESET CỜ NHƯỜNG ngay trước lượt — xem `_nhuongChoCheckDon`. Đường tay cũng phải reset: cờ là của
        // MÁY, không của gói, nên một lượt hoãn cũ đã nhường xong vẫn để cờ bật cho tới lượt kế.
        _nhuongChoCheckDon = false;
        return ChayFlashSaleAsync(
            accountId, email,
            new ThamSoFlashSale(
                KmLichTuDong.NgayDichFlash(bayGio), cf.FlashPhanTramGiam, cf.FlashSoLuongThamGia),
            KmLichTuDong.HanChotLuotTuDong(bayGio, LoaiChuongTrinh.FlashSale),
            phaiNhuongSauShop: () => _nhuongChoCheckDon);
    }

    /// <summary>
    /// KHUNG DÙNG CHUNG của mọi lệnh chạy lát cắt cầu nối. Gom nguyên phần "hạ tầng" mà "Chạy thử (bridge)" và
    /// "Mở list shop" đều cần: chốt cửa → khoá chạy xuyên máy → cờ <see cref="_bridgeRunning"/> +
    /// <see cref="_bridgeCts"/> → đóng cửa sổ cũ → dựng phiên → chạy <paramref name="than"/> → ghi kết luận →
    /// dọn dẹp. Chép khung này lần hai là hai bản trôi lệch nhau ở đúng mấy chỗ nguy hiểm nhất (nhả khoá, nhả
    /// cổng cầu nối, giữ <c>Process</c> để còn đóng được).
    /// <para><b>Cửa sổ CỐ Ý được để lại</b> ở <c>finally { _pocProcess = session.Process; }</c> — cả hai lệnh đều
    /// muốn vậy. Kênh WS thì PHẢI <c>Dispose</c> ngay (nhả cổng 47821), nếu không phiên production kế không mở
    /// nổi cổng.</para>
    /// </summary>
    /// <param name="tenViec">Tên việc VIẾT HOA đầu ("Mở list shop") — ghép vào các câu chốt cửa/hủy/lỗi.</param>
    /// <param name="cauBatDau">Câu ghi nhật ký khi bắt đầu (nói rõ lệnh sắp làm gì).</param>
    /// <param name="trangThaiBatDau">Dòng <see cref="BusyStatus"/> trong lúc chạy — phải nói rõ có thể lâu, vì
    /// nhánh cookie hết hạn còn chờ người dùng nhập mã.</param>
    /// <param name="than">Thân lệnh: nhận phiên cầu nối đã dựng + tham số đăng nhập + token hủy, trả câu tổng kết.</param>
    /// <param name="doVaoLuoiShops">Đổ danh sách shop đọc được vào lưới tab "Shops" ("Mở list shop" và vòng
    /// KM/Combo). Cờ nằm ở khung chứ không phải chỗ gọi vì callback cần <c>accountId</c> — mà chỉ khung mới chốt
    /// được nó sau chặng chốt cửa.</param>
    /// <param name="luotChayMoi">Xoá tập tick <b>check đơn</b> (`_shopDaCheck`) khi lát cắt sắp lặp shop.
    /// Chỉ có nghĩa khi <paramref name="doVaoLuoiShops"/> bật. Check đơn / đọc list để chạy shop: <c>true</c>.
    /// Vòng KM/Combo/Flash và "Mở list shop": <c>false</c> — tick check đơn không dùng chung với ô chương trình.</param>
    /// <param name="laLuotFlash">Lát cắt này có phải một GÓI FLASH SALE không. Hai việc RIÊNG của vòng flash móc
    /// vào đây, và cả hai đều cần biết điều đó ở TẦNG KHUNG chứ không phải ở chỗ gọi:
    /// <list type="number">
    /// <item>ghi <see cref="GhiSoShopThayFlash"/> khi đọc được picker — mẫu số của luật "ngày đã trọn";</item>
    /// <item>XOÁ <see cref="MocChayLaiCheckDonSauFlash"/> khi lượt KHÔNG mượn được hồ sơ — xem khối trong
    /// <c>finally</c>.</item>
    /// </list></param>
    internal async Task ChayLatCatAsync(
        string tenViec,
        string cauBatDau,
        string trangThaiBatDau,
        Func<OrdersBridgeSession, OrdersLoginParams, System.Threading.CancellationToken, Task<KetLuanLatCat>> than,
        bool doVaoLuoiShops = false,
        bool dongCuaSoKhiXong = false,
        long? accountIdChiDinh = null,
        bool xinKhungXuyenMay = false,
        bool luotChayMoi = false,
        bool laLuotFlash = false)
    {
        // `accountIdChiDinh` chỉ dùng cho LƯỢT TỰ ĐỘNG (lịch KM 2–6h sáng): lúc đó không ai đang ngồi trước máy
        // nên KHÔNG có "tài khoản đang chọn" đáng tin — người dùng có thể để con trỏ ở tài khoản khác, hoặc
        // không chọn gì. Mọi đường bấm tay vẫn đi bằng `_editingId` như cũ.
        if ((accountIdChiDinh ?? _editingId) is not long accountId)
        {
            return;
        }

        var acc = _services.Accounts.GetById(accountId);
        var email = acc?.Email ?? EditEmail;

        // Chốt cửa DUY NHẤT còn lại: chính lát cắt đang chạy. Phiên check đơn KHÔNG còn chặn nữa — nay ta TẠM
        // DỪNG nó (xem MuonHoSoAsync) rồi chạy lại khi đóng cửa sổ, vì người dùng chốt 18/08/2026: chặn
        // cả kỳ nghỉ 90' là nút gần như không bao giờ bấm được, mà bắt đăng nhập lại thì càng không.
        if (_bridgeRunning)
        {
            // Cố ý KHÔNG đụng BusyStatus: người dùng vừa bấm một nhịp trước, ô trạng thái đang nói đúng việc.
            _services.Log.Append(email,
                $"Đang {ThuongHoaChuDau(tenViec)} rồi — đợi xong hoặc bấm 'Đóng cửa sổ'.");
            return;
        }

        _bridgeRunning = true;
        // CÙNG CẶP với `_bridgeRunning`, đặt/xoá cùng chỗ: câu "nhường chỗ cho check đơn" đọc tên này để nói
        // đúng việc đang giữ cầu nối — xem `TenLatCatDangChay`.
        TenLatCatDangChay = tenViec;
        // …và tài khoản đang giữ lát cắt — lệnh hub `stop-all` per-tài-khoản đọc để chỉ cắt lát cắt THUỘC đúng
        // tài khoản bị dừng (xem `TaiKhoanLatCatDangChay`).
        TaiKhoanLatCatDangChay = accountId;
        // …và GƯƠNG lên hub phải biết NGAY: mọi phiên check đơn sắp bị dừng để mượn hồ sơ, nên nếu chờ
        // `Sessions.Changed` thì hub thấy "rảnh" (xem `AppServices.LatCatDoi`).
        _services.LatCatDoi?.Invoke();
        BaoCoDoiCuaSoXem(); // bật nút đóng/huỷ trong lúc lát cắt chạy

        // LƯỢT NÀY đã mượn được hồ sơ hay chưa — xem khối đóng cửa sổ trong `finally`. Không có cờ này thì một
        // lượt KM mượn hồ sơ THẤT BẠI vẫn đi giết cửa sổ "Mở list shop" mà người dùng đang thao tác dở.
        var daMuonDuocHoSo = false;

        // LƯỢT NÀY đã nhờ hub dừng máy khác chưa — xem khối (0) bên dưới. Bật TRƯỚC lượt gọi, và `finally`
        // đọc nó để trả khung trên MỌI đường ra.
        var daXinKhung = false;
        try
        {
            _bridgeCts = new System.Threading.CancellationTokenSource();

            // ══════════════════════════════════════════════════════════════════════════════════════════════
            // THỨ TỰ BA BƯỚC DƯỚI ĐÂY LÀ BẮT BUỘC. ĐỪNG ĐẢO.
            //   (1) dừng check đơn → CHỜ DỪNG HẲN   (2) xin khoá xuyên máy   (3) đóng cửa sổ cũ
            //
            // (1) TRƯỚC (2) — MỘT KHOÁ, HAI CHỦ TRÊN CÙNG MỘT MÁY. Phiên check đơn đang giữ ĐÚNG khoá
            //     `orders:<login>` và chỉ nhả nó trong `finally` của `RunBridgeContinuousAsync`
            //     (`AccountSession.NhaKhoaChayAsync`, chạy TRƯỚC khi đặt Stopped). Xin khoá trước khi nó nhả
            //     xong thì: hub thấy CÙNG machine_id nên CẤP cho ta → vài trăm ms sau lượt nhả của phiên kia tới,
            //     `DELETE … WHERE account_id AND machine_id` XOÁ ĐÚNG DÒNG VỪA CẤP CHO TA → `HeartbeatLeases` là
            //     UPDATE thuần nên KHÔNG hồi phục → ta chạy cả tiếng trong khi trên hub tài khoản đang TRỐNG, và
            //     máy 2 nhảy vào. Đây là lần THỨ BA cùng một cơ chế cắn (xem hai khối "ĐỪNG SỬA" ở `finally` và
            //     ở `TryKillPoc`).
            // (2) TRƯỚC (3) — bị máy khác từ chối thì người dùng chưa mất cửa sổ đang xem.
            // ══════════════════════════════════════════════════════════════════════════════════════════════

            // (1)+(2) MƯỢN HỒ SƠ: dừng MỌI phiên → chờ Active rỗng → xin khoá xuyên máy. Cả hai bước nằm trong
            //     một hàm để không ai còn đảo được thứ tự của chúng. Không mượn được ⇒ KHÔNG mở gì (fail-closed);
            //     `finally` sẽ gọi `TraHoSo()` để chạy lại những phiên đã lỡ dừng.
            // (0) KHUNG XUYÊN MÁY — TRƯỚC khi dừng phiên cục bộ. Chỉ luồng KM bật cờ này.
            //
            // Thứ tự CÓ LÝ DO: nhờ hub dừng máy KIA mất tới ~12 giây (lệnh đi kèm nhịp heartbeat) cộng thời gian
            // nó đóng trình duyệt. Dừng phiên cục bộ trước rồi mới đi xin là tự cắt check đơn của mình ra ngồi
            // chờ máy khác — mà nếu hết hạn chờ thì cắt oan hoàn toàn.
            //
            // `daXinKhung` bật TRƯỚC lượt gọi, không phải sau: chỉ cần đã gọi là hub CÓ THỂ đã ra lệnh dừng máy
            // kia, nên `finally` PHẢI trả khung kể cả khi ta hết hạn chờ rồi bỏ lượt. Đặt sau là bỏ máy kia nằm
            // im tới lượt quét quá hạn của hub.
            if (xinKhungXuyenMay)
            {
                daXinKhung = true;
                if (!await ChoKhungXuyenMayAsync(email, tenViec, _bridgeCts.Token))
                {
                    return;
                }
            }

            if (!await MuonHoSoAsync(email, tenViec, dongCuaSoKhiXong, _bridgeCts.Token))
            {
                return;
            }
            daMuonDuocHoSo = true; // từ giây này cửa sổ trên máy là của LƯỢT NÀY — `finally` mới được đóng nó

            // (3) ══ PHẢI là DongCuaSoCuLai, TUYỆT ĐỐI KHÔNG PHẢI TryKillPoc. ĐỪNG SỬA. ══
            // Tới đây ta đã cầm HAI thứ của LƯỢT SẮP CHẠY: `_bridgeCts` và khoá xuyên máy. `TryKillPoc` huỷ
            // token + nhả khoá ⇒ gọi nó ở đây là tự sát: bản 17:04 ngày 18/08/2026 ghi "Đã hủy mở list shop."
            // ngay cùng giây với "Chờ extension nối cầu", Brave mở ra rồi đứng chết ở trang subaccount.
            DongCuaSoCuLai(); // tránh hai Chromium một hồ sơ + hai extension tranh cổng 47821

            // Đặt TRƯỚC khi phóng trình duyệt: từ giây này cửa sổ đã "thuộc về" accountId, và "Chạy đã chọn" /
            // "Dừng đã chọn" phải đóng được nó NGAY CẢ KHI lát cắt còn đang chạy dở.
            _pocAccountId = accountId;

            // Callback ĐỌC XONG PICKER. Đây là chỗ DUY NHẤT còn thấy danh sách shop ĐẦY ĐỦ: vòng chạy lọc
            // `boQuaShop` ngay sau lượt gọi này. Vì thế số shop của lượt FLASH phải được ghi tại đây — nó là
            // mẫu số của luật "ngày đã trọn" (xem `ChotLuotFlash`), và không chỗ nào sau đó dựng lại được.
            void DocXongPicker(IReadOnlyList<ShopListItem> shops)
            {
                if (laLuotFlash)
                {
                    GhiSoShopThayFlash(accountId, KmLichTuDong.NgayDichFlash(System.DateTime.Now), shops.Count);
                }
                DoDanhSachShopVaoLuoi(accountId, shops, luotChayMoi);
            }

            var session = TaoPhienCauNoi(accountId, acc?.PickupAddress, email,
                doVaoLuoiShops ? DocXongPicker : null);
            _bridgeSession = session;

            _services.Log.Append(email, cauBatDau);
            BusyStatus = trangThaiBatDau;

            var login = new OrdersLoginParams(
                acc?.Email ?? EditEmail,
                acc?.Password ?? string.Empty,
                acc?.VerifyEmail,
                acc?.VerifyEmailPassword);

            KetLuanLatCat ketLuan;
            try
            {
                ketLuan = await than(session, login, _bridgeCts.Token);
            }
            finally
            {
                // Gán lại CẢ CẶP, không chỉ Process. Khối này chạy SAU `TryKillPoc` khi người dùng bấm đóng
                // đúng lúc trình duyệt đang phóng — `TryKillPoc` đã xoá `_pocAccountId` về null, mà `Process`
                // thì vừa được gán ở đây. Lệch cặp = cửa sổ sống nhưng vô chủ: `NenDongCuaSoXem` luôn false nên
                // KHÔNG đường nào theo tài khoản đóng nổi nó nữa.
                _pocProcess = session.Process;
                _pocAccountId = accountId;
                TheoDoiCuaSoXemThoat(_pocProcess); // người dùng tự bấm X ⇒ nút phải tự tắt
                BaoCoDoiCuaSoXem(); // cửa sổ để lại là lý do thứ ba bật nút đóng
            }

            // ⚠ HẠN CHẾ ĐÃ BIẾT, KHÔNG sửa trong đợt này (ghi để người sau khỏi tưởng là lỗi mới):
            //  · BusyStatus bị UpdateSelectedSessionStatus xoá trắng khi có BẤT KỲ sự kiện phiên nào — bệnh sẵn
            //    có của cả màn; NHẬT KÝ mới là bản ghi thật, nên câu tổng kết luôn được ghi vào đó trước.
            //  · Huỷ trúng Task.Delay(300) của bước nhả debugger sẽ ném OperationCanceledException, làm mất câu
            //    tổng kết dù danh sách shop đã đọc xong và đã đổ vào lưới.
            //  · _pocAccountId có thể sống sót thành "chủ ma" nếu trình duyệt chưa kịp phóng (Process null) —
            //    vô hại vì NenDongCuaSoXem chỉ dẫn tới TryKillPoc, vốn no-op khi không có tiến trình nào.
            _services.Log.Append(email, ketLuan.Nhat);
            BusyStatus = ketLuan.TrangThai;
        }
        catch (System.OperationCanceledException)
        {
            _services.Log.Append(email, $"Đã hủy {ThuongHoaChuDau(tenViec)}.");
            BusyStatus = $"Đã hủy {ThuongHoaChuDau(tenViec)}.";
        }
        catch (System.Exception ex)
        {
            _services.Log.Append(email, $"Lỗi {ThuongHoaChuDau(tenViec)}: " + ex.Message);
            BusyStatus = $"Lỗi {ThuongHoaChuDau(tenViec)}: " + ex.Message;
        }
        finally
        {
            // ══ GÓI FLASH KHÔNG MƯỢN ĐƯỢC HỒ SƠ ⇒ XOÁ MỐC HOÃN. Phải nằm TRƯỚC `TraHoSo()` bên dưới. ══
            //
            // `NhipLichFlashAsync` đặt `MocChayLaiCheckDonSauFlash = cuối khe` NGAY TRƯỚC khi gọi vào đây, tức
            // trước cả lượt xin khung xuyên máy. Lượt ấy hỏng (máy khác giữ khoá, phiên check đơn không chịu
            // dừng, hết hạn chờ) là hàm `return` sớm mà mốc vẫn nằm đó — rồi `TraHoSo()` ngay dưới sẽ bắt vòng
            // check đơn NẰM CHỜ tới cuối khe cho một gói flash CHƯA HỀ CHẠY. Mất trọn một khe nghỉ, câm lặng.
            //
            // Hỏi `daMuonDuocHoSo`, KHÔNG hỏi kết quả của `than`: mượn được rồi thì gói ĐÃ chiếm khe thật, và
            // trả lại đúng phần khe còn thừa mới là hành vi đúng — kể cả khi gói ném giữa chừng.
            if (laLuotFlash && !daMuonDuocHoSo)
            {
                MocChayLaiCheckDonSauFlash = null;
            }

            // ══ LÁT CẮT "TỰ KẾT THÚC" (KM của Shop): ĐÓNG CỬA SỔ Ở ĐÂY, TRÊN **MỌI** ĐƯỜNG RA. ĐỪNG DỜI LÊN. ══
            //
            // Khác hẳn "Mở list shop" — nút đó CỐ Ý để cửa sổ lại cho người dùng thao tác tay
            // (`dongCuaSoKhiXong == false`, không lối nào ở đây đụng tới nó), nên hồ sơ chỉ được trả khi họ tự
            // đóng. Luồng KM thì không có ai đóng hộ.
            //
            // Bản 19/08/2026 đặt khối này trên ĐƯỜNG THÀNH CÔNG, ngay sau khi ghi câu tổng kết. `than` NÉM
            // (hoặc bị huỷ) là nhảy thẳng xuống `catch` ⇒ cửa sổ Brave còn sống ⇒ `CuaSoXemDangMo == true` ⇒
            // nhánh `TraHoSo()` cuối hàm này cũng bị bỏ qua nốt. Hậu quả lúc 2h sáng: khoá xuyên máy kẹt tới
            // khi tắt app, check đơn KHÔNG BAO GIỜ chạy lại, Brave mở tới sáng — đúng thứ người dùng yêu cầu
            // ngược lại ("sau khi kết thúc chương trình km thì chạy lại flow check đơn").
            //
            // Đặt TRƯỚC lượt `Dispose` bên dưới vì `DongCuaSoCuLai` còn đọc `_bridgeSession.Process` (trong lúc
            // lát cắt chạy, tiến trình nằm ở đó chứ chưa ở `_pocProcess`). Nó xoá `_pocProcess` về null nên
            // nhánh `if (!CuaSoXemDangMo) TraHoSo()` cuối hàm lo luôn phần trả hồ sơ — một cửa, không hai.
            //
            // ⚠ `daMuonDuocHoSo` KHÔNG PHẢI cờ thừa (phản biện vòng 2, 19/08/2026): `MuonHoSoAsync` trả false
            // (máy khác đang giữ khoá, hoặc phiên check đơn không chịu dừng) thì hàm `return` sớm — nhưng
            // `finally` vẫn chạy, và bản đợt 1 đóng cửa sổ ở đây VÔ ĐIỀU KIỆN. Cửa sổ lúc đó là của lượt
            // "Mở list shop" TRƯỚC ĐÓ mà người dùng đang thao tác dở: một lượt KM tự động 2h sáng bị từ chối
            // khoá cũng giết mất nó. Chưa mượn được thì trên máy không có gì thuộc về lượt này để mà đóng.
            if (dongCuaSoKhiXong && daMuonDuocHoSo)
            {
                DongCuaSoCuLai();
            }

            // ══ KHOÁ XUYÊN MÁY ĐƯỢC GIỮ TỚI LÚC **TRẢ HỒ SƠ**, KHÔNG NHẢ Ở ĐÂY. ĐỪNG SỬA. ══
            // Bản 18/08/2026 (sáng) nhả ngay tại `finally` này — cửa sổ để lại sống hàng phút mà khoá đã trống
            // từ giây thứ 5. Hậu quả thật: máy 2 tới chu kỳ giành mất tài khoản, người dùng đóng cửa sổ thì
            // `Sessions.Start` bị hub từ chối ⇒ phiên check đơn CHẾT HẲN với đúng một dòng log, ngay dưới câu
            // vừa hứa "đóng cửa sổ xong sẽ chạy lại".
            // Nay giữ khoá suốt lượt mượn (đúng yêu cầu "1 client / 1 acc / 1 thời điểm"). Nỗi sợ cũ — rò một
            // lease có heartbeat thì kẹt tới khi tắt app — nay đã có ba lưới: `TraHoSo` một cửa,
            // `Process.Exited`, và Job Object KILL_ON_JOB_CLOSE; mà heartbeat cũng chết theo tiến trình app nên
            // ca xấu nhất chỉ còn là lease tự hết hạn ~5'.
            // Ai muốn lật lại quyết định này: đọc CẢ HAI chiều ở trên rồi hãy đổi.
            // Kênh WS phải chết dù cửa sổ còn sống: cổng cầu nối là HẰNG 47821 toàn máy, giữ nó là phiên
            // production kế không mở nổi cổng.
            // ⚠ KHE HỞ CÒN LẠI, ghi để người sau biết (KHÔNG sửa trong đợt này): cửa sổ để lại vẫn thử nối lại
            // cổng đó mỗi 1200ms. Phiên production kế mở cổng thì extension CŨ có thể nối vào trước và chiếm
            // chặng `Ready`, trong khoảnh khắc trước khi `FreeProfile(alsoMatchBridgeExtension: true)` của lượt
            // phóng mới kịp giết nó. Cửa hẹp, nhưng có thật.
            try { _bridgeSession?.Dispose(); } catch { /* bỏ qua */ }
            _bridgeSession = null;
            try { _bridgeCts?.Dispose(); } catch { /* bỏ qua */ }
            _bridgeCts = null;

            _bridgeRunning = false;
            TenLatCatDangChay = null;   // cùng cặp với dòng trên — xem `TenLatCatDangChay`
            TaiKhoanLatCatDangChay = null;   // cùng cặp — xem `TaiKhoanLatCatDangChay`
            _services.LatCatDoi?.Invoke();   // cùng cặp — gương phải bỏ nhãn lát cắt ngay, đừng chờ nhịp 60s
            BaoCoDoiCuaSoXem(); // tắt lại nút nếu không còn gì đang mở

            // KHÔNG để lại cửa sổ nào (lỗi trước lúc mở được, hoặc đã bị huỷ) ⇒ sẽ KHÔNG có đường "đóng cửa sổ"
            // nào chạy nữa, nên phải trả hồ sơ lại cho phiên check đơn NGAY TẠI ĐÂY. Thiếu nhánh này là mượn
            // xong không trả: check đơn của tài khoản đó chết im cho tới khi người dùng tự bấm Chạy.
            if (!CuaSoXemDangMo)
            {
                TraHoSo();
            }

            // ══ TRẢ KHUNG cho máy KIA — trên MỌI đường ra (xong / lỗi / huỷ / hết hạn chờ). ĐỪNG DỜI LÊN. ══
            // `TraHoSo` ở trên chỉ chạy lại phiên của CHÍNH MÁY NÀY; máy bị hub dừng hộ thì chỉ hub bật lại được.
            // Quên nhánh này thì máy kia nằm im tới lượt quét quá hạn (90') — không mất hẳn, nhưng là 90 phút
            // check đơn chết không vì cái gì, và đúng cái hỏng việc này sinh ra để sửa.
            if (daXinKhung && _services.TraKhungKm is { } traKhung)
            {
                try { await traKhung(email); }
                catch (System.Exception ex)
                {
                    // Nuốt: đường dọn dẹp không được ném. Lưới cuối vẫn là lượt quét quá hạn phía hub.
                    _services.Log.Append(email, "Trả khung KM cho máy khác lỗi: " + ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// Hạn CHỜ máy khác nhả khoá sau khi hub đã ra lệnh Dừng cho nó. 3 phút: lệnh đi kèm nhịp heartbeat (~12s),
    /// cộng thời gian phiên kia đóng trình duyệt và nhả khoá trong <c>finally</c> của nó (tới ~30s), cộng đệm.
    /// <para><c>internal</c> + không <c>readonly</c> để test rút xuống — chờ thật 3 phút trong bộ test là không
    /// chấp nhận được. Giá trị mặc định được khoá bằng test riêng.</para>
    /// </summary>
    internal static readonly TimeSpan MacDinhHanChoKhung = TimeSpan.FromMinutes(3);

    /// <summary>Nhịp hỏi lại hub "khoá trống chưa" — mặc định, xem <see cref="MacDinhHanChoKhung"/>.</summary>
    internal static readonly TimeSpan MacDinhNhipHoiKhung = TimeSpan.FromSeconds(10);

    /// <summary>Hạn chờ đang dùng; test rút xuống rồi trả lại. Mặc định khoá bằng <see cref="MacDinhHanChoKhung"/>.</summary>
    internal static TimeSpan HanChoKhungXuyenMay = MacDinhHanChoKhung;

    /// <summary>Nhịp hỏi đang dùng; test rút xuống rồi trả lại.</summary>
    internal static TimeSpan NhipHoiKhungXuyenMay = MacDinhNhipHoiKhung;

    /// <summary>
    /// <b>Chờ KHUNG xuyên máy</b>: nhờ hub dừng phiên check đơn ở máy khác đang giữ khoá tài khoản, rồi hỏi lại
    /// tới khi khoá trống.
    /// <para>Hook chưa rót (app chạy độc lập / chưa có hub) ⇒ <c>true</c> ngay, KHÔNG log gì — y cách
    /// <see cref="XinKhoaXemAsync"/> degrade.</para>
    /// <para>Hết hạn ⇒ <c>false</c>, chỗ gọi bỏ lượt. Ghi rõ tên máy còn giữ: người đọc nhật ký lúc 8h sáng cần
    /// biết đêm qua chờ ai chứ không phải một dòng "không chạy được".</para>
    /// <para><b><paramref name="tenViec"/> nhận từ chỗ gọi, KHÔNG hard-code "KM"</b> (phản biện 25/08/2026,
    /// NHẸ-7): hàm này dùng chung cho MỌI vòng xin khung xuyên máy, nên bản cũ làm lượt combo 06:05 ghi
    /// <i>"BỎ lượt KM đêm nay"</i> — sai cả tên việc lẫn buổi. Vì thế câu chữ ở đây cũng nói <b>"lần này"</b>
    /// thay cho "đêm nay": ba khung nằm ở 02h, 06h và 10h.</para>
    /// </summary>
    /// <param name="tenViec">Tên việc đang xin khung ("KM của Shop" / "Combo Khuyến Mãi") — chính
    /// <c>tenViec</c> của <see cref="ChayLatCatAsync"/>.</param>
    private async Task<bool> ChoKhungXuyenMayAsync(
        string email, string tenViec, System.Threading.CancellationToken ct)
    {
        var xin = _services.XinKhungKm;
        if (xin is null)
        {
            return true;
        }

        var han = DateTime.UtcNow + HanChoKhungXuyenMay;
        var daBao = false;
        while (true)
        {
            var kq = await xin(email, ct).ConfigureAwait(false);
            if (kq.Trong)
            {
                if (daBao)
                {
                    _services.Log.Append(email, $"Máy kia đã dừng check đơn — bắt đầu lượt {tenViec}.");
                }
                return true;
            }

            if (DateTime.UtcNow >= han)
            {
                var loi = $"Chờ {HanChoKhungXuyenMay.TotalMinutes:0} phút mà máy "
                          + (kq.DangDung is { Length: > 0 } m ? $"\"{m}\"" : "khác")
                          + $" chưa nhả khoá tài khoản — BỎ lượt {tenViec} lần này. Máy đó sẽ được cho chạy lại ngay.";
                _services.Log.Append(email, loi);
                BusyStatus = loi;
                return false;
            }

            if (!daBao)
            {
                daBao = true;
                _services.Log.Append(email,
                    "Máy " + (kq.DangDung is { Length: > 0 } m2 ? $"\"{m2}\"" : "khác")
                    + " đang chạy check đơn cho tài khoản này — đã nhờ hub bảo nó dừng, đang chờ nhả khoá. "
                    + $"Xong lượt {tenViec} sẽ tự cho nó chạy lại.");
                BusyStatus = $"Đang chờ máy khác dừng check đơn để chạy {tenViec}...";
            }

            await Task.Delay(NhipHoiKhungXuyenMay, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Lưu danh sách shop mà lát cắt vừa đọc được rồi báo tab "Shops" dựng lưới NGAY — thân của callback
    /// <c>onShopListRead</c> mà "Mở list shop" và vòng KM/Combo rót vào phiên cầu nối.
    /// <para><b><paramref name="luotChayMoi"/> là phần quan trọng nhất của hàm này.</b> Nút "Mở list shop" đọc
    /// danh sách y hệt vòng chạy thật nhưng DỪNG luôn tại trang chọn shop, không lặp shop nào — nó truyền
    /// <c>false</c>. Truyền <c>true</c> ở đó thì <c>OnShopListChanged</c> coi là LƯỢT CHẠY MỚI và xoá sạch dấu
    /// tick tiến độ của tài khoản: bấm một nút "chỉ xem" là mất tick của cả 12 shop, âm thầm, không dòng log nào.</para>
    /// <para>Vòng KM/Combo/Flash truyền <c>false</c>: tick check đơn ở ô Chuẩn bị hàng, tick chương trình ở ô
    /// loại đó (theo dòng <c>km_program_daily</c>) — không còn một dấu V dùng chung nên không được xóa
    /// <c>_shopDaCheck</c> khi bắt đầu vòng chương trình. Lượt <c>UpsertShops</c> vẫn dựng dòng cho shop mới
    /// chưa từng qua check đơn: thiếu nó, số nằm trong DB mà không ô nào hiện.</para>
    /// </summary>
    internal void DoDanhSachShopVaoLuoi(
        long accountId, IReadOnlyList<ShopListItem> shops, bool luotChayMoi = false)
    {
        _services.Results.UpsertShops(accountId, shops);
        _services.RaiseShopListChanged(accountId, luotChayMoi);
    }

    /// <summary>
    /// XIN khoá chạy tài khoản xuyên máy cho cửa sổ lát cắt (hook <see cref="AppServices.AcquireAccountLease"/>) —
    /// cùng khoá, cùng câu chữ từ chối với phiên production (<see cref="AccountSession.XinKhoaChayAsync"/>).
    /// <para>Hook chưa rót (app chạy độc lập / chưa có hub) ⇒ true, KHÔNG log gì thêm — y như phiên production.</para>
    /// </summary>
    /// <returns><c>false</c> = máy khác đang giữ ⇒ chỗ gọi KHÔNG mở gì.</returns>
    private async Task<bool> XinKhoaXemAsync(string login, System.Threading.CancellationToken ct)
    {
        var acquire = _services.AcquireAccountLease;
        if (acquire is null)
        {
            return true;
        }

        var kq = await acquire(login, ct);
        if (!kq.Ok)
        {
            var msg = AccountSession.CauBiTuChoiKhoa(kq.HolderMachine);
            _services.Log.Append(login, msg);
            BusyStatus = msg;
            return false;
        }

        _loginKhoaXem = login;
        System.Threading.Interlocked.Exchange(ref _dangGiuKhoaXem, 1); // đã giữ → mọi đường dọn dẹp PHẢI nhả
        return true;
    }

    /// <summary>
    /// NHẢ khoá chạy tài khoản của cửa sổ lát cắt — ĐÚNG MỘT lần nhờ cờ <see cref="_dangGiuKhoaXem"/> (nhiều đường
    /// dọn dẹp cùng gọi). Nhả sót = tài khoản bị coi là "đang chạy ở máy này" tới khi TẮT APP (lease có heartbeat,
    /// không tự hết hạn) ⇒ máy kia bỏ qua tài khoản đó vĩnh viễn.
    /// <para><b>KHÔNG được gọi thẳng từ khối <c>finally</c> của <see cref="ChayLatCatAsync"/></b> — từ vòng sửa
    /// 18/08/2026 khoá được GIỮ tới lúc trả hồ sơ, xem khối "ĐỪNG SỬA" ở đó. Hai lối vào hợp lệ:
    /// <see cref="TraHoSo"/> (qua <see cref="NoiTiepNhaKhoa"/>, nối đuôi để không chồng lượt) và
    /// <see cref="MuonHoSoAsync"/> (nhả khoá của lượt mượn CŨ trước khi xin khoá mới).</para>
    /// <para>Lỗi nuốt hết + ghi nhật ký: nhả hỏng KHÔNG được phá đường dọn dẹp của lệnh.</para>
    /// </summary>
    private async Task NhaKhoaXemAsync()
    {
        if (System.Threading.Interlocked.Exchange(ref _dangGiuKhoaXem, 0) != 1)
        {
            return; // chưa từng giành được / đã nhả
        }

        var login = _loginKhoaXem;
        _loginKhoaXem = null;
        var release = _services.ReleaseAccountLease;
        if (release is null || login is null)
        {
            return;
        }

        try { await release(login); }
        catch (System.Exception ex) { _services.Log.Append(login, "Nhả khóa tài khoản lỗi: " + ex.ToString()); }
    }

    // ══ ĐÃ XOÁ: bản "bắn đi rồi quên" của NhaKhoaXemAsync (gọi từ TryKillPoc). ĐỪNG DỰNG LẠI. ══
    // Nó tự nhận là "lưới an toàn cho lối ra sớm", nhưng đọc kỹ thì nó KHÔNG BAO GIỜ phục vụ ca đó: cờ
    // `_dangGiuKhoaXem` chỉ = 1 trong khoảng giữa `XinKhoaXemAsync` và `finally` của `ChayLatCatAsync` — cả hai
    // đều nằm TRONG khung ⇒ `_dangGiuKhoaXem == 1` kéo theo `_bridgeRunning == true`. Tức 100% số lần nhánh đó
    // nổ là ca NGUY HIỂM, 0% là ca nó tự nhận phục vụ.
    // Ca nguy hiểm: người dùng bấm "Đóng cửa sổ" GIỮA CHỪNG (nút đó bật được chính nhờ `CoTheDongCuaSo`) →
    // `TryKillPoc` bắn DELETE rồi TIÊU CỜ bằng Interlocked → `await NhaKhoaXemAsync()` trong `finally` thấy cờ
    // đã 0 nên trả về NGAY → `_bridgeRunning = false` trong lúc DELETE còn đang bay (HubClient hạn 8s) → người
    // dùng bấm "Chạy", phiên production reserve được lease, rồi DELETE cũ hạ cánh xoá đúng dòng vừa cấp. Đúng
    // cái mà việc thêm `await` sinh ra để chặn, chỉ là đi vào bằng cửa khác.
    // Nay khung LUÔN là nơi nhả, và nhả có `await`.

    /// <summary>
    /// Dựng một <see cref="OrdersBridgeSession"/> cho tài khoản đang xem. Công thức hồ sơ Y HỆT
    /// <c>AccountSession</c>: baseDir = thư mục <c>Database.Path</c>, kind theo browserChoice ở Cài đặt.
    /// <para>Tách riêng để mọi nơi cần dựng phiên đi CHUNG một công thức hồ sơ — hai nơi tự dựng đường riêng là
    /// hai công thức trôi lệch, mà lệch hồ sơ nghĩa là mở NHẦM profile: mất cookie, phải đăng nhập lại từ đầu.</para>
    /// </summary>
    /// <param name="onShopListRead">Callback đổ danh sách shop đọc được vào lưới tab "Shops" — CHỈ "Mở list shop"
    /// truyền vào. "Chạy thử (bridge)" cố ý để null: nó là lát cắt kiểm chứng, chỉ đọc, không ghi DB.</param>
    private OrdersBridgeSession TaoPhienCauNoi(long accountId, string? province, string email,
        Action<IReadOnlyList<ShopListItem>>? onShopListRead = null)
    {
        var baseDir = System.IO.Path.GetDirectoryName(_services.Database.Path) ?? ".";
        var userDataDir = BrowserProfilePaths.ForAccount(baseDir, accountId, BrowserLocator.LoaiHoSo);
        var invoiceDir = _services.Settings.GetInvoiceFolder(); // GĐ3: thư mục lưu phiếu (Cài đặt)
        return new OrdersBridgeSession(userDataDir,
            m => _services.Log.Append(email, m), invoiceDir, province, onShopListRead: onShopListRead,
            // Ba cột KM | Combo | Flash Sale. Rót cho MỌI lát cắt, không cần cờ như `doVaoLuoiShops`: callback này
            // chỉ được gọi TỪ TRONG vòng tạo chương trình, nên lệnh "Chạy thử (bridge)"/"Mở list shop" không bao
            // giờ chạm tới nó.
            onShopChuongTrinh: (loai, shopLabel, dangChay, soSanPham)
                => GhiTienDoChuongTrinh(accountId, loai, shopLabel, dangChay, soSanPham));
    }

    /// <summary>
    /// Thân callback <c>onShopChuongTrinh</c> mà mọi lát cắt rót vào phiên cầu nối — vòng KM/Combo gọi khi BẮT ĐẦU
    /// và khi XONG mỗi shop.
    /// <list type="number">
    /// <item>XONG shop ⇒ GHI DB ngay — kể cả <paramref name="soSanPham"/> = 0 (shop bỏ qua / hỏng). Ghi ở đây chứ
    /// KHÔNG gom về cuối lượt: vòng có năm lối ra sớm, gom lại là mất sạch số khi captcha/hết khung.</item>
    /// <item>Bắn <see cref="AppServices.ShopProgramChanged"/> — SAU lượt ghi DB, để lượt nạp lại lưới mà nó kích
    /// đọc được đúng con số vừa ghi (ô loại đó hiện số + tick nhỏ). Không đụng <c>ShopCheckChanged</c> /
    /// tick ô Chuẩn bị hàng.</item>
    /// </list>
    /// <c>day</c> là NGÀY HÔM NAY giờ địa phương, cùng khuôn <c>prepare_daily</c> (xem
    /// <c>AccountSession.onOrderPrepared</c>) — không phải ngày đang lọc ở ô lưới.
    /// </summary>
    /// <param name="soSanPham"><c>null</c> = KHÔNG có số để ghi ⇒ <b>không đụng DB</b>. Hai ca: lượt bắn BẮT ĐẦU
    /// shop, và shop bị HUỶ giữa chừng (bấm Dừng lúc shop đang chờ) — ghi <c>0</c> cho ca huỷ là nói "đã chạy mà
    /// không tạo được gì", làm mờ ranh giới <c>—</c>/<c>0</c>. Xem <c>OrdersBridgeSession</c> tham số cùng tên.</param>
    internal void GhiTienDoChuongTrinh(
        long accountId, LoaiChuongTrinh loai, string shopLabel, bool dangChay, int? soSanPham)
    {
        if (!dangChay && soSanPham is int so)
        {
            _services.Results.CongSoSanPhamChuongTrinh(
                accountId, shopLabel,
                System.DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                loai, so);
        }

        _services.RaiseShopProgramChanged(accountId, shopLabel, loai, dangChay, soSanPham ?? 0);
    }

    /// <summary>
    /// <b>HUỶ lát cắt đang chạy + đóng cửa sổ</b> — đường của nút "Đóng cửa sổ" và các nút Dừng.
    /// Đây là hàm "dừng mọi thứ lại", KHÔNG phải hàm dọn chỗ.
    /// <para><b>CỐ Ý KHÔNG nhả khoá xuyên máy ở đây.</b> Huỷ token là khung sẽ thoát qua <c>finally</c> của
    /// <see cref="ChayLatCatAsync"/> ngay sau đó, mà chỗ ấy nhả khoá CÓ <c>await</c>. Tự nhả thêm ở đây chỉ tổ
    /// tiêu cờ <c>_dangGiuKhoaXem</c> trước, biến lượt <c>await</c> kia thành no-op và thả DELETE bay tự do —
    /// xem khối ghi chú "ĐÃ XOÁ" ngay trên <see cref="TaoPhienCauNoi"/>.</para>
    /// <para><b>ĐỪNG gọi hàm này từ trong <see cref="ChayLatCatAsync"/> sau khi đã tạo <c>_bridgeCts</c>.</b>
    /// Dòng đầu của nó huỷ ĐÚNG token của lượt đang mở — bản 17:04 ngày 18/08/2026 làm vậy và mọi cú bấm đều tự
    /// sát trong cùng một giây ("Đã hủy mở list shop." ngay sau "Chờ extension nối cầu"), để lại Brave mồ côi
    /// đứng ở trang subaccount. Chỗ đó dùng <see cref="DongCuaSoCuLai"/>.</para>
    /// </summary>
    private void TryKillPoc()
    {
        try { _bridgeCts?.Cancel(); } catch { /* bỏ qua */ }
        DongCuaSoCuLai();
    }

    /// <summary>
    /// <b>CHỈ đóng cửa sổ trình duyệt còn sót lại</b> rồi quên nó đi — không huỷ token, không nhả khoá.
    /// <para>Tách khỏi <see cref="TryKillPoc"/> vì <see cref="ChayLatCatAsync"/> cần đúng phần này và KHÔNG được
    /// dính hai phần kia: token vừa tạo là của lượt sắp chạy, còn khoá vừa xin cũng là của lượt sắp chạy. Tách
    /// hàm chứ không thêm cờ boolean thứ hai vào <c>TryKillPoc</c> — hai cờ trên một hàm dọn dẹp là chỗ lần sau
    /// lại sai.</para>
    /// </summary>
    private void DongCuaSoCuLai()
    {
        // Trong lúc lát cắt đang chạy, tiến trình nằm ở _bridgeSession.Process (_pocProcess chỉ set sau khi xong).
        var bridgeProc = _bridgeSession?.Process;
        try { if (bridgeProc is { HasExited: false }) bridgeProc.Kill(entireProcessTree: true); }
        catch { /* bỏ qua */ }

        try { if (_pocProcess is { HasExited: false }) _pocProcess.Kill(entireProcessTree: true); }
        catch { /* bỏ qua */ }
        _pocProcess = null;
        _pocAccountId = null;
        // Cửa sổ vừa đóng là nguồn của cả ba cờ — không báo thì nút "Đóng cửa sổ" ở lại sáng dù chẳng còn gì.
        BaoCoDoiCuaSoXem();
    }

    /// <summary>
    /// Theo dõi cửa sổ để lại: người dùng tự bấm <b>X</b> đóng Brave thì QUÊN nó đi và cập nhật nút.
    /// <para>Không có đường này thì nút "Đóng cửa sổ" kẹt ở trạng thái bật mãi (không ai nghe
    /// <c>Process.Exited</c> ở tầng VM), bấm vào chẳng thấy gì phản hồi — mà đóng bằng X lại là thao tác tự
    /// nhiên nhất của chính tính năng này. Khuôn lấy từ <c>OrdersBridgeSession.TheoDoiTrinhDuyetThoat</c>.</para>
    /// <para><b>Chốt danh tính</b>: chỉ dọn khi <see cref="_pocProcess"/> VẪN là đúng tiến trình vừa thoát —
    /// lượt "Mở list shop" kế đã thay cửa sổ khác thì sự kiện muộn của cửa sổ cũ không được xoá cửa sổ mới.</para>
    /// <para>Best-effort tuyệt đối: hỏng ở đây tuyệt đối không được ảnh hưởng lệnh đang chạy.</para>
    /// </summary>
    private void TheoDoiCuaSoXemThoat(System.Diagnostics.Process? p)
    {
        if (p is null) { return; }
        try
        {
            p.EnableRaisingEvents = true;
            p.Exited += (_, _) => RunOnUi(() =>
            {
                if (!ReferenceEquals(_pocProcess, p)) { return; }
                _pocProcess = null;
                _pocAccountId = null;
                BaoCoDoiCuaSoXem();
                // Người dùng tự bấm X — cũng là "cửa sổ đã đóng", nên phải trả hồ sơ lại cho phiên check đơn.
                TraHoSo();
            });
        }
        catch { /* tiến trình có thể đã chết / không gắn được — cờ vẫn tự đúng ở nhịp UpdateSelectedSessionStatus kế */ }
    }

    // ===================== Trạng thái phiên đổ về form + cookie bắt được =====================

    /// <summary>
    /// Phiên "SẴN SÀNG THAO TÁC" theo CỜ TƯỜNG MINH <see cref="IAccountSession.ReadyForActions"/> của phiên.
    /// <b>Căn cứ:</b> cờ đó chỉ bật <c>true</c> tại đúng điểm sau khi luồng tự-đăng-nhập (<c>TryHumanLoginAsync</c>,
    /// đã await xong) hoàn tất VÀ đọc được số "Chờ Lấy Hàng" lần đầu của lần mở hiện tại — và được ĐẶT LẠI
    /// false ở đầu mỗi lần mở/relaunch + khi Stopped/Error (xem <c>AccountSession._readyForActions</c>). KHÔNG
    /// suy từ <c>ToShipCount != null</c> nữa vì số đơn không reset khi relaunch → dễ "sẵn sàng ảo" ngay trong
    /// lúc đang đăng nhập lại. Vẫn kèm <c>state == Running</c> làm lớp chốt (phòng cờ lỡ sót). Hàm thuần (test được).
    /// </summary>
    public static bool IsSessionReadyForActions(SessionState state, bool readyForActions)
        => state == SessionState.Running && readyForActions;

    /// <summary>
    /// Xử lý sự kiện đổi trạng thái của các phiên (có thể đến từ thread nền) — marshal về UI thread rồi
    /// đổ trạng thái phiên của tài khoản đang chọn vào ô hiển thị + cập nhật nút.
    /// </summary>
    private void OnSessionsChanged() => RunOnUi(() =>
    {
        // Đổ trạng thái phiên vào TỪNG dòng (chấm chạy / "Chờ lấy: N") + cập nhật ô hiển thị của form.
        SyncAllRows();
        UpdateSelectedSessionStatus();
    });

    /// <summary>
    /// Đồng bộ trạng thái phiên vào mọi dòng đang hiển thị. LUÔN chạy trên UI thread (gọi từ
    /// <see cref="RunOnUi"/>) — chỉ đọc <see cref="Accounts"/> và set thuộc tính row, KHÔNG cấu trúc lại
    /// ObservableCollection từ thread nền.
    /// </summary>
    private void SyncAllRows()
    {
        foreach (var row in Accounts)
        {
            row.SyncFromSession(_services.Sessions.Get(row.Id));
        }
    }

    /// <summary>Đổ trạng thái/số đơn của phiên theo tài khoản ĐANG CHỌN vào ô hiển thị; cập nhật nút mở/dừng.</summary>
    private void UpdateSelectedSessionStatus()
    {
        var id = _editingId ?? SelectedRow?.Id;
        var session = id is long sid ? _services.Sessions.Get(sid) : null;

        BusyStatus = session?.StatusText;
        OrderStatus = FormatOrderStatus(session?.ToShipCount);

        OnPropertyChanged(nameof(CanStopSeller));
        OnPropertyChanged(nameof(CanRun));
        BaoCoDoiCuaSoXem(); // gồm cả CanStop
    }

    /// <summary>Định dạng dòng theo dõi đơn "Chờ Lấy Hàng" từ số đọc được (null = ẩn).</summary>
    private static string? FormatOrderStatus(int? count)
    {
        if (count is not int n)
        {
            return null;
        }

        return n > 0
            ? $"Chờ Lấy Hàng: {n} đơn — vẫn theo dõi mỗi 30'."
            : "Chờ Lấy Hàng: 0 — kiểm lại sau 30'.";
    }

    /// <summary>Kết quả của thao tác lưu cookie đã bắt được vào tài khoản.</summary>
    public enum SaveCookieResult
    {
        /// <summary>JSON không chứa cookie nào (người dùng có thể chưa đăng nhập xong).</summary>
        NoCookie,

        /// <summary>Không còn tài khoản targetId trong DB (có thể đã bị xóa).</summary>
        AccountMissing,

        /// <summary>Đã ghi cookie vào tài khoản.</summary>
        Saved
    }

    /// <summary>
    /// Ghi chuỗi cookie JSON đã bắt được vào ĐÚNG tài khoản <paramref name="targetId"/>. KHÔNG đọc lại
    /// <c>_editingId</c> nên không bị ảnh hưởng khi người dùng đổi chọn/tạo mới trong lúc chờ browser
    /// (chống race ghi nhầm/crash). Tách khỏi Playwright để test được ở mức ViewModel.
    /// </summary>
    /// <remarks>
    /// Luôn dựng lại danh sách (<see cref="RefreshList"/>) để instance trong <see cref="Accounts"/> có
    /// cookie mới — tránh mất cookie khi người dùng chọn lại tài khoản (instance cũ có Cookie rỗng rồi
    /// bị Save ghi đè về null). Chỉ cập nhật FORM và kéo lựa chọn về targetId khi người dùng VẪN đang
    /// mở đúng tài khoản đó; nếu đã chuyển đi thì vẫn lưu DB cho targetId nhưng giữ nguyên form/lựa chọn.
    /// </remarks>
    public SaveCookieResult SaveCapturedCookie(long targetId, string cookieJson)
    {
        if (CookieJson.Deserialize(cookieJson).Count == 0)
        {
            return SaveCookieResult.NoCookie;
        }

        var acc = _services.Accounts.GetById(targetId);
        if (acc is null)
        {
            return SaveCookieResult.AccountMissing;
        }

        acc.Cookie = cookieJson;
        _services.Accounts.Update(acc);

        // Làm mới cache trước khi dựng lại danh sách.
        _all = _services.Accounts.GetAll();

        if (_editingId == targetId)
        {
            // Người dùng vẫn đang mở tài khoản này → cập nhật form + chọn lại instance mới có cookie.
            EditCookie = cookieJson;
            UpdatedAtText = FormatDate(acc.UpdatedAt);
            RefreshList(targetId);
        }
        else
        {
            // Đã chuyển sang tài khoản khác / đang tạo mới → dựng lại danh sách (để instance của
            // targetId có cookie) nhưng giữ nguyên lựa chọn & form hiện tại.
            RefreshList(_editingId ?? SelectedRow?.Id);
        }

        return SaveCookieResult.Saved;
    }
}
