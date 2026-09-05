# Carbuncle: Tornado (Direction) — size modes

Created from the two exact macros in the live APPDATA configuration, read-only on 2026-08-30. No plugin code or live configuration was changed.

## Apply

The smallest change is to replace only the existing Tornado (Direction) macro's Variables field with the contents of `Carbuncle-Tornado-Direction-Modes.variables.txt`. All 32 command blocks and assignments are already identical between the two source macros and remain unchanged.

Alternatively, import `Carbuncle-Tornado-Direction-Modes.import.json` using **ReplaceExisting**, **Use CIDs**, and **Backup before import**. The file contains only `Carbuncle: Tornado (Direction)`. Do not use OverwriteAll. Wide Tornado remains untouched.

The `.macro.blob.txt` file is the standard clipboard-import blob. Clipboard import appends a macro with the original name rather than replacing it; avoid leaving duplicate names when launching by name. Prefer the Variables edit or ReplaceExisting file import above.

## Modes

| Mode | Four radii (yalms) | Four phase intervals (seconds) |
|---|---|---|
| small (default) | 0.75, 1.25, 1.75, 2.25 | 0.30, 0.32, 0.35, 0.40 |
| medium | 1.375, 2.125, 2.875, 3.625 | 0.325, 0.435, 0.525, 0.65 |
| large | 2, 3, 4, 5 | 0.35, 0.55, 0.70, 0.90 |

Small exactly preserves the current Tornado values. Large exactly preserves Wide Tornado values. Medium uses midpoint radii/timing and recalculated diagonal coordinates (radius / sqrt(2)). These are radii, not diameters.

Set `$mode = small`, `$mode = medium`, or `$mode = large` in Variables, or override at launch:

```text
/mop run "Carbuncle: Tornado (Direction)" -var=$mode=small
/mop run "Carbuncle: Tornado (Direction)" -var=$mode=medium
/mop run "Carbuncle: Tornado (Direction)" -var=$mode=large;$direction=clockwise
```

Use the lowercase mode names exactly. Unknown or capitalized modes do not fall back: they leave preset references unresolved. Stop existing loops and relaunch after changing the mode.

Direction still supports alternating (default), clockwise, and counterclockwise. Anchor and startup behavior are unchanged. The default center is the initiating character's target at launch, with the initiating character as fallback. Summon pets first and keep all owners within placement range.

The mode selector uses existing multi-pass variable substitution (for example, `$r1` -> `$r1_$mode` -> `$r1_small` -> `0.75`). It adds no movement actions or loop conditionals. Preset values can be edited individually; keep each diagonal consistent with its radius. Timing remains requested timing: actual pet travel and command processing may vary.

## Verification

Compiled the existing parser/variable expansion methods directly from Macro.cs in memory, without building the plugin. All 576 complete command expansions matched direct numeric presets across 32 command blocks, three sizes, three directions, and two anchor scenarios. No unresolved variables remained in those cases. All command blocks were confirmed identical between the live source macros. Gzip/Base64 export round trip passed. No in-game execution was performed.
