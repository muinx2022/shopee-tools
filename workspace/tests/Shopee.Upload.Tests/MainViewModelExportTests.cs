using Shopee.Core.BigSeller;
using Shopee.Core.Coordination;
using Shopee.Core.MassUpload.Export;
using Shopee.Core.MassUpload.Images;
using Shopee.Core.MassUpload.Seller;
using Shopee.Upload.Forms;
using Shopee.Upload.Import;
using Shopee.Upload.Services;

namespace Shopee.Upload.Tests;

/// <summary>GĐ8 trên màn chính, với phần xuất / hộp thoại GIẢ: ẩn phần trình duyệt (kiểm không báo gì về proxy / đăng nhập, dữ liệu vẫn
/// lưu nguyên), lượt xuất lấy đúng giá trị trên form, huỷ, xoá ảnh của lượt có hỏi lại + khoá khi đang xuất, nhập shop điền file
/// Excel nguồn.</summary>
public sealed class MainViewModelExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "upload-vm-export-tests-" + Guid.NewGuid().ToString("N"));

    public MainViewModelExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string FilePath => Path.Combine(_dir, "seller-accounts.json");

    private sealed class FakeExport : IExportService
    {
        public string? HubProblem { get; set; }
        public string HubConfigPath => "hub.json";
        public List<MassUploadExportRequest> Requests { get; } = [];
        public List<string> Cleared { get; } = [];
        public List<ExportBatchInfo> Batches { get; } = [];
        public Func<MassUploadExportRequest, IProgress<ExportProgress>, CancellationToken, Task<MassUploadExportResult>>? OnExport { get; set; }
        public Dictionary<string, IReadOnlyList<string>> Sheets { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<MassUploadExportResult> ExportAsync(MassUploadExportRequest request, IProgress<ExportProgress> progress, CancellationToken ct)
        {
            Requests.Add(request);
            return OnExport?.Invoke(request, progress, ct) ?? Task.FromResult(Result(request.BatchId, [Path.Combine(request.OutputDirectory, "a_1.xlsx")]));
        }

        public Task<IReadOnlyList<ExportBatchInfo>> ListBatchesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExportBatchInfo>>(Batches.ToList());

        public Task<BatchClearResult> ClearBatchAsync(string batchId, CancellationToken ct = default)
        {
            Cleared.Add(batchId);
            return Task.FromResult(new BatchClearResult(3, 1, 1, 0));
        }

        public Task<IReadOnlyList<string>> ListSheetsAsync(string workbookPath, CancellationToken ct = default) =>
            Task.FromResult(Sheets.TryGetValue(workbookPath, out var s) ? s : throw new IOException("khoá"));

        public Task<string> DescribeTemplateAsync(string path, CancellationToken ct = default) => Task.FromResult("mô tả " + Path.GetFileName(path));

        public List<string> Retried { get; } = [];

        public Task<int> RetryFailedImagesAsync(string shopKey, CancellationToken ct = default)
        {
            Retried.Add(shopKey);
            return Task.FromResult(4);
        }

        public Queue<SourceImageStatsResponse> Stats { get; } = new();
        public SourceImageStatsResponse? LastStats;
        public int StatsCalls, DeleteCalls;
        public SourceImageDeleteAllResponse DeleteResult = new(0, 0, 0, "");

        /// <summary>Chặn lần đọc stats tới khi task này xong (test đang-xoá-thì-khoá-Xuất).</summary>
        public Task? StatsGate;

        public async Task<SourceImageStatsResponse> GetHubStorageAsync(CancellationToken ct = default)
        {
            if (StatsGate is { } gate) await gate;
            return await StatsCore();
        }

        private Task<SourceImageStatsResponse> StatsCore()
        {
            StatsCalls++;
            if (Stats.Count > 0) LastStats = Stats.Dequeue();
            return LastStats is { } st ? Task.FromResult(st) : throw new InvalidOperationException("Chưa nối Hub");
        }

        public Task<SourceImageDeleteAllResponse> DeleteAllSourceImagesAsync(CancellationToken ct = default)
        {
            DeleteCalls++;
            return Task.FromResult(DeleteResult);
        }

        public int BatchesUsingHub;
        public List<DateTimeOffset> Marked = [];

        public Task<int> CountBatchesUsingHubSourcesAsync(CancellationToken ct = default) => Task.FromResult(BatchesUsingHub);

        public Task<int> MarkHubSourcesDeletedAsync(DateTimeOffset at, CancellationToken ct = default)
        {
            Marked.Add(at);
            return Task.FromResult(BatchesUsingHub);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakePrompts : IExportPrompts
    {
        public string? Workbook;
        public List<string> TemplatesToPick = [];
        public bool ConfirmResult = true;
        public List<string> Confirms = [];
        public List<string> Opened = [];

        public string? PickWorkbook(string? current) => Workbook;
        public IReadOnlyList<string> PickTemplates() => TemplatesToPick;
        public string? PickImage(string? current) => null;
        public string? PickFolder(string? current) => null;

        public bool Confirm(string title, string message)
        {
            Confirms.Add(message);
            return ConfirmResult;
        }

        public bool TypedResult;
        public List<(string Message, string Word)> Typed = [];

        public bool ConfirmTyped(string title, string message, string requiredText)
        {
            Typed.Add((message, requiredText));
            return TypedResult;
        }

        public void OpenFolder(string path) => Opened.Add(path);
    }

    private sealed class NoImportPrompts : IImportPrompts
    {
        public bool ConfirmShops(ShopImportPlan plan) => true;
        public string? PickPasswordFile() => null;
        public OverwriteChoice AskOverwrite(int accounts, int fields) => OverwriteChoice.Cancel;
        public void ShowSummary(string title, string text, bool isError = false) { }
    }

    private static MassUploadExportResult Result(string batchId, IReadOnlyList<string> files) =>
        new(batchId, files, 2, 3, 0, [new ExportSourceLink(5, "https://shopee.vn/x-i.1.5")], [],
            [new ExportMissingTemplate(100011, [new ExportSourceRow(7, "https://shopee.vn/y-i.1.7", 7)])], [], [], [], [], 0, [], true);

    private MainViewModel NewVm(FakeExport export, FakePrompts prompts, Func<IReadOnlyList<BigSellerShopInfo>>? bs = null) =>
        new(new SellerAccountStore(FilePath, Path.Combine(_dir, "profiles")), a => a(), _ => true, null, new NoImportPrompts(), bs,
            export, prompts, showBrowserFeatures: false);

    /// <summary>File cấu hình có đủ thứ phần ẩn sẽ báo nếu còn kiểm: proxy tĩnh sai, mật khẩu thiếu tài khoản, nhịp chạy sai.</summary>
    private void WriteConfig()
    {
        var data = new SellerAccountsData
        {
            Settings = new SellerUploadSettings
            {
                Pacing = new SellerPacing { PauseBetweenShopsMinMinutes = 1 },
                SizeChartImagePath = @"D:\anh\size.jpg",
                ExportDirectory = @"D:\xuat",
            },
            Accounts =
            [
                new SellerAccount
                {
                    Id = "shop-1", Label = "Shop Một", ShopeePassword = "MatKhau#1",
                    ProxyOverride = new SellerProxyOverride { Kind = SellerProxyKind.Static, Static = "sai-dang" },
                    Source = new SellerSource { WorkbookPath = @"D:\nguon.xlsx", Sheet = "Áo", FromRow = 3, ToRow = 40 },
                    TemplatePaths = [@"D:\mau\100009.xlsx"],
                },
            ],
        };
        Assert.True(new SellerAccountStore(FilePath, Path.Combine(_dir, "profiles")).Save(data, browserChecks: false).Saved);
    }

    [Fact]
    public void AnPhanTrinhDuyet_KhongLoiKhongCanhBao_LuuGiuDuLieuAn()
    {
        WriteConfig();
        var vm = NewVm(new FakeExport(), new FakePrompts());

        Assert.False(vm.ShowBrowserFeatures);
        Assert.True(vm.ShowExportFeatures);
        Assert.Empty(vm.Errors);
        Assert.Empty(vm.Warnings);

        vm.Selected!.Label = "Shop Một mới";
        Assert.True(vm.TrySave());

        var d = new SellerAccountStore(FilePath, Path.Combine(_dir, "profiles")).Load();
        var a = d.Accounts.Single();
        Assert.Equal(("Shop Một mới", "MatKhau#1", "sai-dang"), (a.Label, a.ShopeePassword, a.ProxyOverride!.Static));
        Assert.Equal(1, d.Settings.Pacing.PauseBetweenShopsMinMinutes);
        Assert.Equal((@"D:\nguon.xlsx", "Áo", 3, 40), (a.Source.WorkbookPath, a.Source.Sheet, a.Source.FromRow, a.Source.ToRow));
        Assert.Equal([@"D:\mau\100009.xlsx"], a.TemplatePaths);
        Assert.Equal((@"D:\anh\size.jpg", @"D:\xuat"), (d.Settings.SizeChartImagePath, d.Settings.ExportDirectory));
    }

    [Fact]
    public async Task AnPhanTrinhDuyet_ChanCungMoBraveVaDangNhap()
    {
        WriteConfig();
        var factoryCalls = 0;
        var vm = new MainViewModel(new SellerAccountStore(FilePath, Path.Combine(_dir, "profiles")), a => a(), _ => true,
            (_, _) => { factoryCalls++; throw new InvalidOperationException("không được mở Brave"); },
            new NoImportPrompts(), null, new FakeExport(), new FakePrompts(), showBrowserFeatures: false);

        Assert.False(vm.OpenBraveCommand.CanExecute(null));
        Assert.False(vm.OpenAndLoginCommand.CanExecute(null));
        await vm.OpenBraveCommand.ExecuteAsync(null);
        await vm.OpenAndLoginCommand.ExecuteAsync(null);
        Assert.Equal(0, factoryCalls);
        Assert.False(vm.IsOpening);
    }

    [Fact]
    public void BatPhanTrinhDuyet_VanKiemNhuCu()
    {
        WriteConfig();
        var vm = new MainViewModel(new SellerAccountStore(FilePath, Path.Combine(_dir, "profiles")), a => a(), _ => true, null,
            new NoImportPrompts(), null, new FakeExport(), new FakePrompts());

        Assert.True(vm.ShowBrowserFeatures);
        Assert.Contains(vm.Errors, e => e.Contains("proxy tĩnh sai", StringComparison.Ordinal));
        Assert.Contains(vm.Errors, e => e.Contains("Nghỉ giữa 2 shop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Xuat_LayDungGiaTriTrenForm_HienKetQua_MoThuMuc()
    {
        WriteConfig();
        var export = new FakeExport();
        var prompts = new FakePrompts();
        var vm = NewVm(export, prompts);
        vm.Selected!.Sheet = "Quần";
        vm.Selected.ToRow = "0";
        vm.Selected.WeightGrams = "350";
        vm.Settings.ExportDirectory = @"D:\ra";

        await vm.ExportCommand.ExecuteAsync(null);

        var r = Assert.Single(export.Requests);
        Assert.Equal((vm.Selected.ImageKey, "Shop Một", @"D:\nguon.xlsx", "Quần", 3, 0), (r.ShopKey, r.ShopLabel, r.WorkbookPath, r.Sheet, r.FromRow, r.ToRow));
        Assert.Equal([@"D:\mau\100009.xlsx"], r.TemplatePaths);
        Assert.Equal((@"D:\anh\size.jpg", @"D:\ra", 350), (r.SizeChartImagePath, r.OutputDirectory, r.Defaults.WeightGrams));
        Assert.False(string.IsNullOrWhiteSpace(r.BatchId));
        Assert.Equal([@"D:\ra"], prompts.Opened);
        Assert.Contains("xuất 2 SP vào 1 file", vm.ExportStatus);
        Assert.Contains("Chưa cào — dòng 5: https://shopee.vn/x-i.1.5", vm.ExportResultText);
        Assert.Contains("Thiếu mẫu ngành 100011 (1 SP): dòng 7", vm.ExportResultText);
        Assert.False(vm.IsExporting);
        Assert.True(vm.OpenExportFolderCommand.CanExecute(null));
    }

    [Fact]
    public async Task Xuat_ONhapSai_HoacChuaNoiHub_KhongChay()
    {
        WriteConfig();
        var export = new FakeExport();
        var vm = NewVm(export, new FakePrompts());
        vm.Selected!.WeightGrams = "abc";

        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Contains("cân nặng", vm.ExportStatus);

        vm.Selected.WeightGrams = "0";
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Contains("Cân nặng", vm.ExportStatus);

        vm.Selected.WeightGrams = "200";
        export.HubProblem = "Chưa nối Hub";
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Equal("Chưa nối Hub", vm.ExportStatus);

        Assert.Empty(export.Requests);
    }

    [Fact]
    public async Task Huy_DungLuot_KhoaNutXoaAnhKhiDangXuat()
    {
        WriteConfig();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var export = new FakeExport
        {
            OnExport = async (_, progress, ct) =>
            {
                progress.Report(new ExportProgress(ExportStep.Images, 3, 10, "Ảnh: 3 sẵn sàng"));
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("không tới");
            },
        };
        export.Batches.Add(new ExportBatchInfo("lot-0", "shop-1", "Shop Một", DateTimeOffset.Now, ExportBatchState.Done, 1, 1, null, null, null, 4));
        var prompts = new FakePrompts();
        var vm = NewVm(export, prompts);
        await vm.RefreshBatchesAsync();
        vm.SelectedBatch = vm.Batches.Single();

        var run = vm.ExportCommand.ExecuteAsync(null);
        await started.Task;
        // Progress<T> không có SynchronizationContext của UI chạy trên thread pool — chờ báo tiến độ hiện xong rồi mới huỷ.
        for (var i = 0; i < 200 && vm.ExportStatus != "Ảnh: 3 sẵn sàng"; i++) await Task.Delay(10);
        Assert.Equal("Ảnh: 3 sẵn sàng", vm.ExportStatus);
        Assert.True(vm.IsExporting);
        Assert.False(vm.ClearBatchCommand.CanExecute(null));
        Assert.False(vm.ExportCommand.CanExecute(null));
        Assert.True(vm.CancelExportCommand.CanExecute(null));

        vm.CancelExportCommand.Execute(null);
        await run;

        Assert.False(vm.IsExporting);
        Assert.StartsWith("Đã huỷ", vm.ExportStatus);
        Assert.Empty(prompts.Opened);
        Assert.True(vm.ClearBatchCommand.CanExecute(null));
    }

    [Fact]
    public async Task XoaAnhLuot_HoiLaiKemCanhBao_KhongDongYThiKhongXoa()
    {
        var export = new FakeExport();
        export.Batches.Add(new ExportBatchInfo("lot-1", "shop-1", "Shop Một", DateTimeOffset.Now, ExportBatchState.Done, 2, 1, null, null, null, 9));
        var prompts = new FakePrompts { ConfirmResult = false };
        var vm = NewVm(export, prompts);
        await vm.RefreshBatchesAsync();
        vm.SelectedBatch = vm.Batches.Single();

        await vm.ClearBatchCommand.ExecuteAsync(null);
        Assert.Empty(export.Cleared);
        Assert.Contains("đã đẩy file", Assert.Single(prompts.Confirms));
        Assert.Contains("trước hạn của lượt", prompts.Confirms[0]);

        prompts.ConfirmResult = true;
        await vm.ClearBatchCommand.ExecuteAsync(null);
        Assert.Equal(["lot-1"], export.Cleared);
        Assert.Contains("đã xoá 3 ảnh", vm.ExportStatus);
        Assert.Contains("giữ 1 ảnh lượt khác còn dùng", vm.ExportStatus);
    }

    [Fact]
    public async Task MauVaSheet_ThemBotMau_DocSheetTheoFile()
    {
        WriteConfig();
        var export = new FakeExport();
        var wb = Path.Combine(_dir, "nguon.xlsx");
        File.WriteAllText(wb, "x");
        export.Sheets[wb] = ["Áo", "Quần"];
        var prompts = new FakePrompts { Workbook = wb, TemplatesToPick = [@"D:\mau\100009.xlsx", @"D:\mau\100011.xlsx"] };
        var vm = NewVm(export, prompts);
        Assert.Equal(["mô tả 100009.xlsx"], vm.Templates.Select(t => t.Info));

        vm.AddTemplatesCommand.Execute(null);
        Assert.Equal([@"D:\mau\100009.xlsx", @"D:\mau\100011.xlsx"], vm.Selected!.TemplatePaths);
        Assert.Equal(2, vm.Templates.Count);
        Assert.True(vm.IsDirty);

        vm.RemoveTemplateCommand.Execute(vm.Templates[0]);
        Assert.Equal([@"D:\mau\100011.xlsx"], vm.Selected.TemplatePaths);

        vm.PickWorkbookCommand.Execute(null);
        await vm.LoadSheetsAsync();
        Assert.Equal(wb, vm.Selected.WorkbookPath);
        Assert.Equal(["Áo", "Quần"], vm.SheetNames);
    }

    [Fact]
    public void NhapShop_DienFileExcelNguon_ShopMoiVaShopDaCoDangTrong_KhongGhiDe()
    {
        var shops = new List<BigSellerShopInfo>
        {
            new("bs1", "BS Một", "Shop A", "SheetA", @"D:\bs1.xlsx"),
            new("bs1", "BS Một", "Shop B", "SheetB", @"D:\bs1.xlsx"),
            new("bs1", "BS Một", "Shop C", "SheetC", @"D:\bs1.xlsx"),
        };
        var vm = NewVm(new FakeExport(), new FakePrompts(), () => shops);
        vm.AddCommand.Execute(null);
        vm.Selected!.BigSellerAccount = "bs1";
        vm.Selected.Sheet = "SheetB";
        vm.AddCommand.Execute(null);
        vm.Selected!.BigSellerAccount = "bs1";
        vm.Selected.Sheet = "SheetC";
        vm.Selected.WorkbookPath = @"D:\rieng.xlsx";

        vm.ImportShopsCommand.Execute(null);

        var byS = vm.Accounts.ToDictionary(a => a.Sheet);
        Assert.Equal(@"D:\bs1.xlsx", byS["SheetA"].WorkbookPath);   // shop mới
        Assert.Equal(@"D:\bs1.xlsx", byS["SheetB"].WorkbookPath);   // shop đã có, ô trống
        Assert.Equal(@"D:\rieng.xlsx", byS["SheetC"].WorkbookPath); // đã có giá trị → giữ
        Assert.Equal(3, vm.Accounts.Count);
    }

    [Fact]
    public async Task HubHetGio_KhongBaoDaHuy()
    {
        WriteConfig();
        var export = new FakeExport { OnExport = (_, _, _) => throw new TaskCanceledException("HttpClient.Timeout") };
        var vm = NewVm(export, new FakePrompts());

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.StartsWith("Lỗi", vm.ExportStatus);
        Assert.DoesNotContain("huỷ", vm.ExportStatus);
        Assert.False(vm.IsExporting);
    }

    [Fact]
    public async Task MauDoiShop_HoiLai_KhongDongYThiThoi_DongYThiChayLaiCoCo()
    {
        WriteConfig();
        var export = new FakeExport
        {
            OnExport = (req, _, _) => req.AllowTemplateShopChange
                ? Task.FromResult(Result(req.BatchId, [Path.Combine(req.OutputDirectory, "b_1.xlsx")]))
                : throw new TemplateShopChangedException("999", "214522044"),
        };
        var prompts = new FakePrompts { ConfirmResult = false };
        var vm = NewVm(export, prompts);

        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Single(export.Requests);
        Assert.Contains("shop_id 999, khác lần trước 214522044", Assert.Single(prompts.Confirms));
        Assert.StartsWith("Không xuất", vm.ExportStatus);

        prompts.ConfirmResult = true;
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Equal(3, export.Requests.Count);
        Assert.True(export.Requests[2].AllowTemplateShopChange);
        Assert.Equal(export.Requests[1].BatchId, export.Requests[2].BatchId);
        Assert.Contains("xuất 2 SP", vm.ExportStatus);
    }

    [Fact]
    public async Task LamLaiAnhLoi_GoiChoShopDangChon()
    {
        WriteConfig();
        var export = new FakeExport();
        var vm = NewVm(export, new FakePrompts());

        await vm.RetryFailedImagesCommand.ExecuteAsync(null);

        Assert.Equal([vm.Selected!.ImageKey], export.Retried);
        Assert.Matches("^[0-9a-f]{32}$", export.Retried[0]);
        Assert.Contains("đưa 4 ảnh lỗi về chờ làm lại", vm.ExportStatus);
    }

    [Fact]
    public void HanLuot_TheoHanThatCuaAnh()
    {
        var now = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);
        ExportBatchInfo B(DateTimeOffset? exp, DateTimeOffset? cleared = null) =>
            new("l", "s", "S", now.AddDays(-5), ExportBatchState.Done, 1, 1, null, null, cleared, 3, exp);

        var at = now.AddDays(1);
        Assert.Equal("đẩy lên Shopee trước " + at.ToLocalTime().ToString("dd/MM HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            BatchRow.ExpiryText(B(at), now));
        Assert.StartsWith("ảnh trên Hub đã hết hạn", BatchRow.ExpiryText(B(now.AddMinutes(-1)), now));
        Assert.Equal("chưa rõ hạn ảnh", BatchRow.ExpiryText(B(null), now));
        Assert.Equal("", BatchRow.ExpiryText(B(at, now), now));
    }

    [Fact]
    public void BaoCao_CoDongPhanLoaiChuanTheoSp()
    {
        var r = Result("lot", [@"D:a_1.xlsx"]) with
        {
            ItemNotes = [new ExportItemNote(4, 44, "l4", "phân loại chuẩn: 7/11 giá trị đổi sang mã")],
        };

        Assert.Contains("Phân loại — dòng 4 (SP 44): phân loại chuẩn: 7/11 giá trị đổi sang mã", MainViewModel.DescribeResult(r));
    }

    // ─────────────── GĐ9: ảnh gốc trên Hub ───────────────

    private static SourceImageStatsResponse St(long sourceCount, long sourceBytes) =>
        new(100L << 30, 36L << 30, sourceCount, sourceBytes, 15L << 30, 12, 3L << 20, 5000L << 20);

    [Fact]
    public void DungLuongHub_TuDocLucMo_HienGbMb()
    {
        var export = new FakeExport();
        export.Stats.Enqueue(St(1234, 2L << 30));
        var vm = NewVm(export, new FakePrompts());

        Assert.Equal(1, export.StatsCalls);   // tự gọi một lần lúc mở
        Assert.Equal("còn trống 36 GB / 100 GB", vm.HubDiskText);
        Assert.Equal("1234 ảnh · 2 GB / 15 GB", vm.HubSourceText.Replace(",", "").Replace(".", ""));
        Assert.Equal("12 file · 3 MB / 4.88 GB", vm.HubMediaText);
    }

    [Fact]
    public async Task XoaAnhGoc_BamHuy_KhongGoiXoa()
    {
        var export = new FakeExport();
        export.Stats.Enqueue(St(120, 300L << 20));
        var prompts = new FakePrompts { TypedResult = false };
        var vm = NewVm(export, prompts);

        await vm.DeleteAllSourceImagesCommand.ExecuteAsync(null);

        Assert.Equal(0, export.DeleteCalls);
        var (msg, word) = Assert.Single(prompts.Typed);
        Assert.Equal("XOA", word);
        Assert.Contains("120 ảnh gốc", msg);
        Assert.Contains("300 MB", msg);
        Assert.Contains("chưa đẩy lên Shopee sẽ hỏng ảnh", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tải từ CDN", msg);
        Assert.StartsWith("Đã huỷ", vm.HubStorageStatus);
    }

    [Fact]
    public async Task XoaAnhGoc_XacNhan_GoiLaiStats_BaoHubCon0()
    {
        var export = new FakeExport { DeleteResult = new(120, 0, 300L << 20, "link hỏng"), BatchesUsingHub = 2 };
        export.Stats.Enqueue(St(120, 300L << 20));   // lúc mở app
        var prompts = new FakePrompts { TypedResult = true };
        var vm = NewVm(export, prompts);
        export.Stats.Enqueue(St(120, 300L << 20));   // đọc lại trước khi hỏi
        export.Stats.Enqueue(St(0, 0));              // đọc lại sau khi xoá
        export.StatsCalls = 0;

        await vm.DeleteAllSourceImagesCommand.ExecuteAsync(null);

        Assert.Equal(1, export.DeleteCalls);
        Assert.Equal(2, export.StatsCalls);
        Assert.EndsWith("Hub còn 0 ảnh gốc.", vm.HubStorageStatus);
        Assert.StartsWith("0 ảnh", vm.HubSourceText);
        Assert.Contains("2 lượt đã xuất", Assert.Single(prompts.Typed).Message);
        Assert.Single(export.Marked);   // các lượt dùng link Hub được đánh "kho gốc đã xoá"
    }

    [Fact]
    public async Task XoaAnhGoc_ConSot_BaoRoConBaoNhieu()
    {
        var export = new FakeExport { DeleteResult = new(117, 3, 1L << 20, "") };
        export.Stats.Enqueue(St(120, 300L << 20));
        var vm = NewVm(export, new FakePrompts { TypedResult = true });
        export.Stats.Enqueue(St(120, 300L << 20));
        export.Stats.Enqueue(St(3, 1L << 20));

        await vm.DeleteAllSourceImagesCommand.ExecuteAsync(null);

        Assert.Contains("Hub còn 3 ảnh gốc", vm.HubStorageStatus);
    }

    [Fact]
    public async Task DangXuat_KhoaNutXoaAnhGoc_TuyChonBoSpDiVaoYeuCau()
    {
        WriteConfig();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var export = new FakeExport
        {
            OnExport = async (_, _, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("không tới");
            },
        };
        var vm = NewVm(export, new FakePrompts());
        Assert.True(vm.Settings.DownloadMissingFromCdn);
        vm.Settings.SkipMissingHubImages = true;

        var run = vm.ExportCommand.ExecuteAsync(null);
        await started.Task;
        Assert.False(vm.DeleteAllSourceImagesCommand.CanExecute(null));
        vm.CancelExportCommand.Execute(null);
        await run;

        Assert.Equal(MissingHubImagePolicy.SkipProduct, Assert.Single(export.Requests).MissingHubImages);
        Assert.True(vm.DeleteAllSourceImagesCommand.CanExecute(null));
    }

    [Fact]
    public async Task DangXoaAnhGoc_KhoaNutXuatNgayTuDau()
    {
        WriteConfig();
        var gate = new TaskCompletionSource();
        var export = new FakeExport();
        export.Stats.Enqueue(St(5, 1L << 20));
        var vm = NewVm(export, new FakePrompts { TypedResult = false });
        Assert.True(vm.ExportCommand.CanExecute(null));
        export.Stats.Enqueue(St(5, 1L << 20));
        export.StatsGate = gate.Task;

        var run = vm.DeleteAllSourceImagesCommand.ExecuteAsync(null);   // đang chờ đọc stats, chưa hỏi

        Assert.True(vm.IsDeletingSources);
        Assert.False(vm.ExportCommand.CanExecute(null));
        gate.SetResult();
        await run;
        Assert.False(vm.IsDeletingSources);
        Assert.True(vm.ExportCommand.CanExecute(null));
        Assert.Equal(0, export.DeleteCalls);
    }

    [Fact]
    public void HanLuot_KhoGocDaXoa_BaoFileHongAnh()
    {
        var now = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        var gone = now.AddHours(-1);
        var info = new ExportBatchInfo("l", "s", "S", now.AddDays(-1), ExportBatchState.Done, 1, 1, null, null, null, 1, null, null,
            now.AddDays(5), 9, 0, gone);

        Assert.Equal($"ảnh gốc trên Hub đã xoá lúc {BatchRow.Short(gone)} — file của lượt này sẽ hỏng ảnh nếu chưa đẩy",
            BatchRow.ExpiryText(info, now));
    }

    [Fact]
    public async Task KhoaAnh_TaiKhoanCuDuocSinhVaLuuNgay_GiuNguyenQuaLanMoSau_DungLamShopKey()
    {
        WriteConfig();   // tài khoản cũ chưa có ImageKey
        var export = new FakeExport();
        var vm = NewVm(export, new FakePrompts());

        var store = new SellerAccountStore(FilePath, Path.Combine(_dir, "profiles"));
        var key = store.Load().Accounts.Single().ImageKey;
        Assert.Matches("^[0-9a-f]{32}$", key);
        Assert.False(vm.IsDirty);
        Assert.Equal(key, NewVm(new FakeExport(), new FakePrompts()).Selected!.ImageKey);   // mở lại: không sinh khoá mới
        vm.Selected!.Label = "Đổi tên";
        Assert.True(vm.TrySave());
        Assert.Equal(key, store.Load().Accounts.Single().ImageKey);

        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Equal(key, Assert.Single(export.Requests).ShopKey);
    }

    [Fact]
    public async Task KhoaAnh_HaiMayCungMaSellerKhacKhoa_RaShopKeyKhacNhau()
    {
        WriteConfig();
        var first = new FakeExport();
        var vmA = NewVm(first, new FakePrompts());
        await vmA.ExportCommand.ExecuteAsync(null);
        File.Delete(FilePath);
        WriteConfig();   // "máy khác": cùng Id seller-1 (shop-1), khoá sinh mới
        var second = new FakeExport();
        var vmB = NewVm(second, new FakePrompts());
        await vmB.ExportCommand.ExecuteAsync(null);

        Assert.Equal(vmA.Selected!.Id, vmB.Selected!.Id);
        Assert.NotEqual(first.Requests.Single().ShopKey, second.Requests.Single().ShopKey);
    }

    [Fact]
    public void KhoaAnh_TaiKhoanMoiCoNgay_ThemVaNhapShop()
    {
        var vm = NewVm(new FakeExport(), new FakePrompts(),
            () => [new BigSellerShopInfo("bs1", "BS", "Shop Nhập", "Sheet1", @"D:.xlsx")]);
        vm.AddCommand.Execute(null);
        vm.ImportShopsCommand.Execute(null);

        Assert.Equal(2, vm.Accounts.Count);
        Assert.All(vm.Accounts, a => Assert.Matches("^[0-9a-f]{32}$", a.ImageKey));
        Assert.NotEqual(vm.Accounts[0].ImageKey, vm.Accounts[1].ImageKey);
    }

    [Fact]
    public void LuaChonAnhChuaCoTrenHub_LuuVaoCauHinh()
    {
        WriteConfig();
        var vm = NewVm(new FakeExport(), new FakePrompts());
        vm.Settings.SkipMissingHubImages = true;
        Assert.True(vm.IsDirty);
        Assert.True(vm.TrySave());

        var again = NewVm(new FakeExport(), new FakePrompts());
        Assert.True(again.Settings.SkipMissingHubImages);
        Assert.False(again.Settings.DownloadMissingFromCdn);
    }

    [Fact]
    public void HanLuot_ToanLinkHub_KhongHan_ChiHanBangSize()
    {
        var now = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        var size = now.AddDays(6);
        var hubOnly = new ExportBatchInfo("l", "s", "S", now, ExportBatchState.Done, 1, 1, null, null, null, 1, null, null, size, 9, 0);

        Assert.Equal($"không hạn (ảnh SP nằm trên Hub) · ảnh bảng size: đẩy lên Shopee trước {BatchRow.Short(size)}",
            BatchRow.ExpiryText(hubOnly, now));

        var r = Result("lot", [@"D:\ra\a.xlsx"]) with
        {
            HubImages = 9, CdnImages = 2, SizeChartExpiresAt = size,
            CdnItems = [new ExportItemNote(5, 55, "l5", "2 ảnh tải từ CDN (chưa có trên Hub)")],
        };
        var lines = MainViewModel.DescribeResult(r);
        Assert.Contains($"Hạn ảnh: Ảnh SP nằm trên Hub (không hạn); ảnh bảng size: đẩy lên Shopee trước {BatchRow.Short(size)}.", lines);
        Assert.Contains("Ảnh SP: 9 lấy link Hub, 2 tải từ CDN.", lines);
        Assert.Contains("Tải CDN — dòng 5 (SP 55): 2 ảnh tải từ CDN (chưa có trên Hub)", lines);
    }

    [Fact]
    public void BaoCao_MoiDongMotY()
    {
        var r = new MassUploadExportResult("lot", [@"D:\ra\a_1.xlsx"], 1, 4, 1,
            [new ExportSourceLink(5, "https://shopee.vn/x-i.1.5")], [new ExportSourceLink(6, "https://shopee.vn/shop")],
            [new ExportMissingTemplate(100011, [new ExportSourceRow(7, "l7", 7), new ExportSourceRow(9, "l9", 9)])],
            [new ExportItemNote(8, 8, "l8", "SP không còn ảnh dùng được")], [new ExportItemNote(10, 10, "l10", "tên SP dưới 10 ký tự")],
            [new ExportItemNote(3, 3, "l3", "tên dài 130 ký tự → cắt còn 118"), new ExportItemNote(3, 3, "l3", "ảnh SP 12 → 9")],
            [new ExportItemNote(3, 3, "l3", "thiếu Giới tính")], 2, ["basic"], true);

        var lines = MainViewModel.DescribeResult(r);

        Assert.Equal(
        [
            @"File: D:\ra\a_1.xlsx",
            "Lưu ý: basic",
            "Thiếu mẫu ngành 100011 (2 SP): dòng 7, 9",
            "Chưa cào — dòng 5: https://shopee.vn/x-i.1.5",
            "Không có mã SP — dòng 6: https://shopee.vn/shop",
            "Lỗi ảnh — dòng 8 (SP 8): SP không còn ảnh dùng được",
            "Bỏ — dòng 10 (SP 10): tên SP dưới 10 ký tự",
            "Thiếu thuộc tính — dòng 3 (SP 3): thiếu Giới tính",
            "Cắt — dòng 3 (SP 3): tên dài 130 ký tự → cắt còn 118; ảnh SP 12 → 9",
        ], lines);
    }
}
