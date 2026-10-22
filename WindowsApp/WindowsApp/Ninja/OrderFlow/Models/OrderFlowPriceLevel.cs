namespace WindowsApp.Ninja.OrderFlow.Models;

public class OrderFlowPriceLevel
{
    public double Price { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;
    public long BidVolume { get; set; }
    public long AskVolume { get; set; }
    public long TotalVolume => BidVolume + AskVolume;
    public long Delta => AskVolume - BidVolume;

    // Diagonal imbalance detection (e.g. Ask > Bid * 300%)
    public bool IsBuyImbalance { get; set; }
    public bool IsSellImbalance { get; set; }
    public bool IsPoc { get; set; }

    // Advanced MZ Footprint patterns
    public bool IsStackedImbalance { get; set; }
    public bool IsAbsorption { get; set; }
    public bool IsExhaustion { get; set; }
    public bool IsValueArea { get; set; }
}
