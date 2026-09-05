# Campfire pentagram outline — 1-yalm spawn offset

Uses the same 32 CIDs and 6-yalm radius as `Carbuncle: Circled Pentagram`. The measured spawn offset supplied by the user is **1 yalm in front of the character**.

The formation has one unassigned origin plus 32 character positions. Fires occupy the five star tips, five intersections and 22 other points on the star lines. Intersections are shared fire locations, not duplicated for each crossing line. The layout is mirror-symmetric. Minimum fire-centre spacing is approximately 0.898 yalms; actual fire effects may overlap.

All characters face the anchor's heading. Each standing position is exactly one yalm behind the intended fire position in that rotated coordinate frame. This is the same rotation convention used by the Carbuncle pentagram. There is no outer circle of campfires; the blue Carbuncles provide the moving circle.

## Import on participating clients

1. Copy all of `Campfire-Pentagram-Positions.formation.blob.txt`, including its `MOPF1:` prefix. Use **Formations → Import → Import from Clipboard**. Keep the name **Campfire: Pentagram Positions** exactly as exported. Avoid importing it twice: the UI renames duplicates, but the macros still refer to the original name.
2. Import `Campfire-Pentagram-Macros.import.json` with **AppendNew**, **Use CIDs**, and a backup. This adds **Campfire: Pentagram Place** and **Campfire: Pentagram Light**. Alternatively, import the two individual macro blobs; do not import both alternatives.

Alternatively, dev build **1.15.0.338** embeds the formation and both macros. On reload, the temporary startup installer adds any missing names and saves them through the normal configuration API. The exact broken v337 placement actions are repaired in place; custom actions, duplicate names, variables and character assignments are preserved. Nothing runs automatically. No manual import is needed with that build. The placement guard now correctly separates its comparisons with logical OR; the formation geometry and lighting actions are unchanged. See `docs/temporary-campfire-import.md` for the installation and removal policy.

## Use one external, stationary anchor

Use the full name/world of a stationary character **outside the 32 assigned Lalafells** as the anchor for both the campfires and Carbuncles. Leave this character at the desired centre and keep its facing unchanged.

The placement macro rejects an empty anchor, local target/self aliases and the 32 participant names. It does not default to the sender. It explicitly uses the same named actor as its fallback, so a missing actor does not redirect movement to the sender.

If you need all 32 Lalafells to move and do not have a separate stationary anchor, do not run this version. A shared captured world-position centre would require a different implementation. In particular, the existing Carbuncle macro would also need to keep using that same fixed centre.

## Place, check, then light

Stop existing macro-controlled movement before repositioning:

```text
/cwl2 mopstop
```

Replace `External Anchor@World` with the real full name:

```text
/cwl2 moprun "Campfire: Pentagram Place" -var=$anchor="External Anchor@World"
```

This dismisses existing minions, then sends every assigned character to its compensated formation position using precise movement. Facing is applied on arrival. Allow everyone to finish moving and turning, and check for stuck or missing characters. The macro queue can finish before physical movement does; it does **not** indicate arrival.

Once all 32 are stationary:

```text
/cwl2 moprun "Campfire: Pentagram Light"
```

This sends `/minion "Wanderer's Campfire"` once per assigned character. Keep the Lalafells still afterward. Do not repeat Light by itself: the native command toggles the specified minion. Place starts with bare `/minion` to dismiss any current minion, giving the following Light command a known starting state. Native syntax is documented by Square Enix: https://na.finalfantasyxiv.com/lodestone/playguide/db/text_command/fc12bf2340a/

After lighting, resume the Carbuncle movement around the **same** anchor:

```text
/cwl2 moprun "Carbuncle: Circled Pentagram" -var=$anchor="External Anchor@World"
```

Do not move or turn the centre. Formation precision, terrain, delayed commands and any deviation from the measured 1-yalm spawn offset can affect alignment. The routine intentionally does not assume a fixed travel time, automatically repeat summon commands, or move characters away afterward.

## Validation

- Same 32 CID assignments in the formation and both macros; the origin has no assigned character.
- All 32 intended fire centres lie on star segments, including all five tips and five unique crossings.
- 256 checks using production formation/facing math confirm that the standing position plus a 1-yalm forward vector reaches the intended fire point at eight anchor headings.
- 64 production sanitizer and 64 variable-expansion checks passed.
- GZip/Base64 round trips passed, with 76-character payload lines; formation code includes the required `MOPF1:` prefix.
- The placement geometry and commands have not been verified in game.

See `character-assignments.csv` for positions and `validation.json` for the check results.
