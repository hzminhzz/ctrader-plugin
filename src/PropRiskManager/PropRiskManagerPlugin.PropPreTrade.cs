using System.Globalization;
using cAlgo.API;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private CheckBox _preTradeLossRoomGuard = null!;
    private TextBox _safetyBufferPercent = null!;
    private CheckBox _blockUnprotectedExposure = null!;
    private TextBlock _preTradeRoomStatus = null!;

    private void BuildPropPreTradePanel()
    {
        var block = Asp.SymbolTab.AddBlock("Prop Pre-Trade Guard");
        block.Height = 125;
        var root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8) };

        root.AddChild(new TextBlock
        {
            Text = "PROP PRE-TRADE GUARD",
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 5)
        });

        _preTradeLossRoomGuard = new CheckBox
        {
            Text = "Block trades that exceed remaining DD room",
            IsChecked = true
        };
        root.AddChild(_preTradeLossRoomGuard);

        var bufferRow = new Grid(1, 2) { Margin = new Thickness(0, 2, 0, 2) };
        bufferRow.AddChild(new TextBlock { Text = "Safety Buffer (% initial)", VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        _safetyBufferPercent = new TextBox { Text = "0.5", Height = 24 };
        bufferRow.AddChild(_safetyBufferPercent, 0, 1);
        root.AddChild(bufferRow);

        _blockUnprotectedExposure = new CheckBox
        {
            Text = "Block if existing position/order has no SL",
            IsChecked = true
        };
        root.AddChild(_blockUnprotectedExposure);

        _preTradeRoomStatus = new TextBlock { Margin = new Thickness(0, 3, 0, 0) };
        root.AddChild(_preTradeRoomStatus);
        block.Child = root;
    }

    private void CapturePropPreTradeSettingsFromUi()
    {
        _settings.PropFirm.PreTradeLossRoomGuardEnabled = _preTradeLossRoomGuard.IsChecked == true;
        _settings.PropFirm.SafetyBufferPercent = ParseNonNegative(
            _safetyBufferPercent.Text,
            _settings.PropFirm.SafetyBufferPercent);
        _settings.PropFirm.BlockIfExposureHasNoStop = _blockUnprotectedExposure.IsChecked == true;
    }

    private void ApplyPropPreTradeSettingsToUi()
    {
        _preTradeLossRoomGuard.IsChecked = _settings.PropFirm.PreTradeLossRoomGuardEnabled;
        _safetyBufferPercent.Text = _settings.PropFirm.SafetyBufferPercent.ToString(CultureInfo.InvariantCulture);
        _blockUnprotectedExposure.IsChecked = _settings.PropFirm.BlockIfExposureHasNoStop;
    }
}
