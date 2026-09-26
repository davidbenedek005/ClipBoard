using System.Security.Cryptography;

namespace ClipboardSync.App.Services;

/// <summary>
/// Stops the copy loop: local copy is sent, the peer writes its clipboard, that
/// write is observed, and would otherwise be sent back forever.
/// The last value written from the network is remembered and then cleared when
/// the clipboard listener sees it, so that one write is not broadcast again.
/// </summary>
public sealed class EchoGuard
{
    private readonly Queue<string> _sent = new();
    private readonly Queue<string> _applied = new();
    private readonly object _gate = new();
    private string? _lastText;
    private string? _lastImageHash;
    private DateTimeOffset _ignoreImagesUntil;

    public void NoteSent(string contentHash) => Remember(_sent, contentHash);

    public void NoteApplied(string contentHash) => Remember(_applied, contentHash);

    public bool IsEchoOfLocalSend(string contentHash) => Contains(_sent, contentHash);

    public bool WasAppliedFromPeer(string contentHash) => Contains(_applied, contentHash);

    /// <summary>Call immediately before <c>Clipboard.SetText</c> for a network payload.</summary>
    public void ExpectIncomingText(string text)
    {
        lock (_gate)
        {
            _lastText = Normalize(text);
        }
    }

    public void ClearIncomingText()
    {
        lock (_gate)
        {
            _lastText = null;
        }
    }

    /// <summary>Call immediately before <c>Clipboard.SetImage</c>. Windows re-encodes the bitmap, so the next image events for two seconds are the echo.</summary>
    public void ExpectIncomingImage(byte[] jpeg)
    {
        lock (_gate)
        {
            _lastImageHash = Convert.ToHexString(SHA256.HashData(jpeg));
            _ignoreImagesUntil = DateTimeOffset.UtcNow.AddSeconds(2);
        }
    }

    public bool ConsumeIncomingText(string text)
    {
        lock (_gate)
        {
            if (_lastText is null || !string.Equals(Normalize(text), _lastText, StringComparison.Ordinal))
            {
                return false;
            }

            _lastText = null;
            return true;
        }
    }

    public void ClearIncomingImage()
    {
        lock (_gate)
        {
            _lastImageHash = null;
            _ignoreImagesUntil = DateTimeOffset.MinValue;
        }
    }

    public bool ConsumeIncomingImage(byte[] jpeg)
    {
        lock (_gate)
        {
            var hash = Convert.ToHexString(SHA256.HashData(jpeg));
            if (_lastImageHash is not null && string.Equals(hash, _lastImageHash, StringComparison.OrdinalIgnoreCase))
            {
                _lastImageHash = null;
                return true;
            }

            return DateTimeOffset.UtcNow < _ignoreImagesUntil;
        }
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\0');

    private void Remember(Queue<string> queue, string hash)
    {
        lock (_gate)
        {
            queue.Enqueue(hash);
            while (queue.Count > 8)
            {
                queue.Dequeue();
            }
        }
    }

    private bool Contains(Queue<string> queue, string hash)
    {
        lock (_gate)
        {
            return queue.Contains(hash);
        }
    }
}
