using System.Collections.Generic;

namespace PropRiskManager.State;

public sealed class PositionAutomationState
{
    public double OriginalVolumeInUnits { get; set; }
    public List<int> FiredTakeProfitLevels { get; set; } = new();
    public List<int> FiredStopLossLevels { get; set; } = new();
}
