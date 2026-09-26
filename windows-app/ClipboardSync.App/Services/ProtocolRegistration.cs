using Microsoft.Win32;

namespace ClipboardSync.App.Services;

/// <summary>
/// Registers <c>clipboardsync://</c> for this user only. HKCU does not need
/// administrator rights. Windows then starts this executable with the URI as
/// the first argument.
/// </summary>
public static class ProtocolRegistration
{
    public const string Scheme = "clipboardsync";

    public static void RegisterCurrentUser()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        using var protocol = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Scheme);
        protocol.SetValue("", "URL:ClipBoard");
        protocol.SetValue("URL Protocol", "");

        using var command = protocol.CreateSubKey(@"shell\open\command");
        command.SetValue("", $"\"{exe}\" \"%1\"");
    }
}

/// <summary>clipboardsync://connect?ip=192.168.1.50&amp;pin=123456</summary>
public sealed record InviteLink(string Ip, int Port, string Pin)
{
    public static InviteLink? FromArgs(string[] args)
    {
        foreach (var arg in args)
        {
            var invite = Parse(arg);
            if (invite is not null)
            {
                return invite;
            }
        }

        return null;
    }

    public static InviteLink? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim().Trim('"');
        if (!raw.StartsWith(ProtocolRegistration.Scheme + ":", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!uri.Scheme.Equals(ProtocolRegistration.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!uri.Host.Equals("connect", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("ip", out var ip) || string.IsNullOrWhiteSpace(ip))
        {
            return null;
        }

        if (!query.TryGetValue("pin", out var pin))
        {
            return null;
        }

        pin = pin.Trim();
        if (pin.Length != 6 || !pin.All(char.IsDigit))
        {
            return null;
        }

        var port = PairingService.Port;
        if (query.TryGetValue("port", out var portText) &&
            int.TryParse(portText, out var parsed) &&
            parsed is > 0 and <= 65535)
        {
            port = parsed;
        }

        return new InviteLink(ip.Trim(), port, pin);
    }

    public override string ToString() =>
        $"{ProtocolRegistration.Scheme}://connect?ip={Uri.EscapeDataString(Ip)}&port={Port}&pin={Pin}";

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var body = query.TrimStart('?');
        if (body.Length == 0)
        {
            return values;
        }

        foreach (var part in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.Split('=', 2);
            var key = Uri.UnescapeDataString(split[0]);
            var value = split.Length == 2 ? Uri.UnescapeDataString(split[1]) : "";
            values[key] = value;
        }

        return values;
    }
}
