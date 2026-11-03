using Shopee.Core.Coordination;
using Shopee.Core.Progress;

namespace Shopee.Core.Tests;

/// <summary>
/// <see cref="OpProgressStore.ApplyDone"/> (phần thuần của MarkDone): Import thêm id MỚI vào khoá mà Update cùng khoá đang "completed" thì Update đó về
/// "stopped"; id đã có, op Update, Update chưa completed, khoá/tài khoản khác thì không đụng.
/// </summary>
public sealed class OpProgressImportDemoteUpdateTests
{
    private const string Acc = "bs0000000000000000000000000000a1";
    private const string Other = "bs0000000000000000000000000000b2";

    private static OpProgress Make(string acc, string sheet, string op, string status, params string[] ids)
    {
        var p = new OpProgress { AccountId = acc, Sheet = sheet, Op = op, Status = status };
        foreach (var id in ids) p.Done[id] = op == AssignmentOps.Update ? "tên" : null;
        return p;
    }

    private static List<KeyValuePair<string, string?>> Ids(params string[] ids) => ids.Select(i => new KeyValuePair<string, string?>(i, null)).ToList();

    [Fact]
    public void ImportAddsNewId_UpdateCompletedSameKey_GoesStopped_CaseInsensitiveSheet()
    {
        var imp = Make(Acc, "Shop A", AssignmentOps.Import, LedgerStatus.Completed, "1", "2");
        var upd = Make(Acc, "shop a", AssignmentOps.Update, LedgerStatus.Completed, "1", "2");

        OpProgressStore.ApplyDone(new[] { imp, upd }, imp, Ids("3"), 5, 9);

        Assert.Equal(LedgerStatus.Stopped, upd.Status);
        Assert.Equal(new[] { "1", "2", "3" }, imp.Done.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { "1", "2" }, upd.Done.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());   // Update Done không bị đụng
        Assert.Equal(5, imp.MinRow);
        Assert.Equal(9, imp.MaxRow);
    }

    [Fact]
    public void ImportRewritesExistingIds_OnlyNothingNew_UpdateStaysCompleted()
    {
        var imp = Make(Acc, "S1", AssignmentOps.Import, LedgerStatus.Completed, "1", "2");
        var upd = Make(Acc, "S1", AssignmentOps.Update, LedgerStatus.Completed, "1", "2");

        OpProgressStore.ApplyDone(new[] { imp, upd }, imp, Ids("1", "2"), 0, 0);
        OpProgressStore.ApplyDone(new[] { imp, upd }, imp, Ids(), 3, 4);        // chỉ cập nhật khoảng dòng
        OpProgressStore.ApplyDone(new[] { imp, upd }, imp, Ids(""), 0, 0);      // id rỗng bị bỏ qua

        Assert.Equal(LedgerStatus.Completed, upd.Status);
    }

    [Fact]
    public void UpdateOpAddingIds_NeverTouchesStatus()
    {
        var imp = Make(Acc, "S1", AssignmentOps.Import, LedgerStatus.Completed, "1", "2", "3");
        var upd = Make(Acc, "S1", AssignmentOps.Update, LedgerStatus.Completed, "1");

        OpProgressStore.ApplyDone(new[] { imp, upd }, upd, new List<KeyValuePair<string, string?>> { new("2", "tên 2") }, 0, 0);

        Assert.Equal(LedgerStatus.Completed, upd.Status);
        Assert.Equal(LedgerStatus.Completed, imp.Status);
    }

    [Fact]
    public void OtherKeyOrOtherAccountOrNotCompleted_Untouched()
    {
        var imp = Make(Acc, "S1", AssignmentOps.Import, LedgerStatus.Completed, "1");
        var updRunning = Make(Acc, "S1", AssignmentOps.Update, LedgerStatus.Running, "1");
        var updOtherSheet = Make(Acc, "S2", AssignmentOps.Update, LedgerStatus.Completed, "1");
        var updOtherAcc = Make(Other, "S1", AssignmentOps.Update, LedgerStatus.Completed, "1");
        var items = new[] { imp, updRunning, updOtherSheet, updOtherAcc };

        OpProgressStore.ApplyDone(items, imp, Ids("9"), 0, 0);

        Assert.Equal(LedgerStatus.Running, updRunning.Status);
        Assert.Equal(LedgerStatus.Completed, updOtherSheet.Status);
        Assert.Equal(LedgerStatus.Completed, updOtherAcc.Status);
    }

    [Fact]
    public void NoUpdateRecord_NothingToDemote_ImportStillRecorded()
    {
        var imp = Make(Acc, "S1", AssignmentOps.Import, LedgerStatus.Idle);

        OpProgressStore.ApplyDone(new[] { imp }, imp, Ids("1"), 0, 0);

        Assert.Contains("1", imp.Done.Keys);
    }

    [Fact]
    public void SharedKeyDetection_PrefixOnly_CaseInsensitive()
    {
        Assert.True(OpProgressStore.IsSharedProgressKey("shared:abc"));
        Assert.True(OpProgressStore.IsSharedProgressKey("SHARED:abc"));
        Assert.False(OpProgressStore.IsSharedProgressKey("Sheet shared:abc"));
        Assert.False(OpProgressStore.IsSharedProgressKey(""));
        Assert.False(OpProgressStore.IsSharedProgressKey(null));
    }
}
