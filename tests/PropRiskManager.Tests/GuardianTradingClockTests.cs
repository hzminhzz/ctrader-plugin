using Microsoft.VisualStudio.TestTools.UnitTesting;
using PropRiskManager.Risk;

namespace PropRiskManager.Tests;

[TestClass]
public class GuardianTradingClockTests
{
    [TestMethod]
    public void FixedOffsetFallbackHonorsResetHour()
    {
        var ok = GuardianTradingClock.TryGetTradingDay(
            new DateTime(2026, 8, 11, 22, 30, 0, DateTimeKind.Utc),
            string.Empty,
            1,
            2,
            out var day,
            out var error);

        Assert.IsTrue(ok, error);
        Assert.AreEqual(new DateTime(2026, 8, 11), day);
    }

    [TestMethod]
    public void NewYorkSummerUsesDaylightSavingOffset()
    {
        var ok = GuardianTradingClock.TryGetTradingDay(
            new DateTime(2026, 7, 1, 4, 30, 0, DateTimeKind.Utc),
            "America/New_York",
            1,
            0,
            out var day,
            out var error);

        Assert.IsTrue(ok, error);
        Assert.AreEqual(new DateTime(2026, 6, 30), day);
    }

    [TestMethod]
    public void NewYorkWinterUsesStandardOffset()
    {
        var ok = GuardianTradingClock.TryGetTradingDay(
            new DateTime(2026, 1, 1, 5, 30, 0, DateTimeKind.Utc),
            "America/New_York",
            1,
            0,
            out var day,
            out var error);

        Assert.IsTrue(ok, error);
        Assert.AreEqual(new DateTime(2025, 12, 31), day);
    }

    [TestMethod]
    public void WindowsTimeZoneIdResolvesCrossPlatform()
    {
        var ok = GuardianTradingClock.TryGetTradingDay(
            new DateTime(2026, 7, 1, 4, 30, 0, DateTimeKind.Utc),
            "Eastern Standard Time",
            1,
            0,
            out var day,
            out var error);

        Assert.IsTrue(ok, error);
        Assert.AreEqual(new DateTime(2026, 6, 30), day);
    }

    [TestMethod]
    public void InvalidNamedTimeZoneFailsClosed()
    {
        var ok = GuardianTradingClock.TryGetTradingDay(
            new DateTime(2026, 8, 12, 0, 30, 0, DateTimeKind.Utc),
            "Not/A_Real_Time_Zone",
            0,
            0,
            out _,
            out var error);

        Assert.IsFalse(ok);
        StringAssert.Contains(error, "Unknown reset time zone");
    }
}
