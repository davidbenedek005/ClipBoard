using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ClipboardSync.App.Services;

namespace ClipboardSync.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _disconnect;

    public SettingsWindow(AppSettings settings, SyncHistory history, Action disconnect)
    {
        _settings = settings;
        _disconnect = disconnect;
        InitializeComponent();
        SyncImagesBox.IsChecked = settings.SyncImages;
        HistoryList.ItemsSource = history.Entries;
    }

    private void OnImagesChanged(object sender, RoutedEventArgs e)
    {
        _settings.SyncImages = SyncImagesBox.IsChecked == true;
        _settings.Save();
    }

    private void OnDisconnect(object sender, RoutedEventArgs e)
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
        if ((sender as Button)?.DataContext is ImageHistoryEntry entry && entry.Thumbnail is BitmapSource image)
        {
            Clipboard.SetImage(image);
        }
    }
}
