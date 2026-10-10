using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ShopeeBackend.Views;

namespace ShopeeBackend
{
    public partial class MainForm : Form
    {
        private Panel panelTabStrip = null!;
        private Panel panelMain    = null!;
        private Panel panelStatus  = null!;
        private const int TabStripHeight = 48;
        private const int StatusBarHeight = 32;

        private UserControl viewWorkspace = null!;
        private UserControl viewProducts  = null!;
        private UserControl viewAccounts  = null!;
        private UserControl viewProxies   = null!;
        private UserControl viewSettings  = null!;

        private List<TabButton> tabButtons  = new List<TabButton>();
        private TabButton?      currentTab  = null;

        public MainForm()
        {
            try
            {
                this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            ShopeeBackend.Services.DataManager.LoadData();

            this.Text          = "Phân tích Shopee - Automation & Analytics";
            this.AutoScaleMode = AutoScaleMode.None;

            int defaultW = 2210;
            int defaultH = 1336;

            var (savedW, savedH, savedMax) = ShopeeBackend.Services.DataManager.GetWindowBounds(defaultW, defaultH);
            var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 2560, 1440);
            savedW = Math.Clamp(savedW, 1280, workingArea.Width);
            savedH = Math.Clamp(savedH, 750, workingArea.Height);

            this.Size          = new Size(savedW, savedH);
            this.MinimumSize   = new Size(1280, 750);
            this.StartPosition = FormStartPosition.CenterScreen;
            if (savedMax)
            {
                this.WindowState = FormWindowState.Maximized;
            }

            this.Font          = UiTheme.FontBody;
            this.BackColor     = UiTheme.WindowBg;

            this.ResizeEnd += (s, e) =>
            {
                if (this.WindowState == FormWindowState.Normal)
                {
                    ShopeeBackend.Services.DataManager.SaveWindowBounds(this.Width, this.Height, false);
                }
            };

            this.FormClosing += (s, e) =>
            {
                this.Validate();
                bool isMax = this.WindowState == FormWindowState.Maximized;
                int w = isMax ? this.RestoreBounds.Width : this.Width;
                int h = isMax ? this.RestoreBounds.Height : this.Height;
                ShopeeBackend.Services.DataManager.SaveWindowBounds(w, h, isMax);
                ShopeeBackend.Services.DataManager.SaveData();
                (viewWorkspace as WorkspaceView)?.StopAutomation();
                ShopeeBackend.Services.BraveAutomation.KillAppBraveProcesses();
            };

            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                ShopeeBackend.Services.BraveAutomation.KillAppBraveProcesses();
            };

            // Initialize Views
            viewWorkspace = new WorkspaceView(this);
            viewProducts  = new ProductDataView();
            viewAccounts  = new AccountView();
            viewProxies   = new ProxyView();
            viewSettings  = new SettingsView();

            // Không dùng Dock cho 3 vùng này: Fill nằm trên cùng sẽ phủ tab
            // và làm tiêu đề trang chui xuống dưới dải tab.
            SetupStatusBar();
            SetupTabStrip();

            GlobalState.Accounts.ListChanged        += (s, e) => UpdateStatusBarStats();
            GlobalState.Categories.ListChanged      += (s, e) => UpdateStatusBarStats();
            GlobalState.Keywords.ListChanged        += (s, e) => UpdateStatusBarStats();
            GlobalState.ScrapedProducts.ListChanged += (s, e) => UpdateStatusBarStats();
            UpdateStatusBarStats();

            panelMain = new Panel
            {
                BackColor = UiTheme.WindowBg,
                Padding   = new Padding(16)
            };
            this.Controls.Add(panelMain);
            this.Controls.Add(panelTabStrip);
            this.Controls.Add(panelStatus);
            this.Resize += (s, e) => LayoutChrome();
            LayoutChrome();

            // Activate initial view from command-line args
            string[] args = Environment.GetCommandLineArgs();
            int viewIdx = 0;
            if (args.Length > 1)
            {
                if      (args[1] == "--keywords") viewIdx = 1;
                else if (args[1] == "--products") viewIdx = 2;
                else if (args[1] == "--accounts") viewIdx = 3;
                else if (args[1] == "--proxies")  viewIdx = 4;
                else if (args[1] == "--settings") viewIdx = 5;
            }

            if (tabButtons.Count > viewIdx)
            {
                UserControl target = viewIdx switch
                {
                    0 => viewWorkspace,
                    1 => viewWorkspace,
                    2 => viewProducts,
                    3 => viewAccounts,
                    4 => viewProxies,
                    5 => viewSettings,
                    _ => viewWorkspace
                };
                ActivateTab(tabButtons[viewIdx], target);
                if (viewIdx == 0 || viewIdx == 1)
                {
                    (viewWorkspace as WorkspaceView)?.SelectTab(viewIdx, notifyMain: false);
                }
            }
        }

        // ── Tab strip ────────────────────────────────────────────────────
        private void SetupTabStrip()
        {
            panelTabStrip = new Panel
            {
                Height    = TabStripHeight,
                BackColor = Color.White
            };

            // 1px border at bottom
            var stripBorder = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 1,
                BackColor = UiTheme.BorderStrong
            };
            panelTabStrip.Controls.Add(stripBorder);

            // Brand logo area (left, custom-painted)
            var brandPanel = BuildBrandPanel();
            panelTabStrip.Controls.Add(brandPanel);

            // Tab buttons in a FlowLayoutPanel
            var flowTabs = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents  = false,
                BackColor     = Color.White,
                AutoScroll    = false,
                Padding       = new Padding(0),
                Margin        = new Padding(0)
            };
            panelTabStrip.Controls.Add(flowTabs);
            panelTabStrip.Resize += (s, e) => LayoutTabStrip(brandPanel, flowTabs);

            // Create tab buttons (Category search and Keyword search are tab 0 and tab 1)
            AddTabButton(flowTabs, "Search theo danh m\u1EE5c", viewWorkspace, () => (viewWorkspace as WorkspaceView)?.SelectTab(0, notifyMain: false));
            AddTabButton(flowTabs, "Search theo t\u1EEB kh\u00F3a",  viewWorkspace, () => (viewWorkspace as WorkspaceView)?.SelectTab(1, notifyMain: false));
            AddTabButton(flowTabs, "D\u1EEF li\u1EC7u s\u1EA3n ph\u1EA9m", viewProducts);
            AddTabButton(flowTabs, "T\u00E0i kho\u1EA3n",        viewAccounts);
            AddTabButton(flowTabs, "Proxy",                      viewProxies);
            AddTabButton(flowTabs, "C\u00E0i \u0111\u1EB7t",    viewSettings);
        }

        private Panel BuildBrandPanel()
        {
            var measureFont = UiTheme.FontBody;
            int textW = TextRenderer.MeasureText("Phân tích Shopee", measureFont).Width;
            var pnl = new Panel
            {
                Width     = 16 + 20 + 9 + textW + 18,
                BackColor = Color.White,
                Cursor    = Cursors.Hand
            };

            pnl.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // Orange square 20×20 bo 4
                var squareRect = new Rectangle(16, 14, 20, 20);
                using var squarePath = UiTheme.GetRoundedPath(squareRect, 4);
                using var squareBrush = new SolidBrush(UiTheme.Accent);
                g.FillPath(squareBrush, squarePath);

                // Brand text
                var font = UiTheme.FontBody;
                TextRenderer.DrawText(g, "Ph\u00E2n t\u00EDch Shopee", font,
                    new Rectangle(45, 0, Math.Max(0, pnl.Width - 50), pnl.Height),
                    UiTheme.TextPrimary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            };

            pnl.Click += (s, e) =>
            {
                if (tabButtons.Count > 0)
                {
                    ActivateTab(tabButtons[0], viewWorkspace);
                    (viewWorkspace as WorkspaceView)?.SelectTab(0, notifyMain: false);
                }
            };
            return pnl;
        }

        private void AddTabButton(FlowLayoutPanel container, string text, UserControl targetView, Action? onActivated = null)
        {
            var btn = new TabButton { Text = text, Height = 47 };
            btn.Click += (s, e) =>
            {
                ActivateTab(btn, targetView);
                onActivated?.Invoke();
            };
            container.Controls.Add(btn);
            tabButtons.Add(btn);
        }

        public void SyncTopTab(int tabIndex)
        {
            if (tabIndex >= 0 && tabIndex < tabButtons.Count)
            {
                var targetBtn = tabButtons[tabIndex];
                if (currentTab != targetBtn)
                {
                    currentTab?.SetSelected(false);
                    currentTab = targetBtn;
                    currentTab.SetSelected(true);
                }
            }
        }

        private void ActivateTab(TabButton btn, UserControl targetView)
        {
            if (currentTab != null) currentTab.SetSelected(false);
            currentTab = btn;
            btn.SetSelected(true);

            panelMain.Controls.Clear();
            targetView.Dock = DockStyle.Fill;
            panelMain.Controls.Add(targetView);

            if (targetView is ProductDataView pdv) pdv.ReloadData();
        }

        // ── Status bar ───────────────────────────────────────────────────
        private Label lblRightStatus = null!;

        private void SetupStatusBar()
        {
            panelStatus = new Panel
            {
                Height    = StatusBarHeight,
                BackColor = ColorTranslator.FromHtml("#F2EFEC")
            };

            // 1px border at top
            var statusBorder = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 1,
                BackColor = UiTheme.BorderStrong
            };
            panelStatus.Controls.Add(statusBorder);

            // Content area
            var content = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = ColorTranslator.FromHtml("#F2EFEC")
            };

            lblRightStatus = new Label
            {
                Dock         = DockStyle.Right,
                AutoSize     = false,
                Width        = 680,
                TextAlign    = ContentAlignment.MiddleRight,
                ForeColor    = ColorTranslator.FromHtml("#675F5A"),
                Font         = UiTheme.FontCaption,
                BackColor    = Color.Transparent,
                Padding      = new Padding(0, 0, 16, 0)
            };
            content.Controls.Add(lblRightStatus);

            content.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int dotY = (content.Height - 8) / 2;
                using var dotBrush = new SolidBrush(UiTheme.Success);
                g.FillEllipse(dotBrush, 12, dotY, 8, 8);
                var font = UiTheme.FontCaption;
                TextRenderer.DrawText(g, "Database: S\u1EB5n s\u00E0ng", font,
                    new Rectangle(28, 0, Math.Max(100, content.Width - lblRightStatus.Width - 32), content.Height),
                    ColorTranslator.FromHtml("#675F5A"),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
            };
            panelStatus.Controls.Add(content);
        }

        public void UpdateStatusBarStats()
        {
            if (lblRightStatus == null) return;
            if (lblRightStatus.InvokeRequired)
            {
                lblRightStatus.Invoke(new Action(UpdateStatusBarStats));
                return;
            }

            int totalAcc    = GlobalState.Accounts.Count;
            int readyAcc    = GlobalState.Accounts.Count(a => a.Status == "Ready");
            int badAcc      = GlobalState.Accounts.Count(a => a.Status == "Captcha" || a.Status == "Wrong U/P");
            int totalCats   = GlobalState.Categories.Count;
            int totalKws    = GlobalState.Keywords.Count;
            int totalProds  = GlobalState.ScrapedProducts.Count;

            lblRightStatus.Text = $"T\u00E0i kho\u1EA3n: {totalAcc} (S\u1EB5n s\u00E0ng: {readyAcc} \u00B7 L\u1ED7i: {badAcc})" +
                                  $"   |   Danh m\u1EE5c: {totalCats}   |   T\u1EEB kh\u00F3a: {totalKws}   |   \u0110\u00E3 qu\u00E9t: {totalProds:N0} sp";
        }

        private void LayoutChrome()
        {
            if (panelTabStrip == null || panelStatus == null || panelMain == null) return;
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;
            panelTabStrip.SetBounds(0, 0, w, TabStripHeight);
            panelStatus.SetBounds(0, Math.Max(TabStripHeight, h - StatusBarHeight), w, StatusBarHeight);
            int contentTop = TabStripHeight;
            int contentH = Math.Max(0, h - TabStripHeight - StatusBarHeight);
            panelMain.SetBounds(0, contentTop, w, contentH);
            panelTabStrip.BringToFront();
            panelStatus.BringToFront();
        }

        private static void LayoutTabStrip(Panel brand, FlowLayoutPanel tabs)
        {
            int h = Math.Max(0, brand.Parent!.Height - 1);
            brand.SetBounds(0, 0, brand.Width, h);
            tabs.SetBounds(brand.Right, 0, Math.Max(0, brand.Parent.Width - brand.Width), h);
        }

        // ── Public navigation ─────────────────────────────────────────────
        public void NavigateToProducts()
        {
            if (tabButtons.Count > 2)
                ActivateTab(tabButtons[2], viewProducts);
        }
    }
}
