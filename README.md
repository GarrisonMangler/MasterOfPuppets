# Master Of Puppets

Master Of Puppets is a Dalamud plugin for coordinating FFXIV performers across
local clients and multiple PCs. It provides broadcast actions, programmable
macros, formations, synchronized movement, and a Lua scripting system for
stateful theatrical automation.

The [documentation index](docs/index.md) separates current user-facing behavior
from the [long-term Lua automation goal](docs/architecture/LUA_AUTOMATION_V2_GOAL.md).

## Installation

Add this URL under **Dalamud Settings > Experimental > Custom Plugin Repositories**:

```
https://raw.githubusercontent.com/zunetrix/DalamudPlugins/main/pluginmaster.json
```

Save the settings, open the Plugin Installer, and search for **Master Of Puppets**.

## Development

The solution targets .NET 10 for Windows. Build from the repository root:

```sh
dotnet build -c Debug
dotnet build -c Release
dotnet test ./MasterOfPuppetsTests/
```

Regenerate or verify the SDK-derived Lua capability ledger with:

```sh
dotnet run --project tools/LuaCapabilityCoverage
dotnet run --project tools/LuaCapabilityCoverage -- --check
```

The full test suite also checks the ledger against the locally installed Dalamud
and FFXIVClientStructs assemblies, so an SDK upgrade cannot silently change the
planned Lua surface.

### Safe multi-client hot reload

Do not build directly into the Release directory watched by Dalamud. Stage the
complete build first, then deploy dependencies followed by one settled write of
the plugin DLL:

```powershell
# Compile and verify the staged output without touching running clients.
.\tools\Build-HotReload.ps1

# Deploy the settled output and trigger one automatic reload.
.\tools\Build-HotReload.ps1 -Deploy
```

The deployment script only updates support files whose hashes changed and always
writes `MasterOfPuppets.dll` once, last.

## Repository layout

- `MasterOfPuppets/` - plugin source and packaged Lua scripts.
- `MasterOfPuppetsTests/` - automated tests.
- `docs/` - maintained architecture, user guides, and templates.
- `tools/` - development and deployment helpers.

Repository-wide working rules live in [AGENTS.md](AGENTS.md). Lua-related work
must also follow the authoritative long-term goal linked above.
