using Shopee.Core.Infrastructure;

namespace Shopee.Core.Tests;

/// <summary>
/// <see cref="SuitePaths.RootFile"/>: khi gốc bị <c>data-dir.txt</c> ghi đè (test, bản chạy cách ly) thì KHÔNG đọc dự phòng
/// %AppData%\ShopeeSuite — nếu đọc, test sẽ nối Hub thật của máy (CoordinationClientOnlyTests hỏng khi chạy cả class).
/// Luật chọn đường dẫn test qua <see cref="SuitePaths.ResolveRootFile"/> với thư mục tạm; không tạo / sửa gì trong %AppData%.
/// </summary>
public sealed class SuitePathsRootFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "suitepaths-rootfile-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;
    private readonly string _suite;

    public SuitePathsRootFileTests()
    {
        _root = Path.Combine(_dir, "root");
        _suite = Path.Combine(_dir, "ShopeeSuite");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_suite);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void BiGhiDe_FileSuiteCo_VanTraFileDuoiRoot()
    {
        File.WriteAllText(Path.Combine(_suite, "hub-client.json"), "{}");

        var path = SuitePaths.ResolveRootFile(_root, "hub-client.json", dataDirOverridden: true, _suite);

        Assert.Equal(Path.Combine(_root, "hub-client.json"), path);
    }

    [Fact]
    public void KhongGhiDe_RootChuaCo_FileSuiteCo_DungDuPhongSuite()
    {
        File.WriteAllText(Path.Combine(_suite, "hub-client.json"), "{}");

        var path = SuitePaths.ResolveRootFile(_root, "hub-client.json", dataDirOverridden: false, _suite);

        Assert.Equal(Path.Combine(_suite, "hub-client.json"), path);
    }

    [Fact]
    public void KhongGhiDe_RootCoFile_UuTienRoot()
    {
        File.WriteAllText(Path.Combine(_root, "hub-client.json"), "{}");
        File.WriteAllText(Path.Combine(_suite, "hub-client.json"), "{}");

        var path = SuitePaths.ResolveRootFile(_root, "hub-client.json", dataDirOverridden: false, _suite);

        Assert.Equal(Path.Combine(_root, "hub-client.json"), path);
    }

    [Fact]
    public void KhongGhiDe_CaHaiDeuChuaCo_TraRoot()
    {
        var path = SuitePaths.ResolveRootFile(_root, "hub-client.json", dataDirOverridden: false, _suite);

        Assert.Equal(Path.Combine(_root, "hub-client.json"), path);
    }

    /// <summary>Môi trường test luôn có data-dir.txt → RootFile thật phải nằm dưới Root, bất kể %AppData% có gì.</summary>
    [Fact]
    public void MoiTruongTest_DungDataDir_RootFileNamDuoiRoot()
    {
        Assert.True(SuitePaths.IsDataDirOverridden);
        Assert.Equal(Path.Combine(SuitePaths.Root, "hub-client.json"), SuitePaths.RootFile("hub-client.json"));
    }
}
