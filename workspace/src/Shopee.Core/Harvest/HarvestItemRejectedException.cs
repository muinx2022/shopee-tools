namespace Shopee.Core.Harvest;

/// <summary>
/// Dữ liệu CỦA MỘT SP không xử lý được (JSON <c>get_pc</c> sâu quá giới hạn, khoá trùng, thiếu <c>data.item</c>…) — lỗi
/// TẤT ĐỊNH của dòng đó, làm lại y nguyên vẫn hỏng. Sink ném loại này thay vì lỗi hạ tầng: engine ghi LỖI DÒNG rồi sang
/// dòng kế, KHÔNG dừng job (dừng thì "Tiếp tục" lại kẹt đúng dòng đó mãi). Mọi exception khác của sink vẫn là lỗi hạ
/// tầng (đĩa, outbox) → dừng job.
/// </summary>
public sealed class HarvestItemRejectedException : Exception
{
    public HarvestItemRejectedException(string message, Exception? inner = null) : base(message, inner) { }
}
