using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using ShopeeBackend.Models;

namespace ShopeeBackend.Services
{
    public enum AutomationResult
    {
        Success,
        Captcha,
        WrongPassword,
        Error
    }

    public class BraveAutomation
    {
        private const string BravePath = @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe";
        private const string ExtensionPath = @"D:\Projects\phan-tich-shopee\ShopeeExt";
        private const string SuiteSearchExtensionPath = SuiteSearchSession.ExtensionPath;
        public const string ManualProfileName = "ManualTest";

        private readonly System.Collections.Concurrent.ConcurrentDictionary<SuiteSearchSession, byte> _activeSessions = new();
        private Process? _currentProcess;

        public void Stop()
        {
            foreach (var session in _activeSessions.Keys)
            {
                try { session.Stop(); } catch { }
            }
            _activeSessions.Clear();

            try
            {
                if (_currentProcess != null && !_currentProcess.HasExited)
                {
                    _currentProcess.Kill(entireProcessTree: true);
                }
            }
            catch { }
            _currentProcess = null;

            KillAppBraveProcesses();
        }

        /// <summary>
        /// Đóng sạch sẽ toàn bộ tiến trình Brave được mở bởi ShopeeBackend.
        /// Chỉ quét và đóng các process brave có tham số chứa "ShopeeBackend",
        /// tuyệt đối không ảnh hưởng tới các profile cá nhân hay app khác (như XuLyDonShopee).
        /// </summary>
        public static void KillAppBraveProcesses()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Get-CimInstance Win32_Process | Where-Object { $_.Name -like '*brave*' -and $_.CommandLine -like '*ShopeeBackend*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(3000);
            }
            catch { }
        }

        /// <summary>
        /// Profile cố định để test. Không tạo profile theo tài khoản, không mở form login,
        /// không điền username. Chưa có phiên thì mở Shopee để đăng nhập tay; lần sau nếu
        /// cookie còn thì quét link danh mục bằng extension search của Shopee Suite.
        /// </summary>
        public async Task<AutomationResult> RunManualSessionAsync(List<CategoryInfo> categories, Action<string> logCallback, string proxyAddress = "")
        {
            string profileDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ShopeeBackend", "Profiles", ManualProfileName);
            Directory.CreateDirectory(profileDir);

            bool loggedIn = ProfileHasShopeeLogin(profileDir, logCallback);
            logCallback($"[Test] Thư mục profile: {profileDir}");

            var urls = categories
                .Select(c => c.Url)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .ToList();

            if (!loggedIn || urls.Count == 0)
            {
                logCallback(!loggedIn
                    ? $"[Test] Profile {ManualProfileName} chưa đăng nhập. Mở Shopee để bạn đăng nhập tay, rồi đóng Brave."
                    : "[Test] Đã đăng nhập nhưng không có link danh mục. Mở trang chủ.");
                return await LaunchPlainBraveAsync(profileDir, "https://shopee.vn/", logCallback, proxyAddress);
            }

            logCallback($"[Test] Profile {ManualProfileName} đã có phiên Shopee. Quét bằng extension search trong repo này.");
            var session = new SuiteSearchSession(urls, logCallback, proxyAddress);
            _activeSessions.TryAdd(session, 0);
            try
            {
                return await session.RunAsync(profileDir, BravePath);
            }
            finally
            {
                _activeSessions.TryRemove(session, out _);
            }
        }

        private async Task<AutomationResult> LaunchPlainBraveAsync(string profileDir, string startUrl, Action<string> logCallback, string proxyAddress = "")
        {
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
                FileName = BravePath,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("--user-data-dir=" + profileDir);
            psi.ArgumentList.Add("--no-first-run");
            psi.ArgumentList.Add("--no-default-browser-check");
            psi.ArgumentList.Add("--disable-features=DisableLoadExtensionCommandLineSwitch");
            psi.ArgumentList.Add("--lang=vi-VN");
            if (!string.IsNullOrWhiteSpace(proxyAddress))
                psi.ArgumentList.Add("--proxy-server=http://" + proxyAddress);
            if (Directory.Exists(SuiteSearchExtensionPath))
                psi.ArgumentList.Add("--load-extension=" + SuiteSearchExtensionPath);
            psi.ArgumentList.Add(startUrl);

            logCallback("[Test] Đang mở Brave. Đóng cửa sổ Brave khi bạn muốn kết thúc phiên.");
            using var process = Process.Start(psi);
            if (process == null)
            {
                logCallback("[Test] Không khởi động được Brave.");
                return AutomationResult.Error;
            }
            _currentProcess = process;
            try
            {
                await process.WaitForExitAsync();
            }
            finally
            {
                _currentProcess = null;
            }
            logCallback("[Test] Trình duyệt đã đóng.");
            return AutomationResult.Success;
        }

        public async Task<AutomationResult> RunTaskAsync(
            AccountInfo account,
            string proxyAddress,
            List<CategoryInfo> categories,
            Action<string> logCallback,
            Action<SuiteSearchSession>? onSessionCreated = null)
        {
            try
            {
                logCallback($"[{account.Username}] Chuẩn bị phiên làm việc cho tài khoản...");

                string profileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShopeeBackend", "Profiles", account.ProfileName);
                if (!Directory.Exists(profileDir)) Directory.CreateDirectory(profileDir);

                var urls = categories
                    .Select(c => c.Url)
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .ToList();

                if (urls.Count == 0)
                {
                    logCallback($"[{account.Username}] Tất cả danh mục đã hoàn thành hoặc không có danh mục hợp lệ.");
                    return AutomationResult.Success;
                }

                logCallback($"[{account.Username}] Bắt đầu phiên làm việc (Đăng nhập -> Nghỉ ngơi trang chủ -> Cào dữ liệu)...");
                var session = new SuiteSearchSession(urls, logCallback, proxyAddress, account);
                _activeSessions.TryAdd(session, 0);
                onSessionCreated?.Invoke(session);
                try
                {
                    var result = await session.RunAsync(profileDir, BravePath);
                    return result;
                }
                finally
                {
                    _activeSessions.TryRemove(session, out _);
                }
            }
            catch (Exception ex)
            {
                logCallback($"[{account.Username}] Lỗi ngoại lệ trong quá trình chạy: {ex.Message}");
                return AutomationResult.Error;
            }
        }

        public async Task<AutomationResult> RunKeywordTaskAsync(
            AccountInfo account,
            string proxyAddress,
            List<KeywordInfo> keywords,
            Action<string> logCallback,
            Action<SuiteSearchSession>? onSessionCreated = null)
        {
            try
            {
                logCallback($"[{account.Username}] Chuẩn bị phiên làm việc cho tài khoản...");

                string profileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShopeeBackend", "Profiles", account.ProfileName);
                if (!Directory.Exists(profileDir)) Directory.CreateDirectory(profileDir);

                var kwList = keywords
                    .Select(k => k.Keyword)
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .ToList();

                if (kwList.Count == 0)
                {
                    logCallback($"[{account.Username}] Tất cả từ khóa đã hoàn thành hoặc không có từ khóa hợp lệ.");
                    return AutomationResult.Success;
                }

                logCallback($"[{account.Username}] Bắt đầu phiên làm việc tìm kiếm từ khóa...");
                var session = new SuiteSearchSession("keyword", kwList, logCallback, proxyAddress, account);
                _activeSessions.TryAdd(session, 0);
                onSessionCreated?.Invoke(session);
                try
                {
                    var result = await session.RunAsync(profileDir, BravePath);
                    return result;
                }
                finally
                {
                    _activeSessions.TryRemove(session, out _);
                }
            }
            catch (Exception ex)
            {
                logCallback($"[{account.Username}] Lỗi ngoại lệ trong quá trình chạy: {ex.Message}");
                return AutomationResult.Error;
            }
        }

        /// <summary>
        /// Có phiên Shopee khi cookie SPC_EC hoặc SPC_U còn trong profile.
        /// SPC_F có cả khi chưa đăng nhập nên không dùng.
        /// </summary>
        public static bool ProfileHasShopeeLogin(string profileDir, Action<string> logCallback)
        {
            string[] candidates =
            {
                Path.Combine(profileDir, "Default", "Network", "Cookies"),
                Path.Combine(profileDir, "Default", "Cookies")
            };

            bool sawDb = false;
            foreach (string dbPath in candidates)
            {
                if (!File.Exists(dbPath)) continue;
                sawDb = true;
                string temp = Path.Combine(Path.GetTempPath(), "shopee-cookies-" + Guid.NewGuid().ToString("N") + ".db");
                try
                {
                    File.Copy(dbPath, temp, true);
                    CopyIfExists(dbPath + "-wal", temp + "-wal");
                    CopyIfExists(dbPath + "-shm", temp + "-shm");

                    var cs = new SqliteConnectionStringBuilder
                    {
                        DataSource = temp,
                        Mode = SqliteOpenMode.ReadOnly
                    }.ToString();
                    using var conn = new SqliteConnection(cs);
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    long chromeNowUs = (DateTime.UtcNow - new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10;
                    cmd.CommandText = "SELECT 1 FROM cookies WHERE host_key LIKE '%shopee.vn' AND name IN ('SPC_EC','SPC_U') AND (has_expires = 0 OR expires_utc > $now) LIMIT 1";
                    cmd.Parameters.AddWithValue("$now", chromeNowUs);
                    var found = cmd.ExecuteScalar();
                    if (found != null && found != DBNull.Value)
                        return true;
                }
                catch (Exception ex)
                {
                    logCallback($"[Test] Không đọc được cookie ({Path.GetFileName(dbPath)}): {ex.Message}. Coi như chưa đăng nhập.");
                }
                finally
                {
                    TryDelete(temp);
                    TryDelete(temp + "-wal");
                    TryDelete(temp + "-shm");
                }
            }

            if (!sawDb)
                logCallback("[Test] Profile chưa có file cookie.");
            return false;
        }

        private static void CopyIfExists(string source, string dest)
        {
            if (File.Exists(source))
                File.Copy(source, dest, true);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private int GetAvailablePort()
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
