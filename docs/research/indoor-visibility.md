# Indoor visibility: state selection, not an overlay

Verified against installed Sacred Gold `World/Static.pak`, `sectors.wldx`
and the unpacked Gold executable on 2026-09-14.

`Static.pak` records are 0x40 bytes:

| Offset | Meaning |
| --- | --- |
| 0x08, bit 0x08 | Object participates in alternate building states. |
| 0x2B, UInt16 | Native `triggerState`; building exterior = 1, ground floor = 2, next floor = 4, then 8, etc. |
| 0x33, byte | Geometric height layer, multiplied by 28 by the renderer. It is not the visibility state. |

Gold code at `0x62BEEA` tests the alternate-state flag; `0x62BEFE` reads
the object's state at 0x2B, compares it to the parent trigger's state at
`0x62BF0B`, and skips a mismatch at `0x62BF14`. The January 2004 demo
`cWorldView0::drawLine` has the corresponding branch at `0x59D066–0x59D095`.
Gold `0x62E7B7–0x62E7C3` reads byte 0x33 and multiplies it by 28 for geometry.

Example: the building at WLDX origin 2521,2080 has two 14x11 grids.
Static 568246 (`TH01_01`, exterior) has state 1 and height layer 0.
568269 (`TH01 Fußboden EG`) has state 2 and height layer 0.
568285 (`TH01 Fußboden oben 1_01`) has state 4 and height layer 4.
This demonstrates why height cannot select visibility: exterior and ground
floor share height 0. Interior objects and their effects must be selected by
state, while the active building's exterior objects are hidden.

Exterior sprite origins and some wall origins lie in empty indoor-grid border
cells. Neighboring building grid rectangles also overlap. Neither the sprite's
presence cell nor a rectangle alone determines ownership. Outdoor WLDX tiles
with the Indoor bit at 0x1E use the signed offsets at 0x1C/0x1D to resolve the
parent building anchor (`cWorld::getParentObject`). Test that anchor against
the active grid's authored cells. For example, exterior 567963 at 2532,2065
belongs to anchor 2540,2073, while neighboring exterior 568022 at 2532,2072
belongs to 2525,2071 and must remain visible despite overlapping rectangles.

Commit `c8e8f4f` changed the renderer's field from 0x2B to 0x33 while adding
Native names. Keep both interpretations mapped: `SurfaceVisibilityState` aliases
`TriggerState` at 0x2B. Never bake state-dependent objects into permanent sector
textures, since entering or leaving must remove them immediately.

Verification: `_scratch/IndoorProbe` checks real archive objects across three
two-floor buildings. Live captures use the console `screenshot` cheat.

Compiled-script 3D doors and containers do not carry Static.pak's `0x2B` state.
Their placement Z selects the indoor WLDX surface level. Z-zero containers and
other props belong to level one, while Items.pak category `Door` at Z zero is an
unscoped entrance portal used by towns such as Bellevue. Resolve floor membership
with the authored tile anchor and sparse presence cells.
The model is visible only when that exact indoor group is active. This prevents
ground-floor models from leaking through the exterior or an upper floor while
preserving outdoor models whose positions merely overlap a grid rectangle.
