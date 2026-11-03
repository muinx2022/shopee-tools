using Microsoft.Playwright;

namespace UpdateProduct;

// Partial của BigSellerProductUpdateRunner: luồng Lưu (Save & Publish) + phát hiện lỗi/thành công — A2, pure move.
internal sealed partial class BigSellerProductUpdateRunner
{
    // ── [12.2] save ──
    private async Task<bool> SaveWithImageRetryAsync(IPage page, string? imagePath, int maxAttempts, string? itemId, Func<Task>? onSaved, CancellationToken ct)
    {
        // Tab đóng TRƯỚC khi kịp submit save = chưa lưu gì → KHÔNG được báo dòng oan (đó là thất bại thật).
        if (page.IsClosed) { _log("  ↳ Lưu: tab đã đóng trước khi kịp bấm Lưu → coi là CHƯA lưu (không báo dòng oan)."); return false; }
        var wrapper = page.Locator(SaveButtonWrapper);
        if (!await wrapper.IsVisibleAsync()) { _log("  ↳ Lưu: KHÔNG thấy nút 'Save & Publish' (form chưa sẵn sàng?) → bỏ lưu, SP thử lại."); return false; }
        var hasImage = !string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath);

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Attempt TRƯỚC đã submit save; tab tự đóng giữa chừng = dấu hiệu BigSeller đóng sau khi lưu OK (nó
            // hay tự đóng tab rất nhanh). Bug cũ nuốt exception 3 vòng rồi trả false oan → Hub nhận 0 dòng; nay
            // xác nhận qua context còn sống (helper) thay vì coi tab-đóng là thất bại.
            if (page.IsClosed)
                return await BigSellerSaveSuccessHelper.ConfirmSavedThenCloseAsync(page, _context!, 5000, _log, DelayAsync, onSaved, ct);
            try
            {
                if (attempt > 0)
                {
                    await DismissBlockingModalAsync(page);
                    await CloseVisibleAntModalAsync(page, 2000);
                }

                if (hasImage && !await IsImageUploadedAsync(page))
                {
                    if (!await UploadImageWithRetryAsync(page, imagePath!, 1, ct))
                    {
                        if (attempt == maxAttempts - 1)
                        {
                            // Ảnh không lên sau hết lượt → KHÔNG bấm Lưu (lưu thiếu ảnh vô nghĩa). Log rõ + check kho
                            // đầy (cùng ngữ nghĩa các điểm check khác) kẻo im lặng "▶ Lưu" rồi return false.
                            _log($"⚠ Ảnh KHÔNG upload được sau {maxAttempts} lượt → KHÔNG bấm Lưu, SP sẽ được thử lại.");
                            if (await BigSellerMaterialCenterCleaner.IsMediaInsufficientSignalAsync(page))
                            { _mediaFullDetected = true; _log("⚠ Media Center đầy (phát hiện ở bước Lưu)."); }
                            else
                                await BigSellerMaterialCenterCleaner.DumpErrorToastOnceAsync(page, _log, () =>
                                { if (_errorToastDumped) return false; _errorToastDumped = true; return true; });
                            return false;
                        }
                        await DelayAsync(2500, ct);
                        continue;
                    }
                }

                await wrapper.ScrollIntoViewIfNeededAsync();

                // "Save & Publish" (bản EN) giờ là DROPDOWN: nút chính là ant-dropdown-trigger nên bấm nút
                // CHỈ mở menu, KHÔNG lưu — phải bấm đúng <li autoid='save_and_publish_option'>. Bản cũ (VN)
                // là nút thường → bấm nút là lưu luôn. Vì hover có thể KHÔNG mở menu (trigger='click'), ta:
                // hover thử → nếu option chưa hiện mà đây là dropdown thì click nút để mở → chờ option → bấm.
                var opt = page.Locator(SaveOption);
                var btn = wrapper.Locator("button").First;

                await btn.HoverAsync();
                await DelayAsync(500, ct);
                if (!await opt.IsVisibleAsync() && await wrapper.Locator(".ant-dropdown-trigger").CountAsync() > 0)
                {
                    try { await btn.ClickAsync(); } catch { /* click mở dropdown best-effort — kết quả kiểm ngay bằng opt.IsVisibleAsync bên dưới */ }
                    try { await opt.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3000 }); } catch { /* chờ option best-effort — nhánh dưới tự fallback nút thường */ }
                }

                // Mốc "từ lúc bấm Lưu" theo đồng hồ của TRANG (cùng nguồn ts máy ghi toast): toast ghi trước mốc này là toast
                // cũ, không được dừng shop. Không lấy được → bỏ qua kiểm tra toast lượt này (null).
                var saveClickTs = await BigSellerMaterialCenterCleaner.GetPageNowAsync(page);

                if (await opt.IsVisibleAsync())
                {
                    _log("  ↳ Lưu: bấm option 'Save & Publish' (dropdown).");
                    await opt.ClickAsync(new() { Force = true });   // chọn option = lưu
                }
                else
                {
                    _log("  ↳ Lưu: option dropdown KHÔNG hiện → bấm nút thường (nếu là bản dropdown thì click này chỉ mở menu — soi dump timeout).");
                    await btn.ClickAsync();   // fallback: bản nút-thường cũ → bấm là lưu
                }

                var err = await DetectSaveErrorAsync(page, 4000);
                if (err is not null) _log($"  ↳ BigSeller báo lỗi khi lưu: {err} → xử lý & thử lại.");
                if (err == "brand") { await SelectNoBrandAsync(page, ct); await DelayAsync(2500, ct); continue; }
                if (err == "other")
                {
                    if (hasImage) await UploadImageWithRetryAsync(page, imagePath!, 1, ct);
                    await DelayAsync(2500, ct);
                    continue;
                }

                try
                {
                    var confirm = page.Locator(ConfirmPrimaryBtn);
                    await confirm.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
                    await confirm.ClickAsync();
                    var err2 = await DetectSaveErrorAsync(page, 4000);
                    if (err2 is not null) _log($"  ↳ BigSeller báo lỗi khi lưu: {err2} → xử lý & thử lại.");
                    if (err2 == "brand") { await SelectNoBrandAsync(page, ct); await DelayAsync(2500, ct); continue; }
                    if (err2 == "other") { if (hasImage) await UploadImageWithRetryAsync(page, imagePath!, 1, ct); await DelayAsync(2500, ct); continue; }
                }
                catch
                {
                    if (hasImage && (!await IsImageUploadedAsync(page) || await page.Locator(ImageUploadMenuItem).First.IsVisibleAsync()))
                    {
                        await UploadImageWithRetryAsync(page, imagePath!, 1, ct);
                        await DelayAsync(2500, ct);
                        continue;
                    }
                }

                string? publishFailReason = null;
                var productLimitToast = false;
                var saveTimedOut = false;
                // Toast "số sản phẩm vượt giới hạn": CHỈ chốt listing limit khi đủ ngưỡng SP KHÁC NHAU nhận toast (tracker đếm
                // theo item id, mọi lane chung) — 1 toast khớp nhầm ở 1 SP không được dừng cả shop. Nguồn = toast (câu chữ
                // đoán → shop sẽ ghi stopped chứ không completed). Chưa đủ ngưỡng (hoặc không có tracker) thì _listingLimitDetected
                // KHÔNG bật; vẫn return false ngay, không thử lượt Lưu tiếp → SP đi đường fail thường (fail 2 lần thì bỏ qua).
                if (await BigSellerSaveSuccessHelper.ConfirmSavedThenCloseAsync(page, _context!, 60000, _log, DelayAsync, onSaved, ct, r => publishFailReason = r,
                        toastText =>
                        {
                            productLimitToast = true;
                            var (distinct, decided) = _targetTracker?.RecordProductLimitToast(itemId) ?? (0, false);
                            if (decided)
                            {
                                _listingLimitDetected = true;
                                _log($"⛔ Shop '{_settings.ShopName}': toast BigSeller nghi vượt giới hạn số sản phẩm \"{toastText}\" — {distinct}/{UpdateTargetTracker.ListingLimitToastThreshold} SP khác nhau, đủ ngưỡng → dừng update shop này (nguồn toast, chưa xác nhận câu chữ).");
                            }
                            else
                                _log($"⚠ Toast nghi vượt giới hạn số sản phẩm \"{toastText}\" (SP {itemId}) — {distinct}/{UpdateTargetTracker.ListingLimitToastThreshold} SP khác nhau, chưa đủ ngưỡng dừng shop; SP này đi đường fail thường, không thử lượt Lưu tiếp.");
                        }, saveClickTs, () => saveTimedOut = true)) return true;
                if (productLimitToast) return false;

                var fatalModal = publishFailReason ?? await BigSellerSaveSuccessHelper.TryDetectPublishFailureDialogAsync(page);
                if (BigSellerSaveSuccessHelper.IsListingLimitFailure(fatalModal))
                {
                    _listingLimitDetected = true;
                    _log($"⛔ Shop '{_settings.ShopName}' đã đạt giới hạn số sản phẩm (listing limit) — dừng update shop này.");
                    _targetTracker?.MarkListingLimitReached();
                    return false;
                }
                // Không xác nhận được lưu → log mọi toast ghi được từ lúc bấm Lưu để lấy mẫu thật bổ sung bộ nhận diện. Lời mở đầu
                // theo đúng lý do: "hết giờ" CHỈ khi helper báo hết giờ; modal lỗi khác thì nói là modal; còn lại (tab/context
                // đóng) không dump. Hai cờ riêng với _errorToastDumped của luồng media: suất NỘI DUNG chỉ khóa khi đã log được
                // toast; dòng "không bắt được toast nào" có suất riêng (1 lần/lane) để không chiếm mất suất nội dung.
                var dumpLead = saveTimedOut ? "Lưu hết giờ, không xác nhận được"
                    : fatalModal is not null ? "Lưu thất bại (modal lỗi, không phải giới hạn số SP)" : null;
                if (dumpLead is not null)
                    await BigSellerMaterialCenterCleaner.DumpSaveToastsOnceAsync(page, saveClickTs, dumpLead, _log,
                        () => { if (_saveToastContentDumped) return false; _saveToastContentDumped = true; return true; },
                        () => { if (_saveToastEmptyDumped) return false; _saveToastEmptyDumped = true; return true; });
                await DismissBlockingModalAsync(page);
                await CloseVisibleAntModalAsync(page, 2000);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log($"  ↳ Lưu attempt {attempt + 1} lỗi: {ex.Message}"); }
            await DelayAsync(2500, ct);
        }
        _log($"⚠ Lưu thất bại sau {maxAttempts} lượt — không xác nhận được đã lưu, SP sẽ thử lại vòng sau.");
        return false;
    }

    private async Task<string?> DetectSaveErrorAsync(IPage page, int timeoutMs)
    {
        var deadline = timeoutMs;
        var step = 400;
        while (deadline > 0)
        {
            foreach (var sel in SaveErrSels)
            {
                try
                {
                    var loc = page.Locator(sel).First;
                    if (await loc.CountAsync() == 0) continue;
                    var txt = Normalize(await loc.InnerTextAsync());
                    if (txt.Contains("brand cannot be empty")) return "brand";
                    if (txt.Contains("bieu do kich co khong duoc de trong")) return "other";
                }
                catch { }
            }
            await DelayAsync(step, CancellationToken.None);
            deadline -= step;
        }
        return null;
    }
}
