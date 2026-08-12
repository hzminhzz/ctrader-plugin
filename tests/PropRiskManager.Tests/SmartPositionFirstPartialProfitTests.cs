using Microsoft.VisualStudio.TestTools.UnitTesting;
using PropRiskManager.Domain;

namespace PropRiskManager.Tests;

[TestClass]
public sealed class SmartPositionFirstPartialProfitTests
{
    private static readonly DateTime T0 = new(2026, 8, 12, 13, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void FirstPartial_Long_EmitsOneActionAndWaitsForReconciliation()
    {
        var state = Enroll(SmartPositionDirection.Long, 105, 25);
        var first = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 106, 1000, T0.AddSeconds(1)), new SmartPositionSettings(), state);
        Assert.AreEqual(1, first.Actions.Count);
        Assert.AreEqual(SmartPositionActionType.PartialClose, first.Actions[0].ActionType);
        Assert.AreEqual(250, first.Actions[0].RequestedCloseVolumeInUnits, 0.000001);
        var replay = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 106, 1000, T0.AddSeconds(2)), new SmartPositionSettings(), first.NextState);
        Assert.AreEqual(0, replay.Actions.Count);
    }

    [TestMethod]
    public void FirstPartial_Short_TriggerIsSymmetric()
    {
        var state = Enroll(SmartPositionDirection.Short, 95, 20);
        var result = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Short, 94, 1000, T0.AddSeconds(1)), new SmartPositionSettings(), state);
        Assert.AreEqual(1, result.Actions.Count);
        Assert.AreEqual(200, result.Actions[0].RequestedCloseVolumeInUnits, 0.000001);
    }

    [TestMethod]
    public void BrokerVolumeDecrease_CompletesStageAndRestartCannotRepeatIt()
    {
        var state = Enroll(SmartPositionDirection.Long, 105, 25);
        var requested = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 106, 1000, T0.AddSeconds(1)), new SmartPositionSettings(), state);
        var accepted = SmartPositionEngine.RecordPartialProfitActionResult(requested.NextState!, requested.Actions[0].ActionId, SmartActionExecutionStatus.AcceptedAwaitingReconciliation);
        var reconciled = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 106, 750, T0.AddSeconds(2)), new SmartPositionSettings(), accepted);
        Assert.IsTrue(reconciled.NextState!.FirstPartialProfitCompleted);
        Assert.IsNull(reconciled.NextState.PendingPartialProfitAction);
        Assert.AreEqual(0, reconciled.Actions.Count);
        var afterRestart = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 108, 750, T0.AddMinutes(1)), new SmartPositionSettings(), reconciled.NextState);
        Assert.IsTrue(afterRestart.NextState!.FirstPartialProfitCompleted);
        Assert.AreEqual(0, afterRestart.Actions.Count);
    }

    [TestMethod]
    public void BrokerRejection_RemainsIncompleteAndRequiresExplicitRetry()
    {
        var state = Enroll(SmartPositionDirection.Long, 105, 25);
        var requested = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 106, 1000, T0.AddSeconds(1)), new SmartPositionSettings(), state);
        var rejected = SmartPositionEngine.RecordPartialProfitActionResult(requested.NextState!, requested.Actions[0].ActionId, SmartActionExecutionStatus.Rejected, "rejected");
        var noSpam = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 107, 1000, T0.AddSeconds(2)), new SmartPositionSettings(), rejected);
        Assert.IsFalse(noSpam.NextState!.FirstPartialProfitCompleted);
        Assert.AreEqual(0, noSpam.Actions.Count);
        var retry = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 107, 1000, T0.AddSeconds(3)), new SmartPositionSettings { RetryRejectedPartialProfitRequested = true }, noSpam.NextState);
        Assert.AreEqual(1, retry.Actions.Count);
    }

    [TestMethod]
    public void MonitorOnly_PartialProfitAlertStillFiresWithoutVolumeAction()
    {
        var enrolled = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 100, 1000, T0), new SmartPositionSettings
        {
            EnrollRequested = true,
            PartialProfitTriggerPrice = 105,
            ConfigurePartialProfitRequested = true,
            FinancialPartialProfitEnabled = false,
            FirstPartialProfitTriggerPrice = 105,
            PartialProfitClosePercent = 25
        }, null).NextState!;
        var result = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, 106, 1000, T0.AddSeconds(1)), new SmartPositionSettings(), enrolled);
        Assert.AreEqual(0, result.Actions.Count);
        Assert.AreEqual(1, result.Alerts.Count);
        Assert.AreEqual(SmartAlertType.PartialProfit, result.Alerts[0].AlertType);
    }

    [TestMethod]
    public void VolumePlanner_UsesNormalizedRemainderWithoutIncreasingExposure()
    {
        Assert.AreEqual(SmartVolumeReductionDecisionType.ModifyRemaining, SmartVolumeReductionPlanner.Decide(1000, 250, 100, 750).DecisionType);
        Assert.AreEqual(SmartVolumeReductionDecisionType.CloseAll, SmartVolumeReductionPlanner.Decide(1000, 950, 100, 0).DecisionType);
        Assert.AreEqual(SmartVolumeReductionDecisionType.NoChange, SmartVolumeReductionPlanner.Decide(1000, 1, 100, 1000).DecisionType);
        Assert.AreEqual(SmartVolumeReductionDecisionType.CloseAll, SmartVolumeReductionPlanner.Decide(1000, 1000, 100, 0).DecisionType);
    }

    private static SmartPositionState Enroll(SmartPositionDirection direction, double trigger, double closePercent) => SmartPositionEngine.Evaluate(
        Snapshot(direction, 100, 1000, T0),
        new SmartPositionSettings { EnrollRequested = true, ConfigurePartialProfitRequested = true, FinancialPartialProfitEnabled = true, FirstPartialProfitTriggerPrice = trigger, PartialProfitClosePercent = closePercent },
        null).NextState!;

    private static SmartPositionSnapshot Snapshot(SmartPositionDirection direction, double observedPrice, double volume, DateTime observedAt) => new()
    {
        PositionId = 77,
        SymbolName = "TEST",
        Direction = direction,
        EntryPrice = 100,
        VolumeInUnits = volume,
        StopLoss = direction == SmartPositionDirection.Long ? 90 : 110,
        TakeProfit = direction == SmartPositionDirection.Long ? 120 : 80,
        ObservedPrice = observedPrice,
        ObservedAtUtc = observedAt,
        IsOpen = true
    };
}
