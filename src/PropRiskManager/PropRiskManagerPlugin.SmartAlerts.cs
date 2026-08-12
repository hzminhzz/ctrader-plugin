using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;
using PropRiskManager.Domain;
using PropRiskManager.Execution;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private DateTime _lastSmartSafetyReconciliation;

    private void RunSmartPositionMonitoring()
    {
        if (_runtimeState.SmartPositions.Count == 0)
            return;

        foreach (var positionId in _runtimeState.SmartPositions.Keys.ToArray())
        {
            var brokerPosition = Positions.FirstOrDefault(position => position.Id == positionId);
            if (brokerPosition == null)
                continue;

            ReconcileSmartPosition(brokerPosition, evaluateAlerts: true);
        }
    }

    private void RunSmartPositionSafetyReconciliation()
    {
        if (Server.TimeInUtc < _lastSmartSafetyReconciliation.AddSeconds(2))
            return;

        _lastSmartSafetyReconciliation = Server.TimeInUtc;
        foreach (var positionId in _runtimeState.SmartPositions.Keys.ToArray())
        {
            var brokerPosition = Positions.FirstOrDefault(position => position.Id == positionId);
            if (brokerPosition == null)
            {
                _runtimeState.SmartPositions.Remove(positionId);
                Print($"Smart Position Manager position {positionId}: broker position no longer exists; active monitoring state removed.");
                continue;
            }

            ReconcileSmartPosition(brokerPosition, evaluateAlerts: false);
        }
    }

    private void OnSmartPositionOpened(PositionOpenedEventArgs args)
    {
        if (_runtimeState.SmartPositions.ContainsKey(args.Position.Id))
            ReconcileSmartPosition(args.Position, evaluateAlerts: false);
    }

    private void OnSmartPositionModified(PositionModifiedEventArgs args)
    {
        if (_runtimeState.SmartPositions.ContainsKey(args.Position.Id))
            ReconcileSmartPosition(args.Position, evaluateAlerts: false);
    }

    private void OnSmartPositionClosed(PositionClosedEventArgs args)
    {
        if (!_runtimeState.SmartPositions.TryGetValue(args.Position.Id, out var state))
            return;

        var snapshot = BuildSmartPositionSnapshot(args.Position, observedPrice: null, isOpen: false);
        var evaluation = SmartPositionEngine.Evaluate(snapshot, new SmartPositionSettings(), state);
        if (evaluation.NextState == null)
            _runtimeState.SmartPositions.Remove(args.Position.Id);

        foreach (var diagnostic in evaluation.Diagnostics)
            Print($"Smart Position Manager position {args.Position.Id}: {diagnostic}");
    }

    private void ReconcileSmartPosition(Position brokerPosition, bool evaluateAlerts)
    {
        if (!_runtimeState.SmartPositions.TryGetValue(brokerPosition.Id, out var state))
            return;

        var symbol = Symbols.GetSymbol(brokerPosition.SymbolName);
        if (symbol == null)
        {
            Print($"Smart Position Manager position {brokerPosition.Id}: symbol {brokerPosition.SymbolName} is unavailable; reconciliation skipped.");
            return;
        }

        var direction = brokerPosition.TradeType == TradeType.Buy
            ? SmartPositionDirection.Long
            : SmartPositionDirection.Short;
        double? observedPrice = null;
        if (evaluateAlerts)
            observedPrice = direction == SmartPositionDirection.Long ? symbol.Bid : symbol.Ask;

        var snapshot = BuildSmartPositionSnapshot(brokerPosition, observedPrice, isOpen: true);
        var evaluation = SmartPositionEngine.Evaluate(snapshot, new SmartPositionSettings(), state);

        if (evaluation.NextState == null)
        {
            _runtimeState.SmartPositions.Remove(brokerPosition.Id);
        }
        else
        {
            _runtimeState.SmartPositions[brokerPosition.Id] = evaluation.NextState;
            ExecuteSmartPositionActions(brokerPosition, symbol, evaluation.Actions);
        }

        foreach (var alertEvent in evaluation.Alerts)
        {
            if (SmartAlertHistoryRepository.AppendDistinct(_runtimeState.SmartAlertHistory, new[] { alertEvent }) == 0)
                continue;
            ShowSmartAlert(alertEvent);
        }

        foreach (var diagnostic in evaluation.Diagnostics)
            Print($"Smart Position Manager position {brokerPosition.Id}: {diagnostic}");
    }

    private void ExecuteSmartPositionActions(Position brokerPosition, Symbol symbol, System.Collections.Generic.IReadOnlyList<SmartPositionAction> actions)
    {
        foreach (var action in actions)
        {
            if (!_runtimeState.SmartPositions.TryGetValue(brokerPosition.Id, out var state))
                return;

            if (_runtimeFaulted)
            {
                _runtimeState.SmartPositions[brokerPosition.Id] = SmartPositionEngine.RecordStopActionResult(
                    state,
                    action.ActionId,
                    SmartActionExecutionStatus.Rejected,
                    diagnosticError: "Plugin runtime is faulted; financial action was blocked.");
                continue;
            }

            if (action.ActionType != SmartPositionActionType.ImproveStopLoss)
                continue;

            var result = PositionManagementService.ImproveStop(brokerPosition, action.RequestedStopPrice, symbol);
            var status = result.Status switch
            {
                StopModificationStatus.Succeeded => SmartActionExecutionStatus.AcceptedAwaitingReconciliation,
                StopModificationStatus.NoOp => SmartActionExecutionStatus.NoOp,
                _ => SmartActionExecutionStatus.Rejected
            };
            _runtimeState.SmartPositions[brokerPosition.Id] = SmartPositionEngine.RecordStopActionResult(
                state,
                action.ActionId,
                status,
                result.NormalizedRequestedStopPrice,
                result.Error);

            if (result.Status == StopModificationStatus.Failed)
                Print($"Smart Position Manager position {brokerPosition.Id}: {action.Reason} stop modification rejected: {result.Error}");
        }
    }

    private SmartPositionSnapshot BuildSmartPositionSnapshot(Position brokerPosition, double? observedPrice, bool isOpen)
    {
        return new SmartPositionSnapshot
        {
            PositionId = brokerPosition.Id,
            SymbolName = brokerPosition.SymbolName,
            Direction = brokerPosition.TradeType == TradeType.Buy ? SmartPositionDirection.Long : SmartPositionDirection.Short,
            EntryPrice = brokerPosition.EntryPrice,
            VolumeInUnits = brokerPosition.VolumeInUnits,
            StopLoss = brokerPosition.StopLoss,
            TakeProfit = brokerPosition.TakeProfit,
            ObservedPrice = observedPrice,
            ObservedAtUtc = Server.TimeInUtc,
            IsOpen = isOpen
        };
    }

    private void ShowSmartAlert(SmartAlertEvent alertEvent)
    {
        var side = alertEvent.Direction == SmartPositionDirection.Long ? "LONG" : "SHORT";
        var message = $"{alertEvent.AlertType} | {alertEvent.SymbolName} {side} | trigger {alertEvent.TriggerPrice} | observed {alertEvent.ObservedPrice}";
        Notifications.ShowPopup("Smart Position Alert", message, PopupNotificationState.Information);
    }
}
