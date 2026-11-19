using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Shopee.Core.MassUpload.Seller;

namespace Shopee.Upload.Forms;

/// <summary>
/// Đổi qua lại giữa form trên màn và dữ liệu của <see cref="SellerAccountStore"/> (plan GĐ5a mục 6). Hàm thuần, không WPF.
/// <list type="bullet">
/// <item>Ô số: trim, phải là số nguyên (cho phép âm để <c>Validate</c> của Core báo đúng luật); sai → lỗi kèm tên ô, KHÔNG đoán.</item>
/// <item>Phần dữ liệu không có trên màn (<see cref="SellerAccountsData.Version"/>, thuộc tính JSON lạ) lấy lại từ bản đã đọc, để lưu
/// từ màn không xoá mất. Phần bị ẩn khỏi màn (GĐ8: proxy / đăng nhập / nhịp chạy) vẫn nằm trong form nên lưu lại nguyên.</item>
/// </list>
/// </summary>
public static class SellerFormMapper
{
    // ─────────────── dữ liệu → form ───────────────

    public static SettingsForm ToForm(SellerUploadSettings settings)
    {
        var shared = settings.SharedProxy ?? new KiotProxySettings();
        var p = settings.Pacing ?? new SellerPacing();
        return new SettingsForm
        {
            KiotKey = shared.KiotKey ?? "",
            Region = NormalizeRegion(shared.Region),
            Type = shared.Type,
            StaticProxy = shared.Static ?? "",
            SizeChartImagePath = settings.SizeChartImagePath ?? "",
            ExportDirectory = settings.ExportDirectory ?? "",
            SkipMissingHubImages = settings.SkipMissingHubImages == true,
            PauseFilesMin = Num(p.PauseBetweenFilesMinMinutes),
            PauseFilesMax = Num(p.PauseBetweenFilesMaxMinutes),
            PauseShopsMin = Num(p.PauseBetweenShopsMinMinutes),
            PauseShopsMax = Num(p.PauseBetweenShopsMaxMinutes),
            ActiveFromHour = Num(p.ActiveFromHour),
            ActiveToHour = Num(p.ActiveToHour),
        };
    }

    public static AccountForm ToForm(SellerAccount a)
    {
        var d = a.Defaults ?? new SellerDefaults();
        var src = a.Source ?? new SellerSource();
        var ov = a.ProxyOverride;
        var f = new AccountForm(a.Id ?? "")
        {
            Label = a.Label ?? "",
            Enabled = a.Enabled,
            ProfileDir = a.ProfileDir ?? "",
            Proxy = ov is null ? ProxyChoice.Shared : ov.Kind == SellerProxyKind.Kiot ? ProxyChoice.KiotOwn : ProxyChoice.Static,
            StockPerVariation = Num(d.StockPerVariation),
            WeightGrams = Num(d.WeightGrams),
            LengthCm = Num(d.LengthCm),
            WidthCm = Num(d.WidthCm),
            HeightCm = Num(d.HeightCm),
            ProductsPerFile = Num(d.ProductsPerFile),
            DailyProductCap = Num(a.DailyProductCap),
            ImageKey = a.ImageKey ?? "",
            BigSellerAccount = src.BigSellerAccount ?? "",
            WorkbookPath = src.WorkbookPath ?? "",
            Sheet = src.Sheet ?? "",
            FromRow = Num(src.FromRow),
            ToRow = Num(src.ToRow),
            ShopeeLogin = a.ShopeeLogin ?? "",
            ShopeePassword = a.ShopeePassword ?? "",
            VerifyEmail = a.VerifyEmail ?? "",
            VerifyEmailPassword = a.VerifyEmailPassword ?? "",
        };
        if (ov is not null)
        {
            f.OwnKiotKey = ov.KiotKey ?? "";
            f.OwnRegion = NormalizeRegion(ov.Region);
            f.OwnKeepIp = ov.KiotKeepIp;
            f.StaticProxy = ov.Static ?? "";
            f.OverrideType = ov.Type;
        }
        foreach (var t in a.TemplatePaths ?? []) f.TemplatePaths.Add(t);
        return f;
    }

    /// <summary>Form của tài khoản mới: mặc định của <see cref="SellerAccount"/> (Core giữ số mặc định của plan).</summary>
    public static AccountForm NewAccountForm(string id) => ToForm(new SellerAccount { Id = id, ImageKey = SellerAccount.NewImageKey() });

    // ─────────────── form → dữ liệu ───────────────

    /// <summary>Cấu hình chung từ form, GHI ĐÈ lên bản sao của <paramref name="original"/> (bản đã đọc) — phần không có trên màn
    /// (thuộc tính JSON lạ) giữ nguyên.</summary>
    public static SellerUploadSettings ToSettings(SettingsForm f, SellerUploadSettings? original, List<string> errors)
    {
        const string pre = "Nhịp chạy: ";
        var s = Clone(original) ?? new SellerUploadSettings();
        s.SharedProxy ??= new KiotProxySettings();
        s.SharedProxy.KiotKey = NullIfBlank(f.KiotKey);
        s.SharedProxy.Region = NormalizeRegion(f.Region);
        s.SharedProxy.Type = f.Type;
        s.SharedProxy.Static = NullIfBlank(f.StaticProxy);
        s.SizeChartImagePath = NullIfBlank(f.SizeChartImagePath);
        s.ExportDirectory = NullIfBlank(f.ExportDirectory);
        s.SkipMissingHubImages = f.SkipMissingHubImages ? true : null;
        s.Pacing ??= new SellerPacing();
        s.Pacing.PauseBetweenFilesMinMinutes = Int(f.PauseFilesMin, pre + "nghỉ giữa 2 file (từ)", errors);
        s.Pacing.PauseBetweenFilesMaxMinutes = Int(f.PauseFilesMax, pre + "nghỉ giữa 2 file (đến)", errors);
        s.Pacing.PauseBetweenShopsMinMinutes = Int(f.PauseShopsMin, pre + "nghỉ giữa 2 shop (từ)", errors);
        s.Pacing.PauseBetweenShopsMaxMinutes = Int(f.PauseShopsMax, pre + "nghỉ giữa 2 shop (đến)", errors);
        s.Pacing.ActiveFromHour = Int(f.ActiveFromHour, pre + "chạy từ giờ", errors);
        s.Pacing.ActiveToHour = Int(f.ActiveToHour, pre + "chạy đến giờ", errors);
        return s;
    }

    /// <summary>Một tài khoản từ form, GHI ĐÈ các trường form quản lý lên bản sao của <paramref name="original"/> (bản đã đọc cùng
    /// Id) — trường form không biết (vd <c>Mode</c> của GĐ5, thuộc tính JSON lạ) giữ nguyên. null = tài khoản mới, dựng từ đầu.
    /// Chỉ ghi phần proxy của kiểu đang chọn.</summary>
    public static SellerAccount ToAccount(AccountForm f, List<string> errors, SellerAccount? original = null)
    {
        var pre = $"{f.DisplayName} ({f.Id}): ";
        var a = Clone(original) ?? new SellerAccount();
        a.Id = f.Id;
        a.Label = f.Label.Trim();
        // Khoá ảnh (GĐ9): sinh một lần (tài khoản mới: NewAccountForm; tài khoản cũ: màn chính lúc nạp), không bao giờ sửa ở đây.
        a.ImageKey = NullIfBlank(f.ImageKey) ?? a.ImageKey;
        a.Enabled = f.Enabled;
        a.ProfileDir = NullIfBlank(f.ProfileDir);

        if (f.Proxy == ProxyChoice.Shared)
        {
            a.ProxyOverride = null;
        }
        else
        {
            var ov = a.ProxyOverride ?? new SellerProxyOverride();
            ov.Type = f.OverrideType;
            if (f.Proxy == ProxyChoice.KiotOwn)
            {
                ov.Kind = SellerProxyKind.Kiot;
                ov.KiotKey = NullIfBlank(f.OwnKiotKey);
                ov.Region = NormalizeRegion(f.OwnRegion);
                ov.KiotKeepIp = f.OwnKeepIp;
                ov.Static = null;
            }
            else
            {
                ov.Kind = SellerProxyKind.Static;
                ov.Static = NullIfBlank(f.StaticProxy);
                ov.KiotKey = null;
                ov.Region = KiotRegions.Random;
                ov.KiotKeepIp = false;
            }
            a.ProxyOverride = ov;
        }

        a.Defaults ??= new SellerDefaults();
        a.Defaults.StockPerVariation = Int(f.StockPerVariation, pre + "tồn kho mỗi phân loại", errors);
        a.Defaults.WeightGrams = Int(f.WeightGrams, pre + "cân nặng (g)", errors);
        a.Defaults.LengthCm = Int(f.LengthCm, pre + "dài (cm)", errors);
        a.Defaults.WidthCm = Int(f.WidthCm, pre + "rộng (cm)", errors);
        a.Defaults.HeightCm = Int(f.HeightCm, pre + "cao (cm)", errors);
        a.Defaults.ProductsPerFile = Int(f.ProductsPerFile, pre + "số SP mỗi file", errors);

        a.Source ??= new SellerSource();
        a.Source.BigSellerAccount = f.BigSellerAccount.Trim();
        a.Source.WorkbookPath = NullIfBlank(f.WorkbookPath);
        a.Source.Sheet = f.Sheet.Trim();
        a.Source.FromRow = Int(f.FromRow, pre + "từ dòng", errors, blankAs: 0);
        a.Source.ToRow = Int(f.ToRow, pre + "đến dòng", errors, blankAs: 0);

        a.DailyProductCap = Int(f.DailyProductCap, pre + "trần SP mỗi ngày", errors);

        var templates = f.TemplatePaths.Select(NullIfBlank).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        a.TemplatePaths = templates.Count == 0 ? null : templates;

        // Tài khoản / email: trim. Mật khẩu: GIỮ NGUYÊN từng ký tự (dấu cách đầu/cuối có thể là một phần mật khẩu); rỗng → null.
        a.ShopeeLogin = NullIfBlank(f.ShopeeLogin);
        a.ShopeePassword = NullIfEmpty(f.ShopeePassword);
        a.VerifyEmail = NullIfBlank(f.VerifyEmail);
        a.VerifyEmailPassword = NullIfEmpty(f.VerifyEmailPassword);
        return a;
    }

    /// <summary>Toàn bộ dữ liệu để <see cref="SellerAccountStore.Save"/>: bản sao của <paramref name="original"/> (giữ Version,
    /// thuộc tính lạ) với cấu hình chung + danh sách tài khoản từ form (khớp bản gốc theo Id). <paramref name="retiredIds"/> được
    /// THÊM vào <see cref="SellerAccountsData.RetiredIds"/> (không bao giờ bớt). <c>Errors</c> = lỗi đọc ô (chưa gồm lỗi
    /// <see cref="SellerAccountStore.Validate"/>).</summary>
    public static (SellerAccountsData Data, List<string> Errors) ToData(SettingsForm settings, IEnumerable<AccountForm> accounts,
        SellerAccountsData? original, IEnumerable<string>? retiredIds = null)
    {
        var errors = new List<string>();
        // Id trùng (file sửa tay) → ghép lần lượt: form thứ n của Id đó ↔ bản gốc thứ n, không để 2 form cùng ghi đè lên 1 bản gốc.
        var byId = new Dictionary<string, Queue<SellerAccount>>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in original?.Accounts ?? [])
        {
            var key = o.Id ?? "";
            if (!byId.TryGetValue(key, out var q)) byId[key] = q = new Queue<SellerAccount>();
            q.Enqueue(o);
        }

        var data = Clone(original) ?? new SellerAccountsData();
        data.Settings = ToSettings(settings, original?.Settings, errors);
        data.Accounts = accounts
            .Select(f => ToAccount(f, errors, byId.TryGetValue(f.Id, out var q) && q.Count > 0 ? q.Dequeue() : null))
            .ToList();
        data.RetiredIds ??= [];
        foreach (var id in retiredIds ?? [])
            if (!data.RetiredIds.Contains(id, StringComparer.OrdinalIgnoreCase)) data.RetiredIds.Add(id);
        return (data, errors);
    }

    /// <summary>Mã mới cho tài khoản thêm: <c>seller-1</c>, <c>seller-2</c>… — số nhỏ nhất KHÔNG thuộc: mã đang dùng
    /// (<paramref name="existingIds"/>), mã đã xoá (<paramref name="retiredIds"/>), mã bị chiếm ngoài danh sách
    /// (<paramref name="isTaken"/> — màn chính: đã có thư mục profile mặc định trên đĩa). So không phân biệt hoa thường (như Store);
    /// luôn hợp lệ theo <see cref="SellerAccountStore.IsValidId"/>. Lý do: profile cũ còn phiên đăng nhập Shopee — dùng lại mã là tài
    /// khoản mới mở nhầm vào shop cũ.</summary>
    public static string NewId(IEnumerable<string> existingIds, Func<string, bool>? isTaken = null, IEnumerable<string>? retiredIds = null)
    {
        var used = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        used.UnionWith(retiredIds ?? []);
        for (var n = 1; ; n++)
        {
            var id = "seller-" + n.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(id) && isTaken?.Invoke(id) != true) return id;
        }
    }

    private static readonly JsonSerializerOptions CloneJson = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>Bản sao sâu qua JSON (giữ cả thuộc tính lạ) — sửa bản sao không đụng bản đã đọc, lưu lỗi thì bản đã đọc còn nguyên.</summary>
    private static T? Clone<T>(T? value) where T : class =>
        value is null ? null : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, CloneJson), CloneJson);

    /// <summary>Mọi bí mật trong form (key Kiot chung/riêng, mật khẩu proxy tĩnh, mật khẩu Shopee, mật khẩu email xác minh) — để che
    /// khỏi log/màn. Tài khoản Shopee / email KHÔNG che (không phải mật khẩu; log cần chúng để biết đang đăng nhập shop nào).
    /// Proxy tĩnh: LUÔN trả mọi đoạn tách theo <c>:</c> và <c>@</c> dài ≥ 4 ký tự (bỏ scheme), TRỪ host và cổng đã đọc được — đọc
    /// được vẫn có thể là đọc sai (gõ ngược thứ tự mà vẫn ra một proxy "hợp lệ") thì mật khẩu thật nằm ở chỗ khác; che thừa còn hơn
    /// lộ. Đọc được và có mật khẩu thì trả thêm mật khẩu đã giải mã (<c>%40</c> → <c>@</c>).</summary>
    public static IEnumerable<string?> Secrets(SettingsForm settings, IEnumerable<AccountForm> accounts)
    {
        yield return settings.KiotKey;
        // GĐ5d: proxy tĩnh chung che y như proxy tĩnh riêng.
        foreach (var s in StaticProxySecrets(settings.StaticProxy, settings.Type)) yield return s;
        foreach (var a in accounts)
        {
            yield return a.OwnKiotKey;
            yield return a.ShopeePassword;
            yield return a.VerifyEmailPassword;
            foreach (var s in StaticProxySecrets(a.StaticProxy, a.OverrideType)) yield return s;
        }
    }

    /// <summary>Bí mật trong một chuỗi proxy tĩnh: mọi đoạn tách theo <c>:</c>, <c>@</c> và khoảng trắng / tab (dạng GĐ5d) dài ≥ 4 ký
    /// tự (bỏ scheme), TRỪ host và cổng đã đọc được; đọc được và có mật khẩu thì thêm mật khẩu đã giải mã.</summary>
    private static IEnumerable<string?> StaticProxySecrets(string? staticProxy, ProxyScheme type)
    {
        if (string.IsNullOrWhiteSpace(staticProxy)) yield break;
        var ok = StaticProxyParser.TryParse(staticProxy, type, out var lease, out _);
        if (ok && lease!.HasCredentials) yield return lease.Pass;
        var raw = staticProxy.Trim();
        var sep = raw.IndexOf("://", StringComparison.Ordinal);
        if (sep >= 0) raw = raw[(sep + 3)..];
        var parts = raw.TrimEnd('/').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(w => w.Split([':', '@'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        foreach (var part in parts)
        {
            if (part.Length < SecretText.MinRedactLength) continue;
            if (ok && (lease!.HostForUri.Contains(part, StringComparison.OrdinalIgnoreCase)
                       || part == lease.Port.ToString(CultureInfo.InvariantCulture)))
                continue;
            yield return part;
        }
    }

    /// <summary>Vùng về dạng của ô chọn: rỗng → <c>random</c>, chữ thường. Giá trị lạ (sửa tay trong JSON) giữ nguyên để không đổi
    /// âm thầm.</summary>
    internal static string NormalizeRegion(string? region) =>
        string.IsNullOrWhiteSpace(region) ? KiotRegions.Random : region.Trim().ToLowerInvariant();

    private static string Num(int v) => v.ToString(CultureInfo.InvariantCulture);

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    private static int Int(string? text, string field, List<string> errors, int? blankAs = null)
    {
        var t = text?.Trim() ?? "";
        if (t.Length == 0)
        {
            if (blankAs is { } b) return b;
            errors.Add($"{field}: chưa nhập.");
            return 0;
        }
        if (int.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v)) return v;
        errors.Add($"{field}: '{t}' không phải số nguyên.");
        return 0;
    }
}
