using System.Security.Cryptography;
using System.Text;

namespace ClipboardSync.App.Services;

/// <summary>
/// AES-256-GCM with a key from HKDF-SHA256(token). Android's
/// <c>EncryptionUtil</c> uses the same salt, info, nonce size, and tag size.
/// The GCM tag is stored separately from the ciphertext, matching the schema.
/// </summary>
public static class EncryptionService
{
    public const string Salt = "clipboardsync-v1";
    public const string Info = "clipboard-payload";

    public static byte[] DeriveKey(string pairingToken)
    {
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(pairingToken),
            32,
            Encoding.UTF8.GetBytes(Salt),
            Encoding.UTF8.GetBytes(Info));
    }

    public static (byte[] Ciphertext, byte[] Iv, byte[] Tag) Encrypt(byte[] key, byte[] plaintext)
    {
        var iv = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(iv, plaintext, ciphertext, tag);
        return (ciphertext, iv, tag);
    }

    public static byte[] Decrypt(byte[] key, byte[] iv, byte[] ciphertext, byte[] tag)
    {
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, tag.Length);
        aes.Decrypt(iv, ciphertext, tag, plaintext);
        return plaintext;
    }

    public static string Sha256Hex(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
