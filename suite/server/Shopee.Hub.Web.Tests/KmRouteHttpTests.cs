using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shopee.Core.Coordination;
using Shopee.Hub;
using Shopee.Hub.Web.Services;

namespace Shopee.Hub.Web.Tests;

/// <summary>
/// <b>Hai route hồ sơ KM (<c>GET</c>/<c>POST /orders/km</c>) đi qua HOST THẬT</b> — dựng bằng
/// <see cref="WebApplicationFactory{TEntryPoint}"/>, gọi bằng <see cref="HttpClient"/> thật.
/// <para><b>Vì sao cần, dù <c>HubDatabase</c> đã có 13 test:</b> trước bộ này KHÔNG một test nào đi qua
/// <c>ClientApiEndpoints</c>. Quên <c>Map</c> một route, đặt sai đường dẫn so với <c>HubRoutes</c>, đọc sai
/// body, hay DTO serialize lệch tên trường — tất cả đều KHÔNG làm đỏ một test nào, vì mọi test khác gọi thẳng
/// vào lớp database. Triệu chứng ngoài đời chỉ là "đồng bộ không chạy", im lặng.</para>
/// <para><b>Auth KHÔNG bị nới:</b> test đặt token API qua <c>HubDatabase.SetSetting</c> (đúng việc admin làm
/// trên trang cài đặt) rồi gửi header <c>X-Api-Token</c> như client thật. Policy <c>"Client"</c> giữ nguyên —
/// và ca 401 dưới đây khoá chính điều đó.</para>
/// </summary>
public sealed class KmRouteHttpTests : IClassFixture<KmRouteHttpTests.HubApp>
{
    private const string Token = "test-token-8f2c";
    private const string LoginA = "sub-a@shopee.vn";
    private const string May1 = "may-1:orders";

    /// <summary>Host hub dựng một lần cho cả lớp: thư mục dữ liệu TẠM (không đụng <c>hub-data</c> thật) và
    /// token API seed sẵn.</summary>
    public sealed class HubApp : WebApplicationFactory<Program>
    {
        public string DataDir { get; } =
            Path.Combine(Path.GetTempPath(), "hub-km-route-" + Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            Directory.CreateDirectory(DataDir);
            // Cấu hình, KHÔNG phải biến môi trường: env là trạng thái toàn tiến trình, hai bộ test chạy song
            // song sẽ giẫm lên nhau và cùng trỏ vào một thư mục dữ liệu.
            builder.UseSetting("Hub:DataDir", DataDir);
        }

        /// <summary>HttpClient mang sẵn token + id máy, y hệt client thật gọi hub.</summary>
        public HttpClient KhachCoToken(string machineId = May1)
        {
            Services.GetRequiredService<HubDatabase>().SetSetting(SettingKeys.ApiToken, Token);
            var c = CreateClient();
            c.DefaultRequestHeaders.Add("X-Api-Token", Token);
            c.DefaultRequestHeaders.Add("X-Machine-Id", machineId);
            return c;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) { try { Directory.Delete(DataDir, true); } catch { } }
        }
    }

    private readonly HubApp _app;

    public KmRouteHttpTests(HubApp app) => _app = app;

    /// <summary>Đường đi TRỌN VẸN của client: POST một lô hồ sơ → GET đọc lại đúng bản đã gộp. Khoá cả đường
    /// dẫn route (<see cref="HubRoutes.KmProfiles"/>) lẫn hợp đồng JSON hai chiều.</summary>
    [Fact]
    public async Task PostRoiGet_DiQuaRouteThat_TraDungHoSo()
    {
        var c = _app.KhachCoToken();
        var day = new KmProfilesPushRequest(May1, [
            new KmProfileItem(LoginA)
            {
                TuDong = true,
                SoNgay = 3,
                PhanTram = 25,
                GioBatDau = "08:00",
                GioKetThuc = "09:00",
                OwnerMachine = May1,
                CfgUpdatedAt = new DateTimeOffset(2026, 8, 21, 7, 0, 0, TimeSpan.Zero),
            },
        ]);

        var post = await c.PostAsJsonAsync(HubRoutes.KmProfiles, day);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var ve = await c.GetFromJsonAsync<List<KmProfileItem>>(HubRoutes.KmProfiles);

        Assert.NotNull(ve);
        var p = Assert.Single(ve!.Where(x => string.Equals(x.Login, LoginA, StringComparison.OrdinalIgnoreCase)));
        Assert.True(p.TuDong, "TuDong không sống sót qua vòng JSON — hợp đồng DTO lệch");
        Assert.Equal(3, p.SoNgay);
        Assert.Equal(25, p.PhanTram);
        Assert.Equal("08:00", p.GioBatDau);
        Assert.Equal(May1, p.OwnerMachine);
        Assert.Equal(new DateTimeOffset(2026, 8, 21, 7, 0, 0, TimeSpan.Zero), p.CfgUpdatedAt);
    }

    /// <summary>
    /// <b>Phản hồi của <c>POST</c> MANG THEO bản đã gộp, và mốc sửa RÁC đã bị kẹp trong chính bản đó</b> (X1).
    /// <para>Đây là cái khép vòng: máy sai đồng hồ không tự nhận ra mốc 2028 của mình là rác (với nó, đó là
    /// hiện tại), nên nếu lượt đẩy không trả gì thì nó ghi lại 2028 xuống <c>km_config</c> và đẩy lại đúng mốc
    /// ấy ở mỗi lần mở app — lùi cấu hình máy khác, vô hạn. Test này đo ĐÚNG cái body mà client đọc.</para>
    /// </summary>
    [Fact]
    public async Task Post_TraVeBanDaGop_VaKepMocSuaRac()
    {
        var c = _app.KhachCoToken();
        var login = "sub-tra-ve@shopee.vn";
        var mocRac = DateTimeOffset.UtcNow.AddYears(2);
        var day = new KmProfilesPushRequest(May1, [
            new KmProfileItem(login)
            {
                TuDong = true,
                SoNgay = 3,
                PhanTram = 25,
                GioBatDau = "08:00",
                GioKetThuc = "09:00",
                OwnerMachine = May1,
                CfgUpdatedAt = mocRac,
            },
        ]);

        var post = await c.PostAsJsonAsync(HubRoutes.KmProfiles, day);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        var body = await post.Content.ReadFromJsonAsync<KmProfilesPushResponse>();

        Assert.NotNull(body);
        Assert.Equal(1, body!.Profiles);
        var gop = Assert.Single(body.HoSoDaGop);
        Assert.Equal(login, gop.Login);
        Assert.True(gop.CfgUpdatedAt < mocRac, "hub phải KẸP mốc sửa rác trước khi trả về");
        Assert.Equal(25, gop.PhanTram);        // …mà KHÔNG đụng tới cấu hình
        Assert.True(gop.TuDong);
        Assert.Equal(May1, gop.OwnerMachine);
    }

    /// <summary>Hai route nằm SAU policy "Client" — không có token thì 401. Đây là vế giữ cho bộ test này
    /// không âm thầm biến thành cái cớ nới lỏng xác thực.</summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    public async Task KhongCoToken_Thi401(string method)
    {
        var c = _app.CreateClient();

        var res = method == "GET"
            ? await c.GetAsync(HubRoutes.KmProfiles)
            : await c.PostAsJsonAsync(HubRoutes.KmProfiles, new KmProfilesPushRequest(May1, []));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
