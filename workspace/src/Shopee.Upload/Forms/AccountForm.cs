using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Shopee.Core.MassUpload.Seller;

namespace Shopee.Upload.Forms;

/// <summary>Chọn proxy của một tài khoản (plan GĐ5a mục 3).</summary>
public enum ProxyChoice
{
    /// <summary>Không có proxy riêng (<see cref="SellerAccount.ProxyOverride"/> = null) → key Kiot chung.</summary>
    Shared,
    /// <summary>Key Kiot riêng.</summary>
    KiotOwn,
    /// <summary>Proxy tĩnh <c>host:port[:user:pass]</c>.</summary>
    Static,
}

/// <summary>
/// Form một tài khoản seller. Ô số giữ dạng CHUỖI (đổi ở <see cref="SellerFormMapper.ToAccount"/>). Chuyển giữa 3 kiểu proxy
/// KHÔNG xoá ô của kiểu kia trong form (đổi ý quay lại vẫn còn), nhưng khi lưu chỉ ghi phần của kiểu đang chọn. Không phụ thuộc WPF.
/// </summary>
public sealed partial class AccountForm : ObservableObject
{
    private static readonly HashSet<string> Derived =
    [
        nameof(DisplayName), nameof(ProxySummary), nameof(OwnKiotKeyMask), nameof(IsSharedProxy), nameof(IsKiotOwnProxy),
        nameof(IsStaticProxy), nameof(IsOwnRegionRandom), nameof(ResolvedProfileDir), nameof(StaticProxyPreview),
        nameof(ShopeePasswordMask), nameof(VerifyEmailPasswordMask),
    ];

    public event Action? Edited;

    public AccountForm(string id)
    {
        Id = id;
        TemplatePaths.CollectionChanged += (_, _) => Edited?.Invoke();
    }

    /// <summary>Mã cố định (tên thư mục profile) — sinh lúc thêm, không sửa trên màn: đổi mã là đổi profile, mất đăng nhập.</summary>
    public string Id { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _label = "";

    [ObservableProperty]
    private bool _enabled = true;

    /// <summary>Thư mục profile tự chọn; rỗng = mặc định <c>upload\profiles\&lt;Id&gt;</c>.</summary>
    [ObservableProperty]
    private string _profileDir = "";

    /// <summary>Đường dẫn profile thật (<see cref="SellerAccountStore.ResolveProfileDir"/>) — màn chính điền.</summary>
    [ObservableProperty]
    private string _resolvedProfileDir = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProxySummary), nameof(IsSharedProxy), nameof(IsKiotOwnProxy), nameof(IsStaticProxy))]
    private ProxyChoice _proxy = ProxyChoice.Shared;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(OwnKiotKeyMask), nameof(ProxySummary))]
    private string _ownKiotKey = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsOwnRegionRandom))]
    private string _ownRegion = KiotRegions.Random;

    [ObservableProperty]
    private bool _ownKeepIp;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ProxySummary), nameof(StaticProxyPreview))]
    private string _staticProxy = "";

    /// <summary>Kiểu proxy riêng: Kiot chọn cổng http/socks5; proxy tĩnh không ghi scheme thì lấy kiểu này.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StaticProxyPreview))]
    private ProxyScheme _overrideType = ProxyScheme.Http;

    [ObservableProperty] private string _stockPerVariation = "";
    [ObservableProperty] private string _weightGrams = "";
    [ObservableProperty] private string _lengthCm = "";
    [ObservableProperty] private string _widthCm = "";
    [ObservableProperty] private string _heightCm = "";
    [ObservableProperty] private string _productsPerFile = "";
    [ObservableProperty] private string _dailyProductCap = "";

    [ObservableProperty] private string _bigSellerAccount = "";

    /// <summary>(GĐ9) Khoá ảnh của shop (<see cref="SellerAccount.ImageKey"/>) — không hiện, không sửa trên màn.</summary>
    [ObservableProperty] private string _imageKey = "";

    /// <summary>(GĐ8) File Excel nguồn (link SP, như app Harvest đọc).</summary>
    [ObservableProperty] private string _workbookPath = "";

    [ObservableProperty] private string _sheet = "";
    [ObservableProperty] private string _fromRow = "";
    [ObservableProperty] private string _toRow = "";

    /// <summary>Tài khoản Shopee (ô <c>loginKey</c>: SĐT / tên đăng nhập / email) — GĐ5b.</summary>
    [ObservableProperty] private string _shopeeLogin = "";

    /// <summary>Mật khẩu Shopee: ô PasswordBox, chỉ hiện <see cref="ShopeePasswordMask"/>.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShopeePasswordMask))]
    private string _shopeePassword = "";

    /// <summary>Hotmail nhận mã xác minh — app chỉ đăng nhập hộp thư cho user tự lấy mã.</summary>
    [ObservableProperty] private string _verifyEmail = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(VerifyEmailPasswordMask))]
    private string _verifyEmailPassword = "";

    /// <summary>(GĐ8) File mẫu Đăng Hàng Loạt của shop (bản nâng cao theo ngành cấp 1, hoặc một bản basic). Thêm / bớt là sửa form.</summary>
    public ObservableCollection<string> TemplatePaths { get; } = [];

    public string ShopeePasswordMask => SecretText.Mask(ShopeePassword);

    public string VerifyEmailPasswordMask => SecretText.Mask(VerifyEmailPassword);

    public string DisplayName => string.IsNullOrWhiteSpace(Label) ? $"(chưa đặt tên) {Id}" : Label.Trim();

    public bool IsSharedProxy
    {
        get => Proxy == ProxyChoice.Shared;
        set { if (value) Proxy = ProxyChoice.Shared; }
    }

    public bool IsKiotOwnProxy
    {
        get => Proxy == ProxyChoice.KiotOwn;
        set { if (value) Proxy = ProxyChoice.KiotOwn; }
    }

    public bool IsStaticProxy
    {
        get => Proxy == ProxyChoice.Static;
        set { if (value) Proxy = ProxyChoice.Static; }
    }

    public string OwnKiotKeyMask => SecretText.Mask(OwnKiotKey);

    public bool IsOwnRegionRandom => KiotRegions.IsRandom(OwnRegion);

    /// <summary>Proxy tĩnh đọc được thành gì (KHÔNG in mật khẩu — <see cref="ProxyLease.ToString"/>), hoặc lỗi.</summary>
    public string StaticProxyPreview =>
        string.IsNullOrWhiteSpace(StaticProxy) ? ""
        : StaticProxyParser.TryParse(StaticProxy, OverrideType, out var lease, out var err) ? $"Đọc được: {lease}"
        : $"Sai: {err}";

    /// <summary>Một dòng mô tả proxy cho danh sách (key che, mật khẩu không in).</summary>
    public string ProxySummary => Proxy switch
    {
        ProxyChoice.KiotOwn => $"Kiot riêng {SecretText.Mask(OwnKiotKey)}",
        ProxyChoice.Static => StaticProxyParser.TryParse(StaticProxy, OverrideType, out var lease, out _)
            ? $"Tĩnh {lease!.HostForUri}:{lease.Port}" : "Tĩnh (sai)",
        _ => "Proxy chung",
    };

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is { } n && !Derived.Contains(n)) Edited?.Invoke();
    }
}
