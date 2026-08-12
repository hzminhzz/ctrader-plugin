using System;
using System.Collections.Generic;
using PropRiskManager.Domain;

namespace PropRiskManager.State;

public sealed class AccountRuntimeState
{
    public int AccountNumber { get; set; }
    public DateTime TradingDay { get; set; }
    public double DayStartBalance { get; set; }
    public double DayStartEquity { get; set; }
    public double DailyEquityPeak { get; set; }
    public double EquityPeak { get; set; }
    public double BalancePeak { get; set; }
    public bool TradingBlocked { get; set; }
    public string BlockReason { get; set; } = string.Empty;
    public Dictionary<int, PositionAutomationState> PositionAutomation { get; set; } = new();
    public Dictionary<int, SmartPositionState> SmartPositions { get; set; } = new();
}
