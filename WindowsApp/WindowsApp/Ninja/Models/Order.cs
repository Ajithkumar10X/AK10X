namespace WindowsApp.Ninja.Models;

public class Order
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8].ToUpper();
    public required string Symbol { get; set; }
    public OrderSide Side { get; set; }
    public OrderType Type { get; set; }
    public OrderState State { get; set; } = OrderState.Submitted;
    public int Quantity { get; set; } = 1;
    public double LimitPrice { get; set; }
    public double StopPrice { get; set; }
    public double FillPrice { get; set; }
    public DateTime CreatedTime { get; set; } = DateTime.Now;
    public DateTime? FilledTime { get; set; }
    
    // ATM OCO linkage
    public string? OcoGroupId { get; set; }
    public bool IsAtmOrder { get; set; }
    public string? AtmRole { get; set; } // "Entry", "Target", "StopLoss"
}

public class AtmStrategyConfig
{
    public string Name { get; set; } = "Default ATM";
    public bool IsEnabled { get; set; } = true;
    public int TargetTicks { get; set; } = 8;        // e.g., 8 ticks (2.00 pts for ES = $100/contract)
    public int StopLossTicks { get; set; } = 8;      // e.g., 8 ticks
    public bool AutoBreakEvenEnabled { get; set; } = true;
    public int AutoBreakEvenTriggerTicks { get; set; } = 4; // Move to BE when profit >= 4 ticks
    public int AutoBreakEvenOffsetTicks { get; set; } = 1;  // Lock in +1 tick
    public bool AutoTrailEnabled { get; set; } = false;
    public int AutoTrailTriggerTicks { get; set; } = 6;
    public int AutoTrailStepTicks { get; set; } = 2;
}

public class ActiveAtmStrategy
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public required AtmStrategyConfig Config { get; set; }
    public required Order EntryOrder { get; set; }
    public Order? TargetOrder { get; set; }
    public Order? StopLossOrder { get; set; }
    public bool BreakEvenMoved { get; set; }
    public double HighestFavorablePrice { get; set; }
    public double LowestFavorablePrice { get; set; }
}
