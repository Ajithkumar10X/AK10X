using WindowsApp.MarketProfile.Models;

namespace WindowsApp.MarketProfile.Services;

public class FiftyChartsProfileEngine
{
    private static readonly Lazy<FiftyChartsProfileEngine> _instance = new(() => new FiftyChartsProfileEngine());
    public static FiftyChartsProfileEngine Instance => _instance.Value;

    // Time slots for Indian Markets (NSE/MCX: 09:15 to 15:30 IST)
    private static readonly string[] Daily30mTimeLabels = new[]
    {
        "09:15 - 09:45", // A
        "09:45 - 10:15", // B
        "10:15 - 10:45", // C
        "10:45 - 11:15", // D
        "11:15 - 11:45", // E
        "11:45 - 12:15", // F
        "12:15 - 12:45", // G
        "12:45 - 13:15", // H
        "13:15 - 13:45", // I
        "13:45 - 14:15", // J
        "14:15 - 14:45", // K
        "14:45 - 15:15", // L
        "15:15 - 15:30"  // M
    };

    /// <summary>
    /// Generates multi-day Market Profile sessions with realistic market mechanics and exact 50Charts layout.
    /// </summary>
    public List<MarketProfileSession> GenerateHistoricalSessions(string symbol = "NIFTY", int days = 4)
    {
        var sessions = new List<MarketProfileSession>();
        double basePrice = symbol switch
        {
            "NIFTY" => 25850.0,
            "BANKNIFTY" => 54200.0,
            "ES" => 5890.0,
            "GC" => 2740.0,
            _ => 25000.0
        };

        double tpoSize = symbol switch
        {
            "NIFTY" => 3.0,
            "BANKNIFTY" => 10.0,
            "ES" => 0.25,
            "GC" => 0.50,
            _ => 1.0
        };

        var rng = new Random(10101);
        var today = DateTime.Today;

        MarketProfileSession? priorSession = null;

        for (int dayOffset = days - 1; dayOffset >= 0; dayOffset--)
        {
            var sessionDate = today.AddDays(-dayOffset);
            if (sessionDate.DayOfWeek == DayOfWeek.Saturday || sessionDate.DayOfWeek == DayOfWeek.Sunday)
                continue;

            // Price path simulation for this day
            double drift = (rng.NextDouble() - 0.48) * (tpoSize * 15);
            basePrice += drift;
            double openPrice = Math.Round(basePrice / tpoSize) * tpoSize;

            var session = BuildSingleSession(symbol, sessionDate, openPrice, tpoSize, rng, priorSession);
            sessions.Add(session);
            priorSession = session;
        }

        return sessions;
    }

    public MarketProfileSession BuildSingleSession(
        string symbol, 
        DateTime sessionDate, 
        double openPrice, 
        double tpoSize, 
        Random rng, 
        MarketProfileSession? priorSession = null)
    {
        var session = new MarketProfileSession
        {
            SessionDate = sessionDate,
            Symbol = symbol,
            TpoSize = tpoSize,
            Open = openPrice
        };

        int periodCount = 13; // A through M
        double currentPrice = openPrice;
        double sessionHigh = openPrice;
        double sessionLow = openPrice;

        var periodBars = new List<TpoPeriodBar>();
        var priceTposMap = new Dictionary<double, List<TpoBlock>>();
        var priceVolumesMap = new Dictionary<double, (long buyVol, long sellVol)>();

        // Step 1: Simulate the 13 TPO periods (A to M)
        for (int pIdx = 0; pIdx < periodCount; pIdx++)
        {
            char letter = (char)('A' + pIdx);
            string timeLabel = pIdx < Daily30mTimeLabels.Length ? Daily30mTimeLabels[pIdx] : $"P{pIdx + 1}";

            double barOpen = currentPrice;
            // Period swing amplitude
            double stepRange = (rng.NextDouble() * 8 + 4) * tpoSize;
            double bias = (rng.NextDouble() - 0.49);
            double barHigh = barOpen + (bias > 0 ? stepRange * 0.7 : stepRange * 0.3);
            double barLow = barOpen - (bias < 0 ? stepRange * 0.7 : stepRange * 0.3);
            barHigh = Math.Round(barHigh / tpoSize) * tpoSize;
            barLow = Math.Round(barLow / tpoSize) * tpoSize;
            if (barLow > barHigh) (barLow, barHigh) = (barHigh, barLow);
            if (Math.Abs(barHigh - barLow) < tpoSize) barHigh = barLow + tpoSize;

            double barClose = barLow + rng.NextDouble() * (barHigh - barLow);
            barClose = Math.Round(barClose / tpoSize) * tpoSize;
            currentPrice = barClose;

            sessionHigh = Math.Max(sessionHigh, barHigh);
            sessionLow = Math.Min(sessionLow, barLow);

            var bar = new TpoPeriodBar
            {
                PeriodIndex = pIdx,
                Letter = letter,
                TimeLabel = timeLabel,
                Open = barOpen,
                High = barHigh,
                Low = barLow,
                Close = barClose,
                Volume = (long)(rng.Next(1500, 8500) * (pIdx == 0 || pIdx == 12 ? 2.2 : 1.0))
            };

            // Stamp letters at all price levels in [barLow, barHigh]
            for (double price = barLow; price <= barHigh + (tpoSize * 0.001); price += tpoSize)
            {
                double roundedPrice = Math.Round(price, 2);
                bar.PriceLevels.Add(roundedPrice);

                if (!priceTposMap.ContainsKey(roundedPrice))
                {
                    priceTposMap[roundedPrice] = new List<TpoBlock>();
                    priceVolumesMap[roundedPrice] = (0, 0);
                }

                priceTposMap[roundedPrice].Add(new TpoBlock
                {
                    Letter = letter,
                    PeriodIndex = pIdx,
                    Price = roundedPrice,
                    ColorHex = FiftyChartsPalette.GetLetterHex(letter),
                    Brush = FiftyChartsPalette.GetLetterBrush(letter),
                    TimeLabel = timeLabel,
                    Volume = rng.Next(20, 180)
                });

                var currentVols = priceVolumesMap[roundedPrice];
                long addBuy = rng.Next(40, 250);
                long addSell = rng.Next(40, 250);
                priceVolumesMap[roundedPrice] = (currentVols.buyVol + addBuy, currentVols.sellVol + addSell);
            }

            periodBars.Add(bar);
        }

        session.High = sessionHigh;
        session.Low = sessionLow;
        session.Close = currentPrice;
        session.Periods = periodBars;

        // Step 2: Initial Balance (First 2 periods: A and B = 1 hour)
        var ibBarA = periodBars[0];
        var ibBarB = periodBars.Count > 1 ? periodBars[1] : ibBarA;
        session.IbHigh = Math.Max(ibBarA.High, ibBarB.High);
        session.IbLow = Math.Min(ibBarA.Low, ibBarB.Low);

        if (session.Close > session.IbHigh)
            session.IbStatus = "Price Above IB";
        else if (session.Close < session.IbLow)
            session.IbStatus = "Price Below IB";
        else
            session.IbStatus = "Price Within IB";

        // Step 3: Open Type Classification
        double aHigh = ibBarA.High;
        double aLow = ibBarA.Low;
        if (Math.Abs(session.Open - aLow) < tpoSize * 0.5 && session.Close > session.Open)
            session.OpenType = "Open Drive (Bullish)";
        else if (Math.Abs(session.Open - aHigh) < tpoSize * 0.5 && session.Close < session.Open)
            session.OpenType = "Open Drive (Bearish)";
        else if (Math.Abs(session.Open - aLow) < tpoSize * 1.5)
            session.OpenType = "Open Test-Drive";
        else if (session.High > session.IbHigh && session.Low < session.IbLow)
            session.OpenType = "Open Rejection-Reverse";
        else
            session.OpenType = "Open Auction";

        // Prior Day comparison if available
        if (priorSession != null)
        {
            if (session.Open > priorSession.VahPrice)
                session.OpenRelation = "Open Above PDVA";
            else if (session.Open < priorSession.ValPrice)
                session.OpenRelation = "Open Below PDVA";
            else
                session.OpenRelation = "Open Inside PDVA";
        }
        else
        {
            session.OpenRelation = "Open Inside PDVA";
        }

        // Step 4: Build PriceBuckets sorted descending by price
        var sortedPrices = priceTposMap.Keys.OrderByDescending(p => p).ToList();
        var buckets = new List<PriceBucket>();

        double maxTpoPrice = openPrice;
        int maxTpos = 0;
        long totalSessionVolume = 0;

        foreach (var price in sortedPrices)
        {
            var tpos = priceTposMap[price];
            var (buyVol, sellVol) = priceVolumesMap[price];
            long rowVol = buyVol + sellVol;
            totalSessionVolume += rowVol;

            var bucket = new PriceBucket
            {
                Price = price,
                Tpos = tpos,
                Volume = rowVol,
                BuyVolume = buyVol,
                SellVolume = sellVol
            };

            if (tpos.Count > maxTpos)
            {
                maxTpos = tpos.Count;
                maxTpoPrice = price;
            }

            buckets.Add(bucket);
        }

        session.TotalVolume = totalSessionVolume;
        session.PocPrice = maxTpoPrice;
        session.PocTpoCount = maxTpos;

        // Step 5: Calculate 70% Value Area (VAH & VAL)
        CalculateValueArea(buckets, maxTpoPrice, out double vah, out double val);
        session.VahPrice = vah;
        session.ValPrice = val;

        // Step 6: Identify Single Prints (SP), Poor High/Low, and Failed Auctions
        IdentifyProfileFeatures(session, buckets, periodBars);

        session.Buckets = buckets;
        session.RebuildMap();

        return session;
    }

    private void CalculateValueArea(List<PriceBucket> buckets, double pocPrice, out double vah, out double val)
    {
        vah = pocPrice;
        val = pocPrice;
        if (buckets.Count == 0) return;

        int pocIdx = buckets.FindIndex(b => Math.Abs(b.Price - pocPrice) < 0.001);
        if (pocIdx < 0) pocIdx = 0;

        buckets[pocIdx].IsPoc = true;
        buckets[pocIdx].IsValueArea = true;

        int totalTpos = buckets.Sum(b => b.TpoCount);
        int targetTpos = (int)(totalTpos * 0.70);
        int accumulatedTpos = buckets[pocIdx].TpoCount;

        int upIdx = pocIdx - 1;
        int downIdx = pocIdx + 1;

        while (accumulatedTpos < targetTpos && (upIdx >= 0 || downIdx < buckets.Count))
        {
            int upTpos = 0;
            if (upIdx >= 0)
            {
                upTpos += buckets[upIdx].TpoCount;
                if (upIdx - 1 >= 0) upTpos += buckets[upIdx - 1].TpoCount;
            }

            int downTpos = 0;
            if (downIdx < buckets.Count)
            {
                downTpos += buckets[downIdx].TpoCount;
                if (downIdx + 1 < buckets.Count) downTpos += buckets[downIdx + 1].TpoCount;
            }

            if (upTpos >= downTpos && upIdx >= 0)
            {
                buckets[upIdx].IsValueArea = true;
                accumulatedTpos += buckets[upIdx].TpoCount;
                vah = buckets[upIdx].Price;
                upIdx--;

                if (accumulatedTpos < targetTpos && upIdx >= 0)
                {
                    buckets[upIdx].IsValueArea = true;
                    accumulatedTpos += buckets[upIdx].TpoCount;
                    vah = buckets[upIdx].Price;
                    upIdx--;
                }
            }
            else if (downIdx < buckets.Count)
            {
                buckets[downIdx].IsValueArea = true;
                accumulatedTpos += buckets[downIdx].TpoCount;
                val = buckets[downIdx].Price;
                downIdx++;

                if (accumulatedTpos < targetTpos && downIdx < buckets.Count)
                {
                    buckets[downIdx].IsValueArea = true;
                    accumulatedTpos += buckets[downIdx].TpoCount;
                    val = buckets[downIdx].Price;
                    downIdx++;
                }
            }
            else if (upIdx >= 0)
            {
                buckets[upIdx].IsValueArea = true;
                accumulatedTpos += buckets[upIdx].TpoCount;
                vah = buckets[upIdx].Price;
                upIdx--;
            }
            else break;
        }
    }

    private void IdentifyProfileFeatures(MarketProfileSession session, List<PriceBucket> buckets, List<TpoPeriodBar> periodBars)
    {
        if (buckets.Count == 0) return;

        // Poor High check: Session High has 2 or more TPOs (flat top without 1-letter taper)
        var highBucket = buckets.First();
        session.IsPoorHigh = highBucket.TpoCount >= 2;
        if (session.IsPoorHigh) highBucket.IsPoorLevel = true;

        // Poor Low check: Session Low has 2 or more TPOs (flat bottom without 1-letter taper)
        var lowBucket = buckets.Last();
        session.IsPoorLow = lowBucket.TpoCount >= 2;
        if (session.IsPoorLow) lowBucket.IsPoorLevel = true;

        // Rotational factor
        int rotFactor = 0;
        for (int i = 1; i < periodBars.Count; i++)
        {
            var prev = periodBars[i - 1];
            var cur = periodBars[i];
            if (cur.High > prev.High) rotFactor++;
            else if (cur.High < prev.High) rotFactor--;

            if (cur.Low > prev.Low) rotFactor++;
            else if (cur.Low < prev.Low) rotFactor--;
        }
        session.RotationalFactor = rotFactor;

        // Failed auction detection: If a period extends past the extreme of prior 2 periods by 1 tick but reverses
        session.HasFailedAuction = (session.RotationalFactor < -2 && session.Close > session.Open) ||
                                   (session.RotationalFactor > 2 && session.Close < session.Open);

        // Single Prints (SP): Continuous rows with count == 1 between multi-TPO regions
        var spZones = new List<SinglePrintZone>();
        SinglePrintZone? currentZone = null;

        for (int i = 0; i < buckets.Count; i++)
        {
            var b = buckets[i];
            if (b.TpoCount == 1)
            {
                b.IsSinglePrint = true;
                var singleTpo = b.Tpos[0];

                if (currentZone == null)
                {
                    bool isBuy = singleTpo.PeriodIndex > 0 && singleTpo.Price > session.Open;
                    currentZone = new SinglePrintZone
                    {
                        HighPrice = b.Price,
                        LowPrice = b.Price,
                        IsBuy = isBuy,
                        PeriodLetter = singleTpo.Letter,
                        PeriodIndex = singleTpo.PeriodIndex,
                        IsCleared = false
                    };
                }
                else
                {
                    currentZone.LowPrice = b.Price; // expanding downwards since buckets ordered descending
                }
            }
            else
            {
                if (currentZone != null)
                {
                    // Only register if spans at least 2 consecutive rows to avoid noise
                    if (currentZone.Span >= session.TpoSize)
                    {
                        spZones.Add(currentZone);
                    }
                    currentZone = null;
                }
            }
        }

        if (currentZone != null && currentZone.Span >= session.TpoSize)
        {
            spZones.Add(currentZone);
        }

        session.SinglePrints = spZones;
    }
}
