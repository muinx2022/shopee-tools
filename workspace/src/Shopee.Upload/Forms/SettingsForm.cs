using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Shopee.Core.MassUpload.Seller;

namespace Shopee.Upload.Forms;

/// <summary>Vùng Kiot cho ô chọn (như <c>ShopeeAccount.Region</c>).</summary>
public static class KiotRegions
{
    public const string Random = "random";

    public static IReadOnlyList<string> All { get; } = [Random, "bac", "trung", "nam"];

    public static bool IsRandom(string? region) =>
        string.IsNullOrWhiteSpace(region) || region.Trim().Equals(Random, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Các kiểu proxy cho ô chọn.</summary>
public static class ProxySchemes
{
    public static IReadOnlyList<ProxyScheme> All { get; } = [ProxyScheme.Http, ProxyScheme.Socks5];
}

/// <summary>
/// Form "Proxy chung" + "Nhịp chạy" (plan GĐ5a mục 3). Ô số giữ dạng CHUỖI để gõ sai không bị WPF nuốt mất — đổi sang số ở
/// <see cref="SellerFormMapper.ToSettings"/>, sai thì báo lỗi. Không phụ thuộc WPF.
/// </summary>
public sealed partial class SettingsForm : ObservableObject
{
    /// <summary>Thuộc tính suy ra (không phải ô nhập) — đổi chúng không tính là "có thay đổi chưa lưu".</summary>
    private static readonly HashSet<string> Derived =
        [nameof(KiotKeyMask), nameof(IsRandomRegion), nameof(StaticProxyPreview), nameof(ProxyInUse), nameof(StaticIgnoredNote),
         nameof(DownloadMissingFromCdn)];

    /// <summary>Có ô nhập đổi giá trị.</summary>
    public event Action? Edited;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(KiotKeyMask), nameof(ProxyInUse), nameof(StaticIgnoredNote))]
    private string _kiotKey = "";

    /// <summary>(GĐ5d) Proxy tĩnh chung — chỉ dùng khi KHÔNG có key Kiot. Ô giữ nguyên chữ đang gõ (không trim lúc gõ, kẻo nuốt dấu cách
    /// giữa host và cổng); trim hai đầu (cả xuống dòng khi dán từ Excel) lúc đọc / lưu.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StaticProxyPreview), nameof(ProxyInUse), nameof(StaticIgnoredNote))]
    private string _staticProxy = "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsRandomRegion))]
    private string _region = KiotRegions.Random;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(StaticProxyPreview), nameof(ProxyInUse))]
    private ProxyScheme _type = ProxyScheme.Http;

    /// <summary>Ảnh bảng size dùng chung (cột O của mọi SP, plan "Chốt thiết kế" mục 4).</summary>
    [ObservableProperty] private string _sizeChartImagePath = "";

    /// <summary>(GĐ8) Thư mục ghi file xuất.</summary>
    [ObservableProperty] private string _exportDirectory = "";

    /// <summary>(GĐ9) "Ảnh chưa có trên Hub": true = bỏ SP, false = tải từ CDN (mặc định). Lưu trong cấu hình.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DownloadMissingFromCdn))]
    private bool _skipMissingHubImages;

    /// <summary>Ngược của <see cref="SkipMissingHubImages"/> — cho nút chọn "Tải từ CDN".</summary>
    public bool DownloadMissingFromCdn
    {
        get => !SkipMissingHubImages;
        set => SkipMissingHubImages = !value;
    }

    [ObservableProperty] private string _pauseFilesMin = "";
    [ObservableProperty] private string _pauseFilesMax = "";
    [ObservableProperty] private string _pauseShopsMin = "";
    [ObservableProperty] private string _pauseShopsMax = "";
    [ObservableProperty] private string _activeFromHour = "";
    [ObservableProperty] private string _activeToHour = "";

    /// <summary>Key chung đã che (<c>…abcd</c>) — thứ duy nhất của key được hiện ra màn.</summary>
    public string KiotKeyMask => SecretText.Mask(KiotKey);

    public bool IsRandomRegion => KiotRegions.IsRandom(Region);

    /// <summary>Proxy tĩnh chung đọc được thành gì (KHÔNG in user/mật khẩu), hoặc lỗi. Có user/pass thì nhắc kiểm lại: dòng 4 cột kiểu
    /// "IP cổng VN 2026-12-01" (chép thừa cột) cũng đọc thành user/pass.</summary>
    public string StaticProxyPreview =>
        string.IsNullOrWhiteSpace(StaticProxy) ? ""
        : StaticProxyParser.TryParse(StaticProxy, Type, out var lease, out var err)
            ? lease!.HasCredentials
                ? $"Đọc được: {lease.ServerArg} (có user/pass — kiểm lại nếu bạn không có user/pass)"
                : $"Đọc được: {lease.ServerArg}"
        : $"Sai: {err}";

    /// <summary>Proxy chung đang dùng — cùng thứ tự với <see cref="SellerProxySource.Resolve"/>: key Kiot trước, rồi proxy tĩnh.</summary>
    public string ProxyInUse =>
        !string.IsNullOrWhiteSpace(KiotKey) ? "Đang dùng: Kiot"
        : string.IsNullOrWhiteSpace(StaticProxy) ? "Chưa có proxy chung"
        : StaticProxyParser.TryParse(StaticProxy, Type, out _, out _) ? "Đang dùng: proxy tĩnh"
        : "Chưa có proxy chung (proxy tĩnh đang sai)";

    /// <summary>Có cả hai → proxy tĩnh bị bỏ qua; không thì null (ẩn dòng).</summary>
    public string? StaticIgnoredNote =>
        !string.IsNullOrWhiteSpace(KiotKey) && !string.IsNullOrWhiteSpace(StaticProxy)
            ? "Proxy tĩnh đang bị bỏ qua vì đã có key Kiot." : null;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is { } n && !Derived.Contains(n)) Edited?.Invoke();
    }
}
