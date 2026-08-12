using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using PropRiskManager.Domain;
using PropRiskManager.Execution;
using PropRiskManager.State;

namespace PropRiskManager.Protection;

public static class PartialExitEngine
{
    public static void Apply(
        IEnumerable<Position> positions,
        PluginSettings settings,
        AccountRuntimeState runtimeState,
        Func<string, Symbol> getSymbol)
    {
        settings.EnsurePartialExitDefaults();
        runtimeState.PositionAutomation ??= new Dictionary<int, PositionAutomationState>();

        var openPositions = positions.ToArray();
        var openIds = openPositions.Select(p => p.Id).ToHashSet();
        foreach (var staleId in runtimeState.PositionAutomation.Keys.Where(id => !openIds.Contains(id)).ToArray())
            runtimeState.PositionAutomation.Remove(staleId);

        foreach (var position in openPositions)
        {
            if (!runtimeState.PositionAutomation.TryGetValue(position.Id, out var state))
            {
                state = new PositionAutomationState { OriginalVolumeInUnits = position.VolumeInUnits };
                runtimeState.PositionAutomation[position.Id] = state;
            }

            if (state.OriginalVolumeInUnits <= 0)
                state.OriginalVolumeInUnits = position.VolumeInUnits;

            var symbol = getSymbol(position.SymbolName);
            var favorablePips = position.TradeType == TradeType.Buy
                ? (symbol.Bid - position.EntryPrice) / symbol.PipSize
                : (position.EntryPrice - symbol.Ask) / symbol.PipSize;
            var adversePips = position.TradeType == TradeType.Buy
                ? (position.EntryPrice - symbol.Bid) / symbol.PipSize
                : (symbol.Ask - position.EntryPrice) / symbol.PipSize;

            ProcessLevels(position, symbol, state, settings.PartialTakeProfits, true, favorablePips);
            ProcessLevels(position, symbol, state, settings.PartialStopLosses, false, adversePips);
        }
    }

    private static void ProcessLevels(
        Position position,
        Symbol symbol,
        PositionAutomationState state,
        IReadOnlyList<PartialExitLevelSettings> levels,
        bool takeProfitSide,
        double currentDistancePips)
    {
        var fired = takeProfitSide ? state.FiredTakeProfitLevels : state.FiredStopLossLevels;

        for (var index = 0; index < levels.Count && index < 5; index++)
        {
            var level = levels[index];
            if (!level.Enabled || fired.Contains(index) || level.TriggerValue <= 0 || level.CloseValue <= 0)
                continue;

            var triggerPips = ResolveTriggerPips(position, symbol, level, takeProfitSide);
            if (!triggerPips.HasValue || currentDistancePips < triggerPips.Value)
                continue;

            var closeVolume = ResolveCloseVolume(position, symbol, state, level);
            if (closeVolume <= 0)
                continue;

            var volumeBefore = position.VolumeInUnits;
            var result = PositionManagementService.CloseVolume(position, closeVolume, symbol);
            if (!result.IsSuccessful)
                continue;

            fired.Add(index);
            if (closeVolume >= volumeBefore || volumeBefore - closeVolume < symbol.VolumeInUnitsMin)
                break;
        }
    }

    private static double? ResolveTriggerPips(
        Position position,
        Symbol symbol,
        PartialExitLevelSettings level,
        bool takeProfitSide)
    {
        if (level.TriggerMode == PartialTriggerMode.Pips)
            return level.TriggerValue;

        double? protectionPrice = takeProfitSide ? position.TakeProfit : position.StopLoss;
        if (!protectionPrice.HasValue)
            return null;

        var fullDistancePips = Math.Abs(protectionPrice.Value - position.EntryPrice) / symbol.PipSize;
        return fullDistancePips * level.TriggerValue / 100.0;
    }

    private static double ResolveCloseVolume(
        Position position,
        Symbol symbol,
        PositionAutomationState state,
        PartialExitLevelSettings level)
    {
        return level.CloseMode switch
        {
            PartialCloseMode.PercentOriginal => state.OriginalVolumeInUnits * level.CloseValue / 100.0,
            PartialCloseMode.PercentRemaining => position.VolumeInUnits * level.CloseValue / 100.0,
            PartialCloseMode.FixedLots => symbol.QuantityToVolumeInUnits(level.CloseValue),
            _ => 0
        };
    }
}
