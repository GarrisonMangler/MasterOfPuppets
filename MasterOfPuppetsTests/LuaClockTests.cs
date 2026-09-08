using System;
using MasterOfPuppets.LuaScripting;

using Xunit;

public class LuaClockTests {
    [Fact]
    public void EorzeaTime_AdvancesOneHourIn175EarthSeconds() {
        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var before = LuaClock.SecondsOfDay("eorzea", start);
        var after = LuaClock.SecondsOfDay("eorzea", start.AddSeconds(175));

        Assert.Equal(3600, (after - before + 86400) % 86400, precision: 5);
    }

    [Theory]
    [InlineData("eastern", 8)]
    [InlineData("central", 7)]
    [InlineData("mountain", 6)]
    [InlineData("pacific", 5)]
    public void UsTimeZones_ApplyDaylightSavingRules(string timezone, int expectedHour) {
        var summerUtc = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(expectedHour * 3600, LuaClock.SecondsOfDay(timezone, summerUtc));
    }

    [Fact]
    public void UnknownTimezone_FallsBackToEastern() {
        var utc = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(
            LuaClock.SecondsOfDay("eastern", utc),
            LuaClock.SecondsOfDay("unknown", utc));
    }

}
