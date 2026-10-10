using System.Text.RegularExpressions;
using Jint;

namespace Shopee.Core.Tests;

/// <summary>
/// Lưới cho <c>extensions/shopee-orders/flow-chung.js</c> — hạ tầng DÙNG CHUNG của ba luồng tạo chương trình
/// (KM · Combo · Flash Sale), tách khỏi <c>flow-km.js</c> ngày 29/08/2026.
/// <para>
/// <b>Vì sao cần file test này.</b> Đợt gom 29/08/2026 làm hai việc có rủi ro khác hẳn nhau:
/// (a) <b>di chuyển</b> hàm sang module mới — rủi ro là chép lệch; (b) <b>gom</b> ba hàm thuần trước đây chép
/// tay hai bản (<c>docMocNgay</c>, <c>mocNgayLuotDo</c>, <c>cauLoiThamSo</c>) — rủi ro là đổi hành vi của MỘT
/// trong hai luồng mà không ai thấy. Hai hàm <c>gomCanhBao</c>/<c>soTrangLonNhat</c> lại KHÔNG được lượt "Dò DOM
/// Flash Sale" phủ (luồng flash không import chúng), nên nếu không có test này thì bằng chứng runtime duy nhất
/// của chúng là lượt chạy ĐÊM trên shop thật — tức phát hiện lỗi sau khi nó đã tạo chương trình sai.
/// </para>
/// <para>
/// Không có bản chép luật nào trong C#: nguồn hàm được bóc NGUYÊN VĂN ra khỏi file production bằng Acornima rồi
/// chạy thật bằng Jint. Sửa thân hàm ở <c>flow-chung.js</c> là test này đổi theo ngay — đó chính là điều kiện
/// để nó không phải test trang trí.
/// </para>
/// </summary>
public class FlowChungTests
{
    private const string FileChung = "flow-chung.js";
    private const string FileKm = "flow-km.js";
    private const string FileCombo = "flow-combo.js";

    /// <summary>
    /// Nguyên văn khai báo một hằng <c>export const X = …;</c> top-level. Không còn hằng ⇒ ném ngay tại đây:
    /// hằng bị dời đi nơi khác là <c>gomCanhBao</c> mất chỗ dựa của nhánh dự phòng, và đó đúng là thứ test 4 canh.
    /// </summary>
    private static string NguonHang(string tenFile, string ten)
    {
        var nguon = FlashSaleDoDomTests.NguonFile(tenFile);
        // KHÔNG neo `$`: trong .NET, `$` Multiline khớp ngay trước `\n` — bản checkout CRLF có `\r` đứng đó nên
        // `.+;$` trượt và test đỏ với thông điệp sai thủ phạm (phản biện 29/08). `[^;\r\n]` giữ khớp trong MỘT dòng.
        var m = Regex.Match(nguon, $@"^export const {Regex.Escape(ten)} = [^;\r\n]+;", RegexOptions.Multiline);
        Assert.True(m.Success, $"{tenFile} không còn hằng export '{ten}'.");
        return m.Value;
    }

    /// <summary>Máy JS chỉ nạp ĐÚNG những khai báo được kể tên — <c>export</c> bị cắt vì Jint chạy Script, không Module.</summary>
    private static Engine May(params string[] khaiBao)
    {
        var engine = new Engine();
        foreach (var kb in khaiBao)
        {
            engine.Execute(kb.StartsWith("export ", StringComparison.Ordinal) ? kb["export ".Length..] : kb);
        }

        return engine;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 1. `cauLoiThamSo` — hàm DUY NHẤT bị ĐỔI CHỮ KÝ trong đợt gom (thêm tham số `nhan`).

    /// <summary>
    /// Chuỗi lỗi tham số phải ra ĐÚNG NGUYÊN VĂN như hai bản song sinh cũ, kể cả dấu nối <c>" · "</c>.
    /// <para>
    /// <b>Hợp đồng dễ hiểu sai nhất của hàm này:</b> <c>loi</c> là một OBJECT LỖI THEO TRƯỜNG, KHÔNG phải chuỗi.
    /// Đường chạy SẠCH hằng đêm truyền <c>{}</c> ⇒ hàm phải trả <c>null</c>. Ai "đơn giản hoá" thân hàm thành nối
    /// chuỗi <c>+ loi</c> thì mọi lượt sạch sẽ ĐỎ với câu "… — [object Object]" — nên ca <c>{}</c> ⇒ <c>null</c>
    /// dưới đây là ca BẮT BUỘC, không phải ca biên.
    /// </para>
    /// </summary>
    [Fact]
    public void CauLoiThamSo_NguyenVanCu_KhongDoiMotKyTu()
    {
        var engine = May(FlashSaleDoDomTests.NguonHam(FileChung, "cauLoiThamSo"));

        // MỘT trường hỏng — khuôn chuỗi thật đã đi lên nhật ký C# (mẫu trong KmVongMoiShopTests).
        Assert.Equal(
            "tham số lượt KM KHÔNG dùng được — phanTram: % giảm không hợp lệ",
            engine.Evaluate("cauLoiThamSo({ phanTram: '% giảm không hợp lệ' }, 'KM')").ToObject());

        // HAI trường ⇒ nối bằng " · ", giữ đúng thứ tự khoá của object.
        Assert.Equal(
            "tham số lượt Combo KHÔNG dùng được — ten: thiếu tên chương trình · gio: giờ không hợp lệ",
            engine.Evaluate("cauLoiThamSo({ ten: 'thiếu tên chương trình', gio: 'giờ không hợp lệ' }, 'Combo')").ToObject());

        // Nhãn là thứ DUY NHẤT khác nhau giữa hai luồng — cùng một `loi` phải ra hai câu chỉ lệch đúng chữ đó.
        Assert.Equal(
            "tham số lượt Combo KHÔNG dùng được — phanTram: % giảm không hợp lệ",
            engine.Evaluate("cauLoiThamSo({ phanTram: '% giảm không hợp lệ' }, 'Combo')").ToObject());

        // ĐƯỜNG CHẠY SẠCH HẰNG ĐÊM — không trường nào hỏng ⇒ null, cho CẢ HAI nhãn.
        Assert.Null(engine.Evaluate("cauLoiThamSo({}, 'KM')").ToObject());
        Assert.Null(engine.Evaluate("cauLoiThamSo({}, 'Combo')").ToObject());
        Assert.Null(engine.Evaluate("cauLoiThamSo(null, 'KM')").ToObject());
        Assert.Null(engine.Evaluate("cauLoiThamSo(undefined, 'KM')").ToObject());

        // THIẾU NHÃN ⇒ NÉM TO TIẾNG (phản biện 29/08): chỗ gọi thứ ba trong tương lai mà quên truyền nhãn thì
        // phải chết ngay lúc dev, không phải lặng lẽ ghi "tham số lượt undefined KHÔNG dùng được" lên nhật ký.
        var nem = Assert.Throws<Jint.Runtime.JavaScriptException>(() => engine.Evaluate("cauLoiThamSo({ x: 1 })"));
        Assert.Contains("thiếu nhãn luồng", nem.Message);
    }

    /// <summary>
    /// Chỗ GỌI phải truyền đúng nhãn của luồng mình.
    /// <para>Đây là lưới cho rủi ro MỚI DUY NHẤT mà việc tham-số-hoá sinh ra: thân hàm chung có thể đúng tuyệt
    /// đối trong khi một luồng truyền nhãn sai (hoặc quên truyền ⇒ "tham số lượt undefined KHÔNG dùng được").
    /// Test 1 KHÔNG bắt được ca đó — nó chỉ chấm thân hàm — nên phải có hai lưới, không phải một.</para>
    /// </summary>
    [Fact]
    public void CauLoiThamSo_CallSite_TruyenDungNhan()
    {
        Assert.Contains(
            "cauLoiThamSo(tp.loi, \"KM\")",
            FlashSaleDoDomTests.NguonHam(FileKm, "chayKmChoTabDangMo"));
        Assert.Contains(
            "cauLoiThamSo(tp.loi, \"Combo\")",
            FlashSaleDoDomTests.NguonHam(FileCombo, "chayComboChoTabDangMo"));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2. `docMocNgay` — bản GOM của hai bản song sinh cũ, chạy thật.

    /// <summary>
    /// Mốc ngày: ba số rời dùng để CLICK, <c>ngayDayDu</c> dùng cho phép ĐỌC LẠI — hai vế lệch nhau thì mọi bước
    /// vẫn xanh mà chương trình mọc SAI NGÀY, nên hàm phải tự đối chiếu và lỗi phải CÓ TÊN.
    /// </summary>
    [Fact]
    public void DocMocNgay_ChayThatQuaJint_CaDungCaHong()
    {
        var engine = May(FlashSaleDoDomTests.NguonHam(FileChung, "docMocNgay"));
        const string mocDung = "docMocNgay({ ngayTrongThang: 5, thang: 3, nam: 2027, ngayDayDu: '05-03-2027' })";

        // CA ĐÚNG — đủ bốn trường, không lỗi.
        Assert.Null(engine.Evaluate($"{mocDung}.loi").ToObject());
        Assert.Equal(5d, engine.Evaluate($"{mocDung}.ngayTrongThang").ToObject());
        Assert.Equal(3d, engine.Evaluate($"{mocDung}.thang").ToObject());
        Assert.Equal(2027d, engine.Evaluate($"{mocDung}.nam").ToObject());
        Assert.Equal("05-03-2027", engine.Evaluate($"{mocDung}.ngayDayDu").ToObject());

        // CA HỎNG 1 — `ngayDayDu` KHÔNG khớp ba số rời (đúng ca "mọi bước xanh mà sai ngày").
        var lech = engine.Evaluate(
            "docMocNgay({ ngayTrongThang: 5, thang: 3, nam: 2027, ngayDayDu: '06-03-2027' }).loi").ToObject() as string ?? "";
        Assert.Contains("KHÔNG khớp", lech);
        Assert.Contains("06-03-2027", lech);
        Assert.Contains("05-03-2027", lech);

        // CA HỎNG 2 — thiếu `ngayDayDu`: lỗi phải gọi ĐÍCH DANH tên trường, không phải "ngày sai" chung chung.
        var thieu = engine.Evaluate(
            "docMocNgay({ ngayTrongThang: 5, thang: 3, nam: 2027 }).loi").ToObject() as string ?? "";
        Assert.Contains("ngayDayDu", thieu);

        // CA HỎNG 3 — ba số rời vô nghĩa: lỗi nêu cả ba giá trị đọc được để người đọc nhật ký biết gói tới hỏng ở đâu.
        var soHong = engine.Evaluate(
            "docMocNgay({ ngayTrongThang: 32, thang: 13, nam: 1999, ngayDayDu: '32-13-1999' }).loi").ToObject() as string ?? "";
        Assert.Contains("ngày/tháng/năm không hợp lệ", soHong);
        Assert.Contains("32", soHong);

        // CA HỎNG 4 — không phải object: KHÔNG được ném, phải ra lỗi có tên (gói tới rỗng vẫn là ca thật).
        Assert.Contains("ngày/tháng/năm không hợp lệ", engine.Evaluate("docMocNgay(null).loi").ToObject() as string ?? "");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 3. Hai hàm KHÔNG được lượt Dò DOM Flash Sale phủ — bằng chứng runtime duy nhất trước đêm chạy đầu.

    /// <summary>
    /// <c>gomCanhBao</c> + <c>soTrangLonNhat</c> chạy thật TỪ NGUỒN MỚI (<c>flow-chung.js</c>).
    /// <para><b>Ca đắt nhất ở đây là ca CHẠM TRẦN KHÔNG TRUYỀN <c>tran</c></b>: nó ép nhánh dự phòng đọc hằng
    /// module <c>TRAN_CANH_BAO</c> — đúng chỗ mà việc dời file có thể làm hỏng âm thầm (hằng ở lại file cũ, hàm
    /// sang file mới ⇒ trong trang là <c>ReferenceError</c> giữa lúc đang gom cảnh báo cuối shop). Trần kỳ vọng
    /// viết CỨNG là 10, không đọc lại từ nguồn: đọc lại thì đổi hằng thành 999 test vẫn xanh — proof đo rỗng.</para>
    /// </summary>
    [Fact]
    public void GomCanhBaoVaSoTrangLonNhat_ChayThatTuNguonMoi()
    {
        var hang = NguonHang(FileChung, "TRAN_CANH_BAO");
        Assert.Equal("export const TRAN_CANH_BAO = 10;", hang);

        var engine = May(
            hang,
            FlashSaleDoDomTests.NguonHam(FileChung, "gomCanhBao"),
            FlashSaleDoDomTests.NguonHam(FileChung, "soTrangLonNhat"));

        // GOM theo loại: hai lần cùng `loai` ⇒ MỘT mục có đếm + chi tiết; loại chỉ xảy ra một lần ⇒ giữ NGUYÊN câu.
        Assert.Equal(
            "[\"2 lần — bảng sản phẩm lạ (trang 3, trang 7)\",\"chạm trần 105 trang\"]",
            engine.Evaluate(
                "JSON.stringify(gomCanhBao([" +
                "{loai:'bang', cau:'trang 3: bảng sản phẩm lạ', gom:'bảng sản phẩm lạ', chiTiet:'trang 3'}," +
                "{loai:'bang', cau:'trang 7: bảng sản phẩm lạ', gom:'bảng sản phẩm lạ', chiTiet:'trang 7'}," +
                "{loai:'tran', cau:'chạm trần 105 trang'}], 10))").ToObject());

        // Cảnh báo dạng CHUỖI trần trụi vẫn phải gom được (C# gửi lẫn hai kiểu).
        Assert.Equal(
            "[\"2 lần — pager hỏng\"]",
            engine.Evaluate("JSON.stringify(gomCanhBao(['pager hỏng', 'pager hỏng'], 10))").ToObject());

        // CHẠM TRẦN QUA NHÁNH DỰ PHÒNG — KHÔNG truyền `tran`, nên trần phải là hằng module (10).
        const string muoiHaiLoai = "gomCanhBao(Array.from({length: 12}, function (_, i) { return 'loai ' + i; }))";
        Assert.Equal(11d, engine.Evaluate($"{muoiHaiLoai}.length").ToObject());
        Assert.Equal(
            "… + 2 cảnh báo nữa (đã gom theo loại)",
            engine.Evaluate($"{muoiHaiLoai}[10]").ToObject());

        // `tran` truyền tường minh (đường của flow-combo.js) vẫn thắng nhánh dự phòng.
        Assert.Equal(4d, engine.Evaluate(
            "gomCanhBao(Array.from({length: 12}, function (_, i) { return 'loai ' + i; }), 3).length").ToObject());

        // soTrangLonNhat — CẬN DƯỚI của tổng số trang: bỏ qua ô "…", lấy số lớn nhất.
        Assert.Equal(24d, engine.Evaluate("soTrangLonNhat(['1','2','3','…','24'])").ToObject());
        Assert.Equal(0d, engine.Evaluate("soTrangLonNhat(['…'])").ToObject());
        Assert.Equal(0d, engine.Evaluate("soTrangLonNhat([])").ToObject());
        Assert.Equal(0d, engine.Evaluate("soTrangLonNhat(null)").ToObject());
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 4. `mocNgayLuotDo` + `NGAY_DAT_TRUOC_LUOT_DO` — lỗ phủ do phản biện 29/08 chỉ ra: xmldoc đầu file khai
    //    phủ cặp này mà không test nào chạm; sửa lệch một trường là CẢ HAI lượt dò KM/Combo dựng sai ngày
    //    trong khi mọi test vẫn xanh (docMocNgay không bắt được vì 4 trường dựng từ CÙNG một Date).

    /// <summary>
    /// <c>mocNgayLuotDo</c> dựng CẢ BỐN trường từ cùng một <c>Date</c> — dựng lệch (vd quên <c>+1</c> cho tháng
    /// 0-based) thì ba số rời lẫn <c>ngayDayDu</c> cùng lệch, hai vế vẫn khớp nhau và <c>docMocNgay</c> cho qua.
    /// Test này vì thế phải chấm TRỰC TIẾP từng trường trên một mốc cố định.
    /// </summary>
    [Fact]
    public void MocNgayLuotDo_BonTruongTuMotDate_KhopLichThat()
    {
        var engine = May(FlashSaleDoDomTests.NguonHam(FileChung, "mocNgayLuotDo"));
        const string goi = "mocNgayLuotDo(new Date(2027, 2, 5))"; // JS Date: tháng 0-based ⇒ 2 = THÁNG BA

        Assert.Equal(5d, engine.Evaluate($"{goi}.ngayTrongThang").ToObject());
        Assert.Equal(3d, engine.Evaluate($"{goi}.thang").ToObject());
        Assert.Equal(2027d, engine.Evaluate($"{goi}.nam").ToObject());
        Assert.Equal("05-03-2027", engine.Evaluate($"{goi}.ngayDayDu").ToObject());
    }

    /// <summary>
    /// Hằng 180 là BẤT BIẾN LIÊN NGÔN NGỮ: <c>NGAY_DAT_TRUOC_LUOT_DO</c> (JS, đường DÒ) phải bằng
    /// <c>KmLichTuDong.SoNgayDatTruoc</c> (C#, luồng thật). Lệch nhau thì lượt dò đi một đường còn luồng thật
    /// đi đường khác — nút dò thôi chẩn đoán được cho luồng thật. Cùng khuôn "đọc nguồn hai bên rồi so" mà
    /// <c>TraHangParserTests</c>/<c>ThuLaiShopRoiViCauNoiTests</c> đang dùng.
    /// </summary>
    [Fact]
    public void NgayDatTruoc_JsVaCSharp_CungMotSo()
    {
        var mJs = Regex.Match(NguonHang(FileChung, "NGAY_DAT_TRUOC_LUOT_DO"), @"= (\d+);");
        Assert.True(mJs.Success, "không đọc được trị số của NGAY_DAT_TRUOC_LUOT_DO");

        var gocRepo = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(FlashSaleDoDomTests.DuongFile(FileChung))!, "..", ".."));
        var nguonCs = File.ReadAllText(Path.Combine(
            gocRepo, "orders", "XuLyDonShopee.Core", "Services", "KmLichTuDong.cs"));
        var mCs = Regex.Match(nguonCs, @"public const int SoNgayDatTruoc = (\d+);");
        Assert.True(mCs.Success, "KmLichTuDong.cs không còn hằng SoNgayDatTruoc");

        Assert.Equal(mCs.Groups[1].Value, mJs.Groups[1].Value);
    }
}
