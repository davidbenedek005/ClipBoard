using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipboardSync.App.Services;
using QRCoder;

namespace ClipboardSync.App.Views;

/// <summary>
/// The only window. Closing it hides it back to the tray; Exit in the tray
/// menu is what quits the process.
/// </summary>
public partial class MainWindow : Window
{
    private readonly PairingService _pairing;
    private readonly AppSettings _settings;
    private readonly SyncHistory _history;
    private readonly Func<bool> _phoneConnected;
    private readonly Action<string> _sendFile;
    private readonly Action _disconnect;
    private readonly Action _tokenRegenerated;
    private readonly Action<DiscoveredHub> _joinHub;
    private readonly Action<string> _submitPin;
    private readonly Action _leaveHub;
    private readonly Action _probeHubs;

    public MainWindow(
        PairingService pairing,
        AppSettings settings,
        SyncHistory history,
        FileTransferTracker transfers,
        Func<bool> phoneConnected,
        Action<string> sendFile,
        Action disconnect,
        Action tokenRegenerated,
        System.Collections.ObjectModel.ObservableCollection<DiscoveredHub> nearby,
        Action<DiscoveredHub> joinHub,
        Action<string> submitPin,
        Action leaveHub,
        Action probeHubs)
    {
        _pairing = pairing;
        _settings = settings;
        _history = history;
        _phoneConnected = phoneConnected;
        _sendFile = sendFile;
        _disconnect = disconnect;
        _tokenRegenerated = tokenRegenerated;
        _joinHub = joinHub;
        _submitPin = submitPin;
        _leaveHub = leaveHub;
        _probeHubs = probeHubs;
        InitializeComponent();

        TransferList.ItemsSource = transfers.Active;
        HistoryList.ItemsSource = history.Entries;
        history.Entries.CollectionChanged += (_, _) => UpdateEmptyState();
        UpdateEmptyState();

        SyncImagesToggle.IsChecked = settings.SyncImages;
        DeviceNameBox.Text = settings.DisplayName;
        HubList.ItemsSource = nearby;
        AutoStartToggle.IsChecked = StartupRegistration.IsEnabled();
        RenderPairing();
    }

    public bool AllowClose { get; set; }

    public void BringToFront()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void SetStatus(string status)
    {
        StatusText.Text = status;
        var live = status.Contains("device connected", StringComparison.OrdinalIgnoreCase);
        StatusDot.Fill = live
            ? new SolidColorBrush(Color.FromRgb(0x10, 0x89, 0x3E))
            : new SolidColorBrush(Color.FromRgb(0x9A, 0xA4, 0xB1));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (HistoryPage is null)
        {
            return;
        }

        HistoryPage.Visibility = sender == NavHistory ? Visibility.Visible : Visibility.Collapsed;
        PairingPage.Visibility = sender == NavPairing ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = sender == NavSettings ? Visibility.Visible : Visibility.Collapsed;
        if (sender == NavPairing)
        {
            RenderPairing();
            _probeHubs();
        }
    }

    private void UpdateEmptyState()
    {
        var empty = _history.Entries.Count == 0;
        HistoryEmpty.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ClearHistoryButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SendFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Send files to the phone",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            SendFiles(dialog.FileNames);
        }
    }

    private void SendFiles(IEnumerable<string> paths)
    {
        var files = paths.Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            return;
        }

        if (!_phoneConnected())
        {
            MessageBox.Show(this, "Connect a phone or another PC first, then send the file again.", "ClipBoard",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        NavHistory.IsChecked = true;
        foreach (var path in files)
        {
            _sendFile(path);
        }
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        DropOverlay.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Root_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between child elements; only hide once the cursor leaves the window.
        if (!new Rect(RootGrid.RenderSize).Contains(e.GetPosition(RootGrid)))
        {
            DropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            SendFiles(paths);
        }

        e.Handled = true;
    }

    private void CancelTransfer_Click(object sender, RoutedEventArgs e)
    {
        ((sender as Button)?.DataContext as FileTransfer)?.Cancel();
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not FileHistoryEntry entry)
        {
            return;
        }

        if (!File.Exists(entry.FilePath))
        {
            MessageBox.Show(this, "The file was moved or deleted.", "ClipBoard", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(entry.FilePath) { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ClipBoard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not FileHistoryEntry entry)
        {
            return;
        }

        var arguments = File.Exists(entry.FilePath)
            ? $"/select,\"{entry.FilePath}\""
            : $"\"{Path.GetDirectoryName(entry.FilePath)}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is HistoryEntry entry)
        {
            _history.Remove(entry);
        }
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "Remove every item from the sync history on this PC? The phone keeps its own history.",
            "Clear sync history",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK)
        {
            _history.Clear();
        }
    }

    private void RenderPairing()
    {
        var payload = _pairing.CreateQrPayload();
        AddressHint.Text = payload.Ip == "0.0.0.0"
            ? "No LAN address found. Connect this PC to Wi-Fi or Ethernet first."
            : "Open the camera app and point it at the code. It offers to open ClipBoard.";
        IpText.Text = payload.Ip;
        PortText.Text = payload.Port.ToString();
        TokenText.Text = payload.Token;

        // ECC level L keeps the modules large; the quiet zone is the white margin scanners need.
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(_pairing.CreatePairingUri(), QRCodeGenerator.ECCLevel.L);
        var png = new PngByteQRCode(data).GetGraphic(
            pixelsPerModule: 16,
            darkColorRgba: [0, 0, 0, 255],
            lightColorRgba: [255, 255, 255, 255],
            drawQuietZones: true);
        QrImage.Source = global::ClipboardSync.App.Services.QrImage.FromPng(png);
    }

    private void Regenerate_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "The phone will stay disconnected until you scan the new code.",
            "New pairing code",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        _pairing.RegenerateToken();
        _tokenRegenerated();
        RenderPairing();
    }

    public void SetHubStatus(string status)
    {
        HubStatus.Text = status;
    }

    public void PromptForPin()
    {
        NavPairing.IsChecked = true;
        PinEntry.Visibility = Visibility.Visible;
        PinHint.Text = "Enter the 6-digit PIN shown on the other PC.";
        PinBox.Text = "";
        PinBox.Focus();
    }

    private void JoinHub_Click(object sender, RoutedEventArgs e)
    {
        if (HubList.SelectedItem is not DiscoveredHub hub)
        {
            MessageBox.Show(this, "Select a PC from the list first.", "ClipBoard", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        PinEntry.Visibility = Visibility.Collapsed;
        _joinHub(hub);
    }

    private void ProbeHubs_Click(object sender, RoutedEventArgs e) => _probeHubs();

    private void LeaveHub_Click(object sender, RoutedEventArgs e)
    {
        PinEntry.Visibility = Visibility.Collapsed;
        _leaveHub();
    }

    private void SubmitPin_Click(object sender, RoutedEventArgs e)
    {
        _submitPin(PinBox.Text);
    }

    private void DeviceName_LostFocus(object sender, RoutedEventArgs e)
    {
        var name = DeviceNameBox.Text.Trim();
        if (name.Length == 0)
        {
            DeviceNameBox.Text = _settings.DisplayName;
            return;
        }

        _settings.DeviceName = name;
        _settings.Save();
    }

    private void SyncImages_Click(object sender, RoutedEventArgs e)
    {
        _settings.SyncImages = SyncImagesToggle.IsChecked == true;
        _settings.Save();
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        StartupRegistration.SetEnabled(AutoStartToggle.IsChecked == true);
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        _disconnect();
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is TextHistoryEntry entry)
        {
            Clipboard.SetText(entry.Text);
        }
    }

    private void CopyImage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is not ImageHistoryEntry entry)
        {
            return;
        }

        using var stream = new System.IO.MemoryStream(entry.Jpeg);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        Clipboard.SetImage(image);
    }
}
