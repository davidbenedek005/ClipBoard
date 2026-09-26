using System.Collections.ObjectModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace ClipboardSync.App.Services;

public sealed class DiscoveredHub
{
    public required string DeviceId { get; init; }
    public required string Name { get; set; }
    public required string Ip { get; set; }
    public required int Port { get; set; }
    public DateTimeOffset LastSeen { get; set; }

    public string Label => $"{Name}  ·  {Ip}";
}

/// <summary>
/// Presence only. The beacon names this PC and its WebSocket port. It never
/// contains the pairing token. Other PCs answer a probe, so a laptop sees a
/// hub within a second of opening Pairing.
/// </summary>
public sealed class LanDiscovery : IDisposable
{
    public const int Port = 53212;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _deviceId;
    private readonly Func<string> _name;
    private readonly UdpClient _socket;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dispatcher _dispatcher;
    private readonly object _gate = new();

    public ObservableCollection<DiscoveredHub> Hubs { get; } = [];

    public LanDiscovery(string deviceId, Func<string> name)
    {
        _deviceId = deviceId;
        _name = name;
        _dispatcher = Application.Current.Dispatcher;
        _socket = new UdpClient { EnableBroadcast = true };
        _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _socket.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
        _ = ListenAsync(_stop.Token);
        _ = AnnounceAsync(_stop.Token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _socket.Dispose();
    }

    private async Task AnnounceAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                SendBeacon(IPAddress.Broadcast);
                foreach (var broadcast in DirectedBroadcasts())
                {
                    SendBeacon(broadcast);
                }

                Expire();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ListenAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(token).ConfigureAwait(false);
                Handle(result.Buffer, result.RemoteEndPoint);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    private void Handle(byte[] buffer, IPEndPoint remote)
    {
        LanPacket? packet;
        try
        {
            packet = JsonSerializer.Deserialize<LanPacket>(buffer, Json);
        }
        catch (JsonException)
        {
            return;
        }

        if (packet is null || packet.V != 1 || packet.DeviceId == _deviceId)
        {
            return;
        }

        if (packet.Kind == "probe")
        {
            SendBeacon(remote.Address);
            return;
        }

        if (packet.Kind != "hub" || packet.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(packet.DeviceId))
        {
            return;
        }

        var ip = remote.Address.ToString();
        _dispatcher.BeginInvoke(() =>
        {
            var existing = Hubs.FirstOrDefault(hub => hub.DeviceId == packet.DeviceId);
            if (existing is null)
            {
                Hubs.Add(new DiscoveredHub
                {
                    DeviceId = packet.DeviceId,
                    Name = packet.Name,
                    Ip = ip,
                    Port = packet.Port,
                    LastSeen = DateTimeOffset.UtcNow,
                });
            }
            else
            {
                existing.Name = packet.Name;
                existing.Ip = ip;
                existing.Port = packet.Port;
                existing.LastSeen = DateTimeOffset.UtcNow;
            }
        });
    }

    private void Expire()
    {
        _dispatcher.BeginInvoke(() =>
        {
            var cutoff = DateTimeOffset.UtcNow.AddSeconds(-7);
            for (var i = Hubs.Count - 1; i >= 0; i--)
            {
                if (Hubs[i].LastSeen < cutoff)
                {
                    Hubs.RemoveAt(i);
                }
            }
        });
    }

    private void SendBeacon(IPAddress address)
    {
        var packet = new LanPacket
        {
            V = 1,
            Kind = "hub",
            Name = _name(),
            DeviceId = _deviceId,
            Port = PairingService.Port,
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(packet, Json);
        try
        {
            lock (_gate)
            {
                _socket.Send(bytes, bytes.Length, new IPEndPoint(address, Port));
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
        }
    }

    public void Probe()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new LanPacket { V = 1, Kind = "probe", DeviceId = _deviceId }, Json);
        try
        {
            lock (_gate)
            {
                _socket.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, Port));
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
        }
    }

    private static IEnumerable<IPAddress> DirectedBroadcasts()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.IPv4Mask is null)
                {
                    continue;
                }

                var ip = unicast.Address.GetAddressBytes();
                var mask = unicast.IPv4Mask.GetAddressBytes();
                if (ip.Length != 4 || mask.Length != 4)
                {
                    continue;
                }

                var broadcast = new byte[4];
                for (var i = 0; i < 4; i++)
                {
                    broadcast[i] = (byte)(ip[i] | ~mask[i]);
                }

                yield return new IPAddress(broadcast);
            }
        }
    }

    private sealed class LanPacket
    {
        public int V { get; set; }
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public int Port { get; set; }
    }
}
