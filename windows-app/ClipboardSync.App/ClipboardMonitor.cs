using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipboardSync.App.Models;
using ClipboardSync.App.Services;

namespace ClipboardSync.App;

/// <summary>
/// Event-driven clipboard watcher. There is no timer and no poll loop.
///
/// Win32 contract:
/// <list type="bullet">
/// <item><c>AddClipboardFormatListener</c> (Vista+) registers an HWND. Windows sends
/// <c>WM_CLIPBOARDUPDATE</c> (0x031D) to that window on every clipboard change,
/// including changes made by other processes. WPF has no managed event for this.</item>
/// <item>The HWND is a message-only window (<c>HWND_MESSAGE</c>, (HWND)-3). It never
/// appears on the taskbar, in Alt-Tab, or as a visible popup. <c>SendMessage</c> still
/// delivers <c>WM_CLIPBOARDUPDATE</c> to it. The <see cref="HwndSource"/> is stored on
/// this object for the process lifetime; if it were collected, the HWND would die and
/// listening would stop with no exception.</item>
/// <item><c>System.Windows.Clipboard</c> is STA-only. The hook runs on the WPF UI
/// thread, so reads stay on that thread. Moving the read to the thread pool throws
/// or returns empty data.</item>
/// <item>Another app often still owns the clipboard when our message arrives
/// (<c>CLIPBRD_E_CANT_OPEN</c>, 0x800401D0), and Office/browsers use delayed rendering
/// so the first read can fail. We retry briefly on this thread, then once more on a
/// dispatcher timer. Sleeping here is deliberate and short: the data is usually
/// released within a few milliseconds, and a timer-only retry races the owner.</item>
/// <item><c>GetClipboardSequenceNumber</c> dedupes the extra <c>WM_CLIPBOARDUPDATE</c>
/// Windows sometimes delivers for a single update. A return of 0 means "unknown"
/// (remote-desktop edge cases); we then accept the event.</item>
/// <item>Prefer <c>CF_UNICODETEXT</c>. The ANSI text format truncates non-ASCII
/// (accents, CJK) before we ever see it.</item>
/// <item>Do not write the clipboard from inside this handler. A write generates
/// another <c>WM_CLIPBOARDUPDATE</c>. Phase 3's echo guard must ignore the sequence
/// number of our own write, on a later dispatcher turn.</item>
/// <item><c>RemoveClipboardFormatListener</c> on dispose. Process exit also drops the
/// registration, but exiting via the tray should not leave a dead listener until then.</item>
/// </list>
/// </summary>
public sealed class ClipboardMonitor : IDisposable
{
    private HwndSource? _source;
    private uint _lastSequence;
    private int _deferredRetryVersion;
    private bool _disposed;
    private string? _lastSummary;
    private DateTimeOffset _lastSummaryAt;

    public event EventHandler<ClipboardChange>? ClipboardChanged;

    public event EventHandler<string>? ListenerFailed;

    public bool Start(out string? error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var parameters = new HwndSourceParameters("ClipBoard.ClipboardListener")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
            ParentWindow = Native.HwndMessage,
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        if (!Native.AddClipboardFormatListener(_source.Handle))
        {
            var win32 = Marshal.GetLastWin32Error();
            error = $"AddClipboardFormatListener failed (Win32 error {win32}). Clipboard changes will not be detected.";
            ListenerFailed?.Invoke(this, error);
            return false;
        }

        _lastSequence = Native.GetClipboardSequenceNumber();
        error = null;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_source is null)
        {
            return;
        }

        Native.RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WmClipboardUpdate)
        {
            handled = true;
            OnClipboardUpdate();
        }

        return IntPtr.Zero;
    }

    private void OnClipboardUpdate()
    {
        var sequence = Native.GetClipboardSequenceNumber();
        if (sequence != 0 && sequence == _lastSequence)
        {
            return;
        }

        if (TryCapture(out var change))
        {
            if (sequence != 0)
            {
                _lastSequence = sequence;
            }

            Publish(change);
            return;
        }

        // Delayed rendering: the owner advertised the change before the bytes exist.
        var token = ++_deferredRetryVersion;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (token != _deferredRetryVersion || _disposed)
            {
                return;
            }

            var laterSequence = Native.GetClipboardSequenceNumber();
            if (laterSequence != 0 && laterSequence == _lastSequence)
            {
                return;
            }

            if (!TryCapture(out var delayed))
            {
                ListenerFailed?.Invoke(
                    this,
                    "Clipboard changed, but it stayed locked (CLIPBRD_E_CANT_OPEN) after retries.");
                return;
            }

            if (laterSequence != 0)
            {
                _lastSequence = laterSequence;
            }

            Publish(delayed);
        };
        timer.Start();
    }

    /// <summary>
    /// A single image copy often produces two <c>WM_CLIPBOARDUPDATE</c> messages with
    /// two different sequence numbers (DIB, then a synthesized bitmap). Same summary
    /// inside a third of a second is one user action.
    /// </summary>
    private void Publish(ClipboardChange change)
    {
        var now = DateTimeOffset.UtcNow;
        if (change.Summary == _lastSummary && (now - _lastSummaryAt).TotalMilliseconds < 300)
        {
            return;
        }

        _lastSummary = change.Summary;
        _lastSummaryAt = now;
        ClipboardChanged?.Invoke(this, change);
    }

    private static bool TryCapture(out ClipboardChange change)
    {
        change = null!;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                change = ReadClipboard();
                return true;
            }
            catch (Exception ex) when (ex is COMException or Win32Exception or ExternalException)
            {
                Thread.Sleep(20);
            }
        }

        return false;
    }

    private static ClipboardChange ReadClipboard()
    {
        var data = Clipboard.GetDataObject();
        if (data is null)
        {
            return ClipboardChange.Empty();
        }

        string? text = null;
        if (data.GetDataPresent(DataFormats.UnicodeText))
        {
            text = data.GetData(DataFormats.UnicodeText) as string;
        }
        else if (data.GetDataPresent(DataFormats.Text))
        {
            text = data.GetData(DataFormats.Text) as string;
        }

        int? imageWidth = null;
        int? imageHeight = null;
        byte[]? jpeg = null;
        // ContainsImage agrees with what Phase 4 will send. Some owners expose only CF_DIB;
        // WPF's ContainsImage already treats a convertible DIB as an image. A format list
        // with no text and no image is logged as Other so a file-copy still proves the hook.
        if (Clipboard.ContainsImage())
        {
            BitmapSource? image = Clipboard.GetImage();
            if (image is not null)
            {
                imageWidth = image.PixelWidth;
                imageHeight = image.PixelHeight;
                jpeg = ImageCodec.ToJpeg(image);
            }
        }

        var formats = data.GetFormats();
        return ClipboardChange.From(text, imageWidth, imageHeight, formats, jpeg);
    }

    private static class Native
    {
        internal const int WmClipboardUpdate = 0x031D;
        internal static readonly IntPtr HwndMessage = new(-3);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        internal static extern uint GetClipboardSequenceNumber();
    }
}
