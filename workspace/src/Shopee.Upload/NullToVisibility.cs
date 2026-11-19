using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Shopee.Upload;

/// <summary>Ẩn/hiện theo giá trị null (khung sửa tài khoản ↔ dòng "chưa có tài khoản"). <see cref="WhenNotEmpty"/>: chuỗi rỗng cũng
/// tính là null (ô kết quả xuất).</summary>
public sealed class NullToVisibility(bool visibleWhenNull, bool emptyIsNull = false) : IValueConverter
{
    public static NullToVisibility WhenNull { get; } = new(true);

    public static NullToVisibility WhenNotNull { get; } = new(false);

    public static NullToVisibility WhenNotEmpty { get; } = new(false, emptyIsNull: true);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is null || (emptyIsNull && value is string { Length: 0 })) == visibleWhenNull ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
