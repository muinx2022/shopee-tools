using System.Text.RegularExpressions;
using Acornima;
using Acornima.Ast;
// `UnwrapIfPromise` là extension trong namespace `Jint` — cần cho lưới CHẠY THẬT `choLichLang` (hàm async).
using Jint;

namespace Shopee.Core.Tests;

/// <summary>
/// Lượt <b>DÒ DOM "Flash Sale Của Shop"</b> — phần kiểm được bằng test là <b>JS chạy trong trang</b>:
/// hai luật THUẦN và <b>ràng buộc "KHÔNG TẠO GÌ"</b>.
///
/// <para><b>Vì sao ràng buộc "không tạo gì" phải có lưới, chứ không phải chỉ có comment:</b> nút "Dò DOM KM"
/// khởi đầu cũng chỉ dò, rồi lặng lẽ thành nút TẠO THẬT khi nó được nối vào chuỗi bước của luồng thật — đêm
/// 27/08/2026 nó để lại một chương trình rác <c>DO DOM KM …</c> trên shop người dùng, phải xoá tay, và mọi câu
/// chữ "chỉ dò, không tạo gì" trong mã lúc đó đã thành lời nói dối. Lưới ở đây khoá bằng WHITELIST: mọc thêm
/// một hàm có <c>.click()</c>, hoặc thêm một chữ dùng để lọc nút, là test đỏ ngay.</para>
///
/// <para><b>Vì sao lái được JS từ C#</b>: page-func bị <c>chrome.scripting</c> serialize ĐỘC LẬP nên không gọi
/// hàm ngoài được — luật phải cài lên <c>window</c> rồi gọi bare. Test lấy NGUYÊN VĂN thân hàm ra khỏi file
/// production bằng Acornima rồi chạy bằng Jint. Không có bản chép luật nào trong C#: sửa luật ở
/// <c>flow-flashsale.js</c> là test này đổi theo ngay — đó chính là điều kiện để nó không phải test trang trí.
/// Cùng khuôn với <see cref="EdsONgayHopLeTests"/>.</para>
/// </summary>
public class FlashSaleDoDomTests
{
    private const string FileFlash = "flow-flashsale.js";
    private const string FileKm = "flow-km.js";

    /// <summary>Đi từ thư mục chạy test ngược lên tìm gốc repo (có <c>ShopeeSuite.sln</c> + <c>extensions/</c>).</summary>
    internal static string DuongFile(string ten)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var f = Path.Combine(dir.FullName, "extensions", "shopee-orders", ten);
            if (File.Exists(Path.Combine(dir.FullName, "ShopeeSuite.sln")) && File.Exists(f))
            {
                return f;
            }
        }

        throw new FileNotFoundException($"Không tìm thấy extensions/shopee-orders/{ten} từ {AppContext.BaseDirectory}");
    }

    internal static string NguonFile(string ten) => File.ReadAllText(DuongFile(ten));

    internal static Module Cay(string ten) => new Parser().ParseModule(NguonFile(ten), DuongFile(ten));

    /// <summary>Mọi khai báo hàm TOP-LEVEL của một module (kể cả khi có <c>export</c>).</summary>
    internal static IEnumerable<FunctionDeclaration> MoiHamTopLevel(Module module)
    {
        foreach (var stmt in module.Body)
        {
            var fd = stmt switch
            {
                FunctionDeclaration f => f,
                ExportNamedDeclaration en when en.Declaration is FunctionDeclaration ef => ef,
                _ => null,
            };
            if (fd?.Id is not null)
            {
                yield return fd;
            }
        }
    }

    /// <summary>Nguyên văn nguồn của MỘT hàm top-level.</summary>
    internal static string NguonHam(string tenFile, string tenHam)
    {
        var nguon = NguonFile(tenFile);
        foreach (var fd in MoiHamTopLevel(Cay(tenFile)))
        {
            if (fd.Id!.Name == tenHam)
            {
                return nguon[fd.Range.Start..fd.Range.End];
            }
        }

        throw new InvalidOperationException($"{tenFile} không còn hàm top-level tên '{tenHam}'");
    }

    /// <summary>Duyệt TOÀN BỘ cây AST (dùng cho các lưới whitelist — regex không đủ tin cho việc này).</summary>
    internal static IEnumerable<Node> MoiNut(Node goc)
    {
        yield return goc;
        foreach (var con in goc.ChildNodes)
        {
            if (con is null)
            {
                continue;
            }
            foreach (var x in MoiNut(con))
            {
                yield return x;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 1. LUẬT "ô ngày này có ĐỎ không" — chạy THẬT bằng Jint, và phải là luật CHUNG với flow-km.js
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Máy JS đã nạp CẢ HAI luật thật: luật ô ngày chung của <c>flow-km.js</c> rồi luật ĐỎ của Flash Sale.
    /// <para><c>var _edsONgayHopLe = window._edsONgayHopLe;</c> là để giả lập đúng một tính chất của trình
    /// duyệt mà Jint không có: trong trang, <c>window.x</c> LÀ biến toàn cục <c>x</c>, nên page-func gọi bare
    /// <c>_edsONgayHopLe(...)</c> mới chạy được. Thiếu dòng này thì test đỏ vì môi trường, không phải vì mã.</para>
    /// </summary>
    private static Func<string, bool, bool> LuatNgayDoThat()
    {
        var engine = new Jint.Engine();
        engine.Execute("var window = {};");
        engine.Execute(NguonHam(FileKm, "pageCaiLuatONgayEds"));
        engine.Execute("pageCaiLuatONgayEds();");
        engine.Execute("var _edsONgayHopLe = window._edsONgayHopLe;");
        engine.Execute(NguonHam(FileFlash, "pageCaiLuatNgayDoFlash"));
        engine.Execute("pageCaiLuatNgayDoFlash();");

        var luat = engine.Evaluate("window._flashNgayDo");
        Assert.True(luat is Jint.Native.Function.Function,
            $"pageCaiLuatNgayDoFlash() chạy xong mà window._flashNgayDo không phải hàm (là '{luat.Type}')");

        // Ép KIỂU CỨNG sang bool: luật trả về thứ khác boolean (undefined vì lỗi, chuỗi, số…) thì test nổ ngay
        // chứ không lặng lẽ đi qua phép quy đổi truthy của JS — đúng thứ đã che mất một proof đo rỗng đợt trước.
        return (cls, coTimeslots) => (bool)engine.Invoke(luat, cls, coTimeslots).ToObject()!;
    }

    /// <summary>
    /// Ma trận className × có-khung-giờ. Hai dòng <c>disabled-next</c>/<c>disabled-prev</c> là CA GIẾT bản luật
    /// so-chuỗi-con đã hạ 12/12 shop đêm 27/08/2026: chúng chỉ là bo góc dải, ô vẫn HỢP LỆ.
    /// </summary>
    [Theory]
    // Ngày thường, còn khung ⇒ ĐỎ.
    [InlineData("eds-date-table__cell normal", true, true)]
    // ⚠ Ô ở BIÊN dải (mang `disabled-next`) mà còn khung ⇒ VẪN ĐỎ. Bản `/disabled/i.test` loại oan đúng ô này.
    [InlineData("eds-date-table__cell normal current selected disabled-next", true, true)]
    // ⚠ Đầu kia của dải.
    [InlineData("eds-date-table__cell normal disabled-prev", true, true)]
    // Ô THẬT SỰ bị vô hiệu — dù có khung cũng KHÔNG được bấm.
    [InlineData("eds-date-table__cell normal disabled", true, false)]
    // Ngày hết khung (`div.no-timeslot`) ⇒ không đỏ, kể cả class trông bình thường.
    [InlineData("eds-date-table__cell normal", false, false)]
    // Ngoài tháng đang hiện ⇒ bấm là sai tháng.
    [InlineData("eds-date-table__cell out-of-month is-empty disabled-prev", true, false)]
    // Ô đệm không mang ngày nào.
    [InlineData("eds-date-table__cell is-empty", true, false)]
    public void LuatNgayDo_ChamDungTungCa(string className, bool coTimeslots, bool doMongDoi)
    {
        var luat = LuatNgayDoThat();
        Assert.Equal(doMongDoi, luat(className, coTimeslots));
    }

    /// <summary>
    /// <c>pageCaiLuatNgayDoFlash</c> phải GỌI luật chung, và KHÔNG được chứa luật ô ngày thứ hai.
    /// <para>Vì sao cần dù đã có ma trận: ma trận chấm KẾT QUẢ. Ai đó chép luật vào đây (đúng thứ đã xảy ra bên
    /// KM) thì ma trận vẫn xanh ngay lúc chép — rồi hai bản trôi lệch dần và shop hỏng im lặng.</para>
    /// </summary>
    [Fact]
    public void LuatNgayDo_PhaiGoiLuatChung_KhongVietBanThuHai()
    {
        var nguon = NguonHam(FileFlash, "pageCaiLuatNgayDoFlash");

        Assert.Contains("_edsONgayHopLe(className)", nguon);
        // Không một token nào của luật ô ngày được lặp lại ở đây — đó là việc của flow-km.js.
        foreach (var token in new[] { "out-of-month", "out-of-range", "prev-month", "next-month", "is-empty", "/disabled/" })
        {
            Assert.DoesNotContain(token, nguon);
        }
        // Chưa cài luật chung thì HỎNG TO TIẾNG, không có bản dự phòng tại chỗ.
        Assert.Contains("throw new Error", nguon);
    }

    /// <summary>Nơi DÙNG luật cũng phải thật sự gọi luật, không tự lọc className.</summary>
    [Theory]
    [InlineData("pageDumpLichFlash")]
    [InlineData("pageClickNgayFlash")]
    public void NoiDungLuat_PhaiGoiFlashNgayDo(string tenHam)
    {
        var nguon = NguonHam(FileFlash, tenHam);

        Assert.Contains("_flashNgayDo(", nguon);
        Assert.DoesNotContain("/disabled/", nguon);
        Assert.DoesNotContain("out-of-month", nguon);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2. LUẬT "chọn ngày nào" — ngày mai, không có thì ngày đỏ gần nhất
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Máy JS đã nạp <c>chonNgayDich</c> thật; trả về <c>soNgay</c> (null nếu không chọn được).</summary>
    private static Func<string, int, int?> LuatChonNgayThat()
    {
        var engine = new Jint.Engine();
        engine.Execute(NguonHam(FileFlash, "chonNgayDich"));
        return (mangJs, ngayMai) =>
        {
            // `ToObject()` cho `null` khi JS trả null/undefined, `double` khi trả số. Ép kiểu CỨNG sang double:
            // luật trả về chuỗi (vd nối nhầm chuỗi thay vì số) thì test nổ ngay chứ không lặng lẽ đi qua.
            var v = engine.Evaluate($"chonNgayDich({mangJs}, {ngayMai}).soNgay").ToObject();
            return v is null ? null : (int)(double)v;
        };
    }

    /// <summary>
    /// Danh sách SỐ NGÀY ĐỎ dạng JS — đúng hình dạng trường <c>lich.ngayDo</c> mà page-func dựng ra và
    /// <c>gonLich</c> đưa vào dump.
    /// <para>Từ 27/08/2026 <c>chonNgayDich</c> nhận thẳng danh sách này thay vì mảng ô thô: hai đường suy ra
    /// "ngày nào đỏ" là mẫu đã cắn nhiều lần trong file này.</para>
    /// </summary>
    private static string MangNgayDo(params int[] ngayDo) => "[" + string.Join(",", ngayDo) + "]";

    /// <summary>
    /// <b>NGÀY MAI thắng tuyệt đối khi nó ĐỎ</b> — kể cả khi có ngày đỏ khác đứng trước nó trong danh sách.
    /// <para>Đây là ca sai âm thầm điển hình: lấy "ngày đỏ đầu danh sách" cũng cho ra một ngày hợp lệ, nhật ký
    /// vẫn báo ok, mà flash sale thì đặt cho một ngày chẳng ai chờ.</para>
    /// </summary>
    [Fact]
    public void ChonNgay_NgayMaiDo_ThiLayDungNgayMai()
    {
        var luat = LuatChonNgayThat();
        Assert.Equal(28, luat(MangNgayDo(3, 9, 28, 30), 28));
    }

    /// <summary>Ngày mai KHÔNG đỏ ⇒ ngày đỏ gần nhất về phía TƯƠNG LAI (không phải nhỏ nhất, không phải đầu danh sách).</summary>
    [Fact]
    public void ChonNgay_NgayMaiKhongDo_ThiLayNgayDoGanNhatPhiaSau()
    {
        var luat = LuatChonNgayThat();
        Assert.Equal(29, luat(MangNgayDo(3, 9, 29, 31), 28));
    }

    /// <summary>Không còn ngày đỏ nào SAU ngày mai ⇒ lùi về ngày đỏ gần nhất phía trước (chỉ dành cho lượt DÒ).</summary>
    [Fact]
    public void ChonNgay_HetNgayDoPhiaSau_ThiLuiVeGanNhatPhiaTruoc()
    {
        var luat = LuatChonNgayThat();
        Assert.Equal(20, luat(MangNgayDo(3, 9, 20), 28));
    }

    /// <summary>Không còn ngày đỏ nào ⇒ <c>null</c>, để bước gọi HỎNG có tên thay vì bấm bừa một ô.</summary>
    [Fact]
    public void ChonNgay_KhongConNgayDo_ThiTraNull()
    {
        var luat = LuatChonNgayThat();
        Assert.Null(luat(MangNgayDo(), 28));
        Assert.Null(luat("[]", 28));
    }

    /// <summary>Phần tử không phải SỐ (ô không đọc được số ngày) bị bỏ qua, không bao giờ được chọn.</summary>
    [Fact]
    public void ChonNgay_PhanTuKhongPhaiSo_ThiBoQua()
    {
        var luat = LuatChonNgayThat();
        Assert.Null(luat("[null,undefined,\"28\",NaN]", 28));
        // Lẫn lộn: chỉ số THẬT được tính.
        Assert.Equal(29, luat("[null,\"28\",29]", 28));
    }

    /// <summary>
    /// <b>CA THẬT vừa hỏng (lượt 12:20:04 ngày 27/08/2026)</b>: Tháng 8/2026 có ngày đỏ 27–31, ngày mai là 28
    /// ⇒ phải trả ĐÚNG 28.
    /// </summary>
    [Fact]
    public void ChonNgay_CaThat_Thang8_NgayMai28_ThiTra28()
    {
        var luat = LuatChonNgayThat();
        Assert.Equal(28, luat(MangNgayDo(27, 28, 29, 30, 31), 28));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2a. BƯỚC 2 "Bấm Tạo" — lỗi thật lượt 10:48 và 11:34 ngày 27/08/2026 (selectorDung: null)
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Máy JS đã nạp <c>_flashNutTao</c> thật; trả <c>(chiSo, nguon)</c> của ứng viên được chọn.</summary>
    private static Func<string, (int ChiSo, string? Nguon)> LuatNutTaoThat()
    {
        var engine = new Jint.Engine();
        engine.Execute("var window = {};");
        engine.Execute(NguonHam(FileFlash, "pageCaiLuatNutTaoFlash"));
        engine.Execute("pageCaiLuatNutTaoFlash();");
        Assert.True(engine.Evaluate("typeof window._flashNutTao").ToObject() as string == "function",
            "pageCaiLuatNutTaoFlash() chạy xong mà window._flashNutTao không phải hàm");

        return mangJs =>
        {
            var goi = $"window._flashNutTao({mangJs})";
            return ((int)(double)engine.Evaluate(goi + ".chiSo").ToObject()!,
                    engine.Evaluate(goi + ".nguon").ToObject() as string);
        };
    }

    /// <summary>Một ứng viên nút dạng JS.</summary>
    private static string Nut(string text, string cls = "", string tag = "BUTTON", string? role = null,
                              params string[] toTienCls)
    {
        var tt = string.Join(",", toTienCls.Select(c => $"{{tag:\"DIV\",cls:{Js(c)},role:null}}"));
        return $"{{text:{Js(text)},tag:{Js(tag)},cls:{Js(cls)},role:{Js(role)},toTien:[{tt}]}}";
    }

    /// <summary>
    /// <b>Ma trận nhận/loại nút "Tạo"</b> — chạy THẬT luật production.
    /// <para>Mỗi dòng là một ca đã hoặc có thể làm bước 2 hỏng câm.</para>
    /// </summary>
    [Fact]
    public void LuatNutTao_MaTranNhanVaLoai()
    {
        var luat = LuatNutTaoThat();

        // ① Nút "Tạo" trong .landing-page-header ⇒ NHẬN, và đi đường ưu tiên.
        var a = luat("[" + Nut("Tạo", "landing-page-btn", toTienCls: new[] { "landing-page-header" }) + "]");
        Assert.Equal(0, a.ChiSo);
        Assert.Contains("landing-page", a.Nguon!);

        // ② "Tạo" nằm trong li[role=tab] ⇒ LOẠI (bấm chỉ đổi bộ lọc, không điều hướng).
        Assert.Equal(-1, luat("[" + Nut("Tạo", "", "BUTTON", "tab") + "]").ChiSo);
        Assert.Equal(-1, luat("[" + Nut("Tạo", "", "LI") + "]").ChiSo);
        Assert.Equal(-1, luat("[" + Nut("Tạo", "", toTienCls: new[] { "eds-tabs__item" }) + "]").ChiSo);
        Assert.Equal(-1, luat("[" + Nut("Tạo", "", toTienCls: new[] { "filter-bar" }) + "]").ChiSo);

        // ③ "Tạo Chương Trình Mới" ⇒ KHÔNG khớp: chữ phải ĐÚNG BẰNG.
        Assert.Equal(-1, luat("[" + Nut("Tạo Chương Trình Mới", "landing-page-btn") + "]").ChiSo);

        // ④ Nút rỗng chữ ⇒ LOẠI.
        Assert.Equal(-1, luat("[" + Nut("", "landing-page-btn") + "]").ChiSo);
        Assert.Equal(-1, luat("[" + Nut("   ", "landing-page-btn") + "]").ChiSo);
    }

    /// <summary>
    /// <b>Không còn <c>.landing-page-*</c> thì VẪN phải tìm được nút</b> — đây chính là ca đã hỏng hai lượt liền.
    /// <para>Lượt 10:26 có landing-page nên bước 2 ok; lượt 10:48 và 11:34 thì không, và bản cũ neo cứng vào
    /// class bố cục nên trả <c>selectorDung: null</c> — không hề có cú bấm nào.</para>
    /// </summary>
    [Fact]
    public void LuatNutTao_KhongConLandingPage_VanTimDuocNutTheoChu()
    {
        var luat = LuatNutTaoThat();
        var kq = luat("[" + Nut("Xuất", "x") + "," + Nut("Tạo", "eds-button eds-button--primary") + "]");

        Assert.Equal(1, kq.ChiSo);
        Assert.Contains("KHÔNG thấy vùng landing-page", kq.Nguon!);
    }

    /// <summary>
    /// Có CẢ tab "Tạo" LẪN nút "Tạo" thật ⇒ phải bỏ tab và lấy nút thật, kể cả khi tab đứng TRƯỚC trong DOM.
    /// <para>Lấy "ứng viên hợp lệ đầu tiên" mà quên loại vùng tab là bấm trúng tab: click không ném, không điều
    /// hướng, và phép đọc lại theo URL sẽ báo "còn ở /list" — đúng triệu chứng đang gặp, nhưng vì lý do khác.</para>
    /// </summary>
    [Fact]
    public void LuatNutTao_CoCaTabLanNutThat_ThiBoTab()
    {
        var luat = LuatNutTaoThat();

        // ⚠ CỐ Ý KHÔNG có landing-page ở đâu cả. Nếu còn, phép ƯU TIÊN landing-page sẽ tự chọn đúng nút và
        // che mất việc bộ loại vùng tab đã bị gỡ — test vẫn xanh trong khi lưới đã thủng (đo được: bỏ bộ loại
        // tab mà bản cũ của test này vẫn PASS).
        var kq = luat("[" + Nut("Tạo", "", "LI") + "," + Nut("Tạo", "eds-button eds-button--primary") + "]");

        Assert.Equal(1, kq.ChiSo);

        // Và khi tab là ứng viên DUY NHẤT thì phải chịu thua, tuyệt đối không bấm bừa vào tab.
        Assert.Equal(-1, luat("[" + Nut("Tạo", "", "LI") + "]").ChiSo);
    }

    /// <summary>
    /// <b>Khớp TOKEN, không khớp chuỗi con:</b> <c>eds-table</c> chứa chữ "tab" nhưng KHÔNG phải vùng tab.
    /// <para>Cùng bài học đã giết 12/12 shop đêm 27/08 ở luật ô ngày (<c>disabled</c> vs <c>disabled-next</c>).
    /// Loại nhầm ở đây là bước 2 hỏng câm trên đúng những trang có bảng danh sách.</para>
    /// </summary>
    [Fact]
    public void LuatNutTao_EdsTable_KhongBiNhamLaVungTab()
    {
        var luat = LuatNutTaoThat();
        Assert.Equal(0, luat("[" + Nut("Tạo", "", toTienCls: new[] { "eds-table__cell" }) + "]").ChiSo);
    }

    /// <summary>Danh sách rỗng / không có ứng viên nào ⇒ <c>-1</c> kèm câu lý do có số đếm, không ném.</summary>
    [Fact]
    public void LuatNutTao_KhongCoUngVien_ThiTraAmMot()
    {
        var luat = LuatNutTaoThat();
        Assert.Equal(-1, luat("[]").ChiSo);
        Assert.Equal(-1, luat("null").ChiSo);
    }

    /// <summary>
    /// <b>Phép ĐỌC LẠI của bước 2: URL đã RỜI trang danh sách chưa.</b>
    /// <para><c>?type=0</c> là trạng thái tab mặc định của trang danh sách — phải cắt query trước khi so, nếu
    /// không thì mọi URL có query đều bị đọc thành "đã rời".</para>
    /// </summary>
    [Theory]
    // Còn ở trang danh sách ⇒ CHƯA qua. Ba dạng đều phải cho cùng câu trả lời.
    [InlineData("https://banhang.shopee.vn/portal/marketing/shop-flash-sale/list?type=0", false)]
    [InlineData("https://banhang.shopee.vn/portal/marketing/shop-flash-sale/list", false)]
    [InlineData("https://banhang.shopee.vn/portal/marketing/shop-flash-sale/list/", false)]
    [InlineData("https://banhang.shopee.vn/portal/marketing/shop-flash-sale/list#tab", false)]
    // Đã sang trang tạo ⇒ QUA.
    [InlineData("https://banhang.shopee.vn/portal/marketing/shop-flash-sale/create", true)]
    [InlineData("https://banhang.shopee.vn/portal/marketing/shop-flash-sale/list/create", true)]
    // ⚠ Bắn đi đâu đó NGOÀI khu flash sale (hết phiên → trang đăng nhập) ⇒ KHÔNG được tính là qua.
    [InlineData("https://banhang.shopee.vn/portal/shop", false)]
    [InlineData("https://banhang.shopee.vn/portal/marketing/list/discount", false)]
    // Đọc URL hỏng (tab chết) ⇒ chưa qua, không đoán.
    [InlineData("", false)]
    [InlineData(null, false)]
    public void DaRoiTrangList_ChamDungTungCa(string? url, bool mongDoi)
    {
        var engine = new Jint.Engine();
        engine.Execute(NguonHam(FileFlash, "daRoiTrangList"));
        Assert.Equal(mongDoi, (bool)engine.Evaluate($"daRoiTrangList({Js(url)})").ToObject()!);
    }

    /// <summary>
    /// Bước 2 phải: cài luật · bấm qua <c>pageBamNutTaoFlash</c> · đọc lại bằng <c>daRoiTrangList</c> ·
    /// và DUMP trang danh sách khi hỏng.
    /// </summary>
    [Fact]
    public void Buoc2_DungLuatNutTao_DocLaiTheoUrl_VaDumpKhiHong()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        Assert.Contains("pageCaiLuatNutTaoFlash", nguon);
        Assert.Contains("pageBamNutTaoFlash", nguon);
        Assert.Contains("daRoiTrangList(sau)", nguon);
        Assert.Contains("kyVong: \"roi-list\"", nguon);
        // Dump chỉ chạy khi HỎNG, và phải thật sự được gọi.
        Assert.Contains("pageDumpTrangDanhSach", nguon);
        Assert.Contains("roi ? null : await chay(pageDumpTrangDanhSach", nguon);
        // Không còn neo cứng vào class bố cục ở bước 2.
        Assert.DoesNotContain(".landing-page-header button.landing-page-btn", nguon);
    }

    /// <summary>
    /// Bản dump chẩn đoán phải mang đủ thứ để lần sau khỏi mất một lượt chạy thật — và phải NÓI RÕ đã cắt bao
    /// nhiêu (bài học <c>slice(0, 12)</c> im lặng của <c>flow-km.js</c>).
    /// </summary>
    [Fact]
    public void DumpTrangDanhSach_MangDuBangChung_VaNoiRoDaCat()
    {
        var nguon = NguonHam(FileFlash, "pageDumpTrangDanhSach");

        Assert.Contains("url: location.href", nguon);
        Assert.Contains("daCat:", nguon);
        Assert.Contains("phanTuBamDuoc", nguon);
        Assert.Contains("toTien", nguon);          // đường tổ tiên rút gọn
        Assert.Contains("data-", nguon);           // thuộc tính data-*
        Assert.Contains("landingPageHeader", nguon);
        Assert.Contains("landingPageBtn", nguon);
        Assert.Contains("bangDanhSach", nguon);
        Assert.Contains("phanTuChuDungBangTao", nguon);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2a-bis. CHỜ THÂN TRANG RENDER — lỗi thật lượt 12:01:31 ngày 27/08/2026 (bước 1 và 2 CÙNG một giây)
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>CHẠY THẬT <c>choThanTrangDanhSach</c></b> trên một chuỗi kết quả đếm dấu hiệu có kịch bản.
    /// <para>Chạy thật, không soi nguồn — đúng bài học phép thử phá H: một <c>return</c> chèn đầu hàm biến
    /// vòng lặp thành mã chết mà mọi cái tên vẫn còn nguyên trong nguồn, và lưới đọc-nguồn vẫn xanh.</para>
    /// </summary>
    private static (bool Thay, int SoNhip, string? Nguon) ChayThatChoThanTrang(params string[] kichBan)
    {
        var engine = new Jint.Engine();
        engine.Execute($"var THAN_TRANG_TRAN_NHIP = {HangSo("THAN_TRANG_TRAN_NHIP")};");
        engine.Execute("var THAN_TRANG_NHIP_MS = 0;");          // không ngủ thật trong test
        engine.Execute("var pageDemDauHieuThanTrang = 'DEM';"); // chỉ là nhãn truyền cho `chay`
        engine.Execute("async function sleep(ms) { return undefined; }");
        // Nap CA luat thuan: vong cho goi `nguonDauHieuThanTrang` de quyet dinh, nen khong nap thi
        // test khong con chay dung duong san xuat.
        engine.Execute(NguonHam(FileFlash, "nguonDauHieuThanTrang"));
        engine.Execute(NguonHam(FileFlash, "choThanTrangDanhSach"));
        engine.Execute("var _kb = [" + string.Join(",", kichBan) + "]; var _i = 0;");
        engine.Execute("async function _chay(fn, args) { var v = _kb[Math.min(_i, _kb.length - 1)]; _i++; return v; }");

        var o = engine.Evaluate("choThanTrangDanhSach(_chay)").UnwrapIfPromise().AsObject();
        return ((bool)o.Get("thay").ToObject()!,
                (int)(double)o.Get("soNhipCho").ToObject()!,
                o.Get("nguonDauHieu").ToObject() as string);
    }

    /// <summary>Kết quả một lượt đếm: CHƯA dấu hiệu nào — cả bốn con số đều 0.</summary>
    private const string ThanTrangChua =
        "{dem:{landingPageHeader:0, dongBangDanhSach:0, khoiRong:0, nutChuTao:0}}";

    /// <summary>
    /// Kết quả một lượt đếm với ĐÚNG MỘT dấu hiệu bật.
    /// <para><b>Mớm CON SỐ ĐẾM, tuyệt đối không mớm sẵn tên dấu hiệu.</b> Bản đầu của lưới này mớm sẵn
    /// <c>nguonDauHieu</c> nên phép CHỌN dấu hiệu không hề được chạy — bỏ 2 trong 4 dấu hiệu mà cả 7 test vẫn
    /// xanh (đo thật 27/08/2026). Nay kịch bản chỉ có các con số, tên dấu hiệu do luật production tự suy.</para>
    /// </summary>
    private static string ThanTrangCo(string truong) =>
        "{dem:{landingPageHeader:0, dongBangDanhSach:0, khoiRong:0, nutChuTao:0, " + truong + ":1}}";

    /// <summary>
    /// <b>CA THẬT CỦA LỖI:</b> thân trang về ở nhịp thứ 3 ⇒ phải CHỜ tới nhịp 3, không chốt ở nhịp 1.
    /// <para>Lượt 12:01:31 chạy bước 2 ngay trong cùng một giây với bước 1 và thấy 0 dấu hiệu nào —
    /// 39 phần tử bấm được đều là breadcrumb + sidebar.</para>
    /// </summary>
    [Fact]
    public void ChoThanTrang_ThanTrangVeMuon_ThiChoToiKhiCo()
    {
        var kq = ChayThatChoThanTrang(ThanTrangChua, ThanTrangChua, ThanTrangCo("landingPageHeader"));

        Assert.True(kq.Thay, "vòng chờ bỏ cuộc dù thân trang đã render ở nhịp 3");
        Assert.Equal(3, kq.SoNhip);
        Assert.Equal("landing-page-header", kq.Nguon);
    }

    /// <summary>
    /// <b>Mỗi dấu hiệu, MỘT MÌNH nó, phải đủ để thoát chờ.</b>
    /// <para>Bốn dấu hiệu phủ cả hai trạng thái trang (shop chưa có chương trình / đã có). Đòi hai dấu hiệu
    /// cùng lúc là treo đủ trần ở đúng trạng thái chỉ có một.</para>
    /// </summary>
    [Theory]
    [InlineData("landingPageHeader", "landing-page-header")]
    [InlineData("dongBangDanhSach", "bảng danh sách có dòng")]
    [InlineData("khoiRong", "khối rỗng 'chưa có chương trình'")]
    [InlineData("nutChuTao", "nút chữ 'Tạo'")]
    public void ChoThanTrang_MoiDauHieuMotMinhDuThoat(string truongDem, string nguonMongDoi)
    {
        var kq = ChayThatChoThanTrang(ThanTrangChua, ThanTrangCo(truongDem));

        Assert.True(kq.Thay, $"dấu hiệu '{truongDem}' một mình KHÔNG đủ để thoát chờ");
        Assert.Equal(2, kq.SoNhip);
        Assert.Equal(nguonMongDoi, kq.Nguon);
    }

    /// <summary>Luật thuần chọn dấu hiệu — chấm thẳng, không qua vòng chờ.</summary>
    [Theory]
    [InlineData("landingPageHeader", "landing-page-header")]
    [InlineData("dongBangDanhSach", "bảng danh sách có dòng")]
    [InlineData("khoiRong", "khối rỗng 'chưa có chương trình'")]
    [InlineData("nutChuTao", "nút chữ 'Tạo'")]
    public void NguonDauHieuThanTrang_MoiDauHieuMotMinh(string truongDem, string nguonMongDoi)
    {
        var engine = new Jint.Engine();
        engine.Execute(NguonHam(FileFlash, "nguonDauHieuThanTrang"));
        var goi = $"nguonDauHieuThanTrang({{{truongDem}:1}})";
        Assert.Equal(nguonMongDoi, engine.Evaluate(goi).ToObject() as string);
    }

    /// <summary>Không con số nào &gt; 0 ⇒ <c>null</c>; đầu vào rác cũng <c>null</c>, không ném.</summary>
    [Fact]
    public void NguonDauHieuThanTrang_KhongCoGi_ThiNull()
    {
        var engine = new Jint.Engine();
        engine.Execute(NguonHam(FileFlash, "nguonDauHieuThanTrang"));
        foreach (var v in new[] { "{}", "null", "undefined",
                                  "{landingPageHeader:0,dongBangDanhSach:0,khoiRong:0,nutChuTao:0}",
                                  "{landingPageHeader:null,nutChuTao:null}" })
        {
            Assert.Null(engine.Evaluate($"nguonDauHieuThanTrang({v})").ToObject());
        }
    }

    /// <summary>Thân trang KHÔNG BAO GIỜ về ⇒ hết trần, <c>thay:false</c> — không treo, không tự nhận là xong.</summary>
    [Fact]
    public void ChoThanTrang_KhongBaoGioRender_ThiHetTran()
    {
        var kq = ChayThatChoThanTrang(ThanTrangChua);

        Assert.False(kq.Thay);
        Assert.Equal(int.Parse(HangSo("THAN_TRANG_TRAN_NHIP")), kq.SoNhip);
        Assert.Null(kq.Nguon);
    }

    /// <summary>Thân trang có SẴN ngay nhịp đầu ⇒ chốt ngay, đường xanh không tốn ngân sách.</summary>
    [Fact]
    public void ChoThanTrang_CoSanNgayNhipDau_ThiChotNgay()
    {
        var kq = ChayThatChoThanTrang(ThanTrangCo("landingPageHeader"));

        Assert.True(kq.Thay);
        Assert.Equal(1, kq.SoNhip);
    }

    /// <summary>
    /// <b>Bước 2 phải CHỜ thân trang trước khi tìm nút, và hết trần thì HỎNG kèm DUMP.</b>
    /// <para>Vẫn dump khi hỏng là thứ đã cho phép chốt nguyên nhân trong một lượt thay vì ba.</para>
    /// </summary>
    [Fact]
    public void Buoc2_PhaiChoThanTrang_TruocKhiTimNut()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        var iCho = nguon.IndexOf("choThanTrangDanhSach(chay)", StringComparison.Ordinal);
        var iTimNut = nguon.IndexOf("pageBamNutTaoFlash", StringComparison.Ordinal);
        Assert.True(iCho >= 0, "bước 2 không còn chờ thân trang");
        Assert.True(iCho < iTimNut, "tìm nút chạy TRƯỚC vòng chờ thân trang — đúng lỗi cũ");
        // Hết trần ⇒ hỏng có tên + vẫn dump.
        Assert.Contains("THÂN TRANG danh sách chưa render sau", nguon);
        Assert.Contains("if (!choThan.thay)", nguon);
        var iHong = nguon.IndexOf("if (!choThan.thay)", StringComparison.Ordinal);
        var iDumpSom = nguon.IndexOf("dumpSom", StringComparison.Ordinal);
        Assert.True(iDumpSom > iHong, "nhánh hỏng vì thân trang không có dump");
        // Bằng chứng chờ phải vào dump.
        Assert.Contains("choThanTrang: choThan", nguon);
    }

    /// <summary>
    /// Sau khi sang trang TẠO cũng phải CHỜ ô khung giờ, không đếm một phát.
    /// <para>Trang tạo render thân bất đồng bộ y như trang danh sách; bước 3 bấm đúng nút này.</para>
    /// </summary>
    [Fact]
    public void Buoc2_SangTrangTao_PhaiChoOKhungGio_KhongDemMotLan()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        Assert.Contains("choHienCoNhip(chay, \"button.time-slot-btn\")", nguon);
        // Bản cũ đếm MỘT phát bằng pageDemPhanTuHien ngay sau khi URL đổi.
        Assert.DoesNotContain("chay(pageDemPhanTuHien, [\"button.time-slot-btn\"])", nguon);
        Assert.Contains("choOKhungThoiGian: choOKhung", nguon);
    }

    /// <summary>
    /// Bước 9 phải CHỜ BẢNG SẢN PHẨM LẮNG trước khi đọc mốc <c>T0</c>.
    /// <para>Bước 8 mới chỉ chờ dòng ĐẦU TIÊN xuất hiện; đọc <c>T0</c> lúc bảng còn chảy là chụp một trạng
    /// thái dở dang, mà <c>T0</c> là mốc gốc của cửa kiểm "đang là giảm dần" sau này.</para>
    /// </summary>
    [Fact]
    public void Buoc9_PhaiChoBangSanPhamLang_TruocKhiDocMoc()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        var iLang = nguon.IndexOf("choBangSanPhamLang(chay)", StringComparison.Ordinal);
        var iMoc = nguon.IndexOf("const M0 = await doMoc(", StringComparison.Ordinal);
        Assert.True(iLang >= 0, "bước 9 không chờ bảng sản phẩm lắng");
        Assert.True(iLang < iMoc, "mốc M0 đọc TRƯỚC vòng chờ lắng");
        Assert.Contains("choBangSanPham: choBang", nguon);
        // Mỗi mốc tự chờ lại — bảng vẽ lại sau MỖI cú bấm sắp xếp. M0 chỉ chờ LẮNG (chưa bấm gì); mốc sau
        // cú bấm phải chờ ĐỔI rồi mới chờ lắng (xem `choBangDoiRoiLang`).
        Assert.Contains("? await choBangSanPhamLang(chay)", nguon);
        Assert.Contains(": await choBangSauBamSapXep(chay, vanTruocBam)", nguon);
    }

    /// <summary>
    /// <c>choBangSanPhamLang</c> phải dùng CHUNG luật so vân tay và CHUNG hằng với <c>choLichLang</c> — hai
    /// vòng chờ cùng hình dạng mà tách hằng riêng là hai bản trôi lệch.
    /// </summary>
    [Fact]
    public void ChoBangSanPhamLang_DungChungLuatVaHangVoiChoLichLang()
    {
        var nguon = NguonHam(FileFlash, "choBangSanPhamLang");

        Assert.Contains("vanTayGiongNhau(", nguon);
        Assert.Contains("LANG_CAN_GIONG", nguon);
        Assert.Contains("LANG_TRAN_NHIP", nguon);
        Assert.Contains("LANG_NHIP_TOI_THIEU_KHI_RONG", nguon);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2a-ter. ĐỔI THÁNG PHẢI ĐÁP XUỐNG ĐÚNG THÁNG — lỗi thật lượt 12:20:04 ngày 27/08/2026 (lùi 12 tháng)
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Máy JS đã nạp hai luật mốc tháng.</summary>
    private static Jint.Engine MayLuatThang()
    {
        var engine = new Jint.Engine();
        engine.Execute(NguonHam(FileFlash, "thangKeTiep"));
        engine.Execute(NguonHam(FileFlash, "dungThangMongDoi"));
        return engine;
    }

    /// <summary>Tháng kế tiếp / liền trước, kể cả khi vắt qua năm.</summary>
    [Theory]
    [InlineData(8, 2026, true, 9, 2026)]
    [InlineData(9, 2026, false, 8, 2026)]
    [InlineData(12, 2026, true, 1, 2027)]
    [InlineData(1, 2027, false, 12, 2026)]
    public void ThangKeTiep_ChamDungTungCa(int thang, int nam, bool tien, int thangMong, int namMong)
    {
        var e = MayLuatThang();
        var goi = $"thangKeTiep({thang}, {nam}, {(tien ? "true" : "false")})";
        Assert.Equal((double)thangMong, e.Evaluate(goi + ".thang").ToObject());
        Assert.Equal((double)namMong, e.Evaluate(goi + ".nam").ToObject());
    }

    /// <summary>
    /// <b>CA GIẾT BẢN CŨ:</b> bấm VỀ từ Tháng 9/2026 mà đáp xuống Tháng 9/<b>2025</b> — lùi 12 tháng.
    /// <para>Bản cũ chỉ hỏi "tháng/năm có KHÁC không" nên nhận luôn, và cả luồng đi tiếp trên một cái lịch
    /// cách đích 11 tháng: bước 5 lật thêm 2 tháng nữa (10/2025, 11/2025), tháng nào cũng 0 ngày đỏ, rồi báo
    /// "không tìm được ngày đỏ nào" trong khi bước 4 vừa đo được 5 ngày đỏ ở Tháng 8/2026.</para>
    /// </summary>
    [Fact]
    public void DungThangMongDoi_CaThat_LuiMuoiHaiThang_PhaiBiLoai()
    {
        var e = MayLuatThang();
        bool Goi(string a, string b, string tien) => (bool)e.Evaluate($"dungThangMongDoi({a}, {b}, {tien})").ToObject()!;

        // Ca thật: 9/2026 --về--> 9/2025 ⇒ SAI.
        Assert.False(Goi("{thang:9,nam:2026}", "{thang:9,nam:2025}", "false"));
        // Đúng đích: 9/2026 --về--> 8/2026.
        Assert.True(Goi("{thang:9,nam:2026}", "{thang:8,nam:2026}", "false"));
        // Đúng đích chiều tiến: 8/2026 --tới--> 9/2026.
        Assert.True(Goi("{thang:8,nam:2026}", "{thang:9,nam:2026}", "true"));
        // Nhảy 2 tháng ⇒ SAI.
        Assert.False(Goi("{thang:8,nam:2026}", "{thang:10,nam:2026}", "true"));
        // Đứng yên ⇒ SAI.
        Assert.False(Goi("{thang:8,nam:2026}", "{thang:8,nam:2026}", "true"));
        // Vắt năm vẫn đúng.
        Assert.True(Goi("{thang:12,nam:2026}", "{thang:1,nam:2027}", "true"));
        Assert.True(Goi("{thang:1,nam:2027}", "{thang:12,nam:2026}", "false"));
        // Thiếu số ⇒ KHÔNG ĐOÁN.
        Assert.False(Goi("{thang:null,nam:2026}", "{thang:8,nam:2026}", "false"));
        Assert.False(Goi("null", "{thang:8,nam:2026}", "false"));
    }

    /// <summary>
    /// <c>choDoiThang</c> phải đòi ĐÚNG tháng mong đợi, không chỉ "đã khác".
    /// </summary>
    [Fact]
    public void ChoDoiThang_PhaiDoiDungThangMongDoi()
    {
        var nguon = NguonHam(FileFlash, "choDoiThang");

        Assert.Contains("dungThangMongDoi(truoc, l, tien)", nguon);
        // "Đã khác" vẫn phải kiểm TRƯỚC, rồi mới hỏi "đúng chưa" — hai câu hỏi khác nhau, giữ cả hai.
        var iKhac = nguon.IndexOf("daDoiThang(truoc, l)", StringComparison.Ordinal);
        var iDung = nguon.IndexOf("dungThangMongDoi(truoc, l, tien)", StringComparison.Ordinal);
        Assert.True(iKhac >= 0 && iDung > iKhac);
        Assert.Contains("dungThang: false", nguon);
    }

    /// <summary>Hai chỗ lật tháng (bước 4 về lại, bước 5 lật tới) đều phải DỪNG khi lạc tháng.</summary>
    [Fact]
    public void Buoc4Va5_LacThang_ThiDungHan()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        // Chiều bấm phải được truyền xuống — thiếu nó thì không có "mong đợi" nào để so.
        Assert.Contains("choDoiThang(chay, lichNay, true)", nguon);
        Assert.Contains("choDoiThang(chay, lichSau, false)", nguon);
        Assert.Contains("choDoiThang(chay, lich, true)", nguon);
        // Bước 4: lạc tháng ⇒ không nhận cái lịch đó làm lichNay.
        Assert.Contains("if (ve.daDoi && !ve.dungThang)", nguon);
        // Bước 5: lạc tháng ⇒ hỏng có tên.
        Assert.Contains("if (!doi.dungThang)", nguon);
        Assert.Contains("LẠC THÁNG", nguon);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2a-quater. MỘT NGUỒN SỰ THẬT cho "ngày nào đỏ" — lưới chống trôi lệch
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>LƯỚI CHỐNG TRÔI LỆCH QUAN TRỌNG NHẤT CỦA VÒNG NÀY.</b>
    ///
    /// <para>Chạy THẬT cả đường ống: bản dump thô của page-func → <c>gonLich</c> (thứ đi vào
    /// <c>anSo</c>/dump) → <c>ngayDoCuaLich</c> (cửa DUY NHẤT bước 5 đọc) → <c>chonNgayDich</c>. Đổi tên
    /// trường ở bất kỳ mắt xích nào là test đỏ NGAY, chứ không âm thầm thành <c>undefined</c> rồi biến thành
    /// "không có ngày đỏ nào".</para>
    ///
    /// <para>Dữ liệu là CA THẬT: Tháng 8/2026 của Alina Store1, ngày đỏ 27–31, ngày mai 28.</para>
    /// </summary>
    [Fact]
    public void NgayDo_DiTuDumpToiBuoc5_KhongDutMatXichNao()
    {
        var engine = new Jint.Engine();
        engine.Execute(NguonHam(FileFlash, "gonLich"));
        engine.Execute(NguonHam(FileFlash, "ngayDoCuaLich"));
        engine.Execute(NguonHam(FileFlash, "chonNgayDich"));

        // Bản dump THÔ đúng khuôn page-func trả về.
        const string tho = "{coLich:true, thang:8, nam:2026, soNgayDo:5, ngayDo:[27,28,29,30,31], "
                         + "slotsCuaNgayDo:[], soO:42, o:[]}";

        // ① Bản GỌN (thứ đi vào dump) phải còn giữ danh sách ngày đỏ.
        var ngayDoTrongDump = engine.Evaluate($"JSON.stringify(gonLich({tho}).ngayDo)").ToObject() as string;
        Assert.Equal("[27,28,29,30,31]", ngayDoTrongDump);

        // ② Cửa đọc phải rút được ĐÚNG danh sách đó ra — từ CẢ bản thô LẪN bản gọn.
        Assert.Equal("[27,28,29,30,31]",
            engine.Evaluate($"JSON.stringify(ngayDoCuaLich({tho}))").ToObject() as string);
        Assert.Equal("[27,28,29,30,31]",
            engine.Evaluate($"JSON.stringify(ngayDoCuaLich(gonLich({tho})))").ToObject() as string);

        // ③ Và bước 5 chọn được đúng ngày mai.
        Assert.Equal(28d, engine.Evaluate($"chonNgayDich(ngayDoCuaLich({tho}), 28).soNgay").ToObject());
        Assert.Equal(28d, engine.Evaluate($"chonNgayDich(ngayDoCuaLich(gonLich({tho})), 28).soNgay").ToObject());
    }

    /// <summary>
    /// Đọc nhầm hình dạng (một đối tượng KHÔNG có <c>ngayDo</c>) ⇒ danh sách rỗng — và đó chính là ca phải
    /// bị lưới trên bắt, nên ở đây chỉ khẳng định hàm không ném và không bịa.
    /// </summary>
    [Fact]
    public void NgayDoCuaLich_KhongCoTruong_ThiRong_KhongNem()
    {
        var engine = new Jint.Engine();
        engine.Execute(NguonHam(FileFlash, "ngayDoCuaLich"));
        foreach (var v in new[] { "null", "undefined", "{}", "{o:[{soNgay:27,do:true}]}", "{ngayDo:null}" })
        {
            Assert.Equal("[]", engine.Evaluate($"JSON.stringify(ngayDoCuaLich({v}))").ToObject() as string);
        }
    }

    /// <summary>
    /// Bước 5 phải đọc ngày đỏ QUA CỬA <c>ngayDoCuaLich</c>, tuyệt đối không mò <c>lich.o</c> nữa.
    /// <para>Đây là lưới rẻ nhất chặn việc ai đó "vá nhanh" bằng cách trả lại mảng ô thô — làm vậy là dựng
    /// lại đúng hai-đường-suy-ra vừa gỡ đi.</para>
    /// </summary>
    [Fact]
    public void Buoc5_DocNgayDoQuaCuaDuyNhat()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        Assert.Contains("chonNgayDich(ngayDoCuaLich(lich), ngayMai)", nguon);
        Assert.Contains("chonNgayDoDauThang(ngayDoCuaLich(lich))", nguon);
        Assert.DoesNotContain("lich.o", nguon);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2a-quinquies. MŨI TÊN THÁNG vs MŨI TÊN NĂM — lỗi thật lượt 12:20/12:47 ngày 27/08/2026 (lùi 12 tháng)
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Máy JS đã nạp luật CHUNG <c>_edsChonMuiTenThang</c> lấy từ <c>flow-km.js</c> — cùng một luật mà cả hai
    /// luồng (KM và Flash Sale) đều gọi.
    /// </summary>
    private static Func<string, bool, int> LuatMuiTenThat()
    {
        var engine = new Jint.Engine();
        engine.Execute("var window = {};");
        engine.Execute(NguonHam(FileKm, "pageCaiLuatMuiTenThang"));
        engine.Execute("pageCaiLuatMuiTenThang();");
        Assert.True(engine.Evaluate("typeof window._edsChonMuiTenThang").ToObject() as string == "function",
            "pageCaiLuatMuiTenThang() chạy xong mà window._edsChonMuiTenThang không phải hàm");

        return (dsJs, tien) =>
            (int)(double)engine.Evaluate($"window._edsChonMuiTenThang({dsJs}, {(tien ? "true" : "false")})").ToObject()!;
    }

    /// <summary>
    /// <b>HEADER THẬT — bốn mũi tên, hai cặp dùng chung class.</b> Thứ tự DOM đúng như người dùng dump:
    /// <c>‹‹ năm · ‹ tháng · [nhãn] · › tháng · ›› năm</c>.
    /// <para>Nhóm cùng chiều đã được page-func lọc sẵn, nên danh sách truyền vào chỉ gồm hai cái cùng chiều.</para>
    /// </summary>
    private const string HeaderLuiThat = "[{chiSo:0,truocNhan:true},{chiSo:1,truocNhan:true}]";   // ‹‹ năm, ‹ tháng
    private const string HeaderTienThat = "[{chiSo:0,truocNhan:false},{chiSo:1,truocNhan:false}]"; // › tháng, ›› năm

    /// <summary>
    /// <b>CA GIẾT BẢN CŨ:</b> chiều LÙI phải lấy mũi tên thứ HAI (sát nhãn = tháng), không phải cái đầu tiên
    /// (mũi tên NĂM).
    /// <para>Bản cũ lấy phần tử đầu tiên ⇒ mỗi cú "lùi một tháng" thật ra lùi 12 tháng. Lịch đang ở Tháng
    /// 9/2026, bấm lùi một cái, đáp xuống Tháng 9/2025.</para>
    /// </summary>
    [Fact]
    public void LuatMuiTen_ChieuLui_PhaiLayCaiSatNhan_KhongPhaiCaiDauTien()
    {
        var luat = LuatMuiTenThat();
        Assert.Equal(1, luat(HeaderLuiThat, false));
    }

    /// <summary>
    /// Chiều TIẾN lấy cái ĐẦU (sát nhãn = tháng). Đây là đường đang chạy THẬT ở luồng KM, nên luật mới phải
    /// cho ra ĐÚNG cái mà bản cũ vẫn chọn — không được đổi hành vi của đường đang sống.
    /// </summary>
    [Fact]
    public void LuatMuiTen_ChieuTien_LayCaiSatNhan_TrungVoiHanhViCu()
    {
        var luat = LuatMuiTenThat();
        Assert.Equal(0, luat(HeaderTienThat, true));
    }

    /// <summary>Picker chỉ có MỘT cặp mũi tên (không có nút năm) ⇒ vẫn chọn đúng ở cả hai chiều.</summary>
    [Fact]
    public void LuatMuiTen_HeaderChiCoHaiMuiTen_VanChonDung()
    {
        var luat = LuatMuiTenThat();
        Assert.Equal(0, luat("[{chiSo:0,truocNhan:true}]", false));
        Assert.Equal(0, luat("[{chiSo:0,truocNhan:false}]", true));
    }

    /// <summary>
    /// KHÔNG xác định được cụm nhãn (<c>truocNhan: undefined</c>) ⇒ vẫn phải ra đúng mũi tên tháng, vì danh
    /// sách đã được lọc CÙNG CHIỀU từ trước: lùi lấy cái cuối, tiến lấy cái đầu.
    /// </summary>
    [Fact]
    public void LuatMuiTen_KhongThayNhan_VanRaDungMuiTenThang()
    {
        var luat = LuatMuiTenThat();
        Assert.Equal(1, luat("[{chiSo:0},{chiSo:1}]", false));
        Assert.Equal(0, luat("[{chiSo:0},{chiSo:1}]", true));
    }

    /// <summary>Danh sách rỗng / rác ⇒ <c>-1</c>, để chỗ gọi hỏng có tên chứ không click bừa.</summary>
    [Fact]
    public void LuatMuiTen_RongHoacRac_ThiTraAmMot()
    {
        var luat = LuatMuiTenThat();
        Assert.Equal(-1, luat("[]", false));
        Assert.Equal(-1, luat("null", true));
        Assert.Equal(-1, luat("[null,{}]", false));
    }

    /// <summary>
    /// <b>CẢ HAI luồng phải gọi CÙNG luật</b> — không luồng nào được giữ bản chép "lấy phần tử đầu tiên".
    /// <para><c>flow-km.js</c> có cùng lỗi tiềm ẩn: nhánh lùi của nó chưa bao giờ chạy thật (luồng KM chỉ đi
    /// tới, hôm nay → +180 ngày) nên lỗi nằm im ở đó.</para>
    /// </summary>
    [Theory]
    [InlineData(FileKm, "pageBamChuyenThang")]
    [InlineData(FileFlash, "pageBamMuiTenThangFlash")]
    public void HaiLuong_DeuGoiLuatMuiTenChung(string tenFile, string tenHam)
    {
        var nguon = NguonHam(tenFile, tenHam);

        Assert.Contains("_edsChonMuiTenThang(ds, tien)", nguon);
        Assert.Contains("eds-picker-header__label", nguon);   // neo theo cụm nhãn
        // Bản cũ: lấy button đầu tiên / phần tử đầu tiên trong danh sách.
        Assert.DoesNotContain("hopLe.find((e) => e.tagName === \"BUTTON\") || hopLe[0]", nguon);
        // Bằng chứng vào dump.
        Assert.Contains("chiSoDaChon", nguon);
        Assert.Contains("soUngVien", nguon);
    }

    /// <summary>Luật phải được CÀI trước khi dùng, ở cả hai luồng.</summary>
    [Theory]
    [InlineData(FileKm, "datOThoiGian")]
    [InlineData(FileFlash, "doDomFlashSaleChoTabDangMo")]
    public void HaiLuong_DeuCaiLuatMuiTenTruocKhiDung(string tenFile, string tenHam)
    {
        Assert.Contains("pageCaiLuatMuiTenThang", NguonHam(tenFile, tenHam));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2a-sexies. CHỜ BẢNG KHUNG GIỜ LẮNG sau khi click ngày — lỗi thật lượt 7 (27/08/2026, soRadio:0 sau 31s)
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>CHẠY THẬT <c>choKhungGioLang</c></b> trên chuỗi kết quả đo có kịch bản.
    /// <para>Chạy thật, không soi nguồn — bài học phép thử phá H/Q/W: lưới đọc-nguồn vẫn xanh khi vòng lặp bị
    /// biến thành mã chết, và lưới mớm sẵn kết quả thì không chạy phép quyết định.</para>
    /// </summary>
    private static (bool Lang, int SoNhip, int SoRadio, bool VanTayDoi) ChayThatChoKhungGioLang(params string[] kichBan)
    {
        var engine = new Jint.Engine();
        foreach (var ten in new[] { "LANG_TRAN_NHIP", "LANG_CAN_GIONG", "LANG_NHIP_TOI_THIEU_KHI_RONG" })
        {
            engine.Execute($"var {ten} = {HangSo(ten)};");
        }
        engine.Execute("var LANG_NHIP_MS = 0;");
        engine.Execute("var pageDemKhungGioFlash = 'DEM';");
        engine.Execute("async function sleep(ms) { return undefined; }");
        engine.Execute(NguonHam(FileFlash, "vanTayGiongNhau"));
        engine.Execute(NguonHam(FileFlash, "choKhungGioLang"));
        engine.Execute("var _kb = [" + string.Join(",", kichBan) + "]; var _i = 0;");
        engine.Execute("async function _chay(fn, args) { var v = _kb[Math.min(_i, _kb.length - 1)]; _i++; return v; }");

        var o = engine.Evaluate("choKhungGioLang(_chay)").UnwrapIfPromise().AsObject();
        return ((bool)o.Get("lang").ToObject()!,
                (int)(double)o.Get("soNhip").ToObject()!,
                (int)(double)o.Get("soRadio").ToObject()!,
                (bool)o.Get("vanTayDoi").ToObject()!);
    }

    /// <summary>Một lượt đo bảng khung giờ: <paramref name="soRadio"/> radio, nhãn theo đó.</summary>
    private static string KhungGio(int soRadio) =>
        "{soRadio:" + soRadio + ", soRadioHien:0, soNhan:" + soRadio + ", van:" + Js(soRadio + "#nhan" + soRadio) + "}";

    /// <summary>
    /// <b>CA THẬT CỦA LỖI:</b> danh sách khung giờ nạp bất đồng bộ, về ở nhịp thứ 3 ⇒ phải CHỜ tới đó rồi mới
    /// đọc, và đọc ra đúng số radio.
    /// </summary>
    [Fact]
    public void ChoKhungGioLang_RadioVeMuon_ThiChoToiKhiLang()
    {
        var kq = ChayThatChoKhungGioLang(KhungGio(0), KhungGio(0), KhungGio(8), KhungGio(8), KhungGio(8));

        Assert.True(kq.Lang, "vòng chờ bỏ cuộc dù bảng đã đứng yên với 8 khung");
        Assert.Equal(8, kq.SoRadio);
        Assert.True(kq.SoNhip >= 5, $"chốt quá sớm ở nhịp {kq.SoNhip} — đó chính là lỗi đọc trước khi khung giờ về");
        Assert.True(kq.VanTayDoi, "vân tay phải ĐỔI giữa lần đo đầu và lần cuối");
    }

    /// <summary>Bảng KHÔNG BAO GIỜ đứng yên ⇒ hết trần, <c>lang:false</c> — bước gọi sẽ hỏng có tên.</summary>
    [Fact]
    public void ChoKhungGioLang_KhongBaoGioDungYen_ThiHetTran()
    {
        var kb = Enumerable.Range(1, 40).Select(KhungGio).ToArray();
        var kq = ChayThatChoKhungGioLang(kb);

        Assert.False(kq.Lang);
        Assert.Equal(int.Parse(HangSo("LANG_TRAN_NHIP")), kq.SoNhip);
    }

    /// <summary>
    /// <b>Ngày THẬT SỰ 0 khung vẫn phải LẮNG</b> — không kết luận ở nhịp đầu, mà phải qua
    /// <c>LANG_NHIP_TOI_THIEU_KHI_RONG</c>: "chưa nạp xong" và "ngày này hết khung" nhìn giống hệt nhau lúc đầu.
    /// </summary>
    [Fact]
    public void ChoKhungGioLang_NgayThatSuKhongCoKhung_VanLang_ChiChoLauHon()
    {
        var kq = ChayThatChoKhungGioLang(KhungGio(0));

        Assert.True(kq.Lang, "0 khung là kết quả HỢP LỆ — vòng chờ không được từ chối nó");
        Assert.Equal(0, kq.SoRadio);
        Assert.Equal(int.Parse(HangSo("LANG_NHIP_TOI_THIEU_KHI_RONG")), kq.SoNhip);
    }

    /// <summary>Có khung và đứng yên ngay ⇒ chốt NHANH — đường xanh phải rẻ.</summary>
    [Fact]
    public void ChoKhungGioLang_CoKhungVaDungYen_ThiChotNgay()
    {
        var kq = ChayThatChoKhungGioLang(KhungGio(8));

        Assert.True(kq.Lang);
        Assert.Equal(int.Parse(HangSo("LANG_CAN_GIONG")), kq.SoNhip);
    }

    /// <summary>
    /// <b>Phép đếm radio phải theo SỰ TỒN TẠI, không theo kích thước.</b>
    /// <para>Đây là nửa thứ hai của lỗi: <c>pageDemPhanTuHien</c> lọc bằng <c>getBoundingClientRect</c>, mà EDS
    /// ẩn hẳn thẻ <c>&lt;input&gt;</c> gốc ⇒ đếm ra 0 vĩnh viễn, chờ 30s vẫn 0 (bước 5 tốn đúng 31 giây).</para>
    /// </summary>
    [Fact]
    public void DemKhungGio_DemTheoSuTonTai_KhongTheoKichThuoc()
    {
        var nguon = NguonHam(FileFlash, "pageDemKhungGioFlash");

        // Con số QUYẾT ĐỊNH lấy thẳng từ querySelectorAll, không qua bộ lọc hiện.
        Assert.Contains("soRadio: inputs.length", nguon);
        // Số "đang hiện" chỉ là bằng chứng đi kèm.
        Assert.Contains("soRadioHien: inputs.filter(hien).length", nguon);
        // Vân tay phải gồm cả NHÃN, không chỉ số đếm — lượt vẽ lại có thể thay ruột mà giữ nguyên số.
        Assert.Contains("van:", nguon);
        Assert.Contains("nhan.slice(0, 3)", nguon);
    }

    /// <summary>
    /// Bước 5 phải CHỜ bảng khung giờ lắng, và tuyệt đối không quay lại <c>choHien</c> cho radio.
    /// </summary>
    [Fact]
    public void Buoc5_ChoKhungGioLang_KhongDungChoHienChoRadio()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        var iClick = nguon.IndexOf("chay(pageClickNgayFlash", StringComparison.Ordinal);
        var iCho = nguon.IndexOf("choKhungGioLang(chay)", StringComparison.Ordinal);
        Assert.True(iCho > iClick, "không chờ bảng khung giờ sau khi click ngày");
        // Bản cũ — đếm radio theo kích thước, luôn ra 0.
        Assert.DoesNotContain("choHien(chay, \"input.eds-radio__input\")", nguon);
        // Bằng chứng vào dump.
        Assert.Contains("choKhungGio: choKhung", nguon);
        Assert.Contains("daLang: choKhung", nguon);
        Assert.Contains("soNhipCho: choKhung", nguon);
        // Chưa lắng ⇒ nói rõ con số KHÔNG ĐÁNG TIN, không lặng lẽ coi là 0.
        Assert.Contains("KHÔNG ĐÁNG TIN", nguon);
    }

    /// <summary>
    /// Ba vòng chờ-lắng của file dùng CHUNG luật so vân tay và CHUNG hằng — tách hằng riêng là ba bản trôi lệch.
    /// </summary>
    [Theory]
    [InlineData("choLichLang")]
    [InlineData("choBangSanPhamLang")]
    [InlineData("choKhungGioLang")]
    public void MoiVongChoLang_DungChungLuatVaHang(string tenHam)
    {
        var nguon = NguonHam(FileFlash, tenHam);

        Assert.Contains("vanTayGiongNhau(", nguon);
        Assert.Contains("LANG_CAN_GIONG", nguon);
        Assert.Contains("LANG_TRAN_NHIP", nguon);
        Assert.Contains("LANG_NHIP_TOI_THIEU_KHI_RONG", nguon);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2a-septies. BẢNG SẢN PHẨM — ba lỗi của lượt 8 (bấm thẻ bao · đếm đôi · Doanh số rỗng) + luật 2 cú bấm
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Máy JS đã nạp ba luật bảng sản phẩm thật.</summary>
    private static Jint.Engine MayLuatBangSanPham()
    {
        var engine = new Jint.Engine();
        engine.Execute("var window = {};");
        engine.Execute(NguonHam(FileFlash, "pageCaiLuatBangSanPham"));
        engine.Execute("pageCaiLuatBangSanPham();");
        return engine;
    }

    /// <summary>
    /// <b>CA GIẾT BẢN CŨ:</b> cụm sắp xếp là <c>&lt;div class="…eds-table__sort-icons"&gt;</c> bao hai
    /// <c>&lt;i class="…eds-table__sort-icon up/down"&gt;</c> ⇒ phải chọn <c>&lt;i&gt;</c>, không chọn thẻ bao.
    /// <para>Lượt 8 bấm trúng thẻ bao nên <c>T0 = T1 = T2</c> — không cú bấm nào được ghi nhận, mà hàm vẫn trả
    /// <c>ok:true</c>. Lưu ý <c>sort-icons</c> (số nhiều, thẻ bao) vs <c>sort-icon</c> (số ít, bấm được):
    /// khớp chuỗi con là dính cả hai, phải khớp TOKEN.</para>
    /// </summary>
    [Fact]
    public void LuatNutSapXep_PhaiChonTheI_KhongChonTheBao()
    {
        var e = MayLuatBangSanPham();
        const string con = "[{tag:\"I\",cls:\"eds-icon eds-table__sort-icon up\"},"
                         + "{tag:\"I\",cls:\"eds-icon eds-table__sort-icon down\"}]";

        Assert.Equal(0d, e.Evaluate($"window._flashChonNutSapXep({con}, \"up\")").ToObject());
        Assert.Equal(1d, e.Evaluate($"window._flashChonNutSapXep({con}, \"down\")").ToObject());

        // Thẻ BAO lọt vào danh sách con (markup đổi) ⇒ vẫn KHÔNG được chọn.
        const string coBao = "[{tag:\"DIV\",cls:\"eds-table__cell-action eds-table__cell-icon eds-table__sort-icons\"},"
                           + "{tag:\"I\",cls:\"eds-icon eds-table__sort-icon down\"}]";
        Assert.Equal(1d, e.Evaluate($"window._flashChonNutSapXep({coBao}, \"down\")").ToObject());

        // ⚠ CA PHÂN BIỆT TOKEN vs CHUỖI CON — ca duy nhất thật sự chấm được phép khớp.
        // Thẻ bao mang CẢ chiều (`sort-icons down`): khớp chuỗi con thì nhận nhầm nó, khớp token thì loại.
        const string baoCoChieu = "[{tag:\"DIV\",cls:\"eds-table__sort-icons down\"},"
                                + "{tag:\"I\",cls:\"eds-icon eds-table__sort-icon down\"}]";
        Assert.Equal(1d, e.Evaluate($"window._flashChonNutSapXep({baoCoChieu}, \"down\")").ToObject());
        // Và khi thẻ bao là ứng viên DUY NHẤT thì phải chịu thua, không bấm bừa vào nó.
        Assert.Equal(-1d, e.Evaluate(
            "window._flashChonNutSapXep([{tag:\"DIV\",cls:\"eds-table__sort-icons down\"}], \"down\")").ToObject());

        // Không có chiều cần tìm ⇒ -1, không đoán bừa.
        Assert.Equal(-1d, e.Evaluate($"window._flashChonNutSapXep({con}, \"asc\")").ToObject());
        Assert.Equal(-1d, e.Evaluate("window._flashChonNutSapXep([], \"down\")").ToObject());
        Assert.Equal(-1d, e.Evaluate("window._flashChonNutSapXep(null, \"down\")").ToObject());
    }

    /// <summary>
    /// <b>Cú bấm phải rơi vào CON của cụm, không vào chính cụm.</b>
    /// <para>Lượt 8 gọi thẳng <c>cum.click()</c> — thẻ bao không có handler nên không cú bấm nào được ghi
    /// nhận, mà hàm vẫn trả <c>ok:true</c>.</para>
    /// </summary>
    [Fact]
    public void BamSapXep_PhaiBamVaoConCuaCum_KhongBamVaoCum()
    {
        var nguon = NguonHam(FileFlash, "pageBamSapXepDoanhSo");

        Assert.Contains("cum.children[i]", nguon);
        Assert.Contains("_flashChonNutSapXep(con, chieu)", nguon);
        // Bản cũ: bấm thẳng vào thẻ bao.
        Assert.DoesNotContain("cum.click()", nguon);
        // Bằng chứng vào dump: class của cụm VÀ của từng con.
        Assert.Contains("clsCum", nguon);
        Assert.Contains("clsCon", nguon);
    }

    /// <summary>
    /// <b>Đếm dòng trong MỘT bảng, không cộng hai bảng chồng.</b>
    /// <para>Số đo lượt 8: <c>soDongMoiTrang: 20</c> trong khi <c>maSanPham</c> chỉ có 10 mã thật + 10
    /// <c>null</c>. Modal có hai bảng (<c>.eds-table__fix-left</c> + bảng chính) dùng chung lớp dòng.</para>
    /// </summary>
    [Fact]
    public void LuatDemDong_MotBangThoi_KhongCongHaiBang()
    {
        var e = MayLuatBangSanPham();

        // Ca thật: bảng trái 10 + bảng chính 10 ⇒ 10, KHÔNG phải 20.
        Assert.Equal(10d, e.Evaluate(
            "window._flashDemDongMotBang([{laBangChinh:false,soDong:10},{laBangChinh:true,soDong:10}])").ToObject());
        // Không biết bảng nào chính ⇒ lấy LỚN NHẤT, vẫn không cộng.
        Assert.Equal(10d, e.Evaluate(
            "window._flashDemDongMotBang([{soDong:10},{soDong:10}])").ToObject());
        Assert.Equal(0d, e.Evaluate("window._flashDemDongMotBang([])").ToObject());
        Assert.Equal(0d, e.Evaluate("window._flashDemDongMotBang(null)").ToObject());
    }

    /// <summary>
    /// Chỉ số cột "Doanh số" tính theo nhãn <c>th</c> CỦA MỘT BẢNG.
    /// <para>Lượt 8 đọc ra <c>[]</c> vì lấy <c>th</c> toàn cục — mà cột đã tách sang bảng chính, và EDS còn
    /// tách header thành <c>&lt;table&gt;</c> riêng nên bảng của <c>th</c> có 0 dòng.</para>
    /// </summary>
    [Fact]
    public void LuatChiSoCot_TinhTheoNhanCuaMotBang()
    {
        var e = MayLuatBangSanPham();

        // Bảng CHÍNH: [Sản phẩm, Doanh số, Giá, Kho] ⇒ cột 1.
        Assert.Equal(1d, e.Evaluate(
            "window._flashChiSoCotDoanhSo([\"Sản phẩm\",\"Doanh số\",\"Giá\",\"Kho\"])").ToObject());
        // Bảng TRÁI không có cột đó ⇒ -1, để chỗ gọi biết mà đổi bảng.
        Assert.Equal(-1d, e.Evaluate("window._flashChiSoCotDoanhSo([\"\",\"Sản phẩm\"])").ToObject());
        Assert.Equal(-1d, e.Evaluate("window._flashChiSoCotDoanhSo([])").ToObject());
        // Khoảng trắng thừa không được làm lệch.
        Assert.Equal(0d, e.Evaluate("window._flashChiSoCotDoanhSo([\"  Doanh số \"])").ToObject());
    }

    /// <summary>
    /// <b>ĐÚNG HAI CÚ BẤM — không bao giờ ba.</b>
    ///
    /// <para>Người dùng (27/08/2026): <i>"bấm 1 lần là up, lần nữa là down, lần nữa là không sắp xếp, như chưa
    /// bấm gì"</i>. Cú thứ ba XOÁ SẠCH sắp xếp ⇒ luồng chọn 10 sản phẩm <b>tuỳ ý</b> thay vì 10 sản phẩm bán
    /// chạy nhất, mà mọi bước sau vẫn <c>ok</c> và nhật ký vẫn xanh.</para>
    ///
    /// <para>Đây là lưới chống đúng ca đó: hằng phải BẰNG 2, và pha sắp xếp thật phải lặp theo hằng chứ không
    /// theo một con số viết cứng.</para>
    /// </summary>
    [Fact]
    public void SapXep_DungHaiCuBam_KhongBaoGioBa()
    {
        Assert.Equal("2", HangSo("SO_LAN_BAM_SAP_XEP"));

        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");
        // Pha sắp xếp thật lặp theo HẰNG, không phải số viết cứng.
        Assert.Contains("for (let i = 0; i < SO_LAN_BAM_SAP_XEP; i++)", nguon);
        Assert.Contains("pageBamSapXepDoanhSo, [\"down\"]", nguon);
    }

    /// <summary>
    /// <b>CẤM mọi cơ chế THỬ LẠI quanh cú bấm sắp xếp.</b>
    /// <para>Một vòng retry bọc ngoài sẽ đẻ ra cú thứ ba mà không ai cố ý — và không ai nhìn thấy, vì cú thứ
    /// ba không hề báo lỗi. Lưới này chặn việc "vá cho chắc" bằng cách bấm lại khi đọc không khớp.</para>
    /// </summary>
    [Fact]
    public void SapXep_KhongCoCoCheThuLaiQuanhCuBam()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");
        var i = nguon.IndexOf("PHA \"SẮP XẾP THẬT\"", StringComparison.Ordinal);
        Assert.True(i >= 0, "mất khối chú thích ràng buộc 2 cú bấm — nó là nguồn duy nhất của luật này");

        // Trong pha sắp xếp thật: đúng MỘT vòng lặp, và nó chạy theo hằng.
        var j = nguon.IndexOf("const bamDo = await chay", StringComparison.Ordinal);
        Assert.True(j > i);
        var pha = nguon.Substring(i, j - i);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(pha, @"\bfor\s*\("));
        Assert.DoesNotContain("while", pha);
        Assert.DoesNotContain("thuLai", pha);
        // Nguyên văn câu người dùng phải còn trong file — nó là nguồn duy nhất của luật.
        Assert.Contains("bấm 1 lần là up, lần nữa là down, lần nữa là không sắp xếp",
            NguonFile(FileFlash));
    }

    /// <summary>
    /// Cú bấm thứ BA chỉ được phép tồn tại như một phép ĐO của lượt dò, và phải nói rõ luồng thật không được
    /// chép.
    /// </summary>
    [Fact]
    public void SapXep_CuThuBa_PhaiDanhDauLaChiDeDo()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        Assert.Contains("CHỈ ĐỂ ĐO", nguon);
        Assert.Contains("LUỒNG THẬT KHÔNG ĐƯỢC CHÉP", nguon);
        // Tách hẳn khỏi vòng lặp sắp xếp thật, và nằm SAU nó.
        var iVong = nguon.IndexOf("for (let i = 0; i < SO_LAN_BAM_SAP_XEP; i++)", StringComparison.Ordinal);
        var iDo = nguon.IndexOf("const bamDo = await chay", StringComparison.Ordinal);
        Assert.True(iVong >= 0 && iDo > iVong, "cú đo không nằm sau vòng lặp sắp xếp thật");
    }

    /// <summary>
    /// <b>Cú thứ BA phải CÙNG CHIỀU <c>down</c> với hai cú trên</b> — nó là cú thứ ba của CÙNG một chu kỳ.
    ///
    /// <para>Kịch bản trước bấm <c>"up"</c> ở đây, nên chu kỳ ba trạng thái mà người dùng mô tả
    /// (<i>"bấm 1 lần là up, lần nữa là down, lần nữa là không sắp xếp, như chưa bấm gì"</i>) <b>chưa hề được
    /// kiểm</b>. Câu hỏi mà mốc M3 tồn tại để trả lời là: <b>M3 có quay về giống hệt M0 không</b> — bấm chiều
    /// ngược thì câu hỏi đó không được hỏi.</para>
    /// </summary>
    [Fact]
    public void SapXep_CuThuBa_PhaiCungChieuDown()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        Assert.Contains("const bamDo = await chay(pageBamSapXepDoanhSo, [\"down\"]);", nguon);
        // Không còn cú bấm chiều ngược nào trong bước đo.
        Assert.DoesNotContain("pageBamSapXepDoanhSo, [\"up\"]", nguon);
        // Và mốc M3 phải nói rõ nó là cú thứ BA.
        Assert.Contains("thứ BA", nguon);
    }

    /// <summary>
    /// <b>Cú đo thứ ba KHÔNG được làm thủng ràng buộc "đúng 2 cú" của luồng thật.</b>
    /// <para>Hai đường phải tách hẳn: vòng lặp chạy theo <c>SO_LAN_BAM_SAP_XEP</c>, còn cú đo là một lệnh
    /// riêng nằm ngoài vòng. Gộp chúng lại (ví dụ nâng hằng lên 3) là luồng thật mất sắp xếp.</para>
    /// </summary>
    [Fact]
    public void SapXep_CuDo_KhongLamThungRangBuocLuongThat()
    {
        Assert.Equal("2", HangSo("SO_LAN_BAM_SAP_XEP"));

        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");
        // Cú đo KHÔNG nằm trong vòng lặp: nó là một câu lệnh `const bamDo = …` ở cấp thân hàm.
        Assert.Contains("const bamDo = await chay", nguon);
        // Và vòng lặp chỉ đẩy vào `bam`, không đẩy vào `bamDo`.
        Assert.Contains("bam.push(await chay(pageBamSapXepDoanhSo, [\"down\"]));", nguon);
        Assert.Contains("soCuBamThat: bam.length", nguon);
    }

    /// <summary>Ba chỗ đếm dòng / đọc cột đều phải đi qua luật chung, không chỗ nào quét cả modal.</summary>
    [Theory]
    [InlineData("pageVanTayModalSanPham")]
    [InlineData("pageDumpModalSanPhamFlash")]
    public void MoiChoDemDong_DiQuaLuatChung(string tenHam)
    {
        var nguon = NguonHam(FileFlash, tenHam);

        Assert.Contains("_flashDemDongMotBang", nguon);
        Assert.Contains("eds-table__fix-left", nguon);
        // Bản cũ: quét thẳng cả modal ⇒ cộng hai bảng chồng.
        Assert.DoesNotContain("modal.querySelectorAll(\"tr.mkt-product-selector-row-in-root\")", nguon);
    }

    /// <summary>Doanh số phải đọc từ BẢNG CHÍNH, cột theo <c>th</c> của chính bảng chứa nó.</summary>
    [Fact]
    public void DocDoanhSo_TuBangChinh_KhongTheoThToanCuc()
    {
        var nguon = NguonHam(FileFlash, "pageDocDoanhSoMoiDongFlash");

        Assert.Contains("_flashChiSoCotDoanhSo(nhanTh)", nguon);
        Assert.Contains("!t.closest(\".eds-table__fix-left\")", nguon);
        // Bản cũ: `document.querySelectorAll("th")` toàn cục.
        Assert.DoesNotContain("document.querySelectorAll(\"th\")", nguon);
        // Bằng chứng cho lượt sau: header có phải <table> riêng không.
        Assert.Contains("headerLaBangRieng", nguon);
    }

    /// <summary>
    /// <b>CHẠY THẬT <c>choBangDoiRoiLang</c></b> trên chuỗi vân tay có kịch bản.
    /// <para>Chạy thật, không soi nguồn và không mớm sẵn kết quả — bài học H/Q/W/AJ.</para>
    /// </summary>
    private static (bool DaDoi, int SoNhipChoDoi, string? VanSau, bool Lang) ChayThatChoBangDoiRoiLang(
        string vanTruocBam, params string[] kichBan)
    {
        var engine = new Jint.Engine();
        foreach (var ten in new[] { "LANG_TRAN_NHIP", "LANG_CAN_GIONG", "LANG_NHIP_TOI_THIEU_KHI_RONG",
                                    "DOI_TRAN_NHIP" })
        {
            engine.Execute($"var {ten} = {HangSo(ten)};");
        }
        engine.Execute("var LANG_NHIP_MS = 0;");
        engine.Execute("var pageVanTayModalSanPham = 'VAN';");
        engine.Execute("async function sleep(ms) { return undefined; }");
        engine.Execute(NguonHam(FileFlash, "vanTayGiongNhau"));
        engine.Execute(NguonHam(FileFlash, "choBangSanPhamLang"));
        engine.Execute(NguonHam(FileFlash, "choBangDoiRoiLang"));
        engine.Execute("var _kb = [" + string.Join(",", kichBan) + "]; var _i = 0;");
        engine.Execute("async function _chay(fn, args) { var v = _kb[Math.min(_i, _kb.length - 1)]; _i++; return v; }");

        var o = engine.Evaluate($"choBangDoiRoiLang(_chay, {Js(vanTruocBam)})").UnwrapIfPromise().AsObject();
        return ((bool)o.Get("daDoi").ToObject()!,
                (int)(double)o.Get("soNhipChoDoi").ToObject()!,
                o.Get("vanTaySauBam").ToObject() as string,
                (bool)o.Get("lang").ToObject()!);
    }

    /// <summary>Một lượt đo vân tay bảng sản phẩm.</summary>
    private static string BanSp(string van) => "{soDong:10, van:" + Js(van) + "}";

    /// <summary>Bảng đổi ở nhịp 3 ⇒ phải chờ tới nhịp 3, và chốt vào ảnh MỚI.</summary>
    [Fact]
    public void ChoBangDoiRoiLang_DoiONhip3_ThiChotAnhMoi()
    {
        var kq = ChayThatChoBangDoiRoiLang("A", BanSp("A"), BanSp("A"), BanSp("B"), BanSp("B"), BanSp("B"), BanSp("B"));

        Assert.True(kq.DaDoi, "không nhận ra bảng đã đổi");
        Assert.Equal(3, kq.SoNhipChoDoi);
        Assert.Equal("B", kq.VanSau);
        Assert.True(kq.Lang);
    }

    /// <summary>
    /// <b>CA TÁI HIỆN ĐÚNG LỖI LƯỢT 10:</b> vân tay đứng yên 2 nhịp rồi mới đổi.
    /// <para>Bản cũ chỉ chờ LẮNG: <c>LANG_CAN_GIONG</c> = 3 nhịp giống nhau ⇒ chốt vào ảnh CŨ ("A") ngay
    /// trước khi Shopee bắt đầu vẽ lại. Bản mới chờ ĐỔI trước nên chốt vào ảnh MỚI ("B").</para>
    /// </summary>
    [Fact]
    public void ChoBangDoiRoiLang_TaiHienLoiTre_ChotAnhMoiChuKhongPhaiAnhCu()
    {
        var kq = ChayThatChoBangDoiRoiLang("A",
            BanSp("A"), BanSp("A"), BanSp("B"), BanSp("B"), BanSp("B"), BanSp("B"), BanSp("B"));

        Assert.True(kq.DaDoi);
        Assert.Equal("B", kq.VanSau);   // ⇐ ảnh MỚI. Bản cũ cho "A".
    }

    /// <summary>
    /// <b>Bảng KHÔNG BAO GIỜ đổi ⇒ <c>daDoi:false</c> nhưng VẪN ĐI TIẾP</b> — tuyệt đối không coi là hỏng.
    /// <para>Ca "đổi mà nhìn giống hệt" là có thật: cú <c>up</c> trên shop toàn 0 doanh số cho ra đúng thứ tự
    /// cũ. Biến nó thành lỗi là chặn oan một kết quả đúng.</para>
    /// </summary>
    [Fact]
    public void ChoBangDoiRoiLang_KhongBaoGioDoi_VanDiTiep_KhongHong()
    {
        var kq = ChayThatChoBangDoiRoiLang("A", BanSp("A"));

        Assert.False(kq.DaDoi);
        Assert.Equal(int.Parse(HangSo("DOI_TRAN_NHIP")), kq.SoNhipChoDoi);
        // Vẫn chờ lắng và vẫn trả về bình thường — không ném, không cờ hỏng.
        Assert.True(kq.Lang);
        Assert.Equal("A", kq.VanSau);
    }

    /// <summary>Đổi ngay nhịp đầu ⇒ chốt ngay, đường xanh phải rẻ.</summary>
    [Fact]
    public void ChoBangDoiRoiLang_DoiNgayNhipDau_ThiChotNgay()
    {
        var kq = ChayThatChoBangDoiRoiLang("A", BanSp("B"));

        Assert.True(kq.DaDoi);
        Assert.Equal(1, kq.SoNhipChoDoi);
    }

    /// <summary>
    /// Bằng chứng "ảnh mới hay ảnh cũ" phải vào dump ở MỌI mốc sau cú bấm — thiếu nó thì lỗi trễ-một-cú-bấm
    /// lại không ai phát hiện được.
    /// </summary>
    [Fact]
    public void Buoc9_MoiMocSauCuBam_PhaiGhiBangChungDaDoi()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        Assert.Contains("daDoi:", nguon);
        Assert.Contains("soNhipChoDoi:", nguon);
        Assert.Contains("vanTayTruocBam:", nguon);
        Assert.Contains("vanTaySauBam:", nguon);
        // Vân tay phải được chụp TRƯỚC mỗi cú bấm — cả hai cú của pha thật lẫn cú đo.
        Assert.Contains("const vanTruocBam = await vanTay();", nguon);
        Assert.Contains("const vanTruocDo = await vanTay();", nguon);
        var iChup = nguon.IndexOf("const vanTruocBam = await vanTay();", StringComparison.Ordinal);
        var iBam = nguon.IndexOf("bam.push(await chay(pageBamSapXepDoanhSo", StringComparison.Ordinal);
        Assert.True(iChup < iBam, "chụp vân tay SAU cú bấm — vô nghĩa, phải chụp trước");
    }


    /// <summary>
    /// <b>CHẠY THẬT <c>choBangSauBamSapXep</c></b> — hai tầng: lớp phủ, rồi chờ-đổi.
    /// <para><c>chay</c> giả DISPATCH theo hàm được truyền: <c>pageDoLopPhuBangSanPham</c> lấy từ hàng đợi
    /// lớp phủ, <c>pageVanTayModalSanPham</c> lấy từ hàng đợi vân tay. Không dispatch thì hai tầng ăn chung
    /// một hàng đợi và test không còn nghĩa gì.</para>
    /// </summary>
    private static (string? Nguon, bool ThayLopPhu, int SoNhipChoHien, string? VanSau, bool Lang)
        ChayThatChoBangSauBamSapXep(string vanTruocBam, string[] lopPhu, string[] vanTay)
    {
        var engine = new Jint.Engine();
        foreach (var ten in new[] { "LANG_TRAN_NHIP", "LANG_CAN_GIONG", "LANG_NHIP_TOI_THIEU_KHI_RONG",
                                    "DOI_TRAN_NHIP", "LOP_PHU_HIEN_TRAN_NHIP", "LOP_PHU_TAT_TRAN_NHIP" })
        {
            engine.Execute($"var {ten} = {HangSo(ten)};");
        }
        engine.Execute("var LANG_NHIP_MS = 0;");
        engine.Execute("var pageVanTayModalSanPham = 'VAN'; var pageDoLopPhuBangSanPham = 'LOP';");
        engine.Execute("async function sleep(ms) { return undefined; }");
        engine.Execute(NguonHam(FileFlash, "vanTayGiongNhau"));
        engine.Execute(NguonHam(FileFlash, "choBangSanPhamLang"));
        engine.Execute(NguonHam(FileFlash, "choBangDoiRoiLang"));
        engine.Execute(NguonHam(FileFlash, "choBangSauBamSapXep"));
        engine.Execute("var _lop = [" + string.Join(",", lopPhu) + "]; var _li = 0;");
        engine.Execute("var _van = [" + string.Join(",", vanTay) + "]; var _vi = 0;");
        engine.Execute(@"async function _chay(fn, args) {
            if (fn === 'LOP') { var a = _lop[Math.min(_li, _lop.length - 1)]; _li++; return a; }
            var b = _van[Math.min(_vi, _van.length - 1)]; _vi++; return b; }");

        var o = engine.Evaluate($"choBangSauBamSapXep(_chay, {Js(vanTruocBam)})").UnwrapIfPromise().AsObject();
        return (o.Get("nguonTinHieu").ToObject() as string,
                (bool)o.Get("thayLopPhu").ToObject()!,
                (int)(double)o.Get("soNhipChoHien").ToObject()!,
                o.Get("vanTaySauBam").ToObject() as string,
                (bool)o.Get("lang").ToObject()!);
    }

    /// <summary>Một lượt đo lớp phủ.</summary>
    private static string LopPhu(bool dangHien) =>
        "{coModal:true, soLopPhuTrongDom:2, soLopPhuTrongModal:1, coTrongModal:true, dangHien:"
        + (dangHien ? "true" : "false") + "}";

    /// <summary>Lớp phủ hiện ở nhịp 2, tắt ở nhịp 6 ⇒ chốt SAU nhịp 6, nguồn tín hiệu là lớp phủ.</summary>
    [Fact]
    public void ChoSauBamSapXep_LopPhuHienRoiTat_ThiChotTheoLopPhu()
    {
        var lop = new[] { LopPhu(false), LopPhu(true), LopPhu(true), LopPhu(true), LopPhu(true),
                          LopPhu(true), LopPhu(false) };
        var kq = ChayThatChoBangSauBamSapXep("A", lop, new[] { BanSp("B") });

        Assert.Equal("lop-phu", kq.Nguon);
        Assert.True(kq.ThayLopPhu);
        Assert.Equal(2, kq.SoNhipChoHien);
        Assert.True(kq.Lang);
    }

    /// <summary>
    /// <b>CA QUYẾT ĐỊNH — vì sao phải thêm lớp phủ.</b>
    ///
    /// <para>Nội dung sau khi sắp xếp <b>TRÙNG</b> nội dung cũ (shop toàn 0 doanh số ⇒ tăng dần cho ra đúng
    /// thứ tự cũ). Cơ chế "chờ đổi" MỘT MÌNH sẽ hết trần rồi phải đoán; có lớp phủ thì vẫn chốt đúng và
    /// <c>nguonTinHieu = "lop-phu"</c>.</para>
    /// </summary>
    [Fact]
    public void ChoSauBamSapXep_NoiDungTrungNhau_VanChotDungNhoLopPhu()
    {
        var lop = new[] { LopPhu(true), LopPhu(true), LopPhu(false) };
        // Vân tay KHÔNG đổi: trước bấm "A", sau bấm vẫn "A".
        var kq = ChayThatChoBangSauBamSapXep("A", lop, new[] { BanSp("A") });

        Assert.Equal("lop-phu", kq.Nguon);
        Assert.True(kq.ThayLopPhu);
        Assert.Equal("A", kq.VanSau);

        // Đối chứng: chỉ có tầng "chờ đổi" thì cùng dữ liệu đó cho ra `daDoi:false` — tức phải đoán.
        var chiChoDoi = ChayThatChoBangDoiRoiLang("A", BanSp("A"));
        Assert.False(chiChoDoi.DaDoi);
    }

    /// <summary>Không bao giờ thấy lớp phủ ⇒ rơi về "chờ đổi", vẫn đi tiếp.</summary>
    [Fact]
    public void ChoSauBamSapXep_KhongThayLopPhu_ThiRoiVeChoDoi()
    {
        var kq = ChayThatChoBangSauBamSapXep("A", new[] { LopPhu(false) }, new[] { BanSp("B") });

        Assert.Equal("cho-doi", kq.Nguon);
        Assert.False(kq.ThayLopPhu);
        Assert.Equal(int.Parse(HangSo("LOP_PHU_HIEN_TRAN_NHIP")), kq.SoNhipChoHien);
        Assert.Equal("B", kq.VanSau);
    }

    /// <summary>Lớp phủ hiện mà KHÔNG BAO GIỜ tắt ⇒ hết trần, ghi rõ, không treo vĩnh viễn.</summary>
    [Fact]
    public void ChoSauBamSapXep_LopPhuKhongTat_ThiHetTran_KhongTreo()
    {
        var kq = ChayThatChoBangSauBamSapXep("A", new[] { LopPhu(true) }, new[] { BanSp("A") });

        Assert.Equal("het-tran", kq.Nguon);
        Assert.True(kq.ThayLopPhu);
    }

    /// <summary>Không lớp phủ VÀ nội dung cũng không đổi ⇒ het-tran; mốc không đáng tin và dump phải nói ra.</summary>
    [Fact]
    public void ChoSauBamSapXep_KhongLopPhuVaKhongDoi_ThiHetTran()
    {
        var kq = ChayThatChoBangSauBamSapXep("A", new[] { LopPhu(false) }, new[] { BanSp("A") });

        Assert.Equal("het-tran", kq.Nguon);
        Assert.False(kq.ThayLopPhu);
    }

    /// <summary>Lớp phủ phải neo TRONG modal sản phẩm — modal khung giờ cũng có một cái.</summary>
    [Fact]
    public void DoLopPhu_PhaiNeoTrongModalSanPham()
    {
        var nguon = NguonHam(FileFlash, "pageDoLopPhuBangSanPham");

        Assert.Contains("product-modal-available-checkbox", nguon);   // neo modal sản phẩm
        Assert.Contains("modal.querySelectorAll(\".eds-table__loading\")", nguon);
        // Và vẫn ghi tổng số trong DOM để thấy có mấy cái.
        Assert.Contains("soLopPhuTrongDom", nguon);
        Assert.Contains("getClientRects()", nguon);                   // "hiện" đo bằng rect, không đoán style
    }

    /// <summary>Bằng chứng lớp phủ phải vào dump ở mọi mốc sau cú bấm.</summary>
    [Fact]
    public void Buoc9_MoiMocSauCuBam_PhaiGhiBangChungLopPhu()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        Assert.Contains("nguonTinHieu:", nguon);
        Assert.Contains("thayLopPhu:", nguon);
        Assert.Contains("soNhipChoHien:", nguon);
        Assert.Contains("soNhipChoTat:", nguon);
        Assert.Contains("soLopPhuTrongDom:", nguon);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2b. LUẬT "số ngày của ô này là bao nhiêu" — lỗi thật lượt 10:27:07 ngày 27/08/2026
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Một chuỗi C# thành literal JS (null ⇒ <c>null</c>) — để dựng lời gọi luật mà không phải
    /// đánh vật với marshalling của Jint.</summary>
    private static string Js(string? s) => s is null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>Máy JS đã nạp <c>_flashSoNgay</c> thật; trả <c>(soNgay, nguon)</c>.</summary>
    private static Func<string?, string?, string?, (int? SoNgay, string Nguon)> LuatSoNgayThat()
    {
        var engine = new Jint.Engine();
        engine.Execute("var window = {};");
        engine.Execute(NguonHam(FileFlash, "pageCaiLuatSoNgayFlash"));
        engine.Execute("pageCaiLuatSoNgayFlash();");
        Assert.True(engine.Evaluate("typeof window._flashSoNgay").ToObject() as string == "function",
            "pageCaiLuatSoNgayFlash() chạy xong mà window._flashSoNgay không phải hàm");

        return (day, slots, dateText) =>
        {
            var goi = $"window._flashSoNgay({Js(day)}, {Js(slots)}, {Js(dateText)})";
            var so = engine.Evaluate(goi + ".soNgay").ToObject();
            var nguon = (string)engine.Evaluate(goi + ".nguon").ToObject()!;
            return (so is null ? null : (int?)(double)so, nguon);
        };
    }

    /// <summary>
    /// Ma trận đọc số ngày — mọi dòng đều là <b>SỐ ĐO THẬT</b> lấy từ <c>logs/flash-dom-20260827-102707.json</c>
    /// (10 ngày đỏ của tháng 9 shop Alina Store1), trừ hai dòng bẫy được dựng để chốt luật.
    /// </summary>
    [Theory]
    // ── Số đo thật: số ngày nằm TRONG khối `timeslots`, dính liền dòng đếm khung ──
    [InlineData("112 khung giờ", "12 khung giờ", null, 1)]
    [InlineData("28 khung giờ", "8 khung giờ", null, 2)]
    [InlineData("918 khung giờ", "18 khung giờ", null, 9)]
    // Ô không còn khung: textContent chỉ có số ngày.
    [InlineData("10", null, null, 10)]
    [InlineData("25", null, null, 25)]
    // ⚠ CA BẪY — CÙNG MỘT CHUỖI, HAI NGHĨA. Regex `^(\d{1,2})` luôn trả 28 nên SAI ở dòng đầu.
    [InlineData("288 khung giờ", "88 khung giờ", null, 2)]
    [InlineData("288 khung giờ", "8 khung giờ", null, 28)]
    // `p.date-text` nếu Shopee có ⇒ đường thẳng nhất, thắng mọi đường sau.
    [InlineData("288 khung giờ", "8 khung giờ", "2", 2)]
    public void LuatSoNgay_ChamDungTungCa(string? day, string? slots, string? dateText, int mongDoi)
    {
        var luat = LuatSoNgayThat();
        Assert.Equal(mongDoi, luat(day, slots, dateText).SoNgay);
    }

    /// <summary>
    /// <b>Ô chỉ có mỗi dòng đếm khung ⇒ KHÔNG có số ngày, và luật phải nói KHÔNG.</b>
    /// <para>Đây là ca mà lưới regex "cụm chữ số đầu" phá hỏng: nó đọc con số ĐẾM KHUNG (7) thành SỐ NGÀY.
    /// Trừ hậu tố xong còn chuỗi rỗng là lối ra CỨNG — không được rơi tiếp xuống regex.</para>
    /// </summary>
    [Fact]
    public void LuatSoNgay_ChiCoDongDemKhung_ThiTraNull_KhongDocNhamSoKhung()
    {
        var luat = LuatSoNgayThat();
        var kq = luat("7 khung giờ", "7 khung giờ", null);

        Assert.Null(kq.SoNgay);
        Assert.Contains("tru-hau-to-slots", kq.Nguon);
    }

    /// <summary>Rỗng / rác ⇒ null, không ném và không bịa số.</summary>
    [Theory]
    [InlineData("", null, null)]
    [InlineData("   ", null, null)]
    [InlineData("Th 2", null, null)]
    [InlineData("99", null, null)]   // ngoài miền 1..31
    public void LuatSoNgay_KhongDocDuoc_ThiTraNull(string? day, string? slots, string? dateText)
    {
        var luat = LuatSoNgayThat();
        Assert.Null(luat(day, slots, dateText).SoNgay);
    }

    /// <summary>
    /// <c>nguon</c> phải TỰ KHAI khi phải mò: đường regex là lưới cuối và có thể đọc nhầm, nên bản dump phải
    /// hét lên chứ không im lặng trả về một con số trông y hệt số đọc chắc.
    /// </summary>
    [Fact]
    public void LuatSoNgay_PhaiMo_ThiTuKhaiKhongChac()
    {
        var luat = LuatSoNgayThat();
        // Không đọc được slots-count (Shopee đổi class) ⇒ chỉ còn cụm chữ số đầu.
        var kq = luat("28 khung giờ", null, null);

        Assert.Equal(28, kq.SoNgay);
        Assert.Contains("KHÔNG CHẮC", kq.Nguon);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2c. LUẬT "tiêu đề tháng" — lỗi thật cùng lượt: đọc ra "2026", mất hẳn "Tháng 8"
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Máy JS đã nạp <c>_flashTieuDeThang</c> thật.</summary>
    private static Func<string, (string? TieuDe, int? Thang, int? Nam)> LuatTieuDeThat()
    {
        var engine = new Jint.Engine();
        engine.Execute("var window = {};");
        engine.Execute(NguonHam(FileFlash, "pageCaiLuatTieuDeThangFlash"));
        engine.Execute("pageCaiLuatTieuDeThangFlash();");

        return mangJs =>
        {
            var goi = $"window._flashTieuDeThang({mangJs})";
            var td = engine.Evaluate(goi + ".tieuDe").ToObject() as string;
            var th = engine.Evaluate(goi + ".thang").ToObject();
            var nm = engine.Evaluate(goi + ".nam").ToObject();
            return (td, th is null ? null : (int?)(double)th, nm is null ? null : (int?)(double)nm);
        };
    }

    /// <summary>
    /// <b>DOM THẬT của lượt 10:27:07:</b> ba <c>span.eds-picker-header__label</c> — <c>"Tháng 8"</c>,
    /// <c>"2026"</c>, và <c>"2020 – 2029"</c> (bảng thập kỷ, <c>display:none</c>).
    /// <para>Bản cũ sắp theo ĐỘ DÀI rồi lấy ngắn nhất ⇒ ra <c>"2026"</c>. Luật mới lọc theo tính HIỆN và giữ
    /// thứ tự DOM.</para>
    /// </summary>
    [Fact]
    public void LuatTieuDe_BaLabelMotCaiAn_ThiRaThangVaNam()
    {
        var luat = LuatTieuDeThat();
        var kq = luat("""[{text:"Tháng 8",hien:true},{text:"2026",hien:true},{text:"2020 – 2029",hien:false}]""");

        Assert.Equal("Tháng 8 2026", kq.TieuDe);
        Assert.Equal(8, kq.Thang);
        Assert.Equal(2026, kq.Nam);
        // Label ẩn KHÔNG được lọt vào tiêu đề — chính nó làm chuỗi bẩn ở flow-km.js.
        Assert.DoesNotContain("2020", kq.TieuDe!);
        Assert.DoesNotContain("2029", kq.TieuDe!);
    }

    /// <summary>Thứ tự DOM đảo lại vẫn phải tách đúng — luật không được dựa vào "label đầu là tháng".</summary>
    [Fact]
    public void LuatTieuDe_DaoThuTuLabel_VanTachDung()
    {
        var luat = LuatTieuDeThat();
        var kq = luat("""[{text:"2026",hien:true},{text:"Tháng 12",hien:true}]""");

        Assert.Equal(12, kq.Thang);
        Assert.Equal(2026, kq.Nam);
    }

    /// <summary>Không label nào hiện ⇒ null hết, để phép so "đã đổi tháng chưa" từ chối đoán.</summary>
    [Fact]
    public void LuatTieuDe_KhongLabelNaoHien_ThiNullHet()
    {
        var luat = LuatTieuDeThat();
        var kq = luat("""[{text:"Tháng 8",hien:false},{text:"2026",hien:false}]""");

        Assert.Null(kq.TieuDe);
        Assert.Null(kq.Thang);
        Assert.Null(kq.Nam);
    }

    /// <summary>
    /// <b><c>daDoiThang</c> phải so CẶP SỐ, không so chuỗi tiêu đề.</b>
    /// <para>Ca giết bản cũ: hai tháng khác nhau mà tiêu đề đọc ra cùng chuỗi <c>"2026"</c> — cờ báo "chưa
    /// đổi" trong khi picker đã chuyển thật, và số ngày đỏ nhảy 0 → 10.</para>
    /// </summary>
    [Fact]
    public void DaDoiThang_SoTheoCapSo_KhongTheoChuoiTieuDe()
    {
        var engine = new Jint.Engine();
        engine.Execute(NguonHam(FileFlash, "daDoiThang"));
        bool Goi(string a, string b) => (bool)engine.Evaluate($"daDoiThang({a}, {b})").ToObject()!;

        // Cùng chuỗi tiêu đề "2026" nhưng KHÁC tháng ⇒ ĐÃ đổi.
        Assert.True(Goi("""{tieuDe:"2026",thang:8,nam:2026}""", """{tieuDe:"2026",thang:9,nam:2026}"""));
        // Cùng tháng ⇒ chưa đổi.
        Assert.False(Goi("""{tieuDe:"Tháng 8 2026",thang:8,nam:2026}""", """{tieuDe:"Tháng 8 2026",thang:8,nam:2026}"""));
        // Sang năm mới.
        Assert.True(Goi("""{thang:12,nam:2026}""", """{thang:1,nam:2027}"""));
        // Thiếu số ⇒ KHÔNG ĐOÁN (trả false để bước gọi hỏng có tên, thay vì đi tiếp trên tháng không rõ).
        Assert.False(Goi("""{tieuDe:"2026",thang:null,nam:2026}""", """{tieuDe:"2026",thang:9,nam:2026}"""));
        Assert.False(Goi("null", """{thang:9,nam:2026}"""));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2d. HẾT NGÀY ĐỎ THÁNG NÀY ⇒ LẬT SANG THÁNG SAU
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Máy JS đã nạp <c>chonNgayDoDauThang</c> (kéo theo <c>chonNgayDich</c> mà nó gọi).</summary>
    private static Func<string, int?> LuatNgayDauThangThat()
    {
        var engine = new Jint.Engine();
        engine.Execute(NguonHam(FileFlash, "chonNgayDich"));
        engine.Execute(NguonHam(FileFlash, "chonNgayDoDauThang"));
        return mangJs =>
        {
            var v = engine.Evaluate($"chonNgayDoDauThang({mangJs}).soNgay").ToObject();
            return v is null ? null : (int?)(double)v;
        };
    }

    /// <summary>
    /// Tháng này rỗng + tháng sau có ngày đỏ ⇒ phải ra ngày của THÁNG SAU, và là ngày đỏ ĐẦU TIÊN của nó.
    /// <para>Dữ liệu đúng của lượt 10:27:07: tháng 8 có <c>soNgayDo = 0</c>, tháng 9 có 10 ngày đỏ bắt đầu từ
    /// ngày 1. Bản trước dừng ở tháng 8 và bỏ cuộc.</para>
    /// </summary>
    [Fact]
    public void ChonNgayDauThang_ThangNayRong_ThangSauCo_ThiLayNgayDauCuaThangSau()
    {
        var dauThang = LuatNgayDauThangThat();
        var trongThang = LuatChonNgayThat();

        // Tháng 8: sạch ngày đỏ ⇒ luật "ngày mai" chịu thua ⇒ đây chính là cửa mở nhánh lật tháng.
        Assert.Null(trongThang(MangNgayDo(), 28));
        // Tháng 9: 10 ngày đỏ 1..9 ⇒ lấy ngày 1.
        Assert.Equal(1, dauThang(MangNgayDo(1, 2, 3, 4, 5, 6, 7, 8, 9)));
    }

    /// <summary>Ngày 1 không đỏ ⇒ vẫn phải lấy ngày đỏ NHỎ NHẤT, không phải bỏ cuộc.</summary>
    [Fact]
    public void ChonNgayDauThang_Ngay1KhongDo_ThiLayNgayDoNhoNhat()
    {
        var dauThang = LuatNgayDauThangThat();
        Assert.Equal(5, dauThang(MangNgayDo(5, 11, 20)));
    }

    /// <summary>Tháng cũng rỗng ⇒ null, để vòng lật tháng đi tiếp hoặc chịu thua có tên.</summary>
    [Fact]
    public void ChonNgayDauThang_ThangRong_ThiTraNull()
    {
        var dauThang = LuatNgayDauThangThat();
        Assert.Null(dauThang(MangNgayDo()));
        Assert.Null(dauThang("[]"));
    }

    /// <summary>
    /// <b>Bước 5 phải CÓ vòng lật sang tháng sau</b>, có trần, và dùng đúng hai luật thuần.
    /// <para>Vì sao cần lưới đọc-nguồn dù đã có test luật: ma trận chấm LUẬT, không chấm việc bước 5 có gọi
    /// luật hay không. Lượt 10:27:07 chết đúng vì bước 5 không có nhánh này — luật thì vẫn đúng.</para>
    /// </summary>
    [Fact]
    public void Buoc5_PhaiLatSangThangSauKhiHetNgayDo()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        Assert.Contains("chonNgayDich(", nguon);
        Assert.Contains("chonNgayDoDauThang(", nguon);
        Assert.Contains("TRAN_THANG_SANG", nguon);
        Assert.Contains("choDoiThang(", nguon);
        // Không còn lối thoát sớm "tháng đang hiện KHÔNG còn ngày đỏ" ngay khi chưa lật tháng nào.
        var iLat = nguon.IndexOf("thangDaSang < TRAN_THANG_SANG", StringComparison.Ordinal);
        var iChiuThua = nguon.IndexOf("không tìm được ngày đỏ nào sau khi đã lật", StringComparison.Ordinal);
        Assert.True(iLat >= 0, "bước 5 không còn vòng lật tháng");
        Assert.True(iChiuThua > iLat, "câu chịu thua nằm TRƯỚC vòng lật tháng — lại bỏ cuộc sớm");
    }

    /// <summary>Cờ "đã đổi tháng" phải đi qua <c>daDoiThang</c>, tuyệt đối không so chuỗi tiêu đề nữa.</summary>
    [Fact]
    public void ChoDoiThang_PhaiDungLuatDaDoiThang_KhongSoChuoi()
    {
        var nguon = NguonHam(FileFlash, "choDoiThang");

        Assert.Contains("daDoiThang(truoc, l)", nguon);
        Assert.DoesNotContain("tieuDe !==", nguon);
        Assert.DoesNotContain("tieuDe ===", nguon);
    }

    /// <summary>Hai chỗ đọc số ngày phải cùng gọi <c>_flashSoNgay</c>, và không chỗ nào còn trò clone-xoá cũ.</summary>
    [Theory]
    [InlineData("pageDumpLichFlash")]
    [InlineData("pageClickNgayFlash")]
    public void NoiDocSoNgay_PhaiGoiLuatChung_KhongTuBocChuoi(string tenHam)
    {
        var nguon = NguonHam(FileFlash, tenHam);

        Assert.Contains("_flashSoNgay(", nguon);
        // Bản cũ: clone ô rồi xoá khối timeslots — số ngày nằm TRONG đó nên mọi ô ra rỗng.
        Assert.DoesNotContain("cloneNode", nguon);
        Assert.DoesNotContain("div.timeslots, div.no-timeslot", nguon);
    }

    /// <summary>Tiêu đề tháng chỉ được đọc qua luật chung, và phải lấy label theo neo đã đo thật.</summary>
    [Fact]
    public void DumpLich_DocTieuDeQuaLuatChung_VaNeoDungLabel()
    {
        var nguon = NguonHam(FileFlash, "pageDumpLichFlash");

        Assert.Contains("_flashTieuDeThang(nhanLabel)", nguon);
        Assert.Contains("span.eds-picker-header__label", nguon);
        // Bản cũ sắp theo độ dài rồi lấy ngắn nhất ⇒ ra "2026".
        Assert.DoesNotContain("a.length - b.length", nguon);
        // Mẫu HTML để lượt sau khỏi phải suy ngược cấu trúc ô ngày từ chuỗi "112 khung giờ".
        Assert.Contains("htmlOMau", nguon);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 2e. CHỜ LỊCH LẮNG — lỗi đã làm cả một phiên chẩn đoán kết luận SAI về shop Alina Store1
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Máy JS đã nạp ba luật thuần của vòng chờ lắng.</summary>
    private static Jint.Engine MayLuatLang()
    {
        var engine = new Jint.Engine();
        engine.Execute(NguonHam(FileFlash, "vanTayLich"));
        engine.Execute(NguonHam(FileFlash, "vanTayGiongNhau"));
        engine.Execute(NguonHam(FileFlash, "ketLuanNgayDo"));
        return engine;
    }

    /// <summary>
    /// Một bản dump lịch dạng JS. <paramref name="coKhung"/> = ô ngày đã nhận được dữ liệu khung giờ hay chưa —
    /// đúng cái đổi giữa lúc modal vừa bung và lúc API trả về.
    /// </summary>
    private static string LichJs(int thang, int nam, bool coKhung) =>
        $$"""
        { coLich:true, thang:{{thang}}, nam:{{nam}}, soNgayDo:{{(coKhung ? 5 : 0)}}, o:[
          { cls:"eds-date-table__cell normal current disabled-next", textDayDu:{{(coKhung ? "\"275 khung giờ\"" : "\"27\"")}} },
          { cls:"eds-date-table__cell normal selected",              textDayDu:{{(coKhung ? "\"288 khung giờ\"" : "\"28\"")}} },
          { cls:"eds-date-table__cell normal",                       textDayDu:{{(coKhung ? "\"298 khung giờ\"" : "\"29\"")}} }
        ]}
        """;

    /// <summary>
    /// <b>Vân tay phải ĐỔI giữa "lưới ngày đã render" và "dữ liệu khung giờ đã về".</b>
    /// <para>Đây là toàn bộ cơ sở của vòng chờ: hai trạng thái ấy phải phân biệt được. DOM thật (người dùng dump
    /// tay 27/08) khác nhau ở cả <c>class</c> lẫn nội dung ô — vân tay gộp cả hai.</para>
    /// </summary>
    [Fact]
    public void VanTay_DoiKhiDuLieuKhungGioVe()
    {
        var e = MayLuatLang();
        var chuaCo = (string)e.Evaluate($"vanTayLich({LichJs(8, 2026, false)})").ToObject()!;
        var daCo = (string)e.Evaluate($"vanTayLich({LichJs(8, 2026, true)})").ToObject()!;

        Assert.NotEqual(chuaCo, daCo);
        Assert.False((bool)e.Evaluate($"vanTayGiongNhau({Js(chuaCo)}, {Js(daCo)})").ToObject()!);
        // Hai lần chụp CÙNG một trạng thái ⇒ giống nhau ⇒ được tính là lắng.
        Assert.True((bool)e.Evaluate($"vanTayGiongNhau({Js(daCo)}, {Js(daCo)})").ToObject()!);
    }

    /// <summary>Đổi tháng cũng phải đổi vân tay — nếu không, vòng chờ lắng sẽ tưởng tháng mới là tháng cũ.</summary>
    [Fact]
    public void VanTay_DoiKhiDoiThang()
    {
        var e = MayLuatLang();
        var t8 = (string)e.Evaluate($"vanTayLich({LichJs(8, 2026, true)})").ToObject()!;
        var t9 = (string)e.Evaluate($"vanTayLich({LichJs(9, 2026, true)})").ToObject()!;
        Assert.NotEqual(t8, t9);
    }

    /// <summary>Chưa đọc được lịch ⇒ vân tay <c>null</c>, và <c>null</c> KHÔNG bao giờ "giống" cái gì — kể cả
    /// một <c>null</c> khác. Không có chốt này thì hai lượt đọc hỏng liên tiếp bị tính là "đã lắng".</summary>
    [Fact]
    public void VanTay_ChuaDocDuoc_ThiNullVaKhongGiongGiCa()
    {
        var e = MayLuatLang();
        Assert.Null(e.Evaluate("vanTayLich(null)").ToObject());
        Assert.Null(e.Evaluate("vanTayLich({coLich:false})").ToObject());
        Assert.False((bool)e.Evaluate("vanTayGiongNhau(null, null)").ToObject()!);
        Assert.False((bool)e.Evaluate("vanTayGiongNhau(undefined, undefined)").ToObject()!);
    }

    /// <summary>
    /// <b>CA GIẾT BẢN CŨ: vân tay còn đổi + 0 ngày đỏ ⇒ KHÔNG được kết luận "hết ngày đỏ".</b>
    /// <para>Bản dump 10:27:07 ghi <c>soNgayDo: 0</c> cho tháng 8 của Alina Store1 mà không một dấu hiệu nào nói
    /// lịch còn đang vẽ dở — nên cả phiên chẩn đoán đọc nó thành "shop hết ngày đỏ tháng 8". Thực tế tháng 8 có
    /// 5 ngày đỏ (27 = "5 khung giờ", 28–31 = "8 khung giờ").</para>
    /// </summary>
    [Fact]
    public void KetLuanNgayDo_ChuaLang_ThiKhongDuocKetLuan()
    {
        var e = MayLuatLang();
        var goi = $"ketLuanNgayDo({LichJs(8, 2026, false)}, false)";

        Assert.False((bool)e.Evaluate(goi + ".chac").ToObject()!);
        var ly = (string)e.Evaluate(goi + ".ly").ToObject()!;
        Assert.Contains("CHƯA LẮNG", ly);
        Assert.Contains("KHÔNG ĐÁNG TIN", ly);
        // Con số vẫn được ghi ra để đọc, nhưng đi kèm cờ `chac:false`.
        Assert.Equal(0d, e.Evaluate(goi + ".soNgayDo").ToObject());
    }

    /// <summary>Chưa lắng thì CÓ ngày đỏ cũng vẫn không chắc — số đọc giữa lượt vẽ lại không đáng tin theo cả
    /// hai chiều, không phải chỉ chiều "0".</summary>
    [Fact]
    public void KetLuanNgayDo_ChuaLang_CoNgayDo_VanKhongChac()
    {
        var e = MayLuatLang();
        Assert.False((bool)e.Evaluate($"ketLuanNgayDo({LichJs(8, 2026, true)}, false).chac").ToObject()!);
    }

    /// <summary>
    /// Ca HỢP LỆ: đã lắng mà vẫn 0 ngày đỏ ⇒ MỚI được kết luận "tháng này thật sự hết ngày đỏ".
    /// <para>Chốt này quan trọng ngang chốt ngược lại: tháng hết khung là kết quả có thật, và cửa chờ lắng
    /// KHÔNG được biến nó thành lỗi vĩnh viễn.</para>
    /// </summary>
    [Fact]
    public void KetLuanNgayDo_DaLang_ThiMoiDuocKetLuan()
    {
        var e = MayLuatLang();

        var rong = $"ketLuanNgayDo({LichJs(8, 2026, false)}, true)";
        Assert.True((bool)e.Evaluate(rong + ".chac").ToObject()!);
        Assert.Contains("THẬT SỰ không còn ngày đỏ", (string)e.Evaluate(rong + ".ly").ToObject()!);

        var co = $"ketLuanNgayDo({LichJs(8, 2026, true)}, true)";
        Assert.True((bool)e.Evaluate(co + ".chac").ToObject()!);
        Assert.Equal(5d, e.Evaluate(co + ".soNgayDo").ToObject());
    }

    /// <summary>Không đọc được lịch ⇒ không chắc, dù có truyền <c>daLang=true</c>.</summary>
    [Fact]
    public void KetLuanNgayDo_KhongDocDuocLich_ThiKhongChac()
    {
        var e = MayLuatLang();
        Assert.False((bool)e.Evaluate("ketLuanNgayDo(null, true).chac").ToObject()!);
        Assert.False((bool)e.Evaluate("ketLuanNgayDo({coLich:false}, true).chac").ToObject()!);
    }

    /// <summary>
    /// <b>CHẠY THẬT <c>choLichLang</c></b> trên một chuỗi bản dump có kịch bản, với <c>chay</c> giả trả lần lượt
    /// từng bản.
    ///
    /// <para><b>Vì sao PHẢI chạy thật chứ không soi nguồn</b> — bài học vừa trả giá ngay trong lượt này: bản
    /// đầu của lưới này chỉ <c>Assert.Contains</c> mấy cái tên trong thân hàm. Phép thử phá "bỏ vòng chờ lắng"
    /// chèn một <c>return</c> ngay đầu hàm, vòng lặp thành mã CHẾT — mọi cái tên vẫn còn nguyên trong nguồn nên
    /// **59/59 test vẫn xanh**. Đúng nghĩa test trang trí: nó khẳng định "có viết vòng chờ", không khẳng định
    /// "vòng chờ có chạy".</para>
    ///
    /// <para><c>chay</c> giả bỏ qua tham số hàm và trả phần tử kế tiếp của kịch bản; hết kịch bản thì lặp lại
    /// phần tử cuối (đứng yên). <c>sleep</c> giả trả ngay nên test không tốn thời gian thật.</para>
    /// </summary>
    /// <returns>(lang, soNhip, vanTayDoi) của lượt chờ.</returns>
    private static (bool Lang, int SoNhip, bool VanTayDoi) ChayThatChoLichLang(params string[] kichBan)
    {
        var engine = new Jint.Engine();
        // Hằng lấy NGUYÊN VĂN từ file production — sửa hằng bên đó là test này đi theo, không phải bản chép.
        foreach (var ten in new[] { "LANG_TRAN_NHIP", "LANG_CAN_GIONG", "LANG_NHIP_TOI_THIEU_KHI_RONG" })
        {
            engine.Execute($"var {ten} = {HangSo(ten)};");
        }
        engine.Execute("var LANG_NHIP_MS = 0;");              // không ngủ thật trong test
        engine.Execute("var pageDumpLichFlash = 'DUMP';");    // chỉ là cái nhãn truyền cho `chay`
        engine.Execute("async function sleep(ms) { return undefined; }");
        engine.Execute(NguonHam(FileFlash, "vanTayLich"));
        engine.Execute(NguonHam(FileFlash, "vanTayGiongNhau"));
        engine.Execute(NguonHam(FileFlash, "choLichLang"));
        engine.Execute("var _kb = [" + string.Join(",", kichBan) + "]; var _i = 0;");
        engine.Execute("async function _chay(fn, args) { var v = _kb[Math.min(_i, _kb.length - 1)]; _i++; return v; }");

        var kq = engine.Evaluate("choLichLang(_chay)").UnwrapIfPromise();
        var o = kq.AsObject();
        return ((bool)o.Get("lang").ToObject()!,
                (int)(double)o.Get("soNhip").ToObject()!,
                (bool)o.Get("vanTayDoi").ToObject()!);
    }

    /// <summary>Giá trị THẬT của một hằng <c>const</c> top-level trong <c>flow-flashsale.js</c>.</summary>
    private static string HangSo(string ten)
    {
        var m = Regex.Match(NguonFile(FileFlash), @"^const\s+" + Regex.Escape(ten) + @"\s*=\s*([0-9]+)\s*;",
            RegexOptions.Multiline);
        Assert.True(m.Success, $"flow-flashsale.js không còn hằng '{ten}'");
        return m.Groups[1].Value;
    }

    /// <summary>
    /// <b>CA THẬT CỦA LỖI:</b> lịch mở ra toàn <c>no-timeslot</c>, vài nhịp sau dữ liệu khung giờ mới về.
    /// Vòng chờ phải đi qua giai đoạn đầu và chỉ chốt khi lưới đã ĐỨNG YÊN với dữ liệu thật.
    /// </summary>
    [Fact]
    public void ChoLichLang_DuLieuKhungGioVeMuon_ThiChoToiKhiLang()
    {
        var chuaCo = LichJs(8, 2026, false);
        var daCo = LichJs(8, 2026, true);

        // 3 nhịp đầu chưa có khung, rồi khung về và đứng yên.
        var kq = ChayThatChoLichLang(chuaCo, chuaCo, chuaCo, daCo, daCo, daCo, daCo);

        Assert.True(kq.Lang, "vòng chờ không chốt được dù lưới đã đứng yên với dữ liệu thật");
        // Phải chốt SAU khi dữ liệu về (nhịp 4 trở đi), không phải ở 3 nhịp đầu.
        Assert.True(kq.SoNhip >= 6, $"chốt quá sớm ở nhịp {kq.SoNhip} — đó chính là lỗi đọc lịch trước khi dữ liệu về");
        Assert.True(kq.VanTayDoi, "vân tay phải ĐỔI giữa lần chụp đầu và lần cuối");
    }

    /// <summary>
    /// <b>Lịch KHÔNG BAO GIỜ đứng yên ⇒ hết trần, trả <c>lang:false</c></b> — không treo, không tự nhận là lắng.
    /// </summary>
    [Fact]
    public void ChoLichLang_KhongBaoGioDungYen_ThiHetTranVaBaoChuaLang()
    {
        // Kịch bản đổi qua đổi lại mãi: mỗi nhịp một vân tay khác.
        var kb = Enumerable.Range(1, 40).Select(i => LichJs(8, 2000 + i, true)).ToArray();
        var kq = ChayThatChoLichLang(kb);

        Assert.False(kq.Lang);
        Assert.Equal(int.Parse(HangSo("LANG_TRAN_NHIP")), kq.SoNhip);
    }

    /// <summary>
    /// <b>Tháng THẬT SỰ hết khung vẫn phải LẮNG — cửa không được là "phải có ít nhất một ngày đỏ".</b>
    /// <para>Một cửa như vậy sẽ treo đủ trần rồi báo sai đúng cho tháng hết khung, tức đổi một lỗi đọc sớm lấy
    /// một lỗi khác. Ca 0 ngày đỏ chỉ bị đòi KIÊN NHẪN hơn: phải qua <c>LANG_NHIP_TOI_THIEU_KHI_RONG</c> nhịp.</para>
    /// </summary>
    [Fact]
    public void ChoLichLang_ThangHetKhungThat_VanPhaiLang_ChiChoLauHon()
    {
        var rong = LichJs(8, 2026, false);
        var kq = ChayThatChoLichLang(rong);   // đứng yên ở trạng thái rỗng suốt

        Assert.True(kq.Lang, "tháng hết khung là kết quả HỢP LỆ — vòng chờ không được từ chối nó");
        Assert.Equal(int.Parse(HangSo("LANG_NHIP_TOI_THIEU_KHI_RONG")), kq.SoNhip);
        Assert.False(kq.VanTayDoi);
    }

    /// <summary>
    /// Tháng CÓ ngày đỏ và đứng yên ngay ⇒ chốt NHANH (đúng <c>LANG_CAN_GIONG</c> nhịp), không phải chờ hết
    /// ngân sách của ca rỗng. Đây là đường xanh, và nó phải rẻ.
    /// </summary>
    [Fact]
    public void ChoLichLang_CoNgayDoVaDungYen_ThiChotNgay()
    {
        var kq = ChayThatChoLichLang(LichJs(8, 2026, true));

        Assert.True(kq.Lang);
        Assert.Equal(int.Parse(HangSo("LANG_CAN_GIONG")), kq.SoNhip);
    }

    /// <summary>Đọc lịch hỏng liên tiếp KHÔNG được tính là "đã lắng" — hai <c>null</c> không giống nhau.</summary>
    [Fact]
    public void ChoLichLang_DocHongLienTiep_ThiKhongTinhLaLang()
    {
        var kq = ChayThatChoLichLang("null");

        Assert.False(kq.Lang);
        Assert.Equal(int.Parse(HangSo("LANG_TRAN_NHIP")), kq.SoNhip);
    }

    /// <summary>
    /// <b>Bước 4 phải CHỜ LẮNG trước khi dump, và ghi bằng chứng đã chờ.</b>
    /// <para>Bản cũ gọi thẳng <c>pageDumpLichFlash</c> ngay sau khi modal mở — đúng cú đó ra <c>soNgayDo: 0</c>
    /// cho một tháng có 5 ngày đỏ.</para>
    /// </summary>
    [Fact]
    public void Buoc4_PhaiChoLangTruocKhiDump_VaGhiBangChung()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        Assert.Contains("choLichLang(chay)", nguon);
        // Ba trường bằng chứng bạn cần khi đọc dump.
        Assert.Contains("daLang:", nguon);
        Assert.Contains("soNhipCho:", nguon);
        Assert.Contains("vanTayDoi:", nguon);
        // Và con số phải mang cờ đáng-tin.
        Assert.Contains("chac:", nguon);
        Assert.Contains("ketLuan:", nguon);
    }

    /// <summary>
    /// <b>Bước 5 KHÔNG được lật tháng khi lịch chưa lắng.</b>
    /// <para>Thấy 0 ngày đỏ trên một lịch đang vẽ dở rồi lật sang tháng sau là đi tiếp trên tiền đề sai — đúng
    /// chuỗi suy luận đã dẫn tới kết luận sai về Alina Store1. Cửa chặn phải nằm TRƯỚC vòng lật.</para>
    /// </summary>
    [Fact]
    public void Buoc5_ChuaLang_ThiDungHan_KhongLatThang()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        var iCua = nguon.IndexOf("if (!ketLuan.chac)", StringComparison.Ordinal);
        var iVongLat = nguon.IndexOf("thangDaSang < TRAN_THANG_SANG", StringComparison.Ordinal);
        Assert.True(iCua >= 0, "bước 5 không còn cửa chặn 'chưa lắng'");
        Assert.True(iCua < iVongLat, "cửa chặn 'chưa lắng' nằm SAU vòng lật tháng — vẫn quyết trên dữ liệu rác");
        Assert.Contains("không lật tháng và không chọn ngày nào", nguon);
        // Cửa chặn áp cho MỌI tháng lật tới, không riêng tháng đầu.
        Assert.Contains("if (!ketLuanMoi.chac)", nguon);
    }

    /// <summary>Sau khi đổi tháng cũng phải chờ tháng MỚI lắng — nếu không thì chỉ dời cùng lỗi sang tháng khác.</summary>
    [Fact]
    public void ChoDoiThang_DoiXongPhaiChoLangTiep()
    {
        var nguon = NguonHam(FileFlash, "choDoiThang");

        Assert.Contains("daDoiThang(truoc, l)", nguon);
        Assert.Contains("choLichLang(chay)", nguon);
        var iDoi = nguon.IndexOf("daDoiThang(truoc, l)", StringComparison.Ordinal);
        var iLang = nguon.IndexOf("choLichLang(chay)", StringComparison.Ordinal);
        Assert.True(iLang > iDoi, "chờ lắng phải chạy SAU khi xác nhận đã đổi tháng");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // 3. RÀNG BUỘC "KHÔNG TẠO GÌ" — whitelist, không phải comment
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Danh sách ĐẦY ĐỦ các hàm được phép bấm chuột trong lượt dò.</b> Mọc thêm một cái tên là có một đường
    /// bấm mới mà chưa ai xét — mà đúng thứ phải chặn ở file này là một đường bấm mới trỏ vào nút TẠO THẬT.
    /// </summary>
    private static readonly string[] HamDuocBamChuot =
    {
        // ── Dùng CHUNG cho cả lượt dò lẫn luồng thật ────────────────────────────────────────────────────
        "pageBamNutTaoFlash",        // nút "Tạo" trên trang danh sách — MỞ form, chưa tạo gì
        "pageBamMuiTenThangFlash",   // mũi tên đổi tháng trên lịch
        "pageClickNgayFlash",        // ô ngày (chỉ ô ĐỎ)
        "pageTickKhungFlash",        // radio khung giờ
        "pageBamXacNhanKhungFlash",  // Xác nhận CỦA MODAL KHUNG GIỜ — chỉ điền vào form
        "pageBamSapXepDoanhSo",      // mũi tên sắp xếp cột Doanh số
        // ── CHỈ lượt DÒ ────────────────────────────────────────────────────────────────────────────────
        "pageHuyModalSanPham",       // HỦY — lối ra của lượt dò
        // ── CHỈ LUỒNG THẬT (28/08/2026) ────────────────────────────────────────────────────────────────
        "pageTickChonTatCaFlash",    // ô "chọn tất cả" ở header bảng TRÁI (KHÔNG phải bộ lọc "sp có sẵn")
        "pageSangTrangSauFlash",     // nút trang sau của pager modal sản phẩm
        // ⚠ Xác nhận FOOTER MODAL SẢN PHẨM (30/08/2026) KHÔNG còn `.click()` trong trang: nó đổi thành
        // `pageToaDoNutXacNhanModalSp` (chỉ TRẢ TOẠ ĐỘ) + cú chuột thật `trustedClick` qua chrome.debugger.
        // Đường debugger nằm NGOÀI tầm phép đo whitelist này — lưới cho nó là
        // FlashSaleLuongThatTests.TrustedClick_ChiMotDuong_VaChiNhamXacNhanModalSp.
        // pageToaDoNutBatHangLoat (nút "Bật" hàng loạt) KHÔNG còn click trong trang — toạ độ + trustedClick
        // (đo 14:50 31/08: nút thật, rect thật, click DOM thường vẫn câm). Lưới: TrustedClick_ChiCacDuongCoTen.
        // (17:5x 31/08 — quy trình tay người dùng chốt: KHÔNG tick phân loại / KHÔNG bấm nút Bật / KHÔNG
        // dialog chọn ảnh — cả cụm đó là đi nhầm cửa, kích hoạt do modal "Kích hoạt hàng loạt" ở K*.7 lo.
        // pageTickChonTatCaPhanLoaiFlash / pageToaDoNutBatHangLoat / pageXacNhanDialogChonAnh đã XOÁ.)
        // ⚠ Hai hàm DỌN (pageToaDoNutXoaChuongTrinhRong + pageToaDoNutXoaTrongDialog) KHÔNG còn `.click()`:
        // đổi sang trả TOẠ ĐỘ + trustedClick qua chrome.debugger (14:42 31/08 — `.click()` DOM vào link Xóa
        // bị nuốt). Điều kiện kép vẫn khoá trong nguồn hàm: CHỈ hàng "Bật Flash Sale 0" + "Sắp diễn ra" +
        // đúng ngày đích. Lưới đường debugger: TrustedClick_ChiCacDuongCoTen.
        "pageBamQuayLaiDanhSach",    // "Quay lại trang Danh sách" trong dialog "Tạo thành công" — chỉ điều hướng
        // ⚠ THANH ĐÁY — NÚT TẠO THẬT (pageToaDoNutTaoThat) KHÔNG còn click trong trang (31/08): toạ độ +
        // trustedClick, click DOM thường vào DIV này câm suốt 29–30/08. Chốt neo thanh đáy + từ chối modal
        // vẫn khoá trong NutTaoThat_ChiMotDuong_VaNeoDungThanhDay; đường debugger: TrustedClick_ChiCacDuongCoTen.
    };

    [Fact]
    public void MoiHamBamChuot_PhaiNamTrongWhitelist()
    {
        var nguon = NguonFile(FileFlash);
        var thay = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var fd in MoiHamTopLevel(Cay(FileFlash)))
        {
            if (nguon[fd.Range.Start..fd.Range.End].Contains(".click()", StringComparison.Ordinal))
            {
                thay.Add(fd.Id!.Name);
            }
        }

        Assert.Equal(
            string.Join(", ", HamDuocBamChuot.OrderBy(x => x, StringComparer.Ordinal)),
            string.Join(", ", thay));
    }

    /// <summary>
    /// <b>Không một đường nào bấm nút "Xác nhận" TẠO THẬT.</b> Đường duy nhất bấm-theo-CHỮ trong file này là
    /// <c>pageClickTheoSelector</c> (mượn của <c>flow-km.js</c>), nên chốt đặt đúng ở đó: mọi chữ dùng để lọc
    /// nút phải nằm trong whitelist.
    /// <para>Lấy bằng AST chứ không regex: <c>chuCanCo</c> là đối số THỨ HAI của mảng tham số, và một bản vá
    /// vô ý đổi thứ tự thì regex vẫn khớp còn ý nghĩa đã khác.</para>
    /// </summary>
    [Fact]
    public void KhongCoDuongNaoBamNutTaoThat()
    {
        var chu = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var nut in MoiNut(Cay(FileFlash)))
        {
            if (nut is not CallExpression goi
                || goi.Callee is not Identifier { Name: "chay" }
                || goi.Arguments.Count < 2
                || goi.Arguments[0] is not Identifier { Name: "pageClickTheoSelector" }
                || goi.Arguments[1] is not ArrayExpression mang
                || mang.Elements.Count < 2)
            {
                continue;
            }
            if (mang.Elements[1] is Literal { Value: string s })
            {
                chu.Add(s);
            }
        }

        // Từ 27/08/2026 bước 2 đã chuyển sang `pageBamNutTaoFlash` (neo theo chữ qua luật `_flashNutTao`)
        // nên không còn đi qua `pageClickTheoSelector`; lưới cho đường đó nằm ở
        // {@link LuatNutTao_ChiNhanChuDungBangTao}. 31/08/2026 thêm "cập nhật hàng loạt": bước F2 áp %GIẢM
        // cho form flash MỚI — nút này chỉ áp giá vào bảng, không tạo gì (đúng nút km bấm hằng đêm).
        // Cộng lại vẫn phủ kín mọi đường bấm-theo-chữ.
        Assert.Equal("cập nhật hàng loạt, thêm sản phẩm", string.Join(", ", chu));
        Assert.DoesNotContain(chu, s => s.Contains("xác nhận", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(chu, s => s.Contains("confirm", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <b>Đường bấm-theo-chữ THỨ HAI (luật <c>_flashNutTao</c>) cũng phải bị khoá.</b>
    /// <para>Bước 2 nay tự tìm nút theo chữ thay vì qua <c>pageClickTheoSelector</c>, nên whitelist
    /// <c>chuCanCo</c> ở trên KHÔNG còn nhìn thấy nó. Không có lưới này thì việc vá bước 2 đã lặng lẽ mở một
    /// lỗ trong đúng ràng buộc "KHÔNG TẠO GÌ": một luật khớp "chứa" sẽ ăn cả nút TẠO THẬT ở cuối luồng.</para>
    /// </summary>
    [Fact]
    public void LuatNutTao_ChiNhanChuDungBangTao()
    {
        var nguon = NguonHam(FileFlash, "pageCaiLuatNutTaoFlash");

        // So BẰNG, không so "chứa" / không regex.
        Assert.Contains("na(u.text) !== \"tạo\"", nguon);
        Assert.DoesNotContain("indexOf(\"tạo\")", nguon);
        Assert.DoesNotContain("xác nhận", nguon);
        Assert.DoesNotContain("confirm", nguon);
    }

    /// <summary>
    /// Nút "Xác nhận" của MODAL KHUNG GIỜ được phép bấm, nhưng phải giữ CẢ HAI chốt.
    /// <para>Lý do: lớp <c>confirm-btn</c> DÙNG CHUNG với nút Xác nhận ở thanh đáy trang, mà nút thanh đáy là
    /// nút TẠO CHƯƠNG TRÌNH THẬT (<c>flow-km.js</c> bước 9 bấm đúng nó).</para>
    /// </summary>
    [Fact]
    public void XacNhanKhungGio_PhaiNeoModal_VaTuChoiNutThanhDay()
    {
        var nguon = NguonHam(FileFlash, "pageBamXacNhanKhungFlash");

        // Chốt (1): neo cứng vào modal khung giờ — bảng ngày + radio.
        Assert.Contains(".eds-date-table__cell", nguon);
        Assert.Contains("input.eds-radio__input", nguon);
        // Chốt (2): từ chối khi nút tìm được nằm trong thanh đáy.
        Assert.Contains("closest(\".fix-container\")", nguon);
        var iChot = nguon.IndexOf("closest(\".fix-container\")", StringComparison.Ordinal);
        var iBam = nguon.IndexOf("nut.click()", StringComparison.Ordinal);
        Assert.True(iBam > iChot, "cú bấm nằm TRƯỚC chốt từ chối thanh đáy — chốt vô nghĩa");
    }

    /// <summary>
    /// Lượt dò phải kết thúc bằng <b>HỦY</b>, và đó phải là bước CUỐI CÙNG.
    /// <para>Bước 10 là toàn bộ khác biệt giữa lượt dò này và nút "Dò DOM KM" (nút KM đi tiếp tới nút tạo thật).
    /// Mọc thêm bước 11 nghĩa là có thao tác SAU khi đã hủy — không có lý do hợp lệ nào cho việc đó.</para>
    /// </summary>
    [Fact]
    public void BuocCuoiCung_PhaiLaHuyModalSanPham()
    {
        var nguon = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");
        var nhan = Regex.Matches(nguon, "buoc\\(ds,\\s*\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(10, nhan.Count);
        Assert.StartsWith("1. ", nhan[0]);
        Assert.StartsWith("10. ", nhan[^1]);
        Assert.Contains("HỦY", nhan[^1]);
        Assert.Contains("pageHuyModalSanPham", nguon);
    }

    /// <summary>
    /// Hàm HỦY chỉ được nhận nút có chữ đúng bằng "hủy"/"huỷ" (hoặc nút đóng ×) — TUYỆT ĐỐI không rơi xuống
    /// "nút footer đầu tiên": nút cạnh Hủy chính là "Xác nhận".
    /// </summary>
    [Fact]
    public void HuyModal_ChiNhanDungNutHuy()
    {
        var nguon = NguonHam(FileFlash, "pageHuyModalSanPham");

        Assert.Contains("=== \"hủy\"", nguon);
        Assert.Contains(".eds-modal__close", nguon);
        // Không có nhánh "lấy nút đầu tiên" nào.
        Assert.DoesNotContain("querySelectorAll(\"button\")[0]", nguon);
        Assert.DoesNotContain("xác nhận", nguon);
    }

    /// <summary>
    /// Gói kết quả đi về <c>kind:"flashDump"</c> RIÊNG, không dùng chung <c>kmDump</c>.
    /// <para>Hai chặng có hạn chờ khác nhau (180s so với 1800s) và một gói <c>kmDump</c> tới muộn của lượt dò KM
    /// trước không được phép tính thành kết quả của lượt dò Flash Sale đang chờ.</para>
    /// </summary>
    [Fact]
    public void GoiKetQua_DiVeKindRieng()
    {
        var nguon = NguonHam(FileFlash, "doDumpFlashSale");

        Assert.Contains("kind: \"flashDump\"", nguon);
        Assert.DoesNotContain("kmDump", nguon);
        Assert.DoesNotContain("kmKetQua", nguon);
    }

    /// <summary>
    /// Cửa kiểm sắp xếp KHÔNG được đọc một giá trị Doanh số nào để ra quyết định (plan §3.1): trong dump 27/08
    /// cả 10 dòng đều bằng 0, nên phép so số ĐẠT bất kể mã làm gì — proof đo RỖNG. Doanh số chỉ được đi vào
    /// NHẬT KÝ, và ở lượt dò này thì "cửa kiểm" chưa tồn tại: ba mốc T0/T1/T2 mới chỉ được GHI LẠI.
    /// </summary>
    [Fact]
    public void DoanhSo_ChiDiVaoNhatKy_KhongThamGiaQuyetDinh()
    {
        var nguon = NguonHam(FileFlash, "pageDocDoanhSoMoiDongFlash");

        // Hàm đọc doanh số KHÔNG được chứa phép so sánh/sắp xếp nào — nó chỉ đọc và trả về.
        Assert.DoesNotContain(".sort(", nguon);
        Assert.DoesNotContain(">=", nguon);
        Assert.DoesNotContain("<=", nguon);
        // Và BỐN mốc phải được ghi lại đầy đủ, mỗi mốc đủ ba loại bằng chứng.
        var chay = NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");
        Assert.Contains("M0, M1, M2, M3", chay);
        Assert.Contains("clsCum", chay);
        Assert.Contains("clsIcon", chay);
        Assert.Contains("ma: ma ? ma.ma : null", chay);   // TRỌNG TÀI: thứ tự mã sản phẩm
    }
}
