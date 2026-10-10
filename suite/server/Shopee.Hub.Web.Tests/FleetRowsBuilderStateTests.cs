using Shopee.Core.Coordination;
using Shopee.Hub.Web.Components;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// Test cho phần LỌC TRẠNG THÁI của danh sách trái trang BigSeller (chip Tất cả / Đang chạy / Dừng-lỗi /
/// Chưa xong): <see cref="FleetRowsBuilder.StateOf"/>, <see cref="FleetRowsBuilder.FilterByState"/>,
/// <see cref="FleetRowsBuilder.NormalizeState"/>.
///
/// <para>Vì sao có file này: bộ lọc chip là loại code hỏng ÂM THẦM — nó báo thành công rồi trả về tập thiếu,
/// không lỗi, không cảnh báo. Đợt 2026-08-14 đã lọt đúng một ca như thế (StateOf quét 3 việc trong khi cả
/// trang vẽ 4 việc ⇒ shop chỉ còn kẹt "Tên SP" biến mất khỏi chip "Chưa xong"), và toàn bộ 21 tiêu chí nghiệm
/// thu đo bằng trình duyệt KHÔNG phát hiện được, vì các con số đó đọc ra từ chính hàm bị lỗi.</para>
/// </summary>
public sealed class FleetRowsBuilderStateTests
{
    private const string Acc = "acc1";
    private const string Shop = "s1";

    private static FleetShopRow Row(string shopId = Shop, string name = "shop") =>
        new() { AccountId = Acc, ShopId = shopId, ShopName = name, Sheet = "sheet", InConfig = true };

    /// <summary>Snapshot chỉ có ledger — đủ để OpCell quyết trạng thái khi không có lease/assignment nào.</summary>
    private static FleetSnapshot Snap(params (string ShopId, string Op, string Status)[] ledger) => new()
    {
        Ledger = ledger.Select(l => new WorkLedgerRecord
        {
            Key = $"{Acc}__{l.ShopId}__{l.Op}",
            BigsellerId = Acc,
            ShopId = l.ShopId,
            Sheet = "sheet",
            Op = l.Op,
            Status = l.Status,
        }).ToList(),
    };

    // ── StateOf: phải hỏi ĐỦ 4 việc, không phải 3 ────────────────────────────────────────

    /// <summary>CA HỒI QUY của lỗi 2026-08-14. Shop xong scrape/import/update nhưng KẸT rewrite ("Tên SP").
    /// Trang vẫn vẽ nó là "■ dừng dở" ở cột Tên SP ⇒ chip "Dừng/lỗi" và "Chưa xong" PHẢI thấy nó.
    /// Nếu StateOf quay lại quét SummaryOps (3 việc) thì test này đỏ.</summary>
    [Fact]
    public void StateOf_ShopChiKetMoiRewrite_VanTinhLaDungLoiVaChuaXong()
    {
        var snap = Snap(
            (Shop, AssignmentOps.Scrape, LedgerStatus.Completed),
            (Shop, AssignmentOps.Import, LedgerStatus.Completed),
            (Shop, AssignmentOps.Update, LedgerStatus.Completed),
            (Shop, AssignmentOps.Rewrite, LedgerStatus.Stopped));

        var st = FleetRowsBuilder.StateOf(snap, Row());

        Assert.True(st.Stopped, "rewrite dừng dở phải làm shop rơi vào chip Dừng/lỗi");
        Assert.False(st.AllDone, "còn rewrite chưa xong thì KHÔNG được coi là xong hết");
    }

    [Fact]
    public void StateOf_XongCa4Viec_ThiAllDone()
    {
        var snap = Snap(
            (Shop, AssignmentOps.Scrape, LedgerStatus.Completed),
            (Shop, AssignmentOps.Import, LedgerStatus.Completed),
            (Shop, AssignmentOps.Update, LedgerStatus.Completed),
            (Shop, AssignmentOps.Rewrite, LedgerStatus.Completed));

        var st = FleetRowsBuilder.StateOf(snap, Row());

        Assert.True(st.AllDone);
        Assert.False(st.Stopped);
        Assert.False(st.Running);
    }

    [Fact]
    public void StateOf_ChuaChayGi_ThiChuaXongVaKhongPhaiDungLoi()
    {
        var st = FleetRowsBuilder.StateOf(Snap(), Row());

        Assert.False(st.AllDone);
        Assert.False(st.Stopped);
        Assert.False(st.Running);
    }

    // ── FilterByState: chip lọc ──────────────────────────────────────────────────────────

    private static List<FleetAcctGroup> OneGroup(params FleetShopRow[] shops) =>
        [new FleetAcctGroup(Acc, "acc1@mail.com", shops.ToList())];

    [Fact]
    public void FilterByState_ChipDungLoi_GiuDungShopKetRewrite()
    {
        var ketRewrite = Row("s1", "kẹt-rewrite");
        var xongHet = Row("s2", "xong-het");
        var snap = Snap(
            ("s1", AssignmentOps.Scrape, LedgerStatus.Completed),
            ("s1", AssignmentOps.Import, LedgerStatus.Completed),
            ("s1", AssignmentOps.Update, LedgerStatus.Completed),
            ("s1", AssignmentOps.Rewrite, LedgerStatus.Stopped),
            ("s2", AssignmentOps.Scrape, LedgerStatus.Completed),
            ("s2", AssignmentOps.Import, LedgerStatus.Completed),
            ("s2", AssignmentOps.Update, LedgerStatus.Completed),
            ("s2", AssignmentOps.Rewrite, LedgerStatus.Completed));

        var got = FleetRowsBuilder.FilterByState(OneGroup(ketRewrite, xongHet), "err", snap).ToList();

        var shop = Assert.Single(Assert.Single(got).Shops);
        Assert.Equal("kẹt-rewrite", shop.ShopName);
    }

    /// <summary>Acc CHƯA CÓ SHOP NÀO phải được giữ lại với mọi chip — nếu không thì bấm "+ Thêm tài khoản"
    /// trong lúc đang bật chip sẽ tạo ra acc mà danh sách trái không hề hiện.</summary>
    [Fact]
    public void FilterByState_AccKhongCoShop_VanDuocGiu()
    {
        var groups = new List<FleetAcctGroup> { new(Acc, "acc-moi@mail.com", []) };

        foreach (var chip in new[] { "run", "err", "todo" })
        {
            var got = FleetRowsBuilder.FilterByState(groups, chip, Snap()).ToList();
            Assert.True(got.Count == 1, $"chip '{chip}' đã lọc mất acc chưa có shop");
        }
    }

    [Fact]
    public void FilterByState_KhongShopNaoKhop_ThiNhomRungHan()
    {
        var snap = Snap(
            (Shop, AssignmentOps.Scrape, LedgerStatus.Completed),
            (Shop, AssignmentOps.Import, LedgerStatus.Completed),
            (Shop, AssignmentOps.Update, LedgerStatus.Completed),
            (Shop, AssignmentOps.Rewrite, LedgerStatus.Completed));

        var got = FleetRowsBuilder.FilterByState(OneGroup(Row()), "err", snap).ToList();

        Assert.Empty(got);
    }

    [Fact]
    public void FilterByState_ChipTatCa_TraNguyenVen()
    {
        var groups = OneGroup(Row("s1"), Row("s2"));

        var got = FleetRowsBuilder.FilterByState(groups, "", Snap()).ToList();

        Assert.Equal(2, Assert.Single(got).Shops.Count);
    }

    // ── NormalizeState: giá trị đến từ URL (?st=) nên phải chịu được rác ────────────────

    [Theory]
    [InlineData("run", "run")]
    [InlineData("err", "err")]
    [InlineData("todo", "todo")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("banana", "")]
    [InlineData("RUN", "")]
    public void NormalizeState_ChiNhanKeyCoThat(string? input, string expected) =>
        Assert.Equal(expected, FleetRowsBuilder.NormalizeState(input));

    /// <summary>Mọi key trong StateChips phải sống sót NormalizeState — thêm chip mới mà quên là chip đó
    /// im lặng không lọc gì.</summary>
    [Fact]
    public void NormalizeState_MoiKeyCuaStateChips_DeuHopLe()
    {
        foreach (var (key, _) in FleetRowsBuilder.StateChips)
            Assert.Equal(key, FleetRowsBuilder.NormalizeState(key));
    }

    // ── Summarize: câu tóm tắt KHÔNG được lặp thứ đã hiện ngay cạnh ────────────────────

    /// <summary>Câu tóm tắt nằm ngay cạnh các mục rời của dải thống kê và ngay dưới thẻ "Máy offline".
    /// Lặp lại "đang chạy"/"chờ"/"N máy" ở đó là ba con số hiện hai lần trong hai hàng liền nhau.</summary>
    [Fact]
    public void Summarize_CauTomTat_KhongLapDangChayChoVaSoMay()
    {
        var snap = Snap((Shop, AssignmentOps.Scrape, LedgerStatus.Completed));
        snap.Machines.Add(new MachinePresence { MachineId = "m1", Hostname = "PC1", LastSeen = DateTimeOffset.Now });

        var text = FleetRowsBuilder.Summarize([Row()], snap).Text;

        Assert.DoesNotContain("đang chạy", text);
        Assert.DoesNotContain(" chờ ", text);
        Assert.DoesNotContain(" máy ", text);
        // Phần PHẢI còn: dừng/lỗi + 4 tổng số dòng + mốc giờ.
        Assert.Contains("dừng/lỗi", text);
        Assert.Contains("scrape", text);
        Assert.Contains("tên SP", text);
        Assert.Contains("cập nhật", text);
    }
}
