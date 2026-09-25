using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipboardSync.App;

/// <summary>
/// Draws the tray glyph in memory so the shell icon does not depend on a loose PNG.
/// The .ico next to this is only the executable's file icon.
/// </summary>
internal static class TrayIconArtwork
{
    public static ImageSource Create()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = Freeze(Color.FromRgb(0x25, 0x63, 0xEB));
            var paper = Freeze(Colors.White);
            var clip = Freeze(Color.FromRgb(0xDB, 0xEA, 0xFE));
            var ink = Freeze(Color.FromRgb(0x25, 0x63, 0xEB));

            dc.DrawRoundedRectangle(background, null, new Rect(1, 1, 30, 30), 7, 7);
            dc.DrawRoundedRectangle(paper, null, new Rect(8, 11, 16, 16), 2, 2);
            dc.DrawRoundedRectangle(clip, null, new Rect(12, 6, 8, 6), 1.5, 1.5);
            dc.DrawRectangle(ink, null, new Rect(11, 15, 10, 1.4));
            dc.DrawRectangle(ink, null, new Rect(11, 18.5, 10, 1.4));
            dc.DrawRectangle(ink, null, new Rect(11, 22, 7, 1.4));
        }

        // 32 DIP at 192 DPI => 64 physical pixels, so the tray stays sharp on a 200% laptop.
        var bitmap = new RenderTargetBitmap(64, 64, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
