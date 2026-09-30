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

The archive chain traversal, software rasterizer, software model-occlusion
mask, embedded sprite composition and live static-sprite builder now share
this diagonal order. The software mask retains the last drawn sprite's depth,
matching the always-pass GPU state, rather than the maximum X+Y depth key.
Live verification used the console `inspect 3230 2547` screenshot cheat with
the window unfocused; the table/column and bridge overlaps match the reference.

Opaque and ordinary source-alpha static sprites use an always-pass depth state.
Their sorted submission order therefore determines the visible sprite, while
the winning pixel still writes its X+Y tile depth for later 3D world-model
occlusion. Fractional-alpha edges alone do not move scenery into the post-model
pass; that pass is reserved for Front Layer sprites, particles and player-aware
fading. This also keeps indoor furniture above its floor art. Using map Y as
the shared sprite/model depth instead hides the Waldburg gate behind both wall
layers.

Items.pak `GraphicFlags.FrontLayer` selects the final Ceiling queue. These
sprites draw after ordinary scenery and 3D world models, including when their
texture needs alpha composition. Engine marks the resulting ranges explicitly
so the post-model pass retains always-pass painter depth; the terminal model
occlusion mask treats their covered pixels as final. This fixes the courtyard
building near 3449,2556 and hanging heraldry near 2564,2017 without object IDs.

Static.pak offsets 0x23 and 0x25 are the native `anchorOffX` and `anchorOffY`
fields. They describe a multipart object's local tile offsets: subtracting them
from each pavilion or bridge piece yields the common assembly origin. The Demo
world draw path does not read these fields for painter ordering, so they must
not be applied as depth corrections.

## Script-created 3D world models

Doors and containers use their authored integer tile anchor as the base painter
depth and their precise or tile-centred position as the visible mesh pivot.
Each mesh vertex then contributes its actual model-camera Y-Z depth. Confining
the complete mesh to one narrow painter slot was incorrect: large objects could
not cross the static-sprite slots occupied by their individual pixels.

The model-camera conversion is exact for the terrain projection. One X+Y tile
diagonal is `24 * sqrt(2)` model units, and one normalized painter slot is
`1 / 4096`. The DX12 model shaders therefore add
`(localY - localZ) / (24 * sqrt(2) * 4096)` to the anchor depth. The terminal
rasterizer uses the inverse conversion to compare every mesh pixel with the
static-sprite depth mask. This retains authored whole-object ordering while
allowing the near and far parts of a mesh to occlude independently.

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
