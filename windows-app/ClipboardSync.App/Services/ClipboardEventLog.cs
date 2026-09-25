using System.IO;

namespace ClipboardSync.App.Services;

/// <summary>
/// In-memory ring plus a local log file. The Status window and the Phase 1
/// checkpoint both read this. Previews can contain secrets; the file stays
/// under %LOCALAPPDATA%\ClipBoard unless CLIPBOARD_SYNC_LOG overrides it.
/// </summary>
public sealed class ClipboardEventLog
{
    private const int MaxLines = 400;
    private readonly Lock _gate = new();
    private readonly List<string> _lines = [];

    public ClipboardEventLog()
    {
        var overridePath = Environment.GetEnvironmentVariable("CLIPBOARD_SYNC_LOG");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            FilePath = overridePath;
            var directory = Path.GetDirectoryName(overridePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        else
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClipBoard");
            Directory.CreateDirectory(directory);
            FilePath = Path.Combine(directory, "listener.log");
        }
    }

    public string FilePath { get; }

    public event Action<string>? LineAdded;

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_gate)
            {
                return _lines.ToArray();
            }
        }
    }

    public void Write(string message)
    {
        lock (_gate)
        {
            _lines.Add(message);
            if (_lines.Count > MaxLines)
            {
                _lines.RemoveRange(0, _lines.Count - MaxLines);
            }

            try
            {
                RotateIfHuge();
                File.AppendAllText(FilePath, message + Environment.NewLine);
            }
            catch (IOException)
            {
                // The Status window still has the line. A locked log file must not kill the listener.
            }
        }

        LineAdded?.Invoke(message);
    }

    private void RotateIfHuge()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length < 1_000_000)
        {
            return;
        }

        var backup = FilePath + ".1";
        File.Delete(backup);
        File.Move(FilePath, backup);
    }
}
