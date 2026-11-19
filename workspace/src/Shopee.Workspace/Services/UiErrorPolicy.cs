namespace Shopee.Workspace.Services;

/// <summary>Nhánh xử lý một lỗi UI. Hàm chọn nhánh không mở cửa sổ và không ghi log.</summary>
internal enum UiErrorAction
{
    /// <summary>Chỉ log. Không popup, không đóng app.</summary>
    Ignore,

    /// <summary>Log và một popup.</summary>
    Notify,

    /// <summary>Luồng vẽ đã chết. Log một lần rồi đóng app, không popup.</summary>
    Shutdown,
}

/// <summary>
/// Luật thuần cho lỗi UI. HRESULT <c>0x88980406</c> (UCEERR_RENDERTHREADFAILURE) thắng mọi nhánh khác,
/// kể cả khi đang ở trong handler: lần đầu phải đóng app. Cờ handler chỉ chặn popup sinh ra từ chính popup.
/// </summary>
internal static class UiErrorPolicy
{
    /// <summary>MIL/DWM: kênh vẽ không đồng bộ được vì luồng render đã chết.</summary>
    internal const int RenderThreadFailureHResult = unchecked((int)0x88980406);

    internal static UiErrorAction Decide(Exception? ex, bool inHandler)
    {
        if (HasRenderThreadFailure(ex)) return UiErrorAction.Shutdown;
        if (inHandler) return UiErrorAction.Ignore;
        if (IsBenignTeardown(ex)) return UiErrorAction.Ignore;
        return UiErrorAction.Notify;
    }

    private static bool HasRenderThreadFailure(Exception? ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur.HResult == RenderThreadFailureHResult) return true;
        }
        return false;
    }

    private static bool IsBenignTeardown(Exception? ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is ObjectDisposedException or OperationCanceledException)
                return true;
            if ((cur.Message ?? "").Contains("CancellationTokenSource has been disposed", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
