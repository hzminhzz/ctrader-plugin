using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PropRiskManager.Domain;

namespace PropRiskManager.Tests;

[TestClass]
public sealed class SmartDashboardTests
{
    [TestMethod]
    public void Build_SeparatesAccountAndActiveSymbolScopes()
    {
        var states = new[] { State(1, SmartAlertState.Armed, SmartAlertState.Triggered), State(2, SmartAlertState.Armed, SmartAlertState.Disarmed) };
        var positions = new[] { new SmartDashboardPosition(1, "EURUSD", 10), new SmartDashboardPosition(2, "GBPUSD", -4), new SmartDashboardPosition(3, "EURUSD", 3) };
        var now = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);
        var d = SmartDashboardEngine.Build(50000, now, "EURUSD", positions, states);
        Assert.AreEqual(3, d.Account.OpenPositions);
        Assert.AreEqual(50000, d.Account.Equity, 0.000001);
        Assert.AreEqual(now, d.Account.LastUpdateUtc);
        Assert.AreEqual(2, d.Account.ArmedSmartAlerts);
        Assert.AreEqual(2, d.ActiveSymbol.OpenPositions);
        Assert.AreEqual(13, d.ActiveSymbol.NetProfit, 0.000001);
    }

    [TestMethod]
    public void SymbolSwitch_DoesNotChangeAccountScope()
    {
        var positions = new[] { new SmartDashboardPosition(1, "EURUSD", 10), new SmartDashboardPosition(2, "GBPUSD", -4) };
        var eur = SmartDashboardEngine.Build(100, DateTime.UnixEpoch, "EURUSD", positions, Array.Empty<SmartPositionState>());
        var gbp = SmartDashboardEngine.Build(100, DateTime.UnixEpoch, "GBPUSD", positions, Array.Empty<SmartPositionState>());
        Assert.AreEqual(eur.Account.OpenPositions, gbp.Account.OpenPositions);
        Assert.AreEqual(10, eur.ActiveSymbol.NetProfit, 0.000001);
        Assert.AreEqual(-4, gbp.ActiveSymbol.NetProfit, 0.000001);
    }

    [TestMethod]
    public void ActiveSymbolClosePlan_NeverContainsOtherSymbols()
    {
        var positions = new[] { new SmartDashboardPosition(1, "EURUSD", 1), new SmartDashboardPosition(2, "GBPUSD", 2), new SmartDashboardPosition(3, "EURUSD", 3) };
        var plan = SmartDashboardEngine.PlanClose(SmartCloseScope.ActiveSymbol, "EURUSD", positions);
        CollectionAssert.AreEquivalent(new[] { 1, 3 }, new List<int>(plan.PositionIds));
        Assert.AreEqual("EURUSD", plan.SymbolName);
    }

    [TestMethod]
    public void AccountClosePlan_IsExplicitAndIncludesAllPositions()
    {
        var positions = new[] { new SmartDashboardPosition(1, "EURUSD", 1), new SmartDashboardPosition(2, "GBPUSD", 2) };
        var plan = SmartDashboardEngine.PlanClose(SmartCloseScope.Account, "EURUSD", positions);
        Assert.AreEqual(2, plan.Count);
        Assert.IsNull(plan.SymbolName);
        Assert.AreEqual(SmartCloseScope.Account, plan.Scope);
    }

    [TestMethod]
    public void EmptyState_IsDeterministic()
    {
        var d = SmartDashboardEngine.Build(100, DateTime.UnixEpoch, "EURUSD", Array.Empty<SmartDashboardPosition>(), Array.Empty<SmartPositionState>());
        Assert.AreEqual(0, d.Account.OpenPositions);
        Assert.AreEqual(0, d.Account.ArmedSmartAlerts);
        Assert.AreEqual(0, d.ActiveSymbol.OpenPositions);
    }

    private static SmartPositionState State(int id, params SmartAlertState[] alerts)
    {
        var state = new SmartPositionState { PositionId = id };
        for (var i = 0; i < alerts.Length; i++) state.AlertDefinitions.Add(new SmartAlertDefinition { AlertId = $"{id}:{i}", State = alerts[i] });
        return state;
    }
}
