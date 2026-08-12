using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace PropRiskManager.Execution;

public sealed record ManagementResult(int Attempted, int Succeeded, string? LastError)
{
    public static ManagementResult Empty() => new(0, 0, null);
}

public static class PositionManagementService
{
    public static ManagementResult Close(IEnumerable<Position> positions)
    {
        var attempted = 0;
        var succeeded = 0;
        string? lastError = null;

        foreach (var position in positions.ToArray())
        {
            attempted++;
            var result = position.Close();
            if (result.IsSuccessful)
                succeeded++;
            else
                lastError = result.Error.ToString();
        }

        return new ManagementResult(attempted, succeeded, lastError);
    }

    public static ManagementResult Cancel(IEnumerable<PendingOrder> orders)
    {
        var attempted = 0;
        var succeeded = 0;
        string? lastError = null;

        foreach (var order in orders.ToArray())
        {
            attempted++;
            var result = order.Cancel();
            if (result.IsSuccessful)
                succeeded++;
            else
                lastError = result.Error.ToString();
        }

        return new ManagementResult(attempted, succeeded, lastError);
    }

    public static ManagementResult PartialClose(
        IEnumerable<Position> positions,
        double closePercent,
        Func<string, Symbol> getSymbol)
    {
        if (closePercent <= 0 || closePercent > 100)
            throw new ArgumentOutOfRangeException(nameof(closePercent), "Close percentage must be in (0, 100].");

        var attempted = 0;
        var succeeded = 0;
        string? lastError = null;

        foreach (var position in positions.ToArray())
        {
            attempted++;
            var symbol = getSymbol(position.SymbolName);
            var targetRemaining = position.VolumeInUnits * (1.0 - closePercent / 100.0);

            TradeResult result;
            if (closePercent >= 100 || targetRemaining < symbol.VolumeInUnitsMin)
            {
                result = position.Close();
            }
            else
            {
                var normalizedRemaining = symbol.NormalizeVolumeInUnits(targetRemaining, RoundingMode.Down);
                if (normalizedRemaining < symbol.VolumeInUnitsMin)
                {
                    result = position.Close();
                }
                else if (normalizedRemaining >= position.VolumeInUnits)
                {
                    lastError = $"Partial close for position {position.Id} rounds to no volume change.";
                    continue;
                }
                else
                {
                    result = position.ModifyVolume(normalizedRemaining);
                }
            }

            if (result.IsSuccessful)
                succeeded++;
            else
                lastError = result.Error.ToString();
        }

        return new ManagementResult(attempted, succeeded, lastError);
    }

    public static ManagementResult MoveToBreakEven(
        IEnumerable<Position> positions,
        double offsetPips,
        Func<string, Symbol> getSymbol)
    {
        var attempted = 0;
        var succeeded = 0;
        string? lastError = null;

        foreach (var position in positions.ToArray())
        {
            attempted++;
            var symbol = getSymbol(position.SymbolName);
            var desiredStop = position.TradeType == TradeType.Buy
                ? position.EntryPrice + offsetPips * symbol.PipSize
                : position.EntryPrice - offsetPips * symbol.PipSize;

            if (!ImprovesStop(position, desiredStop, symbol.TickSize))
            {
                succeeded++;
                continue;
            }

            var result = position.ModifyStopLossPrice(desiredStop);
            if (result.IsSuccessful)
                succeeded++;
            else
                lastError = result.Error.ToString();
        }

        return new ManagementResult(attempted, succeeded, lastError);
    }

    private static bool ImprovesStop(Position position, double desiredStop, double tickSize)
    {
        if (!position.StopLoss.HasValue)
            return true;

        return position.TradeType == TradeType.Buy
            ? desiredStop > position.StopLoss.Value + tickSize / 2.0
            : desiredStop < position.StopLoss.Value - tickSize / 2.0;
    }
}
