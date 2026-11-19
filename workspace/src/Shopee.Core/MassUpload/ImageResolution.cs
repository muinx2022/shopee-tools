namespace Shopee.Core.MassUpload;

/// <summary>Tình trạng một ảnh khi dựng file: sẵn sàng (có URL), đang chờ (bước đệm MD5 chưa xong — SP để lượt sau), hay
/// lỗi vĩnh viễn (ảnh gốc hỏng / không còn — bỏ ảnh đó).</summary>
public enum ImageReadiness { Ready, Pending, Failed }

/// <summary>Kết quả resolver ảnh cho <see cref="MassUploadMapper"/>: <see cref="Url"/> khi <see cref="ImageReadiness.Ready"/>,
/// <see cref="Reason"/> khi <see cref="ImageReadiness.Failed"/>.</summary>
public readonly record struct ImageResolution(ImageReadiness State, string? Url = null, string? Reason = null)
{
    public static ImageResolution Ready(string url) => new(ImageReadiness.Ready, url);
    public static ImageResolution Pending { get; } = new(ImageReadiness.Pending);
    public static ImageResolution Failed(string? reason) => new(ImageReadiness.Failed, null, reason);

    /// <summary>Resolver kiểu cũ (URL hoặc null): null / trống = <see cref="ImageReadiness.Pending"/>.</summary>
    public static ImageResolution FromUrl(string? url) => string.IsNullOrWhiteSpace(url) ? Pending : Ready(url);
}
