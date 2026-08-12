using PropRiskManager.Domain;

namespace PropRiskManager.State;

public sealed class PluginSettings
{
    public RiskMode RiskMode { get; set; } = RiskMode.PercentEquity;
    public double RiskValue { get; set; } = 1.0;
    public double CommissionPerLotRoundTrip { get; set; }
    public bool UseEntryPrice { get; set; }
    public bool UseStopLoss { get; set; } = true;
    public bool UseTakeProfit { get; set; } = true;
    public bool DrawLines { get; set; } = true;
    public double StopLossPips { get; set; } = 20;
    public double TakeProfitPips { get; set; } = 40;
    public double MaxRiskPercent { get; set; } = 5.0;
    public double MaxSpreadPips { get; set; }
}
