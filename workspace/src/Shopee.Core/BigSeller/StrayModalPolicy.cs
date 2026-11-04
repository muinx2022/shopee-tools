namespace Shopee.Core.BigSeller;

/// <summary>
/// Đóng hay giữ một hộp đang hiện. Chỉ nhận cờ cấu trúc do caller đọc từ DOM —
/// không nhận nội dung quảng cáo, không có nhánh theo tên chiến dịch.
/// Giữ khi có nút Import to Stores, checkbox shop, nút Finish / Hoàn thành,
/// hoặc caller đánh dấu listing limit (BigSellerSaveSuccessHelper.IsListingLimitFailure).
/// </summary>
public static class StrayModalPolicy
{
    public static bool ShouldCloseStrayModal(
        bool hasImportToStoresButton,
        bool hasShopCheckbox,
        bool hasFinishButton,
        bool isListingLimit)
        => !(hasImportToStoresButton || hasShopCheckbox || hasFinishButton || isListingLimit);
}
