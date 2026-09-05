# Garrison: Carbuncle Initiator Orbit

Import Garrison-Carbuncle-Initiator-Orbit.macro.blob.txt through MoP's macro import UI and retain the included eight character assignments. The original Unchained macro is unchanged. Requires the pet-placement fix in 1.15.0.322 or later on each participating client.

Summon all eight pets and stop any previous orbit/Unchained loops first. From the character you want as the center, launch the macro with the all-client run control or:

/mop run "Garrison: Carbuncle Initiator Orbit"

The normal IPC RunMacro path supplies $mop_origin from the launching character; do not define $mop_origin in this macro's Variables. The initiator can be outside the eight pet-owner rows, provided they can launch the shared macro and are visible to every pet owner. Launch once from that initiator, not separately on each client. Ensure the same macro configuration and ordering is available on all clients, since the IPC run path identifies macros by index.

Every participating owner targets the initiator once at startup. All subsequent placement commands use the initiator's name as their anchor, so changing a selected target later does not redirect the orbit. The initiator's position and facing are sampled on every command. The orbit follows movement and turns with the initiator; keep them still for initial testing. Owners remain in place. Pet destinations, not player movement, are controlled.

Eight pets receive eight equally spaced angular starting positions. Each advances around the same eight-point ring. The requested path is an octagon approximating a circle; pet travel can cut inside the nominal radius, especially when destinations change before arrival. Phase waits reduce accumulated scheduling drift but do not guarantee synchronized client start times or physical pet separation.

## Variables

- $radius = 2: distance in yalms from initiator to every requested point (4-yalm diameter).
- $diagonal = 1.414214: must be radius / sqrt(2). Plain macro variables are text substitution, not arithmetic: edit BOTH radius and diagonal together. Do not enter an expression such as $radius * 0.7071.
- $step = 0.35: requested seconds per destination, including command execution and the current 0.25-second global delay. Nominal circuit: 8 * step = 2.8 seconds after the 0.5-second startup. Keep step above the global delay with headroom. This changes destination cadence, not native pet movement speed.

Radius presets:

| Radius | Diameter | $diagonal |
|---|---|---|
| 1 | 2 | 0.707107 |
| 2 | 4 | 1.414214 |
| 3 | 6 | 2.121320 |
| 4 | 8 | 2.828427 |

If pets lag behind, cut across the center or fail to reach points, increase step to 0.5 or 0.75, reduce the radius, or bring owners closer to the initiator. Native range, availability and pathing restrictions still apply. A missing/offscreen initiator cannot serve as an anchor.

Stop through the all-client macro Stop control, or /mop stopmacro on each running client. Pets may finish their most recent movement after requests stop.

Validated: exact eight source CIDs, 64 placement requests, every requested point at the default 2-yalm radius, eight distinct destinations per phase, no unresolved variables after substitution, and GZip/Base64 export round trip. Live configuration was read only; this new multi-pet orbit has not been live-tested.
