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
    MonitoringPreBreakEven,
    BreakEven,
    PostBreakEvenTrailing
}

public enum SmartAlertType
{
    PartialProfit,
    BreakEven,
    StopLoss
}

public enum SmartAlertState
{
    Armed,
    Triggered,
    Disarmed
}

public enum SmartPositionActionType
{
    ImproveStopLoss
}

public enum SmartStopActionReason
{
    PreBreakEvenTrailing,
    BreakEven,
    PostBreakEvenTrailing
}

public enum SmartActionExecutionStatus
{
    Pending,
    AcceptedAwaitingReconciliation,
    Rejected,
    NoOp
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
    public double? ObservedPrice { get; init; }
    public DateTime ObservedAtUtc { get; init; }
    public bool IsOpen { get; init; }
}

public sealed class SmartPositionSettings
{
    public bool EnrollRequested { get; init; }
    public bool RemoveMonitoringRequested { get; init; }
    public bool RemoveAlertsRequested { get; init; }
    public bool RearmAlertsRequested { get; init; }
    public double? PartialProfitTriggerPrice { get; init; }
    public double? BreakEvenTriggerPrice { get; init; }
    public double? StopLossTriggerPrice { get; init; }
    public double? VirtualStopLossPrice { get; init; }

    public bool ConfigureStopManagementRequested { get; init; }
    public bool FinancialStopManagementEnabled { get; init; }
    public bool PreBreakEvenTrailingEnabled { get; init; }
    public bool BreakEvenFinancialEnabled { get; init; }
    public bool PostBreakEvenTrailingEnabled { get; init; }
    public double? BreakEvenFinancialTriggerPrice { get; init; }
    public double? BreakEvenAdjustmentPriceDistance { get; init; }
    public double? PreBreakEvenTrailingPriceDistance { get; init; }
    public double? PostBreakEvenTrailingPriceDistance { get; init; }
    public double StopImprovementEpsilon { get; init; }
}

public sealed class SmartStopManagementPlan
{
    public bool Enabled { get; set; }
    public bool PreBreakEvenTrailingEnabled { get; set; }
    public bool BreakEvenEnabled { get; set; }
    public bool PostBreakEvenTrailingEnabled { get; set; }
    public double? BreakEvenTriggerPrice { get; set; }
    public double BreakEvenAdjustmentPriceDistance { get; set; }
    public double PreBreakEvenTrailingPriceDistance { get; set; }
    public double PostBreakEvenTrailingPriceDistance { get; set; }
    public double StopImprovementEpsilon { get; set; }
}

public sealed class SmartPositionAction
{
    public string ActionId { get; set; } = string.Empty;
    public SmartPositionActionType ActionType { get; set; }
    public SmartStopActionReason Reason { get; set; }
    public int PositionId { get; set; }
    public string SymbolName { get; set; } = string.Empty;
    public SmartPositionDirection Direction { get; set; }
    public double RequestedStopPrice { get; set; }
    public SmartPositionPhase TargetPhase { get; set; }
    public DateTime RequestedAtUtc { get; set; }
}

public sealed class SmartPendingStopAction
{
    public string ActionId { get; set; } = string.Empty;
    public SmartStopActionReason Reason { get; set; }
    public double RequestedStopPrice { get; set; }
    public SmartPositionPhase TargetPhase { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public SmartActionExecutionStatus ExecutionStatus { get; set; } = SmartActionExecutionStatus.Pending;
    public string DiagnosticError { get; set; } = string.Empty;
}

public sealed class SmartAlertDefinition
{
    public string AlertId { get; set; } = string.Empty;
    public int PositionId { get; set; }
    public string SymbolName { get; set; } = string.Empty;
    public SmartPositionDirection Direction { get; set; }
    public SmartAlertType AlertType { get; set; }
    public double TriggerPrice { get; set; }
    public bool IsVirtualStopLoss { get; set; }
    public SmartAlertState State { get; set; } = SmartAlertState.Armed;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? TriggeredAtUtc { get; set; }
}

public sealed class SmartAlertEvent
{
    public string EventId { get; set; } = string.Empty;
    public string AlertId { get; set; } = string.Empty;
    public int PositionId { get; set; }
    public string SymbolName { get; set; } = string.Empty;
    public SmartPositionDirection Direction { get; set; }
    public SmartAlertType AlertType { get; set; }
    public double TriggerPrice { get; set; }
    public double ObservedPrice { get; set; }
    public DateTime TriggeredAtUtc { get; set; }
    public string RequestedAction { get; set; } = "None";
    public string ActionResult { get; set; } = "MonitoringOnly";
    public string DiagnosticError { get; set; } = string.Empty;
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
    public double? CurrentStopLoss { get; set; }
    public double? CurrentTakeProfit { get; set; }
    public DateTime? LastReconciledAtUtc { get; set; }
    public DateTime MonitoringStartedAtUtc { get; set; }
    public SmartPositionPhase Phase { get; set; } = SmartPositionPhase.MonitoringPreBreakEven;
    public DateTime? PhaseChangedAtUtc { get; set; }
    public SmartStopManagementPlan StopManagement { get; set; } = new();
    public SmartPendingStopAction? PendingStopAction { get; set; }
    public List<SmartAlertDefinition> AlertDefinitions { get; set; } = new();
}

public sealed class SmartPositionEvaluation
{
    public IReadOnlyList<SmartPositionAction> Actions { get; init; } = Array.Empty<SmartPositionAction>();
    public IReadOnlyList<SmartAlertEvent> Alerts { get; init; } = Array.Empty<SmartAlertEvent>();
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
    public SmartPositionState? NextState { get; init; }
}
