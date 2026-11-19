using Shopee.Core.MassUpload;
using static Shopee.Core.Tests.MassUpload.MassUploadTestData;

namespace Shopee.Core.Tests.MassUpload;

/// <summary>GĐ2b: <see cref="MassUploadMapper"/> với resolver 3 trạng thái (<see cref="ImageResolution"/>): ảnh SP Failed bị bỏ
/// (ảnh sau lên thay, kể cả bìa), Pending → Skip, hết ảnh → Skip; ảnh lựa chọn Failed → bỏ cả cột; bảng size Failed/Pending
/// → Skip.</summary>
public sealed class MassUploadMapperImageStateTests
{
    private static string Cell(IReadOnlyDictionary<string, MassUploadCell> row, string code) =>
        row.TryGetValue(code, out var c) ? c.Text : "";

    /// <summary>Resolver: <paramref name="failed"/> → Failed, <paramref name="pending"/> → Pending, còn lại Ready.</summary>
    private static Func<string, ImageResolution> R(string[]? failed = null, string[]? pending = null) => id =>
        failed?.Contains(id) == true ? ImageResolution.Failed("ảnh gốc 404")
        : pending?.Contains(id) == true ? ImageResolution.Pending
        : ImageResolution.Ready(ImageBase + id);

    private static MassUploadMapResult Map(ShopeeListing l, Func<string, ImageResolution> resolve, MassUploadLimits? lim = null) =>
        MassUploadMapper.Map(l, Defaults, AdvancedSchema, resolve, limits: lim);

    [Fact]
    public void AnhSpFailed_BiBo_AnhSauLenLamBia_CoWarning()
    {
        var l = OneTier([10_000, 11_000], images: ["img-0", "img-1", "img-2"]);

        var p = Assert.IsType<MassUploadProduct>(Map(l, R(failed: ["img-0"])));

        Assert.Equal(ImageBase + "img-1", Cell(p.Rows[0], MassUploadMapper.ColCoverImage));
        Assert.Equal(ImageBase + "img-2", Cell(p.Rows[0], MassUploadMapper.ColImagePrefix + 1));
        Assert.Equal("", Cell(p.Rows[0], MassUploadMapper.ColImagePrefix + 2));
        Assert.Contains(p.Warnings, w => w.Contains("ảnh img-0 lỗi") && w.Contains("bỏ"));
    }

    [Fact]
    public void AnhSpFailed_AnhThuMuoiLenThay_KhiDuTran9()
    {
        var imgs = Enumerable.Range(0, 11).Select(i => "img-" + i).ToList();
        var l = OneTier([10_000], images: imgs);

        var p = Assert.IsType<MassUploadProduct>(Map(l, R(failed: ["img-3"], pending: ["img-10"])));

        // img-3 bị bỏ → img-9 vào chỗ ảnh thứ 9; img-10 (Pending) không cần tới nên không làm Skip.
        Assert.Equal(ImageBase + "img-9", Cell(p.Rows[0], MassUploadMapper.ColImagePrefix + 8));
        Assert.Contains(p.Warnings, w => w.Contains("ảnh SP 11 → 9"));
    }

    [Fact]
    public void AnhSpPending_Skip_ChuaSanSang()
    {
        var r = Map(OneTier([10_000], images: ["img-0", "img-1"]), R(failed: ["img-0"], pending: ["img-1"]));
        Assert.Equal("ảnh chưa sẵn sàng: img-1", Assert.IsType<MassUploadSkip>(r).Reason);
    }

    [Fact]
    public void MoiAnhSpDeuFailed_Skip()
    {
        var r = Map(OneTier([10_000], images: ["img-0", "img-1"]), R(failed: ["img-0", "img-1"]));
        Assert.Contains("không còn ảnh", Assert.IsType<MassUploadSkip>(r).Reason);
    }

    [Fact]
    public void AnhLuaChonFailed_BoCaCot_CoWarning_SpVanDang()
    {
        var p = Assert.IsType<MassUploadProduct>(Map(OneTier([10_000, 11_000]), R(failed: ["opt-1"])));

        Assert.All(p.Rows, row => Assert.False(row.ContainsKey(MassUploadMapper.ColTier1Image)));
        Assert.Contains(p.Warnings, w => w.Contains("ảnh lựa chọn lỗi 1/2"));
    }

    [Fact]
    public void AnhLuaChonPending_Skip()
    {
        var r = Map(OneTier([10_000, 11_000]), R(pending: ["opt-0"]));
        Assert.Equal("ảnh chưa sẵn sàng: opt-0", Assert.IsType<MassUploadSkip>(r).Reason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BangSizeFailedHoacPending_Skip(bool failed)
    {
        var key = Defaults.SizeChartImageKey;
        var r = Map(OneTier([10_000]), failed ? R(failed: [key]) : R(pending: [key]));
        var reason = Assert.IsType<MassUploadSkip>(r).Reason;
        Assert.Contains(failed ? "ảnh bảng size lỗi" : "ảnh chưa sẵn sàng", reason);
    }

    [Fact]
    public void ResolverCu_NullLaPending()
    {
        var r = MassUploadMapper.Map(OneTier([10_000]), Defaults, AdvancedSchema, id => id == "img-1" ? null : ImageBase + id);
        Assert.Equal("ảnh chưa sẵn sàng: img-1", Assert.IsType<MassUploadSkip>(r).Reason);
    }
}
