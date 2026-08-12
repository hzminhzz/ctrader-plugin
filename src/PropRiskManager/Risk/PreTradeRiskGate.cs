using cAlgo.API;
using cAlgo.API.Internals;
using PropRiskManager.Domain;

namespace PropRiskManager.Risk;

public sealed record RiskGateResult(bool Allowed, string Reason)
{
    public static RiskGateResult Allow() => new(true, string.Empty);
    public static RiskGateResult Block(string reason) => new(false, reason);
}

public static class PreTradeRiskGate
{
    public static RiskGateResult Evaluate(
        Symbol symbol,
        TradePlan plan,
        double maxSpreadPips,
        double maxRiskPercent,
        double maxLotsPerTrade = 0)
    {
        if (plan.VolumeInUnits < symbol.VolumeInUnitsMin)
            return RiskGateResult.Block("Calculated volume is below the broker minimum.");

        if (plan.VolumeInUnits > symbol.VolumeInUnitsMax)
            return RiskGateResult.Block("Calculated volume exceeds the broker maximum.");

        if (maxLotsPerTrade > 0 && plan.QuantityLots > maxLotsPerTrade)
            return RiskGateResult.Block($"Volume {plan.QuantityLots:F2} lots exceeds the prop limit {maxLotsPerTrade:F2} lots.");

        if (maxRiskPercent > 0 && plan.RiskPercent > maxRiskPercent)
            return RiskGateResult.Block($"Risk {plan.RiskPercent:F2}% exceeds the configured maximum {maxRiskPercent:F2}%.");

        if (maxSpreadPips > 0)
        {
            var spreadPips = symbol.Spread / symbol.PipSize;
            if (spreadPips > maxSpreadPips)
                return RiskGateResult.Block($"Spread {spreadPips:F2} pips exceeds the configured maximum {maxSpreadPips:F2} pips.");
        }

        return RiskGateResult.Allow();
    }
}
