using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;

namespace PropRiskManager.Risk;

public sealed record PortfolioRiskSnapshot(double RiskToStop, int UnprotectedExposureCount);

public static class PortfolioRiskCalculator
{
    public static PortfolioRiskSnapshot Calculate(
        IEnumerable<Position> positions,
        IEnumerable<PendingOrder> pendingOrders,
        Func<string, Symbol> getSymbol,
        double commissionPerLotRoundTrip,
        Func<Symbol, double, double, double, double>? automaticRoundTripCommission = null)
    {
        var risk = 0.0;
        var unprotected = 0;
        var commissionPerLot = Math.Max(0, commissionPerLotRoundTrip);

        foreach (var position in positions)
        {
            var symbol = getSymbol(position.SymbolName);
            if (!position.StopLoss.HasValue)
            {
                unprotected++;
                continue;
            }

            var distancePips = position.TradeType == TradeType.Buy
                ? Math.Max(0, symbol.Bid - position.StopLoss.Value) / symbol.PipSize
                : Math.Max(0, position.StopLoss.Value - symbol.Ask) / symbol.PipSize;

            risk += symbol.AmountRisked(position.VolumeInUnits, distancePips);
            risk += automaticRoundTripCommission != null
                ? automaticRoundTripCommission(
                    symbol,
                    position.VolumeInUnits,
                    position.TradeType == TradeType.Buy ? symbol.Bid : symbol.Ask,
                    position.StopLoss.Value) / 2.0
                : commissionPerLot * symbol.VolumeInUnitsToQuantity(position.VolumeInUnits) / 2.0;
        }

        foreach (var order in pendingOrders)
        {
            var symbol = getSymbol(order.SymbolName);
            if (!order.StopLossPips.HasValue)
            {
                unprotected++;
                continue;
            }

            risk += symbol.AmountRisked(order.VolumeInUnits, order.StopLossPips.Value);

            var stopPrice = order.TradeType == TradeType.Buy
                ? order.TargetPrice - order.StopLossPips.Value * symbol.PipSize
                : order.TargetPrice + order.StopLossPips.Value * symbol.PipSize;
            risk += automaticRoundTripCommission != null
                ? automaticRoundTripCommission(symbol, order.VolumeInUnits, order.TargetPrice, stopPrice)
                : commissionPerLot * symbol.VolumeInUnitsToQuantity(order.VolumeInUnits);
        }

        return new PortfolioRiskSnapshot(risk, unprotected);
    }
}
