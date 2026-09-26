using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipboardSync.App.Models;
using Fleck;

namespace ClipboardSync.App.Services;

/// <summary>
/// One local WebSocket listener on port 53211. The phone must send the pairing
/// token in the <c>X-Clipboard-Token</c> header during the HTTP upgrade.
/// Payloads are JSON matching protocol-schema.json. Clipboard writes are posted
/// back to the WPF STA thread because <see cref="Clipboard"/> is STA-only.
/// </summary>
public sealed partial class ClipboardWebSocketServer : IDisposable
{
    public const string TokenHeader = "X-Clipboard-Token";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ClipboardEventLog _log;
    private readonly PairingService _pairing;
    private readonly EchoGuard _echo;
    private readonly SyncHistory _history;
    private readonly AppSettings _settings;
    private readonly FileTransferTracker _transfers;
    private WebSocketServer? _server;
    private DateTimeOffset _suppressImagesUntil;

    public ClipboardWebSocketServer(
        ClipboardEventLog log,
        PairingService pairing,
        EchoGuard echo,
        SyncHistory history,
        AppSettings settings,
        FileTransferTracker transfers)
    {
        _log = log;
        _pairing = pairing;
        _echo = echo;
        _history = history;
        _settings = settings;
        _transfers = transfers;
    }

    public event Action<string>? StatusChanged;

    public void Start()
    {
        FleckLog.Level = LogLevel.Warn;
        _server = new WebSocketServer($"ws://0.0.0.0:{PairingService.Port}");
        _server.RestartAfterListenError = true;
        _server.Start(socket =>
        {
            socket.OnOpen = () => OnOpen(socket);
            socket.OnClose = () => OnClose(socket);
            socket.OnMessage = message => OnMessage(socket, message);
            socket.OnBinary = frame => OnBinary(socket, frame);
            socket.OnError = error => _log.Write("WebSocket error: " + error.Message);
        });
        _log.Write($"WebSocket server listening on port {PairingService.Port}.");
        StatusChanged?.Invoke("Waiting for a device");
    }

    public void PublishText(string text)
    {
        var plaintext = Encoding.UTF8.GetBytes(text);
        var hash = EncryptionService.Sha256Hex(plaintext);
        if (_echo.WasAppliedFromPeer(hash))
        {
            return;
        }

        _echo.NoteSent(hash);
        var (ciphertext, iv, tag) = EncryptionService.Encrypt(CurrentKey(), plaintext);
        var message = new ClipboardPayload
        {
            Version = 1,
            Type = "text",
            OriginDeviceId = _pairing.DeviceId,
            DeviceName = _settings.DisplayName,
            ContentHash = hash,
            Timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Payload = Convert.ToBase64String(ciphertext),
            Encryption = new EncryptionEnvelope
            {
                Iv = Convert.ToBase64String(iv),
                AuthTag = Convert.ToBase64String(tag),
            },
        };
        BroadcastExcept(null, JsonSerializer.Serialize(message, JsonOptions));
        _history.AddText("Sent", text);
    }

    public void PublishImage(byte[] jpeg)
    {
        if (DateTimeOffset.UtcNow < _suppressImagesUntil)
        {
            return;
        }

        if (!_settings.SyncImages)
        {
            _history.AddText("Skipped", "Images are off");
            return;
        }
        var plain = Encoding.UTF8.GetBytes(Convert.ToBase64String(jpeg));
        if (plain.Length > 8 * 1024 * 1024)
        {
            _log.Write("Skipped image: encoded payload is over 8 MB.");
            _history.AddText("Skipped", "Image was too large");
            return;
        }

        var hash = EncryptionService.Sha256Hex(plain);
        if (_echo.WasAppliedFromPeer(hash))
        {
            return;
        }

        _echo.NoteSent(hash);
        var (ciphertext, iv, tag) = EncryptionService.Encrypt(CurrentKey(), plain);
        var message = new ClipboardPayload
        {
            Version = 1,
            Type = "image",
            OriginDeviceId = _pairing.DeviceId,
            DeviceName = _settings.DisplayName,
            ContentHash = hash,
            Timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Payload = Convert.ToBase64String(ciphertext),
            Encryption = new EncryptionEnvelope
            {
                Iv = Convert.ToBase64String(iv),
                AuthTag = Convert.ToBase64String(tag),
            },
        };
        BroadcastExcept(null, JsonSerializer.Serialize(message, JsonOptions));
        _history.AddImage("Sent", jpeg);
    }

    /// <summary>Pushes text to devices paired with this PC. Used for clipboard that arrived from an upstream hub.</summary>
    public void RelayText(string text, string? sourceName = null)
    {
        var plain = Encoding.UTF8.GetBytes(text);
        var hash = EncryptionService.Sha256Hex(plain);
        _echo.NoteSent(hash);
        var (ciphertext, iv, tag) = EncryptionService.Encrypt(CurrentKey(), plain);
        var message = new ClipboardPayload
        {
            Version = 1,
            Type = "text",
            OriginDeviceId = _pairing.DeviceId,
            DeviceName = string.IsNullOrWhiteSpace(sourceName) ? _settings.DisplayName : sourceName.Trim(),
            ContentHash = hash,
            Timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Payload = Convert.ToBase64String(ciphertext),
            Encryption = new EncryptionEnvelope { Iv = Convert.ToBase64String(iv), AuthTag = Convert.ToBase64String(tag) },
        };
        BroadcastExcept(null, JsonSerializer.Serialize(message, JsonOptions));
    }

    public void RelayImage(byte[] jpeg, string? sourceName = null)
    {
        if (!_settings.SyncImages)
        {
            return;
        }

        var plain = Encoding.UTF8.GetBytes(Convert.ToBase64String(jpeg));
        var hash = EncryptionService.Sha256Hex(plain);
        _echo.NoteSent(hash);
        var (ciphertext, iv, tag) = EncryptionService.Encrypt(CurrentKey(), plain);
        var message = new ClipboardPayload
        {
            Version = 1,
            Type = "image",
            OriginDeviceId = _pairing.DeviceId,
            DeviceName = string.IsNullOrWhiteSpace(sourceName) ? _settings.DisplayName : sourceName.Trim(),
            ContentHash = hash,
            Timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Payload = Convert.ToBase64String(ciphertext),
            Encryption = new EncryptionEnvelope { Iv = Convert.ToBase64String(iv), AuthTag = Convert.ToBase64String(tag) },
        };
        BroadcastExcept(null, JsonSerializer.Serialize(message, JsonOptions));
    }

    public void DisconnectAll()
    {
        foreach (var session in _sessions.Values.ToArray())
        {
            session.Socket.Close();
        }

        foreach (var pending in _pending.Values.ToArray())
        {
            pending.Socket.Close();
        }
    }

    private byte[] CurrentKey() => EncryptionService.DeriveKey(_pairing.Token);

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            session.Socket.Close();
        }

        SessionsClear();
        _server?.Dispose();
        _server = null;
        foreach (var id in _incoming.Keys.ToArray())
        {
            AbortIncoming(id, "ClipBoard is closing", notifyPeer: false);
        }
    }

    private void OnMessage(IWebSocketConnection socket, string json)
    {
        if (_pending.TryGetValue(socket.ConnectionInfo.Id, out var pending))
        {
            HandlePending(pending, json);
            return;
        }

        if (!IsOnline(socket))
        {
            socket.Close(1008);
            return;
        }

        ClipboardPayload? message;
        try
        {
            message = JsonSerializer.Deserialize<ClipboardPayload>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _log.Write("Dropped malformed payload: " + ex.Message);
            return;
        }

        if (message is { Version: 1 } && IsFileType(message.Type))
        {
            OnFileMessage(socket, message, json);
            return;
        }

        if (message is null || message.Version != 1 || (message.Type != "text" && message.Type != "image"))
        {
            _log.Write($"Dropped unsupported payload (version {message?.Version}, type {message?.Type}).");
            return;
        }

        if (_echo.IsEchoOfLocalSend(message.ContentHash) || _echo.WasAppliedFromPeer(message.ContentHash))
        {
            return;
        }

        try
        {
            var plain = EncryptionService.Decrypt(
                CurrentKey(),
                Convert.FromBase64String(message.Encryption.Iv),
                Convert.FromBase64String(message.Payload),
                Convert.FromBase64String(message.Encryption.AuthTag));
            if (!string.Equals(EncryptionService.Sha256Hex(plain), message.ContentHash, StringComparison.Ordinal))
            {
                _log.Write("Dropped payload: content hash does not match plaintext.");
                return;
            }

            if (plain.Length > 8 * 1024 * 1024)
            {
                _log.Write("Dropped oversized payload.");
                return;
            }

            if (message.Type == "image" && !_settings.SyncImages)
            {
                _log.Write("Ignored image from the phone because sync is text only.");
                _history.AddText("Skipped", "Images are off");
                return;
            }

            RememberName(socket, message.DeviceName);
            _echo.NoteApplied(message.ContentHash);
            _echo.NoteSent(message.ContentHash);
            BroadcastExcept(socket, json);

            if (message.Type == "image")
            {
                _suppressImagesUntil = DateTimeOffset.UtcNow.AddSeconds(1);
                var jpeg = Convert.FromBase64String(Encoding.UTF8.GetString(plain));
                var from = FromDevice(message);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        using var stream = new MemoryStream(jpeg);
                        var image = new BitmapImage();
                        image.BeginInit();
                        image.CacheOption = BitmapCacheOption.OnLoad;
                        image.StreamSource = stream;
                        image.EndInit();
                        image.Freeze();
                        Clipboard.SetImage(image);
                        _log.Write($"Applied image from {from}, bytes={jpeg.Length}.");
                        _history.AddImage(from, jpeg);
                    }
                    catch (Exception ex) when (ex is ExternalException or IOException or NotSupportedException)
                    {
                        _log.Write("Could not write the Windows clipboard: " + ex.Message);
                    }
                });
                ImageFromClient?.Invoke(jpeg);
                return;
            }

            var text = Encoding.UTF8.GetString(plain);
            var source = FromDevice(message);
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    Clipboard.SetText(text);
                    _log.Write($"Applied text from {source}, len={text.Length}.");
                    _history.AddText(source, text);
                }
                catch (ExternalException ex)
                {
                    _log.Write("Could not write the Windows clipboard: " + ex.Message);
                }
            });
            TextFromClient?.Invoke(text);
        }
        catch (CryptographicException ex)
        {
            _log.Write("Dropped payload: decryption failed. " + ex.Message);
        }
        catch (FormatException ex)
        {
            _log.Write("Dropped payload: bad base64. " + ex.Message);
        }
    }

}

internal static class QrImage
{
    public static BitmapSource FromPng(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
