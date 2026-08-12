using System;
using System.Collections.Generic;

namespace PropRiskManager.Domain;

public sealed record SmartManagementEnrollmentResolution(SmartPositionSettings Settings, IReadOnlyList<string> Diagnostics);

public static class SmartManagementEnrollmentResolver
{
    public static SmartManagementEnrollmentResolution Resolve(SmartManagementParameters parameters, SmartPositionSnapshot snapshot)
    {
        var diagnostics = new List<string>();
        if (!SmartManagementProfileResolver.ValidateParameters(parameters))
        {
            diagnostics.Add("Management parameters are invalid; enrollment remains monitoring-only.");
            return new SmartManagementEnrollmentResolution(new SmartPositionSettings { EnrollRequested = true }, diagnostics);
        }
        return parameters.Mode == SmartManagementMode.Percentage
            ? ResolvePercentage(parameters, snapshot, diagnostics)
            : ResolveResolvedPoints(parameters, snapshot, diagnostics);
    }

    private static SmartManagementEnrollmentResolution ResolveResolvedPoints(SmartManagementParameters p, SmartPositionSnapshot s, ICollection<string> d)
    {
        var sign = SmartManagementSemantics.DirectionSign(s.Direction);
        var pp = IsFinitePositive(p.PartialProfitSpacing) ? s.EntryPrice + sign * p.PartialProfitSpacing : (double?)null;
        var be = IsFinitePositive(p.BreakEvenTrigger) ? s.EntryPrice + sign * p.BreakEvenTrigger : (double?)null;
        return new SmartManagementEnrollmentResolution(new SmartPositionSettings
        {
            EnrollRequested = true, ManagementMode = SmartManagementMode.Points,
            PartialProfitTriggerPrice = pp, BreakEvenTriggerPrice = be,
            ConfigureStopManagementRequested = true, FinancialStopManagementEnabled = p.FinancialStopManagementEnabled,
            PreBreakEvenTrailingEnabled = p.PreBreakEvenTrailingEnabled, PreBreakEvenTrailingPriceDistance = p.PreBreakEvenTrailingAdjustment,
            BreakEvenFinancialEnabled = p.BreakEvenEnabled, BreakEvenFinancialTriggerPrice = be, BreakEvenAdjustmentPriceDistance = p.BreakEvenAdjustment,
            PostBreakEvenTrailingEnabled = p.PostBreakEvenTrailingEnabled, PostBreakEvenTrailingPriceDistance = p.PostBreakEvenTrailingAdjustment,
            ConfigurePartialProfitRequested = true, FinancialPartialProfitEnabled = p.FinancialPartialProfitEnabled,
            MultiPartialProfitEnabled = p.MultiPartialProfitEnabled, PartialProfitSpacingPriceDistance = p.PartialProfitSpacing,
            PartialProfitClosePercent = p.PartialProfitClosePercent
        }, new List<string>(d));
    }

    private static SmartManagementEnrollmentResolution ResolvePercentage(SmartManagementParameters p, SmartPositionSnapshot s, ICollection<string> d)
    {
        double? pp = null, be = null;
        if (HasValidInitialTakeProfit(s))
        {
            if (p.PartialProfitSpacing <= 100) pp = SmartManagementSemantics.FavorablePercentageTrigger(s.Direction, s.EntryPrice, s.TakeProfit!.Value, p.PartialProfitSpacing);
            else d.Add("Percentage Partial Profit spacing must be at most 100%; the PP alert/financial schedule is unavailable.");
            if (p.BreakEvenTrigger <= 100) be = SmartManagementSemantics.FavorablePercentageTrigger(s.Direction, s.EntryPrice, s.TakeProfit!.Value, p.BreakEvenTrigger);
            else d.Add("Percentage Break Even trigger must be at most 100%; the BE alert/financial trigger is unavailable.");
        }
        else d.Add("Percentage profit-side thresholds require a valid captured initial TP; PP/BE thresholds are unavailable.");

        return new SmartManagementEnrollmentResolution(new SmartPositionSettings
        {
            EnrollRequested = true, ManagementMode = SmartManagementMode.Percentage,
            PartialProfitTriggerPrice = pp, BreakEvenTriggerPrice = be,
            ConfigureStopManagementRequested = true, FinancialStopManagementEnabled = p.FinancialStopManagementEnabled,
            PreBreakEvenTrailingEnabled = p.PreBreakEvenTrailingEnabled, PreBreakEvenTrailingPercentage = p.PreBreakEvenTrailingAdjustment,
            BreakEvenFinancialEnabled = p.BreakEvenEnabled && be.HasValue, BreakEvenTriggerPercentage = be.HasValue ? p.BreakEvenTrigger : null, BreakEvenAdjustmentPercentage = p.BreakEvenAdjustment,
            PostBreakEvenTrailingEnabled = p.PostBreakEvenTrailingEnabled, PostBreakEvenTrailingPercentage = p.PostBreakEvenTrailingAdjustment,
            ConfigurePartialProfitRequested = true, FinancialPartialProfitEnabled = p.FinancialPartialProfitEnabled && pp.HasValue,
            MultiPartialProfitEnabled = p.MultiPartialProfitEnabled, PartialProfitSpacingPercentage = pp.HasValue ? p.PartialProfitSpacing : null,
            PartialProfitClosePercent = p.PartialProfitClosePercent
        }, new List<string>(d));
    }

    private static bool HasValidInitialTakeProfit(SmartPositionSnapshot s) =>
        s.TakeProfit.HasValue && IsFinitePositive(s.TakeProfit.Value) &&
        (s.Direction == SmartPositionDirection.Long ? s.TakeProfit.Value > s.EntryPrice : s.TakeProfit.Value < s.EntryPrice);
    private static bool IsFinitePositive(double v) => v > 0 && !double.IsNaN(v) && !double.IsInfinity(v);
}
