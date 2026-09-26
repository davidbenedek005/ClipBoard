using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ClipboardSync.App.Models;
using ClipboardSync.App.Services;
using ClipboardSync.App.Views;

namespace ClipboardSync.App;

/// <summary>
/// Tray process. <see cref="ShutdownMode.OnExplicitShutdown"/> is set in
/// App.xaml so closing the main window hides it instead of exiting.
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
    private LanDiscovery? _lan;
    private UpstreamClient? _upstream;
    private MainWindow? _main;
    private readonly FileTransferTracker _transfers = new();
    private string _lastStatus = "Waiting for a device";
    private string _hubStatus = "Not joined to another PC.";

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
        _server = new ClipboardWebSocketServer(EventLog, _pairing, _echo, _history, _settings, _transfers);
        _server.PinRequested += challenge => Dispatcher.BeginInvoke(() =>
        {
            ShowMain();
            PinPrompt.Show(challenge);
        });
        _server.PinCleared += id => Dispatcher.BeginInvoke(() => PinPrompt.Close(id));
        _server.TextFromClient += text => _upstream?.ForwardText(text);
        _server.ImageFromClient += jpeg => _upstream?.ForwardImage(jpeg);
        _upstream = new UpstreamClient(_pairing, _settings, _echo, _history, _transfers, EventLog);
        _upstream.PinNeeded += () => Dispatcher.BeginInvoke(() =>
        {
            ShowMain();
            _main?.PromptForPin();
        });
        _upstream.StatusChanged += status => Dispatcher.BeginInvoke(() =>
        {
            _hubStatus = status;
            _main?.SetHubStatus(status);
        });
        _upstream.TextReceived += (name, text) => Dispatcher.Invoke(() => ApplyUpstreamText(name, text));
        _upstream.ImageReceived += (name, jpeg) => Dispatcher.Invoke(() => ApplyUpstreamImage(name, jpeg));
        try
        {
            _lan = new LanDiscovery(_pairing.DeviceId, () => _settings.DisplayName);
        }
        catch (Exception ex)
        {
            EventLog.Write("LAN discovery did not start: " + ex.Message);
        }

        _upstream.ConnectSaved();
        _server.FileReceived += name => Dispatcher.BeginInvoke(() =>
        {
            if (_main?.IsVisible != true)
            {
                _tray?.Notify("File received", $"{name} was saved to Downloads.");
            }
        });
        _server.StatusChanged += status => Dispatcher.Invoke(() =>
        {
            _lastStatus = status;
            _tray?.SetStatusText(status);
            _main?.SetStatus(status);
        });
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

        _tray = new TrayIconManager(onOpen: ShowMain, onExit: Shutdown);
        _tray.SetStatusText(_lastStatus);

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
            _upstream?.SendText(change.Text);
        }

        if (change.ImageJpeg is not null)
        {
            _server?.PublishImage(change.ImageJpeg);
            _upstream?.SendImage(change.ImageJpeg);
        }

        _tray?.SetStatusText(change.Summary);
    }

    private void ShowMain()
    {
        if (_pairing is null || _settings is null || _history is null)
        {
            return;
        }

        if (_main is null)
        {
            _main = new MainWindow(
                _pairing,
                _settings,
                _history,
                _transfers,
                phoneConnected: () => _server?.HasDevice == true || _upstream?.IsReady == true,
                sendFile: path =>
                {
                    if (_server?.HasDevice == true)
                    {
                        _ = _server.SendFileAsync(path);
                    }

                    if (_upstream?.IsReady == true)
                    {
                        _ = _upstream.SendFileAsync(path);
                    }
                },
                disconnect: () => _server?.DisconnectAll(),
                tokenRegenerated: () => _server?.DisconnectAll(),
                nearby: _lan?.Hubs ?? [],
                joinHub: hub => _upstream?.Join(hub),
                submitPin: pin => _upstream?.SubmitPin(pin),
                leaveHub: () => _upstream?.Leave(),
                probeHubs: () => _lan?.Probe());
            _main.SetStatus(_lastStatus);
            _main.SetHubStatus(_hubStatus);
        }

        _main.BringToFront();
    }

    private void ApplyUpstreamText(string name, string text)
    {
        try
        {
            Clipboard.SetText(text);
            _history?.AddText("From " + name, text);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            EventLog.Write("Could not write the Windows clipboard: " + ex.Message);
        }

        _server?.RelayText(text, name);
    }

    private void ApplyUpstreamImage(string name, byte[] jpeg)
    {
        try
        {
            using var stream = new MemoryStream(jpeg);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            Clipboard.SetImage(image);
            _history?.AddImage("From " + name, jpeg);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or IOException or NotSupportedException)
        {
            EventLog.Write("Could not write the Windows clipboard: " + ex.Message);
        }

        _server?.RelayImage(jpeg, name);
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
        if (_main is not null)
        {
            _main.AllowClose = true;
            _main.Close();
            _main = null;
        }

        if (_monitor is not null)
        {
            _monitor.ClipboardChanged -= OnClipboardChanged;
            _monitor.Dispose();
            _monitor = null;
        }

        _server?.Dispose();
        _server = null;
        _upstream?.Dispose();
        _upstream = null;
        _lan?.Dispose();
        _lan = null;
        _discovery?.Dispose();
        _discovery = null;

        _tray?.Dispose();
        _tray = null;

        _instanceMutex?.Dispose();
        _instanceMutex = null;
    }
}
