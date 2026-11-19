using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shopee.Core.MassUpload.Seller;
using Shopee.Core.MassUpload.Seller.Login;
using Shopee.Upload.Forms;
using Shopee.Upload.Import;
using Shopee.Upload.Services;

namespace Shopee.Upload;

/// <summary>
/// Màn chính GĐ5a: proxy chung + nhịp chạy + danh sách tài khoản seller, lưu bằng <see cref="SellerAccountStore"/>, và nút mở Brave
/// thử qua proxy cho tài khoản đang chọn (<see cref="SellerBrowserService"/> → <see cref="SellerRunGate"/>).
/// <list type="bullet">
/// <item>Kiểm (<see cref="SellerAccountStore.Validate"/>) chạy lại mỗi lần sửa ô: lỗi/cảnh báo hiện ngay; có lỗi thì không lưu.</item>
/// <item>Đọc file lỗi (khoá, quyền…) → KHÔNG cho lưu: lưu lúc đó là ghi đè mất toàn bộ tài khoản.</item>
/// <item>Một Brave mỗi lúc: đang mở/đang có Brave thì khoá nút mở của mọi tài khoản.</item>
/// <item>GĐ5b: nút "Mở + đăng nhập Shopee" mở Brave chế độ đăng nhập rồi chạy luồng tự đăng nhập; Huỷ dừng luồng (Brave để nguyên);
/// "Đóng Brave" dừng luồng trước rồi đóng.</item>
/// <item>Log đi qua <see cref="SecretText.Redact"/> với mọi key/mật khẩu đang có trên form.</item>
/// <item>GĐ5c: "Nhập shop từ BigSeller" + "Nhập mật khẩu từ Excel" — chỉ sửa FORM, chưa lưu (user bấm Lưu như thường).</item>
/// <item>GĐ8: app chỉ XUẤT FILE (<c>MainViewModel.Export.cs</c>). Phần proxy / Brave / đăng nhập / nhập mật khẩu / nhịp chạy ẩn sau
/// cờ <see cref="ShowBrowserFeatures"/> (code giữ nguyên, dữ liệu vẫn lưu nguyên trong JSON); khi ẩn, kiểm KHÔNG báo gì về các phần
/// đó (user không sửa được).</item>
/// </list>
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const int MaxLogLines = 800;

    private readonly SellerAccountStore _store;
    private readonly Action<Action> _ui;
    private readonly Func<string, bool> _confirm;
    private readonly List<string> _logLines = [];
    private SellerAccountsData _loaded = new();
    private readonly Func<Action<string>, Action<KiotWaitTick>, ISellerBrowserOpener> _openerFactory;
    private ISellerBrowserOpener? _browser;
    private CancellationTokenSource? _openCts;
    private Task<SellerGateResult>? _openTask;
    private CancellationTokenSource? _loginCts;
    private Task<SellerLoginResult>? _loginTask;
    private readonly IImportPrompts _prompts;
    private readonly Func<IReadOnlyList<BigSellerShopInfo>> _bigSellerShops;

    /// <summary>Thông báo GHIM suốt phiên (không bị lần kiểm sau xoá): đọc file lỗi / file cũ hỏng đã đổi tên.</summary>
    private readonly List<string> _pinnedErrors = [];
    private readonly List<string> _pinnedWarnings = [];

    /// <summary>Mã tài khoản đã xoá trên màn, chưa lưu — lúc lưu thêm vào <see cref="SellerAccountsData.RetiredIds"/>.</summary>
    private readonly List<string> _retired = [];

    /// <summary>Tài khoản đang có lỗi chặn mở Brave (theo lần kiểm gần nhất): Id → lỗi.</summary>
    private Dictionary<string, List<string>> _blocked = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="ui">Chạy một việc trên luồng UI (log/đếm ngược từ luồng nền).</param>
    /// <param name="confirm">Hỏi Có/Không.</param>
    /// <param name="openerFactory">Dựng chỗ mở Brave (nhận sink log + đếm ngược). Mặc định <see cref="SellerBrowserService"/> thật;
    /// test tiêm bản giả.</param>
    /// <param name="prompts">Hộp thoại của hai nút nhập (GĐ5c). Mặc định <see cref="WpfImportPrompts"/>.</param>
    /// <param name="bigSellerShops">Đọc shop BigSeller (GĐ5c). Mặc định <see cref="BigSellerShopSource.ReadShared"/> (chỉ đọc
    /// <c>bigseller.json</c>); test tiêm danh sách giả.</param>
    /// <param name="export">Phần chạy thật của panel xuất (GĐ8). null = <see cref="DisabledExportService"/> (không đọc/ghi gì).</param>
    /// <param name="exportPrompts">Hộp thoại của panel xuất. null = <see cref="WpfExportPrompts"/>.</param>
    /// <param name="showBrowserFeatures">false (app GĐ8) = ẩn proxy / Brave / đăng nhập; kiểm bỏ qua các phần đó.</param>
    public MainViewModel(SellerAccountStore store, Action<Action> ui, Func<string, bool> confirm,
        Func<Action<string>, Action<KiotWaitTick>, ISellerBrowserOpener>? openerFactory = null,
        IImportPrompts? prompts = null, Func<IReadOnlyList<BigSellerShopInfo>>? bigSellerShops = null,
        IExportService? export = null, IExportPrompts? exportPrompts = null, bool showBrowserFeatures = true)
    {
        ShowBrowserFeatures = showBrowserFeatures;
        _store = store;
        _ui = ui;
        _confirm = confirm;
        _openerFactory = openerFactory ?? ((log, waiting) => new SellerBrowserService(log, waiting));
        _prompts = prompts ?? new WpfImportPrompts();
        _bigSellerShops = bigSellerShops ?? BigSellerShopSource.ReadShared;
        Settings = new SettingsForm();
        InitExport(export, exportPrompts);
        Load();
    }

    /// <summary>Hiện phần proxy / Brave / đăng nhập Shopee / nhập mật khẩu / nhịp chạy (GĐ5a–5d). App GĐ8: false
    /// (<see cref="UploadFeatures.ShowBrowserFeatures"/>).</summary>
    public bool ShowBrowserFeatures { get; }

    /// <summary>Ngược của <see cref="ShowBrowserFeatures"/> — cho binding hiện panel xuất.</summary>
    public bool ShowExportFeatures => !ShowBrowserFeatures;

    public SettingsForm Settings { get; private set; }

    public ObservableCollection<AccountForm> Accounts { get; } = [];

    public ObservableCollection<string> Errors { get; } = [];

    public ObservableCollection<string> Warnings { get; } = [];

    public IReadOnlyList<string> Regions => KiotRegions.All;

    public IReadOnlyList<ProxyScheme> Schemes => ProxySchemes.All;

    public string StorePath => _store.FilePath;

    /// <summary>Đọc file lỗi → khoá nút Lưu cả phiên.</summary>
    public bool LoadFailed { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(ToggleCommand), nameof(OpenBraveCommand), nameof(OpenAndLoginCommand),
        nameof(ExportCommand), nameof(PickWorkbookCommand), nameof(AddTemplatesCommand), nameof(RetryFailedImagesCommand))]
    private AccountForm? _selected;

    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _saveStatus = "";
    [ObservableProperty] private string _logText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenBraveCommand), nameof(OpenAndLoginCommand), nameof(CancelOpenCommand), nameof(DeleteCommand))]
    private bool _isOpening;

    /// <summary>Đang chạy luồng đăng nhập Shopee trên Brave chế độ đăng nhập (GĐ5b).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenBraveCommand), nameof(OpenAndLoginCommand), nameof(CancelOpenCommand))]
    private bool _isLoggingIn;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenBraveCommand), nameof(OpenAndLoginCommand), nameof(CloseBraveCommand))]
    private bool _isClosing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSession))]
    [NotifyCanExecuteChangedFor(nameof(OpenBraveCommand), nameof(OpenAndLoginCommand), nameof(CloseBraveCommand), nameof(DeleteCommand))]
    private SellerBrowserSession? _session;

    /// <summary>Tài khoản đang có Brave mở (hoặc đang mở dở).</summary>
    [ObservableProperty] private string? _browserAccountId;

    [ObservableProperty] private string _browserStatus = "Chưa mở Brave.";
    [ObservableProperty] private string _countdown = "";

    public bool HasSession => Session is not null;

    public bool HasErrors => Errors.Count > 0;

    public bool HasWarnings => Warnings.Count > 0;

    // ─────────────── nạp / kiểm / lưu ───────────────

    private void Load()
    {
        try
        {
            _loaded = _store.Load();
            // GĐ9: tài khoản cũ chưa có khoá ảnh → sinh ngay và LƯU LUÔN vào file (chỉ thêm trường đó, dữ liệu vừa đọc thành công),
            // để khoá không đổi giữa các lần mở app dù user không bấm Lưu. File cũ hỏng (đã đổi tên) thì không đụng.
            if (_store.LastCorruptBackupPath is null && SellerAccountStore.AssignMissingImageKeys(_loaded) is var n && n > 0)
            {
                if (_store.SaveImageKeys(_loaded)) Log($"Đã tạo khoá ảnh cho {n} shop và lưu vào {Path.GetFileName(_store.FilePath)}.");
                else _pinnedWarnings.Add($"Đã tạo khoá ảnh cho {n} shop nhưng chưa lưu được — bấm Lưu, kẻo lần mở sau khoá đổi (MD5 ảnh đổi).");
            }
        }
        catch (Exception ex)
        {
            LoadFailed = true;
            _loaded = new SellerAccountsData();
            Log($"Không đọc được {_store.FilePath}: {ex.Message}");
        }

        if (LoadFailed)
        {
            _pinnedErrors.Add($"Không đọc được {Path.GetFileName(_store.FilePath)} — KHÔNG lưu để khỏi ghi đè mất tài khoản. Đóng app, " +
                "kiểm file (đang bị khoá? quyền?) rồi mở lại.");
            SaveStatus = "Không lưu được (đọc file lỗi).";
        }
        else if (_store.LastCorruptBackupPath is { } bak)
        {
            _pinnedWarnings.Add($"File cấu hình cũ bị hỏng, đã đổi tên thành {bak}. Các tài khoản cũ nằm trong file đó — mở bằng Notepad " +
                $"để chép lại, hoặc sửa xong thì tắt app rồi đổi tên lại thành {Path.GetFileName(_store.FilePath)}.");
            SaveStatus = "File cấu hình cũ hỏng — đã đổi tên (xem cảnh báo).";
            Log($"File cấu hình cũ hỏng — đã đổi tên thành {bak}.");
        }
        else
        {
            SaveStatus = File.Exists(_store.FilePath)
                ? $"Đã nạp {_loaded.Accounts.Count} {(ShowBrowserFeatures ? "tài khoản" : "shop")}." : "Chưa có cấu hình — nhập rồi bấm Lưu.";
        }

        Settings = SellerFormMapper.ToForm(_loaded.Settings);
        Settings.Edited += OnEdited;
        foreach (var a in _loaded.Accounts) AddForm(SellerFormMapper.ToForm(a));
        Selected = Accounts.FirstOrDefault();
        Revalidate();
        IsDirty = false;
    }

    private void AddForm(AccountForm f)
    {
        f.Edited += OnEdited;
        Accounts.Add(f);
    }

    private void OnEdited()
    {
        if (_editBatch > 0)
        {
            _editPending = true;
            return;
        }
        IsDirty = true;
        Revalidate();
    }

    /// <summary>Đang ghi hàng loạt (2 nút nhập GĐ5c): mỗi ô ghi vào phát <see cref="AccountForm.Edited"/> → <see cref="Revalidate"/>
    /// (kiểm cả danh sách, O(n²)) — 40 tài khoản × 4 ô là vài giây treo UI. Gom lại: chỉ kiểm MỘT lần khi xong.</summary>
    private int _editBatch;
    private bool _editPending;

    private void RunEditBatch(Action apply)
    {
        _editBatch++;
        try { apply(); }
        finally
        {
            _editBatch--;
            if (_editBatch == 0 && _editPending)
            {
                _editPending = false;
                OnEdited();
            }
        }
    }

    /// <summary>Số lần <see cref="Revalidate"/> đã chạy — cho test đo việc gom kiểm.</summary>
    internal int RevalidateCount { get; private set; }

    /// <summary>Đọc form → kiểm như lúc lưu → hiện lỗi/cảnh báo; cập nhật đường dẫn profile thật của từng tài khoản.</summary>
    private void Revalidate()
    {
        RevalidateCount++;
        var (data, parseErrors) = SellerFormMapper.ToData(Settings, Accounts, _loaded, _retired);
        var (errors, warnings) = _store.Validate(data, ShowBrowserFeatures);
        ShowValidation([.. parseErrors, .. errors], warnings);
        foreach (var f in Accounts) f.ResolvedProfileDir = ResolveProfileText(f);
        // Gộp theo Id (KHÔNG ToDictionary): file sửa tay có thể có 2 tài khoản trùng Id / khác hoa-thường / cùng rỗng — ném ở đây là
        // app chết ngay khi mở. Hai tài khoản trùng Id cùng bị khoá (lỗi "Trùng mã" biến mất khi bỏ Id đó).
        var blocked = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Accounts)
        {
            var errs = BlockingErrors(f, data, errors);
            if (errs.Count == 0) continue;
            if (blocked.TryGetValue(f.Id, out var list)) list.AddRange(errs.Where(e => !list.Contains(e)));
            else blocked[f.Id] = errs;
        }
        _blocked = blocked;
        OpenBraveCommand.NotifyCanExecuteChanged();
        OpenAndLoginCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Lỗi chặn mở Brave của MỘT tài khoản: lỗi đọc ô của nó + lỗi <see cref="SellerAccountStore.Validate"/> dính tới nó
    /// (trùng profile, proxy sai, profile tương đối…). Cách tách: kiểm lại dữ liệu khi BỎ tài khoản này — lỗi nào biến mất là của
    /// nó (không dò chuỗi; lỗi nhịp chạy không dính tài khoản nào nên không chặn).</summary>
    private List<string> BlockingErrors(AccountForm f, SellerAccountsData data, IReadOnlyList<string> allErrors)
    {
        var result = new List<string>();
        SellerFormMapper.ToAccount(f, result);
        var without = new SellerAccountsData
        {
            Settings = data.Settings,
            Accounts = data.Accounts.Where(a => !string.Equals(a.Id, f.Id, StringComparison.OrdinalIgnoreCase)).ToList(),
        };
        var rest = _store.Validate(without, ShowBrowserFeatures).Errors;
        result.AddRange(allErrors.Where(e => !rest.Remove(e)));
        return result;
    }

    private string ResolveProfileText(AccountForm f)
    {
        try { return _store.ResolveProfileDir(new SellerAccount { Id = f.Id, ProfileDir = f.ProfileDir.Trim() }); }
        catch (Exception ex) { return $"(đường dẫn sai: {ex.Message})"; }
    }

    private void ShowValidation(IEnumerable<string> errors, IEnumerable<string> warnings)
    {
        Errors.Clear();
        foreach (var e in _pinnedErrors.Concat(errors)) Errors.Add(Redact(e));
        Warnings.Clear();
        foreach (var w in _pinnedWarnings.Concat(warnings)) Warnings.Add(Redact(w));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasWarnings));
    }

    private bool CanSave() => !LoadFailed;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => TrySave();

    /// <summary>Lưu. true = đã ghi file.</summary>
    public bool TrySave()
    {
        if (LoadFailed) return false;
        var (data, parseErrors) = SellerFormMapper.ToData(Settings, Accounts, _loaded, _retired);
        if (parseErrors.Count > 0)
        {
            var (errors, warnings) = _store.Validate(data, ShowBrowserFeatures);
            ShowValidation([.. parseErrors, .. errors], warnings);
            SaveStatus = "Chưa lưu — còn ô nhập sai.";
            return false;
        }
        var r = _store.Save(data, ShowBrowserFeatures);
        ShowValidation(r.Errors, r.Warnings);
        if (!r.Saved)
        {
            SaveStatus = "Chưa lưu — còn lỗi.";
            return false;
        }
        foreach (var old in _loaded.Accounts.Where(o => o.ProxyOverride is not null))
        {
            if (data.Accounts.FirstOrDefault(a => string.Equals(a.Id, old.Id, StringComparison.OrdinalIgnoreCase)) is { ProxyOverride: null } now)
                Log($"Cảnh báo: đã bỏ proxy riêng của {(string.IsNullOrWhiteSpace(now.Label) ? now.Id : $"{now.Label} ({now.Id})")} — từ giờ đi proxy chung.");
        }
        _loaded = data;
        _retired.Clear();
        IsDirty = false;
        SaveStatus = $"Đã lưu lúc {DateTime.Now:HH:mm:ss}.";
        Log($"Đã lưu cấu hình ({data.Accounts.Count} {(ShowBrowserFeatures ? "tài khoản" : "shop")}) vào {_store.FilePath}.");
        return true;
    }

    // ─────────────── danh sách tài khoản ───────────────

    [RelayCommand]
    private void Add()
    {
        var id = SellerFormMapper.NewId(Accounts.Select(a => a.Id),
            id => Directory.Exists(ResolveProfileText(new AccountForm(id))),
            [.. _loaded.RetiredIds, .. _retired]);
        var f = SellerFormMapper.NewAccountForm(id);
        AddForm(f);
        Selected = f;
        OnEdited();
    }

    // ─────────────── nhập từ BigSeller / Excel (GĐ5c) ───────────────

    /// <summary>Mỗi shop BigSeller (tài khoản × shop) chưa có tài khoản nào cùng <c>Source.BigSellerAccount</c> + <c>Source.Sheet</c>
    /// → một tài khoản mới (nhãn = tên shop, proxy chung, mã tự sinh như nút Thêm). Hiện bảng xem trước; user đồng ý mới thêm vào
    /// FORM — chưa lưu. Không ghi gì vào <c>bigseller.json</c>; không log email / mật khẩu BigSeller (không đọc tới chúng).</summary>
    [RelayCommand]
    private void ImportShops()
    {
        IReadOnlyList<BigSellerShopInfo> shops;
        try { shops = _bigSellerShops(); }
        catch (Exception ex)
        {
            Log($"Nhập shop từ BigSeller: không đọc được tài khoản BigSeller — {ex.Message}");
            _prompts.ShowSummary("Nhập shop từ BigSeller", Redact($"Không đọc được tài khoản BigSeller: {ex.Message}"), isError: true);
            return;
        }
        if (shops.Count == 0)
        {
            Log("Nhập shop từ BigSeller: chưa có shop nào trong cấu hình BigSeller.");
            _prompts.ShowSummary("Nhập shop từ BigSeller", "Chưa có shop nào trong cấu hình BigSeller (app Workspace → BigSeller).");
            return;
        }

        var plan = BigSellerShopImport.Plan(shops, Accounts);
        if (!_prompts.ConfirmShops(plan))
        {
            Log($"Nhập shop từ BigSeller: đã huỷ (có {plan.ToAdd.Count} shop mới, {plan.Existing.Count} đã có).");
            return;
        }

        AccountForm? first = null;
        RunEditBatch(() =>
        {
            foreach (var item in plan.ToAdd)
            {
                var id = SellerFormMapper.NewId(Accounts.Select(a => a.Id),
                    x => Directory.Exists(ResolveProfileText(new AccountForm(x))),
                    [.. _loaded.RetiredIds, .. _retired]);
                var f = BigSellerShopImport.NewForm(item, id);
                AddForm(f);
                first ??= f;
            }
            if (first is not null)
            {
                Selected = first;
                OnEdited();
            }
            // GĐ8: shop đã có mà ô file Excel còn trống → điền file của tài khoản BigSeller.
            foreach (var fill in plan.WorkbookFills) fill.Form.WorkbookPath = fill.WorkbookPath;
        });

        var text = $"Đã thêm {plan.ToAdd.Count} shop" +
                   (plan.ToAdd.Count > 0 ? ": " + string.Join(", ", plan.ToAdd.Select(i => $"{i.ShopName} [{i.AccountDisplay}]")) : "") +
                   $". Đã có sẵn {plan.Existing.Count} shop." +
                   (plan.Possible.Count > 0
                       ? $" Có thể đã có — KHÔNG thêm {plan.Possible.Count}: " +
                         string.Join("; ", plan.Possible.Select(i => $"{i.ShopName} [{i.AccountDisplay}] trùng tên với {i.ExistingName}")) +
                         " (đúng là shop khác thì bấm Thêm rồi tự điền nguồn)."
                       : "") +
                   (plan.Skipped.Count > 0
                       ? $" Bỏ qua {plan.Skipped.Count}: " + string.Join("; ", plan.Skipped.Select(i => $"{i.ShopName} [{i.AccountDisplay}] — {i.Note}")) + "."
                       : "") +
                   (plan.WorkbookFills.Count > 0
                       ? $" Đã điền file Excel nguồn cho {plan.WorkbookFills.Count} shop đã có: " +
                         string.Join(", ", plan.WorkbookFills.Select(x => x.Form.DisplayName)) + "."
                       : "") +
                   (plan.HasChanges ? " Chưa lưu — kiểm rồi bấm Lưu." : "");
        Log("Nhập shop từ BigSeller: " + text);
        _prompts.ShowSummary("Nhập shop từ BigSeller", Redact(text));
    }

    /// <summary>Chọn file .xlsx → đọc 4 cột (ACC, Pass, mail, pass Mail) → khớp tài khoản trên form (tài khoản Shopee, rồi nhãn) →
    /// ô đang có giá trị khác thì hỏi MỘT lần (ghi đè tất cả / chỉ điền ô trống / huỷ) → điền vào FORM, chưa lưu. File thiếu cột
    /// thì báo, không ghi gì. Log và hộp tóm tắt không có mật khẩu.</summary>
    [RelayCommand]
    private void ImportPasswords()
    {
        const string title = "Nhập mật khẩu từ Excel";
        var path = _prompts.PickPasswordFile();
        if (string.IsNullOrWhiteSpace(path)) return;
        var fileName = Path.GetFileName(path);

        PasswordSheet sheet;
        try { sheet = PasswordSheetReader.Read(path); }
        catch (PasswordFileException ex)
        {
            Log($"{title}: {fileName} — {ex.Message} Không điền gì.");
            _prompts.ShowSummary(title, Redact($"{fileName}: {ex.Message}\n\nKhông điền gì."), isError: true);
            return;
        }

        var plan = PasswordImport.Plan(sheet.Rows, Accounts);
        PasswordImportResult? result = null;
        if (plan.Matches.Count > 0)
        {
            var choice = plan.ConflictFields > 0
                ? _prompts.AskOverwrite(plan.ConflictAccounts, plan.ConflictFields)
                : OverwriteChoice.FillEmptyOnly;
            if (choice != OverwriteChoice.Cancel) RunEditBatch(() => result = PasswordImport.Apply(plan, choice));
        }
        else
        {
            result = PasswordImport.Apply(plan, OverwriteChoice.FillEmptyOnly);   // không khớp gì — kết quả rỗng
        }

        var summary = PasswordImport.Summary(fileName, sheet, plan, result);
        foreach (var line in summary.Split(Environment.NewLine)) Log($"{title}: {line}");
        _prompts.ShowSummary(title, Redact(summary));
    }

    private bool CanDelete() => Selected is not null && !IsBrowserOf(Selected);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        var f = Selected;
        if (f is null || IsBrowserOf(f)) return;
        if (!_confirm(ShowBrowserFeatures
                ? $"Xoá tài khoản \"{f.DisplayName}\" ({f.Id})?\n\nThư mục profile Brave (phiên đăng nhập Shopee) KHÔNG bị xoá:\n" + f.ResolvedProfileDir
                : $"Xoá shop \"{f.DisplayName}\" ({f.Id}) khỏi danh sách?"))
            return;
        var i = Accounts.IndexOf(f);
        f.Edited -= OnEdited;
        Accounts.Remove(f);
        if (!_retired.Contains(f.Id, StringComparer.OrdinalIgnoreCase)) _retired.Add(f.Id);
        Selected = Accounts.Count == 0 ? null : Accounts[Math.Min(i, Accounts.Count - 1)];
        OnEdited();
    }

    private bool HasSelected() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelected))]
    private void Toggle()
    {
        if (Selected is { } f) f.Enabled = !f.Enabled;
    }

    private bool IsBrowserOf(AccountForm f) =>
        (IsOpening || Session is not null) && string.Equals(BrowserAccountId, f.Id, StringComparison.Ordinal);

    // ─────────────── mở / đóng Brave ───────────────

    /// <summary>Phần trình duyệt bị ẩn (<see cref="ShowBrowserFeatures"/> = false, GĐ8) → luôn false: không chỉ dựa vào nút bị ẩn.</summary>
    private bool CanOpenBrave() =>
        ShowBrowserFeatures && Selected is not null && !IsOpening && !IsLoggingIn && !IsClosing && Session is null && !_blocked.ContainsKey(Selected.Id);

    /// <summary>Mở Brave qua proxy cho tài khoản đang chọn, theo giá trị ĐANG TRÊN FORM (chưa cần lưu). Trước khi gọi gate: kiểm
    /// lại TOÀN BỘ form như lúc lưu — tài khoản này có lỗi (ô nhập sai, trùng profile với tài khoản khác, proxy sai…) thì từ chối,
    /// báo lý do. Mọi kiểm proxy/IP do <see cref="SellerRunGate"/> làm.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenBrave))]
    private Task OpenBraveAsync() => OpenCoreAsync(login: false);

    /// <summary>GĐ5b: mở Brave chế độ đăng nhập (cùng gate, cùng mọi luật proxy/IP, thêm CDP loopback) rồi chạy luồng tự đăng nhập
    /// Shopee bằng tài khoản ĐANG TRÊN FORM. Xong (được hay dừng) thì ngắt Playwright, Brave để nguyên; "Đóng Brave" đóng như thường.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenBrave))]
    private Task OpenAndLoginAsync() => OpenCoreAsync(login: true);

    private async Task OpenCoreAsync(bool login)
    {
        var f = Selected;
        if (!ShowBrowserFeatures || f is null || IsOpening || IsLoggingIn || IsClosing || Session is not null) return;

        Log($"── {f.DisplayName} ({f.Id}): {(login ? "mở Brave qua proxy + đăng nhập Shopee" : "mở Brave qua proxy")} ──");
        var (data, _) = SellerFormMapper.ToData(Settings, Accounts, _loaded, _retired);
        var blocking = BlockingErrors(f, data, _store.Validate(data).Errors);
        if (blocking.Count > 0)
        {
            foreach (var e in blocking) Log("Lỗi: " + e);
            BrowserStatus = Redact($"Không mở Brave — tài khoản đang có lỗi: {blocking[0]}");
            return;
        }

        var account = SellerFormMapper.ToAccount(f, [], _loaded.Accounts.FirstOrDefault(a => string.Equals(a.Id, f.Id, StringComparison.OrdinalIgnoreCase)));
        // Lỗi ô nhịp chạy không dính tới việc mở Brave → bỏ qua.
        var settings = SellerFormMapper.ToSettings(Settings, _loaded.Settings, []);

        string profile;
        try { profile = _store.ResolveProfileDir(account); }
        catch (Exception ex)
        {
            Log($"Thư mục profile sai: {ex.Message}");
            BrowserStatus = "Không mở Brave — thư mục profile sai.";
            return;
        }

        _browser ??= _openerFactory(LogFromWorker, tick => _ui(() =>
            Countdown = $"Chờ Kiot …{tick.KeyTail} cho đổi IP: còn {tick.Remaining:mm\\:ss}"));

        if (SellerProxySource.Resolve(account, settings, out _) is { } source) Log($"Lấy proxy: {source}.");
        Log($"Profile: {profile}");

        BrowserAccountId = account.Id;
        IsOpening = true;
        BrowserStatus = $"Đang lấy proxy, kiểm IP và mở Brave cho {f.DisplayName}…";
        _openCts = new CancellationTokenSource();
        try
        {
            _openTask = login
                ? _browser.OpenForLoginAsync(account, settings, profile, _openCts.Token)
                : _browser.OpenAsync(account, settings, profile, _openCts.Token);
            var r = await _openTask;
            if (r.Ok)
            {
                Session = r.Session;
                BrowserStatus = $"Brave đang mở cho {f.DisplayName} — IP ra {r.ExitIp}.";
                Log(login
                    ? $"Brave đã mở (chế độ đăng nhập), IP ra {r.ExitIp}."
                    : $"Brave đã mở trang trắng, IP ra {r.ExitIp}. Tự gõ browserleaks.com/webrtc để kiểm, rồi vào banhang.shopee.vn đăng nhập.");
            }
            else
            {
                BrowserStatus = Redact($"Không mở Brave: {r.Reason}");
                Log($"Không mở Brave ({r.Failure}): {r.Reason}");
            }
        }
        catch (OperationCanceledException)
        {
            BrowserStatus = "Đã huỷ — không mở Brave.";
            Log("Đã huỷ mở Brave.");
        }
        catch (Exception ex)
        {
            BrowserStatus = Redact($"Lỗi: {ex.Message}");
            Log($"Lỗi khi mở Brave: {ex.Message}");
        }
        finally
        {
            _openCts.Dispose();
            _openCts = null;
            Countdown = "";
            IsOpening = false;
            if (Session is null) BrowserAccountId = null;
        }

        if (login && !_shuttingDown && Session is { } opened) await RunLoginAsync(f, account, opened);
    }

    /// <summary>Đã gọi <see cref="ShutdownAsync"/>.</summary>
    private bool _shuttingDown;

    /// <summary>Brave chế độ đăng nhập giữ cổng CDP (127.0.0.1) tới khi đóng — chưa có "đăng nhập xong thì mở lại sạch" (GĐ5).</summary>
    internal const string CdpStillOpenNote = "Cửa sổ này còn cổng điều khiển trên máy — xong việc thì bấm Đóng Brave.";

    /// <summary>Luồng đăng nhập trên Brave vừa mở chế độ đăng nhập. Trạng thái từng bước lên <see cref="BrowserStatus"/> (đã che).
    /// Huỷ / lỗi / xong đều để nguyên Brave (phiên vẫn là <see cref="Session"/>).</summary>
    private async Task RunLoginAsync(AccountForm f, SellerAccount account, SellerBrowserSession session)
    {
        var creds = SellerLoginCredentials.FromAccount(account);
        Log($"Đăng nhập Shopee: {creds}.");
        IsLoggingIn = true;
        _loginCts = new CancellationTokenSource();
        try
        {
            _loginTask = _browser!.LoginAsync(session, creds, LogFromWorker,
                s => _ui(() => { if (IsLoggingIn) BrowserStatus = Redact($"{f.DisplayName}: {s}"); }), _loginCts.Token);
            var r = await _loginTask;
            BrowserStatus = Redact($"{f.DisplayName}: {r.Message}" + (r.Ok ? $" IP ra {session.ExitIp}." : "") + " " + CdpStillOpenNote);
            Log($"Kết quả đăng nhập ({r.Outcome}): {r.Message}");
        }
        catch (OperationCanceledException)
        {
            BrowserStatus = "Đã huỷ đăng nhập — cửa sổ Brave để nguyên. " + CdpStillOpenNote;
            Log("Đã huỷ đăng nhập Shopee.");
        }
        catch (Exception ex)
        {
            BrowserStatus = Redact($"Lỗi khi đăng nhập: {ex.Message}");
            Log($"Lỗi khi đăng nhập Shopee: {ex.Message}");
        }
        finally
        {
            _loginCts.Dispose();
            _loginCts = null;
            IsLoggingIn = false;
        }
    }

    private bool CanCancelOpen() => IsOpening || IsLoggingIn;

    /// <summary>Huỷ lượt mở đang chạy, hoặc luồng đăng nhập đang chạy (Brave để nguyên).</summary>
    [RelayCommand(CanExecute = nameof(CanCancelOpen))]
    private void CancelOpen()
    {
        try { _openCts?.Cancel(); } catch (ObjectDisposedException) { }
        try { _loginCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    /// <summary>Trần chờ luồng đăng nhập dừng sau khi huỷ (Core cắt kết nối Playwright ngay khi huỷ, thường xong dưới 1 s).</summary>
    internal TimeSpan LoginStopTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Huỷ luồng đăng nhập (nếu đang chạy) và đợi nó ngắt Playwright xong — tối đa <see cref="LoginStopTimeout"/>, quá thì
    /// đi tiếp (đóng Brave / tắt app không bị treo theo một lệnh Playwright kẹt).</summary>
    private async Task StopLoginAsync()
    {
        try { _loginCts?.Cancel(); } catch (ObjectDisposedException) { }
        if (_loginTask is { IsCompleted: false } t)
        {
            var done = await Task.WhenAny(t, Task.Delay(LoginStopTimeout));
            if (done != t) Log($"Luồng đăng nhập chưa dừng sau {LoginStopTimeout.TotalSeconds:0} s — đi tiếp.");
            _ = t.ContinueWith(x => _ = x.Exception, TaskScheduler.Default);
        }
    }

    private bool CanCloseBrave() => Session is not null && !IsClosing;

    /// <summary>Đóng Brave (cả Brave chế độ đăng nhập): đang đăng nhập dở thì huỷ luồng, đợi ngắt Playwright, rồi mới đóng.</summary>
    [RelayCommand(CanExecute = nameof(CanCloseBrave))]
    private async Task CloseBraveAsync()
    {
        var s = Session;
        if (s is null || IsClosing) return;
        IsClosing = true;
        if (IsLoggingIn)
        {
            BrowserStatus = "Đang dừng đăng nhập…";
            await StopLoginAsync();
        }
        BrowserStatus = "Đang đóng Brave…";
        var ok = false;
        try { ok = await s.CloseAsync(); }
        catch (Exception ex) { Log($"Lỗi khi đóng Brave: {ex.Message}"); }
        IsClosing = false;
        if (!ok)
        {
            // Chưa xác nhận Brave thoát → GIỮ phiên: vẫn chặn mở Brave mới (2 Brave cùng lúc), cho bấm Đóng lại.
            BrowserStatus = "Brave chưa đóng hẳn — bấm \"Đóng Brave\" lại.";
            Log("Brave chưa đóng hẳn — chưa cho mở Brave khác.");
            return;
        }
        Session = null;
        BrowserAccountId = null;
        BrowserStatus = "Đã đóng Brave.";
        Log("Đã đóng Brave.");
    }

    private bool _checkingExit;

    /// <summary>Gọi định kỳ từ cửa sổ: Brave app mở đã thoát (user tự đóng cửa sổ) → dọn phiên như bấm "Đóng Brave".
    /// Hỏi <c>Process.HasExited</c>; không hỏi được (không có Process, hoặc Process đã bị launcher dispose sau một lượt đóng thất bại)
    /// thì QUÉT tiến trình theo profile (<see cref="ISellerBrowserOpener.IsBrowserRunning"/>, chạy ngoài luồng UI).</summary>
    public async Task CheckBrowserExitedAsync()
    {
        // Đang đăng nhập: luồng tự thấy tab/cửa sổ bị đóng và kết thúc; quét sau đó.
        if (Session is not { } s || IsClosing || IsOpening || IsLoggingIn || _checkingExit || _browser is null) return;
        _checkingExit = true;
        bool exited;
        try
        {
            bool? byProcess = null;
            if (s.Process is { } p)
            {
                try { byProcess = p.HasExited; }
                catch (InvalidOperationException) { }   // đã dispose / không còn gắn tiến trình
            }
            exited = byProcess ?? !await Task.Run(() => _browser.IsBrowserRunning(s));
        }
        catch { return; }
        finally { _checkingExit = false; }
        if (!exited || !ReferenceEquals(Session, s) || IsClosing) return;
        Log("Cửa sổ Brave đã bị đóng ngoài app — dọn phiên.");
        await CloseBraveAsync();
    }

    /// <summary>Tắt app: huỷ lượt mở đang chạy, đợi nó dừng, rồi đóng Brave (kể cả Brave vừa mở xong đúng lúc huỷ). Mỗi phiên đóng
    /// MỘT lượt: phiên vừa mở xong thường chính là <see cref="Session"/> — đóng lại lần nữa sau một lượt thất bại là chờ thêm ~25 s.</summary>
    public async Task ShutdownAsync()
    {
        _shuttingDown = true;   // Brave chế độ đăng nhập mở xong đúng lúc này → KHÔNG bắt đầu đăng nhập nữa
        await ShutdownExportAsync();
        CancelOpen();
        await StopLoginAsync();
        SellerBrowserSession? closed = null;
        if (_openTask is { } t)
        {
            try
            {
                var r = await t;
                if (r.Session is { } late)
                {
                    closed = late;
                    await late.CloseAsync();
                }
            }
            catch { }
        }
        if (Session is { } s && !ReferenceEquals(s, closed))
        {
            try { await s.CloseAsync(); }
            catch { }
        }
    }

    // ─────────────── log ───────────────

    private void LogFromWorker(string message) => _ui(() => Log(message));

    /// <summary>Che mọi key Kiot / mật khẩu proxy ĐANG có trên form khỏi một chuỗi sắp hiện ra màn hoặc log.</summary>
    public string Redact(string text) => SecretText.Redact(text, SellerFormMapper.Secrets(Settings, Accounts));
    /// <summary>Thêm một dòng log (luồng UI). Che mọi key/mật khẩu đang có trên form.</summary>
    public void Log(string message)
    {
        var text = Redact(message);
        _logLines.Add($"{DateTime.Now:HH:mm:ss}  {text}");
        if (_logLines.Count > MaxLogLines) _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
        LogText = string.Join(Environment.NewLine, _logLines);
    }
}
