using System;

namespace PropRiskManager.Risk;

public static class GuardianTradingClock
{
    public static bool TryGetTradingDay(
        DateTime utcTime,
        string? timeZoneId,
        int resetHour,
        double fallbackUtcOffsetHours,
        out DateTime tradingDay,
        out string error)
    {
        var utc = DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
        var hour = Math.Max(0, Math.Min(23, resetHour));

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            tradingDay = utc.AddHours(Math.Max(-14, Math.Min(14, fallbackUtcOffsetHours)))
                .AddHours(-hour)
                .Date;
            error = string.Empty;
            return true;
        }

        if (!TryResolveTimeZone(timeZoneId.Trim(), out var timeZone))
        {
            tradingDay = utc.AddHours(Math.Max(-14, Math.Min(14, fallbackUtcOffsetHours)))
                .AddHours(-hour)
                .Date;
            error = $"Unknown reset time zone '{timeZoneId}'.";
            return false;
        }

        var localTime = TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone);
        tradingDay = localTime.AddHours(-hour).Date;
        error = string.Empty;
        return true;
    }

    public static bool TryResolveTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
    {
        if (TryFind(timeZoneId, out timeZone))
            return true;

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId) &&
            TryFind(windowsId, out timeZone))
            return true;

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaId) &&
            TryFind(ianaId, out timeZone))
            return true;

        timeZone = TimeZoneInfo.Utc;
        return false;
    }

    private static bool TryFind(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }
}
