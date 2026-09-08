using System;
using System.Numerics;

namespace MasterOfPuppets.LuaScripting;

public readonly record struct LuaPetTrajectorySample(
    Vector3 RelativeOffset,
    string Anchor,
    TimeSpan MinimumInterval,
    float MinimumDistance,
    double ElapsedSeconds) {

    public const float MaximumRadius = 20f;

    public static bool TryCreate(
        double x,
        double z,
        string? anchor,
        double intervalSeconds,
        double minimumDistance,
        double elapsedSeconds,
        out LuaPetTrajectorySample sample) {
        sample = default;
        if (!double.IsFinite(x) || !double.IsFinite(z)
            || !double.IsFinite(intervalSeconds) || !double.IsFinite(minimumDistance)
            || !double.IsFinite(elapsedSeconds))
            return false;

        var offset = new Vector3((float)x, 0f, (float)z);
        var radius = new Vector2(offset.X, offset.Z).Length();
        if (radius > MaximumRadius)
            offset *= MaximumRadius / radius;

        sample = new LuaPetTrajectorySample(
            offset,
            string.IsNullOrWhiteSpace(anchor) ? "target" : anchor.Trim(),
            TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, 0.12, 2.0)),
            (float)Math.Clamp(minimumDistance, 0.01, 2.0),
            elapsedSeconds);
        return true;
    }
}
