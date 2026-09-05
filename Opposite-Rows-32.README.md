# Opposite Rows (32) — Lua-only

This version has no Participant Formation and no movement-helper macro. Import `Opposite-Rows-32.lua.blob.txt` and `Opposite-Rows-32-Run.macro.blob.txt`.

Run `/mop run "Opposite Rows (32) - Run"` from the first world-visible bottom character. The script freezes the catwalk direction from that character's orientation at launch; the initiator does not turn toward a moving opposite. The launch uses only members of `32 Ordered` that are physically visible to the initiator and requires complete opposite pairs. One pair makes one column; each additional pair adds one column at 1-yalm spacing. The top row is 3 yalms forward from the bottom row.

Lua drives each character directly relative to the initiator, so online clients elsewhere cannot reserve gaps. Both members of a pair must remain within 0.12 yalms of their destinations for 0.6 seconds before either invokes `Target - Opposite`, executes `/facetarget`, and uses `/showleft` on the bottom or `/showright` on the top.

This script needs the plugin build containing the duplicate-condition snapshot fix and the 20-yalm Lua trajectory radius. Experimental PREPARE / READY / GO may remain disabled.