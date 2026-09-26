using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace ClipboardSync.App.Models;

/// <summary>
/// Wire message. Property names match <c>shared/protocol-schema.json</c> via camelCase.
/// </summary>
public sealed class ClipboardPayload
{
    public int Version { get; set; } = 1;

    public string Type { get; set; } = "text";

    public string OriginDeviceId { get; set; } = "";

    /// <summary>Display name of the sender, for example "David's Phone". Empty on older clients.</summary>
    public string DeviceName { get; set; } = "";

    public string ContentHash { get; set; } = "";

    public string Timestamp { get; set; } = "";

    public string Payload { get; set; } = "";

    public EncryptionEnvelope Encryption { get; set; } = new();
}

public sealed class EncryptionEnvelope
{
    [JsonPropertyName("iv")]
    public string Iv { get; set; } = "";

    public string AuthTag { get; set; } = "";
}

/// <summary>Encrypted body of a <c>type: "file"</c> message. Chunks follow as binary frames.</summary>
public sealed class FileOffer
{
    public string TransferId { get; set; } = "";

    public string FileName { get; set; } = "";

    public long FileSize { get; set; }

    public string MimeType { get; set; } = "application/octet-stream";

    public int ChunkSize { get; set; }
}

/// <summary>Encrypted body of <c>file-end</c> and <c>file-cancel</c>.</summary>
public sealed class FileControl
{
    public string TransferId { get; set; } = "";

    public int Chunks { get; set; }

    public long FileSize { get; set; }

    public string? Reason { get; set; }
}

public sealed class PairingQr
{
    public int V { get; set; } = 1;

    public string Ip { get; set; } = "";

    public int Port { get; set; }

    public string Token { get; set; } = "";
}
