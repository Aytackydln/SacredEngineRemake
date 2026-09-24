# World object depth

## Static sprites

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

The archive chain traversal, software rasterizer, software model-occlusion
mask, embedded sprite composition and live static-sprite builder now share
this diagonal order. The software mask retains the last drawn sprite's depth,
matching the always-pass GPU state, rather than the maximum X+Y depth key.
Live verification used the console `inspect 3230 2547` screenshot cheat with
the window unfocused; the table/column and bridge overlaps match the reference.

Opaque and ordinary source-alpha static sprites use an always-pass depth state.
Their sorted submission order therefore determines the visible sprite, while
the winning pixel still writes its X+Y tile depth for later 3D world-model
occlusion. Fractional-alpha edges do not move scenery into the post-model pass;
that pass is reserved for particles and player-aware fading. This also keeps
indoor furniture above its floor art. Using map Y as the shared sprite/model
depth instead hides the Waldburg gate behind both wall layers.

Static.pak offsets 0x23 and 0x25 are the native `anchorOffX` and `anchorOffY`
fields. They describe a multipart object's local tile offsets: subtracting them
from each pavilion or bridge piece yields the common assembly origin. The Demo
world draw path does not read these fields for painter ordering, so they must
not be applied as depth corrections.

## Script-created 3D world models

Doors and containers use their authored integer tile anchor for ordering and
their precise script world position only as the visible mesh pivot. Their tile
depth is the isometric X+Y scanline. A model occupies a narrow interval around
that one painter slot so its own triangles retain their order without crossing
adjacent static-object slots.

The bounded local-depth transform must stay strictly monotonic. Clamping every
vertex to the ends of the interval collapses many barrel and chest vertices to
identical depth and exposes triangle submission order as inside-out faces. The
DX12 model shaders use `h * d / (abs(d) + h)`, where `h` is half a painter slot;
this preserves face order while remaining inside the slot.

Characters and their attachments do not use the fixed world-object slot. They
retain projected per-vertex depth and the existing character bias.
