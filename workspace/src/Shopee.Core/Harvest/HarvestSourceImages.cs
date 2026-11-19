namespace Shopee.Core.Harvest;

/// <summary>Một lỗi chi tiết của lượt ảnh gốc (extension giữ tối đa 20 lỗi / lượt). <see cref="ImageId"/> null = lỗi cả SP.</summary>
public sealed record SourceImageRoundError(string ItemId, string? ImageId, string Reason);

/// <summary>
/// GĐ9 — kết quả MỘT lượt ảnh gốc của extension <c>shopee-harvest</c> (<c>source-images.js</c>, RPC <c>sourceImagesStatus</c>): tải
/// ảnh SP vừa cào từ CDN rồi đẩy lên kho ảnh gốc Hub trong khoảng nghỉ giữa 2 SP. <see cref="Pushed"/> = ảnh mới lên Hub;
/// <see cref="Existed"/> = Hub đã có (PUT 200 existed); <see cref="Failed"/> = ảnh hỏng (CDN 404 / WebP / Hub từ chối…);
/// <see cref="QueueIds"/> = số ảnh còn trong các việc chờ của profile đó ("còn thiếu"). <see cref="StoppedBy"/>: null (hết việc) |
/// deadline | abort | hubFull | auth | temp | cdnTemp | nocfg.
/// </summary>
public sealed record SourceImageRound(
    string RoundId,
    int Checked,
    int Present,
    int Pushed,
    int Existed,
    int Failed,
    int Skipped,
    int Dropped,
    string? StoppedBy,
    string? HubFullCode,
    string? LastError,
    int QueueJobs,
    int QueueIds,
    IReadOnlyList<SourceImageRoundError> Errors)
{
    public const string StopHubFull = "hubFull";
    public const string StopAuth = "auth";
    public const string StopNoConfig = "nocfg";

    public bool HubFull => StoppedBy == StopHubFull;

    /// <summary>Đọc từ object <c>round</c> của extension; chịu được thiếu trường (số → 0, chữ → null). Không phải object / thiếu
    /// <c>roundId</c> → null.</summary>
    public static SourceImageRound? Parse(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || Str(e, "roundId") is not { Length: > 0 } roundId) return null;
        var errors = new List<SourceImageRoundError>();
        if (e.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
            foreach (var x in errs.EnumerateArray())
                if (x.ValueKind == JsonValueKind.Object && Str(x, "reason") is { Length: > 0 } reason)
                    errors.Add(new SourceImageRoundError(Str(x, "itemId") ?? "", Str(x, "id"), reason));
        var queue = e.TryGetProperty("queue", out var q) && q.ValueKind == JsonValueKind.Object ? q : default;
        return new SourceImageRound(roundId,
            Int(e, "checked"), Int(e, "present"), Int(e, "pushed"), Int(e, "existed"), Int(e, "failed"), Int(e, "skipped"),
            Int(e, "dropped"), Str(e, "stoppedBy"), Str(e, "hubFullCode"), Str(e, "lastError"),
            queue.ValueKind == JsonValueKind.Object ? Int(queue, "jobs") : 0,
            queue.ValueKind == JsonValueKind.Object ? Int(queue, "ids") : 0,
            errors);
    }

    /// <summary>Lỗi gần nhất để hiện: <see cref="LastError"/>, không có thì lỗi chi tiết cuối.</summary>
    public string? LatestError => LastError ?? (Errors.Count > 0 ? Errors[^1].Reason : null);

    /// <summary>Một dòng log job sau lượt: "Ảnh gốc: đẩy 8 · đã có 2 · lỗi 1 · còn thiếu 30" / "⚠ Hub hết chỗ (mã) — dừng đẩy ảnh" /
    /// lỗi token / cấu hình. Lượt không đẩy / không lỗi gì → null (không làm rối log).</summary>
    public string? LogLine()
    {
        if (HubFull) return $"⚠ Hub hết chỗ ({HubFullCode ?? "507"}) — dừng đẩy ảnh gốc; dọn kho ảnh gốc ở app Upload rồi bấm Chạy lại.";
        if (StoppedBy is StopAuth or StopNoConfig) return $"⚠ Ảnh gốc: không đẩy được — {LatestError ?? StoppedBy}.";
        if (Pushed + Existed + Failed + Dropped == 0) return null;
        var line = $"Ảnh gốc: đẩy {Pushed} · đã có {Existed} · lỗi {Failed} · còn thiếu {QueueIds}";
        if (Dropped > 0) line += $" · bỏ {Dropped} SP";
        return line;
    }

    /// <summary>Cảnh báo còn ảnh chưa đẩy: "⚠ Ảnh gốc: còn N ảnh chưa đẩy (lý do) — sẽ đẩy ở khoảng nghỉ sau."</summary>
    public static string PendingWarning(int missing, string why) =>
        $"⚠ Ảnh gốc: còn {missing} ảnh chưa đẩy ({why}) — sẽ đẩy ở khoảng nghỉ sau.";

    public const string WhyShortRest = "nghỉ giữa link quá ngắn";
    public const string WhyBlockEnd = "hết khối";

    private static int Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n > 0 ? n : 0;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
}

/// <summary>Trả lời <c>startSourceImages</c>: <see cref="Started"/> = lượt mới đã chạy nền; false + <see cref="RoundId"/> = đang có lượt
/// khác (theo dõi lượt đó); false không RoundId = không chạy (<see cref="Reason"/>). <see cref="Unsupported"/> = extension cũ chưa có
/// lệnh này. <see cref="HubFullCleared"/> = extension đã bỏ mốc "Hub hết chỗ" theo cờ <c>clearHubFull</c> (kể cả khi không bắt đầu lượt).</summary>
public sealed record SourceImageStart(bool Started, string? RoundId, string? Reason, bool Unsupported = false, bool HubFullCleared = false)
{
    public static SourceImageStart Parse(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return new SourceImageStart(false, null, "extension không trả lời lệnh ảnh gốc");
        if (e.TryGetProperty("unsupported", out var u) && u.ValueKind == JsonValueKind.True)
            return new SourceImageStart(false, null, "extension chưa hỗ trợ đẩy ảnh gốc", Unsupported: true);
        static string? S(JsonElement x, string n) =>
            x.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
        return new SourceImageStart(e.TryGetProperty("started", out var st) && st.ValueKind == JsonValueKind.True,
            S(e, "roundId"), S(e, "reason"),
            HubFullCleared: e.TryGetProperty("hubFullCleared", out var hc) && hc.ValueKind == JsonValueKind.True);
    }
}

/// <summary>Trả lời <c>sourceImagesStatus</c>: lượt đang chạy (số tới lúc hỏi) hoặc lượt gần nhất. <see cref="QueueIds"/> = số ảnh còn trong
/// hàng đợi profile "auto" (<c>profiles.auto.ids</c>; không có <c>profiles</c> thì <c>queue.ids</c>) — "còn thiếu" khi không có lượt nào.</summary>
public sealed record SourceImageStatus(bool Running, SourceImageRound? Round, int QueueIds = 0)
{
    public const string AppProfile = "auto";

    public static SourceImageStatus? Parse(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        var running = e.TryGetProperty("running", out var r) && r.ValueKind == JsonValueKind.True;
        var round = e.TryGetProperty("round", out var x) ? SourceImageRound.Parse(x) : null;
        int ids;
        if (e.TryGetProperty("profiles", out var ps) && ps.ValueKind == JsonValueKind.Object)
            ids = ps.TryGetProperty(AppProfile, out var auto) && auto.ValueKind == JsonValueKind.Object ? Ids(auto) : 0;
        else
            ids = e.TryGetProperty("queue", out var q) && q.ValueKind == JsonValueKind.Object ? Ids(q) : 0;
        return new SourceImageStatus(running, round, ids);
    }

    private static int Ids(JsonElement q) =>
        q.TryGetProperty("ids", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n > 0 ? n : 0;
}

/// <summary>Ngân sách lượt ảnh trong một khoảng nghỉ: chừa 15 s trước link kế, trần 100 s; còn dưới 10 s thì không chạy (0).</summary>
public static class HarvestImageBudget
{
    public const int ReserveMs = 15_000;
    public const int MaxMs = 100_000;
    public const int MinMs = 10_000;

    public static int For(int restMs)
    {
        var b = Math.Min((long)restMs - ReserveMs, MaxMs);
        return b < MinMs ? 0 : (int)b;
    }
}

/// <summary>Số đọc một lần của <see cref="HarvestSourceImageProgress"/> (UI hiển thị).</summary>
public sealed record HarvestSourceImageSnapshot(long Pushed, long Existed, long Failed, int Missing, string? HubFullCode, string? LastError,
    long Dropped = 0);

/// <summary>
/// GĐ9 — số liệu ảnh gốc CỘNG DỒN từ lúc mở app, gộp mọi cửa sổ Brave (mỗi profile một extension, một hàng đợi riêng): đã đẩy /
/// đã có / lỗi cộng dồn theo lượt (cùng lượt báo lại nhiều lần chỉ cộng phần chênh — không cộng hai lần); "còn thiếu" = tổng số
/// ảnh chờ MỚI NHẤT của từng profile. <see cref="HubFullCode"/> khác null khi có lượt báo Hub hết chỗ → app thôi gọi lượt ảnh tới khi
/// <see cref="ResetHubFull"/> (bấm Chạy). Thread-safe; <see cref="Changed"/> bắn ngoài khoá, có thể từ luồng nền.
/// </summary>
public sealed class HarvestSourceImageProgress
{
    private sealed class Instance
    {
        public string? RoundId;
        public int Pushed, Existed, Failed, Dropped;
        public int QueueIds;
        public int ClearedGeneration = -1;
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Instance> _instances = new(StringComparer.OrdinalIgnoreCase);
    private long _pushed, _existed, _failed, _dropped;
    private string? _hubFullCode, _lastError;
    private int _generation;

    public event Action? Changed;

    public long Pushed { get { lock (_lock) return _pushed; } }
    public long Existed { get { lock (_lock) return _existed; } }
    public long Failed { get { lock (_lock) return _failed; } }
    /// <summary>Số SP bị bỏ ảnh (lỗi riêng lặp lại / hàng đợi extension đầy).</summary>
    public long Dropped { get { lock (_lock) return _dropped; } }
    public int Missing { get { lock (_lock) return _instances.Values.Sum(i => i.QueueIds); } }
    public string? HubFullCode { get { lock (_lock) return _hubFullCode; } }
    public string? LastError { get { lock (_lock) return _lastError; } }

    public HarvestSourceImageSnapshot Snapshot()
    {
        lock (_lock)
            return new HarvestSourceImageSnapshot(_pushed, _existed, _failed, _instances.Values.Sum(i => i.QueueIds), _hubFullCode, _lastError,
                _dropped);
    }

    /// <summary>Ghi kết quả (cuối hoặc giữa chừng) của một lượt của profile <paramref name="instanceKey"/>.</summary>
    public void Apply(string instanceKey, SourceImageRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        lock (_lock)
        {
            if (!_instances.TryGetValue(instanceKey, out var inst)) _instances[instanceKey] = inst = new Instance();
            if (inst.RoundId != round.RoundId)
            {
                inst.RoundId = round.RoundId;
                inst.Pushed = inst.Existed = inst.Failed = inst.Dropped = 0;
            }
            // Cùng lượt báo lại (giữa chừng → cuối): chỉ cộng phần tăng thêm.
            _pushed += Math.Max(0, round.Pushed - inst.Pushed);
            _existed += Math.Max(0, round.Existed - inst.Existed);
            _failed += Math.Max(0, round.Failed - inst.Failed);
            _dropped += Math.Max(0, round.Dropped - inst.Dropped);
            inst.Pushed = Math.Max(inst.Pushed, round.Pushed);
            inst.Existed = Math.Max(inst.Existed, round.Existed);
            inst.Failed = Math.Max(inst.Failed, round.Failed);
            inst.Dropped = Math.Max(inst.Dropped, round.Dropped);
            inst.QueueIds = round.QueueIds;
            if (round.HubFull) _hubFullCode = round.HubFullCode ?? "hub-full";
            if (round.LatestError is { } err) _lastError = err;
        }
        Raise();
    }

    /// <summary>Số ảnh còn chờ của profile, hỏi riêng khi không có lượt nào (nghỉ quá ngắn, hết khối).</summary>
    public void SetMissing(string instanceKey, int queueIds)
    {
        lock (_lock)
        {
            if (!_instances.TryGetValue(instanceKey, out var inst)) _instances[instanceKey] = inst = new Instance();
            inst.QueueIds = Math.Max(0, queueIds);
        }
        Raise();
    }

    /// <summary>Lỗi không thuộc lượt nào (vd extension từ chối bắt đầu lượt) — chỉ cập nhật "lỗi gần nhất".</summary>
    public void NoteError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        lock (_lock) _lastError = message;
        Raise();
    }

    /// <summary>Bấm Chạy: bỏ cờ Hub hết chỗ (người dùng có thể vừa dọn kho). Lượt đầu tiên của mỗi profile sau đó cũng bảo extension bỏ
    /// mốc tạm dừng 30 phút của nó (<see cref="HubFullClearPending"/>).</summary>
    public void ResetHubFull()
    {
        lock (_lock)
        {
            _hubFullCode = null;
            _generation++;
        }
        Raise();
    }

    /// <summary>Profile còn phải gửi <c>clearHubFull</c> cho extension (chưa gửi được sau lần <see cref="ResetHubFull"/> gần nhất) → thế hệ
    /// cần đánh dấu; null = không cần. Chỉ coi là đã gửi bằng <see cref="MarkHubFullCleared"/> khi extension ĐÃ nhận — RPC hỏng thì lần
    /// sau gửi lại.</summary>
    public int? HubFullClearPending(string instanceKey)
    {
        lock (_lock)
        {
            var cleared = _instances.TryGetValue(instanceKey, out var inst) ? inst.ClearedGeneration : -1;
            return cleared >= _generation ? null : _generation;
        }
    }

    /// <summary>Extension của profile đã bỏ mốc Hub hết chỗ ứng với thế hệ <paramref name="generation"/>.</summary>
    public void MarkHubFullCleared(string instanceKey, int generation)
    {
        lock (_lock)
        {
            if (!_instances.TryGetValue(instanceKey, out var inst)) _instances[instanceKey] = inst = new Instance();
            inst.ClearedGeneration = Math.Max(inst.ClearedGeneration, generation);
        }
    }

    private void Raise()
    {
        try { Changed?.Invoke(); } catch { }
    }
}
