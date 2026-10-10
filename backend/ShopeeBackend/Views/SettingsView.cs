using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShopeeBackend.Services;

namespace ShopeeBackend.Views
{
    public class SettingsView : UserControl
    {
        public SettingsView()
        {
            this.Dock      = DockStyle.Fill;
            this.BackColor = UiTheme.WindowBg;
            this.Font      = UiTheme.FontBody;

            InitializeLayout();
        }

        private void InitializeLayout()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.Size          = new Size(1140, 740);

            Panel pnlHead = UiTheme.CreatePageHead(
                "C\u00E0i \u0111\u1EB7t h\u1EC7 th\u1ED1ng & m\u00F4i tr\u01B0\u1EDDng",
                "C\u1EA5u h\u00ECnh \u0111\u01B0\u1EDDng d\u1EABn tr\u00ECnh duy\u1EC7t Brave, Extension v\u00E0 c\u01A1 s\u1EDF d\u1EEF li\u1EC7u SQLite.");
            this.Controls.Add(pnlHead);

            Panel bodyPanel = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = UiTheme.WindowBg,
                Padding   = new Padding(16, 12, 16, 16)
            };
            this.Controls.Add(bodyPanel);
            bodyPanel.BringToFront();

            FlowLayoutPanel flowPanel = new FlowLayoutPanel
            {
                Dock          = DockStyle.Fill,
                AutoScroll    = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents  = false,
                Padding       = new Padding(0)
            };

            var cards = new List<RoundedPanel>
            {
                CreateImageFolderCard(),
                CreateAutoRerunCard(),

                CreateSettingCard(
                    "Tr\u00ECnh duy\u1EC7t Brave (Brave Executable)",
                    @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe",
                    "\u0110\u01B0\u1EDDng d\u1EABn th\u1EF1c thi c\u1EE7a Brave. Tr\u00ECnh duy\u1EC7t ch\u1EA1y \u1EDF ch\u1EBF \u0111\u1ED9 s\u1EA1ch 100% (Clean Browser, kh\u00F4ng flag automation).",
                    null),

                CreateSettingCard(
                    "Th\u01B0 m\u1EE5c Shopee Extension",
                    @"D:\Projects\phan-tich-shopee\ShopeeExt",
                    "Extension t\u1EF1 \u0111\u1ED9ng load c\u00F9ng Brave \u0111\u1EC3 x\u1EED l\u00FD \u0111\u0103ng nh\u1EADp, bypass v\u00E0 c\u00E0o d\u1EEF li\u1EC7u danh m\u1EE5c.",
                    () => OpenFolder(@"D:\Projects\phan-tich-shopee\ShopeeExt")),

                CreateSettingCard(
                    "Th\u01B0 m\u1EE5c d\u1EEF li\u1EC7u & profile (AppData)",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShopeeBackend"),
                    "L\u01B0u tr\u1EEF c\u01A1 s\u1EDF d\u1EEF li\u1EC7u SQLite (shopee_data.db) v\u00E0 th\u01B0 m\u1EE5c Profile \u0111\u1ED9c l\u1EADp cho t\u1EEBng t\u00E0i kho\u1EA3n Shopee.",
                    () => OpenFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShopeeBackend")))
            };

            foreach (var card in cards) flowPanel.Controls.Add(card);

            // Resize cards to fill the flow panel width
            flowPanel.Resize += (s, e) =>
            {
                int w = Math.Max(500, flowPanel.ClientSize.Width);
                foreach (var c in cards) c.Width = w;
            };

            bodyPanel.Controls.Add(flowPanel);
        }

        private RoundedPanel CreateImageFolderCard()
        {
            RoundedPanel card = new RoundedPanel
            {
                Height       = 175,
                Width        = 900,
                BackColor    = UiTheme.CardBg,
                BorderColor  = UiTheme.BorderLight,
                CornerRadius = 6,
                Margin       = new Padding(0, 0, 0, 14)
            };

            var lblTitle = new Label
            {
                Text      = "Thư mục lưu ảnh sản phẩm (Product Images Storage)",
                Font      = UiTheme.FontSection,
                ForeColor = UiTheme.TextPrimary,
                AutoSize  = false
            };
            var lblDesc = new Label
            {
                Text      = "Tự động tải và lưu ảnh sản phẩm vào ổ đĩa máy tính khi quét danh mục (mặc định: e:\\images\\phan-tich-shopee). Ảnh lưu định dạng JPEG (.jpg) theo Item ID giúp hiển thị offline siêu tốc, tiết kiệm băng thông và không phụ thuộc mạng. Nếu thư mục chưa tồn tại, hệ thống sẽ tự động tạo mới.",
                Font      = UiTheme.FontCaption,
                ForeColor = UiTheme.TextSecondary,
                AutoSize  = false
            };
            var txtFolder = new SuiteField
            {
                Text     = GlobalState.ImageStorageFolder,
                ReadOnly = false,
                Height   = 30
            };
            var btnBrowse = new RoundedButton
            {
                IconGlyph = "\uE838",
                IconRole  = ButtonIconRole.Info,
                Text      = "Chọn thư mục..."
            };
            var btnSave = new RoundedButton
            {
                IconGlyph = "\uE74E",
                IconRole  = ButtonIconRole.Success,
                Text      = "Lưu thư mục"
            };
            var btnOpen = new RoundedButton
            {
                IconGlyph = "\uE8B7",
                IconRole  = ButtonIconRole.Muted,
                Text      = "Mở thư mục"
            };
            var btnDownloadAll = new RoundedButton
            {
                IconGlyph = "\uE896",
                IconRole  = ButtonIconRole.Primary,
                Text      = "Tải ảnh dữ liệu đã cào"
            };

            btnBrowse.Click += (s, e) =>
            {
                using var fbd = new FolderBrowserDialog();
                fbd.Description = "Chọn thư mục lưu ảnh sản phẩm Shopee";
                if (Directory.Exists(txtFolder.Text.Trim()))
                    fbd.SelectedPath = txtFolder.Text.Trim();
                if (fbd.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    txtFolder.Text = fbd.SelectedPath;
                }
            };

            btnSave.Click += (s, e) =>
            {
                string path = txtFolder.Text.Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    SuiteDialog.Alert(this, "Lỗi", "Đường dẫn thư mục không được để trống.");
                    return;
                }
                try
                {
                    DataManager.SetImageStorageFolder(path);
                    SuiteDialog.Alert(this, "Thành công", $"Đã lưu và khởi tạo thư mục lưu ảnh:\n{path}");
                }
                catch (Exception ex)
                {
                    SuiteDialog.Alert(this, "Lỗi", $"Không thể tạo hoặc lưu thư mục: {ex.Message}");
                }
            };

            btnOpen.Click += (s, e) =>
            {
                OpenFolder(GlobalState.ImageStorageFolder);
            };

            btnDownloadAll.Click += async (s, e) =>
            {
                var products = GlobalState.ScrapedProducts.ToList();
                int missingCount = products.Count(p => !string.IsNullOrWhiteSpace(p.ImageUrl) && !ImageDownloadService.HasLocalImage(p));
                if (missingCount == 0)
                {
                    SuiteDialog.Alert(this, "Thông báo", "Tất cả sản phẩm đã có ảnh đầy đủ trên ổ đĩa!");
                    return;
                }

                if (SuiteDialog.Confirm(this, "Tải ảnh sản phẩm",
                    $"Tìm thấy {missingCount:N0} sản phẩm chưa tải ảnh về máy.\nBạn có muốn bắt đầu tải ngầm ngay bây giờ không?",
                    "Bắt đầu tải", DialogTone.Primary) != DialogResult.Yes)
                    return;

                btnDownloadAll.Enabled = false;
                btnDownloadAll.Text = "Đang tải ảnh...";
                try
                {
                    int downloaded = await Task.Run(() => ImageDownloadService.DownloadBulkAsync(products));
                    SuiteDialog.Alert(this, "Hoàn tất", $"Đã hoàn tất tải {downloaded:N0} ảnh về thư mục:\n{GlobalState.ImageStorageFolder}");
                }
                catch (Exception ex)
                {
                    SuiteDialog.Alert(this, "Lỗi", $"Có lỗi khi tải ảnh: {ex.Message}");
                }
                finally
                {
                    btnDownloadAll.Enabled = true;
                    btnDownloadAll.Text = "Tải ảnh dữ liệu đã cào";
                }
            };

            card.Controls.Add(lblTitle);
            card.Controls.Add(lblDesc);
            card.Controls.Add(txtFolder);
            card.Controls.Add(btnBrowse);
            card.Controls.Add(btnSave);
            card.Controls.Add(btnOpen);
            card.Controls.Add(btnDownloadAll);

            bool layingOut = false;
            void Reflow()
            {
                if (layingOut || card.Width < 80) return;
                layingOut = true;
                int pad = 16;
                int inner = card.Width - pad * 2;
                int y = 14;
                int titleH = MeasureBlock(lblTitle.Text, lblTitle.Font, inner);
                lblTitle.SetBounds(pad, y, inner, titleH);
                y = lblTitle.Bottom + 6;
                int descH = MeasureBlock(lblDesc.Text, lblDesc.Font, inner);
                lblDesc.SetBounds(pad, y, inner, descH);
                y = lblDesc.Bottom + 12;

                int right = card.Width - pad;
                btnOpen.Location = new Point(right - btnOpen.Width, y);
                right = btnOpen.Left - 8;
                btnSave.Location = new Point(right - btnSave.Width, y);
                right = btnSave.Left - 8;
                btnBrowse.Location = new Point(right - btnBrowse.Width, y);
                right = btnBrowse.Left - 8;

                txtFolder.SetBounds(pad, y, Math.Max(80, right - pad), 30);

                y = txtFolder.Bottom + 10;
                btnDownloadAll.Location = new Point(pad, y);

                int nextH = btnDownloadAll.Bottom + 16;
                if (card.Height != nextH) card.Height = nextH;
                layingOut = false;
            }

            card.Resize += (s, e) => Reflow();
            btnBrowse.SizeChanged += (s, e) => Reflow();
            btnSave.SizeChanged += (s, e) => Reflow();
            btnOpen.SizeChanged += (s, e) => Reflow();
            btnDownloadAll.SizeChanged += (s, e) => Reflow();
            Reflow();
            return card;
        }

        private RoundedPanel CreateAutoRerunCard()
        {
            RoundedPanel card = new RoundedPanel
            {
                Height       = 140,
                Width        = 900,
                BackColor    = UiTheme.CardBg,
                BorderColor  = UiTheme.BorderLight,
                CornerRadius = 6,
                Margin       = new Padding(0, 0, 0, 14)
            };

            var lblTitle = new Label
            {
                Text      = "Chu kỳ tự động chạy lại toàn bộ danh mục (Auto Re-run Cycle)",
                Font      = UiTheme.FontSection,
                ForeColor = UiTheme.TextPrimary,
                AutoSize  = false
            };
            var lblDesc = new Label
            {
                Text      = "Nhập số ngày để tự động làm mới các danh mục đã quét xong (mặc định 7 ngày). Sau số ngày này, danh mục hoàn thành sẽ tự động chuyển về 'Chờ quét' để quét đối soát biến động doanh số phát sinh tìm sản phẩm tiềm năng. Nhập 0 để tắt tự động.",
                Font      = UiTheme.FontCaption,
                ForeColor = UiTheme.TextSecondary,
                AutoSize  = false
            };
            var txtDays = new SuiteField
            {
                Text     = GlobalState.AutoRerunDays.ToString(),
                ReadOnly = false,
                Height   = 30
            };
            var btnSave = new RoundedButton
            {
                IconGlyph = "\uE74E",
                IconRole  = ButtonIconRole.Success,
                Text      = "Lưu số ngày"
            };

            btnSave.Click += (s, e) =>
            {
                if (int.TryParse(txtDays.Text.Trim(), out int days) && days >= 0)
                {
                    ShopeeBackend.Services.DataManager.SetAutoRerunDays(days);
                    int resetCount = ShopeeBackend.Services.DataManager.CheckAndResetExpiredCategories();
                    string msg = days == 0 
                        ? "Đã tắt tính năng tự động chạy lại danh mục." 
                        : $"Đã lưu chu kỳ tự động chạy lại: {days} ngày.";
                    if (resetCount > 0)
                    {
                        msg += $"\nĐã làm mới {resetCount} danh mục đủ điều kiện để quét chu kỳ mới!";
                    }
                    SuiteDialog.Alert(this, "Thành công", msg);
                }
                else
                {
                    SuiteDialog.Alert(this, "Lỗi", "Vui lòng nhập số ngày hợp lệ (số nguyên >= 0).");
                }
            };

            card.Controls.Add(lblTitle);
            card.Controls.Add(lblDesc);
            card.Controls.Add(txtDays);
            card.Controls.Add(btnSave);

            bool layingOut = false;
            void Reflow()
            {
                if (layingOut || card.Width < 80) return;
                layingOut = true;
                int pad = 16;
                int inner = card.Width - pad * 2;
                int y = 14;
                int titleH = MeasureBlock(lblTitle.Text, lblTitle.Font, inner);
                lblTitle.SetBounds(pad, y, inner, titleH);
                y = lblTitle.Bottom + 6;
                int descH = MeasureBlock(lblDesc.Text, lblDesc.Font, inner);
                lblDesc.SetBounds(pad, y, inner, descH);
                y = lblDesc.Bottom + 12;
                btnSave.Location = new Point(card.Width - pad - btnSave.Width, y);
                txtDays.SetBounds(pad, y, Math.Max(80, btnSave.Left - pad - 12), 30);
                int nextH = txtDays.Bottom + 16;
                if (card.Height != nextH) card.Height = nextH;
                layingOut = false;
            }

            card.Resize += (s, e) => Reflow();
            btnSave.SizeChanged += (s, e) => Reflow();
            Reflow();
            return card;
        }

        private RoundedPanel CreateSettingCard(string title, string value, string desc, Action? onOpenFolder)
        {
            RoundedPanel card = new RoundedPanel
            {
                Height       = 140,
                Width        = 900,
                BackColor    = UiTheme.CardBg,
                BorderColor  = UiTheme.BorderLight,
                CornerRadius = 6,
                Margin       = new Padding(0, 0, 0, 14)
            };

            var lblTitle = new Label
            {
                Text      = title,
                Font      = UiTheme.FontSection,
                ForeColor = UiTheme.TextPrimary,
                AutoSize  = false
            };
            var lblDesc = new Label
            {
                Text      = desc,
                Font      = UiTheme.FontCaption,
                ForeColor = UiTheme.TextSecondary,
                AutoSize  = false
            };
            var txtVal = new SuiteField
            {
                Text     = value,
                ReadOnly = true,
                Height   = 30
            };
            card.Controls.Add(lblTitle);
            card.Controls.Add(lblDesc);
            card.Controls.Add(txtVal);

            RoundedButton? btnOpen = null;
            if (onOpenFolder != null)
            {
                btnOpen = new RoundedButton
                {
                    IconGlyph = "\uE8B7",
                    IconRole  = ButtonIconRole.Info,
                    Text      = "M\u1EDF th\u01B0 m\u1EE5c"
                };
                btnOpen.Click += (s, e) => onOpenFolder();
                card.Controls.Add(btnOpen);
            }

            bool layingOut = false;
            void Reflow()
            {
                if (layingOut || card.Width < 80) return;
                layingOut = true;
                int pad = 16;
                int inner = card.Width - pad * 2;
                int y = 14;
                int titleH = MeasureBlock(lblTitle.Text, lblTitle.Font, inner);
                lblTitle.SetBounds(pad, y, inner, titleH);
                y = lblTitle.Bottom + 6;
                int descH = MeasureBlock(lblDesc.Text, lblDesc.Font, inner);
                lblDesc.SetBounds(pad, y, inner, descH);
                y = lblDesc.Bottom + 12;
                if (btnOpen != null)
                {
                    btnOpen.Location = new Point(card.Width - pad - btnOpen.Width, y);
                    txtVal.SetBounds(pad, y, Math.Max(80, btnOpen.Left - pad - 12), 30);
                }
                else
                {
                    txtVal.SetBounds(pad, y, inner, 30);
                }
                int nextH = txtVal.Bottom + 16;
                if (card.Height != nextH) card.Height = nextH;
                layingOut = false;
            }

            card.Resize += (s, e) => Reflow();
            if (btnOpen != null) btnOpen.SizeChanged += (s, e) => Reflow();
            Reflow();
            return card;
        }

        private static int MeasureBlock(string text, Font font, int width)
        {
            var size = TextRenderer.MeasureText(text, font, new Size(Math.Max(40, width), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            return size.Height + 4;
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Process.Start("explorer.exe", path);
                else
                    SuiteDialog.Alert(this, "Th\u00F4ng b\u00E1o", $"Th\u01B0 m\u1EE5c ch\u01B0a t\u1ED3n t\u1EA1i: {path}");
            }
            catch (Exception ex)
            {
                SuiteDialog.Alert(this, "L\u1ED7i", $"Kh\u00F4ng th\u1EC3 m\u1EDF th\u01B0 m\u1EE5c: {ex.Message}");
            }
        }
    }
}
