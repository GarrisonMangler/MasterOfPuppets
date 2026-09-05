# Carbuncle: Four-Ring Initiator Orbit

32 pets in four concentric rings around the character who launches the macro. Pet owners remain in place. Directions are viewed from above.

| Ring | Band | Radius (yalms) | Direction | Step (seconds) | Requested circuit |
|---|---|---:|---|---:|---:|
| 1 | Garrison | 2 | Clockwise | 0.35 | 2.8 s |
| 2 | Wind-up | 3 | Counterclockwise | 0.55 | 4.4 s |
| 3 | Artemis | 4 | Clockwise | 0.70 | 5.6 s |
| 4 | Kazuko | 5 | Counterclockwise | 0.90 | 7.2 s |

Each ring has eight pets spaced 45 degrees apart in its requested destinations. Outer rings have longer intervals to keep requested travel speeds comparable. This does not change the game's pet movement speed. The center and orientation follow the initiator's current position and facing. Eight-point paths approximate circles: actual movement follows chords between points and can cut further inside the ring when commands interrupt travel.

## Import and run

1. Copy the text from `Carbuncle-Four-Ring-Initiator-Orbit.macro.blob.txt` into MoP's macro import UI. Retain the included 32 character assignments. This creates a new macro; existing macros are not overwritten.
2. Make the same macro configuration/order available on all participating clients. The local IPC RunMacro route sends a macro index. Every client needs version 1.15.0.322 or later for native pet placement.
3. Stop previous pet loops. Summon the pets and keep all owners close enough for their full rings to remain in valid placement range. The initiator must be visible to every owner. Use flat, open ground and keep the initiator still for the first test.
4. Launch ONCE from the intended initiator using the all-client macro Run control or:

```text
/mop run "Carbuncle: Four-Ring Initiator Orbit"
```

The normal RunMacro broadcast supplies `$mop_origin` from the launcher. Do not define `$mop_origin` in the macro Variables or launch independently on each client. This command broadcasts to connected local clients; it does not by itself start clients on another PC. For multiple PCs, use the project's configured Chat Sync macro-start workflow with the same initiator identity.

Every owner targets the initiator once at startup. Subsequent placements use the initiator's name directly, so manual target changes do not redirect the orbit. The initiator can be outside the 32 pet-owner assignments.

## Adjustable variables

`$startup` is the initial settling interval, default 0.5 seconds. For ring N, edit `$rN` (radius), `$dN` (diagonal coordinate), and `$stepN` (phase interval).

| Variable pair | Default values |
|---|---|
| `$r1` / `$d1` | 2 / 1.414214 |
| `$r2` / `$d2` | 3 / 2.121320 |
| `$r3` / `$d3` | 4 / 2.828427 |
| `$r4` / `$d4` | 5 / 3.535534 |

Plain macros substitute text and cannot calculate arithmetic. When changing a radius, update its diagonal too: diagonal = radius / sqrt(2). Enter numeric values, not expressions. Diameter is twice the radius.

The phase interval includes command execution and the current 0.25-second global delay. Keep each step above the global delay with headroom. Absolute phase waits reduce accumulated timing drift, but do not guarantee simultaneous client starts or exact pet spacing. The listed circuit times describe destination requests, not confirmed pet arrival.

Increase a ring's step if pets fail to reach successive points. Reduce its radius or move its owners closer if placements are rejected. Native range, pet availability, and terrain/pathing limits still apply. No vertical offset is requested.

Stop with the all-client macro Stop control, or `/mop stopmacro` on every running client. Pets may finish their most recent destination after the loop is stopped.

## Validation

Built from the current live configuration, read-only. Checks passed for 32 distinct exact CIDs, source band membership, eight unique points per pet, eight distinct destination assignments per ring per phase, all default radii, alternating winding including loop closure, complete variable substitution, loop structure, and GZip/Base64 round trip. There are 256 placement requests across one circuit of every ring.

This full 32-pet choreography has not yet been live-tested. No plugin source code, version, installed configuration, or running macro was changed. The accompanying `.preview.txt` contains every owner's name and command row for review; it is not the import blob.
