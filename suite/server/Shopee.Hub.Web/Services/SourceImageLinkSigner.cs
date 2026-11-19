using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Shopee.Core.Coordination;

namespace Shopee.Hub.Web.Services;

/// <summary>
/// Tạo / kiểm token của link ảnh đổi MD5 công khai <c>/api/v/{token}.{jpg|png}</c> (GĐ9). Token v2 MÃ HOÁ TẤT ĐỊNH kiểu
/// SIV, để link không để lộ shop và mã ảnh gốc (giấu chuyện ảnh lấy từ shop khác là mục đích của việc đổi MD5):
/// <list type="bullet">
/// <item>Bản rõ P = <c>[độ dài shopKey UTF-8, 2 byte BE][shopKey][dấu vân tay nội dung 8 byte][id ASCII]</c>.</item>
/// <item>IV = 16 byte đầu HMAC-SHA256(khoá MAC, nhãn ‖ ver ‖ P) — vừa là IV vừa là chữ ký.</item>
/// <item>C = AES-256-CTR(khoá mã, bộ đếm 128 bit bắt đầu từ IV, P).</item>
/// <item>Token = base64url(<c>[ver=2][IV][C]</c>). Kiểm: giải mã rồi tính LẠI HMAC trên bản rõ, so IV bằng
/// <see cref="CryptographicOperations.FixedTimeEquals"/>.</item>
/// </list>
/// Cùng (shop, id, nội dung) luôn ra cùng token; nội dung đổi → dấu vân tay đổi → link khác (cache Cloudflare cũ không trả
/// nhầm). Khoá MAC và khoá mã suy ra từ CÙNG bí mật gốc qua HKDF-SHA256 với hai nhãn riêng.
/// <para>Bí mật gốc: 32 byte ngẫu nhiên, sinh MỘT lần khi bảng <c>settings</c> của hub.db CHƯA CÓ DÒNG
/// <see cref="SettingKeys.SourceImageLinkKey"/> (có trong backup đêm). KHÔNG BAO GIỜ tự xoay: có dòng mà rỗng / NULL / hỏng
/// thì NÉM lúc khởi động (để admin khôi phục) chứ không sinh mới — sinh mới là mọi link trong file đã xuất hỏng âm
/// thầm.</para>
/// </summary>
public sealed class SourceImageLinkSigner
{
    private const byte Version = 2;
    private const int IvBytes = 16;
    private const int FingerprintBytes = 8;
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes("shopee-hub/source-image-link/v2\0");
    private readonly byte[] _macKey;
    private readonly byte[] _encKey;

    public SourceImageLinkSigner(HubDatabase db, ILogger<SourceImageLinkSigner>? log = null)
    {
        // Sinh khoá CHỈ KHI bảng settings CHƯA CÓ DÒNG nào cho key (INSERT … DO NOTHING — không bao giờ ghi đè). Có dòng mà
        // rỗng / NULL / hỏng là lỗi cấu hình → ném, KHÔNG sinh lại.
        if (db.TryInsertSetting(SettingKeys.SourceImageLinkKey, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))))
            log?.LogInformation("source-images: đã sinh khoá ký link ảnh (một lần trong đời Hub)");
        var raw = db.ReadSettingRow(SettingKeys.SourceImageLinkKey).Value ?? "";
        byte[] key;
        try { key = Convert.FromBase64String(raw); }
        catch (FormatException) { key = []; }
        if (key.Length != 32)
            throw new InvalidOperationException($"settings '{SettingKeys.SourceImageLinkKey}' hỏng (cần 32 byte base64) — "
                + "KHÔNG tự sinh khoá mới vì mọi link ảnh đã xuất sẽ hỏng; khôi phục giá trị từ backup hub.db.");
        _macKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, info: Encoding.ASCII.GetBytes("shopee-hub/source-image-link/v2/mac"));
        _encKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 32, info: Encoding.ASCII.GetBytes("shopee-hub/source-image-link/v2/enc"));
    }

    /// <summary>Token cho (shopKey, id, dấu vân tay 8 byte). Gọi viên tự kiểm id đúng luật và shopKey
    /// 1..<see cref="SourceImageLimits.MaxShopKeyLength"/>.</summary>
    public string Sign(string shopKey, string id, ReadOnlySpan<byte> fingerprint)
    {
        if (fingerprint.Length != FingerprintBytes) throw new ArgumentException("dấu vân tay phải 8 byte", nameof(fingerprint));
        var shop = Encoding.UTF8.GetBytes(shopKey);
        var idb = Encoding.ASCII.GetBytes(id);
        var plain = new byte[2 + shop.Length + FingerprintBytes + idb.Length];
        BinaryPrimitives.WriteUInt16BigEndian(plain, (ushort)shop.Length);
        shop.CopyTo(plain, 2);
        fingerprint.CopyTo(plain.AsSpan(2 + shop.Length));
        idb.CopyTo(plain, 2 + shop.Length + FingerprintBytes);
        var iv = Iv(plain);
        var token = new byte[1 + IvBytes + plain.Length];
        token[0] = Version;
        iv.CopyTo(token, 1);
        Ctr(iv, plain, token.AsSpan(1 + IvBytes));
        return Base64UrlEncode(token);
    }

    /// <summary>Kiểm token: đúng dạng + giải mã + HMAC khớp + id đúng luật + shopKey đúng độ dài → (shopKey, id, dấu vân
    /// tay). Sai bất cứ chỗ nào → null (không phân biệt lý do).</summary>
    public (string ShopKey, string Id, byte[] Fingerprint)? Verify(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 2048 || Base64UrlDecode(token) is not { } b) return null;
        if (b.Length < 1 + IvBytes + 2 + 1 + FingerprintBytes + 1 || b[0] != Version) return null;
        var iv = b.AsSpan(1, IvBytes);
        var plain = new byte[b.Length - 1 - IvBytes];
        Ctr(iv, b.AsSpan(1 + IvBytes), plain);
        if (!CryptographicOperations.FixedTimeEquals(Iv(plain), iv)) return null;
        var shopLen = BinaryPrimitives.ReadUInt16BigEndian(plain);
        if (2 + shopLen + FingerprintBytes >= plain.Length) return null;
        string shop, id;
        try
        {
            shop = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(plain, 2, shopLen);
            id = Encoding.ASCII.GetString(plain, 2 + shopLen + FingerprintBytes, plain.Length - 2 - shopLen - FingerprintBytes);
        }
        catch (DecoderFallbackException) { return null; }
        if (shop.Length is < 1 or > SourceImageLimits.MaxShopKeyLength || !SourceImageLimits.IsValidId(id)) return null;
        return (shop, id, plain.AsSpan(2 + shopLen, FingerprintBytes).ToArray());
    }

    private byte[] Iv(ReadOnlySpan<byte> plain)
    {
        var data = new byte[Domain.Length + 1 + plain.Length];
        Domain.CopyTo(data, 0);
        data[Domain.Length] = Version;
        plain.CopyTo(data.AsSpan(Domain.Length + 1));
        return HMACSHA256.HashData(_macKey, data)[..IvBytes];
    }

    /// <summary>AES-256-CTR: dòng khoá = AES-ECB(khoá mã, bộ đếm) với bộ đếm 128 bit big-endian bắt đầu từ IV, tăng 1 mỗi
    /// khối. Mã hoá và giải mã là cùng một phép XOR.</summary>
    private void Ctr(ReadOnlySpan<byte> iv, ReadOnlySpan<byte> input, Span<byte> output)
    {
        using var aes = Aes.Create();
        aes.Key = _encKey;
        var blocks = (input.Length + 15) / 16;
        var counters = new byte[blocks * 16];
        Span<byte> ctr = stackalloc byte[16];
        iv.CopyTo(ctr);
        for (var i = 0; i < blocks; i++)
        {
            ctr.CopyTo(counters.AsSpan(i * 16));
            for (var k = 15; k >= 0 && ++ctr[k] == 0; k--) { }
        }
        var stream = aes.EncryptEcb(counters, PaddingMode.None);
        for (var i = 0; i < input.Length; i++) output[i] = (byte)(input[i] ^ stream[i]);
    }

    private static string Base64UrlEncode(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>base64url không đệm → bytes; ký tự lạ / độ dài sai → null. Chỉ nhận dạng CHUẨN (mã hoá lại phải ra đúng
    /// chuỗi vào): ký tự cuối mang bit thừa thì nhiều chuỗi giải ra cùng bytes — không chặn thì một link có nhiều "bản sửa"
    /// vẫn chạy.</summary>
    private static byte[]? Base64UrlDecode(string s)
    {
        foreach (var ch in s)
            if (!(ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')) return null;
        if (s.Length % 4 == 1) return null;
        var std = s.Replace('-', '+').Replace('_', '/') + new string('=', (4 - s.Length % 4) % 4);
        byte[] b;
        try { b = Convert.FromBase64String(std); }
        catch (FormatException) { return null; }
        return Base64UrlEncode(b) == s ? b : null;
    }
}
