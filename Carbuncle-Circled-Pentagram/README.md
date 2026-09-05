# Circled pentagram macro pair

Two macro exports, with temporary startup delivery added in dev-plugin version 1.15.0.336. Export generation did not modify live configuration or existing macros.

## Assignments

Both macros use the same 32 explicit CIDs, in the existing Tornado V2 command-block order from the live configuration read on 2026-08-30. See `character-assignments.csv` for every character's role and colour.

| Slots | Role | Glamour | Colour |
|---|---|---|---|
| 1–12 | Outer circle, clockwise | Emerald Carbuncle | Blue |
| 13–27 | Moving pentagram, three starting positions on each line | Topaz Carbuncle | Yellow |
| 28–32 | Five stationary star tips | Ruby Carbuncle | Red |

`Carbuncle: Pentagram Colors` gives each character exactly these two commands, substituting Emerald, Topaz, or Ruby as assigned:

```text
/egiglamour "Carbuncle" "Emerald Carbuncle"
/ac "Summon Carbuncle"
```

The macro does not change jobs, dismiss existing pets, or check summon success. Use characters able to execute the supplied commands, and verify that all pets have the intended glamour before starting movement.

## Import

On each participating client's plugin, import `Carbuncle-Pentagram-Pair.import.json` using **AppendNew**, **Use CIDs**, and **Backup before import**. This adds both new names without replacing other macros. If one of these names already exists, AppendNew skips it; use Merge only if you intend to replace those same-name macros. Do not use OverwriteAll.

Alternatively, each `.macro.blob.txt` is a single-macro clipboard import. Import either the pair JSON or the two blobs, not both, to avoid duplicate copies. The blobs use the production GZip/Base64 codec, wrapped at 76 characters.

Import the two macro exports through the normal MasterOfPuppets macro import workflow. Existing same-name macros are preserved, and neither macro runs automatically.

## Run

Stop any running Tornado or other pet movement first:

```text
/cwl2 mopstop
```

Then set colours and summon:

```text
/cwl2 moprun "Carbuncle: Pentagram Colors"
```

Wait for all summons to finish, then start the pattern:

```text
/cwl2 moprun "Carbuncle: Circled Pentagram"
```

The sender is the default centre (`$anchor = "$mop_origin"`). Keep that character stationary and facing the same direction on open, level ground. Initial placement gets a 3-second settling period; increase `$startup` if the pets need longer to reach their initial positions. The five red tips are placed once, so moving or turning the centre afterwards will separate the moving paths from the stationary tips. Stop and restart for a new centre.

To use a different stationary player as centre, pass the same unambiguous name/world to everyone:

```text
/cwl2 moprun "Carbuncle: Circled Pentagram" -var=$anchor="Character Name@World"
```

All commands retain the existing pet-place anchor behaviour and use the sender as fallback when the specified anchor cannot be resolved. No changes from the rejected pet-anchor proposal are included.

## Movement and timing

- Radius: 6 yalms; diameter: 12 yalms. Geometry is baked into the actions; there is no radius variable.
- Circle: 48 equal angular waypoint steps, 0.50 seconds per step, requested 24-second lap. Twelve pets start 30 degrees apart.
- Pentagram: top → bottom-right → upper-left → upper-right → bottom-left → top. Each straight line has 12 waypoint intervals; 60 intervals at 0.60 seconds request a 36-second lap.
- Fifteen yellow pets start at 1/6, 1/2, and 5/6 of each star line. They continue through crossings and turn only at outer tips. Moving pets briefly pass through the positions occupied by the red tips.
- Red tips: one placement each; no recurring pet commands.

Four user variables are used: `$anchor`, `$startup`, `$circle_step`, and `$star_step`. There are no coordinate-variable tables, nested shape branches, or per-frame updates. Only one command block runs for each assigned character.

The live client had a 0.25-second global action delay when these exports were generated. Keep the global delay below the requested step intervals, with room for dispatch overhead, on every client. Actual movement is controlled by the game; phase waits do not verify arrival or create a shared cross-client clock. Pets may pause, cut corners if commands arrive before they settle, or drift out of phase under load. These timings are an initial design, not measured movement or an FPS guarantee.

## Verification

`validation.json` records the checks: 64 production command-sanitizer checks, 64 production variable-expansion checks, production blob round trips, 900 independently checked straight star segments, 75 required corner visits, 12 closed circular paths, five fixed tips, and 32 exact two-line glamour/summon blocks. Character IDs are preserved as 64-bit integers in the JSON exports.

The macros have not been imported or run in game by this task.
