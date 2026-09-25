using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using ClipboardSync.App.Models;

namespace ClipboardSync.App.Services;

/// <summary>
/// Pairing token and device id are created once and stored under LocalAppData
/// so a PC reboot does not force a new QR scan. Regenerating the token does.
/// The QR is built when the window opens so it picks up the current LAN address.
/// </summary>
public sealed class PairingService
{
    public const int Port = 53211;

    public PairingService()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (!File.Exists(StatePath))
        {
            var created = new StoredPairing(Guid.NewGuid().ToString("D"), NewToken());
            File.WriteAllText(StatePath, JsonSerializer.Serialize(created));
        }
    }

    public string DeviceId => Load().DeviceId;

    public string Token => Load().Token;

    public string? LanAddress => ChooseLanAddress();

    public PairingQr CreateQrPayload()
    {
        var ip = LanAddress ?? "0.0.0.0";
        return new PairingQr { V = 1, Ip = ip, Port = Port, Token = Token };
    }

    public string CreatePairingUri()
    {
        var payload = CreateQrPayload();
        return "clipboardsync://pair"
            + "?ip=" + Uri.EscapeDataString(payload.Ip)
            + "&port=" + payload.Port
            + "&token=" + Uri.EscapeDataString(payload.Token)
            + "&device=" + Uri.EscapeDataString(DeviceId);
    }

    public void RegenerateToken()
    {
        var current = Load();
        var next = current with { Token = NewToken() };
        File.WriteAllText(StatePath, JsonSerializer.Serialize(next));
    }

    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static StoredPairing Load()
    {
        var json = File.ReadAllText(StatePath);
        return JsonSerializer.Deserialize<StoredPairing>(json)
            ?? throw new InvalidOperationException("Pairing state is empty.");
    }

    /// <summary>
    /// Prefer a real Wi-Fi or Ethernet IPv4. Hyper-V, WSL, and Docker adapters
    /// often own a private address that the phone cannot route to.
    /// </summary>
    private static string? ChooseLanAddress()
    {
        var candidates = new List<(int Score, string Address)>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var virtualAdapter = IsVirtual(nic.Name) || IsVirtual(nic.Description);
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var address = unicast.Address;
                if (IPAddress.IsLoopback(address))
                {
                    continue;
                }

                var text = address.ToString();
                if (text.StartsWith("169.254.", StringComparison.Ordinal))
                {
                    continue;
                }

                var score = virtualAdapter ? 0 : 2;
                if (text.StartsWith("192.168.", StringComparison.Ordinal) ||
                    text.StartsWith("10.", StringComparison.Ordinal))
                {
                    score += 2;
                }

                candidates.Add((score, text));
            }
        }

        return candidates.OrderByDescending(c => c.Score).Select(c => c.Address).FirstOrDefault();
    }

    private static bool IsVirtual(string value)
    {
        var names = new[] { "vethernet", "hyper-v", "wsl", "docker", "virtual", "vbox", "bluetooth", "loopback" };
        return names.Any(name => value.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string DirectoryPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipBoard");

    private static string StatePath => Path.Combine(DirectoryPath, "pairing.json");

    private sealed record StoredPairing(string DeviceId, string Token);
}
