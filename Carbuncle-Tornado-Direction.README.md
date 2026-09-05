# Carbuncle: Tornado (Direction)

A separate 32-pet Tornado macro with a direction variable. Import `Carbuncle-Tornado-Direction.macro.blob.txt` through the plugin macro import UI, retaining all 32 character assignments. The original Tornado export is unchanged.

## Direction

Set `$direction` at launch:

```text
/mop run "Carbuncle: Tornado (Direction)" -var=$direction=alternating
/mop run "Carbuncle: Tornado (Direction)" -var=$direction=clockwise
/mop run "Carbuncle: Tornado (Direction)" -var=$direction=counterclockwise
```

`alternating` is the default: Garrison and Artemis rotate clockwise; Wind-up and Kazuko rotate counterclockwise. The other options turn all four rings in the selected direction, viewed from above. Values are case-insensitive; unrecognized values use alternating. Stop and restart to change direction cleanly.

The conditional comparisons deliberately wrap the variable and expected value in square brackets. The plugin condition evaluator also accepts substring matches, so these delimiters prevent `clockwise` from matching `counterclockwise`.

## Anchor

Combine an anchor override with direction in the same variable argument:

```text
/mop run "Carbuncle: Tornado (Direction)" -var=$anchor="Artemis Potato@Sargatanas";$direction=clockwise
```

Without an override, `$anchor` defaults to the initiator's target captured at launch; no target means the initiator. An explicitly empty anchor forces the initiator:

```text
/mop run "Carbuncle: Tornado (Direction)" -var=$anchor="";$direction=counterclockwise
```

Use a specific Name@World for a player or a unique visible NPC name. Do not use per-client target aliases. Names are resolved rather than locked object IDs; duplicate NPC names can be ambiguous. An unresolved named anchor falls back to the initiator on that client. Keep the center visible to every owner. Each owner targets the anchor once; placement resolves its current position and facing throughout the loop. Changing the selected target after launch does not change the center.

## Rings and timing

| Ring | Band | Radius (yalms) | Diagonal variable | Phase interval | Requested circuit |
|---|---|---:|---:|---:|---:|
| 1 | Garrison | 0.75 | 0.530330 | 0.30 s | 2.4 s |
| 2 | Wind-up | 1.25 | 0.883883 | 0.32 s | 2.56 s |
| 3 | Artemis | 1.75 | 1.237437 | 0.35 s | 2.8 s |
| 4 | Kazuko | 2.25 | 1.590990 | 0.40 s | 3.2 s |

The existing `$startup=0.5`, `$r1` through `$r4`, `$d1` through `$d4`, and `$step1` through `$step4` remain adjustable. Set each diagonal to its radius divided by sqrt(2), using a numeric value: plain macro variables do not calculate expressions. Do not redefine `$mop_origin` or `$mop_origin_target`.

This is a ground-level swirl with eight requested points per circuit, not a vertical funnel. Pets can overlap in the inner rings. Native movement speed is unchanged; actual travel can cut corners or miss intermediate destinations. Phase intervals include the current 0.25-second global delay; processing load and additional direction checks can stretch the requested cadence. Increase intervals if necessary and do not set them below the global delay.

## Running and stopping

Stop previous pet loops, summon pets, and start on flat open ground with a stationary center and all owners within placement range. Launch once from the intended initiator. Every client needs native pet placement from plugin version 1.15.0.322 or later. Keep macro configuration and order consistent because local IPC starts macros by index. `/mop run` broadcasts to connected local clients; use the configured Chat Sync workflow for additional PCs.

Stop with the all-client macro Stop control, or `/mop stopmacro` on each running client. Pets may finish their last requested movement.

## Validation

All 32 owners were checked against the latest live configuration, read-only. The export compression round trip passed. The built plugin's actual editor normalization, command and variable parsing, variable expansion, condition evaluation, and anchor parsing validated 7,680 active placements across six direction/default cases and five anchor scenarios. Checks covered clockwise versus counterclockwise discrimination, fallback, radius, zero height, timing, loop structure, winding, and distinct destinations.

This variation has not been live-tested. No plugin source, live configuration, or running macros were changed.
