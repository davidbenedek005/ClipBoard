using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ClipboardSync.App.Services;

public abstract class HistoryEntry
{
    public required DateTimeOffset At { get; init; }
    public required string Direction { get; init; }
    public string TimeLabel => At.ToLocalTime().ToString("HH:mm");
}

public sealed class TextHistoryEntry : HistoryEntry
{
    public required string Text { get; init; }
}

public sealed class ImageHistoryEntry : HistoryEntry
{
    public required byte[] Jpeg { get; init; }
    public BitmapSource? Thumbnail { get; init; }
}

public sealed class FileHistoryEntry : HistoryEntry
{
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public required long Size { get; init; }
    public string SizeLabel => ByteSize.Format(Size);

    public string ExtensionLabel
    {
        get
        {
            var extension = Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();
            return extension.Length is > 0 and <= 4 ? extension : "FILE";
        }
    }
}

public sealed class SyncHistory
{
    public ObservableCollection<HistoryEntry> Entries { get; } = [];

    public void AddFile(string direction, string path, long size)
    {
        Add(new FileHistoryEntry
        {
            At = DateTimeOffset.Now,
            Direction = direction,
            FileName = Path.GetFileName(path),
            FilePath = path,
            Size = size,
        });
    }

    public void AddText(string direction, string text)
    {
        Add(new TextHistoryEntry
        {
            At = DateTimeOffset.Now,
            Direction = direction,
            Text = text,
        });
    }

    public void AddImage(string direction, byte[] jpeg)
    {
        BitmapSource? thumbnail = null;
        Application.Current?.Dispatcher.Invoke(() =>
        {
            thumbnail = Decode(jpeg);
        });
        Add(new ImageHistoryEntry
        {
            At = DateTimeOffset.Now,
            Direction = direction,
            Jpeg = jpeg,
            Thumbnail = thumbnail,
        });
    }

    public void Remove(HistoryEntry entry)
    {
        OnUiThread(() => Entries.Remove(entry));
    }

    public void Clear()
    {
        OnUiThread(Entries.Clear);
    }

    private void Add(HistoryEntry entry)
    {
        OnUiThread(() =>
        {
            Entries.Insert(0, entry);
            while (Entries.Count > 20)
            {
                Entries.RemoveAt(Entries.Count - 1);
            }
        });
    }

    /// <summary>The server thread adds entries, so every change is marshalled to the collection's dispatcher.</summary>
    private static void OnUiThread(Action change)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            change();
        }
        else
        {
            dispatcher.Invoke(change);
        }
    }

    private static BitmapSource? Decode(byte[] jpeg)
    {
        try
        {
            using var stream = new MemoryStream(jpeg);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.DecodePixelWidth = 360;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
