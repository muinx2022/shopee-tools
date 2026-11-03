using Shopee.Core.Coordination;
using Shopee.Core.Progress;

namespace Shopee.Core.Tests;

/// <summary>
/// <see cref="OpProgressStore.RemoveImportWithoutUpdate"/> (phần thuần của ResetImportWithoutUpdate): sau khi Clear nháp, mọi SP ở Import
/// Done mà chưa có Update Done (cùng khoá) bị bỏ để import lại; Update Done và tài khoản khác giữ nguyên.
/// </summary>
public sealed class OpProgressImportResetTests
{
    private const string Acc = "bs0000000000000000000000000000a1";
    private const string Other = "bs0000000000000000000000000000b2";

    private static OpProgress Make(string acc, string sheet, string op, params string[] ids)
    {
        var p = new OpProgress { AccountId = acc, Sheet = sheet, Op = op, MinRow = 2, MaxRow = 9 };
        foreach (var id in ids) p.Done[id] = op == AssignmentOps.Update ? "tên" : null;
        return p;
    }

    private static OpProgress MakeWith(string acc, string sheet, string op, string status, params string[] ids)
    {
        var p = Make(acc, sheet, op, ids);
        p.Status = status;
        return p;
    }

    // Cách WorkspaceShopViewModel.IsOpDone xét "Import xong": BẤT KỲ khoá ứng viên nào của shop ở trạng thái completed.
    private static bool ImportDoneLikeShopViewModel(IEnumerable<OpProgress> items, string acc, params string[] candidateSheets) =>
        items.Any(p => p.AccountId == acc && p.Op == AssignmentOps.Import && p.Status == LedgerStatus.Completed
                       && candidateSheets.Any(s => string.Equals(s, p.Sheet, StringComparison.OrdinalIgnoreCase)));

    // Coi MỌI id đang ở Import Done là mục tiêu Update (cách các test cũ vẫn đúng: chỉ xét Update Done / tài khoản / trạng thái).
    private static Dictionary<string, IReadOnlySet<string>> AllImportIdsAreTargets(IEnumerable<OpProgress> items)
    {
        var d = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in items.Where(p => p.Op == AssignmentOps.Import))
        {
            var set = d.TryGetValue(p.Sheet, out var s) ? (HashSet<string>)s : new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in p.Done.Keys) set.Add(id);
            d[p.Sheet] = set;
        }
        return d;
    }

    private static List<ImportResetSheet> Remove(IReadOnlyList<OpProgress> items, IReadOnlyCollection<string>? shopKeys = null) =>
        OpProgressStore.RemoveImportWithoutUpdate(items, Acc, AllImportIdsAreTargets(items), out _, shopKeys);

    private static Dictionary<string, IReadOnlySet<string>> Targets(params (string Key, string[] Ids)[] entries) =>
        entries.ToDictionary(e => e.Key, e => (IReadOnlySet<string>)new HashSet<string>(e.Ids, StringComparer.Ordinal), StringComparer.Ordinal);

    private static string[] Keys(OpProgress p) => p.Done.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    [Fact]
    public void ImportOnlyIdsRemoved_IdsWithUpdateDoneKept()
    {
        var imp = Make(Acc, "S1", AssignmentOps.Import, "1", "2", "3", "4");
        var upd = Make(Acc, "S1", AssignmentOps.Update, "2", "4");
        var items = new List<OpProgress> { imp, upd };

        var res = Remove(items);

        Assert.Equal(new[] { "2", "4" }, Keys(imp));
        Assert.Equal(new[] { "2", "4" }, Keys(upd));   // Update Done không bị đụng
        var only = Assert.Single(res);
        Assert.Equal(new ImportResetSheet("S1", 2), only);
    }

    [Fact]
    public void MultipleKeys_EachPairedWithItsOwnUpdate()
    {
        var imp1 = Make(Acc, "S1", AssignmentOps.Import, "1", "2");
        var upd1 = Make(Acc, "S1", AssignmentOps.Update, "1");
        var imp2 = Make(Acc, "S2", AssignmentOps.Import, "1", "9");   // id "1" có Update ở S1 nhưng KHÔNG ở S2 → bỏ ở S2
        var upd2 = Make(Acc, "S2", AssignmentOps.Update, "9");
        var imp3 = Make(Acc, "S3", AssignmentOps.Import, "7");        // không có khoá Update nào → bỏ hết
        var items = new List<OpProgress> { imp1, upd1, imp2, upd2, imp3 };

        var res = Remove(items);

        Assert.Equal(new[] { "1" }, Keys(imp1));
        Assert.Equal(new[] { "9" }, Keys(imp2));
        Assert.Empty(imp3.Done);
        Assert.Equal(3, res.Sum(r => r.Removed));
        Assert.Equal(new[] { "S1", "S2", "S3" }, res.Select(r => r.Sheet).OrderBy(s => s).ToArray());
    }

    [Fact]
    public void OtherAccount_Untouched()
    {
        var mine = Make(Acc, "S1", AssignmentOps.Import, "1");
        var theirs = Make(Other, "S1", AssignmentOps.Import, "1", "2");
        var theirsUpd = Make(Other, "S1", AssignmentOps.Update);

        var res = Remove(new List<OpProgress> { mine, theirs, theirsUpd });

        Assert.Empty(mine.Done);
        Assert.Equal(new[] { "1", "2" }, Keys(theirs));
        Assert.Equal(1, res.Sum(r => r.Removed));
    }

    [Fact]
    public void UpdateOfOtherAccountSameSheet_DoesNotProtectMyImport()
    {
        var mine = Make(Acc, "S1", AssignmentOps.Import, "1");
        var theirsUpd = Make(Other, "S1", AssignmentOps.Update, "1");

        Remove(new List<OpProgress> { mine, theirsUpd });

        Assert.Empty(mine.Done);
        Assert.Equal(new[] { "1" }, Keys(theirsUpd));
    }

    [Fact]
    public void SheetMatchIgnoresCase_LikeKeyEq()
    {
        var imp = Make(Acc, "Sheet A", AssignmentOps.Import, "1", "2");
        var upd = Make(Acc, "SHEET a", AssignmentOps.Update, "1");

        Remove(new List<OpProgress> { imp, upd });

        Assert.Equal(new[] { "1" }, Keys(imp));
    }

    [Fact]
    public void AllImportsHaveUpdate_ChangesNothing_ReturnsEmpty()
    {
        var imp = Make(Acc, "S1", AssignmentOps.Import, "1", "2");
        var upd = Make(Acc, "S1", AssignmentOps.Update, "1", "2", "3");

        var res = Remove(new List<OpProgress> { imp, upd });

        Assert.Empty(res);
        Assert.Equal(new[] { "1", "2" }, Keys(imp));
    }

    [Fact]
    public void EmptyList_OrNoKeysForAccount_OrEmptyImportDone_ReturnsEmpty()
    {
        Assert.Empty(Remove(new List<OpProgress>()));
        Assert.Empty(Remove(new List<OpProgress> { Make(Other, "S1", AssignmentOps.Import, "1") }));
        Assert.Empty(Remove(new List<OpProgress> { Make(Acc, "S1", AssignmentOps.Import) }));
    }

    // ── N1: khoá bị bỏ id thì không còn "completed" ────────────────────────────────────────

    [Fact]
    public void CompletedImportKeyWithRemovedIds_BecomesStopped()
    {
        var imp = MakeWith(Acc, "S1", AssignmentOps.Import, LedgerStatus.Completed, "1", "2");
        var upd = MakeWith(Acc, "S1", AssignmentOps.Update, LedgerStatus.Running, "1");
        var items = new List<OpProgress> { imp, upd };

        Remove(items);

        Assert.Equal(LedgerStatus.Stopped, imp.Status);
        Assert.Equal(LedgerStatus.Running, upd.Status);   // Update không bị đụng
        Assert.False(ImportDoneLikeShopViewModel(items, Acc, "S1"));
    }

    [Fact]
    public void CompletedImportKeyNothingRemoved_StaysCompleted()
    {
        var imp = MakeWith(Acc, "S1", AssignmentOps.Import, LedgerStatus.Completed, "1", "2");
        var upd = MakeWith(Acc, "S1", AssignmentOps.Update, LedgerStatus.Completed, "1", "2");

        var res = Remove(new List<OpProgress> { imp, upd });

        Assert.Empty(res);
        Assert.Equal(LedgerStatus.Completed, imp.Status);
    }

    [Theory]
    [InlineData(LedgerStatus.Running)]
    [InlineData(LedgerStatus.Stopped)]
    [InlineData(LedgerStatus.Idle)]
    public void NonCompletedStatus_IsLeftAsIs(string status)
    {
        // Runner đang chạy: khoá shop đang ở "running" lúc Clear, FinishRun sẽ ghi trạng thái cuối — không đổi sang stopped ở đây.
        var imp = MakeWith(Acc, "S1", AssignmentOps.Import, status, "1");

        Remove(new List<OpProgress> { imp });

        Assert.Equal(status, imp.Status);
        Assert.Empty(imp.Done);
    }

    [Fact]
    public void ShopCandidateKeys_AllStopped_SoShopIsNoLongerImportDone()
    {
        // Shop có khoá chính "S1" (bị bỏ id) và khoá ứng viên "Tên shop" completed nhưng KHÔNG có id nào (vd Next shop/đánh dấu tay):
        // nếu chỉ dừng "S1", UI vẫn thấy "Tên shop" completed → ImportDone = true oan.
        var main = MakeWith(Acc, "S1", AssignmentOps.Import, LedgerStatus.Completed, "1", "2");
        var alt = MakeWith(Acc, "Tên shop", AssignmentOps.Import, LedgerStatus.Completed);
        var items = new List<OpProgress> { main, alt };
        var candidates = new[] { "S1", "Tên shop" };

        Assert.True(ImportDoneLikeShopViewModel(items, Acc, candidates));   // trước reset: đang "xong"

        Remove(items, candidates);

        Assert.Equal(LedgerStatus.Stopped, main.Status);
        Assert.Equal(LedgerStatus.Stopped, alt.Status);
        Assert.False(ImportDoneLikeShopViewModel(items, Acc, candidates));
    }

    [Fact]
    public void WithoutShopKeys_SiblingKeyWithoutIds_IsNotGuessed()
    {
        // Store không suy ra được quan hệ khoá ↔ shop: không có shopKeys thì khoá không bị bỏ id không bị đổi.
        var main = MakeWith(Acc, "S1", AssignmentOps.Import, LedgerStatus.Completed, "1");
        var alt = MakeWith(Acc, "Tên shop", AssignmentOps.Import, LedgerStatus.Completed);

        Remove(new List<OpProgress> { main, alt });

        Assert.Equal(LedgerStatus.Stopped, main.Status);
        Assert.Equal(LedgerStatus.Completed, alt.Status);
    }

    [Fact]
    public void ShopKeys_NoIdRemovedFromShopKeys_SiblingsUntouched()
    {
        // id bị bỏ nằm ở shop KHÁC (S2); shop đang chạy (S1 + "Tên shop") không mất id nào → giữ completed.
        var s1 = MakeWith(Acc, "S1", AssignmentOps.Import, LedgerStatus.Completed, "1");
        var s1Upd = MakeWith(Acc, "S1", AssignmentOps.Update, LedgerStatus.Completed, "1");
        var alt = MakeWith(Acc, "Tên shop", AssignmentOps.Import, LedgerStatus.Completed);
        var s2 = MakeWith(Acc, "S2", AssignmentOps.Import, LedgerStatus.Completed, "9");

        Remove(new List<OpProgress> { s1, s1Upd, alt, s2 }, new[] { "S1", "Tên shop" });

        Assert.Equal(LedgerStatus.Completed, s1.Status);
        Assert.Equal(LedgerStatus.Completed, alt.Status);
        Assert.Equal(LedgerStatus.Stopped, s2.Status);   // khoá bị bỏ id luôn về stopped
    }

    [Fact]
    public void ShopKeys_OtherAccountAndOtherShopsKeys_Untouched()
    {
        var main = MakeWith(Acc, "S1", AssignmentOps.Import, LedgerStatus.Completed, "1");
        var otherShopManual = MakeWith(Acc, "S3", AssignmentOps.Import, LedgerStatus.Completed);   // shop khác, "Next shop": không đụng
        var otherAcc = MakeWith(Other, "Tên shop", AssignmentOps.Import, LedgerStatus.Completed);
        var updSameName = MakeWith(Acc, "Tên shop", AssignmentOps.Update, LedgerStatus.Completed);   // Update của shop: không đụng

        Remove(new List<OpProgress> { main, otherShopManual, otherAcc, updSameName }, new[] { "S1", "Tên shop" });

        Assert.Equal(LedgerStatus.Stopped, main.Status);
        Assert.Equal(LedgerStatus.Completed, otherShopManual.Status);
        Assert.Equal(LedgerStatus.Completed, otherAcc.Status);
        Assert.Equal(LedgerStatus.Completed, updSameName.Status);
    }

    [Fact]
    public void ScrapeAndUpdateKeys_AreNeverTouched()
    {
        var scrape = Make(Acc, "S1", AssignmentOps.Scrape, "1");
        var upd = Make(Acc, "S1", AssignmentOps.Update, "5");

        var res = Remove(new List<OpProgress> { scrape, upd });

        Assert.Empty(res);
        Assert.Equal(new[] { "1" }, Keys(scrape));
        Assert.Equal(new[] { "5" }, Keys(upd));
    }

    // ── 5e: Update completed, tập mục tiêu Update ───────────────────────────────────────────

    [Theory]
    [InlineData("completed")]
    [InlineData("COMPLETED")]
    public void UpdateCompleted_ImportIdsOfThatKeyAreKept_AndStatusUntouched(string updStatus)
    {
        // Shop đã xong (hoặc user bấm Next shop): id thiếu Update Done ở đó là dòng không phải mục tiêu → không được bỏ.
        var imp = MakeWith(Acc, "S1", AssignmentOps.Import, LedgerStatus.Completed, "1", "2", "3");
        var upd = MakeWith(Acc, "S1", AssignmentOps.Update, updStatus, "1");
        var items = new List<OpProgress> { imp, upd };

        var res = OpProgressStore.RemoveImportWithoutUpdate(items, Acc, Targets(("S1", new[] { "1", "2", "3" })), out var skipped);

        Assert.Empty(res);
        Assert.Empty(skipped);
        Assert.Equal(new[] { "1", "2", "3" }, Keys(imp));
        Assert.Equal(LedgerStatus.Completed, imp.Status);   // không bị bỏ id nên không về stopped
    }

    [Fact]
    public void UpdateCompletedOnOneKeyOnly_OtherKeyStillReset()
    {
        var s1 = Make(Acc, "S1", AssignmentOps.Import, "1", "2");
        var s1Upd = MakeWith(Acc, "S1", AssignmentOps.Update, LedgerStatus.Completed);
        var s2 = Make(Acc, "S2", AssignmentOps.Import, "7", "8");
        var s2Upd = MakeWith(Acc, "S2", AssignmentOps.Update, LedgerStatus.Stopped, "7");

        var res = OpProgressStore.RemoveImportWithoutUpdate(new List<OpProgress> { s1, s1Upd, s2, s2Upd }, Acc,
            Targets(("S1", new[] { "1", "2" }), ("S2", new[] { "7", "8" })), out _);

        Assert.Equal(new[] { "1", "2" }, Keys(s1));
        Assert.Equal(new[] { "7" }, Keys(s2));
        Assert.Equal(new ImportResetSheet("S2", 1), Assert.Single(res));
    }

    [Fact]
    public void OnlyTargetIds_NotInUpdateDone_AreRemoved()
    {
        // 1 = mục tiêu + chưa Update → bỏ; 2 = mục tiêu + đã Update → giữ; 3, 4 = KHÔNG phải mục tiêu (tên đã sửa trống) → giữ.
        var imp = Make(Acc, "S1", AssignmentOps.Import, "1", "2", "3", "4");
        var upd = Make(Acc, "S1", AssignmentOps.Update, "2");

        var res = OpProgressStore.RemoveImportWithoutUpdate(new List<OpProgress> { imp, upd }, Acc,
            Targets(("S1", new[] { "1", "2", "9" })), out var skipped);

        Assert.Equal(new[] { "2", "3", "4" }, Keys(imp));
        Assert.Equal(new ImportResetSheet("S1", 1), Assert.Single(res));
        Assert.Empty(skipped);
    }

    [Fact]
    public void TargetSetEmpty_RemovesNothing_NotListedAsSkipped()
    {
        // Sheet đọc được nhưng không dòng nào là mục tiêu: có trong từ điển (tập rỗng) → không bỏ gì, và cũng không phải "bị bỏ qua".
        var imp = Make(Acc, "S1", AssignmentOps.Import, "1", "2");

        var res = OpProgressStore.RemoveImportWithoutUpdate(new List<OpProgress> { imp }, Acc, Targets(("S1", Array.Empty<string>())), out var skipped);

        Assert.Empty(res);
        Assert.Empty(skipped);
        Assert.Equal(new[] { "1", "2" }, Keys(imp));
    }

    [Fact]
    public void KeyMissingFromTargets_KeepsIds_AndIsReportedSkipped()
    {
        var s1 = Make(Acc, "S1", AssignmentOps.Import, "1", "2");
        var shared = Make(Acc, "shared:abc", AssignmentOps.Import, "5");
        var s3 = Make(Acc, "S3", AssignmentOps.Import, "7");
        var s3Upd = Make(Acc, "S3", AssignmentOps.Update, "7");   // không thiếu Update → không liệt kê "bỏ qua"

        var res = OpProgressStore.RemoveImportWithoutUpdate(new List<OpProgress> { s1, shared, s3, s3Upd }, Acc,
            Targets(("S1", new[] { "1" })), out var skipped);

        Assert.Equal(new[] { "2" }, Keys(s1));
        Assert.Equal(new[] { "5" }, Keys(shared));
        Assert.Equal(new[] { "shared:abc" }, skipped.ToArray());
        Assert.Equal(1, res.Sum(r => r.Removed));
    }

    [Fact]
    public void NullTargets_RemovesNothing_AllPendingKeysSkipped()
    {
        var imp = MakeWith(Acc, "S1", AssignmentOps.Import, LedgerStatus.Completed, "1");

        var res = OpProgressStore.RemoveImportWithoutUpdate(new List<OpProgress> { imp }, Acc, null, out var skipped);

        Assert.Empty(res);
        Assert.Equal(new[] { "S1" }, skipped.ToArray());
        Assert.Equal(new[] { "1" }, Keys(imp));
        Assert.Equal(LedgerStatus.Completed, imp.Status);
    }

    [Fact]
    public void TargetsLookup_IgnoresCaseOfKey()
    {
        var imp = Make(Acc, "Sheet A", AssignmentOps.Import, "1", "2");

        OpProgressStore.RemoveImportWithoutUpdate(new List<OpProgress> { imp }, Acc, Targets(("SHEET a", new[] { "1" })), out var skipped);

        Assert.Equal(new[] { "2" }, Keys(imp));
        Assert.Empty(skipped);
    }

    [Fact]
    public void ShopKeysStopped_OnlyWhenARealIdWasRemoved()
    {
        // Khoá shop S1 không có trong từ điển (id giữ nguyên) → không "chạm" shop, khoá ứng viên "Tên shop" vẫn completed.
        var main = MakeWith(Acc, "S1", AssignmentOps.Import, LedgerStatus.Completed, "1");
        var alt = MakeWith(Acc, "Tên shop", AssignmentOps.Import, LedgerStatus.Completed);
        var other = MakeWith(Acc, "S2", AssignmentOps.Import, LedgerStatus.Completed, "9");

        OpProgressStore.RemoveImportWithoutUpdate(new List<OpProgress> { main, alt, other }, Acc, Targets(("S2", new[] { "9" })), out _,
            new[] { "S1", "Tên shop" });

        Assert.Equal(LedgerStatus.Completed, main.Status);
        Assert.Equal(LedgerStatus.Completed, alt.Status);
        Assert.Equal(LedgerStatus.Stopped, other.Status);
    }

    [Fact]
    public void CountImportRemovable_MatchesWhatResetWouldRemove()
    {
        var imp = Make(Acc, "S1", AssignmentOps.Import, "1", "2", "3", "4");
        var upd = Make(Acc, "S1", AssignmentOps.Update, "2");
        var items = new List<OpProgress> { imp, upd };
        IReadOnlySet<string> targets = new HashSet<string> { "1", "2", "3" };

        Assert.Equal(2, OpProgressStore.CountImportRemovable(items, Acc, "s1", targets));       // 1, 3 (2 đã Update; 4 ngoài mục tiêu)
        Assert.Equal(0, OpProgressStore.CountImportRemovable(items, Acc, "S1", null));          // không biết mục tiêu → reset không bỏ gì
        Assert.Equal(0, OpProgressStore.CountImportRemovable(items, Acc, "S9", targets));       // không có khoá
        Assert.Equal(0, OpProgressStore.CountImportRemovable(items, Other, "S1", targets));     // tài khoản khác

        upd.Status = LedgerStatus.Completed;
        Assert.Equal(0, OpProgressStore.CountImportRemovable(items, Acc, "S1", targets));       // Update completed → giữ nguyên
    }

    [Fact]
    public void FindImportKeysPendingUpdate_ListsOnlyKeysWithMissingUpdateAndUpdateNotCompleted()
    {
        var a = Make(Acc, "A", AssignmentOps.Import, "1", "2");
        var aUpd = Make(Acc, "A", AssignmentOps.Update, "1");                                     // thiếu "2" → liệt kê
        var b = Make(Acc, "B", AssignmentOps.Import, "1");
        var bUpd = Make(Acc, "B", AssignmentOps.Update, "1");                                     // đủ → không
        var c = Make(Acc, "C", AssignmentOps.Import, "1");
        var cUpd = MakeWith(Acc, "C", AssignmentOps.Update, LedgerStatus.Completed);              // Update completed → không
        var d = Make(Acc, "D", AssignmentOps.Import, "1");                                        // không có khoá Update → liệt kê
        var e = Make(Acc, "E", AssignmentOps.Import);                                             // Import Done rỗng → không
        var f = Make(Other, "F", AssignmentOps.Import, "1");                                      // tài khoản khác → không
        var g = Make(Acc, "A", AssignmentOps.Scrape, "1");                                        // không phải Import → không

        var keys = OpProgressStore.FindImportKeysPendingUpdate(new List<OpProgress> { a, aUpd, b, bUpd, c, cUpd, d, e, f, g }, Acc);

        Assert.Equal(new[] { "A", "D" }, keys.ToArray());
    }
}
