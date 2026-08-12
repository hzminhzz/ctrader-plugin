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

public enum StopModificationStatus
{
    NoOp,
    Succeeded,
    Failed
}

public sealed record StopModificationResult(
    StopModificationStatus Status,
    double NormalizedRequestedStopPrice,
    string? Error);

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
            var closeVolume = position.VolumeInUnits * closePercent / 100.0;
            var result = CloseVolume(position, closeVolume, symbol);
            if (result == null)
            {
                lastError = $"Position {position.Id}: requested partial close rounds to no volume change.";
                continue;
            }

            if (result.IsSuccessful)
                succeeded++;
            else
                lastError = result.Error.ToString();
        }

        return new ManagementResult(attempted, succeeded, lastError);
    }

    public static TradeResult? CloseVolume(Position position, double closeVolumeInUnits, Symbol symbol)
    {
        if (closeVolumeInUnits <= 0)
            throw new ArgumentOutOfRangeException(nameof(closeVolumeInUnits));

        if (closeVolumeInUnits >= position.VolumeInUnits)
            return position.Close();

        var requestedRemaining = position.VolumeInUnits - closeVolumeInUnits;
        if (requestedRemaining < symbol.VolumeInUnitsMin)
            return position.Close();

        var normalizedRemaining = symbol.NormalizeVolumeInUnits(requestedRemaining, RoundingMode.Down);
        if (normalizedRemaining < symbol.VolumeInUnitsMin)
            return position.Close();

        if (normalizedRemaining >= position.VolumeInUnits)
            return null;

        return position.ModifyVolume(normalizedRemaining);
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
            var stopResult = ImproveStop(position, desiredStop, symbol);
            if (stopResult.Status is StopModificationStatus.Succeeded or StopModificationStatus.NoOp)
                succeeded++;
            else
                lastError = stopResult.Error;
        }

        return new ManagementResult(attempted, succeeded, lastError);
    }

    public static StopModificationResult ImproveStop(Position position, double desiredStop, Symbol symbol)
    {
        if (double.IsNaN(desiredStop) || double.IsInfinity(desiredStop) || desiredStop <= 0)
            return new StopModificationResult(StopModificationStatus.Failed, desiredStop, "Requested stop price must be finite and positive.");

        var normalizedStop = Math.Round(desiredStop, symbol.Digits);
        if (!ImprovesStop(position, normalizedStop, symbol.TickSize))
            return new StopModificationResult(StopModificationStatus.NoOp, normalizedStop, null);

        var result = position.ModifyStopLossPrice(normalizedStop);
        return result.IsSuccessful
            ? new StopModificationResult(StopModificationStatus.Succeeded, normalizedStop, null)
            : new StopModificationResult(StopModificationStatus.Failed, normalizedStop, result.Error.ToString());
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
