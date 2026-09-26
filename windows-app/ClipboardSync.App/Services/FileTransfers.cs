using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace ClipboardSync.App.Services;

/// <summary>One in-flight file, bound by the progress cards in the History page.</summary>
public sealed class FileTransfer : INotifyPropertyChanged
{
    private long _bytesDone;
    internal long LastReportTicks;

    public required string FileName { get; init; }
    public required bool Sending { get; init; }
    public required long TotalBytes { get; init; }
    public CancellationTokenSource? Cancellation { get; init; }

    public long BytesDone => _bytesDone;
    public double Percent => TotalBytes <= 0 ? 100 : _bytesDone * 100.0 / TotalBytes;
    public string Detail => $"{(Sending ? "Sending" : "Receiving")} · {ByteSize.Format(_bytesDone)} of {ByteSize.Format(TotalBytes)}";
    public Visibility CancelVisibility => Cancellation is null ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Cancel()
    {
        try
        {
            Cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The transfer finished between the click and this call.
        }
    }

    internal void SetBytesDone(long value)
    {
        _bytesDone = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BytesDone)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Percent)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
    }
}

/// <summary>
/// Transfers run on background threads; every collection or property change is
/// posted to the UI dispatcher, and progress is throttled to about ten updates a second.
/// </summary>
public sealed class FileTransferTracker
{
    private static readonly long ReportInterval = TimeSpan.FromMilliseconds(100).Ticks;

    public ObservableCollection<FileTransfer> Active { get; } = [];

    public FileTransfer Begin(string fileName, bool sending, long totalBytes, CancellationTokenSource? cancellation = null)
    {
        var transfer = new FileTransfer
        {
            FileName = fileName,
            Sending = sending,
            TotalBytes = totalBytes,
            Cancellation = cancellation,
        };
        Post(() => Active.Add(transfer));
        return transfer;
    }

    public void Report(FileTransfer transfer, long bytesDone)
    {
        var now = DateTime.UtcNow.Ticks;
        if (bytesDone < transfer.TotalBytes && now - transfer.LastReportTicks < ReportInterval)
        {
            return;
        }

        transfer.LastReportTicks = now;
        Post(() => transfer.SetBytesDone(bytesDone));
    }

    public void End(FileTransfer transfer)
    {
        Post(() => Active.Remove(transfer));
    }

    private static void Post(Action change)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            change();
        }
        else
        {
            dispatcher.BeginInvoke(change);
        }
    }
}

public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {Units[unit]}";
    }
}

public static class DownloadFolder
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>The user's real Downloads folder, which can be moved away from %USERPROFILE%.</summary>
    public static string Path
    {
        get
        {
            try
            {
                if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var path) == 0 && !string.IsNullOrEmpty(path))
                {
                    return path;
                }
            }
            catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
            {
            }

            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    /// <summary>The name comes from the phone, so strip any path and characters Windows rejects.</summary>
    public static string SafeFileName(string raw)
    {
        var name = raw.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        foreach (var invalid in System.IO.Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        name = name.Trim().TrimEnd('.', ' ');
        if (name.Length == 0)
        {
            name = "file";
        }

        if (ReservedNames.Contains(System.IO.Path.GetFileNameWithoutExtension(name)))
        {
            name = "_" + name;
        }

        if (name.Length > 180)
        {
            var extension = System.IO.Path.GetExtension(name);
            extension = extension.Length > 16 ? "" : extension;
            name = name[..(180 - extension.Length)] + extension;
        }

        return name;
    }

    public static string UniquePath(string folder, string fileName)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(fileName);
        var extension = System.IO.Path.GetExtension(fileName);
        var candidate = System.IO.Path.Combine(folder, fileName);
        for (var n = 1; File.Exists(candidate); n++)
        {
            candidate = System.IO.Path.Combine(folder, $"{stem} ({n}){extension}");
        }

        return candidate;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint flags,
        IntPtr token,
        out string path);
}
