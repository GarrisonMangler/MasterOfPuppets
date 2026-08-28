using System;
using System.Collections.Generic;
using System.Linq;

using MasterOfPuppets.Formations;

namespace MasterOfPuppets.LuaScripting.Synchronization;

public static class LuaConductorTrustModes {
    public const string SelfOnly = "self_only";
    public const string Allowlist = "allowlist";

    public static string Normalize(string? mode) => mode?.Trim().ToLowerInvariant() switch {
        Allowlist => Allowlist,
        _ => SelfOnly,
    };
}

public static class LuaConductorTrustPolicy {
    public static bool IsTrusted(
        string actualSender,
        string localPlayer,
        string? mode,
        IReadOnlyCollection<string>? allowlist,
        out string reason) {
        var sender = FormationCharacterName.NormalizeWorldSeparator(actualSender);
        var local = FormationCharacterName.NormalizeWorldSeparator(localPlayer);
        if (string.IsNullOrWhiteSpace(sender)) {
            reason = "the chat sender identity is unavailable";
            return false;
        }

        if (FormationCharacterName.Matches(sender, local)) {
            reason = string.Empty;
            return true;
        }

        if (LuaConductorTrustModes.Normalize(mode) != LuaConductorTrustModes.Allowlist) {
            reason = $"sender '{sender}' is not this client; Lua conductor mode is self-only";
            return false;
        }

        if ((allowlist ?? Array.Empty<string>()).Any(candidate => FormationCharacterName.Matches(sender, candidate))) {
            reason = string.Empty;
            return true;
        }

        reason = $"sender '{sender}' is not in the Lua conductor allowlist";
        return false;
    }
}
