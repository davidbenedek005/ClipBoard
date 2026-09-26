using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClipboardSync.App.Services;

namespace ClipboardSync.App.Views;

/// <summary>Shows the 6-digit PIN on the hub PC. The other PC's user types it. Nothing is asked here.</summary>
internal static class PinPrompt
{
    private static readonly Dictionary<Guid, Window> Open = [];

    public static void Show(PinChallenge challenge)
    {
        Close(challenge.ConnectionId);
        var pin = new TextBlock
        {
            Text = challenge.Pin,
            FontSize = 40,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var window = new Window
        {
            Title = "ClipBoard pairing",
            Width = 380,
            Height = 240,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            Content = new Border
            {
                Padding = new Thickness(28),
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = challenge.DeviceName + " wants to join",
                            FontSize = 16,
                            TextWrapping = TextWrapping.Wrap,
                        },
                        new TextBlock
                        {
                            Text = "Enter this PIN on that PC. It expires in 90 seconds.",
                            Margin = new Thickness(0, 6, 0, 0),
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.FromRgb(0x5F, 0x6B, 0x7A)),
                        },
                        pin,
                    },
                },
            },
        };
        window.Closed += (_, _) => Open.Remove(challenge.ConnectionId);
        Open[challenge.ConnectionId] = window;
        window.Show();
    }

    public static void Close(Guid connectionId)
    {
        if (Open.Remove(connectionId, out var window))
        {
            window.Close();
        }
    }
}
