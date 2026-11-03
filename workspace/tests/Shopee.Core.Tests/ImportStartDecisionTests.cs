using Shopee.Core.BigSeller;
using Shopee.Core.Coordination;
using Shopee.Core.Progress;

namespace Shopee.Core.Tests;

/// <summary>
/// <see cref="ImportScanPolicy.DecideImportStart"/>: điều phối đầu lượt import một shop (thoát sớm / bị cổng Clear chặn / Clear), ghép với phần thuần của
/// <see cref="OpProgressStore"/> (khoá chờ Update, lọc khoá shared:... cũ, hạ Update completed khi Import thêm id mới). Các ca: A, A2, B, E, F.
/// </summary>
public sealed class ImportStartDecisionTests
{
    private const string Acc = "bs0000000000000000000000000000a1";
    private const string SharedOld = "shared:5ea7dd9350364a77a9ffb9e573af64a8";

    private static OpProgress Make(string sheet, string op, string status, params string[] ids)
    {
        var p = new OpProgress { AccountId = Acc, Sheet = sheet, Op = op, Status = status };
        foreach (var id in ids) p.Done[id] = op == AssignmentOps.Update ? "tên" : null;
        return p;
    }

    private static IReadOnlyList<string> Pending(IReadOnlyList<OpProgress> items, bool ignoreShared = false) =>
        OpProgressStore.FindImportKeysPendingUpdate(items, Acc, ignoreShared);

    // shopRemovable: số id Clear sẽ bỏ của khoá shop (0 nếu không biết); hubPending luôn false trừ test hub.
    private static ImportStartDecision Decide(
        int importIds, int imported, string shopKey, int shopRemovable, IReadOnlyCollection<string> pending, params string[] known) =>
        ImportScanPolicy.DecideImportStart(importIds, imported, false, shopKey, shopRemovable, pending, known);

    [Fact]
    public void NoItemIds_ExitsEarly_BeforeAnythingElse()
    {
        var d = Decide(0, 0, "S2", 0, new[] { "S1" });   // kể cả khi có khoá chưa đọc được

        Assert.Equal(ImportStartKind.ExitEarly, d.Kind);
        Assert.Equal(ImportExitReason.NoItemIds, d.ExitReason);
    }

    [Fact]
    public void NoItemIds_HubWithPendingUpdate_DoesNotExit()
    {
        // Hub đã lọc SP đã import nên tập id rỗng chưa chắc là hết việc: còn SP chờ Update thì vẫn phải đi qua Clear.
        Assert.True(ImportScanPolicy.NothingToImport(0, false));
        Assert.False(ImportScanPolicy.NothingToImport(0, true));
        Assert.False(ImportScanPolicy.NothingToImport(5, false));
        var d = ImportScanPolicy.DecideImportStart(0, 0, true, "S2", 0, Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal(ImportStartKind.Clear, d.Kind);
    }

    [Fact]
    public void CaseA_ImportS1ThenS2BeforeUpdateS1_RunningShopS2NotSeeded_NeedsKeyOfS1()
    {
        var items = new List<OpProgress>
        {
            Make("S1", AssignmentOps.Import, LedgerStatus.Completed, "1", "2", "3"),
            Make("S2", AssignmentOps.Import, LedgerStatus.Stopped),
        };
        var pending = Pending(items);
        Assert.Equal(new[] { "S1" }, pending);

        var blocked = Decide(5, 0, "S2", 0, pending);                // chưa đọc được mục tiêu của S1
        Assert.Equal(ImportStartKind.Blocked, blocked.Kind);
        Assert.Equal(new[] { "S1" }, blocked.BlockedKeys);
        Assert.Equal(ImportStartKind.Clear, Decide(5, 0, "S2", 0, pending, "S1").Kind);      // đọc được → Clear
        Assert.Equal(ImportStartKind.Blocked, Decide(5, 0, "S2", 0, pending, "S2").Kind);    // chỉ đọc khoá shop đang chạy là không đủ
    }

    [Fact]
    public void CaseA2_RunningShopHasNothingWaiting_ButOtherShopDoes()
    {
        var items = new List<OpProgress>
        {
            Make("S1", AssignmentOps.Import, LedgerStatus.Completed, "1", "2"),
            Make("S2", AssignmentOps.Import, LedgerStatus.Stopped, "9"),
            Make("S2", AssignmentOps.Update, LedgerStatus.Completed, "9"),
        };
        var pending = Pending(items);
        Assert.Equal(new[] { "S1" }, pending);   // S2 đã Update đủ → không chờ; S1 chờ

        // S2 chưa import đủ (5 id, mới 1): phải qua Clear, và Clear xoá nháp của S1 → cần mục tiêu của S1.
        Assert.Equal(ImportStartKind.Blocked, Decide(5, 1, "S2", 0, pending).Kind);
        Assert.Equal(ImportStartKind.Clear, Decide(5, 1, "S2", 0, pending, "S1").Kind);

        // S2 đã import đủ và không chờ gì: khỏi Clear, KHÔNG bị khoá của S1 làm vướng (S1 không đọc được cũng vẫn thoát sớm "xong").
        var done = Decide(1, 1, "S2", 0, pending);
        Assert.Equal(ImportStartKind.ExitEarly, done.Kind);
        Assert.Equal(ImportExitReason.AllDone, done.ExitReason);
    }

    [Fact]
    public void CaseB_UpdateS1StoppedMidway_RunningShopIsS1_Seeded()
    {
        var items = new List<OpProgress>
        {
            Make("S1", AssignmentOps.Import, LedgerStatus.Completed, "1", "2", "3"),
            Make("S1", AssignmentOps.Update, LedgerStatus.Stopped, "1"),
        };
        var pending = Pending(items);
        Assert.Equal(new[] { "S1" }, pending);

        // Đã import đủ 3/3 nhưng Update dừng giữa chừng: Clear sẽ bỏ id 2, 3 khỏi bảng import (removable = 2) → đi tiếp để import lại.
        Assert.Equal(ImportStartKind.Clear, Decide(3, 3, "S1", 2, pending, "S1").Kind);
        // Mục tiêu Update chỉ gồm id 1 (đã Update): Clear không bỏ gì → xong thật.
        var done = Decide(3, 3, "S1", 0, pending, "S1");
        Assert.Equal(ImportStartKind.ExitEarly, done.Kind);
        Assert.Equal(ImportExitReason.AllDone, done.ExitReason);
        // Không đọc được mục tiêu của chính khoá shop: KHÔNG được nói "mọi SP đã Update" — chặn và nêu khoá.
        var unknown = Decide(3, 3, "S1", 0, pending);
        Assert.Equal(ImportStartKind.Blocked, unknown.Kind);
        Assert.Equal(new[] { "S1" }, unknown.BlockedKeys);
    }

    [Fact]
    public void CaseE_OldSharedKey_WorkbookSharedOff_DoesNotBlock_OnDoesBlock()
    {
        // Tài khoản có khoá shared:... cũ (Import completed 37, không có Update), shop đang chạy dùng khoá tên sheet.
        var items = new List<OpProgress>
        {
            Make(SharedOld, AssignmentOps.Import, LedgerStatus.Completed, Enumerable.Range(1, 37).Select(i => i.ToString()).ToArray()),
            Make("S1", AssignmentOps.Import, LedgerStatus.Stopped),
        };

        var off = Pending(items, ignoreShared: true);       // workbook chung TẮT
        Assert.Empty(off);
        Assert.Equal(ImportStartKind.Clear, Decide(50, 0, "S1", 0, off).Kind);

        var on = Pending(items, ignoreShared: false);       // workbook chung BẬT: khoá chờ Update và không đọc được mục tiêu → chặn
        Assert.Equal(new[] { SharedOld }, on);
        var blocked = Decide(50, 0, "S1", 0, on);
        Assert.Equal(ImportStartKind.Blocked, blocked.Kind);
        Assert.Equal(new[] { SharedOld }, blocked.BlockedKeys);
        Assert.Equal(ImportStartKind.Clear, Decide(50, 0, "S1", 0, on, SharedOld).Kind);
    }

    [Fact]
    public void CaseE_OldSharedKey_NotTouchedByReset_AndNotReportedAsSkipped()
    {
        var shared = Make(SharedOld, AssignmentOps.Import, LedgerStatus.Completed, "1", "2", "3");
        var s1 = Make("S1", AssignmentOps.Import, LedgerStatus.Completed, "7", "8");
        var items = new List<OpProgress> { shared, s1 };
        var targets = new Dictionary<string, IReadOnlySet<string>> { ["S1"] = new HashSet<string> { "7", "8" } };

        var res = OpProgressStore.RemoveImportWithoutUpdate(items, Acc, targets, out var skipped, new[] { "S1" }, ignoreSharedKeys: true);

        Assert.Equal(new[] { "1", "2", "3" }, shared.Done.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());   // dữ liệu cũ nguyên vẹn
        Assert.Equal(LedgerStatus.Completed, shared.Status);
        Assert.Empty(skipped);                                    // không bị coi là "không biết mục tiêu" → Reset không trả false
        Assert.Equal(new ImportResetSheet("S1", 2), Assert.Single(res));

        // Đối chứng: workbook chung BẬT mà không biết mục tiêu của khoá shared → bị bỏ qua và báo (Reset sẽ trả false).
        var items2 = new List<OpProgress> { Make(SharedOld, AssignmentOps.Import, LedgerStatus.Completed, "1") };
        OpProgressStore.RemoveImportWithoutUpdate(items2, Acc, targets, out var skipped2, null, ignoreSharedKeys: false);
        Assert.Equal(new[] { SharedOld }, skipped2);
    }

    [Fact]
    public void CaseF_ShopAlreadyUpdated_ThenImportAddsNewIds_OtherShopClearMustKeepNewOnes()
    {
        // S1: Import {1,2} và Update {1,2} đều completed (đã ✓ Update). Chưa có gì chờ → khoá S1 không nằm trong cổng.
        var impS1 = Make("S1", AssignmentOps.Import, LedgerStatus.Completed, "1", "2");
        var updS1 = Make("S1", AssignmentOps.Update, LedgerStatus.Completed, "1", "2");
        var items = new List<OpProgress> { impS1, updS1, Make("S2", AssignmentOps.Import, LedgerStatus.Stopped) };
        Assert.Empty(Pending(items));
        Assert.Equal(ImportStartKind.Clear, Decide(5, 0, "S2", 0, Pending(items)).Kind);   // trước khi Import thêm: S2 Clear không vướng gì

        // Import thủ công thêm id 3 vào khoá S1 (khoá viết hoa khác): Update của S1 phải về stopped.
        OpProgressStore.ApplyDone(items, impS1, new List<KeyValuePair<string, string?>> { new("3", null) }, 0, 0);
        Assert.Equal(LedgerStatus.Stopped, updS1.Status);
        var pending = Pending(items);
        Assert.Equal(new[] { "S1" }, pending);

        // Shop S2 Clear: không biết mục tiêu của S1 → chặn (không mất SP 3); biết → Clear và reset chỉ bỏ id 3 (id 1, 2 đã có Update Done).
        Assert.Equal(ImportStartKind.Blocked, Decide(5, 0, "S2", 0, pending).Kind);
        Assert.Equal(ImportStartKind.Clear, Decide(5, 0, "S2", 0, pending, "S1").Kind);
        var targets = new Dictionary<string, IReadOnlySet<string>> { ["S1"] = new HashSet<string> { "1", "2", "3" } };
        var res = OpProgressStore.RemoveImportWithoutUpdate(items, Acc, targets, out var skipped, null);
        Assert.Empty(skipped);
        Assert.Equal(new ImportResetSheet("S1", 1), Assert.Single(res));
        Assert.Equal(new[] { "1", "2" }, impS1.Done.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void DescribeProgressKey_AddsShopNameWhenFoundById_ElseKeepsKey()
    {
        var shops = new[] { new BigSellerShop { Id = "5ea7dd9350364a77a9ffb9e573af64a8", Name = " Shop Hoang " }, new BigSellerShop { Id = "other", Name = "X" } };

        Assert.Equal(SharedOld + " (shop 'Shop Hoang')", ImportScanPolicy.DescribeProgressKey(SharedOld, shops));
        Assert.Equal("shared:zzz", ImportScanPolicy.DescribeProgressKey("shared:zzz", shops));        // không có shop theo id → giữ khoá (chứa id)
        Assert.Equal(SharedOld, ImportScanPolicy.DescribeProgressKey(SharedOld, null));
        Assert.Equal("Sheet A", ImportScanPolicy.DescribeProgressKey("Sheet A", shops));              // khoá tên sheet riêng
    }
}
