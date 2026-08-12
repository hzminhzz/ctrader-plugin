using System;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using PropRiskManager.Domain;
using PropRiskManager.Execution;
using PropRiskManager.Protection;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private CheckBox _trailEnabled = null!;
    private ComboBox _trailMode = null!;
    private TextBox _trailPips = null!;
    private CheckBox _breakEvenEnabled = null!;
    private TextBox _breakEvenTrigger = null!;
    private TextBox _breakEvenOffset = null!;

    private ComboBox _managementScope = null!;
    private TextBlock _positionInfo = null!;
    private TextBlock _managementStatus = null!;
    private Button _closeAllButton = null!;
    private Button _closeProfitButton = null!;
    private Button _closeLossButton = null!;
    private Button _cancelAllButton = null!;
    private Button _cancelBuyButton = null!;
    private Button _cancelSellButton = null!;
    private TextBox _partialClosePercent = null!;
    private TextBox _manualBreakEvenOffset = null!;
    private DateTime _lastProtectionRun;

    private void BuildAdvancedProtectionPanel()
    {
        var block = Asp.SymbolTab.AddBlock("Advanced Protection");
        block.Height = 145;

        var root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8) };
        root.AddChild(new TextBlock
        {
            Text = "ADVANCED PROTECTION",
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 5)
        });

        var trailRow = new Grid(1, 4) { Margin = new Thickness(0, 2, 0, 2) };
        _trailEnabled = new CheckBox { Text = "Trail", IsChecked = false };
        _trailMode = new ComboBox { Height = 24 };
        _trailMode.AddItem("Custom");
        _trailMode.AddItem("Server");
        _trailMode.SelectedItem = "Custom";
        _trailPips = new TextBox { Text = "15", Height = 24 };
        trailRow.AddChild(_trailEnabled, 0, 0);
        trailRow.AddChild(_trailMode, 0, 1);
        trailRow.AddChild(new TextBlock { Text = "Pips", VerticalAlignment = VerticalAlignment.Center }, 0, 2);
        trailRow.AddChild(_trailPips, 0, 3);
        root.AddChild(trailRow);

        var beRow = new Grid(1, 4) { Margin = new Thickness(0, 2, 0, 2) };
        _breakEvenEnabled = new CheckBox { Text = "BE Trigger", IsChecked = false };
        _breakEvenTrigger = new TextBox { Text = "20", Height = 24 };
        beRow.AddChild(_breakEvenEnabled, 0, 0);
        beRow.AddChild(_breakEvenTrigger, 0, 1);
        beRow.AddChild(new TextBlock { Text = "Offset", VerticalAlignment = VerticalAlignment.Center }, 0, 2);
        _breakEvenOffset = new TextBox { Text = "10", Height = 24 };
        beRow.AddChild(_breakEvenOffset, 0, 3);
        root.AddChild(beRow);

        block.Child = root;
    }

    private void BuildPositionManagementPanel()
    {
        var block = Asp.SymbolTab.AddBlock("Position Management");
        block.Height = 300;

        var root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8) };
        root.AddChild(new TextBlock
        {
            Text = "POSITION MANAGEMENT",
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 5)
        });

        var scopeRow = new Grid(1, 2) { Margin = new Thickness(0, 2, 0, 4) };
        scopeRow.AddChild(new TextBlock { Text = "Symbol", VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        _managementScope = new ComboBox { Height = 24 };
        _managementScope.AddItem("Current");
        _managementScope.AddItem("All");
        _managementScope.SelectedItem = "Current";
        scopeRow.AddChild(_managementScope, 0, 1);
        root.AddChild(scopeRow);

        _positionInfo = new TextBlock { Margin = new Thickness(0, 2, 0, 5) };
        root.AddChild(_positionInfo);

        var closeRow = new Grid(1, 3);
        _closeAllButton = new Button { Height = 43, Margin = new Thickness(2) };
        _closeProfitButton = new Button { Height = 43, Margin = new Thickness(2) };
        _closeLossButton = new Button { Height = 43, Margin = new Thickness(2) };
        _closeAllButton.Click += _ => ClosePositions(null);
        _closeProfitButton.Click += _ => ClosePositions(true);
        _closeLossButton.Click += _ => ClosePositions(false);
        closeRow.AddChild(_closeAllButton, 0, 0);
        closeRow.AddChild(_closeProfitButton, 0, 1);
        closeRow.AddChild(_closeLossButton, 0, 2);
        root.AddChild(closeRow);

        var cancelRow = new Grid(1, 3);
        _cancelAllButton = new Button { Height = 43, Margin = new Thickness(2) };
        _cancelBuyButton = new Button { Height = 43, Margin = new Thickness(2) };
        _cancelSellButton = new Button { Height = 43, Margin = new Thickness(2) };
        _cancelAllButton.Click += _ => CancelOrders(null);
        _cancelBuyButton.Click += _ => CancelOrders(TradeType.Buy);
        _cancelSellButton.Click += _ => CancelOrders(TradeType.Sell);
        cancelRow.AddChild(_cancelAllButton, 0, 0);
        cancelRow.AddChild(_cancelBuyButton, 0, 1);
        cancelRow.AddChild(_cancelSellButton, 0, 2);
        root.AddChild(cancelRow);

        var partialRow = new Grid(1, 3) { Margin = new Thickness(0, 3, 0, 2) };
        partialRow.AddChild(new TextBlock { Text = "Close %", VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        _partialClosePercent = new TextBox { Text = "50", Height = 24 };
        partialRow.AddChild(_partialClosePercent, 0, 1);
        var partialButton = new Button { Text = "Partial Close", Height = 27 };
        partialButton.Click += _ => PartialClosePositions();
        partialRow.AddChild(partialButton, 0, 2);
        root.AddChild(partialRow);

        var beRow = new Grid(1, 3) { Margin = new Thickness(0, 2, 0, 2) };
        beRow.AddChild(new TextBlock { Text = "BE Offset", VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        _manualBreakEvenOffset = new TextBox { Text = "10", Height = 24 };
        beRow.AddChild(_manualBreakEvenOffset, 0, 1);
        var beButton = new Button { Text = "Move to BE", Height = 27 };
        beButton.Click += _ => MovePositionsToBreakEven();
        beRow.AddChild(beButton, 0, 2);
        root.AddChild(beRow);

        _managementStatus = new TextBlock { Margin = new Thickness(0, 4, 0, 0) };
        root.AddChild(_managementStatus);
        block.Child = root;
    }

    private void RunAdvancedProtection()
    {
        CaptureManagementSettingsFromUi();
        if (Server.Time < _lastProtectionRun.AddSeconds(1))
            return;

        _lastProtectionRun = Server.Time;
        ProtectionEngine.Apply(Positions, _settings, Symbols.GetSymbol);
    }

    private void RefreshPositionManagement()
    {
        var positions = ScopedPositions();
        var orders = ScopedOrders();
        var totalPnL = positions.Sum(p => p.NetProfit);
        var profit = positions.Where(p => p.NetProfit > 0).Sum(p => p.NetProfit);
        var loss = positions.Where(p => p.NetProfit < 0).Sum(p => p.NetProfit);
        var buyOrders = orders.Count(o => o.TradeType == TradeType.Buy);
        var sellOrders = orders.Count(o => o.TradeType == TradeType.Sell);

        _positionInfo.Text = $"Open positions: {positions.Length}   Pending orders: {orders.Length}\n" +
                             $"Equity: {Account.Equity:F2}   Balance: {Account.Balance:F2}   Free Margin: {Account.FreeMargin:F2}";
        _closeAllButton.Text = $"Close All\n{totalPnL:F2}";
        _closeProfitButton.Text = $"Close Profit\n{profit:F2}";
        _closeLossButton.Text = $"Close Loss\n{loss:F2}";
        _cancelAllButton.Text = $"Cancel Order\n({orders.Length})";
        _cancelBuyButton.Text = $"Cancel Buy\n({buyOrders})";
        _cancelSellButton.Text = $"Cancel Sell\n({sellOrders})";
    }

    private Position[] ScopedPositions()
    {
        var positions = Positions.AsEnumerable();
        if (GetManagementScope() == ManagementScope.CurrentSymbol)
        {
            if (_symbol == null)
                return Array.Empty<Position>();
            positions = positions.Where(p => p.SymbolName == _symbol.Name);
        }

        return positions.ToArray();
    }

    private PendingOrder[] ScopedOrders()
    {
        var orders = PendingOrders.AsEnumerable();
        if (GetManagementScope() == ManagementScope.CurrentSymbol)
        {
            if (_symbol == null)
                return Array.Empty<PendingOrder>();
            orders = orders.Where(o => o.SymbolName == _symbol.Name);
        }

        return orders.ToArray();
    }

    private void ClosePositions(bool? profitable)
    {
        var positions = ScopedPositions();
        if (profitable == true)
            positions = positions.Where(p => p.NetProfit > 0).ToArray();
        else if (profitable == false)
            positions = positions.Where(p => p.NetProfit < 0).ToArray();

        ShowManagementResult("Close", PositionManagementService.Close(positions));
        RefreshPositionManagement();
    }

    private void CancelOrders(TradeType? tradeType)
    {
        var orders = ScopedOrders();
        if (tradeType.HasValue)
            orders = orders.Where(o => o.TradeType == tradeType.Value).ToArray();

        ShowManagementResult("Cancel", PositionManagementService.Cancel(orders));
        RefreshPositionManagement();
    }

    private void PartialClosePositions()
    {
        var percent = ParseNonNegative(_partialClosePercent.Text, 50);
        if (percent <= 0 || percent > 100)
        {
            _managementStatus.Text = "Close % must be greater than 0 and at most 100.";
            return;
        }

        try
        {
            ShowManagementResult(
                "Partial close",
                PositionManagementService.PartialClose(ScopedPositions(), percent, Symbols.GetSymbol));
        }
        catch (Exception ex)
        {
            _managementStatus.Text = ex.Message;
        }

        RefreshPositionManagement();
    }

    private void MovePositionsToBreakEven()
    {
        var offset = ParseSigned(_manualBreakEvenOffset.Text, 10);
        ShowManagementResult(
            "Break-even",
            PositionManagementService.MoveToBreakEven(ScopedPositions(), offset, Symbols.GetSymbol));
        RefreshPositionManagement();
    }

    private void ShowManagementResult(string action, ManagementResult result)
    {
        _managementStatus.Text = result.Attempted == 0
            ? $"{action}: nothing selected."
            : result.Succeeded == result.Attempted
                ? $"{action}: {result.Succeeded}/{result.Attempted} succeeded."
                : $"{action}: {result.Succeeded}/{result.Attempted} succeeded. {result.LastError}";
    }

    private ManagementScope GetManagementScope()
        => _managementScope.SelectedItem == "All" ? ManagementScope.AllSymbols : ManagementScope.CurrentSymbol;

    private TrailingMode GetTrailingMode()
        => _trailMode.SelectedItem == "Server" ? TrailingMode.Server : TrailingMode.Custom;

    private void CaptureManagementSettingsFromUi()
    {
        _settings.TrailingEnabled = _trailEnabled.IsChecked == true;
        _settings.TrailingMode = GetTrailingMode();
        _settings.TrailingPips = ParsePositive(_trailPips.Text, _settings.TrailingPips);
        _settings.BreakEvenEnabled = _breakEvenEnabled.IsChecked == true;
        _settings.BreakEvenTriggerPips = ParseNonNegative(_breakEvenTrigger.Text, _settings.BreakEvenTriggerPips);
        _settings.BreakEvenOffsetPips = ParseSigned(_breakEvenOffset.Text, _settings.BreakEvenOffsetPips);
        _settings.ManagementScope = GetManagementScope();
        _settings.PartialClosePercent = ParsePositive(_partialClosePercent.Text, _settings.PartialClosePercent);
        _settings.ManualBreakEvenOffsetPips = ParseSigned(_manualBreakEvenOffset.Text, _settings.ManualBreakEvenOffsetPips);
    }

    private void ApplyManagementSettingsToUi()
    {
        _trailEnabled.IsChecked = _settings.TrailingEnabled;
        _trailMode.SelectedItem = _settings.TrailingMode == TrailingMode.Server ? "Server" : "Custom";
        _trailPips.Text = _settings.TrailingPips.ToString(CultureInfo.InvariantCulture);
        _breakEvenEnabled.IsChecked = _settings.BreakEvenEnabled;
        _breakEvenTrigger.Text = _settings.BreakEvenTriggerPips.ToString(CultureInfo.InvariantCulture);
        _breakEvenOffset.Text = _settings.BreakEvenOffsetPips.ToString(CultureInfo.InvariantCulture);
        _managementScope.SelectedItem = _settings.ManagementScope == ManagementScope.AllSymbols ? "All" : "Current";
        _partialClosePercent.Text = _settings.PartialClosePercent.ToString(CultureInfo.InvariantCulture);
        _manualBreakEvenOffset.Text = _settings.ManualBreakEvenOffsetPips.ToString(CultureInfo.InvariantCulture);
    }

    private static double ParseSigned(string? text, double fallback)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
