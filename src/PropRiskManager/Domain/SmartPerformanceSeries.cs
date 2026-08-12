using System;
using System.Collections.Generic;
using System.Linq;

namespace PropRiskManager.Domain;

public sealed record SmartPerformanceSample(DateTime SampledAtUtc, double MidPrice, double BasisPoints);

public sealed class SmartPerformanceSeriesState
{
    public string SymbolName { get; set; } = string.Empty;
    public double BaselineMidPrice { get; set; }
    public List<SmartPerformanceSample> Samples { get; set; } = new();
}

public sealed record SmartPerformanceUpdate(SmartPerformanceSeriesState State, bool AddedSample, string Diagnostic);

public static class SmartPerformanceSeriesEngine
{
    public static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(2);
    public const int MaximumSamples = 60;

    public static SmartPerformanceUpdate AddSample(SmartPerformanceSeriesState? persisted, string symbolName, double midPrice, DateTime sampledAtUtc)
    {
        if (string.IsNullOrWhiteSpace(symbolName)) return new SmartPerformanceUpdate(Clone(persisted), false, "Active symbol is unavailable; performance sample skipped.");
        if (!IsFinitePositive(midPrice)) return new SmartPerformanceUpdate(Clone(persisted), false, "Mid price is invalid; performance sample skipped.");
        if (sampledAtUtc == default) return new SmartPerformanceUpdate(Clone(persisted), false, "Sample timestamp is invalid; performance sample skipped.");
        var state = persisted == null || !string.Equals(persisted.SymbolName, symbolName, StringComparison.Ordinal)
            ? new SmartPerformanceSeriesState { SymbolName = symbolName, BaselineMidPrice = midPrice }
            : Clone(persisted);
        if (!IsFinitePositive(state.BaselineMidPrice)) state.BaselineMidPrice = midPrice;
        var last = state.Samples.LastOrDefault();
        if (last != null && sampledAtUtc < last.SampledAtUtc.Add(SampleInterval)) return new SmartPerformanceUpdate(state, false, string.Empty);
        var bps = (midPrice / state.BaselineMidPrice - 1.0) * 10_000.0;
        state.Samples.Add(new SmartPerformanceSample(sampledAtUtc, midPrice, bps));
        if (state.Samples.Count > MaximumSamples) state.Samples.RemoveRange(0, state.Samples.Count - MaximumSamples);
        return new SmartPerformanceUpdate(state, true, string.Empty);
    }

    private static SmartPerformanceSeriesState Clone(SmartPerformanceSeriesState? state) => state == null
        ? new SmartPerformanceSeriesState()
        : new SmartPerformanceSeriesState { SymbolName = state.SymbolName, BaselineMidPrice = state.BaselineMidPrice, Samples = state.Samples?.ToList() ?? new List<SmartPerformanceSample>() };
    private static bool IsFinitePositive(double value) => value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
