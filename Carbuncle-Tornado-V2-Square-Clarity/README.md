# Carbuncle: Tornado V2 — square clarity revision

Keeps the existing macro name, all 32 CID/group assignments and five user variables. Circle/triangle phase intervals are now clamped to 0.25 seconds, while square mode uses fewer pets on smaller rings: 4, 8, 8 and 12 from inner to outer. The live configuration was updated on 2026-09-05.

The original square used the circle's compact dimensions and fast per-ring timing. This revision increases inner space and separation, and gives all four rings the same, slower waypoint interval for a selected size. This gives pets more time to reach the corners and side midpoints. It does not add waypoints, corner polling or variable tables.

| Size | Half-side lengths, inner to outer (yalms) | Interval for every ring | Requested lap |
|---|---|---|---|
| small | 1.25, 2.00, 2.75, 3.50 | 0.90 s | 7.20 s |
| medium | 1.75, 2.75, 3.75, 4.75 | 1.15 s | 9.20 s |
| large | 2.25, 3.50, 4.75, 6.00 | 1.40 s | 11.20 s |

Full side lengths are twice the listed values. These are still four nested squares, not one 32-pet perimeter. The rings contain 4, 8, 8 and 12 pets, with matching corner, midpoint and two-thirds side waypoints. Because the rings have different waypoint counts, their lap durations are intentionally different; this creates independent nested motion rather than a shared lap boundary. Actual arrival/settling is not detected; the time intervals are design choices that need in-game visual confirmation. The anchor should be stationary for the first comparison because its position and rotation continue to affect placements.

## Delivery by dev-plugin reload

Plugin **1.15.0.334** embeds this revision. The temporary startup importer adds V2 if missing, or upgrades only the action blocks of the known original V2. It preserves personal variables, assignments and metadata. It skips already updated macros, edited action blocks and duplicate names. The macro is not automatically run. See `docs/temporary-tornado-v2-import.md` for details and removal instructions.

After every participating client has reloaded, launch directly in square mode for a clean comparison:

```text
/cwl2 moprun "Carbuncle: Tornado V2" -var=$anchor_tornado="<t>";$direction=clockwise;$shape=square
```

Use your configured shared channel and summon pets first. Stop any prior Tornado before starting a replacement. Your existing live-switch command is supported:

```text
/cwl2 mopbr /mop setvar -var=$shape=square
```

`setvar` changes executing runs, not the saved Variables field. On **1.15.0.335 or newer**, changing `shape`, `mode`, `direction` or `anchor_tornado` interrupts V2's phase wait and reselects its path without waiting for a lap. The runner clears the previous branch and phase deadline and starts the new shape at each member's assigned first waypoint; startup targeting is not repeated. A pet command already being processed and its global delay can still complete before the switch. Pets must still travel to their new positions. Repeating an identical value does not interrupt the loop.

On 1.15.0.334 and older, changes were picked up only at the next lap, which can take several seconds in square mode. Update all participating plugins for responsive live controls. This is a runtime fix, so the macro payload does not need another import. A common interval does not itself create a shared cross-client clock.

## Manual import alternative

Use `Carbuncle-Tornado-V2-Square-Clarity.replace-existing.import.json` with **ReplaceExisting**, **Use CIDs**, and **Backup before import** to replace an existing V2. This manual replacement imports the full exported macro, unlike the startup upgrade which preserves personal variables and assignments. Review personal settings before using it. Do not use OverwriteAll.

The `.macro.blob.txt` is the standard GZip/Base64 clipboard alternative, but clipboard import appends a copy. Do not use it to update an existing V2 unless you deliberately remove/rename the old copy first; avoid duplicate names.

## Verification

- 2,304 expanded-path cases checked across 32 members, three sizes, four shape inputs, three directions and two anchor cases.
- 1,728 circle/triangle/invalid-shape-fallback cases are identical to the original optimized V2.
- 576 square cases retain direction, starting phase, anchor/fallback arguments, startup wait, and the original member assignments.
- Square rings use 4, 8, 8 and 12 distinct positions per phase from inner to outer.
- Production editor sanitization leaves the generated actions unchanged. Production BlobUtil GZip/Base64 roundtrip and 76-character line wrapping passed.
- Isolated expansion benchmark: about 3.2 ms and 1.43 MiB allocated per lap, averaged over ten repetitions. Still only five user variables plus five built-in variables. This excludes game calls, conditions, rendering, movement and waits.

`validation.json` records export-generation checks before the plugin bundle was updated. The revised movement has not been tested in game; no FPS or arrival-time guarantee is implied.
