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

            RoundedButton btnImport = new RoundedButton
            {
                IconGlyph = "\uE8B7",   // Download/Import
                IconRole  = ButtonIconRole.Primary,
                Text      = "Nh\u1EADp danh s\u00E1ch"
            };
            btnImport.Click += BtnImport_Click;
            pnlToolbar.Controls.Add(btnImport);

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
                btnImport.Location      = new Point(btnAdd.Right + 12, 12);
                btnResetStatus.Location = new Point(btnImport.Right + 12, 12);
                btnDelete.Location      = new Point(btnResetStatus.Right + 12, 12);
                int desiredX = pnlToolbar.Width - lblCount.Width - 18;
                lblCount.Location = new Point(Math.Max(btnDelete.Right + 16, desiredX),
                    (pnlToolbar.Height - lblCount.Height) / 2);
            }
            pnlToolbar.Resize           += (s, e) => UpdateToolbarLayout();
            btnAdd.SizeChanged          += (s, e) => UpdateToolbarLayout();
            btnImport.SizeChanged       += (s, e) => UpdateToolbarLayout();
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

        private void BtnImport_Click(object? sender, EventArgs e)
        {
            using var dlg = new AccountImportDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                if (string.IsNullOrEmpty(txtSearch.Text.Trim()))
                {
                    dgv.DataSource = null;
                    dgv.DataSource = GlobalState.Accounts;
                }
                else
                {
                    TxtSearch_TextChanged(null, EventArgs.Empty);
                }
                UpdateCount();
            }
        }
    }

    public class AccountImportDialog : Form
    {
        private readonly SuiteTextArea txtInput;
        private readonly RoundedButton btnPasteClipboard;
        private readonly RoundedButton btnClear;
        private readonly RoundedButton btnConfirm;
        private readonly RoundedButton btnCancel;
        private readonly Label         lblStatus;

        public AccountImportDialog()
        {
            this.Text            = "Nh\u1EADp danh s\u00E1ch t\u00E0i kho\u1EA3n";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox     = false;
            this.MinimizeBox     = false;
            this.ShowIcon        = false;
            this.ShowInTaskbar   = false;
            this.StartPosition   = FormStartPosition.CenterParent;
            this.BackColor       = UiTheme.WindowBg;
            this.Font            = UiTheme.FontBody;
            this.ClientSize      = new Size(660, 520);
            this.KeyPreview      = true;

            var card = new RoundedPanel
            {
                Location     = new Point(16, 16),
                Size         = new Size(628, 488),
                CornerRadius = 6,
                BackColor    = UiTheme.CardBg,
                BorderColor  = UiTheme.BorderLight,
                Padding      = new Padding(16)
            };
            this.Controls.Add(card);

            var lblTitle = new Label
            {
                Text      = "Nh\u1EADp danh s\u00E1ch t\u00E0i kho\u1EA3n",
                Font      = UiTheme.FontTitle,
                ForeColor = UiTheme.TextPrimary,
                Location  = new Point(16, 14),
                AutoSize  = true
            };
            card.Controls.Add(lblTitle);

            var lblDesc = new Label
            {
                Text      = "\u0110\u1ECBnh d\u1EA1ng: username|password|cookie (ho\u1EB7c username|password, username\\tpassword\\tcookie). M\u1ED7i d\u00F2ng m\u1ED9t t\u00E0i kho\u1EA3n.",
                Font      = UiTheme.FontCaption,
                ForeColor = UiTheme.TextSecondary,
                Location  = new Point(16, 42),
                Size      = new Size(596, 20),
                AutoSize  = false
            };
            card.Controls.Add(lblDesc);

            txtInput = new SuiteTextArea
            {
                Location        = new Point(16, 106),
                Size            = new Size(596, 326),
                PlaceholderText = "username|password|.shopee.vn=SPC_F=...\nusername|password\n..."
            };
            card.Controls.Add(txtInput);

            btnPasteClipboard = new RoundedButton
            {
                IconGlyph = "\uE77F", // Clipboard
                IconRole  = ButtonIconRole.Info,
                Text      = "D\u00E1n t\u1EEB Clipboard",
                Location  = new Point(16, 68),
                Height    = 30
            };
            btnPasteClipboard.Click += BtnPasteClipboard_Click;
            card.Controls.Add(btnPasteClipboard);

            btnClear = new RoundedButton
            {
                IconGlyph = "\uE74D", // Delete
                IconRole  = ButtonIconRole.Muted,
                Text      = "X\u00F3a n\u1ED9i dung",
                Height    = 30
            };
            btnClear.Click += (s, e) => txtInput.Clear();
            card.Controls.Add(btnClear);

            if (txtInput.Controls.Count > 0 && txtInput.Controls[0] is TextBox tb)
            {
                tb.AcceptsReturn = true;
                tb.AcceptsTab    = false;
                tb.KeyDown += (s, e) =>
                {
                    if (e.Control && e.KeyCode == Keys.A)
                    {
                        tb.SelectAll();
                        e.SuppressKeyPress = true;
                    }
                };
            }

            lblStatus = new Label
            {
                Text      = "H\u1ED7 tr\u1EE3 t\u1EF1 \u0111\u1ED9ng th\u00EAm m\u1EDBi ho\u1EB7c c\u1EADp nh\u1EADt t\u00E0i kho\u1EA3n \u0111\u00E3 t\u1ED3n t\u1EA1i.",
                Font      = UiTheme.FontCaption,
                ForeColor = UiTheme.TextMuted,
                Location  = new Point(16, 450),
                AutoSize  = true
            };
            card.Controls.Add(lblStatus);

            btnConfirm = new RoundedButton
            {
                IconGlyph = "\uE8FB", // Checkmark
                IconRole  = ButtonIconRole.Primary,
                Text      = "X\u00E1c nh\u1EADn nh\u1EADp",
                Height    = 32
            };
            btnConfirm.Click += BtnConfirm_Click;
            card.Controls.Add(btnConfirm);

            btnCancel = new RoundedButton
            {
                IconGlyph    = "\uE711", // Cancel
                IconRole     = ButtonIconRole.Muted,
                Text         = "H\u1EE7y",
                DialogResult = DialogResult.Cancel,
                Height       = 32
            };
            card.Controls.Add(btnCancel);

            this.CancelButton = btnCancel;

            void UpdateLayout()
            {
                btnPasteClipboard.Width = btnPasteClipboard.GetPreferredSize(Size.Empty).Width;
                btnClear.Width          = btnClear.GetPreferredSize(Size.Empty).Width;
                btnClear.Location       = new Point(btnPasteClipboard.Right + 8, 68);

                btnConfirm.Width    = btnConfirm.GetPreferredSize(Size.Empty).Width;
                btnCancel.Width     = btnCancel.GetPreferredSize(Size.Empty).Width;
                btnConfirm.Location = new Point(card.Width - 16 - btnConfirm.Width, card.Height - 44);
                btnCancel.Location  = new Point(btnConfirm.Left - 8 - btnCancel.Width, card.Height - 44);
            }

            card.Resize += (s, e) => UpdateLayout();
            UpdateLayout();
        }

        private void BtnPasteClipboard_Click(object? sender, EventArgs e)
        {
            try
            {
                string clipText = Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(clipText))
                {
                    txtInput.Text = clipText.Trim();
                }
                else
                {
                    SuiteDialog.Alert(this, "Th\u00F4ng b\u00E1o", "Clipboard hi\u1EC7n kh\u00F4ng c\u00F3 n\u1ED9i dung v\u0103n b\u1EA3n.");
                }
            }
            catch (Exception ex)
            {
                SuiteDialog.Alert(this, "L\u1ED7i Clipboard", "Kh\u00F4ng th\u1EC3 \u0111\u1ECDc d\u1EEF li\u1EC7u t\u1EEB Clipboard: " + ex.Message);
            }
        }

        private void BtnConfirm_Click(object? sender, EventArgs e)
        {
            string raw = txtInput.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                SuiteDialog.Alert(this, "Th\u00F4ng b\u00E1o", "Vui l\u00F2ng nh\u1EADp ho\u1EB7c d\u00E1n danh s\u00E1ch t\u00E0i kho\u1EA3n tr\u01B0\u1EDBc khi x\u00E1c nh\u1EADn.");
                return;
            }

            var lines = raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            int addedCount   = 0;
            int updatedCount = 0;

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] parts;
                if (line.Contains('\t'))
                    parts = line.Split('\t', 3);
                else if (line.Contains('|'))
                    parts = line.Split('|', 3);
                else
                    parts = new[] { line };

                string username = parts.Length > 0 ? parts[0].Trim() : "";
                if (string.IsNullOrEmpty(username)) continue;

                string password = parts.Length > 1 ? parts[1].Trim() : "";
                string cookie   = parts.Length > 2 ? parts[2].Trim() : "";

                var existing = GlobalState.Accounts.FirstOrDefault(a =>
                    string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    if (parts.Length > 1) existing.Password = password;
                    if (parts.Length > 2) existing.Cookie   = cookie;
                    existing.Status = "Ready";
                    updatedCount++;
                }
                else
                {
                    var acc = new AccountInfo
                    {
                        Id          = Guid.NewGuid().ToString(),
                        Username    = username,
                        Password    = password,
                        Cookie      = cookie,
                        ProfileName = Guid.NewGuid().ToString("N"),
                        Status      = "Ready"
                    };
                    GlobalState.Accounts.Add(acc);
                    addedCount++;
                }
            }

            if (addedCount == 0 && updatedCount == 0)
            {
                SuiteDialog.Alert(this, "Th\u00F4ng b\u00E1o", "Kh\u00F4ng t\u00ECm th\u1EA5y d\u1EEF li\u1EC7u t\u00E0i kho\u1EA3n h\u1EE3p l\u1EC7.");
                return;
            }

            DataManager.SaveData();
            GlobalState.Accounts.ResetBindings();

            SuiteDialog.Alert(this, "Nh\u1EADp danh s\u00E1ch th\u00E0nh c\u00F4ng",
                $"Đ\u00E3 nh\u1EADp th\u00E0nh c\u00F4ng {addedCount} t\u00E0i kho\u1EA3n m\u1EDBi, c\u1EADp nh\u1EADt {updatedCount} t\u00E0i kho\u1EA3n.");

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
