using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shopee.Toolkit.MsLogin;

namespace Shopee.Core.MassUpload.Seller.Login;

/// <summary>
/// Helper dò phần tử trên trang Playwright theo <b>hiển thị</b> (<c>getClientRects</c>) + text. CHÉP từ app check đơn
/// (<c>suite/orders/XuLyDonShopee.Core/Services/LoginPageProbe.cs</c>), chỉ lấy các hàm luồng đăng nhập seller + Hotmail cần; bỏ
/// <c>ReadAlertTextAsync</c> (lỗi đăng nhập đọc qua <see cref="PlaywrightSellerLoginPage.SnapshotAsync"/>) và bản quét mọi frame
/// theo regex (chỉ dùng cho đọc mail — app này không đọc mail). Mọi hàm nuốt lỗi từng selector, chỉ ném khi bị HỦY.
/// </summary>
internal static class LoginPageProbe
{
    /// <summary>True nếu có ÍT NHẤT một phần tử khớp một trong <paramref name="selectors"/> đang HIỂN THỊ (getClientRects có
    /// kích thước &gt; 0). Một lượt quét, không poll.</summary>
    internal static async Task<bool> IsAnyVisibleByClientRectsAsync(IPage page, string[] selectors, CancellationToken ct)
    {
        foreach (var sel in selectors)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var visible = await page.EvaluateAsync<bool>(
                    @"(sel) => { for (const el of document.querySelectorAll(sel)) { const rs = el.getClientRects();"
                    + " for (const r of rs) { if (r.width > 0 && r.height > 0) return true; } } return false; }",
                    sel).ConfigureAwait(false);
                if (visible) return true;
            }
            catch { /* selector không dùng được trên trang này — thử selector kế */ }
        }
        return false;
    }

    /// <summary>True nếu <paramref name="el"/> đang HIỂN THỊ (getClientRects có kích thước &gt; 0).</summary>
    internal static async Task<bool> IsElementVisibleByClientRectsAsync(IElementHandle el)
    {
        try
        {
            return await el.EvaluateAsync<bool>(
                "(node) => { const rs = node.getClientRects(); for (const r of rs) { if (r.width > 0 && r.height > 0) return true; } return false; }")
                .ConfigureAwait(false);
        }
        catch { return false; }
    }

    /// <summary>True nếu <paramref name="selector"/> có phần tử ĐANG HIỂN THỊ (dùng cho error box Microsoft).</summary>
    internal static async Task<bool> IsSelectorVisibleAsync(IPage page, string selector)
    {
        try
        {
            var el = await page.QuerySelectorAsync(selector).ConfigureAwait(false);
            return el is not null && await IsElementVisibleByClientRectsAsync(el).ConfigureAwait(false);
        }
        catch { return false; }
    }

    /// <summary>Phần tử ĐẦU TIÊN đang HIỂN THỊ khớp một trong <paramref name="selectors"/>, poll tới hết <paramref name="timeoutMs"/>.</summary>
    internal static async Task<IElementHandle?> FindFirstVisibleByRectsAsync(
        IPage page, string[] selectors, int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        do
        {
            ct.ThrowIfCancellationRequested();
            foreach (var sel in selectors)
            {
                try
                {
                    var el = await page.QuerySelectorAsync(sel).ConfigureAwait(false);
                    if (el is not null && await IsElementVisibleByClientRectsAsync(el).ConfigureAwait(false)) return el;
                }
                catch { /* selector không dùng được — thử selector kế */ }
            }
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
        while (DateTime.UtcNow < deadline);

        return null;
    }

    /// <summary>Phần tử ĐẦU TIÊN đang HIỂN THỊ khớp selector VÀ có innerText khớp <paramref name="textRegex"/>, poll tới hết
    /// <paramref name="timeoutMs"/>. Duyệt theo thứ tự selector. Chỉ quét frame chính.</summary>
    internal static async Task<IElementHandle?> FindVisibleByTextAsync(
        IPage page, string[] selectors, Regex textRegex, CancellationToken ct, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        do
        {
            ct.ThrowIfCancellationRequested();
            foreach (var sel in selectors)
            {
                IReadOnlyList<IElementHandle> els;
                try { els = await page.QuerySelectorAllAsync(sel).ConfigureAwait(false); }
                catch { continue; }

                foreach (var el in els)
                {
                    try
                    {
                        if (!await IsElementVisibleByClientRectsAsync(el).ConfigureAwait(false)) continue;
                        var txt = await el.InnerTextAsync().ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(txt) && textRegex.IsMatch(txt)) return el;
                    }
                    catch { /* detached / lỗi đọc — bỏ qua phần tử này */ }
                }
            }
            await Task.Delay(300, ct).ConfigureAwait(false);
        }
        while (DateTime.UtcNow < deadline);

        return null;
    }

    /// <summary>Như <see cref="FindVisibleByTextAsync"/> nhưng quét MỌI frame và so khớp KHÔNG DẤU
    /// (<see cref="MsLoginSelectors.NormalizeForMatch"/>) với <paramref name="normalizedNeedles"/> — trị lỗi NFC/NFD của text tiếng
    /// Việt trên form Microsoft.</summary>
    internal static async Task<IElementHandle?> FindByNormalizedTextInFramesAsync(
        IPage page, string[] selectors, string[] normalizedNeedles, CancellationToken ct, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        do
        {
            ct.ThrowIfCancellationRequested();
            foreach (var frame in page.Frames)
            {
                foreach (var sel in selectors)
                {
                    IReadOnlyList<IElementHandle> els;
                    try { els = await frame.QuerySelectorAllAsync(sel).ConfigureAwait(false); }
                    catch { continue; }

                    foreach (var el in els)
                    {
                        try
                        {
                            if (!await IsElementVisibleByClientRectsAsync(el).ConfigureAwait(false)) continue;
                            var txt = MsLoginSelectors.NormalizeForMatch(await el.InnerTextAsync().ConfigureAwait(false));
                            if (txt.Length > 0 && Array.Exists(normalizedNeedles, n => txt.Contains(n, StringComparison.Ordinal))) return el;
                        }
                        catch { /* detached — bỏ qua */ }
                    }
                }
            }
            await Task.Delay(300, ct).ConfigureAwait(false);
        }
        while (DateTime.UtcNow < deadline);

        return null;
    }

    /// <summary>True nếu tại điểm (x,y) phần tử nhận sự kiện chính là el / con / tổ tiên của el.</summary>
    internal static async Task<bool> IsPointOnElementAsync(IElementHandle el, double x, double y)
    {
        try
        {
            return await el.EvaluateAsync<bool>(
                "(node, pt) => { const hit = document.elementFromPoint(pt.x, pt.y);" +
                " return !!hit && (node === hit || node.contains(hit) || hit.contains(node)); }",
                new { x, y }).ConfigureAwait(false);
        }
        catch { return false; }
    }

    /// <summary>Phần tử có bounding box không (đang hiển thị); handle DETACHED → false.</summary>
    internal static async Task<bool> HasBoundingBoxAsync(IElementHandle el)
    {
        try { return await el.BoundingBoxAsync().ConfigureAwait(false) is not null; }
        catch { return false; }
    }
}
