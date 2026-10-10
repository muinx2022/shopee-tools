using Acornima;
using Acornima.Ast;
using Jint.Native;

namespace Shopee.Core.Tests;

/// <summary>
/// Luật <b>"ô ngày EDS này có chọn được không"</b> của <c>extensions/shopee-orders/flow-km.js</c>, chạy THẬT.
///
/// <para><b>Lỗi mà lưới này canh</b> — đêm 27/08/2026, 12/12 shop hỏng ở lượt KM tự động. Bộ lọc ô ngày dùng
/// <c>/disabled/i.test(className)</c>: khớp CHUỖI CON nên loại nhầm cả <c>disabled-next</c> và
/// <c>disabled-prev</c>. Hai class đó KHÔNG có nghĩa "ô này bị vô hiệu" — chúng đánh dấu "ô KẾ BÊN bị vô hiệu"
/// (EDS bo góc dải chọn được), nên ô ở BIÊN của dải luôn mang một trong hai và luôn bị loại oan. Bước 5 muốn
/// 23-02-2027 (đúng mốc 180 ngày ⇒ ô CUỐI dải) và báo "không thấy ô ngày '23'". Đối chứng: <c>flow-combo.js</c>
/// dùng <c>classList.contains("disabled")</c> và đặt được đúng ngày đó trên 9/12 shop đêm trước.</para>
///
/// <para><b>Vì sao lái được luật từ C#</b>: page-func bị <c>chrome.scripting</c> serialize ĐỘC LẬP nên không
/// gọi hàm ngoài được — luật phải cài lên <c>window._edsONgayHopLe</c> (đúng cách <c>_na</c>/<c>_provCore</c>
/// của <c>exec.js</c>). Test lấy NGUYÊN VĂN thân <c>pageCaiLuatONgayEds</c> ra khỏi file production bằng
/// Acornima rồi chạy bằng Jint với một <c>window</c> giả. Không có bản chép luật nào trong C#: sửa luật ở
/// <c>flow-km.js</c> là test này đổi theo ngay, đó chính là điều kiện để nó không phải test trang trí.</para>
/// </summary>
public class EdsONgayHopLeTests
{
    private const string TenHamCaiLuat = "pageCaiLuatONgayEds";

    /// <summary>Đi từ thư mục chạy test ngược lên tìm gốc repo (có <c>ShopeeSuite.sln</c> + <c>extensions/</c>).</summary>
    private static string FileFlowKm()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var f = Path.Combine(dir.FullName, "extensions", "shopee-orders", "flow-km.js");
            if (File.Exists(Path.Combine(dir.FullName, "ShopeeSuite.sln")) && File.Exists(f))
            {
                return f;
            }
        }

        throw new FileNotFoundException(
            $"Không tìm thấy extensions/shopee-orders/flow-km.js từ {AppContext.BaseDirectory}");
    }

    /// <summary>Nguyên văn nguồn của MỘT khai báo hàm top-level trong flow-km.js (kể cả khi có <c>export</c>).</summary>
    private static string NguonHam(string ten)
    {
        var duong = FileFlowKm();
        var nguon = File.ReadAllText(duong);
        var module = new Parser().ParseModule(nguon, duong);

        foreach (var stmt in module.Body)
        {
            var fd = stmt switch
            {
                FunctionDeclaration f => f,
                ExportNamedDeclaration en when en.Declaration is FunctionDeclaration ef => ef,
                _ => null,
            };
            if (fd?.Id?.Name == ten)
            {
                return nguon[fd.Range.Start..fd.Range.End];
            }
        }

        throw new InvalidOperationException($"flow-km.js không còn hàm top-level tên '{ten}'");
    }

    /// <summary>Máy JS đã nạp luật THẬT: gọi <c>HopLe(className)</c> là chạy đúng thân hàm production.</summary>
    private static Func<string, bool> LuatThat()
    {
        var engine = new Jint.Engine();
        engine.Execute("var window = {};");
        engine.Execute(NguonHam(TenHamCaiLuat));
        engine.Execute(TenHamCaiLuat + "();");
        // Cài xong mà không có hàm ⇒ hỏng ngay tại đây, đừng để các ca dưới báo "false" rồi đổ cho luật.
        var luat = engine.Evaluate("window._edsONgayHopLe");
        Assert.True(luat is Jint.Native.Function.Function,
            $"{TenHamCaiLuat}() chạy xong mà window._edsONgayHopLe không phải hàm (là '{luat.Type}')");

        // Ép KIỂU CỨNG sang bool: luật trả về thứ khác boolean (undefined vì lỗi, chuỗi, số…) thì test nổ ngay
        // chứ không lặng lẽ đi qua phép quy đổi truthy của JS — đúng thứ đã che mất một proof đo rỗng đợt trước.
        return cls => (bool)engine.Invoke(luat, cls).ToObject()!;
    }

    // ══════════ Ma trận className — hai dòng đầu là DOM THẬT người dùng dump 27/08/2026 ══════════

    [Theory]
    // Ô ngày thường trong tháng đang hiện.
    [InlineData("eds-date-table__cell normal", true)]
    // ⚠ CA GIẾT BẢN CŨ: ô HÔM NAY, đã chọn sẵn, nằm ở BIÊN dải nên mang `disabled-next`. Bản `/disabled/i.test`
    //   loại nó ⇒ bước 4 báo "không thấy ô ngày '27'".
    [InlineData("eds-date-table__cell normal current selected disabled-next", true)]
    // ⚠ CA GIẾT BẢN CŨ (đầu kia của dải): ô CUỐI khoảng 180 ngày, hợp lệ, chỉ có bo góc.
    [InlineData("eds-date-table__cell normal disabled-prev", true)]
    // Ngoài tháng đang hiện — bấm là đặt SAI THÁNG, dù `disabled-prev` chỉ là bo góc.
    [InlineData("eds-date-table__cell out-of-month is-empty disabled-prev", false)]
    // Ô THẬT SỰ bị vô hiệu (ngoài khoảng Shopee cho phép).
    [InlineData("eds-date-table__cell normal disabled", false)]
    // Ô đệm không mang ngày nào.
    [InlineData("eds-date-table__cell is-empty", false)]
    // Picker react của flow-combo.js: ngày của tháng liền kề, KHÔNG có `disabled` nhưng bấm là sai tháng.
    [InlineData("eds-react-date-picker__table-cell out-of-range", false)]
    public void LuatONgay_ChamDungTungCa(string className, bool hopLe)
    {
        var luat = LuatThat();
        Assert.Equal(hopLe, luat(className));
    }

    /// <summary>Ca biên của chính phép tách token: chuỗi rỗng/null-ish và khoảng trắng thừa không được đổi kết quả.</summary>
    [Theory]
    [InlineData("", true)]
    [InlineData("   eds-date-table__cell   normal   disabled-next  ", true)]
    [InlineData("   eds-date-table__cell   normal   disabled  ", false)]
    public void LuatONgay_KhongVoVeKhoangTrang(string className, bool hopLe)
    {
        var luat = LuatThat();
        Assert.Equal(hopLe, luat(className));
    }

    // ══════════ Lưới chống TRÔI LỆCH: chỗ dùng luật phải THẬT SỰ gọi luật ══════════

    /// <summary>
    /// <c>pageClickNgayTheoSo</c> phải LỌC BẰNG <c>_edsONgayHopLe</c> và tuyệt đối không chứa phép so chuỗi con
    /// <c>/disabled/</c> nào nữa.
    /// <para>Vì sao cần dù đã có ma trận trên: ma trận chấm LUẬT, không chấm NƠI DÙNG. Ai đó chép luật ngược trở
    /// lại vào page-func (đúng thứ vừa xảy ra) thì ma trận vẫn xanh còn shop vẫn hỏng.</para>
    /// </summary>
    [Fact]
    public void PageClickNgayTheoSo_PhaiGoiLuatChung_VaKhongConSoChuoiCon()
    {
        var nguon = NguonHam("pageClickNgayTheoSo");

        Assert.Contains("_edsONgayHopLe(c.className)", nguon);
        Assert.DoesNotContain("/disabled/", nguon);
        // `coSan` phải ĐỦ danh sách: bản cắt `slice(0, 12)` đã làm phiên chẩn đoán 27/08 đọc ra một cái trần
        // "chỉ tới ngày 12" không hề tồn tại.
        Assert.DoesNotContain("slice(0, 12)", nguon);
        // Class thật của ô mang đúng số ngày cần tìm — chính nó nói ngay ra `disabled-next`.
        Assert.Contains("clsOCungSo", nguon);
    }

    /// <summary>
    /// <c>datOThoiGian</c> phải DỪNG trước nút "Xác nhận" khi chọn ngày hỏng.
    /// <para>Dump 27/08 có <c>ngay.ok=false</c> mà <c>xacNhan.ok=true</c>: chốt một picker chưa chọn được gì thì
    /// ô thời gian giữ nguyên giá trị CŨ và bằng chứng về cú trượt bị xoá, chỉ còn "ĐỌC LẠI KHÔNG KHỚP".</para>
    /// </summary>
    [Fact]
    public void DatOThoiGian_ChonNgayHong_ThiKhongDiToiXacNhan()
    {
        var nguon = NguonHam("datOThoiGian");

        var iChonNgay = nguon.IndexOf("pageClickNgayTheoSo", StringComparison.Ordinal);
        var iThoat = nguon.IndexOf("KHÔNG bấm Xác nhận", StringComparison.Ordinal);
        var iXacNhan = nguon.IndexOf("pageXacNhanPicker", StringComparison.Ordinal);

        Assert.True(iChonNgay >= 0, "datOThoiGian không còn gọi pageClickNgayTheoSo");
        Assert.True(iThoat > iChonNgay, "không thấy lối thoát sau khi chọn ngày hỏng");
        Assert.True(iXacNhan > iThoat, "nút Xác nhận vẫn nằm TRƯỚC lối thoát — picker hỏng vẫn bị chốt");
        Assert.Contains("if (!ngay || !ngay.ok)", nguon);
        // Câu lỗi phải tự nói ra nguyên nhân: danh sách ngày chọn được + class ô + lý do chuyển tháng hỏng.
        Assert.Contains("ngay.coSan", nguon);
        Assert.Contains("ngay.clsOCungSo", nguon);
        Assert.Contains("ghiChuThang", nguon);
    }
}
