using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ClipboardSync.App.Services;

/// <summary>
/// Binary WebSocket frame for one file chunk. Android's <c>FileProtocol</c> uses the same layout:
/// <code>
/// [0..16)  transfer id
/// [16..20) chunk index, uint32 big-endian
/// [20..32) AES-GCM nonce
/// [32..^16) ciphertext
/// [^16..)  GCM tag
/// </code>
/// Bytes 0..20 are the associated data, so a chunk cannot be replayed into another
/// transfer or position.
/// </summary>
public static class FileChunkCodec
{
    public const int ChunkSize = 256 * 1024;
    public const int IdSize = 16;
    public const int HeaderSize = IdSize + 4;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    public const int Overhead = HeaderSize + NonceSize + TagSize;

    public static byte[] Seal(AesGcm aes, byte[] transferId, int index, ReadOnlySpan<byte> plain)
    {
        var frame = new byte[Overhead + plain.Length];
        transferId.CopyTo(frame, 0);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(IdSize, 4), (uint)index);
        var nonce = frame.AsSpan(HeaderSize, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        aes.Encrypt(
            nonce,
            plain,
            frame.AsSpan(HeaderSize + NonceSize, plain.Length),
            frame.AsSpan(frame.Length - TagSize, TagSize),
            frame.AsSpan(0, HeaderSize));
        return frame;
    }

    public static string TransferIdHex(byte[] frame) =>
        Convert.ToHexString(frame, 0, IdSize).ToLowerInvariant();

    public static int Index(byte[] frame) =>
        (int)BinaryPrimitives.ReadUInt32BigEndian(frame.AsSpan(IdSize, 4));

    public static int PlainLength(byte[] frame) => frame.Length - Overhead;

    /// <summary>Decrypts into <paramref name="plain"/>; throws <see cref="CryptographicException"/> on a bad tag.</summary>
    public static void Open(AesGcm aes, byte[] frame, Span<byte> plain)
    {
        var length = PlainLength(frame);
        aes.Decrypt(
            frame.AsSpan(HeaderSize, NonceSize),
            frame.AsSpan(HeaderSize + NonceSize, length),
            frame.AsSpan(frame.Length - TagSize, TagSize),
            plain[..length],
            frame.AsSpan(0, HeaderSize));
    }
}
