using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WindowsApp.Ninja.Models;

public enum ConnectionStatus
{
    Connected,
    Connecting,
    Disconnected,
    Error
}

public class NinjaConnection : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private string _typeName = string.Empty;
    private string _provider = string.Empty;
    private string _mode = "Live";
    private string _accountType = "Simulation";
    private bool _connectOnStartup;
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private int _latencyMs;
    private string _feedType = "Level 2 Volumetric";

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public string TypeName
    {
        get => _typeName;
        set { if (_typeName != value) { _typeName = value; OnPropertyChanged(); } }
    }

    public string Provider
    {
        get => _provider;
        set { if (_provider != value) { _provider = value; OnPropertyChanged(); } }
    }

    public string Mode
    {
        get => _mode;
        set { if (_mode != value) { _mode = value; OnPropertyChanged(); } }
    }

    public string AccountType
    {
        get => _accountType;
        set { if (_accountType != value) { _accountType = value; OnPropertyChanged(); } }
    }

    public bool ConnectOnStartup
    {
        get => _connectOnStartup;
        set { if (_connectOnStartup != value) { _connectOnStartup = value; OnPropertyChanged(); } }
    }

    public ConnectionStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsConnected));
                OnPropertyChanged(nameof(StatusBadgeColor));
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(ActionBgColor));
            }
        }
    }

    public int LatencyMs
    {
        get => _latencyMs;
        set { if (_latencyMs != value) { _latencyMs = value; OnPropertyChanged(); } }
    }

    public string FeedType
    {
        get => _feedType;
        set { if (_feedType != value) { _feedType = value; OnPropertyChanged(); } }
    }

    public bool IsConnected => Status == ConnectionStatus.Connected;

    public string StatusBadgeColor => Status switch
    {
        ConnectionStatus.Connected => "#22C55E",    // Green
        ConnectionStatus.Connecting => "#EAB308",   // Yellow
        ConnectionStatus.Error => "#EF4444",        // Red
        _ => "#71717A"                              // Gray
    };

    public string ActionText => IsConnected ? "Disconnect" : "Connect";
    public string ActionBgColor => IsConnected ? "#DC2626" : "#2563EB"; // Red for disconnect, Blue for connect

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
