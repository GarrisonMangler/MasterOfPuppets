-- Mirror v2 orchestration foundation.
--
-- Safety invariants:
--   * the launch-time real-PC identity is immutable;
--   * the first local loss or identity mismatch is terminal;
--   * no target reveal, reacquisition, anchor switch, or random fallback exists;
--   * each module owns an independent desired revision and cancels stale retry work;
--   * conductor state supplies authoritative identities for distributed one-shot events.
--
-- Deliberate feature gates:
--   * membership is the dynamic set of recipients acknowledging this running script;
--     ordinary departure removes only that recipient and rejoin is automatic.
--   * temporary emote readiness waits indefinitely while the event remains current;
--     participant departure removes only that participant from the barrier.
--   * fallback eligibility is host-categorized and coordinator-selected; exact
--     users stay exact, one candidate maximizes coverage, and uncovered users skip.
--   * remote headgear control and universal emote reset verification are absent.
--   * reactive dialogue is a nonfunctional boundary only in this release.

local PROTOCOL = 2
local RECONCILE_SECONDS = 0.50
local RETRY_MIN_SECONDS = 0.35
local RETRY_MAX_SECONDS = 2.00
local STATUS_VERIFY_SECONDS = 1.50
local MAX_OBSERVED_EVENTS = 24
local MEMBER_HELLO_SECONDS = 3.0
local MEMBER_SILENCE_SECONDS = 9.0

mop.capabilities.require("mop.runtime", "2.1")
mop.capabilities.require("mop.events", "2.0")
mop.capabilities.require("mop.game-state", "2.3")
mop.capabilities.require("mop.actions", "2.4")

local coordination_available = mop.capabilities.has("mop.coordination", "2.1")
local coordination = coordination_available and mop.messages.info() or nil
local is_conductor = coordination and coordination.conductor or not coordination_available
local runtime_info = mop.runtime.info()
local local_slot = tonumber(runtime_info.slot) or 0
local local_content_id = coordination and tostring(coordination.local_content_id or "0") or "0"

local function text(value)
    if value == nil then return "" end
    return tostring(value)
end

local function number(value)
    return tonumber(value or "0") or 0
end

local function boolean(value)
    return value == true or value == "true" or value == "True" or value == "1"
end

local function object_id(value)
    local id = text(value)
    if id == "" or id == "nil" or id == "3758096384" then return "0" end
    return id
end

local function split(value)
    local result = {}
    for part in string.gmatch(text(value) .. "|", "(.-)|") do
        result[#result + 1] = part
    end
    return result
end

local function result_ok(result)
    return result ~= nil and result.ok == true
end

local function is_permanently_ineligible(result)
    local message = string.lower(text(result and result.message))
    return string.find(message, "not unlocked", 1, true) ~= nil
        or string.find(message, "no local gearset", 1, true) ~= nil
        or string.find(message, "unknown ", 1, true) ~= nil
        or string.find(message, "outside the game's supported range", 1, true) ~= nil
end

local terminal = false
local modules = {}
local self_state = nil
local target_state = nil
local local_is_target = false

local function clear_pending_work()
    for _, module in pairs(modules) do
        module.pending = nil
    end
end

local function stop_terminal(reason, broadcast_target_loss)
    if terminal then return end
    if broadcast_target_loss and coordination_available then
        -- Coordination state is scoped to this authenticated run. A target-loss
        -- stop is global even when first detected by a follower.
        local payload = table.concat({ "2", "X", "target", local_slot }, "|")
        if #payload <= 96 then
            if is_conductor then mop.shared.set("m2.stop", payload) end
            mop.messages.broadcast("mirror.v2.stop", payload, PROTOCOL)
        end
    end
    terminal = true
    clear_pending_work()
    mop.runtime.log("Mirror v2 terminal: " .. reason)
    mop.runtime.stop("Mirror v2: " .. reason)
end

local function send_message(topic, payload, target_slot)
    if not coordination_available or terminal then return false end
    if #payload > 96 then
        stop_terminal("internal coordination payload exceeded 96 bytes")
        return false
    end
    local result
    if target_slot == nil then
        result = mop.messages.broadcast(topic, payload, PROTOCOL)
    else
        result = mop.messages.send(topic, payload, target_slot, PROTOCOL)
    end
    return result_ok(result)
end

local shared_sequences = {}
local function set_shared(key, value)
    if not coordination_available or not is_conductor or terminal then return false end
    if #key > 32 or #value > 96 then
        stop_terminal("internal shared-state payload exceeded its protocol limit")
        return false
    end
    return result_ok(mop.shared.set(key, value))
end

local function changed_shared(key)
    if not coordination_available then return nil end
    local snapshot = mop.shared.get(key)
    if snapshot == nil or snapshot.status ~= "value" then return nil end
    local sequence = number(snapshot.sequence)
    if sequence <= (shared_sequences[key] or 0) then return nil end
    shared_sequences[key] = sequence
    return snapshot.value
end

-- OD-D boundary: deliberately no triggers, chat, lines, cooldowns, assignment,
-- or send behavior. A later bundle module may implement this contract.
local dialogue = {
    enabled = false,
    version = 1,
    on_mirror_observation = function(_) return "deferred" end,
}

local launch = {
    name = text(mop.runtime.variable("mop_run_target_launch_name")),
    game_object_id = object_id(mop.runtime.variable("mop_run_target_launch_game_object_id")),
    entity_id = number(mop.runtime.variable("mop_run_target_launch_entity_id")),
    content_id = text(mop.runtime.variable("mop_run_target_launch_content_id")),
    source = text(mop.runtime.variable("mop_run_target_launch_source")),
}

if launch.name == "" or launch.game_object_id == "0" or launch.entity_id == 0 then
    stop_terminal("validated launch target identity metadata is missing", true)
    return
end

local run_target = mop.target.snapshot("run")
if run_target == nil
    or run_target.object_kind ~= "Pc"
    or not run_target.is_loaded
    or object_id(run_target.game_object_id) ~= launch.game_object_id
    or number(run_target.entity_id) ~= launch.entity_id then
    stop_terminal("launch target is not the same locally observable real PC", true)
    return
end

local function state_from(actor)
    if actor == nil then return nil end
    return {
        name = text(actor.name),
        game_object_id = object_id(actor.game_object_id),
        entity_id = number(actor.entity_id),
        target_game_object_id = object_id(actor.target_game_object_id),
        is_loaded = boolean(actor.is_loaded),
        is_dead = boolean(actor.is_dead),
        is_moving = boolean(actor.is_moving),
        mount_id = number(actor.mount_id),
        emote_id = number(actor.emote_id),
        emote_target_game_object_id = object_id(actor.emote_target_game_object_id),
        is_emote_looping = boolean(actor.is_emote_looping),
        pose_type = number(actor.pose_type),
        pose_state = number(actor.pose_state),
        ornament_id = number(actor.ornament_id),
        facewear_id = number(actor.facewear_id),
        is_headgear_visible = boolean(actor.is_headgear_visible),
        is_visor_toggled = boolean(actor.is_visor_toggled),
        is_weapon_drawn = boolean(actor.is_weapon_drawn),
        online_status_id = number(actor.online_status_id),
        online_status_name = text(actor.online_status_name),
        class_job_id = number(actor.class_job_id),
    }
end

local function target_identity_matches(state)
    return state ~= nil
        and state.is_loaded
        and state.game_object_id == launch.game_object_id
        and state.entity_id == launch.entity_id
end

local target_watch = mop.actors.watch(launch.game_object_id)
if target_watch.status ~= "found" or not target_identity_matches(state_from(target_watch.actor)) then
    stop_terminal("immutable launch target is not locally observable", true)
    return
end
local target_watch_id = target_watch.watch_id
target_state = state_from(target_watch.actor)

local self_watch = mop.self.watch()
if self_watch.status ~= "found" or self_watch.actor == nil then
    stop_terminal("local player is unavailable")
    return
end
local self_watch_id = self_watch.watch_id
self_state = state_from(self_watch.actor)
local_is_target = self_state.entity_id == launch.entity_id

-- The launch participant table is used only for recursion protection and slot/CID
-- transport mapping. It is not treated as an admitted or permanently frozen roster.
local participant_by_slot = {}
for _, participant in ipairs(mop.participants.list()) do
    local slot = number(participant.slot)
    participant_by_slot[slot] = tostring(participant.content_id or "0")
end

-- Only a running recipient's HELLO admits that slot. Silence removes that one
-- recipient; a later HELLO admits it again without disturbing other recipients.
local acknowledged = {}
local member_last_seen = {}
if not local_is_target then
    acknowledged[local_slot] = true
    member_last_seen[local_slot] = mop.runtime.shared_time()
end
local function send_hello()
    if local_is_target then return end
    send_message("mirror.v2.member", "2|H|" .. local_slot .. "|" .. local_content_id)
end
send_hello()

local participant_actor_ids = {}
local function refresh_participant_actor_ids()
    participant_actor_ids = {}
    for _, participant in ipairs(mop.participants.list()) do
        local slot = number(participant.slot)
        participant_by_slot[slot] = tostring(participant.content_id or "0")
        if acknowledged[slot] and participant.actor ~= nil then
            participant_actor_ids[object_id(participant.actor.game_object_id)] = true
            participant_actor_ids[object_id(participant.actor.entity_id)] = true
        end
    end
end
refresh_participant_actor_ids()

local coordinator_epoch = 0
local function next_epoch()
    coordinator_epoch = coordinator_epoch + 1
    return coordinator_epoch
end

local function acknowledged_mask()
    local mask = 0
    for slot, admitted in pairs(acknowledged) do
        if admitted and slot >= 0 and slot < 32 then
            mask = bit32.bor(mask, bit32.lshift(1, slot))
        end
    end
    return mask
end

local function mask_has(mask, slot)
    return bit32.band(mask, bit32.lshift(1, slot)) ~= 0
end

local function mask_remove(mask, slot)
    return bit32.band(mask, bit32.bnot(bit32.lshift(1, slot)))
end

local function mask_include(mask, slot)
    return bit32.bor(mask, bit32.lshift(1, slot))
end

local function publish_state_identity(module_id, revision, value_key)
    if not is_conductor then return end
    local payload = table.concat({ "2", next_epoch(), "S", module_id, revision, value_key }, "|")
    set_shared("m2.s." .. module_id, payload)
end

local function report_outcome(module_id, revision, disposition, attempt)
    local payload = table.concat({ "2", "O", module_id, revision, local_slot, attempt or 0, disposition }, "|")
    send_message("mirror.v2.result", payload)
end

local base64_values = {}
do
    local alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"
    for index = 1, #alphabet do
        base64_values[string.sub(alphabet, index, index)] = index - 1
    end
end

local function decode_eligibility(token)
    local bytes, accumulator, bit_count = {}, 0, 0
    for index = 1, #token do
        local value = base64_values[string.sub(token, index, index)]
        if value == nil then return {} end
        accumulator = accumulator * 64 + value
        bit_count = bit_count + 6
        while bit_count >= 8 do
            bit_count = bit_count - 8
            local divisor = 2 ^ bit_count
            bytes[#bytes + 1] = math.floor(accumulator / divisor) % 256
            accumulator = accumulator % divisor
        end
    end
    return bytes
end

local function eligibility_has(bytes, one_based_index)
    local zero_based = one_based_index - 1
    local byte = bytes[math.floor(zero_based / 8) + 1] or 0
    return bit32.band(byte, bit32.lshift(1, zero_based % 8)) ~= 0
end

local modules_by_id = {}
local fallback_rounds = {}
local function module(name, short_id, options)
    local value = {
        name = name,
        id = short_id,
        revision = 0,
        key = nil,
        authoritative_epoch = 0,
        authoritative_revision = 0,
        authoritative_key = nil,
        pending = nil,
        skip_on_rejection = options and options.skip_on_rejection or false,
        fallback_required = options and options.fallback_required or false,
        fallback_kind = options and options.fallback_kind or nil,
        fallback_execute = options and options.fallback_execute or nil,
        fallback_verify = options and options.fallback_verify or nil,
        fallback_persistent = nil,
        fallback_desired_id = 0,
        event_mask = 0,
    }
    modules[name] = value
    modules_by_id[short_id] = value
    return value
end

local target_module = module("target", "t")
local mount_module = module("mount", "m", {
    fallback_required = true,
    fallback_kind = "mount",
    fallback_execute = function(id) return mop.actions.use_exact("mount", id, "local") end,
    fallback_verify = function(id, state) return state.mount_id == id end,
})
local accessory_module = module("fashion_accessory", "f", {
    fallback_required = true,
    fallback_kind = "fashion_accessory",
    fallback_execute = function(id) return mop.actions.use_exact("fashion_accessory", id, "local") end,
    fallback_verify = function(id, state) return state.ornament_id == id end,
})
local facewear_module = module("facewear", "g", {
    fallback_required = true,
    fallback_kind = "facewear",
    fallback_execute = function(id) return mop.actions.use_exact("facewear", id, "local") end,
    fallback_verify = function(id, state) return state.facewear_id == id end,
})
local job_module = module("job", "j", {
    fallback_required = true,
    fallback_kind = "job",
    fallback_execute = function(id) return mop.actions.job(id) end,
    fallback_verify = function(id, state) return state.class_job_id == id end,
})
local status_module = module("online_status", "s", { skip_on_rejection = true })
local weapon_module = module("weapon", "w")
local visor_module = module("visor", "v")
local pose_module = module("pose", "p")
local combat_module = module("combat", "a")
local emote_module = module("emote", "e", { fallback_required = true, fallback_kind = "emote" })
local emote_barrier = nil
local reconcile_desired_from_target

local function fallback_round_key(owner, revision)
    return owner.id .. ":" .. revision
end

local function begin_fallback_round(owner, mask)
    owner.event_mask = mask
    if not is_conductor then return end
    fallback_rounds[fallback_round_key(owner, owner.revision)] = {
        owner = owner,
        revision = owner.revision,
        mask = mask,
        complete = {},
        exact = {},
        skipped = {},
        candidate_masks = {},
        finalized = false,
    }
end

local function request_fallback(owner)
    if owner.fallback_kind == nil then
        if owner == emote_module and emote_barrier ~= nil then
            send_message("mirror.v2.emote", table.concat({
                "2", "O", emote_barrier.epoch, local_slot, "skip",
            }, "|"))
        else
            report_outcome(owner.id, owner.revision, "skip", 0)
        end
        return
    end
    local desired_id = owner.fallback_desired_id
    local candidates = mop.actions.fallback_candidates(
        owner.fallback_kind, desired_id, owner.fallback_persistent)
    if candidates == nil or candidates.ok ~= true then
        if owner == emote_module and emote_barrier ~= nil then
            send_message("mirror.v2.emote", table.concat({
                "2", "O", emote_barrier.epoch, local_slot, "skip",
            }, "|"))
        else
            report_outcome(owner.id, owner.revision, "skip", 0)
        end
        return
    end
    local payload = table.concat({
        "2", "C", owner.id, owner.revision, local_slot,
        text(candidates.universe_signature), text(candidates.eligibility),
    }, "|")
    if #payload > 96 then
        if owner == emote_module and emote_barrier ~= nil then
            send_message("mirror.v2.emote", table.concat({
                "2", "O", emote_barrier.epoch, local_slot, "skip",
            }, "|"))
        else
            report_outcome(owner.id, owner.revision, "skip", 0)
        end
        return
    end
    send_message("mirror.v2.fallback", payload)
end

local function set_desired(owner, key, execute, verify)
    local next_revision
    if coordination_available and not is_conductor then
        if owner.authoritative_key ~= key or owner.authoritative_revision <= 0 then return false end
        next_revision = owner.authoritative_revision
        if owner.key == key and owner.revision == next_revision then return false end
    else
        if owner.key == key then return false end
        next_revision = owner.revision + 1
    end
    owner.revision = next_revision
    owner.key = key
    owner.fallback_desired_id = tonumber(key) or 0
    owner.pending = nil -- cancels every retry belonging to the superseded revision
    begin_fallback_round(owner, acknowledged_mask())
    publish_state_identity(owner.id, owner.revision, key)
    if not local_is_target then
        owner.pending = {
            revision = owner.revision,
            execute = execute,
            verify = verify,
            attempts = 0,
            next_try = mop.runtime.shared_time(),
            requested_at = nil,
        }
    end
    return true
end

local function backoff(attempts)
    local delay = RETRY_MIN_SECONDS * (2 ^ math.min(attempts - 1, 3))
    return math.min(delay, RETRY_MAX_SECONDS)
end

local function tick_module(owner, now)
    local pending = owner.pending
    if pending == nil or pending.revision ~= owner.revision or terminal then return end

    if self_state ~= nil and pending.verify(self_state) then
        owner.pending = nil
        report_outcome(owner.id, owner.revision, "ok", pending.attempts)
        return
    end

    if owner.skip_on_rejection and pending.requested_at ~= nil
        and now - pending.requested_at >= STATUS_VERIFY_SECONDS then
        owner.pending = nil
        report_outcome(owner.id, owner.revision, "skip", pending.attempts)
        return
    end

    if now < pending.next_try then return end
    pending.attempts = pending.attempts + 1
    local result = pending.execute()
    if pending.revision ~= owner.revision or terminal then return end
    if result_ok(result) then
        pending.requested_at = now
        pending.next_try = owner.skip_on_rejection
            and now + STATUS_VERIFY_SECONDS
            or now + backoff(pending.attempts)
        return
    end

    if owner.skip_on_rejection then
        owner.pending = nil
        report_outcome(owner.id, owner.revision, "skip", pending.attempts)
    elseif owner.fallback_required and is_permanently_ineligible(result) then
        owner.pending = nil
        request_fallback(owner)
        report_outcome(owner.id, owner.revision, "ineligible", pending.attempts)
    else
        pending.next_try = now + backoff(pending.attempts)
    end
end

local function try_finalize_fallback(round)
    if not is_conductor or round == nil or round.finalized then return end
    for slot = 0, 31 do
        if mask_has(round.mask, slot) and not round.complete[slot] then return end
    end
    local selected_id, selected_mask, selected_count = 0, 0, -1
    for candidate_id, candidate_mask in pairs(round.candidate_masks) do
        local count = 0
        for slot = 0, 31 do
            if mask_has(candidate_mask, slot) then count = count + 1 end
        end
        local numeric_id = tonumber(candidate_id) or 0
        if count > selected_count or (count == selected_count and numeric_id < selected_id) then
            selected_id, selected_mask, selected_count = numeric_id, candidate_mask, count
        end
    end
    local unresolved = 0
    for slot = 0, 31 do
        if mask_has(round.mask, slot)
            and not round.exact[slot]
            and not mask_has(selected_mask, slot) then
            unresolved = mask_include(unresolved, slot)
        end
    end
    round.finalized = true
    set_shared("m2.fallback", table.concat({
        "2", "F", round.owner.id, round.revision,
        selected_id, selected_mask, unresolved,
    }, "|"))
end

local function record_fallback_outcome(fields, sender_content_id)
    if not is_conductor or fields[1] ~= "2" or fields[2] ~= "O" then return end
    local owner = modules_by_id[fields[3]]
    local revision = number(fields[4])
    local slot = number(fields[5])
    local disposition = fields[7]
    if owner == nil or owner.revision ~= revision
        or not mask_has(owner.event_mask, slot)
        or participant_by_slot[slot] ~= text(sender_content_id) then return end
    local round = fallback_rounds[fallback_round_key(owner, revision)]
    if round == nil then return end
    if disposition == "ok" or disposition == "fallback" then
        round.exact[slot] = true
        round.complete[slot] = true
    elseif disposition == "skip" then
        round.skipped[slot] = true
        round.complete[slot] = true
    end
    try_finalize_fallback(round)
end

local function record_fallback_candidates(fields, sender_content_id)
    if not is_conductor or fields[1] ~= "2" or fields[2] ~= "C" then return end
    local owner = modules_by_id[fields[3]]
    local revision = number(fields[4])
    local slot = number(fields[5])
    if owner == nil or owner.revision ~= revision
        or not mask_has(owner.event_mask, slot)
        or participant_by_slot[slot] ~= text(sender_content_id) then return end
    local round = fallback_rounds[fallback_round_key(owner, revision)]
    if round == nil then return end
    local local_candidates = mop.actions.fallback_candidates(
        owner.fallback_kind, owner.fallback_desired_id, owner.fallback_persistent)
    if local_candidates == nil or local_candidates.ok ~= true
        or text(local_candidates.universe_signature) ~= fields[6] then
        round.skipped[slot] = true
        round.complete[slot] = true
        try_finalize_fallback(round)
        return
    end
    local bytes = decode_eligibility(fields[7] or "")
    for index, candidate_id in ipairs(local_candidates.universe or {}) do
        if eligibility_has(bytes, index) then
            local key = tostring(number(candidate_id))
            round.candidate_masks[key] = mask_include(round.candidate_masks[key] or 0, slot)
        end
    end
    round.complete[slot] = true
    try_finalize_fallback(round)
end

local function apply_fallback_selection(payload)
    local fields = split(payload)
    if fields[1] ~= "2" or fields[2] ~= "F" then return end
    local owner = modules_by_id[fields[3]]
    local revision = number(fields[4])
    local candidate_id = number(fields[5])
    local fallback_mask = number(fields[6])
    local unresolved_mask = number(fields[7])
    if owner == nil or owner.revision ~= revision or local_is_target then return end
    if mask_has(unresolved_mask, local_slot) then
        owner.pending = nil
        if owner == emote_module and emote_barrier ~= nil then
            send_message("mirror.v2.emote", table.concat({
                "2", "O", emote_barrier.epoch, local_slot, "skip",
            }, "|"))
        else
            report_outcome(owner.id, owner.revision, "skip", 0)
        end
        return
    end
    if candidate_id <= 0 or not mask_has(fallback_mask, local_slot)
        or owner.fallback_execute == nil or owner.fallback_verify == nil then return end
    local result = owner.fallback_execute(candidate_id)
    if not result_ok(result) then
        if owner == emote_module and emote_barrier ~= nil then
            send_message("mirror.v2.emote", table.concat({
                "2", "O", emote_barrier.epoch, local_slot, "fail",
            }, "|"))
        else
            report_outcome(owner.id, owner.revision, "skip", 1)
        end
        return
    end
    owner.pending = {
        revision = owner.revision,
        attempts = 1,
        next_try = mop.runtime.shared_time() + RETRY_MIN_SECONDS,
        execute = function() return owner.fallback_execute(candidate_id) end,
        verify = function(state) return owner.fallback_verify(candidate_id, state) end,
    }
end

local function valid_external_player(id)
    id = object_id(id)
    if id == "0" or id == launch.game_object_id or participant_actor_ids[id] then return nil end
    local lookup = mop.actors.find(id)
    if lookup == nil or lookup.status ~= "found" or lookup.actor == nil then return nil end
    local actor = lookup.actor
    if actor.object_kind ~= "Pc" or not actor.is_loaded then return nil end
    return object_id(actor.game_object_id)
end

local last_source_target_id = nil
local resolved_mirror_target = launch.game_object_id
reconcile_desired_from_target = function()
    if target_state == nil or terminal then return end

    local source_target_id = target_state.target_game_object_id
    if source_target_id ~= last_source_target_id then
        last_source_target_id = source_target_id
        resolved_mirror_target = valid_external_player(source_target_id) or launch.game_object_id
    end
    local mirrored_target = resolved_mirror_target
    set_desired(target_module, mirrored_target,
        function() return mop.actions.target(mirrored_target) end,
        function(state) return state.target_game_object_id == mirrored_target end)

    local mount_id = target_state.mount_id
    set_desired(mount_module, tostring(mount_id),
        function()
            if mount_id == 0 then return mop.actions.use_exact("general_action", 23, "local") end
            return mop.actions.use_exact("mount", mount_id, "local")
        end,
        function(state) return state.mount_id == mount_id end)

    local accessory_id = target_state.ornament_id
    set_desired(accessory_module, tostring(accessory_id),
        function()
            if accessory_id == 0 then return mop.actions.stop_cosmetic("fashion_accessory") end
            return mop.actions.use_exact("fashion_accessory", accessory_id, "local")
        end,
        function(state) return state.ornament_id == accessory_id end)

    local facewear_id = target_state.facewear_id
    set_desired(facewear_module, tostring(facewear_id),
        function()
            if facewear_id == 0 then return mop.actions.stop_cosmetic("facewear") end
            return mop.actions.use_exact("facewear", facewear_id, "local")
        end,
        function(state) return state.facewear_id == facewear_id end)

    local job_id = target_state.class_job_id
    set_desired(job_module, tostring(job_id),
        function() return mop.actions.job(job_id) end,
        function(state) return state.class_job_id == job_id end)

    local status_id = target_state.online_status_id
    local status_name = target_state.online_status_name
    set_desired(status_module, tostring(status_id),
        function() return mop.actions.online_status(status_id, status_name) end,
        function(state) return state.online_status_id == status_id end)

    local drawn = target_state.is_weapon_drawn
    set_desired(weapon_module, drawn and "1" or "0",
        function() return mop.actions.weapon(drawn) end,
        function(state) return state.is_weapon_drawn == drawn end)

    local visor_enabled = target_state.is_visor_toggled
    set_desired(visor_module, visor_enabled and "1" or "0",
        function() return mop.actions.visor(visor_enabled) end,
        function(state) return state.is_visor_toggled == visor_enabled end)

    local pose_type = target_state.pose_type
    local pose_state = target_state.pose_state
    set_desired(pose_module, pose_type .. ":" .. pose_state,
        function() return mop.actions.pose(pose_type, pose_state) end,
        function(state)
            return state.pose_type == pose_type and state.pose_state == pose_state
        end)
end

local observed_combat = {}
local observed_order = {}
local last_combat_sequence = ""

local function remember_combat(event_data)
    local sequence = text(event_data.global_sequence)
    if sequence == "" then return end
    observed_combat[sequence] = event_data
    observed_order[#observed_order + 1] = sequence
    if #observed_order > MAX_OBSERVED_EVENTS then
        observed_combat[table.remove(observed_order, 1)] = nil
    end
end

local function action_kind(action_type)
    if action_type == 1 then return "action" end
    if action_type == 11 then return "pet_action" end
    if action_type == 14 then return "pvp_action" end
    return nil
end

local function execute_combat(observation)
    local kind = action_kind(number(observation.action_type))
    local id = number(observation.action_id)
    if kind == nil or id <= 0 then return { ok = false, status = "unsupported" } end
    local animation_target = object_id(observation.animation_target_id)
    if boolean(observation.is_ground_targeted) then
        local ground_target = animation_target ~= "0" and animation_target
            or target_state.target_game_object_id ~= "0" and target_state.target_game_object_id
            or launch.game_object_id
        local x, y, z = tonumber(observation.target_x), tonumber(observation.target_y), tonumber(observation.target_z)
        if x ~= nil and y ~= nil and z ~= nil then
            return mop.actions.use_ground_on(kind, id, ground_target, x, y, z)
        end
        return mop.actions.use_ground_on(kind, id, ground_target)
    end
    if animation_target ~= "0" and animation_target ~= launch.game_object_id then
        return mop.actions.use_exact_on(kind, id, animation_target)
    end
    return mop.actions.use_exact(kind, id, "local")
end

local pending_combat_stamp = nil
local function publish_combat(observation)
    combat_module.revision = combat_module.revision + 1
    combat_module.key = text(observation.global_sequence)
    combat_module.pending = nil -- newest combat event supersedes unverified older retry
    local epoch = next_epoch()
    local payload = table.concat({
        "2", epoch, combat_module.revision, observation.global_sequence,
        observation.action_type, observation.action_id,
    }, "|")
    if set_shared("m2.action", payload) then
        pending_combat_stamp = payload
    end
end

local function accept_combat_stamp(payload)
    local fields = split(payload)
    if fields[1] ~= "2" or #fields < 6 then return end
    local sequence = fields[4]
    if sequence == "" or sequence == last_combat_sequence then return end
    local observation = observed_combat[sequence]
    if observation == nil then return end -- missed local source observation: never guess targeting/ground data
    if text(observation.action_type) ~= fields[5] or text(observation.action_id) ~= fields[6] then return end
    last_combat_sequence = sequence
    combat_module.revision = number(fields[3])
    combat_module.key = sequence
    combat_module.pending = nil
    if local_is_target then return end
    combat_module.pending = {
        revision = combat_module.revision,
        action_id = number(observation.action_id),
        attempts = 0,
        next_try = mop.runtime.shared_time(),
        execute = function() return execute_combat(observation) end,
        verify = function(_) return false end,
    }
end

-- Emote barrier protocol. Event membership is the acknowledged-recipient snapshot
-- at event creation, not a configured CidsGroup. Missing readiness never expires.
local EMOTE_RESET_VERIFIED = false
local last_emote_go_epoch = -1

local function emote_payload(barrier)
    return table.concat({
        "2", barrier.epoch, barrier.revision, barrier.id,
        barrier.persistent and 1 or 0, barrier.target_id, barrier.mask,
        barrier.phase,
    }, "|")
end

local function publish_emote_phase(barrier)
    if is_conductor then set_shared("m2.emote", emote_payload(barrier)) end
end

local function evaluate_emote_barrier()
    if not is_conductor or emote_barrier == nil then return end
    if emote_barrier.phase == "P" then
        local all_ready = true
        for candidate = 0, 31 do
            if mask_has(emote_barrier.mask, candidate) and not emote_barrier.ready[candidate] then
                all_ready = false
            end
        end
        if all_ready then
            emote_barrier.phase = "G"
            publish_emote_phase(emote_barrier)
        end
    elseif emote_barrier.phase == "G" then
        local complete, failed = true, false
        for candidate = 0, 31 do
            if mask_has(emote_barrier.mask, candidate) then
                if emote_barrier.outcomes[candidate] == nil then complete = false end
                if emote_barrier.outcomes[candidate] == "fail" then failed = true end
            end
        end
        if complete then
            emote_barrier.phase = failed and "F" or "V"
            publish_emote_phase(emote_barrier)
        end
    end
end

local function start_emote_barrier(id, persistent, target_id)
    if not is_conductor then return end
    emote_module.revision = emote_module.revision + 1
    emote_module.key = tostring(id) .. ":" .. (persistent and "1" or "0")
    emote_module.fallback_desired_id = id
    emote_module.pending = nil
    emote_barrier = {
        epoch = next_epoch(),
        revision = emote_module.revision,
        id = id,
        persistent = persistent,
        target_id = object_id(target_id),
        mask = acknowledged_mask(),
        phase = "P", -- PREPARE
        ready = {},
        outcomes = {},
    }
    emote_module.fallback_persistent = persistent
    emote_module.fallback_execute = function(fallback_id)
        if emote_barrier.target_id ~= "0" and emote_barrier.target_id ~= launch.game_object_id then
            return mop.actions.use_exact_on(
                "emote", fallback_id, emote_barrier.target_id, emote_barrier.persistent)
        end
        return mop.actions.use_exact("emote", fallback_id, "local", emote_barrier.persistent)
    end
    emote_module.fallback_verify = function(fallback_id, state)
        return state.emote_id == fallback_id
            and (not emote_barrier.persistent or state.is_emote_looping)
    end
    begin_fallback_round(emote_module, emote_barrier.mask)
    if not coordination_available then
        local result = mop.actions.use_exact("emote", id, "local", persistent)
        if result_ok(result) then
            emote_module.pending = {
                revision = emote_module.revision, attempts = 1, next_try = math.huge,
                execute = function() return result end,
                verify = function(state)
                    return state.emote_id == id and (not persistent or state.is_emote_looping)
                end,
            }
        elseif is_permanently_ineligible(result) then
            request_fallback(emote_module)
        end
        return
    end
    publish_emote_phase(emote_barrier)
end

local function accept_emote_phase(payload)
    local fields = split(payload)
    if fields[1] ~= "2" or #fields < 8 then return end
    local epoch = number(fields[2])
    if emote_barrier ~= nil and epoch < emote_barrier.epoch then return end
    emote_barrier = emote_barrier or {}
    emote_barrier.epoch = epoch
    emote_barrier.revision = number(fields[3])
    emote_barrier.id = number(fields[4])
    emote_barrier.persistent = fields[5] == "1"
    emote_barrier.target_id = object_id(fields[6])
    emote_barrier.mask = number(fields[7])
    emote_barrier.phase = fields[8]
    emote_barrier.ready = emote_barrier.ready or {}
    emote_barrier.outcomes = emote_barrier.outcomes or {}
    emote_module.revision = emote_barrier.revision
    emote_module.key = tostring(emote_barrier.id) .. ":" .. fields[5]
    local duplicate_go = emote_barrier.phase == "G" and last_emote_go_epoch == epoch
    if not duplicate_go then emote_module.pending = nil end

    if not mask_has(emote_barrier.mask, local_slot) or local_is_target then return end
    if emote_barrier.phase == "P" then
        -- Readiness is deliberately conservative and contains no timeout policy.
        if self_state ~= nil and self_state.is_loaded and not self_state.is_dead then
            send_message("mirror.v2.emote", table.concat({ "2", "R", epoch, local_slot }, "|"))
        end
    elseif emote_barrier.phase == "G" then
        if duplicate_go then return end
        last_emote_go_epoch = epoch
        local id = emote_barrier.id
        local persistent = emote_barrier.persistent
        local target_id = emote_barrier.target_id
        local result
        if target_id ~= "0" and target_id ~= launch.game_object_id then
            result = mop.actions.use_exact_on("emote", id, target_id, persistent)
        else
            result = mop.actions.use_exact("emote", id, "local", persistent)
        end
        if result_ok(result) then
            emote_module.pending = {
                revision = emote_module.revision,
                attempts = 1,
                next_try = math.huge,
                execute = function() return result end,
                verify = function(state)
                    return state.emote_id == id and (not persistent or state.is_emote_looping)
                end,
            }
        elseif is_permanently_ineligible(result) then
            request_fallback(emote_module)
            report_outcome(emote_module.id, emote_module.revision, "ineligible", 1)
        else
            send_message("mirror.v2.emote", table.concat({ "2", "O", epoch, local_slot, "fail" }, "|"))
        end
    elseif emote_barrier.phase == "F" then
        -- Whole-roster retry is required, but reset is gated until
        -- MIR-EMOTE-RESET proves universal reset and entry verification.
        emote_module.pending = nil
        if EMOTE_RESET_VERIFIED then
            mop.runtime.log("Mirror v2 emote reset integration is enabled")
        end
    elseif emote_barrier.phase == "S" then
        emote_module.pending = nil
    end
end

local function conductor_emote_message(fields, sender_content_id)
    if not is_conductor or emote_barrier == nil then return end
    if fields[1] ~= "2" or number(fields[3]) ~= emote_barrier.epoch then return end
    local slot = number(fields[4])
    if not mask_has(emote_barrier.mask, slot)
        or participant_by_slot[slot] ~= text(sender_content_id) then return end
    if fields[2] == "R" and emote_barrier.phase == "P" then
        emote_barrier.ready[slot] = true
        evaluate_emote_barrier()
    elseif fields[2] == "O" and emote_barrier.phase == "G" then
        emote_barrier.outcomes[slot] = fields[5]
        local round = fallback_rounds[fallback_round_key(emote_module, emote_module.revision)]
        if round ~= nil then
            round.complete[slot] = true
            if fields[5] == "ok" then round.exact[slot] = true end
            if fields[5] == "skip" then round.skipped[slot] = true end
            try_finalize_fallback(round)
        end
        evaluate_emote_barrier()
    end
end

local function admit_member(slot)
    local newly_admitted = not acknowledged[slot]
    acknowledged[slot] = true
    member_last_seen[slot] = mop.runtime.shared_time()
    last_source_target_id = nil
    if is_conductor and newly_admitted then
        -- Re-emit every current continuous identity so a restarted/late member
        -- can bind its local observation to the existing authoritative revision.
        for _, owner in pairs(modules) do
            if owner ~= combat_module and owner ~= emote_module and owner.key ~= nil then
                publish_state_identity(owner.id, owner.revision, owner.key)
            end
        end
    end
    -- Admission affects continuous state reconciliation and only future event
    -- snapshots. It never mutates the membership of an in-flight event.
end

local function sweep_members(now)
    if not is_conductor then return end
    local changed = false
    for slot, admitted in pairs(acknowledged) do
        if admitted and slot ~= local_slot
            and now - (member_last_seen[slot] or 0) > MEMBER_SILENCE_SECONDS then
            acknowledged[slot] = nil
            member_last_seen[slot] = nil
            last_source_target_id = nil
            changed = true
            if emote_barrier ~= nil and mask_has(emote_barrier.mask, slot) then
                -- Ordinary recipient departure removes only that recipient. It
                -- never cancels the event or stops remaining recipients.
                emote_barrier.mask = mask_remove(emote_barrier.mask, slot)
                emote_barrier.ready[slot] = nil
                emote_barrier.outcomes[slot] = nil
            end
            for _, owner in pairs(modules) do
                if mask_has(owner.event_mask, slot) then
                    owner.event_mask = mask_remove(owner.event_mask, slot)
                    local round = fallback_rounds[fallback_round_key(owner, owner.revision)]
                    if round ~= nil then
                        round.mask = mask_remove(round.mask, slot)
                        round.complete[slot] = nil
                        round.exact[slot] = nil
                        round.skipped[slot] = nil
                        for candidate_id, candidate_mask in pairs(round.candidate_masks) do
                            round.candidate_masks[candidate_id] = mask_remove(candidate_mask, slot)
                        end
                        try_finalize_fallback(round)
                    end
                end
            end
        end
    end
    if changed and emote_barrier ~= nil then
        publish_emote_phase(emote_barrier)
        evaluate_emote_barrier()
    end
end

local function supersede_emote_if_stopped(previous, current)
    if not is_conductor or emote_barrier == nil or previous == nil then return end
    if previous.is_emote_looping and not current.is_emote_looping
        and emote_barrier.phase ~= "S" then
        emote_module.revision = emote_module.revision + 1
        emote_module.key = "0"
        emote_module.pending = nil
        emote_barrier = {
            epoch = next_epoch(), revision = emote_module.revision, id = 0,
            persistent = false, target_id = "0", mask = acknowledged_mask(),
            phase = "S", ready = {}, outcomes = {},
        }
        publish_emote_phase(emote_barrier)
    end
end

local function process_coordination()
    if not coordination_available then return end
    while true do
        local message = mop.messages.poll()
        if message == nil then break end
        if number(message.schema_version) == PROTOCOL then
            local fields = split(message.payload)
            if message.topic == "mirror.v2.stop"
                and fields[1] == "2" and fields[2] == "X"
                and fields[3] == "target" and #fields == 4
                and number(message.target_content_id) == 0
                and number(fields[4]) >= 0 and number(fields[4]) < 32
                and participant_by_slot[number(fields[4])] == text(message.sender_content_id) then
                if is_conductor then set_shared("m2.stop", message.payload) end
                stop_terminal("authoritative global target-loss stop received", false)
                return
            elseif message.topic == "mirror.v2.member"
                and fields[1] == "2" and fields[2] == "H" then
                local slot = number(fields[3])
                local cid = fields[4]
                if slot >= 0 and slot < 32
                    and participant_by_slot[slot] ~= nil
                    and participant_by_slot[slot] == cid
                    and text(message.sender_content_id) == cid then
                    admit_member(slot)
                end
            elseif message.topic == "mirror.v2.emote" then
                conductor_emote_message(fields, message.sender_content_id)
            elseif message.topic == "mirror.v2.result" then
                record_fallback_outcome(fields, message.sender_content_id)
            elseif message.topic == "mirror.v2.fallback" and is_conductor then
                record_fallback_candidates(fields, message.sender_content_id)
            end
        end
    end

    local accepted_state_identity = false
    for _, owner in pairs(modules) do
        if owner ~= combat_module and owner ~= emote_module then
            local state_value = changed_shared("m2.s." .. owner.id)
            if state_value ~= nil then
                local fields = split(state_value)
                local epoch = number(fields[2])
                local revision = number(fields[5])
                if #fields == 6 and fields[1] == "2" and fields[3] == "S"
                    and fields[4] == owner.id and epoch > owner.authoritative_epoch
                    and revision > 0 then
                    owner.authoritative_epoch = epoch
                    owner.authoritative_revision = revision
                    owner.authoritative_key = fields[6]
                    if not is_conductor
                        and (owner.revision ~= revision or owner.key ~= fields[6]) then
                        owner.key = nil
                        owner.pending = nil
                        accepted_state_identity = true
                    end
                end
            end
        end
    end
    if accepted_state_identity then reconcile_desired_from_target() end

    local action_value = changed_shared("m2.action")
    if action_value ~= nil then
        pending_combat_stamp = action_value
        accept_combat_stamp(action_value)
    elseif pending_combat_stamp ~= nil then
        accept_combat_stamp(pending_combat_stamp)
    end
    local emote_value = changed_shared("m2.emote")
    if emote_value ~= nil then accept_emote_phase(emote_value) end
    local fallback_value = changed_shared("m2.fallback")
    if fallback_value ~= nil then apply_fallback_selection(fallback_value) end
    local stop_value = changed_shared("m2.stop")
    if stop_value ~= nil then
        local fields = split(stop_value)
        local slot = number(fields[4])
        if #fields == 4 and fields[1] == "2" and fields[2] == "X"
            and fields[3] == "target" and slot >= 0 and slot < 32
            and participant_by_slot[slot] ~= nil then
            stop_terminal("authoritative persisted target-loss stop received", false)
        end
    end
end

local function process_event(event)
    if event == nil or event.status ~= "event" then return end
    local data = event.data or {}
    if event.name == "actor.lost" and data.watch_id == target_watch_id then
        stop_terminal("immutable target left local observation", true)
        return
    end
    if event.name == "actor.found" and data.watch_id == target_watch_id then
        -- Reappearance is not reacquisition. If a loss was delivered first the run
        -- is already terminal; otherwise identity still has to match exactly.
        local found = state_from(data)
        if not target_identity_matches(found) then
            stop_terminal("immutable target identity changed", true)
        end
        return
    end
    if event.name == "actor.state" and data.watch_id == target_watch_id then
        local previous = target_state
        local current = state_from(data)
        if not target_identity_matches(current) then
            stop_terminal("immutable target identity changed or became invalid", true)
            return
        end
        target_state = current
        supersede_emote_if_stopped(previous, current)
        reconcile_desired_from_target()
        dialogue.on_mirror_observation({ kind = "state", target = current })
        return
    end
    if (event.name == "actor.state" or event.name == "actor.found")
        and data.watch_id == self_watch_id then
        self_state = state_from(data)
        return
    end
    if event.name == "actor.lost" and data.watch_id == self_watch_id then
        stop_terminal("local player left observation")
        return
    end
    if event.name == "combat.action" then
        local source = number(data.source_entity_id)
        if source == launch.entity_id then
            remember_combat(data)
            if is_conductor then publish_combat(data) end
            if not coordination_available then
                -- Local-only coordination has no shared transport requirement.
                combat_module.revision = combat_module.revision + 1
                local payload = table.concat({ "2", next_epoch(), combat_module.revision,
                    data.global_sequence, data.action_type, data.action_id }, "|")
                accept_combat_stamp(payload)
            elseif pending_combat_stamp ~= nil then
                accept_combat_stamp(pending_combat_stamp)
            end
        elseif self_state ~= nil and source == self_state.entity_id
            and combat_module.pending ~= nil
            and number(data.action_id) == combat_module.pending.action_id then
            combat_module.pending = nil
            report_outcome(combat_module.id, combat_module.revision, "ok", 1)
        end
        return
    end
    if event.name == "emote.played" then
        local source = number(data.source_entity_id)
        if source == launch.entity_id and number(data.emote_id) > 0 then
            start_emote_barrier(number(data.emote_id), boolean(data.is_persistent), data.target_id)
        elseif self_state ~= nil and source == self_state.entity_id
            and emote_barrier ~= nil and emote_barrier.phase == "G"
            and number(data.emote_id) == emote_barrier.id then
            emote_module.pending = nil
            send_message("mirror.v2.emote", table.concat({
                "2", "O", emote_barrier.epoch, local_slot, "ok",
            }, "|"))
        end
    end
end

reconcile_desired_from_target()
local initial_stats = mop.events.stats()
local known_dropped = number(initial_stats.dropped)
local next_reconcile = mop.runtime.shared_time() + RECONCILE_SECONDS
local next_hello = mop.runtime.shared_time() + MEMBER_HELLO_SECONDS

mop.runtime.log("Mirror v2 started for immutable local target " .. launch.name
    .. " (" .. launch.game_object_id .. ")")

while not mop.runtime.cancelled() and not terminal do
    local event = mop.events.next(nil, 0.10)
    process_event(event)
    if terminal then break end
    process_coordination()

    local now = mop.runtime.shared_time()
    if now >= next_reconcile then
        local stats = mop.events.stats()
        local dropped = number(stats.dropped)
        if dropped > known_dropped then
            -- A lost/identity event may have been evicted. Conservatively stop
            -- instead of allowing a later watch reacquisition to hide the loss.
            stop_terminal("event pressure made target continuity unverifiable", true)
            break
        end
        known_dropped = dropped

        local refreshed_target = mop.actors.watch(launch.game_object_id)
        local refreshed_state = state_from(refreshed_target.actor)
        if refreshed_target.status ~= "found" or not target_identity_matches(refreshed_state) then
            stop_terminal("immutable target failed bounded local reconciliation", true)
            break
        end
        target_state = refreshed_state

        local refreshed_self = mop.self.watch()
        if refreshed_self.status ~= "found" or refreshed_self.actor == nil then
            stop_terminal("local player failed bounded reconciliation")
            break
        end
        self_state = state_from(refreshed_self.actor)
        refresh_participant_actor_ids()
        reconcile_desired_from_target()
        next_reconcile = now + RECONCILE_SECONDS
    end

    if coordination_available and now >= next_hello then
        send_hello()
        if not local_is_target then member_last_seen[local_slot] = now end
        next_hello = now + MEMBER_HELLO_SECONDS
    end
    sweep_members(now)

    for _, owner in pairs(modules) do
        if owner ~= emote_module then tick_module(owner, now) end
    end
    if emote_module.pending ~= nil and self_state ~= nil
        and emote_module.pending.verify(self_state) then
        emote_module.pending = nil
        if emote_barrier ~= nil then
            send_message("mirror.v2.emote", table.concat({
                "2", "O", emote_barrier.epoch, local_slot, "ok",
            }, "|"))
        end
    end
end

clear_pending_work()
emote_barrier = nil
