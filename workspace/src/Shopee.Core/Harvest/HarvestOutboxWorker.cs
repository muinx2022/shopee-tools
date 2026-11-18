using System.Net;
using Shopee.Core.Coordination;

namespace Shopee.Core.Harvest;

/// <summary>Tham số của <see cref="HarvestOutboxWorker"/> (mặc định theo plan GĐ4; test thu nhỏ cho nhanh).</summary>
public sealed class HarvestOutboxWorkerOptions
{
    /// <summary>Nhịp quét hàng đợi khi rảnh / sau lỗi.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Số mục tối đa mỗi lô (= trần Hub).</summary>
    public int MaxBatch { get; init; } = HarvestedProductLimits.MaxBatch;
    /// <summary>Lùi dần: <c>min(MaxBackoff, BaseBackoff × 2^(n−1))</c>, n = số lần lỗi liên tiếp.</summary>
    public TimeSpan BaseBackoff { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan MaxBackoff { get; init; } = TimeSpan.FromMinutes(5);
    /// <summary>Mục nghi vấn gửi MỘT MÌNH bị 500 có bằng chứng chừng này lần → <c>failed</c>.</summary>
    public int Suspect500Cap { get; init; } = 5;
}

/// <summary>Kết quả một lượt đẩy (cho vòng nền + test).</summary>
internal enum HarvestPushOutcome
{
    /// <summary>Không có mục nào đến hạn.</summary>
    Idle,
    /// <summary>Chưa tới mốc lùi dần chung, hoặc chỉ còn mục nghi vấn chưa đến hạn riêng.</summary>
    NotDue,
    /// <summary>Hub trả 200 và đã xử lý kết quả từng mục.</summary>
    Sent,
    /// <summary>Lô quá cỡ (413 / batch-too-large) — đã giảm cỡ lô, đẩy tiếp ngay.</summary>
    Split,
    /// <summary>Lô bị đánh lỗi (400 khác / mục quá cỡ / mục nghi vấn đủ trần 500) — đẩy tiếp ngay.</summary>
    Failed,
    /// <summary>Phản hồi khác 200 (500, 5xx, 503, 401, timeout, mất mạng, chưa kết nối Hub, body 200 hỏng) — lùi dần chung.</summary>
    Transient,
    /// <summary>Mục nghi vấn gửi lẻ bị 500 — đã hoãn riêng mục (có bằng chứng thì không đụng mốc chung); đẩy tiếp lô khác.</summary>
    Deferred,
}

/// <summary>
/// Vòng nền đẩy <see cref="HarvestOutbox"/> lên Hub (<see cref="HubRoutes.HarvestedProductsBatch"/>), ĐỘC LẬP với vòng cào
/// (engine chỉ ghi outbox; Hub chậm/sập không làm chậm cào). Mỗi <see cref="HarvestOutboxWorkerOptions.Interval"/> đẩy liên
/// tục tới khi hết mục hoặc gặp lỗi.
/// <para><b>Lùi dần CHUNG</b> cho MỌI phản hồi khác 200 (kể cả 500 — trừ 500 "có bằng chứng" của mục nghi vấn, xem dưới):
/// mốc <c>min(5', 10 s × 2^(n−1))</c> nhiễu ±20%; chưa tới mốc thì không gửi gì. Một lần 200 xoá mốc. Hub sập hẳn suốt 1
/// giờ chỉ tốn ~17 request. <see cref="FlushAsync"/> bỏ qua mốc; mục nghi vấn bị 500 trong lúc Flush thì bỏ qua mục đó,
/// đi tiếp.</para>
/// <para><b>Mục nghi vấn</b> (cờ trong RAM): lô thường bị 500 → mọi mục trong lô thành nghi vấn (không cô lập theo lô).
/// Hub vừa sống lại (200 đầu tiên sau ≥5 lần 500 liền, ở các mục rời nhau) → bỏ cờ của các mục chưa từng 500 có bằng chứng, để hàng chờ đi
/// lại theo lô. Mỗi lượt chọn XEN KẼ: lượt trước là lô thường và có mục nghi vấn đến hạn → gửi MỘT mục nghi vấn một mình; không
/// thì lô thường (≤50 mục, chỉ mục không nghi vấn, đã đến hạn); hết loại này lấy loại kia. Lô thường vì vậy không bao giờ bị
/// nghi vấn chặn. Mục nghi vấn gửi một mình:</para>
/// <list type="bullet">
/// <item>200 → xử như thường (<c>ok=true</c> xoá, <c>ok=false</c> failed) và bỏ cờ nghi vấn.</item>
/// <item>500 → chỉ là "500 có BẰNG CHỨNG" khi đã có một phản hồi 200 (lô bất kỳ) XẢY RA SAU lần 500 gần nhất của chính
/// mục này (so số thứ tự phản hồi). Không có bằng chứng = có thể Hub đang sập → không đếm, lùi dần CHUNG. Có bằng chứng
/// → chỉ hoãn RIÊNG mục (không đụng mốc chung, mục độc không kéo tốc độ cả hàng chờ); đủ
/// <see cref="HarvestOutboxWorkerOptions.Suspect500Cap"/> lần → <c>failed</c>. Mốc hoãn riêng lùi dần theo số lần đã thử.</item>
/// </list>
/// <para>Bảng phản hồi khác: 200 thiếu kết quả cho một mục → giữ mục, lùi dần theo số lần thử của mục; body 200 không đọc
/// được → giữ cả lô (lùi dần chung). 413 / 400 <c>batch-too-large</c> → giảm cỡ lô một nửa (nới ×2 lại sau mỗi 10 lần
/// 200 ở cỡ đó), lô 1 mục mà vẫn quá cỡ → <c>failed</c>; không tính là nghi vấn. 400 khác (<c>client-now</c>, body hỏng) = lỗi code
/// client → <c>failed</c> cả lô + log.</para>
/// <para><c>clientNow</c> đặt NGAY TRƯỚC lời gọi HTTP mỗi lần gửi (kể cả gửi lại), không lưu trong lô.</para>
/// <para>Cờ nghi vấn / mốc chung / cỡ lô nằm trong RAM: tắt app thì lần sau bắt đầu lại (an toàn — chỉ tốn thêm vài lượt
/// gửi). Hẹn gửi lại từng mục (<c>next_try_at</c>) và số lần thử nằm trong DB.</para>
/// </summary>
public sealed class HarvestOutboxWorker : IAsyncDisposable
{
    private readonly HarvestOutbox _outbox;
    private readonly Func<HubClient?> _client;
    private readonly Action<string>? _log;
    private readonly Func<DateTimeOffset> _clock;
    private readonly HarvestOutboxWorkerOptions _opt;
    private readonly Func<double> _random;

    /// <summary>Một lượt đẩy tại một thời điểm (vòng nền, FlushAsync).</summary>
    private readonly SemaphoreSlim _pushGate = new(1, 1);
    /// <summary>Tín hiệu đánh thức — tối đa 1 (gọi Nudge nhiều lần trong lúc bận chỉ đánh thức một lần).</summary>
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly object _life = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Trạng thái RAM của một mục nghi vấn (khoá = id dòng outbox).</summary>
    private sealed class Suspect
    {
        /// <summary>Số thứ tự phản hồi của lần 500 gần nhất của mục — bằng chứng phải là một 200 SAU số này.</summary>
        public long LastFailSeq { get; set; }
        public int Evidenced500 { get; set; }
        public int Attempts { get; set; }
        public long NextTryMs { get; set; }
    }

    private readonly SortedDictionary<long, Suspect> _suspects = [];
    private bool _lastWasNormal;
    private long _lastSuspectId = long.MinValue;
    /// <summary>Số lần 200 liên tiếp ở cỡ lô hiện tại — đủ thì nới cỡ lô (sau khi bị 413 thu nhỏ).</summary>
    private int _okAtCap;
    private int _failStreak;
    private long _globalNextTryMs;
    private int _batchCap;
    /// <summary>Số thứ tự phản hồi HTTP (tăng mỗi lần Hub trả lời) và số thứ tự của lần 200 gần nhất.</summary>
    private long _responseSeq;
    private long _last200Seq = -1;
    /// <summary>Số phản hồi 500 liên tiếp (không có 200 xen giữa) và tập id các mục đã gửi trong chuỗi đó. "Hub vừa sống
    /// lại" = chuỗi ≥ <see cref="HubRecoveredAfter500s"/> lần VÀ có hai lần 500 với tập mục RỜI NHAU (Hub từ chối cả những
    /// mục khác nhau). Một mục độc nằm một mình bị 500 lặp không thoả điều kiện rời nhau → không bị bỏ cờ oan.</summary>
    private int _consec500;
    private readonly HashSet<long> _ids500Chain = [];
    private bool _disjoint500;
    /// <summary>Chuỗi 500 dài chừng này (kèm 500 ở các mục rời nhau) rồi gặp 200 = Hub vừa sống lại.</summary>
    private const int HubRecoveredAfter500s = 5;
    /// <summary>Trong <see cref="FlushAsync"/>: mục nghi vấn vừa bị 500 — bỏ qua, đi tiếp mục khác.</summary>
    private HashSet<long>? _flushSkip;

    private long _sessionPushed;
    private long _sessionApplied;
    private int _passes;

    /// <summary>Có thay đổi số liệu (đẩy xong / lỗi / requeue) — UI gọi lại <see cref="GetStatsAsync"/>. Gọi từ luồng nền.</summary>
    public event Action? Changed;

    /// <summary>Lỗi gần nhất (null sau một lần đẩy thành công) — để UI hiện "Hub lỗi: …".</summary>
    public string? LastError { get; private set; }

    /// <summary>Số mục Hub đã nhận / thật sự ghi TRONG PHIÊN này (số cộng dồn bền xem <see cref="HarvestOutboxStats"/>).</summary>
    public long SessionPushed => Interlocked.Read(ref _sessionPushed);
    public long SessionApplied => Interlocked.Read(ref _sessionApplied);

    /// <summary>Số lượt <see cref="PushOnceAsync"/> đã chạy (cho test vòng nền).</summary>
    internal int Passes => Volatile.Read(ref _passes);

    /// <summary>Số mục đang bị nghi vấn (cho test / chẩn đoán).</summary>
    internal int SuspectCount => _suspects.Count;

    /// <summary>Id dòng outbox đang bị nghi vấn (bản chụp, cho test).</summary>
    internal IReadOnlyCollection<long> SuspectIds => _suspects.Keys.ToList();

    /// <param name="client">Lấy HubClient HIỆN TẠI mỗi lần gửi (vd <c>() =&gt; CoordinationRuntime.Client</c> — client bị tráo
    /// khi kết nối lại). null = chưa kết nối Hub → coi như lỗi thoáng qua.</param>
    /// <param name="random">Nguồn ngẫu nhiên [0,1) cho nhiễu lùi dần (test tiêm giá trị cố định cho tất định).</param>
    public HarvestOutboxWorker(HarvestOutbox outbox, Func<HubClient?> client, Action<string>? log = null,
        HarvestOutboxWorkerOptions? options = null, Func<DateTimeOffset>? clock = null, Func<double>? random = null)
    {
        _outbox = outbox;
        _client = client;
        _log = log;
        _opt = options ?? new HarvestOutboxWorkerOptions();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _random = random ?? Random.Shared.NextDouble;
        _batchCap = Math.Max(1, _opt.MaxBatch);
    }

    public Task<HarvestOutboxStats> GetStatsAsync() => _outbox.GetStatsAsync();

    // ── Vòng đời ────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Chạy vòng nền (gọi lại khi đang chạy = không làm gì).</summary>
    public void Start()
    {
        lock (_life)
        {
            if (_loop is not null) return;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _loop = Task.Run(() => LoopAsync(ct));
        }
    }

    /// <summary>Dừng vòng nền, chờ lượt đẩy đang bay kết thúc (huỷ HTTP đang chờ).</summary>
    public async Task StopAsync()
    {
        Task? loop;
        lock (_life)
        {
            loop = _loop;
            _cts?.Cancel();
            _loop = null;
        }
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        lock (_life)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>Đẩy nốt khi đóng app: bỏ qua mọi mốc lùi dần, đẩy liên tục tới khi hết mục / gặp phản hồi khác 200 / hết
    /// <paramref name="timeout"/>. Trả true nếu hàng chờ (<c>pending</c>) đã rỗng. Không ném (trừ khi outbox hỏng).</summary>
    public async Task<bool> FlushAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        await _pushGate.WaitAsync().ConfigureAwait(false);
        _flushSkip = [];
        _pushGate.Release();
        try
        {
            // Mục nghi vấn bị 500 → bỏ qua mục đó (_flushSkip), đi tiếp. Nhiều mục nghi vấn liền nhau cùng 500 mà không có
            // 200 nào xen giữa thì có thể Hub đang sập → dừng, không dội từng mục.
            var deferredRun = 0;
            while (true)
            {
                var o = await PushOnceAsync(ignoreBackoff: true, cts.Token).ConfigureAwait(false);
                if (o == HarvestPushOutcome.Deferred)
                {
                    if (++deferredRun >= 3) break;
                    continue;
                }
                if (o is not (HarvestPushOutcome.Sent or HarvestPushOutcome.Split or HarvestPushOutcome.Failed)) break;
                if (o == HarvestPushOutcome.Sent) deferredRun = 0;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { Log("Đẩy nốt dữ liệu lên Hub: hết thời gian chờ."); }
        finally
        {
            await _pushGate.WaitAsync().ConfigureAwait(false);
            _flushSkip = null;
            _pushGate.Release();
        }
        return (await _outbox.GetStatsAsync().ConfigureAwait(false)).Pending == 0;
    }

    /// <summary>Nút "Đẩy lại mục lỗi": mọi mục <c>failed</c> về <c>pending</c>, đánh thức vòng nền. Trả số mục.</summary>
    public async Task<int> RequeueFailedAsync()
    {
        var n = await Task.Run(_outbox.RequeueFailed).ConfigureAwait(false);
        if (n > 0)
        {
            Log($"Đưa {n} mục lỗi về hàng chờ đẩy Hub.");
            Changed?.Invoke();
            Nudge();
        }
        return n;
    }

    /// <summary>Vừa nối lại Hub (người dùng lưu cấu hình Hub): bỏ mốc lùi dần chung + mốc hẹn của các mục đang chờ (lùi vì "chưa kết
    /// nối Hub" / lỗi mạng trước đó) rồi đánh thức vòng nền đẩy ngay. Mục nghi vấn / mục lỗi (Hub từ chối) giữ nguyên.</summary>
    public async Task ResetBackoffAsync()
    {
        await _pushGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _globalNextTryMs = 0;
            _failStreak = 0;
            _outbox.ClearDeferrals();
        }
        finally { _pushGate.Release(); }
        Nudge();
    }

    /// <summary>Đánh thức vòng nền đẩy ngay (vd vừa lưu mục mới, vừa kết nối lại Hub). Mốc lùi dần chung vẫn giữ.</summary>
    public void Nudge()
    {
        try { _wake.Release(); }
        catch (SemaphoreFullException) { /* đã có một tín hiệu chờ sẵn — đủ */ }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                HarvestPushOutcome o;
                do o = await PushOnceAsync(ignoreBackoff: false, ct).ConfigureAwait(false);
                while (o is (HarvestPushOutcome.Sent or HarvestPushOutcome.Split or HarvestPushOutcome.Failed or HarvestPushOutcome.Deferred)
                       && !ct.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Lỗi outbox (đĩa, file hỏng…) không được giết vòng nền: ghi log, nhịp sau thử lại.
                LastError = "outbox: " + ex.Message;
                Log("Đẩy Hub: lỗi outbox — " + ex.Message);
            }
            try { await _wake.WaitAsync(_opt.Interval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    // ── Một lượt đẩy ────────────────────────────────────────────────────────────────────────────────────────────────

    internal async Task<HarvestPushOutcome> PushOnceAsync(bool ignoreBackoff, CancellationToken ct)
    {
        await _pushGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Interlocked.Increment(ref _passes);
            var o = await PushCoreAsync(ignoreBackoff, ct).ConfigureAwait(false);
            if (o is not (HarvestPushOutcome.Idle or HarvestPushOutcome.NotDue)) Changed?.Invoke();
            return o;
        }
        finally { _pushGate.Release(); }
    }

    private async Task<HarvestPushOutcome> PushCoreAsync(bool ignoreBackoff, CancellationToken ct)
    {
        var nowMs = _clock().ToUnixTimeMilliseconds();
        if (!ignoreBackoff && nowMs < _globalNextTryMs) return HarvestPushOutcome.NotDue;

        // Xen kẽ: lượt trước là lô thường → ưu tiên một mục nghi vấn; ngược lại ưu tiên lô thường. Hết loại này lấy loại kia.
        List<HarvestOutboxEntry> batch;
        var suspectTurn = _lastWasNormal;
        if (suspectTurn)
        {
            batch = TakeSuspect(nowMs, ignoreBackoff);
            if (batch.Count == 0) { batch = TakeNormal(nowMs, ignoreBackoff); suspectTurn = false; }
        }
        else
        {
            batch = TakeNormal(nowMs, ignoreBackoff);
            if (batch.Count == 0) { batch = TakeSuspect(nowMs, ignoreBackoff); suspectTurn = batch.Count > 0; }
        }
        if (batch.Count == 0) return _suspects.Count > 0 ? HarvestPushOutcome.NotDue : HarvestPushOutcome.Idle;
        var isSuspect = suspectTurn;
        _lastWasNormal = !isSuspect;

        var client = _client();
        if (client is null) return Transient(batch, "chưa kết nối Hub");

        // Dựng lô KHÔNG có clientNow; đặt clientNow ngay trước lời gọi HTTP.
        var request = new HarvestedProductBatchRequest(null, null, batch.Select(e => e.Item).ToList());
        HttpStatusCode status;
        HarvestedProductBatchResponse? body;
        string? code;
        try
        {
            (status, body, code) = await client.PostHarvestedBatchAsync(request with { ClientNow = _clock() }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Transient(batch, "Hub không phản hồi (timeout)"); }
        catch (HttpRequestException ex) { return Transient(batch, "mất kết nối Hub: " + ex.Message); }
        // Người dùng vừa lưu cấu hình Hub → Reconnect dispose client cũ giữa lúc lô đang bay: lỗi TẠM của kết nối, không phải lỗi outbox.
        catch (ObjectDisposedException) { return Transient(batch, "kết nối Hub vừa đổi (đang nối lại)"); }
        _responseSeq++;

        var codeNo = (int)status;
        if (codeNo is >= 200 and < 300)
        {
            if (body?.Results is null) return Transient(batch, $"phản hồi Hub HTTP {codeNo} không đọc được");
            ApplyResults(batch, body.Results);
            foreach (var e in batch) _suspects.Remove(e.Id);
            if (_consec500 >= HubRecoveredAfter500s && _disjoint500)
            {
                // 200 đầu tiên sau một chuỗi toàn 500 = Hub vừa sống lại: cờ nghi vấn đặt trong lúc sập là nhiễu — mục chưa
                // từng 500 "có bằng chứng" về lại lô thường (không thì cả hàng chờ đi lẻ từng mục).
                var cleared = _suspects.Where(x => x.Value.Evidenced500 == 0).Select(x => x.Key).ToList();
                foreach (var id in cleared) _suspects.Remove(id);
                if (cleared.Count > 0) Log($"Đẩy Hub: Hub đã trả lời lại sau {_consec500} lần 500 — bỏ nghi vấn {cleared.Count} mục.");
            }
            _consec500 = 0;
            _ids500Chain.Clear();
            _disjoint500 = false;
            _failStreak = 0;
            _globalNextTryMs = 0;
            _last200Seq = _responseSeq;
            // Sau 413: nới cỡ lô dần (×2 sau mỗi 10 lần 200 ở cỡ hiện tại) — nới ngay thì lô lại vừa quá cỡ, 413/200 xen nhau.
            if (_batchCap < _opt.MaxBatch && ++_okAtCap >= 10)
            {
                _batchCap = Math.Min(Math.Max(1, _opt.MaxBatch), _batchCap * 2);
                _okAtCap = 0;
            }
            return HarvestPushOutcome.Sent;
        }

        if (status == HttpStatusCode.RequestEntityTooLarge
            || (status == HttpStatusCode.BadRequest && code == "batch-too-large"))
        {
            if (batch.Count > 1)
            {
                // Không phải nghi vấn: chỉ là lô to. Giảm cỡ lô một nửa, đẩy tiếp ngay.
                _batchCap = Math.Max(1, batch.Count / 2);
                _okAtCap = 0;
                Log($"Đẩy Hub: HTTP {codeNo} {code} — lô {batch.Count} mục quá lớn, giảm còn {_batchCap}.");
                return HarvestPushOutcome.Split;
            }
            return FailAll(batch, $"http-{codeNo} {code ?? "too-large"}: một SP vượt giới hạn body của Hub");
        }

        if (status == HttpStatusCode.BadRequest)
            return FailAll(batch, $"http-400 {code ?? "bad-request"}: Hub từ chối cả lô (lỗi code client)");

        if (status == HttpStatusCode.InternalServerError)
        {
            _consec500++;
            if (_ids500Chain.Count > 0 && !batch.Any(e => _ids500Chain.Contains(e.Id))) _disjoint500 = true;
            foreach (var e in batch) _ids500Chain.Add(e.Id);
            return On500(batch, isSuspect);
        }

        return Transient(batch, $"Hub trả HTTP {codeNo}{(code is null ? "" : " " + code)}");
    }

    /// <summary>Lô thường: ≤ cỡ lô hiện tại, chỉ mục KHÔNG nghi vấn và đã đến hạn (cũ trước).</summary>
    private List<HarvestOutboxEntry> TakeNormal(long nowMs, bool ignoreBackoff) =>
        _outbox.TakeDue(_batchCap, ignoreBackoff ? long.MaxValue : nowMs, _suspects.Keys.ToList());

    /// <summary>MỘT mục nghi vấn đến hạn riêng, XOAY VÒNG theo id (bắt đầu sau mục nghi vấn vừa gửi) — không thì mục độc id
    /// nhỏ đến hạn lại đúng lúc mốc chung hết sẽ bị chọn mãi, che các mục nghi vấn lành phía sau. Mục đã rời hàng chờ (đã
    /// xoá / failed) thì bỏ cờ.</summary>
    private List<HarvestOutboxEntry> TakeSuspect(long nowMs, bool ignoreBackoff)
    {
        var all = _suspects.ToList();
        var order = all.Where(x => x.Key > _lastSuspectId).Concat(all.Where(x => x.Key <= _lastSuspectId));
        foreach (var (id, s) in order)
        {
            if (!ignoreBackoff && s.NextTryMs > nowMs) continue;
            if (_flushSkip?.Contains(id) == true) continue;
            var e = _outbox.TakeByIds([id]);
            if (e.Count == 0) { _suspects.Remove(id); continue; }
            _lastSuspectId = id;
            return e;
        }
        return [];
    }

    /// <summary>HTTP 500: lùi dần chung (có thể Hub đang hỏng). Lô thường → mọi mục thành nghi vấn. Mục nghi vấn gửi một
    /// mình → đếm khi có bằng chứng, đủ trần → failed.</summary>
    private HarvestPushOutcome On500(List<HarvestOutboxEntry> batch, bool isSuspect)
    {
        if (!isSuspect)
        {
            foreach (var e in batch)
                _suspects[e.Id] = new Suspect { LastFailSeq = _responseSeq, Attempts = 0, NextTryMs = 0 };
            Log($"Đẩy Hub: HTTP 500 với lô {batch.Count} mục — đánh dấu nghi vấn, sẽ gửi lẻ từng mục xen với lô thường.");
            return Transient(batch, "Hub lỗi HTTP 500");
        }

        var e1 = batch[0];
        var s = _suspects.TryGetValue(e1.Id, out var cur) ? cur : _suspects[e1.Id] = new Suspect();
        var evidence = _last200Seq > s.LastFailSeq;
        if (evidence) s.Evidenced500++;
        s.LastFailSeq = _responseSeq;
        s.Attempts++;
        if (s.Evidenced500 >= _opt.Suspect500Cap)
        {
            _suspects.Remove(e1.Id);
            // Lô thường vẫn cần lùi dần chung? Không: Hub vừa trả lời bình thường cho lô khác — chỉ SP này hỏng.
            return FailAll(batch, $"http-500 lặp {s.Evidenced500} lần với riêng SP này (SP khác vẫn vào)");
        }
        s.NextTryMs = Backoff(s.Attempts);
        _flushSkip?.Add(e1.Id);
        var msg = $"Hub lỗi HTTP 500 với SP nghi vấn {e1.Item.ItemId} ({s.Evidenced500}/{_opt.Suspect500Cap} lần có bằng chứng)";
        if (!evidence)
        {
            // Không có 200 nào xen từ lần 500 trước của mục: có thể Hub đang sập → lùi dần CHUNG.
            Transient(batch, msg, s.NextTryMs);
            return HarvestPushOutcome.Deferred;
        }
        // Có bằng chứng (Hub vừa nhận lô khác): chỉ SP này hỏng → chỉ hoãn RIÊNG mục, KHÔNG đẩy mốc chung — mục độc không
        // được kéo tốc độ đẩy của cả hàng chờ.
        _outbox.Defer(batch, s.NextTryMs, msg);
        LastError = msg;
        Log($"Đẩy Hub: {msg} — hoãn riêng SP này {Secs(s.NextTryMs)} s, lô khác đi tiếp.");
        return HarvestPushOutcome.Deferred;
    }

    /// <summary>Kết quả 200: khớp theo CHỈ SỐ mục (+ itemId cho chắc). Thiếu/lệch → giữ mục, lùi dần theo số lần thử của
    /// mục.</summary>
    private void ApplyResults(List<HarvestOutboxEntry> batch, List<HarvestedProductResult> results)
    {
        var done = new List<(HarvestOutboxEntry, bool)>();
        var failed = new List<(HarvestOutboxEntry, string)>();
        var seen = new bool[batch.Count];
        foreach (var r in results)
        {
            if (r is null || r.Index < 0 || r.Index >= batch.Count || seen[r.Index]) continue;
            var e = batch[r.Index];
            if (r.ItemId != e.Item.ItemId) continue;
            seen[r.Index] = true;
            if (r.Ok) done.Add((e, r.Applied));
            else failed.Add((e, "hub: " + (r.Reason ?? "từ chối")));
        }
        _outbox.Complete(done);
        _outbox.MarkFailed(failed);
        var orphan = batch.Where((_, i) => !seen[i]).ToList();
        foreach (var e in orphan)
            _outbox.Defer([e], Backoff(e.Attempts + 1), "Hub không trả kết quả cho mục này");
        if (orphan.Count > 0)
            Log($"Đẩy Hub: {orphan.Count} mục không có kết quả khớp trong phản hồi — giữ lại gửi sau.");
        Interlocked.Add(ref _sessionPushed, done.Count);
        Interlocked.Add(ref _sessionApplied, done.Count(d => d.Item2));
        LastError = null;
        if (failed.Count > 0)
            Log($"Đẩy Hub: {failed.Count} mục bị Hub từ chối (vd {failed[0].Item1.Item.ItemId}: {failed[0].Item2}).");
    }

    /// <summary>Phản hồi khác 200: lùi dần CHUNG (không gửi gì tới mốc). Mục giữ nguyên, hẹn theo mốc chung (hoặc
    /// <paramref name="entryNextTryMs"/> nếu xa hơn — mốc riêng của mục nghi vấn).</summary>
    private HarvestPushOutcome Transient(List<HarvestOutboxEntry> batch, string error, long entryNextTryMs = 0)
    {
        _failStreak++;
        _globalNextTryMs = Backoff(_failStreak);
        _outbox.Defer(batch, Math.Max(_globalNextTryMs, entryNextTryMs), error);
        LastError = error;
        Log($"Đẩy Hub: {error} — giữ {batch.Count} mục, mọi lô chờ {Secs(_globalNextTryMs)} s.");
        return HarvestPushOutcome.Transient;
    }

    private HarvestPushOutcome FailAll(List<HarvestOutboxEntry> batch, string reason)
    {
        _outbox.MarkFailed(batch.Select(e => (e, reason)).ToList());
        foreach (var e in batch) _suspects.Remove(e.Id);
        LastError = reason;
        Log($"Đẩy Hub: đánh lỗi {batch.Count} mục — {reason}.");
        return HarvestPushOutcome.Failed;
    }

    /// <summary>Mốc gửi lại (unix ms) sau <paramref name="n"/> lần lỗi liên tiếp, nhiễu ±20% (tránh khoá pha với Hub tắt-mở
    /// theo chu kỳ / nhiều máy cùng dội lại một lúc).</summary>
    private long Backoff(int n)
    {
        var ms = _opt.BaseBackoff.TotalMilliseconds * Math.Pow(2, Math.Min(Math.Max(n, 1) - 1, 30));
        ms = Math.Min(ms, _opt.MaxBackoff.TotalMilliseconds);
        ms *= 0.8 + 0.4 * Math.Clamp(_random(), 0, 1);
        return _clock().ToUnixTimeMilliseconds() + (long)ms;
    }

    private string Secs(long atMs) =>
        Math.Max(0, (atMs - _clock().ToUnixTimeMilliseconds()) / 1000.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    private void Log(string text)
    {
        try { _log?.Invoke(text); } catch { /* log hỏng không được làm hỏng vòng đẩy */ }
    }
}
