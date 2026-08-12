using cAlgo.API;
using PropRiskManager.Domain;

namespace PropRiskManager.Risk;

public static class PositionSizer
{
    public static TradePlan BuildPlan(
        Symbol symbol,
        TradeType tradeType,
        double entryPrice,
        double stopLossPrice,
        double? takeProfitPrice,
        double accountSize,
        double riskPercent,
        RoundingMode roundingMode = RoundingMode.Down)
    {
        if (accountSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(accountSize));
        if (riskPercent <= 0)
            throw new ArgumentOutOfRangeException(nameof(riskPercent));

        var stopLossPips = Math.Abs(entryPrice - stopLossPrice) / symbol.PipSize;
        if (stopLossPips <= 0)
            throw new ArgumentException("Stop loss must be different from entry price.", nameof(stopLossPrice));

        var stopIsValid = tradeType == TradeType.Buy
            ? stopLossPrice < entryPrice
            : stopLossPrice > entryPrice;
        if (!stopIsValid)
            throw new ArgumentException("Stop loss is on the wrong side of the entry price.", nameof(stopLossPrice));

        var riskAmount = accountSize * riskPercent / 100.0;
        var rawVolume = symbol.VolumeForFixedRisk(riskAmount, stopLossPips);
        var volume = symbol.NormalizeVolumeInUnits(rawVolume, roundingMode);

        volume = Math.Max(symbol.VolumeInUnitsMin, Math.Min(symbol.VolumeInUnitsMax, volume));

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
            SymbolName = symbol.Name,
            EntryPrice = entryPrice,
            StopLossPrice = stopLossPrice,
            TakeProfitPrice = takeProfitPrice,
            RiskPercent = riskPercent,
            RiskAmount = riskAmount,
            StopLossPips = stopLossPips,
            TakeProfitPips = takeProfitPips,
            VolumeInUnits = volume,
            QuantityLots = symbol.VolumeInUnitsToQuantity(volume),
            RewardRiskRatio = takeProfitPips.HasValue ? takeProfitPips.Value / stopLossPips : 0
        };
    }
}
