using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WindowsApp.Ninja.Models;
using WindowsApp.Ninja.Services;

namespace WindowsApp.Ninja.Views;

public partial class NinjaConnectionView : UserControl
{
    private readonly NinjaConnectionManager _manager;

    public NinjaConnectionView()
    {
        InitializeComponent();
        _manager = NinjaConnectionManager.Instance;

        _manager.ConnectionStateChanged += OnConnectionStateChanged;
        _manager.AccountStateChanged += OnAccountStateChanged;

        Loaded += NinjaConnectionView_Loaded;
    }

    private void NinjaConnectionView_Loaded(object sender, RoutedEventArgs e)
    {
        ConnectionsGrid.ItemsSource = _manager.Connections;
        UpdateActiveConnectionDisplay();
        UpdateActiveAccountDisplay();
    }

    private void OnConnectionStateChanged()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateActiveConnectionDisplay();
            ConnectionsGrid.Items.Refresh();
        });
    }

    private void OnAccountStateChanged()
    {
        Dispatcher.Invoke(() =>
        {
            UpdateActiveAccountDisplay();
        });
    }

    private void UpdateActiveConnectionDisplay()
    {
        var active = _manager.ActiveConnection;
        if (active != null)
        {
            ActiveStatusText.Text = $"Connected: {active.Name} ({active.Mode} - {active.LatencyMs}ms)";
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Vivid Green
            ActiveStatusBadge.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
            ActiveStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(5, 150, 105));
        }
        else
        {
            ActiveStatusText.Text = "Disconnected (No Active Feeds)";
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
            ActiveStatusBadge.Background = new SolidColorBrush(Color.FromRgb(69, 10, 10));
            ActiveStatusBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(153, 27, 27));
        }
    }

    private void UpdateActiveAccountDisplay()
    {
        var acc = _manager.ActiveAccount ?? _manager.Accounts.FirstOrDefault();
        if (acc == null) return;

        string symbol = acc.Currency == "INR" ? "₹" : "$";
        CashBalanceText.Text = $"{symbol}{acc.CashBalance:N2}";
        BuyingPowerText.Text = $"{symbol}{acc.BuyingPower:N2}";

        string realSign = acc.RealizedPnL >= 0 ? "+" : "";
        RealizedPnlText.Text = $"{realSign}{symbol}{acc.RealizedPnL:N2}";
        RealizedPnlText.Foreground = acc.RealizedPnL >= 0 
            ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) 
            : new SolidColorBrush(Color.FromRgb(248, 113, 113));

        string unrealSign = acc.UnrealizedPnL >= 0 ? "+" : "";
        UnrealizedPnlText.Text = $"{unrealSign}{symbol}{acc.UnrealizedPnL:N2}";
        UnrealizedPnlText.Foreground = acc.UnrealizedPnL >= 0 
            ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) 
            : new SolidColorBrush(Color.FromRgb(248, 113, 113));
    }

    private void ToggleConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string connectionName)
        {
            _manager.ToggleConnection(connectionName);
        }
    }

    private void RefreshConnections_Click(object sender, RoutedEventArgs e)
    {
        // Simulate latency ping to configured servers
        var rand = new Random();
        foreach (var conn in _manager.Connections)
        {
            if (conn.IsConnected)
            {
                conn.LatencyMs = rand.Next(8, 35);
            }
        }
        ConnectionsGrid.Items.Refresh();
        UpdateActiveConnectionDisplay();
    }

    private void AddConnection_Click(object sender, RoutedEventArgs e)
    {
        // Add custom broker connection dynamically
        int count = _manager.Connections.Count + 1;
        _manager.Connections.Add(new NinjaConnection
        {
            Name = $"Custom Adapter {count}",
            TypeName = "NinjaTrader.Cbi.CustomOptions",
            Provider = "Direct Market Access (DMA) Fix Feed",
            Mode = "Live",
            AccountType = "Institutional FIX",
            ConnectOnStartup = false,
            Status = ConnectionStatus.Connected,
            LatencyMs = 12,
            FeedType = "Raw Tick L2 L3"
        });
        ConnectionsGrid.Items.Refresh();
        UpdateActiveConnectionDisplay();
    }
}
