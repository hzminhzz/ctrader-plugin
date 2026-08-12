using System;
using System.Collections.Generic;
using System.Linq;

namespace PropRiskManager.Domain;

public static class SmartPositionEngine
{
    public static SmartPositionEvaluation Evaluate(SmartPositionSnapshot positionSnapshot, SmartPositionSettings settings, SmartPositionState? persistedState)
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
            diagnostics.AddRange(ConfigureAlerts(state, positionSnapshot, settings, false));
            return new SmartPositionEvaluation { Diagnostics = diagnostics, NextState = state };
        }

        if (persistedState.PositionId != positionSnapshot.PositionId)
            return new SmartPositionEvaluation { Diagnostics = new[] { "Persisted state position identity does not match the supplied snapshot." }, NextState = Clone(persistedState) };

        if (!positionSnapshot.IsOpen)
            return new SmartPositionEvaluation { NextState = null };

        var nextState = Clone(persistedState);
        var diagnosticsResult = ValidateReconciliationIdentity(positionSnapshot, persistedState);
        if (diagnosticsResult.Count > 0)
            return new SmartPositionEvaluation { Diagnostics = diagnosticsResult, NextState = nextState };

        ReconcileBrokerState(nextState, positionSnapshot, diagnosticsResult);

        if (settings.RemoveAlertsRequested)
        {
            foreach (var definition in nextState.AlertDefinitions)
                if (definition.State == SmartAlertState.Armed) definition.State = SmartAlertState.Disarmed;
            return new SmartPositionEvaluation { Diagnostics = diagnosticsResult, NextState = nextState };
        }

        if (settings.RearmAlertsRequested)
            diagnosticsResult.AddRange(ConfigureAlerts(nextState, positionSnapshot, settings, true));

        if (!positionSnapshot.ObservedPrice.HasValue || !IsFinitePositive(positionSnapshot.ObservedPrice.Value))
            return new SmartPositionEvaluation { Diagnostics = diagnosticsResult, NextState = nextState };

        var events = new List<SmartAlertEvent>();
        foreach (var definition in nextState.AlertDefinitions.Where(d => d.State == SmartAlertState.Armed))
        {
            if (!IsTriggered(definition, positionSnapshot.ObservedPrice.Value)) continue;
            definition.State = SmartAlertState.Triggered;
            definition.TriggeredAtUtc = positionSnapshot.ObservedAtUtc;
            events.Add(CreateAlertEvent(definition, positionSnapshot.ObservedPrice.Value, positionSnapshot.ObservedAtUtc));
        }

        return new SmartPositionEvaluation { Alerts = events, Diagnostics = diagnosticsResult, NextState = nextState };
    }

    public static int CountArmedAlerts(IEnumerable<SmartPositionState> states) => states.Sum(s => s.AlertDefinitions.Count(d => d.State == SmartAlertState.Armed));

    private static List<string> ValidateEnrollment(SmartPositionSnapshot snapshot)
    {
        var d = new List<string>();
        if (!snapshot.IsOpen) d.Add("Only open positions can be enrolled.");
        if (snapshot.PositionId <= 0) d.Add("Position identity is required for enrollment.");
        if (string.IsNullOrWhiteSpace(snapshot.SymbolName)) d.Add("Symbol name is required for enrollment.");
        if (!IsFinitePositive(snapshot.EntryPrice)) d.Add("Entry price must be a finite positive value.");
        if (!IsFinitePositive(snapshot.VolumeInUnits)) d.Add("Position volume must be a finite positive value.");
        if (snapshot.ObservedAtUtc == default) d.Add("Observation time is required for enrollment.");
        return d;
    }

    private static List<string> ValidateReconciliationIdentity(SmartPositionSnapshot snapshot, SmartPositionState state)
    {
        var d = new List<string>();
        if (string.IsNullOrWhiteSpace(snapshot.SymbolName) || !string.Equals(snapshot.SymbolName, state.SymbolName, StringComparison.Ordinal)) d.Add("Broker symbol is missing or contradicts the enrolled position identity; reconciliation was skipped.");
        if (snapshot.Direction != state.Direction) d.Add("Broker direction contradicts the enrolled position identity; reconciliation was skipped.");
        if (!IsFinitePositive(snapshot.EntryPrice) || Math.Abs(snapshot.EntryPrice - state.EntryPrice) > 1e-12) d.Add("Broker entry price is missing or contradicts the captured position identity; reconciliation was skipped.");
        if (snapshot.ObservedAtUtc == default) d.Add("Broker observation time is missing; reconciliation was skipped.");
        return d;
    }

    private static IReadOnlyList<string> CaptureReferenceDiagnostics(SmartPositionSnapshot snapshot)
    {
        var d = new List<string>();
        if (snapshot.StopLoss.HasValue && !IsValidInitialStopLoss(snapshot.Direction, snapshot.EntryPrice, snapshot.StopLoss.Value)) d.Add("Initial stop-loss reference is invalid and was captured as unavailable.");
        if (snapshot.TakeProfit.HasValue && !IsValidFavorableTrigger(snapshot.Direction, snapshot.EntryPrice, snapshot.TakeProfit.Value)) d.Add("Initial take-profit reference is invalid and was captured as unavailable.");
        return d;
    }

    private static SmartPositionState CreateInitialState(SmartPositionSnapshot s)
    {
        var validSl = s.StopLoss.HasValue && IsValidInitialStopLoss(s.Direction, s.EntryPrice, s.StopLoss.Value);
        var validTp = s.TakeProfit.HasValue && IsValidFavorableTrigger(s.Direction, s.EntryPrice, s.TakeProfit.Value);
        return new SmartPositionState
        {
            PositionId = s.PositionId, SymbolName = s.SymbolName, Direction = s.Direction, EntryPrice = s.EntryPrice,
            OriginalVolumeInUnits = s.VolumeInUnits, CurrentVolumeInUnits = s.VolumeInUnits,
            InitialStopLoss = validSl ? s.StopLoss : null, InitialTakeProfit = validTp ? s.TakeProfit : null,
            CurrentStopLoss = IsFiniteNullablePositive(s.StopLoss) ? s.StopLoss : null,
            CurrentTakeProfit = IsFiniteNullablePositive(s.TakeProfit) ? s.TakeProfit : null,
            LastReconciledAtUtc = s.ObservedAtUtc, MonitoringStartedAtUtc = s.ObservedAtUtc,
            Phase = SmartPositionPhase.MonitoringPreBreakEven, AlertDefinitions = new List<SmartAlertDefinition>()
        };
    }

    private static void ReconcileBrokerState(SmartPositionState state, SmartPositionSnapshot s, ICollection<string> diagnostics)
    {
        if (!IsFinitePositive(s.VolumeInUnits)) diagnostics.Add("Broker reports an invalid open-position volume; prior volume was retained and no financial action was emitted.");
        else state.CurrentVolumeInUnits = s.VolumeInUnits;
        state.CurrentStopLoss = ReconcileOptionalPrice(s.StopLoss, "stop loss", diagnostics);
        state.CurrentTakeProfit = ReconcileOptionalPrice(s.TakeProfit, "take profit", diagnostics);
        state.LastReconciledAtUtc = s.ObservedAtUtc;

        var stopAlert = state.AlertDefinitions.FirstOrDefault(d => d.AlertType == SmartAlertType.StopLoss && !d.IsVirtualStopLoss);
        if (stopAlert == null) return;
        if (!state.CurrentStopLoss.HasValue)
        {
            if (stopAlert.State == SmartAlertState.Armed) stopAlert.State = SmartAlertState.Disarmed;
            return;
        }
        if (stopAlert.State == SmartAlertState.Armed) stopAlert.TriggerPrice = state.CurrentStopLoss.Value;
    }

    private static double? ReconcileOptionalPrice(double? price, string fieldName, ICollection<string> diagnostics)
    {
        if (!price.HasValue) return null;
        if (IsFinitePositive(price.Value)) return price.Value;
        diagnostics.Add($"Broker {fieldName} is invalid; the reconciled reference was cleared.");
        return null;
    }

    private static IReadOnlyList<string> ConfigureAlerts(SmartPositionState state, SmartPositionSnapshot snapshot, SmartPositionSettings settings, bool rearm)
    {
        var d = new List<string>();
        ConfigureAlert(state, SmartAlertType.PartialProfit, settings.PartialProfitTriggerPrice, false, rearm, d);
        ConfigureAlert(state, SmartAlertType.BreakEven, settings.BreakEvenTriggerPrice, false, rearm, d);
        double? stop = settings.StopLossTriggerPrice;
        var isVirtual = false;
        if (!stop.HasValue && snapshot.StopLoss.HasValue) stop = snapshot.StopLoss;
        else if (!stop.HasValue && !snapshot.StopLoss.HasValue && settings.VirtualStopLossPrice.HasValue) { stop = settings.VirtualStopLossPrice; isVirtual = true; }
        ConfigureAlert(state, SmartAlertType.StopLoss, stop, isVirtual, rearm, d);
        return d;
    }

    private static void ConfigureAlert(SmartPositionState state, SmartAlertType type, double? trigger, bool isVirtual, bool rearm, ICollection<string> diagnostics)
    {
        var existing = state.AlertDefinitions.FirstOrDefault(d => d.AlertType == type);
        if (!trigger.HasValue)
        {
            if (rearm && existing != null) { existing.State = SmartAlertState.Armed; existing.TriggeredAtUtc = null; }
            return;
        }
        if (!IsFinitePositive(trigger.Value) || !IsValidTrigger(type, state.Direction, state.EntryPrice, trigger.Value)) { diagnostics.Add($"{type} alert trigger is invalid for the position and was not armed."); return; }
        if (existing == null)
        {
            state.AlertDefinitions.Add(new SmartAlertDefinition { AlertId = BuildAlertId(state.PositionId, type, state.MonitoringStartedAtUtc), PositionId = state.PositionId, SymbolName = state.SymbolName, Direction = state.Direction, AlertType = type, TriggerPrice = trigger.Value, IsVirtualStopLoss = type == SmartAlertType.StopLoss && isVirtual, State = SmartAlertState.Armed, CreatedAtUtc = state.MonitoringStartedAtUtc });
            return;
        }
        if (!rearm) return;
        existing.TriggerPrice = trigger.Value; existing.IsVirtualStopLoss = type == SmartAlertType.StopLoss && isVirtual; existing.State = SmartAlertState.Armed; existing.TriggeredAtUtc = null;
    }

    private static bool IsTriggered(SmartAlertDefinition d, double p) => d.AlertType is SmartAlertType.PartialProfit or SmartAlertType.BreakEven ? (d.Direction == SmartPositionDirection.Long ? p >= d.TriggerPrice : p <= d.TriggerPrice) : (d.Direction == SmartPositionDirection.Long ? p <= d.TriggerPrice : p >= d.TriggerPrice);

    private static SmartAlertEvent CreateAlertEvent(SmartAlertDefinition d, double p, DateTime t) => new() { EventId = $"{d.AlertId}:{t.Ticks}", AlertId = d.AlertId, PositionId = d.PositionId, SymbolName = d.SymbolName, Direction = d.Direction, AlertType = d.AlertType, TriggerPrice = d.TriggerPrice, ObservedPrice = p, TriggeredAtUtc = t, RequestedAction = "None", ActionResult = "MonitoringOnly" };
    private static string BuildAlertId(int id, SmartAlertType t, DateTime created) => $"{id}:{t}:{created.Ticks}";
    private static bool IsValidTrigger(SmartAlertType t, SmartPositionDirection d, double e, double p) => t == SmartAlertType.StopLoss || IsValidFavorableTrigger(d, e, p);
    private static bool IsValidInitialStopLoss(SmartPositionDirection d, double e, double s) => IsFinitePositive(s) && (d == SmartPositionDirection.Long ? s < e : s > e);
    private static bool IsValidFavorableTrigger(SmartPositionDirection d, double e, double p) => d == SmartPositionDirection.Long ? p > e : p < e;
    private static bool IsFiniteNullablePositive(double? v) => !v.HasValue || IsFinitePositive(v.Value);
    private static bool IsFinitePositive(double v) => v > 0 && !double.IsNaN(v) && !double.IsInfinity(v);

    private static SmartPositionState Clone(SmartPositionState s) => new() { PositionId = s.PositionId, SymbolName = s.SymbolName, Direction = s.Direction, EntryPrice = s.EntryPrice, OriginalVolumeInUnits = s.OriginalVolumeInUnits, CurrentVolumeInUnits = s.CurrentVolumeInUnits, InitialStopLoss = s.InitialStopLoss, InitialTakeProfit = s.InitialTakeProfit, CurrentStopLoss = s.CurrentStopLoss, CurrentTakeProfit = s.CurrentTakeProfit, LastReconciledAtUtc = s.LastReconciledAtUtc, MonitoringStartedAtUtc = s.MonitoringStartedAtUtc, Phase = s.Phase, AlertDefinitions = s.AlertDefinitions.Select(CloneAlert).ToList() };
    private static SmartAlertDefinition CloneAlert(SmartAlertDefinition d) => new() { AlertId = d.AlertId, PositionId = d.PositionId, SymbolName = d.SymbolName, Direction = d.Direction, AlertType = d.AlertType, TriggerPrice = d.TriggerPrice, IsVirtualStopLoss = d.IsVirtualStopLoss, State = d.State, CreatedAtUtc = d.CreatedAtUtc, TriggeredAtUtc = d.TriggeredAtUtc };
}
