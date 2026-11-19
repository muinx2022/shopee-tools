using Shopee.Core.MassUpload.Seller;
using Shopee.Core.MassUpload.Seller.Login;
using Shopee.Upload.Services;

namespace Shopee.Upload.Tests;

/// <summary>Luật của màn chính quanh Brave + lưu, dùng chỗ mở Brave GIẢ (<see cref="ISellerBrowserOpener"/>): không mạng, không
/// Brave thật.</summary>
public sealed class MainViewModelBrowserTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "upload-vm-browser-tests-" + Guid.NewGuid().ToString("N"));

    public MainViewModelBrowserTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string FilePath => Path.Combine(_dir, "seller-accounts.json");

    /// <summary>Số lần màn chính dựng chỗ mở Brave — phải đúng 1 lần cả phiên (gate giữ IP đã dùng + mốc đổi IP Kiot).</summary>
    private int _factoryCalls;

    private MainViewModel NewVm(FakeOpener opener) =>
        new(new SellerAccountStore(FilePath, Path.Combine(_dir, "profiles")), a => a(), _ => true, (_, _) =>
        {
            _factoryCalls++;
            return opener;
        });

    /// <summary>Chỗ mở giả: mỗi lần gọi chạy <see cref="Next"/> (mặc định: mở được ngay). Đếm số lần gọi và số session bị đóng.</summary>
    private sealed class FakeOpener : ISellerBrowserOpener
    {
        public int Calls;
        public int Closed;
        public Func<CancellationToken, Task<SellerGateResult>>? Next;

        /// <summary>Kết quả quét tiến trình theo profile (<see cref="IsBrowserRunning"/>).</summary>
        public bool Running { get; set; } = true;

        /// <summary>true = session mang một <see cref="System.Diagnostics.Process"/> không gắn tiến trình nào (HasExited ném — như
        /// Process đã bị launcher dispose).</summary>
        public bool DetachedProcess { get; set; }

        public bool IsBrowserRunning(SellerBrowserSession session) => Running;

        /// <summary>Chạy lúc session bị đóng (trước khi đếm).</summary>
        public Action? OnClose { get; set; }

        /// <summary>Kết quả lần lượt của các lần đóng; hết thì true.</summary>
        public Queue<bool> CloseResults { get; } = new();

        public SellerBrowserSession NewSession() =>
            new(DetachedProcess ? new System.Diagnostics.Process() : null, @"C:\profile", new ProxyLease(ProxyScheme.Http, "1.2.3.4", 8080), "9.9.9.9", () =>
            {
                OnClose?.Invoke();
                Interlocked.Increment(ref Closed);
                return Task.FromResult(CloseResults.Count > 0 ? CloseResults.Dequeue() : true);
            });

        public SellerGateResult Opened() => new(SellerGateFailure.None, null, NewSession(), "9.9.9.9");

        public Task<SellerGateResult> OpenAsync(SellerAccount account, SellerUploadSettings settings, string profileDir, CancellationToken ct)
        {
            Calls++;
            return Next?.Invoke(ct) ?? Task.FromResult(Opened());
        }

        // ── GĐ5b: mở chế độ đăng nhập + luồng đăng nhập (xem MainViewModelLoginTests) ──

        public int LoginOpenCalls;
        public List<SellerLoginCredentials> Logins { get; } = [];

        /// <summary>Luồng đăng nhập giả: nhận (log, status, ct). Mặc định: đăng nhập được ngay.</summary>
        public Func<Action<string>, Action<string>, CancellationToken, Task<SellerLoginResult>>? NextLogin;

        public Task<SellerGateResult> OpenForLoginAsync(SellerAccount account, SellerUploadSettings settings, string profileDir, CancellationToken ct)
        {
            LoginOpenCalls++;
            return Next?.Invoke(ct) ?? Task.FromResult(Opened());
        }

        public Task<SellerLoginResult> LoginAsync(SellerBrowserSession session, SellerLoginCredentials credentials, Action<string> log,
            Action<string> status, CancellationToken ct)
        {
            Logins.Add(credentials);
            return NextLogin?.Invoke(log, status, ct)
                   ?? Task.FromResult(new SellerLoginResult(SellerLoginOutcome.LoggedIn, "Đã đăng nhập Shopee."));
        }
    }

    // ─────────────── M1: ô số sai thì không lưu ───────────────

    [Fact]
    public void OSoSai_KhongLuu_FileKhongDoi()
    {
        var vm = NewVm(new FakeOpener());
        vm.Settings.KiotKey = "SHARED-KEY-0001";
        vm.Settings.Region = "bac";
        vm.AddCommand.Execute(null);
        Assert.True(vm.TrySave(), string.Join("; ", vm.Errors));
        var before = File.ReadAllBytes(FilePath);

        // Tồn kho 0 là giá trị HỢP LỆ với Core — nếu bỏ chặn lỗi đọc ô, "abc" sẽ được lưu âm thầm thành 0.
        vm.Selected!.StockPerVariation = "abc";

        Assert.False(vm.TrySave());
        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Contains(vm.Errors, e => e.Contains("'abc'"));
        Assert.True(vm.IsDirty);
    }

    // ─────────────── M2: mỗi lúc một Brave ───────────────

    [Fact]
    public async Task DangCoBrave_KhongMoThem_CaTaiKhoanKhacLanChinhNo()
    {
        var opener = new FakeOpener();
        var vm = NewVm(opener);
        vm.AddCommand.Execute(null);
        vm.AddCommand.Execute(null);
        var a = vm.Accounts[0];
        var b = vm.Accounts[1];

        vm.Selected = a;
        await vm.OpenBraveCommand.ExecuteAsync(null);
        Assert.True(vm.HasSession);
        Assert.Equal(a.Id, vm.BrowserAccountId);
        Assert.Equal(1, opener.Calls);

        // Chính tài khoản đang mở.
        Assert.False(vm.OpenBraveCommand.CanExecute(null));
        await vm.OpenBraveCommand.ExecuteAsync(null);
        // Tài khoản khác.
        vm.Selected = b;
        Assert.False(vm.OpenBraveCommand.CanExecute(null));
        await vm.OpenBraveCommand.ExecuteAsync(null);

        Assert.Equal(1, opener.Calls);
        Assert.Equal(a.Id, vm.BrowserAccountId);

        // Đóng xong thì mở lại được.
        await vm.CloseBraveCommand.ExecuteAsync(null);
        Assert.Equal(1, opener.Closed);
        Assert.True(vm.OpenBraveCommand.CanExecute(null));
    }

    [Fact]
    public async Task DangMoDo_KhongMoThem()
    {
        var pending = new TaskCompletionSource<SellerGateResult>();
        var opener = new FakeOpener { Next = _ => pending.Task };
        var vm = NewVm(opener);
        vm.AddCommand.Execute(null);
        vm.AddCommand.Execute(null);

        vm.Selected = vm.Accounts[0];
        var first = vm.OpenBraveCommand.ExecuteAsync(null);
        Assert.True(vm.IsOpening);

        vm.Selected = vm.Accounts[1];
        Assert.False(vm.OpenBraveCommand.CanExecute(null));
        await vm.OpenBraveCommand.ExecuteAsync(null);
        Assert.Equal(1, opener.Calls);

        pending.SetResult(opener.Opened());
        await first;
        Assert.True(vm.HasSession);
    }

    // ─────────────── M3: tắt app thì đóng Brave ───────────────

    [Fact]
    public async Task TatApp_DongBraveDangMo()
    {
        var opener = new FakeOpener();
        var vm = NewVm(opener);
        vm.AddCommand.Execute(null);
        await vm.OpenBraveCommand.ExecuteAsync(null);
        Assert.True(vm.HasSession);

        await vm.ShutdownAsync();

        Assert.Equal(1, opener.Closed);
    }

    [Fact]
    public async Task TatApp_KhiDangMoDo_HuyVaDongBraveVuaMoXong()
    {
        // Brave mở xong đúng lúc bị huỷ (launcher không kiểm huỷ sau khi đã Start) → vẫn phải đóng.
        var opener = new FakeOpener();
        var cancelled = false;
        opener.Next = ct =>
        {
            var tcs = new TaskCompletionSource<SellerGateResult>();
            ct.Register(() => { cancelled = true; tcs.TrySetResult(opener.Opened()); });
            return tcs.Task;
        };
        var vm = NewVm(opener);
        vm.AddCommand.Execute(null);

        var open = vm.OpenBraveCommand.ExecuteAsync(null);
        Assert.True(vm.IsOpening);

        await vm.ShutdownAsync();
        await open;

        Assert.True(cancelled);
        Assert.Equal(1, opener.Closed);
    }

    // ─────────────── che bí mật trên thanh trạng thái ───────────────

    [Fact]
    public async Task TrangThaiBrave_CheKeyTrongLyDoLoi()
    {
        const string key = "SHARED-KEY-0001";
        var opener = new FakeOpener
        {
            Next = _ => Task.FromResult(new SellerGateResult(SellerGateFailure.ProxyUnavailable, $"Kiot key={key} lỗi", null, null)),
        };
        var vm = NewVm(opener);
        vm.Settings.KiotKey = key;
        vm.AddCommand.Execute(null);

        await vm.OpenBraveCommand.ExecuteAsync(null);

        Assert.DoesNotContain(key, vm.BrowserStatus);
        Assert.Contains("key=*** lỗi", vm.BrowserStatus);
        Assert.DoesNotContain("0001", vm.BrowserStatus);
    }

    [Fact]
    public async Task TrangThaiBrave_CheKeyTrongNgoaiLe()
    {
        const string key = "OWN-PASS-ABCD";
        var opener = new FakeOpener { Next = _ => throw new InvalidOperationException($"hỏng proxy pass {key}") };
        var vm = NewVm(opener);
        vm.Settings.KiotKey = key;
        vm.AddCommand.Execute(null);

        await vm.OpenBraveCommand.ExecuteAsync(null);

        Assert.DoesNotContain(key, vm.BrowserStatus);
        Assert.DoesNotContain("ABCD", vm.BrowserStatus);
        Assert.Equal("x ***", vm.Redact($"x {key}"));
    }

    // ─────────────── vòng 3 ───────────────

    [Fact]
    public async Task ChoMoBrave_DungMotLanSuotPhien()
    {
        var opener = new FakeOpener();
        var vm = NewVm(opener);
        vm.AddCommand.Execute(null);
        vm.AddCommand.Execute(null);

        await vm.OpenBraveCommand.ExecuteAsync(null);
        await vm.CloseBraveCommand.ExecuteAsync(null);
        vm.Selected = vm.Accounts[0];
        await vm.OpenBraveCommand.ExecuteAsync(null);
        await vm.CloseBraveCommand.ExecuteAsync(null);

        Assert.Equal(2, opener.Calls);
        Assert.Equal(1, _factoryCalls);
    }

    [Fact]
    public async Task TrungThuMucProfile_KhongMoBrave_CaHaiTaiKhoan()
    {
        var opener = new FakeOpener();
        var vm = NewVm(opener);
        vm.AddCommand.Execute(null);
        vm.AddCommand.Execute(null);
        var a = vm.Accounts[0];
        var b = vm.Accounts[1];
        b.ProfileDir = a.ResolvedProfileDir;   // b mở nhầm vào profile (cookie) của a
        Assert.Contains(vm.Errors, e => e.Contains("chung một thư mục profile"));

        foreach (var acc in new[] { a, b })
        {
            vm.Selected = acc;
            Assert.False(vm.OpenBraveCommand.CanExecute(null));
            await vm.OpenBraveCommand.ExecuteAsync(null);
            Assert.Contains("chung một thư mục profile", vm.BrowserStatus);
        }
        Assert.Equal(0, opener.Calls);

        // Sửa xong thì mở được.
        b.ProfileDir = "";
        vm.Selected = b;
        Assert.True(vm.OpenBraveCommand.CanExecute(null));
        await vm.OpenBraveCommand.ExecuteAsync(null);
        Assert.Equal(1, opener.Calls);
    }

    [Fact]
    public async Task LoiNhipChay_KhongChanMoBrave()
    {
        var opener = new FakeOpener();
        var vm = NewVm(opener);
        vm.AddCommand.Execute(null);
        vm.Settings.PauseShopsMin = "1";   // lỗi nhịp chạy (< 5 phút) — không dính tới tài khoản nào
        Assert.True(vm.HasErrors);

        Assert.True(vm.OpenBraveCommand.CanExecute(null));
        await vm.OpenBraveCommand.ExecuteAsync(null);
        Assert.Equal(1, opener.Calls);
    }

    [Fact]
    public async Task DongChuaHan_GiuPhien_ChanMoMoi_ChoDongLai()
    {
        var opener = new FakeOpener();
        opener.CloseResults.Enqueue(false);
        var vm = NewVm(opener);
        vm.AddCommand.Execute(null);
        vm.AddCommand.Execute(null);
        await vm.OpenBraveCommand.ExecuteAsync(null);
        var session = vm.Session;

        await vm.CloseBraveCommand.ExecuteAsync(null);

        Assert.Same(session, vm.Session);
        Assert.Contains("chưa đóng hẳn", vm.BrowserStatus);
        Assert.True(vm.CloseBraveCommand.CanExecute(null));
        vm.Selected = vm.Accounts[0];
        Assert.False(vm.OpenBraveCommand.CanExecute(null));
        await vm.OpenBraveCommand.ExecuteAsync(null);
        Assert.Equal(1, opener.Calls);

        await vm.CloseBraveCommand.ExecuteAsync(null);   // lần 2 đóng được
        Assert.Null(vm.Session);
        Assert.Equal(2, opener.Closed);
        Assert.True(vm.OpenBraveCommand.CanExecute(null));
    }

    [Fact]
    public void ProxyTinhNhapSai_KhongLoMatKhauRaMan()
    {
        var vm = NewVm(new FakeOpener());
        vm.AddCommand.Execute(null);
        var f = vm.Selected!;
        f.Proxy = Shopee.Upload.Forms.ProxyChoice.Static;
        f.StaticProxy = "myuser:Secret99:1.2.3.4:8080";

        vm.Log("thử in Secret99 ra log");

        Assert.NotEmpty(vm.Errors);
        Assert.DoesNotContain(vm.Errors, e => e.Contains("Secret99"));
        Assert.DoesNotContain("Secret99", f.StaticProxyPreview);
        Assert.DoesNotContain("Secret99", vm.LogText);
    }

    // ─────────────── vòng 4 ───────────────

    [Theory]
    [InlineData("shop-a", "shop-a")]
    [InlineData("shop-a", "SHOP-A")]
    [InlineData("", "")]
    public async Task FileCoIdTrung_KhongNem_CaHaiBiKhoa(string id1, string id2)
    {
        File.WriteAllText(FilePath,
            "{\"Settings\":{\"SharedProxy\":{\"KiotKey\":\"SHARED-KEY-0001\",\"Region\":\"bac\"}}," +
            $"\"Accounts\":[{{\"Id\":\"{id1}\",\"Label\":\"A\"}},{{\"Id\":\"{id2}\",\"Label\":\"B\",\"Keep\":1}}]}}");
        var opener = new FakeOpener();

        var vm = NewVm(opener);   // trước đây ném ArgumentException (ToDictionary trùng khoá) ngay trong constructor

        Assert.Equal(2, vm.Accounts.Count);
        Assert.Contains(vm.Errors, e => e.Contains(id1.Length == 0 ? "không hợp lệ" : "Trùng mã"));
        foreach (var acc in vm.Accounts)
        {
            vm.Selected = acc;
            Assert.False(vm.OpenBraveCommand.CanExecute(null));
            await vm.OpenBraveCommand.ExecuteAsync(null);
        }
        Assert.Equal(0, opener.Calls);
        Assert.False(vm.TrySave());   // Store chặn lưu
    }

    [Fact]
    public void FileCoIdTrung_GhepDungBanGoc_KhongChepTruongLaSangNhau()
    {
        var original = new SellerAccountsData
        {
            Accounts =
            [
                new SellerAccount { Id = "shop-a", Label = "A", ExtensionData = new() { ["Mode"] = System.Text.Json.JsonSerializer.SerializeToElement("x") } },
                new SellerAccount { Id = "SHOP-A", Label = "B" },
            ],
        };
        var forms = original.Accounts.Select(Shopee.Upload.Forms.SellerFormMapper.ToForm).ToList();

        var (data, _) = Shopee.Upload.Forms.SellerFormMapper.ToData(Shopee.Upload.Forms.SellerFormMapper.ToForm(original.Settings), forms, original);

        Assert.NotNull(data.Accounts[0].ExtensionData);
        Assert.Null(data.Accounts[1].ExtensionData);
        Assert.Equal("B", data.Accounts[1].Label);
    }

    [Fact]
    public async Task TatApp_DongThatBai_KhongDongLanHai()
    {
        var opener = new FakeOpener();
        opener.CloseResults.Enqueue(false);
        opener.CloseResults.Enqueue(false);
        var vm = NewVm(opener);
        vm.AddCommand.Execute(null);
        await vm.OpenBraveCommand.ExecuteAsync(null);

        await vm.ShutdownAsync();

        Assert.Equal(1, opener.Closed);
    }

    [Fact]
    public async Task ProcessDaDispose_QuetTienTrinh_PhatHienUserTuDongBrave()
    {
        var opener = new FakeOpener { DetachedProcess = true };
        var vm = NewVm(opener);
        vm.AddCommand.Execute(null);
        await vm.OpenBraveCommand.ExecuteAsync(null);

        await vm.CheckBrowserExitedAsync();   // Brave còn chạy → giữ phiên
        Assert.True(vm.HasSession);
        Assert.Equal(0, opener.Closed);

        opener.Running = false;               // user tự đóng Brave
        await vm.CheckBrowserExitedAsync();
        Assert.False(vm.HasSession);
        Assert.Equal(1, opener.Closed);
    }

    // ─────────────── GĐ5b: Mở + đăng nhập Shopee ───────────────

    private const string ShopeePass = "MatKhauShop#26";
    private const string MailPass = "MatKhauMail#77";

    private MainViewModel NewLoginVm(FakeOpener opener)
    {
        var vm = NewVm(opener);
        vm.Settings.KiotKey = "SHARED-KEY-0001";
        vm.AddCommand.Execute(null);
        var f = vm.Selected!;
        f.ShopeeLogin = "  shop.a@gmail.com ";
        f.ShopeePassword = ShopeePass;
        f.VerifyEmail = "shop.a@hotmail.com";
        f.VerifyEmailPassword = MailPass;
        return vm;
    }

    [Fact]
    public async Task MoVaDangNhap_MoCheDoDangNhap_ChayLuong_GiuBrave()
    {
        var opener = new FakeOpener();
        var vm = NewLoginVm(opener);

        Assert.True(vm.OpenAndLoginCommand.CanExecute(null));
        await vm.OpenAndLoginCommand.ExecuteAsync(null);

        Assert.Equal(1, opener.LoginOpenCalls);
        Assert.Equal(0, opener.Calls);                 // không đi đường mở thường
        var c = Assert.Single(opener.Logins);
        Assert.Equal("shop.a@gmail.com", c.Login);
        Assert.Equal(ShopeePass, c.Password);
        Assert.Equal("shop.a@hotmail.com", c.VerifyEmail);
        Assert.Equal(MailPass, c.VerifyEmailPassword);
        Assert.Contains("Đã đăng nhập Shopee", vm.BrowserStatus);
        Assert.True(vm.HasSession);                     // xong thì KHÔNG tắt Brave
        Assert.Equal(0, opener.Closed);
        Assert.False(vm.IsLoggingIn);
        Assert.False(vm.OpenAndLoginCommand.CanExecute(null));   // vẫn một Brave mỗi lúc
        Assert.True(vm.CloseBraveCommand.CanExecute(null));
    }

    [Fact]
    public async Task MoVaDangNhap_MatKhauKhongLotRaLogVaTrangThai()
    {
        var opener = new FakeOpener
        {
            NextLogin = (log, status, _) =>
            {
                log($"thử in {ShopeePass} và {MailPass}");
                status($"trạng thái {ShopeePass}");
                return Task.FromResult(new SellerLoginResult(SellerLoginOutcome.Failed, $"lỗi {MailPass} {ShopeePass}"));
            },
        };
        var vm = NewLoginVm(opener);

        await vm.OpenAndLoginCommand.ExecuteAsync(null);

        foreach (var secret in new[] { ShopeePass, MailPass })
        {
            Assert.DoesNotContain(secret, vm.LogText);
            Assert.DoesNotContain(secret, vm.BrowserStatus);
        }
        Assert.Contains("lỗi", vm.BrowserStatus);
    }

    [Fact]
    public async Task DangDangNhap_Huy_DungLuong_GiuBrave()
    {
        var started = new TaskCompletionSource();
        var opener = new FakeOpener
        {
            NextLogin = async (_, _, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return new SellerLoginResult(SellerLoginOutcome.LoggedIn, "không tới đây");
            },
        };
        var vm = NewLoginVm(opener);

        var run = vm.OpenAndLoginCommand.ExecuteAsync(null);
        await started.Task;
        Assert.True(vm.IsLoggingIn);
        Assert.False(vm.OpenBraveCommand.CanExecute(null));
        Assert.True(vm.CancelOpenCommand.CanExecute(null));

        vm.CancelOpenCommand.Execute(null);
        await run;

        Assert.False(vm.IsLoggingIn);
        Assert.Contains("huỷ đăng nhập", vm.BrowserStatus);
        Assert.True(vm.HasSession);
        Assert.Equal(0, opener.Closed);
    }

    [Fact]
    public async Task DangDangNhap_DongBrave_DungLuongRoiDong()
    {
        var started = new TaskCompletionSource();
        var opener = new FakeOpener
        {
            NextLogin = async (_, _, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return new SellerLoginResult(SellerLoginOutcome.LoggedIn, "không tới đây");
            },
        };
        var vm = NewLoginVm(opener);

        var run = vm.OpenAndLoginCommand.ExecuteAsync(null);
        await started.Task;
        Assert.True(vm.CloseBraveCommand.CanExecute(null));

        await vm.CloseBraveCommand.ExecuteAsync(null);
        await run;

        Assert.Equal(1, opener.Closed);
        Assert.Null(vm.Session);
        Assert.False(vm.IsLoggingIn);
        Assert.True(vm.OpenAndLoginCommand.CanExecute(null));
    }

    [Fact]
    public async Task DangDangNhap_TatApp_DungLuongRoiDongBrave()
    {
        var started = new TaskCompletionSource();
        var opener = new FakeOpener
        {
            NextLogin = async (_, _, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return new SellerLoginResult(SellerLoginOutcome.LoggedIn, "không tới đây");
            },
        };
        var vm = NewLoginVm(opener);

        var run = vm.OpenAndLoginCommand.ExecuteAsync(null);
        await started.Task;
        await vm.ShutdownAsync();
        await run;

        Assert.Equal(1, opener.Closed);
    }

    [Fact]
    public async Task CoMatKhauMaThieuTaiKhoan_KhongMoBrave_LoiKhongChuaMatKhau()
    {
        var opener = new FakeOpener();
        var vm = NewLoginVm(opener);
        vm.Selected!.ShopeeLogin = "";

        Assert.Contains(vm.Errors, e => e.Contains("chưa nhập tài khoản Shopee"));
        Assert.DoesNotContain(vm.Errors, e => e.Contains(ShopeePass));
        Assert.False(vm.OpenAndLoginCommand.CanExecute(null));
        await vm.OpenAndLoginCommand.ExecuteAsync(null);
        Assert.Equal(0, opener.LoginOpenCalls);
        Assert.Empty(opener.Logins);
    }

    // ─────────────── GĐ5b vòng 2 ───────────────

    /// <summary>Luồng đăng nhập giả: chờ huỷ, rồi mất <paramref name="disconnectMs"/> để "ngắt Playwright"; ghi lúc kết thúc.</summary>
    private static Func<Action<string>, Action<string>, CancellationToken, Task<SellerLoginResult>> LoginUntilCancelled(
        TaskCompletionSource started, Action ended, int disconnectMs = 50) =>
        async (_, _, ct) =>
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { await Task.Delay(disconnectMs); ended(); throw; }
            return new SellerLoginResult(SellerLoginOutcome.LoggedIn, "không tới đây");
        };

    [Fact]
    public async Task TatAppKhiDangDangNhap_DungLuongXongMoiDongBrave()
    {
        var started = new TaskCompletionSource();
        var loginEnded = false;
        bool? endedAtClose = null;
        var opener = new FakeOpener();
        opener.NextLogin = LoginUntilCancelled(started, () => loginEnded = true);
        opener.OnClose = () => endedAtClose ??= loginEnded;
        var vm = NewLoginVm(opener);

        var run = vm.OpenAndLoginCommand.ExecuteAsync(null);
        await started.Task;
        await vm.ShutdownAsync();
        await run;

        Assert.Equal(1, opener.Closed);
        Assert.True(endedAtClose, "Brave bị đóng khi luồng đăng nhập (Playwright) chưa dừng.");
    }

    [Fact]
    public async Task DongBraveKhiDangDangNhap_DungLuongXongMoiDong()
    {
        var started = new TaskCompletionSource();
        var loginEnded = false;
        bool? endedAtClose = null;
        var opener = new FakeOpener();
        opener.NextLogin = LoginUntilCancelled(started, () => loginEnded = true);
        opener.OnClose = () => endedAtClose ??= loginEnded;
        var vm = NewLoginVm(opener);

        var run = vm.OpenAndLoginCommand.ExecuteAsync(null);
        await started.Task;
        await vm.CloseBraveCommand.ExecuteAsync(null);
        await run;

        Assert.True(endedAtClose);
    }

    [Fact]
    public async Task LuongDangNhapKhongChiuDung_TatAppVaDongBraveKhongTreoQuaTran()
    {
        // Lệnh Playwright kẹt, không nhận huỷ: tắt app / đóng Brave chỉ chờ tới trần rồi đi tiếp.
        var started = new TaskCompletionSource();
        var never = new TaskCompletionSource<SellerLoginResult>();
        var opener = new FakeOpener { NextLogin = (_, _, _) => { started.TrySetResult(); return never.Task; } };
        var vm = NewLoginVm(opener);
        vm.LoginStopTimeout = TimeSpan.FromMilliseconds(150);

        _ = vm.OpenAndLoginCommand.ExecuteAsync(null);
        await started.Task;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await vm.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"tắt app mất {sw.Elapsed}");
        Assert.Equal(1, opener.Closed);
        Assert.Contains("chưa dừng", vm.LogText);
        never.SetResult(new SellerLoginResult(SellerLoginOutcome.Failed, "x"));
    }

    [Fact]
    public async Task DangNhapXong_TrangThaiNhacConCongDieuKhien()
    {
        var vm = NewLoginVm(new FakeOpener());

        await vm.OpenAndLoginCommand.ExecuteAsync(null);

        Assert.Contains("Đã đăng nhập Shopee", vm.BrowserStatus);
        Assert.Contains("còn cổng điều khiển trên máy", vm.BrowserStatus);
        Assert.Contains("Đóng Brave", vm.BrowserStatus);
    }
}
