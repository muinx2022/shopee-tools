using Shopee.Core.BigSeller;
using Shopee.Core.MassUpload.Seller;
using Shopee.Upload.Forms;
using Shopee.Upload.Import;
using static Shopee.Upload.Tests.ImportFixtures;

namespace Shopee.Upload.Tests;

/// <summary>Hai nút nhập GĐ5c trên màn chính, với hộp thoại GIẢ và nguồn BigSeller GIẢ (không đọc <c>bigseller.json</c> thật).</summary>
public sealed class MainViewModelImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "upload-vm-import-tests-" + Guid.NewGuid().ToString("N"));

    public MainViewModelImportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string FilePath => Path.Combine(_dir, "seller-accounts.json");

    private sealed class FakePrompts : IImportPrompts
    {
        public bool ConfirmResult = true;
        public string? File;
        public OverwriteChoice Choice = OverwriteChoice.FillEmptyOnly;
        public int ConfirmCalls, AskCalls;
        public ShopImportPlan? LastPlan;
        public List<string> Summaries { get; } = [];
        public bool LastIsError;

        public bool ConfirmShops(ShopImportPlan plan)
        {
            ConfirmCalls++;
            LastPlan = plan;
            return ConfirmResult;
        }

        public string? PickPasswordFile() => File;

        public OverwriteChoice AskOverwrite(int accounts, int fields)
        {
            AskCalls++;
            return Choice;
        }

        public void ShowSummary(string title, string text, bool isError = false)
        {
            Summaries.Add(text);
            LastIsError = isError;
        }
    }

    /// <summary>Tài khoản BigSeller giả CÓ email / mật khẩu — để kiểm chúng không đi tới log.</summary>
    private static List<BigSellerAccount> BigSellerAccounts() =>
    [
        new()
        {
            Id = "bs1", Label = "BS Một", Email = "bs1@secret-mail.vn", Password = "BsPass#Secret1", EmailPassword = "BsMail#Secret1",
            Shops = [new() { Name = "Shop A", ShopeeDataSheet = "SheetA" }, new() { Name = "Shop B", ShopeeDataSheet = "" }],
        },
        new()
        {
            Id = "bs2", Label = "BS Hai", Email = "bs2@secret-mail.vn", Password = "BsPass#Secret2",
            Shops = [new() { Name = "Shop C", ShopeeDataSheet = "SheetC" }, new() { Name = "Shop C trùng", ShopeeDataSheet = "sheetc" }],
        },
    ];

    private MainViewModel NewVm(FakePrompts prompts, List<BigSellerAccount>? bs = null) =>
        new(new SellerAccountStore(FilePath, Path.Combine(_dir, "profiles")), a => a(), _ => true, null, prompts,
            () => BigSellerShopSource.Flatten(bs ?? BigSellerAccounts()));

    // ─────────────── nhập shop ───────────────

    [Fact]
    public void NhapShop_ThemDung_ChuaLuu_NhapLanHaiKhongTrung()
    {
        var prompts = new FakePrompts();
        var vm = NewVm(prompts);

        vm.ImportShopsCommand.Execute(null);

        Assert.Equal(3, vm.Accounts.Count);
        var a = vm.Accounts[0];
        Assert.Equal(("seller-1", "Shop A", "bs1", "SheetA", ProxyChoice.Shared), (a.Id, a.Label, a.BigSellerAccount, a.Sheet, a.Proxy));
        var b = vm.Accounts[1];
        Assert.Equal(("seller-2", "Shop B", "Shop B"), (b.Id, b.Label, b.Sheet));   // sheet rỗng → tên shop
        Assert.Equal(("seller-3", "bs2", "SheetC"), (vm.Accounts[2].Id, vm.Accounts[2].BigSellerAccount, vm.Accounts[2].Sheet));
        Assert.Single(prompts.LastPlan!.Skipped);   // "Shop C trùng" cùng sheet (khác hoa thường) với Shop C
        Assert.True(vm.IsDirty);
        Assert.False(File.Exists(FilePath));        // chưa lưu

        vm.ImportShopsCommand.Execute(null);        // lần 2 (chưa lưu)
        Assert.Equal(3, vm.Accounts.Count);
        Assert.Empty(prompts.LastPlan!.ToAdd);
        Assert.Equal(4, prompts.LastPlan.Existing.Count);   // cả "Shop C trùng": sheet đã thuộc tài khoản Shop C

        Assert.True(vm.TrySave(), string.Join("; ", vm.Errors));
        var again = NewVm(prompts);
        again.ImportShopsCommand.Execute(null);     // lần 3 sau khi lưu + mở lại
        Assert.Equal(3, again.Accounts.Count);
        Assert.Equal(["Shop A", "Shop B", "Shop C"], again.Accounts.Select(x => x.Label));
    }

    [Fact]
    public void NhapShop_TaiKhoanDaCoCungNguon_KhacHoaThuong_KhongThem()
    {
        var prompts = new FakePrompts();
        var vm = NewVm(prompts);
        vm.AddCommand.Execute(null);
        vm.Selected!.Label = "Tự đặt tên";
        vm.Selected.BigSellerAccount = " BS1 ";
        vm.Selected.Sheet = "sheeta";

        vm.ImportShopsCommand.Execute(null);

        Assert.Equal(3, vm.Accounts.Count);
        Assert.DoesNotContain(vm.Accounts, x => x.Label == "Shop A");
        Assert.Contains(prompts.LastPlan!.Existing, i => i.ShopName == "Shop A" && i.ExistingName == "Tự đặt tên");
    }

    [Fact]
    public void NhapShop_Huy_KhongThemGi()
    {
        var prompts = new FakePrompts { ConfirmResult = false };
        var vm = NewVm(prompts);

        vm.ImportShopsCommand.Execute(null);

        Assert.Empty(vm.Accounts);
        Assert.False(vm.IsDirty);
        Assert.Equal(1, prompts.ConfirmCalls);
    }

    [Fact]
    public void NhapShop_KhongLogEmailMatKhauBigSeller_IdMoiKhongDungLaiIdDaXoa()
    {
        var prompts = new FakePrompts();
        var vm = NewVm(prompts);
        vm.AddCommand.Execute(null);                 // seller-1
        vm.DeleteCommand.Execute(null);              // seller-1 vào danh sách đã xoá

        vm.ImportShopsCommand.Execute(null);

        Assert.DoesNotContain(vm.Accounts, x => x.Id == "seller-1");
        var all = vm.LogText + string.Join("", prompts.Summaries);
        foreach (var s in new[] { "secret-mail", "BsPass#Secret1", "BsMail#Secret1", "BsPass#Secret2" })
            Assert.DoesNotContain(s, all);
        Assert.Contains("BS Một", all);
    }

    [Fact]
    public void NhapShop_NhanBigSellerRong_BangXemTruocVaLogHienTKBigSellerN()
    {
        var bs = BigSellerAccounts();
        bs[0].Label = "";
        var prompts = new FakePrompts();
        var vm = NewVm(prompts, bs);

        vm.ImportShopsCommand.Execute(null);

        Assert.Equal("TK BigSeller 1 (bs1)", prompts.LastPlan!.ToAdd[0].AccountDisplay);
        Assert.Contains("Shop A [TK BigSeller 1 (bs1)]", vm.LogText);
        Assert.DoesNotContain("secret-mail", vm.LogText);
    }

    [Fact]
    public void NhapShop_DocBigSellerLoi_BaoLoi_KhongThemGi()
    {
        var prompts = new FakePrompts();
        var vm = new MainViewModel(new SellerAccountStore(FilePath, Path.Combine(_dir, "profiles")), a => a(), _ => true, null, prompts,
            () => throw new BigSellerReadException("Không đọc được bigseller.json (x): file hỏng, JSON sai ở dòng 1."));

        vm.ImportShopsCommand.Execute(null);

        Assert.Empty(vm.Accounts);
        Assert.Equal(0, prompts.ConfirmCalls);
        Assert.True(prompts.LastIsError);
        Assert.Contains("Không đọc được bigseller.json", Assert.Single(prompts.Summaries));
    }

    [Fact]
    public void NhapShop_KhoaNguonKhongKhop_NhungTrungTen_KhongThem_BaoCoTheDaCo()
    {
        var prompts = new FakePrompts();
        var bs = BigSellerAccounts();
        bs[1].Shops.Add(new() { Name = "Shop D", ShopeeDataSheet = "Sheet D" });
        var vm = NewVm(prompts, bs);
        void Add(string label, string bs = "", string sheet = "", string login = "")
        {
            vm.AddCommand.Execute(null);
            vm.Selected!.Label = label;
            vm.Selected.BigSellerAccount = bs;
            vm.Selected.Sheet = sheet;
            vm.Selected.ShopeeLogin = login;
        }
        Add("shop a");                                  // Shop A: sheet + TK BigSeller để trống
        Add("Tự đặt", bs: "gõ tay", sheet: "x", login: "Shop B");   // Shop B: trùng tài khoản Shopee, TK BigSeller gõ tay
        Add("Shop D mới", bs: " BS2 ", sheet: "sheet   d");          // Shop D: khoảng trắng giữa chữ / hoa thường → khớp khoá nguồn

        vm.ImportShopsCommand.Execute(null);

        var plan = prompts.LastPlan!;
        Assert.Equal(["Shop C"], plan.ToAdd.Select(i => i.ShopName));
        Assert.Equal(["Shop A", "Shop B"], plan.Possible.Select(i => i.ShopName));
        Assert.Equal(["shop a", "Tự đặt"], plan.Possible.Select(i => i.ExistingName));
        Assert.Contains(plan.Existing, i => i.ShopName == "Shop D" && i.ExistingName == "Shop D mới");
        Assert.Equal(4, vm.Accounts.Count);   // chỉ thêm Shop C
        Assert.Contains("Có thể đã có — KHÔNG thêm 2", Assert.Single(prompts.Summaries));
    }

    [Fact]
    public void NhapShop_KhongDungIdCoThuMucProfileSanTrenDia()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "profiles", "seller-1"));
        var vm = NewVm(new FakePrompts());

        vm.ImportShopsCommand.Execute(null);

        Assert.Equal(["seller-2", "seller-3", "seller-4"], vm.Accounts.Select(a => a.Id));
    }

    [Fact]
    public void NhapShop_KhongDungIdDaXoaOPhienTruoc_LayTuFileDaLuu()
    {
        var vm = NewVm(new FakePrompts());
        vm.AddCommand.Execute(null);           // seller-1
        Assert.True(vm.TrySave());
        vm.DeleteCommand.Execute(null);
        Assert.True(vm.TrySave());             // RetiredIds = [seller-1] nằm trong FILE
        Assert.Contains("seller-1", File.ReadAllText(FilePath));

        var again = NewVm(new FakePrompts());  // phiên mới: _retired rỗng, chỉ còn RetiredIds đã lưu
        again.ImportShopsCommand.Execute(null);

        Assert.DoesNotContain(again.Accounts, a => a.Id == "seller-1");
        Assert.Equal("seller-2", again.Accounts[0].Id);
    }

    [Fact]
    public void Nhap_GomKiemMotLan_KhongKiemTheoTungO()
    {
        var bs = Enumerable.Range(1, 20).Select(i => new BigSellerAccount
        {
            Id = $"bs{i}", Label = $"BS {i}", Shops = [new() { Name = $"Shop {i}", ShopeeDataSheet = $"S{i}" }],
        }).ToList();
        var path = Path.Combine(_dir, "nhieu.xlsx");
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("S");
            Row(ws, 1, "ACC", "Pass", "mail", "pass Mail");
            for (var i = 1; i <= 20; i++) Row(ws, i + 1, $"Shop {i}", $"Pass#{i:00}x", $"m{i}@x.vn", $"Mail#{i:00}x");
            wb.SaveAs(path);
        }
        var prompts = new FakePrompts { File = path };
        var vm = NewVm(prompts, bs);

        var before = vm.RevalidateCount;
        vm.ImportShopsCommand.Execute(null);
        Assert.Equal(20, vm.Accounts.Count);
        Assert.Equal(1, vm.RevalidateCount - before);

        before = vm.RevalidateCount;
        vm.ImportPasswordsCommand.Execute(null);
        Assert.All(vm.Accounts, a => Assert.NotEqual("", a.ShopeePassword));
        Assert.Equal(1, vm.RevalidateCount - before);
        Assert.True(vm.IsDirty);
        Assert.False(vm.HasErrors, string.Join("; ", vm.Errors));
    }

    // ─────────────── nhập mật khẩu ───────────────

    private MainViewModel VmWithShops(FakePrompts prompts, Action<MainViewModel>? edit = null)
    {
        var vm = NewVm(prompts);
        foreach (var label in new[] { "Shop Bé Xinh", "Shop A", "Không có trong file" })
        {
            vm.AddCommand.Execute(null);
            vm.Selected!.Label = label;
        }
        vm.Accounts[1].ShopeeLogin = "SHOP-A.LOGIN";
        edit?.Invoke(vm);
        Assert.True(vm.TrySave(), string.Join("; ", vm.Errors));
        return NewVm(prompts);
    }

    [Fact]
    public void NhapMatKhau_DienDung_KhongHoiKhiKhongXungDot_LuuMoLaiThayDu()
    {
        var prompts = new FakePrompts { File = WriteSample(_dir) };
        var vm = VmWithShops(prompts);

        vm.ImportPasswordsCommand.Execute(null);

        Assert.Equal(0, prompts.AskCalls);
        var xinh = vm.Accounts.Single(x => x.Label == "Shop Bé Xinh");
        Assert.Equal(("Shop Bé Xinh", PassB, "b@hotmail.com", MailPassB),
            (xinh.ShopeeLogin, xinh.ShopeePassword, xinh.VerifyEmail, xinh.VerifyEmailPassword));
        var a = vm.Accounts.Single(x => x.Label == "Shop A");
        Assert.Equal(("SHOP-A.LOGIN", PassA, "a@hotmail.com", MailPassA),
            (a.ShopeeLogin, a.ShopeePassword, a.VerifyEmail, a.VerifyEmailPassword));
        var none = vm.Accounts.Single(x => x.Label == "Không có trong file");
        Assert.Equal(("", ""), (none.ShopeeLogin, none.ShopeePassword));
        Assert.True(vm.IsDirty);

        var summary = Assert.Single(prompts.Summaries);
        Assert.Contains("Đã điền 2 tài khoản", summary);
        Assert.Contains("Không tìm thấy trong file: 1 tài khoản (Không có trong file)", summary);
        Assert.Contains("không khớp tài khoản nào: 1", summary);

        Assert.True(vm.TrySave(), string.Join("; ", vm.Errors));
        var again = NewVm(prompts);
        var a2 = again.Accounts.Single(x => x.Label == "Shop A");
        Assert.Equal((PassA, MailPassA), (a2.ShopeePassword, a2.VerifyEmailPassword));
    }

    [Fact]
    public void NhapMatKhau_ChiDienOTrong_HoiMotLan_KhongGhiDe_KhongLotMatKhauVaoLog()
    {
        var prompts = new FakePrompts { File = WriteSample(_dir), Choice = OverwriteChoice.FillEmptyOnly };
        var vm = VmWithShops(prompts, v =>
        {
            v.Accounts[0].ShopeeLogin = "shop be xinh";       // cùng ACC sau chuẩn hoá → không phải ô khác
            v.Accounts[0].ShopeePassword = "Cu#XinhPass";     // khác file
            v.Accounts[1].VerifyEmail = "a@hotmail.com";
            v.Accounts[1].VerifyEmailPassword = "Cu#AMail";   // khác file
        });

        vm.ImportPasswordsCommand.Execute(null);

        Assert.Equal(1, prompts.AskCalls);
        Assert.Equal("Cu#XinhPass", vm.Accounts[0].ShopeePassword);
        Assert.Equal(MailPassB, vm.Accounts[0].VerifyEmailPassword);   // ô trống thì vẫn điền
        Assert.Equal("Cu#AMail", vm.Accounts[1].VerifyEmailPassword);
        Assert.Equal(PassA, vm.Accounts[1].ShopeePassword);

        // Mật khẩu trong file mà KHÔNG lên form (giữ ô cũ / dòng không khớp) thì Redact không biết để che — phải không bao giờ được viết ra.
        var all = vm.LogText + string.Join("", prompts.Summaries);
        foreach (var s in new[] { PassA, MailPassA, PassB, MailPassB, PassUnmatched, "Cu#XinhPass", "Cu#AMail" })
            Assert.DoesNotContain(s, all);
    }

    [Fact]
    public void NhapMatKhau_GhiDeTatCa()
    {
        var prompts = new FakePrompts { File = WriteSample(_dir), Choice = OverwriteChoice.OverwriteAll };
        var vm = VmWithShops(prompts, v =>
        {
            v.Accounts[0].ShopeeLogin = "Shop Bé Xinh";
            v.Accounts[0].ShopeePassword = "Cu#XinhPass";
        });

        vm.ImportPasswordsCommand.Execute(null);

        Assert.Equal(1, prompts.AskCalls);
        Assert.Equal(PassB, vm.Accounts[0].ShopeePassword);
    }

    [Fact]
    public void NhapMatKhau_HuyKhiHoi_KhongGhiGi()
    {
        var prompts = new FakePrompts { File = WriteSample(_dir), Choice = OverwriteChoice.Cancel };
        var vm = VmWithShops(prompts, v =>
        {
            v.Accounts[0].ShopeeLogin = "Shop Bé Xinh";
            v.Accounts[0].ShopeePassword = "Cu#XinhPass";
        });

        vm.ImportPasswordsCommand.Execute(null);

        Assert.False(vm.IsDirty);
        Assert.Equal("Cu#XinhPass", vm.Accounts[0].ShopeePassword);
        Assert.Equal("", vm.Accounts[1].ShopeePassword);
        Assert.Contains("Đã huỷ", Assert.Single(prompts.Summaries));
    }

    [Fact]
    public void NhapMatKhau_FileThieuCot_BaoLoi_KhongGhiGi()
    {
        var path = Path.Combine(_dir, "thieu.xlsx");
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("Trang tính1");
            Row(ws, 1, "ACC", "Pass", "mail");
            Row(ws, 2, "Shop A", "Thieu#Cot1", "a@x.vn");
            wb.SaveAs(path);
        }
        var prompts = new FakePrompts { File = path };
        var vm = VmWithShops(prompts);

        vm.ImportPasswordsCommand.Execute(null);

        Assert.False(vm.IsDirty);
        Assert.All(vm.Accounts, x => Assert.Equal("", x.ShopeePassword));
        Assert.True(prompts.LastIsError);
        Assert.Contains("pass Mail", Assert.Single(prompts.Summaries));
    }

    [Fact]
    public void NhapMatKhau_KhongChonFile_KhongLamGi()
    {
        var prompts = new FakePrompts { File = null };
        var vm = VmWithShops(prompts);
        vm.ImportPasswordsCommand.Execute(null);
        Assert.False(vm.IsDirty);
        Assert.Empty(prompts.Summaries);
    }
}
