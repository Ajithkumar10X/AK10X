using System.Collections.ObjectModel;

namespace WindowsApp.Ninja.OrderFlow.Models;

public class OrderFlowBar
{
    public int BarIndex { get; set; }
    public DateTime Time { get; set; } = DateTime.Now;
    public string FormattedTime => Time.ToString("HH:mm");

    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }

    public long BuyVolume { get; set; }
    public long SellVolume { get; set; }
    public long TotalVolume => BuyVolume + SellVolume;
    public long Delta => BuyVolume - SellVolume;
    public double DeltaPercent => TotalVolume > 0 ? ((double)Delta / TotalVolume) * 100.0 : 0.0;

    public long CumDelta { get; set; }
    public long MinDelta { get; set; }
    public long MaxDelta { get; set; }

    public double PocPrice { get; set; }
    public bool IsBullish => Close >= Open;

    // Advanced MZ Footprint patterns
    public bool HasAbsorption { get; set; }
    public bool HasStackedBuyImbalance { get; set; }
    public bool HasStackedSellImbalance { get; set; }
    public bool IsUnfinishedAuctionHigh { get; set; }
    public bool IsUnfinishedAuctionLow { get; set; }

    public ObservableCollection<OrderFlowPriceLevel> Levels { get; } = new();

    public void RecalculatePocAndImbalances(double imbalanceRatio = 3.0, long minImbalanceVolume = 15)
    {
        if (Levels.Count == 0) return;

        // 1. Identify POC (Level with maximum total volume)
        long maxVol = -1;
        double poc = Levels[0].Price;
        foreach (var lvl in Levels)
        {
            if (lvl.TotalVolume > maxVol)
            {
                maxVol = lvl.TotalVolume;
                poc = lvl.Price;
            }
        }
        PocPrice = poc;

        // 2. Mark POC level & Diagonal imbalances
        var sortedDesc = Levels.OrderByDescending(l => l.Price).ToList();
        int consecutiveBuy = 0;
        int consecutiveSell = 0;
        HasStackedBuyImbalance = false;
        HasStackedSellImbalance = false;
        HasAbsorption = false;

        for (int i = 0; i < sortedDesc.Count; i++)
        {
            var current = sortedDesc[i];
            current.IsPoc = Math.Abs(current.Price - PocPrice) < 0.0001;
            current.IsBuyImbalance = false;
            current.IsSellImbalance = false;
            current.IsStackedImbalance = false;
            current.IsAbsorption = false;

            // Buy imbalance vs diagonal lower level (index i + 1)
            if (i + 1 < sortedDesc.Count)
            {
                var lower = sortedDesc[i + 1];
                if (current.AskVolume >= minImbalanceVolume && 
                    (lower.BidVolume == 0 || current.AskVolume >= lower.BidVolume * imbalanceRatio))
                {
                    current.IsBuyImbalance = true;
                    consecutiveBuy++;
                }
                else
                {
                    consecutiveBuy = 0;
                }
            }

            // Sell imbalance vs diagonal higher level (index i - 1)
            if (i - 1 >= 0)
            {
                var higher = sortedDesc[i - 1];
                if (current.BidVolume >= minImbalanceVolume && 
                    (higher.AskVolume == 0 || current.BidVolume >= higher.AskVolume * imbalanceRatio))
                {
                    current.IsSellImbalance = true;
                    consecutiveSell++;
                }
                else
                {
                    consecutiveSell = 0;
                }
            }

            if (consecutiveBuy >= 3)
            {
                HasStackedBuyImbalance = true;
                current.IsStackedImbalance = true;
            }
            if (consecutiveSell >= 3)
            {
                HasStackedSellImbalance = true;
                current.IsStackedImbalance = true;
            }

            // Absorption Detection: High volume at the extremes (High or Low) with opposing close
            bool isNearExtreme = (i == 0 || i == sortedDesc.Count - 1);
            if (isNearExtreme && current.TotalVolume >= 100)
            {
                // If at High, but candle closed lower -> aggressive buyers absorbed by passive limit sellers
                if (i == 0 && Close < current.Price)
                {
                    current.IsAbsorption = true;
                    HasAbsorption = true;
                }
                // If at Low, but candle closed higher -> aggressive sellers absorbed by passive limit buyers
                else if (i == sortedDesc.Count - 1 && Close > current.Price)
                {
                    current.IsAbsorption = true;
                    HasAbsorption = true;
                }
            }
        }

        // Unfinished Auction:
        if (sortedDesc.Count > 0)
        {
            var highest = sortedDesc.First();
            var lowest = sortedDesc.Last();
            IsUnfinishedAuctionHigh = highest.BidVolume > 0 && highest.AskVolume > 0;
            IsUnfinishedAuctionLow = lowest.BidVolume > 0 && lowest.AskVolume > 0;
        }
    }
}
