using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WindowsApp.MarketProfile.Models;
using WindowsApp.MarketProfile.Services;

namespace WindowsApp.MarketProfile.Views;

public partial class FiftyChartsMarketProfileView : UserControl
{
    private List<MarketProfileSession> _sessions = new();
    private string _currentSymbol = "NIFTY_I";
    private double _zoomFactor = 1.0;
    private bool _isSplitMode = false;

    // Pan state
    private bool _isPanning = false;
    private Point _lastPanPoint;

    // View boundaries
    private double _globalMinPrice = 0;
    private double _globalMaxPrice = 0;
    private double _rowHeight = 15.0; // base pixels per TPO row
    private double _letterWidth = 11.0; // base pixels per TPO letter
    private bool _isViewLoaded = false;

    public FiftyChartsMarketProfileView()
    {
        InitializeComponent();
        _isViewLoaded = true;
        Loaded += FiftyChartsMarketProfileView_Loaded;
        Unloaded += FiftyChartsMarketProfileView_Unloaded;
    }

    private void FiftyChartsMarketProfileView_Loaded(object sender, RoutedEventArgs e)
    {
        AccelPixDataProvider.Instance.TickReceived += OnAccelPixTickReceived;
        LoadDataForSymbol(_currentSymbol);
    }

    private void FiftyChartsMarketProfileView_Unloaded(object sender, RoutedEventArgs e)
    {
        AccelPixDataProvider.Instance.TickReceived -= OnAccelPixTickReceived;
    }

    private void OnAccelPixTickReceived(AccelPixTick tick)
    {
        if (!_isViewLoaded) return;

        Dispatcher.InvokeAsync(() =>
        {
            if (LiveLtpText != null)
            {
                LiveLtpText.Text = $"LTP: {tick.Price:N2}";
            }
            if (LiveStreamDot != null)
            {
                LiveStreamDot.Fill = tick.Direction >= 0
                    ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                    : new SolidColorBrush(Color.FromRgb(239, 68, 68));
            }

            if (_sessions.Count > 0)
            {
                var today = _sessions.Last();
                today.Close = tick.Price;
                if (tick.Price > today.High) today.High = tick.Price;
                if (tick.Price < today.Low) today.Low = tick.Price;
                if (SummaryRangeText != null)
                {
                    SummaryRangeText.Text = $"{(today.High - today.Low):N1} pts";
                }
            }
        }, DispatcherPriority.Background);
    }

    public void LoadDataForSymbol(string symbol)
    {
        if (!_isViewLoaded) return;
        _currentSymbol = symbol;

        double tpoSize = _currentSymbol switch
        {
            "BANKNIFTY_I" or "BANKNIFTY" => 10.0,
            "TATASTEEL_I" or "TATASTEEL" => 0.25,
            "HDFCBANK_I" or "HDFCBANK" => 2.0,
            "ES" => 0.25,
            "GC" => 0.10,
            _ => 3.0
        };

        // Load authentic historical 1-min data from AccelPix
        _sessions = AccelPixDataProvider.Instance.BuildSessionsFromAccelPix(_currentSymbol, tpoSize);

        if (_sessions.Count == 0)
        {
            _sessions = FiftyChartsProfileEngine.Instance.GenerateHistoricalSessions(_currentSymbol, days: 4);
        }

        if (_sessions.Count == 0) return;

        // Calculate global price range across visible sessions
        _globalMinPrice = _sessions.Min(s => s.Low);
        _globalMaxPrice = _sessions.Max(s => s.High);

        // Add padding
        _globalMinPrice -= tpoSize * 4;
        _globalMaxPrice += tpoSize * 4;

        UpdateHeaderAndSummary();
        RenderProfileChart();
    }

    private void UpdateHeaderAndSummary()
    {
        if (!_isViewLoaded || _sessions.Count == 0 || TodayPocText == null) return;
        var today = _sessions.Last();

        TodayPocText.Text = today.PocPrice.ToString("N2");
        TodayVahText.Text = today.VahPrice.ToString("N2");
        TodayValText.Text = today.ValPrice.ToString("N2");
        IbRangeText.Text = $"{today.IbRange:N1} pts";
        LiveLtpText.Text = $"LTP: {today.Close:N2}";
        TpoSizeText.Text = $"{today.TpoSize:N1} pts";

        // Summary bar
        if (SummaryOpenType != null) SummaryOpenType.Text = today.OpenType;
        if (SummaryRotFactor != null)
        {
            SummaryRotFactor.Text = $"{(today.RotationalFactor >= 0 ? "+" : "")}{today.RotationalFactor} ({(today.RotationalFactor >= 0 ? "Bullish" : "Bearish")})";
            SummaryRotFactor.Foreground = today.RotationalFactor >= 0 ? Brushes.LimeGreen : Brushes.OrangeRed;
        }
        if (SummaryIbStatus != null) SummaryIbStatus.Text = today.IbStatus;
        if (SummaryPoorStatus != null)
        {
            SummaryPoorStatus.Text = today.IsPoorHigh ? "Poor High Detected!" : (today.IsPoorLow ? "Poor Low Detected!" : "Clean Taper");
            SummaryPoorStatus.Foreground = (today.IsPoorHigh || today.IsPoorLow) ? FiftyChartsPalette.PoorHighLowBrush : FiftyChartsPalette.TitleHighlightBrush;
        }
        if (SummaryRangeText != null) SummaryRangeText.Text = $"{(today.High - today.Low):N1} pts";
        if (SummaryTotalTpos != null) SummaryTotalTpos.Text = today.Buckets.Sum(b => b.TpoCount).ToString();
    }

    private double PriceToY(double price)
    {
        double range = _globalMaxPrice - _globalMinPrice;
        if (range <= 0.001) return 50;

        double normalized = (_globalMaxPrice - price) / range;
        double totalHeight = ProfileContainerGrid.Height > 0 ? ProfileContainerGrid.Height : 700;
        return 40 + normalized * (totalHeight - 80);
    }

    private double YToPrice(double y)
    {
        double totalHeight = ProfileContainerGrid.Height > 0 ? ProfileContainerGrid.Height : 700;
        double normalized = (y - 40) / (totalHeight - 80);
        return _globalMaxPrice - (normalized * (_globalMaxPrice - _globalMinPrice));
    }

    public void RenderProfileChart()
    {
        if (_sessions.Count == 0) return;

        ProfileDrawingCanvas.Children.Clear();
        PriceAxisCanvas.Children.Clear();

        double tpoSize = _sessions.First().TpoSize;
        int totalPriceSteps = (int)Math.Ceiling((_globalMaxPrice - _globalMinPrice) / tpoSize) + 1;
        double effectiveRowHeight = Math.Max(12, _rowHeight * _zoomFactor);
        double totalCanvasHeight = Math.Max(700, (totalPriceSteps * effectiveRowHeight) + 100);

        // Layout width per session
        double mergedSessionWidth = 280 * _zoomFactor;
        double splitSessionWidth = 580 * _zoomFactor;
        double sessionWidth = _isSplitMode ? splitSessionWidth : mergedSessionWidth;
        double leftMargin = 30;
        double sessionSpacing = 24;

        double totalCanvasWidth = leftMargin + (_sessions.Count * (sessionWidth + sessionSpacing)) + 120;

        ProfileDrawingCanvas.Width = totalCanvasWidth;
        ProfileDrawingCanvas.Height = totalCanvasHeight;
        ProfileContainerGrid.Width = totalCanvasWidth;
        ProfileContainerGrid.Height = totalCanvasHeight;

        // Render sessions
        for (int sIdx = 0; sIdx < _sessions.Count; sIdx++)
        {
            var session = _sessions[sIdx];
            double sessionLeft = leftMargin + (sIdx * (sessionWidth + sessionSpacing));

            RenderSingleSession(session, sessionLeft, sessionWidth, effectiveRowHeight, sIdx == _sessions.Count - 1);
        }

        // Render Virgin POC (VPOC) projection from prior days into today
        if (TogglePoc.IsChecked == true)
        {
            RenderVpocProjections(leftMargin, sessionWidth, sessionSpacing);
        }

        // Render Price Axis
        RenderPriceAxis(effectiveRowHeight);
    }

    private void RenderSingleSession(MarketProfileSession session, double sessionLeft, double sessionWidth, double effectiveRowHeight, bool isToday)
    {
        // 1. Session Header & Date Divider Line
        var dateHeader = new TextBlock
        {
            Text = $"{session.DateDisplay} ({(isToday ? "Today" : "Prev")})",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = isToday ? FiftyChartsPalette.TitleHighlightBrush : FiftyChartsPalette.TitleBrush
        };
        Canvas.SetLeft(dateHeader, sessionLeft);
        Canvas.SetTop(dateHeader, 12);
        ProfileDrawingCanvas.Children.Add(dateHeader);

        // Session right divider line
        var divider = new Line
        {
            X1 = sessionLeft + sessionWidth,
            Y1 = 10,
            X2 = sessionLeft + sessionWidth,
            Y2 = ProfileDrawingCanvas.Height - 10,
            Stroke = FiftyChartsPalette.DividerBrush,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 4 }
        };
        ProfileDrawingCanvas.Children.Add(divider);

        // 2. Value Area Shaded Box (50Charts #122229)
        if (ToggleVa.IsChecked == true)
        {
            double vahY = PriceToY(session.VahPrice);
            double valY = PriceToY(session.ValPrice);
            double vaBoxTop = Math.Min(vahY, valY) - (effectiveRowHeight * 0.5);
            double vaBoxHeight = Math.Abs(valY - vahY) + effectiveRowHeight;

            var vaBox = new Rectangle
            {
                Width = sessionWidth - 10,
                Height = Math.Max(effectiveRowHeight, vaBoxHeight),
                Fill = FiftyChartsPalette.PriceValueAreaBg,
                Opacity = 0.85
            };
            Canvas.SetLeft(vaBox, sessionLeft);
            Canvas.SetTop(vaBox, vaBoxTop);
            ProfileDrawingCanvas.Children.Add(vaBox);

            // VAH Horizontal line
            var vahLine = new Line
            {
                X1 = sessionLeft,
                Y1 = vahY,
                X2 = sessionLeft + sessionWidth - 10,
                Y2 = vahY,
                Stroke = new SolidColorBrush(Color.FromArgb(160, 56, 189, 248)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 2 }
            };
            ProfileDrawingCanvas.Children.Add(vahLine);

            var vahLabel = new TextBlock
            {
                Text = $"VAH: {session.VahPrice:N1}",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248))
            };
            Canvas.SetLeft(vahLabel, sessionLeft + sessionWidth - 70);
            Canvas.SetTop(vahLabel, vahY - 12);
            ProfileDrawingCanvas.Children.Add(vahLabel);

            // VAL Horizontal line
            var valLine = new Line
            {
                X1 = sessionLeft,
                Y1 = valY,
                X2 = sessionLeft + sessionWidth - 10,
                Y2 = valY,
                Stroke = new SolidColorBrush(Color.FromArgb(160, 56, 189, 248)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 2 }
            };
            ProfileDrawingCanvas.Children.Add(valLine);

            var valLabel = new TextBlock
            {
                Text = $"VAL: {session.ValPrice:N1}",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248))
            };
            Canvas.SetLeft(valLabel, sessionLeft + sessionWidth - 70);
            Canvas.SetTop(valLabel, valY + 2);
            ProfileDrawingCanvas.Children.Add(valLabel);
        }

        // 3. Initial Balance (IB) Bracket & Lines
        if (ToggleIb.IsChecked == true)
        {
            double ibHighY = PriceToY(session.IbHigh);
            double ibLowY = PriceToY(session.IbLow);

            // Green bracket on left edge
            var ibBracket = new Line
            {
                X1 = sessionLeft + 4,
                Y1 = ibHighY,
                X2 = sessionLeft + 4,
                Y2 = ibLowY,
                Stroke = FiftyChartsPalette.InitialBalanceBrush,
                StrokeThickness = 3
            };
            ProfileDrawingCanvas.Children.Add(ibBracket);

            // Top bracket serif
            var ibTopSerif = new Line
            {
                X1 = sessionLeft + 2,
                Y1 = ibHighY,
                X2 = sessionLeft + 12,
                Y2 = ibHighY,
                Stroke = FiftyChartsPalette.InitialBalanceBrush,
                StrokeThickness = 2
            };
            ProfileDrawingCanvas.Children.Add(ibTopSerif);

            // Bottom bracket serif
            var ibBottomSerif = new Line
            {
                X1 = sessionLeft + 2,
                Y1 = ibLowY,
                X2 = sessionLeft + 12,
                Y2 = ibLowY,
                Stroke = FiftyChartsPalette.InitialBalanceBrush,
                StrokeThickness = 2
            };
            ProfileDrawingCanvas.Children.Add(ibBottomSerif);

            // IB label
            var ibText = new TextBlock
            {
                Text = $"IB ({session.IbRange:N0} pts)",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = FiftyChartsPalette.InitialBalanceBrush
            };
            Canvas.SetLeft(ibText, sessionLeft + 14);
            Canvas.SetTop(ibText, (ibHighY + ibLowY) / 2 - 6);
            ProfileDrawingCanvas.Children.Add(ibText);
        }

        // 4. Point of Control (POC) Highlight
        if (TogglePoc.IsChecked == true)
        {
            double pocY = PriceToY(session.PocPrice);

            var pocLine = new Line
            {
                X1 = sessionLeft,
                Y1 = pocY,
                X2 = sessionLeft + sessionWidth - 10,
                Y2 = pocY,
                Stroke = FiftyChartsPalette.PricePocBrush,
                StrokeThickness = 2
            };
            ProfileDrawingCanvas.Children.Add(pocLine);

            var pocBadge = new Border
            {
                Background = FiftyChartsPalette.PricePocBrush,
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock
                {
                    Text = $"POC: {session.PocPrice:N1} ({session.PocTpoCount} TPOs)",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Black
                }
            };
            Canvas.SetLeft(pocBadge, sessionLeft + sessionWidth - 130);
            Canvas.SetTop(pocBadge, pocY - 8);
            ProfileDrawingCanvas.Children.Add(pocBadge);
        }

        // 5. Halfback Line (50% Range)
        if (ToggleHalfback.IsChecked == true)
        {
            double hbY = PriceToY(session.Halfback);
            var hbLine = new Line
            {
                X1 = sessionLeft,
                Y1 = hbY,
                X2 = sessionLeft + sessionWidth - 10,
                Y2 = hbY,
                Stroke = FiftyChartsPalette.HalfbackProfileBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 2, 4 }
            };
            ProfileDrawingCanvas.Children.Add(hbLine);

            var hbText = new TextBlock
            {
                Text = $"HB: {session.Halfback:N1}",
                FontSize = 9,
                Foreground = FiftyChartsPalette.HalfbackProfileBrush
            };
            Canvas.SetLeft(hbText, sessionLeft + sessionWidth - 60);
            Canvas.SetTop(hbText, hbY - 12);
            ProfileDrawingCanvas.Children.Add(hbText);
        }

        // 6. Single Prints (SP) Zones
        if (ToggleSp.IsChecked == true)
        {
            foreach (var sp in session.SinglePrints)
            {
                double spHighY = PriceToY(sp.HighPrice);
                double spLowY = PriceToY(sp.LowPrice);
                double topY = Math.Min(spHighY, spLowY);
                double spHeight = Math.Max(effectiveRowHeight, Math.Abs(spLowY - spHighY));

                var spRect = new Rectangle
                {
                    Width = sessionWidth - 20,
                    Height = spHeight,
                    Fill = sp.IsBuy ? FiftyChartsPalette.SinglePrintBuyZone : FiftyChartsPalette.SinglePrintSellZone,
                    Stroke = sp.IsBuy ? FiftyChartsPalette.SinglePrintBuyLine : FiftyChartsPalette.SinglePrintSellLine,
                    StrokeThickness = 1,
                    Opacity = 0.7
                };
                Canvas.SetLeft(spRect, sessionLeft + 8);
                Canvas.SetTop(spRect, topY - (effectiveRowHeight * 0.5));
                ProfileDrawingCanvas.Children.Add(spRect);

                var spLabel = new TextBlock
                {
                    Text = $"SP {(sp.IsBuy ? "BUY" : "SELL")} [{sp.PeriodLetter}]",
                    FontSize = 8,
                    FontWeight = FontWeights.Bold,
                    Foreground = sp.IsBuy ? Brushes.LimeGreen : Brushes.Salmon
                };
                Canvas.SetLeft(spLabel, sessionLeft + sessionWidth - 85);
                Canvas.SetTop(spLabel, topY - 6);
                ProfileDrawingCanvas.Children.Add(spLabel);
            }
        }

        // 7. Poor High / Poor Low Markers
        if (session.IsPoorHigh)
        {
            double highY = PriceToY(session.High);
            var poorHighBadge = new Border
            {
                Background = FiftyChartsPalette.PoorHighLowBrush,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock
                {
                    Text = $"Poor High {session.High:N1}",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                }
            };
            Canvas.SetLeft(poorHighBadge, sessionLeft + 20);
            Canvas.SetTop(poorHighBadge, highY - 16);
            ProfileDrawingCanvas.Children.Add(poorHighBadge);
        }

        if (session.IsPoorLow)
        {
            double lowY = PriceToY(session.Low);
            var poorLowBadge = new Border
            {
                Background = FiftyChartsPalette.PoorHighLowBrush,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock
                {
                    Text = $"Poor Low {session.Low:N1}",
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                }
            };
            Canvas.SetLeft(poorLowBadge, sessionLeft + 20);
            Canvas.SetTop(poorLowBadge, lowY + 4);
            ProfileDrawingCanvas.Children.Add(poorLowBadge);
        }

        // 8. TPO Letter Stamping (Merged vs Split)
        if (_isSplitMode)
        {
            RenderSplitLetters(session, sessionLeft, sessionWidth, effectiveRowHeight);
        }
        else
        {
            RenderMergedLetters(session, sessionLeft, effectiveRowHeight);
        }

        // 9. Volume Profile Overlay
        if (ToggleVp.IsChecked == true)
        {
            RenderVolumeProfileOverlay(session, sessionLeft, sessionWidth, effectiveRowHeight);
        }
    }

    private void RenderMergedLetters(MarketProfileSession session, double sessionLeft, double effectiveRowHeight)
    {
        double letterSpacing = _letterWidth * _zoomFactor;
        double startX = sessionLeft + 22;

        foreach (var bucket in session.Buckets)
        {
            double y = PriceToY(bucket.Price) - (effectiveRowHeight * 0.45);

            for (int i = 0; i < bucket.Tpos.Count; i++)
            {
                var tpo = bucket.Tpos[i];
                var letterBlock = new TextBlock
                {
                    Text = tpo.Letter.ToString(),
                    FontSize = Math.Max(8, 10 * _zoomFactor),
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = tpo.Brush
                };

                Canvas.SetLeft(letterBlock, startX + (i * letterSpacing));
                Canvas.SetTop(letterBlock, y);
                ProfileDrawingCanvas.Children.Add(letterBlock);
            }
        }
    }

    private void RenderSplitLetters(MarketProfileSession session, double sessionLeft, double sessionWidth, double effectiveRowHeight)
    {
        int periodCount = session.Periods.Count;
        if (periodCount == 0) return;

        double colWidth = (sessionWidth - 40) / periodCount;

        for (int pIdx = 0; pIdx < periodCount; pIdx++)
        {
            var pBar = session.Periods[pIdx];
            double colX = sessionLeft + 20 + (pIdx * colWidth);

            // Column Header
            var colHeader = new TextBlock
            {
                Text = pBar.Letter.ToString(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = FiftyChartsPalette.GetLetterBrush(pBar.Letter),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Canvas.SetLeft(colHeader, colX + 4);
            Canvas.SetTop(colHeader, 28);
            ProfileDrawingCanvas.Children.Add(colHeader);

            // Period vertical range line
            double highY = PriceToY(pBar.High);
            double lowY = PriceToY(pBar.Low);
            var rangeLine = new Line
            {
                X1 = colX + 8,
                Y1 = highY,
                X2 = colX + 8,
                Y2 = lowY,
                Stroke = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                StrokeThickness = 1
            };
            ProfileDrawingCanvas.Children.Add(rangeLine);

            // Letter stamps
            foreach (var price in pBar.PriceLevels)
            {
                double y = PriceToY(price) - (effectiveRowHeight * 0.45);
                var tb = new TextBlock
                {
                    Text = pBar.Letter.ToString(),
                    FontSize = Math.Max(8, 9 * _zoomFactor),
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = FiftyChartsPalette.GetLetterBrush(pBar.Letter)
                };
                Canvas.SetLeft(tb, colX + 4);
                Canvas.SetTop(tb, y);
                ProfileDrawingCanvas.Children.Add(tb);
            }
        }
    }

    private void RenderVolumeProfileOverlay(MarketProfileSession session, double sessionLeft, double sessionWidth, double effectiveRowHeight)
    {
        long maxVol = session.Buckets.Max(b => b.Volume);
        if (maxVol <= 0) return;

        double maxBarWidth = 60 * _zoomFactor;

        foreach (var bucket in session.Buckets)
        {
            double barWidth = (double)bucket.Volume / maxVol * maxBarWidth;
            double y = PriceToY(bucket.Price);

            var vpRect = new Rectangle
            {
                Width = Math.Max(2, barWidth),
                Height = Math.Max(2, effectiveRowHeight * 0.6),
                Fill = bucket.IsPoc ? FiftyChartsPalette.VolumePocBrush : FiftyChartsPalette.VisibleVolumeBrush,
                Opacity = 0.4
            };
            // Render on the right boundary of the session
            Canvas.SetLeft(vpRect, sessionLeft + sessionWidth - barWidth - 14);
            Canvas.SetTop(vpRect, y - (effectiveRowHeight * 0.3));
            ProfileDrawingCanvas.Children.Add(vpRect);
        }
    }

    private void RenderVpocProjections(double leftMargin, double sessionWidth, double sessionSpacing)
    {
        if (_sessions.Count < 2) return;

        for (int i = 0; i < _sessions.Count - 1; i++)
        {
            var prior = _sessions[i];
            var next = _sessions[i + 1];

            // If next day did not hit prior POC, project ray
            if (next.High < prior.PocPrice || next.Low > prior.PocPrice)
            {
                double priorLeft = leftMargin + (i * (sessionWidth + sessionSpacing));
                double nextRight = leftMargin + ((i + 1) * (sessionWidth + sessionSpacing)) + sessionWidth;
                double pocY = PriceToY(prior.PocPrice);

                var vpocLine = new Line
                {
                    X1 = priorLeft + sessionWidth * 0.5,
                    Y1 = pocY,
                    X2 = nextRight - 10,
                    Y2 = pocY,
                    Stroke = FiftyChartsPalette.PriceVpocBrush,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 5, 3 }
                };
                ProfileDrawingCanvas.Children.Add(vpocLine);

                var vpocLabel = new Border
                {
                    Background = FiftyChartsPalette.PriceVpocBrush,
                    CornerRadius = new CornerRadius(2),
                    Padding = new Thickness(3, 1, 3, 1),
                    Child = new TextBlock
                    {
                        Text = $"VPOC: {prior.PocPrice:N1}",
                        FontSize = 8,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.Black
                    }
                };
                Canvas.SetLeft(vpocLabel, nextRight - 80);
                Canvas.SetTop(vpocLabel, pocY - 14);
                ProfileDrawingCanvas.Children.Add(vpocLabel);
            }
        }
    }

    private void RenderPriceAxis(double effectiveRowHeight)
    {
        if (_sessions.Count == 0) return;
        var today = _sessions.Last();
        double tpoSize = today.TpoSize;

        PriceAxisCanvas.Children.Clear();
        PriceAxisCanvas.Height = ProfileDrawingCanvas.Height;

        double step = tpoSize * 5; // Major ticks every 5 TPO increments
        if (step < 1.0) step = 1.0;

        double startPrice = Math.Floor(_globalMinPrice / step) * step;

        for (double price = startPrice; price <= _globalMaxPrice; price += step)
        {
            double y = PriceToY(price);
            if (y < 20 || y > PriceAxisCanvas.Height - 20) continue;

            // Tick mark
            var tick = new Line
            {
                X1 = 0,
                Y1 = y,
                X2 = 6,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                StrokeThickness = 1
            };
            PriceAxisCanvas.Children.Add(tick);

            // Price text
            var tb = new TextBlock
            {
                Text = price.ToString("N1"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontFamily = new FontFamily("Consolas")
            };
            Canvas.SetLeft(tb, 10);
            Canvas.SetTop(tb, y - 6);
            PriceAxisCanvas.Children.Add(tb);
        }

        // Highlight active LTP on price axis
        double ltpY = PriceToY(today.Close);
        var ltpBadge = new Border
        {
            Background = FiftyChartsPalette.YAxisLtpBrush,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Child = new TextBlock
            {
                Text = today.Close.ToString("N1"),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas")
            }
        };
        Canvas.SetLeft(ltpBadge, 4);
        Canvas.SetTop(ltpBadge, ltpY - 9);
        PriceAxisCanvas.Children.Add(ltpBadge);

        // Highlight POC on price axis
        double pocY = PriceToY(today.PocPrice);
        var pocAxisBadge = new Border
        {
            Background = FiftyChartsPalette.PricePocBrush,
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(3, 1, 3, 1),
            Child = new TextBlock
            {
                Text = today.PocPrice.ToString("N1"),
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                FontFamily = new FontFamily("Consolas")
            }
        };
        Canvas.SetLeft(pocAxisBadge, 4);
        Canvas.SetTop(pocAxisBadge, pocY - 7);
        PriceAxisCanvas.Children.Add(pocAxisBadge);
    }

    #region Interaction & Controls

    private void SymbolCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isViewLoaded) return;
        if (SymbolCombo.SelectedItem is ComboBoxItem item)
        {
            string raw = item.Content?.ToString() ?? "NIFTY_I";
            string clean = raw.Split(' ')[0].Trim();
            _currentSymbol = clean;
            LoadDataForSymbol(_currentSymbol);
        }
    }

    private void LiveStreamBadge_Click(object sender, MouseButtonEventArgs e)
    {
        bool newState = !AccelPixDataProvider.Instance.IsLiveStreaming;
        AccelPixDataProvider.Instance.ToggleStreaming(newState);

        if (newState)
        {
            LiveStreamBadge.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
            LiveStreamBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(5, 150, 105));
            LiveStreamDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            LiveLtpText.Text = $"LTP: {AccelPixDataProvider.Instance.CurrentLtp:N2}";
            LiveLtpText.Foreground = new SolidColorBrush(Color.FromRgb(209, 250, 229));
        }
        else
        {
            LiveStreamBadge.Background = new SolidColorBrush(Color.FromRgb(62, 24, 24));
            LiveStreamBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(153, 27, 27));
            LiveStreamDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            LiveLtpText.Text = "PAUSED";
            LiveLtpText.Foreground = new SolidColorBrush(Color.FromRgb(254, 202, 202));
        }
    }

    private void StreamSpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isViewLoaded || StreamSpeedCombo?.SelectedItem is not ComboBoxItem item) return;
        string text = item.Content?.ToString() ?? "1x";
        int mult = int.TryParse(text.Replace("x", "").Trim(), out int m) ? m : 1;
        AccelPixDataProvider.Instance.SetStreamSpeed(mult);
    }

    private void LaunchPixBtn_Click(object sender, RoutedEventArgs e)
    {
        AccelPixDataProvider.Instance.LaunchPixConnect();
        MessageBox.Show(
            "AccelPix PixConnect bridge invoked.\nData provider connected to:\nC:\\Program Files (x86)\\Accelpix\\Pix Connect", 
            "AccelPix Engine", 
            MessageBoxButton.OK, 
            MessageBoxImage.Information);
    }

    private void TpoTfCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isViewLoaded) return;
        if (TpoTfCombo.SelectedItem is ComboBoxItem item)
        {
            // Recompute or reload with selected timeframe
            LoadDataForSymbol(_currentSymbol);
        }
    }

    private void ProfileMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isViewLoaded || SplitRadio == null) return;
        _isSplitMode = SplitRadio.IsChecked == true;
        RenderProfileChart();
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (!_isViewLoaded) return;
        RenderProfileChart();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _zoomFactor = Math.Min(2.5, _zoomFactor * 1.15);
        RenderProfileChart();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _zoomFactor = Math.Max(0.5, _zoomFactor / 1.15);
        RenderProfileChart();
    }

    private void ResetZoom_Click(object sender, RoutedEventArgs e)
    {
        _zoomFactor = 1.0;
        RenderProfileChart();
    }

    private void RefreshData_Click(object sender, RoutedEventArgs e)
    {
        LoadDataForSymbol(_currentSymbol);
    }

    private void ExportRefLines_Click(object sender, RoutedEventArgs e)
    {
        if (_sessions.Count == 0) return;
        var today = _sessions.Last();
        string export = $"[50Charts MP Export]\nSymbol: {today.Symbol}\nDate: {today.DateDisplay}\nPOC: {today.PocPrice:N2}\nVAH: {today.VahPrice:N2}\nVAL: {today.ValPrice:N2}\nIB High: {today.IbHigh:N2}\nIB Low: {today.IbLow:N2}\nIBR: {today.IbRange:N1}\nOpen Type: {today.OpenType}\nRotational Factor: {today.RotationalFactor}";
        Clipboard.SetText(export);
        MessageBox.Show($"50Charts Reference Lines copied to clipboard!\n\n{export}", "50Charts Export", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ChartScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Delta > 0)
                _zoomFactor = Math.Min(2.5, _zoomFactor * 1.1);
            else
                _zoomFactor = Math.Max(0.5, _zoomFactor / 1.1);

            RenderProfileChart();
            e.Handled = true;
        }
    }

    private void ProfileContainerGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isPanning = true;
            _lastPanPoint = e.GetPosition(ChartScrollViewer);
            ProfileContainerGrid.CaptureMouse();
        }
    }

    private void ProfileContainerGrid_MouseMove(object sender, MouseEventArgs e)
    {
        Point currentPoint = e.GetPosition(ProfileContainerGrid);

        // Hover inspection
        double hoveredPrice = YToPrice(currentPoint.Y);
        InspectHoverLevel(currentPoint, hoveredPrice);

        // Pan handling
        if (_isPanning)
        {
            Point currentPan = e.GetPosition(ChartScrollViewer);
            double deltaX = currentPan.X - _lastPanPoint.X;
            double deltaY = currentPan.Y - _lastPanPoint.Y;

            ChartScrollViewer.ScrollToHorizontalOffset(ChartScrollViewer.HorizontalOffset - deltaX);
            ChartScrollViewer.ScrollToVerticalOffset(ChartScrollViewer.VerticalOffset - deltaY);

            _lastPanPoint = currentPan;
        }
    }

    private void ProfileContainerGrid_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            ProfileContainerGrid.ReleaseMouseCapture();
        }
    }

    private void InspectHoverLevel(Point canvasPoint, double price)
    {
        if (_sessions.Count == 0) return;
        var today = _sessions.Last();
        double tpoSize = today.TpoSize;
        double roundedPrice = Math.Round(price / tpoSize) * tpoSize;

        var matchingBucket = today.Buckets.FirstOrDefault(b => Math.Abs(b.Price - roundedPrice) < tpoSize * 0.6);
        if (matchingBucket != null)
        {
            HoverTooltip.Visibility = Visibility.Visible;
            Canvas.SetLeft(HoverTooltip, canvasPoint.X + 16);
            Canvas.SetTop(HoverTooltip, canvasPoint.Y - 20);

            HoverPriceText.Text = $"Price: {matchingBucket.Price:N2}";
            HoverTpoCountText.Text = $"TPO Count: {matchingBucket.TpoCount}";
            HoverPeriodsText.Text = $"Periods: {string.Join(" ", matchingBucket.Tpos.Select(t => t.Letter))}";
            HoverVolumeText.Text = $"Volume: {matchingBucket.Volume:N0} (Buy: {matchingBucket.BuyVolume:N0} | Sell: {matchingBucket.SellVolume:N0})";

            string status = matchingBucket.IsPoc ? "POC (Point of Control)" : (matchingBucket.IsValueArea ? "Value Area (70%)" : "Non-VA");
            if (matchingBucket.IsSinglePrint) status += " | Single Print (SP)";
            if (matchingBucket.IsPoorLevel) status += " | Poor Extreme";
            HoverStatusText.Text = $"Status: {status}";
            HoverStatusText.Foreground = matchingBucket.IsPoc ? Brushes.Yellow : (matchingBucket.IsValueArea ? Brushes.LightSkyBlue : Brushes.Gray);
        }
        else
        {
            HoverTooltip.Visibility = Visibility.Collapsed;
        }
    }

    #endregion
}
