using System.Text;
using System.Text.Json;
using Shopee.Core.BigSeller;
using Shopee.Core.Infrastructure;
using Shopee.Upload.Forms;

namespace Shopee.Upload.Import;

/// <summary>Một shop của một tài khoản BigSeller — CHỈ phần app đăng SP cần (Id + nhãn tài khoản, tên shop, sheet, và — GĐ8 — file
/// Excel của tài khoản). Không mang Email / mật khẩu BigSeller: chép ra lớp này là để các bước sau không thể log nhầm chúng.</summary>
public sealed record BigSellerShopInfo(string AccountId, string AccountLabel, string ShopName, string Sheet, string WorkbookPath = "");

/// <summary>Đọc tài khoản BigSeller lỗi (file có mà không đọc được) — câu lỗi không có nội dung file.</summary>
public sealed class BigSellerReadException(string message) : Exception(message);

/// <summary>Nguồn shop BigSeller. App thật đọc <see cref="BigSellerStore.Shared"/> (chỉ ĐỌC — không bao giờ gọi Save/Add/Remove);
/// test tiêm danh sách giả.</summary>
public static class BigSellerShopSource
{
    /// <summary>Đường dẫn file của <see cref="BigSellerStore"/> (Store giữ private — tính lại theo đúng cách của nó) — chỉ để KIỂM.</summary>
    public static string SharedFilePath => Path.Combine(SuitePaths.ModuleDir("shared"), "bigseller.json");

    /// <summary>Đọc lại file nếu app khác (Workspace) đã sửa từ lúc app này mở — Store là singleton nạp một lần. Chỉ đọc.</summary>
    public static IReadOnlyList<BigSellerShopInfo> ReadShared()
    {
        var store = BigSellerStore.Shared;
        store.ReloadIfExternalChange();
        return Read(SharedFilePath, () => store.Accounts, store.Load);
    }

    /// <summary>Lấy tài khoản; Store rỗng mà file có vẻ có dữ liệu (<see cref="EnsureLoaded"/> báo lỗi) → nạp lại Store MỘT lần (chỉ đọc
    /// — vd lần nạp trước trúng lúc app Workspace đang ghi file) rồi mới quyết. Vẫn lỗi thì ném.</summary>
    public static IReadOnlyList<BigSellerShopInfo> Read(string path, Func<IReadOnlyList<BigSellerAccount>> current, Action reload)
    {
        var accounts = current();
        try
        {
            EnsureLoaded(path, accounts.Count);
        }
        catch (BigSellerReadException)
        {
            reload();
            accounts = current();
            EnsureLoaded(path, accounts.Count);
        }
        return Flatten(accounts);
    }

    /// <summary>Store đọc file lỗi thì nuốt lỗi và trả danh sách RỖNG (Core, không sửa) — user sẽ chỉ thấy "chưa có shop". Store rỗng
    /// mà file có nội dung → tự đọc lại file (chỉ đọc): JSON hỏng / không đọc được / thực ra có tài khoản → ném
    /// <see cref="BigSellerReadException"/>. File không có, rỗng, hoặc đúng là danh sách rỗng → không lỗi.</summary>
    public static void EnsureLoaded(string path, int loadedCount)
    {
        if (loadedCount > 0 || !File.Exists(path)) return;
        var name = Path.GetFileName(path);
        string text;
        try { text = File.ReadAllText(path, Encoding.UTF8); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BigSellerReadException($"Không đọc được {name} ({path}): {ex.GetType().Name}. File đang bị khoá hoặc thiếu quyền?");
        }
        if (text.Trim().Length == 0) return;
        int count;
        try { count = JsonSerializer.Deserialize<List<BigSellerAccount>>(text)?.Count ?? 0; }
        catch (JsonException ex)
        {
            // KHÔNG đưa ex.Message (có thể trích nội dung) — chỉ vị trí.
            throw new BigSellerReadException($"Không đọc được {name} ({path}): file hỏng, JSON sai ở dòng {(ex.LineNumber ?? 0) + 1}. " +
                                             "Mở app Workspace kiểm lại cấu hình BigSeller.");
        }
        if (count > 0)
            throw new BigSellerReadException($"Không đọc được {name}: file có {count} tài khoản nhưng lúc app nạp thì đọc lỗi. Tắt app rồi mở lại.");
    }

    /// <summary>Phẳng hoá tài khoản × shop. Nhãn tài khoản rỗng (máy user: cả 6 tài khoản) → "TK BigSeller N (abcdef)" — N là thứ tự
    /// trong danh sách (từ 1) + 6 ký tự đầu của Id. KHÔNG dùng Email.</summary>
    public static IReadOnlyList<BigSellerShopInfo> Flatten(IEnumerable<BigSellerAccount> accounts) =>
        accounts.SelectMany((a, i) => (a.Shops ?? []).Select(s => new BigSellerShopInfo(
                a.Id ?? "", AccountName(a, i), s.Name ?? "", s.ShopeeDataSheet ?? "", a.WorkbookPath ?? "")))
            .ToList();

    internal static string AccountName(BigSellerAccount a, int index)
    {
        if (!string.IsNullOrWhiteSpace(a.Label)) return a.Label.Trim();
        var id = (a.Id ?? "").Trim();
        return $"TK BigSeller {index + 1}" + (id.Length > 0 ? $" ({id[..Math.Min(6, id.Length)]})" : "");
    }
}

/// <summary>Một dòng của bảng xem trước.</summary>
/// <param name="ExistingName">Tài khoản trên app đã nhận shop này (tên hiện) — null nếu sẽ thêm.</param>
/// <param name="Note">Lý do bỏ qua (dòng lỗi) — null nếu bình thường.</param>
public sealed record ShopImportItem(string AccountId, string AccountLabel, string ShopName, string Sheet, string? ExistingName = null,
    string? Note = null, string WorkbookPath = "")
{
    /// <summary>Tên tài khoản BigSeller để hiện (nhãn, không có thì Id).</summary>
    public string AccountDisplay => string.IsNullOrWhiteSpace(AccountLabel) ? AccountId : AccountLabel.Trim();
}

/// <summary>Kết quả so: <see cref="ToAdd"/> sẽ thêm, <see cref="Existing"/> đã có (cùng tài khoản BigSeller + sheet),
/// <see cref="Skipped"/> không thêm được (thiếu Id / thiếu cả tên và sheet / trùng ngay trong BigSeller).
/// <see cref="Possible"/>: KHÔNG thêm — tên shop / sheet trùng nhãn hoặc tài khoản Shopee của một tài khoản đã có (khoá nguồn không
/// khớp vì ô nguồn gõ tay / để trống / đã sửa), có thể chính là shop đó. Cần thì user tự bấm Thêm.</summary>
public sealed record ShopImportPlan(IReadOnlyList<ShopImportItem> ToAdd, IReadOnlyList<ShopImportItem> Existing,
    IReadOnlyList<ShopImportItem> Skipped)
{
    public IReadOnlyList<ShopImportItem> Possible { get; init; } = [];

    /// <summary>(GĐ8) Shop ĐÃ CÓ trên app (cùng khoá nguồn) mà ô file Excel còn trống, tài khoản BigSeller có file → sẽ điền
    /// <see cref="WorkbookFill.WorkbookPath"/>. Ô đã có giá trị thì không đụng.</summary>
    public IReadOnlyList<WorkbookFill> WorkbookFills { get; init; } = [];

    /// <summary>Có gì để làm khi user đồng ý.</summary>
    public bool HasChanges => ToAdd.Count > 0 || WorkbookFills.Count > 0;
}

/// <summary>Điền file Excel nguồn cho một tài khoản đã có (GĐ8).</summary>
public sealed record WorkbookFill(AccountForm Form, ShopImportItem Item, string WorkbookPath);

/// <summary>
/// Nút "Nhập shop từ BigSeller" (plan GĐ5c mục 1) — hàm thuần, không WPF.
/// <list type="bullet">
/// <item>Khoá trùng = (<c>Source.BigSellerAccount</c>, <c>Source.Sheet</c>), so bằng <see cref="ImportText.Key"/> (hoa thường, dấu,
/// khoảng trắng thừa). Nhập lại lần nữa không tạo trùng.</item>
/// <item>Kiểm phụ: khoá không khớp nhưng tên shop hoặc sheet trùng <c>Label</c> / <c>ShopeeLogin</c> của tài khoản đã có →
/// <see cref="ShopImportPlan.Possible"/>, mặc định KHÔNG thêm.</item>
/// <item>Sheet = <c>ShopeeDataSheet</c>, rỗng thì lấy tên shop.</item>
/// <item>(GĐ8) File Excel nguồn = <c>WorkbookPath</c> của tài khoản BigSeller: điền cho shop mới, và cho shop đã có mà ô còn trống.</item>
/// </list>
/// </summary>
public static class BigSellerShopImport
{
    public static ShopImportPlan Plan(IEnumerable<BigSellerShopInfo> shops, IEnumerable<AccountForm> existing)
    {
        var have = new Dictionary<(string, string), AccountForm>();
        var byName = new Dictionary<string, string>();
        foreach (var f in existing)
        {
            var k = Key(f.BigSellerAccount, f.Sheet);
            if (k.Item1.Length > 0 && k.Item2.Length > 0) have.TryAdd(k, f);
            foreach (var n in new[] { f.Label, f.ShopeeLogin })
                if (ImportText.Key(n) is { Length: > 0 } nk) byName.TryAdd(nk, f.DisplayName);
        }

        var toAdd = new List<ShopImportItem>();
        var possible = new List<ShopImportItem>();
        var existingItems = new List<ShopImportItem>();
        var skipped = new List<ShopImportItem>();
        var fills = new List<WorkbookFill>();
        var seen = new HashSet<(string, string)>();
        foreach (var s in shops)
        {
            var id = s.AccountId.Trim();
            var name = s.ShopName.Trim();
            var sheet = string.IsNullOrWhiteSpace(s.Sheet) ? name : s.Sheet.Trim();
            var item = new ShopImportItem(id, s.AccountLabel.Trim(), name, sheet, WorkbookPath: (s.WorkbookPath ?? "").Trim());
            if (id.Length == 0) { skipped.Add(item with { Note = "tài khoản BigSeller không có Id" }); continue; }
            if (sheet.Length == 0) { skipped.Add(item with { Note = "shop không có tên và sheet" }); continue; }

            var k = Key(id, sheet);
            if (have.TryGetValue(k, out var owner))
            {
                existingItems.Add(item with { ExistingName = owner.DisplayName });
                if (string.IsNullOrWhiteSpace(owner.WorkbookPath) && item.WorkbookPath.Length > 0
                    && fills.All(x => !ReferenceEquals(x.Form, owner)))
                    fills.Add(new WorkbookFill(owner, item, item.WorkbookPath));
            }
            else if (byName.TryGetValue(ImportText.Key(name), out var same) || byName.TryGetValue(ImportText.Key(sheet), out same))
                possible.Add(item with { ExistingName = same, Note = $"có thể đã có (trùng tên với {same}) — không thêm" });
            else if (!seen.Add(k)) skipped.Add(item with { Note = "trùng sheet với một shop khác cùng tài khoản BigSeller" });
            else toAdd.Add(item with { ShopName = name.Length == 0 ? sheet : name });
        }
        return new ShopImportPlan(toAdd, existingItems, skipped) { Possible = possible, WorkbookFills = fills };
    }

    /// <summary>Form tài khoản mới cho một shop: nhãn = tên shop, nguồn = tài khoản BigSeller + file Excel + sheet, proxy chung (mặc
    /// định).</summary>
    public static AccountForm NewForm(ShopImportItem item, string id)
    {
        var f = SellerFormMapper.NewAccountForm(id);
        f.Label = item.ShopName;
        f.BigSellerAccount = item.AccountId;
        f.Sheet = item.Sheet;
        f.WorkbookPath = item.WorkbookPath;
        f.Proxy = ProxyChoice.Shared;
        return f;
    }

    private static (string, string) Key(string? account, string? sheet) => (ImportText.Key(account), ImportText.Key(sheet));
}
