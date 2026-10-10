using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShopeeBackend
{
    public enum ButtonIconRole { None, Primary, Success, Danger, Info, Muted }
    public enum DialogTone { Primary, Danger }

    public static class UiTheme
    {
        // ══════════════════════════════════════════════════════════════
        // DESIGN TOKENS FROM SHOPEE.SUITE
        // ══════════════════════════════════════════════════════════════

        public static readonly Color Accent        = ColorTranslator.FromHtml("#EE4D2D");
        public static readonly Color AccentHover   = ColorTranslator.FromHtml("#D8401F");
        public static readonly Color AccentPressed = ColorTranslator.FromHtml("#C93B1D");
        public static readonly Color Success       = ColorTranslator.FromHtml("#1F8A4C");
        public static readonly Color SuccessGreen  = ColorTranslator.FromHtml("#16A34A");
        public static readonly Color Danger        = ColorTranslator.FromHtml("#C22B1E");
        public static readonly Color Warning       = ColorTranslator.FromHtml("#B06F06");
        public static readonly Color Info          = ColorTranslator.FromHtml("#2B5FC4");

        // Backgrounds & Cards
        public static readonly Color WindowBg      = ColorTranslator.FromHtml("#F7F5F3");
        public static readonly Color CardBg        = ColorTranslator.FromHtml("#FFFFFF");
        public static readonly Color SubtleBg      = ColorTranslator.FromHtml("#FBFAF9");

        // Borders
        public static readonly Color BorderStrong     = ColorTranslator.FromHtml("#E5E1DD");
        public static readonly Color BorderLight      = ColorTranslator.FromHtml("#EBE7E3");
        public static readonly Color BorderLightest   = ColorTranslator.FromHtml("#F4F1EE");
        public static readonly Color BorderDefault    = ColorTranslator.FromHtml("#E2DDD8");
        public static readonly Color InputHoverBorder = ColorTranslator.FromHtml("#CFC8C2");

        // Brand Tints
        public static readonly Color BrandTint       = ColorTranslator.FromHtml("#FFF3EF");
        public static readonly Color BrandTintBorder = ColorTranslator.FromHtml("#FBD6CA");
        public static readonly Color BrandText       = ColorTranslator.FromHtml("#C93B1D");

        // Typography
        public static readonly Color TextPrimary   = ColorTranslator.FromHtml("#2C2724");
        public static readonly Color TextBody      = ColorTranslator.FromHtml("#423C38");
        public static readonly Color TextSecondary = ColorTranslator.FromHtml("#675F5A");
        public static readonly Color TextMuted     = ColorTranslator.FromHtml("#8B837D");

        // Dark Log Terminal
        public static readonly Color LogBg = ColorTranslator.FromHtml("#121212");
        public static readonly Color LogFg = ColorTranslator.FromHtml("#E8E8E8");

        // Fonts
        public static readonly Font FontTitle   = new Font("Segoe UI", 13f,  FontStyle.Regular);
        public static readonly Font FontSection = new Font("Segoe UI", 10.5f, FontStyle.Regular);
        public static readonly Font FontBody    = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        public static readonly Font FontCaption = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        public static readonly Font FontConsole = new Font("Cascadia Mono", 9f, FontStyle.Regular);

        // Icon-role colour mapping
        public static Color GetRoleColor(ButtonIconRole role) => role switch
        {
            ButtonIconRole.Primary => Accent,
            ButtonIconRole.Success => Success,
            ButtonIconRole.Danger  => Danger,
            ButtonIconRole.Info    => Info,
            ButtonIconRole.Muted   => TextMuted,
            _                      => TextBody
        };

        // Graphics helper: full rounded path
        public static GraphicsPath GetRoundedPath(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            if (d <= 0) { path.AddRectangle(bounds); return path; }
            path.AddArc(bounds.X,          bounds.Y,          d, d, 180, 90);
            path.AddArc(bounds.Right - d,  bounds.Y,          d, d, 270, 90);
            path.AddArc(bounds.Right - d,  bounds.Bottom - d, d, d,   0, 90);
            path.AddArc(bounds.X,          bounds.Bottom - d, d, d,  90, 90);
            path.CloseFigure();
            return path;
        }

        // DataGrid — header 32, row 32 (ProductDataView overrides row to 44)
        public static void StyleSuiteDataGrid(DataGridView dgv)
        {
            dgv.BackgroundColor = CardBg;
            dgv.BorderStyle     = BorderStyle.None;
            dgv.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgv.GridColor       = BorderLightest;
            dgv.EnableHeadersVisualStyles = false;
            dgv.RowHeadersVisible         = false;
            dgv.SelectionMode             = DataGridViewSelectionMode.FullRowSelect;
            dgv.MultiSelect               = true;

            dgv.ColumnHeadersBorderStyle                      = DataGridViewHeaderBorderStyle.None;
            dgv.ColumnHeadersDefaultCellStyle.BackColor       = SubtleBg;
            dgv.ColumnHeadersDefaultCellStyle.ForeColor       = TextSecondary;
            dgv.ColumnHeadersDefaultCellStyle.SelectionBackColor = SubtleBg;
            dgv.ColumnHeadersDefaultCellStyle.SelectionForeColor = TextSecondary;
            dgv.ColumnHeadersDefaultCellStyle.Font            = FontBody;
            dgv.ColumnHeadersDefaultCellStyle.Alignment       = DataGridViewContentAlignment.MiddleLeft;
            dgv.ColumnHeadersDefaultCellStyle.WrapMode        = DataGridViewTriState.False;
            dgv.ColumnHeadersDefaultCellStyle.Padding         = new Padding(8, 0, 8, 0);
            dgv.ColumnHeadersHeight                           = 44;
            dgv.ColumnHeadersHeightSizeMode                   = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

            dgv.DefaultCellStyle.BackColor          = CardBg;
            dgv.DefaultCellStyle.ForeColor          = TextBody;
            dgv.DefaultCellStyle.Font               = FontBody;
            dgv.DefaultCellStyle.SelectionBackColor = BrandTint;
            dgv.DefaultCellStyle.SelectionForeColor = TextPrimary;
            dgv.DefaultCellStyle.Padding            = new Padding(10, 0, 10, 0);
            dgv.RowTemplate.Height                  = 36;

            // No zebra — both rows same white background
            dgv.AlternatingRowsDefaultCellStyle.BackColor          = CardBg;
            dgv.AlternatingRowsDefaultCellStyle.SelectionBackColor = BrandTint;
            dgv.AlternatingRowsDefaultCellStyle.SelectionForeColor = TextPrimary;
            dgv.AlternatingRowsDefaultCellStyle.Font               = FontBody;
        }

        // Page header — transparent on WindowBg, Semibold 17 title, 12 subtitle
        public static Panel CreatePageHead(string title, string subtitle)
        {
            Panel pnl = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 58,
                BackColor = WindowBg
            };

            Font titleFont;
            try   { titleFont = new Font("Segoe UI Semibold", 13f, FontStyle.Regular); }
            catch { titleFont = FontTitle; }
            var subFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            int titleH = (int)Math.Ceiling(titleFont.GetHeight()) + 2;
            int subH   = (int)Math.Ceiling(subFont.GetHeight()) + 2;
            pnl.Height = 8 + titleH + 2 + subH + 6;

            Label lblT = new Label
            {
                Text      = title,
                Font      = titleFont,
                ForeColor = TextPrimary,
                AutoSize  = false,
                Location  = new Point(18, 6),
                Size      = new Size(800, titleH),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Label lblS = new Label
            {
                Text      = subtitle,
                Font      = subFont,
                ForeColor = TextSecondary,
                AutoSize  = false,
                Location  = new Point(18, 6 + titleH + 2),
                Size      = new Size(800, subH),
                TextAlign = ContentAlignment.MiddleLeft
            };

            pnl.Controls.Add(lblT);
            pnl.Controls.Add(lblS);
            pnl.Resize += (s, e) =>
            {
                lblT.Width = pnl.Width - 36;
                lblS.Width = pnl.Width - 36;
            };
            return pnl;
        }
    }

    // ══════════════════════════════════════════════════════════════
    // ROUNDED PANEL
    // ══════════════════════════════════════════════════════════════
    public class RoundedPanel : Panel
    {
        public int   CornerRadius { get; set; } = 6;
        public Color BorderColor  { get; set; } = ColorTranslator.FromHtml("#EBE7E3");
        public int   BorderWidth  { get; set; } = 1;

        public RoundedPanel()
        {
            this.DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = UiTheme.CardBg;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode    = SmoothingMode.AntiAlias;
            g.PixelOffsetMode  = PixelOffsetMode.HighQuality;
            if (this.Width <= 2 || this.Height <= 2) return;

            var rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using var path  = UiTheme.GetRoundedPath(rect, CornerRadius);
            using var brush = new SolidBrush(this.BackColor);
            g.FillPath(brush, path);
            if (BorderWidth > 0 && BorderColor != Color.Transparent)
            {
                using var pen = new Pen(BorderColor, BorderWidth);
                g.DrawPath(pen, path);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // ROUNDED BUTTON — MDL2 icon, Active state, height 30
    // ══════════════════════════════════════════════════════════════
    public class RoundedButton : Button
    {
        private static readonly Font _mdl2Font = new Font("Segoe MDL2 Assets", 9f, FontStyle.Regular);
        private static readonly Font _btnFont  = new Font("Segoe UI", 9f, FontStyle.Regular);

        public int            CornerRadius { get; set; } = 4;
        public string         IconGlyph    { get; set; } = "";
        public ButtonIconRole IconRole     { get; set; } = ButtonIconRole.None;
        public Color          IconColor    { get; set; } = Color.Transparent;
        public bool           Active       { get; set; } = false;

        // Legacy alias kept for backward compat (no-op rename)
        [System.ComponentModel.Browsable(false)]
        public string IconText { get => IconGlyph; set => IconGlyph = value; }

        private bool _isHovered = false;
        private bool _isPressed = false;

        public RoundedButton()
        {
            this.DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            this.Cursor       = Cursors.Hand;
            this.Font         = _btnFont;
            this.AutoSize     = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.Height       = 30;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            string glyph = IconGlyph ?? "";
            string text  = this.Text ?? "";
            var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoClipping;

            using var g = Graphics.FromHwnd(IntPtr.Zero);
            int iconW = 0;
            if (!string.IsNullOrEmpty(glyph))
                iconW = TextRenderer.MeasureText(g, glyph, _mdl2Font, Size.Empty, flags).Width + 7;
            int textW = TextRenderer.MeasureText(g, text, _btnFont, Size.Empty, flags).Width;
            return new Size(iconW + textW + 24, 30);
        }

        protected override void OnMouseEnter(EventArgs e) { _isHovered = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _isHovered = false; _isPressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _isPressed = true;  Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)   { _isPressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode   = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (this.Width <= 2 || this.Height <= 2) return;

            var rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using var path = UiTheme.GetRoundedPath(rect, CornerRadius);

            Color bg, border;
            if (Active)
            {
                bg = ColorTranslator.FromHtml("#FFF3EF");
                border = ColorTranslator.FromHtml("#FBD6CA");
            }
            else if (!this.Enabled)
            {
                bg = ColorTranslator.FromHtml("#FAFAFA");
                border = ColorTranslator.FromHtml("#E8E4E0");
            }
            else if (_isPressed)
            {
                bg = ColorTranslator.FromHtml("#F2EFEC");
                border = ColorTranslator.FromHtml("#CFC8C2");
            }
            else if (_isHovered)
            {
                bg = ColorTranslator.FromHtml("#FAF8F6");
                border = ColorTranslator.FromHtml("#CFC8C2");
            }
            else
            {
                bg = ColorTranslator.FromHtml("#FFFFFF");
                border = ColorTranslator.FromHtml("#E2DDD8");
            }

            using (var brush = new SolidBrush(bg)) g.FillPath(brush, path);
            using (var pen   = new Pen(border, 1))  g.DrawPath(pen, path);

            // Colours for text and icon
            Color textColor, iconColor;
            if (Active)
            {
                textColor = UiTheme.Accent;
                iconColor = UiTheme.Accent;
            }
            else if (!this.Enabled)
            {
                textColor = ColorTranslator.FromHtml("#B0A89E");
                iconColor = ColorTranslator.FromHtml("#B0A89E");
            }
            else
            {
                textColor = ColorTranslator.FromHtml("#423C38");
                iconColor = (IconColor != Color.Transparent && !IconColor.IsEmpty)
                    ? IconColor
                    : UiTheme.GetRoleColor(IconRole);
            }

            string glyph = IconGlyph ?? "";
            string text  = this.Text  ?? "";
            var tf = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
                     TextFormatFlags.NoClipping;

            if (!string.IsNullOrEmpty(glyph))
            {
                Size iconSz    = TextRenderer.MeasureText(g, glyph, _mdl2Font, Size.Empty, tf);
                Size textSz    = TextRenderer.MeasureText(g, text,  _btnFont,  Size.Empty, tf);
                int  contentW  = iconSz.Width + 7 + textSz.Width;
                int  startX    = Math.Max(12, (this.Width - contentW) / 2);

                TextRenderer.DrawText(g, glyph, _mdl2Font,
                    new Rectangle(startX, 0, iconSz.Width, this.Height), iconColor, tf | TextFormatFlags.Left);
                TextRenderer.DrawText(g, text,  _btnFont,
                    new Rectangle(startX + iconSz.Width + 7, 0, this.Width - startX - iconSz.Width - 7 - 4, this.Height),
                    textColor, tf | TextFormatFlags.Left);
            }
            else
            {
                int pad = (this.Width < 45) ? 2 : 8;
                TextRenderer.DrawText(g, text, _btnFont,
                    new Rectangle(pad, 0, Math.Max(0, this.Width - pad * 2), this.Height), textColor,
                    tf | TextFormatFlags.HorizontalCenter);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // SUITE FIELD — rounded input with focus underline
    // ══════════════════════════════════════════════════════════════
    public class SuiteField : Panel
    {
        private static readonly Color FieldBg        = ColorTranslator.FromHtml("#FBFAF9");
        private static readonly Color FieldBorder    = ColorTranslator.FromHtml("#E2DDD8");
        private static readonly Color IdleUnderline  = ColorTranslator.FromHtml("#CFC8C2");
        private static readonly Color FocusUnderline = ColorTranslator.FromHtml("#EE4D2D");

        private readonly TextBox _txt;
        private bool _focused = false;

        public SuiteField()
        {
            this.Height = 30;
            this.DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = FieldBg;

            _txt = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor   = FieldBg,
                Font        = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor   = ColorTranslator.FromHtml("#2C2724"),
                TabStop     = true
            };
            _txt.GotFocus  += (s, e) => { _focused = true;  Invalidate(); };
            _txt.LostFocus += (s, e) => { _focused = false; Invalidate(); };
            this.Controls.Add(_txt);
            this.Resize += (s, e) => PositionInner();
            PositionInner();
        }

        private void PositionInner()
        {
            _txt.Location = new Point(8, (this.Height - _txt.Height) / 2);
            _txt.Width    = Math.Max(4, this.Width - 16);
        }

#pragma warning disable CS8765
        public override string Text
        {
            get => _txt?.Text ?? string.Empty;
            set { if (_txt != null) _txt.Text = value ?? string.Empty; }
        }
#pragma warning restore CS8765

        public string PlaceholderText
        {
            get => _txt.PlaceholderText;
            set => _txt.PlaceholderText = value;
        }

        public bool ReadOnly
        {
            get => _txt.ReadOnly;
            set => _txt.ReadOnly = value;
        }

#pragma warning disable CS0108
        public new event EventHandler? TextChanged
        {
            add    => _txt.TextChanged += value;
            remove => _txt.TextChanged -= value;
        }
#pragma warning restore CS0108

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode   = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (this.Width <= 4 || this.Height <= 4) return;

            var rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using var path  = UiTheme.GetRoundedPath(rect, 4);
            using var brush = new SolidBrush(FieldBg);
            g.FillPath(brush, path);
            using var borderPen = new Pen(FieldBorder, 1);
            g.DrawPath(borderPen, path);

            // Bottom underline 2px
            using var uPen = new Pen(_focused ? FocusUnderline : IdleUnderline, 2f);
            g.DrawLine(uPen, 4, this.Height - 2, this.Width - 4, this.Height - 2);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // SUITE TEXT AREA — multiline input for keywords / batch data
    // ══════════════════════════════════════════════════════════════
    public class SuiteTextArea : Panel
    {
        private static readonly Color FieldBg     = ColorTranslator.FromHtml("#FBFAF9");
        private static readonly Color FieldBorder = ColorTranslator.FromHtml("#E2DDD8");
        private static readonly Color FocusBorder = ColorTranslator.FromHtml("#EE4D2D");

        private readonly TextBox _txt;
        private bool _focused = false;

        public SuiteTextArea()
        {
            this.DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = FieldBg;
            this.Padding   = new Padding(8);

            _txt = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor   = FieldBg,
                Font        = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor   = ColorTranslator.FromHtml("#2C2724"),
                Multiline   = true,
                ScrollBars  = ScrollBars.Vertical,
                Dock        = DockStyle.Fill,
                TabStop     = true
            };
            _txt.GotFocus  += (s, e) => { _focused = true;  Invalidate(); };
            _txt.LostFocus += (s, e) => { _focused = false; Invalidate(); };
            this.Controls.Add(_txt);
        }

#pragma warning disable CS8765
        public override string Text
        {
            get => _txt?.Text ?? string.Empty;
            set { if (_txt != null) _txt.Text = value ?? string.Empty; }
        }
#pragma warning restore CS8765

        public string[] Lines
        {
            get => _txt?.Lines ?? Array.Empty<string>();
            set { if (_txt != null) _txt.Lines = value; }
        }

        public string PlaceholderText
        {
            get => _txt.PlaceholderText;
            set => _txt.PlaceholderText = value;
        }

        public bool ReadOnly
        {
            get => _txt.ReadOnly;
            set => _txt.ReadOnly = value;
        }

        public void AppendText(string text) => _txt.AppendText(text);
        public void Clear() => _txt.Clear();

#pragma warning disable CS0108
        public new event EventHandler? TextChanged
        {
            add    => _txt.TextChanged += value;
            remove => _txt.TextChanged -= value;
        }
#pragma warning restore CS0108

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode   = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (this.Width <= 4 || this.Height <= 4) return;

            var rect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
            using var path  = UiTheme.GetRoundedPath(rect, 4);
            using var brush = new SolidBrush(FieldBg);
            g.FillPath(brush, path);
            using var borderPen = new Pen(_focused ? FocusBorder : FieldBorder, 1);
            g.DrawPath(borderPen, path);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // SUITE COMBO — flat combo with owner-draw list
    // ══════════════════════════════════════════════════════════════
    public class SuiteCombo : ComboBox
    {
        public SuiteCombo()
        {
            this.DropDownStyle = ComboBoxStyle.DropDownList;
            this.FlatStyle     = FlatStyle.Flat;
            this.BackColor     = ColorTranslator.FromHtml("#FBFAF9");
            this.Font          = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.ForeColor     = ColorTranslator.FromHtml("#2C2724");
            this.DrawMode      = DrawMode.OwnerDrawFixed;
            this.ItemHeight    = 24;
            this.Height        = 30;
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            this.BackColor = this.Enabled ? ColorTranslator.FromHtml("#FBFAF9") : ColorTranslator.FromHtml("#EFECE8");
            this.ForeColor = this.Enabled ? ColorTranslator.FromHtml("#2C2724") : UiTheme.TextMuted;
            this.Invalidate();
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= this.Items.Count) return;
            var g = e.Graphics;
            bool hot = (e.State & DrawItemState.Selected) != 0;
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            Color itemBg;
            if (!this.Enabled)
            {
                itemBg = ColorTranslator.FromHtml("#EFECE8");
            }
            else if (edit)
            {
                itemBg = ColorTranslator.FromHtml("#FBFAF9");
            }
            else if (hot && e.Index == this.SelectedIndex)
            {
                itemBg = UiTheme.BrandTint;
            }
            else if (hot)
            {
                itemBg = ColorTranslator.FromHtml("#FAF8F6");
            }
            else
            {
                itemBg = Color.White;
            }

            g.FillRectangle(new SolidBrush(itemBg), e.Bounds);
            string text = this.Items[e.Index]?.ToString() ?? "";
            Color textColor = this.Enabled ? UiTheme.TextPrimary : UiTheme.TextMuted;
            TextRenderer.DrawText(g, text, this.Font,
                new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height),
                textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // SUITE DIALOG — replaces MessageBox
    // ══════════════════════════════════════════════════════════════
    public static class SuiteDialog
    {
        /// <summary>
        /// Modal confirm dialog. Returns Yes when user clicks the confirm button.
        /// </summary>
        public static DialogResult Confirm(IWin32Window owner, string title, string message,
            string confirmText, DialogTone tone = DialogTone.Danger)
        {
            using var frm = new DialogForm(title, message, confirmText, tone, isAlert: false);
            return frm.ShowDialog(owner);
        }

        /// <summary>
        /// Modal alert dialog with a single Dismiss button. Returns OK.
        /// </summary>
        public static DialogResult Alert(IWin32Window owner, string title, string message)
        {
            using var frm = new DialogForm(title, message, "Đóng", DialogTone.Primary, isAlert: true);
            return frm.ShowDialog(owner);
        }

        private class DialogForm : Form
        {
            public DialogForm(string title, string message, string confirmText,
                              DialogTone tone, bool isAlert)
            {
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.MaximizeBox     = false;
                this.MinimizeBox     = false;
                this.ShowIcon        = false;
                this.ShowInTaskbar   = false;
                this.StartPosition   = FormStartPosition.CenterParent;
                this.BackColor       = ColorTranslator.FromHtml("#F7F5F3");
                this.Text            = title;

                Font titleFont;
                try   { titleFont = new Font("Segoe UI Semibold", 11f, FontStyle.Regular); }
                catch { titleFont = new Font("Segoe UI",          11f, FontStyle.Bold);    }
                var msgFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);

                using var g0 = Graphics.FromHwnd(IntPtr.Zero);
                int longest = 0;
                foreach (var part in (message ?? "").Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int w = TextRenderer.MeasureText(g0, part, msgFont).Width;
                    if (w > longest) longest = w;
                }
                int textW = Math.Min(640, Math.Max(368, longest + 8));
                int titleH = Math.Max(24,
                    TextRenderer.MeasureText(g0, title, titleFont,
                        new Size(textW, 1000), TextFormatFlags.WordBreak).Height);
                int msgH = Math.Max(28,
                    TextRenderer.MeasureText(g0, message, msgFont,
                        new Size(textW, 1000), TextFormatFlags.WordBreak).Height + 6);

                int cardW  = textW + 40;
                int cardH  = 16 + titleH + 10 + msgH + 12 + 48 + 16;
                int formH  = cardH + 32;

                this.ClientSize = new Size(cardW + 32, formH);

                // White card
                var card = new RoundedPanel
                {
                    Location     = new Point(16, 16),
                    Size         = new Size(cardW, cardH),
                    CornerRadius = 6,
                    BackColor    = Color.White,
                    BorderColor  = ColorTranslator.FromHtml("#EBE7E3")
                };
                this.Controls.Add(card);

                // Title
                card.Controls.Add(new Label
                {
                    Text      = title,
                    Font      = titleFont,
                    ForeColor = ColorTranslator.FromHtml("#2C2724"),
                    Location  = new Point(20, 16),
                    Size      = new Size(textW, titleH),
                    AutoSize  = false
                });

                // Message
                card.Controls.Add(new Label
                {
                    Text      = message,
                    Font      = msgFont,
                    ForeColor = ColorTranslator.FromHtml("#423C38"),
                    Location  = new Point(20, 16 + titleH + 10),
                    Size      = new Size(textW, msgH),
                    AutoSize  = false
                });

                // Buttons — right-aligned
                int btnY = 16 + titleH + 10 + msgH + 12;

                string confirmGlyph = tone == DialogTone.Danger ? "\uE74D" : "\uE8FB";
                Color  confirmIcon  = tone == DialogTone.Danger ? UiTheme.Danger : UiTheme.Accent;

                var btnConfirm = new RoundedButton
                {
                    IconGlyph    = confirmGlyph,
                    IconColor    = confirmIcon,
                    Text         = confirmText,
                    DialogResult = isAlert ? DialogResult.OK : DialogResult.Yes,
                    Height       = 30
                };
                // Measure width manually (handle may not be created yet)
                btnConfirm.Width = MeasureWidth(confirmGlyph, confirmText);
                btnConfirm.Location = new Point(20 + textW - btnConfirm.Width, btnY + 9);
                card.Controls.Add(btnConfirm);

                RoundedButton? btnCancel = null;
                if (!isAlert)
                {
                    btnCancel = new RoundedButton
                    {
                        IconGlyph    = "\uE711",
                        IconRole     = ButtonIconRole.Muted,
                        Text         = "Hủy",
                        DialogResult = DialogResult.No,
                        Height       = 30
                    };
                    btnCancel.Width    = MeasureWidth("\uE711", "Hủy");
                    btnCancel.Location = new Point(btnConfirm.Left - btnCancel.Width - 8, btnY + 9);
                    card.Controls.Add(btnCancel);
                }

                this.AcceptButton = btnConfirm;
                this.CancelButton = isAlert ? (IButtonControl?)btnConfirm : (IButtonControl?)btnCancel;
            }

            private static int MeasureWidth(string glyph, string text)
            {
                using var g     = Graphics.FromHwnd(IntPtr.Zero);
                using var mdl2  = new Font("Segoe MDL2 Assets", 9f, FontStyle.Regular);
                using var bFont = new Font("Segoe UI", 9f, FontStyle.Regular);
                var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
                            TextFormatFlags.NoClipping;
                int iconW = string.IsNullOrEmpty(glyph) ? 0 :
                    TextRenderer.MeasureText(g, glyph, mdl2, Size.Empty, flags).Width + 7;
                int textW = TextRenderer.MeasureText(g, text, bFont, Size.Empty, flags).Width;
                return iconW + textW + 24;
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // TAB BUTTON — used in MainForm tab strip
    // ══════════════════════════════════════════════════════════════
    public class TabButton : Control
    {
        private static readonly Font _tabFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        public bool IsSelected { get; private set; }
        private bool _hovered = false;

        public TabButton()
        {
            this.DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            this.Cursor = Cursors.Hand;
            this.Height = 47;
            this.Margin = Padding.Empty;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            using var g  = Graphics.FromHwnd(IntPtr.Zero);
            var sz = TextRenderer.MeasureText(g, this.Text ?? "", _tabFont);
            this.Width = sz.Width + 24;
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true;  Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color bg = (_hovered && !IsSelected) ? ColorTranslator.FromHtml("#FAF8F6") : Color.White;
            g.Clear(bg);

            Color textColor = IsSelected ? UiTheme.TextPrimary : UiTheme.TextSecondary;
            TextRenderer.DrawText(g, this.Text, _tabFont,
                new Rectangle(0, 0, this.Width, this.Height - 3),
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine);

            if (IsSelected)
            {
                // 3px indicator at bottom; top corners rounded 2px
                var ir = new Rectangle(12, this.Height - 3, this.Width - 24, 3);
                using var brush = new SolidBrush(UiTheme.Accent);
                if (ir.Width > 4)
                {
                    using var path = new GraphicsPath();
                    int rd = 4;
                    path.AddArc(ir.X,          ir.Y, rd, rd, 180, 90);
                    path.AddArc(ir.Right - rd,  ir.Y, rd, rd, 270, 90);
                    path.AddLine(ir.Right, ir.Bottom, ir.X, ir.Bottom);
                    path.CloseFigure();
                    g.FillPath(brush, path);
                }
                else
                {
                    g.FillRectangle(brush, ir);
                }
            }
        }
    }
}
