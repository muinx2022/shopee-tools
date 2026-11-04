using System.Net;
using System.Net.Sockets;
using System.Text;
using Shopee.Core.Scrape;

namespace Shopee.Core.Tests;

/// <summary>
/// Tên file video + đẩy/kéo qua delegate. Không cần hub: push/pull là fake.
/// Thư mục tạm riêng — không đụng video thật trên máy.
/// </summary>
public sealed class SkuVideoSyncTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "shopee-core-tests", "sku-video-" + Guid.NewGuid().ToString("N"));

    public SkuVideoSyncTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void FileName_SkuThuong_KemRemoteName()
    {
        Assert.Equal("ABC.mp4", SkuVideoSync.FileName("ABC"));
        Assert.Equal("videos/ABC.mp4", SkuVideoSync.RemoteName("ABC"));
        Assert.Equal("ABC.mp4", SkuVideoSync.FileName(" ABC "));
        Assert.Equal("ABC.mp4", SkuVideoSync.FileName("ABC."));
        Assert.Equal(TimeSpan.FromSeconds(90), SkuVideoSync.TransferTimeout);
        Assert.Equal(80L * 1024 * 1024, SkuVideoSync.MaxPushBytes);
    }

    [Theory]
    [InlineData("a/b", "a_b.mp4")]
    [InlineData(@"..\..", "_.mp4")]
    [InlineData("C:foo", "C_foo.mp4")]
    public void RemoteName_SkuCoKyTuDuong_MotDoanAnToan(string sku, string file)
    {
        Assert.Equal(file, SkuVideoSync.FileName(sku));
        var remote = SkuVideoSync.RemoteName(sku);
        Assert.Equal("videos/" + file, remote);
        Assert.Matches(@"^videos/[^/\\:]+$", remote);
        Assert.DoesNotContain("..", remote);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void FileName_SkuRong_LaVideoMp4(string? sku)
    {
        Assert.Equal("video.mp4", SkuVideoSync.FileName(sku));
        Assert.Equal("videos/video.mp4", SkuVideoSync.RemoteName(sku));
    }

    [Fact]
    public async Task DownloadBestAsync_GhiDungFileName()
    {
        var body = new byte[] { 9, 8, 7, 6, 5 };
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var stop = new CancellationTokenSource();
        var server = Task.Run(() => ServeLocalMp4Async(listener, body, stop.Token));
        try
        {
            var sku = "a/b";
            var result = await VideoDownloader.DownloadBestAsync(
                sku,
                new[] { new VideoCandidate($"http://127.0.0.1:{port}/clip.mp4", 12, "local") },
                _dir);

            Assert.True(result.Success, result.Error);
            var expected = SkuVideoSync.LocalPath(_dir, sku);
            Assert.Equal(expected, result.SavedPath);
            Assert.Equal(body, await File.ReadAllBytesAsync(expected));
            Assert.False(File.Exists(expected + ".part"));
        }
        finally
        {
            stop.Cancel();
            listener.Stop();
            try { await server.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
    }

    [Fact]
    public async Task TryEnsureLocalAsync_DaCoFile_KhongGoiPull()
    {
        var sku = "CO-SAN";
        var dest = SkuVideoSync.LocalPath(_dir, sku);
        var original = new byte[] { 9, 9 };
        await File.WriteAllBytesAsync(dest, original);

        var called = false;
        var got = await SkuVideoSync.TryEnsureLocalAsync(
            _dir,
            sku,
            (_, _) =>
            {
                called = true;
                return Task.FromResult<byte[]?>(new byte[] { 1 });
            },
            CancellationToken.None);

        Assert.False(called);
        Assert.Equal(dest, got);
        Assert.Equal(original, await File.ReadAllBytesAsync(dest));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryEnsureLocalAsync_FolderTrong_KhongGoiPull(string? folder)
    {
        var called = false;
        var got = await SkuVideoSync.TryEnsureLocalAsync(
            folder,
            "ABC",
            (_, _) =>
            {
                called = true;
                return Task.FromResult<byte[]?>(new byte[] { 1 });
            },
            CancellationToken.None);

        Assert.Null(got);
        Assert.False(called);
    }

    [Fact]
    public async Task TryEnsureLocalAsync_PullTraByte_GhiDungVaXoaPart()
    {
        var sku = "KEO1";
        string? remote = null;
        var got = await SkuVideoSync.TryEnsureLocalAsync(
            _dir,
            sku,
            (name, _) =>
            {
                remote = name;
                return Task.FromResult<byte[]?>(new byte[] { 1, 2, 3 });
            },
            CancellationToken.None);

        var dest = SkuVideoSync.LocalPath(_dir, sku);
        Assert.Equal(SkuVideoSync.RemoteName(sku), remote);
        Assert.Equal(dest, got);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(dest));
        AssertNoPartFiles();
    }

    [Fact]
    public async Task TryEnsureLocalAsync_HaiLuongCungSku_ChiPullMotLan()
    {
        var sku = "SONGSONG";
        var pulls = 0;
        Task<string?> Run() => SkuVideoSync.TryEnsureLocalAsync(
            _dir,
            sku,
            async (_, token) =>
            {
                Interlocked.Increment(ref pulls);
                await Task.Delay(200, token);
                return new byte[] { 1, 2, 3 };
            },
            CancellationToken.None);

        var results = await Task.WhenAll(Run(), Run());
        var dest = SkuVideoSync.LocalPath(_dir, sku);
        Assert.Equal(1, pulls);
        Assert.Equal(dest, results[0]);
        Assert.Equal(dest, results[1]);
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(dest));
        AssertNoPartFiles();
    }

    [Fact]
    public async Task TryEnsureLocalAsync_ByteDaiHonTran_TraNullKhongGhi()
    {
        var sku = "QUA-TRAN";
        var calls = 0;
        var got = await SkuVideoSync.TryEnsureLocalAsync(
            _dir,
            sku,
            (_, _) =>
            {
                calls++;
                return Task.FromResult<byte[]?>(new byte[(int)SkuVideoSync.MaxPushBytes + 1]);
            },
            CancellationToken.None);

        Assert.Null(got);
        Assert.Equal(1, calls);
        Assert.False(File.Exists(SkuVideoSync.LocalPath(_dir, sku)));
        AssertNoPartFiles();
    }

    [Fact]
    public async Task TryEnsureLocalAsync_PullNullRongHoacNem_TraNullKhongRac()
    {
        Assert.Null(await SkuVideoSync.TryEnsureLocalAsync(
            _dir, "NULL1", (_, _) => Task.FromResult<byte[]?>(null), CancellationToken.None));
        Assert.Null(await SkuVideoSync.TryEnsureLocalAsync(
            _dir, "RONG1", (_, _) => Task.FromResult<byte[]?>(Array.Empty<byte>()), CancellationToken.None));
        Assert.Null(await SkuVideoSync.TryEnsureLocalAsync(
            _dir, "IO1", (_, _) => throw new IOException("dut"), CancellationToken.None));

        foreach (var sku in new[] { "NULL1", "RONG1", "IO1" })
            Assert.False(File.Exists(SkuVideoSync.LocalPath(_dir, sku)));
        AssertNoPartFiles();
    }

    [Fact]
    public async Task TryPushAsync_FileQuaLon_KhongGoiPush()
    {
        var big = Path.Combine(_dir, "big.mp4");
        using (var fs = new FileStream(big, FileMode.Create, FileAccess.Write, FileShare.None))
            fs.SetLength(SkuVideoSync.MaxPushBytes + 1);

        var called = false;
        var ok = await SkuVideoSync.TryPushAsync(
            big,
            "BIG",
            (_, _, _) =>
            {
                called = true;
                return Task.FromResult(true);
            },
            CancellationToken.None);

        Assert.False(ok);
        Assert.False(called);
    }

    [Fact]
    public async Task TryPushAsync_KhongCoFileHoacRong_KhongGoiPush()
    {
        var called = false;
        Task<bool> Push(string name, byte[] bytes, CancellationToken token)
        {
            _ = name;
            _ = bytes;
            _ = token;
            called = true;
            return Task.FromResult(true);
        }

        Assert.False(await SkuVideoSync.TryPushAsync(
            Path.Combine(_dir, "khong-co.mp4"), "Z", Push, CancellationToken.None));
        Assert.False(called);

        var empty = Path.Combine(_dir, "rong.mp4");
        await File.WriteAllBytesAsync(empty, Array.Empty<byte>());
        Assert.False(await SkuVideoSync.TryPushAsync(empty, "Z", Push, CancellationToken.None));
        Assert.False(called);

        Assert.False(await SkuVideoSync.TryPushAsync(null, "Z", Push, CancellationToken.None));
        Assert.False(await SkuVideoSync.TryPushAsync("  ", "Z", Push, CancellationToken.None));
        Assert.False(called);
    }

    [Fact]
    public async Task TryPushAsync_DayDungRemoteNameVaNoiDung()
    {
        var path = Path.Combine(_dir, "local-ten-khac.mp4");
        var payload = new byte[] { 4, 5, 6 };
        await File.WriteAllBytesAsync(path, payload);

        string? name = null;
        byte[]? got = null;
        var ok = await SkuVideoSync.TryPushAsync(
            path,
            "ABC",
            (n, b, _) =>
            {
                name = n;
                got = b;
                return Task.FromResult(true);
            },
            CancellationToken.None);

        Assert.True(ok);
        Assert.Equal("videos/ABC.mp4", name);
        Assert.Equal(payload, got);
    }

    [Fact]
    public async Task TryPushAsync_PushFalseHoacHttpRequest_TraFalse()
    {
        var path = await WriteSmallAsync("push-fail.mp4");

        Assert.False(await SkuVideoSync.TryPushAsync(
            path, "A", (_, _, _) => Task.FromResult(false), CancellationToken.None));
        Assert.False(await SkuVideoSync.TryPushAsync(
            path, "A", (_, _, _) => throw new HttpRequestException("dut"), CancellationToken.None));
    }

    [Fact]
    public async Task Huy_PushVaPull_NemTiep()
    {
        var path = await WriteSmallAsync("huy.mp4");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SkuVideoSync.TryPushAsync(
                path,
                "A",
                (_, _, _) => throw new OperationCanceledException(),
                CancellationToken.None));

        var sku = "HUY-PULL";
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SkuVideoSync.TryEnsureLocalAsync(
                _dir,
                sku,
                (_, _) => throw new OperationCanceledException(),
                CancellationToken.None));
        Assert.False(File.Exists(SkuVideoSync.LocalPath(_dir, sku)));
        AssertNoPartFiles();

        using var cts = new CancellationTokenSource();
        var skuGhi = "HUY-GHI";
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SkuVideoSync.TryEnsureLocalAsync(
                _dir,
                skuGhi,
                (_, _) =>
                {
                    cts.Cancel();
                    return Task.FromResult<byte[]?>(new byte[] { 1, 2, 3 });
                },
                cts.Token));
        var dest = SkuVideoSync.LocalPath(_dir, skuGhi);
        Assert.False(File.Exists(dest));
        AssertNoPartFiles();
    }

    private void AssertNoPartFiles() =>
        Assert.Empty(Directory.GetFiles(_dir, "*.part", SearchOption.AllDirectories));

    private async Task<string> WriteSmallAsync(string name)
    {
        var path = Path.Combine(_dir, name);
        await File.WriteAllBytesAsync(path, new byte[] { 1 });
        return path;
    }

    /// <summary>HTTP/1.1 tối thiểu trên loopback — khỏi HttpListener (cần urlacl). HEAD rồi GET, body cố định.</summary>
    private static async Task ServeLocalMp4Async(TcpListener listener, byte[] body, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                break;
            }

            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var header = await ReadHttpHeaderAsync(stream, ct).ConfigureAwait(false);
                    var isHead = header.StartsWith("HEAD ", StringComparison.OrdinalIgnoreCase);
                    var head = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 200 OK\r\nContent-Type: video/mp4\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head, ct).ConfigureAwait(false);
                    if (!isHead)
                        await stream.WriteAsync(body, ct).ConfigureAwait(false);
                    await stream.FlushAsync(ct).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Một kết nối hỏng không được chặn request kế (GET sau HEAD).
            }
        }
    }

    private static async Task<string> ReadHttpHeaderAsync(NetworkStream stream, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var buf = new byte[4096];
        var read = 0;
        while (read < buf.Length && !Encoding.ASCII.GetString(buf, 0, read).Contains("\r\n\r\n"))
        {
            var n = await stream.ReadAsync(buf.AsMemory(read, buf.Length - read), timeout.Token).ConfigureAwait(false);
            if (n == 0) break;
            read += n;
        }
        return Encoding.ASCII.GetString(buf, 0, read);
    }
}
