using System;

namespace PropRiskManager.State;

public static class AccountStateManager
{
    public static AccountRuntimeState Create(int accountNumber, DateTime tradingDay, double balance, double equity)
    {
        return new AccountRuntimeState
        {
            AccountNumber = accountNumber,
            TradingDay = tradingDay.Date,
            DayStartBalance = balance,
            DayStartEquity = equity,
            DailyEquityPeak = equity,
            BalancePeak = balance,
            EquityPeak = equity
        };
    }

    public static void Update(AccountRuntimeState state, DateTime tradingDay, double balance, double equity)
    {
        var day = tradingDay.Date;
        if (state.TradingDay != day)
        {
            state.TradingDay = day;
            state.DayStartBalance = balance;
            state.DayStartEquity = equity;
            state.DailyEquityPeak = equity;
        }

        state.DailyEquityPeak = Math.Max(state.DailyEquityPeak, equity);
        state.BalancePeak = Math.Max(state.BalancePeak, balance);
        state.EquityPeak = Math.Max(state.EquityPeak, equity);
    }
}
