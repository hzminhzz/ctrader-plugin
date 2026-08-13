using System.Collections.Generic;
using PropRiskManager.Domain;

namespace PropRiskManager.State;

public sealed class PluginSettings
{
    public bool SafeDefaultsApplied { get; set; }
    public bool TrailingEnabled { get; set; }
    public TrailingMode TrailingMode { get; set; } = TrailingMode.Custom;
    public double TrailingPips { get; set; } = 15;
    public bool BreakEvenEnabled { get; set; }
    public double BreakEvenTriggerPips { get; set; } = 20;
    public double BreakEvenOffsetPips { get; set; } = 10;

    public ManagementScope ManagementScope { get; set; } = ManagementScope.CurrentSymbol;
    public double PartialClosePercent { get; set; } = 50;
    public double ManualBreakEvenOffsetPips { get; set; } = 10;

    public List<PartialExitLevelSettings> PartialTakeProfits { get; set; } = new();
    public List<PartialExitLevelSettings> PartialStopLosses { get; set; } = new();
    public PropFirmSettings PropFirm { get; set; } = new();

    public void EnsureDefaults(double currentBalance = 0)
    {
        EnsurePartialExitDefaults();

        if (!SafeDefaultsApplied)
        {
            foreach (var level in PartialTakeProfits)
                level.Enabled = false;
            SafeDefaultsApplied = true;
        }

        PropFirm ??= new PropFirmSettings();
        if (PropFirm.InitialBalance <= 0 && currentBalance > 0)
            PropFirm.InitialBalance = currentBalance;
    }

    public void EnsurePartialExitDefaults()
    {
        PartialTakeProfits ??= new List<PartialExitLevelSettings>();
        PartialStopLosses ??= new List<PartialExitLevelSettings>();

        if (PartialTakeProfits.Count > 5)
            PartialTakeProfits.RemoveRange(5, PartialTakeProfits.Count - 5);
        if (PartialStopLosses.Count > 5)
            PartialStopLosses.RemoveRange(5, PartialStopLosses.Count - 5);

        while (PartialTakeProfits.Count < 5)
        {
            var index = PartialTakeProfits.Count + 1;
            PartialTakeProfits.Add(new PartialExitLevelSettings
            {
                Enabled = false,
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

}
