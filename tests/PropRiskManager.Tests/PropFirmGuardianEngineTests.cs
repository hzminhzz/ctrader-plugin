using PropRiskManager.Risk;
using PropRiskManager.State;
using Xunit;

namespace PropRiskManager.Tests;

public sealed class PropFirmGuardianEngineTests
{
    [Fact]
    public void StaticTotalDrawdown_BlocksAtFloor()
    {
        var settings = BaseSettings();
        settings.DailyDrawdownEnabled = false;
        settings.TotalDrawdownPercent = 10;
        var runtime = Runtime(100_000, 100_000);

        var result = PropFirmGuardianEngine.Evaluate(settings, runtime, 90_000);

        Assert.True(result.ShouldBlockTrading);
        Assert.True(result.HardDrawdownBreach);
        Assert.Equal("BREACH", result.Status);
        Assert.Equal(90_000, result.TotalFloor, 6);
    }

    [Fact]
    public void StaticDailyDrawdown_UsesDayStartEquity()
    {
        var settings = BaseSettings();
        settings.TotalDrawdownEnabled = false;
        settings.DailyDrawdownPercent = 5;
        var runtime = Runtime(100_000, 100_000);
        runtime.DayStartEquity = 100_000;

        var result = PropFirmGuardianEngine.Evaluate(settings, runtime, 95_000);

        Assert.True(result.HardDrawdownBreach);
        Assert.Equal(95_000, result.DailyFloor, 6);
        Assert.Equal(5, result.DailyDrawdownPercent, 6);
    }

    [Fact]
    public void TrailingTotalDrawdown_UsesEquityHighWaterMark()
    {
        var settings = BaseSettings();
        settings.DailyDrawdownEnabled = false;
        settings.TotalDrawdownTrailing = true;
        settings.TotalDrawdownPercent = 10;
        var runtime = Runtime(100_000, 110_000);
        runtime.EquityPeak = 110_000;

        var result = PropFirmGuardianEngine.Evaluate(settings, runtime, 98_999);

        Assert.True(result.HardDrawdownBreach);
        Assert.Equal(99_000, result.TotalFloor, 6);
    }

    [Fact]
    public void ProfitTarget_IsSoftTradingLock_NotHardDrawdownBreach()
    {
        var settings = BaseSettings();
        settings.TotalDrawdownEnabled = false;
        settings.DailyDrawdownEnabled = false;
        settings.ProfitTargetEnabled = true;
        settings.ProfitTargetPercent = 10;
        var runtime = Runtime(100_000, 100_000);

        var result = PropFirmGuardianEngine.Evaluate(settings, runtime, 110_000);

        Assert.True(result.ShouldBlockTrading);
        Assert.False(result.HardDrawdownBreach);
        Assert.True(result.ProfitTargetReached);
        Assert.Equal("TARGET", result.Status);
    }

    [Fact]
    public void DailyProfitCap_IsPercentageOfProfitTargetAmount()
    {
        var settings = BaseSettings();
        settings.TotalDrawdownEnabled = false;
        settings.DailyDrawdownEnabled = false;
        settings.ProfitTargetEnabled = true;
        settings.ProfitTargetPercent = 10;
        settings.DailyProfitCapEnabled = true;
        settings.DailyProfitCapPercentOfTarget = 30;
        var runtime = Runtime(100_000, 100_000);
        runtime.DayStartEquity = 100_000;

        var result = PropFirmGuardianEngine.Evaluate(settings, runtime, 103_000);

        Assert.True(result.ShouldBlockTrading);
        Assert.False(result.HardDrawdownBreach);
        Assert.True(result.DailyProfitCapReached);
        Assert.Equal(3_000, result.DailyProfitCapAmount, 6);
    }

    [Fact]
    public void MissingInitialBalance_DoesNotBlock()
    {
        var settings = BaseSettings();
        settings.InitialBalance = 0;
        var runtime = Runtime(100_000, 100_000);

        var result = PropFirmGuardianEngine.Evaluate(settings, runtime, 50_000);

        Assert.False(result.ShouldBlockTrading);
        Assert.False(result.HardDrawdownBreach);
        Assert.Equal("NOT CONFIGURED", result.Status);
    }

    private static PropFirmSettings BaseSettings() => new()
    {
        InitialBalance = 100_000,
        ProfitTargetEnabled = false,
        DailyProfitCapEnabled = false,
        TotalDrawdownEnabled = true,
        TotalDrawdownPercent = 10,
        DailyDrawdownEnabled = true,
        DailyDrawdownPercent = 5
    };

    private static AccountRuntimeState Runtime(double dayStartEquity, double equityPeak) => new()
    {
        AccountNumber = 1,
        TradingDay = new DateTime(2026, 8, 12),
        DayStartBalance = dayStartEquity,
        DayStartEquity = dayStartEquity,
        DailyEquityPeak = dayStartEquity,
        BalancePeak = dayStartEquity,
        EquityPeak = equityPeak
    };
}
