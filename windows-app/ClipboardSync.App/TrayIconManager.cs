using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace ClipboardSync.App;

/// <summary>
/// System-tray shell. Left-click opens the main window. The right-click menu
/// has only Exit.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly TaskbarIcon _icon;
    private bool _disposed;

    public TrayIconManager(Action onOpen, Action onExit)
    {
        var menu = new ContextMenu();
        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => onExit();
        menu.Items.Add(exit);

        _icon = new TaskbarIcon
        {
            IconSource = TrayIconArtwork.Create(),
            ToolTipText = "ClipBoard",
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.RightClick,
        };

        _icon.TrayLeftMouseUp += (_, _) => onOpen();
    }

    public void SetStatusText(string summary)
    {
        if (_disposed)
        {
            return;
        }

        var text = summary.Length <= 120 ? summary : summary[..120] + "…";
        _icon.ToolTipText = "ClipBoard — " + text;
    }

    public void Notify(string title, string text)
    {
        if (!_disposed)
        {
            _icon.ShowBalloonTip(title, text, BalloonIcon.Info);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Dispose();
    }
}
