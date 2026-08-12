namespace PropRiskManager.Domain;

public enum RiskMode
{
    PercentEquity,
    PercentBalance,
    PercentFreeMargin,
    FixedAmount,
    FixedLots
}

public enum OrderKind
{
    Market,
    Limit,
    Stop
}
