using Shopee.Core.BigSeller;
using Shopee.Core.Coordination;
using Shopee.Core.Progress;

namespace Shopee.Core.Tests;

/// <summary>
/// <see cref="ImportScanPolicy.DecideClearGate"/>: Clear nháp xoá nháp của CẢ tài khoản, nên mọi khoá còn SP đã import mà chưa Update xong
/// (<see cref="OpProgressStore.FindImportKeysPendingUpdate"/>) phải biết mục tiêu Update thì mới được Clear. Lỗi cũ: chỉ đọc mục tiêu khi shop
/// đang chạy có SP chờ Update, và khoá không đọc được vẫn bị Clear → nháp shop khác mất mà "✓ Import" của nó còn nguyên.
/// </summary>
public sealed class ClearGateTests
{
    private const string Acc = "bs0000000000000000000000000000a1";

    private static OpProgress Make(string sheet, string op, string status, params string[] ids)
    {
        var p = new OpProgress { AccountId = Acc, Sheet = sheet, Op = op, Status = status };
        foreach (var id in ids) p.Done[id] = op == AssignmentOps.Update ? "tên" : null;
        return p;
    }

    private static IReadOnlyList<string> Pending(params OpProgress[] items) => OpProgressStore.FindImportKeysPendingUpdate(items, Acc);

    [Fact]
    public void NoPendingKeys_CanClear_EvenWithNothingRead()
    {
        var gate = ImportScanPolicy.DecideClearGate(Array.Empty<string>(), Array.Empty<string>());

        Assert.True(gate.CanClear);
        Assert.Empty(gate.UnknownKeys);
    }

    [Fact]
    public void CaseA_ImportS1ThenS2BeforeUpdateS1_KeyOfS1MustBeRead()
    {
        // S1 đã import 3 SP nhưng chưa Update; shop đang chạy là S2 (không có SP chờ Update) → khoá chờ Update chỉ là S1.
        var pending = Pending(
            Make("S1", AssignmentOps.Import, LedgerStatus.Completed, "1", "2", "3"),
            Make("S2", AssignmentOps.Import, LedgerStatus.Stopped));
        Assert.Equal(new[] { "S1" }, pending);

        Assert.True(ImportScanPolicy.DecideClearGate(pending, new[] { "S1" }).CanClear);          // đọc được mục tiêu của S1 → Clear
        var notRead = ImportScanPolicy.DecideClearGate(pending, Array.Empty<string>());           // lỗi cũ: không đọc khoá S1 vì S2 không có gì chờ
        Assert.False(notRead.CanClear);
        Assert.Equal(new[] { "S1" }, notRead.UnknownKeys);
        Assert.False(ImportScanPolicy.DecideClearGate(pending, new[] { "S2" }).CanClear);         // chỉ đọc khoá của shop đang chạy cũng không đủ
    }

    [Fact]
    public void CaseB_UpdateS1StoppedMidway_KeyOfS1IsPending()
    {
        var pending = Pending(
            Make("S1", AssignmentOps.Import, LedgerStatus.Completed, "1", "2", "3"),
            Make("S1", AssignmentOps.Update, LedgerStatus.Stopped, "1"));
        Assert.Equal(new[] { "S1" }, pending);

        Assert.True(ImportScanPolicy.DecideClearGate(pending, new[] { "S1" }).CanClear);
        Assert.False(ImportScanPolicy.DecideClearGate(pending, Array.Empty<string>()).CanClear);
    }

    [Fact]
    public void UpdateCompleted_KeyNotPending_CanClearWithoutTargets()
    {
        var pending = Pending(
            Make("S1", AssignmentOps.Import, LedgerStatus.Completed, "1", "2", "3"),
            Make("S1", AssignmentOps.Update, LedgerStatus.Completed, "1"));

        Assert.Empty(pending);
        Assert.True(ImportScanPolicy.DecideClearGate(pending, Array.Empty<string>()).CanClear);
    }

    [Fact]
    public void SharedKeyOfOtherShopPending_CannotClear_OwnSharedKeyKnownViaDataSheetIsFine()
    {
        // Workbook chung: khoá là shared:{shopId}. Khoá của shop đang chạy (shared:s2) đọc được theo DataSheet; khoá shared:s1 của shop khác thì không.
        var pending = Pending(
            Make("shared:s1", AssignmentOps.Import, LedgerStatus.Completed, "1", "2"),
            Make("shared:s2", AssignmentOps.Import, LedgerStatus.Stopped, "9"));
        Assert.Equal(new[] { "shared:s1", "shared:s2" }, pending);

        var gate = ImportScanPolicy.DecideClearGate(pending, new[] { "shared:s2" });

        Assert.False(gate.CanClear);
        Assert.Equal(new[] { "shared:s1" }, gate.UnknownKeys);
    }

    [Fact]
    public void ReadFailure_NoKeyKnown_CannotClear_ListsAllPendingKeysInOrder()
    {
        var gate = ImportScanPolicy.DecideClearGate(new[] { "S1", "S2", "S3" }, Array.Empty<string>());   // file đọc lỗi → từ điển mục tiêu rỗng

        Assert.False(gate.CanClear);
        Assert.Equal(new[] { "S1", "S2", "S3" }, gate.UnknownKeys);
    }

    [Fact]
    public void KeyMatching_IsCaseInsensitive_IgnoresBlankAndDuplicates()
    {
        var gate = ImportScanPolicy.DecideClearGate(new[] { "s1", "", "S2", "S2", "s2", null }, new[] { "S1", "  ", null });

        Assert.False(gate.CanClear);
        Assert.Equal(new[] { "S2" }, gate.UnknownKeys);
        Assert.True(ImportScanPolicy.DecideClearGate(new[] { "s1" }, new[] { "S1" }).CanClear);
    }
}
