namespace OpenMultiBraveLauncherV3;

/// <summary>
/// Mở Brave CHỈ để mở trang sản phẩm Shopee và đọc JSON <c>shopee-api-data-*</c> (extension "Product Copy" của BigSeller
/// tự ghi khi trang tải) — dùng cho "Cào lại SP lỗi" của Scrape Multi. KHÔNG nạp extension runner, KHÔNG chạy vòng
/// runner, KHÔNG bật đồng hồ giám sát/đồng bộ tiến độ → không đụng tiến độ scrape (đồng bộ tiến độ trong phiên đã tự
/// bỏ qua khi <c>_extensionAutomationEnabled = false</c>).
/// </summary>
internal sealed partial class BraveInstanceSession
{
    /// <summary>Dựng profile + phóng Brave (qua đúng <see cref="BringUpProfileAsync"/> như scrape thường) rồi chờ CDP.
    /// Không có đồng hồ giám sát nên nếu Brave chết giữa chừng thì caller thấy lỗi CDP và dừng.</summary>
    public async Task StartForApiFetchAsync(string braveExe, string sourceUserData, CancellationToken cancellationToken)
    {
        if (_busy || _config is null) return;
        _busy = true;
        _extensionAutomationEnabled = false;
        _braveExe = braveExe.Trim();
        _sourceUserData = sourceUserData.Trim();
        SetStatus("Đang khởi động…");
        try
        {
            if (!File.Exists(_braveExe))
                throw new FileNotFoundException("Không tìm thấy brave.exe.", _braveExe);

            var (proxyServer, proxyData) = await _proxy.ResolveForLaunchAsync().ConfigureAwait(false);
            await BringUpProfileAsync(
                proxyServer,
                proxyData is not null ? KiotProxyRotator.BuildFingerprint(proxyData) : proxyServer ?? "",
                ensureProfile: true).ConfigureAwait(false);

            // BringUpProfileAsync chỉ tự chờ CDP khi bật extension automation → chờ ở đây.
            if (!await WaitForCdpReadyAsync(attempts: 40, delayMs: 500, cancellationToken).ConfigureAwait(false))
                throw new InvalidOperationException("Brave không mở được cổng CDP.");
        }
        catch (Exception ex)
        {
            _running = false;
            SetStatus($"Lỗi: {ex.Message}");
            throw;
        }
        finally
        {
            _busy = false;
            RaiseStatusChanged();
        }
    }

    public int CdpPort => _cdpPort;
}
