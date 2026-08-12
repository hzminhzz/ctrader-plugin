using System;
using System.Linq;
using cAlgo.API;
using PropRiskManager.Domain;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private void RunSmartPositionMonitoring()
    {
        if (_runtimeState.SmartPositions.Count == 0)
            return;

        foreach (var positionId in _runtimeState.SmartPositions.Keys.ToArray())
        {
            var brokerPosition = Positions.FirstOrDefault(position => position.Id == positionId);
            if (brokerPosition == null)
                continue;

            var symbol = Symbols.GetSymbol(brokerPosition.SymbolName);
            if (symbol == null)
                continue;

            var direction = brokerPosition.TradeType == TradeType.Buy
                ? SmartPositionDirection.Long
                : SmartPositionDirection.Short;
            var observedPrice = direction == SmartPositionDirection.Long ? symbol.Bid : symbol.Ask;
            var snapshot = new SmartPositionSnapshot
            {
                PositionId = brokerPosition.Id,
                SymbolName = brokerPosition.SymbolName,
                Direction = direction,
                EntryPrice = brokerPosition.EntryPrice,
                VolumeInUnits = brokerPosition.VolumeInUnits,
                StopLoss = brokerPosition.StopLoss,
                TakeProfit = brokerPosition.TakeProfit,
                ObservedPrice = observedPrice,
                ObservedAtUtc = Server.TimeInUtc,
                IsOpen = true
            };

            var evaluation = SmartPositionEngine.Evaluate(
                snapshot,
                new SmartPositionSettings(),
                _runtimeState.SmartPositions[positionId]);

            if (evaluation.NextState != null)
                _runtimeState.SmartPositions[positionId] = evaluation.NextState;

            foreach (var alertEvent in evaluation.Alerts)
            {
                if (_runtimeState.SmartAlertHistory.Any(item => item.EventId == alertEvent.EventId))
                    continue;

                _runtimeState.SmartAlertHistory.Add(alertEvent);
                ShowSmartAlert(alertEvent);
            }

            foreach (var diagnostic in evaluation.Diagnostics)
                Print($"Smart Position Manager position {positionId}: {diagnostic}");
        }
    }

    private void ShowSmartAlert(SmartAlertEvent alertEvent)
    {
        var side = alertEvent.Direction == SmartPositionDirection.Long ? "LONG" : "SHORT";
        var message = $"{alertEvent.AlertType} | {alertEvent.SymbolName} {side} | trigger {alertEvent.TriggerPrice} | observed {alertEvent.ObservedPrice}";
        Notifications.ShowPopup("Smart Position Alert", message, PopupNotificationState.Information);
    }
}
