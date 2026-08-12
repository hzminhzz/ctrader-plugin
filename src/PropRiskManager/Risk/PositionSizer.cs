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
        Func<double, double>? roundTripCommissionEstimator = null,
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

        double CommissionForVolume(double volumeInUnits)
        {
            if (roundTripCommissionEstimator != null)
                return Math.Max(0, roundTripCommissionEstimator(volumeInUnits));

            var lotsForCommission = symbol.VolumeInUnitsToQuantity(volumeInUnits);
            return Math.Max(0, commissionPerLotRoundTrip) * lotsForCommission;
        }

        double TotalRisk(double volumeInUnits)
            => symbol.AmountRisked(volumeInUnits, stopLossPips) + CommissionForVolume(volumeInUnits);

        double volume;
        if (riskMode == RiskMode.FixedLots)
        {
            var rawVolume = symbol.QuantityToVolumeInUnits(riskInput);
            volume = symbol.NormalizeVolumeInUnits(rawVolume, roundingMode);
        }
        else
        {
            if (riskBudget <= 0)
                throw new ArgumentException("Risk budget must be positive.", nameof(riskInput));

            volume = FindHighestVolumeWithinRisk(symbol, riskBudget, TotalRisk, roundingMode);
        }

        if (volume < symbol.VolumeInUnitsMin)
            throw new InvalidOperationException("Calculated volume is below the broker minimum.");
        if (volume > symbol.VolumeInUnitsMax)
            volume = symbol.VolumeInUnitsMax;

        var lots = symbol.VolumeInUnitsToQuantity(volume);
        var commissionAmount = CommissionForVolume(volume);
        var riskAmount = TotalRisk(volume);
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

    private static double FindHighestVolumeWithinRisk(
        Symbol symbol,
        double riskBudget,
        Func<double, double> totalRisk,
        RoundingMode roundingMode)
    {
        var min = symbol.VolumeInUnitsMin;
        var max = symbol.VolumeInUnitsMax;
        var step = symbol.VolumeInUnitsStep;

        if (min <= 0 || max < min || step <= 0)
            throw new InvalidOperationException("Symbol volume constraints are invalid.");

        if (totalRisk(min) > riskBudget)
            throw new InvalidOperationException("Calculated volume is below the broker minimum.");

        if (totalRisk(max) <= riskBudget)
            return max;

        var maxSteps = (long)Math.Floor((max - min) / step);
        long low = 0;
        long high = maxSteps;
        var best = min;

        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            var candidate = symbol.NormalizeVolumeInUnits(min + mid * step, roundingMode);
            var candidateRisk = totalRisk(candidate);

            if (candidateRisk <= riskBudget)
            {
                best = candidate;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best;
    }
}
