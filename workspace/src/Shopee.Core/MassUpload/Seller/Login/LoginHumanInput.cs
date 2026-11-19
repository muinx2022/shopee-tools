using System.Globalization;
using Microsoft.Playwright;

namespace Shopee.Core.MassUpload.Seller.Login;

/// <summary>
/// Thao tác chuột/bàn phím <b>kiểu người</b> trên trang Playwright của luồng đăng nhập: di chuột theo đường cong
/// (<see cref="HumanMouse"/>), gõ từng ký tự có delay (<see cref="HumanTyping"/>), bấm có hit-test.
/// CHÉP từ app check đơn (<c>suite/orders/XuLyDonShopee.Core/Services/LoginHumanInput.cs</c>), giữ nguyên các con số chống bot.
/// <para><b>Khác bản check đơn (GĐ5b vòng 2, phản biện tái hiện trên Brave thật: trang vẽ lại node ô mật khẩu sau khi gõ tài
/// khoản → mật khẩu rơi vào ô tài khoản, hiện chữ trên màn):</b></para>
/// <list type="bullet">
/// <item>BỎ <c>HumanFillAsync</c> (gõ vào handle lấy từ trước, rơi xuống Ctrl+A/Delete rồi gõ mù khi <c>FillAsync("")</c> lỗi) và
/// <c>HumanMoveAndClickAsync</c> (không có khung thì bấm vào chỗ chuột đang đứng). Gõ ô nhập đi qua <see cref="HumanFieldFill"/>
/// + <see cref="PlaywrightHumanField"/>: lấy lại ô ngay trước khi gõ, bấm phải trúng, kiểm focus trước mỗi ký tự.</item>
/// <item>Mọi cú bấm đều có hit-test (<see cref="TryHumanClickVisibleAsync"/>): không trúng thì KHÔNG bấm.</item>
/// </list>
/// <para>KHÔNG log gì ở đây: ô nhập nhận cả mật khẩu.</para>
/// </summary>
internal static class LoginHumanInput
{
    /// <summary>Di chuột theo <b>đường cong</b> tới tâm phần tử (+jitter nhỏ), <c>Mouse.MoveAsync</c> từng điểm. Chỉ đưa chuột tới
    /// đích — KHÔNG click. Box null → kéo phần tử vào tầm nhìn, giữ vị trí chuột, <c>HasBox=false</c>.</summary>
    internal static async Task<(double X, double Y, bool HasBox)> HumanMoveToAsync(
        IPage page, IElementHandle el, double mx, double my, Random rng, CancellationToken ct)
    {
        ElementHandleBoundingBoxResult? box;
        try { box = await el.BoundingBoxAsync().ConfigureAwait(false); }
        catch { box = null; }

        double tx, ty;
        bool hasBox;
        if (box is not null)
        {
            tx = box.X + box.Width / 2.0 + (rng.NextDouble() - 0.5) * Math.Min(box.Width * 0.3, 20);
            ty = box.Y + box.Height / 2.0 + (rng.NextDouble() - 0.5) * Math.Min(box.Height * 0.3, 8);
            hasBox = true;
        }
        else
        {
            try { await el.ScrollIntoViewIfNeededAsync().ConfigureAwait(false); } catch { /* bỏ qua */ }
            tx = mx;
            ty = my;
            hasBox = false;
        }

        // Số điểm theo khoảng cách (đường dài → nhiều điểm), giới hạn [12, 60] cho mượt.
        var dist = Math.Sqrt((tx - mx) * (tx - mx) + (ty - my) * (ty - my));
        var steps = Math.Clamp((int)(dist / 8) + 10, 12, 60);

        foreach (var (px, py) in HumanMouse.GeneratePath(mx, my, tx, ty, steps, rng))
        {
            ct.ThrowIfCancellationRequested();
            await page.Mouse.MoveAsync((float)px, (float)py).ConfigureAwait(false);
            await Task.Delay(rng.Next(5, 26), ct).ConfigureAwait(false); // 5–25ms giữa các điểm
        }

        return (tx, ty, hasBox);
    }

    /// <summary>Click kiểu người CÓ HIT-TEST: chỉ nhả click khi <c>elementFromPoint</c> tại điểm click đúng là phần tử đích (poll
    /// ~2s, chuột đứng yên). Không có box / hit-test hỏng suốt ~2s → KHÔNG click, <c>Clicked=false</c>.</summary>
    internal static async Task<(double X, double Y, bool Clicked)> HumanMoveAndClickVerifiedAsync(
        IPage page, IElementHandle el, double mx, double my, Random rng, CancellationToken ct)
    {
        (double tx, double ty, bool hasBox) = await HumanMoveToAsync(page, el, mx, my, rng, ct).ConfigureAwait(false);

        if (!hasBox)
        {
            try { await el.ScrollIntoViewIfNeededAsync().ConfigureAwait(false); } catch { /* bỏ qua */ }
            (tx, ty, hasBox) = await HumanMoveToAsync(page, el, mx, my, rng, ct).ConfigureAwait(false);
            if (!hasBox) return (mx, my, false);
        }

        var deadline = DateTime.UtcNow.AddMilliseconds(2000);
        do
        {
            ct.ThrowIfCancellationRequested();
            if (await LoginPageProbe.IsPointOnElementAsync(el, tx, ty).ConfigureAwait(false))
            {
                await page.Mouse.DownAsync().ConfigureAwait(false);
                await Task.Delay(rng.Next(40, 121), ct).ConfigureAwait(false);
                await page.Mouse.UpAsync().ConfigureAwait(false);
                return (tx, ty, true);
            }

            await Task.Delay(rng.Next(150, 301), ct).ConfigureAwait(false);
        }
        while (DateTime.UtcNow < deadline);

        return (tx, ty, false);
    }

    /// <summary>Click kiểu người CÓ HIT-TEST, chỉ khi phần tử đang hiển thị (có bounding box sau khi kéo vào tầm nhìn).</summary>
    internal static async Task<(double X, double Y, bool Clicked)> TryHumanClickVisibleAsync(
        IPage page, IElementHandle el, double mx, double my, Random rng, CancellationToken ct)
    {
        try { await el.ScrollIntoViewIfNeededAsync().ConfigureAwait(false); } catch { /* bỏ qua */ }

        if (!await LoginPageProbe.HasBoundingBoxAsync(el).ConfigureAwait(false))
        {
            try { await el.ScrollIntoViewIfNeededAsync().ConfigureAwait(false); } catch { /* bỏ qua */ }
            if (!await LoginPageProbe.HasBoundingBoxAsync(el).ConfigureAwait(false)) return (mx, my, false);
        }

        return await HumanMoveAndClickVerifiedAsync(page, el, mx, my, rng, ct).ConfigureAwait(false);
    }
}

/// <summary>Vị trí chuột hiện tại của một tab — dùng chung giữa các thao tác để đường chuột nối tiếp nhau.</summary>
internal sealed class MouseState(double x, double y)
{
    public double X { get; set; } = x;
    public double Y { get; set; } = y;

    public static MouseState CenterOf(IPage page)
    {
        var vp = page.ViewportSize;
        return new MouseState(vp is not null ? vp.Width / 2.0 : 640, vp is not null ? vp.Height / 2.0 : 360);
    }
}

/// <summary>Một ô nhập trên trang, nhìn từ phía "gõ an toàn" (<see cref="HumanFieldFill"/>). Bản thật
/// <see cref="PlaywrightHumanField"/>; test dùng bản giả mô phỏng node bị thay / focus nhảy.</summary>
internal interface IHumanField
{
    /// <summary>Di chuột tới ô rồi bấm CÓ hit-test. false = ô không có khung, hoặc điểm bấm thuộc phần tử khác → KHÔNG bấm.</summary>
    Task<bool> ClickAsync(CancellationToken ct);

    /// <summary>Node của ô còn trong trang (<c>isConnected</c>) VÀ đang giữ focus (<c>=== document.activeElement</c>). Lỗi → false.</summary>
    Task<bool> IsFocusedAsync();

    /// <summary>Xoá trắng ô (Playwright <c>FillAsync("")</c>). false = lỗi → bên gọi DỪNG, không gõ mù.</summary>
    Task<bool> ClearAsync();

    /// <summary>Gõ một text element (một ký tự người dùng thấy; có thể gồm cặp surrogate / dấu tổ hợp) vào chỗ đang có focus.</summary>
    Task TypeAsync(string textElement);
}

/// <summary>
/// Gõ an toàn một ô (hàm thuần trên <see cref="IHumanField"/>, test được không cần trình duyệt). Luật (GĐ5b vòng 2):
/// <list type="number">
/// <item>Ô do bên gọi tìm LẠI ngay trước khi gõ (không dùng handle cũ). null → false.</item>
/// <item>Bấm phải trúng (<see cref="IHumanField.ClickAsync"/>), sau đó ô phải còn và đang có focus.</item>
/// <item>Xoá trắng bằng <see cref="IHumanField.ClearAsync"/>; lỗi → false (KHÔNG Ctrl+A/Delete rồi gõ mù).</item>
/// <item>Trước MỖI ký tự kiểm lại "ô còn + đang focus"; sai → false ngay (ký tự đó không được gõ).</item>
/// <item>Gõ theo text element (<see cref="StringInfo"/>), không theo <c>char</c> — không tách đôi cặp surrogate.</item>
/// </list>
/// </summary>
internal static class HumanFieldFill
{
    /// <summary>Tách chuỗi thành các text element (ký tự người dùng thấy).</summary>
    internal static IEnumerable<string> TextElements(string text)
    {
        var e = StringInfo.GetTextElementEnumerator(text);
        while (e.MoveNext()) yield return (string)e.Current;
    }

    /// <param name="delay">Chờ (thật: <c>Task.Delay</c>; test: đồng hồ giả).</param>
    internal static async Task<bool> FillAsync(IHumanField? field, string text, Random rng,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct)
    {
        if (field is null) return false;
        if (!await field.ClickAsync(ct).ConfigureAwait(false)) return false;
        if (!await field.IsFocusedAsync().ConfigureAwait(false)) return false;
        if (!await field.ClearAsync().ConfigureAwait(false)) return false;
        await delay(TimeSpan.FromMilliseconds(rng.Next(60, 160)), ct).ConfigureAwait(false);

        foreach (var ch in TextElements(text))
        {
            ct.ThrowIfCancellationRequested();
            if (!await field.IsFocusedAsync().ConfigureAwait(false)) return false;
            // Gõ TỪNG ký tự (KHÔNG fill/dán) + delay kiểu người.
            await field.TypeAsync(ch).ConfigureAwait(false);
            await delay(TimeSpan.FromMilliseconds(HumanTyping.NextCharDelayMs(rng)), ct).ConfigureAwait(false);
        }
        return await field.IsFocusedAsync().ConfigureAwait(false);
    }
}

/// <summary>Bản thật của <see cref="IHumanField"/> trên một handle VỪA tìm được. Lỗi Playwright (node bị thay giữa chừng) → false,
/// không ném (trừ huỷ).</summary>
internal sealed class PlaywrightHumanField(IPage page, IElementHandle element, MouseState mouse, Random rng) : IHumanField
{
    private const string FocusedJs = "(n) => n.isConnected && n === document.activeElement";

    public async Task<bool> ClickAsync(CancellationToken ct)
    {
        var (x, y, clicked) = await LoginHumanInput.TryHumanClickVisibleAsync(page, element, mouse.X, mouse.Y, rng, ct).ConfigureAwait(false);
        mouse.X = x;
        mouse.Y = y;
        return clicked;
    }

    public async Task<bool> IsFocusedAsync()
    {
        try { return await element.EvaluateAsync<bool>(FocusedJs).ConfigureAwait(false); }
        catch (PlaywrightException) { return false; }
    }

    public async Task<bool> ClearAsync()
    {
        try
        {
            await element.FillAsync("", new ElementHandleFillOptions { Timeout = 3000 }).ConfigureAwait(false);
            return true;
        }
        catch (PlaywrightException) { return false; }
        catch (TimeoutException) { return false; }
    }

    public Task TypeAsync(string textElement) => page.Keyboard.TypeAsync(textElement);
}
