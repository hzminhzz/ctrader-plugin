using Microsoft.VisualStudio.TestTools.UnitTesting;
using PropRiskManager.Domain;

namespace PropRiskManager.Tests;

[TestClass]
public sealed class SmartPositionEngineTests
{
    private static readonly DateTime ObservedAt = new(2026, 8, 12, 9, 30, 0, DateTimeKind.Utc);

    [TestMethod]
    public void Evaluate_EnrollsLongPosition_WithCapturedReferences()
    {
        var snapshot = Snapshot(
            direction: SmartPositionDirection.Long,
            entry: 1.1000,
            stopLoss: 1.0950,
            takeProfit: 1.1100,
            volume: 100_000);

        var result = SmartPositionEngine.Evaluate(snapshot, Enroll(), null);

        Assert.AreEqual(0, result.Actions.Count);
        Assert.AreEqual(0, result.Alerts.Count);
        Assert.AreEqual(0, result.Diagnostics.Count);
        Assert.IsNotNull(result.NextState);
        Assert.AreEqual(snapshot.PositionId, result.NextState.PositionId);
        Assert.AreEqual(snapshot.SymbolName, result.NextState.SymbolName);
        Assert.AreEqual(SmartPositionDirection.Long, result.NextState.Direction);
        Assert.AreEqual(1.1000, result.NextState.EntryPrice, 0.0000001);
        Assert.AreEqual(100_000, result.NextState.OriginalVolumeInUnits, 0.000001);
        Assert.AreEqual(100_000, result.NextState.CurrentVolumeInUnits, 0.000001);
        Assert.AreEqual(1.0950, result.NextState.InitialStopLoss!.Value, 0.0000001);
        Assert.AreEqual(1.1100, result.NextState.InitialTakeProfit!.Value, 0.0000001);
        Assert.AreEqual(ObservedAt, result.NextState.MonitoringStartedAtUtc);
        Assert.AreEqual(SmartPositionPhase.MonitoringPreBreakEven, result.NextState.Phase);
    }

    [TestMethod]
    public void Evaluate_EnrollsShortPosition_WithCapturedReferences()
    {
        var snapshot = Snapshot(
            direction: SmartPositionDirection.Short,
            entry: 1.1000,
            stopLoss: 1.1050,
            takeProfit: 1.0900,
            volume: 75_000);

        var result = SmartPositionEngine.Evaluate(snapshot, Enroll(), null);

        Assert.IsNotNull(result.NextState);
        Assert.AreEqual(SmartPositionDirection.Short, result.NextState.Direction);
        Assert.AreEqual(1.1050, result.NextState.InitialStopLoss!.Value, 0.0000001);
        Assert.AreEqual(1.0900, result.NextState.InitialTakeProfit!.Value, 0.0000001);
        Assert.AreEqual(75_000, result.NextState.OriginalVolumeInUnits, 0.000001);
    }

    [TestMethod]
    public void Evaluate_NonEnrolledPosition_ProducesNoBehavior()
    {
        var result = SmartPositionEngine.Evaluate(Snapshot(), new SmartPositionSettings(), null);

        Assert.IsNull(result.NextState);
        Assert.AreEqual(0, result.Actions.Count);
        Assert.AreEqual(0, result.Alerts.Count);
        Assert.AreEqual(0, result.Diagnostics.Count);
    }

    [TestMethod]
    public void Evaluate_RepeatedSnapshot_PreservesOriginalReferencesAndStartTime()
    {
        var initial = SmartPositionEngine.Evaluate(Snapshot(), Enroll(), null).NextState!;
        var changedSnapshot = Snapshot(
            entry: 1.2000,
            stopLoss: 1.1800,
            takeProfit: 1.2500,
            volume: 60_000,
            observedAt: ObservedAt.AddHours(1));

        var result = SmartPositionEngine.Evaluate(changedSnapshot, new SmartPositionSettings(), initial);

        Assert.IsNotNull(result.NextState);
        Assert.AreEqual(1.1000, result.NextState.EntryPrice, 0.0000001);
        Assert.AreEqual(100_000, result.NextState.OriginalVolumeInUnits, 0.000001);
        Assert.AreEqual(60_000, result.NextState.CurrentVolumeInUnits, 0.000001);
        Assert.AreEqual(1.0950, result.NextState.InitialStopLoss!.Value, 0.0000001);
        Assert.AreEqual(1.1100, result.NextState.InitialTakeProfit!.Value, 0.0000001);
        Assert.AreEqual(ObservedAt, result.NextState.MonitoringStartedAtUtc);
        Assert.AreEqual(0, result.Actions.Count);
        Assert.AreEqual(0, result.Alerts.Count);
    }

    [TestMethod]
    public void Evaluate_RestoredPersistedState_IsDeterministic()
    {
        var persisted = SmartPositionEngine.Evaluate(Snapshot(), Enroll(), null).NextState!;

        var first = SmartPositionEngine.Evaluate(Snapshot(volume: 80_000), new SmartPositionSettings(), persisted);
        var second = SmartPositionEngine.Evaluate(Snapshot(volume: 80_000), new SmartPositionSettings(), persisted);

        AssertStatesEqual(first.NextState!, second.NextState!);
        Assert.AreEqual(100_000, first.NextState!.OriginalVolumeInUnits, 0.000001);
        Assert.AreEqual(80_000, first.NextState.CurrentVolumeInUnits, 0.000001);
    }

    [TestMethod]
    public void Evaluate_RemoveMonitoring_ReturnsNoStateAndNoBrokerAction()
    {
        var persisted = SmartPositionEngine.Evaluate(Snapshot(), Enroll(), null).NextState!;

        var result = SmartPositionEngine.Evaluate(
            Snapshot(),
            new SmartPositionSettings { RemoveMonitoringRequested = true },
            persisted);

        Assert.IsNull(result.NextState);
        Assert.AreEqual(0, result.Actions.Count);
        Assert.AreEqual(0, result.Alerts.Count);
    }

    [TestMethod]
    public void Evaluate_MissingOptionalProtectionReferences_AreExplicitlyUnavailable()
    {
        var result = SmartPositionEngine.Evaluate(
            Snapshot(stopLoss: null, takeProfit: null),
            Enroll(),
            null);

        Assert.IsNotNull(result.NextState);
        Assert.IsNull(result.NextState.InitialStopLoss);
        Assert.IsNull(result.NextState.InitialTakeProfit);
        Assert.AreEqual(0, result.Diagnostics.Count);
    }

    [TestMethod]
    public void Evaluate_InvalidOptionalProtectionReferences_AreNotGuessed()
    {
        var result = SmartPositionEngine.Evaluate(
            Snapshot(
                direction: SmartPositionDirection.Long,
                stopLoss: 1.1050,
                takeProfit: 1.0900),
            Enroll(),
            null);

        Assert.IsNotNull(result.NextState);
        Assert.IsNull(result.NextState.InitialStopLoss);
        Assert.IsNull(result.NextState.InitialTakeProfit);
        Assert.AreEqual(2, result.Diagnostics.Count);
    }

    [TestMethod]
    public void Evaluate_InvalidRequiredEnrollmentInput_DoesNotEnroll()
    {
        var result = SmartPositionEngine.Evaluate(
            Snapshot(positionId: 0, symbolName: string.Empty, entry: 0, volume: 0, isOpen: false, observedAt: default),
            Enroll(),
            null);

        Assert.IsNull(result.NextState);
        Assert.AreEqual(6, result.Diagnostics.Count);
        Assert.AreEqual(0, result.Actions.Count);
        Assert.AreEqual(0, result.Alerts.Count);
    }

    private static SmartPositionSettings Enroll() => new() { EnrollRequested = true };

    private static SmartPositionSnapshot Snapshot(
        int positionId = 42,
        string symbolName = "EURUSD",
        SmartPositionDirection direction = SmartPositionDirection.Long,
        double entry = 1.1000,
        double? stopLoss = 1.0950,
        double? takeProfit = 1.1100,
        double volume = 100_000,
        DateTime? observedAt = null,
        bool isOpen = true)
    {
        return new SmartPositionSnapshot
        {
            PositionId = positionId,
            SymbolName = symbolName,
            Direction = direction,
            EntryPrice = entry,
            StopLoss = stopLoss,
            TakeProfit = takeProfit,
            VolumeInUnits = volume,
            ObservedAtUtc = observedAt ?? ObservedAt,
            IsOpen = isOpen
        };
    }

    private static void AssertStatesEqual(SmartPositionState expected, SmartPositionState actual)
    {
        Assert.AreEqual(expected.PositionId, actual.PositionId);
        Assert.AreEqual(expected.SymbolName, actual.SymbolName);
        Assert.AreEqual(expected.Direction, actual.Direction);
        Assert.AreEqual(expected.EntryPrice, actual.EntryPrice, 0.0000001);
        Assert.AreEqual(expected.OriginalVolumeInUnits, actual.OriginalVolumeInUnits, 0.000001);
        Assert.AreEqual(expected.CurrentVolumeInUnits, actual.CurrentVolumeInUnits, 0.000001);
        Assert.AreEqual(expected.InitialStopLoss, actual.InitialStopLoss);
        Assert.AreEqual(expected.InitialTakeProfit, actual.InitialTakeProfit);
        Assert.AreEqual(expected.MonitoringStartedAtUtc, actual.MonitoringStartedAtUtc);
        Assert.AreEqual(expected.Phase, actual.Phase);
    }
}
