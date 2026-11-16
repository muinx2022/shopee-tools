using System.Text.Json;
using Shopee.Core.Coordination;
using Shopee.Core.Harvest;
using Shopee.Core.Scrape;

namespace Shopee.Core.Tests;

/// <summary>
/// Logic thuần của chế độ Harvest (GĐ3): đọc kết quả bước extension <c>shopee-harvest</c>, bảng kết quả → hành động +
/// cầu dao pdpError, làm sạch link, cảnh báo hook-reload, khoá lease/ledger riêng, store tiến độ tách file.
/// </summary>
public sealed class HarvestEngineLogicTests
{
    // ── Đọc kết quả bước (shape thật: harvestResult() trong extensions/shopee-harvest/background.js) ──────────────

    private static string FixturePath(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Fixtures", "pdp", name);
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "tests", "Shopee.Core.Tests", "Fixtures", "pdp", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(name);
    }

    private static JsonElement Obj(object o) => JsonSerializer.SerializeToElement(o);

    [Fact]
    public void Parse_OkResult_ReadsAllFields_JsonKeptVerbatim()
    {
        var body = File.ReadAllText(FixturePath("pdp-2tang-8model.json"));
        // Đúng các khoá extension trả ở nhánh ok (probe → harvestResult({ok,scrapeOk,harvestOk,…,capturedUrl,capturedAt})).
        var value = Obj(new
        {
            ok = true, scrapeOk = true, harvestOk = true, captcha = false, proxyError = false, trafficError = false,
            blocked = false, notProduct = false, aborted = false, superseded = false,
            message = "Đã lấy dữ liệu sản phẩm 26393229156.", tabId = 812,
            pageUrl = "https://shopee.vn/ao-i.1490438686.26393229156",
            itemId = "26393229156",        // extension lấy từ regex link → CHUỖI
            shopId = 1490438686L,          // check.shopId lấy từ body → SỐ
            json = body, source = "hook", pdpError = (object?)null,
            capturedUrl = "https://shopee.vn/api/v4/pdp/get_pc?item_id=26393229156", capturedAt = 1731650000000L,
        });

        var r = HarvestStepResult.Parse(value, fallbackTabId: 1, fallbackPageUrl: "fallback");

        Assert.True(r.HarvestOk);
        Assert.False(r.Captcha || r.ProxyError || r.TrafficError || r.Blocked || r.NotProduct || r.Aborted || r.Superseded);
        Assert.Equal(812, r.TabId);
        Assert.Equal("https://shopee.vn/ao-i.1490438686.26393229156", r.PageUrl);
        Assert.Equal(26393229156L, r.ItemId);
        Assert.Equal(1490438686L, r.ShopId);
        Assert.Equal(body, r.Json);
        Assert.Equal("hook", r.Source);
        Assert.Null(r.PdpError);
        Assert.Equal("https://shopee.vn/api/v4/pdp/get_pc?item_id=26393229156", r.CapturedUrl);
        Assert.Equal(1731650000000L, r.CapturedAtMs);
        using var doc = JsonDocument.Parse(r.Json!);
        Assert.Equal(r.ItemId, doc.RootElement.GetProperty("data").GetProperty("item").GetProperty("item_id").GetInt64());
    }

    [Fact]
    public void HarvestedAt_UsesCapturedAtWhenPresent_ElseReceivedAt()
    {
        var received = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.FromHours(7));
        var captured = received.AddSeconds(-42);
        var withCaptured = HarvestStepResult.Parse(Obj(new { harvestOk = true, capturedAt = captured.ToUnixTimeMilliseconds() }), null, null);
        Assert.Equal(captured, withCaptured.HarvestedAtOr(received));
        Assert.Equal(received.Offset, withCaptured.HarvestedAtOr(received).Offset);

        Assert.Equal(received, HarvestStepResult.Parse(Obj(new { harvestOk = true }), null, null).HarvestedAtOr(received));
        Assert.Equal(received, HarvestStepResult.Parse(Obj(new { capturedAt = 0 }), null, null).HarvestedAtOr(received));
        Assert.Equal(received, HarvestStepResult.Parse(Obj(new { capturedAt = -5 }), null, null).HarvestedAtOr(received));
        Assert.Equal(received, HarvestStepResult.Parse(Obj(new { capturedAt = long.MaxValue }), null, null).HarvestedAtOr(received));   // ngoài khoảng → không ném
        Assert.Equal(received, HarvestStepResult.Parse(Obj(new { capturedAt = "abc" }), null, null).HarvestedAtOr(received));
    }

    [Fact]
    public void Parse_BlockedResult_PdpErrorNumberAsText()
    {
        var r = HarvestStepResult.Parse(Obj(new
        {
            ok = false, harvestOk = false, trafficError = true, blocked = true, pdpError = 90309999,
            message = "Shopee chặn get_pc", tabId = (int?)null, pageUrl = "", itemId = "5", shopId = "4",
            json = (string?)null, source = (string?)null,
        }), fallbackTabId: 7, fallbackPageUrl: "https://shopee.vn/product/4/5");

        Assert.True(r.TrafficError);
        Assert.True(r.Blocked);
        Assert.Equal("90309999", r.PdpError);
        Assert.Equal(7, r.TabId);                                    // tabId null → giữ tab cũ
        Assert.Equal("https://shopee.vn/product/4/5", r.PageUrl);    // pageUrl rỗng → link đã gửi
        Assert.Null(r.Json);
    }

    [Fact]
    public void Parse_SupersededStopResult()
    {
        var r = HarvestStepResult.Parse(Obj(new
        {
            ok = false, harvestOk = false, aborted = true, superseded = true, message = "Bước đã bị bước mới thay thế.",
        }), null, null);
        Assert.True(r.Superseded);
        Assert.True(r.Aborted);
        Assert.False(r.HarvestOk);
    }

    [Fact]
    public void Parse_MissingFieldsAndNonObject_AreSafe()
    {
        var empty = HarvestStepResult.Parse(Obj(new { }), 3, "u");
        Assert.False(empty.HarvestOk);
        Assert.Null(empty.ItemId);
        Assert.Equal(3, empty.TabId);

        var notObject = HarvestStepResult.Parse(JsonSerializer.SerializeToElement(42), 3, "u");
        Assert.False(notObject.HarvestOk);
        Assert.Equal(HarvestStepAction.RowFailed, HarvestStepPolicy.Decide(notObject, new HarvestPdpBreaker()).Action);
    }

    // ── Bảng kết quả → hành động ────────────────────────────────────────────────────────────────────────────────

    private static HarvestStepResult R(
        bool ok = false, bool captcha = false, bool proxy = false, bool traffic = false, bool blocked = false,
        bool notProduct = false, bool aborted = false, bool superseded = false, string? json = null, long? itemId = null,
        string? pdpError = null, string? source = null) =>
        new(ok, captcha, proxy, traffic, blocked, notProduct, aborted, superseded, "m", 1, "u", itemId, 2, json, source, pdpError);

    private static readonly string J = "{\"error\":null,\"data\":{\"item\":{\"item_id\":9}}}";

    public static IEnumerable<object[]> Table() =>
    [
        [R(superseded: true, aborted: true), HarvestStepAction.Superseded],
        [R(ok: true, json: J, itemId: 9, superseded: true, aborted: true), HarvestStepAction.Superseded],
        [R(aborted: true), HarvestStepAction.Abort],
        [R(aborted: true, captcha: true), HarvestStepAction.Abort],
        [R(traffic: true), HarvestStepAction.Traffic],
        [R(traffic: true, blocked: true, pdpError: "90309999"), HarvestStepAction.Traffic],
        [R(proxy: true), HarvestStepAction.ProxyError],
        [R(captcha: true), HarvestStepAction.Captcha],
        [R(ok: true, captcha: true, json: J, itemId: 9), HarvestStepAction.Save],   // đã giải captcha giữa chừng
        [R(ok: true, json: J, itemId: 9, source: "hook-reload"), HarvestStepAction.Save],
        [R(ok: true, json: null, itemId: 9), HarvestStepAction.RowFailed],          // ok mà thiếu JSON
        [R(ok: true, json: J, itemId: null), HarvestStepAction.RowFailed],          // ok mà thiếu itemId
        [R(notProduct: true), HarvestStepAction.RowFailed],
        [R(pdpError: "123"), HarvestStepAction.PdpError],
        [R(), HarvestStepAction.RowFailed],
    ];

    [Theory]
    [MemberData(nameof(Table))]
    public void Decide_MapsEachResultKind(HarvestStepResult result, HarvestStepAction expected) =>
        Assert.Equal(expected, HarvestStepPolicy.Decide(result, new HarvestPdpBreaker(3)).Action);

    private static HarvestStepAction Feed(HarvestPdpBreaker b, HarvestStepResult r) => HarvestStepPolicy.Decide(r, b).Action;

    [Fact]
    public void Breaker_TripsOnNthConsecutiveSameCode_AndResetsOnSave()
    {
        var b = new HarvestPdpBreaker(3);
        Assert.Equal(HarvestStepAction.PdpError, Feed(b, R(pdpError: "77")));
        var d2 = HarvestStepPolicy.Decide(R(pdpError: "77"), b);
        Assert.Equal((HarvestStepAction.PdpError, "77", 2), (d2.Action, d2.PdpCode, d2.PdpStreak));
        Assert.Equal(HarvestStepAction.PdpBreakerTrip, Feed(b, R(pdpError: "77")));
        Assert.Equal(0, b.Streak);
        Assert.Contains("77", b.AwaitingConfirmCodes);
        Assert.Empty(b.ConfirmedDeadCodes);

        // Dòng ok xen giữa → đếm lại từ đầu.
        var c = new HarvestPdpBreaker(3);
        Feed(c, R(pdpError: "5"));
        Feed(c, R(pdpError: "5"));
        Assert.Equal(HarvestStepAction.Save, Feed(c, R(ok: true, json: J, itemId: 9)));
        Assert.Equal(0, c.Streak);
        Assert.Equal(HarvestStepAction.PdpError, Feed(c, R(pdpError: "5")));
        Assert.Equal(HarvestStepAction.PdpError, Feed(c, R(pdpError: "5")));
    }

    [Fact]
    public void Breaker_DifferentCodeRestartsCount()
    {
        var b = new HarvestPdpBreaker(3);
        Feed(b, R(pdpError: "1"));
        Feed(b, R(pdpError: "1"));
        Assert.Equal(HarvestStepAction.PdpError, Feed(b, R(pdpError: "2")));    // mã khác → đếm lại, không bật
        Assert.Equal(("2", 1), (b.CurrentCode, b.Streak));
        Assert.Equal(HarvestStepAction.PdpError, Feed(b, R(pdpError: "2")));
        Assert.Equal(HarvestStepAction.PdpBreakerTrip, Feed(b, R(pdpError: "2")));
        Assert.Equal(new[] { "2" }, b.AwaitingConfirmCodes);
    }

    [Fact]
    public void Breaker_CodeAlreadyTripped_IsPlainRowError_OtherCodesStillCount()
    {
        var b = new HarvestPdpBreaker(2);
        Feed(b, R(pdpError: "9"));
        Assert.Equal(HarvestStepAction.PdpBreakerTrip, Feed(b, R(pdpError: "9")));

        b.ResetStreak();   // vòng chạy lại sau khi đổi IP — tập mã đã bật vẫn giữ
        for (var i = 0; i < 5; i++)
            Assert.Equal(HarvestStepAction.RowFailed, Feed(b, R(pdpError: "9")));   // cụm SP chết không đốt IP nữa

        Assert.Equal(HarvestStepAction.PdpError, Feed(b, R(pdpError: "4")));
        Assert.Equal(HarvestStepAction.PdpBreakerTrip, Feed(b, R(pdpError: "4")));   // mã MỚI vẫn bật được
    }

    [Fact]
    public void Breaker_IpBlockedCode_SaveAfterIpChange_ForgetsCode_TripsAgainLater()
    {
        // Kịch bản phản biện: dòng 1–3 lỗi X → bật, đổi IP; dòng 1–5 lưu được (X là chặn theo IP); dòng 6–10 lại lỗi X
        // trên IP mới → phải BẬT LẠI cầu dao, không được thành 5 lỗi dòng.
        var b = new HarvestPdpBreaker(3);
        Feed(b, R(pdpError: "X"));
        Feed(b, R(pdpError: "X"));
        Assert.Equal(HarvestStepAction.PdpBreakerTrip, Feed(b, R(pdpError: "X")));
        Assert.Equal(new[] { "X" }, b.AwaitingConfirmCodes);

        b.ResetStreak();                                                          // lượt mới sau khi đổi IP
        for (var row = 1; row <= 5; row++)
            Assert.Equal(HarvestStepAction.Save, Feed(b, R(ok: true, json: J, itemId: 9)));
        Assert.Empty(b.AwaitingConfirmCodes);
        Assert.Empty(b.ConfirmedDeadCodes);

        Assert.Equal(HarvestStepAction.PdpError, Feed(b, R(pdpError: "X")));     // dòng 6
        Assert.Equal(HarvestStepAction.PdpError, Feed(b, R(pdpError: "X")));     // dòng 7
        Assert.Equal(HarvestStepAction.PdpBreakerTrip, Feed(b, R(pdpError: "X"))); // dòng 8 → đổi IP/tk lần nữa
    }

    [Fact]
    public void Breaker_CodeRecursOnFirstRetriedRow_ConfirmedDead_StaysDeadAfterLaterSaves()
    {
        var b = new HarvestPdpBreaker(3);
        Feed(b, R(pdpError: "X"));
        Feed(b, R(pdpError: "X"));
        Assert.Equal(HarvestStepAction.PdpBreakerTrip, Feed(b, R(pdpError: "X")));

        b.ResetStreak();                                                          // sau đổi IP, dòng làm lại đầu tiên
        Assert.Equal(HarvestStepAction.RowFailed, Feed(b, R(pdpError: "X")));    // vẫn X → SP chết, nhớ X
        Assert.Equal(new[] { "X" }, b.ConfirmedDeadCodes);
        Assert.Empty(b.AwaitingConfirmCodes);
        Assert.Equal(HarvestStepAction.RowFailed, Feed(b, R(pdpError: "X")));
        Assert.Equal(HarvestStepAction.Save, Feed(b, R(ok: true, json: J, itemId: 9)));
        Assert.Equal(HarvestStepAction.RowFailed, Feed(b, R(pdpError: "X")));    // đã xác nhận → dòng lưu được không xoá
        Assert.Equal(HarvestStepAction.RowFailed, Feed(b, R(pdpError: "X")));
        Assert.Equal(HarvestStepAction.RowFailed, Feed(b, R(pdpError: "X")));
    }

    [Fact]
    public void Breaker_OtherRowErrorBreaksChain_SupersededAndCaptchaKeepIt()
    {
        static HarvestPdpBreaker Two()
        {
            var b = new HarvestPdpBreaker(3);
            Feed(b, R(pdpError: "1"));
            Feed(b, R(pdpError: "1"));
            return b;
        }

        var x = Two(); Feed(x, R(notProduct: true)); Assert.Equal(0, x.Streak);
        var y = Two(); Feed(y, R(superseded: true, aborted: true)); Assert.Equal(2, y.Streak);
        var z = Two(); Feed(z, R(captcha: true)); Assert.Equal(2, z.Streak);
        Assert.Equal(HarvestStepAction.PdpBreakerTrip, Feed(z, R(pdpError: "1")));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(0, HarvestStepPolicy.DefaultPdpErrorBreakerThreshold)]   // ≤0 → mặc định 3
    public void Breaker_ThresholdConfigurable(int threshold, int tripsAt)
    {
        var b = new HarvestPdpBreaker(threshold);
        Assert.Equal(tripsAt, b.Threshold);
        for (var i = 1; i <= tripsAt; i++)
            Assert.Equal(i == tripsAt ? HarvestStepAction.PdpBreakerTrip : HarvestStepAction.PdpError, Feed(b, R(pdpError: "1")));
    }

    // ── Cảnh báo hook-reload ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SourceMonitor_WarnsOnlyWhenWindowFullAndRatioAboveThreshold_NoSpam()
    {
        var m = new HarvestSourceMonitor(window: 10, threshold: 0.3);
        for (var i = 0; i < 9; i++) Assert.Null(m.Record("hook-reload"));   // chưa đủ cửa sổ
        Assert.Equal(1.0, m.Record("hook-reload"));                          // đủ 10, 100% > 30%
        for (var i = 0; i < 9; i++) Assert.Null(m.Record("hook-reload"));   // không spam trong cùng cửa sổ
        Assert.NotNull(m.Record("hook-reload"));                             // qua 1 cửa sổ → nhắc lại

        var low = new HarvestSourceMonitor(window: 10, threshold: 0.3);
        double? any = null;
        for (var i = 0; i < 30; i++) any ??= low.Record(i % 10 < 3 ? "hook-reload" : "hook");   // đúng 30% → không vượt
        Assert.Null(any);
    }

    // ── Làm sạch link ───────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://shopee.vn/Ao-thun-i.123.456%7DD", "https://shopee.vn/product/123/456")]          // đuôi lạ
    [InlineData("  https://shopee.vn/Ao-thun-i.123.456?sp_atk=x  ", "https://shopee.vn/product/123/456")]
    [InlineData("https://shopee.vn/Router-Wi.2.4G-5G-i.111.222", "https://shopee.vn/product/111/222")]  // P-20261115-11
    [InlineData("https://shopee.vn/i.123.456", "https://shopee.vn/product/123/456")]
    [InlineData("https://shopee.vn/product/123/456", "https://shopee.vn/product/123/456")]              // đã sạch
    [InlineData("https://shopee.vn/product/123/456?x=1", "https://shopee.vn/product/123/456?x=1")]      // không phải SEO → giữ
    [InlineData("https://s.shopee.vn/AbCd", "https://s.shopee.vn/AbCd")]                               // link rút gọn → giữ
    [InlineData("shopee.vn/Ao-i.1.2", "shopee.vn/Ao-i.1.2")]                                           // không phải URL tuyệt đối
    [InlineData("", "")]
    [InlineData(null, "")]
    public void CleanProductUrl(string? input, string expected) =>
        Assert.Equal(expected, HarvestLinks.CleanProductUrl(input));

    [Fact]
    public void CleanProductUrl_UnanchoredHelperWouldPickWrongIds_AnchoredDoesNot()
    {
        const string link = "https://shopee.vn/Router-Wi.2.4G-5G-i.111.222";
        // Helper cũ (chưa neo) đọc nhầm "i.2.4" — lý do Harvest không dùng nó (ghi nhận để không ai đổi ngược).
        Assert.Equal("https://shopee.vn/product/2/4", ShopeeApiDataHelper.CanonicalProductUrl(link));
        Assert.Equal("https://shopee.vn/product/111/222", HarvestLinks.CleanProductUrl(link));
    }

    // ── Khoá điều phối riêng ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CoordKey_HarvestDiffersFromScrape_OpStringMatchesContract()
    {
        var scrape = new CoordKey("bs1", "shop1", "Data", CoordOp.Scrape);
        var harvest = new CoordKey("bs1", "shop1", "Data", CoordOp.Harvest);
        Assert.NotEqual(scrape.Id, harvest.Id);
        Assert.Equal("bs1__shop1__harvest", harvest.Id);
        Assert.Equal(AssignmentOps.Harvest, HttpCoordinationHub.OpStr(CoordOp.Harvest));
    }

    [Fact]
    public void LeaseRequest_HarvestSendsEmptyBigsellerId_KeyStillScopedByBigseller()
    {
        var req = HttpCoordinationHub.BuildLeaseRequest(new CoordKey("bs1", "shop1", "Data", CoordOp.Harvest), "m1", "h1", false);
        Assert.Equal("", req.BigsellerId);                 // Hub không ghim acc BigSeller theo lease harvest
        Assert.Equal("bs1__shop1__harvest", req.Key);      // nhưng khoá vẫn duy nhất theo (bigseller, shop, op)
        Assert.Equal("harvest", req.Op);
        Assert.Equal(("shop1", "Data", "m1", "h1", false), (req.ShopId, req.Sheet, req.MachineId, req.Hostname, req.Force));
    }

    [Theory]
    [InlineData(CoordOp.Scrape)]
    [InlineData(CoordOp.Import)]
    [InlineData(CoordOp.Update)]
    [InlineData(CoordOp.Rewrite)]
    public void LeaseRequest_OtherOpsUnchanged(CoordOp op)
    {
        var key = new CoordKey("bs1", "shop1", "Data", op);
        var req = HttpCoordinationHub.BuildLeaseRequest(key, "m1", "h1", true);
        Assert.Equal(new LeaseAcquireRequest(key.Id, "bs1", "shop1", "Data", op.ToString().ToLowerInvariant(), "m1", "h1", true), req);
    }

    // ── Store tiến độ / đích tách file ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProgressStore_HarvestUsesSeparateFile_AndDoesNotTouchScrape()
    {
        Assert.EndsWith("scrape-progress.json", ScrapeProgressStore.Shared.FilePath);
        Assert.EndsWith("harvest-progress.json", ScrapeProgressStore.Harvest.FilePath);
        Assert.NotSame(ScrapeProgressStore.Shared, ScrapeProgressStore.Harvest);

        var acc = "test-" + Guid.NewGuid().ToString("N");
        ScrapeProgressStore.Harvest.BeginFresh(acc, "Data", "acc", 10);
        ScrapeProgressStore.Harvest.MarkCompleted(acc, "Data", 2, 5);
        try
        {
            Assert.Null(ScrapeProgressStore.Shared.Find(acc, "Data"));
            ScrapeProgressStore.Shared.Load();   // đọc lại từ đĩa: file scrape cũng không có
            Assert.Null(ScrapeProgressStore.Shared.Find(acc, "Data"));
            Assert.NotNull(ScrapeProgressStore.Harvest.Find(acc, "Data"));
        }
        finally
        {
            ScrapeProgressStore.Harvest.Clear(acc, "Data");
        }
    }

    [Fact]
    public void TargetStore_HarvestUsesSeparateFile()
    {
        Assert.EndsWith("scrape-targets.json", ScrapeTargetConfigStore.Shared.FilePath);
        Assert.EndsWith("harvest-targets.json", ScrapeTargetConfigStore.Harvest.FilePath);

        // Store không có API xoá mục → giữ nguyên nội dung file trước test rồi trả lại (kể cả file chưa tồn tại).
        var path = ScrapeTargetConfigStore.Harvest.FilePath;
        var before = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var acc = "test-" + Guid.NewGuid().ToString("N");
        try
        {
            ScrapeTargetConfigStore.Harvest.Save(new ScrapeTargetConfig { AccountId = acc, StartRow = 7 });
            Assert.Null(ScrapeTargetConfigStore.Shared.Find(acc));
            Assert.Equal(7, ScrapeTargetConfigStore.Harvest.Find(acc)?.StartRow);
        }
        finally
        {
            if (before is null) File.Delete(path);
            else File.WriteAllBytes(path, before);
            ScrapeTargetConfigStore.Harvest.Load();
        }
        Assert.Null(ScrapeTargetConfigStore.Harvest.Find(acc));
    }

    // ── Lỗi lưu dữ liệu = dừng job (không phải lỗi tk) ──────────────────────────────────────────────────────────

    [Fact]
    public void SinkFailureMessage_IsRecognized()
    {
        var msg = HarvestSinkFailure.Message(12, new IOException("disk full"));
        Assert.StartsWith(HarvestSinkFailure.Prefix, msg);
        Assert.Contains("disk full", msg);
        Assert.True(HarvestSinkFailure.Is(msg));
        Assert.False(HarvestSinkFailure.Is("Lỗi proxy"));
        Assert.False(HarvestSinkFailure.Is(null));
        Assert.True(HarvestSinkFailure.Is(HarvestSinkFailure.NotReadyMessage(new IOException("db locked"))));
    }

    [Fact]
    public void SinkFailure_ShortReason_OneLineMax120()
    {
        var longMsg = HarvestSinkFailure.Message(3, new IOException("SQLite Error 13: 'database or disk is full'.\r\n   at Microsoft.Data.Sqlite.SqliteException.ThrowExceptionForRC\n" + new string('x', 300)));
        var shortReason = HarvestSinkFailure.ShortReason(longMsg);
        Assert.True(shortReason.Length <= HarvestSinkFailure.MaxHubReasonLength);
        Assert.DoesNotContain('\n', shortReason);
        Assert.DoesNotContain('\r', shortReason);
        Assert.StartsWith(HarvestSinkFailure.Prefix, shortReason);
        Assert.Equal("Lưu dữ liệu lỗi: ngắn", HarvestSinkFailure.ShortReason("  Lưu dữ liệu lỗi: ngắn \n"));
    }
}
