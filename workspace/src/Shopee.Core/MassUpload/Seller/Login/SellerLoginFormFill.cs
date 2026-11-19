namespace Shopee.Core.MassUpload.Seller.Login;

/// <summary>Form đăng nhập Seller Centre, nhìn từ phía "gõ an toàn" (<see cref="SellerLoginFormFill"/>). Bản thật nằm trong
/// <see cref="PlaywrightSellerLoginPage"/>; test dùng bản giả mô phỏng trang vẽ lại node giữa chừng.</summary>
internal interface ISellerLoginForm
{
    /// <summary>Tab đang ở đúng host Seller Centre (<see cref="SellerLoginPageClassifier.SellerHost"/>). Host lạ thì KHÔNG gõ.</summary>
    bool OnSellerHost { get; }

    /// <summary>Tìm LẠI ô tài khoản (<c>input[name=loginKey]</c> đang hiện) — gọi ngay trước khi gõ ô đó.</summary>
    Task<IHumanField?> FindLoginKeyAsync(CancellationToken ct);

    /// <summary>Tìm LẠI ô mật khẩu — gọi ngay trước khi gõ ô đó (sau khi gõ tài khoản, trang có thể đã thay node ô mật khẩu).</summary>
    Task<IHumanField?> FindPasswordAsync(CancellationToken ct);

    /// <summary>Ô tài khoản còn đúng <paramref name="login"/> không — so TRONG trang, chỉ trả true/false (không đọc giá trị về).</summary>
    Task<bool> LoginKeyHoldsAsync(string login);

    /// <summary>Xoá trắng ô tài khoản (khi nghi mật khẩu đã lọt vào đó). Best-effort.</summary>
    Task ClearLoginKeyAsync();
}

/// <summary>
/// Gõ tài khoản + mật khẩu Shopee (GĐ5b vòng 2 — sửa lỗi phản biện tái hiện trên Brave thật: trang vẽ lại node ô mật khẩu sau khi
/// gõ tài khoản, mật khẩu rơi vào ô tài khoản và hiện chữ trên màn, mà hàm cũ vẫn báo đạt):
/// <list type="number">
/// <item>Chỉ gõ khi tab ở host Seller Centre (kiểm trước MỖI ô).</item>
/// <item>Mỗi ô tìm lại NGAY trước khi gõ, gõ bằng <see cref="HumanFieldFill"/> (bấm phải trúng, xoá lỗi thì dừng, focus kiểm từng
/// ký tự). Ô tài khoản hỏng → false, chưa gõ ký tự mật khẩu nào.</item>
/// <item>Ô mật khẩu hỏng giữa chừng (trang vẽ lại ô muộn — GĐ5b vòng 3) → tìm lại, xoá, gõ lại ĐÚNG MỘT lần; vẫn hỏng → false.</item>
/// <item>Gõ xong (hoặc dừng giữa chừng) ô mật khẩu: ô tài khoản phải còn ĐÚNG tài khoản; sai → xoá trắng ô tài khoản, false.</item>
/// </list>
/// false = bên gọi báo <see cref="SellerLoginOutcome.FormNotFound"/>, KHÔNG bấm Đăng nhập.
/// </summary>
internal static class SellerLoginFormFill
{
    internal static async Task<bool> FillAsync(ISellerLoginForm form, string login, string password, Random rng,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct)
    {
        if (!form.OnSellerHost) return false;
        if (!await HumanFieldFill.FillAsync(await form.FindLoginKeyAsync(ct).ConfigureAwait(false), login, rng, delay, ct).ConfigureAwait(false))
            return false;

        await delay(TimeSpan.FromMilliseconds(rng.Next(300, 900)), ct).ConfigureAwait(false);
        if (!form.OnSellerHost) return false;
        var ok = await HumanFieldFill.FillAsync(await form.FindPasswordAsync(ct).ConfigureAwait(false), password, rng, delay, ct)
            .ConfigureAwait(false);
        if (!ok && form.OnSellerHost)
        {
            // Trang vẽ lại ô mật khẩu MUỘN (vd 1,5 s sau phím cuối của ô tài khoản) → mất focus giữa chừng. Tìm lại ô, xoá, gõ lại
            // ĐÚNG MỘT lần (vẫn kiểm focus từng ký tự); lần này hỏng nữa thì dừng.
            await delay(TimeSpan.FromMilliseconds(rng.Next(400, 900)), ct).ConfigureAwait(false);
            ok = await HumanFieldFill.FillAsync(await form.FindPasswordAsync(ct).ConfigureAwait(false), password, rng, delay, ct)
                .ConfigureAwait(false);
        }

        if (!await form.LoginKeyHoldsAsync(login).ConfigureAwait(false))
        {
            await form.ClearLoginKeyAsync().ConfigureAwait(false);
            return false;
        }
        return ok;
    }
}
