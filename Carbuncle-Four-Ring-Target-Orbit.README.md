# Carbuncle: Four-Ring Target Orbit

Target-first revision of the four-ring macro. The 32 character assignments, ring coordinates, alternating directions, and timing variables are preserved from the currently imported `Carbuncle: Four-Ring Initiator Orbit`.

## Center selection

- Select a target on the initiating character before starting: all pets orbit that target.
- Start with no target: all pets orbit the initiating character.
- If a selected target cannot be resolved on a particular client, that client's placement falls back to the initiator. Keep the chosen target visible to all owners to avoid rings splitting between centers.

The launcher's target identity is captured in `$mop_origin_target` when the normal broadcast starts the macro. Each pet owner's unrelated local target is ignored. The selected center's position and facing remain live, so the ring follows that actor's movement and turns. Changing the initiator's selected target after launch does not switch centers; stop and restart to select a different one.

Targets are identified by name (Name@World for players), not a locked object ID. Multiple NPCs with the same name are ambiguous; use a uniquely named visible actor for predictable results. The fallback is implemented explicitly for an empty launch target, avoiding the parser's default-to-local-target behavior.

## Import and launch

Stop the old orbit on all participating clients. Import the contents of `Carbuncle-Four-Ring-Target-Orbit.macro.blob.txt` through the macro import UI, retaining the 32 character assignments. This creates a separately named macro; it does not edit the live configuration directly or overwrite the old macro.

Summon the pets, position owners within placement range, choose the initiator's target (or clear it), and launch once from the initiator:

```text
/mop run "Carbuncle: Four-Ring Target Orbit"
```

Every participating client needs the native placement implementation in version 1.15.0.322 or later. Keep the macro configuration/order consistent: the local IPC run path identifies the macro by index. The command starts connected local clients; use the existing configured Chat Sync workflow for additional PCs. Do not launch separately on each character or define `$mop_origin` / `$mop_origin_target` in macro Variables.

## Rings and variables

| Ring | Band | Radius | Direction viewed from above | Phase interval | Requested circuit |
|---|---|---:|---|---:|---:|
| 1 | Garrison | 2 yalms | Clockwise | 0.35 s | 2.8 s |
| 2 | Wind-up | 3 yalms | Counterclockwise | 0.55 s | 4.4 s |
| 3 | Artemis | 4 yalms | Clockwise | 0.70 s | 5.6 s |
| 4 | Kazuko | 5 yalms | Counterclockwise | 0.90 s | 7.2 s |

For each ring N, `$rN` sets radius, `$dN` must be radius / sqrt(2), and `$stepN` sets the full phase interval including global delay. Macro variables cannot perform arithmetic: enter numeric radius and diagonal values together. `$startup` is 0.5 seconds. Keep phase intervals above the global delay with headroom. Pets follow eight-point paths, not perfect circles; the listed times describe destination requests rather than confirmed arrival.

Native pet movement speed, range, terrain and availability still apply. Increase a ring's interval if pets cut across or fail to reach successive points. Stop through the all-client macro Stop control, or `/mop stopmacro` on each running client; pets may finish their last requested movement.

## Validation

GZip/Base64 round trip, original variables, and exact CID mappings verified. The built plugin's own JSON deserializer, variable expansion, condition evaluator, argument tokenizer and anchor parser were used to check all 32 rows for three launch scenarios: player target, NPC target, and no target while a recipient has an unrelated local target. All 768 active placements selected the intended named anchor; target-present branches also parsed the named initiator fallback correctly. No plugin source changes or deployment were required. Live behavior of this revision remains to be tested.
