namespace Shopee.Core.MassUpload.Seller;

/// <summary>
/// Một tài khoản seller Shopee của app đăng SP (plan GĐ4 bước 1). Phiên đăng nhập nằm trong profile Brave riêng của tài khoản;
/// các lượt sau dùng lại phiên đó. Từ GĐ5b app tự đăng nhập được bằng <see cref="ShopeeLogin"/> / <see cref="ShopeePassword"/>
/// (nút "Mở + đăng nhập Shopee"); mật khẩu lưu chữ thường như key Kiot (user chốt), KHÔNG bao giờ in ra log.
/// </summary>
public sealed class SellerAccount
{
    /// <summary>Mã ổn định, dùng làm tên thư mục profile — chỉ chữ/số/<c>_</c>/<c>-</c> (xem <see cref="SellerAccountStore.IsValidId"/>).</summary>
    public string Id { get; set; } = "";

    public string Label { get; set; } = "";

    /// <summary>Tắt = lượt chạy bỏ qua tài khoản này.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Thư mục profile Brave riêng. null/rỗng = mặc định <c>&lt;SuitePaths.Root&gt;\upload\profiles\&lt;Id&gt;</c>
    /// (<see cref="SellerAccountStore.ResolveProfileDir"/>).</summary>
    public string? ProfileDir { get; set; }

    /// <summary>null = đi proxy chung của app (<see cref="SellerUploadSettings.SharedProxy"/>); khác null = proxy riêng.</summary>
    public SellerProxyOverride? ProxyOverride { get; set; }

    public SellerDefaults Defaults { get; set; } = new();

    public SellerSource Source { get; set; } = new();

    /// <summary>Trần SP đăng mỗi ngày của shop (plan GĐ5 "Nhịp chạy": 100).</summary>
    public int DailyProductCap { get; set; } = 100;

    /// <summary>Tài khoản đăng nhập Seller Centre (SĐT / tên đăng nhập / email — ô <c>loginKey</c>). null = chưa nhập.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShopeeLogin { get; set; }

    /// <summary>Mật khẩu Shopee (chữ thường trong file, plan GĐ5b). Ô mật khẩu của Shopee có <c>maxlength=16</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShopeePassword { get; set; }

    /// <summary>Hotmail/Outlook nhận mã xác minh của Shopee — app chỉ đăng nhập hộp thư cho user tự lấy mã, KHÔNG đọc mail.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerifyEmail { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerifyEmailPassword { get; set; }

    /// <summary>(GĐ8) File mẫu Đăng Hàng Loạt (bản nâng cao, mỗi file một ngành cấp 1; hoặc một bản basic) đã chọn cho shop lần
    /// trước. null = chưa chọn.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? TemplatePaths { get; set; }

    /// <summary>(GĐ9) Khoá ảnh của shop: chuỗi ngẫu nhiên 128-bit (GUID "N") sinh MỘT lần rồi giữ mãi — dùng làm shopKey cho đổi MD5
    /// ảnh (đường CDN) và link ảnh gốc trên Hub. Khác <see cref="Id"/> (<c>seller-N</c> chỉ duy nhất trên một máy, tạo lại tài khoản
    /// là trùng) để hai shop không bao giờ ra cùng MD5 / cùng URL. null = tài khoản cũ chưa có (app sinh lúc nạp).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageKey { get; set; }

    /// <summary>Khoá ảnh mới (GUID "N", 32 hex).</summary>
    public static string NewImageKey() => Guid.NewGuid().ToString("N");

    /// <summary>Thuộc tính JSON bản này không biết (bản app mới hơn ghi, hoặc sửa tay) — giữ nguyên khi đọc rồi lưu lại.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public enum ProxyScheme { Http, Socks5 }

public enum SellerProxyKind { Kiot, Static }

/// <summary>Proxy riêng của một tài khoản, đè proxy chung.</summary>
public sealed class SellerProxyOverride
{
    public SellerProxyKind Kind { get; set; } = SellerProxyKind.Kiot;

    /// <summary>Key KiotProxy riêng (khi <see cref="Kind"/> = Kiot).</summary>
    public string? KiotKey { get; set; }

    /// <summary>Vùng Kiot (<c>random | bac | trung | nam</c>, như <c>ShopeeAccount.Region</c>). Plan: nên cố định, không random.</summary>
    public string Region { get; set; } = "random";

    /// <summary>Key riêng: true = giữ IP hiện có của key (gọi <c>/current</c>, chỉ <c>/new</c> khi key chưa có IP) — cho shop
    /// hay bị đòi xác minh (plan mục 6). false = đổi IP mỗi lần mở shop như key chung.</summary>
    public bool KiotKeepIp { get; set; }

    /// <summary>Proxy tĩnh <c>host:port[:user:pass]</c>, có thể kèm scheme (<see cref="StaticProxyParser"/>).</summary>
    public string? Static { get; set; }

    /// <summary>Kiểu proxy: Kiot chọn cổng http/socks5; proxy tĩnh không ghi scheme thì lấy kiểu này.</summary>
    public ProxyScheme Type { get; set; } = ProxyScheme.Http;

    /// <summary>Thuộc tính JSON lạ — giữ nguyên khi đọc rồi lưu lại.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Giá trị mặc định của tài khoản cho các cột JSON <c>get_pc</c> không có. Đơn vị theo kết quả GĐ0: cân nặng gram, kích thước cm,
/// tồn kho tính cho MỖI phân loại. Số mặc định = số đã dùng ở GĐ0 (100 / 200 g / 10×10×10 cm).
/// </summary>
public sealed class SellerDefaults
{
    public int StockPerVariation { get; set; } = 100;
    public int WeightGrams { get; set; } = 200;
    public int LengthCm { get; set; } = 10;
    public int WidthCm { get; set; } = 10;
    public int HeightCm { get; set; } = 10;

    /// <summary>Số SP mỗi file (plan GĐ5 "Nhịp chạy": 30).</summary>
    public int ProductsPerFile { get; set; } = 30;

    /// <summary>Trần tồn kho của cột <c>ps_stock</c>.</summary>
    public const int MaxStock = 999_999;

    /// <summary>Trần SP/file của writer (plan GĐ2 bước 3: mặc định tối đa 100).</summary>
    public const int MaxProductsPerFile = 100;

    /// <summary>Lỗi giá trị (rỗng = hợp lệ).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (StockPerVariation is < 0 or > MaxStock) errors.Add($"Tồn kho mỗi phân loại phải trong 0–{MaxStock:N0}.");
        if (WeightGrams <= 0) errors.Add("Cân nặng (g) phải > 0.");
        if (LengthCm <= 0 || WidthCm <= 0 || HeightCm <= 0) errors.Add("Dài/rộng/cao (cm) phải > 0.");
        if (ProductsPerFile is < 1 or > MaxProductsPerFile) errors.Add($"Số SP mỗi file phải trong 1–{MaxProductsPerFile}.");
        return errors;
    }

    /// <summary>Đổi sang tham số của mapper GĐ2a. <paramref name="sizeChartImageKey"/> = khoá ảnh bảng size dùng chung của lượt
    /// (<c>SizeChartImage.FromFile(...).Key</c>). Giá trị sai → <see cref="InvalidOperationException"/>.</summary>
    public MassUploadDefaults ToMassUploadDefaults(string sizeChartImageKey)
    {
        var errors = Validate();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        if (string.IsNullOrWhiteSpace(sizeChartImageKey)) throw new InvalidOperationException("Chưa có ảnh bảng size.");
        return new MassUploadDefaults(StockPerVariation, WeightGrams, LengthCm, WidthCm, HeightCm, sizeChartImageKey, ProductsPerFile);
    }

    /// <summary>Thuộc tính JSON lạ — giữ nguyên khi đọc rồi lưu lại.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Nguồn SP: TK BigSeller × sheet × dải dòng (giống Harvest/Scrape). Từ GĐ8: file Excel (<see cref="WorkbookPath"/>) × sheet ×
/// dải dòng — số dòng Excel thật như app Harvest.</summary>
public sealed class SellerSource
{
    public string BigSellerAccount { get; set; } = "";

    /// <summary>(GĐ8) File Excel nguồn (link SP, như app Harvest đọc). null = chưa chọn.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkbookPath { get; set; }

    public string Sheet { get; set; } = "";
    public int FromRow { get; set; }
    /// <summary>0 = tới hết sheet.</summary>
    public int ToRow { get; set; }

    /// <summary>Thuộc tính JSON lạ — giữ nguyên khi đọc rồi lưu lại.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Cấu hình chung của app đăng SP.</summary>
public sealed class SellerUploadSettings
{
    /// <summary>Proxy chung: 1 key Kiot gói "Đổi IP VIP - VN" cho cả lượt, đổi IP mỗi lần sang shop (plan GĐ4 bước 3).</summary>
    public KiotProxySettings SharedProxy { get; set; } = new();

    /// <summary>Ảnh bảng size dùng chung cho cột O (plan "Chốt thiết kế" mục 4).</summary>
    public string? SizeChartImagePath { get; set; }

    /// <summary>(GĐ8) Thư mục ghi file xuất lần trước. null = chưa chọn.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExportDirectory { get; set; }

    /// <summary>(GĐ9) "Ảnh chưa có trên Hub": true = bỏ SP thiếu ảnh (không tải CDN); null / false = tải từ CDN (mặc định).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SkipMissingHubImages { get; set; }

    public SellerPacing Pacing { get; set; } = new();

    /// <summary>Thuộc tính JSON lạ — giữ nguyên khi đọc rồi lưu lại.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class KiotProxySettings
{
    public string? KiotKey { get; set; }
    public string Region { get; set; } = "random";
    public ProxyScheme Type { get; set; } = ProxyScheme.Http;

    /// <summary>(GĐ5d) Proxy tĩnh dùng chung, dạng như <see cref="StaticProxyParser"/> nhận. CHỈ dùng khi KHÔNG có
    /// <see cref="KiotKey"/> (Kiot luôn được ưu tiên). Không ghi scheme thì lấy <see cref="Type"/>.</summary>
    public string? Static { get; set; }

    /// <summary>Thuộc tính JSON lạ — giữ nguyên khi đọc rồi lưu lại.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Nhịp chạy (plan GĐ5 bước 5). Chỉ là dữ liệu cấu hình — GĐ5 dùng. Số mặc định theo plan.
/// </summary>
public sealed class SellerPacing
{
    /// <summary>Gói Kiot đổi IP bắt mỗi IP dùng tối thiểu 5 phút ⇒ nghỉ giữa 2 shop không được dưới mốc này.</summary>
    public const int MinPauseBetweenShopsMinutes = 5;

    public int PauseBetweenFilesMinMinutes { get; set; } = 3;
    public int PauseBetweenFilesMaxMinutes { get; set; } = 8;
    public int PauseBetweenShopsMinMinutes { get; set; } = 10;
    public int PauseBetweenShopsMaxMinutes { get; set; } = 20;

    /// <summary>Khung giờ chạy [từ, đến) theo giờ máy.</summary>
    public int ActiveFromHour { get; set; } = 8;
    public int ActiveToHour { get; set; } = 22;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (PauseBetweenFilesMinMinutes < 0 || PauseBetweenFilesMaxMinutes < PauseBetweenFilesMinMinutes)
            errors.Add("Nghỉ giữa 2 file: cần 0 ≤ từ ≤ đến.");
        if (PauseBetweenShopsMinMinutes < MinPauseBetweenShopsMinutes)
            errors.Add($"Nghỉ giữa 2 shop phải ≥ {MinPauseBetweenShopsMinutes} phút (gói Kiot chỉ cho đổi IP sau 5 phút).");
        if (PauseBetweenShopsMaxMinutes < PauseBetweenShopsMinMinutes)
            errors.Add("Nghỉ giữa 2 shop: cần từ ≤ đến.");
        if (ActiveFromHour is < 0 or > 23 || ActiveToHour is < 1 or > 24 || ActiveToHour <= ActiveFromHour)
            errors.Add("Khung giờ chạy: cần 0 ≤ từ < đến ≤ 24.");
        return errors;
    }

    /// <summary>Thuộc tính JSON lạ — giữ nguyên khi đọc rồi lưu lại.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Nội dung file <c>seller-accounts.json</c>.</summary>
public sealed class SellerAccountsData
{
    public int Version { get; set; } = 1;
    public SellerUploadSettings Settings { get; set; } = new();
    public List<SellerAccount> Accounts { get; set; } = [];

    /// <summary>Mã của tài khoản đã xoá — CHỈ THÊM. Không cấp lại cho tài khoản mới: profile (phiên đăng nhập shop cũ) có thể vẫn
    /// nằm ở thư mục tuỳ chỉnh mà app không thấy.</summary>
    public List<string> RetiredIds { get; set; } = [];

    /// <summary>Thuộc tính JSON lạ ở gốc file — giữ nguyên khi đọc rồi lưu lại.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
