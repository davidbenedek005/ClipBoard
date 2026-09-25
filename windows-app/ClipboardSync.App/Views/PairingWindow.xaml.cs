using System.Windows;
using ClipboardSync.App.Services;
using QRCoder;

namespace ClipboardSync.App.Views;

public partial class PairingWindow : Window
{
    private readonly PairingService _pairing;

    public PairingWindow(PairingService pairing)
    {
        _pairing = pairing;
        InitializeComponent();
        RenderQr();
    }

    public Action? TokenRegenerated { get; set; }

    private void RenderQr()
    {
        var payload = _pairing.CreateQrPayload();
        AddressText.Text = payload.Ip == "0.0.0.0"
            ? "No LAN address found. Type the values below only after this PC has a Wi-Fi address."
            : "Scan with the phone's camera app. It should offer to open ClipBoard.";
        IpText.Text = payload.Ip;
        PortText.Text = payload.Port.ToString();
        TokenText.Text = payload.Token;

        // ECC level L uses the fewest modules for this payload, so the modules
        // stay large on a monitor. Quiet zones are the white margin the scanner
        // needs; the XAML border adds more white around that.
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
            "The phone will stay disconnected until you scan the new code.",
            "New pairing code",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        _pairing.RegenerateToken();
        TokenRegenerated?.Invoke();
        RenderQr();
    }
}
