using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XuLyDonShopee.Core.Data;
using XuLyDonShopee.Core.Services;

namespace XuLyDonShopee.App.ViewModels;

/// <summary>
/// Form "KM của Shop" — nhập thông số một chương trình khuyến mãi rồi LƯU theo tài khoản.
/// <para><b>Nút "Chạy chương trình" chạy THẬT</b>: nơi gọi (<c>AccountsViewModel.MoFormKm</c>) mở trình duyệt
/// và tạo chương trình khuyến mãi trên MỌI shop của tài khoản. Form chỉ lo kiểm đầu vào + câu tổng kết.</para>
/// <para>Mọi luật (sinh tên, kiểm đầu vào, câu tổng kết) đều là HÀM THUẦN <c>static</c> — chúng là phần dễ sai
/// nhất và là phần duy nhất test được mà không cần dựng cửa sổ.</para>
/// </summary>
public partial class KmDialogViewModel : ObservableObject
{
    /// <summary>Trần độ dài tên chương trình — Shopee chặn ở 150 (ô <c>input[maxlength="150"]</c>).</summary>
    public const int TranDoDaiTen = 150;

    /// <summary>
    /// <b>Trần độ dài tên COMBO — 25 ký tự</b>, KHÔNG dùng chung <see cref="TranDoDaiTen"/> (150).
    /// <para>Ô tên của form Combo Khuyến Mãi mang <c>maxlength="25"</c> — trần CỨNG của Shopee. Khuôn tên của
    /// Chương Trình Của Shop (<c>KM ngày 15-02-2027 từ 11:00</c>, <b>27 ký tự</b> kể từ 26/08/2026 — trước đó là
    /// 35, khi tên còn mang cả giờ kết thúc) VẪN KHÔNG nhét vừa, nên combo giữ khuôn riêng
    /// <see cref="SinhTenComboMacDinh"/> (<c>Combo 15-02-2027</c>, 16 ký tự).</para>
    /// <para>Gửi dài hơn trần là <b>mất phần đuôi âm thầm</b> — đúng bài học <c>maxlength=150</c> của KM.</para>
    /// </summary>
    public const int TranDoDaiTenCombo = 25;

    /// <summary>Giờ mặc định — người dùng chốt 9:30–10:30.</summary>
    public const string GioBatDauMacDinh = "09:30";
    public const string GioKetThucMacDinh = "10:30";

    private readonly long _accountId;
    private readonly string _login;
    private readonly int _soShop;

    /// <summary>Id suất đơn hàng của máy này; rỗng = không có hub ⇒ không nhận chủ trì được (và cũng không cần).</summary>
    private readonly string _mayNay;

    /// <summary>Id suất đơn hàng của máy này ĐÚNG NHƯ FORM ĐANG DÙNG — phơi ra để đường lưu (<c>MoFormKm</c>)
    /// đóng dấu bằng chính giá trị form vừa hiển thị, thay vì gọi hook <c>MayNay</c> lần thứ hai: hub bật/tắt
    /// được trong lúc modal đang mở, và hai lượt đọc khác nhau là dòng "Máy chủ trì: … (máy này)" người dùng
    /// vừa đọc nói một đằng, giá trị ghi xuống DB một nẻo.</summary>
    internal string MayNay => _mayNay;

    /// <summary>Tra TÊN máy từ id suất (hook <c>AppServices.TenMay</c>); null = không tra được ⇒ hiện id ngắn.
    /// Xem <see cref="MoTaChuTri"/>.</summary>
    private readonly Func<string, string?>? _traTenMay;

    /// <summary>
    /// <b>BẢN NẠP LÚC MỞ FORM</b> — dòng <c>km_config</c> đúng như người dùng đang NHÌN THẤY.
    /// <para><b>Đây là vế đúng của câu hỏi "người dùng có sửa gì không"</b> (phản biện vòng 3, 22/08/2026).
    /// Bản vòng 2 so cấu hình trên form với dòng ĐỌC LẠI LÚC LƯU — đó là câu hỏi khác: "DB có khác form
    /// không". Hai câu đó lệch nhau đúng bằng cửa sổ 60 giây của worker đồng bộ: máy B mở form (25%) chỉ để
    /// xem → máy A đổi 25%→40% và đẩy lên → worker của B kéo về (nhịp Timer, modal KHÔNG chặn nó) → B bấm Đóng
    /// mà không chạm gì ⇒ "DB khác form" ⇒ B lưu 25%, đóng dấu mốc mới, cướp chủ trì ⇒ <b>A bị lùi về
    /// 25%</b>. Đúng hình dạng lỗi vòng 1, chỉ dịch vào một cửa sổ hẹp hơn.</para>
    /// </summary>
    public KmConfig BanNap { get; }

    /// <summary>Người dùng đã TỰ SỬA tên ⇒ thôi sinh lại. Sửa tay thì tôn trọng, đừng ghi đè.</summary>
    private bool _nguoiDungTuSuaTen;

    /// <summary>Đang tự gán tên (không tính là người dùng sửa).</summary>
    private bool _dangTuSinhTen;

    /// <summary>Ctor cho designer/XAML previewer.</summary>
    public KmDialogViewModel() : this(0, "(chưa chọn)", 0, KmConfigRepository.MacDinh(0))
    {
    }

    /// <param name="mayNay">Id suất đơn hàng của máy này (rỗng = app chạy độc lập / chưa có hub) — để form nói
    /// được "máy chủ trì có phải máy này không" và để nút nhận chủ trì có cái mà đóng dấu.</param>
    /// <param name="traTenMay">Tra TÊN máy từ id suất (hook <c>AppServices.TenMay</c>); null = không có hub ⇒
    /// form hiện id NGẮN. Không bao giờ hiện id thô — xem <see cref="MoTaChuTri"/>.</param>
    public KmDialogViewModel(
        long accountId, string login, int soShop, KmConfig cauHinh, string mayNay = "",
        Func<string, string?>? traTenMay = null)
    {
        _accountId = accountId;
        _login = login;
        _soShop = soShop;
        _mayNay = mayNay ?? "";
        _traTenMay = traTenMay;
        BanNap = cauHinh;

        // NGÀY trên form nay là ngày MỞ ĐẦU khoảng — mặc định HÔM NAY (người dùng đính chính 26/08/2026: chương
        // trình chạy MỘT KHOẢNG 180 ngày, không phải một lát cắt một tiếng ở ngày +180). Ngày đóng khoảng suy ra
        // từ đây (`NgayKetThuc`), không có ô nhập riêng. Dùng chung đúng hàm của lượt tự động — hai đường tự
        // tính lấy là hai hằng số sẽ trôi khỏi nhau.
        _ngay = KmLichTuDong.NgayBatDau(DateTime.Now);
        _gioBatDau = cauHinh.GioBatDau;
        _gioKetThuc = cauHinh.GioKetThuc;
        _phanTram = cauHinh.PhanTram;
        _tuDong = cauHinh.TuDong;
        _soNgay = cauHinh.SoNgay;
        _lanChayCuoi = cauHinh.LanChayCuoi;
        _lanThuTuDong = cauHinh.LanThuTuDong;

        // COMBO — 7 trường cấu hình nạp từ DB, 2 mốc chạy MANG THEO để lượt lưu không xoá trắng chúng (cùng
        // lý do với `_lanThuTuDong` ở trên).
        _comboBat = cauHinh.ComboBat;
        _comboGioBatDau = cauHinh.ComboGioBatDau;
        _comboGioKetThuc = cauHinh.ComboGioKetThuc;
        _comboLoai = cauHinh.ComboLoai;
        _comboMuaSl = cauHinh.ComboMuaSl;
        _comboGiam = cauHinh.ComboGiam;
        _comboGioiHan = cauHinh.ComboGioiHan;
        _comboLanChayCuoi = cauHinh.ComboLanChayCuoi;
        _comboLanThuTuDong = cauHinh.ComboLanThuTuDong;

        // FLASH SALE — chỉ MỘT công tắc; hai mốc chạy MANG THEO để lượt lưu không xoá trắng chúng (cùng lý do
        // với `_lanThuTuDong`). Không có ô thông số nào: ngày đích suy từ chính ngày chạy (hôm nay + 1).
        _flashBat = cauHinh.FlashBat;
        _flashLanChayCuoi = cauHinh.FlashLanChayCuoi;
        _flashLanThuTuDong = cauHinh.FlashLanThuTuDong;
        // HAI THÔNG SỐ FLASH (03/09/2026) — người dùng gõ được, nên chúng phải NẠP TỪ DB. Câu "vòng này không
        // có thông số nào để nhập" ở khối trên đã hết đúng từ hôm nay.
        _flashPhanTramGiam = cauHinh.FlashPhanTramGiam;
        _flashSoLuongThamGia = cauHinh.FlashSoLuongThamGia;

        // Tên khởi tạo theo ngày/giờ vừa nạp; chưa tính là người dùng sửa.
        _tenChuongTrinh = SinhTenMacDinh(_ngay, _gioBatDau);
    }

    [ObservableProperty] private string _tenChuongTrinh;
    [ObservableProperty] private DateTime _ngay;
    [ObservableProperty] private string _gioBatDau;
    /// <summary>
    /// <b>⚠ TRƯỜNG CHẾT trên form từ 26/08/2026</b> — không còn ô nhập, không đi xuống Shopee, không vào tên
    /// chương trình. Chỉ còn sống để CHỞ giá trị cũ của cột <c>km_config.gio_ket_thuc</c> qua một lượt mở/đóng
    /// form mà không xoá trắng nó (cột + trường DTO hub cố ý GIỮ: xoá là kéo theo migration + hợp đồng hub +
    /// hàng chục test, đổi lấy đúng một cột không hại ai).
    /// </summary>
    [ObservableProperty] private string _gioKetThuc;
    [ObservableProperty] private int _phanTram;
    [ObservableProperty] private bool _tuDong;
    [ObservableProperty] private int _soNgay;

    /// <summary>Mốc chạy gần nhất đọc từ DB (lịch tự động dùng); form chỉ hiển thị gián tiếp qua câu tổng kết.</summary>
    private DateTime? _lanChayCuoi;

    /// <summary>Mốc lượt TỰ ĐỘNG thử gần nhất, đọc từ DB. Form KHÔNG đụng tới — nhưng phải MANG THEO khi lưu,
    /// kẻo mỗi lần bấm Đóng là xoá trắng mốc và lịch đêm nay lại thử thêm lượt nữa.</summary>
    private DateTime? _lanThuTuDong;

    // ══════════════════════════ COMBO KHUYẾN MÃI (25/08/2026) ══════════════════════════

    [ObservableProperty] private bool _comboBat;
    [ObservableProperty] private string _comboGioBatDau;
    /// <summary><b>⚠ TRƯỜNG CHẾT</b>, y <see cref="GioKetThuc"/> — chỉ chở giá trị cột
    /// <c>km_config.combo_gio_ket_thuc</c> qua lượt mở/đóng form.</summary>
    [ObservableProperty] private string _comboGioKetThuc;
    [ObservableProperty] private int _comboLoai;
    [ObservableProperty] private int _comboMuaSl;
    [ObservableProperty] private int _comboGiam;
    [ObservableProperty] private int _comboGioiHan;

    /// <summary>Hai mốc chạy của vòng COMBO — form KHÔNG đụng, chỉ mang theo khi lưu (xem
    /// <see cref="_lanThuTuDong"/> cho cùng lý do).</summary>
    private DateTime? _comboLanChayCuoi;
    private DateTime? _comboLanThuTuDong;

    /// <summary>
    /// <b>Luồng combo đã chạy được chưa</b> — phơi <see cref="KmLichTuDong.ComboSanSang"/> cho XAML bind, để ô
    /// tick "Combo Khuyến Mãi — chạy tự động" KHOÁ khi chưa sẵn sàng.
    /// <para><b>Vì sao form phải khoá chứ không chỉ chặn ở lịch</b> (phản biện 25/08/2026, NẶNG-1): ô tick trông
    /// bình thường là một CÔNG TẮC ĐÃ LÊN ĐẠN — người dùng bật xong rồi ngồi chờ mỗi sáng một vòng combo không
    /// bao giờ tới. Chặn câm ở <see cref="KmLichTuDong.VongBat"/> giữ máy an toàn nhưng không nói cho ai biết vì
    /// sao; dòng chữ cạnh ô tick mới là phần nói ra.</para>
    /// </summary>
    public bool ComboSanSang => KmLichTuDong.ComboSanSang;

    /// <summary>Ô thông số combo chỉ có nghĩa khi đã bật vòng combo.</summary>
    public bool ChoNhapCombo => ComboBat;

    // ══════════════════════════ FLASH SALE CỦA SHOP (28/08/2026) ══════════════════════════

    /// <summary>Công tắc vòng FLASH — ô tick của tab "Flash Sale". Ngày đích là hôm nay + 1, khung giờ do Shopee
    /// phát hành, chu kỳ CỨNG mỗi ngày; hai thứ người dùng gõ được là
    /// <see cref="FlashPhanTramGiam"/>/<see cref="FlashSoLuongThamGia"/>.</summary>
    [ObservableProperty] private bool _flashBat;

    /// <summary>%GIẢM áp HÀNG LOẠT cho mọi sản phẩm của mọi khung — ô thứ NHẤT của khối "Chỉnh sửa hàng loạt"
    /// trên form flash Shopee. Trước 03/09/2026 là hằng <c>PHAN_TRAM_GIAM_FLASH = 5</c> trong extension; người
    /// dùng chốt "cho phép nhập từ client, lần sau là 10% thì không phải sửa code".</summary>
    [ObservableProperty] private int _flashPhanTramGiam;

    /// <summary>"SL sản phẩm khuyến mãi" áp HÀNG LOẠT — ô thứ HAI của cùng khối. <b>KHÔNG PHẢI</b> số sản phẩm
    /// mỗi khung (vẫn 1 trang 10 SP bán chạy) — người dùng đính chính đúng điểm này 03/09/2026.</summary>
    [ObservableProperty] private int _flashSoLuongThamGia;

    /// <summary>Hai mốc chạy của vòng FLASH — form KHÔNG đụng, chỉ mang theo khi lưu.
    /// <para><b>Bỏ mang theo <c>_flashLanThuTuDong</c> là ca hỏng NẶNG</b>: mỗi lần bấm Đóng form sẽ xoá trắng
    /// mốc thử ⇒ vế GIÃN CÁCH GÓI của <c>KmLichTuDong.DenGioGoiFlash</c> mất chỗ đo ⇒ nhịp 60 giây phóng lại
    /// một lượt Brave nữa ngay lập tức.</para></summary>
    private DateTime? _flashLanChayCuoi;
    private DateTime? _flashLanThuTuDong;

    /// <summary>
    /// <b>Luồng flash đã chạy được chưa</b> — phơi <see cref="KmLichTuDong.FlashSanSang"/> cho XAML bind, y
    /// khuôn <see cref="ComboSanSang"/> (bài học "công tắc đã lên đạn" 25/08/2026).
    /// </summary>
    public bool FlashSanSang => KmLichTuDong.FlashSanSang;

    partial void OnFlashBatChanged(bool value) => OnPropertyChanged(nameof(ChoNhapChuKyChung));

    // HAI Ô FLASH ĐỔI ⇒ KIỂM NGAY, hiện lỗi trong khung cảnh báo của form. Ô bind
    // `UpdateSourceTrigger=PropertyChanged` nên người dùng gõ `0` là thấy câu lỗi ngay, thay vì bấm Đóng rồi
    // lần sau mở lại mới phát hiện cấu hình KHÔNG được lưu (đường Đóng KHÔNG có chỗ nào hiện lỗi được:
    // `CauHinhHopLe` chỉ chặn lượt lưu, và lúc đó form đã đóng).
    partial void OnFlashPhanTramGiamChanged(int value) => NhacLoiFlash();

    partial void OnFlashSoLuongThamGiaChanged(int value) => NhacLoiFlash();

    /// <summary>
    /// Gán/xoá <see cref="Loi"/> theo hai ô flash.
    /// <para><b>CHỈ đụng <see cref="Loi"/> khi câu đang hiện là câu của CHÍNH hai ô này</b> (hoặc chưa có câu
    /// nào): xoá vô điều kiện là nuốt mất câu lỗi của tab khác — người dùng sửa xong ô % flash thì câu "Giờ
    /// không hợp lệ" của tab KM biến mất trong khi lỗi đó vẫn còn nguyên.</para>
    /// </summary>
    private void NhacLoiFlash()
    {
        var moi = KiemTraFormFlash(FlashPhanTramGiam, FlashSoLuongThamGia);
        if (moi is not null) { Loi = moi; return; }
        if (Loi is not null && Loi.StartsWith(DauCauLoiFlash, StringComparison.Ordinal)) { Loi = null; }
    }

    /// <summary>Tiền tố CHUNG của mọi câu lỗi do <see cref="KiemTraFormFlash"/> sinh ra — <see cref="NhacLoiFlash"/>
    /// dùng nó để biết câu đang hiện có phải của mình hay không. Một chuỗi, một chỗ.</summary>
    internal const string DauCauLoiFlash = "Flash Sale: ";

    partial void OnComboBatChanged(bool value)
    {
        OnPropertyChanged(nameof(ChoNhapCombo));
        OnPropertyChanged(nameof(ChoNhapChuKyChung));
    }

    /// <summary>Lỗi đầu vào hiện ngay trong form (null = không có). KHÔNG ném, KHÔNG đóng dialog.</summary>
    [ObservableProperty] private string? _loi;

    /// <summary>Dòng tổng kết sau khi bấm "Chạy chương trình" (null = chưa bấm).</summary>
    [ObservableProperty] private string? _tongKet;

    /// <summary>Ô "Sau xx ngày" chỉ có nghĩa khi đã tick Chạy tự động.</summary>
    public bool ChoNhapSoNgay => TuDong;

    /// <summary>
    /// <b>Ô chu kỳ "sau xx ngày" ở VÙNG CHUNG của form</b> — mở khi BẤT KỲ vòng nào đang bật.
    /// <para>KHÔNG dùng lại <see cref="ChoNhapSoNgay"/> (chỉ theo <see cref="TuDong"/> của Chương Trình Của
    /// Shop): từ bản chia tab, <c>SoNgay</c> nằm NGOÀI tab và là chu kỳ DÙNG CHUNG cho cả ba vòng, nên bật
    /// riêng vòng combo mà ô chu kỳ vẫn mờ là người dùng không còn đường nào chỉnh nhịp của nó — một ô mờ
    /// không nói vì sao nó mờ thì y hệt như nó không tồn tại.</para>
    /// </summary>
    /// <para><b>⚠ CỐ Ý KHÔNG có <c>FlashBat</c> ở đây</b> (28/08/2026): vòng Flash Sale đi chu kỳ CỨNG 1 ngày
    /// (<see cref="KmLichTuDong.SoNgayChuKy"/>) và KHÔNG đọc <c>SoNgay</c>. Mở ô chu kỳ khi chỉ bật mỗi flash
    /// là mời người dùng gõ một con số chẳng ai đọc — tệ hơn một ô mờ.</para>
    public bool ChoNhapChuKyChung => TuDong || ComboBat;

    // ══════════════════════════ MÁY CHỦ TRÌ (W4, người dùng chốt 22/08/2026) ══════════════════════════

    /// <summary>
    /// Người dùng đã bấm "Máy này nhận chủ trì" ⇒ đường lưu phải đóng dấu <c>OwnerMachine</c> +
    /// <c>CfgUpdatedAt</c> <b>dù cấu hình không đổi một chữ nào</b>.
    /// <para><b>Vì sao phải là một NÚT TƯỜNG MINH</b> (người dùng chốt 22/08/2026, sau phản biện vòng 3): hai
    /// hạng mục của vòng 2 đá nhau — cửa chủ trì chặn máy không chủ trì chạy lượt tự động, còn
    /// <see cref="BanLuuSauForm"/> lại không đóng dấu gì khi người dùng chỉ mở form ra rồi đóng. Máy chủ trì
    /// nghỉ hưu (cài lại Windows ⇒ đổi id máy) là <b>KM chết vĩnh viễn trên cả fleet</b>, và không có thao tác
    /// nào trong app gỡ được. Nút này là đường gỡ; và nó tường minh nên "mở form ra xem" vẫn không cướp chủ
    /// trì của máy khác (V5).</para>
    /// </summary>
    public bool DaBamNhanChuTri { get; private set; }

    /// <summary>Còn bấm được nút nhận chủ trì không: có biết mình là máy nào, và chưa bấm, và chủ trì hiện tại
    /// chưa phải máy này.</summary>
    public bool CoTheNhanChuTri
        => _mayNay.Length > 0 && !DaBamNhanChuTri
           && !string.Equals(BanNap.OwnerMachine?.Trim() ?? "", _mayNay, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Dòng chữ nói MÁY NÀO đang chủ trì — trước bản này form không hề lộ thông tin đó, nên người dùng đọc dòng
    /// nhật ký "máy chủ trì là X" xong mở form ra cũng không đối chiếu được với cái gì.
    /// <para><b>Hiện TÊN MÁY, không phải GUID thô</b> (phản biện vòng 3, X6): id suất là
    /// <c>&lt;guid 32 ký tự&gt;:orders</c>; in nguyên chuỗi đó thì một nhóm 3 máy vẫn phải mở form ở từng máy để
    /// đoán ra ai đang giữ — đúng cái W4 sinh ra để chấm dứt. Nhãn dựng bằng
    /// <see cref="KmLichTuDong.NhanMay"/>, không tra được tên thì rơi về id NGẮN.</para>
    /// </summary>
    public string MoTaChuTri
    {
        get
        {
            if (DaBamNhanChuTri)
            {
                return $"Máy chủ trì: {Nhan(_mayNay)} (máy này) — sẽ ghi khi đóng form.";
            }
            var chuTri = BanNap.OwnerMachine?.Trim() ?? "";
            if (chuTri.Length == 0)
            {
                return "Máy chủ trì: chưa có — mọi máy đều được chạy lượt KM tự động.";
            }
            return string.Equals(chuTri, _mayNay, StringComparison.OrdinalIgnoreCase)
                ? $"Máy chủ trì: {Nhan(chuTri)} (máy này)."
                : $"Máy chủ trì: {Nhan(chuTri)} — máy này KHÔNG chạy lượt KM tự động của tài khoản đó.";
        }
    }

    /// <summary>Nhãn người đọc được của một id máy (tên + id ngắn), tra tên qua hook nếu có.</summary>
    private string Nhan(string machineId)
        => KmLichTuDong.NhanMay(machineId, _traTenMay?.Invoke(machineId));

    /// <summary>Bấm "Máy này nhận chủ trì": chỉ đặt cờ + làm mới hai property hiển thị. Việc ghi xuống DB là
    /// của đường lưu (<see cref="BanLuuSauForm"/>) — form không đụng database, y như mọi thứ khác ở đây.</summary>
    [RelayCommand]
    private void NhanChuTri()
    {
        if (!CoTheNhanChuTri) { return; }
        DaBamNhanChuTri = true;
        OnPropertyChanged(nameof(DaBamNhanChuTri));
        OnPropertyChanged(nameof(CoTheNhanChuTri));
        OnPropertyChanged(nameof(MoTaChuTri));
    }

    partial void OnTuDongChanged(bool value)
    {
        OnPropertyChanged(nameof(ChoNhapSoNgay));
        OnPropertyChanged(nameof(ChoNhapChuKyChung));
    }

    partial void OnTenChuongTrinhChanged(string value)
    {
        if (!_dangTuSinhTen)
        {
            _nguoiDungTuSuaTen = true;
        }
    }

    partial void OnNgayChanged(DateTime value) => SinhLaiTenNeuChuaSuaTay();
    partial void OnGioBatDauChanged(string value) => SinhLaiTenNeuChuaSuaTay();

    /// <summary>
    /// Sinh lại tên theo ngày/giờ hiện tại — CHỈ khi người dùng chưa tự sửa tên.
    /// <para>Cờ <c>_dangTuSinhTen</c> bọc quanh lượt gán để chính lượt gán này không bị hiểu nhầm là "người
    /// dùng vừa sửa" (nếu không, lần sinh đầu tiên sẽ tự khoá mọi lần sinh sau).</para>
    /// </summary>
    private void SinhLaiTenNeuChuaSuaTay()
    {
        if (_nguoiDungTuSuaTen)
        {
            return;
        }

        _dangTuSinhTen = true;
        try { TenChuongTrinh = SinhTenMacDinh(Ngay, GioBatDau); }
        finally { _dangTuSinhTen = false; }
    }

    // ══════════════════════════ HÀM THUẦN ══════════════════════════

    /// <summary>
    /// PURE — tên mặc định: <c>KM ngày dd-MM-yyyy từ HH:mm</c>, cắt về tối đa 150 ký tự.
    /// <para><b>Mang ngày BẮT ĐẦU và MỘT giờ</b> (26/08/2026, cùng đợt đổi sang khoảng 180 ngày). Ngày bắt đầu
    /// là danh tính tự nhiên của một chương trình và là thứ người dùng đối chiếu khi mở Shopee ngay sau lượt
    /// chạy; vế <c>- HH:mm</c> cũ biến mất vì hai đầu khoảng nay dùng CHUNG một giờ.</para>
    /// </summary>
    public static string SinhTenMacDinh(DateTime ngayBatDau, string gio)
        => CatTen($"KM ngày {ngayBatDau.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)} từ {gio}");

    /// <summary>PURE — cắt về trần 150 ký tự (Shopee chặn cứng ở đó; gửi dài hơn là mất phần đuôi âm thầm).</summary>
    public static string CatTen(string? s)
    {
        var t = s ?? string.Empty;
        return t.Length <= TranDoDaiTen ? t : t[..TranDoDaiTen];
    }

    /// <summary>
    /// PURE — tên mặc định của chương trình COMBO: <c>Combo dd-MM-yyyy HH:mm</c> (<b>22 ký tự</b>), cắt về
    /// <see cref="TranDoDaiTenCombo"/>. Ngày và giờ đều là mốc <b>BẮT ĐẦU</b> của khoảng.
    /// <para><b>Có GIỜ trong tên (người dùng chốt 26/08/2026, sau lượt chạy thật).</b> Bản trước chỉ mang ngày
    /// (<c>Combo 26-08-2026</c>) nên hai lượt chạy trong CÙNG một ngày đẻ ra hai combo TRÙNG TÊN trên cùng một
    /// shop — chạy lại để kiểm chứng một bản vá là phải đi gỡ tay từng shop. Thêm giờ vào là mỗi lượt một tên,
    /// và tên tự nói chương trình chạy từ lúc nào.</para>
    /// <para><b>Khuôn RIÊNG, cố ý không dùng <see cref="SinhTenMacDinh"/>:</b> khuôn kia (<c>KM ngày … từ …</c>)
    /// dài 27 ký tự nên vẫn bị ô <c>maxlength="25"</c> của form combo cắt cụt, và phần bị cắt là đúng phần giờ
    /// — tức lại quay về đúng ca trùng tên ở trên. 22 ký tự là số ĐO, không phải ước lượng.</para>
    /// </summary>
    public static string SinhTenComboMacDinh(DateTime ngayBatDau, string? gio)
        => CatTenCombo(
            $"Combo {ngayBatDau.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)} {(gio ?? "").Trim()}".Trim());

    /// <summary>PURE — cắt về trần <see cref="TranDoDaiTenCombo"/> ký tự.</summary>
    public static string CatTenCombo(string? s)
    {
        var t = s ?? string.Empty;
        return t.Length <= TranDoDaiTenCombo ? t : t[..TranDoDaiTenCombo];
    }

    /// <summary>PURE — đọc giờ dạng <c>HH:mm</c>; sai định dạng ⇒ null.
    /// <para><c>TryParseExact</c> với <c>hh\:mm</c> đòi ĐÚNG 5 ký tự, hai chữ số mỗi vế: <c>"9:30"</c>,
    /// <c>"09:30:00"</c>, <c>"9h30"</c> đều trượt. Cố ý — đây là chuỗi sẽ được gửi thẳng xuống extension để so
    /// BẰNG với text đọc lại trên picker (<c>HH:mm dd-MM-yyyy</c>), lệch một ký tự là bước 4 đỏ.</para>
    /// <para><b>Thân hàm nằm ở <see cref="KmLichTuDong.DocGio"/></b> (26/08/2026): vòng chạy trong Core cũng
    /// phải đọc được giờ để biết mốc bắt đầu là lúc nào, mà hai bản chép tay là hai luật "giờ hợp lệ" khác
    /// nhau. Tên này giữ nguyên vì nhiều test và mọi cửa kiểm của form đang gọi nó.</para>
    /// </summary>
    public static TimeSpan? DocGio(string? s) => KmLichTuDong.DocGio(s);

    /// <summary>
    /// PURE — kiểm tra đầu vào của form. Trả <c>null</c> nếu hợp lệ, ngược lại là CÂU LỖI hiện thẳng trong form.
    /// <para>Trả câu lỗi chứ không ném: form phải ở lại cho người dùng sửa, không được đóng và không được nổ.</para>
    /// </summary>
    /// <param name="gio">Giờ dùng cho CẢ HAI đầu khoảng — vế "giờ kết thúc phải SAU giờ bắt đầu" đã BỎ
    /// (26/08/2026): hai đầu cùng một giờ thì luật ấy vô nghĩa, giữ lại là chặn đúng cấu hình hợp lệ.</param>
    public static string? KiemTraForm(string? ten, string? gio, int phanTram, bool tuDong, int soNgay)
    {
        if (string.IsNullOrWhiteSpace(ten))
        {
            return "Tên chương trình không được để trống.";
        }
        if (ten.Length > TranDoDaiTen)
        {
            return $"Tên chương trình tối đa {TranDoDaiTen} ký tự (đang {ten.Length}).";
        }

        if (DocGio(gio) is null)
        {
            return "Giờ không hợp lệ — định dạng HH:mm (ví dụ 09:30).";
        }

        if (phanTram < 1 || phanTram > 99)
        {
            return "% giảm phải là số nguyên từ 1 đến 99.";
        }
        if (tuDong && soNgay < 1)
        {
            return "Số ngày của chu kỳ tự động phải ≥ 1.";
        }
        return null;
    }

    /// <summary>
    /// PURE — kiểm tra đầu vào phần COMBO. Trả <c>null</c> nếu hợp lệ, ngược lại là CÂU LỖI hiện thẳng trong
    /// form. Cùng hợp đồng "trả câu lỗi chứ không ném" với <see cref="KiemTraForm"/>.
    /// <para>Tách hẳn khỏi <see cref="KiemTraForm"/> vì hai chương trình có hai bộ ô khác nhau và hai trần độ
    /// dài tên khác nhau — gộp lại là một tài khoản chỉ bật vòng KM vẫn bị chặn vì ô combo để rác.</para>
    /// </summary>
    /// <param name="loai">2 = giảm %, 3 = giảm số tiền, 1 = giá đặc biệt (đúng <c>value</c> radio của Shopee).</param>
    /// <param name="muaSl">Mua bao nhiêu sản phẩm — <b>≥ 2</b>, mua 1 sp không thành combo.</param>
    /// <param name="giam">Mức giảm: ĐỒNG nếu <paramref name="loai"/> = 3, PHẦN TRĂM nếu = 2.</param>
    /// <param name="gioiHan">Giới hạn đặt hàng (ô <c>usageLimit</c>).</param>
    /// <param name="gio">Giờ dùng cho CẢ HAI đầu khoảng — xem <see cref="KiemTraForm"/>.</param>
    public static string? KiemTraFormCombo(
        string? ten, string? gio, int loai, int muaSl, int giam, int gioiHan)
    {
        if (string.IsNullOrWhiteSpace(ten))
        {
            return "Tên combo không được để trống.";
        }
        if (ten.Length > TranDoDaiTenCombo)
        {
            return $"Tên combo tối đa {TranDoDaiTenCombo} ký tự (đang {ten.Length}).";
        }

        if (DocGio(gio) is null)
        {
            return "Giờ của combo không hợp lệ — định dạng HH:mm (ví dụ 11:00).";
        }

        if (loai is not (1 or 2 or 3))
        {
            return "Loại combo phải là 1 (giá đặc biệt), 2 (giảm %) hoặc 3 (giảm số tiền).";
        }
        if (muaSl < 2)
        {
            return "Combo phải mua tối thiểu 2 sản phẩm (mua 1 sản phẩm thì không còn là combo).";
        }
        // Vế % phải đứng TRƯỚC vế "≥ 1" chung? Không: `giam < 1` sai với cả hai loại, nói câu chung trước là
        // đúng thứ tự từ tổng quát tới riêng — người dùng gõ 0 nhận đúng một câu, không phải hai câu khác nhau
        // tuỳ loại.
        if (giam < 1)
        {
            return "Mức giảm của combo phải ≥ 1.";
        }
        if (loai == 2 && giam > 99)
        {
            return "Combo giảm theo % thì mức giảm phải là số nguyên từ 1 đến 99.";
        }
        if (gioiHan < 1)
        {
            return "Giới hạn đặt hàng của combo phải ≥ 1.";
        }
        return null;
    }

    /// <summary>
    /// PURE — kiểm tra hai ô thông số của tab FLASH SALE. Trả <c>null</c> nếu hợp lệ, ngược lại là CÂU LỖI hiện
    /// thẳng trong form. Cùng hợp đồng "trả câu lỗi chứ không ném" với <see cref="KiemTraForm"/>.
    ///
    /// <para><b>Tách hẳn khỏi hai hàm kiểm kia</b>, cùng lý do đã ghi ở <see cref="KiemTraFormCombo"/>: ba
    /// chương trình có ba bộ ô khác nhau, gộp lại là một tài khoản chỉ bật vòng KM cũng bị chặn vì ô của vòng
    /// khác.</para>
    ///
    /// <para><b>Biên: % ∈ [1, 99] và SL ∈ [1, 9999]</b> — hai con số này đi THẲNG xuống form Shopee qua
    /// <c>ThamSoFlashSale.Goi()</c>, và extension kiểm lại đúng hai biên ấy (<c>docThamSoFlash</c>, fail-closed).
    /// Trần 99 vì đó là dải ô %GIẢM của Shopee; trần 9999 vì ô "SL sản phẩm khuyến mãi" là số lượng kho, không
    /// phải phần trăm. Câu lỗi % còn NÓI RA mức tối thiểu Shopee đang đòi: bản trước là hằng 5 chính vì lý do
    /// đó, và người dùng gõ 1–4 sẽ nhận một lỗi từ Shopee ở bước cuối chứ không phải ở form.</para>
    /// </summary>
    /// <param name="phanTram">%GIẢM áp hàng loạt.</param>
    /// <param name="soLuong">"SL sản phẩm khuyến mãi" áp hàng loạt.</param>
    public static string? KiemTraFormFlash(int phanTram, int soLuong)
    {
        if (phanTram < 1 || phanTram > 99)
        {
            return DauCauLoiFlash + "% giảm phải là số nguyên từ 1 đến 99 "
                   + "(Shopee hiện đòi tối thiểu 5% cho Flash Sale của shop).";
        }
        if (soLuong < 1 || soLuong > 9999)
        {
            return DauCauLoiFlash + "Số lượng sản phẩm tham gia phải là số nguyên từ 1 đến 9999.";
        }
        return null;
    }

    /// <summary>
    /// PURE — mốc <c>Ngày + Giờ bắt đầu</c> phải ở TƯƠNG LAI <b>và cách bây giờ ít nhất một BIÊN AN</b>. Trả
    /// <c>null</c> nếu hợp lệ, ngược lại là câu lỗi.
    /// <para><b>Chỉ áp cho đường bấm "Chạy chương trình"</b>, KHÔNG áp cho đường "Đóng": người dùng mở form ra
    /// sửa % giảm mà trên ô ngày còn một mốc đã trôi qua (họ tự lùi ngày trên <c>DatePicker</c>) thì chặn ở đó
    /// là không cho họ lưu nổi một thay đổi hoàn toàn hợp lệ.</para>
    /// <para><b>⚠ TỪ 26/08/2026 ĐÂY LÀ CỬA HAY ĐỎ NHẤT, KHÔNG CÒN LÀ CỬA HIẾM.</b> Mốc bắt đầu nay là <i>HÔM
    /// NAY lúc <paramref name="gio"/></i> — tức nó TRÔI QUA TRONG NGÀY. Bấm tay lúc 13:28 với giờ 11:00 là mốc
    /// đã ở quá khứ ⇒ picker Shopee vô hiệu hoá ⇒ bước 4 chắc chắn đỏ. Vì thế câu lỗi phải DẠY ĐƯỢC người đọc:
    /// nói rõ mốc bắt đầu là hôm nay lúc mấy giờ, rằng nó đã trôi qua, và cách gỡ (đặt ô "Giờ" muộn hơn bây
    /// giờ). Một câu chỉ nói "đã ở QUÁ KHỨ" thì người dùng đứng trước một form không có ô nào tên "quá khứ".</para>
    /// <para>Hai lượt TỰ ĐỘNG vẫn an toàn tự nhiên: KM chạy 02–04h với giờ 09:30, combo chạy 06–08h với giờ
    /// 11:00 — cả hai đều còn ở tương lai trong ngày.</para>
    /// <para><b>⚠ BIÊN AN, KHÔNG PHẢI "lớn hơn bây giờ"</b> (phản biện 26/08/2026, lỗi NẶNG). Mốc bắt đầu là
    /// một HẠN CHÓT ĐỒNG HỒ mà cửa này chỉ chạy MỘT LẦN, trước vòng. Một vòng 12 shop mất ~1 tiếng (nghỉ 3–5
    /// phút mỗi shop), nên mốc "còn 2 phút nữa" qua được cửa rồi TRÔI QUA GIỮA VÒNG ⇒ mọi shop còn lại đỏ ở
    /// bước đặt thời gian. Bản trước còn TỰ TAY đẩy người dùng vào đúng ca đó: nó khuyên "đặt Giờ muộn hơn, ví
    /// dụ 14:00" lúc 13:28 — biên vỏn vẹn 32 phút. Nay mốc phải cách bây giờ ít nhất
    /// <see cref="KmLichTuDong.BienAnMocBatDau"/>, và GIỜ GỢI Ý cũng tính từ chính biên an đó.</para>
    /// <para><b>Vì sao chặn ở đường Chạy:</b> chặn ngay trên form thì người dùng biết lý do trong một giây, thay
    /// vì mở Brave, đi qua SSO, mở shop rồi mới nhận một dòng "ĐỌC LẠI KHÔNG KHỚP".</para>
    /// </summary>
    /// <param name="ngayBatDau">Ngày MỞ ĐẦU khoảng (ô "Ngày" trên form / <c>KmLichTuDong.NgayBatDau</c>).</param>
    /// <param name="gio">Giờ dùng cho cả hai đầu khoảng.</param>
    /// <param name="bayGio">Mốc hiện tại.</param>
    /// <param name="soShop">Số shop của lượt — quyết định BIÊN AN (<see cref="KmLichTuDong.BienAnMocBatDau"/>).
    /// <b>Bắt buộc truyền</b>, không có giá trị mặc định: một mặc định 0 ở đây là mọi chỗ gọi quên truyền đều
    /// lặng lẽ tụt về sàn 15 phút, tức đúng cái lỗ vừa phải vá.</param>
    public static string? KiemTraMocTuongLai(DateTime ngayBatDau, string? gio, DateTime bayGio, int soShop)
    {
        if (DocGio(gio) is not TimeSpan bd)
        {
            return null; // định dạng giờ đã có câu lỗi riêng ở KiemTraForm — không nói hai lần
        }
        var moc = ngayBatDau.Date.Add(bd);
        var bienAn = KmLichTuDong.BienAnMocBatDau(soShop);
        var somNhat = bayGio + bienAn;
        if (moc >= somNhat)
        {
            return null;
        }

        var soPhut = (int)bienAn.TotalMinutes;
        var moTaVong = soShop > 0 ? $"một vòng {soShop} shop" : "một vòng chạy";

        // Hai hình dạng hỏng, hai câu mở đầu khác nhau: mốc ĐÃ trôi qua thì Shopee chặn ngay từ bước 4 của shop
        // ĐẦU; mốc còn ở tương lai nhưng quá sát thì shop đầu qua được và nửa sau danh sách mới đỏ — người đọc
        // phải phân biệt được, vì hai ca ấy cho hai triệu chứng hoàn toàn khác nhau trên nhật ký.
        var veLyDo = moc <= bayGio
            ? $"Chương trình bắt đầu lúc {moc:dd-MM-yyyy HH:mm} — mốc này ĐÃ TRÔI QUA (bây giờ là "
              + $"{bayGio:dd-MM-yyyy HH:mm}), Shopee không cho tạo chương trình lùi giờ."
            : $"Chương trình bắt đầu lúc {moc:dd-MM-yyyy HH:mm} — mốc này QUÁ SÁT bây giờ "
              + $"({bayGio:dd-MM-yyyy HH:mm}): {moTaVong} cần khoảng {soPhut} phút, nên mốc sẽ TRÔI QUA GIỮA "
              + "VÒNG và mọi shop chạy sau đó đều đỏ ở bước đặt thời gian.";

        // Gợi ý một giờ CỤ THỂ, không nói chung chung: người đang bực vì bị chặn thì đọc số nhanh hơn đọc luật.
        // Giờ ấy phải cách bây giờ ÍT NHẤT biên an — làm tròn LÊN đầu giờ/nửa giờ nên chỉ có thể xa hơn.
        //
        // ⚠ KHÔNG bọc chữ "Giờ" trong ngoặc kép, dù nó là nhãn một ô trên form: câu này chảy vào dòng nhật ký
        // "CẤU HÌNH KHÔNG DÙNG ĐƯỢC", mà lưới rà `MoiCauHuongDan_ChiNhacToiNutCoThat` chỉ chấp nhận cụm trong
        // ngoặc kép là TÊN MỘT NÚT CÓ THẬT. Cùng lý do với `CauBatDauChayCombo`.
        var deXuat = LamTronLenBuocGoiY(somNhat);
        var veCachGo = deXuat.Date > ngayBatDau.Date
            // Gợi ý một giờ đã sang ngày hôm sau là gợi ý một mốc VẪN ở quá khứ — ô Giờ chỉ đặt được giờ TRONG
            // NGÀY bắt đầu. Nói thẳng là hôm nay hết chỗ.
            ? $"Cách gỡ: ngày {ngayBatDau:dd-MM-yyyy} KHÔNG còn đủ chỗ cho {moTaVong} ({soPhut} phút) — để lượt "
              + "này sang ngày kế và đặt ô Giờ trên form sớm hơn trong ngày."
            : $"Cách gỡ: đặt ô Giờ trên form MUỘN HƠN bây giờ, từ {deXuat:HH:mm} trở đi (cách bây giờ ít nhất "
              + $"{soPhut} phút — đủ cho {moTaVong} chạy xong trước mốc) rồi chạy lại.";

        return veLyDo + " " + veCachGo
               + $" Chương trình sẽ chạy từ {ngayBatDau:dd-MM-yyyy} tới "
               + $"{ngayBatDau.Date.AddDays(KmLichTuDong.SoNgayDatTruoc):dd-MM-yyyy}, cùng giờ đó ở hai đầu.";
    }

    /// <summary>
    /// <summary>
    /// <b>Tooltip của nút "Chạy Combo" — DỰNG Ở VM, không viết cứng trong XAML.</b>
    /// <para>Vế cuối phụ thuộc <see cref="KmLichTuDong.TranShopLuotCombo"/>: đang ở giai đoạn thử thì nói rõ
    /// lượt chạy dừng sau mấy shop, gỡ trần rồi thì câu đó phải BIẾN MẤT. Viết cứng trong XAML là tooltip nói
    /// dối ngay khi ai đó đổi hằng — ca thật 26/08/2026, và test
    /// <c>TooltipNutChayCombo_…</c> bắt được đúng lúc gỡ trần về 0.</para>
    /// </summary>
    public string TooltipChayCombo
        => "⚠ CHẠY THẬT NGAY: mở trình duyệt, đăng nhập SSO rồi TẠO Combo Khuyến Mãi THẬT trên MỌI shop của "
           + "tài khoản này (nghỉ 3-5 phút giữa hai shop). Check đơn tạm dừng và tự chạy lại khi xong. ĐỘC LẬP "
           + "với ô tick chạy tự động ở trên: không tick vẫn bấm chạy được, tick rồi cũng không phải chờ tới 6h "
           + "sáng."
           + (KmLichTuDong.TranShopLuotCombo > 0
               ? $" Đang ở CHẾ ĐỘ THỬ nên lượt chạy dừng sau {KmLichTuDong.TranShopLuotCombo} shop đầu."
               : "");

    /// <summary>Bước làm tròn của GIỜ GỢI Ý, tính bằng phút — xem <see cref="LamTronLenBuocGoiY"/>.</summary>
    internal const int BuocGoiYPhut = 5;

    /// PURE — làm tròn LÊN bội số <see cref="BuocGoiYPhut"/> phút gần nhất (đã đúng mốc thì giữ nguyên).
    /// <para>Dùng cho GIỜ GỢI Ý của <see cref="KiemTraMocTuongLai"/>: ô Giờ trên form là <c>HH:mm</c>, gợi ý
    /// <c>14:14</c> thì người dùng gõ lại thành số tròn rồi vô tình tụt xuống dưới biên an. Làm tròn LÊN nên
    /// khoảng cách chỉ có thể xa hơn biên an, không bao giờ gần hơn — đó là bất biến của cả phép kiểm.</para>
    /// <para><b>Bước 5 phút, KHÔNG phải nửa giờ</b> (26/08/2026). Bản đầu làm tròn lên <c>:00</c>/<c>:30</c>, và
    /// với lượt thử 2 shop (biên an 16 phút) nó đẩy gợi ý từ <c>16:33</c> lên <c>17:00</c> — thêm 27 phút chờ
    /// không vì lý do gì. Bước nhỏ vẫn giữ nguyên bất biến "không bao giờ gần hơn biên an", mà con số gợi ý thì
    /// dùng được ngay.</para>
    /// </summary>
    internal static DateTime LamTronLenBuocGoiY(DateTime t)
    {
        var buoc = TimeSpan.FromMinutes(BuocGoiYPhut).Ticks;
        var du = t.Ticks % buoc;
        return du == 0 ? t : new DateTime(t.Ticks - du + buoc, t.Kind);
    }

    /// <summary>
    /// <b>PURE — mốc chạy kế dự kiến</b>: <c>ngày CHẠY + soNgay</c>, kéo về đầu khung KM (02:00).
    /// <para><b>Tính từ mốc CHẠY, KHÔNG từ ngày chương trình</b> (25/08/2026, cùng đợt đẩy ngày đi 180 ngày).
    /// Bản trước lấy <c>ngày chương trình + giờ bắt đầu + soNgay</c>; hai thứ đó trùng nhau chừng nào chương
    /// trình còn đặt cho CHÍNH hôm nay. Từ khi ngày chương trình đi trước 180 ngày, giữ công thức cũ là câu
    /// tổng kết hứa "lần kế dự kiến" lệch đúng 180 ngày so với lịch thật.</para>
    /// <para>Công thức bám <see cref="KmLichTuDong.MocChayKe"/> — chính nó mới là thứ quyết định lượt tự động
    /// chạy lúc nào: <c>LanChayCuoi.Date + max(1, SoNgay)</c> kéo về <see cref="KmLichTuDong.GioMoKhung"/>.
    /// Câu tổng kết nói một đằng mà lịch chạy một nẻo thì thà đừng nói.</para>
    /// <para><b>Đây là DỰ BÁO in TRƯỚC khi chạy, không phải cam kết</b> (phản biện 25/08/2026). Hai ca lệch, cả
    /// hai đều chỉ biết được SAU khi lượt kết thúc nên không tính trước được: (a) lượt tạo được 0 shop ⇒
    /// <c>ChotLuotKm</c> không ghi <c>LanChayCuoi</c> ⇒ mốc thật là khung 02:00 GẦN NHẤT chứ không phải
    /// +<c>soNgay</c>; (b) lượt vắt qua nửa đêm ⇒ <c>LanChayCuoi</c> ghi lúc KẾT THÚC nên lệch một ngày.</para>
    /// </summary>
    /// <param name="bayGio">Mốc lượt này CHẠY (production truyền <c>DateTime.Now</c>) — sẽ thành
    /// <c>LanChayCuoi</c> khi lượt tạo được ít nhất một shop.</param>
    /// <param name="soNgay">Chu kỳ tự động, tính theo NGÀY LỊCH.</param>
    /// <param name="loai">Vòng nào — quyết định GIỜ MỞ KHUNG kéo về (Shop 02:00 · Combo 06:00). Tham số hoá vì
    /// cùng lý do với <c>AccountsViewModel.CauTongKetKm</c>: đọc thẳng hằng của vòng KM là câu tổng kết lượt
    /// combo hứa "lần kế dự kiến 02:00" cho một vòng chỉ chạy từ 06:00.</param>
    public static DateTime LanChayKe(
        DateTime bayGio, int soNgay, LoaiChuongTrinh loai = LoaiChuongTrinh.ChuongTrinhShop)
        => bayGio.Date.AddDays(Math.Max(1, soNgay)).AddHours(KmLichTuDong.Khung(loai).Mo);

    /// <summary>
    /// PURE — dòng tổng kết ghi vào nhật ký NGAY KHI bấm "Chạy chương trình".
    /// <para><b>Nói rõ lượt chạy SẮP làm gì và trên bao nhiêu shop.</b> Từ đây trở đi trình duyệt sẽ mở và
    /// chương trình khuyến mãi được tạo THẬT trên từng shop — dòng này là bản ghi duy nhất của thông số đã
    /// dùng, nên phải đủ để đối chiếu lại về sau (tên · ngày giờ · % · phạm vi · chu kỳ tự động).</para>
    /// </summary>
    /// <param name="thamSo">Gói thông số của lượt sắp chạy — <b>cùng gói mà nơi gọi gửi xuống extension</b>.
    /// Ghép lại từ biến rời ở đây là dựng một chuỗi song song tự nhất quán (lỗ phủ 25/08/2026).</param>
    /// <param name="bayGio">Mốc lượt này CHẠY — gốc tính "lần kế dự kiến" (<see cref="LanChayKe"/>). KHÔNG
    /// dùng ngày của chương trình cho việc đó: khoảng chương trình dài 180 ngày.</param>
    public static string CauBatDauChay(
        ThamSoKm thamSo, int soShop, string login, bool tuDong, int soNgay, DateTime bayGio)
    {
        var tuDongText = tuDong
            ? $"mỗi {soNgay} ngày, lần kế dự kiến "
              + LanChayKe(bayGio, soNgay).ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture)
            : "tắt";

        return "KM của Shop — BẮT ĐẦU CHẠY: " + thamSo.MoTa()
               + $" · phạm vi: {soShop} shop của {login} · tự động: {tuDongText}";
    }

    /// <summary>
    /// PURE — dòng tổng kết ghi vào nhật ký NGAY KHI bấm nút <b>Chạy Combo</b>. Bản đối xứng của
    /// <see cref="CauBatDauChay"/> cho vòng COMBO.
    /// <para><b>Đọc TỪ CHÍNH gói <paramref name="thamSo"/> sắp gửi xuống extension</b>, không ghép lại từ biến
    /// rời — đúng bài học lỗ phủ 25/08/2026 (đổi ngày gửi xuống Shopee mà 216 test vẫn xanh vì dòng log đọc một
    /// biến song song). Mức ưu đãi lấy qua <c>ThamSoCombo.MoTaUuDai</c> để cách viết số tiền (2.000đ) chỉ có
    /// đúng một bản.</para>
    /// <para><b>Cố ý KHÔNG bọc tên chương trình trong ngoặc kép</b> (khác <see cref="CauBatDauChay"/>): lưới rà
    /// câu chữ <c>MoiCauHuongDan_ChiNhacToiNutCoThat</c> chỉ chấp nhận cụm trong ngoặc kép là TÊN NÚT CÓ THẬT
    /// hoặc một ngoại lệ khai tay — mà tên combo đổi theo NGÀY nên không khai tay được.</para>
    /// <para><b>Trần chế độ thử được nói ra ngay ở dòng này</b> (<see cref="KmLichTuDong.TranShopLuotCombo"/>):
    /// người bấm nút phải biết TRƯỚC rằng lượt sắp chạy sẽ dừng sau 2 shop, chứ không phải đọc ra sau khi nó đã
    /// dừng.</para>
    /// </summary>
    /// <param name="thamSo">Gói thông số của lượt sắp chạy — cùng gói mà nơi gọi gửi xuống extension.</param>
    /// <param name="soShop">Số shop của tài khoản (phạm vi lượt chạy).</param>
    /// <param name="comboBat">Vòng combo TỰ ĐỘNG có đang bật không — nút bấm tay chạy được dù nó tắt, nên câu
    /// này phải nói rõ trạng thái của ô tick thay vì để người đọc suy ra.</param>
    /// <param name="bayGio">Mốc lượt này CHẠY — gốc tính "lần kế dự kiến".</param>
    public static string CauBatDauChayCombo(
        ThamSoCombo thamSo, int soShop, string login, bool comboBat, int soNgay, DateTime bayGio)
    {
        var tuDongText = comboBat
            ? $"mỗi {soNgay} ngày, lần kế dự kiến "
              + LanChayKe(bayGio, soNgay, LoaiChuongTrinh.Combo)
                  .ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture)
            : "tắt (lượt này là bấm tay)";

        var tran = KmLichTuDong.TranShopLuotCombo;
        var phamVi = tran > 0
            ? $"phạm vi: {soShop} shop của {login} nhưng CHẾ ĐỘ THỬ chỉ chạy tối đa {tran} shop rồi dừng"
            : $"phạm vi: {soShop} shop của {login}";

        return "Combo Khuyến Mãi — BẮT ĐẦU CHẠY: "
               + $"{thamSo.Ten} · {thamSo.NgayBatDau.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)} "
               + $"{thamSo.Gio} → "
               + $"{thamSo.NgayKetThuc.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)} {thamSo.Gio}"
               + $" · {thamSo.MoTaUuDai()} · giới hạn đặt hàng {thamSo.GioiHan}"
               + $" · {phamVi} · tự động: {tuDongText}";
    }

    // ══════════════════════════ Kết quả cho nơi gọi ══════════════════════════

    /// <summary>
    /// Cấu hình hiện trên form, đóng gói để lưu.
    /// <para><b>⚠ Hai mốc chạy trong bản này là ẢNH CHỤP LÚC MỞ FORM</b> — đừng ghi thẳng xuống DB. Đường lưu
    /// phải đi qua <see cref="BanLuuSauForm"/>, nơi mốc được lấy từ dòng ĐỌC LẠI lúc lưu.</para>
    /// </summary>
    public KmConfig LayCauHinh()
        => new(_accountId, TuDong, SoNgay, PhanTram, GioBatDau, GioKetThuc, _lanChayCuoi, _lanThuTuDong)
        {
            ComboBat = ComboBat,
            ComboGioBatDau = ComboGioBatDau,
            ComboGioKetThuc = ComboGioKetThuc,
            ComboLoai = ComboLoai,
            ComboMuaSl = ComboMuaSl,
            ComboGiam = ComboGiam,
            ComboGioiHan = ComboGioiHan,
            ComboLanChayCuoi = _comboLanChayCuoi,
            ComboLanThuTuDong = _comboLanThuTuDong,
            FlashBat = FlashBat,
            FlashLanChayCuoi = _flashLanChayCuoi,
            FlashLanThuTuDong = _flashLanThuTuDong,
            FlashPhanTramGiam = FlashPhanTramGiam,
            FlashSoLuongThamGia = FlashSoLuongThamGia,
        };

    /// <summary>
    /// <b>PURE — dòng <c>km_config</c> cần LƯU sau khi đóng form, hoặc <c>null</c> = KHÔNG lưu gì cả.</b>
    /// <para><b>(V5) Mở form ra XEM rồi bấm Đóng thì KHÔNG được cướp chủ trì.</b> Bản trước đóng dấu
    /// <c>CfgUpdatedAt = now</c> + <c>OwnerMachine = máy này</c> vô điều kiện. Người dùng chốt "đổi chủ trì =
    /// mở form ở máy mới", nhưng *mở ra nhìn* và *mở ra đổi* là hai việc khác nhau — và lượt đóng dấu vô cớ ấy
    /// là bước then chốt của ca mất cấu hình vòng 1: máy B xem một hồ sơ RỖNG rồi vô tình đóng dấu cho nó, thế
    /// là bản rỗng thắng cấu hình thật của máy A trên toàn fleet.</para>
    /// <para><b>(W5) "Có sửa gì không" so <paramref name="tuForm"/> với <paramref name="banNap"/>, KHÔNG so với
    /// <paramref name="tuoi"/>.</b> Bản vòng 2 so với dòng đọc lại lúc lưu — câu hỏi khác hẳn, và lệch nhau
    /// đúng bằng cửa sổ 60 giây của worker đồng bộ. Xem xmldoc <see cref="BanNap"/> cho ca hỏng đầy đủ.</para>
    /// <para><b>(W4) Đường DUY NHẤT lưu mà không sửa gì là <paramref name="nhanChuTri"/></b> — nút tường minh
    /// trên form. Không có nó thì một máy chủ trì đã nghỉ hưu khoá chết KM trên cả fleet.</para>
    /// <para><b>(V6) Hai mốc chạy lấy từ <paramref name="tuoi"/>, KHÔNG lấy từ form.</b> Form là modal mở hàng
    /// phút; worker đồng bộ (nhịp 60s) hoặc một lượt <c>ChotLuotKm</c> có thể đã đẩy mốc lên trong lúc đó. Ghi
    /// đè bằng ảnh chụp lúc mở là LÙI mốc — hub gộp theo MAX nên kéo lại được, nhưng chỉ khi hub còn sống.</para>
    /// </summary>
    /// <param name="tuoi">Dòng <c>km_config</c> ĐỌC LẠI ngay trước lúc lưu — CHỈ dùng để lấy hai mốc chạy (V6).</param>
    /// <param name="banNap">Bản nạp LÚC MỞ FORM — <see cref="BanNap"/>. Đây mới là vế so của câu hỏi "người
    /// dùng có sửa gì không".</param>
    /// <param name="tuForm">Cấu hình đang hiện trên form — <see cref="LayCauHinh"/>.</param>
    /// <param name="mayNay">Id suất đơn hàng của máy này (rỗng = không có hub ⇒ chủ trì để rỗng).</param>
    /// <param name="luc">Mốc đóng dấu (giờ địa phương; production truyền <c>DateTime.Now</c>).</param>
    /// <param name="nhanChuTri">Người dùng đã bấm "Máy này nhận chủ trì" (<see cref="DaBamNhanChuTri"/>) ⇒ lưu
    /// và đóng dấu DÙ cấu hình y nguyên. Đây là đường DUY NHẤT gỡ được ca "máy chủ trì đã nghỉ hưu".</param>
    public static KmConfig? BanLuuSauForm(
        KmConfig tuoi, KmConfig banNap, KmConfig tuForm, string mayNay, DateTime luc, bool nhanChuTri = false)
    {
        // "Người dùng có sửa gì không" = so form với BẢN NẠP LÚC MỞ, không phải với dòng đọc lại lúc lưu — xem
        // xmldoc của BanNap cho ca hỏng đầy đủ. Cả hai vế đều mang OwnerMachine của `banNap` nên trường đó tự
        // triệt tiêu trong phép so.
        var nhuNguoiDungThay = banNap with
        {
            TuDong = tuForm.TuDong,
            SoNgay = tuForm.SoNgay,
            PhanTram = tuForm.PhanTram,
            GioBatDau = tuForm.GioBatDau,
            GioKetThuc = tuForm.GioKetThuc,
            ComboBat = tuForm.ComboBat,
            ComboGioBatDau = tuForm.ComboGioBatDau,
            ComboGioKetThuc = tuForm.ComboGioKetThuc,
            ComboLoai = tuForm.ComboLoai,
            ComboMuaSl = tuForm.ComboMuaSl,
            ComboGiam = tuForm.ComboGiam,
            ComboGioiHan = tuForm.ComboGioiHan,
            // Công tắc FLASH cũng là thứ người dùng bấm được trên form ⇒ phải vào phép so, kẻo bật/tắt nó rồi
            // bấm Đóng là "không sửa gì" ⇒ KHÔNG lưu ⇒ cú bấm biến mất không dấu vết. Hai THÔNG SỐ flash
            // (03/09/2026) cùng lý do — chúng là hai ô người dùng gõ.
            FlashBat = tuForm.FlashBat,
            FlashPhanTramGiam = tuForm.FlashPhanTramGiam,
            FlashSoLuongThamGia = tuForm.FlashSoLuongThamGia,
        };
        if (KmConfig.CungCauHinh(banNap, nhuNguoiDungThay) && !nhanChuTri)
        {
            return null;   // không sửa gì, không nhận chủ trì ⇒ không lưu, không đóng dấu, không rung chuông đẩy hub
        }

        // Hai mốc CHẠY lấy từ dòng TƯƠI (V6): form là modal mở hàng phút, worker hoặc `ChotLuotKm` có thể đã
        // đẩy mốc lên trong lúc đó — ghi đè bằng ảnh chụp lúc mở là LÙI mốc.
        return tuoi with
        {
            TuDong = tuForm.TuDong,
            SoNgay = tuForm.SoNgay,
            PhanTram = tuForm.PhanTram,
            GioBatDau = tuForm.GioBatDau,
            GioKetThuc = tuForm.GioKetThuc,
            // 7 trường CẤU HÌNH combo đi cùng nhóm. Hai MỐC chạy combo cố ý lấy từ `tuoi` (không liệt kê ở
            // đây) — đúng luật (V6) của hai mốc bên vòng kia: form là ảnh chụp lúc mở, ghi đè là LÙI mốc.
            ComboBat = tuForm.ComboBat,
            ComboGioBatDau = tuForm.ComboGioBatDau,
            ComboGioKetThuc = tuForm.ComboGioKetThuc,
            ComboLoai = tuForm.ComboLoai,
            ComboMuaSl = tuForm.ComboMuaSl,
            ComboGiam = tuForm.ComboGiam,
            ComboGioiHan = tuForm.ComboGioiHan,
            // Công tắc + hai THÔNG SỐ flash đi cùng nhóm cấu hình. Hai MỐC chạy flash cố ý lấy từ `tuoi` (không
            // liệt kê ở đây) — đúng luật (V6) của bốn mốc kia.
            FlashBat = tuForm.FlashBat,
            FlashPhanTramGiam = tuForm.FlashPhanTramGiam,
            FlashSoLuongThamGia = tuForm.FlashSoLuongThamGia,
            CfgUpdatedAt = luc,
            // (Z1d, vòng 6) `mayNay` RỖNG = "máy này KHÔNG BIẾT MÌNH LÀ AI" (chưa cấu hình hub) — khác hẳn
            // "KHÔNG AI chủ trì". Ghi rỗng đè lên là xoá chủ trì của cả fleet chỉ vì một máy chưa nối hub mở
            // form ra sửa %; mà `LaMayChuTri("", …)` trả true cho MỌI máy, nên hậu quả là đêm sau mọi máy cùng
            // chạy lượt KM của tài khoản đó. Không biết thì GIỮ NGUYÊN thứ đang có.
            OwnerMachine = mayNay is { Length: > 0 } m ? m : (tuoi.OwnerMachine ?? ""),
        };
    }

    /// <summary>
    /// PURE — dòng nhật ký CẢNH BÁO "máy khác vừa đổi cấu hình trong lúc form đang mở", hoặc <c>null</c> =
    /// không có gì phải nói.
    /// <para>Nhịp kéo 60 giây của worker chạy trên thread Timer, <b>modal KHÔNG chặn nó</b> — nên đây là ca
    /// THẬT chứ không phải ca lý thuyết: bản người dùng đang nhìn (<paramref name="banNap"/>) có thể đã bị bản
    /// của máy khác thay mất ngay dưới chân họ. Im lặng ở đây thì hoặc là thay đổi của máy kia biến mất không
    /// dấu vết (người dùng có sửa ⇒ lưu đè), hoặc là người dùng đóng form với niềm tin sai về cấu hình đang
    /// chạy.</para>
    /// </summary>
    /// <param name="banNap">Bản nạp lúc mở form — <see cref="BanNap"/>.</param>
    /// <param name="tuoi">Dòng <c>km_config</c> đọc lại lúc lưu.</param>
    /// <param name="daLuu">Lượt đóng form này CÓ ghi xuống DB không (<see cref="BanLuuSauForm"/> trả khác
    /// <c>null</c>) — hai kết cục khác nhau nên phải nói khác nhau.</param>
    public static string? CauCanhBaoSauForm(KmConfig banNap, KmConfig tuoi, bool daLuu)
    {
        if (KmConfig.CungCauHinh(banNap, tuoi))
        {
            return null;
        }
        // ⚠ CHỈ in `GioBatDau`. `GioKetThuc` là TRƯỜNG CHẾT từ 26/08/2026 (cột DB giữ lại, nhưng không còn
        // đường nào đi xuống Shopee) — in nó ra như thể đó là khung giờ chương trình là nói sai với đúng người
        // đang cần biết cấu hình thật đang chạy là gì.
        return "KM của Shop: cấu hình tài khoản này vừa ĐỔI Ở MÁY KHÁC trong lúc form đang mở (nay là "
               + (tuoi.TuDong ? $"tự động BẬT mỗi {tuoi.SoNgay} ngày" : "tự động TẮT")
               + $", giảm {tuoi.PhanTram}%, {tuoi.GioBatDau} (khoảng {KmLichTuDong.SoNgayDatTruoc} ngày, "
               + "cùng giờ hai đầu)). "
               + (daLuu
                   ? "Bản vừa lưu ở máy này ĐÈ LÊN bản đó — mở lại form để xem kết quả cuối cùng."
                   : "Form đang hiện bản CŨ nên KHÔNG lưu gì; mở lại form để thấy bản mới.");
    }

    /// <summary>
    /// Cấu hình trên form có ĐÁNG LƯU không (đường bấm "Đóng").
    /// <para>KHÔNG gồm phép kiểm "mốc ở tương lai" — xem <see cref="KiemTraMocTuongLai"/>: mốc quá khứ chỉ chặn
    /// đường CHẠY, còn % giảm / chu kỳ / khung giờ thì vẫn phải lưu được.</para>
    /// </summary>
    public bool CauHinhHopLe
        => KiemTraForm(TenChuongTrinh, GioBatDau, PhanTram, TuDong, SoNgay) is null
           // Phần combo CHỈ chặn khi vòng combo đang BẬT: một tài khoản chỉ dùng vòng KM không việc gì phải
           // sửa mấy ô combo nó chưa từng mở ra mới lưu nổi % giảm.
           && (!ComboBat
               || KiemTraFormCombo(
                   SinhTenComboMacDinh(Ngay, ComboGioBatDau), ComboGioBatDau,
                   ComboLoai, ComboMuaSl, ComboGiam, ComboGioiHan) is null)
           // ⚠ HAI Ô FLASH CHẶN VÔ ĐIỀU KIỆN, KHÔNG gác sau `FlashBat` như cụm combo ở trên — và đó là chủ ý.
           // Gác sau công tắc thì người dùng gõ 150% trong lúc vòng flash đang TẮT sẽ được LƯU (cửa này mở),
           // rồi tháng sau họ bật vòng lên và gói đầu gửi 150% xuống Shopee. Fail-open ở đúng chỗ tệ nhất.
           // Hai ô này luôn hiện trên form và luôn nạp sẵn 5/10 từ DB, nên cách duy nhất để chúng thành rác là
           // người dùng TỰ gõ — và lúc ấy không lưu gì mới là hành vi đúng (`NhacLoiFlash` đã nói ra lý do
           // ngay trong form, tại thời điểm gõ).
           && KiemTraFormFlash(FlashPhanTramGiam, FlashSoLuongThamGia) is null;

    /// <summary>
    /// <b>Gói <see cref="ThamSoKm"/> của lượt BẤM TAY, dựng từ chính form.</b> Một bộ dựng DUY NHẤT, dùng cho cả
    /// câu tổng kết (<see cref="ThuChay"/>) lẫn lượt chạy thật (<c>AccountsViewModel.MoFormKmVaChayAsync</c>).
    /// <para><b>Đừng viết bộ dựng thứ hai ở nơi gọi</b> — đúng bài học §5c VỪA-2 của
    /// <c>plans/2026-08-25-combo-khuyen-mai.md</c>: hai đường tự dựng lấy là hai cơ hội trôi lệch, mà test chỉ
    /// đo một đường. Đây là bản đối xứng của <c>AccountsViewModel.DungThamSoCombo</c> cho vòng KM.</para>
    /// <para><b>Ô "Ngày" trên form là ngày MỞ ĐẦU khoảng</b>; ngày đóng suy ra bằng
    /// <see cref="KmLichTuDong.NgayKetThuc"/> nên độ dài khoảng luôn đúng
    /// <see cref="KmLichTuDong.SoNgayDatTruoc"/> ngày dù người dùng chọn ngày nào.</para>
    /// </summary>
    public ThamSoKm DungThamSoKm()
        => new(CatTen(TenChuongTrinh),
               KmLichTuDong.NgayBatDau(Ngay), KmLichTuDong.NgayKetThuc(Ngay),
               GioBatDau, PhanTram);

    /// <summary>
    /// Bấm "Chạy chương trình": kiểm tra → dựng câu tổng kết. Trả <c>null</c> khi dữ liệu SAI (đã gán
    /// <see cref="Loi"/>) — nơi gọi KHÔNG được lưu và KHÔNG được ghi nhật ký.
    /// </summary>
    /// <param name="bayGio">Mốc "bây giờ" để kiểm ngày/giờ bắt đầu ở tương lai; bỏ trống = đồng hồ hệ thống
    /// (tham số hoá để test được mọi ca biên mà không phải đợi tới đúng giờ).</param>
    public string? ThuChay(DateTime? bayGio = null)
    {
        var luc = bayGio ?? DateTime.Now;

        // MỘT GÓI, MỘT NGUỒN: cùng object đi vào luật kiểm, câu tổng kết và (ở nơi gọi) lượt chạy thật.
        var thamSo = DungThamSoKm();

        // ⚠ TÊN kiểm bản THÔ trên form, KHÔNG kiểm `thamSo.Ten`: gói đã đi qua `CatTen` nên vế "tối đa 150 ký
        // tự" sẽ không bao giờ đỏ nữa — người dùng dán một chuỗi dài rồi mất phần đuôi trong im lặng.
        Loi = KiemTraForm(TenChuongTrinh, thamSo.Gio, thamSo.PhanTram, TuDong, SoNgay)
              ?? KiemTraMocTuongLai(thamSo.NgayBatDau, thamSo.Gio, luc, _soShop);
        if (Loi is not null)
        {
            // ⚠ XOÁ HẲN vòng yêu cầu, không để sót giá trị của lượt bấm TRƯỚC: người dùng bấm Chạy Combo hợp lệ
            // rồi sửa tay giờ cho sai và bấm Chạy chương trình ⇒ để sót là nơi gọi chạy nhầm vòng combo.
            VongYeuCauChay = null;
            ThamSoComboDaKiem = null;
            TongKet = null;
            return null;
        }

        // Lượt này là vòng KM ⇒ gói combo của lượt bấm TRƯỚC phải biến mất cùng `VongYeuCauChay`: hai thứ ấy
        // luôn đi đôi, để sót một cái là nơi gọi cầm gói của lượt cũ.
        ThamSoComboDaKiem = null;
        VongYeuCauChay = LoaiChuongTrinh.ChuongTrinhShop;
        TongKet = CauBatDauChay(thamSo, _soShop, _login, TuDong, SoNgay, luc);
        return TongKet;
    }

    /// <summary>
    /// <b>VÒNG nào vừa được yêu cầu chạy</b> — <c>null</c> = chỉ đóng form (hoặc lượt bấm gần nhất có dữ liệu
    /// SAI); <see cref="LoaiChuongTrinh.ChuongTrinhShop"/> = bấm nút Chạy chương trình;
    /// <see cref="LoaiChuongTrinh.Combo"/> = bấm nút Chạy Combo.
    /// <para><b>Vì sao là property trên VM chứ không phải giá trị trả của <c>DialogService.ShowKm</c></b>: chữ
    /// ký <c>ShowKm(vm) → bool</c> đang được nhiều test bám vào; đổi nó kéo theo cả một vòng sửa test cho một
    /// thông tin mà VM vốn đã biết. Nơi gọi vẫn đọc <c>bool</c> để biết "có chạy không", rồi đọc property này để
    /// biết "chạy vòng nào".</para>
    /// <para><b>Luôn ghi lại ở MỌI lối ra của cả hai hàm <c>ThuChay*</c></b> — kể cả lối ra lỗi. Một lượt bấm
    /// sai mà giữ nguyên giá trị của lượt bấm trước là nơi gọi chạy nhầm vòng, và đó là vòng MỞ TRÌNH DUYỆT và
    /// TẠO CHƯƠNG TRÌNH THẬT trên mọi shop.</para>
    /// </summary>
    public LoaiChuongTrinh? VongYeuCauChay { get; private set; }

    /// <summary>
    /// Bấm "Chạy Combo": kiểm tra → dựng câu tổng kết riêng của vòng combo. Trả <c>null</c> khi dữ liệu SAI (đã
    /// gán <see cref="Loi"/>) — nơi gọi KHÔNG được chạy và KHÔNG được ghi nhật ký. Cùng hợp đồng với
    /// <see cref="ThuChay"/>.
    /// <para><b>Gói thông số dựng bằng ĐÚNG builder của đường tự động</b>
    /// (<c>AccountsViewModel.DungThamSoCombo</c>): hai mốc ngày lấy qua <see cref="KmLichTuDong.NgayBatDau"/> /
    /// <see cref="KmLichTuDong.NgayKetThuc"/>, tên
    /// qua <see cref="SinhTenComboMacDinh"/>. Viết bộ dựng thứ hai ở đây là hai đường bấm-tay/tự-động trôi lệch
    /// nhau, mà test chỉ đo một đường — đúng bài học §5c VỪA-2 của
    /// <c>plans/2026-08-25-combo-khuyen-mai.md</c>.</para>
    /// <para><b>Hai mốc ngày của combo KHÔNG lấy từ ô <see cref="Ngay"/> trên form</b>: ô đó thuộc tab Chương
    /// Trình Của Shop. Vòng combo luôn chạy từ HÔM NAY tới hôm nay + <see cref="KmLichTuDong.SoNgayDatTruoc"/>,
    /// y lượt tự động.</para>
    /// </summary>
    /// <param name="bayGio">Mốc "bây giờ"; bỏ trống = đồng hồ hệ thống (tham số hoá để test mọi ca biên).</param>
    public string? ThuChayCombo(DateTime? bayGio = null)
    {
        var luc = bayGio ?? DateTime.Now;
        var thamSo = AccountsViewModel.DungThamSoCombo(LayCauHinh(), luc);

        Loi = KiemTraFormCombo(
                  thamSo.Ten, thamSo.Gio,
                  thamSo.Loai, thamSo.MuaSl, thamSo.Giam, thamSo.GioiHan)
              ?? KiemTraMocTuongLai(thamSo.NgayBatDau, thamSo.Gio, luc,
                     KmLichTuDong.SoShopSeChay(_soShop, LoaiChuongTrinh.Combo));
        if (Loi is not null)
        {
            VongYeuCauChay = null;
            ThamSoComboDaKiem = null;
            TongKet = null;
            return null;
        }

        VongYeuCauChay = LoaiChuongTrinh.Combo;
        ThamSoComboDaKiem = thamSo;
        TongKet = CauBatDauChayCombo(thamSo, _soShop, _login, ComboBat, SoNgay, luc);
        return TongKet;
    }

    /// <summary>
    /// <b>ĐÚNG gói <see cref="ThamSoCombo"/> vừa được KIỂM và vừa đi vào câu tổng kết</b> — <c>null</c> khi
    /// lượt bấm gần nhất không phải một lượt Chạy Combo hợp lệ. Nơi gọi PHẢI gửi chính gói này xuống extension.
    /// <para><b>Vì sao là property chứ không để nơi gọi tự dựng lại</b> (phản biện 26/08/2026, VỪA-3):
    /// <c>AccountsViewModel.MoFormKmVaChayAsync</c> từng gọi <c>DungThamSoCombo(vm.LayCauHinh(), DateTime.Now)</c>
    /// lần thứ HAI, tức một lượt đọc đồng hồ khác với lượt đã dùng để kiểm. Bấm lúc <c>23:59:5x</c> thì câu tổng
    /// kết vừa ghi vào nhật ký nói <c>D → D+180</c> còn Shopee nhận <c>D+1 → D+181</c> — cái được KIỂM không
    /// phải cái được GỬI, và không test nào thấy được vì cả hai đường đều "đúng" riêng lẻ.</para>
    /// <para><b>Luôn ghi lại ở MỌI lối ra của cả hai hàm <c>ThuChay*</c></b>, y khuôn
    /// <see cref="VongYeuCauChay"/> — hai thứ này là một cặp, để sót một cái là nơi gọi cầm gói của lượt cũ.</para>
    /// </summary>
    public ThamSoCombo? ThamSoComboDaKiem { get; private set; }
}
