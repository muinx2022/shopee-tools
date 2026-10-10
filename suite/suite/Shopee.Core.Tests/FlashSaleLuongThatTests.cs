using Acornima.Ast;
using Jint;

namespace Shopee.Core.Tests;

/// <summary>
/// <b>LUỒNG THẬT "Flash Sale Của Shop"</b> — phần kiểm được bằng test là JS chạy trong trang: bốn LUẬT THUẦN
/// (<c>dayDoanhSoKhongTangDan</c> · <c>khungChuaDung</c> · <c>trangChoKhung</c> · <c>docThamSoFlash</c>) và
/// RÀNG BUỘC "đúng 2 cú bấm sắp xếp, không bao giờ 3" ở phía luồng thật.
///
/// <para>Dùng lại nguyên bộ đồ nghề của <see cref="FlashSaleDoDomTests"/> (Acornima bóc thân hàm production +
/// Jint chạy thật). <b>Không có bản chép luật nào trong C#</b>: sửa luật ở <c>flow-flashsale.js</c> là test này
/// đổi theo ngay.</para>
///
/// <para><b>Vì sao tách file khỏi lưới của lượt DÒ:</b> hai lượt có hai ràng buộc NGƯỢC NHAU ở đúng chỗ nguy
/// hiểm nhất — lượt dò bấm cú sắp xếp THỨ BA (để đo chu kỳ) và TUYỆT ĐỐI không bấm nút tạo; luồng thật thì
/// ngược lại. Gộp một file là hai bộ ràng buộc dễ bị đọc nhầm sang nhau.</para>
/// </summary>
public class FlashSaleLuongThatTests
{
    private const string FileFlash = "flow-flashsale.js";

    /// <summary>Máy Jint đã nạp nguyên văn bốn hàm THUẦN của luồng thật (bỏ <c>export</c> — Jint chạy script).</summary>
    private static Engine MayLuatThuan()
    {
        var engine = new Engine();
        foreach (var ten in new[]
                 {
                     "dayDoanhSoKhongTangDan", "khungChuaDung", "trangChoKhung", "docThamSoFlash",
                     // RÀNG BUỘC "sắp xếp ĐẠT rồi mới lật" (29/08/2026) — Jint resolve tên hàm toàn cục LÚC
                     // GỌI, nên `cuaKiemTrangSauLat` gọi `dayDoanhSoKhongTangDan` không phụ thuộc thứ tự nạp.
                     "congLatTrang", "cuaKiemTrangSauLat", "cuaDungTrangDich",
                     // QUAY VÒNG TRANG (03/09/2026): số trang thật đọc từ nhãn pager.
                     "soTrangTuNhan",
                 })
        {
            engine.Execute(FlashSaleDoDomTests.NguonHam(FileFlash, ten));
        }

        // Trần vòng khung là hằng KỸ THUẬT của module — nạp đúng giá trị trong file, không viết cứng.
        // (`SO_KHUNG_TOI_DA` đã bị gỡ 03/09/2026 cùng trần 8 khung; `docThamSoFlash` không còn đọc hằng nào.)
        engine.Execute("const TRAN_VONG_KHUNG_KY_THUAT = " + HangSo("TRAN_VONG_KHUNG_KY_THUAT") + ";");
        return engine;
    }

    /// <summary>Giá trị nguyên văn của một hằng <c>const</c> top-level trong file production.</summary>
    private static string HangSo(string ten)
    {
        var nguon = FlashSaleDoDomTests.NguonFile(FileFlash);
        var m = System.Text.RegularExpressions.Regex.Match(nguon, @"^const\s+" + ten + @"\s*=\s*([^;]+);",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(m.Success, $"không còn hằng `{ten}` trong {FileFlash}");
        return m.Groups[1].Value.Trim();
    }

    /// <summary>Số lần <paramref name="tim"/> xuất hiện trong <paramref name="s"/> — dùng cho các phép grep
    /// nguồn cần khoá "MỌI chỗ gọi", không phải "có ít nhất một chỗ".</summary>
    private static int Dem(string s, string tim)
        => (s.Length - s.Replace(tim, "", StringComparison.Ordinal).Length) / tim.Length;

    private static string Js(IEnumerable<string?> ds)
        => "[" + string.Join(",", ds.Select(x => x is null ? "null" : "\"" + x + "\"")) + "]";

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // LUẬT 2 — CỬA KIỂM DOANH SỐ: dãy phải KHÔNG TĂNG DẦN
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Dựng TRỌN GÓI mà <c>pageDocDoanhSoMoiDongFlash</c> trả về rồi chấm.
    /// <para><b>⚠ Cố ý KHÔNG có overload nhận mảng trần.</b> Bản đầu của cửa nhận mỗi <c>giaTri</c> và vì thế
    /// đánh rơi <c>coCot</c>/<c>loi</c> — bốn lối ra "không đọc được" đều lọt thành ĐẠT. Test mà còn đường
    /// truyền mảng trần thì nó lại khoá đúng cái hình dạng vừa phải bỏ.</para>
    /// </summary>
    private static (bool Dat, string Ly) CuaDoanhSo(
        Engine engine, int soDong, bool coCot = true, string? loi = null, params string?[] giaTri)
    {
        var goi = "{coCot:" + (coCot ? "true" : "false") + ", soDong:" + soDong
                  + ", giaTri:" + Js(giaTri) + ", loi:" + (loi is null ? "null" : "\"" + loi + "\"") + "}";
        var kq = engine.Evaluate("dayDoanhSoKhongTangDan(" + goi + ")").AsObject();
        return (kq.Get("dat").AsBoolean(), kq.Get("ly").AsString());
    }

    /// <summary>Gói ĐỌC ĐƯỢC BÌNH THƯỜNG — <c>soDong</c> khớp số giá trị truyền vào.</summary>
    private static (bool Dat, string Ly) CuaDoanhSoDocDuoc(Engine engine, params string?[] giaTri)
        => CuaDoanhSo(engine, giaTri.Length, true, null, giaTri);

    /// <summary>
    /// <b>Ba ca của cửa kiểm, đo bằng CHÍNH số đo lượt dò 11 (Minoa Store, 28/08/2026).</b>
    /// <list type="bullet">
    /// <item>M2 = <c>14,4,4,4,3,3,2,2,2,2</c> (2 cú bấm, GIẢM DẦN) ⇒ <b>ĐẠT</b> — đây là trạng thái đích;</item>
    /// <item>M1 đảo ngược (TĂNG DẦN) ⇒ <b>HỎNG</b> — mới bấm một cú, hoặc cú bấm không ăn;</item>
    /// <item>toàn <c>0</c> ⇒ <b>ĐẠT</b> — mọi giá trị bằng nhau thì không có cặp nào tăng, và với dữ liệu đó
    /// tăng dần với giảm dần là cùng một thứ tự nên không có gì bị chọn sai.</item>
    /// </list>
    /// </summary>
    [Fact]
    public void CuaKiemDoanhSo_BaCa_TheoDungSoDoLuot11()
    {
        var e = MayLuatThuan();

        // M2 THẬT của lượt 11 — 2 cú bấm `down`.
        var giam = CuaDoanhSoDocDuoc(e, "14", "4", "4", "4", "3", "3", "2", "2", "2", "2");
        Assert.True(giam.Dat, giam.Ly);

        // TĂNG DẦN ⇒ HỎNG, và câu lỗi phải chỉ đúng cặp tăng.
        var tang = CuaDoanhSoDocDuoc(e, "2", "2", "2", "2", "3", "3", "4", "4", "4", "14");
        Assert.False(tang.Dat);
        Assert.Contains("TĂNG", tang.Ly);

        // TOÀN 0 ⇒ ĐẠT. Đây là ca bản đầu của plan không làm được (cửa kiểm cũ đặt ở class nút).
        var khong = CuaDoanhSoDocDuoc(e, "0", "0", "0", "0", "0", "0", "0", "0", "0", "0");
        Assert.True(khong.Dat, khong.Ly);
        Assert.Contains("BẰNG NHAU", khong.Ly);
    }

    /// <summary>
    /// <b>Chỉ MỘT cặp tăng ở GIỮA dãy cũng phải HỎNG.</b>
    /// <para>Đây là ca hiểm: dãy trông giảm dần ở hai đầu nên mắt người lướt qua sẽ cho đạt, mà một cặp tăng
    /// nghĩa là bảng KHÔNG ở trạng thái sắp xếp — 10 sản phẩm nạp vào không phải 10 sản phẩm bán chạy nhất.</para>
    /// </summary>
    [Fact]
    public void CuaKiemDoanhSo_MotCapTangOGiua_VanHong()
    {
        var e = MayLuatThuan();
        var kq = CuaDoanhSoDocDuoc(e, "14", "4", "3", "9", "2", "1");
        Assert.False(kq.Dat);
        Assert.Contains("3 < 9", kq.Ly);
    }

    /// <summary>
    /// Chuỗi Shopee trả về có dấu phân nhóm nghìn, và có ô RỖNG. Luật: giữ chữ số; ô không còn chữ số nào thì
    /// <b>bỏ khỏi phép so</b> — KHÔNG đoán nó bằng 0, vì đoán là tự chế ra một cặp tăng/giảm không có thật.
    /// </summary>
    [Fact]
    public void CuaKiemDoanhSo_DocSoCoDauPhanNhom()
    {
        var e = MayLuatThuan();

        var kq = CuaDoanhSoDocDuoc(e, "1.234", "999", "12");
        Assert.True(kq.Dat, kq.Ly);

        // Ô rỗng nằm giữa hai giá trị giảm dần: bỏ ô rỗng ⇒ còn 2 giá trị, vẫn giảm dần ⇒ ĐẠT.
        // (Nếu đoán ô rỗng = 0 thì dãy thành 14,0,4 và cặp 0 < 4 sẽ chấm HỎNG OAN.)
        var coRong = CuaDoanhSoDocDuoc(e, "14", "", "4");
        Assert.True(coRong.Dat, coRong.Ly);
    }

    /// <summary>
    /// <b>⚠ CỬA PHẢI FAIL-CLOSED: KHÔNG ĐỌC ĐƯỢC ⇒ HỎNG.</b> Đây là lưới thay cho một test cũ đã khoá NHẦM
    /// hành vi ngược lại (<c>Assert.True(rongHet.Dat)</c>) — tức bộ test khi ấy BẢO VỆ lỗ hổng.
    ///
    /// <para><c>pageDocDoanhSoMoiDongFlash</c> có BỐN lối ra trả <c>giaTri: []</c>: chưa cài luật · không thấy
    /// modal · không thấy cột "Doanh số" · không thấy bảng THÂN có dòng. Với cửa fail-open thì cả bốn dẫn thẳng
    /// tới nút TẠO THẬT với 10 sản phẩm TUỲ Ý — <c>daTao:true</c>, nhật ký xanh, không một dòng cảnh báo. Đúng
    /// thứ plan gọi là "ca sai âm thầm nguy hiểm nhất của luồng này".</para>
    /// </summary>
    [Fact]
    public void CuaKiemDoanhSo_KhongDocDuoc_ThiHONG_KhongPhaiDat()
    {
        var e = MayLuatThuan();

        // (1)(2)(3) — ba lối ra `coCot: false`.
        var khongCot = CuaDoanhSo(e, 10, coCot: false, loi: "chưa cài window._flashChiSoCotDoanhSo");
        Assert.False(khongCot.Dat);
        Assert.Contains("KHÔNG đọc được cột Doanh số", khongCot.Ly);
        Assert.Contains("FAIL-CLOSED", khongCot.Ly);
        Assert.Contains("chưa cài", khongCot.Ly);   // câu lỗi của page-func phải đi kèm

        // (4) — thấy cột nhưng bảng THÂN không có dòng nào, trong khi bước trước vừa chờ được dòng hiện ra.
        var khongDong = CuaDoanhSo(e, 0, loi: "không thấy bảng nào (ngoài bảng trái) có dòng sản phẩm");
        Assert.False(khongDong.Dat);
        Assert.Contains("không có dòng sản phẩm nào", khongDong.Ly);

        // LỆCH CỘT — bảng có 10 dòng mà chỉ đọc được 0/1 giá trị có chữ số. Đây là ca `headerLaBangRieng`:
        // chỉ số cột lấy từ bảng HEADER rồi áp vào bảng THÂN.
        var lechCot = CuaDoanhSo(e, 10, true, null, null, "—");
        Assert.False(lechCot.Dat);
        Assert.Contains("CHỈ SỐ CỘT LỆCH", lechCot.Ly);

        // …và gói RỖNG/null cũng HỎNG, không được ném.
        var goiRong = e.Evaluate("dayDoanhSoKhongTangDan(null)").AsObject();
        Assert.False(goiRong.Get("dat").AsBoolean());
    }

    /// <summary>
    /// Vế ĐỐI CHỨNG của cửa fail-closed: bảng có ĐÚNG 1 dòng (shop chỉ còn 1 sản phẩm nhận được) là ca HỢP LỆ
    /// — không có cặp nào để so, và cũng không có gì bị chọn sai.
    /// <para>Không có vế này thì một bản "HỎNG mọi lúc" cũng qua được mọi test ở trên, và luồng sẽ bỏ oan mọi
    /// shop ít hàng.</para>
    /// </summary>
    [Fact]
    public void CuaKiemDoanhSo_BangDungMotDong_VanDat()
    {
        var e = MayLuatThuan();
        var kq = CuaDoanhSo(e, 1, true, null, "14");
        Assert.True(kq.Dat, kq.Ly);
        Assert.Contains("chỉ có 1 dòng", kq.Ly);
    }

    /// <summary>
    /// <b>Chỗ GỌI phải truyền TRỌN GÓI, không bóc <c>giaTri</c> ra.</b> Cửa fail-closed ở trên vô nghĩa nếu
    /// luồng thật vẫn đưa vào một mảng trần — lúc đó <c>coCot</c> luôn <c>undefined</c> và mọi lượt đều HỎNG,
    /// hoặc tệ hơn, ai đó "sửa" bằng cách nới cửa ra.
    /// </summary>
    [Fact]
    public void ChoGoiCuaKiem_TruyenTronGoiDoc_KhongBocMangTran()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");

        Assert.Contains("docDoanhSo = await chay(pageDocDoanhSoMoiDongFlash, []);", nguon);
        Assert.Contains("dayDoanhSoKhongTangDan(docDoanhSo)", nguon);
        // Không còn đường bóc mảng ra rồi truyền đi.
        Assert.DoesNotContain("dayDoanhSoKhongTangDan(doanhSo", nguon);
        // Và bằng chứng đọc được gì PHẢI vào dump — cửa đỏ mà không nói vì sao là phải chạy lại một lượt dò.
        foreach (var truong in new[] { "coCot:", "soDongDocDuoc:", "loiDoc:", "nhanCacCot:" })
        {
            Assert.Contains(truong, nguon);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // LUẬT 3 — KHUNG ĐÃ DÙNG TRONG LƯỢT THÌ KHÔNG CHỌN LẠI
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    private const string BaKhung =
        "[{chiSo:0,value:\"A\",disabled:false,nhan:\"02:00 - 09:00\"},"
        + "{chiSo:1,value:\"B\",disabled:false,nhan:\"09:00 - 12:00\"},"
        + "{chiSo:2,value:\"C\",disabled:false,nhan:\"12:00 - 15:00\"}]";

    /// <summary>
    /// <b>3 khung, 3 lượt chọn ⇒ 3 <c>value</c> KHÁC NHAU, rồi lượt thứ tư báo HẾT.</b>
    /// <para>Đây là thứ DUY NHẤT luồng phải nhớ trong một lượt (plan §4) — và nó sống trong bộ nhớ, không phải
    /// DB. Chọn lại một khung đã dùng nghĩa là Shopee từ chối (khung đã có chương trình) hoặc tệ hơn: tạo
    /// chương trình THỨ HAI đè lên cùng khung.</para>
    /// </summary>
    [Fact]
    public void KhungChuaDung_BaLuot_RaBaValueKhacNhau_RoiHet()
    {
        var e = MayLuatThuan();
        var daDung = new List<string>();

        for (var i = 0; i < 3; i++)
        {
            var kq = e.Evaluate("khungChuaDung(" + BaKhung + ", " + Js(daDung!) + ")").AsObject();
            var value = kq.Get("value").AsString();
            Assert.True(kq.Get("chiSo").AsNumber() >= 0, "lượt " + (i + 1) + " KHÔNG chọn được khung nào");
            Assert.DoesNotContain(value, daDung);
            daDung.Add(value);
        }

        Assert.Equal(new[] { "A", "B", "C" }, daDung);

        var het = e.Evaluate("khungChuaDung(" + BaKhung + ", " + Js(daDung!) + ")").AsObject();
        Assert.Equal(-1, het.Get("chiSo").AsNumber());
        Assert.Contains("hết khung chưa dùng", het.Get("ly").AsString());
    }

    /// <summary>Khung <c>disabled</c> hoặc KHÔNG có <c>value</c> đều bị loại: không có <c>value</c> thì không có
    /// cách nào ghi nhớ là đã dùng, và bấm mù là bấm lại đúng khung vừa tạo.</summary>
    [Fact]
    public void KhungChuaDung_LoaiKhungDisabled_VaKhungKhongCoValue()
    {
        var e = MayLuatThuan();
        var ds = "[{chiSo:0,value:\"A\",disabled:true},{chiSo:1,value:null,disabled:false},"
                 + "{chiSo:2,value:\"C\",disabled:false}]";
        var kq = e.Evaluate("khungChuaDung(" + ds + ", [])").AsObject();
        Assert.Equal(2, kq.Get("chiSo").AsNumber());
        Assert.Equal("C", kq.Get("value").AsString());
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // LUẬT 4 — KHUNG THỨ k ⇒ TRANG THỨ k
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Khung thứ k của ngày ⇒ TRANG thứ k, QUAY VÒNG khi hết trang</b> (đếm từ 1), và chỉ số trang RESET về
    /// 1 ở mỗi ngày mới — mà "ngày mới" ở bản nhật-ngày chính là một lượt mới với <c>soKhungDaTao = 0</c>.
    /// <para>Sai vế "trang khác nhau" là các khung của một ngày cùng nạp một tập 10 sản phẩm, thay vì phủ nhiều
    /// sản phẩm bán chạy nhất — không lỗi, không log, chỉ là chương trình kém hiệu quả nhiều lần.</para>
    /// <para><b>Sai vế QUAY VÒNG thì đắt hơn hẳn</b> (P-20260903-4): bản trước trả thẳng <c>n + 1</c>, nên shop
    /// 3 trang tới khung 4 là bước F lật quá trang ⇒ <c>pageSangTrangSauFlash</c> đỏ ⇒ <c>ketThuc</c> ⇒ MẤT nốt
    /// mọi khung còn lại của shop. Hai hàng cuối khoá ca <c>soTrang</c> rác (0/âm) ⇒ coi như 1 trang, KHÔNG chia
    /// cho 0.</para>
    /// </summary>
    [Theory]
    [InlineData(0, 8, 1)]
    [InlineData(1, 8, 2)]
    [InlineData(7, 8, 8)]
    [InlineData(3, 3, 1)]
    [InlineData(4, 3, 2)]
    [InlineData(5, 3, 3)]
    [InlineData(6, 3, 1)]
    [InlineData(0, 1, 1)]
    [InlineData(5, 1, 1)]
    [InlineData(2, 0, 1)]
    [InlineData(2, -1, 1)]
    public void TrangChoKhung_KhungThuK_RaTrangK(int soKhungDaTao, int soTrang, int trangMongDoi)
    {
        var e = MayLuatThuan();
        Assert.Equal(trangMongDoi, e.Evaluate($"trangChoKhung({soKhungDaTao}, {soTrang})").AsNumber());
    }

    /// <summary>
    /// <b>Số trang THẬT đọc từ nhãn pager = SỐ NGUYÊN LỚN NHẤT trong dãy, không phải phần tử cuối.</b>
    /// <para>Pager EDS vẽ <c>1 2 3 … 12</c> và có nút mũi tên; lấy phần tử cuối thì gặp <c>"›"</c> là ra 1 trang
    /// ⇒ mọi khung dồn về trang 1. Dãy không có số nào (shop 1 trang không render pager, hoặc chỉ có mũi tên) ⇒
    /// <b>1</b>, chứ không phải 0 — 0 sẽ làm <c>trangChoKhung</c> chia cho 0.</para>
    /// </summary>
    [Theory]
    [InlineData("[\"1\",\"2\",\"3\"]", 3)]
    [InlineData("[\"1\",\"2\",\"...\",\"12\"]", 12)]
    [InlineData("[]", 1)]
    [InlineData("[\"‹\",\"1\",\"›\"]", 1)]
    [InlineData("null", 1)]
    [InlineData("[\"a\",\"b\"]", 1)]
    public void SoTrangTuNhan_LayMaxSoNguyen_KhongCoSoThiMotTrang(string nhanJs, int mongDoi)
    {
        var e = MayLuatThuan();
        Assert.Equal(mongDoi, e.Evaluate($"soTrangTuNhan({nhanJs})").AsNumber());
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // LUẬT 1 — ĐÚNG 2 CÚ BẤM SẮP XẾP, KHÔNG BAO GIỜ 3, VÀ CÚ THỨ BA CỦA LƯỢT DÒ KHÔNG LỌT SANG LUỒNG THẬT
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Luồng thật lặp theo HẰNG <c>SO_LAN_BAM_SAP_XEP</c>, và KHÔNG có cú bấm sắp xếp nào ngoài vòng đó.</b>
    ///
    /// <para>Cú thứ ba XOÁ SẠCH sắp xếp — đo thật lượt 11: M3 quay về giống hệt M0. Luồng sẽ nạp 10 sản phẩm
    /// TUỲ Ý thay vì 10 sản phẩm bán chạy nhất, mà mọi bước sau vẫn <c>ok</c> và nhật ký vẫn xanh. Đây là lưới
    /// chặn đúng ca đó, đo bằng AST chứ không bằng mắt.</para>
    /// </summary>
    [Fact]
    public void LuongThat_DungHaiCuBamSapXep_KhongBaoGioBa()
    {
        Assert.Equal("2", HangSo("SO_LAN_BAM_SAP_XEP"));

        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");

        // Vòng lặp chạy theo HẰNG, không theo số viết cứng.
        Assert.Contains("for (let i = 0; i < SO_LAN_BAM_SAP_XEP; i++)", nguon);

        // ĐẾM BẰNG AST: mọi lời gọi `chay(pageBamSapXepDoanhSo, …)` trong luồng thật phải là ĐÚNG MỘT — cái
        // nằm trong vòng lặp. Hai lời gọi = có một cú bấm ngoài vòng, tức cú thứ ba.
        var soLoiGoi = DemLoiGoiBamSapXep(nguon);
        Assert.Equal(1, soLoiGoi);

        // Và KHÔNG có cơ chế thử lại quanh cú bấm: một vòng retry bọc ngoài đẻ ra cú thứ ba mà không ai cố ý.
        Assert.DoesNotContain("thuLai", nguon);
        Assert.DoesNotContain("bamLai", nguon);
    }

    /// <summary>
    /// <b>Cú bấm THỨ BA của lượt dò KHÔNG được chép sang luồng thật.</b>
    /// <para>Nó chỉ hợp lệ ở lượt dò vì lượt đó kết thúc bằng HỦY — bảng có bị xoá sắp xếp cũng không hại ai.
    /// Ở luồng thật thì đó là chọn nhầm 10 sản phẩm, im lặng.</para>
    /// </summary>
    [Fact]
    public void CuBamThuBaCuaLuotDo_KhongLotSangLuongThat()
    {
        var that = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");
        var doDom = FlashSaleDoDomTests.NguonHam(FileFlash, "doDomFlashSaleChoTabDangMo");

        // Dấu vân tay của cú đo: biến `bamDo` + chữ "CHỈ ĐỂ ĐO". Cả hai phải CÒN ở lượt dò và KHÔNG có ở luồng thật.
        Assert.Contains("const bamDo = await chay", doDom);
        Assert.Contains("CHỈ ĐỂ ĐO", doDom);
        Assert.DoesNotContain("bamDo", that);
        Assert.DoesNotContain("CHỈ ĐỂ ĐO", that);

        // Lượt dò: 2 lời gọi (vòng lặp + cú đo). Luồng thật: đúng 1.
        Assert.Equal(2, DemLoiGoiBamSapXep(doDom));
        Assert.Equal(1, DemLoiGoiBamSapXep(that));
    }

    /// <summary>
    /// <b>Bước TẠO THẬT phải CUỘN + CHỜ thanh đáy trước khi bấm.</b> Số đo thật 18:44 29/08/2026: K1.7 hỏng
    /// "không thấy thanh đáy .fix-container" ngay sau khi modal sản phẩm đóng — đúng lớp lỗi b9 của luồng KM
    /// (thanh đáy chỉ hiện khi trang đã cuộn). Lưới: vòng cuộn <c>window.scrollBy</c> phải đứng TRƯỚC lời gọi
    /// <c>chay(pageToaDoNutTaoThat, …)</c> trong luồng thật (31/08: đổi từ pageBamTaoThatFlash — cú LƯU đi
    /// bằng toạ độ + trustedClick, click DOM thường vào DIV thanh đáy câm suốt 29–30/08).
    /// </summary>
    [Fact]
    public void LuongThat_CuonChoThanhDay_TruocKhiBamTaoThat()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");
        var viCuon = nguon.IndexOf("window.scrollBy(0, 400)", StringComparison.Ordinal);
        Assert.True(viCuon >= 0, "luồng thật không còn vòng cuộn cho thanh đáy trước cú TẠO THẬT");

        var cay = new Acornima.Parser().ParseScript(nguon);
        var goiBamTao = FlashSaleDoDomTests.MoiNut(cay)
            .OfType<CallExpression>()
            .Where(g => g.Callee is Identifier { Name: "chay" }
                     && g.Arguments.Count >= 1
                     && g.Arguments[0] is Identifier { Name: "pageToaDoNutTaoThat" })
            .Select(g => g.Range.Start)
            .ToList();
        Assert.Single(goiBamTao);
        Assert.True(viCuon < goiBamTao[0], "vòng cuộn phải đứng TRƯỚC cú bấm TẠO THẬT");

        // 30/08 (phản biện): tín hiệu toast-thành-công phải (a) CHẶN VẾ PHỦ ĐỊNH — "Tạo KHÔNG thành công"
        // chứa "thành công" nên regex trần chấm XANH một cú tạo hỏng; (b) so với BASELINE quét TRƯỚC cú bấm —
        // toast còn sót của bước F không được tính. Đo bằng AST: đúng HAI lời gọi chay(pageQuetBaoFlash,…)
        // (baseline + vòng chờ), một TRƯỚC một SAU cú bấm TẠO THẬT.
        // (31/08: nhánh chẩn đoán "form đếm 0/x" của bước F — từng mang cú quét thứ ba — đã GỠ sau khi tìm ra
        // gốc: form flash mới cần áp %GIẢM + BẬT, xem bước F2 và commit lịch sử. Về lại đúng HAI.)
        Assert.Contains("không thành công|chưa thành công|thất bại", nguon);
        var goiQuetBao = FlashSaleDoDomTests.MoiNut(cay)
            .OfType<CallExpression>()
            .Where(g => g.Callee is Identifier { Name: "chay" }
                     && g.Arguments.Count >= 1
                     && g.Arguments[0] is Identifier { Name: "pageQuetBaoFlash" })
            .Select(g => g.Range.Start)
            .OrderBy(v => v)
            .ToList();
        Assert.Equal(2, goiQuetBao.Count);
        Assert.True(goiQuetBao[0] < goiBamTao[0] && goiBamTao[0] < goiQuetBao[1],
            "phải quét baseline TRƯỚC cú bấm TẠO THẬT và quét lại SAU cú bấm");
    }

    /// <summary>
    /// <b>Đường bấm qua chrome.debugger (trustedClick) chỉ được có Ở CÁC CHỖ CÓ TÊN.</b>
    /// <para>30/08/2026: cú Xác nhận modal sp đổi từ `.click()` trong trang sang cú chuột thật qua
    /// chrome.debugger (`.click()` DOM trúng đúng nút mà Shopee ĐÓNG-mà-KHÔNG-LƯU). 31/08: thêm HAI cú của
    /// vòng DỌN chương trình rỗng — link "Xóa" của hàng + nút "Xóa" trong dialog (`.click()` bị nuốt, 20 cú
    /// mù, rác chiếm hết khung làm 01/09 mất "ngày đỏ"). Whitelist `.click()` của FlashSaleDoDomTests KHÔNG
    /// nhìn thấy đường debugger, nên khoá riêng ở đây: cả file đúng BA lời gọi <c>trustedClick</c>, toạ độ
    /// chỉ được lấy từ các hàm pageToaDo* mang chốt an toàn trong nguồn (từ chối thanh đáy; điều kiện kép
    /// hàng-rỗng). Mọc thêm một lời gọi trustedClick là có một đường bấm-thật mới chưa ai xét.</para>
    /// </summary>
    [Fact]
    public void TrustedClick_ChiCacDuongCoTen()
    {
        var cay = FlashSaleDoDomTests.Cay(FileFlash);
        var goiTrusted = FlashSaleDoDomTests.MoiNut(cay)
            .OfType<CallExpression>()
            .Where(g => g.Callee is Identifier { Name: "trustedClick" })
            .ToList();
        // 9 cú: xoá hàng + xoá dialog (vòng DỌN) · Xác nhận modal KHUNG GIỜ (15:12: click DOM đóng modal mà
        // state form không nhận khung) · "Xem chi tiết" sau dialog Tạo-thành-công (form mới TẠO ngay tại
        // xác nhận khung — phải sang trang chỉnh sửa, ở lại form create là xác sống) · Xác nhận modal sp
        // · Bật hàng loạt · thử toggle đơn + Xác-nhận-lại-khung trong nhánh chẩn đoán (dọn khi chốt đường)
        // · cú LƯU thanh đáy K*.7 (31/08 — click DOM thường vào DIV thanh đáy câm suốt 29–30/08).
        // 6 cú (chốt 17:5x 31/08 — quy trình tay người dùng): Xác nhận modal KHUNG GIỜ (K.3) · "Xem chi
        // tiết" sau dialog Tạo-thành-công nếu nở (K.3) · Xác nhận modal SẢN PHẨM (K.6) · "Chỉnh sửa" hàng
        // 0-bật đã nở (tái chế K0/dự phòng) · cú Xác nhận thanh đáy + cú "Kích hoạt hàng loạt" trong
        // luuVaKichHoat (dùng chung K0 và K*.7). Cụm toggle/Bật/dialog-ảnh đã XOÁ (đi nhầm cửa), vòng DỌN
        // đã TẮT (hàng 0-bật là nguyên liệu tái chế).
        Assert.Equal(6, goiTrusted.Count);

        // Mỗi cú trusted phải đứng SAU lời gọi lấy toạ độ CỦA NÓ, tất cả trong LUỒNG THẬT — và luồng thật
        // không còn gọi hàm click-trong-trang cũ. (31/08: hai cú DỌN đứng TRƯỚC cú modal sp trong nguồn,
        // nên tìm trustedClick TÍNH TỪ vị trí lời gọi toạ độ tương ứng, không lấy cú đầu tiên của cả hàm.)
        var that = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");
        // Vòng DỌN đã TẮT (~16:30 31/08): hàng 0-bật là nguyên liệu tái chế — luồng thật không được gọi
        // đường xoá nữa (hàm giữ trong kho cho thao tác tay).
        Assert.DoesNotContain("chay(pageToaDoNutXoaChuongTrinhRong", that);
        var viToaDo = that.IndexOf("chay(pageToaDoNutXacNhanModalSp", StringComparison.Ordinal);
        Assert.True(viToaDo >= 0, "luồng thật không còn lời gọi lấy toạ độ nút Xác nhận modal sản phẩm");
        Assert.True(that.IndexOf("trustedClick(", viToaDo, StringComparison.Ordinal) > viToaDo,
            "cú trusted modal sp phải đứng SAU lời gọi lấy toạ độ của nó");
        Assert.DoesNotContain("pageXacNhanModalSpFlash", that);

        // Toạ độ + cú click phải CÙNG trạng thái banner "đang gỡ lỗi" (luật dbg-input.js): attach TRƯỚC khi
        // đọc toạ độ.
        var viAttach = that.IndexOf("ensureDbg(", StringComparison.Ordinal);
        Assert.True(viAttach >= 0 && viAttach < viToaDo, "ensureDbg phải attach TRƯỚC khi đọc toạ độ — lệch banner là bấm trượt");
    }

    /// <summary>Đếm lời gọi <c>chay(pageBamSapXepDoanhSo, …)</c> bằng AST — regex không phân biệt được lời gọi
    /// thật với một chuỗi/chú thích nhắc tới cùng cái tên.</summary>
    private static int DemLoiGoiBamSapXep(string nguonHam)
    {
        // `NguonHam` trả về nguyên văn khai báo hàm (đã bỏ `export`) nên nó tự là một câu lệnh script hợp lệ.
        var cay = new Acornima.Parser().ParseScript(nguonHam);
        var so = 0;
        foreach (var nut in FlashSaleDoDomTests.MoiNut(cay))
        {
            if (nut is CallExpression { Callee: Identifier { Name: "chay" } } goi
                && goi.Arguments.Count >= 1
                && goi.Arguments[0] is Identifier { Name: "pageBamSapXepDoanhSo" })
            {
                so++;
            }
        }

        return so;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // THAM SỐ — ngày đích do C# rót xuống, extension TUYỆT ĐỐI không tự đọc đồng hồ
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Extension KHÔNG được đọc đồng hồ để suy ra ngày đích.</b>
    /// <para>Grep chứng minh: trong luồng thật không có <c>new Date()</c> nào ngoài dấu thời gian của gói kết
    /// quả. Đọc đồng hồ trong extension là mở lại đúng ca "ngày mai đổi nghĩa giữa lượt" mà cả thiết kế
    /// <c>ThamSoFlashSale</c> sinh ra để chặn.</para>
    /// </summary>
    [Fact]
    public void LuongThat_KhongDocDongHo_NgayDichDoCSharpRotXuong()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");
        Assert.DoesNotContain("new Date", nguon);
        Assert.DoesNotContain("Date.now", nguon);
        // Ngày đích đi qua ĐÚNG một cửa đọc tham số.
        Assert.Contains("docThamSoFlash(ts)", nguon);
    }

    /// <summary>Tham số thiếu/rác ⇒ bước giả "0. Tham số" ĐỎ TRƯỚC KHI ĐỤNG TRANG, không để bước sau tự báo.</summary>
    [Fact]
    public void DocThamSoFlash_ThieuHoacRac_ThiCoCauLoiCoTen()
    {
        var e = MayLuatThuan();

        var tot = e.Evaluate(
            "docThamSoFlash({ngay:29,thang:8,nam:2026,phanTramGiam:6,soLuongThamGia:12,soKhungToiDa:8})").AsObject();
        Assert.True(tot.Get("loi").IsNull());
        Assert.Equal(29, tot.Get("ngay").AsNumber());

        // TRẦN KHUNG ĐÃ GỠ (03/09/2026): gói C# không còn gửi `soKhungToiDa`, và một bản client CŨ còn gửi thì
        // cửa đọc tham số phải BỎ QUA — đọc lại thành trần là dựng lại đúng cái vừa bỏ, im lặng.
        Assert.True(tot.Get("soKhungToiDa").IsUndefined());

        var rac = e.Evaluate("docThamSoFlash({})").AsObject();
        Assert.False(rac.Get("loi").IsNull());
        Assert.Contains("ngày đích", rac.Get("loi").AsString());
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // HAI THÔNG SỐ BULK NHẬP TỪ FORM (03/09/2026) — hai hằng trong file đã XOÁ
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b><c>docThamSoFlash</c> đọc ĐÚNG hai con số C# rót xuống — không phải một hằng.</b>
    /// <para>Người dùng chốt 03/09/2026: <i>"số 5% và 10 sp cho phép nhập từ client, không fix trong code, lần
    /// sau là 10%, 20 sp thì không phải sửa code"</i>. Số dùng ở đây CỐ Ý khác mặc định 5/10, vì một bản vá vẫn
    /// đọc hằng sẽ ra đúng 5/10 và xanh nếu bài test dùng chính hai số ấy.</para>
    /// </summary>
    [Fact]
    public void DocThamSoFlash_DocDungHaiThongSoBulk()
    {
        var e = MayLuatThuan();
        var tp = e.Evaluate(
            "docThamSoFlash({ngay:4,thang:9,nam:2026,phanTramGiam:8,soLuongThamGia:25})").AsObject();

        Assert.True(tp.Get("loi").IsNull());
        Assert.Equal(8, tp.Get("phanTramGiam").AsNumber());
        Assert.Equal(25, tp.Get("soLuongThamGia").AsNumber());
    }

    /// <summary>
    /// <b>FAIL-CLOSED: thiếu / rác / ngoài biên ⇒ <c>loi</c> CÓ TÊN, tuyệt đối KHÔNG rơi về 5/10.</b>
    ///
    /// <para>Rơi-về-mặc-định là dựng lại đúng hai hằng vừa gỡ, và tệ hơn hằng: người dùng đổi số trên form, tin
    /// rằng Shopee nhận số mới, mà thực tế mọi khung vẫn được áp 5% — không lỗi, không log. Biên khớp
    /// <c>KmDialogViewModel.KiemTraFormFlash</c> (1–99 / 1–9999); lệch biên giữa hai bên là một số qua được form
    /// rồi chết ở bước "0. Tham số".</para>
    /// </summary>
    [Theory]
    [InlineData("{ngay:4,thang:9,nam:2026}", "%giảm")]                                          // client CŨ, thiếu cả hai
    [InlineData("{ngay:4,thang:9,nam:2026,soLuongThamGia:10}", "%giảm")]                        // thiếu %
    [InlineData("{ngay:4,thang:9,nam:2026,phanTramGiam:5}", "SL sản phẩm tham gia")]            // thiếu SL
    [InlineData("{ngay:4,thang:9,nam:2026,phanTramGiam:'5',soLuongThamGia:10}", "%giảm")]       // rác: chuỗi
    [InlineData("{ngay:4,thang:9,nam:2026,phanTramGiam:0,soLuongThamGia:10}", "%giảm")]         // biên dưới
    [InlineData("{ngay:4,thang:9,nam:2026,phanTramGiam:100,soLuongThamGia:10}", "%giảm")]       // biên trên
    [InlineData("{ngay:4,thang:9,nam:2026,phanTramGiam:5,soLuongThamGia:0}", "SL sản phẩm tham gia")]
    [InlineData("{ngay:4,thang:9,nam:2026,phanTramGiam:5,soLuongThamGia:10000}", "SL sản phẩm tham gia")]
    public void DocThamSoFlash_ThamSoBulkHong_ThiFailClosed(string goi, string mongDoiTrongLoi)
    {
        var e = MayLuatThuan();
        var tp = e.Evaluate("docThamSoFlash(" + goi + ")").AsObject();

        Assert.False(tp.Get("loi").IsNull(), "gói hỏng mà `loi` vẫn null ⇒ luồng đi tiếp với một số bịa");
        Assert.Contains(mongDoiTrongLoi, tp.Get("loi").AsString());
    }

    /// <summary>Biên HỢP LỆ vẫn phải qua — không có vế này thì một bản vá "luôn báo lỗi" cũng xanh, và vòng
    /// flash chết câm ở bước "0. Tham số" trên mọi shop.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 10)]
    [InlineData(99, 9999)]
    public void DocThamSoFlash_BienHopLe_ThiQua(int phanTram, int soLuong)
    {
        var e = MayLuatThuan();
        var tp = e.Evaluate(
            $"docThamSoFlash({{ngay:4,thang:9,nam:2026,phanTramGiam:{phanTram},soLuongThamGia:{soLuong}}})")
            .AsObject();

        Assert.True(tp.Get("loi").IsNull());
        Assert.Equal(phanTram, tp.Get("phanTramGiam").AsNumber());
        Assert.Equal(soLuong, tp.Get("soLuongThamGia").AsNumber());
    }

    /// <summary>
    /// <b>Nguồn luồng thật KHÔNG còn hằng <c>PHAN_TRAM_GIAM_FLASH</c>/<c>SO_LUONG_KM_FLASH</c> nào</b> — bước F2
    /// (<c>K*.6b</c>) và bước tái chế K0 phải đọc <c>tp.*</c>.
    ///
    /// <para><b>Vì sao grep nguồn là phép đo bắt buộc</b> (cùng lý do với <c>LuongThat_KhongDocDongHo</c>): hai
    /// hằng ấy nằm ở CẤP MODULE, nên chúng vẫn giải được tên trong luồng thật dù gói tham số đã mang số mới —
    /// bỏ quên một chỗ gọi là shop đó nhận 5% trong khi người dùng đặt 8%, và không test hành vi nào bắt được vì
    /// bước F2 chỉ chạy trên trang Shopee thật.</para>
    /// </summary>
    [Fact]
    public void LuongThat_KhongConHangPhanTramVaSoLuong_MaDocTuGoiThamSo()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");

        Assert.DoesNotContain("PHAN_TRAM_GIAM_FLASH", nguon);
        Assert.DoesNotContain("SO_LUONG_KM_FLASH", nguon);

        // ⚠ ĐẾM, không chỉ `Contains`: hai page-func này có HAI chỗ gọi trong luồng (bước F2 của mỗi khung, và
        // bước tái chế K0 qua `dienVaLuuKichHoat`). `Contains` một mình xanh ngay cả khi MỘT trong hai chỗ vẫn
        // gõ một số cứng — đúng hình dạng lỗ phủ mà lượt thử phá 03/09/2026 bóc ra. Mọi chỗ gọi phải đọc `tp`.
        foreach (var (ham, doiSo) in new[]
                 {
                     ("pageGoPhanTramBulkFlash", "[tp.phanTramGiam]"),
                     ("pageGoSoLuongKmBulkFlash", "[tp.soLuongThamGia]"),
                 })
        {
            var soChoGoi = Dem(nguon, ham + ", ");
            Assert.True(soChoGoi >= 2, $"chỉ còn {soChoGoi} chỗ gọi `{ham}` — luồng đã đổi, xem lại bài test");
            Assert.Equal(soChoGoi, Dem(nguon, ham + ", " + doiSo));
        }
        // Tên bước cũng đọc từ gói — người đọc nhật ký phải thấy ĐÚNG số đã gửi xuống Shopee.
        Assert.Contains("Áp ${tp.phanTramGiam}%GIẢM + SL ${tp.soLuongThamGia} hàng loạt", nguon);

        // …và không còn khai hằng nào ở cấp module (chỉ còn khối chú thích giải thích vì sao đã xoá).
        var caFile = FlashSaleDoDomTests.NguonFile(FileFlash);
        Assert.DoesNotContain("const PHAN_TRAM_GIAM_FLASH", caFile);
        Assert.DoesNotContain("const SO_LUONG_KM_FLASH", caFile);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // GÓI KẾT QUẢ — cùng KHUÔN `kmKetQua`, KIND riêng
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gói trả về phải mang ĐỦ các trường của khuôn <c>kmKetQua</c> (để C# dùng lại
    /// <c>OrdersBridgeSession.DocKetQuaKmMotShop</c>, không đẻ khuôn thứ hai) và đi về <c>kind</c> RIÊNG.
    /// </summary>
    [Fact]
    public void GoiKetQuaLuongThat_CungKhuonKmKetQua_NhungKindRieng()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "doChayFlashSale");

        Assert.Contains("kind: \"flashKetQua\"", nguon);
        Assert.DoesNotContain("kind: \"kmKetQua\"", nguon);
        Assert.DoesNotContain("kind: \"flashDump\"", nguon);

        foreach (var truong in new[] { "soSanPham", "daTao", "boQua", "canhBao", "captcha", "buocHong" })
        {
            Assert.Contains(truong + ":", nguon);
        }

        // Trường RIÊNG của luồng này — C# đọc nó để dừng cả lượt và đặt mốc gọi lại check đơn.
        Assert.Contains("ngayMaiKhongDo", nguon);
        // `token` trả lại NGUYÊN VĂN: channel chỉ nhận gói khớp token của lượt đang chờ.
        Assert.Contains("token: token === undefined ? null : token", nguon);
    }

    /// <summary>
    /// <b>Nút TẠO THẬT chỉ có ĐÚNG MỘT đường bấm</b>, và nó neo NGƯỢC với hai hàm Xác nhận kia: bắt buộc nằm
    /// TRONG <c>.fix-container</c> và bắt buộc KHÔNG nằm trong modal nào.
    /// <para>Lớp <c>confirm-btn</c> dùng chung cho cả nút của modal khung giờ, nên thiếu chốt này là bấm nhầm
    /// sang một nút chỉ điền form rồi báo "đã tạo" — chương trình không hề tồn tại trên Shopee, còn app thì ghi
    /// mốc đã chạy.</para>
    /// </summary>
    [Fact]
    public void NutTaoThat_ChiMotDuong_VaNeoDungThanhDay()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "pageToaDoNutTaoThat");

        Assert.Contains(".fix-container", nguon);
        Assert.Contains("closest(\".eds-modal\")", nguon);

        // ⚠ HAI HÀM XÁC NHẬN KHÔNG-TẠO phải TỪ CHỐI thanh đáy — và phép đo phải phân biệt được "từ chối" với
        // "nhắm vào". Bản trước chỉ `Assert.Contains("fix-container", n)`, mà CẢ HAI hành vi ngược nhau đều
        // chứa đúng chuỗi đó ⇒ đổi một hàm thành nhắm thẳng thanh đáy (tức bấm nút TẠO THẬT ở giữa luồng) thì
        // assert vẫn xanh.
        //
        // `pageToaDoNutXacNhanModalSp` (30/08: đổi từ `pageXacNhanModalSpFlash` — trả TOẠ ĐỘ cho trustedClick
        // thay vì tự `.click()`) LỌC BỎ nút trong `.fix-container`; `pageBamXacNhanKhungFlash` TỪ CHỐI nếu
        // nút tìm được nằm trong đó. Hai cách viết khác nhau nhưng cùng một ý, nên đo theo TỪNG hàm.
        var nSp = FlashSaleDoDomTests.NguonHam(FileFlash, "pageToaDoNutXacNhanModalSp");
        Assert.Contains("!b.closest(\".fix-container\")", nSp);          // LỌC BỎ ngay từ danh sách ứng viên
        Assert.Contains("nut.closest(\".fix-container\")", nSp);         // …và chốt thứ hai trước khi bấm
        Assert.Contains("TỪ CHỐI", nSp);
        // 30/08 (phản biện): chốt phải phủ CẢ `.fixed-container` (markup thật của trang Flash) — thiếu assert
        // này thì xoá vế fixed khỏi hai hàm là 419 test vẫn xanh trong khi lưới chống bấm-nhầm-TẠO-THẬT thủng.
        Assert.Contains("!b.closest(\".fixed-container\")", nSp);
        Assert.Contains("nut.closest(\".fixed-container\")", nSp);

        var nKhung = FlashSaleDoDomTests.NguonHam(FileFlash, "pageBamXacNhanKhungFlash");
        Assert.Contains("nut.closest(\".fix-container\")", nKhung);
        Assert.Contains("nut.closest(\".fixed-container\")", nKhung);
        Assert.Contains("TỪ CHỐI", nKhung);
        var iChot = nKhung.IndexOf("closest(\".fix-container\")", StringComparison.Ordinal);
        var iBam = nKhung.IndexOf("nut.click()", StringComparison.Ordinal);
        Assert.True(iChot > 0 && iBam > iChot, "chốt từ chối thanh đáy nằm SAU cú bấm — vô nghĩa");

        // 31/08: LUỒNG THẬT đi qua bản TOẠ ĐỘ (trustedClick) — chốt từ chối thanh đáy phải theo sang,
        // và chốt phải đứng TRƯỚC chỗ trả toạ độ.
        var nKhungToaDo = FlashSaleDoDomTests.NguonHam(FileFlash, "pageToaDoNutXacNhanKhung");
        Assert.Contains("nut.closest(\".fix-container\")", nKhungToaDo);
        Assert.Contains("nut.closest(\".fixed-container\")", nKhungToaDo);
        Assert.Contains("TỪ CHỐI", nKhungToaDo);
        var iChotToaDo = nKhungToaDo.IndexOf("closest(\".fix-container\")", StringComparison.Ordinal);
        // Mốc "trả toạ độ" = câu return thành công (KHÔNG dùng getBoundingClientRect — helper `hien` đầu hàm
        // cũng gọi nó, trỏ nhầm vào đó là đo trước cả chốt).
        var iTraToaDo = nKhungToaDo.IndexOf("return { ok: true, x:", StringComparison.Ordinal);
        Assert.True(iChotToaDo > 0 && iTraToaDo > iChotToaDo, "chốt từ chối thanh đáy phải đứng TRƯỚC chỗ trả toạ độ");

        // Và chỉ hàm TẠO THẬT mới được NHẮM vào thanh đáy: nó lấy `.fix-container` làm gốc tìm.
        // 29/08/2026: trang tạo FLASH dùng `fixed-container` (đo NEO tại chỗ), trang KM dùng `fix-container`
        // — hàm TẠO THẬT phải neo CẢ HAI, và vẫn chỉ MÌNH nó được lấy thanh đáy làm gốc tìm.
        Assert.Contains("document.querySelectorAll(\".fix-container, .fixed-container\")", nguon);
        Assert.DoesNotContain("document.querySelectorAll(\".fix-container", nSp);
        Assert.DoesNotContain("document.querySelectorAll(\".fix-container", nKhung);
    }

    /// <summary>
    /// Ô "Xem sản phẩm có sẵn" (<c>product-modal-available-checkbox</c>) là BỘ LỌC, mặc định đã BẬT (đo thật
    /// lượt 11) — luồng thật <b>không được tick nó</b>, tick là đổi cả tập sản phẩm.
    /// </summary>
    [Fact]
    public void OXemSanPhamCoSan_KhongDuocTick()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "pageTickChonTatCaFlash");

        // Ô "chọn tất cả" neo vào header bảng TRÁI, không phải vào label bộ lọc.
        Assert.Contains(".eds-table__fix-left", nguon);
        Assert.Contains("const inp = th ? th.querySelector", nguon);

        // ⚠ ĐẾM MỌI CÚ CLICK, không so vị trí IndexOf. Bản trước dùng `IndexOf` ĐẦU TIÊN của chuỗi
        // "product-modal-available-checkbox" — mà chuỗi ấy LUÔN nằm ở helper `neo(...)` đầu hàm, tức luôn đứng
        // trước cú click. Thêm hẳn một cú tick vào ô bộ lọc thì assert cũ VẪN XANH.
        //
        // Luật đo được: trong hàm này có ĐÚNG MỘT cú `.click()`, và nó là `inp.click()` với `inp` lấy từ `th`
        // của bảng trái.
        var soClick = System.Text.RegularExpressions.Regex.Matches(nguon, @"\.click\(\)").Count;
        Assert.Equal(1, soClick);
        Assert.Contains("inp.click()", nguon);

        // Và KHÔNG có biến nào được gán từ label bộ lọc rồi đem bấm.
        Assert.DoesNotContain("querySelector('label[data-cy=\"product-modal-available-checkbox\"]')", nguon);
        Assert.DoesNotContain("coSan.click()", nguon);
        Assert.DoesNotContain("nhanCoSan.click()", nguon);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // ĐỌC LỊCH ĐÚNG MỘT LẦN MỖI SHOP
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Lượt đọc lịch phải nằm SAU một cửa memo, không phải chạy lại mỗi khung.</b>
    ///
    /// <para>Plan §4: <i>"Ngày nào có khung là lịch phát hành của Shopee, không phải thuộc tính riêng của
    /// shop… Đọc 12 lần là 12 lượt chờ render cho cùng một câu trả lời."</i> Cùng lý lẽ ấy áp cho 8 KHUNG của
    /// một shop. Bản đầu để nguyên <c>choLichLang</c> trong thân vòng ⇒ 8 lần/shop, <b>~96 lần/đêm</b>, mỗi lần
    /// tới 12s trần chờ lắng — cho một câu trả lời đã biết từ khung đầu.</para>
    ///
    /// <para>Đo bằng AST + vị trí nguồn: biến memo khai TRƯỚC vòng <c>for</c>, và lời gọi <c>choLichLang</c>
    /// duy nhất nằm SAU cửa <c>if (phaiDocLich)</c>.</para>
    /// </summary>
    [Fact]
    public void DocLich_DungMotLanMoiShop_KhongLapLaiMoiKhung()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");

        var iMemo = nguon.IndexOf("let lichShop = null;", StringComparison.Ordinal);
        var iVong = nguon.IndexOf("for (let vong = 0; vong < TRAN_VONG_KHUNG_KY_THUAT; vong++)",
            StringComparison.Ordinal);
        Assert.True(iMemo > 0, "mất biến memo `lichShop`");
        Assert.True(iVong > iMemo, "memo `lichShop` khai TRONG vòng lặp — mỗi khung lại đọc lịch từ đầu");

        var iCua = nguon.IndexOf("const phaiDocLich = lichShop === null || lichShop.soLatThang > 0;",
            StringComparison.Ordinal);
        Assert.True(iCua > iVong, "mất cửa memo `phaiDocLich`");

        // ĐÚNG MỘT lời gọi `choLichLang` trong luồng thật, và nó nằm SAU cửa.
        var soGoi = System.Text.RegularExpressions.Regex.Matches(nguon, @"await choLichLang\(").Count;
        Assert.Equal(1, soGoi);
        Assert.True(nguon.IndexOf("await choLichLang(", StringComparison.Ordinal) > iCua,
            "lượt đọc lịch nằm NGOÀI cửa memo — nó sẽ chạy lại mỗi khung");

        // Và bằng chứng số lần đọc phải đi vào dump: không có nó thì lần sau không ai đo lại được.
        Assert.Contains("soLanDocLich", nguon);
        Assert.Contains("daDocLaiLich: phaiDocLich", nguon);
    }

    /// <summary>
    /// <b>Ma trận plan §5 đòi: 2 NGÀY × 3 KHUNG ⇒ dãy trang <c>1,2,3,1,2,3</c>.</b>
    ///
    /// <para>Chỉ số trang RESET về 1 ở mỗi NGÀY — mà ở bản nhật-ngày, "ngày mới" chính là một LƯỢT mới với
    /// <c>soKhungDaTao = 0</c>. Test này đi đúng hai lượt đó để khoá cả hai vế: tăng dần trong một ngày, và
    /// reset khi sang ngày.</para>
    ///
    /// <para>Sai vế reset là ngày thứ hai bắt đầu từ trang 4 ⇒ 30 sản phẩm bán chạy nhất KHÔNG bao giờ vào
    /// flash sale; sai vế tăng là các khung của một ngày cùng nạp một tập 10 sản phẩm.</para>
    /// </summary>
    [Fact]
    public void TrangChoKhung_MaTran_HaiNgayBaKhung_Ra123123()
    {
        var e = MayLuatThuan();
        var day = new List<double>();

        for (var ngay = 0; ngay < 2; ngay++)
        {
            // Mỗi NGÀY là một lượt mới: bộ đếm khung của shop bắt đầu lại từ 0.
            var soKhungDaTao = 0;
            for (var khung = 0; khung < 3; khung++)
            {
                day.Add(e.Evaluate($"trangChoKhung({soKhungDaTao}, 3)").AsNumber());
                soKhungDaTao++;   // khung vừa rồi TẠO ĐƯỢC
            }
        }

        Assert.Equal(new double[] { 1, 2, 3, 1, 2, 3 }, day);
    }

    /// <summary>
    /// <b>Ma trận người dùng chốt 03/09/2026: shop 3 TRANG, 8 KHUNG ⇒ <c>1,2,3,1,2,3,1,2</c>.</b>
    /// <para><i>"nếu chỉ có 2–3 page mà 8 khung thì khung 4 quay lại page 1, khung 5 quay lại page 2"</i>. Đây
    /// là ca sẽ CẮN NGAY khi trần 8 khung được gỡ: bản trước bước F lật tới trang 4 của một modal chỉ có 3
    /// trang, <c>pageSangTrangSauFlash</c> trả <c>ok:false</c>, bước ĐỎ, shop mất nốt 5 khung còn lại.</para>
    /// </summary>
    [Fact]
    public void TrangChoKhung_TamKhungBaTrang_QuayVong12312312()
    {
        var e = MayLuatThuan();
        var day = new List<double>();
        for (var soKhungDaTao = 0; soKhungDaTao < 8; soKhungDaTao++)
        {
            day.Add(e.Evaluate($"trangChoKhung({soKhungDaTao}, 3)").AsNumber());
        }

        Assert.Equal(new double[] { 1, 2, 3, 1, 2, 3, 1, 2 }, day);
    }

    /// <summary>
    /// <b>Bước F phải ĐỌC SỐ TRANG THẬT rồi mới tính trang đích</b> — nếu không, <c>trangChoKhung</c> quay vòng
    /// theo một con số không ai đo.
    /// <para>Ma trận thuần ở trên vẫn xanh nguyên vẹn khi production gọi <c>trangChoKhung(soKhungDaTao)</c> một
    /// tham số (JS không báo lỗi thiếu đối, <c>soTrang</c> thành <c>undefined</c> ⇒ mọi khung dồn về trang 1).
    /// Lưới này đo bằng vị trí nguồn: <c>pageDemTickFlash</c> được gọi TRƯỚC <c>buoc(</c> của bước F, và
    /// <c>trangChoKhung</c> nhận đúng hai đối.</para>
    /// </summary>
    [Fact]
    public void BuocF_DocSoTrangThat_TruocKhiTinhTrangDich()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");

        var iDem = nguon.IndexOf("demTruocLat = await chay(pageDemTickFlash, []);", StringComparison.Ordinal);
        Assert.True(iDem > 0, "bước F không còn đọc pager TRƯỚC khi tính trang đích");

        var iSoTrang = nguon.IndexOf("const soTrang = soTrangTuNhan(", StringComparison.Ordinal);
        var iTrangDich = nguon.IndexOf("const trangDich = trangChoKhung(soKhungDaTao, soTrang);",
            StringComparison.Ordinal);
        Assert.True(iSoTrang > iDem, "số trang phải đọc TỪ gói vừa đếm, không phải từ chỗ khác");
        Assert.True(iTrangDich > iSoTrang, "trang đích tính TRƯỚC khi biết số trang");

        // …và cả ba nằm TRƯỚC lời gọi `buoc(` của bước F: tên bước phải nói được `Trang k/n`.
        var iBuocF = nguon.IndexOf("const bF = await buoc(", StringComparison.Ordinal);
        Assert.True(iBuocF > iTrangDich, "đọc pager nằm TRONG thân bước F — tên bước không có số trang thật");
        Assert.Contains("`${K}.6 Trang ${trangDich}/${soTrang}", nguon);

        // Không còn lời gọi một-đối nào sót lại.
        Assert.DoesNotContain("trangChoKhung(soKhungDaTao)", nguon);
    }

    /// <summary>
    /// <b>Vòng khung KHÔNG còn trần nghiệp vụ</b> — người dùng chốt 03/09/2026 <i>"bỏ trần 8 khung, lấy hết
    /// khung Shopee mở"</i>.
    /// <para>Thứ còn lại là <c>TRAN_VONG_KHUNG_KY_THUAT</c>: chốt chống vòng vô tận khi memo <c>khungDaDung</c>
    /// hỏng, không phải mục tiêu. Điểm dừng THẬT là <c>hetKhung</c>. Lưới đo ba vế: hằng cũ đã biến mất khỏi
    /// file, header vòng chạy theo hằng mới, và hằng mới ≥ 24 (một ngày không thể có quá 24 khung giờ).</para>
    /// </summary>
    [Fact]
    public void VongKhung_KhongConTranNghiepVu()
    {
        var nguonFile = FlashSaleDoDomTests.NguonFile(FileFlash);
        Assert.DoesNotContain("SO_KHUNG_TOI_DA", nguonFile);

        Assert.True(double.Parse(HangSo("TRAN_VONG_KHUNG_KY_THUAT")) >= 24,
            "trần kỹ thuật thấp hơn 24 là trần nghiệp vụ trá hình — một ngày có thể tới 24 khung");

        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");
        Assert.Contains("for (let vong = 0; vong < TRAN_VONG_KHUNG_KY_THUAT; vong++)", nguon);
        Assert.DoesNotContain("soKhungToiDa", nguon);

        // Chạm trần mà Shopee vẫn còn khung ⇒ phải NÓI RA, không im lặng coi như xong shop.
        Assert.Contains("chạm trần kỹ thuật", nguon);
    }

    /// <summary>
    /// <b>Vòng chờ dialog "Tạo thành công" ở K*.7 phải là 240 nhịp (2 phút)</b> — khoá hồi quy <b>f9e118d</b>.
    /// <para>Commit đó (31/08/2026 19:35) hạ 240 → 20 nhịp; từ đó <b>9/9 lượt shop</b> (01/09 15:19–15:33,
    /// 02/09 01:2x, 03/09 01:07–01:21) chết ở <c>K2.2</c> với câu <i>"không thấy ô ngày 'N' đang ĐỎ (tổng 35 ô,
    /// 0 ô đỏ)"</i> — mỗi shop chỉ tạo được ĐÚNG 1 khung/ngày, rồi C# tính shop XONG và 9 gói sau bỏ qua nó.
    /// Lượt 31/08 19:10–19:25 với 240 nhịp tạo được liền 4 khung (<c>K2.6 Trang 2</c>, <c>K3.6 Trang 3</c>,
    /// <c>K4.6 Trang 4</c>); giữa hai mốc extension không đổi gì khác. Khoảng bị cắt là K*.3 → K*.7 (2'15" →
    /// 30s): kích hoạt lên bản nháp Shopee chưa dựng xong ⇒ lịch shop mất sạch <c>div.timeslots</c>.</para>
    /// <para>Lý do f9e118d cắt (vượt trần chờ C# 900s) đã hết hiệu lực: <c>FlashChay</c> = 2700s.</para>
    /// </summary>
    [Fact]
    public void ChoDialogTao_240Nhip_KhoaHoiQuyF9e118d()
    {
        Assert.Equal("240", HangSo("CHO_DIALOG_TAO_NHIP"));

        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");
        Assert.Contains("for (let i = 0; i < CHO_DIALOG_TAO_NHIP; i++)", nguon);
    }

    /// <summary>
    /// <b>K*.2 chờ ô ngày đích chuyển ĐỎ có trần, và KHÔNG thêm lượt đọc lịch nào.</b>
    /// <para>Lưới ô ngày render TRƯỚC khi dữ liệu khung của tháng về, nên "0 ô đỏ" ngay sau khi modal mở là
    /// trạng thái TẠM. Khung thứ hai trở đi không đọc lại lịch (<c>phaiDocLich = false</c>) nên trước bản này
    /// nó bấm ngay khi lưới vừa vẽ.</para>
    /// <para><b>Vế thứ hai mới là vế dễ hỏng</b>: cách sửa "cho chắc" là gọi thêm <c>choLichLang</c> — đúng thứ
    /// quyết định "đọc lịch MỘT LẦN mỗi shop" cấm. Số lời gọi phải còn ĐÚNG 1
    /// (xem <see cref="DocLich_DungMotLanMoiShop_KhongLapLaiMoiKhung"/>).</para>
    /// </summary>
    [Fact]
    public void ChoONgayDo_CoTran_KhongThemChoLichLang()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");

        Assert.Contains("for (let i = 0; i < CHO_O_NGAY_DO_NHIP; i++)", nguon);
        Assert.True(double.Parse(HangSo("CHO_O_NGAY_DO_NHIP")) >= 20, "cửa chờ ô ngày đỏ ngắn hơn 10s");

        // Chỉ thử lại ĐÚNG ca "chưa có ô đỏ" — lỗi khác (thiếu luật, không thấy rows) là hỏng thật, thoát ngay.
        Assert.Contains("/đang ĐỎ/", nguon);
        // Bằng chứng số nhịp đã chờ phải đi vào dump, không thì lần sau không ai đo lại được.
        Assert.Contains("soNhipChoNgayDo", nguon);

        var soGoiLich = System.Text.RegularExpressions.Regex.Matches(nguon, @"await choLichLang\(").Count;
        Assert.Equal(1, soGoiLich);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // HẾT KHUNG — kết luận NGHIỆP VỤ, phải khai `boQua`
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Hết khung mà chưa tạo được gì ⇒ phải khai <c>boQua</c>, KHÔNG được để C# tự đoán.</b>
    ///
    /// <para>Bản đầu chỉ trả <c>daTao:false</c> không kèm <c>boQua</c> ⇒ <c>OrdersBridgeSession</c> rơi vào
    /// nhánh cuối và ghi <c>"không tạo được chương trình"</c> — một câu không nêu bước nào. Hai hậu quả: shop
    /// hợp lệ (bị Shopee khoá hết khung) bị đếm vào <c>SoHong</c> thay vì <c>SoBoQua</c>; và khi Shopee đổi
    /// <c>value</c> của radio thì MỌI shop ra đúng câu vô nghĩa đó, đêm nào cũng lặp.</para>
    /// </summary>
    [Fact]
    public void HetKhung_ChuaTaoDuocGi_ThiPhaiKhaiBoQua()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");

        var iHet = nguon.IndexOf("if (hetKhung) {", StringComparison.Ordinal);
        Assert.True(iHet > 0, "mất nhánh hết khung");
        var iBreak = nguon.IndexOf("break;", iHet, StringComparison.Ordinal);
        var khoi = nguon[iHet..iBreak];

        Assert.Contains("soKhungDaTao === 0", khoi);
        Assert.Contains("boQua:", khoi);
        // Câu bỏ qua phải nêu SỐ ĐO, không phải một câu chung chung.
        Assert.Contains("cauHetKhung", khoi);
        Assert.Contains("bị Shopee khoá", nguon);
        Assert.Contains("không đọc được value", nguon);

        // Tạo được rồi mà hết khung thì KHÔNG khai `boQua` — lượt đó là thành công, chỉ là hết khung.
        Assert.Contains("break;", khoi + "break;");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════════════
    // LUẬT 5 (29/08/2026) — "SẮP XẾP ĐẠT RỒI MỚI ĐƯỢC LẬT TRANG", và sort phải CÒN ĐỨNG trên từng trang đã lật
    //
    // Trước bản này thứ tự "sắp xếp trước → lật sau" chỉ được giữ bằng VỊ TRÍ DÒNG CODE, và sau cú lật thì
    // không ai đọc lại Doanh số — trang k bị tick 10 sản phẩm SAI trong im lặng. Ba cửa THUẦN dưới đây biến
    // quy ước thành ràng buộc có lưới; mọi phép kiểm mới là CHỈ ĐỌC, không đẻ thêm cú bấm sắp xếp nào.
    // ══════════════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Dựng TRỌN GÓI mà <c>pageDocDoanhSoMoiDongFlash</c> trả về (khuôn giống <see cref="CuaDoanhSo"/>
    /// nhưng để truyền sang <c>cuaKiemTrangSauLat</c>).</summary>
    private static string GoiDocJs(bool coCot, int soDong, string? loi, params string?[] giaTri)
        => "{coCot:" + (coCot ? "true" : "false") + ", soDong:" + soDong
           + ", giaTri:" + Js(giaTri) + ", loi:" + (loi is null ? "null" : "\"" + loi + "\"") + "}";

    /// <summary>Chấm cửa trang-sau-lật với một trang ĐỌC ĐƯỢC BÌNH THƯỜNG (<c>soDong</c> khớp số ô truyền vào).
    /// <para><c>ViThuTu</c> là cờ THỦ PHẠM: <c>true</c> = hỏng vì thứ tự, <c>false</c> = hỏng vì không đọc được,
    /// <c>null</c> = không có nghĩa (cửa đạt). Bước F chọn câu lỗi theo nó.</para></summary>
    private static (bool Dat, string Ly, double? SoCuoi, bool? ViThuTu) TrangSauLat(
        Engine engine, string soCuoiTrangTruoc, params string?[] giaTri)
        => DocCuaTrang(engine, GoiDocJs(true, giaTri.Length, null, giaTri), soCuoiTrangTruoc);

    /// <summary>Gọi <c>cuaKiemTrangSauLat</c> với một gói đọc dựng SẴN (dùng cho các ca không đọc được).</summary>
    private static (bool Dat, string Ly, double? SoCuoi, bool? ViThuTu) DocCuaTrang(
        Engine engine, string goiDocJs, string soCuoiTrangTruoc)
    {
        var kq = engine.Evaluate("cuaKiemTrangSauLat(" + goiDocJs + ", " + soCuoiTrangTruoc + ")").AsObject();
        var soCuoi = kq.Get("soCuoi");
        var viThuTu = kq.Get("viThuTu");
        return (kq.Get("dat").AsBoolean(), kq.Get("ly").AsString(),
                soCuoi.IsNull() || soCuoi.IsUndefined() ? null : soCuoi.AsNumber(),
                viThuTu.IsNull() || viThuTu.IsUndefined() ? null : viThuTu.AsBoolean());
    }

    /// <summary>
    /// <b>Cổng lật trang chỉ MỞ khi cửa kiểm sắp xếp của TRANG 1 đã ĐẠT — và fail-closed cả ca QUÊN GỌI CỬA.</b>
    ///
    /// <para>Ca <c>null</c>/<c>{}</c> là ca quan trọng nhất: nó mô phỏng bản vá tương lai bỏ hẳn bước E hoặc
    /// thêm một nhánh đi tắt tới bước F. Cổng đòi thấy TẬN MẮT một kết quả <c>dat:true</c>, nên "không có cửa
    /// nào" ra đúng cùng một câu từ chối như "cửa đỏ".</para>
    ///
    /// <para><c>soCuoi</c> là mắt xích đầu của chuỗi kiểm giữa các trang; trang 1 ĐẠT mà không đọc được số nào
    /// (shop một dòng, ô trống) thì <c>null</c> — không bịa ra một mắt xích không có thật.</para>
    /// </summary>
    [Fact]
    public void CongLatTrang_ChiMoKhiCuaTrang1Dat()
    {
        var e = MayLuatThuan();

        // Cửa trang 1 ĐẠT ⇒ mở, và mắt xích = giá trị CUỐI của dãy.
        var mo = e.Evaluate("congLatTrang({dat:true, so:[14,4,4,3,2], cap:-1, soDong:5, ly:\"ok\"})").AsObject();
        Assert.True(mo.Get("dat").AsBoolean(), mo.Get("ly").AsString());
        Assert.Equal(2, mo.Get("soCuoi").AsNumber());

        // Cửa ĐỎ ⇒ đóng, và câu từ chối phải mang theo lý do của cửa con.
        var do_ = e.Evaluate("congLatTrang({dat:false, so:[2,3], cap:0, soDong:2, ly:\"dãy Doanh số TĂNG\"})")
            .AsObject();
        Assert.False(do_.Get("dat").AsBoolean());
        Assert.Contains("KHÔNG lật", do_.Get("ly").AsString());
        Assert.Contains("TĂNG", do_.Get("ly").AsString());

        // QUÊN GỌI CỬA (null/undefined/gói rỗng) ⇒ vẫn ĐÓNG, không ném.
        foreach (var xau in new[] { "null", "undefined", "{}", "{dat:\"true\"}", "{so:[5,4]}" })
        {
            var dong = e.Evaluate("congLatTrang(" + xau + ")").AsObject();
            Assert.False(dong.Get("dat").AsBoolean(), "cổng MỞ với đối số `" + xau + "` — fail-open");
            Assert.Contains("KHÔNG lật", dong.Get("ly").AsString());
            Assert.True(dong.Get("soCuoi").IsNull());
        }

        // ĐẠT mà không đọc được số nào ⇒ vẫn mở (cửa con đã chấm ĐẠT), nhưng KHÔNG có mắt xích.
        var moRong = e.Evaluate("congLatTrang({dat:true, so:[], cap:-1, soDong:1, ly:\"1 dòng\"})").AsObject();
        Assert.True(moRong.Get("dat").AsBoolean());
        Assert.True(moRong.Get("soCuoi").IsNull());
    }

    /// <summary>
    /// Trang vừa đáp giảm dần NỐI TIẾP trang trước ⇒ ĐẠT, và mắt xích chuyển thành giá trị cuối của trang này.
    /// <para>Vế HOÀ ở biên (trang trước kết <c>2</c>, trang này mở <c>2</c>) phải ĐẠT: hai sản phẩm cùng doanh
    /// số nằm hai bên biên trang là chuyện bình thường, chấm đỏ ở đây là đỏ oan hàng loạt.</para>
    /// </summary>
    [Fact]
    public void CuaTrangSauLat_GiamNoiTiep_ThiDat()
    {
        var e = MayLuatThuan();

        var noi = TrangSauLat(e, "3", "3", "2", "2", "1");
        Assert.True(noi.Dat, noi.Ly);
        Assert.Equal(1, noi.SoCuoi);
        // Cờ thủ phạm KHÔNG có nghĩa khi cửa ĐẠT — trả `true`/`false` ở đây là mời người đọc suy diễn bậy.
        Assert.Null(noi.ViThuTu);

        var hoa = TrangSauLat(e, "2", "2", "2");
        Assert.True(hoa.Dat, hoa.Ly);
        Assert.Equal(2, hoa.SoCuoi);
        Assert.Null(hoa.ViThuTu);
    }

    /// <summary>
    /// <b>Trang này mở CAO HƠN chỗ trang trước kết ⇒ HỎNG</b> — thứ tự đã đứt ở BIÊN TRANG, dù trong từng
    /// trang vẫn giảm dần đẹp. Đây chính là lỗ mà cửa của bước E (chỉ chấm trang 1) không thấy.
    /// </summary>
    [Fact]
    public void CuaTrangSauLat_MoLonHonKetTrangTruoc_ThiHong()
    {
        var e = MayLuatThuan();
        var kq = TrangSauLat(e, "2", "5", "4");
        Assert.False(kq.Dat, kq.Ly);
        Assert.Contains("2", kq.Ly);
        Assert.Contains("5", kq.Ly);
        Assert.Contains("đứt giữa hai trang", kq.Ly);
        // Vế NỐI TIẾP vỡ là hỏng vì THỨ TỰ ⇒ bước F được phép nói "không còn giảm dần".
        Assert.True(kq.ViThuTu, "vế nối tiếp vỡ mà không khai là hỏng THỨ TỰ — bước F sẽ nói sai nguyên nhân");
    }

    /// <summary>
    /// <b>CHỐT CHỐNG BUG 0-FALSY.</b> Trang trước kết ở <c>0</c> mà trang này mở ở <c>5</c> ⇒ phải HỎNG.
    ///
    /// <para>Bản cài <c>if (soCuoiTrangTruoc &amp;&amp; …)</c> cho ĐẠT SAI đúng ở ca này — và <c>0</c> là giá trị
    /// PHỔ BIẾN NHẤT của cột Doanh số (sản phẩm chưa bán được cái nào nằm đúng các trang sau). Tức bug falsy sẽ
    /// tắt vế nối tiếp ở chính chỗ nó cần chạy nhất, im lặng.</para>
    /// </summary>
    [Fact]
    public void CuaTrangSauLat_TrangTruocKet0_TrangNayMoLonHon_ThiHong()
    {
        var e = MayLuatThuan();
        var kq = TrangSauLat(e, "0", "5", "4");
        Assert.False(kq.Dat, kq.Ly);
        Assert.Contains("0", kq.Ly);
        Assert.Contains("5", kq.Ly);
        Assert.True(kq.ViThuTu);
    }

    /// <summary>
    /// Vế TRONG-TRANG mượn nguyên <c>dayDoanhSoKhongTangDan</c>: dãy <c>{2,3}</c> tăng ⇒ HỎNG, và câu lý do là
    /// NGUYÊN VĂN của cửa con.
    /// <para>Đây là bằng chứng "một nguồn luật": nếu cửa mới chép lại phép so sánh lần hai thì sửa luật ở
    /// <c>dayDoanhSoKhongTangDan</c> sẽ không kéo theo ca này.</para>
    /// </summary>
    [Fact]
    public void CuaTrangSauLat_TrongTrangTang_ThiHong()
    {
        var e = MayLuatThuan();
        var kq = TrangSauLat(e, "null", "2", "3");
        Assert.False(kq.Dat, kq.Ly);
        Assert.Contains("TĂNG", kq.Ly);
        // Cặp TĂNG trong trang ⇒ hỏng vì THỨ TỰ, không phải vì đọc.
        Assert.True(kq.ViThuTu, "cặp tăng trong trang mà không khai là hỏng THỨ TỰ");
    }

    /// <summary>
    /// <b>Fail-closed XUYÊN QUA cửa con:</b> không đọc được cột Doanh số ở trang vừa đáp ⇒ HỎNG, không phải
    /// "đạt vì không có gì để so". Cửa mới không được nới lỏng thứ cửa con đã siết.
    ///
    /// <para><b>Và cờ thủ phạm phải là <c>false</c>:</b> hai ca dưới đây (mất cột · lệch cột) đỏ vì phép ĐỌC,
    /// không vì cú bấm sắp xếp. Khai <c>true</c> ở đây là chỉ sai thủ phạm — người vận hành đọc câu "dãy Doanh
    /// số không còn giảm dần" sẽ đi soi cú bấm, trong khi hỏng nằm ở chỉ số cột.</para>
    /// </summary>
    [Fact]
    public void CuaTrangSauLat_KhongDocDuocCot_ThiHong()
    {
        var e = MayLuatThuan();

        // (1) Không thấy cột Doanh số (luật 1 của cửa con).
        var mat = DocCuaTrang(e, GoiDocJs(false, 10, "không thấy cột Doanh số"), "3");
        Assert.False(mat.Dat);
        Assert.Contains("KHÔNG đọc được cột Doanh số", mat.Ly);
        Assert.Contains("FAIL-CLOSED", mat.Ly);
        Assert.False(mat.ViThuTu, "mất cột mà khai là hỏng THỨ TỰ — bước F sẽ đổ oan cho cú bấm sắp xếp");

        // (2) LỆCH CỘT (luật 3): bảng khai 10 dòng mà chỉ đọc được 1 giá trị có chữ số.
        var lech = DocCuaTrang(e, GoiDocJs(true, 10, null, "4", "", "", ""), "9");
        Assert.False(lech.Dat);
        Assert.Contains("CHỈ SỐ CỘT LỆCH", lech.Ly);
        Assert.False(lech.ViThuTu, "lệch cột mà khai là hỏng THỨ TỰ — sai thủ phạm y hệt ca mất cột");
    }

    /// <summary>
    /// <b>Trang ĐẠT mà không đọc được số nào ⇒ KẾ THỪA mắt xích của trang trước.</b>
    ///
    /// <para>Trang cuối một dòng / ô Doanh số trống là ca hợp lệ (cửa con cho ĐẠT). Nếu ở đây trả <c>soCuoi =
    /// null</c> thì chuỗi kiểm ĐỨT: mọi trang sau mất vế nối tiếp và cửa thành nửa mù — im lặng, không log.</para>
    /// </summary>
    [Fact]
    public void CuaTrangSauLat_TrangKhongCoSoNaoNhungDat_KeThuaSoCuoi()
    {
        var e = MayLuatThuan();
        var kq = TrangSauLat(e, "4", "");
        Assert.True(kq.Dat, kq.Ly);
        Assert.Equal(4, kq.SoCuoi);
    }

    /// <summary>
    /// <b>Shop TOÀN 0 doanh số không được đỏ oan.</b> Trang trước kết <c>0</c>, trang này toàn <c>0</c> ⇒ ĐẠT
    /// và mắt xích vẫn là <c>0</c> (không bị <c>||</c> nuốt thành <c>null</c>).
    /// <para>Với dữ liệu toàn bằng nhau thì tăng dần và giảm dần là CÙNG một thứ tự — không có gì bị chọn sai,
    /// nên chấm đỏ ở đây là chặn oan cả một lớp shop mới.</para>
    /// </summary>
    [Fact]
    public void CuaTrangSauLat_ToanKhong_HaiTrang_VanDat()
    {
        var e = MayLuatThuan();
        var kq = TrangSauLat(e, "0", "0", "0", "0", "0");
        Assert.True(kq.Dat, kq.Ly);
        Assert.Equal(0, kq.SoCuoi);
    }

    /// <summary>
    /// <b>Năm nhánh của cửa "đã đứng đúng trang k chưa".</b> Luật cố ý LỆCH: chỉ đỏ khi nhãn là SỐ THUẦN và
    /// LỆCH; nhãn rỗng HOẶC nhãn đọc được mà không parse thành số ⇒ cảnh báo, KHÔNG đỏ.
    ///
    /// <para>Nhãn trang active là điểm markup mỏng nhất của cả luồng. Siết thành "không đọc được cũng đỏ" là
    /// chặn cả lượt chạy vì một class name Shopee đổi — trong khi hai lưới nặng hơn (bảng phải ĐỔI sau cú lật
    /// + cửa Doanh số từng trang) đã đứng trước rồi.</para>
    ///
    /// <para><b>Nhánh 5 là nhánh dễ cài sai nhất</b> (và bản đầu đã cài sai): so thẳng
    /// <c>nhan !== String(trangDich)</c> thì <c>"Trang 3"</c> hay <c>"3/8"</c> — hai định dạng pager hoàn toàn
    /// khả dĩ mà CHƯA CÓ SỐ ĐO nào loại trừ — đều thành ĐỎ. Tức nhãn "đọc được mà lạ" bị xử NGHIÊM HƠN nhãn
    /// rỗng, dù cả hai đều là "không biết đang ở trang nào". Không có bằng chứng thì không kết tội.</para>
    /// </summary>
    [Fact]
    public void CuaDungTrangDich_MaTranNamNhanh()
    {
        var e = MayLuatThuan();

        // (1) SỐ THUẦN khớp ⇒ đạt, không cảnh báo.
        var khop = e.Evaluate("cuaDungTrangDich(\"3\", 3)").AsObject();
        Assert.True(khop.Get("dat").AsBoolean(), khop.Get("ly").AsString());
        Assert.True(khop.Get("canhBao").IsNull());

        // (2) SỐ THUẦN mà LỆCH ⇒ hỏng, và câu lỗi nêu CẢ HAI số trang.
        var lech = e.Evaluate("cuaDungTrangDich(\"2\", 3)").AsObject();
        Assert.False(lech.Get("dat").AsBoolean());
        Assert.Contains("trang 2", lech.Get("ly").AsString());
        Assert.Contains("k=3", lech.Get("ly").AsString());

        // (3) không đọc được nhãn ⇒ ĐẠT kèm cảnh báo (không đỏ oan theo markup).
        foreach (var xau in new[] { "null", "undefined", "\"\"", "\"   \"" })
        {
            var mu = e.Evaluate("cuaDungTrangDich(" + xau + ", 3)").AsObject();
            Assert.True(mu.Get("dat").AsBoolean(), "nhãn `" + xau + "` bị chấm ĐỎ — đỏ oan theo markup");
            Assert.False(mu.Get("canhBao").IsNull(), "nhãn `" + xau + "` lọt im lặng, không cảnh báo");
        }

        // (4) trangDich = 1 ⇒ đạt, KHÔNG cảnh báo — khung 1 không lật đi đâu, cửa bước E vừa chấm xong.
        var motTrang = e.Evaluate("cuaDungTrangDich(null, 1)").AsObject();
        Assert.True(motTrang.Get("dat").AsBoolean());
        Assert.True(motTrang.Get("canhBao").IsNull());

        // (5) nhãn CÓ NỘI DUNG nhưng KHÔNG phải số thuần ⇒ đạt kèm cảnh báo, và cảnh báo nêu NGUYÊN VĂN nhãn
        //     (đó là số đo duy nhất để lượt sau siết cho đúng định dạng thật).
        foreach (var (xau, nguyenVan) in new[]
                 {
                     ("\"Trang 3\"", "Trang 3"), ("\"3/8\"", "3/8"), ("\"‹ 3 ›\"", "‹ 3 ›"), ("\"3.\"", "3."),
                 })
        {
            var la = e.Evaluate("cuaDungTrangDich(" + xau + ", 3)").AsObject();
            Assert.True(la.Get("dat").AsBoolean(),
                "nhãn " + xau + " bị chấm ĐỎ — chưa có số đo nào nói định dạng pager phải là số trơn, "
                + "xử nhãn 'đọc được mà lạ' nghiêm hơn nhãn rỗng là ngược đời");
            Assert.False(la.Get("canhBao").IsNull(), "nhãn " + xau + " lọt im lặng, không cảnh báo");
            Assert.Contains(nguyenVan, la.Get("canhBao").AsString());
        }
    }

    /// <summary>
    /// <b>THỨ TỰ TRONG LUỒNG THẬT: cổng TRƯỚC cú lật, cửa trang TRƯỚC cú tick.</b>
    ///
    /// <para>Ba cửa THUẦN ở trên vô nghĩa nếu bước F gọi thiếu, gọi sai chỗ, hay ai đó bỏ đi. Test này là lưới
    /// duy nhất khoá phần "bắt buộc" trong yêu cầu của user.</para>
    ///
    /// <para><b>⚠ Đo bằng AST, KHÔNG <c>IndexOf</c> chuỗi thô</b> — đúng bài học của <see cref="DemLoiGoiBamSapXep"/>:
    /// chú thích trong thân hàm có nhắc tên ba hàm này, nên <c>IndexOf</c> sẽ bắt trúng comment và cho vị trí
    /// sai. Và <b>chạy trên NGUỒN HÀM, không phải nguồn FILE</b>: lượt DÒ cũng gọi
    /// <c>pageDocDoanhSoMoiDongFlash</c>, đếm cả file ra 3 và test sai ngay từ lúc sinh.</para>
    /// </summary>
    [Fact]
    public void LuongThat_CongTruocLat_CuaTruocTick()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");
        var nut = FlashSaleDoDomTests.MoiNut(new Acornima.Parser().ParseScript(nguon)).ToList();

        // (v1) ĐÚNG MỘT lời gọi `congLatTrang`, và nó đứng TRƯỚC cú lật trang ĐẦU TIÊN.
        var viCong = ViTriGoiHam(nut, "congLatTrang");
        Assert.Single(viCong);
        var viLat = ViTriGoiChay(nut, "pageSangTrangSauFlash");
        Assert.NotEmpty(viLat);
        Assert.True(viCong[0] < viLat[0],
            "cổng `congLatTrang` nằm SAU cú lật trang — luồng đã lật rồi mới hỏi có được lật không");

        // (v2) Có cửa trang-sau-lật, và nó đứng TRƯỚC cú tick "chọn tất cả".
        var viCua = ViTriGoiHam(nut, "cuaKiemTrangSauLat");
        Assert.NotEmpty(viCua);
        var viTick = ViTriGoiChay(nut, "pageTickChonTatCaFlash");
        Assert.NotEmpty(viTick);
        Assert.True(viCua[0] < viTick[0],
            "cửa Doanh số của trang vừa đáp nằm SAU cú tick — tick xong mới kiểm thì đã tick nhầm rồi");

        // (v3) ĐÚNG HAI lượt đọc Doanh số trong luồng thật: một ở bước D (trang 1), một trong vòng lật.
        // Nhiều hơn = có một lượt đọc chưa ai xét; ít hơn = vòng lật lại mù như trước bản này.
        Assert.Equal(2, ViTriGoiChay(nut, "pageDocDoanhSoMoiDongFlash").Count);
    }

    /// <summary>
    /// <b>Ba cửa phải CƯỠNG CHẾ được, không phải trang trí.</b>
    ///
    /// <para><see cref="LuongThat_CongTruocLat_CuaTruocTick"/> mới khoá "CÓ GỌI + ĐÚNG THỨ TỰ" — và chừng đó
    /// chưa đủ: bốn phép phá dưới đây biến cả ba cửa thành đồ trang trí mà lưới ấy vẫn xanh trọn vẹn.
    /// <list type="number">
    ///   <item>xoá <c>if (!cong.dat) { return … }</c> nhưng giữ lời gọi ⇒ cổng vẫn "được gọi", chỉ là không
    ///   chặn ai;</item>
    ///   <item>xoá <c>if (!cuaTrang.dat) { return … }</c> ⇒ cửa từng trang đọc xong rồi vứt kết quả;</item>
    ///   <item>gọi <c>cuaKiemTrangSauLat(docTrang, null)</c> ⇒ vế NỐI TIẾP tắt vĩnh viễn, mỗi trang lại chấm
    ///   một mình, đúng cái lỗ mà bản vá này sinh ra để bịt;</item>
    ///   <item>xoá <c>soCuoiTrangTruoc = cuaTrang.soCuoi;</c> ⇒ mắt xích đứng yên ở giá trị trang 1, từ trang 3
    ///   trở đi so với một mốc đã cũ.</item>
    /// </list>
    /// Bốn ca ấy đều IM LẶNG trên nhật ký: mọi bước vẫn <c>ok</c>. Test này khoá đúng phần CƯỠNG CHẾ.</para>
    ///
    /// <para>Đo bằng AST (<see cref="FlashSaleDoDomTests.MoiNut"/>), không <c>Contains</c> chuỗi thô — chú thích
    /// trong thân hàm nhắc nguyên văn cả bốn mẩu code trên, nên phép so chuỗi sẽ bắt trúng comment và xanh giả.</para>
    /// </summary>
    [Fact]
    public void LuongThat_CuaCoRangCuong_KhongPhaiTrangTri()
    {
        var nguon = FlashSaleDoDomTests.NguonHam(FileFlash, "chayFlashSaleChoTabDangMo");
        var nut = FlashSaleDoDomTests.MoiNut(new Acornima.Parser().ParseScript(nguon)).ToList();

        // (a) + (b) — mỗi cửa phải có một `if (!<bien>.dat)` mà thân CÓ `return`: gọi xong phải THOÁT được.
        foreach (var bien in new[] { "cong", "cuaTrang" })
        {
            Assert.True(CoIfPhuDinhRoiReturn(nut, bien, "dat"),
                "không thấy `if (!" + bien + ".dat) { … return … }` trong luồng thật — cửa được gọi nhưng "
                + "không chặn được gì, mọi bước vẫn ok và lượt chạy tick tiếp như chưa có cửa nào");
        }

        // (c) — mắt xích phải được TRUYỀN VÀO cửa. Đối số 2 là literal/null ⇒ vế nối tiếp giữa trang tắt hẳn.
        var goiCua = nut.OfType<CallExpression>()
            .Where(g => g.Callee is Identifier { Name: "cuaKiemTrangSauLat" })
            .ToList();
        Assert.NotEmpty(goiCua);
        foreach (var g in goiCua)
        {
            Assert.True(g.Arguments.Count >= 2, "`cuaKiemTrangSauLat` gọi thiếu đối số mắt xích");
            Assert.True(g.Arguments[1] is Identifier { Name: "soCuoiTrangTruoc" },
                "đối số 2 của `cuaKiemTrangSauLat` không phải biến `soCuoiTrangTruoc` — vế NỐI TIẾP giữa hai "
                + "trang tắt im lặng, mỗi trang lại tự chấm một mình đúng như trước bản vá");
        }

        // (d) — và mắt xích phải ĐƯỢC ĐẨY TỚI sau mỗi trang đạt, không đứng yên ở giá trị trang 1.
        Assert.True(
            nut.OfType<AssignmentExpression>().Any(a =>
                a.Left is Identifier { Name: "soCuoiTrangTruoc" }
                && a.Right is MemberExpression
                {
                    Object: Identifier { Name: "cuaTrang" }, Property: Identifier { Name: "soCuoi" },
                }),
            "không thấy `soCuoiTrangTruoc = cuaTrang.soCuoi` — mắt xích đứng yên ở trang 1, từ trang 3 trở đi "
            + "vế nối tiếp so với một mốc đã cũ");
    }

    /// <summary>Có một <c>if (!bien.truong …)</c> mà thân chứa <c>return</c> không? — AST, không so chuỗi.</summary>
    private static bool CoIfPhuDinhRoiReturn(IEnumerable<Node> moiNut, string bien, string truong)
        => moiNut.OfType<IfStatement>().Any(nhanh =>
            FlashSaleDoDomTests.MoiNut(nhanh.Test).OfType<UnaryExpression>().Any(u =>
                u.Operator == Acornima.Operator.LogicalNot
                && u.Argument is MemberExpression m
                && m.Object is Identifier o && o.Name == bien
                && m.Property is Identifier p && p.Name == truong)
            && FlashSaleDoDomTests.MoiNut(nhanh.Consequent).OfType<ReturnStatement>().Any());

    /// <summary>Vị trí nguồn của mọi lời gọi <c>ten(...)</c> — AST, không regex.</summary>
    private static List<int> ViTriGoiHam(IEnumerable<Node> moiNut, string ten)
        => moiNut.OfType<CallExpression>()
            .Where(g => g.Callee is Identifier id && id.Name == ten)
            .Select(g => g.Range.Start)
            .OrderBy(x => x)
            .ToList();

    /// <summary>Vị trí nguồn của mọi lời gọi <c>chay(tenPageFunc, …)</c> — AST, không regex.</summary>
    private static List<int> ViTriGoiChay(IEnumerable<Node> moiNut, string tenPageFunc)
        => moiNut.OfType<CallExpression>()
            .Where(g => g.Callee is Identifier { Name: "chay" }
                        && g.Arguments.Count >= 1
                        && g.Arguments[0] is Identifier pf && pf.Name == tenPageFunc)
            .Select(g => g.Range.Start)
            .OrderBy(x => x)
            .ToList();
}
