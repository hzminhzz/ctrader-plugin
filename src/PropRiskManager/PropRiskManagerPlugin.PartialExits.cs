using System;
using System.Globalization;
using cAlgo.API;
using PropRiskManager.Domain;
using PropRiskManager.Protection;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private readonly CheckBox[] _partialTpEnabled = new CheckBox[5];
    private readonly TextBox[] _partialTpTrigger = new TextBox[5];
    private readonly ComboBox[] _partialTpTriggerMode = new ComboBox[5];
    private readonly TextBox[] _partialTpClose = new TextBox[5];
    private readonly ComboBox[] _partialTpCloseMode = new ComboBox[5];

    private readonly CheckBox[] _partialSlEnabled = new CheckBox[5];
    private readonly TextBox[] _partialSlTrigger = new TextBox[5];
    private readonly ComboBox[] _partialSlTriggerMode = new ComboBox[5];
    private readonly TextBox[] _partialSlClose = new TextBox[5];
    private readonly ComboBox[] _partialSlCloseMode = new ComboBox[5];

    private DateTime _lastPartialExitRun;

    private void BuildPartialTakeProfitPanel()
    {
        BuildPartialExitPanel(
            "Partial Take Profit",
            "PARTIAL TAKE PROFIT",
            true,
            _partialTpEnabled,
            _partialTpTrigger,
            _partialTpTriggerMode,
            _partialTpClose,
            _partialTpCloseMode);
    }

    private void BuildPartialStopLossPanel()
    {
        BuildPartialExitPanel(
            "Partial Stop Loss",
            "PARTIAL STOP LOSS",
            false,
            _partialSlEnabled,
            _partialSlTrigger,
            _partialSlTriggerMode,
            _partialSlClose,
            _partialSlCloseMode);
    }

    private void BuildPartialExitPanel(
        string blockName,
        string title,
        bool takeProfit,
        CheckBox[] enabled,
        TextBox[] trigger,
        ComboBox[] triggerMode,
        TextBox[] close,
        ComboBox[] closeMode)
    {
        var block = Asp.SymbolTab.AddBlock(blockName);
        ConfigureAspBlock(block, 235);
        var root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8) };
        root.AddChild(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 5)
        });

        for (var i = 0; i < 5; i++)
        {
            var row = new Grid(1, 6) { Margin = new Thickness(0, 1, 0, 1) };
            enabled[i] = new CheckBox { Text = (takeProfit ? "TP" : "SL") + (i + 1), IsChecked = takeProfit };
            trigger[i] = new TextBox { Text = ((i + 1) * 10).ToString(CultureInfo.InvariantCulture), Height = 23 };
            triggerMode[i] = new ComboBox { Height = 23 };
            triggerMode[i].AddItem("Pips");
            triggerMode[i].AddItem(takeProfit ? "% TP" : "% SL");
            triggerMode[i].SelectedItem = "Pips";
            close[i] = new TextBox { Text = "25", Height = 23 };
            closeMode[i] = new ComboBox { Height = 23 };
            closeMode[i].AddItem("% Original");
            closeMode[i].AddItem("% Remaining");
            closeMode[i].AddItem("Fixed Lots");
            closeMode[i].SelectedItem = "% Original";

            row.AddChild(enabled[i], 0, 0);
            row.AddChild(trigger[i], 0, 1);
            row.AddChild(triggerMode[i], 0, 2);
            row.AddChild(new TextBlock { Text = "Close", VerticalAlignment = VerticalAlignment.Center }, 0, 3);
            row.AddChild(close[i], 0, 4);
            row.AddChild(closeMode[i], 0, 5);
            root.AddChild(row);
        }

        block.Child = root;
    }

    private void RunPartialExitAutomation()
    {
        if (Server.Time < _lastPartialExitRun.AddMilliseconds(500))
            return;

        _lastPartialExitRun = Server.Time;
        CapturePartialExitSettingsFromUi();
        PartialExitEngine.Apply(Positions, _settings, _runtimeState, Symbols.GetSymbol);
    }

    private void CapturePartialExitSettingsFromUi()
    {
        _settings.EnsurePartialExitDefaults();
        CaptureLevels(
            _settings.PartialTakeProfits,
            _partialTpEnabled,
            _partialTpTrigger,
            _partialTpTriggerMode,
            _partialTpClose,
            _partialTpCloseMode);
        CaptureLevels(
            _settings.PartialStopLosses,
            _partialSlEnabled,
            _partialSlTrigger,
            _partialSlTriggerMode,
            _partialSlClose,
            _partialSlCloseMode);
    }

    private static void CaptureLevels(
        System.Collections.Generic.IList<PartialExitLevelSettings> settings,
        CheckBox[] enabled,
        TextBox[] trigger,
        ComboBox[] triggerMode,
        TextBox[] close,
        ComboBox[] closeMode)
    {
        for (var i = 0; i < 5; i++)
        {
            var level = settings[i];
            level.Enabled = enabled[i].IsChecked == true;
            level.TriggerValue = ParseNonNegative(trigger[i].Text, level.TriggerValue);
            level.TriggerMode = triggerMode[i].SelectedItem == "Pips"
                ? PartialTriggerMode.Pips
                : PartialTriggerMode.PercentOfProtection;
            level.CloseValue = ParseNonNegative(close[i].Text, level.CloseValue);
            level.CloseMode = closeMode[i].SelectedItem switch
            {
                "% Remaining" => PartialCloseMode.PercentRemaining,
                "Fixed Lots" => PartialCloseMode.FixedLots,
                _ => PartialCloseMode.PercentOriginal
            };
        }
    }

    private void ApplyPartialExitSettingsToUi()
    {
        _settings.EnsurePartialExitDefaults();
        ApplyLevels(
            _settings.PartialTakeProfits,
            true,
            _partialTpEnabled,
            _partialTpTrigger,
            _partialTpTriggerMode,
            _partialTpClose,
            _partialTpCloseMode);
        ApplyLevels(
            _settings.PartialStopLosses,
            false,
            _partialSlEnabled,
            _partialSlTrigger,
            _partialSlTriggerMode,
            _partialSlClose,
            _partialSlCloseMode);
    }

    private static void ApplyLevels(
        System.Collections.Generic.IReadOnlyList<PartialExitLevelSettings> settings,
        bool takeProfit,
        CheckBox[] enabled,
        TextBox[] trigger,
        ComboBox[] triggerMode,
        TextBox[] close,
        ComboBox[] closeMode)
    {
        for (var i = 0; i < 5; i++)
        {
            var level = settings[i];
            enabled[i].IsChecked = level.Enabled;
            trigger[i].Text = level.TriggerValue.ToString(CultureInfo.InvariantCulture);
            triggerMode[i].SelectedItem = level.TriggerMode == PartialTriggerMode.Pips
                ? "Pips"
                : takeProfit ? "% TP" : "% SL";
            close[i].Text = level.CloseValue.ToString(CultureInfo.InvariantCulture);
            closeMode[i].SelectedItem = level.CloseMode switch
            {
                PartialCloseMode.PercentRemaining => "% Remaining",
                PartialCloseMode.FixedLots => "Fixed Lots",
                _ => "% Original"
            };
        }
    }
}
