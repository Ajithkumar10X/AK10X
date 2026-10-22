namespace WindowsApp.Ninja.Models;

public class NinjaAccount
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ConnectionName { get; set; } = string.Empty;
    public double CashBalance { get; set; }
    public double BuyingPower { get; set; }
    public double RealizedPnL { get; set; }
    public double UnrealizedPnL { get; set; }
    public string Currency { get; set; } = "USD";
    public bool IsActive { get; set; }

    public double NetLiquidation => CashBalance + RealizedPnL + UnrealizedPnL;
}
