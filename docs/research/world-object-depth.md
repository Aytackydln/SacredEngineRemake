# World object depth

## Static sprites

The [verified world render layers](demo-symbols/world-render-layers.md) are
Floor, Shadows, Floor2, Objects, Ceiling (0..4). Queue precedence comes before
the within-queue traversal below. Items.pak +0x2F is zero in every inspected
Demo/Gold descriptor and does not select these queues. Some static shadows
draw inline in Objects, rather than in Shadows; see the linked routing rules.

Static sprite queues preserve the native isometric traversal: increasing X+Y,
then increasing X (decreasing Y), then the Static.pak linked-list order. Sorting
by map Y alone is incorrect. The earlier interpretation on this page matched
one bridge junction but failed the pavilion furniture in the same scene.

At 3230,2547, `Table 8` (Static.pak 759102), its bottle (759103), and mug
(759104) belong to tile 3222,2547. `Pavillon01` (759093) belongs to 3223,2546.
Both tiles have X+Y=5769; the table chain must draw first, so the pavilion's
front pillars and balustrade cover it. `WOOD_BRIDGE1_01` (759079), at
3225,2545, follows on diagonal 5770. No object-specific correction is needed.

Native evidence from the Demo:

- `cWorldView0::drawLine` loads the WLDX static chain at `0059C2E3`
  (`[tile+4]`), follows `Static.pak +0x1F` at `0059D3C3`, and advances
  the tile index with `sub edi, 0x3f` at `0059E27F`. For a 64-wide tile
  array this is `(X+1,Y-1)`, with screen X increasing at `0059E26E`.
- `showWorld` advances its two interleaved diagonal starts by `0x41`
  at `005A1287` and `005A12BE` and emits each line in order.
- `renderObjects` walks each of five queues from begin to end, advancing
  by the 24-byte entry size at `005A080F`; it does not re-sort by map Y.

The archive chain traversal, software rasterizer, embedded sprite composition
and live static-sprite builder preserve diagonal and linked-list order. Engine
now interleaves live sprites and models in that same traversal; the static
chain precedes runtime objects at the same tile. Software model occlusion uses
an exact integer tile/chain key, separately from the physical model depth.
Floating-point tie biases are not a substitute for this order.

Ordinary static sprites disable depth tests and depth writes. This is verified
in Demo `renderObjects` at `0059F194..0059F1A6`: both state requests are zero.
The opposite branch at `0059F157..0059F169` enables both only for descriptor
mask `0x00000200` (low-word GraphicType). For these sprites, the four calls at
`0059F590..0059F5D3` compute depth from each corner's screen Y and the geometric
height from Static.pak +0x33, multiplied by 28. `WorldSpriteDepth` records the
plane conversion; GPU sprites interpolate their depth span instead of using a
constant corner depth. Depth-enabled sector sprites are promoted out of baked
terrain so their authored plane can populate the depth buffer.

Floor/Floor2/Objects/Ceiling routing uses `WorldStaticRenderRouting`, independently
of depth enablement. Ordinary source-alpha and player-fading art stay in their
native queue. The prepared player occlusion map supplies fading without moving
walls after every 3D object. Ceiling art remains after Objects; its depth state
still follows the descriptor's depth bit.

Static.pak offsets 0x23 and 0x25 are the native `anchorOffX` and `anchorOffY`
fields. They describe a multipart object's local tile offsets: subtracting them
from each pavilion or bridge piece yields the common assembly origin. The Demo
world draw path does not read these fields for painter ordering, so they must
not be applied as depth corrections.

## Script-created 3D world models

Doors and containers use their authored integer tile anchor for queue insertion
and their precise or tile-centred position for the visible mesh pivot. These
are different roles: the Waldburg gate is authored at 1711,3403, with a visual
pivot near 1714.431,3403.706. Moving its mesh into a different painter slot or
translating its nearest vertex behind the tile incorrectly clips other doors.
The nearest-vertex translation has been removed.

World-model depth remains physical per vertex, with the original positive
camera Y-Z scale. In Engine's model-camera coordinates:

`depth = 0.5 + (worldVertexY - cameraCenterY - worldVertexZ) / (24 * sqrt(2) * 4096)`

The world transform includes the extracted asset's restored source origin,
facing, projection and precise pivot. The shader's existing local vertex-depth
term is unchanged; its base uses the transformed origin in the same physical
coordinate system. This changes neither the sign nor the scale of vertex-depth
differences. Models retain depth writes and self-occlusion.

Sprite/model occlusion is determined by native submission order, not by comparing
a door's individual vertex depths with constant depths of ordinary wall sprites.
This is what allows the closed gate through the authored seam while the opened
gate becomes hidden by the neighboring wall. The terminal renderer reproduces
this using separate masks for exact painter order and depth-enabled planes.

Characters and their attachments do not use the fixed world-object slot. They
retain projected per-vertex depth and the existing character bias.

### ARGB model cutouts

Model textures with fractional alpha are cutout materials, not a request to
disable depth writes for the whole mesh. `SacredModel.hlsl` discards samples
below alpha `0.10`; every remaining fragment writes depth, including through
the `TransparentModel` pipeline. This is required for mesh self-occlusion.
With depth writes disabled, a later back triangle passes the unchanged depth
test and blends over a front triangle, making the model appear inside out.

The sampled affected textures demonstrate the common trigger:

| Texture | Alpha 0 | Alpha 1-254 | Alpha 255 |
|---|---:|---:|---:|
| `CHEST_01.TGA` | 15,392 | 2,574 | 47,570 |
| `COFFIN_04.TGA` | 15,404 | 2,867 | 47,265 |
| `SHRINE_02.TGA` | 15,448 | 3,050 | 47,038 |

Effect overlays and particles retain their non-writing depth state because
they are composited layers. The model cutout fix is material-wide and has no
model-name, texture-ID, or coordinate exception.
