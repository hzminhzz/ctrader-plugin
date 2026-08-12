using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PropRiskManager.Domain;

namespace PropRiskManager.Tests;

[TestClass]
public sealed class SmartPositionReconciliationTests
{
    private static readonly DateTime T0 = new(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void ExternalSlTpChanges_UpdateCurrentReferencesOnly()
    {
        var enrolled = Enroll(stopLoss: 95, takeProfit: 120);
        var result = SmartPositionEngine.Evaluate(Snapshot(stopLoss: 102, takeProfit: 130, observedAt: T0.AddMinutes(1)), new SmartPositionSettings(), enrolled);
        Assert.AreEqual(95, result.NextState!.InitialStopLoss!.Value, 0.000001);
        Assert.AreEqual(120, result.NextState.InitialTakeProfit!.Value, 0.000001);
        Assert.AreEqual(102, result.NextState.CurrentStopLoss!.Value, 0.000001);
        Assert.AreEqual(130, result.NextState.CurrentTakeProfit!.Value, 0.000001);
    }

    [TestMethod]
    public void BrokerStopAlert_TracksCurrentStopAcrossBreakEven()
    {
        var enrolled = SmartPositionEngine.Evaluate(Snapshot(stopLoss: 95), new SmartPositionSettings { EnrollRequested = true }, null).NextState!;
        var result = SmartPositionEngine.Evaluate(Snapshot(stopLoss: 102, observedAt: T0.AddSeconds(1)), new SmartPositionSettings(), enrolled);
        Assert.AreEqual(102, result.NextState!.CurrentStopLoss!.Value, 0.000001);
        Assert.AreEqual(102, result.NextState.AlertDefinitions[0].TriggerPrice, 0.000001);
    }

    [TestMethod]
    public void RemovedBrokerStop_DisarmsBrokerStopAlert()
    {
        var enrolled = SmartPositionEngine.Evaluate(Snapshot(stopLoss: 95), new SmartPositionSettings { EnrollRequested = true }, null).NextState!;
        var result = SmartPositionEngine.Evaluate(Snapshot(stopLoss: null, observedAt: T0.AddSeconds(1)), new SmartPositionSettings(), enrolled);
        Assert.IsNull(result.NextState!.CurrentStopLoss);
        Assert.AreEqual(SmartAlertState.Disarmed, result.NextState.AlertDefinitions[0].State);
    }

    [TestMethod]
    public void ExternalPartialClose_PreservesOriginalVolume()
    {
        var enrolled = Enroll(volume: 1000);
        var result = SmartPositionEngine.Evaluate(Snapshot(volume: 400, observedAt: T0.AddSeconds(1)), new SmartPositionSettings(), enrolled);
        Assert.AreEqual(1000, result.NextState!.OriginalVolumeInUnits, 0.000001);
        Assert.AreEqual(400, result.NextState.CurrentVolumeInUnits, 0.000001);
    }

    [TestMethod]
    public void UnchangedSnapshot_ProducesNoActionsOrEvents()
    {
        var enrolled = Enroll();
        var first = SmartPositionEngine.Evaluate(Snapshot(observedAt: T0.AddSeconds(1)), new SmartPositionSettings(), enrolled);
        var second = SmartPositionEngine.Evaluate(Snapshot(observedAt: T0.AddSeconds(1)), new SmartPositionSettings(), first.NextState);
        Assert.AreEqual(0, first.Actions.Count);
        Assert.AreEqual(0, first.Alerts.Count);
        Assert.AreEqual(0, second.Actions.Count);
        Assert.AreEqual(0, second.Alerts.Count);
    }

    [TestMethod]
    public void ClosedPosition_RemovesActiveStateWithoutEventReplay()
    {
        var result = SmartPositionEngine.Evaluate(Snapshot(isOpen: false, observedAt: T0.AddMinutes(1)), new SmartPositionSettings(), Enroll());
        Assert.IsNull(result.NextState);
        Assert.AreEqual(0, result.Actions.Count);
        Assert.AreEqual(0, result.Alerts.Count);
    }

    [TestMethod]
    public void RestartedTriggeredAlert_DoesNotReplay()
    {
        var enrolled = SmartPositionEngine.Evaluate(Snapshot(observedPrice: 100, stopLoss: null), new SmartPositionSettings { EnrollRequested = true, PartialProfitTriggerPrice = 110 }, null).NextState!;
        var fired = SmartPositionEngine.Evaluate(Snapshot(observedPrice: 111, stopLoss: null, observedAt: T0.AddSeconds(1)), new SmartPositionSettings(), enrolled);
        var afterRestart = SmartPositionEngine.Evaluate(Snapshot(observedPrice: 115, stopLoss: null, observedAt: T0.AddMinutes(2)), new SmartPositionSettings(), fired.NextState);
        Assert.AreEqual(0, afterRestart.Alerts.Count);
        Assert.AreEqual(SmartAlertState.Triggered, afterRestart.NextState!.AlertDefinitions[0].State);
    }

    [TestMethod]
    public void ContradictoryBrokerIdentity_FailsConservatively()
    {
        var result = SmartPositionEngine.Evaluate(Snapshot(symbol: "GBPUSD", entry: 101, observedAt: T0.AddSeconds(1)), new SmartPositionSettings(), Enroll());
        Assert.IsTrue(result.Diagnostics.Count >= 2);
        Assert.AreEqual(0, result.Actions.Count);
        Assert.AreEqual(0, result.Alerts.Count);
        Assert.AreEqual("EURUSD", result.NextState!.SymbolName);
    }

    [TestMethod]
    public void InvalidOpenVolume_RetainsPriorVolumeAndDiagnoses()
    {
        var result = SmartPositionEngine.Evaluate(Snapshot(volume: 0, observedAt: T0.AddSeconds(1)), new SmartPositionSettings(), Enroll(volume: 1000));
        Assert.AreEqual(1000, result.NextState!.CurrentVolumeInUnits, 0.000001);
        Assert.AreEqual(1, result.Diagnostics.Count);
    }

    private static SmartPositionState Enroll(double volume = 1000, double? stopLoss = 95, double? takeProfit = 120) => SmartPositionEngine.Evaluate(Snapshot(volume: volume, stopLoss: stopLoss, takeProfit: takeProfit), new SmartPositionSettings { EnrollRequested = true }, null).NextState!;

    private static SmartPositionSnapshot Snapshot(string symbol = "EURUSD", double entry = 100, double volume = 1000, double? stopLoss = 95, double? takeProfit = 120, double? observedPrice = null, DateTime? observedAt = null, bool isOpen = true) => new()
    {
        PositionId = 42, SymbolName = symbol, Direction = SmartPositionDirection.Long, EntryPrice = entry, VolumeInUnits = volume,
        StopLoss = stopLoss, TakeProfit = takeProfit, ObservedPrice = observedPrice, ObservedAtUtc = observedAt ?? T0, IsOpen = isOpen
    };
}
