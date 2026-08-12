using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PropRiskManager.Domain;

public static class SmartPositionEngine
{
    public static SmartPositionEvaluation Evaluate(
        SmartPositionSnapshot positionSnapshot,
        SmartPositionSettings settings,
        SmartPositionState? persistedState)
    {
        if (settings.RemoveMonitoringRequested)
            return new SmartPositionEvaluation { NextState = null };

        if (persistedState is null)
        {
            if (!settings.EnrollRequested)
                return new SmartPositionEvaluation { NextState = null };

            var diagnostics = ValidateEnrollment(positionSnapshot);
            if (diagnostics.Count > 0)
                return new SmartPositionEvaluation { Diagnostics = diagnostics, NextState = null };

            var state = CreateInitialState(positionSnapshot);
            diagnostics.AddRange(CaptureReferenceDiagnostics(positionSnapshot));
            diagnostics.AddRange(ConfigureAlerts(state, positionSnapshot, settings, rearmExisting: false));
            if (settings.ConfigureStopManagementRequested)
                diagnostics.AddRange(ConfigureStopManagement(state, settings));
            if (settings.ConfigurePartialProfitRequested)
                diagnostics.AddRange(ConfigurePartialProfit(state, settings));

            return new SmartPositionEvaluation { Diagnostics = diagnostics, NextState = state };
        }

        if (persistedState.PositionId != positionSnapshot.PositionId)
        {
            return new SmartPositionEvaluation
            {
                Diagnostics = new[] { "Persisted state position identity does not match the supplied snapshot." },
                NextState = Clone(persistedState)
            };
        }

        if (!positionSnapshot.IsOpen)
            return new SmartPositionEvaluation { NextState = null };

        var nextState = Clone(persistedState);
        var diagnosticsResult = ValidateReconciliationIdentity(positionSnapshot, persistedState);
        if (diagnosticsResult.Count > 0)
            return new SmartPositionEvaluation { Diagnostics = diagnosticsResult, NextState = nextState };

        ReconcileBrokerState(nextState, positionSnapshot, diagnosticsResult);

        if (settings.ConfigureStopManagementRequested)
            diagnosticsResult.AddRange(ConfigureStopManagement(nextState, settings));
        if (settings.ConfigurePartialProfitRequested)
            diagnosticsResult.AddRange(ConfigurePartialProfit(nextState, settings));
        if (settings.RetryRejectedPartialProfitRequested &&
            nextState.PendingPartialProfitAction?.ExecutionStatus is SmartActionExecutionStatus.Rejected or SmartActionExecutionStatus.NoOp)
            nextState.PendingPartialProfitAction = null;

        if (settings.RemoveAlertsRequested)
        {
            foreach (var definition in nextState.AlertDefinitions)
            {
                if (definition.State == SmartAlertState.Armed)
                    definition.State = SmartAlertState.Disarmed;
            }
        }
        else if (settings.RearmAlertsRequested)
        {
            diagnosticsResult.AddRange(ConfigureAlerts(nextState, positionSnapshot, settings, rearmExisting: true));
        }

        var events = EvaluateAlerts(nextState, positionSnapshot);
        var stopActions = EvaluateFinancialStopManagement(nextState, positionSnapshot, diagnosticsResult);
        var partialActions = EvaluateFirstPartialProfit(nextState, positionSnapshot, diagnosticsResult);
        var actions = stopActions.Concat(partialActions).ToArray();

        return new SmartPositionEvaluation
        {
            Actions = actions,
            Alerts = events,
            Diagnostics = diagnosticsResult,
            NextState = nextState
        };
    }

    public static SmartPositionState RecordStopActionResult(
        SmartPositionState persistedState,
        string actionId,
        SmartActionExecutionStatus status,
        double? normalizedRequestedStopPrice = null,
        string? diagnosticError = null)
    {
        var nextState = Clone(persistedState);
        var pending = nextState.PendingStopAction;
        if (pending == null || !string.Equals(pending.ActionId, actionId, StringComparison.Ordinal))
            return nextState;

        if (normalizedRequestedStopPrice.HasValue && IsFinitePositive(normalizedRequestedStopPrice.Value))
            pending.RequestedStopPrice = normalizedRequestedStopPrice.Value;
        pending.ExecutionStatus = status;
        pending.DiagnosticError = diagnosticError ?? string.Empty;
        return nextState;
    }

    public static SmartPositionState RecordPartialProfitActionResult(
        SmartPositionState persistedState,
        string actionId,
        SmartActionExecutionStatus status,
        string? diagnosticError = null)
    {
        var nextState = Clone(persistedState);
        var pending = nextState.PendingPartialProfitAction;
        if (pending == null || !string.Equals(pending.ActionId, actionId, StringComparison.Ordinal))
            return nextState;

        pending.ExecutionStatus = status;
        pending.DiagnosticError = diagnosticError ?? string.Empty;
        return nextState;
    }

    public static int CountArmedAlerts(IEnumerable<SmartPositionState> states) =>
        states.Sum(state => state.AlertDefinitions.Count(definition => definition.State == SmartAlertState.Armed));

    private static List<string> ValidateEnrollment(SmartPositionSnapshot snapshot)
    {
        var diagnostics = new List<string>();
        if (!snapshot.IsOpen) diagnostics.Add("Only open positions can be enrolled.");
        if (snapshot.PositionId <= 0) diagnostics.Add("Position identity is required for enrollment.");
        if (string.IsNullOrWhiteSpace(snapshot.SymbolName)) diagnostics.Add("Symbol name is required for enrollment.");
        if (!IsFinitePositive(snapshot.EntryPrice)) diagnostics.Add("Entry price must be a finite positive value.");
        if (!IsFinitePositive(snapshot.VolumeInUnits)) diagnostics.Add("Position volume must be a finite positive value.");
        if (snapshot.ObservedAtUtc == default) diagnostics.Add("Observation time is required for enrollment.");
        return diagnostics;
    }

    private static List<string> ValidateReconciliationIdentity(SmartPositionSnapshot snapshot, SmartPositionState state)
    {
        var diagnostics = new List<string>();
        if (string.IsNullOrWhiteSpace(snapshot.SymbolName) || !string.Equals(snapshot.SymbolName, state.SymbolName, StringComparison.Ordinal))
            diagnostics.Add("Broker symbol is missing or contradicts the enrolled position identity; reconciliation was skipped.");
        if (snapshot.Direction != state.Direction)
            diagnostics.Add("Broker direction contradicts the enrolled position identity; reconciliation was skipped.");
        if (!IsFinitePositive(snapshot.EntryPrice) || Math.Abs(snapshot.EntryPrice - state.EntryPrice) > 1e-12)
            diagnostics.Add("Broker entry price is missing or contradicts the captured position identity; reconciliation was skipped.");
        if (snapshot.ObservedAtUtc == default)
            diagnostics.Add("Broker observation time is missing; reconciliation was skipped.");
        return diagnostics;
    }

    private static IReadOnlyList<string> CaptureReferenceDiagnostics(SmartPositionSnapshot snapshot)
    {
        var diagnostics = new List<string>();
        if (snapshot.StopLoss.HasValue && !IsValidInitialStopLoss(snapshot.Direction, snapshot.EntryPrice, snapshot.StopLoss.Value))
            diagnostics.Add("Initial stop-loss reference is invalid and was captured as unavailable.");
        if (snapshot.TakeProfit.HasValue && !IsValidFavorableTrigger(snapshot.Direction, snapshot.EntryPrice, snapshot.TakeProfit.Value))
            diagnostics.Add("Initial take-profit reference is invalid and was captured as unavailable.");
        return diagnostics;
    }

    private static SmartPositionState CreateInitialState(SmartPositionSnapshot snapshot)
    {
        var validInitialSl = snapshot.StopLoss.HasValue && IsValidInitialStopLoss(snapshot.Direction, snapshot.EntryPrice, snapshot.StopLoss.Value);
        var validInitialTp = snapshot.TakeProfit.HasValue && IsValidFavorableTrigger(snapshot.Direction, snapshot.EntryPrice, snapshot.TakeProfit.Value);
        return new SmartPositionState
        {
            PositionId = snapshot.PositionId,
            SymbolName = snapshot.SymbolName,
            Direction = snapshot.Direction,
            EntryPrice = snapshot.EntryPrice,
            OriginalVolumeInUnits = snapshot.VolumeInUnits,
            CurrentVolumeInUnits = snapshot.VolumeInUnits,
            InitialStopLoss = validInitialSl ? snapshot.StopLoss : null,
            InitialTakeProfit = validInitialTp ? snapshot.TakeProfit : null,
            CurrentStopLoss = IsFiniteNullablePositive(snapshot.StopLoss) ? snapshot.StopLoss : null,
            CurrentTakeProfit = IsFiniteNullablePositive(snapshot.TakeProfit) ? snapshot.TakeProfit : null,
            LastReconciledAtUtc = snapshot.ObservedAtUtc,
            MonitoringStartedAtUtc = snapshot.ObservedAtUtc,
            Phase = SmartPositionPhase.MonitoringPreBreakEven,
            StopManagement = new SmartStopManagementPlan(),
            PartialProfit = new SmartPartialProfitPlan(),
            AlertDefinitions = new List<SmartAlertDefinition>()
        };
    }

    private static void ReconcileBrokerState(SmartPositionState state, SmartPositionSnapshot snapshot, ICollection<string> diagnostics)
    {
        if (!IsFinitePositive(snapshot.VolumeInUnits))
        {
            diagnostics.Add("Broker reports an invalid open-position volume; prior volume was retained and no financial action was emitted.");
        }
        else
        {
            state.CurrentVolumeInUnits = snapshot.VolumeInUnits;
        }

        state.CurrentStopLoss = ReconcileOptionalPrice(snapshot.StopLoss, "stop loss", diagnostics);
        state.CurrentTakeProfit = ReconcileOptionalPrice(snapshot.TakeProfit, "take profit", diagnostics);
        state.LastReconciledAtUtc = snapshot.ObservedAtUtc;

        var brokerStopAlert = state.AlertDefinitions.FirstOrDefault(d => d.AlertType == SmartAlertType.StopLoss && !d.IsVirtualStopLoss);
        if (brokerStopAlert == null)
            return;

        if (!state.CurrentStopLoss.HasValue)
        {
            if (brokerStopAlert.State == SmartAlertState.Armed)
                brokerStopAlert.State = SmartAlertState.Disarmed;
            return;
        }

        if (brokerStopAlert.State == SmartAlertState.Armed)
            brokerStopAlert.TriggerPrice = state.CurrentStopLoss.Value;
    }

    private static double? ReconcileOptionalPrice(double? price, string fieldName, ICollection<string> diagnostics)
    {
        if (!price.HasValue)
            return null;
        if (IsFinitePositive(price.Value))
            return price.Value;
        diagnostics.Add($"Broker {fieldName} is invalid; the reconciled reference was cleared.");
        return null;
    }

    private static IReadOnlyList<string> ConfigurePartialProfit(SmartPositionState state, SmartPositionSettings settings)
    {
        var diagnostics = new List<string>();
        var plan = new SmartPartialProfitPlan { Enabled = settings.FinancialPartialProfitEnabled };
        var triggerValid = settings.FirstPartialProfitTriggerPrice.HasValue &&
                           IsFinitePositive(settings.FirstPartialProfitTriggerPrice.Value) &&
                           IsValidFavorableTrigger(state.Direction, state.EntryPrice, settings.FirstPartialProfitTriggerPrice.Value);
        var closePercentValid = settings.PartialProfitClosePercent.HasValue &&
                                settings.PartialProfitClosePercent.Value > 0 &&
                                settings.PartialProfitClosePercent.Value <= 100 &&
                                !double.IsNaN(settings.PartialProfitClosePercent.Value) &&
                                !double.IsInfinity(settings.PartialProfitClosePercent.Value);

        if (triggerValid)
            plan.TriggerPrice = settings.FirstPartialProfitTriggerPrice!.Value;
        if (closePercentValid)
            plan.ClosePercent = settings.PartialProfitClosePercent!.Value;

        if (plan.Enabled && (!triggerValid || !closePercentValid))
        {
            plan.Enabled = false;
            diagnostics.Add("First partial-profit trigger/close percentage is invalid; financial partial profit was disabled.");
        }

        state.PartialProfit = plan;
        state.PendingPartialProfitAction = null;
        state.FirstPartialProfitCompleted = false;
        state.FirstPartialProfitCompletedAtUtc = null;
        return diagnostics;
    }

    private static IReadOnlyList<SmartPositionAction> EvaluateFirstPartialProfit(
        SmartPositionState state,
        SmartPositionSnapshot snapshot,
        ICollection<string> diagnostics)
    {
        state.PartialProfit ??= new SmartPartialProfitPlan();
        ConfirmPendingPartialProfit(state, snapshot);

        if (!state.PartialProfit.Enabled || state.FirstPartialProfitCompleted || state.PendingPartialProfitAction != null)
            return Array.Empty<SmartPositionAction>();
        if (!snapshot.ObservedPrice.HasValue || !IsFinitePositive(snapshot.ObservedPrice.Value))
            return Array.Empty<SmartPositionAction>();
        if (!IsFavorableTriggerReached(state.Direction, snapshot.ObservedPrice.Value, state.PartialProfit.TriggerPrice))
            return Array.Empty<SmartPositionAction>();
        if (!IsFinitePositive(state.CurrentVolumeInUnits))
        {
            diagnostics.Add("First partial profit requires a finite positive reconciled broker volume.");
            return Array.Empty<SmartPositionAction>();
        }

        var requestedCloseVolume = state.CurrentVolumeInUnits * state.PartialProfit.ClosePercent / 100.0;
        if (!IsFinitePositive(requestedCloseVolume) || requestedCloseVolume > state.CurrentVolumeInUnits)
        {
            diagnostics.Add("First partial-profit close volume is invalid; no financial action was emitted.");
            return Array.Empty<SmartPositionAction>();
        }

        var action = new SmartPositionAction
        {
            ActionId = BuildPartialProfitActionId(state.PositionId, 1, requestedCloseVolume, snapshot.ObservedAtUtc),
            ActionType = SmartPositionActionType.PartialClose,
            PositionId = state.PositionId,
            SymbolName = state.SymbolName,
            Direction = state.Direction,
            RequestedCloseVolumeInUnits = requestedCloseVolume,
            PartialProfitStageIndex = 1,
            RequestedAtUtc = snapshot.ObservedAtUtc
        };
        state.PendingPartialProfitAction = new SmartPendingPartialProfitAction
        {
            ActionId = action.ActionId,
            StageIndex = 1,
            BrokerVolumeAtRequest = state.CurrentVolumeInUnits,
            RequestedCloseVolumeInUnits = requestedCloseVolume,
            RequestedAtUtc = snapshot.ObservedAtUtc,
            ExecutionStatus = SmartActionExecutionStatus.Pending
        };
        return new[] { action };
    }

    private static void ConfirmPendingPartialProfit(SmartPositionState state, SmartPositionSnapshot snapshot)
    {
        var pending = state.PendingPartialProfitAction;
        if (pending == null)
            return;

        var tolerance = Math.Max(1e-9, Math.Abs(pending.BrokerVolumeAtRequest) * 1e-12);
        if (state.CurrentVolumeInUnits < pending.BrokerVolumeAtRequest - tolerance)
        {
            state.FirstPartialProfitCompleted = true;
            state.FirstPartialProfitCompletedAtUtc = snapshot.ObservedAtUtc;
            state.PendingPartialProfitAction = null;
        }
    }

    private static IReadOnlyList<string> ConfigureStopManagement(SmartPositionState state, SmartPositionSettings settings)
    {
        var diagnostics = new List<string>();
        var plan = new SmartStopManagementPlan
        {
            Enabled = settings.FinancialStopManagementEnabled,
            StopImprovementEpsilon = IsFiniteNonNegative(settings.StopImprovementEpsilon)
                ? settings.StopImprovementEpsilon
                : 0
        };

        if (!IsFiniteNonNegative(settings.StopImprovementEpsilon))
            diagnostics.Add("Stop-improvement epsilon is invalid and was reset to zero.");

        if (settings.PreBreakEvenTrailingEnabled)
        {
            if (IsFinitePositive(settings.PreBreakEvenTrailingPriceDistance ?? double.NaN))
            {
                plan.PreBreakEvenTrailingEnabled = true;
                plan.PreBreakEvenTrailingPriceDistance = settings.PreBreakEvenTrailingPriceDistance!.Value;
            }
            else
            {
                diagnostics.Add("Pre-BE trailing price distance must be finite and positive; pre-BE trailing was disabled.");
            }
        }

        if (settings.BreakEvenFinancialEnabled)
        {
            var triggerValid = settings.BreakEvenFinancialTriggerPrice.HasValue &&
                               IsFinitePositive(settings.BreakEvenFinancialTriggerPrice.Value) &&
                               IsValidFavorableTrigger(state.Direction, state.EntryPrice, settings.BreakEvenFinancialTriggerPrice.Value);
            var adjustmentValid = settings.BreakEvenAdjustmentPriceDistance.HasValue &&
                                  IsFiniteNonNegative(settings.BreakEvenAdjustmentPriceDistance.Value);
            if (triggerValid && adjustmentValid)
            {
                plan.BreakEvenEnabled = true;
                plan.BreakEvenTriggerPrice = settings.BreakEvenFinancialTriggerPrice;
                plan.BreakEvenAdjustmentPriceDistance = settings.BreakEvenAdjustmentPriceDistance!.Value;
            }
            else
            {
                diagnostics.Add("Break-even trigger/adjustment is invalid; financial break-even was disabled.");
            }
        }

        if (settings.PostBreakEvenTrailingEnabled)
        {
            if (IsFinitePositive(settings.PostBreakEvenTrailingPriceDistance ?? double.NaN))
            {
                plan.PostBreakEvenTrailingEnabled = true;
                plan.PostBreakEvenTrailingPriceDistance = settings.PostBreakEvenTrailingPriceDistance!.Value;
            }
            else
            {
                diagnostics.Add("Post-BE trailing price distance must be finite and positive; post-BE trailing was disabled.");
            }
        }

        if (plan.Enabled && !plan.PreBreakEvenTrailingEnabled && !plan.BreakEvenEnabled && !plan.PostBreakEvenTrailingEnabled)
        {
            plan.Enabled = false;
            diagnostics.Add("Financial stop management had no valid enabled behavior and was disabled.");
        }

        state.StopManagement = plan;
        state.PendingStopAction = null;
        state.Phase = SmartPositionPhase.MonitoringPreBreakEven;
        state.PhaseChangedAtUtc = null;
        return diagnostics;
    }

    private static IReadOnlyList<SmartPositionAction> EvaluateFinancialStopManagement(
        SmartPositionState state,
        SmartPositionSnapshot snapshot,
        ICollection<string> diagnostics)
    {
        var plan = state.StopManagement ?? new SmartStopManagementPlan();
        state.StopManagement = plan;
        if (!plan.Enabled)
            return Array.Empty<SmartPositionAction>();

        ConfirmPendingStopAction(state, snapshot);

        if (!snapshot.ObservedPrice.HasValue || !IsFinitePositive(snapshot.ObservedPrice.Value))
            return Array.Empty<SmartPositionAction>();

        if (state.Phase == SmartPositionPhase.BreakEven &&
            state.PendingStopAction == null &&
            state.PhaseChangedAtUtc.HasValue &&
            snapshot.ObservedAtUtc > state.PhaseChangedAtUtc.Value)
        {
            state.Phase = SmartPositionPhase.PostBreakEvenTrailing;
            state.PhaseChangedAtUtc = snapshot.ObservedAtUtc;
        }

        if (state.PendingStopAction != null)
        {
            var pending = state.PendingStopAction;
            if (pending.ExecutionStatus is SmartActionExecutionStatus.Pending or SmartActionExecutionStatus.AcceptedAwaitingReconciliation)
                return Array.Empty<SmartPositionAction>();

            if (pending.Reason == SmartStopActionReason.BreakEven)
                return Array.Empty<SmartPositionAction>();
        }

        return state.Phase switch
        {
            SmartPositionPhase.MonitoringPreBreakEven => EvaluatePreBreakEven(state, snapshot, diagnostics),
            SmartPositionPhase.PostBreakEvenTrailing => EvaluatePostBreakEven(state, snapshot, diagnostics),
            _ => Array.Empty<SmartPositionAction>()
        };
    }

    private static IReadOnlyList<SmartPositionAction> EvaluatePreBreakEven(
        SmartPositionState state,
        SmartPositionSnapshot snapshot,
        ICollection<string> diagnostics)
    {
        var plan = state.StopManagement;
        var observedPrice = snapshot.ObservedPrice!.Value;

        if (plan.BreakEvenEnabled && plan.BreakEvenTriggerPrice.HasValue &&
            IsFavorableTriggerReached(state.Direction, observedPrice, plan.BreakEvenTriggerPrice.Value))
        {
            var desiredStop = state.EntryPrice + DirectionSign(state.Direction) * plan.BreakEvenAdjustmentPriceDistance;
            if (!IsExecutableStopCandidate(state.Direction, desiredStop, observedPrice, plan.StopImprovementEpsilon))
            {
                diagnostics.Add("Break-even stop candidate is not executable relative to the observed close-side price; no action was emitted.");
                return Array.Empty<SmartPositionAction>();
            }

            if (IsAtOrBetterProtection(state.Direction, state.CurrentStopLoss, desiredStop, plan.StopImprovementEpsilon))
            {
                state.Phase = SmartPositionPhase.BreakEven;
                state.PhaseChangedAtUtc = snapshot.ObservedAtUtc;
                state.PendingStopAction = null;
                return Array.Empty<SmartPositionAction>();
            }

            return EmitStopAction(state, snapshot, desiredStop, SmartStopActionReason.BreakEven, SmartPositionPhase.BreakEven);
        }

        if (!plan.PreBreakEvenTrailingEnabled)
            return Array.Empty<SmartPositionAction>();

        var candidate = observedPrice - DirectionSign(state.Direction) * plan.PreBreakEvenTrailingPriceDistance;
        return EmitTrailingActionIfImproving(
            state,
            snapshot,
            candidate,
            SmartStopActionReason.PreBreakEvenTrailing,
            SmartPositionPhase.MonitoringPreBreakEven,
            diagnostics);
    }

    private static IReadOnlyList<SmartPositionAction> EvaluatePostBreakEven(
        SmartPositionState state,
        SmartPositionSnapshot snapshot,
        ICollection<string> diagnostics)
    {
        var plan = state.StopManagement;
        if (!plan.PostBreakEvenTrailingEnabled)
            return Array.Empty<SmartPositionAction>();

        var observedPrice = snapshot.ObservedPrice!.Value;
        var candidate = observedPrice - DirectionSign(state.Direction) * plan.PostBreakEvenTrailingPriceDistance;
        return EmitTrailingActionIfImproving(
            state,
            snapshot,
            candidate,
            SmartStopActionReason.PostBreakEvenTrailing,
            SmartPositionPhase.PostBreakEvenTrailing,
            diagnostics);
    }

    private static IReadOnlyList<SmartPositionAction> EmitTrailingActionIfImproving(
        SmartPositionState state,
        SmartPositionSnapshot snapshot,
        double candidate,
        SmartStopActionReason reason,
        SmartPositionPhase targetPhase,
        ICollection<string> diagnostics)
    {
        var epsilon = state.StopManagement.StopImprovementEpsilon;
        if (!IsFinitePositive(candidate) || !IsExecutableStopCandidate(state.Direction, candidate, snapshot.ObservedPrice!.Value, epsilon))
        {
            diagnostics.Add($"{reason} stop candidate is invalid; no action was emitted.");
            return Array.Empty<SmartPositionAction>();
        }

        if (!ImprovesProtection(state.Direction, state.CurrentStopLoss, candidate, epsilon))
            return Array.Empty<SmartPositionAction>();

        if (state.PendingStopAction != null &&
            !ImprovesProtection(state.Direction, state.PendingStopAction.RequestedStopPrice, candidate, epsilon))
            return Array.Empty<SmartPositionAction>();

        return EmitStopAction(state, snapshot, candidate, reason, targetPhase);
    }

    private static IReadOnlyList<SmartPositionAction> EmitStopAction(
        SmartPositionState state,
        SmartPositionSnapshot snapshot,
        double requestedStop,
        SmartStopActionReason reason,
        SmartPositionPhase targetPhase)
    {
        var action = new SmartPositionAction
        {
            ActionId = BuildStopActionId(state.PositionId, reason, requestedStop, snapshot.ObservedAtUtc),
            ActionType = SmartPositionActionType.ImproveStopLoss,
            Reason = reason,
            PositionId = state.PositionId,
            SymbolName = state.SymbolName,
            Direction = state.Direction,
            RequestedStopPrice = requestedStop,
            TargetPhase = targetPhase,
            RequestedAtUtc = snapshot.ObservedAtUtc
        };
        state.PendingStopAction = new SmartPendingStopAction
        {
            ActionId = action.ActionId,
            Reason = reason,
            RequestedStopPrice = requestedStop,
            TargetPhase = targetPhase,
            RequestedAtUtc = snapshot.ObservedAtUtc,
            ExecutionStatus = SmartActionExecutionStatus.Pending
        };
        return new[] { action };
    }

    private static void ConfirmPendingStopAction(SmartPositionState state, SmartPositionSnapshot snapshot)
    {
        var pending = state.PendingStopAction;
        if (pending == null || !IsAtOrBetterProtection(state.Direction, state.CurrentStopLoss, pending.RequestedStopPrice, state.StopManagement.StopImprovementEpsilon))
            return;

        if (pending.TargetPhase != state.Phase)
        {
            state.Phase = pending.TargetPhase;
            state.PhaseChangedAtUtc = snapshot.ObservedAtUtc;
        }
        state.PendingStopAction = null;
    }

    private static IReadOnlyList<SmartAlertEvent> EvaluateAlerts(SmartPositionState state, SmartPositionSnapshot snapshot)
    {
        if (!snapshot.ObservedPrice.HasValue || !IsFinitePositive(snapshot.ObservedPrice.Value))
            return Array.Empty<SmartAlertEvent>();

        var events = new List<SmartAlertEvent>();
        foreach (var definition in state.AlertDefinitions.Where(d => d.State == SmartAlertState.Armed))
        {
            if (!IsTriggered(definition, snapshot.ObservedPrice.Value))
                continue;

            definition.State = SmartAlertState.Triggered;
            definition.TriggeredAtUtc = snapshot.ObservedAtUtc;
            events.Add(CreateAlertEvent(definition, snapshot.ObservedPrice.Value, snapshot.ObservedAtUtc));
        }
        return events;
    }

    private static IReadOnlyList<string> ConfigureAlerts(
        SmartPositionState state,
        SmartPositionSnapshot snapshot,
        SmartPositionSettings settings,
        bool rearmExisting)
    {
        var diagnostics = new List<string>();
        ConfigureAlert(state, SmartAlertType.PartialProfit, settings.PartialProfitTriggerPrice, false, rearmExisting, diagnostics);
        ConfigureAlert(state, SmartAlertType.BreakEven, settings.BreakEvenTriggerPrice, false, rearmExisting, diagnostics);

        double? stopTrigger = settings.StopLossTriggerPrice;
        var isVirtualStop = false;
        if (!stopTrigger.HasValue && snapshot.StopLoss.HasValue)
            stopTrigger = snapshot.StopLoss;
        else if (!stopTrigger.HasValue && !snapshot.StopLoss.HasValue && settings.VirtualStopLossPrice.HasValue)
        {
            stopTrigger = settings.VirtualStopLossPrice;
            isVirtualStop = true;
        }
        ConfigureAlert(state, SmartAlertType.StopLoss, stopTrigger, isVirtualStop, rearmExisting, diagnostics);
        return diagnostics;
    }

    private static void ConfigureAlert(
        SmartPositionState state,
        SmartAlertType alertType,
        double? triggerPrice,
        bool isVirtualStopLoss,
        bool rearmExisting,
        ICollection<string> diagnostics)
    {
        var existing = state.AlertDefinitions.FirstOrDefault(definition => definition.AlertType == alertType);
        if (!triggerPrice.HasValue)
        {
            if (rearmExisting && existing != null)
            {
                existing.State = SmartAlertState.Armed;
                existing.TriggeredAtUtc = null;
            }
            return;
        }

        if (!IsFinitePositive(triggerPrice.Value) || !IsValidTrigger(alertType, state.Direction, state.EntryPrice, triggerPrice.Value))
        {
            diagnostics.Add($"{alertType} alert trigger is invalid for the position and was not armed.");
            return;
        }

        if (existing == null)
        {
            state.AlertDefinitions.Add(new SmartAlertDefinition
            {
                AlertId = BuildAlertId(state.PositionId, alertType, state.MonitoringStartedAtUtc),
                PositionId = state.PositionId,
                SymbolName = state.SymbolName,
                Direction = state.Direction,
                AlertType = alertType,
                TriggerPrice = triggerPrice.Value,
                IsVirtualStopLoss = alertType == SmartAlertType.StopLoss && isVirtualStopLoss,
                State = SmartAlertState.Armed,
                CreatedAtUtc = state.MonitoringStartedAtUtc
            });
            return;
        }

        if (!rearmExisting)
            return;

        existing.TriggerPrice = triggerPrice.Value;
        existing.IsVirtualStopLoss = alertType == SmartAlertType.StopLoss && isVirtualStopLoss;
        existing.State = SmartAlertState.Armed;
        existing.TriggeredAtUtc = null;
    }

    private static bool IsTriggered(SmartAlertDefinition definition, double observedPrice)
    {
        if (definition.AlertType is SmartAlertType.PartialProfit or SmartAlertType.BreakEven)
            return IsFavorableTriggerReached(definition.Direction, observedPrice, definition.TriggerPrice);
        return definition.Direction == SmartPositionDirection.Long ? observedPrice <= definition.TriggerPrice : observedPrice >= definition.TriggerPrice;
    }

    private static SmartAlertEvent CreateAlertEvent(SmartAlertDefinition definition, double observedPrice, DateTime triggeredAtUtc) =>
        new()
        {
            EventId = $"{definition.AlertId}:{triggeredAtUtc.Ticks}",
            AlertId = definition.AlertId,
            PositionId = definition.PositionId,
            SymbolName = definition.SymbolName,
            Direction = definition.Direction,
            AlertType = definition.AlertType,
            TriggerPrice = definition.TriggerPrice,
            ObservedPrice = observedPrice,
            TriggeredAtUtc = triggeredAtUtc,
            RequestedAction = "None",
            ActionResult = "MonitoringOnly"
        };

    private static string BuildAlertId(int positionId, SmartAlertType alertType, DateTime createdAtUtc) => $"{positionId}:{alertType}:{createdAtUtc.Ticks}";

    private static string BuildStopActionId(int positionId, SmartStopActionReason reason, double requestedStop, DateTime observedAtUtc) =>
        $"{positionId}:{reason}:{requestedStop.ToString("R", CultureInfo.InvariantCulture)}:{observedAtUtc.Ticks}";

    private static string BuildPartialProfitActionId(int positionId, int stageIndex, double requestedCloseVolume, DateTime observedAtUtc) =>
        $"{positionId}:PP:{stageIndex}:{requestedCloseVolume.ToString("R", CultureInfo.InvariantCulture)}:{observedAtUtc.Ticks}";

    private static bool IsValidTrigger(SmartAlertType alertType, SmartPositionDirection direction, double entryPrice, double triggerPrice) =>
        alertType == SmartAlertType.StopLoss || IsValidFavorableTrigger(direction, entryPrice, triggerPrice);

    private static bool IsValidInitialStopLoss(SmartPositionDirection direction, double entryPrice, double stopLoss) =>
        IsFinitePositive(stopLoss) && (direction == SmartPositionDirection.Long ? stopLoss < entryPrice : stopLoss > entryPrice);

    private static bool IsValidFavorableTrigger(SmartPositionDirection direction, double entryPrice, double triggerPrice) =>
        direction == SmartPositionDirection.Long ? triggerPrice > entryPrice : triggerPrice < entryPrice;

    private static bool IsFavorableTriggerReached(SmartPositionDirection direction, double observedPrice, double triggerPrice) =>
        direction == SmartPositionDirection.Long ? observedPrice >= triggerPrice : observedPrice <= triggerPrice;

    private static bool ImprovesProtection(SmartPositionDirection direction, double? currentStop, double desiredStop, double epsilon)
    {
        if (!currentStop.HasValue)
            return true;
        return direction == SmartPositionDirection.Long
            ? desiredStop > currentStop.Value + epsilon
            : desiredStop < currentStop.Value - epsilon;
    }

    private static bool IsAtOrBetterProtection(SmartPositionDirection direction, double? currentStop, double targetStop, double epsilon)
    {
        if (!currentStop.HasValue)
            return false;
        return direction == SmartPositionDirection.Long
            ? currentStop.Value >= targetStop - epsilon
            : currentStop.Value <= targetStop + epsilon;
    }

    private static bool IsExecutableStopCandidate(SmartPositionDirection direction, double desiredStop, double observedPrice, double epsilon) =>
        direction == SmartPositionDirection.Long
            ? desiredStop < observedPrice - epsilon
            : desiredStop > observedPrice + epsilon;

    private static double DirectionSign(SmartPositionDirection direction) => direction == SmartPositionDirection.Long ? 1.0 : -1.0;
    private static bool IsFiniteNullablePositive(double? value) => !value.HasValue || IsFinitePositive(value.Value);
    private static bool IsFiniteNonNegative(double value) => value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool IsFinitePositive(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static SmartPositionState Clone(SmartPositionState state) =>
        new()
        {
            PositionId = state.PositionId,
            SymbolName = state.SymbolName,
            Direction = state.Direction,
            EntryPrice = state.EntryPrice,
            OriginalVolumeInUnits = state.OriginalVolumeInUnits,
            CurrentVolumeInUnits = state.CurrentVolumeInUnits,
            InitialStopLoss = state.InitialStopLoss,
            InitialTakeProfit = state.InitialTakeProfit,
            CurrentStopLoss = state.CurrentStopLoss,
            CurrentTakeProfit = state.CurrentTakeProfit,
            LastReconciledAtUtc = state.LastReconciledAtUtc,
            MonitoringStartedAtUtc = state.MonitoringStartedAtUtc,
            Phase = state.Phase,
            PhaseChangedAtUtc = state.PhaseChangedAtUtc,
            StopManagement = CloneStopManagement(state.StopManagement),
            PendingStopAction = ClonePendingStopAction(state.PendingStopAction),
            PartialProfit = ClonePartialProfit(state.PartialProfit),
            PendingPartialProfitAction = ClonePendingPartialProfitAction(state.PendingPartialProfitAction),
            FirstPartialProfitCompleted = state.FirstPartialProfitCompleted,
            FirstPartialProfitCompletedAtUtc = state.FirstPartialProfitCompletedAtUtc,
            AlertDefinitions = (state.AlertDefinitions ?? new List<SmartAlertDefinition>()).Select(CloneAlert).ToList()
        };

    private static SmartStopManagementPlan CloneStopManagement(SmartStopManagementPlan? plan)
    {
        plan ??= new SmartStopManagementPlan();
        return new SmartStopManagementPlan
        {
            Enabled = plan.Enabled,
            PreBreakEvenTrailingEnabled = plan.PreBreakEvenTrailingEnabled,
            BreakEvenEnabled = plan.BreakEvenEnabled,
            PostBreakEvenTrailingEnabled = plan.PostBreakEvenTrailingEnabled,
            BreakEvenTriggerPrice = plan.BreakEvenTriggerPrice,
            BreakEvenAdjustmentPriceDistance = plan.BreakEvenAdjustmentPriceDistance,
            PreBreakEvenTrailingPriceDistance = plan.PreBreakEvenTrailingPriceDistance,
            PostBreakEvenTrailingPriceDistance = plan.PostBreakEvenTrailingPriceDistance,
            StopImprovementEpsilon = plan.StopImprovementEpsilon
        };
    }

    private static SmartPendingStopAction? ClonePendingStopAction(SmartPendingStopAction? pending) =>
        pending == null
            ? null
            : new SmartPendingStopAction
            {
                ActionId = pending.ActionId,
                Reason = pending.Reason,
                RequestedStopPrice = pending.RequestedStopPrice,
                TargetPhase = pending.TargetPhase,
                RequestedAtUtc = pending.RequestedAtUtc,
                ExecutionStatus = pending.ExecutionStatus,
                DiagnosticError = pending.DiagnosticError
            };

    private static SmartPartialProfitPlan ClonePartialProfit(SmartPartialProfitPlan? plan)
    {
        plan ??= new SmartPartialProfitPlan();
        return new SmartPartialProfitPlan
        {
            Enabled = plan.Enabled,
            TriggerPrice = plan.TriggerPrice,
            ClosePercent = plan.ClosePercent
        };
    }

    private static SmartPendingPartialProfitAction? ClonePendingPartialProfitAction(SmartPendingPartialProfitAction? pending) =>
        pending == null
            ? null
            : new SmartPendingPartialProfitAction
            {
                ActionId = pending.ActionId,
                StageIndex = pending.StageIndex,
                BrokerVolumeAtRequest = pending.BrokerVolumeAtRequest,
                RequestedCloseVolumeInUnits = pending.RequestedCloseVolumeInUnits,
                RequestedAtUtc = pending.RequestedAtUtc,
                ExecutionStatus = pending.ExecutionStatus,
                DiagnosticError = pending.DiagnosticError
            };

    private static SmartAlertDefinition CloneAlert(SmartAlertDefinition definition) =>
        new()
        {
            AlertId = definition.AlertId,
            PositionId = definition.PositionId,
            SymbolName = definition.SymbolName,
            Direction = definition.Direction,
            AlertType = definition.AlertType,
            TriggerPrice = definition.TriggerPrice,
            IsVirtualStopLoss = definition.IsVirtualStopLoss,
            State = definition.State,
            CreatedAtUtc = definition.CreatedAtUtc,
            TriggeredAtUtc = definition.TriggeredAtUtc
        };
}
