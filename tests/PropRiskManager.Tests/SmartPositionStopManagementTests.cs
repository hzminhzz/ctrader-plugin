using Microsoft.VisualStudio.TestTools.UnitTesting;
using PropRiskManager.Domain;

namespace PropRiskManager.Tests;

[TestClass]
public sealed class SmartPositionStopManagementTests
{
    private static readonly DateTime T0 = new(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void PreBreakEvenTrailing_Long_EmitsOnlyImprovingStop()
    {
        var state = Enroll(Snapshot(SmartPositionDirection.Long, 90, 100), Plan(preDistance: 5));
        var result = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 90, 104, T0.AddSeconds(1)), new SmartPositionSettings(), state);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.AreEqual(SmartStopActionReason.PreBreakEvenTrailing, result.Actions[0].Reason);
        Assert.AreEqual(99, result.Actions[0].RequestedStopPrice, 0.000001);
        Assert.AreEqual(SmartPositionPhase.MonitoringPreBreakEven, result.NextState!.Phase);
        var replay = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 90, 104, T0.AddSeconds(1)), new SmartPositionSettings(), result.NextState);
        Assert.AreEqual(0, replay.Actions.Count);
    }

    [TestMethod]
    public void PreBreakEvenTrailing_Short_IsSymmetric()
    {
        var state = Enroll(Snapshot(SmartPositionDirection.Short, 110, 100), Plan(preDistance: 5));
        var result = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Short, 110, 96, T0.AddSeconds(1)), new SmartPositionSettings(), state);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.AreEqual(101, result.Actions[0].RequestedStopPrice, 0.000001);
    }

    [TestMethod]
    public void BreakEven_Long_WaitsForBrokerConfirmationThenUsesPostTrail()
    {
        var state = Enroll(Snapshot(SmartPositionDirection.Long, 90, 100), Plan(beTrigger: 105, beAdjustment: 2, postDistance: 3));
        var requested = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 90, 106, T0.AddSeconds(1)), new SmartPositionSettings(), state);
        Assert.AreEqual(1, requested.Actions.Count);
        Assert.AreEqual(SmartStopActionReason.BreakEven, requested.Actions[0].Reason);
        Assert.AreEqual(102, requested.Actions[0].RequestedStopPrice, 0.000001);
        Assert.AreEqual(SmartPositionPhase.MonitoringPreBreakEven, requested.NextState!.Phase);
        var accepted = SmartPositionEngine.RecordStopActionResult(requested.NextState, requested.Actions[0].ActionId, SmartActionExecutionStatus.AcceptedAwaitingReconciliation, 102);
        Assert.AreEqual(SmartPositionPhase.MonitoringPreBreakEven, accepted.Phase);
        var confirmed = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 102, 106, T0.AddSeconds(2)), new SmartPositionSettings(), accepted);
        Assert.AreEqual(SmartPositionPhase.BreakEven, confirmed.NextState!.Phase);
        Assert.AreEqual(0, confirmed.Actions.Count);
        var post = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 102, 110, T0.AddSeconds(3)), new SmartPositionSettings(), confirmed.NextState);
        Assert.AreEqual(SmartPositionPhase.PostBreakEvenTrailing, post.NextState!.Phase);
        Assert.AreEqual(1, post.Actions.Count);
        Assert.AreEqual(SmartStopActionReason.PostBreakEvenTrailing, post.Actions[0].Reason);
        Assert.AreEqual(107, post.Actions[0].RequestedStopPrice, 0.000001);
    }

    [TestMethod]
    public void BreakEven_Short_UsesEntryMinusAdjustment()
    {
        var state = Enroll(Snapshot(SmartPositionDirection.Short, 110, 100), Plan(beTrigger: 95, beAdjustment: 2));
        var result = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Short, 110, 94, T0.AddSeconds(1)), new SmartPositionSettings(), state);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.AreEqual(98, result.Actions[0].RequestedStopPrice, 0.000001);
    }

    [TestMethod]
    public void BrokerRejection_DoesNotAdvanceOrSpamBreakEven()
    {
        var state = Enroll(Snapshot(SmartPositionDirection.Long, 90, 100), Plan(beTrigger: 105, beAdjustment: 2));
        var requested = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 90, 106, T0.AddSeconds(1)), new SmartPositionSettings(), state);
        var rejected = SmartPositionEngine.RecordStopActionResult(requested.NextState!, requested.Actions[0].ActionId, SmartActionExecutionStatus.Rejected, diagnosticError: "broker rejected");
        var replay = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 90, 106, T0.AddSeconds(2)), new SmartPositionSettings(), rejected);
        Assert.AreEqual(SmartPositionPhase.MonitoringPreBreakEven, replay.NextState!.Phase);
        Assert.AreEqual(0, replay.Actions.Count);
        Assert.AreEqual(SmartActionExecutionStatus.Rejected, replay.NextState.PendingStopAction!.ExecutionStatus);
    }

    [TestMethod]
    public void AlreadyBetterBrokerStop_AdvancesFromBrokerStateWithoutNoOpRequest()
    {
        var state = Enroll(Snapshot(SmartPositionDirection.Long, 103, 104), Plan(beTrigger: 105, beAdjustment: 2));
        var result = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 103, 106, T0.AddSeconds(1)), new SmartPositionSettings(), state);
        Assert.AreEqual(0, result.Actions.Count);
        Assert.AreEqual(SmartPositionPhase.BreakEven, result.NextState!.Phase);
    }

    [TestMethod]
    public void TrailingNeverWorsensExistingProtection()
    {
        var state = Enroll(Snapshot(SmartPositionDirection.Long, 99, 100), Plan(preDistance: 5));
        var result = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 99, 102, T0.AddSeconds(1)), new SmartPositionSettings(), state);
        Assert.AreEqual(0, result.Actions.Count);
        Assert.AreEqual(99, result.NextState!.CurrentStopLoss!.Value, 0.000001);
    }

    [TestMethod]
    public void InvalidDistances_DisableUnsafeComponents()
    {
        var result = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 90, 100), Plan(preDistance: -1, beTrigger: 105, beAdjustment: -2, postDistance: 0), null);
        Assert.IsNotNull(result.NextState);
        Assert.IsFalse(result.NextState!.StopManagement.Enabled);
        Assert.IsTrue(result.Diagnostics.Count >= 3);
        Assert.AreEqual(0, result.Actions.Count);
    }

    [TestMethod]
    public void FinancialAutomationDisabled_DoesNotDisableAlerts()
    {
        var enrolled = SmartPositionEngine.Evaluate(
            Snapshot(SmartPositionDirection.Long, 90, 100),
            new SmartPositionSettings { EnrollRequested = true, BreakEvenTriggerPrice = 105, ConfigureStopManagementRequested = true, FinancialStopManagementEnabled = false, BreakEvenFinancialEnabled = true, BreakEvenFinancialTriggerPrice = 105, BreakEvenAdjustmentPriceDistance = 2 },
            null).NextState!;
        var result = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 90, 106, T0.AddSeconds(1)), new SmartPositionSettings(), enrolled);
        Assert.AreEqual(0, result.Actions.Count);
        Assert.AreEqual(1, result.Alerts.Count);
        Assert.AreEqual(SmartAlertType.BreakEven, result.Alerts[0].AlertType);
    }

    private static SmartPositionState Enroll(SmartPositionSnapshot snapshot, SmartPositionSettings plan) => SmartPositionEngine.Evaluate(snapshot, plan, null).NextState!;

    private static SmartPositionSettings Plan(double? preDistance = null, double? beTrigger = null, double? beAdjustment = null, double? postDistance = null) =>
        new()
        {
            EnrollRequested = true,
            ConfigureStopManagementRequested = true,
            FinancialStopManagementEnabled = true,
            PreBreakEvenTrailingEnabled = preDistance.HasValue,
            PreBreakEvenTrailingPriceDistance = preDistance,
            BreakEvenFinancialEnabled = beTrigger.HasValue || beAdjustment.HasValue,
            BreakEvenFinancialTriggerPrice = beTrigger,
            BreakEvenAdjustmentPriceDistance = beAdjustment,
            PostBreakEvenTrailingEnabled = postDistance.HasValue,
            PostBreakEvenTrailingPriceDistance = postDistance,
            StopImprovementEpsilon = 0.000001
        };

    private static SmartPositionSnapshot Snapshot(SmartPositionDirection direction, double stopLoss, double observedPrice, DateTime? observedAt = null) =>
        new()
        {
            PositionId = 42,
            SymbolName = "TEST",
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
