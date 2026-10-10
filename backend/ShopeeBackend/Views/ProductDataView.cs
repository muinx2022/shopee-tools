using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using ShopeeBackend.Models;
using ShopeeBackend.Services;

namespace ShopeeBackend.Views
{
    public partial class ProductDataView : UserControl
    {
        private enum FilterMode { All, GoldenOpp, HasDelta, Rescanned, FirstScan, LowVoteHighSold, TopRated, Disqualified }

        private FilterMode currentFilter = FilterMode.All;
        private List<ScrapedProduct> allProducts        = new();
        private List<ScrapedProduct> filteredProducts   = new();
        private List<ScrapedProduct> currentPageProducts = new();

        // Paging state
        private int currentPage = 1;
        private int pageSize = 100;
        private int totalPages = 1;
        private decimal filteredTurnover = 0;
        private bool _isUpdatingCombos = false;
        private System.Windows.Forms.Timer? reloadDebounceTimer;

        private static readonly ConcurrentDictionary<string, string> ParentCategoryCache = new(StringComparer.OrdinalIgnoreCase);

        // KPI labels
        private Label lblTotalCountVal  = null!;
        private Label lblTotalCountSub  = null!;
        private Label lblTopMonthlyVal  = null!;
        private Label lblTopMonthlySub  = null!;
        private Label lblLowVoteVal     = null!;
        private Label lblLowVoteSub     = null!;
        private Label lblAvgPriceVal    = null!;
        private Label lblAvgPriceSub    = null!;
        private Action? relayoutKpis;

        // Toolbar controls
        private RoundedButton btnFilterAll          = null!;
        private RoundedButton btnFilterGoldenOpp    = null!;
        private RoundedButton btnFilterHasDelta     = null!;
        private RoundedButton btnFilterRescanned    = null!;
        private RoundedButton btnFilterFirstScan    = null!;
        private RoundedButton btnFilterLowVote      = null!;
        private RoundedButton btnFilterTopRated     = null!;
        private RoundedButton btnFilterDisqualified = null!;
        private SuiteCombo    cboParentCategory     = null!;
        private SuiteCombo    cboCategory           = null!;
        private RoundedButton btnExportExcel        = null!;
        private RoundedButton btnRefresh            = null!;
        private RoundedButton btnDelete             = null!;
        private RoundedButton btnClearAll           = null!;

        // Grid & Pagination controls
        private DataGridView  dgvProducts           = null!;
        private Label         lblGridStatus         = null!;
        private SuiteCombo    cboPageSize           = null!;
        private RoundedButton btnFirstPage          = null!;
        private RoundedButton btnPrevPage           = null!;
        private TextBox       txtCurrentPage        = null!;
        private Label         lblTotalPages         = null!;
        private RoundedButton btnNextPage           = null!;
        private RoundedButton btnLastPage           = null!;

        public ProductDataView()
        {
            this.Dock          = DockStyle.Fill;
            this.BackColor     = UiTheme.WindowBg;
            this.Font          = UiTheme.FontBody;
            this.AutoScaleMode = AutoScaleMode.None;

            InitializeLayout();
            ReloadData();

            reloadDebounceTimer = new System.Windows.Forms.Timer { Interval = 500 };
            reloadDebounceTimer.Tick += (s, e) =>
            {
                reloadDebounceTimer.Stop();
                if (this.IsHandleCreated && !this.IsDisposed)
                    ReloadData(preservePage: true);
            };

            GlobalState.ScrapedProducts.ListChanged += (s, e) =>
            {
                if (this.IsHandleCreated && !this.IsDisposed)
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        reloadDebounceTimer.Stop();
                        reloadDebounceTimer.Start();
                    }));
                }
            };

            GlobalState.Categories.ListChanged += (s, e) => ParentCategoryCache.Clear();
        }

        private void InitializeLayout()
        {
            this.Size = new Size(1140, 740);

            Panel pnlHead = UiTheme.CreatePageHead(
                "D\u1EEF li\u1EC7u s\u1EA3n ph\u1EA9m & Ph\u00E2n t\u00EDch",
                "Qu\u1EA3n l\u00FD d\u1EEF li\u1EC7u c\u00E0o t\u1EEB Shopee, theo d\u00F5i xu h\u01B0\u1EDBng b\u00E1n ch\u1EA1y, ph\u00E1t hi\u1EC7n s\u1EA3n ph\u1EA9m vote th\u1EA5p v\u00E0 xu\u1EA5t Excel.");
            this.Controls.Add(pnlHead);

            Panel bodyPanel = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = UiTheme.WindowBg,
                Padding   = new Padding(16, 12, 16, 16)
            };
            this.Controls.Add(bodyPanel);
            bodyPanel.BringToFront();

            Panel pnlKpis = CreateKpiSummaryPanel();
            bodyPanel.Controls.Add(pnlKpis);

            Panel pnlSep1 = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = UiTheme.WindowBg };
            bodyPanel.Controls.Add(pnlSep1);

            Panel pnlToolbar = CreateToolbarPanel();
            bodyPanel.Controls.Add(pnlToolbar);

            Panel pnlSep2 = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = UiTheme.WindowBg };
            bodyPanel.Controls.Add(pnlSep2);

            RoundedPanel pnlGridCard = new RoundedPanel
            {
                Dock         = DockStyle.Fill,
                CornerRadius = 6,
                BorderColor  = UiTheme.BorderLight,
                Padding      = new Padding(12)
            };

            Panel pnlGridFooter = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 38,
                BackColor = UiTheme.CardBg,
                Padding   = new Padding(4, 4, 4, 4)
            };

            FlowLayoutPanel pnlPagination = new FlowLayoutPanel
            {
                Dock          = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                AutoSize      = true,
                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                BackColor     = UiTheme.CardBg,
                Padding       = new Padding(0, 4, 0, 0),
                Margin        = Padding.Empty
            };

            cboPageSize = new SuiteCombo { Width = 130, Height = 28 };
            cboPageSize.Items.AddRange(new object[] { "50 / trang", "100 / trang", "200 / trang", "500 / trang" });
            cboPageSize.SelectedIndex = 1; // 100 / trang
            cboPageSize.Margin = new Padding(0, 1, 8, 0);
            cboPageSize.SelectedIndexChanged += (s, e) =>
            {
                int newSize = cboPageSize.SelectedIndex switch
                {
                    0 => 50,
                    1 => 100,
                    2 => 200,
                    3 => 500,
                    _ => 100
                };
                if (newSize != pageSize)
                {
                    pageSize = newSize;
                    currentPage = 1;
                    RenderPage();
                }
            };

            btnFirstPage = new RoundedButton
            {
                Text     = "«",
                AutoSize = false,
                Size     = new Size(32, 28),
                Margin   = new Padding(2, 1, 2, 0)
            };
            btnFirstPage.Click += (s, e) => GoToPage(1);

            btnPrevPage = new RoundedButton
            {
                Text     = "‹ Trước",
                AutoSize = false,
                Size     = new Size(72, 28),
                Margin   = new Padding(2, 1, 4, 0)
            };
            btnPrevPage.Click += (s, e) => GoToPage(currentPage - 1);

            txtCurrentPage = new TextBox
            {
                Text        = "1",
                Width       = 46,
                Height      = 24,
                TextAlign   = HorizontalAlignment.Center,
                Font        = UiTheme.FontBody,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor   = UiTheme.SubtleBg,
                ForeColor   = UiTheme.TextPrimary,
                Margin      = new Padding(2, 3, 2, 0)
            };
            txtCurrentPage.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                    e.Handled = true;
            };
            txtCurrentPage.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    JumpToPage(txtCurrentPage.Text);
                }
            };
            txtCurrentPage.Leave += (s, e) => JumpToPage(txtCurrentPage.Text);

            lblTotalPages = new Label
            {
                Text      = "/ 1",
                Font      = UiTheme.FontCaption,
                ForeColor = UiTheme.TextSecondary,
                AutoSize  = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin    = new Padding(2, 7, 4, 0)
            };

            btnNextPage = new RoundedButton
            {
                Text     = "Sau ›",
                AutoSize = false,
                Size     = new Size(68, 28),
                Margin   = new Padding(4, 1, 2, 0)
            };
            btnNextPage.Click += (s, e) => GoToPage(currentPage + 1);

            btnLastPage = new RoundedButton
            {
                Text     = "»",
                AutoSize = false,
                Size     = new Size(32, 28),
                Margin   = new Padding(2, 1, 4, 0)
            };
            btnLastPage.Click += (s, e) => GoToPage(totalPages);

            var tip = new ToolTip();
            tip.SetToolTip(btnFirstPage, "Về trang đầu");
            tip.SetToolTip(btnPrevPage, "Trang trước");
            tip.SetToolTip(txtCurrentPage, "Nhập số trang và nhấn Enter để chuyển nhanh");
            tip.SetToolTip(btnNextPage, "Trang kế tiếp");
            tip.SetToolTip(btnLastPage, "Đến trang cuối");
            tip.SetToolTip(cboPageSize, "Số lượng sản phẩm trên mỗi trang");

            pnlPagination.Controls.AddRange(new Control[]
            {
                cboPageSize,
                btnFirstPage,
                btnPrevPage,
                txtCurrentPage,
                lblTotalPages,
                btnNextPage,
                btnLastPage
            });

            lblGridStatus = new Label
            {
                Text         = "Đang tải dữ liệu...",
                Dock         = DockStyle.Fill,
                ForeColor    = UiTheme.TextSecondary,
                Font         = UiTheme.FontCaption,
                TextAlign    = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };

            pnlGridFooter.Controls.Add(lblGridStatus);
            pnlGridFooter.Controls.Add(pnlPagination);
            lblGridStatus.BringToFront();

            pnlGridCard.Controls.Add(pnlGridFooter);

            dgvProducts = new DataGridView
            {
                Dock                = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows  = false,
                RowTemplate         = { Height = 44 }  // image rows need 44px
            };
            UiTheme.StyleSuiteDataGrid(dgvProducts);
            dgvProducts.RowTemplate.Height = 44;
            try
            {
                typeof(DataGridView).InvokeMember("DoubleBuffered",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.SetProperty,
                    null, dgvProducts, new object[] { true });
            }
            catch { }
            SetupGridColumns();

            pnlGridCard.Controls.Add(dgvProducts);
            dgvProducts.BringToFront();

            bodyPanel.Controls.Add(pnlGridCard);
            pnlGridCard.BringToFront();
        }

        // ── KPI panel ────────────────────────────────────────────────────
        private Panel CreateKpiSummaryPanel()
        {
            Panel pnl = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 110,
                BackColor = UiTheme.WindowBg
            };

            int cardCount = 4, gap = 12;

            var card1 = CreateSingleKpiCard("\uE7B8", "T\u1ED4NG S\u1EA2N PH\u1EA8M",          out lblTotalCountVal, out lblTotalCountSub);
            var card2 = CreateSingleKpiCard("\uE9D9", "S\u1EA2N PH\u1EA8M C\u00D3 PH\u00C1T SINH", out lblTopMonthlyVal,  out lblTopMonthlySub);
            var card3 = CreateSingleKpiCard("\uE72C", "S\u1EA2N PH\u1EA8M QU\u00C9T L\u1EA0I",   out lblLowVoteVal,    out lblLowVoteSub);
            var card4 = CreateSingleKpiCard("\uE714", "GI\u00C1 TRUNG B\u00CCNH TH\u1ECB TR\u01AF\u1EE2NG", out lblAvgPriceVal, out lblAvgPriceSub);
            var cards = new[] { card1, card2, card3, card4 };

            pnl.Controls.AddRange(cards);

            void ResizeCards()
            {
                int totalW = pnl.Width;
                int cardW  = Math.Max(200, (totalW - gap * (cardCount - 1)) / cardCount);
                int cardH  = 0;
                foreach (var c in cards) cardH = Math.Max(cardH, LayoutKpiCard(c, cardW));
                card1.Bounds = new Rectangle(0, 0, cardW, cardH);
                card2.Bounds = new Rectangle(cardW + gap, 0, cardW, cardH);
                card3.Bounds = new Rectangle((cardW + gap) * 2, 0, cardW, cardH);
                card4.Bounds = new Rectangle((cardW + gap) * 3, 0, totalW - (cardW + gap) * 3, cardH);
                if (pnl.Height != cardH) pnl.Height = cardH;
            }

            pnl.Resize += (s, e) => ResizeCards();
            relayoutKpis = ResizeCards;
            ResizeCards();
            return pnl;
        }

        private RoundedPanel CreateSingleKpiCard(string mdl2Glyph, string groupLabel,
            out Label valLabel, out Label subLabel)
        {
            RoundedPanel card = new RoundedPanel
            {
                CornerRadius = 6,
                BorderColor  = UiTheme.BorderLight,
                BackColor    = UiTheme.CardBg,
                Padding      = new Padding(12, 10, 12, 8)
            };

            Label lblIcon = new Label
            {
                Name      = "kpiIcon",
                Text      = mdl2Glyph,
                Font      = new Font("Segoe MDL2 Assets", 12f, FontStyle.Regular),
                ForeColor = UiTheme.TextMuted,
                AutoSize  = false,
                TextAlign = ContentAlignment.MiddleCenter
            };
            card.Controls.Add(lblIcon);

            Font groupFont;
            try   { groupFont = new Font("Segoe UI Semibold", 8.5f, FontStyle.Regular); }
            catch { groupFont = UiTheme.FontCaption; }

            Label lblTitle = new Label
            {
                Name      = "kpiTitle",
                Text      = groupLabel,
                Font      = groupFont,
                ForeColor = UiTheme.TextMuted,
                AutoSize  = false
            };
            card.Controls.Add(lblTitle);

            Font valFont;
            try   { valFont = new Font("Segoe UI Semibold", 13f, FontStyle.Regular); }
            catch { valFont = UiTheme.FontTitle; }

            valLabel = new Label
            {
                Name      = "kpiValue",
                Text      = "--",
                Font      = valFont,
                ForeColor = UiTheme.TextPrimary,
                AutoSize  = false
            };
            card.Controls.Add(valLabel);

            subLabel = new Label
            {
                Name      = "kpiSub",
                Text      = "--",
                Font      = UiTheme.FontCaption,
                ForeColor = UiTheme.TextSecondary,
                AutoSize  = false
            };
            card.Controls.Add(subLabel);

            return card;
        }

        private static int LayoutKpiCard(RoundedPanel card, int width)
        {
            var icon  = card.Controls["kpiIcon"];
            var title = card.Controls["kpiTitle"];
            var value = card.Controls["kpiValue"];
            var sub   = card.Controls["kpiSub"];
            if (icon == null || title == null || value == null || sub == null) return 96;

            int textLeft = 46;
            int textW = Math.Max(80, width - textLeft - 12);
            int y = 10;
            int titleH = MeasureLine(title.Text, title.Font, textW);
            title.SetBounds(textLeft, y, textW, titleH);
            y = title.Bottom + 2;
            int valueH = MeasureLine(string.IsNullOrEmpty(value.Text) ? "--" : value.Text, value.Font, textW);
            value.SetBounds(textLeft, y, textW, valueH);
            y = value.Bottom + 2;
            int subH = MeasureLine(string.IsNullOrEmpty(sub.Text) ? "--" : sub.Text, sub.Font, textW);
            sub.SetBounds(textLeft, y, textW, subH);
            int h = sub.Bottom + 12;
            icon.SetBounds(10, 10, 28, Math.Max(28, h - 20));
            return h;
        }

        private static int MeasureLine(string text, Font font, int width)
        {
            var size = TextRenderer.MeasureText(text, font, new Size(Math.Max(40, width), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            return size.Height + 6;
        }

        // ── Toolbar (2-row, height 86) ────────────────────────────────────
        private Panel CreateToolbarPanel()
        {
            RoundedPanel pnl = new RoundedPanel
            {
                Dock         = DockStyle.Top,
                Height       = 92,
                CornerRadius = 6,
                BorderColor  = UiTheme.BorderLight,
                BackColor    = UiTheme.CardBg
            };

            // Row 1: filter buttons + search + combo
            btnFilterAll          = new RoundedButton { Text = "T\u1EA5t c\u1EA3" };
            btnFilterGoldenOpp    = new RoundedButton { Text = "\uD83C\uDFAF C\u01A1 h\u1ED9i v\u00E0ng" };
            btnFilterHasDelta     = new RoundedButton { Text = "\uD83D\uDCC8 C\u00F3 ph\u00E1t sinh" };
            btnFilterRescanned    = new RoundedButton { Text = "\uD83D\uDD04 SP qu\u00E9t l\u1EA1i" };
            btnFilterFirstScan    = new RoundedButton { Text = "\uD83C\uDD95 SP l\u1EA7n \u0111\u1EA7u" };
            btnFilterLowVote      = new RoundedButton { Text = "Vote th\u1EA5p" };
            btnFilterTopRated     = new RoundedButton { Text = "\u0110\u00E1nh gi\u00E1 cao" };
            btnFilterDisqualified = new RoundedButton { Text = "\u26D4 \u0110\u00E3 lo\u1EA1i" };

            btnFilterAll.Click          += (s, e) => SetFilterMode(FilterMode.All);
            btnFilterGoldenOpp.Click    += (s, e) => SetFilterMode(FilterMode.GoldenOpp);
            btnFilterHasDelta.Click     += (s, e) => SetFilterMode(FilterMode.HasDelta);
            btnFilterRescanned.Click    += (s, e) => SetFilterMode(FilterMode.Rescanned);
            btnFilterFirstScan.Click    += (s, e) => SetFilterMode(FilterMode.FirstScan);
            btnFilterLowVote.Click      += (s, e) => SetFilterMode(FilterMode.LowVoteHighSold);
            btnFilterTopRated.Click     += (s, e) => SetFilterMode(FilterMode.TopRated);
            btnFilterDisqualified.Click += (s, e) => SetFilterMode(FilterMode.Disqualified);

            cboParentCategory = new SuiteCombo { Width = 180 };
            cboParentCategory.SelectedIndexChanged += (s, e) => OnParentCategoryChanged();

            cboCategory = new SuiteCombo { Width = 180, Enabled = false };
            cboCategory.SelectedIndexChanged += (s, e) =>
            {
                if (!_isUpdatingCombos) ApplyFilter(resetPage: true);
            };

            // Row 2: action buttons (right-aligned)
            btnExportExcel = new RoundedButton
            {
                IconGlyph = "\uE9D9",   // Chart
                IconRole  = ButtonIconRole.Success,
                Text      = "Xuất file Excel"
            };
            btnExportExcel.Click += BtnExportExcel_Click;

            btnRefresh = new RoundedButton
            {
                IconGlyph = "\uE72C",   // Refresh
                IconRole  = ButtonIconRole.Info,
                Text      = "Làm mới"
            };
            btnRefresh.Click += (s, e) => ReloadData(preservePage: false);

            btnDelete = new RoundedButton
            {
                IconGlyph = "\uE74D",   // Delete
                IconRole  = ButtonIconRole.Danger,
                Text      = "Xóa đã chọn"
            };
            btnDelete.Click += BtnDelete_Click;

            btnClearAll = new RoundedButton
            {
                IconGlyph = "\uE74D",   // Delete
                IconRole  = ButtonIconRole.Muted,
                Text      = "Xóa tất cả"
            };
            btnClearAll.Click += BtnClearAll_Click;

            pnl.Controls.AddRange(new Control[]
            {
                btnFilterAll, btnFilterGoldenOpp, btnFilterHasDelta, btnFilterRescanned, btnFilterFirstScan, btnFilterLowVote, btnFilterTopRated, btnFilterDisqualified,
                cboParentCategory, cboCategory,
                btnExportExcel, btnRefresh, btnDelete, btnClearAll
            });

            void UpdateToolbarLayout()
            {
                int y1 = 12;
                int y2 = 50;
                int x = 12;
                foreach (var b in new[] { btnFilterAll, btnFilterGoldenOpp, btnFilterHasDelta, btnFilterRescanned, btnFilterFirstScan, btnFilterLowVote, btnFilterTopRated, btnFilterDisqualified })
                {
                    b.Location = new Point(x, y1);
                    x += b.Width + 6;
                }

                int right = pnl.Width - 12;
                btnClearAll.Location = new Point(right - btnClearAll.Width, y2);
                right = btnClearAll.Left - 8;
                btnDelete.Location = new Point(right - btnDelete.Width, y2);
                right = btnDelete.Left - 8;
                btnRefresh.Location = new Point(right - btnRefresh.Width, y2);
                right = btnRefresh.Left - 8;
                btnExportExcel.Location = new Point(right - btnExportExcel.Width, y2);

                int gap = 10;
                int avail = Math.Max(0, btnExportExcel.Left - 12 - gap);
                // 2 category comboboxes on the left: cboParentCategory & cboCategory
                int comboW = Math.Min(320, Math.Max(180, (avail - gap) / 2));
                cboParentCategory.SetBounds(12, y2, comboW, 30);
                cboCategory.SetBounds(12 + comboW + gap, y2, comboW, 30);
            }

            pnl.Resize                    += (s, e) => UpdateToolbarLayout();
            btnFilterAll.SizeChanged      += (s, e) => UpdateToolbarLayout();
            btnFilterGoldenOpp.SizeChanged += (s, e) => UpdateToolbarLayout();
            btnFilterHasDelta.SizeChanged += (s, e) => UpdateToolbarLayout();
            btnFilterRescanned.SizeChanged += (s, e) => UpdateToolbarLayout();
            btnFilterFirstScan.SizeChanged += (s, e) => UpdateToolbarLayout();
            btnFilterLowVote.SizeChanged   += (s, e) => UpdateToolbarLayout();
            btnFilterTopRated.SizeChanged  += (s, e) => UpdateToolbarLayout();
            btnFilterDisqualified.SizeChanged += (s, e) => UpdateToolbarLayout();
            btnExportExcel.SizeChanged     += (s, e) => UpdateToolbarLayout();
            btnRefresh.SizeChanged         += (s, e) => UpdateToolbarLayout();
            btnDelete.SizeChanged          += (s, e) => UpdateToolbarLayout();
            btnClearAll.SizeChanged        += (s, e) => UpdateToolbarLayout();

            UpdateToolbarLayout();
            HighlightFilterButton();
            return pnl;
        }

        // ── Grid columns ────────────────────────────────────────────────
        private void SetupGridColumns()
        {
            dgvProducts.Columns.Clear();

            dgvProducts.Columns.Add(new DataGridViewImageColumn
                { Name = "ColImage", HeaderText = "\u1EA2nh", Width = 50, ImageLayout = DataGridViewImageCellLayout.Zoom,
                  ToolTipText = "Bấm để xem ảnh đã tải về" });
            dgvProducts.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "ColScanType", HeaderText = "L\u1EA7n qu\u00E9t", DataPropertyName = "ScanTypeDisplay", Width = 95,
                  DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            dgvProducts.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "ColTag", HeaderText = "Khuy\u1EBFn ngh\u1ECB", DataPropertyName = "RecommendationTag", Width = 140,
                  DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            dgvProducts.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "ColScore", HeaderText = "\u0110i\u1EC3m", DataPropertyName = "FormattedScore", Width = 65,
                  DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            dgvProducts.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "ColName", HeaderText = "T\u00EAn s\u1EA3n ph\u1EA9m", DataPropertyName = "Name", Width = 380, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 280 });
            dgvProducts.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "ColCategory", HeaderText = "Danh m\u1EE5c ph\u00E2n c\u1EA5p", DataPropertyName = "CategoryHierarchy", Width = 300, MinimumWidth = 240 });
            dgvProducts.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "ColPrice", HeaderText = "Gi\u00E1 b\u00E1n", DataPropertyName = "FormattedPrice", Width = 110,
                  DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            dgvProducts.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "ColRating", HeaderText = "\u0110\u00E1nh gi\u00E1", DataPropertyName = "FormattedRating", Width = 95,
                  DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            dgvProducts.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "ColHistoricalSold", HeaderText = "\u0110\u00E3 b\u00E1n", DataPropertyName = "FormattedSold", Width = 95,
                  DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            dgvProducts.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "ColMonthlySold", HeaderText = "Th\u00E1ng", DataPropertyName = "FormattedMonthlySold", Width = 90,
                  DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            dgvProducts.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "ColSoldDelta", HeaderText = "Ph\u00E1t sinh", DataPropertyName = "FormattedSoldDelta", Width = 100,
                  DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleRight } });
            dgvProducts.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "ColLocation", HeaderText = "N\u01A1i b\u00E1n", DataPropertyName = "Location", Width = 110 });
            dgvProducts.Columns.Add(new DataGridViewLinkColumn
            {
                Name = "ColLink", HeaderText = "Shopee", Width = 80,
                Text = "M\u1EDF link", UseColumnTextForLinkValue = true,
                LinkColor = UiTheme.Accent,
                ActiveLinkColor = UiTheme.Accent,
                VisitedLinkColor = UiTheme.Accent,
                TrackVisitedState = false,
                LinkBehavior = LinkBehavior.HoverUnderline,
                DefaultCellStyle = { Alignment = DataGridViewContentAlignment.MiddleCenter, ForeColor = UiTheme.Accent, SelectionForeColor = UiTheme.Accent }
            });
            dgvProducts.Columns.Add(new DataGridViewTextBoxColumn
                { Name = "ColTime", HeaderText = "Th\u1EDDi gian", DataPropertyName = "FormattedScrapedAt", Width = 120 });

            foreach (DataGridViewColumn col in dgvProducts.Columns)
            {
                int textW = TextRenderer.MeasureText(col.HeaderText, UiTheme.FontBody).Width + 28;
                if (col.Width < textW) col.Width = textW;
            }

            dgvProducts.CellFormatting   += DgvProducts_CellFormatting;
            dgvProducts.CellContentClick += DgvProducts_CellContentClick;
            dgvProducts.CellClick        += DgvProducts_CellClick;
            dgvProducts.CellDoubleClick  += DgvProducts_CellDoubleClick;
            dgvProducts.CellMouseEnter   += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && dgvProducts.Columns[e.ColumnIndex].Name == "ColImage")
                    dgvProducts.Cursor = Cursors.Hand;
            };
            dgvProducts.CellMouseLeave   += (s, e) =>
            {
                if (dgvProducts.Cursor == Cursors.Hand)
                    dgvProducts.Cursor = Cursors.Default;
            };
        }

        private void DgvProducts_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dgvProducts.Rows.Count) return;
            var product = dgvProducts.Rows[e.RowIndex].DataBoundItem as ScrapedProduct;
            if (product == null) return;

            if (e.CellStyle != null && dgvProducts.Columns[e.ColumnIndex].Name == "ColScanType")
            {
                if (product.IsRescanned)
                {
                    e.CellStyle.ForeColor = Color.FromArgb(0, 120, 212);
                    e.CellStyle.Font = new Font(dgvProducts.Font, FontStyle.Bold);
                }
                else
                {
                    e.CellStyle.ForeColor = UiTheme.TextSecondary;
                }
            }

            if (e.CellStyle != null && dgvProducts.Columns[e.ColumnIndex].Name == "ColSoldDelta")
            {
                if (product.SoldDelta > 0)
                {
                    e.CellStyle.ForeColor = Color.FromArgb(16, 137, 62);
                    e.CellStyle.Font = new Font(dgvProducts.Font, FontStyle.Bold);
                }
                else
                {
                    e.CellStyle.ForeColor = UiTheme.TextMuted;
                }
            }

            if (e.CellStyle != null && dgvProducts.Columns[e.ColumnIndex].Name == "ColTag" && e.Value is string tag)
            {
                if (tag.Contains("C\u01A1 h\u1ED9i v\u00E0ng"))
                {
                    e.CellStyle.ForeColor = Color.FromArgb(16, 137, 62);
                    e.CellStyle.Font = new Font(dgvProducts.Font, FontStyle.Bold);
                }
                else if (tag.Contains("Hot Trend"))
                {
                    e.CellStyle.ForeColor = Color.FromArgb(0, 120, 212);
                    e.CellStyle.Font = new Font(dgvProducts.Font, FontStyle.Bold);
                }
                else if (tag.Contains("Lo\u1EA1i"))
                {
                    e.CellStyle.ForeColor = Color.FromArgb(209, 52, 56);
                }
                else if (tag.Contains("Ch\u1EDD theo d\u00F5i"))
                {
                    e.CellStyle.ForeColor = Color.FromArgb(202, 80, 16);
                }
            }

            if (dgvProducts.Columns[e.ColumnIndex].Name == "ColLink")
            {
                // Dòng đang chọn bị DataGridView tách khỏi hàng dùng chung, ô link mất chữ cột.
                e.Value = "M\u1EDF link";
                e.FormattingApplied = true;
                if (e.CellStyle != null)
                {
                    e.CellStyle.ForeColor = UiTheme.Accent;
                    e.CellStyle.SelectionForeColor = UiTheme.Accent;
                }
            }

            if (e.CellStyle != null && dgvProducts.Columns[e.ColumnIndex].Name == "ColCategory")
            {
                e.Value = product.CategoryHierarchy;
                e.FormattingApplied = true;
            }

            if (dgvProducts.Columns[e.ColumnIndex].Name == "ColImage")
            {
                if (!string.IsNullOrWhiteSpace(product.ImageUrl))
                {
                    var cached = ImageCache.GetCached(product.ImageUrl);
                    if (cached != null)
                        e.Value = cached;
                    else
                    {
                        e.Value = ImageCache.GetPlaceholder();
                        ImageCache.LoadThumbnailAsync(product.ImageUrl, product.ItemId, dgvProducts, () =>
                        {
                            if (dgvProducts.IsHandleCreated && !dgvProducts.IsDisposed)
                            {
                                if (e.RowIndex < dgvProducts.Rows.Count &&
                                    dgvProducts.Rows[e.RowIndex].DataBoundItem == product)
                                {
                                    dgvProducts.InvalidateCell(e.ColumnIndex, e.RowIndex);
                                }
                            }
                        });
                    }
                }
                else
                    e.Value = ImageCache.GetPlaceholder();
                e.FormattingApplied = true;
            }
        }

        private bool _isShowingImage = false;
        private DateTime _lastImageShownAt = DateTime.MinValue;

        private void DgvProducts_CellContentClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dgvProducts.Rows.Count) return;
            if (dgvProducts.Rows[e.RowIndex].DataBoundItem is not ScrapedProduct product) return;

            if (dgvProducts.Columns[e.ColumnIndex].Name == "ColLink")
                OpenProductUrl(product);
        }

        private void DgvProducts_CellClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dgvProducts.Rows.Count) return;
            if (dgvProducts.Columns[e.ColumnIndex].Name == "ColImage")
            {
                if (dgvProducts.Rows[e.RowIndex].DataBoundItem is ScrapedProduct product)
                    ShowProductImage(product);
            }
        }

        private void DgvProducts_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= dgvProducts.Rows.Count) return;
            if (dgvProducts.Rows[e.RowIndex].DataBoundItem is not ScrapedProduct product) return;

            if (dgvProducts.Columns[e.ColumnIndex].Name != "ColImage")
                OpenProductUrl(product);
        }

        private void ShowProductImage(ScrapedProduct product)
        {
            if (_isShowingImage || (DateTime.UtcNow - _lastImageShownAt).TotalMilliseconds < 400)
                return;

            _isShowingImage = true;
            _lastImageShownAt = DateTime.UtcNow;

            try
            {
                using var dlg = new ProductImageViewerDialog(product, this.FindForm());
                dlg.ShowDialog(this.FindForm());
            }
            catch (Exception ex)
            {
                SuiteDialog.Alert(this, "Lỗi", $"Không thể hiển thị ảnh: {ex.Message}");
            }
            finally
            {
                _isShowingImage = false;
                _lastImageShownAt = DateTime.UtcNow;
            }
        }

        private void OpenProductUrl(ScrapedProduct product)
        {
            string url = product.ProductUrl;
            if (string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(product.ItemId) &&
                !string.IsNullOrWhiteSpace(product.ShopId))
                url = $"https://shopee.vn/product/{product.ShopId}/{product.ItemId}";

            if (!string.IsNullOrWhiteSpace(url))
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex)
                { SuiteDialog.Alert(this, "L\u1ED7i", $"Kh\u00F4ng th\u1EC3 m\u1EDF tr\u00ECnh duy\u1EC7t: {ex.Message}"); }
            }
        }

        // ── Data loading ──────────────────────────────────────────────────
        public void ReloadData(bool preservePage = false)
        {
            allProducts = GlobalState.ScrapedProducts.ToList();

            string currentSelectedParent = cboParentCategory.SelectedItem?.ToString() ?? "Tất cả ngành hàng (Cấp 1)";

            // Chỉ lấy danh mục cấp 1 được nhập vào từ Main Form (GlobalState.Categories)
            var parentNames = GlobalState.Categories
                .Select(c => c.Name.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();

            bool hasUncategorized = allProducts.Any(p => GetParentCategoryName(p.CategoryUrl, p.CategoryPath) == "Chưa phân loại");

            _isUpdatingCombos = true;
            try
            {
                cboParentCategory.BeginUpdate();
                cboParentCategory.Items.Clear();
                cboParentCategory.Items.Add("Tất cả ngành hàng (Cấp 1)");
                foreach (var pName in parentNames) cboParentCategory.Items.Add(pName);
                if (hasUncategorized) cboParentCategory.Items.Add("Chưa phân loại");

                if (cboParentCategory.Items.Contains(currentSelectedParent))
                    cboParentCategory.SelectedItem = currentSelectedParent;
                else
                    cboParentCategory.SelectedIndex = 0;
            }
            finally
            {
                cboParentCategory.EndUpdate();
                _isUpdatingCombos = false;
            }

            PopulateSubCategories();
            ApplyFilter(resetPage: !preservePage);
        }

        public void ReloadData() => ReloadData(preservePage: false);

        private void OnParentCategoryChanged()
        {
            if (_isUpdatingCombos) return;
            PopulateSubCategories();
            ApplyFilter(resetPage: true);
        }

        private void PopulateSubCategories()
        {
            string selectedParent = cboParentCategory.SelectedItem?.ToString() ?? "Tất cả ngành hàng (Cấp 1)";
            string currentSelectedSub = cboCategory.SelectedItem?.ToString() ?? "Tất cả danh mục con";

            bool hasParent = selectedParent != "Tất cả ngành hàng (Cấp 1)" && !string.IsNullOrWhiteSpace(selectedParent);

            _isUpdatingCombos = true;
            try
            {
                cboCategory.BeginUpdate();
                cboCategory.Items.Clear();
                cboCategory.Items.Add("Tất cả danh mục con");

                if (!hasParent)
                {
                    cboCategory.SelectedIndex = 0;
                    cboCategory.Enabled = false;
                }
                else
                {
                    cboCategory.Enabled = true;

                    var filteredForSubs = allProducts.AsEnumerable()
                        .Where(p => GetParentCategoryName(p.CategoryUrl, p.CategoryPath) == selectedParent);

                    var subCategories = filteredForSubs
                        .Select(p => !string.IsNullOrWhiteSpace(p.CategoryPath) ? p.CategoryPath : "Chưa phân loại")
                        .Distinct()
                        .OrderBy(c => c)
                        .ToList();

                    foreach (var sub in subCategories) cboCategory.Items.Add(sub);

                    if (cboCategory.Items.Contains(currentSelectedSub))
                        cboCategory.SelectedItem = currentSelectedSub;
                    else
                        cboCategory.SelectedIndex = 0;
                }
            }
            finally
            {
                cboCategory.EndUpdate();
                _isUpdatingCombos = false;
            }
        }

        public static string GetParentCategoryName(string categoryUrl, string categoryPath = "")
        {
            if (string.IsNullOrWhiteSpace(categoryUrl) && string.IsNullOrWhiteSpace(categoryPath))
                return "Chưa phân loại";

            string cacheKey = string.IsNullOrWhiteSpace(categoryPath) ? categoryUrl : $"{categoryUrl}|{categoryPath}";
            if (ParentCategoryCache.TryGetValue(cacheKey, out var cached))
                return cached;

            string result = ResolveParentCategoryName(categoryUrl, categoryPath);
            ParentCategoryCache[cacheKey] = result;
            return result;
        }

        private static string ResolveParentCategoryName(string categoryUrl, string categoryPath)
        {
            if (GlobalState.Categories == null || GlobalState.Categories.Count == 0)
                return "Chưa phân loại";

            // 1. Khớp trực tiếp URL danh mục được nhập từ Main Form
            if (!string.IsNullOrWhiteSpace(categoryUrl))
            {
                string normUrl = categoryUrl.Trim().TrimEnd('/');
                var directCat = GlobalState.Categories.FirstOrDefault(c =>
                    !string.IsNullOrWhiteSpace(c.Url) &&
                    (string.Equals(c.Url.Trim().TrimEnd('/'), normUrl, StringComparison.OrdinalIgnoreCase) ||
                     normUrl.StartsWith(c.Url.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase)));
                if (directCat != null)
                    return directCat.Name;
            }

            // 2. Khớp theo Shopee Root Category ID (-cat.xxxxxx)
            if (!string.IsNullOrWhiteSpace(categoryUrl))
            {
                string rootId = ExtractShopeeRootCatId(categoryUrl);
                if (!string.IsNullOrEmpty(rootId))
                {
                    var idCat = GlobalState.Categories.FirstOrDefault(c =>
                        !string.IsNullOrWhiteSpace(c.Url) &&
                        ExtractShopeeRootCatId(c.Url) == rootId);
                    if (idCat != null)
                        return idCat.Name;
                }
            }

            // 3. Khớp theo phần tử đầu tiên của chuỗi phân cấp CategoryPath ("Thời Trang Nam > Áo Polo...")
            if (!string.IsNullOrWhiteSpace(categoryPath))
            {
                string[] parts = categoryPath.Split(new[] { '>', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    string rootPathName = parts[0].Trim();
                    var pathCat = GlobalState.Categories.FirstOrDefault(c =>
                        string.Equals(c.Name.Trim(), rootPathName, StringComparison.OrdinalIgnoreCase));
                    if (pathCat != null)
                        return pathCat.Name;
                }
            }

            // 4. Nếu không thuộc bất kỳ danh mục cấp 1 nào nhập từ Main Form -> xếp vào "Chưa phân loại"
            return "Chưa phân loại";
        }

        private static string ExtractShopeeRootCatId(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return string.Empty;
            int idx = url.IndexOf("-cat.", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return string.Empty;

            string after = url.Substring(idx + 5);
            int stopIdx = after.IndexOfAny(new[] { '?', '#', '/', '&' });
            if (stopIdx >= 0) after = after.Substring(0, stopIdx);

            string[] dots = after.Split('.');
            return dots.Length > 0 ? dots[0].Trim() : string.Empty;
        }

        private void UpdateKpiStats(IList<ScrapedProduct>? targetList = null)
        {
            var list = targetList ?? filteredProducts ?? allProducts;
            int total = list.Count;
            lblTotalCountVal.Text = $"{total:N0} sp";

            if (total == 0)
            {
                lblTotalCountSub.Text = allProducts.Count > 0 ? $"0 / {allProducts.Count:N0} sp" : "Từ 0 danh mục quét";
                lblTopMonthlyVal.Text = "0 sp";
                lblTopMonthlySub.Text = "Chưa có biến động";
                lblLowVoteVal.Text = "0 sp";
                lblLowVoteSub.Text = "Quét lại: 0 | Lần đầu: 0";
                lblAvgPriceVal.Text = "0 ₫";
                lblAvgPriceSub.Text = "Cao nhất: 0 ₫";
                relayoutKpis?.Invoke();
                return;
            }

            var catPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int hasDeltaCount = 0;
            long maxDelta = 0;
            int rescannedCount = 0;
            decimal totalPrice = 0;
            decimal maxPrice = 0;

            for (int i = 0; i < total; i++)
            {
                var p = list[i];
                if (!string.IsNullOrWhiteSpace(p.CategoryPath)) catPaths.Add(p.CategoryPath);
                if (p.SoldDelta > 0)
                {
                    hasDeltaCount++;
                    if (p.SoldDelta > maxDelta) maxDelta = p.SoldDelta;
                }
                if (p.IsRescanned) rescannedCount++;
                totalPrice += p.Price;
                if (p.Price > maxPrice) maxPrice = p.Price;
            }

            if (total < allProducts.Count)
                lblTotalCountSub.Text = $"Từ {catPaths.Count} danh mục (Kho: {allProducts.Count:N0} sp)";
            else
                lblTotalCountSub.Text = $"Từ {catPaths.Count} danh mục quét";

            lblTopMonthlyVal.Text = $"{hasDeltaCount:N0} sp";
            lblTopMonthlySub.Text = maxDelta > 0 ? $"Phát sinh cao nhất: +{maxDelta:N0} sp" : "Chưa có biến động";

            lblLowVoteVal.Text = $"{rescannedCount:N0} sp";
            lblLowVoteSub.Text = $"Quét lại: {rescannedCount:N0} | Lần đầu: {total - rescannedCount:N0}";

            decimal avgPrice = totalPrice / total;
            lblAvgPriceVal.Text = avgPrice > 0 ? $"{avgPrice:N0} ₫" : "0 ₫";
            lblAvgPriceSub.Text = $"Cao nhất: {maxPrice:N0} ₫";
            relayoutKpis?.Invoke();
        }

        // ── Filtering ─────────────────────────────────────────────────────
        private void SetFilterMode(FilterMode mode)
        {
            currentFilter = mode;
            HighlightFilterButton();
            ApplyFilter(resetPage: true);
        }

        private void HighlightFilterButton()
        {
            btnFilterAll.Active          = currentFilter == FilterMode.All;
            btnFilterGoldenOpp.Active    = currentFilter == FilterMode.GoldenOpp;
            btnFilterHasDelta.Active     = currentFilter == FilterMode.HasDelta;
            btnFilterRescanned.Active    = currentFilter == FilterMode.Rescanned;
            btnFilterFirstScan.Active    = currentFilter == FilterMode.FirstScan;
            btnFilterLowVote.Active      = currentFilter == FilterMode.LowVoteHighSold;
            btnFilterTopRated.Active     = currentFilter == FilterMode.TopRated;
            btnFilterDisqualified.Active = currentFilter == FilterMode.Disqualified;

            btnFilterAll.Invalidate();
            btnFilterGoldenOpp.Invalidate();
            btnFilterHasDelta.Invalidate();
            btnFilterRescanned.Invalidate();
            btnFilterFirstScan.Invalidate();
            btnFilterLowVote.Invalidate();
            btnFilterTopRated.Invalidate();
            btnFilterDisqualified.Invalidate();
        }

        private void ApplyFilter(bool resetPage = true)
        {
            var query = allProducts.AsEnumerable();

            switch (currentFilter)
            {
                case FilterMode.GoldenOpp:
                    query = query.Where(p => p.RecommendationTag.Contains("C\u01A1 h\u1ED9i v\u00E0ng"))
                                 .OrderByDescending(p => p.OpportunityScore)
                                 .ThenByDescending(p => p.DailyVelocity);
                    break;
                case FilterMode.Disqualified:
                    query = query.Where(p => p.RecommendationTag.Contains("Lo\u1EA1i"))
                                 .OrderBy(p => p.OpportunityScore);
                    break;
                case FilterMode.HasDelta:
                    query = query.Where(p => p.SoldDelta > 0)
                                 .OrderByDescending(p => p.SoldDelta);
                    break;
                case FilterMode.Rescanned:
                    query = query.Where(p => p.IsRescanned)
                                 .OrderByDescending(p => p.ScanCount)
                                 .ThenByDescending(p => p.SoldDelta);
                    break;
                case FilterMode.FirstScan:
                    query = query.Where(p => !p.IsRescanned)
                                 .OrderByDescending(p => p.HistoricalSold);
                    break;
                case FilterMode.LowVoteHighSold:
                    query = query.Where(p => (p.HistoricalSold >= 50 || p.MonthlySold >= 10) && p.Rating > 0 && p.Rating < 4.5)
                                 .OrderByDescending(p => p.HistoricalSold);
                    break;
                case FilterMode.TopRated:
                    query = query.Where(p => p.Rating >= 4.8)
                                 .OrderByDescending(p => p.Rating)
                                 .ThenByDescending(p => p.HistoricalSold);
                    break;
                default:
                    query = query.OrderByDescending(p => p.ScrapedAt);
                    break;
            }

            string selectedParent = cboParentCategory.SelectedItem?.ToString() ?? "Tất cả ngành hàng (Cấp 1)";
            if (selectedParent != "Tất cả ngành hàng (Cấp 1)")
            {
                query = query.Where(p => GetParentCategoryName(p.CategoryUrl, p.CategoryPath) == selectedParent);
            }

            string selectedCat = cboCategory.SelectedItem?.ToString() ?? "Tất cả danh mục con";
            if (cboCategory.Enabled && selectedCat != "Tất cả danh mục con" && selectedCat != "Tất cả danh mục")
            {
                query = query.Where(p =>
                    (string.IsNullOrWhiteSpace(p.CategoryPath) ? "Chưa phân loại" : p.CategoryPath) == selectedCat ||
                    p.CategoryHierarchy == selectedCat);
            }
            filteredProducts = query.ToList();
            filteredTurnover = filteredProducts.Sum(p => p.Price * p.HistoricalSold);

            UpdateKpiStats(filteredProducts);

            if (resetPage) currentPage = 1;
            RenderPage();
        }

        private void ApplyFilter() => ApplyFilter(resetPage: true);

        // ── Pagination logic ──────────────────────────────────────────────
        private void GoToPage(int page)
        {
            if (totalPages <= 0) totalPages = 1;
            int target = Math.Max(1, Math.Min(totalPages, page));
            if (target != currentPage || dgvProducts.DataSource != currentPageProducts)
            {
                currentPage = target;
                RenderPage();
            }
            else
            {
                txtCurrentPage.Text = currentPage.ToString();
            }
        }

        private void JumpToPage(string text)
        {
            if (int.TryParse(text.Trim(), out int page))
            {
                GoToPage(page);
            }
            else
            {
                txtCurrentPage.Text = currentPage.ToString();
            }
        }

        private void RenderPage()
        {
            totalPages = Math.Max(1, (int)Math.Ceiling((double)filteredProducts.Count / pageSize));
            if (currentPage > totalPages) currentPage = totalPages;
            if (currentPage < 1) currentPage = 1;

            if (filteredProducts.Count == 0)
            {
                currentPageProducts = new List<ScrapedProduct>();
                dgvProducts.DataSource = currentPageProducts;
                txtCurrentPage.Text = "1";
                lblTotalPages.Text = "/ 1";
                btnFirstPage.Enabled = false;
                btnPrevPage.Enabled = false;
                btnNextPage.Enabled = false;
                btnLastPage.Enabled = false;
                lblGridStatus.Text = "Không có sản phẩm nào phù hợp với bộ lọc";
                return;
            }

            int startIndex = (currentPage - 1) * pageSize;
            currentPageProducts = filteredProducts.Skip(startIndex).Take(pageSize).ToList();
            dgvProducts.DataSource = currentPageProducts;

            txtCurrentPage.Text = currentPage.ToString();
            lblTotalPages.Text = $"/ {totalPages:N0}";

            btnFirstPage.Enabled = currentPage > 1;
            btnPrevPage.Enabled = currentPage > 1;
            btnNextPage.Enabled = currentPage < totalPages;
            btnLastPage.Enabled = currentPage < totalPages;

            int fromItem = startIndex + 1;
            int toItem = startIndex + currentPageProducts.Count;

            lblGridStatus.Text = $"Hiển thị: {fromItem:N0} - {toItem:N0} / {filteredProducts.Count:N0} sản phẩm (Trang {currentPage:N0}/{totalPages:N0})" +
                                 $"   |   Ước tính doanh số bộ lọc: {filteredTurnover:N0} ₫";
        }

        // ── Action handlers ───────────────────────────────────────────────
        private void BtnExportExcel_Click(object? sender, EventArgs e)
        {
            if (filteredProducts.Count == 0)
            {
                SuiteDialog.Alert(this, "Th\u00F4ng b\u00E1o", "Kh\u00F4ng c\u00F3 d\u1EEF li\u1EC7u s\u1EA3n ph\u1EA9m n\u00E0o \u0111\u1EC3 xu\u1EA5t Excel.");
                return;
            }

            using var sfd = new SaveFileDialog
            {
                Filter   = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"Shopee_Products_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                Title    = "Ch\u1ECDn n\u01A1i l\u01B0u file Excel b\u00E1o c\u00E1o s\u1EA3n ph\u1EA9m"
            };

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    ExcelExportService.ExportProductsToExcel(sfd.FileName, filteredProducts);
                    if (SuiteDialog.Confirm(this,
                        "Xu\u1EA5t file th\u00E0nh c\u00F4ng",
                        $"Xu\u1EA5t th\u00E0nh c\u00F4ng {filteredProducts.Count:N0} s\u1EA3n ph\u1EA9m ra file:\n{sfd.FileName}\n\nB\u1EA1n c\u00F3 mu\u1ED1n m\u1EDF file ngay kh\u00F4ng?",
                        "M\u1EDF file", DialogTone.Primary) == DialogResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(sfd.FileName) { UseShellExecute = true });
                    }
                }
                catch (Exception ex)
                {
                    SuiteDialog.Alert(this, "L\u1ED7i", $"C\u00F3 l\u1ED7i khi xu\u1EA5t file Excel: {ex.Message}");
                }
            }
        }

        private void BtnDelete_Click(object? sender, EventArgs e)
        {
            if (dgvProducts.SelectedRows.Count == 0)
            {
                SuiteDialog.Alert(this, "Th\u00F4ng b\u00E1o", "Vui l\u00F2ng ch\u1ECDn \u00EDt nh\u1EA5t m\u1ED9t d\u00F2ng s\u1EA3n ph\u1EA9m \u0111\u1EC3 x\u00F3a.");
                return;
            }

            if (SuiteDialog.Confirm(this, "X\u00E1c nh\u1EADn x\u00F3a",
                $"B\u1EA1n c\u00F3 ch\u1EAFc ch\u1EAFn mu\u1ED1n x\u00F3a {dgvProducts.SelectedRows.Count} s\u1EA3n ph\u1EA9m \u0111\u00E3 ch\u1ECDn kh\u00F4ng?",
                "X\u00F3a", DialogTone.Danger) != DialogResult.Yes) return;

            var idsToDelete = new List<string>();
            foreach (DataGridViewRow row in dgvProducts.SelectedRows)
            {
                if (row.DataBoundItem is ScrapedProduct p)
                {
                    idsToDelete.Add(p.Id);
                    GlobalState.ScrapedProducts.Remove(p);
                }
            }
            DataManager.DeleteScrapedProducts(idsToDelete);
            ReloadData(preservePage: true);
        }

        private void BtnClearAll_Click(object? sender, EventArgs e)
        {
            if (allProducts.Count == 0) return;

            if (SuiteDialog.Confirm(this, "X\u00E1c nh\u1EADn x\u00F3a t\u1EA5t c\u1EA3",
                "B\u1EA1n c\u00F3 ch\u1EAFc ch\u1EAFn mu\u1ED1n x\u00F3a TO\u00C0N B\u1ED8 d\u1EEF li\u1EC7u s\u1EA3n ph\u1EA9m \u0111\u00E3 qu\u00E9t kh\u00F4ng? Thao t\u00E1c n\u00E0y kh\u00F4ng th\u1EC3 ho\u00E0n t\u00E1c!",
                "X\u00F3a", DialogTone.Danger) != DialogResult.Yes) return;

            DataManager.ClearAllScrapedProducts();
            GlobalState.ScrapedProducts.Clear();
            currentPage = 1;
            ReloadData(preservePage: false);
        }
    }
}
