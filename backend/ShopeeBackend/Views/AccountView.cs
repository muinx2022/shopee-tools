using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ShopeeBackend.Models;
using ShopeeBackend.Services;

namespace ShopeeBackend.Views
{
    public class AccountView : UserControl
    {
        private DataGridView dgv      = null!;
        private SuiteField   txtSearch = null!;
        private Label        lblCount  = null!;

        public AccountView()
        {
            this.Dock      = DockStyle.Fill;
            this.BackColor = UiTheme.WindowBg;
            this.Font      = UiTheme.FontBody;

            InitializeLayout();
            UpdateCount();
            GlobalState.Accounts.ListChanged += (s, e) => UpdateCount();
        }

        private void InitializeLayout()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.Size          = new Size(1140, 740);

            Panel pnlHead = UiTheme.CreatePageHead(
                "Qu\u1EA3n l\u00FD t\u00E0i kho\u1EA3n",
                "Danh s\u00E1ch t\u00E0i kho\u1EA3n \u0111\u0103ng nh\u1EADp Shopee, cookie SPC_F v\u00E0 h\u1ED3 s\u01A1 Profile \u0111\u1ED9c l\u1EADp.");
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

            txtSearch = new SuiteField
            {
                Location        = new Point(14, 12),
                Width           = 180,
                PlaceholderText = "T\u00ECm t\u00E0i kho\u1EA3n"
            };
            txtSearch.TextChanged += TxtSearch_TextChanged;
            pnlToolbar.Controls.Add(txtSearch);

            RoundedButton btnAdd = new RoundedButton
            {
                IconGlyph = "\uE716",   // Person/add user
                IconRole  = ButtonIconRole.Info,
                Text      = "Th\u00EAm t\u00E0i kho\u1EA3n"
            };
            btnAdd.Click += BtnAdd_Click;
            pnlToolbar.Controls.Add(btnAdd);

            RoundedButton btnResetStatus = new RoundedButton
            {
                IconGlyph = "\uE72C",   // Refresh
                IconRole  = ButtonIconRole.Success,
                Text      = "\u0110\u1EB7t l\u1EA1i tr\u1EA1ng th\u00E1i"
            };
            btnResetStatus.Click += (s, e) =>
            {
                if (SuiteDialog.Confirm(this,
                    "X\u00E1c nh\u1EADn",
                    "B\u1EA1n c\u00F3 mu\u1ED1n \u0111\u1EB7t l\u1EA1i tr\u1EA1ng th\u00E1i c\u1EE7a t\u1EA5t c\u1EA3 t\u00E0i kho\u1EA3n v\u1EC1 'Ready' kh\u00F4ng?",
                    "\u0110\u1EB7t l\u1EA1i", DialogTone.Danger) == DialogResult.Yes)
                {
                    foreach (var acc in GlobalState.Accounts)
                    {
                        acc.Status = "Ready";
                        DataManager.UpdateAccountStatus(acc.Id, "Ready");
                    }
                    GlobalState.Accounts.ResetBindings();
                }
            };
            pnlToolbar.Controls.Add(btnResetStatus);

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
                Text      = "0 t\u00E0i kho\u1EA3n",
                AutoSize  = true,
                ForeColor = UiTheme.TextSecondary,
                Font      = UiTheme.FontBody
            };
            pnlToolbar.Controls.Add(lblCount);

            void UpdateToolbarLayout()
            {
                btnAdd.Location         = new Point(txtSearch.Right + 14, 12);
                btnResetStatus.Location = new Point(btnAdd.Right + 12, 12);
                btnDelete.Location      = new Point(btnResetStatus.Right + 12, 12);
                int desiredX = pnlToolbar.Width - lblCount.Width - 18;
                lblCount.Location = new Point(Math.Max(btnDelete.Right + 16, desiredX),
                    (pnlToolbar.Height - lblCount.Height) / 2);
            }
            pnlToolbar.Resize           += (s, e) => UpdateToolbarLayout();
            btnAdd.SizeChanged          += (s, e) => UpdateToolbarLayout();
            btnResetStatus.SizeChanged  += (s, e) => UpdateToolbarLayout();
            btnDelete.SizeChanged       += (s, e) => UpdateToolbarLayout();
            lblCount.SizeChanged        += (s, e) => UpdateToolbarLayout();
            lblCount.TextChanged        += (s, e) => UpdateToolbarLayout();
            UpdateToolbarLayout();

            Panel pnlSep = new Panel { Dock = DockStyle.Top, Height = 12, BackColor = UiTheme.WindowBg };

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

            dgv.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Username",    HeaderText = "T\u00EAn \u0111\u0103ng nh\u1EADp", Width = 180 });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Password",    HeaderText = "M\u1EADt kh\u1EA9u",              Width = 150 });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Cookie",      HeaderText = "Cookie (SPC_F)",                  Width = 320 });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "ProfileName", HeaderText = "Profile Brave",                   Width = 180 });
            dgv.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Status",      HeaderText = "Tr\u1EA1ng th\u00E1i", Width = 120, ReadOnly = true });

            dgv.CellFormatting += Dgv_CellFormatting;
            dgv.DataSource      = GlobalState.Accounts;

            pnlGridCard.Controls.Add(dgv);
            bodyPanel.Controls.Add(pnlGridCard);
            bodyPanel.Controls.Add(pnlSep);
            bodyPanel.Controls.Add(pnlToolbar);
        }

        private void Dgv_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.CellStyle != null && dgv.Columns[e.ColumnIndex].DataPropertyName == "Status" && e.Value != null)
            {
                string status = e.Value.ToString() ?? "";
                if      (status == "Ready")    e.CellStyle.ForeColor = UiTheme.Success;
                else if (status == "Running")  e.CellStyle.ForeColor = UiTheme.Info;
                else if (status == "Captcha")  e.CellStyle.ForeColor = UiTheme.Warning;
                else if (status == "Wrong U/P") e.CellStyle.ForeColor = UiTheme.Danger;
            }
        }

        private void TxtSearch_TextChanged(object? sender, EventArgs e)
        {
            string filter = txtSearch.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(filter))
                dgv.DataSource = GlobalState.Accounts;
            else
            {
                var filtered = GlobalState.Accounts.Where(a =>
                    (a.Username != null && a.Username.ToLower().Contains(filter)) ||
                    (a.Status   != null && a.Status.ToLower().Contains(filter))
                ).ToList();
                dgv.DataSource = new System.ComponentModel.BindingList<AccountInfo>(filtered);
            }
        }

        private void UpdateCount()
        {
            if (lblCount != null)
                lblCount.Text = $"{GlobalState.Accounts.Count} t\u00E0i kho\u1EA3n";
        }

        private void BtnAdd_Click(object? sender, EventArgs e)
        {
            GlobalState.Accounts.Add(new AccountInfo
            {
                Username    = "user_" + (GlobalState.Accounts.Count + 1),
                Password    = "password",
                ProfileName = "Profile_user" + (GlobalState.Accounts.Count + 1),
                Status      = "Ready"
            });
            UpdateCount();
        }

        private void BtnDelete_Click(object? sender, EventArgs e)
        {
            if (dgv.SelectedRows.Count > 0)
            {
                if (SuiteDialog.Confirm(this, "X\u00E1c nh\u1EADn x\u00F3a",
                    $"B\u1EA1n c\u00F3 ch\u1EAFc ch\u1EAFn mu\u1ED1n x\u00F3a {dgv.SelectedRows.Count} t\u00E0i kho\u1EA3n \u0111\u00E3 ch\u1ECDn?",
                    "X\u00F3a", DialogTone.Danger) != DialogResult.Yes) return;

                foreach (DataGridViewRow row in dgv.SelectedRows)
                {
                    if (row.DataBoundItem is AccountInfo acc)
                        GlobalState.Accounts.Remove(acc);
                }
                UpdateCount();
            }
        }
    }
}
