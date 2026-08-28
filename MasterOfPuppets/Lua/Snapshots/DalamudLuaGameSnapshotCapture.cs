using System;
using System.Collections.Generic;
using System.Linq;

using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Buddy;
using Dalamud.Game.ClientState.Objects.Types;

using MasterOfPuppets.Extensions.Dalamud;
using MasterOfPuppets.Formations;

namespace MasterOfPuppets.LuaScripting.Snapshots;

internal static class DalamudLuaGameSnapshotCapture {
    private const int MaximumVisibleActors = 256;
    private static readonly (ConditionFlag Flag, string Name)[] ConditionEntries =
        Enum.GetValues<ConditionFlag>()
            .Select(flag => (flag, flag.ToString()))
            .ToArray();

    /// <summary>
    /// Captures only the state consumed by
    /// <see cref="MasterOfPuppets.LuaScripting.Events.LuaGameEventTracker"/>.
    /// This intentionally avoids enumerating, sorting, and materializing the full
    /// object table for the automatic four-hertz event observation path.
    /// </summary>
    public static LuaGameSnapshot CaptureEvents(
        IReadOnlyList<ulong> participantCids,
        IReadOnlyDictionary<ulong, string> configuredNames) {
        var local = DalamudApi.ObjectTable.LocalPlayer;
        var localId = local?.GameObjectId ?? 0;
        LuaActorSnapshot? Resolve(IGameObject? actor, string authority) =>
            actor == null ? null : CaptureActor(actor, localId, authority);

        return new LuaGameSnapshot(
            Resolve(local, "local"),
            Resolve(DalamudApi.TargetManager.Target, "selected-target"),
            Resolve(DalamudApi.TargetManager.FocusTarget, "focus-target"),
            null,
            Array.Empty<LuaActorSnapshot>(),
            CaptureParticipants(participantCids, configuredNames, local, Resolve, worldActors: null),
            DalamudApi.ClientState.TerritoryType,
            DalamudApi.ClientState.MapId,
            DalamudApi.ClientState.Instance,
            DalamudApi.ClientState.IsLoggedIn,
            DalamudApi.ClientState.IsGPosing,
            DalamudApi.ClientState.IsPvP,
            CaptureConditions());
    }

    public static LuaGameSnapshot Capture(
        IReadOnlyList<ulong> participantCids,
        IReadOnlyDictionary<ulong, string> configuredNames,
        ulong runTargetObjectId,
        uint runTargetEntityId,
        string runTargetName) {
        var local = DalamudApi.ObjectTable.LocalPlayer;
        var localId = local?.GameObjectId ?? 0;
        var visible = DalamudApi.ObjectTable
            .Where(actor => actor is { Address: not 0 })
            .OrderBy(actor => actor.CurrentDistance)
            .Take(MaximumVisibleActors)
            .Select(actor => CaptureActor(actor, localId, "visible"))
            .ToArray();
        var byGameObject = visible.ToDictionary(actor => actor.GameObjectId, StringComparer.Ordinal);
        LuaActorSnapshot? Resolve(IGameObject? actor, string authority) {
            if (actor == null)
                return null;
            if (byGameObject.TryGetValue(actor.GameObjectId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var snapshot))
                return snapshot with { Authority = authority };
            return CaptureActor(actor, localId, authority);
        }
        LuaBuddySnapshot? CaptureBuddy(IBuddyMember? buddy, string kind) {
            if (buddy == null || buddy.Address == IntPtr.Zero)
                return null;
            var actor = Resolve(buddy.GameObject, "buddy");
            return new LuaBuddySnapshot(
                kind,
                actor?.GameObjectId ?? "0",
                buddy.EntityId,
                buddy.DataID,
                buddy.CurrentHP,
                buddy.MaxHP,
                actor);
        }

        var worldActors = DalamudApi.ObjectTable
            .Where(actor => actor is { Address: not 0 })
            .ToArray();
        var participants = CaptureParticipants(participantCids, configuredNames, local, Resolve, worldActors);

        var runTarget = DalamudApi.ObjectTable.FirstOrDefault(actor =>
            (runTargetEntityId != 0 && runTargetEntityId != 0xE0000000 && actor.EntityId == runTargetEntityId)
            || (runTargetObjectId != 0 && actor.GameObjectId == runTargetObjectId));
        if (runTarget == null && !string.IsNullOrWhiteSpace(runTargetName))
            runTarget = DalamudApi.ObjectTable
                .Where(actor => actor is { Address: not 0 })
                .OrderByDescending(actor => FormationCharacterName.MatchScore(
                    runTargetName,
                    actor.GetPlayerNameWorld() ?? actor.Name.TextValue))
                .FirstOrDefault(actor => FormationCharacterName.MatchScore(
                    runTargetName,
                    actor.GetPlayerNameWorld() ?? actor.Name.TextValue) >= 0);

        var buddies = DalamudApi.BuddyList
            .Select(buddy => CaptureBuddy(buddy, "battle"))
            .Append(CaptureBuddy(DalamudApi.BuddyList.CompanionBuddy, "companion"))
            .Append(CaptureBuddy(DalamudApi.BuddyList.PetBuddy, "pet"))
            .Where(buddy => buddy != null)
            .Cast<LuaBuddySnapshot>()
            .ToArray();
        return new LuaGameSnapshot(
            Resolve(local, "local"),
            Resolve(DalamudApi.TargetManager.Target, "selected-target"),
            Resolve(DalamudApi.TargetManager.FocusTarget, "focus-target"),
            Resolve(runTarget, "run-target"),
            visible,
            participants,
            DalamudApi.ClientState.TerritoryType,
            DalamudApi.ClientState.MapId,
            DalamudApi.ClientState.Instance,
            DalamudApi.ClientState.IsLoggedIn,
            DalamudApi.ClientState.IsGPosing,
            DalamudApi.ClientState.IsPvP,
            CaptureConditions()) {
            PlayerProfile = CapturePlayerProfile(),
            Party = DalamudApi.PartyList
                .Select(member => Resolve(member.GameObject, "party"))
                .Where(actor => actor != null)
                .Cast<LuaActorSnapshot>()
                .ToArray(),
            Buddies = buddies,
        };
    }

    private static IReadOnlyList<LuaParticipantSnapshot> CaptureParticipants(
        IReadOnlyList<ulong> participantCids,
        IReadOnlyDictionary<ulong, string> configuredNames,
        IGameObject? local,
        Func<IGameObject?, string, LuaActorSnapshot?> resolve,
        IReadOnlyList<IGameObject>? worldActors) {
        var participants = new List<LuaParticipantSnapshot>(participantCids.Count);
        for (var slot = 0; slot < participantCids.Count; slot++) {
            var cid = participantCids[slot];
            var isLocal = cid != 0 && cid == DalamudApi.PlayerState.ContentId;
            var party = DalamudApi.PartyList.FirstOrDefault(member => member.ContentId == cid);
            var configuredName = configuredNames.GetValueOrDefault(cid) ?? string.Empty;
            var worldObject = isLocal
                ? local
                : party?.GameObject ?? FindVisibleRosterActor(configuredName, worldActors);
            var actor = isLocal
                ? resolve(local, "local")
                : worldObject != null
                    ? resolve(worldObject, party?.GameObject != null ? "party" : "visible")
                    : null;
            var name = actor?.Name
                ?? (party == null ? null : $"{party.Name.TextValue}@{party.World.ValueNullable?.Name}")
                ?? configuredName;
            var isVisible = isLocal || actor != null;
            participants.Add(new LuaParticipantSnapshot(
                slot,
                cid,
                name,
                isLocal,
                actor?.Authority ?? "configured",
                actor,
                isVisible));
        }
        return participants;
    }

    private static IGameObject? FindVisibleRosterActor(
        string configuredName,
        IReadOnlyList<IGameObject>? worldActors) {
        if (worldActors == null || string.IsNullOrWhiteSpace(configuredName))
            return null;

        IGameObject? bestMatch = null;
        var bestScore = -1;
        foreach (var actor in worldActors) {
            if (actor is not { Address: not 0 })
                continue;
            var actorName = actor.GetPlayerNameWorld() ?? actor.Name.TextValue;
            var score = FormationCharacterName.MatchScore(configuredName, actorName);
            if (score <= bestScore)
                continue;
            bestMatch = actor;
            bestScore = score;
            if (score == int.MaxValue)
                break;
        }

        return bestScore >= 0 ? bestMatch : null;
    }

    private static IReadOnlyDictionary<string, bool> CaptureConditions() {
        var conditions = new Dictionary<string, bool>(ConditionEntries.Length, StringComparer.Ordinal);
        foreach (var (flag, name) in ConditionEntries)
            conditions.Add(name, DalamudApi.Condition[flag]);
        return conditions;
    }

    private static LuaPlayerProfileSnapshot? CapturePlayerProfile() {
        var player = DalamudApi.PlayerState;
        if (!player.IsLoaded || player.ContentId == 0)
            return null;
        return new LuaPlayerProfileSnapshot(
            player.ContentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            player.CharacterName,
            player.EntityId,
            player.CurrentWorld.RowId,
            player.HomeWorld.RowId,
            player.ClassJob.RowId,
            player.Level,
            player.EffectiveLevel,
            player.Race.RowId,
            player.Tribe.RowId,
            player.GrandCompany.RowId,
            player.Sex.ToString(),
            player.IsLoaded,
            player.IsLevelSynced,
            player.IsMentor,
            player.IsBattleMentor,
            player.IsTradeMentor,
            player.IsNovice,
            player.IsReturner,
            player.BaseStrength,
            player.BaseDexterity,
            player.BaseVitality,
            player.BaseIntelligence,
            player.BaseMind,
            player.BasePiety);
    }

    private static LuaActorSnapshot CaptureActor(IGameObject actor, ulong localId, string authority) {
        var character = actor as ICharacter;
        var battle = actor as IBattleChara;
        return new LuaActorSnapshot(
            actor.GetPlayerNameWorld() ?? actor.Name.TextValue,
            actor.GameObjectId == localId ? "local" : authority,
            actor.ObjectKind.ToString(),
            actor.GameObjectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            actor.EntityId,
            actor.TargetObjectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            actor.Position,
            actor.Rotation,
            actor.GameObjectId == localId,
            actor.IsTargetable,
            actor.IsDead,
            character?.ClassJob.RowId ?? 0,
            character?.Level ?? 0,
            character?.CurrentHp ?? 0,
            character?.MaxHp ?? 0,
            character?.CurrentMp ?? 0,
            character?.MaxMp ?? 0,
            character?.CurrentCp ?? 0,
            character?.MaxCp ?? 0,
            character?.CurrentGp ?? 0,
            character?.MaxGp ?? 0,
            character?.StatusFlags.ToString() ?? string.Empty,
            battle?.IsCasting ?? false,
            battle?.CastActionId ?? 0,
            (battle?.CastTargetObjectId ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            battle?.CurrentCastTime ?? 0f,
            battle?.TotalCastTime ?? 0f);
    }
}
