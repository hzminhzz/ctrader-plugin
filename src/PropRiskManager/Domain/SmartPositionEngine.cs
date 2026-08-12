using System;
using System.Collections.Generic;
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
        {
            return new SmartPositionEvaluation
            {
                NextState = null
            };
        }

        if (persistedState is null)
        {
            if (!settings.EnrollRequested)
                return new SmartPositionEvaluation { NextState = null };

            var diagnostics = ValidateEnrollment(positionSnapshot);
            if (diagnostics.Count > 0)
            {
                return new SmartPositionEvaluation
                {
                    Diagnostics = diagnostics,
                    NextState = null
                };
            }

            var state = CreateInitialState(positionSnapshot);
            diagnostics.AddRange(CaptureReferenceDiagnostics(positionSnapshot));
            diagnostics.AddRange(ConfigureAlerts(state, positionSnapshot, settings, rearmExisting: false));

            return new SmartPositionEvaluation
            {
                Diagnostics = diagnostics,
                NextState = state
            };
        }

        if (persistedState.PositionId != positionSnapshot.PositionId)
        {
            return new SmartPositionEvaluation
            {
                Diagnostics = new[] { "Persisted state position identity does not match the supplied snapshot." },
                NextState = Clone(persistedState)
            };
        }

        var nextState = Clone(persistedState);
        if (positionSnapshot.IsOpen && positionSnapshot.VolumeInUnits > 0)
            nextState.CurrentVolumeInUnits = positionSnapshot.VolumeInUnits;

        var diagnosticsResult = new List<string>();

        if (settings.RemoveAlertsRequested)
        {
            foreach (var definition in nextState.AlertDefinitions)
            {
                if (definition.State == SmartAlertState.Armed)
                    definition.State = SmartAlertState.Disarmed;
            }

            return new SmartPositionEvaluation
            {
                Diagnostics = diagnosticsResult,
                NextState = nextState
            };
        }

        if (settings.RearmAlertsRequested)
            diagnosticsResult.AddRange(ConfigureAlerts(nextState, positionSnapshot, settings, rearmExisting: true));

        if (!positionSnapshot.ObservedPrice.HasValue || !IsFinitePositive(positionSnapshot.ObservedPrice.Value))
        {
            return new SmartPositionEvaluation
            {
                Diagnostics = diagnosticsResult,
                NextState = nextState
            };
        }

        var events = new List<SmartAlertEvent>();
        foreach (var definition in nextState.AlertDefinitions.Where(d => d.State == SmartAlertState.Armed))
        {
            if (!IsTriggered(definition, positionSnapshot.ObservedPrice.Value))
                continue;

            definition.State = SmartAlertState.Triggered;
            definition.TriggeredAtUtc = positionSnapshot.ObservedAtUtc;
            events.Add(CreateAlertEvent(definition, positionSnapshot.ObservedPrice.Value, positionSnapshot.ObservedAtUtc));
        }

        return new SmartPositionEvaluation
        {
            Alerts = events,
            Diagnostics = diagnosticsResult,
            NextState = nextState
        };
    }

    public static int CountArmedAlerts(IEnumerable<SmartPositionState> states) =>
        states.Sum(state => state.AlertDefinitions.Count(definition => definition.State == SmartAlertState.Armed));

    private static List<string> ValidateEnrollment(SmartPositionSnapshot snapshot)
    {
        var diagnostics = new List<string>();

        if (!snapshot.IsOpen)
            diagnostics.Add("Only open positions can be enrolled.");
        if (snapshot.PositionId <= 0)
            diagnostics.Add("Position identity is required for enrollment.");
        if (string.IsNullOrWhiteSpace(snapshot.SymbolName))
            diagnostics.Add("Symbol name is required for enrollment.");
        if (!IsFinitePositive(snapshot.EntryPrice))
            diagnostics.Add("Entry price must be a finite positive value.");
        if (!IsFinitePositive(snapshot.VolumeInUnits))
            diagnostics.Add("Position volume must be a finite positive value.");
        if (snapshot.ObservedAtUtc == default)
            diagnostics.Add("Observation time is required for enrollment.");

        return diagnostics;
    }

    private static IReadOnlyList<string> CaptureReferenceDiagnostics(SmartPositionSnapshot snapshot)
    {
        var diagnostics = new List<string>();

        if (snapshot.StopLoss.HasValue && !IsValidStopLoss(snapshot.Direction, snapshot.EntryPrice, snapshot.StopLoss.Value))
            diagnostics.Add("Initial stop-loss reference is invalid and was captured as unavailable.");
        if (snapshot.TakeProfit.HasValue && !IsValidFavorableTrigger(snapshot.Direction, snapshot.EntryPrice, snapshot.TakeProfit.Value))
            diagnostics.Add("Initial take-profit reference is invalid and was captured as unavailable.");

        return diagnostics;
    }

    private static SmartPositionState CreateInitialState(SmartPositionSnapshot snapshot)
    {
        return new SmartPositionState
        {
            PositionId = snapshot.PositionId,
            SymbolName = snapshot.SymbolName,
            Direction = snapshot.Direction,
            EntryPrice = snapshot.EntryPrice,
            OriginalVolumeInUnits = snapshot.VolumeInUnits,
            CurrentVolumeInUnits = snapshot.VolumeInUnits,
            InitialStopLoss = snapshot.StopLoss.HasValue && IsValidStopLoss(snapshot.Direction, snapshot.EntryPrice, snapshot.StopLoss.Value)
                ? snapshot.StopLoss
                : null,
            InitialTakeProfit = snapshot.TakeProfit.HasValue && IsValidFavorableTrigger(snapshot.Direction, snapshot.EntryPrice, snapshot.TakeProfit.Value)
                ? snapshot.TakeProfit
                : null,
            MonitoringStartedAtUtc = snapshot.ObservedAtUtc,
            Phase = SmartPositionPhase.MonitoringPreBreakEven,
            AlertDefinitions = new List<SmartAlertDefinition>()
        };
    }

    private static IReadOnlyList<string> ConfigureAlerts(
        SmartPositionState state,
        SmartPositionSnapshot snapshot,
        SmartPositionSettings settings,
        bool rearmExisting)
    {
        var diagnostics = new List<string>();
        ConfigureAlert(
            state,
            snapshot,
            SmartAlertType.PartialProfit,
            settings.PartialProfitTriggerPrice,
            isVirtualStopLoss: false,
            rearmExisting,
            diagnostics);
        ConfigureAlert(
            state,
            snapshot,
            SmartAlertType.BreakEven,
            settings.BreakEvenTriggerPrice,
            isVirtualStopLoss: false,
            rearmExisting,
            diagnostics);

        double? stopTrigger = settings.StopLossTriggerPrice;
        var isVirtualStop = false;
        if (!stopTrigger.HasValue && snapshot.StopLoss.HasValue)
            stopTrigger = snapshot.StopLoss;
        else if (!stopTrigger.HasValue && !snapshot.StopLoss.HasValue && settings.VirtualStopLossPrice.HasValue)
        {
            stopTrigger = settings.VirtualStopLossPrice;
            isVirtualStop = true;
        }

        ConfigureAlert(
            state,
            snapshot,
            SmartAlertType.StopLoss,
            stopTrigger,
            isVirtualStop,
            rearmExisting,
            diagnostics);

        return diagnostics;
    }

    private static void ConfigureAlert(
        SmartPositionState state,
        SmartPositionSnapshot snapshot,
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
            diagnostics.Add($"{alertType} alert trigger is invalid for the position direction and was not armed.");
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
        var favorable = definition.AlertType is SmartAlertType.PartialProfit or SmartAlertType.BreakEven;
        if (favorable)
        {
            return definition.Direction == SmartPositionDirection.Long
                ? observedPrice >= definition.TriggerPrice
                : observedPrice <= definition.TriggerPrice;
        }

        return definition.Direction == SmartPositionDirection.Long
            ? observedPrice <= definition.TriggerPrice
            : observedPrice >= definition.TriggerPrice;
    }

    private static SmartAlertEvent CreateAlertEvent(SmartAlertDefinition definition, double observedPrice, DateTime triggeredAtUtc)
    {
        return new SmartAlertEvent
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
    }

    private static string BuildAlertId(int positionId, SmartAlertType alertType, DateTime createdAtUtc) =>
        $"{positionId}:{alertType}:{createdAtUtc.Ticks}";

    private static bool IsValidTrigger(SmartAlertType alertType, SmartPositionDirection direction, double entryPrice, double triggerPrice) =>
        alertType == SmartAlertType.StopLoss
            ? IsValidStopLoss(direction, entryPrice, triggerPrice)
            : IsValidFavorableTrigger(direction, entryPrice, triggerPrice);

    private static bool IsValidStopLoss(SmartPositionDirection direction, double entryPrice, double stopLoss) =>
        direction == SmartPositionDirection.Long ? stopLoss < entryPrice : stopLoss > entryPrice;

    private static bool IsValidFavorableTrigger(SmartPositionDirection direction, double entryPrice, double triggerPrice) =>
        direction == SmartPositionDirection.Long ? triggerPrice > entryPrice : triggerPrice < entryPrice;

    private static bool IsFinitePositive(double value) =>
        value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static SmartPositionState Clone(SmartPositionState state)
    {
        return new SmartPositionState
        {
            PositionId = state.PositionId,
            SymbolName = state.SymbolName,
            Direction = state.Direction,
            EntryPrice = state.EntryPrice,
            OriginalVolumeInUnits = state.OriginalVolumeInUnits,
            CurrentVolumeInUnits = state.CurrentVolumeInUnits,
            InitialStopLoss = state.InitialStopLoss,
            InitialTakeProfit = state.InitialTakeProfit,
            MonitoringStartedAtUtc = state.MonitoringStartedAtUtc,
            Phase = state.Phase,
            AlertDefinitions = state.AlertDefinitions.Select(CloneAlert).ToList()
        };
    }

    private static SmartAlertDefinition CloneAlert(SmartAlertDefinition definition)
    {
        return new SmartAlertDefinition
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
}
