using Shopee.Core.Coordination;
using Shopee.Core.Scrape;

namespace Shopee.Core.Tests;

/// <summary>
/// Đẩy lô video: lọc file local, so hash với manifest, lõi đẩy từng file. Không cần hub: manifest và push là dữ liệu/fake.
/// Thư mục tạm riêng — không đụng video thật trên máy.
/// </summary>
public sealed class HubVideoBulkPushTests : IDisposable
{
    private const string H1 = "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"; // SHA-256("abc")

    private static readonly DateTime NowUtc = new(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "shopee-core-tests", "hub-video-bulk-" + Guid.NewGuid().ToString("N"));

    public HubVideoBulkPushTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string Make(string name, string content, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private LocalVideo Local(string name, string content)
    {
        var path = Make(name, content, NowUtc);
        return new LocalVideo(path, "videos/" + name, content.Length, NowUtc);
    }

    private static LocalVideo Item(string name, long size) =>
        new(Path.Combine("unused", name), "videos/" + name, size, NowUtc);

    private static FileManifestEntry Entry(string name, long size, string hash) =>
        new() { Name = name, Size = size, Hash = hash };

    [Fact]
    public void ListLocal_ChiGiuMp4Moi30Ngay()
    {
        Make("A.mp4", "aaa", NowUtc - TimeSpan.FromDays(1));
        Make("B.mp4", "bbb", NowUtc - TimeSpan.FromDays(31));
        Make("C.mp4.part", "ccc", NowUtc);
        Make("D.txt", "ddd", NowUtc);
        Make("E.mp4", "", NowUtc);
        Make("sub/F.mp4", "fff", NowUtc);
        Make("G.MP4", "ggg", NowUtc);
        Make("H.mp4", "hhh", NowUtc - HubVideoRetention.MaxAge + TimeSpan.FromSeconds(1));
        var big = Path.Combine(_dir, "BIG.mp4");
        using (var fs = File.Create(big)) fs.SetLength(SkuVideoSync.MaxPushBytes + 1);
        File.SetLastWriteTimeUtc(big, NowUtc);

        var list = HubVideoBulkPush.ListLocal(_dir, NowUtc);

        Assert.Equal(new[] { "videos/A.mp4", "videos/H.mp4" }, list.Select(v => v.RemoteName));
        var a = list.Single(v => v.RemoteName == "videos/A.mp4");
        Assert.Equal(Path.Combine(_dir, "A.mp4"), a.Path);
        Assert.Equal(3, a.Size);
    }

    [Fact]
    public void ListLocal_ThuMucKhongCo_RongKhongNem()
    {
        Assert.Empty(HubVideoBulkPush.ListLocal(Path.Combine(_dir, "khong-co"), NowUtc));
        Assert.Empty(HubVideoBulkPush.ListLocal("", NowUtc));
    }

    [Fact]
    public void SelectMissing_HubKhongCoKey_ChonVaKhongHash()
    {
        var calls = 0;
        var missing = HubVideoBulkPush.SelectMissing(
            [Item("A.mp4", 3)],
            [],
            _ => { calls++; return H1; });

        Assert.Equal(new[] { "videos/A.mp4" }, missing.Select(v => v.RemoteName));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void SelectMissing_SizeKhac_ChonVaKhongGoiHash()
    {
        var calls = 0;
        var missing = HubVideoBulkPush.SelectMissing(
            [Item("A.mp4", 3)],
            [Entry("videos/A.mp4", 4, H1.ToLowerInvariant())],
            _ => { calls++; return H1; });

        Assert.Single(missing);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void SelectMissing_CungSizeCungHashKhacHoaThuong_KhongChon()
    {
        var missing = HubVideoBulkPush.SelectMissing(
            [Item("A.mp4", 3)],
            [Entry("videos/A.mp4", 3, H1.ToLowerInvariant())],
            _ => H1);

        Assert.Empty(missing);
    }

    [Fact]
    public void SelectMissing_CungSizeKhacHash_Chon()
    {
        var missing = HubVideoBulkPush.SelectMissing(
            [Item("A.mp4", 3)],
            [Entry("videos/A.mp4", 3, new string('0', 64))],
            _ => H1);

        Assert.Equal(new[] { "videos/A.mp4" }, missing.Select(v => v.RemoteName));
    }

    [Fact]
    public void SelectMissing_HashNem_ChonVaKhongNem()
    {
        var missing = HubVideoBulkPush.SelectMissing(
            [Item("A.mp4", 3)],
            [Entry("videos/A.mp4", 3, H1)],
            _ => throw new IOException("file đang bị khóa"));

        Assert.Single(missing);
    }

    [Fact]
    public void SelectMissing_HuyTrongHash_NemRa()
    {
        Assert.ThrowsAny<OperationCanceledException>(() =>
        {
            HubVideoBulkPush.SelectMissing(
                [Item("A.mp4", 3)],
                [Entry("videos/A.mp4", 3, H1)],
                _ => throw new OperationCanceledException());
        });
    }

    [Fact]
    public void Sha256Hex_KhopSha256Chuan()
    {
        var path = Make("abc.mp4", "abc", NowUtc);
        Assert.Equal(H1, HubVideoBulkPush.Sha256Hex(path));
    }

    [Fact]
    public void Sha256Hex_DocDuocFileDangMoDeGhi()
    {
        var path = Path.Combine(_dir, "dang-ghi.mp4");
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        writer.Write("abc"u8);
        writer.Flush();

        Assert.Equal(H1, HubVideoBulkPush.Sha256Hex(path));
    }

    [Fact]
    public async Task PushAll_ItemLoi_DiTiepVaOnEachGoiDungTungFile()
    {
        var items = new[] { Local("A.mp4", "aaa"), Local("B.mp4", "bbb"), Local("C.mp4", "ccc") };
        var pushed = new List<string>();
        var each = new List<(string Name, bool Ok, string? Reason)>();

        var result = await HubVideoBulkPush.PushAllAsync(
            items,
            (name, _, _) =>
            {
                pushed.Add(name);
                if (name == "videos/B.mp4") throw new HttpRequestException("mất kết nối");
                return Task.FromResult<string?>(null);
            },
            (v, ok, reason) => each.Add((v.RemoteName, ok, reason)),
            CancellationToken.None);

        Assert.Equal((2, 1), result);
        Assert.Equal(new[] { "videos/A.mp4", "videos/B.mp4", "videos/C.mp4" }, pushed);
        Assert.Equal(
            new (string Name, bool Ok, string? Reason)[]
            {
                ("videos/A.mp4", true, null),
                ("videos/B.mp4", false, "mất kết nối"),
                ("videos/C.mp4", true, null),
            },
            each);
    }

    [Fact]
    public async Task PushAll_PushNemHuy_NemTiepVaKhongDayFileSau()
    {
        var items = new[] { Local("A.mp4", "aaa"), Local("B.mp4", "bbb"), Local("C.mp4", "ccc") };
        var pushed = new List<string>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HubVideoBulkPush.PushAllAsync(
            items,
            (name, _, _) =>
            {
                pushed.Add(name);
                if (name == "videos/B.mp4") throw new OperationCanceledException();
                return Task.FromResult<string?>(null);
            },
            (_, _, _) => { },
            CancellationToken.None));

        Assert.Equal(new[] { "videos/A.mp4", "videos/B.mp4" }, pushed);
    }

    [Fact]
    public async Task PushAll_GuiDungByteVaTenRemote()
    {
        var item = Local("A.mp4", "xyz");
        string? gotName = null;
        byte[]? gotBytes = null;

        var result = await HubVideoBulkPush.PushAllAsync(
            [item],
            (name, bytes, _) =>
            {
                gotName = name;
                gotBytes = bytes;
                return Task.FromResult<string?>(null);
            },
            (_, _, _) => { },
            CancellationToken.None);

        Assert.Equal((1, 0), result);
        Assert.Equal("videos/A.mp4", gotName);
        Assert.Equal(File.ReadAllBytes(item.Path), gotBytes);
    }

    [Fact]
    public async Task PushAll_FileBiXoaTruocKhiDay_LoiVaKhongGoiPush()
    {
        var item = Local("A.mp4", "aaa");
        File.Delete(item.Path);
        var pushed = 0;
        string? reason = null;

        var result = await HubVideoBulkPush.PushAllAsync(
            [item],
            (_, _, _) =>
            {
                pushed++;
                return Task.FromResult<string?>(null);
            },
            (_, _, r) => reason = r,
            CancellationToken.None);

        Assert.Equal((0, 1), result);
        Assert.Equal(0, pushed);
        Assert.Equal("không đọc được file", reason);
    }

    [Fact]
    public async Task PushAllAsync_DocDuocFileDangMoDeGhi()
    {
        var path = Path.Combine(_dir, "A.mp4");
        using var writer = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        writer.Write("abc"u8);
        writer.Flush();
        var item = new LocalVideo(path, "videos/A.mp4", 3, NowUtc);
        var calls = 0;
        string? gotName = null;
        byte[]? gotBytes = null;

        var result = await HubVideoBulkPush.PushAllAsync(
            [item],
            (name, bytes, _) =>
            {
                calls++;
                gotName = name;
                gotBytes = bytes;
                return Task.FromResult<string?>(null);
            },
            (_, _, _) => { },
            CancellationToken.None);

        Assert.Equal((1, 0), result);
        Assert.Equal(1, calls);
        Assert.Equal("videos/A.mp4", gotName);
        Assert.Equal("abc"u8.ToArray(), gotBytes);
    }

    [Fact]
    public async Task PushAllAsync_TokenDaHuy_NemVaKhongGoiPush()
    {
        var item = Local("A.mp4", "aaa");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var pushed = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HubVideoBulkPush.PushAllAsync(
            [item],
            (_, _, _) =>
            {
                pushed++;
                return Task.FromResult<string?>(null);
            },
            (_, _, _) => { },
            cts.Token));

        Assert.Equal(0, pushed);
    }
}
