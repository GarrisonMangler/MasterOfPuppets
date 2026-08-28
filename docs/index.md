# Documentation

## Architecture and direction

- [Lua Automation V2 Long-Term Goal](architecture/LUA_AUTOMATION_V2_GOAL.md) - Authoritative long-term architecture, Dalamud coverage contract, synchronization and movement requirements, repository organization rules, tests, and acceptance criteria.
- [Lua V2 Runtime Architecture](architecture/lua-runtime-v2.md) - Implemented dependency direction, provider contract, security boundaries, phased file-level plan, and risks.

## Current behavior

- [Lua Scripting](lua-scripting.md) - Current script editor, storage, commands, public `mop` API, and synchronization for imported scripts.
- [Live Formations & Natural Movement](live-formations-and-natural-movement.md) - Real-time tracking, anchor tokens, slot holding, and movement policies used by formations and Lua.
- [Configuration & Live Variables](configuration-and-live-variables.md) - Configuration reload aliases, out-of-band `/mop setvar` updates, and Windows clipboard/importer features.
- [Macro Conditionals](macro-conditionals.md) - Control flow with `/mopif`, `/mopelseif`, `/mopelse`, and `/mopendif`.
- [Phase-Locked Macro Timing](phase-locked-macro-timing.md) - Monotonic timeline management for synchronized movement loops.
- [Movement Coordinate System](movement.md) - FFXIV world axes, relative directions, and facing angles.

## Templates

- [Formation Request](templates/formation-request.md) - Reusable specification for requesting an importable formation without editing live configuration.
