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

public sealed class PairingQr
{
    public int V { get; set; } = 1;

    public string Ip { get; set; } = "";

    public int Port { get; set; }

    public string Token { get; set; } = "";
}
