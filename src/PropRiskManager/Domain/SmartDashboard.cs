using System;
using System.Collections.Generic;
using System.Linq;

namespace PropRiskManager.Domain;

public sealed record SmartDashboardPosition(int PositionId, string SymbolName, double NetProfit);
public sealed record SmartAccountDashboard(int OpenPositions, double Equity, DateTime LastUpdateUtc, int ArmedSmartAlerts);
public sealed record SmartSymbolDashboard(string SymbolName, int OpenPositions, double NetProfit);
public sealed record SmartDashboardSnapshot(SmartAccountDashboard Account, SmartSymbolDashboard ActiveSymbol);

public enum SmartCloseScope { ActiveSymbol, Account }

public sealed record SmartClosePlan(SmartCloseScope Scope, string? SymbolName, IReadOnlyList<int> PositionIds)
{
    public int Count => PositionIds.Count;
}

public static class SmartDashboardEngine
{
    public static SmartDashboardSnapshot Build(double equity, DateTime lastUpdateUtc, string activeSymbol, IEnumerable<SmartDashboardPosition> positions, IEnumerable<SmartPositionState> smartStates)
    {
        var all = positions.ToArray();
        var scoped = all.Where(p => string.Equals(p.SymbolName, activeSymbol, StringComparison.Ordinal)).ToArray();
        return new SmartDashboardSnapshot(
            new SmartAccountDashboard(all.Length, equity, lastUpdateUtc, SmartPositionEngine.CountArmedAlerts(smartStates)),
            new SmartSymbolDashboard(activeSymbol, scoped.Length, scoped.Sum(p => p.NetProfit)));
    }

    public static SmartClosePlan PlanClose(SmartCloseScope scope, string activeSymbol, IEnumerable<SmartDashboardPosition> positions)
    {
        var selected = scope == SmartCloseScope.Account ? positions : positions.Where(p => string.Equals(p.SymbolName, activeSymbol, StringComparison.Ordinal));
        return new SmartClosePlan(scope, scope == SmartCloseScope.ActiveSymbol ? activeSymbol : null, selected.Select(p => p.PositionId).ToArray());
    }
}
