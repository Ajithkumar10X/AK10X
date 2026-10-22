namespace WindowsApp.Ninja.OrderFlow.Models;

public enum OrderFlowZoneType
{
    Supply, // Red / Resistance zone
    Demand  // Green / Support zone
}

public class OrderFlowZone
{
    public double PriceHigh { get; set; }
    public double PriceLow { get; set; }
    public OrderFlowZoneType Type { get; set; }
    public long Volume { get; set; }
    public int StartBarIndex { get; set; }
    public int? EndBarIndex { get; set; }
    public bool IsActive => !EndBarIndex.HasValue;
}
