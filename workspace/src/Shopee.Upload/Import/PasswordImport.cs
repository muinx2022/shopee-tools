using Shopee.Upload.Forms;

namespace Shopee.Upload.Import;

/// <summary>Ô đã có giá trị khác giá trị trong file — hỏi MỘT lần cho cả lượt nhập.</summary>
public enum OverwriteChoice
{
    /// <summary>Ghi đè mọi ô khác bằng giá trị trong file.</summary>
    OverwriteAll,
    /// <summary>Chỉ điền ô đang trống; ô đã có giá trị (dù khác) để nguyên.</summary>
    FillEmptyOnly,
    /// <summary>Huỷ cả lượt — không ghi gì.</summary>
    Cancel,
}

/// <summary>Một tài khoản trên app khớp một dòng trong file.</summary>
public sealed record PasswordMatch(AccountForm Form, PasswordRow Row, bool ByLogin)
{
    public override string ToString() => $"{Form.DisplayName} ← {Row}";
}

/// <summary>Kết quả so file với form (chưa ghi gì).</summary>
public sealed class PasswordImportPlan
{
    public List<PasswordMatch> Matches { get; } = [];

    /// <summary>Tài khoản trên app không tìm thấy trong file (tên hiện).</summary>
    public List<string> NotFound { get; } = [];

    /// <summary>ACC lặp trong file: ACC (như trong file, dòng đầu) + các số dòng. Các dòng này KHÔNG dùng.</summary>
    public List<(string Acc, int[] Rows)> DuplicateAccs { get; } = [];

    /// <summary>Tài khoản bỏ qua vì mơ hồ (một dòng khớp nhiều tài khoản / khớp dòng ACC trùng) — câu đã có tên, không có mật khẩu.</summary>
    public List<string> Ambiguous { get; } = [];

    /// <summary>Tài khoản khớp dòng file qua NHÃN nhưng ô tài khoản Shopee trên form đang là tài khoản KHÁC ACC của dòng đó — không điền
    /// ô nào (điền là ghép mật khẩu của ACC này vào tài khoản kia). Tên hiện.</summary>
    public List<string> LoginMismatch { get; } = [];

    /// <summary>Số dòng có ACC mà không khớp tài khoản nào trên app (chỉ đếm, không liệt kê).</summary>
    public int UnmatchedRows { get; set; }

    /// <summary>Tổng số dòng có ACC đã đọc.</summary>
    public int TotalRows { get; set; }

    /// <summary>Số ô (trên mọi tài khoản khớp) đang có giá trị KHÁC giá trị trong file.</summary>
    public int ConflictFields => Matches.Sum(m => PasswordImport.Changes(m).Count(c => c.Conflict));

    /// <summary>Số tài khoản có ít nhất một ô khác.</summary>
    public int ConflictAccounts => Matches.Count(m => PasswordImport.Changes(m).Any(c => c.Conflict));
}

/// <summary>Kết quả sau khi điền (tên tài khoản). <see cref="Filled"/>: có ít nhất một ô được ghi. <see cref="Kept"/>: có ít nhất một ô
/// đang có giá trị khác bị GIỮ NGUYÊN (chọn chỉ điền ô trống) — một tài khoản có thể ở cả hai nhóm. <see cref="Unchanged"/>: khớp
/// nhưng mọi ô đã giống file / file trống.</summary>
public sealed record PasswordImportResult(IReadOnlyList<string> Filled, IReadOnlyList<string> Kept, IReadOnlyList<string> Unchanged,
    int FieldsWritten, int FieldsKept);

/// <summary>
/// Nút "Nhập mật khẩu từ Excel" (plan GĐ5c mục 2) — hàm thuần, không WPF.
/// <list type="bullet">
/// <item>Khớp mỗi tài khoản trên form với MỘT dòng: dòng có ACC = <c>ShopeeLogin</c> của nó; không có thì dòng có ACC = <c>Label</c>.
/// So sau <see cref="ImportText.Key"/>, khớp đúng, không khớp mờ. Khớp qua nhãn mà form ĐÃ có tài khoản Shopee khác → bỏ qua, báo
/// (<see cref="PasswordImportPlan.LoginMismatch"/>).</item>
/// <item>ACC lặp trong file → mọi dòng ACC đó bị bỏ, báo; tài khoản dính tới chúng không được điền. Một dòng khớp 2 tài khoản trở
/// lên → bỏ cả nhóm, báo. Không chọn bừa.</item>
/// <item>Không bao giờ xoá giá trị: ô trong file trống thì ô trên form để nguyên.</item>
/// </list>
/// </summary>
public static class PasswordImport
{
    /// <summary>Một ô sẽ điền: <see cref="Conflict"/> = form đang có giá trị khác.</summary>
    public sealed record Change(string Field, string Current, string Incoming, bool Conflict, Action<AccountForm, string> Set)
    {
        // Không in giá trị (mật khẩu).
        public override string ToString() => Conflict ? $"{Field} (khác)" : Field;
    }

    public static PasswordImportPlan Plan(IReadOnlyList<PasswordRow> rows, IEnumerable<AccountForm> accounts)
    {
        var plan = new PasswordImportPlan { TotalRows = rows.Count };
        var byAcc = rows.GroupBy(r => ImportText.Key(r.Acc)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var g in byAcc.Where(g => g.Value.Count > 1))
            plan.DuplicateAccs.Add((g.Value[0].Acc, g.Value.Select(r => r.RowNumber).ToArray()));

        // Tài khoản → khoá ACC nó khớp (login trước, rồi nhãn).
        var claims = new List<(AccountForm Form, string Key, bool ByLogin)>();
        foreach (var f in accounts)
        {
            var login = ImportText.Key(f.ShopeeLogin);
            var label = ImportText.Key(f.Label);
            if (login.Length > 0 && byAcc.ContainsKey(login)) claims.Add((f, login, true));
            else if (label.Length > 0 && byAcc.ContainsKey(label)) claims.Add((f, label, false));
            else plan.NotFound.Add(f.DisplayName);
        }

        var usedKeys = new HashSet<string>();
        foreach (var group in claims.GroupBy(c => c.Key))
        {
            usedKeys.Add(group.Key);
            var fileRows = byAcc[group.Key];
            var names = string.Join(", ", group.Select(c => c.Form.DisplayName));
            if (fileRows.Count > 1)
            {
                plan.Ambiguous.Add($"{names}: ACC \"{fileRows[0].Acc}\" lặp ở các dòng {string.Join(", ", fileRows.Select(r => r.RowNumber))} — không điền.");
                continue;
            }
            if (group.Count() > 1)
            {
                plan.Ambiguous.Add($"{names}: cùng khớp {fileRows[0]} — không biết điền cho tài khoản nào, không điền.");
                continue;
            }
            var c = group.Single();
            // Khớp qua nhãn, form đã có tài khoản Shopee (khác ACC — giống thì đã khớp qua login ở trên) → không ghép.
            if (!c.ByLogin && !string.IsNullOrWhiteSpace(c.Form.ShopeeLogin))
            {
                plan.LoginMismatch.Add(c.Form.DisplayName);
                continue;
            }
            plan.Matches.Add(new PasswordMatch(c.Form, fileRows[0], c.ByLogin));
        }
        plan.UnmatchedRows = byAcc.Where(kv => !usedKeys.Contains(kv.Key)).Sum(kv => kv.Value.Count);
        return plan;
    }

    /// <summary>Các ô sẽ đổi của một tài khoản (bỏ ô file trống và ô đã giống).</summary>
    public static List<Change> Changes(PasswordMatch m)
    {
        var f = m.Form;
        var r = m.Row;
        var list = new List<Change>();
        // Tài khoản / email: giống nhau sau chuẩn hoá (khác hoa thường, dấu cách thừa) coi là giống — không đổi.
        if (r.Acc.Length > 0 && !ImportText.SameKey(f.ShopeeLogin, r.Acc))
            list.Add(new Change("tài khoản Shopee", f.ShopeeLogin, r.Acc, !string.IsNullOrWhiteSpace(f.ShopeeLogin), (x, v) => x.ShopeeLogin = v));
        // Mật khẩu: so đúng từng ký tự.
        if (r.Pass.Length > 0 && !string.Equals(f.ShopeePassword, r.Pass, StringComparison.Ordinal))
            list.Add(new Change("mật khẩu Shopee", f.ShopeePassword, r.Pass, f.ShopeePassword.Length > 0, (x, v) => x.ShopeePassword = v));
        if (r.Mail.Length > 0 && !string.Equals(f.VerifyEmail.Trim(), r.Mail, StringComparison.OrdinalIgnoreCase))
            list.Add(new Change("email xác minh", f.VerifyEmail, r.Mail, !string.IsNullOrWhiteSpace(f.VerifyEmail), (x, v) => x.VerifyEmail = v));
        if (r.MailPass.Length > 0 && !string.Equals(f.VerifyEmailPassword, r.MailPass, StringComparison.Ordinal))
            list.Add(new Change("mật khẩu email", f.VerifyEmailPassword, r.MailPass, f.VerifyEmailPassword.Length > 0, (x, v) => x.VerifyEmailPassword = v));
        return list;
    }

    /// <summary>Điền vào form theo lựa chọn. <see cref="OverwriteChoice.Cancel"/> → không ghi gì.</summary>
    public static PasswordImportResult Apply(PasswordImportPlan plan, OverwriteChoice choice)
    {
        var filled = new List<string>();
        var keptAccounts = new List<string>();
        var unchanged = new List<string>();
        int written = 0, kept = 0;
        if (choice == OverwriteChoice.Cancel) return new PasswordImportResult(filled, keptAccounts, unchanged, 0, 0);
        foreach (var m in plan.Matches)
        {
            bool anyWritten = false, anyKept = false;
            foreach (var c in Changes(m))
            {
                if (c.Conflict && choice != OverwriteChoice.OverwriteAll) { kept++; anyKept = true; continue; }
                c.Set(m.Form, c.Incoming);
                written++;
                anyWritten = true;
            }
            if (anyWritten) filled.Add(m.Form.DisplayName);
            if (anyKept) keptAccounts.Add(m.Form.DisplayName);
            if (!anyWritten && !anyKept) unchanged.Add(m.Form.DisplayName);
        }
        return new PasswordImportResult(filled, keptAccounts, unchanged, written, kept);
    }

    /// <summary>Câu báo kết quả — theo TÊN (tài khoản app, ACC lặp), không có mật khẩu. Dòng file không khớp chỉ ghi số.</summary>
    public static string Summary(string fileName, PasswordSheet sheet, PasswordImportPlan plan, PasswordImportResult? result)
    {
        var lines = new List<string>
        {
            $"File {fileName}, {(sheet.Hidden ? "đang đọc sheet ẩn" : "sheet")} \"{sheet.SheetName}\" (tiêu đề ở dòng {sheet.HeaderRow}): " +
            $"{plan.TotalRows} dòng có ACC.",
        };
        if (sheet.BadRows.Count > 0)
            lines.Add($"Bỏ {sheet.BadRows.Count} dòng có ô lỗi hoặc ô chỉ có khoảng trắng (ACC / Pass / pass Mail): dòng " +
                      string.Join(", ", sheet.BadRows) + ".");
        if (result is null)
        {
            lines.Add("Đã huỷ — không điền gì.");
        }
        else
        {
            lines.Add($"Đã điền {result.Filled.Count} tài khoản ({result.FieldsWritten} ô)" +
                      (result.Filled.Count > 0 ? ": " + string.Join(", ", result.Filled) : "") + ".");
            if (result.Kept.Count > 0)
                lines.Add($"Giữ nguyên (không ghi đè): {result.Kept.Count} tài khoản ({string.Join(", ", result.Kept)}) — " +
                          $"{result.FieldsKept} ô đang có giá trị khác file, đã chọn chỉ điền ô trống.");
            if (result.Unchanged.Count > 0)
                lines.Add($"Khớp nhưng không có gì để đổi: {result.Unchanged.Count} tài khoản ({string.Join(", ", result.Unchanged)}).");
        }
        if (plan.NotFound.Count > 0)
            lines.Add($"Không tìm thấy trong file: {plan.NotFound.Count} tài khoản ({string.Join(", ", plan.NotFound)}).");
        if (plan.LoginMismatch.Count > 0)
            lines.Add($"Bỏ qua — tài khoản Shopee trên form khác file (khớp theo tên shop nhưng ô tài khoản đang là tài khoản khác, không " +
                      $"điền ô nào): {plan.LoginMismatch.Count} tài khoản ({string.Join(", ", plan.LoginMismatch)}).");
        foreach (var a in plan.Ambiguous) lines.Add("Bỏ qua: " + a);
        if (plan.DuplicateAccs.Count > 0)
            lines.Add("ACC lặp trong file (các dòng này không dùng): " +
                      string.Join("; ", plan.DuplicateAccs.Select(d => $"\"{d.Acc}\" dòng {string.Join(", ", d.Rows)}")) + ".");
        lines.Add($"Dòng trong file không khớp tài khoản nào: {plan.UnmatchedRows}.");
        lines.Add("Chưa lưu — kiểm rồi bấm Lưu.");
        return string.Join(Environment.NewLine, lines);
    }
}
