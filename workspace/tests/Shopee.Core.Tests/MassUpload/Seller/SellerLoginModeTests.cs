using System.Diagnostics;
using System.Net;
using Shopee.Core.Browser;
using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>GĐ5b: chế độ mở <see cref="SellerLaunchMode.Login"/>. Cờ CDP CHỈ có ở chế độ này (chế độ thường vẫn cấm), cổng phải
/// chỉ nghe loopback, mọi luật GĐ4 (proxy, WebRTC, kiểm sau khi mở) giữ nguyên; gate đi cùng một đường. Kèm luật dữ liệu đăng nhập
/// của Store.</summary>
public sealed class SellerLoginModeTests : IDisposable
{
    private readonly string _dir = SellerTestData.NewTempDir();

    public void Dispose() => SellerTestData.TryDelete(_dir);

    private static readonly ProxyLease Plain = new(ProxyScheme.Http, "113.160.0.1", 20001);
    private const string Profile = @"C:\Data\upload\profiles\shop-01";

    // ─────────────── tham số dòng lệnh ───────────────

    [Fact]
    public void Args_CheDoDangNhap_DungMotCoCdp_VanDuLuatGd4()
    {
        var args = SellerBrowserArgs.Build(Profile, Plain, null, mode: SellerLaunchMode.Login);

        Assert.Single(args, a => a.Contains("remote-debugging", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(SellerBrowserArgs.CdpFlag, args);
        Assert.Contains("--proxy-server=http://113.160.0.1:20001", args);
        Assert.Contains(SellerBrowserArgs.WebRtcFlag, args);
        Assert.Contains("--disable-quic", args);
        Assert.DoesNotContain(args, a => a.Contains("enable-automation", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(args, a => a.Contains("remote-debugging-address", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("about:blank", args[^1]);
    }

    [Fact]
    public void Args_CheDoThuong_KhongCoCoCdpNao()
    {
        foreach (var args in new[]
                 {
                     SellerBrowserArgs.Build(Profile, Plain, null),
                     SellerBrowserArgs.Build(Profile, Plain, [@"C:\ext\a"], mode: SellerLaunchMode.Normal),
                 })
        {
            Assert.DoesNotContain(args, SellerBraveProcesses.IsCdpToken);
        }
    }

    // ─────────────── kiểm tiến trình chính ───────────────

    private static string MainCmd(string extra = "") =>
        $"brave.exe \"--user-data-dir={Profile}\" --proxy-server=http://113.160.0.1:20001 {SellerBrowserArgs.WebRtcFlag} {extra}".TrimEnd();

    [Theory]
    [InlineData("--remote-debugging-port=0")]
    [InlineData("--remote-debugging-port=9222")]
    [InlineData("--remote-debugging-pipe")]
    [InlineData("--remote-debugging-address=0.0.0.0")]
    [InlineData("--remote-allow-origins=*")]
    public void CheckMain_CheDoThuong_CamMoiCoCdp(string flag)
    {
        Assert.Null(SellerBraveProcesses.CheckMain([new(1, MainCmd())], Plain));
        var err = SellerBraveProcesses.CheckMain([new(1, MainCmd(flag))], Plain);
        Assert.NotNull(err);
        Assert.Contains("CDP", err);
        Assert.NotNull(SellerBraveProcesses.CheckMain([new(1, MainCmd(flag))], Plain, SellerLaunchMode.Normal));
    }

    [Fact]
    public void CheckMain_CheDoDangNhap_DungMotCoCdpCong0()
    {
        Assert.Null(SellerBraveProcesses.CheckMain([new(1, MainCmd(SellerBrowserArgs.CdpFlag))], Plain, SellerLaunchMode.Login));

        Assert.NotNull(SellerBraveProcesses.CheckMain([new(1, MainCmd())], Plain, SellerLaunchMode.Login));   // thiếu cờ
        Assert.NotNull(SellerBraveProcesses.CheckMain([new(1, MainCmd("--remote-debugging-port=9222"))], Plain, SellerLaunchMode.Login));
        Assert.NotNull(SellerBraveProcesses.CheckMain(
            [new(1, MainCmd($"{SellerBrowserArgs.CdpFlag} --remote-debugging-address=0.0.0.0"))], Plain, SellerLaunchMode.Login));
        Assert.NotNull(SellerBraveProcesses.CheckMain(
            [new(1, MainCmd($"{SellerBrowserArgs.CdpFlag} {SellerBrowserArgs.CdpFlag}"))], Plain, SellerLaunchMode.Login));
        // Luật GĐ4 vẫn áp: sai proxy / thiếu WebRTC là không đạt dù cờ CDP đúng.
        Assert.NotNull(SellerBraveProcesses.CheckMain(
            [new(1, MainCmd(SellerBrowserArgs.CdpFlag).Replace("20001", "20002"))], Plain, SellerLaunchMode.Login));
        Assert.NotNull(SellerBraveProcesses.CheckMain(
            [new(1, MainCmd(SellerBrowserArgs.CdpFlag).Replace(SellerBrowserArgs.WebRtcFlag, ""))], Plain, SellerLaunchMode.Login));
    }

    // ─────────────── DevToolsActivePort + loopback ───────────────

    [Theory]
    [InlineData("53422\n/devtools/browser/abc", 53422)]
    [InlineData("53422\r\n/devtools/browser/abc", 53422)]
    [InlineData(" 1 ", 1)]
    [InlineData("", null)]
    [InlineData("abc\n/x", null)]
    [InlineData("0", null)]
    [InlineData("70000", null)]
    [InlineData("-5", null)]
    public void ParseDevToolsActivePort(string content, int? expected) =>
        Assert.Equal(expected, SellerCdp.ParseDevToolsActivePort(content));

    [Fact]
    public void CongCdp_PhaiChiNgheLoopback()
    {
        Assert.Null(SellerCdp.CheckLoopbackOnly([new IPEndPoint(IPAddress.Loopback, 5000)], 5000));
        Assert.Null(SellerCdp.CheckLoopbackOnly([new IPEndPoint(IPAddress.IPv6Loopback, 5000), new IPEndPoint(IPAddress.Loopback, 5000)], 5000));
        Assert.NotNull(SellerCdp.CheckLoopbackOnly([new IPEndPoint(IPAddress.Loopback, 5000), new IPEndPoint(IPAddress.Any, 5000)], 5000));
        Assert.NotNull(SellerCdp.CheckLoopbackOnly([new IPEndPoint(IPAddress.Parse("192.168.1.5"), 5000)], 5000));
        Assert.NotNull(SellerCdp.CheckLoopbackOnly([new IPEndPoint(IPAddress.Loopback, 5001)], 5000));   // không thấy cổng
        Assert.NotNull(SellerCdp.CheckLoopbackOnly([], 5000));
        Assert.Equal("http://127.0.0.1:5000", SellerCdp.Endpoint(5000));
    }

    // ─────────────── launcher ───────────────

    private SellerBrowserLauncher NewLauncher(LoginHost host) =>
        new(Path.Combine(_dir, "proxy-auth"), host: host)
        {
            GracefulCloseTimeout = TimeSpan.FromMilliseconds(50),
            ExitTimeout = TimeSpan.FromMilliseconds(50),
            PostStartTimeout = TimeSpan.FromMilliseconds(80),
            PollInterval = TimeSpan.FromMilliseconds(5),
            DevToolsPortTimeout = TimeSpan.FromMilliseconds(150),
        };

    private string ProfileOf(string id) => Path.Combine(_dir, "profiles", id);

    private SellerLaunchRequest Req(string id, SellerLaunchMode mode) =>
        new(id, ProfileOf(id), Plain, "113.160.0.1", [], Mode: mode);

    [Fact]
    public async Task Launcher_DangNhap_DocCongMoi_KiemLoopback_PhienCoCdp()
    {
        var host = new LoginHost { WritePort = 53422 };
        var profile = ProfileOf("a");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, SellerCdp.DevToolsActivePortFile), "1111\n/devtools/browser/cu");   // cổng phiên trước

        var s = await NewLauncher(host).LaunchAsync(Req("a", SellerLaunchMode.Login), CancellationToken.None);

        Assert.Equal(53422, s.CdpPort);
        Assert.Equal("http://127.0.0.1:53422", s.CdpEndpoint);
        Assert.Contains(SellerBrowserArgs.CdpFlag, host.Starts.Single());
        Assert.Equal(0, host.Kills);
        await s.CloseAsync();
    }

    [Fact]
    public async Task Launcher_DangNhap_ChiCoFileCongCu_KhongDungCongCu_TatNem()
    {
        var host = new LoginHost { WritePort = null };   // Brave không ghi cổng mới
        var profile = ProfileOf("a");
        Directory.CreateDirectory(profile);
        File.WriteAllText(Path.Combine(profile, SellerCdp.DevToolsActivePortFile), "1111\n/devtools/browser/cu");
        host.Listeners.Add(new IPEndPoint(IPAddress.Loopback, 1111));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewLauncher(host).LaunchAsync(Req("a", SellerLaunchMode.Login), CancellationToken.None));

        Assert.Contains("DevToolsActivePort", ex.Message);
        Assert.Equal(1, host.Kills);
        Assert.Empty(host.Running);
    }

    [Fact]
    public async Task Launcher_DangNhap_CongNgheRaMang_TatNem_NhaProfile()
    {
        var host = new LoginHost { WritePort = 53422 };
        host.Listeners.Add(new IPEndPoint(IPAddress.Any, 53422));
        var launcher = NewLauncher(host);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => launcher.LaunchAsync(Req("a", SellerLaunchMode.Login), CancellationToken.None));

        Assert.Contains("không chỉ loopback", ex.Message);
        Assert.Equal(1, host.Kills);

        // Profile được nhả: sửa xong mở lại được.
        host.Listeners.Clear();
        var s = await launcher.LaunchAsync(Req("a", SellerLaunchMode.Login), CancellationToken.None);
        await s.CloseAsync();
    }

    [Fact]
    public async Task Launcher_DangNhap_TienTrinhChinhMatCoCdp_TatNem()
    {
        var host = new LoginHost { WritePort = 53422, DropCdpFromMain = true };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewLauncher(host).LaunchAsync(Req("a", SellerLaunchMode.Login), CancellationToken.None));

        Assert.Contains(SellerBrowserArgs.CdpFlag, ex.Message);
        Assert.Equal(1, host.Kills);
    }

    [Fact]
    public async Task Launcher_CheDoThuong_KhongCdp_KhongChoCong()
    {
        var host = new LoginHost { WritePort = null };

        var s = await NewLauncher(host).LaunchAsync(Req("a", SellerLaunchMode.Normal), CancellationToken.None);

        Assert.Null(s.CdpPort);
        Assert.Null(s.CdpEndpoint);
        Assert.DoesNotContain("remote-debugging", host.Starts.Single());
        Assert.Equal(0, host.Kills);
        await s.CloseAsync();
    }

    [Fact]
    public async Task Launcher_CheDoThuong_TienTrinhChinhCoCdp_TatNem()
    {
        // Lệnh mở bị chuyển sang một Brave khác đang mở CDP → chế độ thường phải từ chối.
        var host = new LoginHost { WritePort = null, AddCdpToMain = true };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NewLauncher(host).LaunchAsync(Req("a", SellerLaunchMode.Normal), CancellationToken.None));

        Assert.Contains("cấm CDP", ex.Message);
        Assert.Equal(1, host.Kills);
    }

    // ─────────────── gate ───────────────

    [Fact]
    public async Task Gate_CheDoDangNhap_DiCungDuong_KiemIpRoiMoiMo()
    {
        var clock = new FakeClock(SellerTestData.T0);
        var kiot = new FakeKiot(clock);
        var ips = new FakeIpWorld();
        var launcher = new FakeLauncher();
        var gate = new SellerRunGate(
            new SellerProxyProvider(kiot.Api, new SellerProxyProviderOptions { Tick = TimeSpan.FromMinutes(1) }, () => clock.Now, clock.Delay),
            new ExitIpVerifier(ips.Create), launcher, clock: () => clock.Now, delay: clock.Delay);
        var settings = new SellerUploadSettings { SharedProxy = new KiotProxySettings { KiotKey = "SHARED-KEY-0001", Region = "bac" } };

        var r = await gate.OpenAsync(new SellerAccount { Id = "shop-1" }, settings, @"C:\p\1", [], SellerLaunchMode.Login, CancellationToken.None);
        Assert.True(r.Ok, r.Reason);
        Assert.Equal(SellerLaunchMode.Login, launcher.Requests.Single().Mode);
        Assert.Equal(launcher.Requests[0].Proxy.ServerArg, ips.Handlers.Last()!.ServerArg);   // mở đúng proxy vừa kiểm

        var r2 = await gate.OpenAsync(new SellerAccount { Id = "shop-2" }, settings, @"C:\p\2", [], CancellationToken.None);
        Assert.True(r2.Ok, r2.Reason);
        Assert.Equal(SellerLaunchMode.Normal, launcher.Requests[1].Mode);
        Assert.NotEqual(r.ExitIp, r2.ExitIp);   // luật "khác mọi IP đã dùng" chung cho cả hai chế độ

        // IP ra trùng IP máy: chế độ đăng nhập cũng KHÔNG mở.
        ips.ExitIpFor = _ => ips.MachineIp;
        var r3 = await gate.OpenAsync(new SellerAccount { Id = "shop-3" }, settings, @"C:\p\3", [], SellerLaunchMode.Login, CancellationToken.None);
        Assert.Equal(SellerGateFailure.ExitIpRejected, r3.Failure);
        Assert.Equal(2, launcher.Requests.Count);

        // Không có proxy: không mở.
        var r4 = await gate.OpenAsync(new SellerAccount { Id = "shop-4" }, new SellerUploadSettings(), @"C:\p\4", [], SellerLaunchMode.Login,
            CancellationToken.None);
        Assert.Equal(SellerGateFailure.ProxyConfig, r4.Failure);
        Assert.Equal(2, launcher.Requests.Count);
    }

    // ─────────────── luật dữ liệu đăng nhập ───────────────

    private SellerAccountStore NewStore() => new(Path.Combine(_dir, "seller-accounts.json"), Path.Combine(_dir, "profiles"));

    private (List<string> Errors, List<string> Warnings) Validate(Action<SellerAccount> edit)
    {
        var a = new SellerAccount { Id = "shop-a", Label = "Shop A" };
        edit(a);
        return NewStore().Validate(new SellerAccountsData
        {
            Settings = new SellerUploadSettings { SharedProxy = new KiotProxySettings { KiotKey = "SHARED-KEY-0001", Region = "bac" } },
            Accounts = [a],
        });
    }

    [Fact]
    public void Validate_DuLieuDangNhap()
    {
        Assert.Empty(Validate(_ => { }).Errors);
        Assert.Empty(Validate(a => a.ShopeeLogin = "0901234567").Errors);   // chỉ có tài khoản: được
        Assert.Empty(Validate(a => { a.ShopeeLogin = "u"; a.ShopeePassword = "Pass#1234"; a.VerifyEmail = "x@hotmail.com"; a.VerifyEmailPassword = "M#1"; }).Errors);

        var noLogin = Validate(a => a.ShopeePassword = "Secret#Pass1");
        Assert.Contains(noLogin.Errors, e => e.Contains("chưa nhập tài khoản Shopee"));
        Assert.Contains(Validate(a => a.VerifyEmail = "x@hotmail.com").Errors, e => e.Contains("chưa nhập mật khẩu email"));
        Assert.Contains(Validate(a => a.VerifyEmailPassword = "MailSecret#9").Errors, e => e.Contains("chưa nhập email xác minh"));

        var longPass = Validate(a => { a.ShopeeLogin = "u"; a.ShopeePassword = "Abcdefghijklmnopq"; });   // 17 ký tự
        Assert.Empty(longPass.Errors);
        Assert.Contains(longPass.Warnings, w => w.Contains("16 ký tự"));
        Assert.Empty(Validate(a => { a.ShopeeLogin = "u"; a.ShopeePassword = "Abcdefghijklmnop"; }).Warnings);   // đúng 16

        foreach (var text in noLogin.Errors.Concat(longPass.Warnings))
        {
            Assert.DoesNotContain("Secret#Pass1", text);
            Assert.DoesNotContain("Abcdefghijklmnopq", text);
        }
    }

    [Theory]
    [InlineData(@"rel\profile")]                    // profile tương đối → nhánh 'continue' cũ
    [InlineData("")]                                // profile mặc định + proxy riêng sai → nhánh 'continue' của proxy
    public void Validate_ProfileHoacProxySai_VanBaoLoiDangNhap(string profile)
    {
        var (errors, _) = Validate(a =>
        {
            a.ProfileDir = profile.Length == 0 ? null : profile;
            if (profile.Length == 0) a.ProxyOverride = new SellerProxyOverride { Kind = SellerProxyKind.Static, Static = "khong-phai-proxy" };
            a.ShopeePassword = "Secret#Pass1";
            a.VerifyEmail = "x@hotmail.com";
        });
        Assert.Contains(errors, e => e.Contains("chưa nhập tài khoản Shopee"));
        Assert.Contains(errors, e => e.Contains("chưa nhập mật khẩu email"));
        Assert.True(errors.Count >= 3);
    }

    [Fact]
    public void Store_LuuDocLai_GiuBonTruongDangNhap_FileCuThieuTruongLaNull()
    {
        var store = NewStore();
        var data = new SellerAccountsData
        {
            Accounts = [new SellerAccount { Id = "shop-a", ShopeeLogin = "u1", ShopeePassword = "P#1", VerifyEmail = "e@hotmail.com", VerifyEmailPassword = "E#2" }],
        };
        Assert.True(store.Save(data).Saved);

        var a = NewStore().Load().Accounts.Single();
        Assert.Equal(("u1", "P#1", "e@hotmail.com", "E#2"), (a.ShopeeLogin, a.ShopeePassword, a.VerifyEmail, a.VerifyEmailPassword));

        File.WriteAllText(store.FilePath, """{"Accounts":[{"Id":"old"}]}""");
        var old = NewStore().Load().Accounts.Single();
        Assert.Null(old.ShopeeLogin);
        Assert.Null(old.ShopeePassword);
        Assert.Null(old.ExtensionData);
    }

    /// <summary>Host giả cho chế độ đăng nhập: Start ghi <c>DevToolsActivePort</c> (nếu <see cref="WritePort"/>) và dựng tiến trình
    /// chính từ đúng dòng lệnh đã mở; <see cref="Listeners"/> trống thì coi cổng vừa ghi đang nghe 127.0.0.1.</summary>
    private sealed class LoginHost : ISellerBrowserHost
    {
        public List<SellerProcInfo> Running { get; } = [];
        public List<string> Starts { get; } = [];
        public List<IPEndPoint> Listeners { get; } = [];
        public int Kills;
        public int? WritePort { get; set; }
        public bool DropCdpFromMain { get; set; }
        public bool AddCdpToMain { get; set; }

        public string? FindBraveExe() => @"C:\fake\brave.exe";
        public IReadOnlyList<string> ReadBravePolicyValues() => [];
        public void ReapProfile(string profileDir) => Running.Clear();
        public void PrepareProfile(string profileDir) => Directory.CreateDirectory(profileDir);

        public IReadOnlyList<SellerProcInfo> FindProfileProcesses(string profileDir) =>
            SellerBraveProcesses.FilterByProfile(
                Running.Select(p => new Shopee.Core.Platform.ProcessCommandLine(p.Pid, "brave.exe", p.CommandLine)), profileDir);

        public Process? Start(string exe, string commandLine)
        {
            Starts.Add(commandLine);
            var profile = BraveProcessReaper.ExtractUserDataDir(commandLine)!;
            var main = $"\"{exe}\" {commandLine}";
            if (DropCdpFromMain) main = main.Replace(SellerBrowserArgs.CdpFlag, "");
            if (AddCdpToMain) main += " --remote-debugging-port=9222";
            Running.Add(new SellerProcInfo(100, main));
            Running.Add(new SellerProcInfo(101, $"\"{exe}\" --type=renderer \"--user-data-dir={profile}\""));
            if (WritePort is { } p)
            {
                File.WriteAllText(Path.Combine(profile, SellerCdp.DevToolsActivePortFile), $"{p}\n/devtools/browser/moi");
            }
            return null;
        }

        public IReadOnlyList<IPEndPoint> FindTcpListeners() =>
            Listeners.Count > 0 ? Listeners : WritePort is { } p ? [new IPEndPoint(IPAddress.Loopback, p)] : [];

        public void RequestClose(int pid) => Running.Clear();
        public void Kill(Process? process, string profileDir) { Kills++; Running.Clear(); }
    }
}
