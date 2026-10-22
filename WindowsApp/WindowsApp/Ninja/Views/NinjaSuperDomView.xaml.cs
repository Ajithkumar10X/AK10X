using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WindowsApp.Ninja.Models;
using WindowsApp.Ninja.Services;

namespace WindowsApp.Ninja.Views;

public partial class NinjaSuperDomView : UserControl
{
    private readonly NinjaFuturesRegistry _registry;
    private readonly NinjaTradingEngine _engine;
    private readonly NinjaMarketFeed _feed;
    private FuturesContract? _currentContract;
    private bool _isInitialized = false;

    public NinjaSuperDomView()
    {
        InitializeComponent();

        _registry = new NinjaFuturesRegistry();
        _engine = new NinjaTradingEngine();
        _feed = new NinjaMarketFeed(_engine);

        // Bind DOM ladder
        LadderItemsControl.ItemsSource = _feed.DomLadder;

        // Hook engine events
        _engine.PositionUpdated += OnPositionUpdated;
        _engine.AccountUpdated += OnAccountUpdated;
        _engine.EngineLog += OnEngineLog;
        _engine.OrderSubmitted += _ => Dispatcher.Invoke(_feed.UpdateWorkingOrdersOnLadder);
        _engine.OrderFilled += _ => Dispatcher.Invoke(_feed.UpdateWorkingOrdersOnLadder);
        _engine.OrderCancelled += _ => Dispatcher.Invoke(_feed.UpdateWorkingOrdersOnLadder);

        // Load contracts into dropdown
        var contracts = _registry.GetAllContracts();
        InstrumentCombo.ItemsSource = contracts;
        InstrumentCombo.DisplayMemberPath = "DisplayCode";
        if (contracts.Count > 0)
        {
            InstrumentCombo.SelectedIndex = 0;
        }

        _isInitialized = true;

        // Apply initial ATM config
        SyncAtmConfig();

        // Start real-time simulated feed
        _feed.Start();
    }

    private int SelectedQuantity
    {
        get
        {
            if (int.TryParse(OrderQtyBox.Text, out int qty) && qty > 0)
                return qty;
            return 1;
        }
    }

    private void InstrumentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InstrumentCombo.SelectedItem is FuturesContract contract)
        {
            _currentContract = contract;
            ContractInfoText.Text = $"Tick: {contract.TickSize} | Pt: ${contract.PointValue} | Exch: {contract.Exchange} | Day Margin: ${contract.DayMargin:N0}";
            _feed.SetContract(contract);
            RefreshPositionDisplay();
        }
    }

    private void OnPositionUpdated(Position pos)
    {
        Dispatcher.Invoke(() =>
        {
            if (_currentContract != null && pos.Symbol == _currentContract.Symbol)
            {
                RefreshPositionDisplay();
            }
        });
    }

    private void OnAccountUpdated(SimAccount acc)
    {
        Dispatcher.Invoke(() =>
        {
            RealizedPnlText.Text = $"${acc.RealizedPnL:N2}";
            RealizedPnlText.Foreground = acc.RealizedPnL >= 0 
                ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) 
                : new SolidColorBrush(Color.FromRgb(248, 113, 113));
        });
    }

    private void RefreshPositionDisplay()
    {
        if (_currentContract == null) return;
        var pos = _engine.GetPosition(_currentContract.Symbol);

        if (pos.MarketPosition == MarketPosition.Flat)
        {
            PositionText.Text = "FLAT";
            PositionText.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
            OpenPnlText.Text = "$0.00";
            OpenPnlText.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        }
        else
        {
            string sideText = pos.MarketPosition == MarketPosition.Long ? "LONG" : "SHORT";
            PositionText.Text = $"{sideText} x{pos.Quantity} @ {_currentContract.FormatPrice(pos.AveragePrice)}";
            PositionText.Foreground = pos.MarketPosition == MarketPosition.Long
                ? new SolidColorBrush(Color.FromRgb(96, 165, 250))
                : new SolidColorBrush(Color.FromRgb(248, 113, 113));

            OpenPnlText.Text = $"{pos.UnrealizedPnL:+$#,##0.00;-$#,##0.00;$0.00} ({pos.TicksPnL:+0;-0;0}t)";
            OpenPnlText.Foreground = pos.UnrealizedPnL >= 0
                ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                : new SolidColorBrush(Color.FromRgb(248, 113, 113));
        }
    }

    private void OnEngineLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            string entry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            LogListBox.Items.Insert(0, entry);
            if (LogListBox.Items.Count > 200)
            {
                LogListBox.Items.RemoveAt(LogListBox.Items.Count - 1);
            }
        });
    }

    // --- Ladder Click Handlers ---
    private void BidCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is DomLevel level && _currentContract != null)
        {
            // Buy Limit at clicked price
            _engine.SubmitOrder(_currentContract, OrderSide.Buy, OrderType.Limit, SelectedQuantity, level.Price, useAtm: true);
        }
    }

    private void AskCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is DomLevel level && _currentContract != null)
        {
            // Sell Limit at clicked price
            _engine.SubmitOrder(_currentContract, OrderSide.Sell, OrderType.Limit, SelectedQuantity, level.Price, useAtm: true);
        }
    }

    private void WorkingBuy_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is DomLevel level && _currentContract != null)
        {
            var working = _engine.WorkingOrders
                .Where(o => o.Symbol == _currentContract.Symbol && o.Side == OrderSide.Buy && Math.Abs((o.LimitPrice > 0 ? o.LimitPrice : o.StopPrice) - level.Price) < 0.0001)
                .ToList();

            foreach (var o in working)
            {
                _engine.CancelOrder(o.Id);
            }
        }
    }

    private void WorkingSell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is DomLevel level && _currentContract != null)
        {
            var working = _engine.WorkingOrders
                .Where(o => o.Symbol == _currentContract.Symbol && o.Side == OrderSide.Sell && Math.Abs((o.LimitPrice > 0 ? o.LimitPrice : o.StopPrice) - level.Price) < 0.0001)
                .ToList();

            foreach (var o in working)
            {
                _engine.CancelOrder(o.Id);
            }
        }
    }

    // --- Action Bar Handlers ---
    private void BuyMarket_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContract == null) return;
        _engine.SubmitOrder(_currentContract, OrderSide.Buy, OrderType.Market, SelectedQuantity, _currentContract.CurrentPrice, useAtm: true);
    }

    private void SellMarket_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContract == null) return;
        _engine.SubmitOrder(_currentContract, OrderSide.Sell, OrderType.Market, SelectedQuantity, _currentContract.CurrentPrice, useAtm: true);
    }

    private void BuyBid_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContract == null) return;
        double bid = _currentContract.RoundToTick(_currentContract.CurrentPrice - _currentContract.TickSize);
        _engine.SubmitOrder(_currentContract, OrderSide.Buy, OrderType.Limit, SelectedQuantity, bid, useAtm: true);
    }

    private void BuyAsk_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContract == null) return;
        double ask = _currentContract.RoundToTick(_currentContract.CurrentPrice + _currentContract.TickSize);
        _engine.SubmitOrder(_currentContract, OrderSide.Buy, OrderType.Limit, SelectedQuantity, ask, useAtm: true);
    }

    private void SellBid_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContract == null) return;
        double bid = _currentContract.RoundToTick(_currentContract.CurrentPrice - _currentContract.TickSize);
        _engine.SubmitOrder(_currentContract, OrderSide.Sell, OrderType.Limit, SelectedQuantity, bid, useAtm: true);
    }

    private void SellAsk_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContract == null) return;
        double ask = _currentContract.RoundToTick(_currentContract.CurrentPrice + _currentContract.TickSize);
        _engine.SubmitOrder(_currentContract, OrderSide.Sell, OrderType.Limit, SelectedQuantity, ask, useAtm: true);
    }

    private void Flatten_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContract == null) return;
        _engine.FlattenPosition(_currentContract, _currentContract.CurrentPrice);
    }

    private void Reverse_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContract == null) return;
        _engine.ReversePosition(_currentContract, _currentContract.CurrentPrice);
    }

    private void CancelAll_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContract == null) return;
        _engine.CancelAllOrders(_currentContract.Symbol);
    }

    private void SetQty_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string qtyStr)
        {
            OrderQtyBox.Text = qtyStr;
        }
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_feed.IsRunning)
        {
            _feed.Stop();
            PlayPauseButton.Content = "▶ Play Feed";
            PlayPauseButton.Background = new SolidColorBrush(Color.FromRgb(5, 150, 105));
        }
        else
        {
            _feed.Start();
            PlayPauseButton.Content = "⏸ Pause Feed";
            PlayPauseButton.Background = new SolidColorBrush(Color.FromRgb(51, 65, 85));
        }
    }

    private void CenterDomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentContract != null)
        {
            _feed.SetContract(_currentContract);
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogListBox.Items.Clear();
    }

    private void AtmConfig_Changed(object sender, RoutedEventArgs e)
    {
        SyncAtmConfig();
    }

    private void AtmConfig_Changed(object sender, TextChangedEventArgs e)
    {
        SyncAtmConfig();
    }

    private void SyncAtmConfig()
    {
        if (!_isInitialized || _engine == null || TargetTicksBox == null || StopTicksBox == null || AtmEnabledCheck == null || AutoBeCheck == null || AutoBeTriggerBox == null || AutoTrailCheck == null || AutoTrailStepBox == null) return;

        var config = _engine.CurrentAtmConfig;
        config.IsEnabled = AtmEnabledCheck.IsChecked == true;

        if (int.TryParse(TargetTicksBox.Text, out int t) && t > 0)
            config.TargetTicks = t;

        if (int.TryParse(StopTicksBox.Text, out int s) && s > 0)
            config.StopLossTicks = s;

        config.AutoBreakEvenEnabled = AutoBeCheck.IsChecked == true;
        if (int.TryParse(AutoBeTriggerBox.Text, out int beTrig) && beTrig > 0)
            config.AutoBreakEvenTriggerTicks = beTrig;

        config.AutoTrailEnabled = AutoTrailCheck.IsChecked == true;
        if (int.TryParse(AutoTrailStepBox.Text, out int trailStep) && trailStep > 0)
            config.AutoTrailStepTicks = trailStep;
    }
}
