using Shopee.Core.MassUpload.Seller.Login;

namespace Shopee.Core.Tests.MassUpload.Seller;

/// <summary>
/// GĐ5b vòng 2: gõ an toàn (<see cref="HumanFieldFill"/> + <see cref="SellerLoginFormFill"/>) trên một "trang" giả mô phỏng bàn phím
/// thật: phím luôn đi vào ô đang có focus, KHÔNG phải ô mình định gõ. Tái hiện lỗi phản biện bắt được trên Brave thật: trang vẽ lại
/// node ô mật khẩu sau khi gõ tài khoản → mật khẩu rơi vào ô tài khoản.
/// </summary>
public sealed class SellerLoginFillTests
{
    private const string Login = "shopuser01";
    private const string Pass = "Secr3tPw!";

    private static Task NoDelay(TimeSpan _, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }

    /// <summary>Trang giả: các ô (node) + ô đang focus. <see cref="Field.TypeAsync"/> gõ vào <see cref="Active"/> (như bàn phím thật).
    /// Ghi lại mọi ký tự từng lọt vào từng ô.</summary>
    private sealed class FakeDoc
    {
        public Node? Active { get; set; }
        public Node LoginKey { get; set; } = new("loginKey");
        public Node Password { get; set; } = new("password");

        /// <summary>Gọi sau mỗi ký tự gõ (vd: thay node ô mật khẩu sau khi gõ tài khoản).</summary>
        public Action<FakeDoc, Node?>? AfterType { get; set; }
    }

    private sealed class Node(string name)
    {
        public string Name { get; } = name;
        public string Value { get; set; } = "";
        public bool Connected { get; set; } = true;
        public bool HasBox { get; set; } = true;
        public bool ClearThrows { get; set; }
        public List<string> EverReceived { get; } = [];
        public override string ToString() => Name;
    }

    private sealed class Field(FakeDoc doc, Node node) : IHumanField
    {
        public Task<bool> ClickAsync(CancellationToken ct)
        {
            // Bản thật: không có khung / hit-test không trúng → không bấm (KHÔNG bấm vào chỗ chuột đang đứng).
            if (!node.Connected || !node.HasBox) return Task.FromResult(false);
            doc.Active = node;
            return Task.FromResult(true);
        }

        public Task<bool> IsFocusedAsync() => Task.FromResult(node.Connected && ReferenceEquals(doc.Active, node));

        public Task<bool> ClearAsync()
        {
            if (node.ClearThrows || !node.Connected) return Task.FromResult(false);
            node.Value = "";
            return Task.FromResult(true);
        }

        public Task TypeAsync(string textElement)
        {
            if (doc.Active is { Connected: true } target)
            {
                target.Value += textElement;
                target.EverReceived.Add(textElement);
            }
            doc.AfterType?.Invoke(doc, doc.Active);
            return Task.CompletedTask;
        }
    }

    private sealed class Form(FakeDoc doc) : ISellerLoginForm
    {
        public bool OnSellerHost { get; set; } = true;
        public int LoginKeyFinds, PasswordFinds, Clears;

        public Task<IHumanField?> FindLoginKeyAsync(CancellationToken ct)
        {
            LoginKeyFinds++;
            return Task.FromResult<IHumanField?>(doc.LoginKey.Connected ? new Field(doc, doc.LoginKey) : null);
        }

        public Task<IHumanField?> FindPasswordAsync(CancellationToken ct)
        {
            PasswordFinds++;
            return Task.FromResult<IHumanField?>(doc.Password.Connected ? new Field(doc, doc.Password) : null);
        }

        public Task<bool> LoginKeyHoldsAsync(string login) => Task.FromResult(doc.LoginKey.Value == login);

        public Task ClearLoginKeyAsync()
        {
            Clears++;
            doc.LoginKey.Value = "";
            return Task.CompletedTask;
        }
    }

    private static Task<bool> Fill(Form form) =>
        SellerLoginFormFill.FillAsync(form, Login, Pass, new Random(1), NoDelay, CancellationToken.None);

    private static bool PasswordLeakedInto(Node n) => n.EverReceived.Count > 0 && string.Concat(n.EverReceived).Contains(Pass[..3]);

    [Fact]
    public async Task BinhThuong_GoDungHaiO()
    {
        var doc = new FakeDoc();
        var ok = await Fill(new Form(doc));

        Assert.True(ok);
        Assert.Equal(Login, doc.LoginKey.Value);
        Assert.Equal(Pass, doc.Password.Value);
    }

    [Fact]
    public async Task TrangThayNodeOMatKhauSauKhiGoTaiKhoan_TimLaiO_GoDung()
    {
        // Đúng thí nghiệm phản biện (cdpexp "mistype"): gõ ký tự đầu của tài khoản thì trang thay node ô mật khẩu.
        var doc = new FakeDoc();
        var old = doc.Password;
        doc.AfterType = (d, _) =>
        {
            if (!ReferenceEquals(d.Password, old)) return;
            old.Connected = false;
            d.Password = new Node("password-moi");
        };
        var form = new Form(doc);

        var ok = await Fill(form);

        Assert.True(ok);
        Assert.Equal(Login, doc.LoginKey.Value);
        Assert.Equal(Pass, doc.Password.Value);          // ô MỚI nhận mật khẩu
        Assert.Empty(old.EverReceived);
        Assert.False(PasswordLeakedInto(doc.LoginKey));
        Assert.Equal(1, form.PasswordFinds);              // ô mật khẩu tìm SAU khi gõ xong tài khoản
    }

    [Fact]
    public async Task OMatKhauKhongCoKhung_KhongBamMu_KhongGo_BaoKhongDat()
    {
        // Bản cũ: không có khung → bấm vào chỗ chuột đang đứng (vừa gõ tài khoản xong = ô tài khoản) rồi gõ mật khẩu vào đó.
        var doc = new FakeDoc();
        doc.Password.HasBox = false;

        var ok = await Fill(new Form(doc));

        Assert.False(ok);
        Assert.False(PasswordLeakedInto(doc.LoginKey));
        Assert.Equal(Login, doc.LoginKey.Value);
        Assert.Empty(doc.Password.EverReceived);
    }

    [Fact]
    public async Task XoaOLoi_KhongGoMu_BaoKhongDat()
    {
        var doc = new FakeDoc();
        doc.Password.ClearThrows = true;

        var ok = await Fill(new Form(doc));

        Assert.False(ok);
        Assert.Empty(doc.Password.EverReceived);
        Assert.False(PasswordLeakedInto(doc.LoginKey));
    }

    [Fact]
    public async Task NodeBiThayGiuaLucGoMatKhau_KhongLotSangOTaiKhoan_GoLaiOMoi()
    {
        // Gõ được 2 ký tự mật khẩu thì node bị thay và focus nhảy về ô tài khoản — ký tự thứ 3 trở đi KHÔNG được gõ.
        var doc = new FakeDoc();
        var typedPass = 0;
        doc.AfterType = (d, target) =>
        {
            if (!ReferenceEquals(target, d.Password) || ++typedPass != 2) return;
            d.Password.Connected = false;
            d.Password = new Node("password-moi");
            d.Active = d.LoginKey;
        };
        var form = new Form(doc);

        var ok = await Fill(form);

        // Ký tự thứ 3 trở đi KHÔNG lọt sang ô tài khoản; ô mật khẩu mới được tìm lại, xoá, gõ lại trọn một lần (GĐ5b vòng 3).
        Assert.True(ok);
        Assert.Equal(Login, doc.LoginKey.Value);
        Assert.False(PasswordLeakedInto(doc.LoginKey));
        Assert.Empty(doc.LoginKey.EverReceived.Skip(Login.Length));   // sau tài khoản không thêm ký tự nào
        Assert.Equal(Pass, doc.Password.Value);
        Assert.Equal(2, form.PasswordFinds);
    }

    [Fact]
    public async Task OMatKhauVeLaiMuon_SauPhimCuoiTaiKhoan_GoLaiMotLanThanhCong()
    {
        // Ca phản biện vòng 2: trang thay ô mật khẩu ~1,5 s sau phím cuối của ô tài khoản → lúc đó đang gõ dở mật khẩu.
        var doc = new FakeDoc();
        var replaced = false;
        doc.AfterType = (d, target) =>
        {
            if (replaced || !ReferenceEquals(target, d.Password) || d.Password.Value.Length != 4) return;
            replaced = true;
            d.Password.Connected = false;
            d.Password = new Node("password-ve-lai");
            d.Active = null;   // node đang focus bị gỡ → focus về body
        };
        var form = new Form(doc);

        Assert.True(await Fill(form));
        Assert.Equal(Pass, doc.Password.Value);
        Assert.Equal(Login, doc.LoginKey.Value);
        Assert.Equal(2, form.PasswordFinds);
    }

    [Fact]
    public async Task OMatKhauHongCaHaiLan_DungKhongGoLanBa()
    {
        // Mỗi lần gõ được 2 ký tự là trang lại thay node ô mật khẩu → chỉ gõ lại ĐÚNG một lần rồi dừng.
        var doc = new FakeDoc();
        doc.AfterType = (d, target) =>
        {
            if (!ReferenceEquals(target, d.Password) || d.Password.Value.Length != 2) return;
            d.Password.Connected = false;
            d.Password = new Node("password-moi");
            d.Active = d.LoginKey;
        };
        var form = new Form(doc);

        Assert.False(await Fill(form));
        Assert.Equal(2, form.PasswordFinds);
        Assert.Equal(Login, doc.LoginKey.Value);
        Assert.Empty(doc.LoginKey.EverReceived.Skip(Login.Length));
    }

    [Fact]
    public async Task OTaiKhoanBiDoiGiaTri_XoaTrangRoiBaoKhongDat()
    {
        // Lưới cuối: dù vì lý do gì ô tài khoản không còn đúng tài khoản (vd trang tự chép gì vào) → xoá trắng, không bấm.
        var doc = new FakeDoc();
        doc.AfterType = (d, target) => { if (ReferenceEquals(target, d.Password) && d.Password.Value.Length == Pass.Length) d.LoginKey.Value += "x"; };
        var form = new Form(doc);

        var ok = await Fill(form);

        Assert.False(ok);
        Assert.Equal(1, form.Clears);
        Assert.Equal("", doc.LoginKey.Value);
    }

    [Fact]
    public async Task OTaiKhoanKhongCoKhung_KhongGoGiCa()
    {
        var doc = new FakeDoc();
        doc.LoginKey.HasBox = false;
        var form = new Form(doc);

        Assert.False(await Fill(form));
        Assert.Equal(0, form.PasswordFinds);
        Assert.Empty(doc.Password.EverReceived);
    }

    [Fact]
    public async Task HostLa_KhongGo()
    {
        var doc = new FakeDoc();
        var form = new Form(doc) { OnSellerHost = false };

        Assert.False(await Fill(form));
        Assert.Equal(0, form.LoginKeyFinds);
        Assert.Empty(doc.LoginKey.EverReceived);
    }

    [Fact]
    public void TextElement_KhongTachCapSurrogate()
    {
        var s = "a😀b" + "é";   // emoji ngoài BMP + e có dấu tổ hợp
        Assert.Equal(["a", "😀", "b", "é"], HumanFieldFill.TextElements(s));
    }

    [Fact]
    public async Task GoTheoTextElement_EmojiVaoONguyenVen()
    {
        var doc = new FakeDoc();
        var ok = await HumanFieldFill.FillAsync(new Field(doc, doc.Password), "p😀", new Random(1), NoDelay, CancellationToken.None);
        Assert.True(ok);
        Assert.Equal(["p", "😀"], doc.Password.EverReceived);
    }
}
