using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MasterOfPuppets.LuaScripting;

public sealed record LuaActorFollowRequest(
    IReadOnlyList<string> AnchorCandidates,
    Vector3 RelativeOffset,
    bool FaceAnchor = true,
    float FacingOffsetRadians = 0f,
    float Precision = 0.1f,
    bool BrakeAtPosition = true,
    bool PursuitPrediction = false,
    bool ImmediateSteering = true) {

    public LuaActorFollowRequest Validate() {
        var candidates = AnchorCandidates?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        if (candidates.Length == 0)
            throw new ArgumentException("follow_actor requires at least one anchor candidate");
        if (candidates.Length > 64)
            throw new ArgumentException("follow_actor accepts at most 64 anchor candidates");
        if (!IsFinite(RelativeOffset) || RelativeOffset.Length() > 100f)
            throw new ArgumentException("follow_actor offset must be finite and within 100 yalms");
        if (!float.IsFinite(FacingOffsetRadians))
            throw new ArgumentException("follow_actor facing offset must be finite");
        if (!float.IsFinite(Precision) || Precision < 0.02f || Precision > 5f)
            throw new ArgumentException("follow_actor precision must be between 0.02 and 5 yalms");

        return this with { AnchorCandidates = candidates };
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
