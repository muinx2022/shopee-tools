using Shopee.Core.BigSeller;

namespace Shopee.Core.Tests;

/// <summary>
/// <see cref="ImportShopMatcher.Pick"/> chọn shop trong modal "Import to Stores". Lỗi cũ: so khớp kiểu
/// "chứa" rồi lấy phần tử đầu → "shop1" chọn nhầm "shop10". Không chắc thì phải trả -1/-2 (KHÔNG import).
/// </summary>
public sealed class ImportShopMatcherTests
{
    [Fact]
    public void Pick_Shop1_DoesNotPickShop10_WhenExactExists()
    {
        Assert.Equal(1, ImportShopMatcher.Pick(["shop10", "shop1"], "shop1"));
    }

    [Fact]
    public void Pick_Shop1_NotFound_WhenOnlyShop10()
    {
        // "shop1" chỉ là tiền tố của "shop10" → không có ranh giới từ → không khớp.
        Assert.Equal(ImportShopMatcher.NotFound, ImportShopMatcher.Pick(["shop10"], "shop1"));
        Assert.Equal(-1, ImportShopMatcher.Pick(["shop10", "myshop1"], "shop1"));
    }

    [Fact]
    public void Pick_IgnoresDiacriticsAndCase()
    {
        Assert.Equal(1, ImportShopMatcher.Pick(["Cửa Hàng Khác", "CỬA HÀNG ĐẸP"], "cua hang dep"));
        Assert.Equal(0, ImportShopMatcher.Pick(["Đồ Chơi"], "do choi"));
    }

    [Fact]
    public void Pick_CompactEquality_IgnoresPunctuationAndSpaces()
    {
        Assert.Equal(0, ImportShopMatcher.Pick(["shop 1"], "shop1"));
        Assert.Equal(0, ImportShopMatcher.Pick(["Shop-A_1"], "shop a 1"));
    }

    [Fact]
    public void Pick_StripsBracketSuffix_MatchesLabelWithSuffix()
    {
        // Đổi từ Pick_ContainsWithWordBoundary_...: tầng 3 không còn "chứa + ranh giới" mà là "bỏ hậu tố (...)/[...] rồi so bằng".
        // Nhãn thật trên BigSeller hay kèm "(Shopee)".
        Assert.Equal(0, ImportShopMatcher.Pick(["shop.1 (Shopee)"], "shop.1"));
        Assert.Equal(1, ImportShopMatcher.Pick(["shop.10 (Shopee)", "shop.1 (Shopee)"], "shop.1"));
        Assert.Equal(0, ImportShopMatcher.Pick(["shop.1 [VN] (Shopee)"], "shop.1"));   // nhiều hậu tố liền nhau
    }

    [Fact]
    public void Pick_Ambiguous_WhenTwoLabelsEqualNameAfterStrippingSuffix()
    {
        // Đổi từ Pick_Ambiguous_WhenTwoLabelsContainNameAtSameTier (assertion giữ nguyên, chỉ đổi tên theo luật tầng 3 mới).
        Assert.Equal(ImportShopMatcher.Ambiguous, ImportShopMatcher.Pick(["abc (VN)", "abc (SG)"], "abc"));
        Assert.Equal(-2, ImportShopMatcher.Pick(["abc (VN)", "abc (SG)", "xyz"], "abc"));
    }

    [Fact]
    public void Pick_ExactTierWinsOverSuffixTier()
    {
        // Đổi từ Pick_ExactTierWinsOverContainsTier. Có nhãn bằng đúng tên thì lấy nó, dù nhãn khác cũng bằng tên
        // sau khi bỏ hậu tố (không báo mơ hồ).
        Assert.Equal(1, ImportShopMatcher.Pick(["abc (VN)", "abc"], "abc"));
    }

    [Fact]
    public void Pick_LabelThatOnlyContainsName_IsNotAMatch()
    {
        // Lỗi cũ: "Sully Store 2" bị chọn khi tìm "Sully Store".
        Assert.Equal(-1, ImportShopMatcher.Pick(["Sully Store 2"], "Sully Store"));
        // Hậu tố "(Shopee)" không biến "shop.1.2" thành "shop.1".
        Assert.Equal(-1, ImportShopMatcher.Pick(["shop.1.2 (Shopee)"], "shop.1"));
    }

    [Fact]
    public void Pick_SuffixTier_CompactMatchesPunctuationDifference()
    {
        // Bỏ hậu tố rồi so theo compact: "shop-a" ≈ "shop a".
        Assert.Equal(0, ImportShopMatcher.Pick(["Shop-A (Shopee)"], "shop a"));
    }

    [Fact]
    public void Pick_SuffixTier_PicksOnlyTheExactNameAmongSimilarLabels()
    {
        Assert.Equal(0, ImportShopMatcher.Pick(["Sully Store (Shopee)", "Sully Store 2 (Shopee)"], "Sully Store"));
    }

    [Fact]
    public void Pick_NonAsciiLetters_AreNotCollapsed()
    {
        // Compact giữ mọi chữ/số Unicode: "店铺1" và "小店1" KHÔNG cùng thành "1".
        Assert.Equal(-1, ImportShopMatcher.Pick(["店铺1"], "小店1"));
        Assert.Equal(1, ImportShopMatcher.Pick(["店铺1", "小店1"], "小店1"));
    }

    [Fact]
    public void Pick_DuplicateExactLabels_AreAmbiguous()
    {
        Assert.Equal(ImportShopMatcher.Ambiguous, ImportShopMatcher.Pick(["abc", "ABC"], "abc"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Pick_EmptyName_IsNotFound(string? name)
    {
        Assert.Equal(ImportShopMatcher.NotFound, ImportShopMatcher.Pick(["shop1", ""], name!));
    }

    [Fact]
    public void Pick_NoLabels_IsNotFound()
    {
        Assert.Equal(ImportShopMatcher.NotFound, ImportShopMatcher.Pick([], "shop1"));
    }
}
