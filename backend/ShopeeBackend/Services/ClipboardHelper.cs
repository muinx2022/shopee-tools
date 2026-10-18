using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ShopeeBackend.Services
{
    public static class ClipboardHelper
    {
        public static void SetImageRobust(Image img)
        {
            if (img == null) return;
            SetImageAndFileRobust(string.Empty, img);
        }

        public static void SetImageAndFileRobust(string filePath, Image? img)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.Clear();

                    var data = new DataObject();

                    if (!string.IsNullOrWhiteSpace(filePath))
                    {
                        data.SetData(DataFormats.UnicodeText, true, filePath);
                        data.SetData(DataFormats.Text, true, filePath);

                        if (File.Exists(filePath))
                        {
                            data.SetData(DataFormats.FileDrop, true, new string[] { filePath });
                        }
                    }

                    if (img != null)
                    {
                        data.SetData(DataFormats.Bitmap, true, img);

                        try
                        {
                            var pngStream = new MemoryStream();
                            img.Save(pngStream, ImageFormat.Png);
                            pngStream.Position = 0;
                            data.SetData("PNG", false, pngStream);
                        }
                        catch { }
                    }

                    Clipboard.SetDataObject(data, true, 5, 100);
                    return;
                }
                catch
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
