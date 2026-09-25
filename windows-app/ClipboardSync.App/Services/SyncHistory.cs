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

public sealed class SyncHistory
{
    public ObservableCollection<HistoryEntry> Entries { get; } = [];

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

    private void Add(HistoryEntry entry)
    {
        void Insert()
        {
            Entries.Insert(0, entry);
            while (Entries.Count > 20)
            {
                Entries.RemoveAt(Entries.Count - 1);
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Insert();
        }
        else
        {
            dispatcher.Invoke(Insert);
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
