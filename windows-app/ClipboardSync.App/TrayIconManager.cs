using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace ClipboardSync.App;

/// <summary>
/// System-tray shell. The WPF main window is never shown; this icon is the UI.
/// Right-click opens the menu. Left-click opens the status log.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly TaskbarIcon _icon;
    private bool _disposed;

    public TrayIconManager(Action onStatus, Action onPairing, Action onSettings, Action onExit)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item("Status", onStatus));
        menu.Items.Add(Item("Show Pairing QR", onPairing));
        menu.Items.Add(Item("Settings", onSettings));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Exit", onExit));

        _icon = new TaskbarIcon
        {
            IconSource = TrayIconArtwork.Create(),
            ToolTipText = "ClipBoard — listening for clipboard changes",
            ContextMenu = menu,
            MenuActivation = PopupActivationMode.RightClick,
        };

        _icon.TrayLeftMouseUp += (_, _) => onStatus();
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _icon.Dispose();
    }

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
