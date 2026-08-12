using System;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using PropRiskManager.Domain;
using PropRiskManager.Risk;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private TextBlock _marketInfo = null!;
    private TextBlock _orderTypeInfo = null!;
    private TextBlock _sizing = null!;
    private TextBlock _status = null!;
    private TextBox _entryPrice = null!;
    private TextBox _riskValue = null!;
    private TextBox _commissionPerLot = null!;
    private TextBox _slPips = null!;
    private TextBox _tpPips = null!;
    private TextBox _maxRiskPercent = null!;
    private TextBox _maxSpreadPips = null!;
    private ComboBox _riskMode = null!;
    private CheckBox _useEntryPrice = null!;
    private CheckBox _useStopLoss = null!;
    private CheckBox _useTakeProfit = null!;
    private CheckBox _drawLines = null!;
    private ChartText? _entryLabel;
    private ChartText? _stopLabel;
    private ChartText? _targetLabel;
    private bool _syncingTradeControls;
    private DateTime _lastLabelRefresh;

    private const string EntryLabelName = "PRM_ENTRY_LABEL";
    private const string StopLabelName = "PRM_STOP_LABEL";
    private const string TargetLabelName = "PRM_TARGET_LABEL";

    private void BuildTradeExecutionPanel()
    {
        var block = Asp.SymbolTab.AddBlock("Prop Risk Manager");
        ConfigureAspBlock(block, 460);

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(8)
        };

        root.AddChild(new TextBlock
        {
            Text = "TRADE EXECUTION",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });

        _marketInfo = new TextBlock { Margin = new Thickness(0, 0, 0, 6) };
        root.AddChild(_marketInfo);

        var tradeButtons = new Grid(1, 2);
        var buy = new Button
        {
            Text = "BUY",
            Height = 42,
            Margin = new Thickness(2),
            BackgroundColor = Color.SeaGreen,
            ForegroundColor = Color.White
        };
        var sell = new Button
        {
            Text = "SELL",
            Height = 42,
            Margin = new Thickness(2),
            BackgroundColor = Color.OrangeRed,
            ForegroundColor = Color.White
        };
        buy.Click += _ => Execute(TradeType.Buy);
        sell.Click += _ => Execute(TradeType.Sell);
        tradeButtons.AddChild(buy, 0, 0);
        tradeButtons.AddChild(sell, 0, 1);
        root.AddChild(tradeButtons);

        var priceRow = new Grid(1, 3) { Margin = new Thickness(0, 5, 0, 2) };
        _useEntryPrice = new CheckBox { Text = "Price", IsChecked = false };
        _entryPrice = new TextBox { Height = 24 };
        _entryPrice.TextChanged += _ => SyncLinesFromManualEntry();
        _orderTypeInfo = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        priceRow.AddChild(_useEntryPrice, 0, 0);
        priceRow.AddChild(_entryPrice, 0, 1);
        priceRow.AddChild(_orderTypeInfo, 0, 2);
        root.AddChild(priceRow);

        var riskRow = new Grid(1, 4) { Margin = new Thickness(0, 2, 0, 2) };
        _riskValue = new TextBox { Text = "1.0", Height = 24 };
        _riskMode = new ComboBox { Height = 24 };
        _riskMode.AddItem("% Equity");
        _riskMode.AddItem("% Balance");
        _riskMode.AddItem("% Free Margin");
        _riskMode.AddItem("Fixed $");
        _riskMode.AddItem("Fixed Lots");
        _riskMode.SelectedItem = "% Equity";
        _sizing = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        riskRow.AddChild(new TextBlock { Text = "Risk", VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        riskRow.AddChild(_riskValue, 0, 1);
        riskRow.AddChild(_riskMode, 0, 2);
        riskRow.AddChild(_sizing, 0, 3);
        root.AddChild(riskRow);

        _commissionPerLot = AddInput(root, "Commission / lot", "0");

        var slRow = new Grid(1, 2);
        _useStopLoss = new CheckBox { Text = "SL Pips", IsChecked = true };
        _slPips = new TextBox { Text = "20", Height = 24 };
        _slPips.TextChanged += _ => SyncLinesFromDistanceInputs();
        slRow.AddChild(_useStopLoss, 0, 0);
        slRow.AddChild(_slPips, 0, 1);
        root.AddChild(slRow);

        var tpRow = new Grid(1, 2);
        _useTakeProfit = new CheckBox { Text = "TP Pips", IsChecked = true };
        _tpPips = new TextBox { Text = "40", Height = 24 };
        _tpPips.TextChanged += _ => SyncLinesFromDistanceInputs();
        tpRow.AddChild(_useTakeProfit, 0, 0);
        tpRow.AddChild(_tpPips, 0, 1);
        root.AddChild(tpRow);

        _drawLines = new CheckBox { Text = "Draw and drag lines", IsChecked = true };
        _drawLines.Checked += _ => UpdateLineVisibility();
        _drawLines.Unchecked += _ => UpdateLineVisibility();
        root.AddChild(_drawLines);

        _maxRiskPercent = AddInput(root, "Max risk %", "5.0");
        _maxSpreadPips = AddInput(root, "Max spread pips", "0");

        var reset = new Button { Text = "Reset trade lines", Height = 26, Margin = new Thickness(0, 5, 0, 2) };
        reset.Click += _ => ResetLines();
        root.AddChild(reset);

        _status = new TextBlock
        {
            Text = "Select an active chart.",
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };
        root.AddChild(_status);

        block.Child = root;
    }

    private static TextBox AddInput(StackPanel root, string label, string initialValue)
    {
        var row = new Grid(1, 2) { Margin = new Thickness(0, 2, 0, 2) };
        row.AddChild(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        var box = new TextBox { Text = initialValue, Height = 24 };
        row.AddChild(box, 0, 1);
        root.AddChild(row);
        return box;
    }

    private void OnActiveFrameChanged(ActiveFrameChangedEventArgs args) => BindToActiveChart();

    private void BindToActiveChart()
    {
        UnbindChart();

        var frame = ChartManager.ActiveFrame as ChartFrame
            ?? ChartManager.OfType<ChartFrame>().FirstOrDefault(f => f.Chart.IsActive)
            ?? ChartManager.OfType<ChartFrame>().FirstOrDefault(f => f.Chart.IsVisible)
            ?? ChartManager.OfType<ChartFrame>().FirstOrDefault();

        if (frame == null)
        {
            _status.Text = "No chart frame is available.";
            return;
        }

        _chart = frame.Chart;
        _symbol = frame.Symbol;
        _chart.ObjectsUpdated += OnChartObjectsUpdated;
        _chart.ObjectsRemoved += OnChartObjectsRemoved;
        DrawInitialLines();
        RefreshMarketInfo();
        RecalculatePreview();
    }

    private void UnbindChart()
    {
        if (_chart != null)
        {
            _chart.ObjectsUpdated -= OnChartObjectsUpdated;
            _chart.ObjectsRemoved -= OnChartObjectsRemoved;
            RemoveTradeObjects(_chart);
        }

        _chart = null;
        _symbol = null;
        _entryLine = null;
        _stopLine = null;
        _targetLine = null;
        _entryLabel = null;
        _stopLabel = null;
        _targetLabel = null;
    }

    private void DrawInitialLines()
    {
        if (_chart == null || _symbol == null)
            return;

        var entry = (_symbol.Bid + _symbol.Ask) / 2.0;
        var sl = ParsePositive(_slPips.Text, 20);
        var tp = ParsePositive(_tpPips.Text, 40);

        _syncingTradeControls = true;
        _entryPrice.Text = entry.ToString("F" + _symbol.Digits, CultureInfo.InvariantCulture);
        _syncingTradeControls = false;

        _entryLine = _chart.DrawHorizontalLine(EntryLineName, entry, Color.DodgerBlue, 1, LineStyle.DotsRare);
        _stopLine = _chart.DrawHorizontalLine(StopLineName, entry - sl * _symbol.PipSize, Color.OrangeRed, 2, LineStyle.Solid);
        _targetLine = _chart.DrawHorizontalLine(TargetLineName, entry + tp * _symbol.PipSize, Color.SeaGreen, 2, LineStyle.Solid);

        _entryLine.IsInteractive = true;
        _stopLine.IsInteractive = true;
        _targetLine.IsInteractive = true;
        UpdateChartLineLabels();
        UpdateLineVisibility();
    }

    private void ResetLines()
    {
        if (_symbol == null)
            return;

        DrawInitialLines();
        RecalculatePreview();
    }

    private void UpdateLineVisibility()
    {
        var hidden = _drawLines.IsChecked != true;
        if (_entryLine != null)
            _entryLine.IsHidden = hidden;
        if (_stopLine != null)
            _stopLine.IsHidden = hidden;
        if (_targetLine != null)
            _targetLine.IsHidden = hidden;
        if (_entryLabel != null)
            _entryLabel.IsHidden = hidden;
        if (_stopLabel != null)
            _stopLabel.IsHidden = hidden;
        if (_targetLabel != null)
            _targetLabel.IsHidden = hidden;
    }

    private void OnChartObjectsUpdated(ChartObjectsUpdatedEventArgs args)
    {
        if (_symbol == null || _syncingTradeControls)
            return;

        _syncingTradeControls = true;
        try
        {
            if (_entryLine != null)
                _entryPrice.Text = _entryLine.Y.ToString("F" + _symbol.Digits, CultureInfo.InvariantCulture);

            if (_entryLine != null && _stopLine != null)
                _slPips.Text = (Math.Abs(_entryLine.Y - _stopLine.Y) / _symbol.PipSize).ToString("F1", CultureInfo.InvariantCulture);

            if (_entryLine != null && _targetLine != null)
                _tpPips.Text = (Math.Abs(_targetLine.Y - _entryLine.Y) / _symbol.PipSize).ToString("F1", CultureInfo.InvariantCulture);
        }
        finally
        {
            _syncingTradeControls = false;
        }

        UpdateChartLineLabels();
        RecalculatePreview();
    }

    private void OnChartObjectsRemoved(ChartObjectsRemovedEventArgs args)
    {
        if (_drawLines.IsChecked != true || _chart == null || _symbol == null)
            return;

        if (_entryLine?.IsAlive != true || _stopLine?.IsAlive != true || _targetLine?.IsAlive != true)
            DrawInitialLines();
    }

    private void SyncLinesFromDistanceInputs()
    {
        if (_syncingTradeControls || _symbol == null || _entryLine == null || _stopLine == null || _targetLine == null)
            return;

        var direction = InferDirectionFromStop();
        var entry = _entryLine.Y;
        if (_useEntryPrice.IsChecked == true && TryParsePositive(_entryPrice.Text, out var manualEntry))
            entry = manualEntry;

        _syncingTradeControls = true;
        try
        {
            _entryLine.Y = entry;
            ApplyProtectionDistances(direction, entry);
        }
        finally
        {
            _syncingTradeControls = false;
        }

        UpdateChartLineLabels();
        RecalculatePreview();
    }

    private void SyncLinesFromManualEntry()
    {
        if (_syncingTradeControls || _useEntryPrice.IsChecked != true || _symbol == null ||
            _entryLine == null || _stopLine == null || _targetLine == null ||
            !TryParsePositive(_entryPrice.Text, out var entry))
            return;

        var direction = InferDirectionFromStop();
        _syncingTradeControls = true;
        try
        {
            _entryLine.Y = entry;
            ApplyProtectionDistances(direction, entry);
        }
        finally
        {
            _syncingTradeControls = false;
        }

        UpdateChartLineLabels();
        RecalculatePreview();
    }

    private void ApplyProtectionDistances(TradeType direction, double entry)
    {
        if (_symbol == null || _stopLine == null || _targetLine == null)
            return;

        if (TryParsePositive(_slPips.Text, out var slPips))
        {
            _stopLine.Y = direction == TradeType.Buy
                ? entry - slPips * _symbol.PipSize
                : entry + slPips * _symbol.PipSize;
        }

        if (TryParsePositive(_tpPips.Text, out var tpPips))
        {
            _targetLine.Y = direction == TradeType.Buy
                ? entry + tpPips * _symbol.PipSize
                : entry - tpPips * _symbol.PipSize;
        }
    }

    private void EnsureLinesForDirection(TradeType tradeType)
    {
        if (_symbol == null || _entryLine == null || _stopLine == null || _targetLine == null)
            return;

        var marketEntry = tradeType == TradeType.Buy ? _symbol.Ask : _symbol.Bid;
        var entry = _useEntryPrice.IsChecked == true && TryParsePositive(_entryPrice.Text, out var manualEntry)
            ? manualEntry
            : marketEntry;

        var stopInvalid = tradeType == TradeType.Buy
            ? _stopLine.Y >= entry
            : _stopLine.Y <= entry;
        var targetInvalid = _useTakeProfit.IsChecked == true && (tradeType == TradeType.Buy
            ? _targetLine.Y <= entry
            : _targetLine.Y >= entry);

        if (!stopInvalid && !targetInvalid)
            return;

        _syncingTradeControls = true;
        try
        {
            if (_useEntryPrice.IsChecked == true)
                _entryLine.Y = entry;
            ApplyProtectionDistances(tradeType, entry);
        }
        finally
        {
            _syncingTradeControls = false;
        }

        UpdateChartLineLabels();
    }

    private void UpdateChartLineLabels()
    {
        if (_chart == null || _symbol == null || _entryLine == null || _stopLine == null || _targetLine == null)
            return;

        var barIndex = Math.Max(_chart.FirstVisibleBarIndex, _chart.LastVisibleBarIndex - 6);
        var entry = _entryLine.Y;
        var slPips = Math.Abs(entry - _stopLine.Y) / _symbol.PipSize;
        var tpPips = Math.Abs(_targetLine.Y - entry) / _symbol.PipSize;
        var rr = slPips > 0 ? tpPips / slPips : 0;
        var digits = _symbol.Digits;

        _entryLabel = _chart.DrawText(
            EntryLabelName,
            $"ENTRY  {entry.ToString("F" + digits, CultureInfo.InvariantCulture)}",
            barIndex,
            entry,
            Color.DodgerBlue);
        _stopLabel = _chart.DrawText(
            StopLabelName,
            $"SL  {_stopLine.Y.ToString("F" + digits, CultureInfo.InvariantCulture)}  |  {slPips:F1} pips",
            barIndex,
            _stopLine.Y,
            Color.OrangeRed);
        _targetLabel = _chart.DrawText(
            TargetLabelName,
            $"TP  {_targetLine.Y.ToString("F" + digits, CultureInfo.InvariantCulture)}  |  {tpPips:F1} pips  |  R:R {rr:F2}",
            barIndex,
            _targetLine.Y,
            Color.SeaGreen);

        ConfigureLineLabel(_entryLabel);
        ConfigureLineLabel(_stopLabel);
        ConfigureLineLabel(_targetLabel);
        UpdateLineVisibility();
    }

    private static void ConfigureLineLabel(ChartText label)
    {
        label.IsInteractive = false;
        label.IsBold = true;
        label.FontSize = 10;
        label.HorizontalAlignment = HorizontalAlignment.Left;
        label.VerticalAlignment = VerticalAlignment.Center;
    }

    private static void RemoveTradeObjects(Chart chart)
    {
        chart.RemoveObject(EntryLineName);
        chart.RemoveObject(StopLineName);
        chart.RemoveObject(TargetLineName);
        chart.RemoveObject(EntryLabelName);
        chart.RemoveObject(StopLabelName);
        chart.RemoveObject(TargetLabelName);
    }

    private void RefreshMarketInfo()
    {
        if (_symbol == null)
            return;

        var spreadPips = _symbol.Spread / _symbol.PipSize;
        var commission = ParseNonNegative(_commissionPerLot.Text, 0);
        _marketInfo.Text = $"{_symbol.Name}   Spread: {spreadPips:F1} pips   Commission: {commission:F2}/lot   Pip: {_symbol.PipValue:G6}";

        if (Server.Time >= _lastLabelRefresh.AddSeconds(1))
        {
            UpdateChartLineLabels();
            _lastLabelRefresh = Server.Time;
        }
    }

    private void RecalculatePreview()
    {
        if (_symbol == null)
            return;

        var previewType = InferDirectionFromStop();
        if (!TryBuildPlan(previewType, out var plan, out var reason))
        {
            _sizing.Text = "Volume: --";
            _status.Text = reason;
            return;
        }

        _sizing.Text = $"Volume: {plan.QuantityLots:F2} lots";
        _orderTypeInfo.Text = plan.OrderKind == OrderKind.Market ? "Market Execution" : plan.OrderKind.ToString();
        _status.Text = $"Risk {plan.RiskAmount:F2} | SL {plan.StopLossPips:F1} pips | R:R {plan.RewardRiskRatio:F2}";
    }

    private TradeType InferDirectionFromStop()
    {
        if (_symbol == null || _stopLine == null)
            return TradeType.Buy;

        var reference = _useEntryPrice.IsChecked == true && TryParsePositive(_entryPrice.Text, out var manualEntry)
            ? manualEntry
            : (_symbol.Bid + _symbol.Ask) / 2.0;

        return _stopLine.Y < reference ? TradeType.Buy : TradeType.Sell;
    }

    private void Execute(TradeType tradeType)
    {
        if (_symbol == null)
            return;

        EnsureLinesForDirection(tradeType);

        if (!TryBuildPlan(tradeType, out var plan, out var reason))
        {
            _status.Text = reason;
            return;
        }

        var maxRisk = ParseNonNegative(_maxRiskPercent.Text, 5.0);
        var maxSpread = ParseNonNegative(_maxSpreadPips.Text, 0);
        var gate = PreTradeRiskGate.Evaluate(
            _symbol,
            plan,
            maxSpread,
            maxRisk,
            _settings.PropFirm.MaxLotsPerTrade);
        if (!gate.Allowed)
        {
            _status.Text = "BLOCKED: " + gate.Reason;
            return;
        }

        if (!AllowByPropLossRoom(plan))
        {
            _status.Text = _preTradeRoomStatus.Text;
            return;
        }

        TradeResult result;
        switch (plan.OrderKind)
        {
            case OrderKind.Market:
                result = ExecuteMarketOrder(tradeType, _symbol.Name, plan.VolumeInUnits, Label, plan.StopLossPips, plan.TakeProfitPips);
                break;
            case OrderKind.Limit:
                result = PlaceLimitOrder(
                    tradeType,
                    _symbol.Name,
                    plan.VolumeInUnits,
                    plan.EntryPrice,
                    Label,
                    plan.StopLossPips,
                    plan.TakeProfitPips,
                    ProtectionType.Relative,
                    null);
                break;
            case OrderKind.Stop:
                result = PlaceStopOrder(
                    tradeType,
                    _symbol.Name,
                    plan.VolumeInUnits,
                    plan.EntryPrice,
                    Label,
                    plan.StopLossPips,
                    plan.TakeProfitPips,
                    ProtectionType.Relative,
                    null);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        _status.Text = result.IsSuccessful
            ? $"{plan.OrderKind} {tradeType}: {plan.QuantityLots:F2} lots submitted."
            : $"Execution error: {result.Error}";
    }

    private bool TryBuildPlan(TradeType tradeType, out TradePlan plan, out string reason)
    {
        plan = null!;
        reason = string.Empty;

        if (_symbol == null || _stopLine == null || _targetLine == null)
        {
            reason = "Chart lines are not ready.";
            return false;
        }

        if (_useStopLoss.IsChecked != true)
        {
            reason = "Risk sizing requires an enabled stop loss.";
            return false;
        }

        try
        {
            var riskMode = GetRiskMode();
            var riskInput = ParsePositive(_riskValue.Text, riskMode == RiskMode.FixedLots ? 0.01 : 1.0);
            var useManualEntry = _useEntryPrice.IsChecked == true;
            var marketPrice = tradeType == TradeType.Buy ? _symbol.Ask : _symbol.Bid;
            var entry = useManualEntry ? ParsePositive(_entryPrice.Text, marketPrice) : marketPrice;
            var orderKind = DetectOrderKind(tradeType, useManualEntry, entry);
            var stop = _stopLine.Y;
            var target = _useTakeProfit.IsChecked == true ? _targetLine.Y : (double?)null;
            var commission = ParseNonNegative(_commissionPerLot.Text, 0);

            plan = PositionSizer.BuildPlan(
                _symbol,
                tradeType,
                orderKind,
                riskMode,
                riskInput,
                entry,
                stop,
                target,
                Account.Equity,
                Account.Balance,
                Account.FreeMargin,
                commission);

            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private OrderKind DetectOrderKind(TradeType tradeType, bool useManualEntry, double entry)
    {
        if (_symbol == null || !useManualEntry)
            return OrderKind.Market;

        var market = tradeType == TradeType.Buy ? _symbol.Ask : _symbol.Bid;
        if (Math.Abs(entry - market) < _symbol.TickSize)
            return OrderKind.Market;

        if (tradeType == TradeType.Buy)
            return entry < _symbol.Ask ? OrderKind.Limit : OrderKind.Stop;

        return entry > _symbol.Bid ? OrderKind.Limit : OrderKind.Stop;
    }

    private RiskMode GetRiskMode()
    {
        return _riskMode.SelectedItem switch
        {
            "% Balance" => RiskMode.PercentBalance,
            "% Free Margin" => RiskMode.PercentFreeMargin,
            "Fixed $" => RiskMode.FixedAmount,
            "Fixed Lots" => RiskMode.FixedLots,
            _ => RiskMode.PercentEquity
        };
    }

    private void CaptureTradeSettingsFromUi()
    {
        _settings.RiskMode = GetRiskMode();
        _settings.RiskValue = ParsePositive(_riskValue.Text, _settings.RiskValue);
        _settings.CommissionPerLotRoundTrip = ParseNonNegative(_commissionPerLot.Text, _settings.CommissionPerLotRoundTrip);
        _settings.UseEntryPrice = _useEntryPrice.IsChecked == true;
        _settings.UseStopLoss = _useStopLoss.IsChecked == true;
        _settings.UseTakeProfit = _useTakeProfit.IsChecked == true;
        _settings.DrawLines = _drawLines.IsChecked == true;
        _settings.StopLossPips = ParsePositive(_slPips.Text, _settings.StopLossPips);
        _settings.TakeProfitPips = ParsePositive(_tpPips.Text, _settings.TakeProfitPips);
        _settings.MaxRiskPercent = ParseNonNegative(_maxRiskPercent.Text, _settings.MaxRiskPercent);
        _settings.MaxSpreadPips = ParseNonNegative(_maxSpreadPips.Text, _settings.MaxSpreadPips);
    }

    private void ApplyTradeSettingsToUi()
    {
        _riskMode.SelectedItem = RiskModeLabel(_settings.RiskMode);
        _riskValue.Text = _settings.RiskValue.ToString(CultureInfo.InvariantCulture);
        _commissionPerLot.Text = _settings.CommissionPerLotRoundTrip.ToString(CultureInfo.InvariantCulture);
        _useEntryPrice.IsChecked = _settings.UseEntryPrice;
        _useStopLoss.IsChecked = _settings.UseStopLoss;
        _useTakeProfit.IsChecked = _settings.UseTakeProfit;
        _drawLines.IsChecked = _settings.DrawLines;
        _slPips.Text = _settings.StopLossPips.ToString(CultureInfo.InvariantCulture);
        _tpPips.Text = _settings.TakeProfitPips.ToString(CultureInfo.InvariantCulture);
        _maxRiskPercent.Text = _settings.MaxRiskPercent.ToString(CultureInfo.InvariantCulture);
        _maxSpreadPips.Text = _settings.MaxSpreadPips.ToString(CultureInfo.InvariantCulture);
    }

    private static string RiskModeLabel(RiskMode mode)
    {
        return mode switch
        {
            RiskMode.PercentBalance => "% Balance",
            RiskMode.PercentFreeMargin => "% Free Margin",
            RiskMode.FixedAmount => "Fixed $",
            RiskMode.FixedLots => "Fixed Lots",
            _ => "% Equity"
        };
    }

    private static double ParsePositive(string? text, double fallback)
        => TryParsePositive(text, out var value) ? value : fallback;

    private static bool TryParsePositive(string? text, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0;

    private static double ParseNonNegative(string? text, double fallback)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : fallback;
}
