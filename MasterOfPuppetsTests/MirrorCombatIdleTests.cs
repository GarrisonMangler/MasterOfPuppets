using Lua;
using Lua.Standard;
using Xunit;

namespace MasterOfPuppetsTests;

public class MirrorCombatIdleTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdleMirrorBoundsWorkEvenWhenActionsNeverConverge(bool mismatched) {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName,
                   "MasterOfPuppets", "Lua", "Scripts", "mirror_target_combat.lua")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName,
            "MasterOfPuppets", "Lua", "Scripts", "mirror_target_combat.lua"));
        using var state = LuaState.Create();
        state.OpenBasicLibrary();
        state.OpenStringLibrary();
        state.OpenTableLibrary();
        state.Environment["mismatched"] = mismatched;
        await state.DoStringAsync("""
            now, iterations, watches, calls = 0, 0, 0, 0
            local self_actor = {
                entity_id=1, game_object_id="1", target_game_object_id="2",
                is_loaded=true, pose_type=1, pose_state=0
            }
            local target_actor = {
                entity_id=2, game_object_id="2", target_game_object_id="0",
                is_loaded=true, pose_type=1, pose_state=0
            }
            if mismatched then
                self_actor.target_game_object_id="0"
                target_actor.is_weapon_drawn=true
                target_actor.pose_type=0
            end
            local function watch(actor, id)
                watches = watches + 1
                return {status="found", actor=actor, watch_id=id}
            end
            local function action()
                calls = calls + 1
                return {ok=false, message="temporarily rejected"}
            end
            mop = {
                get_var=function() return nil end,
                get_run_target=function() return "Target" end,
                capabilities={require=function() end},
                runtime={info=function() return {} end},
                time=function() return now end,
                log=function() end,
                is_running=function() return iterations < 6000 end,
                self={watch=function() return watch(self_actor, "self") end},
                actors={watch=function() return watch(target_actor, "target") end},
                participants={list=function() return {} end},
                actions={target=action, target_via_leader=action, weapon=action,
                    pose=function() error("idle/incorrect pose must not be requested") end},
                events={
                    set_game_sampling=function(enabled) sampling=enabled end,
                    next=function()
                        iterations=iterations+1
                        now=now+0.005
                        return {status="event", name="unrelated", data={}}
                    end
                }
            }
            """ + "\n" + source + """

            assert(sampling == false, "broad sampling must be disabled")
            assert(watches <= 34, "fallback timer bypassed: " .. watches)
            assert(calls <= 9, "rejected actions retried indefinitely: " .. calls)
            if not mismatched then assert(calls == 0, "idle target generated actions") end
            """);
    }
}
