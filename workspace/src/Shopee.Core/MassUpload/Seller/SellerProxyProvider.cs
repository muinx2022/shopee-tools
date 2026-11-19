namespace Shopee.Core.MassUpload.Seller;

/// <summary>Proxy của một tài khoản sau khi gộp cấu hình chung + riêng. <see cref="Static"/> có giá trị khi
/// <see cref="Kind"/> = Static.</summary>
public sealed record SellerProxySource(SellerProxyKind Kind, string? KiotKey, string Region, ProxyScheme Scheme,
    bool RotatePerShop, ProxyLease? Static, bool IsShared)
{
    public override string ToString() => Kind == SellerProxyKind.Static
        ? $"proxy tĩnh {Static}"
        : $"Kiot …{SellerAccountStore.Tail(KiotKey ?? "")} ({Region}{(IsShared ? ", key chung" : ", key riêng")})";

    /// <summary>Gộp cấu hình: không có proxy riêng → proxy chung: có key Kiot chung thì Kiot (đổi IP mỗi shop); KHÔNG có key mà có
    /// proxy tĩnh chung thì proxy tĩnh đó (GĐ5d — các shop cùng một IP, user tự chọn). Thiếu/sai → <c>error</c>, KHÔNG có nhánh
    /// "không proxy".</summary>
    public static SellerProxySource? Resolve(SellerAccount account, SellerUploadSettings settings, out string? error)
    {
        error = null;
        var ov = account.ProxyOverride;
        if (ov is null)
        {
            var shared = settings.SharedProxy ?? new KiotProxySettings();
            if (!string.IsNullOrWhiteSpace(shared.KiotKey))
                return new SellerProxySource(SellerProxyKind.Kiot, shared.KiotKey.Trim(), shared.Region, shared.Type,
                    RotatePerShop: true, Static: null, IsShared: true);
            if (string.IsNullOrWhiteSpace(shared.Static))
            { error = "Chưa cấu hình key Kiot chung hay proxy tĩnh chung — chưa có proxy chung."; return null; }
            if (!StaticProxyParser.TryParse(shared.Static, shared.Type, out var sharedLease, out var sharedErr))
            { error = "Proxy tĩnh chung sai: " + sharedErr; return null; }
            return new SellerProxySource(SellerProxyKind.Static, null, "", sharedLease!.Scheme, RotatePerShop: false, Static: sharedLease,
                IsShared: true);
        }
        if (ov.Kind == SellerProxyKind.Kiot)
        {
            if (string.IsNullOrWhiteSpace(ov.KiotKey)) { error = "Proxy riêng Kiot thiếu key."; return null; }
            return new SellerProxySource(SellerProxyKind.Kiot, ov.KiotKey.Trim(), ov.Region, ov.Type,
                RotatePerShop: !ov.KiotKeepIp, Static: null, IsShared: false);
        }
        if (!StaticProxyParser.TryParse(ov.Static, ov.Type, out var lease, out var err)) { error = "Proxy tĩnh sai: " + err; return null; }
        return new SellerProxySource(SellerProxyKind.Static, null, "", lease!.Scheme, RotatePerShop: false, Static: lease, IsShared: false);
    }
}

/// <summary>Kết quả xin proxy. <see cref="Lease"/> null = lỗi; <see cref="IsKeyDead"/> = key Kiot chết (với key chung: dừng cả lượt).</summary>
public sealed record ProxyAcquireResult(ProxyLease? Lease, string? Error = null, bool IsKeyDead = false)
{
    public bool Ok => Lease is not null;
}

/// <summary>Một nhịp đếm ngược khi chờ tới mốc được đổi IP của key Kiot.</summary>
public sealed record KiotWaitTick(string KeyTail, TimeSpan Remaining, DateTimeOffset Until);

public sealed class SellerProxyProviderOptions
{
    /// <summary>Số lần gọi <c>/new</c> tối đa cho một shop (gồm cả lần bị Kiot bắt chờ).</summary>
    public int MaxNewAttempts { get; init; } = 4;

    /// <summary>Nghỉ trước khi gọi lại sau lỗi tạm (mạng, Kiot nghẽn).</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Đệm thêm sau mốc <c>nextRequestAt</c> cho đồng hồ máy chủ Kiot kịp qua mốc.</summary>
    public TimeSpan WaitBuffer { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Chờ dài hơn mức này thì coi là lỗi (gói đổi IP chỉ bắt chờ ≤ 5 phút).</summary>
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Nhịp báo đếm ngược.</summary>
    public TimeSpan Tick { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Cấp proxy cho MỘT shop sắp chạy (plan GĐ4 bước 3).
/// <list type="bullet">
/// <item><b>Kiot đổi IP</b> (key chung, hoặc key riêng không giữ IP): gọi <c>/new</c>. Chưa tới <c>nextRequestAt</c> thì CHỜ tới mốc
/// (báo <see cref="Waiting"/> từng nhịp, huỷ được) rồi mới gọi. Không biết mốc (lần đầu của key trong phiên) thì hỏi
/// <c>/current</c> trước — <c>/current</c> không đổi IP. Kiot vẫn bắt chờ ("sau X giây") thì chờ đúng chừng đó rồi gọi lại.</item>
/// <item><b>Kiot giữ IP</b> (key riêng, <see cref="SellerProxyOverride.KiotKeepIp"/>): <c>/current</c>; key chưa có IP thì <c>/new</c>.</item>
/// <item><b>Tĩnh</b>: trả nguyên.</item>
/// </list>
/// Lỗi thì trả lỗi — KHÔNG BAO GIỜ trả IP cũ thay IP mới (khác <c>ProxyPool</c>), không có nhánh "không proxy".
/// <para>Chỉ được gọi TRƯỚC khi mở Brave của shop (<see cref="SellerRunGate"/>); không có API nào đổi IP giữa lúc đang làm shop.
/// Không thread-safe theo key: luồng chạy tuần tự từng shop.</para>
/// </summary>
public sealed class SellerProxyProvider
{
    private readonly SellerKiotApi _kiot;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly SellerProxyProviderOptions _opt;
    private readonly Action<string>? _log;

    /// <summary>Mốc được đổi IP tiếp theo của từng key (đã biết trong phiên), theo GIỜ MÁY CHỦ KIOT.</summary>
    private readonly Dictionary<string, DateTimeOffset> _nextChange = new(StringComparer.Ordinal);

    /// <summary>Độ lệch đồng hồ máy chủ Kiot − giờ máy, theo từng key.</summary>
    private readonly Dictionary<string, TimeSpan> _offset = new(StringComparer.Ordinal);

    /// <summary>Đếm ngược khi chờ mốc đổi IP. Gọi trên luồng đang chờ.</summary>
    public event Action<KiotWaitTick>? Waiting;

    public SellerProxyProvider(SellerKiotApi kiot, SellerProxyProviderOptions? options = null, Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Action<string>? log = null)
    {
        _kiot = kiot;
        _opt = options ?? new SellerProxyProviderOptions();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
        _log = log;
    }

    /// <summary>Nạp mốc đổi IP (giờ máy chủ Kiot) đã biết từ lần chạy trước (GĐ5 lưu sổ) — tránh gọi <c>/new</c> sớm ngay sau khi mở lại app.</summary>
    public void Seed(string key, DateTimeOffset nextChangeAt) => _nextChange[key.Trim()] = nextChangeAt;

    /// <summary>Mốc đổi IP đã biết của key (null = chưa biết).</summary>
    public DateTimeOffset? NextChangeAt(string key) => _nextChange.TryGetValue(key.Trim(), out var t) ? t : null;

    /// <summary>Xin proxy cho một shop. Huỷ → <see cref="OperationCanceledException"/>.</summary>
    public async Task<ProxyAcquireResult> AcquireAsync(SellerProxySource source, CancellationToken ct)
    {
        if (source.Kind == SellerProxyKind.Static)
            return source.Static is null ? new(null, "Thiếu proxy tĩnh.") : new(source.Static);

        var key = source.KiotKey?.Trim();
        if (string.IsNullOrWhiteSpace(key)) return new(null, "Thiếu key Kiot.");

        if (!source.RotatePerShop)
        {
            var cur = await _kiot.CurrentAsync(key, ct).ConfigureAwait(false);
            Track(key, cur);
            if (cur.Ok) { Remember(key, cur.Data!); return ToLease(source, key, cur.Data!, fresh: false); }
            if (cur.IsKeyDead) return new(null, $"Key Kiot …{Tail(key)} chết: {cur.Describe()}", IsKeyDead: true);
            _log?.Invoke($"Kiot …{Tail(key)} chưa có IP ({cur.Describe()}) → xin IP mới.");
        }
        else if (!_nextChange.ContainsKey(key))
        {
            // Lần đầu của key trong phiên: hỏi mốc qua /current (không đổi IP) để khỏi gọi /new sớm.
            var cur = await _kiot.CurrentAsync(key, ct).ConfigureAwait(false);
            Track(key, cur);
            if (cur.Ok) Remember(key, cur.Data!);
            else if (cur.IsKeyDead) return new(null, $"Key Kiot …{Tail(key)} chết: {cur.Describe()}", IsKeyDead: true);
        }

        string? last = null;
        for (var attempt = 1; attempt <= _opt.MaxNewAttempts; attempt++)
        {
            if (_nextChange.TryGetValue(key, out var until))
            {
                var wait = until + _opt.WaitBuffer - ServerNow(key);
                if (wait > _opt.MaxWait)
                    return new(null, $"Kiot …{Tail(key)} bắt chờ {wait.TotalMinutes:0} phút mới được đổi IP — quá lâu.");
                if (wait > TimeSpan.Zero) await WaitUntilAsync(key, until + _opt.WaitBuffer, ct).ConfigureAwait(false);
            }

            var r = await _kiot.NewAsync(key, source.Region, ct).ConfigureAwait(false);
            Track(key, r);
            if (r.Ok)
            {
                Remember(key, r.Data!);
                return ToLease(source, key, r.Data!, fresh: source.RotatePerShop);
            }
            if (r.IsKeyDead) return new(null, $"Key Kiot …{Tail(key)} chết: {r.Describe()}", IsKeyDead: true);

            last = r.Describe();
            if (r.WaitSeconds is > 0)
            {
                _log?.Invoke($"Kiot …{Tail(key)} bắt chờ {r.WaitSeconds}s mới được đổi IP.");
                _nextChange[key] = ServerNow(key) + TimeSpan.FromSeconds(r.WaitSeconds.Value);
                continue;
            }
            _log?.Invoke($"Kiot …{Tail(key)} lỗi lần {attempt}/{_opt.MaxNewAttempts}: {last}");
            if (attempt < _opt.MaxNewAttempts) await _delay(_opt.RetryDelay, ct).ConfigureAwait(false);
        }
        return new(null, $"Không lấy được IP mới từ Kiot …{Tail(key)}: {last}");
    }

    /// <summary>Giờ hiện tại THEO ĐỒNG HỒ KIOT (giờ máy + độ lệch đo được). <c>nextRequestAt</c> là giờ của máy chủ Kiot — so với
    /// giờ máy lệch 10 phút là gọi <c>/new</c> sớm (bị từ chối) hoặc chờ thừa (quá <see cref="SellerProxyProviderOptions.MaxWait"/>).</summary>
    private DateTimeOffset ServerNow(string key) => _clock() + (_offset.TryGetValue(key, out var o) ? o : TimeSpan.Zero);

    /// <summary>Cập nhật độ lệch đồng hồ theo header <c>Date</c> của phản hồi; không có header thì giữ độ lệch cũ (mặc định 0).</summary>
    private void Track(string key, KiotCallResult r)
    {
        if (r.ServerTime is { } t) _offset[key] = t - _clock();
    }

    private void Remember(string key, KiotProxyData d)
    {
        if (d.NextRequestAtMs > 0) _nextChange[key] = DateTimeOffset.FromUnixTimeMilliseconds(d.NextRequestAtMs);
    }

    private static ProxyAcquireResult ToLease(SellerProxySource source, string key, KiotProxyData d, bool fresh)
    {
        var ep = d.Endpoint(source.Scheme);
        if (ep is null) return new(null, $"Kiot …{Tail(key)} không trả địa chỉ proxy {source.Scheme}.");
        // Cùng luật host/cổng với proxy tĩnh (GĐ5d vòng 2): Kiot trả host lạ thì từ chối, không đưa cho Brave.
        if (!ProxyHostRules.IsValidHost(ep.Value.Host) || !ProxyHostRules.IsValidPort(ep.Value.Port))
            return new(null, $"Kiot …{Tail(key)} trả địa chỉ proxy {source.Scheme} không hợp lệ (host/cổng có ký tự lạ) — bỏ.");
        return new(new ProxyLease(source.Scheme, ep.Value.Host, ep.Value.Port, ExpectedIp: d.RealIp)
        {
            Source = ProxyLeaseSource.Kiot,
            MustBeUnusedIp = fresh,
            KiotKeyTail = Tail(key),
        });
    }

    /// <summary>Chờ tới <paramref name="until"/> theo từng nhịp <see cref="SellerProxyProviderOptions.Tick"/>, báo đếm ngược.</summary>
    private async Task WaitUntilAsync(string key, DateTimeOffset until, CancellationToken ct)
    {
        var tail = Tail(key);
        _log?.Invoke($"Kiot …{tail}: chờ {(until - ServerNow(key)).TotalSeconds:0}s mới được đổi IP.");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var remaining = until - ServerNow(key);
            if (remaining <= TimeSpan.Zero) return;
            Waiting?.Invoke(new KiotWaitTick(tail, remaining, _clock() + remaining));
            await _delay(remaining < _opt.Tick ? remaining : _opt.Tick, ct).ConfigureAwait(false);
        }
    }

    private static string Tail(string s) => SellerAccountStore.Tail(s);
}
