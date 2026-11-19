using Shopee.Core.MassUpload.Seller;
using Shopee.Core.Platform;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>GĐ4: khớp tiến trình Brave theo profile (so chuẩn hoá), kiểm tiến trình chính, lọc chính sách máy.</summary>
public sealed class SellerBraveProcessesTests
{
    private static ProcessCommandLine P(int pid, string cmd) => new(pid, "brave.exe", cmd);

    [Fact]
    public void FilterByProfile_BatCaDuongDanGachXuoi_HoaThuong_NgoacKep_GachCuoi()
    {
        var all = new[]
        {
            P(1, @"brave.exe --user-data-dir=C:/Data/Profiles/Shop-01"),
            P(2, @"brave.exe ""--user-data-dir=c:\data\profiles\shop-01\"""),
            P(3, @"brave.exe --user-data-dir=""C:\Data\Profiles\Shop-01"" --type=renderer"),
            P(4, @"brave.exe --user-data-dir=C:\Data\Profiles\Shop-010"),
            P(5, @"brave.exe --user-data-dir=C:\Data\Profiles"),
            P(6, @"brave.exe --profile-directory=Default"),
        };
        var hit = SellerBraveProcesses.FilterByProfile(all, @"C:\Data\Profiles\Shop-01");
        Assert.Equal([1, 2, 3], hit.Select(h => h.Pid));
    }

    [Fact]
    public void Normalize_MoTenNgan83_NeuCo()
    {
        var temp = Path.GetTempPath();
        var n = SellerBraveProcesses.NormalizeProfilePath(temp);
        Assert.True(Directory.Exists(n));
        if (temp.Contains('~')) Assert.DoesNotContain("~", n);   // máy có tên ngắn 8.3 trong %TEMP%
        Assert.True(SellerBraveProcesses.SameProfile(temp.Replace('\\', '/').ToUpperInvariant(), n));
    }

    [Fact]
    public void CheckMain_DungProxyVaWebRtc_Dat_SaiThiLyDo()
    {
        var lease = new ProxyLease(ProxyScheme.Http, "1.2.3.4", 8080);
        var ok = $@"""C:\b\brave.exe"" ""--user-data-dir=C:\p q"" --proxy-server=http://1.2.3.4:8080 {SellerBrowserArgs.WebRtcFlag} about:blank";
        var renderer = new SellerProcInfo(2, @"brave.exe --type=renderer --user-data-dir=C:\p");
        Assert.Null(SellerBraveProcesses.CheckMain([new(1, ok), renderer], lease));

        Assert.Contains("không thấy", SellerBraveProcesses.CheckMain([renderer], lease));
        Assert.NotNull(SellerBraveProcesses.CheckMain([new(1, ok.Replace("8080", "8081"))], lease));
        Assert.NotNull(SellerBraveProcesses.CheckMain([new(1, ok + " --proxy-server=http://9.9.9.9:1")], lease));
        Assert.NotNull(SellerBraveProcesses.CheckMain([new(1, ok.Replace(SellerBrowserArgs.WebRtcFlag, "--force-webrtc-ip-handling-policy=disable_non_proxied_udp"))], lease));
        Assert.NotNull(SellerBraveProcesses.CheckMain([new(1, ok), new(3, "brave.exe --user-data-dir=C:\\p")], lease));   // 2 tiến trình chính, 1 sai
    }

    [Fact]
    public void BlockingPolicies_ChiProxyVaWebRtc()
    {
        var found = SellerBraveProcesses.BlockingPolicies([@"HKLM\ProxyMode", @"HKCU\ProxySettings", @"HKLM\WebRtcIPHandlingPolicy",
            @"HKLM\DnsOverHttpsMode", @"HKCU\BraveRewardsDisabled", @"HKLM\ProxyServer"]);
        Assert.Equal([@"HKLM\ProxyMode", @"HKCU\ProxySettings", @"HKLM\WebRtcIPHandlingPolicy", @"HKLM\ProxyServer"], found);
    }
}
