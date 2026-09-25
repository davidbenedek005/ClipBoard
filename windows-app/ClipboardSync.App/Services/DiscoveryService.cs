using Makaretu.Dns;

namespace ClipboardSync.App.Services;

/// <summary>
/// Advertises <c>_clipboardsync._tcp</c> so the phone can replace a stale DHCP
/// address without scanning the QR again. The TXT record carries the device id,
/// never the pairing token.
/// </summary>
public sealed class DiscoveryService : IDisposable
{
    private readonly MulticastService _mdns = new();
    private readonly ServiceDiscovery _discovery;
    private readonly ServiceProfile _profile;

    public DiscoveryService(string deviceId)
    {
        _discovery = new ServiceDiscovery(_mdns);
        _profile = new ServiceProfile("ClipBoard", "_clipboardsync._tcp", (ushort)PairingService.Port);
        _profile.AddProperty("id", deviceId);
        _discovery.Advertise(_profile);
        _mdns.Start();
    }

    public void Dispose()
    {
        try
        {
            _discovery.Unadvertise(_profile);
        }
        catch (Exception)
        {
            // The multicast socket may already be closed.
        }

        _mdns.Stop();
        _mdns.Dispose();
    }
}
