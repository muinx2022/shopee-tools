namespace Shopee.Core.Harvest;

/// <summary>Việc engine phải làm với MỘT kết quả bước Harvest (quyết định của <see cref="HarvestStepPolicy"/>).</summary>
public enum HarvestStepAction
{
    /// <summary>Bước bị bước mới thay (<c>superseded</c>) → bỏ kết quả: KHÔNG tính dòng xong, KHÔNG ghi lỗi dòng;
    /// engine làm lại đúng dòng này.</summary>
    Superseded,
    /// <summary>Người dùng dừng (<c>aborted</c>, không phải bị thay) → dừng vòng như bước scrape bị huỷ.</summary>
    Abort,
    /// <summary>Tài khoản Shopee mất đăng nhập (<c>loginRequired</c> — trang bị đẩy sang /buyer/login…) → dừng khối, dòng KHÔNG
    /// tính lỗi (làm lại bằng tài khoản khác), tài khoản đi đường lỗi tạm (nghỉ). Không bao giờ là lỗi vĩnh viễn của SP.</summary>
    LoginRequired,
    /// <summary>Shopee chặn traffic / chặn get_pc (<c>trafficError</c>, kể cả <c>blocked</c>) → đường traffic của
    /// scrape: lần đầu đổi IP, lần sau đổi tài khoản.</summary>
    Traffic,
    /// <summary>Lỗi proxy → tạm dừng instance như scrape.</summary>
    ProxyError,
    /// <summary>Captcha chưa giải → đường captcha của scrape (chờ giải tay rồi làm lại dòng / dừng).</summary>
    Captcha,
    /// <summary>Có JSON khớp → giao <see cref="IHarvestSink"/>; chỉ khi sink xong mới tính dòng.</summary>
    Save,
    /// <summary>Lỗi dòng có <c>pdpError</c> (mã chưa coi là chặn), chưa tới ngưỡng cầu dao → lỗi dòng (ghi HOÃN:
    /// nếu cầu dao bật thì các dòng này được làm lại thay vì vào sổ bỏ qua).</summary>
    PdpError,
    /// <summary>Đủ N dòng LIÊN TIẾP có CÙNG mã <c>pdpError</c> → coi như Shopee chặn (đường <see cref="Traffic"/>).</summary>
    PdpBreakerTrip,
    /// <summary>Lỗi dòng thường (notProduct, hết giờ, thiếu JSON, lỗi RPC, pdpError của mã đã bật cầu dao…) → ghi
    /// sổ bỏ qua, sang dòng kế.</summary>
    RowFailed,
}

/// <summary>Quyết định + trạng thái cầu dao SAU quyết định (để log).</summary>
public readonly record struct HarvestStepDecision(HarvestStepAction Action, string? PdpCode, int PdpStreak);

/// <summary>
/// Cầu dao pdpError: đếm số dòng LIÊN TIẾP có CÙNG mã lỗi get_pc. Mã khác → đếm lại từ 1 với mã mới. Đủ ngưỡng → bật
/// (xử như traffic: đổi IP / tài khoản) và đưa mã vào trạng thái CHỜ XÁC NHẬN:
/// <list type="bullet">
/// <item>Sau khi đổi IP, mã ấy TÁI DIỄN trước khi có dòng nào lưu được → mã bị chặn không phải do IP mà do SP chết
/// → NHỚ mã (<see cref="ConfirmedDeadCodes"/>): từ đó dòng lỗi đúng mã là lỗi dòng thường, không bật lại — một cụm SP
/// chết cùng mã không đốt IP/tài khoản liên tục.</item>
/// <item>Có dòng lưu được trước khi mã tái diễn → mã là chặn theo IP (IP mới chạy được) → bỏ khỏi trạng thái chờ VÀ
/// khỏi tập đã nhớ: mã lại được đếm và bật bình thường.</item>
/// </list>
/// Sống theo PHIÊN (một khối dòng, qua các lần đổi IP); <see cref="ResetStreak"/> mỗi lần vòng dòng chạy lại, còn hai
/// tập mã thì giữ.
/// </summary>
public sealed class HarvestPdpBreaker
{
    private readonly HashSet<string> _awaitingConfirm = new(StringComparer.Ordinal);
    private readonly HashSet<string> _confirmedDead = new(StringComparer.Ordinal);

    public HarvestPdpBreaker(int threshold = HarvestStepPolicy.DefaultPdpErrorBreakerThreshold) =>
        Threshold = threshold > 0 ? threshold : HarvestStepPolicy.DefaultPdpErrorBreakerThreshold;

    /// <summary>Ngưỡng thực dùng (≤0 lúc dựng → mặc định).</summary>
    public int Threshold { get; }
    public string? CurrentCode { get; private set; }
    public int Streak { get; private set; }
    /// <summary>Mã đã bật cầu dao, chờ xem sau khi đổi IP có tái diễn không.</summary>
    public IReadOnlyCollection<string> AwaitingConfirmCodes => _awaitingConfirm;
    /// <summary>Mã đã xác nhận là SP chết (tái diễn ngay sau khi đổi IP) → lỗi dòng thường.</summary>
    public IReadOnlyCollection<string> ConfirmedDeadCodes => _confirmedDead;

    public void ResetStreak()
    {
        CurrentCode = null;
        Streak = 0;
    }

    /// <summary>Một dòng lưu được: cắt chuỗi đếm; mã đang chờ xác nhận chưa tái diễn → là chặn theo IP, quên nó. Mã ĐÃ xác nhận
    /// là "SP chết" thì GIỮ tới hết phiên (quên đi thì mỗi cụm SP chết lại bật cầu dao → đổi IP rồi đổi tài khoản vô ích); ghi
    /// nhầm vĩnh viễn đã có luật "thấy ở ≥ 2 lượt" của tập lỗi chặn.</summary>
    internal void RecordSave()
    {
        ResetStreak();
        foreach (var code in _awaitingConfirm)
            _confirmedDead.Remove(code);
        _awaitingConfirm.Clear();
    }

    /// <summary>Dòng lỗi mã <paramref name="code"/> đã thuộc diện "SP chết": đã nhớ, hoặc đang chờ xác nhận và nay tái
    /// diễn (chưa có dòng nào lưu được kể từ khi bật) → nhớ luôn. Trả true = lỗi dòng thường.</summary>
    internal bool IsDeadCode(string code)
    {
        if (_awaitingConfirm.Remove(code))
            _confirmedDead.Add(code);
        return _confirmedDead.Contains(code);
    }

    /// <summary>Ghi một dòng pdpError chưa bật; trả true nếu dòng này làm cầu dao bật (mã vào trạng thái chờ xác nhận).</summary>
    internal bool Record(string code)
    {
        if (string.Equals(code, CurrentCode, StringComparison.Ordinal)) Streak++;
        else { CurrentCode = code; Streak = 1; }
        if (Streak < Threshold) return false;
        _awaitingConfirm.Add(code);
        ResetStreak();
        return true;
    }
}

/// <summary>
/// Bảng "kết quả bước → hành động" của chế độ Harvest — hàm THUẦN trừ cầu dao truyền vào (engine cần Brave/CDP thật
/// nên tách ra để test). Thứ tự kiểm theo đúng bước scrape (<c>LauncherRunnerLoop</c>: aborted → traffic → proxy →
/// captcha → ok/lỗi), thêm <c>superseded</c> lên đầu (extension trả superseded kèm aborted=true).
/// </summary>
public static class HarvestStepPolicy
{
    /// <summary>Ngưỡng mặc định của cầu dao pdpError.</summary>
    public const int DefaultPdpErrorBreakerThreshold = 3;

    /// <summary>Số dòng <c>notProduct</c> LIÊN TIẾP trong một khối thì coi là lỗi phiên / tài khoản (vd bị đẩy về trang chủ, trang
    /// lạ) chứ không phải N SP hỏng: dừng khối, cả chuỗi KHÔNG vào sổ lỗi / tập lỗi vĩnh viễn (làm lại bằng tài khoản khác).</summary>
    public const int NotProductBreakerThreshold = 3;

    private static readonly System.Text.RegularExpressions.Regex ClassifyWords =
        new("captcha|verify|proxy", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Làm sạch chuỗi tự do (tên tài khoản, <c>ex.Message</c>…) trước khi ghép vào thông điệp dừng đi qua
    /// <c>ScrapeRunner.Classify</c>: bộ phân loại đọc chữ "captcha" / "verify" / "proxy" ra cách ly / lỗi proxy.</summary>
    public static string SafeForClassify(string? text) => ClassifyWords.Replace(text ?? "", "…");

    /// <summary>Chuỗi notProduct ĐÃ được link mẫu chứng minh phiên ổn: các dòng chết tiếp theo được GIỮ CHỜ, cứ đủ chừng này dòng thì
    /// kiểm chứng lại — mẫu sống thì xả cả nhóm vào tập lỗi, mẫu hỏng / captcha thì nhóm đó vào sổ bỏ qua (không vào tập lỗi). Phiên
    /// hỏng giữa chuỗi mà Shopee không đẩy về trang đăng nhập không làm SP sống phía sau bị ghi lỗi mà chưa kiểm lần nào.</summary>
    public const int ProvenChainRecheckEvery = 10;

    /// <summary>Kết quả bước của link mẫu có chứng minh phiên / tài khoản đang lấy được dữ liệu không.</summary>
    public static bool SampleProvesSession(HarvestStepResult result) =>
        result.HarvestOk && !result.LoginRequired && !string.IsNullOrWhiteSpace(result.Json) && result.ItemId is > 0;

    /// <summary>Dòng lỗi (<see cref="HarvestStepAction.RowFailed"/>) có phải lỗi VĨNH VIỄN của SP (lượt sau không lấy lại) không:
    /// <c>notProduct</c> (trang không phải SP / SP đã xoá) hoặc mã pdpError đã được cầu dao xác nhận là SP chết (tái diễn ngay
    /// sau khi đổi IP). Mọi lỗi khác (hết giờ, thiếu JSON, lỗi RPC, pdpError chưa xác nhận, captcha / traffic / proxy) là TẠM —
    /// không chắc thì coi là tạm. Lỗi "JSON bị từ chối vì nội dung" (<see cref="HarvestItemRejectedException"/>) xét riêng ở
    /// chỗ lưu (luôn vĩnh viễn).</summary>
    public static bool IsPermanentRowFailure(HarvestStepAction action, HarvestStepResult result) =>
        action == HarvestStepAction.RowFailed && !result.HarvestOk
        && (result.NotProduct || !string.IsNullOrWhiteSpace(result.PdpError));

    /// <param name="result">Kết quả bước.</param>
    /// <param name="breaker">Cầu dao của phiên — được cập nhật theo kết quả này.</param>
    public static HarvestStepDecision Decide(HarvestStepResult result, HarvestPdpBreaker breaker)
    {
        var action = DecideAction(result, breaker);
        return new(action, breaker.CurrentCode, breaker.Streak);
    }

    private static HarvestStepAction DecideAction(HarvestStepResult result, HarvestPdpBreaker breaker)
    {
        // superseded / abort / proxy / captcha: dòng chưa có kết luận (làm lại hoặc dừng) → giữ nguyên chuỗi.
        if (result.Superseded) return HarvestStepAction.Superseded;
        if (result.Aborted) return HarvestStepAction.Abort;
        if (result.LoginRequired && !result.HarvestOk) { breaker.ResetStreak(); return HarvestStepAction.LoginRequired; }
        if (result.TrafficError) { breaker.ResetStreak(); return HarvestStepAction.Traffic; }
        if (result.ProxyError) return HarvestStepAction.ProxyError;
        if (result.Captcha && !result.HarvestOk) return HarvestStepAction.Captcha;

        // ok mà thiếu JSON / itemId là lỗi dòng (không bao giờ giao sink một bản ghi rỗng).
        if (result.HarvestOk && !string.IsNullOrWhiteSpace(result.Json) && result.ItemId is > 0)
        {
            breaker.RecordSave();
            return HarvestStepAction.Save;
        }

        if (!result.HarvestOk && !string.IsNullOrWhiteSpace(result.PdpError))
        {
            var code = result.PdpError.Trim();
            if (breaker.IsDeadCode(code))
            {
                // Mã này đã bật cầu dao (đã đổi IP) và tái diễn trước khi có dòng lưu được → SP chết thật, không đốt
                // IP/tài khoản nữa.
                breaker.ResetStreak();
                return HarvestStepAction.RowFailed;
            }
            return breaker.Record(code) ? HarvestStepAction.PdpBreakerTrip : HarvestStepAction.PdpError;
        }

        // Lỗi dòng khác cắt chuỗi "liên tiếp" của cầu dao.
        breaker.ResetStreak();
        return HarvestStepAction.RowFailed;
    }
}

/// <summary>
/// Theo dõi tỉ lệ <c>source = "hook-reload"</c> trong cửa sổ trượt các dòng lấy được gần nhất (chỉ để cảnh báo:
/// tỉ lệ cao = trang hay không tự gọi get_pc lần đầu, mỗi dòng tốn thêm một lần tải lại).
/// </summary>
public sealed class HarvestSourceMonitor
{
    private readonly int _window;
    private readonly double _threshold;
    private readonly Queue<bool> _recent = new();
    private int _sinceWarn;

    public HarvestSourceMonitor(int window = 20, double threshold = 0.3)
    {
        _window = Math.Max(1, window);
        _threshold = threshold;
        _sinceWarn = _window;   // được cảnh báo ngay khi cửa sổ đầy lần đầu
    }

    /// <summary>Ghi nhận một dòng lấy được. Trả tỉ lệ hook-reload khi CẦN cảnh báo (cửa sổ đã đầy, tỉ lệ vượt
    /// ngưỡng, và đã qua ít nhất một cửa sổ kể từ lần cảnh báo trước — không spam mỗi dòng); null nếu không.</summary>
    public double? Record(string? source)
    {
        _recent.Enqueue(string.Equals(source, "hook-reload", StringComparison.OrdinalIgnoreCase));
        while (_recent.Count > _window) _recent.Dequeue();
        _sinceWarn++;
        if (_recent.Count < _window) return null;

        var ratio = _recent.Count(x => x) / (double)_recent.Count;
        if (ratio <= _threshold || _sinceWarn < _window) return null;
        _sinceWarn = 0;
        return ratio;
    }
}
