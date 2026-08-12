namespace PropRiskManager.Domain;

public enum PartialTriggerMode
{
    Pips,
    PercentOfProtection
}

public enum PartialCloseMode
{
    PercentOriginal,
    PercentRemaining,
    FixedLots
}

public sealed class PartialExitLevelSettings
{
    public bool Enabled { get; set; }
    public double TriggerValue { get; set; }
    public PartialTriggerMode TriggerMode { get; set; } = PartialTriggerMode.Pips;
    public double CloseValue { get; set; } = 25;
    public PartialCloseMode CloseMode { get; set; } = PartialCloseMode.PercentOriginal;
}
