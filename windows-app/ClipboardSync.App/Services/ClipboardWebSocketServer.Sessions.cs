using System.Collections.Concurrent;
using System.Security.Cryptography;
using ClipboardSync.App.Models;
using System.Text;
using System.Text.Json;
using Fleck;

namespace ClipboardSync.App.Services;

internal sealed class ClientSession
{
    public required string DeviceId { get; init; }
    public string DeviceName { get; set; } = "Device";
    public required IWebSocketConnection Socket { get; init; }
}

/// <summary>A socket that has not presented the pairing token. It may only speak the PIN handshake.</summary>
internal sealed class PendingPair
{
    public required IWebSocketConnection Socket { get; init; }
    public string? DeviceId { get; set; }
    public string DeviceName { get; set; } = "PC";
    public string? Pin { get; set; }
    public DateTimeOffset Expires { get; set; }
    public int Attempts { get; set; }
}

public readonly record struct PinChallenge(Guid ConnectionId, string DeviceName, string Pin);

public sealed partial class ClipboardWebSocketServer
{
    public const string DeviceHeader = "X-Clipboard-Device";
    public const string NameHeader = "X-Clipboard-Name";

    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, PendingPair> _pending = new();
    private readonly ConcurrentDictionary<string, (int Fails, DateTimeOffset Window)> _pinFails = new();

    public bool HasDevice => !_sessions.IsEmpty;

    public event Action<PinChallenge>? PinRequested;

    public event Action<Guid>? PinCleared;

    /// <summary>Plaintext from a paired phone or PC, already accepted. The app forwards it to an upstream hub.</summary>
    public event Action<string>? TextFromClient;

    public event Action<byte[]>? ImageFromClient;

    private void SessionsClear()
    {
        foreach (var id in _pending.Keys)
        {
            PinCleared?.Invoke(id);
        }

        _sessions.Clear();
        _pending.Clear();
    }

    private void OnOpen(IWebSocketConnection socket)
    {
        var token = PresentedToken(socket);
        if (token == TokenCheck.Wrong)
        {
            _log.Write($"Rejected WebSocket from {socket.ConnectionInfo.ClientIpAddress}: pairing token did not match.");
            socket.Close(1008);
            return;
        }

        if (token == TokenCheck.Missing)
        {
            if (PinBanned(socket))
            {
                socket.Close(1008);
                return;
            }

            _pending[socket.ConnectionInfo.Id] = new PendingPair { Socket = socket };
            _log.Write($"Pairing connection from {socket.ConnectionInfo.ClientIpAddress}.");
            return;
        }

        var deviceId = Header(socket, DeviceHeader);
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            deviceId = "device-" + socket.ConnectionInfo.Id.ToString("N");
        }

        var name = Header(socket, NameHeader);
        RememberSession(socket, deviceId, string.IsNullOrWhiteSpace(name) ? "Device" : name);
    }

    private void OnClose(IWebSocketConnection socket)
    {
        if (_pending.TryRemove(socket.ConnectionInfo.Id, out _))
        {
            PinCleared?.Invoke(socket.ConnectionInfo.Id);
        }

        var gone = _sessions.FirstOrDefault(pair => pair.Value.Socket.ConnectionInfo.Id == socket.ConnectionInfo.Id);
        if (!string.IsNullOrEmpty(gone.Key))
        {
            _sessions.TryRemove(gone.Key, out _);
        }

        AbortIncomingFrom(socket.ConnectionInfo.Id);
        PublishPresence();
    }

    private void RememberSession(IWebSocketConnection socket, string deviceId, string deviceName)
    {
        var previous = _sessions.GetValueOrDefault(deviceId);
        if (previous is not null && previous.Socket.ConnectionInfo.Id != socket.ConnectionInfo.Id)
        {
            previous.Socket.Close(1000);
        }

        _sessions[deviceId] = new ClientSession
        {
            DeviceId = deviceId,
            DeviceName = deviceName.Trim(),
            Socket = socket,
        };
        _log.Write($"{deviceName} connected from {socket.ConnectionInfo.ClientIpAddress}.");
        PublishPresence();
    }

    private void PublishPresence()
    {
        var count = _sessions.Count;
        var status = count switch
        {
            0 => "Waiting for a device",
            1 => "1 device connected",
            _ => $"{count} devices connected",
        };
        _log.Write(status + ".");
        StatusChanged?.Invoke(status);
    }

    private bool IsOnline(IWebSocketConnection socket) =>
        _sessions.Values.Any(session => session.Socket.ConnectionInfo.Id == socket.ConnectionInfo.Id);

    private IReadOnlyList<IWebSocketConnection> OnlineSockets(IWebSocketConnection? except = null)
    {
        return _sessions.Values
            .Select(session => session.Socket)
            .Where(socket => socket.IsAvailable && (except is null || socket.ConnectionInfo.Id != except.ConnectionInfo.Id))
            .ToArray();
    }

    private void RememberName(IWebSocketConnection socket, string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return;
        }

        var session = _sessions.Values.FirstOrDefault(item => item.Socket.ConnectionInfo.Id == socket.ConnectionInfo.Id);
        if (session is null || session.DeviceName == deviceName.Trim())
        {
            return;
        }

        session.DeviceName = deviceName.Trim();
        PublishPresence();
    }

    private bool HandlePending(PendingPair pending, string json)
    {
        string? type;
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
            type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        }
        catch (JsonException)
        {
            pending.Socket.Close(1008);
            return true;
        }

        if (type == "pair-hello")
        {
            var deviceId = root.TryGetProperty("deviceId", out var idElement) ? idElement.GetString() : null;
            var deviceName = root.TryGetProperty("deviceName", out var name) ? name.GetString() : null;
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                pending.Socket.Close(1008);
                return true;
            }

            pending.DeviceId = deviceId;
            pending.DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "PC" : deviceName.Trim();
            pending.Pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            pending.Expires = DateTimeOffset.UtcNow.AddSeconds(90);
            pending.Attempts = 0;
            PinRequested?.Invoke(new PinChallenge(pending.Socket.ConnectionInfo.Id, pending.DeviceName, pending.Pin));
            return true;
        }

        if (type != "pair-pin")
        {
            pending.Socket.Close(1008);
            return true;
        }

        var pin = root.TryGetProperty("pin", out var pinElement) ? pinElement.GetString() ?? "" : "";
        if (pending.Pin is null || DateTimeOffset.UtcNow > pending.Expires || pending.DeviceId is null)
        {
            SendPair(pending, new { v = 1, type = "pair-reject", reason = "That PIN expired. Try connecting again." });
            pending.Socket.Close(1008);
            return true;
        }

        pending.Attempts++;
        var presented = Encoding.UTF8.GetBytes(pin.Trim());
        var expected = Encoding.UTF8.GetBytes(pending.Pin);
        var matches = presented.Length == expected.Length && CryptographicOperations.FixedTimeEquals(presented, expected);
        if (!matches)
        {
            NotePinFail(pending.Socket);
            if (pending.Attempts >= 5)
            {
                SendPair(pending, new { v = 1, type = "pair-reject", reason = "Too many attempts." });
                pending.Socket.Close(1008);
            }
            else
            {
                SendPair(pending, new { v = 1, type = "pair-reject", reason = "Wrong PIN." });
            }

            return true;
        }

        SendPair(pending, new
        {
            v = 1,
            type = "pair-ok",
            token = _pairing.Token,
            hubDeviceId = _pairing.DeviceId,
            hubName = _settings.DisplayName,
            port = PairingService.Port,
        });
        var connectionId = pending.Socket.ConnectionInfo.Id;
        _pending.TryRemove(connectionId, out _);
        PinCleared?.Invoke(connectionId);
        RememberSession(pending.Socket, pending.DeviceId, pending.DeviceName);
        return true;
    }

    private void SendPair(PendingPair pending, object body)
    {
        _ = SendAsync(pending.Socket, JsonSerializer.Serialize(body, JsonOptions));
    }

    private void BroadcastExcept(IWebSocketConnection? except, string json)
    {
        foreach (var socket in OnlineSockets(except))
        {
            _ = SendAsync(socket, json).ContinueWith(
                task => _log.Write("Send failed: " + task.Exception?.GetBaseException().Message),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private void BroadcastExcept(IWebSocketConnection? except, byte[] frame)
    {
        foreach (var socket in OnlineSockets(except))
        {
            _ = SendAsync(socket, frame).ContinueWith(
                task => _log.Write("Send failed: " + task.Exception?.GetBaseException().Message),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private static string FromDevice(ClipboardPayload message) =>
        string.IsNullOrWhiteSpace(message.DeviceName) ? "Received" : "From " + message.DeviceName.Trim();

    private enum TokenCheck { Missing, Match, Wrong }

    private TokenCheck PresentedToken(IWebSocketConnection socket)
    {
        var presented = Header(socket, TokenHeader);
        if (presented is null)
        {
            return TokenCheck.Missing;
        }

        var given = Encoding.UTF8.GetBytes(presented);
        var expected = Encoding.UTF8.GetBytes(_pairing.Token);
        return given.Length == expected.Length && CryptographicOperations.FixedTimeEquals(given, expected)
            ? TokenCheck.Match
            : TokenCheck.Wrong;
    }

    private static string? Header(IWebSocketConnection socket, string name)
    {
        var headers = socket.ConnectionInfo.Headers;
        if (headers is null)
        {
            return null;
        }

        foreach (var pair in headers)
        {
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private bool PinBanned(IWebSocketConnection socket)
    {
        var ip = socket.ConnectionInfo.ClientIpAddress ?? "";
        if (!_pinFails.TryGetValue(ip, out var entry))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow - entry.Window > TimeSpan.FromMinutes(10))
        {
            _pinFails.TryRemove(ip, out _);
            return false;
        }

        return entry.Fails >= 20;
    }

    private void NotePinFail(IWebSocketConnection socket)
    {
        var ip = socket.ConnectionInfo.ClientIpAddress ?? "";
        _pinFails.AddOrUpdate(
            ip,
            _ => (1, DateTimeOffset.UtcNow),
            (_, entry) => DateTimeOffset.UtcNow - entry.Window > TimeSpan.FromMinutes(10)
                ? (1, DateTimeOffset.UtcNow)
                : (entry.Fails + 1, entry.Window));
    }
}
