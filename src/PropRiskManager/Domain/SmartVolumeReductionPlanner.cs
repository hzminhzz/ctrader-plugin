using System;

namespace PropRiskManager.Domain;

public enum SmartVolumeReductionDecisionType
{
    NoChange,
    ModifyRemaining,
    CloseAll
}

public sealed record SmartVolumeReductionDecision(
    SmartVolumeReductionDecisionType DecisionType,
    double NormalizedRemainingVolumeInUnits);

public static class SmartVolumeReductionPlanner
{
    public static SmartVolumeReductionDecision Decide(
        double currentVolumeInUnits,
        double requestedCloseVolumeInUnits,
        double minimumVolumeInUnits,
        double normalizedRemainingVolumeInUnits)
    {
        if (!IsFinitePositive(currentVolumeInUnits)) throw new ArgumentOutOfRangeException(nameof(currentVolumeInUnits));
        if (!IsFinitePositive(requestedCloseVolumeInUnits)) throw new ArgumentOutOfRangeException(nameof(requestedCloseVolumeInUnits));
        if (!IsFinitePositive(minimumVolumeInUnits)) throw new ArgumentOutOfRangeException(nameof(minimumVolumeInUnits));
        if (!IsFiniteNonNegative(normalizedRemainingVolumeInUnits)) throw new ArgumentOutOfRangeException(nameof(normalizedRemainingVolumeInUnits));
        if (requestedCloseVolumeInUnits >= currentVolumeInUnits)
            return new SmartVolumeReductionDecision(SmartVolumeReductionDecisionType.CloseAll, 0);
        var requestedRemaining = currentVolumeInUnits - requestedCloseVolumeInUnits;
        if (requestedRemaining < minimumVolumeInUnits || normalizedRemainingVolumeInUnits < minimumVolumeInUnits)
            return new SmartVolumeReductionDecision(SmartVolumeReductionDecisionType.CloseAll, 0);
        if (normalizedRemainingVolumeInUnits >= currentVolumeInUnits)
            return new SmartVolumeReductionDecision(SmartVolumeReductionDecisionType.NoChange, currentVolumeInUnits);
        return new SmartVolumeReductionDecision(SmartVolumeReductionDecisionType.ModifyRemaining, normalizedRemainingVolumeInUnits);
    }

    private static bool IsFinitePositive(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool IsFiniteNonNegative(double value) => value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
