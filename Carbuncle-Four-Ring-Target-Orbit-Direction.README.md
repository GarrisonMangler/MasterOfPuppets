# Carbuncle: Four-Ring Target Orbit (Direction)

Revised from the supplied Four-Ring Target Orbit blob. Import `Carbuncle-Four-Ring-Target-Orbit-Direction.macro.blob.txt` in the plugin UI. The new name leaves the original macro available. All 32 owner assignments, eight-point paths, initial phases, ring dimensions, startup delay, and phase intervals are retained.

## Run

```text
/mop run "Carbuncle: Four-Ring Target Orbit (Direction)"
```

Default: alternating directions, centered on the initiator's target captured at launch, or the initiator when no target is selected.

```text
/mop run "Carbuncle: Four-Ring Target Orbit (Direction)" -var=$direction=clockwise
/mop run "Carbuncle: Four-Ring Target Orbit (Direction)" -var=$direction=counterclockwise
/mop run "Carbuncle: Four-Ring Target Orbit (Direction)" -var=$direction=alternating
```

Combine anchor and direction overrides:

```text
/mop run "Carbuncle: Four-Ring Target Orbit (Direction)" -var=$anchor="Artemis Potato@Sargatanas";$direction=clockwise
```

Force the initiator as center even with a target selected:

```text
/mop run "Carbuncle: Four-Ring Target Orbit (Direction)" -var=$anchor="";$direction=counterclockwise
```

## Variables and behavior

`$anchor` defaults to `$mop_origin_target`. Use a specific Name@World for a player, or a unique visible NPC name; duplicate NPC names are ambiguous. Avoid per-client target aliases. Empty anchors explicitly use `$mop_origin`. If a named anchor cannot be resolved on a particular client, placement falls back to the initiator on that client. Keep the center visible to every participant so rings do not split between centers.

Each owner targets the anchor once; placement resolves the named actor's position and facing throughout the loop. Changing the initiator's selected target after launch does not redirect the macro. Stop and restart to change center or direction.

`$direction` defaults to `alternating`. `clockwise` or `counterclockwise` turns all four rings in that direction, viewed from above. Values are case-insensitive; unrecognized values use alternating. Conditional comparisons bracket both strings to prevent the plugin's substring matching from confusing clockwise with counterclockwise.

| Band | Radius (yalms) | Diagonal | Alternating direction | Phase interval | Requested circuit |
|---|---:|---:|---|---:|---:|
| Garrison | 2 | 1.414214 | Clockwise | 0.35 s | 2.8 s |
| Wind-up | 3 | 2.12132 | Counterclockwise | 0.55 s | 4.4 s |
| Artemis | 4 | 2.828427 | Clockwise | 0.70 s | 5.6 s |
| Kazuko | 5 | 3.535534 | Counterclockwise | 0.90 s | 7.2 s |

Existing `$startup=0.5`, `$r1` through `$r4`, `$d1` through `$d4`, and `$step1` through `$step4` remain adjustable. If changing a radius, set its diagonal to radius / sqrt(2) as a numeric value; plain macro variables do not calculate expressions. Do not redefine `$mop_origin` or `$mop_origin_target`.

These are ground-level rings. Pet speed is unchanged; actual travel can cut corners or miss intermediate points. Phase intervals include the global delay, and client load or added conditional processing can stretch the requested cadence. Keep intervals above the configured global delay.

## Import, stop, and validation

Stop previous pet loops before starting. Summon pets, keep owners within native placement range, and start on flat open ground with a stationary center. Every client needs native pet placement from plugin version 1.15.0.322 or later. Keep macro configuration and order consistent because local IPC starts macros by index. Launch once from the intended initiator: `/mop run` broadcasts to connected local clients; use configured Chat Sync for additional PCs.

Stop through the all-client macro Stop control, or `/mop stopmacro` on each running client. Pets may finish their last requested movement.

Compression round trip and original CID preservation passed. All 32 owners were validated against the current live configuration, read-only. The built plugin's actual normalization, parsing, variable expansion, conditional evaluation, and anchor parsing validated 7,680 active placements across six direction/default cases and five anchor scenarios, including timing, winding, radius, zero height, and distinct destinations. This revision has not been live-tested. No plugin source or live configuration was changed.
