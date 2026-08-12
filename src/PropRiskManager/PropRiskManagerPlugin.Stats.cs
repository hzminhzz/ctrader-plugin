using System;
using System.Linq;
using cAlgo.API;
using PropRiskManager.Stats;

namespace PropRiskManager;

public sealed partial class PropRiskManagerPlugin
{
    private ComboBox _statsPeriod = null!;
    private ComboBox _statsSymbol = null!;
    private TextBlock _statsSummary = null!;
    private TextBlock _statsRiskReward = null!;
    private TextBlock _statsStreaks = null!;
    private TextBlock _statsTime = null!;
    private DateTime _lastStatsRefresh;

    private void BuildTradingStatsPanel()
    {
        var block = Asp.SymbolTab.AddBlock("Trading Stats");
        block.Height = 390;
        var root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(8) };

        root.AddChild(new TextBlock
        {
            Text = "TRADING STATS",
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 5)
        });

        var filters = new Grid(1, 5) { Margin = new Thickness(0, 1, 0, 5) };
        filters.AddChild(new TextBlock { Text = "Period", VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        _statsPeriod = new ComboBox { Height = 24 };
        _statsPeriod.AddItem("Today");
        _statsPeriod.AddItem("7d");
        _statsPeriod.AddItem("30d");
        _statsPeriod.AddItem("90d");
        _statsPeriod.AddItem("All");
        _statsPeriod.SelectedItem = "All";
        filters.AddChild(_statsPeriod, 0, 1);
        filters.AddChild(new TextBlock { Text = "Symbol", VerticalAlignment = VerticalAlignment.Center }, 0, 2);
        _statsSymbol = new ComboBox { Height = 24 };
        _statsSymbol.AddItem("All");
        _statsSymbol.AddItem("Current");
        _statsSymbol.SelectedItem = "All";
        filters.AddChild(_statsSymbol, 0, 3);
        var refresh = new Button { Text = "Refresh", Height = 25 };
        refresh.Click += _ => RefreshTradingStats(true);
        filters.AddChild(refresh, 0, 4);
        root.AddChild(filters);

        _statsSummary = new TextBlock { Margin = new Thickness(0, 2, 0, 5) };
        _statsRiskReward = new TextBlock { Margin = new Thickness(0, 2, 0, 5) };
        _statsStreaks = new TextBlock { Margin = new Thickness(0, 2, 0, 5) };
        _statsTime = new TextBlock { Margin = new Thickness(0, 2, 0, 0) };
        root.AddChild(_statsSummary);
        root.AddChild(_statsRiskReward);
        root.AddChild(_statsStreaks);
        root.AddChild(_statsTime);
        block.Child = root;
    }

    private void RefreshTradingStats(bool force = false)
    {
        if (!force && Server.Time < _lastStatsRefresh.AddSeconds(2))
            return;
        _lastStatsRefresh = Server.Time;

        var query = History.AsEnumerable();
        var period = _statsPeriod.SelectedItem ?? "All";
        if (period == "Today")
        {
            var currentDay = GetGuardianTradingDay(Server.TimeInUtc);
            query = query.Where(t => GetGuardianTradingDay(t.ClosingTime) == currentDay);
        }
        else if (period == "7d")
            query = query.Where(t => t.ClosingTime >= Server.TimeInUtc.AddDays(-7));
        else if (period == "30d")
            query = query.Where(t => t.ClosingTime >= Server.TimeInUtc.AddDays(-30));
        else if (period == "90d")
            query = query.Where(t => t.ClosingTime >= Server.TimeInUtc.AddDays(-90));

        if (_statsSymbol.SelectedItem == "Current" && _symbol != null)
            query = query.Where(t => t.SymbolName == _symbol.Name);

        var initialBalance = _settings.PropFirm.InitialBalance > 0
            ? _settings.PropFirm.InitialBalance
            : Account.Balance;
        var stats = TradingStatsEngine.Calculate(query, initialBalance, GetGuardianTradingDay);

        _statsSummary.Text =
            $"Summary\n" +
            $"Total Trades: {stats.TotalTrades} ({stats.Wins}W / {stats.Losses}L / {stats.BreakEven}BE)\n" +
            $"Win Rate: {stats.WinRate:F1}%\n" +
            $"Net Profit: {stats.NetProfit:F2}\n" +
            $"Profit Factor: {FormatRatio(stats.ProfitFactor)}\n" +
            $"Expectancy: {stats.Expectancy:F2}/trade";
        _statsSummary.ForegroundColor = stats.NetProfit >= 0 ? Color.Green : Color.Red;

        _statsRiskReward.Text =
            $"Risk & Reward\n" +
            $"Avg Win: {stats.AverageWinPips:F1} pips / {stats.AverageWinAmount:F2}\n" +
            $"Avg Loss: {stats.AverageLossPips:F1} pips / {stats.AverageLossAmount:F2}\n" +
            $"Avg R:R: {stats.AverageRewardRisk:F2}\n" +
            $"Best / Worst: {stats.BestTrade:F2} / {stats.WorstTrade:F2}\n" +
            $"Max Drawdown: -{stats.MaxDrawdown:F2} ({stats.MaxDrawdownPercent:F1}%)\n" +
            $"Recovery Factor: {stats.RecoveryFactor:F2}";

        var total = Math.Max(1, stats.TotalTrades);
        _statsStreaks.Text =
            $"Streaks & Direction\n" +
            $"Max Consec: {stats.MaxConsecutiveWins}W / {stats.MaxConsecutiveLosses}L\n" +
            $"Current Streak: {stats.CurrentStreak}\n" +
            $"Long / Short: {stats.LongTrades * 100.0 / total:F0}% ({stats.LongTrades}) / {stats.ShortTrades * 100.0 / total:F0}% ({stats.ShortTrades})";
        _statsStreaks.ForegroundColor = stats.CurrentStreak.EndsWith("L") ? Color.Red : Color.Green;

        _statsTime.Text =
            $"Time\n" +
            $"Avg Duration: {FormatDuration(stats.AverageDuration)}\n" +
            $"Short / Long: {FormatDuration(stats.AverageShortDuration)} / {FormatDuration(stats.AverageLongDuration)}\n" +
            $"Best / Worst Day: {stats.BestDay:F2} / {stats.WorstDay:F2}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{duration.Seconds}s";
    }

    private static string FormatRatio(double value)
        => double.IsPositiveInfinity(value) ? "∞" : value.ToString("F2");
}
