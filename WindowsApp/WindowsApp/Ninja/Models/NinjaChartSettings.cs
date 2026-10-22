namespace WindowsApp.Ninja.Models;

public enum ChartType
{
    Footprint,
    Candlestick,
    OhlcBars,
    VolumeProfile,
    MountainArea
}

public enum DrawingToolType
{
    Cursor,
    Crosshair,
    TrendLine,
    HorizontalLine,
    VerticalLine,
    Fibonacci,
    Ruler,
    RectangleBox
}

public class NinjaChartSettings
{
    public string Timeframe { get; set; } = "5 Minute";
    public ChartType Type { get; set; } = ChartType.Footprint;
    public DrawingToolType ActiveDrawingTool { get; set; } = DrawingToolType.Cursor;

    // Indicator Toggles
    public bool ShowVwap { get; set; } = true;
    public bool ShowVwapBands { get; set; } = true;
    public bool ShowVolumeProfile { get; set; } = true;
    public bool ShowCumulativeDelta { get; set; } = true;
    public bool ShowEma { get; set; } = false;
    public bool ShowCrosshair { get; set; } = true;
}
