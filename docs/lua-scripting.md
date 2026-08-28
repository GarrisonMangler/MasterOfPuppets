# Lua Scripting

> **Status:** This document describes the currently implemented Lua system. The
> broader, versioned architecture and coverage requirements are defined in the
> [Lua Automation V2 Long-Term Goal](architecture/LUA_AUTOMATION_V2_GOAL.md).

## Overview

Master of Puppets now has two separate automation systems:

| System | Best For | Authoring Model |
| :--- | :--- | :--- |
| **Macros** | Ordered game commands, existing group assignments, macro variables, and saved formations. | A list of commands interpreted by the macro engine. |
| **Lua Scripts** | Stateful behavior, loops, calculations, conversations, dynamic targets, and movement logic that reacts while it runs. | Lua code executed by the Lua runtime through the explicit `mop` API. |

Lua does not execute a macro or load a saved formation unless an API is deliberately added to do so. The current actor-follow and trajectory features run independently from the macro queue and saved formation definitions. They share only the plugin's lower-level services, such as actor lookup, chat, movement input, local IPC, and Chat Sync.

## What Changed

The implementation adds:

* A **Scripts** section in the main UI, separate from Macros.
* A script list with search, tags, icons, colors, duplicate, import, export, edit, local-PC run, and local-PC stop actions.
* A schema-v2 script editor containing source, bundle modules, typed parameters, capability declarations, resource leases, participant formation, appearance, and tags.
* Local multi-client execution and cross-PC Chat Sync execution.
* A synchronized run context containing the selected target, start time, seed, participant information, and script identity.
* Lua APIs for movement trajectories, dynamic actor following, chat, synchronized conversations, timing, per-character identity, runtime metadata, and capability discovery.
* A bounded per-run typed event stream for lifecycle and chat observations.
* Safe standard Lua base, math, string, table, bitwise, and coroutine features.
* Sandboxed, bundle-local reusable modules loaded with `require`.
* Packaged examples covering freeform motion, dialogue, dynamic following,
  stable rings, and typed-event orchestration, including **Event-Driven Curtain
  Call**.
* Independent movement policies so ordinary formations retain exact arrival behavior while continuous Lua trajectories can use smoother pursuit behavior.

## Script Storage and Deployment

There are three distinct locations to understand:

1. **Project source**: Packaged default scripts are maintained in `MasterOfPuppets/Lua/Scripts/*.lua`.
2. **Built plugin**: The build copies those files to a `Scripts` directory beside `MasterOfPuppets.dll`. They are not embedded inside the DLL.
3. **User configuration**: Scripts shown and edited in the in-game Script Editor are stored as `LuaScripts` entries in `MasterOfPuppets.json`.

At startup, MoP imports a packaged default only when a script with that default name is missing. It does not overwrite an existing edited script. Consequently, changing a project `.lua` file and deploying a new build does not replace an existing configuration copy with the same name. Delete or rename the saved copy before reload if the packaged default must be imported again, or update it in the Script Editor.

The project build still needs to be deployed to every PC that runs the plugin. A shared OneDrive project makes the same source available, but it does not by itself replace a running DLL or an existing saved script.

## Running and Stopping Scripts

### Current PC

```text
/mop lua run "Bee Swarm"
/mop lua run "Dynamic Conga Line"
/mop lua status
/mop lua stop
```

Here, **local** means all active MoP clients on the current PC, not only the game window where the command was entered. MoP sends a launch request with a shared start time, seed, and complete bundle-contract hash. Every receiver executes only its own installed copy after source, module, resource, capability, parameter, and identity hashes match; transmitted text is never the execution trust root.

### Multiple PCs through Chat Sync

```text
/cwl2 mopluarun "Dynamic Conga Line"
/cwl2 mopluastop
```

Use the chat prefix configured in **Settings > Chat Sync**; `/cwl2` is only an example. `/mop lua sync "Dynamic Conga Line"` is a local alias that sends the same cross-PC command through the configured prefix.

The readable `mopluarun` command is expanded by the sender's MoP client into a synchronization envelope containing a future start time, shared seed, SHA-256 script hash, and encoded run target. Listening MoP clients suppress that internal envelope from their chat display. The short readable command remains visible.

Chat Sync does not transmit script or module source. Each PC must already have the identical installed schema-v2 bundle. A missing or different copy is rejected instead of silently running different behavior. Envelope version 6 carries the full contract hash, a unique message ID, and a creation timestamp. Receivers reject duplicate, stale, future-dated, or malformed envelopes.

Cross-PC Lua starts and stops also require a trusted conductor. The default is
**self only**, which rejects Lua control sent by another character. To use one
character as the conductor for other PCs, select **allowlist** under
**Settings > Trusted Lua Conductors** on every receiving client and enter the
conductor's exact `Name@World`. Name-only matches are intentionally rejected.

The next distributed protocol generation also has a compact binary wire codec
for PREPARE, STAGE/READY, GO, clock probe/reply, shared variables, participant
messages, heartbeat, ACK/NACK, stop, completion, and error
frames. It detects corruption and fits an authoritative 32-CID PREPARE roster
under the 500-byte chat-command ceiling. Internal `mopluaphase` handlers now
validate exact sender/CID identity, conductor authority, replay age, phase
ordering, and roster membership, and expose participant state in the Scripts
window.

**Settings > Trusted Lua Conductors > Experimental PREPARE / READY / GO
staging** opts the public `mopluarun` flow into the readiness bridge. Each client
materializes PREPARE from the authenticated v6 manifest, takes a temporary
movement/synchronized-control lease, moves its local actor to the configured
Participant Formation with precise movement, emits STAGE and READY only after a
settled interval, and starts Lua at the conductor's future GO epoch. Readiness
regresses if movement restarts. Timeout policy can abort, continue only ready
performers, or continue everyone. Running participants send bounded heartbeats
and terminal status frames. Non-conductors also exchange bounded NTP-style
clock probes with the conductor. The filtered offset drives `mop.time()`,
`mop.runtime.shared_time()`, runtime elapsed diagnostics, and trajectory sample
time throughout the run; per-sample correction is capped to avoid phase snaps.
If a valid GO arrives late by at most five seconds, the client starts
immediately at the original shared phase instead of restarting the show at
phase zero. Older late joins are rejected explicitly. Loss of the conductor's
heartbeat aborts the local distributed session and stops its managed Lua run;
there is no implicit conductor election.

This mode defaults off pending live multi-PC validation. Every participant must
run this build, have identical scripts/formations and exact CID/name@world
configuration, trust the conductor, and share the same Chat Sync channel. The
GO frame carries both a wall-clock fallback and a high-resolution shared-clock
epoch. Live multi-PC validation is still required before enabling this mode by
default.

`mopluastop` is sufficient for a cross-PC stop. It does not need to be nested inside `mopbr`, because it is itself a Chat Sync command. Likewise, `mopluarun` does not need `mopbr` or `moprun`.

## Run Target Behavior

The target is dynamic. Select any player, NPC, enemy, or other visible actor before starting a target-aware script; MoP captures that actor and exposes its name through:

```lua
local anchor = mop.get_run_target()
```

The script therefore does not need to hardcode the target's name.

* `/mop lua run`: if no target is selected, `mop.get_run_target()` returns `nil`.
* `mopluarun` through Chat Sync: a selected target is used; if none is selected, the command sender becomes the effective run target.

That sender fallback lets a cross-PC Dynamic Conga start behind its conductor without requiring the conductor to target themselves. Scripts such as Bee Swarm intentionally reject a missing local run target because they require an anchor.

## Available Lua API

The safe base, math, string, table, bitwise, and coroutine libraries are exposed.
`require` loads only modules stored in the script bundle. File access,
operating-system access, unrestricted package searchers, debug/IO libraries,
dynamic compilation, networking, process access, and CLR access are unavailable.
`math.random` is a deterministic run-local stream initialized from the shared
run seed; it does not use ambient process randomness.

Source and every module are compiled before save/import and again before launch.
Syntax failures include the script or module chunk name, line, column, and nearby
token when the runtime supplies one.

Each run currently permits 1,000 log lines, 256 KiB of UTF-8 log text, 32 pending
waiters, 200 total chat actions, and at most 10 chat actions in any five-second
window. Crossing a limit stops the script with a quota error. `mop.runtime.info()`
and `mop.runtime.status()` expose current quota usage in their `quota` table.

### Versioned V2 runtime API

| Function | Purpose |
| :--- | :--- |
| `mop.api_version()` | Returns the current namespaced API version (`2.0.0`). |
| `mop.capabilities.list()` | Returns capability descriptors installed for this run. |
| `mop.capabilities.has(name, minimum_version?)` | Tests whether a capability and optional minimum version are available. |
| `mop.capabilities.describe(name)` | Returns one descriptor or `nil`. |
| `mop.capabilities.require(name, minimum_version?)` | Returns the descriptor or raises an actionable error. |
| `mop.runtime.info()` | Returns run ID, script, character, slot, participant count, seed, elapsed time, cancellation state, and host versions. |
| `mop.runtime.status()` | Returns the current run state and runtime metadata. |
| `mop.runtime.cancelled()` | Reports cancellation to cooperative script logic. |
| `mop.runtime.paused()` | Reports whether operator pause is active. |
| `mop.runtime.stop(reason?)` | Requests a structured stop of the current run. |
| `mop.runtime.local_time()` | Returns monotonic elapsed time for this run. |
| `mop.runtime.shared_time()` | Returns continuously corrected elapsed choreography time for distributed runs. |
| `mop.runtime.dalamud_version()` | Returns the host Dalamud version or `unknown`. |
| `mop.runtime.game_version()` | Returns the game version when available, otherwise `unknown`. |
| `mop.runtime.clientstructs_version()` | Returns the host ClientStructs version or `unknown`. |

Capability descriptors contain `name`, `version`, `description`, and a
`permissions` array. An absent capability is explicit, allowing portable scripts
to degrade gracefully.

### Typed event API (`mop.events`)

`mop.events.poll(name?)` returns the next matching event immediately or `nil`.
`mop.events.next(name?, timeout_seconds?)` waits without blocking the game
thread and returns either an event or `{ status = "timeout" }`.
`mop.events.stats()` reports capacity, published, consumed, dropped, and
completed counters. Events contain `status`, `sequence`, `name`,
`timestamp_unix_ms`, and a string-valued `data` table.

Each run owns a bounded 256-event queue. Producers never call Lua directly;
when the queue is full, the oldest entry is dropped and the pressure is visible
through `stats()`. Filtering is sequential: unmatched entries encountered while
searching for a named event are consumed. Current producers include run
lifecycle, host transition, `/say` chat, selected/focus target changes,
condition changes, and participant visibility/loss events. Game observations
are sampled at most four times per second from immutable framework-thread
snapshots.

### Shared variables and participant messages (`mop.coordination`)

`mop.shared.get(key)`, `mop.shared.list()`, and
`mop.shared.wait(key, timeout, after_sequence?)` read bounded per-run state.
`mop.shared.set(key, value)` updates it locally, but in distributed runs only
the authenticated conductor may write. Updates are monotonic and publish a
`sync.variable` event.

`mop.messages.send(topic, payload, target_slot?, schema_version?)` sends an
ordered message to the full authoritative roster or one zero-based participant
slot. `mop.messages.poll(topic?)` and `mop.messages.next(topic?, timeout?)`
consume the bounded per-run inbox. Messages expose ID, topic, payload, schema
version, sequence, sender/target CIDs, and receive time, and also publish a
`sync.message` event. Names are limited to 32 UTF-8 bytes, values/payloads to 96
UTF-8 bytes, schemas to versions 1–255, and queues to 256 messages. Distributed
coordination requires the experimental PREPARE/READY/GO bridge because its
authenticated protocol session supplies the roster and conductor authority.

### Typed game-state API (`mop.game-state`)

`mop.self.snapshot()`, `mop.target.snapshot(kind)`, `mop.actors.list()`,
`mop.actors.find(query)`, `mop.participants.list()`, and `mop.game.snapshot()`
return immutable tables. Actor lookup reports `found`, `missing`, or `ambiguous`
instead of guessing. Game-object and content IDs are strings so 64-bit values are
not rounded by Lua numbers. Snapshot `authority` distinguishes local, visible,
party, run-target, and configured-only data.

`mop.party.list()` exposes all currently observed party actors through the same
actor schema. `mop.player.profile()` adds locally authoritative content/world,
job, level-sync, race/tribe/company, mentor/returner, sex, and base-attribute
fields. It returns `nil` until Dalamud reports the player state loaded.

`mop.buddies.list()` returns immutable battle-buddy, companion, and pet
snapshots with typed IDs, HP, data ID, and an actor snapshot when the buddy has
a visible game object. `mop.actors.wait_visible(query, timeout)`,
`mop.actors.wait_lost(query, timeout)`, and
`mop.actors.wait_proximity(query, yalms, timeout)` provide bounded cancellable
waits without busy polling. `mop.target.wait_changed(timeout)` waits for the
selected target identity to change, and
`mop.game.wait_condition(name, active?, timeout?)` matches condition names
case-insensitively. Every wait returns a structured status table; timeout is
`{ status = "timeout" }`.

### Macro and formation API (`mop.automation`)

`mop.macros.list/status/run/pause/resume/stop` and
`mop.formations.list/find/run/stop` reuse the existing MoP managers. Mutating
calls require the corresponding resource leases. Every call specifies `local`
or `current_pc`; formation launch currently supports `current_pc` because the
existing formation executor is a coordinated local-PC operation.

`mop.macros.await(timeout_seconds?)` and
`mop.formations.await(timeout_seconds?)` provide cancellable, timeout-bounded
completion waits. They observe the current client's macro queue or local
formation movement; they do not claim that every remote PC completed. Results
use `completed` or `timeout` status and also publish `macro.*` or `formation.*`
events to the run event stream.

### Commands and game actions (`mop.actions`)

`mop.commands.execute(text, scope?)` dispatches one validated line to `local`
or `current_pc`; empty, multiline, NUL-containing, and over-500-byte commands
are rejected. `mop.actions.use(kind, id, scope?)` supports typed `action`,
`general_action`, and `item` IDs. `mop.actions.gearset(index, scope?)`,
`mop.actions.walk(on|off|toggle, scope?)`, and
`mop.actions.stop_movement(scope?)` reuse existing MoP services.

These calls require declared game-action, chat/action-budget, or movement
resources as appropriate and consume the centralized action-rate quota. Scope
never broadcasts implicitly. The movement-only stop intentionally leaves the
owning Lua run alive; the legacy global Stop Movement command still stops Lua
runs as before.

### Bundle-local modules

Imported/exported script definitions may contain a `Modules` object whose keys
are dot-separated identifiers and whose values are Lua source:

```json
{
  "Modules": {
    "theatre.dialogue": "return { opening = 'Welcome.' }"
  }
}
```

```lua
local dialogue = require("theatre.dialogue")
mop.say(dialogue.opening)
```

Modules are editable in the Script Editor and cached per run. Names containing traversal, slashes, empty segments,
or non-identifier segments are rejected. A bundle supports at most 64 modules,
64 KiB per module, and 256 KiB of module source in total.

### Legacy flat API

| Function | Purpose |
| :--- | :--- |
| `mop.log(text)` | Writes a line to the plugin log. |
| `mop.wait(seconds)` | Pauses the script for 0 to 10 seconds without blocking the game thread. |
| `mop.time()` | Returns elapsed seconds for the current run. |
| `mop.is_running()` | Returns false after the run is stopped or cancelled. |
| `mop.get_name()` | Returns the current character's name. |
| `mop.get_slot()` | Returns this local client's zero-based synchronized participant slot. |
| `mop.get_count()` | Returns the synchronized participant count. |
| `mop.get_seed()` | Returns the shared random seed for the run. |
| `mop.get_run_target()` | Returns the captured dynamic run target, or `nil` when unavailable. |
| `mop.names_match(a, b)` | Compares character names using MoP's world/name normalization. |
| `mop.set_anchor(name)` | Selects the actor used as the origin of subsequent trajectory samples. |
| `mop.trajectory_update(x, z, facing)` | Publishes the next anchor-relative trajectory sample. |
| `mop.follow_actor(options)` | Continuously follows the first visible actor from an ordered fallback list. |
| `mop.chat(text)` | Queues one chat command or message. |
| `mop.say(text)` | Sends one `/say` message. |
| `mop.wait_for_chat(speaker, text, timeout)` | Waits for an exact `/say` line from the expected speaker. |

Every run has a unique synchronized ID and a maximum lifetime of ten minutes.
Use `/mop lua pause|resume|stop|restart|status [run-id|"Script Name"]`, or the
per-run controls in the Scripts window. Disjoint declared resources may coexist;
conflicting leases are rejected with the owning run ID.

Expand **Logs and diagnostics** below an active or most recent run to inspect a
bounded 200-line script/error console and the event queue's published,
consumed, and dropped counts. Finished-run diagnostics are retained with the
last 50 lifecycle history entries.

Plugin unload, logout/character transition, and territory changes cancel every
active run, stop Lua-owned movement immediately, and release resources through
the same terminal cleanup path as script completion and failure.

## Dynamic Actor Following

`mop.follow_actor` accepts an ordered list of possible anchors and movement policy values:

```lua
mop.follow_actor {
    anchors = { "Nearest Predecessor", "Earlier Predecessor", leader },
    offset_x = 0,
    offset_y = 0,
    offset_z = -0.8,
    face_anchor = true,
    facing_offset = 0,
    precision = 0.1,
    brake_at_position = true,
    pursuit_prediction = false,
    immediate_steering = true,
}
```

The controller reevaluates visibility continuously and follows the first visible candidate that is not the local player. The relative offset is rotated by the chosen anchor's current facing. Defaults are zero offset, face the anchor, `0.1` precision, braking enabled, pursuit prediction disabled, and immediate steering enabled.

This ordered fallback is what allows a chain to survive missing performers. If the nearest predecessor is absent, the follower attaches to the next visible predecessor. If a previously missing predecessor later becomes visible while its script is already running, it automatically becomes the preferred anchor.

## Dynamic Conga Line

The packaged **Dynamic Conga Line** script uses this fixed performer roster:

1. Artemis Potato
2. Hermes Potato
3. Athena Potato
4. Gaia Potato
5. Nyx Potato
6. Apollo Potato
7. Hephaestus Potato
8. Poseidon Potato
9. Kazuko Aura
10. Barnabus Mangler
11. Cassandra Mangler
12. Ellis Mangler
13. Amelia Mangler
14. Madeline Mangler
15. Sabrina Mangler
16. Martin Mangler

Its correct behavior is:

* The selected actor leads. With cross-PC Chat Sync and no selected target, the sender leads.
* If the leader is one of the sixteen performers, the roster rotates around that leader to produce one chain rather than two branches from the fixed list.
* Each performer follows the nearest visible predecessor at a rotated offset of `0.8` yalms behind them.
* Missing performers are skipped without breaking the chain.
* Late-visible performers whose script is already running are inserted automatically.
* The exact slot is used, braking remains enabled, pursuit projection is disabled, and steering is immediate. This avoids overshoot and circular orbit behavior at short spacing.
* A selected external actor can lead but does not become a performer. For example, Garrison Mangler can lead the sixteen-person chain, but is not one of its follower entries.

Only clients that received the original run command have a running Lua script. A character that logs in after the command was sent must receive a new run command before it can participate.

Recommended cross-PC launch:

```text
/cwl2 mopluarun "Dynamic Conga Line"
```

Recommended cross-PC stop:

```text
/cwl2 mopluastop
```

This Lua conga is separate from the macro named `Conga: Target-Based Auto Line`. Running `moprun` invokes the old macro and its saved formation/order logic; it does not invoke Dynamic Conga Line.

## Sixteen Voices Synchronization

**Sixteen Voices** demonstrates state-aware conversation logic. Each client runs the same dialogue table, but only the named character speaks a line. Every client then waits until the expected speaker and exact message are observed in `/say` before advancing. If the expected response is not observed within the timeout, the conversation stops instead of drifting onto different lines on different PCs.

## Relevant Files

* `MasterOfPuppets/Lua/LuaScriptRunner.cs`: compatibility facade for one run.
* `MasterOfPuppets/Lua/Runtime/`: sandbox, validation, module loader, deterministic random stream, and provider registry.
* `MasterOfPuppets/Lua/Providers/`: versioned runtime and legacy API providers.
* `MasterOfPuppets/Lua/LuaScriptManager.cs`: synchronized run lifecycle and cancellation.
* `MasterOfPuppets/Lua/LuaActorFollowController.cs`: dynamic actor resolution and following.
* `MasterOfPuppets/Lua/LuaTrajectoryController.cs`: anchor-relative continuous trajectories.
* `MasterOfPuppets/Lua/LuaScriptCatalog.cs`: packaged default discovery and configuration import.
* `MasterOfPuppets/Ipc/IpcProvider.Lua.cs`: local IPC and cross-PC synchronization.
* `MasterOfPuppets/Game/ChatWatcher.cs`: `mopluarun` and `mopluastop` handling.
* `MasterOfPuppets/Ui/Windows/LuaScriptsWindow.cs`: Scripts list and convenience actions.
* `MasterOfPuppets/Ui/Windows/LuaScriptEditorWindow.cs`: script editor.
* `MasterOfPuppets/Lua/Scripts/dynamic_conga.lua`: packaged Dynamic Conga Line source.
