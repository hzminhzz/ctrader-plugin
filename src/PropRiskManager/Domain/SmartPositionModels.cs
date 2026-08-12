using System;
using System.Collections.Generic;

namespace PropRiskManager.Domain;

public enum SmartPositionDirection
{
    Long,
    Short
}

public enum SmartPositionPhase
{
    MonitoringPreBreakEven
}

public sealed class SmartPositionSnapshot
{
    public int PositionId { get; init; }
    public string SymbolName { get; init; } = string.Empty;
    public SmartPositionDirection Direction { get; init; }
    public double EntryPrice { get; init; }
    public double VolumeInUnits { get; init; }
    public double? StopLoss { get; init; }
    public double? TakeProfit { get; init; }
    public DateTime ObservedAtUtc { get; init; }
    public bool IsOpen { get; init; }
}

public sealed class SmartPositionSettings
{
    public bool EnrollRequested { get; init; }
    public bool RemoveMonitoringRequested { get; init; }
}

public sealed class SmartPositionState
{
    public int PositionId { get; set; }
    public string SymbolName { get; set; } = string.Empty;
    public SmartPositionDirection Direction { get; set; }
    public double EntryPrice { get; set; }
    public double OriginalVolumeInUnits { get; set; }
    public double CurrentVolumeInUnits { get; set; }
    public double? InitialStopLoss { get; set; }
    public double? InitialTakeProfit { get; set; }
    public DateTime MonitoringStartedAtUtc { get; set; }
    public SmartPositionPhase Phase { get; set; } = SmartPositionPhase.MonitoringPreBreakEven;
}

public sealed class SmartPositionEvaluation
{
    public IReadOnlyList<string> Actions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Alerts { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    public SmartPositionState? NextState { get; init; }
}
