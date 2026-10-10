using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShopeeBackend.Models;
using ShopeeBackend.Services;

namespace ShopeeBackend.Views
{
    /// <summary>
    /// Lightbox dialog displaying product image with dark backdrop overlay.
    /// Closes immediately when clicking outside the image frame or pressing Esc.
    /// </summary>
    public class ProductImageViewerDialog : Form
    {
        private readonly ScrapedProduct _product;
        private readonly string _localPath;

        private RoundedPanel pnlCard = null!;
        private PictureBox picImage = null!;
        private Label lblFilePath = null!;
        private Label lblBackdropHint = null!;
        private Button btnBackdropClose = null!;
        private RoundedButton btnOpenDefault = null!;
        private RoundedButton btnOpenFolder = null!;
        private RoundedButton btnClose = null!;
        private RoundedButton btnCardClose = null!;

        public ProductImageViewerDialog(ScrapedProduct product, Form? owner = null)
        {
            _product = product ?? throw new ArgumentNullException(nameof(product));
            _localPath = ImageDownloadService.GetLocalImagePath(_product);

            InitializeLightbox(owner);
            LoadImage();
        }

        private void InitializeLightbox(Form? owner)
        {
            this.Text            = string.IsNullOrWhiteSpace(_product.Name) ? "Ảnh sản phẩm" : _product.Name;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar   = false;
            this.ShowIcon        = false;
            this.KeyPreview      = true;
            this.DoubleBuffered  = true;
            this.BackColor       = Color.FromArgb(16, 18, 24);
            this.Opacity         = 0.96;

            // Cover owner's window bounds or center on screen
            if (owner != null && owner.WindowState != FormWindowState.Minimized)
            {
                this.StartPosition = FormStartPosition.Manual;
                if (owner.WindowState == FormWindowState.Maximized)
                    this.Bounds = Screen.FromControl(owner).WorkingArea;
                else
                    this.Bounds = owner.Bounds;
            }
            else
            {
                this.StartPosition = FormStartPosition.CenterScreen;
                this.Size          = new Size(1100, 800);
            }

            // Keyboard shortcut: Esc to close
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) this.Close();
            };

            // 1. Top hint on backdrop: "Nhấp chuột ra ngoài hoặc nhấn Esc để đóng"
            lblBackdropHint = new Label
            {
                Text      = "Nhấp chuột ra ngoài khung ảnh hoặc nhấn Esc để đóng",
                Font      = UiTheme.FontCaption,
                ForeColor = Color.FromArgb(170, 173, 190),
                TextAlign = ContentAlignment.MiddleCenter,
                Height    = 34,
                Dock      = DockStyle.Top,
                Cursor    = Cursors.Hand,
                BackColor = Color.Transparent
            };
            lblBackdropHint.Click += (s, e) => this.Close();
            this.Controls.Add(lblBackdropHint);

            // 2. Backdrop top-right "✕" close button
            btnBackdropClose = new Button
            {
                Text      = "✕",
                Font      = new Font("Segoe UI", 13f, FontStyle.Regular),
                ForeColor = Color.FromArgb(190, 193, 210),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size      = new Size(38, 34),
                Cursor    = Cursors.Hand,
                Anchor    = AnchorStyles.Top | AnchorStyles.Right
            };
            btnBackdropClose.FlatAppearance.BorderSize = 0;
            btnBackdropClose.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 48, 62);
            btnBackdropClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnBackdropClose);

            // 3. Central Image Card
            pnlCard = new RoundedPanel
            {
                CornerRadius = 10,
                BorderColor  = Color.FromArgb(50, 54, 68),
                BorderWidth  = 1,
                BackColor    = Color.White,
                Padding      = new Padding(18, 14, 18, 14)
            };
            this.Controls.Add(pnlCard);

            BuildCardControls();
            UpdateCardLayout();

            // Resize event: keep card centered
            this.Resize += (s, e) => UpdateCardLayout();

            // Click outside card (on dark backdrop) immediately closes the lightbox
            this.MouseDown += (s, e) =>
            {
                if (pnlCard != null && !pnlCard.Bounds.Contains(e.Location))
                {
                    this.Close();
                }
            };
            this.Click += (s, e) =>
            {
                Point pt = this.PointToClient(Cursor.Position);
                if (pnlCard != null && !pnlCard.Bounds.Contains(pt))
                {
                    this.Close();
                }
            };
        }

        private void UpdateCardLayout()
        {
            int padX = 60;
            int padY = 46;
            int maxW = Math.Max(420, this.ClientSize.Width - padX * 2);
            int maxH = Math.Max(420, this.ClientSize.Height - padY * 2);
            int cardW = Math.Min(780, maxW);
            int cardH = Math.Min(760, maxH);

            pnlCard.Size = new Size(cardW, cardH);
            pnlCard.Location = new Point(
                Math.Max(0, (this.ClientSize.Width - cardW) / 2),
                Math.Max(0, (this.ClientSize.Height - cardH) / 2)
            );

            btnBackdropClose.Location = new Point(this.ClientSize.Width - 46, 6);
        }

        private void BuildCardControls()
        {
            // A. Header (Title + Meta + Card Close Button)
            Panel pnlHead = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 58,
                BackColor = Color.White
            };

            btnCardClose = new RoundedButton
            {
                Text         = "✕",
                Font         = new Font("Segoe UI", 10f, FontStyle.Bold),
                Size         = new Size(32, 28),
                CornerRadius = 4,
                Cursor       = Cursors.Hand,
                Dock         = DockStyle.Right,
                Margin       = new Padding(6, 2, 0, 0)
            };
            btnCardClose.Click += (s, e) => this.Close();

            Panel pnlHeadTexts = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = Color.White
            };

            Label lblTitle = new Label
            {
                Text         = _product.Name,
                Font         = UiTheme.FontSection,
                ForeColor    = UiTheme.TextPrimary,
                Dock         = DockStyle.Top,
                Height       = 24,
                AutoEllipsis = true,
                TextAlign    = ContentAlignment.MiddleLeft
            };

            string metaText = $"Giá: {_product.FormattedPrice}   ·   Đã bán: {_product.FormattedSold}   ·   Đánh giá: {_product.FormattedRating}   ·   Nơi bán: {_product.Location}";
            Label lblMeta = new Label
            {
                Text         = metaText,
                Font         = UiTheme.FontCaption,
                ForeColor    = UiTheme.TextSecondary,
                Dock         = DockStyle.Top,
                Height       = 22,
                AutoEllipsis = true,
                TextAlign    = ContentAlignment.MiddleLeft
            };

            pnlHeadTexts.Controls.Add(lblMeta);
            pnlHeadTexts.Controls.Add(lblTitle);
            lblTitle.BringToFront();

            pnlHead.Controls.Add(pnlHeadTexts);
            pnlHead.Controls.Add(btnCardClose);
            pnlHeadTexts.BringToFront();
            pnlCard.Controls.Add(pnlHead);

            // B. Footer Action Bar
            Panel pnlFooter = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 42,
                BackColor = Color.White,
                Padding   = new Padding(0, 6, 0, 0)
            };

            lblFilePath = new Label
            {
                Text         = "Đang kiểm tra ảnh...",
                Dock         = DockStyle.Fill,
                Font         = UiTheme.FontCaption,
                ForeColor    = UiTheme.TextMuted,
                TextAlign    = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            FlowLayoutPanel pnlButtons = new FlowLayoutPanel
            {
                Dock          = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                AutoSize      = true,
                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                BackColor     = Color.White,
                Margin        = Padding.Empty
            };

            btnOpenDefault = new RoundedButton
            {
                Text      = "Mở ảnh gốc",
                IconGlyph = "\uE8B7",
                IconRole  = ButtonIconRole.Primary,
                Height    = 30,
                Margin    = new Padding(4, 2, 4, 0)
            };
            btnOpenDefault.Click += (s, e) => OpenInDefaultApp();

            btnOpenFolder = new RoundedButton
            {
                Text      = "Mở thư mục",
                IconGlyph = "\uED25",
                IconRole  = ButtonIconRole.Info,
                Height    = 30,
                Margin    = new Padding(4, 2, 4, 0)
            };
            btnOpenFolder.Click += (s, e) => OpenInFolder();

            btnClose = new RoundedButton
            {
                Text   = "Đóng (Esc)",
                Height = 30,
                Margin = new Padding(4, 2, 0, 0)
            };
            btnClose.Click += (s, e) => this.Close();

            pnlButtons.Controls.AddRange(new Control[] { btnOpenDefault, btnOpenFolder, btnClose });
            pnlFooter.Controls.Add(lblFilePath);
            pnlFooter.Controls.Add(pnlButtons);
            lblFilePath.BringToFront();

            pnlCard.Controls.Add(pnlFooter);

            // C. Center Image Frame
            RoundedPanel pnlImageWrap = new RoundedPanel
            {
                Dock         = DockStyle.Fill,
                CornerRadius = 6,
                BorderColor  = UiTheme.BorderLight,
                BackColor    = Color.FromArgb(248, 248, 250),
                Padding      = new Padding(10)
            };

            picImage = new PictureBox
            {
                Dock      = DockStyle.Fill,
                SizeMode  = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Cursor    = Cursors.Hand
            };

            var tip = new ToolTip();
            tip.SetToolTip(picImage, "Nhấp đúp chuột để mở ảnh bằng trình xem ảnh Windows");
            picImage.DoubleClick += (s, e) => OpenInDefaultApp();

            pnlImageWrap.Controls.Add(picImage);
            pnlCard.Controls.Add(pnlImageWrap);
            pnlImageWrap.BringToFront();
        }

        private void LoadImage()
        {
            if (File.Exists(_localPath) && new FileInfo(_localPath).Length > 0)
            {
                try
                {
                    byte[] bytes = File.ReadAllBytes(_localPath);
                    using var ms = new MemoryStream(bytes);
                    picImage.Image = new Bitmap(ms);

                    var fi = new FileInfo(_localPath);
                    lblFilePath.Text = $"📁 {_localPath}  ({picImage.Image.Width}x{picImage.Image.Height} px · {fi.Length / 1024:N0} KB)";
                    btnOpenDefault.Enabled = true;
                    btnOpenFolder.Enabled = true;
                    return;
                }
                catch { }
            }

            // Fallback to ImageCache thumbnail while downloading
            var cached = ImageCache.GetCached(_product.ImageUrl);
            if (cached != null)
            {
                picImage.Image = cached;
            }

            lblFilePath.Text = "🌐 Đang tải ảnh gốc từ Shopee về máy...";
            btnOpenDefault.Enabled = false;
            btnOpenFolder.Enabled = false;

            Task.Run(async () =>
            {
                bool success = await ImageDownloadService.DownloadProductImageAsync(_product);
                if (success && File.Exists(_localPath))
                {
                    try
                    {
                        byte[] bytes = await File.ReadAllBytesAsync(_localPath);
                        this.BeginInvoke(new Action(() =>
                        {
                            if (!this.IsDisposed)
                            {
                                using var ms = new MemoryStream(bytes);
                                picImage.Image?.Dispose();
                                picImage.Image = new Bitmap(ms);

                                var fi = new FileInfo(_localPath);
                                lblFilePath.Text = $"📁 {_localPath}  ({picImage.Image.Width}x{picImage.Image.Height} px · {fi.Length / 1024:N0} KB)";
                                btnOpenDefault.Enabled = true;
                                btnOpenFolder.Enabled = true;
                            }
                        }));
                        return;
                    }
                    catch { }
                }

                this.BeginInvoke(new Action(() =>
                {
                    if (!this.IsDisposed)
                    {
                        lblFilePath.Text = "⚠️ Chưa có file ảnh trên ổ đĩa và không tải được từ Shopee";
                    }
                }));
            });
        }

        private void OpenInDefaultApp()
        {
            if (File.Exists(_localPath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(_localPath) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    SuiteDialog.Alert(this, "Lỗi", $"Không thể mở ảnh: {ex.Message}");
                }
            }
            else if (!string.IsNullOrWhiteSpace(_product.ImageUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(_product.ImageUrl) { UseShellExecute = true });
                }
                catch { }
            }
        }

        private void OpenInFolder()
        {
            if (File.Exists(_localPath))
            {
                try
                {
                    Process.Start("explorer.exe", $"/select,\"{_localPath}\"");
                }
                catch (Exception ex)
                {
                    SuiteDialog.Alert(this, "Lỗi", $"Không thể mở thư mục: {ex.Message}");
                }
            }
            else
            {
                string folder = GlobalState.ImageStorageFolder;
                if (Directory.Exists(folder))
                {
                    try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
                    catch { }
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            picImage.Image?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
