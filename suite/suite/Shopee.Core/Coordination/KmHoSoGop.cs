namespace Shopee.Core.Coordination;

/// <summary>
/// <b>LUẬT GỘP hồ sơ KM của Shop — hàm THUẦN, MỘT BỘ DUY NHẤT dùng chung cho CẢ hub lẫn client.</b>
/// <para><b>Vì sao bắt buộc dùng chung một file:</b> hồ sơ KM đi hai chiều (client đẩy lên, client kéo về) và
/// mỗi chiều đều phải quyết "bản nào thắng". Viết luật hai lần — một bản trong <c>HubDatabase</c>, một bản
/// trong worker của client — là hai bản sẽ trôi lệch, và triệu chứng của trôi lệch không phải lỗi đỏ mà là
/// <b>một chiếc máy chạy KM trùng</b> hoặc <b>cấu hình người dùng vừa sửa bị nuốt</b>: cả hai đều im lặng.
/// Hub LINK thẳng file này (xem <c>Shopee.Hub.Web.csproj</c>) nên hai phía biên dịch CÙNG một mã nguồn.</para>
/// <para><b>HAI NHÓM TRƯỜNG, HAI LUẬT KHÁC NHAU — đừng gộp làm một:</b></para>
/// <list type="bullet">
/// <item><b>Cấu hình</b> (<c>TuDong</c>, <c>SoNgay</c>, <c>PhanTram</c>, <c>GioBatDau</c>, <c>GioKetThuc</c>,
/// <c>OwnerMachine</c>): bản có <c>CfgUpdatedAt</c> MỚI HƠN thắng. Người dùng đã chốt "máy nào sửa sau thắng"
/// và vẫn muốn mở form ở máy bất kỳ. Mốc BẰNG NHAU mà cấu hình khác ⇒ phá hoà có thứ tự
/// (<see cref="KmHoSoGop.CanPhaHoa"/>), cấu hình y hệt ⇒ giữ bản đang có cho idempotent.</item>
/// <item><b>Mốc chạy</b> (<c>LanChayCuoi</c>, <c>LanThuTuDong</c>): lấy <b>MAX</b> — chỉ tiến, không lùi,
/// <c>null</c> thua mọi giá trị. Đây là <i>sự kiện đã xảy ra</i>, không phải ý muốn của người dùng: cho một
/// máy có mốc cũ ghi đè mốc mới là mở đường chạy KM trùng cho cùng một shop trong cùng một chu kỳ.</item>
/// </list>
/// <para><b>Mốc thời gian ở đây LUÔN là UTC</b> — xem <see cref="KmMocThoiGian"/>. Luật gộp so sánh
/// <see cref="DateTimeOffset"/> nên vẫn đúng kể cả khi hai bên gửi hai offset khác nhau, nhưng quy ước UTC là
/// thứ giữ cho dữ liệu trong bảng hub đọc bằng mắt vẫn hiểu được.</para>
/// </summary>
public static class KmHoSoGop
{
    /// <summary>
    /// <b>BIÊN TRẦN của mốc chạy so với "bây giờ"</b> — mốc xa hơn ngần này ở TƯƠNG LAI bị coi là rác và BỎ
    /// (xem <see cref="MocQuaXaTuongLai"/>).
    /// <para><b>Vì sao phải có trần</b> (phản biện 22/08/2026): <c>LanChayCuoi</c>/<c>LanThuTuDong</c> gộp theo
    /// MAX — một bánh cóc chỉ tiến. Một máy sai đồng hồ (pin CMOS chết, chỉnh nhầm năm) ghi
    /// <c>LanChayCuoi = 2028</c> là mốc đó thắng vĩnh viễn trên hub, mọi máy kéo về, <c>DenGioChay</c> trả
    /// <c>false</c> tới tận 2028 và <b>KM chết trên toàn fleet</b> — im lặng, và không gỡ được từ trong app
    /// (form không lộ mốc chạy, xoá dòng cục bộ thì nhịp kéo 60 giây dựng lại ngay).</para>
    /// <para><b>Vì sao 6 giờ:</b> mốc chạy hợp lệ luôn ở QUÁ KHỨ (nó ghi lúc lượt KM vừa xảy ra), nên về lý
    /// biên nào cũng được. Phần đệm này chỉ để nuốt lệch đồng hồ THẬT giữa hai máy: lệch múi giờ do một máy
    /// đặt sai timezone là bội số của 1 giờ (nặng nhất trong vùng là ±7), lệch NTP thường vài giây. 6 giờ
    /// nằm gọn dưới ngưỡng một chu kỳ ngắn nhất (1 ngày) nên không bao giờ nuốt nhầm mốc thật, mà vẫn chặn
    /// được kiểu sai "nhầm năm/nhầm tháng" — đúng kiểu sai gây thiệt hại vĩnh viễn.</para>
    /// <para><b>Lọc CẢ HAI phía</b> (bản đang giữ lẫn bản vừa nhận), cố ý: đó là đường TỰ GỠ. Mốc rác đã nằm
    /// trên hub sẽ bị rụng ở chính lượt gộp kế tiếp, không cần ai sửa tay database.</para>
    /// </summary>
    public static readonly TimeSpan BienMocTuongLai = TimeSpan.FromHours(6);

    /// <summary>(THUẦN) Mốc có nằm quá xa ở TƯƠNG LAI không (&gt; <paramref name="bayGio"/> +
    /// <see cref="BienMocTuongLai"/>) ⇒ rác, phải bỏ. <c>null</c> ⇒ false. Công khai để nơi gọi GHI LOG được:
    /// bỏ mốc mà không nói gì thì lần sau lại không ai chẩn đoán nổi.</summary>
    public static bool MocQuaXaTuongLai(DateTimeOffset? moc, DateTimeOffset bayGio)
        => moc is { } m && m > bayGio + BienMocTuongLai;

    /// <summary>(THUẦN) Mốc hợp lệ thì giữ nguyên, mốc quá xa tương lai thành <c>null</c> = "chưa có mốc".</summary>
    private static DateTimeOffset? LocMoc(DateTimeOffset? moc, DateTimeOffset bayGio)
        => MocQuaXaTuongLai(moc, bayGio) ? null : moc;

    /// <summary>
    /// (THUẦN) Kẹp trần <c>CfgUpdatedAt</c>: mốc SỬA quá xa tương lai bị <b>KÉO VỀ <paramref name="bayGio"/></b>
    /// — <b>KHÔNG bỏ về <c>null</c></b>.
    /// <para><b>Vì sao phải kẹp</b> (phản biện vòng 3, 22/08/2026): mốc sửa là trọng tài của luật "máy nào sửa
    /// sau thắng", và nó vừa là bánh cóc vừa TỰ LAN — một máy sai đồng hồ đóng dấu <c>2028</c> là bản của nó
    /// thắng vĩnh viễn trên hub, rồi mọi máy kéo về và GHI chính mốc đó xuống <c>km_config</c> của mình, nên
    /// xoá dòng trên hub cũng vô ích (máy kế tiếp đẩy lại đúng mốc ấy). Khác hẳn mốc CHẠY: mốc chạy bị lọc ở
    /// cả hai phía nên tự rụng.</para>
    /// <para><b>Vì sao kẹp về <paramref name="bayGio"/> chứ không bỏ về <c>null</c>:</b> ở nhóm mốc chạy,
    /// <c>null</c> nghĩa là "chưa chạy lần nào" — bỏ đi chỉ mất một sự kiện. Ở đây <c>null</c> nghĩa là "thua
    /// mọi giá trị", tức bỏ mốc là <b>vứt trọn cấu hình</b> của bản đó. Hai ngữ nghĩa khác hẳn nhau.</para>
    /// </summary>
    public static DateTimeOffset? KepMocSua(DateTimeOffset? moc, DateTimeOffset bayGio)
        => MocQuaXaTuongLai(moc, bayGio) ? bayGio : moc;

    /// <summary>
    /// Gộp hồ sơ <paramref name="den"/> (bản vừa nhận) vào <paramref name="dangCo"/> (bản đang giữ) theo hai
    /// luật ở chú thích lớp. IDEMPOTENT: gộp cùng một bản hai lần ra đúng một kết quả — đây là thứ cho phép
    /// worker đẩy/kéo chạy mỗi 60 giây mà không làm dữ liệu dao động.
    /// <para>Đối xứng hai phía: hub gọi với <c>dangCo</c> = dòng trong bảng, <c>den</c> = item client gửi lên;
    /// client gọi với <c>dangCo</c> = <c>km_config</c> cục bộ, <c>den</c> = hồ sơ kéo về từ hub.</para>
    /// <para><b>Dòng CHƯA TỒN TẠI thì nơi gọi phải truyền <c>dangCo = den</c></b>, KHÔNG phải một
    /// <c>new KmProfileItem(login)</c> mặc định — xem chú thích ở <c>HubDatabase.UpsertKmProfiles</c>. Gộp với
    /// bản mặc định là nuốt trọn cấu hình thật ngay lượt đẩy đầu tiên.</para>
    /// </summary>
    /// <param name="bayGio">Mốc "bây giờ" (UTC) để kẹp trần mốc chạy — xem <see cref="BienMocTuongLai"/>.
    /// Nhận từ ngoài để hàm vẫn THUẦN và test được mọi ca biên.</param>
    public static KmProfileItem Gop(KmProfileItem dangCo, KmProfileItem den, DateTimeOffset bayGio)
    {
        ArgumentNullException.ThrowIfNull(dangCo);
        ArgumentNullException.ThrowIfNull(den);

        // Kẹp trần mốc SỬA ở CẢ HAI phía TRƯỚC khi so — xem KepMocSua. Kẹp trước thì mốc rác thôi thắng ngay ở
        // lượt so này, không phải chỉ bị sửa lại sau khi đã quyết xong.
        var mocDangCo = KepMocSua(dangCo.CfgUpdatedAt, bayGio);
        var mocDen = KepMocSua(den.CfgUpdatedAt, bayGio);

        // Cấu hình đi theo GÓI: lấy trọn bộ của bản thắng chứ không trộn từng trường. Trộn lẻ thì "giờ bắt đầu
        // của máy A + giờ kết thúc của máy B" ra một khung giờ chẳng ai từng gõ.
        var nen = MoiHon(mocDen, mocDangCo) ? den
                : MoiHon(mocDangCo, mocDen) ? dangCo
                : ThangKhiHoaMoc(dangCo, den);
        var mocNen = ReferenceEquals(nen, den) ? mocDen : mocDangCo;
        // BẢN THUA — cần riêng cho các trường NULLABLE: "bản nền không biết trường" thì bên kia mới là bên duy
        // nhất có ý kiến. Xem `FlashPhanTramGiam` bên dưới.
        var khac = ReferenceEquals(nen, den) ? dangCo : den;

        var login = dangCo.Login is { Length: > 0 } l ? l : (den.Login ?? "");
        return new KmProfileItem(login)
        {
            TuDong = nen.TuDong,
            SoNgay = nen.SoNgay,
            PhanTram = nen.PhanTram,
            GioBatDau = nen.GioBatDau ?? "",
            GioKetThuc = nen.GioKetThuc ?? "",
            OwnerMachine = nen.OwnerMachine ?? "",
            CfgUpdatedAt = mocNen,
            // 7 trường CẤU HÌNH combo đi theo ĐÚNG BẢN NỀN, cùng gói với cấu hình KM: cùng một mốc sửa
            // (`CfgUpdatedAt`) quyết cả hai nhóm, nên trộn lẻ ở đây là lấy giờ combo của máy A ghép với %
            // giảm của máy B — một cấu hình chẳng ai từng gõ.
            ComboBat = nen.ComboBat,
            ComboGioBatDau = nen.ComboGioBatDau ?? "",
            ComboGioKetThuc = nen.ComboGioKetThuc ?? "",
            ComboLoai = nen.ComboLoai,
            ComboMuaSl = nen.ComboMuaSl,
            ComboGiam = nen.ComboGiam,
            ComboGioiHan = nen.ComboGioiHan,
            // Công tắc vòng FLASH cũng đi theo BẢN NỀN, cùng gói với cấu hình kia — trộn lẻ ở đây là bật
            // một vòng tạo chương trình thật bằng ý của máy A ghép với cấu hình của máy B.
            FlashBat = nen.FlashBat,
            // ⚠ HAI THÔNG SỐ flash (03/09/2026) là NGOẠI LỆ DUY NHẤT của luật "lấy trọn gói của bản nền":
            // `nen.X ?? khac.X`, KHÔNG phải `nen.X`.
            //
            // Vì chúng NULLABLE, và `null` KHÔNG phải một ý muốn — nó là "bên này KHÔNG BIẾT trường này" (hub
            // cũ / client cũ chưa có nó trong JSON). Lấy thẳng `nen.X` là để một bản CŨ thắng mốc sửa XOÁ TRẮNG
            // con số của bản mới: ca đo được (phản biện vòng 2) là client cũ đẩy lên với mốc sửa mới hơn ⇒
            // `nen` = bản cũ ⇒ `FlashPhanTramGiam = null` ⇒ hàng trên hub mất 8% người dùng vừa gõ, rồi mọi máy
            // kéo về DEFAULT 5. Bên không có ý kiến thì không được phủ quyết bên có.
            //
            // Cả hai cùng `null` ⇒ vẫn `null` (không ai biết trường này), và chỗ ánh xạ về `km_config` sẽ giữ
            // giá trị cục bộ (`hoSo.X ?? cu.X`) — xem `KmProfileItem.FlashPhanTramGiam`.
            FlashPhanTramGiam = nen.FlashPhanTramGiam ?? khac.FlashPhanTramGiam,
            FlashSoLuongThamGia = nen.FlashSoLuongThamGia ?? khac.FlashSoLuongThamGia,
            // Lọc trần TRƯỚC khi lấy MAX, và lọc cả hai phía — xem BienMocTuongLai.
            LanChayCuoi = Max(LocMoc(dangCo.LanChayCuoi, bayGio), LocMoc(den.LanChayCuoi, bayGio)),
            LanThuTuDong = Max(LocMoc(dangCo.LanThuTuDong, bayGio), LocMoc(den.LanThuTuDong, bayGio)),
            // Hai mốc COMBO theo ĐÚNG luật MAX như hai mốc trên — không có luật gộp thứ hai.
            ComboLanChayCuoi = Max(LocMoc(dangCo.ComboLanChayCuoi, bayGio), LocMoc(den.ComboLanChayCuoi, bayGio)),
            ComboLanThuTuDong = Max(LocMoc(dangCo.ComboLanThuTuDong, bayGio), LocMoc(den.ComboLanThuTuDong, bayGio)),
            // Hai mốc FLASH theo ĐÚNG luật MAX như bốn mốc trên.
            FlashLanChayCuoi = Max(LocMoc(dangCo.FlashLanChayCuoi, bayGio), LocMoc(den.FlashLanChayCuoi, bayGio)),
            FlashLanThuTuDong = Max(LocMoc(dangCo.FlashLanThuTuDong, bayGio), LocMoc(den.FlashLanThuTuDong, bayGio)),
        };
    }

    /// <summary>(THUẦN) <paramref name="den"/> có mốc sửa MỚI HƠN <paramref name="dangCo"/> không.
    /// <c>null</c> = "máy này chưa từng mở form" ⇒ THUA mọi giá trị; bằng nhau ⇒ false (giữ bản đang có, để
    /// <see cref="Gop"/> idempotent).</summary>
    public static bool MoiHon(DateTimeOffset? den, DateTimeOffset? dangCo)
        => den is { } d && (dangCo is not { } c || d > c);

    /// <summary>
    /// (THUẦN) Hai hồ sơ có CÙNG phần CẤU HÌNH không (bỏ qua mốc sửa và hai mốc chạy). Có tính
    /// <see cref="KmProfileItem.OwnerMachine"/>: hai máy cùng cấu hình mà khác chủ trì thì cả fleet vẫn bất
    /// đồng về "ai chạy lượt tự động" — đúng thứ phải phá hoà.
    /// </summary>
    public static bool CungCauHinh(KmProfileItem a, KmProfileItem b)
        => a.TuDong == b.TuDong
           && a.SoNgay == b.SoNgay
           && a.PhanTram == b.PhanTram
           && string.Equals(a.GioBatDau ?? "", b.GioBatDau ?? "", StringComparison.Ordinal)
           && string.Equals(a.GioKetThuc ?? "", b.GioKetThuc ?? "", StringComparison.Ordinal)
           && string.Equals(a.OwnerMachine ?? "", b.OwnerMachine ?? "", StringComparison.OrdinalIgnoreCase)
           // 7 trường cấu hình COMBO — cùng nhóm "ý muốn người dùng" nên cùng luật. Thiếu chúng ở đây thì hai
           // bản chỉ khác thông số combo bị coi là "y hệt" ⇒ lượt phá hoà giữ bản đang có ⇒ thay đổi combo của
           // máy kia biến mất, không một dòng log.
           && a.ComboBat == b.ComboBat
           && string.Equals(a.ComboGioBatDau ?? "", b.ComboGioBatDau ?? "", StringComparison.Ordinal)
           && string.Equals(a.ComboGioKetThuc ?? "", b.ComboGioKetThuc ?? "", StringComparison.Ordinal)
           && a.ComboLoai == b.ComboLoai
           && a.ComboMuaSl == b.ComboMuaSl
           && a.ComboGiam == b.ComboGiam
           && a.ComboGioiHan == b.ComboGioiHan
           // Công tắc vòng FLASH — thiếu nó ở đây thì hai bản chỉ khác mỗi "có bật flash không" bị coi là y
           // hệt, và lượt phá hoà giữ bản đang có ⇒ cú BẬT (hoặc cú TẮT) của máy kia biến mất, không một dòng log.
           && a.FlashBat == b.FlashBat
           // Hai THÔNG SỐ flash (03/09/2026) — cùng nhóm "ý muốn người dùng", nhưng so bằng
           // `CungThamSoNullable`: `null` = "bên này KHÔNG BIẾT trường", không phải một ý kiến khác.
           && CungThamSoNullable(a.FlashPhanTramGiam, b.FlashPhanTramGiam)
           && CungThamSoNullable(a.FlashSoLuongThamGia, b.FlashSoLuongThamGia);

    /// <summary>
    /// (THUẦN) Hai giá trị của MỘT trường nullable có mâu thuẫn nhau không — <c>null</c> ở bất kỳ bên nào nghĩa
    /// là <b>bên đó KHÔNG BIẾT trường này</b> (hub cũ / client cũ chưa có nó trong JSON), tức KHÔNG có ý kiến, tức
    /// KHÔNG phải một khác biệt.
    ///
    /// <para><b>Vì sao không so thẳng <c>a == b</c></b> (đo được ngay lượt đầu, 03/09/2026): trong cửa sổ rollout
    /// "client MỚI ↔ hub CŨ", máy đẩy 8% lên rồi nhận về một hồ sơ không có trường ⇒ hai bản CÙNG mốc sửa mà
    /// <c>CungCauHinh</c> nói "khác" ⇒ <see cref="CanPhaHoa"/> bật ⇒ mỗi lượt kéo ghi một dòng nhật ký "cùng mốc
    /// sửa mà cấu hình khác nhau, đã chọn bản thắng theo thứ tự cố định" — một CẢNH BÁO SAI, cho một tình huống
    /// mà cả thiết kế nullable sinh ra để đi qua trong im lặng. (Bài
    /// <c>KmApHoSoVeMayTests.ChiMocChayTien_ThiGhiDbNhungKhongLog</c> bắt đúng ca này.)</para>
    ///
    /// <para><b>Và nó khớp với LUẬT ÁP</b>: <c>OrdersModuleHost.ApNhomCauHinh</c> đọc <c>hoSo.X ?? cu.X</c>, tức
    /// một bản <c>null</c> KHÔNG bao giờ đổi được giá trị cục bộ. Nếu phép so nói "khác" trong khi phép áp không
    /// đổi gì thì hai luật đang trả lời hai câu khác nhau cho cùng một cặp bản — và chênh lệch đó chỉ hiện ra
    /// dưới dạng nhật ký nói sai.</para>
    ///
    /// <para><b>Đánh đổi đã biết:</b> quan hệ này KHÔNG bắc cầu (5 ~ null ~ 8 nhưng 5 ≁ 8). Chấp nhận: nơi dùng
    /// duy nhất là phép so HAI bản, không phải một phép phân lớp.</para>
    /// </summary>
    private static bool CungThamSoNullable(int? a, int? b) => a is null || b is null || a == b;

    /// <summary>
    /// (THUẦN) Lượt gộp này có phải PHÁ HOÀ không: hai bản CÙNG mốc sửa (sau khi kẹp trần) mà KHÁC cấu hình.
    /// Công khai để nơi gọi GHI LOG được — lượt phá hoà là lượt một máy mất cấu hình của mình theo một luật mà
    /// người dùng không hề gõ ra; im lặng ở đó thì không ai lần lại nổi.
    /// </summary>
    public static bool CanPhaHoa(KmProfileItem dangCo, KmProfileItem den, DateTimeOffset bayGio)
    {
        var a = KepMocSua(dangCo.CfgUpdatedAt, bayGio);
        var b = KepMocSua(den.CfgUpdatedAt, bayGio);
        return !MoiHon(a, b) && !MoiHon(b, a) && !CungCauHinh(dangCo, den);
    }

    /// <summary>
    /// (THUẦN) Bản THẮNG khi hai bên CÙNG mốc sửa mà khác cấu hình.
    /// <para><b>Vì sao cần</b> (phản biện vòng 3, 22/08/2026): lượt backfill đóng dấu CÙNG một mốc quá khứ cho
    /// mọi máy, mà <see cref="MoiHon"/> dùng <c>&gt;</c> nên "bằng nhau = thua" ⇒ bản thắng là bản ĐẨY TRƯỚC,
    /// tức phụ thuộc máy nào khởi động trước. Hệ quả: hub giữ bản của máy B, máy A đẩy lên không đổi được gì, A
    /// kéo về cũng không đổi được gì ⇒ <b>hub / A / B ba bản khác nhau, ổn định VĨNH VIỄN</b>.</para>
    /// <para><b>Thứ tự phá hoà:</b> (1) bản có <c>TuDong = true</c> thắng — một lịch đang BẬT là thứ người dùng
    /// cố ý đặt, còn bản tắt thường chỉ là mặc định của máy chưa ai mở form; (2) hoà tiếp thì
    /// <c>OwnerMachine</c> nhỏ hơn thắng — không mang ý nghĩa nghiệp vụ nào, chỉ cần MỘT thứ tự TOÀN PHẦN để
    /// mọi máy ra cùng một kết quả; (3) cấu hình y hệt nhau ⇒ giữ bản đang có, để <see cref="Gop"/>
    /// idempotent.</para>
    /// </summary>
    private static KmProfileItem ThangKhiHoaMoc(KmProfileItem dangCo, KmProfileItem den)
    {
        if (CungCauHinh(dangCo, den)) { return dangCo; }
        if (dangCo.TuDong != den.TuDong) { return dangCo.TuDong ? dangCo : den; }

        // ⚠ CHUỖI RỖNG KHÔNG PHẢI MỘT ID MÁY — phải loại nó khỏi vế so, đừng bỏ hai dòng này.
        // `string.Compare` xếp "" trước MỌI chuỗi, nên bản có `OwnerMachine = ""` thắng ở CẢ HAI chiều
        // (`Compare("", "may-A") < 0` ⇒ giữ bản rỗng; `Compare("may-A", "") > 0` ⇒ lấy bản rỗng). Hậu quả thật
        // (phản biện vòng 6, 22/08/2026): chỉ cần MỘT máy từng đẩy hồ sơ với chủ trì rỗng — ví dụ lượt backfill
        // hỏng thoáng qua — là quyền chủ trì bị xoá trên TOÀN FLEET ở lượt gộp kế, và `LaMayChuTri("", …)` trả
        // true cho mọi máy ⇒ đêm sau cả fleet cùng chạy KM. Đúng blocker W3 quay lại, không một dòng log.
        var coA = !string.IsNullOrWhiteSpace(dangCo.OwnerMachine);
        var coB = !string.IsNullOrWhiteSpace(den.OwnerMachine);
        if (coA != coB) { return coA ? dangCo : den; }   // bản CÓ chủ trì thắng bản không có

        return string.Compare(dangCo.OwnerMachine ?? "", den.OwnerMachine ?? "",
            StringComparison.OrdinalIgnoreCase) <= 0 ? dangCo : den;
    }

    /// <summary>(THUẦN) Mốc LỚN HƠN trong hai mốc; <c>null</c> thua mọi giá trị, cả hai null ⇒ null.
    /// Dùng cho <c>LanChayCuoi</c>/<c>LanThuTuDong</c> — chỉ tiến, không bao giờ lùi.</summary>
    public static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a is not { } x) { return b; }
        if (b is not { } y) { return a; }
        return x >= y ? x : y;
    }
}

/// <summary>
/// <b>QUY ƯỚC MÚI GIỜ của hồ sơ KM — đổi ở ĐÚNG HAI CỬA (đẩy / kéo), không rải khắp nơi.</b>
/// <list type="bullet">
/// <item>Trên dây và trong bảng <c>km_profiles</c> của hub: <see cref="DateTimeOffset"/> <b>UTC</b>.</item>
/// <item>Trong <c>km_config</c> của client và trong mọi luật của <c>KmLichTuDong</c>: <b>giờ địa phương</b>
/// (khung KM 2–6h là giờ địa phương của người dùng, không phải giờ UTC).</item>
/// </list>
/// <para><b>Vì sao đây là rủi ro số 1 của cả đợt:</b> <c>km_config</c> đang lưu <see cref="DateTime"/> với
/// <c>Kind = Unspecified</c> — <c>ToString("o")</c> của nó KHÔNG kèm offset. Đẩy thẳng chuỗi đó lên hub rồi
/// máy khác đọc về là lệch 7 tiếng, và triệu chứng không phải lỗi đỏ: chu kỳ "mỗi 7 ngày" thành 6 hoặc 8, hoặc
/// mốc kế rơi ra ngoài khung 2–6h và <b>KM không bao giờ chạy nữa</b> mà không một dòng log nào nói vì sao.</para>
/// <para><b>Không xử DST:</b> múi giờ Việt Nam không có giờ mùa hè, nên một mốc địa phương luôn ánh xạ 1–1 sang
/// UTC. Máy đặt múi giờ có DST thì hai mốc trong giờ nhập nhằng có thể lệch một tiếng — chấp nhận, vì đổi sang
/// lưu offset trong <c>km_config</c> là đổi hợp đồng của một bảng đã có dữ liệu người dùng.</para>
/// </summary>
public static class KmMocThoiGian
{
    /// <summary>Mốc trong DB client (giờ địa phương) → mốc đi dây (UTC). <c>Kind = Unspecified</c> được HIỂU LÀ
    /// giờ địa phương — đúng thứ <c>km_config</c> đang lưu; <c>Kind = Utc</c> giữ nguyên nghĩa (dữ liệu cũ ghi
    /// bằng <c>DateTime.Now</c> ra <c>Local</c>, ghi bằng chuỗi có offset đọc lại cũng ra <c>Local</c>).</summary>
    public static DateTimeOffset? LenDay(DateTime? mocMay)
    {
        if (mocMay is not { } d) { return null; }
        var offs = d.Kind == DateTimeKind.Utc
            ? new DateTimeOffset(d)
            : new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Local));
        return offs.ToUniversalTime();
    }

    /// <summary>Mốc đi dây (UTC) → mốc trong DB client (giờ địa phương, <c>Kind = Unspecified</c> đúng như
    /// <c>km_config</c> đang lưu). Đi vòng tròn <c>VeMay(LenDay(x)) == x</c> cho mọi mốc địa phương.</summary>
    public static DateTime? VeMay(DateTimeOffset? mocDay)
        => mocDay is { } d ? d.ToLocalTime().DateTime : null;
}
