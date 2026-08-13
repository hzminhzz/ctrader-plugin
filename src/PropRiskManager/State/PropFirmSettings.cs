namespace PropRiskManager.State;

public sealed class PropFirmSettings
{
    public double InitialBalance { get; set; }

    public bool ProfitTargetEnabled { get; set; } = true;
    public double ProfitTargetPercent { get; set; } = 10;

    public bool DailyProfitCapEnabled { get; set; } = true;
    public double DailyProfitCapPercentOfTarget { get; set; } = 30;

    public bool TotalDrawdownEnabled { get; set; } = true;
    public double TotalDrawdownPercent { get; set; } = 10;
    public bool TotalDrawdownTrailing { get; set; }

    public bool DailyDrawdownEnabled { get; set; } = true;
    public double DailyDrawdownPercent { get; set; } = 5;
    public bool DailyDrawdownTrailing { get; set; }

    public int MinimumTradingDays { get; set; } = 5;

    public bool PreTradeLossRoomGuardEnabled { get; set; } = true;
    public double SafetyBufferPercent { get; set; } = 0.5;
    public bool BlockIfExposureHasNoStop { get; set; } = true;

    public bool AutoCloseOnDrawdownBreach { get; set; } = true;
    public string ResetTimeZoneId { get; set; } = string.Empty;
    public double ResetUtcOffsetHours { get; set; }
    public int ResetHour { get; set; }
}
