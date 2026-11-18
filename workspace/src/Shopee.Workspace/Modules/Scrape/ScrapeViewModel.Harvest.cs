using Shopee.Core.Accounts;
using Shopee.Core.Browser;
using Shopee.Core.Harvest;
using Shopee.Core.Infrastructure;
using Shopee.Core.Scrape;
using Shopee.Modules.MultiBrave;
using Shopee.Workspace.Infrastructure;

namespace Shopee.Workspace.Modules.Scrape;

/// <summary>Một đơn vị chạy của app Harvest: một sheet trong file Excel do app tự chọn. Tiến độ khoá theo
/// <see cref="ProgressKey"/> (đường dẫn file đã chuẩn hoá + tên sheet) trong <c>harvest-progress.json</c>.</summary>
public sealed record HarvestSheetJob(string FilePath, string Sheet)
{
    public string ProgressKey => HarvestSource.SheetKey(FilePath, Sheet);
}

/// <summary>Cấu hình chạy của app Harvest (một bộ cho cả app, không theo tài khoản nào).</summary>
public sealed record HarvestRunSettings(int Processes, int FrameSize, int RowsPerAccount, ScrapeRestWindow Rest, int MaxAgeDays);

/// <summary>Báo về màn Harvest trong lúc chạy (gọi từ luồng nền).</summary>
public sealed class HarvestRunCallbacks
{
    /// <summary>Sheet vừa dựng lại tiến độ đầu lượt (kết quả + Hub có trả lời không).</summary>
    public Action<HarvestSheetJob, HarvestDoneRebuildResult, HarvestHubLookup>? Planned { get; init; }
    /// <summary>Tiến độ / trạng thái của sheet vừa đổi (bắt đầu, xong khối, bỏ dòng, kết thúc).</summary>
    public Action<HarvestSheetJob>? Changed { get; init; }
    /// <summary>Sheet đang chạy (null = hết lượt).</summary>
    public Action<HarvestSheetJob?>? Running { get; init; }
}

// Partial của ScrapeViewModel: lượt chạy của app Harvest — lần lượt từng sheet của MỘT file Excel, mỗi sheet một job trong cùng
// phiên (kho tk Shopee, khung, lưới tiến trình, Dừng dùng chung với Scrape). Không có tài khoản BigSeller / shop ở đây.
public sealed partial class ScrapeViewModel
{
    /// <summary>Kết thúc của một sheet: đi tiếp sheet sau hay dừng cả lượt (huỷ, lỗi hạ tầng toàn cục, file đang bị khoá).</summary>
    internal enum HarvestSheetEnd { Next, StopRun }

    /// <summary>Vòng sheet của một lượt (tách ra để test): chạy LẦN LƯỢT theo đúng thứ tự <paramref name="sheets"/>, mọi sheet dùng
    /// CHUNG một <c>runId</c> (lỗi vĩnh viễn của cùng SP ở 2 sheet chỉ tính một lượt); <see cref="HarvestSheetEnd.StopRun"/> hoặc huỷ
    /// → dừng ngay, không chạy sheet sau. Trả số sheet đã chạy.</summary>
    internal static async Task<int> RunSheetsAsync(IReadOnlyList<HarvestSheetJob> sheets, string runId,
        Func<HarvestSheetJob, string, Task<HarvestSheetEnd>> runSheet, CancellationToken ct, HarvestRunCallbacks? callbacks = null)
    {
        var done = 0;
        foreach (var job in sheets)
        {
            if (ct.IsCancellationRequested) break;
            callbacks?.Running?.Invoke(job);
            var end = await runSheet(job, runId);
            done++;
            callbacks?.Changed?.Invoke(job);
            if (end == HarvestSheetEnd.StopRun) break;
        }
        return done;
    }

    /// <summary>Đọc sheet lỗi: file đang bị app Update khoá quá hạn → cả file (mọi sheet sau) cũng vậy → dừng lượt; lỗi khác (sheet
    /// hỏng / vắng) → bỏ RIÊNG sheet đó, sheet sau vẫn chạy.</summary>
    internal static HarvestSheetEnd EndAfterReadError(Exception error) =>
        error is HarvestWorkbookLockedException ? HarvestSheetEnd.StopRun : HarvestSheetEnd.Next;

    /// <summary>Sheet chạy xong: lỗi hạ tầng toàn cục (key proxy chết / chỗ lưu hỏng — <c>_jobFatal</c>) hoặc đã huỷ → dừng lượt
    /// (sheet sau cũng hỏng như vậy); còn lại → sheet sau.</summary>
    internal static HarvestSheetEnd EndAfterRun(bool jobFatal, bool cancelled) =>
        jobFatal || cancelled ? HarvestSheetEnd.StopRun : HarvestSheetEnd.Next;

    /// <summary>Chạy các sheet đã chọn LẦN LƯỢT (sheet sau thấy được SP sheet trước vừa lưu — cùng SP ở 2 sheet chỉ lấy một lần).
    /// Đang chạy rồi / thiếu điều kiện → báo và trả về. Chờ tới khi xong hoặc bị Dừng. TOTAL: không ném.</summary>
    public async Task RunHarvestAsync(IReadOnlyList<HarvestSheetJob> sheets, HarvestRunSettings settings,
        HarvestRunCallbacks? callbacks = null)
    {
        if (IsBusy) { Warn("Đang chạy — bấm Dừng trước."); return; }
        if (sheets.Count == 0) { Warn("Tick chọn ít nhất 1 sheet."); return; }
        if (HarvestRuntime.Current is not { } rt)
        { Warn("Chưa mở được kho lưu dữ liệu (harvest-outbox.db) — xem log khởi động, khởi động lại app."); return; }
        var accounts = AccountStore.Shared.Accounts.Where(a => !a.Disabled).ToList();
        if (accounts.Count == 0) { Warn("Kho chưa có tài khoản Shopee (thêm ở mục Tài khoản & Proxy)."); return; }
        if (Math.Max(1, settings.Processes) > accounts.Count)
        { Warn($"Cần {settings.Processes} tài khoản Shopee (số cửa sổ) nhưng kho chỉ có {accounts.Count}."); return; }
        var sourceUserData = BrowserLauncher.DetectUserData(BrowserKind.Brave);
        if (sourceUserData is null) { Warn("Không tìm thấy User Data của Brave (profile Default). Hãy mở Brave ít nhất 1 lần."); return; }

        var session = new RunSession { SourceUserData = sourceUserData };
        session.Available.AddRange(accounts);
        session.LruTick = AccountStore.Shared.Accounts.Select(a => a.LastUsedTick).DefaultIfEmpty(0).Max();
        // Mỗi lần bấm Chạy là MỘT lượt: lỗi vĩnh viễn của cùng SP thấy ở 2 sheet trong lượt này chỉ tính một lần.
        var runId = Guid.NewGuid().ToString("N");
        var done = 0;
        var cancelToken = session.MasterCts.Token;
        try
        {
            _session = session;
            IsBusy = true;
            ShopeeAccountUsage.Shared.BeginRun();
            _jobFatal.TryRemove(HarvestSource.AccountId, out _);
            OnUi(() =>
            {
                LogLines.Clear();
                Instances.Clear();
                ErroredAccounts.Clear();
            });
            Log($"▶ Lấy dữ liệu {sheets.Count} sheet · kho {accounts.Count} tk Shopee · lấy lại SP có dữ liệu cũ hơn {HarvestPlanning.ClampMaxAgeDays(settings.MaxAgeDays)} ngày.");
            done = await RunSheetsAsync(sheets, runId,
                (job, id) => RunHarvestSheetAsync(session, rt, job, settings, id, callbacks), cancelToken, callbacks);
        }
        catch (Exception ex)
        {
            session.MasterCts.Cancel();
            Log($"✖ Lỗi lượt lấy dữ liệu: {ex.Message}");
        }
        finally
        {
            callbacks?.Running?.Invoke(null);
            Status = session.MasterCts.IsCancellationRequested ? "Đã dừng." : $"Xong {done}/{sheets.Count} sheet.";
            Log($"── {Status} ──");
            try { AccountStore.Shared.SaveOverlayOnly(); } catch { }
            _session = null;
            session.MasterCts.Dispose();
            IsBusy = false;
            ShopeeAccountUsage.Shared.EndRun();
        }
    }

    /// <summary>Đăng ký + chạy job của MỘT sheet trong phiên (vào sổ job để Dừng / bấm dòng tiến trình dùng được) rồi chờ xong.</summary>
    private async Task<HarvestSheetEnd> RunHarvestSheetAsync(RunSession s, HarvestRuntime rt, HarvestSheetJob job,
        HarvestRunSettings settings, string runId, HarvestRunCallbacks? callbacks)
    {
        var end = HarvestSheetEnd.StopRun;
        JobHandle? handle = null;
        var started = s.Jobs.TryAdd(job.ProgressKey, () =>
        {
            var h = new JobHandle
            {
                Target = null,
                Shop = null,
                Sheet = job.ProgressKey,
                DataPath = job.FilePath,
                DataSheet = job.Sheet,
                ShopId = "",
                Seq = Interlocked.Increment(ref s.JobSeq),
                Cts = CancellationTokenSource.CreateLinkedTokenSource(s.MasterCts.Token),
            };
            // Như StartJob: KHÔNG truyền token vào Task.Run (token đã huỷ thì body + finally không chạy → kẹt sổ).
            h.Task = Task.Run(async () => end = await HarvestSheetBodyAsync(s, h, rt, job, settings, runId, callbacks));
            handle = h;
            return h;
        });
        if (!started || handle is null) return HarvestSheetEnd.StopRun;   // phiên đang đóng
        await handle.Task;
        return end;
    }

    private async Task<HarvestSheetEnd> HarvestSheetBodyAsync(RunSession s, JobHandle h, HarvestRuntime rt, HarvestSheetJob job,
        HarvestRunSettings settings, string runId, HarvestRunCallbacks? callbacks)
    {
        const string acct = HarvestSource.AccountId;
        var key = job.ProgressKey;
        var label = job.Sheet;
        var store = RoleStores.ScrapeProgress;
        var ct = h.Cts.Token;
        void LogH(string m) => LogAcc(acct, label, $"[{label}] {m}");
        // Huỷ TRƯỚC khi dựng lại (vd đang chờ app Update nhả khoá file) thì KHÔNG được đụng trạng thái tiến độ.
        var progressTouched = false;
        var startRow = 1;
        SessionAccountPool? pool = null;
        List<string>? frameIds = null;
        try
        {
            HarvestWorkbookRange range;
            try { range = await HarvestWorkbookReader.ReadSheetAsync(job.FilePath, job.Sheet, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var readEnd = EndAfterReadError(ex);
                LogH(readEnd == HarvestSheetEnd.StopRun
                    ? $"⏳ {ex.Message}"
                    : $"✘ Không đọc được sheet: {ex.Message} — bỏ qua sheet này.");
                return readEnd;
            }
            if (range.Info?.Warning(job.Sheet) is { } warning) LogH("⚠ " + warning);
            startRow = range.DenseStart;
            var totalRows = range.DenseEnd;
            if (totalRows < startRow) { LogH("Sheet không có dòng dữ liệu nào — bỏ qua."); return HarvestSheetEnd.Next; }

            // Dựng lại tiến độ từ tập SP đã lấy (toàn cục, còn hạn) + SP Hub đã có còn hạn + lỗi vĩnh viễn — không tin số dòng lượt trước.
            var items = range.Links?.Items ?? [];
            var doneKeys = rt.Outbox.DoneKeys(HarvestPlanning.DoneSince(DateTimeOffset.UtcNow, settings.MaxAgeDays));
            var hub = await HarvestPlanning.AskHubAsync(rt.KnownQuery, HarvestPlanning.ItemIdsToAsk(items, doneKeys),
                settings.MaxAgeDays, ct).ConfigureAwait(false);
            if (hub.Known is null)
                LogH($"⚠ Không hỏi được Hub SP nào đã có ({hub.Error}) — lượt này chỉ bỏ qua SP đã lấy trên máy này.");
            var plan = HarvestPlanning.Build(range, doneKeys, rt.Outbox.FailedKeys(), hub.Known);
            store.ReplaceProgress(acct, key, label, totalRows, plan.Completed, plan.Skipped);
            progressTouched = true;
            callbacks?.Planned?.Invoke(job, plan, hub);
            LogH($"Đã có {plan.DoneRows} dòng · bỏ qua/lỗi {plan.Skipped.Count} dòng · còn phải lấy {plan.TodoRows} dòng.");
            if (plan.Remaining.Count == 0)
            {
                store.FinishRun(acct, key, startRow, totalRows);
                LogH("✓ Không còn dòng nào phải lấy.");
                return HarvestSheetEnd.Next;
            }

            var maxProc = Math.Max(1, settings.Processes);
            var frameSize = Math.Max(maxProc, Math.Max(1, settings.FrameSize));
            HashSet<string> mineIds = new(StringComparer.Ordinal), blockedIds = new(StringComparer.Ordinal);
            var frame = s.ClaimFrame(frameSize, preferIds: null, mineIds, blockedIds);
            frameIds = frame.Select(a => a.Id).ToList();
            if (frame.Count == 0) { LogH("Kho tk Shopee đã cạn (mọi tk đang bận / bị tắt) — dừng lượt."); return HarvestSheetEnd.StopRun; }
            store.SaveFrame(acct, key, frameIds);
            var procs = Math.Max(1, Math.Min(maxProc, frame.Count));
            var seq = h.Seq;
            var brush = JobPalette[(seq - 1) % JobPalette.Length];
            OnUi(() =>
            {
                for (var i = 1; i <= procs; i++)
                    Instances.Add(new ScrapeInstanceViewModel($"{seq}:P{i}", $"[{label}] P{i}", brush));
            });
            BraveFleet.Notice = Log;
            LogH($"── ▶ {plan.TodoRows} dòng cần lấy (tổng sheet {totalRows}) · {procs} cửa sổ · khung {frame.Count} tk Shopee · nghỉ giữa link {settings.Rest} ──");
            store.BeginResume(acct, key, label, totalRows);
            callbacks?.Changed?.Invoke(job);

            var runner = new ScrapeRunner(job.FilePath, "", braveExe: null, s.SourceUserData, bigSellerAccountName: label,
                bigSellerAccountId: acct, useHubData: false, restWindow: settings.Rest, mode: RunnerMode.Harvest)
            {
                // GĐ7: extension tự đẩy Hub (app mở trang + bấm nút hộp nổi bằng chuột thật) → sink chỉ ghi done khi Hub đã nhận;
                // outbox không nhận mục mới (worker vẫn đẩy nốt mục cũ còn trong hàng).
                Harvest = new HarvestRunOptions(rt.ExtensionSink, "",
                    HubStatusText: rt.HubStatusLineAsync,
                    RecordDone: true,
                    FailureSink: rt.Outbox,
                    RunId: runId,   // lỗi vĩnh viễn cần thấy ở ≥ 2 lượt bấm Chạy khác nhau
                    SampleLink: () => HarvestRuntime.Current?.Outbox.LastSavedLink(),
                    ExtensionPush: new HarvestExtensionPush(rt.HubAccess)),
            };
            s.Jobs.TryUpdate(key, j => j.Runner = runner);
            RunnerCreated?.Invoke(runner);
            runner.RowsCompleted += (from, to) => { store.MarkCompleted(acct, key, from, to); callbacks?.Changed?.Invoke(job); };
            runner.RowSkipped += (row, reason) =>
            {
                store.MarkSkipped(acct, key, row);
                LogH($"⚠ Dòng {row} chưa lấy được: {reason} — lượt Chạy sau tự thử lại; SP đã bị bỏ qua thì bấm \"Cào lại dòng đã bỏ\".");
                callbacks?.Changed?.Invoke(job);
            };
            WireRunner(runner, seq, acct, label);
            pool = new SessionAccountPool(job.Sheet, frame,
                (excludeIds, _) => Task.FromResult(s.ClaimReplacement(excludeIds)), msg => LogH(msg));
            await runner.RunAutoAsync(pool, procs, plan.Remaining, Math.Max(1, settings.RowsPerAccount), null, ct).ConfigureAwait(false);

            store.FinishRun(acct, key, startRow, totalRows);
            var after = store.Find(acct, key);
            var skipped = after?.SkippedRows ?? [];
            var skipNote = skipped.Count == 0 ? "" : $" ⚠ BỎ QUA {skipped.Count} dòng (chưa lấy được): {string.Join(", ", skipped.Take(20))}{(skipped.Count > 20 ? "…" : "")} — lượt sau tự thử lại.";
            LogH(string.Equals(after?.Status, Shopee.Core.Coordination.LedgerStatus.Completed, StringComparison.OrdinalIgnoreCase)
                ? $"✔ Xong sheet.{skipNote}"
                : $"■ Chưa xong (xong tới dòng {after?.LastRowReached ?? 0}) — bấm Chạy để chạy nốt.{skipNote}");
            // Lỗi hạ tầng toàn cục (key proxy chết / chỗ lưu hỏng) → mọi sheet sau cũng hỏng như vậy: dừng cả lượt.
            return EndAfterRun(_jobFatal.TryRemove(acct, out _), ct.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            LogH("■ đã dừng — bấm Chạy để chạy nốt.");
            if (progressTouched)
                try { store.FinishRun(acct, key, startRow, 0); } catch { }
            return HarvestSheetEnd.StopRun;
        }
        catch (Exception ex) { LogH($"✘ lỗi: {ex.Message}"); return HarvestSheetEnd.Next; }
        finally
        {
            if (pool is not null)
            {
                try
                {
                    store.SaveFrame(acct, key, pool.ActiveFrameIds());
                    ShopeeAccountUsage.Shared.ReleaseReservation(pool.AllFrameIds());
                }
                catch { }
            }
            else if (frameIds is { Count: > 0 })
            {
                try { ShopeeAccountUsage.Shared.ReleaseReservation(frameIds); } catch { }
            }
            s.Jobs.Remove(key);
            var prefix = h.Seq + ":";
            OnUi(() => { for (var i = Instances.Count - 1; i >= 0; i--) if (Instances[i].Key.StartsWith(prefix, StringComparison.Ordinal)) Instances.RemoveAt(i); });
            h.Cts.Dispose();
        }
    }
}
