using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ClipboardSync.App.Views;

/// <summary>
/// Sidebar text and color, plus whether the "Join a PC" form should be visible.
/// Green when this PC is hosting at least one device or has joined another PC.
/// </summary>
public sealed class ConnectionStatus : INotifyPropertyChanged
{
    private string _text = "Waiting for a device";
    private bool _isConnected;
    private bool _isJoined;
    private int _devices;
    private bool _hubJoined;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Text
    {
        get => _text;
        private set => Set(ref _text, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set => Set(ref _isConnected, value);
    }

    public bool IsJoined
    {
        get => _isJoined;
        private set => Set(ref _isJoined, value);
    }

    public void NoteServer(string status)
    {
        var count = 0;
        var first = status.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is not null &&
            int.TryParse(first, out var parsed) &&
            status.Contains("connected", StringComparison.OrdinalIgnoreCase))
        {
            count = parsed;
        }

        if (_devices == count)
        {
            return;
        }

        _devices = count;
        Publish();
    }

    public void NoteHub(bool joined)
    {
        if (_hubJoined == joined)
        {
            return;
        }

        _hubJoined = joined;
        Publish();
    }

    private void Publish()
    {
        IsJoined = _hubJoined;
        IsConnected = _devices > 0 || _hubJoined;
        Text = (_hubJoined, _devices) switch
        {
            (true, > 1) => $"Connected to Hub · {_devices} devices connected",
            (true, 1) => "Connected to Hub · 1 device connected",
            (true, _) => "Connected to Hub",
            (false, 1) => "1 device connected",
            (false, > 1) => $"{_devices} devices connected",
            _ => "Waiting for a device",
        };
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
