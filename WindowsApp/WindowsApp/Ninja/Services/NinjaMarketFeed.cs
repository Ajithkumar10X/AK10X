using System.Collections.ObjectModel;
using System.Windows.Threading;
using WindowsApp.Ninja.Models;

namespace WindowsApp.Ninja.Services;

public class NinjaMarketFeed
{
    private readonly NinjaTradingEngine _engine;
    private readonly DispatcherTimer _timer;
    private readonly Random _rand = new();
    private FuturesContract? _activeContract;
    private readonly Dictionary<double, long> _volumeProfile = new();
    private bool _isRunning = false;

    public ObservableCollection<DomLevel> DomLadder { get; } = new();
    public bool IsRunning => _isRunning;
    public int LadderDepth { get; set; } = 15; // 15 levels above and below

    public event Action<FuturesContract, double, double, double, int>? TickReceived;
    public event Action? DomLadderRefreshed;

    public NinjaMarketFeed(NinjaTradingEngine engine)
    {
        _engine = engine;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _timer.Tick += OnTimerTick;
    }

    public void SetContract(FuturesContract contract)
    {
        _activeContract = contract;
        _volumeProfile.Clear();
        InitializeLadder();
    }

    public void Start()
    {
        _isRunning = true;
        _timer.Start();
    }

    public void Stop()
    {
        _isRunning = false;
        _timer.Stop();
    }

    public void SetTickInterval(int milliseconds)
    {
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, milliseconds));
    }

    private void InitializeLadder()
    {
        if (_activeContract == null) return;

        DomLadder.Clear();
        double centerPrice = _activeContract.RoundToTick(_activeContract.CurrentPrice);
        double tick = _activeContract.TickSize;

        // Populate from Highest price to Lowest price (Top to Bottom)
        for (int i = LadderDepth; i >= -LadderDepth; i--)
        {
            double price = _activeContract.RoundToTick(centerPrice + (i * tick));
            int initialVol = _rand.Next(15, 250);
            _volumeProfile[price] = _rand.Next(200, 3500);

            var level = new DomLevel
            {
                Price = price,
                FormattedPrice = _activeContract.FormatPrice(price),
                AskVolume = i > 0 ? initialVol : 0,
                BidVolume = i < 0 ? initialVol : 0,
                TradedVolume = _volumeProfile[price],
                IsInsideAsk = (i == 1),
                IsInsideBid = (i == -1),
                IsLastTraded = (i == 0)
            };
            DomLadder.Add(level);
        }

        UpdateProfilePercentages();
        UpdateWorkingOrdersOnLadder();
        DomLadderRefreshed?.Invoke();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_activeContract == null) return;

        // Random walk: -1 tick, 0, or +1 tick
        int tickStep = _rand.Next(-1, 2);
        double oldPrice = _activeContract.CurrentPrice;
        double newPrice = _activeContract.RoundToTick(oldPrice + (tickStep * _activeContract.TickSize));
        
        _activeContract.CurrentPrice = newPrice;
        double bid = _activeContract.RoundToTick(newPrice - _activeContract.TickSize);
        double ask = _activeContract.RoundToTick(newPrice + _activeContract.TickSize);
        int tradeVolume = _rand.Next(1, 15);

        // Update Volume profile
        if (!_volumeProfile.ContainsKey(newPrice))
            _volumeProfile[newPrice] = 0;
        _volumeProfile[newPrice] += tradeVolume;

        // Process in trading engine (fills limit/stop orders & updates ATM)
        _engine.ProcessTick(_activeContract, newPrice, bid, ask);

        // Update DOM levels
        SyncDomLadder(newPrice, bid, ask);

        TickReceived?.Invoke(_activeContract, newPrice, bid, ask, tradeVolume);
    }

    public void SyncDomLadder(double lastPrice, double bestBid, double bestAsk)
    {
        if (_activeContract == null || DomLadder.Count == 0) return;

        double centerPrice = _activeContract.RoundToTick(lastPrice);
        double topPrice = _activeContract.RoundToTick(centerPrice + (LadderDepth * _activeContract.TickSize));
        double bottomPrice = _activeContract.RoundToTick(centerPrice - (LadderDepth * _activeContract.TickSize));

        // If market drifted outside the current top/bottom bounds, re-center
        if (DomLadder.First().Price < topPrice || DomLadder.Last().Price > bottomPrice)
        {
            InitializeLadder();
            return;
        }

        foreach (var level in DomLadder)
        {
            double p = level.Price;
            level.IsLastTraded = Math.Abs(p - lastPrice) < 0.0001;
            level.IsInsideAsk = Math.Abs(p - bestAsk) < 0.0001;
            level.IsInsideBid = Math.Abs(p - bestBid) < 0.0001;

            if (p > bestBid)
            {
                // Ask side: fluctuate liquidity
                level.AskVolume = Math.Max(5, level.AskVolume + _rand.Next(-5, 6));
                level.BidVolume = 0;
            }
            else if (p < bestAsk)
            {
                // Bid side: fluctuate liquidity
                level.BidVolume = Math.Max(5, level.BidVolume + _rand.Next(-5, 6));
                level.AskVolume = 0;
            }

            if (_volumeProfile.TryGetValue(p, out long vol))
            {
                level.TradedVolume = vol;
            }
        }

        UpdateProfilePercentages();
        UpdateWorkingOrdersOnLadder();
    }

    public void UpdateWorkingOrdersOnLadder()
    {
        if (_activeContract == null) return;

        var working = _engine.WorkingOrders.Where(o => o.Symbol == _activeContract.Symbol).ToList();

        foreach (var level in DomLadder)
        {
            int buyQty = working
                .Where(o => o.Side == OrderSide.Buy && Math.Abs((o.LimitPrice > 0 ? o.LimitPrice : o.StopPrice) - level.Price) < 0.0001)
                .Sum(o => o.Quantity);

            int sellQty = working
                .Where(o => o.Side == OrderSide.Sell && Math.Abs((o.LimitPrice > 0 ? o.LimitPrice : o.StopPrice) - level.Price) < 0.0001)
                .Sum(o => o.Quantity);

            level.WorkingBuyQuantity = buyQty;
            level.WorkingSellQuantity = sellQty;
        }
    }

    private void UpdateProfilePercentages()
    {
        long maxVol = DomLadder.Count > 0 ? DomLadder.Max(l => l.TradedVolume) : 1;
        if (maxVol <= 0) maxVol = 1;

        foreach (var level in DomLadder)
        {
            level.VolumeProfilePercentage = Math.Min(100.0, ((double)level.TradedVolume / maxVol) * 100.0);
        }
    }
}
