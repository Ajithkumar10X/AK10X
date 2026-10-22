using System.Collections.ObjectModel;
using WindowsApp.Ninja.Models;

namespace WindowsApp.Ninja.Services;

public class NinjaConnectionManager
{
    private static NinjaConnectionManager? _instance;
    public static NinjaConnectionManager Instance => _instance ??= new NinjaConnectionManager();

    public ObservableCollection<NinjaConnection> Connections { get; } = new();
    public ObservableCollection<NinjaAccount> Accounts { get; } = new();

    public NinjaConnection? ActiveConnection => Connections.FirstOrDefault(c => c.IsConnected);
    public NinjaAccount? ActiveAccount => Accounts.FirstOrDefault(a => a.IsActive);

    public event Action? ConnectionStateChanged;
    public event Action? AccountStateChanged;

    public NinjaConnectionManager()
    {
        InitializeClonedConnections();
        InitializeClonedAccounts();
    }

    private void InitializeClonedConnections()
    {
        Connections.Clear();

        // 1. My NinjaTrader (Cloned from NT8 TradovateOptions - Default Connected on Startup)
        Connections.Add(new NinjaConnection
        {
            Name = "My NinjaTrader",
            TypeName = "NinjaTrader.Cbi.TradovateOptions",
            Provider = "Tradovate / NinjaTrader Brokerage",
            Mode = "Simulation",
            AccountType = "Simulation",
            ConnectOnStartup = true,
            Status = ConnectionStatus.Connected,
            LatencyMs = 14,
            FeedType = "Tick By Tick Level 2 Volumetric"
        });

        // 2. My Accelpix (Cloned from NT8 SampleOpenConnectOptions - Indian Market NSE/MCX)
        Connections.Add(new NinjaConnection
        {
            Name = "My Accelpix",
            TypeName = "NinjaTrader.NinjaScript.Adapters.SampleOpenConnectOptions",
            Provider = "Accelpix OpenConnect (NSE / MCX)",
            Mode = "Live",
            AccountType = "Live Market Data",
            ConnectOnStartup = false,
            Status = ConnectionStatus.Disconnected,
            LatencyMs = 28,
            FeedType = "Indian Markets High-Speed L2"
        });

        // 3. Simulated Data Feed (Cloned from NT8 SimulatorOptions)
        Connections.Add(new NinjaConnection
        {
            Name = "Simulated Data Feed",
            TypeName = "NinjaTrader.Cbi.SimulatorOptions",
            Provider = "NinjaTrader Native Simulator",
            Mode = "Simulation",
            AccountType = "Simulated Generator",
            ConnectOnStartup = false,
            Status = ConnectionStatus.Disconnected,
            LatencyMs = 1,
            FeedType = "Native Tick Replay"
        });

        // 4. Playback Connection (Cloned from NT8 PlaybackOptions)
        Connections.Add(new NinjaConnection
        {
            Name = "Playback Connection",
            TypeName = "NinjaTrader.Cbi.PlaybackOptions",
            Provider = "Historical Market Replay",
            Mode = "Historical",
            AccountType = "Replay",
            ConnectOnStartup = false,
            Status = ConnectionStatus.Disconnected,
            LatencyMs = 0,
            FeedType = "Historical Tick Level"
        });

        // 5. Kinetick - End Of Day (Free)
        Connections.Add(new NinjaConnection
        {
            Name = "Kinetick – End Of Day (Free)",
            TypeName = "NinjaTrader.Cbi.KinetickOptions",
            Provider = "Kinetick",
            Mode = "EOD",
            AccountType = "Free EOD",
            ConnectOnStartup = false,
            Status = ConnectionStatus.Disconnected,
            LatencyMs = 0,
            FeedType = "End of Day Equities & Futures"
        });
    }

    private void InitializeClonedAccounts()
    {
        Accounts.Clear();

        Accounts.Add(new NinjaAccount
        {
            Name = "Sim101",
            DisplayName = "Sim101 (Simulation)",
            ConnectionName = "My NinjaTrader",
            CashBalance = 100000.00,
            BuyingPower = 400000.00,
            RealizedPnL = 1420.50,
            UnrealizedPnL = 385.00,
            Currency = "USD",
            IsActive = true
        });

        Accounts.Add(new NinjaAccount
        {
            Name = "Tradovate_Sim",
            DisplayName = "My NinjaTrader Live Sim",
            ConnectionName = "My NinjaTrader",
            CashBalance = 50000.00,
            BuyingPower = 200000.00,
            RealizedPnL = 650.00,
            UnrealizedPnL = -120.00,
            Currency = "USD",
            IsActive = false
        });

        Accounts.Add(new NinjaAccount
        {
            Name = "Accelpix_Live",
            DisplayName = "Accelpix NSE/MCX Feed",
            ConnectionName = "My Accelpix",
            CashBalance = 250000.00,
            BuyingPower = 1000000.00,
            RealizedPnL = 4250.00,
            UnrealizedPnL = 1120.00,
            Currency = "INR",
            IsActive = false
        });
    }

    public void Connect(string name)
    {
        var conn = Connections.FirstOrDefault(c => c.Name == name);
        if (conn == null) return;

        conn.Status = ConnectionStatus.Connected;
        ConnectionStateChanged?.Invoke();
    }

    public void Disconnect(string name)
    {
        var conn = Connections.FirstOrDefault(c => c.Name == name);
        if (conn == null) return;

        conn.Status = ConnectionStatus.Disconnected;
        ConnectionStateChanged?.Invoke();
    }

    public void ToggleConnection(string name)
    {
        var conn = Connections.FirstOrDefault(c => c.Name == name);
        if (conn == null) return;

        conn.Status = conn.IsConnected ? ConnectionStatus.Disconnected : ConnectionStatus.Connected;
        ConnectionStateChanged?.Invoke();
    }

    public void SelectAccount(string accountName)
    {
        foreach (var acc in Accounts)
        {
            acc.IsActive = (acc.Name == accountName);
        }
        AccountStateChanged?.Invoke();
    }
}
