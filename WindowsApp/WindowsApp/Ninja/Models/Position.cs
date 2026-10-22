namespace WindowsApp.Ninja.Models;

public class Position
{
    public required string Symbol { get; set; }
    public MarketPosition MarketPosition { get; set; } = MarketPosition.Flat;
    public int Quantity { get; set; } = 0;
    public double AveragePrice { get; set; } = 0.0;
    public double UnrealizedPnL { get; set; } = 0.0;
    public double RealizedPnL { get; set; } = 0.0;
    public double PointsPnL { get; set; } = 0.0;
    public double TicksPnL { get; set; } = 0.0;

    public void UpdatePnL(double currentPrice, double pointValue, double tickSize)
    {
        if (MarketPosition == MarketPosition.Flat || Quantity == 0)
        {
            UnrealizedPnL = 0;
            PointsPnL = 0;
            TicksPnL = 0;
            return;
        }

        double priceDiff = (MarketPosition == MarketPosition.Long)
            ? (currentPrice - AveragePrice)
            : (AveragePrice - currentPrice);

        PointsPnL = priceDiff;
        TicksPnL = tickSize > 0 ? priceDiff / tickSize : 0;
        UnrealizedPnL = priceDiff * pointValue * Quantity;
    }
}

public class SimAccount
{
    public string Name { get; set; } = "Sim101";
    public decimal CashBalance { get; set; } = 100_000.00m;
    public decimal RealizedPnL { get; set; } = 0.00m;
    public decimal MarginUsed { get; set; } = 0.00m;
    public decimal BuyingPower => CashBalance + RealizedPnL - MarginUsed;
}
