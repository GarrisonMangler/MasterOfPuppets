# Carbuncle: Shape Tornado

Replacement for live macro #466, Carbuncle: Tornado, read on 2026-08-30.
Preserves all 32 member CIDs and their original four groups of eight. Each group
moves on its own nested shape. This is not a single perimeter containing all 32.
No live configuration or plugin source was modified.

## Replace #466 and rename it

1. Stop the running Tornado macro.
2. Rename existing #466 from Carbuncle: Tornado to Carbuncle: Shape Tornado.
3. Import Carbuncle-Shape-Tornado.import.json using ReplaceExisting, Use CIDs,
   and Backup before import. Do not use OverwriteAll.

Import matches by name. Renaming first lets ReplaceExisting update the same
list position (#466); importing the new name before renaming will not match.
Any other macros that launch the old name need their references updated.

The .macro.blob.txt file is the standard gzip/Base64 clipboard import alternative.
Clipboard import APPENDS a new macro; it does not replace #466. Use the JSON
replacement workflow above to keep the same macro number and avoid duplicates.

## Settings

$shape = circle
$mode = small
$direction = "alternating"

Shapes: circle, square, triangle. Unknown shape values fall back to circle.
Sizes: small, medium, large, lowercase. Unknown size values are not supported.
Directions: alternating, clockwise, counterclockwise (same behavior as #466).
Stop and restart after changing settings.

The source target/initiator fallback and 0.5-second startup are unchanged.
Summon all pets first and keep the owners within placement range.

Example:
/mop run "Carbuncle: Shape Tornado" -var=$shape=triangle;$mode=medium

Circle uses the exact original eight-waypoint paths and timings.
Square uses eight evenly spaced side midpoints/corners. Its half-side length
is the original radius; corners extend to sqrt(2) times that radius.
Triangle is equilateral, centered on the anchor, with one vertex at local Z=-r.
Its circumradius is the original radius. It uses 24 perimeter waypoints so
all eight members per ring visit every corner, starting three waypoints apart.
At a given instant, eight equally spaced pets cannot mark all three triangle
corners simultaneously. Their movement paths still traverse the full triangle.

Triangle subdivides each original phase interval into three centisecond waits,
keeping the exact same requested lap duration after the plugin's rounding.
It issues three times as many pet-placement commands per lap. Actual movement
and corner sharpness depend on frame rate, pet travel speed, and game handling;
these were not tested in game. Square travels farther per lap than circle.

## Customization and verification

The .variables.txt file contains the complete variables block, including
triangle coordinate tables. It is not enough on its own to update old #466:
the command blocks must also be replaced using the full import.
Generated triangle coordinates/timing are independent preset tables; changing
circle radius/timing variables alone does not regenerate triangle values.

Verified the actual variable-expansion methods from Macro.cs in memory without
building or changing the plugin. All 2,304 paths passed offline checks across
32 members, three sizes, four shape inputs (including invalid fallback), three
directions and two anchor cases. Checked complete original circle behavior,
square straight edges, triangle vertices/edges/loop closure, eight distinct
starting positions per group, direction, anchor fallback, all 32 original
assignments, and exact rounded lap durations. Gzip/Base64 round trip passed,
with 76-character MIME wrapping. No in-game execution was performed.
