using WindowsApp.Ninja.Models;

namespace WindowsApp.Ninja.Services;

public class NinjaTradingEngine
{
    public SimAccount Account { get; } = new();
    public List<Order> WorkingOrders { get; } = new();
    public List<Order> OrderHistory { get; } = new();
    public List<ActiveAtmStrategy> ActiveAtmStrategies { get; } = new();
    public AtmStrategyConfig CurrentAtmConfig { get; set; } = new();
    
    private readonly Dictionary<string, Position> _positions = new();
    private readonly object _lock = new();

    public event Action<Order>? OrderSubmitted;
    public event Action<Order>? OrderFilled;
    public event Action<Order>? OrderCancelled;
    public event Action<Position>? PositionUpdated;
    public event Action<SimAccount>? AccountUpdated;
    public event Action<string>? EngineLog;

    public Position GetPosition(string symbol)
    {
        lock (_lock)
        {
            if (!_positions.TryGetValue(symbol, out var pos))
            {
                pos = new Position { Symbol = symbol };
                _positions[symbol] = pos;
            }
            return pos;
        }
    }

    public Order SubmitOrder(FuturesContract contract, OrderSide side, OrderType type, int quantity, double price, bool useAtm = true)
    {
        lock (_lock)
        {
            var order = new Order
            {
                Symbol = contract.Symbol,
                Side = side,
                Type = type,
                Quantity = quantity,
                LimitPrice = (type == OrderType.Limit || type == OrderType.StopLimit) ? contract.RoundToTick(price) : 0,
                StopPrice = (type == OrderType.StopMarket || type == OrderType.StopLimit) ? contract.RoundToTick(price) : 0,
                IsAtmOrder = useAtm && CurrentAtmConfig.IsEnabled,
                AtmRole = "Entry"
            };

            // If Market order, fill immediately at current price
            if (type == OrderType.Market)
            {
                double fillPrice = (side == OrderSide.Buy) ? contract.CurrentPrice : contract.CurrentPrice;
                ExecuteFill(order, fillPrice, contract);
            }
            else
            {
                order.State = OrderState.Working;
                WorkingOrders.Add(order);
                OrderSubmitted?.Invoke(order);
                EngineLog?.Invoke($"[ORDER SUBMITTED] {order.Side} {order.Quantity} {contract.DisplayCode} @ {contract.FormatPrice(price)} ({type})");
            }

            return order;
        }
    }

    public void CancelOrder(string orderId)
    {
        lock (_lock)
        {
            var order = WorkingOrders.FirstOrDefault(o => o.Id == orderId);
            if (order != null)
            {
                order.State = OrderState.Cancelled;
                WorkingOrders.Remove(order);
                OrderHistory.Insert(0, order);
                OrderCancelled?.Invoke(order);
                EngineLog?.Invoke($"[ORDER CANCELLED] ID: {order.Id} ({order.Side} {order.Quantity} {order.Symbol})");
            }
        }
    }

    public void CancelAllOrders(string symbol)
    {
        lock (_lock)
        {
            var ordersToCancel = WorkingOrders.Where(o => o.Symbol == symbol).ToList();
            foreach (var order in ordersToCancel)
            {
                CancelOrder(order.Id);
            }
        }
    }

    public void FlattenPosition(FuturesContract contract, double marketPrice)
    {
        lock (_lock)
        {
            var pos = GetPosition(contract.Symbol);
            if (pos.MarketPosition == MarketPosition.Flat || pos.Quantity == 0)
                return;

            CancelAllOrders(contract.Symbol);

            var closeSide = pos.MarketPosition == MarketPosition.Long ? OrderSide.Sell : OrderSide.Buy;
            var closeOrder = new Order
            {
                Symbol = contract.Symbol,
                Side = closeSide,
                Type = OrderType.Market,
                Quantity = pos.Quantity,
                IsAtmOrder = false
            };

            ExecuteFill(closeOrder, marketPrice, contract);
            EngineLog?.Invoke($"[POSITION FLATTENED] {contract.DisplayCode} closed @ {contract.FormatPrice(marketPrice)}");
        }
    }

    public void ReversePosition(FuturesContract contract, double marketPrice)
    {
        lock (_lock)
        {
            var pos = GetPosition(contract.Symbol);
            if (pos.MarketPosition == MarketPosition.Flat || pos.Quantity == 0)
                return;

            int reverseQty = pos.Quantity * 2;
            var reverseSide = pos.MarketPosition == MarketPosition.Long ? OrderSide.Sell : OrderSide.Buy;
            
            CancelAllOrders(contract.Symbol);

            var reverseOrder = new Order
            {
                Symbol = contract.Symbol,
                Side = reverseSide,
                Type = OrderType.Market,
                Quantity = reverseQty,
                IsAtmOrder = CurrentAtmConfig.IsEnabled
            };

            ExecuteFill(reverseOrder, marketPrice, contract);
            EngineLog?.Invoke($"[POSITION REVERSED] {contract.DisplayCode} reversed to {reverseSide} x{pos.Quantity} @ {contract.FormatPrice(marketPrice)}");
        }
    }

    public void ProcessTick(FuturesContract contract, double lastPrice, double bidPrice, double askPrice)
    {
        lock (_lock)
        {
            contract.CurrentPrice = lastPrice;

            // 1. Check if any working orders are filled
            var workingSnapshot = WorkingOrders.Where(o => o.Symbol == contract.Symbol).ToList();
            foreach (var order in workingSnapshot)
            {
                bool filled = false;
                double fillPrice = 0;

                if (order.Type == OrderType.Limit)
                {
                    if (order.Side == OrderSide.Buy && askPrice <= order.LimitPrice)
                    {
                        filled = true;
                        fillPrice = order.LimitPrice;
                    }
                    else if (order.Side == OrderSide.Sell && bidPrice >= order.LimitPrice)
                    {
                        filled = true;
                        fillPrice = order.LimitPrice;
                    }
                }
                else if (order.Type == OrderType.StopMarket)
                {
                    if (order.Side == OrderSide.Buy && askPrice >= order.StopPrice)
                    {
                        filled = true;
                        fillPrice = askPrice;
                    }
                    else if (order.Side == OrderSide.Sell && bidPrice <= order.StopPrice)
                    {
                        filled = true;
                        fillPrice = bidPrice;
                    }
                }

                if (filled)
                {
                    ExecuteFill(order, fillPrice, contract);
                }
            }

            // 2. Manage Active ATM Trailing Stop & Auto Break-Even
            var pos = GetPosition(contract.Symbol);
            if (pos.MarketPosition != MarketPosition.Flat)
            {
                pos.UpdatePnL(lastPrice, contract.PointValue, contract.TickSize);
                PositionUpdated?.Invoke(pos);

                foreach (var atm in ActiveAtmStrategies.Where(a => a.EntryOrder.Symbol == contract.Symbol).ToList())
                {
                    ManageAtmStrategy(atm, contract, lastPrice);
                }
            }
        }
    }

    private void ExecuteFill(Order order, double fillPrice, FuturesContract contract)
    {
        order.FillPrice = fillPrice;
        order.FilledTime = DateTime.Now;
        order.State = OrderState.Filled;

        WorkingOrders.Remove(order);
        OrderHistory.Insert(0, order);
        OrderFilled?.Invoke(order);

        // Update Position
        var pos = GetPosition(contract.Symbol);
        UpdatePositionWithFill(pos, order, contract);

        EngineLog?.Invoke($"[ORDER FILLED] {order.Side} {order.Quantity} {contract.DisplayCode} @ {contract.FormatPrice(fillPrice)} ({order.Type})");

        // Handle OCO linkage
        if (!string.IsNullOrEmpty(order.OcoGroupId))
        {
            var ocoCounterparts = WorkingOrders
                .Where(o => o.OcoGroupId == order.OcoGroupId && o.Id != order.Id)
                .ToList();

            foreach (var counterpart in ocoCounterparts)
            {
                counterpart.State = OrderState.Cancelled;
                WorkingOrders.Remove(counterpart);
                OrderHistory.Insert(0, counterpart);
                OrderCancelled?.Invoke(counterpart);
                EngineLog?.Invoke($"[OCO CANCEL] Counterpart Order {counterpart.AtmRole} ID: {counterpart.Id} cancelled.");
            }
        }

        // Handle ATM Generation on Entry Fill
        if (order.IsAtmOrder && order.AtmRole == "Entry" && pos.MarketPosition != MarketPosition.Flat)
        {
            SpawnAtmBracket(order, fillPrice, contract);
        }
    }

    private void SpawnAtmBracket(Order entryOrder, double fillPrice, FuturesContract contract)
    {
        string ocoId = Guid.NewGuid().ToString("N")[..8];
        var config = CurrentAtmConfig;
        var exitSide = entryOrder.Side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;

        double targetOffset = config.TargetTicks * contract.TickSize;
        double stopOffset = config.StopLossTicks * contract.TickSize;

        double targetPrice = entryOrder.Side == OrderSide.Buy 
            ? contract.RoundToTick(fillPrice + targetOffset) 
            : contract.RoundToTick(fillPrice - targetOffset);

        double stopPrice = entryOrder.Side == OrderSide.Buy 
            ? contract.RoundToTick(fillPrice - stopOffset) 
            : contract.RoundToTick(fillPrice + stopOffset);

        var targetOrder = new Order
        {
            Symbol = contract.Symbol,
            Side = exitSide,
            Type = OrderType.Limit,
            Quantity = entryOrder.Quantity,
            LimitPrice = targetPrice,
            OcoGroupId = ocoId,
            IsAtmOrder = true,
            AtmRole = "Target",
            State = OrderState.Working
        };

        var stopOrder = new Order
        {
            Symbol = contract.Symbol,
            Side = exitSide,
            Type = OrderType.StopMarket,
            Quantity = entryOrder.Quantity,
            StopPrice = stopPrice,
            OcoGroupId = ocoId,
            IsAtmOrder = true,
            AtmRole = "StopLoss",
            State = OrderState.Working
        };

        WorkingOrders.Add(targetOrder);
        WorkingOrders.Add(stopOrder);
        OrderSubmitted?.Invoke(targetOrder);
        OrderSubmitted?.Invoke(stopOrder);

        var activeAtm = new ActiveAtmStrategy
        {
            Config = config,
            EntryOrder = entryOrder,
            TargetOrder = targetOrder,
            StopLossOrder = stopOrder,
            HighestFavorablePrice = fillPrice,
            LowestFavorablePrice = fillPrice
        };
        ActiveAtmStrategies.Add(activeAtm);

        EngineLog?.Invoke($"[ATM BRACKET DEPLOYED] Target: {contract.FormatPrice(targetPrice)} (+{config.TargetTicks}t) | Stop: {contract.FormatPrice(stopPrice)} (-{config.StopLossTicks}t) | OCO: {ocoId}");
    }

    private void ManageAtmStrategy(ActiveAtmStrategy atm, FuturesContract contract, double currentPrice)
    {
        if (atm.StopLossOrder == null || atm.StopLossOrder.State != OrderState.Working)
            return;

        double entryPrice = atm.EntryOrder.FillPrice;
        bool isLong = atm.EntryOrder.Side == OrderSide.Buy;

        // Auto Break-Even
        if (atm.Config.AutoBreakEvenEnabled && !atm.BreakEvenMoved)
        {
            double beTriggerDistance = atm.Config.AutoBreakEvenTriggerTicks * contract.TickSize;
            bool triggerReached = isLong 
                ? (currentPrice >= entryPrice + beTriggerDistance)
                : (currentPrice <= entryPrice - beTriggerDistance);

            if (triggerReached)
            {
                double beOffset = atm.Config.AutoBreakEvenOffsetTicks * contract.TickSize;
                double newStop = isLong 
                    ? contract.RoundToTick(entryPrice + beOffset)
                    : contract.RoundToTick(entryPrice - beOffset);

                atm.StopLossOrder.StopPrice = newStop;
                atm.BreakEvenMoved = true;
                EngineLog?.Invoke($"[ATM AUTO-BE TRIGGERED] Stop moved to BE+{atm.Config.AutoBreakEvenOffsetTicks}t ({contract.FormatPrice(newStop)})");
            }
        }
    }

    private void UpdatePositionWithFill(Position pos, Order fillOrder, FuturesContract contract)
    {
        int fillQty = fillOrder.Quantity;
        double fillPrice = fillOrder.FillPrice;

        if (pos.MarketPosition == MarketPosition.Flat)
        {
            pos.MarketPosition = fillOrder.Side == OrderSide.Buy ? MarketPosition.Long : MarketPosition.Short;
            pos.Quantity = fillQty;
            pos.AveragePrice = fillPrice;
        }
        else if ((pos.MarketPosition == MarketPosition.Long && fillOrder.Side == OrderSide.Buy) ||
                 (pos.MarketPosition == MarketPosition.Short && fillOrder.Side == OrderSide.Sell))
        {
            // Adding to position
            double totalCost = (pos.AveragePrice * pos.Quantity) + (fillPrice * fillQty);
            pos.Quantity += fillQty;
            pos.AveragePrice = contract.RoundToTick(totalCost / pos.Quantity);
        }
        else
        {
            // Reducing or reversing position
            int closingQty = Math.Min(pos.Quantity, fillQty);
            double pointsPnL = pos.MarketPosition == MarketPosition.Long 
                ? (fillPrice - pos.AveragePrice) 
                : (pos.AveragePrice - fillPrice);

            double tradeRealizedPnL = pointsPnL * contract.PointValue * closingQty;
            pos.RealizedPnL += tradeRealizedPnL;
            Account.RealizedPnL += (decimal)tradeRealizedPnL;

            if (fillQty < pos.Quantity)
            {
                pos.Quantity -= fillQty;
            }
            else if (fillQty == pos.Quantity)
            {
                pos.MarketPosition = MarketPosition.Flat;
                pos.Quantity = 0;
                pos.AveragePrice = 0;
            }
            else
            {
                // Reversed
                int remainingQty = fillQty - pos.Quantity;
                pos.MarketPosition = fillOrder.Side == OrderSide.Buy ? MarketPosition.Long : MarketPosition.Short;
                pos.Quantity = remainingQty;
                pos.AveragePrice = fillPrice;
            }
        }

        pos.UpdatePnL(contract.CurrentPrice, contract.PointValue, contract.TickSize);
        PositionUpdated?.Invoke(pos);
        AccountUpdated?.Invoke(Account);
    }
}
