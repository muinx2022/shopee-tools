using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShopeeBackend.Models;

namespace ShopeeBackend.Services
{
    /// <summary>
    /// Quét danh mục bằng bản copy extension search trong repo này
    /// (extensions/shopee-search), URL https://shopee.vn/#_ss_ws={cổng},
    /// lệnh {action:start, mode:categoryFromLink}. App chỉ nhận pageData rồi lưu.
    /// </summary>
    public sealed class SuiteSearchSession
    {
        public const string ExtensionPath = @"D:\Projects\phan-tich-shopee\extensions\shopee-search";
        public const string ShopeeExtPath = @"D:\Projects\phan-tich-shopee\ShopeeExt";

        private static readonly JsonSerializerOptions SendJson = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly string _mode = "category";
        private readonly List<string> _links;
        private readonly Action<string> _log;
        private readonly AccountInfo? _account;
        private readonly int _wsPort;
        private readonly int _cdpPort;
        private readonly int _httpPort;

        private HttpListener? _listener;
        private HttpListener? _httpListener;
        private WebSocket? _socket;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private ClientWebSocket? _cdp;
        private readonly SemaphoreSlim _cdpLock = new(1, 1);
        private int _cdpId;
        private Process? _process;

        private readonly string _proxyAddress;
        private int _linkIndex;
        private int _resumeCategory = 1;
        private int _resumePage = 1;
        private AutomationResult _result = AutomationResult.Error;
        private TaskCompletionSource<bool>? _loginTcs;
        private bool _isLoggedIn;
        private bool _hasStartedCrawling;

        public void Stop()
        {
            _log($"[{_account?.Username ?? "Search"}] 🛑 Đang dừng phiên quét và đóng trình duyệt...");
            try
            {
                if (_socket != null && _socket.State == WebSocketState.Open)
                {
                    var stopJson = Encoding.UTF8.GetBytes("{\"action\":\"stop\"}");
                    _ = _socket.SendAsync(new ArraySegment<byte>(stopJson), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            catch { }

            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch { }

            try
            {
                if (_linkIndex >= 0 && _linkIndex < _links.Count)
                {
                    string target = _links[_linkIndex];
                    if (_mode == "keyword")
                    {
                        var currentKw = GlobalState.Keywords.FirstOrDefault(k => k.Keyword == target);
                        if (currentKw != null && currentKw.Status == "Đang quét")
                        {
                            DataManager.UpdateKeywordProgress(target, currentKw.CurrentPage, currentKw.TotalPages, currentKw.ProductCount, "Chờ quét");
                        }
                    }
                    else
                    {
                        var currentCat = GlobalState.Categories.FirstOrDefault(c => c.Url == target);
                        if (currentCat != null && currentCat.Status == "Đang quét")
                        {
                            DataManager.UpdateCategoryProgress(target, currentCat.CurrentSubIndex, currentCat.CurrentPage, currentCat.TotalSubCategories, "Chờ quét");
                        }
                    }
                }
            }
            catch { }

            StopListener();
            try { _cdp?.Abort(); } catch { }
            try { _cdp?.Dispose(); } catch { }
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch { }
        }

        public SuiteSearchSession(IReadOnlyList<string> links, Action<string> log, string proxyAddress = "", AccountInfo? account = null)
            : this("category", links, log, proxyAddress, account)
        {
        }

        public SuiteSearchSession(string mode, IReadOnlyList<string> targets, Action<string> log, string proxyAddress = "", AccountInfo? account = null)
        {
            _mode = mode;
            _links = targets.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => _mode == "keyword" ? u.Trim() : u.Split('#')[0]).ToList();
            _log = log;
            _proxyAddress = proxyAddress?.Trim() ?? "";
            _account = account;
            _wsPort = FreePort();
            _cdpPort = FreePort();
            _httpPort = FreePort();

            _linkIndex = 0;
            if (_mode == "keyword")
            {
                for (int i = 0; i < _links.Count; i++)
                {
                    var kw = GlobalState.Keywords.FirstOrDefault(k => k.Keyword == _links[i]);
                    if (kw == null || kw.Status != "Hoàn thành")
                    {
                        _linkIndex = i;
                        _resumeCategory = 1;
                        _resumePage = Math.Max(1, kw?.CurrentPage ?? 1);
                        break;
                    }
                }
            }
            else
            {
                for (int i = 0; i < _links.Count; i++)
                {
                    var cat = GlobalState.Categories.FirstOrDefault(c => c.Url == _links[i]);
                    if (cat == null || cat.Status != "Hoàn thành")
                    {
                        _linkIndex = i;
                        _resumeCategory = Math.Max(1, cat?.CurrentSubIndex ?? 1);
                        _resumePage = Math.Max(1, cat?.CurrentPage ?? 1);
                        break;
                    }
                }
            }
        }

        public async Task<AutomationResult> RunAsync(string profileDir, string bravePath)
        {
            if (!Directory.Exists(ExtensionPath) || !File.Exists(Path.Combine(ExtensionPath, "manifest.json")))
            {
                _log("[Search] Không thấy extension: " + ExtensionPath);
                return AutomationResult.Error;
            }

            try
            {
                _httpListener = new HttpListener();
                _httpListener.Prefixes.Add($"http://localhost:{_httpPort}/");
                _httpListener.Start();
                _ = Task.Run(HttpListenLoopAsync);

                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://localhost:{_wsPort}/");
                _listener.Start();
                _ = Task.Run(AcceptLoopAsync);

                // Xoá Service Worker cache để Chromium luôn nạp phiên bản JS mới nhất của extension mà không ảnh hưởng cookies/session
                try
                {
                    string swCache = Path.Combine(profileDir, "Default", "Service Worker");
                    if (Directory.Exists(swCache))
                    {
                        Directory.Delete(swCache, recursive: true);
                    }
                }
                catch { }

                var psi = new ProcessStartInfo
                {
                    FileName = bravePath,
                    UseShellExecute = false
                };
                psi.ArgumentList.Add("--user-data-dir=" + profileDir);
                psi.ArgumentList.Add("--no-first-run");
                psi.ArgumentList.Add("--no-default-browser-check");
                psi.ArgumentList.Add("--no-restore-last-session");
                psi.ArgumentList.Add("--restore-last-session=false");
                psi.ArgumentList.Add("--disable-background-mode");
                psi.ArgumentList.Add("--disable-backgrounding-occluded-windows");
                psi.ArgumentList.Add("--disable-renderer-backgrounding");
                psi.ArgumentList.Add("--disable-background-timer-throttling");
                psi.ArgumentList.Add("--disable-features=DisableLoadExtensionCommandLineSwitch");
                psi.ArgumentList.Add("--lang=vi-VN");
                if (!string.IsNullOrWhiteSpace(_proxyAddress))
                    psi.ArgumentList.Add("--proxy-server=http://" + _proxyAddress);
                psi.ArgumentList.Add("--remote-debugging-port=" + _cdpPort);

                var extList = new List<string>();
                if (Directory.Exists(ExtensionPath)) extList.Add(ExtensionPath);
                if (Directory.Exists(ShopeeExtPath)) extList.Add(ShopeeExtPath);
                if (extList.Count > 0)
                    psi.ArgumentList.Add("--load-extension=" + string.Join(",", extList));

                _isLoggedIn = BraveAutomation.ProfileHasShopeeLogin(profileDir, _log);
                string startUrl;
                if (_isLoggedIn)
                {
                    _log($"[{_account?.Username ?? "Search"}] Profile đã có phiên đăng nhập Shopee hợp lệ.");
                    startUrl = $"https://shopee.vn/?_ss_ws={_wsPort}#_ss_ws={_wsPort}";
                }
                else
                {
                    if (_account != null && !string.IsNullOrWhiteSpace(_account.Username))
                    {
                        _log($"[{_account.Username}] Chưa có phiên đăng nhập. Mở Brave và tiến hành đăng nhập...");
                        _loginTcs = new TaskCompletionSource<bool>();
                        var payloadObj = new
                        {
                            u = _account.Username,
                            p = _account.Password,
                            c = _account.Cookie,
                            port = _httpPort,
                            wsPort = _wsPort
                        };
                        var payloadJson = JsonSerializer.Serialize(payloadObj);
                        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));
                        startUrl = $"https://shopee.vn/buyer/login?next=/?_ss_ws={_wsPort}#shopee_ext_login={payload}";
                    }
                    else
                    {
                        _log("[Search] Chưa đăng nhập. Mở trang chủ Shopee...");
                        startUrl = $"https://shopee.vn/?_ss_ws={_wsPort}#_ss_ws={_wsPort}";
                    }
                }
                psi.ArgumentList.Add(startUrl);

                if (!string.IsNullOrWhiteSpace(_proxyAddress))
                    _log($"[{_account?.Username ?? "Search"}] 🌐 Sử dụng proxy: {_proxyAddress}");
                _log($"[{_account?.Username ?? "Search"}] Khởi chạy Brave (cổng lệnh WS {_wsPort}, HTTP {_httpPort}). Có {_links.Count} link danh mục.");

                _process = Process.Start(psi);
                if (_process == null)
                {
                    _log($"[{_account?.Username ?? "Search"}] Không khởi động được Brave.");
                    return AutomationResult.Error;
                }

                // Watchdog: Tự động đồng bộ tab nếu Brave khôi phục session cũ hoặc tab chưa đúng cổng lệnh
                _ = Task.Run(async () =>
                {
                    for (int retry = 0; retry < 20; retry++)
                    {
                        await Task.Delay(1500);
                        if (_hasStartedCrawling || _process == null || _process.HasExited)
                            return;

                        // Nếu WebSocket đã mở nhưng sau 5s chưa cào (và đã đăng nhập), tự động phát lệnh cào
                        if (_socket is { State: WebSocketState.Open })
                        {
                            if (retry >= 3 && !_hasStartedCrawling && _isLoggedIn)
                            {
                                _log($"[{_account?.Username ?? "Search"}] ⚡ Extension đã sẵn sàng, tự động phát lệnh cào dữ liệu...");
                                _hasStartedCrawling = true;
                                _loginTcs?.TrySetResult(true);
                                await SendStartAsync();
                                return;
                            }
                            continue;
                        }

                        // Nếu WebSocket chưa nối: dùng CDP kiểm tra các tab đang mở để ép điều hướng về đúng cổng
                        try
                        {
                            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                            var listJson = await http.GetStringAsync($"http://127.0.0.1:{_cdpPort}/json/list");
                            using var doc = JsonDocument.Parse(listJson);
                            foreach (var target in doc.RootElement.EnumerateArray())
                            {
                                var type = Text(target, "type");
                                var url = Text(target, "url");
                                if (type == "page" && url.Contains("shopee.vn") && !url.Contains("shopee.vn/api/"))
                                {
                                    if (!url.Contains($"_ss_ws={_wsPort}"))
                                    {
                                        string wsDbg = Text(target, "webSocketDebuggerUrl");
                                        if (!string.IsNullOrWhiteSpace(wsDbg))
                                        {
                                            _log($"[{_account?.Username ?? "Search"}] 🔄 Đồng bộ tab trình duyệt tới cổng lệnh {_wsPort}...");
                                            using var tabCdp = new ClientWebSocket();
                                            await tabCdp.ConnectAsync(new Uri(wsDbg), CancellationToken.None);
                                            string navPayload = JsonSerializer.Serialize(new
                                            {
                                                id = 1001,
                                                method = "Page.navigate",
                                                @params = new { url = $"https://shopee.vn/?_ss_ws={_wsPort}#_ss_ws={_wsPort}" }
                                            });
                                            await tabCdp.SendAsync(Encoding.UTF8.GetBytes(navPayload), WebSocketMessageType.Text, true, CancellationToken.None);
                                            await Task.Delay(1000);
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                });

                if (!_isLoggedIn && _loginTcs != null)
                {
                    _ = Task.Run(async () =>
                    {
                        var timeoutTask = Task.Delay(150000);
                        var finished = await Task.WhenAny(_loginTcs.Task, timeoutTask);
                        if (finished == timeoutTask && !_loginTcs.Task.IsCompleted && !_hasStartedCrawling)
                        {
                            if (BraveAutomation.ProfileHasShopeeLogin(profileDir, _log))
                            {
                                _log($"[{_account?.Username ?? "Search"}] Đã phát hiện cookie đăng nhập thành công!");
                                _loginTcs.TrySetResult(true);
                            }
                            else
                            {
                                _log($"[{_account?.Username ?? "Search"}] ⚠️ Quá thời gian chờ (150 giây) chưa hoàn tất đăng nhập. Chuyển tài khoản tiếp theo.");
                                _result = AutomationResult.Captcha;
                                Stop();
                            }
                        }
                    });
                }

                await _process.WaitForExitAsync();
                _log($"[{_account?.Username ?? "Search"}] Trình duyệt đã đóng.");
                return _result;
            }
            catch (Exception ex)
            {
                _log($"[{_account?.Username ?? "Search"}] Lỗi: " + ex.Message);
                return AutomationResult.Error;
            }
            finally
            {
                StopListener();
                try { _process?.Dispose(); } catch { }
                _process = null;
                try { _cdp?.Abort(); } catch { }
                try { _cdp?.Dispose(); } catch { }
            }
        }

        private async Task HttpListenLoopAsync()
        {
            while (_httpListener != null && _httpListener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _httpListener.GetContextAsync(); }
                catch { break; }

                try
                {
                    var req = ctx.Request;
                    var res = ctx.Response;

                    res.Headers.Add("Access-Control-Allow-Origin", "*");
                    res.Headers.Add("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
                    res.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                    if (req.HttpMethod == "OPTIONS")
                    {
                        res.StatusCode = 200;
                        res.Close();
                        continue;
                    }

                    using var reader = new StreamReader(req.InputStream, Encoding.UTF8);
                    var body = await reader.ReadToEndAsync();

                    if (req.Url?.AbsolutePath == "/status")
                    {
                        if (body.Contains("wrong_password"))
                        {
                            _log($"[{_account?.Username ?? "Search"}] ❌ Báo lỗi: Sai tên đăng nhập hoặc mật khẩu.");
                            _result = AutomationResult.WrongPassword;
                            _loginTcs?.TrySetResult(false);
                            Stop();
                        }
                        else if (body.Contains("captcha_waiting"))
                        {
                            _log($"[{_account?.Username ?? "Search"}] ⚠️ Phát hiện Captcha/trang xác minh đăng nhập! Đang chờ bạn giải Captcha thủ công trên trình duyệt (tối đa 2 phút)...");
                        }
                        else if (body.Contains("captcha"))
                        {
                            _log($"[{_account?.Username ?? "Search"}] ❌ Captcha chưa được giải sau 2 phút. Đóng trình duyệt để chuyển sang tài khoản tiếp theo...");
                            _result = AutomationResult.Captcha;
                            _loginTcs?.TrySetResult(false);
                            Stop();
                        }
                        else if (body.Contains("login_success") || body.Contains("success"))
                        {
                            _log($"[{_account?.Username ?? "Search"}] ✅ Đăng nhập thành công! Đang chuyển hướng về trang chủ...");
                            _isLoggedIn = true;
                            _loginTcs?.TrySetResult(true);
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(4000);
                                if (!_hasStartedCrawling && _socket is { State: WebSocketState.Open })
                                {
                                    int delaySec = Random.Shared.Next(5, 11);
                                    _log($"[{_account?.Username ?? "Search"}] ⏳ Đã về trang chủ. Nghỉ ngơi tự nhiên {delaySec} giây trước khi mở link danh mục...");
                                    _hasStartedCrawling = true;
                                    await Task.Delay(delaySec * 1000);
                                    await SendStartAsync();
                                }
                            });
                        }
                    }

                    res.StatusCode = 200;
                    res.Close();
                }
                catch
                {
                    if (_httpListener == null || !_httpListener.IsListening) break;
                }
            }
        }

        private async Task AcceptLoopAsync()
        {
            while (_listener != null && _listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { break; }

                if (!ctx.Request.IsWebSocketRequest)
                {
                    ctx.Response.StatusCode = 400;
                    ctx.Response.Close();
                    continue;
                }

                try
                {
                    var wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null);
                    var previous = Interlocked.Exchange(ref _socket, wsCtx.WebSocket);
                    if (previous != null && previous.State == WebSocketState.Open)
                    {
                        try { await previous.CloseAsync(WebSocketCloseStatus.NormalClosure, "replaced", CancellationToken.None); }
                        catch { }
                    }
                    _log("[Search] Extension đã nối.");
                    _ = Task.Run(() => ReceiveLoopAsync(wsCtx.WebSocket));
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(4000);
                        if (!_hasStartedCrawling && _isLoggedIn && _socket is { State: WebSocketState.Open })
                        {
                            _log($"[{_account?.Username ?? "Search"}] ⚡ Extension đã sẵn sàng, tự động kích hoạt cào danh mục...");
                            _hasStartedCrawling = true;
                            _loginTcs?.TrySetResult(true);
                            await SendStartAsync();
                        }
                    });
                }
                catch (Exception ex)
                {
                    _log("[Search] Không nhận được websocket: " + ex.Message);
                }
            }
        }

        private async Task ReceiveLoopAsync(WebSocket ws)
        {
            var buffer = new byte[1024 * 256];
            var msg = new List<byte>(64 * 1024);
            while (ws.State == WebSocketState.Open && ReferenceEquals(_socket, ws))
            {
                WebSocketReceiveResult result;
                try { result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None); }
                catch { break; }

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                msg.AddRange(new ArraySegment<byte>(buffer, 0, result.Count));
                if (!result.EndOfMessage)
                    continue;

                var json = Encoding.UTF8.GetString(msg.ToArray());
                msg.Clear();
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    await HandleMessageAsync(doc.RootElement);
                }
                catch (Exception ex)
                {
                    _log("[Search] Gói lỗi: " + ex.Message);
                }
            }
        }

        private async Task HandleMessageAsync(JsonElement root)
        {
            if (root.TryGetProperty("kind", out var kindEl) && kindEl.GetString() == "cdpInput")
            {
                await HandleCdpAsync(root);
                return;
            }

            if (!root.TryGetProperty("action", out var actionEl))
                return;
            var action = actionEl.GetString();

            switch (action)
            {
                case "ready":
                    if (!_isLoggedIn)
                    {
                        _log($"[{_account?.Username ?? "Search"}] Extension đã nối. Đang chờ hoàn tất đăng nhập...");
                        break;
                    }
                    if (_hasStartedCrawling)
                    {
                        _log($"[{_account?.Username ?? "Search"}] Extension kết nối lại.");
                        break;
                    }
                    _hasStartedCrawling = true;
                    _loginTcs?.TrySetResult(true);
                    int delaySec = Random.Shared.Next(5, 11);
                    _log($"[{_account?.Username ?? "Search"}] ⏳ Đã về trang chủ. Nghỉ ngơi tự nhiên {delaySec} giây trước khi mở link danh mục...");
                    await Task.Delay(delaySec * 1000);
                    await SendStartAsync();
                    break;
                case "progress":
                    if (root.TryGetProperty("message", out var msg))
                        _log($"[{_account?.Username ?? "Search"}] " + (msg.GetString() ?? ""));
                    break;
                case "discoveredSubCategories":
                    HandleDiscoveredSubCategories(root);
                    break;
                case "pageData":
                    HandlePageData(root);
                    break;
                case "subCategoryDone":
                    HandleSubCategoryDone(root);
                    break;
                case "done":
                    await HandleCategoryDoneAsync();
                    break;
                case "captchaWaiting":
                    string reason = Text(root, "reason");
                    string reasonMsg = string.IsNullOrWhiteSpace(reason) ? "" : $" ({reason})";
                    _log($"[{_account?.Username ?? "Search"}] ⚠️ Phát hiện Captcha/trang xác minh{reasonMsg}! Đang tạm dừng chờ bạn giải Captcha thủ công (tối đa 2 phút)...");
                    break;
                case "captchaResolved":
                    _log($"[{_account?.Username ?? "Search"}] ✅ Captcha đã được giải thành công! Tiếp tục cào dữ liệu...");
                    break;
                case "captcha":
                    _result = AutomationResult.Captcha;
                    _log($"[{_account?.Username ?? "Search"}] ❌ Hết thời gian chờ 2 phút mà chưa giải Captcha! Đóng trình duyệt để chuyển sang tài khoản tiếp theo...");
                    Stop();
                    break;
                case "networkError":
                    _result = AutomationResult.Error;
                    _log($"[{_account?.Username ?? "Search"}] ❌ Lỗi mạng/proxy: {Text(root, "message")}. Đóng trình duyệt để phục hồi...");
                    Stop();
                    break;
                case "error":
                    _result = AutomationResult.Error;
                    _log($"[{_account?.Username ?? "Search"}] ❌ Lỗi: {Text(root, "message")}. Đóng trình duyệt...");
                    Stop();
                    break;
            }
        }

        private async Task SendStartAsync()
        {
            if (_linkIndex < 0 || _linkIndex >= _links.Count)
                return;
            var target = _links[_linkIndex];

            if (_mode == "keyword")
            {
                var kw = GlobalState.Keywords.FirstOrDefault(k => k.Keyword == target);
                _resumePage = Math.Max(1, kw?.CurrentPage ?? 1);

                _log($"[Search] 🔍 Bắt đầu cào từ khóa ({_linkIndex + 1}/{_links.Count}): '{target}' (Trang #{_resumePage})");
                DataManager.UpdateKeywordProgress(target, _resumePage, kw?.TotalPages ?? 50, kw?.ProductCount ?? 0, "Đang quét");

                await SendAsync(new
                {
                    action = "start",
                    mode = "keyword",
                    keyword = target,
                    resumePage = _resumePage,
                    maxPages = 50
                });
            }
            else
            {
                var cat = GlobalState.Categories.FirstOrDefault(c => c.Url == target);
                if (cat != null)
                {
                    _resumeCategory = Math.Max(1, cat.CurrentSubIndex);
                    _resumePage = Math.Max(1, cat.CurrentPage);
                }

                _log($"[Search] Gửi categoryFromLink ({_linkIndex + 1}/{_links.Count}): {target}");
                _log($"[Search] ⏯ Điểm bắt đầu: Danh mục con #{_resumeCategory}, Trang #{_resumePage}");

                if (_linkIndex >= 0 && _linkIndex < _links.Count)
                {
                    DataManager.UpdateCategoryProgress(target, _resumeCategory, _resumePage, cat?.TotalSubCategories ?? 0, "Đang quét");
                }

                await SendAsync(new
                {
                    action = "start",
                    mode = "categoryFromLink",
                    keyword = target,
                    link = target,
                    region = "",
                    resumeCategoryIndex = _resumeCategory,
                    resumePage = _resumePage
                });
            }
        }

        private void HandlePageData(JsonElement root)
        {
            var source = root.TryGetProperty("data", out var dataEl) ? dataEl : root;
            int catIdx = 1;
            int page = 1;
            int totalCats = 0;
            bool hasNext = false;

            if (source.TryGetProperty("categoryIndex", out var ci) && ci.TryGetInt32(out var idx))
                catIdx = Math.Max(1, idx);
            if (source.TryGetProperty("categoryTotal", out var ct) && ct.TryGetInt32(out var total))
                totalCats = total;
            if (source.TryGetProperty("page", out var pageEl) && pageEl.TryGetInt32(out var p) && p > 0)
                page = p;
            if (source.TryGetProperty("hasNext", out var hn) && hn.ValueKind == JsonValueKind.True)
                hasNext = true;

            if (_mode == "keyword")
            {
                string kw = _linkIndex >= 0 && _linkIndex < _links.Count ? _links[_linkIndex] : "";
                _resumePage = hasNext ? page + 1 : 1;
                var kwInfo = GlobalState.Keywords.FirstOrDefault(k => k.Keyword == kw);
                int prodsSoFar = kwInfo?.ProductCount ?? 0;
                DataManager.UpdateKeywordProgress(kw, _resumePage, 50, prodsSoFar, "Đang quét");
            }
            else
            {
                _resumeCategory = catIdx;
                _resumePage = hasNext ? page + 1 : 1;

                if (_linkIndex >= 0 && _linkIndex < _links.Count)
                {
                    string currentUrl = _links[_linkIndex];
                    DataManager.UpdateCategoryProgress(currentUrl, _resumeCategory, _resumePage, totalCats, "Đang quét");
                }
            }

            if (!source.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return;

            string categoryPath = Text(source, "category");
            string categoryUrl;
            if (_mode == "keyword")
            {
                string kw = _linkIndex >= 0 && _linkIndex < _links.Count ? _links[_linkIndex] : "";
                if (string.IsNullOrWhiteSpace(categoryPath))
                    categoryPath = "Từ khóa: " + kw;
                categoryUrl = "https://shopee.vn/search?keyword=" + Uri.EscapeDataString(kw);
            }
            else
            {
                categoryUrl = _linkIndex >= 0 && _linkIndex < _links.Count ? _links[_linkIndex] : "";
            }
            var products = new List<ScrapedProduct>();
            foreach (var it in items.EnumerateArray())
            {
                string itemId = Num(it, "itemid");
                string shopId = Num(it, "shopid");
                if (string.IsNullOrEmpty(itemId) || itemId == "0")
                    continue;
                var image = Text(it, "image");
                if (!string.IsNullOrWhiteSpace(image) &&
                    !image.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    image = "https://cf.shopee.vn/file/" + image;

                long histSold = it.TryGetProperty("historical_sold", out var hs) && hs.TryGetInt64(out var hsv) ? hsv : 0;
                long monthSold = it.TryGetProperty("monthly_sold", out var ms) && ms.TryGetInt64(out var msv) ? msv : 0;
                long sold = Lng(it, "sold");
                (histSold, monthSold) = SoldRules.NormalizePayload(histSold, monthSold, sold);

                products.Add(new ScrapedProduct
                {
                    Id = $"{shopId}_{itemId}",
                    ItemId = itemId,
                    ShopId = shopId,
                    Name = Text(it, "name"),
                    Price = Dec(it, "price"),
                    Rating = Dbl(it, "rating"),
                    HistoricalSold = histSold,
                    MonthlySold = monthSold,
                    Location = Text(it, "location"),
                    ImageUrl = image,
                    CategoryPath = ShopeeCategoryTranslator.Translate(string.IsNullOrWhiteSpace(categoryPath) ? Text(it, "category") : categoryPath),
                    CategoryUrl = categoryUrl,
                    ProductUrl = Text(it, "link"),
                    ScrapedAt = DateTime.Now
                });
            }

            if (products.Count == 0)
                return;

            DataManager.SaveScrapedProducts(products);
            if (_mode == "keyword" && _linkIndex >= 0 && _linkIndex < _links.Count)
            {
                string kw = _links[_linkIndex];
                var kwInfo = GlobalState.Keywords.FirstOrDefault(k => k.Keyword == kw);
                int updatedCount = (kwInfo?.ProductCount ?? 0) + products.Count;
                DataManager.UpdateKeywordProgress(kw, _resumePage, 50, updatedCount, "Đang quét");
            }

            if (GlobalState.AutoDownloadImages)
            {
                ImageDownloadService.Enqueue(products);
            }

            if (Application.OpenForms.Count > 0)
            {
                var main = Application.OpenForms[0];
                main?.BeginInvoke(new Action(() =>
                {
                    GlobalState.ScrapedProducts.RaiseListChangedEvents = false;
                    try
                    {
                        foreach (var p in products)
                        {
                            var existing = GlobalState.ScrapedProducts.FirstOrDefault(x =>
                                x.Id == p.Id || (!string.IsNullOrEmpty(x.ItemId) && x.ItemId == p.ItemId && x.ShopId == p.ShopId));
                            if (existing != null)
                            {
                                p.EvaluateOpportunity();
                                int at = GlobalState.ScrapedProducts.IndexOf(existing);
                                GlobalState.ScrapedProducts[at] = p;
                            }
                            else
                            {
                                p.EvaluateOpportunity();
                                GlobalState.ScrapedProducts.Insert(0, p);
                            }
                        }
                    }
                    finally
                    {
                        GlobalState.ScrapedProducts.RaiseListChangedEvents = true;
                        GlobalState.ScrapedProducts.ResetBindings();
                    }
                }));
            }

            string title = string.IsNullOrWhiteSpace(products[0].CategoryPath) ? "Danh mục" : products[0].CategoryPath;
            _log($"[Search] Nhận {products.Count} sản phẩm từ [{title}]. Trang {page}/(Checkpoint kế tiếp: p{_resumePage}).");
            if (GlobalState.AutoDownloadImages)
            {
                _log($"[Ảnh] 📥 Đang tải {products.Count} ảnh về {GlobalState.ImageStorageFolder}...");
            }
        }

        private void HandleDiscoveredSubCategories(JsonElement root)
        {
            string parentUrl = Text(root, "link");
            if (string.IsNullOrWhiteSpace(parentUrl) && _linkIndex >= 0 && _linkIndex < _links.Count)
                parentUrl = _links[_linkIndex];

            var subList = new List<(int Index, string Name, string Url)>();
            if (root.TryGetProperty("subs", out var subsEl) && subsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in subsEl.EnumerateArray())
                {
                    int idx = it.TryGetProperty("index", out var ie) && ie.TryGetInt32(out var iv) ? iv : subList.Count + 1;
                    string rawName = it.TryGetProperty("name", out var ne) ? ne.GetString() ?? "" : "";
                    string name = ShopeeCategoryTranslator.Translate(rawName);
                    string url = it.TryGetProperty("url", out var ue) ? ue.GetString() ?? "" : "";
                    subList.Add((idx, name, url));
                }
            }

            if (subList.Count > 0)
            {
                DataManager.SaveDiscoveredSubCategories(parentUrl, subList);
                var cat = GlobalState.Categories.FirstOrDefault(c => c.Url == parentUrl);
                if (cat != null)
                {
                    cat.TotalSubCategories = subList.Count;
                    DataManager.UpdateCategoryProgress(parentUrl, cat.CurrentSubIndex, cat.CurrentPage, subList.Count, "Đang quét");
                }
                _log($"[Search] 📁 Đã phát hiện và lưu {subList.Count} danh mục con vào hệ thống.");
            }
        }

        private void HandleSubCategoryDone(JsonElement root)
        {
            int nextCat = _resumeCategory + 1;
            if (root.TryGetProperty("nextCategoryIndex", out var nci) && nci.TryGetInt32(out var nval))
                nextCat = nval;
            int total = 0;
            if (root.TryGetProperty("categoryTotal", out var ct) && ct.TryGetInt32(out var tval))
                total = tval;
            string subName = root.TryGetProperty("subName", out var sne) ? sne.GetString() ?? "" : "";

            int completedCatIdx = _resumeCategory;
            _resumeCategory = nextCat;
            _resumePage = 1;

            if (_linkIndex >= 0 && _linkIndex < _links.Count)
            {
                string currentUrl = _links[_linkIndex];
                DataManager.MarkSubCategoryCompleted(currentUrl, completedCatIdx, subName);
                DataManager.UpdateCategoryProgress(currentUrl, _resumeCategory, 1, total, "Đang quét");
            }

            _log($"[Search] ✅ Đã cào xong danh mục con: {subName}. Kế tiếp: mục #{_resumeCategory}");
        }

        private async Task HandleCategoryDoneAsync()
        {
            if (_mode == "keyword")
            {
                if (_linkIndex >= 0 && _linkIndex < _links.Count)
                {
                    string currentKw = _links[_linkIndex];
                    var kwInfo = GlobalState.Keywords.FirstOrDefault(k => k.Keyword == currentKw);
                    DataManager.UpdateKeywordProgress(currentKw, _resumePage, 50, kwInfo?.ProductCount ?? 0, "Hoàn thành");
                    _log($"[Search] 🎉 Hoàn thành từ khóa ({_linkIndex + 1}/{_links.Count}): '{currentKw}'");
                }

                _linkIndex++;
                while (_linkIndex < _links.Count)
                {
                    var kw = GlobalState.Keywords.FirstOrDefault(k => k.Keyword == _links[_linkIndex]);
                    if (kw != null && kw.Status == "Hoàn thành")
                    {
                        _log($"[Search] Bỏ qua từ khóa đã hoàn thành: '{kw.Keyword}'");
                        _linkIndex++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (_linkIndex < _links.Count)
                {
                    var nextKw = GlobalState.Keywords.FirstOrDefault(k => k.Keyword == _links[_linkIndex]);
                    _resumePage = Math.Max(1, nextKw?.CurrentPage ?? 1);
                    _log($"[{_account?.Username ?? "Search"}] Chuyển sang từ khóa tiếp theo ({_linkIndex + 1}/{_links.Count}): '{_links[_linkIndex]}'");
                    await SendStartAsync();
                }
                else
                {
                    _log($"[{_account?.Username ?? "Search"}] 🏁 Đã quét hết tất cả từ khóa.");
                    _result = AutomationResult.Success;
                    Stop();
                }
                return;
            }

            if (_linkIndex >= 0 && _linkIndex < _links.Count)
            {
                string currentUrl = _links[_linkIndex];
                DataManager.UpdateCategoryProgress(currentUrl, _resumeCategory, 1, 0, "Hoàn thành");
                _log($"[Search] 🎉 Hoàn thành danh mục ({_linkIndex + 1}/{_links.Count}): {currentUrl}");
            }

            _linkIndex++;
            while (_linkIndex < _links.Count)
            {
                var cat = GlobalState.Categories.FirstOrDefault(c => c.Url == _links[_linkIndex]);
                if (cat != null && cat.Status == "Hoàn thành")
                {
                    _log($"[Search] Bỏ qua danh mục đã hoàn thành: {cat.Name} ({cat.Url})");
                    _linkIndex++;
                }
                else
                {
                    break;
                }
            }

            if (_linkIndex < _links.Count)
            {
                var nextCat = GlobalState.Categories.FirstOrDefault(c => c.Url == _links[_linkIndex]);
                _resumeCategory = Math.Max(1, nextCat?.CurrentSubIndex ?? 1);
                _resumePage = Math.Max(1, nextCat?.CurrentPage ?? 1);
                _log($"[{_account?.Username ?? "Search"}] Chuyển sang danh mục tiếp theo ({_linkIndex + 1}/{_links.Count}): {_links[_linkIndex]}");
                await SendStartAsync();
            }
            else
            {
                _log($"[{_account?.Username ?? "Search"}] 🏁 Đã quét hết tất cả link danh mục.");
                _result = AutomationResult.Success;
                Stop();
            }
        }

        private async Task HandleCdpAsync(JsonElement root)
        {
            int id = root.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var n) ? n : 0;
            string op = Text(root, "op");
            double x = Dbl(root, "x");
            double y = Dbl(root, "y");
            double dx = Dbl(root, "deltaX");
            double dy = Dbl(root, "deltaY");
            int clicks = root.TryGetProperty("clickCount", out var cc) && cc.TryGetInt32(out var c) ? c : 1;
            bool ok = false;
            try
            {
                ok = await DispatchCdpAsync(op, x, y, dx, dy, clicks);
            }
            catch (Exception ex)
            {
                _log("[Search] CDP " + op + " lỗi: " + ex.Message);
            }
            await SendAsync(new { kind = "cdpInputAck", id, ok });
        }

        private async Task<bool> DispatchCdpAsync(string op, double x, double y, double dx, double dy, int clicks)
        {
            if (!await EnsureCdpAsync())
                return false;

            if (op == "moveTo")
            {
                await CdpAsync("Input.dispatchMouseEvent", new { type = "mouseMoved", x, y });
                return true;
            }
            if (op == "click")
            {
                await CdpAsync("Input.dispatchMouseEvent", new { type = "mouseMoved", x, y });
                await CdpAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", clickCount = clicks });
                await CdpAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", clickCount = clicks });
                return true;
            }
            if (op == "wheel")
            {
                await CdpAsync("Input.dispatchMouseEvent", new { type = "mouseWheel", x, y, deltaX = dx, deltaY = dy });
                return true;
            }
            return false;
        }

        private async Task<bool> EnsureCdpAsync()
        {
            if (_cdp is { State: WebSocketState.Open })
                return true;
            await _cdpLock.WaitAsync();
            try
            {
                if (_cdp is { State: WebSocketState.Open })
                    return true;
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var body = await http.GetStringAsync($"http://127.0.0.1:{_cdpPort}/json/list");
                using var doc = JsonDocument.Parse(body);
                string? wsUrl = null;
                foreach (var t in doc.RootElement.EnumerateArray())
                {
                    var type = Text(t, "type");
                    var url = Text(t, "url");
                    if (type == "page" && url.Contains("shopee.vn", StringComparison.OrdinalIgnoreCase)
                        && !url.Contains("shopee.vn/api/", StringComparison.OrdinalIgnoreCase))
                    {
                        wsUrl = Text(t, "webSocketDebuggerUrl");
                        break;
                    }
                }
                if (string.IsNullOrWhiteSpace(wsUrl))
                    return false;
                var cdp = new ClientWebSocket();
                await cdp.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
                _cdp = cdp;
                _ = Task.Run(() => DrainCdpAsync(cdp));
                return true;
            }
            catch (Exception ex)
            {
                _log("[Search] Chưa nối được CDP: " + ex.Message);
                return false;
            }
            finally
            {
                _cdpLock.Release();
            }
        }

        private static async Task DrainCdpAsync(ClientWebSocket cdp)
        {
            var buf = new byte[64 * 1024];
            try
            {
                while (cdp.State == WebSocketState.Open)
                    await cdp.ReceiveAsync(buf, CancellationToken.None);
            }
            catch { }
        }

        private async Task CdpAsync(string method, object args)
        {
            var cdp = _cdp;
            if (cdp == null || cdp.State != WebSocketState.Open)
                throw new InvalidOperationException("CDP chưa mở");
            int id = Interlocked.Increment(ref _cdpId);
            var payload = JsonSerializer.Serialize(new { id, method, @params = args });
            var bytes = Encoding.UTF8.GetBytes(payload);
            await _cdpLock.WaitAsync();
            try
            {
                await cdp.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            finally
            {
                _cdpLock.Release();
            }
        }

        private async Task SendAsync(object payload)
        {
            var ws = _socket;
            if (ws == null || ws.State != WebSocketState.Open)
                return;
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, SendJson));
            await _sendLock.WaitAsync();
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private void StopListener()
        {
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
            try { _httpListener?.Stop(); } catch { }
            try { _httpListener?.Close(); } catch { }
            _httpListener = null;
            var ws = _socket;
            if (ws != null && ws.State == WebSocketState.Open)
            {
                try { ws.Abort(); } catch { }
            }
        }

        private static int FreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string Text(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null)
                return "";
            return p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : p.ToString();
        }

        private static string Num(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null)
                return "";
            if (p.ValueKind == JsonValueKind.Number)
            {
                if (p.TryGetInt64(out var n)) return n.ToString();
                if (p.TryGetDouble(out var d)) return ((long)d).ToString();
            }
            return p.ToString();
        }

        private static decimal Dec(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var p)) return 0;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;
            if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), out var s)) return s;
            return 0;
        }

        private static double Dbl(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var p)) return 0;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d)) return d;
            if (p.ValueKind == JsonValueKind.String && double.TryParse(p.GetString(), out var s)) return s;
            return 0;
        }

        private static long Lng(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out var p)) return 0;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var n)) return n;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var d)) return (long)d;
            return 0;
        }
    }
}
