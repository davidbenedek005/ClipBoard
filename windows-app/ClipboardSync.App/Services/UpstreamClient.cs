using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using ClipboardSync.App.Models;

namespace ClipboardSync.App.Services;

/// <summary>
/// This PC as a spoke of another PC's hub. Pairing is a 6-digit PIN shown on
/// the hub; the hub token comes back only after the PIN matches, and later
/// connections send it in the same header phones use. Clipboard JSON uses the
/// hub token, so the hub can forward one ciphertext to every other device.
/// </summary>
public sealed class UpstreamClient : IDisposable
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly PairingService _local;
    private readonly AppSettings _settings;
    private readonly EchoGuard _echo;
    private readonly SyncHistory _history;
    private readonly FileTransferTracker _transfers;
    private readonly ClipboardEventLog _log;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _fileGate = new(1, 1);
    private readonly object _gate = new();

    private ClientWebSocket? _socket;
    private CancellationTokenSource _life = new();
    private TaskCompletionSource<string?>? _pinWait;
    private SavedHub? _hub;
    private bool _ready;
    private bool _leaveRequested;
    private IncomingFile? _incoming;

    public UpstreamClient(
        PairingService local,
        AppSettings settings,
        EchoGuard echo,
        SyncHistory history,
        FileTransferTracker transfers,
        ClipboardEventLog log)
    {
        _local = local;
        _settings = settings;
        _echo = echo;
        _history = history;
        _transfers = transfers;
        _log = log;
        _hub = SavedHub.Load();
    }

    public bool IsReady => _ready;

    public string? HubName => _hub?.HubName;

    public event Action? PinNeeded;

    public event Action<string>? StatusChanged;

    public event Action<string, string>? TextReceived;

    public event Action<string, byte[]>? ImageReceived;

    public void ConnectSaved()
    {
        if (_hub is null)
        {
            return;
        }

        _leaveRequested = false;
        _ = RunAsync(_hub, pairing: false);
    }

    public void Join(DiscoveredHub hub)
    {
        _leaveRequested = false;
        _hub = new SavedHub(hub.DeviceId, hub.Name, hub.Ip, hub.Port, Token: "");
        _ = RunAsync(_hub, pairing: true);
    }

    public void SubmitPin(string pin)
    {
        _pinWait?.TrySetResult(pin.Trim());
    }

    public void Leave()
    {
        _leaveRequested = true;
        _ready = false;
        _hub = null;
        SavedHub.Delete();
        CancelLife();
        StatusChanged?.Invoke("Not joined");
    }

    /// <summary>Local clipboard. Skipped when this text was just applied from the network.</summary>
    public void SendText(string text) => SendClipboard("text", Encoding.UTF8.GetBytes(text), fromClipboard: true);

    public void SendImage(byte[] jpeg)
    {
        if (!_settings.SyncImages)
        {
            return;
        }

        var plain = Encoding.UTF8.GetBytes(Convert.ToBase64String(jpeg));
        if (plain.Length > 8 * 1024 * 1024)
        {
            return;
        }

        SendClipboard("image", plain, fromClipboard: true);
    }

    /// <summary>A phone paired to this PC. Forward even though we just applied it locally.</summary>
    public void ForwardText(string text) => SendClipboard("text", Encoding.UTF8.GetBytes(text), fromClipboard: false);

    public void ForwardImage(byte[] jpeg)
    {
        if (!_settings.SyncImages)
        {
            return;
        }

        SendClipboard("image", Encoding.UTF8.GetBytes(Convert.ToBase64String(jpeg)), fromClipboard: false);
    }

    public Task SendFileAsync(string path) => Task.Run(() => SendFileCoreAsync(path));

    public void Dispose()
    {
        _leaveRequested = true;
        CancelLife();
        _sendLock.Dispose();
        _fileGate.Dispose();
    }

    private void SendClipboard(string type, byte[] plain, bool fromClipboard)
    {
        if (!_ready || _hub?.Token.Length == 0)
        {
            return;
        }

        var hash = EncryptionService.Sha256Hex(plain);
        if (fromClipboard && _echo.WasAppliedFromPeer(hash))
        {
            return;
        }

        _echo.NoteSent(hash);
        _ = SendAsync(Envelope(type, plain, hash));
    }

    private async Task RunAsync(SavedHub hub, bool pairing)
    {
        CancelLife();
        var life = _life;
        var token = life.Token;
        try
        {
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            if (!pairing && !string.IsNullOrEmpty(hub.Token))
            {
                socket.Options.SetRequestHeader(ClipboardWebSocketServer.TokenHeader, hub.Token);
                socket.Options.SetRequestHeader(ClipboardWebSocketServer.DeviceHeader, _local.DeviceId);
                socket.Options.SetRequestHeader(ClipboardWebSocketServer.NameHeader, Ascii(_settings.DisplayName));
            }

            await socket.ConnectAsync(new Uri($"ws://{hub.Ip}:{hub.Port}/"), token).ConfigureAwait(false);
            lock (_gate)
            {
                _socket = socket;
            }

            if (pairing)
            {
                await SendAsync(JsonSerializer.Serialize(new
                {
                    v = 1,
                    type = "pair-hello",
                    deviceId = _local.DeviceId,
                    deviceName = _settings.DisplayName,
                })).ConfigureAwait(false);
                StatusChanged?.Invoke($"Enter the PIN shown on {hub.HubName}");
                Application.Current?.Dispatcher.BeginInvoke(() => PinNeeded?.Invoke());
                _pinWait = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var pin = await _pinWait.Task.WaitAsync(TimeSpan.FromSeconds(90), token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(pin))
                {
                    StatusChanged?.Invoke("Pairing cancelled");
                    return;
                }

                await SendAsync(JsonSerializer.Serialize(new { v = 1, type = "pair-pin", pin })).ConfigureAwait(false);
                var reply = await ReceiveTextAsync(socket, token).ConfigureAwait(false);
                var ok = JsonSerializer.Deserialize<PairOk>(reply, Json);
                if (ok?.Type != "pair-ok" || string.IsNullOrEmpty(ok.Token))
                {
                    StatusChanged?.Invoke(ok?.Reason ?? "The PIN was not accepted");
                    return;
                }

                _hub = hub with { Token = ok.Token, HubName = ok.HubName ?? hub.HubName, HubDeviceId = ok.HubDeviceId ?? hub.HubDeviceId };
                _hub.Save();
            }

            _ready = true;
            StatusChanged?.Invoke($"Joined {_hub?.HubName}");
            _log.Write($"Joined hub {_hub?.HubName} at {hub.Ip}.");
            await ReadLoopAsync(socket, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is WebSocketException or IOException or JsonException or TimeoutException)
        {
            _log.Write("Hub connection failed: " + ex.Message);
            StatusChanged?.Invoke("Could not join that PC");
        }
        finally
        {
            _ready = false;
            lock (_gate)
            {
                if (_socket is { State: WebSocketState.Open })
                {
                    // Closed by CancelLife or the remote side.
                }

                _socket = null;
            }

            DiscardIncoming("disconnected");
            if (!_leaveRequested && _hub?.Token.Length > 0 && !token.IsCancellationRequested)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2000).ConfigureAwait(false);
                    if (!_leaveRequested)
                    {
                        await RunAsync(_hub, pairing: false).ConfigureAwait(false);
                    }
                });
            }
        }
    }

    private async Task ReadLoopAsync(ClientWebSocket socket, CancellationToken token)
    {
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var (kind, bytes) = await ReceiveAsync(socket, token).ConfigureAwait(false);
            if (kind == WebSocketMessageType.Close)
            {
                return;
            }

            if (kind == WebSocketMessageType.Binary)
            {
                AcceptChunk(bytes);
            }
            else
            {
                AcceptText(Encoding.UTF8.GetString(bytes));
            }
        }
    }

    private void AcceptText(string json)
    {
        ClipboardPayload? message;
        try
        {
            message = JsonSerializer.Deserialize<ClipboardPayload>(json, Json);
        }
        catch (JsonException)
        {
            return;
        }

        if (message is null || message.Version != 1 || _hub is null)
        {
            return;
        }

        if (message.Type is "file" or "file-end" or "file-cancel")
        {
            AcceptFileControl(message);
            return;
        }

        if (message.Type is not ("text" or "image"))
        {
            return;
        }

        byte[] plain;
        try
        {
            plain = EncryptionService.Decrypt(
                EncryptionService.DeriveKey(_hub.Token),
                Convert.FromBase64String(message.Encryption.Iv),
                Convert.FromBase64String(message.Payload),
                Convert.FromBase64String(message.Encryption.AuthTag));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            _log.Write("Dropped hub payload: " + ex.Message);
            return;
        }

        if (!string.Equals(EncryptionService.Sha256Hex(plain), message.ContentHash, StringComparison.Ordinal))
        {
            return;
        }

        if (_echo.IsEchoOfLocalSend(message.ContentHash) || _echo.WasAppliedFromPeer(message.ContentHash))
        {
            return;
        }

        _echo.NoteApplied(message.ContentHash);
        var name = string.IsNullOrWhiteSpace(message.DeviceName) ? _hub.HubName : message.DeviceName.Trim();
        if (message.Type == "text")
        {
            TextReceived?.Invoke(name, Encoding.UTF8.GetString(plain));
        }
        else if (_settings.SyncImages)
        {
            try
            {
                ImageReceived?.Invoke(name, Convert.FromBase64String(Encoding.UTF8.GetString(plain)));
            }
            catch (FormatException)
            {
                _log.Write("Dropped hub image: bad base64.");
            }
        }
    }

    private void AcceptFileControl(ClipboardPayload message)
    {
        if (_hub is null)
        {
            return;
        }

        byte[] plain;
        try
        {
            plain = EncryptionService.Decrypt(
                EncryptionService.DeriveKey(_hub.Token),
                Convert.FromBase64String(message.Encryption.Iv),
                Convert.FromBase64String(message.Payload),
                Convert.FromBase64String(message.Encryption.AuthTag));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            _log.Write("Dropped hub file message: " + ex.Message);
            return;
        }

        if (message.Type == "file-cancel")
        {
            DiscardIncoming("the other PC cancelled it");
            return;
        }

        if (message.Type == "file")
        {
            var offer = JsonSerializer.Deserialize<FileOffer>(plain, Json);
            if (offer is null)
            {
                return;
            }

            BeginIncoming(offer, message.DeviceName);
            return;
        }

        var end = JsonSerializer.Deserialize<FileControl>(plain, Json);
        if (end is null || _incoming is null || end.TransferId != _incoming.Id)
        {
            return;
        }

        if (end.Chunks != _incoming.NextIndex || _incoming.Written != _incoming.Size)
        {
            _history.AddText("Skipped", $"{_incoming.Name} arrived incomplete");
            DiscardIncoming("incomplete");
            return;
        }

        try
        {
            _incoming.Stream.Dispose();
            _incoming.Aes.Dispose();
            var finalPath = DownloadFolder.UniquePath(Path.GetDirectoryName(_incoming.PartPath)!, _incoming.Name);
            File.Move(_incoming.PartPath, finalPath);
            _history.AddFile("From " + _incoming.From, finalPath, _incoming.Written);
            _log.Write($"Saved file from hub: {finalPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write("Could not save hub file: " + ex.Message);
        }
        finally
        {
            _transfers.End(_incoming.Transfer);
            _incoming = null;
        }
    }

    private void BeginIncoming(FileOffer offer, string? from)
    {
        DiscardIncoming("replaced");
        var name = DownloadFolder.SafeFileName(offer.FileName);
        var folder = DownloadFolder.Path;
        Directory.CreateDirectory(folder);
        var part = Path.Combine(folder, $"{name}.{offer.TransferId[..Math.Min(8, offer.TransferId.Length)]}.part");
        var stream = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.SequentialScan);
        _incoming = new IncomingFile
        {
            Id = offer.TransferId,
            Name = name,
            From = string.IsNullOrWhiteSpace(from) ? "PC" : from.Trim(),
            Size = offer.FileSize,
            PartPath = part,
            Stream = stream,
            Aes = new AesGcm(EncryptionService.DeriveKey(_hub!.Token), 16),
            Plain = new byte[Math.Clamp(offer.ChunkSize, 1, FileChunkCodec.ChunkSize * 4)],
            Transfer = _transfers.Begin(name, sending: false, offer.FileSize),
        };
    }

    private void AcceptChunk(byte[] frame)
    {
        var incoming = _incoming;
        if (incoming is null || frame.Length < FileChunkCodec.Overhead)
        {
            return;
        }

        if (!string.Equals(FileChunkCodec.TransferIdHex(frame), incoming.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var length = FileChunkCodec.PlainLength(frame);
        if (FileChunkCodec.Index(frame) != incoming.NextIndex || length > incoming.Plain.Length)
        {
            DiscardIncoming("chunk out of order");
            return;
        }

        try
        {
            FileChunkCodec.Open(incoming.Aes, frame, incoming.Plain);
            incoming.Stream.Write(incoming.Plain, 0, length);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            DiscardIncoming(ex.Message);
            return;
        }

        incoming.NextIndex++;
        incoming.Written += length;
        _transfers.Report(incoming.Transfer, incoming.Written);
    }

    private void DiscardIncoming(string reason)
    {
        var incoming = _incoming;
        if (incoming is null)
        {
            return;
        }

        _incoming = null;
        _log.Write($"Discarded hub file {incoming.Name}: {reason}.");
        try
        {
            incoming.Stream.Dispose();
            incoming.Aes.Dispose();
            File.Delete(incoming.PartPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        _transfers.End(incoming.Transfer);
    }

    private async Task SendFileCoreAsync(string path)
    {
        if (!_ready || _hub is null)
        {
            _history.AddText("Skipped", "Join a PC before sending a file there");
            return;
        }

        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return;
        }

        var transfer = _transfers.Begin(info.Name, sending: true, info.Length);
        var transferId = RandomNumberGenerator.GetBytes(FileChunkCodec.IdSize);
        var idHex = Convert.ToHexString(transferId).ToLowerInvariant();
        try
        {
            await _fileGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var offer = JsonSerializer.SerializeToUtf8Bytes(new FileOffer
                {
                    TransferId = idHex,
                    FileName = info.Name,
                    FileSize = info.Length,
                    MimeType = "application/octet-stream",
                    ChunkSize = FileChunkCodec.ChunkSize,
                }, Json);
                await SendAsync(Envelope("file", offer, EncryptionService.Sha256Hex(offer))).ConfigureAwait(false);
                using var aes = new AesGcm(EncryptionService.DeriveKey(_hub.Token), 16);
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
                var buffer = new byte[FileChunkCodec.ChunkSize];
                long sent = 0;
                var index = 0;
                while (true)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    await SendAsync(FileChunkCodec.Seal(aes, transferId, index, buffer.AsSpan(0, read))).ConfigureAwait(false);
                    index++;
                    sent += read;
                    _transfers.Report(transfer, sent);
                    if (read < buffer.Length)
                    {
                        break;
                    }
                }

                var end = JsonSerializer.SerializeToUtf8Bytes(new FileControl { TransferId = idHex, Chunks = index, FileSize = sent }, Json);
                await SendAsync(Envelope("file-end", end, EncryptionService.Sha256Hex(end))).ConfigureAwait(false);
                _history.AddFile("Sent", path, sent);
            }
            finally
            {
                _fileGate.Release();
            }
        }
        catch (Exception ex) when (ex is IOException or WebSocketException or CryptographicException or ObjectDisposedException)
        {
            _log.Write($"File send to hub failed: {ex.Message}");
            _history.AddText("Skipped", $"Could not send {info.Name}");
        }
        finally
        {
            _transfers.End(transfer);
        }
    }

    private string Envelope(string type, byte[] plain, string? hash = null)
    {
        hash ??= EncryptionService.Sha256Hex(plain);
        var (ciphertext, iv, tag) = EncryptionService.Encrypt(EncryptionService.DeriveKey(_hub!.Token), plain);
        return JsonSerializer.Serialize(new ClipboardPayload
        {
            Version = 1,
            Type = type,
            OriginDeviceId = _local.DeviceId,
            DeviceName = _settings.DisplayName,
            ContentHash = hash,
            Timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            Payload = Convert.ToBase64String(ciphertext),
            Encryption = new EncryptionEnvelope
            {
                Iv = Convert.ToBase64String(iv),
                AuthTag = Convert.ToBase64String(tag),
            },
        }, Json);
    }

    private async Task SendAsync(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await SendRawAsync(bytes, WebSocketMessageType.Text).ConfigureAwait(false);
    }

    private async Task SendAsync(byte[] frame)
    {
        await SendRawAsync(frame, WebSocketMessageType.Binary).ConfigureAwait(false);
    }

    private async Task SendRawAsync(byte[] bytes, WebSocketMessageType kind)
    {
        ClientWebSocket? socket;
        lock (_gate)
        {
            socket = _socket;
        }

        if (socket is null || socket.State != WebSocketState.Open)
        {
            return;
        }

        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await socket.SendAsync(bytes, kind, endOfMessage: true, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static async Task<string> ReceiveTextAsync(ClientWebSocket socket, CancellationToken token)
    {
        var (kind, bytes) = await ReceiveAsync(socket, token).ConfigureAwait(false);
        if (kind != WebSocketMessageType.Text)
        {
            throw new IOException("Expected a text reply from the hub.");
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static async Task<(WebSocketMessageType Kind, byte[] Bytes)> ReceiveAsync(ClientWebSocket socket, CancellationToken token)
    {
        using var data = new MemoryStream();
        var buffer = new byte[64 * 1024];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return (WebSocketMessageType.Close, []);
            }

            data.Write(buffer, 0, result.Count);
            if (data.Length > 16 * 1024 * 1024)
            {
                throw new IOException("Hub message is over 16 MB.");
            }
        }
        while (!result.EndOfMessage);

        return (result.MessageType, data.ToArray());
    }

    private void CancelLife()
    {
        var previous = _life;
        _life = new CancellationTokenSource();
        previous.Cancel();
        _pinWait?.TrySetResult(null);
        lock (_gate)
        {
            if (_socket is { State: WebSocketState.Open })
            {
                try
                {
                    _socket.Abort();
                }
                catch (WebSocketException)
                {
                }
            }
        }
    }

    private static string Ascii(string name)
    {
        var clean = new string(name.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').ToArray());
        return clean.Length <= 40 ? clean : clean[..40];
    }

    private sealed class IncomingFile
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string From { get; init; }
        public required long Size { get; init; }
        public required string PartPath { get; init; }
        public required FileStream Stream { get; init; }
        public required AesGcm Aes { get; init; }
        public required byte[] Plain { get; init; }
        public required FileTransfer Transfer { get; init; }
        public int NextIndex { get; set; }
        public long Written { get; set; }
    }

    private sealed record PairOk(string? Type, string? Token, string? HubDeviceId, string? HubName, string? Reason);

    private sealed record SavedHub(string HubDeviceId, string HubName, string Ip, int Port, string Token)
    {
        private static string Path =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBoard", "upstream.json");

        public static SavedHub? Load()
        {
            try
            {
                return File.Exists(Path) ? JsonSerializer.Deserialize<SavedHub>(File.ReadAllText(Path), Json) : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                return null;
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, Json));
        }

        public static void Delete()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
            }
        }
    }
}
