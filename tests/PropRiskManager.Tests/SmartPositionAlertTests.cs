using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PropRiskManager.Domain;

namespace PropRiskManager.Tests;

[TestClass]
public sealed class SmartPositionAlertTests
{
    private static readonly DateTime T0 = new(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void Enrollment_ArmsOnlyAvailableAlertDefinitions()
    {
        var result = SmartPositionEngine.Evaluate(
            Snapshot(stopLoss: 95, observedPrice: 100),
            new SmartPositionSettings
            {
                EnrollRequested = true,
                PartialProfitTriggerPrice = 110
            },
            null);

        Assert.IsNotNull(result.NextState);
        Assert.AreEqual(2, result.NextState.AlertDefinitions.Count);
        Assert.AreEqual(2, SmartPositionEngine.CountArmedAlerts(new[] { result.NextState }));
        Assert.IsTrue(result.NextState.AlertDefinitions.Exists(d => d.AlertType == SmartAlertType.PartialProfit));
        Assert.IsTrue(result.NextState.AlertDefinitions.Exists(d => d.AlertType == SmartAlertType.StopLoss));
        Assert.IsFalse(result.NextState.AlertDefinitions.Exists(d => d.AlertType == SmartAlertType.BreakEven));
    }

    [TestMethod]
    public void LongTriggers_AreSymmetricAndOneShot()
    {
        var enrolled = Enroll(SmartPositionDirection.Long, partial: 110, breakEven: 105, stop: 95);

        var first = SmartPositionEngine.Evaluate(
            Snapshot(SmartPositionDirection.Long, observedPrice: 111, stopLoss: 95, observedAt: T0.AddSeconds(1)),
            new SmartPositionSettings(),
            enrolled);

        Assert.AreEqual(2, first.Alerts.Count);
        Assert.AreEqual(0, first.Actions.Count);

        var repeated = SmartPositionEngine.Evaluate(
            Snapshot(SmartPositionDirection.Long, observedPrice: 109, stopLoss: 95, observedAt: T0.AddSeconds(2)),
            new SmartPositionSettings(),
            first.NextState);

        Assert.AreEqual(0, repeated.Alerts.Count);

        var stop = SmartPositionEngine.Evaluate(
            Snapshot(SmartPositionDirection.Long, observedPrice: 94, stopLoss: 95, observedAt: T0.AddSeconds(3)),
            new SmartPositionSettings(),
            repeated.NextState);

        Assert.AreEqual(1, stop.Alerts.Count);
        Assert.AreEqual(SmartAlertType.StopLoss, stop.Alerts[0].AlertType);
        Assert.AreEqual(0, SmartPositionEngine.CountArmedAlerts(new[] { stop.NextState! }));
    }

    [TestMethod]
    public void ShortTriggers_AreDirectionallySymmetric()
    {
        var enrolled = Enroll(SmartPositionDirection.Short, partial: 90, breakEven: 95, stop: 105);

        var favorable = SmartPositionEngine.Evaluate(
            Snapshot(SmartPositionDirection.Short, observedPrice: 89, stopLoss: 105, observedAt: T0.AddSeconds(1)),
            new SmartPositionSettings(),
            enrolled);

        Assert.AreEqual(2, favorable.Alerts.Count);
        Assert.IsTrue(favorable.Alerts.Any(e => e.AlertType == SmartAlertType.PartialProfit));
        Assert.IsTrue(favorable.Alerts.Any(e => e.AlertType == SmartAlertType.BreakEven));

        var adverse = SmartPositionEngine.Evaluate(
            Snapshot(SmartPositionDirection.Short, observedPrice: 106, stopLoss: 105, observedAt: T0.AddSeconds(2)),
            new SmartPositionSettings(),
            favorable.NextState);

        Assert.AreEqual(1, adverse.Alerts.Count);
        Assert.AreEqual(SmartAlertType.StopLoss, adverse.Alerts[0].AlertType);
    }

    [TestMethod]
    public void RemoveAlerts_DisarmsWithoutRemovingPositionOrCreatingActions()
    {
        var enrolled = Enroll(SmartPositionDirection.Long, partial: 110, breakEven: 105, stop: 95);

        var result = SmartPositionEngine.Evaluate(
            Snapshot(observedPrice: 100, stopLoss: 95),
            new SmartPositionSettings { RemoveAlertsRequested = true },
            enrolled);

        Assert.IsNotNull(result.NextState);
        Assert.AreEqual(0, result.Actions.Count);
        Assert.AreEqual(0, result.Alerts.Count);
        Assert.AreEqual(0, SmartPositionEngine.CountArmedAlerts(new[] { result.NextState }));
        Assert.IsTrue(result.NextState.AlertDefinitions.TrueForAll(d => d.State == SmartAlertState.Disarmed));
    }

    [TestMethod]
    public void Rearm_RequiresExplicitRequestAndCanUseUpdatedTrigger()
    {
        var enrolled = Enroll(SmartPositionDirection.Long, partial: 110, breakEven: null, stop: null);
        var fired = SmartPositionEngine.Evaluate(
            Snapshot(observedPrice: 111, stopLoss: null, observedAt: T0.AddSeconds(1)),
            new SmartPositionSettings(),
            enrolled);

        Assert.AreEqual(1, fired.Alerts.Count);

        var noRearm = SmartPositionEngine.Evaluate(
            Snapshot(observedPrice: 120, stopLoss: null, observedAt: T0.AddSeconds(2)),
            new SmartPositionSettings(),
            fired.NextState);
        Assert.AreEqual(0, noRearm.Alerts.Count);

        var rearmed = SmartPositionEngine.Evaluate(
            Snapshot(observedPrice: 100, stopLoss: null, observedAt: T0.AddSeconds(3)),
            new SmartPositionSettings
            {
                RearmAlertsRequested = true,
                PartialProfitTriggerPrice = 115
            },
            noRearm.NextState);

        Assert.AreEqual(1, SmartPositionEngine.CountArmedAlerts(new[] { rearmed.NextState! }));
        Assert.AreEqual(115, rearmed.NextState!.AlertDefinitions[0].TriggerPrice, 0.000001);
    }

    [TestMethod]
    public void RestartedTriggeredState_DoesNotReplayEvent()
    {
        var enrolled = Enroll(SmartPositionDirection.Long, partial: 110, breakEven: null, stop: null);
        var fired = SmartPositionEngine.Evaluate(
            Snapshot(observedPrice: 111, stopLoss: null, observedAt: T0.AddSeconds(1)),
            new SmartPositionSettings(),
            enrolled);

        var afterRestart = SmartPositionEngine.Evaluate(
            Snapshot(observedPrice: 120, stopLoss: null, observedAt: T0.AddMinutes(1)),
            new SmartPositionSettings(),
            fired.NextState);

        Assert.AreEqual(0, afterRestart.Alerts.Count);
        Assert.AreEqual(SmartAlertState.Triggered, afterRestart.NextState!.AlertDefinitions[0].State);
    }

    [TestMethod]
    public void ExplicitVirtualStop_ArmsOnlyWhenBrokerStopIsMissing()
    {
        var virtualResult = SmartPositionEngine.Evaluate(
            Snapshot(stopLoss: null, observedPrice: 100),
            new SmartPositionSettings { EnrollRequested = true, VirtualStopLossPrice = 95 },
            null);

        Assert.AreEqual(1, virtualResult.NextState!.AlertDefinitions.Count);
        Assert.IsTrue(virtualResult.NextState.AlertDefinitions[0].IsVirtualStopLoss);

        var brokerResult = SmartPositionEngine.Evaluate(
            Snapshot(stopLoss: 96, observedPrice: 100),
            new SmartPositionSettings { EnrollRequested = true, VirtualStopLossPrice = 95 },
            null);

        Assert.AreEqual(1, brokerResult.NextState!.AlertDefinitions.Count);
        Assert.IsFalse(brokerResult.NextState.AlertDefinitions[0].IsVirtualStopLoss);
        Assert.AreEqual(96, brokerResult.NextState.AlertDefinitions[0].TriggerPrice, 0.000001);
    }

    [TestMethod]
    public void HistoryRepository_AppendsEachEventOnceAndRetrievesByPosition()
    {
        var history = new List<SmartAlertEvent>();
        var alertEvent = new SmartAlertEvent
        {
            EventId = "42:PartialProfit:1",
            PositionId = 42,
            SymbolName = "EURUSD",
            Direction = SmartPositionDirection.Long,
            AlertType = SmartAlertType.PartialProfit,
            TriggerPrice = 110,
            ObservedPrice = 111,
            TriggeredAtUtc = T0,
            RequestedAction = "None",
            ActionResult = "MonitoringOnly"
        };

        Assert.AreEqual(1, SmartAlertHistoryRepository.AppendDistinct(history, new[] { alertEvent }));
        Assert.AreEqual(0, SmartAlertHistoryRepository.AppendDistinct(history, new[] { alertEvent }));
        Assert.AreEqual(1, SmartAlertHistoryRepository.GetForPosition(history, 42).Count);
        Assert.AreEqual(0, SmartAlertHistoryRepository.GetForPosition(history, 7).Count);
    }

    private static SmartPositionState Enroll(SmartPositionDirection direction, double? partial, double? breakEven, double? stop)
    {
        var result = SmartPositionEngine.Evaluate(
            Snapshot(direction, stopLoss: stop, observedPrice: 100),
            new SmartPositionSettings
            {
                EnrollRequested = true,
                PartialProfitTriggerPrice = partial,
                BreakEvenTriggerPrice = breakEven,
                StopLossTriggerPrice = stop
            },
            null);
        return result.NextState!;
    }

    private static SmartPositionSnapshot Snapshot(
        SmartPositionDirection direction = SmartPositionDirection.Long,
        double? observedPrice = null,
        double? stopLoss = 95,
        DateTime? observedAt = null)
    {
        return new SmartPositionSnapshot
        {
            PositionId = 42,
            SymbolName = "EURUSD",
            Direction = direction,
            EntryPrice = 100,
            VolumeInUnits = 1000,
            StopLoss = stopLoss,
            TakeProfit = direction == SmartPositionDirection.Long ? 120 : 80,
            ObservedPrice = observedPrice,
            ObservedAtUtc = observedAt ?? T0,
            IsOpen = true
        };
    }
}
