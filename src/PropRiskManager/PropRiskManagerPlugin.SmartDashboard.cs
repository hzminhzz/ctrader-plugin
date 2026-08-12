using System;
using System.Linq;
using cAlgo.API;
using PropRiskManager.Domain;
using PropRiskManager.Execution;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private TextBlock _smartAccountDashboard = null!;
    private TextBlock _smartSymbolDashboard = null!;
    private TextBlock _smartPerformanceDashboard = null!;
    private Button _smartContextualActionButton = null!;
    private TextBlock _smartDashboardStatus = null!;
    private SmartPerformanceSeriesState _smartPerformanceSeries = new();

    private void BuildSmartDashboardPanel()
    {
        var block = Asp.SymbolTab.AddBlock("Smart Position Manager");
        ConfigureAspBlock(block, 205);
        var root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8) };
        root.AddChild(new TextBlock { Text = "SMART POSITION MANAGER", FontSize = 15, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 5) });
        _smartAccountDashboard = new TextBlock { Margin = new Thickness(0, 2, 0, 3) };
        _smartSymbolDashboard = new TextBlock { Margin = new Thickness(0, 2, 0, 3) };
        _smartPerformanceDashboard = new TextBlock { Margin = new Thickness(0, 2, 0, 5) };
        root.AddChild(_smartAccountDashboard);
        root.AddChild(_smartSymbolDashboard);
        root.AddChild(_smartPerformanceDashboard);
        _smartContextualActionButton = new Button { Height = 45, Margin = new Thickness(2) };
        _smartContextualActionButton.Click += _ => ExecuteContextualSmartClose();
        root.AddChild(_smartContextualActionButton);
        _smartDashboardStatus = new TextBlock { Margin = new Thickness(0, 4, 0, 0) };
        root.AddChild(_smartDashboardStatus);
        block.Child = root;
    }

    private void RefreshSmartDashboard()
    {
        var activeSymbol = _symbol?.Name ?? string.Empty;
        var positions = Positions.Select(p => new SmartDashboardPosition(p.Id, p.SymbolName, p.NetProfit)).ToArray();
        var snapshot = SmartDashboardEngine.Build(Account.Equity, Server.TimeInUtc, activeSymbol, positions, _runtimeState.SmartPositions.Values);
        _smartAccountDashboard.Text = $"ACCOUNT  Pos {snapshot.Account.OpenPositions} | Equity {snapshot.Account.Equity:F2} | Alerts {snapshot.Account.ArmedSmartAlerts}\nLast update {snapshot.Account.LastUpdateUtc:HH:mm:ss} UTC";
        _smartSymbolDashboard.Text = string.IsNullOrEmpty(activeSymbol) ? "ACTIVE SYMBOL  unavailable" : $"{activeSymbol}  Pos {snapshot.ActiveSymbol.OpenPositions} | Net P&L {snapshot.ActiveSymbol.NetProfit:F2}";
        UpdateSmartPerformanceSeries(activeSymbol);
        var card = SmartDashboardEngine.BuildContextualActionCard(activeSymbol, positions);
        var scopeText = card.Scope == SmartCloseScope.ActiveSymbol ? (string.IsNullOrEmpty(activeSymbol) ? "SYMBOL" : activeSymbol) : "ACCOUNT";
        _smartContextualActionButton.Text = $"{scopeText} P&L {card.NetProfit:F2}\n{card.ActionText}";
        _smartContextualActionButton.IsEnabled = card.CanClose;
    }

    private void UpdateSmartPerformanceSeries(string activeSymbol)
    {
        if (_symbol == null || string.IsNullOrEmpty(activeSymbol))
        {
            _smartPerformanceDashboard.Text = "PERF  unavailable";
            return;
        }
        var mid = (_symbol.Bid + _symbol.Ask) / 2.0;
        var update = SmartPerformanceSeriesEngine.AddSample(_smartPerformanceSeries, activeSymbol, mid, Server.TimeInUtc);
        _smartPerformanceSeries = update.State;
        if (_smartPerformanceSeries.Samples.Count == 0)
        {
            _smartPerformanceDashboard.Text = $"{activeSymbol} PERF  empty";
            return;
        }
        var latest = _smartPerformanceSeries.Samples[^1];
        var compact = string.Join("  ", _smartPerformanceSeries.Samples.Skip(Math.Max(0, _smartPerformanceSeries.Samples.Count - 8)).Select(sample => sample.BasisPoints.ToString("+0.0;-0.0;0.0")));
        _smartPerformanceDashboard.Text = $"PERF {latest.BasisPoints:+0.0;-0.0;0.0} bps  [{compact}]";
    }

    private void ExecuteContextualSmartClose()
    {
        var activeSymbol = _symbol?.Name ?? string.Empty;
        var positions = Positions.Select(p => new SmartDashboardPosition(p.Id, p.SymbolName, p.NetProfit)).ToArray();
        var card = SmartDashboardEngine.BuildContextualActionCard(activeSymbol, positions);
        ExecuteSmartClose(card.Scope);
    }

    private void ExecuteSmartClose(SmartCloseScope scope)
    {
        var activeSymbol = _symbol?.Name ?? string.Empty;
        var snapshots = Positions.Select(p => new SmartDashboardPosition(p.Id, p.SymbolName, p.NetProfit)).ToArray();
        var plan = SmartDashboardEngine.PlanClose(scope, activeSymbol, snapshots);
        if (plan.Count == 0)
        {
            _smartDashboardStatus.Text = scope == SmartCloseScope.Account ? "Close All: no open positions." : $"Close {activeSymbol}: no open positions.";
            RefreshSmartDashboard();
            return;
        }
        var selectedIds = plan.PositionIds.ToHashSet();
        var result = PositionManagementService.Close(Positions.Where(p => selectedIds.Contains(p.Id)).ToArray());
        var scopeText = scope == SmartCloseScope.Account ? "ACCOUNT" : activeSymbol;
        _smartDashboardStatus.Text = $"Close {scopeText}: {result.Succeeded}/{result.Attempted} succeeded." + (string.IsNullOrEmpty(result.LastError) ? string.Empty : $" {result.LastError}");
        RefreshSmartDashboard();
        RefreshPositionManagement();
    }
}
