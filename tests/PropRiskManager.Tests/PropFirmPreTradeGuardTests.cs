using Microsoft.VisualStudio.TestTools.UnitTesting;
using PropRiskManager.Risk;
using PropRiskManager.State;

namespace PropRiskManager.Tests;

[TestClass]
public sealed class PropFirmPreTradeGuardTests
{
    [TestMethod]
    public void BlocksWhenWorstCaseFallsBelowFloorPlusBuffer()
    {
        var settings = Settings();
        settings.SafetyBufferPercent = 0.5;
        var guardian = Snapshot(dailyFloor: 95_000, totalFloor: 90_000);

        var result = PropFirmPreTradeGuard.Evaluate(
            settings,
            guardian,
            currentEquity: 100_000,
            existingRiskToStop: 2_000,
            proposedRisk: 3_000,
            unprotectedExposureCount: 0);

        Assert.IsFalse(result.Allowed);
        Assert.AreEqual(95_500, result.BindingFloor + result.SafetyBufferAmount, 0.000001);
        Assert.AreEqual(95_000, result.WorstCaseEquity, 0.000001);
    }

    [TestMethod]
    public void AllowsWhenWorstCasePreservesProtectedRoom()
    {
        var settings = Settings();
        settings.SafetyBufferPercent = 0.5;
        var guardian = Snapshot(dailyFloor: 95_000, totalFloor: 90_000);

        var result = PropFirmPreTradeGuard.Evaluate(
            settings,
            guardian,
            currentEquity: 100_000,
            existingRiskToStop: 1_000,
            proposedRisk: 2_000,
            unprotectedExposureCount: 0);

        Assert.IsTrue(result.Allowed);
        Assert.AreEqual(97_000, result.WorstCaseEquity, 0.000001);
    }

    [TestMethod]
    public void BlocksUnprotectedExistingExposureWhenConfigured()
    {
        var settings = Settings();
        settings.BlockIfExposureHasNoStop = true;
        var guardian = Snapshot(dailyFloor: 95_000, totalFloor: 90_000);

        var result = PropFirmPreTradeGuard.Evaluate(
            settings,
            guardian,
            currentEquity: 100_000,
            existingRiskToStop: 0,
            proposedRisk: 100,
            unprotectedExposureCount: 1);

        Assert.IsFalse(result.Allowed);
        StringAssert.Contains(result.Reason, "no stop loss");
    }

    [TestMethod]
    public void DisabledGuardAllowsRegardlessOfRoom()
    {
        var settings = Settings();
        settings.PreTradeLossRoomGuardEnabled = false;
        var guardian = Snapshot(dailyFloor: 99_000, totalFloor: 99_000);

        var result = PropFirmPreTradeGuard.Evaluate(
            settings,
            guardian,
            currentEquity: 100_000,
            existingRiskToStop: 50_000,
            proposedRisk: 50_000,
            unprotectedExposureCount: 10);

        Assert.IsTrue(result.Allowed);
    }

    private static PropFirmSettings Settings() => new()
    {
        InitialBalance = 100_000,
        PreTradeLossRoomGuardEnabled = true,
        SafetyBufferPercent = 0,
        BlockIfExposureHasNoStop = true
    };

    private static PropGuardianSnapshot Snapshot(double dailyFloor, double totalFloor)
        => new(
            ShouldBlockTrading: false,
            HardDrawdownBreach: false,
            Status: "OK",
            BlockReason: string.Empty,
            DailyDrawdownPercent: 0,
            TotalDrawdownPercent: 0,
            DailyFloor: dailyFloor,
            TotalFloor: totalFloor,
            DayProfitLoss: 0,
            DailyProfitCapAmount: 0,
            ProfitTargetAmount: 0,
            ProfitTargetEquity: 0,
            ProfitTargetReached: false,
            DailyProfitCapReached: false);
}
