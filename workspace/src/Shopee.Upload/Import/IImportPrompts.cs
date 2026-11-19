namespace Shopee.Upload.Import;

/// <summary>Các hộp thoại của hai nút nhập (GĐ5c). App thật: <see cref="WpfImportPrompts"/>; test tiêm bản giả.</summary>
public interface IImportPrompts
{
    /// <summary>Bảng xem trước "Nhập shop từ BigSeller". true = user bấm Thêm.</summary>
    bool ConfirmShops(ShopImportPlan plan);

    /// <summary>Chọn file .xlsx mật khẩu. null = huỷ.</summary>
    string? PickPasswordFile();

    /// <summary>Hỏi MỘT lần cho cả lượt: có <paramref name="accounts"/> tài khoản / <paramref name="fields"/> ô đang có giá trị khác.</summary>
    OverwriteChoice AskOverwrite(int accounts, int fields);

    /// <summary>Hộp thoại tóm tắt kết quả (chuỗi đã không có mật khẩu).</summary>
    void ShowSummary(string title, string text, bool isError = false);
}
