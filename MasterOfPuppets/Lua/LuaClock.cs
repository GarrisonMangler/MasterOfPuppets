using System;

namespace MasterOfPuppets.LuaScripting;

internal static class LuaClock {
    private const double EorzeaRate = 144.0 / 7.0;
    private static readonly TimeZoneInfo Eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
    private static readonly TimeZoneInfo Mountain = TimeZoneInfo.FindSystemTimeZoneById("Mountain Standard Time");
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

    public static double SecondsOfDay(string? clock, DateTimeOffset utcNow) {
        var normalized = clock?.Trim().ToLowerInvariant() ?? "eorzea";
        if (normalized is "eorzea" or "et")
            return PositiveModulo(utcNow.ToUnixTimeMilliseconds() / 1000.0 * EorzeaRate, 86400.0);

        var zone = normalized switch {
            "eastern" or "est" or "edt" => Eastern,
            "central" or "cst" or "cdt" => Central,
            "mountain" or "mst" or "mdt" => Mountain,
            "pacific" or "pst" or "pdt" => Pacific,
            _ => Eastern,
        };
        var local = TimeZoneInfo.ConvertTime(utcNow, zone);
        return local.TimeOfDay.TotalSeconds;
    }

    private static double PositiveModulo(double value, double modulus) =>
        ((value % modulus) + modulus) % modulus;
}
