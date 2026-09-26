using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClipboardSync.App.Models;
using Fleck;
using Microsoft.Win32;

namespace ClipboardSync.App.Services;

/// <summary>
/// File transfer over the same socket: a <c>file</c> JSON message with encrypted
/// metadata, then binary chunk frames (<see cref="FileChunkCodec"/>), then
/// <c>file-end</c>. <c>file-cancel</c> aborts from either side. Only one chunk is
/// ever in memory; Fleck delivers frames one at a time per connection, so writing
/// synchronously in <see cref="OnBinary"/> also throttles the sender.
/// </summary>
public sealed partial class ClipboardWebSocketServer
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _fileSendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, IncomingFile> _incoming = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _outgoing = new();

    public event Action<string>? FileReceived;

    public bool HasPhone => HasDevice;

    private static bool IsFileType(string? type) => type is "file" or "file-end" or "file-cancel";

    /// <summary>Queues a file for the connected phone. Files are sent one at a time.</summary>
    public Task SendFileAsync(string path) => Task.Run(() => SendFileCoreAsync(path));

    private async Task SendFileCoreAsync(string path)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log.Write($"Cannot read {path}: {ex.Message}");
            return;
        }

        using var cancellation = new CancellationTokenSource();
        var transfer = _transfers.Begin(info.Name, sending: true, info.Length, cancellation);
        var transferId = RandomNumberGenerator.GetBytes(FileChunkCodec.IdSize);
        var idHex = Convert.ToHexString(transferId).ToLowerInvariant();
        _outgoing[idHex] = cancellation;
        try
        {
            await _fileSendGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
            try
            {
                if (OnlineSockets().Count == 0)
                {
                    throw new IOException("No device is connected.");
                }

                var offer = new FileOffer
                {
                    TransferId = idHex,
                    FileName = info.Name,
                    FileSize = info.Length,
                    MimeType = MimeTypeFor(info.Extension),
                    ChunkSize = FileChunkCodec.ChunkSize,
                };
                await SendToAllAsync(Envelope("file", JsonSerializer.SerializeToUtf8Bytes(offer, JsonOptions))).ConfigureAwait(false);

                using var aes = new AesGcm(CurrentKey(), 16);
                await using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[FileChunkCodec.ChunkSize];
                long sent = 0;
                var index = 0;
                while (true)
                {
                    var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellation.Token)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    if (OnlineSockets().Count == 0)
                    {
                        throw new IOException("Every device disconnected.");
                    }

                    var frame = FileChunkCodec.Seal(aes, transferId, index, buffer.AsSpan(0, read));
                    await SendToAllAsync(frame).ConfigureAwait(false);
                    index++;
                    sent += read;
                    _transfers.Report(transfer, sent);
                    if (read < buffer.Length)
                    {
                        break;
                    }
                }

                if (sent != info.Length)
                {
                    throw new IOException("The file changed while it was being sent.");
                }

                var end = new FileControl { TransferId = idHex, Chunks = index, FileSize = sent };
                await SendToAllAsync(Envelope("file-end", JsonSerializer.SerializeToUtf8Bytes(end, JsonOptions))).ConfigureAwait(false);
                _history.AddFile("Sent", path, sent);
                _log.Write($"Sent file {info.Name}, bytes={sent}, chunks={index}.");
            }
            finally
            {
                _fileSendGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            await CancelAllAsync(idHex, "cancelled").ConfigureAwait(false);
            _history.AddText("Skipped", $"Cancelled {info.Name}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            await CancelAllAsync(idHex, "failed").ConfigureAwait(false);
            _log.Write($"File send failed for {info.Name}: {ex.Message}");
            _history.AddText("Skipped", $"Could not send {info.Name}: {ex.Message}");
        }
        finally
        {
            _outgoing.TryRemove(idHex, out _);
            _transfers.End(transfer);
        }
    }

    private async Task SendToAllAsync(string json)
    {
        foreach (var socket in OnlineSockets())
        {
            await SendAsync(socket, json).ConfigureAwait(false);
        }
    }

    private async Task SendToAllAsync(byte[] frame)
    {
        foreach (var socket in OnlineSockets())
        {
            await SendAsync(socket, frame).ConfigureAwait(false);
        }
    }

    private async Task CancelAllAsync(string idHex, string reason)
    {
        foreach (var socket in OnlineSockets())
        {
            await TrySendCancelAsync(socket, idHex, reason).ConfigureAwait(false);
        }
    }

    private async Task TrySendCancelAsync(IWebSocketConnection? client, string idHex, string reason)
    {
        if (client is null || !client.IsAvailable)
        {
            return;
        }

        try
        {
            var cancel = new FileControl { TransferId = idHex, Reason = reason };
            await SendAsync(client, Envelope("file-cancel", JsonSerializer.SerializeToUtf8Bytes(cancel, JsonOptions))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Write("Could not send file-cancel: " + ex.Message);
        }
    }

    private void OnFileMessage(IWebSocketConnection socket, ClipboardPayload message, string json)
    {
        byte[] plain;
        try
        {
            plain = EncryptionService.Decrypt(
                CurrentKey(),
                Convert.FromBase64String(message.Encryption.Iv),
                Convert.FromBase64String(message.Payload),
                Convert.FromBase64String(message.Encryption.AuthTag));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            _log.Write($"Dropped {message.Type} message: {ex.Message}");
            return;
        }

        if (!string.Equals(EncryptionService.Sha256Hex(plain), message.ContentHash, StringComparison.Ordinal))
        {
            _log.Write($"Dropped {message.Type} message: content hash does not match plaintext.");
            return;
        }

        RememberName(socket, message.DeviceName);
        BroadcastExcept(socket, json);

        try
        {
            switch (message.Type)
            {
                case "file":
                    BeginIncoming(socket, JsonSerializer.Deserialize<FileOffer>(plain, JsonOptions)!, message.DeviceName);
                    break;
                case "file-end":
                    FinishIncoming(JsonSerializer.Deserialize<FileControl>(plain, JsonOptions)!);
                    break;
                case "file-cancel":
                    var cancel = JsonSerializer.Deserialize<FileControl>(plain, JsonOptions)!;
                    AbortIncoming(cancel.TransferId, "the phone cancelled it", notifyPeer: false);
                    if (_outgoing.TryGetValue(cancel.TransferId, out var outgoing))
                    {
                        try
                        {
                            outgoing.Cancel();
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    }

                    break;
            }
        }
        catch (JsonException ex)
        {
            _log.Write($"Dropped {message.Type} message: {ex.Message}");
        }
    }

    private void BeginIncoming(IWebSocketConnection socket, FileOffer offer, string? from)
    {
        if (offer.TransferId.Length != FileChunkCodec.IdSize * 2 || offer.FileSize < 0 ||
            offer.ChunkSize <= 0 || offer.ChunkSize > 4 * FileChunkCodec.ChunkSize)
        {
            _log.Write("Dropped file offer with invalid metadata.");
            return;
        }

        var name = DownloadFolder.SafeFileName(offer.FileName);
        var folder = DownloadFolder.Path;
        Directory.CreateDirectory(folder);
        var partPath = Path.Combine(folder, $"{name}.{offer.TransferId[..8]}.part");
        FileStream stream;
        try
        {
            stream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write($"Cannot create {partPath}: {ex.Message}");
            return;
        }

        var incoming = new IncomingFile
        {
            Socket = socket,
            FileName = name,
            FileSize = offer.FileSize,
            PartPath = partPath,
            Stream = stream,
            Aes = new AesGcm(CurrentKey(), 16),
            Plain = new byte[offer.ChunkSize],
            Transfer = _transfers.Begin(name, sending: false, offer.FileSize),
            From = string.IsNullOrWhiteSpace(from) ? "Received" : "From " + from.Trim(),
        };
        if (_incoming.TryRemove(offer.TransferId, out var previous))
        {
            Discard(previous);
        }

        _incoming[offer.TransferId] = incoming;
        _log.Write($"Receiving file {name}, bytes={offer.FileSize}.");
    }

    private void OnBinary(IWebSocketConnection socket, byte[] frame)
    {
        if (!IsOnline(socket))
        {
            socket.Close(1008);
            return;
        }

        if (frame.Length < FileChunkCodec.Overhead)
        {
            return;
        }

        var idHex = FileChunkCodec.TransferIdHex(frame);
        if (!_incoming.TryGetValue(idHex, out var incoming))
        {
            return;
        }

        var length = FileChunkCodec.PlainLength(frame);
        if (FileChunkCodec.Index(frame) != incoming.NextIndex ||
            length > incoming.Plain.Length ||
            incoming.Written + length > incoming.FileSize)
        {
            AbortIncoming(idHex, "a chunk arrived out of order or oversized");
            return;
        }

        try
        {
            FileChunkCodec.Open(incoming.Aes, frame, incoming.Plain);
            incoming.Stream.Write(incoming.Plain, 0, length);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            AbortIncoming(idHex, ex.Message);
            return;
        }

        incoming.NextIndex++;
        incoming.Written += length;
        _transfers.Report(incoming.Transfer, incoming.Written);
        BroadcastExcept(socket, frame);
    }

    private void FinishIncoming(FileControl end)
    {
        if (!_incoming.TryRemove(end.TransferId, out var incoming))
        {
            return;
        }

        if (end.Chunks != incoming.NextIndex || end.FileSize != incoming.FileSize || incoming.Written != incoming.FileSize)
        {
            _log.Write($"Discarded {incoming.FileName}: received {incoming.Written} of {incoming.FileSize} bytes.");
            _history.AddText("Skipped", $"{incoming.FileName} arrived incomplete");
            Discard(incoming);
            return;
        }

        try
        {
            incoming.Stream.Dispose();
            incoming.Aes.Dispose();
            var finalPath = DownloadFolder.UniquePath(Path.GetDirectoryName(incoming.PartPath)!, incoming.FileName);
            File.Move(incoming.PartPath, finalPath);
            _history.AddFile(incoming.From, finalPath, incoming.Written);
            _log.Write($"Saved file {finalPath}.");
            FileReceived?.Invoke(Path.GetFileName(finalPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write($"Could not save {incoming.FileName}: {ex.Message}");
            Discard(incoming);
        }
        finally
        {
            _transfers.End(incoming.Transfer);
        }
    }

    private void AbortIncoming(string idHex, string reason, bool notifyPeer = true)
    {
        if (!_incoming.TryRemove(idHex, out var incoming))
        {
            return;
        }

        _log.Write($"Discarded incoming {incoming.FileName}: {reason}.");
        if (notifyPeer)
        {
            _ = TrySendCancelAsync(incoming.Socket, idHex, "rejected");
        }

        _history.AddText("Skipped", $"{incoming.FileName} was not received");
        Discard(incoming);
    }

    private void AbortIncomingFrom(Guid socketId)
    {
        foreach (var pair in _incoming.ToArray())
        {
            if (pair.Value.Socket.ConnectionInfo.Id == socketId)
            {
                AbortIncoming(pair.Key, "the phone disconnected", notifyPeer: false);
            }
        }
    }

    private void Discard(IncomingFile incoming)
    {
        try
        {
            incoming.Stream.Dispose();
            incoming.Aes.Dispose();
            File.Delete(incoming.PartPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write($"Could not delete {incoming.PartPath}: {ex.Message}");
        }

        _transfers.End(incoming.Transfer);
    }

    private string Envelope(string type, byte[] plain)
    {
        var (ciphertext, iv, tag) = EncryptionService.Encrypt(CurrentKey(), plain);
        return JsonSerializer.Serialize(
            new ClipboardPayload
            {
                Version = 1,
                Type = type,
                OriginDeviceId = _pairing.DeviceId,
                ContentHash = EncryptionService.Sha256Hex(plain),
                Timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                Payload = Convert.ToBase64String(ciphertext),
                Encryption = new EncryptionEnvelope
                {
                    Iv = Convert.ToBase64String(iv),
                    AuthTag = Convert.ToBase64String(tag),
                },
            },
            JsonOptions);
    }

    /// <summary>Fleck does not serialize concurrent writes on one connection, so every send goes through this lock.</summary>
    private async Task SendAsync(IWebSocketConnection client, string json)
    {
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await client.Send(json).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task SendAsync(IWebSocketConnection client, byte[] frame)
    {
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await client.Send(frame).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static string MimeTypeFor(string extension)
    {
        try
        {
            using var key = Registry.ClassesRoot.OpenSubKey(extension);
            if (key?.GetValue("Content Type") is string mime && mime.Length > 0)
            {
                return mime;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return "application/octet-stream";
    }

    private sealed class IncomingFile
    {
        public required IWebSocketConnection Socket { get; init; }
        public required string FileName { get; init; }
        public required long FileSize { get; init; }
        public required string PartPath { get; init; }
        public required FileStream Stream { get; init; }
        public required AesGcm Aes { get; init; }
        public required byte[] Plain { get; init; }
        public required FileTransfer Transfer { get; init; }
        public string From { get; init; } = "Received";
        public int NextIndex { get; set; }
        public long Written { get; set; }
    }
}
