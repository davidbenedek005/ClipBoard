using System.IO;
using System.IO.Pipes;
using System.Text;
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
    private const string InvitePipeName = "ClipBoard.Sync.Invite";

    private Mutex? _instanceMutex;
    private TrayIconManager? _tray;
    private ClipboardMonitor? _monitor;
    private ClipboardWebSocketServer? _server;
    private EchoGuard? _echo;
    private PairingService? _pairing;
    private AppSettings? _settings;
    private SyncHistory? _history;
    private DiscoveryService? _discovery;
    private UpstreamClient? _upstream;
    private MainWindow? _main;
    private readonly FileTransferTracker _transfers = new();
    private readonly CancellationTokenSource _pipeStop = new();
    private NamedPipeServerStream? _invitePipe;
    private InviteLink? _launchInvite;
    private readonly ConnectionStatus _link = new();
    private string _lastStatus = "Waiting for a device";
    private string _hubStatus = "Not joined to another PC.";

    public ClipboardEventLog EventLog { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        var invite = InviteLink.FromArgs(e.Args);
        if (!AcquireSingleInstance())
        {
            if (invite is not null && ForwardInvite(e.Args))
            {
                Shutdown();
                return;
            }

            MessageBox.Show(
                "ClipBoard is already running. Look for the clipboard icon in the system tray.",
                "ClipBoard",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        ProtocolRegistration.RegisterCurrentUser();
        _launchInvite = invite;
        StartInvitePipe();
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
        _server.InviteChanged += () => Dispatcher.BeginInvoke(() => _main?.RefreshInvite());
        _server.TextFromClient += text => _upstream?.ForwardText(text);
        _server.ImageFromClient += jpeg => _upstream?.ForwardImage(jpeg);
        _upstream = new UpstreamClient(_pairing, _settings, _echo, _history, _transfers, EventLog);
        _upstream.StatusChanged += status => Dispatcher.BeginInvoke(() =>
        {
            _hubStatus = status;
            _main?.SetHubStatus(status);
        });
        _upstream.ReadyChanged += joined => Dispatcher.BeginInvoke(() =>
        {
            _link.NoteHub(joined);
            _tray?.SetStatusText(_link.Text);
        });
        _upstream.TextReceived += (name, text) => Dispatcher.Invoke(() => ApplyUpstreamText(name, text));
        _upstream.ImageReceived += (name, jpeg) => Dispatcher.Invoke(() => ApplyUpstreamImage(name, jpeg));
        if (_launchInvite is null)
        {
            _upstream.ConnectSaved();
        }
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
            _link.NoteServer(status);
            _tray?.SetStatusText(_link.Text);
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

        _tray = new TrayIconManager(onOpen: () => ShowMain(), onExit: Shutdown);
        _tray.SetStatusText(_lastStatus);

        if (_launchInvite is not null)
        {
            ShowMain(_launchInvite);
        }

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
        var textEcho = change.Text is not null && _echo?.ConsumeIncomingText(change.Text) == true;
        var imageEcho = change.ImageJpeg is not null && _echo?.ConsumeIncomingImage(change.ImageJpeg) == true;
        if (!textEcho &&
            change.Text is not null &&
            !change.Text.TrimStart().StartsWith("clipboardsync://", StringComparison.OrdinalIgnoreCase))
        {
            _server?.PublishText(change.Text);
            _upstream?.SendText(change.Text);
        }

        if (!imageEcho && change.ImageJpeg is not null)
        {
            _server?.PublishImage(change.ImageJpeg);
            _upstream?.SendImage(change.ImageJpeg);
        }

        _tray?.SetStatusText(change.Summary);
    }

    private void ShowMain(InviteLink? invite = null)
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
                link: _link,
                currentInvite: () => _server?.CurrentInvite(),
                joinPc: (ip, port, pin) => _upstream?.Join(ip, port, pin),
                leaveHub: () => _upstream?.Leave(),
                autoJoin: invite);
            _main.SetHubStatus(_hubStatus);
        }
        else if (invite is not null)
        {
            _main.BeginJoin(invite);
        }

        _main.BringToFront();
    }

    private void StartInvitePipe()
    {
        _ = Task.Run(() =>
        {
            while (!_pipeStop.IsCancellationRequested)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new NamedPipeServerStream(
                        InvitePipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    _invitePipe = pipe;
                    pipe.WaitForConnection();
                    using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                    var line = reader.ReadLine();
                    var invite = InviteLink.Parse(line);
                    if (invite is not null)
                    {
                        Dispatcher.BeginInvoke(() => ShowMain(invite));
                    }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    if (_pipeStop.IsCancellationRequested)
                    {
                        return;
                    }
                }
                finally
                {
                    pipe?.Dispose();
                    if (ReferenceEquals(_invitePipe, pipe))
                    {
                        _invitePipe = null;
                    }
                }
            }
        });
    }

    private static bool ForwardInvite(string[] args)
    {
        var raw = args.FirstOrDefault(arg =>
            arg.Contains("clipboardsync:", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        try
        {
            using var pipe = new NamedPipeClientStream(".", InvitePipeName, PipeDirection.Out);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(raw.Trim().Trim('"'));
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void ApplyUpstreamText(string name, string text)
    {
        try
        {
            _echo?.ExpectIncomingText(text);
            Clipboard.SetText(text);
            _history?.AddText("From " + name, text);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            _echo?.ClearIncomingText();
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
            _echo?.ExpectIncomingImage(jpeg);
            Clipboard.SetImage(image);
            _history?.AddImage("From " + name, jpeg);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or IOException or NotSupportedException)
        {
            _echo?.ClearIncomingImage();
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

        _pipeStop.Cancel();
        try
        {
            _invitePipe?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }

        _server?.Dispose();
        _server = null;
        _upstream?.Dispose();
        _upstream = null;
        _discovery?.Dispose();
        _discovery = null;

        _tray?.Dispose();
        _tray = null;

        _instanceMutex?.Dispose();
        _instanceMutex = null;
    }
}
