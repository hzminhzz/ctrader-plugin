using cAlgo.API;

namespace PropRiskManager.Domain;

public sealed class TradePlan
{
    public TradeType TradeType { get; init; }
    public string SymbolName { get; init; } = string.Empty;
    public double EntryPrice { get; init; }
    public double StopLossPrice { get; init; }
    public double? TakeProfitPrice { get; init; }
    public double RiskPercent { get; init; }
    public double RiskAmount { get; init; }
    public double StopLossPips { get; init; }
    public double? TakeProfitPips { get; init; }
    public double VolumeInUnits { get; init; }
    public double QuantityLots { get; init; }
    public double RewardRiskRatio { get; init; }
}
