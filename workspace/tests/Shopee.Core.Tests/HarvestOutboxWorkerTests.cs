using System.Text.Json;
using System.Net;
using System.Net.Http.Json;
using Shopee.Core.Coordination;
using Shopee.Core.Harvest;
using static Shopee.Core.Tests.HarvestTestData;

namespace Shopee.Core.Tests;

/// <summary>
/// GĐ4: <see cref="HarvestOutboxWorker"/> với Hub GIẢ (HttpMessageHandler) — từng nhánh "Quy tắc client" của plan:
/// ok/applied, ok=false, lùi dần CHUNG cho mọi phản hồi khác 200 (kể cả 500, không trần), lô 500 → mục nghi vấn gửi lẻ
/// xen kẽ với lô thường (đếm 500 chỉ khi có bằng chứng), 413 / batch-too-large giảm cỡ lô, 400 khác, mất mạng / timeout,
/// clientNow đặt lúc gửi, Flush, Nudge.
/// </summary>
public sealed class HarvestOutboxWorkerTests : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly string _dir = NewTempDir();
    private readonly HarvestOutbox _box;
    private readonly FakeClock _clock = new(T0);
    private readonly FakeHub _hub = new();
    private readonly HubClient _client;
    private readonly List<string> _log = [];

    public HarvestOutboxWorkerTests()
    {
        _box = new HarvestOutbox(Path.Combine(_dir, "outbox.db"));
        _client = new HubClient(new HubClientConfig { Enabled = true, BaseUrl = "http://hub.test", ApiToken = "t" }, "may-1", _hub);
    }

    public void Dispose()
    {
        _client.Dispose();
        TryDelete(_dir);
    }

    private HarvestOutboxWorker Worker(HarvestOutboxWorkerOptions? opt = null, Func<double>? random = null) =>
        new(_box, () => _client, s => { lock (_log) _log.Add(s); }, opt, () => _clock.Now, random ?? (() => 0.5));   // 0.5 = không nhiễu

    /// <summary>Lưu <paramref name="n"/> mục, mỗi mục một fixture khác (itemId khác nhau), dòng 1..n.</summary>
    private async Task<List<long>> Seed(int n)
    {
        var ids = new List<long>();
        for (var i = 0; i < n; i++)
        {
            var item = Item(Fixtures[i], i + 1);
            await _box.SaveAsync(item, CancellationToken.None);
            ids.Add(item.ItemId);
        }
        return ids;
    }

    private static HttpResponseMessage Ok(HarvestedProductBatchRequest req, Func<HarvestedProductItem, int, HarvestedProductResult>? per = null) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new HarvestedProductBatchResponse(
                req.Items!.Select((it, i) => per?.Invoke(it, i) ?? new HarvestedProductResult(i, it.ItemId, true, true, null)).ToList()), options: Web),
        };

    private static HttpResponseMessage Status(HttpStatusCode code, string? error = null) =>
        new(code) { Content = error is null ? new StringContent("") : JsonContent.Create(new { error }, options: Web) };

    private (string Status, int Attempts, string? LastError, long NextTryAtMs, long Version) Row(int row) =>
        _box.Inspect("acc-1", "Shop1", row)!.Value;

    // ── 200 ────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ok_XoaMuc_DemApplied()
    {
        await Seed(3);
        _hub.Respond = (req, _) => Ok(req, (it, i) => new HarvestedProductResult(i, it.ItemId, true, i != 1, null));
        var w = Worker();

        Assert.Equal(HarvestPushOutcome.Sent, await w.PushOnceAsync(false, CancellationToken.None));

        Assert.Equal(new HarvestOutboxStats(0, 0, 3, 2), _box.GetStats());
        Assert.Equal((3, 2), (w.SessionPushed, w.SessionApplied));
        var req = Assert.Single(_hub.Requests);
        Assert.Equal(3, req.Items!.Count);
        Assert.Equal(3, req.Items.Select(x => x.RawJson).Distinct().Count());
        Assert.All(req.Items, x => Assert.NotNull(x.RawHash));
        Assert.Equal(HarvestPushOutcome.Idle, await w.PushOnceAsync(false, CancellationToken.None));
    }

    [Fact]
    public async Task OkFalse_DanhLoi_KhongTuGuiLai_RequeueThiGui()
    {
        var ids = await Seed(2);
        _hub.Respond = (req, _) => Ok(req, (it, i) => it.ItemId == ids[1]
            ? new HarvestedProductResult(i, it.ItemId, false, false, "db 22P05: x")
            : new HarvestedProductResult(i, it.ItemId, true, true, null));
        var w = Worker();

        await w.PushOnceAsync(false, CancellationToken.None);

        Assert.Equal(new HarvestOutboxStats(0, 1, 1, 1), _box.GetStats());
        Assert.Equal("failed", Row(2).Status);
        Assert.Contains("22P05", Row(2).LastError);
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(HarvestPushOutcome.Idle, await w.PushOnceAsync(false, CancellationToken.None));
        Assert.Single(_hub.Requests);

        Assert.Equal(1, await w.RequeueFailedAsync());
        _hub.Respond = (req, _) => Ok(req);
        Assert.Equal(HarvestPushOutcome.Sent, await w.PushOnceAsync(false, CancellationToken.None));
        Assert.Equal([ids[1]], _hub.Requests[1].Items!.Select(x => x.ItemId));
        Assert.Equal(new HarvestOutboxStats(0, 0, 2, 2), _box.GetStats());
    }

    [Fact]
    public async Task Ok_KetQuaThieuHoacLechItemId_GiuMuc()
    {
        var ids = await Seed(3);
        _hub.Respond = (req, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new HarvestedProductBatchResponse(
            [
                new(0, ids[0], true, true, null),
                new(1, 999, true, true, null),   // lệch itemId
                // thiếu mục 2
            ]), options: Web),
        };
        var w = Worker();

        await w.PushOnceAsync(false, CancellationToken.None);

        Assert.Equal(new HarvestOutboxStats(2, 0, 1, 1), _box.GetStats());
        Assert.Equal("pending", Row(2).Status);
        Assert.True(Row(2).NextTryAtMs > _clock.Now.ToUnixTimeMilliseconds());
    }

    /// <summary>Bản mới lưu đè đúng lúc lô đang bay → Hub ok cho bản cũ không xoá bản mới.</summary>
    [Fact]
    public async Task Ok_BanMoiLuuDeTrongLucBay_VanConCho()
    {
        await Seed(1);
        _hub.OnSend = () => _box.SaveAsync(Item(Fixtures[0], 1, at: T0.AddMinutes(5)), CancellationToken.None);
        var w = Worker();

        await w.PushOnceAsync(false, CancellationToken.None);

        Assert.Equal(new HarvestOutboxStats(1, 0, 1, 1), _box.GetStats());
    }

    // ── 500: lùi dần chung + mục nghi vấn gửi lẻ xen kẽ với lô thường ───────────────────────────────────────────────

    /// <summary>Lưu <paramref name="n"/> SP tổng hợp (itemId 1000+i), dòng 1..n.</summary>
    private async Task<List<long>> SeedSynthetic(int n)
    {
        var ids = new List<long>();
        for (var i = 1; i <= n; i++)
        {
            var id = 1000 + i;
            await _box.SaveAsync(Item("", i, json: SyntheticJson(id)), CancellationToken.None);
            ids.Add(id);
        }
        return ids;
    }

    /// <summary>Mô phỏng vòng nền trong <paramref name="duration"/>: đẩy liên tục khi Sent/Split/Failed, còn lại ngủ một
    /// nhịp (10 s) như <c>LoopAsync</c>. Dừng sớm khi hàng chờ rỗng.</summary>
    private async Task Simulate(HarvestOutboxWorker w, TimeSpan duration)
    {
        var end = _clock.Now + duration;
        while (_clock.Now < end)
        {
            var o = await w.PushOnceAsync(false, CancellationToken.None);
            if (o is HarvestPushOutcome.Sent or HarvestPushOutcome.Split or HarvestPushOutcome.Failed) continue;
            if (_box.GetStats().Pending == 0) return;
            _clock.Advance(TimeSpan.FromSeconds(10));
        }
    }

    private static bool Has(HarvestedProductBatchRequest r, long id) => r.Items!.Any(x => x.ItemId == id);

    // ── Các ca phản biện vòng 2 đã tái hiện trên thiết kế ghim/chia đôi cũ (phải xanh theo đúng nghĩa) ───────────────

    /// <summary>1 mục độc + Hub chớp tắt 5 phút (mọi lô 500): không kẹt — 11 mục lành vào hết, mục độc failed (đủ 5 lần
    /// 500 có bằng chứng nhờ các lô lành xen kẽ) hoặc còn chờ, không mục lành nào failed.</summary>
    [Fact]
    public async Task PB_MotMucDoc_HubChopTat_KhongKet_MucLanhVaoHet()
    {
        var ids = await SeedSynthetic(12);
        var poison = ids[2];
        var hubBackAt = T0.AddMinutes(5);
        _hub.Respond = (req, _) => _clock.Now < hubBackAt || Has(req, poison) ? Status(HttpStatusCode.InternalServerError) : Ok(req);
        var w = Worker(new HarvestOutboxWorkerOptions { MaxBatch = 4 });

        await Simulate(w, TimeSpan.FromHours(6));

        var st = _box.GetStats();
        Assert.Equal(11, st.PushedTotal);
        Assert.Equal(1, st.Pending + st.Failed);   // chỉ còn mục độc
    }

    /// <summary>2 mục độc ở 2 lô: không kẹt — 10 mục lành vào hết, 2 mục độc failed hoặc còn chờ.</summary>
    [Fact]
    public async Task PB_HaiMucDoc_HaiLo_KhongKet_MucLanhVaoHet()
    {
        var ids = await SeedSynthetic(12);
        long p1 = ids[1], p2 = ids[5];
        _hub.Respond = (req, _) => Has(req, p1) || Has(req, p2) ? Status(HttpStatusCode.InternalServerError) : Ok(req);
        var w = Worker(new HarvestOutboxWorkerOptions { MaxBatch = 4 });

        await Simulate(w, TimeSpan.FromHours(6));

        var st = _box.GetStats();
        Assert.Equal(10, st.PushedTotal);
        Assert.Equal(2, st.Pending + st.Failed);
    }

    /// <summary>2 mục độc cùng một lô: 10 mục lành vào hết. Hai mục độc chỉ còn lại với nhau (không lô nào 200 xen giữa) →
    /// không có bằng chứng, nên KHÔNG bị ép failed (có thể Hub đang sập) — chúng nằm chờ, lùi dần.</summary>
    [Fact]
    public async Task PB_HaiMucDoc_CungLo_MucLanhVaoHet_MucDocNamCho()
    {
        var ids = await SeedSynthetic(12);
        long p1 = ids[0], p2 = ids[3];
        _hub.Respond = (req, _) => Has(req, p1) || Has(req, p2) ? Status(HttpStatusCode.InternalServerError) : Ok(req);
        var w = Worker(new HarvestOutboxWorkerOptions { MaxBatch = 4 });

        await Simulate(w, TimeSpan.FromHours(6));

        var st = _box.GetStats();
        Assert.Equal(10, st.PushedTotal);
        Assert.Equal(2, st.Pending + st.Failed);
    }

    /// <summary>Mục lành đơn lẻ gặp một lần 500 ngẫu nhiên, có một lần 200 của lô khác, rồi Hub hỏng hẳn: chỉ có tối đa 1
    /// lần "có bằng chứng" (200 cũ không được dùng mãi) → không failed oan.</summary>
    [Fact]
    public async Task PB_MucLanhDon_Le500_RoiHubSapHan_KhongFailed()
    {
        await SeedSynthetic(2);
        _hub.Respond = (req, n) => n == 1 ? Ok(req) : Status(HttpStatusCode.InternalServerError);
        var w = Worker(new HarvestOutboxWorkerOptions { MaxBatch = 1 });

        await Simulate(w, TimeSpan.FromHours(6));

        Assert.Equal(0, _box.GetStats().Failed);
        Assert.Equal(1, _box.GetStats().Pending);
    }

    [Fact]
    public async Task PB_MucLanh_Le500_RoiHubSapHan_KhongFailed()
    {
        await SeedSynthetic(5);
        _hub.Respond = (req, n) => n == 1 ? Ok(req) : Status(HttpStatusCode.InternalServerError);
        var w = Worker(new HarvestOutboxWorkerOptions { MaxBatch = 4 });

        await Simulate(w, TimeSpan.FromHours(6));

        Assert.Equal(0, _box.GetStats().Failed);
    }

    // ── Bắt buộc theo thiết kế mới ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Hub sập hoàn toàn 6 giờ, 200 mục: số request theo lùi dần chung (10,20,40,80,160 rồi mỗi 300 s ≈ 77), không
    /// mục nào failed; Hub sống lại thì vào hết.</summary>
    [Fact]
    public async Task HubSapHoanToan_6Gio_200Muc_RequestTheoLuiDan_KhongFailed()
    {
        await SeedSynthetic(200);
        _hub.Respond = (_, _) => Status(HttpStatusCode.InternalServerError);
        var w = Worker();

        await Simulate(w, TimeSpan.FromHours(6));

        Assert.InRange(_hub.Requests.Count, 70, 80);   // 6 mốc đầu trong 310 s + (21600−310)/300 ≈ 71 mốc 5'
        Assert.Equal(new HarvestOutboxStats(200, 0, 0, 0), _box.GetStats());

        _hub.Respond = (req, _) => Ok(req);
        await Simulate(w, TimeSpan.FromHours(2));
        Assert.Equal(new HarvestOutboxStats(0, 0, 200, 200), _box.GetStats());
    }

    /// <summary>1 mục độc giữa 200 mục lành, Hub sống, app đang cào (mục mới vào hàng chờ đều đặn — 50 mục đầu có sẵn, rồi
    /// 10 mục mỗi phút): lô thường KHÔNG bị chặn (ngay sau lô lỗi là một mục lẻ rồi lô thường; không bao giờ có hai lượt mục
    /// nghi vấn liền nhau khi còn mục thường đến hạn); mục độc failed sau đúng 5 lần 500 có bằng chứng; 199 mục kia vào hết.
    /// (Hàng chờ tĩnh thì mục độc chỉ còn lại một mình sau khi mục lành đi hết → không còn bằng chứng → nằm chờ, xem
    /// <see cref="MucDocMotMinh_KhongBaoGioFailed_LuiDan"/>.)</summary>
    [Fact]
    public async Task MotMucDoc_Giua200MucLanh_FailedSau5LanCoBangChung_LoThuongKhongBiChan()
    {
        const long poison = 1011;
        var next = 1;
        async Task Add(int n)
        {
            for (var i = 0; i < n && next <= 200; i++, next++)
                await _box.SaveAsync(Item("", next, json: SyntheticJson(1000 + next)), CancellationToken.None);
        }
        await Add(50);
        _hub.Respond = (req, _) => Has(req, poison) ? Status(HttpStatusCode.InternalServerError) : Ok(req);
        var w = Worker();
        var pendingNormalAtSuspectSend = 0;
        var wasSuspect = new List<bool>();
        _hub.OnSend = () =>
        {
            // Lượt gửi mục nghi vấn lẻ ngay sau một lượt nghi vấn khác trong khi còn mục THƯỜNG đến hạn = lô thường bị chặn.
            // (SP tổng hợp lưu theo thứ tự trên DB mới → id dòng outbox = itemId − 1000.)
            var r = _hub.Requests[^1];
            wasSuspect.Add(r.Items!.Count == 1 && w.SuspectIds.Contains(r.Items[0].ItemId - 1000));
            if (wasSuspect.Count >= 2 && wasSuspect[^1] && wasSuspect[^2]
                && _box.TakeDue(1, _clock.Now.ToUnixTimeMilliseconds(), w.SuspectIds).Count > 0)
                pendingNormalAtSuspectSend++;
            return Task.CompletedTask;
        };

        var end = _clock.Now.AddHours(3);
        var lastAdd = _clock.Now;
        while (_clock.Now < end)
        {
            var o = await w.PushOnceAsync(false, CancellationToken.None);
            if (o is HarvestPushOutcome.Sent or HarvestPushOutcome.Split or HarvestPushOutcome.Failed) continue;
            _clock.Advance(TimeSpan.FromSeconds(10));
            if (_clock.Now - lastAdd >= TimeSpan.FromMinutes(1)) { await Add(10); lastAdd = _clock.Now; }
        }

        Assert.Equal(new HarvestOutboxStats(0, 1, 199, 199), _box.GetStats());
        Assert.Equal("failed", Row(11).Status);
        Assert.Contains("http-500 lặp 5 lần", Row(11).LastError);
        var reqs = _hub.Requests;
        Assert.Equal(50, reqs[0].Items!.Count);   // lô có mục độc → 500 → 50 nghi vấn
        Assert.Single(reqs[1].Items!);            // sau lô lỗi: mục nghi vấn gửi lẻ
        Assert.Equal(0, pendingNormalAtSuspectSend);
        // Failed khi đủ 5 lần CÓ bằng chứng; lần nào không có 200 xen trước (vd lúc hàng chờ chỉ còn mục nghi vấn) không đếm.
        Assert.InRange(reqs.Count(r => r.Items!.Count == 1 && Has(r, poison)), 5, 8);
    }


    /// <summary>Mục độc nằm một mình trong hàng chờ (Hub vẫn có thể đang sập): không bao giờ failed, lùi dần chung.</summary>
    [Fact]
    public async Task MucDocMotMinh_KhongBaoGioFailed_LuiDan()
    {
        await SeedSynthetic(1);
        _hub.Respond = (_, _) => Status(HttpStatusCode.InternalServerError);
        var w = Worker();

        await Simulate(w, TimeSpan.FromHours(24));

        Assert.Equal(new HarvestOutboxStats(1, 0, 0, 0), _box.GetStats());
        Assert.InRange(_hub.Requests.Count, 280, 295);   // ~1 / 5' sau khi lùi tối đa
    }

    /// <summary>Hub chớp tắt: mọi lô 500 trong 5 phút đầu rồi sống lại → hết hàng, 0 failed.</summary>
    [Fact]
    public async Task HubChopTat5Phut_RoiSongLai_HetHang_KhongFailed()
    {
        await SeedSynthetic(120);
        var back = T0.AddMinutes(5);
        _hub.Respond = (req, _) => _clock.Now < back ? Status(HttpStatusCode.InternalServerError) : Ok(req);
        var w = Worker();

        await Simulate(w, TimeSpan.FromHours(2));

        Assert.Equal(new HarvestOutboxStats(0, 0, 120, 120), _box.GetStats());
    }

    /// <summary>Mục nghi vấn bị lưu đè bản mới đúng lúc đang được gửi lẻ: Hub ok cho bản cũ không xoá bản mới (version).</summary>
    [Fact]
    public async Task MucNghiVan_BiLuuDeTrongLucBay_VersionDung()
    {
        var ids = await SeedSynthetic(3);
        _hub.Respond = (_, _) => Status(HttpStatusCode.InternalServerError);
        var w = Worker(new HarvestOutboxWorkerOptions { MaxBatch = 4 });
        Assert.Equal(HarvestPushOutcome.Transient, await w.PushOnceAsync(false, CancellationToken.None));   // 3 mục → nghi vấn
        Assert.Equal(3, w.SuspectCount);
        _clock.Advance(TimeSpan.FromMinutes(1));

        _hub.Respond = (req, _) => Ok(req);
        _hub.OnSend = () => _box.SaveAsync(Item("", 1, at: T0.AddMinutes(5), json: SyntheticJson(ids[0])), CancellationToken.None);
        Assert.Equal(HarvestPushOutcome.Sent, await w.PushOnceAsync(false, CancellationToken.None));       // gửi lẻ mục 1
        _hub.OnSend = null;

        Assert.Equal([ids[0]], _hub.Requests[1].Items!.Select(x => x.ItemId));
        Assert.Equal(new HarvestOutboxStats(3, 0, 1, 1), _box.GetStats());   // bản mới của dòng 1 vẫn chờ
        Assert.Equal(2, Row(1).Version);
    }

    /// <summary>500 của mục nghi vấn chỉ đếm khi có một 200 SAU lần 500 trước của chính nó (lô thường xen giữa).</summary>
    [Fact]
    public async Task MucNghiVan_Dem500_ChiKhiCo200SauLanLoiTruoc()
    {
        var ids = await SeedSynthetic(2);
        var poison = ids[0];
        var w = Worker(new HarvestOutboxWorkerOptions { MaxBatch = 2, Suspect500Cap = 2 });
        _hub.Respond = (_, _) => Status(HttpStatusCode.InternalServerError);
        await w.PushOnceAsync(false, CancellationToken.None);                 // [1,2] 500 → cả hai nghi vấn
        // Hub sập: chỉ còn 2 mục nghi vấn gửi lẻ thay nhau, không 200 nào → không bao giờ đủ bằng chứng.
        await Simulate(w, TimeSpan.FromHours(3));
        Assert.Equal(new HarvestOutboxStats(2, 0, 0, 0), _box.GetStats());

        // Hub sống, chỉ mục độc 500: mục lành vào (200 — đồng thời là "Hub sống lại" nên cờ nghi vấn chưa có bằng chứng bị
        // bỏ); mục độc lại 500 (thành nghi vấn lại), không còn 200 nào xen → đứng yên, không failed.
        _hub.Respond = (req, _) => Has(req, poison) ? Status(HttpStatusCode.InternalServerError) : Ok(req);
        await Simulate(w, TimeSpan.FromHours(3));
        Assert.Equal(new HarvestOutboxStats(1, 0, 1, 1), _box.GetStats());

        // Mỗi mục mới vào hàng chờ → một lần 200 xen giữa → lần 500 kế của mục độc có bằng chứng; 2 mục mới → đủ 2 → failed.
        for (var k = 0; k < 2; k++)
        {
            await _box.SaveAsync(Item("", 9 + k, json: SyntheticJson(9990 + k)), CancellationToken.None);
            await Simulate(w, TimeSpan.FromHours(1));
        }
        Assert.Equal(new HarvestOutboxStats(0, 1, 3, 3), _box.GetStats());
    }

    /// <summary>Mục mồ côi (200 mà thiếu kết quả của mục) lùi dần theo SỐ LẦN THỬ của mục: 10, 20, 40 s…</summary>
    [Fact]
    public async Task MucMoCoi_LuiDanTheoSoLanThu()
    {
        await SeedSynthetic(1);
        _hub.Respond = (_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new HarvestedProductBatchResponse([]), options: Web),
        };
        var w = Worker();
        var delays = new List<double>();

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(HarvestPushOutcome.Sent, await w.PushOnceAsync(false, CancellationToken.None));
            var r = Row(1);
            delays.Add((r.NextTryAtMs - _clock.Now.ToUnixTimeMilliseconds()) / 1000.0);
            _clock.Advance(TimeSpan.FromMilliseconds(r.NextTryAtMs - _clock.Now.ToUnixTimeMilliseconds()));
        }

        Assert.Equal([10, 20, 40, 80], delays);
        Assert.Equal(4, Row(1).Attempts);
    }

    /// <summary>Nudge nhiều lần trong lúc một POST đang treo → sau khi POST xong vòng chỉ chạy thêm MỘT lượt (semaphore tối
    /// đa 1), không chạy liên tiếp theo số lần Nudge.</summary>
    [Fact]
    public async Task Nudge_NhieuLanKhiDangBan_ChiThemMotLuot()
    {
        await SeedSynthetic(1);
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        _hub.OnSend = async () => { entered.TrySetResult(); await release.Task; };
        _hub.Respond = (req, _) => Ok(req);
        await using var w = Worker(new HarvestOutboxWorkerOptions { Interval = TimeSpan.FromHours(1) });

        w.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        for (var i = 0; i < 5; i++) w.Nudge();
        release.SetResult();
        for (var i = 0; i < 100 && _box.GetStats().Pending > 0; i++) await Task.Delay(20);
        await Task.Delay(500);   // đủ để mọi lượt "thừa" (nếu có) chạy hết

        // Lượt 1: POST (Sent) → lượt 2: rỗng (Idle) → chờ: 1 tín hiệu → lượt 3: Idle → ngủ 1 giờ.
        Assert.Equal(3, w.Passes);
        await w.StopAsync();
    }

    /// <summary>200 mà body không đọc được (HTML proxy, rỗng, <c>{}</c> thiếu results) → KHÔNG được coi là đã nhận: giữ
    /// cả lô, lùi dần.</summary>
    [Theory]
    [InlineData("<html><body>Bad gateway</body></html>", "text/html")]
    [InlineData("", "application/json")]
    [InlineData("{}", "application/json")]
    [InlineData("{\"results\":null}", "application/json")]
    public async Task Body200KhongDocDuoc_GiuCaLo(string body, string type)
    {
        await Seed(2);
        _hub.Respond = (_, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, type) };
        var w = Worker();

        Assert.Equal(HarvestPushOutcome.Transient, await w.PushOnceAsync(false, CancellationToken.None));

        Assert.Equal(new HarvestOutboxStats(2, 0, 0, 0), _box.GetStats());
        Assert.Equal(("pending", 1), (Row(1).Status, Row(1).Attempts));
        Assert.Contains("không đọc được", Row(2).LastError);
    }

    // ── Lượt dọn cuối GĐ4 ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Xen kẽ: hàng chờ còn NHIỀU mục thường đến hạn, sau một lô thường phải là MỘT mục nghi vấn lẻ (không để
    /// nghi vấn dồn tới cuối).</summary>
    [Fact]
    public async Task XenKe_SauLoThuong_LaMotMucNghiVanLe()
    {
        var ids = await SeedSynthetic(10);
        var first = true;
        _hub.Respond = (req, _) => { if (first) { first = false; return Status(HttpStatusCode.InternalServerError); } return Ok(req); };
        var w = Worker(new HarvestOutboxWorkerOptions { MaxBatch = 2 });

        await w.PushOnceAsync(false, CancellationToken.None);   // [1,2] 500 → nghi vấn
        _clock.Advance(TimeSpan.FromMinutes(1));
        for (var i = 0; i < 4; i++) await w.PushOnceAsync(false, CancellationToken.None);

        var lots = _hub.Requests.Select(r => r.Items!.Select(x => x.ItemId).ToArray()).ToList();
        // Lô lỗi → (lượt trước là lô thường) mục nghi vấn 1 → lô thường [3,4] → mục nghi vấn 2 → lô thường [5,6].
        Assert.Equal([ids[0]], lots[1]);
        Assert.Equal([ids[2], ids[3]], lots[2]);
        Assert.Equal([ids[1]], lots[3]);
        Assert.Equal([ids[4], ids[5]], lots[4]);
    }

    /// <summary>Một lần 200 xoá mốc lùi dần chung VÀ đếm lỗi: ngay sau 200 gửi tiếp được; lỗi kế tiếp lùi lại từ 10 s.</summary>
    [Fact]
    public async Task Ok200_XoaMocLuiDanChung_VaDemLoi()
    {
        await SeedSynthetic(6);
        var w = Worker(new HarvestOutboxWorkerOptions { MaxBatch = 1 });
        _hub.Respond = (_, _) => Status(HttpStatusCode.ServiceUnavailable);
        await w.PushOnceAsync(false, CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(10));
        await w.PushOnceAsync(false, CancellationToken.None);   // lỗi lần 2 → mốc +20 s
        Assert.Equal(HarvestPushOutcome.NotDue, await w.PushOnceAsync(false, CancellationToken.None));

        _hub.Respond = (req, _) => Ok(req);
        Assert.Equal(HarvestPushOutcome.Sent, await w.PushOnceAsync(true, CancellationToken.None));    // 200 (bỏ qua mốc)
        Assert.Equal(HarvestPushOutcome.Sent, await w.PushOnceAsync(false, CancellationToken.None));   // mốc chung đã xoá

        _hub.Respond = (_, _) => Status(HttpStatusCode.ServiceUnavailable);
        await w.PushOnceAsync(false, CancellationToken.None);
        var r = _box.TakeDue(10, long.MaxValue).OrderByDescending(e => e.Attempts).First();
        Assert.Equal(10, (r.NextTryAtMs - _clock.Now.ToUnixTimeMilliseconds()) / 1000.0);   // đếm lỗi đã về 0
    }

    /// <summary>413 thu cỡ lô; sau đúng 10 lần 200 ở cỡ nhỏ thì nới gấp đôi lại.</summary>
    [Fact]
    public async Task QuaCo_NoiCoLoSau10Lan200()
    {
        await SeedSynthetic(40);
        var first = true;
        _hub.Respond = (req, _) => { if (first) { first = false; return Status(HttpStatusCode.RequestEntityTooLarge); } return Ok(req); };
        var w = Worker(new HarvestOutboxWorkerOptions { MaxBatch = 4 });

        await Simulate(w, TimeSpan.FromMinutes(5));

        Assert.Equal([4, .. Enumerable.Repeat(2, 10), .. Enumerable.Repeat(4, 5)], _hub.Requests.Select(r => r.Items!.Count));
        Assert.Equal(40, _box.GetStats().PushedTotal);
    }

    /// <summary>500 CÓ bằng chứng của mục nghi vấn chỉ hoãn riêng mục đó — lô thường đi tiếp NGAY, không chờ mốc chung.</summary>
    [Fact]
    public async Task MucNghiVan500CoBangChung_KhongDayMocChung()
    {
        var ids = await SeedSynthetic(10);
        var poison = ids[0];
        _hub.Respond = (req, _) => Has(req, poison) ? Status(HttpStatusCode.InternalServerError) : Ok(req);
        var w = Worker(new HarvestOutboxWorkerOptions { MaxBatch = 2 });

        Assert.Equal(HarvestPushOutcome.Transient, await w.PushOnceAsync(false, CancellationToken.None));   // [1,2] 500
        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(HarvestPushOutcome.Deferred, await w.PushOnceAsync(false, CancellationToken.None));    // độc lẻ, chưa bằng chứng
        Assert.Equal(HarvestPushOutcome.NotDue, await w.PushOnceAsync(false, CancellationToken.None));      // → mốc chung
        _clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(HarvestPushOutcome.Sent, await w.PushOnceAsync(false, CancellationToken.None));        // [3,4]
        Assert.Equal(HarvestPushOutcome.Sent, await w.PushOnceAsync(false, CancellationToken.None));        // nghi vấn 2 lẻ
        Assert.Equal(HarvestPushOutcome.Sent, await w.PushOnceAsync(false, CancellationToken.None));        // [5,6]
        Assert.Equal(HarvestPushOutcome.Deferred, await w.PushOnceAsync(false, CancellationToken.None));    // độc lẻ, CÓ bằng chứng
        Assert.Equal(HarvestPushOutcome.Sent, await w.PushOnceAsync(false, CancellationToken.None));        // [7,8] đi ngay

        Assert.Equal([ids[6], ids[7]], _hub.Requests[^1].Items!.Select(x => x.ItemId));
        Assert.True(Row(1).NextTryAtMs > _clock.Now.ToUnixTimeMilliseconds());                              // mục độc hoãn riêng
    }

    /// <summary>Hub 500 toàn bộ 24 giờ rồi sống lại: 200 đầu tiên bỏ cờ nghi vấn của mục chưa từng 500 có bằng chứng → hàng
    /// chờ đi lại theo lô 50 (vài request), không đi lẻ 200 lần.</summary>
    [Fact]
    public async Task HubSongLai_Sau24Gio500_BoNghiVan_DiTheoLo()
    {
        await SeedSynthetic(200);
        var back = T0.AddHours(24);
        _hub.Respond = (req, _) => _clock.Now < back ? Status(HttpStatusCode.InternalServerError) : Ok(req);
        var w = Worker();

        await Simulate(w, TimeSpan.FromHours(24));
        var during = _hub.Requests.Count;
        Assert.True(w.SuspectCount > 0);
        await Simulate(w, TimeSpan.FromHours(2));

        Assert.Equal(new HarvestOutboxStats(0, 0, 200, 200), _box.GetStats());
        Assert.InRange(_hub.Requests.Count - during, 4, 8);   // ~1 lẻ (200 đầu) + 4 lô 50
        Assert.InRange(during, 280, 300);                    // trong lúc sập: theo lùi dần chung
    }

    /// <summary>Flush: mục nghi vấn gửi lẻ bị 500 → bỏ qua mục đó, đi tiếp lô thường (không dừng cả Flush).</summary>
    [Fact]
    public async Task Flush_MucNghiVan500_BoQua_DiTiep()
    {
        var ids = await SeedSynthetic(6);
        var poison = ids[0];
        _hub.Respond = (req, _) => Has(req, poison) ? Status(HttpStatusCode.InternalServerError) : Ok(req);
        var w = Worker(new HarvestOutboxWorkerOptions { MaxBatch = 2 });
        await w.PushOnceAsync(false, CancellationToken.None);   // [1,2] 500 → nghi vấn

        Assert.False(await w.FlushAsync(TimeSpan.FromSeconds(30)));

        Assert.Equal(new HarvestOutboxStats(1, 0, 5, 5), _box.GetStats());   // chỉ còn mục độc
        Assert.Equal(1, _hub.Requests.Count(r => r.Items!.Count == 1 && Has(r, poison)));   // không dội mục độc
    }

    /// <summary>Lùi dần có nhiễu ±20% theo nguồn ngẫu nhiên tiêm vào: 0 → 8 s, ~1 → ~12 s, 0.5 → 10 s.</summary>
    [Theory]
    [InlineData(0.0, 8.0)]
    [InlineData(0.5, 10.0)]
    [InlineData(0.999, 11.996)]
    public async Task LuiDan_CoNhieu20PhanTram(double rnd, double expectedSec)
    {
        await SeedSynthetic(1);
        _hub.Respond = (_, _) => Status(HttpStatusCode.ServiceUnavailable);
        var w = Worker(random: () => rnd);

        await w.PushOnceAsync(false, CancellationToken.None);

        Assert.Equal(expectedSec, (Row(1).NextTryAtMs - _clock.Now.ToUnixTimeMilliseconds()) / 1000.0, 2);
    }

    // ── 503 / 401 / 404 / mất mạng / timeout: giữ, lùi dần, không trần ─────────────────────────────────────────────

    [Fact]
    public async Task Loi503_LuiDanToi5Phut_KhongDatTran()
    {
        await Seed(1);
        _hub.Respond = (_, _) => Status(HttpStatusCode.ServiceUnavailable, "pg-not-ready");
        var w = Worker();
        var delays = new List<double>();

        for (var i = 0; i < 30; i++)
        {
            Assert.Equal(HarvestPushOutcome.Transient, await w.PushOnceAsync(false, CancellationToken.None));
            var r = Row(1);
            delays.Add((r.NextTryAtMs - _clock.Now.ToUnixTimeMilliseconds()) / 1000.0);
            Assert.Equal(HarvestPushOutcome.NotDue, await w.PushOnceAsync(false, CancellationToken.None));   // chưa tới mốc chung
            _clock.Advance(TimeSpan.FromMilliseconds(r.NextTryAtMs - _clock.Now.ToUnixTimeMilliseconds()));
        }

        Assert.Equal([10, 20, 40, 80, 160, 300, 300], delays.Take(7));
        Assert.All(delays.Skip(5), d => Assert.Equal(300, d));
        Assert.Equal(("pending", 30), (Row(1).Status, Row(1).Attempts));
        Assert.Contains("pg-not-ready", Row(1).LastError);
        Assert.Equal(30, _hub.Requests.Count);
        Assert.Contains("pg-not-ready", w.LastError);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task MaKhac_GiuLo_LuiDan(HttpStatusCode code)
    {
        await Seed(2);
        _hub.Respond = (_, _) => Status(code);
        var w = Worker();

        Assert.Equal(HarvestPushOutcome.Transient, await w.PushOnceAsync(false, CancellationToken.None));

        Assert.Equal(new HarvestOutboxStats(2, 0, 0, 0), _box.GetStats());
        Assert.Equal(1, Row(1).Attempts);
    }

    [Fact]
    public async Task MatMang_VaTimeout_GiuLo_LuiDan()
    {
        await Seed(2);
        var w = Worker();

        _hub.Respond = (_, _) => throw new HttpRequestException("No such host is known.");
        Assert.Equal(HarvestPushOutcome.Transient, await w.PushOnceAsync(false, CancellationToken.None));
        Assert.Contains("mất kết nối", Row(1).LastError);

        _clock.Advance(TimeSpan.FromMinutes(6));
        _hub.Respond = (_, _) => throw new TaskCanceledException("timeout");
        Assert.Equal(HarvestPushOutcome.Transient, await w.PushOnceAsync(false, CancellationToken.None));
        Assert.Contains("timeout", Row(1).LastError);

        Assert.Equal(new HarvestOutboxStats(2, 0, 0, 0), _box.GetStats());
        Assert.Equal(2, Row(2).Attempts);
    }

    [Fact]
    public async Task ChuaKetNoiHub_GiuLo()
    {
        await Seed(1);
        var w = new HarvestOutboxWorker(_box, () => null, null, null, () => _clock.Now);

        Assert.Equal(HarvestPushOutcome.Transient, await w.PushOnceAsync(false, CancellationToken.None));
        Assert.Equal(("pending", 1), (Row(1).Status, Row(1).Attempts));
    }

    // ── 413 / 400 ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(413, null)]
    [InlineData(400, "batch-too-large")]
    public async Task QuaCo_ChiaNho_TatCaVao(int code, string? error)
    {
        await Seed(5);
        _hub.Respond = (req, _) => req.Items!.Count > 1 ? Status((HttpStatusCode)code, error) : Ok(req);
        var w = Worker();

        for (var i = 0; i < 50 && await w.PushOnceAsync(false, CancellationToken.None) != HarvestPushOutcome.Idle; i++) { }

        Assert.Equal(new HarvestOutboxStats(0, 0, 5, 5), _box.GetStats());
        Assert.Equal([5, 2, 1, 1, 1, 1, 1], _hub.Requests.Select(r => r.Items!.Count));   // 413 → 2 → 413 → 1; giữ cỡ 1 tới khi đủ 10 lần 200
    }

    [Fact]
    public async Task QuaCo_Lo1Muc_DanhLoi()
    {
        await Seed(1);
        _hub.Respond = (_, _) => Status(HttpStatusCode.RequestEntityTooLarge);
        var w = Worker();

        Assert.Equal(HarvestPushOutcome.Failed, await w.PushOnceAsync(false, CancellationToken.None));
        Assert.Equal("failed", Row(1).Status);
    }

    [Fact]
    public async Task Loi400Khac_DanhLoiCaLo_KhongGuiLai()
    {
        await Seed(3);
        _hub.Respond = (_, _) => Status(HttpStatusCode.BadRequest, "client-now");
        var w = Worker();

        Assert.Equal(HarvestPushOutcome.Failed, await w.PushOnceAsync(false, CancellationToken.None));
        _clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(HarvestPushOutcome.Idle, await w.PushOnceAsync(false, CancellationToken.None));

        Assert.Equal(new HarvestOutboxStats(0, 3, 0, 0), _box.GetStats());
        Assert.Contains("client-now", Row(2).LastError);
        Assert.Single(_hub.Requests);
        Assert.Contains(_log, l => l.Contains("client-now"));
    }

    // ── clientNow ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClientNow_DatLucGui_GuiLaiThiMocMoi()
    {
        await Seed(1);
        var nowAtSend = new List<DateTimeOffset>();
        _hub.Respond = (req, n) =>
        {
            nowAtSend.Add(_clock.Now);
            return n == 0 ? Status(HttpStatusCode.ServiceUnavailable) : Ok(req);
        };
        // Thời gian trôi GIỮA lúc lấy lô khỏi outbox và lúc gửi (lấy HubClient chậm, lô lớn…): mốc phải là lúc gửi.
        var w = new HarvestOutboxWorker(_box, () => { _clock.Advance(TimeSpan.FromSeconds(5)); return _client; }, null, null, () => _clock.Now);

        await w.PushOnceAsync(false, CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(2));
        await w.PushOnceAsync(false, CancellationToken.None);

        Assert.Equal(2, _hub.Requests.Count);
        Assert.Equal(nowAtSend[0], _hub.Requests[0].ClientNow);
        Assert.Equal(nowAtSend[1], _hub.Requests[1].ClientNow);   // gửi lại: mốc MỚI, không dùng lại mốc lần đầu
        Assert.Equal(T0.AddSeconds(5), nowAtSend[0]);
    }

    // ── Vòng nền / Flush ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task VongNen_DayHet_Stop()
    {
        await Seed(3);
        _hub.Respond = (req, _) => Ok(req);
        await using var w = Worker(new HarvestOutboxWorkerOptions { Interval = TimeSpan.FromMilliseconds(50) });

        w.Start();
        for (var i = 0; i < 100 && _box.GetStats().Pending > 0; i++) await Task.Delay(20);
        await _box.SaveAsync(Item(Fixtures[3], 9), CancellationToken.None);
        w.Nudge();
        for (var i = 0; i < 100 && _box.GetStats().Pending > 0; i++) await Task.Delay(20);
        await w.StopAsync();

        Assert.Equal(new HarvestOutboxStats(0, 0, 4, 4), _box.GetStats());
    }

    [Fact]
    public async Task Flush_BoQuaHenLuiDan_DayHet()
    {
        await Seed(3);
        var w = Worker(new HarvestOutboxWorkerOptions { MaxBatch = 2 });
        _hub.Respond = (_, _) => Status(HttpStatusCode.ServiceUnavailable);
        await w.PushOnceAsync(false, CancellationToken.None);   // lô 2 mục bị hẹn lùi dần

        _hub.Respond = (req, _) => Ok(req);
        Assert.True(await w.FlushAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(new HarvestOutboxStats(0, 0, 3, 3), _box.GetStats());
    }

    [Fact]
    public async Task Flush_HubLoi_TraFalse_GiuDuLieu()
    {
        await Seed(2);
        _hub.Respond = (_, _) => Status(HttpStatusCode.ServiceUnavailable);
        var w = Worker();

        Assert.False(await w.FlushAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, _box.GetStats().Pending);
        Assert.Single(_hub.Requests);
    }

    // ── Hub / đồng hồ giả ──────────────────────────────────────────────────────────────────────────────────────────

    private sealed class FakeClock(DateTimeOffset start)
    {
        public DateTimeOffset Now { get; private set; } = start;

        public void Advance(TimeSpan by) => Now += by;
    }

    /// <summary>Hub giả: ghi lại request đã parse, trả theo <see cref="Respond"/> (tham số 2 = số thứ tự lần gọi).</summary>
    private sealed class FakeHub : HttpMessageHandler
    {
        public Func<HarvestedProductBatchRequest, int, HttpResponseMessage> Respond { get; set; } = (r, _) => Ok(r);
        public List<HarvestedProductBatchRequest> Requests { get; } = [];
        /// <summary>Chạy trong lúc request đang "bay" (trước khi trả phản hồi).</summary>
        public Func<Task>? OnSend { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HubRoutes.HarvestedProductsBatch, request.RequestUri!.AbsolutePath);
            Assert.Equal("t", request.Headers.GetValues("X-Api-Token").Single());
            Assert.Equal("may-1", request.Headers.GetValues("X-Machine-Id").Single());
            var body = (await request.Content!.ReadFromJsonAsync<HarvestedProductBatchRequest>(Web, cancellationToken))!;
            Requests.Add(body);
            if (OnSend is not null) await OnSend();
            return Respond(body, Requests.Count - 1);
        }
    }
}
