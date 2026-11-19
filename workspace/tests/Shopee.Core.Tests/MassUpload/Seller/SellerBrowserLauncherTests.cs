using System.Diagnostics;
using System.Text.Json.Nodes;
using Shopee.Core.Browser;
using Shopee.Core.MassUpload.Seller;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>GĐ4: luật của <see cref="SellerBrowserLauncher"/> với host tiến trình giả — chính sách máy chặn; còn Brave sót trên
/// profile thì không khởi động; mở xong phải đúng proxy + cờ WebRTC, sai thì tắt; mở trùng profile thì ném; thư mục proxy-auth bị
/// xoá khi đóng và khi mở lỗi; dọn bản sót chỉ của ĐÚNG tài khoản; ghi lớp 2 vào Preferences / Local State.</summary>
public sealed class SellerBrowserLauncherTests : IDisposable
{
    private readonly string _dir = SellerTestData.NewTempDir();
    private readonly FakeHost _host = new();
    private readonly SellerBrowserLauncher _launcher;

    public SellerBrowserLauncherTests() => _launcher = NewLauncher();

    private SellerBrowserLauncher NewLauncher() =>
        new(AuthRoot, host: _host)
        {
            GracefulCloseTimeout = TimeSpan.FromMilliseconds(50),
            ExitTimeout = TimeSpan.FromMilliseconds(50),
            PostStartTimeout = TimeSpan.FromMilliseconds(80),
            PollInterval = TimeSpan.FromMilliseconds(5),
        };

    public void Dispose() => SellerTestData.TryDelete(_dir);

    private string AuthRoot => Path.Combine(_dir, "proxy-auth");
    private string ProfileOf(string id) => Path.Combine(_dir, "profiles", id);

    private static readonly ProxyLease Auth = new(ProxyScheme.Http, "proxy.vn", 3128, "alice", "s3cret");
    private static readonly ProxyLease Plain = new(ProxyScheme.Http, "113.160.0.1", 20001);

    private SellerLaunchRequest Req(string id, ProxyLease? proxy = null) =>
        new(id, ProfileOf(id), proxy ?? Auth, "113.160.0.1", []);

    private string[] AuthDirs() => Directory.Exists(AuthRoot) ? Directory.GetDirectories(AuthRoot).Select(d => Path.GetFileName(d)).ToArray() : [];

    [Fact]
    public async Task ConBraveSotSauKhiDon_Nem_KhongKhoiDong()
    {
        // Brave mở tay trên profile bằng đường dẫn '/' + khác hoa/thường, dọn không chết.
        _host.Unkillable.Add(new SellerProcInfo(4242, $"brave.exe \"--user-data-dir={ProfileOf("a").Replace('\\', '/').ToUpperInvariant()}/\""));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _launcher.LaunchAsync(Req("a"), CancellationToken.None));

        Assert.Contains("Còn Brave", ex.Message);
        Assert.Equal(1, _host.Reaps);
        Assert.Empty(_host.Starts);
        Assert.Empty(AuthDirs());

        // Profile được nhả: dọn được Brave sót rồi thì mở lại bình thường.
        _host.Unkillable.Clear();
        var s = await _launcher.LaunchAsync(Req("a"), CancellationToken.None);
        Assert.Single(_host.Starts);
        await s.CloseAsync();
    }

    [Fact]
    public async Task ChinhSachMayDeProxy_Nem_KhongDonKhongKhoiDong()
    {
        _host.Policies.AddRange([@"HKLM\DnsOverHttpsMode", @"HKCU\ProxyMode"]);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _launcher.LaunchAsync(Req("a"), CancellationToken.None));
        Assert.Contains(@"HKCU\ProxyMode", ex.Message);
        Assert.DoesNotContain("DnsOverHttpsMode", ex.Message);
        Assert.Equal(0, _host.Reaps);
        Assert.Empty(_host.Starts);
    }

    [Fact]
    public async Task MoXong_TienTrinhChinhSaiProxy_TatHet_Nem()
    {
        // Lệnh mở bị chuyển sang một Brave khác (vd không proxy) — tiến trình chính của profile mang cờ khác.
        _host.MainCommandLine = p => $"brave.exe \"--user-data-dir={p}\" --proxy-server=http://9.9.9.9:1 {SellerBrowserArgs.WebRtcFlag}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _launcher.LaunchAsync(Req("a"), CancellationToken.None));

        Assert.Contains("--proxy-server=http://proxy.vn:3128", ex.Message);
        Assert.Equal(1, _host.Kills);
        Assert.Empty(_host.Running);
        Assert.Empty(AuthDirs());
    }

    [Fact]
    public async Task MoXong_ThieuCoWebRtc_TatHet_Nem()
    {
        _host.MainCommandLine = p => $"brave.exe \"--user-data-dir={p}\" --proxy-server=http://proxy.vn:3128";
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _launcher.LaunchAsync(Req("a"), CancellationToken.None));
        Assert.Contains("webrtc", ex.Message);
        Assert.Equal(1, _host.Kills);
    }

    [Fact]
    public async Task MoXong_KhongThayTienTrinhChinh_TatHet_Nem()
    {
        _host.MainCommandLine = _ => null;
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _launcher.LaunchAsync(Req("a"), CancellationToken.None));
        Assert.Contains("không thấy tiến trình", ex.Message);
        Assert.Equal(1, _host.Kills);
    }

    [Fact]
    public async Task MoTrungProfileTrongApp_Nem_DongRoiMoLaiDuoc()
    {
        var s1 = await _launcher.LaunchAsync(Req("a"), CancellationToken.None);
        var other = NewLauncher();   // instance khác vẫn chung danh sách

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => other.LaunchAsync(Req("a"), CancellationToken.None));
        Assert.Contains("đang mở", ex.Message);
        Assert.Single(_host.Starts);

        _host.OnRequestClose = () => _host.Running.Clear();
        Assert.True(await s1.CloseAsync());
        var s2 = await _launcher.LaunchAsync(Req("a"), CancellationToken.None);
        Assert.Equal(2, _host.Starts.Count);
        await s2.CloseAsync();
    }

    [Fact]
    public async Task ProxyAuth_NapKemKhiMo_XoaKhiDong_DongCoXinDongEm()
    {
        var s = await _launcher.LaunchAsync(Req("a"), CancellationToken.None);
        var dir = Assert.Single(AuthDirs());
        Assert.Contains($"--load-extension={Path.Combine(AuthRoot, dir)}", _host.Starts.Single());
        Assert.DoesNotContain("s3cret", _host.Starts.Single());

        _host.OnRequestClose = () => _host.Running.Clear();     // Brave đóng êm

        Assert.True(await s.CloseAsync());
        Assert.Empty(AuthDirs());
        Assert.Equal(0, _host.Kills);
        Assert.NotEmpty(_host.CloseRequests);
    }

    [Fact]
    public async Task DongEmKhongDuoc_BuocTat_VanXoaProxyAuth()
    {
        var s = await _launcher.LaunchAsync(Req("a"), CancellationToken.None);

        Assert.True(await s.CloseAsync());
        Assert.Equal(1, _host.Kills);
        Assert.Empty(AuthDirs());
    }

    [Fact]
    public async Task KhoiDongLoi_XoaProxyAuth_NhaProfile()
    {
        _host.StartThrows = new InvalidOperationException("brave hỏng");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _launcher.LaunchAsync(Req("a"), CancellationToken.None));
        Assert.Empty(AuthDirs());

        _host.StartThrows = null;
        var s = await _launcher.LaunchAsync(Req("a"), CancellationToken.None);
        await s.CloseAsync();
    }

    [Fact]
    public async Task KhongCoMatKhau_KhongSinhProxyAuth()
    {
        var s = await _launcher.LaunchAsync(Req("a", Plain), CancellationToken.None);
        Assert.Empty(AuthDirs());
        Assert.DoesNotContain("--load-extension", _host.Starts.Single());
        await s.CloseAsync();
    }

    [Fact]
    public async Task ChuanBiProfile_GhiLop2WebRtc_TatDoH_GiuKhoaKhac()
    {
        var profile = ProfileOf("a");
        Directory.CreateDirectory(Path.Combine(profile, "Default"));
        File.WriteAllText(Path.Combine(profile, "Default", "Preferences"),
            """{"profile":{"name":"Shop A","exit_type":"Crashed"},"webrtc":{"ip_handling_policy":"default","udp_port_range":"1-2"},"x":[1,2]}""");
        File.WriteAllText(Path.Combine(profile, "Local State"), """{"browser":{"enabled_labs_experiments":["a"]},"dns_over_https":{"mode":"automatic","templates":"t"}}""");

        var s = await _launcher.LaunchAsync(Req("a", Plain), CancellationToken.None);
        await s.CloseAsync();

        var prefs = JsonNode.Parse(File.ReadAllText(Path.Combine(profile, "Default", "Preferences")))!;
        Assert.Equal("disable_non_proxied_udp", (string?)prefs["webrtc"]!["ip_handling_policy"]);
        Assert.False((bool)prefs["webrtc"]!["multiple_routes_enabled"]!);
        Assert.False((bool)prefs["webrtc"]!["nonproxied_udp_enabled"]!);
        Assert.Equal("1-2", (string?)prefs["webrtc"]!["udp_port_range"]);
        Assert.Equal("Shop A", (string?)prefs["profile"]!["name"]);
        Assert.Equal(2, prefs["x"]!.AsArray().Count);

        var ls = JsonNode.Parse(File.ReadAllText(Path.Combine(profile, "Local State")))!;
        Assert.Equal("off", (string?)ls["dns_over_https"]!["mode"]);
        Assert.Equal("t", (string?)ls["dns_over_https"]!["templates"]);
        Assert.Equal("a", (string?)ls["browser"]!["enabled_labs_experiments"]![0]);
    }

    [Fact]
    public void Prefs_ChuaCoFile_TaoMoi_FileHong_DoiTenRoiGhiMoi_KhongBom()
    {
        var p1 = Path.Combine(_dir, "p1");
        SellerProfilePrefs.Apply(p1);
        var bytes = File.ReadAllBytes(Path.Combine(p1, "Default", "Preferences"));
        Assert.NotEqual(0xEF, bytes[0]);                              // UTF-8 không BOM như Chromium
        Assert.Equal("off", (string?)JsonNode.Parse(File.ReadAllText(Path.Combine(p1, "Local State")))!["dns_over_https"]!["mode"]);

        var p2 = Path.Combine(_dir, "p2");
        Directory.CreateDirectory(Path.Combine(p2, "Default"));
        File.WriteAllText(Path.Combine(p2, "Default", "Preferences"), "[1,2");
        SellerProfilePrefs.Apply(p2);
        Assert.Single(Directory.GetFiles(Path.Combine(p2, "Default"), "Preferences.corrupt-*"));
        Assert.Equal("disable_non_proxied_udp",
            (string?)JsonNode.Parse(File.ReadAllText(Path.Combine(p2, "Default", "Preferences")))!["webrtc"]!["ip_handling_policy"]);
        Assert.Empty(Directory.GetFiles(Path.Combine(p2, "Default"), "*.tmp"));
    }

    [Fact]
    public async Task DonBanSot_ChiDungTaiKhoan_KhongXoaNhamIdCoGach()
    {
        var hex = new string('a', 32);
        foreach (var name in new[] { $"a-{hex}", $"a-b-{hex}", $"a-{hex}x", "a-old" })
            Directory.CreateDirectory(Path.Combine(AuthRoot, name));

        var s = await _launcher.LaunchAsync(Req("a"), CancellationToken.None);
        var left = AuthDirs();
        Assert.DoesNotContain($"a-{hex}", left);                 // bản sót của "a" đã dọn
        Assert.Contains($"a-b-{hex}", left);                     // của tài khoản "a-b" — giữ
        Assert.Contains($"a-{hex}x", left);
        Assert.Contains("a-old", left);
        await s.CloseAsync();
    }

    [Theory]
    [InlineData("a-0123456789abcdef0123456789abcdef", "a", true)]
    [InlineData("a-b-0123456789abcdef0123456789abcdef", "a", false)]
    [InlineData("a-b-0123456789abcdef0123456789abcdef", "a-b", true)]
    [InlineData("a-0123456789abcdef0123456789abcde", "a", false)]
    [InlineData("xa-0123456789abcdef0123456789abcdef", "a", false)]
    public void IsAuthDirOf_KhopChinhXac(string name, string id, bool expected) =>
        Assert.Equal(expected, SellerBrowserLauncher.IsAuthDirOf(name, id));

    /// <summary>Host tiến trình giả. Sau <see cref="Start"/> profile có tiến trình chính (dòng lệnh = đúng dòng lệnh đã mở, hoặc
    /// <see cref="MainCommandLine"/>) + 1 renderer. <see cref="Unkillable"/> = tiến trình reap không giết được. Mọi phép tìm theo
    /// profile đi qua <see cref="SellerBraveProcesses.FilterByProfile"/> thật.</summary>
    private sealed class FakeHost : ISellerBrowserHost
    {
        public List<SellerProcInfo> Unkillable { get; } = [];
        public List<SellerProcInfo> Running { get; } = [];
        public List<string> Policies { get; } = [];
        public List<string> Starts { get; } = [];
        public List<int> CloseRequests { get; } = [];
        public int Reaps, Kills;
        public Exception? StartThrows { get; set; }
        public Func<string, string?>? MainCommandLine { get; set; }
        public Action? OnRequestClose { get; set; }

        public string? FindBraveExe() => @"C:\fake\brave.exe";
        public IReadOnlyList<string> ReadBravePolicyValues() => Policies;
        public void ReapProfile(string profileDir) { Reaps++; Running.Clear(); }

        public IReadOnlyList<SellerProcInfo> FindProfileProcesses(string profileDir) =>
            SellerBraveProcesses.FilterByProfile(
                Unkillable.Concat(Running).Select(p => new Shopee.Core.Platform.ProcessCommandLine(p.Pid, "brave.exe", p.CommandLine)),
                profileDir);

        public void PrepareProfile(string profileDir) => Directory.CreateDirectory(profileDir);

        public Process? Start(string exe, string commandLine)
        {
            if (StartThrows is not null) throw StartThrows;
            Starts.Add(commandLine);
            var profile = BraveProcessReaper.ExtractUserDataDir(commandLine)!;
            var main = MainCommandLine is null ? $"\"{exe}\" {commandLine}" : MainCommandLine(profile);
            if (main is not null) Running.Add(new SellerProcInfo(100, main));
            Running.Add(new SellerProcInfo(101, $"\"{exe}\" --type=renderer \"--user-data-dir={profile}\""));
            return null;
        }

        public void RequestClose(int pid) { CloseRequests.Add(pid); OnRequestClose?.Invoke(); }
        public void Kill(Process? process, string profileDir) { Kills++; Running.Clear(); }
    }
}
