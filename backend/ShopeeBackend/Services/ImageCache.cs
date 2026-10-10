using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShopeeBackend.Services
{
    public static class ImageCache
    {
        private static readonly ConcurrentDictionary<string, Image> _cache = new();
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
        private static readonly Image _placeholder = CreatePlaceholder();

        static ImageCache()
        {
            _http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        private static Image CreatePlaceholder()
        {
            var bmp = new Bitmap(38, 38);
            using var g = Graphics.FromImage(bmp);
            g.Clear(ColorTranslator.FromHtml("#F4F1EE"));
            using var pen = new Pen(ColorTranslator.FromHtml("#E2DDD8"), 1);
            g.DrawRectangle(pen, 0, 0, 37, 37);
            TextRenderer.DrawText(g, "📷", new Font("Segoe UI", 9f), new Rectangle(0, 0, 38, 38), ColorTranslator.FromHtml("#8B837D"), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return bmp;
        }

        public static Image GetPlaceholder() => _placeholder;

        public static Image? GetCached(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            return _cache.TryGetValue(url, out var img) ? img : null;
        }

        public static void LoadThumbnailAsync(string url, string? itemId, Control ownerControl, Action onLoaded)
        {
            if (string.IsNullOrWhiteSpace(url) || _cache.ContainsKey(url)) return;

            Task.Run(async () =>
            {
                try
                {
                    byte[]? bytes = null;
                    string? localPath = null;

                    // 1. Check local file on disk first (Instant offline load)
                    if (!string.IsNullOrWhiteSpace(itemId) && !string.IsNullOrWhiteSpace(GlobalState.ImageStorageFolder))
                    {
                        localPath = Path.Combine(GlobalState.ImageStorageFolder, $"{itemId}.jpg");
                        if (File.Exists(localPath) && new FileInfo(localPath).Length > 0)
                        {
                            try { bytes = await File.ReadAllBytesAsync(localPath); } catch { }
                        }
                    }

                    // 2. If not on disk, download from Shopee
                    if (bytes == null || bytes.Length == 0)
                    {
                        string downloadUrl = ImageDownloadService.NormalizeImageUrlToJpeg(url);
                        bytes = await _http.GetByteArrayAsync(downloadUrl);

                        // Save downloaded image to disk
                        if (bytes != null && bytes.Length > 0 && !string.IsNullOrWhiteSpace(localPath))
                        {
                            try
                            {
                                DataManager.EnsureFolderExists(GlobalState.ImageStorageFolder);
                                string tmp = localPath + ".tmp_" + Guid.NewGuid().ToString("N");
                                await File.WriteAllBytesAsync(tmp, bytes);
                                File.Move(tmp, localPath, true);
                            }
                            catch { }
                        }
                    }

                    if (bytes == null || bytes.Length == 0)
                    {
                        _cache[url] = _placeholder;
                        return;
                    }

                    using var ms = new MemoryStream(bytes);
                    using var original = Image.FromStream(ms);

                    var thumb = new Bitmap(38, 38);
                    using (var g = Graphics.FromImage(thumb))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(original, 0, 0, 38, 38);
                        using var pen = new Pen(ColorTranslator.FromHtml("#E2DDD8"), 1);
                        g.DrawRectangle(pen, 0, 0, 37, 37);
                    }

                    _cache[url] = thumb;

                    if (ownerControl != null && !ownerControl.IsDisposed && ownerControl.IsHandleCreated)
                    {
                        ownerControl.BeginInvoke(onLoaded);
                    }
                }
                catch
                {
                    // If failed to download image, cache placeholder to prevent repeated failing requests
                    _cache[url] = _placeholder;
                }
            });
        }

        public static void LoadThumbnailAsync(string url, Control ownerControl, Action onLoaded)
            => LoadThumbnailAsync(url, null, ownerControl, onLoaded);
    }
}
