using cAlgo.API;
using cAlgo.API.Internals;
using PropRiskManager.Domain;
using PropRiskManager.Risk;

namespace PropRiskManager;

[Plugin(AccessRights = AccessRights.None)]
public sealed class PropRiskManagerPlugin : Plugin
{
    private const string EntryLineName = "PRM_ENTRY";
    private const string StopLineName = "PRM_STOP";
    private const string TargetLineName = "PRM_TARGET";

    private Chart? _chart;
    private Symbol? _symbol;
    private ChartHorizontalLine? _entryLine;
    private ChartHorizontalLine? _stopLine;
    private ChartHorizontalLine? _targetLine;

    private TextBlock _status = null!;
    private TextBlock _sizing = null!;
    private TextBox _riskPercent = null!;
    private TextBox _maxRiskPercent = null!;
    private TextBox _maxSpreadPips = null!;

    protected override void OnStart()
    {
        BuildTradeWatchPanel();
        ChartManager.ActiveFrameChanged += OnActiveFrameChanged;
        BindToActiveChart();
    }

    protected override void OnStop()
    {
        ChartManager.ActiveFrameChanged -= OnActiveFrameChanged;
        UnbindChart();
    }

    private void BuildTradeWatchPanel()
    {
        var tab = TradeWatch.AddTab("Prop Risk Manager");
        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(10),
            Width = 280
        };

        root.AddChild(new TextBlock
        {
            Text = "PROP RISK MANAGER",
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        _riskPercent = AddInput(root, "Risk %", "0.50");
        _maxRiskPercent = AddInput(root, "Max risk %", "1.00");
        _maxSpreadPips = AddInput(root, "Max spread (pips)", "3.0");

        var buy = new Button { Text = "BUY", Height = 30, Margin = new Thickness(0, 8, 0, 3) };
        buy.Click += _ => Execute(TradeType.Buy);
        root.AddChild(buy);

        var sell = new Button { Text = "SELL", Height = 30, Margin = new Thickness(0, 3, 0, 8) };
        sell.Click += _ => Execute(TradeType.Sell);
        root.AddChild(sell);

        var reset = new Button { Text = "Reset chart lines", Height = 26 };
        reset.Click += _ => ResetLines();
        root.AddChild(reset);

        _sizing = new TextBlock { Margin = new Thickness(0, 10, 0, 3) };
        _status = new TextBlock { Text = "Select an active chart." };
        root.AddChild(_sizing);
        root.AddChild(_status);

        tab.Child = root;
    }

    private static TextBox AddInput(StackPanel root, string label, string initialValue)
    {
        root.AddChild(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 2) });
        var box = new TextBox { Text = initialValue, Height = 24 };
        root.AddChild(box);
        return box;
    }

    private void OnActiveFrameChanged(ActiveFrameChangedEventArgs args) => BindToActiveChart();

    private void BindToActiveChart()
    {
        UnbindChart();

        if (ChartManager.ActiveFrame is not ChartFrame frame)
        {
            _status.Text = "Active frame is not a chart.";
            return;
        }

        _chart = frame.Chart;
        _symbol = frame.Symbol;
        _chart.ObjectsUpdated += OnChartObjectsUpdated;
        DrawInitialLines();
        RecalculatePreview();
    }

    private void UnbindChart()
    {
        if (_chart != null)
            _chart.ObjectsUpdated -= OnChartObjectsUpdated;

        _chart = null;
        _symbol = null;
        _entryLine = null;
        _stopLine = null;
        _targetLine = null;
    }

    private void DrawInitialLines()
    {
        if (_chart == null || _symbol == null)
            return;

        var entry = (_symbol.Bid + _symbol.Ask) / 2.0;
        var stop = entry - 20 * _symbol.PipSize;
        var target = entry + 40 * _symbol.PipSize;

        _entryLine = _chart.DrawHorizontalLine(EntryLineName, entry, Color.DodgerBlue, 1, LineStyle.DotsRare);
        _stopLine = _chart.DrawHorizontalLine(StopLineName, stop, Color.OrangeRed, 2, LineStyle.Solid);
        _targetLine = _chart.DrawHorizontalLine(TargetLineName, target, Color.SeaGreen, 2, LineStyle.Solid);

        _entryLine.IsInteractive = true;
        _stopLine.IsInteractive = true;
        _targetLine.IsInteractive = true;
    }

    private void ResetLines()
    {
        if (_symbol == null)
            return;

        DrawInitialLines();
        RecalculatePreview();
    }

    private void OnChartObjectsUpdated(ChartObjectsUpdatedEventArgs args) => RecalculatePreview();

    private void RecalculatePreview()
    {
        if (!TryBuildPlan(TradeType.Buy, out var plan, out var reason))
        {
            _sizing.Text = string.Empty;
            _status.Text = reason;
            return;
        }

        _sizing.Text = $"{_symbol!.Name} | {plan.QuantityLots:F2} lots | {plan.StopLossPips:F1} pip SL | R:R {plan.RewardRiskRatio:F2}";
        _status.Text = "Preview uses BUY geometry. SELL is validated again on click.";
    }

    private void Execute(TradeType tradeType)
    {
        if (_symbol == null)
            return;

        if (!TryBuildPlan(tradeType, out var plan, out var reason))
        {
            _status.Text = reason;
            return;
        }

        var maxRisk = ParsePositive(_maxRiskPercent.Text, 1.0);
        var maxSpread = ParsePositive(_maxSpreadPips.Text, 3.0);
        var gate = PreTradeRiskGate.Evaluate(_symbol, plan, maxSpread, maxRisk);
        if (!gate.Allowed)
        {
            _status.Text = "BLOCKED: " + gate.Reason;
            return;
        }

        var result = ExecuteMarketOrder(
            tradeType,
            _symbol.Name,
            plan.VolumeInUnits,
            "PropRiskManager",
            plan.StopLossPips,
            plan.TakeProfitPips);

        _status.Text = result.IsSuccessful
            ? $"Executed {tradeType} {plan.QuantityLots:F2} lots."
            : $"Execution error: {result.Error}";
    }

    private bool TryBuildPlan(TradeType tradeType, out TradePlan plan, out string reason)
    {
        plan = null!;
        reason = string.Empty;

        if (_symbol == null || _entryLine == null || _stopLine == null || _targetLine == null)
        {
            reason = "Chart lines are not ready.";
            return false;
        }

        try
        {
            var riskPercent = ParsePositive(_riskPercent.Text, 0.5);
            var entry = tradeType == TradeType.Buy ? _symbol.Ask : _symbol.Bid;
            var stop = _stopLine.Y;
            var target = _targetLine.Y;

            plan = PositionSizer.BuildPlan(
                _symbol,
                tradeType,
                entry,
                stop,
                target,
                Account.Equity,
                riskPercent);

            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    private static double ParsePositive(string? text, double fallback)
    {
        return double.TryParse(text, out var value) && value > 0 ? value : fallback;
    }
}
