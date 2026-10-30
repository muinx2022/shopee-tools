using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IconGenerator
{
    public static void GenerateAll(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        using (var scrapeBmp = RenderScrapeIcon(1024))
        {
            SavePngAndIco(scrapeBmp, outputDir, "scrape-icon");
        }

        using (var updateBmp = RenderUpdateIcon(1024))
        {
            SavePngAndIco(updateBmp, outputDir, "update-icon");
        }
    }

    private static GraphicsPath CreateRoundedRect(RectangleF rect, float radius)
    {
        float d = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void SetupGraphics(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
    }

    private static void DrawSquircleBase(Graphics g, int size, Color topLeft, Color midColor, Color bottomRight)
    {
        float margin = 16f;
        var box = new RectangleF(margin, margin, size - margin * 2f, size - margin * 2f);
        float radius = 224f;

        using (var path = CreateRoundedRect(box, radius))
        using (var brush = new LinearGradientBrush(
            new PointF(0f, 0f),
            new PointF(size, size),
            topLeft,
            bottomRight))
        {
            var cb = new ColorBlend(3)
            {
                Colors = new[] { topLeft, midColor, bottomRight },
                Positions = new[] { 0f, 0.52f, 1f }
            };
            brush.InterpolationColors = cb;
            g.FillPath(brush, path);
        }
    }

    public static Bitmap RenderScrapeIcon(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            SetupGraphics(g);
            g.Clear(Color.Transparent);

            // Shopee Orange-Coral squircle (#FF6E40 -> #EE4D2D -> #CC3012)
            DrawSquircleBase(
                g,
                size,
                Color.FromArgb(255, 255, 110, 64),
                Color.FromArgb(255, 238, 77, 45),
                Color.FromArgb(255, 204, 48, 18)
            );

            // Magnifying glass parameters
            float cx = 442f;
            float cy = 438f;
            float lensR = 244f;
            float ringW = 70f;

            float angleRad = (float)(45.0 * Math.PI / 180.0);
            float cosA = (float)Math.Cos(angleRad);
            float sinA = (float)Math.Sin(angleRad);

            float hx1 = cx + (lensR + 8f) * cosA;
            float hy1 = cy + (lensR + 8f) * sinA;
            float hx2 = 824f;
            float hy2 = 820f;

            // Subtle drop shadow under magnifying glass + handle
            float shOffY = 16f;
            using (var shBrush = new SolidBrush(Color.FromArgb(65, 95, 18, 5)))
            {
                float outerR = lensR + ringW / 2f;
                g.FillEllipse(shBrush, cx - outerR, cy - outerR + shOffY, outerR * 2f, outerR * 2f);
            }
            using (var shHandlePen = new Pen(Color.FromArgb(65, 95, 18, 5), 100f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(shHandlePen, hx1, hy1 + shOffY, hx2, hy2 + shOffY);
            }

            // Handle neck connector
            using (var neckPen = new Pen(Color.White, 58f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(neckPen, cx + (lensR - 12f) * cosA, cy + (lensR - 12f) * sinA, hx1 + 28f, hy1 + 28f);
            }

            // Handle outer body (White)
            using (var handlePen = new Pen(Color.White, 96f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(handlePen, hx1 + 20f, hy1 + 20f, hx2, hy2);
            }

            // Handle inner accent slot (Deep Navy #162238)
            using (var gripPen = new Pen(Color.FromArgb(255, 22, 34, 56), 40f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(gripPen, hx1 + 44f, hy1 + 44f, hx2 - 16f, hy2 - 16f);
            }

            // Deep Navy-Slate Lens Interior (#1E2E4A -> #0F172A)
            var lensRect = new RectangleF(cx - lensR, cy - lensR, lensR * 2f, lensR * 2f);
            using (var lensBrush = new LinearGradientBrush(
                new PointF(lensRect.Left, lensRect.Top),
                new PointF(lensRect.Right, lensRect.Bottom),
                Color.FromArgb(255, 30, 46, 74),
                Color.FromArgb(255, 15, 23, 42)))
            {
                g.FillEllipse(lensBrush, lensRect);
            }

            // Inside the lens:
            // 1. Vibrant Sky-Cyan Extraction Tray Bracket at bottom of lens
            float trayY = cy + 118f;
            float trayHalfW = 108f;
            using (var trayPen = new Pen(Color.FromArgb(255, 56, 189, 248), 36f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            })
            {
                g.DrawLines(trayPen, new PointF[]
                {
                    new PointF(cx - trayHalfW, trayY - 36f),
                    new PointF(cx - trayHalfW, trayY),
                    new PointF(cx + trayHalfW, trayY),
                    new PointF(cx + trayHalfW, trayY - 36f)
                });
            }

            // 2. Bold White Downward Scrape Arrow
            float arrowTop = cy - 134f;
            float arrowTip = cy + 56f;
            float wingW = 104f;
            float wingY = cy - 32f;

            using (var shaftPen = new Pen(Color.White, 54f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(shaftPen, cx, arrowTop, cx, arrowTip - 16f);
            }
            using (var headPen = new Pen(Color.White, 54f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            })
            {
                g.DrawLines(headPen, new PointF[]
                {
                    new PointF(cx - wingW, wingY),
                    new PointF(cx, arrowTip),
                    new PointF(cx + wingW, wingY)
                });
            }

            // Crisp White Magnifying Glass Ring
            using (var ringPen = new Pen(Color.White, ringW))
            {
                g.DrawEllipse(ringPen, lensRect);
            }
        }
        return bmp;
    }

    public static Bitmap RenderUpdateIcon(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            SetupGraphics(g);
            g.Clear(Color.Transparent);

            // Royal Blue squircle (#3D8BFD -> #2563EB -> #1739A6)
            DrawSquircleBase(
                g,
                size,
                Color.FromArgb(255, 61, 139, 253),
                Color.FromArgb(255, 37, 99, 235),
                Color.FromArgb(255, 23, 57, 166)
            );

            float cx = size / 2f;
            float cy = size / 2f;

            // Outer Circular Sync Arrows (White)
            float syncR = 310f;
            float syncStroke = 62f;
            var arcRect = new RectangleF(cx - syncR, cy - syncR, syncR * 2f, syncR * 2f);

            float start1 = 204f;
            float arcSweep = 128f; // arc stops 6 deg before chevron vertex so flat end tucks cleanly inside chevron
            float tipAngle1 = 204f + 134f; // 338 deg

            float start2 = 24f;
            float tipAngle2 = 24f + 134f;  // 158 deg

            // Subtle drop shadow for sync arcs & arrowheads
            float shOffY = 15f;
            Color shColor = Color.FromArgb(65, 10, 24, 75);
            using (var shadowPen = new Pen(shColor, syncStroke) { StartCap = LineCap.Round, EndCap = LineCap.Flat })
            {
                var sRect = new RectangleF(arcRect.X, arcRect.Y + shOffY, arcRect.Width, arcRect.Height);
                g.DrawArc(shadowPen, sRect, start1, arcSweep);
                g.DrawArc(shadowPen, sRect, start2, arcSweep);
            }
            DrawSyncArrowHead(g, cx, cy + shOffY, syncR, tipAngle1, 98f, syncStroke, shColor);
            DrawSyncArrowHead(g, cx, cy + shOffY, syncR, tipAngle2, 98f, syncStroke, shColor);

            // Main White Sync Arcs (Round tail cap, Flat head cap tucked into chevron)
            using (var syncPen = new Pen(Color.White, syncStroke) { StartCap = LineCap.Round, EndCap = LineCap.Flat })
            {
                g.DrawArc(syncPen, arcRect, start1, arcSweep);
                g.DrawArc(syncPen, arcRect, start2, arcSweep);
            }

            // Seamless Arrowheads at 338 deg and 158 deg
            DrawSyncArrowHead(g, cx, cy, syncR, tipAngle1, 98f, syncStroke, Color.White);
            DrawSyncArrowHead(g, cx, cy, syncR, tipAngle2, 98f, syncStroke, Color.White);

            // Center Core: Shopee Orange Circular Badge (#FF6E40 -> #E03A18)
            float coreR = 186f;
            var coreShadowRect = new RectangleF(cx - coreR, cy - coreR + shOffY, coreR * 2f, coreR * 2f);
            using (var csb = new SolidBrush(shColor))
            {
                g.FillEllipse(csb, coreShadowRect);
            }

            var coreRect = new RectangleF(cx - coreR, cy - coreR, coreR * 2f, coreR * 2f);
            using (var coreBrush = new LinearGradientBrush(
                new PointF(coreRect.Left, coreRect.Top),
                new PointF(coreRect.Right, coreRect.Bottom),
                Color.FromArgb(255, 255, 110, 64),
                Color.FromArgb(255, 224, 58, 24)))
            {
                g.FillEllipse(coreBrush, coreRect);
            }

            // Crisp White Ring around the Orange Core
            using (var coreBorderPen = new Pen(Color.White, 16f))
            {
                g.DrawEllipse(coreBorderPen, coreRect);
            }

            // Bold White Upward Upload/Update Arrow inside the Orange Core
            float arrowTop = cy - 92f;
            float arrowBottom = cy + 92f;
            float wingW = 86f;
            float wingY = cy - 8f;

            using (var shaftPen = new Pen(Color.White, 50f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(shaftPen, cx, arrowTop + 14f, cx, arrowBottom);
            }
            using (var headPen = new Pen(Color.White, 50f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            })
            {
                g.DrawLines(headPen, new PointF[]
                {
                    new PointF(cx - wingW, wingY),
                    new PointF(cx, arrowTop),
                    new PointF(cx + wingW, wingY)
                });
            }
        }
        return bmp;
    }

    private static void DrawSyncArrowHead(Graphics g, float cx, float cy, float radius, float endAngleDeg, float wingLen, float strokeWidth, Color color)
    {
        double endRad = endAngleDeg * Math.PI / 180.0;
        float tipX = cx + radius * (float)Math.Cos(endRad);
        float tipY = cy + radius * (float)Math.Sin(endRad);

        double tangentDeg = endAngleDeg + 80.0;
        double wing1Rad = (tangentDeg + 136.0) * Math.PI / 180.0;
        double wing2Rad = (tangentDeg - 136.0) * Math.PI / 180.0;

        var p1 = new PointF(tipX + wingLen * (float)Math.Cos(wing1Rad), tipY + wingLen * (float)Math.Sin(wing1Rad));
        var p2 = new PointF(tipX, tipY);
        var p3 = new PointF(tipX + wingLen * (float)Math.Cos(wing2Rad), tipY + wingLen * (float)Math.Sin(wing2Rad));

        using (var pen = new Pen(color, strokeWidth - 4f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        {
            g.DrawLines(pen, new[] { p1, p2, p3 });
        }
    }

    private static Bitmap ResizeBitmap(Bitmap src, int targetSize)
    {
        var dst = new Bitmap(targetSize, targetSize, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(dst))
        {
            SetupGraphics(g);
            g.Clear(Color.Transparent);
            g.DrawImage(src, new Rectangle(0, 0, targetSize, targetSize));
        }
        return dst;
    }

    private static void SavePngAndIco(Bitmap master1024, string outputDir, string baseName)
    {
        string pngPath = Path.Combine(outputDir, baseName + ".png");
        using (var bmp512 = ResizeBitmap(master1024, 512))
        {
            bmp512.Save(pngPath, ImageFormat.Png);
        }

        int[] sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var pngFrames = new List<byte[]>();

        foreach (int s in sizes)
        {
            using (var resized = ResizeBitmap(master1024, s))
            using (var ms = new MemoryStream())
            {
                resized.Save(ms, ImageFormat.Png);
                pngFrames.Add(ms.ToArray());
            }
        }

        string icoPath = Path.Combine(outputDir, baseName + ".ico");
        using (var fs = new FileStream(icoPath, FileMode.Create, FileAccess.Write))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);

            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                byte dim = s >= 256 ? (byte)0 : (byte)s;
                bw.Write(dim);
                bw.Write(dim);
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)pngFrames[i].Length);
                bw.Write((uint)offset);
                offset += pngFrames[i].Length;
            }

            for (int i = 0; i < sizes.Length; i++)
            {
                bw.Write(pngFrames[i]);
            }
        }
    }
}
