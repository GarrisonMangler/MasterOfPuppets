# Garrison: Carbuncle Unchained (Native Fast)

Import the text from Garrison-Carbuncle-Unchained-Native.macro.blob.txt through the plugin's macro import UI. This creates a separately named replacement; it does not overwrite the original. Preserve the included character assignments if the import UI offers that choice. Stop the old Unchained/orbit loops before starting this one.

Requires the native pet-placement implementation in version 1.15.0.322 or later on every participating client. Summon the pets first. All eight named characters must be nearby, visible, and in valid placement range. No selected target or saved formation is required. Run using the same all-client macro control you use for the original Unchained; running only locally drives only that client's pet.

Each pet keeps its original eight-character route. At any phase, each character is the destination of one pet. Destinations are sampled from live character positions. Full Name@World anchors avoid ambiguous short names. The owner performs /psych motion once; repeated /throw, /beckon and /aback actions are omitted so they do not delay the pet loop. The loop uses absolute phase waits to limit accumulated timing drift, but does not guarantee simultaneous starts across clients.

Variables:
- $step = 0.35: requested seconds per destination, including the configured global delay. Eight stages request a 2.8-second circuit after a 0.5-second opener. The current global delay is 0.25 seconds; slower settings and client lag can lengthen the circuit. Keep $step above the global delay with some headroom.
- $x = 0, $y = 0, $z = 0: preserve the original placement at each character's position. For a one-yalm offset behind each character, set $z = -1; for two yalms, set $z = -2. Offsets rotate with each destination character's facing. Nonzero height does not guarantee valid ground.

This speeds up destination requests, not the pet's native movement speed. A new destination can interrupt travel to the preceding one. If pets cut across the intended route or fail to reach its points, increase $step to 0.5 or 0.75 and/or bring the characters closer together. Native range, availability, and pathing limits still apply.

Stop with the macro UI stop control across all participating clients, or /mop stopmacro on each running client. A stop ends future placement requests; a pet may finish its last issued movement.

Validation: eight exact source CIDs; eight unique anchors per route; all eight phase destinations distinct; 64 placements per circuit; GZip/Base64 round trip and integer CID preservation checked. The original live configuration was read only. This new multi-pet choreography has not yet been live-tested.

## Routes

- Garrison Mangler@Sargatanas: Garrison Mangler -> Boo Mangler -> Giovanni Mangler -> Bertucci Mangler -> Sarducci Mangler -> Achenarius Mangler -> Maltheusen Mangler -> Walter Mangler
- Boo Mangler@Sargatanas: Boo Mangler -> Giovanni Mangler -> Bertucci Mangler -> Sarducci Mangler -> Achenarius Mangler -> Maltheusen Mangler -> Walter Mangler -> Garrison Mangler
- Giovanni Mangler@Sargatanas: Giovanni Mangler -> Bertucci Mangler -> Sarducci Mangler -> Achenarius Mangler -> Maltheusen Mangler -> Walter Mangler -> Garrison Mangler -> Boo Mangler
- Bertucci Mangler@Sargatanas: Bertucci Mangler -> Sarducci Mangler -> Achenarius Mangler -> Maltheusen Mangler -> Walter Mangler -> Garrison Mangler -> Boo Mangler -> Giovanni Mangler
- Sarducci Mangler@Sargatanas: Sarducci Mangler -> Achenarius Mangler -> Maltheusen Mangler -> Walter Mangler -> Garrison Mangler -> Boo Mangler -> Giovanni Mangler -> Bertucci Mangler
- Achenarius Mangler@Sargatanas: Achenarius Mangler -> Maltheusen Mangler -> Walter Mangler -> Garrison Mangler -> Boo Mangler -> Giovanni Mangler -> Bertucci Mangler -> Sarducci Mangler
- Maltheusen Mangler@Sargatanas: Maltheusen Mangler -> Walter Mangler -> Garrison Mangler -> Boo Mangler -> Giovanni Mangler -> Bertucci Mangler -> Sarducci Mangler -> Achenarius Mangler
- Walter Mangler@Sargatanas: Walter Mangler -> Garrison Mangler -> Boo Mangler -> Giovanni Mangler -> Bertucci Mangler -> Sarducci Mangler -> Achenarius Mangler -> Maltheusen Mangler
