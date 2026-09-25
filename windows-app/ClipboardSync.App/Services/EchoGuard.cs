namespace ClipboardSync.App.Services;

/// <summary>
/// Stops the copy loop: local copy is sent, the peer writes its clipboard, that
/// write is observed, and would otherwise be sent back forever.
/// Incoming messages whose hash is one we just sent are dropped.
/// Local clipboard events whose hash is one we just applied from the peer are not sent.
/// </summary>
public sealed class EchoGuard
{
    private readonly Queue<string> _sent = new();
    private readonly Queue<string> _applied = new();
    private readonly object _gate = new();

    public void NoteSent(string contentHash) => Remember(_sent, contentHash);

    public void NoteApplied(string contentHash) => Remember(_applied, contentHash);

    public bool IsEchoOfLocalSend(string contentHash) => Contains(_sent, contentHash);

    public bool WasAppliedFromPeer(string contentHash) => Contains(_applied, contentHash);

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
