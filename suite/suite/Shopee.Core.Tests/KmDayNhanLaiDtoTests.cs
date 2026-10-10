using System.Text.Json;
using Shopee.Core.Coordination;

namespace Shopee.Core.Tests;

/// <summary>
/// <b>Hợp đồng JSON của phản hồi <c>POST /orders/km</c></b> (X1, 22/08/2026) — cái khép vòng phản hồi hồ sơ KM.
/// <para><b>Nhánh DEGRADE là phần bắt buộc:</b> hub CŨ (đã deploy) trả <c>{"profiles": n}</c>, không hề có bản
/// gộp. Client mới đọc body đó <b>không được ném và không được đổi gì</b> — deploy lệch bản giữa hub và client
/// là chuyện bình thường, và một lỗi ném ở đây làm chết luôn nhịp đồng bộ. Luật chống lệch bản là "trường mới
/// phải là property <c>init</c> trong thân record" (xem khối chú thích trên <c>OrdersAccountItem</c>); test này
/// đo đúng cái luật đó chứ không đọc lại code.</para>
/// </summary>
public class KmDayNhanLaiDtoTests
{
    private static readonly JsonSerializerOptions Opts = new(JsonSerializerDefaults.Web);

    /// <summary>Body của hub CŨ ⇒ parse được, <c>HoSoDaGop</c> RỖNG (không null) ⇒ nơi gọi bỏ qua.</summary>
    [Fact]
    public void BodyCuaHubCu_ThiParseDuoc_VaHoSoDaGopRong()
    {
        var doc = JsonSerializer.Deserialize<KmProfilesPushResponse>("""{"profiles":3}""", Opts);

        Assert.NotNull(doc);
        Assert.Equal(3, doc!.Profiles);
        Assert.NotNull(doc.HoSoDaGop);
        Assert.Empty(doc.HoSoDaGop);
    }

    /// <summary>Body RỖNG HẲN (hub trả 200 mà không có gì) ⇒ vẫn không ném.</summary>
    [Fact]
    public void BodyRong_ThiVanKhongNem()
    {
        var doc = JsonSerializer.Deserialize<KmProfilesPushResponse>("{}", Opts);

        Assert.NotNull(doc);
        Assert.Empty(doc!.HoSoDaGop);
    }

    /// <summary>
    /// <b>Cửa đọc body của client KHÔNG ĐƯỢC NÉM với bất kỳ thứ gì hub cũ / tunnel hỏng trả về.</b> Ném ở đây
    /// là lượt đẩy bị coi như HỎNG (bật lại cờ bẩn, giãn nhịp) trong khi hub đã ghi xong — tức nhịp đồng bộ tự
    /// làm hỏng chính nó vì một cái body nó không cần.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html>404 not found</html>")]      // tunnel/proxy trả trang HTML
    [InlineData("{\"profiles\":3}")]                 // hub CŨ
    [InlineData("{\"profiles\":3,\"hoSoDaGop\":null}")]
    [InlineData("[1,2,3]")]                          // JSON hợp lệ nhưng sai hình dạng
    public void BodyLa_ThiTraKhongCoBanGop_VaKhongNem(string? body)
    {
        var kq = HubClient.DocBanGopKm(body);

        Assert.True(kq is null || kq.Count == 0);
    }

    /// <summary>Body của hub MỚI ⇒ đọc ra đúng danh sách (vế đối chứng: thiếu nó thì một bản "lúc nào cũng trả
    /// null" cũng xanh, mà bản đó tắt hẳn vòng phản hồi).</summary>
    [Fact]
    public void BodyHubMoi_ThiDocRaBanGop()
    {
        var kq = HubClient.DocBanGopKm(
            """{"profiles":1,"hoSoDaGop":[{"login":"sub@shopee.vn","tuDong":true,"phanTram":25}]}""");

        var p = Assert.Single(kq!);
        Assert.Equal("sub@shopee.vn", p.Login);
        Assert.True(p.TuDong);
        Assert.Equal(25, p.PhanTram);
    }

    /// <summary>Vòng tròn ĐẦY ĐỦ: hub mới ghi ra sao thì client đọc lại đúng vậy — gồm cả mốc UTC và chủ trì.</summary>
    [Fact]
    public void HubMoi_ThiVongTronGiuDuTruong()
    {
        var moc = new DateTimeOffset(2026, 8, 22, 3, 0, 0, TimeSpan.Zero);
        var goc = new KmProfilesPushResponse
        {
            Profiles = 1,
            HoSoDaGop =
            [
                new KmProfileItem("sub@shopee.vn")
                {
                    TuDong = true,
                    SoNgay = 3,
                    PhanTram = 25,
                    GioBatDau = "08:00",
                    GioKetThuc = "09:00",
                    OwnerMachine = "may-mot:orders",
                    CfgUpdatedAt = moc,
                    LanChayCuoi = moc.AddDays(-3),
                },
            ],
        };

        var ve = JsonSerializer.Deserialize<KmProfilesPushResponse>(JsonSerializer.Serialize(goc, Opts), Opts);

        Assert.NotNull(ve);
        var p = Assert.Single(ve!.HoSoDaGop);
        Assert.True(p.TuDong);
        Assert.Equal(25, p.PhanTram);
        Assert.Equal("may-mot:orders", p.OwnerMachine);
        Assert.Equal(moc, p.CfgUpdatedAt);
        Assert.Equal(moc.AddDays(-3), p.LanChayCuoi);
    }
}
