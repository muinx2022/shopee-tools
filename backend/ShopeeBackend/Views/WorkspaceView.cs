using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ShopeeBackend.Models;
using ShopeeBackend.Services;

namespace ShopeeBackend.Views
{
    public partial class WorkspaceView : UserControl
    {
        private MainForm? _mainForm;
        private DataGridView dgvCategories = null!;
        private TextBox txtLogs            = null!;   // single allowed TextBox in Views
        private RoundedButton btnRun          = null!;
        private RoundedButton btnRunSelected  = null!;
        private RoundedButton btnStopSelected = null!;
        private RoundedButton btnStop         = null!;
        private RoundedButton btnViewData     = null!;
        private BraveAutomation _automation;
        private volatile bool _isStopRequested = false;
        private volatile bool _isRunning = false;
        private Action? _spawnAdditionalWorkersAction;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SuiteSearchSession> _activeCatSessions = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _manuallyStoppedCats = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _currentExcludedUrls = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _activeTargetUrls = new();

        private int _activeTab = 0; // 0: Category, 1: Keyword
        private RoundedButton btnModeCategory = null!;
        private RoundedButton btnModeKeyword  = null!;
        private Label? lblPageSubtitle;
        private RoundedPanel pnlCatCard       = null!;
        private RoundedPanel pnlKeywordCard   = null!;
        private SuiteTextArea txtKeywordsInput = null!;
        private DataGridView dgvKeywords      = null!;

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SuiteSearchSession> _activeKeywordSessions = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _manuallyStoppedKws = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _currentExcludedKws = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _activeTargetKws = new();
        private Action? _spawnAdditionalKeywordWorkersAction;

        public WorkspaceView(MainForm? mainForm = null)
        {
            _mainForm   = mainForm;
            this.Dock   = DockStyle.Fill;
            this.BackColor = UiTheme.WindowBg;
            this.Font   = UiTheme.FontBody;
            _automation = new BraveAutomation();

            InitializeLayout();
            UpdateStats();
            GlobalState.Accounts.ListChanged        += (s, e) => UpdateStats();
            GlobalState.Categories.ListChanged      += (s, e) => UpdateStats();
            GlobalState.Keywords.ListChanged        += (s, e) => UpdateStats();
            GlobalState.ScrapedProducts.ListChanged += (s, e) => UpdateStats();
        }

        private void InitializeLayout()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.Size          = new Size(1140, 740);

            // Page header
            Panel pnlHead = UiTheme.CreatePageHead(
                "Automation Workspace",
                "Qu\u1EA3n l\u00FD danh m\u1EE5c qu\u00E9t, lu\u00E2n phi\u00EAn t\u00E0i kho\u1EA3n v\u00E0 theo d\u00F5i ti\u1EBFn tr\u00ECnh c\u00E0o d\u1EEF li\u1EC7u.");
            lblPageSubtitle = pnlHead.Controls.OfType<Label>().LastOrDefault();
            this.Controls.Add(pnlHead);

            // Sub-tabs switcher (Search theo danh mục vs Search theo từ khóa)
            Panel pnlModeTabs = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 38,
                BackColor = UiTheme.WindowBg,
                Padding   = new Padding(16, 2, 16, 6)
            };

            btnModeCategory = new RoundedButton
            {
                Text      = "Search theo danh m\u1EE5c",
                IconGlyph = "\uE8B7", // Folder
                IconRole  = ButtonIconRole.Primary,
                Active    = true,
                Height    = 30
            };
            btnModeCategory.Click += (s, e) => SelectTab(0);
            pnlModeTabs.Controls.Add(btnModeCategory);

            btnModeKeyword = new RoundedButton
            {
                Text      = "Search theo t\u1EEB kh\u00F3a",
                IconGlyph = "\uE721", // Search
                IconRole  = ButtonIconRole.Primary,
                Active    = false,
                Height    = 30
            };
            btnModeKeyword.Click += (s, e) => SelectTab(1);
            pnlModeTabs.Controls.Add(btnModeKeyword);

            void UpdateModeTabsLayout()
            {
                btnModeCategory.Location = new Point(16, 3);
                btnModeKeyword.Location  = new Point(btnModeCategory.Right + 8, 3);
            }
            pnlModeTabs.Resize          += (s, e) => UpdateModeTabsLayout();
            btnModeCategory.SizeChanged += (s, e) => UpdateModeTabsLayout();
            btnModeKeyword.SizeChanged  += (s, e) => UpdateModeTabsLayout();
            UpdateModeTabsLayout();

            this.Controls.Add(pnlModeTabs);
            pnlModeTabs.BringToFront();

            // Body
            Panel bodyPanel = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = UiTheme.WindowBg,
                Padding   = new Padding(16, 12, 16, 16)
            };
            this.Controls.Add(bodyPanel);
            bodyPanel.BringToFront();

            // Log card (Bottom, compact height 220)
            RoundedPanel pnlLogCard = new RoundedPanel
            {
                Dock        = DockStyle.Bottom,
                Height      = 220,
                BackColor   = UiTheme.LogBg,
                BorderColor = UiTheme.BorderDefault,
                CornerRadius = 6,
                Padding     = new Padding(12)
            };

            // The ONE allowed new TextBox in Views — dark log area
            txtLogs = new TextBox
            {
                Dock        = DockStyle.Fill,
                Multiline   = true,
                ReadOnly    = true,
                ScrollBars  = ScrollBars.Vertical,
                BackColor   = UiTheme.LogBg,
                ForeColor   = UiTheme.LogFg,
                Font        = UiTheme.FontConsole,
                BorderStyle = BorderStyle.None
            };
            pnlLogCard.Controls.Add(txtLogs);

            // Separator below action bar
            Panel pnlSep2 = new Panel { Dock = DockStyle.Bottom, Height = 10, BackColor = UiTheme.WindowBg };

            // Action bar (height 38)
            Panel pnlAction = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 38,
                BackColor = UiTheme.WindowBg
            };

            btnRun = new RoundedButton
            {
                IconGlyph = "\uE768",  // Play
                IconRole  = ButtonIconRole.Success,
                Text      = "Ch\u1EA1y t\u1EA5t c\u1EA3"
            };
            btnRun.Location = new Point(0, 4);
            btnRun.Click   += BtnRun_Click;
            pnlAction.Controls.Add(btnRun);

            btnRunSelected = new RoundedButton
            {
                IconGlyph = "\uE768",  // Play
                IconRole  = ButtonIconRole.Success,
                Text      = "Ch\u1EA1y \u0111\u00E3 ch\u1ECDn"
            };
            btnRunSelected.Click += BtnRunSelected_Click;
            pnlAction.Controls.Add(btnRunSelected);

            btnStopSelected = new RoundedButton
            {
                IconGlyph = "\uE71A",  // Stop
                IconColor = UiTheme.Warning,
                Text      = "D\u1EEBng \u0111\u00E3 ch\u1ECDn",
                Enabled   = false
            };
            btnStopSelected.Click += BtnStopSelected_Click;
            pnlAction.Controls.Add(btnStopSelected);

            btnStop = new RoundedButton
            {
                IconGlyph = "\uE71A",  // Stop
                IconRole  = ButtonIconRole.Danger,
                Text      = "D\u1EEBng t\u1EA5t c\u1EA3",
                Enabled   = false
            };
            btnStop.Click += BtnStop_Click;
            pnlAction.Controls.Add(btnStop);

            btnViewData = new RoundedButton
            {
                IconGlyph = "\uE7B8",  // Package
                IconRole  = ButtonIconRole.Primary,
                Text      = "Xem d\u1EEF li\u1EC7u \u0111\u00E3 qu\u00E9t"
            };
            btnViewData.Click += (s, e) => _mainForm?.NavigateToProducts();
            pnlAction.Controls.Add(btnViewData);

            RoundedButton btnClearLogs = new RoundedButton
            {
                IconGlyph = "\uE74D",  // Delete/clear
                IconRole  = ButtonIconRole.Muted,
                Text      = "X\u00F3a nh\u1EADt k\u00FD"
            };
            btnClearLogs.Click += (s, e) => txtLogs.Clear();
            pnlAction.Controls.Add(btnClearLogs);

            Label lblFlows = new Label
            {
                Text         = "S\u1ED1 flow:",
                AutoSize     = true,
                TextAlign    = ContentAlignment.MiddleLeft,
                ForeColor    = UiTheme.TextSecondary,
                Font         = UiTheme.FontBody
            };
            pnlAction.Controls.Add(lblFlows);

            NumericUpDown numFlows = new NumericUpDown
            {
                Minimum      = 1,
                Maximum      = 20,
                Value        = Math.Max(1, Math.Min(20, GlobalState.MaxConcurrentFlows)),
                Width        = 48,
                Font         = UiTheme.FontBody
            };
            numFlows.ValueChanged += (s, e) =>
            {
                int val = (int)numFlows.Value;
                GlobalState.MaxConcurrentFlows = val;
                DataManager.SetMaxConcurrentFlows(val);
            };
            pnlAction.Controls.Add(numFlows);

            void UpdateActionLayout()
            {
                btnRun.Location          = new Point(0, 4);
                btnRunSelected.Location  = new Point(btnRun.Right + 8, 4);
                btnStopSelected.Location = new Point(btnRunSelected.Right + 8, 4);
                btnStop.Location         = new Point(btnStopSelected.Right + 8, 4);
                btnViewData.Location     = new Point(btnStop.Right + 12, 4);
                lblFlows.Location        = new Point(btnViewData.Right + 16, 10);
                numFlows.Location        = new Point(lblFlows.Right + 6, 7);
                btnClearLogs.Location    = new Point(Math.Max(numFlows.Right + 12, pnlAction.Width - btnClearLogs.Width - 4), 4);
                btnClearLogs.BringToFront();
            }
            pnlAction.Resize              += (s, e) => UpdateActionLayout();
            btnRun.SizeChanged            += (s, e) => UpdateActionLayout();
            btnRunSelected.SizeChanged    += (s, e) => UpdateActionLayout();
            btnStopSelected.SizeChanged   += (s, e) => UpdateActionLayout();
            btnStop.SizeChanged           += (s, e) => UpdateActionLayout();
            btnViewData.SizeChanged       += (s, e) => UpdateActionLayout();
            btnClearLogs.SizeChanged      += (s, e) => UpdateActionLayout();
            numFlows.SizeChanged          += (s, e) => UpdateActionLayout();
            UpdateActionLayout();

            // Separator above action bar
            Panel pnlSep1 = new Panel { Dock = DockStyle.Bottom, Height = 10, BackColor = UiTheme.WindowBg };

            // Content container panel (fills area above action bar)
            Panel pnlContent = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = UiTheme.WindowBg
            };

            // Categories card (Fill)
            pnlCatCard = new RoundedPanel
            {
                Dock         = DockStyle.Fill,
                CornerRadius = 6,
                BorderColor  = UiTheme.BorderLight,
                Padding      = new Padding(14),
                Visible      = true
            };

            Panel pnlCatHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 42,
                BackColor = UiTheme.CardBg
            };

            pnlCatHeader.Controls.Add(new Label
            {
                Text      = "Danh m\u1EE5c Shopee c\u1EA7n qu\u00E9t",
                Font      = UiTheme.FontSection,
                ForeColor = UiTheme.TextPrimary,
                Location  = new Point(0, 8),
                AutoSize  = true
            });

            RoundedButton btnDeleteCat = new RoundedButton
            {
                IconGlyph = "\uE74D",  // Delete
                IconRole  = ButtonIconRole.Danger,
                Text      = "X\u00F3a \u0111\u00E3 ch\u1ECDn"
            };
            btnDeleteCat.Click += BtnDeleteCat_Click;
            pnlCatHeader.Controls.Add(btnDeleteCat);

            RoundedButton btnResetCat = new RoundedButton
            {
                IconGlyph = "\uE72C",  // Refresh / Reset
                IconRole  = ButtonIconRole.Muted,
                Text      = "\u0110\u1EB7t l\u1EA1i ti\u1EBFn \u0111\u1ED9"
            };
            btnResetCat.Click += BtnResetCat_Click;
            pnlCatHeader.Controls.Add(btnResetCat);

            RoundedButton btnAddCat = new RoundedButton
            {
                IconGlyph = "\uE710",  // Add
                IconRole  = ButtonIconRole.Info,
                Text      = "Th\u00EAm danh m\u1EE5c"
            };
            btnAddCat.Click += (s, e) => GlobalState.Categories.Add(
                new CategoryInfo { Name = "Danh m\u1EE5c m\u1EDBi", Url = "https://shopee.vn/" });
            pnlCatHeader.Controls.Add(btnAddCat);

            void UpdateCatButtons()
            {
                btnDeleteCat.Location = new Point(pnlCatHeader.Width - btnDeleteCat.Width - 10, 5);
                btnResetCat.Location  = new Point(btnDeleteCat.Left - btnResetCat.Width - 8, 5);
                btnAddCat.Location    = new Point(btnResetCat.Left - btnAddCat.Width - 8, 5);
            }
            pnlCatHeader.Resize       += (s, e) => UpdateCatButtons();
            btnDeleteCat.SizeChanged  += (s, e) => UpdateCatButtons();
            btnResetCat.SizeChanged   += (s, e) => UpdateCatButtons();
            btnAddCat.SizeChanged     += (s, e) => UpdateCatButtons();
            UpdateCatButtons();

            dgvCategories = new DataGridView
            {
                Dock               = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                DataSource         = GlobalState.Categories
            };
            UiTheme.StyleSuiteDataGrid(dgvCategories);
            dgvCategories.Columns.Add(new DataGridViewTextBoxColumn
                { DataPropertyName = "Name", HeaderText = "T\u00EAn danh m\u1EE5c / nh\u00E3n", Width = 280, MinimumWidth = 220 });
            dgvCategories.Columns.Add(new DataGridViewTextBoxColumn
                { DataPropertyName = "ProgressDisplay", HeaderText = "Ti\u1EBFn \u0111\u1ED9 c\u00E0o", Width = 360, MinimumWidth = 280 });
            dgvCategories.Columns.Add(new DataGridViewTextBoxColumn
                { DataPropertyName = "Url", HeaderText = "\u0110\u01B0\u1EDDng d\u1EABn Shopee (URL)",
                  AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 250 });

            dgvCategories.CellFormatting += (s, e) =>
            {
                if (e.CellStyle == null || e.RowIndex < 0 || e.RowIndex >= GlobalState.Categories.Count) return;
                var cat = GlobalState.Categories[e.RowIndex];
                if (dgvCategories.Columns[e.ColumnIndex].DataPropertyName == "ProgressDisplay")
                {
                    if (cat.Status == "Hoàn thành")
                    {
                        e.CellStyle.ForeColor = UiTheme.Success;
                        e.CellStyle.SelectionForeColor = UiTheme.Success;
                    }
                    else if (cat.Status == "Đang quét")
                    {
                        e.CellStyle.ForeColor = UiTheme.Accent;
                        e.CellStyle.SelectionForeColor = UiTheme.Accent;
                    }
                    else if (cat.CurrentSubIndex > 1 || cat.CurrentPage > 1)
                    {
                        e.CellStyle.ForeColor = UiTheme.Warning;
                        e.CellStyle.SelectionForeColor = UiTheme.Warning;
                    }
                    else
                    {
                        e.CellStyle.ForeColor = UiTheme.TextMuted;
                        e.CellStyle.SelectionForeColor = UiTheme.TextMuted;
                    }
                }
            };
            dgvCategories.CellPainting += DgvCategories_CellPainting;
            dgvCategories.CellEndEdit += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.RowIndex < GlobalState.Categories.Count)
                {
                    var cat = GlobalState.Categories[e.RowIndex];
                    if (dgvCategories.Columns[e.ColumnIndex].DataPropertyName == "Name")
                    {
                        cat.Name = ShopeeCategoryTranslator.Translate(cat.Name);
                    }
                    else if (dgvCategories.Columns[e.ColumnIndex].DataPropertyName == "Url")
                    {
                        if (string.IsNullOrWhiteSpace(cat.Name) || cat.Name == "Danh m\u1EE5c m\u1EDBi")
                        {
                            string extracted = ShopeeCategoryTranslator.ExtractCategoryNameFromUrl(cat.Url);
                            if (!string.IsNullOrWhiteSpace(extracted))
                                cat.Name = extracted;
                        }
                    }
                }
            };

            pnlCatCard.Controls.Add(dgvCategories);
            pnlCatCard.Controls.Add(pnlCatHeader);
            pnlContent.Controls.Add(pnlCatCard);

            // Keywords card (Fill)
            pnlKeywordCard = new RoundedPanel
            {
                Dock         = DockStyle.Fill,
                CornerRadius = 6,
                BorderColor  = UiTheme.BorderLight,
                Padding      = new Padding(14),
                Visible      = false
            };

            Panel pnlKwHeader = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 42,
                BackColor = UiTheme.CardBg
            };

            pnlKwHeader.Controls.Add(new Label
            {
                Text      = "T\u1EEB kh\u00F3a t\u00ECm ki\u1EBFm Shopee",
                Font      = UiTheme.FontSection,
                ForeColor = UiTheme.TextPrimary,
                Location  = new Point(0, 8),
                AutoSize  = true
            });

            RoundedButton btnDeleteKw = new RoundedButton
            {
                IconGlyph = "\uE74D",  // Delete
                IconRole  = ButtonIconRole.Danger,
                Text      = "X\u00F3a \u0111\u00E3 ch\u1ECDn"
            };
            btnDeleteKw.Click += BtnDeleteKw_Click;
            pnlKwHeader.Controls.Add(btnDeleteKw);

            RoundedButton btnResetKw = new RoundedButton
            {
                IconGlyph = "\uE72C",  // Refresh / Reset
                IconRole  = ButtonIconRole.Muted,
                Text      = "\u0110\u1EB7t l\u1EA1i ti\u1EBFn \u0111\u1ED9"
            };
            btnResetKw.Click += BtnResetKw_Click;
            pnlKwHeader.Controls.Add(btnResetKw);

            RoundedButton btnSyncKw = new RoundedButton
            {
                IconGlyph = "\uE896",  // Download / Import / Sync
                IconRole  = ButtonIconRole.Info,
                Text      = "N\u1EA1p t\u1EEB kh\u00F3a"
            };
            btnSyncKw.Click += (s, e) => SyncKeywordsFromInput(showFeedback: true);
            pnlKwHeader.Controls.Add(btnSyncKw);

            void UpdateKwButtons()
            {
                btnDeleteKw.Location = new Point(pnlKwHeader.Width - btnDeleteKw.Width - 10, 5);
                btnResetKw.Location  = new Point(btnDeleteKw.Left - btnResetKw.Width - 8, 5);
                btnSyncKw.Location   = new Point(btnResetKw.Left - btnSyncKw.Width - 8, 5);
            }
            pnlKwHeader.Resize       += (s, e) => UpdateKwButtons();
            btnDeleteKw.SizeChanged  += (s, e) => UpdateKwButtons();
            btnResetKw.SizeChanged   += (s, e) => UpdateKwButtons();
            btnSyncKw.SizeChanged    += (s, e) => UpdateKwButtons();
            UpdateKwButtons();

            // Left panel for textarea input
            Panel pnlKwLeft = new Panel
            {
                Dock      = DockStyle.Left,
                Width     = 340,
                BackColor = UiTheme.CardBg,
                Padding   = new Padding(0, 6, 12, 0)
            };

            Label lblKwInputTitle = new Label
            {
                Dock      = DockStyle.Top,
                Height    = 24,
                Text      = "Danh s\u00E1ch t\u1EEB kh\u00F3a (m\u1ED7i d\u00F2ng 1 t\u1EEB):",
                Font      = _boldCatFont,
                ForeColor = UiTheme.TextSecondary
            };

            Label lblKwInputNote = new Label
            {
                Dock      = DockStyle.Bottom,
                Height    = 34,
                Font      = UiTheme.FontCaption,
                ForeColor = UiTheme.TextMuted,
                Text      = "Nh\u1EADp danh s\u00E1ch t\u1EEB kh\u00F3a v\u00E0o \u0111\u00E2y. B\u1EA5m 'N\u1EA1p t\u1EEB kh\u00F3a' ho\u1EB7c b\u1EA5m 'Ch\u1EA1y' \u0111\u1EC3 qu\u00E9t."
            };

            txtKeywordsInput = new SuiteTextArea
            {
                Dock = DockStyle.Fill
            };
            txtKeywordsInput.Text = string.Join(Environment.NewLine, GlobalState.Keywords.Select(k => k.Keyword));
            txtKeywordsInput.Leave += (s, e) => SyncKeywordsFromInput(showFeedback: false);

            pnlKwLeft.Controls.Add(txtKeywordsInput);
            pnlKwLeft.Controls.Add(lblKwInputTitle);
            pnlKwLeft.Controls.Add(lblKwInputNote);

            // Right panel / grid for keywords status
            dgvKeywords = new DataGridView
            {
                Dock                = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows  = false,
                DataSource          = GlobalState.Keywords
            };
            UiTheme.StyleSuiteDataGrid(dgvKeywords);
            dgvKeywords.Columns.Add(new DataGridViewTextBoxColumn
                { DataPropertyName = "Keyword", HeaderText = "T\u1EEB kh\u00F3a t\u00ECm ki\u1EBFm", Width = 260, MinimumWidth = 180 });
            dgvKeywords.Columns.Add(new DataGridViewTextBoxColumn
                { DataPropertyName = "ProgressDisplay", HeaderText = "Ti\u1EBFn \u0111\u1ED9 c\u00E0o", Width = 300, MinimumWidth = 200 });
            dgvKeywords.Columns.Add(new DataGridViewTextBoxColumn
                { DataPropertyName = "ProductCount", HeaderText = "S\u1ED1 SP", Width = 90, MinimumWidth = 70 });
            dgvKeywords.Columns.Add(new DataGridViewTextBoxColumn
                { DataPropertyName = "LastScrapedAt", HeaderText = "C\u1EADp nh\u1EADt l\u00FAc",
                  AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 140,
                  DefaultCellStyle = new DataGridViewCellStyle { Format = "dd/MM/yyyy HH:mm" } });

            dgvKeywords.CellFormatting += (s, e) =>
            {
                if (e.CellStyle == null || e.RowIndex < 0 || e.RowIndex >= GlobalState.Keywords.Count) return;
                var kw = GlobalState.Keywords[e.RowIndex];
                if (dgvKeywords.Columns[e.ColumnIndex].DataPropertyName == "ProgressDisplay")
                {
                    if (kw.Status == "Hoàn thành")
                    {
                        e.CellStyle.ForeColor = UiTheme.Success;
                        e.CellStyle.SelectionForeColor = UiTheme.Success;
                    }
                    else if (kw.Status == "Đang quét")
                    {
                        e.CellStyle.ForeColor = UiTheme.Accent;
                        e.CellStyle.SelectionForeColor = UiTheme.Accent;
                    }
                    else if (kw.CurrentPage > 1)
                    {
                        e.CellStyle.ForeColor = UiTheme.Warning;
                        e.CellStyle.SelectionForeColor = UiTheme.Warning;
                    }
                    else
                    {
                        e.CellStyle.ForeColor = UiTheme.TextMuted;
                        e.CellStyle.SelectionForeColor = UiTheme.TextMuted;
                    }
                }
            };
            dgvKeywords.CellPainting += DgvKeywords_CellPainting;

            pnlKeywordCard.Controls.Add(dgvKeywords);
            pnlKeywordCard.Controls.Add(pnlKwLeft);
            pnlKeywordCard.Controls.Add(pnlKwHeader);
            dgvKeywords.BringToFront();
            pnlContent.Controls.Add(pnlKeywordCard);

            bodyPanel.Controls.Add(pnlContent);
            bodyPanel.Controls.Add(pnlSep1);
            bodyPanel.Controls.Add(pnlAction);
            bodyPanel.Controls.Add(pnlSep2);
            bodyPanel.Controls.Add(pnlLogCard);
        }

        private void UpdateStats()
        {
            if (this.InvokeRequired) { this.Invoke(new Action(UpdateStats)); return; }

            int totalProds = GlobalState.ScrapedProducts.Count;
            if (btnViewData != null)
                btnViewData.Text = $"Xem d\u1EEF li\u1EC7u ({totalProds:N0})";

            _mainForm?.UpdateStatusBarStats();
        }

        public void SelectTab(int index, bool notifyMain = true)
        {
            if (_activeTab == index && pnlCatCard != null && pnlCatCard.Visible == (index == 0)) return;
            _activeTab = index;
            if (btnModeCategory != null) btnModeCategory.Active = (index == 0);
            if (btnModeKeyword != null)  btnModeKeyword.Active  = (index == 1);
            if (pnlCatCard != null)      pnlCatCard.Visible     = (index == 0);
            if (pnlKeywordCard != null)  pnlKeywordCard.Visible = (index == 1);

            if (lblPageSubtitle != null)
            {
                lblPageSubtitle.Text = index == 0
                    ? "Qu\u1EA3n l\u00FD danh m\u1EE5c qu\u00E9t, lu\u00E2n phi\u00EAn t\u00E0i kho\u1EA3n v\u00E0 theo d\u00F5i ti\u1EBFn tr\u00ECnh c\u00E0o d\u1EEF li\u1EC7u."
                    : "Qu\u00E9t s\u1EA3n ph\u1EA9m theo t\u1EEB kh\u00F3a t\u00ECm ki\u1EBFm (like human typing), s\u1EAFp x\u1EBFp b\u00E1n ch\u1EA1y v\u00E0 c\u00E0o d\u1EEF li\u1EC7u.";
            }

            if (notifyMain)
            {
                _mainForm?.SyncTopTab(index);
            }
        }

        private void SyncKeywordsFromInput(bool showFeedback = false)
        {
            var rawText = txtKeywordsInput?.Text ?? string.Empty;
            var lines = rawText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(l => l.Trim())
                               .Where(l => !string.IsNullOrWhiteSpace(l))
                               .Distinct(StringComparer.OrdinalIgnoreCase)
                               .ToList();

            int added = 0;
            var existingSet = new HashSet<string>(GlobalState.Keywords.Select(k => k.Keyword), StringComparer.OrdinalIgnoreCase);

            foreach (var kw in lines)
            {
                if (!existingSet.Contains(kw))
                {
                    var info = new KeywordInfo
                    {
                        Keyword = kw,
                        Status = "Chưa quét",
                        CurrentPage = 0,
                        TotalPages = 0,
                        ProductCount = 0
                    };
                    GlobalState.Keywords.Add(info);
                    existingSet.Add(kw);
                    added++;
                }
            }

            if (added > 0)
            {
                DataManager.SaveData();
                RefreshKeywordsGrid();
            }

            if (showFeedback)
            {
                SuiteDialog.Alert(this, "N\u1EA1p t\u1EEB kh\u00F3a",
                    added > 0
                        ? $"Đã nạp thành công {added} từ khóa mới vào danh sách!"
                        : "Không có từ khóa mới nào được nạp (tất cả đã có trong danh sách).");
            }
        }

        private void BtnDeleteKw_Click(object? sender, EventArgs e)
        {
            var selected = GetSelectedKeywords();
            if (selected.Count > 0)
            {
                if (SuiteDialog.Confirm(this,
                    "X\u00E1c nh\u1EADn x\u00F3a",
                    $"B\u1EA1n c\u00F3 ch\u1EAFc ch\u1EAFn mu\u1ED1n x\u00F3a {selected.Count} t\u1EEB kh\u00F3a \u0111\u00E3 ch\u1ECDn kh\u00F4ng?",
                    "X\u00F3a", DialogTone.Danger) != DialogResult.Yes) return;

                foreach (var kw in selected)
                {
                    GlobalState.Keywords.Remove(kw);
                }
                DataManager.SaveData();
                txtKeywordsInput.Text = string.Join(Environment.NewLine, GlobalState.Keywords.Select(k => k.Keyword));
                RefreshKeywordsGrid();
            }
            else
            {
                SuiteDialog.Alert(this, "Th\u00F4ng b\u00E1o", "Vui l\u00F2ng ch\u1ECDn \u00EDt nh\u1EA5t m\u1ED9t t\u1EEB kh\u00F3a trong b\u1EA3ng \u0111\u1EC3 x\u00F3a.");
            }
        }

        private void BtnResetKw_Click(object? sender, EventArgs e)
        {
            var selected = GetSelectedKeywords();
            if (selected.Count > 0)
            {
                if (SuiteDialog.Confirm(this,
                    "\u0110\u1EB7t l\u1EA1i ti\u1EBFn \u0111\u1ED9",
                    $"B\u1EA1n c\u00F3 mu\u1ED1n \u0111\u1EB7t l\u1EA1i ti\u1EBFn \u0111\u1ED9 {selected.Count} t\u1EEB kh\u00F3a \u0111\u00E3 ch\u1ECDn v\u1EC1 ban \u0111\u1EA7u \u0111\u1EC3 qu\u00E9t l\u1EA1i t\u1EEB \u0111\u1EA7u kh\u00F4ng?",
                    "\u0110\u1EB7t l\u1EA1i", DialogTone.Primary) == DialogResult.Yes)
                {
                    foreach (var kw in selected)
                    {
                        DataManager.ResetKeywordProgress(kw.Keyword);
                    }
                    RefreshKeywordsGrid();
                }
            }
            else if (GlobalState.Keywords.Count > 0)
            {
                if (SuiteDialog.Confirm(this,
                    "\u0110\u1EB7t l\u1EA1i ti\u1EBFn \u0111\u1ED9 t\u1EA5t c\u1EA3",
                    "B\u1EA1n c\u00F3 mu\u1ED1n \u0111\u1EB7t l\u1EA1i ti\u1EBFn \u0111\u1ED9 c\u1EE7a TO\u00C0N B\u1ED8 t\u1EEB kh\u00F3a v\u1EC1 ban \u0111\u1EA7u \u0111\u1EC3 qu\u00E9t l\u1EA1i t\u1EEB \u0111\u1EA7u kh\u00F4ng?",
                    "\u0110\u1EB7t l\u1EA1i t\u1EA5t c\u1EA3", DialogTone.Primary) == DialogResult.Yes)
                {
                    foreach (var kw in GlobalState.Keywords)
                    {
                        DataManager.ResetKeywordProgress(kw.Keyword);
                    }
                    RefreshKeywordsGrid();
                }
            }
        }

        private List<KeywordInfo> GetSelectedKeywords()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (dgvKeywords.SelectedRows.Count > 0)
            {
                foreach (DataGridViewRow row in dgvKeywords.SelectedRows)
                {
                    if (row.DataBoundItem is KeywordInfo kw && !string.IsNullOrWhiteSpace(kw.Keyword))
                        seen.Add(kw.Keyword);
                }
            }
            else if (dgvKeywords.SelectedCells.Count > 0)
            {
                foreach (DataGridViewCell cell in dgvKeywords.SelectedCells)
                {
                    if (cell.RowIndex >= 0 && cell.RowIndex < dgvKeywords.Rows.Count)
                    {
                        if (dgvKeywords.Rows[cell.RowIndex].DataBoundItem is KeywordInfo kw && !string.IsNullOrWhiteSpace(kw.Keyword))
                            seen.Add(kw.Keyword);
                    }
                }
            }

            return GlobalState.Keywords.Where(k => seen.Contains(k.Keyword)).ToList();
        }

        private void RefreshKeywordsGrid()
        {
            if (dgvKeywords.InvokeRequired) { dgvKeywords.Invoke(new Action(RefreshKeywordsGrid)); return; }
            dgvKeywords.Refresh();
            UpdateStats();
        }

        private void BtnDeleteCat_Click(object? sender, EventArgs e)
        {
            if (dgvCategories.SelectedRows.Count > 0)
            {
                if (SuiteDialog.Confirm(this,
                    "X\u00E1c nh\u1EADn x\u00F3a",
                    $"B\u1EA1n c\u00F3 ch\u1EAFc ch\u1EAFn mu\u1ED1n x\u00F3a {dgvCategories.SelectedRows.Count} danh m\u1EE5c \u0111\u00E3 ch\u1ECDn kh\u00F4ng?",
                    "X\u00F3a", DialogTone.Danger) != DialogResult.Yes) return;

                foreach (DataGridViewRow row in dgvCategories.SelectedRows)
                {
                    if (row.DataBoundItem is CategoryInfo cat)
                        GlobalState.Categories.Remove(cat);
                }
            }
        }

        private void BtnResetCat_Click(object? sender, EventArgs e)
        {
            if (dgvCategories.SelectedRows.Count > 0)
            {
                if (SuiteDialog.Confirm(this,
                    "\u0110\u1EB7t l\u1EA1i ti\u1EBFn \u0111\u1ED9",
                    $"B\u1EA1n c\u00F3 mu\u1ED1n \u0111\u1EB7t l\u1EA1i ti\u1EBFn \u0111\u1ED9 {dgvCategories.SelectedRows.Count} danh m\u1EE5c \u0111\u00E3 ch\u1ECDn v\u1EC1 ban \u0111\u1EA7u \u0111\u1EC3 qu\u00E9t l\u1EA1i t\u1EEB \u0111\u1EA7u kh\u00F4ng?",
                    "\u0110\u1EB7t l\u1EA1i", DialogTone.Primary) == DialogResult.Yes)
                {
                    foreach (DataGridViewRow row in dgvCategories.SelectedRows)
                    {
                        if (row.DataBoundItem is CategoryInfo cat)
                            DataManager.ResetCategoryProgress(cat.Url);
                    }
                    dgvCategories.Refresh();
                }
            }
            else if (GlobalState.Categories.Count > 0)
            {
                if (SuiteDialog.Confirm(this,
                    "\u0110\u1EB7t l\u1EA1i ti\u1EBFn \u0111\u1ED9 t\u1EA5t c\u1EA3",
                    "B\u1EA1n c\u00F3 mu\u1ED1n \u0111\u1EB7t l\u1EA1i ti\u1EBFn \u0111\u1ED9 c\u1EE7a TO\u00C0N B\u1ED8 danh m\u1EE5c v\u1EC1 ban \u0111\u1EA7u \u0111\u1EC3 qu\u00E9t l\u1EA1i t\u1EEB \u0111\u1EA7u kh\u00F4ng?",
                    "\u0110\u1EB7t l\u1EA1i t\u1EA5t c\u1EA3", DialogTone.Primary) == DialogResult.Yes)
                {
                    foreach (var cat in GlobalState.Categories)
                        DataManager.ResetCategoryProgress(cat.Url);
                    dgvCategories.Refresh();
                }
            }
        }

        private List<CategoryInfo> GetSelectedCategories()
        {
            var seen = new HashSet<string>();
            if (dgvCategories.SelectedRows.Count > 0)
            {
                foreach (DataGridViewRow row in dgvCategories.SelectedRows)
                {
                    if (row.DataBoundItem is CategoryInfo cat && !string.IsNullOrWhiteSpace(cat.Url))
                        seen.Add(cat.Url);
                }
            }
            else if (dgvCategories.SelectedCells.Count > 0)
            {
                foreach (DataGridViewCell cell in dgvCategories.SelectedCells)
                {
                    if (cell.RowIndex >= 0 && cell.RowIndex < dgvCategories.Rows.Count)
                    {
                        if (dgvCategories.Rows[cell.RowIndex].DataBoundItem is CategoryInfo cat && !string.IsNullOrWhiteSpace(cat.Url))
                            seen.Add(cat.Url);
                    }
                }
            }

            return GlobalState.Categories.Where(c => seen.Contains(c.Url)).ToList();
        }

        private void SetRunningUiState(bool running)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => SetRunningUiState(running)));
                return;
            }
            _isRunning = running;
            btnRun.Enabled          = !running; // Chạy tất cả: tắt khi đang chạy, bật khi rảnh rỗi
            btnRunSelected.Enabled  = true;     // Chạy đã chọn: LUÔN BẬT (cả khi đang chạy lẫn rảnh rỗi)
            btnStop.Enabled         = running;  // Dừng tất cả: bật khi đang chạy, tắt khi rảnh rỗi
            btnStopSelected.Enabled = running;  // Dừng đã chọn: bật khi đang chạy, tắt khi rảnh rỗi
        }

        private async void BtnRun_Click(object? sender, EventArgs e)
        {
            if (_isRunning) return;

            if (_activeTab == 0)
            {
                if (GlobalState.Categories.Count == 0)
                {
                    SuiteDialog.Alert(this, "Thông báo",
                        "Vui lòng thêm ít nhất một danh mục để bắt đầu quét.");
                    return;
                }

                await StartSequenceAsync(GlobalState.Categories.ToList());
            }
            else
            {
                SyncKeywordsFromInput(showFeedback: false);
                if (GlobalState.Keywords.Count == 0)
                {
                    SuiteDialog.Alert(this, "Thông báo",
                        "Vui lòng nhập ít nhất một từ khóa để bắt đầu quét.");
                    return;
                }

                await StartKeywordSequenceAsync(GlobalState.Keywords.ToList());
            }
        }

        private async void BtnRunSelected_Click(object? sender, EventArgs e)
        {
            if (_activeTab == 0)
            {
                var selectedCats = GetSelectedCategories();
                if (selectedCats.Count == 0)
                {
                    SuiteDialog.Alert(this, "Thông báo",
                        "Vui lòng chọn ít nhất một danh mục trong bảng để bắt đầu chạy.");
                    return;
                }

                if (_isRunning)
                {
                    int addedCount = 0;
                    foreach (var cat in selectedCats)
                    {
                        if (cat.Status == "Hoàn thành")
                        {
                            DataManager.ResetCategoryProgress(cat.Url);
                        }
                        else if (cat.Status != "Đang quét")
                        {
                            cat.Status = "Chờ quét";
                            DataManager.UpdateCategoryProgress(cat.Url, cat.CurrentSubIndex, cat.CurrentPage, cat.TotalSubCategories, "Chờ quét");
                        }

                        _currentExcludedUrls.TryRemove(cat.Url, out _);
                        if (_activeTargetUrls.TryAdd(cat.Url, 0))
                            addedCount++;
                    }

                    RefreshCategoriesGrid();
                    LogMessage($"[System] ➕ Đã thêm {selectedCats.Count} danh mục vào hàng đợi đang chạy ({addedCount} danh mục mới).");
                    _spawnAdditionalWorkersAction?.Invoke();
                    return;
                }

                await StartSequenceAsync(selectedCats);
            }
            else
            {
                var selectedKws = GetSelectedKeywords();
                if (selectedKws.Count == 0)
                {
                    SuiteDialog.Alert(this, "Thông báo",
                        "Vui lòng chọn ít nhất một từ khóa trong bảng để bắt đầu chạy.");
                    return;
                }

                if (_isRunning)
                {
                    int addedCount = 0;
                    foreach (var kw in selectedKws)
                    {
                        if (kw.Status == "Hoàn thành")
                        {
                            DataManager.ResetKeywordProgress(kw.Keyword);
                        }
                        else if (kw.Status != "Đang quét")
                        {
                            kw.Status = "Chờ quét";
                            DataManager.UpdateKeywordProgress(kw.Keyword, kw.CurrentPage, kw.TotalPages, kw.ProductCount, "Chờ quét");
                        }

                        _currentExcludedKws.TryRemove(kw.Keyword, out _);
                        if (_activeTargetKws.TryAdd(kw.Keyword, 0))
                            addedCount++;
                    }

                    RefreshKeywordsGrid();
                    LogMessage($"[System] ➕ Đã thêm {selectedKws.Count} từ khóa vào hàng đợi đang chạy ({addedCount} từ khóa mới).");
                    _spawnAdditionalKeywordWorkersAction?.Invoke();
                    return;
                }

                await StartKeywordSequenceAsync(selectedKws);
            }
        }

        private void BtnStopSelected_Click(object? sender, EventArgs e)
        {
            if (!_isRunning)
            {
                SuiteDialog.Alert(this, "Thông báo", "Hiện không có tiến trình nào đang chạy.");
                return;
            }

            if (_activeTab == 0)
            {
                var selectedCats = GetSelectedCategories();
                if (selectedCats.Count == 0)
                {
                    SuiteDialog.Alert(this, "Thông báo",
                        "Vui lòng chọn ít nhất một danh mục trong bảng để dừng flow.");
                    return;
                }

                int stoppedCount = 0;
                foreach (var cat in selectedCats)
                {
                    _currentExcludedUrls[cat.Url] = 0;
                    _activeTargetUrls.TryRemove(cat.Url, out _);

                    if (_activeCatSessions.TryGetValue(cat.Url, out var session))
                    {
                        _manuallyStoppedCats[cat.Url] = 0;
                        try { session.Stop(); } catch { }
                        stoppedCount++;
                        LogMessage($"[System] 🛑 Đã gửi lệnh dừng flow của danh mục: {cat.Name}");
                    }
                    else if (cat.Status == "Chờ quét")
                    {
                        stoppedCount++;
                        LogMessage($"[System] 🛑 Đã loại danh mục đang chờ khỏi hàng đợi: {cat.Name}");
                    }

                    if (cat.Status == "Đang quét")
                    {
                        cat.Status = "Chờ quét";
                        DataManager.UpdateCategoryProgress(cat.Url, cat.CurrentSubIndex, cat.CurrentPage, cat.TotalSubCategories, "Chờ quét");
                    }
                }

                RefreshCategoriesGrid();

                if (stoppedCount == 0)
                {
                    SuiteDialog.Alert(this, "Thông báo",
                        "Các danh mục đã chọn hiện không có flow nào đang chạy hoặc đang chờ.");
                }
            }
            else
            {
                var selectedKws = GetSelectedKeywords();
                if (selectedKws.Count == 0)
                {
                    SuiteDialog.Alert(this, "Thông báo",
                        "Vui lòng chọn ít nhất một từ khóa trong bảng để dừng flow.");
                    return;
                }

                int stoppedCount = 0;
                foreach (var kw in selectedKws)
                {
                    _currentExcludedKws[kw.Keyword] = 0;
                    _activeTargetKws.TryRemove(kw.Keyword, out _);

                    if (_activeKeywordSessions.TryGetValue(kw.Keyword, out var session))
                    {
                        _manuallyStoppedKws[kw.Keyword] = 0;
                        try { session.Stop(); } catch { }
                        stoppedCount++;
                        LogMessage($"[System] 🛑 Đã gửi lệnh dừng flow của từ khóa: {kw.Keyword}");
                    }
                    else if (kw.Status == "Chờ quét")
                    {
                        stoppedCount++;
                        LogMessage($"[System] 🛑 Đã loại từ khóa đang chờ khỏi hàng đợi: {kw.Keyword}");
                    }

                    if (kw.Status == "Đang quét")
                    {
                        kw.Status = "Chờ quét";
                        DataManager.UpdateKeywordProgress(kw.Keyword, kw.CurrentPage, kw.TotalPages, kw.ProductCount, "Chờ quét");
                    }
                }

                RefreshKeywordsGrid();

                if (stoppedCount == 0)
                {
                    SuiteDialog.Alert(this, "Thông báo",
                        "Các từ khóa đã chọn hiện không có flow nào đang chạy hoặc đang chờ.");
                }
            }
        }

        public void StopAutomation()
        {
            _isStopRequested = true;
            try
            {
                foreach (var session in _activeCatSessions.Values)
                {
                    try { session.Stop(); } catch { }
                }
                foreach (var session in _activeKeywordSessions.Values)
                {
                    try { session.Stop(); } catch { }
                }
                _automation.Stop();
            }
            catch { }
        }

        private void BtnStop_Click(object? sender, EventArgs e)
        {
            btnStop.Enabled = false;
            btnStopSelected.Enabled = false;
            LogMessage("[System] 🛑 Đang dừng toàn bộ tiến trình và đóng trình duyệt sạch sẽ...");
            StopAutomation();
        }

        private async Task StartSequenceAsync(List<CategoryInfo> categoriesList)
        {
            int autoReset = DataManager.CheckAndResetExpiredCategories();
            if (autoReset > 0)
            {
                LogMessage($"[AutoRerun] 🔄 Đã tự động làm mới {autoReset} danh mục đã qua {GlobalState.AutoRerunDays} ngày để quét chu kỳ mới!");
                dgvCategories.Refresh();
            }

            var pendingCats = categoriesList.Where(c => c.Status != "Ho\u00E0n th\u00E0nh").ToList();
            if (pendingCats.Count == 0 && categoriesList.Count > 0)
            {
                if (SuiteDialog.Confirm(this, "Th\u00F4ng b\u00E1o",
                    "T\u1EA5t c\u1EA3 danh m\u1EE5c \u0111\u01B0\u1EE3c ch\u1ECDn \u0111\u1EC1u \u0111\u00E3 HO\u00C0N TH\u00C0NH.\nB\u1EA1n c\u00F3 mu\u1ED1n \u0111\u1EB7t l\u1EA1i ti\u1EBFn \u0111\u1ED9 \u0111\u1EC3 qu\u00E9t l\u1EA1i t\u1EEB \u0111\u1EA7u kh\u00F4ng?",
                    "Qu\u00E9t l\u1EA1i t\u1EEB \u0111\u1EA7u", DialogTone.Primary) == DialogResult.Yes)
                {
                    foreach (var c in categoriesList)
                        DataManager.ResetCategoryProgress(c.Url);
                    dgvCategories.Refresh();
                    pendingCats = categoriesList.ToList();
                }
                else
                {
                    return;
                }
            }

            // Tạm: một Brave, profile cố định, đăng nhập tay.
            // Bật lại false khi chạy user Shopee thật — lúc đó mới tạo profile theo tài khoản.
            if (UseFixedTestProfile)
            {
                _isStopRequested = false;
                SetRunningUiState(true);
                try
                {
                    LogMessage("--- [Phiên test — profile cố định] ---");
                    string proxyAddress = string.Empty;
                    var testProxies = GlobalState.Proxies.Where(p => !string.IsNullOrWhiteSpace(p.ApiKey)).ToList();
                    if (testProxies.Count > 0)
                    {
                        var globalProxy = testProxies[0];
                        var proxyInfo = await KiotProxyClient.FetchOrRefreshProxyAsync(
                            globalProxy.ApiKey,
                            safeSecondsThreshold: 180,
                            logCallback: LogMessage,
                            isStopRequested: () => _isStopRequested);
                        if (proxyInfo.Success && !string.IsNullOrWhiteSpace(proxyInfo.Http))
                        {
                            var (isAlive, publicIp) = await KiotProxyClient.CheckProxyAliveAsync(proxyInfo.Http, 8);
                            if (isAlive)
                            {
                                proxyAddress = proxyInfo.Http;
                                LogMessage($"[Proxy] 🌐 Proxy hoạt động tốt! IP Public: {publicIp} (Hạn còn {proxyInfo.SecondsRemaining}s)");
                            }
                            else
                            {
                                LogMessage($"[Proxy] ❌ Proxy {proxyInfo.Http} không phản hồi mạng (Chết hoặc nghẽn)! DỪNG PHIÊN.");
                                return;
                            }
                        }
                        else
                        {
                            LogMessage($"[Proxy] ❌ Không lấy được IP từ KiotProxy ({proxyInfo.ErrorMessage}). DỪNG PHIÊN.");
                            return;
                        }
                    }
                    else
                    {
                        LogMessage("[Proxy] ⚠️ Chưa cấu hình Proxy. Sẽ chạy trực tiếp qua IP máy.");
                    }

                    await _automation.RunManualSessionAsync(categoriesList, LogMessage, proxyAddress);
                    LogMessage("--- [Phiên test đã kết thúc] ---");
                }
                catch (Exception ex)
                {
                    LogMessage("[Test] Lỗi: " + ex.Message);
                }
                finally
                {
                    SetRunningUiState(false);
                    UpdateStats();
                }
                return;
            }

            var availableAccounts = GlobalState.Accounts
                .Where(a => a.Status != "Captcha" && a.Status != "Wrong U/P").ToList();
            if (availableAccounts.Count == 0)
            {
                SuiteDialog.Alert(this, "C\u1EA3nh b\u00E1o",
                    "Kh\u00F4ng c\u00F3 t\u00E0i kho\u1EA3n kh\u1EA3 d\u1EE5ng n\u00E0o (T\u1EA5t c\u1EA3 t\u00E0i kho\u1EA3n \u0111\u1EC1u \u0111\u00E3 d\u00EDnh Captcha ho\u1EB7c sai U/P).");
                return;
            }

            var availableProxies = GlobalState.Proxies
                .Where(p => !string.IsNullOrWhiteSpace(p.ApiKey)).ToList();

            // Ràng buộc số flow:
            // 1. Số flow không vượt quá số Proxy (nếu có proxy)
            // 2. Số flow không vượt quá số Account khả dụng
            // 3. Số flow không vượt quá số danh mục cần quét
            // 4. Số flow tuân theo cấu hình người dùng
            int configFlows = Math.Max(1, GlobalState.MaxConcurrentFlows);
            int proxyCount = availableProxies.Count;
            int accountCount = availableAccounts.Count;
            int pendingTargetCount = categoriesList.Count(c => c.Status != "Ho\u00E0n th\u00E0nh");
            if (pendingTargetCount <= 0) pendingTargetCount = 1;

            int actualFlows;
            if (proxyCount == 0)
            {
                LogMessage("[Proxy] ⚠️ Không có proxy nào được cấu hình! Để tránh bị khóa IP/Captcha hàng loạt, hệ thống chỉ cho phép chạy tối đa 1 luồng bằng IP máy.");
                actualFlows = 1;
            }
            else
            {
                actualFlows = Math.Min(pendingTargetCount, Math.Min(configFlows, Math.Min(proxyCount, accountCount)));
                LogMessage($"[System] 🚀 Khởi động {actualFlows} flow song song (Cấu hình: {configFlows} flow | Cần quét: {pendingTargetCount} cat | Proxy: {proxyCount} | Tài khoản: {accountCount}).");
                LogMessage($"[System] 🔒 Ràng buộc: Mỗi flow chạy 1 tài khoản riêng biệt trên 1 proxy riêng biệt (1-1-1).");
            }

            _isStopRequested = false;
            _activeCatSessions.Clear();
            _manuallyStoppedCats.Clear();
            _currentExcludedUrls.Clear();
            _activeTargetUrls.Clear();
            foreach (var c in categoriesList)
            {
                _activeTargetUrls[c.Url] = 0;
            }
            SetRunningUiState(true);

            try
            {
                LogMessage($"--- [Bắt đầu tiến trình sequence {actualFlows} luồng ({categoriesList.Count} danh mục mục tiêu)] ---");

                var accountQueue = new System.Collections.Concurrent.ConcurrentQueue<AccountInfo>(availableAccounts);
                var categoryLock = new object();
                var workerLock = new object();
                var activeFlowIndices = new HashSet<int>();
                var flowTasks = new List<Task>();

                CategoryInfo? ClaimNextCategory()
                {
                    lock (categoryLock)
                    {
                        var cat = GlobalState.Categories.FirstOrDefault(c =>
                            _activeTargetUrls.ContainsKey(c.Url) &&
                            !_currentExcludedUrls.ContainsKey(c.Url) &&
                            c.Status == "Chờ quét");
                        if (cat != null)
                        {
                            cat.Status = "Đang quét";
                            DataManager.UpdateCategoryProgress(cat.Url, cat.CurrentSubIndex, cat.CurrentPage, cat.TotalSubCategories, "Đang quét");
                            RefreshCategoriesGrid();
                            return cat;
                        }
                        return null;
                    }
                }

                void ReleaseCategoryBack(CategoryInfo cat)
                {
                    lock (categoryLock)
                    {
                        if (cat.Status == "Đang quét")
                        {
                            cat.Status = "Chờ quét";
                            DataManager.UpdateCategoryProgress(cat.Url, cat.CurrentSubIndex, cat.CurrentPage, cat.TotalSubCategories, "Chờ quét");
                            RefreshCategoriesGrid();
                        }
                    }
                }

                bool HasAnyActiveOrPendingCategories()
                {
                    lock (categoryLock)
                    {
                        return GlobalState.Categories.Any(c =>
                            _activeTargetUrls.ContainsKey(c.Url) &&
                            !_currentExcludedUrls.ContainsKey(c.Url) &&
                            (c.Status == "Chờ quét" || c.Status == "Đang quét"));
                    }
                }

                async Task RunWorkerAsync(int flowIndex, ProxyInfo? assignedProxy)
                {
                    string flowTag = $"[Flow #{flowIndex + 1}]";
                    AccountInfo? currentAccount = null;

                    while (!_isStopRequested)
                    {
                        // 1. Nhận danh mục tiếp theo
                        var category = ClaimNextCategory();
                        if (category == null)
                        {
                            if (HasAnyActiveOrPendingCategories())
                            {
                                await Task.Delay(2500);
                                if (_isStopRequested) break;
                                category = ClaimNextCategory();
                            }

                            if (category == null)
                            {
                                LogMessage($"{flowTag} 🏁 Không còn danh mục nào cần quét. Flow kết thúc.");
                                break;
                            }
                        }

                        // 2. Đảm bảo flow đang có một tài khoản hợp lệ
                        if (currentAccount == null || currentAccount.Status == "Captcha" || currentAccount.Status == "Wrong U/P")
                        {
                            if (!accountQueue.TryDequeue(out currentAccount))
                            {
                                LogMessage($"{flowTag} ⚠️ Hết tài khoản khả dụng trong hàng đợi!");
                                ReleaseCategoryBack(category);
                                break;
                            }

                            currentAccount.Status = "Running";
                            DataManager.UpdateAccountStatus(currentAccount.Id, "Running");
                            RefreshAccountsGrid();
                            LogMessage($"{flowTag} 👤 Kích hoạt tài khoản: {currentAccount.Username}");
                        }

                        // 3. Chuẩn bị Proxy (Chống rò rỉ IP & kiểm tra TTL)
                        string proxyAddress = string.Empty;
                        if (assignedProxy != null && !string.IsNullOrWhiteSpace(assignedProxy.ApiKey))
                        {
                            LogMessage($"{flowTag} [Proxy] Kiểm tra trạng thái proxy ({assignedProxy.Name})...");
                            var proxyInfo = await KiotProxyClient.FetchOrRefreshProxyAsync(
                                assignedProxy.ApiKey,
                                safeSecondsThreshold: 180,
                                logCallback: msg => LogMessage($"{flowTag} {msg}"),
                                isStopRequested: () => _isStopRequested);

                            if (_isStopRequested) break;

                            if (!proxyInfo.Success || string.IsNullOrWhiteSpace(proxyInfo.Http))
                            {
                                LogMessage($"{flowTag} [Proxy] ❌ Không lấy được proxy từ key {assignedProxy.Name} ({proxyInfo.ErrorMessage}).");
                                LogMessage($"{flowTag} [Proxy] 🛑 DỪNG FLOW ĐỂ TRÁNH LỘ IP MÁY THẬT GÂY CAPTCHA!");
                                ReleaseCategoryBack(category);
                                break;
                            }

                            LogMessage($"{flowTag} [Proxy] Đang kiểm tra kết nối mạng qua {proxyInfo.Http}...");
                            var (isAlive, publicIp) = await KiotProxyClient.CheckProxyAliveAsync(proxyInfo.Http, timeoutSeconds: 8);
                            if (!isAlive && !_isStopRequested)
                            {
                                LogMessage($"{flowTag} [Proxy] ⚠️ Proxy {proxyInfo.Http} không phản hồi mạng. Đang cấp lại proxy mới...");
                                var refreshedProxy = await KiotProxyClient.FetchOrRefreshProxyAsync(
                                    assignedProxy.ApiKey,
                                    safeSecondsThreshold: 999999, // Ép xin cấp IP mới
                                    logCallback: msg => LogMessage($"{flowTag} {msg}"),
                                    isStopRequested: () => _isStopRequested);

                                if (refreshedProxy.Success && !string.IsNullOrWhiteSpace(refreshedProxy.Http))
                                {
                                    var (alive2, publicIp2) = await KiotProxyClient.CheckProxyAliveAsync(refreshedProxy.Http, timeoutSeconds: 8);
                                    if (alive2)
                                    {
                                        proxyInfo = refreshedProxy;
                                        isAlive = true;
                                        publicIp = publicIp2;
                                        LogMessage($"{flowTag} [Proxy] 🎉 Đổi IP mới thành công: {refreshedProxy.Http} (IP: {publicIp2}, Hạn còn {refreshedProxy.SecondsRemaining}s)");
                                    }
                                }
                            }

                            if (!isAlive)
                            {
                                if (_isStopRequested) break;
                                LogMessage($"{flowTag} [Proxy] ❌ Proxy {proxyInfo.Http} không phản hồi mạng (Chết hoặc nghẽn)! DỪNG FLOW ĐỂ TRÁNH LỘ IP MÁY.");
                                ReleaseCategoryBack(category);
                                break;
                            }

                            proxyAddress = proxyInfo.Http;
                            LogMessage($"{flowTag} [Proxy] 🌐 Proxy hoạt động tốt! IP Public: {publicIp} (Hạn còn {proxyInfo.SecondsRemaining}s)");
                        }
                        else
                        {
                            LogMessage($"{flowTag} [Proxy] ⚠️ Chạy trực tiếp qua IP máy.");
                        }

                        // 4. Khởi chạy tiến trình cào danh mục
                        LogMessage($"{flowTag} 🚀 Bắt đầu quét danh mục: {category.Name} ({category.Url})");
                        var result = await _automation.RunTaskAsync(
                            currentAccount,
                            proxyAddress,
                            new List<CategoryInfo> { category },
                            msg => LogMessage($"{flowTag} {msg}"),
                            session =>
                            {
                                _activeCatSessions[category.Url] = session;
                                if (_manuallyStoppedCats.ContainsKey(category.Url))
                                {
                                    try { session.Stop(); } catch { }
                                }
                            });

                        _activeCatSessions.TryRemove(category.Url, out _);

                        // 5. Xử lý kết quả
                        if (_isStopRequested)
                        {
                            if (category.Status != "Hoàn thành")
                            {
                                ReleaseCategoryBack(category);
                            }
                            break;
                        }

                        if (_manuallyStoppedCats.TryRemove(category.Url, out _))
                        {
                            LogMessage($"{flowTag} 🛑 Danh mục '{category.Name}' đã được dừng thủ công.");
                            await Task.Delay(1000);
                            continue;
                        }

                        if (result == AutomationResult.Captcha)
                        {
                            currentAccount.Status = "Captcha";
                            DataManager.UpdateAccountStatus(currentAccount.Id, "Captcha");
                            RefreshAccountsGrid();
                            LogMessage($"{flowTag} ⚠️ Tài khoản {currentAccount.Username} dính Captcha!");
                            ReleaseCategoryBack(category);
                            currentAccount = null;
                            await Task.Delay(2000);
                        }
                        else if (result == AutomationResult.WrongPassword)
                        {
                            currentAccount.Status = "Wrong U/P";
                            DataManager.UpdateAccountStatus(currentAccount.Id, "Wrong U/P");
                            RefreshAccountsGrid();
                            LogMessage($"{flowTag} ❌ Tài khoản {currentAccount.Username} sai Username/Password!");
                            ReleaseCategoryBack(category);
                            currentAccount = null;
                            await Task.Delay(2000);
                        }
                        else if (result == AutomationResult.Success)
                        {
                            LogMessage($"{flowTag} ✅ Hoàn thành danh mục: {category.Name}!");
                            RefreshCategoriesGrid();
                            UpdateStats();
                        }
                        else
                        {
                            LogMessage($"{flowTag} ⚠️ Phiên quét kết thúc với trạng thái {result}.");
                            if (category.Status != "Hoàn thành")
                            {
                                ReleaseCategoryBack(category);
                            }
                            await Task.Delay(2000);
                        }
                    }

                    if (currentAccount != null && currentAccount.Status == "Running")
                    {
                        currentAccount.Status = "Done";
                        DataManager.UpdateAccountStatus(currentAccount.Id, "Done");
                        RefreshAccountsGrid();
                    }
                }

                void TrySpawnAdditionalWorkers()
                {
                    lock (workerLock)
                    {
                        if (_isStopRequested) return;

                        int pendingCount = GlobalState.Categories.Count(c =>
                            _activeTargetUrls.ContainsKey(c.Url) &&
                            !_currentExcludedUrls.ContainsKey(c.Url) &&
                            (c.Status == "Chờ quét" || c.Status == "Đang quét"));

                        int maxPossible = proxyCount == 0
                            ? 1
                            : Math.Min(pendingCount, Math.Min(configFlows, Math.Min(proxyCount, accountCount)));

                        for (int i = 0; i < maxPossible; i++)
                        {
                            if (!activeFlowIndices.Contains(i))
                            {
                                activeFlowIndices.Add(i);
                                int flowIdx = i;
                                var assignedProxy = proxyCount > 0 ? availableProxies[flowIdx] : null;
                                var task = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await RunWorkerAsync(flowIdx, assignedProxy);
                                    }
                                    finally
                                    {
                                        lock (workerLock)
                                        {
                                            activeFlowIndices.Remove(flowIdx);
                                        }
                                    }
                                });
                                flowTasks.Add(task);
                            }
                        }
                    }
                }

                _spawnAdditionalWorkersAction = TrySpawnAdditionalWorkers;
                TrySpawnAdditionalWorkers();

                while (true)
                {
                    Task[] currentTasks;
                    lock (workerLock)
                    {
                        currentTasks = flowTasks.Where(t => !t.IsCompleted).ToArray();
                    }
                    if (currentTasks.Length == 0) break;
                    await Task.WhenAny(currentTasks);
                }

                LogMessage("--- [Tiến trình sequence đa luồng đã kết thúc] ---");
            }
            catch (Exception ex)
            {
                LogMessage("[System] Lỗi sequence: " + ex.Message);
            }
            finally
            {
                _spawnAdditionalWorkersAction = null;
                _activeCatSessions.Clear();
                _manuallyStoppedCats.Clear();
                _currentExcludedUrls.Clear();
                _activeTargetUrls.Clear();
                SetRunningUiState(false);
                UpdateStats();
                RefreshCategoriesGrid();
                RefreshAccountsGrid();
            }
        }

        private static readonly bool UseFixedTestProfile = false;

        private void RefreshCategoriesGrid()
        {
            if (dgvCategories.InvokeRequired) { dgvCategories.Invoke(new Action(RefreshCategoriesGrid)); return; }
            dgvCategories.Refresh();
            UpdateStats();
        }

        private void RefreshAccountsGrid()
        {
            if (this.InvokeRequired) { this.Invoke(new Action(RefreshAccountsGrid)); return; }
            GlobalState.Accounts.ResetBindings();
            UpdateStats();
        }

        private void LogMessage(string message)
        {
            if (txtLogs.InvokeRequired) { txtLogs.Invoke(new Action<string>(LogMessage), message); return; }
            string ts = DateTime.Now.ToString("HH:mm:ss");
            txtLogs.AppendText($"[{ts}] {message}\r\n");
        }

        private static readonly Font _mdl2CatFont = CreateMdl2CatFont();
        private static readonly Font _boldCatFont = CreateBoldCatFont();

        private static Font CreateMdl2CatFont()
        {
            try { return new Font("Segoe MDL2 Assets", 9.5f, FontStyle.Regular); }
            catch { return UiTheme.FontBody; }
        }

        private static Font CreateBoldCatFont()
        {
            try { return new Font("Segoe UI Semibold", 9.5f, FontStyle.Regular); }
            catch { return UiTheme.FontBody; }
        }

        private void DgvCategories_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.RowIndex >= GlobalState.Categories.Count) return;
            if (dgvCategories.Columns[e.ColumnIndex].DataPropertyName != "ProgressDisplay") return;

            var cat = GlobalState.Categories[e.RowIndex];

            // 1. Paint background (handles selection, hover, and normal white)
            e.PaintBackground(e.CellBounds, true);

            string glyph;
            Color iconColor;
            Color textColor;
            Font textFont = dgvCategories.Font;
            string text;

            string subText = cat.TotalSubCategories > 0
                ? $"Mục {cat.CurrentSubIndex}/{cat.TotalSubCategories} (Trang {cat.CurrentPage})"
                : $"Mục {cat.CurrentSubIndex} (Trang {cat.CurrentPage})";

            if (cat.Status == "Đang quét")
            {
                glyph = "\uE72C"; // Windows MDL2 Sync / Rotating arrows (quét dữ liệu)
                iconColor = UiTheme.Accent;
                textColor = UiTheme.Accent;
                textFont = _boldCatFont;
                text = $"Đang quét: {subText}";
            }
            else if (cat.Status == "Hoàn thành")
            {
                glyph = "\uE73E"; // Checkmark
                iconColor = UiTheme.Success;
                textColor = UiTheme.Success;
                text = "Hoàn thành";
            }
            else if (cat.CurrentSubIndex > 1 || cat.CurrentPage > 1)
            {
                glyph = "\uE769"; // Pause
                iconColor = UiTheme.Warning;
                textColor = UiTheme.Warning;
                text = $"Tạm dừng: {subText}";
            }
            else
            {
                glyph = "\uE738"; // Dot / circle outline
                iconColor = UiTheme.TextMuted;
                textColor = UiTheme.TextMuted;
                text = "Chưa quét";
            }

            if (e.Graphics == null) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int padLeft = e.CellBounds.Left + 10;
            int cellH = e.CellBounds.Height;
            int cellTop = e.CellBounds.Top;

            // 2. Draw MDL2 vector icon
            if (!string.IsNullOrEmpty(glyph))
            {
                Rectangle iconRect = new Rectangle(padLeft, cellTop, 18, cellH);
                TextRenderer.DrawText(g, glyph, _mdl2CatFont, iconRect, iconColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                padLeft += 22;
            }

            // 3. Draw text with auto-ellipsis and proper color
            int textW = Math.Max(10, e.CellBounds.Right - padLeft - 6);
            Rectangle textRect = new Rectangle(padLeft, cellTop, textW, cellH);
            TextRenderer.DrawText(g, text, textFont, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            // 4. Paint bottom horizontal border line
            using (var borderPen = new Pen(UiTheme.BorderLightest, 1))
            {
                g.DrawLine(borderPen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
            }

            e.Handled = true;
        }

        private async Task StartKeywordSequenceAsync(List<KeywordInfo> keywordsList)
        {
            var pendingKws = keywordsList.Where(k => k.Status != "Ho\u00E0n th\u00E0nh").ToList();
            if (pendingKws.Count == 0 && keywordsList.Count > 0)
            {
                if (SuiteDialog.Confirm(this, "Th\u00F4ng b\u00E1o",
                    "T\u1EA5t c\u1EA3 t\u1EEB kh\u00F3a \u0111\u01B0\u1EE3c ch\u1ECDn \u0111\u1EC1u \u0111\u00E3 HO\u00C0N TH\u00C0NH.\nB\u1EA1n c\u00F3 mu\u1ED1n \u0111\u1EB7t l\u1EA1i ti\u1EBFn \u0111\u1ED9 \u0111\u1EC3 qu\u00E9t l\u1EA1i t\u1EEB \u0111\u1EA7u kh\u00F4ng?",
                    "Qu\u00E9t l\u1EA1i t\u1EEB \u0111\u1EA7u", DialogTone.Primary) == DialogResult.Yes)
                {
                    foreach (var k in keywordsList)
                        DataManager.ResetKeywordProgress(k.Keyword);
                    RefreshKeywordsGrid();
                    pendingKws = keywordsList.ToList();
                }
                else
                {
                    return;
                }
            }

            var availableAccounts = GlobalState.Accounts
                .Where(a => a.Status != "Captcha" && a.Status != "Wrong U/P").ToList();
            if (availableAccounts.Count == 0)
            {
                SuiteDialog.Alert(this, "C\u1EA3nh b\u00E1o",
                    "Kh\u00F4ng c\u00F3 t\u00E0i kho\u1EA3n kh\u1EA3 d\u1EE5ng n\u00E0o (T\u1EA5t c\u1EA3 t\u00E0i kho\u1EA3n \u0111\u1EC1u \u0111\u00E3 d\u00EDnh Captcha ho\u1EB7c sai U/P).");
                return;
            }

            var availableProxies = GlobalState.Proxies
                .Where(p => !string.IsNullOrWhiteSpace(p.ApiKey)).ToList();

            int configFlows = Math.Max(1, GlobalState.MaxConcurrentFlows);
            int proxyCount = availableProxies.Count;
            int accountCount = availableAccounts.Count;
            int pendingTargetCount = keywordsList.Count(k => k.Status != "Ho\u00E0n th\u00E0nh");
            if (pendingTargetCount <= 0) pendingTargetCount = 1;

            int actualFlows;
            if (proxyCount == 0)
            {
                LogMessage("[Proxy] ⚠️ Không có proxy nào được cấu hình! Để tránh bị khóa IP/Captcha hàng loạt, hệ thống chỉ cho phép chạy tối đa 1 luồng bằng IP máy.");
                actualFlows = 1;
            }
            else
            {
                actualFlows = Math.Min(pendingTargetCount, Math.Min(configFlows, Math.Min(proxyCount, accountCount)));
                LogMessage($"[System] 🚀 Khởi động {actualFlows} flow tìm kiếm từ khóa song song (Cấu hình: {configFlows} flow | Cần quét: {pendingTargetCount} từ khóa | Proxy: {proxyCount} | Tài khoản: {accountCount}).");
                LogMessage($"[System] 🔒 Ràng buộc: Mỗi flow chạy 1 tài khoản riêng biệt trên 1 proxy riêng biệt (1-1-1).");
            }

            _isStopRequested = false;
            _activeKeywordSessions.Clear();
            _manuallyStoppedKws.Clear();
            _currentExcludedKws.Clear();
            _activeTargetKws.Clear();
            foreach (var k in keywordsList)
            {
                _activeTargetKws[k.Keyword] = 0;
                if (k.Status != "Hoàn thành" && k.Status != "Đang quét")
                {
                    k.Status = "Chờ quét";
                    DataManager.UpdateKeywordProgress(k.Keyword, k.CurrentPage, k.TotalPages, k.ProductCount, "Chờ quét");
                }
            }
            RefreshKeywordsGrid();
            SetRunningUiState(true);

            try
            {
                LogMessage($"--- [Bắt đầu tiến trình sequence {actualFlows} luồng ({keywordsList.Count} từ khóa mục tiêu)] ---");

                var accountQueue = new System.Collections.Concurrent.ConcurrentQueue<AccountInfo>(availableAccounts);
                var keywordLock = new object();
                var workerLock = new object();
                var activeFlowIndices = new HashSet<int>();
                var flowTasks = new List<Task>();

                KeywordInfo? ClaimNextKeyword()
                {
                    lock (keywordLock)
                    {
                        var kw = GlobalState.Keywords.FirstOrDefault(k =>
                            _activeTargetKws.ContainsKey(k.Keyword) &&
                            !_currentExcludedKws.ContainsKey(k.Keyword) &&
                            k.Status == "Chờ quét");
                        if (kw != null)
                        {
                            kw.Status = "Đang quét";
                            DataManager.UpdateKeywordProgress(kw.Keyword, kw.CurrentPage, kw.TotalPages, kw.ProductCount, "Đang quét");
                            RefreshKeywordsGrid();
                            return kw;
                        }
                        return null;
                    }
                }

                void ReleaseKeywordBack(KeywordInfo kw)
                {
                    lock (keywordLock)
                    {
                        if (kw.Status == "Đang quét")
                        {
                            kw.Status = "Chờ quét";
                            DataManager.UpdateKeywordProgress(kw.Keyword, kw.CurrentPage, kw.TotalPages, kw.ProductCount, "Chờ quét");
                            RefreshKeywordsGrid();
                        }
                    }
                }

                bool HasAnyActiveOrPendingKeywords()
                {
                    lock (keywordLock)
                    {
                        return GlobalState.Keywords.Any(k =>
                            _activeTargetKws.ContainsKey(k.Keyword) &&
                            !_currentExcludedKws.ContainsKey(k.Keyword) &&
                            (k.Status == "Chờ quét" || k.Status == "Đang quét"));
                    }
                }

                async Task RunKeywordWorkerAsync(int flowIndex, ProxyInfo? assignedProxy)
                {
                    string flowTag = $"[Flow #{flowIndex + 1}]";
                    AccountInfo? currentAccount = null;

                    while (!_isStopRequested)
                    {
                        var keyword = ClaimNextKeyword();
                        if (keyword == null)
                        {
                            if (HasAnyActiveOrPendingKeywords())
                            {
                                await Task.Delay(2500);
                                if (_isStopRequested) break;
                                keyword = ClaimNextKeyword();
                            }

                            if (keyword == null)
                            {
                                LogMessage($"{flowTag} 🏁 Không còn từ khóa nào cần quét. Flow kết thúc.");
                                break;
                            }
                        }

                        if (currentAccount == null || currentAccount.Status == "Captcha" || currentAccount.Status == "Wrong U/P")
                        {
                            if (!accountQueue.TryDequeue(out currentAccount))
                            {
                                LogMessage($"{flowTag} ⚠️ Hết tài khoản khả dụng trong hàng đợi!");
                                ReleaseKeywordBack(keyword);
                                break;
                            }

                            currentAccount.Status = "Running";
                            DataManager.UpdateAccountStatus(currentAccount.Id, "Running");
                            RefreshAccountsGrid();
                            LogMessage($"{flowTag} 👤 Kích hoạt tài khoản: {currentAccount.Username}");
                        }

                        string proxyAddress = string.Empty;
                        if (assignedProxy != null && !string.IsNullOrWhiteSpace(assignedProxy.ApiKey))
                        {
                            LogMessage($"{flowTag} [Proxy] Kiểm tra trạng thái proxy ({assignedProxy.Name})...");
                            var proxyInfo = await KiotProxyClient.FetchOrRefreshProxyAsync(
                                assignedProxy.ApiKey,
                                safeSecondsThreshold: 180,
                                logCallback: msg => LogMessage($"{flowTag} {msg}"),
                                isStopRequested: () => _isStopRequested);

                            if (_isStopRequested) break;

                            if (!proxyInfo.Success || string.IsNullOrWhiteSpace(proxyInfo.Http))
                            {
                                LogMessage($"{flowTag} [Proxy] ❌ Không lấy được proxy từ key {assignedProxy.Name} ({proxyInfo.ErrorMessage}).");
                                LogMessage($"{flowTag} [Proxy] 🛑 DỪNG FLOW ĐỂ TRÁNH LỘ IP MÁY THẬT GÂY CAPTCHA!");
                                ReleaseKeywordBack(keyword);
                                break;
                            }

                            LogMessage($"{flowTag} [Proxy] Đang kiểm tra kết nối mạng qua {proxyInfo.Http}...");
                            var (isAlive, publicIp) = await KiotProxyClient.CheckProxyAliveAsync(proxyInfo.Http, timeoutSeconds: 8);
                            if (!isAlive && !_isStopRequested)
                            {
                                LogMessage($"{flowTag} [Proxy] ⚠️ Proxy {proxyInfo.Http} không phản hồi mạng. Đang cấp lại proxy mới...");
                                var refreshedProxy = await KiotProxyClient.FetchOrRefreshProxyAsync(
                                    assignedProxy.ApiKey,
                                    safeSecondsThreshold: 999999,
                                    logCallback: msg => LogMessage($"{flowTag} {msg}"),
                                    isStopRequested: () => _isStopRequested);

                                if (refreshedProxy.Success && !string.IsNullOrWhiteSpace(refreshedProxy.Http))
                                {
                                    var (alive2, publicIp2) = await KiotProxyClient.CheckProxyAliveAsync(refreshedProxy.Http, timeoutSeconds: 8);
                                    if (alive2)
                                    {
                                        proxyInfo = refreshedProxy;
                                        isAlive = true;
                                        publicIp = publicIp2;
                                        LogMessage($"{flowTag} [Proxy] 🎉 Đổi IP mới thành công: {refreshedProxy.Http} (IP: {publicIp2}, Hạn còn {refreshedProxy.SecondsRemaining}s)");
                                    }
                                }
                            }

                            if (!isAlive)
                            {
                                if (_isStopRequested) break;
                                LogMessage($"{flowTag} [Proxy] ❌ Proxy {proxyInfo.Http} không phản hồi mạng (Chết hoặc nghẽn)! DỪNG FLOW ĐỂ TRÁNH LỘ IP MÁY.");
                                ReleaseKeywordBack(keyword);
                                break;
                            }

                            proxyAddress = proxyInfo.Http;
                            LogMessage($"{flowTag} [Proxy] 🌐 Proxy hoạt động tốt! IP Public: {publicIp} (Hạn còn {proxyInfo.SecondsRemaining}s)");
                        }
                        else
                        {
                            LogMessage($"{flowTag} [Proxy] ⚠️ Chạy trực tiếp qua IP máy.");
                        }

                        LogMessage($"{flowTag} 🔍 Bắt đầu tìm kiếm từ khóa: \"{keyword.Keyword}\"");
                        var result = await _automation.RunKeywordTaskAsync(
                            currentAccount,
                            proxyAddress,
                            new List<KeywordInfo> { keyword },
                            msg => LogMessage($"{flowTag} {msg}"),
                            session =>
                            {
                                _activeKeywordSessions[keyword.Keyword] = session;
                                if (_manuallyStoppedKws.ContainsKey(keyword.Keyword))
                                {
                                    try { session.Stop(); } catch { }
                                }
                            });

                        _activeKeywordSessions.TryRemove(keyword.Keyword, out _);

                        if (_isStopRequested)
                        {
                            if (keyword.Status != "Hoàn thành")
                            {
                                ReleaseKeywordBack(keyword);
                            }
                            break;
                        }

                        if (_manuallyStoppedKws.TryRemove(keyword.Keyword, out _))
                        {
                            LogMessage($"{flowTag} 🛑 Từ khóa '{keyword.Keyword}' đã được dừng thủ công.");
                            await Task.Delay(1000);
                            continue;
                        }

                        if (result == AutomationResult.Captcha)
                        {
                            currentAccount.Status = "Captcha";
                            DataManager.UpdateAccountStatus(currentAccount.Id, "Captcha");
                            RefreshAccountsGrid();
                            LogMessage($"{flowTag} ⚠️ Tài khoản {currentAccount.Username} dính Captcha!");
                            ReleaseKeywordBack(keyword);
                            currentAccount = null;
                            await Task.Delay(2000);
                        }
                        else if (result == AutomationResult.WrongPassword)
                        {
                            currentAccount.Status = "Wrong U/P";
                            DataManager.UpdateAccountStatus(currentAccount.Id, "Wrong U/P");
                            RefreshAccountsGrid();
                            LogMessage($"{flowTag} ❌ Tài khoản {currentAccount.Username} sai Username/Password!");
                            ReleaseKeywordBack(keyword);
                            currentAccount = null;
                            await Task.Delay(2000);
                        }
                        else if (result == AutomationResult.Success)
                        {
                            LogMessage($"{flowTag} ✅ Hoàn thành từ khóa: \"{keyword.Keyword}\"!");
                            RefreshKeywordsGrid();
                            UpdateStats();
                        }
                        else
                        {
                            LogMessage($"{flowTag} ⚠️ Phiên quét từ khóa kết thúc với trạng thái {result}.");
                            if (keyword.Status != "Hoàn thành")
                            {
                                ReleaseKeywordBack(keyword);
                            }
                            await Task.Delay(2000);
                        }
                    }

                    if (currentAccount != null && currentAccount.Status == "Running")
                    {
                        currentAccount.Status = "Done";
                        DataManager.UpdateAccountStatus(currentAccount.Id, "Done");
                        RefreshAccountsGrid();
                    }
                }

                void TrySpawnAdditionalKeywordWorkers()
                {
                    lock (workerLock)
                    {
                        if (_isStopRequested) return;

                        int pendingCount = GlobalState.Keywords.Count(k =>
                            _activeTargetKws.ContainsKey(k.Keyword) &&
                            !_currentExcludedKws.ContainsKey(k.Keyword) &&
                            (k.Status == "Chờ quét" || k.Status == "Đang quét"));

                        int maxPossible = proxyCount == 0
                            ? 1
                            : Math.Min(pendingCount, Math.Min(configFlows, Math.Min(proxyCount, accountCount)));

                        for (int i = 0; i < maxPossible; i++)
                        {
                            if (!activeFlowIndices.Contains(i))
                            {
                                activeFlowIndices.Add(i);
                                int flowIdx = i;
                                var assignedProxy = proxyCount > 0 ? availableProxies[flowIdx] : null;
                                var task = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await RunKeywordWorkerAsync(flowIdx, assignedProxy);
                                    }
                                    finally
                                    {
                                        lock (workerLock)
                                        {
                                            activeFlowIndices.Remove(flowIdx);
                                        }
                                    }
                                });
                                flowTasks.Add(task);
                            }
                        }
                    }
                }

                _spawnAdditionalKeywordWorkersAction = TrySpawnAdditionalKeywordWorkers;
                TrySpawnAdditionalKeywordWorkers();

                while (true)
                {
                    Task[] currentTasks;
                    lock (workerLock)
                    {
                        currentTasks = flowTasks.Where(t => !t.IsCompleted).ToArray();
                    }
                    if (currentTasks.Length == 0) break;
                    await Task.WhenAny(currentTasks);
                }

                LogMessage("--- [Tiến trình sequence từ khóa đa luồng đã kết thúc] ---");
            }
            catch (Exception ex)
            {
                LogMessage("[System] Lỗi sequence từ khóa: " + ex.Message);
            }
            finally
            {
                _spawnAdditionalKeywordWorkersAction = null;
                _activeKeywordSessions.Clear();
                _manuallyStoppedKws.Clear();
                _currentExcludedKws.Clear();
                _activeTargetKws.Clear();
                SetRunningUiState(false);
                UpdateStats();
                RefreshKeywordsGrid();
                RefreshAccountsGrid();
            }
        }

        private void DgvKeywords_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.RowIndex >= GlobalState.Keywords.Count) return;
            if (dgvKeywords.Columns[e.ColumnIndex].DataPropertyName != "ProgressDisplay") return;

            var kw = GlobalState.Keywords[e.RowIndex];

            e.PaintBackground(e.CellBounds, true);

            string glyph;
            Color iconColor;
            Color textColor;
            Font textFont = dgvKeywords.Font;
            string text;

            string subText = kw.TotalPages > 0
                ? $"Trang {kw.CurrentPage}/{kw.TotalPages}"
                : $"Trang {kw.CurrentPage}";

            if (kw.Status == "Đang quét")
            {
                glyph = "\uE72C"; // Rotating sync arrows
                iconColor = UiTheme.Accent;
                textColor = UiTheme.Accent;
                textFont = _boldCatFont;
                text = $"Đang quét: {subText}";
            }
            else if (kw.Status == "Hoàn thành")
            {
                glyph = "\uE73E"; // Checkmark
                iconColor = UiTheme.Success;
                textColor = UiTheme.Success;
                text = "Hoàn thành";
            }
            else if (kw.CurrentPage > 1)
            {
                glyph = "\uE769"; // Pause
                iconColor = UiTheme.Warning;
                textColor = UiTheme.Warning;
                text = $"Tạm dừng: {subText}";
            }
            else
            {
                glyph = "\uE738"; // Dot outline
                iconColor = UiTheme.TextMuted;
                textColor = UiTheme.TextMuted;
                text = "Chưa quét";
            }

            if (e.Graphics == null) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int padLeft = e.CellBounds.Left + 10;
            int cellH = e.CellBounds.Height;
            int cellTop = e.CellBounds.Top;

            if (!string.IsNullOrEmpty(glyph))
            {
                Rectangle iconRect = new Rectangle(padLeft, cellTop, 18, cellH);
                TextRenderer.DrawText(g, glyph, _mdl2CatFont, iconRect, iconColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                padLeft += 22;
            }

            int textW = Math.Max(10, e.CellBounds.Right - padLeft - 6);
            Rectangle textRect = new Rectangle(padLeft, cellTop, textW, cellH);
            TextRenderer.DrawText(g, text, textFont, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            using (var borderPen = new Pen(UiTheme.BorderLightest, 1))
            {
                g.DrawLine(borderPen, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
            }

            e.Handled = true;
        }
    }
}
