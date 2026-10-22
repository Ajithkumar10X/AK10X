namespace WindowsApp.Ninja.Models;

public enum OrderSide
{
    Buy,
    Sell
}

public enum OrderType
{
    Market,
    Limit,
    StopMarket,
    StopLimit
}

public enum OrderState
{
    Submitted,
    Working,
    Filled,
    Cancelled,
    Rejected
}

public enum MarketPosition
{
    Flat,
    Long,
    Short
}

public class FuturesContract
{
    public required string Symbol { get; set; }
    public required string Name { get; set; }
    public required string Exchange { get; set; }
    public required string Category { get; set; }
    public double TickSize { get; set; }
    public double PointValue { get; set; }
    public double TickValue => TickSize * PointValue;
    public decimal DayMargin { get; set; }
    public decimal InitialMargin { get; set; }
    public double CurrentPrice { get; set; }
    public double PreviousClose { get; set; }
    public string FrontMonth { get; set; } = "03-26";
    public string DisplayCode => $"{Symbol} {FrontMonth}";

    public string FormatPrice(double price)
    {
        if (TickSize >= 1.0)
            return price.ToString("F0");
        if (TickSize >= 0.1)
            return price.ToString("F1");
        if (TickSize >= 0.01)
            return price.ToString("F2");
        return price.ToString("F4");
    }

    public double RoundToTick(double price)
    {
        return Math.Round(price / TickSize) * TickSize;
    }
}
