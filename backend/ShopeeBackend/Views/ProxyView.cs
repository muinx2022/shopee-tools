using System;
using System.Drawing;
using System.Windows.Forms;
using ShopeeBackend.Models;

namespace ShopeeBackend.Views
{
    public class ProxyView : UserControl
    {
        private DataGridView dgv      = null!;
        private Label        lblCount = null!;

        public ProxyView()
        {
            this.Dock      = DockStyle.Fill;
            this.BackColor = UiTheme.WindowBg;
            this.Font      = UiTheme.FontBody;

            InitializeLayout();
            UpdateCount();
            GlobalState.Proxies.ListChanged += (s, e) => UpdateCount();
        }

        private void InitializeLayout()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.Size          = new Size(1140, 740);

            Panel pnlHead = UiTheme.CreatePageHead(
                "Qu\u1EA3n l\u00FD Kiot Proxy",
                "Kho API Key xoay v\u00F2ng. T\u1EF1 \u0111\u1ED9ng l\u1EA5y IP s\u1EA1ch t\u1EEB KiotProxy tr\u01B0\u1EDBc khi kh\u1EDFi \u0111\u1ED9ng tr\u00ECnh duy\u1EC7t.");
            this.Controls.Add(pnlHead);

            Panel bodyPanel = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = UiTheme.WindowBg,
                Padding   = new Padding(16, 12, 16, 16)
            };
            this.Controls.Add(bodyPanel);
            bodyPanel.BringToFront();

            // Toolbar
            RoundedPanel pnlToolbar = new RoundedPanel
            {
                Dock         = DockStyle.Top,
                Height       = 54,
                CornerRadius = 6,
                BorderColor  = UiTheme.BorderLight,
                Padding      = new Padding(10)
            };

            RoundedButton btnAdd = new RoundedButton
            {
                IconGlyph = "\uE710",   // Add
                IconRole  = ButtonIconRole.Info,
                Text      = "Th\u00EAm proxy key"
            };
            btnAdd.Click += BtnAdd_Click;
            pnlToolbar.Controls.Add(btnAdd);

            RoundedButton btnDelete = new RoundedButton
            {
                IconGlyph = "\uE74D",   // Delete
                IconRole  = ButtonIconRole.Danger,
                Text      = "X\u00F3a \u0111\u00E3 ch\u1ECDn"
            };
            btnDelete.Click += BtnDelete_Click;
            pnlToolbar.Controls.Add(btnDelete);

            lblCount = new Label
            {
                Text      = "0 proxy",
                AutoSize  = true,
                ForeColor = UiTheme.TextSecondary,
                Font      = UiTheme.FontBody
            };
            pnlToolbar.Controls.Add(lblCount);

            void UpdateProxyToolbarLayout()
            {
                btnAdd.Location    = new Point(14, 12);
                btnDelete.Location = new Point(btnAdd.Right + 12, 12);
                lblCount.Location  = new Point(pnlToolbar.Width - lblCount.Width - 14,
                    (pnlToolbar.Height - lblCount.Height) / 2);
            }
            pnlToolbar.Resize      += (s, e) => UpdateProxyToolbarLayout();
            btnAdd.SizeChanged     += (s, e) => UpdateProxyToolbarLayout();
            btnDelete.SizeChanged  += (s, e) => UpdateProxyToolbarLayout();
            lblCount.SizeChanged   += (s, e) => UpdateProxyToolbarLayout();
            UpdateProxyToolbarLayout();

            Panel pnlSepTop    = new Panel { Dock = DockStyle.Top,    Height = 12, BackColor = UiTheme.WindowBg };
            Panel pnlSepBottom = new Panel { Dock = DockStyle.Bottom, Height = 12, BackColor = UiTheme.WindowBg };

            // Hint strip — suite-style warning (no emoji)
            RoundedPanel pnlHint = new RoundedPanel
            {
                Dock         = DockStyle.Bottom,
                Height       = 48,
                BackColor    = ColorTranslator.FromHtml("#FFF6E6"),
                BorderColor  = ColorTranslator.FromHtml("#F0DFC0"),
                CornerRadius = 6,
                Padding      = new Padding(14, 10, 14, 10)
            };
            Label lblHint = new Label
            {
                Text      = "C\u01A1 ch\u1EBF: H\u1EC7 th\u1ED1ng t\u1EF1 \u0111\u1ED9ng g\u1ECDi API KiotProxy \u0111\u1EC3 sinh IP m\u1EDBi tr\u01B0\u1EDBc m\u1ED7i phi\u00EAn c\u00E0o danh m\u1EE5c. N\u1EBFu danh s\u00E1ch tr\u1ED1ng, tr\u00ECnh duy\u1EC7t s\u1EBD ch\u1EA1y b\u1EB1ng IP m\u1EA1ng g\u1ED1c c\u1EE7a m\u00E1y.",
                Font      = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = UiTheme.TextSecondary,
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlHint.Controls.Add(lblHint);

            // Grid card
            RoundedPanel pnlGridCard = new RoundedPanel
            {
                Dock         = DockStyle.Fill,
                CornerRadius = 6,
                BorderColor  = UiTheme.BorderLight,
                Padding      = new Padding(10)
            };

            dgv = new DataGridView
            {
                Dock               = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                SelectionMode      = DataGridViewSelectionMode.FullRowSelect
            };
            UiTheme.StyleSuiteDataGrid(dgv);

            dgv.Columns.Add(new DataGridViewTextBoxColumn
                { DataPropertyName = "Name",   HeaderText = "T\u00EAn g\u1EE3i nh\u1EDB / nh\u00E3n", Width = 220 });
            dgv.Columns.Add(new DataGridViewTextBoxColumn
                { DataPropertyName = "ApiKey", HeaderText = "Kiot API Key",
                  AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });

            dgv.DataSource = GlobalState.Proxies;
            pnlGridCard.Controls.Add(dgv);

            bodyPanel.Controls.Add(pnlGridCard);
            bodyPanel.Controls.Add(pnlSepBottom);
            bodyPanel.Controls.Add(pnlHint);
            bodyPanel.Controls.Add(pnlSepTop);
            bodyPanel.Controls.Add(pnlToolbar);
        }

        private void UpdateCount()
        {
            if (lblCount != null)
                lblCount.Text = $"{GlobalState.Proxies.Count} proxy key";
        }

        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            GlobalState.Proxies.Add(new ProxyInfo
            {
                Name   = "Kiot Proxy " + (GlobalState.Proxies.Count + 1),
                ApiKey = "Nh\u1EADp_Kiot_Key_T\u1EA1i_\u0110\u00E2y..."
            });
            UpdateCount();
        }

        private void BtnDelete_Click(object? sender, EventArgs e)
        {
            if (dgv.SelectedRows.Count > 0)
            {
                if (SuiteDialog.Confirm(this, "X\u00E1c nh\u1EADn x\u00F3a",
                    $"B\u1EA1n c\u00F3 ch\u1EAFc ch\u1EAFn mu\u1ED1n x\u00F3a {dgv.SelectedRows.Count} proxy \u0111\u00E3 ch\u1ECDn kh\u00F4ng?",
                    "X\u00F3a", DialogTone.Danger) != DialogResult.Yes) return;

                foreach (DataGridViewRow row in dgv.SelectedRows)
                {
                    if (row.DataBoundItem is ProxyInfo px)
                        GlobalState.Proxies.Remove(px);
                }
                UpdateCount();
            }
        }
    }
}
