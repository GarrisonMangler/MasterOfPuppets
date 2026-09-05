# Carbuncle: Tornado

A newly named 32-pet variation with four smaller, tighter rings and alternating directions. This is a ground-level swirl, not a vertical funnel: the macro does not lift pets off the terrain or alter their native movement speed. Dense inner rings may visually overlap.

| Ring | Band | Radius | Diameter | Direction from above | Phase step | Requested circuit |
|---|---|---:|---:|---|---:|---:|
| 1 | Garrison | 0.75 yalms | 1.5 yalms | Clockwise | 0.30 s | 2.4 s |
| 2 | Wind-up | 1.25 yalms | 2.5 yalms | Counterclockwise | 0.32 s | 2.56 s |
| 3 | Artemis | 1.75 yalms | 3.5 yalms | Clockwise | 0.35 s | 2.8 s |
| 4 | Kazuko | 2.25 yalms | 4.5 yalms | Counterclockwise | 0.40 s | 3.2 s |

## Anchor override

Launch once from the initiating character with an explicit actor name:

```text
/mop run "Carbuncle: Tornado" -var=$anchor="Artemis Potato@Sargatanas"
```

The override takes precedence over the initiator's selected target. Use a specific Name@World for players, or a unique visible NPC name. Targets are resolved by name rather than a locked object ID; duplicate NPC names can be ambiguous. Do not use per-client aliases such as `target` or `[t]` for the override.

Without an override:

```text
/mop run "Carbuncle: Tornado"
```

The macro defaults `$anchor` to `$mop_origin_target`, captured from the initiating character at launch. If that is empty, the center is the initiator. An explicitly empty override also forces the initiator, even if they have a target selected:

```text
/mop run "Carbuncle: Tornado" -var=$anchor=""
```

If a nonempty anchor is not visible/resolvable on a particular client, placement on that client falls back to the initiator. Keep the center visible to every owner so the rings do not split between centers. Each owner targets the requested anchor once; placement itself uses the named anchor directly. Center identity is chosen at launch; its position and facing are sampled throughout the loop. Manual target changes after launch do not redirect it. Stop and restart to choose another center.

## Import and run

Copy `Carbuncle-Tornado.macro.blob.txt` into the macro import UI and retain all 32 character assignments. This creates `Carbuncle: Tornado`; it does not overwrite the previous orbit macros. Stop earlier pet loops before starting. Summon the pets and keep their owners close enough that the entire orbit remains in native placement range. Start on flat open ground with the center stationary.

Each client requires the native pet-placement implementation in version 1.15.0.322 or later. The local IPC macro-start command sends a macro index, so keep macro configuration/order consistent across the participating clients. `/mop run` broadcasts to connected local clients. Use the configured Chat Sync workflow for additional PCs. Launch once from the intended initiator, not separately on each owner.

Stop through the all-client macro Stop control, or `/mop stopmacro` on each running client. Pets may finish the last requested movement after requests stop.

## Variables

```text
$anchor = "$mop_origin_target"
$startup = 0.5
$r1 = 0.75
$d1 = 0.530330
$step1 = 0.30
$r2 = 1.25
$d2 = 0.883883
$step2 = 0.32
$r3 = 1.75
$d3 = 1.237437
$step3 = 0.35
$r4 = 2.25
$d4 = 1.590990
$step4 = 0.40
```

For ring N, `$rN` is radius, `$dN` is radius / sqrt(2), and `$stepN` is the full phase interval. Plain macro variables do not perform arithmetic; update the radius and diagonal with numeric values together. Do not redefine `$mop_origin` or `$mop_origin_target`.

The phase steps include the current 0.25-second global delay and command execution. Framework load from 32 clients can stretch the requested cadence. Increase steps if needed; don't reduce them below the global delay. Timing controls how often destinations change, not how fast pets can run. Paths use eight requested points per circuit; actual travel cuts between points and can miss intermediate destinations.

## Validation

Created from the imported four-ring macro using the current configuration read-only. All 32 exact CID assignments are retained. Compression round trip checked. The built plugin's actual command-line parser, inline-variable parser, macro deserializer, variable substitution, condition evaluator and anchor parser validated 1,280 active placements across five scenarios: override with another target selected, override with no selected target, default selected target, default no target, and explicitly empty override. Radius, zero height, alternating winding, timing and distinct destination assignments also passed checks.

This new choreography has not been live-tested. No plugin source, installed configuration or running macros were changed.
