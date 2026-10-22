using System.Windows.Media;

namespace WindowsApp.MarketProfile.Models;

public class TpoBlock
{
    public char Letter { get; set; } = 'A';
    public int PeriodIndex { get; set; }
    public double Price { get; set; }
    public string ColorHex { get; set; } = "#FFFFFF";
    public SolidColorBrush Brush { get; set; } = Brushes.White;
    public long Volume { get; set; }
    public string TimeLabel { get; set; } = string.Empty;
}

public class PriceBucket
{
    public double Price { get; set; }
    public List<TpoBlock> Tpos { get; set; } = new();
    public int TpoCount => Tpos.Count;
    public bool IsPoc { get; set; }
    public bool IsValueArea { get; set; }
    public bool IsSinglePrint { get; set; }
    public bool IsPoorLevel { get; set; }
    public long Volume { get; set; }
    public long BuyVolume { get; set; }
    public long SellVolume { get; set; }
    public long Delta => BuyVolume - SellVolume;

    public string LettersDisplay => new string(Tpos.Select(t => t.Letter).ToArray());
}

public class SinglePrintZone
{
    public double HighPrice { get; set; }
    public double LowPrice { get; set; }
    public bool IsBuy { get; set; } // true = Buy SP (Green #00783C), false = Sell SP (Red #960000)
    public bool IsCleared { get; set; }
    public char PeriodLetter { get; set; }
    public int PeriodIndex { get; set; }
    public double Span => Math.Abs(HighPrice - LowPrice);
}

public class TpoPeriodBar
{
    public int PeriodIndex { get; set; }
    public char Letter { get; set; }
    public string TimeLabel { get; set; } = string.Empty;
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }
    public List<double> PriceLevels { get; set; } = new();
}

public class MarketProfileSession
{
    public DateTime SessionDate { get; set; }
    public string DateDisplay => SessionDate.ToString("dd-MMM-yyyy");
    public string Symbol { get; set; } = "NIFTY";
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long TotalVolume { get; set; }

    public double PocPrice { get; set; }
    public int PocTpoCount { get; set; }
    public double VahPrice { get; set; }
    public double ValPrice { get; set; }
    public double IbHigh { get; set; }
    public double IbLow { get; set; }
    public double IbRange => Math.Max(0, IbHigh - IbLow);
    public double Halfback => (High + Low) / 2.0;

    public bool IsVirginPoc { get; set; }
    public bool IsPoorHigh { get; set; }
    public bool IsPoorLow { get; set; }
    public bool HasFailedAuction { get; set; }

    public string OpenType { get; set; } = "Open Auction";
    public string OpenRelation { get; set; } = "Open Inside PDVA";
    public string IbStatus { get; set; } = "Price Within IB";
    public int RotationalFactor { get; set; }

    public double TpoSize { get; set; } = 3.0;
    public List<PriceBucket> Buckets { get; set; } = new();
    public List<SinglePrintZone> SinglePrints { get; set; } = new();
    public List<TpoPeriodBar> Periods { get; set; } = new();
    public bool IsSplitMode { get; set; } = false;

    // Fast lookup
    private Dictionary<double, PriceBucket> _bucketMap = new();
    public Dictionary<double, PriceBucket> BucketMap => _bucketMap;

    public void RebuildMap()
    {
        _bucketMap = Buckets.ToDictionary(b => Math.Round(b.Price, 2), b => b);
    }
}

public class FiftyChartsPalette
{
    public static readonly SolidColorBrush BackgroundBrush = new(Color.FromRgb(7, 13, 16));         // #070D10
    public static readonly SolidColorBrush DividerBrush = new(Color.FromRgb(32, 32, 32));           // #202020
    public static readonly SolidColorBrush TitleBrush = new(Color.FromRgb(128, 128, 128));          // #808080
    public static readonly SolidColorBrush TitleHighlightBrush = new(Color.FromRgb(202, 138, 41));  // #CA8A29
    public static readonly SolidColorBrush PricePocBrush = new(Color.FromRgb(255, 255, 0));         // #FFFF00
    public static readonly SolidColorBrush PriceVpocBrush = new(Color.FromRgb(140, 140, 0));        // #8C8C00
    public static readonly SolidColorBrush PriceVppocBrush = new(Color.FromRgb(200, 200, 0));       // #C8C800
    public static readonly SolidColorBrush PriceValueAreaBg = new(Color.FromRgb(18, 34, 41));       // #122229
    public static readonly SolidColorBrush VolumePocBrush = new(Color.FromRgb(255, 83, 173));       // #FF53AD
    public static readonly SolidColorBrush VolumeVaBrush = new(Color.FromRgb(166, 0, 87));          // #A60057
    public static readonly SolidColorBrush InitialBalanceBrush = new(Color.FromRgb(0, 255, 0));     // #00FF00
    public static readonly SolidColorBrush SinglePrintBuyLine = new(Color.FromRgb(0, 120, 60));     // #00783C
    public static readonly SolidColorBrush SinglePrintBuyZone = new(Color.FromRgb(0, 64, 32));      // #004020
    public static readonly SolidColorBrush SinglePrintBuyCleared = new(Color.FromRgb(0, 44, 22));   // #002C16
    public static readonly SolidColorBrush SinglePrintSellLine = new(Color.FromRgb(150, 0, 0));     // #960000
    public static readonly SolidColorBrush SinglePrintSellZone = new(Color.FromRgb(66, 0, 0));      // #420000
    public static readonly SolidColorBrush SinglePrintSellCleared = new(Color.FromRgb(44, 0, 0));   // #2C0000
    public static readonly SolidColorBrush PoorHighLowBrush = new(Color.FromRgb(70, 70, 145));      // #464691
    public static readonly SolidColorBrush FailedAuctionBrush = new(Color.FromRgb(133, 133, 197));  // #8585C5
    public static readonly SolidColorBrush HalfbackIntrabarBrush = Brushes.White;
    public static readonly SolidColorBrush HalfbackProfileBrush = new(Color.FromRgb(102, 102, 102));// #666666
    public static readonly SolidColorBrush VisibleVolumeBrush = new(Color.FromRgb(71, 182, 182));   // #47B2B2
    public static readonly SolidColorBrush YAxisLtpBrush = new(Color.FromRgb(61, 61, 126));         // #3D3D7E

    // Exact 50Charts A-M letter rainbow palette
    public static readonly Dictionary<char, SolidColorBrush> LetterBrushes = new()
    {
        { 'A', new SolidColorBrush(Color.FromRgb(241, 5, 8)) },     // #F10508 Red
        { 'B', new SolidColorBrush(Color.FromRgb(254, 45, 0)) },    // #FE2D00 Red-Orange
        { 'C', new SolidColorBrush(Color.FromRgb(254, 111, 0)) },   // #FE6F00 Orange
        { 'D', new SolidColorBrush(Color.FromRgb(254, 155, 0)) },   // #FE9B00 Amber
        { 'E', new SolidColorBrush(Color.FromRgb(241, 188, 15)) },  // #F1BC0F Yellow-Gold
        { 'F', new SolidColorBrush(Color.FromRgb(239, 225, 1)) },   // #EFE101 Yellow
        { 'G', new SolidColorBrush(Color.FromRgb(209, 254, 3)) },   // #D1FE03 Lime Green
        { 'H', new SolidColorBrush(Color.FromRgb(150, 254, 38)) },  // #96FE26 Bright Green
        { 'I', new SolidColorBrush(Color.FromRgb(69, 243, 112)) },  // #45F370 Mint Green
        { 'J', new SolidColorBrush(Color.FromRgb(0, 220, 200)) },   // #00DCC8 Turquoise
        { 'K', new SolidColorBrush(Color.FromRgb(1, 170, 212)) },   // #01AAD4 Cyan
        { 'L', new SolidColorBrush(Color.FromRgb(0, 124, 224)) },   // #007CE0 Sky Blue
        { 'M', new SolidColorBrush(Color.FromRgb(0, 75, 236)) },    // #004BEC Royal Blue
    };

    public static readonly Dictionary<char, string> LetterHexes = new()
    {
        { 'A', "#F10508" },
        { 'B', "#FE2D00" },
        { 'C', "#FE6F00" },
        { 'D', "#FE9B00" },
        { 'E', "#F1BC0F" },
        { 'F', "#EFE101" },
        { 'G', "#D1FE03" },
        { 'H', "#96FE26" },
        { 'I', "#45F370" },
        { 'J', "#00DCC8" },
        { 'K', "#01AAD4" },
        { 'L', "#007CE0" },
        { 'M', "#004BEC" },
    };

    public static SolidColorBrush GetLetterBrush(char letter)
    {
        if (LetterBrushes.TryGetValue(letter, out var brush))
            return brush;
        return Brushes.White;
    }

    public static string GetLetterHex(char letter)
    {
        if (LetterHexes.TryGetValue(letter, out var hex))
            return hex;
        return "#FFFFFF";
    }

    static FiftyChartsPalette()
    {
        BackgroundBrush.Freeze();
        DividerBrush.Freeze();
        TitleBrush.Freeze();
        TitleHighlightBrush.Freeze();
        PricePocBrush.Freeze();
        PriceVpocBrush.Freeze();
        PriceVppocBrush.Freeze();
        PriceValueAreaBg.Freeze();
        VolumePocBrush.Freeze();
        VolumeVaBrush.Freeze();
        InitialBalanceBrush.Freeze();
        SinglePrintBuyLine.Freeze();
        SinglePrintBuyZone.Freeze();
        SinglePrintBuyCleared.Freeze();
        SinglePrintSellLine.Freeze();
        SinglePrintSellZone.Freeze();
        SinglePrintSellCleared.Freeze();
        PoorHighLowBrush.Freeze();
        FailedAuctionBrush.Freeze();
        HalfbackIntrabarBrush.Freeze();
        HalfbackProfileBrush.Freeze();
        VisibleVolumeBrush.Freeze();
        YAxisLtpBrush.Freeze();

        foreach (var b in LetterBrushes.Values)
        {
            b.Freeze();
        }
    }
}
