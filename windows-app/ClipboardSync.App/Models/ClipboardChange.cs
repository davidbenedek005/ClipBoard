using System.Text;

namespace ClipboardSync.App.Models;

public enum ClipboardContentKind
{
    Empty,
    Text,
    Image,
    TextAndImage,
    Other,
}

/// <summary>
/// One observed clipboard update. Phase 1 logs this. Phase 3 turns it into the
/// encrypted payload in <c>shared/protocol-schema.json</c>.
/// </summary>
public sealed class ClipboardChange
{
    public required DateTimeOffset Timestamp { get; init; }
    public required ClipboardContentKind Kind { get; init; }
    public string? Text { get; init; }
    public string? TextPreview { get; init; }
    public int? TextLength { get; init; }
    public int? ImageWidth { get; init; }
    public int? ImageHeight { get; init; }
    public byte[]? ImageJpeg { get; init; }
    public string Summary { get; init; } = "";

    public static ClipboardChange Empty() =>
        Build(null, null, null, []);

    public static ClipboardChange From(string? text, int? imageWidth, int? imageHeight, string[] formats, byte[]? jpeg = null)
    {
        var hasText = !string.IsNullOrEmpty(text);
        var hasImage = imageWidth is > 0 && imageHeight is > 0;
        if (!hasText && !hasImage && formats.Length == 0)
        {
            return Empty();
        }

        return Build(text, imageWidth, imageHeight, formats, jpeg);
    }

    public override string ToString()
    {
        var stamp = Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
        return $"{stamp}  {Summary}";
    }

    private static ClipboardChange Build(string? text, int? imageWidth, int? imageHeight, string[] formats, byte[]? jpeg = null)
    {
        var hasText = !string.IsNullOrEmpty(text);
        var hasImage = imageWidth is > 0 && imageHeight is > 0;
        var kind = (hasText, hasImage) switch
        {
            (true, true) => ClipboardContentKind.TextAndImage,
            (true, false) => ClipboardContentKind.Text,
            (false, true) => ClipboardContentKind.Image,
            _ when formats.Length > 0 => ClipboardContentKind.Other,
            _ => ClipboardContentKind.Empty,
        };

        string? preview = null;
        int? length = null;
        if (hasText && text is not null)
        {
            length = text.Length;
            preview = OneLine(text, 80);
        }

        var summary = kind switch
        {
            ClipboardContentKind.Text => $"text  len={length}  \"{preview}\"",
            ClipboardContentKind.Image => $"image  {imageWidth}x{imageHeight}",
            ClipboardContentKind.TextAndImage => $"text+image  len={length}  \"{preview}\"  {imageWidth}x{imageHeight}",
            ClipboardContentKind.Other => "other  formats=" + string.Join(",", formats.Take(8)),
            _ => "empty",
        };

        return new ClipboardChange
        {
            Timestamp = DateTimeOffset.UtcNow,
            Kind = kind,
            Text = hasText ? text : null,
            TextPreview = preview,
            TextLength = length,
            ImageWidth = hasImage ? imageWidth : null,
            ImageHeight = hasImage ? imageHeight : null,
            ImageJpeg = jpeg,
            Summary = summary,
        };
    }

    private static string OneLine(string text, int max)
    {
        var builder = new StringBuilder(Math.Min(text.Length, max));
        foreach (var ch in text)
        {
            if (builder.Length >= max)
            {
                break;
            }

            builder.Append(char.IsControl(ch) ? ' ' : ch);
        }

        if (text.Length > max)
        {
            builder.Append('…');
        }

        return builder.ToString();
    }
}
