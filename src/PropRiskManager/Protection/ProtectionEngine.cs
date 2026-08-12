using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using PropRiskManager.Domain;
using PropRiskManager.Execution;
using PropRiskManager.State;

namespace PropRiskManager.Protection;

public static class ProtectionEngine
{
    public static void Apply(
        IEnumerable<Position> positions,
        PluginSettings settings,
        Func<string, Symbol> getSymbol)
    {
        foreach (var position in positions.ToArray())
        {
            var symbol = getSymbol(position.SymbolName);

            if (settings.BreakEvenEnabled)
                ApplyBreakEven(position, symbol, settings.BreakEvenTriggerPips, settings.BreakEvenOffsetPips);

            if (!settings.TrailingEnabled)
                continue;

            if (settings.TrailingMode == TrailingMode.Server)
                ApplyServerTrailing(position);
            else
                ApplyCustomTrailing(position, symbol, settings.TrailingPips);
        }
    }

    private static void ApplyBreakEven(Position position, Symbol symbol, double triggerPips, double offsetPips)
    {
        if (triggerPips < 0 || position.Pips < triggerPips)
            return;

        var desiredStop = position.TradeType == TradeType.Buy
            ? position.EntryPrice + offsetPips * symbol.PipSize
            : position.EntryPrice - offsetPips * symbol.PipSize;

        PositionManagementService.ImproveStop(position, desiredStop, symbol);
    }

    private static void ApplyServerTrailing(Position position)
    {
        if (!position.StopLoss.HasValue || position.HasTrailingStop)
            return;

        position.ModifyTrailingStop(true);
    }

    private static void ApplyCustomTrailing(Position position, Symbol symbol, double trailingPips)
    {
        if (trailingPips <= 0)
            return;

        if (position.HasTrailingStop)
        {
            var disabled = position.ModifyTrailingStop(false);
            if (!disabled.IsSuccessful)
                return;
        }

        var desiredStop = position.TradeType == TradeType.Buy
            ? symbol.Bid - trailingPips * symbol.PipSize
            : symbol.Ask + trailingPips * symbol.PipSize;

        PositionManagementService.ImproveStop(position, desiredStop, symbol);
    }
}
