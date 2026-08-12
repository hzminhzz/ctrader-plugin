using System.Linq;
using cAlgo.API;
using PropRiskManager.Domain;
using PropRiskManager.Execution;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private TextBlock _smartAccountDashboard = null!;
    private TextBlock _smartSymbolDashboard = null!;
    private Button _smartCloseSymbolButton = null!;
    private Button _smartCloseAllButton = null!;
    private TextBlock _smartDashboardStatus = null!;

    private void BuildSmartDashboardPanel()
    {
        var block = Asp.SymbolTab.AddBlock("Smart Position Manager");
        ConfigureAspBlock(block, 175);
        var root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8) };
        root.AddChild(new TextBlock { Text = "SMART POSITION MANAGER", FontSize = 15, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 5) });
        _smartAccountDashboard = new TextBlock { Margin = new Thickness(0, 2, 0, 3) };
        _smartSymbolDashboard = new TextBlock { Margin = new Thickness(0, 2, 0, 5) };
        root.AddChild(_smartAccountDashboard);
        root.AddChild(_smartSymbolDashboard);
        var row = new Grid(1, 2);
        _smartCloseSymbolButton = new Button { Height = 38, Margin = new Thickness(2) };
        _smartCloseAllButton = new Button { Height = 38, Margin = new Thickness(2) };
        _smartCloseSymbolButton.Click += _ => ExecuteSmartClose(SmartCloseScope.ActiveSymbol);
        _smartCloseAllButton.Click += _ => ExecuteSmartClose(SmartCloseScope.Account);
        row.AddChild(_smartCloseSymbolButton, 0, 0);
        row.AddChild(_smartCloseAllButton, 0, 1);
        root.AddChild(row);
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
        var symbolPlan = SmartDashboardEngine.PlanClose(SmartCloseScope.ActiveSymbol, activeSymbol, positions);
        var accountPlan = SmartDashboardEngine.PlanClose(SmartCloseScope.Account, activeSymbol, positions);
        _smartCloseSymbolButton.Text = $"CLOSE {activeSymbol}\n({symbolPlan.Count} pos)";
        _smartCloseAllButton.Text = $"CLOSE ALL\n({accountPlan.Count} pos)";
        _smartCloseSymbolButton.IsEnabled = symbolPlan.Count > 0;
        _smartCloseAllButton.IsEnabled = accountPlan.Count > 0;
    }

    private void ExecuteSmartClose(SmartCloseScope scope)
    {
        var activeSymbol = _symbol?.Name ?? string.Empty;
        var snapshots = Positions.Select(p => new SmartDashboardPosition(p.Id, p.SymbolName, p.NetProfit)).ToArray();
        var plan = SmartDashboardEngine.PlanClose(scope, activeSymbol, snapshots);
        if (plan.Count == 0)
        {
            _smartDashboardStatus.Text = scope == SmartCloseScope.Account ? "Close All: no open positions." : $"Close {activeSymbol}: no open positions.";
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
