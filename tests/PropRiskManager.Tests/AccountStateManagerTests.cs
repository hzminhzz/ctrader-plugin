using PropRiskManager.State;
using Xunit;

namespace PropRiskManager.Tests;

public sealed class AccountStateManagerTests
{
    [Fact]
    public void Update_NewTradingDay_ResetsDayReferencesButKeepsAccountPeaks()
    {
        var state = AccountStateManager.Create(1, new DateTime(2026, 8, 11), 100_000, 100_000);
        AccountStateManager.Update(state, new DateTime(2026, 8, 11), 106_000, 108_000);

        AccountStateManager.Update(state, new DateTime(2026, 8, 12), 104_000, 103_000);

        Assert.Equal(new DateTime(2026, 8, 12), state.TradingDay);
        Assert.Equal(104_000, state.DayStartBalance, 6);
        Assert.Equal(103_000, state.DayStartEquity, 6);
        Assert.Equal(103_000, state.DailyEquityPeak, 6);
        Assert.Equal(106_000, state.BalancePeak, 6);
        Assert.Equal(108_000, state.EquityPeak, 6);
    }

    [Fact]
    public void Update_SameDay_AdvancesDailyAndAccountHighWaterMarks()
    {
        var state = AccountStateManager.Create(1, new DateTime(2026, 8, 12), 100_000, 100_000);

        AccountStateManager.Update(state, new DateTime(2026, 8, 12), 102_000, 105_000);
        AccountStateManager.Update(state, new DateTime(2026, 8, 12), 101_000, 103_000);

        Assert.Equal(105_000, state.DailyEquityPeak, 6);
        Assert.Equal(102_000, state.BalancePeak, 6);
        Assert.Equal(105_000, state.EquityPeak, 6);
        Assert.Equal(100_000, state.DayStartEquity, 6);
    }
}
