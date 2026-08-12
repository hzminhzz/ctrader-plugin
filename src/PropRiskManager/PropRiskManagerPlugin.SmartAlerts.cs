using System;
using System.Linq;
using cAlgo.API;
using PropRiskManager.Domain;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private DateTime _lastSmartSafetyReconciliation;

    private void RunSmartPositionMonitoring()
    {
        if (_runtimeState.SmartPositions.Count == 0) return;
        foreach (var positionId in _runtimeState.SmartPositions.Keys.ToArray())
        {
            var brokerPosition = Positions.FirstOrDefault(position => position.Id == positionId);
            if (brokerPosition != null) ReconcileSmartPosition(brokerPosition, true);
        }
    }

    private void RunSmartPositionSafetyReconciliation()
    {
        if (Server.TimeInUtc < _lastSmartSafetyReconciliation.AddSeconds(2)) return;
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
            ReconcileSmartPosition(brokerPosition, false);
        }
    }

    private void OnSmartPositionOpened(PositionOpenedEventArgs args)
    {
        if (_runtimeState.SmartPositions.ContainsKey(args.Position.Id)) ReconcileSmartPosition(args.Position, false);
    }

    private void OnSmartPositionModified(PositionModifiedEventArgs args)
    {
        if (_runtimeState.SmartPositions.ContainsKey(args.Position.Id)) ReconcileSmartPosition(args.Position, false);
    }

    private void OnSmartPositionClosed(PositionClosedEventArgs args)
    {
        if (!_runtimeState.SmartPositions.TryGetValue(args.Position.Id, out var state)) return;
        var evaluation = SmartPositionEngine.Evaluate(BuildSmartPositionSnapshot(args.Position, null, false), new SmartPositionSettings(), state);
        if (evaluation.NextState == null) _runtimeState.SmartPositions.Remove(args.Position.Id);
        foreach (var diagnostic in evaluation.Diagnostics) Print($"Smart Position Manager position {args.Position.Id}: {diagnostic}");
    }

    private void ReconcileSmartPosition(Position brokerPosition, bool evaluateAlerts)
    {
        if (!_runtimeState.SmartPositions.TryGetValue(brokerPosition.Id, out var state)) return;
        var symbol = Symbols.GetSymbol(brokerPosition.SymbolName);
        if (symbol == null)
        {
            Print($"Smart Position Manager position {brokerPosition.Id}: symbol {brokerPosition.SymbolName} is unavailable; reconciliation skipped.");
            return;
        }

        var direction = brokerPosition.TradeType == TradeType.Buy ? SmartPositionDirection.Long : SmartPositionDirection.Short;
        double? observedPrice = evaluateAlerts ? (direction == SmartPositionDirection.Long ? symbol.Bid : symbol.Ask) : null;
        var evaluation = SmartPositionEngine.Evaluate(BuildSmartPositionSnapshot(brokerPosition, observedPrice, true), new SmartPositionSettings(), state);

        if (evaluation.NextState == null) _runtimeState.SmartPositions.Remove(brokerPosition.Id);
        else _runtimeState.SmartPositions[brokerPosition.Id] = evaluation.NextState;

        foreach (var alertEvent in evaluation.Alerts)
        {
            if (SmartAlertHistoryRepository.AppendDistinct(_runtimeState.SmartAlertHistory, new[] { alertEvent }) == 0) continue;
            ShowSmartAlert(alertEvent);
        }
        foreach (var diagnostic in evaluation.Diagnostics) Print($"Smart Position Manager position {brokerPosition.Id}: {diagnostic}");
    }

    private SmartPositionSnapshot BuildSmartPositionSnapshot(Position brokerPosition, double? observedPrice, bool isOpen) => new()
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

    private void ShowSmartAlert(SmartAlertEvent alertEvent)
    {
        var side = alertEvent.Direction == SmartPositionDirection.Long ? "LONG" : "SHORT";
        var message = $"{alertEvent.AlertType} | {alertEvent.SymbolName} {side} | trigger {alertEvent.TriggerPrice} | observed {alertEvent.ObservedPrice}";
        Notifications.ShowPopup("Smart Position Alert", message, PopupNotificationState.Information);
    }
}
