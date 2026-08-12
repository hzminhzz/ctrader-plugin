using System;
using System.Collections.Generic;

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

            return new SmartPositionEvaluation
            {
                Diagnostics = CaptureReferenceDiagnostics(positionSnapshot),
                NextState = CreateInitialState(positionSnapshot)
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

        return new SmartPositionEvaluation
        {
            NextState = nextState
        };
    }

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

        if (snapshot.StopLoss.HasValue && !IsValidStopLoss(snapshot))
            diagnostics.Add("Initial stop-loss reference is invalid and was captured as unavailable.");
        if (snapshot.TakeProfit.HasValue && !IsValidTakeProfit(snapshot))
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
            InitialStopLoss = IsValidStopLoss(snapshot) ? snapshot.StopLoss : null,
            InitialTakeProfit = IsValidTakeProfit(snapshot) ? snapshot.TakeProfit : null,
            MonitoringStartedAtUtc = snapshot.ObservedAtUtc,
            Phase = SmartPositionPhase.MonitoringPreBreakEven
        };
    }

    private static bool IsValidStopLoss(SmartPositionSnapshot snapshot)
    {
        if (!snapshot.StopLoss.HasValue || !IsFinitePositive(snapshot.StopLoss.Value))
            return false;

        return snapshot.Direction == SmartPositionDirection.Long
            ? snapshot.StopLoss.Value < snapshot.EntryPrice
            : snapshot.StopLoss.Value > snapshot.EntryPrice;
    }

    private static bool IsValidTakeProfit(SmartPositionSnapshot snapshot)
    {
        if (!snapshot.TakeProfit.HasValue || !IsFinitePositive(snapshot.TakeProfit.Value))
            return false;

        return snapshot.Direction == SmartPositionDirection.Long
            ? snapshot.TakeProfit.Value > snapshot.EntryPrice
            : snapshot.TakeProfit.Value < snapshot.EntryPrice;
    }

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
            Phase = state.Phase
        };
    }
}
