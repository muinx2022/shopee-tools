using System.Net;
using System.Text.RegularExpressions;
using Shopee.Core.Infrastructure;

namespace Shopee.Core.MassUpload.Seller;

/// <summary>Kết quả <see cref="SellerAccountStore.Save"/>: <see cref="Errors"/> chặn lưu; <see cref="Warnings"/> chỉ cảnh báo.</summary>
public sealed record SellerAccountsSaveResult(bool Saved, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>
/// Kho tài khoản seller + cấu hình chung, file <c>&lt;SuitePaths.Root&gt;\upload\seller-accounts.json</c>. Đọc/ghi qua
/// <see cref="JsonAtomicFile"/> (cùng khuôn các store khác: file tạm → move).
/// <para>Key Kiot / user-pass proxy tĩnh / mật khẩu Shopee / mật khẩu email xác minh (GĐ5b) nằm trong file này dạng chữ thường,
/// như <c>ShopeeAccount.KiotProxyKey</c> của app hiện có — file nằm trong hồ sơ người dùng.</para>
/// </summary>
public sealed class SellerAccountStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Regex IdPattern = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant);

    private readonly string _path;
    private readonly string _profilesRoot;
    private readonly Action<string>? _log;
    private readonly object _lock = new();

    /// <param name="path">File JSON. Mặc định <see cref="DefaultPath"/>.</param>
    /// <param name="profilesRoot">Gốc thư mục profile mặc định. Mặc định <c>&lt;Root&gt;\upload\profiles</c>.</param>
    public SellerAccountStore(string? path = null, string? profilesRoot = null, Action<string>? log = null)
    {
        _path = path ?? DefaultPath();
        _profilesRoot = profilesRoot ?? Path.Combine(SuitePaths.ModuleDir("upload"), "profiles");
        _log = log;
    }

    public static string DefaultPath() => Path.Combine(SuitePaths.ModuleDir("upload"), "seller-accounts.json");

    public string FilePath => _path;

    /// <summary>Lần <see cref="Load"/> gần nhất gặp file hỏng → đường dẫn bản đã đổi tên (<c>.corrupt-&lt;giờ&gt;</c>); null = không.
    /// Màn hình dùng để báo user là tài khoản cũ nằm trong file đó (thay vì "chưa có cấu hình").</summary>
    public string? LastCorruptBackupPath { get; private set; }

    public static bool IsValidId(string? id) => id is not null && IdPattern.IsMatch(id);

    /// <summary>Thư mục profile Brave của tài khoản: <see cref="SellerAccount.ProfileDir"/> nếu có, không thì
    /// <c>&lt;profilesRoot&gt;\&lt;Id&gt;</c>. Đường dẫn tuyệt đối.</summary>
    public string ResolveProfileDir(SellerAccount account) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(account.ProfileDir)
            ? Path.Combine(_profilesRoot, account.Id)
            : account.ProfileDir);

    /// <summary>Đọc file. Chưa có file → dữ liệu rỗng.
    /// <list type="bullet">
    /// <item>Nội dung JSON hỏng (hoặc là <c>null</c>) → đổi tên thành <c>.corrupt-&lt;giờ&gt;</c> (giữ để cứu tay) rồi trả rỗng. Đổi tên
    /// không được → NÉM.</item>
    /// <item>Lỗi đọc (file đang bị khoá, quyền…) → NÉM, không đổi tên.</item>
    /// </list>
    /// Không dùng <see cref="JsonAtomicFile.TryLoad{T}"/> vì nó gộp mọi lỗi thành <c>null</c>: trả rỗng khi chỉ là khoá tạm thì lần
    /// <see cref="Save"/> sau ghi đè mất toàn bộ tài khoản.</summary>
    public SellerAccountsData Load()
    {
        lock (_lock)
        {
            LastCorruptBackupPath = null;
            if (!File.Exists(_path)) return new SellerAccountsData();
            var text = File.ReadAllText(_path, Encoding.UTF8);   // lỗi IO → ném
            SellerAccountsData? data;
            try { data = JsonSerializer.Deserialize<SellerAccountsData>(text, JsonOpts); }
            catch (JsonException) { data = null; }
            if (data is null)
            {
                var bak = $"{_path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                try { File.Move(_path, bak); }
                catch (Exception ex)
                {
                    throw new IOException($"seller-accounts.json hỏng và không đổi tên được ({ex.Message}) — sửa tay hoặc xoá file rồi mở lại.", ex);
                }
                LastCorruptBackupPath = bak;
                _log?.Invoke($"seller-accounts.json hỏng — đã đổi tên thành {Path.GetFileName(bak)}.");
                return new SellerAccountsData();
            }
            data.Settings ??= new SellerUploadSettings();
            data.Settings.SharedProxy ??= new KiotProxySettings();
            data.Settings.Pacing ??= new SellerPacing();
            data.Accounts ??= [];
            data.RetiredIds ??= [];
            // Phần tử null (sửa tay / file cắt dở) không mang dữ liệu nào → BỎ, ghi log; không coi cả file là hỏng.
            var nulls = data.Accounts.RemoveAll(a => a is null);
            data.RetiredIds.RemoveAll(x => x is null);
            if (nulls > 0) _log?.Invoke($"seller-accounts.json có {nulls} tài khoản rỗng (null) — đã bỏ qua.");
            foreach (var a in data.Accounts)
            {
                a.Defaults ??= new SellerDefaults();
                a.Source ??= new SellerSource();
            }
            return data;
        }
    }

    /// <summary>(GĐ9) Gán <see cref="SellerAccount.ImageKey"/> mới cho mọi tài khoản chưa có. Trả số tài khoản vừa gán.</summary>
    public static int AssignMissingImageKeys(SellerAccountsData data)
    {
        var n = 0;
        foreach (var a in data.Accounts ?? [])
            if (string.IsNullOrWhiteSpace(a.ImageKey)) { a.ImageKey = SellerAccount.NewImageKey(); n++; }
        return n;
    }

    /// <summary>(GĐ9) Ghi nguyên tử KHÔNG kiểm — chỉ dùng để lưu ngay <see cref="SellerAccount.ImageKey"/> vừa sinh cho dữ liệu VỪA ĐỌC
    /// THÀNH CÔNG từ file (không đổi gì khác), để khoá ảnh không đổi giữa các lần mở app. true = đã ghi.</summary>
    public bool SaveImageKeys(SellerAccountsData loaded)
    {
        lock (_lock) return JsonAtomicFile.Save(_path, loaded, JsonOpts, _log);
    }

    /// <summary>Kiểm rồi ghi nguyên tử. Có lỗi → KHÔNG ghi. <paramref name="browserChecks"/>: xem <see cref="Validate"/>.</summary>
    public SellerAccountsSaveResult Save(SellerAccountsData data, bool browserChecks = true)
    {
        var (errors, warnings) = Validate(data, browserChecks);
        if (errors.Count > 0) return new SellerAccountsSaveResult(false, errors, warnings);
        bool ok;
        lock (_lock) ok = JsonAtomicFile.Save(_path, data, JsonOpts, _log);
        if (!ok) errors = [.. errors, $"Không ghi được {Path.GetFileName(_path)}."];
        return new SellerAccountsSaveResult(ok, errors, warnings);
    }

    /// <summary>Kiểm dữ liệu (hàm thuần trừ phần chuẩn hoá đường dẫn profile). Lỗi: Id sai/trùng, trùng profile, proxy riêng sai
    /// dạng, proxy tĩnh chung sai dạng, giá trị mặc định / nhịp chạy sai. Cảnh báo (không chặn): 2 tài khoản trùng key Kiot riêng, key
    /// riêng trùng key chung, Kiot để vùng random, có tài khoản đi proxy chung mà chưa có proxy chung, và (GĐ5d) từ 2 tài khoản ĐANG
    /// BẬT trở lên cùng đi một proxy tĩnh (proxy tĩnh chung khi không có key Kiot, hoặc trùng proxy tĩnh riêng).
    /// <para><paramref name="browserChecks"/> = false (GĐ8: app chỉ xuất file, các ô proxy / đăng nhập / profile / nhịp chạy / trần
    /// ngày bị ẩn — user không sửa được): CHỈ kiểm mã tài khoản và giá trị mặc định; không lỗi, không cảnh báo nào về các phần ẩn.
    /// Dữ liệu các phần đó vẫn được lưu nguyên.</para></summary>
    public (List<string> Errors, List<string> Warnings) Validate(SellerAccountsData data, bool browserChecks = true)
    {
        if (!browserChecks) return ValidateExportOnly(data);
        var errors = new List<string>();
        var warnings = new List<string>();
        var settings = data.Settings ?? new SellerUploadSettings();
        var shared = settings.SharedProxy ?? new KiotProxySettings();
        var sharedKey = shared.KiotKey?.Trim();

        foreach (var e in (settings.Pacing ?? new SellerPacing()).Validate()) errors.Add(e);
        if (!string.IsNullOrWhiteSpace(sharedKey) && IsRandomRegion(shared.Region))
            warnings.Add("Proxy chung: vùng Kiot đang là random — nên chọn một vùng cố định.");
        // GĐ5d: proxy tĩnh chung — sai dạng là lỗi (kể cả khi đang bị bỏ qua vì có key Kiot: để sẵn một chuỗi sai là bẫy khi xoá key).
        string? sharedStaticId = null;
        if (!string.IsNullOrWhiteSpace(shared.Static))
        {
            if (!StaticProxyParser.TryParse(shared.Static, shared.Type, out var sl, out var serr))
            {
                // Đang có key Kiot → proxy tĩnh bị bỏ qua: riêng lỗi "SOCKS5 có user/pass" (do ô Kiểu chung đang là SOCKS5 cho Kiot) chỉ
                // cảnh báo. Lỗi định dạng khác vẫn chặn: bỏ key là proxy đó được dùng ngay.
                if (!string.IsNullOrWhiteSpace(sharedKey) && serr == StaticProxyParser.Socks5WithCredentialsError)
                    warnings.Add($"Proxy chung: proxy tĩnh (đang bị bỏ qua vì có key Kiot) sẽ không dùng được: {serr}");
                else
                    errors.Add($"Proxy chung: proxy tĩnh sai: {serr}");
            }
            else if (string.IsNullOrWhiteSpace(sharedKey))
                sharedStaticId = StaticKey(sl!);
        }
        // Proxy tĩnh "host:port" → tài khoản ĐANG BẬT đi qua nó (chung hoặc riêng).
        var staticOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void AddStaticOwner(string id, string owner)
        {
            if (!staticOwners.TryGetValue(id, out var list)) staticOwners[id] = list = [];
            list.Add(owner);
        }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profiles = new List<(string Path, string Name)>();
        var proxyOwners = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var usesShared = false;

        foreach (var a in data.Accounts ?? [])
        {
            var name = string.IsNullOrWhiteSpace(a.Label) ? a.Id : $"{a.Label} ({a.Id})";
            if (!IsValidId(a.Id)) { errors.Add($"Mã tài khoản '{a.Id}' không hợp lệ (chỉ chữ, số, _ và -, tối đa 64 ký tự)."); continue; }
            if (!ids.Add(a.Id)) errors.Add($"Trùng mã tài khoản '{a.Id}'.");
            // Kiểm dữ liệu đăng nhập TRƯỚC các nhánh 'continue' của profile / proxy: profile sai không được che mất lỗi đăng nhập.
            ValidateLogin(a, name, errors, warnings);

            string profile;
            try { profile = ResolveProfileDir(a); }
            catch (Exception ex) { errors.Add($"{name}: thư mục profile sai: {ex.Message}"); continue; }
            if (!string.IsNullOrWhiteSpace(a.ProfileDir) && !Path.IsPathFullyQualified(a.ProfileDir.Trim()))
            { errors.Add($"{name}: thư mục profile phải là đường dẫn tuyệt đối (vd D:\\profiles\\{a.Id})."); continue; }
            if (PersonalBrowserDataDirs().FirstOrDefault(d => IsSameOrUnder(profile, d)) is { } personal)
            {
                errors.Add($"{name}: thư mục profile nằm trong dữ liệu trình duyệt cá nhân ({personal}) — mở Brave sẽ tắt trình duyệt đang " +
                           "dùng profile đó. Chọn thư mục khác.");
                continue;
            }
            if (!string.IsNullOrWhiteSpace(a.ProfileDir) && IsSameOrUnder(_profilesRoot, profile))
            {
                errors.Add($"{name}: thư mục profile chứa gốc profile mặc định của app ({NormalizePath(_profilesRoot)}) — chọn thư mục con riêng.");
                continue;
            }
            // Trùng HOẶC lồng nhau (A = D:\x\p, B = D:\x\p\Default) đều là dùng chung: Brave của shop này đọc/ghi vào profile shop kia.
            if (profiles.FirstOrDefault(p => IsSameOrUnder(profile, p.Path) || IsSameOrUnder(p.Path, profile)) is { Name: { } other } hit)
                errors.Add(string.Equals(NormalizePath(hit.Path), NormalizePath(profile), StringComparison.OrdinalIgnoreCase)
                    ? $"{name} và {other} dùng chung một thư mục profile."
                    : $"{name} và {other} có thư mục profile lồng nhau — coi như dùng chung một thư mục profile.");
            else profiles.Add((profile, name));

            foreach (var e in (a.Defaults ?? new SellerDefaults()).Validate()) errors.Add($"{name}: {e}");
            if (a.DailyProductCap < 1) errors.Add($"{name}: trần SP/ngày phải ≥ 1.");

            var ov = a.ProxyOverride;
            if (ov is null)
            {
                usesShared = true;
                if (a.Enabled && sharedStaticId is not null) AddStaticOwner(sharedStaticId, name);
                continue;
            }
            string identity;
            if (ov.Kind == SellerProxyKind.Kiot)
            {
                var key = ov.KiotKey?.Trim();
                if (string.IsNullOrWhiteSpace(key)) { errors.Add($"{name}: proxy riêng Kiot thiếu key."); continue; }
                if (IsRandomRegion(ov.Region)) warnings.Add($"{name}: vùng Kiot đang là random — nên chọn một vùng cố định.");
                if (string.Equals(key, sharedKey, StringComparison.Ordinal))
                    warnings.Add($"{name}: key riêng …{Tail(key)} trùng key proxy chung.");
                identity = "kiot:" + key;
            }
            else
            {
                if (!StaticProxyParser.TryParse(ov.Static, ov.Type, out var lease, out var err))
                { errors.Add($"{name}: proxy tĩnh sai: {err}"); continue; }
                identity = "static:" + StaticKey(lease!);
                if (a.Enabled) AddStaticOwner(identity["static:".Length..], name);
            }
            if (!proxyOwners.TryGetValue(identity, out var owners)) proxyOwners[identity] = owners = [];
            owners.Add(name);
        }

        foreach (var (identity, owners) in proxyOwners)
        {
            if (owners.Count < 2) continue;
            if (identity.StartsWith("kiot:", StringComparison.Ordinal))
            {
                warnings.Add($"{string.Join(", ", owners)} dùng chung key Kiot …{Tail(identity["kiot:".Length..])}.");
                continue;
            }
            // Proxy tĩnh riêng trùng nhau (kể cả tài khoản đang tắt — luật GĐ4). Đã có cảnh báo "IP tĩnh" bên dưới thì khỏi nói hai lần.
            var hostPort = identity["static:".Length..];
            if (staticOwners.TryGetValue(hostPort, out var enabled) && enabled.Count >= 2) continue;
            warnings.Add($"{string.Join(", ", owners)} dùng chung proxy {hostPort}.");
        }
        // GĐ5d: cảnh báo, không chặn — user tự chọn đánh đổi so với luật "mỗi shop một IP". Chỉ host:port, không user/pass.
        foreach (var (hostPort, owners) in staticOwners)
        {
            if (owners.Count < 2) continue;
            warnings.Add($"{owners.Count} shop dùng chung 1 IP tĩnh ({hostPort}) — Shopee thấy các shop này cùng một IP: " +
                         $"{string.Join(", ", owners)}.");
        }

        if (usesShared && string.IsNullOrWhiteSpace(sharedKey) && string.IsNullOrWhiteSpace(shared.Static))
            warnings.Add("Có tài khoản đi proxy chung nhưng chưa nhập key Kiot chung hay proxy tĩnh chung — các tài khoản đó sẽ không chạy.");

        return (errors, warnings);
    }

    /// <summary>Kiểm khi phần trình duyệt bị ẩn (GĐ8): mã tài khoản hợp lệ / không trùng + giá trị mặc định cho file xuất.</summary>
    private static (List<string> Errors, List<string> Warnings) ValidateExportOnly(SellerAccountsData data)
    {
        var errors = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in data.Accounts ?? [])
        {
            var name = string.IsNullOrWhiteSpace(a.Label) ? a.Id : $"{a.Label} ({a.Id})";
            if (!IsValidId(a.Id)) { errors.Add($"Mã tài khoản '{a.Id}' không hợp lệ (chỉ chữ, số, _ và -, tối đa 64 ký tự)."); continue; }
            if (!ids.Add(a.Id)) errors.Add($"Trùng mã tài khoản '{a.Id}'.");
            foreach (var e in (a.Defaults ?? new SellerDefaults()).Validate()) errors.Add($"{name}: {e}");
        }
        return (errors, []);
    }

    /// <summary>Khoá so trùng proxy tĩnh: <c>host:port</c>, host là IP thì viết chuẩn (<c>2001:DB8:0::1</c> và <c>2001:db8::1</c> là
    /// một IP).</summary>
    internal static string StaticKey(ProxyLease lease)
    {
        var host = IPAddress.TryParse(lease.Host, out var ip)
            ? ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{ip}]" : ip.ToString()
            : lease.Host.ToLowerInvariant();
        return $"{host}:{lease.Port}";
    }

    /// <summary>Ô mật khẩu của form đăng nhập Seller Centre có <c>maxlength=16</c>: dài hơn thì gõ vào bị cắt.</summary>
    public const int ShopeePasswordMaxLength = 16;

    /// <summary>Luật dữ liệu đăng nhập (plan GĐ5b mục 1). Câu lỗi KHÔNG chứa mật khẩu, kể cả độ dài.
    /// <list type="bullet">
    /// <item>Có mật khẩu Shopee mà thiếu tài khoản → lỗi (thiếu cả hai, hoặc chỉ có tài khoản, thì được — luồng đăng nhập tự báo).</item>
    /// <item>Email xác minh và mật khẩu email phải đi cặp → thiếu một trong hai là lỗi.</item>
    /// <item>Mật khẩu Shopee dài hơn <see cref="ShopeePasswordMaxLength"/> → cảnh báo.</item>
    /// </list></summary>
    private static void ValidateLogin(SellerAccount a, string name, List<string> errors, List<string> warnings)
    {
        var hasLogin = !string.IsNullOrWhiteSpace(a.ShopeeLogin);
        var hasPass = !string.IsNullOrEmpty(a.ShopeePassword);
        var hasEmail = !string.IsNullOrWhiteSpace(a.VerifyEmail);
        var hasEmailPass = !string.IsNullOrEmpty(a.VerifyEmailPassword);

        if (hasPass && !hasLogin) errors.Add($"{name}: có mật khẩu Shopee nhưng chưa nhập tài khoản Shopee.");
        if (hasEmail && !hasEmailPass) errors.Add($"{name}: có email xác minh nhưng chưa nhập mật khẩu email.");
        if (hasEmailPass && !hasEmail) errors.Add($"{name}: có mật khẩu email nhưng chưa nhập email xác minh.");
        if (hasPass && a.ShopeePassword!.Length > ShopeePasswordMaxLength)
            warnings.Add($"{name}: mật khẩu Shopee dài hơn {ShopeePasswordMaxLength} ký tự — ô mật khẩu của Shopee chỉ nhận " +
                         $"{ShopeePasswordMaxLength} ký tự, gõ vào sẽ bị cắt.");
    }

    /// <summary>Thư mục <c>User Data</c> mặc định của Brave / Chrome / Edge cá nhân trên máy (dưới %LOCALAPPDATA%).</summary>
    internal static IReadOnlyList<string> PersonalBrowserDataDirs()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local)) return [];
        return
        [
            Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
            Path.Combine(local, "BraveSoftware", "Brave-Browser-Beta", "User Data"),
            Path.Combine(local, "BraveSoftware", "Brave-Browser-Nightly", "User Data"),
            Path.Combine(local, "Google", "Chrome", "User Data"),
            Path.Combine(local, "Google", "Chrome Beta", "User Data"),
            Path.Combine(local, "Google", "Chrome Dev", "User Data"),
            Path.Combine(local, "Google", "Chrome SxS", "User Data"),        // Canary
            Path.Combine(local, "Microsoft", "Edge", "User Data"),
            Path.Combine(local, "CocCoc", "Browser", "User Data"),
        ];
    }

    /// <summary><paramref name="path"/> trùng hoặc nằm dưới <paramref name="root"/> (đã chuẩn hoá, không phân biệt hoa thường).</summary>
    private static bool IsSameOrUnder(string path, string root)
    {
        var p = NormalizePath(path);
        var r = NormalizePath(root);
        return p.Equals(r, StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Đường dẫn để SO: bỏ tiền tố <c>\\?\</c> / <c>\\?\UNC\</c> (cùng một thư mục viết hai cách), đường dẫn đầy đủ, bỏ
    /// <c>\</c> cuối.</summary>
    internal static string NormalizePath(string path)
    {
        var s = path.Trim();
        if (s.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) s = @"\\" + s[8..];
        else if (s.StartsWith(@"\\?\", StringComparison.Ordinal)) s = s[4..];
        return Path.GetFullPath(s).TrimEnd('\\', '/');
    }

    private static bool IsRandomRegion(string? region) =>
        string.IsNullOrWhiteSpace(region) || region.Trim().Equals("random", StringComparison.OrdinalIgnoreCase);

    internal static string Tail(string s) => s.Length <= 4 ? s : s[^4..];
}
