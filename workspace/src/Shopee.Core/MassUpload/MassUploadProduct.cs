using System.Globalization;

namespace Shopee.Core.MassUpload;

/// <summary>Giá trị một ô: chuỗi, hoặc số nguyên (ghi bằng <c>&lt;v&gt;</c> nếu cột có style số, không thì ghi chuỗi).</summary>
public readonly record struct MassUploadCell(string Text, bool IsNumber)
{
    public static MassUploadCell Of(string text) => new(text, false);

    public static MassUploadCell Of(long number) => new(number.ToString(CultureInfo.InvariantCulture), true);

    public override string ToString() => Text;
}

/// <summary>Một cặp (thuộc tính, giá trị EN) chưa dịch / chưa khớp được — để bổ sung <see cref="AttributeDictionary"/>.</summary>
public sealed record MissingAttributeValue(long AttrId, string AttrName, string Value);

/// <summary>Kết quả map một SP: <see cref="MassUploadProduct"/> (đăng được) hoặc <see cref="MassUploadSkip"/>.</summary>
public abstract record MassUploadMapResult(long ItemId);

/// <summary>SP không đăng được ở lượt này, kèm lý do (ghi sổ).</summary>
public sealed record MassUploadSkip(long ItemId, string Reason) : MassUploadMapResult(ItemId);

/// <summary>
/// SP đã map: mỗi phần tử <see cref="Rows"/> là một dòng file (một model), khoá = mã cột dòng 1 (vd <c>ps_price</c>).
/// <see cref="NeedsFix"/> = vẫn đăng (Shopee để Bản nháp) nhưng thiếu thuộc tính bắt buộc — <see cref="NeedsFixReasons"/>
/// nêu rõ, <see cref="MissingAttributeValues"/> liệt kê cặp cần bổ sung từ điển. <see cref="Notes"/> = ghi chú không phải cảnh
/// báo (vd "phân loại chuẩn: x/y giá trị đổi sang mã", GĐ8b).
/// </summary>
public sealed record MassUploadProduct(
    long ItemId,
    long TopCategoryId,
    long LeafCategoryId,
    IReadOnlyList<IReadOnlyDictionary<string, MassUploadCell>> Rows,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> NeedsFixReasons,
    IReadOnlyList<MissingAttributeValue> MissingAttributeValues,
    IReadOnlyList<string>? Notes = null) : MassUploadMapResult(ItemId)
{
    public bool NeedsFix => NeedsFixReasons.Count > 0;
}
