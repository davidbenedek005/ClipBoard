using System.Windows;
using ClipboardSync.App.Models;
using ClipboardSync.App.Services;
using ClipboardSync.App.Views;

namespace ClipboardSync.App;

/// <summary>
/// Tray-only process. <see cref="ShutdownMode.OnExplicitShutdown"/> is set in
/// App.xaml so closing Status, Pairing, or Settings does not exit.
/// </summary>
public partial class App : Application
{
    private const string MutexName = @"Local\ClipBoard.Sync.SingleInstance";

    private Mutex? _instanceMutex;
    private TrayIconManager? _tray;
    private ClipboardMonitor? _monitor;
    private ClipboardWebSocketServer? _server;
    private EchoGuard? _echo;
    private PairingService? _pairing;
    private AppSettings? _settings;
    private SyncHistory? _history;
    private DiscoveryService? _discovery;

    public ClipboardEventLog EventLog { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        if (!AcquireSingleInstance())
        {
            MessageBox.Show(
                "ClipBoard is already running. Look for the clipboard icon in the system tray.",
                "ClipBoard",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            EventLog.Write("UI exception: " + args.Exception);
            args.Handled = true;
        };

        var version = typeof(App).Assembly.GetName().Version;
        EventLog.Write($"ClipBoard {version} started (pid {Environment.ProcessId}). Log file: {EventLog.FilePath}");

        _echo = new EchoGuard();
        _pairing = new PairingService();
        _settings = AppSettings.Load();
        _history = new SyncHistory();
        _server = new ClipboardWebSocketServer(EventLog, _pairing, _echo, _history, _settings);
        _server.StatusChanged += status => Dispatcher.Invoke(() => _tray?.SetStatusText(status));
        try
        {
            _server.Start();
        }
        catch (Exception ex)
        {
            EventLog.Write($"WebSocket server did not start (is port {PairingService.Port} in use?): {ex.Message}");
        }

        try
        {
            _discovery = new DiscoveryService(_pairing.DeviceId);
            EventLog.Write("mDNS advertising _clipboardsync._tcp.");
        }
        catch (Exception ex)
        {
            EventLog.Write("mDNS did not start: " + ex.Message);
        }
        _monitor = new ClipboardMonitor();
        _monitor.ClipboardChanged += OnClipboardChanged;
        _monitor.ListenerFailed += (_, message) => EventLog.Write(message);

        if (!_monitor.Start(out var listenerError))
        {
            EventLog.Write(listenerError ?? "Clipboard listener failed to start.");
            MessageBox.Show(
                listenerError ?? "Clipboard listener failed to start.",
                "ClipBoard",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else
        {
            EventLog.Write("Clipboard listener attached (WM_CLIPBOARDUPDATE).");
        }

        _tray = new TrayIconManager(
            onStatus: ShowStatus,
            onPairing: ShowPairing,
            onSettings: ShowSettings,
            onExit: Shutdown);
        _tray.SetStatusText("Listening for clipboard changes");

        SessionEnding += (_, _) => DisposeBackground();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DisposeBackground();
        base.OnExit(e);
    }

    private void OnClipboardChanged(object? sender, ClipboardChange change)
    {
        EventLog.Write(change.ToString());
        if (change.Text is not null)
        {
            _server?.PublishText(change.Text);
        }

        if (change.ImageJpeg is not null)
        {
            _server?.PublishImage(change.ImageJpeg);
        }

        _tray?.SetStatusText(change.Summary);
    }

    private void ShowStatus()
    {
        foreach (Window window in Windows)
        {
            if (window is StatusWindow status)
            {
                status.Activate();
                return;
            }
        }

        new StatusWindow(_history ?? new SyncHistory()).Show();
    }

    private void ShowPairing()
    {
        foreach (Window window in Windows)
        {
            if (window is PairingWindow open)
            {
                open.Activate();
                return;
            }
        }

        if (_pairing is null)
        {
            return;
        }

        var pairing = new PairingWindow(_pairing)
        {
            TokenRegenerated = () => _server?.DisconnectAll(),
        };
        pairing.Show();
    }

    private void ShowSettings()
    {
        foreach (Window window in Windows)
        {
            if (window is SettingsWindow open)
            {
                open.Activate();
                return;
            }
        }

        if (_settings is null || _history is null)
        {
            return;
        }

        new SettingsWindow(_settings, _history, () => _server?.DisconnectAll()).Show();
    }

    private bool AcquireSingleInstance()
    {
        try
        {
            _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
            return createdNew;
        }
        catch (AbandonedMutexException)
        {
            // The previous process crashed while holding the mutex. We own it now.
            return true;
        }
    }

    private void DisposeBackground()
    {
        if (_monitor is not null)
        {
            _monitor.ClipboardChanged -= OnClipboardChanged;
            _monitor.Dispose();
            _monitor = null;
        }

        _server?.Dispose();
        _server = null;
        _discovery?.Dispose();
        _discovery = null;

        _tray?.Dispose();
        _tray = null;

        _instanceMutex?.Dispose();
        _instanceMutex = null;
    }
}
