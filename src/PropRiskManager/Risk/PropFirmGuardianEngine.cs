using System;
using PropRiskManager.State;

namespace PropRiskManager.Risk;

public sealed record PropGuardianSnapshot(
    bool ShouldBlockTrading,
    bool HardDrawdownBreach,
    string Status,
    string BlockReason,
    double DailyDrawdownPercent,
    double TotalDrawdownPercent,
    double DailyFloor,
    double TotalFloor,
    double DayProfitLoss,
    double DailyProfitCapAmount,
    double ProfitTargetAmount,
    double ProfitTargetEquity,
    bool ProfitTargetReached,
    bool DailyProfitCapReached);

public static class PropFirmGuardianEngine
{
    public static PropGuardianSnapshot Evaluate(
        PropFirmSettings settings,
        AccountRuntimeState runtime,
        double equity)
    {
        var initial = settings.InitialBalance;
        if (initial <= 0)
        {
            return new PropGuardianSnapshot(
                false, false, "NOT CONFIGURED", string.Empty,
                0, 0, double.NegativeInfinity, double.NegativeInfinity,
                0, 0, 0, 0, false, false);
        }

        var totalReference = settings.TotalDrawdownTrailing
            ? Math.Max(initial, runtime.EquityPeak)
            : initial;
        var dailyReference = settings.DailyDrawdownTrailing
            ? Math.Max(runtime.DayStartEquity, runtime.DailyEquityPeak)
            : runtime.DayStartEquity;

        if (dailyReference <= 0)
            dailyReference = equity;

        var totalFloor = settings.TotalDrawdownEnabled
            ? totalReference * (1.0 - settings.TotalDrawdownPercent / 100.0)
            : double.NegativeInfinity;
        var dailyFloor = settings.DailyDrawdownEnabled
            ? dailyReference * (1.0 - settings.DailyDrawdownPercent / 100.0)
            : double.NegativeInfinity;

        var totalDdPct = totalReference > 0
            ? Math.Max(0, (totalReference - equity) / totalReference * 100.0)
            : 0;
        var dailyDdPct = dailyReference > 0
            ? Math.Max(0, (dailyReference - equity) / dailyReference * 100.0)
            : 0;

        var totalBreached = settings.TotalDrawdownEnabled && equity <= totalFloor;
        var dailyBreached = settings.DailyDrawdownEnabled && equity <= dailyFloor;
        var hardBreach = totalBreached || dailyBreached;

        var dayPnl = equity - runtime.DayStartEquity;
        var targetAmount = settings.ProfitTargetEnabled
            ? initial * settings.ProfitTargetPercent / 100.0
            : 0;
        var targetEquity = initial + targetAmount;
        var targetReached = settings.ProfitTargetEnabled && equity >= targetEquity;

        var dailyCap = settings.DailyProfitCapEnabled && settings.ProfitTargetEnabled
            ? targetAmount * settings.DailyProfitCapPercentOfTarget / 100.0
            : 0;
        var dailyCapReached = settings.DailyProfitCapEnabled && dailyCap > 0 && dayPnl >= dailyCap;

        string reason;
        string status;
        if (totalBreached)
        {
            status = "BREACH";
            reason = "Total drawdown limit breached.";
        }
        else if (dailyBreached)
        {
            status = "BREACH";
            reason = "Daily drawdown limit breached.";
        }
        else if (targetReached)
        {
            status = "TARGET";
            reason = "Profit target reached.";
        }
        else if (dailyCapReached)
        {
            status = "DAILY CAP";
            reason = "Daily profit cap reached.";
        }
        else
        {
            status = "OK";
            reason = string.Empty;
        }

        return new PropGuardianSnapshot(
            hardBreach || targetReached || dailyCapReached,
            hardBreach,
            status,
            reason,
            dailyDdPct,
            totalDdPct,
            dailyFloor,
            totalFloor,
            dayPnl,
            dailyCap,
            targetAmount,
            targetEquity,
            targetReached,
            dailyCapReached);
    }
}
