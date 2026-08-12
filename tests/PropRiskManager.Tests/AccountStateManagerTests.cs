using Microsoft.VisualStudio.TestTools.UnitTesting;
using PropRiskManager.State;

namespace PropRiskManager.Tests;

[TestClass]
public sealed class AccountStateManagerTests
{
    [TestMethod]
    public void Update_NewTradingDay_ResetsDayReferencesButKeepsAccountPeaks()
    {
        var state = AccountStateManager.Create(1, new DateTime(2026, 8, 11), 100_000, 100_000);
        AccountStateManager.Update(state, new DateTime(2026, 8, 11), 106_000, 108_000);

        AccountStateManager.Update(state, new DateTime(2026, 8, 12), 104_000, 103_000);

        Assert.AreEqual(new DateTime(2026, 8, 12), state.TradingDay);
        Assert.AreEqual(104_000, state.DayStartBalance, 0.000001);
        Assert.AreEqual(103_000, state.DayStartEquity, 0.000001);
        Assert.AreEqual(103_000, state.DailyEquityPeak, 0.000001);
        Assert.AreEqual(106_000, state.BalancePeak, 0.000001);
        Assert.AreEqual(108_000, state.EquityPeak, 0.000001);
    }

    [TestMethod]
    public void Update_SameDay_AdvancesDailyAndAccountHighWaterMarks()
    {
        var state = AccountStateManager.Create(1, new DateTime(2026, 8, 12), 100_000, 100_000);

        AccountStateManager.Update(state, new DateTime(2026, 8, 12), 102_000, 105_000);
        AccountStateManager.Update(state, new DateTime(2026, 8, 12), 101_000, 103_000);

        Assert.AreEqual(105_000, state.DailyEquityPeak, 0.000001);
        Assert.AreEqual(102_000, state.BalancePeak, 0.000001);
        Assert.AreEqual(105_000, state.EquityPeak, 0.000001);
        Assert.AreEqual(100_000, state.DayStartEquity, 0.000001);
    }
}
