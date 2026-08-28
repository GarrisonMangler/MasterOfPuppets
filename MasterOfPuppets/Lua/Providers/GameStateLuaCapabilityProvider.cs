using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Lua;

using MasterOfPuppets.Formations;
using MasterOfPuppets.LuaScripting.Runtime;
using MasterOfPuppets.LuaScripting.Snapshots;

namespace MasterOfPuppets.LuaScripting.Providers;

public sealed class GameStateLuaCapabilityProvider : ILuaCapabilityProvider {
    private static readonly LuaCapabilityDescriptor Capability = new(
        "mop.game-state",
        "2.1.0",
        "Immutable self, target, actor, participant, buddy, territory, condition snapshots, and bounded waits.",
        ["game-state.observe"]);

    public LuaCapabilityDescriptor Descriptor => Capability;

    public void Register(LuaApiRegistrationContext registration) {
        var self = registration.GetOrCreateModule("self");
        self["snapshot"] = SnapshotFunction(registration, snapshot => snapshot.Self);

        var target = registration.GetOrCreateModule("target");
        target["snapshot"] = new LuaFunction(async (call, cancellationToken) => {
            var kind = call.ArgumentCount == 0 ? "selected" : call.GetArgument<string>(0).Trim().ToLowerInvariant();
            var snapshot = await Capture(registration, cancellationToken);
            var actor = kind switch {
                "selected" or "target" => snapshot.SelectedTarget,
                "focus" => snapshot.FocusTarget,
                "run" or "run-target" => snapshot.RunTarget,
                "self" => snapshot.Self,
                _ => throw new ArgumentException("target kind must be selected, focus, run, or self"),
            };
            return call.Return(actor == null ? LuaValue.Nil : ToLua(actor));
        });

        var actors = registration.GetOrCreateModule("actors");
        actors["list"] = new LuaFunction(async (call, cancellationToken) => {
            var snapshot = await Capture(registration, cancellationToken);
            return call.Return(ToLua(snapshot.VisibleActors));
        });
        actors["find"] = new LuaFunction(async (call, cancellationToken) => {
            var query = call.GetArgument<string>(0).Trim();
            if (query.Length == 0)
                throw new ArgumentException("actor query cannot be empty");
            var snapshot = await Capture(registration, cancellationToken);
            return call.Return(ToLookupResult(FindActors(snapshot, query)));
        });
        actors["wait_visible"] = new LuaFunction(async (call, cancellationToken) => {
            var query = RequiredQuery(call.GetArgument<string>(0));
            var timeout = ReadTimeout(call, 1);
            var result = await WaitForSnapshotAsync(
                registration,
                timeout,
                cancellationToken,
                snapshot => {
                    var matches = FindActors(snapshot, query);
                    return matches.Length == 1
                        ? (true, (LuaValue)ToLookupResult(matches))
                        : (false, LuaValue.Nil);
                });
            return call.Return(result);
        });
        actors["wait_lost"] = new LuaFunction(async (call, cancellationToken) => {
            var query = RequiredQuery(call.GetArgument<string>(0));
            var timeout = ReadTimeout(call, 1);
            var result = await WaitForSnapshotAsync(
                registration,
                timeout,
                cancellationToken,
                snapshot => FindActors(snapshot, query).Length == 0
                    ? (true, (LuaValue)new LuaTable { ["status"] = "lost" })
                    : (false, LuaValue.Nil));
            return call.Return(result);
        });
        actors["wait_proximity"] = new LuaFunction(async (call, cancellationToken) => {
            var query = RequiredQuery(call.GetArgument<string>(0));
            var distance = call.GetArgument<double>(1);
            if (!double.IsFinite(distance) || distance < 0 || distance > 1000)
                throw new ArgumentOutOfRangeException(nameof(distance), "proximity distance must be between 0 and 1000 yalms");
            var timeout = ReadTimeout(call, 2);
            var result = await WaitForSnapshotAsync(
                registration,
                timeout,
                cancellationToken,
                snapshot => {
                    var matches = FindActors(snapshot, query);
                    if (snapshot.Self == null || matches.Length != 1)
                        return (false, LuaValue.Nil);
                    var actual = Vector3.Distance(snapshot.Self.Position, matches[0].Position);
                    if (actual > distance)
                        return (false, LuaValue.Nil);
                    var value = ToLookupResult(matches);
                    value["distance"] = actual;
                    return (true, (LuaValue)value);
                });
            return call.Return(result);
        });

        var participants = registration.GetOrCreateModule("participants");
        participants["list"] = new LuaFunction(async (call, cancellationToken) => {
            var snapshot = await Capture(registration, cancellationToken);
            var result = new LuaTable();
            for (var index = 0; index < snapshot.Participants.Count; index++)
                result[index + 1] = ToLua(snapshot.Participants[index]);
            return call.Return(result);
        });

        var player = registration.GetOrCreateModule("player");
        player["profile"] = new LuaFunction(async (call, cancellationToken) => {
            var snapshot = await Capture(registration, cancellationToken);
            return call.Return(snapshot.PlayerProfile == null ? LuaValue.Nil : ToLua(snapshot.PlayerProfile));
        });

        var party = registration.GetOrCreateModule("party");
        party["list"] = new LuaFunction(async (call, cancellationToken) => {
            var snapshot = await Capture(registration, cancellationToken);
            return call.Return(ToLua(snapshot.Party));
        });

        var buddies = registration.GetOrCreateModule("buddies");
        buddies["list"] = new LuaFunction(async (call, cancellationToken) => {
            var snapshot = await Capture(registration, cancellationToken);
            return call.Return(ToLua(snapshot.Buddies));
        });

        var game = registration.GetOrCreateModule("game");
        game["snapshot"] = new LuaFunction(async (call, cancellationToken) => {
            var snapshot = await Capture(registration, cancellationToken);
            var conditions = new LuaTable();
            foreach (var (name, active) in snapshot.Conditions)
                conditions[name] = active;
            return call.Return(new LuaTable {
                ["territory_id"] = (double)snapshot.TerritoryId,
                ["map_id"] = (double)snapshot.MapId,
                ["instance_id"] = (double)snapshot.InstanceId,
                ["logged_in"] = snapshot.IsLoggedIn,
                ["gpose"] = snapshot.IsGPosing,
                ["pvp"] = snapshot.IsPvP,
                ["conditions"] = conditions,
            });
        });
        game["wait_condition"] = new LuaFunction(async (call, cancellationToken) => {
            var name = RequiredQuery(call.GetArgument<string>(0));
            var expected = call.ArgumentCount <= 1 || call.GetArgument<bool>(1);
            var timeout = ReadTimeout(call, 2);
            var result = await WaitForSnapshotAsync(
                registration,
                timeout,
                cancellationToken,
                snapshot => {
                    var condition = snapshot.Conditions.FirstOrDefault(pair =>
                        pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
                    return !string.IsNullOrEmpty(condition.Key) && condition.Value == expected
                        ? (true, (LuaValue)new LuaTable {
                            ["status"] = "matched",
                            ["condition"] = condition.Key,
                            ["active"] = condition.Value,
                        })
                        : (false, LuaValue.Nil);
                });
            return call.Return(result);
        });

        target["wait_changed"] = new LuaFunction(async (call, cancellationToken) => {
            var timeout = ReadTimeout(call, 0);
            var initial = await Capture(registration, cancellationToken);
            var initialId = initial.SelectedTarget?.GameObjectId ?? string.Empty;
            var result = await WaitForSnapshotAsync(
                registration,
                timeout,
                cancellationToken,
                snapshot => {
                    var currentId = snapshot.SelectedTarget?.GameObjectId ?? string.Empty;
                    if (currentId.Equals(initialId, StringComparison.Ordinal))
                        return (false, LuaValue.Nil);
                    return (true, (LuaValue)new LuaTable {
                        ["status"] = "changed",
                        ["target"] = snapshot.SelectedTarget == null ? LuaValue.Nil : ToLua(snapshot.SelectedTarget),
                    });
                });
            return call.Return(result);
        });
    }

    private static LuaFunction SnapshotFunction(
        LuaApiRegistrationContext registration,
        Func<LuaGameSnapshot, LuaActorSnapshot?> select) =>
        new(async (call, cancellationToken) => {
            var actor = select(await Capture(registration, cancellationToken));
            return call.Return(actor == null ? LuaValue.Nil : ToLua(actor));
        });

    private static async Task<LuaGameSnapshot> Capture(
        LuaApiRegistrationContext registration,
        System.Threading.CancellationToken cancellationToken) {
        var capture = registration.Script.CaptureGameSnapshot
            ?? throw new InvalidOperationException("game-state snapshots are unavailable in this Lua host context");
        using var waiter = registration.Quota.EnterWaiter();
        await registration.ExecutionControl.WaitIfPausedAsync(cancellationToken);
        return await capture(cancellationToken);
    }

    private static async Task<LuaGameSnapshot> CaptureWithoutWaiter(
        LuaApiRegistrationContext registration,
        CancellationToken cancellationToken) {
        var capture = registration.Script.CaptureGameSnapshot
            ?? throw new InvalidOperationException("game-state snapshots are unavailable in this Lua host context");
        await registration.ExecutionControl.WaitIfPausedAsync(cancellationToken);
        return await capture(cancellationToken);
    }

    private static async Task<LuaValue> WaitForSnapshotAsync(
        LuaApiRegistrationContext registration,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Func<LuaGameSnapshot, (bool Complete, LuaValue Result)> observe) {
        var pollInterval = TimeSpan.FromMilliseconds(100);
        var attempts = Math.Max(1, (int)Math.Ceiling(timeout.TotalMilliseconds / pollInterval.TotalMilliseconds) + 1);
        using var waiter = registration.Quota.EnterWaiter();
        for (var attempt = 0; attempt < attempts; attempt++) {
            var observation = observe(await CaptureWithoutWaiter(registration, cancellationToken));
            if (observation.Complete)
                return observation.Result;
            if (attempt + 1 < attempts)
                await registration.DelayAsync(pollInterval, cancellationToken);
        }
        return new LuaTable { ["status"] = "timeout" };
    }

    private static LuaActorSnapshot[] FindActors(LuaGameSnapshot snapshot, string query) {
        var exactId = snapshot.VisibleActors
            .Where(actor => actor.GameObjectId.Equals(query, StringComparison.Ordinal)
                || actor.EntityId.ToString(System.Globalization.CultureInfo.InvariantCulture).Equals(query, StringComparison.Ordinal))
            .ToArray();
        return exactId.Length > 0
            ? exactId
            : snapshot.VisibleActors
                .Where(actor => FormationCharacterName.MatchScore(query, actor.Name) >= 0)
                .ToArray();
    }

    private static LuaTable ToLookupResult(IReadOnlyList<LuaActorSnapshot> matches) {
        var result = new LuaTable {
            ["status"] = matches.Count switch { 0 => "missing", 1 => "found", _ => "ambiguous" },
            ["count"] = (double)matches.Count,
        };
        if (matches.Count == 1)
            result["actor"] = ToLua(matches[0]);
        else if (matches.Count > 1)
            result["matches"] = ToLua(matches);
        return result;
    }

    private static string RequiredQuery(string value) {
        value = value?.Trim() ?? string.Empty;
        return value.Length > 0 ? value : throw new ArgumentException("query cannot be empty");
    }

    private static TimeSpan ReadTimeout(LuaFunctionExecutionContext call, int index) {
        var seconds = call.ArgumentCount <= index ? 30.0 : call.GetArgument<double>(index);
        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > 120)
            throw new ArgumentOutOfRangeException(nameof(seconds), "timeout must be between 0 and 120 seconds");
        return TimeSpan.FromSeconds(seconds);
    }

    private static LuaTable ToLua(System.Collections.Generic.IReadOnlyList<LuaActorSnapshot> actors) {
        var result = new LuaTable();
        for (var index = 0; index < actors.Count; index++)
            result[index + 1] = ToLua(actors[index]);
        return result;
    }

    private static LuaTable ToLua(IReadOnlyList<LuaBuddySnapshot> buddies) {
        var result = new LuaTable();
        for (var index = 0; index < buddies.Count; index++) {
            var buddy = buddies[index];
            result[index + 1] = new LuaTable {
                ["kind"] = buddy.Kind,
                ["game_object_id"] = buddy.GameObjectId,
                ["entity_id"] = (double)buddy.EntityId,
                ["data_id"] = (double)buddy.DataId,
                ["hp"] = new LuaTable {
                    ["current"] = (double)buddy.CurrentHp,
                    ["max"] = (double)buddy.MaxHp,
                },
                ["actor"] = buddy.Actor == null ? LuaValue.Nil : ToLua(buddy.Actor),
            };
        }
        return result;
    }

    private static LuaTable ToLua(LuaParticipantSnapshot participant) => new() {
        ["slot"] = (double)participant.Slot,
        ["content_id"] = participant.ContentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["name"] = participant.Name,
        ["is_local"] = participant.IsLocal,
        ["is_visible"] = participant.IsVisible || participant.IsLocal || participant.Actor != null,
        ["authority"] = participant.Authority,
        ["actor"] = participant.Actor == null ? LuaValue.Nil : ToLua(participant.Actor),
    };

    private static LuaTable ToLua(LuaPlayerProfileSnapshot profile) => new() {
        ["content_id"] = profile.ContentId,
        ["character_name"] = profile.CharacterName,
        ["entity_id"] = (double)profile.EntityId,
        ["current_world_id"] = (double)profile.CurrentWorldId,
        ["home_world_id"] = (double)profile.HomeWorldId,
        ["class_job_id"] = (double)profile.ClassJobId,
        ["level"] = (double)profile.Level,
        ["effective_level"] = (double)profile.EffectiveLevel,
        ["race_id"] = (double)profile.RaceId,
        ["tribe_id"] = (double)profile.TribeId,
        ["grand_company_id"] = (double)profile.GrandCompanyId,
        ["sex"] = profile.Sex,
        ["is_loaded"] = profile.IsLoaded,
        ["is_level_synced"] = profile.IsLevelSynced,
        ["is_mentor"] = profile.IsMentor,
        ["is_battle_mentor"] = profile.IsBattleMentor,
        ["is_trade_mentor"] = profile.IsTradeMentor,
        ["is_novice"] = profile.IsNovice,
        ["is_returner"] = profile.IsReturner,
        ["attributes"] = new LuaTable {
            ["strength"] = (double)profile.Strength,
            ["dexterity"] = (double)profile.Dexterity,
            ["vitality"] = (double)profile.Vitality,
            ["intelligence"] = (double)profile.Intelligence,
            ["mind"] = (double)profile.Mind,
            ["piety"] = (double)profile.Piety,
        },
    };

    private static LuaTable ToLua(LuaActorSnapshot actor) => new() {
        ["name"] = actor.Name,
        ["authority"] = actor.Authority,
        ["object_kind"] = actor.ObjectKind,
        ["game_object_id"] = actor.GameObjectId,
        ["entity_id"] = (double)actor.EntityId,
        ["target_game_object_id"] = actor.TargetGameObjectId,
        ["position"] = new LuaTable { ["x"] = actor.Position.X, ["y"] = actor.Position.Y, ["z"] = actor.Position.Z },
        ["rotation"] = actor.Rotation,
        ["is_local"] = actor.IsLocal,
        ["is_targetable"] = actor.IsTargetable,
        ["is_dead"] = actor.IsDead,
        ["class_job_id"] = (double)actor.ClassJobId,
        ["level"] = (double)actor.Level,
        ["hp"] = new LuaTable { ["current"] = (double)actor.CurrentHp, ["max"] = (double)actor.MaxHp },
        ["mp"] = new LuaTable { ["current"] = (double)actor.CurrentMp, ["max"] = (double)actor.MaxMp },
        ["cp"] = new LuaTable { ["current"] = (double)actor.CurrentCp, ["max"] = (double)actor.MaxCp },
        ["gp"] = new LuaTable { ["current"] = (double)actor.CurrentGp, ["max"] = (double)actor.MaxGp },
        ["status_flags"] = actor.StatusFlags,
        ["cast"] = new LuaTable {
            ["is_casting"] = actor.IsCasting,
            ["action_id"] = (double)actor.CastActionId,
            ["target_game_object_id"] = actor.CastTargetGameObjectId,
            ["current_time"] = actor.CurrentCastTime,
            ["total_time"] = actor.TotalCastTime,
        },
    };
}
