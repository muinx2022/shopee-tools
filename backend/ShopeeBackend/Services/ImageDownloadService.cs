using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ShopeeBackend.Models;

namespace ShopeeBackend.Services
{
    public static class ImageDownloadService
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly ConcurrentQueue<ScrapedProduct> _queue = new();
        private static int _runningWorkers = 0;
        private static readonly int MaxConcurrency = 4;
        private static Action<string>? _logAction;

        static ImageDownloadService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        public static void SetLogger(Action<string> logger)
        {
            _logAction = logger;
        }

        public static string NormalizeImageUrlToJpeg(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            if (url.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                return url.Substring(0, url.Length - 5) + ".jpg";
            if (url.Contains(".webp?", StringComparison.OrdinalIgnoreCase))
                return url.Replace(".webp?", ".jpg?");
            return url;
        }

        public static string GetProductImageDirectory(ScrapedProduct p)
        {
            string baseFolder = GlobalState.ImageStorageFolder;
            if (string.IsNullOrWhiteSpace(baseFolder)) baseFolder = @"d:\shopee-images";

            DateTime date = p.ScrapedAt != default ? p.ScrapedAt : DateTime.Now;
            string dateFolder = date.ToString("dd-MM-yyyy");

            string id = !string.IsNullOrWhiteSpace(p.ItemId) && p.ItemId != "0" ? p.ItemId : p.Id;
            if (string.IsNullOrWhiteSpace(id)) id = Guid.NewGuid().ToString("N");
            foreach (char c in Path.GetInvalidFileNameChars())
                id = id.Replace(c, '_');

            return Path.Combine(baseFolder, dateFolder, id);
        }

        public static string GetLocalImageFileName(ScrapedProduct p)
        {
            string id = !string.IsNullOrWhiteSpace(p.ItemId) && p.ItemId != "0" ? p.ItemId : p.Id;
            if (string.IsNullOrWhiteSpace(id)) id = Guid.NewGuid().ToString("N");
            foreach (char c in Path.GetInvalidFileNameChars())
                id = id.Replace(c, '_');
            return $"{id}.jpg";
        }

        public static string GetLocalImagePath(ScrapedProduct p)
        {
            string dir = GetProductImageDirectory(p);
            string fileName = GetLocalImageFileName(p);
            string standardPath = Path.Combine(dir, fileName);

            if (File.Exists(standardPath) && new FileInfo(standardPath).Length > 0)
                return standardPath;

            if (Directory.Exists(dir))
            {
                try
                {
                    var existing = Directory.EnumerateFiles(dir, "*.*")
                        .FirstOrDefault(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                             f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                                             f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                                             f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));
                    if (existing != null && new FileInfo(existing).Length > 0)
                        return existing;
                }
                catch { }
            }

            // Fallback legacy paths if old image exists on disk
            string baseFolder = GlobalState.ImageStorageFolder;
            if (string.IsNullOrWhiteSpace(baseFolder)) baseFolder = @"d:\shopee-images";
            string legacyInBase = Path.Combine(baseFolder, fileName);
            if (File.Exists(legacyInBase) && new FileInfo(legacyInBase).Length > 0)
                return legacyInBase;

            string legacyOldFolder = Path.Combine(@"e:\images\phan-tich-shopee", fileName);
            if (File.Exists(legacyOldFolder) && new FileInfo(legacyOldFolder).Length > 0)
                return legacyOldFolder;

            return standardPath;
        }

        public static bool HasLocalImage(ScrapedProduct p)
        {
            try
            {
                string path = GetLocalImagePath(p);
                return File.Exists(path) && new FileInfo(path).Length > 0;
            }
            catch { return false; }
        }

        public static void Enqueue(IEnumerable<ScrapedProduct> products)
        {
            if (products == null) return;
            int count = 0;
            foreach (var p in products)
            {
                if (!string.IsNullOrWhiteSpace(p.ImageUrl) && !HasLocalImage(p))
                {
                    _queue.Enqueue(p);
                    count++;
                }
            }
            if (count > 0)
            {
                EnsureWorkersRunning();
            }
        }

        public static void Enqueue(ScrapedProduct p)
        {
            if (p != null && !string.IsNullOrWhiteSpace(p.ImageUrl) && !HasLocalImage(p))
            {
                _queue.Enqueue(p);
                EnsureWorkersRunning();
            }
        }

        private static void EnsureWorkersRunning()
        {
            while (_runningWorkers < MaxConcurrency && !_queue.IsEmpty)
            {
                Interlocked.Increment(ref _runningWorkers);
                Task.Run(ProcessQueueAsync);
            }
        }

        private static async Task ProcessQueueAsync()
        {
            try
            {
                while (_queue.TryDequeue(out var product))
                {
                    await DownloadProductImageAsync(product);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _runningWorkers);
            }
        }

        public static async Task<bool> DownloadProductImageAsync(ScrapedProduct product)
        {
            if (product == null || string.IsNullOrWhiteSpace(product.ImageUrl)) return false;

            try
            {
                string filePath = GetLocalImagePath(product);
                if (File.Exists(filePath) && new FileInfo(filePath).Length > 0)
                    return true;

                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(dir))
                    DataManager.EnsureFolderExists(dir);

                string downloadUrl = NormalizeImageUrlToJpeg(product.ImageUrl);
                var bytes = await _httpClient.GetByteArrayAsync(downloadUrl);
                if (bytes != null && bytes.Length > 0)
                {
                    string tempPath = filePath + ".tmp_" + Guid.NewGuid().ToString("N");
                    await File.WriteAllBytesAsync(tempPath, bytes);
                    File.Move(tempPath, filePath, true);
                    return true;
                }
            }
            catch
            {
                // Silently ignore individual failure
            }
            return false;
        }

        public static async Task<int> DownloadBulkAsync(IEnumerable<ScrapedProduct> products, IProgress<(int Current, int Total)>? progress = null, CancellationToken ct = default)
        {
            var list = new List<ScrapedProduct>();
            foreach (var p in products)
            {
                if (!string.IsNullOrWhiteSpace(p.ImageUrl) && !HasLocalImage(p))
                    list.Add(p);
            }

            int total = list.Count;
            if (total == 0) return 0;

            int completed = 0;
            using var throttler = new SemaphoreSlim(6);

            var tasks = new List<Task>();
            foreach (var p in list)
            {
                if (ct.IsCancellationRequested) break;
                await throttler.WaitAsync(ct);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await DownloadProductImageAsync(p);
                    }
                    finally
                    {
                        throttler.Release();
                        int c = Interlocked.Increment(ref completed);
                        progress?.Report((c, total));
                    }
                }, ct));
            }

            await Task.WhenAll(tasks);
            return completed;
        }
    }
}
