using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WindowsApp.Ninja.Models;
using WindowsApp.Ninja.OrderFlow.Models;
using WindowsApp.Ninja.OrderFlow.Services;
using WindowsApp.Ninja.Services;

namespace WindowsApp.Ninja.OrderFlow.Views;

public partial class NinjaOrderFlowView : UserControl
{
    private readonly OrderFlowDataEngine _engine;
    private readonly NinjaFuturesRegistry _registry;

    // Zoom & Scaling
    private double _zoomFactor = 1.35;
    private readonly double _baseBarWidth = 136.0;
    private readonly double _baseLevelHeight = 22.0;

    // Price Bounds
    private double _minPrice = 4094.0;
    private double _maxPrice = 4111.0;

    // Pan / Drag State
    private bool _isPanning = false;
    private Point _panStartPoint;
    private double _panStartHOffset;
    private double _panStartVOffset;

    // Price Axis Drag Scaling State
    private bool _isDraggingAxis = false;
    private Point _axisDragStartPoint;
    private double _axisDragStartZoom;

    // Feature & Indicator Toggles
    private bool _showZones = true;
    private bool _showPocLine = true;
    private bool _showPatterns = true;
    private bool _showVwap = true;
    private bool _showProfile = true;
    private bool _showCvd = true;
    private bool _showCandleLine = true;
    private bool _showAbsorption = true;
    private bool _showBarDelta = true;
    private bool _isInitialized = false;

    // Chart Settings
    private ChartType _chartType = ChartType.Footprint;
    private string _activeTimeframe = "5 Min";
    private DrawingToolType _activeDrawingTool = DrawingToolType.Cursor;

    // Drawing Interaction
    private Point? _drawingStartPoint = null;
    private Shape? _drawingPreviewShape = null;

    // Crosshair Elements
    private Line? _crosshairH;
    private Line? _crosshairV;
    private Border? _priceChip;
    private TextBlock? _priceChipText;
    private Border? _timeChip;
    private TextBlock? _timeChipText;

    // Palette Colors matching NinjaTrader 8 MZpack 3.14.21
    private readonly Brush _chartBg = new SolidColorBrush(Color.FromRgb(14, 14, 16));
    private readonly Brush _gridLine = new SolidColorBrush(Color.FromRgb(30, 30, 38));
    private readonly Brush _supplyZoneBrush = new SolidColorBrush(Color.FromArgb(85, 185, 28, 28)); // Crimson Red
    private readonly Brush _demandZoneBrush = new SolidColorBrush(Color.FromArgb(85, 21, 128, 61));  // Forest Green
    private readonly Brush _textWhite = new SolidColorBrush(Color.FromRgb(244, 244, 245));
    private readonly Brush _textMuted = new SolidColorBrush(Color.FromRgb(161, 161, 170));
    private readonly Brush _pocBorderBrush = new SolidColorBrush(Color.FromRgb(255, 255, 0)); // Pure Yellow #FFFF00 matching NT8 mzFootprint BarPOCStroke
    private readonly Brush _pocBgBrush = new SolidColorBrush(Color.FromArgb(90, 180, 160, 0)); // Warm golden POC tint
    private readonly Brush _buyImbalanceBrush = new SolidColorBrush(Color.FromRgb(0, 255, 0)); // #00FF00 Lime Green NT8 Default
    private readonly Brush _sellImbalanceBrush = new SolidColorBrush(Color.FromRgb(255, 20, 147)); // #FF1493 DeepPink NT8 Default
    private readonly Brush _buyImbalanceBg = new SolidColorBrush(Color.FromArgb(100, 0, 180, 0));
    private readonly Brush _sellImbalanceBg = new SolidColorBrush(Color.FromArgb(100, 180, 20, 80));
    private readonly Brush _absorptionBadgeBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Amber
    private readonly Brush _vwapBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Golden Amber
    private readonly Brush _vwapBandBrush = new SolidColorBrush(Color.FromArgb(160, 56, 189, 248)); // Sky Blue
    private readonly Brush _cvdBrush = new SolidColorBrush(Color.FromRgb(168, 85, 247)); // Purple
    private readonly Brush _profileBarBrush = new SolidColorBrush(Color.FromArgb(70, 56, 189, 248));
    private readonly Brush _profilePocBrush = new SolidColorBrush(Color.FromArgb(140, 16, 185, 129));
    private readonly Brush _developingPocBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Developing POC Amber

    public NinjaOrderFlowView()
    {
        InitializeComponent();

        _registry = new NinjaFuturesRegistry();
        var defaultContract = _registry.GetBySymbol("NIFTY_I") ?? _registry.GetBySymbol("GC") ?? new FuturesContract
        {
            Symbol = "NIFTY_I",
            Name = "Nifty 50 Continuous Futures",
            Exchange = "NSE_FUT",
            Category = "Indices",
            TickSize = 0.05,
            PointValue = 50.0,
            CurrentPrice = 24374.5,
            FrontMonth = "OCT-26"
        };

        _engine = new OrderFlowDataEngine(defaultContract);
        _engine.CurrentTimeframe = "700 Range";
        _engine.BarUpdated += OnBarUpdated;

        Loaded += NinjaOrderFlowView_Loaded;
        SizeChanged += (s, e) => RedrawAll();

        // Connect scroll synchronization
        ChartScrollViewer.ScrollChanged += (s, e) =>
        {
            if (RibbonScrollViewer != null && ChartScrollViewer != null)
            {
                RibbonScrollViewer.ScrollToHorizontalOffset(ChartScrollViewer.HorizontalOffset);
            }
            if (PriceAxisScrollViewer != null && ChartScrollViewer != null)
            {
                PriceAxisScrollViewer.ScrollToVerticalOffset(ChartScrollViewer.VerticalOffset);
            }
        };

        InitializeCrosshairLayers();

        _isInitialized = true;
    }

    private void InitializeCrosshairLayers()
    {
        _crosshairH = new Line
        {
            Stroke = new SolidColorBrush(Color.FromArgb(180, 148, 163, 184)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            Visibility = Visibility.Collapsed
        };
        _crosshairV = new Line
        {
            Stroke = new SolidColorBrush(Color.FromArgb(180, 148, 163, 184)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 3 },
            Visibility = Visibility.Collapsed
        };
        CrosshairCanvas.Children.Add(_crosshairH);
        CrosshairCanvas.Children.Add(_crosshairV);

        _priceChipText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _priceChip = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2, 4, 2),
            Child = _priceChipText,
            Visibility = Visibility.Collapsed
        };
        PriceAxisCanvas.Children.Add(_priceChip);

        _timeChipText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _timeChip = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6, 2, 6, 2),
            Child = _timeChipText,
            Visibility = Visibility.Collapsed
        };
        RibbonCanvas.Children.Add(_timeChip);
    }

    private void NinjaOrderFlowView_Loaded(object sender, RoutedEventArgs e)
    {
        HighlightActiveDrawingTool();
        PopulateSymbolSearchResults();
        if (SymbolTitleText != null)
        {
            SymbolTitleText.Text = $"{_engine.ActiveContract.Symbol} {_engine.ActiveContract.FrontMonth} ({_activeTimeframe})";
        }
        UpdateTabSelection(_engine.ActiveContract.Symbol);
        RedrawAll();
        Dispatcher.BeginInvoke(new Action(CenterOnCurrentPrice), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void CenterOnCurrentPrice()
    {
        if (_engine.CurrentBar == null || ChartScrollViewer == null) return;
        double targetY = PriceToY(_engine.CurrentBar.Close);
        double vOffset = Math.Max(0, targetY - (ChartScrollViewer.ActualHeight / 2));
        ChartScrollViewer.ScrollToVerticalOffset(vOffset);
        ChartScrollViewer.ScrollToRightEnd();
    }

    private void OnBarUpdated()
    {
        Dispatcher.Invoke(() =>
        {
            if (_engine.CurrentBar != null && LivePriceIndicator != null)
            {
                double tick = _engine.ActiveContract.TickSize > 0.0001 ? _engine.ActiveContract.TickSize : 0.05;
                LivePriceIndicator.Text = $"Last: {_engine.ActiveContract.FormatPrice(_engine.CurrentBar.Close)}  Ask: {_engine.ActiveContract.FormatPrice(_engine.CurrentBar.Close + tick)}  Bid: {_engine.ActiveContract.FormatPrice(_engine.CurrentBar.Close - tick)}  Delta: {_engine.CurrentBar.Delta:+0;-0;0}";
            }
            RedrawAll();
        });
    }

    public void RedrawAll()
    {
        if (!_isInitialized || FootprintCanvas == null || RibbonCanvas == null || PriceAxisCanvas == null || _engine == null) return;
        UpdatePriceBounds();
        RenderFootprintCanvas();
        RenderRibbonCanvas();
        RenderPriceAxis();
    }

    private void UpdatePriceBounds()
    {
        if (_engine.Bars.Count > 0)
        {
            double min = _engine.Bars.Min(b => b.Low);
            double max = _engine.Bars.Max(b => b.High);
            double step = _engine.PriceStep;
            double pad = step * 4.0;
            _minPrice = min - pad;
            _maxPrice = max + pad;
        }
        else
        {
            _minPrice = 24350.0;
            _maxPrice = 24395.0;
        }
    }

    private double PriceToY(double price)
    {
        double levelHeight = _baseLevelHeight * _zoomFactor;
        double step = _engine.PriceStep;
        if (step <= 0.0001) step = 1.0;
        double levelIndex = (_maxPrice - price) / step;
        return (levelIndex * levelHeight) + 30.0;
    }

    private double YToPrice(double y)
    {
        double levelHeight = _baseLevelHeight * _zoomFactor;
        double step = _engine.PriceStep;
        if (step <= 0.0001) step = 1.0;
        double levelIndex = (y - 30.0) / levelHeight;
        return _maxPrice - (levelIndex * step);
    }

    // --- Chart Canvas Rendering (Footprint, Candlestick, OHLC, Profile, Overlays) ---
    private void RenderFootprintCanvas()
    {
        FootprintCanvas.Children.Clear();

        double barWidth = _baseBarWidth * _zoomFactor;
        double levelHeight = _baseLevelHeight * _zoomFactor;
        double barSpacing = 12.0 * _zoomFactor;
        double barStride = barWidth + barSpacing;

        double requiredWidth = Math.Max(2400, (_engine.Bars.Count * barStride) + 300);
        double step = _engine.PriceStep;
        if (step <= 0.0001) step = 1.0;
        double totalLevels = (_maxPrice - _minPrice) / step;
        double requiredHeight = Math.Max(500, (totalLevels * levelHeight) + 120);

        FootprintCanvas.Width = requiredWidth;
        FootprintCanvas.Height = requiredHeight;
        if (ChartContainerGrid != null)
        {
            ChartContainerGrid.Width = requiredWidth;
            ChartContainerGrid.Height = requiredHeight;
        }
        if (DrawingsCanvas != null)
        {
            DrawingsCanvas.Width = requiredWidth;
            DrawingsCanvas.Height = requiredHeight;
        }
        if (CrosshairCanvas != null)
        {
            CrosshairCanvas.Width = requiredWidth;
            CrosshairCanvas.Height = requiredHeight;
        }

        // Draw Horizontal Price Grid Lines
        for (double p = Math.Ceiling(_maxPrice / step) * step; p >= _minPrice - (step * 0.01); p -= step)
        {
            double y = PriceToY(p);
            var line = new Line
            {
                X1 = 0,
                X2 = requiredWidth,
                Y1 = y,
                Y2 = y,
                Stroke = _gridLine,
                StrokeThickness = 0.6
            };
            FootprintCanvas.Children.Add(line);
        }

        // Render Supply & Demand Zones
        if (_showZones)
        {
            foreach (var zone in _engine.Zones)
            {
                double zTop = PriceToY(zone.PriceHigh);
                double zBottom = PriceToY(zone.PriceLow);
                double zHeight = Math.Max(levelHeight, zBottom - zTop);

                double zLeft = (zone.StartBarIndex * barStride) + 15;
                double zRight = (((zone.EndBarIndex ?? zone.StartBarIndex) + 1) * barStride) + 15;
                double zWidth = Math.Max(barStride, zRight - zLeft);

                var zoneRect = new Rectangle
                {
                    Width = zWidth,
                    Height = zHeight,
                    Fill = zone.Type == OrderFlowZoneType.Supply ? _supplyZoneBrush : _demandZoneBrush,
                    Stroke = zone.Type == OrderFlowZoneType.Supply ? new SolidColorBrush(Color.FromRgb(220, 38, 38)) : new SolidColorBrush(Color.FromRgb(22, 163, 74)),
                    StrokeThickness = 1,
                    RadiusX = 2,
                    RadiusY = 2
                };
                Canvas.SetLeft(zoneRect, zLeft);
                Canvas.SetTop(zoneRect, zTop);
                FootprintCanvas.Children.Add(zoneRect);

                var zoneLabel = new TextBlock
                {
                    Text = $"{(zone.Type == OrderFlowZoneType.Supply ? "SUPPLY" : "DEMAND")} ({zone.Volume})",
                    Foreground = zone.Type == OrderFlowZoneType.Supply ? new SolidColorBrush(Color.FromRgb(254, 202, 202)) : new SolidColorBrush(Color.FromRgb(187, 247, 208)),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold
                };
                Canvas.SetLeft(zoneLabel, zLeft + 6);
                Canvas.SetTop(zoneLabel, zTop + 2);
                FootprintCanvas.Children.Add(zoneLabel);
            }
        }

        // Render Candles according to selected ChartType
        for (int i = 0; i < _engine.Bars.Count; i++)
        {
            var bar = _engine.Bars[i];
            double barLeft = (i * barStride) + 20;
            double centerX = barLeft + (barWidth / 2);

            double yHigh = PriceToY(bar.High);
            double yLow = PriceToY(bar.Low) + levelHeight;
            double yOpen = PriceToY(bar.Open);
            double yClose = PriceToY(bar.Close);
            bool isBullish = bar.Close >= bar.Open;

            // For Non-Footprint styles (Candlestick, OHLC), draw background wick
            if (_chartType != ChartType.Footprint)
            {
                var wick = new Line
                {
                    X1 = centerX,
                    X2 = centerX,
                    Y1 = yHigh,
                    Y2 = yLow,
                    Stroke = new SolidColorBrush(Color.FromRgb(140, 140, 150)),
                    StrokeThickness = 1.5
                };
                FootprintCanvas.Children.Add(wick);
            }

            if (_chartType == ChartType.Footprint)
            {
                RenderFootprintBar(bar, barLeft, centerX, barWidth, levelHeight, yHigh, yLow, yOpen, yClose, isBullish);
            }
            else if (_chartType == ChartType.Candlestick)
            {
                RenderJapaneseCandleBar(bar, barLeft, centerX, barWidth, levelHeight, yHigh, yLow, yOpen, yClose, isBullish);
            }
            else if (_chartType == ChartType.OhlcBars)
            {
                RenderOhlcBar(bar, barLeft, centerX, barWidth, levelHeight, yHigh, yLow, yOpen, yClose, isBullish);
            }
            else if (_chartType == ChartType.VolumeProfile)
            {
                RenderVolumeProfileBar(bar, barLeft, centerX, barWidth, levelHeight, yHigh, yLow, yOpen, yClose, isBullish);
            }
            else if (_chartType == ChartType.MountainArea)
            {
                // Mountain area rendered as smooth connected line across all bars
            }

            // Developing POC Line
            if (_showPocLine && bar.PocPrice > 0)
            {
                double pocY = PriceToY(bar.PocPrice) + (levelHeight / 2);
                var pocDash = new Line
                {
                    X1 = barLeft,
                    X2 = barLeft + barWidth,
                    Y1 = pocY,
                    Y2 = pocY,
                    Stroke = _developingPocBrush,
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                FootprintCanvas.Children.Add(pocDash);
            }
        }

        // Overlays: Mountain Line, VWAP, Session Volume Profile, CVD
        if (_chartType == ChartType.MountainArea)
        {
            RenderMountainCurve(barStride, barWidth, levelHeight);
        }

        if (_showVwap)
        {
            RenderVwapAndBands(barStride, barWidth);
        }

        if (_showProfile)
        {
            RenderSessionVolumeProfile(requiredWidth, levelHeight);
        }

        if (_showCvd)
        {
            RenderCvdCurve(barStride, barWidth);
        }
    }

    private void RenderFootprintBar(OrderFlowBar bar, double barLeft, double centerX, double barWidth, double levelHeight, double yHigh, double yLow, double yOpen, double yClose, bool isBullish)
    {
        // 1. Footprint Column Outer Bounding Frame (Clean contiguous rectangular block)
        double colTop = yHigh;
        double colHeight = Math.Max(levelHeight, yLow - yHigh);
        var colFrame = new Border
        {
            Width = barWidth,
            Height = colHeight,
            Background = new SolidColorBrush(Color.FromArgb(160, 16, 16, 20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 63, 63, 70)),
            BorderThickness = new Thickness(0.8),
            CornerRadius = new CornerRadius(2)
        };
        Canvas.SetLeft(colFrame, barLeft);
        Canvas.SetTop(colFrame, colTop);
        FootprintCanvas.Children.Add(colFrame);

        // 2. Candlestick Straight Line Wick & Side Bar (Clean, uniform MZpack style)
        if (_showCandleLine)
        {
            // Vertical Candle Wick (Center High to Low straight line)
            var candleWick = new Line
            {
                X1 = centerX,
                X2 = centerX,
                Y1 = yHigh,
                Y2 = yLow,
                Stroke = new SolidColorBrush(Color.FromRgb(113, 113, 122)),
                StrokeThickness = 1.2
            };
            FootprintCanvas.Children.Add(candleWick);

            // Left Mini-Candle Bar (Draws authentic candlestick body and wick alongside the footprint)
            double sideX = barLeft - 6;
            var sideWick = new Line
            {
                X1 = sideX,
                X2 = sideX,
                Y1 = yHigh,
                Y2 = yLow,
                Stroke = new SolidColorBrush(Color.FromRgb(140, 140, 150)),
                StrokeThickness = 1.4
            };
            FootprintCanvas.Children.Add(sideWick);

            double spineTop = Math.Min(yOpen, yClose);
            double spineBottom = Math.Max(yOpen, yClose) + levelHeight;
            var sideBody = new Rectangle
            {
                Width = 4.5,
                Height = Math.Max(4.0, spineBottom - spineTop),
                Fill = isBullish ? _buyImbalanceBrush : _sellImbalanceBrush,
                Stroke = isBullish ? new SolidColorBrush(Color.FromRgb(22, 163, 74)) : new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                StrokeThickness = 0.8,
                RadiusX = 1,
                RadiusY = 1
            };
            Canvas.SetLeft(sideBody, sideX - 2.25);
            Canvas.SetTop(sideBody, spineTop);
            FootprintCanvas.Children.Add(sideBody);

            // Candle Body Spine at center
            var spineLine = new Line
            {
                X1 = centerX,
                X2 = centerX,
                Y1 = spineTop,
                Y2 = spineBottom,
                Stroke = isBullish ? _buyImbalanceBrush : _sellImbalanceBrush,
                StrokeThickness = 2.2
            };
            FootprintCanvas.Children.Add(spineLine);
        }

        // 3. Footprint Price Level Rows (Contiguous, straight rectangular clusters)
        var sortedLevels = bar.Levels.OrderByDescending(l => l.Price).ToList();
        foreach (var lvl in sortedLevels)
        {
            double y = PriceToY(lvl.Price);
            var rowBorder = new Border
            {
                Width = barWidth,
                Height = Math.Max(12.0, levelHeight - 1.0),
                CornerRadius = new CornerRadius(lvl.IsPoc ? 3 : 1)
            };

            if (lvl.IsPoc && _showPocLine)
            {
                rowBorder.BorderBrush = _pocBorderBrush; // #FFFF00 Yellow
                rowBorder.BorderThickness = new Thickness(2);
                rowBorder.Background = _pocBgBrush;
            }
            else if (lvl.IsBuyImbalance)
            {
                rowBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 34, 197, 94));
                rowBorder.BorderThickness = new Thickness(0.8);
                rowBorder.Background = _buyImbalanceBg;
            }
            else if (lvl.IsSellImbalance)
            {
                rowBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 239, 68, 68));
                rowBorder.BorderThickness = new Thickness(0.8);
                rowBorder.Background = _sellImbalanceBg;
            }
            else
            {
                rowBorder.Background = new SolidColorBrush(Color.FromArgb(190, 20, 20, 24));
                rowBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(35, 39, 39, 42));
                rowBorder.BorderThickness = new Thickness(0.5);
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            string mode = (ModeCombo?.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Bid x Ask";
            double fontSize = Math.Clamp(10.0 * _zoomFactor, 9.0, 15.0);

            if (mode == "Delta")
            {
                long delta = lvl.Delta;
                var deltaText = new TextBlock
                {
                    Text = delta.ToString(),
                    Foreground = delta >= 0 ? _buyImbalanceBrush : _sellImbalanceBrush,
                    FontSize = fontSize,
                    FontWeight = lvl.IsPoc ? FontWeights.Bold : FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumnSpan(deltaText, 2);
                grid.Children.Add(deltaText);
            }
            else if (mode == "Volume")
            {
                var volText = new TextBlock
                {
                    Text = lvl.TotalVolume.ToString(),
                    Foreground = _textWhite,
                    FontSize = fontSize,
                    FontWeight = lvl.IsPoc ? FontWeights.Bold : FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumnSpan(volText, 2);
                grid.Children.Add(volText);
            }
            else
            {
                // Bid x Ask: numbers aligned uniformly with balanced margins so no text is ever clipped
                var bidText = new TextBlock
                {
                    Text = lvl.BidVolume.ToString(),
                    Foreground = lvl.IsSellImbalance ? _sellImbalanceBrush : _textWhite,
                    FontSize = fontSize,
                    FontWeight = (lvl.IsSellImbalance || lvl.IsPoc) ? FontWeights.Bold : FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 8, 0)
                };
                Grid.SetColumn(bidText, 0);
                grid.Children.Add(bidText);

                var askText = new TextBlock
                {
                    Text = lvl.AskVolume.ToString(),
                    Foreground = lvl.IsBuyImbalance ? _buyImbalanceBrush : _textWhite,
                    FontSize = fontSize,
                    FontWeight = (lvl.IsBuyImbalance || lvl.IsPoc) ? FontWeights.Bold : FontWeights.Normal,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 4, 0)
                };
                Grid.SetColumn(askText, 1);
                grid.Children.Add(askText);
            }

            rowBorder.Child = grid;
            Canvas.SetLeft(rowBorder, barLeft);
            Canvas.SetTop(rowBorder, y);
            FootprintCanvas.Children.Add(rowBorder);
        }

        // 4. Straight Center Vertical Divider Line (Divides Bid from Ask cleanly)
        var centerDivider = new Line
        {
            X1 = centerX,
            X2 = centerX,
            Y1 = yHigh,
            Y2 = yLow,
            Stroke = new SolidColorBrush(Color.FromArgb(120, 82, 82, 91)),
            StrokeThickness = 1.0
        };
        FootprintCanvas.Children.Add(centerDivider);

        // Candlestick Open/Close horizontal ticks drawn over center divider for crisp visibility
        if (_showCandleLine)
        {
            var openTick = new Line
            {
                X1 = centerX - 8,
                X2 = centerX,
                Y1 = yOpen + (levelHeight / 2),
                Y2 = yOpen + (levelHeight / 2),
                Stroke = isBullish ? _buyImbalanceBrush : _sellImbalanceBrush,
                StrokeThickness = 2.0
            };
            FootprintCanvas.Children.Add(openTick);

            var closeTick = new Line
            {
                X1 = centerX,
                X2 = centerX + 8,
                Y1 = yClose + (levelHeight / 2),
                Y2 = yClose + (levelHeight / 2),
                Stroke = isBullish ? _buyImbalanceBrush : _sellImbalanceBrush,
                StrokeThickness = 2.0
            };
            FootprintCanvas.Children.Add(closeTick);
        }

        // 5. Candle Delta Badge (Above or Below, strictly outside footprint cells)
        if (_showBarDelta)
        {
            var deltaTb = new TextBlock
            {
                Text = bar.Delta.ToString(),
                Foreground = bar.Delta >= 0 ? _buyImbalanceBrush : _sellImbalanceBrush,
                FontWeight = FontWeights.Bold,
                FontSize = Math.Clamp(12.5 * _zoomFactor * 0.85, 11.0, 18.0)
            };
            deltaTb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(deltaTb, barLeft + ((barWidth - deltaTb.DesiredSize.Width) / 2));

            double topBound = sortedLevels.Count > 0 ? PriceToY(sortedLevels.First().Price) : yHigh;
            double botBound = sortedLevels.Count > 0 ? PriceToY(sortedLevels.Last().Price) + levelHeight : yLow;

            if (bar.Delta < 0) Canvas.SetTop(deltaTb, Math.Max(2, topBound - 24));
            else Canvas.SetTop(deltaTb, botBound + 6);
            FootprintCanvas.Children.Add(deltaTb);
        }

        // 6. Absorption Badge (Amber [ABS])
        if (bar.HasAbsorption && _showAbsorption)
        {
            var absHeader = new Border
            {
                Background = _absorptionBadgeBrush, // Amber #F59E0B
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock { Text = "ABS", FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = Brushes.Black }
            };
            Canvas.SetLeft(absHeader, barLeft + barWidth - 26);
            Canvas.SetTop(absHeader, Math.Max(2, yHigh - 22));
            FootprintCanvas.Children.Add(absHeader);
        }
    }

    private void RenderJapaneseCandleBar(OrderFlowBar bar, double barLeft, double centerX, double barWidth, double levelHeight, double yHigh, double yLow, double yOpen, double yClose, bool isBullish)
    {
        double bodyWidth = barWidth * 0.72;
        double bodyLeft = centerX - (bodyWidth / 2);
        double bodyTop = Math.Min(yOpen, yClose);
        double bodyHeight = Math.Max(3.0, Math.Abs(yClose - yOpen));

        var candleBody = new Rectangle
        {
            Width = bodyWidth,
            Height = bodyHeight,
            Fill = isBullish ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            Stroke = isBullish ? new SolidColorBrush(Color.FromRgb(5, 150, 105)) : new SolidColorBrush(Color.FromRgb(185, 28, 28)),
            StrokeThickness = 1.5,
            RadiusX = 2,
            RadiusY = 2
        };
        Canvas.SetLeft(candleBody, bodyLeft);
        Canvas.SetTop(candleBody, bodyTop);
        FootprintCanvas.Children.Add(candleBody);

        // Delta Label
        var deltaTb = new TextBlock
        {
            Text = $"{bar.Delta:+0;-0;0}",
            Foreground = bar.Delta >= 0 ? _buyImbalanceBrush : _sellImbalanceBrush,
            FontWeight = FontWeights.Bold,
            FontSize = 11
        };
        deltaTb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(deltaTb, centerX - (deltaTb.DesiredSize.Width / 2));
        Canvas.SetTop(deltaTb, bar.Delta >= 0 ? yLow + 4 : Math.Max(2, yHigh - 18));
        FootprintCanvas.Children.Add(deltaTb);
    }

    private void RenderOhlcBar(OrderFlowBar bar, double barLeft, double centerX, double barWidth, double levelHeight, double yHigh, double yLow, double yOpen, double yClose, bool isBullish)
    {
        var barBrush = isBullish ? _buyImbalanceBrush : _sellImbalanceBrush;
        double tickWidth = barWidth * 0.35;

        // Open tick (left)
        var openTick = new Line
        {
            X1 = centerX - tickWidth,
            X2 = centerX,
            Y1 = yOpen,
            Y2 = yOpen,
            Stroke = barBrush,
            StrokeThickness = 2
        };
        FootprintCanvas.Children.Add(openTick);

        // Close tick (right)
        var closeTick = new Line
        {
            X1 = centerX,
            X2 = centerX + tickWidth,
            Y1 = yClose,
            Y2 = yClose,
            Stroke = barBrush,
            StrokeThickness = 2
        };
        FootprintCanvas.Children.Add(closeTick);
    }

    private void RenderVolumeProfileBar(OrderFlowBar bar, double barLeft, double centerX, double barWidth, double levelHeight, double yHigh, double yLow, double yOpen, double yClose, bool isBullish)
    {
        // Render horizontal profile histogram inside the candle boundary
        long maxVol = bar.Levels.Count > 0 ? bar.Levels.Max(l => l.TotalVolume) : 1;
        if (maxVol <= 0) maxVol = 1;

        foreach (var lvl in bar.Levels)
        {
            double y = PriceToY(lvl.Price);
            double w = (lvl.TotalVolume / (double)maxVol) * (barWidth - 4);
            var rect = new Rectangle
            {
                Width = Math.Max(4, w),
                Height = Math.Max(4, levelHeight - 2),
                Fill = lvl.IsPoc ? _profilePocBrush : _profileBarBrush,
                RadiusX = 1,
                RadiusY = 1
            };
            Canvas.SetLeft(rect, barLeft + 2);
            Canvas.SetTop(rect, y);
            FootprintCanvas.Children.Add(rect);
        }
    }

    private void RenderMountainCurve(double barStride, double barWidth, double levelHeight)
    {
        var points = new PointCollection();
        for (int i = 0; i < _engine.Bars.Count; i++)
        {
            double x = (i * barStride) + 20 + (barWidth / 2);
            double y = PriceToY(_engine.Bars[i].Close);
            points.Add(new Point(x, y));
        }

        var polyline = new Polyline
        {
            Points = points,
            Stroke = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            StrokeThickness = 2.5
        };
        FootprintCanvas.Children.Add(polyline);
    }

    private void RenderVwapAndBands(double barStride, double barWidth)
    {
        var vwapPoints = new PointCollection();
        var upper1Points = new PointCollection();
        var lower1Points = new PointCollection();
        var upper2Points = new PointCollection();
        var lower2Points = new PointCollection();

        double cumVol = 0;
        double cumPv = 0;
        var prices = new List<double>();
        var vols = new List<double>();

        for (int i = 0; i < _engine.Bars.Count; i++)
        {
            var bar = _engine.Bars[i];
            double x = (i * barStride) + 20 + (barWidth / 2);
            double typPrice = (bar.High + bar.Low + bar.Close) / 3.0;
            double v = bar.TotalVolume <= 0 ? 1 : bar.TotalVolume;

            cumVol += v;
            cumPv += (typPrice * v);
            prices.Add(typPrice);
            vols.Add(v);

            double vwap = cumPv / cumVol;
            vwapPoints.Add(new Point(x, PriceToY(vwap)));

            // Compute standard deviation
            double varianceSum = 0;
            for (int k = 0; k <= i; k++)
            {
                double diff = prices[k] - vwap;
                varianceSum += (diff * diff * vols[k]);
            }
            double stdev = Math.Sqrt(varianceSum / cumVol);

            upper1Points.Add(new Point(x, PriceToY(vwap + stdev)));
            lower1Points.Add(new Point(x, PriceToY(vwap - stdev)));
            upper2Points.Add(new Point(x, PriceToY(vwap + (2 * stdev))));
            lower2Points.Add(new Point(x, PriceToY(vwap - (2 * stdev))));
        }

        // VWAP Central Curve
        FootprintCanvas.Children.Add(new Polyline
        {
            Points = vwapPoints,
            Stroke = _vwapBrush,
            StrokeThickness = 2.5
        });

        // +/- 1 Std Dev Bands
        FootprintCanvas.Children.Add(new Polyline
        {
            Points = upper1Points,
            Stroke = _vwapBandBrush,
            StrokeThickness = 1.2,
            StrokeDashArray = new DoubleCollection { 4, 3 }
        });
        FootprintCanvas.Children.Add(new Polyline
        {
            Points = lower1Points,
            Stroke = _vwapBandBrush,
            StrokeThickness = 1.2,
            StrokeDashArray = new DoubleCollection { 4, 3 }
        });

        // +/- 2 Std Dev Bands
        FootprintCanvas.Children.Add(new Polyline
        {
            Points = upper2Points,
            Stroke = new SolidColorBrush(Color.FromArgb(120, 129, 140, 248)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 2 }
        });
        FootprintCanvas.Children.Add(new Polyline
        {
            Points = lower2Points,
            Stroke = new SolidColorBrush(Color.FromArgb(120, 129, 140, 248)),
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 2 }
        });
    }

    private void RenderSessionVolumeProfile(double canvasWidth, double levelHeight)
    {
        var volByPrice = new Dictionary<double, long>();
        foreach (var bar in _engine.Bars)
        {
            foreach (var lvl in bar.Levels)
            {
                if (!volByPrice.ContainsKey(lvl.Price)) volByPrice[lvl.Price] = 0;
                volByPrice[lvl.Price] += lvl.TotalVolume;
            }
        }

        if (volByPrice.Count == 0) return;
        long maxVol = volByPrice.Values.Max();
        if (maxVol <= 0) maxVol = 1;

        double profileWidth = 140.0;
        double rightX = canvasWidth - 10;

        foreach (var kvp in volByPrice)
        {
            double price = kvp.Key;
            long vol = kvp.Value;
            double y = PriceToY(price);
            double barLen = (vol / (double)maxVol) * profileWidth;

            var r = new Rectangle
            {
                Width = Math.Max(2, barLen),
                Height = Math.Max(3, levelHeight - 2),
                Fill = (vol == maxVol) ? _profilePocBrush : _profileBarBrush,
                RadiusX = 1,
                RadiusY = 1
            };
            Canvas.SetLeft(r, rightX - barLen);
            Canvas.SetTop(r, y);
            FootprintCanvas.Children.Add(r);
        }
    }

    private void RenderCvdCurve(double barStride, double barWidth)
    {
        if (_engine.Bars.Count < 2) return;

        var cvdPoints = new PointCollection();
        long minDelta = _engine.Bars.Min(b => b.CumDelta);
        long maxDelta = _engine.Bars.Max(b => b.CumDelta);
        long deltaSpan = Math.Max(1, maxDelta - minDelta);

        double chartBottom = FootprintCanvas.Height - 30;
        double cvdHeight = 80.0;

        for (int i = 0; i < _engine.Bars.Count; i++)
        {
            double x = (i * barStride) + 20 + (barWidth / 2);
            double norm = (double)(_engine.Bars[i].CumDelta - minDelta) / deltaSpan;
            double y = chartBottom - (norm * cvdHeight);
            cvdPoints.Add(new Point(x, y));
        }

        FootprintCanvas.Children.Add(new Polyline
        {
            Points = cvdPoints,
            Stroke = _cvdBrush,
            StrokeThickness = 2
        });
    }

    // --- Price Axis Rendering ---
    private void RenderPriceAxis()
    {
        PriceAxisCanvas.Children.Clear();
        PriceAxisCanvas.Height = FootprintCanvas.Height;
        double levelHeight = _baseLevelHeight * _zoomFactor;

        double step = _engine.PriceStep;
        if (step <= 0.0001) step = 1.0;
        for (double p = Math.Ceiling(_maxPrice / step) * step; p >= _minPrice - (step * 0.01); p -= step)
        {
            double y = PriceToY(p);
            var tb = new TextBlock
            {
                Text = _engine.ActiveContract.FormatPrice(p),
                Foreground = _textMuted,
                FontSize = 10,
                FontFamily = new FontFamily("Consolas")
            };
            Canvas.SetLeft(tb, 6);
            Canvas.SetTop(tb, y - 7);
            PriceAxisCanvas.Children.Add(tb);
        }

        // Active Price Pill
        if (_engine.CurrentBar != null)
        {
            double curY = PriceToY(_engine.CurrentBar.Close);
            var curPill = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 2, 4, 2),
                Child = new TextBlock
                {
                    Text = _engine.ActiveContract.FormatPrice(_engine.CurrentBar.Close),
                    Foreground = Brushes.White,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Consolas")
                }
            };
            Canvas.SetLeft(curPill, 2);
            Canvas.SetTop(curPill, curY - 10);
            PriceAxisCanvas.Children.Add(curPill);
        }

        // Re-attach crosshair price chip
        if (_priceChip != null) PriceAxisCanvas.Children.Add(_priceChip);
    }

    // --- Order Flow Ribbon Rendering ---
    private void RenderRibbonCanvas()
    {
        RibbonCanvas.Children.Clear();

        double barWidth = _baseBarWidth * _zoomFactor;
        double barSpacing = 12.0 * _zoomFactor;
        double barStride = barWidth + barSpacing;
        RibbonCanvas.Width = Math.Max(2400, (_engine.Bars.Count * barStride) + 300);

        string[] rowHeaders = { "Buy volume", "Sell volume", "Delta", "Cum Delta", "Min Delta", "Max Delta", "Time" };
        for (int r = 0; r <= 7; r++)
        {
            var line = new Line
            {
                X1 = 0,
                X2 = RibbonCanvas.Width,
                Y1 = r * 23,
                Y2 = r * 23,
                Stroke = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
                StrokeThickness = 1
            };
            RibbonCanvas.Children.Add(line);
        }

        for (int i = 0; i < _engine.Bars.Count; i++)
        {
            var bar = _engine.Bars[i];
            double x = (i * barStride) + 20;

            // Row 0: Buy Volume
            bool heavyBuy = bar.BuyVolume > bar.SellVolume * 1.4;
            Brush? buyBg = heavyBuy ? new SolidColorBrush(Color.FromArgb(140, 22, 101, 52)) : null;
            AddRibbonCell(bar.BuyVolume.ToString(), x, barWidth, 0 * 23, heavyBuy ? Brushes.White : _textWhite, heavyBuy, buyBg);

            // Row 1: Sell Volume (User's screenshot shows red highlight on heavy sell)
            bool heavySell = bar.SellVolume > bar.BuyVolume * 1.4;
            Brush? sellBg = heavySell ? new SolidColorBrush(Color.FromArgb(180, 185, 28, 28)) : null;
            AddRibbonCell(bar.SellVolume.ToString(), x, barWidth, 1 * 23, heavySell ? Brushes.White : _textWhite, heavySell, sellBg);

            // Row 2: Delta
            AddRibbonCell(bar.Delta.ToString(), x, barWidth, 2 * 23, bar.Delta >= 0 ? _buyImbalanceBrush : _sellImbalanceBrush, true, null);

            // Row 3: Cum Delta
            Brush cumBg = bar.CumDelta < 0 ? new SolidColorBrush(Color.FromArgb(140, 185, 28, 28)) : new SolidColorBrush(Color.FromArgb(140, 22, 101, 52));
            AddRibbonCell(bar.CumDelta.ToString(), x, barWidth, 3 * 23, Brushes.White, true, cumBg);

            // Row 4: Min Delta
            AddRibbonCell(bar.MinDelta.ToString(), x, barWidth, 4 * 23, new SolidColorBrush(Color.FromRgb(253, 164, 175)), false, null);

            // Row 5: Max Delta
            AddRibbonCell(bar.MaxDelta.ToString(), x, barWidth, 5 * 23, new SolidColorBrush(Color.FromRgb(110, 231, 183)), false, null);

            // Row 6: Time
            AddRibbonCell(bar.FormattedTime, x, barWidth, 6 * 23, new SolidColorBrush(Color.FromRgb(161, 161, 170)), true, null);
        }

        // Re-attach crosshair time chip
        if (_timeChip != null) RibbonCanvas.Children.Add(_timeChip);
    }

    private void AddRibbonCell(string text, double x, double width, double y, Brush fg, bool isBold, Brush? bg)
    {
        var border = new Border
        {
            Width = width,
            Height = 23,
            Background = bg ?? Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 39, 39, 42)),
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        var tb = new TextBlock
        {
            Text = text,
            Foreground = fg,
            FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
            FontSize = 10.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        border.Child = tb;
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        RibbonCanvas.Children.Add(border);
    }

    // --- Interactive Mouse Handlers (Pan, Scale, Crosshair, Draw) ---
    private void ChartCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            Point pt = e.GetPosition(FootprintCanvas);

            if (_activeDrawingTool == DrawingToolType.Cursor)
            {
                _isPanning = true;
                _panStartPoint = e.GetPosition(this);
                _panStartHOffset = ChartScrollViewer.HorizontalOffset;
                _panStartVOffset = ChartScrollViewer.VerticalOffset;
                ChartScrollViewer.CaptureMouse();
                Cursor = Cursors.SizeAll;
            }
            else if (_activeDrawingTool == DrawingToolType.HorizontalLine)
            {
                // Place horizontal ray at clicked price
                double price = YToPrice(pt.Y);
                double y = PriceToY(price);
                var ray = new Line
                {
                    X1 = 0,
                    X2 = FootprintCanvas.Width,
                    Y1 = y,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 2 }
                };
                DrawingsCanvas.Children.Add(ray);
            }
            else if (_activeDrawingTool == DrawingToolType.TrendLine || _activeDrawingTool == DrawingToolType.Fibonacci || _activeDrawingTool == DrawingToolType.Ruler)
            {
                _drawingStartPoint = pt;
                ChartScrollViewer.CaptureMouse();
            }
        }
    }

    private void ChartCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        Point canvasPt = e.GetPosition(FootprintCanvas);

        // Update Crosshair & Floating Price/Time Chips
        UpdateCrosshair(canvasPt);

        if (_isPanning)
        {
            Point currentPoint = e.GetPosition(this);
            double deltaX = currentPoint.X - _panStartPoint.X;
            double deltaY = currentPoint.Y - _panStartPoint.Y;

            ChartScrollViewer.ScrollToHorizontalOffset(_panStartHOffset - deltaX);
            ChartScrollViewer.ScrollToVerticalOffset(_panStartVOffset - deltaY);
        }
    }

    private void ChartCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            ChartScrollViewer.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }

        if (_drawingStartPoint.HasValue)
        {
            Point endPt = e.GetPosition(FootprintCanvas);
            Point startPt = _drawingStartPoint.Value;

            if (_activeDrawingTool == DrawingToolType.TrendLine)
            {
                var line = new Line
                {
                    X1 = startPt.X,
                    Y1 = startPt.Y,
                    X2 = endPt.X,
                    Y2 = endPt.Y,
                    Stroke = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                    StrokeThickness = 2
                };
                DrawingsCanvas.Children.Add(line);
            }
            else if (_activeDrawingTool == DrawingToolType.Fibonacci)
            {
                DrawFibonacciLevels(startPt, endPt);
            }
            else if (_activeDrawingTool == DrawingToolType.Ruler)
            {
                DrawRulerMeasurement(startPt, endPt);
            }

            _drawingStartPoint = null;
            ChartScrollViewer.ReleaseMouseCapture();
        }
    }

    private void UpdateCrosshair(Point pt)
    {
        if (_crosshairH == null || _crosshairV == null || _priceChip == null || _timeChip == null) return;

        bool show = _activeDrawingTool == DrawingToolType.Crosshair || Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

        if (show && pt.X >= 0 && pt.X <= FootprintCanvas.Width && pt.Y >= 0 && pt.Y <= FootprintCanvas.Height)
        {
            _crosshairH.Visibility = Visibility.Visible;
            _crosshairV.Visibility = Visibility.Visible;
            _priceChip.Visibility = Visibility.Visible;
            _timeChip.Visibility = Visibility.Visible;

            _crosshairH.X1 = 0;
            _crosshairH.X2 = FootprintCanvas.Width;
            _crosshairH.Y1 = pt.Y;
            _crosshairH.Y2 = pt.Y;

            _crosshairV.X1 = pt.X;
            _crosshairV.X2 = pt.X;
            _crosshairV.Y1 = 0;
            _crosshairV.Y2 = FootprintCanvas.Height;

            double price = YToPrice(pt.Y);
            if (_priceChipText != null) _priceChipText.Text = _engine.ActiveContract.FormatPrice(price);
            Canvas.SetLeft(_priceChip, 2);
            Canvas.SetTop(_priceChip, Math.Clamp(pt.Y - 10, 0, PriceAxisCanvas.Height - 20));

            // Determine time at X
            double barWidth = _baseBarWidth * _zoomFactor;
            double barSpacing = 12.0 * _zoomFactor;
            int barIndex = (int)Math.Clamp((pt.X - 20) / (barWidth + barSpacing), 0, _engine.Bars.Count - 1);
            if (_engine.Bars.Count > 0 && barIndex < _engine.Bars.Count)
            {
                if (_timeChipText != null) _timeChipText.Text = _engine.Bars[barIndex].FormattedTime;
                Canvas.SetLeft(_timeChip, Math.Clamp(pt.X - 25, 0, RibbonCanvas.Width - 60));
                Canvas.SetTop(_timeChip, 6 * 23);
            }
        }
        else
        {
            _crosshairH.Visibility = Visibility.Collapsed;
            _crosshairV.Visibility = Visibility.Collapsed;
            _priceChip.Visibility = Visibility.Collapsed;
            _timeChip.Visibility = Visibility.Collapsed;
        }
    }

    private void DrawFibonacciLevels(Point start, Point end)
    {
        double top = Math.Min(start.Y, end.Y);
        double bottom = Math.Max(start.Y, end.Y);
        double height = bottom - top;
        double left = Math.Min(start.X, end.X);
        double right = Math.Max(start.X, end.X) + 200;

        double[] ratios = { 0.0, 0.236, 0.382, 0.50, 0.618, 0.786, 1.0 };
        string[] labels = { "0.0%", "23.6%", "38.2%", "50.0%", "61.8%", "78.6%", "100.0%" };
        Color[] colors = {
            Color.FromRgb(148, 163, 184),
            Color.FromRgb(56, 189, 248),
            Color.FromRgb(34, 197, 94),
            Color.FromRgb(245, 158, 11),
            Color.FromRgb(239, 68, 68),
            Color.FromRgb(168, 85, 247),
            Color.FromRgb(148, 163, 184)
        };

        for (int i = 0; i < ratios.Length; i++)
        {
            double y = top + (height * ratios[i]);
            var line = new Line
            {
                X1 = left,
                X2 = right,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(colors[i]),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 2 }
            };
            DrawingsCanvas.Children.Add(line);

            var tb = new TextBlock
            {
                Text = $"Fib {labels[i]} ({_engine.ActiveContract.FormatPrice(YToPrice(y))})",
                Foreground = new SolidColorBrush(colors[i]),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold
            };
            Canvas.SetLeft(tb, left + 4);
            Canvas.SetTop(tb, y - 14);
            DrawingsCanvas.Children.Add(tb);
        }
    }

    private void DrawRulerMeasurement(Point start, Point end)
    {
        double p1 = YToPrice(start.Y);
        double p2 = YToPrice(end.Y);
        double deltaPrice = p2 - p1;
        double ticks = deltaPrice / (_engine.ActiveContract.TickSize <= 0 ? 1 : _engine.ActiveContract.TickSize);

        var line = new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            StrokeThickness = 1.5
        };
        DrawingsCanvas.Children.Add(line);

        var box = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 15, 23, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 4, 6, 4),
            Child = new TextBlock
            {
                Text = $"Δ: {deltaPrice:+0.00;-0.00;0.00} ({ticks:+0;-0;0} Ticks)",
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold
            }
        };
        Canvas.SetLeft(box, Math.Max(start.X, end.X) + 8);
        Canvas.SetTop(box, (start.Y + end.Y) / 2);
        DrawingsCanvas.Children.Add(box);
    }

    // --- Price Axis Scaling Handlers ---
    private void PriceAxis_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDraggingAxis = true;
            _axisDragStartPoint = e.GetPosition(this);
            _axisDragStartZoom = _zoomFactor;
            PriceAxisScrollViewer.CaptureMouse();
            Cursor = Cursors.SizeNS;
        }
    }

    private void PriceAxis_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingAxis)
        {
            Point current = e.GetPosition(this);
            double dy = _axisDragStartPoint.Y - current.Y;
            double newZoom = _axisDragStartZoom + (dy * 0.008);
            SetZoom(newZoom);
        }
    }

    private void PriceAxis_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingAxis)
        {
            _isDraggingAxis = false;
            PriceAxisScrollViewer.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
    }

    private void PriceAxis_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        double step = e.Delta > 0 ? 0.08 : -0.08;
        SetZoom(_zoomFactor + step);
    }

    private void ChartScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            e.Handled = true;
            double step = e.Delta > 0 ? 0.08 : -0.08;
            SetZoom(_zoomFactor + step);
        }
    }

    public void SetZoom(double zoom)
    {
        _zoomFactor = Math.Clamp(zoom, 0.6, 3.0);
        if (ZoomSlider != null && Math.Abs(ZoomSlider.Value - _zoomFactor) > 0.01)
        {
            ZoomSlider.Value = _zoomFactor;
        }
        if (ZoomLabel != null)
        {
            ZoomLabel.Text = $"{Math.Round(_zoomFactor * 100)}%";
        }
        RedrawAll();
    }

    private void ZoomInBtn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoomFactor + 0.15);
    private void ZoomOutBtn_Click(object sender, RoutedEventArgs e) => SetZoom(_zoomFactor - 0.15);
    private void ZoomResetBtn_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);
    private void ZoomFitBtn_Click(object sender, RoutedEventArgs e) => SetZoom(1.35);

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized) return;
        SetZoom(e.NewValue);
    }

    // --- Toolbar Event Handlers ---
    private void TimeframeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || TimeframeCombo?.SelectedItem is not ComboBoxItem item) return;
        _activeTimeframe = item.Content.ToString() ?? "700 Range";
        _engine.CurrentTimeframe = _activeTimeframe;
        if (SymbolTitleText != null)
        {
            SymbolTitleText.Text = $"{_engine.ActiveContract.Symbol} {_engine.ActiveContract.FrontMonth} ({_activeTimeframe})";
        }
        RedrawAll();
        CenterOnCurrentPrice();
    }

    private void ChartTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || ChartTypeCombo?.SelectedItem is not ComboBoxItem item) return;
        string text = item.Content.ToString() ?? "";
        if (text.Contains("Footprint")) _chartType = ChartType.Footprint;
        else if (text.Contains("Candlestick")) _chartType = ChartType.Candlestick;
        else if (text.Contains("OHLC")) _chartType = ChartType.OhlcBars;
        else if (text.Contains("Volume Profile")) _chartType = ChartType.VolumeProfile;
        else if (text.Contains("Area Line")) _chartType = ChartType.MountainArea;

        RedrawAll();
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized) return;
        RedrawAll();
    }

    private void ImbalanceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || ImbalanceCombo?.SelectedItem is not ComboBoxItem item) return;
        string text = item.Content.ToString() ?? "";
        double ratio = 3.0;
        if (text.Contains("200%")) ratio = 2.0;
        else if (text.Contains("400%")) ratio = 4.0;

        _engine.RecalculateAllBars(ratio);
        RedrawAll();
    }

    private void OptionCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized) return;
        _showCandleLine = ShowCandleCheck?.IsChecked == true;
        _showZones = ShowZonesCheck?.IsChecked == true;
        _showPocLine = ShowPocLineCheck?.IsChecked == true;
        _showVwap = ShowVwapCheck?.IsChecked == true;
        _showProfile = ShowProfileCheck?.IsChecked == true;
        _showCvd = ShowCvdCheck?.IsChecked == true;
        _showAbsorption = ShowAbsCheck?.IsChecked == true;
        _showPatterns = _showAbsorption;
        RedrawAll();
    }

    private void DrawingTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            _activeDrawingTool = tag switch
            {
                "Crosshair" => DrawingToolType.Crosshair,
                "Trendline" => DrawingToolType.TrendLine,
                "HRay" => DrawingToolType.HorizontalLine,
                "Fib" => DrawingToolType.Fibonacci,
                "Ruler" => DrawingToolType.Ruler,
                _ => DrawingToolType.Cursor
            };
            HighlightActiveDrawingTool();
        }
    }

    private void HighlightActiveDrawingTool()
    {
        var activeBg = new SolidColorBrush(Color.FromRgb(37, 99, 235)); // Blue
        var normalBg = new SolidColorBrush(Color.FromRgb(39, 39, 42)); // Zinc

        if (ToolPanBtn != null) ToolPanBtn.Background = _activeDrawingTool == DrawingToolType.Cursor ? activeBg : normalBg;
        if (ToolCrosshairBtn != null) ToolCrosshairBtn.Background = _activeDrawingTool == DrawingToolType.Crosshair ? activeBg : normalBg;
        if (ToolTrendlineBtn != null) ToolTrendlineBtn.Background = _activeDrawingTool == DrawingToolType.TrendLine ? activeBg : normalBg;
        if (ToolHorizontalRayBtn != null) ToolHorizontalRayBtn.Background = _activeDrawingTool == DrawingToolType.HorizontalLine ? activeBg : normalBg;
        if (ToolFibonacciBtn != null) ToolFibonacciBtn.Background = _activeDrawingTool == DrawingToolType.Fibonacci ? activeBg : normalBg;
        if (ToolRulerBtn != null) ToolRulerBtn.Background = _activeDrawingTool == DrawingToolType.Ruler ? activeBg : normalBg;

        if (CrosshairCanvas != null && _activeDrawingTool != DrawingToolType.Crosshair)
        {
            if (_crosshairH != null) _crosshairH.Visibility = Visibility.Collapsed;
            if (_crosshairV != null) _crosshairV.Visibility = Visibility.Collapsed;
            if (_priceChip != null) _priceChip.Visibility = Visibility.Collapsed;
            if (_timeChip != null) _timeChip.Visibility = Visibility.Collapsed;
        }
    }

    private void ClearDrawings_Click(object sender, RoutedEventArgs e)
    {
        DrawingsCanvas?.Children.Clear();
    }

    // --- Symbol Selection & Bottom Multi-Tab Navigation ---
    private void SymbolSelectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized || SymbolSelectCombo?.SelectedItem is not ComboBoxItem item) return;
        string text = item.Content.ToString() ?? "";
        string sym = text.Split(' ')[0].Trim();
        SelectAndLoadSymbol(sym);
    }

    public void SelectAndLoadSymbol(string symbol)
    {
        var contract = _registry.GetBySymbol(symbol);
        if (contract == null)
        {
            contract = new FuturesContract
            {
                Symbol = symbol,
                Name = symbol,
                Exchange = symbol.EndsWith("_I") ? "NSE_FUT" : "NSE_EQ",
                Category = symbol.EndsWith("_I") ? "Indian Futures" : "Indian Equities",
                TickSize = 0.05,
                PointValue = 1.0,
                CurrentPrice = 24374.5,
                FrontMonth = "OCT-26"
            };
        }

        _engine.SwitchContract(contract);
        _engine.CurrentTimeframe = _activeTimeframe;

        if (SymbolTitleText != null)
        {
            SymbolTitleText.Text = $"{contract.Symbol} {contract.FrontMonth} ({_activeTimeframe})";
        }

        if (SymbolSelectCombo != null)
        {
            foreach (ComboBoxItem itm in SymbolSelectCombo.Items)
            {
                string c = itm.Content.ToString() ?? "";
                if (c.StartsWith(symbol, StringComparison.OrdinalIgnoreCase))
                {
                    SymbolSelectCombo.SelectedItem = itm;
                    break;
                }
            }
        }

        if (LivePriceIndicator != null && _engine.CurrentBar != null)
        {
            double tick = contract.TickSize > 0.0001 ? contract.TickSize : 0.05;
            LivePriceIndicator.Text = $"Last: {contract.FormatPrice(_engine.CurrentBar.Close)}  Ask: {contract.FormatPrice(_engine.CurrentBar.Close + tick)}  Bid: {contract.FormatPrice(_engine.CurrentBar.Close - tick)}  Delta: {_engine.CurrentBar.Delta:+0;-0;0}";
        }

        UpdateTabSelection(symbol);
        RedrawAll();
        CenterOnCurrentPrice();
    }

    private void InstrumentTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string symbol)
        {
            SelectAndLoadSymbol(symbol);
            HighlightInstrumentTab(btn);
        }
    }

    private void UpdateTabSelection(string symbol)
    {
        if (InstrumentTabsPanel == null) return;
        foreach (var child in InstrumentTabsPanel.Children)
        {
            if (child is Button b && b.Tag is string tag && tag.Equals(symbol, StringComparison.OrdinalIgnoreCase))
            {
                HighlightInstrumentTab(b);
                return;
            }
        }
    }

    private void HighlightInstrumentTab(Button activeBtn)
    {
        if (InstrumentTabsPanel == null) return;
        foreach (var child in InstrumentTabsPanel.Children)
        {
            if (child is Button tab && tab != TabAddBtn)
            {
                if (tab == activeBtn)
                {
                    tab.Background = new SolidColorBrush(Color.FromRgb(9, 9, 11));
                    tab.BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                    tab.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250));
                    tab.FontWeight = FontWeights.Bold;
                }
                else
                {
                    tab.Background = new SolidColorBrush(Color.FromRgb(39, 39, 42));
                    tab.BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70));
                    tab.Foreground = new SolidColorBrush(Color.FromRgb(228, 228, 231));
                    tab.FontWeight = FontWeights.SemiBold;
                }
            }
        }
    }

    private void AddTab_Click(object sender, RoutedEventArgs e)
    {
        OpenSymbolSearch_Click(sender, e);
    }

    // --- Symbol Search Dialog Handlers ---
    private void OpenSymbolSearch_Click(object sender, RoutedEventArgs e)
    {
        if (SymbolSearchModal == null) return;
        PopulateSymbolSearchResults();
        SymbolSearchModal.Visibility = Visibility.Visible;
        SymbolSearchQueryBox?.Focus();
    }

    private void CloseSymbolSearch_Click(object sender, RoutedEventArgs e)
    {
        if (SymbolSearchModal != null) SymbolSearchModal.Visibility = Visibility.Collapsed;
    }

    private void PopulateSymbolSearchResults(string? filter = null)
    {
        if (SymbolSearchResultsList == null) return;
        var all = _registry.GetAllContracts();
        if (string.IsNullOrWhiteSpace(filter))
        {
            SymbolSearchResultsList.ItemsSource = all;
        }
        else
        {
            string q = filter.Trim().ToUpperInvariant();
            SymbolSearchResultsList.ItemsSource = all.Where(c => 
                c.Symbol.ToUpperInvariant().Contains(q) || 
                c.Name.ToUpperInvariant().Contains(q) || 
                c.Category.ToUpperInvariant().Contains(q)).ToList();
        }
    }

    private void SymbolSearchQueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PopulateSymbolSearchResults(SymbolSearchQueryBox?.Text);
    }

    private void SelectSearchedSymbol_Click(object sender, RoutedEventArgs e)
    {
        if (SymbolSearchResultsList?.SelectedItem is FuturesContract selected)
        {
            ApplySearchedSymbol(selected);
        }
        CloseSymbolSearch_Click(sender, e);
    }

    private void SymbolSearchResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SymbolSearchResultsList?.SelectedItem is FuturesContract selected)
        {
            ApplySearchedSymbol(selected);
            CloseSymbolSearch_Click(sender, e);
        }
    }

    private void ApplySearchedSymbol(FuturesContract selected)
    {
        bool tabExists = false;
        Button? targetTab = null;
        if (InstrumentTabsPanel != null)
        {
            foreach (var child in InstrumentTabsPanel.Children)
            {
                if (child is Button b && b.Tag is string tag && tag.Equals(selected.Symbol, StringComparison.OrdinalIgnoreCase))
                {
                    tabExists = true;
                    targetTab = b;
                    break;
                }
            }

            if (!tabExists)
            {
                targetTab = new Button
                {
                    Content = selected.Symbol,
                    Tag = selected.Symbol,
                    Style = (Style)FindResource("SmallToolButton"),
                    Margin = new Thickness(2, 0, 2, 0)
                };
                targetTab.Click += InstrumentTab_Click;
                int insertIndex = Math.Max(0, InstrumentTabsPanel.Children.Count - 1);
                InstrumentTabsPanel.Children.Insert(insertIndex, targetTab);
            }
        }

        SelectAndLoadSymbol(selected.Symbol);
        if (targetTab != null) HighlightInstrumentTab(targetTab);
    }

    // --- NinjaTrader 8 mzFootprint Properties Modal Handlers ---
    public void OpenProperties_Click(object sender, RoutedEventArgs e)
    {
        if (PropShowCandleCheck != null) PropShowCandleCheck.IsChecked = _showCandleLine;
        if (PropShowStackedZonesCheck != null) PropShowStackedZonesCheck.IsChecked = _showZones;
        if (PropShowAbsorptionCheck != null) PropShowAbsorptionCheck.IsChecked = _showAbsorption;
        if (PropShowBarPocCheck != null) PropShowBarPocCheck.IsChecked = _showPocLine;
        if (PropShowDevelopingPocCheck != null) PropShowDevelopingPocCheck.IsChecked = _showPocLine;
        if (PropShowVwapCheck != null) PropShowVwapCheck.IsChecked = _showVwap;
        if (PropShowProfileCheck != null) PropShowProfileCheck.IsChecked = _showProfile;
        if (PropShowCvdCheck != null) PropShowCvdCheck.IsChecked = _showCvd;
        if (PropShowBarDeltaCheck != null) PropShowBarDeltaCheck.IsChecked = _showBarDelta;

        if (PropImbalanceRatioCombo != null)
        {
            if (Math.Abs(_engine.ImbalanceRatio - 2.0) < 0.1) PropImbalanceRatioCombo.SelectedIndex = 2; // 200%
            else if (Math.Abs(_engine.ImbalanceRatio - 3.0) < 0.1) PropImbalanceRatioCombo.SelectedIndex = 1; // 300%
            else if (Math.Abs(_engine.ImbalanceRatio - 5.0) < 0.1) PropImbalanceRatioCombo.SelectedIndex = 3; // 500%
            else PropImbalanceRatioCombo.SelectedIndex = 0; // 400%
        }

        if (PropertiesModal != null) PropertiesModal.Visibility = Visibility.Visible;
    }

    public void CloseProperties_Click(object sender, RoutedEventArgs e)
    {
        if (PropertiesModal != null) PropertiesModal.Visibility = Visibility.Collapsed;
    }

    private void ApplyProperties_Click(object sender, RoutedEventArgs e)
    {
        if (PropImbalanceRatioCombo?.SelectedItem is ComboBoxItem imbItem)
        {
            string s = imbItem.Content.ToString() ?? "";
            double ratio = 4.0;
            if (s.Contains("200%")) ratio = 2.0;
            else if (s.Contains("300%")) ratio = 3.0;
            else if (s.Contains("400%")) ratio = 4.0;
            else if (s.Contains("500%")) ratio = 5.0;
            _engine.ImbalanceRatio = ratio;
            _engine.RecalculateAllBars(ratio);

            if (ImbalanceCombo != null)
            {
                if (Math.Abs(ratio - 2.0) < 0.1) ImbalanceCombo.SelectedIndex = 1; // 200%
                else if (Math.Abs(ratio - 3.0) < 0.1) ImbalanceCombo.SelectedIndex = 0; // 300%
                else if (Math.Abs(ratio - 4.0) < 0.1) ImbalanceCombo.SelectedIndex = 2; // 400%
            }
        }

        if (PropShowCandleCheck != null) _showCandleLine = PropShowCandleCheck.IsChecked == true;
        if (PropShowStackedZonesCheck != null) _showZones = PropShowStackedZonesCheck.IsChecked == true;
        if (PropShowAbsorptionCheck != null) _showAbsorption = PropShowAbsorptionCheck.IsChecked == true;
        _showPatterns = _showAbsorption;
        if (PropShowBarPocCheck != null) _showPocLine = PropShowBarPocCheck.IsChecked == true;
        if (PropShowVwapCheck != null) _showVwap = PropShowVwapCheck.IsChecked == true;
        if (PropShowProfileCheck != null) _showProfile = PropShowProfileCheck.IsChecked == true;
        if (PropShowCvdCheck != null) _showCvd = PropShowCvdCheck.IsChecked == true;
        if (PropShowBarDeltaCheck != null) _showBarDelta = PropShowBarDeltaCheck.IsChecked == true;

        // Synchronize toolbar checkboxes
        if (ShowCandleCheck != null) ShowCandleCheck.IsChecked = _showCandleLine;
        if (ShowZonesCheck != null) ShowZonesCheck.IsChecked = _showZones;
        if (ShowAbsCheck != null) ShowAbsCheck.IsChecked = _showAbsorption;
        if (ShowPocLineCheck != null) ShowPocLineCheck.IsChecked = _showPocLine;
        if (ShowVwapCheck != null) ShowVwapCheck.IsChecked = _showVwap;
        if (ShowProfileCheck != null) ShowProfileCheck.IsChecked = _showProfile;
        if (ShowCvdCheck != null) ShowCvdCheck.IsChecked = _showCvd;

        RedrawAll();
        CloseProperties_Click(sender, e);
    }

    private void ResetPropertiesToNt8_Click(object sender, RoutedEventArgs e)
    {
        if (PropClusterTypeCombo != null) PropClusterTypeCombo.SelectedIndex = 0;
        if (PropTicksPerLevelText != null) PropTicksPerLevelText.Text = "100 (5 pts for Nifty)";
        if (PropShowCandleCheck != null) PropShowCandleCheck.IsChecked = true;
        if (PropShowBarDeltaCheck != null) PropShowBarDeltaCheck.IsChecked = true;
        if (PropShowImbalancesCheck != null) PropShowImbalancesCheck.IsChecked = true;
        if (PropImbalanceRatioCombo != null) PropImbalanceRatioCombo.SelectedIndex = 0;
        if (PropShowStackedZonesCheck != null) PropShowStackedZonesCheck.IsChecked = true;
        if (PropShowAbsorptionCheck != null) PropShowAbsorptionCheck.IsChecked = true;
        if (PropShowBarPocCheck != null) PropShowBarPocCheck.IsChecked = true;
        if (PropShowBarVaCheck != null) PropShowBarVaCheck.IsChecked = true;
        if (PropShowDevelopingPocCheck != null) PropShowDevelopingPocCheck.IsChecked = true;
        if (PropShowVwapCheck != null) PropShowVwapCheck.IsChecked = true;
        if (PropShowProfileCheck != null) PropShowProfileCheck.IsChecked = true;
        if (PropShowCvdCheck != null) PropShowCvdCheck.IsChecked = true;

        _showCandleLine = true;
        _showZones = true;
        _showAbsorption = true;
        _showPatterns = true;
        _showPocLine = true;
        _showVwap = true;
        _showProfile = true;
        _showCvd = true;
        _showBarDelta = true;

        if (ShowCandleCheck != null) ShowCandleCheck.IsChecked = true;
        if (ShowZonesCheck != null) ShowZonesCheck.IsChecked = true;
        if (ShowAbsCheck != null) ShowAbsCheck.IsChecked = true;
        if (ShowPocLineCheck != null) ShowPocLineCheck.IsChecked = true;
        if (ShowVwapCheck != null) ShowVwapCheck.IsChecked = true;
        if (ShowProfileCheck != null) ShowProfileCheck.IsChecked = true;
        if (ShowCvdCheck != null) ShowCvdCheck.IsChecked = true;

        _engine.ImbalanceRatio = 4.0;
        _engine.RecalculateAllBars(4.0);
        RedrawAll();
    }

    private void RibbonScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!_isInitialized || ChartScrollViewer == null || RibbonScrollViewer == null) return;
        ChartScrollViewer.ScrollToHorizontalOffset(RibbonScrollViewer.HorizontalOffset);
    }

    private void FeedToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsLiveActive)
        {
            _engine.Stop();
            FeedToggleBtn.Content = "▶ Resume";
        }
        else
        {
            _engine.Start();
            FeedToggleBtn.Content = "⏸ Pause";
        }
    }

    private void ResetViewBtn_Click(object sender, RoutedEventArgs e)
    {
        _engine.LoadHistoricalFootprintData();
        FeedToggleBtn.Content = "▶ Resume";
        SetZoom(1.35);
        RedrawAll();
        CenterOnCurrentPrice();
    }
}
