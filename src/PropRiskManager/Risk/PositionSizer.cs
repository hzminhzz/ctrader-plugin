using cAlgo.API;
using cAlgo.API.Internals;
using PropRiskManager.Domain;

namespace PropRiskManager.Risk;

public static class PositionSizer
{
    public static TradePlan BuildPlan(
        Symbol symbol,
        TradeType tradeType,
        OrderKind orderKind,
        RiskMode riskMode,
        double riskInput,
        double entryPrice,
        double stopLossPrice,
        double? takeProfitPrice,
        double equity,
        double balance,
        double freeMargin,
        double commissionPerLotRoundTrip,
        RoundingMode roundingMode = RoundingMode.Down)
    {
        if (riskInput <= 0)
            throw new ArgumentOutOfRangeException(nameof(riskInput));

        var stopLossPips = Math.Abs(entryPrice - stopLossPrice) / symbol.PipSize;
        if (stopLossPips <= 0)
            throw new ArgumentException("Stop loss must be different from entry price.", nameof(stopLossPrice));

        var stopIsValid = tradeType == TradeType.Buy
            ? stopLossPrice < entryPrice
            : stopLossPrice > entryPrice;
        if (!stopIsValid)
            throw new ArgumentException("Stop loss is on the wrong side of the entry price.", nameof(stopLossPrice));

        var riskBudget = riskMode switch
        {
            RiskMode.PercentEquity => equity * riskInput / 100.0,
            RiskMode.PercentBalance => balance * riskInput / 100.0,
            RiskMode.PercentFreeMargin => freeMargin * riskInput / 100.0,
            RiskMode.FixedAmount => riskInput,
            RiskMode.FixedLots => 0.0,
            _ => throw new ArgumentOutOfRangeException(nameof(riskMode))
        };

        double rawVolume;
        if (riskMode == RiskMode.FixedLots)
        {
            rawVolume = symbol.QuantityToVolumeInUnits(riskInput);
        }
        else
        {
            if (riskBudget <= 0)
                throw new ArgumentException("Risk budget must be positive.", nameof(riskInput));

            var commissionPerUnit = symbol.LotSize > 0
                ? Math.Max(0, commissionPerLotRoundTrip) / symbol.LotSize
                : 0.0;
            var riskPerUnit = stopLossPips * symbol.PipValue + commissionPerUnit;
            if (riskPerUnit <= 0)
                throw new InvalidOperationException("Unable to calculate risk per unit for this symbol.");

            rawVolume = riskBudget / riskPerUnit;
        }

        var volume = symbol.NormalizeVolumeInUnits(rawVolume, roundingMode);
        if (volume < symbol.VolumeInUnitsMin)
            throw new InvalidOperationException("Calculated volume is below the broker minimum.");
        if (volume > symbol.VolumeInUnitsMax)
            volume = symbol.VolumeInUnitsMax;

        var lots = symbol.VolumeInUnitsToQuantity(volume);
        var commissionAmount = Math.Max(0, commissionPerLotRoundTrip) * lots;
        var riskAmount = symbol.AmountRisked(volume, stopLossPips) + commissionAmount;
        var referenceCapital = riskMode switch
        {
            RiskMode.PercentEquity => equity,
            RiskMode.PercentBalance => balance,
            RiskMode.PercentFreeMargin => freeMargin,
            _ => equity
        };
        var riskPercent = referenceCapital > 0 ? riskAmount / referenceCapital * 100.0 : 0.0;

        double? takeProfitPips = null;
        if (takeProfitPrice.HasValue)
        {
            var validTp = tradeType == TradeType.Buy
                ? takeProfitPrice.Value > entryPrice
                : takeProfitPrice.Value < entryPrice;

            if (validTp)
                takeProfitPips = Math.Abs(takeProfitPrice.Value - entryPrice) / symbol.PipSize;
        }

        return new TradePlan
        {
            TradeType = tradeType,
            OrderKind = orderKind,
            RiskMode = riskMode,
            SymbolName = symbol.Name,
            EntryPrice = entryPrice,
            StopLossPrice = stopLossPrice,
            TakeProfitPrice = takeProfitPrice,
            RiskInput = riskInput,
            RiskPercent = riskPercent,
            RiskAmount = riskAmount,
            CommissionAmount = commissionAmount,
            StopLossPips = stopLossPips,
            TakeProfitPips = takeProfitPips,
            VolumeInUnits = volume,
            QuantityLots = lots,
            RewardRiskRatio = takeProfitPips.HasValue ? takeProfitPips.Value / stopLossPips : 0
        };
    }
}
