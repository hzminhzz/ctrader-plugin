using cAlgo.API;
using cAlgo.API.Internals;

namespace PropRiskManager.Risk;

public sealed class CommissionSchedule
{
    public CommissionSchedule(double perSidePerUnit, double minimumPerSide, string description)
    {
        PerSidePerUnit = Math.Max(0, perSidePerUnit);
        MinimumPerSide = Math.Max(0, minimumPerSide);
        Description = description;
    }

    public double PerSidePerUnit { get; }
    public double MinimumPerSide { get; }
    public string Description { get; }

    public double EstimateRoundTrip(double volumeInUnits)
    {
        if (volumeInUnits <= 0)
            return 0;

        var oneSide = Math.Max(PerSidePerUnit * volumeInUnits, MinimumPerSide);
        return oneSide * 2.0;
    }
}

public static class CommissionEstimator
{
    public static bool TryCreate(
        Symbol symbol,
        IAssetConverter assetConverter,
        Asset accountAsset,
        double entryPrice,
        double stopLossPrice,
        out CommissionSchedule schedule,
        out string error)
    {
        schedule = null!;
        error = string.Empty;

        if (symbol.LotSize <= 0)
        {
            error = "Symbol lot size is invalid.";
            return false;
        }

        try
        {
            var referencePrice = Math.Max(Math.Abs(entryPrice), Math.Abs(stopLossPrice));
            var accountName = accountAsset.Name;
            var perSidePerUnit = symbol.CommissionType switch
            {
                SymbolCommissionType.UsdPerOneLot =>
                    Convert(assetConverter, symbol.Commission / symbol.LotSize, "USD", accountName),

                SymbolCommissionType.QuoteCurrencyPerOneLot =>
                    Convert(assetConverter, symbol.Commission / symbol.LotSize, symbol.QuoteAsset.Name, accountName),

                SymbolCommissionType.UsdPerMillionUsdVolume =>
                    Convert(
                        assetConverter,
                        symbol.Commission * Convert(assetConverter, 1.0, symbol.BaseAsset.Name, "USD") / 1_000_000.0,
                        "USD",
                        accountName),

                SymbolCommissionType.PercentageOfTradingVolume =>
                    Convert(
                        assetConverter,
                        referencePrice * symbol.Commission / 100.0,
                        symbol.QuoteAsset.Name,
                        accountName),

                _ => throw new NotSupportedException($"Unsupported commission type: {symbol.CommissionType}")
            };

            var minimumPerSide = EstimateMinimumPerSide(symbol, assetConverter, accountName);
            schedule = new CommissionSchedule(
                perSidePerUnit,
                minimumPerSide,
                $"{symbol.CommissionType} base={symbol.Commission:G6}");
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static double EstimateMinimumPerSide(Symbol symbol, IAssetConverter assetConverter, string accountAsset)
    {
        if (symbol.MinCommission <= 0)
            return 0;

        return symbol.MinCommissionType switch
        {
            SymbolMinCommissionType.Asset when symbol.MinCommissionAsset != null =>
                Convert(assetConverter, symbol.MinCommission, symbol.MinCommissionAsset.Name, accountAsset),

            SymbolMinCommissionType.QuoteAsset =>
                Convert(assetConverter, symbol.MinCommission, symbol.QuoteAsset.Name, accountAsset),

            _ => 0
        };
    }

    private static double Convert(IAssetConverter converter, double value, string from, string to)
    {
        if (value == 0 || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return value;

        return converter.Convert(value, from, to);
    }
}
