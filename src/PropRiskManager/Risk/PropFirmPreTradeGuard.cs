using System;
using PropRiskManager.State;

namespace PropRiskManager.Risk;

public sealed record PropPreTradeGuardResult(
    bool Allowed,
    string Reason,
    double WorstCaseEquity,
    double BindingFloor,
    double SafetyBufferAmount,
    double ExistingRiskToStop,
    double ProposedRisk);

public static class PropFirmPreTradeGuard
{
    public static PropPreTradeGuardResult Evaluate(
        PropFirmSettings settings,
        PropGuardianSnapshot guardian,
        double currentEquity,
        double existingRiskToStop,
        double proposedRisk,
        int unprotectedExposureCount)
    {
        if (!settings.PreTradeLossRoomGuardEnabled)
            return Allow(currentEquity, double.NegativeInfinity, 0, existingRiskToStop, proposedRisk);

        if (settings.BlockIfExposureHasNoStop && unprotectedExposureCount > 0)
        {
            return new PropPreTradeGuardResult(
                false,
                $"{unprotectedExposureCount} existing position/order exposure(s) have no stop loss.",
                currentEquity,
                Math.Max(guardian.DailyFloor, guardian.TotalFloor),
                SafetyBuffer(settings),
                existingRiskToStop,
                proposedRisk);
        }

        var bindingFloor = Math.Max(guardian.DailyFloor, guardian.TotalFloor);
        if (double.IsNegativeInfinity(bindingFloor))
            return Allow(currentEquity, bindingFloor, 0, existingRiskToStop, proposedRisk);

        var buffer = SafetyBuffer(settings);
        var worstCaseEquity = currentEquity - Math.Max(0, existingRiskToStop) - Math.Max(0, proposedRisk);
        var requiredFloor = bindingFloor + buffer;
        if (worstCaseEquity < requiredFloor)
        {
            return new PropPreTradeGuardResult(
                false,
                $"Worst-case equity {worstCaseEquity:F2} would fall below protected floor {requiredFloor:F2}.",
                worstCaseEquity,
                bindingFloor,
                buffer,
                existingRiskToStop,
                proposedRisk);
        }

        return Allow(worstCaseEquity, bindingFloor, buffer, existingRiskToStop, proposedRisk);
    }

    private static double SafetyBuffer(PropFirmSettings settings)
        => Math.Max(0, settings.InitialBalance) * Math.Max(0, settings.SafetyBufferPercent) / 100.0;

    private static PropPreTradeGuardResult Allow(
        double worstCaseEquity,
        double bindingFloor,
        double buffer,
        double existingRisk,
        double proposedRisk)
        => new(true, string.Empty, worstCaseEquity, bindingFloor, buffer, existingRisk, proposedRisk);
}
