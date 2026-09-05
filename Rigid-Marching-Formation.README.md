# Rigid Marching Formation

Requires the accompanying plugin build because the Lua script uses the new rigid-formation options on `mop.follow_actor`.

Import `Rigid-Marching-Formation.lua.blob.txt` in the Lua Scripts window and `Rigid-Marching-Formation-Run.macro.blob.txt` in the Macro window. The plugin also installs **Rigid Marching Formation** as a packaged default when the upgraded build first loads.

Run **Rigid Marching Formation - Run** from the intended leader. If the initiator has a target, that target leads; otherwise the initiator leads. A targeted puppet is excluded from the grid and remains operator-controlled. An external target is followed but is not counted in the roster.

Defaults are 4 rows, 8 columns, 1.0 yalm horizontal spacing, and 1.5 yalms between rows. Edit the macro variables before launch, or reflow a running formation immediately with:

```text
/cwl2 mopbr /mop setvar -var=$rows=3;$columns=6;$horizontal=1.2;$vertical=1.6
```

To hand leadership to your current target during the run:

```text
/cwl2 mopluavars -var=$anchor="<t>"
```

The sending game client expands the target placeholder before sharing the update. A new puppet leader leaves the grid, and the previous puppet leader returns to its frozen-roster position.

`$preserve_emote=false` cancels looping emotes when a stationary slot resumes movement. Set it to `true` when you intentionally want animations such as `/attention` retained during movement. It can be changed live with the same `setvar`/`mopluavars` mechanism.

The launch roster is frozen, but `$anchor` is live. Excess followers stand down, and the normal Lua run limit is ten minutes.

See `docs/rigid-marching-formation.md` for the turn model, physical limitations, and performance analysis.
