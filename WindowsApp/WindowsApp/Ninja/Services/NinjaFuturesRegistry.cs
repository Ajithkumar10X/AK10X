using WindowsApp.Ninja.Models;

namespace WindowsApp.Ninja.Services;

public class NinjaFuturesRegistry
{
    private readonly List<FuturesContract> _contracts;

    public NinjaFuturesRegistry()
    {
        _contracts = new List<FuturesContract>
        {
            // Equity Indices
            new()
            {
                Symbol = "ES",
                Name = "E-mini S&P 500",
                Exchange = "CME",
                Category = "Equity Index",
                TickSize = 0.25,
                PointValue = 50.0,
                DayMargin = 500m,
                InitialMargin = 12980m,
                CurrentPrice = 5824.50,
                PreviousClose = 5818.00,
                FrontMonth = "03-26"
            },
            new()
            {
                Symbol = "NQ",
                Name = "E-mini Nasdaq 100",
                Exchange = "CME",
                Category = "Equity Index",
                TickSize = 0.25,
                PointValue = 20.0,
                DayMargin = 1000m,
                InitialMargin = 18700m,
                CurrentPrice = 20450.25,
                PreviousClose = 20410.00,
                FrontMonth = "03-26"
            },
            new()
            {
                Symbol = "MES",
                Name = "Micro E-mini S&P 500",
                Exchange = "CME",
                Category = "Equity Index",
                TickSize = 0.25,
                PointValue = 5.0,
                DayMargin = 50m,
                InitialMargin = 1298m,
                CurrentPrice = 5824.50,
                PreviousClose = 5818.00,
                FrontMonth = "03-26"
            },
            new()
            {
                Symbol = "MNQ",
                Name = "Micro E-mini Nasdaq 100",
                Exchange = "CME",
                Category = "Equity Index",
                TickSize = 0.25,
                PointValue = 2.0,
                DayMargin = 100m,
                InitialMargin = 1870m,
                CurrentPrice = 20450.25,
                PreviousClose = 20410.00,
                FrontMonth = "03-26"
            },
            new()
            {
                Symbol = "YM",
                Name = "E-mini Dow Jones",
                Exchange = "CBOT",
                Category = "Equity Index",
                TickSize = 1.0,
                PointValue = 5.0,
                DayMargin = 500m,
                InitialMargin = 9900m,
                CurrentPrice = 42850.0,
                PreviousClose = 42790.0,
                FrontMonth = "03-26"
            },
            new()
            {
                Symbol = "MYM",
                Name = "Micro E-mini Dow",
                Exchange = "CBOT",
                Category = "Equity Index",
                TickSize = 1.0,
                PointValue = 0.50,
                DayMargin = 50m,
                InitialMargin = 990m,
                CurrentPrice = 42850.0,
                PreviousClose = 42790.0,
                FrontMonth = "03-26"
            },
            new()
            {
                Symbol = "RTY",
                Name = "E-mini Russell 2000",
                Exchange = "CME",
                Category = "Equity Index",
                TickSize = 0.10,
                PointValue = 50.0,
                DayMargin = 500m,
                InitialMargin = 7700m,
                CurrentPrice = 2210.4,
                PreviousClose = 2205.0,
                FrontMonth = "03-26"
            },

            // Energy
            new()
            {
                Symbol = "CL",
                Name = "Crude Oil",
                Exchange = "NYMEX",
                Category = "Energy",
                TickSize = 0.01,
                PointValue = 1000.0,
                DayMargin = 1000m,
                InitialMargin = 7200m,
                CurrentPrice = 71.45,
                PreviousClose = 70.80,
                FrontMonth = "04-26"
            },
            new()
            {
                Symbol = "MCL",
                Name = "Micro WTI Crude Oil",
                Exchange = "NYMEX",
                Category = "Energy",
                TickSize = 0.01,
                PointValue = 100.0,
                DayMargin = 100m,
                InitialMargin = 720m,
                CurrentPrice = 71.45,
                PreviousClose = 70.80,
                FrontMonth = "04-26"
            },

            // Metals
            new()
            {
                Symbol = "GC",
                Name = "Gold Futures",
                Exchange = "COMEX",
                Category = "Metals",
                TickSize = 0.10,
                PointValue = 100.0,
                DayMargin = 1000m,
                InitialMargin = 10450m,
                CurrentPrice = 4104.90,
                PreviousClose = 4106.00,
                FrontMonth = "12-26"
            },
            new()
            {
                Symbol = "MGC",
                Name = "Micro Gold",
                Exchange = "COMEX",
                Category = "Metals",
                TickSize = 0.10,
                PointValue = 10.0,
                DayMargin = 100m,
                InitialMargin = 1045m,
                CurrentPrice = 4104.90,
                PreviousClose = 4106.00,
                FrontMonth = "12-26"
            },

            // Indian NSE / MCX (via Accelpix OpenConnect Adapter)
            new()
            {
                Symbol = "NIFTY_I",
                Name = "NIFTY 50 Futures",
                Exchange = "NSE",
                Category = "Indian Indices",
                TickSize = 0.05,
                PointValue = 25.0,
                DayMargin = 400m,
                InitialMargin = 3200m,
                CurrentPrice = 24780.0,
                PreviousClose = 24710.0,
                FrontMonth = "OCT-26"
            },
            new()
            {
                Symbol = "BANKNIFTY_I",
                Name = "BANK NIFTY Futures",
                Exchange = "NSE",
                Category = "Indian Indices",
                TickSize = 0.05,
                PointValue = 15.0,
                DayMargin = 500m,
                InitialMargin = 4100m,
                CurrentPrice = 51240.0,
                PreviousClose = 51100.0,
                FrontMonth = "OCT-26"
            },
            new()
            {
                Symbol = "HDFCLIFE_I",
                Name = "HDFC Life Futures",
                Exchange = "NSE",
                Category = "Indian Equities",
                TickSize = 0.05,
                PointValue = 1100.0,
                DayMargin = 200m,
                InitialMargin = 1800m,
                CurrentPrice = 742.50,
                PreviousClose = 738.00,
                FrontMonth = "OCT-26"
            },
            new()
            {
                Symbol = "RELIANCE_I",
                Name = "Reliance Industries Futures",
                Exchange = "NSE",
                Category = "Indian Equities",
                TickSize = 0.05,
                PointValue = 250.0,
                DayMargin = 500m,
                InitialMargin = 3500m,
                CurrentPrice = 2740.0,
                PreviousClose = 2735.0,
                FrontMonth = "OCT-26"
            },
            new()
            {
                Symbol = "TATASTEEL_I",
                Name = "Tata Steel Futures",
                Exchange = "NSE",
                Category = "Indian Equities",
                TickSize = 0.05,
                PointValue = 5500.0,
                DayMargin = 300m,
                InitialMargin = 2200m,
                CurrentPrice = 171.74,
                PreviousClose = 171.25,
                FrontMonth = "OCT-26"
            },
            new()
            {
                Symbol = "SBIN_I",
                Name = "State Bank of India Futures",
                Exchange = "NSE",
                Category = "Indian Equities",
                TickSize = 0.05,
                PointValue = 1500.0,
                DayMargin = 400m,
                InitialMargin = 2800m,
                CurrentPrice = 812.0,
                PreviousClose = 808.50,
                FrontMonth = "OCT-26"
            },
            new()
            {
                Symbol = "TCS_I",
                Name = "Tata Consultancy Services Futures",
                Exchange = "NSE",
                Category = "Indian Equities",
                TickSize = 0.05,
                PointValue = 175.0,
                DayMargin = 600m,
                InitialMargin = 4200m,
                CurrentPrice = 4120.0,
                PreviousClose = 4110.0,
                FrontMonth = "OCT-26"
            },
            new()
            {
                Symbol = "INFY_I",
                Name = "Infosys Futures",
                Exchange = "NSE",
                Category = "Indian Equities",
                TickSize = 0.05,
                PointValue = 400.0,
                DayMargin = 400m,
                InitialMargin = 3100m,
                CurrentPrice = 1885.0,
                PreviousClose = 1878.0,
                FrontMonth = "OCT-26"
            },
            new()
            {
                Symbol = "HDFCBANK_I",
                Name = "HDFC Bank Futures",
                Exchange = "NSE",
                Category = "Indian Equities",
                TickSize = 0.05,
                PointValue = 550.0,
                DayMargin = 450m,
                InitialMargin = 3300m,
                CurrentPrice = 1680.0,
                PreviousClose = 1672.0,
                FrontMonth = "OCT-26"
            }
        };
    }

    public IReadOnlyList<FuturesContract> GetAllContracts() => _contracts.AsReadOnly();

    public FuturesContract? GetBySymbol(string symbol)
    {
        var existing = _contracts.FirstOrDefault(c => c.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) ||
                                                      c.Symbol.Equals(symbol + "_I", StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        // Auto-generate contract definition for newly subscribed Accelpix EQ or Futures symbols
        string clean = symbol.Trim().ToUpperInvariant();
        bool isFutures = clean.EndsWith("_I") || clean.EndsWith("_II");
        var contract = new FuturesContract
        {
            Symbol = clean,
            Name = clean.Replace("_I", "") + (isFutures ? " Futures" : " Equity (Cash)"),
            Exchange = "NSE",
            Category = isFutures ? "Indian Futures" : "Indian Equities (Cash)",
            TickSize = 0.05,
            PointValue = isFutures ? 100.0 : 1.0,
            DayMargin = 200m,
            InitialMargin = 1500m,
            CurrentPrice = 1000.0,
            PreviousClose = 1000.0,
            FrontMonth = "OCT-26"
        };
        _contracts.Add(contract);
        return contract;
    }
}
