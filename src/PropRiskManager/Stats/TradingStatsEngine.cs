using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;

namespace PropRiskManager.Stats;

public sealed record TradingStatsSnapshot(
    int TotalTrades,
    int Wins,
    int Losses,
    int BreakEven,
    double WinRate,
    double NetProfit,
    double ProfitFactor,
    double Expectancy,
    double AverageWinPips,
    double AverageWinAmount,
    double AverageLossPips,
    double AverageLossAmount,
    double AverageRewardRisk,
    double BestTrade,
    double WorstTrade,
    double MaxDrawdown,
    double MaxDrawdownPercent,
    double RecoveryFactor,
    int MaxConsecutiveWins,
    int MaxConsecutiveLosses,
    string CurrentStreak,
    int LongTrades,
    int ShortTrades,
    TimeSpan AverageDuration,
    TimeSpan AverageLongDuration,
    TimeSpan AverageShortDuration,
    double BestDay,
    double WorstDay);

public static class TradingStatsEngine
{
    private const double BreakEvenTolerance = 0.005;

    public static TradingStatsSnapshot Calculate(
        IEnumerable<HistoricalTrade> history,
        double initialBalance,
        Func<DateTime, DateTime> dayResolver)
    {
        var trades = history
            .GroupBy(t => t.PositionId)
            .Select(BuildTrade)
            .OrderBy(t => t.ClosingTime)
            .ToArray();

        if (trades.Length == 0)
            return Empty();

        var wins = trades.Where(t => t.NetProfit > BreakEvenTolerance).ToArray();
        var losses = trades.Where(t => t.NetProfit < -BreakEvenTolerance).ToArray();
        var breakEven = trades.Length - wins.Length - losses.Length;
        var classified = wins.Length + losses.Length;
        var winRate = classified > 0 ? wins.Length * 100.0 / classified : 0;

        var netProfit = trades.Sum(t => t.NetProfit);
        var grossWins = wins.Sum(t => t.NetProfit);
        var grossLoss = Math.Abs(losses.Sum(t => t.NetProfit));
        var profitFactor = grossLoss > 0 ? grossWins / grossLoss : grossWins > 0 ? double.PositiveInfinity : 0;
        var expectancy = netProfit / trades.Length;

        var avgWinPips = wins.Length > 0 ? wins.Average(t => t.Pips) : 0;
        var avgWinAmount = wins.Length > 0 ? wins.Average(t => t.NetProfit) : 0;
        var avgLossPips = losses.Length > 0 ? losses.Average(t => t.Pips) : 0;
        var avgLossAmount = losses.Length > 0 ? losses.Average(t => t.NetProfit) : 0;
        var avgRr = Math.Abs(avgLossPips) > 0 ? avgWinPips / Math.Abs(avgLossPips) : 0;

        var maxDrawdown = CalculateMaxDrawdown(trades);
        var maxDrawdownPercent = initialBalance > 0 ? maxDrawdown / initialBalance * 100.0 : 0;
        var recovery = maxDrawdown > 0 ? netProfit / maxDrawdown : 0;

        CalculateStreaks(trades, out var maxWins, out var maxLosses, out var currentStreak);

        var longs = trades.Where(t => t.TradeType == TradeType.Buy).ToArray();
        var shorts = trades.Where(t => t.TradeType == TradeType.Sell).ToArray();
        var avgDuration = AverageDuration(trades);
        var avgLong = AverageDuration(longs);
        var avgShort = AverageDuration(shorts);

        var daily = trades
            .GroupBy(t => dayResolver(t.ClosingTime))
            .Select(g => g.Sum(t => t.NetProfit))
            .ToArray();

        return new TradingStatsSnapshot(
            trades.Length,
            wins.Length,
            losses.Length,
            breakEven,
            winRate,
            netProfit,
            profitFactor,
            expectancy,
            avgWinPips,
            avgWinAmount,
            avgLossPips,
            avgLossAmount,
            avgRr,
            trades.Max(t => t.NetProfit),
            trades.Min(t => t.NetProfit),
            maxDrawdown,
            maxDrawdownPercent,
            recovery,
            maxWins,
            maxLosses,
            currentStreak,
            longs.Length,
            shorts.Length,
            avgDuration,
            avgLong,
            avgShort,
            daily.Length > 0 ? daily.Max() : 0,
            daily.Length > 0 ? daily.Min() : 0);
    }

    private static PositionTrade BuildTrade(IGrouping<int, HistoricalTrade> group)
    {
        var items = group.OrderBy(t => t.ClosingTime).ToArray();
        var totalVolume = items.Sum(t => t.VolumeInUnits);
        var weightedPips = totalVolume > 0
            ? items.Sum(t => t.Pips * t.VolumeInUnits) / totalVolume
            : items.Average(t => t.Pips);

        return new PositionTrade(
            group.Key,
            items[0].SymbolName,
            items[0].TradeType,
            items.Min(t => t.EntryTime),
            items.Max(t => t.ClosingTime),
            items.Sum(t => t.NetProfit),
            weightedPips);
    }

    private static double CalculateMaxDrawdown(IEnumerable<PositionTrade> trades)
    {
        var curve = 0.0;
        var peak = 0.0;
        var maxDrawdown = 0.0;
        foreach (var trade in trades)
        {
            curve += trade.NetProfit;
            peak = Math.Max(peak, curve);
            maxDrawdown = Math.Max(maxDrawdown, peak - curve);
        }
        return maxDrawdown;
    }

    private static void CalculateStreaks(
        IEnumerable<PositionTrade> trades,
        out int maxWins,
        out int maxLosses,
        out string currentStreak)
    {
        maxWins = 0;
        maxLosses = 0;
        var winStreak = 0;
        var lossStreak = 0;
        var currentType = string.Empty;
        var currentCount = 0;

        foreach (var trade in trades)
        {
            if (trade.NetProfit > BreakEvenTolerance)
            {
                winStreak++;
                lossStreak = 0;
                maxWins = Math.Max(maxWins, winStreak);
                if (currentType == "W") currentCount++; else { currentType = "W"; currentCount = 1; }
            }
            else if (trade.NetProfit < -BreakEvenTolerance)
            {
                lossStreak++;
                winStreak = 0;
                maxLosses = Math.Max(maxLosses, lossStreak);
                if (currentType == "L") currentCount++; else { currentType = "L"; currentCount = 1; }
            }
        }

        currentStreak = currentCount == 0 ? "--" : $"{currentCount}{currentType}";
    }

    private static TimeSpan AverageDuration(IReadOnlyCollection<PositionTrade> trades)
    {
        if (trades.Count == 0)
            return TimeSpan.Zero;
        return TimeSpan.FromTicks((long)trades.Average(t => (t.ClosingTime - t.EntryTime).Ticks));
    }

    private static TradingStatsSnapshot Empty()
        => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "--", 0, 0,
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0, 0);

    private sealed record PositionTrade(
        int PositionId,
        string SymbolName,
        TradeType TradeType,
        DateTime EntryTime,
        DateTime ClosingTime,
        double NetProfit,
        double Pips);
}
