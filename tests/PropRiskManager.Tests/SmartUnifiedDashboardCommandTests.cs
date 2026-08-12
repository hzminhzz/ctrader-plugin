using Microsoft.VisualStudio.TestTools.UnitTesting;
using PropRiskManager.Domain;

namespace PropRiskManager.Tests;

[TestClass]
public sealed class SmartUnifiedDashboardCommandTests
{
    private static readonly DateTime T0 = new(2026, 8, 12, 16, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void PointsEnrollment_UsesOnlyAlreadyResolvedPriceDistances()
    {
        var p = SmartManagementProfileResolver.BuiltInDefaults();
        p.Mode = SmartManagementMode.Points; p.FinancialStopManagementEnabled = true; p.PreBreakEvenTrailingEnabled = true; p.BreakEvenEnabled = true; p.PostBreakEvenTrailingEnabled = true;
        p.BreakEvenTrigger = 5; p.BreakEvenAdjustment = 1; p.PreBreakEvenTrailingAdjustment = 3; p.PostBreakEvenTrailingAdjustment = 2;
        p.FinancialPartialProfitEnabled = true; p.MultiPartialProfitEnabled = true; p.PartialProfitSpacing = 4; p.PartialProfitClosePercent = 25;
        var r = SmartManagementEnrollmentResolver.Resolve(p, Snapshot(SmartPositionDirection.Long, takeProfit: 120));
        Assert.AreEqual(0, r.Diagnostics.Count);
        Assert.AreEqual(105, r.Settings.BreakEvenFinancialTriggerPrice!.Value, 0.000001);
        Assert.AreEqual(1, r.Settings.BreakEvenAdjustmentPriceDistance!.Value, 0.000001);
        Assert.AreEqual(104, r.Settings.PartialProfitTriggerPrice!.Value, 0.000001);
        Assert.AreEqual(4, r.Settings.PartialProfitSpacingPriceDistance!.Value, 0.000001);
    }

    [TestMethod]
    public void PercentageEnrollment_ResolvesAlertLevelsFromInitialTp()
    {
        var p = SmartManagementProfileResolver.BuiltInDefaults();
        p.Mode = SmartManagementMode.Percentage; p.BreakEvenTrigger = 25; p.BreakEvenAdjustment = 10; p.PreBreakEvenTrailingAdjustment = 20; p.PostBreakEvenTrailingAdjustment = 15; p.PartialProfitSpacing = 20;
        p.FinancialStopManagementEnabled = true; p.BreakEvenEnabled = true; p.FinancialPartialProfitEnabled = true;
        var r = SmartManagementEnrollmentResolver.Resolve(p, Snapshot(SmartPositionDirection.Short, stopLoss: 110, takeProfit: 80));
        Assert.AreEqual(0, r.Diagnostics.Count);
        Assert.AreEqual(95, r.Settings.BreakEvenTriggerPrice!.Value, 0.000001);
        Assert.AreEqual(96, r.Settings.PartialProfitTriggerPrice!.Value, 0.000001);
    }

    [TestMethod]
    public void PercentageMissingTp_DowngradesProfitFinancialRulesWithoutGuessing()
    {
        var p = SmartManagementProfileResolver.BuiltInDefaults();
        p.Mode = SmartManagementMode.Percentage; p.FinancialPartialProfitEnabled = true; p.BreakEvenEnabled = true; p.FinancialStopManagementEnabled = true;
        var r = SmartManagementEnrollmentResolver.Resolve(p, Snapshot(SmartPositionDirection.Long, takeProfit: null));
        Assert.IsTrue(r.Diagnostics.Count > 0);
        Assert.IsNull(r.Settings.PartialProfitTriggerPrice);
        Assert.IsNull(r.Settings.BreakEvenTriggerPrice);
        Assert.IsFalse(r.Settings.FinancialPartialProfitEnabled);
        Assert.IsFalse(r.Settings.BreakEvenFinancialEnabled);
    }

    [TestMethod]
    public void RemoveAlertsCommand_DisarmsAlertsWithoutFinancialActions()
    {
        var snapshot = Snapshot(SmartPositionDirection.Long, observedPrice: 100);
        var state = SmartPositionEngine.Evaluate(snapshot, new SmartPositionSettings { EnrollRequested = true, PartialProfitTriggerPrice = 105, BreakEvenTriggerPrice = 104 }, null).NextState!;
        var r = SmartPositionEngine.Evaluate(Snapshot(SmartPositionDirection.Long, observedPrice: null, observedAt: T0.AddSeconds(1)), new SmartPositionSettings { RemoveAlertsRequested = true }, state);
        Assert.AreEqual(0, r.Actions.Count);
        Assert.AreEqual(0, r.Alerts.Count);
        Assert.IsTrue(r.NextState!.AlertDefinitions.TrueForAll(definition => definition.State != SmartAlertState.Armed));
    }

    private static SmartPositionSnapshot Snapshot(SmartPositionDirection direction, double stopLoss = 90, double? takeProfit = 120, double? observedPrice = 100, DateTime? observedAt = null) => new()
    {
        PositionId = 101, SymbolName = "TEST", Direction = direction, EntryPrice = 100, VolumeInUnits = 1000,
        StopLoss = direction == SmartPositionDirection.Long ? stopLoss : (stopLoss == 90 ? 110 : stopLoss), TakeProfit = takeProfit,
        ObservedPrice = observedPrice, ObservedAtUtc = observedAt ?? T0, IsOpen = true
    };
}
