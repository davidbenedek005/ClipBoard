using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ClipboardSync.App.Services;

namespace ClipboardSync.App.Views;

public partial class StatusWindow : Window
{
    public StatusWindow(SyncHistory history)
    {
        InitializeComponent();
        HistoryList.ItemsSource = history.Entries;
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
