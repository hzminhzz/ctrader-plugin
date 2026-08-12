using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using cAlgo.API;
using PropRiskManager.Execution;
using PropRiskManager.Risk;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private TextBox _propInitialBalance = null!;
    private CheckBox _profitTargetEnabled = null!;
    private TextBox _profitTargetPercent = null!;
    private CheckBox _dailyProfitCapEnabled = null!;
    private TextBox _dailyProfitCapPercent = null!;
    private CheckBox _totalDdEnabled = null!;
    private TextBox _totalDdPercent = null!;
    private CheckBox _totalDdTrailing = null!;
    private CheckBox _dailyDdEnabled = null!;
    private TextBox _dailyDdPercent = null!;
    private CheckBox _dailyDdTrailing = null!;
    private TextBox _minimumDays = null!;
    private TextBox _maxLots = null!;
    private CheckBox _autoCloseDrawdown = null!;
    private TextBox _resetUtcOffset = null!;
    private TextBox _resetHour = null!;
    private TextBlock _propStatus = null!;
    private TextBlock _propDrawdowns = null!;
    private TextBlock _propDayPnl = null!;
    private TextBlock _propConsistency = null!;
    private DateTime _lastGuardianLiquidationAttempt;

    private void BuildPropFirmProtectionPanel()
    {
        var block = Asp.SymbolTab.AddBlock("Prop Firm Protection");
        block.Height = 335;
        var root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8) };

        root.AddChild(new TextBlock
        {
            Text = "PROP FIRM PROTECTION",
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 5)
        });

        _propInitialBalance = AddInput(root, "Initial Balance", "0");

        var targetRow = new Grid(1, 2);
        _profitTargetEnabled = new CheckBox { Text = "Profit Target (%)", IsChecked = true };
        _profitTargetPercent = new TextBox { Text = "10", Height = 24 };
        targetRow.AddChild(_profitTargetEnabled, 0, 0);
        targetRow.AddChild(_profitTargetPercent, 0, 1);
        root.AddChild(targetRow);

        var capRow = new Grid(1, 2);
        _dailyProfitCapEnabled = new CheckBox { Text = "Daily Profit Cap (%)", IsChecked = true };
        _dailyProfitCapPercent = new TextBox { Text = "30", Height = 24 };
        capRow.AddChild(_dailyProfitCapEnabled, 0, 0);
        capRow.AddChild(_dailyProfitCapPercent, 0, 1);
        root.AddChild(capRow);

        var totalDdRow = new Grid(1, 3);
        _totalDdEnabled = new CheckBox { Text = "Total DD (%)", IsChecked = true };
        _totalDdPercent = new TextBox { Text = "10", Height = 24 };
        _totalDdTrailing = new CheckBox { Text = "Trail?", IsChecked = false };
        totalDdRow.AddChild(_totalDdEnabled, 0, 0);
        totalDdRow.AddChild(_totalDdPercent, 0, 1);
        totalDdRow.AddChild(_totalDdTrailing, 0, 2);
        root.AddChild(totalDdRow);

        var dailyDdRow = new Grid(1, 3);
        _dailyDdEnabled = new CheckBox { Text = "Daily DD (%)", IsChecked = true };
        _dailyDdPercent = new TextBox { Text = "5", Height = 24 };
        _dailyDdTrailing = new CheckBox { Text = "Trail?", IsChecked = false };
        dailyDdRow.AddChild(_dailyDdEnabled, 0, 0);
        dailyDdRow.AddChild(_dailyDdPercent, 0, 1);
        dailyDdRow.AddChild(_dailyDdTrailing, 0, 2);
        root.AddChild(dailyDdRow);

        var ruleRow = new Grid(1, 4) { Margin = new Thickness(0, 2, 0, 2) };
        ruleRow.AddChild(new TextBlock { Text = "Min Days", VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        _minimumDays = new TextBox { Text = "5", Height = 24 };
        ruleRow.AddChild(_minimumDays, 0, 1);
        ruleRow.AddChild(new TextBlock { Text = "Max Lot", VerticalAlignment = VerticalAlignment.Center }, 0, 2);
        _maxLots = new TextBox { Text = "100", Height = 24 };
        ruleRow.AddChild(_maxLots, 0, 3);
        root.AddChild(ruleRow);

        var resetRow = new Grid(1, 4) { Margin = new Thickness(0, 2, 0, 2) };
        resetRow.AddChild(new TextBlock { Text = "Reset UTC offset", VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        _resetUtcOffset = new TextBox { Text = "0", Height = 24 };
        resetRow.AddChild(_resetUtcOffset, 0, 1);
        resetRow.AddChild(new TextBlock { Text = "Hour", VerticalAlignment = VerticalAlignment.Center }, 0, 2);
        _resetHour = new TextBox { Text = "0", Height = 24 };
        resetRow.AddChild(_resetHour, 0, 3);
        root.AddChild(resetRow);

        _autoCloseDrawdown = new CheckBox { Text = "Auto-close positions and cancel orders on DD breach", IsChecked = true };
        root.AddChild(_autoCloseDrawdown);

        _propStatus = new TextBlock { Margin = new Thickness(0, 5, 0, 1), FontWeight = FontWeight.Bold };
        _propDrawdowns = new TextBlock { Margin = new Thickness(0, 1, 0, 1) };
        _propDayPnl = new TextBlock { Margin = new Thickness(0, 1, 0, 1) };
        _propConsistency = new TextBlock { Margin = new Thickness(0, 1, 0, 0) };
        root.AddChild(_propStatus);
        root.AddChild(_propDrawdowns);
        root.AddChild(_propDayPnl);
        root.AddChild(_propConsistency);

        block.Child = root;
    }

    private void RunPropFirmGuardian()
    {
        CapturePropFirmSettingsFromUi();
        var snapshot = PropFirmGuardianEngine.Evaluate(_settings.PropFirm, _runtimeState, Account.Equity);

        _runtimeState.TradingBlocked = snapshot.ShouldBlockTrading;
        _runtimeState.BlockReason = snapshot.BlockReason;

        if (snapshot.HardDrawdownBreach &&
            _settings.PropFirm.AutoCloseOnDrawdownBreach &&
            Server.Time >= _lastGuardianLiquidationAttempt.AddSeconds(2) &&
            (Positions.Any() || PendingOrders.Any()))
        {
            _lastGuardianLiquidationAttempt = Server.Time;
            PositionManagementService.Close(Positions);
            PositionManagementService.Cancel(PendingOrders);
        }

        var days = GetTradingDaysCount();
        var consistency = GetConsistencyPercent(snapshot.DayProfitLoss);
        _propStatus.Text = "Status: " + snapshot.Status + (string.IsNullOrEmpty(snapshot.BlockReason) ? string.Empty : " - " + snapshot.BlockReason);
        _propStatus.ForegroundColor = snapshot.HardDrawdownBreach
            ? Color.Red
            : snapshot.ShouldBlockTrading ? Color.OrangeRed : Color.Green;
        _propDrawdowns.Text = $"Daily: {snapshot.DailyDrawdownPercent:F2}%   Total: {snapshot.TotalDrawdownPercent:F2}%";
        _propDayPnl.Text = $"Day P&L: {snapshot.DayProfitLoss:F2} / {snapshot.DailyProfitCapAmount:F2}   Target: {snapshot.ProfitTargetAmount:F2}";
        _propConsistency.Text = $"Consistency: {consistency:F1}%   Days: {days} / {_settings.PropFirm.MinimumTradingDays}";
    }

    private DateTime GetGuardianTradingDay(DateTime utcTime)
    {
        var settings = _settings.PropFirm;
        var shifted = utcTime.AddHours(settings.ResetUtcOffsetHours).AddHours(-settings.ResetHour);
        return shifted.Date;
    }

    private int GetTradingDaysCount()
    {
        var days = new HashSet<DateTime>();
        foreach (var trade in History)
            days.Add(GetGuardianTradingDay(trade.EntryTime));
        foreach (var position in Positions)
            days.Add(GetGuardianTradingDay(position.EntryTime));
        return days.Count;
    }

    private double GetConsistencyPercent(double currentDayPnl)
    {
        var initial = _settings.PropFirm.InitialBalance;
        var totalProfit = Account.Equity - initial;
        if (totalProfit <= 0)
            return 100;

        var currentDay = GetGuardianTradingDay(Server.TimeInUtc);
        var dailyProfits = History
            .GroupBy(t => GetGuardianTradingDay(t.ClosingTime))
            .ToDictionary(g => g.Key, g => g.Sum(t => t.NetProfit));
        dailyProfits[currentDay] = currentDayPnl;

        var bestDay = dailyProfits.Count == 0 ? 0 : dailyProfits.Values.Max();
        if (bestDay <= 0)
            return 0;

        return Math.Min(100, bestDay / totalProfit * 100.0);
    }

    private void CapturePropFirmSettingsFromUi()
    {
        var settings = _settings.PropFirm;
        settings.InitialBalance = ParsePositive(_propInitialBalance.Text, settings.InitialBalance > 0 ? settings.InitialBalance : Account.Balance);
        settings.ProfitTargetEnabled = _profitTargetEnabled.IsChecked == true;
        settings.ProfitTargetPercent = ParseNonNegative(_profitTargetPercent.Text, settings.ProfitTargetPercent);
        settings.DailyProfitCapEnabled = _dailyProfitCapEnabled.IsChecked == true;
        settings.DailyProfitCapPercentOfTarget = ParseNonNegative(_dailyProfitCapPercent.Text, settings.DailyProfitCapPercentOfTarget);
        settings.TotalDrawdownEnabled = _totalDdEnabled.IsChecked == true;
        settings.TotalDrawdownPercent = ParseNonNegative(_totalDdPercent.Text, settings.TotalDrawdownPercent);
        settings.TotalDrawdownTrailing = _totalDdTrailing.IsChecked == true;
        settings.DailyDrawdownEnabled = _dailyDdEnabled.IsChecked == true;
        settings.DailyDrawdownPercent = ParseNonNegative(_dailyDdPercent.Text, settings.DailyDrawdownPercent);
        settings.DailyDrawdownTrailing = _dailyDdTrailing.IsChecked == true;
        settings.MinimumTradingDays = Math.Max(0, (int)ParseNonNegative(_minimumDays.Text, settings.MinimumTradingDays));
        settings.MaxLotsPerTrade = ParseNonNegative(_maxLots.Text, settings.MaxLotsPerTrade);
        settings.AutoCloseOnDrawdownBreach = _autoCloseDrawdown.IsChecked == true;
        settings.ResetUtcOffsetHours = Math.Max(-14, Math.Min(14, ParseSigned(_resetUtcOffset.Text, settings.ResetUtcOffsetHours)));
        settings.ResetHour = Math.Max(0, Math.Min(23, (int)ParseNonNegative(_resetHour.Text, settings.ResetHour)));
    }

    private void ApplyPropFirmSettingsToUi()
    {
        var settings = _settings.PropFirm;
        _propInitialBalance.Text = settings.InitialBalance.ToString(CultureInfo.InvariantCulture);
        _profitTargetEnabled.IsChecked = settings.ProfitTargetEnabled;
        _profitTargetPercent.Text = settings.ProfitTargetPercent.ToString(CultureInfo.InvariantCulture);
        _dailyProfitCapEnabled.IsChecked = settings.DailyProfitCapEnabled;
        _dailyProfitCapPercent.Text = settings.DailyProfitCapPercentOfTarget.ToString(CultureInfo.InvariantCulture);
        _totalDdEnabled.IsChecked = settings.TotalDrawdownEnabled;
        _totalDdPercent.Text = settings.TotalDrawdownPercent.ToString(CultureInfo.InvariantCulture);
        _totalDdTrailing.IsChecked = settings.TotalDrawdownTrailing;
        _dailyDdEnabled.IsChecked = settings.DailyDrawdownEnabled;
        _dailyDdPercent.Text = settings.DailyDrawdownPercent.ToString(CultureInfo.InvariantCulture);
        _dailyDdTrailing.IsChecked = settings.DailyDrawdownTrailing;
        _minimumDays.Text = settings.MinimumTradingDays.ToString(CultureInfo.InvariantCulture);
        _maxLots.Text = settings.MaxLotsPerTrade.ToString(CultureInfo.InvariantCulture);
        _autoCloseDrawdown.IsChecked = settings.AutoCloseOnDrawdownBreach;
        _resetUtcOffset.Text = settings.ResetUtcOffsetHours.ToString(CultureInfo.InvariantCulture);
        _resetHour.Text = settings.ResetHour.ToString(CultureInfo.InvariantCulture);
    }
}
