using Microsoft.VisualStudio.TestTools.UnitTesting;
using PropRiskManager.Domain;

namespace PropRiskManager.Tests;

[TestClass]
public sealed class SmartPerformanceDashboardTests
{
    private static readonly DateTime T0 = new(2026, 8, 12, 15, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void PerformanceSeries_UsesBaselineBpsAndTwoSecondCadence()
    {
        var first = SmartPerformanceSeriesEngine.AddSample(null, "EURUSD", 1.1000, T0);
        Assert.IsTrue(first.AddedSample);
        Assert.AreEqual(0, first.State.Samples[0].BasisPoints, 0.000001);
        var tooSoon = SmartPerformanceSeriesEngine.AddSample(first.State, "EURUSD", 1.1010, T0.AddSeconds(1));
        Assert.IsFalse(tooSoon.AddedSample);
        var second = SmartPerformanceSeriesEngine.AddSample(tooSoon.State, "EURUSD", 1.1011, T0.AddSeconds(2));
        Assert.IsTrue(second.AddedSample);
        Assert.AreEqual(10, second.State.Samples[1].BasisPoints, 0.000001);
    }

    [TestMethod]
    public void PerformanceSeries_SymbolSwitchStartsCleanBaseline()
    {
        var eur = SmartPerformanceSeriesEngine.AddSample(null, "EURUSD", 1.1000, T0).State;
        eur = SmartPerformanceSeriesEngine.AddSample(eur, "EURUSD", 1.1011, T0.AddSeconds(2)).State;
        var xau = SmartPerformanceSeriesEngine.AddSample(eur, "XAUUSD", 2400, T0.AddSeconds(3));
        Assert.AreEqual("XAUUSD", xau.State.SymbolName);
        Assert.AreEqual(1, xau.State.Samples.Count);
        Assert.AreEqual(0, xau.State.Samples[0].BasisPoints, 0.000001);
    }

    [TestMethod]
    public void PerformanceSeries_IsBoundedAtSixtySamplesAndRebasesOldestRetainedSample()
    {
        SmartPerformanceSeriesState? state = null;
        for (var i = 0; i < 75; i++)
            state = SmartPerformanceSeriesEngine.AddSample(state, "EURUSD", 1.1000 + i * 0.00001, T0.AddSeconds(i * 2)).State;
        Assert.AreEqual(SmartPerformanceSeriesEngine.MaximumSamples, state!.Samples.Count);
        Assert.AreEqual(T0.AddSeconds(30), state.Samples[0].SampledAtUtc);
        Assert.AreEqual(0, state.Samples[0].BasisPoints, 0.000001);
    }

    [TestMethod]
    public void ContextCard_UsesSymbolScopeWhenActiveSymbolHasPositions()
    {
        var card = SmartDashboardEngine.BuildContextualActionCard("EURUSD", new[] { new SmartDashboardPosition(1,"EURUSD",10), new SmartDashboardPosition(2,"EURUSD",-3), new SmartDashboardPosition(3,"XAUUSD",20) });
        Assert.AreEqual(SmartCloseScope.ActiveSymbol, card.Scope);
        Assert.AreEqual(2, card.PositionCount);
        Assert.AreEqual(7, card.NetProfit, 0.000001);
        Assert.AreEqual("2 pos / CLOSE SYMBOL", card.ActionText);
        Assert.IsTrue(card.CanClose);
    }

    [TestMethod]
    public void ContextCard_UsesAccountScopeWhenOnlyOtherSymbolsAreOpen()
    {
        var card = SmartDashboardEngine.BuildContextualActionCard("EURUSD", new[] { new SmartDashboardPosition(1,"XAUUSD",12), new SmartDashboardPosition(2,"GBPUSD",-2) });
        Assert.AreEqual(SmartCloseScope.Account, card.Scope);
        Assert.AreEqual(2, card.PositionCount);
        Assert.AreEqual(10, card.NetProfit, 0.000001);
        Assert.AreEqual("2 pos / CLOSE ALL", card.ActionText);
    }

    [TestMethod]
    public void ContextCard_ZeroPositionStateIsSymbolScopedAndDisabled()
    {
        var card = SmartDashboardEngine.BuildContextualActionCard("EURUSD", System.Array.Empty<SmartDashboardPosition>());
        Assert.AreEqual(SmartCloseScope.ActiveSymbol, card.Scope);
        Assert.AreEqual(0, card.PositionCount);
        Assert.AreEqual("0 pos / CLOSE SYMBOL", card.ActionText);
        Assert.IsFalse(card.CanClose);
    }
}
