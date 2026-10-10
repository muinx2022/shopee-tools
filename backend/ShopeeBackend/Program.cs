using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShopeeBackend.Models;
using ShopeeBackend.Services;

namespace ShopeeBackend
{
    internal static class Program
    {
        [STAThread]
        static async Task Main(string[] args)
        {


            if (args.Any(a => a.Equals("--test-cat", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    await RunCliTestAsync(args);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_crash.log"), ex.ToString());
                }
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.ExceptionObject?.ToString() ?? "UnhandledException"); } catch { }
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"), e.Exception?.ToString() ?? "ThreadException"); } catch { }
            };

            string startLog = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup.log");
            try { File.AppendAllText(startLog, $"[{DateTime.Now:HH:mm:ss.fff}] Program Main started.\n"); } catch { }

            try
            {
                ApplicationConfiguration.Initialize();
                try { File.AppendAllText(startLog, $"[{DateTime.Now:HH:mm:ss.fff}] AppConfig initialized. Calling Application.Run...\n"); } catch { }
                Application.Run(new MainForm());
                try { File.AppendAllText(startLog, $"[{DateTime.Now:HH:mm:ss.fff}] Application.Run exited normally.\n"); } catch { }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(startLog, $"[{DateTime.Now:HH:mm:ss.fff}] Exception in Main: {ex}\n"); } catch { }
                string crashPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                File.WriteAllText(crashPath, ex.ToString());
                MessageBox.Show(ex.ToString(), "Startup Error");
            }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        private static async Task RunCliTestAsync(string[] args)
        {
            AttachConsole(-1);
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            string logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "test_run.log");
            File.WriteAllText(logFile, $"=== BẮT ĐẦU CHẠY THỬ NGHIỆM {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\r\n");

            void Log(string msg)
            {
                string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
                Console.WriteLine(line);
                try { File.AppendAllText(logFile, line + "\r\n"); } catch { }
            }

            try
            {
                Log("=== [BẮT ĐẦU CHẠY THỬ NGHIỆM TỰ ĐỘNG BẰNG CLI] ===");
            DataManager.LoadData();

            string catUrl = args.Length > 1 ? args[1] : "https://shopee.vn/Women-Bags-cat.11035761";
            var category = GlobalState.Categories.FirstOrDefault(c => c.Url == catUrl) 
                ?? GlobalState.Categories.FirstOrDefault(c => c.Name.Contains("Women-Bags", StringComparison.OrdinalIgnoreCase))
                ?? GlobalState.Categories.FirstOrDefault(c => c.Status != "Hoàn thành");

            if (category == null)
            {
                Log("❌ Không tìm thấy danh mục để chạy!");
                return;
            }

            Log($"📌 Danh mục kiểm tra: {category.Name} ({category.Url})");
            Log($"📌 Tiến độ hiện tại: Sub {category.CurrentSubIndex}/{category.TotalSubCategories}, Page {category.CurrentPage}");

            // Chọn account: ưu tiên tài khoản vừa chạy (t2dopk3lr6) hoặc tài khoản Ready
            var account = GlobalState.Accounts.FirstOrDefault(a => a.Username == "t2dopk3lr6")
                ?? GlobalState.Accounts.FirstOrDefault(a => a.Status == "Ready" || a.Status == "Done");

            if (account == null)
            {
                Log("❌ Không có tài khoản nào khả dụng!");
                return;
            }
            Log($"👤 Tài khoản: {account.Username} (Profile: {account.ProfileName})");

            // Chọn proxy
            string proxyAddress = string.Empty;
            var proxyConfig = GlobalState.Proxies.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.ApiKey));
            if (proxyConfig != null)
            {
                Log($"🌐 Đang kiểm tra proxy: {proxyConfig.Name}...");
                var proxyInfo = await KiotProxyClient.FetchOrRefreshProxyAsync(
                    proxyConfig.ApiKey,
                    safeSecondsThreshold: 180,
                    logCallback: msg => Log($"[Proxy] {msg}"));

                if (proxyInfo.Success && !string.IsNullOrWhiteSpace(proxyInfo.Http))
                {
                    var (isAlive, publicIp) = await KiotProxyClient.CheckProxyAliveAsync(proxyInfo.Http, 8);
                    if (isAlive)
                    {
                        proxyAddress = proxyInfo.Http;
                        Log($"🌐 Proxy OK! IP: {publicIp} ({proxyInfo.Http})");
                    }
                    else
                    {
                        Log($"⚠️ Proxy không phản hồi ping, tạm chạy IP máy...");
                    }
                }
            }

            var automation = new BraveAutomation();
            Log("🚀 Đang khởi động Brave và tiến trình cào dữ liệu...");
            var result = await automation.RunTaskAsync(
                account,
                proxyAddress,
                new System.Collections.Generic.List<CategoryInfo> { category },
                msg => Log(msg));

            Log($"=== [KẾT QUẢ PHIÊN TEST]: {result} ===");
            }
            catch (Exception ex)
            {
                Log($"❌ LỖI NGOẠI LỆ: {ex}");
            }
        }
    }
}
