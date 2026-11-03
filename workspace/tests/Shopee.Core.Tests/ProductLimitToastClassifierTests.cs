using Shopee.Core.BigSeller;

namespace Shopee.Core.Tests;

/// <summary>
/// <see cref="ProductLimitToastClassifier.IsProductLimitToast"/>: toast "số sản phẩm vượt giới hạn" → dừng cả shop; mọi toast
/// giới hạn của trường/media (tên, giá, tồn kho, ảnh…), cảnh báo sắp tới và toast thường phải trả false để không dừng oan.
/// </summary>
public sealed class ProductLimitToastClassifierTests
{
    // ── Câu kiểm do phiên chính giao ─────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("Số lượng sản phẩm vượt quá giới hạn")]
    [InlineData("Vượt quá giới hạn sản phẩm được phép thêm")]
    [InlineData("The number of products exceeds the limit")]
    [InlineData("The shop has reached its listing limit")]
    [InlineData("The shop has reached its listing limit, please delete some products or media")]
    [InlineData("Số sản phẩm vượt giới hạn")]
    [InlineData("Đã đạt đến giới hạn sản phẩm")]
    [InlineData("Exceeded the number of products allowed")]
    [InlineData("Over the product limit")]
    [InlineData("Vượt quá giới hạn đăng SP")]
    [InlineData("Số lượng sản phẩm đã đạt giới hạn, vui lòng mở tài khoản cao cấp")]
    [InlineData("  SỐ LƯỢNG   SẢN PHẨM\nVƯỢT QUÁ GIỚI HẠN  ")]
    // Vòng 3: lời khuyên "thử lại" đi kèm KHÔNG còn chặn; "100%" không phải dưới 100; từ/tín hiệu mới.
    [InlineData("Số lượng sản phẩm đã đạt giới hạn, vui lòng xóa bớt sản phẩm và thử lại.")]
    [InlineData("Product limit exceeded. Please delete some products and try again.")]
    [InlineData("Số lượng sản phẩm đã vượt quá giới hạn, vui lòng gia hạn gói.")]
    [InlineData("Đã đạt giới hạn sản phẩm của gói (100%)")]
    [InlineData("Exceeding the product limit")]
    [InlineData("Vượt hạn mức sản phẩm")]
    [InlineData("Number of items exceeds the limit")]
    [InlineData("Số mặt hàng vượt mức giới hạn")]
    [InlineData("Đã quá giới hạn sản phẩm")]
    [InlineData("Hết hạn mức sản phẩm")]
    public void ProductLimitText_IsMatched(string text)
        => Assert.True(ProductLimitToastClassifier.IsProductLimitToast(text));

    [Theory]
    [InlineData("Tên sản phẩm vượt quá giới hạn 120 ký tự")]
    [InlineData("Product name exceeds the limit")]
    [InlineData("Product title exceeds the limit")]
    [InlineData("Tiêu đề sản phẩm vượt quá giới hạn")]
    [InlineData("Trọng lượng sản phẩm vượt quá giới hạn của kênh vận chuyển")]
    [InlineData("Product weight exceeds the limit of the selected shipping channel")]
    [InlineData("Giá sản phẩm vượt quá giới hạn cho phép")]
    [InlineData("Product price exceeds the maximum")]
    [InlineData("Product stock exceeds the maximum")]
    [InlineData("Số lượng tồn kho sản phẩm tối đa là 999999")]
    [InlineData("Thuộc tính sản phẩm vượt quá giới hạn")]
    [InlineData("Product update limit exceeded, please try again later")]
    [InlineData("Bạn sắp đạt giới hạn sản phẩm")]
    [InlineData("You are about to reach the product limit")]
    [InlineData("You have reached 80% of your product limit")]
    [InlineData("Chưa đạt giới hạn sản phẩm")]
    [InlineData("Lưu sản phẩm thành công. Bạn sắp đạt giới hạn sản phẩm")]
    [InlineData("Unlimited products available, upgrade before you reach")]
    [InlineData("The Media Center space is insufficient")]
    [InlineData("Vượt quá giới hạn cho phép")]
    [InlineData("Request exceeds the limit")]
    [InlineData("Lưu thành công")]
    [InlineData("Successfully saved the product")]
    [InlineData("Thanh toán thành công")]
    // Vòng 3: giới hạn của trường/media/tần suất; "listing limit" thiếu động từ đạt/vượt; "over" không liền "limit".
    [InlineData("Ảnh sản phẩm vượt quá giới hạn")]
    [InlineData("Product picture exceeds the limit")]
    [InlineData("Product videos exceed the limit")]
    [InlineData("Product names exceed the limit")]
    [InlineData("Product variants exceed the limit")]
    [InlineData("Mã sản phẩm vượt quá giới hạn")]
    [InlineData("Chiều dài sản phẩm vượt quá giới hạn")]
    [InlineData("Product quantity exceeds the maximum")]
    [InlineData("Đã đạt giới hạn cập nhật sản phẩm hôm nay")]
    [InlineData("Request limit reached for product API")]
    [InlineData("Số lượng sản phẩm được chọn vượt quá giới hạn")]
    [InlineData("Hover over the product to view, no limit")]
    [InlineData("Listing limit settings saved")]
    public void OtherToasts_AreNotMatched(string text)
        => Assert.False(ProductLimitToastClassifier.IsProductLimitToast(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrBlank_IsNotMatched(string? text)
        => Assert.False(ProductLimitToastClassifier.IsProductLimitToast(text));

    // ── Từng nhánh tín hiệu giới hạn (mỗi câu CHỈ đi qua đúng nhánh ghi chú) ──────────────────────────────────
    [Theory]
    [InlineData("Số sản phẩm vượt quá giới hạn")]                   // vuot qua gioi han
    [InlineData("Số sản phẩm vượt giới hạn")]                       // vuot gioi han (bỏ "qua")
    [InlineData("Vượt quá số lượng sản phẩm cho phép")]             // vuot qua so luong
    [InlineData("Vượt số lượng sản phẩm cho phép")]                 // vuot so luong
    [InlineData("Shop đạt giới hạn sản phẩm")]                      // dat gioi han
    [InlineData("Shop đạt đến giới hạn sản phẩm")]                  // dat den gioi han
    [InlineData("Shop đạt tới giới hạn sản phẩm")]                  // dat toi gioi han
    [InlineData("Giới hạn đăng sản phẩm của shop đã đủ")]           // gioi han dang
    [InlineData("Products exceed the limit")]                       // exceed + limit
    [InlineData("Products exceed the maximum")]                     // exceed + maximum
    [InlineData("Product quota exceeded for this shop")]            // exceed + quota (quota đứng TRƯỚC exceed)
    [InlineData("Exceeded the number of products allowed")]         // exceed + number
    [InlineData("Shop reached the product limit")]                  // reach + limit
    [InlineData("You have reached the maximum of products")]        // reach + maximum
    [InlineData("Over the product limit")]                          // over the … limit
    [InlineData("Listing quota exceeded")]                          // "listing" số ít là từ chỉ SP
    [InlineData("Listings exceed the maximum allowed")]             // "listings" số nhiều là từ chỉ SP
    [InlineData("Quá giới hạn: vượt giới hạn SP")]                  // "sp" là từ chỉ SP
    [InlineData("The shop has exceeded its listing limit")]         // chỉ nhánh "listing limit" + exceed* (không tín hiệu nào khác)
    [InlineData("Shop đã vượt listing limit")]                      // "listing limit" + vuot
    [InlineData("Shop đạt listing limit")]                          // "listing limit" + dat
    [InlineData("Shop over listing limit")]                         // "listing limit" + over (không "the" nên OverLimit không góp)
    [InlineData("Exceeding the product limit")]                     // exceeding + limit
    [InlineData("Vượt mức giới hạn SP")]                            // vuot muc gioi han
    [InlineData("Vượt hạn mức sản phẩm")]                           // vuot han muc
    [InlineData("Đã quá giới hạn sản phẩm")]                        // da qua gioi han
    [InlineData("Hết hạn mức sản phẩm")]                            // het han muc
    [InlineData("Number of items exceeds the limit")]               // "items" là từ chỉ SP
    [InlineData("Số mặt hàng vượt giới hạn")]                       // "mat hang" là từ chỉ SP
    public void EachLimitBranch_IsMatched(string text)
        => Assert.True(ProductLimitToastClassifier.IsProductLimitToast(text));

    // Có tín hiệu giới hạn nhưng thiếu từ chỉ sản phẩm → KHÔNG khớp.
    [Theory]
    [InlineData("Vượt quá giới hạn cho phép")]
    [InlineData("Đã đạt giới hạn")]
    [InlineData("Giới hạn đăng đã đủ")]
    [InlineData("Request exceeds the limit")]
    [InlineData("You have reached the maximum")]
    [InlineData("Over the limit")]
    public void LimitWithoutProductWord_IsNotMatched(string text)
        => Assert.False(ProductLimitToastClassifier.IsProductLimitToast(text));

    // "listing limit" KHÔNG còn tự đủ: phải đi cùng reach*/exceed*/dat/vuot/over. (Câu cũ "The shop has hit its listing limit"
    // trước đây khớp nhờ nhánh tự đủ; nay "hit" không nằm trong danh sách động từ → không khớp — xung đột báo lại phiên chính.)
    [Theory]
    [InlineData("The shop has hit its listing limit")]
    [InlineData("Listing limit settings saved")]
    [InlineData("Listing limit")]
    [InlineData("Check your listing limit in the plan page")]
    public void ListingLimitWithoutVerb_IsNotMatched(string text)
        => Assert.False(ProductLimitToastClassifier.IsProductLimitToast(text));

    // Có từ chỉ SP nhưng không có tín hiệu giới hạn → KHÔNG khớp ("so luong" + "toi da" không còn là tín hiệu).
    [Theory]
    [InlineData("Lưu sản phẩm lỗi")]
    [InlineData("Product saved")]
    [InlineData("Số lượng sản phẩm tối đa là 5000")]
    public void ProductWithoutLimitSignal_IsNotMatched(string text)
        => Assert.False(ProductLimitToastClassifier.IsProductLimitToast(text));

    // ── Từng từ cảnh báo/tạm thời (a): chặn một câu vốn khớp ──────────────────────────────────────────────────
    [Theory]
    [InlineData("Số lượng sản phẩm vượt quá giới hạn", " (bạn sắp hết lượt)")]               // sap
    [InlineData("Số lượng sản phẩm vượt quá giới hạn", " (chưa đạt mức cao)")]                // chua dat
    [InlineData("Số lượng sản phẩm vượt quá giới hạn", " (chưa vượt ngưỡng)")]                // chua vuot
    [InlineData("The number of products exceeds the limit", ", you are about to upgrade")]    // about to
    [InlineData("Số lượng sản phẩm vượt quá giới hạn", ", thao tác thành công")]              // thanh cong
    [InlineData("Số lượng sản phẩm vượt quá giới hạn", " trong hôm nay")]                     // hom nay
    [InlineData("Số lượng sản phẩm vượt quá giới hạn", " mỗi phút")]                          // moi phut
    [InlineData("The number of products exceeds the limit", ", 10 requests per minute")]      // per minute
    [InlineData("The number of products exceeds the limit", " today")]                        // today
    [InlineData("The number of products exceeds the limit", ", request limit applies")]       // request limit
    [InlineData("Số lượng sản phẩm vượt quá giới hạn", " (giới hạn cập nhật sản phẩm)")]      // cap nhat san pham
    [InlineData("The number of products exceeds the limit", ", update limit applies")]        // update limit
    [InlineData("The number of products exceeds the limit", ", 99.5% used")]                  // \d+(\.\d+)?% dưới 100
    [InlineData("The number of products exceeds the limit", ", success")]                     // success
    [InlineData("The number of products exceeds the limit", ", successfully")]                // success (đầu từ)
    [InlineData("The number of products exceeds the limit", ", unlimited plan")]              // unlimited
    [InlineData("The number of products exceeds the limit", ", 80% used")]                    // \d+%
    [InlineData("The number of products exceeds the limit", ", 80 % used")]                   // \d+ %
    public void WarningOrTransientWord_BlocksOtherwiseMatchingText(string baseText, string extra)
    {
        Assert.True(ProductLimitToastClassifier.IsProductLimitToast(baseText));
        Assert.False(ProductLimitToastClassifier.IsProductLimitToast(baseText + extra));
    }

    // Lời khuyên "thử lại"/"try again" đi kèm KHÔNG chặn nữa; phần trăm từ 100 trở lên không chặn; "chưa" trơn không chặn.
    [Theory]
    [InlineData("Số lượng sản phẩm vượt quá giới hạn", ", vui lòng thử lại")]
    [InlineData("The number of products exceeds the limit", ", please try again")]
    [InlineData("The number of products exceeds the limit", ", try again later")]
    [InlineData("The number of products exceeds the limit", " (100% used)")]
    [InlineData("The number of products exceeds the limit", " (150% used)")]
    [InlineData("The number of products exceeds the limit", " (100.5% used)")]
    [InlineData("Số lượng sản phẩm vượt quá giới hạn", " (chưa xử lý)")]
    public void RemovedTransientWords_DoNotBlock(string baseText, string extra)
        => Assert.True(ProductLimitToastClassifier.IsProductLimitToast(baseText + extra));

    // "listing limit" thắng từ loại trừ (c) nhưng KHÔNG thắng từ cảnh báo (a).
    [Fact]
    public void ListingLimit_BeatsExclusion_ButNotWarning()
    {
        Assert.True(ProductLimitToastClassifier.IsProductLimitToast("Reached the listing limit, delete media or images"));
        Assert.False(ProductLimitToastClassifier.IsProductLimitToast("You are about to reach the listing limit"));
        Assert.False(ProductLimitToastClassifier.IsProductLimitToast("You have used 90% of the listing limit"));
    }

    // ── Từng từ loại trừ (c): thêm đúng từ đó vào câu vốn khớp → false ───────────────────────────────────────
    [Theory]
    [InlineData("tên")]
    [InlineData("tiêu đề")]
    [InlineData("mô tả")]
    [InlineData("ký tự")]
    [InlineData("giá")]
    [InlineData("tồn kho")]
    [InlineData("trọng lượng")]
    [InlineData("cân nặng")]
    [InlineData("kích thước")]
    [InlineData("hình ảnh")]
    [InlineData("video")]
    [InlineData("media")]
    [InlineData("dung lượng")]
    [InlineData("biến thể")]
    [InlineData("phân loại")]
    [InlineData("thuộc tính")]
    [InlineData("ảnh")]
    [InlineData("mã sản phẩm")]
    [InlineData("chiều dài")]
    [InlineData("chiều rộng")]
    [InlineData("chiều cao")]
    [InlineData("SKU")]
    [InlineData("được chọn")]
    public void ExcludedVietnameseWord_BlocksOtherwiseMatchingText(string word)
    {
        const string baseText = "Sản phẩm vượt quá giới hạn";
        Assert.True(ProductLimitToastClassifier.IsProductLimitToast(baseText));
        Assert.False(ProductLimitToastClassifier.IsProductLimitToast($"{baseText} {word}"));
    }

    // "giá" chặn, nhưng "gia hạn" (gia hạn gói) thì không; "gia" lẻ không đi liền "hạn" vẫn chặn.
    [Theory]
    [InlineData(" vui lòng gia hạn", true)]
    [InlineData(", hãy gia hạn gói", true)]
    [InlineData(" giá", false)]
    [InlineData(" gia", false)]
    [InlineData(" gia nhập", false)]
    public void Gia_BlocksExceptWhenFollowedByHan(string extra, bool stillMatched)
        => Assert.Equal(stillMatched, ProductLimitToastClassifier.IsProductLimitToast("Số lượng sản phẩm vượt quá giới hạn" + extra));

    [Theory]
    [InlineData("name")]
    [InlineData("title")]
    [InlineData("description")]
    [InlineData("character")]
    [InlineData("characters")]
    [InlineData("price")]
    [InlineData("stock")]
    [InlineData("weight")]
    [InlineData("size")]
    [InlineData("image")]
    [InlineData("images")]
    [InlineData("attribute")]
    [InlineData("attributes")]
    [InlineData("variation")]
    [InlineData("variations")]
    [InlineData("video")]
    [InlineData("media")]
    [InlineData("names")]
    [InlineData("titles")]
    [InlineData("descriptions")]
    [InlineData("prices")]
    [InlineData("videos")]
    [InlineData("variant")]
    [InlineData("variants")]
    [InlineData("picture")]
    [InlineData("pictures")]
    [InlineData("photo")]
    [InlineData("photos")]
    [InlineData("sku")]
    [InlineData("length")]
    [InlineData("width")]
    [InlineData("height")]
    [InlineData("quantity")]
    [InlineData("selected")]
    public void ExcludedEnglishWord_BlocksOtherwiseMatchingText(string word)
    {
        const string baseText = "Product exceeds the limit";
        Assert.True(ProductLimitToastClassifier.IsProductLimitToast(baseText));
        Assert.False(ProductLimitToastClassifier.IsProductLimitToast($"{baseText} {word}"));
    }

    // ── Ranh giới từ: từ khóa nằm TRONG từ khác không được tính ───────────────────────────────────────────────
    [Theory]
    [InlineData("Thanh toán lỗi: số lượng sản phẩm vượt quá giới hạn")]        // "thanh" chứa "anh"
    [InlineData("Số sản phẩm vượt giới hạn trong giai đoạn này")]               // "giai" chứa "gia"
    [InlineData("The number of products in the content exceeds the limit")]    // "content" chứa "ten"
    [InlineData("Số lượng sản phẩm đã đạt giới hạn, vui lòng mở tài khoản")]    // "mo tai" không phải "mo ta"
    [InlineData("Number of products exceeds the limit of this resizer")]        // "resizer" chứa "size"
    [InlineData("Products exceed the limit for this username")]                 // "username" chứa "name" (ranh giới ĐẦU từ)
    [InlineData("Products exceed the limit, contact support asap")]             // "asap" chứa "sap" (ranh giới ĐẦU từ)
    public void KeywordInsideAnotherWord_IsNotCounted(string text)
        => Assert.True(ProductLimitToastClassifier.IsProductLimitToast(text));

    // ── FindFirstProductLimitToast: lọc theo loại + mốc thời gian, trả nguyên văn mục đầu ──────────────────────
    private static RecordedToast T(string text, long ts, string kind = "error") => new(text, ts, kind);

    [Fact]
    public void Find_ReturnsFirstMatchVerbatim()
    {
        var list = new[]
        {
            T("Lưu thành công", 100),
            T("  Số sản phẩm vượt giới hạn  ", 110),
            T("The shop has reached its listing limit", 120),
        };
        Assert.Equal("  Số sản phẩm vượt giới hạn  ", ProductLimitToastClassifier.FindFirstProductLimitToast(list, 100));
    }

    [Fact]
    public void Find_SkipsSuccessAndInfoKinds()
    {
        var list = new[]
        {
            T("Số sản phẩm vượt giới hạn", 200, "success"),
            T("Số sản phẩm vượt giới hạn", 201, "info"),
            T("Số sản phẩm vượt giới hạn", 202, "SUCCESS"),
            T("Over the product limit", 203, "error"),
        };
        Assert.Equal("Over the product limit", ProductLimitToastClassifier.FindFirstProductLimitToast(list, 0));
        Assert.Null(ProductLimitToastClassifier.FindFirstProductLimitToast(list.Take(3), 0));
    }

    // Vòng 3: loading không kích hoạt; mục chỉ đến từ [role=alert] (kind "alert:…") không kích hoạt dù text khớp;
    // warning của toast thật vẫn kích hoạt.
    [Fact]
    public void Find_SkipsLoadingAndAlertSourcedKinds()
    {
        var list = new[]
        {
            T("Số sản phẩm vượt giới hạn", 300, "loading"),
            T("Số sản phẩm vượt giới hạn", 301, "alert"),
            T("Số sản phẩm vượt giới hạn", 302, "alert:error"),
            T("Số sản phẩm vượt giới hạn", 303, "alert:warning"),
            T("Số sản phẩm vượt giới hạn", 304, "ALERT:error"),
        };
        Assert.Null(ProductLimitToastClassifier.FindFirstProductLimitToast(list, 0));
        Assert.Equal("Over the product limit", ProductLimitToastClassifier.FindFirstProductLimitToast(
            list.Append(T("Over the product limit", 305, "error")), 0));
    }

    [Fact]
    public void Find_WarningKindOfRealToast_Triggers()
        => Assert.Equal("Số sản phẩm vượt giới hạn", ProductLimitToastClassifier.FindFirstProductLimitToast(
            [T("Số sản phẩm vượt giới hạn", 400, "warning")], 0));

    [Theory]
    [InlineData("error", true)]
    [InlineData("warning", true)]
    [InlineData("WARNING", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("success", false)]
    [InlineData("info", false)]
    [InlineData("loading", false)]
    [InlineData("alert", false)]
    [InlineData("alert:warning", false)]
    public void IsTriggerKind_Table(string? kind, bool expected)
        => Assert.Equal(expected, ProductLimitToastClassifier.IsTriggerKind(kind));

    [Fact]
    public void Find_SkipsEntriesOlderThanSince_KeepsBoundary()
    {
        var list = new[] { T("Số sản phẩm vượt giới hạn", 99), T("Over the product limit", 100) };
        Assert.Equal("Over the product limit", ProductLimitToastClassifier.FindFirstProductLimitToast(list, 100));
        Assert.Null(ProductLimitToastClassifier.FindFirstProductLimitToast(list.Take(1), 100));
        Assert.Equal("Số sản phẩm vượt giới hạn", ProductLimitToastClassifier.FindFirstProductLimitToast(list.Take(1), 99));
    }

    [Fact]
    public void Find_NonMatchingOrEmpty_ReturnsNull()
    {
        Assert.Null(ProductLimitToastClassifier.FindFirstProductLimitToast([], 0));
        Assert.Null(ProductLimitToastClassifier.FindFirstProductLimitToast(
            [T("Tên sản phẩm vượt quá giới hạn 120 ký tự", 10), T("", 11)], 0));
    }
}
