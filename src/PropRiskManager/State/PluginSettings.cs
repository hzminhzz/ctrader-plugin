using System.Collections.Generic;
using PropRiskManager.Domain;

namespace PropRiskManager.State;

public sealed class PluginSettings
{
    public RiskMode RiskMode { get; set; } = RiskMode.PercentEquity;
    public double RiskValue { get; set; } = 1.0;
    public double CommissionPerLotRoundTrip { get; set; }
    public bool UseEntryPrice { get; set; }
    public bool UseStopLoss { get; set; } = true;
    public bool UseTakeProfit { get; set; } = true;
    public bool DrawLines { get; set; } = true;
    public double StopLossPips { get; set; } = 20;
    public double TakeProfitPips { get; set; } = 40;
    public double MaxRiskPercent { get; set; } = 5.0;
    public double MaxSpreadPips { get; set; }

    public bool TrailingEnabled { get; set; }
    public TrailingMode TrailingMode { get; set; } = TrailingMode.Custom;
    public double TrailingPips { get; set; } = 15;
    public bool BreakEvenEnabled { get; set; }
    public double BreakEvenTriggerPips { get; set; } = 20;
    public double BreakEvenOffsetPips { get; set; } = 10;

    public ManagementScope ManagementScope { get; set; } = ManagementScope.CurrentSymbol;
    public double PartialClosePercent { get; set; } = 50;
    public double ManualBreakEvenOffsetPips { get; set; } = 10;

    public List<PartialExitLevelSettings> PartialTakeProfits { get; set; } = CreateDefaultTakeProfits();
    public List<PartialExitLevelSettings> PartialStopLosses { get; set; } = CreateDefaultStopLosses();

    public void EnsurePartialExitDefaults()
    {
        PartialTakeProfits ??= new List<PartialExitLevelSettings>();
        PartialStopLosses ??= new List<PartialExitLevelSettings>();

        while (PartialTakeProfits.Count < 5)
        {
            var index = PartialTakeProfits.Count + 1;
            PartialTakeProfits.Add(new PartialExitLevelSettings
            {
                Enabled = true,
                TriggerValue = index * 10,
                CloseValue = 25
            });
        }

        while (PartialStopLosses.Count < 5)
        {
            var index = PartialStopLosses.Count + 1;
            PartialStopLosses.Add(new PartialExitLevelSettings
            {
                Enabled = false,
                TriggerValue = index * 10,
                CloseValue = 25
            });
        }
    }

    private static List<PartialExitLevelSettings> CreateDefaultTakeProfits()
    {
        var levels = new List<PartialExitLevelSettings>();
        for (var i = 1; i <= 5; i++)
        {
            levels.Add(new PartialExitLevelSettings
            {
                Enabled = true,
                TriggerValue = i * 10,
                CloseValue = 25
            });
        }
        return levels;
    }

    private static List<PartialExitLevelSettings> CreateDefaultStopLosses()
    {
        var levels = new List<PartialExitLevelSettings>();
        for (var i = 1; i <= 5; i++)
        {
            levels.Add(new PartialExitLevelSettings
            {
                Enabled = false,
                TriggerValue = i * 10,
                CloseValue = 25
            });
        }
        return levels;
    }
}
